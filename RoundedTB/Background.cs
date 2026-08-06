using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;

namespace RoundedTB
{
    /// <summary>
    /// Opacity steps for non-blocking taskbar fade (one step per worker tick).
    /// Keep short — with ~16ms fast-poll during fade this is ~30–50ms total.
    /// </summary>
    internal static class FadeSteps
    {
        internal static readonly byte[] FadeIn = { 140, 255 };
        internal static readonly byte[] FadeOut = { 100, 1 };
    }

    public class Background
    {
        private struct ReloadChecker
        {
            public bool IsReload { get; set; }
        }

        public MainWindow mw;
        bool redrawOverride = false;
        int infrequentCount = 0;
        int heartbeatCount = 0;
        bool loggedNativeAutoHideCompat = false;
        int fastPollRemaining = 0;

        public Background()
        {
            mw = (MainWindow)Application.Current.MainWindow;
        }

        private static void ClearTransparent(IntPtr hwnd)
        {
            int style = LocalPInvoke.GetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if ((style & LocalPInvoke.WS_EX_TRANSPARENT) == LocalPInvoke.WS_EX_TRANSPARENT)
            {
                LocalPInvoke.SetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE, style ^ LocalPInvoke.WS_EX_TRANSPARENT);
            }
        }

        private static void SetTransparent(IntPtr hwnd)
        {
            int style = LocalPInvoke.GetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if ((style & LocalPInvoke.WS_EX_TRANSPARENT) != LocalPInvoke.WS_EX_TRANSPARENT)
            {
                LocalPInvoke.SetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE, style ^ LocalPInvoke.WS_EX_TRANSPARENT);
            }
        }

        /// <summary>
        /// Advances one fade step per call (no Thread.Sleep) so the worker keeps polling.
        /// </summary>
        private static void TickFadeAnimation(Types.Taskbar tb)
        {
            if (tb.FadeAnimDir == 1)
            {
                if (tb.FadeAnimStep == 0)
                {
                    ClearTransparent(tb.TaskbarHwnd);
                }
                if (tb.FadeAnimStep >= 0 && tb.FadeAnimStep < FadeSteps.FadeIn.Length)
                {
                    LocalPInvoke.SetLayeredWindowAttributes(tb.TaskbarHwnd, 0, FadeSteps.FadeIn[tb.FadeAnimStep], LocalPInvoke.LWA_ALPHA);
                    tb.FadeAnimStep++;
                }
                if (tb.FadeAnimStep >= FadeSteps.FadeIn.Length)
                {
                    tb.FadeAnimDir = 0;
                    tb.FadeAnimStep = 0;
                    tb.TaskbarHidden = false;
                    tb.Ignored = true;
                    Debug.WriteLine("MouseOver TB");
                }
            }
            else if (tb.FadeAnimDir == -1)
            {
                if (tb.FadeAnimStep >= 0 && tb.FadeAnimStep < FadeSteps.FadeOut.Length)
                {
                    LocalPInvoke.SetLayeredWindowAttributes(tb.TaskbarHwnd, 0, FadeSteps.FadeOut[tb.FadeAnimStep], LocalPInvoke.LWA_ALPHA);
                    tb.FadeAnimStep++;
                }
                if (tb.FadeAnimStep >= FadeSteps.FadeOut.Length)
                {
                    SetTransparent(tb.TaskbarHwnd);
                    tb.FadeAnimDir = 0;
                    tb.FadeAnimStep = 0;
                    tb.TaskbarHidden = true;
                    tb.Ignored = true;
                    Debug.WriteLine("MouseOff TB");
                }
            }
        }

        public void DoWork(object sender, DoWorkEventArgs e)
        {
            mw.interaction.AddLog("in bw");
            BackgroundWorker worker = sender as BackgroundWorker;
            while (true)
            {
                try
                {
                    if (worker.CancellationPending == true)
                    {
                        mw.interaction.AddLog("cancelling");
                        e.Cancel = true;
                        break;
                    }

                    infrequentCount++;
                    if (infrequentCount == 10)
                    {
                        List<IntPtr> windowList = Interaction.GetTopLevelWindows();
                        foreach (IntPtr hwnd in windowList)
                        {
                            StringBuilder windowClass = new StringBuilder(1024);
                            StringBuilder windowTitle = new StringBuilder(1024);
                            try
                            {
                                LocalPInvoke.GetClassName(hwnd, windowClass, 1024);
                                LocalPInvoke.GetWindowText(hwnd, windowTitle, 1024);

                                if (windowClass.ToString().Contains("HwndWrapper[RoundedTB.exe") && windowTitle.ToString() == "RoundedTB_SettingsRequest")
                                {
                                    mw.Dispatcher.Invoke(() =>
                                    {
                                        if (mw.Visibility != Visibility.Visible)
                                        {
                                            mw.ShowMenuItem_Click(null, null);
                                        }
                                    });
                                    LocalPInvoke.SetWindowText(hwnd, "RoundedTB");
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Error checking window: {ex.Message}");
                            }
                        }

                        ReloadChecker checker = new();
                        List<Types.Taskbar> infrequentBars;
                        lock (mw.DataLock)
                        {
                            infrequentBars = mw.taskbarDetails;
                        }
                        infrequentBars?.ForEach(taskbar =>
                        {
                            if (taskbar.AppListXaml != null && taskbar.AppListXaml.ReloadRequired)
                            {
                                taskbar.AppListXaml.ReloadTaskbarFrameElement();
                                checker.IsReload = true;
                            }
                        });
                        mw.interaction.RefreshUiTray(isForceReset: checker.IsReload);
                        infrequentCount = 0;
                    }

                    bool isCentred = Taskbar.CheckIfCentred();
                    List<Types.Taskbar> taskbars;
                    Types.Settings settings;
                    lock (mw.DataLock)
                    {
                        mw.activeSettings.IsCentred = isCentred;
                        taskbars = mw.taskbarDetails;
                        settings = mw.activeSettings.Clone();
                    }

                    if (taskbars == null || taskbars.Count == 0)
                    {
                        Thread.Sleep(100);
                        continue;
                    }

                    if (Taskbar.TaskbarCountOrHandleChanged(taskbars.Count, taskbars[0].TaskbarHwnd))
                    {
                        List<Types.Taskbar> regenerated = Taskbar.GenerateTaskbarInfo(mw.interaction.IsWindows11());
                        lock (mw.DataLock)
                        {
                            taskbars.ForEach(t => t.Dispose());
                            mw.taskbarDetails = regenerated;
                            taskbars = regenerated;
                        }
                        mw.interaction.RefreshUiTray(isForceReset: true);
                        mw.interaction.AddLog("Regenerating taskbar info (count/handle changed)");
                        Debug.WriteLine("Regenerating taskbar info");
                    }

                    for (int current = 0; current < taskbars.Count; current++)
                    {
                        if (taskbars[current].TaskbarHwnd == IntPtr.Zero || taskbars[current].AppListHwnd == IntPtr.Zero)
                        {
                            List<Types.Taskbar> regenerated = Taskbar.GenerateTaskbarInfo(mw.interaction.IsWindows11());
                            lock (mw.DataLock)
                            {
                                taskbars.ForEach(t => t.Dispose());
                                mw.taskbarDetails = regenerated;
                                taskbars = regenerated;
                            }
                            mw.interaction.RefreshUiTray(isForceReset: true);
                            mw.interaction.AddLog("Regenerating taskbar info (missing handle)");
                            Debug.WriteLine("Regenerating taskbar info due to a missing handle");
                            break;
                        }

                        Types.Taskbar newTaskbar = Taskbar.GetQuickTaskbarRects(
                            taskbars[current].TaskbarHwnd,
                            taskbars[current].TrayHwnd,
                            taskbars[current].AppListHwnd,
                            taskbars[current].AppListXaml);

                        // Windows native autohide (torchgm #36) v10:
                        // Peek+near-edge: pre-arm pill|strip. Show slide: stay alpha=1 until rect stable, then 255.
                        // (Revealing at alpha 255 mid-slide still shows Explorer stock frames ~half the time.)
                        bool nativeAutoHide = Taskbar.IsWindowsTaskbarAutoHideEnabled(taskbars[current].TaskbarHwnd);
                        if (nativeAutoHide && !loggedNativeAutoHideCompat)
                        {
                            mw.interaction.AddLog("Windows native autohide detected - peek arm; show stays alpha=1 until stable (torchgm #36)");
                            loggedNativeAutoHideCompat = true;
                        }

                        LocalPInvoke.RECT prevTbRect = taskbars[current].TaskbarRect;
                        bool rectMoved =
                            newTaskbar.TaskbarRect.Top != prevTbRect.Top
                            || newTaskbar.TaskbarRect.Bottom != prevTbRect.Bottom
                            || newTaskbar.TaskbarRect.Left != prevTbRect.Left
                            || newTaskbar.TaskbarRect.Right != prevTbRect.Right;
                        bool peek = nativeAutoHide && Taskbar.IsTaskbarEdgeRevealOnly(taskbars[current].TaskbarHwnd);

                        if (peek)
                        {
                            taskbars[current].TaskbarRect = newTaskbar.TaskbarRect;
                            taskbars[current].AppListRect = newTaskbar.AppListRect;
                            taskbars[current].TrayRect = newTaskbar.TrayRect;
                            taskbars[current].NativeAhFrozen = true;
                            taskbars[current].NativeAhCleared = true;
                            taskbars[current].NativeAhRevealPending = false;
                            taskbars[current].FadeAnimDir = 0;
                            taskbars[current].FadeAnimStep = 0;

                            LocalPInvoke.GetCursorPos(out LocalPInvoke.POINT edgePt);
                            bool nearEdge = Taskbar.IsCursorNearAutohideEdge(taskbars[current].TaskbarHwnd, edgePt);

                            if (nearEdge && taskbars[current].HasLastGoodLayout)
                            {
                                Taskbar.ApplyNativeAutohidePeekArmed(taskbars[current], settings);
                                fastPollRemaining = 60;
                            }
                            else
                            {
                                Taskbar.ApplyNativeAutohidePeekHitRegion(taskbars[current].TaskbarHwnd);
                                fastPollRemaining = nearEdge ? 60 : 25;
                            }
                            continue;
                        }

                        if (nativeAutoHide && rectMoved
                            && Taskbar.IsNativeAutohideHiding(prevTbRect, newTaskbar.TaskbarRect, taskbars[current].TaskbarHwnd))
                        {
                            Taskbar.SetTaskbarAlpha(taskbars[current].TaskbarHwnd, 1);
                            if (!taskbars[current].NativeAhCleared)
                            {
                                Taskbar.ResetTaskbar(taskbars[current], settings);
                                taskbars[current].NativeAhCleared = true;
                            }
                            Taskbar.SetTaskbarAlpha(taskbars[current].TaskbarHwnd, 1);
                            taskbars[current].TaskbarRect = newTaskbar.TaskbarRect;
                            taskbars[current].AppListRect = newTaskbar.AppListRect;
                            taskbars[current].TrayRect = newTaskbar.TrayRect;
                            taskbars[current].NativeAhFrozen = true;
                            taskbars[current].NativeAhRevealPending = false;
                            fastPollRemaining = 25;
                            continue;
                        }

                        bool ahShowing = nativeAutoHide && rectMoved
                            && Taskbar.IsNativeAutohideShowing(prevTbRect, newTaskbar.TaskbarRect, taskbars[current].TaskbarHwnd);
                        bool leavingPeek = nativeAutoHide
                            && (taskbars[current].NativeAhFrozen || taskbars[current].NativeAhCleared);

                        if (ahShowing || leavingPeek || taskbars[current].NativeAhRevealPending)
                        {
                            taskbars[current].NativeAhRevealPending = true;
                            taskbars[current].NativeAhFrozen = false;
                            taskbars[current].NativeAhCleared = false;
                            taskbars[current].FadeAnimDir = 0;
                            taskbars[current].FadeAnimStep = 0;

                            // Stay near-invisible for the whole slide; Explorer stock paint is then unseen.
                            Taskbar.SetTaskbarAlpha(taskbars[current].TaskbarHwnd, 1);
                            Taskbar.ApplyRounding(taskbars[current], newTaskbar, settings);

                            if (!rectMoved)
                            {
                                Taskbar.SetTaskbarAlpha(taskbars[current].TaskbarHwnd, 255);
                                taskbars[current].NativeAhRevealPending = false;
                            }

                            fastPollRemaining = 60;
                            continue;
                        }

                        // Fill-on-maximise fights native AH and intentionally undoes dynamic rounding.
                        if (!nativeAutoHide && Taskbar.TaskbarShouldBeFilled(taskbars[current].TaskbarHwnd, settings))
                        {
                            if (taskbars[current].Ignored == false)
                            {
                                Taskbar.ResetTaskbar(taskbars[current], settings);
                                taskbars[current].Ignored = true;
                            }
                            continue;
                        }

                        // Hover overrides on the snapshot only — never mutate UI-bound activeSettings.
                        // Only force refresh on hover *transitions* (Clone() resets ShowTray each loop).
                        if (settings.ShowSegmentsOnHover)
                        {
                            LocalPInvoke.RECT currentTrayRect = taskbars[current].TrayRect;
                            LocalPInvoke.RECT currentWidgetsRect = taskbars[current].TaskbarRect;
                            currentWidgetsRect.Right = Convert.ToInt32(
                                currentWidgetsRect.Right - (currentWidgetsRect.Right - currentWidgetsRect.Left) + (168 * taskbars[current].ScaleFactor));

                            if (currentTrayRect.Left != 0)
                            {
                                LocalPInvoke.GetCursorPos(out LocalPInvoke.POINT msPt);
                                bool isHoveringOverTray = LocalPInvoke.PtInRect(ref currentTrayRect, msPt);
                                bool isHoveringOverWidgets = LocalPInvoke.PtInRect(ref currentWidgetsRect, msPt);
                                settings.ShowTray = isHoveringOverTray;
                                settings.ShowWidgets = isHoveringOverWidgets;
                                if (isHoveringOverTray != taskbars[current].HoverShowTray
                                    || isHoveringOverWidgets != taskbars[current].HoverShowWidgets)
                                {
                                    taskbars[current].HoverShowTray = isHoveringOverTray;
                                    taskbars[current].HoverShowWidgets = isHoveringOverWidgets;
                                    taskbars[current].Ignored = true;
                                }
                            }
                        }

                        if (settings.AutoHide > 0 && !nativeAutoHide)
                        {
                            LocalPInvoke.RECT currentTaskbarRect = taskbars[current].TaskbarRect;
                            LocalPInvoke.GetCursorPos(out LocalPInvoke.POINT msPt);
                            bool isHoveringOverTaskbar;
                            if (taskbars[current].TaskbarHidden)
                            {
                                currentTaskbarRect.Top = currentTaskbarRect.Bottom - 2;
                                isHoveringOverTaskbar = LocalPInvoke.PtInRect(ref currentTaskbarRect, msPt);
                            }
                            else
                            {
                                isHoveringOverTaskbar = LocalPInvoke.PtInRect(ref currentTaskbarRect, msPt);
                            }

                            LocalPInvoke.GetLayeredWindowAttributes(taskbars[current].TaskbarHwnd, out _, out byte taskbarOpacity, out _);

                            if (taskbars[current].FadeAnimDir == 0)
                            {
                                if (isHoveringOverTaskbar && (taskbars[current].TaskbarHidden || taskbarOpacity <= 1))
                                {
                                    taskbars[current].FadeAnimDir = 1;
                                    taskbars[current].FadeAnimStep = 0;
                                }
                                else if (!isHoveringOverTaskbar && !taskbars[current].TaskbarHidden && taskbarOpacity >= 255)
                                {
                                    taskbars[current].FadeAnimDir = -1;
                                    taskbars[current].FadeAnimStep = 0;
                                }
                            }
                            else if (taskbars[current].FadeAnimDir == 1 && !isHoveringOverTaskbar)
                            {
                                // Reverse toward hide mid-animation.
                                taskbars[current].FadeAnimDir = -1;
                                taskbars[current].FadeAnimStep = 0;
                            }
                            else if (taskbars[current].FadeAnimDir == -1 && isHoveringOverTaskbar)
                            {
                                taskbars[current].FadeAnimDir = 1;
                                taskbars[current].FadeAnimStep = 0;
                            }

                            TickFadeAnimation(taskbars[current]);
                            if (taskbars[current].FadeAnimDir != 0)
                            {
                                fastPollRemaining = Math.Max(fastPollRemaining, 8);
                            }
                        }
                        else
                        {
                            LocalPInvoke.GetLayeredWindowAttributes(taskbars[current].TaskbarHwnd, out _, out byte taskbarOpacity, out _);
                            if (taskbarOpacity < 255 || taskbars[current].TaskbarHidden)
                            {
                                if (taskbars[current].FadeAnimDir != 1)
                                {
                                    taskbars[current].FadeAnimDir = 1;
                                    taskbars[current].FadeAnimStep = 0;
                                }
                                TickFadeAnimation(taskbars[current]);
                                if (taskbars[current].FadeAnimDir != 0)
                                {
                                    fastPollRemaining = Math.Max(fastPollRemaining, 8);
                                }
                            }
                            else
                            {
                                taskbars[current].FadeAnimDir = 0;
                                taskbars[current].FadeAnimStep = 0;
                            }
                        }

                        if (Taskbar.TaskbarRefreshRequired(taskbars[current], newTaskbar, settings.IsDynamic) || taskbars[current].Ignored || redrawOverride)
                        {
                            Debug.WriteLine($"Refresh required on taskbar {current}");
                            taskbars[current].Ignored = false;
                            Taskbar.ApplyRounding(taskbars[current], newTaskbar, settings);
                        }

                        // During Windows autohide settle, poll faster so we reapply soon after freeze ends.
                        if (nativeAutoHide && (rectMoved || peek || taskbars[current].Ignored))
                        {
                            fastPollRemaining = 25;
                        }
                    }

                    lock (mw.DataLock)
                    {
                        mw.taskbarDetails = taskbars;
                    }

                    heartbeatCount++;
                    if (heartbeatCount >= 600) // ~60s at 100ms sleep — daily use, not spam
                    {
                        mw.interaction.AddLog($"bw heartbeat bars={taskbars.Count} dyn={settings.IsDynamic} hoverSeg={settings.ShowSegmentsOnHover} fillMax={settings.FillOnMaximise}");
                        heartbeatCount = 0;
                    }

                    if (fastPollRemaining > 0)
                    {
                        fastPollRemaining--;
                        // Edge-triggered AH peek sets remaining to 60 → 1ms polls; other fast paths use 16ms.
                        Thread.Sleep(fastPollRemaining >= 40 ? 1 : 16);
                    }
                    else
                    {
                        Thread.Sleep(100);
                    }
                }
                catch (Exception ex)
                {
                    mw.interaction.AddLog($"bw exception ({ex.GetType().Name}): {ex}");
                    if (ex is TypeInitializationException tip && tip.InnerException != null)
                    {
                        mw.interaction.AddLog($"bw TypeInit inner: {tip.InnerException}");
                    }
                    // Stay alive — previous code rethrew TypeInitializationException and killed the worker.
                    Thread.Sleep(500);
                }
            }
            mw.interaction.AddLog("bw loop exited");
        }
    }
}
