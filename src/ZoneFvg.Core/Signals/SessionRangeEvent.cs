namespace ZoneFvg.Core.Signals;

/// <summary>
/// An answer of the session range tracker for a closed M15 bar (SIG-031 to SIG-033): a new setup,
/// or the notice that the setup of the trading day is void. Data only.
/// </summary>
/// <param name="Kind">Setup or Void.</param>
/// <param name="Setup">The new setup (Kind Setup), or the setup that became void (Kind Void).</param>
public sealed record SessionRangeEvent(SessionRangeEventKind Kind, SessionRangeSetup Setup);
