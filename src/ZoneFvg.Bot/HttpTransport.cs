using System;
using cAlgo.API;
using ZoneFvg.Core.Execution.Ports;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// HTTP port over the Algo API's <c>Http</c> interface (architecture section 3). Works with
    /// AccessRights.None. Each request carries a hard timeout of the requested length (5 s for
    /// alerts and heartbeat, 10 s for the news feed), so a slow server delays the calling event by
    /// at most that time. Never throws: every failure is returned as a result with
    /// <see cref="HttpResult.Ok"/> false and logged, masked (NWS-005, OPS-004, OPS-007).
    /// </summary>
    internal sealed class HttpTransport : IHttpTransport
    {
        private readonly Http _http;
        private readonly AdapterLog _log;

        /// <summary>Creates the transport over the cBot's <c>Http</c> object (NWS-005).</summary>
        /// <param name="http">The cBot's <c>Http</c>.</param>
        /// <param name="log">Masked log for failures.</param>
        public HttpTransport(Http http, AdapterLog log)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        /// <summary>Sends a GET request with a hard timeout and returns the outcome. Never throws (NWS-005).</summary>
        /// <param name="url">The address to read.</param>
        /// <param name="timeoutSeconds">Longest time to wait, in seconds; at least 1 second is used.</param>
        /// <returns>The outcome of the request.</returns>
        public HttpResult Get(string url, int timeoutSeconds)
        {
            try
            {
                // Network access with AccessRights.None: https://help.ctrader.com/ctrader-algo/guides/network-access/
                // HttpRequest(Uri), Timeout (default 100 s), Method (default Get):
                // https://help.ctrader.com/ctrader-algo/references/HTTP/HttpRequest/
                var request = new HttpRequest(new Uri(url))
                {
                    Timeout = TimeSpan.FromSeconds(Math.Max(1, timeoutSeconds)),
                };

                // Http.Send(HttpRequest) is synchronous: https://help.ctrader.com/ctrader-algo/references/HTTP/Http
                HttpResponse response = _http.Send(request);

                // HttpResponse: IsSuccessful, StatusCode (int), Body, Exception:
                // https://help.ctrader.com/ctrader-algo/references/HTTP/HttpResponse/
                if (response == null)
                {
                    _log.Info("http: no response object");
                    return new HttpResult(false, 0, string.Empty);
                }

                if (!response.IsSuccessful)
                {
                    // The text is masked by the log, so an address with a token cannot be printed (OPS-007).
                    _log.Info("http: request failed, status " + response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        + (response.Exception == null ? string.Empty : ", " + response.Exception));
                }

                return new HttpResult(response.IsSuccessful, response.StatusCode, response.Body ?? string.Empty);
            }
            catch (Exception exception)
            {
                // The port must never throw (NWS-005); the failure is logged, masked, not swallowed.
                _log.Error("http request", exception);
                return new HttpResult(false, 0, string.Empty);
            }
        }
    }
}
