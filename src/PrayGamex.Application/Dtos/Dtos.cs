using PrayGamex.Domain.Enums;
namespace PrayGamex.Application.Dtos;

public record RegisterRequest(string Email, string Name, string Password);
public record LoginRequest(string Email, string Password);
public record RefreshRequest(string RefreshToken);
public record AuthResponse(string AccessToken, string RefreshToken, UserDto User);
public record UserDto(Guid Id, string Email, string Name, AccountType AccountType, MembershipDto? Membership);
public record MembershipDto(string Plan, DateTime StartsAt, DateTime ExpiresAt, bool IsActive);
public record PlanDto(string Code, string Name, decimal Price, string Currency, int Days);

public record CategoryDto(Guid Id, string Name, string Slug);
public record ChallengeDto(Guid Id, Guid GameId, string GameName, string Title, string Detail, int Points);
public record GameDto(Guid Id, string Name, string Slug, string Description, string IconUrl, string LaunchUrl,
    Guid CategoryId, string CategoryName, bool IsPro, decimal Price, string Currency, int? AccessDays, bool IsFavorite, bool Locked);
public record CategoryDetailDto(CategoryDto Category, IReadOnlyList<GameDto> Games);
public record MenuItemDto(string Key, string Label, string Icon, string Route, int Order, IReadOnlyList<MenuItemDto> Children);

public record SaveProgressRequest(string Data, int Score, int Level);
public record ProgressDto(Guid GameId, string Data, int Score, int Level, DateTime UpdatedAt);

/// <summary>Indicar GameId (compra de un juego, precio desde BD) o PlanCode (membresía, precio desde BD).</summary>
public record CheckoutRequest(Guid? GameId, string? PlanCode);
public record CheckoutResponse(Guid PaymentId, string PreferenceId, string CheckoutUrl);

public record UpsertGameRequest(string Name, string Slug, string LaunchUrl, string CategorySlug, string? Description, string? IconUrl,
    bool IsPro, decimal Price, string? Currency, int? AccessDays, bool FeaturedToday, bool AddToMenu = true);
public record AdminGameDto(Guid Id, string Name, string Slug, string LaunchUrl, string CategorySlug, string Description, string IconUrl,
    bool IsPro, decimal Price, string Currency, int? AccessDays, bool IsActive, bool FeaturedToday);
public record GrantRequest(string Email, string Slug, int? Days);
public record ActiveRequest(bool IsActive);
public record LaunchResponse(string Url);
public record VerifyRequest(string Token);
public record VerifyResponse(bool Valid, Guid? UserId, Guid? GameId);