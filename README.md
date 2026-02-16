# Omni Recall RAG

Personal AI memory system: ingest documents, ask questions, get cited answers grounded in your own content.

## Demo

### Frontend Navigation

https://github.com/user-attachments/assets/frontend-navigation.webm

<video src="docs/frontend-navigation.webm" width="100%" controls></video>

### Chat & Recall in Action

https://github.com/user-attachments/assets/demo-chat-recall.webm

<video src="docs/demo-chat-recall.webm" width="100%" controls></video>

## Features

- **Document Ingestion** — Upload PDF, TXT, Markdown files; automatic chunking, deduplication (SHA-256), and embedding generation
- **Hybrid Recall Search** — Combines vector similarity (70%), keyword matching (20%), and recency (10%) for ranked citations
- **Grounded Chat** — LLM answers cite specific document chunks with marker references `[1]`, `[2]`, etc.
- **Provider Failover** — Gemini (primary) with exponential backoff retry, falls to GitHub Models, then recall-only fallback
- **Eval Harness** — Browser-based test runner with editable cases and pass/fail tracking
- **Azure Free Tier** — Runs entirely on free-tier Azure services

## Architecture

| Layer | Technology |
|-------|-----------|
| Frontend | Angular 17 on Azure Static Web Apps (Free) |
| API | .NET 10 Minimal APIs on Azure App Service (Free F1) |
| Documents | Azure Cosmos DB (Free Tier, shared database) |
| Files | Azure Blob Storage |
| Chat AI | Gemini 2.5 Flash (primary), GitHub Models DeepSeek-V3 (fallback) |
| Embeddings | Gemini Embedding 001 |
| OCR | Azure Document Intelligence (optional) |
| CI/CD | GitHub Actions (CI on push, manual CD) |

## API Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/documents/upload` | Upload and ingest a document |
| GET | `/api/documents` | List all documents |
| GET | `/api/documents/{id}` | Get document details |
| GET | `/api/documents/{id}/chunks` | Preview document chunks |
| DELETE | `/api/documents/{id}` | Delete a document |
| POST | `/api/documents/{id}/reindex` | Re-embed document chunks |
| POST | `/api/recall/search` | Hybrid retrieval search |
| POST | `/api/chat` | Grounded chat with citations |
| GET | `/health` | Health check with dependency probes |

## Run Locally

```bash
dotnet test OmniRecall.sln
dotnet run --project src/OmniRecall.Api
cd src/OmniRecall.App && npm start
```

- Swagger UI: `http://localhost:5169/swagger`
- Health: `http://localhost:5169/health`
- Frontend proxies `/api` to the backend via `proxy.conf.json`

## Configuration

Set via `appsettings.json`, environment variables, or Azure App Settings (`__` = section separator):

| Key | Description |
|-----|-------------|
| `Gemini__ApiKey` | Gemini API key |
| `Gemini__Model` | Chat model (default: `gemini-2.5-flash`) |
| `GitHubModels__Token` | GitHub PAT for fallback models |
| `Storage__Provider` | `InMemory` or `Azure` |
| `Embeddings__Provider` | `None` or `Gemini` |
| `AzureCosmos__ConnectionString` | Cosmos DB connection string |
| `AzureStorage__BlobConnectionString` | Blob Storage connection string |
| `Cors__AllowedOriginsCsv` | Allowed frontend origins |
| `Ingestion__MaxUploadBytes` | Max upload size (default: 51200) |
| `Ingestion__EmbeddingParallelism` | Concurrent embedding calls (default: 2) |
| `Ocr__Provider` | `None` or `AzureDocumentIntelligence` |
| `Health__ProbeExternalAi` | `true` to probe Gemini/GitHub in health check |

## Deploy to Azure

See [docs/deploy-azure-free-tier.md](docs/deploy-azure-free-tier.md) for the full runbook.

### GitHub Actions Workflows

- **CI** (`.github/workflows/ci.yml`) — Runs tests + Angular build on push/PR to `main`
- **CD** (`.github/workflows/cd-manual.yml`) — Manual deploy: API to App Service, frontend to Static Web Apps
- **Smoke Test** (`.github/workflows/smoke-test-azure.yml`) — Validates `/health`, `/api/documents`, and CORS

### Required GitHub Secrets

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC app registration |
| `AZURE_TENANT_ID` | Entra tenant |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription |
| `AZURE_WEBAPP_RESOURCE_GROUP` | Resource group for API deploy |
| `AZURE_STATIC_WEB_APPS_API_TOKEN` | Static Web App deploy token |
| `FRONTEND_API_URL` | API URL baked into frontend build |

## Tests

52 passing tests covering: chat orchestration, recall search, document ingestion, chunking, embedding client, provider routing, PDF extraction, and endpoint contracts.

```bash
dotnet test OmniRecall.sln
```
