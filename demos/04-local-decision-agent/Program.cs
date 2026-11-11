// Purpose: Run the hosted sample's decision agent directly, without an HTTP host.
// Requires an existing Foundry Decision__Endpoint and Decision__Deployment, plus
// separately authorized Azure CLI sign-in. Expected result: one typed routing result.
using Azure.Identity;
using ElBruno.AI.Decisions.Foundry;
using SupportRouting;
using System.Text.Json;

// Step 1: Configure the native decision deployment, not a chat project endpoint.
var endpoint = new Uri(DemoSettings.Read("Decision__Endpoint"));
var deployment = DemoSettings.Read("Decision__Deployment");

using var decisions = new FoundryDecisionClient(new FoundryDecisionOptions
{
    Endpoint = endpoint,
    Model = deployment,
    Credential = new AzureCliCredential(),
    Timeout = TimeSpan.FromSeconds(45)
});

// Step 2: Run the exact same agent logic that Demo 05 exposes through Responses.
const string ticket = "I was charged twice for my subscription.";
Console.WriteLine($"Question: {ticket}");
var response = await new SupportRoutingAgent(decisions).RunAsync(ticket);

// Step 3: Print the bounded decision, not a generated chat explanation.
using var result = JsonDocument.Parse(response.Messages.Single().Text!);
Console.WriteLine(JsonSerializer.Serialize(result.RootElement, new JsonSerializerOptions { WriteIndented = true }));
