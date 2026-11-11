/*
 * Purpose: Call a deployed Responses endpoint and return only a completed typed decision.
 * 1. Request a Foundry token for the caller's Azure CLI identity.
 * 2. Verify both HTTP success and the final Responses status.
 * 3. Deserialize the agent's JSON into the shared public contract.
 */
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Net;
using Azure.Core;
using SupportRouting;

namespace SupportRoutingCaller;

public sealed record ResponsesAgentResult(RoutingDecision Decision, string? ResponseId);

public sealed class ResponsesAgentHttpException : HttpRequestException
{
    public ResponsesAgentHttpException(
        HttpStatusCode statusCode,
        string? providerErrorType,
        string? providerErrorCode,
        string? providerErrorParameter,
        string? providerRequestIdHeader,
        string? providerRequestId)
        : base($"Foundry Responses request failed with HTTP {(int)statusCode}.", null, statusCode)
    {
        ResponseStatusCode = statusCode;
        ProviderErrorType = providerErrorType;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorParameter = providerErrorParameter;
        ProviderRequestIdHeader = providerRequestIdHeader;
        ProviderRequestId = providerRequestId;
    }

    public HttpStatusCode ResponseStatusCode { get; }
    public string? ProviderErrorType { get; }
    public string? ProviderErrorCode { get; }
    public string? ProviderErrorParameter { get; }
    public string? ProviderRequestIdHeader { get; }
    public string? ProviderRequestId { get; }
}

public sealed class ResponsesAgentStatusException : InvalidOperationException
{
    public ResponsesAgentStatusException(string finalStatus, string? responseId)
        : base($"The Foundry Responses request did not complete successfully. Final status: {finalStatus}.")
    {
        FinalStatus = finalStatus;
        ResponseId = responseId;
    }

    public string FinalStatus { get; }
    public string? ResponseId { get; }
}

public sealed class ResponsesAgentClient
{
    private const string FoundryScope = "https://ai.azure.com/.default";
    private const string ResponsesApiVersion = "v1";
    private const string HostedResponseIdPrefix = "caresp_";
    private const int MaximumErrorBodyBytes = 8192;
    private static readonly string[] ProviderErrorTypes =
    [
        "invalid_request_error",
        "authentication_error",
        "permission_error",
        "not_found_error",
        "rate_limit_error",
        "server_error",
        "unprocessable_entity_error",
        "service_unavailable_error",
        "conflict_error",
        "request_timeout_error"
    ];
    private static readonly string[] ProviderErrorCodes =
    [
        "invalid_request_error",
        "invalid_value",
        "invalid_parameter",
        "missing_required_parameter",
        "unknown_parameter",
        "invalid_type",
        "model_not_found",
        "invalid_api_key",
        "insufficient_quota",
        "rate_limit_exceeded",
        "permission_denied",
        "not_found",
        "bad_request",
        "unsupported_media_type",
        "unauthorized",
        "server_error"
    ];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly Uri _responsesEndpoint;
    private readonly string _agentName;

    public ResponsesAgentClient(
        HttpClient httpClient,
        TokenCredential credential,
        Uri agentOpenAiBaseUrl,
        string agentName)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(agentOpenAiBaseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);

        if (!agentOpenAiBaseUrl.IsAbsoluteUri ||
            agentOpenAiBaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("The agent OpenAI-compatible base URL must be an absolute HTTPS URI.", nameof(agentOpenAiBaseUrl));
        }

        _httpClient = httpClient;
        _credential = credential;
        _responsesEndpoint = CreateResponsesEndpoint(agentOpenAiBaseUrl);
        _agentName = agentName;
    }

    public async Task<ResponsesAgentResult> InvokeAsync(
        string ticket,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticket);

        var accessToken = await _credential.GetTokenAsync(
            new TokenRequestContext([FoundryScope]),
            cancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, _responsesEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Token);
        request.Content = JsonContent.Create(new
        {
            model = _agentName,
            input = ticket,
            stream = false,
            store = false
        });

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var providerError = await ReadProviderErrorAsync(response.Content, cancellationToken);
            var providerRequestId = FindProviderRequestId(response.Headers);
            throw new ResponsesAgentHttpException(
                response.StatusCode,
                providerError?.Type,
                providerError?.Code,
                providerError?.Parameter,
                providerRequestId?.Header,
                providerRequestId?.Value);
        }

        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        var root = document.RootElement;
        var status = root.TryGetProperty("status", out var statusElement) &&
            statusElement.ValueKind == JsonValueKind.String
            ? statusElement.GetString()
            : null;
        var responseId = FindResponseId(root);

        if (!string.Equals(status, "completed", StringComparison.Ordinal))
        {
            throw new ResponsesAgentStatusException(NormalizeResponseStatus(status), responseId);
        }

        var decisionJson = FindOutputText(root);
        var decision = JsonSerializer.Deserialize<RoutingDecision>(decisionJson, JsonOptions)
            ?? throw new InvalidDataException("The completed Foundry response did not contain a routing decision.");

        return new ResponsesAgentResult(decision, responseId);
    }

    private static async Task<ProviderErrorDetails?> ReadProviderErrorAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        var mediaType = content.Headers.ContentType?.MediaType;
        if (content.Headers.ContentLength is > MaximumErrorBodyBytes ||
            !(string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase) ||
              mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true))
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[MaximumErrorBodyBytes + 1];
        var bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(bytesRead), cancellationToken);
            if (count == 0)
            {
                break;
            }

            bytesRead += count;
        }

        if (bytesRead > MaximumErrorBodyBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(buffer.AsMemory(0, bytesRead));
            if (!document.RootElement.TryGetProperty("error", out var error) ||
                error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return new ProviderErrorDetails(
                ReadSafeErrorToken(error, "type"),
                ReadSafeErrorToken(error, "code"),
                ReadSafeErrorParameter(error));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadSafeErrorToken(JsonElement error, string propertyName)
    {
        if (!error.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var token = value.GetString();
        if (token is not { Length: > 0 and <= 80 } ||
            !token.All(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            return null;
        }

        var knownValues = propertyName switch
        {
            "type" => ProviderErrorTypes,
            "code" => ProviderErrorCodes,
            _ => []
        };
        return knownValues.Contains(token, StringComparer.Ordinal) ? token : "other";
    }

    private static string? ReadSafeErrorParameter(JsonElement error)
    {
        if (!error.TryGetProperty("param", out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var parameter = value.GetString();
        return parameter switch
        {
            "model" or "input" or "stream" or "store" or "previous_response_id" or "agent_session_id" =>
                parameter,
            _ => "other"
        };
    }

    private static (string Header, string Value)? FindProviderRequestId(
        System.Net.Http.Headers.HttpResponseHeaders headers)
    {
        foreach (var header in new[]
        {
            "x-ms-request-id",
            "apim-request-id",
            "x-ms-correlation-request-id",
            "x-ms-correlation-id"
        })
        {
            if (!headers.TryGetValues(header, out var values))
            {
                continue;
            }

            var requestIds = values.Take(2).ToArray();
            if (requestIds is [var value] &&
                Guid.TryParseExact(value, "D", out var requestId))
            {
                return (header, requestId.ToString("D"));
            }
        }

        return null;
    }

    private static string? FindResponseId(JsonElement response)
    {
        if (!response.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var responseId = idElement.GetString();
        if (responseId is null ||
            responseId.Length <= HostedResponseIdPrefix.Length ||
            responseId.Length > 256 ||
            !responseId.StartsWith(HostedResponseIdPrefix, StringComparison.Ordinal) ||
            responseId.Skip(HostedResponseIdPrefix.Length).Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-')))
        {
            return null;
        }

        return responseId;
    }

    private static string NormalizeResponseStatus(string? status) =>
        status switch
        {
            "failed" or "incomplete" or "in_progress" or "queued" or "cancelled" or "requires_action" =>
                status,
            null => "missing",
            _ => "other"
        };

    private static string FindOutputText(JsonElement response)
    {
        if (response.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("type", out var type) &&
                        type.GetString() == "output_text" &&
                        part.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString()
                            ?? throw new InvalidDataException("The completed Foundry response contained null output text.");
                    }
                }
            }
        }

        throw new InvalidDataException("The completed Foundry response did not contain output text.");
    }

    private static Uri CreateResponsesEndpoint(Uri agentOpenAiBaseUrl)
    {
        var endpoint = new UriBuilder(agentOpenAiBaseUrl)
        {
            Path = $"{agentOpenAiBaseUrl.AbsolutePath.TrimEnd('/')}/responses"
        };
        var queryParameters = agentOpenAiBaseUrl.Query
            .TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(parameter =>
                !string.Equals(
                    Uri.UnescapeDataString(parameter.Split('=', 2)[0]),
                    "api-version",
                    StringComparison.OrdinalIgnoreCase));

        endpoint.Query = string.Join(
            '&',
            queryParameters.Append($"api-version={ResponsesApiVersion}"));

        return endpoint.Uri;
    }

    private sealed record ProviderErrorDetails(string? Type, string? Code, string? Parameter);
}
