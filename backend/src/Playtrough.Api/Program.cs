using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Playtrough.Api.Data;
using Playtrough.Api.Models;
using Playtrough.Api.OpenApi;
using Playtrough.Api.Repositories.Implementations;
using Playtrough.Api.Repositories.Interfaces;
using Playtrough.Api.Services.Implementations;
using Playtrough.Api.Services.Interfaces;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();
builder.Services.AddProblemDetails();

// EF Core - MySQL (Oracle provider MySql.EntityFrameworkCore).
// This provider exposes no ServerVersion/AutoDetect API: migration generation never
// needs a live server, and there is no server version to pin.
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:DefaultConnection' is not configured.")));

// ASP.NET Core Identity (int keys).
// NIST SP 800-63B: length dominates strength, so a required symbol is NOT enforced.
builder.Services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
{
    options.User.RequireUniqueEmail = true;
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
.AddDefaultTokenProviders();

builder.Services.AddSingleton<IValidateOptions<EmailSettings>, EmailSettingsValidator>();
builder.Services.AddOptions<EmailSettings>()
    .Bind(builder.Configuration.GetSection(EmailSettings.SectionName))
    .ValidateOnStart();
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
builder.Services.AddSingleton<IPasswordResetQueue, PasswordResetQueue>();
builder.Services.AddHostedService<PasswordResetWorker>();
builder.Services.AddOptions<PasswordResetSettings>()
    .Bind(builder.Configuration.GetSection(PasswordResetSettings.SectionName))
    .ValidateDataAnnotations()
    .Validate(settings => Uri.TryCreate(settings.FrontendBaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) && string.IsNullOrEmpty(uri.UserInfo),
        "PasswordReset:FrontendBaseUrl must be an absolute HTTP(S) URL without credentials, query or fragment.")
    .ValidateOnStart();
builder.Services.AddOptions<DataProtectionTokenProviderOptions>()
    .Configure<IOptions<PasswordResetSettings>>((options, settings) =>
        options.TokenLifespan = TimeSpan.FromMinutes(settings.Value.TokenLifespanMinutes));

builder.Services.AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .ValidateDataAnnotations()
    .Validate(settings => Encoding.UTF8.GetByteCount(settings.Key) >= 32 &&
        !settings.Key.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase),
        "Jwt:Key must be a secret of at least 32 bytes, supplied via user-secrets or environment variables.")
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
builder.Services.AddAuthorization();

// Application services
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
builder.Services.AddScoped<ITokenService, TokenService>();

// Swagger / OpenAPI
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<RegisterEndpointExamplesFilter>();
    options.OperationFilter<LoginEndpointExamplesFilter>();
    options.OperationFilter<RefreshEndpointExamplesFilter>();
    options.OperationFilter<JsonOnlyContentFilter>();
});

// CORS — Angular dev server
builder.Services.AddCors(o =>
{
    o.AddPolicy("AngularDev", p =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:4200" };
        p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
    });
});

// SignalR services (no hub yet — Sprint 0 registers the framework only)
builder.Services.AddSignalR();

// Health checks
builder.Services.AddHealthChecks();

var app = builder.Build();

// Pipeline
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AngularDev");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();

// Exposes the entry point to WebApplicationFactory without changing the API pipeline.
public partial class Program { }
