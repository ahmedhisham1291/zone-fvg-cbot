namespace ZoneFvg.Core.Indicators;

/// <summary>Kind of swing point (SIG-006).</summary>
public enum SwingKind
{
    /// <summary>Swing high: a bar whose high is strictly above the highs of the bars on each side.</summary>
    High,

    /// <summary>Swing low: a bar whose low is strictly below the lows of the bars on each side.</summary>
    Low,
}
