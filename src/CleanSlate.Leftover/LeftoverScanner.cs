using System.Text.RegularExpressions;
using CleanSlate.Core;
using CleanSlate.Safety;

namespace CleanSlate.Leftover;

/// <summary>
/// Scans file-system leftovers under known app data roots using simple name matching.
/// Registry/service scanning is a follow-up; this covers MVP FS residuals.
/// </summary>
public sealed class LeftoverScanner
{
    private readonly PathMatcher _matcher = new();

    public IReadOnlyList<LeftoverItem> ScanDirectories(
        IEnumerable<string> rootDirectories,
        IEnumerable<string> nameHints,
        CancellationToken cancellationToken = default)
    {
        var hints = nameHints
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (hints.Count == 0)
        {
            return [];
        }

        var items = new List<LeftoverItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in rootDirectories)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();

            foreach (var dir in EnumerateSafe(root, directories: true))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesAny(dir, hints))
                {
                    continue;
                }

                if (seen.Add(dir))
                {
                    items.Add(CreateItem(LeftoverKind.Folder, dir, hints));
                }
            }

            foreach (var file in EnumerateSafe(root, directories: false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!MatchesAny(file, hints))
                {
                    continue;
                }

                if (seen.Add(file))
                {
                    items.Add(CreateItem(LeftoverKind.File, file, hints));
                }
            }
        }

        return items
            .OrderBy(i => i.Kind)
            .ThenBy(i => i.PathOrKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private LeftoverItem CreateItem(LeftoverKind kind, string path, IReadOnlyList<string> hints)
    {
        var policy = new PathSafetyPolicy();
        var matched = hints.First(h => MatchesAny(path, [h]));
        var confidence = ComputeConfidence(path, matched);
        var risk = policy.EvaluateRisk(path, kind, confidence);
        var protection = policy.GetProtection(path);

        return new LeftoverItem(
            Id: $"{kind}:{path}",
            Kind: kind,
            PathOrKey: path,
            MatchReason: $"Name matches '{matched}'",
            Confidence: confidence,
            Risk: risk,
            DefaultSelected: policy.CanDefaultSelect(risk, confidence),
            Recoverable: protection == PathProtectionLevel.Protected
                ? Recoverability.None
                : Recoverability.Quarantine);
    }

    private static int ComputeConfidence(string path, string hint)
    {
        var name = Path.GetFileName(path.TrimEnd('\\')) ?? string.Empty;
        if (name.Equals(hint, StringComparison.OrdinalIgnoreCase))
        {
            return 90;
        }

        if (name.Contains(hint, StringComparison.OrdinalIgnoreCase))
        {
            return 75;
        }

        return 50;
    }

    private bool MatchesAny(string path, IReadOnlyList<string> hints)
    {
        foreach (var hint in hints)
        {
            if (_matcher.Matches(path, hint))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> EnumerateSafe(string root, bool directories)
    {
        var option = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
        };

        IEnumerable<string> enumerable;
        try
        {
            enumerable = directories
                ? Directory.EnumerateDirectories(root, "*", option)
                : Directory.EnumerateFiles(root, "*", option);
        }
        catch
        {
            yield break;
        }

        foreach (var path in enumerable)
        {
            yield return path;
        }
    }
}

internal sealed class PathMatcher
{
    private static readonly Regex EnvToken = new(@"%([A-Za-z0-9_()]+)%", RegexOptions.Compiled);

    public bool Matches(string path, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }

        var expanded = Expand(pattern);
        var normalizedPath = path.Replace('/', '\\');
        var normalizedPattern = expanded.Replace('/', '\\');

        if (normalizedPattern.Contains('*'))
        {
            var regex = "^" + Regex.Escape(normalizedPattern)
                .Replace(@"\*\*", ".*", StringComparison.Ordinal)
                .Replace(@"\*", @"[^\\]*", StringComparison.Ordinal) + "$";
            return Regex.IsMatch(normalizedPath, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return normalizedPath.Contains(normalizedPattern, StringComparison.OrdinalIgnoreCase);
    }

    private static string Expand(string pattern)
    {
        return EnvToken.Replace(pattern, m =>
        {
            var name = m.Groups[1].Value;
            var value = Environment.GetEnvironmentVariable(name);
            return string.IsNullOrEmpty(value) ? m.Value : value.TrimEnd('\\');
        });
    }
}
