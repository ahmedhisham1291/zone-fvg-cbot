# TEMPORARY (WP08): prints selected members of the cTrader.Automate XML documentation with their
# summaries to the CI build log. Removed before the work package is finished.
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(sys.argv[1]) / "ctrader.automate"
pattern = re.compile(
    r"^[MPEFT]:cAlgo\.API\.(Algo|Internals\.\w+|ParameterAttribute|AssetConverter|TimeFrame|TimeZones|"
    r"AccessRights|Positions|PendingOrders|History|Http|LocalStorage|Timer|Bars|PositionModifiedEventArgs|"
    r"PendingOrder\w*EventArgs|AlgoException|RunningMode|ErrorCode|ProtectionType|PositionCloseReason|"
    r"TradeResult|TradeType|Robot|Color|LineStyle|ChartRectangle|ChartObject|ChartShape|Chart|IAccount|Asset\w*|Symbol\w*|MarketHours|MarketData)(\.|$)"
)
for xml in sorted(root.rglob("net6.0/cAlgo.API.xml")):
    print("== " + str(xml))
    for member in ET.parse(xml).getroot().iter("member"):
        name = member.get("name", "")
        if not pattern.search(name):
            continue
        if "PlaceStop" in name or "ExecuteMarket" in name or ".Draw" in name and "Rectangle" not in name:
            continue
        summary = member.find("summary")
        text = " ".join("".join(summary.itertext()).split()) if summary is not None else ""
        remarks = member.find("remarks")
        rtext = " ".join("".join(remarks.itertext()).split()) if remarks is not None else ""
        print("API| " + name + " || " + text[:300] + (" || R: " + rtext[:300] if rtext else ""))
