using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using PrayGamex.Application.Abstractions;
using PrayGamex.Application.Services;
using PrayGamex.Infrastructure.Payments;
using PrayGamex.Infrastructure.Persistence;
using PrayGamex.Infrastructure.Security;
namespace PrayGamex.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection s, IConfiguration cfg)
    {
        s.AddSingleton<ISqlConnectionFactory, SqlConnectionFactory>();
        s.AddScoped<IUserRepository, UserRepository>();
        s.AddScoped<ICatalogRepository, CatalogRepository>();
        s.AddScoped<IMenuRepository, MenuRepository>();
        s.AddScoped<IProgressRepository, ProgressRepository>();
        s.AddScoped<IPaymentRepository, PaymentRepository>();
        s.AddScoped<IAdminRepository, AdminRepository>();

        s.Configure<JwtOptions>(cfg.GetSection("Jwt"));
        s.Configure<MercadoPagoOptions>(cfg.GetSection("MercadoPago"));
        s.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        s.AddSingleton<ITokenService, JwtTokenService>();

        s.AddHttpClient<IPaymentGateway, MercadoPagoGateway>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<MercadoPagoOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", o.AccessToken);
        }).AddResilienceHandler("mercadopago", b => // Polly v8: timeout total + reintentos + circuit breaker + timeout por intento
        {
            b.AddTimeout(TimeSpan.FromSeconds(30));
            b.AddRetry(new HttpRetryStrategyOptions { MaxRetryAttempts = 3, BackoffType = DelayBackoffType.Exponential, UseJitter = true, Delay = TimeSpan.FromMilliseconds(500) });
            b.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions { SamplingDuration = TimeSpan.FromSeconds(30), FailureRatio = 0.5, MinimumThroughput = 5, BreakDuration = TimeSpan.FromSeconds(30) });
            b.AddTimeout(TimeSpan.FromSeconds(10));
        });

        s.AddScoped<AuthService>(); s.AddScoped<CatalogService>(); s.AddScoped<MenuService>();
        s.AddScoped<ProgressService>(); s.AddScoped<PaymentService>(); s.AddScoped<AdminService>();
        return s;
    }

    public static Task InitializeDatabaseAsync(this IServiceProvider sp, IConfiguration cfg) =>
        DbInitializer.RunAsync(cfg, sp.GetRequiredService<ISqlConnectionFactory>());
}
