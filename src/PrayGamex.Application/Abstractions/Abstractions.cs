using PrayGamex.Domain.Entities;
using PrayGamex.Domain.Enums;
namespace PrayGamex.Application.Abstractions;

public interface IPasswordHasher { string Hash(string password); bool Verify(string password, string hash); }

public interface ITokenService
{
    string CreateAccessToken(User user, Guid sessionId);
    /// <summary>Token de lanzamiento: lo recibe el servidor del juego (aud distinta) y solo sirve para validar al jugador y guardar su avance.</summary>
    string CreateGameToken(Guid userId, Guid sessionId, Guid gameId);
    /// <summary>Valida un token de lanzamiento (firma, caducidad, audiencia). Devuelve null si no es válido.</summary>
    (Guid UserId, Guid SessionId, Guid GameId)? ValidateGameToken(string token);
    (string Token, string Hash) CreateRefreshToken();
    string HashToken(string token);
}

public interface IPaymentGateway
{
    Task<(string PreferenceId, string CheckoutUrl)> CreatePreferenceAsync(Payment payment, string payerEmail, CancellationToken ct);
    Task<(string Status, string? ExternalReference, decimal Amount)> GetPaymentAsync(string externalPaymentId, CancellationToken ct);
}

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email, CancellationToken ct);
    Task<User?> GetByIdAsync(Guid id, CancellationToken ct);
    Task AddAsync(User user, CancellationToken ct);
    Task SetAccountTypeAsync(Guid userId, AccountType type, CancellationToken ct);
    /// <summary>Crea una sesión y cierra las más antiguas para que queden como máximo <paramref name="maxActive"/> activas (incluida la nueva).</summary>
    Task<Guid> CreateSessionAsync(Guid userId, int maxActive, DateTime expiresAt, CancellationToken ct);
    Task<bool> IsSessionActiveAsync(Guid sessionId, CancellationToken ct);
    Task RevokeSessionAsync(Guid sessionId, CancellationToken ct);
    Task ExtendSessionAsync(Guid sessionId, DateTime expiresAt, CancellationToken ct);
    Task AddRefreshTokenAsync(RefreshToken token, CancellationToken ct);
    Task<RefreshToken?> GetRefreshTokenAsync(string hash, CancellationToken ct);
    /// <summary>Revoca el token solo si seguía activo. Devuelve false si otro request ya lo usó (anti-replay).</summary>
    Task<bool> RevokeRefreshTokenAsync(Guid id, CancellationToken ct);
    Task<Membership?> GetMembershipAsync(Guid userId, CancellationToken ct);
    /// <summary>Crea o extiende la membresía sumando <paramref name="days"/> a la vigencia actual (o a hoy).</summary>
    Task ExtendMembershipAsync(Guid userId, string plan, int days, CancellationToken ct);
}

public interface ICatalogRepository
{
    Task<IReadOnlyList<GameView>> SearchAsync(Guid userId, string? q, Guid? categoryId, bool onlyFavorites, bool onlyFeatured, CancellationToken ct);
    Task<IReadOnlyList<GameView>> RecentsAsync(Guid userId, CancellationToken ct);
    Task<GameView?> GetGameAsync(Guid userId, Guid gameId, CancellationToken ct);
    Task<IReadOnlyList<Category>> CategoriesAsync(CancellationToken ct);
    Task<Category?> GetCategoryAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Challenge>> ChallengesAsync(Guid? gameId, CancellationToken ct);
    Task<bool> ToggleFavoriteAsync(Guid userId, Guid gameId, CancellationToken ct);
    Task TouchRecentAsync(Guid userId, Guid gameId, CancellationToken ct);
}

public interface IAdminRepository
{
    Task<IReadOnlyList<AdminGameRow>> ListGamesAsync(CancellationToken ct);
    Task UpsertGameAsync(UpsertGame g, CancellationToken ct);
    Task<bool> SetActiveAsync(string slug, bool active, CancellationToken ct);
    Task GrantAsync(string email, string slug, int? days, CancellationToken ct);
}

public record UpsertGame(string Name, string Slug, string LaunchUrl, string CategorySlug, string Description, string IconUrl,
    bool IsPro, decimal Price, string Currency, int? AccessDays, bool FeaturedToday, bool AddToMenu);

public interface IMenuRepository
{
    Task<IReadOnlyList<MenuItem>> GetAsync(DeviceType device, bool authenticated, CancellationToken ct);
}

public interface IProgressRepository
{
    Task<GameProgress?> GetAsync(Guid userId, Guid gameId, CancellationToken ct);
    Task<GameProgress> UpsertAsync(Guid userId, Guid gameId, string data, int score, int level, CancellationToken ct);
}

public interface IPaymentRepository
{
    Task<IReadOnlyList<MembershipPlan>> PlansAsync(CancellationToken ct);
    Task<MembershipPlan?> GetPlanAsync(string code, CancellationToken ct);
    Task AddAsync(Payment payment, CancellationToken ct);
    Task SetPreferenceAsync(Guid paymentId, string preferenceId, CancellationToken ct);
    Task<Payment?> GetAsync(Guid id, CancellationToken ct);
    /// <summary>Pasa a Aprobado de forma atómica. Devuelve true solo para quien lo aprobó primero (idempotencia del webhook).</summary>
    Task<bool> TryApproveAsync(Guid id, string externalPaymentId, CancellationToken ct);
    Task SetStatusAsync(Guid id, PaymentStatus status, string externalPaymentId, CancellationToken ct);
    /// <summary>Otorga o extiende el acceso a un juego (days null = permanente).</summary>
    Task GrantGameAsync(Guid userId, Guid gameId, int? days, CancellationToken ct);
}

