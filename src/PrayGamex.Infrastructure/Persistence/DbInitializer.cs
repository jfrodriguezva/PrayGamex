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
            await SeedAsync(c, cfg);
        }, ct);
    }

    private static async Task SeedAsync(SqlConnection c, IConfiguration cfg)
    {
        var catalog = LoadCatalog();
        if (await c.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM Categories") == 0)
            foreach (var cat in catalog.Categories)
                await c.ExecuteAsync("INSERT Categories(Id,Name,Slug,[Order]) VALUES(NEWID(),@Name,@Slug,@Order)", cat);

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
            // Catálogo real: Arcade (demo, abiertos) y Uapeani (Pro, por ahora solo suscripción hasta fijar precio desde /admin).
            // URLs de prueba en GitHub Pages; cambiarlas desde /admin cuando exista el servidor de juegos (Games:ArcadeBaseUrl / Games:UapeaniBaseUrl).
            var arcadeBase = cfg["Games:ArcadeBaseUrl"] ?? "https://jfrodriguezva.github.io/Arcade/demo/";
            var uapeaniBase = cfg["Games:UapeaniBaseUrl"] ?? "https://jfrodriguezva.github.io/Uapeani/demo/";
            foreach (var g in catalog.Games)
                await c.ExecuteAsync("dbo.usp_AddGame", new
                {
                    Name = g.Name, Slug = g.Slug, Description = "", IconUrl = "emoji:" + g.Icon,
                    LaunchUrl = (g.Family == "uapeani" ? uapeaniBase : arcadeBase) + "?juego=" + Uri.EscapeDataString(g.PublicName),
                    CategorySlug = g.Category, IsPro = g.Pro, Price = 0m, AddToMenu = false, // 100+ juegos: no se llena el menú lateral
                }, commandType: System.Data.CommandType.StoredProcedure);
        }
    }

    private record CatalogCategory(string Slug, string Name, int Order);
    private record CatalogGame(string Name, string Slug, string PublicName, string Category, string Icon, string Family, bool Pro);
    private record CatalogFile(List<CatalogCategory> Categories, List<CatalogGame> Games);

    private static CatalogFile LoadCatalog()
    {
        using var s = typeof(DbInitializer).Assembly.GetManifestResourceStream("PrayGamex.Infrastructure.Persistence.catalogo.json")
            ?? throw new InvalidOperationException("Falta el recurso catalogo.json.");
        return System.Text.Json.JsonSerializer.Deserialize<CatalogFile>(s, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    private static async Task<Guid> Item(SqlConnection c, DeviceType d, Guid? parent, string key, string label, string icon, string route, int order)
    {
        var id = Guid.NewGuid();
        await c.ExecuteAsync("INSERT MenuItems(Id,Device,ParentId,[Key],Label,Icon,Route,[Order],IsVisible,RequiresAuth) VALUES(@id,@d,@parent,@key,@label,@icon,@route,@order,1,1)",
            new { id, d = (int)d, parent, key, label, icon, route, order });
        return id;
    }
}
