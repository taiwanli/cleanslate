using System.Text.Json;
using System.Text.Json.Serialization;
using CleanSlate.Core;

namespace CleanSlate.AgentRules;

/// <summary>
/// Loads Agent cleanup rule packs from JSON (schema: docs/api/agent-rules.schema.json).
/// </summary>
public sealed class AgentRuleLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AgentRuleLoadResult LoadFile(string path)
    {
        if (!File.Exists(path))
        {
            return AgentRuleLoadResult.Fail("E_RULE_NOT_FOUND", $"Rule pack not found: {path}");
        }

        try
        {
            var json = File.ReadAllText(path);
            return LoadJson(json);
        }
        catch (Exception ex)
        {
            return AgentRuleLoadResult.Fail("E_RULE_READ", ex.Message);
        }
    }

    public AgentRuleLoadResult LoadJson(string json)
    {
        AgentRulePackDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AgentRulePackDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return AgentRuleLoadResult.Fail("E_RULE_INVALID", ex.Message);
        }

        if (dto is null)
        {
            return AgentRuleLoadResult.Fail("E_RULE_INVALID", "Empty rule pack.");
        }

        if (dto.SchemaVersion < 1)
        {
            return AgentRuleLoadResult.Fail("E_RULE_INVALID", "schemaVersion must be >= 1.");
        }

        if (dto.Rules is null || dto.Rules.Count == 0)
        {
            return AgentRuleLoadResult.Fail("E_RULE_INVALID", "rules must be a non-empty array.");
        }

        var rules = new List<AgentCleanupRule>();
        var errors = new List<string>();

        foreach (var r in dto.Rules)
        {
            if (string.IsNullOrWhiteSpace(r.AgentId) || string.IsNullOrWhiteSpace(r.DisplayName))
            {
                errors.Add("rule missing agentId/displayName");
                continue;
            }

            if (r.Layers is null || r.Layers.Count == 0)
            {
                errors.Add($"{r.AgentId}: layers required");
                continue;
            }

            var layers = new List<AgentCleanupLayer>();
            foreach (var layer in r.Layers)
            {
                if (string.IsNullOrWhiteSpace(layer.Name))
                {
                    errors.Add($"{r.AgentId}: layer name required");
                    continue;
                }

                // Enforce safety: L4/L5 never default-selected.
                var defaultSelected = layer.DefaultSelected;
                if (IsCredentialOrModel(layer.Name) && defaultSelected)
                {
                    defaultSelected = false;
                    errors.Add($"{r.AgentId}/{layer.Name}: defaultSelected forced to false for safety");
                }

                layers.Add(new AgentCleanupLayer(
                    Name: layer.Name,
                    Risk: Enum.TryParse<RiskLevel>(layer.Risk, out var risk) ? risk : RiskLevel.Medium,
                    DefaultSelected: defaultSelected,
                    Notes: layer.Notes,
                    RelativePath: layer.RelativePath));
            }

            rules.Add(new AgentCleanupRule(
                AgentId: r.AgentId.Trim(),
                DisplayName: r.DisplayName.Trim(),
                ProcessNames: r.ProcessNames ?? [],
                PathPatterns: r.PathPatterns ?? [],
                Layers: layers));
        }

        if (rules.Count == 0)
        {
            return AgentRuleLoadResult.Fail("E_RULE_INVALID", "No valid rules after parse.");
        }

        return AgentRuleLoadResult.Ok(rules, errors);
    }

    private static bool IsCredentialOrModel(string layerName) =>
        layerName is "L4Credential" or "L5Model" or "L4" or "L5";

    private sealed class AgentRulePackDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("rules")]
        public List<AgentRuleDto>? Rules { get; set; }
    }

    private sealed class AgentRuleDto
    {
        [JsonPropertyName("agentId")]
        public string? AgentId { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("processNames")]
        public List<string>? ProcessNames { get; set; }

        [JsonPropertyName("pathPatterns")]
        public List<string>? PathPatterns { get; set; }

        [JsonPropertyName("layers")]
        public List<AgentLayerDto>? Layers { get; set; }
    }

    private sealed class AgentLayerDto
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("relativePath")]
        public string? RelativePath { get; set; }

        [JsonPropertyName("risk")]
        public string? Risk { get; set; }

        [JsonPropertyName("defaultSelected")]
        public bool DefaultSelected { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }
    }
}

public sealed record AgentRuleLoadResult(
    bool Success,
    IReadOnlyList<AgentCleanupRule>? Rules,
    IReadOnlyList<string> Warnings,
    string? ErrorCode,
    string? ErrorMessage)
{
    public static AgentRuleLoadResult Ok(IReadOnlyList<AgentCleanupRule> rules, IReadOnlyList<string> warnings) =>
        new(true, rules, warnings, null, null);

    public static AgentRuleLoadResult Fail(string code, string message) =>
        new(false, null, [], code, message);
}

