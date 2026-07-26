namespace FocusAI.Domain.Common;

/// <summary>
/// Entity whose create/update timestamps are stamped automatically by the
/// persistence layer's SaveChanges interceptor.
/// </summary>
public abstract class AuditableEntity : BaseEntity
{
    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}
