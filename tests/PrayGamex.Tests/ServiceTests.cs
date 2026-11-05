using System.Security.Cryptography;
using System.Text;
using NSubstitute;
using PrayGamex.Application.Abstractions;
using PrayGamex.Application.Dtos;
using PrayGamex.Application.Services;
using PrayGamex.Domain.Entities;
using PrayGamex.Domain.Enums;
using PrayGamex.Infrastructure.Payments;
using PrayGamex.Infrastructure.Security;

namespace PrayGamex.Tests;

public class SecurityTests
{
    [Fact]
    public void Password_hash_verifies_and_rejects_wrong_password()
    {
        var h = new Pbkdf2PasswordHasher();
        var hash = h.Hash("Passw0rd!!");
        Assert.True(h.Verify("Passw0rd!!", hash));
        Assert.False(h.Verify("otra", hash));
    }

    private static string Sign(string secret, string id, string reqId, string ts) =>
        $"ts={ts},v1=" + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"id:{id};request-id:{reqId};ts:{ts};"))).ToLowerInvariant();

    [Fact]
    public void MercadoPago_signature_accepts_valid_and_rejects_tampered()
    {
        var sig = Sign("secreto", "123456", "req-1", "1700000000");
        Assert.True(MercadoPagoSignature.Verify("secreto", sig, "req-1", "123456"));
        Assert.False(MercadoPagoSignature.Verify("secreto", sig, "req-1", "999999"));   // otro pago
        Assert.False(MercadoPagoSignature.Verify("otro-secreto", sig, "req-1", "123456"));
        Assert.False(MercadoPagoSignature.Verify("secreto", null, "req-1", "123456"));
        Assert.False(MercadoPagoSignature.Verify("", sig, "req-1", "123456"));
    }

    [Fact]
    public void Game_token_roundtrip_and_audience_isolation()
    {
        var opt = Microsoft.Extensions.Options.Options.Create(new JwtOptions { Key = new string('k', 40) });
        var svc = new JwtTokenService(opt);
        var (u, s, g) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var v = svc.ValidateGameToken(svc.CreateGameToken(u, s, g));
        Assert.Equal((u, s, g), v);
        // un access token de usuario (otra audiencia) no sirve como token de lanzamiento
        Assert.Null(svc.ValidateGameToken(svc.CreateAccessToken(new User { Email = "a@b.c" }, s)));
        Assert.Null(svc.ValidateGameToken("basura"));
    }
}

public class AuthServiceTests
{
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly ITokenService tokens = Substitute.For<ITokenService>();
    private readonly AuthService auth;
    public AuthServiceTests()
    {
        tokens.HashToken(Arg.Any<string>()).Returns(ci => "h:" + ci.Arg<string>());
        tokens.CreateRefreshToken().Returns(("nuevo", "h:nuevo"));
        auth = new AuthService(users, new Pbkdf2PasswordHasher(), tokens);
    }

    [Fact]
    public async Task Login_creates_session_limited_to_two()
    {
        var hasher = new Pbkdf2PasswordHasher();
        users.GetByEmailAsync("a@b.com", Arg.Any<CancellationToken>()).Returns(new User { Email = "a@b.com", PasswordHash = hasher.Hash("Passw0rd!!") });
        users.CreateSessionAsync(Arg.Any<Guid>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        await auth.LoginAsync(new LoginRequest("A@B.com", "Passw0rd!!"), default);
        await users.Received(1).CreateSessionAsync(Arg.Any<Guid>(), 2, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Login_with_wrong_password_is_401()
    {
        users.GetByEmailAsync("a@b.com", Arg.Any<CancellationToken>()).Returns(new User { PasswordHash = new Pbkdf2PasswordHasher().Hash("correcta1") });
        var ex = await Assert.ThrowsAsync<AppException>(() => auth.LoginAsync(new LoginRequest("a@b.com", "incorrecta"), default));
        Assert.Equal(401, ex.Status);
    }

    [Fact]
    public async Task Reused_refresh_token_revokes_whole_session()
    {
        var sid = Guid.NewGuid();
        users.GetRefreshTokenAsync("h:viejo", Arg.Any<CancellationToken>())
            .Returns(new RefreshToken { SessionId = sid, ExpiresAt = DateTime.UtcNow.AddDays(1), RevokedAt = DateTime.UtcNow });
        await Assert.ThrowsAsync<AppException>(() => auth.RefreshAsync("viejo", default));
        await users.Received(1).RevokeSessionAsync(sid, Arg.Any<CancellationToken>());
    }
}

public class CatalogServiceTests
{
    private readonly ICatalogRepository catalog = Substitute.For<ICatalogRepository>();
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private static readonly Guid Uid = Guid.NewGuid();

    private CatalogService Svc(GameView g, Membership? m = null, AccountType t = AccountType.Free)
    {
        catalog.GetGameAsync(Uid, g.Id, Arg.Any<CancellationToken>()).Returns(g);
        users.GetByIdAsync(Uid, Arg.Any<CancellationToken>()).Returns(new User { AccountType = t });
        users.GetMembershipAsync(Uid, Arg.Any<CancellationToken>()).Returns(m);
        return new CatalogService(catalog, users);
    }

    [Fact]
    public async Task Demo_game_is_open()
    {
        var g = new GameView { IsPro = false, LaunchUrl = "https://x/demo" };
        Assert.Equal("https://x/demo", await Svc(g).LaunchAsync(Uid, g.Id, default));
    }

    [Fact]
    public async Task Pro_game_without_access_is_403()
    {
        var g = new GameView { IsPro = true, Price = 10, LaunchUrl = "https://x/pro" };
        var ex = await Assert.ThrowsAsync<AppException>(() => Svc(g).LaunchAsync(Uid, g.Id, default));
        Assert.Equal(403, ex.Status);
    }

    [Fact]
    public async Task Pro_game_opens_with_purchase_membership_or_admin()
    {
        var owned = new GameView { IsPro = true, Owned = true, LaunchUrl = "u" };
        Assert.Equal("u", await Svc(owned).LaunchAsync(Uid, owned.Id, default));

        var g = new GameView { IsPro = true, LaunchUrl = "u" };
        var active = new Membership { ExpiresAt = DateTime.UtcNow.AddDays(5) };
        Assert.Equal("u", await Svc(g, active).LaunchAsync(Uid, g.Id, default));

        var expired = new Membership { ExpiresAt = DateTime.UtcNow.AddDays(-1) };
        await Assert.ThrowsAsync<AppException>(() => Svc(g, expired).LaunchAsync(Uid, g.Id, default));

        Assert.Equal("u", await Svc(g, null, AccountType.Admin).LaunchAsync(Uid, g.Id, default));
    }

    [Fact]
    public async Task Locked_pro_game_hides_launch_url_in_listing()
    {
        var g = new GameView { IsPro = true, LaunchUrl = "https://secreto" };
        catalog.SearchAsync(Uid, null, null, false, false, Arg.Any<CancellationToken>()).Returns(new[] { g });
        users.GetByIdAsync(Uid, Arg.Any<CancellationToken>()).Returns(new User());
        var list = await new CatalogService(catalog, users).SearchAsync(Uid, null, null, false, default);
        Assert.True(list[0].Locked);
        Assert.Equal("", list[0].LaunchUrl);
    }
}

public class PaymentServiceTests
{
    private readonly IPaymentRepository payments = Substitute.For<IPaymentRepository>();
    private readonly IUserRepository users = Substitute.For<IUserRepository>();
    private readonly IPaymentGateway gateway = Substitute.For<IPaymentGateway>();
    private readonly PaymentService svc;
    private readonly Payment pay;
    public PaymentServiceTests()
    {
        svc = new PaymentService(payments, Substitute.For<ICatalogRepository>(), users, gateway);
        pay = new Payment { UserId = Guid.NewGuid(), Kind = PaymentKind.Game, GameId = Guid.NewGuid(), Amount = 49m, AccessDays = 30 };
        payments.GetAsync(pay.Id, Arg.Any<CancellationToken>()).Returns(pay);
    }

    [Fact]
    public async Task Underpaid_payment_grants_nothing()
    {
        gateway.GetPaymentAsync("1", Arg.Any<CancellationToken>()).Returns(("approved", pay.Id.ToString(), 1m));
        await svc.HandleWebhookAsync("1", default);
        await payments.DidNotReceiveWithAnyArgs().GrantGameAsync(default, default, default, default);
    }

    [Fact]
    public async Task Approved_payment_grants_game_once()
    {
        gateway.GetPaymentAsync("1", Arg.Any<CancellationToken>()).Returns(("approved", pay.Id.ToString(), 49m));
        payments.TryApproveAsync(pay.Id, "1", Arg.Any<CancellationToken>()).Returns(true, false); // el 2.º webhook ya no gana
        await svc.HandleWebhookAsync("1", default);
        await svc.HandleWebhookAsync("1", default);
        await payments.Received(1).GrantGameAsync(pay.UserId, pay.GameId!.Value, 30, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Approved_membership_extends_and_promotes_free_user()
    {
        var m = new Payment { UserId = Guid.NewGuid(), Kind = PaymentKind.Membership, PlanCode = "premium-monthly", Amount = 99m, AccessDays = 30 };
        payments.GetAsync(m.Id, Arg.Any<CancellationToken>()).Returns(m);
        payments.TryApproveAsync(m.Id, "2", Arg.Any<CancellationToken>()).Returns(true);
        users.GetByIdAsync(m.UserId, Arg.Any<CancellationToken>()).Returns(new User { Id = m.UserId, AccountType = AccountType.Free });
        gateway.GetPaymentAsync("2", Arg.Any<CancellationToken>()).Returns(("approved", m.Id.ToString(), 99m));
        await svc.HandleWebhookAsync("2", default);
        await users.Received(1).ExtendMembershipAsync(m.UserId, "premium-monthly", 30, Arg.Any<CancellationToken>());
        await users.Received(1).SetAccountTypeAsync(m.UserId, AccountType.Premium, Arg.Any<CancellationToken>());
    }
}

public class AdminServiceTests
{
    private readonly AdminService admin = new(Substitute.For<IAdminRepository>());
    private static UpsertGameRequest Req(bool pro = true, decimal price = 10, string slug = "mi-juego", string url = "https://x.com/j") =>
        new("Juego", slug, url, "arcade", null, null, pro, price, "mxn", null, false);

    [Theory]
    [InlineData("Mi Juego")] [InlineData("a_b")] [InlineData("-x")] [InlineData("")]
    public async Task Invalid_slug_is_rejected(string slug) => await Assert.ThrowsAsync<AppException>(() => admin.UpsertAsync(Req(slug: slug), default));

    [Fact] public async Task Demo_with_price_is_rejected() => await Assert.ThrowsAsync<AppException>(() => admin.UpsertAsync(Req(pro: false, price: 5), default));
    [Fact] public async Task Non_http_url_is_rejected() => await Assert.ThrowsAsync<AppException>(() => admin.UpsertAsync(Req(url: "javascript:alert(1)"), default));
    [Fact] public async Task Valid_request_passes() => await admin.UpsertAsync(Req(), default);
}

public class AdminUsersTests
{
    private readonly IAdminRepository repo = Substitute.For<IAdminRepository>();
    private readonly AdminService admin;
    public AdminUsersTests() => admin = new AdminService(repo);

    [Fact]
    public async Task Admin_cannot_change_own_account_type()
    {
        var me = Guid.NewGuid();
        var ex = await Assert.ThrowsAsync<AppException>(() => admin.SetAccountTypeAsync(me, me, AccountType.Free, default));
        Assert.Equal(400, ex.Status);
        await repo.DidNotReceiveWithAnyArgs().SetAccountTypeAsync(default, default, default);
    }

    [Fact]
    public async Task Changing_account_type_closes_target_sessions()
    {
        var target = Guid.NewGuid();
        repo.SetAccountTypeAsync(target, AccountType.Admin, Arg.Any<CancellationToken>()).Returns(true);
        await admin.SetAccountTypeAsync(Guid.NewGuid(), target, AccountType.Admin, default);
        await repo.Received(1).RevokeSessionsAsync(target, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_user_is_404_and_invalid_type_is_400()
    {
        repo.SetAccountTypeAsync(Arg.Any<Guid>(), Arg.Any<AccountType>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => admin.SetAccountTypeAsync(Guid.NewGuid(), Guid.NewGuid(), AccountType.Premium, default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<AppException>(() => admin.SetAccountTypeAsync(Guid.NewGuid(), Guid.NewGuid(), (AccountType)99, default))).Status);
    }

    [Fact]
    public async Task Revoking_missing_permission_is_404()
    {
        repo.RevokeGameAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => admin.RevokeGameAsync(Guid.NewGuid(), Guid.NewGuid(), default))).Status);
    }

    [Theory]
    [InlineData(0)] [InlineData(-5)] [InlineData(4000)]
    public async Task Manual_membership_rejects_invalid_days(int days) =>
        Assert.Equal(400, (await Assert.ThrowsAsync<AppException>(() => admin.GrantMembershipAsync(Guid.NewGuid(), days, default))).Status);

    [Fact]
    public async Task Manual_membership_for_unknown_user_is_404()
    {
        repo.GrantMembershipAsync(Arg.Any<Guid>(), 30, Arg.Any<CancellationToken>()).Returns(false);
        Assert.Equal(404, (await Assert.ThrowsAsync<AppException>(() => admin.GrantMembershipAsync(Guid.NewGuid(), 30, default))).Status);
    }
}