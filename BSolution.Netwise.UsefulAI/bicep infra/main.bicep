@description('Location for all resources. Defaults to resource group location.')
param location string = resourceGroup().location

@description('Short prefix used in all resource names.')
@maxLength(12)
param prefix string = 'devops-ai'

@description('Azure DevOps organization name (e.g. "myorg").')
param devOpsOrg string

@description('Azure DevOps project name.')
param devOpsProject string

@description('Azure DevOps Personal Access Token — stored in Key Vault.')
@secure()
param devOpsPat string

// ── Naming ───────────────────────────────────────────────────────────────────

var suffix        = uniqueString(resourceGroup().id)
var identityName  = 'id-${prefix}'
var kvName        = 'kv-${prefix}-${suffix}'
var openAIName    = 'oai-${prefix}-${suffix}'
var searchName    = 'srch-${prefix}-${suffix}'
var storageName   = 'st${replace(prefix, '-', '')}${suffix}'
var logName       = 'log-${prefix}'
var appInsName    = 'appi-${prefix}'
var planName      = 'plan-${prefix}'
var funcName      = 'func-${prefix}-${suffix}'
var aiHubName     = 'aihub-${prefix}'
var aiProjectName = 'aiproj-${prefix}'

// ── Managed Identity ─────────────────────────────────────────────────────────

resource identity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: identityName
  location: location
}

// ── Observability ─────────────────────────────────────────────────────────────

resource logWorkspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logName
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logWorkspace.id
  }
}

// ── Storage Account (shared: Function App + AI Hub) ───────────────────────────

resource storage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageName
  location: location
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    allowBlobPublicAccess: false
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
  }
}

// ── Key Vault ─────────────────────────────────────────────────────────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: kvName
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
  }
}

// ── Azure OpenAI ──────────────────────────────────────────────────────────────

resource openAI 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: openAIName
  location: location
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: {
    customSubDomainName: openAIName
    publicNetworkAccess: 'Enabled'
  }
}

resource gpt4oDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' = {
  parent: openAI
  name: 'gpt-4o'
  sku: { name: 'Standard', capacity: 10 }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4o'
      version: '2024-11-20'
    }
    versionUpgradeOption: 'OnceNewDefaultVersionAvailable'
  }
}

// Deployments muszą być sekwencyjne — quota OpenAI nie pozwala na równoległe
resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' = {
  parent: openAI
  name: 'text-embedding-3-large'
  sku: { name: 'Standard', capacity: 30 }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-large'
      version: '1'
    }
  }
  dependsOn: [gpt4oDeployment]
}

// ── Azure AI Search (Standard — wymagany dla semantic search) ─────────────────

resource search 'Microsoft.Search/searchServices@2023-11-01' = {
  name: searchName
  location: location
  sku: { name: 'standard' }
  properties: {
    replicaCount: 1
    partitionCount: 1
    semanticSearch: 'standard'
    publicNetworkAccess: 'enabled'
  }
}

// ── Function App (Consumption / Linux / .NET isolated) ───────────────────────

resource hostingPlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: planName
  location: location
  sku: { name: 'Y1', tier: 'Dynamic' }
  properties: { reserved: true }
}

resource functionApp 'Microsoft.Web/sites@2023-01-01' = {
  name: funcName
  location: location
  kind: 'functionapp,linux'
  identity: {
    type: 'SystemAssigned, UserAssigned'
    userAssignedIdentities: { '${identity.id}': {} }
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNET-ISOLATED|9.0'
      ftpsState: 'Disabled'
      appSettings: [
        { name: 'FUNCTIONS_EXTENSION_VERSION',              value: '~4' }
        { name: 'FUNCTIONS_WORKER_RUNTIME',                 value: 'dotnet-isolated' }
        { name: 'AzureWebJobsStorage',
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};AccountKey=${storage.listKeys().keys[0].value};EndpointSuffix=${environment().suffixes.storage}' }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING',    value: appInsights.properties.ConnectionString }
        // OpenAI — klucz przechowywany w Key Vault
        { name: 'AzureOpenAI__Endpoint',                    value: openAI.properties.endpoint }
        { name: 'AzureOpenAI__ApiKey',
          value: '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/openai-api-key/)' }
        { name: 'AzureOpenAI__EmbeddingDeployment',         value: 'text-embedding-3-large' }
        // Azure AI Search
        { name: 'AzureSearch__Endpoint',
          value: 'https://${search.name}.search.windows.net' }
        { name: 'AzureSearch__ApiKey',
          value: '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/search-api-key/)' }
        // Azure DevOps
        { name: 'AzureDevOps__Organization',                value: devOpsOrg }
        { name: 'AzureDevOps__Project',                     value: devOpsProject }
        { name: 'AzureDevOps__PersonalAccessToken',
          value: '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/devops-pat/)' }
        // Azure AI Foundry — uzupełnić po wdrożeniu AI Hub
        { name: 'Foundry__Endpoint',
          value: 'https://${aiProjectName}.${location}.api.azureml.ms' }
      ]
    }
  }
  dependsOn: [secretOpenAIKey, secretSearchKey, secretDevOpsPat]
}

// ── Azure AI Foundry (Hub + Project) ─────────────────────────────────────────

resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-04-01' = {
  name: aiHubName
  location: location
  kind: 'Hub'
  sku: { name: 'Basic', tier: 'Basic' }
  identity: { type: 'SystemAssigned' }
  properties: {
    friendlyName: 'DevOps Impact Analyzer Hub'
    storageAccount: storage.id
    keyVault: keyVault.id
    applicationInsights: appInsights.id
  }
}

resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-04-01' = {
  name: aiProjectName
  location: location
  kind: 'Project'
  sku: { name: 'Basic', tier: 'Basic' }
  identity: { type: 'SystemAssigned' }
  properties: {
    friendlyName: 'DevOps Impact Analyzer Project'
    hubResourceId: aiHub.id
  }
}

// Połączenie AI Hub ↔ Azure OpenAI
resource openAIConnection 'Microsoft.MachineLearningServices/workspaces/connections@2024-04-01' = {
  parent: aiHub
  name: 'openai-connection'
  properties: {
    category: 'AzureOpenAI'
    target: openAI.properties.endpoint
    authType: 'ApiKey'
    isSharedToAll: true
    credentials: { key: openAI.listKeys().key1 }
    metadata: {
      ApiType: 'azure'
      ResourceId: openAI.id
    }
  }
}

// ── Secrets in Key Vault ──────────────────────────────────────────────────────

resource secretOpenAIKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'openai-api-key'
  properties: { value: openAI.listKeys().key1 }
}

resource secretSearchKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'search-api-key'
  properties: { value: search.listAdminKeys().primaryKey }
}

resource secretDevOpsPat 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'devops-pat'
  properties: { value: devOpsPat }
}

// ── RBAC Role Assignments ─────────────────────────────────────────────────────

var roleKvSecretsUser         = '4633458b-17de-408a-b874-0445c86b69e0'
var roleOpenAIUser            = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'
var roleSearchIndexContrib    = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
var roleStorageBlobContrib    = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// Function App (system MSI) → Key Vault — odczyt sekretów
resource funcKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, funcName, roleKvSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Managed Identity → Key Vault
resource idKvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, identity.id, roleKvSecretsUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleKvSecretsUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Managed Identity → Azure OpenAI
resource idOpenAIRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: openAI
  name: guid(openAI.id, identity.id, roleOpenAIUser)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleOpenAIUser)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Managed Identity → Azure AI Search (indeksowanie + wyszukiwanie)
resource idSearchRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: search
  name: guid(search.id, identity.id, roleSearchIndexContrib)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleSearchIndexContrib)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// Managed Identity → Storage (wymagane przez Azure Functions runtime)
resource idStorageRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, identity.id, roleStorageBlobContrib)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roleStorageBlobContrib)
    principalId: identity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output functionAppName     string = functionApp.name
output functionAppHostname string = functionApp.properties.defaultHostName
output openAIEndpoint      string = openAI.properties.endpoint
output searchEndpoint      string = 'https://${search.name}.search.windows.net'
output keyVaultUri         string = keyVault.properties.vaultUri
output aiHubName           string = aiHub.name
output aiProjectName       string = aiProject.name