using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// REC-001: the states Recovering, Running, Blocked and Halted with the transitions of spec 8.5.
/// Every transition carries a reason. Only Running permits new orders. Halted is entered only with
/// a cause; it is left only after an error storm, at the next CE(S)T day. A kill switch, an
/// emergency guard or a request-limit halt lasts until the process is restarted.
/// </summary>
public class BotStateMachineTests
{
    // Wednesday 2026-03-04 10:00 UTC = 11:00 CET; the CE(S)T day began 2026-03-03 23:00 UTC.
    private static readonly DateTime Ten = new(2026, 3, 4, 10, 0, 0, DateTimeKind.Utc);

    public static IEnumerable<object[]> OrdinaryTransitions()
    {
        BotState[] states = { BotState.Recovering, BotState.Running, BotState.Blocked };
        foreach (BotState from in states)
        {
            foreach (BotState to in states)
            {
                if (from != to)
                {
                    yield return new object[] { from, to };
                }
            }
        }
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void Constructor_StartsInRecovering_WithoutNewOrders()
    {
        var machine = new BotStateMachine();

        Assert.Equal(BotState.Recovering, machine.State);
        Assert.False(machine.AllowsNewOrders);
        Assert.Null(machine.LastTransition);
        Assert.Empty(machine.HaltCauses);
    }

    [Theory]
    [Trait("Req", "REC-001")]
    [MemberData(nameof(OrdinaryTransitions))]
    public void Transition_EveryLegalPairOutsideHalted_IsAcceptedWithItsReason(BotState from, BotState to)
    {
        BotStateMachine machine = MachineIn(from);

        StateTransition transition = machine.Transition(to, "because", Ten.AddMinutes(5.0));

        Assert.Equal(new StateTransition(from, to, "because", Ten.AddMinutes(5.0)), transition);
        Assert.Equal(to, machine.State);
        Assert.Same(transition, machine.LastTransition);
        Assert.Equal(to == BotState.Running, machine.AllowsNewOrders);
    }

    [Theory]
    [Trait("Req", "REC-001")]
    [InlineData(BotState.Recovering)]
    [InlineData(BotState.Running)]
    [InlineData(BotState.Blocked)]
    public void Halt_FromEveryOtherState_EntersHaltedWithTheCause(BotState from)
    {
        BotStateMachine machine = MachineIn(from);

        StateTransition? transition = machine.Halt(HaltCause.KillSwitch, "drawdown 8%", Ten);

        Assert.Equal(new StateTransition(from, BotState.Halted, "drawdown 8%", Ten), transition);
        Assert.Equal(BotState.Halted, machine.State);
        Assert.False(machine.AllowsNewOrders);
        Assert.Equal(new[] { HaltCause.KillSwitch }, machine.HaltCauses);
    }

    [Theory]
    [Trait("Req", "REC-001")]
    [InlineData(BotState.Recovering)]
    [InlineData(BotState.Running)]
    [InlineData(BotState.Blocked)]
    public void ReleaseHalt_AfterAnErrorStormOnTheNextCestDay_GoesToEveryOtherState(BotState to)
    {
        BotStateMachine machine = MachineIn(BotState.Running);
        machine.Halt(HaltCause.ErrorLimit, "errors in the last hour 6 above 5", Ten);

        // 2026-03-04 23:00 UTC is 00:00 CET on 5 March: the next CE(S)T day.
        DateTime nextDay = new(2026, 3, 4, 23, 0, 0, DateTimeKind.Utc);
        StateTransition transition = machine.ReleaseHalt(to, "next CE(S)T day", nextDay);

        Assert.Equal(new StateTransition(BotState.Halted, to, "next CE(S)T day", nextDay), transition);
        Assert.Equal(to, machine.State);
        Assert.Empty(machine.HaltCauses);
    }

    [Theory]
    [Trait("Req", "REC-001")]
    [InlineData(BotState.Recovering)]
    [InlineData(BotState.Running)]
    [InlineData(BotState.Blocked)]
    public void Transition_ToTheSameState_IsRejected(BotState state)
    {
        BotStateMachine machine = MachineIn(state);

        Assert.Throws<InvalidOperationException>(() => machine.Transition(state, "again", Ten));
        Assert.Equal(state, machine.State);
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void Transition_ToHaltedWithoutACause_IsRejected()
    {
        var machine = new BotStateMachine();

        Assert.Throws<InvalidOperationException>(() => machine.Transition(BotState.Halted, "no cause", Ten));
        Assert.Equal(BotState.Recovering, machine.State);
    }

    [Theory]
    [Trait("Req", "REC-001")]
    [InlineData(BotState.Recovering)]
    [InlineData(BotState.Running)]
    [InlineData(BotState.Blocked)]
    public void Transition_OutOfHaltedWithoutRelease_IsRejected(BotState to)
    {
        BotStateMachine machine = MachineIn(BotState.Running);
        machine.Halt(HaltCause.ErrorLimit, "errors", Ten);

        Assert.Throws<InvalidOperationException>(() => machine.Transition(to, "try", Ten.AddDays(2.0)));
        Assert.Equal(BotState.Halted, machine.State);
    }

    [Theory]
    [Trait("Req", "REC-001")]
    [InlineData(HaltCause.KillSwitch)]
    [InlineData(HaltCause.EmergencyGuard)]
    [InlineData(HaltCause.RequestLimit)]
    public void ReleaseHalt_ForACauseThatNeedsARestart_IsRejectedEvenDaysLater(HaltCause cause)
    {
        BotStateMachine machine = MachineIn(BotState.Running);
        machine.Halt(cause, "halt", Ten);

        Assert.False(machine.CanReleaseHalt(Ten.AddDays(10.0)));
        Assert.Throws<InvalidOperationException>(() => machine.ReleaseHalt(BotState.Running, "try", Ten.AddDays(10.0)));
        Assert.Equal(BotState.Halted, machine.State);
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void ReleaseHalt_ErrorStormOnTheSameCestDay_IsRejected()
    {
        BotStateMachine machine = MachineIn(BotState.Running);
        machine.Halt(HaltCause.ErrorLimit, "errors", Ten);

        // 22:59:59 UTC is still 4 March in CET (23:59:59).
        DateTime lastSecond = new(2026, 3, 4, 22, 59, 59, DateTimeKind.Utc);

        Assert.False(machine.CanReleaseHalt(lastSecond));
        Assert.Throws<InvalidOperationException>(() => machine.ReleaseHalt(BotState.Running, "try", lastSecond));
        Assert.True(machine.CanReleaseHalt(lastSecond.AddSeconds(1.0)));
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void ReleaseHalt_ErrorStormPlusKillSwitch_IsRejected()
    {
        BotStateMachine machine = MachineIn(BotState.Running);
        machine.Halt(HaltCause.ErrorLimit, "errors", Ten);
        StateTransition? second = machine.Halt(HaltCause.KillSwitch, "drawdown", Ten.AddMinutes(1.0));

        Assert.Null(second);
        Assert.Equal(new[] { HaltCause.KillSwitch, HaltCause.ErrorLimit }, machine.HaltCauses);
        Assert.False(machine.CanReleaseHalt(Ten.AddDays(3.0)));
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void ReleaseHalt_ToHalted_IsRejected()
    {
        BotStateMachine machine = MachineIn(BotState.Running);
        machine.Halt(HaltCause.ErrorLimit, "errors", Ten);

        Assert.Throws<InvalidOperationException>(() => machine.ReleaseHalt(BotState.Halted, "try", Ten.AddDays(1.0)));
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void ReleaseHalt_WhenNotHalted_IsRejected()
    {
        BotStateMachine machine = MachineIn(BotState.Running);

        Assert.False(machine.CanReleaseHalt(Ten.AddDays(1.0)));
        Assert.Throws<InvalidOperationException>(() => machine.ReleaseHalt(BotState.Blocked, "try", Ten.AddDays(1.0)));
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void ErrorStorm_RepeatedOnALaterDay_MovesTheReleaseDay()
    {
        // A second error storm on the next day restarts the wait: the release is on the day after it.
        BotStateMachine machine = MachineIn(BotState.Running);
        machine.Halt(HaltCause.ErrorLimit, "errors", Ten);
        machine.Halt(HaltCause.ErrorLimit, "errors again", Ten.AddDays(1.0));

        Assert.False(machine.CanReleaseHalt(Ten.AddDays(1.0).AddHours(1.0)));
        Assert.True(machine.CanReleaseHalt(Ten.AddDays(2.0)));
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void Arguments_UndefinedStateOrCause_EmptyReasonOrNonUtcTime_AreRejected()
    {
        var machine = new BotStateMachine();

        Assert.Throws<ArgumentOutOfRangeException>(() => machine.Transition((BotState)9, "x", Ten));
        Assert.Throws<ArgumentOutOfRangeException>(() => machine.Halt((HaltCause)9, "x", Ten));
        Assert.Throws<ArgumentException>(() => machine.Transition(BotState.Running, string.Empty, Ten));
        Assert.Throws<ArgumentException>(() => machine.Transition(BotState.Running, "x", DateTime.SpecifyKind(Ten, DateTimeKind.Unspecified)));
        Assert.Throws<ArgumentException>(() => machine.Halt(HaltCause.KillSwitch, " ", Ten));
        Assert.Throws<ArgumentException>(() => machine.CanReleaseHalt(DateTime.SpecifyKind(Ten, DateTimeKind.Local)));
        Assert.Equal(BotState.Recovering, machine.State);
        Assert.Empty(machine.HaltCauses);
    }

    [Theory]
    [Trait("Req", "REC-001")]
    [InlineData(BotState.Recovering, false)]
    [InlineData(BotState.Running, true)]
    [InlineData(BotState.Blocked, false)]
    [InlineData(BotState.Halted, false)]
    public void IsLegal_ListsEveryDistinctPairAndNoSelfTransition(BotState state, bool ordersAllowed)
    {
        foreach (BotState other in Enum.GetValues<BotState>())
        {
            Assert.Equal(other != state, BotStateMachine.IsLegal(state, other));
        }

        Assert.False(BotStateMachine.IsLegal(state, (BotState)9));
        Assert.Equal(ordersAllowed, BotStateMachine.PermitsNewOrders(state));
    }

    private static BotStateMachine MachineIn(BotState state)
    {
        var machine = new BotStateMachine();
        if (state == BotState.Running || state == BotState.Blocked)
        {
            machine.Transition(state, "set-up", Ten);
        }
        else if (state == BotState.Halted)
        {
            machine.Halt(HaltCause.ErrorLimit, "set-up", Ten);
        }

        return machine;
    }
}
