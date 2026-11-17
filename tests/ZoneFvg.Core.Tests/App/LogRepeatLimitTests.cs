using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-008: repeat limits for the Log. (1) A recovery that keeps failing is written for its first
/// failed run after a start, a reconnect or a completed recovery, then at most once per 24 hours
/// with the number of failed runs; the completed run is always written. (2) The same error
/// (handler, type, message) within 60 minutes of its group's last written line is counted for
/// RSK-010 but neither journaled nor alerted. (3) The risk-unavailable line is written when its
/// text changes or 60 minutes after the last one. Data: <see cref="RecoveryData"/>, start
/// Wednesday 2026-03-04 08:00 UTC.
/// </summary>
public class LogRepeatLimitTests
{
    private static readonly DateTime A = RecoveryData.Activation;

    private static void TimerEveryMinute(BotHarness h, DateTime from, DateTime to)
    {
        for (DateTime at = from; at <= to; at = at.AddMinutes(1.0))
        {
            h.Timer(at);
        }
    }

    private static List<string> ReportLines(BotHarness h)
    {
        return h.Journal.Lines("report").ToList();
    }

    // ------------------------------------------------------------------ (1) recovery

    [Fact]
    [Trait("Req", "OPS-008")]
    public void FailedRecovery_WrittenAtTheFirstRunAndThenOncePer24Hours()
    {
        var h = new BotHarness();
        h.History.Available[TimeFrame.D1] = 10;
        h.Start(A);

        TimerEveryMinute(h, A.AddMinutes(1.0), A.AddDays(1.0));

        DateTime nextDay = A.AddDays(1.0);
        List<string> reports = ReportLines(h);
        Assert.Equal(2, reports.Count);
        Assert.StartsWith(JournalFormat.Time(A) + " report trigger=start ", reports[0], StringComparison.Ordinal);
        Assert.EndsWith(" failed-runs=1", reports[0], StringComparison.Ordinal);
        Assert.StartsWith(JournalFormat.Time(nextDay) + " report trigger=waiting\\sfor\\sdata ", reports[1], StringComparison.Ordinal);
        Assert.EndsWith(" failed-runs=96", reports[1], StringComparison.Ordinal);
        List<string> recoveries = h.Journal.Lines("recovery").ToList();
        Assert.Equal(2, recoveries.Count);
        Assert.StartsWith(JournalFormat.Time(A) + " ", recoveries[0], StringComparison.Ordinal);
        Assert.StartsWith(JournalFormat.Time(nextDay) + " ", recoveries[1], StringComparison.Ordinal);
        Assert.EndsWith(" failed-runs=1", recoveries[0], StringComparison.Ordinal);
        Assert.EndsWith(" failed-runs=96", recoveries[1], StringComparison.Ordinal);
        Assert.DoesNotContain(h.Journal.Lines("report"), line => line.StartsWith(JournalFormat.Time(nextDay.AddMinutes(-15.0)), StringComparison.Ordinal));

        // The 15-minute retry is unchanged: the last run was the one at 08:00 on the next day.
        Assert.Contains(" at=" + JournalFormat.Time(nextDay) + " ", h.Core.LastReport!.Text, StringComparison.Ordinal);
        Assert.Equal(BotState.Recovering, h.Core.State);
        Assert.Equal(1, h.Alerts(AlertKind.Started));
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void FailedRecovery_TheRunThatCompletesIsAlwaysWritten()
    {
        var h = new BotHarness();
        h.History.Available[TimeFrame.D1] = 10;
        h.Start(A);
        TimerEveryMinute(h, A.AddMinutes(1.0), A.AddMinutes(20.0));

        h.History.Available.Remove(TimeFrame.D1);
        TimerEveryMinute(h, A.AddMinutes(21.0), A.AddMinutes(30.0));

        List<string> reports = ReportLines(h);
        Assert.Equal(2, reports.Count);
        Assert.EndsWith(" failed-runs=1", reports[0], StringComparison.Ordinal);
        Assert.StartsWith(JournalFormat.Time(A.AddMinutes(30.0)) + " report trigger=waiting\\sfor\\sdata text=", reports[1], StringComparison.Ordinal);
        Assert.DoesNotContain("failed-runs", reports[1], StringComparison.Ordinal);
        Assert.Equal(2, h.Journal.Lines("recovery").Count);
        Assert.EndsWith(" state=Running", h.Journal.Lines("recovery")[1], StringComparison.Ordinal);
        Assert.Equal(BotState.Running, h.Core.State);
        Assert.Equal(1, h.Alerts(AlertKind.Started));
        Assert.Equal(1, h.Alerts(AlertKind.Recovered));
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void FailedRecovery_AfterAReconnect_TheFirstFailedRunIsWrittenAgain()
    {
        var h = new BotHarness();
        h.History.Available[TimeFrame.D1] = 10;
        h.Start(A);
        TimerEveryMinute(h, A.AddMinutes(1.0), A.AddMinutes(15.0));
        Assert.Single(ReportLines(h));

        h.Core.OnDisconnected();
        h.Broker.AdvanceTo(A.AddMinutes(20.0));
        h.Core.OnConnected(h.History);

        List<string> reports = ReportLines(h);
        Assert.Equal(2, reports.Count);
        Assert.StartsWith(JournalFormat.Time(A.AddMinutes(20.0)) + " report trigger=reconnect ", reports[1], StringComparison.Ordinal);
        Assert.EndsWith(" failed-runs=1", reports[1], StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------ (2) errors

    private static (BotHarness Harness, BotCore Core, FlakyStore Store) StoreThatFails()
    {
        var h = new BotHarness();
        var store = new FlakyStore();
        var core = new BotCore(RecoveryData.Parameters(), h.Port, store, h.Http, h.Journal, true);
        h.Broker.AdvanceTo(A);
        core.OnStart(h.History);
        Assert.Equal(BotState.Running, core.State);
        store.Fault = "store down";
        return (h, core, store);
    }

    private static IReadOnlyList<string> StoreErrors(BotHarness h, string message)
    {
        string escaped = message.Replace(" ", "\\s", StringComparison.Ordinal);
        return h.Journal.Lines("error").Where(line => line.Contains(" error source=OnTimer error=InvalidOperationException:\\s" + escaped, StringComparison.Ordinal)).ToList();
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void SameError_EveryMinuteForThreeHours_WrittenOncePerHourWithTheNumberLeftOut()
    {
        (BotHarness h, BotCore core, _) = StoreThatFails();
        int? haltedAt = null;

        for (int minute = 0; minute <= 180; minute++)
        {
            h.Broker.AdvanceTo(A.AddMinutes(minute));
            core.OnTimer();
            if (!haltedAt.HasValue && core.State == BotState.Halted)
            {
                haltedAt = minute;
            }
        }

        IReadOnlyList<string> errors = StoreErrors(h, "store down");
        Assert.Equal(
            Times(A, A.AddMinutes(60.0), A.AddMinutes(120.0), A.AddMinutes(180.0)),
            errors.Select(line => line.Substring(0, line.IndexOf(' ', StringComparison.Ordinal))));
        Assert.DoesNotContain("left-out", errors[0], StringComparison.Ordinal);
        Assert.All(errors.Skip(1), line => Assert.EndsWith(" left-out=59", line, StringComparison.Ordinal));
        IReadOnlyList<string> alerts = h.AlertLines(AlertKind.Error);
        Assert.Equal(4, alerts.Count);
        Assert.Equal(3, alerts.Count(line => line.Contains("left-out=59", StringComparison.Ordinal)));

        // The error counter of RSK-010 still counts every error: the sixth in the hour (minute 5) halts, as without the limit.
        Assert.Equal(5, haltedAt);
        Assert.Contains("ErrorLimit", HaltCauseName(core), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void SameError_TheErrorAtMinute59IsLeftOut()
    {
        (BotHarness h, BotCore core, _) = StoreThatFails();

        for (int minute = 0; minute <= 59; minute++)
        {
            h.Broker.AdvanceTo(A.AddMinutes(minute));
            core.OnTimer();
        }

        Assert.Single(StoreErrors(h, "store down"));
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void OtherMessage_IsWrittenAtOnce()
    {
        (BotHarness h, BotCore core, FlakyStore store) = StoreThatFails();

        for (int minute = 0; minute <= 61; minute++)
        {
            store.Fault = minute == 30 ? "store down again" : "store down";
            h.Broker.AdvanceTo(A.AddMinutes(minute));
            core.OnTimer();
        }

        IReadOnlyList<string> other = StoreErrors(h, "store down again");
        Assert.Single(other);
        Assert.StartsWith(JournalFormat.Time(A.AddMinutes(30.0)) + " ", other[0], StringComparison.Ordinal);
        IReadOnlyList<string> first = StoreErrors(h, "store down").Except(other).ToList();
        Assert.Equal(2, first.Count);
        Assert.StartsWith(JournalFormat.Time(A.AddMinutes(60.0)) + " ", first[1], StringComparison.Ordinal);
        Assert.EndsWith(" left-out=58", first[1], StringComparison.Ordinal);
    }

    private static string HaltCauseName(BotCore core)
    {
        return string.Join(",", core.HaltCauses);
    }

    // ------------------------------------------------------------------ (3) risk unavailable

    private static IReadOnlyList<string> RiskLineTimes(BotHarness h)
    {
        return h.Journal.Lines("risk")
            .Where(line => line.Contains(" risk event=unavailable ", StringComparison.Ordinal))
            .Select(line => line.Substring(0, line.IndexOf(' ', StringComparison.Ordinal)))
            .ToList();
    }

    private static IEnumerable<string> Times(params DateTime[] times)
    {
        return times.Select(time => JournalFormat.Time(time));
    }

    private static DateTime Bar(int index)
    {
        // Bar 0 is the M15 bar closing at 08:15.
        return A.AddMinutes(15.0 * (index + 1));
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void RiskUnavailable_SameTextOnEveryBar_WrittenOncePerHour()
    {
        var h = new BotHarness();
        h.Start(A);
        h.Port.History = _ => throw new InvalidOperationException("history unavailable");

        h.FeedTo(Bar(8));

        Assert.Equal(Times(Bar(0), Bar(4), Bar(8)), RiskLineTimes(h));
        Assert.Equal(1, h.Alerts(AlertKind.RiskDataUnavailable));
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void RiskUnavailable_TextChanges_WrittenAtOnceAndThenOncePerHour()
    {
        var h = new BotHarness();
        h.Start(A);
        h.Port.History = _ => throw new InvalidOperationException("history unavailable");
        h.FeedTo(Bar(4));

        h.Port.History = _ => throw new InvalidOperationException("history still unavailable");
        h.FeedTo(Bar(9));

        Assert.Equal(Times(Bar(0), Bar(4), Bar(5), Bar(9)), RiskLineTimes(h));
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void RiskUnavailable_NewOutageWithTheSameTextAfterEntriesWereAllowed_WrittenAgain()
    {
        // Owner answer of 2026-11-17: the last text is forgotten when entries are allowed again.
        var h = new BotHarness();
        h.Start(A);
        h.Port.History = _ => throw new InvalidOperationException("history unavailable");
        h.FeedTo(Bar(1));
        h.Port.History = null;
        h.FeedTo(Bar(2));

        h.Port.History = _ => throw new InvalidOperationException("history unavailable");
        h.FeedTo(Bar(4));

        Assert.Equal(Times(Bar(0), Bar(3)), RiskLineTimes(h));
        Assert.Equal(2, h.Alerts(AlertKind.RiskDataUnavailable));
    }

    // ------------------------------------------------------------------ the limits alone

    [Fact]
    [Trait("Req", "OPS-008")]
    public void Limits_ErrorExactly60MinutesLater_WrittenWithTheNumberLeftOut()
    {
        var limits = new LogRepeatLimits();
        var error = new InvalidOperationException("x");

        Assert.Equal(0, limits.Error("OnTimer", error, A));
        Assert.Null(limits.Error("OnTimer", error, A.AddMinutes(60.0).AddTicks(-1)));
        Assert.Equal(1, limits.Error("OnTimer", error, A.AddMinutes(60.0)));
        Assert.Equal(0, limits.Error("OnTimer", error, A.AddMinutes(180.0)));
        Assert.Equal(0, limits.Error("OnBarClosed", error, A.AddMinutes(180.0)));
        Assert.Equal(0, limits.Error("OnTimer", new ArgumentException("x"), A.AddMinutes(180.0)));
        Assert.Equal(0, limits.Error("OnTimer", new InvalidOperationException("y"), A.AddMinutes(300.0)));
        Assert.Equal(0, limits.Error("OnTimer", error, A.AddMinutes(301.0)));
    }

    [Fact]
    [Trait("Req", "OPS-008")]
    public void Limits_RecoveryAndRiskLines_WrittenOnTheirIntervals()
    {
        var limits = new LogRepeatLimits();

        Assert.Equal(1, limits.FailedRecovery(A));
        Assert.Null(limits.FailedRecovery(A.AddHours(24.0).AddTicks(-1)));
        Assert.Equal(2, limits.FailedRecovery(A.AddHours(24.0)));
        limits.ResetRecovery();
        Assert.Equal(1, limits.FailedRecovery(A.AddHours(25.0)));

        Assert.True(limits.RiskUnavailable("a", A));
        Assert.False(limits.RiskUnavailable("a", A.AddMinutes(59.0)));
        Assert.True(limits.RiskUnavailable("b", A.AddMinutes(59.0)));
        Assert.True(limits.RiskUnavailable("a", A.AddMinutes(60.0)));
        Assert.True(limits.RiskUnavailable("a", A.AddMinutes(120.0)));
        Assert.False(limits.RiskUnavailable("a", A.AddMinutes(121.0)));
        limits.RiskAvailable();
        Assert.True(limits.RiskUnavailable("a", A.AddMinutes(122.0)));
    }

    /// <summary>A key-value store whose writes throw while <see cref="Fault"/> is set.</summary>
    private sealed class FlakyStore : IKeyValueStore
    {
        private readonly MemoryStore _inner = new();

        public string? Fault { get; set; }

        public string? Get(string key) => _inner.Get(key);

        public void Set(string key, string value)
        {
            if (Fault != null)
            {
                throw new InvalidOperationException(Fault);
            }

            _inner.Set(key, value);
        }
    }
}
