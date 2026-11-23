using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// PLT-014: a backtest runs with the default parameters. An empty RiskEpochDate in a backtest is
/// the CE(S)T date of the server time at start (CEST = UTC+2 until 01:00 UTC on 2023-10-29, then
/// CET = UTC+1); live it refuses the start. The news filter is off in a backtest whose embedded
/// history has no events, and otherwise follows NewsEnabled.
/// </summary>
public class BacktestDefaultsTests
{
    private static readonly DateTime Configured = new(2025, 1, 2, 0, 0, 0, DateTimeKind.Utc);

    private static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    private static DateTime Date(int year, int month, int day)
    {
        return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
    }

    // ------------------------------------------------------------------ RiskEpoch: configured date

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData(true)]
    [InlineData(false)]
    public void RiskEpoch_ConfiguredDate_ReturnedUnchangedInBothModes(bool isBacktest)
    {
        // The server date (2023-10-22) differs from the configured date, so a default would show.
        DateTime epoch = BacktestDefaults.RiskEpoch(Configured, isBacktest, Utc(2023, 10, 22, 21, 5));

        Assert.Equal(Configured, epoch);
        Assert.Equal(DateTimeKind.Utc, epoch.Kind);
    }

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData(true)]
    [InlineData(false)]
    public void RiskEpoch_ConfiguredDate_ServerTimeIsNotRead(bool isBacktest)
    {
        // A server time that is not UTC would be refused if it were read.
        DateTime unspecified = new(2023, 10, 22, 21, 5, 0, DateTimeKind.Unspecified);

        Assert.Equal(Configured, BacktestDefaults.RiskEpoch(Configured, isBacktest, unspecified));
    }

    // ------------------------------------------------------------------ RiskEpoch: empty in a backtest

    [Fact]
    [Trait("Req", "PLT-014")]
    public void RiskEpoch_BacktestEmptyAt2105UtcInSummer_IsTheSameCestDate()
    {
        // 21:05 UTC + 2 hours = 23:05 CEST on 2023-10-22.
        DateTime epoch = BacktestDefaults.RiskEpoch(null, true, Utc(2023, 10, 22, 21, 5));

        Assert.Equal(Date(2023, 10, 22), epoch);
        Assert.Equal(DateTimeKind.Utc, epoch.Kind);
        Assert.Equal(TimeSpan.Zero, epoch.TimeOfDay);
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void RiskEpoch_BacktestEmptyAt2230UtcInSummer_IsTheNextCestDate()
    {
        // 22:30 UTC + 2 hours = 00:30 CEST on 2023-10-23: the CE(S)T date, not the UTC date.
        DateTime epoch = BacktestDefaults.RiskEpoch(null, true, Utc(2023, 10, 22, 22, 30));

        Assert.Equal(Date(2023, 10, 23), epoch);
        Assert.Equal(DateTimeKind.Utc, epoch.Kind);
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void RiskEpoch_BacktestEmptyAt2330UtcInWinter_IsTheNextCetDate()
    {
        // 23:30 UTC + 1 hour = 00:30 CET on 2023-12-05.
        DateTime epoch = BacktestDefaults.RiskEpoch(null, true, Utc(2023, 12, 4, 23, 30));

        Assert.Equal(Date(2023, 12, 5), epoch);
        Assert.Equal(DateTimeKind.Utc, epoch.Kind);
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void RiskEpoch_BacktestEmptyAtTheSummerMidnight_OneTickEarlierIsThePreviousDate()
    {
        // CEST midnight of 2023-10-23 is 2023-10-22 22:00 UTC.
        DateTime midnight = Utc(2023, 10, 22, 22, 0);

        Assert.Equal(Date(2023, 10, 23), BacktestDefaults.RiskEpoch(null, true, midnight));
        Assert.Equal(Date(2023, 10, 22), BacktestDefaults.RiskEpoch(null, true, midnight.AddTicks(-1)));
        Assert.Equal(Date(2023, 10, 23), BacktestDefaults.RiskEpoch(null, true, midnight.AddTicks(1)));
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void RiskEpoch_BacktestEmptyAtTheWinterMidnight_OneTickEarlierIsThePreviousDate()
    {
        // CET midnight of 2023-12-05 is 2023-12-04 23:00 UTC; at 22:00 UTC it is still 2023-12-04 (23:00 CET).
        DateTime midnight = Utc(2023, 12, 4, 23, 0);

        Assert.Equal(Date(2023, 12, 5), BacktestDefaults.RiskEpoch(null, true, midnight));
        Assert.Equal(Date(2023, 12, 4), BacktestDefaults.RiskEpoch(null, true, midnight.AddTicks(-1)));
        Assert.Equal(Date(2023, 12, 4), BacktestDefaults.RiskEpoch(null, true, Utc(2023, 12, 4, 22, 0)));
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void RiskEpoch_BacktestEmptyWithAServerTimeThatIsNotUtc_Refused()
    {
        DateTime local = new(2023, 10, 22, 21, 5, 0, DateTimeKind.Local);

        ArgumentException error = Assert.Throws<ArgumentException>(() => BacktestDefaults.RiskEpoch(null, true, local));

        Assert.Equal("serverTimeUtc", error.ParamName);
    }

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData(2023, 10, 22, 21, 5)]
    [InlineData(2023, 10, 22, 22, 30)]
    [InlineData(2023, 12, 4, 23, 30)]
    [InlineData(2023, 10, 22, 22, 0)]
    [InlineData(2023, 12, 4, 23, 0)]
    public void RiskEpoch_BacktestEmpty_PassesTheStartChecksAndTheRiskValidation(int year, int month, int day, int hour, int minute)
    {
        DateTime server = Utc(year, month, day, hour, minute);
        DateTime epoch = BacktestDefaults.RiskEpoch(null, true, server);
        BotParameters parameters = RecoveryData.Parameters() with { Risk = RiskData.Parameters with { RiskEpochDate = epoch } };
        AccountSnapshot account = ExecutionData.Account(100000.0, 100000.0, 100000.0);

        Assert.Null(StartChecks.Check(parameters, account, server, true));
        parameters.Risk.Validate();

        // The epoch starts at or before the server time and less than one day before it: the backtest's first day.
        DateTime epochStart = CestClock.StartOfCestDateUtc(epoch);
        Assert.True(epochStart <= server);
        Assert.True(server - epochStart < TimeSpan.FromDays(1.0));
    }

    // ------------------------------------------------------------------ RiskEpoch: empty live

    [Fact]
    [Trait("Req", "PLT-014")]
    public void RiskEpoch_LiveEmpty_RefusedWithTheMessageOfTodayAndParameterNameRiskEpochDate()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => BacktestDefaults.RiskEpoch(null, false, Utc(2023, 10, 22, 21, 5)));

        Assert.Equal("RiskEpochDate", error.ParamName);
        Assert.Equal("RiskEpochDate must be a date written as yyyy-MM-dd, for example 2026-12-01. (Parameter 'RiskEpochDate')", error.Message);
        Assert.Equal("RiskEpochDate must be a date written as yyyy-MM-dd, for example 2026-12-01.", BacktestDefaults.EmptyRiskEpochMessage);
    }

    [Fact]
    [Trait("Req", "PLT-014")]
    public void RiskEpoch_LiveEmptyWithAServerTimeThatIsNotUtc_RefusedForTheEmptyDate()
    {
        DateTime local = new(2023, 10, 22, 21, 5, 0, DateTimeKind.Local);

        ArgumentException error = Assert.Throws<ArgumentException>(() => BacktestDefaults.RiskEpoch(null, false, local));

        Assert.Equal("RiskEpochDate", error.ParamName);
    }

    // ------------------------------------------------------------------ NewsFilterOn: the truth table

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData(false, false, 0, false)]
    [InlineData(false, false, 1, false)]
    [InlineData(false, false, 250, false)]
    [InlineData(false, true, 0, false)]
    [InlineData(false, true, 1, false)]
    [InlineData(false, true, 250, false)]
    [InlineData(true, false, 0, true)]
    [InlineData(true, false, 1, true)]
    [InlineData(true, false, 250, true)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, true, 1, true)]
    [InlineData(true, true, 250, true)]
    public void NewsFilterOn_TruthTable(bool newsEnabled, bool isBacktest, int embeddedEventCount, bool expected)
    {
        Assert.Equal(expected, BacktestDefaults.NewsFilterOn(newsEnabled, isBacktest, embeddedEventCount));
    }

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void NewsFilterOn_NegativeEventCount_Refused(bool newsEnabled, bool isBacktest)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() => BacktestDefaults.NewsFilterOn(newsEnabled, isBacktest, -1));

        Assert.Equal("embeddedEventCount", error.ParamName);
    }

    // ------------------------------------------------------------------ NewsFilterOffReason: the start log line

    [Fact]
    [Trait("Req", "PLT-014")]
    public void NewsFilterOffReason_NewsEnabledAndFilterOff_IsTheOneLineThatSaysWhy()
    {
        Assert.Equal(
            "news filter off for this backtest: the embedded news history has no events",
            BacktestDefaults.NewsFilterOffReason(true, false));
        Assert.Equal(BacktestDefaults.NewsFilterOffMessage, BacktestDefaults.NewsFilterOffReason(true, BacktestDefaults.NewsFilterOn(true, true, 0)));
    }

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void NewsFilterOffReason_OtherwiseNothing(bool newsEnabled, bool newsFilterOn)
    {
        Assert.Null(BacktestDefaults.NewsFilterOffReason(newsEnabled, newsFilterOn));
    }

    [Theory]
    [Trait("Req", "PLT-014")]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(true, 250)]
    public void NewsFilterOffReason_LiveOrWithEvents_Nothing(bool isBacktest, int embeddedEventCount)
    {
        Assert.Null(BacktestDefaults.NewsFilterOffReason(true, BacktestDefaults.NewsFilterOn(true, isBacktest, embeddedEventCount)));
    }
}
