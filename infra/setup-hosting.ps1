# ------------------------------------------------------------------
# One-time hosting setup for CI/CD. Run AFTER setup-azure.ps1, same -Prefix.
# Creates: App Service (Linux, .NET 10), Azure SQL (free offer),
#          app settings, and GitHub -> Azure OIDC login (no passwords in GitHub).
#
#   ./infra/setup-hosting.ps1 -Prefix bab01 -Location eastus2 -GitHubRepo "babuk123/portfolio-rag-poc"
#
# Cost: App Service F1 = free (sleeps when idle, 60 CPU min/day).
#       Azure SQL free offer = free up to 100,000 vCore-seconds/month, then auto-pauses.
# ------------------------------------------------------------------
param(
    [Parameter(Mandatory = $true)][string]$Prefix,
    [Parameter(Mandatory = $true)][string]$GitHubRepo,   # owner/repo
    [string]$Location = "eastus2",
    [string]$AppSku = "F1"                                # B1 (~$13/month) if F1 is not allowed
)

$ErrorActionPreference = "Stop"

# Windows PowerShell does not stop on failed native commands, so check every az call.
function Assert-Az([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "FAILED: $Step (see the az error above). Fix it and rerun this script." }
}
$rg      = "rg-portfolio-rag-$Prefix"
$plan    = "plan-portfolio-$Prefix"
$webapp  = "app-portfolio-$Prefix"
$sqlSrv  = "sql-portfolio-$Prefix-$Location"   # region in the name avoids clashes with an earlier failed create
$sqlDb   = "PortfolioRagPoc"
$storage = "stportfolio$Prefix"
$search  = "srch-portfolio-$Prefix"
$openai  = "oai-portfolio-$Prefix"
$subId    = az account show --query id -o tsv
Assert-Az '$subId    = az account show --query id -o tsv'
$tenantId = az account show --query tenantId -o tsv
Assert-Az '$tenantId = az account show --query tenantId -o tsv'

# ---------- 1. App Service ----------
Write-Host "1/5 App Service $webapp ($AppSku)" -ForegroundColor Cyan
az appservice plan create -n $plan -g $rg -l $Location --is-linux --sku $AppSku | Out-Null
Assert-Az 'az appservice plan create -n $plan -g $rg -l $Location --is-'
az webapp create -n $webapp -g $rg -p $plan --runtime "DOTNETCORE:10.0" | Out-Null
Assert-Az 'az webapp create -n $webapp -g $rg -p $plan --runtime DOTNE'
az webapp update -n $webapp -g $rg --https-only true | Out-Null
Assert-Az 'az webapp update -n $webapp -g $rg --https-only true | Out-N'
az webapp config set -n $webapp -g $rg --min-tls-version 1.2 --ftps-state Disabled | Out-Null
Assert-Az 'az webapp config set -n $webapp -g $rg --min-tls-version 1.2'

# ---------- 2. Azure SQL (free offer) ----------
Write-Host "2/5 Azure SQL $sqlSrv / $sqlDb" -ForegroundColor Cyan
$sqlUser = "sqladminbab"
$sqlPwd  = "Pa1!" + (-join ((48..57) + (65..90) + (97..122) | Get-Random -Count 20 | ForEach-Object { [char]$_ }))
# Reuse the SQL server/database if a previous run created them (possibly in another region)
$sqlLoc = az sql server list -g $rg --query "[?name=='$sqlSrv'].location" -o tsv
Assert-Az 'az sql server list'
if ($sqlLoc) {
    Write-Host "   SQL server exists in $sqlLoc - reusing it and resetting the admin password" -ForegroundColor DarkGray
    az sql server update -n $sqlSrv -g $rg -p $sqlPwd | Out-Null
    Assert-Az 'az sql server update (reset password)'
} else {
    az sql server create -n $sqlSrv -g $rg -l $Location -u $sqlUser -p $sqlPwd | Out-Null
    Assert-Az 'az sql server create'
}
# 0.0.0.0 = allow Azure services (App Service) only, not the internet
az sql server firewall-rule create -g $rg -s $sqlSrv -n AllowAzureServices --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0 | Out-Null
Assert-Az 'az sql server firewall-rule create'
$dbExists = az sql db list -g $rg -s $sqlSrv --query "[?name=='$sqlDb'].name" -o tsv
Assert-Az 'az sql db list'
if (-not $dbExists) {
    az sql db create -g $rg -s $sqlSrv -n $sqlDb --edition GeneralPurpose --compute-model Serverless `
        --family Gen5 --capacity 2 --use-free-limit --free-limit-exhaustion-behavior AutoPause | Out-Null
    Assert-Az 'az sql db create (free offer)'
}
$sqlConn = "Server=tcp:$sqlSrv.database.windows.net,1433;Database=$sqlDb;User ID=$sqlUser;Password=$sqlPwd;Encrypt=True;TrustServerCertificate=False;Connection Timeout=60;"

# ---------- 3. App settings (read keys from the resources made by setup-azure.ps1) ----------
Write-Host "3/5 App settings" -ForegroundColor Cyan
$openaiKey   = az cognitiveservices account keys list -n $openai -g $rg --query key1 -o tsv
Assert-Az '$openaiKey   = az cognitiveservices account keys list -n $op'
$searchKey   = az search admin-key show --service-name $search -g $rg --query primaryKey -o tsv
Assert-Az '$searchKey   = az search admin-key show --service-name $sear'
$storageConn = az storage account show-connection-string -n $storage -g $rg --query connectionString -o tsv
Assert-Az '$storageConn = az storage account show-connection-string -n '

# Shows up in the app as ConnectionStrings:Sql
az webapp config connection-string set -n $webapp -g $rg --connection-string-type SQLAzure --settings "Sql=$sqlConn" | Out-Null
Assert-Az 'az webapp config connection-string set -n $webapp -g $rg --c'
az webapp config appsettings set -n $webapp -g $rg --settings `
    "AzureOpenAI__Endpoint=https://$openai.openai.azure.com/" `
    "AzureOpenAI__ApiKey=$openaiKey" `
    "AzureOpenAI__ChatDeployment=gpt-4.1-mini" `
    "AzureOpenAI__EmbeddingDeployment=text-embedding-3-small" `
    "AzureSearch__Endpoint=https://$search.search.windows.net" `
    "AzureSearch__ApiKey=$searchKey" `
    "BlobStorage__ConnectionString=$storageConn" `
    "ASPNETCORE_ENVIRONMENT=Production" | Out-Null
Assert-Az 'az webapp config appsettings set -n $webapp -g $rg --setting'

# ---------- 4. GitHub OIDC (federated credential, no client secret) ----------
Write-Host "4/5 GitHub OIDC for $GitHubRepo" -ForegroundColor Cyan
# Reuse the app registration if a previous run already created it
$appId = az ad app list --display-name "gh-portfolio-rag-$Prefix" --query "[0].appId" -o tsv
Assert-Az 'az ad app list'
if (-not $appId) {
    $appId = az ad app create --display-name "gh-portfolio-rag-$Prefix" --query appId -o tsv
    Assert-Az 'az ad app create'
}
$spExists = az ad sp list --filter "appId eq '$appId'" --query "[0].id" -o tsv
if (-not $spExists) {
    az ad sp create --id $appId | Out-Null
    Assert-Az 'az ad sp create'
    Start-Sleep -Seconds 20   # let the service principal replicate
}

$fedFile = Join-Path $env:TEMP "fed-$Prefix.json"
@{
    name      = "github-production"
    issuer    = "https://token.actions.githubusercontent.com"
    subject   = "repo:${GitHubRepo}:environment:production"
    audiences = @("api://AzureADTokenExchange")
} | ConvertTo-Json | ForEach-Object { [System.IO.File]::WriteAllText($fedFile, $_) }   # UTF-8 without BOM (works in PowerShell 5.1 and 7)
$fedExists = az ad app federated-credential list --id $appId --query "[?name=='github-production'].name" -o tsv
if (-not $fedExists) { az ad app federated-credential create --id $appId --parameters "@$fedFile" | Out-Null }
Assert-Az 'az ad app federated-credential create --id $appId --paramete'
Remove-Item $fedFile

# Least privilege: deploy rights on this one web app only
$webappId = az webapp show -n $webapp -g $rg --query id -o tsv
Assert-Az '$webappId = az webapp show -n $webapp -g $rg --query id -o t'
$spObjectId = az ad sp list --filter "appId eq '$appId'" --query "[0].id" -o tsv
Assert-Az 'az ad sp list (object id)'
$hasRole = az role assignment list --assignee-object-id $spObjectId --scope $webappId --query "[?roleDefinitionName=='Website Contributor'].id" -o tsv
if (-not $hasRole) {
    # Object ID + principal type: required when your own access has conditions (ABAC) and avoids replication delays
    az role assignment create --assignee-object-id $spObjectId --assignee-principal-type ServicePrincipal `
        --role "Website Contributor" --scope $webappId | Out-Null
    Assert-Az 'az role assignment create (Website Contributor)'
}

# ---------- 5. GitHub secrets ----------
Write-Host "5/5 GitHub secrets" -ForegroundColor Cyan
if (Get-Command gh -ErrorAction SilentlyContinue) {
    gh secret set AZURE_CLIENT_ID       -R $GitHubRepo -b $appId
    gh secret set AZURE_TENANT_ID       -R $GitHubRepo -b $tenantId
    gh secret set AZURE_SUBSCRIPTION_ID -R $GitHubRepo -b $subId
    gh variable set AZURE_WEBAPP_NAME   -R $GitHubRepo -b $webapp
    Write-Host "GitHub secrets set with gh CLI." -ForegroundColor Green
} else {
    Write-Host "`nAdd these in GitHub > repo > Settings > Secrets and variables > Actions:" -ForegroundColor Yellow
    Write-Host "  Secret   AZURE_CLIENT_ID       = $appId"
    Write-Host "  Secret   AZURE_TENANT_ID       = $tenantId"
    Write-Host "  Secret   AZURE_SUBSCRIPTION_ID = $subId"
    Write-Host "  Variable AZURE_WEBAPP_NAME     = $webapp"
}

Write-Host "`nDone. App URL: https://$webapp.azurewebsites.net" -ForegroundColor Green
Write-Host "Push to main (or run the workflow) to deploy."
