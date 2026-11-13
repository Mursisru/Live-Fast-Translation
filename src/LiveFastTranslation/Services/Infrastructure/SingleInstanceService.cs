using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace LiveFastTranslation.Services.Infrastructure;

/// <summary>
/// Keeps a single running copy of the application per user session and lets a second launch
/// ask the first one to open its settings window. Portable profiles (LFT_DATA_DIR) get their
/// own instance name, so an isolated test profile never collides with the installed app.
/// </summary>
public sealed class SingleInstanceService : IDisposable
{
    private const string MutexBaseName = @"Local\LiveFastTranslation.SingleInstance.4C1F9E2A";
    private const string ActivationEventBaseName = @"Local\LiveFastTranslation.Activate.4C1F9E2A";

    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private readonly EventWaitHandle? _activationEvent;
    private readonly RegisteredWaitHandle? _registeredWait;
    private bool _disposed;

    public SingleInstanceService()
    {
        var suffix = BuildProfileSuffix();
        var mutexName = MutexBaseName + suffix;
        var eventName = ActivationEventBaseName + suffix;

        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        _ownsMutex = createdNew;
        IsFirstInstance = createdNew;

        if (!createdNew)
        {
            return;
        }

        _activationEvent = new EventWaitHandle(
            initialState: false,
            EventResetMode.AutoReset,
            eventName);
        _registeredWait = ThreadPool.RegisterWaitForSingleObject(
            _activationEvent,
            OnActivationSignal,
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    private static string BuildProfileSuffix()
    {
        var dataDirectory = Environment.GetEnvironmentVariable("LFT_DATA_DIR");
        if (string.IsNullOrWhiteSpace(dataDirectory))
        {
            return string.Empty;
        }

        try
        {
            var normalized = Path.GetFullPath(dataDirectory.Trim()).ToLowerInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return "." + Convert.ToHexString(hash.AsSpan(0, 6));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "." + Guid.NewGuid().ToString("N")[..8];
        }
    }

    public bool IsFirstInstance { get; }

    /// <summary>Raised on a thread-pool thread when another launch asks this instance to show itself.</summary>
    public event EventHandler? ActivationRequested;

    /// <summary>Signals the already running instance. Returns false when it cannot be reached.</summary>
    public bool NotifyExistingInstance()
    {
        var eventName = ActivationEventBaseName + BuildProfileSuffix();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var handle = EventWaitHandle.OpenExisting(eventName);
                handle.Set();
                return true;
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(50);
            }
            catch (Exception exception)
            {
                AppLogger.Warn($"Не удалось активировать запущенный экземпляр: {exception.Message}");
                return false;
            }
        }

        return false;
    }

    private void OnActivationSignal(object? state, bool timedOut)
    {
        if (timedOut || _disposed)
        {
            return;
        }

        try
        {
            ActivationRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            AppLogger.Error("Ошибка обработки запроса второго экземпляра.", exception);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _registeredWait?.Unregister(waitObject: null);
        _activationEvent?.Dispose();

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex was already released during shutdown.
            }
        }

        _mutex.Dispose();
    }
}
