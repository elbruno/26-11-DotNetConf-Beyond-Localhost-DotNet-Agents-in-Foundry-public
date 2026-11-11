// Purpose: Read only non-secret demo settings, independently of the working directory.
// Setup saves an ignored JSON file copied beside the compiled program.
// Meaningful environment overrides win; blank overrides cannot erase saved settings.
using System.Text.Json;

internal static class DemoSettings
{
    public static string Read(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            Console.Error.WriteLine($"Ignoring a blank {name} environment override; checking saved demo settings.");
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            string path = Path.Combine(AppContext.BaseDirectory, "demo-settings.local.json");
            if (File.Exists(path))
            {
                using var settings = JsonDocument.Parse(File.ReadAllText(path));
                value = settings.RootElement.TryGetProperty(name, out var saved)
                    ? saved.GetString()
                    : null;
            }
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing {name}. Configure the demos once with Set-DemoEnvironment.ps1.");
        }

        if (name.EndsWith("ENDPOINT", StringComparison.Ordinal) || name.EndsWith("Endpoint", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"
                || string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo)
                || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)
                || value.Any(char.IsWhiteSpace)
                || (name == "FOUNDRY_PROJECT_ENDPOINT"
                    ? !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, @"^/api/projects/[^/]+/?$")
                    : uri.AbsolutePath != "/"))
            {
                throw new InvalidOperationException($"Invalid {name}. Use the HTTPS project URL or native decision resource root appropriate to this setting.");
            }
        }
        else if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}\z"))
        {
            throw new InvalidOperationException($"Invalid {name}. A deployment name must contain 1 to 128 letters, digits, dots, underscores or hyphens.");
        }

        return value;
    }
}
