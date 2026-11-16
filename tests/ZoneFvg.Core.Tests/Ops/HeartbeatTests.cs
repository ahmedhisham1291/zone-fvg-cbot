using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-005: the heartbeat calls HeartbeatUrl once per five minutes of server time, in every bot
/// state; a failed call is journaled and never affects the caller.
/// </summary>
public class HeartbeatTests
{
    private static readonly DateTime T0 = OpsData.T0;

    private static (Heartbeat Heartbeat, ScriptedTransport Transport, RecordingSink Sink) Create(OpsParameters? parameters = null)
    {
        OpsParameters p = parameters ?? OpsData.WithSecrets;
        var sink = new RecordingSink();
        var transport = new ScriptedTransport();
        var masker = SecretMasker.For(p);
        return (new Heartbeat(p, transport, new Journal(sink, masker), masker), transport, sink);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_FirstCall_CallsTheUrlAtOnce()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, RecordingSink sink) = Create();

        Assert.True(heartbeat.Pump(T0, BotState.Running));

        Assert.Equal(new[] { (OpsData.HeartbeatUrl, 5) }, transport.Calls);
        Assert.Equal(T0, heartbeat.LastCallUtc);
        Assert.Equal(1, heartbeat.Calls);
        Assert.Empty(sink.Lines);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_ExactlyFiveMinutesAfterLastCall_CallsAgainButNotOneSecondBefore()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, _) = Create();
        heartbeat.Pump(T0, BotState.Running);

        Assert.False(heartbeat.Pump(T0.AddMinutes(4.0).AddSeconds(59.0), BotState.Running));
        Assert.Single(transport.Calls);
        Assert.True(heartbeat.Pump(T0.AddMinutes(5.0), BotState.Running));
        Assert.Equal(2, transport.Calls.Count);
        Assert.False(heartbeat.Pump(T0.AddMinutes(9.0).AddSeconds(59.0), BotState.Running));
        Assert.True(heartbeat.Pump(T0.AddMinutes(10.0), BotState.Running));
        Assert.Equal(3, transport.Calls.Count);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_OneSecondAfterTheInterval_CallsAndTheNextIntervalCountsFromThatCall()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, _) = Create();
        heartbeat.Pump(T0, BotState.Running);

        Assert.True(heartbeat.Pump(T0.AddMinutes(5.0).AddSeconds(1.0), BotState.Running));
        Assert.False(heartbeat.Pump(T0.AddMinutes(10.0), BotState.Running));
        Assert.True(heartbeat.Pump(T0.AddMinutes(10.0).AddSeconds(1.0), BotState.Running));
        Assert.Equal(3, transport.Calls.Count);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_EveryMinuteForAnHour_CallsTwelveTimes()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, _) = Create();

        for (int minute = 0; minute < 60; minute++)
        {
            heartbeat.Pump(T0.AddMinutes(minute), BotState.Running);
        }

        Assert.Equal(12, transport.Calls.Count);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_CallFails_IsJournaledAndNotRetriedBeforeTheInterval()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, RecordingSink sink) = Create();
        transport.Then(ScriptedTransport.Failed(503));

        Assert.True(heartbeat.Pump(T0, BotState.Running));

        Assert.Equal(new[] { "2026-01-14T10:00:00Z heartbeat event=failed status=503 state=Running failures=1 error=" }, sink.Of("heartbeat"));
        Assert.False(heartbeat.Pump(T0.AddMinutes(1.0), BotState.Running));
        Assert.Single(transport.Calls);
        Assert.Equal(1, heartbeat.Failures);
        Assert.True(heartbeat.Pump(T0.AddMinutes(5.0), BotState.Running));
        Assert.Single(sink.Of("heartbeat"));
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_TransportThrowsWithUrl_NeverThrowsAndJournalsMaskedMessage()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, RecordingSink sink) = Create();
        transport.ThenThrow(url => new InvalidOperationException("GET " + url + " refused"));

        Assert.True(heartbeat.Pump(T0, BotState.Halted));

        Assert.Equal(new[] { "2026-01-14T10:00:00Z heartbeat event=failed status=0 state=Halted failures=1 error=InvalidOperationException:\\sGET\\s***\\srefused" }, sink.Of("heartbeat"));
        Assert.DoesNotContain(sink.Lines, l => OpsData.ContainsSecret(l.Line));
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_TransportReturnsNull_CountsAsFailure()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, _) = Create();
        transport.Then(null!);

        Assert.True(heartbeat.Pump(T0, BotState.Running));
        Assert.Equal(1, heartbeat.Failures);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_EmptyUrl_NeverCalls()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, _) = Create(new OpsParameters());

        Assert.False(heartbeat.Pump(T0, BotState.Running));
        Assert.False(heartbeat.Pump(T0.AddHours(1.0), BotState.Running));
        Assert.Empty(transport.Calls);
        Assert.Null(heartbeat.LastCallUtc);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Pump_ServerTimeBeforeLastCall_CallsAgain()
    {
        (Heartbeat heartbeat, ScriptedTransport transport, _) = Create();
        heartbeat.Pump(T0, BotState.Running);

        Assert.True(heartbeat.Pump(T0.AddMinutes(-2.0), BotState.Running));
        Assert.Equal(2, transport.Calls.Count);
    }

    [Theory]
    [Trait("Req", "OPS-005")]
    [InlineData(BotState.Recovering)]
    [InlineData(BotState.Running)]
    [InlineData(BotState.Blocked)]
    [InlineData(BotState.Halted)]
    public void OnTimer_EveryBotState_CallsHeartbeatAndPumpsAlerts(BotState state)
    {
        var transport = new ScriptedTransport();
        var ops = new Operations(OpsData.WithSecrets, ExecutionData.RiskDefaults, OpsData.Instance, ExecutionHarness.DefaultBroker(), transport, new RecordingSink(), T0);
        ops.Alerts.Raise(OpsAlertKind.StateChanged, "now " + state, T0);

        ops.OnTimer(T0, state);
        ops.OnTimer(T0.AddMinutes(1.0), state);
        ops.OnTimer(T0.AddMinutes(5.0), state);

        Assert.Equal(2, transport.Calls.Count(c => c.Url == OpsData.HeartbeatUrl));
        Assert.Single(transport.Calls, c => c.Url != OpsData.HeartbeatUrl);
        Assert.Equal(OpsData.HeartbeatUrl, transport.Calls[0].Url);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void OnTimer_HeartbeatAndTelegramDown_NeverThrows()
    {
        var transport = new ScriptedTransport { Default = ScriptedTransport.Failed() };
        var sink = new RecordingSink();
        var ops = new Operations(OpsData.WithSecrets, ExecutionData.RiskDefaults, OpsData.Instance, ExecutionHarness.DefaultBroker(), transport, sink, T0);
        ops.Alerts.Raise(OpsAlertKind.KillSwitch, "halt", T0);

        for (int minute = 0; minute < 30; minute++)
        {
            ops.OnTimer(T0.AddMinutes(minute), BotState.Halted);
        }

        Assert.Equal(6, ops.Heartbeat.Failures);
        Assert.Equal(1, ops.Alerts.QueueLength);
        Assert.Equal(6, sink.Of("heartbeat").Count);
    }

    [Fact]
    [Trait("Req", "OPS-005")]
    public void Constructor_NullArguments_Throw()
    {
        var masker = new SecretMasker(Array.Empty<string>());
        var journal = new Journal(new RecordingSink(), masker);
        var http = new ScriptedTransport();

        Assert.Throws<ArgumentNullException>(() => new Heartbeat(null!, http, journal, masker));
        Assert.Throws<ArgumentNullException>(() => new Heartbeat(new OpsParameters(), null!, journal, masker));
        Assert.Throws<ArgumentNullException>(() => new Heartbeat(new OpsParameters(), http, null!, masker));
        Assert.Throws<ArgumentNullException>(() => new Heartbeat(new OpsParameters(), http, journal, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Heartbeat(new OpsParameters { HeartbeatUrl = "x" }, http, journal, masker));
        Assert.Throws<ArgumentNullException>(() => new Operations(null!, ExecutionData.RiskDefaults, "x", ExecutionHarness.DefaultBroker(), http, new RecordingSink(), T0));
    }
}
