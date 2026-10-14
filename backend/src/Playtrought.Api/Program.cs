using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Playtrought.Api.Data;
using Playtrought.Api.Models;
using Playtrought.Api.OpenApi;
using Playtrought.Api.Repositories.Implementations;
using Playtrought.Api.Repositories.Interfaces;
using Playtrought.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

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
app.UseSwagger();
app.UseSwaggerUI();
app.UseCors("AngularDev");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
