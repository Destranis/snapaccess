using System;
using System.Collections.Generic;
using System.IO;
using MelonLoader;

namespace SnapAccess;

/// <summary>
/// Manages mod settings. Saved as JSON in UserData/SnapAccess.json.
/// Settings are toggled via the in-game settings navigator (F4 key).
/// </summary>
public class ModSettings
{
    private static ModSettings _instance;
    public static ModSettings Instance => _instance ??= Load();

    // --- Settings ---

    /// <summary>When true, announces "card X of Y" / "location X of Y" during navigation.</summary>
    public bool PositionCounts { get; set; } = true;

    /// <summary>When true, announces detailed card info (cost, power) on navigation. When false, just name.</summary>
    public bool VerboseCardInfo { get; set; } = true;

    /// <summary>When true, announces opponent actions (plays, reveals). When false, silent.</summary>
    public bool OpponentAnnouncements { get; set; } = true;

    /// <summary>When true, announces turn start automatically. When false, only on demand (T key).</summary>
    public bool AutoTurnAnnounce { get; set; } = true;

    /// <summary>When true, announces screen transitions ("Main Menu", "Battlefield", etc.).</summary>
    public bool TransitionAnnouncements { get; set; } = true;

    /// <summary>When true, announces tutorial hints and guidance messages.</summary>
    public bool TutorialMessages { get; set; } = true;

    // --- Settings metadata for the navigator ---

    public struct SettingDef
    {
        public string Key;
        public string LocKey;
        public string DescLocKey;
        public Func<ModSettings, bool> Get;
        public Action<ModSettings, bool> Set;
    }

    public static readonly List<SettingDef> AllSettings = new List<SettingDef>
    {
        new SettingDef {
            Key = "PositionCounts", LocKey = "mod_setting_position_counts",
            DescLocKey = "mod_setting_position_counts_desc",
            Get = s => s.PositionCounts, Set = (s, v) => s.PositionCounts = v
        },
        new SettingDef {
            Key = "VerboseCardInfo", LocKey = "mod_setting_verbose_cards",
            DescLocKey = "mod_setting_verbose_cards_desc",
            Get = s => s.VerboseCardInfo, Set = (s, v) => s.VerboseCardInfo = v
        },
        new SettingDef {
            Key = "OpponentAnnouncements", LocKey = "mod_setting_opponent",
            DescLocKey = "mod_setting_opponent_desc",
            Get = s => s.OpponentAnnouncements, Set = (s, v) => s.OpponentAnnouncements = v
        },
        new SettingDef {
            Key = "AutoTurnAnnounce", LocKey = "mod_setting_auto_turn",
            DescLocKey = "mod_setting_auto_turn_desc",
            Get = s => s.AutoTurnAnnounce, Set = (s, v) => s.AutoTurnAnnounce = v
        },
        new SettingDef {
            Key = "TransitionAnnouncements", LocKey = "mod_setting_transitions",
            DescLocKey = "mod_setting_transitions_desc",
            Get = s => s.TransitionAnnouncements, Set = (s, v) => s.TransitionAnnouncements = v
        },
        new SettingDef {
            Key = "TutorialMessages", LocKey = "mod_setting_tutorials",
            DescLocKey = "mod_setting_tutorials_desc",
            Get = s => s.TutorialMessages, Set = (s, v) => s.TutorialMessages = v
        },
    };

    // --- Save/Load ---

    private static readonly string SettingsPath = Path.Combine("UserData", "SnapAccess.json");

    public static ModSettings Load()
    {
        var settings = new ModSettings();
        try
        {
            if (!File.Exists(SettingsPath))
            {
                MelonLogger.Msg("No settings file found, using defaults.");
                return settings;
            }

            string json = File.ReadAllText(SettingsPath);
            ParseJson(json, settings);
            MelonLogger.Msg("Settings loaded from " + SettingsPath);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("Failed to load settings: " + ex.Message);
        }
        return settings;
    }

    public void Save()
    {
        try
        {
            string dir = Path.GetDirectoryName(SettingsPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string json = ToJson();
            File.WriteAllText(SettingsPath, json);
            MelonLogger.Msg("Settings saved to " + SettingsPath);
        }
        catch (Exception ex)
        {
            MelonLogger.Warning("Failed to save settings: " + ex.Message);
        }
    }

    private string ToJson()
    {
        return "{\n"
            + $"  \"PositionCounts\": {BoolStr(PositionCounts)},\n"
            + $"  \"VerboseCardInfo\": {BoolStr(VerboseCardInfo)},\n"
            + $"  \"OpponentAnnouncements\": {BoolStr(OpponentAnnouncements)},\n"
            + $"  \"AutoTurnAnnounce\": {BoolStr(AutoTurnAnnounce)},\n"
            + $"  \"TransitionAnnouncements\": {BoolStr(TransitionAnnouncements)},\n"
            + $"  \"TutorialMessages\": {BoolStr(TutorialMessages)}\n"
            + "}";
    }

    private static string BoolStr(bool v) => v ? "true" : "false";

    private static void ParseJson(string json, ModSettings settings)
    {
        // Simple key-value parser for flat boolean JSON
        foreach (var line in json.Split('\n'))
        {
            string trimmed = line.Trim().TrimEnd(',');
            if (!trimmed.Contains(':')) continue;

            int colonIdx = trimmed.IndexOf(':');
            string key = trimmed.Substring(0, colonIdx).Trim().Trim('"');
            string val = trimmed.Substring(colonIdx + 1).Trim().Trim('"').ToLower();
            bool boolVal = val == "true";

            switch (key)
            {
                case "PositionCounts": settings.PositionCounts = boolVal; break;
                case "VerboseCardInfo": settings.VerboseCardInfo = boolVal; break;
                case "OpponentAnnouncements": settings.OpponentAnnouncements = boolVal; break;
                case "AutoTurnAnnounce": settings.AutoTurnAnnounce = boolVal; break;
                case "TransitionAnnouncements": settings.TransitionAnnouncements = boolVal; break;
                case "TutorialMessages": settings.TutorialMessages = boolVal; break;
            }
        }
    }
}
