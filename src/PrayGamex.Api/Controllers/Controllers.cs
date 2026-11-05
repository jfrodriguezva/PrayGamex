using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using PrayGamex.Application.Abstractions;
using PrayGamex.Infrastructure.Payments;
using PrayGamex.Application.Dtos;
using PrayGamex.Application.Services;
using PrayGamex.Domain.Enums;
namespace PrayGamex.Api.Controllers;

public abstract class ApiController : ControllerBase
{
    protected Guid UserId => Guid.Parse(User.FindFirstValue("sub")!);
}

[ApiController, Route("api/auth"), EnableRateLimiting("auth")]
public class AuthController(AuthService auth) : ControllerBase
{
    [HttpPost("register")] public Task<AuthResponse> Register(RegisterRequest r, CancellationToken ct) => auth.RegisterAsync(r, ct);
    [HttpPost("login")] public Task<AuthResponse> Login(LoginRequest r, CancellationToken ct) => auth.LoginAsync(r, ct);
    [HttpPost("refresh")] public Task<AuthResponse> Refresh(RefreshRequest r, CancellationToken ct) => auth.RefreshAsync(r.RefreshToken, ct);
    [HttpPost("logout")] public async Task<IActionResult> Logout(RefreshRequest r, CancellationToken ct) { await auth.LogoutAsync(r.RefreshToken, ct); return NoContent(); }
}

[ApiController, Route("api/menu")]
public class MenuController(MenuService menu) : ControllerBase
{
    /// <summary>Menú configurado en BD. device=mobile|tablet|desktop (si se omite se detecta por User-Agent).</summary>
    [HttpGet]
    public async Task<IReadOnlyList<MenuItemDto>> Get([FromQuery] string? device, CancellationToken ct)
    {
        var d = Enum.TryParse<DeviceType>(device, true, out var parsed) ? parsed : Detect(Request.Headers.UserAgent.ToString());
        return await menu.GetAsync(d, User.Identity?.IsAuthenticated == true, ct);
    }

    private static DeviceType Detect(string ua) =>
        ua.Contains("iPad", StringComparison.OrdinalIgnoreCase) || ua.Contains("Tablet", StringComparison.OrdinalIgnoreCase) ? DeviceType.Tablet
        : ua.Contains("Mobi", StringComparison.OrdinalIgnoreCase) || ua.Contains("Android", StringComparison.OrdinalIgnoreCase) ? DeviceType.Mobile
        : DeviceType.Desktop;
}

[ApiController, Route("api"), Authorize]
public class GamesController(CatalogService catalog, ITokenService tokens) : ApiController
{
    [HttpGet("games")] public Task<IReadOnlyList<GameDto>> Search([FromQuery] string? q, [FromQuery] Guid? categoryId, [FromQuery] bool favorites, CancellationToken ct) => catalog.SearchAsync(UserId, q, categoryId, favorites, ct);
    [HttpGet("categories")] public Task<IReadOnlyList<CategoryDto>> Categories(CancellationToken ct) => catalog.CategoriesAsync(ct);
    [HttpGet("categories/{id:guid}")] public Task<CategoryDetailDto> Category(Guid id, CancellationToken ct) => catalog.CategoryDetailAsync(UserId, id, ct);
    [HttpGet("featured")] public Task<IReadOnlyList<GameDto>> Featured(CancellationToken ct) => catalog.FeaturedTodayAsync(UserId, ct);
    [HttpGet("challenges")] public Task<IReadOnlyList<ChallengeDto>> Challenges([FromQuery] Guid? gameId, CancellationToken ct) => catalog.ChallengesAsync(gameId, ct);
    [HttpGet("recents")] public Task<IReadOnlyList<GameDto>> Recents(CancellationToken ct) => catalog.RecentsAsync(UserId, ct);
    [HttpPost("games/{id:guid}/favorite")] public async Task<object> Favorite(Guid id, CancellationToken ct) => new { isFavorite = await catalog.ToggleFavoriteAsync(UserId, id, ct) };

    /// <summary>Valida el permiso y devuelve la URL del juego con un token de lanzamiento (?pgt=) que el servidor del juego puede verificar.</summary>
    [HttpPost("games/{id:guid}/launch")]
    public async Task<LaunchResponse> Launch(Guid id, CancellationToken ct)
    {
        var url = await catalog.LaunchAsync(UserId, id, ct);
        var token = tokens.CreateGameToken(UserId, Guid.Parse(User.FindFirstValue("sid")!), id);
        return new(url + (url.Contains('?') ? "&" : "?") + "pgt=" + Uri.EscapeDataString(token));
    }
}

/// <summary>Avance por juego. Acepta el token de usuario o el token de lanzamiento (solo para el juego al que pertenece).</summary>
[ApiController, Route("api/games/{id:guid}/progress"), Authorize(AuthenticationSchemes = "Bearer,Game")]
public class ProgressController(ProgressService progress) : ApiController
{
    private void CheckGame(Guid id)
    {
        var gid = User.FindFirstValue("gid");
        if (gid is not null && !string.Equals(gid, id.ToString(), StringComparison.OrdinalIgnoreCase)) throw new AppException("Token de otro juego.", 403);
    }

    [HttpGet] public async Task<IActionResult> Get(Guid id, CancellationToken ct) { CheckGame(id); return (await progress.GetAsync(UserId, id, ct)) is { } p ? Ok(p) : NoContent(); }
    [HttpPut] public Task<ProgressDto> Save(Guid id, SaveProgressRequest r, CancellationToken ct) { CheckGame(id); return progress.SaveAsync(UserId, id, r, ct); }
}

/// <summary>Los servidores de juegos validan aquí el token de lanzamiento que recibieron en ?pgt=.</summary>
[ApiController, Route("api/launch"), EnableRateLimiting("auth")]
public class LaunchController(ITokenService tokens, IUserRepository users) : ControllerBase
{
    [HttpPost("verify")]
    public async Task<VerifyResponse> Verify(VerifyRequest r, CancellationToken ct)
    {
        var v = tokens.ValidateGameToken(r.Token);
        if (v is null || !await users.IsSessionActiveAsync(v.Value.SessionId, ct)) return new(false, null, null);
        return new(true, v.Value.UserId, v.Value.GameId);
    }
}

/// <summary>Administración de juegos, precios y permisos (solo cuentas Admin).</summary>
[ApiController, Route("api/admin"), Authorize(Roles = "Admin")]
public class AdminController(AdminService admin) : ControllerBase
{
    [HttpGet("games")] public Task<IReadOnlyList<AdminGameDto>> Games(CancellationToken ct) => admin.ListAsync(ct);
    [HttpPut("games")] public async Task<IActionResult> Upsert(UpsertGameRequest r, CancellationToken ct) { await admin.UpsertAsync(r, ct); return NoContent(); }
    [HttpPost("games/{slug}/active")] public async Task<IActionResult> Active(string slug, ActiveRequest r, CancellationToken ct) { await admin.SetActiveAsync(slug, r.IsActive, ct); return NoContent(); }
    [HttpPost("grants")] public async Task<IActionResult> Grant(GrantRequest r, CancellationToken ct) { await admin.GrantAsync(r, ct); return NoContent(); }
}

[ApiController, Route("api/me"), Authorize]
public class MeController(AuthService auth) : ApiController
{
    [HttpGet] public Task<UserDto> Get(CancellationToken ct) => auth.MeAsync(UserId, ct);
}

[ApiController, Route("api/payments")]
public class PaymentsController(PaymentService payments, IOptions<MercadoPagoOptions> mp, IWebHostEnvironment env) : ApiController
{
    [HttpGet("plans"), Authorize]
    public Task<IReadOnlyList<PlanDto>> Plans(CancellationToken ct) => payments.PlansAsync(ct);

    /// <summary>Compra de un juego (GameId) o de una membresía (PlanCode). El monto se toma de la BD.</summary>
    [HttpPost("checkout"), Authorize]
    public Task<CheckoutResponse> Checkout(CheckoutRequest r, CancellationToken ct) => payments.CheckoutAsync(UserId, r, ct);

    /// <summary>Webhook de Mercado Pago: firma x-signature verificada y, además, el pago se re-consulta a la API de MP (no se confía en el cuerpo).</summary>
    [HttpPost("webhook"), AllowAnonymous]
    public async Task<IActionResult> Webhook([FromQuery(Name = "data.id")] string? dataId, [FromQuery] string? type, CancellationToken ct)
    {
        var secret = mp.Value.WebhookSecret;
        if (!string.IsNullOrEmpty(secret))
        {
            if (!MercadoPagoSignature.Verify(secret, Request.Headers["x-signature"], Request.Headers["x-request-id"], dataId)) return Unauthorized();
        }
        else if (!env.IsDevelopment()) return StatusCode(503); // sin secreto configurado solo se aceptan webhooks en Development
        if (type == "payment" && !string.IsNullOrWhiteSpace(dataId)) await payments.HandleWebhookAsync(dataId, ct);
        return Ok();
    }
}
