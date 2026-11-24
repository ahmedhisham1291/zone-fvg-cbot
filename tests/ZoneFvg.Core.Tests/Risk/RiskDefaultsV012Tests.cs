using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;
using D = ZoneFvg.Core.Tests.RecoverySizingData;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// The risk defaults of spec v0.12 on the 200,000 USD account (RSK-018) and the rules they change:
/// the switchable sanity cap (RSK-002), the open-risk and currency caps off by default (RSK-004,
/// RSK-005), the 2% daily budget (RSK-006) and the FTMO guard at 200,000 (RSK-009).
/// </summary>
/// <remarks>
/// Forex positions: entry 1.10000 and a 20-pip stop, so 500,000 units risk 1,000 and 10,000 units
/// risk 20. Snapshots at <see cref="RiskData.Now"/>, history since the epoch.
/// </remarks>
public class RiskDefaultsV012Tests
{
    private static RiskEngine Engine(RiskParameters? parameters = null) => new(parameters ?? D.Defaults);

    private static PositionInfo Buy(long id, string symbol, double volume) => RiskData.Position(id, symbol, Side.Buy, volume, 1.1, 1.098);

    private static PlannedTrade NewBuy(string symbol, double volume, double margin = 0.0) => new(symbol, Side.Buy, 1.1, 1.098, volume, margin);

    private static RiskSnapshot Snapshot(double balance, IEnumerable<PositionInfo>? positions = null, IEnumerable<ClosedTradeInfo>? history = null, IEnumerable<SymbolInfo>? symbols = null, double? equity = null)
    {
        return RiskData.Snapshot(RiskData.Account(balance, equity ?? balance, balance), positions, null, history, symbols);
    }

    // ------------------------------------------------------------------ RSK-018 validation

    [Theory]
    [Trait("Req", "RSK-018")]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(100.0001)]
    public void Validate_RecoveryRunLossPercentOutOfRange_ThrowsNamingIt(double value)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (D.Defaults with { RecoveryRunLossPercent = value }).Validate());

        Assert.Equal("RecoveryRunLossPercent", exception.ParamName);
        Assert.StartsWith("RecoveryRunLossPercent must be greater than 0 and at most 100 but was ", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "RSK-018")]
    [InlineData("RecoveryMaxRiskPercent", 0.0)]
    [InlineData("RecoveryMaxRiskPercent", 100.0001)]
    [InlineData("RecoveryMaxRiskPercent", double.NaN)]
    [InlineData("SanityCapPercent", 0.0)]
    [InlineData("SanityCapPercent", 100.0001)]
    [InlineData("SanityCapPercent", double.NegativeInfinity)]
    public void Validate_PercentOutOfRange_ThrowsNamingIt(string name, double value)
    {
        RiskParameters parameters = name == "SanityCapPercent" ? D.Defaults with { SanityCapPercent = value } : D.Defaults with { RecoveryMaxRiskPercent = value };

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => parameters.Validate());

        Assert.Equal(name, exception.ParamName);
        Assert.StartsWith(name + " must be greater than 0 and at most 100 but was ", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-018")]
    public void Validate_BoundaryValues_AreAccepted()
    {
        Assert.Null(Record.Exception(() => (D.Defaults with { RecoveryRunLossPercent = 0.01 }).Validate()));
        Assert.Null(Record.Exception(() => (D.Defaults with { RecoveryRunLossPercent = 100.0 }).Validate()));
        Assert.Null(Record.Exception(() => (D.Defaults with { RecoveryMaxRiskPercent = 100.0, SanityCapPercent = 100.0 }).Validate()));
        Assert.Null(Record.Exception(() => D.Recovery.Validate()));
    }

    [Fact]
    [Trait("Req", "RSK-018")]
    public void Validate_UndefinedSizingPlan_ThrowsNamingIt()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => (D.Defaults with { SizingPlan = (SizingPlan)2 }).Validate());

        Assert.Equal("SizingPlan", exception.ParamName);
        Assert.StartsWith("SizingPlan must be Ladder or Recovery but was 2.", exception.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => (D.Defaults with { SizingPlan = (SizingPlan)(-1) }).ValidateSizing());
    }

    [Fact]
    [Trait("Req", "RSK-018")]
    public void SizingPlan_Members_AreLadderZeroAndRecoveryOne()
    {
        Assert.Equal(new[] { "Ladder", "Recovery" }, Enum.GetNames<SizingPlan>());
        Assert.Equal(0, (int)SizingPlan.Ladder);
        Assert.Equal(1, (int)SizingPlan.Recovery);
        Assert.Equal(SizingPlan.Ladder, default(SizingPlan));
    }

    [Fact]
    [Trait("Req", "RSK-018")]
    public void Defaults_EveryOtherValueUnchanged()
    {
        var parameters = new RiskParameters();

        Assert.Equal(0.5, parameters.RiskPercent);
        Assert.Equal(1.5, parameters.MaxOpenRiskPercent);
        Assert.Equal(2, parameters.MaxPositionsPerCurrency);
        Assert.Equal(8.0, parameters.MaxDrawdownPercent);
        Assert.True(parameters.LadderEnabled);
        Assert.Equal(0.30, parameters.LadderDealRiskPercent);
        Assert.Equal(1.5924, parameters.LadderGrowth);
        Assert.Equal(0.2962, parameters.LadderFirstWeight);
        Assert.Equal(0.5, parameters.MaxRiskPercentPerTrade);
    }

    // ------------------------------------------------------------------ RSK-002 sanity cap

    private static SizingResult Size(RiskParameters parameters, double riskPercent, double balance = D.Capital, double stop = 1.098)
    {
        var plan = new TradePlan("EURUSD", Side.Buy, 1.1, stop, 1.104);
        return new PositionSizer(parameters).Size(RiskData.Account(balance), plan, RiskData.EurUsd, riskPercent);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_SanityCapOffByDefault_056PercentAllowed()
    {
        SizingResult result = Size(D.Defaults, 0.56);

        Assert.True(result.IsAllowed);
        Assert.Equal(560000.0, result.VolumeUnits);
        Assert.Equal(1120.0, result.RiskMoney, 6);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_SanityCapOn_056RefusedAndExactly055Allowed()
    {
        RiskParameters on = D.Defaults with { SanityCapEnabled = true };

        SizingResult above = Size(on, 0.56);
        SizingResult at = Size(on, 0.55);

        Assert.Equal(RiskReason.RiskAboveSanityCap, above.Reason);
        Assert.Equal(1120.0, above.Measured, 6);
        Assert.Equal(1100.0, above.Limit, 6);
        Assert.StartsWith("risk 1120", above.Detail, StringComparison.Ordinal);
        Assert.Contains(" above 0.55% of balance 1100", above.Detail, StringComparison.Ordinal);
        Assert.True(above.AlertRequired);
        Assert.True(at.IsAllowed);
        Assert.Equal(550000.0, at.VolumeUnits);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_SanityCapOnAt128Percent_2560Allowed2562Refused()
    {
        RiskParameters on = D.Defaults with { SanityCapEnabled = true, SanityCapPercent = 1.28 };

        // 1.28% of 200,000 = 2,560: 1,280,000 units. 1.281%: 2,562, 1,281,000 units, above the limit.
        SizingResult at = Size(on, 1.28);
        SizingResult above = Size(on, 1.281);

        Assert.True(at.IsAllowed, at.Detail);
        Assert.Equal(1280000.0, at.VolumeUnits);
        Assert.Equal(RiskReason.RiskAboveSanityCap, above.Reason);
        Assert.Equal(2562.0, above.Measured, 6);
        Assert.Equal(2560.0, above.Limit, 6);
        Assert.Contains(" above 1.28% of balance 2560", above.Detail, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-002")]
    public void Size_SanityCapOff_TheOtherSanityChecksStillRefuseInTheirOrder()
    {
        RiskParameters off = D.Defaults with { MaxVolumeUnits = 100000.0 };

        Assert.Equal(RiskReason.VolumeAboveCap, Size(off, 0.5).Reason);
        Assert.Equal(RiskReason.StopOnWrongSide, Size(off, 0.5, stop: 1.1).Reason);

        // Wrong stop and wrong target together: the stop is reported first, as before.
        var both = new TradePlan("EURUSD", Side.Buy, 1.1, 1.102, 1.098);
        Assert.Equal(RiskReason.StopOnWrongSide, new PositionSizer(off).Size(RiskData.Account(D.Capital), both, RiskData.EurUsd, 0.5).Reason);
        var target = new TradePlan("EURUSD", Side.Buy, 1.1, 1.098, 1.1);
        Assert.Equal(RiskReason.TargetOnWrongSide, new PositionSizer(off).Size(RiskData.Account(D.Capital), target, RiskData.EurUsd, 0.5).Reason);

        // Volume above the cap comes before the sanity cap when both apply.
        Assert.Equal(RiskReason.VolumeAboveCap, Size(off with { SanityCapEnabled = true }, 0.6).Reason);
    }

    // ------------------------------------------------------------------ RSK-004 open-risk cap

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_OpenRiskCapOffByDefault_4000OpenAllowedTheDailyBudgetDecides()
    {
        PositionInfo[] three = { Buy(1, "EURUSD", 500000.0), Buy(2, "GBPUSD", 500000.0), Buy(3, "AUDUSD", 500000.0) };
        RiskSnapshot snapshot = Snapshot(D.Capital, three);

        RiskCheckResult allowed = Engine().CheckNewTrade(snapshot, NewBuy("EURUSD", 500000.0));
        RiskCheckResult oneCentMore = Engine().CheckNewTrade(snapshot, NewBuy("EURUSD", 500005.0));

        Assert.True(allowed.IsAllowed, allowed.Detail);
        Assert.Equal(3000.0, Engine().OpenRiskAtStop(snapshot).RiskMoney, 6);
        Assert.Equal(RiskReason.DailyBudgetExceeded, oneCentMore.Reason);
        Assert.Equal(4000.01, oneCentMore.Measured, 6);
        Assert.Equal(4000.0, oneCentMore.Limit, 6);
    }

    [Fact]
    [Trait("Req", "RSK-004")]
    public void CheckNewTrade_OpenRiskCapOn_4000RefusedAndExactly3000Allowed()
    {
        RiskEngine engine = Engine(D.Defaults with { OpenRiskCapEnabled = true });
        PositionInfo[] three = { Buy(1, "EURUSD", 500000.0), Buy(2, "GBPUSD", 500000.0), Buy(3, "AUDUSD", 500000.0) };

        RiskCheckResult refused = engine.CheckNewTrade(Snapshot(D.Capital, three), NewBuy("EURUSD", 500000.0));
        RiskCheckResult atCap = engine.CheckNewTrade(Snapshot(D.Capital, three.Take(2)), NewBuy("EURUSD", 500000.0));

        Assert.Equal(RiskReason.OpenRiskCapExceeded, refused.Reason);
        Assert.Equal(4000.0, refused.Measured, 6);
        Assert.Equal(3000.0, refused.Limit, 6);
        Assert.True(atCap.IsAllowed, atCap.Detail);
    }

    // ------------------------------------------------------------------ RSK-005 currency cap

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_CurrencyCapOffByDefault_ThirdUsdShortAllowed()
    {
        PositionInfo[] two = { Buy(1, "EURUSD", 10000.0), Buy(2, "GBPUSD", 10000.0) };

        RiskCheckResult result = Engine().CheckNewTrade(Snapshot(D.Capital, two), NewBuy("AUDUSD", 10000.0));

        Assert.True(result.IsAllowed, result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    public void CheckNewTrade_CurrencyCapOn_ThirdUsdShortRefused()
    {
        PositionInfo[] two = { Buy(1, "EURUSD", 10000.0), Buy(2, "GBPUSD", 10000.0) };

        RiskCheckResult result = Engine(D.Defaults with { CurrencyCapEnabled = true }).CheckNewTrade(Snapshot(D.Capital, two), NewBuy("AUDUSD", 10000.0));

        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, result.Reason);
        Assert.Equal(3.0, result.Measured);
        Assert.Equal(2.0, result.Limit);
        Assert.Equal("USD short", result.Detail);
    }

    [Fact]
    [Trait("Req", "RSK-005")]
    [Trait("Req", "RSK-015")]
    public void CheckNewTrade_GoldWithTheForexPairs_CountedOnlyWhenTheCapIsOn()
    {
        IEnumerable<SymbolInfo> symbols = RiskData.AllSymbols.Concat(new[] { GoldData.Gold() });
        PositionInfo gold = RiskData.Position(1, GoldData.Symbol, Side.Buy, 10.0, 2650.0, 2642.0);
        PositionInfo[] positions = { gold, Buy(2, "EURUSD", 10000.0) };
        RiskSnapshot snapshot = Snapshot(D.Capital, positions, symbols: symbols);

        RiskCheckResult off = Engine().CheckNewTrade(snapshot, NewBuy("GBPUSD", 10000.0));
        RiskCheckResult on = Engine(D.Defaults with { CurrencyCapEnabled = true }).CheckNewTrade(snapshot, NewBuy("GBPUSD", 10000.0));

        Assert.True(off.IsAllowed, off.Detail);
        Assert.Equal(RiskReason.CurrencyExposureCapExceeded, on.Reason);
        Assert.Equal("USD short", on.Detail);
    }

    // ------------------------------------------------------------------ RSK-006 daily budget at 2%

    private static RiskSnapshot AfterTodaysLoss(IEnumerable<PositionInfo>? positions = null)
    {
        // One loss of 2,560 closed today: balance 197,440, M = 200,000.
        ClosedTradeInfo loss = RiskData.Closed(1, "EURUSD", RiskData.DayStart.AddHours(8.0), RiskData.DayStart.AddHours(9.0), -2560.0, 197440.0);
        return Snapshot(197440.0, positions, new[] { loss });
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void DailyBudget_At200000_Is4000AndTheDayStartIs200000()
    {
        DailyBudgetStatus status = Engine().DailyBudgetExhausted(AfterTodaysLoss());

        Assert.Equal(200000.0, status.DayStartBalance, 6);
        Assert.Equal(4000.0, status.Limit, 6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_AfterALossOf2560_1440AllowedOneCentMoreOrACentOfCommissionRefused()
    {
        RiskCheckResult exactly = Engine().CheckNewTrade(AfterTodaysLoss(), NewBuy("EURUSD", 720000.0));
        RiskCheckResult oneCent = Engine().CheckNewTrade(AfterTodaysLoss(), NewBuy("EURUSD", 720005.0));
        RiskSnapshot withCommission = RiskData.Snapshot(
            RiskData.Account(197440.0),
            history: AfterTodaysLoss().History,
            symbols: new[] { RiskData.EurUsd with { RoundTripCommissionPerUnit = 0.01 / 720000.0 }, RiskData.GbpUsd, RiskData.AudUsd, RiskData.UsdJpy });
        RiskCheckResult commission = Engine().CheckNewTrade(withCommission, NewBuy("EURUSD", 720000.0));

        Assert.True(exactly.IsAllowed, exactly.Detail);
        Assert.Equal(RiskReason.DailyBudgetExceeded, oneCent.Reason);
        Assert.Equal(4000.01, oneCent.Measured, 6);
        Assert.Equal(RiskReason.DailyBudgetExceeded, commission.Reason);
        Assert.Equal(4000.01, commission.Measured, 6);
    }

    [Fact]
    [Trait("Req", "RSK-006")]
    public void CheckNewTrade_DailyLossLimit15Explicitly_LimitIs3000()
    {
        RiskCheckResult result = Engine(D.Defaults with { DailyLossLimitPercent = 1.5 }).CheckNewTrade(AfterTodaysLoss(), NewBuy("EURUSD", 720000.0));

        Assert.Equal(RiskReason.DailyBudgetExceeded, result.Reason);
        Assert.Equal(4000.0, result.Measured, 6);
        Assert.Equal(3000.0, result.Limit, 6);
    }

    // ------------------------------------------------------------------ RSK-009 FTMO guard at 200,000

    [Theory]
    [Trait("Req", "RSK-009")]
    [InlineData(182000.0, true)]
    [InlineData(182000.01, false)]
    public void EmergencyGuard_OverallFloorAt200000_Is182000(double equity, bool overall)
    {
        // Balance 182,000 too, so M = 182,000 and the daily floor 174,000 is not reached.
        EmergencyDecision decision = Engine().EmergencyGuard(Snapshot(equity, equity: equity));

        Assert.Equal(182000.0, decision.OverallFloor, 6);
        Assert.Equal(overall, decision.OverallFloorReached);
        Assert.False(decision.DailyFloorReached);
        Assert.Equal(overall ? EmergencyAction.CloseAllAndHalt : EmergencyAction.None, decision.Action);
    }

    [Theory]
    [Trait("Req", "RSK-009")]
    [InlineData(192000.0, true)]
    [InlineData(192000.01, false)]
    public void EmergencyGuard_DailyFloorAt200000_IsTheDayStartMinus8000(double equity, bool daily)
    {
        EmergencyDecision decision = Engine().EmergencyGuard(Snapshot(D.Capital, equity: equity));

        Assert.Equal(192000.0, decision.DailyFloor, 6);
        Assert.Equal(daily, decision.DailyFloorReached);
        Assert.False(decision.OverallFloorReached);
        Assert.Equal(daily ? EmergencyAction.CloseAllAndHalt : EmergencyAction.None, decision.Action);
    }
}
