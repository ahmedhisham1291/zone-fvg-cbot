namespace ZoneFvg.Core.Execution;

/// <summary>
/// Hook through which the execution layer records skipped signals (EXE-014) and raises alerts
/// (RSK-002, spec 11). WP07 and WP09 connect it to the journal and to the alert service. Calls
/// are synchronous; an implementation must not throw for an ordinary journal or alert failure.
/// </summary>
public interface IExecutionEvents
{
    /// <summary>Records a signal that did not become an order, with its one reason (EXE-014).</summary>
    /// <param name="skip">The skip record.</param>
    void Skipped(SkipRecord skip);

    /// <summary>Raises an alert (spec 11).</summary>
    /// <param name="alert">The alert.</param>
    void Alert(ExecutionAlert alert);
}
