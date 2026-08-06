using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Newtonsoft.Json;
using System.Runtime.InteropServices;
using Interop.UIAutomationClient;


namespace RoundedTB
{
    class Taskbar
    {
        /// <summary>
        /// Checks if the taskbar is centred.
        /// </summary>
        /// <returns>
        /// A bool indicating if the taskbar is centred.
        /// </returns>
        public static bool CheckIfCentred()
        {
            bool retVal;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced"))
                {
                    if (key != null)
                    {
                        int val = (int)key.GetValue("TaskbarAl");

                        if (val == 1)
                        {
                            retVal = true;
                        }
                        else
                        {
                            retVal = false;
                        }
                    }
                    else
                    {
                        retVal = false;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }
            return retVal;
        }

        /// <summary>
        /// Compares two taskbars' rects to see if they've changed
        /// </summary>
        /// <returns>
        /// a bool indicating if the taskbar's, applist's, and tray's rects rects have changed.
        /// </returns>
        public static bool TaskbarRefreshRequired(Types.Taskbar currentTB, Types.Taskbar newTB, bool isDynamic)
        {
            // REMINDER: newTB will only have rect & hwnd info. Everything else will be null.


            bool taskbarRectChanged = true;
            bool appListRectChanged = true;
            bool trayRectChanged = true;

            if (
                currentTB.TaskbarRect.Left == newTB.TaskbarRect.Left &&
                currentTB.TaskbarRect.Top == newTB.TaskbarRect.Top &&
                currentTB.TaskbarRect.Right == newTB.TaskbarRect.Right &&
                currentTB.TaskbarRect.Bottom == newTB.TaskbarRect.Bottom)
            {
                taskbarRectChanged = false;
            }
            if (
                currentTB.AppListRect.Left == newTB.AppListRect.Left &&
                currentTB.AppListRect.Top == newTB.AppListRect.Top &&
                currentTB.AppListRect.Right == newTB.AppListRect.Right &&
                currentTB.AppListRect.Bottom == newTB.AppListRect.Bottom)
            {
                appListRectChanged = false;
            }
            if (
                (currentTB.TrayRect.Left + 5 >= newTB.TrayRect.Left && currentTB.TrayRect.Left - 5 <= newTB.TrayRect.Left) &&
                currentTB.TrayRect.Top == newTB.TrayRect.Top &&
                currentTB.TrayRect.Right == newTB.TrayRect.Right &&
                currentTB.TrayRect.Bottom == newTB.TrayRect.Bottom)
            {
                trayRectChanged = false;
            }

            if (isDynamic && (taskbarRectChanged || appListRectChanged || trayRectChanged))
            {
                return true;
            }
            else if (!isDynamic && taskbarRectChanged)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the rects of the three components of the taskbar from their respective handles.
        /// </summary>
        /// <returns>
        /// a partial Taskbar containing just rects and handles.
        /// </returns>
        public static Types.Taskbar GetQuickTaskbarRects(IntPtr taskbarHwnd, IntPtr trayHwnd, IntPtr appListHwnd, Types.AppListXaml appListXaml)
        {
            LocalPInvoke.GetWindowRect(taskbarHwnd, out LocalPInvoke.RECT taskbarRectCheck);
            LocalPInvoke.GetWindowRect(trayHwnd, out LocalPInvoke.RECT trayRectCheck);
            LocalPInvoke.GetWindowRect(appListHwnd, out LocalPInvoke.RECT appListRectCheck);

            LocalPInvoke.RECT? r = appListXaml?.GetWindowRect();
            if (r != null)
            {
                appListRectCheck = r.Value;
            }

            return new Types.Taskbar()
            {
                AppListXaml = appListXaml,
                TaskbarHwnd = taskbarHwnd,
                TrayHwnd = trayHwnd,
                AppListHwnd = appListHwnd,
                TaskbarRect = taskbarRectCheck,
                TrayRect = trayRectCheck,
                AppListRect = appListRectCheck
            };
        }

        /// <summary>
        /// Resets the specified taskbar.
        /// </summary>
        public static void ResetTaskbar(Types.Taskbar taskbar, Types.Settings settings)
        {
            LocalPInvoke.SetWindowRgn(taskbar.TaskbarHwnd, IntPtr.Zero, true);
            LocalPInvoke.SetLayeredWindowAttributes(taskbar.TaskbarHwnd, 0, 255, LocalPInvoke.LWA_ALPHA);
            int style = LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if ((style & LocalPInvoke.WS_EX_LAYERED) == LocalPInvoke.WS_EX_LAYERED)
            {
                LocalPInvoke.SetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE, LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32() ^ LocalPInvoke.WS_EX_LAYERED);
            }
            style = LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if ((style & LocalPInvoke.WS_EX_TRANSPARENT) == LocalPInvoke.WS_EX_TRANSPARENT)
            {
                LocalPInvoke.SetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE, LocalPInvoke.GetWindowLong(taskbar.TaskbarHwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32() ^ LocalPInvoke.WS_EX_TRANSPARENT);
            }

            if (settings.CompositionCompat)
            {
                Interaction.UpdateTranslucentTB(taskbar.TaskbarHwnd);
            }
        }

        /// <summary>
        /// Creates a basic region for a specific taskbar and applies it.
        /// </summary>
        /// <returns>
        /// a bool indicating success.
        /// </returns>
        public static bool UpdateSimpleTaskbar(Types.Taskbar taskbar, Types.Settings settings)
        {
            IntPtr region = IntPtr.Zero;
            bool regionOwnedBySystem = false;
            try
            {
                // Create an effective region to be applied to the taskbar
                Types.EffectiveRegion taskbarEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = Convert.ToInt32(settings.SimpleTaskbarLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = Convert.ToInt32(settings.SimpleTaskbarLayout.MarginTop * taskbar.ScaleFactor),
                    Left = Convert.ToInt32(settings.SimpleTaskbarLayout.MarginLeft * taskbar.ScaleFactor),
                    Width = Convert.ToInt32(taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left - (settings.SimpleTaskbarLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = Convert.ToInt32(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.SimpleTaskbarLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                region = LocalPInvoke.CreateRoundRectRgn(taskbarEffectiveRegion.Left, taskbarEffectiveRegion.Top, taskbarEffectiveRegion.Width, taskbarEffectiveRegion.Height, taskbarEffectiveRegion.CornerRadius, taskbarEffectiveRegion.CornerRadius);
                if (region == IntPtr.Zero)
                {
                    return false;
                }

                // On success, the system owns the HRGN — must not DeleteObject it.
                if (LocalPInvoke.SetWindowRgn(taskbar.TaskbarHwnd, region, true) != 0)
                {
                    regionOwnedBySystem = true;
                    if (settings.CompositionCompat)
                    {
                        Interaction.UpdateTranslucentTB(taskbar.TaskbarHwnd);
                    }
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (!regionOwnedBySystem && region != IntPtr.Zero)
                {
                    LocalPInvoke.DeleteObject(region);
                }
            }
        }

        /// <summary>
        /// Creates a dynamic region for a specific taskbar and applies it.
        /// </summary>
        /// <returns>
        /// a bool indicating success.
        /// </returns>
        public static bool UpdateDynamicTaskbar(Types.Taskbar taskbar, Types.Settings settings)
        {
            IntPtr workingRegion = IntPtr.Zero;
            IntPtr trayRegion = IntPtr.Zero;
            IntPtr widgetsRegion = IntPtr.Zero;
            IntPtr clockRegion = IntPtr.Zero;
            bool workingRegionOwnedBySystem = false;
            try
            {
                int centredDistanceFromEdge = 0;

                // Create an effective region to be applied to the taskbar for the applist
                Types.EffectiveRegion taskbarEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = Convert.ToInt32(settings.DynamicAppListLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = Convert.ToInt32(settings.DynamicAppListLayout.MarginTop * taskbar.ScaleFactor),
                    Left = Convert.ToInt32(settings.DynamicAppListLayout.MarginLeft * taskbar.ScaleFactor),
                    Width = Convert.ToInt32(taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left - (settings.DynamicAppListLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = Convert.ToInt32(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicAppListLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                // Create an effective region to be applied to the taskbar for the applist
                Types.EffectiveRegion centredEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = Convert.ToInt32(settings.DynamicAppListLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = Convert.ToInt32(settings.DynamicAppListLayout.MarginTop * taskbar.ScaleFactor),
                    Left = Convert.ToInt32(settings.DynamicAppListLayout.MarginRight * taskbar.ScaleFactor) - 1,
                    Width = Convert.ToInt32(taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left - (settings.DynamicAppListLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = Convert.ToInt32(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicAppListLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                // Create an effective region to be applied to the taskbar for the tray
                Types.EffectiveRegion trayEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = Convert.ToInt32(settings.DynamicTrayLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = Convert.ToInt32(settings.DynamicTrayLayout.MarginTop * taskbar.ScaleFactor),
                    Left = Convert.ToInt32((settings.DynamicTrayLayout.MarginLeft * taskbar.ScaleFactor) - (3 * taskbar.ScaleFactor)), // Add extra margin for taskbar left as there's no "padding" provided by Windows and always looks weird as soon as you trim it otherwise.
                    Width = Convert.ToInt32(taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left - (settings.DynamicTrayLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = Convert.ToInt32(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicTrayLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                Types.EffectiveRegion widgetsEffectiveRegion = new Types.EffectiveRegion
                {
                    CornerRadius = Convert.ToInt32(settings.DynamicWidgetsLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = Convert.ToInt32(settings.DynamicWidgetsLayout.MarginTop * taskbar.ScaleFactor),
                    Left = Convert.ToInt32(settings.DynamicWidgetsLayout.MarginLeft * taskbar.ScaleFactor),
                    Width = Convert.ToInt32(settings.WidgetsWidth * taskbar.ScaleFactor - (settings.DynamicWidgetsLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = Convert.ToInt32(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicWidgetsLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };


                Types.EffectiveRegion secondaryClockRegion = new Types.EffectiveRegion
                {
                    CornerRadius = Convert.ToInt32(settings.DynamicSecondaryClockLayout.CornerRadius * taskbar.ScaleFactor),
                    Top = Convert.ToInt32(settings.DynamicSecondaryClockLayout.MarginTop * taskbar.ScaleFactor),
                    Left = (taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left) - settings.ClockWidth - Convert.ToInt32(settings.DynamicSecondaryClockLayout.MarginLeft * taskbar.ScaleFactor),
                    Width = Convert.ToInt32(settings.ClockWidth * taskbar.ScaleFactor - (settings.DynamicSecondaryClockLayout.MarginRight * taskbar.ScaleFactor)) + 1,
                    Height = Convert.ToInt32(taskbar.TaskbarRect.Bottom - taskbar.TaskbarRect.Top - (settings.DynamicSecondaryClockLayout.MarginBottom * taskbar.ScaleFactor)) + 1
                };

                centredDistanceFromEdge = taskbar.TaskbarRect.Right - taskbar.AppListRect.Right - Convert.ToInt32(2 * taskbar.ScaleFactor);

                // If on Windows 10, add an extra 20 logical pixels for the grabhandle
                if (!settings.IsWindows11)
                {
                    centredDistanceFromEdge -= Convert.ToInt32(20 * taskbar.ScaleFactor);
                }

                // Create region for if the taskbar is centred by take the right-to-right distance (centredDistanceFromEdge) off from both sides, as well as the margin
                if (settings.IsCentred)
                {
                    workingRegion = LocalPInvoke.CreateRoundRectRgn(
                        centredDistanceFromEdge + centredEffectiveRegion.Left,
                        centredEffectiveRegion.Top,
                        centredEffectiveRegion.Width - centredDistanceFromEdge,
                        centredEffectiveRegion.Height,
                        centredEffectiveRegion.CornerRadius,
                        centredEffectiveRegion.CornerRadius
                        );
                }

                // Create a region for if the taskbar is left-aligned, right-to-right distance (centredDistanceFromEdge) off from the right-hand side, as well as the margin
                else
                {

                    workingRegion = LocalPInvoke.CreateRoundRectRgn(
                        taskbarEffectiveRegion.Left,
                        taskbarEffectiveRegion.Top,
                        taskbarEffectiveRegion.Width - centredDistanceFromEdge,
                        taskbarEffectiveRegion.Height,
                        taskbarEffectiveRegion.CornerRadius,
                        taskbarEffectiveRegion.CornerRadius
                        );
                }

                if (settings.ShowSecondaryClock && taskbar.IsSecondary)
                {
                    clockRegion = LocalPInvoke.CreateRoundRectRgn(
                        secondaryClockRegion.Left,
                        secondaryClockRegion.Top,
                        secondaryClockRegion.Width + secondaryClockRegion.Left,
                        secondaryClockRegion.Height,
                        secondaryClockRegion.CornerRadius,
                        secondaryClockRegion.CornerRadius
                        );
                    LocalPInvoke.CombineRgn(workingRegion, clockRegion, workingRegion, 2);
                }

                // If the user has it enabled and the tray handle isn't null, create a region for the system tray and merge it with the taskbar region
                if (settings.ShowTray && taskbar.TrayHwnd != IntPtr.Zero)
                {
                    trayRegion = LocalPInvoke.CreateRoundRectRgn(
                        (taskbar.TrayRect.Left - taskbar.TaskbarRect.Left) - trayEffectiveRegion.Left,
                        trayEffectiveRegion.Top,
                        trayEffectiveRegion.Width,
                        trayEffectiveRegion.Height,
                        trayEffectiveRegion.CornerRadius,
                        trayEffectiveRegion.CornerRadius
                        );

                    LocalPInvoke.CombineRgn(workingRegion, trayRegion, workingRegion, 2);
                }

                if (settings.ShowWidgets)
                {
                    widgetsRegion = LocalPInvoke.CreateRoundRectRgn(
                        widgetsEffectiveRegion.Left,
                        widgetsEffectiveRegion.Top,
                        widgetsEffectiveRegion.Width,
                        widgetsEffectiveRegion.Height,
                        widgetsEffectiveRegion.CornerRadius,
                        widgetsEffectiveRegion.CornerRadius
                        );
                    LocalPInvoke.CombineRgn(workingRegion, widgetsRegion, workingRegion, 2);
                }


                // Apply the final region to the taskbar.
                // On success the system owns workingRegion — do not DeleteObject it.
                // Combined source regions (clock/tray/widgets) remain ours to free.
                if (workingRegion == IntPtr.Zero)
                {
                    return false;
                }

                if (LocalPInvoke.SetWindowRgn(taskbar.TaskbarHwnd, workingRegion, true) != 0)
                {
                    workingRegionOwnedBySystem = true;
                    if (settings.CompositionCompat)
                    {
                        Interaction.UpdateTranslucentTB(taskbar.TaskbarHwnd);
                    }
                    return true;
                }
                return false;
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                if (clockRegion != IntPtr.Zero)
                {
                    LocalPInvoke.DeleteObject(clockRegion);
                }
                if (widgetsRegion != IntPtr.Zero)
                {
                    LocalPInvoke.DeleteObject(widgetsRegion);
                }
                if (trayRegion != IntPtr.Zero)
                {
                    LocalPInvoke.DeleteObject(trayRegion);
                }
                if (!workingRegionOwnedBySystem && workingRegion != IntPtr.Zero)
                {
                    LocalPInvoke.DeleteObject(workingRegion);
                }
            }

        }

        /// <summary>
        /// Checks if there are any new taskbars, or if any taskbars are no longer present.
        /// </summary>
        /// <returns>
        /// a bool indicating success.
        /// </returns>
        public static bool TaskbarCountOrHandleChanged(int taskbarCount, IntPtr mainTaskbarHandle)
        {
            List<IntPtr> currentTaskbars = new List<IntPtr>();
            bool otherTaskbarsExist = true;
            IntPtr hwndPrevious = IntPtr.Zero;
            currentTaskbars.Add(LocalPInvoke.FindWindowExA(IntPtr.Zero, hwndPrevious, "Shell_TrayWnd", null));

            if (currentTaskbars[0] == IntPtr.Zero)
            {
                return false;
            }

            if (currentTaskbars[0] != mainTaskbarHandle)
            {
                return true;
            }

            while (otherTaskbarsExist)
            {
                IntPtr hwndCurrent = LocalPInvoke.FindWindowExA(IntPtr.Zero, hwndPrevious, "Shell_SecondaryTrayWnd", null);
                hwndPrevious = hwndCurrent;

                if (hwndCurrent == IntPtr.Zero)
                {
                    otherTaskbarsExist = false;
                }
                else
                {
                    currentTaskbars.Add(hwndCurrent);
                }
            }
            if (currentTaskbars.Count != taskbarCount)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Caps AppListRect.Right so the dynamic pill never overlaps the tray.
        /// Prevents CheckDynamicUpdateIsValid from rejecting updates (frozen pill) when many icons are open.
        /// </summary>
        public static Types.Taskbar ClampAppListAwayFromTray(Types.Taskbar tb, double scaleFactor)
        {
            if (tb == null || tb.TrayRect.Left == 0)
            {
                return tb;
            }

            int pad = Math.Max(2, Convert.ToInt32(2 * scaleFactor));
            int maxRight = tb.TrayRect.Left - pad;
            if (tb.AppListRect.Right <= maxRight)
            {
                return tb;
            }

            LocalPInvoke.RECT app = tb.AppListRect;
            int minRight = app.Left + Math.Max(pad, Convert.ToInt32(20 * scaleFactor));
            app.Right = Math.Max(minRight, maxRight);

            return new Types.Taskbar
            {
                TaskbarHwnd = tb.TaskbarHwnd,
                TrayHwnd = tb.TrayHwnd,
                AppListHwnd = tb.AppListHwnd,
                AppListXaml = tb.AppListXaml,
                TaskbarRect = tb.TaskbarRect,
                TrayRect = tb.TrayRect,
                AppListRect = app,
                ScaleFactor = tb.ScaleFactor,
                IsSecondary = tb.IsSecondary
            };
        }

        /// <summary>
        /// Checks if the provided update is valid.
        /// </summary>
        /// <returns>
        /// A bool indicating if the update is valid.
        /// </returns>
        public static bool CheckDynamicUpdateIsValid(Types.Taskbar currentTB, Types.Taskbar newTB)
        {
            // REMINDER: newTB will only have rect & hwnd info. Everything else will be null.

            // Check if either of the supplied taskbars are null
            if (currentTB == null || newTB == null)
            {
                return false;
            }

            // Check if the taskbar handles are different
            if (currentTB.TaskbarHwnd != newTB.TaskbarHwnd)
            {
                return false;
            }

            // Get width of app list. Not strictly necessary as the applist is always measured from the left but doing so just in case
            int newAppListWidth = newTB.AppListRect.Right - newTB.AppListRect.Left;
            int currentAppListWidth = currentTB.AppListRect.Right - currentTB.AppListRect.Left;

            // Overlap with tray: caller should ClampAppListAwayFromTray first. Still reject raw overlap.
            if (newTB.TrayRect.Left != 0 && newTB.AppListRect.Right > newTB.TrayRect.Left)
            {
                return false;
            }

            if (newAppListWidth <= 20 * currentTB.ScaleFactor && newAppListWidth != 0)
            {
                return false;
            }

            if (newAppListWidth >= newTB.TaskbarRect.Right - newTB.TaskbarRect.Left && newAppListWidth != 0)
            {
                return false;
            }

            Debug.WriteLine($"Old width: {currentAppListWidth}\nNew width: {newAppListWidth}");
            return true;
        }


        /// <summary>Get AppList handle for win23h2 and later. </summary>
        public static Types.AppListXaml GetAppListSince23H2(IntPtr hwndTaskbarMain)
        {
            return new Types.AppListXaml(hwndTaskbarMain);
        }


        /// <summary>
        /// Collects information on any currently-present taskbars.
        /// </summary>
        /// <returns>
        /// A list of taskbars populated with information about their size, handles etc.
        /// </returns>
        public static List<Types.Taskbar> GenerateTaskbarInfo(bool isWindows11)
        {
            List<Types.Taskbar> retVal = new List<Types.Taskbar>();

            IntPtr hwndMain = LocalPInvoke.FindWindowExA(IntPtr.Zero, IntPtr.Zero, "Shell_TrayWnd", null); // Find main taskbar
            LocalPInvoke.GetWindowRect(hwndMain, out LocalPInvoke.RECT rectMain); // Get the RECT of the main taskbar
            IntPtr hrgnMain = IntPtr.Zero; // Set recovery region to IntPtr.Zero
            IntPtr hwndTray = LocalPInvoke.FindWindowExA(hwndMain, IntPtr.Zero, "TrayNotifyWnd", null); // Get handle to the main taskbar's tray
            LocalPInvoke.GetWindowRect(hwndTray, out LocalPInvoke.RECT rectTray); // Get the RECT for the main taskbar's tray
            IntPtr hwndAppList = LocalPInvoke.FindWindowExA(LocalPInvoke.FindWindowExA(hwndMain, IntPtr.Zero, "ReBarWindow32", null), IntPtr.Zero, "MSTaskSwWClass", null); // Get the handle to the main taskbar's app list
            LocalPInvoke.GetWindowRect(hwndAppList, out LocalPInvoke.RECT rectAppList);// Get the RECT for the main taskbar's app list
            Types.AppListXaml appList = GetAppListSince23H2(hwndMain);

            retVal.Add(new Types.Taskbar
            {
                AppListXaml = appList,
                TaskbarHwnd = hwndMain,
                TrayHwnd = hwndTray,
                AppListHwnd = hwndAppList,
                TaskbarRect = rectMain,
                TrayRect = rectTray,
                AppListRect = rectAppList,
                RecoveryHrgn = hrgnMain,
                ScaleFactor = Convert.ToDouble(LocalPInvoke.GetDpiForWindow(hwndMain)) / 96.00,
                TaskbarRes = $"{rectMain.Right - rectMain.Left} x {rectMain.Bottom - rectMain.Top}",
                Ignored = false,
                IsSecondary = false,
            });
            int style = LocalPInvoke.GetWindowLong(hwndMain, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if ((style & LocalPInvoke.WS_EX_LAYERED) != LocalPInvoke.WS_EX_LAYERED)
            {
                LocalPInvoke.SetWindowLong(hwndMain, LocalPInvoke.GWL_EXSTYLE, LocalPInvoke.GetWindowLong(hwndMain, LocalPInvoke.GWL_EXSTYLE).ToInt32() ^ LocalPInvoke.WS_EX_LAYERED);
                LocalPInvoke.SetLayeredWindowAttributes(hwndMain, 0, 255, LocalPInvoke.LWA_ALPHA);
            }




            bool i = true;
            IntPtr hwndPrevious = IntPtr.Zero;
            while (i)
            {
                IntPtr hwndCurrent = LocalPInvoke.FindWindowExA(IntPtr.Zero, hwndPrevious, "Shell_SecondaryTrayWnd", null);
                hwndPrevious = hwndCurrent;

                if (hwndCurrent == IntPtr.Zero)
                {
                    i = false;
                }
                else
                {
                    LocalPInvoke.GetWindowRect(hwndCurrent, out LocalPInvoke.RECT rectCurrent);
                    IntPtr hrgnCurrent = IntPtr.Zero;
                    IntPtr hwndSecTray = IntPtr.Zero;
                    if (isWindows11)
                    {
                        IntPtr imd = LocalPInvoke.FindWindowExA(hwndCurrent, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                        hwndSecTray = LocalPInvoke.FindWindowExA(hwndCurrent, imd, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                    }
                    else
                    {
                        hwndSecTray = LocalPInvoke.FindWindowExA(hwndCurrent, IntPtr.Zero, "TrayNotifyWnd", null); // Get handle to this secondary taskbar's tray
                    }
                    LocalPInvoke.RECT rectSecTray = default;
                    if (hwndSecTray != IntPtr.Zero)
                    {
                        LocalPInvoke.GetWindowRect(hwndSecTray, out rectSecTray);
                    }
                    IntPtr hwndWorkerW = LocalPInvoke.FindWindowExA(hwndCurrent, IntPtr.Zero, "WorkerW", null);
                    IntPtr hwndSecAppList = IntPtr.Zero;
                    // windows 11 22H2 has multiple WorkerW handles.
                    while (hwndWorkerW != IntPtr.Zero)
                    {
                        hwndSecAppList = LocalPInvoke.FindWindowExA(hwndWorkerW, IntPtr.Zero, "MSTaskListWClass", null); // Get the handle to the main taskbar's app list
                        if (hwndSecAppList != IntPtr.Zero)
                        {
                            break;
                        }
                        hwndWorkerW = LocalPInvoke.FindWindowExA(hwndCurrent, hwndWorkerW, "WorkerW", null);
                    }
                    LocalPInvoke.GetWindowRect(hwndSecAppList, out LocalPInvoke.RECT rectSecAppList);// Get the RECT for this secondary taskbar's app list
                    Types.AppListXaml appListSec = GetAppListSince23H2(hwndCurrent);
                    retVal.Add(new Types.Taskbar
                    {
                        AppListXaml = appListSec,
                        TaskbarHwnd = hwndCurrent,
                        TrayHwnd = hwndSecTray,
                        AppListHwnd = hwndSecAppList,
                        TaskbarRect = rectCurrent,
                        TrayRect = rectSecTray,
                        AppListRect = rectSecAppList,
                        RecoveryHrgn = hrgnCurrent,
                        ScaleFactor = Convert.ToDouble(LocalPInvoke.GetDpiForWindow(hwndCurrent)) / 96.00,
                        TaskbarRes = $"{rectCurrent.Right - rectCurrent.Left} x {rectCurrent.Bottom - rectCurrent.Top}",
                        Ignored = false,
                        IsSecondary = true,
                    });
                    style = LocalPInvoke.GetWindowLong(hwndCurrent, LocalPInvoke.GWL_EXSTYLE).ToInt32();
                    if ((style & LocalPInvoke.WS_EX_LAYERED) != LocalPInvoke.WS_EX_LAYERED)
                    {
                        LocalPInvoke.SetWindowLong(hwndCurrent, LocalPInvoke.GWL_EXSTYLE, LocalPInvoke.GetWindowLong(hwndCurrent, LocalPInvoke.GWL_EXSTYLE).ToInt32() ^ LocalPInvoke.WS_EX_LAYERED);
                        LocalPInvoke.SetLayeredWindowAttributes(hwndCurrent, 0, 255, LocalPInvoke.LWA_ALPHA);
                    }
                }
            }

            //foreach (var tb in retVal)
            //{
            //    TaskbarShouldBeFilled(tb.TaskbarHwnd);
            //}
            try
            {
                TaskbarWatchdog.PublishHwnds(
                    retVal.Select(t => t.TaskbarHwnd),
                    System.Diagnostics.Process.GetCurrentProcess().Id);
            }
            catch
            {
                // Watchdog publish is best-effort.
            }
            return retVal;
        }

        /// <summary>
        /// Checks if the given taskbar should be filled to the edge of the screen.
        /// </summary>
        /// <returns>
        /// A bool indicating whether or not the taskbar needs to be filled.
        /// </returns>
        public static bool TaskbarShouldBeFilled(IntPtr taskbarHwnd, Types.Settings settings)
        {
            bool retVal = false;

            if (settings.FillOnMaximise)
            {
                // Attempt to check for if alt+tab/task switcher is open (Windows 11 only)
                IntPtr topHwnd = LocalPInvoke.WindowFromPoint(new LocalPInvoke.POINT() { x = 0, y = 0 });
                StringBuilder windowClass = new StringBuilder(1024);
                try
                {
                    LocalPInvoke.GetClassName(topHwnd, windowClass, 1024);

                    if (windowClass.ToString() == "XamlExplorerHostIslandWindow" && settings.FillOnTaskSwitch)
                    {
                        return true;
                    }
                }
                catch (Exception) { }

                List<IntPtr> windowList = Interaction.GetTopLevelWindows();
                foreach (IntPtr windowHwnd in windowList)
                {
                    if (LocalPInvoke.IsWindowVisible(windowHwnd))
                    {
                        if (LocalPInvoke.MonitorFromWindow(taskbarHwnd, 2) == LocalPInvoke.MonitorFromWindow(windowHwnd, 2))
                        {
                            LocalPInvoke.DwmGetWindowAttribute(windowHwnd, LocalPInvoke.DWMWINDOWATTRIBUTE.Cloaked, out bool isCloaked, 0x4);
                            if (!isCloaked)
                            {
                                LocalPInvoke.WINDOWPLACEMENT lpwndpl = new LocalPInvoke.WINDOWPLACEMENT();
                                LocalPInvoke.GetWindowPlacement(windowHwnd, ref lpwndpl);
                                if (lpwndpl.ShowCmd == LocalPInvoke.ShowWindowCommands.ShowMaximized)
                                {
                                    retVal = true;
                                }
                            }
                        }
                    }
                }
            }

            return retVal;
        }

        /// <summary>
        /// Ensure WS_EX_LAYERED and set per-window alpha (0 = invisible). Used to hide stock TB during native AH show.
        /// </summary>
        public static void SetTaskbarAlpha(IntPtr hwnd, byte alpha)
        {
            if (hwnd == IntPtr.Zero || !LocalPInvoke.IsWindow(hwnd))
            {
                return;
            }

            int style = LocalPInvoke.GetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE).ToInt32();
            if ((style & LocalPInvoke.WS_EX_LAYERED) == 0)
            {
                LocalPInvoke.SetWindowLong(hwnd, LocalPInvoke.GWL_EXSTYLE, style | LocalPInvoke.WS_EX_LAYERED);
            }
            LocalPInvoke.SetLayeredWindowAttributes(hwnd, 0, alpha, LocalPInvoke.LWA_ALPHA);
        }

        /// <summary>
        /// Apply simple or dynamic rounding for current measured rects (shared by worker refresh / AH reveal).
        /// </summary>
        public static void ApplyRounding(Types.Taskbar taskbar, Types.Taskbar measured, Types.Settings settings)
        {
            Types.Taskbar m = measured;
            if (settings.IsDynamic)
            {
                m = ClampAppListAwayFromTray(measured, taskbar.ScaleFactor);

                // First frames after AH show often report a full-width AppList — keep last good pill.
                int tbW = Math.Max(1, m.TaskbarRect.Right - m.TaskbarRect.Left);
                int appW = m.AppListRect.Right - m.AppListRect.Left;
                if (taskbar.HasLastGoodLayout && appW >= (int)(tbW * 0.9))
                {
                    LocalPInvoke.RECT app = taskbar.LastGoodAppListRect;
                    LocalPInvoke.RECT tray = taskbar.LastGoodTrayRect;
                    m = new Types.Taskbar
                    {
                        TaskbarHwnd = m.TaskbarHwnd,
                        TrayHwnd = m.TrayHwnd,
                        AppListHwnd = m.AppListHwnd,
                        AppListXaml = m.AppListXaml,
                        TaskbarRect = m.TaskbarRect,
                        TrayRect = tray.Left != 0 ? tray : m.TrayRect,
                        AppListRect = app,
                        ScaleFactor = m.ScaleFactor,
                        IsSecondary = m.IsSecondary
                    };
                }
            }

            int gap = m.TrayRect.Left - m.AppListRect.Right;
            bool forceSimpleNearTray = settings.ShowTray
                && m.TrayRect.Left != 0
                && gap <= taskbar.ScaleFactor * 25
                && gap > 0;

            if (!settings.IsDynamic || forceSimpleNearTray)
            {
                taskbar.TaskbarRect = measured.TaskbarRect;
                taskbar.AppListRect = measured.AppListRect;
                taskbar.TrayRect = measured.TrayRect;
                if (UpdateSimpleTaskbar(taskbar, settings))
                {
                    RememberGoodLayout(taskbar);
                }
                return;
            }

            if (CheckDynamicUpdateIsValid(taskbar, m))
            {
                taskbar.TaskbarRect = m.TaskbarRect;
                taskbar.AppListRect = m.AppListRect;
                taskbar.TrayRect = m.TrayRect;
                if (UpdateDynamicTaskbar(taskbar, settings))
                {
                    RememberGoodLayout(taskbar);
                }
                return;
            }

            int w = m.AppListRect.Right - m.AppListRect.Left;
            if (w > 20 * taskbar.ScaleFactor)
            {
                taskbar.TaskbarRect = m.TaskbarRect;
                taskbar.AppListRect = m.AppListRect;
                taskbar.TrayRect = m.TrayRect;
                if (UpdateDynamicTaskbar(taskbar, settings))
                {
                    RememberGoodLayout(taskbar);
                }
            }
        }

        public static void RememberGoodLayout(Types.Taskbar taskbar)
        {
            if (taskbar == null)
            {
                return;
            }
            int w = taskbar.AppListRect.Right - taskbar.AppListRect.Left;
            int tbW = taskbar.TaskbarRect.Right - taskbar.TaskbarRect.Left;
            if (w <= 20 * taskbar.ScaleFactor || (tbW > 0 && w >= tbW * 0.9))
            {
                return;
            }
            taskbar.LastGoodAppListRect = taskbar.AppListRect;
            taskbar.LastGoodTrayRect = taskbar.TrayRect;
            taskbar.HasLastGoodLayout = true;
        }

        /// <summary>
        /// While peeked with cursor on the edge: hit-strip OR last-good pill so the bar is already rounded
        /// when Explorer slides it on-screen (avoids stock full-bar flash).
        /// </summary>
        public static bool ApplyNativeAutohidePeekArmed(Types.Taskbar taskbar, Types.Settings settings)
        {
            if (taskbar == null || taskbar.TaskbarHwnd == IntPtr.Zero)
            {
                return false;
            }

            if (!taskbar.HasLastGoodLayout)
            {
                return ApplyNativeAutohidePeekHitRegion(taskbar.TaskbarHwnd);
            }

            // Apply last-good pill for current client size, then OR the peek hit-strip for hover.
            Types.Taskbar measured = new Types.Taskbar
            {
                TaskbarHwnd = taskbar.TaskbarHwnd,
                TrayHwnd = taskbar.TrayHwnd,
                AppListHwnd = taskbar.AppListHwnd,
                TaskbarRect = taskbar.TaskbarRect,
                AppListRect = taskbar.LastGoodAppListRect,
                TrayRect = taskbar.LastGoodTrayRect,
                ScaleFactor = taskbar.ScaleFactor,
                IsSecondary = taskbar.IsSecondary,
                HasLastGoodLayout = true,
                LastGoodAppListRect = taskbar.LastGoodAppListRect,
                LastGoodTrayRect = taskbar.LastGoodTrayRect
            };

            if (settings.IsDynamic)
            {
                taskbar.TaskbarRect = measured.TaskbarRect;
                taskbar.AppListRect = measured.AppListRect;
                taskbar.TrayRect = measured.TrayRect;
                if (!UpdateDynamicTaskbar(taskbar, settings))
                {
                    return ApplyNativeAutohidePeekHitRegion(taskbar.TaskbarHwnd);
                }
            }
            else
            {
                taskbar.TaskbarRect = measured.TaskbarRect;
                if (!UpdateSimpleTaskbar(taskbar, settings))
                {
                    return ApplyNativeAutohidePeekHitRegion(taskbar.TaskbarHwnd);
                }
            }

            IntPtr strip = CreateNativeAutohidePeekStripRegion(taskbar.TaskbarHwnd);
            if (strip == IntPtr.Zero)
            {
                return true; // pill alone is better than nothing
            }

            IntPtr pillCopy = LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
            IntPtr combined = LocalPInvoke.CreateRectRgn(0, 0, 0, 0);
            bool combinedOwnedBySystem = false;
            try
            {
                LocalPInvoke.GetWindowRgn(taskbar.TaskbarHwnd, pillCopy);
                LocalPInvoke.CombineRgn(combined, pillCopy, strip, LocalPInvoke.RGN_OR);
                if (LocalPInvoke.SetWindowRgn(taskbar.TaskbarHwnd, combined, true) != 0)
                {
                    combinedOwnedBySystem = true;
                    return true;
                }
                return false;
            }
            finally
            {
                LocalPInvoke.DeleteObject(pillCopy);
                LocalPInvoke.DeleteObject(strip);
                if (!combinedOwnedBySystem && combined != IntPtr.Zero)
                {
                    LocalPInvoke.DeleteObject(combined);
                }
            }
        }

        static IntPtr CreateNativeAutohidePeekStripRegion(IntPtr hwnd)
        {
            if (!LocalPInvoke.GetWindowRect(hwnd, out LocalPInvoke.RECT wr))
            {
                return IntPtr.Zero;
            }

            IntPtr hMon = LocalPInvoke.MonitorFromWindow(hwnd, 2);
            MonitorStuff.MONITORINFO mi = new MonitorStuff.MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf(mi);
            if (!MonitorStuff.GetMonitorInfo(hMon, ref mi))
            {
                return IntPtr.Zero;
            }

            LocalPInvoke.RECT mon = mi.rcMonitor;
            int winW = Math.Max(1, wr.Right - wr.Left);
            int winH = Math.Max(1, wr.Bottom - wr.Top);
            int strip = Math.Max(6, winH / 8);
            if (strip > winH)
            {
                strip = winH;
            }

            if (wr.Bottom > mon.Bottom && wr.Top < mon.Bottom)
            {
                return LocalPInvoke.CreateRectRgn(0, 0, winW + 1, strip + 1);
            }
            if (wr.Top < mon.Top && wr.Bottom > mon.Top)
            {
                return LocalPInvoke.CreateRectRgn(0, winH - strip, winW + 1, winH + 1);
            }
            if (wr.Right > mon.Right && wr.Left < mon.Right)
            {
                return LocalPInvoke.CreateRectRgn(0, 0, strip + 1, winH + 1);
            }
            if (wr.Left < mon.Left && wr.Right > mon.Left)
            {
                return LocalPInvoke.CreateRectRgn(winW - strip, 0, winW + 1, winH + 1);
            }
            return LocalPInvoke.CreateRectRgn(0, 0, winW + 1, strip + 1);
        }

        /// <summary>
        /// True when Windows Settings → Automatically hide the taskbar is active for this appbar.
        /// </summary>
        public static bool IsWindowsTaskbarAutoHideEnabled(IntPtr hwnd)
        {
            LocalPInvoke.APPBARDATA data = new LocalPInvoke.APPBARDATA();
            data.cbSize = (uint)Marshal.SizeOf(data);
            data.hWnd = hwnd;
            IntPtr result = LocalPInvoke.SHAppBarMessage(LocalPInvoke.ABM.GetState, ref data);
            return (result.ToInt32() & LocalPInvoke.ABS.Autohide) == LocalPInvoke.ABS.Autohide;
        }

        /// <summary>
        /// Cursor is in the monitor band that typically triggers Windows taskbar autohide reveal.
        /// Wider than the peek window so we pre-arm before Explorer starts sliding.
        /// </summary>
        public static bool IsCursorNearAutohideEdge(IntPtr hwnd, LocalPInvoke.POINT pt)
        {
            IntPtr hMon = LocalPInvoke.MonitorFromWindow(hwnd, 2);
            MonitorStuff.MONITORINFO mi = new MonitorStuff.MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf(mi);
            if (!MonitorStuff.GetMonitorInfo(hMon, ref mi))
            {
                return false;
            }

            LocalPInvoke.RECT mon = mi.rcMonitor;
            const int band = 64;
            if (!LocalPInvoke.GetWindowRect(hwnd, out LocalPInvoke.RECT wr))
            {
                return pt.y >= mon.Bottom - band;
            }

            // Infer dock edge from which side of the monitor the window overhangs / sits on.
            int midY = (wr.Top + wr.Bottom) / 2;
            int midX = (wr.Left + wr.Right) / 2;
            if (midY > (mon.Top + mon.Bottom) / 2)
            {
                return pt.y >= mon.Bottom - band;
            }
            if (midY < (mon.Top + mon.Bottom) / 2)
            {
                return pt.y <= mon.Top + band;
            }
            if (midX > (mon.Left + mon.Right) / 2)
            {
                return pt.x >= mon.Right - band;
            }
            return pt.x <= mon.Left + band;
        }

        /// <summary>
        /// While the taskbar is edge-peeked (native AH), apply only a thin full-width strip so hit-testing
        /// works. Leaving a centred/rounded RGN from the fully-shown state clips that strip → hover fails.
        /// Do not use this while the bar is fully shown (causes hairline ghosts in dynamic gaps).
        /// </summary>
        public static bool ApplyNativeAutohidePeekHitRegion(IntPtr hwnd)
        {
            if (!LocalPInvoke.GetWindowRect(hwnd, out LocalPInvoke.RECT wr))
            {
                return false;
            }

            IntPtr hMon = LocalPInvoke.MonitorFromWindow(hwnd, 2);
            MonitorStuff.MONITORINFO mi = new MonitorStuff.MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf(mi);
            if (!MonitorStuff.GetMonitorInfo(hMon, ref mi))
            {
                return false;
            }

            LocalPInvoke.RECT mon = mi.rcMonitor;
            int winW = Math.Max(1, wr.Right - wr.Left);
            int winH = Math.Max(1, wr.Bottom - wr.Top);
            int strip = Math.Max(6, winH / 8);
            if (strip > winH)
            {
                strip = winH;
            }

            IntPtr region = IntPtr.Zero;
            bool ownedBySystem = false;
            try
            {
                // Client-relative strip on the edge that still intersects the monitor.
                if (wr.Bottom > mon.Bottom && wr.Top < mon.Bottom)
                {
                    // Bottom-docked, slid down — visible band is the top of the window.
                    region = LocalPInvoke.CreateRectRgn(0, 0, winW + 1, strip + 1);
                }
                else if (wr.Top < mon.Top && wr.Bottom > mon.Top)
                {
                    // Top-docked, slid up — visible band is the bottom of the window.
                    region = LocalPInvoke.CreateRectRgn(0, winH - strip, winW + 1, winH + 1);
                }
                else if (wr.Right > mon.Right && wr.Left < mon.Right)
                {
                    region = LocalPInvoke.CreateRectRgn(0, 0, strip + 1, winH + 1);
                }
                else if (wr.Left < mon.Left && wr.Right > mon.Left)
                {
                    region = LocalPInvoke.CreateRectRgn(winW - strip, 0, winW + 1, winH + 1);
                }
                else
                {
                    region = LocalPInvoke.CreateRectRgn(0, 0, winW + 1, strip + 1);
                }

                if (region == IntPtr.Zero)
                {
                    return false;
                }

                if (LocalPInvoke.SetWindowRgn(hwnd, region, true) != 0)
                {
                    ownedBySystem = true;
                    return true;
                }
                return false;
            }
            finally
            {
                if (!ownedBySystem && region != IntPtr.Zero)
                {
                    LocalPInvoke.DeleteObject(region);
                }
            }
        }

        /// <summary>
        /// Intersection of a window rect with the monitor that contains <paramref name="hwnd"/>.
        /// Returns false if rect/monitor lookup fails.
        /// </summary>
        public static bool TryGetVisibleTaskbarArea(IntPtr hwnd, LocalPInvoke.RECT wr, out int visibleW, out int visibleH, out int winW, out int winH)
        {
            visibleW = 0;
            visibleH = 0;
            winW = Math.Max(1, wr.Right - wr.Left);
            winH = Math.Max(1, wr.Bottom - wr.Top);

            IntPtr hMon = LocalPInvoke.MonitorFromWindow(hwnd, 2);
            MonitorStuff.MONITORINFO mi = new MonitorStuff.MONITORINFO();
            mi.cbSize = (uint)Marshal.SizeOf(mi);
            if (!MonitorStuff.GetMonitorInfo(hMon, ref mi))
            {
                return false;
            }

            LocalPInvoke.RECT mon = mi.rcMonitor;
            int visTop = Math.Max(wr.Top, mon.Top);
            int visBot = Math.Min(wr.Bottom, mon.Bottom);
            int visLeft = Math.Max(wr.Left, mon.Left);
            int visRight = Math.Min(wr.Right, mon.Right);
            visibleH = Math.Max(0, visBot - visTop);
            visibleW = Math.Max(0, visRight - visLeft);
            return true;
        }

        /// <summary>
        /// True when the taskbar window is mostly off its monitor (Windows native autohide peek).
        /// Upstream (torchgm #36): do not fight SetWindowRgn while peeked/sliding — freeze RTB updates.
        /// </summary>
        public static bool IsTaskbarEdgeRevealOnly(IntPtr hwnd)
        {
            if (!LocalPInvoke.GetWindowRect(hwnd, out LocalPInvoke.RECT wr))
            {
                return false;
            }

            if (!TryGetVisibleTaskbarArea(hwnd, wr, out int visibleW, out int visibleH, out int width, out int height))
            {
                return false;
            }

            if (visibleW > width / 2 && visibleH > 0 && visibleH < Math.Max(16, height / 3))
            {
                return true;
            }
            if (visibleH > height / 2 && visibleW > 0 && visibleW < Math.Max(16, width / 3))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Native AH slide toward fully shown: on-monitor visible area grew vs previous rect.
        /// </summary>
        public static bool IsNativeAutohideShowing(LocalPInvoke.RECT prevRect, LocalPInvoke.RECT newRect, IntPtr hwnd)
        {
            if (!TryGetVisibleTaskbarArea(hwnd, prevRect, out int prevW, out int prevH, out _, out _))
            {
                return false;
            }
            if (!TryGetVisibleTaskbarArea(hwnd, newRect, out int newW, out int newH, out _, out _))
            {
                return false;
            }
            int prevArea = prevW * prevH;
            int newArea = newW * newH;
            return newArea > prevArea;
        }

        /// <summary>
        /// Native AH slide toward peek/off-screen: on-monitor visible area shrank vs previous rect.
        /// </summary>
        public static bool IsNativeAutohideHiding(LocalPInvoke.RECT prevRect, LocalPInvoke.RECT newRect, IntPtr hwnd)
        {
            if (!TryGetVisibleTaskbarArea(hwnd, prevRect, out int prevW, out int prevH, out _, out _))
            {
                return false;
            }
            if (!TryGetVisibleTaskbarArea(hwnd, newRect, out int newW, out int newH, out _, out _))
            {
                return false;
            }
            int prevArea = prevW * prevH;
            int newArea = newW * newH;
            return newArea < prevArea;
        }

        /// <summary>
        /// Sets the appbar properties of the taskbar.
        /// </summary>
        public static void SetTaskbarState(LocalPInvoke.AppBarStates option, IntPtr hwnd)
        {
            LocalPInvoke.APPBARDATA msgData = new LocalPInvoke.APPBARDATA();
            msgData.cbSize = (uint)Marshal.SizeOf(msgData);
            msgData.hWnd = hwnd;
            msgData.lParam = (int)option;
            LocalPInvoke.SHAppBarMessage(LocalPInvoke.ABM.SetState, ref msgData);
        }


    }
}
