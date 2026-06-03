namespace SnapAccess;

/// <summary>
/// Seam over the raw screen-reader output. Lets <see cref="AnnouncementService"/>
/// be tested without the native Tolk screen-reader bridge.
/// </summary>
public interface ISpeechOutput
{
    /// <summary>Speak <paramref name="text"/>, interrupting current speech when <paramref name="interrupt"/> is true.</summary>
    void Say(string text, bool interrupt);

    /// <summary>Stop all current speech.</summary>
    void Silence();
}
