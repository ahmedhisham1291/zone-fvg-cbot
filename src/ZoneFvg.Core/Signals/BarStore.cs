using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using ZoneFvg.Core.Domain;
using static ZoneFvg.Core.Domain.Invariant;

namespace ZoneFvg.Core.Signals;

/// <summary>
/// Bar intake of the signal layer: one bounded, ordered buffer of closed bars per timeframe
/// (SIG-001, SIG-004). Each buffer holds the warm-up requirement of its timeframe (SIG-020)
/// plus <see cref="CapacityMargin"/> bars; when it is full the oldest bar is dropped.
/// </summary>
/// <remarks>
/// Only closed bars are ever stored, and each bar exactly once: a bar that is not strictly newer
/// than the newest stored bar of its timeframe is rejected. When bars of several timeframes close
/// at the same instant, the caller passes them in the order given by <see cref="BarIntakeOrder"/>.
/// </remarks>
public sealed class BarStore
{
    /// <summary>Bars kept per timeframe on top of the warm-up requirement.</summary>
    public const int CapacityMargin = 16;

    private readonly IReadOnlyDictionary<TimeFrame, int> _warmupBars;
    private readonly RingBuffer _d1;
    private readonly RingBuffer _h4;
    private readonly RingBuffer _m15;

    /// <summary>Creates an empty store sized for the given parameters (SIG-020).</summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public BarStore(SignalParameters parameters)
        : this(parameters, null)
    {
    }

    /// <summary>
    /// Creates an empty store sized for the given parameters (SIG-020, SIG-028): the warm-up of
    /// <see cref="WarmupCalculator.For(SignalParameters, BreakoutParameters)"/>, so that with the
    /// breakout trend filter on the D1 buffer holds the window of the slow EMA.
    /// </summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <param name="breakout">The breakout parameters, or null in ZoneFvg mode.</param>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public BarStore(SignalParameters parameters, BreakoutParameters? breakout)
        : this(parameters, breakout, null)
    {
    }

    /// <summary>
    /// Creates an empty store sized for the given parameters (SIG-020, SIG-028, SIG-034): the
    /// warm-up of <see cref="WarmupCalculator.For(SignalParameters, BreakoutParameters, SessionRangeParameters)"/>.
    /// </summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <param name="breakout">The breakout parameters, or null outside Breakout mode.</param>
    /// <param name="sessionRange">The session range parameters, or null outside SessionRange mode.</param>
    /// <exception cref="ArgumentException">Breakout and session range parameters are both set.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public BarStore(SignalParameters parameters, BreakoutParameters? breakout, SessionRangeParameters? sessionRange)
        : this(parameters, breakout, sessionRange, null)
    {
    }

    /// <summary>
    /// Creates an empty store sized for the given parameters (SIG-020, SIG-028, SIG-034, SIG-041):
    /// the warm-up of <see cref="WarmupCalculator.For(SignalParameters, BreakoutParameters, SessionRangeParameters, ChochParameters)"/>.
    /// </summary>
    /// <param name="parameters">The signal parameters. They are validated first.</param>
    /// <param name="breakout">The breakout parameters, or null outside Breakout mode.</param>
    /// <param name="sessionRange">The session range parameters, or null outside SessionRange mode.</param>
    /// <param name="choch">The Choch parameters, or null outside the Choch model.</param>
    /// <exception cref="ArgumentException">More than one of breakout, session range and Choch parameters is set.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A parameter is out of range.</exception>
    public BarStore(SignalParameters parameters, BreakoutParameters? breakout, SessionRangeParameters? sessionRange, ChochParameters? choch)
    {
        _warmupBars = WarmupCalculator.For(parameters, breakout, sessionRange, choch);
        _d1 = new RingBuffer(_warmupBars[TimeFrame.D1] + CapacityMargin);
        _h4 = new RingBuffer(_warmupBars[TimeFrame.H4] + CapacityMargin);
        _m15 = new RingBuffer(_warmupBars[TimeFrame.M15] + CapacityMargin);
    }

    /// <summary>Closed bars needed per timeframe before the signal layer is ready (SIG-020). Read it by key.</summary>
    public IReadOnlyDictionary<TimeFrame, int> WarmupBars => _warmupBars;

    /// <summary>
    /// True once every timeframe holds at least its warm-up requirement (SIG-020). Until then the
    /// signal layer is not ready and must produce no zone and no signal.
    /// </summary>
    public bool IsReady
    {
        get
        {
            bool d1Ready = _d1.Count >= _warmupBars[TimeFrame.D1];
            bool h4Ready = _h4.Count >= _warmupBars[TimeFrame.H4];
            bool m15Ready = _m15.Count >= _warmupBars[TimeFrame.M15];
            return d1Ready && h4Ready && m15Ready;
        }
    }

    /// <summary>Largest number of bars kept for the timeframe: its warm-up requirement plus the margin.</summary>
    /// <param name="timeFrame">The timeframe.</param>
    /// <returns>The buffer capacity in bars.</returns>
    public int Capacity(TimeFrame timeFrame)
    {
        return BufferFor(timeFrame).Capacity;
    }

    /// <summary>Number of closed bars currently stored for the timeframe.</summary>
    /// <param name="timeFrame">The timeframe.</param>
    /// <returns>The number of stored bars.</returns>
    public int Count(TimeFrame timeFrame)
    {
        return BufferFor(timeFrame).Count;
    }

    /// <summary>
    /// The stored closed bars of the timeframe, oldest first; the last element is the newest
    /// closed bar. The list is a read-only live view: it follows later calls to
    /// <see cref="Add"/>, and positions shift by one when the oldest bar is dropped.
    /// </summary>
    /// <param name="timeFrame">The timeframe.</param>
    /// <returns>A read-only view of the buffer.</returns>
    public IReadOnlyList<Bar> Bars(TimeFrame timeFrame)
    {
        return BufferFor(timeFrame);
    }

    /// <summary>
    /// Stores a closed bar (SIG-001, SIG-004). Bars of one timeframe must arrive in strictly
    /// increasing open-time order. A duplicate or out-of-order bar is rejected with an
    /// <see cref="InvalidOperationException"/> and the store is left exactly as it was.
    /// </summary>
    /// <param name="timeFrame">The timeframe of the bar.</param>
    /// <param name="bar">The closed bar.</param>
    /// <exception cref="InvalidOperationException">The bar does not open after the newest stored bar of its timeframe.</exception>
    public void Add(TimeFrame timeFrame, Bar bar)
    {
        RingBuffer buffer = BufferFor(timeFrame);
        ArgumentNullException.ThrowIfNull(bar);

        if (buffer.Count > 0)
        {
            Bar newest = buffer[buffer.Count - 1];
            if (bar.OpenTimeUtc <= newest.OpenTimeUtc)
            {
                string rejected = "the " + timeFrame + " bar opening at " + Second(bar.OpenTimeUtc) + " UTC";
                string stored = "the newest stored " + timeFrame + " bar, which opens at " + Second(newest.OpenTimeUtc) + " UTC";
                throw new InvalidOperationException("Bar rejected (SIG-004): " + rejected + " is not after " + stored + ".");
            }
        }

        buffer.Append(bar);
    }

    /// <summary>Returns the n-th most recent closed bar of the timeframe; 0 is the newest.</summary>
    /// <param name="timeFrame">The timeframe.</param>
    /// <param name="n">How many bars back from the newest closed bar. Must be at least 0 and below the stored count.</param>
    /// <returns>The closed bar.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than n + 1 bars are stored, or n is negative.</exception>
    internal Bar Last(TimeFrame timeFrame, int n)
    {
        RingBuffer buffer = BufferFor(timeFrame);
        if (n < 0 || n >= buffer.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "No closed bar is stored at that distance from the newest bar.");
        }

        return buffer[buffer.Count - 1 - n];
    }

    private RingBuffer BufferFor(TimeFrame timeFrame)
    {
        switch (timeFrame)
        {
            case TimeFrame.D1:
                return _d1;
            case TimeFrame.H4:
                return _h4;
            case TimeFrame.M15:
                return _m15;
            default:
                throw new ArgumentOutOfRangeException(nameof(timeFrame), "Unknown timeframe.");
        }
    }

    /// <summary>Fixed-capacity buffer that overwrites its oldest bar when full. Index 0 is the oldest bar.</summary>
    private sealed class RingBuffer : IReadOnlyList<Bar>
    {
        private readonly Bar[] _items;
        private int _start;
        private int _count;

        public RingBuffer(int capacity)
        {
            _items = new Bar[capacity];
        }

        public int Capacity => _items.Length;

        public int Count => _count;

        public Bar this[int index]
        {
            get
            {
                if (index < 0 || index >= _count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index), "The index is outside the stored bars.");
                }

                return _items[(_start + index) % _items.Length];
            }
        }

        public void Append(Bar bar)
        {
            if (_count < _items.Length)
            {
                _items[(_start + _count) % _items.Length] = bar;
                _count++;
            }
            else
            {
                _items[_start] = bar;
                _start = (_start + 1) % _items.Length;
            }
        }

        /// <summary>Enumerates a snapshot taken now, so a later <see cref="Append"/> cannot disturb the enumeration.</summary>
        public IEnumerator<Bar> GetEnumerator()
        {
            var snapshot = new List<Bar>(_count);
            for (int index = 0; index < _count; index++)
            {
                snapshot.Add(this[index]);
            }

            return snapshot.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
