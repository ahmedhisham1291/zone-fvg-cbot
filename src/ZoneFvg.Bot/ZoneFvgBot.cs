using cAlgo.API;
using ZoneFvg.Core;

namespace cAlgo.Robots
{
    /// <summary>cTrader adapter. Contains no trading logic (PLT-002).</summary>
    [Robot(AccessRights = AccessRights.None, TimeZone = TimeZones.UTC)]
    public class ZoneFvgBot : Robot
    {
        /// <inheritdoc />
        protected override void OnStart()
        {
            Print("ZoneFvgBot " + BuildInfo.Version + " started. Trading logic is not implemented yet.");
        }
    }
}
