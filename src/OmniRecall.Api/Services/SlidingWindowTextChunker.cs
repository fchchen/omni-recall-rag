namespace OmniRecall.Api.Services;

public sealed class SlidingWindowTextChunker : ITextChunker
{
    public IReadOnlyList<string> Chunk(string text, int chunkSizeWords, int chunkOverlapWords)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var words = text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return [];

        var chunkSize = Math.Max(1, chunkSizeWords);
        var overlap = Math.Max(0, Math.Min(chunkOverlapWords, chunkSize - 1));
        var step = Math.Max(1, chunkSize - overlap);

        var chunks = new List<string>();
        for (var i = 0; i < words.Length; i += step)
        {
            var end = Math.Min(i + chunkSize, words.Length);
            var length = end - i;
            if (length <= 0)
                break;

            var slice = new string[length];
            Array.Copy(words, i, slice, 0, length);
            chunks.Add(string.Join(' ', slice));

            if (i + chunkSize >= words.Length)
                break;
        }

        return chunks;
    }
}
