using Xunit;
using ZoneFvg.Core.Execution;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>OPS-007: secrets never appear in journal lines, alert texts or exception messages.</summary>
public class SecretMaskerTests
{
    [Fact]
    [Trait("Req", "OPS-007")]
    public void Apply_TextWithEverySecret_ReplacesEachWithStars()
    {
        SecretMasker masker = SecretMasker.For(OpsData.WithSecrets);

        string masked = masker.Apply("token " + OpsData.Token + " chat " + OpsData.ChatId + " url " + OpsData.HeartbeatUrl + "?x=1");

        Assert.Equal("token *** chat *** url ***?x=1", masked);
        Assert.Equal(3, masker.Count);
    }

    [Fact]
    [Trait("Req", "OPS-007")]
    public void Apply_SecretInsideLongerSecret_MasksTheLongerOneWhole()
    {
        var masker = new SecretMasker(new[] { "abc", "abcdef" });

        Assert.Equal("x***x and ***", masker.Apply("xabcdefx and abc"));
    }

    [Fact]
    [Trait("Req", "OPS-007")]
    public void Constructor_EmptyAndNullSecrets_AreIgnored()
    {
        var masker = new SecretMasker(new string?[] { null, string.Empty, "k1", "k1" });

        Assert.Equal(1, masker.Count);
        Assert.Equal("a *** b", masker.Apply("a k1 b"));
        Assert.Equal(0, SecretMasker.For(new OpsParameters()).Count);
        Assert.Equal("nothing to hide", SecretMasker.For(new OpsParameters()).Apply("nothing to hide"));
        Assert.Equal(string.Empty, masker.Apply((string?)null));
        Assert.Throws<ArgumentNullException>(() => new SecretMasker(null!));
        Assert.Throws<ArgumentNullException>(() => SecretMasker.For(null!));
    }

    [Fact]
    [Trait("Req", "OPS-007")]
    public void Apply_ExceptionWithUrlAndInnerToken_MasksBoth()
    {
        SecretMasker masker = SecretMasker.For(OpsData.WithSecrets);
        var exception = new InvalidOperationException("GET " + OpsData.HeartbeatUrl + " failed", new ArgumentException("bad bot" + OpsData.Token));

        Assert.Equal("InvalidOperationException: GET *** failed ---> ArgumentException: bad bot***", masker.Apply(exception));
        Assert.Equal(string.Empty, masker.Apply((Exception?)null));
    }

    [Fact]
    [Trait("Req", "OPS-007")]
    public void Journal_LineContainingToken_IsMasked()
    {
        var sink = new RecordingSink();
        Journal journal = OpsData.NewJournal(sink);

        journal.State(OpsData.T0, BotState.Running, BotState.Blocked, "sent to bot" + OpsData.Token);
        journal.Error(OpsData.T0, "OnTimer", new InvalidOperationException("timeout calling " + OpsData.HeartbeatUrl));
        journal.Error(OpsData.T0, "chat " + OpsData.ChatId, "x");

        Assert.Equal("2026-01-14T10:00:00Z state from=Running to=Blocked reason=sent\\sto\\sbot***", sink.Lines[0].Line);
        Assert.Equal("2026-01-14T10:00:00Z error source=OnTimer error=InvalidOperationException:\\stimeout\\scalling\\s***", sink.Lines[1].Line);
        Assert.Equal("2026-01-14T10:00:00Z error source=chat\\s*** error=x", sink.Lines[2].Line);
        Assert.DoesNotContain(sink.Lines, l => OpsData.ContainsSecret(l.Line));
    }

    [Fact]
    [Trait("Req", "OPS-007")]
    public void Journal_SinkThrowsWithSecretInMessage_KeepsOnlyMaskedMessage()
    {
        var sink = new RecordingSink { ThrowMessage = "cannot write " + OpsData.Token };
        Journal journal = OpsData.NewJournal(sink);

        journal.Error(OpsData.T0, "x", "y");

        Assert.Equal("InvalidOperationException: cannot write ***", journal.LastWriteFailure);
    }

    [Theory]
    [Trait("Req", "OPS-007")]
    [InlineData("TelegramBotToken")]
    [InlineData("TelegramChatId")]
    [InlineData("HeartbeatUrl")]
    public void Validate_InvalidSecret_MessageNeverContainsTheValue(string name)
    {
        const string value = "secret value/with space";
        OpsParameters p = name switch
        {
            "TelegramBotToken" => OpsData.WithSecrets with { TelegramBotToken = value },
            "TelegramChatId" => OpsData.WithSecrets with { TelegramChatId = value },
            _ => OpsData.WithSecrets with { HeartbeatUrl = value },
        };

        ArgumentOutOfRangeException e = Assert.Throws<ArgumentOutOfRangeException>(() => p.Validate());

        Assert.Equal(name, e.ParamName);
        Assert.DoesNotContain("secret", e.Message, StringComparison.Ordinal);
        Assert.Null(e.ActualValue);
    }
}
