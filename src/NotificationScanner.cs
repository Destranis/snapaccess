using System;
using System.Collections.Generic;
using Il2CppCubeUnity.Ui;
using Il2CppTMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Scans for active notification badges in the UI and reports unread counts.
/// Uses the game's NotificationBadge MonoBehaviour components.
/// </summary>
public static class NotificationScanner
{
    /// <summary>
    /// Reads all visible notification badges and returns a summary string.
    /// Returns empty string if no notifications are active.
    /// </summary>
    public static string GetNotificationSummary()
    {
        try
        {
            var badges = Object.FindObjectsOfType<NotificationBadge>();
            if (badges == null || badges.Count == 0) return "";

            var notifications = new List<string>();
            for (int i = 0; i < badges.Count; i++)
            {
                var badge = badges[i];
                if ((Object)(object)badge == (Object)null) continue;
                if (!((Component)badge).gameObject.activeInHierarchy) continue;

                string count = "";
                if ((Object)(object)badge._NotificationAmountText != (Object)null)
                {
                    count = badge._NotificationAmountText.text?.Trim();
                }

                // Walk up parent hierarchy to find context (what screen/section this badge belongs to)
                string context = GetBadgeContext(((Component)badge).transform);
                if (string.IsNullOrEmpty(context)) continue;

                if (!string.IsNullOrEmpty(count) && count != "0")
                    notifications.Add(Loc.Get("notif_badge_count", context, count));
                else
                    notifications.Add(Loc.Get("notif_badge", context));
            }

            if (notifications.Count == 0) return "";
            return string.Join(". ", notifications);
        }
        catch (Exception ex)
        {
            DebugLogger.Log(LogCategory.Handler, "NotificationScanner", $"Error: {ex.Message}");
            return "";
        }
    }

    /// <summary>Walk up the transform hierarchy to find a meaningful parent name for context.</summary>
    private static string GetBadgeContext(Transform badgeTransform)
    {
        Transform current = badgeTransform.parent;
        int depth = 0;
        while ((Object)(object)current != (Object)null && depth < 8)
        {
            string name = ((Object)current.gameObject).name;
            // Skip generic container names
            if (!string.IsNullOrEmpty(name) &&
                !name.StartsWith("Badge") &&
                !name.StartsWith("Notification") &&
                !name.Contains("Container") &&
                !name.Contains("Root") &&
                name.Length > 2)
            {
                // Clean up common prefixes
                string clean = name.Replace("Button_", "")
                    .Replace("Tab_", "")
                    .Replace("_Button", "")
                    .Replace("_Tab", "")
                    .Replace("MenuBar_", "")
                    .Replace("(Clone)", "")
                    .Trim();
                if (clean.Length > 1) return clean;
            }
            current = current.parent;
            depth++;
        }
        return "";
    }
}
