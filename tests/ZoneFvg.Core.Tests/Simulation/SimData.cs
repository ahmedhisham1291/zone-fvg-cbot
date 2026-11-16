using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Simulation;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Builders for trade records in the simulation tests (SIM-001 to SIM-005). Every record built
/// here is consistent: result_money = result_r x risk_money.
/// </summary>
internal static class SimData
{
    public static DateTime Utc(int year, int month, int day, int hour, int minute)
    {
        return new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
    }

    /// <summary>
    /// A trade with the given result in R and risk in money. Prices are those of a 20-pip EURUSD
    /// trade (buy: entry 1.1, stop 1.098, target 1.104; sell mirrored). Commission is a
    /// component of the result and is not added again.
    /// </summary>
    public static TradeRecord Trade(string symbol, DateTime openUtc, DateTime closeUtc, double resultR, double riskMoney = 500.0, double commission = 0.0, Side side = Side.Buy, string zoneId = "D-20260105-0800")
    {
        double entry = 1.1;
        double stop = side == Side.Buy ? 1.098 : 1.102;
        double target = side == Side.Buy ? 1.104 : 1.096;
        double move = resultR * 0.002;
        double exit = side == Side.Buy ? entry + move : entry - move;
        string reason = resultR >= 0.0 ? "Target" : "Stop";
        return new TradeRecord(symbol, zoneId, openUtc, closeUtc, side, 250000.0, entry, stop, target, exit, reason, resultR * riskMoney, resultR, commission, 0.0, riskMoney);
    }

    public static IReadOnlyList<TradeRecord> Log(params TradeRecord[] trades)
    {
        return trades;
    }
}
