using Xunit;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>OPS-004 and OPS-005: the operations parameters and their checks.</summary>
public class OpsParametersTests
{
    [Fact]
    [Trait("Req", "OPS-004")]
    public void Defaults_AreEmptySecretsAndValid()
    {
        var p = new OpsParameters();

        p.Validate();
        Assert.Equal(string.Empty, p.HeartbeatUrl);
        Assert.Equal(string.Empty, p.TelegramBotToken);
        Assert.Equal(string.Empty, p.TelegramChatId);
        Assert.Equal(10.0, p.ProfitTargetPercent);
        Assert.False(p.AlertsEnabled);
        Assert.False(p.HeartbeatEnabled);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void FixedValues_AreTheDocumentedOnes()
    {
        Assert.Equal(5, OpsParameters.HeartbeatIntervalMinutes);
        Assert.Equal(21, OpsParameters.InactivityAlertDays);
        Assert.Equal(3, OpsParameters.InactivityRepeatDays);
        Assert.Equal(1000, OpsParameters.MaxAlertLength);
        Assert.Equal(5, OpsParameters.HttpTimeoutSeconds);
        Assert.Equal(50, OpsParameters.AlertQueueCapacity);
        Assert.Equal(3, OpsParameters.AlertsPerPump);
        Assert.Equal(1, OpsParameters.BackoffFirstMinutes);
        Assert.Equal(30, OpsParameters.BackoffMaxMinutes);
        Assert.Equal(10, OpsParameters.CollapseWindowMinutes);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Validate_AllSecretsSet_IsValidAndEnabled()
    {
        OpsParameters p = OpsData.WithSecrets;

        p.Validate();
        Assert.True(p.AlertsEnabled);
        Assert.True(p.HeartbeatEnabled);
        (p with { TelegramChatId = "-1001234", HeartbeatUrl = "http://hb.example.invalid/x" }).Validate();
        (p with { TelegramChatId = "@my_channel" }).Validate();
    }

    [Theory]
    [Trait("Req", "OPS-004")]
    [InlineData("TelegramBotToken")]
    [InlineData("TelegramChatId")]
    public void Validate_OnlyOneTelegramValue_Throws(string missing)
    {
        OpsParameters p = missing == "TelegramBotToken"
            ? OpsData.WithSecrets with { TelegramBotToken = string.Empty }
            : OpsData.WithSecrets with { TelegramChatId = string.Empty };

        ArgumentOutOfRangeException e = Assert.Throws<ArgumentOutOfRangeException>(() => p.Validate());

        Assert.Equal(missing, e.ParamName);
        Assert.False(OpsData.ContainsSecret(e.Message));
    }

    [Theory]
    [Trait("Req", "OPS-005")]
    [InlineData("ftp://hb.example.invalid/x")]
    [InlineData("hb.example.invalid/x")]
    [InlineData("https://hb.example.invalid/a b")]
    [InlineData("https://hb.example.invalid/a\tb")]
    public void Validate_BadHeartbeatUrl_Throws(string url)
    {
        ArgumentOutOfRangeException e = Assert.Throws<ArgumentOutOfRangeException>(() => (new OpsParameters { HeartbeatUrl = url }).Validate());

        Assert.Equal("HeartbeatUrl", e.ParamName);
        Assert.DoesNotContain("hb.example", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Req", "OPS-004")]
    [InlineData("TelegramBotToken", "a/b")]
    [InlineData("TelegramBotToken", "a?b")]
    [InlineData("TelegramChatId", "12 34")]
    [InlineData("TelegramChatId", "12:34")]
    public void Validate_CharacterNotAllowed_Throws(string name, string value)
    {
        OpsParameters p = name == "TelegramBotToken"
            ? OpsData.WithSecrets with { TelegramBotToken = value }
            : OpsData.WithSecrets with { TelegramChatId = value };

        Assert.Equal(name, Assert.Throws<ArgumentOutOfRangeException>(() => p.Validate()).ParamName);
    }

    [Fact]
    [Trait("Req", "OPS-004")]
    public void Validate_NullSecret_Throws()
    {
        Assert.Equal("HeartbeatUrl", Assert.Throws<ArgumentOutOfRangeException>(() => (new OpsParameters { HeartbeatUrl = null! }).Validate()).ParamName);
        Assert.Equal("TelegramBotToken", Assert.Throws<ArgumentOutOfRangeException>(() => (new OpsParameters { TelegramBotToken = null! }).Validate()).ParamName);
        Assert.Equal("TelegramChatId", Assert.Throws<ArgumentOutOfRangeException>(() => (new OpsParameters { TelegramChatId = null! }).Validate()).ParamName);
    }

    [Theory]
    [Trait("Req", "OPS-003")]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(100.000001)]
    [InlineData(double.NaN)]
    public void Validate_ProfitTargetOutOfRange_Throws(double value)
    {
        Assert.Equal("ProfitTargetPercent", Assert.Throws<ArgumentOutOfRangeException>(() => (new OpsParameters { ProfitTargetPercent = value }).Validate()).ParamName);
    }

    [Theory]
    [Trait("Req", "OPS-003")]
    [InlineData(0.000001)]
    [InlineData(5.0)]
    [InlineData(100.0)]
    public void Validate_ProfitTargetInRange_Passes(double value)
    {
        (new OpsParameters { ProfitTargetPercent = value }).Validate();
    }
}
