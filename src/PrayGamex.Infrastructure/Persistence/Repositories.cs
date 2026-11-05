using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using PrayGamex.Application.Abstractions;
using PrayGamex.Application.Services;
using PrayGamex.Domain.Entities;
using PrayGamex.Domain.Enums;
namespace PrayGamex.Infrastructure.Persistence;

public interface ISqlConnectionFactory { Task<SqlConnection> OpenAsync(CancellationToken ct); }

public class SqlConnectionFactory(IConfiguration cfg) : ISqlConnectionFactory
{
    private readonly string cs = new SqlConnectionStringBuilder(cfg.GetConnectionString("Default")) { ConnectRetryCount = 3, ConnectRetryInterval = 5 }.ConnectionString;
    public async Task<SqlConnection> OpenAsync(CancellationToken ct) { var c = new SqlConnection(cs); await c.OpenAsync(ct); return c; }
}

file static class Sql
{
    public static CommandDefinition Cmd(string sql, object? p, CancellationToken ct) => new(sql, p, cancellationToken: ct);
}

public class UserRepository(ISqlConnectionFactory f) : IUserRepository
{
    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<User>(Sql.Cmd("SELECT * FROM Users WHERE Email=@email", new { email }, ct)); }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<User>(Sql.Cmd("SELECT * FROM Users WHERE Id=@id", new { id }, ct)); }

    public async Task AddAsync(User u, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        try { await c.ExecuteAsync(Sql.Cmd("INSERT Users(Id,Email,Name,PasswordHash,AccountType,CreatedAt) VALUES(@Id,@Email,@Name,@PasswordHash,@AccountType,@CreatedAt)", u, ct)); }
        catch (SqlException e) when (e.Number is 2627 or 2601) { throw new AppException("El email ya está registrado.", 409); }
    }

    public async Task SetAccountTypeAsync(Guid userId, AccountType type, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); await c.ExecuteAsync(Sql.Cmd("UPDATE Users SET AccountType=@t WHERE Id=@userId", new { userId, t = (int)type }, ct)); }

    public async Task<Guid> CreateSessionAsync(Guid userId, int maxActive, DateTime expiresAt, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return await c.ExecuteScalarAsync<Guid>(Sql.Cmd("""
            SET XACT_ABORT ON;
            BEGIN TRAN;
            DECLARE @id UNIQUEIDENTIFIER = NEWID(), @now DATETIME2 = SYSUTCDATETIME();
            -- Conserva solo las (max-1) sesiones activas más recientes; la nueva completa el máximo.
            UPDATE Sessions SET RevokedAt=@now WHERE Id IN (
                SELECT Id FROM (SELECT Id, ROW_NUMBER() OVER(ORDER BY CreatedAt DESC) rn
                                FROM Sessions WITH (UPDLOCK, HOLDLOCK)
                                WHERE UserId=@userId AND RevokedAt IS NULL AND ExpiresAt>@now) s
                WHERE rn > @maxActive - 1);
            UPDATE RefreshTokens SET RevokedAt=@now
             WHERE RevokedAt IS NULL AND SessionId IN (SELECT Id FROM Sessions WHERE UserId=@userId AND RevokedAt IS NOT NULL);
            INSERT Sessions(Id,UserId,CreatedAt,ExpiresAt,RevokedAt) VALUES(@id,@userId,@now,@expiresAt,NULL);
            COMMIT;
            SELECT @id;
            """, new { userId, maxActive, expiresAt }, ct));
    }

    public async Task<bool> IsSessionActiveAsync(Guid sessionId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return await c.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM Sessions WHERE Id=@sessionId AND RevokedAt IS NULL AND ExpiresAt>SYSUTCDATETIME()", new { sessionId }, ct)) > 0;
    }

    public async Task RevokeSessionAsync(Guid sessionId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        await c.ExecuteAsync(Sql.Cmd("""
            UPDATE Sessions SET RevokedAt=SYSUTCDATETIME() WHERE Id=@sessionId AND RevokedAt IS NULL;
            UPDATE RefreshTokens SET RevokedAt=SYSUTCDATETIME() WHERE SessionId=@sessionId AND RevokedAt IS NULL;
            """, new { sessionId }, ct));
    }

    public async Task ExtendSessionAsync(Guid sessionId, DateTime expiresAt, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); await c.ExecuteAsync(Sql.Cmd("UPDATE Sessions SET ExpiresAt=@expiresAt WHERE Id=@sessionId AND RevokedAt IS NULL", new { sessionId, expiresAt }, ct)); }

    public async Task AddRefreshTokenAsync(RefreshToken t, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); await c.ExecuteAsync(Sql.Cmd("INSERT RefreshTokens(Id,UserId,SessionId,TokenHash,ExpiresAt,RevokedAt) VALUES(@Id,@UserId,@SessionId,@TokenHash,@ExpiresAt,@RevokedAt)", t, ct)); }

    public async Task<RefreshToken?> GetRefreshTokenAsync(string hash, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<RefreshToken>(Sql.Cmd("SELECT * FROM RefreshTokens WHERE TokenHash=@hash", new { hash }, ct)); }

    public async Task<bool> RevokeRefreshTokenAsync(Guid id, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return await c.ExecuteAsync(Sql.Cmd("UPDATE RefreshTokens SET RevokedAt=SYSUTCDATETIME() WHERE Id=@id AND RevokedAt IS NULL", new { id }, ct)) > 0;
    }

    public async Task<Membership?> GetMembershipAsync(Guid userId, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<Membership>(Sql.Cmd("SELECT * FROM Memberships WHERE UserId=@userId", new { userId }, ct)); }

    public async Task ExtendMembershipAsync(Guid userId, string plan, int days, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        await c.ExecuteAsync(Sql.Cmd("""
            DECLARE @now DATETIME2 = SYSUTCDATETIME();
            UPDATE Memberships SET [Plan]=@plan, StartsAt=@now,
                   ExpiresAt=DATEADD(DAY,@days, CASE WHEN ExpiresAt>@now THEN ExpiresAt ELSE @now END)
             WHERE UserId=@userId;
            IF @@ROWCOUNT=0
                INSERT Memberships(Id,UserId,[Plan],StartsAt,ExpiresAt) VALUES(NEWID(),@userId,@plan,@now,DATEADD(DAY,@days,@now));
            """, new { userId, plan, days }, ct));
    }
}

public class CatalogRepository(ISqlConnectionFactory f) : ICatalogRepository
{
    private const string Select = """
        SELECT g.*, c.Name AS CategoryName,
               CAST(CASE WHEN fv.GameId IS NULL THEN 0 ELSE 1 END AS BIT) AS IsFavorite,
               CAST(CASE WHEN EXISTS(SELECT 1 FROM UserGames ug WHERE ug.UserId=@UserId AND ug.GameId=g.Id
                                     AND (ug.ExpiresAt IS NULL OR ug.ExpiresAt>SYSUTCDATETIME())) THEN 1 ELSE 0 END AS BIT) AS Owned
        FROM Games g
        JOIN Categories c ON c.Id=g.CategoryId
        LEFT JOIN Favorites fv ON fv.GameId=g.Id AND fv.UserId=@UserId
        """;

    private static string EscapeLike(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");

    public async Task<IReadOnlyList<GameView>> SearchAsync(Guid userId, string? q, Guid? categoryId, bool onlyFavorites, bool onlyFeatured, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        var like = string.IsNullOrWhiteSpace(q) ? null : $"%{EscapeLike(q.Trim())}%";
        var rows = await c.QueryAsync<GameView>(Sql.Cmd(Select + """

            WHERE g.IsActive=1
              AND (@Q IS NULL OR g.Name LIKE @Q ESCAPE '\' OR g.Description LIKE @Q ESCAPE '\')
              AND (@CategoryId IS NULL OR g.CategoryId=@CategoryId)
              AND (@OnlyFav=0 OR fv.GameId IS NOT NULL)
              AND (@OnlyFeat=0 OR g.FeaturedToday=1)
            ORDER BY g.Name
            """, new { userId, Q = like, categoryId, OnlyFav = onlyFavorites, OnlyFeat = onlyFeatured }, ct));
        return rows.ToList();
    }

    public async Task<IReadOnlyList<GameView>> RecentsAsync(Guid userId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return (await c.QueryAsync<GameView>(Sql.Cmd(Select.Replace("SELECT g.*", "SELECT TOP 12 g.*") + """

            JOIN RecentPlays rp ON rp.GameId=g.Id AND rp.UserId=@UserId
            WHERE g.IsActive=1 ORDER BY rp.LastPlayedAt DESC
            """, new { userId }, ct))).ToList();
    }

    public async Task<GameView?> GetGameAsync(Guid userId, Guid gameId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return await c.QuerySingleOrDefaultAsync<GameView>(Sql.Cmd(Select + "\nWHERE g.Id=@gameId", new { userId, gameId }, ct));
    }

    public async Task<IReadOnlyList<Category>> CategoriesAsync(CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return (await c.QueryAsync<Category>(Sql.Cmd("SELECT * FROM Categories ORDER BY [Order]", null, ct))).ToList(); }

    public async Task<Category?> GetCategoryAsync(Guid id, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<Category>(Sql.Cmd("SELECT * FROM Categories WHERE Id=@id", new { id }, ct)); }

    public async Task<IReadOnlyList<Challenge>> ChallengesAsync(Guid? gameId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return (await c.QueryAsync<Challenge>(Sql.Cmd("""
            SELECT ch.*, g.Name AS GameName FROM Challenges ch JOIN Games g ON g.Id=ch.GameId
            WHERE g.IsActive=1 AND (@gameId IS NULL OR ch.GameId=@gameId) ORDER BY g.Name, ch.Points
            """, new { gameId }, ct))).ToList();
    }

    public async Task<bool> ToggleFavoriteAsync(Guid userId, Guid gameId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        if (await c.ExecuteAsync(Sql.Cmd("DELETE FROM Favorites WHERE UserId=@userId AND GameId=@gameId", new { userId, gameId }, ct)) > 0) return false;
        if (await c.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM Games WHERE Id=@gameId", new { gameId }, ct)) == 0) throw new AppException("Juego no encontrado.", 404);
        await c.ExecuteAsync(Sql.Cmd("""
            IF NOT EXISTS(SELECT 1 FROM Favorites WHERE UserId=@userId AND GameId=@gameId)
                INSERT Favorites(UserId,GameId,CreatedAt) VALUES(@userId,@gameId,SYSUTCDATETIME())
            """, new { userId, gameId }, ct));
        return true;
    }

    public async Task TouchRecentAsync(Guid userId, Guid gameId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        await c.ExecuteAsync(Sql.Cmd("""
            UPDATE RecentPlays SET LastPlayedAt=SYSUTCDATETIME() WHERE UserId=@userId AND GameId=@gameId;
            IF @@ROWCOUNT=0 INSERT RecentPlays(UserId,GameId,LastPlayedAt) VALUES(@userId,@gameId,SYSUTCDATETIME());
            """, new { userId, gameId }, ct));
    }
}

public class MenuRepository(ISqlConnectionFactory f) : IMenuRepository
{
    public async Task<IReadOnlyList<MenuItem>> GetAsync(DeviceType device, bool authenticated, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return (await c.QueryAsync<MenuItem>(Sql.Cmd(
            "SELECT * FROM MenuItems WHERE Device=@d AND IsVisible=1 AND (@auth=1 OR RequiresAuth=0)",
            new { d = (int)device, auth = authenticated }, ct))).ToList();
    }
}

public class ProgressRepository(ISqlConnectionFactory f) : IProgressRepository
{
    public async Task<GameProgress?> GetAsync(Guid userId, Guid gameId, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<GameProgress>(Sql.Cmd("SELECT * FROM GameProgresses WHERE UserId=@userId AND GameId=@gameId", new { userId, gameId }, ct)); }

    public async Task<GameProgress> UpsertAsync(Guid userId, Guid gameId, string data, int score, int level, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return await c.QuerySingleAsync<GameProgress>(Sql.Cmd("""
            UPDATE GameProgresses SET Data=@data, Score=CASE WHEN @score>Score THEN @score ELSE Score END,
                   Level=CASE WHEN @level>Level THEN @level ELSE Level END, UpdatedAt=SYSUTCDATETIME()
             WHERE UserId=@userId AND GameId=@gameId;
            IF @@ROWCOUNT=0
                INSERT GameProgresses(Id,UserId,GameId,Data,Score,Level,UpdatedAt) VALUES(NEWID(),@userId,@gameId,@data,@score,@level,SYSUTCDATETIME());
            SELECT * FROM GameProgresses WHERE UserId=@userId AND GameId=@gameId;
            """, new { userId, gameId, data, score, level }, ct));
    }
}

public class PaymentRepository(ISqlConnectionFactory f) : IPaymentRepository
{
    public async Task<IReadOnlyList<MembershipPlan>> PlansAsync(CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return (await c.QueryAsync<MembershipPlan>(Sql.Cmd("SELECT * FROM MembershipPlans WHERE IsActive=1 ORDER BY Price", null, ct))).ToList(); }

    public async Task<MembershipPlan?> GetPlanAsync(string code, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<MembershipPlan>(Sql.Cmd("SELECT * FROM MembershipPlans WHERE Code=@code", new { code }, ct)); }

    public async Task AddAsync(Payment p, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        await c.ExecuteAsync(Sql.Cmd("""
            INSERT Payments(Id,UserId,Kind,GameId,PlanCode,Title,Amount,Currency,AccessDays,PreferenceId,ExternalPaymentId,Status,CreatedAt)
            VALUES(@Id,@UserId,@Kind,@GameId,@PlanCode,@Title,@Amount,@Currency,@AccessDays,@PreferenceId,@ExternalPaymentId,@Status,@CreatedAt)
            """, p, ct));
    }

    public async Task SetPreferenceAsync(Guid paymentId, string preferenceId, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); await c.ExecuteAsync(Sql.Cmd("UPDATE Payments SET PreferenceId=@preferenceId WHERE Id=@paymentId", new { paymentId, preferenceId }, ct)); }

    public async Task<Payment?> GetAsync(Guid id, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.QuerySingleOrDefaultAsync<Payment>(Sql.Cmd("SELECT * FROM Payments WHERE Id=@id", new { id }, ct)); }

    public async Task<bool> TryApproveAsync(Guid id, string externalPaymentId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return await c.ExecuteAsync(Sql.Cmd("UPDATE Payments SET Status=@s, ExternalPaymentId=@e WHERE Id=@id AND Status<>@s", new { id, e = externalPaymentId, s = (int)PaymentStatus.Approved }, ct)) > 0;
    }

    public async Task SetStatusAsync(Guid id, PaymentStatus status, string externalPaymentId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        await c.ExecuteAsync(Sql.Cmd("UPDATE Payments SET Status=@st, ExternalPaymentId=@e WHERE Id=@id AND Status<>@ok", new { id, e = externalPaymentId, st = (int)status, ok = (int)PaymentStatus.Approved }, ct));
    }

    public async Task GrantGameAsync(Guid userId, Guid gameId, int? days, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        await c.ExecuteAsync(Sql.Cmd("""
            DECLARE @now DATETIME2 = SYSUTCDATETIME();
            UPDATE UserGames SET GrantedAt=@now,
                   ExpiresAt = CASE WHEN @days IS NULL THEN NULL
                                    WHEN ExpiresAt IS NULL THEN NULL
                                    WHEN ExpiresAt>@now THEN DATEADD(DAY,@days,ExpiresAt)
                                    ELSE DATEADD(DAY,@days,@now) END
             WHERE UserId=@userId AND GameId=@gameId;
            IF @@ROWCOUNT=0
                INSERT UserGames(UserId,GameId,GrantedAt,ExpiresAt) VALUES(@userId,@gameId,@now,CASE WHEN @days IS NULL THEN NULL ELSE DATEADD(DAY,@days,@now) END);
            """, new { userId, gameId, days }, ct));
    }
}

public class AdminRepository(ISqlConnectionFactory f) : IAdminRepository
{
    public async Task<IReadOnlyList<AdminGameRow>> ListGamesAsync(CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return (await c.QueryAsync<AdminGameRow>(Sql.Cmd("SELECT g.*, c.Slug AS CategorySlug FROM Games g JOIN Categories c ON c.Id=g.CategoryId ORDER BY g.Name", null, ct))).ToList();
    }

    public async Task UpsertGameAsync(UpsertGame g, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        try
        {
            await c.ExecuteAsync(new CommandDefinition("dbo.usp_AddGame", new
            {
                g.Name, g.Slug, g.LaunchUrl, g.CategorySlug, g.Description, g.IconUrl, g.IsPro, g.Price, g.Currency, g.AccessDays,
                g.FeaturedToday, AddToMenu = g.AddToMenu,
            }, commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException e) when (e.Number == 50001) { throw new AppException("Categoría inexistente."); }
    }

    public async Task<bool> SetActiveAsync(string slug, bool active, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        var n = await c.ExecuteAsync(Sql.Cmd("""
            UPDATE Games SET IsActive=@active WHERE Slug=@slug;
            DECLARE @n INT = @@ROWCOUNT;
            UPDATE MenuItems SET IsVisible=@active WHERE ParentId IS NOT NULL AND [Key]=@slug;
            SELECT @n;
            """, new { slug, active }, ct));
        return await c.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM Games WHERE Slug=@slug", new { slug }, ct)) > 0;
    }

    private static string Like(string s) => "%" + s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[") + "%";

    public async Task<IReadOnlyList<AdminUserRow>> ListUsersAsync(string? q, int take, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return (await c.QueryAsync<AdminUserRow>(Sql.Cmd("""
            SELECT TOP (@take) u.Id, u.Email, u.Name, u.AccountType, u.CreatedAt,
                   m.[Plan] AS MembershipPlan, m.ExpiresAt AS MembershipExpiresAt,
                   (SELECT COUNT(1) FROM Sessions s WHERE s.UserId=u.Id AND s.RevokedAt IS NULL AND s.ExpiresAt>SYSUTCDATETIME()) AS ActiveSessions,
                   (SELECT COUNT(1) FROM UserGames ug WHERE ug.UserId=u.Id AND (ug.ExpiresAt IS NULL OR ug.ExpiresAt>SYSUTCDATETIME())) AS GrantedGames
            FROM Users u LEFT JOIN Memberships m ON m.UserId=u.Id
            WHERE @q IS NULL OR u.Email LIKE @q ESCAPE '\' OR u.Name LIKE @q ESCAPE '\'
            ORDER BY u.CreatedAt DESC
            """, new { take, q = q is null ? null : Like(q) }, ct))).ToList();
    }

    public async Task<bool> SetAccountTypeAsync(Guid userId, AccountType type, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.ExecuteAsync(Sql.Cmd("UPDATE Users SET AccountType=@t WHERE Id=@userId", new { userId, t = (int)type }, ct)) > 0; }

    public async Task RevokeSessionsAsync(Guid userId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        await c.ExecuteAsync(Sql.Cmd("""
            UPDATE Sessions SET RevokedAt=SYSUTCDATETIME() WHERE UserId=@userId AND RevokedAt IS NULL;
            UPDATE RefreshTokens SET RevokedAt=SYSUTCDATETIME() WHERE UserId=@userId AND RevokedAt IS NULL;
            """, new { userId }, ct));
    }

    public async Task<bool> GrantMembershipAsync(Guid userId, int days, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        if (await c.ExecuteScalarAsync<int>(Sql.Cmd("SELECT COUNT(1) FROM Users WHERE Id=@userId", new { userId }, ct)) == 0) return false;
        await c.ExecuteAsync(Sql.Cmd("""
            DECLARE @now DATETIME2 = SYSUTCDATETIME();
            UPDATE Memberships SET [Plan]='manual', StartsAt=@now,
                   ExpiresAt=DATEADD(DAY,@days, CASE WHEN ExpiresAt>@now THEN ExpiresAt ELSE @now END) WHERE UserId=@userId;
            IF @@ROWCOUNT=0
                INSERT Memberships(Id,UserId,[Plan],StartsAt,ExpiresAt) VALUES(NEWID(),@userId,'manual',@now,DATEADD(DAY,@days,@now));
            UPDATE Users SET AccountType=1 WHERE Id=@userId AND AccountType=0;
            """, new { userId, days }, ct));
        return true;
    }

    public async Task<IReadOnlyList<AdminUserGameRow>> UserGamesAsync(Guid userId, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        return (await c.QueryAsync<AdminUserGameRow>(Sql.Cmd("""
            SELECT g.Id AS GameId, g.Slug, g.Name, ug.GrantedAt, ug.ExpiresAt
            FROM UserGames ug JOIN Games g ON g.Id=ug.GameId WHERE ug.UserId=@userId ORDER BY g.Name
            """, new { userId }, ct))).ToList();
    }

    public async Task<bool> RevokeGameAsync(Guid userId, Guid gameId, CancellationToken ct)
    { await using var c = await f.OpenAsync(ct); return await c.ExecuteAsync(Sql.Cmd("DELETE FROM UserGames WHERE UserId=@userId AND GameId=@gameId", new { userId, gameId }, ct)) > 0; }

    public async Task GrantAsync(string email, string slug, int? days, CancellationToken ct)
    {
        await using var c = await f.OpenAsync(ct);
        try { await c.ExecuteAsync(new CommandDefinition("dbo.usp_GrantGame", new { Email = email, Slug = slug, Days = days }, commandType: System.Data.CommandType.StoredProcedure, cancellationToken: ct)); }
        catch (SqlException e) when (e.Number == 50002) { throw new AppException("Usuario o juego inexistente.", 404); }
    }
}