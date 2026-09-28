using CleanSlate.Core;
using CleanSlate.AgentRules;
using FluentAssertions;
using Xunit;

namespace CleanSlate.UnitTests;

public class AgentRuleLoaderTests
{
    private readonly AgentRuleLoader _loader = new();

    [Fact]
    public void Loads_sample_rule_pack()
    {
        var path = FindSample();
        var result = _loader.LoadFile(path);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Rules.Should().NotBeNull();
        result.Rules!.Count.Should().BeGreaterThanOrEqualTo(3);
        result.Rules.Select(r => r.AgentId).Should().Contain("claude-code");
    }

    [Fact]
    public void Forces_L4_L5_default_selected_false()
    {
        const string json = """
        {
          "schemaVersion": 1,
          "rules": [
            {
              "agentId": "demo",
              "displayName": "Demo Agent",
              "processNames": ["demo.exe"],
              "pathPatterns": ["%APPDATA%\\Demo\\**"],
              "layers": [
                { "name": "L1Cache", "risk": "Low", "defaultSelected": true },
                { "name": "L4Credential", "risk": "High", "defaultSelected": true },
                { "name": "L5Model", "risk": "High", "defaultSelected": true }
              ]
            }
          ]
        }
        """;

        var result = _loader.LoadJson(json);
        result.Success.Should().BeTrue(result.ErrorMessage);

        var layers = result.Rules!.Single().Layers;
        layers.Single(l => l.Name == "L1Cache").DefaultSelected.Should().BeTrue();
        layers.Single(l => l.Name == "L4Credential").DefaultSelected.Should().BeFalse();
        layers.Single(l => l.Name == "L5Model").DefaultSelected.Should().BeFalse();
        result.Warnings.Should().NotBeEmpty();
    }

    [Fact]
    public void Rejects_empty_rules()
    {
        var result = _loader.LoadJson("""{ "schemaVersion": 1, "rules": [] }""");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("E_RULE_INVALID");
    }

    [Fact]
    public void Rejects_bad_json()
    {
        var result = _loader.LoadJson("not-json");
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be("E_RULE_INVALID");
    }

    private static string FindSample()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            var candidate = Path.Combine(dir, "docs", "api", "agent-rules.sample.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            // Walk up from bin/Release/net8.0 → repo root
            var rootCandidate = Path.GetFullPath(Path.Combine(dir, string.Join("\\", Enumerable.Repeat("..", i)), "docs", "api", "agent-rules.sample.json"));
            if (File.Exists(rootCandidate))
            {
                return rootCandidate;
            }

            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }

        // Fallback: relative from workspace known path
        var fallback = @"C:\Users\Administrator\XiaomiMiMoProjects\.mimo-sessions\2026\09\27\windows-agent-agent\CleanSlate\docs\api\agent-rules.sample.json";
        if (File.Exists(fallback))
        {
            return fallback;
        }

        throw new FileNotFoundException("agent-rules.sample.json not found");
    }
}
