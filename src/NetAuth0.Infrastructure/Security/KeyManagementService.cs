using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using NetAuth0.Application.Abstractions;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Infrastructure.Security;

public sealed class KeyManagementService(IAppDbContext db, IClock clock) : IKeyManagementService
{
    public async Task<string> RotateAsync(CancellationToken cancellationToken)
    {
        var current = await db.SigningKeys.Where(x => x.RetiredAt == null).ToListAsync(cancellationToken);
        foreach (var key in current)
        {
            key.Retire(clock.UtcNow);
        }

        using var rsa = RSA.Create(2048);
        var newKey = new SigningKey($"kid_{Guid.NewGuid():N}", rsa.ExportRSAPrivateKeyPem(), clock.UtcNow);
        db.SigningKeys.Add(newKey);
        await db.SaveChangesAsync(cancellationToken);
        return newKey.KeyId;
    }
}
