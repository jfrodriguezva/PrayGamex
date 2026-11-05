using PrayGamex.Domain.Enums;
namespace PrayGamex.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public AccountType AccountType { get; set; } = AccountType.Free;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public string TokenHash { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public bool IsActive => RevokedAt is null && ExpiresAt > DateTime.UtcNow;
}

/// <summary>Sesión de un usuario (un dispositivo). Máximo 2 activas por usuario; al iniciar una nueva se cierra la más antigua.</summary>
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public int Order { get; set; }
}

/// <summary>
/// Juego registrado (la URL y el cobro se definen aquí, en BD).
/// IsPro = false → DEMO, abierto para cualquier usuario autenticado.
/// IsPro = true  → PRO: requiere membresía vigente o un permiso por juego (UserGames) obtenido por compra o asignación.
/// Price &gt; 0 habilita la compra individual; AccessDays = null → acceso permanente, N → N días desde el pago.
/// </summary>
public class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
    /// <summary>URL del juego en el servidor externo (Arcade / Uapeani).</summary>
    public string LaunchUrl { get; set; } = "";
    public Guid CategoryId { get; set; }
    public bool IsPro { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = "MXN";
    public int? AccessDays { get; set; }
    public bool IsActive { get; set; } = true;
    public bool FeaturedToday { get; set; }
}

/// <summary>Usuario tal como lo ve el administrador (con membresía, sesiones y permisos por juego).</summary>
public class AdminUserRow
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType AccountType { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? MembershipPlan { get; set; }
    public DateTime? MembershipExpiresAt { get; set; }
    public int ActiveSessions { get; set; }
    public int GrantedGames { get; set; }
}

public class AdminUserGameRow
{
    public Guid GameId { get; set; }
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>Juego tal como lo ve el administrador (incluye inactivos y el slug de categoría).</summary>
public class AdminGameRow : Game { public string CategorySlug { get; set; } = ""; }

/// <summary>Juego + datos calculados para un usuario concreto.</summary>
public class GameView : Game
{
    public string CategoryName { get; set; } = "";
    public bool IsFavorite { get; set; }
    /// <summary>El usuario compró este juego y su acceso sigue vigente.</summary>
    public bool Owned { get; set; }
}

public class Challenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public string GameName { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public int Points { get; set; }
}

public class GameProgress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid GameId { get; set; }
    /// <summary>Estado del juego serializado (JSON) enviado por el servidor del juego.</summary>
    public string Data { get; set; } = "{}";
    public int Score { get; set; }
    public int Level { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class MembershipPlan
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string Currency { get; set; } = "MXN";
    public int Days { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Membership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Plan { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsActive => ExpiresAt > DateTime.UtcNow;
}

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public PaymentKind Kind { get; set; }
    public Guid? GameId { get; set; }
    public string? PlanCode { get; set; }
    public string Title { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "MXN";
    /// <summary>Días de acceso congelados al crear el cobro (null = permanente).</summary>
    public int? AccessDays { get; set; }
    public string? PreferenceId { get; set; }
    public string? ExternalPaymentId { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Configuración del menú almacenada en BD, por tipo de dispositivo.</summary>
public class MenuItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Device { get; set; }
    public Guid? ParentId { get; set; }
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
    public string Route { get; set; } = "";
    public int Order { get; set; }
    public bool IsVisible { get; set; } = true;
    public bool RequiresAuth { get; set; } = true;
}
