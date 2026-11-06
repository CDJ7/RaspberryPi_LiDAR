using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using LidarPi.Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace LidarPi.Api;

/// <summary>
/// ASP.NET Core authentication handler for HTTP Basic auth: every REST request must carry
/// <c>Authorization: Basic base64(user:password)</c> matching the stored login.
/// </summary>
/// <remarks>
/// WHY Basic auth: the course requires username + password, every HTTP client
/// (HttpClient, curl, a browser) supports it with no extra libraries, and the GUI can send
/// it on every poll without a separate login step. Basic auth sends the password with
/// every request, readable on the network, so use it on a trusted lab network only.
/// For anything else, put the API behind HTTPS.
/// </remarks>
public sealed class BasicAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Scheme name registered in Program.cs.</summary>
    public const string SchemeName = "Basic";

    private readonly ConfigStore _store;

    /// <summary>Created per request by ASP.NET Core's authentication middleware.</summary>
    public BasicAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder, ConfigStore store)
        : base(options, logger, encoder) => _store = store;

    /// <inheritdoc/>
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out AuthenticationHeaderValue? header)
            || !string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrEmpty(header.Parameter))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(header.Parameter));
        }
        catch (FormatException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials"));
        }

        int colon = decoded.IndexOf(':', StringComparison.Ordinal);   // user names cannot contain ':', passwords can
        if (colon < 0) return Task.FromResult(AuthenticateResult.Fail("Malformed Basic credentials"));
        string user = decoded[..colon];
        string password = decoded[(colon + 1)..];

        if (!_store.VerifyCredentials(user, password))
        {
            Logger.LogWarning("Rejected login for user '{User}' from {Remote}", user, Context.Connection.RemoteIpAddress);
            return Task.FromResult(AuthenticateResult.Fail("Wrong username or password"));
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, user)], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    /// <inheritdoc/>
    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // WWW-Authenticate tells browsers/curl to ask for credentials; the JSON body is for the GUI.
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = "Basic realm=\"LidarAgv\", charset=\"UTF-8\"";
        await Response.WriteAsJsonAsync(new ApiError("Wrong or missing username/password."));
    }
}
