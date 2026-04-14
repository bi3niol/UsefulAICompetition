using Azure.AI.Projects;
using Azure.Identity;
using BSolution.Netwise.UsefulAI.DevOpsImpactAnalyzer.App.Configs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var endpoint = new Uri(config["Foundry:Endpoint"]!);
    return new AIProjectClient(endpoint, new DefaultAzureCredential());
});

builder.Services.AddImpactAnalyzerTools();

builder.Build().Run();
