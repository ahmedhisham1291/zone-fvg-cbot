using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-018: with SizingPlan Ladder (the default) every order, journal line and the parameter hash
/// are those of before WP38, in every entry mode and model; with Recovery the hash is the same and
/// the start line gains <c>sizing=Recovery</c> at its very end. The reference scenarios of
/// REC-017 (Breakout), the WP07 recovery data (ZoneFvg, Fvg), REC-020 (Choch) and SIG-034
/// (SessionRange) run to their first placement.
/// </summary>
public class Rsk018LadderUnchangedScenarios
{
    public static IEnumerable<object[]> Modes()
    {
        yield return new object[] { "Breakout", BreakoutRecoveryData.Hash };
        yield return new object[] { "ZoneFvg", RecoveryData.Hash };
        yield return new object[] { "Choch", ChochBotData.Hash };
        yield return new object[] { "SessionRange", SessionRangeBotData.Hash };
    }

    private static BotHarness Run(string mode, Func<RiskParameters, RiskParameters> risk)
    {
        BotHarness h;
        BotParameters parameters;
        DateTime start;
        DateTime until;
        switch (mode)
        {
            case "Breakout":
                start = BreakoutRecoveryData.Start;
                h = BreakoutRecoveryData.Harness(start);
                parameters = BreakoutRecoveryData.Parameters();
                until = BreakoutRecoveryData.Confirmation;
                break;
            case "ZoneFvg":
                start = RecoveryData.Activation;
                h = new BotHarness();
                parameters = RecoveryData.Parameters();
                until = RecoveryData.Confirmation;
                break;
            case "Choch":
                start = ChochBotData.Start;
                h = ChochBotData.Harness(start);
                parameters = ChochBotData.Parameters();
                until = ChochBotData.Confirmation;
                break;
            default:
                start = SessionRangeBotData.Utc(2026, 3, 4, 6, 0);
                h = SessionRangeBotData.Harness(start, SessionRangeBotData.Bars(SessionRangeBotData.Winter));
                parameters = SessionRangeBotData.Parameters();
                until = SessionRangeBotData.Utc(2026, 3, 4, 8, 0);
                break;
        }

        h.Start(start, parameters with { Risk = risk(parameters.Risk) });
        h.FeedTo(until);
        return h;
    }

    private static string CommentOf(BrokerRequest request)
    {
        return request.Order?.Comment ?? request.StopOrder!.Comment;
    }

    [Theory]
    [Trait("Req", "RSK-018")]
    [MemberData(nameof(Modes))]
    public void LadderLeftAtItsDefaultOrSetExplicitly_SameOrdersJournalAndHash(string mode, string hash)
    {
        foreach (bool ladderOn in new[] { false, true })
        {
            BotHarness byDefault = Run(mode, r => r with { LadderEnabled = ladderOn });
            BotHarness explicitly = Run(mode, r => r with { LadderEnabled = ladderOn, SizingPlan = SizingPlan.Ladder });

            BrokerRequest placed = Assert.Single(byDefault.Broker.Requests);
            Assert.Equal(byDefault.Broker.Requests, explicitly.Broker.Requests);
            Assert.Equal(byDefault.Journal.Entries, explicitly.Journal.Entries);
            Assert.EndsWith(";h=" + hash, CommentOf(placed), StringComparison.Ordinal);
            string start = Assert.Single(byDefault.Journal.Lines("start"));
            Assert.Contains(" hash=" + hash + " ", start, StringComparison.Ordinal);
            Assert.DoesNotContain("sizing=", start, StringComparison.Ordinal);
        }
    }

    [Theory]
    [Trait("Req", "RSK-018")]
    [MemberData(nameof(Modes))]
    public void Recovery_SameHash_TheStartLineEndsWithSizingRecovery(string mode, string hash)
    {
        BotHarness ladder = Run(mode, r => r);
        BotHarness recovery = Run(mode, r => r with { SizingPlan = SizingPlan.Recovery });

        string ladderStart = Assert.Single(ladder.Journal.Lines("start"));
        string recoveryStart = Assert.Single(recovery.Journal.Lines("start"));
        Assert.Equal(ladderStart + " sizing=Recovery", recoveryStart);
        Assert.EndsWith(";h=" + hash, CommentOf(Assert.Single(recovery.Broker.Requests)), StringComparison.Ordinal);
    }
}
