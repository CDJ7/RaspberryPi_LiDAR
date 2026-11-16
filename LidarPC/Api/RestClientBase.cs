using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LidarPC.Api;

/// <summary>
/// Generic JSON-over-HTTP client with Basic authentication: base URL, credentials, JSON
/// (de)serialisation, timeouts and translation of every failure into <see cref="ApiException"/>.
/// </summary>
/// <remarks>
/// <para><b>Why this is the base of a real "is-a" hierarchy:</b> nothing in here knows about
/// LiDARs. It is "a REST client", and <see cref="LidarPiClient"/> IS a REST client that adds
/// the typed calls of this particular API (scan, LEDs, E-stop...). Another device API could
/// derive from the same base and reuse all of the plumbing, which is exactly the
/// "is-a reuse of behaviour" the inheritance is for.</para>
/// <para>One <see cref="HttpClient"/> per client instance is reused for every request.
/// Creating a new one per poll at 10 Hz would exhaust sockets.</para>
/// </remarks>
public abstract class RestClientBase : IDisposable
{
    /// <summary>JSON settings shared by all requests: camelCase names and enums as strings, like the Pi.</summary>
    protected static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly HttpClient _http;

    /// <summary>Creates the client.</summary>
    /// <param name="baseAddress">e.g. http://192.168.1.100:5000/</param>
    /// <param name="username">Basic-auth user.</param>
    /// <param name="password">Basic-auth password.</param>
    /// <param name="timeout">Per-request timeout. Short, so a dead Pi is noticed quickly.</param>
    protected RestClientBase(Uri baseAddress, string username, string password, TimeSpan timeout)
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(3),
            // Recycle connections now and then so a changed DNS entry / Pi reboot is picked up.
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            // The Pi is on the local network: never route through a system/school HTTP proxy.
            UseProxy = false,
        };
        _http = new HttpClient(handler) { BaseAddress = baseAddress, Timeout = timeout };
        string token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
        // Sent with every request (pre-emptive), so there is no 401 round trip on each poll.
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    /// <summary>The server's base address.</summary>
    public Uri BaseAddress => _http.BaseAddress!;

    /// <summary>GET a JSON resource.</summary>
    protected Task<T> GetAsync<T>(string path, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Get, path, null, cancellationToken);

    /// <summary>POST a JSON body and read a JSON response.</summary>
    protected Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Post, path, body, cancellationToken);

    /// <summary>PUT a JSON body and read a JSON response.</summary>
    protected Task<T> PutAsync<T>(string path, object? body, CancellationToken cancellationToken = default) =>
        SendAsync<T>(HttpMethod.Put, path, body, cancellationToken);

    /// <summary>Sends a request and maps every kind of failure to <see cref="ApiException"/>.</summary>
    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path.TrimStart('/'));
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation; the caller did not cancel.
            throw new ApiException($"The Pi did not answer within {_http.Timeout.TotalSeconds:0.#} s.", ApiErrorKind.Timeout, inner: ex);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException($"Cannot reach {BaseAddress.Authority}: {ex.Message}", ApiErrorKind.Unreachable, inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw await CreateErrorAsync(response, cancellationToken).ConfigureAwait(false);
            try
            {
                T? value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
                return value ?? throw new ApiException("The Pi sent an empty response.", ApiErrorKind.ServerError, response.StatusCode);
            }
            catch (JsonException ex)
            {
                // Usually means the GUI and Pi app versions do not match.
                throw new ApiException($"Unexpected response from the Pi: {ex.Message}", ApiErrorKind.ServerError, response.StatusCode, ex);
            }
        }
    }

    /// <summary>Builds an <see cref="ApiException"/> from an error response, using the Pi's {"error": "..."} message if present.</summary>
    private static async Task<ApiException> CreateErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? message = null;
        try
        {
            message = (await response.Content.ReadFromJsonAsync<ApiErrorBody>(JsonOptions, cancellationToken).ConfigureAwait(false))?.Error;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            // Not our JSON error format (e.g. a proxy's HTML page): fall back to the status text below.
        }

        ApiErrorKind kind = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => ApiErrorKind.Unauthorized,
            HttpStatusCode.BadRequest or HttpStatusCode.Conflict => ApiErrorKind.Rejected,
            _ => ApiErrorKind.ServerError,
        };
        message ??= kind == ApiErrorKind.Unauthorized ? "Wrong username or password." : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        return new ApiException(message, kind, response.StatusCode);
    }

    /// <summary>Releases the HttpClient and its connections.</summary>
    public void Dispose()
    {
        _http.Dispose();
        GC.SuppressFinalize(this);
    }
}
