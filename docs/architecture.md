# Architecture

## System Overview

```text
Angular (Static Web Apps)
    -> Azure Functions API (.NET 9)
        -> Blob Storage (raw files)
        -> Cosmos DB (documents, chunks, chats)
        -> Gemini API (primary chat + embeddings)
        -> GitHub Models API (fallback chat + optional fallback embeddings)
```

## AI Provider Router

Use a provider abstraction in the API:
- `IAiChatClient`
- `IAiEmbeddingClient`

Implementations:
- `GeminiClient` (primary)
- `GitHubModelsClient` (fallback)

Routing policy:
1. Try primary provider/model.
2. On quota/rate-limit/timeout, retry with backoff.
3. If still failing, switch provider.
4. If all model calls fail, return retrieval-only answer with citations.

## Containers

1. `documents`
- `id`
- `sourceType` (`pdf|md|url|text`)
- `title`
- `createdAt`
- `blobPath`
- `tags`

2. `chunks`
- `id`
- `documentId`
- `chunkIndex`
- `content`
- `embedding` (vector)
- `createdAt`

3. `conversations`
- `id`
- `userId`
- `createdAt`
- `lastMessageAt`

4. `messages`
- `id`
- `conversationId`
- `role`
- `content`
- `citations`
- `createdAt`

## Retrieval Flow

1. User asks question.
2. API computes query embedding.
3. Vector search on `chunks`.
4. Re-rank by recency and source quality.
5. Compose prompt with top chunks + citation metadata.
6. Call primary chat provider.
7. If throttled/timed out, call fallback provider.
8. Return streamed answer + citations.

## Rate-Limit Strategy

- Per-model client-side token bucket.
- Backoff: `1s -> 2s -> 4s` (max 3 retries).
- Fallback chain:
  1. Primary provider/model (Gemini)
  2. Secondary provider/model (GitHub Models)
  3. Return retrieval-only answer with citations

## Security

- `GEMINI_API_KEY` and `GITHUB_TOKEN` in Function App settings.
- Signed upload URLs for Blob.
- Entra ID auth optional for v2.
- PII redaction step before model call (optional hardening).
