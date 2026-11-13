using Xunit;

namespace ZoneFvg.Core.Tests;

public class BuildInfoTests
{
    [Fact]
    public void Version_IsSet()
    {
        Assert.False(string.IsNullOrWhiteSpace(BuildInfo.Version));
    }

    [Fact]
    public void LabelVersion_IsV1()
    {
        Assert.Equal("v1", BuildInfo.LabelVersion);
    }
}
