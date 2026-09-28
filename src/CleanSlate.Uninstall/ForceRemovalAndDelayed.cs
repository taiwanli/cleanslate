using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CleanSlate.Uninstall;

/// <summary>
/// Force-removal wizard: traces leftovers for an app from a path/shortcut/name without running uninstaller.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ForceRemovalPlanner
{
    public ForceRemovalPlan Build(string nameOrPath, IEnumerable<string> extraHints, CancellationToken cancellationToken = default)
    {
        var hints = new List<string>();
        if (!string.IsNullOrWhiteSpace(nameOrPath))
        {
            hints.Add(Path.GetFileNameWithoutExtension(nameOrPath.Trim().TrimEnd('\\')));
            hints.Add(Path.GetFileName(nameOrPath.Trim().TrimEnd('\\')));
        }

        hints.AddRange(extraHints.Where(h => !string.IsNullOrWhiteSpace(h)).Select(h => h.Trim()));
        hints = hints.Distinct(StringComparer.OrdinalIgnoreCase).Where(h => h.Length >= 3).ToList();

        var targets = new List<ForceRemovalTarget>();

        // File system roots
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                 })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var hint in hints)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dir = Path.Combine(root, hint);
                if (Directory.Exists(dir))
                {
                    targets.Add(new ForceRemovalTarget(ForceRemovalKind.Folder, dir, hint));
                }

                var file = Path.Combine(root, hint + ".log");
                if (File.Exists(file))
                {
                    targets.Add(new ForceRemovalTarget(ForceRemovalKind.File, file, hint));
                }
            }
        }

        // Registry Software keys
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            foreach (var softPath in new[] { "SOFTWARE", @"SOFTWARE\WOW6432Node" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var soft = hive.OpenSubKey(softPath);
                if (soft is null)
                {
                    continue;
                }

                foreach (var vendor in soft.GetSubKeyNames())
                {
                    foreach (var hint in hints)
                    {
                        // Issue 009: exact or word-boundary only (avoid substring false positives)
                        if (IsNameMatch(vendor, hint))
                        {
                            targets.Add(new ForceRemovalTarget(ForceRemovalKind.RegistryKey, $"{hive.Name}\\{softPath}\\{vendor}", hint));
                        }
                    }
                }
            }
        }

        // Uninstall entries that look broken / matching name
        using (var uninstall = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
        {
            if (uninstall is not null)
            {
                foreach (var sub in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(sub);
                    var display = key?.GetValue("DisplayName") as string;
                    if (display is null)
                    {
                        continue;
                    }

                    if (hints.Any(h => display.Contains(h, StringComparison.OrdinalIgnoreCase)))
                    {
                        targets.Add(new ForceRemovalTarget(
                            ForceRemovalKind.UninstallEntry,
                            $@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{sub}",
                            display));
                    }
                }
            }
        }

        return new ForceRemovalPlan(
            Query: nameOrPath,
            Hints: hints,
            Targets: targets.DistinctBy(t => t.Path, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static bool IsNameMatch(string value, string hint)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(hint))
        {
            return false;
        }

        if (value.Equals(hint, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return System.Text.RegularExpressions.Regex.IsMatch(
            value,
            "\\b" + System.Text.RegularExpressions.Regex.Escape(hint) + "\\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}

public enum ForceRemovalKind
{
    Folder,
    File,
    RegistryKey,
    UninstallEntry,
}

public sealed record ForceRemovalTarget(ForceRemovalKind Kind, string Path, string Hint);

public sealed record ForceRemovalPlan(string Query, IReadOnlyList<string> Hints, IReadOnlyList<ForceRemovalTarget> Targets);

/// <summary>
/// Schedules deletion of locked files on next reboot via PendingFileRenameOperations.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DelayedFileDeleter
{
    public bool ScheduleDelete(string path, bool isFolder = false)
    {
        try
        {
            var normalized = path.Replace('/', '\\');
            if (!File.Exists(normalized) && !Directory.Exists(normalized))
            {
                return false;
            }

            // Mark delete-on-reboot for files; folders need children first
            if (isFolder && Directory.Exists(normalized))
            {
                var any = false;
                foreach (var file in Directory.EnumerateFiles(normalized, "*", SearchOption.AllDirectories))
                {
                    any |= ScheduleFile(file);
                }

                return any;
            }

            return ScheduleFile(normalized);
        }
        catch
        {
            return false;
        }
    }

    public IReadOnlyList<string> ListPending()
    {
        var list = new List<string>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager");
            var value = key?.GetValue("PendingFileRenameOperations") as string[];
            if (value is not null)
            {
                list.AddRange(value.Where(v => !string.IsNullOrWhiteSpace(v)));
            }
        }
        catch
        {
            // ignore
        }

        return list;
    }

    private static bool ScheduleFile(string path)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Session Manager",
                writable: true);
            if (key is null)
            {
                return false;
            }

            var existing = key.GetValue("PendingFileRenameOperations") as string[] ?? [];
            // REG_MULTI_SZ pairs: source = \??\C:\path , dest = "" (empty) means delete on reboot.
            // Do NOT embed '\0' in entries — MultiString already null-separates.
            var src = @"\??\" + path.TrimEnd('\\');
            var dst = string.Empty;

            for (var i = 0; i < existing.Length - 1; i += 2)
            {
                if (existing[i].Equals(src, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            var next = existing.Concat(new[] { src, dst }).ToArray();
            key.SetValue("PendingFileRenameOperations", next, RegistryValueKind.MultiString);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
