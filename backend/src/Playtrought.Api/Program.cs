using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Playtrought.Api.Data;
using Playtrought.Api.Models;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();

// EF Core - MySQL (Oracle provider MySql.EntityFrameworkCore).
// This provider exposes no ServerVersion/AutoDetect API: migration generation never
// needs a live server, and there is no server version to pin.
builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseMySQL(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:DefaultConnection' is not configured.")));

// ASP.NET Core Identity (int keys). Password policy and auto-login/auth middleware come with US1.
builder.Services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
{
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// Swagger / OpenAPI
builder.Services.AddSwaggerGen();

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
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
