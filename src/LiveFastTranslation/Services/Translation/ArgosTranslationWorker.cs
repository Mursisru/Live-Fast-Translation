using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Infrastructure;

namespace LiveFastTranslation.Services.Translation;

/// <summary>
/// Keeps a single Argos Translate Python process alive and talks to it with newline-delimited
/// JSON. Starting Python and importing argostranslate costs several seconds, so reusing the
/// process turns repeated translations into fast requests; the loaded translation objects for
/// each language pair are cached inside the process as well.
/// </summary>
public sealed class ArgosTranslationWorker : IDisposable
{
    /// <summary>Process-wide worker shared by every translator instance.</summary>
    public static ArgosTranslationWorker Shared { get; } = new();

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly StringBuilder _stderr = new();
    private readonly HashSet<string> _warmedPairs = new(StringComparer.OrdinalIgnoreCase);

    private Process? _process;
    private StreamWriter? _input;
    private StreamReader? _output;
    private string _commandSignature = string.Empty;
    private int _requestCounter;
    private bool _disposed;

    /// <summary>True when the Python process is alive and ready for requests.</summary>
    public bool IsRunning
    {
        get
        {
            var process = _process;
            try
            {
                return process is { HasExited: false };
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Starts Python and imports argostranslate ahead of the first user request.
    /// When a language pair is given, its model is loaded as well, so the first real
    /// translation does not pay the several-second package load.
    /// Failures are reported through the exception and the next call retries.
    /// </summary>
    public async Task WarmUpAsync(string pythonCommand, CancellationToken cancellationToken = default)
    {
        await WarmUpAsync(pythonCommand, null, null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts Python and optionally preloads one language pair. Returns true when the pair
    /// was loaded by this call; false when it was already warm or no pair was requested.
    /// </summary>
    public async Task<bool> WarmUpAsync(
        string pythonCommand,
        string? from,
        string? to,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedAsync(pythonCommand, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(from)
                || string.IsNullOrWhiteSpace(to)
                || string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var pair = $"{from}>{to}";
            if (_warmedPairs.Contains(pair))
            {
                return false;
            }

            var input = _input ?? throw new TranslationException("Процесс Argos Translate не запущен.");
            var output = _output ?? throw new TranslationException("Процесс Argos Translate не запущен.");
            var requestId = Interlocked.Increment(ref _requestCounter);
            var request = JsonSerializer.Serialize(new { id = requestId, warmup = true, from, to });

            await input.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);

            await ReadAckAsync(output, requestId, cancellationToken).ConfigureAwait(false);
            _warmedPairs.Add(pair);
            return true;
        }
        catch (OperationCanceledException)
        {
            // Output of a cancelled warm-up could arrive during the next request and desync the
            // pipe, so restart lazily instead of keeping the process.
            StopProcess();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Translates text through the persistent process, starting or restarting it when needed.</summary>
    public async Task<string> TranslateAsync(
        string text,
        string from,
        string to,
        string pythonCommand,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureStartedAsync(pythonCommand, cancellationToken).ConfigureAwait(false);

            var input = _input ?? throw new TranslationException("Процесс Argos Translate не запущен.");
            var output = _output ?? throw new TranslationException("Процесс Argos Translate не запущен.");
            var requestId = Interlocked.Increment(ref _requestCounter);
            var request = JsonSerializer.Serialize(new { id = requestId, q = text, from, to });

            await input.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);

            var translated = await ReadResponseAsync(output, requestId, cancellationToken).ConfigureAwait(false);
            _warmedPairs.Add($"{from}>{to}");
            return translated;
        }
        catch
        {
            // A cancelled or failed request can leave unread output in the pipe, so the next
            // call starts a clean process instead of trying to resynchronise.
            StopProcess();
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ReadResponseAsync(StreamReader output, int expectedId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await output.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                var details = CaptureStderr();
                throw new TranslationException(
                    string.IsNullOrWhiteSpace(details)
                        ? "Процесс Argos Translate неожиданно завершился. Проверьте, что Python и языковые пакеты установлены."
                        : $"Процесс Argos Translate завершился. {ArgosTranslateProvider.Compact(details)}");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                AppLogger.Warn($"Argos: неожиданный вывод процесса: {ArgosTranslateProvider.Compact(line)}");
                continue;
            }

            using (document)
            {
                var root = document.RootElement;

                // Late answers to a cancelled request are simply skipped.
                if (root.TryGetProperty("id", out var idElement)
                    && idElement.TryGetInt32(out var responseId)
                    && responseId != expectedId)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    throw ArgosTranslateProvider.MapPythonError(error.GetString() ?? string.Empty, string.Empty);
                }

                if (root.TryGetProperty("translatedText", out var translated)
                    && translated.ValueKind == JsonValueKind.String)
                {
                    var value = (translated.GetString() ?? string.Empty).Trim();
                    if (value.Length == 0)
                    {
                        throw new TranslationException("Argos Translate вернул пустой перевод.");
                    }

                    return value;
                }
            }
        }
    }

    private async Task ReadAckAsync(StreamReader output, int expectedId, CancellationToken cancellationToken)
    {
        while (true)
        {
            var line = await output.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                var details = CaptureStderr();
                throw new TranslationException(
                    string.IsNullOrWhiteSpace(details)
                        ? "Процесс Argos Translate неожиданно завершился во время прогрева."
                        : $"Процесс Argos Translate завершился во время прогрева. {ArgosTranslateProvider.Compact(details)}");
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                AppLogger.Warn($"Argos: неожиданный вывод процесса: {ArgosTranslateProvider.Compact(line)}");
                continue;
            }

            using (document)
            {
                var root = document.RootElement;

                if (root.TryGetProperty("id", out var idElement)
                    && idElement.TryGetInt32(out var responseId)
                    && responseId != expectedId)
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                {
                    throw ArgosTranslateProvider.MapPythonError(error.GetString() ?? string.Empty, string.Empty);
                }

                if (root.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True)
                {
                    return;
                }
            }
        }
    }

    private async Task EnsureStartedAsync(string pythonCommand, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var signature = pythonCommand ?? string.Empty;
        if (IsRunning && string.Equals(_commandSignature, signature, StringComparison.Ordinal))
        {
            return;
        }

        StopProcess();
        _commandSignature = signature;

        var (fileName, prefixArgs) = ResolvePythonCommand(signature);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        ConfigureProcessEnvironment(psi);
        foreach (var arg in prefixArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        // -u keeps stdout unbuffered so answers arrive as soon as Python writes them.
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(Script);

        var process = new Process { StartInfo = psi };
        process.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            lock (_stderr)
            {
                _stderr.AppendLine(e.Data);
                if (_stderr.Length > 8000)
                {
                    _stderr.Remove(0, _stderr.Length - 8000);
                }
            }
        };

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                throw new TranslationException("Не удалось запустить Python для Argos Translate.");
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new TranslationException(
                "Не найден Python. Установите Python 3 и пакет argostranslate, затем добавьте Python в PATH или укажите команду Python в настройках Argos Translate.",
                null,
                exception);
        }

        _process = process;
        _input = process.StandardInput;
        _output = process.StandardOutput;
        process.BeginErrorReadLine();

        await ConfirmReadyAsync(process.StandardOutput, cancellationToken).ConfigureAwait(false);
    }

    private async Task ConfirmReadyAsync(StreamReader output, CancellationToken cancellationToken)
    {
        var line = await output.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
        {
            var details = CaptureStderr();
            StopProcess();
            throw new TranslationException(
                string.IsNullOrWhiteSpace(details)
                    ? "Не удалось запустить Argos Translate. Проверьте установку Python и пакета argostranslate."
                    : ArgosTranslateProvider.MapPythonError(details, string.Empty).Message);
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("ready", out var ready) && ready.ValueKind == JsonValueKind.True)
            {
                return;
            }

            var message = root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                ? error.GetString() ?? string.Empty
                : string.Empty;
            StopProcess();
            throw ArgosTranslateProvider.MapPythonError(message, line);
        }
        catch (JsonException)
        {
            // Older or unexpected builds print a banner before JSON; keep the process and
            // let the first real request surface any problem.
            AppLogger.Warn($"Argos: неожиданный стартовый вывод: {ArgosTranslateProvider.Compact(line)}");
        }
    }

    private static (string FileName, string[] PrefixArgs) ResolvePythonCommand(string configuredCommand)
    {
        var configured = (configuredCommand ?? string.Empty).Trim();
        if (configured.Length > 0)
        {
            var parts = SplitCommandLine(configured);
            if (parts.Length > 0)
            {
                return (parts[0], parts.Skip(1).ToArray());
            }
        }

        // Prefer the Windows launcher, which also picks up the newest installed Python 3.
        return ("py", new[] { "-3" });
    }

    private static string[] SplitCommandLine(string command)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        foreach (var ch in command)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(ch))
            {
                if (current.Length > 0)
                {
                    parts.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            parts.Add(current.ToString());
        }

        return parts.ToArray();
    }

    private static void ConfigureProcessEnvironment(ProcessStartInfo psi)
    {
        var packagesDir = ArgosPackageService.ResolvePackagesDirectory();
        if (string.IsNullOrWhiteSpace(packagesDir) || !Directory.Exists(packagesDir))
        {
            return;
        }

        // Argos Translate keeps installed package data in these directories.
        psi.Environment["ARGOS_PACKAGES_DIR"] = packagesDir;
        psi.Environment["ARGOS_TRANSLATE_PACKAGE_DIR"] = packagesDir;
    }

    private string CaptureStderr()
    {
        lock (_stderr)
        {
            return _stderr.ToString().Trim();
        }
    }

    private void StopProcess()
    {
        var process = _process;
        _process = null;
        _input = null;
        _output = null;
        _warmedPairs.Clear();

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or Win32Exception)
        {
            AppLogger.Warn($"Argos: не удалось остановить процесс Python: {exception.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    public void Shutdown()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopProcess();
    }

    public void Dispose() => Shutdown();

    /// <summary>
    /// Protocol: one JSON object per line. Python answers every request with
    /// <c>{"id": n, "translatedText": "..."}</c> or <c>{"id": n, "error": "..."}</c>.
    /// </summary>
    private const string Script = """
import json, sys, traceback

def emit(payload):
    sys.stdout.write(json.dumps(payload, ensure_ascii=False) + '\n')
    sys.stdout.flush()

def log(message):
    sys.stderr.write(str(message) + '\n')
    sys.stderr.flush()

translations = {}

def build_translation(from_code, to_code):
    import argostranslate.translate as translate
    return translate.get_translation_from_codes(from_code, to_code)

try:
    import argostranslate.package  # noqa: F401
    import argostranslate.translate  # noqa: F401
    emit({'id': 0, 'ready': True})
except Exception as exc:
    log(traceback.format_exc())
    emit({'id': 0, 'ready': False, 'error': str(exc)})
    sys.exit(1)

while True:
    raw = sys.stdin.readline()
    if raw == '':
        break
    raw = raw.strip()
    if not raw:
        continue
    try:
        data = json.loads(raw)
    except ValueError as exc:
        emit({'id': None, 'error': 'bad request: %s' % exc})
        continue
    request_id = data.get('id')
    try:
        from_code = data.get('from') or 'en'
        to_code = data.get('to') or 'en'
        key = from_code + '>' + to_code
        translation = translations.get(key)
        if translation is None:
            translation = build_translation(from_code, to_code)
            translations[key] = translation
        if data.get('warmup'):
            emit({'id': request_id, 'ready': True})
            continue
        translated = translation.translate(data.get('q') or '')
        emit({'id': request_id, 'translatedText': translated})
    except Exception as exc:
        log(traceback.format_exc())
        emit({'id': request_id, 'error': str(exc)})
""";
}
