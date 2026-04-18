targetScope = 'resourceGroup'

// ── Parameters ────────────────────────────────────────────────────────────────

param location string
param resourcePrefix string
param environment string
param tags object

@description('Principal ID of the Function App system-assigned managed identity for RBAC assignments')
param functionAppPrincipalId string

// ── Naming ────────────────────────────────────────────────────────────────────
// Storage account: lowercase alphanumeric only, max 24 chars

var cleanPrefix      = toLower(replace(resourcePrefix, '-', ''))
var searchName       = '${resourcePrefix}-search-${environment}'
var openAiName       = '${resourcePrefix}-openai-${environment}'
var hubName          = '${resourcePrefix}-aihub-${environment}'
var projectName      = '${resourcePrefix}-aiproj-${environment}'
var aiStorageName    = take('${cleanPrefix}aist${uniqueString(resourceGroup().id)}', 24)
var aiKeyVaultName   = take('${cleanPrefix}aikv${uniqueString(resourceGroup().id)}', 24)

// ── Built-in Role Definition IDs ──────────────────────────────────────────────

var cognitiveServicesOpenAiUserRoleId  = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
var searchIndexDataContributorRoleId   = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
var searchServiceContributorRoleId     = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'

// ── Azure AI Search (Standard — required for semantic reranker + vector HNSW) ─

resource aiSearch 'Microsoft.Search/searchServices@2024-03-01-preview' = {
  name: searchName
  location: location
  tags: tags
  sku: {
    name: 'standard'
  }
  properties: {
    replicaCount: 1
    partitionCount: 1
    hostingMode: 'default'
    publicNetworkAccess: 'enabled'
    semanticSearch: 'standard'
  }
}

// Role: Search Index Data Contributor → Function App (upload + query documents)
resource searchDataContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiSearch
  name: guid(aiSearch.id, functionAppPrincipalId, searchIndexDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributorRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Role: Search Service Contributor → Function App (create/update index definitions)
resource searchServiceContributorRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiSearch
  name: guid(aiSearch.id, functionAppPrincipalId, searchServiceContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Azure OpenAI ──────────────────────────────────────────────────────────────

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: 'Enabled'
  }
}

// GPT-4o — used by all 4 agents in the pipeline
resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'gpt-4o'
  sku: {
    name: 'GlobalStandard'
    capacity: 10 // 10K tokens/min — increase for prod
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
  }
}

// text-embedding-3-large — used by WorkItemIndexer + WikiIndexer (3072 dims)
resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'text-embedding-3-large'
  dependsOn: [gpt4oDeployment]
  sku: {
    name: 'Standard'
    capacity: 10 // 10K tokens/min — increase if indexing is slow
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }
}

// Role: Cognitive Services OpenAI User → Function App (call OpenAI via managed identity)
resource openAiUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: openAi
  name: guid(openAi.id, functionAppPrincipalId, cognitiveServicesOpenAiUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Storage Account for AI Hub (required dependency) ──────────────────────────

resource aiStorage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: aiStorageName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
  }
}

// ── Key Vault for AI Hub (required dependency) ────────────────────────────────

resource aiKeyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: aiKeyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ── Azure AI Hub ──────────────────────────────────────────────────────────────

resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: hubName
  location: location
  tags: tags
  kind: 'Hub'
  identity: { type: 'SystemAssigned' }
  properties: {
    friendlyName: '${resourcePrefix} AI Hub'
    storageAccount: aiStorage.id
    keyVault: aiKeyVault.id
    publicNetworkAccess: 'Enabled'
  }
}

// ── Connection: AI Hub → Azure OpenAI (visible in Foundry portal) ─────────────

resource openAiConnection 'Microsoft.MachineLearningServices/workspaces/connections@2024-10-01' = {
  parent: aiHub
  name: 'openai-connection'
  properties: {
    category: 'AzureOpenAI'
    target: openAi.properties.endpoint
    authType: 'ApiKey'
    isSharedToAll: true
    credentials: {
      key: openAi.listKeys().key1
    }
    metadata: {
      ApiVersion: '2024-05-01-preview'
      ApiType: 'azure'
      ResourceId: openAi.id
    }
  }
}

// ── Connection: AI Hub → Azure AI Search (visible in Foundry portal) ──────────

resource searchConnection 'Microsoft.MachineLearningServices/workspaces/connections@2024-10-01' = {
  parent: aiHub
  name: 'search-connection'
  properties: {
    category: 'CognitiveSearch'
    target: 'https://${aiSearch.name}.search.windows.net'
    authType: 'ApiKey'
    isSharedToAll: true
    credentials: {
      key: aiSearch.listAdminKeys().primaryKey
    }
    metadata: {
      ApiVersion: '2024-05-01-preview'
      ResourceId: aiSearch.id
    }
  }
}

// ── Azure AI Foundry Project ───────────────────────────────────────────────────

resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-10-01' = {
  name: projectName
  location: location
  tags: tags
  kind: 'Project'
  identity: { type: 'SystemAssigned' }
  properties: {
    friendlyName: '${resourcePrefix} DevOps Impact Analyzer'
    hubResourceId: aiHub.id
    publicNetworkAccess: 'Enabled'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output aiSearchEndpoint string = 'https://${aiSearch.name}.search.windows.net'
output aiSearchName string     = aiSearch.name
output openAiEndpoint string   = openAi.properties.endpoint
output openAiName string       = openAi.name
output hubName string          = aiHub.name
output projectName string      = aiProject.name
// Format: https://<hub>.services.ai.azure.com/api/projects/<project>
// Verify the exact URL in Azure AI Foundry portal → Project overview → API endpoint
output foundryProjectEndpoint string = 'https://${aiHub.name}.services.ai.azure.com/api/projects/${aiProject.name}'
