using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.News;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// OPS-007: a full scenario with the execution layer wired to the operations module through
/// <see cref="FakeBroker"/>, where secrets are pushed into broker error codes, exception messages
/// and alert texts. Afterwards every string written to the journal sink, every alert text and the
/// text part of every Telegram request is searched for the token, the chat id and the heartbeat
/// address. The secrets may appear only where they must: the token and chat id in the Telegram
/// request address, the heartbeat address as the heartbeat request.
/// </summary>
public class OpsSecretScenarioTests
{
    private const string TelegramPrefix = "https://api.telegram.org/bot" + OpsData.Token + "/sendMessage?chat_id=" + OpsData.ChatId + "&text=";

    [Fact]
    [Trait("Req", "OPS-007")]
    public void FullScenario_NoSecretInJournalAlertsOrRequestTexts()
    {
        FakeBroker broker = ExecutionHarness.DefaultBroker();
        DateTime start = broker.ServerTimeUtc;
        var transport = new ScriptedTransport();
        transport.ThenThrow(url => new InvalidOperationException("GET " + url + " failed"));
        transport.ThenThrow(url => new InvalidOperationException("GET " + url + " failed"));
        transport.Then(ScriptedTransport.Failed(502));
        var sink = new RecordingSink();
        var ops = new Operations(OpsData.WithSecrets, ExecutionData.RiskDefaults, OpsData.Instance, broker, transport, sink, start);
        var news = new NewsService(new NewsParameters(), new[] { NewsTestData.ForexFactoryFeed() }, new MemoryStore(), new FakeTransport(NewsTestData.Failed()), true, ExecutionData.QuietNews);
        var counter = new RequestCounter(ExecutionData.RiskDefaults);
        var filters = new PreTradeFilters(ExecutionData.ExecutionDefaults, ExecutionData.SignalDefaults, ExecutionData.RiskDefaults, broker, news, ops.Events);
        var orders = new OrderManager(broker, counter, ops.Events);
        var trades = new TradeManager(ExecutionData.Symbol, ExecutionData.ExecutionDefaults, broker, counter, ops.Events);
        PreTradeResult Evaluate(Signal signal) => filters.Evaluate(signal, new PreTradeContext(ExecutionData.Symbol, BotState.Running, Array.Empty<Zone>(), orders.HasUnresolved(ExecutionData.Symbol)));
        SymbolInfo symbol = broker.GetSymbol(ExecutionData.Symbol);

        // A stale signal is skipped and journaled.
        Signal stale = ExecutionData.Buy(closeUtc: start.AddMinutes(-30.0));
        PreTradeResult skipped = Evaluate(stale);
        Assert.False(skipped.IsAllowed);
        ops.Journal.Signal(SignalJournalEntry.Skipped(stale, ExecutionData.Symbol, Bias.Long, null, skipped.Skip!, symbol, NewsStatus.Available));

        // The broker rejects a buy with an error code that contains the token.
        broker.Script(BrokerCall.Place, BrokerFault.Rejected, "denied-for-" + OpsData.Token);
        PreTradeResult buy = Evaluate(ExecutionData.Buy());
        Assert.True(buy.IsAllowed);
        Assert.Equal(PlaceOutcome.Rejected, orders.TryPlace(buy.Order!).Outcome);

        // A sell is placed and journaled as taken.
        PreTradeResult sell = Evaluate(ExecutionData.Sell());
        Assert.True(sell.IsAllowed);
        orders.TryPlace(sell.Order!);
        ops.Journal.Signal(SignalJournalEntry.Taken(sell.Order!, Bias.Short, null, symbol, NewsStatus.Available));

        // A position without a stop loss: alerts for the missing and the restored stop.
        broker.AddPosition(ExecutionData.Symbol, Side.Buy, 1000.0, 1.1, null, 1.104, ExecutionData.Label("D-20260113-0800"), "e=1.10000;s=1.09800;t=1.10400;r=2.00;h=0123abcd");
        trades.EnsureProtection();

        // Errors and alerts that carry every secret.
        ops.Journal.Error(start, "OnTimer", new InvalidOperationException("heartbeat " + OpsData.HeartbeatUrl + " down", new ArgumentException("bot" + OpsData.Token)));
        ops.Alerts.Raise(OpsAlertKind.ErrorStorm, "chat " + OpsData.ChatId + " token " + OpsData.Token + " url " + OpsData.HeartbeatUrl, start);
        ops.Journal.State(start, BotState.Running, BotState.Halted, "error storm at " + OpsData.HeartbeatUrl);
        ops.Events.NewsUpdated(NewsTestData.LiveService(new MemoryStore(), new FakeTransport(NewsTestData.Failed())).Refresh(start), start);

        // A closed trade, the inactivity check, the timer for 40 minutes and the next daily summary.
        ops.Journal.Trade(new TradeJournalEntry(
            new ClosedTradeInfo(1001, ExecutionData.Symbol, ExecutionData.Label(ExecutionData.BuyZone), start, start.AddMinutes(20.0), 993.0, -7.0, 0.0, 100993.0),
            "e=1.10000;s=1.09800;t=1.10400;r=500.00;h=0123abcd",
            Side.Buy,
            1.1,
            0.0001,
            TradeExitReason.TakeProfit,
            null,
            null));
        ops.Inactivity.Check(start, Array.Empty<ClosedTradeInfo>(), start.AddDays(-30.0), Array.Empty<PositionInfo>());
        for (int minute = 0; minute <= 40; minute++)
        {
            ops.OnTimer(start.AddMinutes(minute), minute % 2 == 0 ? BotState.Running : BotState.Halted);
        }

        DateTime nextDay = new(2026, 1, 14, 23, 0, 0, DateTimeKind.Utc);
        Assert.NotNull(ops.Summary.Pump(nextDay, () => throw new InvalidOperationException("no data at " + OpsData.HeartbeatUrl)));
        for (int minute = 0; minute <= 60; minute++)
        {
            ops.OnTimer(nextDay.AddMinutes(minute), BotState.Halted);
        }

        // The scenario exercised every path: masking happened, alerts went out, heartbeats were called.
        Assert.Contains(sink.Lines, l => l.Line.Contains(SecretMasker.Replacement, StringComparison.Ordinal));
        Assert.Contains(sink.Lines, l => l.Category == "skip");
        Assert.Contains(sink.Lines, l => l.Category == "signal");
        Assert.Contains(sink.Lines, l => l.Category == "trade");
        Assert.Contains(sink.Lines, l => l.Category == "heartbeat");
        Assert.Contains(sink.Lines, l => l.Category == "summary");
        Assert.True(ops.Alerts.Sent >= 7, "alerts sent: " + ops.Alerts.Sent);
        Assert.Equal(0, ops.Alerts.QueueLength);
        Assert.True(ops.Heartbeat.Calls >= 20, "heartbeat calls: " + ops.Heartbeat.Calls);
        Assert.Equal(0, ops.Journal.WriteFailures);

        // No secret in any journal line.
        Assert.All(sink.Lines, l => Assert.False(OpsData.ContainsSecret(l.Line), "secret in journal line: " + l.Category));

        // Secrets in requests only where they must be.
        foreach ((string Url, int Timeout) call in transport.Calls)
        {
            string url = call.Url;
            if (url == OpsData.HeartbeatUrl)
            {
                continue;
            }

            Assert.StartsWith(TelegramPrefix, url, StringComparison.Ordinal);
            string text = Uri.UnescapeDataString(url.Substring(TelegramPrefix.Length));
            Assert.False(OpsData.ContainsSecret(text), "secret in an alert text");
            Assert.StartsWith("ZFVG EURUSD ", text, StringComparison.Ordinal);
        }

        Assert.Contains(transport.Calls, c => Uri.UnescapeDataString(c.Url).Contains("token *** url ***", StringComparison.Ordinal));
        Assert.Contains(transport.Calls, c => Uri.UnescapeDataString(c.Url).Contains("denied-for-***", StringComparison.Ordinal));
    }
}
