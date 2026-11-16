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

    protected virtual Task<string> RunPythonTranslateAsync(string text, string from, string to, CancellationToken cancellationToken)
    {
        return ArgosTranslationWorker.Shared.TranslateAsync(text, from, to, GetPythonCommand(), cancellationToken);
    }

    /// <summary>Custom Python command from the provider profile; empty means "py -3".</summary>
    private string GetPythonCommand()
    {
        // UI stores the Python command in Model; BaseUrl is accepted as a compatibility fallback.
        var configured = (_profile.Model ?? string.Empty).Trim();
        return configured.Length > 0 ? configured : (_profile.BaseUrl ?? string.Empty).Trim();
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

    internal static TranslationException MapPythonError(string stderr, string stdout)
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
            || lower.Contains("no installed language")
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

    internal static string Compact(string text)
    {
        var singleLine = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length > 500 ? singleLine[..500] + "…" : singleLine;
    }

    private static string ToArgosCode(string code)
    {
        return LanguageCatalog.ToArgosCode(code);
    }
}
