using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace CleanSlate.ElevatedHelper;

/// <summary>
/// Client that launches the elevated helper (when needed) and sends one-shot delete requests.
/// Falls back to same-process deletion when already elevated.
/// </summary>
public sealed class ElevatedCleanupClient
{
    private readonly string? _helperExePath;
    private readonly string _token;
    private readonly DateTime _tokenCreatedUtc = DateTime.UtcNow;

    public ElevatedCleanupClient(string? helperExePath = null, string? token = null)
    {
        _helperExePath = helperExePath;
        // Issue 008: high-entropy session token
        _token = token ?? Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    }

    public async Task<HelperResponse> DeleteFileAsync(string path, CancellationToken ct = default) =>
        await SendAsync(new HelperRequest(HelperProtocol.ProtocolVersion, Guid.NewGuid().ToString("N"), "DeleteFile", path, null, null, _token), ct);

    public async Task<HelperResponse> DeleteFolderAsync(string path, CancellationToken ct = default) =>
        await SendAsync(new HelperRequest(HelperProtocol.ProtocolVersion, Guid.NewGuid().ToString("N"), "DeleteFolder", path, null, null, _token), ct);

    public async Task<HelperResponse> DeleteRegistryKeyAsync(string keyPath, CancellationToken ct = default) =>
        await SendAsync(new HelperRequest(HelperProtocol.ProtocolVersion, Guid.NewGuid().ToString("N"), "DeleteRegistryKey", null, keyPath, null, _token), ct);

    public async Task<HelperResponse> DeleteRegistryValueAsync(string keyPath, string valueName, CancellationToken ct = default) =>
        await SendAsync(new HelperRequest(HelperProtocol.ProtocolVersion, Guid.NewGuid().ToString("N"), "DeleteRegistryValue", null, keyPath, valueName, _token), ct);

    private async Task<HelperResponse> SendAsync(HelperRequest request, CancellationToken ct)
    {
        // Prefer in-process when elevated
        if (IsCurrentProcessElevated())
        {
            return ExecuteLocal(request);
        }

        if (string.IsNullOrWhiteSpace(_helperExePath) || !File.Exists(_helperExePath))
        {
            return new HelperResponse(false, request.RequestId, "E_HELPER_MISSING", "Elevated helper executable not found.", _helperExePath);
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _helperExePath,
                Arguments = "--token " + _token,
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null)
            {
                return new HelperResponse(false, request.RequestId, "E_HELPER_START", "Failed to start elevated helper.", null);
            }

            await using var client = new NamedPipeClientStream(".", HelperProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(TimeSpan.FromSeconds(15), ct);

            var json = JsonSerializer.Serialize(request, HelperProtocol.Json) + "\n";
            var bytes = Encoding.UTF8.GetBytes(json);
            await client.WriteAsync(bytes, ct);
            await client.FlushAsync(ct);

            using var reader = new StreamReader(client, Encoding.UTF8);
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line))
            {
                return new HelperResponse(false, request.RequestId, "E_EMPTY_RESPONSE", "Helper returned empty response.", null);
            }

            var response = JsonSerializer.Deserialize<HelperResponse>(line, HelperProtocol.Json);
            return response ?? new HelperResponse(false, request.RequestId, "E_PARSE", "Invalid helper response.", line);
        }
        catch (Exception ex)
        {
            return new HelperResponse(false, request.RequestId, "E_HELPER_FAIL", ex.Message, ex.GetType().Name);
        }
    }

    private static HelperResponse ExecuteLocal(HelperRequest request)
    {
        // Mirror helper SafetyGate behavior without IPC
        if (!SafetyGate.CanDeletePath(request.Path ?? string.Empty) &&
            (request.Action == "DeleteFile" || request.Action == "DeleteFolder"))
        {
            return new HelperResponse(false, request.RequestId, "E_PATH_PROTECTED", "Path is protected.", request.Path);
        }

        try
        {
            switch (request.Action)
            {
                case "DeleteFile" when request.Path is not null && File.Exists(request.Path):
                    if (!SafetyGate.CanDeletePath(request.Path))
                    {
                        return new HelperResponse(false, request.RequestId, "E_PATH_PROTECTED", "Path is protected.", request.Path);
                    }

                    File.Delete(request.Path);
                    return new HelperResponse(true, request.RequestId, null, "File deleted.", request.Path);

                case "DeleteFolder" when request.Path is not null && Directory.Exists(request.Path):
                    if (!SafetyGate.CanDeletePath(request.Path))
                    {
                        return new HelperResponse(false, request.RequestId, "E_PATH_PROTECTED", "Path is protected.", request.Path);
                    }

                    Directory.Delete(request.Path, recursive: true);
                    return new HelperResponse(true, request.RequestId, null, "Folder deleted.", request.Path);

                case "DeleteRegistryKey" when request.KeyPath is not null:
                    return DeleteRegKeyLocal(request);

                case "DeleteRegistryValue" when request.KeyPath is not null && request.ValueName is not null:
                    return DeleteRegValueLocal(request);

                default:
                    return new HelperResponse(false, request.RequestId, "E_NOT_FOUND", "Target not found or unsupported.", request.Path ?? request.KeyPath);
            }
        }
        catch (Exception ex)
        {
            return new HelperResponse(false, request.RequestId, "E_FAIL", ex.Message, ex.GetType().Name);
        }
    }

    private static HelperResponse DeleteRegKeyLocal(HelperRequest request)
    {
        if (!SafetyGate.CanDeleteRegistry(request.KeyPath!))
        {
            return new HelperResponse(false, request.RequestId, "E_KEY_PROTECTED", "Registry key is protected.", request.KeyPath);
        }

        var (hive, sub) = Split(request.KeyPath!);
        var root = hive.ToUpperInvariant() is "HKLM" or "HKEY_LOCAL_MACHINE"
            ? Microsoft.Win32.Registry.LocalMachine
            : Microsoft.Win32.Registry.CurrentUser;
        root.DeleteSubKeyTree(sub, throwOnMissingSubKey: false);
        return new HelperResponse(true, request.RequestId, null, "Key deleted.", request.KeyPath);
    }

    private static HelperResponse DeleteRegValueLocal(HelperRequest request)
    {
        if (!SafetyGate.CanDeleteRegistry(request.KeyPath!))
        {
            return new HelperResponse(false, request.RequestId, "E_KEY_PROTECTED", "Registry key is protected.", request.KeyPath);
        }

        var (hive, sub) = Split(request.KeyPath!);
        var root = hive.ToUpperInvariant() is "HKLM" or "HKEY_LOCAL_MACHINE"
            ? Microsoft.Win32.Registry.LocalMachine
            : Microsoft.Win32.Registry.CurrentUser;
        using var key = root.OpenSubKey(sub, writable: true);
        if (key is null)
        {
            return new HelperResponse(false, request.RequestId, "E_NOT_FOUND", "Key not found.", request.KeyPath);
        }

        key.DeleteValue(request.ValueName!, throwOnMissingValue: false);
        return new HelperResponse(true, request.RequestId, null, "Value deleted.", $"{request.KeyPath}\\{request.ValueName}");
    }

    private static (string Hive, string Sub) Split(string keyPath)
    {
        var idx = keyPath.IndexOf('\\');
        return idx <= 0 ? (keyPath, string.Empty) : (keyPath[..idx], keyPath[(idx + 1)..]);
    }

    private static bool IsCurrentProcessElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
}
