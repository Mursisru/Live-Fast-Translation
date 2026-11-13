using System.Net.Http;

namespace LiveFastTranslation.Tests.Fakes;

/// <summary>Creates an isolated settings directory and removes it after the test.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "LiveFastTranslation.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public HttpClient CreateHttpClient(RecordingHttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Test cleanup must not mask the assertion result.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
