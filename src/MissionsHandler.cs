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
/// Two-level navigation: categories → missions.
/// Follows AccessibleArena pattern: title + status in browse, full details on Right.
/// </summary>
public class MissionsHandler : IScreenNavigator
{
    private class MissionInfo
    {
        public string Title;
        public string Desc;
        public string Progress;
        public string Goal;
        public string Reward;
        public bool IsComplete;
        public Button ClaimButton;
    }

    private class MissionCategory
    {
        public string Name;
        public List<MissionInfo> Missions = new List<MissionInfo>();
        public int CompletedCount;
    }

    private readonly List<MissionCategory> _categories = new List<MissionCategory>();
    private int _catIndex = 0;
    private int _missionIndex = 0;
    private int _menuLevel = 0; // 0=Categories, 1=Missions
    private bool _isActive = false;
    private bool _activated = false;
    private float _lastScanTime = 0f;
    private readonly KeyHoldRepeater _holdRepeater = new KeyHoldRepeater();

    public string NavigatorId => "Missions";
    public int Priority => 600;
    public bool IsActive => _isActive;

    /// <summary>
    /// Called by MainMenuHandler when user navigates to the Missions category.
    /// </summary>
    public void Activate()
    {
        _activated = true;
        _lastScanTime = 0f;
    }

    public void Update()
    {
        if (!_activated || !ScanMissions())
        {
            _isActive = false;
            return;
        }

        _isActive = true;
        ProcessInput();
    }

    private bool ScanMissions()
    {
        if (Time.time - _lastScanTime < 1.5f) return _isActive;
        _lastScanTime = Time.time;

        GameObject root = GameObject.Find("MissionsSection") ?? GameObject.Find("Mission_Landscape");
        if (root == null || !root.activeInHierarchy) return false;

        _categories.Clear();

        // 1. Daily Missions
        GameObject dailyRoot = GameObject.Find("DailyMissionsPanel");
        if (dailyRoot != null && dailyRoot.activeInHierarchy)
        {
            var cat = new MissionCategory { Name = Loc.Get("ms_cat_daily") };
            cat.Missions = ScanTiles(dailyRoot);
            if (cat.Missions.Count > 0)
            {
                cat.CompletedCount = CountCompleted(cat.Missions);
                _categories.Add(cat);
            }
        }

        // 2. Season Pass Missions
        GameObject seasonRoot = GameObject.Find("SeasonPassMissionPanel");
        if (seasonRoot != null && seasonRoot.activeInHierarchy)
        {
            var cat = new MissionCategory { Name = Loc.Get("ms_cat_season") };
            cat.Missions = ScanTiles(seasonRoot);
            if (cat.Missions.Count > 0)
            {
                cat.CompletedCount = CountCompleted(cat.Missions);
                _categories.Add(cat);
            }
        }

        // Fallback: scan all missions under root
        if (_categories.Count == 0)
        {
            var cat = new MissionCategory { Name = Loc.Get("ms_cat_all") };
            cat.Missions = ScanTiles(root);
            if (cat.Missions.Count > 0)
            {
                cat.CompletedCount = CountCompleted(cat.Missions);
                _categories.Add(cat);
            }
        }

        return _categories.Count > 0;
    }

    private List<MissionInfo> ScanTiles(GameObject root)
    {
        var list = new List<MissionInfo>();
        Il2CppArrayBase<Button> buttons = root.GetComponentsInChildren<Button>(true);
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
        Il2CppArrayBase<TMP_Text> texts = tile.GetComponentsInChildren<TMP_Text>(true);
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
        if (string.IsNullOrEmpty(info.Title)) return null;

        // Determine completion status
        try
        {
            if (!string.IsNullOrEmpty(info.Progress) && !string.IsNullOrEmpty(info.Goal))
                info.IsComplete = info.Progress == info.Goal;
        }
        catch { }

        return info;
    }

    private int CountCompleted(List<MissionInfo> missions)
    {
        int count = 0;
        foreach (var m in missions)
            if (m.IsComplete) count++;
        return count;
    }

    private void ProcessInput()
    {
        if (_holdRepeater.Check(SDLInput.Key.Up, MovePrev)) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadUp)) MovePrev();
        else if (_holdRepeater.Check(SDLInput.Key.Down, MoveNext)) { }
        else if (SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadDown)) MoveNext();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Right) || SDLInput.IsButtonDown(SDLInput.GamepadButton.DPadRight)) ReadDetails();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Home)) JumpToFirst();
        else if (SDLInput.IsKeyDown(SDLInput.Key.End)) JumpToLast();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Return) || SDLInput.IsButtonDown(SDLInput.GamepadButton.South)) Enter();
        else if (SDLInput.IsKeyDown(SDLInput.Key.Backspace) || SDLInput.IsKeyDown(SDLInput.Key.Escape) || SDLInput.IsButtonDown(SDLInput.GamepadButton.East)) Back();
        else if (SDLInput.IsKeyDown(SDLInput.Key.H)) AnnounceContext();
    }

    private void JumpToFirst()
    {
        if (_menuLevel == 0) { _catIndex = 0; AnnounceCat(); }
        else if (_categories[_catIndex].Missions.Count > 0) { _missionIndex = 0; AnnounceMission(); }
    }

    private void JumpToLast()
    {
        if (_menuLevel == 0 && _categories.Count > 0) { _catIndex = _categories.Count - 1; AnnounceCat(); }
        else if (_categories[_catIndex].Missions.Count > 0)
        {
            _missionIndex = _categories[_catIndex].Missions.Count - 1;
            AnnounceMission();
        }
    }

    private void MovePrev()
    {
        if (_menuLevel == 0) { _catIndex = (_catIndex - 1 + _categories.Count) % _categories.Count; AnnounceCat(); }
        else
        {
            _missionIndex = (_missionIndex - 1 + _categories[_catIndex].Missions.Count) % _categories[_catIndex].Missions.Count;
            AnnounceMission();
        }
    }

    private void MoveNext()
    {
        if (_menuLevel == 0) { _catIndex = (_catIndex + 1) % _categories.Count; AnnounceCat(); }
        else
        {
            _missionIndex = (_missionIndex + 1) % _categories[_catIndex].Missions.Count;
            AnnounceMission();
        }
    }

    private void ReadDetails()
    {
        if (_menuLevel == 0)
        {
            // Right on category: read summary with completion info
            if (_catIndex < 0 || _catIndex >= _categories.Count) return;
            var cat = _categories[_catIndex];
            string summary = cat.Name + ". " + cat.Missions.Count + " missions, " +
                cat.CompletedCount + " complete.";
            AnnouncementService.Instance.Announce(summary);
        }
        else
        {
            // Right on mission: read full details
            AnnounceMissionFull();
        }
    }

    private void Enter()
    {
        if (_menuLevel == 0)
        {
            _menuLevel = 1;
            _missionIndex = 0;
            var cat = _categories[_catIndex];
            AnnouncementService.Instance.Announce(
                cat.Name + ", " + cat.Missions.Count + " missions. " +
                Loc.Get("ms_nav_hint"), AnnouncementPriority.High);
            AnnounceMission();
        }
        else
        {
            var m = _categories[_catIndex].Missions[_missionIndex];
            if (m.IsComplete)
            {
                AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("ms_claiming", m.Title));
                UIHelper.ActivateButton(m.ClaimButton);
                // Force rescan after claiming
                _lastScanTime = 0f;
            }
            else
            {
                AnnouncementService.Instance.AnnounceInterrupt(Loc.Get("ms_opening", m.Title));
                UIHelper.ActivateButton(m.ClaimButton);
            }
            _isActive = false;
        }
    }

    private void Back()
    {
        if (_menuLevel == 1)
        {
            _menuLevel = 0;
            AnnouncementService.Instance.Announce(Loc.Get("ms_back_to_categories"));
            AnnounceCat();
        }
        else
        {
            _activated = false;
            _isActive = false;
        }
    }

    private void AnnounceCat()
    {
        if (_catIndex < 0 || _catIndex >= _categories.Count) return;
        var cat = _categories[_catIndex];
        string msg = cat.Name + ", " + cat.Missions.Count + " missions";
        if (cat.CompletedCount > 0)
            msg += ", " + cat.CompletedCount + " complete";
        msg += ". " + (_catIndex + 1) + " of " + _categories.Count;
        AnnouncementService.Instance.Announce(msg);
    }

    /// <summary>Browse announcement: title + progress status + position.</summary>
    private void AnnounceMission()
    {
        if (_catIndex < 0 || _catIndex >= _categories.Count) return;
        var missions = _categories[_catIndex].Missions;
        if (_missionIndex < 0 || _missionIndex >= missions.Count) return;

        var m = missions[_missionIndex];
        string status = GetMissionStatus(m);
        string msg = m.Title + ", " + status + ", " + (_missionIndex + 1) + " of " + missions.Count;
        AnnouncementService.Instance.Announce(msg);
    }

    /// <summary>Full details on Right key.</summary>
    private void AnnounceMissionFull()
    {
        if (_catIndex < 0 || _catIndex >= _categories.Count) return;
        var missions = _categories[_catIndex].Missions;
        if (_missionIndex < 0 || _missionIndex >= missions.Count) return;

        var m = missions[_missionIndex];
        var parts = new List<string>();
        parts.Add(m.Title);
        if (!string.IsNullOrEmpty(m.Desc))
            parts.Add(m.Desc);
        parts.Add(Loc.Get("ms_progress_detail", m.Progress ?? "0", m.Goal ?? "?"));
        if (m.IsComplete)
            parts.Add(Loc.Get("ms_status_complete"));
        if (!string.IsNullOrEmpty(m.Reward))
            parts.Add(Loc.Get("ms_reward", m.Reward.Trim()));
        if (m.IsComplete)
            parts.Add(Loc.Get("ms_claim_hint"));

        AnnouncementService.Instance.Announce(string.Join(". ", parts));
    }

    private string GetMissionStatus(MissionInfo m)
    {
        if (m.IsComplete) return Loc.Get("ms_complete");
        if (!string.IsNullOrEmpty(m.Progress) && !string.IsNullOrEmpty(m.Goal))
            return m.Progress + "/" + m.Goal;
        return Loc.Get("ms_incomplete");
    }

    public void AnnounceContext()
    {
        AnnouncementService.Instance.Announce(Loc.Get("ms_help"), AnnouncementPriority.High);
        if (_menuLevel == 0)
            AnnounceCat();
        else
            AnnounceMission();
    }

    public void Deactivate()
    {
        _isActive = false;
        _activated = false;
        _holdRepeater.Reset();
    }

    public void OnSceneChanged(string sceneName)
    {
        _isActive = false;
        _activated = false;
        _menuLevel = 0;
        _categories.Clear();
        _catIndex = 0;
        _missionIndex = 0;
        _lastScanTime = 0f;
    }
}
