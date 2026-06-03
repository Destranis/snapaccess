using System;
using System.Net.Http;
using System.Threading.Tasks;
using MelonLoader;

namespace SnapAccess;

/// <summary>
/// Checks GitHub releases for a newer version of the mod on startup.
/// Inspired by AccessibleArena's UpdateChecker pattern.
/// Announces to the user if a new version is available.
/// </summary>
public static class UpdateChecker
{
    private const string CurrentVersion = "0.6";
    private const string GitHubApiUrl = "https://api.github.com/repos/{owner}/{repo}/releases/latest";

    // Set these to the actual GitHub repo when published
    private static string _owner = "";
    private static string _repo = "";

    private static bool _checked = false;
    private static string _latestVersion = null;

    /// <summary>Whether a newer version was found.</summary>
    public static bool UpdateAvailable => IsNewer(CurrentVersion, _latestVersion);

    /// <summary>The latest version string, or null if not checked yet.</summary>
    public static string LatestVersion => _latestVersion;

    /// <summary>The current mod version.</summary>
    public static string Version => CurrentVersion;

    /// <summary>
    /// Kick off an async check for updates. Non-blocking.
    /// Call once on mod load. Results available later via UpdateAvailable/LatestVersion.
    /// </summary>
    public static void CheckAsync()
    {
        if (_checked || string.IsNullOrEmpty(_owner) || string.IsNullOrEmpty(_repo)) return;
        _checked = true;

        Task.Run(async () =>
        {
            try
            {
                using HttpClient client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "SnapAccess-Mod");
                client.Timeout = TimeSpan.FromSeconds(10);

                string url = GitHubApiUrl.Replace("{owner}", _owner).Replace("{repo}", _repo);
                string json = await client.GetStringAsync(url);

                string tag = ParseTagName(json);
                if (tag == null) return;

                _latestVersion = tag;

                if (IsNewer(CurrentVersion, tag))
                {
                    MelonLogger.Msg($"SnapAccess update available: v{tag} (current: v{CurrentVersion})");
                    // Announce will happen in Main.cs after startup delay
                }
                else
                {
                    MelonLogger.Msg($"SnapAccess is up to date (v{CurrentVersion})");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"Update check failed: {ex.Message}");
            }
        });
    }

    /// <summary>Configure the GitHub repository for update checks.</summary>
    public static void Configure(string owner, string repo)
    {
        _owner = owner;
        _repo = repo;
    }

    /// <summary>
    /// Extracts the release version from a GitHub "latest release" JSON payload.
    /// Reads the first <c>"tag_name"</c> field and strips a leading <c>v</c>.
    /// Returns null if no tag_name field is present. Avoids a JSON dependency.
    /// </summary>
    public static string ParseTagName(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;

        int tagIdx = json.IndexOf("\"tag_name\"", StringComparison.Ordinal);
        if (tagIdx < 0) return null;
        int colonIdx = json.IndexOf(':', tagIdx);
        if (colonIdx < 0) return null;
        int quoteStart = json.IndexOf('"', colonIdx + 1);
        if (quoteStart < 0) return null;
        int quoteEnd = json.IndexOf('"', quoteStart + 1);
        if (quoteEnd < 0) return null;

        string tag = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
        if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            tag = tag.Substring(1);
        return tag;
    }

    /// <summary>
    /// Returns true only when <paramref name="latest"/> is a strictly higher
    /// semantic version than <paramref name="current"/>. Components are compared
    /// numerically (so 0.10.0 > 0.9.0), not lexically. Equal versions and
    /// downgrades return false; a null/empty latest returns false.
    /// </summary>
    public static bool IsNewer(string current, string latest)
    {
        if (string.IsNullOrEmpty(latest)) return false;
        if (string.IsNullOrEmpty(current)) return true;
        return CompareVersions(latest, current) > 0;
    }

    private static int CompareVersions(string a, string b)
    {
        string[] pa = a.Split('.');
        string[] pb = b.Split('.');
        int len = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < len; i++)
        {
            int na = i < pa.Length && int.TryParse(pa[i], out int x) ? x : 0;
            int nb = i < pb.Length && int.TryParse(pb[i], out int y) ? y : 0;
            if (na != nb) return na.CompareTo(nb);
        }
        return 0;
    }
}
