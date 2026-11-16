namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// Journal port (docs/architecture.md section 3). The adapter writes each line to the log files on
/// the mounted volume (spec 11); tests use a fake. Every line that reaches this port has passed
/// through <see cref="ZoneFvg.Core.Ops.SecretMasker"/> (OPS-007). An implementation should not
/// throw; <see cref="ZoneFvg.Core.Ops.Journal"/> counts a write that throws and carries on
/// (OPS-001, OPS-002).
/// </summary>
public interface IJournalSink
{
    /// <summary>Writes one journal line (OPS-001, OPS-002, OPS-007).</summary>
    /// <param name="category">Category of the line, for example <c>signal</c> or <c>trade</c>; also the second word of the line.</param>
    /// <param name="line">The complete line, already masked, without a line break.</param>
    void Write(string category, string line);
}
