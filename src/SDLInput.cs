using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using MelonLoader;

namespace SnapAccess;

public static class SDLInput
{
	public enum Key
	{
		A = 65,
		B = 66,
		C = 67,
		D = 68,
		E = 69,
		F = 70,
		G = 71,
		H = 72,
		I = 73,
		J = 74,
		K = 75,
		L = 76,
		M = 77,
		N = 78,
		O = 79,
		P = 80,
		Q = 81,
		R = 82,
		S = 83,
		T = 84,
		U = 85,
		V = 86,
		W = 87,
		X = 88,
		Y = 89,
		Z = 90,
		Num0 = 48,
		Num1 = 49,
		Num2 = 50,
		Num3 = 51,
		Num4 = 52,
		Num5 = 53,
		Num6 = 54,
		Num7 = 55,
		Num8 = 56,
		Num9 = 57,
		F1 = 112,
		F2 = 113,
		F3 = 114,
		F4 = 115,
		F5 = 116,
		F6 = 117,
		F7 = 118,
		F8 = 119,
		F9 = 120,
		F10 = 121,
		F11 = 122,
		F12 = 123,
		Left = 37,
		Up = 38,
		Right = 39,
		Down = 40,
		Insert = 45,
		Delete = 46,
		Home = 36,
		End = 35,
		PageUp = 33,
		PageDown = 34,
		Return = 13,
		Enter = 13,
		Escape = 27,
		Space = 32,
		Tab = 9,
		Backspace = 8,
		LShift = 160,
		RShift = 161,
		LCtrl = 162,
		RCtrl = 163,
		LAlt = 164,
		RAlt = 165,
		Numpad0 = 96,
		Numpad1 = 97,
		Numpad2 = 98,
		Numpad3 = 99,
		Numpad4 = 100,
		Numpad5 = 101,
		Numpad6 = 102,
		Numpad7 = 103,
		Numpad8 = 104,
		Numpad9 = 105,
		NumpadMultiply = 106,
		NumpadAdd = 107,
		NumpadSubtract = 109,
		NumpadDecimal = 110,
		NumpadDivide = 111,
		Minus = 189,
		Equals = 187,
		LeftBracket = 219,
		RightBracket = 221,
		Semicolon = 186,
		Apostrophe = 222,
		Comma = 188,
		Period = 190,
		Slash = 191,
		Backslash = 220,
		Grave = 192
	}

	public enum GamepadButton
	{
		Invalid = -1,
		South = 0,
		East = 1,
		West = 2,
		North = 3,
		Back = 4,
		Guide = 5,
		Start = 6,
		LeftStick = 7,
		RightStick = 8,
		LeftShoulder = 9,
		RightShoulder = 10,
		DPadUp = 11,
		DPadDown = 12,
		DPadLeft = 13,
		DPadRight = 14,
		Touchpad = 20,
		A = 0,
		B = 1,
		X = 2,
		Y = 3,
		Cross = 0,
		Circle = 1,
		Square = 2,
		Triangle = 3,
		L1 = 9,
		R1 = 10,
		L3 = 7,
		R3 = 8,
		Select = 4,
		Options = 6
	}

	public enum GamepadAxis
	{
		Invalid = -1,
		LeftX,
		LeftY,
		RightX,
		RightY,
		LeftTrigger,
		RightTrigger
	}

	private static readonly Dictionary<Key, bool> _keyStates = new Dictionary<Key, bool>();

	private static readonly Dictionary<Key, bool> _lastKeyStates = new Dictionary<Key, bool>();

	private static bool _keyboardInitialized = false;

	private const string SDL3_DLL = "SDL3.dll";

	private const uint SDL_INIT_GAMEPAD = 8192u;

	private const uint SDL_INIT_JOYSTICK = 512u;

	private static bool _initialized = false;

	private static bool _sdlAvailable = false;

	private static bool _windowFocused = false;

	private static uint _processId = 0u;

	private static IntPtr _gamepad = IntPtr.Zero;

	private static string _controllerName = "";

	private static Dictionary<GamepadButton, bool> _buttonStates = new Dictionary<GamepadButton, bool>();

	private static Dictionary<GamepadButton, bool> _lastButtonStates = new Dictionary<GamepadButton, bool>();

	private static Dictionary<GamepadAxis, short> _axisStates = new Dictionary<GamepadAxis, short>();

	private static Dictionary<GamepadAxis, short> _lastAxisStates = new Dictionary<GamepadAxis, short>();

	private const short TriggerThreshold = 8000;

	private const short StickThreshold = 16000;

	public static bool IsWindowFocused => _windowFocused;

	public static bool IsShiftHeld => IsKeyHeld(Key.LShift) || IsKeyHeld(Key.RShift);

	public static bool IsCtrlHeld => IsKeyHeld(Key.LCtrl) || IsKeyHeld(Key.RCtrl);

	public static bool IsAltHeld => IsKeyHeld(Key.LAlt) || IsKeyHeld(Key.RAlt);

	public static bool HasGamepad => _gamepad != IntPtr.Zero;

	public static string GamepadName => _controllerName;

	public static bool IsLeftTriggerHeld
	{
		get
		{
			short value;
			return _axisStates.TryGetValue(GamepadAxis.LeftTrigger, out value) && value > 8000;
		}
	}

	public static bool IsRightTriggerHeld
	{
		get
		{
			short value;
			return _axisStates.TryGetValue(GamepadAxis.RightTrigger, out value) && value > 8000;
		}
	}

	public static short LeftStickX
	{
		get
		{
			short value;
			return (short)(_axisStates.TryGetValue(GamepadAxis.LeftX, out value) ? value : 0);
		}
	}

	public static short LeftStickY
	{
		get
		{
			short value;
			return (short)(_axisStates.TryGetValue(GamepadAxis.LeftY, out value) ? value : 0);
		}
	}

	public static short RightStickX
	{
		get
		{
			short value;
			return (short)(_axisStates.TryGetValue(GamepadAxis.RightX, out value) ? value : 0);
		}
	}

	public static short RightStickY
	{
		get
		{
			short value;
			return (short)(_axisStates.TryGetValue(GamepadAxis.RightY, out value) ? value : 0);
		}
	}

	public static bool IsAvailable => _keyboardInitialized;

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int vKey);

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("kernel32.dll")]
	private static extern uint GetCurrentProcessId();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool SDL_Init(uint flags);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern void SDL_Quit();

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern void SDL_PumpEvents();

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr SDL_GetGamepads(out int count);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr SDL_OpenGamepad(uint instance_id);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern void SDL_CloseGamepad(IntPtr gamepad);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool SDL_GamepadConnected(IntPtr gamepad);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool SDL_GetGamepadButton(IntPtr gamepad, GamepadButton button);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern short SDL_GetGamepadAxis(IntPtr gamepad, GamepadAxis axis);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr SDL_GetGamepadName(IntPtr gamepad);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern IntPtr SDL_GetError();

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern bool SDL_SetHint([MarshalAs(UnmanagedType.LPUTF8Str)] string name, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

	[DllImport("SDL3.dll", CallingConvention = CallingConvention.Cdecl)]
	private static extern void SDL_free(IntPtr mem);

	public static bool Initialize()
	{
		if (_initialized)
		{
			return true;
		}
		_initialized = true;
		_processId = GetCurrentProcessId();
		InitializeKeyboard();
		InitializeSDL();
		return true;
	}

	private static void InitializeKeyboard()
	{
		foreach (Key value in Enum.GetValues(typeof(Key)))
		{
			_keyStates[value] = false;
			_lastKeyStates[value] = false;
		}
		_keyboardInitialized = true;
		MelonLogger.Msg("[SDLInput] Keyboard ready (Windows API)");
	}

	private static void InitializeSDL()
	{
		try
		{
			SDL_SetHint("SDL_WINDOWS_DISABLE_THREAD_NAMING", "1");
			if (!SDL_Init(8704u))
			{
				string sDLError = GetSDLError();
				MelonLogger.Warning("[SDLInput] SDL_Init failed: " + sDLError);
				return;
			}
			_sdlAvailable = true;
			MelonLogger.Msg("[SDLInput] SDL3 controller support ready");
			foreach (GamepadButton value in Enum.GetValues(typeof(GamepadButton)))
			{
				int num = (int)value;
				if (num >= 0 && num <= 20)
				{
					_buttonStates[value] = false;
					_lastButtonStates[value] = false;
				}
			}
			foreach (GamepadAxis value2 in Enum.GetValues(typeof(GamepadAxis)))
			{
				if (value2 != GamepadAxis.Invalid)
				{
					_axisStates[value2] = 0;
					_lastAxisStates[value2] = 0;
				}
			}
			OpenFirstGamepad();
		}
		catch (DllNotFoundException)
		{
			MelonLogger.Msg("[SDLInput] SDL3.dll not found — controller support disabled. Keyboard still works.");
		}
		catch (Exception ex2)
		{
			MelonLogger.Warning("[SDLInput] SDL3 init error: " + ex2.Message + ". Controller support disabled.");
		}
	}

	public static void Update()
	{
		UpdateWindowFocus();
		UpdateKeyboard();
		if (_sdlAvailable)
		{
			try
			{
				SDL_PumpEvents();
				UpdateGamepad();
			}
			catch (Exception ex)
			{
				MelonLogger.Warning("[SDLInput] Gamepad update error: " + ex.Message);
			}
		}
	}

	private static void UpdateWindowFocus()
	{
		try
		{
			IntPtr foregroundWindow = GetForegroundWindow();
			if (foregroundWindow == IntPtr.Zero)
			{
				_windowFocused = false;
				return;
			}
			GetWindowThreadProcessId(foregroundWindow, out var processId);
			_windowFocused = processId == _processId;
		}
		catch
		{
			_windowFocused = true;
		}
	}

	private static readonly HashSet<int> _uniqueKeyCodes = new HashSet<int>();

	private static void UpdateKeyboard()
	{
		if (!_keyboardInitialized)
		{
			return;
		}
		_uniqueKeyCodes.Clear();
		foreach (Key value in Enum.GetValues(typeof(Key)))
		{
			int vk = (int)value;
			if (!_uniqueKeyCodes.Add(vk))
			{
				continue; // Skip duplicate enum values (e.g. Return=13 and Enter=13)
			}
			_lastKeyStates[value] = _keyStates[value];
			if (_windowFocused)
			{
				_keyStates[value] = (GetAsyncKeyState(vk) & 0x8000) != 0;
			}
			else
			{
				_keyStates[value] = false;
			}
		}
	}

	private static void UpdateGamepad()
	{
		if (_gamepad != IntPtr.Zero && !SDL_GamepadConnected(_gamepad))
		{
			MelonLogger.Msg("[SDLInput] Controller disconnected: " + _controllerName);
			ScreenReader.Say("Controller disconnected: " + _controllerName);
			SDL_CloseGamepad(_gamepad);
			_gamepad = IntPtr.Zero;
			_controllerName = "";
		}
		if (_gamepad == IntPtr.Zero)
		{
			OpenFirstGamepad();
		}
		if (_gamepad == IntPtr.Zero)
		{
			return;
		}
		foreach (KeyValuePair<GamepadButton, bool> buttonState in _buttonStates)
		{
			_lastButtonStates[buttonState.Key] = buttonState.Value;
		}
		foreach (KeyValuePair<GamepadAxis, short> axisState in _axisStates)
		{
			_lastAxisStates[axisState.Key] = axisState.Value;
		}
		foreach (GamepadButton item in new List<GamepadButton>(_buttonStates.Keys))
		{
			_buttonStates[item] = SDL_GetGamepadButton(_gamepad, item);
		}
		foreach (GamepadAxis item2 in new List<GamepadAxis>(_axisStates.Keys))
		{
			_axisStates[item2] = SDL_GetGamepadAxis(_gamepad, item2);
		}
	}

	public static bool IsKeyDown(Key key)
	{
		bool value;
		bool value2;
		return _keyStates.TryGetValue(key, out value) && value && _lastKeyStates.TryGetValue(key, out value2) && !value2;
	}

	public static bool IsKeyHeld(Key key)
	{
		bool value;
		return _keyStates.TryGetValue(key, out value) && value;
	}

	public static bool IsKeyUp(Key key)
	{
		bool value;
		bool value2;
		return _lastKeyStates.TryGetValue(key, out value) && value && _keyStates.TryGetValue(key, out value2) && !value2;
	}

	public static bool IsButtonDown(GamepadButton button)
	{
		if (_gamepad == IntPtr.Zero)
		{
			return false;
		}
		bool value;
		bool flag = _buttonStates.TryGetValue(button, out value) && value;
		bool value2;
		bool flag2 = _lastButtonStates.TryGetValue(button, out value2) && value2;
		return flag && !flag2;
	}

	public static bool IsButtonHeld(GamepadButton button)
	{
		if (_gamepad == IntPtr.Zero)
		{
			return false;
		}
		bool value;
		return _buttonStates.TryGetValue(button, out value) && value;
	}

	public static bool IsLeftTriggerDown()
	{
		if (_gamepad == IntPtr.Zero)
		{
			return false;
		}
		short value;
		short num = (short)(_axisStates.TryGetValue(GamepadAxis.LeftTrigger, out value) ? value : 0);
		short value2;
		short num2 = (short)(_lastAxisStates.TryGetValue(GamepadAxis.LeftTrigger, out value2) ? value2 : 0);
		return num > 8000 && num2 <= 8000;
	}

	public static bool IsRightTriggerDown()
	{
		if (_gamepad == IntPtr.Zero)
		{
			return false;
		}
		short value;
		short num = (short)(_axisStates.TryGetValue(GamepadAxis.RightTrigger, out value) ? value : 0);
		short value2;
		short num2 = (short)(_lastAxisStates.TryGetValue(GamepadAxis.RightTrigger, out value2) ? value2 : 0);
		return num > 8000 && num2 <= 8000;
	}

	public static void Shutdown()
	{
		if (!_sdlAvailable)
		{
			return;
		}
		try
		{
			if (_gamepad != IntPtr.Zero)
			{
				SDL_CloseGamepad(_gamepad);
				_gamepad = IntPtr.Zero;
			}
			SDL_Quit();
			_sdlAvailable = false;
			MelonLogger.Msg("[SDLInput] SDL3 shutdown complete");
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[SDLInput] Shutdown error: " + ex.Message);
		}
	}

	private static void OpenFirstGamepad()
	{
		try
		{
			int count;
			IntPtr intPtr = SDL_GetGamepads(out count);
			if (!(intPtr == IntPtr.Zero) && count != 0)
			{
				uint instance_id = (uint)Marshal.ReadInt32(intPtr);
				SDL_free(intPtr);
				_gamepad = SDL_OpenGamepad(instance_id);
				if (_gamepad != IntPtr.Zero)
				{
					IntPtr intPtr2 = SDL_GetGamepadName(_gamepad);
					_controllerName = ((intPtr2 != IntPtr.Zero) ? Marshal.PtrToStringUTF8(intPtr2) : "Unknown Controller");
					MelonLogger.Msg("[SDLInput] Controller connected: " + _controllerName);
					ScreenReader.Say("Controller connected: " + _controllerName);
				}
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[SDLInput] Error opening gamepad: " + ex.Message);
		}
	}

	private static string GetSDLError()
	{
		try
		{
			IntPtr intPtr = SDL_GetError();
			return (intPtr != IntPtr.Zero) ? Marshal.PtrToStringUTF8(intPtr) : "Unknown error";
		}
		catch
		{
			return "Could not get error message";
		}
	}
}
