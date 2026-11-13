namespace ZoneFvg.Core.Domain;

/// <summary>Direction of a trade. Contract of docs/architecture.md section 3.</summary>
public enum Side
{
    /// <summary>Long trade, taken from a demand zone.</summary>
    Buy,

    /// <summary>Short trade, taken from a supply zone.</summary>
    Sell,
}
