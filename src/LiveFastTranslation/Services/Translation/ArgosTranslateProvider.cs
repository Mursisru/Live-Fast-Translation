using System.Diagnostics;
using System.Text;
using System.Text.Json;
using LiveFastTranslation.Models;

namespace LiveFastTranslation.Services.Translation;

/// <summary>
/// Fully local translation provider backed by Argos Translate (Python).
/// Requires Python and the argostranslate package with installed language models.
/// </summary>
public class ArgosTranslateProvider : ITranslationProvider
{
    private static readonly SemaphoreSlim InitLock = new(1, 1);
    private static bool _checkedEnvironment;

    private readonly ProviderProfile _profile;

    public ArgosTranslateProvider(HttpClient httpClient, ProviderProfile profile)
    {
        _ = httpClient;
        _profile = profile?.Clone() ?? throw new ArgumentNullException(nameof(profile));
    }

    public ProviderKind Kind => ProviderKind.ArgosTranslate;

    public string ProviderName => "Argos Translate";

    public async Task<TranslationResult> TranslateAsync(
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        OpenAiChatProviderBase.ValidateRequest(request);

        if (!LanguageCatalog.IsSupported(request.TargetLanguage))
        {
            throw new TranslationException(BuildLanguageUnavailableMessage(request.TargetLanguage, "целевой язык"));
        }

        var source = ResolveSourceLanguage(request);
        if (!LanguageCatalog.IsSupported(source))
        {
            throw new TranslationException(
                "Argos Translate: не удалось определить исходный язык. Укажите язык исходного текста или установите соответствующий языковой пакет.");
        }

        if (string.Equals(source, request.TargetLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return new TranslationResult(request.Text, ProviderName, request.TargetLanguage, source);
        }

        await EnsureEnvironmentHintAsync(cancellationToken).ConfigureAwait(false);

        var from = ToArgosCode(source);
        var to = ToArgosCode(request.TargetLanguage);
        var result = await RunPythonTranslateAsync(request.Text, from, to, cancellationToken).ConfigureAwait(false);

        return new TranslationResult(result, ProviderName, request.TargetLanguage, source);
    }

    private static string ResolveSourceLanguage(TranslationRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceLanguage)
            && !request.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return request.SourceLanguage;
        }

        var detected = LanguageDetector.Detect(request.Text);
        return string.IsNullOrWhiteSpace(detected) || detected.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : detected;
    }

    private async Task EnsureEnvironmentHintAsync(CancellationToken cancellationToken)
    {
        if (_checkedEnvironment)
        {
            return;
        }

        await InitLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_checkedEnvironment)
            {
                return;
            }

            // Best-effort probe: if python/argostranslate is missing, first real call will report clearly.
            _checkedEnvironment = true;
        }
        finally
        {
            InitLock.Release();
        }
    }

    protected virtual async Task<string> RunPythonTranslateAsync(string text, string from, string to, CancellationToken cancellationToken)
    {
        var (fileName, prefixArgs) = ResolvePythonCommand();
        var script = GetPythonScript();
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
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(script);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                throw new TranslationException("Не удалось запустить Python для Argos Translate.");
            }
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            throw new TranslationException(
                "Не найден Python. Установите Python 3 и пакет argostranslate, затем добавьте Python в PATH или укажите команду Python в настройках Argos Translate.",
                null,
                exception);
        }

        var input = JsonSerializer.Serialize(new { q = text, from, to });
        await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
        var stderr = (await stderrTask.ConfigureAwait(false)).Trim();

        if (process.ExitCode != 0)
        {
            throw MapPythonError(stderr, stdout);
        }

        if (string.IsNullOrWhiteSpace(stdout))
        {
            throw new TranslationException("Argos Translate вернул пустой ответ.");
        }

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                throw new TranslationException($"Argos Translate: {error.GetString()}");
            }

            if (root.TryGetProperty("translatedText", out var translated) && translated.ValueKind == JsonValueKind.String)
            {
                var value = (translated.GetString() ?? string.Empty).Trim();
                if (value.Length == 0)
                {
                    throw new TranslationException("Argos Translate вернул пустой перевод.");
                }

                return value;
            }
        }
        catch (JsonException)
        {
            // Fallback: treat stdout as plain translated text.
            return stdout;
        }

        throw new TranslationException("Argos Translate вернул ответ без перевода.");
    }

    private (string FileName, string[] PrefixArgs) ResolvePythonCommand()
    {
        // UI stores Python command in Model; BaseUrl is accepted as fallback for compatibility.
        var configured = (_profile.Model ?? string.Empty).Trim();
        if (configured.Length == 0)
        {
            configured = (_profile.BaseUrl ?? string.Empty).Trim();
        }

        if (configured.Length > 0)
        {
            // Allow custom command like "py -3" or "python".
            var parts = SplitCommandLine(configured);
            if (parts.Length > 0)
            {
                return (parts[0], parts.Skip(1).ToArray());
            }
        }

        // Prefer Windows launcher if available.
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
        if (!string.IsNullOrWhiteSpace(packagesDir) && Directory.Exists(packagesDir))
        {
            psi.Environment["ARGOS_PACKAGES_DIR"] = packagesDir;
            psi.EnvironmentVariables["ARGOS_PACKAGES_DIR"] = packagesDir;
            psi.Environment["ARGOS_TRANSLATE_PACKAGE_DIR"] = packagesDir;
            psi.EnvironmentVariables["ARGOS_TRANSLATE_PACKAGE_DIR"] = packagesDir;
        }
    }

    private static string BuildLanguageUnavailableMessage(string code, string role)
    {
        var display = LanguageCatalog.FindAny(code)?.DisplayName ?? code;
        if (LanguageCatalog.All.Count == 0)
        {
            var dir = ArgosPackageService.ResolvePackagesDirectory() ?? ArgosPackageService.PackagesDirectoryName;
            return $"Argos Translate: не обнаружены установленные языковые пакеты. Поместите пакеты Argos в папку \"{dir}\" и перезапустите приложение.";
        }

        return $"Argos Translate: {role} \"{display}\" недоступен (языковой пакет не установлен).";
    }

    private static TranslationException MapPythonError(string stderr, string stdout)
    {
        var combined = $"{stderr}\n{stdout}".Trim();
        var lower = combined.ToLowerInvariant();

        if (lower.Contains("no module named 'argostranslate'")
            || lower.Contains("modulenotfounderror") && lower.Contains("argostranslate"))
        {
            return new TranslationException(
                "Argos Translate не установлен. Установите Python-пакет: py -3 -m pip install argostranslate. Языковые пакеты должны находиться в папке argos-packages проекта.");
        }

        if (lower.Contains("no module named"))
        {
            return new TranslationException(
                $"Не найден модуль Python, необходимый для Argos Translate. {Compact(combined)}");
        }

        if (lower.Contains("no argos translate language packages installed")
            || (lower.Contains("is not installed") && lower.Contains("translation"))
            || lower.Contains("no translation installed")
            || lower.Contains("no translation from")
            || lower.Contains("from_code")
            || (lower.Contains("to_code") && lower.Contains("package"))
            || (lower.Contains("pair") && lower.Contains("install")))
        {
            var dir = ArgosPackageService.ResolvePackagesDirectory() ?? ArgosPackageService.PackagesDirectoryName;
            return new TranslationException(
                $"Не установлены языковые пакеты Argos Translate или нет пакета для выбранной пары языков. Убедитесь, что нужные пакеты находятся в папке \"{dir}\" (рядом с приложением/в папке проекта) и соответствуют обнаруженным языкам.");
        }

        if (lower.Contains("py' is not recognized")
            || lower.Contains("python' is not recognized")
            || lower.Contains("not found")
            || lower.Contains("no such file or directory") && lower.Contains("python"))
        {
            return new TranslationException(
                "Не найден Python (py/python). Установите Python 3, добавьте его в PATH или укажите команду Python в настройках Argos Translate.");
        }

        return new TranslationException(
            string.IsNullOrWhiteSpace(combined)
                ? "Ошибка локального перевода Argos Translate. Проверьте, что установлен argostranslate и языковые пакеты в папке argos-packages."
                : $"Argos Translate: {Compact(combined)}");
    }

    private static string Compact(string text)
    {
        var singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length > 500 ? singleLine[..500] + "…" : singleLine;
    }

    private static string ToArgosCode(string code)
    {
        return LanguageCatalog.ToArgosCode(code);
    }

    private static string GetPythonScript()
    {
        // Reads JSON from stdin: {q, from, to} and prints JSON {translatedText} or {error}.
        return @"
import json, sys
try:
    data = json.load(sys.stdin)
    q = data.get('q', '')
    from_code = (data.get('from') or 'en')
    to_code = (data.get('to') or 'en')
    import argostranslate.translate as t
    import argostranslate.package as p
    # Helpful diagnostics when no packages installed
    try:
        installed = p.get_installed_packages()
        if installed is not None and len(installed) == 0:
            raise Exception('No Argos Translate language packages installed')
    except Exception:
        # If package inspection fails for some reason, continue to translation attempt
        if 'No Argos Translate language packages installed' in str(sys.exc_info()[1] or ''):
            raise
    translated = t.translate(q, from_code, to_code)
    sys.stdout.write(json.dumps({'translatedText': translated}, ensure_ascii=False))
except Exception as e:
    sys.stdout.write(json.dumps({'error': str(e)}, ensure_ascii=False))
    sys.exit(1)
";
    }
}
