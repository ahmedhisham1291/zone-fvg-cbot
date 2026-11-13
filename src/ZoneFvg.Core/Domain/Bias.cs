namespace ZoneFvg.Core.Domain;

/// <summary>Trend bias from the daily timeframe (SIG-005). Contract of docs/architecture.md section 3.</summary>
public enum Bias
{
    /// <summary>No direction: no zone may produce a signal. This is the value of an unset bias.</summary>
    Neutral,

    /// <summary>Only demand zones may produce a signal.</summary>
    Long,

    /// <summary>Only supply zones may produce a signal.</summary>
    Short,
}
