using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using System.Reflection;
using AthanTime.Net.ViewModels;
using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace AthanTime.Net;

/// <summary>Owns desktop/tray lifetime, main-window activation and scheduled reminder windows.</summary>
public partial class App : Application
{
    /// <summary>Gets the shared semantic release version, including prerelease labels but excluding source-revision metadata.</summary>
    public static string ReleaseVersion { get; } = typeof(App).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];

    /// <summary>Gets whether this build belongs to the beta release channel.</summary>
    public static bool IsBeta { get; } = ReleaseVersion.Split('-', 2) is [_, var label]
        && label.Split('.')[0].Equals("beta", StringComparison.OrdinalIgnoreCase);

    private Func<MainWindow> _createWindow = null!;
    private ILogger<App> _logger = NullLogger<App>.Instance;
    private TrayIcon? _trayIcon;
    private MainWindow? _window;
    private Window? _notification;
    private TextAdhkarWindow? _textAdhkarWindow;
    private readonly SingleInstanceGuard? _singleInstance;
    private bool _exiting;
    private IActivatableLifetime? _activationLifetime;
    private bool _mainWindowOpened;
    private bool _reopenPending;

    /// <summary>Creates the parameterless instance required by the Avalonia XAML loader.</summary>
    public App()
    {
    }

    /// <summary>Creates the runtime application from dependencies supplied by the host.</summary>
    public App(Func<MainWindow> createWindow, ILogger<App> logger, SingleInstanceGuard? singleInstance = null)
    {
        _createWindow = createWindow;
        _logger = logger;
        _singleInstance = singleInstance;
    }

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _activationLifetime ??= this.TryGetFeature<IActivatableLifetime>();
            if (_activationLifetime is not null) _activationLifetime.Activated += OnApplicationActivated;
            _window = _createWindow();
            _window.ViewModel.PrayerDue += (_, alert) => ShowPrayerAlert(alert);
            _window.ViewModel.TextAdhkarChanged += (_, text) => ShowTextAdhkar(text);
            _window.ViewModel.NotificationsPausedChanged += (_, _) => { _notification?.Close(); _textAdhkarWindow?.Close(); };
            desktop.MainWindow = _window;
            CreateTrayIcon();
            _window.PrepareStartupVisibility(_window.ViewModel.StartMinimized || Environment.GetCommandLineArgs().Contains(
                "--start-minimized", StringComparer.OrdinalIgnoreCase));
            _window.Opened += OnMainWindowOpened;
            _window.ViewModel.Start();
            desktop.Exit += (_, _) =>
            {
                _exiting = true;
                if (_activationLifetime is not null) _activationLifetime.Activated -= OnApplicationActivated;
                _singleInstance?.StopActivationListener();
                _trayIcon?.Dispose();
                _window.ViewModel.Dispose();
                _textAdhkarWindow?.Close();
                _notification?.Close();
                _logger.LogInformation("Desktop lifetime exited");
            };
            _logger.LogInformation("Desktop lifetime initialized");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Creates the native tray icon with pause and independent reminder controls.</summary>
    private void CreateTrayIcon()
    {
        if (_window is null)
            return;

        var show = new NativeMenuItem(_window.ViewModel.TrayShowLabel);
        show.Click += (_, _) => ShowWindow();
        var exit = new NativeMenuItem(_window.ViewModel.TrayExitLabel);
        exit.Click += async (_, _) =>
        {
            _logger.LogInformation("Exit requested from tray menu");
            ShowWindow();
            await _window.RequestExitAsync();
        };

        var menu = new NativeMenu();
        menu.Add(show);
        var pause = new NativeMenuItem();
        pause.Click += (_, _) => _window.ViewModel.ToggleNotificationsPause();
        menu.Add(pause);
        var prayerAudio = new NativeMenuItem();
        prayerAudio.Click += async (_, _) =>
        {
            _window.ViewModel.PrayerAudioEnabled = !_window.ViewModel.PrayerAudioEnabled;
            if (!_window.ViewModel.PrayerAudioEnabled) await _window.ViewModel.StopAudioAsync();
        };
        menu.Add(prayerAudio);
        var adhkarAudio = new NativeMenuItem();
        adhkarAudio.Click += async (_, _) =>
        {
            _window.ViewModel.AdhkarEnabled = !_window.ViewModel.AdhkarEnabled;
            if (!_window.ViewModel.AdhkarEnabled) await _window.ViewModel.StopAudioAsync();
        };
        menu.Add(adhkarAudio);
        var adhkarText = new NativeMenuItem();
        adhkarText.Click += (_, _) => _window.ViewModel.TextAdhkarEnabled = !_window.ViewModel.TextAdhkarEnabled;
        menu.Add(adhkarText);
        menu.Add(new NativeMenuItemSeparator());
        var playAthan = new NativeMenuItem();
        playAthan.Click += async (_, _) => await _window.ViewModel.PreviewAudioAsync("bundled:adhan");
        menu.Add(playAthan);
        var playTakbeer = new NativeMenuItem();
        playTakbeer.Click += async (_, _) => await _window.ViewModel.PreviewAudioAsync("bundled:takbeer");
        menu.Add(playTakbeer);
        var stop = new NativeMenuItem(_window.ViewModel["StopAudio"]);
        stop.Click += async (_, _) => await _window.ViewModel.StopAudioAsync();
        menu.Add(stop);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        void RefreshMenu()
        {
            pause.Header = _window.ViewModel.PauseButtonLabel;
            prayerAudio.Header = _window.ViewModel[_window.ViewModel.PrayerAudioEnabled ? "StopPrayerAudio" : "StartPrayerAudio"];
            adhkarAudio.Header = _window.ViewModel[_window.ViewModel.AdhkarEnabled ? "StopAdhkarAudio" : "StartAdhkarAudio"];
            adhkarText.Header = _window.ViewModel[_window.ViewModel.TextAdhkarEnabled ? "StopTextAdhkar" : "StartTextAdhkar"];
            playAthan.Header = _window.ViewModel["PlayBundledAthan"];
            playTakbeer.Header = _window.ViewModel["PlayBundledTakbeer"];
            show.Header = _window.ViewModel.TrayShowLabel;
            exit.Header = _window.ViewModel.TrayExitLabel;
            stop.Header = _window.ViewModel["StopAudio"];
        }
        RefreshMenu();
        _window.ViewModel.PropertyChanged += (_, change) =>
        {
            // Countdown changes every second; menu labels change only with these preferences.
            if (string.IsNullOrEmpty(change.PropertyName) || change.PropertyName is
                nameof(MainViewModel.PauseButtonLabel) or nameof(MainViewModel.SelectedLanguage) or
                nameof(MainViewModel.PrayerAudioEnabled) or nameof(MainViewModel.AdhkarEnabled) or
                nameof(MainViewModel.TextAdhkarEnabled)) RefreshMenu();
        };

        _trayIcon = new TrayIcon
        {
            Icon = CreateDesktopIcon(),
            ToolTipText = _window.ViewModel.AppName,
            Menu = menu,
            IsVisible = true
        };
        _trayIcon.Clicked += (_, _) => ShowWindow();
        _window.ViewModel.TrayTextChanged += (_, value) =>
        {
            _trayIcon.ToolTipText = value;
        };
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
        // Linux desktops can omit the status-notifier host; keep a taskbar route back to the app.
        _window.CanHideToTray = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();
    }

    /// <summary>Loads DPI-specific Windows ICO frames or the compact macOS/Linux desktop PNG.</summary>
    public static WindowIcon CreateDesktopIcon()
    {
        // A large PNG takes the Windows bitmap fallback and loses the ICO's DPI-specific frames.
        string name = OperatingSystem.IsWindows() ? "athan-icon.ico" : "athan-icon-desktop.png";
        using var stream = AssetLoader.Open(new Uri($"avares://AthanTime.Net/Assets/{name}"));
        return new WindowIcon(stream);
    }

    /// <summary>Connects duplicate-launch activation after the initial window opening/startup hiding.</summary>
    private void OnMainWindowOpened(object? sender, EventArgs args)
    {
        _window!.Opened -= OnMainWindowOpened;
        _mainWindowOpened = true;
        // Register after the first Opened/start-minimized handlers; an early duplicate is coalesced until now.
        _singleInstance?.RegisterActivationHandler(QueueWindowActivation,
            exception => _logger.LogWarning(exception, "The single-instance activation channel is unavailable"));
        if (_reopenPending)
        {
            _reopenPending = false;
            QueueWindowActivation();
        }
    }

    private void OnApplicationActivated(object? sender, ActivatedEventArgs args) => HandleApplicationActivation(args.Kind);

    /// <summary>Handles shell reopen requests, including those received before the first window opening; other activation kinds are ignored.</summary>
    public void HandleApplicationActivation(ActivationKind kind)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => HandleApplicationActivation(kind));
            return;
        }
        if (_exiting || kind != ActivationKind.Reopen) return;
        if (!_mainWindowOpened) { _reopenPending = true; return; }
        QueueWindowActivation();
    }

    private void QueueWindowActivation() => Dispatcher.UIThread.Post(() =>
        {
            if (_exiting) return;
            _logger.LogInformation("Restoring the main window for an application activation request");
            try
            {
                _activationLifetime?.TryLeaveBackground();
                ShowWindow();
            }
            catch (Exception exception) { _logger.LogError(exception, "The main window could not be restored"); }
        });

    /// <summary>Restores and activates the main application window.</summary>
    private void ShowWindow()
    {
        if (_window is null || _exiting)
            return;
        _window.ShowFromTray();
        _logger.LogDebug("Main window shown");
    }

    /// <summary>Shows a localized prayer or sunrise reminder with the shared window appearance.</summary>
    private void ShowPrayerAlert(PrayerAlert alert)
    {
        if (_window is null) return;
        _window.ViewModel.DismissTextAdhkar();
        _notification?.Close();
        var notification = new PrayerNotificationWindow(_window.ViewModel, alert);
        _notification = notification;
        var closeTimer = DispatcherTimer.RunOnce(notification.Close, TimeSpan.FromSeconds(30));
        notification.Closed += (_, _) =>
        {
            closeTimer.Dispose();
            if (ReferenceEquals(_notification, notification)) _notification = null;
        };
        notification.Show();
        _logger.LogInformation("Displayed {Moment} reminder for {Prayer} in {Location}", alert.Moment, alert.PrayerName, alert.LocationName);
    }

    private void ShowTextAdhkar(string? text)
    {
        // Clear the reference first so a timer-driven close does not restart the pause twice.
        var previous = _textAdhkarWindow;
        _textAdhkarWindow = null;
        previous?.Close();
        if (text is null || _window is null) return;
        var popup = new TextAdhkarWindow(_window.ViewModel, text);
        _textAdhkarWindow = popup;
        popup.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_textAdhkarWindow, popup)) return;
            _textAdhkarWindow = null;
            _window.ViewModel.DismissTextAdhkar();
        };
        popup.Show();
        _logger.LogDebug("Displayed a text Adhkar reminder");
    }
}
