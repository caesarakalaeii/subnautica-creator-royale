using System;
using System.Collections.Generic;
using Lms.Core.Diagnostics;
using Lms.Core.Match;
using Lms.Core.Spawning;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;

namespace Lms.Server
{
    /// <summary>
    ///     Sets a round up in the world: game modes, seating, sweep, then each contestant's instructions.
    /// </summary>
    /// <remarks>
    ///     The order matters. Seating must precede the sweep: Subnautica has one real lifepod, which a
    ///     client re-tags and moves into its assigned pod, so sweeping first would destroy it.
    /// </remarks>
    internal sealed class RoundRun
    {
        private readonly Func<GameModeSweep> modes;
        private readonly Func<PlayerMode, bool> correct;
        private readonly Func<PodCommandOutcome> sweep;
        private readonly Func<PodCommandOutcome> seat;
        private readonly Action<RoundBroadcast, IReadOnlyList<string>> instruct;
        private readonly Func<int> group;
        private readonly Func<int> generation;
        private readonly StartPolicy start;

        /// <param name="start">The start policy: hold, kit and recipes sent to each contestant.</param>
        /// <param name="modes">Which contestants are not in survival mode.</param>
        /// <param name="correct">Puts one contestant back into survival; false if they have left.</param>
        /// <param name="sweep"><c>podclear</c> of derelict pods; runs after <paramref name="seat"/>.</param>
        /// <param name="seat"><c>podspawn</c>: lays out fresh pods and seats each contestant in one.</param>
        /// <param name="instruct">Sends the kit and hold to each named contestant (not to ghosts).</param>
        /// <param name="group">The host's current group, stamped on the instruction.</param>
        /// <param name="generation">The host's current generation, stamped on the instruction.</param>
        internal RoundRun(StartPolicy start, Func<GameModeSweep> modes,
            Func<PlayerMode, bool> correct, Func<PodCommandOutcome> sweep,
            Func<PodCommandOutcome> seat, Action<RoundBroadcast, IReadOnlyList<string>> instruct,
            Func<int> group, Func<int> generation)
        {
            this.start = start;
            this.modes = modes;
            this.correct = correct;
            this.sweep = sweep;
            this.seat = seat;
            this.instruct = instruct;
            this.group = group;
            this.generation = generation;
        }

        /// <summary>Runs the sequence for one prepared round.</summary>
        internal RoundOutcome Run(RoundPlan plan)
        {
            if (plan == null || plan.IsRefused)
            {
                // MatchHost refuses first; guarded anyway because everything below removes something.
                return RoundOutcome.Nothing("there was nothing to prepare.");
            }

            // First, because it removes nothing. A contestant who cannot be corrected still plays.
            GameModeSweep wrongMode = modes();

            // An unreadable mode refuses the round, before anything is removed.
            if (wrongMode.Refusal != null)
            {
                return RoundOutcome.Nothing($"the round was not prepared: {wrongMode.Refusal}.");
            }

            List<PlayerMode> corrected = new List<PlayerMode>(wrongMode.Wrong.Count);

            foreach (PlayerMode player in wrongMode.Wrong)
            {
                if (correct(player))
                {
                    corrected.Add(player);
                    Log.Info($"{LmsServerMod.LogPrefix} round: {player.Name} was in {player.Mode} " +
                             "and is now in survival. A creative body takes no damage from the wall.");
                }
                else
                {
                    Log.Warn($"{LmsServerMod.LogPrefix} round: {player.Name} is in {player.Mode} and " +
                             "could not be put back into survival -- the server no longer has them. " +
                             "THEY MAY BE IMMORTAL FOR THIS ROUND.");
                }
            }

            // Seat before sweeping: each client moves its one real lifepod into its new pod.
            PodCommandOutcome seated = seat();
            if (seated.Changed == 0)
            {
                return RoundOutcome.Nothing($"the round was not prepared: {seated.Message}");
            }

            // Refuse if the roster changed between planning and seating; still before the sweep.
            IReadOnlyList<string> missing = seated.Missing(plan.Contestants);
            if (missing.Count > 0)
            {
                return RoundOutcome.Nothing(Readable.Line(
                    $"the round was not prepared: the seating lost {string.Join(", ", missing)}, ",
                    $"so the roster moved between planning and seating. Run it again."));
            }

            // Sweep the derelict pods the group was just moved off. By now the old pod ids are unmapped
            // from the real lifepod, so the destroy is a no-op on clients.
            PodCommandOutcome cleared = sweep();
            if (cleared.Changed == 0)
            {
                Log.Info($"{LmsServerMod.LogPrefix} round: nothing was swept -- {cleared.Message}");
            }

            RoundBroadcast instruction = new RoundBroadcast(group(), generation(), start.Hold,
                start.Kit, start.LockedRecipes, start.Recipes);

            instruct(instruction, plan.Contestants);

            string detail = Readable.Line(
                $"pods={seated.Changed} swept={cleared.Changed} told={plan.Contestants.Count} ",
                $"survival={corrected.Count} unread={wrongMode.Unseen.Count}");

            Log.Info($"{LmsServerMod.LogPrefix} round prepared: {detail}; {instruction}");

            string aboutModes = wrongMode.IsClean
                ? string.Empty
                : $" {wrongMode.Message}";

            return RoundOutcome.Prepared(
                Readable.Line(
                    $"{plan.Contestants.Count} seated on {seated.Changed} pod(s), ",
                    $"{cleared.Changed} old one(s) swept after, and everybody told what they start with.")
                + aboutModes,
                detail, corrected, wrongMode.Unseen);
        }
    }
}
