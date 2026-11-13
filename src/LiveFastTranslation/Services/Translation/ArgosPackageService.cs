using LiveFastTranslation.Models;
using LiveFastTranslation.Services.Infrastructure;

namespace LiveFastTranslation.Services.Translation;

/// <summary>
/// Detects installed Argos Translate language packages stored in the project/application package directory.
/// </summary>
public sealed class ArgosPackageService
{
    public const string PackagesDirectoryName = "argos-packages";

    /// <summary>Resolved directory containing Argos package folders (from_to), if found/created.</summary>
    public string? PackagesDirectory { get; private set; }

    /// <summary>Installed translation pairs detected from package folders (Argos codes).</summary>
    public IReadOnlyList<(string From, string To)> InstalledPairs { get; private set; } = Array.Empty<(string, string)>();

    /// <summary>Detects packages and updates <see cref="LanguageCatalog"/> with available app language codes.</summary>
    public IReadOnlyList<string> Initialize()
    {
        PackagesDirectory = ResolvePackagesDirectory();
        InstalledPairs = DetectInstalledPairs(PackagesDirectory);

        var appCodes = InstalledPairs
            .SelectMany(pair => new[] { LanguageCatalog.NormalizeToAppCode(pair.From), LanguageCatalog.NormalizeToAppCode(pair.To) })
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (appCodes.Count > 0)
        {
            LanguageCatalog.SetAvailable(appCodes);
            AppLogger.Info($"Argos: обнаружено {InstalledPairs.Count} пакетов, доступно языков: {LanguageCatalog.All.Count}. Папка: {PackagesDirectory}");
        }
        else
        {
            // Keep catalog usable but reflect that nothing is installed; UI/services will handle gracefully.
            LanguageCatalog.SetAvailable(Array.Empty<string>());
            AppLogger.Warn($"Argos: языковые пакеты не обнаружены. Ожидаемая папка: {PackagesDirectory ?? PackagesDirectoryName}");
        }

        return appCodes;
    }

    public void ConfigureProcessEnvironment(IDictionary<string, string?> environment)
    {
        if (string.IsNullOrWhiteSpace(PackagesDirectory))
        {
            return;
        }

        // Argos Translate uses ARGOS_PACKAGES_DIR for installed package data.
        environment["ARGOS_PACKAGES_DIR"] = PackagesDirectory;
        // Set common related dirs under the same root for portability where applicable.
        var root = Path.GetDirectoryName(PackagesDirectory);
        if (!string.IsNullOrWhiteSpace(root))
        {
            environment["ARGOS_TRANSLATE_PACKAGE_DIR"] ??= PackagesDirectory;
            environment["XDG_DATA_HOME"] ??= root;
        }
    }

    public static string? ResolvePackagesDirectory()
    {
        var candidates = new List<string>();
        var baseDir = AppContext.BaseDirectory;

        // Typical runtime locations (copied to output/published).
        candidates.Add(Path.Combine(baseDir, PackagesDirectoryName));
        candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", PackagesDirectoryName))); // project dir during dev run (bin/.../net8.0-windows)
        candidates.Add(Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", PackagesDirectoryName))); // solution-ish

        // Current working directory variants.
        try
        {
            candidates.Add(Path.Combine(Environment.CurrentDirectory, PackagesDirectoryName));
            candidates.Add(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "..", "..", PackagesDirectoryName)));
        }
        catch
        {
        }

        // De-duplicate while preserving order.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(candidate);
            }
            catch
            {
                continue;
            }

            if (!seen.Add(full))
            {
                continue;
            }

            if (Directory.Exists(full))
            {
                return full;
            }
        }

        // Fallback to first candidate path (may be created by caller/copy step) to keep behavior deterministic.
        try
        {
            return Path.GetFullPath(Path.Combine(baseDir, PackagesDirectoryName));
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<(string From, string To)> DetectInstalledPairs(string? packagesDirectory)
    {
        if (string.IsNullOrWhiteSpace(packagesDirectory) || !Directory.Exists(packagesDirectory))
        {
            return Array.Empty<(string, string)>();
        }

        var pairs = new List<(string From, string To)>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(packagesDirectory))
            {
                var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var parts = name.Split('_', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (parts.Length != 2)
                {
                    continue;
                }

                var from = parts[0].ToLowerInvariant();
                var to = parts[1].ToLowerInvariant();
                if (from.Length == 0 || to.Length == 0)
                {
                    continue;
                }

                pairs.Add((from, to));
            }
        }
        catch (Exception exception)
        {
            AppLogger.Warn($"Argos: не удалось прочитать папку пакетов '{packagesDirectory}': {exception.Message}");
        }

        return pairs
            .Distinct()
            .OrderBy(p => p.From, StringComparer.Ordinal)
            .ThenBy(p => p.To, StringComparer.Ordinal)
            .ToList();
    }

    public bool HasAnyInstalledPackage => InstalledPairs.Count > 0;
}
