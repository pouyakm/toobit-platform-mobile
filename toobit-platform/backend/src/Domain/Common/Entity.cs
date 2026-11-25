namespace Toobit.Platform.Domain.Common;

/// <summary>Every table: Id, CreatedAt, UpdatedAt, RowVersion (Postgres xmin, optimistic concurrency).</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Uuid7.NewGuid();
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint RowVersion { get; set; }
}
