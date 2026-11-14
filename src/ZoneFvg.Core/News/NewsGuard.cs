using System;

namespace ZoneFvg.Core.News;

/// <summary>Argument checks shared by the news module.</summary>
internal static class NewsGuard
{
    internal static void RequireUtc(DateTime utc, string name)
    {
        if (utc.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time must be a UTC time.", name);
        }
    }
}
