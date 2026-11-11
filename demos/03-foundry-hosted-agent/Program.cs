// Purpose: Expose netconf-hello-agent and three read-only tools through Foundry Responses.
// Requires the same project, deployment, and authorized identity as Demo 1.
// Expected result: an HTTP host; a response still requires a real Foundry model call.
using Azure.AI.AgentServer.Core;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;
using OpenTelemetry.Trace;

// Step 1: Reuse the same model and identity as the basic host.
var projectEndpoint = new Uri(DemoSettings.Read("FOUNDRY_PROJECT_ENDPOINT"));
var deployment = DemoSettings.Read("AZURE_AI_MODEL_DEPLOYMENT_NAME");

AIAgent agent = new AIProjectClient(projectEndpoint, new DefaultAzureCredential())
    .AsAIAgent(
        model: deployment,
        instructions: """
            You are a friendly assistant. Keep your answers brief.
            When asked for the demo's .NET fact, call GetDotNetFact.
            When asked to add two integers, call AddNumbers instead of calculating yourself.
            When asked to count words in supplied text, call CountWords.
            Use the tool results in your answer.
            """,
        name: "netconf-hello-agent",
        tools: DemoTools.Create(),
        // Instrument the inner client before MAF builds its function-invocation loop.
        clientFactory: client => client.AsBuilder()
            .UseOpenTelemetry(sourceName: DemoTools.TelemetrySource)
            .Build());

// Step 2: These are the three hosting registrations from the article.
var builder = AgentHost.CreateBuilder();
builder.ConfigureTracing(tracing => tracing.AddSource(DemoTools.TelemetrySource));
builder.WebApplicationBuilder.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

// Step 3: The host now owns the interaction instead of Console.WriteLine.
var app = builder.Build();
app.Run();
