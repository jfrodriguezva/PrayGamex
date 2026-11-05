namespace PrayGamex.Infrastructure.Persistence;

/// <summary>Esquema idempotente (se puede ejecutar en cada arranque).</summary>
public static class Schema
{
    public const string Tables = """
    IF OBJECT_ID(N'dbo.Users','U') IS NULL CREATE TABLE dbo.Users(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, Email NVARCHAR(256) NOT NULL, Name NVARCHAR(120) NOT NULL,
        PasswordHash NVARCHAR(400) NOT NULL, AccountType INT NOT NULL DEFAULT 0, CreatedAt DATETIME2 NOT NULL,
        CONSTRAINT UX_Users_Email UNIQUE(Email));

    IF OBJECT_ID(N'dbo.Sessions','U') IS NULL CREATE TABLE dbo.Sessions(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, UserId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Users(Id),
        CreatedAt DATETIME2 NOT NULL, ExpiresAt DATETIME2 NOT NULL, RevokedAt DATETIME2 NULL);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Sessions_User') CREATE INDEX IX_Sessions_User ON dbo.Sessions(UserId, CreatedAt DESC);

    IF OBJECT_ID(N'dbo.RefreshTokens','U') IS NULL CREATE TABLE dbo.RefreshTokens(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, UserId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Users(Id),
        SessionId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Sessions(Id),
        TokenHash NVARCHAR(128) NOT NULL, ExpiresAt DATETIME2 NOT NULL, RevokedAt DATETIME2 NULL);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_RefreshTokens_Hash') CREATE INDEX IX_RefreshTokens_Hash ON dbo.RefreshTokens(TokenHash);

    IF OBJECT_ID(N'dbo.Categories','U') IS NULL CREATE TABLE dbo.Categories(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, Name NVARCHAR(100) NOT NULL, Slug NVARCHAR(100) NOT NULL UNIQUE, [Order] INT NOT NULL DEFAULT 0);

    IF OBJECT_ID(N'dbo.Games','U') IS NULL CREATE TABLE dbo.Games(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, Name NVARCHAR(150) NOT NULL, Slug NVARCHAR(150) NOT NULL UNIQUE,
        Description NVARCHAR(1000) NOT NULL DEFAULT '', IconUrl NVARCHAR(500) NOT NULL DEFAULT '', LaunchUrl NVARCHAR(500) NOT NULL,
        CategoryId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Categories(Id),
        IsPro BIT NOT NULL DEFAULT 0,
        Price DECIMAL(18,2) NOT NULL DEFAULT 0 CHECK (Price >= 0), Currency CHAR(3) NOT NULL DEFAULT 'MXN',
        AccessDays INT NULL CHECK (AccessDays IS NULL OR AccessDays > 0),
        IsActive BIT NOT NULL DEFAULT 1, FeaturedToday BIT NOT NULL DEFAULT 0);

    IF OBJECT_ID(N'dbo.Challenges','U') IS NULL CREATE TABLE dbo.Challenges(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, GameId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Games(Id),
        Title NVARCHAR(150) NOT NULL, Detail NVARCHAR(1000) NOT NULL, Points INT NOT NULL);

    IF OBJECT_ID(N'dbo.Favorites','U') IS NULL CREATE TABLE dbo.Favorites(
        UserId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Users(Id), GameId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Games(Id),
        CreatedAt DATETIME2 NOT NULL, PRIMARY KEY(UserId, GameId));

    IF OBJECT_ID(N'dbo.RecentPlays','U') IS NULL CREATE TABLE dbo.RecentPlays(
        UserId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Users(Id), GameId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Games(Id),
        LastPlayedAt DATETIME2 NOT NULL, PRIMARY KEY(UserId, GameId));

    IF OBJECT_ID(N'dbo.GameProgresses','U') IS NULL CREATE TABLE dbo.GameProgresses(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, UserId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Users(Id),
        GameId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Games(Id), Data NVARCHAR(MAX) NOT NULL,
        Score INT NOT NULL, Level INT NOT NULL, UpdatedAt DATETIME2 NOT NULL, CONSTRAINT UX_Progress UNIQUE(UserId, GameId));

    IF OBJECT_ID(N'dbo.MembershipPlans','U') IS NULL CREATE TABLE dbo.MembershipPlans(
        Code NVARCHAR(50) NOT NULL PRIMARY KEY, Name NVARCHAR(100) NOT NULL, Price DECIMAL(18,2) NOT NULL CHECK (Price > 0),
        Currency CHAR(3) NOT NULL DEFAULT 'MXN', Days INT NOT NULL CHECK (Days > 0), IsActive BIT NOT NULL DEFAULT 1);

    IF OBJECT_ID(N'dbo.Memberships','U') IS NULL CREATE TABLE dbo.Memberships(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, UserId UNIQUEIDENTIFIER NOT NULL UNIQUE REFERENCES dbo.Users(Id),
        [Plan] NVARCHAR(50) NOT NULL, StartsAt DATETIME2 NOT NULL, ExpiresAt DATETIME2 NOT NULL);

    IF OBJECT_ID(N'dbo.UserGames','U') IS NULL CREATE TABLE dbo.UserGames(
        UserId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Users(Id), GameId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Games(Id),
        GrantedAt DATETIME2 NOT NULL, ExpiresAt DATETIME2 NULL, PRIMARY KEY(UserId, GameId)); -- permiso por juego (compra o asignación)

    IF OBJECT_ID(N'dbo.Payments','U') IS NULL CREATE TABLE dbo.Payments(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, UserId UNIQUEIDENTIFIER NOT NULL REFERENCES dbo.Users(Id),
        Kind INT NOT NULL, GameId UNIQUEIDENTIFIER NULL REFERENCES dbo.Games(Id), PlanCode NVARCHAR(50) NULL,
        Title NVARCHAR(200) NOT NULL, Amount DECIMAL(18,2) NOT NULL, Currency CHAR(3) NOT NULL, AccessDays INT NULL,
        PreferenceId NVARCHAR(100) NULL, ExternalPaymentId NVARCHAR(100) NULL, Status INT NOT NULL, CreatedAt DATETIME2 NOT NULL);
    IF NOT EXISTS(SELECT 1 FROM sys.indexes WHERE name='IX_Payments_Ext') CREATE INDEX IX_Payments_Ext ON dbo.Payments(ExternalPaymentId);

    IF OBJECT_ID(N'dbo.MenuItems','U') IS NULL CREATE TABLE dbo.MenuItems(
        Id UNIQUEIDENTIFIER NOT NULL PRIMARY KEY, Device INT NOT NULL, ParentId UNIQUEIDENTIFIER NULL REFERENCES dbo.MenuItems(Id),
        [Key] NVARCHAR(100) NOT NULL, Label NVARCHAR(100) NOT NULL, Icon NVARCHAR(50) NOT NULL DEFAULT '', Route NVARCHAR(200) NOT NULL,
        [Order] INT NOT NULL DEFAULT 0, IsVisible BIT NOT NULL DEFAULT 1, RequiresAuth BIT NOT NULL DEFAULT 1,
        CONSTRAINT UX_Menu UNIQUE(Device, ParentId, [Key]));
    """;

    /// <summary>
    /// Alta/actualización de un juego desde BD: define su precio y vigencia de acceso y lo agrega al menú
    /// (submenú de "Categorías") en todos los dispositivos. Ej.:
    /// EXEC dbo.usp_AddGame @Name='Mi juego', @Slug='mi-juego', @LaunchUrl='https://...', @CategorySlug='arcade', @Price=49, @AccessDays=30;
    /// @IsPro = 0 → DEMO (abierto). @IsPro = 1 → PRO (membresía o permiso por juego). @Price &gt; 0 habilita la compra individual.
    /// AccessDays = NULL → acceso permanente.
    /// </summary>
    /// <summary>Asigna manualmente el permiso de un usuario a un juego (EXEC dbo.usp_GrantGame @Email='a@b.com', @Slug='mi-juego', @Days=NULL).</summary>
    public const string GrantGameProc = """
    CREATE OR ALTER PROCEDURE dbo.usp_GrantGame @Email NVARCHAR(256), @Slug NVARCHAR(150), @Days INT = NULL
    AS
    BEGIN
        SET NOCOUNT ON;
        DECLARE @UserId UNIQUEIDENTIFIER = (SELECT Id FROM dbo.Users WHERE Email = LOWER(@Email));
        DECLARE @GameId UNIQUEIDENTIFIER = (SELECT Id FROM dbo.Games WHERE Slug = @Slug);
        IF @UserId IS NULL OR @GameId IS NULL THROW 50002, 'Usuario o juego inexistente.', 1;
        DECLARE @now DATETIME2 = SYSUTCDATETIME();
        UPDATE dbo.UserGames SET GrantedAt=@now, ExpiresAt = CASE WHEN @Days IS NULL THEN NULL ELSE DATEADD(DAY,@Days,@now) END
         WHERE UserId=@UserId AND GameId=@GameId;
        IF @@ROWCOUNT=0 INSERT dbo.UserGames(UserId,GameId,GrantedAt,ExpiresAt)
            VALUES(@UserId,@GameId,@now,CASE WHEN @Days IS NULL THEN NULL ELSE DATEADD(DAY,@Days,@now) END);
    END
    """;

    /// <summary>Define el tipo de cuenta de un usuario: 0 Free, 1 Premium, 2 Admin. Ej.: EXEC dbo.usp_SetAccountType @Email='yo@correo.com', @AccountType=2;</summary>
    public const string SetAccountTypeProc = """
    CREATE OR ALTER PROCEDURE dbo.usp_SetAccountType @Email NVARCHAR(256), @AccountType INT
    AS
    BEGIN
        SET NOCOUNT ON;
        UPDATE dbo.Users SET AccountType=@AccountType WHERE Email=LOWER(@Email);
        IF @@ROWCOUNT=0 THROW 50003, 'Usuario inexistente.', 1;
    END
    """;

    public const string AddGameProc = """
    CREATE OR ALTER PROCEDURE dbo.usp_AddGame
        @Name NVARCHAR(150), @Slug NVARCHAR(150), @LaunchUrl NVARCHAR(500), @CategorySlug NVARCHAR(100),
        @Description NVARCHAR(1000) = '', @IconUrl NVARCHAR(500) = '', @IsPro BIT = 0, @Price DECIMAL(18,2) = 0, @Currency CHAR(3) = 'MXN',
        @AccessDays INT = NULL, @FeaturedToday BIT = 0, @AddToMenu BIT = 1
    AS
    BEGIN
        SET NOCOUNT ON;
        DECLARE @CategoryId UNIQUEIDENTIFIER = (SELECT Id FROM dbo.Categories WHERE Slug = @CategorySlug);
        IF @CategoryId IS NULL THROW 50001, 'Categoría inexistente.', 1;
        IF EXISTS(SELECT 1 FROM dbo.Games WHERE Slug = @Slug)
            UPDATE dbo.Games SET Name=@Name, LaunchUrl=@LaunchUrl, CategoryId=@CategoryId, Description=@Description, IconUrl=@IconUrl, IsPro=@IsPro,
                Price=@Price, Currency=@Currency, AccessDays=@AccessDays, FeaturedToday=@FeaturedToday WHERE Slug=@Slug;
        ELSE
            INSERT dbo.Games(Id,Name,Slug,Description,IconUrl,LaunchUrl,CategoryId,IsPro,Price,Currency,AccessDays,IsActive,FeaturedToday)
            VALUES(NEWID(),@Name,@Slug,@Description,@IconUrl,@LaunchUrl,@CategoryId,@IsPro,@Price,@Currency,@AccessDays,1,@FeaturedToday);
        IF @AddToMenu = 1
            INSERT dbo.MenuItems(Id,Device,ParentId,[Key],Label,Icon,Route,[Order],IsVisible,RequiresAuth)
            SELECT NEWID(), p.Device, p.Id, @Slug, @Name, 'game', '/categorias/' + @Slug,
                   100 + (SELECT COUNT(*) FROM dbo.MenuItems c WHERE c.ParentId = p.Id), 1, 1
            FROM dbo.MenuItems p
            WHERE p.[Key] = 'categorias' AND p.ParentId IS NULL
              AND NOT EXISTS(SELECT 1 FROM dbo.MenuItems x WHERE x.ParentId = p.Id AND x.[Key] = @Slug);
    END
    """;
}
