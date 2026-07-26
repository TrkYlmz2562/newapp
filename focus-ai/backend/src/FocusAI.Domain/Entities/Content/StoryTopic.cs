using FocusAI.Domain.Common;

namespace FocusAI.Domain.Entities.Content;

/// <summary>Weighted story ↔ topic edge. Weight is the tagger's confidence in [0,1].</summary>
public class StoryTopic : BaseEntity
{
    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public Guid TopicId { get; set; }

    public Topic? Topic { get; set; }

    public double Weight { get; set; } = 1.0;
}
