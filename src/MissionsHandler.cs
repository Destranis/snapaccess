using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SnapAccess;

/// <summary>
/// Specialized handler for the Missions screen.
/// </summary>
public class MissionsHandler : IHandler
{
    private class MissionInfo
    {
        public string Title;
        public string Desc;
        public string Progress;
        public string Goal;
        public string Reward;
        public Button ClaimButton;
    }

    private class MissionCategory
    {
        public string Name;
        public List<MissionInfo> Missions = new List<MissionInfo>();
    }

    private readonly List<MissionCategory> _categories = new List<MissionCategory>();
    private int _catIndex = 0;
    private int _missionIndex = 0;
    private int _menuLevel = 0; // 0=Categories, 1=Missions
    private bool _isActive = false;
    private bool _activated = false;
    private float _lastScanTime = 0f;

    public bool IsActive => _isActive && _activated;

    /// <summary>Explicitly activate this handler (called from MainMenuHandler).</summary>
    public void Activate()
    {
        _activated = true;
        _lastScanTime = 0f;
        _menuLevel = 0;
        _catIndex = 0;
    }

    public bool Update()
    {
        if (!_activated) return false;

        if (!ScanMissions())
        {
            _isActive = false;
            return false;
        }

        _isActive = true;
        ProcessInput();
        return true;
    }

    private bool ScanMissions()
    {
        if (Time.time - _lastScanTime < 1.5f) return _isActive;
        _lastScanTime = Time.time;

        // Try to find the missions container
        GameObject root = GameObject.Find("MissionsSection") ?? GameObject.Find("Mission_Landscape");
        if (root == null || !root.activeInHierarchy) return false;

        _categories.Clear();

        // 1. Daily Missions
        GameObject dailyRoot = GameObject.Find("DailyMissionsPanel");
        if (dailyRoot != null && dailyRoot.activeInHierarchy)
        {
            var cat = new MissionCategory { Name = "Daily Missions" };
            cat.Missions = ScanTiles(dailyRoot);
            if (cat.Missions.Count > 0) _categories.Add(cat);
        }

        // 2. Season Pass Missions
        GameObject seasonRoot = GameObject.Find("SeasonPassMissionPanel");
        if (seasonRoot != null && seasonRoot.activeInHierarchy)
        {
            var cat = new MissionCategory { Name = "Season Missions" };
            cat.Missions = ScanTiles(seasonRoot);
            if (cat.Missions.Count > 0) _categories.Add(cat);
        }

        // Fallback
        if (_categories.Count == 0)
        {
            var cat = new MissionCategory { Name = "All Missions" };
            cat.Missions = ScanTiles(root);
            if (cat.Missions.Count > 0) _categories.Add(cat);
        }

        return _categories.Count > 0;
    }

    private List<MissionInfo> ScanTiles(GameObject root)
    {
        var list = new List<MissionInfo>();
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        foreach (var btn in buttons)
        {
            if (btn == null || !btn.gameObject.activeInHierarchy) continue;
            if (!btn.name.Contains("tile_pc_main_missions")) continue;

            var info = ReadMissionTile(btn.gameObject);
            if (info != null)
            {
                info.ClaimButton = btn;
                list.Add(info);
            }
        }
        return list;
    }

    private MissionInfo ReadMissionTile(GameObject tile)
    {
        var info = new MissionInfo();
        TMP_Text[] texts = tile.GetComponentsInChildren<TMP_Text>(true);
        foreach (var t in texts)
        {
            string n = t.gameObject.name.ToLower();
            string v = UIHelper.StripRichText(t.text);
            if (string.IsNullOrEmpty(v)) continue;

            if (n.Contains("title")) info.Title = v;
            else if (n.Contains("description")) info.Desc = v;
            else if (n.Contains("progress")) info.Progress = v;
            else if (n.Contains("goal")) info.Goal = v;
            else if (n.Contains("credit")) info.Reward += v + " credits ";
            else if (n.Contains("battlepass")) info.Reward += v + " XP ";
        }
        return string.IsNullOrEmpty(info.Title) ? null : info;
    }

    private void ProcessInput()
    {
        if (SDLInput.IsKeyDown(SDLInput.Key.Left) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadLeft)) MovePrev();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight)) MoveNext();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Down) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown)) ReadDetails();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South)) Enter();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape)) Back();
    }

    private void MovePrev()
    {
        if (_menuLevel == 0) { _catIndex = (_catIndex - 1 + _categories.Count) % _categories.Count; AnnounceCat(); }
        else { _missionIndex = (_missionIndex - 1 + _categories[_catIndex].Missions.Count) % _categories[_catIndex].Missions.Count; AnnounceMissionTitle(); }
    }

    private void MoveNext()
    {
        if (_menuLevel == 0) { _catIndex = (_catIndex + 1) % _categories.Count; AnnounceCat(); }
        else { _missionIndex = (_missionIndex + 1) % _categories[_catIndex].Missions.Count; AnnounceMissionTitle(); }
    }

    private void ReadDetails()
    {
        if (_menuLevel == 0)
        {
            if (_catIndex >= 0 && _catIndex < _categories.Count)
                ScreenReader.Say(_categories[_catIndex].Missions.Count + " missions in this category.");
        }
        else
        {
            // Read full mission details on Down
            AnnounceMission();
        }
    }

    private void Enter()
    {
        if (_menuLevel == 0) { _menuLevel = 1; _missionIndex = 0; AnnounceMissionTitle(); }
        else
        {
            var m = _categories[_catIndex].Missions[_missionIndex];
            ScreenReader.Say("Claiming mission...");
            UIHelper.ClickButton(m.ClaimButton);
        }
    }

    private void Back()
    {
        if (_menuLevel == 1) { _menuLevel = 0; AnnounceCat(); }
        else { _isActive = false; _activated = false; }
    }

    private void AnnounceCat() => ScreenReader.Say(_categories[_catIndex].Name + ", " + _categories[_catIndex].Missions.Count + " missions.");

    /// <summary>Short announcement when navigating missions with Left/Right.</summary>
    private void AnnounceMissionTitle()
    {
        var m = _categories[_catIndex].Missions[_missionIndex];
        ScreenReader.Say(m.Title + ", " + (_missionIndex + 1) + " of " + _categories[_catIndex].Missions.Count);
    }

    /// <summary>Full details when pressing Down.</summary>
    private void AnnounceMission()
    {
        var m = _categories[_catIndex].Missions[_missionIndex];
        ScreenReader.Say(m.Title + ". " + m.Desc + ". Progress " + m.Progress + " of " + m.Goal + ". Reward: " + m.Reward);
    }

    public void AnnounceContext() => AnnounceMission();
    public void Reset() { _isActive = false; _activated = false; _menuLevel = 0; }
}
