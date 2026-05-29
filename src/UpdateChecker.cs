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
    private const string CurrentVersion = "0.5.0";
    private const string GitHubApiUrl = "https://api.github.com/repos/{owner}/{repo}/releases/latest";

    // Set these to the actual GitHub repo when published
    private static string _owner = "";
    private static string _repo = "";

    private static bool _checked = false;
    private static string _latestVersion = null;

    /// <summary>Whether a newer version was found.</summary>
    public static bool UpdateAvailable => _latestVersion != null && _latestVersion != CurrentVersion;

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

                // Simple JSON tag_name extraction (avoid dependency on JSON library)
                int tagIdx = json.IndexOf("\"tag_name\"", StringComparison.Ordinal);
                if (tagIdx < 0) return;
                int colonIdx = json.IndexOf(':', tagIdx);
                if (colonIdx < 0) return;
                int quoteStart = json.IndexOf('"', colonIdx + 1);
                if (quoteStart < 0) return;
                int quoteEnd = json.IndexOf('"', quoteStart + 1);
                if (quoteEnd < 0) return;

                string tag = json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                // Strip leading 'v' if present
                if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                    tag = tag.Substring(1);

                _latestVersion = tag;

                if (tag != CurrentVersion)
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
}
