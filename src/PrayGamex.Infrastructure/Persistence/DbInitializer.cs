using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Retry;
using PrayGamex.Domain.Enums;
namespace PrayGamex.Infrastructure.Persistence;

public static class DbInitializer
{
    /// <summary>Crea BD, tablas y procedimiento; siembra datos. Reintenta con Polly porque SQL Server en Docker tarda en arrancar.</summary>
    public static async Task RunAsync(IConfiguration cfg, ISqlConnectionFactory factory, CancellationToken ct = default)
    {
        var csb = new SqlConnectionStringBuilder(cfg.GetConnectionString("Default"));
        var dbName = csb.InitialCatalog;
        csb.InitialCatalog = "master";
        var pipeline = new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
        { MaxRetryAttempts = 12, Delay = TimeSpan.FromSeconds(3), BackoffType = DelayBackoffType.Constant }).Build();

        await pipeline.ExecuteAsync(async token =>
        {
            await using (var master = new SqlConnection(csb.ConnectionString))
            {
                await master.OpenAsync(token);
                await master.ExecuteAsync($"IF DB_ID(@n) IS NULL EXEC('CREATE DATABASE [' + @n + ']')", new { n = dbName });
            }
            await using var c = await factory.OpenAsync(token);
            await c.ExecuteAsync(Schema.Tables);
            await c.ExecuteAsync(Schema.AddGameProc);
            await c.ExecuteAsync(Schema.GrantGameProc);
            await c.ExecuteAsync(Schema.SetAccountTypeProc);
            await SeedAsync(c);
        }, ct);
    }

    private static async Task SeedAsync(SqlConnection c)
    {
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Categories") == 0)
            foreach (var (name, slug, i) in new[] { ("Arcade", "arcade", 0), ("Puzzle", "puzzle", 1), ("Aventura", "aventura", 2), ("Estrategia", "estrategia", 3) })
                await c.ExecuteAsync("INSERT Categories(Id,Name,Slug,[Order]) VALUES(NEWID(),@name,@slug,@i)", new { name, slug, i });

        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM MembershipPlans") == 0)
            await c.ExecuteAsync("""
                INSERT MembershipPlans(Code,Name,Price,Currency,Days,IsActive) VALUES
                ('premium-monthly','Premium mensual',99,'MXN',30,1),
                ('premium-yearly','Premium anual',990,'MXN',365,1)
                """);

        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM MenuItems") == 0)
            foreach (var device in Enum.GetValues<DeviceType>())
            {
                // El front cambia el layout por dispositivo (móvil: barra inferior, tablet: riel de iconos, PC: barra lateral con submenús).
                var inicio = await Item(c, device, null, "inicio", "Inicio", "home", "/", 0);
                var cat = await Item(c, device, null, "categorias", "Categorías", "grid", "/categorias", 1);
                var fav = await Item(c, device, null, "favoritos", "Favoritos", "star", "/favoritos", 2);
                var perfil = await Item(c, device, null, "perfil", "Perfil", "user", "/perfil", 3);
                await Item(c, device, inicio, "buscador", "Buscador", "search", "/", 0);
                await Item(c, device, inicio, "retos", "Retos", "flag", "/#retos", 1);
                await Item(c, device, inicio, "recientes", "Recientes", "clock", "/#recientes", 2);
                await Item(c, device, cat, "detalle-retos", "Retos por juego", "flag", "/categorias", 0);
                await Item(c, device, cat, "recomendaciones", "Recomendaciones del día", "sparkles", "/categorias", 1);
                await Item(c, device, fav, "filtro-favoritos", "Filtrar favoritos", "filter", "/favoritos", 0);
                await Item(c, device, perfil, "datos", "Datos de usuario", "id", "/perfil", 0);
                await Item(c, device, perfil, "membresia", "Membresía", "crown", "/perfil", 1);
            }

        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Games") == 0)
        {
            // Ejemplos: los juegos reales viven en otros servidores (Arcade / Uapeani); aquí solo se registra su metadato y su precio.
            await c.ExecuteAsync("dbo.usp_AddGame", new { Name = "Arcade Demo", Slug = "arcade-demo", Description = "Juego de ejemplo de Arcade", IconUrl = "/icons/arcade.svg",
                LaunchUrl = "https://games.example.com/arcade-demo", CategorySlug = "arcade", IsPro = false, Price = 0m, FeaturedToday = true }, commandType: System.Data.CommandType.StoredProcedure);
            await c.ExecuteAsync("dbo.usp_AddGame", new { Name = "Uapeani Puzzle", Slug = "uapeani-puzzle", Description = "Puzzle de Uapeani", IconUrl = "/icons/puzzle.svg",
                LaunchUrl = "https://games.example.com/uapeani-puzzle", CategorySlug = "puzzle", IsPro = true, Price = 49m, AccessDays = (int?)30 }, commandType: System.Data.CommandType.StoredProcedure);
            await c.ExecuteAsync("""
                INSERT Challenges(Id,GameId,Title,Detail,Points) SELECT NEWID(), Id, 'Primer nivel', 'Completa el nivel 1 sin perder vidas', 100 FROM Games WHERE Slug='arcade-demo';
                INSERT Challenges(Id,GameId,Title,Detail,Points) SELECT NEWID(), Id, 'Sin pistas', 'Resuelve 5 puzzles sin usar pistas', 250 FROM Games WHERE Slug='uapeani-puzzle';
                """);
        }
    }

    private static async Task<Guid> Item(SqlConnection c, DeviceType d, Guid? parent, string key, string label, string icon, string route, int order)
    {
        var id = Guid.NewGuid();
        await c.ExecuteAsync("INSERT MenuItems(Id,Device,ParentId,[Key],Label,Icon,Route,[Order],IsVisible,RequiresAuth) VALUES(@id,@d,@parent,@key,@label,@icon,@route,@order,1,1)",
            new { id, d = (int)d, parent, key, label, icon, route, order });
        return id;
    }
}
