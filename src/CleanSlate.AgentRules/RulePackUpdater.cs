using System.Security.Cryptography;
using System.Text.Json;

namespace CleanSlate.AgentRules;

/// <summary>
/// Hot-updates rule packs from a remote URL or local/network path with optional SHA256 check.
/// </summary>
public sealed class RulePackUpdater
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    public async Task<RulePackUpdateResult> UpdateFromUrlAsync(
        string url,
        string destinationPath,
        string? expectedSha256 = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return RulePackUpdateResult.Fail("E_HTTP", $"HTTP {(int)response.StatusCode}");
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            return ApplyBytes(bytes, destinationPath, expectedSha256, url);
        }
        catch (Exception ex)
        {
            return RulePackUpdateResult.Fail("E_UPDATE", ex.Message);
        }
    }

    public RulePackUpdateResult UpdateFromFile(string sourcePath, string destinationPath, string? expectedSha256 = null)
    {
        if (!File.Exists(sourcePath))
        {
            return RulePackUpdateResult.Fail("E_SRC", "Source rule pack not found.");
        }

        var bytes = File.ReadAllBytes(sourcePath);
        return ApplyBytes(bytes, destinationPath, expectedSha256, sourcePath);
    }

    private static RulePackUpdateResult ApplyBytes(byte[] bytes, string destinationPath, string? expectedSha256, string source)
    {
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(expectedSha256)
            && !actual.Equals(expectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
        {
            return RulePackUpdateResult.Fail("E_HASH", $"SHA256 mismatch: {actual}");
        }

        // Validate JSON before write
        try
        {
            JsonDocument.Parse(bytes);
        }
        catch (Exception ex)
        {
            return RulePackUpdateResult.Fail("E_JSON", ex.Message);
        }

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // Backup previous
        if (File.Exists(destinationPath))
        {
            File.Copy(destinationPath, destinationPath + ".bak", overwrite: true);
        }

        File.WriteAllBytes(destinationPath, bytes);
        return RulePackUpdateResult.Ok(destinationPath, actual, source);
    }

    /// <summary>CDN path entry that reuses validation/write pipeline.</summary>
    public RulePackUpdateResult ApplyForCdn(byte[] bytes, string destinationPath, string? expectedSha256, string source) =>
        ApplyBytes(bytes, destinationPath, expectedSha256, source);
}

public sealed record RulePackUpdateResult(
    bool Success,
    string? DestinationPath,
    string? Sha256,
    string? Source,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static RulePackUpdateResult Ok(string dest, string sha256, string source) =>
        new(true, dest, sha256, source, null, null);

    public static RulePackUpdateResult Fail(string code, string message) =>
        new(false, null, null, null, code, message);
}
