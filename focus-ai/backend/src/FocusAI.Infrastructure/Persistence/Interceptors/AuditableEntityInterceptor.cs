using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FocusAI.Infrastructure.Persistence.Interceptors;

/// <summary>
/// Stamps CreatedAt/UpdatedAt so no handler has to remember to. Runs on both the
/// sync and async paths because Hangfire jobs use the sync one.
/// </summary>
public sealed class AuditableEntityInterceptor(IDateTimeProvider clock) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = clock.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.CreatedAt == default:
                    entry.Entity.CreatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;

                // Owned-type edits surface as Unchanged on the principal, but the
                // principal is still logically modified. Restricted to owned
                // navigations: matching every reference would stamp — and so
                // issue a pointless UPDATE for — any loaded entity that merely
                // points at something that changed.
                case EntityState.Unchanged when entry.References.Any(reference =>
                    reference.TargetEntry is { State: EntityState.Modified } target &&
                    target.Metadata.IsOwned()):
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }
}
