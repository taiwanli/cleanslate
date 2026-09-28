using CleanSlate.Core;

namespace CleanSlate.Safety;

/// <summary>
/// Decides whether a path/key is protected and how risky a cleanup item is.
/// Never delete system or user-generated content without explicit high-risk confirmation.
/// </summary>
public sealed class PathSafetyPolicy
{
    private static readonly string[] ProtectedRootPrefixes =
    [
        @"C:\Windows\",
        @"C:\Program Files\WindowsApps\",
        @"C:\$Recycle.Bin\",
        @"C:\$WinREAgent\",
        @"C:\Recovery\",
        @"C:\System Volume Information\",
        @"C:\ProgramData\Microsoft\Windows\Start Menu\",
        @"C:\ProgramData\Microsoft\Windows\Templates\",
        @"C:\Users\Public\Documents\",
        @"C:\Users\Public\Desktop\",
        @"C:\Users\Public\Music\",
        @"C:\Users\Public\Pictures\",
        @"C:\Users\Public\Videos\",
    ];

    /// <summary>Any path under these segments is protected (user content).</summary>
    private static readonly string[] ProtectedKnownFolderMarkers =
    [
        @"\Desktop\",
        @"\Documents\",
        @"\Downloads\",
        @"\Pictures\",
        @"\Music\",
        @"\Videos\",
        @"\OneDrive\",
    ];

    private static readonly string[] SharedRuntimeMarkers =
    [
        @"\Common Files\",
        @"\Windows Kits\",
        @"\dotnet\",
        @"\Microsoft.NET\",
        @"\Package Cache\",
    ];

    public PathProtectionLevel GetProtection(string pathOrKey)
    {
        if (string.IsNullOrWhiteSpace(pathOrKey))
        {
            return PathProtectionLevel.Protected;
        }

        var normalized = NormalizePath(pathOrKey);

        if (IsProtectedRoot(normalized))
        {
            return PathProtectionLevel.Protected;
        }

        if (IsUserKnownFolder(normalized))
        {
            return PathProtectionLevel.Protected;
        }

        if (IsSharedRuntime(normalized))
        {
            return PathProtectionLevel.Shared;
        }

        return PathProtectionLevel.None;
    }

    public RiskLevel EvaluateRisk(string pathOrKey, LeftoverKind kind, int confidence)
    {
        var protection = GetProtection(pathOrKey);

        return protection switch
        {
            PathProtectionLevel.Protected => RiskLevel.Protected,
            PathProtectionLevel.Shared => RiskLevel.High,
            _ when confidence >= 70 && kind is LeftoverKind.File or LeftoverKind.Folder => RiskLevel.Low,
            _ when confidence >= 70 && kind is LeftoverKind.Registry or LeftoverKind.Shortcut => RiskLevel.Medium,
            _ when confidence >= 40 => RiskLevel.Medium,
            _ => RiskLevel.High,
        };
    }

    public bool CanDefaultSelect(RiskLevel risk, int confidence)
    {
        return risk == RiskLevel.Low && confidence >= 70;
    }

    public bool IsDeletionAllowed(string pathOrKey)
    {
        return GetProtection(pathOrKey) != PathProtectionLevel.Protected;
    }

    private static bool IsProtectedRoot(string normalized)
    {
        foreach (var prefix in ProtectedRootPrefixes)
        {
            if (normalized.StartsWith(NormalizePath(prefix), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Drive root itself (C:\ or D:\)
        if (normalized.Length == 3 && normalized[1] == ':' && normalized[2] == '\\')
        {
            return true;
        }

        return false;
    }

    private static bool IsUserKnownFolder(string normalized)
    {
        // Protect entire Desktop/Documents/Downloads/... trees — not just the folder root.
        foreach (var marker in ProtectedKnownFolderMarkers)
        {
            if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Also match path ending with the folder name (e.g. ...\Desktop)
            if (normalized.EndsWith(marker.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSharedRuntime(string normalized)
    {
        foreach (var marker in SharedRuntimeMarkers)
        {
            if (normalized.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizePath(string path)
    {
        var p = path.Trim().Replace('/', '\\');

        // Preserve UNC prefix (\\server\share) while collapsing other duplicate separators
        if (p.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var rest = p[2..];
            while (rest.Contains(@"\\", StringComparison.Ordinal))
            {
                rest = rest.Replace(@"\\", @"\", StringComparison.Ordinal);
            }

            return @"\\" + rest;
        }

        while (p.Contains(@"\\", StringComparison.Ordinal))
        {
            p = p.Replace(@"\\", @"\", StringComparison.Ordinal);
        }

        return p;
    }
}

public enum PathProtectionLevel
{
    None,
    Shared,
    Protected,
}
