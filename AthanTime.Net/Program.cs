using System.Runtime.ExceptionServices;
using Avalonia;
using AthanTime.Net;
using AthanTime.Net.Services;
using AthanTime.Net.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

// Installer shutdown is explicit and never starts a host, window or audio device.
if (args.Contains("--shutdown", StringComparer.OrdinalIgnoreCase))
{
    if (args.Length != 1) { Environment.ExitCode = 2; return; }
    using var lease = SingleInstanceGuard.TryAcquire();
    if (lease is null)
        Environment.ExitCode = SingleInstanceGuard.RequestShutdownAsync().GetAwaiter().GetResult() ? 0 : 1;
    return;
}
// Destructive cleanup is an explicit installer/helper action, never a duplicate activation.
var removeUserSettings = args.Contains("--remove-user-settings", StringComparer.OrdinalIgnoreCase);
using var instance = removeUserSettings ? SingleInstanceGuard.TryAcquire() : SingleInstanceGuard.TryAcquireForStartup();
if (instance is null)
{
    Environment.ExitCode = !removeUserSettings && SingleInstanceGuard.RequestActivationAsync().GetAwaiter().GetResult() ? 0 : 1;
    return;
}

if (removeUserSettings)
{
    if (args.Length != 1) { Environment.ExitCode = 2; return; }
    var userData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    if (!Path.IsPathFullyQualified(userData)) { Environment.ExitCode = (int)UserProfileCleanupResult.InvalidProfilePath; return; }
    var directory = Path.Combine(userData, "AthanTime");
    // No host, timer, audio or file sink may start while deleting this user's profile.
    var cleanup = new UserProfileCleanup(directory, new LoginStartupService(),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<UserProfileCleanup>.Instance);
    Environment.ExitCode = (int)cleanup.Remove();
    return;
}

if (OperatingSystem.IsWindows())
    RunOnStaThread(args, instance);
else
    RunApplication(args, instance);

static void RunOnStaThread(string[] arguments, SingleInstanceGuard instance)
{
    ExceptionDispatchInfo? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            RunApplication(arguments, instance);
        }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    failure?.Throw();
}

static void RunApplication(string[] arguments, SingleInstanceGuard instance)
{
    var appDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AthanTime");

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Information()
        .CreateBootstrapLogger();

    try
    {
        using var host = Program.CreateHost(arguments, appDataDirectory, singleInstance: instance);
        host.Start();
        var savedSettings = host.Services.GetRequiredService<ISettingsService>().Load();
        var logging = host.Services.GetRequiredService<LoggingLevelController>();
        logging.SetEnabled(savedSettings.LoggingEnabled);
        logging.SetDebug(savedSettings.DebugLoggingEnabled);
        Log.Information(
            "Starting Athan Time {Version} in {Environment}", App.ReleaseVersion,
            host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        BuildAvaloniaApp(host.Services).StartWithClassicDesktopLifetime(arguments);
        host.StopAsync().GetAwaiter().GetResult();
    }
    catch (Exception exception)
    {
        Log.Fatal(exception, "Athan Time terminated unexpectedly");
        throw;
    }
    finally
    {
        Log.Information("Athan Time stopped");
        Log.CloseAndFlush();
    }
}

static AppBuilder BuildAvaloniaApp(IServiceProvider serviceProvider) =>
    AppBuilder.Configure(() => serviceProvider.GetRequiredService<App>())
        .UsePlatformDetect()
        .WithInterFont();

/// <summary>Application composition root, shared by runtime startup and isolated host tests.</summary>
public partial class Program
{
    /// <summary>Builds the real host with an injectable data directory and environment for testing.</summary>
    public static IHost CreateHost(string[] arguments, string appDataDirectory, string? environment = null,
        Action<IServiceCollection>? configureServices = null, SingleInstanceGuard? singleInstance = null)
    {
#if DEBUG
        environment ??= Environments.Development;
#else
        environment ??= "Release";
#endif
        return Host.CreateDefaultBuilder(arguments)
            .UseContentRoot(AppContext.BaseDirectory)
            .UseEnvironment(environment)
            .UseSerilog((context, services, configuration) =>
            {
                var levelSwitch = services.GetRequiredService<LoggingLevelSwitch>();
                var fileSink = services.GetRequiredService<FileLogSink>();
                configuration
                    .ReadFrom.Configuration(context.Configuration)
                    .MinimumLevel.ControlledBy(levelSwitch)
                    .Filter.ByIncludingOnly(_ => fileSink.Enabled)
                    .Enrich.FromLogContext()
                    .WriteTo.Sink(fileSink);
            })
            .ConfigureServices(services =>
            {
                // Externally owned: the composition root releases this mutex on its acquiring thread after host disposal.
                if (singleInstance is not null) services.AddSingleton(singleInstance);
                services.AddSingleton<App>();
                services.AddSingleton<MainWindow>();
                // Defer window creation until Avalonia initializes its application and styles.
                services.AddSingleton<Func<MainWindow>>(provider => provider.GetRequiredService<MainWindow>);
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<IPrayerCalculator, PrayerCalculator>();
                services.AddSingleton<IPrayerConventionRepository, PrayerConventionRepository>();
                services.AddSingleton<ILocationRepository, LocationRepository>();
                services.AddSingleton<ILocalizationService, LocalizationService>();
                services.AddSingleton<IHijriDateService, HijriDateService>();
                services.AddSingleton<ISettingsService>(provider => new SettingsService(
                    provider.GetRequiredService<ILogger<SettingsService>>(),
                    provider.GetRequiredService<ManualLocationService>(), Path.Combine(appDataDirectory, "settings.json"),
                    settings => provider.GetRequiredService<FirstRunDefaults>().Apply(settings)));
                services.AddSingleton<FirstRunDefaults>();
                services.AddSingleton<ManualLocationService>();
                services.AddSingleton<NodaTime.IDateTimeZoneProvider>(_ => NodaTime.DateTimeZoneProviders.Tzdb);
                services.AddSingleton<TimeZoneService>();
                services.AddSingleton<IAudioPlayer, AudioPlayer>();
                services.AddSingleton<AdhkarScheduler>();
                services.AddSingleton<Random>(_ => Random.Shared);
                services.AddSingleton<TextAdhkarScheduler>();
                services.AddSingleton<DefaultAdhkarRepository>();
                services.AddSingleton<IMovementBreakRepository>(_ => new MovementBreakRepository(
                    Path.Combine(appDataDirectory, "movement-reminders.db"),
                    Path.Combine(AppContext.BaseDirectory, "Data", "movement-breaks.json")));
                services.AddSingleton<LoggingLevelSwitch>();
                services.AddSingleton<FileLogSink>(_ => new FileLogSink(appDataDirectory));
                services.AddSingleton<LoggingLevelController>();
                services.AddSingleton<ILoginStartupService, LoginStartupService>();
                services.AddSingleton<UserProfileCleanup>(provider => new UserProfileCleanup(appDataDirectory,
                    provider.GetRequiredService<ILoginStartupService>(), provider.GetRequiredService<ILogger<UserProfileCleanup>>()));
                services.AddSingleton<SupportService>();
                services.AddSingleton<WindowPositionService>(provider => new WindowPositionService(
                    provider.GetRequiredService<ILogger<WindowPositionService>>(),
                    Path.Combine(appDataDirectory, "window-position.json")));
                services.AddSingleton(TimeProvider.System);
                configureServices?.Invoke(services);
            })
            .Build();
    }
}
