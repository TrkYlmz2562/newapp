namespace FocusAI.Domain.Common;

/// <summary>
/// Root of every persisted aggregate. Uses UUIDv7 so primary keys stay
/// time-ordered — important for PostgreSQL index locality on a table that
/// grows by thousands of rows a day.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
}
