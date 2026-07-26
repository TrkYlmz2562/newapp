namespace FocusAI.Domain.Enums;

/// <summary>Answers the PRD section 5.5 question "Abartılıyor mu?" (is it overhyped?).</summary>
public enum HypeLevel
{
    /// <summary>Bigger deal than the coverage suggests.</summary>
    Understated = 0,
    Accurate = 1,
    SlightlyOverhyped = 2,
    Overhyped = 3
}

/// <summary>Answers "Bu teknoloji ölür mü?" (will this technology die?).</summary>
public enum LongevityOutlook
{
    /// <summary>Becoming infrastructure — worth deep investment.</summary>
    Foundational = 0,
    Durable = 1,
    Uncertain = 2,
    Fading = 3
}

/// <summary>Answers "Ne zaman öğrenilmeli?" (when should I learn it?).</summary>
public enum LearnUrgency
{
    Now = 0,
    ThisQuarter = 1,
    Watch = 2,
    Skip = 3
}

/// <summary>
/// Provider-agnostic LLM backends (PRD section 11). Every one of these speaks
/// either its native protocol or the OpenAI-compatible chat/completions shape.
/// </summary>
public enum LlmProviderKind
{
    /// <summary>No key configured — deterministic offline stub keeps the pipeline runnable.</summary>
    Disabled = 0,
    OpenAI = 1,
    Anthropic = 2,
    Gemini = 3,
    OpenRouter = 4,
    Ollama = 5,
    VLlm = 6,
    LmStudio = 7
}

public enum TopicKind
{
    Concept = 0,
    Language = 1,
    Framework = 2,
    Library = 3,
    Company = 4,
    Product = 5,
    Platform = 6,
    Role = 7
}
