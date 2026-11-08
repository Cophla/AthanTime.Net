using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using Avalonia.Markup.Xaml.Styling;

namespace AthanTime.Tests;

/// <summary>Runs ordinary xUnit v3 tests on a headless UI thread without a second test framework.</summary>
public sealed class UiTestHost
{
    /// <summary>Creates the real Fluent styles with off-screen rendering.</summary>
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApplication>()
        .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });

    /// <summary>Serializes UI assertions on Avalonia's dispatcher.</summary>
    public async Task Run(Action action)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestHost));
        await session.Dispatch(action, TestContext.Current.CancellationToken);
    }

    /// <summary>Runs an asynchronous binding check while the UI dispatcher keeps processing work.</summary>
    public async Task RunAsync(Func<Task> action)
    {
        await using var session = HeadlessUnitTestSession.StartNew(typeof(UiTestHost));
        await session.Dispatch(async () => { await action(); return true; }, TestContext.Current.CancellationToken);
    }
}

/// <summary>Minimal test application; the main window still loads its production XAML.</summary>
public sealed class TestApplication : Application
{
    /// <inheritdoc />
    public override void Initialize()
    {
        Styles.Add(new FluentTheme { DensityStyle = DensityStyle.Compact });
        Styles.Add(new StyleInclude(new Uri("avares://AthanTime.Net/"))
            { Source = new Uri("avares://AthanTime.Net/Styles/AppStyles.axaml") });
    }
}

/// <summary>Headless Avalonia owns one dispatcher; UI tests must not enter it concurrently.</summary>
[CollectionDefinition("UI", DisableParallelization = true)]
public sealed class UiCollection;
