using System;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// The reference day of the Breakout mode (spec section 3.7, SIG-022): the last closed D1 bar.
/// Contract of docs/architecture.md section 3. Data only.
/// </summary>
/// <param name="DayOpenUtc">UTC open time of the reference D1 bar. The breakout id is built from it (SIG-025).</param>
/// <param name="High">High of the reference D1 bar: a buy confirms on a close above it (SIG-023).</param>
/// <param name="Low">Low of the reference D1 bar: a sell confirms on a close below it (SIG-023).</param>
/// <param name="Armed">True while the reference can still confirm; false once it is Done (SIG-022, SIG-023).</param>
public sealed record BreakoutReference(DateTime DayOpenUtc, double High, double Low, bool Armed);
