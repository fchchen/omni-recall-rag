using OmniRecall.Api.Contracts;
using OmniRecall.Api.Data.Models;

namespace OmniRecall.Api.Services;

public interface IRecallSearchService
{
    Task<RecallSearchResponseDto> SearchAsync(string query, int topK, CancellationToken cancellationToken = default);
}

public sealed class RecallSearchService(IIngestionStore store, IEmbeddingClient embeddingClient) : IRecallSearchService
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "how", "in", "is",
        "it", "of", "on", "or", "that", "the", "to", "was", "what", "when", "where", "which",
        "who", "why", "with"
    };

    public async Task<RecallSearchResponseDto> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            throw new ArgumentException("Query is required.", nameof(query));

        var queryEmbedding = await embeddingClient.EmbedAsync(query, cancellationToken);
        var candidates = await store.GetRecentChunksAsync(maxCount: 300, cancellationToken);

        var scored = candidates
            .Select(c => new
            {
                Chunk = c,
                Score = ScoreChunk(c, query, queryEmbedding.Vector)
            })
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Chunk.CreatedAtUtc)
            .Take(Math.Max(1, topK))
            .ToList();

        var documents = await store.GetDocumentsByIdsAsync(scored.Select(s => s.Chunk.DocumentId).Distinct().ToArray(), cancellationToken);

        var citations = scored
            .Select(s =>
            {
                documents.TryGetValue(s.Chunk.DocumentId, out var doc);
                return new RecallCitationDto(
                    s.Chunk.DocumentId,
                    doc?.FileName ?? "unknown",
                    s.Chunk.Id,
                    s.Chunk.ChunkIndex,
                    TextSnippetHelper.BuildSnippet(s.Chunk.Content, 180),
                    Math.Round(s.Score, 4),
                    s.Chunk.CreatedAtUtc);
            })
            .ToList();

        return new RecallSearchResponseDto(query, citations);
    }

    private static double ScoreChunk(CosmosChunkRecord chunk, string query, IReadOnlyList<float> queryEmbedding)
    {
        var embeddingScore = CosineSimilarity(queryEmbedding, chunk.Embedding);
        var keywordScore = KeywordScore(query, chunk.Content);
        var recencyScore = RecencyScore(chunk.CreatedAtUtc);

        // Hybrid: embedding dominates when present, keywords/recency stabilize results.
        return (embeddingScore * 0.7d) + (keywordScore * 0.2d) + (recencyScore * 0.1d);
    }

    private static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float>? b)
    {
        if (a.Count == 0 || b is null || b.Count == 0 || a.Count != b.Count)
            return 0d;

        double dot = 0d;
        double normA = 0d;
        double normB = 0d;
        for (var i = 0; i < a.Count; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA <= 0d || normB <= 0d)
            return 0d;

        return dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    private static double KeywordScore(string query, string content)
    {
        if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(content))
            return 0d;

        var rawTerms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => t.ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (rawTerms.Length == 0)
            return 0d;

        var queryTerms = rawTerms
            .Where(t => !StopWords.Contains(t))
            .ToArray();

        if (queryTerms.Length == 0)
            queryTerms = rawTerms;

        var contentLower = content.ToLowerInvariant();
        var matches = queryTerms.Count(t => contentLower.Contains(t, StringComparison.Ordinal));
        return (double)matches / queryTerms.Length;
    }

    private static double RecencyScore(DateTime createdAtUtc)
    {
        var ageDays = Math.Max(0d, (DateTime.UtcNow - createdAtUtc).TotalDays);
        return Math.Exp(-ageDays / 30d);
    }

}
