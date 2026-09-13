using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TinyShell.Debloat;

public static class Debloater
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static string? _scriptPath;

    /// <summary>
    /// Extracts the embedded PowerShell engine to %LOCALAPPDATA%\TinyShell.
    /// FIX: previously only re-extracted when the cached file was missing,
    /// so shipping an updated catalog in a new TinyShell build would keep
    /// silently running whatever script version a user's machine cached
    /// from an earlier install. Now compares a SHA-256 of the embedded
    /// resource against a sidecar .hash file and re-extracts on mismatch.
    /// </summary>
    private static string ScriptPath()
    {
        if (_scriptPath is not null && File.Exists(_scriptPath)) return _scriptPath;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinyShell");
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "debloat.ps1");
        var hashPath = path + ".hash";

        var asm = Assembly.GetExecutingAssembly();
        var res = asm.GetManifestResourceNames()
                     .FirstOrDefault(n => n.EndsWith("debloat.ps1", StringComparison.OrdinalIgnoreCase))
                  ?? throw new InvalidOperationException("debloat.ps1 resource missing");

        using var stream = asm.GetManifestResourceStream(res)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();
        var currentHash = Convert.ToHexString(SHA256.HashData(bytes));

        var needsWrite = !File.Exists(path)
            || !File.Exists(hashPath)
            || File.ReadAllText(hashPath).Trim() != currentHash;

        if (needsWrite)
        {
            File.WriteAllBytes(path, bytes);
            File.WriteAllText(hashPath, currentHash);
        }

        _scriptPath = path;
        return path;
    }

    public static async Task<List<DebloatItem>> GetCatalogAsync()
    {
        var (code, output) = await RunUnelevatedAsync("-List", default);
        if (code != 0)
            throw new InvalidOperationException($"Catalog query failed (exit {code}):\n{output}");

        // FIX: bracket-scanning silently produced "[]" on any malformed
        // output. Now surfaces a real parse error instead of pretending
        // the catalog is empty.
        var json = ExtractJson(output);
        try
        {
            return JsonSerializer.Deserialize<List<DebloatItem>>(json, JsonOpts)
                   ?? throw new InvalidOperationException("Catalog deserialized to null.");
        }
        catch (JsonException jex)
        {
            throw new InvalidOperationException(
                $"Could not parse the tweak catalog. Raw engine output:\n{output}", jex);
        }
    }

    public static async Task<bool> CreateRestorePointAsync(Action<string> onLine, CancellationToken ct = default)
    {
        var (code, output) = await RunElevatedAsync("-RestorePoint", progressFile: null, onLine, ct);
        return code == 0;
    }

    public static async Task ApplyAsync(
        IEnumerable<DebloatItem> selected,
        Action<string> onLine,
        CancellationToken ct = default)
    {
        var ids = selected.Select(s => s.Id).ToArray();
        if (ids.Length == 0) { onLine("(nothing selected)"); return; }

        var progressFile = Path.Combine(Path.GetTempPath(), $"tinyshell-debloat-{Guid.NewGuid():N}.log");
        File.WriteAllText(progressFile, "");

        var idList = string.Join(",", ids.Select(i => $"'{i}'"));
        var args = $"-Apply -Ids {idList} -ProgressFile \"{progressFile}\"";

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var tailTask = Task.Run(async () =>
        {
            long pos = 0;
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var fs = new FileStream(progressFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    if (fs.Length > pos)
                    {
                        fs.Seek(pos, SeekOrigin.Begin);
                        using var sr = new StreamReader(fs);
                        string? line;
                        while ((line = await sr.ReadLineAsync()) is not null)
                            onLine(line);
                        pos = fs.Position;
                    }
                }
                catch { /* file may be locked mid-write */ }

                try { await Task.Delay(200, cts.Token); } catch { break; }
            }
        }, cts.Token);

        try
        {
            await RunElevatedAsync(args, progressFile, onLine: null, ct);
        }
        finally
        {
            cts.Cancel();
            try { await tailTask; } catch { }
            try { File.Delete(progressFile); } catch { }
        }
    }

    // ------------------------------------------------------------------

    private static Task<(int Code, string Output)> RunUnelevatedAsync(string args, CancellationToken ct)
    {
        var script = ScriptPath();
        var psi = new ProcessStartInfo("powershell.exe")
        {
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {args}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        return RunAsync(psi, ct);
    }

    /// <summary>
    /// FIX 1: added -WindowStyle Hidden to the inner elevated invocation.
    /// Previously CreateNoWindow only suppressed the outer wrapper process;
    /// the actual elevated PowerShell (running all the AppX/registry work)
    /// would flash a visible console window on screen.
    ///
    /// FIX 2: no longer treats the outer wrapper's stdout as meaningful.
    /// Start-Process -Wait launches the elevated process out-of-proc and
    /// the wrapper never sees its stdout, so the returned "console output"
    /// was consistently empty/useless. Real-time progress already comes
    /// from tailing progressFile; this method now only reports exit code.
    /// </summary>
    private static async Task<(int Code, string Output)> RunElevatedAsync(
        string args, string? progressFile, Action<string>? onLine, CancellationToken ct)
    {
        var script = ScriptPath();
        var inner = $"& '{script}' {args}";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(inner));

        var psi = new ProcessStartInfo("powershell.exe")
        {
            Arguments = "-NoProfile -ExecutionPolicy Bypass -Command " +
                        "\"Start-Process powershell.exe -Verb RunAs -Wait -WindowStyle Hidden " +
                        $"-ArgumentList '-NoProfile','-ExecutionPolicy','Bypass','-WindowStyle','Hidden'," +
                        $"'-EncodedCommand','{encoded}'\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        return await RunAsync(psi, ct);
    }

    private static async Task<(int Code, string Output)> RunAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        await proc.WaitForExitAsync(ct);

        var combined = stdout.ToString();
        if (stderr.Length > 0) combined += "\n[stderr]\n" + stderr;

        return (proc.ExitCode, combined);
    }

    private static string ExtractJson(string text)
    {
        int start = text.IndexOf('[');
        int end = text.LastIndexOf(']');
        if (start < 0 || end <= start)
            throw new InvalidOperationException($"No JSON array found in engine output:\n{text}");
        return text.Substring(start, end - start + 1);
    }
}
