using System;

namespace ZoneFvg.Core.News;

/// <summary>
/// Parser of one live news feed (NWS-003). A second feed plugs into <see cref="NewsService"/>
/// through this interface and the same <see cref="NewsParseResult"/>.
/// </summary>
public interface INewsFeedParser
{
    /// <summary>Name of the source. Used in events, store keys and reports. Unique per service.</summary>
    string Source { get; }

    /// <summary>
    /// Parses and validates one downloaded document (NWS-003, NWS-005). Never throws for bad
    /// input: a bad document is returned as a rejected result with its reason.
    /// </summary>
    /// <param name="body">The downloaded document.</param>
    /// <param name="nowUtc">Server time of the download, kind UTC; decides which period must be covered.</param>
    /// <returns>The parse result.</returns>
    NewsParseResult Parse(string body, DateTime nowUtc);
}
