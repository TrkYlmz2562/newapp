using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;

namespace FocusAI.Domain.Entities.Users;

public class Bookmark : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public string? Note { get; set; }

    public List<string> Tags { get; set; } = [];
}
