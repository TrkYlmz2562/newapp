namespace FocusAI.Domain.Enums;

/// <summary>
/// How far the actor has bound itself, according to the text — not how likely the
/// event is. Certainty here is a property of the document, so a reader can check
/// it and so can a test; a predicted probability is neither.
/// </summary>
/// <remarks>
/// Ordinal and append-only: higher means less committed. The numbering is
/// persisted and the publication gate depends on it, so members must never be
/// renumbered or reordered.
/// </remarks>
public enum CommitmentTier
{
    /// <summary>Not classified — never publishable.</summary>
    Unknown = 0,

    /// <summary>"Gerçekleşti" — already happened and is on the record.</summary>
    Realized = 1,

    /// <summary>"Yürürlükte / Tarihi belli" — a binding instrument fixes a future date.</summary>
    EnactedDated = 2,

    /// <summary>"Resmî taahhüt" — the deciding authority announced its own dated decision.</summary>
    OfficialCommitment = 3,

    /// <summary>"Şarta bağlı" — a real agreement waiting on a named approval.</summary>
    ConditionalPending = 4,

    /// <summary>"Niyet / hedef" — intent, plan or target with no binding instrument.</summary>
    StatedIntent = 5,

    /// <summary>"Doğrulanmamış iddia" — unnamed sources, hearsay, the -mış evidential.</summary>
    UnverifiedClaim = 6,

    /// <summary>"Tahmin / yorum" — an analyst's expectation, target or scenario.</summary>
    AnalystSpeculation = 7
}

/// <summary>How far away the event is. Computed in code, never by the model.</summary>
public enum EventHorizon
{
    Unknown = 0,

    /// <summary>Already happened or already in force.</summary>
    Completed = 1,

    /// <summary>Within a week.</summary>
    Imminent = 2,

    /// <summary>8–90 days — roughly a quarter, which is how corporate calendars cluster.</summary>
    Near = 3,

    /// <summary>91–365 days.</summary>
    Mid = 4,

    /// <summary>More than a year out.</summary>
    Long = 5,

    /// <summary>No resolvable date. "Yakında" is not a date.</summary>
    Undated = 6
}

public enum DatePrecision
{
    None = 0,

    /// <summary>A specific day.</summary>
    Day = 1,

    /// <summary>A month, quarter or year.</summary>
    Window = 2
}

/// <summary>The document that makes a claim checkable. "Dayanak" on the card.</summary>
public enum FinanceInstrument
{
    None = 0,
    ResmiGazete = 1,
    Kap = 2,

    /// <summary>A decision published by the institution that holds the decision right.</summary>
    KurumKarari = 3,
    Mahkeme = 4,
    Sozlesme = 5,

    /// <summary>A published institutional calendar (PPK, TÜİK, Hazine).</summary>
    ResmiTakvim = 6
}

/// <summary>Who is making the assertion — the second axis of certainty.</summary>
public enum ClaimSource
{
    Unknown = 0,
    OfficialDocument = 1,
    ActorItself = 2,
    NamedThirdParty = 3,
    UnnamedSource = 4,
    OutletInference = 5
}
