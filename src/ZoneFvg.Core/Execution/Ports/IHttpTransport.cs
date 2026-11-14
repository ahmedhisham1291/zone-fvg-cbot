namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// HTTP port (docs/architecture.md section 3). The adapter implements it; the core never opens a
/// connection itself. Implementations never throw: every failure, including a timeout, is
/// returned as a result with <see cref="HttpResult.Ok"/> false (NWS-005, NWS-010).
/// </summary>
public interface IHttpTransport
{
    /// <summary>Sends a GET request and returns the outcome. Never throws (NWS-005).</summary>
    /// <param name="url">The address to read.</param>
    /// <param name="timeoutSeconds">Longest time to wait for the response, in seconds.</param>
    /// <returns>The outcome of the request.</returns>
    HttpResult Get(string url, int timeoutSeconds);
}

/// <summary>Outcome of one <see cref="IHttpTransport.Get"/> call (NWS-005).</summary>
/// <param name="Ok">True when a response with a success status arrived in time.</param>
/// <param name="Status">HTTP status code, or 0 when no response arrived.</param>
/// <param name="Body">Response body; empty when there is none.</param>
public sealed record HttpResult(bool Ok, int Status, string Body);
