using System;
using System.Collections.Generic;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Recovery;

/// <summary>Why the bot is Halted (spec 8.5, RSK-008, RSK-009, RSK-010).</summary>
public enum HaltCause
{
    /// <summary>The drawdown kill switch fired (RSK-008). Lasts until a restart with a new RiskEpochDate.</summary>
    KillSwitch,

    /// <summary>The FTMO emergency guard closed everything (RSK-009). Lasts until a restart with a new RiskEpochDate.</summary>
    EmergencyGuard,

    /// <summary>The request limit would have been exceeded or was exceeded (RSK-010). Lasts until a restart.</summary>
    RequestLimit,

    /// <summary>More unhandled errors in one hour than allowed (RSK-010, REC-011). Lasts until the next CE(S)T day or a restart.</summary>
    ErrorLimit,
}

/// <summary>One state change with its reason (REC-001).</summary>
/// <param name="From">State before.</param>
/// <param name="To">State after.</param>
/// <param name="Reason">Why, in short invariant text.</param>
/// <param name="AtUtc">Server time of the change, kind UTC.</param>
public sealed record StateTransition(BotState From, BotState To, string Reason, DateTime AtUtc);

/// <summary>
/// The bot states of spec 8.5 and their transitions (REC-001). The machine starts in Recovering.
/// Only Running permits new orders. Every change carries a reason and a server time.
/// </summary>
/// <remarks>
/// <para>Recovering, Running and Blocked can change into each other in any direction with
/// <see cref="Transition"/>. Halted is entered only with <see cref="Halt"/>, which names a
/// cause, and left only with <see cref="ReleaseHalt"/>.</para>
/// <para>Spec 8.5 lets Halted end automatically only after an error storm, at the next CE(S)T
/// day. A kill switch or emergency guard needs a new RiskEpochDate and a request-limit halt has
/// no automatic exit; both end only when the process is restarted, which creates a new machine.
/// While any such cause is present, the halt cannot be released.</para>
/// <para>A transition to the same state is rejected; it would carry no information.</para>
/// </remarks>
public sealed class BotStateMachine
{
    private readonly SortedSet<HaltCause> _haltCauses = new SortedSet<HaltCause>();
    private DateTime? _lastErrorHaltUtc;

    /// <summary>Creates a machine in state Recovering (REC-001, spec 8.3: every start begins with recovery).</summary>
    public BotStateMachine()
    {
        State = BotState.Recovering;
    }

    /// <summary>The current state (REC-001).</summary>
    public BotState State { get; private set; }

    /// <summary>True only in Running (REC-001, EXE-002).</summary>
    public bool AllowsNewOrders => PermitsNewOrders(State);

    /// <summary>The last change, or null before the first (REC-001).</summary>
    public StateTransition? LastTransition { get; private set; }

    /// <summary>The causes of the current halt in enum order; empty when not Halted (REC-001).</summary>
    public IReadOnlyList<HaltCause> HaltCauses => new List<HaltCause>(_haltCauses).AsReadOnly();

    /// <summary>True when the state permits new orders: Running only (REC-001).</summary>
    /// <param name="state">A state.</param>
    /// <returns>True for Running.</returns>
    public static bool PermitsNewOrders(BotState state)
    {
        return state == BotState.Running;
    }

    /// <summary>
    /// True when spec 8.5 has a transition between the two states (REC-001): every pair of two
    /// different defined states. Entering and leaving Halted have further conditions, checked by
    /// <see cref="Halt"/> and <see cref="ReleaseHalt"/>.
    /// </summary>
    /// <param name="from">State before.</param>
    /// <param name="to">State after.</param>
    /// <returns>True when the pair is a transition.</returns>
    public static bool IsLegal(BotState from, BotState to)
    {
        return Enum.IsDefined(typeof(BotState), from) && Enum.IsDefined(typeof(BotState), to) && from != to;
    }

    /// <summary>
    /// Changes between Recovering, Running and Blocked (REC-001).
    /// </summary>
    /// <param name="to">The new state; not Halted.</param>
    /// <param name="reason">Why; not empty.</param>
    /// <param name="atUtc">Server time, kind UTC.</param>
    /// <returns>The transition.</returns>
    /// <exception cref="ArgumentException">The reason is empty or the time is not UTC.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The state is not defined.</exception>
    /// <exception cref="InvalidOperationException">The state is the current one, the new state is Halted, or the machine is Halted.</exception>
    public StateTransition Transition(BotState to, string reason, DateTime atUtc)
    {
        RequireReasonAndTime(reason, atUtc);
        RequireDefined(to);
        if (to == BotState.Halted)
        {
            throw new InvalidOperationException("Halted is entered only with a cause (REC-001).");
        }

        if (State == BotState.Halted)
        {
            throw new InvalidOperationException("Halted is left only by releasing the halt (REC-001).");
        }

        return Apply(to, reason, atUtc);
    }

    /// <summary>
    /// Enters Halted with a cause, or adds the cause when already Halted (REC-001, spec 8.5).
    /// </summary>
    /// <param name="cause">The cause.</param>
    /// <param name="reason">Why; not empty.</param>
    /// <param name="atUtc">Server time, kind UTC.</param>
    /// <returns>The transition, or null when the machine was already Halted.</returns>
    /// <exception cref="ArgumentException">The reason is empty or the time is not UTC.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The cause is not defined.</exception>
    public StateTransition? Halt(HaltCause cause, string reason, DateTime atUtc)
    {
        RequireReasonAndTime(reason, atUtc);
        if (!Enum.IsDefined(typeof(HaltCause), cause))
        {
            throw new ArgumentOutOfRangeException(nameof(cause), "Unknown halt cause.");
        }

        _haltCauses.Add(cause);
        if (cause == HaltCause.ErrorLimit)
        {
            _lastErrorHaltUtc = atUtc;
        }

        return State == BotState.Halted ? null : Apply(BotState.Halted, reason, atUtc);
    }

    /// <summary>
    /// True when the halt may end at the server time (REC-001, spec 8.5): the machine is Halted,
    /// its only cause is an error storm, and the CE(S)T day of the time is later than the day of
    /// the last error storm.
    /// </summary>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>True when the halt can be released.</returns>
    /// <exception cref="ArgumentException">The time is not UTC.</exception>
    public bool CanReleaseHalt(DateTime nowUtc)
    {
        RequireUtc(nowUtc);
        if (State != BotState.Halted || _haltCauses.Count != 1 || !_haltCauses.Contains(HaltCause.ErrorLimit) || !_lastErrorHaltUtc.HasValue)
        {
            return false;
        }

        return CestClock.StartOfCestDayUtc(nowUtc) > CestClock.StartOfCestDayUtc(_lastErrorHaltUtc.Value);
    }

    /// <summary>Leaves Halted after an error storm on an earlier CE(S)T day (REC-001, spec 8.5).</summary>
    /// <param name="to">The new state; not Halted.</param>
    /// <param name="reason">Why; not empty.</param>
    /// <param name="nowUtc">Server time, kind UTC.</param>
    /// <returns>The transition.</returns>
    /// <exception cref="ArgumentException">The reason is empty or the time is not UTC.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The state is not defined.</exception>
    /// <exception cref="InvalidOperationException">The new state is Halted or <see cref="CanReleaseHalt"/> is false.</exception>
    public StateTransition ReleaseHalt(BotState to, string reason, DateTime nowUtc)
    {
        RequireReasonAndTime(reason, nowUtc);
        RequireDefined(to);
        if (to == BotState.Halted || !CanReleaseHalt(nowUtc))
        {
            throw new InvalidOperationException("The halt cannot be released now (REC-001).");
        }

        _haltCauses.Clear();
        _lastErrorHaltUtc = null;
        return Apply(to, reason, nowUtc);
    }

    private StateTransition Apply(BotState to, string reason, DateTime atUtc)
    {
        if (!IsLegal(State, to))
        {
            throw new InvalidOperationException("No transition from " + State + " to " + to + " (REC-001).");
        }

        var transition = new StateTransition(State, to, reason, atUtc);
        State = to;
        LastTransition = transition;
        return transition;
    }

    private static void RequireDefined(BotState state)
    {
        if (!Enum.IsDefined(typeof(BotState), state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), "Unknown bot state.");
        }
    }

    private static void RequireReasonAndTime(string reason, DateTime atUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A transition needs a reason (REC-001).", nameof(reason));
        }

        RequireUtc(atUtc);
    }

    private static void RequireUtc(DateTime atUtc)
    {
        if (atUtc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time must be a UTC time (kind UTC).", nameof(atUtc));
        }
    }
}
