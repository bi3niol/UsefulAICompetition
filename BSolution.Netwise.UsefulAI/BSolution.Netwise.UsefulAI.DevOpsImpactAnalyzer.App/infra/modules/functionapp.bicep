targetScope = 'resourceGroup'

// ── Parameters ────────────────────────────────────────────────────────────────

param location string
param resourcePrefix string
param environment string
param aspSku string
param tags object

// ── Naming ────────────────────────────────────────────────────────────────────
// Storage account: lowercase alphanumeric only, max 24 chars

var cleanPrefix        = toLower(replace(resourcePrefix, '-', ''))
var storageAccountName = take('${cleanPrefix}fn${uniqueString(resourceGroup().id)}', 24)
var keyVaultName       = take('${cleanPrefix}kv${uniqueString(resourceGroup().id)}', 24)
var aspName            = '${resourcePrefix}-asp-${environment}'
var functionAppName    = '${resourcePrefix}-func-${environment}'
var appInsightsName    = '${resourcePrefix}-appi-${environment}'
var logAnalyticsName   = '${resourcePrefix}-law-${environment}'

// ── Built-in Role Definition IDs ──────────────────────────────────────────────

var keyVaultSecretsUserRoleId      = '4633458b-17de-408a-b874-0445c86b69e6'
var storageBlobDataOwnerRoleId     = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageQueueDataContributorId  = '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributorId  = '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'

// ── Storage Account (Function App trigger state, timer coordination) ───────────

resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageAccountName
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

// Roles: Function App managed identity → Storage (keyless AzureWebJobsStorage auth)
resource storageBlobOwnerRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, storageBlobDataOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, storageQueueDataContributorId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributorId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, functionApp.id, storageTableDataContributorId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributorId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Log Analytics Workspace ───────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

// ── Application Insights ──────────────────────────────────────────────────────

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
  }
}

// ── Key Vault (for PAT, API keys stored as Key Vault references) ──────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
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

// ── App Service Plan (Windows, Dedicated) ─────────────────────────────────────

resource appServicePlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: aspName
  location: location
  tags: tags
  sku: {
    name: aspSku
  }
  properties: {
    reserved: false // Windows
  }
}

// ── Function App (.NET 10 isolated, Windows ASP hosting) ─────────────────────

resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      // .NET 10 isolated worker — if deployment fails, check Azure Functions .NET 10 support status
      netFrameworkVersion: 'v10.0'
      use32BitWorkerProcess: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      appSettings: [
        // Keyless storage auth via managed identity (no connection string needed)
        {
          name: 'AzureWebJobsStorage__accountName'
          value: storage.name
        }
        {
          name: 'AzureWebJobsStorage__credential'
          value: 'managedidentity'
        }
        {
          name: 'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name: 'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'WEBSITE_RUN_FROM_PACKAGE'
          value: '1'
        }
        // ── Placeholders — replace values with Key Vault references after deployment ──
        // {
        //   name: 'Foundry__Endpoint'
        //   value: '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/Foundry--Endpoint)'
        // }
        // {
        //   name: 'AzureDevOps__PersonalAccessToken'
        //   value: '@Microsoft.KeyVault(SecretUri=${keyVault.properties.vaultUri}secrets/AzureDevOps--PersonalAccessToken)'
        // }
      ]
    }
  }
}

// ── Role: Key Vault Secrets User → Function App ───────────────────────────────

resource kvSecretsUserRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: keyVault
  name: guid(keyVault.id, functionApp.id, keyVaultSecretsUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: functionApp.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output functionAppName string        = functionApp.name
output functionAppUrl string         = 'https://${functionApp.properties.defaultHostName}'
output functionAppPrincipalId string = functionApp.identity.principalId
output keyVaultName string           = keyVault.name
output keyVaultUri string            = keyVault.properties.vaultUri
output storageAccountName string     = storage.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
