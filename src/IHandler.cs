namespace SnapAccess;

/// <summary>
/// Common interface for all screen/feature handlers.
/// </summary>
public interface IHandler
{
    /// <summary>Called every frame when the handler's state is active.</summary>
    bool Update();

    /// <summary>Called when the screen reader should describe the current context.</summary>
    void AnnounceContext();

    /// <summary>Called when the handler's state is entered or the scene changes.</summary>
    void Reset();
}
