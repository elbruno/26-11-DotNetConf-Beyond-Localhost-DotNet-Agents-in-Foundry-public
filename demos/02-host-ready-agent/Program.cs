// Purpose: Expose the console demo's agent through the Foundry Responses protocol.
// Requires the same project, deployment, and authorized identity as Demo 1.
// Expected result: an HTTP host; a response still requires a real Foundry model call.
using Azure.AI.AgentServer.Core;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;

// Step 1: Keep the model configuration and agent behavior unchanged.
var projectEndpoint = new Uri(DemoSettings.Read("FOUNDRY_PROJECT_ENDPOINT"));
var deployment = DemoSettings.Read("AZURE_AI_MODEL_DEPLOYMENT_NAME");

AIAgent agent = new AIProjectClient(projectEndpoint, new AzureCliCredential())
    .AsAIAgent(
        model: deployment,
        instructions: "You are a friendly assistant. Keep your answers brief.",
        name: "HelloAgent");

// Step 2: These are the three hosting registrations from the article.
var builder = AgentHost.CreateBuilder();
builder.WebApplicationBuilder.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

// Step 3: The host now owns the interaction instead of Console.WriteLine.
var app = builder.Build();
app.Run();
