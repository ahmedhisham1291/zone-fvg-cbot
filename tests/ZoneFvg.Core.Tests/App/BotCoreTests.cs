using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using ZoneFvg.Core.App;
using ZoneFvg.Core.Domain;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Ops;
using ZoneFvg.Core.Recovery;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// <see cref="BotCore"/> behaviour that is not a recovery step: the event boundary (REC-011), the
/// clock rule (REC-012), the states (REC-001), connection handling (REC-002) and the constructor.
/// Data: <see cref="RecoveryData"/>.
/// </summary>
public class BotCoreTests
{
    private static readonly DateTime A = RecoveryData.Activation;

    // ------------------------------------------------------------------ constructor

    [Fact]
    [Trait("Req", "REC-011")]
    public void Constructor_InvalidParameters_ThrowBeforeAnyEvent()
    {
        var h = new BotHarness();
        var store = new MemoryStore();
        var http = new FakeTransport(NewsTestData.Failed());
        var journal = new RecordingJournal();

        Assert.Throws<ArgumentException>(() => new BotCore(RecoveryData.Parameters() with { Symbol = string.Empty }, h.Broker, store, http, journal, true));
        Assert.Throws<ArgumentException>(() => new BotCore(RecoveryData.Parameters() with { Symbol = "USDJPY|x" }, h.Broker, store, http, journal, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BotCore(RecoveryData.Parameters() with { Risk = new ZoneFvg.Core.Risk.RiskParameters() }, h.Broker, store, http, journal, true));
        Assert.Throws<ArgumentException>(() => new BotCore(RecoveryData.Parameters() with { NewsFeedUrl = string.Empty }, h.Broker, store, http, journal, false));
        Assert.Throws<ArgumentNullException>(() => new BotCore(RecoveryData.Parameters() with { Signal = null! }, h.Broker, store, http, journal, true));
        Assert.Throws<ArgumentException>(() => new BotCore(RecoveryData.Parameters() with { MarketHolidays = "25.12." }, h.Broker, store, http, journal, true));
        Assert.Equal("12-25,01-01", new BotParameters().MarketHolidays);
        Assert.Throws<ArgumentNullException>(() => new BotCore(null!, h.Broker, store, http, journal, true));
        Assert.Throws<ArgumentNullException>(() => new BotCore(RecoveryData.Parameters(), null!, store, http, journal, true));
        Assert.Throws<ArgumentNullException>(() => new BotCore(RecoveryData.Parameters(), h.Broker, null!, http, journal, true));
        Assert.Throws<ArgumentNullException>(() => new BotCore(RecoveryData.Parameters(), h.Broker, store, null!, journal, true));
        Assert.Throws<ArgumentNullException>(() => new BotCore(RecoveryData.Parameters(), h.Broker, store, http, null!, true));

        // A backtest needs no feed address; with the news filter off neither does a live run.
        Assert.NotNull(new BotCore(RecoveryData.Parameters() with { NewsFeedUrl = string.Empty }, h.Broker, store, http, journal, true));
        Assert.NotNull(new BotCore(RecoveryData.Parameters(false) with { NewsFeedUrl = string.Empty }, h.Broker, store, http, journal, false));
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void PublicConstructor_StartsInRecoveringAndReachesRunning()
    {
        var h = new BotHarness();
        var core = new BotCore(RecoveryData.Parameters(), h.Broker, h.Store, h.Http, h.Journal, true);
        Assert.Equal(BotState.Recovering, core.State);

        core.OnStart(h.History);

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, h.Journal.Count("state", "from=Recovering to=Running reason=recovery\\scomplete"));
    }

    // ------------------------------------------------------------------ REC-001

    [Fact]
    [Trait("Req", "REC-001")]
    public void Blocked_CalendarUnavailable_SignalIsNotPlaced()
    {
        // An embedded history that does not cover March 2026: the calendar is unavailable.
        var h = new BotHarness();
        BotCore core = h.Start(A, RecoveryData.Parameters(true, "2025-01-08 13:30,USD,High,CPI m/m\n"));
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(BotState.Blocked, core.State);
        Assert.Equal(0, h.Count(BrokerCall.Place));
        Assert.Equal(1, h.Journal.Count("skip", "reason=BotNotRunning"));
        Assert.Equal(1, h.Journal.Count("state", "to=Blocked reason=recovery\\scomplete\\s(calendar\\sunavailable)"));
    }

    [Fact]
    [Trait("Req", "REC-001")]
    public void Running_IsTheOnlyStateThatPlaces()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.FeedTo(RecoveryData.Confirmation);

        Assert.Equal(BotState.Running, core.State);
        Assert.Equal(1, h.Count(BrokerCall.Place));
    }

    // ------------------------------------------------------------------ REC-002 (connection handling)

    [Fact]
    [Trait("Req", "REC-002")]
    public void Disconnect_StopsRequestsAndBarsUntilReconnectRunsRecovery()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A, RecoveryData.WithHeartbeat());
        h.FeedTo(RecoveryData.Confirmation);
        h.FillReferenceOrder(A.AddMinutes(50.0));
        DateTime friday = RecoveryData.At(6, 20, 5);
        h.Broker.AdvanceTo(RecoveryData.At(6, 19, 0));
        core.OnDisconnected();
        int writes = h.Broker.WriteRequestCount;

        // Friday close is due, but no request is sent while disconnected; the heartbeat still runs.
        h.Timer(friday);
        core.OnTradeEvent();
        Bar bar = h.Bars.M15.Single(b => b.OpenTimeUtc == RecoveryData.At(6, 19, 45));
        core.OnBarClosed(TimeFrame.M15, bar);

        Assert.Equal(BotState.Recovering, core.State);
        Assert.Equal(writes, h.Broker.WriteRequestCount);
        string heartbeat = h.Journal.Lines("heartbeat")[^1];
        Assert.StartsWith(JournalFormat.Time(friday) + " heartbeat ", heartbeat, StringComparison.Ordinal);
        Assert.Contains(" state=Recovering ", heartbeat, StringComparison.Ordinal);

        core.OnConnected(h.History);

        Assert.Null(h.ReferencePosition());
        Assert.Equal(1, h.Count(BrokerCall.Close));
        Assert.Equal(BotState.Running, core.State);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-002")]
    public void Disconnect_WhileHalted_StaysHalted()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.Port.PositionsFault = new InvalidOperationException("x");
        for (int i = 0; i < 6; i++)
        {
            core.OnTradeEvent();
        }

        h.Port.PositionsFault = null;
        core.OnDisconnected();
        Assert.Equal(BotState.Halted, core.State);
        core.OnConnected(h.History);
        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(BotState.Halted, core.LastReport!.State);
    }

    // ------------------------------------------------------------------ REC-011

    [Fact]
    [Trait("Req", "REC-011")]
    public void HandlerException_IsJournaledCountedAndAlerted_AndTheCallReturns()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.Port.PositionsFault = new InvalidOperationException("positions\nunavailable");

        core.OnTradeEvent();

        string line = Assert.Single(h.Journal.Lines("error"));
        Assert.Equal("2026-03-04T08:00:00Z error source=OnTradeEvent error=InvalidOperationException:\\spositions\\nunavailable", line);
        string alert = Assert.Single(h.AlertLines(AlertKind.Error));
        Assert.Contains(" notice kind=Error message=handler=OnTradeEvent\\stype=InvalidOperationException\\s", alert, StringComparison.Ordinal);
        Assert.Equal(BotState.Running, core.State);
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void ErrorStorm_HaltsCancelsEntryOrdersAndEndsAtTheNextCestDay()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.FeedTo(RecoveryData.Confirmation);
        Assert.NotNull(h.ReferenceOrder());
        h.Port.PositionsFault = new InvalidOperationException("positions unavailable");
        for (int i = 0; i < 6; i++)
        {
            core.OnTradeEvent();
        }

        Assert.Equal(BotState.Halted, core.State);
        Assert.Null(h.ReferenceOrder());
        Assert.Equal(1, h.Count(BrokerCall.Cancel));

        h.Port.PositionsFault = null;
        h.Timer(RecoveryData.At(4, 22, 59));
        Assert.Equal(BotState.Halted, core.State);

        // 23:00 UTC is 00:00 CET on 5 March.
        h.Timer(RecoveryData.At(4, 23, 0));
        Assert.Equal(BotState.Running, core.State);
        Assert.Empty(core.HaltCauses);
        ScenarioAssert.Global(h.Broker);
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void OnException_IsCountedLikeAHandlerError()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);

        for (int i = 0; i < 5; i++)
        {
            core.OnException(new InvalidOperationException("platform error " + i));
        }

        Assert.Equal(BotState.Running, core.State);
        core.OnException(null!);

        Assert.Equal(BotState.Halted, core.State);
        Assert.Equal(6, h.Journal.Count("error", "source=platform "));
        Assert.Equal(1, h.Journal.Count("error", "source=platform error=ArgumentNullException:"));
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void JournalThatThrows_NeverEscapesAndIsCounted()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.Port.PositionsFault = new InvalidOperationException("positions unavailable");
        h.Journal.Throw = true;

        core.OnTradeEvent();
        core.OnTimer();

        Assert.True(core.UnjournaledErrors >= 2);
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void ServerTimeThatFails_IsRecordedAtTheLastKnownTime()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.Port.TimeFault = new InvalidOperationException("no server time");

        for (int i = 0; i < 6; i++)
        {
            core.OnTimer();
        }

        Assert.Equal(6, h.Journal.Count("error", "source=OnTimer error=InvalidOperationException:\\sno\\sserver\\stime"));
        Assert.Equal(BotState.Halted, core.State);
        Assert.All(h.AlertLines(AlertKind.Error), line => Assert.StartsWith(JournalFormat.Time(A) + " notice kind=Error ", line, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void ServerTimeThatIsNotUtc_IsAnError()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        h.Broker.ReportedKind = DateTimeKind.Unspecified;

        core.OnTimer();

        Assert.Equal(1, h.Journal.Count("error", "source=OnTimer error=InvalidOperationException:\\sThe\\sbroker's\\sserver\\stime\\sis\\snot\\sUTC\\s(REC-012)."));
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void BarNotClosedOrMissing_IsAnErrorAndARepeatedBarIsIgnored()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A);
        Bar m0 = h.Bars.M15.Single(b => b.OpenTimeUtc == A);

        core.OnBarClosed(TimeFrame.M15, m0);
        core.OnBarClosed(TimeFrame.M15, null!);
        h.Broker.AdvanceTo(A.AddMinutes(15.0));
        core.OnBarClosed(TimeFrame.M15, m0);
        core.OnBarClosed(TimeFrame.M15, m0);

        Assert.Equal(1, h.Journal.Count("error", "source=OnBarClosed error=InvalidOperationException:\\sThe\\sM15\\sbar\\sopening\\s2026-03-04\\s08:00\\shas\\snot\\sclosed"));
        Assert.Equal(1, h.Journal.Count("error", "source=OnBarClosed error=ArgumentNullException:"));
        Assert.Equal(1, h.Journal.Count("bar", "event=ignored timeframe=M15 open=2026-03-04\\s08:00 reason=already\\sprocessed"));
        Assert.Equal(ZoneState.Touched, Assert.Single(core.Zones).State);
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void OnStartWithoutHistory_IsAnError()
    {
        var h = new BotHarness();
        BotCore core = h.NewCore();

        core.OnStart(null!);
        core.OnConnected(null!);

        Assert.Equal(2, h.Journal.Count("error", "error=ArgumentNullException:"));
        Assert.Equal(BotState.Recovering, core.State);
    }

    [Fact]
    [Trait("Req", "REC-011")]
    public void Timer_HeartbeatAndInactivityCheckInEveryState()
    {
        var h = new BotHarness();
        BotCore core = h.Start(A, RecoveryData.WithHeartbeat());
        var historyReads = new List<DateTime>();
        h.Port.History = since =>
        {
            historyReads.Add(since);
            return h.Broker.GetHistory(since);
        };
        h.Timer(A.AddMinutes(1.0));
        core.OnDisconnected();
        h.Timer(A.AddMinutes(6.0));

        // The heartbeat is called every 5 minutes in every state (OPS-005); the harness's transport fails it, so it is journaled.
        IReadOnlyList<string> heartbeats = h.Journal.Lines("heartbeat");
        Assert.Equal(2, heartbeats.Count);
        Assert.StartsWith(JournalFormat.Time(A.AddMinutes(1.0)) + " heartbeat ", heartbeats[0], StringComparison.Ordinal);
        Assert.Contains(" state=Running ", heartbeats[0], StringComparison.Ordinal);
        Assert.StartsWith(JournalFormat.Time(A.AddMinutes(6.0)) + " heartbeat ", heartbeats[1], StringComparison.Ordinal);
        Assert.Contains(" state=Recovering ", heartbeats[1], StringComparison.Ordinal);

        // The inactivity check reads 30 days of history at every timer event, also while disconnected (OPS-006).
        Assert.Contains(A.AddMinutes(1.0).AddDays(-30.0), historyReads);
        Assert.Contains(A.AddMinutes(6.0).AddDays(-30.0), historyReads);
        Assert.Equal(A.AddMinutes(1.0).ToString("o", System.Globalization.CultureInfo.InvariantCulture), h.Store.Values[RecoveryService.LastSeenKey]);
    }

    // ------------------------------------------------------------------ REC-012

    [Fact]
    [Trait("Req", "REC-012")]
    public void CoreAssembly_ContainsNoCallToTheMachineClockOrStopwatch()
    {
        // The coverage run injects its own tracker types (namespace Coverlet.*) into the Core assembly; they are
        // not compiled from Core sources and are left out. Every type from the Core sources is scanned.
        Type[] coreTypes = typeof(BotCore).Assembly.GetTypes()
            .Where(type => type.Namespace == null || !type.Namespace.StartsWith("Coverlet", StringComparison.Ordinal))
            .ToArray();
        IReadOnlyList<string> found = ClockScan.Find(coreTypes);

        Assert.Contains(coreTypes, type => type == typeof(BotCore));
        Assert.True(coreTypes.Length > 100, "only " + coreTypes.Length + " types scanned");
        Assert.Empty(found);
    }

    [Fact]
    [Trait("Req", "REC-012")]
    public void ClockScan_FindsAStopwatchCallWhereThereIsOne()
    {
        IReadOnlyList<string> found = ClockScan.Find(new[] { typeof(ClockScanControl) });

        Assert.Contains(found, entry => entry.StartsWith("ClockScanControl.Measure calls System.Diagnostics.Stopwatch.GetTimestamp", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "REC-012")]
    public void ClockScan_RecognisesEveryClockMember()
    {
        Assert.True(ClockScan.IsClock(typeof(DateTime).GetProperty("Now")!.GetMethod!));
        Assert.True(ClockScan.IsClock(typeof(DateTime).GetProperty("UtcNow")!.GetMethod!));
        Assert.True(ClockScan.IsClock(typeof(DateTime).GetProperty("Today")!.GetMethod!));
        Assert.True(ClockScan.IsClock(typeof(DateTimeOffset).GetProperty("UtcNow")!.GetMethod!));
        Assert.True(ClockScan.IsClock(typeof(System.Diagnostics.Stopwatch).GetMethod("StartNew")!));
        Assert.False(ClockScan.IsClock(typeof(DateTime).GetProperty("Kind")!.GetMethod!));
        Assert.False(ClockScan.IsClock(typeof(DateTime).GetMethod("AddMinutes")!));
    }
}

/// <summary>A method that measures wall-clock time, as the positive control of <see cref="ClockScan"/>. Never called.</summary>
public static class ClockScanControl
{
    public static long Measure()
    {
        return System.Diagnostics.Stopwatch.GetTimestamp();
    }
}

/// <summary>
/// Scans the IL of every method body of an assembly for calls to DateTime.get_Now,
/// DateTime.get_UtcNow, DateTime.get_Today, DateTimeOffset.get_Now, DateTimeOffset.get_UtcNow and
/// any member of System.Diagnostics.Stopwatch (REC-012). Operands are decoded with the opcode
/// table of System.Reflection.Emit so that operand bytes are never read as opcodes.
/// </summary>
public static class ClockScan
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    public static IReadOnlyList<string> Find(Assembly assembly)
    {
        return Find(assembly.GetTypes());
    }

    public static IReadOnlyList<string> Find(IEnumerable<Type> types)
    {
        var found = new List<string>();
        foreach (Type type in types)
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            IEnumerable<MethodBase> methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (MethodBase method in methods)
            {
                byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
                if (il != null)
                {
                    Scan(method, il, found);
                }
            }
        }

        return found;
    }

    private static void Scan(MethodBase method, byte[] il, List<string> found)
    {
        Type[]? typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        Type[]? methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
        int position = 0;
        while (position < il.Length)
        {
            short value = il[position] == 0xFE ? (short)(0xFE00 | il[position + 1]) : il[position];
            position += il[position] == 0xFE ? 2 : 1;
            OpCode code = OpCodesByValue[value];
            if (code.OperandType == OperandType.InlineMethod)
            {
                int token = BitConverter.ToInt32(il, position);
                MethodBase? target = method.Module.ResolveMethod(token, typeArguments, methodArguments);
                if (target != null && IsClock(target))
                {
                    found.Add(method.DeclaringType?.Name + "." + method.Name + " calls " + target.DeclaringType?.FullName + "." + target.Name);
                }
            }

            position += OperandSize(code.OperandType, il, position);
        }
    }

    public static bool IsClock(MethodBase target)
    {
        Type? type = target.DeclaringType;
        if (type == typeof(System.Diagnostics.Stopwatch))
        {
            return true;
        }

        bool clockType = type == typeof(DateTime) || type == typeof(DateTimeOffset);
        return clockType && (target.Name == "get_Now" || target.Name == "get_UtcNow" || target.Name == "get_Today");
    }

    private static int OperandSize(OperandType type, byte[] il, int position)
    {
        switch (type)
        {
            case OperandType.InlineNone:
                return 0;
            case OperandType.ShortInlineBrTarget:
            case OperandType.ShortInlineI:
            case OperandType.ShortInlineVar:
                return 1;
            case OperandType.InlineVar:
                return 2;
            case OperandType.InlineI8:
            case OperandType.InlineR:
                return 8;
            case OperandType.InlineSwitch:
                return 4 + (4 * BitConverter.ToInt32(il, position));
            default:
                return 4;
        }
    }
}
