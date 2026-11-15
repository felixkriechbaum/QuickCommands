using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using QuickCommands.Server.Data;
using QuickCommands.Server.Filters;
using QuickCommands.Server.Interfaces;
using QuickCommands.Server.Middlewares;
using QuickCommands.Server.Options;
using QuickCommands.Server.Services;
using IPNetwork = System.Net.IPNetwork;

namespace QuickCommands.Server.Setup;

internal static class ServiceRegistration
{
    public static IServiceCollection AddServer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FirebaseOptions>().Bind(configuration.GetSection(FirebaseOptions.Section))
            .Validate(o => AppInfo.IsGeneratingOpenApiDocument || !string.IsNullOrWhiteSpace(o.ProjectId), "Firebase:ProjectId is required.")
            .ValidateOnStart();
        services.AddOptions<StoreOptions>().Bind(configuration.GetSection(StoreOptions.Section));
        services.AddOptions<CloudSaveOptions>().Bind(configuration.GetSection(CloudSaveOptions.Section));
        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.Section));
        services.AddOptions<LoggerUiOptions>().Bind(configuration.GetSection(LoggerUiOptions.Section));

        services.AddSingleton(TimeProvider.System);
        services.AddDatabase(configuration);
        services.AddAuth();
        services.AddRateLimits(configuration);
        services.AddApi();
        services.AddForwardedHeaders(configuration);

        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUser>());
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<ICloudSaveService, CloudSaveService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IStoreService, StoreService>();
        services.AddScoped<IModerationService, ModerationService>();
        return services;
    }

    private static void AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName) ?? "";
        var databaseOptions = configuration.GetSection(DatabaseOptions.Section).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.AddDbContext<AppDbContext>(o => o.UseMySql(connectionString, ServerVersion.Parse(databaseOptions.ServerVersion)));
    }

    private static void AddAuth(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        // Configured from FirebaseOptions (not inline), so tests and environments can override the values.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<FirebaseOptions>>((o, firebase) =>
            {
                var projectId = firebase.Value.ProjectId;
                o.Authority = firebase.Value.Issuer;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = firebase.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = projectId,
                    ValidateLifetime = true,
                    // Firebase signs ID tokens with RS256 only.
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    NameClaimType = CurrentUserMiddleware.UidClaim,
                };
            });

        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, AdminAuthorizationHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Admin, p => p.RequireAuthenticatedUser().AddRequirements(new AdminRequirement()));
    }

    private static void AddRateLimits(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection(RateLimitOptions.Section).Get<RateLimitOptions>() ?? new RateLimitOptions();
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => Window(limits.GlobalPerMinute)));
            o.AddPolicy(RateLimitPolicies.Write, context =>
                RateLimitPartition.GetFixedWindowLimiter(PartitionKey(context), _ => Window(limits.WritePerMinute)));
            o.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
                var problem = new ProblemDetails { Status = StatusCodes.Status429TooManyRequests, Detail = "Too many requests. Try again later." };
                problem.Extensions["code"] = "rate_limited";
                await context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>()
                    .WriteAsync(new ProblemDetailsContext { HttpContext = context.HttpContext, ProblemDetails = problem });
            };
        });

        static FixedWindowRateLimiterOptions Window(int permits) =>
            new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

        static string PartitionKey(HttpContext context) =>
            context.User.FindFirstValue(CurrentUserMiddleware.UidClaim) is { } uid
                ? "uid:" + uid
                : "ip:" + (context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
    }

    private static void AddApi(this IServiceCollection services)
    {
        services.AddControllers(o =>
            {
                o.Filters.Add<ValidationFilter>();
                // FluentValidation is the only validation path.
                o.ModelValidatorProviders.Clear();
            })
            .AddJsonOptions(o => ConfigureJson(o.JsonSerializerOptions));

        services.AddValidatorsFromAssemblyContaining<Program>(includeInternalTypes: true);
        services.AddProblemDetails(o => o.CustomizeProblemDetails = context =>
        {
            var problem = context.ProblemDetails;
            problem.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
            if (!problem.Extensions.ContainsKey("code"))
                problem.Extensions["code"] = DefaultCode(problem.Status ?? context.HttpContext.Response.StatusCode);
        });
        services.AddOpenApi("v1", o =>
        {
            o.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            o.AddOperationTransformer<BearerSecurityRequirementTransformer>();
        });
    }

    public static void ConfigureJson(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    private static string DefaultCode(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "validation_failed",
        StatusCodes.Status401Unauthorized => "unauthorized",
        StatusCodes.Status403Forbidden => "forbidden",
        StatusCodes.Status404NotFound => "not_found",
        StatusCodes.Status405MethodNotAllowed => "method_not_allowed",
        StatusCodes.Status415UnsupportedMediaType => "unsupported_media_type",
        StatusCodes.Status429TooManyRequests => "rate_limited",
        >= 500 => "internal_error",
        _ => "error",
    };

    private static void AddForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection(ForwardedHeadersSettings.Section).Get<ForwardedHeadersSettings>() ?? new ForwardedHeadersSettings();
        services.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
            var proxies = settings.KnownProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (proxies.Length > 0)
            {
                foreach (var proxy in proxies)
                    o.KnownProxies.Add(IPAddress.Parse(proxy));
                return;
            }
            // nginx on the Docker host reaches the container through the bridge gateway (private address).
            foreach (var network in new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "fc00::/7" })
                o.KnownIPNetworks.Add(IPNetwork.Parse(network));
        });
    }
}
