using System;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// A fair value gap on the M15 timeframe that confirms a touched zone (SIG-016). Contract of
/// docs/architecture.md section 3. With c3, c2, c1 the three bars that form it (c1 newest), a
/// bullish gap runs from high(c3) up to low(c1) and a bearish gap from high(c1) up to low(c3).
/// </summary>
/// <param name="Side">Buy for a bullish gap (demand zone), Sell for a bearish gap (supply zone).</param>
/// <param name="Upper">Upper edge of the gap: low(c1) for a bullish gap, low(c3) for a bearish gap (SIG-016).</param>
/// <param name="Lower">Lower edge of the gap: high(c3) for a bullish gap, high(c1) for a bearish gap (SIG-016).</param>
/// <param name="C3OpenUtc">UTC open time of c3, the oldest of the three bars (SIG-016, SIG-017).</param>
/// <param name="C2OpenUtc">UTC open time of c2, the middle bar (SIG-016, SIG-017).</param>
/// <param name="C1OpenUtc">UTC open time of c1, the newest bar, whose close confirms the gap (SIG-016, SIG-017).</param>
public sealed record Fvg(Side Side, double Upper, double Lower, DateTime C3OpenUtc, DateTime C2OpenUtc, DateTime C1OpenUtc);
