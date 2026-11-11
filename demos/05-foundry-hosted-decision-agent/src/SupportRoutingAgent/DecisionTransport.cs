/*
 * Purpose: Keep native decision transport telemetry free of request content.
 * Status and bounded, unvalidated authentication metadata support private diagnosis.
 * Never record bearer tokens or ticket text. Authentication stays in the SDK.
 */
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SupportRouting;

public sealed class DecisionTransport(ILogger logger) : DelegatingHandler(
    new SocketsHttpHandler { AllowAutoRedirect = false })
{
    private const long MaximumExpiryUnixSeconds = 253402300799;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var decisionActivity = FindDecisionActivity(Activity.Current);
        var response = await base.SendAsync(request, cancellationToken);
        logger.LogInformation("Native decision HTTP {StatusCode}", (int)response.StatusCode);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            var diagnostics = new ActivityTagsCollection
            {
                ["http.response.status_code"] = (int)response.StatusCode,
                ["native_decision.authentication_challenge_present"] =
                    response.Headers.WwwAuthenticate.Count > 0
            };
            logger.LogWarning("Native decision authentication challenge present: {Present}",
                response.Headers.WwwAuthenticate.Count > 0);
            LogProviderRequestIds(response, logger, diagnostics);
            LogUnvalidatedBearerMetadata(request.Headers.Authorization?.Parameter, logger, diagnostics);
            try
            {
                await LogProviderErrorBodyAsync(response, logger, diagnostics, cancellationToken);
            }
            finally
            {
                // Preserve available diagnostics even when reading the error body fails.
                AddActivityTags(decisionActivity, diagnostics);
            }
        }
        else if (response.IsSuccessStatusCode)
        {
            decisionActivity?.SetTag("native_decision.failure_diagnostics_wired", true);
        }

        return response;
    }

    internal static void LogUnvalidatedBearerMetadata(
        string? token,
        ILogger logger,
        ActivityTagsCollection diagnostics)
    {
        // Comparison metadata is not token validation or authorization evidence.
        if (token is null || token.Length > 16384 || token.Split('.') is not [_, var payload, _]
            || payload.Length > 8192)
        {
            logger.LogWarning("Unvalidated bearer metadata unavailable");
            diagnostics["native_decision.bearer_metadata_available"] = false;
            return;
        }

        try
        {
            payload = payload.Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
            var root = claims.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning("Unvalidated bearer metadata unavailable");
                diagnostics["native_decision.bearer_metadata_available"] = false;
                return;
            }

            var audience = root.TryGetProperty("aud", out var aud)
                && aud.ValueKind == JsonValueKind.String
                && aud.GetString() == "https://cognitiveservices.azure.com"
                    ? "https://cognitiveservices.azure.com" : "unrecognized";
            long? expiry = root.TryGetProperty("exp", out var exp)
                && exp.ValueKind == JsonValueKind.Number && exp.TryGetInt64(out var seconds)
                && seconds is >= 0 and <= MaximumExpiryUnixSeconds ? seconds : null;
            var fingerprint = root.TryGetProperty("oid", out var oid)
                && oid.ValueKind == JsonValueKind.String
                ? FingerprintPrincipal(oid.GetString()) : null;
            diagnostics["native_decision.bearer_metadata_available"] = true;
            diagnostics["native_decision.token_audience"] = audience;
            diagnostics["native_decision.token_oid_sha256"] = fingerprint ?? "unavailable";
            if (expiry is not null)
                diagnostics["native_decision.token_expires_unix_seconds"] = expiry.Value;
            logger.LogWarning(
                "Unvalidated bearer metadata: audience={Audience}, expires={Expires}, oidSha256={OidSha256}",
                audience, expiry, fingerprint ?? "unavailable");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            logger.LogWarning("Unvalidated bearer metadata unavailable: {ErrorType}", ex.GetType().Name);
            diagnostics["native_decision.bearer_metadata_available"] = false;
        }
    }

    internal static string? FingerprintPrincipal(string? value) =>
        value is { Length: 36 } && Guid.TryParseExact(value, "D", out var principal)
            && principal != Guid.Empty
                ? Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(principal.ToString("D"))))
                : null;

    private static async Task LogProviderErrorBodyAsync(
        HttpResponseMessage response,
        ILogger logger,
        ActivityTagsCollection diagnostics,
        CancellationToken cancellationToken)
    {
        diagnostics["native_decision.provider_error_body_inspected"] = false;
        if (response.Content.Headers.ContentLength is not (> 0 and <= 4096))
            return;

        diagnostics["native_decision.provider_error_body_inspected"] = true;
        try
        {
            using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = error.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("error", out var details)
                || details.ValueKind != JsonValueKind.Object)
            {
                diagnostics["native_decision.provider_error_shape_unexpected"] = true;
                logger.LogWarning("Native authentication error body had an unexpected JSON shape");
                return;
            }

            var unexpectedValue = false;
            if (details.TryGetProperty("code", out var code))
            {
                if (code.ValueKind == JsonValueKind.String)
                {
                    if (code.GetString() == "PermissionDenied")
                    {
                        diagnostics["native_decision.provider_error_code"] = "PermissionDenied";
                        logger.LogWarning("Native decision error code: PermissionDenied");
                    }
                }
                else
                {
                    unexpectedValue = true;
                }
            }

            if (details.TryGetProperty("message", out var message))
            {
                if (message.ValueKind == JsonValueKind.String)
                {
                    if (message.GetString()?.Contains(
                        "Principal does not have access to API/Operation",
                        StringComparison.Ordinal) == true)
                    {
                        diagnostics["native_decision.provider_error_message"] =
                            "Principal does not have access to API/Operation";
                        logger.LogWarning("Native decision error message: Principal does not have access to API/Operation");
                    }
                }
                else
                {
                    unexpectedValue = true;
                }
            }

            diagnostics["native_decision.provider_error_shape_unexpected"] = unexpectedValue;
            if (unexpectedValue)
                logger.LogWarning("Native authentication error body had an unexpected JSON shape");
        }
        catch (JsonException)
        {
            diagnostics["native_decision.provider_error_body_not_json"] = true;
            logger.LogWarning("Native authentication error body was not JSON");
        }
    }

    private static void LogProviderRequestIds(
        HttpResponseMessage response,
        ILogger logger,
        ActivityTagsCollection diagnostics)
    {
        foreach (var (header, tag) in new[]
        {
            ("x-ms-request-id", "native_decision.x_ms_request_id"),
            ("apim-request-id", "native_decision.apim_request_id"),
            ("x-ms-correlation-request-id", "native_decision.x_ms_correlation_request_id"),
            ("x-ms-correlation-id", "native_decision.x_ms_correlation_id")
        })
        {
            if (!response.Headers.TryGetValues(header, out var values))
                continue;
            var ids = values.Take(2).ToArray();
            if (ids is [var value] && value.Length == 36 && Guid.TryParseExact(value, "D", out var id))
            {
                var normalizedId = id.ToString("D");
                diagnostics[tag] = normalizedId;
                logger.LogWarning("Native decision provider identifier: {Header}={Identifier}", header, id.ToString("D"));
            }
        }
    }

    private static void AddActivityTags(Activity? activity, ActivityTagsCollection tags)
    {
        if (activity is null)
            return;

        foreach (var tag in tags)
            activity.SetTag(tag.Key, tag.Value);
    }

    private static Activity? FindDecisionActivity(Activity? activity)
    {
        while (activity is not null)
        {
            if (activity.Source.Name == SupportRoutingAgent.TelemetrySource
                && activity.DisplayName == "decision.route_support")
                return activity;

            activity = activity.Parent;
        }

        return null;
    }
}
