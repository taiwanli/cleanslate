using System.Diagnostics;

namespace CleanSlate.Leftover;

/// <summary>
/// Disables or removes browser extensions (Chromium folder-based; Firefox best-effort).
/// </summary>
public sealed class BrowserExtensionRemover
{
    public ExtensionActionResult DisableChromium(string installPath)
    {
        try
        {
            // Chromium loads extensions from profile Extensions\<id>\<ver>\manifest.json
            // "Disabling" by renaming the extension folder is a reversible local technique.
            if (!Directory.Exists(installPath))
            {
                return ExtensionActionResult.Fail(installPath, "E_NOT_FOUND");
            }

            var disabled = installPath + ".disabled-cleanslate";
            if (Directory.Exists(disabled))
            {
                return ExtensionActionResult.FromOk(installPath, "Already disabled");
            }

            Directory.Move(installPath, disabled);
            return ExtensionActionResult.FromOk(installPath, "Disabled (folder renamed)");
        }
        catch (Exception ex)
        {
            return ExtensionActionResult.Fail(installPath, "E_DISABLE", ex.Message);
        }
    }

    public ExtensionActionResult EnableChromium(string installPath)
    {
        try
        {
            var disabled = installPath.EndsWith(".disabled-cleanslate", StringComparison.OrdinalIgnoreCase)
                ? installPath
                : installPath + ".disabled-cleanslate";
            var enabled = disabled.EndsWith(".disabled-cleanslate", StringComparison.OrdinalIgnoreCase)
                ? disabled[..^".disabled-cleanslate".Length]
                : disabled;

            if (!Directory.Exists(disabled))
            {
                return ExtensionActionResult.Fail(installPath, "E_NOT_FOUND");
            }

            Directory.Move(disabled, enabled);
            return ExtensionActionResult.FromOk(enabled, "Enabled");
        }
        catch (Exception ex)
        {
            return ExtensionActionResult.Fail(installPath, "E_ENABLE", ex.Message);
        }
    }

    public ExtensionActionResult Remove(BrowserExtension extension)
    {
        if (string.IsNullOrWhiteSpace(extension.InstallPath))
        {
            return ExtensionActionResult.Fail(extension.Name, "E_PATH");
        }

        if (extension.Browser.Equals("Firefox", StringComparison.OrdinalIgnoreCase))
        {
            // Firefox manages extensions via extensions.json; do not delete profile dirs blindly.
            return ExtensionActionResult.Fail(extension.InstallPath, "E_FIREFOX_MANUAL",
                "Firefox extensions should be removed from about:addons");
        }

        try
        {
            if (Directory.Exists(extension.InstallPath))
            {
                Directory.Delete(extension.InstallPath, recursive: true);
                return ExtensionActionResult.FromOk(extension.InstallPath, "Removed");
            }

            return ExtensionActionResult.Fail(extension.InstallPath, "E_NOT_FOUND");
        }
        catch (Exception ex)
        {
            return ExtensionActionResult.Fail(extension.InstallPath, "E_REMOVE", ex.Message);
        }
    }
}

public sealed record ExtensionActionResult(bool Ok, string Path, string? Code, string? Message)
{
    public static ExtensionActionResult FromOk(string path, string message) => new(true, path, null, message);

    public static ExtensionActionResult Fail(string path, string code, string? message = null) =>
        new(false, path, code, message ?? code);
}
