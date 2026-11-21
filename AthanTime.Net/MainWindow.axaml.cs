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

/// <summary>Main desktop window with device-local placement, work-area fitting and UI-only event handlers.</summary>
public partial class MainWindow : Window
{
    private ILogger<MainWindow> _logger = NullLogger<MainWindow>.Instance;
    private bool _exitDialogOpen;
    private SupportService? _support;
    private FileLogSink? _fileLogs;
    private bool _emailBusy;
    private bool _settingsTransferBusy;
    private readonly UserProfileCleanup? _profileCleanup;
    private readonly SingleInstanceGuard? _instance;
    private bool _profileReset;
    private readonly WindowPositionService? _windowPositions;
    private WindowPosition? _normalPosition;
    private WindowPosition? _savedPosition;
    private bool _positionOpened;
    private bool _positionCapturePending;
    private readonly Size _preferredMinimumSize;
    private bool _windowFitPending;
    private bool _fittingWindow;
    private bool _interactiveMoveResize;
    private const uint _wmEnterSizeMove = 0x0231;
    private const uint _wmExitSizeMove = 0x0232;

    /// <summary>Creates the parameterless instance required by the Avalonia XAML loader.</summary>
    public MainWindow()
    {
        InitializeComponent();
        Icon = App.CreateDesktopIcon();
        _preferredMinimumSize = new Size(MinWidth, MinHeight);
        PositionChanged += (_, _) => QueuePositionCapture();
        Deactivated += (_, _) => SaveWindowPosition();
        ScalingChanged += (_, _) => QueueWindowFit();
        Screens.Changed += OnScreensChanged;
        if (OperatingSystem.IsWindows()) Win32Properties.AddWndProcHookCallback(this, ObserveWindowsMoveResize);
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
    }

    /// <summary>Creates the runtime window with device-local placement, log export and email draft services.</summary>
    public MainWindow(MainViewModel viewModel, ILogger<MainWindow> logger, SupportService support,
        FileLogSink fileLogs, WindowPositionService positions, UserProfileCleanup profileCleanup,
        SingleInstanceGuard? instance = null) : this(viewModel, logger, positions)
    {
        _support = support;
        _fileLogs = fileLogs;
        _profileCleanup = profileCleanup;
        _instance = instance;
    }

    /// <summary>Gets the dependency-injected main view model.</summary>
    public MainViewModel ViewModel { get; private set; } = null!;

    /// <summary>Gets or sets whether the next close should terminate instead of hiding.</summary>
    public bool ForceClose { get; set; }

    /// <summary>Only hide completely when the desktop can expose a tray icon.</summary>
    public bool CanHideToTray { get; set; }

    /// <summary>Applies minimized startup unless setup requests a visible window, without changing saved preferences.</summary>
    public void PrepareStartupVisibility(bool startMinimized, bool showWindow = false)
    {
        startMinimized &= !showWindow;
        if (!startMinimized) return;
        if (CanHideToTray && ViewModel.CloseToTray)
        {
            WindowState = WindowState.Minimized;
            Opened += HideAfterStartup;
            return;
        }

        // X11/XWayland can ignore iconify before the window is shown. Keep its taskbar entry.
        ShowActivated = false;
        Opened += MinimizeAfterStartup;
    }

    private void MinimizeAfterStartup(object? sender, EventArgs args)
    {
        Opened -= MinimizeAfterStartup;
        WindowState = WindowState.Minimized;
        ShowActivated = true;
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
        FitWindowOnScreen();
        QueueWindowFit();
        Activate();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs args)
    {
        _positionOpened = true;
        // The base updates native frame size before fitting; Opened may also apply one-time startup hiding.
        base.OnOpened(args);
        FitWindowOnScreen();
        QueueWindowFit();
        CaptureNormalPosition();
    }

    /// <inheritdoc />
    public override void Hide()
    {
        SaveWindowPosition();
        _interactiveMoveResize = false;
        base.Hide();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs args)
    {
        SaveWindowPosition();
        _positionOpened = false;
        _interactiveMoveResize = false;
        Screens.Changed -= OnScreensChanged;
        if (OperatingSystem.IsWindows()) Win32Properties.RemoveWndProcHookCallback(this, ObserveWindowsMoveResize);
        base.OnClosed(args);
    }

    private IntPtr ObserveWindowsMoveResize(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Observe only: Windows must retain its native title-bar/resize loop and DPI handling.
        if (message == _wmEnterSizeMove) _interactiveMoveResize = true;
        else if (message == _wmExitSizeMove)
        {
            _interactiveMoveResize = false;
            QueueWindowFit();
        }
        return IntPtr.Zero;
    }

    private void OnScreensChanged(object? sender, EventArgs args)
    {
        QueueWindowFit();
    }

    /// <inheritdoc />
    protected override void OnResized(WindowResizedEventArgs args)
    {
        base.OnResized(args);
        QueueWindowFit();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty) QueueWindowFit();
    }

    private void QueueWindowFit()
    {
        if (!_positionOpened || !IsVisible || _windowFitPending) return;
        _windowFitPending = true;
        // DPI, state and native resize callbacks can describe intermediate geometry. Fit after they settle.
        Dispatcher.UIThread.Post(() =>
        {
            _windowFitPending = false;
            FitWindowOnScreen();
            CaptureNormalPosition();
        }, DispatcherPriority.Background);
    }

    private void FitWindowOnScreen()
    {
        if (!_positionOpened || !IsVisible || _fittingWindow || _interactiveMoveResize || WindowState != WindowState.Normal) return;
        var client = ClientSize;
        var border = FrameSize is { Width: > 0, Height: > 0 } frame
            ? new Size(Math.Max(0, frame.Width - client.Width), Math.Max(0, frame.Height - client.Height))
            : new Size();
        var requested = Position;
        var frameSize = FrameSize ?? new Size(Width, Height);
        var bounds = new PixelRect(Position, PixelSize.FromSize(frameSize, DesktopScaling));
        var intersectsDisplay = Screens.All.Any(screen => screen.Bounds.Intersects(bounds));
        // The hosting monitor tracks the native window's DPI; its top-left corner can be on another monitor.
        var targetScreen = intersectsDisplay ? Screens.ScreenFromWindow(this) ?? Screens.Primary : Screens.Primary;
        if (!intersectsDisplay && _normalPosition is { } normal)
        {
            requested = new PixelPoint(normal.X, normal.Y);
            targetScreen = Screens.ScreenFromPoint(requested) ?? Screens.Primary;
        }
        if (targetScreen is null) return;
        var fit = WindowPositionService.Fit(requested, new Size(Width, Height), _preferredMinimumSize, border,
            DesktopScaling, [targetScreen.WorkingArea]);
        if (fit is not { } placement) return;
        _fittingWindow = true;
        try
        {
            if (Width != placement.ClientSize.Width || Height != placement.ClientSize.Height || Position != placement.Position)
                _logger.LogDebug("Fitting main window to work area: client {Size}, position {Position}, desktop scale {Scale}",
                    placement.ClientSize, placement.Position, DesktopScaling);
            // On unusually small work areas only, lower the minimum; restore it when more space is available.
            MinWidth = placement.MinimumSize.Width;
            MinHeight = placement.MinimumSize.Height;
            if (Width != placement.ClientSize.Width) Width = placement.ClientSize.Width;
            if (Height != placement.ClientSize.Height) Height = placement.ClientSize.Height;
            if (Position != placement.Position) Position = placement.Position;
        }
        finally { _fittingWindow = false; }
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
            if (!_positionOpened || !IsVisible) return;
            CaptureNormalPosition();
        }, DispatcherPriority.Background);
    }

    private void CaptureNormalPosition()
    {
        // Ignore startup layout and native minimized/maximized coordinates, including Windows' hidden sentinel.
        if (_windowPositions is null || !_positionOpened || !IsVisible || _interactiveMoveResize || WindowState != WindowState.Normal
            || !Screens.All.Any(screen => screen.WorkingArea.Contains(Position))) return;
        _normalPosition = new WindowPosition(Position.X, Position.Y);
    }

    private void SaveWindowPosition()
    {
        if (_profileReset) return;
        CaptureNormalPosition();
        if (_normalPosition is { } normal && normal != _savedPosition && _windowPositions!.Save(normal))
            _savedPosition = normal;
    }

    private async void OnExit(object? sender, RoutedEventArgs args) => await RequestExitAsync();

    private async void OnResetProfile(object? sender, RoutedEventArgs args)
    {
        if (_settingsTransferBusy || _exitDialogOpen || _emailBusy || _profileCleanup is null) return;
        _settingsTransferBusy = true;
        try
        {
            if (!await ConfirmSettingsActionAsync("ResetProfile", "ResetProfileConfirm", "ResetProfile")) return;
            if (!TryResetProfile())
            {
                await ShowInformationAsync(ViewModel["ResetProfile"], ViewModel["ResetProfileFailed"]);
                return;
            }

            _profileReset = true;
            ForceClose = true;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.Shutdown();
            else Close();
        }
        finally { _settingsTransferBusy = false; }
    }

    private bool TryResetProfile()
    {
        var restoreLogging = false;
        try
        {
            // Debug runs may coexist with Release. Refuse reset while Release owns the profile.
            using var lease = _instance is { HoldsLease: false } ? SingleInstanceGuard.TryAcquire() : null;
            if (_instance is { HoldsLease: false } && lease is null) return false;
            restoreLogging = _fileLogs?.Enabled == true;
            _fileLogs?.Dispose();
            if (_profileCleanup!.Remove() == UserProfileCleanupResult.Success) return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(exception, "Could not reset the current profile");
        }
        if (restoreLogging)
        {
            try { _fileLogs!.SetEnabled(true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { _logger.LogWarning(exception, "Could not reopen the log after a reset failure"); }
        }
        return false;
    }

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
    private void OnConfirmLocation(object? sender, RoutedEventArgs args) => ViewModel.ConfirmLocation();
    private void OnRemoveAdhkarText(object? sender, RoutedEventArgs args) => ViewModel.RemoveAdhkarText();
    private async void OnStopAudio(object? sender, RoutedEventArgs args) => await ViewModel.StopAudioAsync();
    private async void OnTestPrayerReminder(object? sender, RoutedEventArgs args) => await ViewModel.TestPrayerReminderAsync();
    private async void OnTestAdhkarAudio(object? sender, RoutedEventArgs args) => await ViewModel.TestAdhkarAudioAsync();
    private void OnTestTextAdhkar(object? sender, RoutedEventArgs args) => ViewModel.TestTextAdhkar();
    private void OnAddMovementPractice(object? sender, RoutedEventArgs args) => ViewModel.AddMovementPractice();
    private void OnUpdateMovementPractice(object? sender, RoutedEventArgs args) => ViewModel.UpdateMovementPractice();
    private void OnRemoveMovementPractice(object? sender, RoutedEventArgs args) => ViewModel.RemoveMovementPractice();
    private void OnTestMovementBreak(object? sender, RoutedEventArgs args) => ViewModel.TestMovementBreak();
    private void OnReloadMovementBreaks(object? sender, RoutedEventArgs args) => ViewModel.ReloadMovementBreaks();
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
        await new AboutWindow(ViewModel, OpenProjectPageAsync).ShowDialog(this);

    private async void OnSuggestion(object? sender, RoutedEventArgs args) => await OpenEmailAsync(SupportMessage.Suggestion);
    private async void OnOpenProjectPage(object? sender, RoutedEventArgs args) => await OpenProjectPageAsync(App.ProjectUrl);
    private async void OnOpenReleasesPage(object? sender, RoutedEventArgs args) => await OpenProjectPageAsync(App.ReleasesUrl);

    private async Task OpenProjectPageAsync(string url)
    {
        try
        {
            if (!await Launcher.LaunchUriAsync(new Uri(url))) throw new InvalidOperationException("No browser accepted the project link.");
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Could not open project link {Url}", url);
            await ShowInformationAsync(ViewModel["ProjectPage"], ViewModel["BrowserOpenError"] + "\n\n" + url);
        }
    }

    private async void OnComplaint(object? sender, RoutedEventArgs args) => await OpenEmailAsync(SupportMessage.Complaint);

    private string SelectedBackupScope =>
        (this.FindControl<ComboBox>("SettingsBackupScope")?.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";

    private async void OnExportSettings(object? sender, RoutedEventArgs args)
    {
        var section = SelectedBackupScope;
        if (_settingsTransferBusy) return;
        _settingsTransferBusy = true;
        try
        {
            using var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = ViewModel["ExportSettings"], SuggestedFileName = $"AthanTime-settings-{section}.json", DefaultExtension = "json",
                FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }], ShowOverwritePrompt = true
            });
            if (file is null) return;
            var path = file.TryGetLocalPath() ?? throw new IOException("A local settings file is required.");
            var exported = section == "MovementBreaks" ? ViewModel.ExportMovementBreaks(path)
                : ViewModel.ExportSettingsBackup(section, path);
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
        var section = SelectedBackupScope;
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
            if (section == "MovementBreaks")
            {
                var movement = ViewModel.ReadMovementBreaksImport(path);
                if (movement is null)
                    await ShowInformationAsync(ViewModel["ImportSettings"], ViewModel["SettingsImportFailed"]);
                else if (await ConfirmSettingsActionAsync("MovementBreaks", "MovementImportConfirm"))
                {
                    var restored = ViewModel.ApplyImportedMovementBreaks(movement);
                    await ShowInformationAsync(ViewModel["ImportSettings"], ViewModel[restored ? "MovementImported" : "SettingsSaveFailed"]);
                }
                return;
            }
            var imported = ViewModel.ReadSettingsBackup(section, path);
            if (imported is null)
            {
                await ShowInformationAsync(ViewModel["ImportSettings"], ViewModel["SettingsImportFailed"]);
                return;
            }
            var title = imported.Section switch { "All" => "SettingsAllBackup", "PrayerReminders" => "Notifications", _ => imported.Section };
            if (!await ConfirmSettingsActionAsync(title, imported.Section == "All" ? "SettingsImportConfirm" : "SettingsSectionImportConfirm")) return;
            var applied = ViewModel.ApplySettingsBackup(imported);
            await ShowInformationAsync(ViewModel["ImportSettings"], applied ? ViewModel.SettingsStatus : ViewModel["SettingsSaveFailed"]);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Settings import failed");
            await ShowInformationAsync(ViewModel["ImportSettings"], ViewModel["SettingsImportFailed"]);
        }
        finally { _settingsTransferBusy = false; }
    }

    private Task<bool> ConfirmSettingsActionAsync(string titleKey, string messageKey, string confirmKey = "ImportSettings")
    {
        var dialog = new AppWindow(ViewModel)
        {
            Title = ViewModel[titleKey], Width = 520, Height = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var cancel = new Button { Content = ViewModel["Cancel"], MinWidth = 110 };
        var apply = new Button { Content = ViewModel[confirmKey], MinWidth = 110 };
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Margin = new Thickness(6); apply.Margin = new Thickness(6);
        buttons.Children.Add(cancel); buttons.Children.Add(apply);
        var panel = new StackPanel { Spacing = 22 };
        panel.Children.Add(new TextBlock { Text = ViewModel[messageKey], TextWrapping = TextWrapping.Wrap });
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

    /// <summary>Preserves close-to-tray while allowing native Windows update/session shutdown.</summary>
    private void OnClosing(object? sender, WindowClosingEventArgs eventArgs)
    {
        SaveWindowPosition();
        // Restart Manager must finish the desktop lifetime, including tray-only operation.
        if (OperatingSystem.IsWindows() && !eventArgs.IsProgrammatic &&
            eventArgs.CloseReason is WindowCloseReason.ApplicationShutdown or WindowCloseReason.OSShutdown)
        {
            _logger.LogInformation("Accepting native Windows shutdown: {Reason}", eventArgs.CloseReason);
            return;
        }
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
