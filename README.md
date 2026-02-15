# omni-recall-rag

Omni Recall RAG is a **personal AI memory system** for engineers: ingest notes/docs/code snippets/links, then ask high-quality questions and get cited answers with timeline context.

## Why This Project

Most RAG demos are static PDF chat. This project is more interesting:
- Hybrid retrieval: semantic + time-aware recall ("what did I decide last week?")
- Agentic flow without paid Azure AI services
- Production-style architecture on Azure free tier
- Built with a stack that is portfolio-relevant: .NET + Angular + Azure

## Free-Tier Architecture (Reality-Checked)

- Frontend: Angular app on Azure Static Web Apps (Free)
- Backend: .NET 9 Azure Functions (Consumption free grant)
- Storage:
  - Azure Cosmos DB (Free Tier account) for documents/chunks/metadata/chat history
  - Azure Blob Storage for raw files
- AI models:
  - Primary: Google Gemini API (free-tier API key)
  - Fallback: GitHub Models API with GitHub PAT

## Important 2026 Corrections

1. GitHub deprecated the old endpoint `models.inference.ai.azure.com`.
   Use `https://models.github.ai/inference`.
2. DeepSeek-R1 free-tier limits are very low in GitHub Models, so it should not be the default model.
3. Gemini free tier is suitable for personal MVP traffic, but quotas vary by model and can change.
4. "Ask about my uploaded PDF" only works after implementing the full RAG pipeline.

## Recommended Model Strategy

- Chat/default: Gemini fast model (free tier)
- Reasoning mode (optional): Gemini reasoning-capable model for hard queries only
- Embeddings: Gemini embedding model (batch + cache to stay in quota)
- Fallback chat: `deepseek/DeepSeek-V3-0324` on GitHub Models
- Fallback reasoning: `deepseek/DeepSeek-R1-0528` only when explicitly requested

## Core Features (MVP)

1. Ingestion
- Upload PDF/Markdown/Text/URL
- Normalize + chunk + deduplicate
- Store raw file in Blob, metadata/chunks in Cosmos

2. Retrieval
- Hybrid query: vector similarity + recency weighting
- Citation output (document name, chunk id, timestamp)

3. Chat
- Conversation API with streaming
- "Recall mode" prompts:
  - Summary Recall
  - Decision Recall
  - Timeline Recall

4. Guardrails
- Rate limit handling + exponential backoff for both Gemini and GitHub Models
- Prompt/token budget controls
- Timeout + fallback model

## Proposed Repo Layout

```text
src/
  OmniRecall.Api/                 # Azure Functions (.NET 9 isolated)
  OmniRecall.App/                 # Angular frontend
docs/
  architecture.md
  backlog.md
```

## 6-Phase Build Plan

1. Bootstrap
- Create Functions app + Angular app + shared contracts
- Configure CI/CD to Azure Static Web Apps + Functions

2. Data foundation
- Cosmos containers and partition keys
- Blob upload pipeline

3. RAG ingestion
- Chunking, embedding generation, indexing
- Background ingestion queue

4. Query + chat
- Retrieval pipeline + LLM orchestration
- Streaming responses in Angular UI

5. Recall UX
- Timeline view
- Source explorer + citation drilldown

6. Hardening
- Observability, retries, throttling
- Cost and quota dashboards
- Provider failover metrics (Gemini -> GitHub Models)

## Claude + Codex Collaboration Split

- Claude: architecture decisions, prompt/retrieval strategy, test scenarios
- Codex: implementation scaffolding, refactors, endpoint wiring, Angular components

## First Build Target

Ship a single-user MVP:
- 100 uploaded documents
- <4s median response for cached retrieval
- Citations on every answer
- Survives provider throttling without user-visible failures

## Current Code Status

Implemented starter backend with TDD-first slices:
- `src/OmniRecall.Api` minimal API scaffold (`net10.0` in this environment)
- `/api/chat` endpoint with grounded orchestration (`recall -> LLM`) and citation return
- `/api/documents/upload` ingestion endpoint (`.pdf`, `.txt`, `.md`, `.markdown`)
- `/api/documents/{documentId}` metadata retrieval endpoint
- `/api/documents` list endpoint
- `/api/documents/{documentId}/chunks` chunk preview endpoint
- `/api/documents/{documentId}` delete endpoint
- `/api/documents/{documentId}/reindex` embedding reindex endpoint
- `/api/recall/search` hybrid retrieval endpoint (embedding + keyword + recency ranking)
- PDF extraction path:
  - Primary: `PdfPig`
  - Fallback: optional OCR provider (`AzureDocumentIntelligence`)
- Sliding-window chunking + per-chunk embeddings
- Storage abstraction:
  - `InMemory` provider (default)
  - `Azure` provider (`CosmosIngestionStore` + `BlobRawDocumentStore`)
- Citation-aware chat post-processing:
  - strips unsupported markers like `[99]`
  - keeps only valid cited snippets when markers are present
- Angular frontend scaffold in `src/OmniRecall.App`:
  - `/chat`, `/documents`, `/recall`, `/eval`, `/upload`
  - API client wired to backend endpoints
  - Eval harness page with editable local test cases + pass/fail run table
- Documents UI now shows reindex metrics:
  - `embedded`, `rate-limited`, `empty`, `failed`
- Tests passing: router, orchestration, chat endpoints, chunker, ingestion service, document endpoints (including pdf/list/chunks/delete/reindex), recall search, provider registration, embedding client/model fallback, pdf extraction fallback, optional azure smoke test (`33` passing tests)

Chat request/response (current):
- Request: `{"prompt":"...","topK":5}`
- Response: `answer`, `provider`, `model`, `citations[]`

## Run Locally

```bash
dotnet test OmniRecall.sln
dotnet run --project src/OmniRecall.Api
cd src/OmniRecall.App && npm start
```

Frontend dev proxy:
- `src/OmniRecall.App/proxy.conf.json` forwards `/api` to `http://localhost:5169`

Configure keys in `src/OmniRecall.Api/appsettings.json` or environment variables:
- `Gemini__ApiKey`
- `Gemini__Model`
- `Gemini__FallbackModels__0` (optional)
- `Gemini__FallbackModels__1` (optional)
- `Gemini__FallbackModels__2` (optional)
- `Gemini__FallbackModels__3` (optional)
- `GitHubModels__Token`
- `AiRouting__RetryBaseDelayMs` (optional retry backoff base)
- `AiRouting__RetryMaxDelayMs` (optional retry backoff cap)
- `ChatQuality__EnableRecallOnlyFallbackOnProviderFailure` (`true` for free-tier mode)
- `Ingestion__MaxUploadBytes` (max accepted upload size)
- `Storage__Provider` (`InMemory` or `Azure`)
- `Embeddings__Provider` (`None` or `Gemini`)
- `Ocr__Provider` (`None` or `AzureDocumentIntelligence`)
- `AzureStorage__BlobConnectionString`
- `AzureCosmos__ConnectionString`
- `Ocr__Endpoint` (when OCR provider is Azure)
- `Ocr__Key` (when OCR provider is Azure)
- `Cors__AllowedOriginsCsv` (for deployed frontend origin(s), comma-separated)

Optional Azure integration smoke test env vars:
- `AZURE_COSMOS_CONNECTION_STRING`
- `AZURE_COSMOS_DATABASE`
- `AZURE_COSMOS_DOCS_CONTAINER`
- `AZURE_COSMOS_CHUNKS_CONTAINER`
- `AZURE_BLOB_CONNECTION_STRING`
- `AZURE_BLOB_CONTAINER`

## GitHub CI/CD

Two GitHub Actions workflows are included:

- CI: `.github/workflows/ci.yml`
  - Runs on push/PR to `main`
  - Runs `.NET` tests and Angular build
- CD (manual): `.github/workflows/cd-manual.yml`
  - Triggered manually with `workflow_dispatch`
  - Optional API deploy
  - Optional frontend deploy

### Required GitHub Secrets

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_WEBAPP_RESOURCE_GROUP`
  - Used by OIDC login and `az webapp deploy` for API deployment
- `AZURE_STATIC_WEB_APPS_API_TOKEN`
  - Deployment token for your Azure Static Web App
- `FRONTEND_API_URL` (recommended)
  - Example: `https://<your-api-app>.azurewebsites.net/api`
  - If not set, frontend defaults to `/api`

### Manual CD Usage

1. Go to **Actions** -> **CD Manual Azure Deploy**.
2. Click **Run workflow**.
3. Choose toggles:
   - `deploy_api`: true/false
   - `deploy_frontend`: true/false
4. If `deploy_api=true`, set `api_app_name` to your Azure app name.
5. In Azure App Service configuration, set:
   - `Cors__AllowedOriginsCsv=https://<your-static-web-app-domain>`

This gives you CI on every change and manual, controlled Azure deployments for free-tier usage.
