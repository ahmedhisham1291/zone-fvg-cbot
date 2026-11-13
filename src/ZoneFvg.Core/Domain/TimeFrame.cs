namespace ZoneFvg.Core.Domain;

/// <summary>Bar timeframes the bot reads (spec section 2). Contract of docs/architecture.md section 3.</summary>
public enum TimeFrame
{
    /// <summary>15-minute bars: zone touches, fair value gaps and entries.</summary>
    M15,

    /// <summary>4-hour bars: swings and supply and demand zones.</summary>
    H4,

    /// <summary>Daily bars: trend bias.</summary>
    D1,
}
