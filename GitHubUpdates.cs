using System.Net;

namespace SonarFavoritePicker;

internal sealed record GitHubRelease(Version Version, string Tag, Uri Page);

internal sealed class GitHubUpdates : IDisposable
{
    public static readonly Uri Repository = new("https://github.com/codeineXO/SonarFavoritePicker");
    public static readonly Uri LatestReleasePage = new(Repository.AbsoluteUri + "/releases/latest");
    public static Version CurrentVersion => typeof(GitHubUpdates).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
    private readonly HttpClient http;

    public GitHubUpdates(HttpMessageHandler? handler = null)
    {
        http = new HttpClient(handler ?? new HttpClientHandler { UseCookies = false, MaxAutomaticRedirections = 5 });
        http.Timeout = TimeSpan.FromSeconds(10);
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"SonarFavoritePicker/{CurrentVersion.ToString(3)}");
    }

    public async Task<GitHubRelease?> GetLatestAsync()
    {
        // GitHub's documented public latest-release URL redirects to the tag.
        // HEAD follows that redirect without downloading HTML or using the REST API quota.
        using var request = new HttpRequestMessage(HttpMethod.Head, LatestReleasePage);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (response.StatusCode == HttpStatusCode.Forbidden || (int)response.StatusCode == 429)
            throw new HttpRequestException("GitHub temporarily declined the request. You can check the release page in your browser.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();
        var page = response.RequestMessage?.RequestUri
            ?? throw new InvalidDataException("GitHub did not return a release page address.");
        if (page == new Uri(Repository.AbsoluteUri + "/releases")) return null;
        return Parse(page);
    }

    internal static GitHubRelease Parse(Uri page)
    {
        var prefix = Repository.AbsolutePath + "/releases/tag/";
        if (page.Scheme != Uri.UriSchemeHttps || page.Host != Repository.Host ||
            !string.IsNullOrEmpty(page.UserInfo) || !page.IsDefaultPort ||
            !page.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("GitHub returned an unexpected release page address.");
        var tag = Uri.UnescapeDataString(page.AbsolutePath[prefix.Length..]);
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version))
            throw new InvalidDataException("The latest release has an unrecognized version number.");
        version = new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
        return new GitHubRelease(version, tag, page);
    }

    public void Dispose() => http.Dispose();
}