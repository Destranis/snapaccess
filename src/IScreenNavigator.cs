namespace SnapAccess;

/// <summary>
/// Interface for screen/feature navigators managed by NavigatorManager.
/// Replaces IHandler with priority-based activation and preemption support.
/// </summary>
public interface IScreenNavigator
{
    /// <summary>Unique identifier for this navigator (e.g., "Battlefield", "MainMenu").</summary>
    string NavigatorId { get; }

    /// <summary>
    /// Priority for activation ordering. Higher values take precedence.
    /// When multiple navigators can activate, the highest priority wins.
    /// A higher-priority navigator can preempt a lower-priority active one.
    /// </summary>
    int Priority { get; }

    /// <summary>Whether this navigator is currently active and handling input.</summary>
    bool IsActive { get; }

    /// <summary>
    /// Called every frame by NavigatorManager.
    /// Should detect whether this navigator's screen/state is present,
    /// set IsActive accordingly, and process input if active.
    /// </summary>
    void Update();

    /// <summary>
    /// Called when another navigator preempts this one, or when this navigator
    /// should release control. Should pause state but may keep cached data.
    /// </summary>
    void Deactivate();

    /// <summary>
    /// Called on Unity scene transitions. Should perform a full state wipe
    /// (clear caches, reset indices, etc.) since the entire UI has changed.
    /// </summary>
    void OnSceneChanged(string sceneName);

    /// <summary>
    /// Called when the user requests context help (F1).
    /// Should announce the current state and available controls.
    /// </summary>
    void AnnounceContext();
}
