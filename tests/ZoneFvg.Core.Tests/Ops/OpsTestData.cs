using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Core.Tests;

/// <summary>
/// Hand-written data for the operations tests (WP09). The secrets below are made-up test values
/// that do not look like real tokens; they only need to be recognisable in the output.
/// </summary>
public static class OpsData
{
    public const string Token = "TESTTOKEN-abc_DEF";

    public const string ChatId = "CHAT-42";

    public const string HeartbeatUrl = "https://hb.example.invalid/ping/hb-key-77";

    public const string Instance = "EURUSD";

    /// <summary>Wednesday 2026-01-14 10:00 UTC (11:00 CET).</summary>
    public static readonly DateTime T0 = new(2026, 1, 14, 10, 0, 0, DateTimeKind.Utc);

    public static OpsParameters WithSecrets => new() { TelegramBotToken = Token, TelegramChatId = ChatId, HeartbeatUrl = HeartbeatUrl };

    public static string[] Secrets => new[] { Token, ChatId, HeartbeatUrl };

    public static Journal NewJournal(RecordingSink sink, OpsParameters? parameters = null)
    {
        return new Journal(sink, SecretMasker.For(parameters ?? WithSecrets));
    }

    public static AlertSetup Alerts(OpsParameters? parameters = null)
    {
        OpsParameters p = parameters ?? WithSecrets;
        var sink = new RecordingSink();
        var transport = new ScriptedTransport();
        var masker = SecretMasker.For(p);
        var journal = new Journal(sink, masker);
        var service = new AlertService(p, Instance, transport, journal, masker);
        return new AlertSetup(service, transport, sink, journal);
    }

    /// <summary>The Telegram request address for a message, written out by hand.</summary>
    public static string TelegramUrl(string escapedText)
    {
        return "https://api.telegram.org/bot" + Token + "/sendMessage?chat_id=" + ChatId + "&text=" + escapedText;
    }

    public static bool ContainsSecret(string text)
    {
        return Secrets.Any(secret => text.Contains(secret, StringComparison.Ordinal));
    }
}

public sealed record AlertSetup(AlertService Service, ScriptedTransport Transport, RecordingSink Sink, Journal Journal);

/// <summary>Records every journal line; can be told to throw.</summary>
public sealed class RecordingSink : IJournalSink
{
    public List<(string Category, string Line)> Lines { get; } = new();

    /// <summary>When set, <see cref="Write"/> throws an exception with this message.</summary>
    public string? ThrowMessage { get; set; }

    public void Write(string category, string line)
    {
        if (ThrowMessage != null)
        {
            throw new InvalidOperationException(ThrowMessage);
        }

        Lines.Add((category, line));
    }

    public IReadOnlyList<string> Of(string category)
    {
        return Lines.Where(l => l.Category == category).Select(l => l.Line).ToList();
    }
}

/// <summary>
/// HTTP fake for the operations tests. Answers each call with the next scripted response (which
/// may throw), then with <see cref="Default"/>. Records every address and timeout.
/// </summary>
public sealed class ScriptedTransport : IHttpTransport
{
    private readonly Queue<Func<string, HttpResult>> _script = new();

    public HttpResult Default { get; set; } = new(true, 200, "{\"ok\":true}");

    public List<(string Url, int Timeout)> Calls { get; } = new();

    public void Then(HttpResult result)
    {
        _script.Enqueue(_ => result);
    }

    public void ThenThrow(Func<string, Exception> exception)
    {
        _script.Enqueue(url => throw exception(url));
    }

    public HttpResult Get(string url, int timeoutSeconds)
    {
        Calls.Add((url, timeoutSeconds));
        return _script.Count > 0 ? _script.Dequeue()(url) : Default;
    }

    public static HttpResult Failed(int status = 0)
    {
        return new HttpResult(false, status, string.Empty);
    }
}
