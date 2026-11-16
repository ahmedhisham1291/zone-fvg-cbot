using System.Globalization;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The wiring of WP09's operations module into <see cref="BotCore"/>, through a <see cref="FakeBroker"/>
/// and a fake Telegram transport: journal lines and alerts of a restart, exactly once per closed trade
/// (REC-008, REC-010, OPS-001, OPS-002). Data: <see cref="RecoveryData"/>. The reference trade risks
/// 489.13 (comment r=489.13) and closes at its target: (103.061 - 100.675) / 0.01 x 0.0001 x 41,000 =
/// 978.26, which is 2.00 R.
/// </summary>
public class OpsWiringScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;

    private static BotParameters WithTelegram()
    {
        // A token and chat id of the allowed shape; the transport is a fake, nothing leaves the test.
        return RecoveryData.Parameters() with { Ops = new OpsParameters { TelegramBotToken = "1:test", TelegramChatId = "42" } };
    }

    private static int Lines(BotHarness h, string category, string fragment)
    {
        return h.Journal.Count(category, fragment);
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Restart_TradeClosedWhileAway_IsJournaledInROnceAndAlertedOnce()
    {
        var http = new FakeTransport(NewsTestData.Ok("{\"ok\":true}"));
        var h = new BotHarness(http: http);
        h.Start(A, WithTelegram());
        h.FeedTo(RecoveryData.Confirmation);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        h.Core.OnTradeEvent();
        long position = h.ReferencePosition()!.Id;
        h.Timer(A.AddMinutes(51.0));

        // The core is gone while the target fills.
        h.Broker.AdvanceTo(A.AddMinutes(90.0));
        h.Broker.Tick(RecoveryData.Symbol, 103.07, 103.08);
        h.Start(A.AddHours(2.0), WithTelegram());
        h.Timer(A.AddHours(2.0).AddMinutes(1.0));
        h.Start(A.AddHours(2.25), WithTelegram());
        h.Timer(A.AddHours(2.25).AddMinutes(1.0));

        string id = position.ToString(CultureInfo.InvariantCulture);
        Assert.Equal(1, Lines(h, "position", "event=opened symbol=USDJPY position=" + id + " "));
        Assert.Equal(1, Lines(h, "trade", " trade symbol=USDJPY position=" + id + " "));
        string trade = h.Journal.Lines("trade").Single(line => line.Contains(" position=" + id + " ", StringComparison.Ordinal));
        Assert.Contains(" r=2.00 ", trade, StringComparison.Ordinal);
        Assert.Contains(" side=Buy requested=100.675 filled=100.675 ", trade, StringComparison.Ordinal);
        Assert.Contains(" exit=Unknown ", trade, StringComparison.Ordinal);

        Assert.Equal(1, Lines(h, "alert", "event=queued kind=TradeOpened "));
        Assert.Equal(1, Lines(h, "alert", "event=queued kind=TradeClosed "));
        Assert.Equal(1, http.Urls.Count(url => url.Contains("TradeClosed", StringComparison.Ordinal)));
        Assert.Equal(3, Lines(h, "recovery", " trigger=start "));
        Assert.Equal(3, h.Hooks.Count(BotAlertKind.RecoveryReport));
        Assert.Equal(1, Lines(h, "signal", " result=taken "));
        Assert.Equal(2, Lines(h, "signal", " skip=SignalFromReplay"));
        Assert.Equal(2, Lines(h, "skip", " reason=SignalFromReplay "));
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-010")]
    public void Start_JournalsTheRecoveryEntryReportAndStateAndQueuesStarted()
    {
        var http = new FakeTransport(NewsTestData.Ok("{\"ok\":true}"));
        var h = new BotHarness(http: http);

        h.Start(A, WithTelegram());
        h.Timer(A.AddMinutes(1.0));

        Assert.Equal(1, Lines(h, "recovery", " recovery symbol=USDJPY trigger=start adopted=0 kept=0 cancelled=0 strangers=0 zones=1 downtime=NA state=Running"));
        Assert.Equal(1, Lines(h, "report", " report trigger=start text=symbol=USDJPY"));
        Assert.Equal(1, Lines(h, "state", " state from=Recovering to=Running "));
        Assert.Equal(1, Lines(h, "alert", "event=queued kind=Started "));
        Assert.Equal(1, Lines(h, "alert", "event=queued kind=StateChanged "));
        Assert.Contains(http.Urls, url => url.Contains("Started", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "REC-008")]
    public void Reconnect_IsJournaledAsRecoveredWithItsTrigger()
    {
        var h = new BotHarness(http: new FakeTransport(NewsTestData.Ok("{\"ok\":true}")));
        h.Start(A, WithTelegram());
        h.Core.OnDisconnected();
        h.Broker.AdvanceTo(A.AddMinutes(5.0));

        h.Core.OnConnected(h.History);

        Assert.Equal(1, Lines(h, "recovery", " trigger=reconnect "));
        Assert.Equal(1, Lines(h, "alert", "event=queued kind=Recovered "));
        Assert.Equal(1, Lines(h, "connection", " event=disconnected"));
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void Timer_DailySummaryAndInactivityRunInEveryState()
    {
        // Started Wednesday; the first summary is due after 00:00 Prague time on Thursday (23:00 UTC).
        var h = new BotHarness(http: new FakeTransport(NewsTestData.Ok("{\"ok\":true}")));
        h.Start(A, WithTelegram());
        h.Core.OnDisconnected();

        h.Timer(RecoveryData.At(4, 23, 1));

        Assert.Equal(BotState.Recovering, h.Core.State);
        Assert.Equal(1, Lines(h, "alert", "event=queued kind=DailySummary "));
        Assert.Equal(RecoveryData.At(4, 23, 1), h.Hooks.InactivityChecks[^1]);
    }
}
