using System.Net;

namespace LidarPC.Api;

/// <summary>What kind of failure an <see cref="ApiException"/> represents, so the GUI can react differently.</summary>
public enum ApiErrorKind
{
    /// <summary>The Pi could not be reached (wrong IP, Pi off, firewall, service not running).</summary>
    Unreachable,
    /// <summary>The request took longer than the timeout.</summary>
    Timeout,
    /// <summary>401: wrong user name or password. Polling stops so we do not keep hammering the login.</summary>
    Unauthorized,
    /// <summary>400/409: the Pi refused the request; the message says why (e.g. E-stop still pressed).</summary>
    Rejected,
    /// <summary>Any other HTTP error or an unreadable response.</summary>
    ServerError,
}

/// <summary>
/// The single exception type the GUI has to handle for REST calls. <see cref="RestClientBase"/>
/// translates HttpClient's many failure types into this, with a message an operator understands.
/// </summary>
public sealed class ApiException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">Operator-readable message.</param>
    /// <param name="kind">Category of the failure.</param>
    /// <param name="statusCode">HTTP status, if a response was received.</param>
    /// <param name="inner">Original exception.</param>
    public ApiException(string message, ApiErrorKind kind, HttpStatusCode? statusCode = null, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    /// <summary>Failure category.</summary>
    public ApiErrorKind Kind { get; }

    /// <summary>HTTP status code, or null if no response arrived.</summary>
    public HttpStatusCode? StatusCode { get; }
}
