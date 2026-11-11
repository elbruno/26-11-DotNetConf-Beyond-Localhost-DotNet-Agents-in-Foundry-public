// Purpose: Keep the Kimi model, instructions and tools together for the walkthrough.
using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

public static class KimiToolAgent
{
    public const string Name = "netconf-kimi-tools";
    public const string Model = "Kimi-K3.1-Pro";
    public const string TelemetrySource = "BeyondLocalhost.KimiAgent";

    public static AIAgent Create(AIProjectClient project) =>
        project.AsAIAgent(CreateOptions(), clientFactory: Instrument);

    public static AIAgent Create(IChatClient client) =>
        Instrument(client).AsAIAgent(CreateOptions());

    private static IChatClient Instrument(IChatClient client) =>
        client.AsBuilder()
            .UseOpenTelemetry(sourceName: TelemetrySource)
            .Build();

    private static ChatClientAgentOptions CreateOptions() => new()
    {
        Name = Name,
        ChatOptions = new ChatOptions
        {
            ModelId = Model,
            Instructions = """
                You are a concise .NET demo assistant.
                For the demo's .NET fact, call GetDotNetFact.
                For integer addition, call AddNumbers instead of calculating yourself.
                For word counts, call CountWords with the exact supplied text.
                When a request names multiple tools, call every named tool.
                Use the returned tool results in your short final answer.
                """,
            Tools = DemoTools.Create()
        }
    };
}
