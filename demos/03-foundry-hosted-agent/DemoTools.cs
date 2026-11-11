// Purpose: Provide three deterministic, read-only functions for the hosted agent.
// No service, credentials or setup is needed by these functions.
// MAF invokes them when the model requests a tool and records the execution spans.
using System.ComponentModel;
using Microsoft.Extensions.AI;

public static class DemoTools
{
    public const string TelemetrySource = "BeyondLocalhost.HelloAgent";

    public static IList<AITool> Create() =>
    [
        AIFunctionFactory.Create(GetDotNetFact, name: "GetDotNetFact"),
        AIFunctionFactory.Create(AddNumbers, name: "AddNumbers"),
        AIFunctionFactory.Create(CountWords, name: "CountWords")
    ];

    [Description("Get the demo's short, fixed fact about the .NET garbage collector.")]
    public static string GetDotNetFact() =>
        "The .NET garbage collector automatically reclaims memory occupied by unreachable objects.";

    [Description("Add two integers and return their exact sum.")]
    public static int AddNumbers(
        [Description("The first integer.")] int a,
        [Description("The second integer.")] int b) => checked(a + b);

    [Description("Count the whitespace-separated words in the supplied text.")]
    public static int CountWords(
        [Description("Text whose whitespace-separated words are counted.")] string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
}
