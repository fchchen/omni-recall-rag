# Azure Free-Tier Deployment (Manual CD)

This guide is for deploying `omni-recall-rag` with:

- Frontend: Azure Static Web Apps (Free)
- API: Azure App Service (Free F1)
- CD: GitHub Actions manual workflow (`workflow_dispatch`)

## 1) Create Azure resources

1. Create a resource group (example: `rg-omni-recall`).
2. Create an App Service plan on Free tier (`F1`).
3. Create an App Service Web App for the API.
   - Runtime stack should match your target framework (`net10.0` in this repo currently).
4. Create an Azure Static Web App for the frontend (Free plan).
   - Deployment source can be "Other" because this repo deploys using workflow token.

Optional data services for durable storage:

- Azure Cosmos DB (Free Tier account)
- Azure Blob Storage

## 2) Configure API app settings (App Service)

In App Service -> Configuration -> Application settings, add what you use:

- `Gemini__ApiKey`
- `Gemini__Model`
- `GitHubModels__Token`
- `Embeddings__Provider` (`None` or `Gemini`)
- `Storage__Provider` (`InMemory` or `Azure`)
- `AzureStorage__BlobConnectionString` (if `Storage__Provider=Azure`)
- `AzureCosmos__ConnectionString` (if `Storage__Provider=Azure`)
- `Cors__AllowedOriginsCsv=https://<your-static-web-app-domain>`

If you use OCR:

- `Ocr__Provider=AzureDocumentIntelligence`
- `Ocr__Endpoint`
- `Ocr__Key`

## 3) Configure GitHub Secrets

In GitHub -> repo -> Settings -> Secrets and variables -> Actions, add:

- `AZURE_CLIENT_ID`
- `AZURE_TENANT_ID`
- `AZURE_SUBSCRIPTION_ID`
- `AZURE_WEBAPP_RESOURCE_GROUP`
- `AZURE_STATIC_WEB_APPS_API_TOKEN`
- `FRONTEND_API_URL` (recommended)
  - Example: `https://<your-api-app-name>.azurewebsites.net/api`

## 4) Configure Azure OIDC trust (for API deploy)

The API deploy workflow uses `azure/login@v2` with OIDC.

1. Create or reuse an Entra app registration for GitHub deploy automation.
2. In the app registration, add a Federated Credential:
   - Scenario: GitHub Actions deploying Azure resources
   - Org/repo: your GitHub org/repo
   - Branch: `main`
3. Copy IDs to GitHub secrets:
   - Application (client) ID -> `AZURE_CLIENT_ID`
   - Directory (tenant) ID -> `AZURE_TENANT_ID`
   - Subscription ID -> `AZURE_SUBSCRIPTION_ID`
4. Grant RBAC to the service principal:
   - Scope: target resource group (or specific Web App)
   - Role: `Contributor` (or narrower role if preferred)

## 5) Run manual deployment

Go to GitHub -> Actions -> `CD Manual Azure Deploy` -> `Run workflow`:

- `deploy_api`: `true`/`false`
- `deploy_frontend`: `true`/`false`
- `api_app_name`: required when `deploy_api=true`

The workflow:

- Publishes API artifact
- Builds Angular app using `FRONTEND_API_URL`
- Deploys API with Azure CLI (`az webapp deploy`)
- Deploys frontend with `Azure/static-web-apps-deploy`

## 6) Verify deployment

1. Open frontend URL (`https://<your-static-web-app-domain>`).
2. Upload a test `.txt` file.
3. Check:
   - `/documents` shows the file
   - `/recall` returns citations
   - `/chat` returns grounded answer or guard fallback message

## 7) Common issues

- CORS errors in browser:
  - Set `Cors__AllowedOriginsCsv` correctly on API app settings.
- Frontend calls wrong API host:
  - Set `FRONTEND_API_URL` secret to your API URL ending with `/api`.
- API deploy auth failure:
  - Verify OIDC federated credential and RBAC role assignment.
- Free tier throttling/timeouts:
  - Expected under burst traffic; retry and reduce concurrent usage.

## 8) Optional post-deploy smoke test workflow

Use GitHub Actions -> `Smoke Test Azure Deploy`:

- `api_base_url`: `https://<your-api-app-name>.azurewebsites.net`
- `frontend_origin` (optional): `https://<your-static-web-app-domain>`

It validates:

- `GET /health` returns `200`
- `GET /api/documents` returns `200`
- CORS header matches frontend origin (when provided)
