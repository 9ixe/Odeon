using System.Diagnostics;
namespace Odeon.Core.Playback {
    public static class DebugLogger {
        public static void Log(string msg) {
            Debug.WriteLine(msg);
            try { System.IO.File.AppendAllText(@""P:\Odeon\debug.log"", msg + ""\n""); } catch { }
        }
    }
}
