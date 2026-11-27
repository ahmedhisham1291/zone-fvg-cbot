using Xunit;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// SIG-042 (row AY) in the cBot source and the parameter files, as far as it can be checked
/// without cTrader: <c>RangeTrendFastEma</c> is declared once with default 20 in the group of the
/// other range parameters, right after RangeStopAtr; its XML summary names spec 3.8 and row AY;
/// it is passed into the core's SessionRange parameters, which reach the core in SessionRange mode
/// only; the five files under <c>deploy/params</c> carry <c>RangeTrendFastEma=20</c> once, right
/// after <c>RangeStopAtr=1.0</c>, and stay equal apart from <c>HeartbeatUrl</c>.
/// </summary>
public class Sig042CbotRangeTrendTests
{
    private const string Declaration = "[Parameter(\"RangeTrendFastEma\", DefaultValue = 20, Group = \"Entry mode\")]";

    private static readonly string[] Files = { "audusd", "eurusd", "gbpusd", "usdjpy", "xauusd" };

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            if (Directory.Exists(Path.Combine(folder.FullName, "src", "ZoneFvg.Bot")) && Directory.Exists(Path.Combine(folder.FullName, "deploy", "params")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException("src/ZoneFvg.Bot and deploy/params were not found above " + AppContext.BaseDirectory);
    }

    private static string BotSource()
    {
        return File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "ZoneFvg.Bot", "ZoneFvgBot.cs"));
    }

    private static string[] ParameterFile(string name)
    {
        return File.ReadAllLines(Path.Combine(RepositoryRoot(), "deploy", "params", name + ".cbotset.example"));
    }

    private static int Once(string source, string text)
    {
        int at = source.IndexOf(text, StringComparison.Ordinal);
        Assert.True(at > 0, text);
        Assert.Equal(at, source.LastIndexOf(text, StringComparison.Ordinal));
        return at;
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Bot_DeclaresRangeTrendFastEmaOnceWithDefault20InTheEntryModeGroup()
    {
        string source = BotSource();

        Once(source, Declaration);
        Once(source, "public int RangeTrendFastEma { get; set; }");
        Assert.Single(source.Split('\n'), line => line.Contains("[Parameter(\"RangeTrendFastEma\"", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Bot_TheDeclarationDirectlyAfterRangeStopAtr_WithASummaryNamingSpec38AndRowAY()
    {
        string source = BotSource();
        int stop = Once(source, "[Parameter(\"RangeStopAtr\", DefaultValue = 1.0, Group = \"Entry mode\")]");
        int fast = Once(source, Declaration);
        // WP43 (PLT-021): the seven Night parameters of the group "Overnight" now follow it; the next one is NightEntryNy.
        int next = Once(source, "[Parameter(\"NightEntryNy\"");

        Assert.True(stop < fast && fast < next);
        Assert.DoesNotContain("[Parameter(", source.Substring(stop + 1, fast - stop - 1), StringComparison.Ordinal);
        Assert.DoesNotContain("[Parameter(", source.Substring(fast + 1, next - fast - 1), StringComparison.Ordinal);
        int summary = source.LastIndexOf("/// <summary>", fast, StringComparison.Ordinal);
        string text = source.Substring(summary, fast - summary);
        Assert.Contains("SIG-042", text, StringComparison.Ordinal);
        Assert.Contains("spec 3.8", text, StringComparison.Ordinal);
        Assert.Contains("row AY", text, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void Bot_PassesTheValueIntoTheSessionRangeParameters_UsedInSessionRangeModeOnly()
    {
        string source = BotSource();
        int start = Once(source, "private BotParameters BuildParameters(");
        int end = source.IndexOf("private SignalParameters BuildSignal(", StringComparison.Ordinal);
        Assert.True(end > start);
        string build = source.Substring(start, end - start);

        Assert.Contains("SessionRangeParameters sessionRange = SessionRangeText.Build(RangeStartNy, RangeEndNy, RangeLastEntryNy, RangeExitNy, RangeMinWidthAtr, RangeStopAtr, RangeTrendFastEma);", build, StringComparison.Ordinal);
        Assert.Contains("SessionRange = mode == CoreEntryMode.SessionRange ? sessionRange : null,", build, StringComparison.Ordinal);
        Once(source, "RangeTrendFastEma);");
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void ParameterFiles_CarryRangeTrendFastEma20OnceDirectlyAfterRangeStopAtr()
    {
        foreach (string name in Files)
        {
            string[] lines = ParameterFile(name);

            Assert.Single(lines, line => line.StartsWith("RangeTrendFastEma=", StringComparison.Ordinal));
            int at = Array.IndexOf(lines, "RangeTrendFastEma=20");
            Assert.Equal("RangeStopAtr=1.0", lines[at - 1]);
            // WP43 (PLT-021): the seven Night lines now follow it; EntryModel comes after them.
            Assert.Equal("NightEntryNy=18:15", lines[at + 1]);
            Assert.Equal("EntryModel=Fvg", lines[at + 8]);
            Assert.Equal(11, at);
            Assert.Equal("EntryMode=SessionRange", lines[0]);
        }
    }

    [Fact]
    [Trait("Req", "SIG-042")]
    public void ParameterFiles_EqualApartFromTheHeartbeat()
    {
        string[] Without(string name) => ParameterFile(name).Where(l => !l.StartsWith("HeartbeatUrl=", StringComparison.Ordinal)).ToArray();

        string[] gold = Without("xauusd");
        foreach (string name in Files)
        {
            Assert.Equal(gold, Without(name));
            Assert.Single(ParameterFile(name), l => l.StartsWith("HeartbeatUrl=", StringComparison.Ordinal));
        }
    }
}
