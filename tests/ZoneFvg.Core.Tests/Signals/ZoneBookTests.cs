using Xunit;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Signals;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Zone book tests: supersede rule (SIG-012), id collisions (SIG-013) and lifecycle (SIG-014).
/// Zones are built by hand. The reference demand zone has proximal 100.2, distal 99.5 and
/// activates 2026-01-06 00:00 UTC; the reference supply zone has proximal 99.8 and distal 100.5.
/// M15 bar k opens at activation + k x 15 minutes. Within one M15 close the caller runs
/// <see cref="ZoneBook.OnM15Closed"/> and then <see cref="ZoneBook.EndConfirmationWindows"/>.
/// </summary>
public class ZoneBookTests
{
    private const string DemandId = "D-20260105-0000";
    private const string SupplyId = "S-20260105-0000";

    private static readonly DateTime Activation = new(2026, 1, 6, 0, 0, 0, DateTimeKind.Utc);
    private static readonly SignalParameters Defaults = new();

    // ------------------------------------------------------------------ SIG-012 supersede rule

    [Theory]
    [Trait("Req", "SIG-012")]
    [InlineData(100.49, 101.49, false)] // intersection 101.0 - 100.49 = 0.51 > 0.5: removed
    [InlineData(100.499, 101.499, false)] // 0.501 > 0.5: removed
    [InlineData(100.5, 101.5, true)] // 0.500, not more than 50%: kept
    [InlineData(100.501, 101.501, true)] // 0.499: kept
    public void Add_OverlapAroundHalfOfOlderActiveDemand_RemovesOnlyAboveHalf(double newLow, double newHigh, bool olderKept)
    {
        // Older demand zone 100.0 to 101.0 (height 1.0), still Active.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange("D-20260105-0000", 100.0, 101.0));

        Assert.True(book.Add(DemandRange("D-20260106-0000", newLow, newHigh)));

        string[] expected = olderKept ? new[] { "D-20260105-0000", "D-20260106-0000" } : new[] { "D-20260106-0000" };
        Assert.Equal(expected, Ids(book));
    }

    [Theory]
    [Trait("Req", "SIG-012")]
    [InlineData(100.49, 101.49, false)]
    [InlineData(100.5, 101.5, true)]
    public void Add_OverlapAroundHalfOfOlderActiveSupply_RemovesOnlyAboveHalf(double newLow, double newHigh, bool olderKept)
    {
        // Older supply zone 100.0 to 101.0 (proximal 100.0, distal 101.0).
        var book = new ZoneBook(Defaults);
        book.Add(SupplyRange("S-20260105-0000", 100.0, 101.0));

        Assert.True(book.Add(SupplyRange("S-20260106-0000", newLow, newHigh)));

        string[] expected = olderKept ? new[] { "S-20260105-0000", "S-20260106-0000" } : new[] { "S-20260106-0000" };
        Assert.Equal(expected, Ids(book));
    }

    [Fact]
    [Trait("Req", "SIG-012")]
    public void Add_OverlapMeasuredAgainstOlderHeight_SmallOlderZoneRemoved()
    {
        // Older 100.0 to 100.4 (height 0.4); new 100.19 to 102.0. Intersection 100.4 - 100.19 =
        // 0.21 > 0.2 = 50% of the older height, although it is only about 12% of the new height.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange("D-20260105-0000", 100.0, 100.4));

        book.Add(DemandRange("D-20260106-0000", 100.19, 102.0));

        Assert.Equal(new[] { "D-20260106-0000" }, Ids(book));
    }

    [Fact]
    [Trait("Req", "SIG-012")]
    public void Add_OverlapMeasuredAgainstOlderHeight_LargeOlderZoneKept()
    {
        // Older 100.0 to 102.0 (height 2.0); new 100.0 to 100.9 lies fully inside it. Intersection
        // 0.9 < 1.0 = 50% of the older height, so the older zone stays.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange("D-20260105-0000", 100.0, 102.0));

        book.Add(DemandRange("D-20260106-0000", 100.0, 100.9));

        Assert.Equal(new[] { "D-20260105-0000", "D-20260106-0000" }, Ids(book));
    }

    [Fact]
    [Trait("Req", "SIG-012")]
    public void Add_OlderZoneTouched_IsNeverRemoved()
    {
        // Older demand 100.0 to 101.0 is touched by an M15 bar with low 100.9 (<= proximal 101.0)
        // that closes at 100.95 (above distal 100.0). A new zone with the same range (100% overlap)
        // does not remove it.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange("D-20260105-0000", 100.0, 101.0));
        book.OnM15Closed(M15(0, 100.95, 101.0, 100.9, 100.95));
        Assert.Equal(ZoneState.Touched, book.Zones[0].State);

        book.Add(DemandRange("D-20260106-0000", 100.0, 101.0));

        Assert.Equal(new[] { "D-20260105-0000", "D-20260106-0000" }, Ids(book));
        Assert.Equal(ZoneState.Touched, book.Zones[0].State);
    }

    [Fact]
    [Trait("Req", "SIG-012")]
    public void Add_DifferentTypes_NeverInteract()
    {
        // Demand and supply zones over the same range 100.0 to 101.0, in both orders.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange("D-20260105-0000", 100.0, 101.0));
        book.Add(SupplyRange("S-20260106-0000", 100.0, 101.0));
        book.Add(SupplyRange("S-20260107-0000", 105.0, 106.0));
        book.Add(DemandRange("D-20260108-0000", 105.0, 106.0));

        Assert.Equal(new[] { "D-20260105-0000", "D-20260108-0000", "S-20260106-0000", "S-20260107-0000" }, Ids(book));
    }

    [Fact]
    [Trait("Req", "SIG-012")]
    public void Add_SeveralOlderZones_RemovesEveryOneOverlappedMoreThanHalf()
    {
        // New 100.1 to 101.1. Older 100.0-101.0: intersection 0.9 > 0.5, removed.
        // Older 100.2-101.2: intersection 101.1 - 100.2 = 0.9 > 0.5, removed.
        // Older 105.0-106.0: no intersection, kept.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange("D-20260101-0000", 100.0, 101.0));
        book.Add(DemandRange("D-20260102-0000", 100.2, 101.2));
        book.Add(DemandRange("D-20260103-0000", 105.0, 106.0));

        book.Add(DemandRange("D-20260104-0000", 100.1, 101.1));

        Assert.Equal(new[] { "D-20260103-0000", "D-20260104-0000" }, Ids(book));
    }

    [Fact]
    [Trait("Req", "SIG-012")]
    public void Add_AdjacentZones_AreKept()
    {
        // Older 100.0-101.0, new 101.0-102.0: they share only the line 101.0, intersection 0.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange("D-20260105-0000", 100.0, 101.0));

        book.Add(DemandRange("D-20260106-0000", 101.0, 102.0));

        Assert.Equal(new[] { "D-20260105-0000", "D-20260106-0000" }, Ids(book));
    }

    // ------------------------------------------------------------------ SIG-013 id collisions

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Add_SameIdAsExistingZone_DiscardsTheLaterOneAndRecordsDiagnostic()
    {
        var book = new ZoneBook(Defaults);
        Zone original = DemandRange(DemandId, 100.0, 101.0);
        Assert.True(book.Add(original));

        bool added = book.Add(DemandRange(DemandId, 100.3, 101.3));

        Assert.False(added);
        Assert.Equal(original, Assert.Single(book.Zones));
        string diagnostic = Assert.Single(book.Diagnostics);
        Assert.Contains(DemandId, diagnostic, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Add_SameIdAsZoneThatEnded_IsStillDiscarded()
    {
        // The first zone is invalidated by a close at 99.0 below its distal 100.0; a later
        // detection with the same id is discarded all the same.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange(DemandId, 100.0, 101.0));
        book.OnM15Closed(M15(0, 100.5, 100.6, 98.9, 99.0));
        Assert.Empty(book.Zones);

        Assert.False(book.Add(DemandRange(DemandId, 100.0, 101.0)));

        Assert.Empty(book.Zones);
        Assert.Single(book.Diagnostics);
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Add_DiscardedCollision_SupersedesNothing()
    {
        // D-20260104-0000 (100-101) is Active. A colliding detection of D-20260105-0000 over the
        // same range is discarded, so it cannot remove D-20260104-0000.
        var book = new ZoneBook(Defaults);
        book.Add(DemandRange("D-20260105-0000", 105.0, 106.0));
        book.Add(DemandRange("D-20260104-0000", 100.0, 101.0));

        Assert.False(book.Add(DemandRange("D-20260105-0000", 100.0, 101.0)));

        Assert.Equal(new[] { "D-20260104-0000", "D-20260105-0000" }, Ids(book));
        Assert.Equal(106.0, book.Zones[1].Proximal);
    }

    [Fact]
    [Trait("Req", "SIG-013")]
    public void Add_SameTimeDifferentType_IsNotACollision()
    {
        var book = new ZoneBook(Defaults);

        Assert.True(book.Add(DemandRange("D-20260105-0000", 100.0, 101.0)));
        Assert.True(book.Add(SupplyRange("S-20260105-0000", 102.0, 103.0)));

        Assert.Empty(book.Diagnostics);
        Assert.Equal(2, book.Zones.Count);
    }

    // ------------------------------------------------------------------ SIG-014 activation filter

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_BarOpenedBeforeActivation_IsIgnored()
    {
        // Bar opening 15 minutes before activation would touch (low 99.0) and invalidate (close 99.0).
        var book = BookWith(DemandZone());

        IReadOnlyList<Zone> changed = book.OnM15Closed(M15(-1, 100.0, 100.1, 99.0, 99.0));

        Assert.Empty(changed);
        Zone zone = Assert.Single(book.Zones);
        Assert.Equal(ZoneState.Active, zone.State);
        Assert.Null(zone.TouchBarOpenUtc);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_BarOpeningAtActivation_IsEvaluated()
    {
        var book = BookWith(DemandZone());

        IReadOnlyList<Zone> changed = book.OnM15Closed(M15(0, 100.5, 100.6, 100.2, 100.4));

        Assert.Equal(ZoneState.Touched, Assert.Single(changed).State);
    }

    // ------------------------------------------------------------------ SIG-014 touch

    [Theory]
    [Trait("Req", "SIG-014")]
    [InlineData(100.2, true)] // low == proximal
    [InlineData(100.199, true)] // one tick below
    [InlineData(100.201, false)] // one tick above
    public void OnM15Closed_DemandLowAroundProximal_TouchesFromProximal(double low, bool touched)
    {
        var book = BookWith(DemandZone());

        IReadOnlyList<Zone> changed = book.OnM15Closed(M15(0, 100.5, 100.6, low, 100.4));

        Zone zone = Assert.Single(book.Zones);
        Assert.Equal(touched ? ZoneState.Touched : ZoneState.Active, zone.State);
        Assert.Equal(touched ? new[] { zone } : Array.Empty<Zone>(), changed);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_DemandTouch_RecordsTouchBarAndExtreme()
    {
        var book = BookWith(DemandZone());

        Zone touched = Assert.Single(book.OnM15Closed(M15(0, 100.5, 100.6, 100.0, 100.4)));

        Zone expected = DemandZone() with { State = ZoneState.Touched, TouchBarOpenUtc = Activation, ExtremeSinceTouch = 100.0 };
        Assert.Equal(expected, touched);
        Assert.Equal(expected, Assert.Single(book.Zones));
        TouchedZone open = Assert.Single(book.TouchedZones);
        Assert.Equal(expected, open.Zone);
        Assert.Equal(0, open.BarsSinceTouch);
    }

    [Theory]
    [Trait("Req", "SIG-014")]
    [InlineData(99.8, true)] // high == proximal
    [InlineData(99.801, true)] // one tick above
    [InlineData(99.799, false)] // one tick below
    public void OnM15Closed_SupplyHighAroundProximal_TouchesFromProximal(double high, bool touched)
    {
        var book = BookWith(SupplyZone());

        book.OnM15Closed(M15(0, 99.5, high, 99.4, 99.6));

        Assert.Equal(touched ? ZoneState.Touched : ZoneState.Active, Assert.Single(book.Zones).State);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_SupplyTouch_RecordsHighestHighAsExtreme()
    {
        var book = BookWith(SupplyZone());

        Zone touched = Assert.Single(book.OnM15Closed(M15(0, 99.5, 100.0, 99.4, 99.6)));

        Zone expected = SupplyZone() with { State = ZoneState.Touched, TouchBarOpenUtc = Activation, ExtremeSinceTouch = 100.0 };
        Assert.Equal(expected, touched);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_TouchedTwice_KeepsTheFirstTouchBar()
    {
        var book = BookWith(DemandZone());
        book.OnM15Closed(M15(0, 100.5, 100.6, 100.1, 100.4));

        IReadOnlyList<Zone> changed = book.OnM15Closed(M15(1, 100.4, 100.5, 100.15, 100.3));

        Assert.Empty(changed);
        Assert.Equal(Activation, Assert.Single(book.Zones).TouchBarOpenUtc);
    }

    // ------------------------------------------------------------------ SIG-014 extreme since touch

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_DemandAfterTouch_TracksLowestLowSinceTouch()
    {
        // Touch bar low 100.0; then lows 99.8 (new extreme), 99.9 (no change) and 99.0 with a close
        // of 99.6 above the distal 99.5 (new extreme, still Touched).
        var book = BookWith(DemandZone());
        book.OnM15Closed(M15(0, 100.5, 100.6, 100.0, 100.4));
        Assert.Equal(100.0, book.Zones[0].ExtremeSinceTouch);

        Assert.Empty(book.OnM15Closed(M15(1, 100.4, 100.5, 99.8, 100.0)));
        Assert.Equal(99.8, book.Zones[0].ExtremeSinceTouch);

        Assert.Empty(book.OnM15Closed(M15(2, 100.0, 100.1, 99.9, 100.0)));
        Assert.Equal(99.8, book.Zones[0].ExtremeSinceTouch);

        Assert.Empty(book.OnM15Closed(M15(3, 100.0, 100.1, 99.0, 99.6)));
        Assert.Equal(99.0, book.Zones[0].ExtremeSinceTouch);
        Assert.Equal(ZoneState.Touched, book.Zones[0].State);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_SupplyAfterTouch_TracksHighestHighSinceTouch()
    {
        // Touch bar high 99.9; then highs 100.3 (new extreme) and 100.1 (no change).
        var book = BookWith(SupplyZone());
        book.OnM15Closed(M15(0, 99.5, 99.9, 99.4, 99.6));

        book.OnM15Closed(M15(1, 99.6, 100.3, 99.5, 100.0));
        Assert.Equal(100.3, book.Zones[0].ExtremeSinceTouch);

        book.OnM15Closed(M15(2, 100.0, 100.1, 99.9, 100.0));
        Assert.Equal(100.3, book.Zones[0].ExtremeSinceTouch);
    }

    // ------------------------------------------------------------------ SIG-014 invalidation

    [Theory]
    [Trait("Req", "SIG-014")]
    [InlineData(99.499, ZoneState.Invalid)] // close one tick below distal 99.5
    [InlineData(99.5, ZoneState.Touched)] // close at distal: not beyond it
    [InlineData(99.501, ZoneState.Touched)]
    public void OnM15Closed_DemandTouchBarClosesAroundDistal_InvalidOnlyBelow(double close, ZoneState expected)
    {
        // The bar touches (low 99.0 <= 100.2) and closes near the distal 99.5. Touch and close
        // beyond distal in the same bar make the zone Invalid, not Touched.
        var book = BookWith(DemandZone());

        Zone changed = Assert.Single(book.OnM15Closed(M15(0, 100.5, 100.6, 99.0, close)));

        Assert.Equal(expected, changed.State);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_DemandTouchAndCloseBelowDistalInOneBar_InvalidWithoutTouchData()
    {
        var book = BookWith(DemandZone());

        Zone changed = Assert.Single(book.OnM15Closed(M15(0, 100.5, 100.6, 99.0, 99.4)));

        Assert.Equal(DemandZone() with { State = ZoneState.Invalid }, changed);
        Assert.Empty(book.Zones);
        Assert.Empty(book.TouchedZones);
    }

    [Theory]
    [Trait("Req", "SIG-014")]
    [InlineData(100.501, ZoneState.Invalid)] // close one tick above distal 100.5
    [InlineData(100.5, ZoneState.Touched)]
    public void OnM15Closed_SupplyTouchBarClosesAroundDistal_InvalidOnlyAbove(double close, ZoneState expected)
    {
        var book = BookWith(SupplyZone());

        Zone changed = Assert.Single(book.OnM15Closed(M15(0, 99.5, 101.0, 99.4, close)));

        Assert.Equal(expected, changed.State);
    }

    [Theory]
    [Trait("Req", "SIG-014")]
    [InlineData(99.499, true)]
    [InlineData(99.5, false)]
    public void OnM15Closed_TouchedDemandLaterCloseAroundDistal_InvalidOnlyBelow(double close, bool invalid)
    {
        var book = BookWith(DemandZone());
        book.OnM15Closed(M15(0, 100.5, 100.6, 100.0, 100.4));

        IReadOnlyList<Zone> changed = book.OnM15Closed(M15(1, 100.0, 100.1, 99.2, close));

        if (invalid)
        {
            Zone zone = Assert.Single(changed);
            Assert.Equal(ZoneState.Invalid, zone.State);
            Assert.Equal(Activation, zone.TouchBarOpenUtc);
            Assert.Empty(book.Zones);
        }
        else
        {
            Assert.Empty(changed);
            Assert.Equal(ZoneState.Touched, Assert.Single(book.Zones).State);
        }
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_TouchedSupplyLaterCloseAboveDistal_Invalid()
    {
        var book = BookWith(SupplyZone());
        book.OnM15Closed(M15(0, 99.5, 99.9, 99.4, 99.6));

        Zone changed = Assert.Single(book.OnM15Closed(M15(1, 99.6, 100.7, 99.5, 100.6)));

        Assert.Equal(ZoneState.Invalid, changed.State);
        Assert.Empty(book.Zones);
    }

    // ------------------------------------------------------------------ SIG-014 confirmation window

    [Fact]
    [Trait("Req", "SIG-014")]
    public void EndConfirmationWindows_SixteenBarsAfterTouch_MovesTouchedToUsed()
    {
        // Touch bar = bar 0. Bars 1 to 15 leave the window open. After bar 16 closes the zone is
        // still Touched for FVG detection, then the window end moves it to Used.
        var book = BookWith(DemandZone());
        book.OnM15Closed(M15(0, 100.5, 100.6, 100.0, 100.4));
        Assert.Empty(book.EndConfirmationWindows());

        for (int k = 1; k <= 15; k++)
        {
            Assert.Empty(book.OnM15Closed(Quiet(k)));
            Assert.Empty(book.EndConfirmationWindows());
        }

        Assert.Equal(15, Assert.Single(book.TouchedZones).BarsSinceTouch);

        book.OnM15Closed(Quiet(16));
        TouchedZone beforeEnd = Assert.Single(book.TouchedZones);
        Assert.Equal(16, beforeEnd.BarsSinceTouch);
        Assert.Equal(ZoneState.Touched, beforeEnd.Zone.State);

        Zone ended = Assert.Single(book.EndConfirmationWindows());

        Assert.Equal(ZoneState.Used, ended.State);
        Assert.Equal(Activation, ended.TouchBarOpenUtc);
        Assert.Empty(book.Zones);
        Assert.Empty(book.TouchedZones);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void EndConfirmationWindows_ConfirmWindowBarsTwo_EndsAfterTwoBars()
    {
        var book = new ZoneBook(Defaults with { ConfirmWindowBars = 2 });
        book.Add(DemandZone());
        book.OnM15Closed(M15(0, 100.5, 100.6, 100.0, 100.4));
        book.OnM15Closed(Quiet(1));
        Assert.Empty(book.EndConfirmationWindows());

        book.OnM15Closed(Quiet(2));

        Assert.Equal(ZoneState.Used, Assert.Single(book.EndConfirmationWindows()).State);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void EndConfirmationWindows_ActiveZone_StaysActive()
    {
        var book = BookWith(DemandZone());
        for (int k = 0; k < 40; k++)
        {
            book.OnM15Closed(Quiet(k));
        }

        Assert.Empty(book.EndConfirmationWindows());
        Assert.Equal(ZoneState.Active, Assert.Single(book.Zones).State);
    }

    // ------------------------------------------------------------------ SIG-014 signalled and used

    [Fact]
    [Trait("Req", "SIG-014")]
    public void MarkSignalled_TouchedZone_GoesThroughSignalledToUsed()
    {
        var book = BookWith(DemandZone());
        Zone touched = Assert.Single(book.OnM15Closed(M15(0, 100.5, 100.6, 100.0, 100.4)));

        IReadOnlyList<Zone> transitions = book.MarkSignalled(DemandId);

        Assert.Equal(2, transitions.Count);
        Assert.Equal(touched with { State = ZoneState.Signalled }, transitions[0]);
        Assert.Equal(touched with { State = ZoneState.Used }, transitions[1]);
        Assert.Empty(book.Zones);
        Assert.Empty(book.TouchedZones);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void MarkUsed_TouchedZone_GoesToUsed()
    {
        var book = BookWith(SupplyZone());
        Zone touched = Assert.Single(book.OnM15Closed(M15(0, 99.5, 99.9, 99.4, 99.6)));

        Zone used = Assert.Single(book.MarkUsed(SupplyId));

        Assert.Equal(touched with { State = ZoneState.Used }, used);
        Assert.Empty(book.Zones);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void MarkSignalled_ActiveZone_Throws()
    {
        var book = BookWith(DemandZone());

        Assert.Throws<InvalidOperationException>(() => book.MarkSignalled(DemandId));
        Assert.Throws<InvalidOperationException>(() => book.MarkUsed(DemandId));
        Assert.Equal(ZoneState.Active, Assert.Single(book.Zones).State);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void MarkSignalled_UnknownId_Throws()
    {
        var book = BookWith(DemandZone());

        Assert.Throws<InvalidOperationException>(() => book.MarkSignalled("D-20990101-0000"));
        Assert.Throws<InvalidOperationException>(() => book.MarkUsed("D-20990101-0000"));
        Assert.Throws<ArgumentNullException>("zoneId", () => book.MarkSignalled(null!));
        Assert.Throws<ArgumentNullException>("zoneId", () => book.MarkUsed(null!));
    }

    // ------------------------------------------------------------------ SIG-014 expiry

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnH4Closed_ExactlyMaxZoneAgeBarsSinceActivation_Expires()
    {
        // H4 bars opening at activation, activation + 4h, ... The 120th closes the zone's life.
        var book = BookWith(DemandZone());
        IReadOnlyList<Bar> h4 = H4From(Activation, 120);

        for (int i = 0; i < 119; i++)
        {
            Assert.Empty(book.OnH4Closed(h4[i]));
        }

        Assert.Equal(ZoneState.Active, Assert.Single(book.Zones).State);

        Zone expired = Assert.Single(book.OnH4Closed(h4[119]));

        Assert.Equal(DemandZone() with { State = ZoneState.Expired }, expired);
        Assert.Empty(book.Zones);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnH4Closed_BarOpenedBeforeActivation_IsNotCounted()
    {
        // The H4 bar opening at activation - 4h (the leg bar) is not counted, so 119 bars from
        // activation still leave the zone alive and the 120th expires it.
        var book = BookWith(DemandZone());
        book.OnH4Closed(H4From(Activation.AddHours(-4.0), 1)[0]);
        IReadOnlyList<Bar> h4 = H4From(Activation, 120);

        for (int i = 0; i < 119; i++)
        {
            book.OnH4Closed(h4[i]);
        }

        Assert.Single(book.Zones);
        Assert.Equal(ZoneState.Expired, Assert.Single(book.OnH4Closed(h4[119])).State);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnH4Closed_TouchedZoneReachesMaxAge_Expires()
    {
        var book = new ZoneBook(Defaults with { MaxZoneAgeBars = 3 });
        book.Add(DemandZone());
        book.OnM15Closed(M15(0, 100.5, 100.6, 100.0, 100.4));
        IReadOnlyList<Bar> h4 = H4From(Activation, 3);

        Assert.Empty(book.OnH4Closed(h4[0]));
        Assert.Empty(book.OnH4Closed(h4[1]));
        Zone expired = Assert.Single(book.OnH4Closed(h4[2]));

        Assert.Equal(ZoneState.Expired, expired.State);
        Assert.Equal(Activation, expired.TouchBarOpenUtc);
        Assert.Empty(book.Zones);
        Assert.Empty(book.TouchedZones);
    }

    // ------------------------------------------------------------------ SIG-014 terminal states

    [Fact]
    [Trait("Req", "SIG-014")]
    public void TerminalInvalid_NeverChangesAgain()
    {
        var book = BookWith(DemandZone());
        book.OnM15Closed(M15(0, 100.5, 100.6, 99.0, 99.4));

        AssertNothingChangesAnyMore(book, DemandId);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void TerminalUsed_NeverChangesAgain()
    {
        var book = BookWith(DemandZone());
        book.OnM15Closed(M15(0, 100.5, 100.6, 100.0, 100.4));
        book.MarkUsed(DemandId);

        AssertNothingChangesAnyMore(book, DemandId);
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void TerminalExpired_NeverChangesAgain()
    {
        var book = new ZoneBook(Defaults with { MaxZoneAgeBars = 1 });
        book.Add(DemandZone());
        Assert.Equal(ZoneState.Expired, Assert.Single(book.OnH4Closed(H4From(Activation, 1)[0])).State);

        AssertNothingChangesAnyMore(book, DemandId);
    }

    // ------------------------------------------------------------------ ordering and contract

    [Fact]
    [Trait("Req", "SIG-014")]
    public void Zones_AreOrderedByIdOrdinal()
    {
        var book = new ZoneBook(Defaults);
        book.Add(SupplyRange("S-20260105-0000", 99.0, 99.5));
        book.Add(DemandRange("D-20260107-0000", 100.0, 101.0));
        book.Add(DemandRange("D-20260105-0800", 102.0, 103.0));

        Assert.Equal(new[] { "D-20260105-0800", "D-20260107-0000", "S-20260105-0000" }, Ids(book));
    }

    [Fact]
    [Trait("Req", "SIG-014")]
    public void OnM15Closed_TwoZonesChangeOnOneBar_ReportedInIdOrder()
    {
        // Supply 100.6-101.0 (proximal 100.6) and demand 100.0-100.4 (proximal 100.4), added
        // supply first. One bar with high 100.7 and low 100.3 touches both.
        var book = new ZoneBook(Defaults);
        book.Add(SupplyRange("S-20260105-0000", 100.6, 101.0));
        book.Add(DemandRange("D-20260105-0000", 100.0, 100.4));

        IReadOnlyList<Zone> changed = book.OnM15Closed(M15(0, 100.5, 100.7, 100.3, 100.5));

        Assert.Equal(new[] { "D-20260105-0000", "S-20260105-0000" }, changed.Select(z => z.Id).ToArray());
        Assert.All(changed, z => Assert.Equal(ZoneState.Touched, z.State));
        Assert.Equal(new[] { "D-20260105-0000", "S-20260105-0000" }, book.TouchedZones.Select(t => t.Zone.Id).ToArray());
    }

    [Fact]
    public void Add_ZoneNotFresh_Throws()
    {
        var book = new ZoneBook(Defaults);

        Assert.Throws<ArgumentException>("zone", () => book.Add(DemandZone() with { State = ZoneState.Touched }));
        Assert.Throws<ArgumentException>("zone", () => book.Add(DemandZone() with { TouchBarOpenUtc = Activation }));
        Assert.Throws<ArgumentException>("zone", () => book.Add(DemandZone() with { ExtremeSinceTouch = 100.0 }));
        Assert.Throws<ArgumentException>("zone", () => book.Add(DemandZone() with { Proximal = 99.5 }));
        Assert.Throws<ArgumentException>("zone", () => book.Add(SupplyZone() with { Proximal = 100.6 }));
        Assert.Throws<ArgumentException>("zone", () => book.Add(DemandZone() with { Id = string.Empty }));
        Assert.Throws<ArgumentNullException>("zone", () => book.Add(null!));
        Assert.Empty(book.Zones);
    }

    [Fact]
    public void Constructor_InvalidParameters_Throws()
    {
        Assert.Throws<ArgumentNullException>("parameters", () => new ZoneBook(null!));
        Assert.Throws<ArgumentOutOfRangeException>("MaxZoneAgeBars", () => new ZoneBook(Defaults with { MaxZoneAgeBars = 0 }));
    }

    [Fact]
    public void BarHandlers_NullBar_Throw()
    {
        var book = new ZoneBook(Defaults);

        Assert.Throws<ArgumentNullException>("bar", () => book.OnM15Closed(null!));
        Assert.Throws<ArgumentNullException>("bar", () => book.OnH4Closed(null!));
    }

    // ------------------------------------------------------------------ helpers

    private static void AssertNothingChangesAnyMore(ZoneBook book, string id)
    {
        Assert.Empty(book.Zones);
        Assert.Empty(book.TouchedZones);

        // Bars that would touch, extend and invalidate, a full window and enough H4 bars to expire.
        Assert.Empty(book.OnM15Closed(M15(1, 100.5, 100.6, 100.0, 100.4)));
        Assert.Empty(book.OnM15Closed(M15(2, 100.5, 100.6, 99.0, 99.0)));
        for (int k = 3; k < 30; k++)
        {
            Assert.Empty(book.OnM15Closed(Quiet(k)));
            Assert.Empty(book.EndConfirmationWindows());
        }

        foreach (Bar bar in H4From(Activation.AddHours(8.0), 130))
        {
            Assert.Empty(book.OnH4Closed(bar));
        }

        Assert.Throws<InvalidOperationException>(() => book.MarkSignalled(id));
        Assert.Throws<InvalidOperationException>(() => book.MarkUsed(id));
        Assert.Empty(book.Zones);
    }

    private static ZoneBook BookWith(Zone zone)
    {
        var book = new ZoneBook(Defaults);
        Assert.True(book.Add(zone));
        return book;
    }

    private static Zone DemandZone()
    {
        return new Zone(DemandId, ZoneType.Demand, 100.2, 99.5, Activation, 1.0, ZoneState.Active, null, null);
    }

    private static Zone SupplyZone()
    {
        return new Zone(SupplyId, ZoneType.Supply, 99.8, 100.5, Activation, 1.0, ZoneState.Active, null, null);
    }

    private static Zone DemandRange(string id, double low, double high)
    {
        return new Zone(id, ZoneType.Demand, high, low, Activation, 1.0, ZoneState.Active, null, null);
    }

    private static Zone SupplyRange(string id, double low, double high)
    {
        return new Zone(id, ZoneType.Supply, low, high, Activation, 1.0, ZoneState.Active, null, null);
    }

    private static Bar M15(int index, double open, double high, double low, double close)
    {
        return BarBuilder.One(TimeFrame.M15, Activation.AddMinutes(15.0 * index), open, high, low, close);
    }

    /// <summary>
    /// M15 bar (100.5, 100.6, 100.4, 100.5). For the reference demand zone it neither touches
    /// (low 100.4 above proximal 100.2), nor invalidates, nor lowers the extreme of a touch at 100.0.
    /// </summary>
    private static Bar Quiet(int index)
    {
        return M15(index, 100.5, 100.6, 100.4, 100.5);
    }

    private static IReadOnlyList<Bar> H4From(DateTime start, int count)
    {
        return new BarBuilder(TimeFrame.H4).At(start).Filler(count).Build();
    }

    private static string[] Ids(ZoneBook book)
    {
        return book.Zones.Select(z => z.Id).ToArray();
    }
}
