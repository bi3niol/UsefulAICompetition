targetScope = 'subscription'

// ── Parameters ────────────────────────────────────────────────────────────────

@description('Name of the Resource Group for AI resources (Foundry Hub, AI Search, Azure OpenAI)')
param aiResourceGroupName string = 'rg-ntw-usefulai-ai'

@description('Name of the Resource Group for the Function App and supporting resources')
param appResourceGroupName string = 'rg-ntw-usefulai-app'

@description('Azure region for all resources. Choose a region that supports Azure AI Foundry and Azure OpenAI.')
param location string = 'swedencentral'

@description('Base name prefix for all resources (lowercase, alphanumeric only, max 10 chars)')
@maxLength(10)
param resourcePrefix string = 'bs-useful'

@description('Environment tag used in resource names and tags')
@allowed(['dev', 'test', 'prod'])
param environment string = 'dev'

@description('App Service Plan SKU for the Function App (B1 for dev, P1v3 recommended for prod)')
@allowed(['B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P1v3', 'P2v3', 'P3v3'])
param aspSku string = 'B1'

// ── Tags ──────────────────────────────────────────────────────────────────────

var tags = {
  project: 'DevOpsImpactAnalyzer'
  environment: environment
  managedBy: 'bicep'
}

// ── Resource Groups ───────────────────────────────────────────────────────────

// Append environment suffix so the same prefix can be deployed across dev/test/prod.
var aiRGFullName  = '${aiResourceGroupName}-${environment}'
var appRGFullName = '${appResourceGroupName}-${environment}'

resource aiRG 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: aiRGFullName
  location: location
  tags: tags
}

resource appRG 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: appRGFullName
  location: location
  tags: tags
}

// ── Function App (deployed first — we need its managed identity principalId) ──

module appModule 'modules/functionapp.bicep' = {
  name: 'deploy-functionapp'
  scope: appRG
  params: {
    location: location
    resourcePrefix: resourcePrefix
    environment: environment
    aspSku: aspSku
    tags: tags
  }
}

// ── AI Resources ──────────────────────────────────────────────────────────────

module aiModule 'modules/ai.bicep' = {
  name: 'deploy-ai'
  scope: aiRG
  params: {
    location: location
    resourcePrefix: resourcePrefix
    environment: environment
    tags: tags
    functionAppPrincipalId: appModule.outputs.functionAppPrincipalId
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output functionAppName string       = appModule.outputs.functionAppName
output functionAppUrl string        = appModule.outputs.functionAppUrl
output functionAppPrincipalId string = appModule.outputs.functionAppPrincipalId
output keyVaultName string          = appModule.outputs.keyVaultName
output keyVaultUri string           = appModule.outputs.keyVaultUri
output aiSearchEndpoint string      = aiModule.outputs.aiSearchEndpoint
output aiSearchName string          = aiModule.outputs.aiSearchName
output openAiEndpoint string        = aiModule.outputs.openAiEndpoint
output openAiName string            = aiModule.outputs.openAiName
output foundryAccountName string    = aiModule.outputs.foundryAccountName
output foundryProjectName string    = aiModule.outputs.foundryProjectName
// Foundry project endpoint — consumed by Microsoft.Agents.AI.Foundry / Azure.AI.Projects SDKs
output foundryProjectEndpoint string = aiModule.outputs.foundryProjectEndpoint
