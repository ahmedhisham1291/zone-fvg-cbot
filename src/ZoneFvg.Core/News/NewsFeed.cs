using System;

namespace ZoneFvg.Core.News;

/// <summary>
/// A live feed for <see cref="NewsService"/>: where to download it and how to parse it
/// (NWS-003, NWS-006). The adapter supplies the address.
/// </summary>
public sealed class NewsFeed
{
    /// <summary>Creates a feed (NWS-006).</summary>
    /// <param name="url">Address of the document.</param>
    /// <param name="parser">Parser of the document.</param>
    /// <exception cref="ArgumentException">The address is empty or the parser has no source name.</exception>
    /// <exception cref="ArgumentNullException">The parser is null.</exception>
    public NewsFeed(string url, INewsFeedParser parser)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("The feed address must not be empty.", nameof(url));
        }

        Parser = parser ?? throw new ArgumentNullException(nameof(parser));
        if (string.IsNullOrWhiteSpace(parser.Source))
        {
            throw new ArgumentException("The parser must name its source.", nameof(parser));
        }

        Url = url;
        Source = parser.Source;
    }

    /// <summary>Address of the document.</summary>
    public string Url { get; }

    /// <summary>Parser of the document.</summary>
    public INewsFeedParser Parser { get; }

    /// <summary>Name of the source, read from the parser once at construction.</summary>
    public string Source { get; }
}
