using Xunit;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Platform storage keys (NWS-006, NWS-007): cTrader local storage accepts only Latin letters,
/// digits and inner spaces in a key, so the adapter stores every port key under its encoded form.
/// </summary>
public class StorageKeyTests
{
    [Theory]
    [Trait("Req", "NWS-007")]
    [InlineData("journal.trades", "journalQ2Etrades")]
    [InlineData("app.lastSeenUtc", "appQ2ElastSeenUtc")]
    [InlineData("news.forexfactory", "newsQ2Eforexfactory")]
    [InlineData("journal.plan.12345", "journalQ2EplanQ2E12345")]
    [InlineData("risk.budgetExhaustedDay", "riskQ2EbudgetExhaustedDay")]
    [InlineData("abc123XYZ", "abc123XYZ")]
    [InlineData("Q", "Q51")]
    [InlineData("a_b-c:d e", "aQ5FbQ2DcQ3AdQ20e")]
    public void Encode_KnownKey_GivesTheExpectedPlatformKey(string key, string expected)
    {
        Assert.Equal(expected, StorageKey.Encode(key));
    }

    [Theory]
    [Trait("Req", "NWS-007")]
    [InlineData("journal.trades")]
    [InlineData("app.starts")]
    [InlineData("news.attempt.forexfactory")]
    [InlineData(" lead and trail ")]
    [InlineData("Q.q~Z")]
    public void Encode_AnyKey_HasOnlyLatinLettersAndDigits(string key)
    {
        string encoded = StorageKey.Encode(key);

        Assert.NotEmpty(encoded);
        Assert.All(encoded, c => Assert.True((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'), "unexpected character " + c));
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Encode_DifferentKeys_GiveDifferentPlatformKeys()
    {
        string[] keys =
        {
            "a.b", "aQ2Eb", "a_b", "aQb", "aQ51b", "Q", "Q51", "q", ".", "Q2E", "a b", "aQ20b",
            "journal.plan.1", "journal.plan.11", "journalQ2Eplan.1",
        };

        string[] encoded = keys.Select(StorageKey.Encode).ToArray();

        Assert.Equal(keys.Length, encoded.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Encode_SameKey_GivesTheSameResultEveryTime()
    {
        Assert.Equal(StorageKey.Encode("news.forexfactory"), StorageKey.Encode("news.forexfactory"));
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Encode_EmptyKey_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => StorageKey.Encode(string.Empty));
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Encode_NullKey_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => StorageKey.Encode(null!));
    }

    [Fact]
    [Trait("Req", "NWS-007")]
    public void Encode_NonAsciiCharacter_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => StorageKey.Encode("news.é"));
    }
}
