using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// RSK-011: every risk decision is a pure function of an explicit input snapshot. The risk module
/// holds no mutable state between calls, except the in-memory request and error counters of
/// RSK-010.
/// </summary>
public class RiskModulePurityTests
{
    private const BindingFlags AllFields = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private static readonly string[] AllowedMutable = { "RequestCounter", "ErrorCounter", "EventWindow" };

    private static IEnumerable<Type> RiskTypes()
    {
        return typeof(RiskEngine).Assembly.GetTypes()
            .Where(type => type.Namespace == "ZoneFvg.Core.Risk")
            .Where(type => !type.IsEnum)
            .Where(type => type.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void RiskEngine_HasNoInstanceFieldOtherThanTheReadonlyParameters()
    {
        FieldInfo[] fields = typeof(RiskEngine).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        FieldInfo field = Assert.Single(fields);
        Assert.True(field.IsInitOnly);
        Assert.Equal(typeof(RiskParameters), field.FieldType);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void PositionSizer_HasNoInstanceFieldOtherThanTheReadonlyParameters()
    {
        FieldInfo[] fields = typeof(PositionSizer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        FieldInfo field = Assert.Single(fields);
        Assert.True(field.IsInitOnly);
        Assert.Equal(typeof(RiskParameters), field.FieldType);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void RiskTypes_OtherThanTheCounters_HaveOnlyReadonlyFields()
    {
        var mutable = new List<string>();
        foreach (Type type in RiskTypes().Where(type => !AllowedMutable.Contains(type.Name)))
        {
            foreach (FieldInfo field in type.GetFields(AllFields))
            {
                if (!field.IsLiteral && !field.IsInitOnly)
                {
                    mutable.Add(type.Name + "." + field.Name);
                }
            }
        }

        Assert.Empty(mutable);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void RiskTypes_HaveNoStaticFieldOfAMutableType()
    {
        // Static fields may only be constants or readonly values of immutable types.
        Type[] immutable = { typeof(TimeSpan), typeof(string), typeof(double), typeof(int) };
        var offending = new List<string>();
        foreach (Type type in RiskTypes())
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!field.IsLiteral && !(field.IsInitOnly && immutable.Contains(field.FieldType)))
                {
                    offending.Add(type.Name + "." + field.Name);
                }
            }
        }

        Assert.Empty(offending);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void RiskEngine_DecisionMethods_TakeOnlyTheSnapshotAndThePlannedTrade()
    {
        Type[] allowed = { typeof(RiskSnapshot), typeof(PlannedTrade), typeof(string) };
        MethodInfo[] methods = typeof(RiskEngine).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .ToArray();

        Assert.NotEmpty(methods);
        foreach (MethodInfo method in methods)
        {
            Assert.All(method.GetParameters(), parameter => Assert.Contains(parameter.ParameterType, allowed));
        }
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void RiskEngine_SameSnapshot_GivesTheSameResultsEveryTime()
    {
        RiskSnapshot snapshot = BusySnapshot(reverse: false);
        RiskEngine engine = RiskData.Engine;
        PlannedTrade trade = RiskData.Trade("GBPUSD", Side.Buy, 1.3, 1.298, 100000.0);

        RiskCheckResult first = engine.CheckNewTrade(snapshot, trade);
        DailyBudgetStatus budgetFirst = engine.DailyBudgetExhausted(snapshot);
        RiskCheckResult second = engine.CheckNewTrade(snapshot, trade);
        DailyBudgetStatus budgetSecond = engine.DailyBudgetExhausted(snapshot);

        Assert.Equal(first.Reason, second.Reason);
        Assert.Equal(first.Measured, second.Measured);
        Assert.Equal(budgetFirst.Used, budgetSecond.Used);
        Assert.Equal(budgetFirst.WorstCaseEquity, budgetSecond.WorstCaseEquity);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void RiskEngine_BrokerDataInAnotherOrder_GivesBitIdenticalResults()
    {
        RiskSnapshot forward = BusySnapshot(reverse: false);
        RiskSnapshot backward = BusySnapshot(reverse: true);
        RiskEngine engine = RiskData.Engine;

        Assert.Equal(engine.DayStartBalance(forward), engine.DayStartBalance(backward));
        Assert.Equal(engine.OpenRiskAtStop(forward).RiskMoney, engine.OpenRiskAtStop(backward).RiskMoney);
        Assert.Equal(engine.DailyBudgetExhausted(forward).Used, engine.DailyBudgetExhausted(backward).Used);
        Assert.Equal(engine.KillSwitch(forward).Peak, engine.KillSwitch(backward).Peak);
        Assert.Equal(engine.EmergencyGuard(forward).DailyFloor, engine.EmergencyGuard(backward).DailyFloor);
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void RiskEngine_Constructor_ValidatesTheParameters()
    {
        Assert.Throws<ArgumentNullException>(() => new RiskEngine(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RiskEngine(new RiskParameters()));
    }

    [Fact]
    [Trait("Req", "RSK-011")]
    public void RiskEngine_NullArguments_Throw()
    {
        RiskEngine engine = RiskData.Engine;
        RiskSnapshot snapshot = RiskData.Snapshot(RiskData.Account(100000.0));

        Assert.Throws<ArgumentNullException>(() => engine.OpenRiskAtStop(null!));
        Assert.Throws<ArgumentNullException>(() => engine.CheckNewTrade(null!, RiskData.Trade("EURUSD", Side.Buy, 1.1, 1.098, 1000.0)));
        Assert.Throws<ArgumentNullException>(() => engine.CheckNewTrade(snapshot, null!));
        Assert.Throws<ArgumentNullException>(() => engine.KillSwitch(null!));
        Assert.Throws<ArgumentNullException>(() => engine.EmergencyGuard(null!));
        Assert.Throws<ArgumentNullException>(() => engine.DailyBudgetExhausted(null!));
    }

    [Theory]
    [Trait("Req", "RSK-011")]
    [InlineData("ZFVG|v1|EURUSD|D-20260113-0800", true)]
    [InlineData("ZFVG|", true)]
    [InlineData("ZFVG", false)]
    [InlineData("zfvg|v1|EURUSD|x", false)]
    [InlineData("manual", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsBotLabel_RecognisesTheBotPrefixExactly(string? label, bool expected)
    {
        Assert.Equal(expected, RiskEngine.IsBotLabel(label));
    }

    private static RiskSnapshot BusySnapshot(bool reverse)
    {
        // Values with many binary digits (0.1, 0.2, 0.7 and odd volumes) so that a different summation order would
        // show up in the last bits.
        PositionInfo[] positions =
        {
            RiskData.Position(1, "EURUSD", Side.Buy, 123000.0, 1.10017, 1.09871),
            RiskData.Position(2, "USDJPY", Side.Sell, 77000.0, 125.013, 125.197),
            RiskData.Position(3, "AUDUSD", Side.Buy, 51000.0, 0.65031, 0.64899),
        };
        PendingOrderInfo[] orders =
        {
            RiskData.Order(10, "GBPUSD", Side.Sell, 33000.0, 1.30071, 1.30213),
            RiskData.Order(11, "EURUSD", Side.Sell, 47000.0, 1.10311, 1.10477),
        };
        ClosedTradeInfo[] history =
        {
            RiskData.Closed(20, "EURUSD", RiskData.Now.AddHours(-6.0), RiskData.Now.AddHours(-5.0), 0.1, 100000.1),
            RiskData.Closed(21, "GBPUSD", RiskData.Now.AddHours(-6.0), RiskData.Now.AddHours(-5.0), 0.2, 100000.3),
            RiskData.Closed(22, "AUDUSD", RiskData.Now.AddHours(-6.0), RiskData.Now.AddHours(-4.0), -0.7, 99999.6),
            RiskData.Closed(23, "USDJPY", RiskData.Epoch.AddDays(2.0), RiskData.Epoch.AddDays(3.0), 1234.56, 101234.56),
        };
        SymbolInfo[] symbols = RiskData.AllSymbols.Select(s => s with { RoundTripCommissionPerUnit = 0.000047 }).ToArray();
        if (reverse)
        {
            Array.Reverse(positions);
            Array.Reverse(orders);
            Array.Reverse(history);
            Array.Reverse(symbols);
        }

        return RiskData.Snapshot(RiskData.Account(99999.6, 99876.54, 95000.0), positions, orders, history, symbols);
    }
}
