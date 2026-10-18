using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace SonarFavoritePicker;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--self-test")) { SelfTests.Run(); return; }
        if (args.Contains("--live-test")) { SelfTests.RunLiveAsync().GetAwaiter().GetResult(); return; }
        using var mutex = new Mutex(true, @"Local\SonarFavoritePicker", out var first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        using var tray = new TrayApp();
        Application.Run(tray);
    }
}

internal sealed class TrayApp : ApplicationContext
{
    private const string StartupKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "SonarFavoritePicker";
    private readonly SonarClient sonar = new();
    private readonly ContextMenuStrip menu = new() { ShowImageMargin = false, ShowCheckMargin = true };
    private readonly NotifyIcon tray;
    private readonly Control dispatcher = new();
    private readonly Icon icon;
    private bool busy;
    private bool disposed;
    private bool checkingUpdates;
    private string? error;

    public TrayApp()
    {
        dispatcher.CreateControl();
        icon = CreateIcon();
        tray = new NotifyIcon { Icon = icon, Text = "Sonar Favorites • Gaming", Visible = true };
        // No ContextMenuStrip on NotifyIcon: route both buttons through one path.
        tray.MouseUp += async (_, e) =>
        {
            if (e.Button is MouseButtons.Left or MouseButtons.Right) await OpenMenuAsync();
        };
        menu.Font = new Font("Segoe UI", 10);
    }

    private async Task OpenMenuAsync()
    {
        if (disposed || busy) return;
        if (menu.Visible) { menu.Close(); return; }
        busy = true;
        var position = Cursor.Position;
        try
        {
            var snapshot = await sonar.ReadAsync();
            if (disposed) return;
            error = null;
            tray.Text = Truncate("Sonar • " + (snapshot.SelectedName ?? "Gaming"), 63);
            BuildMenu(snapshot);
        }
        catch (Exception ex)
        {
            if (disposed) return;
            error = FriendlyError(ex);
            BuildMenu(null, "Sonar unavailable — click Refresh to retry");
        }
        finally { busy = false; }
        if (disposed) return;
        // Build the complete menu while hidden, then display it exactly once.
        ApplyTheme();
        ShowMenu(position);
    }

    private void BuildMenu(Snapshot? snapshot, string? status = null)
    {
        var oldItems = menu.Items.Cast<ToolStripItem>().ToArray();
        menu.Items.Clear();
        foreach (var item in oldItems) item.Dispose();
        var about = new ClickOnlyMenuItem("About");
        about.DropDownItems.Add("Check for updates", null, async (_, _) => await CheckForUpdatesAsync());
        about.DropDownItems.Add("Open on GitHub", null, (_, _) => OpenGitHub(GitHubUpdates.Repository));
        menu.Items.Add(about);
        menu.Items.Add(new ToolStripSeparator());
        Add("SONAR  /  GAMING", enabled: false);
        menu.Items.Add(new ToolStripSeparator());
        if (snapshot != null)
        {
            if (snapshot.Favorites.Count == 0) Add("No favorites — add some in GG → Sonar → Gaming", enabled: false);
            foreach (var preset in snapshot.Favorites)
            {
                var item = Add(preset.Name, async () => await SelectAsync(preset));
                item.Checked = preset.Id == snapshot.SelectedId;
                item.ToolTipText = preset.Name;
            }
        }
        else Add(status ?? "Sonar unavailable", enabled: false);
        menu.Items.Add(new ToolStripSeparator());
        Add("Refresh favorites", async () => { menu.Close(); await OpenMenuAsync(); });
        if (error != null) Add("Show connection error…", () => MessageBox.Show(error,
            "Sonar Favorites", MessageBoxButtons.OK, MessageBoxIcon.Information));
        var startup = Add("Start silently with Windows", ToggleStartup);
        startup.Checked = IsStartupEnabled();
        menu.Items.Add(new ToolStripSeparator());
        Add("Exit", ExitThread);
    }

    private ToolStripMenuItem Add(string text, Action? action = null, bool enabled = true)
    {
        var item = new ToolStripMenuItem { Text = text, Enabled = enabled };
        // Preset names may contain ampersands; do not interpret them as shortcuts.
        item.Text = text.Replace("&", "&&");
        if (action != null) item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private async Task SelectAsync(Preset preset)
    {
        if (busy || disposed) return;
        busy = true;
        try
        {
            await sonar.SelectAsync(preset.Id);
            if (!disposed) tray.Text = Truncate("Sonar • " + preset.Name, 63);
        }
        catch (Exception ex)
        {
            if (!disposed) tray.ShowBalloonTip(5000, "Could not switch Gaming preset", FriendlyError(ex), ToolTipIcon.Warning);
        }
        finally { busy = false; }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (checkingUpdates || disposed) return;
        checkingUpdates = true;
        try
        {
            using var updates = new GitHubUpdates();
            var release = await updates.GetLatestAsync();
            if (disposed) return;
            if (release == null)
                MessageBox.Show("No releases have been published yet.", "Sonar Favorite Picker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (release.Version <= GitHubUpdates.CurrentVersion)
                MessageBox.Show($"You're up to date (v{GitHubUpdates.CurrentVersion.ToString(3)}).", "Sonar Favorite Picker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            else if (MessageBox.Show($"Version {release.Tag} is available. You have v{GitHubUpdates.CurrentVersion.ToString(3)}.\n\nOpen the release page to download it?",
                "Sonar Favorite Picker", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                OpenGitHub(release.Page);
        }
        catch (Exception ex)
        {
            if (!disposed) MessageBox.Show("Could not check for updates. Check your internet connection and try again.\n\n" + ex.Message,
                "Sonar Favorite Picker", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        finally { checkingUpdates = false; }
    }

    private static void OpenGitHub(Uri page)
    {
        try { Process.Start(new ProcessStartInfo(page.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("Could not open your browser.\n\n" + ex.Message, "Sonar Favorite Picker"); }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Windows denied access to SteelSeries GG settings. Check the permissions on coreProps.json.",
        HttpRequestException => "Could not connect to Sonar. Start SteelSeries GG, enable Sonar, and try Refresh favorites.",
        TaskCanceledException => "Sonar took too long to respond. Wait for GG to finish starting, then refresh.",
        IOException => ex.Message,
        _ => "Could not read Sonar settings. GG may still be starting or its local API may have changed. " + ex.Message
    };

    private static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(StartupKey);
        return string.Equals(key?.GetValue(StartupName) as string, StartupCommand, StringComparison.OrdinalIgnoreCase);
    }

    private static string StartupCommand => $"\"{Environment.ProcessPath}\" --silent";

    private void ToggleStartup()
    {
        try
        {
            bool enabled = IsStartupEnabled();
            using var key = Registry.CurrentUser.CreateSubKey(StartupKey);
            if (enabled) key.DeleteValue(StartupName, false);
            else key.SetValue(StartupName, StartupCommand);
        }
        catch (Exception ex) { MessageBox.Show("Could not update Windows startup: " + ex.Message, "Sonar Favorites"); }
    }

    private void ApplyTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        bool dark = !SystemInformation.HighContrast && key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        menu.Renderer = SystemInformation.HighContrast ? new ToolStripSystemRenderer() : new ToolStripProfessionalRenderer(new MenuColors(dark));
        menu.BackColor = dark ? Color.FromArgb(32, 32, 32) : SystemColors.Menu;
        menu.ForeColor = dark ? Color.FromArgb(242, 242, 242) : SystemColors.MenuText;
        foreach (var item in menu.Items.OfType<ToolStripMenuItem>().Where(item => item.HasDropDownItems))
        {
            item.DropDown.Renderer = menu.Renderer;
            item.DropDown.BackColor = menu.BackColor;
            item.DropDown.ForeColor = menu.ForeColor;
            item.DropDown.Font = menu.Font;
        }
    }

    private void ShowMenu(Point position)
    {
        SetForegroundWindow(dispatcher.Handle);
        menu.Show(position);
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    internal static Icon CreateIcon()
    {
        using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("SonarFavoritePicker.sonar.ico")
            ?? throw new InvalidOperationException("The embedded Sonar icon is missing.");
        // Load a DPI-appropriate PNG frame; retain its full alpha channel.
        var size = SystemInformation.SmallIconSize;
        using var original = new Icon(stream, size);
        return (Icon)original.Clone();
    }

    protected override void Dispose(bool disposing)
    {
        disposed = true;
        if (disposing)
        {
            tray.Visible = false;
            tray.Dispose(); menu.Dispose(); icon.Dispose(); dispatcher.Dispose(); sonar.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
}

internal sealed class MenuColors(bool dark) : ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => dark ? Color.FromArgb(32, 32, 32) : SystemColors.Menu;
    public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
    public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
    public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
    public override Color MenuBorder => dark ? Color.FromArgb(64, 64, 64) : Color.FromArgb(200, 200, 200);
    public override Color MenuItemSelected => dark ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 237, 245);
    public override Color MenuItemBorder => MenuItemSelected;
    public override Color MenuItemSelectedGradientBegin => MenuItemSelected;
    public override Color MenuItemSelectedGradientEnd => MenuItemSelected;
    public override Color SeparatorDark => MenuBorder;
    public override Color SeparatorLight => ToolStripDropDownBackground;
    public override Color CheckBackground => MenuItemSelected;
    public override Color CheckSelectedBackground => MenuItemSelected;
    public override Color CheckPressedBackground => MenuItemSelected;
}
