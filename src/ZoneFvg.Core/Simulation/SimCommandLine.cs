using System;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.Simulation;

/// <summary>
/// The commands of the <c>sim</c> console tool (SIM-002 to SIM-005): <c>combine</c>,
/// <c>montecarlo</c>, <c>ftmo</c> and <c>accept</c>, each with <c>--help</c>. Arguments are
/// parsed here, so the console program only reads and writes files. Every command that uses
/// randomness requires <c>--seed</c>, and the same arguments and files always give the same
/// output.
/// </summary>
/// <remarks>
/// Every command first combines the trade logs it is given with <see cref="PortfolioCombiner"/>
/// and the default caps. Combining a list that is already combined changes nothing, because each
/// kept trade was checked against exactly the trades that are kept again.
/// </remarks>
public static class SimCommandLine
{
    /// <summary>Exit code of a successful command (SIM-002 to SIM-005).</summary>
    public const int ExitOk = 0;

    /// <summary>Exit code for invalid arguments or an invalid trade log (SIM-002 to SIM-005).</summary>
    public const int ExitInvalid = 1;

    /// <summary>Exit code when a file cannot be read (SIM-002 to SIM-005).</summary>
    public const int ExitUnreadable = 2;

    private const string GeneralHelp =
        "Usage: sim <command> [options]\n\n"
        + "Commands:\n"
        + "  combine     Merge trade logs and replay the account caps (SIM-002)\n"
        + "  montecarlo  Reshuffle the trade order: drawdown and 30-trade average R (SIM-003)\n"
        + "  ftmo        FTMO 2-Step challenge simulation (SIM-004)\n"
        + "  accept      Acceptance report against spec 13.1 (SIM-005)\n\n"
        + "Run \"sim <command> --help\" for the options of a command.\n";

    /// <summary>Runs one command (SIM-002 to SIM-005).</summary>
    /// <param name="args">The command line arguments, the command first.</param>
    /// <param name="readFile">Reads a whole text file; may throw when the file cannot be read.</param>
    /// <returns>Exit code, standard output, standard error and the files to write.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static SimCommandResult Run(IReadOnlyList<string> args, Func<string, string> readFile)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(readFile);
        if (args.Count == 0 || args[0] == "--help" || args[0] == "help")
        {
            return SimCommandResult.Ok(GeneralHelp, Array.Empty<SimOutputFile>());
        }

        CommandSpec? spec = CommandSpec.Find(args[0]);
        if (spec == null)
        {
            return SimCommandResult.Failed(ExitInvalid, "Unknown command \"" + args[0] + "\".\n\n" + GeneralHelp);
        }

        for (int i = 1; i < args.Count; i++)
        {
            if (args[i] == "--help")
            {
                return SimCommandResult.Ok(spec.Help, Array.Empty<SimOutputFile>());
            }
        }

        try
        {
            Options options = Options.Parse(args, spec);
            var context = new CommandContext(options, readFile);
            return spec.Name switch
            {
                "combine" => Combine(context),
                "montecarlo" => MonteCarloCommand(context),
                "ftmo" => Ftmo(context),
                _ => Accept(context),
            };
        }
        catch (SimInputException exception)
        {
            string help = exception.ExitCode == ExitInvalid ? "\n\n" + spec.Help : "\n";
            return SimCommandResult.Failed(exception.ExitCode, exception.Message + help);
        }
        catch (ArgumentException exception)
        {
            // A parameter out of range, reported by the Validate method of the parameters.
            return SimCommandResult.Failed(ExitInvalid, exception.Message + "\n\n" + spec.Help);
        }
    }

    private static SimCommandResult Combine(CommandContext c)
    {
        PortfolioResult portfolio = c.Combined("--log");
        var files = new List<SimOutputFile>();
        string? output = c.Options.Single("--out");
        if (output != null)
        {
            files.Add(new SimOutputFile(output, TradeLogCsv.Write(portfolio.Trades)));
        }

        string? equity = c.Options.Single("--equity");
        if (equity != null)
        {
            files.Add(new SimOutputFile(equity, portfolio.ToEquityCsv()));
        }

        return SimCommandResult.Ok(portfolio.ToMarkdown(), files);
    }

    private static SimCommandResult MonteCarloCommand(CommandContext c)
    {
        PortfolioResult portfolio = c.Combined("--log");
        var parameters = new MonteCarloParameters
        {
            Sequences = c.Options.Int("--sequences", 1000),
            RiskPercent = c.Options.Number("--risk", 0.5),
            WindowTrades = c.Options.Int("--window", 30),
        };
        var results = new double[portfolio.Trades.Count];
        for (int i = 0; i < results.Length; i++)
        {
            results[i] = portfolio.Trades[i].ResultR;
        }

        MonteCarloResult result = MonteCarlo.Run(results, parameters, c.Options.Seed());
        return SimCommandResult.Ok(result.ToMarkdown(), Array.Empty<SimOutputFile>());
    }

    private static SimCommandResult Ftmo(CommandContext c)
    {
        PortfolioResult portfolio = c.Combined("--log");
        ulong seed = c.Options.Seed();
        FtmoChallengeParameters parameters = FtmoParameters(c.Options);
        IReadOnlyList<ChallengeSummary> grid = parameters.Ladder == null
            ? FtmoChallengeSimulator.Grid(portfolio.Trades, parameters, seed)
            : new[]
            {
                // RSK-013: the ladder sets the risk of each trade, so only the two phases are run.
                FtmoChallengeSimulator.Simulate(portfolio.Trades, parameters with { ProfitTargetPercent = 10.0 }, seed),
                FtmoChallengeSimulator.Simulate(portfolio.Trades, parameters with { ProfitTargetPercent = 5.0 }, seed),
            };
        return SimCommandResult.Ok(FtmoChallengeSimulator.ToMarkdown(grid, seed), Array.Empty<SimOutputFile>());
    }

    private static SimCommandResult Accept(CommandContext c)
    {
        ulong seed = c.Options.Seed();
        PortfolioResult development = c.Combined("--dev");
        ChallengeSummary challenge = FtmoChallengeSimulator.Simulate(development.Trades, FtmoParameters(c.Options), seed);
        var inputs = new AcceptanceInputs(development.Trades)
        {
            Holdout = c.CombinedOrNull("--holdout"),
            DevelopmentCostStress = c.CombinedOrNull("--stress"),
            DevelopmentBenchmark = c.CombinedOrNull("--benchmark"),
            HoldoutBenchmark = c.CombinedOrNull("--benchmark-holdout"),
            ChallengePassProbability = challenge.PassProbability,
            ChallengeBreachProbability = challenge.BreachProbability,
            InitialCapital = development.Parameters.InitialCapital,
        };

        string report = AcceptanceReport.ToMarkdown(AcceptanceReport.Evaluate(inputs))
            + "\nChallenge simulation used for the two challenge rows:\n\n"
            + FtmoChallengeSimulator.ToMarkdown(new[] { challenge }, seed);
        return SimCommandResult.Ok(report, Array.Empty<SimOutputFile>());
    }

    private static FtmoChallengeParameters FtmoParameters(Options options)
    {
        return new FtmoChallengeParameters
        {
            InitialCapital = options.Number("--initial-capital", 100000.0),
            RiskPercent = options.Number("--risk", 0.5),
            DailyLossPercent = options.Number("--daily-loss", 5.0),
            MaxLossPercent = options.Number("--max-loss", 10.0),
            MinTradingDays = options.Int("--min-days", 4),
            DailyBudgetPercent = options.Number("--daily-budget", 1.5),
            KillSwitchPercent = options.Number("--kill-switch", 8.0),
            Runs = options.Int("--runs", 5000),
            Ladder = Sizing(options),
        };
    }

    private static RiskParameters? Sizing(Options options)
    {
        // RSK-013: "ladder" sizes every trade with LadderSizer and its default values; "fixed" (the default) uses --risk.
        string sizing = options.Single("--sizing") ?? "fixed";
        switch (sizing)
        {
            case "fixed":
                return null;
            case "ladder":
                return new RiskParameters();
            default:
                throw new SimInputException(ExitInvalid, "Option --sizing needs fixed or ladder, but was \"" + sizing + "\".");
        }
    }

    /// <summary>The options and the file reader of one command.</summary>
    private sealed class CommandContext
    {
        private readonly Func<string, string> _readFile;

        internal CommandContext(Options options, Func<string, string> readFile)
        {
            Options = options;
            _readFile = readFile;
        }

        internal Options Options { get; }

        internal PortfolioResult Combined(string option)
        {
            IReadOnlyList<string> paths = Options.All(option);
            if (paths.Count == 0)
            {
                throw new SimInputException(ExitInvalid, "At least one " + option + " file is required.");
            }

            var logs = new List<IReadOnlyList<TradeRecord>>(paths.Count);
            foreach (string path in paths)
            {
                logs.Add(ReadLog(path));
            }

            var parameters = new PortfolioParameters
            {
                InitialCapital = Options.Number("--initial-capital", 100000.0),
                MaxOpenRiskPercent = Options.Number("--max-open-risk", 1.5),
                MaxPositionsPerCurrency = Options.Int("--max-per-currency", 2),
                DailyLossLimitPercent = Options.Number("--daily-budget", 1.5),
            };
            return PortfolioCombiner.Combine(logs, parameters);
        }

        internal IReadOnlyList<TradeRecord>? CombinedOrNull(string option)
        {
            return Options.All(option).Count == 0 ? null : Combined(option).Trades;
        }

        private IReadOnlyList<TradeRecord> ReadLog(string path)
        {
            string text;
            try
            {
                text = _readFile(path);
            }
            catch (Exception exception)
            {
                // Reported to the user with the reason; nothing is computed from a file that cannot be read.
                throw new SimInputException(ExitUnreadable, "Cannot read \"" + path + "\": " + exception.Message);
            }

            TradeLogReadResult result = TradeLogCsv.Read(text);
            if (!result.IsValid)
            {
                throw new SimInputException(ExitInvalid, "Invalid trade log \"" + path + "\": " + result.Error);
            }

            return result.Records;
        }
    }

    /// <summary>Parsed "--name value" pairs of one command.</summary>
    private sealed class Options
    {
        private readonly SortedDictionary<string, List<string>> _values = new(StringComparer.Ordinal);

        internal static Options Parse(IReadOnlyList<string> args, CommandSpec spec)
        {
            var options = new Options();
            for (int i = 1; i < args.Count; i += 2)
            {
                string name = args[i];
                if (!spec.Allows(name))
                {
                    throw new SimInputException(ExitInvalid, "Unknown option \"" + name + "\" for " + spec.Name + ".");
                }

                if (i + 1 >= args.Count)
                {
                    throw new SimInputException(ExitInvalid, "Option " + name + " needs a value.");
                }

                if (!options._values.TryGetValue(name, out List<string>? values))
                {
                    values = new List<string>();
                    options._values[name] = values;
                }
                else if (!spec.Repeats(name))
                {
                    throw new SimInputException(ExitInvalid, "Option " + name + " may be given only once.");
                }

                values.Add(args[i + 1]);
            }

            return options;
        }

        internal IReadOnlyList<string> All(string name)
        {
            return _values.TryGetValue(name, out List<string>? values) ? values : Array.Empty<string>();
        }

        internal string? Single(string name)
        {
            IReadOnlyList<string> values = All(name);
            return values.Count == 0 ? null : values[0];
        }

        internal double Number(string name, double fallback)
        {
            string? text = Single(name);
            if (text == null)
            {
                return fallback;
            }

            const NumberStyles style = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;
            if (double.TryParse(text, style, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
            {
                return value;
            }

            throw new SimInputException(ExitInvalid, "Option " + name + " needs a number, but was \"" + text + "\".");
        }

        internal int Int(string name, int fallback)
        {
            string? text = Single(name);
            if (text == null)
            {
                return fallback;
            }

            if (int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value))
            {
                return value;
            }

            throw new SimInputException(ExitInvalid, "Option " + name + " needs a whole number, but was \"" + text + "\".");
        }

        internal ulong Seed()
        {
            string? text = Single("--seed");
            if (text == null)
            {
                throw new SimInputException(ExitInvalid, "Option --seed is required, so that the result can be reproduced.");
            }

            if (ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out ulong seed))
            {
                return seed;
            }

            throw new SimInputException(ExitInvalid, "Option --seed needs a whole number from 0 to 18446744073709551615, but was \"" + text + "\".");
        }
    }

    /// <summary>Name, options and help text of one command.</summary>
    private sealed class CommandSpec
    {
        private readonly string[] _single;
        private readonly string[] _repeated;

        private CommandSpec(string name, string[] repeated, string[] single, string help)
        {
            Name = name;
            _repeated = repeated;
            _single = single;
            Help = help;
        }

        internal string Name { get; }

        internal string Help { get; }

        internal static CommandSpec? Find(string name)
        {
            const string caps = "  --initial-capital <n>   Initial capital (default 100000)\n"
                + "  --max-open-risk <pct>   Open risk cap, RSK-004 (default 1.5)\n"
                + "  --max-per-currency <n>  Same-side trades per currency, RSK-005 (default 2)\n"
                + "  --daily-budget <pct>    Daily risk budget, RSK-006 (default 1.5)\n";
            const string ftmo = "  --risk <pct>            Risk per trade at which --daily-budget applies (default 0.5)\n"
                + "  --daily-loss <pct>      FTMO maximum daily loss (default 5)\n"
                + "  --max-loss <pct>        FTMO maximum loss (default 10)\n"
                + "  --min-days <n>          Minimum trading days (default 4)\n"
                + "  --kill-switch <pct>     Bot drawdown kill switch (default 8)\n"
                + "  --runs <n>              Runs per cell (default 5000)\n"
                + "  --sizing <fixed|ladder> fixed: every trade risks --risk; ladder: RSK-013 ladder sizing, Challenge and Verification only (default fixed)\n"
                + "  --seed <n>              Seed of the generator (required)\n";
            string[] capOptions = { "--initial-capital", "--max-open-risk", "--max-per-currency", "--daily-budget" };
            string[] ftmoOptions = { "--initial-capital", "--max-open-risk", "--max-per-currency", "--daily-budget", "--risk", "--daily-loss", "--max-loss", "--min-days", "--kill-switch", "--runs", "--seed", "--sizing" };
            switch (name)
            {
                case "combine":
                    return new CommandSpec(name, new[] { "--log" }, Concat(capOptions, "--out", "--equity"), "Usage: sim combine --log <file> [--log <file> ...] [--out <file>] [--equity <file>] [options]\n\n"
                        + "Merges trade logs by time, replays the caps of RSK-004 to RSK-006 and prints the report (SIM-002).\n\n"
                        + "  --log <file>            Trade log (SIM-001); repeat for each symbol\n"
                        + "  --out <file>            Write the kept trades as a trade log\n"
                        + "  --equity <file>         Write the equity curve as CSV\n" + caps);
                case "montecarlo":
                    return new CommandSpec(name, new[] { "--log" }, Concat(capOptions, "--seed", "--sequences", "--risk", "--window"), "Usage: sim montecarlo --log <file> [--log <file> ...] --seed <n> [options]\n\n"
                        + "Reshuffles the combined trade order (SIM-003).\n\n"
                        + "  --log <file>            Trade log (SIM-001); repeat for each symbol\n"
                        + "  --seed <n>              Seed of the generator (required)\n"
                        + "  --sequences <n>         Number of sequences (default 1000)\n"
                        + "  --risk <pct>            Risk per trade for the drawdown in percent (default 0.5)\n"
                        + "  --window <n>            Trades in the rolling window (default 30)\n" + caps);
                case "ftmo":
                    return new CommandSpec(name, new[] { "--log" }, ftmoOptions, "Usage: sim ftmo --log <file> [--log <file> ...] --seed <n> [options]\n\n"
                        + "FTMO 2-Step simulation for the Challenge and Verification at 0.5%, 0.75% and 1% risk (SIM-004).\n\n"
                        + "  --log <file>            Trade log (SIM-001); repeat for each symbol\n" + caps + ftmo);
                case "accept":
                    return new CommandSpec(name, new[] { "--dev", "--holdout", "--stress", "--benchmark", "--benchmark-holdout" }, ftmoOptions, "Usage: sim accept --dev <file> [--dev <file> ...] --seed <n> [options]\n\n"
                        + "Acceptance report against spec 13.1 (SIM-005). Each group of logs is combined first.\n\n"
                        + "  --dev <file>                Development trade log; repeat for each symbol\n"
                        + "  --holdout <file>            Holdout trade log; repeat for each symbol\n"
                        + "  --stress <file>             Development log under cost stress\n"
                        + "  --benchmark <file>          Benchmark log, development period\n"
                        + "  --benchmark-holdout <file>  Benchmark log, holdout period\n" + caps + ftmo);
                default:
                    return null;
            }
        }

        internal bool Allows(string option)
        {
            return Array.IndexOf(_single, option) >= 0 || Array.IndexOf(_repeated, option) >= 0;
        }

        internal bool Repeats(string option)
        {
            return Array.IndexOf(_repeated, option) >= 0;
        }

        private static string[] Concat(string[] first, params string[] second)
        {
            var all = new string[first.Length + second.Length];
            first.CopyTo(all, 0);
            second.CopyTo(all, first.Length);
            return all;
        }
    }

    /// <summary>An invalid argument, file or log, reported to the user with an exit code.</summary>
    private sealed class SimInputException : Exception
    {
        internal SimInputException(int exitCode, string message)
            : base(message)
        {
            ExitCode = exitCode;
        }

        internal int ExitCode { get; }
    }
}

/// <summary>A file a command asks the console program to write (SIM-002).</summary>
/// <param name="Path">Path as given on the command line.</param>
/// <param name="Content">Whole content of the file.</param>
public sealed record SimOutputFile(string Path, string Content);

/// <summary>What a command produced (SIM-002 to SIM-005).</summary>
public sealed class SimCommandResult
{
    private SimCommandResult(int exitCode, string output, string error, IReadOnlyList<SimOutputFile> files)
    {
        ExitCode = exitCode;
        Output = output;
        Error = error;
        Files = files;
    }

    /// <summary>Process exit code: 0 on success (SIM-002 to SIM-005).</summary>
    public int ExitCode { get; }

    /// <summary>Text for standard output (SIM-002 to SIM-005).</summary>
    public string Output { get; }

    /// <summary>Text for standard error; empty on success (SIM-002 to SIM-005).</summary>
    public string Error { get; }

    /// <summary>Files to write, in order; empty on failure (SIM-002).</summary>
    public IReadOnlyList<SimOutputFile> Files { get; }

    internal static SimCommandResult Ok(string output, IReadOnlyList<SimOutputFile> files)
    {
        return new SimCommandResult(SimCommandLine.ExitOk, output, string.Empty, files);
    }

    internal static SimCommandResult Failed(int exitCode, string error)
    {
        return new SimCommandResult(exitCode, string.Empty, error, Array.Empty<SimOutputFile>());
    }
}
