using PrayGamex.Application.Abstractions;
using PrayGamex.Application.Dtos;
using PrayGamex.Domain.Entities;
using PrayGamex.Domain.Enums;
namespace PrayGamex.Application.Services;

public class AppException(string message, int status = 400) : Exception(message) { public int Status { get; } = status; }

public class AuthService(IUserRepository users, IPasswordHasher hasher, ITokenService tokens)
{
    public const int MaxSessionsPerUser = 2;
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(14);

    public async Task<AuthResponse> RegisterAsync(RegisterRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (!email.Contains('@') || r.Password.Length < 8 || string.IsNullOrWhiteSpace(r.Name)) throw new AppException("Datos inválidos: email, nombre y contraseña de al menos 8 caracteres.");
        if (await users.GetByEmailAsync(email, ct) is not null) throw new AppException("El email ya está registrado.", 409);
        var user = new User { Email = email, Name = r.Name.Trim(), PasswordHash = hasher.Hash(r.Password) };
        await users.AddAsync(user, ct);
        return await IssueAsync(user, await NewSessionAsync(user, ct), ct);
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest r, CancellationToken ct)
    {
        var user = await users.GetByEmailAsync(r.Email.Trim().ToLowerInvariant(), ct);
        if (user is null || !hasher.Verify(r.Password, user.PasswordHash)) throw new AppException("Credenciales inválidas.", 401);
        return await IssueAsync(user, await NewSessionAsync(user, ct), ct); // cierra la sesión más antigua si ya había 2
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        var rt = await users.GetRefreshTokenAsync(tokens.HashToken(refreshToken), ct);
        if (rt is null) throw new AppException("Refresh token inválido.", 401);
        if (!rt.IsActive || !await users.RevokeRefreshTokenAsync(rt.Id, ct))
        {
            await users.RevokeSessionAsync(rt.SessionId, ct); // token reutilizado: posible robo, se cierra la sesión
            throw new AppException("Refresh token inválido.", 401);
        }
        if (!await users.IsSessionActiveAsync(rt.SessionId, ct)) throw new AppException("Sesión cerrada.", 401);
        var user = await users.GetByIdAsync(rt.UserId, ct) ?? throw new AppException("Usuario no encontrado.", 401);
        await users.ExtendSessionAsync(rt.SessionId, DateTime.UtcNow + SessionLifetime, ct);
        return await IssueAsync(user, rt.SessionId, ct);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        var rt = await users.GetRefreshTokenAsync(tokens.HashToken(refreshToken), ct);
        if (rt is not null) await users.RevokeSessionAsync(rt.SessionId, ct);
    }

    private Task<Guid> NewSessionAsync(User user, CancellationToken ct) =>
        users.CreateSessionAsync(user.Id, MaxSessionsPerUser, DateTime.UtcNow + SessionLifetime, ct);

    public async Task<UserDto> MeAsync(Guid userId, CancellationToken ct) =>
        await ToDtoAsync(await users.GetByIdAsync(userId, ct) ?? throw new AppException("Usuario no encontrado.", 404), ct);

    private async Task<AuthResponse> IssueAsync(User user, Guid sessionId, CancellationToken ct)
    {
        var (token, hash) = tokens.CreateRefreshToken();
        await users.AddRefreshTokenAsync(new RefreshToken { UserId = user.Id, SessionId = sessionId, TokenHash = hash, ExpiresAt = DateTime.UtcNow + SessionLifetime }, ct);
        return new AuthResponse(tokens.CreateAccessToken(user, sessionId), token, await ToDtoAsync(user, ct));
    }

    private async Task<UserDto> ToDtoAsync(User u, CancellationToken ct)
    {
        var m = await users.GetMembershipAsync(u.Id, ct);
        return new(u.Id, u.Email, u.Name, u.AccountType, m is null ? null : new MembershipDto(m.Plan, m.StartsAt, m.ExpiresAt, m.IsActive));
    }
}

public class CatalogService(ICatalogRepository catalog, IUserRepository users)
{
    /// <summary>Premium (membresía vigente) y administradores acceden a todos los juegos.</summary>
    private async Task<bool> HasPremiumAsync(Guid userId, CancellationToken ct)
    {
        var u = await users.GetByIdAsync(userId, ct);
        if (u?.AccountType == AccountType.Admin) return true;
        return (await users.GetMembershipAsync(userId, ct))?.IsActive == true;
    }

    /// <summary>Demo: abierto. Pro: requiere membresía vigente o permiso por juego (compra / asignación).</summary>
    private static bool IsLocked(GameView g, bool premium) => g.IsPro && !g.Owned && !premium;

    private static GameDto Map(GameView g, bool premium)
    {
        var locked = IsLocked(g, premium);
        return new(g.Id, g.Name, g.Slug, g.Description, g.IconUrl, locked ? "" : g.LaunchUrl, g.CategoryId, g.CategoryName,
            g.IsPro, g.Price, g.Currency, g.AccessDays, g.IsFavorite, locked);
    }

    private async Task<IReadOnlyList<GameDto>> MapAsync(Guid userId, IReadOnlyList<GameView> games, CancellationToken ct)
    {
        var premium = await HasPremiumAsync(userId, ct);
        return games.Select(g => Map(g, premium)).ToList();
    }

    public async Task<IReadOnlyList<GameDto>> SearchAsync(Guid userId, string? q, Guid? categoryId, bool onlyFavorites, CancellationToken ct) =>
        await MapAsync(userId, await catalog.SearchAsync(userId, q, categoryId, onlyFavorites, false, ct), ct);

    public async Task<IReadOnlyList<CategoryDto>> CategoriesAsync(CancellationToken ct) =>
        (await catalog.CategoriesAsync(ct)).Select(c => new CategoryDto(c.Id, c.Name, c.Slug)).ToList();

    public async Task<CategoryDetailDto> CategoryDetailAsync(Guid userId, Guid categoryId, CancellationToken ct)
    {
        var c = await catalog.GetCategoryAsync(categoryId, ct) ?? throw new AppException("Categoría no encontrada.", 404);
        return new(new CategoryDto(c.Id, c.Name, c.Slug), await SearchAsync(userId, null, categoryId, false, ct));
    }

    public async Task<IReadOnlyList<GameDto>> FeaturedTodayAsync(Guid userId, CancellationToken ct) =>
        await MapAsync(userId, await catalog.SearchAsync(userId, null, null, false, true, ct), ct);

    public async Task<IReadOnlyList<ChallengeDto>> ChallengesAsync(Guid? gameId, CancellationToken ct) =>
        (await catalog.ChallengesAsync(gameId, ct)).Select(c => new ChallengeDto(c.Id, c.GameId, c.GameName, c.Title, c.Detail, c.Points)).ToList();

    public async Task<IReadOnlyList<GameDto>> RecentsAsync(Guid userId, CancellationToken ct) =>
        await MapAsync(userId, await catalog.RecentsAsync(userId, ct), ct);

    public Task<bool> ToggleFavoriteAsync(Guid userId, Guid gameId, CancellationToken ct) => catalog.ToggleFavoriteAsync(userId, gameId, ct);

    /// <summary>Lanza 403 si el juego es Pro y el usuario no tiene membresía ni permiso por juego.</summary>
    public async Task EnsureAccessAsync(Guid userId, Guid gameId, CancellationToken ct)
    {
        var g = await catalog.GetGameAsync(userId, gameId, ct);
        if (g is null || !g.IsActive) throw new AppException("Juego no encontrado.", 404);
        if (IsLocked(g, await HasPremiumAsync(userId, ct))) throw new AppException("Este juego es Pro: compra el juego o suscríbete.", 403);
    }

    /// <summary>Valida acceso (compra o membresía) y registra el juego como reciente. Devuelve la URL de lanzamiento.</summary>
    public async Task<string> LaunchAsync(Guid userId, Guid gameId, CancellationToken ct)
    {
        var g = await catalog.GetGameAsync(userId, gameId, ct);
        if (g is null || !g.IsActive) throw new AppException("Juego no encontrado.", 404);
        if (IsLocked(g, await HasPremiumAsync(userId, ct))) throw new AppException("Este juego es Pro: compra el juego o suscríbete.", 403);
        await catalog.TouchRecentAsync(userId, gameId, ct);
        return g.LaunchUrl;
    }
}

public class MenuService(IMenuRepository menu)
{
    public async Task<IReadOnlyList<MenuItemDto>> GetAsync(DeviceType device, bool authenticated, CancellationToken ct)
    {
        var items = await menu.GetAsync(device, authenticated, ct);
        MenuItemDto Build(MenuItem m) => new(m.Key, m.Label, m.Icon, m.Route, m.Order,
            items.Where(c => c.ParentId == m.Id).OrderBy(c => c.Order).Select(Build).ToList());
        return items.Where(m => m.ParentId == null).OrderBy(m => m.Order).Select(Build).ToList();
    }
}

public class ProgressService(IProgressRepository progress, CatalogService access)
{
    public async Task<ProgressDto?> GetAsync(Guid userId, Guid gameId, CancellationToken ct)
    {
        var p = await progress.GetAsync(userId, gameId, ct);
        return p is null ? null : new(p.GameId, p.Data, p.Score, p.Level, p.UpdatedAt);
    }

    public async Task<ProgressDto> SaveAsync(Guid userId, Guid gameId, SaveProgressRequest r, CancellationToken ct)
    {
        if (r.Data.Length > 256_000) throw new AppException("Datos de avance demasiado grandes.", 413);
        await access.EnsureAccessAsync(userId, gameId, ct); // 404 si no existe, 403 si es Pro sin permiso
        var p = await progress.UpsertAsync(userId, gameId, r.Data, r.Score, r.Level, ct);
        return new(p.GameId, p.Data, p.Score, p.Level, p.UpdatedAt);
    }
}

public class PaymentService(IPaymentRepository payments, ICatalogRepository catalog, IUserRepository users, IPaymentGateway gateway)
{
    public async Task<IReadOnlyList<PlanDto>> PlansAsync(CancellationToken ct) =>
        (await payments.PlansAsync(ct)).Select(p => new PlanDto(p.Code, p.Name, p.Price, p.Currency, p.Days)).ToList();

    /// <summary>El precio SIEMPRE sale de BD (Games.Price / MembershipPlans.Price); el cliente nunca envía montos.</summary>
    public async Task<CheckoutResponse> CheckoutAsync(Guid userId, CheckoutRequest r, CancellationToken ct)
    {
        var user = await users.GetByIdAsync(userId, ct) ?? throw new AppException("Usuario no encontrado.", 404);
        var payment = new Payment { UserId = userId };
        if (r.GameId is { } gid)
        {
            var g = await catalog.GetGameAsync(userId, gid, ct);
            if (g is null || !g.IsActive) throw new AppException("Juego no encontrado.", 404);
            if (!g.IsPro) throw new AppException("Este juego es demo y es gratuito.");
            if (g.Price <= 0) throw new AppException("Este juego Pro solo está disponible con suscripción.");
            if (g.Owned) throw new AppException("Ya tienes acceso vigente a este juego.", 409);
            payment.Kind = PaymentKind.Game; payment.GameId = g.Id; payment.Title = $"PrayGamex - {g.Name}";
            payment.Amount = g.Price; payment.Currency = g.Currency; payment.AccessDays = g.AccessDays;
        }
        else if (!string.IsNullOrWhiteSpace(r.PlanCode))
        {
            var p = await payments.GetPlanAsync(r.PlanCode, ct);
            if (p is null || !p.IsActive) throw new AppException("Plan inválido.");
            payment.Kind = PaymentKind.Membership; payment.PlanCode = p.Code; payment.Title = $"PrayGamex - {p.Name}";
            payment.Amount = p.Price; payment.Currency = p.Currency; payment.AccessDays = p.Days;
        }
        else throw new AppException("Indica GameId o PlanCode.");

        await payments.AddAsync(payment, ct);
        string prefId, url;
        try { (prefId, url) = await gateway.CreatePreferenceAsync(payment, user.Email, ct); }
        catch (Exception e) when (e is HttpRequestException or TimeoutException or InvalidOperationException || e.GetType().Name.Contains("BrokenCircuit") || e.GetType().Name.Contains("Timeout"))
        { throw new AppException("El servicio de pagos no está disponible por ahora. Intenta más tarde.", 502); }
        await payments.SetPreferenceAsync(payment.Id, prefId, ct);
        return new(payment.Id, prefId, url);
    }

    /// <summary>Procesa el aviso (webhook) de Mercado Pago consultando el pago a su API; es idempotente.</summary>
    public async Task HandleWebhookAsync(string externalPaymentId, CancellationToken ct)
    {
        var (status, reference, amount) = await gateway.GetPaymentAsync(externalPaymentId, ct);
        if (!Guid.TryParse(reference, out var paymentId)) return;
        var payment = await payments.GetAsync(paymentId, ct);
        if (payment is null || payment.Status == PaymentStatus.Approved) return;

        if (status == "approved")
        {
            if (amount < payment.Amount) return; // monto pagado menor al cobrado en BD: no se otorga acceso
            if (!await payments.TryApproveAsync(paymentId, externalPaymentId, ct)) return; // otro webhook ya lo procesó
            if (payment.Kind == PaymentKind.Game)
                await payments.GrantGameAsync(payment.UserId, payment.GameId!.Value, payment.AccessDays, ct);
            else
            {
                await users.ExtendMembershipAsync(payment.UserId, payment.PlanCode!, payment.AccessDays ?? 30, ct);
                var u = await users.GetByIdAsync(payment.UserId, ct);
                if (u?.AccountType == AccountType.Free) await users.SetAccountTypeAsync(u.Id, AccountType.Premium, ct);
            }
        }
        else if (status is "rejected" or "cancelled")
            await payments.SetStatusAsync(paymentId, status == "rejected" ? PaymentStatus.Rejected : PaymentStatus.Cancelled, externalPaymentId, ct);
    }
}

public partial class AdminService(IAdminRepository admin)
{
    private static readonly System.Text.RegularExpressions.Regex SlugRx = new("^[a-z0-9]+(-[a-z0-9]+)*$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public async Task<IReadOnlyList<AdminGameDto>> ListAsync(CancellationToken ct) =>
        (await admin.ListGamesAsync(ct)).Select(g => new AdminGameDto(g.Id, g.Name, g.Slug, g.LaunchUrl, g.CategorySlug, g.Description, g.IconUrl,
            g.IsPro, g.Price, g.Currency, g.AccessDays, g.IsActive, g.FeaturedToday)).ToList();

    public Task UpsertAsync(UpsertGameRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Length > 150) throw new AppException("Nombre inválido.");
        if (!SlugRx.IsMatch(r.Slug ?? "") || r.Slug!.Length > 150) throw new AppException("Slug inválido (minúsculas, números y guiones).");
        if (!Uri.TryCreate(r.LaunchUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new AppException("La URL del juego debe ser http(s) absoluta.");
        if (r.Price < 0 || r.Price > 1_000_000) throw new AppException("Precio inválido.");
        if (!r.IsPro && r.Price > 0) throw new AppException("Un juego demo no puede tener precio; márcalo como Pro.");
        if (r.AccessDays is <= 0) throw new AppException("AccessDays debe ser mayor a 0 o vacío (permanente).");
        var currency = (r.Currency ?? "MXN").ToUpperInvariant();
        if (currency.Length != 3) throw new AppException("Moneda inválida (3 letras).");
        return admin.UpsertGameAsync(new UpsertGame(r.Name.Trim(), r.Slug, r.LaunchUrl, r.CategorySlug, r.Description ?? "", r.IconUrl ?? "",
            r.IsPro, r.Price, currency, r.AccessDays, r.FeaturedToday, r.AddToMenu), ct);
    }

    public async Task SetActiveAsync(string slug, bool active, CancellationToken ct)
    { if (!await admin.SetActiveAsync(slug, active, ct)) throw new AppException("Juego no encontrado.", 404); }

    public async Task<IReadOnlyList<AdminUserDto>> UsersAsync(string? q, CancellationToken ct) =>
        (await admin.ListUsersAsync(string.IsNullOrWhiteSpace(q) ? null : q.Trim(), 200, ct))
            .Select(x => new AdminUserDto(x.Id, x.Email, x.Name, x.AccountType, x.CreatedAt, x.MembershipPlan, x.MembershipExpiresAt, x.ActiveSessions, x.GrantedGames)).ToList();

    /// <summary>Cambia el tipo de cuenta y cierra las sesiones del usuario para que el rol nuevo (o su pérdida) aplique de inmediato.</summary>
    public async Task SetAccountTypeAsync(Guid actingUserId, Guid userId, AccountType type, CancellationToken ct)
    {
        if (!Enum.IsDefined(type)) throw new AppException("Tipo de cuenta inválido.");
        if (actingUserId == userId) throw new AppException("No puedes cambiar tu propio tipo de cuenta.");
        if (!await admin.SetAccountTypeAsync(userId, type, ct)) throw new AppException("Usuario no encontrado.", 404);
        await admin.RevokeSessionsAsync(userId, ct);
    }

    public Task RevokeSessionsAsync(Guid userId, CancellationToken ct) => admin.RevokeSessionsAsync(userId, ct);

    public async Task GrantMembershipAsync(Guid userId, int days, CancellationToken ct)
    {
        if (days is < 1 or > 3650) throw new AppException("Días debe estar entre 1 y 3650.");
        if (!await admin.GrantMembershipAsync(userId, days, ct)) throw new AppException("Usuario no encontrado.", 404);
    }

    public async Task<IReadOnlyList<AdminUserGameDto>> UserGamesAsync(Guid userId, CancellationToken ct) =>
        (await admin.UserGamesAsync(userId, ct)).Select(g => new AdminUserGameDto(g.GameId, g.Slug, g.Name, g.GrantedAt, g.ExpiresAt)).ToList();

    public async Task RevokeGameAsync(Guid userId, Guid gameId, CancellationToken ct)
    { if (!await admin.RevokeGameAsync(userId, gameId, ct)) throw new AppException("El usuario no tiene ese permiso.", 404); }

    public Task GrantAsync(GrantRequest r, CancellationToken ct)
    {
        if (r.Days is <= 0) throw new AppException("Days debe ser mayor a 0 o vacío (permanente).");
        return admin.GrantAsync(r.Email.Trim().ToLowerInvariant(), r.Slug, r.Days, ct);
    }
}