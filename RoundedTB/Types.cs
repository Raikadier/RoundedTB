using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;
using Interop.UIAutomationClient;


namespace RoundedTB
{
    public class Types
    {
        public class Taskbar : IDisposable
        {
            public AppListXaml AppListXaml { get; set; }
            public IntPtr TaskbarHwnd { get; set; } // Handle to the taskbar
            public IntPtr TrayHwnd { get; set; } // Handle to the tray on the taskbar (if present)
            public IntPtr AppListHwnd { get; set; } // Handle to the list of open/pinned apps on the taskbar
            public LocalPInvoke.RECT TaskbarRect { get; set; } // Bounding box for the taskbar
            public LocalPInvoke.RECT TrayRect { get; set; }  // Bounding box for the tray (dynamic)
            public LocalPInvoke.RECT AppListRect { get; set; } // Bounding box for the list of pinned & open apps (dynamic)
            public IntPtr RecoveryHrgn { get; set; } // Pointer to the recovery region for any given taskbar. Defaults to IntPtr.Zero
            public double ScaleFactor { get; set; } // The scale factor of the monitor the taskbar is on
            public string TaskbarRes { get; set; } // Resolution of the taskbar as text
            public bool Ignored { get; set; } // Specifies if the taskbar should be ignored when applying changes
            public bool TaskbarHidden { get; set; } // Specifies if this taskbar is currently hidden by RTB
            public bool TrayHidden { get; set; } // Specifies if the tray is currently hidden by RTB on this taskbar
            public int AppListWidth { get; set; } // Specifies the width of the app list
            public bool IsSecondary { get; set; }
            /// <summary>Non-blocking fade: 0 idle, 1 fade-in, -1 fade-out.</summary>
            public int FadeAnimDir { get; set; }
            /// <summary>Index into FadeSteps for the current direction.</summary>
            public int FadeAnimStep { get; set; }
            /// <summary>Last applied ShowSegmentsOnHover tray override (avoid SetWindowRgn every tick).</summary>
            public bool HoverShowTray { get; set; }
            /// <summary>Last applied ShowSegmentsOnHover widgets override.</summary>
            public bool HoverShowWidgets { get; set; }
            /// <summary>True while skipping SetWindowRgn because Windows native autohide is sliding/peeking.</summary>
            public bool NativeAhFrozen { get; set; }
            /// <summary>True after clearing RTB region so Explorer can slide the AppBar away.</summary>
            public bool NativeAhCleared { get; set; }
            /// <summary>Last stable AppList rect while fully shown — used to pre-arm pill before Windows AH slides up.</summary>
            public LocalPInvoke.RECT LastGoodAppListRect { get; set; }
            /// <summary>Last stable Tray rect while fully shown.</summary>
            public LocalPInvoke.RECT LastGoodTrayRect { get; set; }
            public bool HasLastGoodLayout { get; set; }
            /// <summary>True after leaving peek until AH show animation finishes — keep alpha low so stock frames stay invisible.</summary>
            public bool NativeAhRevealPending { get; set; }
            /// <summary>TickCount when rect first went stable during reveal; 0 = still moving. Used to debounce alpha 255.</summary>
            public int NativeAhRevealStableSinceTick { get; set; }

            public void Dispose()
            {
                AppListXaml?.Dispose();
            }
        }

#nullable enable
        public class AppListXaml : IDisposable
        {
            private IUIAutomationElement? _taskbarFrame;
            private IUIAutomation? _uia;
            private readonly IntPtr _hwndTaskbarMain;

            // singleton checker
            private static bool appListXamlAlreadyExists = false;

            public bool ReloadRequired => (AppListXaml.appListXamlAlreadyExists && this._taskbarFrame == null);

            public AppListXaml(IntPtr hwndTaskbarMain)
            {
                this._hwndTaskbarMain = hwndTaskbarMain;
                this._uia = new CUIAutomation();
                _taskbarFrame = GetTaskbarFrameElement(this._hwndTaskbarMain, this._uia);
            }

            private static IUIAutomationElement? GetTaskbarFrameElement(IntPtr hwndTaskbarMain, IUIAutomation uia)
            {
                if (!LocalPInvoke.IsWindow(hwndTaskbarMain))
                {
                    return null;
                }

                // Fast path: hardcoded HWND descent (Win11 21H2..23H2).
                try
                {
                    IntPtr hwndDesktopXamlSrc = LocalPInvoke.FindWindowExA(hwndTaskbarMain, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null);
                    if (hwndDesktopXamlSrc != IntPtr.Zero)
                    {
                        IntPtr hwndWindowCls = LocalPInvoke.FindWindowExA(hwndDesktopXamlSrc, IntPtr.Zero, "Windows.UI.Input.InputSite.WindowClass", null);
                        if (hwndWindowCls != IntPtr.Zero)
                        {
                            IUIAutomationElement taskEle = uia.ElementFromHandle(hwndWindowCls);
                            IUIAutomationCondition con = uia.CreatePropertyCondition(UIA_PropertyIds.UIA_AutomationIdPropertyId, "TaskbarFrame");
                            IUIAutomationElement? taskFrameEle = taskEle.FindFirst(Interop.UIAutomationClient.TreeScope.TreeScope_Children, con);

                            Marshal.ReleaseComObject(con);
                            Marshal.ReleaseComObject(taskEle);
                            if (taskFrameEle != null)
                            {
                                AppListXaml.appListXamlAlreadyExists = true;
                                return taskFrameEle;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AppListXaml: TaskbarFrame fast path threw: {ex.Message}");
                }

                // Fallback: descendants search from Shell_TrayWnd (resilient to 24H2+ XAML reshuffles).
                try
                {
                    IUIAutomationElement rootEle = uia.ElementFromHandle(hwndTaskbarMain);
                    IUIAutomationCondition con = uia.CreatePropertyCondition(UIA_PropertyIds.UIA_AutomationIdPropertyId, "TaskbarFrame");
                    IUIAutomationElement? taskFrameEle = rootEle.FindFirst(Interop.UIAutomationClient.TreeScope.TreeScope_Descendants, con);

                    Marshal.ReleaseComObject(con);
                    Marshal.ReleaseComObject(rootEle);
                    if (taskFrameEle != null)
                    {
                        AppListXaml.appListXamlAlreadyExists = true;
                        return taskFrameEle;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AppListXaml: TaskbarFrame descendants fallback threw: {ex.Message}");
                }

                System.Diagnostics.Debug.WriteLine("AppListXaml: TaskbarFrame not found via fast path or descendants fallback");
                return null;
            }


            public void ReloadTaskbarFrameElement()
            {
                // When the taskbar is restarted, XAML may not exist yet when the handle appears.
                // If XAML was seen before, treat as restart and retry acquisition.
                if (_uia == null)
                {
                    return;
                }
                if (!LocalPInvoke.IsWindow(_hwndTaskbarMain))
                {
                    return;
                }
                if (_taskbarFrame != null)
                {
                    Marshal.ReleaseComObject(_taskbarFrame);
                    _taskbarFrame = null;
                }
                _taskbarFrame = GetTaskbarFrameElement(_hwndTaskbarMain, _uia);
            }

            public LocalPInvoke.RECT? GetWindowRect()
            {
                if (_taskbarFrame == null || _uia == null)
                {
                    return null;
                }
                if (!LocalPInvoke.IsWindow(_hwndTaskbarMain))
                {
                    return null;
                }

                IUIAutomationElementArray? children = null;
                IUIAutomationElement? child = null;
                try
                {
                    children = _taskbarFrame.FindAll(
                        Interop.UIAutomationClient.TreeScope.TreeScope_Children,
                        _uia.CreateTrueCondition());
                    tagRECT? leftRect = null;
                    tagRECT? rightRect = null;
                    int len = children.Length;
                    if (len == 0)
                    {
                        return null;
                    }

                    for (int i = 0; i < len; i++)
                    {
                        child = children.GetElement(i);
                        tagRECT r = child.CurrentBoundingRectangle;
                        if (leftRect == null || r.left < leftRect.Value.left)
                        {
                            leftRect = r;
                        }
                        if (rightRect == null || rightRect.Value.right < r.right)
                        {
                            rightRect = r;
                        }
                        Marshal.ReleaseComObject(child);
                        child = null;
                    }
                    if (leftRect == null || rightRect == null)
                    {
                        return null;
                    }

                    LocalPInvoke.RECT rect = new()
                    {
                        Left = (int)leftRect.Value.left,
                        Top = (int)leftRect.Value.top,
                        Right = (int)rightRect.Value.right,
                        Bottom = (int)leftRect.Value.bottom,
                    };
                    return rect;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AppListXaml.GetWindowRect failed: {ex.Message}");
                    // Drop stale frame so ReloadRequired can re-acquire after Explorer restart.
                    if (_taskbarFrame != null)
                    {
                        try { Marshal.ReleaseComObject(_taskbarFrame); } catch { }
                        _taskbarFrame = null;
                    }
                    return null;
                }
                finally
                {
                    if (child != null)
                    {
                        Marshal.ReleaseComObject(child);
                    }
                    if (children != null)
                    {
                        Marshal.ReleaseComObject(children);
                    }
                }
            }

            public void Dispose()
            {
                if (_taskbarFrame != null)
                {
                    Marshal.ReleaseComObject(_taskbarFrame);
                    _taskbarFrame = null;
                }
                if (_uia != null)
                {
                    Marshal.ReleaseComObject(_uia);
                    _uia = null;
                }
            }
        }
#nullable restore

        public class Settings
        {
            public int Version { get; set; }
            public SegmentSettings SimpleTaskbarLayout { get; set; }
            public SegmentSettings DynamicAppListLayout { get; set; }
            public SegmentSettings DynamicTrayLayout { get; set; }
            public SegmentSettings DynamicWidgetsLayout { get; set; }
            public SegmentSettings DynamicSecondaryClockLayout { get; set; }
            public int WidgetsWidth { get; set; }
            public int ClockWidth { get; set; }
            public bool IsDynamic { get; set; }
            public bool IsCentred { get; set; }
            public bool IsWindows11 { get; set; }
            public bool ShowTray { get; set; }
            public bool ShowWidgets { get; set; }
            public bool ShowSecondaryClock { get; set; }
            public bool CompositionCompat { get; set; }
            public bool IsNotFirstLaunch { get; set; }
            public bool FillOnMaximise { get; set; }
            public bool FillOnTaskSwitch { get; set; }
            public bool ShowSegmentsOnHover { get; set; }
            public int AutoHide { get; set; }

            /// <summary>Deep copy so worker hover overrides never mutate UI-bound settings.</summary>
            public Settings Clone()
            {
                return new Settings
                {
                    Version = Version,
                    SimpleTaskbarLayout = SimpleTaskbarLayout?.Clone() ?? new SegmentSettings(),
                    DynamicAppListLayout = DynamicAppListLayout?.Clone() ?? new SegmentSettings(),
                    DynamicTrayLayout = DynamicTrayLayout?.Clone() ?? new SegmentSettings(),
                    DynamicWidgetsLayout = DynamicWidgetsLayout?.Clone() ?? new SegmentSettings(),
                    DynamicSecondaryClockLayout = DynamicSecondaryClockLayout?.Clone() ?? new SegmentSettings(),
                    WidgetsWidth = WidgetsWidth,
                    ClockWidth = ClockWidth,
                    IsDynamic = IsDynamic,
                    IsCentred = IsCentred,
                    IsWindows11 = IsWindows11,
                    ShowTray = ShowTray,
                    ShowWidgets = ShowWidgets,
                    ShowSecondaryClock = ShowSecondaryClock,
                    CompositionCompat = CompositionCompat,
                    IsNotFirstLaunch = IsNotFirstLaunch,
                    FillOnMaximise = FillOnMaximise,
                    FillOnTaskSwitch = FillOnTaskSwitch,
                    ShowSegmentsOnHover = ShowSegmentsOnHover,
                    AutoHide = AutoHide
                };
            }
        }

        public class EffectiveRegion
        {
            public int CornerRadius { get; set; }
            public int Top { get; set; }
            public int Left { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
        }

        public class SegmentSettings
        {
            public int CornerRadius { get; set; }
            public int MarginTop { get; set; }
            public int MarginLeft { get; set; }
            public int MarginBottom { get; set; }
            public int MarginRight { get; set; }

            public SegmentSettings Clone()
            {
                return new SegmentSettings
                {
                    CornerRadius = CornerRadius,
                    MarginTop = MarginTop,
                    MarginLeft = MarginLeft,
                    MarginBottom = MarginBottom,
                    MarginRight = MarginRight
                };
            }
        }

        public enum TrayMode
        {
            Show = 0,
            Hide = 1,
            AutoHide = 2,
        }

        public enum CompositionMode
        {
            None = 0,
            TranslucentTB = 1,
            Legacy = 2,
        }

        public enum KeyModifier
        {
            None = 0,
            Alt = 1,
            Control = 2,
            Shift = 4,
            WinKey = 8
        }
    }
}
