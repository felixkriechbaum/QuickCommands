using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using QuickCommands.Server.Tests.Infrastructure;
using Testcontainers.MySql;

[assembly: AssemblyFixture(typeof(ServerFixture))]

namespace QuickCommands.Server.Tests.Infrastructure;

/// <summary>
/// One MySQL 8.4 container and one server for the whole test run. Tests stay independent by using fresh users
/// (random Firebase UIDs) and unique tags instead of resetting the database.
/// </summary>
public sealed class ServerFixture : IAsyncLifetime
{
    public const string ProjectId = "test-project";
    public const string AdminUid = "admin-uid";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test" };

    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4").Build();
    private WebApplicationFactory<Program>? _factory;

    public WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("Not initialized.");

    public async ValueTask InitializeAsync()
    {
        await _mysql.StartAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:DefaultConnection", _mysql.GetConnectionString());
            builder.UseSetting("Firebase:ProjectId", ProjectId);
            builder.UseSetting("Store:AdminUids", AdminUid);
            builder.UseSetting("RateLimits:GlobalPerMinute", "100000");
            builder.UseSetting("RateLimits:WritePerMinute", "100000");
            builder.UseSetting("CloudSave:MaxDocumentBytes", "4096");
            builder.ConfigureTestServices(services =>
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, o =>
                {
                    // Instead of Google's keys (OpenID discovery): a local key, same issuer/audience rules as production.
                    o.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration { Issuer = Issuer });
                    o.TokenValidationParameters.IssuerSigningKey = SigningKey;
                }));
        });
        // Starts the host: runs the migrations against the container.
        _ = _factory.Server;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await _mysql.DisposeAsync();
    }

    private static string Issuer => $"https://securetoken.google.com/{ProjectId}";

    /// <summary>A Firebase-like ID token (RS256, sub = uid).</summary>
    public static string CreateToken(string uid, string? issuer = null, string? audience = null, DateTime? expires = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? Issuer,
            Audience = audience ?? ProjectId,
            Claims = new Dictionary<string, object> { ["sub"] = uid, ["user_id"] = uid },
            IssuedAt = DateTime.UtcNow.AddMinutes(-10),
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });

    public HttpClient Anonymous() => Factory.CreateClient();

    public HttpClient As(string uid)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(uid));
        return client;
    }

    public HttpClient Admin() => As(AdminUid);
}
