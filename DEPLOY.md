# CI/CD: build and deploy to Azure with GitHub Actions

```
 git push main ──► GitHub Actions
                    ├─ build:  npm ci → ng build → dotnet build → dotnet publish
                    │          → copy Angular into wwwroot → artifact
                    └─ deploy: OIDC login → App Service deploy → /health smoke test
                                   │
                                   ▼
            App Service (Linux, .NET 10)  ── serves UI + API at one URL
               ├─ Azure SQL (free offer)
               ├─ Blob Storage
               ├─ Azure AI Search
               └─ Azure OpenAI
```

Pull requests run **build only**. Pushes to `main` build **and deploy**.

## Step 1: Azure AI resources (if not done yet)
```powershell
az login
./infra/setup-azure.ps1 -Prefix bab01 -Location eastus2
```

## Step 2: Create the GitHub repo and push the code
1. On github.com, click **New repository** → name `portfolio-rag-poc` → **Private** → don't add a README → **Create**.
2. In PowerShell, in the POC folder:
```powershell
git init
git add .
git commit -m "Portfolio RAG POC"
git branch -M main
git remote add origin https://github.com/babuk123/portfolio-rag-poc.git
git push -u origin main
```
The first run of the workflow will build and then fail at **deploy**, because Azure isn't connected yet. That's expected.

## Step 3: Hosting + GitHub-to-Azure login (one time)
```powershell
./infra/setup-hosting.ps1 -Prefix bab01 -Location eastus2 -GitHubRepo "babuk123/portfolio-rag-poc"
```
This creates the App Service, Azure SQL and app settings, and a login for GitHub that uses **OIDC**: GitHub gets a short-lived token, and no password is stored.

If you have the GitHub CLI (`gh auth login` first), the script also saves the secrets. If not, it prints 4 values. Add them in **GitHub → repo → Settings → Secrets and variables → Actions**:

| Type | Name | Value |
| --- | --- | --- |
| Secret | `AZURE_CLIENT_ID` | printed by the script |
| Secret | `AZURE_TENANT_ID` | printed by the script |
| Secret | `AZURE_SUBSCRIPTION_ID` | printed by the script |
| Variable | `AZURE_WEBAPP_NAME` | `app-portfolio-bab01` |

## Step 4: Create the "production" environment
GitHub → repo → **Settings → Environments → New environment** → name it `production`.
Optional: add yourself as a **required reviewer**, so every deploy waits for your approval. This is a good talking point in interviews.

## Step 5: Deploy
GitHub → **Actions → ci-cd → Run workflow**, or push any commit to `main`.
When it's green, open `https://app-portfolio-bab01.azurewebsites.net`.

The first request after idle can take 20–40 seconds on the free plan, because the app wakes up and Azure SQL resumes.

## Troubleshooting
| Problem | Fix |
| --- | --- |
| `AADSTS70021: No matching federated identity record` | The environment name must be exactly `production`, and the repo name in `-GitHubRepo` must match. |
| `F1 ... not available` or the runtime isn't found | Rerun with `-AppSku B1` (about $13/month), or check `az webapp list-runtimes --os linux`. |
| App shows HTTP 500 | Portal → App Service → **Log stream**. Usually a wrong app setting or SQL firewall. |
| SQL login timeout | The free database was paused; refresh after 30 seconds. |
| Free SQL offer error | Only one free database per subscription. Remove `--use-free-limit` to use the Basic tier (about $5/month). |

## What to say in an interview
- **CI on pull requests, CD on main**, with an environment approval gate.
- **OIDC federated credentials** instead of stored secrets; the deploy identity can only reach one web app (least privilege).
- **Build once, deploy the same artifact**; smoke test after deploy.
- Next steps for production: staging slot with swap, Key Vault references for keys, Managed Identity to OpenAI, Search and SQL, Bicep for infrastructure, and `dotnet test` plus code coverage in CI.

## Clean up
```powershell
az group delete -n rg-portfolio-rag-bab01 --yes --no-wait
az ad app delete --id <AZURE_CLIENT_ID>
```
