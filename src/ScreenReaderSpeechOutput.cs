namespace SnapAccess;

/// <summary>
/// Production <see cref="ISpeechOutput"/> that forwards to the static
/// <see cref="ScreenReader"/> Tolk bridge. This is the Shell adapter; it is not
/// exercised in tests (which use a fake speech output instead).
/// </summary>
public sealed class ScreenReaderSpeechOutput : ISpeechOutput
{
    public void Say(string text, bool interrupt) => ScreenReader.Say(text, interrupt);

    public void Silence() => ScreenReader.Silence();
}
