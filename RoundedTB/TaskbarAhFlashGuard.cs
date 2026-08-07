using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RoundedTB
{
    /// <summary>
    /// UI-thread WinEvent hook: the moment Explorer moves a watched taskbar HWND, force alpha=1
    /// so stock frames during native autohide show are invisible before the worker poll catches up.
    /// </summary>
    public static class TaskbarAhFlashGuard
    {
        const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
        const int OBJID_WINDOW = 0;
        const uint WINEVENT_OUTOFCONTEXT = 0;
        const uint WINEVENT_SKIPOWNPROCESS = 2;

        static readonly object Gate = new object();
        static IntPtr hook;
        static LocalPInvoke.WinEventDelegate proc; // prevent GC
        static readonly HashSet<long> Watch = new HashSet<long>();

        public static void Start()
        {
            lock (Gate)
            {
                if (hook != IntPtr.Zero)
                {
                    return;
                }

                proc = OnWinEvent;
                hook = LocalPInvoke.SetWinEventHook(
                    EVENT_OBJECT_LOCATIONCHANGE,
                    EVENT_OBJECT_LOCATIONCHANGE,
                    IntPtr.Zero,
                    proc,
                    0,
                    0,
                    WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
            }
        }

        public static void Stop()
        {
            lock (Gate)
            {
                if (hook != IntPtr.Zero)
                {
                    LocalPInvoke.UnhookWinEvent(hook);
                    hook = IntPtr.Zero;
                }
                Watch.Clear();
            }
        }

        public static void UpdateHwnds(IEnumerable<IntPtr> hwnds)
        {
            lock (Gate)
            {
                Watch.Clear();
                if (hwnds == null)
                {
                    return;
                }
                foreach (IntPtr h in hwnds)
                {
                    if (h != IntPtr.Zero)
                    {
                        Watch.Add(h.ToInt64());
                    }
                }
            }
        }

        static void OnWinEvent(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (hwnd == IntPtr.Zero || idObject != OBJID_WINDOW)
            {
                return;
            }

            bool watch;
            lock (Gate)
            {
                watch = Watch.Contains(hwnd.ToInt64());
            }
            if (!watch)
            {
                return;
            }

            // Never touch alpha while edge-peeked — low alpha breaks Windows AH hover.
            if (Taskbar.IsTaskbarEdgeRevealOnly(hwnd))
            {
                return;
            }

            // Instant gate — worker will ApplyRounding / raise alpha when stable.
            Taskbar.SetTaskbarAlpha(hwnd, 1);
        }
    }
}
