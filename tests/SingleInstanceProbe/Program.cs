using AthanTime.Net.Services;

// Test-only process: exercise the real guard/activation policy without UI, profiles or native integrations.
if (args.Length > 1 && args[1] == "activate")
{
    using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var accepted = await SingleInstanceGuard.RequestActivationAsync(args[0], cancellation.Token,
        processId => Console.WriteLine($"target:{processId}"));
    Console.WriteLine(accepted ? "activated" : "unavailable");
    return;
}
SingleInstanceGuard? abandoned = null;
if (args.Length > 1 && args[1] == "abandon")
{
    // Retain the handle after its owning thread exits to exercise AbandonedMutexException.
    var thread = new Thread(() => abandoned = SingleInstanceGuard.TryAcquire(args[0]));
    thread.Start();
    thread.Join();
}
using var instance = args.Length > 1 && args[1] is "startup" or "startup-activate"
    ? SingleInstanceGuard.TryAcquireForStartup(args[0])
    : SingleInstanceGuard.TryAcquire(args[0]);
if (instance is not null && args.Length > 1 && args[1] == "activation-owner") instance.StartActivationListener();
if (instance is null && args.Length > 1 && args[1] == "startup-activate")
    await SingleInstanceGuard.RequestActivationAsync(args[0], allowForeground: _ => { });
Console.WriteLine(instance is null ? "duplicate" : "acquired");
if (instance is not null && args.Length > 1 && args[1] == "activation-owner")
{
    if (args.Length > 2 && args[2] == "delayed") Console.ReadLine();
    int failNext = 0;
    instance.RegisterActivationHandler(() =>
        {
            if (Interlocked.Exchange(ref failNext, 0) != 0) throw new InvalidOperationException("Test activation failure.");
            Console.WriteLine("activated");
        },
        exception => Console.WriteLine("error:" + exception.GetType().Name));
    while (Console.ReadLine() is { } command)
    {
        if (command == "fail-next") { Interlocked.Exchange(ref failNext, 1); Console.WriteLine("ready"); continue; }
        if (command != "stop") break;
        instance.StopActivationListener();
        Console.WriteLine("stopped");
    }
}
else if (instance is not null) Console.ReadLine();
GC.KeepAlive(abandoned);
