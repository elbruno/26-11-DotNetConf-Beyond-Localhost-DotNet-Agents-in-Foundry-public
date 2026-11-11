// Purpose: Run one concrete Agent Framework request from the terminal.
// Requires a Foundry project, a model deployment, and an authorized Azure CLI sign-in.
// Expected result: a real model-generated .NET fact, not a local or canned response.
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;

// Step 1: Require the existing project and deployment explicitly.
var projectEndpoint = new Uri(DemoSettings.Read("FOUNDRY_PROJECT_ENDPOINT"));
var deployment = DemoSettings.Read("AZURE_AI_MODEL_DEPLOYMENT_NAME");

// Step 2: Give the model a name and a small, easy-to-explain instruction.
AIAgent agent = new AIProjectClient(projectEndpoint, new AzureCliCredential())
    .AsAIAgent(
        model: deployment,
        instructions: "You are a friendly assistant. Keep your answers brief.",
        name: "HelloAgent");

// Step 3: Ask once. The console owns the interaction in this first demo.
Console.WriteLine(await agent.RunAsync("Hello! Tell me a fun fact about .NET."));
