namespace ZoneFvg.Core.Execution;

/// <summary>
/// Bot states of spec 8.5. Only <see cref="Running"/> permits new orders (EXE-002). WP07 owns the
/// transitions (REC-001); WP06 only reads the current state.
/// </summary>
public enum BotState
{
    /// <summary>Recovery is running; no new orders.</summary>
    Recovering,

    /// <summary>New orders are possible.</summary>
    Running,

    /// <summary>A cause such as an unavailable calendar or an exhausted daily budget blocks new orders.</summary>
    Blocked,

    /// <summary>Kill switch, emergency guard or an error storm; no new orders.</summary>
    Halted,
}
