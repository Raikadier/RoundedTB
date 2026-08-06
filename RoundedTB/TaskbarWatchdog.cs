using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RoundedTB
{
    /// <summary>
    /// Separate process that waits for the main RoundedTB PID to die, then clears SetWindowRgn
    /// on known taskbar HWNDs. Covers Task Manager TerminateProcess where OnClosing/OnExit never run.
    /// </summary>
    public static class TaskbarWatchdog
    {
        public const string Arg = "--watchdog";

        static readonly object Gate = new object();
        static bool started;

        public static string StatePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "rtb.watchdog.json");

        public class State
        {
            public int ParentPid { get; set; }
            public List<long> Hwnds { get; set; } = new List<long>();
            /// <summary>True after a graceful RestoreAllTaskbars — watchdog should not touch Explorer.</summary>
            public bool SkipRestore { get; set; }
        }

        public static bool TryHandleArgs(string[] args)
        {
            if (args == null || args.Length < 2)
            {
                return false;
            }
            if (!string.Equals(args[0], Arg, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (!int.TryParse(args[1], out int parentPid))
            {
                return false;
            }

            Run(parentPid);
            return true;
        }

        public static void Run(int parentPid)
        {
            try
            {
                using Process parent = Process.GetProcessById(parentPid);
                parent.WaitForExit();
            }
            catch (ArgumentException)
            {
                // Parent already gone — still try restore from last state.
            }
            catch (Exception)
            {
                return;
            }

            State state = ReadState();
            if (state == null || state.SkipRestore || state.Hwnds == null || state.Hwnds.Count == 0)
            {
                return;
            }

            foreach (long raw in state.Hwnds.Distinct())
            {
                IntPtr hwnd = new IntPtr(raw);
                try
                {
                    if (!LocalPInvoke.IsWindow(hwnd))
                    {
                        continue;
                    }
                    LocalPInvoke.SetWindowRgn(hwnd, IntPtr.Zero, true);
                    LocalPInvoke.SetLayeredWindowAttributes(hwnd, 0, 255, LocalPInvoke.LWA_ALPHA);
                    int style = LocalPInvoke.GetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
                    if ((style & LocalPInvoke.WS_EX_LAYERED) == LocalPInvoke.WS_EX_LAYERED)
                    {
                        LocalPInvoke.SetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE, style ^ LocalPInvoke.WS_EX_LAYERED);
                    }
                    style = LocalPInvoke.GetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
                    if ((style & LocalPInvoke.WS_EX_TRANSPARENT) == LocalPInvoke.WS_EX_TRANSPARENT)
                    {
                        LocalPInvoke.SetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE, style ^ LocalPInvoke.WS_EX_TRANSPARENT);
                    }
                }
                catch
                {
                    // Best-effort restore after hard kill.
                }
            }

            try
            {
                File.Delete(StatePath);
            }
            catch
            {
                // ignore
            }
        }

        public static void EnsureStarted(int parentPid)
        {
            lock (Gate)
            {
                if (started)
                {
                    return;
                }
                started = true;
            }

            try
            {
                string exe = Environment.ProcessPath
                    ?? Process.GetCurrentProcess().MainModule?.FileName
                    ?? Path.Combine(AppContext.BaseDirectory, "RoundedTB.exe");

                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = $"{Arg} {parentPid}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                };
                Process.Start(psi)?.Dispose();
            }
            catch (Exception ex)
            {
                lock (Gate) { started = false; }
                Interaction.WriteCrashLog($"TaskbarWatchdog.EnsureStarted failed: {ex.Message}");
            }
        }

        public static void PublishHwnds(IEnumerable<IntPtr> hwnds, int parentPid, bool skipRestore = false)
        {
            try
            {
                State state = new State
                {
                    ParentPid = parentPid,
                    SkipRestore = skipRestore,
                    Hwnds = hwnds
                        .Where(h => h != IntPtr.Zero)
                        .Select(h => h.ToInt64())
                        .Distinct()
                        .ToList(),
                };
                File.WriteAllText(StatePath, JsonConvert.SerializeObject(state));
            }
            catch (Exception ex)
            {
                Interaction.WriteCrashLog($"TaskbarWatchdog.PublishHwnds failed: {ex.Message}");
            }
        }

        public static void MarkGracefulExit(int parentPid)
        {
            State state = ReadState() ?? new State { ParentPid = parentPid };
            state.SkipRestore = true;
            state.ParentPid = parentPid;
            try
            {
                File.WriteAllText(StatePath, JsonConvert.SerializeObject(state));
            }
            catch
            {
                // ignore
            }
        }

        static State ReadState()
        {
            try
            {
                if (!File.Exists(StatePath))
                {
                    return null;
                }
                return JsonConvert.DeserializeObject<State>(File.ReadAllText(StatePath));
            }
            catch
            {
                return null;
            }
        }
    }
}
