// Purpose: Host a Kimi-K3.1-Pro agent with the same deterministic tools as Demo 03.
// Local authentication uses Azure CLI; Foundry supplies the hosted identity.
using Azure.AI.AgentServer.Core;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI.Foundry.Hosting;
using OpenTelemetry.Trace;
using System.ClientModel.Primitives;

// Step 1: Use the existing project, without changing its model deployments.
var projectEndpoint = new Uri(DemoSettings.Read("FOUNDRY_PROJECT_ENDPOINT"));
TokenCredential credential = string.IsNullOrEmpty(
    Environment.GetEnvironmentVariable("FOUNDRY_HOSTING_ENVIRONMENT"))
    ? new AzureCliCredential()
    : new DefaultAzureCredential();

// Step 2: Use the verified project Responses route with this exact Kimi deployment.
var project = new AIProjectClient(projectEndpoint, credential,
    new AIProjectClientOptions { RetryPolicy = new ClientRetryPolicy(maxRetries: 0) });
var agent = KimiToolAgent.Create(project);

// Step 3: Collect real model and execute_tool spans without capturing their content.
var builder = AgentHost.CreateBuilder();
builder.ConfigureTracing(tracing => tracing.AddSource(KimiToolAgent.TelemetrySource));
builder.WebApplicationBuilder.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();
