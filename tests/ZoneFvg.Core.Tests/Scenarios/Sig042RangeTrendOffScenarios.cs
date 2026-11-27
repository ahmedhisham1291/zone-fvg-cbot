using System.Globalization;
using System.Text;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Signals;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-042 with RangeTrendFastEma 0 (off, the core default): a full SessionRange run through
/// <see cref="BotCore"/> and a <see cref="FakeBroker"/> gives, byte for byte, the journal and the
/// broker requests it gave before WP42. The expected text was produced by this test on the code
/// of main before WP42 (commit 8ae7271 of pull request 103 contains only this test on top of main
/// f01b805; its build-test run printed the text) and pasted here; it is the winter day of <see cref="Sig034SessionRangeScenarios"/> from the start at
/// 06:00 UTC to 16:00 UTC.
/// </summary>
public class Sig042RangeTrendOffScenarios
{
    private const string Before =
        "start|2026-03-04T06:00:00Z start event=start symbol=USDJPY version=0.1.0 hash=3f0f30a3 news=enabled backtest=yes riskEpoch=2026-01-01 holidays=12-25,01-01 sessions=none\\sreported mode=SessionRange brake=off\n"
        + "news|2026-03-04T06:00:00Z news source=EmbeddedHistory outcome=HistoryLoaded detail=2\\sevents\n"
        + "state|2026-03-04T06:00:00Z state from=Recovering to=Running reason=recovery\\scomplete\n"
        + "alert|2026-03-04T06:00:00Z alert event=disabled kind=StateChanged hash=af58e2e3 text=ZFVG\\sUSDJPY\\sStateChanged:\\sRecovering\\s->\\sRunning:\\srecovery\\scomplete queue=0\n"
        + "recovery|2026-03-04T06:00:00Z recovery symbol=USDJPY trigger=start adopted=0 kept=0 cancelled=0 strangers=0 zones=0 downtime=NA state=Running\n"
        + "report|2026-03-04T06:00:00Z report trigger=start text=symbol=USDJPY\\sat=2026-03-04T06:00:00Z\\sdowntime=unknown\\sdata=complete[]\\szones=[]\\sreplay-signals=0\\sadopted=[]\\sstrangers=[]\\swithout-target=[]\\sprotection=[]\\sorders-kept=[]\\sorders-cancelled=[]\\sjournaled=0\\srisk={emergency=ok\\sequity\\s100000\\sdaily\\sfloor\\s96000\\soverall\\sfloor\\s91000;\\skill-switch=ok\\speak\\s100000\\sdrawdown\\s0\\slimit\\s8000;\\sdaily-budget=ok\\sused\\s0\\slimit\\s1500;\\sdata=ok}\\scatch-up=[]\\sweekend-close-pending=no\\sstate=Running\\snews=enabled\\shash=3f0f30a3\\sversion=0.1.0\n"
        + "alert|2026-03-04T06:00:00Z alert event=disabled kind=Started hash=de478d2e text=ZFVG\\sUSDJPY\\sStarted:\\ssymbol=USDJPY\\sat=2026-03-04T06:00:00Z\\sdowntime=unknown\\sdata=complete[]\\szones=[]\\sreplay-signals=0\\sadopted=[]\\sstrangers=[]\\swithout-target=[]\\sprotection=[]\\sorders-kept=[]\\sorders-cancelled=[]\\sjournaled=0\\srisk={emergency=ok\\sequity\\s100000\\sdaily\\sfloor\\s96000\\soverall\\sfloor\\s91000;\\skill-switch=ok\\speak\\s100000\\sdrawdown\\s0\\slimit\\s8000;\\sdaily-budget=ok\\sused\\s0\\slimit\\s1500;\\sdata=ok}\\scatch-up=[]\\sweekend-close-pending=no\\sstate=Running\\snews=enabled\\shash=3f0f30a3\\sversion=0.1.0 queue=0\n"
        + "order|2026-03-04T08:00:00Z order event=placement label=ZFVG|v1|USDJPY|RH-20260304 outcome=Placed detail=verified\n"
        + "signal|2026-03-04T08:00:00Z signal symbol=USDJPY result=taken bias=Long zone=RH-20260304 zoneProximal=NA zoneDistal=NA fvgUpper=NA fvgLower=NA entry=100.41 stop=100 target=101.2 stopPips=41.0 volume=121000 spreadPips=1.0 costToRisk=0.024 news=Available skip=none kind=range level=100.4\n"
        + "request|Place 2026-03-04T08:00:00.0000000Z stop USDJPY Buy entry=100.41 sl=100 tp=101.2 volume=121000 expiry=2026-03-04T17:00:00.0000000Z label=ZFVG|v1|USDJPY|RH-20260304 comment=e=100.410;s=100.000;t=101.200;r=496.10;h=3f0f30a3 outcome=Success\n";

    internal static string Transcript(BotHarness h)
    {
        var text = new StringBuilder();
        foreach ((string category, string line) in h.Journal.Entries)
        {
            text.Append(category).Append('|').Append(line).Append('\n');
        }

        foreach (BrokerRequest request in h.Broker.Requests)
        {
            text.Append("request|").Append(request.Call.ToString()).Append(' ').Append(request.AtUtc.ToString("o", CultureInfo.InvariantCulture));
            if (request.StopOrder is not null)
            {
                StopOrderRequest o = request.StopOrder;
                text.Append(" stop ").Append(o.Symbol).Append(' ').Append(o.Side.ToString())
                    .Append(" entry=").Append(R(o.EntryPrice))
                    .Append(" sl=").Append(R(o.StopLossPrice))
                    .Append(" tp=").Append(R(o.TakeProfitPrice))
                    .Append(" volume=").Append(R(o.VolumeUnits))
                    .Append(" expiry=").Append(o.ExpiryUtc.ToString("o", CultureInfo.InvariantCulture))
                    .Append(" label=").Append(o.Label)
                    .Append(" comment=").Append(o.Comment);
            }

            text.Append(" outcome=").Append(request.Outcome.ToString()).Append('\n');
        }

        return text.ToString();
    }

    private static string R(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    internal static BotHarness WinterRun(BotParameters parameters)
    {
        BotHarness h = Data.Started(Data.Utc(2026, 3, 4, 6, 0), Data.Bars(Data.Winter), parameters: parameters);
        h.FeedTo(Data.Utc(2026, 3, 4, 16, 0));
        return h;
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void WinterRun_DefaultSessionRangeParameters_TheTranscriptOfBeforeWp42()
    {
        string actual = Transcript(WinterRun(Data.Parameters()));

        Assert.Equal(Before, actual);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void WinterRun_RangeTrendFastEmaZeroSetExplicitly_TheTranscriptOfBeforeWp42AndHash3f0f30a3()
    {
        BotParameters zero = Data.Parameters() with { SessionRange = new SessionRangeParameters { RangeTrendFastEma = 0 } };

        BotHarness h = WinterRun(zero);

        Assert.Equal(Before, Transcript(h));
        Assert.Equal("3f0f30a3", h.Core.LastReport!.ParameterHash);
        Assert.Equal("3f0f30a3", ParameterHash.Compute(new SignalParameters(), new ExecutionParameters(), null, new SessionRangeParameters { RangeTrendFastEma = 0 }));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void SellRun_RangeTrendFastEmaZero_SameJournalAndRequestsAsTheDefaultRecord()
    {
        BotParameters zero = Data.Parameters() with { SessionRange = new SessionRangeParameters { RangeTrendFastEma = 0 } };

        BotHarness defaults = Data.Started(Data.Utc(2026, 3, 4, 6, 0), Data.Bars(true, null, Data.Winter), sell: true);
        BotHarness explicitZero = Data.Started(Data.Utc(2026, 3, 4, 6, 0), Data.Bars(true, null, Data.Winter), sell: true, parameters: zero);
        defaults.FeedTo(Data.Utc(2026, 3, 4, 16, 0));
        explicitZero.FeedTo(Data.Utc(2026, 3, 4, 16, 0));

        Assert.Equal(Transcript(defaults), Transcript(explicitZero));
        Assert.Contains(" result=taken bias=Short zone=RL-20260304 ", Transcript(explicitZero), StringComparison.Ordinal);
    }
}
