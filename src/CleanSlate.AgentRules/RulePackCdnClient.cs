using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanSlate.AgentRules;

/// <summary>
/// Official rule-pack CDN source: versioned manifest + package with SHA256 integrity.
/// Default endpoints are overridable via policy/local config.
/// </summary>
public sealed class RulePackCdnClient
{
    /// <summary>Primary official CDN (placeholder corporate/public bucket). Override in production.</summary>
    public const string DefaultManifestUrl = "https://cdn.cleanslate.app/rules/manifest.json";

    public const string FallbackManifestUrl = "https://raw.githubusercontent.com/cleanslate-rules/agent-rules/main/manifest.json";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ManifestUrl { get; }

    public RulePackCdnClient(string? manifestUrl = null)
    {
        ManifestUrl = string.IsNullOrWhiteSpace(manifestUrl) ? DefaultManifestUrl : manifestUrl;
    }

    public async Task<RulePackCdnCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var manifestJson = await FetchStringAsync(ManifestUrl, ct);
            if (manifestJson is null)
            {
                manifestJson = await FetchStringAsync(FallbackManifestUrl, ct);
            }

            if (manifestJson is null)
            {
                return RulePackCdnCheckResult.Fail("E_CDN_UNREACHABLE", $"Cannot reach manifest: {ManifestUrl}");
            }

            var manifest = JsonSerializer.Deserialize<RulePackManifest>(manifestJson, Json);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.DownloadUrl))
            {
                return RulePackCdnCheckResult.Fail("E_MANIFEST", "Invalid CDN manifest.");
            }

            return RulePackCdnCheckResult.Ok(manifest, manifestJson);
        }
        catch (Exception ex)
        {
            return RulePackCdnCheckResult.Fail("E_CDN", ex.Message);
        }
    }

    public async Task<RulePackUpdateResult> UpdateAsync(
        string destinationPath,
        string? currentSha256 = null,
        CancellationToken ct = default)
    {
        var check = await CheckAsync(ct);
        if (!check.Success || check.Manifest is null)
        {
            return RulePackUpdateResult.Fail(check.ErrorCode ?? "E_CDN", check.ErrorMessage ?? "CDN check failed");
        }

        var manifest = check.Manifest;
        if (!string.IsNullOrWhiteSpace(currentSha256)
            && !string.IsNullOrWhiteSpace(manifest.Sha256)
            && currentSha256.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            return RulePackUpdateResult.Ok(destinationPath, manifest.Sha256!, "cdn:up-to-date");
        }

        var bytes = await FetchBytesAsync(manifest.DownloadUrl!, ct);
        if (bytes is null)
        {
            return RulePackUpdateResult.Fail("E_DOWNLOAD", $"Cannot download {manifest.DownloadUrl}");
        }

        var updater = new RulePackUpdater();
        return updater.ApplyForCdn(bytes, destinationPath, manifest.Sha256, "cdn:" + manifest.DownloadUrl);
    }

    public static string? ComputeLocalSha256(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        using var stream = File.OpenRead(filePath);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task<string?> FetchStringAsync(string url, CancellationToken ct)
    {
        var bytes = await FetchBytesAsync(url, ct);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static async Task<byte[]?> FetchBytesAsync(string url, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}

public sealed record RulePackManifest(
    string Version,
    string? DownloadUrl,
    string? Sha256,
    DateTime? PublishedUtc,
    string? Notes);

public sealed record RulePackCdnCheckResult(
    bool Success,
    RulePackManifest? Manifest,
    string? RawManifestJson,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static RulePackCdnCheckResult Ok(RulePackManifest manifest, string raw) =>
        new(true, manifest, raw, null, null);

    public static RulePackCdnCheckResult Fail(string code, string message) =>
        new(false, null, null, code, message);
}
