<#
.SYNOPSIS
    Deploys the DevOps Impact Analyzer infrastructure to Azure.

.DESCRIPTION
    Creates two resource groups (AI + App) and deploys all required Azure resources
    using Bicep templates at subscription scope.

.PARAMETER SubscriptionId
    Azure Subscription ID to deploy into.

.PARAMETER AiResourceGroupName
    Name of the Resource Group for AI resources (Foundry Hub, AI Search, Azure OpenAI).
    Default: rg-ntw-usefulai-ai

.PARAMETER AppResourceGroupName
    Name of the Resource Group for the Function App and supporting resources.
    Default: rg-ntw-usefulai-app

.PARAMETER Location
    Azure region for all resources. Must support Azure AI Foundry and Azure OpenAI.
    Default: eastus2

.PARAMETER ResourcePrefix
    Lowercase alphanumeric prefix (max 10 chars) used in all resource names.
    Default: netwise

.PARAMETER Environment
    Environment tag: dev | test | prod.  Default: dev

.PARAMETER AspSku
    App Service Plan SKU. Use B1 for dev, P1v3 for production AI workloads.
    Default: B1

.EXAMPLE
    .\deploy.ps1 -SubscriptionId "00000000-0000-0000-0000-000000000000"

.EXAMPLE
    .\deploy.ps1 -SubscriptionId "..." -Environment prod -Location swedencentral -AspSku P1v3
#>

param(
    [Parameter(Mandatory)]
    [string] $SubscriptionId,

    [string] $AiResourceGroupName  = 'rg-ntw-usefulai-ai',
    [string] $AppResourceGroupName = 'rg-ntw-usefulai-app',
    [string] $Location             = 'swedencentral',
    [string] $ResourcePrefix       = 'bs-useful',

    [ValidateSet('dev', 'test', 'prod')]
    [string] $Environment = 'dev',

    [ValidateSet('B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P1v3', 'P2v3', 'P3v3')]
    [string] $AspSku = 'B1',

    [string] $DeploymentName = "deploy-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$bicepFile = Join-Path $PSScriptRoot 'main.bicep'

# Bicep appends the environment suffix to RG names so the same prefix can be deployed across dev/test/prod.
$AiResourceGroupNameFull  = "$AiResourceGroupName-$Environment"
$AppResourceGroupNameFull = "$AppResourceGroupName-$Environment"

Write-Host ''
Write-Host '══════════════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host '  DevOps Impact Analyzer — Infrastructure Deployment'      -ForegroundColor Cyan
Write-Host '══════════════════════════════════════════════════════════' -ForegroundColor Cyan
Write-Host ''
Write-Host "  Subscription : $SubscriptionId"
Write-Host "  AI RG        : $AiResourceGroupNameFull"
Write-Host "  App RG       : $AppResourceGroupNameFull"
Write-Host "  Location     : $Location"
Write-Host "  Prefix       : $ResourcePrefix"
Write-Host "  Environment  : $Environment"
Write-Host "  ASP SKU      : $AspSku"
Write-Host "  Deployment   : $DeploymentName"
Write-Host ''

# ── Set subscription ──────────────────────────────────────────────────────────

Write-Host '▶ Setting active subscription...' -ForegroundColor Cyan
az account set --subscription $SubscriptionId
if ($LASTEXITCODE -ne 0) { Write-Error '❌ Failed to set subscription.'; exit 1 }

# ── Deploy ────────────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '▶ Deploying infrastructure (this may take 10–15 minutes)...' -ForegroundColor Cyan
$resultJson = az deployment sub create `
    --name $DeploymentName `
    --location $Location `
    --template-file $bicepFile `
    --parameters `
        aiResourceGroupName=$AiResourceGroupName `
        appResourceGroupName=$AppResourceGroupName `
        location=$Location `
        resourcePrefix=$ResourcePrefix `
        environment=$Environment `
        aspSku=$AspSku

if ($LASTEXITCODE -ne 0) { Write-Error '❌ Deployment failed.'; exit 1 }

$result  = $resultJson | ConvertFrom-Json
$outputs = $result.properties.outputs

# ── Print results ─────────────────────────────────────────────────────────────

Write-Host ''
Write-Host '✅ Deployment complete!' -ForegroundColor Green
Write-Host ''
Write-Host '── Outputs ──────────────────────────────────────────────────────────' -ForegroundColor Yellow
Write-Host "  Function App Name  : $($outputs.functionAppName.value)"
Write-Host "  Function App URL   : $($outputs.functionAppUrl.value)"
Write-Host "  Key Vault URI      : $($outputs.keyVaultUri.value)"
Write-Host "  AI Search Endpoint : $($outputs.aiSearchEndpoint.value)"
Write-Host "  Azure OpenAI EP    : $($outputs.openAiEndpoint.value)"
Write-Host "  Foundry Hub        : $($outputs.foundryHubName.value)"
Write-Host "  Foundry Project    : $($outputs.foundryProjectName.value)"
Write-Host ''
Write-Host '⚠️  Foundry Project Endpoint (verify in Azure AI Foundry portal → Project overview → API endpoint):' -ForegroundColor Yellow
Write-Host "  $($outputs.foundryProjectEndpoint.value)"
Write-Host ''
Write-Host '── Next steps ───────────────────────────────────────────────────────' -ForegroundColor Yellow
Write-Host '  1. Add secrets to Key Vault:'
Write-Host "       az keyvault secret set --vault-name $($outputs.keyVaultName.value) --name AzureDevOps--PersonalAccessToken --value <PAT>"
Write-Host "       az keyvault secret set --vault-name $($outputs.keyVaultName.value) --name Foundry--Endpoint --value <endpoint>"
Write-Host ''
Write-Host '  2. Configure Function App settings with Key Vault references:'
Write-Host "       az functionapp config appsettings set --name $($outputs.functionAppName.value) \\"
Write-Host "         --resource-group $AppResourceGroupNameFull \\"
Write-Host "         --settings ""Foundry__Endpoint=@Microsoft.KeyVault(SecretUri=$($outputs.keyVaultUri.value)secrets/Foundry--Endpoint)"""
Write-Host ''
Write-Host '  3. Deploy the function app code:'
Write-Host "       func azure functionapp publish $($outputs.functionAppName.value)"
Write-Host ''
Write-Host '  4. Configure Azure DevOps Service Hook (Webhook) to:'
Write-Host "       $($outputs.functionAppUrl.value)/api/WorkItemWebhook"
Write-Host ''
