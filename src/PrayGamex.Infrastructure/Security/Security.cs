using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PrayGamex.Application.Abstractions;
using PrayGamex.Domain.Entities;
namespace PrayGamex.Infrastructure.Security;

public class JwtOptions { public string Issuer { get; set; } = "PrayGamex"; public string Audience { get; set; } = "PrayGamex"; public string Key { get; set; } = ""; public int AccessMinutes { get; set; } = 15; public int GameTokenMinutes { get; set; } = 240; public string GameAudience => Audience + ".Game"; }

public class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iter = 210_000;
    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iter, HashAlgorithmName.SHA256, 32);
        return $"{Iter}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }
    public bool Verify(string password, string hash)
    {
        var p = hash.Split('.');
        if (p.Length != 3) return false;
        var key = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[1]), int.Parse(p[0]), HashAlgorithmName.SHA256, 32);
        return CryptographicOperations.FixedTimeEquals(key, Convert.FromBase64String(p[2]));
    }
}

public class JwtTokenService(IOptions<JwtOptions> opt) : ITokenService
{
    private readonly JwtOptions o = opt.Value;
    public string CreateAccessToken(User user, Guid sessionId)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("sid", sessionId.ToString()),
            new Claim(ClaimTypes.Role, user.AccountType.ToString()),
        };
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, expires: DateTime.UtcNow.AddMinutes(o.AccessMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public string CreateGameToken(Guid userId, Guid sessionId, Guid gameId)
    {
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), SecurityAlgorithms.HmacSha256);
        var claims = new[] { new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()), new Claim("sid", sessionId.ToString()), new Claim("gid", gameId.ToString()) };
        var token = new JwtSecurityToken(o.Issuer, o.GameAudience, claims, expires: DateTime.UtcNow.AddMinutes(o.GameTokenMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
    public (Guid UserId, Guid SessionId, Guid GameId)? ValidateGameToken(string token)
    {
        try
        {
            var p = new JwtSecurityTokenHandler { MapInboundClaims = false }.ValidateToken(token, new TokenValidationParameters
            {
                ValidIssuer = o.Issuer, ValidAudience = o.GameAudience, ValidateIssuer = true, ValidateAudience = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Key)), ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);
            string? C(string n) => p.FindFirst(n)?.Value;
            if (Guid.TryParse(C("sub"), out var u) && Guid.TryParse(C("sid"), out var s) && Guid.TryParse(C("gid"), out var g)) return (u, s, g);
        }
        catch (Exception) { }
        return null;
    }

    public (string Token, string Hash) CreateRefreshToken()
    {
        var t = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (t, HashToken(t));
    }
    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
