using System;

namespace ZoneFvg.Core.Execution;

/// <summary>
/// A signal that did not become an order, with exactly one reason and the values behind the
/// decision (EXE-014). A data record.
/// </summary>
/// <param name="Symbol">Symbol name.</param>
/// <param name="ZoneId">Id of the zone whose signal was skipped.</param>
/// <param name="Reason">The one reason.</param>
/// <param name="Measured">The measured value that failed, in the unit named by <paramref name="Detail"/> (for example pips, minutes or money); zero when the rule has no number.</param>
/// <param name="Limit">The limit the measured value was compared with; zero when the rule has no number.</param>
/// <param name="Detail">Short invariant text naming the values, for example <c>stop 11.9 pips below 12</c>.</param>
/// <param name="AtUtc">Server time of the decision, kind UTC.</param>
public sealed record SkipRecord(string Symbol, string ZoneId, SkipReason Reason, double Measured, double Limit, string Detail, DateTime AtUtc);
