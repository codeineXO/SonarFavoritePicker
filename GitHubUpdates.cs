using System.Net;
using System.Text.Json;

namespace SonarFavoritePicker;

internal sealed record GitHubRelease(Version Version, string Tag, Uri Page);

internal sealed class GitHubUpdates : IDisposable
{
    public static readonly Uri Repository = new("https://github.com/codeineXO/SonarFavoritePicker");
    public static Version CurrentVersion => typeof(GitHubUpdates).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
    private readonly HttpClient http;

    public GitHubUpdates(HttpMessageHandler? handler = null)
    {
        http = handler == null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SonarFavoritePicker/{CurrentVersion.ToString(3)}");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<GitHubRelease?> GetLatestAsync()
    {
        using var response = await http.GetAsync("https://api.github.com/repos/codeineXO/SonarFavoritePicker/releases/latest");
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
            throw new HttpRequestException("GitHub is limiting requests. Please try again later.");
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync());
    }

    internal static GitHubRelease Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            throw new InvalidDataException("The latest release has an unrecognized version number.");
        version = new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
        var url = root.GetProperty("html_url").GetString();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var page) ||
            page.Scheme != Uri.UriSchemeHttps || page.Host != Repository.Host ||
            !string.IsNullOrEmpty(page.UserInfo) || !page.IsDefaultPort ||
            !page.AbsolutePath.StartsWith(Repository.AbsolutePath + "/releases/tag/", StringComparison.Ordinal))
            throw new InvalidDataException("GitHub returned an unexpected release page address.");
        return new GitHubRelease(version, tag, page);
    }

    public void Dispose() => http.Dispose();
}
