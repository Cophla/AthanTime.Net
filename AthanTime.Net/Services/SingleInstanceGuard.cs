using System.Buffers.Binary;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace AthanTime.Net.Services;

/// <summary>Owns the per-user Release lease and local window-activation channel; Debug startup permits concurrent runs.</summary>
public sealed class SingleInstanceGuard : IDisposable
{
    // Keep these wire values stable: a running copy may receive a request from an updated executable.
    private const int ActivationSignature = 0x41544831;
    private const byte ShowWindowCommand = 1;
    private const byte AcceptedResponse = 1;
    private const byte ReceiptCommand = 2;

    private Mutex? _mutex;
    private readonly string _applicationId;
    private readonly object _activationGate = new();
    private CancellationTokenSource? _activationStop;
    private NamedPipeServerStream? _activationPipe;
    private Task? _activationTask;
    private Action? _activationHandler;
    private Action<Exception>? _activationError;
    private Exception? _activationFailure;
    private bool _activationPending;
    private bool _activationClosed;

    private SingleInstanceGuard(Mutex? mutex, string applicationId) { _mutex = mutex; _applicationId = applicationId; }

    /// <summary>Returns an unguarded lifetime in Debug builds; Release acquires the per-user lease before host/UI startup.</summary>
    public static SingleInstanceGuard? TryAcquireForStartup(string applicationId = "org.athantime.net")
    {
#if DEBUG
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        // Development runs neither acquire nor contend with an installed Release instance.
        return new SingleInstanceGuard(null, applicationId);
#else
        // Runtime environment/logging settings cannot disable the shipped Release guard.
        var instance = TryAcquire(applicationId);
        instance?.StartActivationListener();
        return instance;
#endif
    }

    /// <summary>Enforces a lease in any build, returning null if owned; keep IDs stable across versions/install paths.</summary>
    public static SingleInstanceGuard? TryAcquire(string applicationId = "org.athantime.net")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        var name = applicationId;
        if (OperatingSystem.IsWindows())
        {
            // Windows user-only ACLs restrict access but do not put different users in separate namespaces.
            using var identity = WindowsIdentity.GetCurrent();
            name += "." + identity.User!.Value;
        }
        var mutex = new Mutex(false, name, new NamedWaitHandleOptions
        {
            CurrentUserOnly = true,
            CurrentSessionOnly = false
        });
        try
        {
            if (!mutex.WaitOne(0))
            {
                mutex.Dispose();
                return null;
            }
        }
        catch (AbandonedMutexException)
        {
            // A terminated owner releases the OS mutex; this thread now owns it.
        }
        catch
        {
            mutex.Dispose();
            throw;
        }
        return new SingleInstanceGuard(mutex, applicationId);
    }

    /// <summary>Starts the same-user channel before UI initialization; an unguarded Debug lifetime does nothing.</summary>
    public void StartActivationListener()
    {
        lock (_activationGate)
        {
            if (_mutex is null || _activationTask is not null || _activationClosed) return;
            try
            {
                _activationStop?.Dispose();
                _activationStop = new CancellationTokenSource();
                _activationPipe = CreateActivationPipe();
                _activationFailure = null;
                _activationTask = ListenForActivationAsync(_activationPipe, _activationStop.Token);
            }
            catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException or PlatformNotSupportedException)
            { _activationFailure = exception; }
        }
    }

    /// <summary>Connects activation to a nonblocking UI-dispatch callback, consuming any coalesced startup request.</summary>
    public void RegisterActivationHandler(Action activate, Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(activate);
        bool pending;
        Exception? failure;
        lock (_activationGate)
        {
            if (_activationClosed) return;
            _activationHandler = activate;
            _activationError = onError;
            pending = _activationPending;
            _activationPending = false;
            failure = _activationFailure;
        }
        if (failure is not null) onError?.Invoke(failure);
        if (pending) activate();
    }

    /// <summary>Requests activation without creating a host or profile; a missing/older primary or denied channel returns false within five seconds.</summary>
    public static async Task<bool> RequestActivationAsync(string applicationId = "org.athantime.net",
        CancellationToken cancellationToken = default, Action<int>? allowForeground = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeClientStream(".", ActivationPipeName(applicationId), PipeDirection.InOut,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
                    var greeting = new byte[8];
                    await pipe.ReadExactlyAsync(greeting, timeout.Token).ConfigureAwait(false);
                    if (BinaryPrimitives.ReadInt32LittleEndian(greeting) != ActivationSignature) return false;
                    int processId = BinaryPrimitives.ReadInt32LittleEndian(greeting.AsSpan(4));
                    if (processId <= 0) return false;
                    if (allowForeground is not null) allowForeground(processId);
                    else if (OperatingSystem.IsWindows()) AllowSetForegroundWindow(processId);
                    await pipe.WriteAsync(new byte[] { ShowWindowCommand }, timeout.Token).ConfigureAwait(false);
                    var accepted = new byte[1];
                    await pipe.ReadExactlyAsync(accepted, timeout.Token).ConfigureAwait(false);
                    // Let the server close only after the acknowledgement was read; a show request is idempotent.
                    try { await pipe.WriteAsync(new byte[] { ReceiptCommand }, timeout.Token).ConfigureAwait(false); }
                    catch (Exception exception) when (exception is IOException or OperationCanceledException) { }
                    return accepted[0] == AcceptedResponse;
                }
                catch (Exception exception) when (exception is IOException or SocketException)
                {
                    // A prior client may be disconnecting as the next pipe is created; retry within the same deadline.
                    await Task.Delay(40, timeout.Token).ConfigureAwait(false);
                }
            }
        }
        catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException or OperationCanceledException
            or PlatformNotSupportedException) { return false; }
    }

    private NamedPipeServerStream CreateActivationPipe() => new(ActivationPipeName(_applicationId), PipeDirection.InOut,
        1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private async Task ListenForActivationAsync(NamedPipeServerStream pipe, CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(stopping).ConfigureAwait(false);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stopping);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                var greeting = new byte[8];
                BinaryPrimitives.WriteInt32LittleEndian(greeting, ActivationSignature);
                BinaryPrimitives.WriteInt32LittleEndian(greeting.AsSpan(4), Environment.ProcessId);
                await pipe.WriteAsync(greeting, timeout.Token).ConfigureAwait(false);
                var command = new byte[1];
                await pipe.ReadExactlyAsync(command, timeout.Token).ConfigureAwait(false);
                bool accepted = command[0] == ShowWindowCommand && DispatchActivationRequest();
                await pipe.WriteAsync(new byte[] { accepted ? AcceptedResponse : (byte)0 }, timeout.Token).ConfigureAwait(false);
                await pipe.ReadExactlyAsync(new byte[1], timeout.Token).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException or OperationCanceledException) { }
            catch (ObjectDisposedException) when (stopping.IsCancellationRequested) { }
            finally { pipe.Dispose(); }
            if (stopping.IsCancellationRequested) return;
            try
            {
                lock (_activationGate)
                {
                    if (_activationClosed) return;
                    pipe = CreateActivationPipe();
                    _activationPipe = pipe;
                }
            }
            catch (Exception exception) when (exception is IOException or SocketException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                Action<Exception>? report;
                lock (_activationGate) { _activationFailure = exception; report = _activationError; }
                report?.Invoke(exception);
                return;
            }
        }
    }

    // Keep request dispatch separate from pipe I/O: startup queues one request, shutdown rejects it.
    private bool DispatchActivationRequest()
    {
        Action? handler;
        lock (_activationGate)
        {
            if (_activationClosed) return false;
            handler = _activationHandler;
            if (handler is null) _activationPending = true;
        }
        try
        {
            handler?.Invoke();
            return true;
        }
        catch (Exception exception)
        {
            Action<Exception>? report;
            lock (_activationGate) { report = _activationError; }
            report?.Invoke(exception);
            return false;
        }
    }

    private static string ActivationPipeName(string applicationId)
    {
        string user = Environment.UserName;
        if (OperatingSystem.IsWindows())
        {
            using var identity = WindowsIdentity.GetCurrent();
            user = identity.User!.Value;
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(applicationId + "\0" + user)))[..32];
        // An explicit short Unix path avoids macOS socket limits and differing TMPDIR values across launchers/sessions.
        return OperatingSystem.IsWindows() ? "athan-" + hash : "/tmp/athan-" + hash;
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    /// <summary>Stops activation on any thread while keeping mutex ownership through host shutdown.</summary>
    public void StopActivationListener()
    {
        CancellationTokenSource? stop;
        NamedPipeServerStream? pipe;
        Task? task;
        lock (_activationGate)
        {
            _activationClosed = true;
            _activationHandler = null;
            _activationError = null;
            _activationPending = false;
            stop = _activationStop; pipe = _activationPipe; task = _activationTask;
            _activationStop = null; _activationPipe = null; _activationTask = null;
        }
        if (stop is null) return;
        stop.Cancel();
        pipe?.Dispose();
        task?.GetAwaiter().GetResult();
        stop.Dispose();
    }

    /// <summary>Releases an acquired lease on its owning thread; development/disposed lifetimes do nothing.</summary>
    public void Dispose()
    {
        StopActivationListener();
        if (_mutex is null) return;
        _mutex.ReleaseMutex();
        _mutex.Dispose();
        _mutex = null;
    }
}
