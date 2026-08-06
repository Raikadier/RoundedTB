using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace RoundedTB
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            SessionEnding += App_SessionEnding;

            Interaction.WriteCrashLog("App.OnStartup");
            WPFUI.Theme.Watcher.Start();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            TryRestoreTaskbars("App.OnExit");
            Interaction.WriteCrashLog($"App.OnExit code={e.ApplicationExitCode}");
            base.OnExit(e);
        }

        private void App_SessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            TryRestoreTaskbars($"SessionEnding ({e.ReasonSessionEnding})");
        }

        private static void TryRestoreTaskbars(string reason)
        {
            try
            {
                if (Current?.MainWindow is MainWindow mw)
                {
                    mw.RestoreAllTaskbars(reason);
                }
            }
            catch (Exception ex)
            {
                Interaction.WriteCrashLog($"TryRestoreTaskbars failed: {ex}");
            }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Interaction.WriteCrashLog($"DispatcherUnhandledException: {e.Exception}");
            TryRestoreTaskbars("DispatcherUnhandledException");
            // Keep running so we can inspect; mark handled to avoid silent process death when possible.
            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Interaction.WriteCrashLog($"AppDomain.UnhandledException (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
            if (e.IsTerminating)
            {
                TryRestoreTaskbars("AppDomain.UnhandledException terminating");
            }
        }

        private void TaskScheduler_UnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            Interaction.WriteCrashLog($"UnobservedTaskException: {e.Exception}");
            e.SetObserved();
        }
    }
}
