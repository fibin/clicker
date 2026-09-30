using System;
using System.Threading;
using System.Windows;
using Clicker.Localization;
using Clicker.Models;
using Clicker.Services;

namespace Clicker;

public partial class App : Application
{
    /// <summary>Passed when the app restarts itself as administrator.</summary>
    public const string ElevatedRestartArg = "--elevated-restart";

    private const string MutexName = @"Local\Clicker_SingleInstance_B7E2";
    private const string ShowEventName = @"Local\Clicker_Show_B7E2";

    private Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppSettings settings = SettingsStore.Load();
        Loc.Instance.SetLanguage(string.IsNullOrEmpty(settings.Language)
            ? Loc.DetectSystemLanguage()
            : settings.Language);

        bool elevatedRestart = Array.IndexOf(e.Args, ElevatedRestartArg) >= 0;

        // Only one instance: two copies would both react to the hotkey and cancel each other out.
        if (!AcquireSingleInstance(waitForPrevious: elevatedRestart))
        {
            ActivateExistingInstance();
            Shutdown();
            return;
        }

        var window = new MainWindow(settings);
        MainWindow = window;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        window.Show();

        ListenForSecondInstance(window);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* already released */ }
        }

        _mutex?.Dispose();
        _showEvent?.Dispose();
        base.OnExit(e);
    }

    private bool AcquireSingleInstance(bool waitForPrevious)
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            if (createdNew)
                return _ownsMutex = true;

            if (waitForPrevious)
            {
                // The old (non-admin) instance is shutting down after starting us.
                try
                {
                    if (_mutex.WaitOne(TimeSpan.FromSeconds(10)))
                        return _ownsMutex = true;
                }
                catch (AbandonedMutexException)
                {
                    return _ownsMutex = true;
                }
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated instance owns the mutex.
            return false;
        }
    }

    private static void ActivateExistingInstance()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch
        {
            MessageBox.Show(Loc.Instance["AlreadyRunning"], Loc.Instance["AppTitle"],
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    /// <summary>When the user starts the exe a second time, bring the existing window to front.</summary>
    private void ListenForSecondInstance(MainWindow window)
    {
        try
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        }
        catch
        {
            return;
        }

        EventWaitHandle showEvent = _showEvent;
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    showEvent.WaitOne();
                }
                catch
                {
                    return; // disposed on exit
                }

                Dispatcher.BeginInvoke(new Action(window.ShowFromTray));
            }
        })
        {
            IsBackground = true,
            Name = "SingleInstance",
        };
        thread.Start();
    }
}
