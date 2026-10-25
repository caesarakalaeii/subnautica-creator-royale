using System;
using System.Collections.Generic;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Commands the casting booth sent back, plus the watermark that stops one running twice.
    /// </summary>
    /// <remarks>
    ///     The watermark is per server run: after a restart the booth re-offers everything
    ///     unacknowledged, and its expiry bounds commands that ran but whose answer was lost.
    ///     Authorisation stays in <see cref="Match.MatchHost"/>. Not thread-safe; the host drives it
    ///     under its match lock.
    /// </remarks>
    public sealed class BoothCommandQueue
    {
        /// <summary>
        ///     Cap on commands waiting for a tick. A well-behaved booth never reaches it; it bounds
        ///     memory against a misbehaving endpoint.
        /// </summary>
        private const int MaxPending = 64;

        /// <summary>
        ///     Cap on answers held for the console report, reached only while the booth refuses them.
        ///     Oldest are dropped first.
        /// </summary>
        private const int MaxAnswers = 64;

        /// <summary>Waiting to be run, lowest id first, preserving the operator's order.</summary>
        private readonly SortedList<long, string> pending = new SortedList<long, string>();

        private readonly List<BoothCommandAnswer> answers = new List<BoothCommandAnswer>();

        /// <param name="accepts">
        ///     <c>telemetry.acceptsCommands</c>. False makes every method a no-op and nulls
        ///     <see cref="Cursor"/>, so the booth offers nothing.
        /// </param>
        public BoothCommandQueue(bool accepts = true)
        {
            Accepts = accepts;
        }

        /// <summary>Whether this server takes commands from the booth.</summary>
        public bool Accepts { get; }

        /// <summary>The highest command id this server has run, this run.</summary>
        public long Ran { get; private set; }

        /// <summary>
        ///     The watermark header value, or null to send none; the booth only offers commands to a
        ///     server that reports one.
        /// </summary>
        public long? Cursor => Accepts ? Ran : (long?)null;

        /// <summary>How many are waiting for a tick.</summary>
        public int Waiting => pending.Count;

        /// <summary>Answers waiting to go back on the console channel.</summary>
        public IReadOnlyList<BoothCommandAnswer> Answers => answers;

        /// <summary>
        ///     Takes what a push's reply offered and returns how many are new. Ids at or below the
        ///     watermark are resends and are silently dropped.
        /// </summary>
        public int Offer(IReadOnlyList<BoothCommand>? offered)
        {
            if (!Accepts || offered == null)
            {
                return 0;
            }

            int taken = 0;

            foreach (BoothCommand command in offered)
            {
                if (command.Id <= Ran || pending.ContainsKey(command.Id))
                {
                    continue;
                }

                if (pending.Count >= MaxPending)
                {
                    // Refuse new ones rather than drop the oldest, so verbs still run in the order asked.
                    break;
                }

                pending.Add(command.Id, command.Verb);
                taken++;
            }

            return taken;
        }

        /// <summary>
        ///     Takes the next command to run, lowest id first.
        /// </summary>
        /// <remarks>
        ///     The caller runs one per tick so each verb sees the world the previous one left (e.g.
        ///     <c>podclear all</c> before <c>podspawn</c>). Only <see cref="Answered"/> moves the watermark.
        /// </remarks>
        public bool TryTake(out BoothCommand command)
        {
            if (!Accepts || pending.Count == 0)
            {
                command = default;
                return false;
            }

            long id = pending.Keys[0];
            command = new BoothCommand(id, pending.Values[0]);
            pending.RemoveAt(0);

            return true;
        }

        /// <summary>
        ///     Writes down what the match said, and moves the watermark past it.
        /// </summary>
        /// <param name="id">The command that ran.</param>
        /// <param name="verb">The verb, so the console draws the pair without a lookup.</param>
        /// <param name="answer">What the match said, verbatim.</param>
        /// <returns>How many answers were dropped to make room; zero unless the booth is down.</returns>
        public int Answered(long id, string verb, string answer)
        {
            if (!Accepts)
            {
                return 0;
            }

            if (id > Ran)
            {
                Ran = id;
            }

            pending.Remove(id);
            answers.Add(new BoothCommandAnswer(id, verb, answer));

            int dropped = 0;
            while (answers.Count > MaxAnswers)
            {
                answers.RemoveAt(0);
                dropped++;
            }

            return dropped;
        }

        /// <summary>
        ///     Forgets the first <paramref name="count"/> answers, the prefix an accepted console report
        ///     carried. Call only after the push was accepted.
        /// </summary>
        public void Forget(int count)
        {
            if (count <= 0)
            {
                return;
            }

            answers.RemoveRange(0, Math.Min(count, answers.Count));
        }

        public override string ToString() =>
            !Accepts
                ? "the booth's commands are switched off"
                : $"ran up to #{Ran}, {pending.Count} waiting, {answers.Count} answer(s) owed";
    }
}
