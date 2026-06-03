// Test-only stub of MelonLoader.MelonLogger.
//
// The real MelonLoader.dll ships with the game's MelonLoader install and is
// not available in CI. The Core sources under test only ever call the three
// static logging methods below, so a no-op stub lets them compile and run in
// isolation without pulling in the game runtime.
namespace MelonLoader
{
    internal static class MelonLogger
    {
        public static void Msg(string message) { }
        public static void Warning(string message) { }
        public static void Error(string message) { }
    }
}
