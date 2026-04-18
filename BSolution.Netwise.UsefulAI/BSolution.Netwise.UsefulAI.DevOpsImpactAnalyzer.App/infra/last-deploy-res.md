 Deployment complete!

  Function App Name  : bs-useful-func-dev
  Function App URL   : https://bs-useful-func-dev.azurewebsites.net
  Key Vault URI      : https://bsusefulkvwulklaqz5uiow.vault.azure.net/
  AI Search Endpoint : https://bs-useful-search-dev.search.windows.net
  Azure OpenAI EP    : https://bs-useful-openai-dev.openai.azure.com/
  Foundry Hub        : bs-useful-aihub-dev
  Foundry Project    : bs-useful-aiproj-dev

 Foundry Project Endpoint (verify in Azure AI Foundry portal â† Project overview â† API endpoint):
  https://bs-useful-aihub-dev.services.ai.azure.com/api/projects/bs-useful-aiproj-dev

 Next steps:
  1. Add secrets to Key Vault:
       az keyvault secret set --vault-name bsusefulkvwulklaqz5uiow --name AzureDevOps--PersonalAccessToken --value <PAT>
       az keyvault secret set --vault-name bsusefulkvwulklaqz5uiow --name Foundry--Endpoint --value <endpoint>

  2. Configure Function App settings with Key Vault references:
       az functionapp config appsettings set --name bs-useful-func-dev \\
         --resource-group rg-ntw-usefulai-app-dev \\
         --settings "Foundry__Endpoint=@Microsoft.KeyVault(SecretUri=https://bsusefulkvwulklaqz5uiow.vault.azure.net/secrets/Foundry--Endpoint)"

  3. Deploy the function app code:
       func azure functionapp publish bs-useful-func-dev

  4. Configure Azure DevOps Service Hook (Webhook) to:
       https://bs-useful-func-dev.azurewebsites.net/api/WorkItemWebhook