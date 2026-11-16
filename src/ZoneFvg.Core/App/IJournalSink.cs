namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// Journal port (docs/architecture.md section 3): one line per call, under a category such as
/// <c>recovery</c>, <c>state</c>, <c>skip</c>, <c>alert</c>, <c>trade</c> or <c>error</c>. The
/// adapter writes the lines to the log volume (spec 11).
/// </summary>
/// <remarks>
/// The signature is the one in docs/architecture.md section 3. WP09 owns the journal; this file
/// exists because <c>BotCore</c>'s constructor (WP07) needs the port before WP09 is merged. When
/// both are merged, exactly one declaration must remain.
/// </remarks>
public interface IJournalSink
{
    /// <summary>Writes one journal line (REC-010, REC-011, spec 11).</summary>
    /// <param name="category">Short category name.</param>
    /// <param name="line">The line, invariant formatting.</param>
    void Write(string category, string line);
}
