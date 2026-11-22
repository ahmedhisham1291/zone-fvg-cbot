namespace ZoneFvg.Core.Execution;

/// <summary>How an entry order is placed (EXE-008, EXE-018).</summary>
public enum EntryOrderType
{
    /// <summary>A limit order (ZoneFvg signals, EXE-008). The value of an unset entry type.</summary>
    Limit,

    /// <summary>A stop order: buy stop or sell stop (Breakout signals, EXE-018).</summary>
    Stop,
}
