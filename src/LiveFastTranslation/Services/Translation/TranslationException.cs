using System.Net;

namespace LiveFastTranslation.Services.Translation;

public sealed class TranslationException : Exception
{
    public TranslationException(string message, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode? StatusCode { get; }
}
