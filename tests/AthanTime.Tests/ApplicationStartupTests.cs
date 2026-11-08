using AthanTime.Net;
using AthanTime.Net.Services;
using AthanTime.Net.Models;
using AthanTime.Net.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace AthanTime.Tests;

/// <summary>Exercises production JSON, Serilog and DI startup without touching the real user profile.</summary>
[Collection("UI")]
public sealed class ApplicationStartupTests(UiTestHost ui)
{
    /// <summary>Host disposal cannot release a pre-host mutex on another thread; the external startup owner retains it.</summary>
    [Fact]
    public Task HostDoesNotDisposeTheExternallyOwnedSingleInstanceLease() => ui.Run(() =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-host-lease-test-");
        var name = "athan-host-lease-" + Guid.NewGuid().ToString("N");
        using var owner = SingleInstanceGuard.TryAcquire(name)!;
        try
        {
            using (var host = global::Program.CreateHost([], directory.FullName, singleInstance: owner))
            {
                Assert.Same(owner, host.Services.GetRequiredService<SingleInstanceGuard>());
                Assert.NotNull(host.Services.GetRequiredService<App>());
            }
            // A separate thread cannot re-enter the owning thread's recursive mutex.
            bool acquired = false;
            var thread = new Thread(() =>
            {
                using var duplicate = SingleInstanceGuard.TryAcquire(name);
                acquired = duplicate is not null;
            });
            thread.Start(); thread.Join();
            Assert.False(acquired);
        }
        finally { directory.Delete(recursive: true); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task London2026BeforeReminderCrossesSpringChangeUsingElapsedMinutes(bool custom) => ui.Run(() =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-dst-alert-test-").FullName;
        try
        {
            var clock = new TestClock { Now = new DateTimeOffset(2026, 3, 29, 0, 0, 0, TimeSpan.Zero) };
            var settings = new MemorySettings();
            var location = new LocationInfo("london-dst", "London", "GB", 51.5074, -0.1278, "Europe/London");
            settings.Value.LocationId = location.Id;
            settings.Value.SavedLocations = [location];
            settings.Value.LoggingEnabled = false;
            settings.Value.NotificationsEnabled = true;
            if (custom) settings.Value.SummerTimeOverrides[location.TimeZoneId] = new()
                { Automatic = false, UseCustomSchedule = true, Start = new() { Hour = 1 }, End = new() { Month = 10, Hour = 2 } };
            settings.Value.PrayerNotifications = Enum.GetValues<Prayer>().ToDictionary(prayer => prayer,
                prayer => new PrayerNotificationSettings { NotifyBefore = prayer == Prayer.Fajr,
                    MinutesBefore = 180, NotifyAtTime = false });
            using var host = global::Program.CreateHost([], directory, "Development", services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ILoginStartupService, FakeLoginStartupService>());
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Singleton<ISettingsService>(settings));
                services.Replace(ServiceDescriptor.Singleton<IAudioPlayer, RecordingAudio>());
            });
            var calculator = host.Services.GetRequiredService<IPrayerCalculator>();
            var schedule = calculator.Calculate(new DateOnly(2026, 3, 29), location,
                CalculationMethod.Find("MWL"), AsrJuristic.Standard, HighLatitudeRule.AngleBased);
            var fajr = schedule.Times.Single(prayer => prayer.Prayer == Prayer.Fajr);
            clock.Now = fajr.UtcTime!.Value.AddMinutes(-180);
            var zones = host.Services.GetRequiredService<TimeZoneService>();
            Assert.NotEqual(TimeSpan.FromMinutes(180), fajr.LocalTime - zones.ToLocal(clock.Now, zones.Find(location.TimeZoneId)));
            var vm = host.Services.GetRequiredService<MainViewModel>();
            var alerts = new List<PrayerAlert>();
            vm.PrayerDue += (_, alert) => alerts.Add(alert);
            vm.Tick(); vm.Tick();
            Assert.Equal(PrayerAlertMoment.Before, Assert.Single(alerts).Moment);
        }
        finally { Directory.Delete(directory, recursive: true); }
    });

    [Theory]
    [InlineData(3, 29, false)]
    [InlineData(10, 25, false)]
    [InlineData(3, 29, true)]
    [InlineData(10, 25, true)]
    public Task London2026CountdownUsesElapsedTimeAcrossClockChanges(int month, int day, bool custom) => ui.Run(() =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-dst-test-").FullName;
        try
        {
            var clock = new TestClock { Now = new DateTimeOffset(2026, month, day, 0, 0, 0, TimeSpan.Zero) };
            var settings = new MemorySettings();
            var location = new LocationInfo("london-dst", "London", "GB", 51.5074, -0.1278, "Europe/London");
            settings.Value.LocationId = location.Id;
            settings.Value.SavedLocations = [location];
            settings.Value.LoggingEnabled = false;
            settings.Value.NotificationsEnabled = false;
            if (custom) settings.Value.SummerTimeOverrides[location.TimeZoneId] = new()
                { Automatic = false, UseCustomSchedule = true, Start = new() { Hour = 1 }, End = new() { Month = 10, Hour = 2 } };
            using var host = global::Program.CreateHost([], directory, "Development", services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ILoginStartupService, FakeLoginStartupService>());
                services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
                services.Replace(ServiceDescriptor.Singleton<ISettingsService>(settings));
                services.Replace(ServiceDescriptor.Singleton<IAudioPlayer, RecordingAudio>());
            });
            var vm = host.Services.GetRequiredService<MainViewModel>();
            var calculator = host.Services.GetRequiredService<IPrayerCalculator>();
            var schedule = calculator.Calculate(new DateOnly(2026, month, day), location,
                CalculationMethod.Find("MWL"), AsrJuristic.Standard, HighLatitudeRule.AngleBased);
            var fajr = schedule.Times.Single(prayer => prayer.Prayer == Prayer.Fajr);
            var remaining = fajr.UtcTime!.Value - clock.Now;
            Assert.Equal($"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}", vm.Countdown);
            var localNow = host.Services.GetRequiredService<TimeZoneService>().ToLocal(clock.Now,
                host.Services.GetRequiredService<TimeZoneService>().Find(location.TimeZoneId));
            Assert.NotEqual(fajr.LocalTime - localNow, remaining);
        }
        finally { Directory.Delete(directory, recursive: true); }
    });

    [Theory]
    [InlineData("Development")]
    [InlineData("Release")]
    public Task RealHostStartsAndCreatesWindowThroughDependencyInjection(string environment) => ui.RunAsync(async () =>
    {
        var directory = Directory.CreateTempSubdirectory("athan-host-test-").FullName;
        try
        {
            var windowConstructions = 0;
            using var host = global::Program.CreateHost([], directory, environment, services =>
            {
                services.Replace(ServiceDescriptor.Singleton<ILoginStartupService, FakeLoginStartupService>());
                // Count real DI construction without opening a native desktop or measuring elapsed time.
                var registration = services.Single(descriptor => descriptor.ServiceType == typeof(MainWindow));
                services.Replace(ServiceDescriptor.Describe(typeof(MainWindow), provider =>
                {
                    windowConstructions++;
                    return ActivatorUtilities.CreateInstance<MainWindow>(provider);
                }, registration.Lifetime));
            });
            Assert.Equal(0, windowConstructions);
            await host.StartAsync(TestContext.Current.CancellationToken);
            Assert.Equal(environment, host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
            var settings = host.Services.GetRequiredService<ISettingsService>();
            var saved = settings.Load();
            saved.LoggingEnabled = false;
            saved.StartOnLogin = false;
            settings.Save(saved);
            Assert.True(File.Exists(Path.Combine(directory, "settings.json")));
            Assert.False(Directory.Exists(Path.Combine(directory, "Logs")));
            Assert.IsType<AudioPlayer>(host.Services.GetRequiredService<IAudioPlayer>());
            var support = host.Services.GetRequiredService<SupportService>();
            Assert.StartsWith("mailto:athantimedotnet@gmail.com?", support.CreateDraft(SupportMessage.Suggestion, "en").OriginalString,
                StringComparison.Ordinal);
            var positions = host.Services.GetRequiredService<WindowPositionService>();
            Assert.Null(positions.Load());
            Assert.False(File.Exists(Path.Combine(directory, "window-position.json")));
            Assert.NotNull(host.Services.GetRequiredService<App>());
            var createWindow = host.Services.GetRequiredService<Func<MainWindow>>();
            Assert.Equal(0, windowConstructions);
            var window = createWindow();
            Assert.Same(window, createWindow());
            Assert.Equal(1, windowConstructions);
            Assert.Same(host.Services.GetRequiredService<MainViewModel>(), window.ViewModel);
            window.ForceClose = true;
            window.Show();
            Assert.True(window.IsVisible);
            window.Close();
            Assert.NotNull(positions.Load());
            Assert.True(File.Exists(Path.Combine(directory, "window-position.json")));
            await host.StopAsync(TestContext.Current.CancellationToken);
        }
        finally { Directory.Delete(directory, recursive: true); }
    });
}
