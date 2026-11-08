using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls.ApplicationLifetimes;
using AthanTime.Net.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AthanTime.Net.Services;

namespace AthanTime.Net;

/// <summary>Main desktop window with device-local position restoration and UI-only event handlers.</summary>
public partial class MainWindow : Window
{
    private ILogger<MainWindow> _logger = NullLogger<MainWindow>.Instance;
    private bool _exitDialogOpen;
    private SupportService? _support;
    private FileLogSink? _fileLogs;
    private bool _emailBusy;
    private bool _settingsTransferBusy;
    private readonly WindowPositionService? _windowPositions;
    private WindowPosition? _normalPosition;
    private WindowPosition? _savedPosition;
    private bool _positionOpened;
    private bool _positionCapturePending;

    /// <summary>Creates the parameterless instance required by the Avalonia XAML loader.</summary>
    public MainWindow()
    {
        InitializeComponent();
        Icon = App.CreateDesktopIcon();
    }

    /// <summary>Creates the main window from dependencies supplied by the host.</summary>
    public MainWindow(MainViewModel viewModel, ILogger<MainWindow> logger) : this()
    {
        ViewModel = viewModel;
        _logger = logger;
        DataContext = ViewModel;
    }

    /// <summary>Creates a window with device-local position restoration and centered first-run fallback.</summary>
    public MainWindow(MainViewModel viewModel, ILogger<MainWindow> logger, WindowPositionService positions) : this(viewModel, logger)
    {
        _windowPositions = positions;
        _savedPosition = positions.Load();
        if (_savedPosition is { } saved && VisiblePosition(new PixelPoint(saved.X, saved.Y)) is { } visible)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = visible;
        }
        PositionChanged += (_, _) => QueuePositionCapture();
        Deactivated += (_, _) => SaveWindowPosition();
        ScalingChanged += (_, _) => { if (IsVisible) KeepPositionOnScreen(); };
        Screens.Changed += OnScreensChanged;
    }

    /// <summary>Creates the runtime window with device-local placement, log export and email draft services.</summary>
    public MainWindow(MainViewModel viewModel, ILogger<MainWindow> logger, SupportService support,
        FileLogSink fileLogs, WindowPositionService positions) : this(viewModel, logger, positions)
    {
        _support = support;
        _fileLogs = fileLogs;
    }

    /// <summary>Gets the dependency-injected main view model.</summary>
    public MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>Gets or sets whether the next close should terminate instead of hiding.</summary>
    public bool ForceClose { get; set; }

    /// <summary>Only hide completely when the desktop can expose a tray icon.</summary>
    public bool CanHideToTray { get; set; }

    /// <summary>Applies minimized startup while preserving a taskbar route when the tray cannot hide the window.</summary>
    public void PrepareStartupVisibility(bool startMinimized)
    {
        if (!startMinimized) return;
        WindowState = WindowState.Minimized;
        if (CanHideToTray && ViewModel.CloseToTray) Opened += HideAfterStartup;
    }

    private void HideAfterStartup(object? sender, EventArgs args)
    {
        // Opened also fires after a tray Show; consume startup hiding before the first Hide.
        Opened -= HideAfterStartup;
        Hide();
    }

    /// <summary>Shows, restores and activates the existing window for tray or repeat-launch requests.</summary>
    public void ShowFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        KeepPositionOnScreen();
        Activate();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs args)
    {
        _positionOpened = true;
        KeepPositionOnScreen();
        CaptureNormalPosition();
        base.OnOpened(args);
    }

    /// <inheritdoc />
    public override void Hide()
    {
        SaveWindowPosition();
        base.Hide();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs args)
    {
        SaveWindowPosition();
        if (_windowPositions is not null) Screens.Changed -= OnScreensChanged;
        base.OnClosed(args);
    }

    private void OnScreensChanged(object? sender, EventArgs args)
    {
        if (IsVisible) KeepPositionOnScreen();
    }

    private void KeepPositionOnScreen()
    {
        if (_windowPositions is null || WindowState != WindowState.Normal) return;
        var requested = Position;
        if (_normalPosition is { } normal && !Screens.All.Any(screen => screen.WorkingArea.Contains(requested)))
            requested = new PixelPoint(normal.X, normal.Y);
        if (VisiblePosition(requested) is { } visible && visible != Position) Position = visible;
    }

    private PixelPoint? VisiblePosition(PixelPoint requested)
    {
        var frame = FrameSize is { Width: > 0, Height: > 0 } size ? size : new Size(Width, Height);
        return WindowPositionService.Constrain(requested, PixelSize.FromSize(frame, DesktopScaling),
            Screens.All.OrderByDescending(screen => screen.IsPrimary).Select(screen => screen.WorkingArea).ToArray());
    }

    private void QueuePositionCapture()
    {
        if (_positionCapturePending) return;
        _positionCapturePending = true;
        // Native move/state events can arrive together. Capture after a maximize/minimize transition settles.
        Dispatcher.UIThread.Post(() =>
        {
            _positionCapturePending = false;
            CaptureNormalPosition();
        }, DispatcherPriority.Background);
    }

    private void CaptureNormalPosition()
    {
        // Ignore startup layout and native minimized/maximized coordinates, including Windows' hidden sentinel.
        if (_windowPositions is null || !_positionOpened || !IsVisible || WindowState != WindowState.Normal
            || !Screens.All.Any(screen => screen.WorkingArea.Contains(Position))) return;
        _normalPosition = new WindowPosition(Position.X, Position.Y);
    }

    private void SaveWindowPosition()
    {
        CaptureNormalPosition();
        if (_normalPosition is { } normal && normal != _savedPosition && _windowPositions!.Save(normal))
            _savedPosition = normal;
    }

    private async void OnExit(object? sender, RoutedEventArgs args) => await RequestExitAsync();

    /// <summary>Asks before an explicit exit; a normal close-to-tray action does not exit.</summary>
    public async Task RequestExitAsync()
    {
        if (_exitDialogOpen) return;
        _exitDialogOpen = true;
        try
        {
            if (!IsVisible) Show();
            Activate();
            var message = new TextBlock { Text = ViewModel["ExitConfirmMessage"], TextWrapping = TextWrapping.Wrap };
            var confirm = new Button { Content = ViewModel.TrayExitLabel, MinWidth = 110 };
            var cancel = new Button { Content = ViewModel["Cancel"], MinWidth = 110 };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12,
                HorizontalAlignment = HorizontalAlignment.Right };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            var content = new StackPanel { Spacing = 22 };
            content.Children.Add(message);
            content.Children.Add(buttons);
            var dialog = new AppWindow(ViewModel)
            {
                Title = ViewModel["ExitConfirmTitle"], Width = 440, Height = 280, MinWidth = 360,
                CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            dialog.SetBody(content);
            cancel.Click += (_, _) => dialog.Close(false);
            confirm.Click += (_, _) => dialog.Close(true);
            if (!await dialog.ShowDialog<bool>(this)) return;
        }
        finally { _exitDialogOpen = false; }
        ForceClose = true;
        _logger.LogInformation("Exit confirmed by user");
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
        else Close();
    }

    private void OnAddLocation(object? sender, RoutedEventArgs args) => ViewModel.AddManualLocation();
    private void OnApplySummerTime(object? sender, RoutedEventArgs args) => ViewModel.ApplySummerTimeChanges();
    private void OnCancelSummerTime(object? sender, RoutedEventArgs args) => ViewModel.CancelSummerTimeChanges();
    private void OnRestoreAutomaticSummerTime(object? sender, RoutedEventArgs args) => ViewModel.RestoreAutomaticSummerTime();
    private void OnRemoveLocation(object? sender, RoutedEventArgs args) => ViewModel.RemoveSavedLocation();
    private void OnRemoveAdhkar(object? sender, RoutedEventArgs args) => ViewModel.RemoveAdhkarFile();
    private void OnMoveAdhkarUp(object? sender, RoutedEventArgs args) => ViewModel.MoveAdhkarUp();
    private void OnAddAdhkarText(object? sender, RoutedEventArgs args) => ViewModel.AddAdhkarText();
    private void OnUpdateAdhkarText(object? sender, RoutedEventArgs args) => ViewModel.UpdateAdhkarText();
    private void OnRemoveAdhkarText(object? sender, RoutedEventArgs args) => ViewModel.RemoveAdhkarText();
    private async void OnStopAudio(object? sender, RoutedEventArgs args) => await ViewModel.StopAudioAsync();
    private async void OnPlayBundledAthan(object? sender, RoutedEventArgs args) =>
        await ViewModel.PreviewAudioAsync("bundled:adhan");
    private async void OnPreviewAdhkar(object? sender, RoutedEventArgs args) => await ViewModel.PreviewAudioAsync(ViewModel.SelectedAdhkarFile);
    private async void OnPreviewPrayerAudio(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PrayerNotificationRow row })
            await ViewModel.PreviewAudioAsync(string.IsNullOrWhiteSpace(row.AudioPath) ? "bundled:adhan" : row.AudioPath);
    }
    private void OnClearPrayerAudio(object? sender, RoutedEventArgs args)
    {
        if (sender is Button { DataContext: PrayerNotificationRow row }) { row.AudioPath = ""; row.AudioEnabled = true; }
    }
    private async void OnChoosePrayerAudio(object? sender, RoutedEventArgs args)
    {
        if (sender is not Button { DataContext: PrayerNotificationRow row }) return;
        var paths = await PickAudioAsync(false);
        if (paths.Count > 0) ViewModel.SetPrayerAudio(row, paths[0]);
    }
    private async void OnAddAdhkarFiles(object? sender, RoutedEventArgs args) => ViewModel.AddAdhkarFiles(await PickAudioAsync(true));

    private async Task<IReadOnlyList<string>> PickAudioAsync(bool multiple)
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = ViewModel["AudioPicker"], AllowMultiple = multiple,
                FileTypeFilter = [new FilePickerFileType("Ogg Vorbis") { Patterns = ["*.ogg", "*.OGG"],
                    MimeTypes = ["audio/ogg", "application/ogg"], AppleUniformTypeIdentifiers = ["org.xiph.ogg-audio"] }]
            });
            var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
            foreach (var file in files) file.Dispose();
            return paths;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "The native audio picker failed");
            await ShowInformationAsync(ViewModel["Audio"], ViewModel["AudioFileError"]);
            return [];
        }
    }

    /// <summary>Selects the location represented by the clicked search result.</summary>
    private void OnLocationClicked(object? sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { DataContext: LocationResult result })
            ViewModel.SelectLocation(result);
    }

    /// <summary>Moves the displayed schedule to the previous day.</summary>
    private void OnPreviousDay(object? sender, RoutedEventArgs eventArgs) => ViewModel.PreviousDay();

    /// <summary>Moves the displayed schedule to the next day.</summary>
    private void OnNextDay(object? sender, RoutedEventArgs eventArgs) => ViewModel.NextDay();

    /// <summary>Returns the displayed schedule to the location's current day.</summary>
    private void OnToday(object? sender, RoutedEventArgs eventArgs) => ViewModel.Today();

    /// <summary>Pauses or resumes all automatic reminders from the settings panel.</summary>
    private void OnToggleNotificationsPause(object? sender, RoutedEventArgs eventArgs) => ViewModel.ToggleNotificationsPause();

    /// <summary>Shows concise localized usage guidance.</summary>
    private async void OnHelp(object? sender, RoutedEventArgs eventArgs) =>
        await ShowInformationAsync(ViewModel.HelpLabel, ViewModel.HelpText);

    /// <summary>Shows application and version information.</summary>
    private async void OnAbout(object? sender, RoutedEventArgs eventArgs) =>
        await ShowInformationAsync(ViewModel.AboutLabel, ViewModel.AboutText);

    private async void OnSuggestion(object? sender, RoutedEventArgs args) => await OpenEmailAsync(SupportMessage.Suggestion);
    private async void OnComplaint(object? sender, RoutedEventArgs args) => await OpenEmailAsync(SupportMessage.Complaint);

    private async void OnExportSettings(object? sender, RoutedEventArgs args)
    {
        if (_settingsTransferBusy) return;
        _settingsTransferBusy = true;
        try
        {
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = ViewModel["ExportSettings"], SuggestedFileName = "AthanTime-settings.json", DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }], ShowOverwritePrompt = true
            });
            if (file is null) return;
            var path = file.TryGetLocalPath() ?? throw new IOException("A local settings file is required.");
            var exported = ViewModel.ExportSettings(path);
            await ShowInformationAsync(ViewModel["ExportSettings"], ViewModel[exported ? "SettingsExported" : "SettingsExportFailed"]);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Settings export picker failed");
            await ShowInformationAsync(ViewModel["ExportSettings"], ViewModel["SettingsExportFailed"]);
        }
        finally { _settingsTransferBusy = false; }
    }

    private async void OnImportSettings(object? sender, RoutedEventArgs args)
    {
        if (_settingsTransferBusy) return;
        _settingsTransferBusy = true;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = ViewModel["ImportSettings"], AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json", "*.JSON"] }]
            });
            if (files.Count == 0) return;
            string? path;
            try { path = files[0].TryGetLocalPath(); }
            finally { foreach (var file in files) file.Dispose(); }
            if (path is null) throw new IOException("A local settings file is required.");
            var imported = ViewModel.ReadSettingsImport(path);
            if (imported is null)
            {
                await ShowInformationAsync(ViewModel["ImportSettings"], ViewModel["SettingsImportFailed"]);
                return;
            }
            if (!await ConfirmImportAsync()) return;
            ViewModel.ApplyImportedSettings(imported);
            await ShowInformationAsync(ViewModel["ImportSettings"], ViewModel.SettingsStatus);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Settings import failed");
            await ShowInformationAsync(ViewModel["ImportSettings"], ViewModel["SettingsImportFailed"]);
        }
        finally { _settingsTransferBusy = false; }
    }

    private Task<bool> ConfirmImportAsync()
    {
        var dialog = new AppWindow(ViewModel)
        {
            Title = ViewModel["ImportSettings"], Width = 520, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var cancel = new Button { Content = ViewModel["Cancel"], MinWidth = 110 };
        var apply = new Button { Content = ViewModel["ImportSettings"], MinWidth = 110 };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Margin = new Thickness(6); apply.Margin = new Thickness(6);
        buttons.Children.Add(cancel); buttons.Children.Add(apply);
        var panel = new StackPanel { Spacing = 22 };
        panel.Children.Add(new TextBlock { Text = ViewModel["SettingsImportConfirm"], TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(buttons);
        dialog.SetBody(panel);
        cancel.Click += (_, _) => dialog.Close(false);
        apply.Click += (_, _) => dialog.Close(true);
        return dialog.ShowDialog<bool>(this);
    }

    // mailto has no standard attachment field: export locally, then let the user review and attach the ZIP.
    private async void OnEmailLogs(object? sender, RoutedEventArgs args)
    {
        if (_emailBusy || _fileLogs is null) return;
        if (!_fileLogs.Enabled)
        {
            await ShowInformationAsync(ViewModel["EmailLogs"], ViewModel["NoLogs"]);
            return;
        }
        _emailBusy = true;
        try
        {
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = ViewModel["ExportLogs"], SuggestedFileName = "AthanTime-logs.zip", DefaultExtension = "zip",
                FileTypeChoices = [new FilePickerFileType("ZIP") { Patterns = ["*.zip"] }], ShowOverwritePrompt = true
            });
            if (file is null) return;
            bool exported;
            await using (var stream = await file.OpenWriteAsync())
            {
                stream.SetLength(0);
                exported = await Task.Run(() => _fileLogs.Export(stream));
            }
            if (!exported)
            {
                await ShowInformationAsync(ViewModel["EmailLogs"], ViewModel["NoLogs"]);
                return;
            }
            _logger.LogInformation("User exported diagnostic logs");
            await ShowInformationAsync(ViewModel["EmailLogs"], string.Format(
                System.Globalization.CultureInfo.CurrentCulture, ViewModel["AttachLogs"], file.Name));
            await OpenEmailAsync(SupportMessage.DebugLogs, file.Name);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Diagnostic log export failed");
            await ShowInformationAsync(ViewModel["EmailLogs"], ViewModel["ExportLogsError"]);
        }
        finally { _emailBusy = false; }
    }

    private async Task OpenEmailAsync(SupportMessage message, string? attachmentName = null)
    {
        if (_support is null) return;
        try
        {
            var draft = _support.CreateDraft(message, ViewModel.SelectedLanguage.Code, attachmentName);
            if (!await Launcher.LaunchUriAsync(draft)) throw new InvalidOperationException("No mailto handler accepted the draft.");
            _logger.LogInformation("Opened user email draft for {MessageKind}", message);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not open the user's email application");
            await ShowInformationAsync(ViewModel["Feedback"], ViewModel["EmailError"]);
        }
    }

    private async Task ShowInformationAsync(string title, string message)
    {
        var close = new Button
        {
            Content = ViewModel.OkLabel,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 22, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(close);
        var dialog = new AppWindow(ViewModel)
        {
            Title = title,
            Icon = Icon,
            Width = 520,
            Height = 500,
            MinWidth = 360,
            MinHeight = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        dialog.SetBody(panel);
        close.Click += (_, _) => dialog.Close();
        _logger.LogDebug("Showing {DialogTitle} dialog", title);
        await dialog.ShowDialog(this);
    }

    /// <summary>Hides the window instead of exiting when close-to-tray is enabled.</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        SaveWindowPosition();
        if (ViewModel is null) return;
        if (!ForceClose && ViewModel.CloseToTray)
        {
            eventArgs.Cancel = true;
            if (CanHideToTray) Hide();
            else WindowState = WindowState.Minimized;
            _logger.LogDebug("Main window hidden or minimized; tray available: {TrayAvailable}", CanHideToTray);
        }
        else if (!ForceClose && Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            eventArgs.Cancel = true;
            _ = RequestExitAsync();
        }
    }
}
