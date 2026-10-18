using System.Text.Json;

namespace SonarFavoritePicker;

internal sealed record Preset(string Id, string Name, int Position);
internal sealed record Snapshot(IReadOnlyList<Preset> Favorites, string? SelectedId, string? SelectedName);

internal sealed class SonarClient : IDisposable
{
    private readonly HttpClient http;
    private readonly string propsPath;

    public SonarClient(HttpMessageHandler? handler = null, string? propsPath = null)
    {
        this.propsPath = propsPath ?? Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.CommonApplicationData), "SteelSeries", "GG", "coreProps.json");
        http = new HttpClient(handler ?? new HttpClientHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            // GG uses a self-signed certificate. Only loopback requests are permitted.
            ServerCertificateCustomValidationCallback = (request, _, _, _) => request.RequestUri?.IsLoopback == true
        }) { Timeout = TimeSpan.FromSeconds(4) };
    }

    internal static Uri LocalAddress(string address)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            !uri.IsLoopback || (uri.Scheme != "http" && uri.Scheme != "https") ||
            !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Sonar returned an invalid local server address.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
    }

    private async Task<Uri> DiscoverAsync()
    {
        if (!File.Exists(propsPath))
            throw new IOException("SteelSeries GG is unavailable. Start GG and enable Sonar, then try again.");
        using var props = JsonDocument.Parse(await File.ReadAllTextAsync(propsPath));
        var gg = LocalAddress("https://" + props.RootElement.GetProperty("ggEncryptedAddress").GetString());
        using var apps = await GetAsync(new Uri(gg, "subApps"));
        var sonar = apps.RootElement.GetProperty("subApps").GetProperty("sonar");
        if (!sonar.TryGetProperty("isRunning", out var running) || !running.GetBoolean())
            throw new IOException("Sonar is not running. Enable Sonar in SteelSeries GG, then try again.");
        var address = sonar.GetProperty("metadata").GetProperty("webServerAddress").GetString();
        return LocalAddress(address ?? "");
    }

    private async Task<JsonDocument> GetAsync(Uri uri)
    {
        using var response = await http.GetAsync(uri);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    internal static Snapshot Parse(string configs, string selected)
    {
        using var all = JsonDocument.Parse(configs);
        using var active = JsonDocument.Parse(selected);
        var favorites = all.RootElement.EnumerateArray()
            .Where(IsGame)
            .Where(x => x.TryGetProperty("isFavorite", out var f) && f.ValueKind == JsonValueKind.True)
            .Select(x => new Preset(x.GetProperty("id").GetString()!, x.GetProperty("name").GetString() ?? "Unnamed preset",
                x.TryGetProperty("favoritePosition", out var p) && p.TryGetInt32(out var pos) && pos >= 0 ? pos : int.MaxValue))
            .OrderBy(x => x.Position).ToArray();
        var game = active.RootElement.EnumerateArray().FirstOrDefault(IsGame);
        return new Snapshot(favorites,
            game.ValueKind == JsonValueKind.Object ? game.GetProperty("id").GetString() : null,
            game.ValueKind == JsonValueKind.Object ? game.GetProperty("name").GetString() : null);
    }

    private static bool IsGame(JsonElement x) => x.TryGetProperty("virtualAudioDevice", out var device) && device.GetString() == "game";

    public async Task<Snapshot> ReadAsync()
    {
        var server = await DiscoverAsync();
        var configs = GetAsync(new Uri(server, "configs"));
        var selected = GetAsync(new Uri(server, "configs/selected"));
        // Dispose both successful responses even if the other request fails.
        try
        {
            await Task.WhenAll(configs, selected);
            return Parse(configs.Result.RootElement.GetRawText(), selected.Result.RootElement.GetRawText());
        }
        finally
        {
            if (configs.IsCompletedSuccessfully) configs.Result.Dispose();
            if (selected.IsCompletedSuccessfully) selected.Result.Dispose();
        }
    }

    public async Task SelectAsync(string id)
    {
        var server = await DiscoverAsync();
        // Validate again against current Gaming favorites before making a change.
        using var all = await GetAsync(new Uri(server, "configs"));
        var valid = Parse(all.RootElement.GetRawText(), "[]").Favorites.Any(p => p.Id == id);
        if (!valid) throw new IOException("This preset is no longer a Gaming favorite. Reopen the menu to refresh.");
        using var response = await http.PutAsync(new Uri(server, $"configs/{Uri.EscapeDataString(id)}/select"), null);
        response.EnsureSuccessStatusCode();
        using var selected = await GetAsync(new Uri(server, "configs/selected"));
        if (Parse("[]", selected.RootElement.GetRawText()).SelectedId != id)
            throw new IOException("Sonar did not confirm the preset change. Try again.");
    }

    public void Dispose() => http.Dispose();
}
