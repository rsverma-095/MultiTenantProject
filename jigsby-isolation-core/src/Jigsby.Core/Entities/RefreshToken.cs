namespace Jigsby.Core.Entities;

public sealed class RefreshToken
{
    public Guid      Id              { get; set; } = Guid.NewGuid();
    public Guid      UserId          { get; set; }
    public string    Token           { get; set; } = string.Empty;
    public DateTime  CreatedAt       { get; set; }
    public DateTime  ExpiresAt       { get; set; }
    public DateTime? RevokedAt       { get; set; }
    public string?   ReplacedByToken { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;

    public ApplicationUser User { get; set; } = null!;
}
