using System.Net;
using System.Net.Http;
using System.Text;

namespace LiveFastTranslation.Tests.Fakes;

internal sealed class RecordedRequest
{
    public required HttpMethod Method { get; init; }

    public Uri? Uri { get; init; }

    public string Body { get; init; } = string.Empty;

    public string? ContentType { get; init; }

    public required IReadOnlyDictionary<string, string> Headers { get; init; }
}

internal sealed class RecordingHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<RecordedRequest, HttpResponseMessage> _responder;

    public RecordingHttpMessageHandler(Func<RecordedRequest, HttpResponseMessage> responder)
    {
        _responder = responder ?? throw new ArgumentNullException(nameof(responder));
    }

    public List<RecordedRequest> Requests { get; } = new();

    public static RecordingHttpMessageHandler Json(
        string json,
        HttpStatusCode statusCode = HttpStatusCode.OK)
    {
        return new RecordingHttpMessageHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers)
            {
                headers[header.Key] = string.Join(", ", header.Value);
            }
        }

        var recorded = new RecordedRequest
        {
            Method = request.Method,
            Uri = request.RequestUri,
            Body = body,
            ContentType = request.Content?.Headers.ContentType?.MediaType,
            Headers = headers
        };

        Requests.Add(recorded);
        return _responder(recorded);
    }
}
