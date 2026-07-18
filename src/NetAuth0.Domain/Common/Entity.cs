namespace NetAuth0.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; protected set; }
    public uint Version { get; protected set; }

    public void Touch(DateTimeOffset now) => UpdatedAt = now;
}
