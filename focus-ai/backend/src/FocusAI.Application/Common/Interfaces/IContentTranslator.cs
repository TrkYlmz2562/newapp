namespace FocusAI.Application.Common.Interfaces;

/// <summary>
/// Names of the <see cref="StorySummaryResult"/> fields a producer can declare as
/// still being in the source language. Constants rather than a flags enum because
/// they are also what the translator logs, and a name is readable in a log line.
/// </summary>
public static class SummaryField
{
    public const string Title = "title";
    public const string Dek = "dek";
    public const string Summary = "summary";
    public const string KeyPoints = "keyPoints";
    public const string WhyItMatters = "whyItMatters";
    public const string WhoIsAffected = "whoIsAffected";
    public const string WhatShouldIDo = "whatShouldIDo";

    /// <summary>Everything the extractive fallback emits verbatim from the article.</summary>
    public static readonly IReadOnlyList<string> AllExtracted = [Title, Dek, Summary, KeyPoints];
}

/// <summary>
/// Machine translation into Turkish for text the summariser did not write.
/// </summary>
/// <remarks>
/// This is deliberately NOT a replacement for the LLM summary layer. The model
/// that writes a story's summary is doing editorial work — deciding what matters,
/// what to leave out, how to phrase it — and Turkish is a by-product of that.
/// A translator cannot do any of it.
///
/// What this covers is the path where there is no model at all: the extractive
/// fallback publishes real sentences lifted from the article, which on an English
/// source means a Turkish reader gets an English card. That, and the handful of
/// places where the LLM path falls back to source text, are the whole job.
///
/// Implementations must return exactly as many segments as they were given, in
/// order, and must return the original text for any segment they could not
/// translate confidently. Refusing is expected and is not an error.
/// </remarks>
public interface IContentTranslator
{
    /// <summary>False when no translation backend is configured. Callers skip the work entirely.</summary>
    bool IsEnabled { get; }

    /// <summary>Identifies the backend for logging. "none" when disabled.</summary>
    string Backend { get; }

    /// <summary>
    /// Translates each segment into Turkish, independently. A segment that cannot
    /// be translated safely comes back unchanged, so the result is always usable
    /// and always the same length as the input.
    /// </summary>
    Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> segments,
        string sourceLanguage,
        CancellationToken cancellationToken = default);
}
