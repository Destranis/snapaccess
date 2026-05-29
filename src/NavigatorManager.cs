using System.Collections.Generic;
using System.Linq;

namespace SnapAccess;

/// <summary>
/// Manages screen navigators with priority-based activation and preemption.
/// Only one navigator is active at a time. Higher-priority navigators
/// can preempt lower-priority ones when their screen becomes available.
/// </summary>
public class NavigatorManager
{
    private readonly List<IScreenNavigator> _navigators = new List<IScreenNavigator>();
    private IScreenNavigator _activeNavigator;
    private string _previousActiveId;
    private string _currentScene;

    public static NavigatorManager Instance { get; private set; }

    /// <summary>The currently active navigator, or null if none.</summary>
    public IScreenNavigator ActiveNavigator => _activeNavigator;

    public string CurrentScene => _currentScene;

    public bool HasActiveNavigator => _activeNavigator != null;

    public NavigatorManager()
    {
        Instance = this;
    }

    /// <summary>
    /// Register a navigator. Navigators are kept sorted by priority (descending).
    /// </summary>
    public void Register(IScreenNavigator navigator)
    {
        _navigators.Add(navigator);
        _navigators.Sort((a, b) => b.Priority.CompareTo(a.Priority));
        DebugLogger.Log(LogCategory.State, "NavigatorManager",
            $"Registered: {navigator.NavigatorId} (priority {navigator.Priority})");
    }

    /// <summary>
    /// Core update loop. Checks for preemption, updates active navigator,
    /// or tries to activate a new one if none is active.
    /// </summary>
    public void Update()
    {
        if (_activeNavigator != null)
        {
            // Check if any higher-priority navigator wants to activate
            foreach (var nav in _navigators)
            {
                if (nav.Priority <= _activeNavigator.Priority)
                    break; // Sorted descending, no more higher-priority navigators

                if (nav == _activeNavigator)
                    continue;

                nav.Update();
                if (nav.IsActive)
                {
                    DebugLogger.Log(LogCategory.State, "NavigatorManager",
                        $"{nav.NavigatorId} preempting {_activeNavigator.NavigatorId}");
                    _activeNavigator.Deactivate();
                    _activeNavigator = nav;
                    AnnounceTransition(nav);
                    return;
                }
            }

            // Update the active navigator
            _activeNavigator.Update();

            // Check if it deactivated itself
            if (_activeNavigator == null || !_activeNavigator.IsActive)
            {
                if (_activeNavigator != null)
                {
                    DebugLogger.Log(LogCategory.State, "NavigatorManager",
                        $"{_activeNavigator.NavigatorId} deactivated");
                    _activeNavigator = null;
                }
            }
            return;
        }

        // No active navigator — try to find one
        foreach (var nav in _navigators)
        {
            nav.Update();
            if (nav.IsActive)
            {
                _activeNavigator = nav;
                DebugLogger.Log(LogCategory.State, "NavigatorManager",
                    $"{nav.NavigatorId} activated");
                AnnounceTransition(nav);
                break;
            }
        }
    }

    /// <summary>Notify all navigators of a scene change.</summary>
    public void OnSceneChanged(string sceneName)
    {
        _currentScene = sceneName;
        foreach (var nav in _navigators)
        {
            nav.OnSceneChanged(sceneName);
        }
        if (_activeNavigator != null && !_activeNavigator.IsActive)
        {
            _activeNavigator = null;
        }
    }

    /// <summary>Force-deactivate the current navigator.</summary>
    public void DeactivateCurrent()
    {
        _activeNavigator?.Deactivate();
        _activeNavigator = null;
    }

    /// <summary>Announces a navigator transition to orient the user.</summary>
    private void AnnounceTransition(IScreenNavigator nav)
    {
        if (nav == null) return;
        string id = nav.NavigatorId;
        if (id == _previousActiveId) return; // Same navigator, no announcement
        _previousActiveId = id;
        if (!ModSettings.Instance.TransitionAnnouncements) return;
        string locKey = "nav_" + id.ToLower().Replace(" ", "_");
        string name = Loc.Get(locKey);
        // If Loc returns the key itself (no translation), use the NavigatorId directly
        if (name == locKey) name = id;
        AnnouncementService.Instance.Announce(name, AnnouncementPriority.High);
    }

    /// <summary>Find a navigator by its ID.</summary>
    public IScreenNavigator GetNavigator(string navigatorId)
    {
        return _navigators.FirstOrDefault(n => n.NavigatorId == navigatorId);
    }

    /// <summary>Find a navigator by type.</summary>
    public T GetNavigator<T>() where T : class, IScreenNavigator
    {
        return _navigators.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Request activation of a specific navigator by ID.
    /// Deactivates the current navigator and tries to activate the requested one.
    /// </summary>
    public bool RequestActivation(string navigatorId)
    {
        var nav = GetNavigator(navigatorId);
        if (nav == null)
        {
            DebugLogger.Log(LogCategory.State, "NavigatorManager",
                $"RequestActivation: navigator '{navigatorId}' not found");
            return false;
        }

        var previous = _activeNavigator;
        if (_activeNavigator != null && _activeNavigator != nav)
        {
            _activeNavigator.Deactivate();
            _activeNavigator = null;
        }

        nav.Update();
        if (nav.IsActive)
        {
            _activeNavigator = nav;
            DebugLogger.Log(LogCategory.State, "NavigatorManager",
                $"RequestActivation: {navigatorId} activated");
            return true;
        }

        // Restore previous if activation failed
        if (previous != null && previous != nav)
        {
            previous.Update();
            if (previous.IsActive)
                _activeNavigator = previous;
        }
        return false;
    }
}
