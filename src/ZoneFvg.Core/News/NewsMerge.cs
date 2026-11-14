using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZoneFvg.Core.News;

/// <summary>
/// The merge rule for several feeds (NWS-002). Two events of different feeds with the same
/// currency and times at most <see cref="SameEventMinutes"/> minutes apart match. Matched events
/// form one group (if A matches B and B matches C, all three are one group). A group is high
/// impact if any member is high. Events of the same feed never match directly; events without a
/// time never match.
/// </summary>
/// <remarks>
/// Owner decision of 2026-11-14: merging is for logging and impact only. <see cref="Merge(IReadOnlyList{IReadOnlyList{NewsEvent}})"/>
/// gives one logged event per group; <see cref="WindowEvents"/> keeps every event at its own
/// time for the windows, so merging never shortens the time in which entries are blocked.
/// </remarks>
public static class NewsMerge
{
    /// <summary>Largest time difference, in minutes, at which two feeds' events are the same event (NWS-002).</summary>
    public const int SameEventMinutes = 5;

    /// <summary>Merges the events of two feeds for logging (NWS-002).</summary>
    /// <param name="first">Events of the first feed.</param>
    /// <param name="second">Events of the second feed.</param>
    /// <returns>One event per group, in <see cref="NewsEventOrder"/> order.</returns>
    /// <exception cref="ArgumentNullException">A list or an event is null.</exception>
    public static IReadOnlyList<NewsEvent> Merge(IReadOnlyList<NewsEvent> first, IReadOnlyList<NewsEvent> second)
    {
        if (first == null)
        {
            throw new ArgumentNullException(nameof(first));
        }

        if (second == null)
        {
            throw new ArgumentNullException(nameof(second));
        }

        return Merge(new[] { first, second });
    }

    /// <summary>
    /// Merges the events of the feeds for logging (NWS-002). A group becomes one event: the
    /// earliest time, high impact if any member is high, otherwise the strongest rating, the title
    /// of the earliest member and the source names of all members, sorted and joined with "+".
    /// </summary>
    /// <param name="sources">Events of each feed, in a fixed feed order.</param>
    /// <returns>One event per group, in <see cref="NewsEventOrder"/> order.</returns>
    /// <exception cref="ArgumentNullException">A list or an event is null.</exception>
    public static IReadOnlyList<NewsEvent> Merge(IReadOnlyList<IReadOnlyList<NewsEvent>> sources)
    {
        List<NewsEvent> all = Flatten(sources, out List<int> feedOf);
        var merged = new List<NewsEvent>(all.Count);
        foreach (List<int>? group in Groups(all, feedOf))
        {
            if (group != null)
            {
                merged.Add(group.Count == 1 ? all[group[0]] : Combine(all, group));
            }
        }

        merged.Sort(NewsEventOrder.Instance);
        return merged;
    }

    /// <summary>
    /// The events that open windows (NWS-002): every event at its own time, with high impact when
    /// any member of its group is high, otherwise unchanged.
    /// </summary>
    /// <param name="sources">Events of each feed, in a fixed feed order.</param>
    /// <returns>All events, in <see cref="NewsEventOrder"/> order.</returns>
    /// <exception cref="ArgumentNullException">A list or an event is null.</exception>
    public static IReadOnlyList<NewsEvent> WindowEvents(IReadOnlyList<IReadOnlyList<NewsEvent>> sources)
    {
        List<NewsEvent> all = Flatten(sources, out List<int> feedOf);
        var result = new List<NewsEvent>(all.Count);
        foreach (List<int>? group in Groups(all, feedOf))
        {
            if (group == null)
            {
                continue;
            }

            bool high = group.Exists(i => all[i].Impact == NewsImpact.High);
            foreach (int index in group)
            {
                NewsEvent member = all[index];
                result.Add(high && member.Impact != NewsImpact.High
                    ? new NewsEvent(member.TimeUtc, member.Currency, NewsImpact.High, member.Title, member.Source)
                    : member);
            }
        }

        result.Sort(NewsEventOrder.Instance);
        return result;
    }

    private static List<NewsEvent> Flatten(IReadOnlyList<IReadOnlyList<NewsEvent>> sources, out List<int> feedOf)
    {
        if (sources == null)
        {
            throw new ArgumentNullException(nameof(sources));
        }

        var all = new List<NewsEvent>();
        feedOf = new List<int>();
        for (int feed = 0; feed < sources.Count; feed++)
        {
            IReadOnlyList<NewsEvent> source = sources[feed] ?? throw new ArgumentNullException(nameof(sources), "A source list must not be null.");
            foreach (NewsEvent newsEvent in source)
            {
                all.Add(newsEvent ?? throw new ArgumentNullException(nameof(sources), "An event must not be null."));
                feedOf.Add(feed);
            }
        }

        return all;
    }

    private static List<int>?[] Groups(List<NewsEvent> all, List<int> feedOf)
    {
        int[] parent = new int[all.Count];
        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        TimeSpan limit = TimeSpan.FromMinutes(SameEventMinutes);
        for (int i = 0; i < all.Count; i++)
        {
            for (int j = i + 1; j < all.Count; j++)
            {
                if (feedOf[i] != feedOf[j] && SameEvent(all[i], all[j], limit))
                {
                    Union(parent, i, j);
                }
            }
        }

        var groups = new List<int>?[all.Count];
        for (int i = 0; i < all.Count; i++)
        {
            int root = Find(parent, i);
            (groups[root] ??= new List<int>()).Add(i);
        }

        return groups;
    }

    private static bool SameEvent(NewsEvent a, NewsEvent b, TimeSpan limit)
    {
        if (!a.TimeUtc.HasValue || !b.TimeUtc.HasValue)
        {
            return false;
        }

        TimeSpan difference = a.TimeUtc.Value - b.TimeUtc.Value;
        return difference.Duration() <= limit
            && string.Equals(a.Currency.Trim(), b.Currency.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static int Find(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            i = parent[i];
        }

        return i;
    }

    private static void Union(int[] parent, int a, int b)
    {
        int rootA = Find(parent, a);
        int rootB = Find(parent, b);
        if (rootA != rootB)
        {
            parent[Math.Max(rootA, rootB)] = Math.Min(rootA, rootB);
        }
    }

    private static NewsEvent Combine(List<NewsEvent> all, List<int> group)
    {
        NewsEvent earliest = all[group[0]];
        NewsImpact impact = earliest.Impact;
        var sources = new SortedSet<string>(StringComparer.Ordinal);
        foreach (int index in group)
        {
            NewsEvent member = all[index];
            if (member.TimeUtc!.Value < earliest.TimeUtc!.Value)
            {
                earliest = member;
            }

            impact = Rank(member.Impact) > Rank(impact) ? member.Impact : impact;
            sources.Add(member.Source);
        }

        var source = new StringBuilder();
        foreach (string name in sources)
        {
            source.Append(source.Length == 0 ? string.Empty : "+").Append(name);
        }

        return new NewsEvent(earliest.TimeUtc, earliest.Currency.Trim().ToUpper(CultureInfo.InvariantCulture), impact, earliest.Title, source.ToString());
    }

    private static int Rank(NewsImpact impact)
    {
        switch (impact)
        {
            case NewsImpact.High:
                return 4;
            case NewsImpact.Medium:
                return 3;
            case NewsImpact.Low:
                return 2;
            case NewsImpact.Unknown:
                return 1;
            default:
                return 0;
        }
    }
}
