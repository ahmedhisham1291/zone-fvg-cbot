using System;
using System.Collections.Generic;
using System.Text;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// FTMO 2-Step challenge simulator (SIM-004, spec 12.1 step 9, 15.2, 15.4, 15.5). Each run starts
/// at 00:00 CE(S)T of a trading day drawn at random from the data and replays every later trade of
/// the combined trade list, in time order, until the phase is passed, a floor is breached or the
/// data ends (a contiguous block from a random start).
/// </summary>
/// <remarks>
/// <para>Scaling (RSK-001): a trade risks RiskPercent of the balance when it opens; its result is
/// result_r times that risk, and at its stop it would lose that risk plus its commission in R
/// (commission / risk_money). Swap is not counted at the stop (architecture section 10).</para>
/// <para>Each event is processed in time order; a close at the same instant as an open comes
/// first. At 00:00 CE(S)T the day's start balance M is the balance then.</para>
/// <list type="number">
/// <item>The bot's own limits come first. Daily risk budget (RSK-006): a trade opens only if
/// M - (balance + every open trade and the new one at its stop) is at most DailyBudgetPercent of
/// InitialCapital; after a refusal no trade opens until the next CE(S)T day. Kill switch
/// (RSK-008): when a close leaves the balance KillSwitchPercent or more below its peak, the bot
/// halts and opens nothing more; open trades still run to their close.</item>
/// <item>FTMO daily floor: the intraday worst case of a day is M plus, for every trade open at
/// any time that day, its result at the stop, or its actual result if it closed that day below
/// its stop. Equity must stay above M - DailyLossPercent of InitialCapital.</item>
/// <item>FTMO static floor: the same worst case must stay above InitialCapital minus
/// MaxLossPercent of it.</item>
/// <item>Pass: after a close with no trade open, the balance is at least InitialCapital plus
/// ProfitTargetPercent of it and the bot has opened trades on at least MinTradingDays CE(S)T
/// days.</item>
/// </list>
/// <para>Equity exactly at a floor is a breach: spec 15.2 and 15.4 say it must stay above.
/// Days to pass count CE(S)T calendar days, the start day being day 1.</para>
/// </remarks>
public static class FtmoChallengeSimulator
{
    /// <summary>
    /// Replays one path from <paramref name="startDayUtc"/> (SIM-004). Trades that open before
    /// the start are ignored.
    /// </summary>
    /// <param name="trades">The combined trade list, in any order.</param>
    /// <param name="startDayUtc">00:00 CE(S)T of the start day, as a UTC instant.</param>
    /// <param name="parameters">The rules and the risk per trade.</param>
    /// <returns>How the path ended.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="startDayUtc"/> is not the start of a CE(S)T day, or a trade is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    internal static ChallengePath RunPath(IReadOnlyList<TradeRecord> trades, DateTime startDayUtc, FtmoChallengeParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        if (startDayUtc.Kind != DateTimeKind.Utc || CestClock.StartOfCestDayUtc(startDayUtc) != startDayUtc)
        {
            throw new ArgumentException("The start must be 00:00 CE(S)T of a day, given in UTC.", nameof(startDayUtc));
        }

        TradeRecord[] sorted = Sorted(trades);
        int first = 0;
        while (first < sorted.Length && sorted[first].OpenTimeUtc < startDayUtc)
        {
            first++;
        }

        return new PathRun(sorted, first, startDayUtc, parameters).Run();
    }

    /// <summary>
    /// Runs <see cref="FtmoChallengeParameters.Runs"/> paths, each from a start day drawn
    /// uniformly from the CE(S)T days on which a trade opens (SIM-004).
    /// </summary>
    /// <param name="trades">The combined trade list, at least one trade, in any order.</param>
    /// <param name="parameters">The rules, the risk per trade and the number of runs.</param>
    /// <param name="seed">Seed of the generator that draws the start days.</param>
    /// <returns>Counts, probabilities and days to pass.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">There is no trade, or a trade is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public static ChallengeSummary Simulate(IReadOnlyList<TradeRecord> trades, FtmoChallengeParameters parameters, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(trades);
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        if (trades.Count == 0)
        {
            throw new ArgumentException("The challenge simulation needs at least one trade.", nameof(trades));
        }

        TradeRecord[] sorted = Sorted(trades);
        var startDays = new List<DateTime>();
        var firstIndex = new List<int>();
        for (int i = 0; i < sorted.Length; i++)
        {
            DateTime day = CestClock.StartOfCestDayUtc(sorted[i].OpenTimeUtc);
            if (startDays.Count == 0 || startDays[startDays.Count - 1] != day)
            {
                startDays.Add(day);
                firstIndex.Add(i);
            }
        }

        var generator = new Xoshiro256StarStar(seed);
        var paths = new List<ChallengePath>(parameters.Runs);
        for (int run = 0; run < parameters.Runs; run++)
        {
            int pick = generator.NextInt(startDays.Count);
            paths.Add(new PathRun(sorted, firstIndex[pick], startDays[pick], parameters).Run());
        }

        return new ChallengeSummary(parameters, seed, startDays.Count, paths);
    }

    /// <summary>
    /// Runs <see cref="Simulate"/> for the Challenge (10%) and Verification (5%) targets at 0.5%,
    /// 0.75% and 1.0% risk, with the same seed in every cell (SIM-004, spec 15.5). The daily risk
    /// budget scales with the risk: DailyBudgetPercent x risk / RiskPercent of
    /// <paramref name="parameters"/> (spec 15.5); every other value is taken from it.
    /// </summary>
    /// <param name="trades">The combined trade list.</param>
    /// <param name="parameters">The base parameters; RiskPercent is the risk at which DailyBudgetPercent applies.</param>
    /// <param name="seed">Seed used for every cell.</param>
    /// <returns>Six summaries: Challenge at each risk, then Verification at each risk.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">There is no trade.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public static IReadOnlyList<ChallengeSummary> Grid(IReadOnlyList<TradeRecord> trades, FtmoChallengeParameters parameters, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        parameters.Validate();
        // Targets of the Challenge and Verification phases (spec 15.2) and the risks of spec 12.1 step 9.
        double[] targets = { 10.0, 5.0 };
        double[] risks = { 0.5, 0.75, 1.0 };
        var grid = new List<ChallengeSummary>(targets.Length * risks.Length);
        foreach (double target in targets)
        {
            foreach (double risk in risks)
            {
                FtmoChallengeParameters cell = parameters with
                {
                    ProfitTargetPercent = target,
                    RiskPercent = risk,
                    DailyBudgetPercent = parameters.DailyBudgetPercent * risk / parameters.RiskPercent,
                };
                grid.Add(Simulate(trades, cell, seed));
            }
        }

        return grid;
    }

    /// <summary>The summaries as Markdown, with the assumptions first (SIM-004).</summary>
    /// <param name="summaries">Summaries, for example from <see cref="Grid"/>.</param>
    /// <param name="seed">The seed, printed in the report.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="summaries"/> is null.</exception>
    public static string ToMarkdown(IReadOnlyList<ChallengeSummary> summaries, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        var text = new StringBuilder();
        text.Append("# FTMO 2-Step challenge simulation (SIM-004)\n\n");
        text.Append("Assumptions:\n\n");
        text.Append("- The trades are a sample of the past: past trade distributions may not repeat.\n");
        text.Append("- Each run starts at 00:00 CE(S)T of a trading day drawn at random and replays the later trades of the combined list in time order (a contiguous block) until a pass, a breach or the end of the data.\n");
        text.Append("- Each trade risks the chosen percentage of the balance at its open; its result is its logged result in R times that risk, commission included.\n");
        text.Append("- Intraday worst case: every trade open during a CE(S)T day counts at its stop (plus commission), or at its actual loss if worse. Equity at a floor counts as a breach. The real intraday path is not known.\n");
        text.Append("- The bot's daily risk budget (scaled with the risk per trade) and kill switch act before the FTMO floors; the emergency guard and the entry caps of the combined list are not replayed here.\n");
        text.Append("- Pass needs the target with no trade open and the minimum number of trading days (days on which the bot opened a trade). Days to pass are CE(S)T calendar days from the start day.\n\n");
        text.Append("Seed: ").Append(SimText.Seed(seed)).Append(".\n\n");
        text.Append("| Phase | Risk | Daily budget | Pass | Breach (daily / static) | Neither (halted / data ended) | Median days to pass | P90 days to pass |\n");
        text.Append("|---|---|---|---|---|---|---|---|\n");
        foreach (ChallengeSummary summary in summaries)
        {
            AppendRow(text, summary);
        }

        return text.ToString();
    }

    private static void AppendRow(StringBuilder text, ChallengeSummary s)
    {
        double runs = s.Runs;
        text.Append("| ").Append(PhaseName(s.Parameters.ProfitTargetPercent))
            .Append(" | ").Append(SimText.Short(s.Parameters.RiskPercent)).Append('%')
            .Append(" | ").Append(SimText.Short(s.Parameters.DailyBudgetPercent)).Append('%')
            .Append(" | ").Append(SimText.Percent(s.PassProbability))
            .Append(" | ").Append(SimText.Percent(s.BreachProbability))
            .Append(" (").Append(SimText.Percent(s.DailyFloorBreaches / runs)).Append(" / ").Append(SimText.Percent(s.StaticFloorBreaches / runs)).Append(')')
            .Append(" | ").Append(SimText.Percent(s.NeitherProbability))
            .Append(" (").Append(SimText.Percent(s.Halted / runs)).Append(" / ").Append(SimText.Percent(s.NotDecided / runs)).Append(')')
            .Append(" | ").Append(s.MedianDaysToPass.HasValue ? SimText.Int(s.MedianDaysToPass.Value) : "n/a")
            .Append(" | ").Append(s.P90DaysToPass.HasValue ? SimText.Int(s.P90DaysToPass.Value) : "n/a")
            .Append(" |\n");
    }

    private static string PhaseName(double targetPercent)
    {
        if (PriceMath.AlmostEqual(targetPercent, 10.0, PriceMath.DefaultTolerance))
        {
            return "Challenge";
        }

        if (PriceMath.AlmostEqual(targetPercent, 5.0, PriceMath.DefaultTolerance))
        {
            return "Verification";
        }

        return "Target " + SimText.Short(targetPercent) + "%";
    }

    private static TradeRecord[] Sorted(IReadOnlyList<TradeRecord> trades)
    {
        var indexed = new List<KeyValuePair<int, TradeRecord>>(trades.Count);
        for (int i = 0; i < trades.Count; i++)
        {
            TradeRecord? trade = trades[i];
            if (trade == null)
            {
                throw new ArgumentException("Trade " + SimText.Int(i + 1) + " is null.", nameof(trades));
            }

            indexed.Add(new KeyValuePair<int, TradeRecord>(i, trade));
        }

        // Open time, then symbol (ordinal), then input position: independent of the sort algorithm.
        indexed.Sort((a, b) =>
        {
            int result = a.Value.OpenTimeUtc.CompareTo(b.Value.OpenTimeUtc);
            if (result == 0)
            {
                result = string.CompareOrdinal(a.Value.Symbol, b.Value.Symbol);
            }

            return result != 0 ? result : a.Key.CompareTo(b.Key);
        });

        var sorted = new TradeRecord[indexed.Count];
        for (int i = 0; i < sorted.Length; i++)
        {
            sorted[i] = indexed[i].Value;
        }

        return sorted;
    }

    /// <summary>The state of one path while it is replayed.</summary>
    private sealed class PathRun
    {
        private readonly TradeRecord[] _trades;
        private readonly int _first;
        private readonly DateTime _startDay;
        private readonly FtmoChallengeParameters _p;
        private readonly List<OpenTrade> _open = new();
        private double _balance;
        private double _peak;
        private DateTime _day;
        private double _dayStartBalance;
        private double _dayWorst;
        private bool _dayBlocked;
        private bool _halted;
        private DateTime? _lastTradingDay;
        private int _tradingDays;
        private int _taken;
        private int _refused;
        private int _sequence;
        private DateTime _lastEvent;
        private ChallengeOutcome? _outcome;

        internal PathRun(TradeRecord[] trades, int first, DateTime startDay, FtmoChallengeParameters parameters)
        {
            _trades = trades;
            _first = first;
            _startDay = startDay;
            _p = parameters;
            _balance = parameters.InitialCapital;
            _peak = _balance;
            _day = startDay;
            _dayStartBalance = _balance;
            _dayWorst = _balance;
            _lastEvent = startDay;
        }

        internal ChallengePath Run()
        {
            for (int i = _first; i < _trades.Length && _outcome == null; i++)
            {
                if (_halted && _open.Count == 0)
                {
                    break;
                }

                CloseUpTo(_trades[i].OpenTimeUtc);
                if (_outcome == null)
                {
                    RollTo(_trades[i].OpenTimeUtc);
                }

                if (_outcome == null)
                {
                    Open(_trades[i]);
                }
            }

            CloseUpTo(DateTime.MaxValue);
            ChallengeOutcome outcome = _outcome ?? (_halted ? ChallengeOutcome.Halted : ChallengeOutcome.NotDecided);
            return new ChallengePath(outcome, DayNumber(_day), _tradingDays, _taken, _refused, _balance, _lastEvent);
        }

        private void CloseUpTo(DateTime timeUtc)
        {
            while (_outcome == null)
            {
                int next = -1;
                for (int i = 0; i < _open.Count; i++)
                {
                    OpenTrade candidate = _open[i];
                    if (candidate.Trade.CloseTimeUtc <= timeUtc && (next < 0 || Earlier(candidate, _open[next])))
                    {
                        next = i;
                    }
                }

                if (next < 0)
                {
                    return;
                }

                // Roll the day before removing the trade: it was open during the new day too.
                OpenTrade closing = _open[next];
                RollTo(closing.Trade.CloseTimeUtc);
                _open.RemoveAt(next);
                if (_outcome == null)
                {
                    Close(closing);
                }
            }
        }

        private static bool Earlier(OpenTrade a, OpenTrade b)
        {
            int result = a.Trade.CloseTimeUtc.CompareTo(b.Trade.CloseTimeUtc);
            return result < 0 || (result == 0 && a.Sequence < b.Sequence);
        }

        private void RollTo(DateTime timeUtc)
        {
            _lastEvent = timeUtc;
            DateTime day = CestClock.StartOfCestDayUtc(timeUtc);
            if (day <= _day)
            {
                return;
            }

            _day = day;
            _dayStartBalance = _balance;
            _dayBlocked = false;
            _dayWorst = _balance;
            foreach (OpenTrade open in _open)
            {
                _dayWorst += open.AtStop;
            }

            CheckFloors();
        }

        private void Open(TradeRecord trade)
        {
            if (_halted || _dayBlocked)
            {
                _refused += _dayBlocked && !_halted ? 1 : 0;
                return;
            }

            double risk = _balance * _p.RiskPercent / 100.0;
            double atStop = (-1.0 + (trade.Commission / trade.RiskMoney)) * risk;
            double worst = _balance + atStop;
            foreach (OpenTrade open in _open)
            {
                worst += open.AtStop;
            }

            double budget = _p.InitialCapital * _p.DailyBudgetPercent / 100.0;
            if (PriceMath.GreaterThan(_dayStartBalance - worst, budget, PriceMath.DefaultTolerance))
            {
                _dayBlocked = true;
                _refused++;
                return;
            }

            _open.Add(new OpenTrade(trade, trade.ResultR * risk, atStop, _sequence++));
            _taken++;
            if (_lastTradingDay != _day)
            {
                _lastTradingDay = _day;
                _tradingDays++;
            }

            _dayWorst += atStop;
            CheckFloors();
        }

        private void Close(OpenTrade closing)
        {
            _balance += closing.Result;
            if (closing.Result < closing.AtStop)
            {
                // Closed below its stop (a gap or slippage): the day's worst case is the actual loss.
                _dayWorst += closing.Result - closing.AtStop;
            }

            CheckFloors();
            if (_outcome != null)
            {
                return;
            }

            _peak = Math.Max(_peak, _balance);
            double drawdownPercent = (_peak - _balance) / _peak * 100.0;
            if (!PriceMath.LessThan(drawdownPercent, _p.KillSwitchPercent, PriceMath.DefaultTolerance))
            {
                _halted = true;
            }

            double target = _p.InitialCapital + (_p.InitialCapital * _p.ProfitTargetPercent / 100.0);
            if (_open.Count == 0 && _tradingDays >= _p.MinTradingDays && !PriceMath.LessThan(_balance, target, PriceMath.DefaultTolerance))
            {
                _outcome = ChallengeOutcome.Passed;
            }
        }

        private void CheckFloors()
        {
            double dailyFloor = _dayStartBalance - (_p.InitialCapital * _p.DailyLossPercent / 100.0);
            double staticFloor = _p.InitialCapital - (_p.InitialCapital * _p.MaxLossPercent / 100.0);
            if (!PriceMath.GreaterThan(_dayWorst, dailyFloor, PriceMath.DefaultTolerance))
            {
                _outcome = ChallengeOutcome.DailyFloorBreached;
            }
            else if (!PriceMath.GreaterThan(_dayWorst, staticFloor, PriceMath.DefaultTolerance))
            {
                _outcome = ChallengeOutcome.StaticFloorBreached;
            }
        }

        private int DayNumber(DateTime dayStartUtc)
        {
            return (LocalDate(dayStartUtc) - LocalDate(_startDay)).Days + 1;
        }

        private static DateTime LocalDate(DateTime dayStartUtc)
        {
            return (dayStartUtc + CestClock.UtcOffset(dayStartUtc)).Date;
        }

        private sealed record OpenTrade(TradeRecord Trade, double Result, double AtStop, int Sequence);
    }
}

/// <summary>How one path of the challenge simulation ended (SIM-004).</summary>
public enum ChallengeOutcome
{
    /// <summary>The profit target was reached with no trade open and enough trading days.</summary>
    Passed,

    /// <summary>The intraday worst case reached the daily floor (M minus the daily loss limit).</summary>
    DailyFloorBreached,

    /// <summary>The intraday worst case reached the static floor (initial capital minus the maximum loss).</summary>
    StaticFloorBreached,

    /// <summary>The kill switch halted the bot and the data ended without a pass or a breach.</summary>
    Halted,

    /// <summary>The data ended without a pass, a breach or a halt.</summary>
    NotDecided,
}

/// <summary>One replayed path of the challenge simulation (SIM-004).</summary>
/// <param name="Outcome">How the path ended.</param>
/// <param name="CalendarDays">CE(S)T calendar days from the start day (day 1) to the day of the last event processed.</param>
/// <param name="TradingDays">CE(S)T days on which the bot opened at least one trade.</param>
/// <param name="TradesTaken">Trades the bot opened.</param>
/// <param name="TradesRefusedByBudget">Trades the daily risk budget refused, including those of a blocked day.</param>
/// <param name="FinalBalance">Balance after the last close processed.</param>
/// <param name="LastEventUtc">Time of the last event processed: the decision, or the last close.</param>
public sealed record ChallengePath(ChallengeOutcome Outcome, int CalendarDays, int TradingDays, int TradesTaken, int TradesRefusedByBudget, double FinalBalance, DateTime LastEventUtc);

/// <summary>
/// Parameters of the challenge simulation (SIM-004), with the 2-Step values of spec 15.2 and the
/// bot limits of spec 15.4 as defaults. Immutable; derive changes with <c>with</c>.
/// </summary>
public sealed record FtmoChallengeParameters
{
    /// <summary>Initial capital (spec 15.2). Default 100,000.</summary>
    public double InitialCapital { get; init; } = 100000.0;

    /// <summary>Risk per trade in percent of the balance at the open (RSK-001, spec 15.5). Default 0.5.</summary>
    public double RiskPercent { get; init; } = 0.5;

    /// <summary>Profit target in percent of initial capital: 10 for the Challenge, 5 for Verification (spec 15.2). Default 10.</summary>
    public double ProfitTargetPercent { get; init; } = 10.0;

    /// <summary>FTMO maximum daily loss in percent of initial capital (spec 15.2). Default 5.</summary>
    public double DailyLossPercent { get; init; } = 5.0;

    /// <summary>FTMO maximum loss in percent of initial capital, fixed (spec 15.2). Default 10.</summary>
    public double MaxLossPercent { get; init; } = 10.0;

    /// <summary>Minimum trading days per phase (spec 15.2). Default 4.</summary>
    public int MinTradingDays { get; init; } = 4;

    /// <summary>The bot's daily risk budget in percent of initial capital at <see cref="RiskPercent"/> (RSK-006, spec 15.4). Default 1.5.</summary>
    public double DailyBudgetPercent { get; init; } = 1.5;

    /// <summary>The bot's drawdown kill switch in percent of the peak balance (RSK-008, spec 15.4). Default 8.</summary>
    public double KillSwitchPercent { get; init; } = 8.0;

    /// <summary>Number of runs of <see cref="FtmoChallengeSimulator.Simulate"/> ("several thousand", spec 12.1 step 9). Default 5,000.</summary>
    public int Runs { get; init; } = 5000;

    /// <summary>Checks every value (SIM-004).</summary>
    /// <exception cref="ArgumentOutOfRangeException">A value is out of range; the parameter name is the property name.</exception>
    public void Validate()
    {
        if (!double.IsFinite(InitialCapital) || InitialCapital <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(InitialCapital), "InitialCapital must be a finite number greater than zero.");
        }

        RequirePercent(RiskPercent, nameof(RiskPercent));
        RequirePercent(ProfitTargetPercent, nameof(ProfitTargetPercent));
        RequirePercent(DailyLossPercent, nameof(DailyLossPercent));
        RequirePercent(MaxLossPercent, nameof(MaxLossPercent));
        if (MinTradingDays < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinTradingDays), "MinTradingDays must not be negative.");
        }

        RequirePercent(DailyBudgetPercent, nameof(DailyBudgetPercent));
        RequirePercent(KillSwitchPercent, nameof(KillSwitchPercent));
        if (Runs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Runs), "Runs must be at least 1.");
        }
    }

    private static void RequirePercent(double value, string name)
    {
        if (!double.IsFinite(value) || value <= 0.0 || value > 100.0)
        {
            throw new ArgumentOutOfRangeException(name, name + " must be greater than 0 and at most 100.");
        }
    }
}

/// <summary>Counts, probabilities and days to pass of <see cref="FtmoChallengeSimulator.Simulate"/> (SIM-004).</summary>
public sealed class ChallengeSummary
{
    internal ChallengeSummary(FtmoChallengeParameters parameters, ulong seed, int startDays, IReadOnlyList<ChallengePath> paths)
    {
        Parameters = parameters;
        Seed = seed;
        StartDays = startDays;
        Runs = paths.Count;
        var daysToPass = new List<double>();
        foreach (ChallengePath path in paths)
        {
            switch (path.Outcome)
            {
                case ChallengeOutcome.Passed:
                    Passed++;
                    daysToPass.Add(path.CalendarDays);
                    break;
                case ChallengeOutcome.DailyFloorBreached:
                    DailyFloorBreaches++;
                    break;
                case ChallengeOutcome.StaticFloorBreached:
                    StaticFloorBreaches++;
                    break;
                case ChallengeOutcome.Halted:
                    Halted++;
                    break;
                default:
                    NotDecided++;
                    break;
            }
        }

        if (daysToPass.Count > 0)
        {
            MedianDaysToPass = (int)NearestRank.Percentile(daysToPass, 50);
            P90DaysToPass = (int)NearestRank.Percentile(daysToPass, 90);
        }
    }

    /// <summary>The parameters used (SIM-004).</summary>
    public FtmoChallengeParameters Parameters { get; }

    /// <summary>The seed used (SIM-004).</summary>
    public ulong Seed { get; }

    /// <summary>Number of distinct start days available in the data (SIM-004).</summary>
    public int StartDays { get; }

    /// <summary>Number of runs (SIM-004).</summary>
    public int Runs { get; }

    /// <summary>Runs that passed (SIM-004).</summary>
    public int Passed { get; }

    /// <summary>Runs that breached the daily floor (SIM-004).</summary>
    public int DailyFloorBreaches { get; }

    /// <summary>Runs that breached the static floor (SIM-004).</summary>
    public int StaticFloorBreaches { get; }

    /// <summary>Runs in which the kill switch halted the bot without a pass or breach (SIM-004).</summary>
    public int Halted { get; }

    /// <summary>Runs in which the data ended without a pass, breach or halt (SIM-004).</summary>
    public int NotDecided { get; }

    /// <summary>Share of runs that passed (SIM-004).</summary>
    public double PassProbability => (double)Passed / Runs;

    /// <summary>Share of runs that breached either floor (SIM-004).</summary>
    public double BreachProbability => (double)(DailyFloorBreaches + StaticFloorBreaches) / Runs;

    /// <summary>Share of runs with neither a pass nor a breach within the data, halts included (SIM-004).</summary>
    public double NeitherProbability => (double)(Halted + NotDecided) / Runs;

    /// <summary>Median CE(S)T calendar days to pass over the passing runs, by nearest rank; null when none passed (SIM-004).</summary>
    public int? MedianDaysToPass { get; }

    /// <summary>90th percentile of the calendar days to pass, by nearest rank; null when none passed (SIM-004).</summary>
    public int? P90DaysToPass { get; }
}
