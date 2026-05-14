targetScope = 'resourceGroup'

// ── Parameters ────────────────────────────────────────────────────────────────

param location string
param resourcePrefix string
param environment string
param tags object

@description('Principal ID of the Function App system-assigned managed identity for RBAC assignments')
param functionAppPrincipalId string

@description('Capacity (in thousands of tokens per minute) for model deployments')
param modelCapacity int = 50

// ── Naming ────────────────────────────────────────────────────────────────────
// AIServices (new Foundry) account: globally unique, becomes the subdomain
//   <accountName>.services.ai.azure.com
// Project is a child resource of the AIServices account (no global naming conflict).

var searchName       = '${resourcePrefix}-search-${environment}'
var foundryName      = '${resourcePrefix}-foundry-${environment}'
var projectName      = '${resourcePrefix}-aiproj-${environment}'

// ── Built-in Role Definition IDs ──────────────────────────────────────────────

var cognitiveServicesOpenAiUserRoleId  = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
var azureAiDeveloperRoleId             = '64702f94-c441-49e6-a78b-ef80e0188fee'
var searchIndexDataContributorRoleId   = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
var searchServiceContributorRoleId     = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'

// ── Azure AI Search (Basic — sufficient for dev/small workloads) ───────────────

resource aiSearch 'Microsoft.Search/searchServices@2024-03-01-preview' = {
  name: searchName
  location: location
  tags: tags
  sku: {
    name: 'basic'
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

// ── Azure AI Foundry — AIServices account (modern Foundry) ───────────────────
// Replaces legacy: separate Azure OpenAI account + AI Hub workspace + Hub deps.
// AIServices = Cognitive Services account that hosts OpenAI models AND Foundry
// projects + agents. Endpoint: https://<accountName>.services.ai.azure.com

resource aiFoundry 'Microsoft.CognitiveServices/accounts@2025-04-01-preview' = {
  name: foundryName
  location: location
  tags: tags
  kind: 'AIServices'
  identity: { type: 'SystemAssigned' }
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: foundryName
    publicNetworkAccess: 'Enabled'
    // allowProjectManagement = true is required for the Foundry "projects" sub-resource
    allowProjectManagement: true
  }
}

// GPT-4o — used by all 4 agents in the pipeline
resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: aiFoundry
  name: 'gpt-4o'
  sku: {
    name: 'GlobalStandard'
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
  }
}

// o4-mini — used by Researcher and Writer agents (stronger reasoning model)
resource o4MiniDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: aiFoundry
  name: 'o4-mini'
  dependsOn: [gpt4oDeployment]
  sku: {
    name: 'Standard'
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'o4-mini'
      version: '2025-04-16'
    }
  }
}

// text-embedding-3-large — used by WorkItemIndexer + WikiIndexer (3072 dims)
resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2025-04-01-preview' = {
  parent: aiFoundry
  name: 'text-embedding-3-large'
  dependsOn: [o4MiniDeployment]
  sku: {
    name: 'Standard'
    capacity: modelCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }
}

// Role: Cognitive Services OpenAI User → Function App (call models via managed identity)
resource openAiUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiFoundry
  name: guid(aiFoundry.id, functionAppPrincipalId, cognitiveServicesOpenAiUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Azure AI Foundry Project (child of AIServices account) ────────────────────

resource aiProject 'Microsoft.CognitiveServices/accounts/projects@2025-04-01-preview' = {
  parent: aiFoundry
  name: projectName
  location: location
  tags: tags
  identity: { type: 'SystemAssigned' }
  properties: {
    displayName: '${resourcePrefix} DevOps Impact Analyzer'
    description: 'Foundry project hosting agents for the DevOps Impact Analyzer pipeline'
  }
}

// Role: Azure AI Developer → Function App (use Foundry agents on the project)
resource projectAiDeveloperRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: aiProject
  name: guid(aiProject.id, functionAppPrincipalId, azureAiDeveloperRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', azureAiDeveloperRoleId)
    principalId: functionAppPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output aiSearchEndpoint string      = 'https://${aiSearch.name}.search.windows.net'
output aiSearchName string          = aiSearch.name
// Endpoint of the AIServices account (also serves OpenAI models for direct calls if needed)
output openAiEndpoint string        = aiFoundry.properties.endpoint
output openAiName string            = aiFoundry.name
output foundryAccountName string    = aiFoundry.name
output foundryProjectName string    = aiProject.name
// Foundry project endpoint — consumed by Microsoft.Agents.AI.Foundry / Azure.AI.Projects SDKs
// Format: https://<accountName>.services.ai.azure.com/api/projects/<projectName>
output foundryProjectEndpoint string = 'https://${aiFoundry.name}.services.ai.azure.com/api/projects/${aiProject.name}'
