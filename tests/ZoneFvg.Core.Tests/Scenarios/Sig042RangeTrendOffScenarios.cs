using System.Globalization;
using System.Text;
using Xunit;
using Xunit.Sdk;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution.Ports;
using Data = ZoneFvg.Core.Tests.SessionRangeBotData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-042 with RangeTrendFastEma 0 (off, the core default): a full SessionRange run through
/// <see cref="BotCore"/> and a <see cref="FakeBroker"/> gives, byte for byte, the journal and the
/// broker requests it gave before WP42. The expected text was produced by this test on the code
/// of main before WP42 (the first commit of the pull request contains only this test) and pasted
/// here; it is the winter day of <see cref="Sig034SessionRangeScenarios"/> from the start at
/// 06:00 UTC to 16:00 UTC.
/// </summary>
public class Sig042RangeTrendOffScenarios
{
    private const string Before = "";

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

        if (Before.Length == 0)
        {
            throw new XunitException("PIN-BEGIN\n" + actual + "PIN-END");
        }

        Assert.Equal(Before, actual);
    }
}
