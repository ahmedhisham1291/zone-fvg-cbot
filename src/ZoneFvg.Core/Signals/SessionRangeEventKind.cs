namespace ZoneFvg.Core.Signals;

/// <summary>What the session range tracker reports for a closed M15 bar (SIG-031 to SIG-033).</summary>
public enum SessionRangeEventKind
{
    /// <summary>The trading day has a setup (SIG-031, SIG-032).</summary>
    Setup,

    /// <summary>The setup of the trading day is void: price reached the other edge of the range first (SIG-033).</summary>
    Void,
}
