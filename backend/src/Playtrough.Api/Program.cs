using System.Text;
using System.Net;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.OpenApi;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;
using Playtrough.Api.Authorization;
using Playtrough.Api.Controllers;
using Playtrough.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    var defaultResponse = options.InvalidModelStateResponseFactory;
    options.InvalidModelStateResponseFactory = context =>
    {
        var response = defaultResponse(context);
        if (context.ActionDescriptor is ControllerActionDescriptor action &&
            action.ControllerTypeInfo.AsType() == typeof(AdminUsersController) &&
            action.ActionName == nameof(AdminUsersController.ChangeRole) &&
            context.ModelState.Any(entry => entry.Value?.Errors.Count > 0 &&
                !string.Equals(entry.Key, "id", StringComparison.OrdinalIgnoreCase)) &&
            response is ObjectResult { Value: ProblemDetails problem })
            problem.Extensions["code"] = "invalid_role";
        return response;
    };
});
builder.Services.AddProblemDetails();

builder.Services.AddOptions<TransportSettings>()
    .BindSafely(builder.Configuration.GetSection(TransportSettings.SectionName))
    .ValidateDataAnnotations()
    .Validate(settings => settings.KnownProxies.All(value => IPAddress.TryParse(value, out _)),
        "Transport:KnownProxies must contain valid IP addresses.")
    .Validate(settings => settings.KnownNetworks.All(value => System.Net.IPNetwork.TryParse(value, out _)),
        "Transport:KnownNetworks must contain valid CIDR networks.")
    .ValidateOnStart();
builder.Services.AddOptions<KestrelServerOptions>().Configure<IOptions<TransportSettings>>((options, settings) =>
{
    options.AddServerHeader = false;
    options.Limits.MaxRequestBodySize = settings.Value.MaxRequestBodySizeBytes;
});
builder.Services.AddOptions<HttpsRedirectionOptions>().Configure<IOptions<TransportSettings>>((options, settings) =>
    options.HttpsPort = settings.Value.HttpsPort);
builder.Services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(30));
// PostConfigure overrides the framework's automatic forwarded-header defaults too.
builder.Services.AddOptions<ForwardedHeadersOptions>().PostConfigure<IOptions<TransportSettings>>((options, settings) =>
{
    options.KnownProxies.Clear();
    options.KnownIPNetworks.Clear();
    foreach (var proxy in settings.Value.KnownProxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
    foreach (var network in settings.Value.KnownNetworks) options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
    options.ForwardLimit = settings.Value.ForwardLimit;
    options.ForwardedHeaders = options.KnownProxies.Count + options.KnownIPNetworks.Count == 0
        ? ForwardedHeaders.None : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// EF Core - MySQL (Oracle provider MySql.EntityFrameworkCore).
// This provider exposes no ServerVersion/AutoDetect API: migration generation never
// needs a live server, and there is no server version to pin.
builder.Services.AddSingleton<IValidateOptions<ConnectionStringSettings>, ConnectionStringSettingsValidator>();
builder.Services.AddOptions<ConnectionStringSettings>()
    .BindSafely(builder.Configuration.GetSection(ConnectionStringSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseMySQL(services.GetRequiredService<IOptions<ConnectionStringSettings>>().Value.DefaultConnection));

// ASP.NET Core Identity (int keys).
// NIST SP 800-63B: length dominates strength, so a required symbol is NOT enforced.
builder.Services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Tokens.EmailConfirmationTokenProvider = EmailConfirmationTokenProvider.ProviderName;
    options.Lockout.AllowedForNewUsers = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Password.RequiredLength = 8;
    options.Password.RequireUppercase = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = false;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders()
.AddTokenProvider<EmailConfirmationTokenProvider>(EmailConfirmationTokenProvider.ProviderName);

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IEmailConfirmationQueue, EmailConfirmationQueue>();
builder.Services.AddHostedService<EmailConfirmationWorker>();
builder.Services.AddOptions<EmailConfirmationSettings>()
    .BindSafely(builder.Configuration.GetSection(EmailConfirmationSettings.SectionName))
    .ValidateDataAnnotations().ValidateOnStart();
builder.Services.AddOptions<EmailConfirmationTokenProviderOptions>()
    .Configure<IOptions<EmailConfirmationSettings>>((options, settings) =>
    {
        options.Name = EmailConfirmationTokenProvider.ProviderName;
        options.TokenLifespan = TimeSpan.FromMinutes(settings.Value.TokenLifespanMinutes);
    });

builder.Services.AddSingleton<IValidateOptions<EmailSettings>, EmailSettingsValidator>();
builder.Services.AddOptions<EmailSettings>()
    .BindSafely(builder.Configuration.GetSection(EmailSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<IPasswordResetQueue, PasswordResetQueue>();
builder.Services.AddHostedService<PasswordResetWorker>();
builder.Services.AddOptions<PasswordResetSettings>()
    .BindSafely(builder.Configuration.GetSection(PasswordResetSettings.SectionName))
    .ValidateDataAnnotations()
    .Validate(settings => Uri.TryCreate(settings.FrontendBaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo),
        "PasswordReset:FrontendBaseUrl must be an absolute HTTP(S) URL without credentials, query or fragment.")
    .ValidateOnStart();
builder.Services.AddOptions<DataProtectionTokenProviderOptions>()
    .Configure<IOptions<PasswordResetSettings>>((options, settings) =>
        options.TokenLifespan = TimeSpan.FromMinutes(settings.Value.TokenLifespanMinutes));

builder.Services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
builder.Services.AddOptions<JwtSettings>()
    .BindSafely(builder.Configuration.GetSection(JwtSettings.SectionName))
    .ValidateOnStart();

builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer();

builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, settings) =>
    {
        var jwt = settings.Value;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = "unique_name",
            RoleClaimType = "role",
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.Headers.WWWAuthenticate = "Bearer";
                await Results.Problem(statusCode: StatusCodes.Status401Unauthorized,
                    title: "Unauthorized", detail: "A valid access token is required.")
                    .ExecuteAsync(context.HttpContext);
            },
            OnForbidden = context => Results.Problem(statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden", detail: "You do not have permission to access this resource.")
                .ExecuteAsync(context.HttpContext),
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
    options.AddPolicy(AppPolicies.RequireUser, policy =>
        policy.RequireAuthenticatedUser().RequireRole(AppRoles.User, AppRoles.Moderator, AppRoles.Admin));
    options.AddPolicy(AppPolicies.RequireModerator, policy =>
        policy.RequireAuthenticatedUser().RequireRole(AppRoles.Moderator, AppRoles.Admin));
    options.AddPolicy(AppPolicies.RequireAdmin, policy =>
        policy.RequireAuthenticatedUser().RequireRole(AppRoles.Admin));
});
builder.Services.AddOptions<AdminSeedSettings>()
    .BindSafely(builder.Configuration.GetSection(AdminSeedSettings.SectionName));
builder.Services.AddScoped<IdentitySeeder>();

// Application services
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAdminUserRepository, AdminUserRepository>();
builder.Services.AddScoped<IAdminUserService, AdminUserService>();

// Swagger / OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Pegá el accessToken recibido en el login, sin agregar el prefijo Bearer. No uses el refreshToken.",
    });
    // Match the API's authenticated-by-default behavior; anonymous operations override this below.
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(JwtBearerDefaults.AuthenticationScheme, document)] = [],
    });
    options.OperationFilter<AnonymousSecurityFilter>();
    options.OperationFilter<RegisterEndpointExamplesFilter>();
    options.OperationFilter<LoginEndpointExamplesFilter>();
    options.OperationFilter<RefreshEndpointExamplesFilter>();
    options.OperationFilter<JsonOnlyContentFilter>();
});

// Explicit origins only; an empty production configuration grants no CORS access.
builder.Services.AddOptions<CorsSettings>()
    .BindSafely(builder.Configuration.GetSection(CorsSettings.SectionName))
    .Validate(settings => settings.AllowedOrigins.All(CorsSettings.IsOrigin),
        "Cors:AllowedOrigins must contain absolute HTTP(S) origins without wildcards, credentials, paths, query or fragment.")
    .ValidateOnStart();
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IOptions<CorsSettings>>((options, settings) =>
{
    options.AddPolicy("AngularDev", policy =>
    {
        policy.WithOrigins(settings.Value.AllowedOrigins.Select(origin => origin.TrimEnd('/')).ToArray())
            .WithMethods("GET", "POST", "PUT", "DELETE", "OPTIONS")
            .WithHeaders("Authorization", "Content-Type");
    });
});

// SignalR services (no hub yet — Sprint 0 registers the framework only)
builder.Services.AddSignalR();

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Validate before the seeder can open a database connection. Host startup also
// runs this validator, but that normally happens after this initialization block.
app.Services.GetRequiredService<IStartupValidator>().Validate();

await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().InitializeAsync();

// Pipeline
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseExceptionHandler();
app.UseForwardedHeaders();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AngularDev");
app.UseMiddleware<RequestBodyLimitMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/test/user", () => Results.Ok(new { message = "User access granted." }))
        .RequireAuthorization(AppPolicies.RequireUser);
    app.MapGet("/api/test/moderator", () => Results.Ok(new { message = "Moderator access granted." }))
        .RequireAuthorization(AppPolicies.RequireModerator);
    app.MapGet("/api/test/admin", () => Results.Ok(new { message = "Admin access granted." }))
        .RequireAuthorization(AppPolicies.RequireAdmin);
}
// Public liveness check: no database, credentials or internal state in the response.
app.MapHealthChecks("/health").AllowAnonymous();
app.Run();

// Exposes the entry point to WebApplicationFactory without changing the API pipeline.
public partial class Program { }
