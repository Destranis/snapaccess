using System;

namespace SnapAccess;

public static class AccessStateManager
{
    public enum State
    {
        None,
        MainMenu,
        Collection,
        DeckBuilder,
        Shop,
        BattlePass,
        Gameplay,
        Dialog
    }

    public static State Current { get; private set; }

    public static event Action<State, State> OnStateChanged;

    public static bool TryEnter(State state)
    {
        if (state == State.None)
        {
            DebugLogger.Log(LogCategory.State, "AccessState", "Warning: Use Exit() instead of TryEnter(None)");
            return false;
        }
        if (Current == state)
        {
            return true;
        }
        if (Current != State.None)
        {
            DebugLogger.Log(LogCategory.State, "AccessState", $"Auto-exiting {Current} for {state}");
            State previous = Current;
            Current = State.None;
            OnStateChanged?.Invoke(previous, State.None);
        }
        State old = Current;
        Current = state;
        DebugLogger.Log(LogCategory.State, "AccessState", $"Entered {state}");
        OnStateChanged?.Invoke(old, state);
        return true;
    }

    public static void Exit(State state)
    {
        if (Current == state)
        {
            State old = Current;
            Current = State.None;
            DebugLogger.Log(LogCategory.State, "AccessState", $"Exited {state}");
            OnStateChanged?.Invoke(old, State.None);
        }
    }

    public static void ForceReset()
    {
        if (Current != State.None)
        {
            State old = Current;
            Current = State.None;
            DebugLogger.Log(LogCategory.State, "AccessState", $"Force reset from {old}");
            OnStateChanged?.Invoke(old, State.None);
        }
    }

    public static bool IsIn(State state)
    {
        return Current == state;
    }
}
