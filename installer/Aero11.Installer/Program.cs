using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Aero11.Installer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--verify", StringComparer.OrdinalIgnoreCase))
        {
            var staging = Path.Combine(Path.GetTempPath(), "Aero11-verify-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(staging);
                InstallerForm.ExtractAndValidatePayload(staging);
                Console.WriteLine("Aero11 payload verification passed.");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
            finally
            {
                InstallerForm.TryDeleteStaging(staging);
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm(args.Contains("--resume-after-logoff", StringComparer.OrdinalIgnoreCase)));
        return 0;
    }
}

internal sealed class InstallerForm : Form
{
    private const int MinimumBuild = 22000;
    private static readonly Version MaximumTestedBuild = new(10, 0, 28000, 1896);
    private const string ThemeResourcePrefix = "Payload/Themes/";
    private const string AeroResourcePrefix = "Payload/Aero11/";
    private const string LicenseResourcePrefix = "Payload/Licenses/";
    private const string SecureUxResource = "Payload/SecureUxTheme.msi";

    private static readonly string[] ThemeFiles =
    [
        "Aero11_Metro_8.1.theme",
        "Aero11_Metro_8.theme",
        "Aero11_Seven_Clear.theme",
        "Aero11_Seven_Shiny.theme",
        "Aero11_Vista.theme"
    ];

    private static readonly string[] ThemeSelectionFiles =
    [
        "Aero11_Seven_Shiny.theme",
        "Aero11_Seven_Clear.theme",
        "Aero11_Vista.theme",
        "Aero11_Metro_8.1.theme",
        "Aero11_Metro_8.theme"
    ];

    private readonly Label _status = new();
    private readonly ComboBox _theme = new();
    private readonly CheckBox _installSecureUx = new();
    private readonly CheckBox _applyTheme = new();
    private readonly Button _install = new();
    private readonly Button _restore = new();
    private readonly ProgressBar _progress = new();
    private readonly TextBox _log = new();
    private readonly BuildInfo _build;
    private readonly bool _resumeAfterLogoff;

    public InstallerForm(bool resumeAfterLogoff = false)
    {
        _build = BuildInfo.Read();
        _resumeAfterLogoff = resumeAfterLogoff;
        Text = "Instalador de Aero11";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 560);
        Size = new Size(760, 600);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(245, 248, 252);

        var title = new Label
        {
            Text = "Aero11 para Windows 11",
            Font = new Font("Segoe UI Semibold", 22F),
            AutoSize = true,
            Location = new Point(28, 22)
        };
        var subtitle = new Label
        {
            Text = "Instala los temas en Windows\\Resources\\Themes con copia de seguridad.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(31, 70)
        };

        _status.AutoSize = false;
        _status.Location = new Point(31, 106);
        _status.Size = new Size(680, 52);
        _status.Font = new Font("Segoe UI Semibold", 10F);

        var themeLabel = new Label { Text = "Tema que se aplicará", AutoSize = true, Location = new Point(31, 174) };
        _theme.DropDownStyle = ComboBoxStyle.DropDownList;
        _theme.Location = new Point(31, 199);
        _theme.Width = 360;
        _theme.Items.AddRange(
        [
            "Aero11: Seven (Shiny)",
            "Aero11: Seven (Clear)",
            "Aero11: Vista",
            "Aero11: Metro (8.1)",
            "Aero11: Metro (8)"
        ]);
        _theme.SelectedIndex = 0;

        _installSecureUx.Text = "Instalar/reinstalar SecureUxTheme 4 (limpieza forzada automática)";
        _installSecureUx.AutoSize = true;
        _installSecureUx.Checked = true;
        _installSecureUx.Enabled = true;
        _installSecureUx.Location = new Point(31, 244);

        _applyTheme.Text = "Abrir el tema seleccionado al terminar";
        _applyTheme.AutoSize = true;
        _applyTheme.Checked = true;
        _applyTheme.Location = new Point(31, 276);

        _install.Text = "Instalar";
        _install.Size = new Size(145, 40);
        _install.Location = new Point(31, 318);
        _install.BackColor = Color.FromArgb(0, 103, 192);
        _install.ForeColor = Color.White;
        _install.FlatStyle = FlatStyle.Flat;
        _install.Click += async (_, _) => await InstallAsync();

        _restore.Text = "Restaurar copia";
        _restore.Size = new Size(155, 40);
        _restore.Location = new Point(190, 318);
        _restore.Click += async (_, _) => await RestoreAsync();

        _progress.Location = new Point(31, 371);
        _progress.Size = new Size(680, 7);
        _progress.Style = ProgressBarStyle.Continuous;

        _log.Location = new Point(31, 397);
        _log.Size = new Size(680, 125);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.BackColor = Color.White;

        Controls.AddRange([title, subtitle, _status, themeLabel, _theme, _installSecureUx, _applyTheme, _install, _restore, _progress, _log]);
        Shown += async (_, _) =>
        {
            InitializeState();
            if (_resumeAfterLogoff) await ResumeAfterLogoffAsync();
        };
    }

    private void InitializeState()
    {
        var supported = IsSupportedBuild(_build.Version);
        _status.Text = $"Sistema detectado: Windows build {_build.Build}.{_build.Ubr} ({RuntimeInformation.OSArchitecture})\r\n" +
            (supported ? "Compatible: validado hasta 28000.1896." : "Fuera del rango validado 22000–28000.1896. La instalación está bloqueada.");
        _status.ForeColor = supported ? Color.DarkGreen : Color.DarkRed;
        _install.Enabled = supported && RuntimeInformation.OSArchitecture == Architecture.X64;
        Log(IsAdministrator() ? "Permisos de administrador confirmados." : "No se obtuvieron permisos de administrador.");
        var secureUx = SecureUxThemeCleanup.Detect();
        Log(secureUx.Found ? secureUx.Describe() : "SecureUxTheme no está instalado.");
    }

    private async Task InstallAsync()
    {
        if (!IsSupportedBuild(_build.Version) || RuntimeInformation.OSArchitecture != Architecture.X64)
        {
            MessageBox.Show("Este paquete solo admite Windows 11 x64 builds 22000 a 28000.1896.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        SetBusy(true);
        var staging = Path.Combine(Path.GetTempPath(), "Aero11-" + Guid.NewGuid().ToString("N"));
        string? backup = null;
        var themeFilesInstalled = false;
        try
        {
            Directory.CreateDirectory(staging);
            Log("Validando el contenido integrado...");
            ExtractAndValidatePayload(staging);
            _progress.Value = 20;

            if (_installSecureUx.Checked)
            {
                Log("Ejecutando siempre la limpieza forzada de SecureUxTheme antes del MSI...");
                var cleanup = await SecureUxThemeCleanup.ForceUninstallAsync(Log);
                if (cleanup.RequiresReboot)
                    throw new InvalidOperationException("La desinstalación MSI anterior requiere reiniciar. Reinicia Windows y vuelve a ejecutar Aero11 Installer.");
            }

            backup = CreateBackup();
            Log($"Copia de seguridad: {backup}");
            _progress.Value = 40;

            themeFilesInstalled = true;
            InstallThemeFiles(staging);
            Log($"Temas instalados en {ThemesDirectory}.");
            _progress.Value = 70;

            var rebootRequired = false;
            if (_installSecureUx.Checked)
            {
                Log("Instalando SecureUxTheme 4...");
                var msi = Path.Combine(staging, "SecureUxTheme.msi");
                var result = await RunProcessAsync("msiexec.exe", $"/i \"{msi}\" /passive /norestart");
                if (result is not (0 or 1641 or 3010))
                    throw new InvalidOperationException($"SecureUxTheme devolvió el código {result}.");
                rebootRequired = true;
                Log("SecureUxTheme instalado. Es necesario reiniciar Windows.");
            }

            _progress.Value = 100;
            if (_applyTheme.Checked && !rebootRequired)
            {
                var selected = Path.Combine(ThemesDirectory, SelectedThemeFile());
                Process.Start(new ProcessStartInfo(selected) { UseShellExecute = true });
                Log("Tema abierto en Personalización.");
            }

            if (rebootRequired)
            {
                var answer = MessageBox.Show(
                    "SecureUxTheme se instaló correctamente. Para completar la instalación se cerrará tu sesión y Aero11 Installer volverá a abrirse al iniciar sesión.\r\n\r\n¿Deseas cerrar sesión ahora?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (answer == DialogResult.Yes)
                {
                    try
                    {
                        PrepareResumeAfterLogoff(SelectedThemeFile(), _applyTheme.Checked);
                        Log("Reanudación registrada para el siguiente inicio de sesión.");
                        Log("Cerrando sesión...");
                        Process.Start(new ProcessStartInfo("shutdown.exe", "/l") { UseShellExecute = false, CreateNoWindow = true });
                        return;
                    }
                    catch (Exception resumeError)
                    {
                        Log("No se pudo preparar la reanudación: " + resumeError.Message);
                        MessageBox.Show(
                            "SecureUxTheme sí quedó instalado, pero no se pudo preparar el inicio automático. Cierra sesión manualmente y vuelve a abrir Aero11 Installer.\r\n\r\n" + resumeError.Message,
                            Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                }

                MessageBox.Show(
                    "La sesión no se cerrará. Cierra sesión manualmente o reinicia Windows antes de aplicar el tema.",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show("Aero11 se instaló correctamente.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            if (themeFilesInstalled && backup is not null)
            {
                try
                {
                    RestoreBackup(backup);
                    Log("Los archivos de tema se revirtieron a la copia anterior.");
                }
                catch (Exception rollbackError)
                {
                    Log("ERROR durante la reversión: " + rollbackError.Message);
                }
            }
            MessageBox.Show("No se completó la instalación. No reinicies hasta revisar el registro.\r\n\r\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            TryDeleteStaging(staging);
            SetBusy(false);
        }
    }

    private async Task ResumeAfterLogoffAsync()
    {
        await Task.Yield();
        ResumeState? state = null;
        try
        {
            if (File.Exists(ResumeStatePath))
                state = JsonSerializer.Deserialize<ResumeState>(File.ReadAllText(ResumeStatePath));
        }
        catch (Exception ex)
        {
            Log("No se pudo leer el estado de reanudación: " + ex.Message);
        }

        if (state is null || !ThemeFiles.Contains(state.ThemeFile, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show("No existe una instalación pendiente para continuar.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            DeleteResumeState();
            return;
        }

        DeleteResumeRegistration();

        _theme.SelectedIndex = Array.FindIndex(ThemeSelectionFiles,
            value => value.Equals(state.ThemeFile, StringComparison.OrdinalIgnoreCase));
        if (_theme.SelectedIndex < 0) _theme.SelectedIndex = 0;

        var resumeQuestion = state.ApplyTheme
            ? "¿Deseas continuar y abrir el tema ahora?"
            : "¿Deseas continuar y finalizar la instalación?";
        var answer = MessageBox.Show(
            $"La sesión se inició de nuevo y SecureUxTheme ya fue instalado.\r\n\r\nTema pendiente: {state.ThemeFile}\r\n\r\n{resumeQuestion}",
            "Continuar instalación de Aero11", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        DeleteResumeState();
        if (answer == DialogResult.Yes && state.ApplyTheme)
        {
            var selected = Path.Combine(ThemesDirectory, state.ThemeFile);
            if (!File.Exists(selected))
            {
                MessageBox.Show("No se encontró el tema pendiente: " + selected, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Process.Start(new ProcessStartInfo(selected) { UseShellExecute = true });
            Log("Tema abierto después del nuevo inicio de sesión.");
        }
        else if (answer == DialogResult.Yes)
        {
            MessageBox.Show("La instalación terminó. No se abrió el tema porque esa opción estaba desactivada.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        Close();
    }

    private static void PrepareResumeAfterLogoff(string themeFile, bool applyTheme)
    {
        if (!ThemeFiles.Contains(themeFile, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Tema no válido para reanudar: " + themeFile);

        Directory.CreateDirectory(ResumeDirectory);
        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("No se pudo obtener la ruta del instalador actual.");
        if (!Path.GetFullPath(currentExecutable).Equals(Path.GetFullPath(ResumeInstallerPath), StringComparison.OrdinalIgnoreCase))
            File.Copy(currentExecutable, ResumeInstallerPath, true);

        var state = new ResumeState(themeFile, applyTheme, DateTimeOffset.Now);
        File.WriteAllText(ResumeStatePath, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        var userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("No se pudo obtener el SID del usuario actual.");
        var escapedSid = SecurityElement.Escape(userSid);
        var escapedCommand = SecurityElement.Escape(ResumeInstallerPath);
        var taskXml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo><Description>Continúa Aero11 después de cerrar sesión.</Description></RegistrationInfo>
              <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{escapedSid}</UserId></LogonTrigger></Triggers>
              <Principals><Principal id="Author"><UserId>{escapedSid}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
              <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled><Hidden>false</Hidden><RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT1H</ExecutionTimeLimit><Priority>7</Priority></Settings>
              <Actions Context="Author"><Exec><Command>{escapedCommand}</Command><Arguments>--resume-after-logoff</Arguments></Exec></Actions>
            </Task>
            """;
        File.WriteAllText(ResumeTaskXmlPath, taskXml, Encoding.Unicode);

        var result = RunProcess("schtasks.exe", $"/Create /TN \"{ResumeTaskName}\" /XML \"{ResumeTaskXmlPath}\" /F");
        if (result.ExitCode != 0)
        {
            DeleteResumeState();
            throw new InvalidOperationException("No se pudo registrar la tarea de reanudación: " + result.Error);
        }
    }

    private static void DeleteResumeState()
    {
        try { if (File.Exists(ResumeStatePath)) File.Delete(ResumeStatePath); } catch { }
        DeleteResumeRegistration();
    }

    private static void DeleteResumeRegistration()
    {
        try { RunProcess("schtasks.exe", $"/Delete /TN \"{ResumeTaskName}\" /F"); } catch { }
        try
        {
            using var runOnce = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\RunOnce", true);
            runOnce?.DeleteValue("Aero11Resume", false);
        }
        catch { }
    }

    private static (int ExitCode, string Error) RunProcess(string file, string arguments)
    {
        using var process = Process.Start(new ProcessStartInfo(file, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        }) ?? throw new InvalidOperationException("No se pudo iniciar " + file);
        var error = process.StandardError.ReadToEnd();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, string.Join(" ", new[] { error.Trim(), output.Trim() }.Where(value => value.Length != 0)));
    }

    private async Task RestoreAsync()
    {
        var backup = LatestBackup();
        if (backup is null)
        {
            MessageBox.Show("No existe una copia de seguridad de Aero11.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show($"Se quitarán los archivos actuales de Aero11 y se restaurará:\r\n{backup}", Text,
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        SetBusy(true);
        try
        {
            await Task.Run(() => RestoreBackup(backup));
            _progress.Value = 100;
            Log("Copia restaurada. SecureUxTheme no fue desinstalado.");
            MessageBox.Show("La copia se restauró. SecureUxTheme permanece instalado y puede quitarse desde Aplicaciones instaladas.", Text,
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("ERROR al restaurar: " + ex.Message);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal static void ExtractAndValidatePayload(string staging)
    {
        var assembly = Assembly.GetExecutingAssembly();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith("Payload/", StringComparison.Ordinal)))
        {
            var relative = name switch
            {
                SecureUxResource => "SecureUxTheme.msi",
                _ when name.StartsWith(ThemeResourcePrefix, StringComparison.Ordinal) => name[ThemeResourcePrefix.Length..],
                _ when name.StartsWith(AeroResourcePrefix, StringComparison.Ordinal) => Path.Combine("Aero11", name[AeroResourcePrefix.Length..].Replace('/', Path.DirectorySeparatorChar)),
                _ when name.StartsWith(LicenseResourcePrefix, StringComparison.Ordinal) => Path.Combine("Licenses", name[LicenseResourcePrefix.Length..]),
                _ => throw new InvalidDataException("Recurso inesperado: " + name)
            };

            var destination = SafeChildPath(staging, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var input = assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("No se pudo leer " + name);
            if (input.Length == 0) throw new InvalidDataException("Recurso vacío: " + name);
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
        }

        foreach (var required in ThemeFiles.Select(f => Path.Combine(staging, f))
                     .Concat([
                         Path.Combine(staging, "Aero11", "Seven", "aero11_seven_shiny.msstyles"),
                         Path.Combine(staging, "SecureUxTheme.msi"),
                         Path.Combine(staging, "Licenses", "Aero11-LICENSE.txt"),
                         Path.Combine(staging, "Licenses", "SecureUxTheme-COPYING.txt")
                     ]))
            if (!File.Exists(required) || new FileInfo(required).Length == 0)
                throw new InvalidDataException("Falta un archivo requerido: " + required);
    }

    private static string CreateBackup()
    {
        var backup = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(backup);
        var aeroTarget = Path.Combine(ThemesDirectory, "Aero11");
        if (Directory.Exists(aeroTarget)) CopyDirectory(aeroTarget, Path.Combine(backup, "Aero11"), false);
        foreach (var file in ThemeFiles)
        {
            var source = Path.Combine(ThemesDirectory, file);
            if (File.Exists(source)) File.Copy(source, Path.Combine(backup, file), false);
        }
        File.WriteAllText(Path.Combine(backup, "backup.info"), $"Aero11 backup\r\nWindows={Environment.OSVersion.Version}\r\nCreated={DateTimeOffset.Now:O}\r\n");
        return backup;
    }

    private static void InstallThemeFiles(string staging)
    {
        Directory.CreateDirectory(ThemesDirectory);
        CopyDirectory(Path.Combine(staging, "Aero11"), Path.Combine(ThemesDirectory, "Aero11"), true);
        foreach (var file in ThemeFiles)
            File.Copy(Path.Combine(staging, file), Path.Combine(ThemesDirectory, file), true);
        CopyDirectory(Path.Combine(staging, "Licenses"), Path.Combine(AeroDataRoot, "Licenses"), true);
    }

    private static void RestoreBackup(string backup)
    {
        EnsureChildPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows), ThemesDirectory);
        var removed = Path.Combine(BackupRoot, "Removed-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(removed);
        var aero = Path.Combine(ThemesDirectory, "Aero11");
        if (Directory.Exists(aero)) Directory.Move(aero, Path.Combine(removed, "Aero11"));
        foreach (var file in ThemeFiles)
        {
            var current = Path.Combine(ThemesDirectory, file);
            if (File.Exists(current)) File.Move(current, Path.Combine(removed, file));
        }

        var oldAero = Path.Combine(backup, "Aero11");
        if (Directory.Exists(oldAero)) CopyDirectory(oldAero, aero, false);
        foreach (var file in ThemeFiles)
        {
            var oldFile = Path.Combine(backup, file);
            if (File.Exists(oldFile)) File.Copy(oldFile, Path.Combine(ThemesDirectory, file), false);
        }
    }

    private static void CopyDirectory(string source, string destination, bool overwrite)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(SafeChildPath(destination, Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, SafeChildPath(destination, Path.GetRelativePath(source, file)), overwrite);
    }

    private static string SafeChildPath(string root, string relative)
    {
        var path = Path.GetFullPath(Path.Combine(root, relative));
        EnsureChildPath(root, path);
        return path;
    }

    private static void EnsureChildPath(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ruta fuera del directorio permitido: " + path);
    }

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

    private string SelectedThemeFile() => _theme.SelectedIndex switch
    {
        0 => "Aero11_Seven_Shiny.theme",
        1 => "Aero11_Seven_Clear.theme",
        2 => "Aero11_Vista.theme",
        3 => "Aero11_Metro_8.1.theme",
        4 => "Aero11_Metro_8.theme",
        _ => throw new InvalidOperationException("Selección de tema no válida.")
    };

    private void SetBusy(bool busy)
    {
        _install.Enabled = !busy && IsSupportedBuild(_build.Version);
        _restore.Enabled = !busy;
        _theme.Enabled = !busy;
        _installSecureUx.Enabled = !busy;
        _applyTheme.Enabled = !busy;
        _progress.Value = busy ? 5 : 0;
        UseWaitCursor = busy;
    }

    private void Log(string message) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private static bool IsSupportedBuild(Version version) =>
        version.Major == 10 && version.Build >= MinimumBuild && version <= MaximumTestedBuild;

    private static bool IsAdministrator() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    private static string ThemesDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Resources", "Themes");
    private static string AeroDataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Aero11");
    private static string BackupRoot => Path.Combine(AeroDataRoot, "Backups");
    private static string ResumeDirectory => Path.Combine(AeroDataRoot, "Resume");
    private static string ResumeInstallerPath => Path.Combine(ResumeDirectory, "Aero11-Installer.exe");
    private static string ResumeStatePath => Path.Combine(ResumeDirectory, "resume.json");
    private static string ResumeTaskXmlPath => Path.Combine(ResumeDirectory, "resume-task.xml");
    private const string ResumeTaskName = "Aero11 ResumeAfterLogoff";

    private static string? LatestBackup() => Directory.Exists(BackupRoot)
        ? Directory.GetDirectories(BackupRoot).Where(d => File.Exists(Path.Combine(d, "backup.info"))).OrderByDescending(d => d).FirstOrDefault()
        : null;

    internal static void TryDeleteStaging(string staging)
    {
        try
        {
            var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var target = Path.GetFullPath(staging);
            if (target.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
                Directory.Delete(target, true);
        }
        catch { }
    }
}

internal sealed record ResumeState(string ThemeFile, bool ApplyTheme, DateTimeOffset CreatedAt);

internal sealed record BuildInfo(int Build, int Ubr)
{
    public Version Version => new(10, 0, Build, Ubr);

    public static BuildInfo Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var build = int.TryParse(key?.GetValue("CurrentBuildNumber")?.ToString(), out var parsed) ? parsed : Environment.OSVersion.Version.Build;
        var ubr = key?.GetValue("UBR") is int value ? value : Math.Max(0, Environment.OSVersion.Version.Revision);
        return new BuildInfo(build, ubr);
    }
}
