using System;
using cAlgo.API;
using ZoneFvg.Core.Execution.Ports;
using ZoneFvg.Core.Ops;

namespace ZoneFvg.Bot
{
    /// <summary>
    /// Writes the adapter's own messages and the core's journal lines to the cTrader log (PLT-002,
    /// OPS-007). Every text passes through the core's <see cref="SecretMasker"/> before it is
    /// printed, so no Telegram token, chat id or heartbeat address reaches the log.
    /// </summary>
    internal sealed class AdapterLog : IJournalSink
    {
        private readonly Robot _robot;
        private readonly SecretMasker _masker;

        /// <summary>Creates the log for one cBot instance (PLT-002).</summary>
        /// <param name="robot">The running cBot, used for its Print method.</param>
        /// <param name="masker">The masker built from the secret parameters (OPS-007).</param>
        public AdapterLog(Robot robot, SecretMasker masker)
        {
            _robot = robot ?? throw new ArgumentNullException(nameof(robot));
            _masker = masker ?? throw new ArgumentNullException(nameof(masker));
        }

        /// <summary>Prints one adapter message, masked (OPS-007).</summary>
        /// <param name="message">The message.</param>
        public void Info(string message)
        {
            Print("ZFVG adapter " + message);
        }

        /// <summary>Prints one adapter message about an exception, masked (OPS-007, AGENTS.md rule 10).</summary>
        /// <param name="context">What the adapter was doing.</param>
        /// <param name="exception">The exception.</param>
        public void Error(string context, Exception exception)
        {
            Print("ZFVG adapter error in " + context + ": " + _masker.Apply(exception));
        }

        /// <summary>
        /// Journal port (OPS-001, OPS-002): prints the line, which the core has already masked; it is
        /// masked again here at no cost. Never throws for a printable line.
        /// </summary>
        /// <param name="category">Category of the line; also part of the line itself.</param>
        /// <param name="line">The complete line.</param>
        public void Write(string category, string line)
        {
            Print(line);
        }

        private void Print(string text)
        {
            // Algo.Print(string, params object[]) formats its first argument, so the text is passed
            // as an argument, never as the format: a '{' in a journal line cannot break the call.
            // https://help.ctrader.com/ctrader-algo/references/General/Algo/
            _robot.Print("{0}", _masker.Apply(text));
        }
    }
}
