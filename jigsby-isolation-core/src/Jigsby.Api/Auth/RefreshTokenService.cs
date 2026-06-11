using System.Security.Cryptography;
using Jigsby.Core.Entities;
using Jigsby.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Jigsby.Api.Auth;

public sealed class RefreshTokenService
{
    private readonly AppDbContext  _db;
    private readonly IConfiguration _config;

    public RefreshTokenService(AppDbContext db, IConfiguration config)
    {
        _db     = db;
        _config = config;
    }

    public async Task<RefreshToken> CreateAsync(Guid userId)
    {
        var expiryDays = int.Parse(_config["Jwt:RefreshTokenExpiryDays"]!);

        var refreshToken = new RefreshToken
        {
            UserId    = userId,
            Token     = GenerateToken(),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(expiryDays)
        };

        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync();
        return refreshToken;
    }

    public async Task<RefreshToken?> GetValidAsync(string token)
        => await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == token && rt.RevokedAt == null && rt.ExpiresAt > DateTime.UtcNow);

    public async Task RevokeAsync(RefreshToken token, string? replacedByToken = null)
    {
        token.RevokedAt       = DateTime.UtcNow;
        token.ReplacedByToken = replacedByToken;
        await _db.SaveChangesAsync();
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Convert.ToBase64String(bytes);
    }
}
