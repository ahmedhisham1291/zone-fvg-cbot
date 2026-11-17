using System;
using System.Globalization;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Risk;

namespace ZoneFvg.Core.App;

/// <summary>
/// Start checks (RSK-014, fail closed): the adapter calls them after creating the core and before
/// <see cref="BotCore.OnStart"/>, and refuses to start with the returned message. They read only
/// what they are given; nothing is stored.
/// </summary>
public static class StartChecks
{
    /// <summary>Largest accepted difference between the backtest's starting balance and InitialCapital, in account currency (RSK-014).</summary>
    public const decimal MaxBalanceDifference = 0.01m;

    /// <summary>
    /// Reads the server time and the account from the broker and checks them (RSK-014). A read
    /// that throws is a refusal whose message names the exception.
    /// </summary>
    /// <param name="parameters">The validated parameters of the instance.</param>
    /// <param name="broker">The broker port.</param>
    /// <param name="isBacktest">True in a backtest.</param>
    /// <returns>Null when the bot may start; otherwise the reason it may not.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static string? Check(BotParameters parameters, IBroker broker, bool isBacktest)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(broker);
        DateTime serverTimeUtc;
        AccountSnapshot account;
        try
        {
            serverTimeUtc = broker.ServerTimeUtc;
            account = broker.GetAccount();
        }
        catch (Exception exception)
        {
            // Fail closed: without the account or the server time the bot does not start (RSK-014).
            return "the account or the server time could not be read: " + exception.GetType().Name + ": " + exception.Message;
        }

        return Check(parameters, account, serverTimeUtc, isBacktest);
    }

    /// <summary>
    /// Checks the start conditions (RSK-014): 00:00 CE(S)T of RiskEpochDate must not be later than
    /// the server time, and in a backtest the account balance must equal InitialCapital within
    /// 0.01 in account currency (a difference of exactly 0.01 is accepted). Outside backtests the
    /// balance is not compared.
    /// </summary>
    /// <param name="parameters">The validated parameters of the instance.</param>
    /// <param name="account">The account as the broker reports it; null is a refusal.</param>
    /// <param name="serverTimeUtc">The broker's server time, kind UTC.</param>
    /// <param name="isBacktest">True in a backtest.</param>
    /// <returns>Null when the bot may start; otherwise the reason it may not, naming both values.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parameters"/> is null.</exception>
    public static string? Check(BotParameters parameters, AccountSnapshot? account, DateTime serverTimeUtc, bool isBacktest)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (serverTimeUtc.Kind != DateTimeKind.Utc)
        {
            return "the server time " + Minute(serverTimeUtc) + " is not a UTC time";
        }

        if (account == null)
        {
            return "the account could not be read";
        }

        DateTime epoch = parameters.Risk.RiskEpochDate;
        DateTime epochStart = CestClock.StartOfCestDateUtc(epoch);
        if (epochStart > serverTimeUtc)
        {
            return "RiskEpochDate " + epoch.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " starts at " + Minute(epochStart)
                + " UTC, after the server time " + Minute(serverTimeUtc) + " UTC";
        }

        if (isBacktest && !SameAmount(account.Balance, parameters.Risk.InitialCapital))
        {
            return "backtest starting balance " + Money(account.Balance) + " differs from InitialCapital " + Money(parameters.Risk.InitialCapital)
                + "; set both to the same value";
        }

        return null;
    }

    private static bool SameAmount(double balance, double capital)
    {
        // Compared in decimal, so 100,000.01 against 100,000 is a difference of exactly 0.01, not 0.0100000000093.
        if (!double.IsFinite(balance) || !double.IsFinite(capital) || Math.Abs(balance) > 1e15 || Math.Abs(capital) > 1e15)
        {
            return false;
        }

        return Math.Abs((decimal)balance - (decimal)capital) <= MaxBalanceDifference;
    }

    private static string Minute(DateTime value)
    {
        return value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Money(double value)
    {
        return value.ToString("F2", CultureInfo.InvariantCulture);
    }
}
