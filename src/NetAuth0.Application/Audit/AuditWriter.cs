using System.Text.Json;
using NetAuth0.Application.Abstractions;
using NetAuth0.Domain.Identity;

namespace NetAuth0.Application.Audit;

public interface IAuditWriter
{
    Task WriteAsync(Guid tenantId, string eventType, Guid? actorUserId, string? actorClientId, RequestContext context, object? data, CancellationToken cancellationToken);
}

public sealed class AuditWriter(IAppDbContext db) : IAuditWriter
{
    public async Task WriteAsync(Guid tenantId, string eventType, Guid? actorUserId, string? actorClientId, RequestContext context, object? data, CancellationToken cancellationToken)
    {
        var json = data is null ? "{}" : JsonSerializer.Serialize(data);
        db.AuditEvents.Add(new AuditEvent(tenantId, eventType, actorUserId, actorClientId, context.IpAddress, context.UserAgent, json));
        await db.SaveChangesAsync(cancellationToken);
    }
}
