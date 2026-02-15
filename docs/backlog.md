# Backlog

## Sprint 1 - Foundation

1. Create solution structure:
- `src/OmniRecall.Api`
- `src/OmniRecall.App`

2. Azure bootstrap:
- Static Web Apps resource
- Function App resource
- Storage account + Blob container
- Cosmos DB free-tier account

3. CI/CD:
- Build/test workflow
- Deploy Functions + Static Web App

4. AI provider abstraction:
- Add `IAiChatClient` and `IAiEmbeddingClient` interfaces
- Add Gemini implementation (primary)
- Add GitHub Models implementation (fallback)

## Sprint 2 - Ingestion

1. File upload API + Blob storage.
2. Document normalization and chunking.
3. Chunk persistence in Cosmos.
4. Embedding generation endpoint integration.

## Sprint 3 - Query/Chat

1. Query embedding + vector retrieval.
2. Prompt assembly with citations.
3. Chat completion call with streaming.
4. Angular chat UI with source chips.
5. Provider failover path test (`Gemini -> GitHub Models`).

## Sprint 4 - Recall UX

1. Timeline filter (`today|7d|30d|custom`).
2. "What changed?" query preset.
3. Conversation history + bookmarks.

## Sprint 5 - Reliability

1. Rate-limit middleware.
2. Timeout/fallback handling.
3. Basic telemetry (latency, token estimates, error rates).
4. Quota telemetry by provider/model.

## Done Criteria (MVP)

1. Upload + index 20 PDFs end-to-end.
2. 90% of answers include at least one citation.
3. No unhandled 429/timeout returned to UI.
4. Deployed on Azure free-tier services only.
