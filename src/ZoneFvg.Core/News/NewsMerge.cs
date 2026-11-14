using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZoneFvg.Core.News;

/// <summary>
/// The merge rule for two feeds (NWS-002). An event of the first list and an event of the second
/// list with the same currency and times at most <see cref="SameEventMinutes"/> minutes apart
/// are the same event. Linked events form one group (if A matches B and B matches C, all three
/// are one event). A group becomes one event: the earliest time, high impact if any member is
/// high, otherwise the strongest rating, the title of the earliest member and the source names
/// of all members joined with "+". Events of the same list are never linked directly. Events
/// without a time are kept unchanged.
/// </summary>
public static class NewsMerge
{
    /// <summary>Largest time difference, in minutes, at which two feeds' events are the same event (NWS-002).</summary>
    public const int SameEventMinutes = 5;

    /// <summary>Merges the events of two feeds (NWS-002).</summary>
    /// <param name="first">Events of the first feed.</param>
    /// <param name="second">Events of the second feed.</param>
    /// <returns>The merged events in <see cref="NewsEventOrder"/> order.</returns>
    /// <exception cref="ArgumentNullException">A list is null.</exception>
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

        var all = new List<NewsEvent>(first.Count + second.Count);
        all.AddRange(first);
        all.AddRange(second);
        int[] parent = new int[all.Count];
        for (int i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        TimeSpan limit = TimeSpan.FromMinutes(SameEventMinutes);
        for (int i = 0; i < first.Count; i++)
        {
            for (int j = first.Count; j < all.Count; j++)
            {
                if (SameEvent(all[i], all[j], limit))
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

        var merged = new List<NewsEvent>(all.Count);
        foreach (List<int>? group in groups)
        {
            if (group != null)
            {
                merged.Add(group.Count == 1 ? all[group[0]] : Combine(all, group));
            }
        }

        merged.Sort(NewsEventOrder.Instance);
        return merged;
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
