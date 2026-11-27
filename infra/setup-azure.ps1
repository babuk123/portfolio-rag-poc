# ------------------------------------------------------------------
# Creates the Azure resources for the Portfolio RAG POC and stores
# the keys in .NET user-secrets (never in appsettings.json).
#
# Prereqs: Azure CLI (az), .NET 10 SDK. Run from the repo root:
#   az login
#   ./infra/setup-azure.ps1 -Prefix bab01
#
# Cost: Search "free" tier = $0 (one per subscription). Storage and
# Azure OpenAI are pay-per-use (a few cents for this POC).
# Delete everything afterwards:  az group delete -n <rg> --yes
# ------------------------------------------------------------------
param(
    [Parameter(Mandatory = $true)][string]$Prefix,   # short, lowercase letters/digits, e.g. bab01
    [string]$Location = "eastus",
    [string]$SearchSku = "free"                      # use "basic" for the semantic ranker
)

$ErrorActionPreference = "Stop"

# Windows PowerShell does not stop on failed native commands, so check every az call.
function Assert-Az([string]$Step) {
    if ($LASTEXITCODE -ne 0) { throw "FAILED: $Step (see the az error above). Fix it and rerun this script." }
}
$rg      = "rg-portfolio-rag-$Prefix"
$storage = "stportfolio$Prefix"
$search  = "srch-portfolio-$Prefix"
$openai  = "oai-portfolio-$Prefix"
$api     = Join-Path $PSScriptRoot "..\src\PortfolioRag.Api"

Write-Host "1/5 Resource group $rg" -ForegroundColor Cyan
az group create -n $rg -l $Location | Out-Null
Assert-Az 'az group create -n $rg -l $Location | Out-Null'

Write-Host "2/5 Storage account $storage" -ForegroundColor Cyan
az storage account create -n $storage -g $rg -l $Location --sku Standard_LRS --kind StorageV2 `
    --allow-blob-public-access false --min-tls-version TLS1_2 | Out-Null
Assert-Az 'az storage account create -n $storage -g $rg -l $Location --'
$storageConn = az storage account show-connection-string -n $storage -g $rg --query connectionString -o tsv
Assert-Az '$storageConn = az storage account show-connection-string -n '

Write-Host "3/5 Azure AI Search $search ($SearchSku)" -ForegroundColor Cyan
az search service create -n $search -g $rg -l $Location --sku $SearchSku | Out-Null
Assert-Az 'az search service create -n $search -g $rg -l $Location --sk'
if ($SearchSku -ne "free") {
    # Semantic ranker free plan (1,000 queries/month) for the relevance threshold
    az search service update -n $search -g $rg --semantic-search free | Out-Null
    Assert-Az 'az search service update -n $search -g $rg --semantic-search'
}
$searchKey = az search admin-key show --service-name $search -g $rg --query primaryKey -o tsv
Assert-Az '$searchKey = az search admin-key show --service-name $search'

Write-Host "4/5 Azure OpenAI $openai + model deployments" -ForegroundColor Cyan
az cognitiveservices account create -n $openai -g $rg -l $Location --kind OpenAI --sku S0 `
    --custom-domain $openai --yes | Out-Null
Assert-Az 'az cognitiveservices account create -n $openai -g $rg -l $Lo'
$openaiKey = az cognitiveservices account keys list -n $openai -g $rg --query key1 -o tsv
Assert-Az '$openaiKey = az cognitiveservices account keys list -n $open'

# Model versions change over time. If a command fails, check the available versions with:
#   az cognitiveservices account list-models -n $openai -g $rg -o table
az cognitiveservices account deployment create -n $openai -g $rg --deployment-name gpt-4.1-mini `
    --model-name gpt-4.1-mini --model-version "2025-04-14" --model-format OpenAI `
    --sku-name Standard --sku-capacity 10 | Out-Null
Assert-Az 'az cognitiveservices account deployment create -n $openai -g'
az cognitiveservices account deployment create -n $openai -g $rg --deployment-name text-embedding-3-small `
    --model-name text-embedding-3-small --model-version "1" --model-format OpenAI `
    --sku-name Standard --sku-capacity 10 | Out-Null
Assert-Az 'az cognitiveservices account deployment create -n $openai -g'

Write-Host "5/5 Saving settings to user-secrets" -ForegroundColor Cyan
Push-Location $api
dotnet user-secrets set "AzureOpenAI:Endpoint" "https://$openai.openai.azure.com/" | Out-Null
dotnet user-secrets set "AzureOpenAI:ApiKey" $openaiKey | Out-Null
dotnet user-secrets set "AzureSearch:Endpoint" "https://$search.search.windows.net" | Out-Null
dotnet user-secrets set "AzureSearch:ApiKey" $searchKey | Out-Null
dotnet user-secrets set "BlobStorage:ConnectionString" $storageConn | Out-Null
if ($SearchSku -ne "free") { dotnet user-secrets set "AzureSearch:UseSemanticRanker" "true" | Out-Null }
Pop-Location

Write-Host "`nDone. Resource group: $rg" -ForegroundColor Green
Write-Host "Next: cd src/PortfolioRag.Api; dotnet run"
