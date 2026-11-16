using System;
using ZoneFvg.Core.Execution;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The recovery report of spec 8.3 step 10 as journaled and sent (spec 11, OPS-001 journal
/// format): positions adopted, orders kept or cancelled, strangers, zones active, downtime and
/// the state entered. A data record built by the recovery service (WP07).
/// </summary>
/// <param name="TimeUtc">Server time at the end of recovery, kind UTC.</param>
/// <param name="Symbol">Symbol name.</param>
/// <param name="Trigger">What started the recovery, for example <c>start</c> or <c>reconnect</c>.</param>
/// <param name="PositionsAdopted">Bot positions adopted.</param>
/// <param name="OrdersKept">Pending orders kept.</param>
/// <param name="OrdersCancelled">Pending orders cancelled.</param>
/// <param name="Strangers">Labelled positions whose zone was not found (REC-007).</param>
/// <param name="ZonesActive">Non-terminal zones after the rebuild.</param>
/// <param name="Downtime">Time the bot was away, or null when not known.</param>
/// <param name="ResultState">The state entered after recovery.</param>
public sealed record RecoveryJournalEntry(
    DateTime TimeUtc,
    string Symbol,
    string Trigger,
    int PositionsAdopted,
    int OrdersKept,
    int OrdersCancelled,
    int Strangers,
    int ZonesActive,
    TimeSpan? Downtime,
    BotState ResultState);
