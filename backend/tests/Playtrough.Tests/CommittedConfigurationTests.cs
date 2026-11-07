using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace Playtrough.Tests;

public class CommittedConfigurationTests
{
    [Theory]
    [InlineData("{\"Jwt\":{\"Key\":\"test-only-canary\"}}")]
    [InlineData("{\"Jwt__Key\":\"test-only-canary\"}")]
    [InlineData("{\"Email\":{\"Password\":\"test-only-canary\"}}")]
    [InlineData("{\"AdminSeed\":{\"Password\":\"test-only-canary\"}}")]
    [InlineData("{\"ConnectionStrings\":{\"DefaultConnection\":\"Server=localhost;Password=test-only-canary;\"}}")]
    [InlineData("{\"ConnectionStrings\":{\"DefaultConnection\":\"Server=localhost;Pwd=test-only-canary;\"}}")]
    public void Guard_RejectsCredentialsAndReportsOnlyLocation(string source)
    {
        using var document = JsonDocument.Parse(source);
        var failures = new List<string>();
        Inspect(document.RootElement, "appsettings.test.json", "", failures);
        Assert.NotEmpty(failures);
        Assert.All(failures, message =>
        {
            Assert.Contains("appsettings.test.json", message);
            Assert.DoesNotContain("test-only-canary", message);
        });
    }

    [Fact]
    public void TrackedAppsettings_NeverContainNonEmptyCredentials()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, ".git")) &&
               !File.Exists(Path.Combine(root.FullName, ".git"))) root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root.FullName, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
        };
        start.ArgumentList.Add("ls-files");
        start.ArgumentList.Add("-z");
        using var process = Process.Start(start)!;
        var paths = process.StandardOutput.ReadToEnd().Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(process.WaitForExit(10000), "Could not enumerate tracked configuration files.");
        Assert.True(process.ExitCode == 0, "Git could not enumerate tracked configuration files.");
        var configs = paths.Where(path => Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(configs);
        var failures = new List<string>();
        foreach (var path in configs)
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, path)),
                    new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                Inspect(json.RootElement, path, "", failures);
            }
            catch (JsonException) { failures.Add($"{path}: invalid JSON (content withheld)."); }
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    private static void Inspect(JsonElement node, string file, string key, List<string> failures)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in node.EnumerateObject())
                Inspect(property.Value, file, key.Length == 0 ? property.Name : $"{key}:{property.Name}", failures);
            return;
        }
        if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in node.EnumerateArray()) Inspect(item, file, key, failures);
            return;
        }
        var normalized = key.Replace("__", ":", StringComparison.Ordinal).ToLowerInvariant();
        var name = normalized.Split(':')[^1];
        var sensitive = name.EndsWith("password", StringComparison.Ordinal) || name is "pwd" or "secret" or "clientsecret" or "apikey" ||
            ((normalized.StartsWith("jwt:", StringComparison.Ordinal) || normalized.StartsWith("jwtsettings:", StringComparison.Ordinal)) && name == "key");
        if (sensitive && node.ValueKind != JsonValueKind.Null &&
            (node.ValueKind != JsonValueKind.String || !string.IsNullOrEmpty(node.GetString())))
            failures.Add($"{file}: {key} must be empty (value withheld).");
        if (normalized.StartsWith("connectionstrings:", StringComparison.Ordinal) && node.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(node.GetString()))
        {
            try
            {
                var connection = new DbConnectionStringBuilder { ConnectionString = node.GetString()! };
                foreach (string part in connection.Keys)
                    if ((part.Equals("password", StringComparison.OrdinalIgnoreCase) || part.Equals("pwd", StringComparison.OrdinalIgnoreCase)) &&
                        !string.IsNullOrEmpty(connection[part]?.ToString()))
                        failures.Add($"{file}: {key} contains a non-empty database password (value withheld).");
            }
            catch (ArgumentException) { failures.Add($"{file}: {key} cannot be checked safely (value withheld)."); }
        }
    }
}
