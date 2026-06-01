using System;
using System.Text.RegularExpressions;

namespace SnapAccess;

/// <summary>
/// Parses the game's turn-counter label, which arrives as TMP rich text such as
/// "&lt;size=490&gt;2&lt;/size&gt; / 6". The markup must be removed before the
/// "current / total" string is split, because the closing "&lt;/size&gt;" tag
/// itself contains a '/'.
/// </summary>
public static class TurnTextParser
{
    /// <summary>Removes TMP/HTML-style markup tags. Returns "" for null/empty input.</summary>
    public static string StripMarkup(string raw)
        => string.IsNullOrEmpty(raw) ? "" : Regex.Replace(raw, "<[^>]*>", "");

    /// <summary>
    /// Returns the current turn number as a string, the sentinel "FINAL" for the
    /// final turn, or null when the text is not a recognizable turn counter.
    /// </summary>
    public static string ParseCurrent(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;

        string clean = StripMarkup(raw).Trim();
        if (clean.IndexOf("FINAL", StringComparison.OrdinalIgnoreCase) >= 0)
            return "FINAL";

        int slash = clean.IndexOf('/');
        if (slash >= 0)
            return clean.Substring(0, slash).Trim();

        return null;
    }
}
