using System;
using System.Runtime.InteropServices;
using MelonLoader;

namespace SnapAccess;

public static class ScreenReader
{
	private static bool _available;

	private static bool _initialized;

	public static bool IsAvailable => _available;

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
	private static extern void Tolk_Load();

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
	private static extern void Tolk_Unload();

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
	private static extern bool Tolk_IsLoaded();

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
	private static extern bool Tolk_HasSpeech();

	[DllImport("Tolk.dll", CharSet = CharSet.Unicode, CallingConvention = CallingConvention.StdCall)]
	private static extern bool Tolk_Output([MarshalAs(UnmanagedType.LPWStr)] string str, bool interrupt);

	public static void Initialize()
	{
		if (_initialized)
		{
			return;
		}
		try
		{
			Tolk_Load();
			_initialized = true;
			_available = Tolk_IsLoaded() && Tolk_HasSpeech();
			if (_available)
			{
				MelonLogger.Msg("Screen reader support initialized (Tolk)");
			}
			else
			{
				MelonLogger.Warning("Tolk loaded but no active screen reader found");
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("Failed to initialize screen reader: " + ex.Message);
		}
	}

	public static void Say(string text, bool interrupt = true)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
        
        string cleanText = UIHelper.StripTags(text);
		DebugLogger.LogScreenReader(cleanText);
		if (_available)
		{
			try
			{
				Tolk_Output(cleanText, interrupt);
			}
			catch
			{
			}
		}
	}

    public static void SayQueued(string text)
    {
        Say(text, false);
    }

	public static void Shutdown()
	{
		if (_initialized)
		{
			try
			{
				Tolk_Unload();
			}
			catch
			{
			}
			_initialized = false;
			_available = false;
		}
	}
}
