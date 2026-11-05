using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using PrayGamex.Application.Abstractions;
using PrayGamex.Application.Services;
using PrayGamex.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

var jwtKey = cfg["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32) throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres (configúralo por variable de entorno).");
var audience = cfg["Jwt:Audience"] ?? "PrayGamex";

builder.Services.AddInfrastructure(cfg);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

TokenValidationParameters Validation(string aud) => new()
{
    ValidIssuer = cfg["Jwt:Issuer"], ValidAudience = aud, ValidateIssuer = true, ValidateAudience = true,
    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ClockSkew = TimeSpan.FromSeconds(30),
    RoleClaimType = ClaimTypes.Role, NameClaimType = "sub",
};

// Una sesión cerrada (logout, o desplazada por una 3.ª sesión) invalida de inmediato sus tokens.
async Task CheckSession(TokenValidatedContext ctx)
{
    var ok = Guid.TryParse(ctx.Principal?.FindFirstValue("sid"), out var sid)
        && await ctx.HttpContext.RequestServices.GetRequiredService<IUserRepository>().IsSessionActiveAsync(sid, ctx.HttpContext.RequestAborted);
    if (!ok) ctx.Fail("Sesión cerrada.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = Validation(audience);
        o.Events = new JwtBearerEvents { OnTokenValidated = CheckSession };
    })
    // Token de lanzamiento (audiencia distinta): solo se acepta donde se indique el esquema "Game".
    .AddJwtBearer("Game", o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = Validation(audience + ".Game");
        o.Events = new JwtBearerEvents { OnTokenValidated = CheckSession };
    });
builder.Services.AddAuthorization();
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(cfg.GetSection("Cors:Origins").Get<string[]>() ?? []).AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "x",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var status = ex is AppException ae ? ae.Status : 500;
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = ex is AppException ? ex.Message : "Error interno del servidor." });
}));
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");

await app.Services.InitializeDatabaseAsync(cfg);
app.Run();

public partial class Program;
