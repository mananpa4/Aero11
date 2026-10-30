using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Aero11.Installer;

internal sealed record SecureUxThemeDetection(
    IReadOnlyList<SecureUxThemeFile> Files,
    IReadOnlyList<SecureUxThemeHook> Hooks,
    IReadOnlyList<SecureUxThemeProduct> Products)
{
    public bool Found => Files.Count != 0 || Hooks.Count != 0 || Products.Count != 0;

    public string Describe()
    {
        var text = new StringBuilder();
        if (!Found) text.AppendLine("No se detectó una instalación anterior.");
        foreach (var product in Products)
            text.AppendLine($"MSI registrado: {product.Name} {product.Version} ({product.ProductCode})");
        foreach (var file in Files)
            text.AppendLine($"Archivo: {file.Path} — {file.VersionLabel} — SHA-256 {file.Sha256}");
        foreach (var hook in Hooks)
            text.AppendLine($"Hook {hook.View}: {hook.Executable} -> {hook.VerifierDlls}, GlobalFlag=0x{hook.GlobalFlag:X}");
        return text.ToString().TrimEnd();
    }
}

internal sealed record SecureUxThemeFile(string Path, string Sha256, string VersionLabel);
internal sealed record SecureUxThemeHook(RegistryView View, string Executable, string VerifierDlls, int GlobalFlag);
internal sealed record SecureUxThemeProduct(string ProductCode, string Name, string Version);
internal sealed record SecureUxThemeCleanupResult(string BackupDirectory, bool RequiresReboot, SecureUxThemeDetection Before, SecureUxThemeDetection After);

internal static class SecureUxThemeCleanup
{
    private const int ApplicationVerifierFlag = 0x100;
    private const uint MoveFileReplaceExisting = 0x1;
    private const uint MoveFileDelayUntilReboot = 0x4;
    private const string V3X64Sha256 = "1F34D15EEF9FFFD1122904310C42DE2131B8BCD1B830E3F2C26C71535D6F1722";
    private static readonly string[] Executables = ["winlogon.exe", "explorer.exe", "SystemSettings.exe", "LogonUI.exe", "dwm.exe"];
    private static readonly string[] DllNames = ["SecureUxTheme.dll", "SecureUxTheme4.dll"];

    public static SecureUxThemeDetection Detect()
    {
        var files = new List<SecureUxThemeFile>();
        foreach (var directory in SystemDirectories())
        foreach (var name in DllNames)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var version = hash.Equals(V3X64Sha256, StringComparison.OrdinalIgnoreCase)
                ? "SecureUxTheme oficial v3.0.0 x64"
                : name.Equals("SecureUxTheme4.dll", StringComparison.OrdinalIgnoreCase)
                    ? "SecureUxTheme v4"
                    : "versión heredada/no identificada";
            files.Add(new SecureUxThemeFile(path, hash, version));
        }

        var hooks = new List<SecureUxThemeHook>();
        foreach (var view in RegistryViews())
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (var executable in Executables)
            {
                var subkey = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{executable}";
                using var key = machine.OpenSubKey(subkey, false);
                var verifier = key?.GetValue("VerifierDlls") as string;
                if (!ContainsSecureUxTheme(verifier)) continue;
                var flag = Convert.ToInt32(key?.GetValue("GlobalFlag") ?? 0);
                hooks.Add(new SecureUxThemeHook(view, executable, verifier!, flag));
            }
        }

        return new SecureUxThemeDetection(files, hooks, FindRegisteredProducts());
    }

    public static async Task<SecureUxThemeCleanupResult> ForceUninstallAsync(Action<string>? log = null)
    {
        var before = Detect();
        var backup = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Aero11", "SecureUxThemeCleanup", DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        File.WriteAllText(Path.Combine(backup, "detection-before.txt"), before.Describe() + Environment.NewLine, Encoding.UTF8);
        log?.Invoke(before.Found ? before.Describe() : "No se encontró SecureUxTheme anterior; se hará igualmente la limpieza preventiva.");

        BackupRegistry(backup, log);
        BackupFiles(before.Files, backup, log);

        var requiresReboot = false;
        foreach (var product in before.Products.DistinctBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase))
        {
            log?.Invoke($"Desinstalando MSI registrado {product.Name} {product.Version}...");
            var exitCode = await RunProcessAsync("msiexec.exe", $"/x {product.ProductCode} /passive /norestart");
            if (exitCode is not (0 or 1605 or 1614 or 1641 or 3010))
                log?.Invoke($"El desinstalador MSI devolvió {exitCode}; continuará la limpieza forzada.");
            if (exitCode is 1641 or 3010) requiresReboot = true;
        }

        RemoveHooks(log);
        RemoveFiles(log);

        var after = Detect();
        File.WriteAllText(Path.Combine(backup, "detection-after.txt"), after.Describe() + Environment.NewLine, Encoding.UTF8);
        if (after.Files.Count != 0 || after.Hooks.Count != 0)
            throw new InvalidOperationException("La limpieza no pudo retirar todos los archivos o hooks de SecureUxTheme. Revisa " + backup);

        log?.Invoke("Limpieza forzada terminada. Copia de seguridad: " + backup);
        return new SecureUxThemeCleanupResult(backup, requiresReboot, before, after);
    }

    private static void BackupRegistry(string backup, Action<string>? log)
    {
        foreach (var view in RegistryViews())
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (var executable in Executables)
            {
                var relative = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{executable}";
                using var key = machine.OpenSubKey(relative, false);
                if (!ContainsSecureUxTheme(key?.GetValue("VerifierDlls") as string)) continue;
                var file = Path.Combine(backup, $"IFEO-{view}-{executable}.reg");
                var registryPath = view == RegistryView.Registry32
                    ? $@"HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{executable}"
                    : $@"HKLM\{relative}";
                using var process = Process.Start(new ProcessStartInfo("reg.exe", $"export \"{registryPath}\" \"{file}\" /y")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                process?.WaitForExit();
                log?.Invoke("Registro respaldado: " + registryPath);
            }
        }
    }

    private static void BackupFiles(IEnumerable<SecureUxThemeFile> files, string backup, Action<string>? log)
    {
        var directory = Path.Combine(backup, "Files");
        Directory.CreateDirectory(directory);
        foreach (var file in files)
        {
            var parent = Path.GetFileName(Path.GetDirectoryName(file.Path)) ?? "System";
            var targetDirectory = Path.Combine(directory, parent);
            Directory.CreateDirectory(targetDirectory);
            var target = Path.Combine(targetDirectory, Path.GetFileName(file.Path));
            File.Copy(file.Path, target, true);
            log?.Invoke("Archivo respaldado: " + target);
        }
    }

    private static void RemoveHooks(Action<string>? log)
    {
        foreach (var view in RegistryViews())
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (var executable in Executables)
            {
                var path = $@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{executable}";
                using var key = machine.OpenSubKey(path, true);
                if (key is null) continue;
                var verifier = key.GetValue("VerifierDlls") as string;
                if (!ContainsSecureUxTheme(verifier)) continue;

                var remaining = SplitVerifierDlls(verifier!).Where(token => !IsSecureUxThemeName(token)).ToArray();
                if (remaining.Length == 0)
                {
                    key.DeleteValue("VerifierDlls", false);
                    var flag = Convert.ToInt32(key.GetValue("GlobalFlag") ?? 0) & ~ApplicationVerifierFlag;
                    if (flag == 0) key.DeleteValue("GlobalFlag", false);
                    else key.SetValue("GlobalFlag", flag, RegistryValueKind.DWord);
                }
                else
                {
                    key.SetValue("VerifierDlls", string.Join(' ', remaining), RegistryValueKind.String);
                }
                log?.Invoke($"Hook retirado ({view}): {executable}");
            }
        }
    }

    private static void RemoveFiles(Action<string>? log)
    {
        foreach (var directory in SystemDirectories())
        foreach (var name in DllNames)
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            try
            {
                File.Delete(path);
                log?.Invoke("Archivo retirado: " + path);
            }
            catch (IOException)
            {
                RenameAndScheduleDeletion(path, log);
            }
            catch (UnauthorizedAccessException)
            {
                RenameAndScheduleDeletion(path, log);
            }
        }
    }

    private static void RenameAndScheduleDeletion(string path, Action<string>? log)
    {
        var renamed = path + ".aero11-removed-" + Guid.NewGuid().ToString("N");
        if (!MoveFileEx(path, renamed, MoveFileReplaceExisting))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "No se pudo renombrar " + path);
        if (!MoveFileEx(renamed, null, MoveFileDelayUntilReboot))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "No se pudo programar la eliminación de " + renamed);
        log?.Invoke("Archivo bloqueado renombrado y programado para eliminar al reiniciar: " + renamed);
    }

    private static IReadOnlyList<SecureUxThemeProduct> FindRegisteredProducts()
    {
        var products = new List<SecureUxThemeProduct>();
        foreach (var view in RegistryViews())
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using var uninstall = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", false);
            if (uninstall is null) continue;
            foreach (var name in uninstall.GetSubKeyNames())
            {
                using var item = uninstall.OpenSubKey(name, false);
                var displayName = item?.GetValue("DisplayName") as string;
                if (displayName?.Contains("SecureUxTheme", StringComparison.OrdinalIgnoreCase) != true) continue;
                if (!Guid.TryParse(name, out _)) continue;
                products.Add(new SecureUxThemeProduct(name, displayName, item?.GetValue("DisplayVersion")?.ToString() ?? "desconocida"));
            }
        }
        return products;
    }

    private static IEnumerable<string> SystemDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (Environment.Is64BitOperatingSystem)
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            yield return Path.Combine(windows, "SysWOW64");
        }
    }

    private static IEnumerable<RegistryView> RegistryViews()
    {
        yield return RegistryView.Registry64;
        if (Environment.Is64BitOperatingSystem) yield return RegistryView.Registry32;
    }

    private static bool ContainsSecureUxTheme(string? value) =>
        value is not null && SplitVerifierDlls(value).Any(IsSecureUxThemeName);

    private static IEnumerable<string> SplitVerifierDlls(string value) =>
        value.Split([' ', '\t', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsSecureUxThemeName(string value) =>
        value.Equals("SecureUxTheme.dll", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("SecureUxTheme4.dll", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("SecureUxTheme", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("SecureUxTheme4", StringComparison.OrdinalIgnoreCase);

    private static async Task<int> RunProcessAsync(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("No se pudo iniciar " + file);
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingFileName, string? newFileName, uint flags);
}
