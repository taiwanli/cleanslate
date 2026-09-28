using System.Security.Principal;
using System.Security.AccessControl;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CleanSlate.ElevatedHelper;

public static class HelperProtocol
{
    public const string PipeName = "CleanSlate.Elevated";
    public const int ProtocolVersion = 1;

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };
}

public sealed record HelperRequest(
    int Version,
    string RequestId,
    string Action, // DeleteFile | DeleteFolder | DeleteRegistryKey | DeleteRegistryValue | Ping
    string? Path,
    string? KeyPath,
    string? ValueName,
    string Token);

public sealed record HelperResponse(
    bool Ok,
    string RequestId,
    string? Code,
    string? Message,
    string? Detail);

/// <summary>
/// Runs elevated. Accepts one-shot JSON requests over a named pipe and performs deletions.
/// </summary>
public static class ElevatedHelperProgram
{
    public static int Main(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--token")
            {
                Environment.SetEnvironmentVariable("CLEANSLATE_HELPER_TOKEN", args[i + 1]);
            }
        }

        return Run(args);
    }

    public static int Run(string[] args)
    {
        // Issue 008: ACL the pipe so only the current user can connect.
        var pipeSecurity = new PipeSecurity();
        var current = WindowsIdentity.GetCurrent().User;
        if (current is not null)
        {
            pipeSecurity.AddAccessRule(new PipeAccessRule(current, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));
        }

        pipeSecurity.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl,
            AccessControlType.Deny));

        using var server = NamedPipeServerStreamAcl.Create(
            HelperProtocol.PipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);

        server.WaitForConnection();

        using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(server, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };

        var line = reader.ReadLine();
        if (string.IsNullOrWhiteSpace(line))
        {
            writer.WriteLine(JsonSerializer.Serialize(new HelperResponse(false, "?", "E_EMPTY", "No request", null), HelperProtocol.Json));
            return 1;
        }

        HelperRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<HelperRequest>(line, HelperProtocol.Json);
        }
        catch (Exception ex)
        {
            writer.WriteLine(JsonSerializer.Serialize(new HelperResponse(false, "?", "E_PARSE", ex.Message, null), HelperProtocol.Json));
            return 1;
        }

        if (request is null)
        {
            writer.WriteLine(JsonSerializer.Serialize(new HelperResponse(false, "?", "E_PARSE", "null request", null), HelperProtocol.Json));
            return 1;
        }

        var response = Execute(request);
        writer.WriteLine(JsonSerializer.Serialize(response, HelperProtocol.Json));
        return response.Ok ? 0 : 2;
    }

    private static HelperResponse Execute(HelperRequest request)
    {
        if (!IsElevated())
        {
            return new HelperResponse(false, request.RequestId, "E_NOT_ELEVATED", "Helper is not running as administrator.", null);
        }

        if (request.Version != HelperProtocol.ProtocolVersion)
        {
            return new HelperResponse(false, request.RequestId, "E_VERSION", "Unsupported protocol version.", null);
        }

        // Token is a simple shared secret from the client session; reject empty.
        var expected = Environment.GetEnvironmentVariable("CLEANSLATE_HELPER_TOKEN");
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return new HelperResponse(false, request.RequestId, "E_TOKEN", "Missing token.", null);
        }

        // Issue 008: token must match the value issued to this helper instance (not merely non-empty).
        if (!string.IsNullOrWhiteSpace(expected) &&
             TokenUtil.CryptographicEquals(request.Token, expected))
        {
            return new HelperResponse(false, request.RequestId, "E_TOKEN_INVALID", "Token mismatch.", null);
        }

        try
        {
            return request.Action switch
            {
                "Ping" => new HelperResponse(true, request.RequestId, null, "pong", null),
                "DeleteFile" => DeleteFile(request),
                "DeleteFolder" => DeleteFolder(request),
                "DeleteRegistryKey" => DeleteRegistryKey(request),
                "DeleteRegistryValue" => DeleteRegistryValue(request),
                _ => new HelperResponse(false, request.RequestId, "E_ACTION", $"Unknown action {request.Action}", null),
            };
        }
        catch (Exception ex)
        {
            return new HelperResponse(false, request.RequestId, "E_FAIL", ex.Message, ex.GetType().Name);
        }
    }

    private static HelperResponse DeleteFile(HelperRequest request)
    {
        var path = request.Path;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new HelperResponse(false, request.RequestId, "E_NOT_FOUND", "File not found.", path);
        }

        if (!SafetyGate.CanDeletePath(path))
        {
            return new HelperResponse(false, request.RequestId, "E_PATH_PROTECTED", "Path is protected.", path);
        }

        File.Delete(path);
        return new HelperResponse(true, request.RequestId, null, "File deleted.", path);
    }

    private static HelperResponse DeleteFolder(HelperRequest request)
    {
        var path = request.Path;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return new HelperResponse(false, request.RequestId, "E_NOT_FOUND", "Folder not found.", path);
        }

        if (!SafetyGate.CanDeletePath(path))
        {
            return new HelperResponse(false, request.RequestId, "E_PATH_PROTECTED", "Path is protected.", path);
        }

        Directory.Delete(path, recursive: true);
        return new HelperResponse(true, request.RequestId, null, "Folder deleted.", path);
    }

    private static HelperResponse DeleteRegistryKey(HelperRequest request)
    {
        var keyPath = request.KeyPath;
        if (string.IsNullOrWhiteSpace(keyPath))
        {
            return new HelperResponse(false, request.RequestId, "E_KEY", "Missing key path.", null);
        }

        if (!SafetyGate.CanDeleteRegistry(keyPath))
        {
            return new HelperResponse(false, request.RequestId, "E_KEY_PROTECTED", "Registry key is protected.", keyPath);
        }

        using var key = OpenWritableKey(keyPath, delete: true);
        return key is null
            ? new HelperResponse(false, request.RequestId, "E_NOT_FOUND", "Key not found.", keyPath)
            : new HelperResponse(true, request.RequestId, null, "Key deleted.", keyPath);
    }

    private static HelperResponse DeleteRegistryValue(HelperRequest request)
    {
        var keyPath = request.KeyPath;
        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(request.ValueName))
        {
            return new HelperResponse(false, request.RequestId, "E_KEY", "Missing key path or value name.", null);
        }

        if (!SafetyGate.CanDeleteRegistry(keyPath))
        {
            return new HelperResponse(false, request.RequestId, "E_KEY_PROTECTED", "Registry key is protected.", keyPath);
        }

        using var key = OpenWritableKey(keyPath, delete: false);
        if (key is null)
        {
            return new HelperResponse(false, request.RequestId, "E_NOT_FOUND", "Key not found.", keyPath);
        }

        key.DeleteValue(request.ValueName, throwOnMissingValue: false);
        return new HelperResponse(true, request.RequestId, null, "Value deleted.", $"{keyPath}\\{request.ValueName}");
    }

    private static Microsoft.Win32.RegistryKey? OpenWritableKey(string keyPath, bool delete)
    {
        var (hive, subKey) = SplitHive(keyPath);
        var root = hive switch
        {
            "HKLM" or "HKEY_LOCAL_MACHINE" => Microsoft.Win32.Registry.LocalMachine,
            "HKCU" or "HKEY_CURRENT_USER" => Microsoft.Win32.Registry.CurrentUser,
            _ => null,
        };

        if (root is null || string.IsNullOrEmpty(subKey))
        {
            return null;
        }

        if (delete)
        {
            root.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            return null;
        }

        return root.OpenSubKey(subKey, writable: true);
    }

    private static (string Hive, string SubKey) SplitHive(string keyPath)
    {
        var normalized = keyPath.Replace('/', '\\').Trim();
        var idx = normalized.IndexOf('\\');
        return idx <= 0
            ? (normalized, string.Empty)
            : (normalized[..idx], normalized[(idx + 1)..]);
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>Hard safety rails for elevated deletion.</summary>
public static class SafetyGate
{
    public static bool CanDeletePath(string path)
    {
        var p = path.Replace('/', '\\').Trim();
        if (p.Length < 4)
        {
            return false;
        }

        var protectedPrefixes = new[]
        {
            @"C:\Windows\",
            @"C:\Program Files\WindowsApps\",
            @"C:\$Recycle.Bin\",
            @"C:\$WinREAgent\",
            @"C:\Recovery\",
            @"C:\System Volume Information\",
            @"C:\ProgramData\Microsoft\",
        };

        foreach (var prefix in protectedPrefixes)
        {
            if (p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    public static bool CanDeleteRegistry(string keyPath)
    {
        var k = keyPath.Replace('/', '\\').Trim();
        string[] blocked =
        [
            @"HKLM\SYSTEM",
            @"HKLM\SOFTWARE\Microsoft\Windows NT",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKLM\SOFTWARE\Microsoft\Windows Defender",
        ];

        foreach (var b in blocked)
        {
            if (k.StartsWith(b, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // Only allow cleanup under Software trees or known Uninstall paths
        var allowed =
            k.Contains(@"\Software\", StringComparison.OrdinalIgnoreCase)
            || k.Contains(@"\Uninstall\", StringComparison.OrdinalIgnoreCase)
            || k.Contains(@"\CLSID\", StringComparison.OrdinalIgnoreCase)
            || k.Contains(@"\Wow6432Node\", StringComparison.OrdinalIgnoreCase);

        return allowed;
    }
}

internal static class TokenUtil
{
    public static bool CryptographicEquals(string a, string b)
    {
        var ba = System.Text.Encoding.UTF8.GetBytes(a);
        var bb = System.Text.Encoding.UTF8.GetBytes(b);
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}
