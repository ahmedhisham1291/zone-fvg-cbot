using System;

namespace ZoneFvg.Core.Indicators;

/// <summary>A confirmed swing point on the zone timeframe (SIG-006).</summary>
/// <param name="Kind">Whether the bar is a swing high or a swing low.</param>
/// <param name="BarOpenTimeUtc">UTC open time of the swing bar.</param>
/// <param name="Price">The swing level: the high of the bar for a swing high, its low for a swing low.</param>
public sealed record Swing(SwingKind Kind, DateTime BarOpenTimeUtc, double Price);
