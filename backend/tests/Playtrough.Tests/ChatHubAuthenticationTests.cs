using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Playtrough.Api.Hubs;
using Xunit;

namespace Playtrough.Tests;

public sealed class ChatHubAuthenticationTests
{
    private const string Negotiate = ChatHub.Path + "/negotiate?negotiateVersion=1";

    [Fact]
    public async Task OnlyAnAuthenticatedNumericSubjectCanNegotiateAndRestIgnoresQueryTokens()
    {
        using var factory = new LoginApiFactory();
        using var client = factory.CreateClient();
        var valid = Token(factory, "21", DateTime.UtcNow.AddMinutes(5));
        using var anonymous = await client.PostAsync(Negotiate, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var negotiated = await NegotiateAsync(client, valid);
        Assert.Equal(HttpStatusCode.OK, negotiated.StatusCode);
        Assert.Contains("WebSockets", await negotiated.Content.ReadAsStringAsync());

        using var malformed = await NegotiateAsync(client, Token(factory, "bad-subject", DateTime.UtcNow.AddMinutes(5)));
        Assert.Equal(HttpStatusCode.Forbidden, malformed.StatusCode);
        using var expired = await NegotiateAsync(client, Token(factory, "21", DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        using var queryNegotiate = await client.PostAsync(Negotiate + "&access_token=" + valid, null);
        Assert.Equal(HttpStatusCode.Unauthorized, queryNegotiate.StatusCode);
        using var queryRest = await client.GetAsync("/api/groups?access_token=" + valid);
        Assert.Equal(HttpStatusCode.Unauthorized, queryRest.StatusCode);
        Assert.Equal("21", JwtHubUserIdProvider.GetUserId(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "00021")]))));
    }

    [Fact]
    public async Task BrowserWebSocketTokenIsNotLoggedAndTheConnectionClosesOnExpiry()
    {
        using var factory = new LoginApiFactory();
        var logs = new TokenLogRecorder();
        using var configured = factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.AddFilter("Microsoft.AspNetCore", LogLevel.Information);
            logging.AddProvider(logs);
        }));
        using var client = configured.CreateClient();
        var expires = DateTime.UtcNow.AddSeconds(5);
        var token = Token(factory, "21", expires);
        logs.Token = token;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(18));
        var webSockets = configured.Server.CreateWebSocketClient();
        using var socket = await webSockets.ConnectAsync(
            new Uri("wss://localhost" + ChatHub.Path + "?access_token=" + token), deadline.Token);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")),
            WebSocketMessageType.Text, true, deadline.Token);
        var buffer = new ArraySegment<byte>(new byte[1024]);
        var handshake = await socket.ReceiveAsync(buffer, deadline.Token);
        Assert.StartsWith("{}\u001e", Encoding.UTF8.GetString(buffer.Array!, 0, handshake.Count));

        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(buffer, deadline.Token);
        } while (result.MessageType != WebSocketMessageType.Close);
        Assert.True(DateTime.UtcNow >= expires.AddSeconds(-1));
        Assert.False(logs.TokenSeen);
        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, deadline.Token);
    }

    private static async Task<HttpResponseMessage> NegotiateAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Negotiate);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    private static string Token(LoginApiFactory factory, string subject, DateTime expires)
        => new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "Playtrough.Tests", "Playtrough.Tests.Client", [new Claim("sub", subject)],
            expires: expires, signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(factory.SigningKey)), SecurityAlgorithms.HmacSha256)));

    private sealed class TokenLogRecorder : ILoggerProvider
    {
        private int _tokenSeen;
        public string? Token { get; set; }
        public bool TokenSeen => Volatile.Read(ref _tokenSeen) != 0;
        public ILogger CreateLogger(string categoryName) => new Recorder(this);
        public void Dispose() { }

        private sealed class Recorder(TokenLogRecorder owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (owner.Token is { } token && formatter(state, exception).Contains(token, StringComparison.Ordinal))
                    Interlocked.Exchange(ref owner._tokenSeen, 1);
            }
        }
    }
}
