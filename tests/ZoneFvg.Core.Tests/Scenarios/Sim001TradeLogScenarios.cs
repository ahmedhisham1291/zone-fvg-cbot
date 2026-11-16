using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIM-001 through the whole core: with <see cref="BotParameters.WriteTradeLog"/> the bot journals
/// one <c>tradelog</c> line per closed trade, which <c>sim extract</c> turns into a valid trade log;
/// and switching the line on changes nothing else: the same broker requests, the same alerts and
/// the same journal lines apart from the <c>tradelog</c> lines. Data: <see cref="RecoveryData"/>;
/// the reference buy fills at 08:50 at 100.675 and closes at its target 103.061 at 09:30, a net
/// 978.26 on a planned risk of 489.13, which is 2.00 R.
/// </summary>
public class Sim001TradeLogScenarios
{
    private static readonly DateTime A = RecoveryData.Activation;

    private static BotHarness Run(bool writeTradeLog, bool hitStop = false)
    {
        var h = new BotHarness();
        h.Broker.ReportExitReasons = true;
        h.Start(A, RecoveryData.Parameters() with { WriteTradeLog = writeTradeLog });
        h.FeedTo(RecoveryData.Confirmation);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        h.Core.OnTradeEvent();
        h.FeedTo(A.AddMinutes(75.0));
        h.Broker.AdvanceTo(A.AddMinutes(90.0));
        if (hitStop)
        {
            h.Broker.Tick(RecoveryData.Symbol, 99.47, 99.48);
        }
        else
        {
            h.Broker.Tick(RecoveryData.Symbol, 103.07, 103.08);
        }

        h.Core.OnTradeEvent();
        h.FeedTo(A.AddHours(4.0));
        h.Timer(A.AddHours(4.0).AddMinutes(1.0));
        return h;
    }

    private static List<(string Category, string Line)> WithoutTradeLog(BotHarness h)
    {
        return h.Journal.Entries.Where(entry => entry.Category != "tradelog").ToList();
    }

    [Theory]
    [Trait("Req", "SIM-001")]
    [InlineData(false)]
    [InlineData(true)]
    public void TradeLogLine_OnAndOff_SameOrdersAlertsAndJournalDecisions(bool hitStop)
    {
        BotHarness off = Run(false, hitStop);
        BotHarness on = Run(true, hitStop);

        Assert.Single(off.Broker.History);
        Assert.Equal(off.Broker.Requests, on.Broker.Requests);
        Assert.Equal(off.Broker.History, on.Broker.History);
        Assert.Equal(WithoutTradeLog(off), WithoutTradeLog(on));
        Assert.Equal(off.Journal.Entries, WithoutTradeLog(off));
        Assert.Single(on.Journal.Lines("tradelog"));
        Assert.Equal(off.Core.State, on.Core.State);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void TradeLogLine_TargetHit_ExtractsToTheTradeOfTheBacktest()
    {
        BotHarness h = Run(true);

        string line = Assert.Single(h.Journal.Lines("tradelog"));
        BacktestLogResult extracted = BacktestLogExtract.Extract(line);

        Assert.True(extracted.IsValid, extracted.Error);
        TradeLogReadResult read = TradeLogCsv.Read(extracted.TradeLog);
        Assert.True(read.IsValid, read.Error);
        TradeRecord trade = Assert.Single(read.Records);
        Assert.Equal(RecoveryData.Symbol, trade.Symbol);
        Assert.Equal(RecoveryData.ZoneId, trade.ZoneId);
        Assert.Equal(A.AddMinutes(50.0), trade.OpenTimeUtc);
        Assert.Equal(A.AddMinutes(90.0), trade.CloseTimeUtc);
        Assert.Equal(Side.Buy, trade.Side);
        Assert.Equal(RecoveryData.Volume, trade.VolumeUnits);
        Assert.Equal(RecoveryData.Entry, trade.Entry, 9);
        Assert.Equal(RecoveryData.Stop, trade.Stop, 9);
        Assert.Equal(RecoveryData.Target, trade.Target, 9);
        Assert.Equal(RecoveryData.Target, trade.ExitPrice, 9);
        Assert.Equal("TakeProfit", trade.ExitReason);
        Assert.Equal(978.26, trade.ResultMoney, 6);
        Assert.Equal(2.0, trade.ResultR, 4);
        Assert.Equal(0.0, trade.Commission);
        Assert.Equal(0.0, trade.Swap);
        Assert.Equal(489.13, trade.RiskMoney);
        Assert.StartsWith("2026-03-04T09:30:00Z tradelog seq=1 row=USDJPY,D-20260303-1600,", line, StringComparison.Ordinal);
        Assert.EndsWith(" problem=none", line, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIM-001")]
    public void TradeLogLine_StopHit_IsAStopLossOfMinusOneR()
    {
        BotHarness h = Run(true, hitStop: true);

        TradeRecord trade = Assert.Single(TradeLogCsv.Read(BacktestLogExtract.Extract(Assert.Single(h.Journal.Lines("tradelog"))).TradeLog).Records);

        Assert.Equal("StopLoss", trade.ExitReason);
        Assert.Equal(RecoveryData.Stop, trade.ExitPrice, 9);
        Assert.Equal(-1.0, trade.ResultR, 2);
    }
}
