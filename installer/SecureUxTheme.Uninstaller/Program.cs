namespace Aero11.Installer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--scan", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(SecureUxThemeCleanup.Detect().Describe());
            return 0;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new CleanupForm());
        return 0;
    }
}

internal sealed class CleanupForm : Form
{
    private readonly TextBox _details = new();
    private readonly Button _refresh = new();
    private readonly Button _clean = new();
    private readonly ProgressBar _progress = new();

    public CleanupForm()
    {
        Text = "Desinstalador forzado de SecureUxTheme";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 500);
        Size = new Size(760, 540);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(245, 248, 252);

        var title = new Label
        {
            Text = "SecureUxTheme — limpieza forzada",
            Font = new Font("Segoe UI Semibold", 21F),
            AutoSize = true,
            Location = new Point(28, 22)
        };
        var explanation = new Label
        {
            Text = "Detecta MSI, hooks IFEO y DLL huérfanas. Crea una copia recuperable antes de retirarlos.",
            AutoSize = true,
            ForeColor = Color.DimGray,
            Location = new Point(31, 70)
        };

        _details.Location = new Point(31, 108);
        _details.Size = new Size(680, 280);
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = false;
        _details.BackColor = Color.White;

        _refresh.Text = "Volver a detectar";
        _refresh.Location = new Point(31, 410);
        _refresh.Size = new Size(155, 40);
        _refresh.Click += (_, _) => RefreshDetection();

        _clean.Text = "Desinstalar por fuerza";
        _clean.Location = new Point(198, 410);
        _clean.Size = new Size(190, 40);
        _clean.BackColor = Color.FromArgb(180, 45, 45);
        _clean.ForeColor = Color.White;
        _clean.FlatStyle = FlatStyle.Flat;
        _clean.Click += async (_, _) => await CleanAsync();

        _progress.Location = new Point(31, 468);
        _progress.Size = new Size(680, 7);

        Controls.AddRange([title, explanation, _details, _refresh, _clean, _progress]);
        Shown += (_, _) => RefreshDetection();
    }

    private void RefreshDetection()
    {
        try
        {
            _details.Text = SecureUxThemeCleanup.Detect().Describe();
        }
        catch (Exception ex)
        {
            _details.Text = "ERROR: " + ex;
        }
    }

    private async Task CleanAsync()
    {
        if (MessageBox.Show(
                "Se respaldarán y retirarán todas las versiones detectadas de SecureUxTheme. ¿Continuar?",
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        SetBusy(true);
        _details.Clear();
        try
        {
            var result = await SecureUxThemeCleanup.ForceUninstallAsync(Log);
            _progress.Value = 100;
            var reboot = result.RequiresReboot ? " Reinicia Windows antes de reinstalar." : string.Empty;
            MessageBox.Show("Limpieza terminada." + reboot + "\r\n\r\nCopia: " + result.BackupDirectory,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Log("ERROR: " + ex.Message);
            MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Log(string message) => _details.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private void SetBusy(bool busy)
    {
        _refresh.Enabled = !busy;
        _clean.Enabled = !busy;
        _progress.Style = busy ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
        if (!busy && _progress.Value != 100) _progress.Value = 0;
        UseWaitCursor = busy;
    }
}
