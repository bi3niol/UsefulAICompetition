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
  project: 'WikiDocGenerator'
  environment: environment
  managedBy: 'bicep'
}

// ── Resource Groups ───────────────────────────────────────────────────────────

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

var serviceBusNamespaceName = '${resourcePrefix}-sb-${environment}'

module appModule 'modules/functionapp.bicep' = {
  name: 'deploy-functionapp-wikigen'
  scope: appRG
  params: {
    location: location
    resourcePrefix: resourcePrefix
    environment: environment
    aspSku: aspSku
    tags: tags
    serviceBusNamespaceName: serviceBusNamespaceName
  }
}

// ── Service Bus (queues for the wiki generation pipeline) ─────────────────────

module serviceBusModule 'modules/servicebus.bicep' = {
  name: 'deploy-servicebus-wikigen'
  scope: appRG
  params: {
    location: location
    resourcePrefix: resourcePrefix
    environment: environment
    tags: tags
    functionAppPrincipalId: appModule.outputs.functionAppPrincipalId
    serviceBusNamespaceName: serviceBusNamespaceName
  }
}

// ── AI Resources ──────────────────────────────────────────────────────────────

module aiModule 'modules/ai.bicep' = {
  name: 'deploy-ai-wikigen'
  scope: aiRG
  params: {
    location: location
    resourcePrefix: resourcePrefix
    environment: environment
    tags: tags
    functionAppPrincipalId: appModule.outputs.functionAppPrincipalId
  }
}
