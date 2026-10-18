using System.Net;
using System.Text;

namespace SonarFavoritePicker;

internal static class SelfTests
{
    public static async Task RunUpdateCheckAsync()
    {
        try
        {
            using var updates = new GitHubUpdates();
            var release = await updates.GetLatestAsync();
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "update-test-results.txt"),
                release == null ? "PASS: no published releases" : $"PASS: latest release {release.Tag}; current {GitHubUpdates.CurrentVersion.ToString(3)}; page {release.Page}");
        }
        catch (Exception ex)
        {
            Environment.ExitCode = 1;
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "update-test-results.txt"), "FAIL: " + ex);
        }
    }

    public static async Task RunLiveAsync()
    {
        var results = new List<string>();
        using var client = new SonarClient();
        string? restore = null;
        try
        {
            var before = await client.ReadAsync();
            results.Add($"Read {before.Favorites.Count} Gaming favorites; selected: {before.SelectedName}");
            foreach (var p in before.Favorites) results.Add($"Slot {p.Position}: {p.Name}");
            if (before.SelectedId == null || !before.Favorites.Any(p => p.Id == before.SelectedId))
                throw new InvalidOperationException("Live switch test requires the current preset to be a favorite so it can be restored.");
            restore = before.SelectedId;
            var target = before.Favorites.FirstOrDefault(p => p.Id != restore)
                ?? throw new InvalidOperationException("Live switch test requires two favorites.");
            await client.SelectAsync(target.Id);
            Require((await client.ReadAsync()).SelectedId == target.Id, "Live selected preset");
            results.Add("PASS: switched to " + target.Name + " and confirmed through Sonar");
        }
        catch (Exception ex) { results.Add("FAIL: " + ex); Environment.ExitCode = 1; }
        finally
        {
            if (restore != null)
            {
                try { await client.SelectAsync(restore); results.Add("PASS: restored original preset"); }
                catch (Exception ex) { results.Add("RESTORE FAILED: " + ex); Environment.ExitCode = 1; }
            }
            File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "live-test-results.txt"), results);
        }
    }

    public static void Run()
    {
        var results = new List<string>();
        try
        {
            ApplicationConfiguration.Initialize();
            using (var root = new ContextMenuStrip())
            using (var about = new TestAboutItem())
            {
                root.Items.Add(about);
                about.DropDownItems.Add("Check for updates");
                about.DropDownItems.Add("Open on GitHub");
                about.ShowDropDown();
                Require(!about.DropDown.Visible, "About must reject automatic submenu opening");
                about.Hover();
                about.ShowDropDown();
                Require(!about.DropDown.Visible, "Hover must not open About");
                about.MouseActivate();
                Require(about.DropDown.Visible, "Click must open About submenu");
                about.HideDropDown();
                about.Select();
                Require(about.KeyboardActivate() && about.DropDown.Visible, "Explicit keyboard activation must open About");
                about.HideDropDown();
                results.Add("PASS: About submenu rejects automatic/hover opening and opens on click");
            }
            // The console-style test runner has no WinForms message loop.
            SynchronizationContext.SetSynchronizationContext(null);
            var releasePage = "https://github.com/codeineXO/SonarFavoritePicker/releases/tag/v1.0.0";
            Require(GitHubUpdates.Parse(new Uri(releasePage)).Version == new Version(1, 0, 0, 0), "Normalize release versions");
            var newer = GitHubUpdates.Parse(new Uri(releasePage.Replace("1.0.0", "1.1.0")));
            Require(newer.Version > GitHubUpdates.CurrentVersion, "Newer GitHub release comparison");
            foreach (var invalid in new[] { releasePage.Replace("github.com/", "example.com/"), releasePage.Replace("v1.0.0", "not-a-version") })
            {
                try { GitHubUpdates.Parse(new Uri(invalid)); throw new Exception("Accepted invalid release"); }
                catch (InvalidDataException) { }
            }
            using (var noReleases = new GitHubUpdates(new FakeRelease(HttpStatusCode.NotFound, "{}")))
                Require(noReleases.GetLatestAsync().GetAwaiter().GetResult() == null, "No published releases");
            using (var available = new GitHubUpdates(new FakeRelease(HttpStatusCode.OK, releasePage)))
                Require(available.GetLatestAsync().GetAwaiter().GetResult()?.Tag == "v1.0.0", "GitHub release response");
            using (var limited = new GitHubUpdates(new FakeRelease(HttpStatusCode.Forbidden, "{}")))
            {
                try { limited.GetLatestAsync().GetAwaiter().GetResult(); throw new Exception("Accepted rate-limited response"); }
                catch (HttpRequestException) { }
            }
            using (var emptyPage = new GitHubUpdates(new FakeRelease(HttpStatusCode.OK, GitHubUpdates.Repository.AbsoluteUri + "/releases")))
                Require(emptyPage.GetLatestAsync().GetAwaiter().GetResult() == null, "Empty releases page redirect");
            Require(GitHubUpdates.Parse(new Uri(releasePage.Replace("v1.0.0", "1.0.0"))).Version == new Version(1, 0, 0, 0), "Unprefixed release tag");
            using (var limited = new GitHubUpdates(new FakeRelease((HttpStatusCode)429, "{}")))
            {
                try { limited.GetLatestAsync().GetAwaiter().GetResult(); throw new Exception("Accepted throttled response"); }
                catch (HttpRequestException) { }
            }
            results.Add("PASS: public release redirect checks, version comparison, no releases, rate limiting, release URL validation");
            using (var icon = TrayApp.CreateIcon())
            using (var bitmap = icon.ToBitmap())
            {
                Require(bitmap.GetPixel(0, 0).A == 0, "Icon corners must be transparent");
                bool hasOriginalColor = false;
                for (int y = 0; y < bitmap.Height; y++)
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        if (pixel.A > 200 && pixel.B > pixel.R + 15 && pixel.R > 150)
                            hasOriginalColor = true;
                    }
                Require(hasOriginalColor, "Icon must preserve Sonar's pale blue color");
                results.Add("PASS: embedded tray icon transparency and original color");
            }
            const string configs = """
                [
                  {"id":"second","name":"Music & Bass","virtualAudioDevice":"game","isFavorite":true,"favoritePosition":8},
                  {"id":"chat","name":"Chat","virtualAudioDevice":"chatRender","isFavorite":true,"favoritePosition":0},
                  {"id":"first","name":"FPS","virtualAudioDevice":"game","isFavorite":true,"favoritePosition":0},
                  {"id":"other","name":"Not favorite","virtualAudioDevice":"game","isFavorite":false}
                ]
                """;
            const string selected = """[{"id":"first","name":"FPS","virtualAudioDevice":"game"}]""";
            var snapshot = SonarClient.Parse(configs, selected);
            Require(snapshot.Favorites.Select(p => p.Id).SequenceEqual(["first", "second"]), "Only Gaming favorites, ordered by slot");
            Require(snapshot.SelectedId == "first", "Current Gaming selection");
            Require(SonarClient.Parse("[]", "[]").Favorites.Count == 0, "Empty favorites");
            results.Add("PASS: filtering, slot order, selected preset, empty list");
            foreach (var address in new[] { "https://example.com", "file:///C:/test", "http://user:password@localhost:80" })
            {
                try { SonarClient.LocalAddress(address); throw new Exception("Accepted remote/invalid address"); }
                catch (InvalidDataException) { }
            }
            Require(SonarClient.LocalAddress("http://127.0.0.1:12345").IsLoopback, "Loopback accepted");
            results.Add("PASS: local-only address validation");

            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, """{"ggEncryptedAddress":"127.0.0.1:1234"}""");
                using var handler = new FakeSonar(configs, selected);
                using var client = new SonarClient(handler, path);
                Require(client.ReadAsync().GetAwaiter().GetResult().Favorites.Count == 2, "Discovery and list");
                client.SelectAsync("second").GetAwaiter().GetResult();
                Require(handler.Selection == "second", "PUT selection and confirmation");
                try { client.SelectAsync("chat").GetAwaiter().GetResult(); throw new Exception("Accepted Chat favorite"); }
                catch (IOException) { }
                Require(handler.PutCount == 1, "Rejected selection must not send PUT");
                handler.Running = false;
                try { client.ReadAsync().GetAwaiter().GetResult(); throw new Exception("Accepted stopped Sonar"); }
                catch (IOException) { }
                results.Add("PASS: discovery, read, switch/confirm, reject other channels, stopped Sonar");
            }
            finally { File.Delete(path); }
            results.Add("ALL TESTS PASSED");
        }
        catch (Exception ex) { results.Add("FAIL: " + ex); Environment.ExitCode = 1; }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "self-test-results.txt"), results);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private sealed class TestAboutItem() : ClickOnlyMenuItem("About")
    {
        public void Hover() => OnMouseEnter(EventArgs.Empty);
        public void MouseActivate() => OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, 1, 1, 0));
        public bool KeyboardActivate() => ProcessDialogKey(Keys.Enter);
    }

    private sealed class FakeRelease(HttpStatusCode status, string finalPage) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Require(request.RequestUri?.AbsoluteUri == GitHubUpdates.LatestReleasePage.AbsoluteUri, "Correct release repository");
            Require(request.Headers.UserAgent.Count > 0, "GitHub requires a User-Agent");
            Require(request.Method == HttpMethod.Head, "Check redirects without downloading HTML or using the REST API");
            return Task.FromResult(new HttpResponseMessage(status)
            { RequestMessage = new HttpRequestMessage(HttpMethod.Head, status == HttpStatusCode.OK ? finalPage : request.RequestUri!.AbsoluteUri) });
        }
    }

    private sealed class FakeSonar(string configs, string selected) : HttpMessageHandler
    {
        public string Selection = "first";
        public int PutCount;
        public bool Running = true;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            string json;
            if (path == "/subApps") json = System.Text.Json.JsonSerializer.Serialize(new
            { subApps = new { sonar = new { isRunning = Running, metadata = new { webServerAddress = "http://127.0.0.1:5678" } } } });
            else if (request.Method == HttpMethod.Put && path == "/configs/second/select")
            { PutCount++; Selection = "second"; json = "{}"; }
            else if (path == "/configs") json = configs;
            else if (path == "/configs/selected") json = Selection == "first" ? selected : """[{"id":"second","name":"Music & Bass","virtualAudioDevice":"game"}]""";
            else throw new Exception("Unexpected request: " + request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
