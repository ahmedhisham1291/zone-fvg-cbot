using System;
using System.Collections.Generic;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Ops;

/// <summary>
/// The fields of spec section 11 for one closed trade (OPS-002): requested and filled price,
/// slippage, exit reason, result in R and in money, commission, swap, duration, and the maximum
/// adverse and favourable moves in R. A data record; derived values are computed from it, and a
/// value that cannot be computed is journaled as <c>NA</c>.
/// </summary>
/// <param name="Trade">The closed trade from the broker's history.</param>
/// <param name="Comment">The order comment of the position, which holds the plan (EXE-008), or null when not known.</param>
/// <param name="Side">Direction of the position, or null when not known.</param>
/// <param name="FilledPrice">Fill (entry) price of the position, or null when not known.</param>
/// <param name="PipSize">Pip size of the symbol, or null when not known.</param>
/// <param name="ExitReason">Why the trade ended.</param>
/// <param name="MaxAdverseR">Largest move against the position in R (see <see cref="TradeExcursion"/>), or null when not known.</param>
/// <param name="MaxFavourableR">Largest move in favour of the position in R, or null when not known.</param>
public sealed record TradeJournalEntry(
    ClosedTradeInfo Trade,
    string? Comment,
    Side? Side,
    double? FilledPrice,
    double? PipSize,
    TradeExitReason ExitReason,
    double? MaxAdverseR,
    double? MaxFavourableR)
{
    /// <summary>The plan read from <see cref="Comment"/>, or null when the comment is missing or not a plan comment (OPS-002, EXE-008).</summary>
    public CommentPlan? Plan => OrderComment.TryParse(Comment);

    /// <summary>The requested (planned) entry price from the comment, or null (OPS-002).</summary>
    public double? RequestedPrice => Plan?.Entry;

    /// <summary>
    /// Result in R: the trade's money result (<see cref="ClosedTradeInfo.NetProfit"/>, commission
    /// and swap included) divided by the planned risk money from the comment (OPS-002). Null when
    /// the comment is missing or invalid, the planned risk is not greater than zero, or the result
    /// is not finite.
    /// </summary>
    public double? ResultR
    {
        get
        {
            CommentPlan? plan = Plan;
            if (Trade == null || plan == null || !(plan.RiskMoney > 0.0) || !double.IsFinite(Trade.NetProfit))
            {
                return null;
            }

            return Trade.NetProfit / plan.RiskMoney;
        }
    }

    /// <summary>
    /// Slippage in pips between the requested and the filled price, positive when the fill is
    /// worse for the position: (filled - requested) / pip for a buy, (requested - filled) / pip
    /// for a sell (OPS-002). Null when a value needed is missing or the pip size is not greater
    /// than zero.
    /// </summary>
    public double? SlippagePips
    {
        get
        {
            double? requested = RequestedPrice;
            if (!requested.HasValue || !FilledPrice.HasValue || !Side.HasValue || !PipSize.HasValue || !(PipSize.Value > 0.0))
            {
                return null;
            }

            double difference = FilledPrice.Value - requested.Value;
            return (Side.Value == Domain.Side.Buy ? difference : -difference) / PipSize.Value;
        }
    }

    /// <summary>Time from opening to closing the position (OPS-002).</summary>
    public TimeSpan Duration => Trade.CloseTimeUtc - Trade.EntryTimeUtc;
}

/// <summary>
/// Largest adverse and favourable price moves of a position in R (OPS-002): the move from entry
/// to the lowest low and the highest high of the bars given, divided by the distance from entry
/// to stop. Which bars cover the life of the trade is the caller's choice.
/// </summary>
public static class TradeExcursion
{
    private static readonly TimeSpan M15 = TimeSpan.FromMinutes(15.0);

    /// <summary>
    /// Adverse and favourable moves in R (OPS-002). For a buy the adverse move is entry minus the
    /// lowest low and the favourable move the highest high minus entry; for a sell the other way
    /// round. A move that never went beyond entry counts as zero.
    /// </summary>
    /// <param name="side">Direction of the position.</param>
    /// <param name="entry">Fill price.</param>
    /// <param name="stop">Planned stop price.</param>
    /// <param name="bars">Closed bars covering the life of the trade.</param>
    /// <returns>The two moves in R, or null when there is no bar or the stop is not on the loss side of entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bars"/> is null.</exception>
    public static (double AdverseR, double FavourableR)? InR(Side side, double entry, double stop, IEnumerable<Bar> bars)
    {
        ArgumentNullException.ThrowIfNull(bars);
        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (Bar bar in bars)
        {
            low = Math.Min(low, bar.Low);
            high = Math.Max(high, bar.High);
        }

        return FromExtremes(side, entry, stop, low, high);
    }

    /// <summary>
    /// Adverse and favourable moves in R of a closed trade (OPS-002), from the closed M15 bars that
    /// overlap its life (open time before the close, close time after the fill) and its exit price.
    /// The bars of the fill and of the close count whole, so at M15 resolution a move can be shown
    /// slightly larger than it was; the exit price is included because the bar of the close may not
    /// have closed yet when the trade is journaled.
    /// </summary>
    /// <param name="side">Direction of the position.</param>
    /// <param name="entry">Fill price.</param>
    /// <param name="stop">Planned stop price.</param>
    /// <param name="entryUtc">Fill time.</param>
    /// <param name="closeUtc">Close time.</param>
    /// <param name="exitPrice">Closing price, or null when not known.</param>
    /// <param name="m15Bars">Closed M15 bars, oldest first.</param>
    /// <returns>
    /// The two moves in R, or null when the bars do not reach back to the bar of the fill, nothing
    /// covers the trade, or the stop is not on the loss side of entry.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="m15Bars"/> is null.</exception>
    public static (double AdverseR, double FavourableR)? ForTrade(Side side, double entry, double stop, DateTime entryUtc, DateTime closeUtc, double? exitPrice, IReadOnlyList<Bar> m15Bars)
    {
        ArgumentNullException.ThrowIfNull(m15Bars);
        if (m15Bars.Count == 0 || m15Bars[0].OpenTimeUtc > entryUtc)
        {
            // The bars do not cover the start of the trade: NA rather than a value that is too small.
            return null;
        }

        double low = double.PositiveInfinity;
        double high = double.NegativeInfinity;
        foreach (Bar bar in m15Bars)
        {
            if (bar.OpenTimeUtc < closeUtc && bar.OpenTimeUtc + M15 > entryUtc)
            {
                low = Math.Min(low, bar.Low);
                high = Math.Max(high, bar.High);
            }
        }

        if (exitPrice.HasValue && double.IsFinite(exitPrice.Value))
        {
            low = Math.Min(low, exitPrice.Value);
            high = Math.Max(high, exitPrice.Value);
        }

        return FromExtremes(side, entry, stop, low, high);
    }

    private static (double AdverseR, double FavourableR)? FromExtremes(Side side, double entry, double stop, double low, double high)
    {
        double risk = side == Side.Buy ? entry - stop : stop - entry;
        if (!double.IsFinite(risk) || !(risk > 0.0) || double.IsPositiveInfinity(low))
        {
            return null;
        }

        double down = Math.Max(0.0, entry - low) / risk;
        double up = Math.Max(0.0, high - entry) / risk;
        return side == Side.Buy ? (down, up) : (up, down);
    }
}
