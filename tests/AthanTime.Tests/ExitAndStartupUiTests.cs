using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AthanTime.Net;
using AthanTime.Net.Services;
using Microsoft.Extensions.Logging;

namespace AthanTime.Tests;

/// <summary>Checks login registration, one-time minimized startup, tray restoration and explicit exit choices.</summary>
[Collection("UI")]
public sealed class ExitAndStartupUiTests(UiTestHost ui, ViewModelHarness harness,
    ILogger<MainWindow> logger, ILogger<WindowPositionService> positionLogger, ILogger<App> appLogger)
{
    /// <summary>macOS shell reopen events restore an existing window without needing another process.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ShellReopenRestoresTheWindowAfterStartupHiding(bool duringStartup) => ui.Run(() =>
    {
        harness.Settings.Value.StartMinimized = duringStartup;
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { ForceClose = true };
        var app = new App(() => window, appLogger)
            { ApplicationLifetime = new ClassicDesktopStyleApplicationLifetime() };
        try
        {
            app.OnFrameworkInitializationCompleted();
            if (duringStartup) app.HandleApplicationActivation(ActivationKind.Reopen);
            window.Show();
            if (!duringStartup) { window.Hide(); app.HandleApplicationActivation(ActivationKind.Reopen); }
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Normal, window.WindowState);
        }
        finally { window.Close(); foreach (var icon in TrayIcon.GetIcons(app) ?? []) icon.Dispose(); }
    });

    [Theory]
    [InlineData(ActivationKind.Background)]
    [InlineData(ActivationKind.File)]
    [InlineData(ActivationKind.OpenUri)]
    public Task OtherShellActivationKindsDoNotOpenTheWindow(ActivationKind kind) => ui.Run(() =>
    {
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { ForceClose = true };
        var app = new App(() => window, appLogger)
            { ApplicationLifetime = new ClassicDesktopStyleApplicationLifetime() };
        try
        {
            app.OnFrameworkInitializationCompleted();
            window.Show(); window.Hide(); app.HandleApplicationActivation(kind);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.IsVisible);
        }
        finally { window.Close(); foreach (var icon in TrayIcon.GetIcons(app) ?? []) icon.Dispose(); }
    });

    /// <summary>Uses the production App binding: an IPC request restores one real headless window after startup hiding.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AnotherLaunchRestoresTheExistingWindowThroughTheApplication(bool duringStartup) => ui.Run(() =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-activation-ui-");
        var name = "athan-activation-ui-" + Guid.NewGuid().ToString("N");
        using var owner = SingleInstanceGuard.TryAcquire(name)!;
        owner.StartActivationListener();
        harness.Settings.Value.StartMinimized = duringStartup;
        using var vm = harness.Create();
        var positions = new WindowPositionService(positionLogger, Path.Combine(directory.FullName, "window-position.json"));
        var window = new MainWindow(vm, logger, positions) { ForceClose = true };
        var app = new App(() => window, appLogger, owner)
            { ApplicationLifetime = new ClassicDesktopStyleApplicationLifetime() };
        try
        {
            if (duringStartup)
                Assert.True(SingleInstanceGuard.RequestActivationAsync(name, allowForeground: _ => { }).GetAwaiter().GetResult());
            app.OnFrameworkInitializationCompleted();
            window.Show();
            if (!duringStartup)
            {
                window.Hide();
                window.WindowState = WindowState.Minimized;
                Assert.True(SingleInstanceGuard.RequestActivationAsync(name, allowForeground: _ => { }).GetAwaiter().GetResult());
            }
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Normal, window.WindowState);
            for (int cycle = 0; cycle < 2; cycle++)
            {
                window.Hide();
                Assert.True(SingleInstanceGuard.RequestActivationAsync(name, allowForeground: _ => { }).GetAwaiter().GetResult());
                Dispatcher.UIThread.RunJobs();
                Assert.True(window.IsVisible);
            }
            Assert.Equal(0, harness.Settings.Saves);
            Assert.Empty(harness.Audio.Played);
        }
        finally
        {
            window.Close();
            foreach (var icon in TrayIcon.GetIcons(app) ?? []) icon.Dispose();
            directory.Delete(recursive: true);
        }
    });

    [Fact]
    public Task PositionSurvivesTrayCyclesAndANewWindowWithoutSavingEachMove() => ui.Run(() =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-window-position-ui-");
        using var vm = harness.Create();
        var path = Path.Combine(directory.FullName, "window-position.json");
        var store = new WindowPositionService(positionLogger, path);
        var window = new MainWindow(vm, logger, store) { CanHideToTray = true, ForceClose = true };
        try
        {
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            window.Show();
            var area = window.Screens.Primary!.WorkingArea;
            var point = new PixelPoint(area.X + 60, area.Y + 70);
            window.Position = point;
            Dispatcher.UIThread.RunJobs();
            Assert.False(File.Exists(path));
            window.Hide();
            Assert.Equal(new WindowPosition(point.X, point.Y), store.Load());
            File.AppendAllText(path, "\n ");
            var savedBytes = File.ReadAllBytes(path);
            for (int cycle = 0; cycle < 3; cycle++)
            {
                window.ShowFromTray();
                Assert.True(window.IsVisible);
                Assert.Equal(point, window.Position);
                window.Hide();
                Assert.Equal(savedBytes, File.ReadAllBytes(path));
            }
            window.Close();
            window = new MainWindow(vm, logger, store) { ForceClose = true };
            Assert.Equal(WindowStartupLocation.Manual, window.WindowStartupLocation);
            window.Show();
            Assert.Equal(point, window.Position);
            Assert.Equal(1180, window.Width);
            Assert.Equal(800, window.Height);
        }
        finally { window.Close(); directory.Delete(recursive: true); }
    });

    [Theory]
    [InlineData(WindowState.Minimized)]
    [InlineData(WindowState.Maximized)]
    [InlineData(WindowState.FullScreen)]
    public Task NonNormalStatesDoNotReplaceTheLastNormalPosition(WindowState state) => ui.Run(() =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-window-position-ui-");
        using var vm = harness.Create();
        var store = new WindowPositionService(positionLogger, Path.Combine(directory.FullName, "window-position.json"));
        var window = new MainWindow(vm, logger, store) { ForceClose = true };
        try
        {
            window.Show();
            var area = window.Screens.Primary!.WorkingArea;
            var point = new PixelPoint(area.X + 60, area.Y + 70);
            window.Position = point;
            Dispatcher.UIThread.RunJobs();
            // Simulate native move-before-state notifications in the same dispatcher turn.
            window.Position = new PixelPoint(area.X, area.Y);
            window.WindowState = state;
            if (state == WindowState.Minimized) window.Position = new PixelPoint(-32000, -32000);
            Dispatcher.UIThread.RunJobs();
            window.Hide();
            Assert.Equal(new WindowPosition(point.X, point.Y), store.Load());
        }
        finally { window.Close(); directory.Delete(recursive: true); }
    });

    [Fact]
    public Task MinimizedStartupRetainsSavedCoordinatesAndOffscreenCoordinatesRecover() => ui.Run(() =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-window-position-ui-");
        using var vm = harness.Create();
        var path = Path.Combine(directory.FullName, "window-position.json");
        var store = new WindowPositionService(positionLogger, path);
        Assert.True(store.Save(new(60, 70)));
        var bytes = File.ReadAllBytes(path);
        var window = new MainWindow(vm, logger, store) { ForceClose = true, CanHideToTray = true };
        try
        {
            window.PrepareStartupVisibility(true);
            window.Show();
            Assert.False(window.IsVisible);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            window.ShowFromTray();
            Assert.True(window.IsVisible);
            Assert.Equal(new PixelPoint(60, 70), window.Position);
            window.Close();
            Assert.True(store.Save(new(int.MaxValue, int.MinValue)));
            window = new MainWindow(vm, logger, store) { ForceClose = true };
            window.Show();
            Assert.True(window.Screens.Primary!.WorkingArea.Contains(window.Position));
        }
        finally { window.Close(); directory.Delete(recursive: true); }
    });

    [Fact]
    public Task PositionSaveFailureDoesNotTrapTheWindowAndTheNextHideRetries() => ui.Run(() =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-window-position-ui-");
        using var vm = harness.Create();
        var blocked = Path.Combine(directory.FullName, "profile");
        File.WriteAllText(blocked, "Directory creation is deliberately blocked.");
        var store = new WindowPositionService(positionLogger, Path.Combine(blocked, "window-position.json"));
        var window = new MainWindow(vm, logger, store) { ForceClose = true };
        try
        {
            window.Show();
            window.Position = new PixelPoint(60, 70);
            window.Hide();
            window.ShowFromTray();
            Assert.True(window.IsVisible);
            File.Delete(blocked);
            window.Hide();
            Assert.Equal(new WindowPosition(60, 70), store.Load());
        }
        finally { window.Close(); directory.Delete(recursive: true); }
    });

    [Fact]
    public Task StartupToggleUsesInjectedRegistration() => ui.Run(() =>
    {
        using var vm = harness.Create();
        vm.StartOnLogin = true;
        Assert.True(vm.StartOnLogin);
        Assert.True(harness.Settings.Value.StartOnLogin);
        vm.StartOnLogin = false;
        Assert.False(vm.StartOnLogin);
    });

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, true)]
    public Task StartupVisibilityDoesNotHideLaterTrayRestores(bool minimized, bool trayAvailable, bool closeToTray) => ui.Run(() =>
    {
        harness.Settings.Value.CloseToTray = closeToTray;
        harness.Settings.Value.StartMinimized = minimized;
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger) { CanHideToTray = trayAvailable };
        try
        {
            window.PrepareStartupVisibility(minimized);
            window.Show();
            Assert.Equal(!(minimized && trayAvailable && closeToTray), window.IsVisible);
            Assert.Equal(minimized ? WindowState.Minimized : WindowState.Normal, window.WindowState);

            // Opened fires on every Show after Hide, not only on the first application launch.
            for (int cycle = 0; cycle < 3; cycle++)
            {
                window.ShowFromTray();
                Assert.True(window.IsVisible);
                Assert.Equal(WindowState.Normal, window.WindowState);
                if (closeToTray)
                {
                    window.Close();
                    Assert.Equal(!trayAvailable, window.IsVisible);
                    if (!trayAvailable) Assert.Equal(WindowState.Minimized, window.WindowState);
                }
                else window.Hide();
            }
            Assert.Equal(minimized, vm.StartMinimized);
            Assert.Equal(closeToTray, vm.CloseToTray);
            Assert.Equal(0, harness.Settings.Saves);
        }
        finally { window.ForceClose = true; window.Close(); }
    });

    [Fact]
    public Task CancelingExitKeepsMainWindowOpen() => ui.RunAsync(async () =>
    {
        using var vm = harness.Create();
        var window = new MainWindow(vm, logger);
        Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
        window.Show();
        try
        {
            var pending = window.RequestExitAsync();
            var dialog = Assert.Single(window.OwnedWindows);
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, vm["Cancel"]));
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await pending;
            Assert.False(window.ForceClose);
            Assert.True(window.IsVisible);
        }
        finally { window.ForceClose = true; window.Close(); }
    });
}
