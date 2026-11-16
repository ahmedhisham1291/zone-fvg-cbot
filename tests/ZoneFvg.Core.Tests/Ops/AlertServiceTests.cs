using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-004: Telegram alerts are best-effort. Raising only queues; sending happens in Pump through
/// the HTTP port, never throws, keeps failed alerts queued with exponential back-off, drops the
/// oldest when the queue is full, collapses duplicates and sends nothing without a token.
/// </summary>
public class AlertServiceTests
{
    private static readonly DateTime T0 = OpsData.T0;

    [Fact]
    [Trait("Req", "OPS-004")]
    public void RaiseThenPump_SendsOnePlainTextRequestToTelegram()
    {
        AlertSetup a = OpsData.Alerts();

        Assert.Equal(AlertRaiseOutcome.Queued, a.Service.Raise(OpsAlertKind.OrderRejected, "order 1001 rejected", T0));
        Assert.Empty(a.Transport.Calls);
        Assert.Equal(1, a.Service.Pump(T0));

        Assert.Equal(new[] { (OpsData.TelegramUrl("ZFVG%20EURUSD%20OrderRejected%3A%20order%201001%20rejected"), 5) }, a.Transport.Calls);
        Assert.Equal(0, a.Service.QueueLength);
        Assert.Equal(1, a.Service.Sent);
        Assert.Null(a.Service.NextAttemptUtc);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Raise_JournalsKindHashAndFullTextButNeverTheUrl()
    {
        AlertSetup a = OpsData.Alerts();
        const string message = "ZFVG EURUSD KillSwitch: drawdown 8.0%";

        a.Service.Raise(OpsAlertKind.KillSwitch, "drawdown 8.0%", T0);
        a.Service.Pump(T0);

        string hash = JournalFormat.Hash8(message);
        Assert.Equal(
            new[]
            {
                "2026-01-14T10:00:00Z alert event=queued kind=KillSwitch hash=" + hash + " text=ZFVG\\sEURUSD\\sKillSwitch:\\sdrawdown\\s8.0% queue=1",
                "2026-01-14T10:00:00Z alert event=sent kind=KillSwitch hash=" + hash + " queue=0",
            },
            a.Sink.Of("alert"));
        Assert.DoesNotContain(a.Sink.Lines, l => l.Line.Contains("telegram", StringComparison.OrdinalIgnoreCase) || l.Line.Contains("sendMessage", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Raise_MultiLineText_IsJournaledWholeOnOneLine()
    {
        AlertSetup a = OpsData.Alerts();

        a.Service.Raise(OpsAlertKind.DailySummary, "line one\nline two", T0);

        Assert.EndsWith(" text=ZFVG\\sEURUSD\\sDailySummary:\\sline\\sone\\nline\\stwo queue=1", a.Sink.Of("alert").Single(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-007")]
    public void Raise_SecretsInText_NeverReachTheLog()
    {
        AlertSetup a = OpsData.Alerts();
        string text = "chat " + OpsData.ChatId + " token " + OpsData.Token + " url " + OpsData.HeartbeatUrl;

        a.Service.Raise(OpsAlertKind.ErrorStorm, text, T0);
        a.Service.Raise(OpsAlertKind.ErrorStorm, text, T0.AddMinutes(1.0));

        Assert.Equal(
            new[]
            {
                "2026-01-14T10:00:00Z alert event=queued kind=ErrorStorm hash=" + JournalFormat.Hash8("ZFVG EURUSD ErrorStorm: chat *** token *** url ***")
                    + " text=ZFVG\\sEURUSD\\sErrorStorm:\\schat\\s***\\stoken\\s***\\surl\\s*** queue=1",
                "2026-01-14T10:01:00Z alert event=collapsed kind=ErrorStorm hash=" + JournalFormat.Hash8("ZFVG EURUSD ErrorStorm: chat *** token *** url ***")
                    + " text=ZFVG\\sEURUSD\\sErrorStorm:\\schat\\s***\\stoken\\s***\\surl\\s*** queue=1",
            },
            a.Sink.Of("alert"));
        Assert.DoesNotContain(a.Sink.Lines, l => OpsData.ContainsSecret(l.Line));
    }

    [Fact]
    [Trait("Req", "OPS-007")]
    public void Raise_AlertsDisabledWithHeartbeatUrlInText_LogStoresMaskedText()
    {
        AlertSetup a = OpsData.Alerts(new OpsParameters { HeartbeatUrl = OpsData.HeartbeatUrl });

        Assert.Equal(AlertRaiseOutcome.Disabled, a.Service.Raise(OpsAlertKind.ErrorStorm, "monitor " + OpsData.HeartbeatUrl + " down", T0));

        Assert.EndsWith(" event=disabled kind=ErrorStorm hash=" + JournalFormat.Hash8("ZFVG EURUSD ErrorStorm: monitor *** down") + " text=ZFVG\\sEURUSD\\sErrorStorm:\\smonitor\\s***\\sdown queue=0", a.Sink.Of("alert").Single(), StringComparison.Ordinal);
        Assert.DoesNotContain(a.Sink.Lines, l => OpsData.ContainsSecret(l.Line));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Format_LongText_IsCutToThousandCharactersWithEllipsis()
    {
        AlertSetup a = OpsData.Alerts();

        string text = a.Service.Format(OpsAlertKind.ErrorStorm, new string('a', 2000));

        Assert.Equal(1000, text.Length);
        Assert.StartsWith("ZFVG EURUSD ErrorStorm: aaa", text, StringComparison.Ordinal);
        Assert.EndsWith("aaa...", text, StringComparison.Ordinal);
        // Prefix "ZFVG EURUSD ErrorStorm: " is 24 characters, so exactly 976 'a' fit without cutting.
        Assert.Equal(1000, a.Service.Format(OpsAlertKind.ErrorStorm, new string('a', 976)).Length);
        Assert.EndsWith("aaaa", a.Service.Format(OpsAlertKind.ErrorStorm, new string('a', 976)), StringComparison.Ordinal);
        Assert.EndsWith("a...", a.Service.Format(OpsAlertKind.ErrorStorm, new string('a', 977)), StringComparison.Ordinal);
        Assert.Equal("ZFVG EURUSD Started: ", a.Service.Format(OpsAlertKind.Started, null));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Format_CutInsideSurrogatePair_KeepsThePairWhole()
    {
        AlertSetup a = OpsData.Alerts();
        string text = new string('a', 972) + "\U0001F600" + new string('b', 50);

        string message = a.Service.Format(OpsAlertKind.ErrorStorm, text);

        // Prefix 24 + 972 = 996 characters before the pair; the pair occupies 996 and 997, so a cut at 997 would split it.
        Assert.Equal(999, message.Length);
        Assert.EndsWith("a...", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Pump_TransportFails_KeepsAlertQueuedAndNeverThrows()
    {
        AlertSetup a = OpsData.Alerts();
        a.Transport.Then(ScriptedTransport.Failed(0));
        a.Service.Raise(OpsAlertKind.TradeOpened, "buy", T0);

        Assert.Equal(0, a.Service.Pump(T0));

        Assert.Equal(1, a.Service.QueueLength);
        Assert.Equal(1, a.Service.FailedAttempts);
        Assert.Equal(T0.AddMinutes(1.0), a.Service.NextAttemptUtc);
        Assert.Equal(
            "2026-01-14T10:00:00Z alert event=failed kind=TradeOpened hash=" + JournalFormat.Hash8("ZFVG EURUSD TradeOpened: buy") + " status=0 failures=1 retryAt=2026-01-14T10:01:00Z error= queue=1",
            a.Sink.Of("alert")[1]);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Pump_TransportThrowsWithUrlInMessage_NeverThrowsAndJournalsMaskedError()
    {
        AlertSetup a = OpsData.Alerts();
        a.Transport.ThenThrow(url => new InvalidOperationException("GET " + url + " timed out"));
        a.Service.Raise(OpsAlertKind.TradeClosed, "sell", T0);

        Assert.Equal(0, a.Service.Pump(T0));

        Assert.Equal(1, a.Service.QueueLength);
        string failed = a.Sink.Of("alert")[1];
        Assert.Contains(" error=InvalidOperationException:\\sGET\\shttps://api.telegram.org/bot***/sendMessage?chat_id=***&text=", failed, StringComparison.Ordinal);
        Assert.DoesNotContain(a.Sink.Lines, l => OpsData.ContainsSecret(l.Line));
        Assert.Equal(1, a.Service.Pump(T0.AddMinutes(1.0)));
        Assert.Equal(0, a.Service.QueueLength);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Pump_RepeatedFailures_BackOffDoublesUpToThirtyMinutes()
    {
        AlertSetup a = OpsData.Alerts();
        a.Transport.Default = ScriptedTransport.Failed(503);
        a.Service.Raise(OpsAlertKind.ErrorStorm, "x", T0);
        DateTime now = T0;
        var waits = new List<double>();
        for (int i = 0; i < 7; i++)
        {
            int calls = a.Transport.Calls.Count;
            a.Service.Pump(now);
            Assert.Equal(calls + 1, a.Transport.Calls.Count);
            DateTime next = a.Service.NextAttemptUtc!.Value;
            waits.Add((next - now).TotalMinutes);

            // One second before the next attempt nothing is sent.
            a.Service.Pump(next.AddSeconds(-1.0));
            Assert.Equal(calls + 1, a.Transport.Calls.Count);
            now = next;
        }

        Assert.Equal(new[] { 1.0, 2.0, 4.0, 8.0, 16.0, 30.0, 30.0 }, waits);
        a.Transport.Default = new HttpResult(true, 200, "{}");
        Assert.Equal(1, a.Service.Pump(now));
        Assert.Null(a.Service.NextAttemptUtc);
        Assert.Equal(7, a.Service.FailedAttempts);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Pump_AfterSuccess_BackOffStartsAgainAtOneMinute()
    {
        AlertSetup a = OpsData.Alerts();
        a.Transport.Then(ScriptedTransport.Failed());
        a.Transport.Then(ScriptedTransport.Failed());
        a.Service.Raise(OpsAlertKind.ErrorStorm, "x", T0);
        a.Service.Pump(T0);
        a.Service.Pump(T0.AddMinutes(1.0));
        Assert.Equal(T0.AddMinutes(3.0), a.Service.NextAttemptUtc);
        Assert.Equal(1, a.Service.Pump(T0.AddMinutes(3.0)));

        a.Transport.Then(ScriptedTransport.Failed());
        a.Service.Raise(OpsAlertKind.ErrorStorm, "y", T0.AddMinutes(4.0));
        a.Service.Pump(T0.AddMinutes(4.0));

        Assert.Equal(T0.AddMinutes(5.0), a.Service.NextAttemptUtc);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Pump_ManyQueued_SendsAtMostThreePerCallOldestFirst()
    {
        AlertSetup a = OpsData.Alerts();
        for (int i = 1; i <= 5; i++)
        {
            a.Service.Raise(OpsAlertKind.TradeOpened, "t" + i, T0);
        }

        Assert.Equal(3, a.Service.Pump(T0));
        Assert.Equal(new[] { "ZFVG EURUSD TradeOpened: t4", "ZFVG EURUSD TradeOpened: t5" }, a.Service.PendingTexts);
        Assert.EndsWith("t1", a.Transport.Calls[0].Url, StringComparison.Ordinal);
        Assert.EndsWith("t3", a.Transport.Calls[2].Url, StringComparison.Ordinal);
        Assert.Equal(2, a.Service.Pump(T0.AddMinutes(1.0)));
        Assert.Equal(0, a.Service.Pump(T0.AddMinutes(2.0)));
        Assert.Equal(5, a.Transport.Calls.Count);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Raise_QueueFull_DropsOldestAndCountsIt()
    {
        AlertSetup a = OpsData.Alerts();
        for (int i = 0; i < 50; i++)
        {
            a.Service.Raise(OpsAlertKind.TradeOpened, "n" + i, T0);
        }

        Assert.Equal(50, a.Service.QueueLength);
        Assert.Equal(0, a.Service.Dropped);

        a.Service.Raise(OpsAlertKind.TradeOpened, "n50", T0);

        Assert.Equal(50, a.Service.QueueLength);
        Assert.Equal(1, a.Service.Dropped);
        Assert.Equal("ZFVG EURUSD TradeOpened: n1", a.Service.PendingTexts[0]);
        Assert.Equal("ZFVG EURUSD TradeOpened: n50", a.Service.PendingTexts[49]);
        Assert.Contains(a.Sink.Of("alert"), l => l.Contains(" alert event=dropped kind=TradeOpened hash=" + JournalFormat.Hash8("ZFVG EURUSD TradeOpened: n0") + " ", StringComparison.Ordinal));
        Assert.Empty(a.Transport.Calls);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Raise_IdenticalWithinTenMinutes_IsCollapsed()
    {
        AlertSetup a = OpsData.Alerts();

        Assert.Equal(AlertRaiseOutcome.Queued, a.Service.Raise(OpsAlertKind.StopLossMissing, "position 7", T0));
        Assert.Equal(AlertRaiseOutcome.Collapsed, a.Service.Raise(OpsAlertKind.StopLossMissing, "position 7", T0));
        Assert.Equal(AlertRaiseOutcome.Collapsed, a.Service.Raise(OpsAlertKind.StopLossMissing, "position 7", T0.AddMinutes(10.0).AddSeconds(-1.0)));
        Assert.Equal(AlertRaiseOutcome.Queued, a.Service.Raise(OpsAlertKind.StopLossMissing, "position 8", T0));
        Assert.Equal(AlertRaiseOutcome.Queued, a.Service.Raise(OpsAlertKind.StopLossRepaired, "position 7", T0));
        Assert.Equal(AlertRaiseOutcome.Queued, a.Service.Raise(OpsAlertKind.StopLossMissing, "position 7", T0.AddMinutes(10.0)));
        Assert.Equal(AlertRaiseOutcome.Collapsed, a.Service.Raise(OpsAlertKind.StopLossMissing, "position 7", T0.AddMinutes(19.0)));

        Assert.Equal(3, a.Service.Collapsed);
        Assert.Equal(4, a.Service.QueueLength);
        Assert.Equal(3, a.Sink.Of("alert").Count(l => l.Contains(" event=collapsed ", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Raise_TimeBeforeEarlierRaise_IsNotCollapsed()
    {
        AlertSetup a = OpsData.Alerts();
        a.Service.Raise(OpsAlertKind.StopLossMissing, "position 7", T0);

        Assert.Equal(AlertRaiseOutcome.Queued, a.Service.Raise(OpsAlertKind.StopLossMissing, "position 7", T0.AddMinutes(-1.0)));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void EmptyToken_RaiseJournalsAndPumpMakesNoTransportCall()
    {
        AlertSetup a = OpsData.Alerts(new OpsParameters());

        Assert.False(a.Service.Enabled);
        Assert.Equal(AlertRaiseOutcome.Disabled, a.Service.Raise(OpsAlertKind.KillSwitch, "halt", T0));
        Assert.Equal(0, a.Service.Pump(T0));
        Assert.Equal(0, a.Service.Pump(T0.AddHours(1.0)));

        Assert.Empty(a.Transport.Calls);
        Assert.Equal(0, a.Service.QueueLength);
        Assert.Equal(
            new[] { "2026-01-14T10:00:00Z alert event=disabled kind=KillSwitch hash=" + JournalFormat.Hash8("ZFVG EURUSD KillSwitch: halt") + " text=ZFVG\\sEURUSD\\sKillSwitch:\\shalt queue=0" },
            a.Sink.Of("alert"));
    }

    [Theory]
    [Trait("Req", "OPS-004")]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(404)]
    [InlineData(499)]
    public void Pump_ClientError_RemovesThatAlertAndGoesOn(int status)
    {
        AlertSetup a = OpsData.Alerts();
        a.Transport.Then(ScriptedTransport.Failed(status));
        a.Service.Raise(OpsAlertKind.TradeOpened, "first", T0);
        a.Service.Raise(OpsAlertKind.TradeOpened, "second", T0);

        Assert.Equal(1, a.Service.Pump(T0));

        Assert.Equal(1, a.Service.Refused);
        Assert.Equal(0, a.Service.QueueLength);
        Assert.Null(a.Service.NextAttemptUtc);
        Assert.Contains(a.Sink.Of("alert"), l => l.Contains(" event=refused kind=TradeOpened ", StringComparison.Ordinal) && l.Contains(" status=" + status + " ", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Req", "OPS-004")]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(399)]
    [InlineData(200)]
    public void Pump_RateLimitServerErrorOrNotOk_KeepsAlertWithBackOff(int status)
    {
        AlertSetup a = OpsData.Alerts();
        a.Transport.Then(ScriptedTransport.Failed(status));
        a.Service.Raise(OpsAlertKind.TradeOpened, "first", T0);

        Assert.Equal(0, a.Service.Pump(T0));

        Assert.Equal(1, a.Service.QueueLength);
        Assert.Equal(0, a.Service.Refused);
        Assert.Equal(T0.AddMinutes(1.0), a.Service.NextAttemptUtc);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Pump_TransportReturnsNull_CountsAsFailure()
    {
        AlertSetup a = OpsData.Alerts();
        a.Transport.Then(null!);
        a.Service.Raise(OpsAlertKind.TradeOpened, "first", T0);

        Assert.Equal(0, a.Service.Pump(T0));
        Assert.Equal(1, a.Service.QueueLength);
    }

    [Fact]
    [Trait("Req", "OPS-007")]
    public void Raise_TextWithSecrets_IsMaskedBeforeSending()
    {
        AlertSetup a = OpsData.Alerts();

        a.Service.Raise(OpsAlertKind.ErrorStorm, "failed " + OpsData.HeartbeatUrl + " with " + OpsData.Token, T0);
        a.Service.Pump(T0);

        Assert.Equal("ZFVG EURUSD ErrorStorm: failed *** with ***", a.Service.Format(OpsAlertKind.ErrorStorm, "failed " + OpsData.HeartbeatUrl + " with " + OpsData.Token));
        Assert.Equal(OpsData.TelegramUrl("ZFVG%20EURUSD%20ErrorStorm%3A%20failed%20%2A%2A%2A%20with%20%2A%2A%2A"), a.Transport.Calls.Single().Url);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Constructor_NullOrInvalidArguments_Throw()
    {
        var sink = new RecordingSink();
        var masker = new SecretMasker(Array.Empty<string>());
        var journal = new Journal(sink, masker);
        var http = new ScriptedTransport();

        Assert.Throws<ArgumentNullException>(() => new AlertService(null!, "x", http, journal, masker));
        Assert.Throws<ArgumentNullException>(() => new AlertService(new OpsParameters(), null!, http, journal, masker));
        Assert.Throws<ArgumentNullException>(() => new AlertService(new OpsParameters(), "x", null!, journal, masker));
        Assert.Throws<ArgumentNullException>(() => new AlertService(new OpsParameters(), "x", http, null!, masker));
        Assert.Throws<ArgumentNullException>(() => new AlertService(new OpsParameters(), "x", http, journal, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlertService(new OpsParameters { TelegramBotToken = "t" }, "x", http, journal, masker));
    }

    public static IEnumerable<object[]> ExecutionKinds()
    {
        yield return new object[] { AlertKind.SanityCap, OpsAlertKind.SanityCap };
        yield return new object[] { AlertKind.OrderRejected, OpsAlertKind.OrderRejected };
        yield return new object[] { AlertKind.OrderMismatch, OpsAlertKind.OrderProblem };
        yield return new object[] { AlertKind.OrderNotFound, OpsAlertKind.OrderProblem };
        yield return new object[] { AlertKind.CancelFailed, OpsAlertKind.OrderProblem };
        yield return new object[] { AlertKind.StopLossMissing, OpsAlertKind.StopLossMissing };
        yield return new object[] { AlertKind.StopLossRestored, OpsAlertKind.StopLossRepaired };
        yield return new object[] { AlertKind.PositionClosedWithoutStop, OpsAlertKind.StopLossMissing };
        yield return new object[] { AlertKind.CloseFailed, OpsAlertKind.OrderProblem };
        yield return new object[] { AlertKind.RequestLimitReached, OpsAlertKind.CircuitBreaker };
    }

    [Theory]
    [Trait("Req", "OPS-004")]
    [MemberData(nameof(ExecutionKinds))]
    public void From_EveryExecutionAlertKind_MapsToTelegramKind(AlertKind kind, OpsAlertKind expected)
    {
        Assert.Equal(expected, OpsAlertKinds.From(kind));
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void From_EveryDefinedKindIsMappedAndUndefinedThrows()
    {
        Assert.Equal(Enum.GetValues<AlertKind>().Length, ExecutionKinds().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => OpsAlertKinds.From((AlertKind)99));
    }
}
