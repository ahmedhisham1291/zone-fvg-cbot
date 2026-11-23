namespace ZoneFvg.Core.App;

/// <summary>
/// The entry mode of one bot instance (spec v0.10 decision E, REC-017), fixed at start and read
/// from <see cref="BotParameters.Mode"/>.
/// </summary>
public enum EntryMode
{
    /// <summary>Zones and fair value gaps (spec 3.2 to 3.5): <see cref="BotParameters.Breakout"/> is null.</summary>
    ZoneFvg,

    /// <summary>Breakout of the reference day's range (spec 3.7): <see cref="BotParameters.Breakout"/> is set.</summary>
    Breakout,

    /// <summary>Break of the session range (spec 3.8, SIG-034): <see cref="BotParameters.SessionRange"/> is set.</summary>
    SessionRange,
}
