/*
 * Purpose: Configure the same agent for local development and Foundry hosting.
 * 1. Read the native decision resource root and deployment from external configuration.
 * 2. Use Azure CLI locally and DefaultAzureCredential in Foundry.
 * 3. Expose the agent through the official Responses 2.0.0 host.
 */
using System.Runtime.CompilerServices;
using Azure.AI.AgentServer.Core;
using Azure.Core;
using Azure.Identity;
using ElBruno.AI.Decisions.Foundry;
using Microsoft.Agents.AI.Foundry.Hosting;
using SupportRouting;

[assembly: InternalsVisibleTo("SupportRoutingAgent.Tests")]

var builder = AgentHost.CreateBuilder(args);
builder.ConfigureTracing(tracing => tracing.AddSource(SupportRoutingAgent.TelemetrySource));
var endpoint = DemoSettings.Read("Decision__Endpoint");
var deployment = DemoSettings.Read("Decision__Deployment");

TokenCredential credential = Program.CreateDecisionCredential(
    Environment.GetEnvironmentVariable("FOUNDRY_HOSTING_ENVIRONMENT"));

using var logs = LoggerFactory.Create(logging => logging.AddConsole());
using var http = new HttpClient(new DecisionTransport(logs.CreateLogger("NativeDecision")));
using var decisions = new FoundryDecisionClient(http, new FoundryDecisionOptions
{
    Endpoint = new Uri(endpoint),
    Model = deployment,
    Credential = credential,
    Timeout = TimeSpan.FromSeconds(45)
});

builder.Services.AddFoundryResponses(new SupportRoutingAgent(decisions));
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();

public partial class Program
{
    internal static TokenCredential CreateDecisionCredential(string? hostingEnvironment) =>
        string.IsNullOrEmpty(hostingEnvironment)
            ? new AzureCliCredential()
            : new DefaultAzureCredential();
}
