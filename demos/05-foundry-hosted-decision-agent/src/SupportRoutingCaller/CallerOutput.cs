/*
 * Purpose: Keep the typed decision on stdout and trace correlation diagnostics on stderr.
 */
using System.Text.Json;
using SupportRouting;

namespace SupportRoutingCaller;

public static class CallerOutput
{
    public static void WriteTerminalFailure(
        ResponsesAgentStatusException failure,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(standardError);

        var responseId = failure.ResponseId is { } id
            ? $", responseId={id}"
            : ", responseId=unavailable";

        standardError.WriteLine(
            $"Foundry Responses terminal status: status={failure.FinalStatus}{responseId}");
        if (failure.ResponseId is null)
        {
            standardError.WriteLine("Trace correlation is unavailable.");
        }
    }

    public static void WriteHttpFailure(
        ResponsesAgentHttpException failure,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(standardError);

        var requestId = failure.ProviderRequestId is { } id &&
            failure.ProviderRequestIdHeader is { } header
                ? $", {header}={id}"
                : string.Empty;

        standardError.WriteLine(
            $"Foundry Responses HTTP failure: status={(int)failure.ResponseStatusCode}, " +
            $"errorType={failure.ProviderErrorType ?? "unavailable"}, " +
            $"errorCode={failure.ProviderErrorCode ?? "unavailable"}, " +
            $"parameter={failure.ProviderErrorParameter ?? "unavailable"}{requestId}");
    }

    public static void Write(
        ResponsesAgentResult result,
        TextWriter standardOutput,
        TextWriter standardError)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(standardOutput);
        ArgumentNullException.ThrowIfNull(standardError);

        standardOutput.WriteLine(JsonSerializer.Serialize(
            result.Decision,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        standardError.WriteLine(result.ResponseId is { } responseId
            ? $"Foundry response ID: {responseId}"
            : "Foundry response ID is missing or malformed; trace correlation is unavailable.");
    }
}
