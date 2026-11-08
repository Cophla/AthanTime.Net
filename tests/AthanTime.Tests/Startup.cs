using AthanTime.Net.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;

namespace AthanTime.Tests;

/// <summary>Registers constructor-injected test services through xUnit v3.</summary>
public sealed class Startup
{
    /// <summary>Builds the test service collection.</summary>
    public void ConfigureServices(IServiceCollection services)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Application:LocationDatabasePath"] = Path.Combine(AppContext.BaseDirectory, "Data", "locations.db")
            })
            .Build();
        var logger = new LoggerConfiguration().MinimumLevel.Information().CreateLogger();

        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder => builder.ClearProviders().AddSerilog(logger, dispose: true));
        services.AddSingleton<IPrayerCalculator, PrayerCalculator>();
        services.AddSingleton<IPrayerConventionRepository, PrayerConventionRepository>();
        services.AddSingleton<ILocationRepository, LocationRepository>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IHijriDateService, HijriDateService>();
        services.AddSingleton<ManualLocationService>();
        services.AddSingleton<NodaTime.IDateTimeZoneProvider>(_ => NodaTime.DateTimeZoneProviders.Tzdb);
        services.AddSingleton<TimeZoneService>();
        services.AddSingleton<DefaultAdhkarRepository>();
        services.AddSingleton(new LoggingLevelSwitch());
        services.AddSingleton(new FileLogSink(Path.Combine(Path.GetTempPath(), "AthanTime-Tests-" + Guid.NewGuid().ToString("N"))));
        services.AddSingleton<LoggingLevelController>();
        services.AddSingleton<ILoginStartupService, FakeLoginStartupService>();
        services.AddSingleton<UiTestHost>();
        services.AddSingleton<SupportService>();
        services.AddTransient<ViewModelHarness>();
        services.AddTransient<Random>(_ => new Random(42));
        services.AddTransient<AdhkarScheduler>();
        services.AddTransient<TextAdhkarScheduler>();
    }
}
