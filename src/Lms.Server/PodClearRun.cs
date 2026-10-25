using System;
using System.Collections.Generic;
using Lms.Core.Diagnostics;
using Lms.Core.Spawning;
using Lms.Core.Zones;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;

namespace Lms.Server
{
    /// <summary>
    ///     Removes derelict escape pods from the world (<c>lms podclear</c>); <see cref="PodClearing"/>
    ///     decides which pods are derelict.
    /// </summary>
    /// <remarks>
    ///     <see cref="Run"/> removes only pods off the layout; <see cref="RunAll"/> (<c>podclear all</c>)
    ///     removes every pod unless someone is standing in one.
    /// </remarks>
    internal sealed class PodClearRun
    {
        private readonly SpawnPolicy policy;
        private readonly ZoneSphere arena;

        /// <summary>
        ///     Players in the match (not just connected). Ghosts are excluded, so a ghost inside a pod
        ///     never blocks a sweep.
        /// </summary>
        private readonly Func<IReadOnlyList<LmsPlayerReading>> contestants;

        internal PodClearRun(SpawnPolicy policy, ZoneSphere arena,
            Func<IReadOnlyList<LmsPlayerReading>> contestants)
        {
            this.policy = policy;
            this.arena = arena;
            this.contestants = contestants;
        }

        /// <summary>Removes every pod that is off the layout and has nobody in it.</summary>
        internal PodCommandOutcome Run()
        {
            ILmsPodSeating? seating = LmsPods.Seating;

            if (seating == null)
            {
                return PodCommandOutcome.Nothing(
                    "there is no server here to remove pods, so podclear did nothing.");
            }

            if (!policy.OwnPod)
            {
                // With a shared pod there is no layout, so every pod would look derelict: refuse.
                return PodCommandOutcome.Nothing(
                    "spawn.ownPod is off, so there is one pod between everybody and no layout to be " +
                    "off. Nothing here is a derelict.");
            }

            IReadOnlyList<LmsPod> pods = seating.Pods;
            PodClearingPlan plan = PodClearing.Plan(Layout(seating, pods).Pods, PodPoints.Readings(pods),
                PodPoints.Of(contestants()));

            if (plan.NoLayout)
            {
                return PodCommandOutcome.Nothing(Readable.Line(
                    $"the placement planned no pods, so nothing was removed. {plan}"));
            }

            if (plan.Clear.Count == 0)
            {
                return PodCommandOutcome.Nothing(Message(plan, 0));
            }

            int removed = 0;
            foreach (int index in plan.Clear)
            {
                if (seating.RemovePod(pods[index].Id))
                {
                    removed++;
                }
            }

            string message = Message(plan, removed);
            Log.Info($"{LmsServerMod.LogPrefix} podclear: {message}");

            return new PodCommandOutcome(removed, message, Readable.Line(
                $"{removed} pods removed, {plan.StowedItems} stowed thing(s) with them, ",
                $"{plan.Kept} kept, {plan.SomebodyInside} occupied"));
        }

        /// <summary>
        ///     Removes every escape pod, or refuses by name if somebody is standing in one
        ///     (<c>podclear all</c>). Ignores <see cref="SpawnPolicy.OwnPod"/>.
        /// </summary>
        internal PodCommandOutcome RunAll()
        {
            ILmsPodSeating? seating = LmsPods.Seating;

            if (seating == null)
            {
                return PodCommandOutcome.Nothing(
                    "there is no server here to remove pods, so podclear all did nothing.");
            }

            IReadOnlyList<LmsPod> pods = seating.Pods;
            IReadOnlyList<LmsPlayerReading> present = contestants();
            PodClearingPlan plan = PodClearing.PlanAll(PodPoints.Readings(pods), PodPoints.Of(present));

            if (plan.Occupants.Count > 0)
            {
                string refusal = Readable.Line(
                    $"{Names(plan.Occupants, present)} standing in a pod, so nothing was removed. ",
                    $"Ask them to swim out, or run podspawn to seat the group first.");

                Log.Info($"{LmsServerMod.LogPrefix} podclear all refused: {refusal}");

                return PodCommandOutcome.Nothing(refusal);
            }

            if (plan.Clear.Count == 0)
            {
                return PodCommandOutcome.Nothing("no escape pods in the world at all, so nothing to remove.");
            }

            int removed = 0;
            foreach (int index in plan.Clear)
            {
                if (seating.RemovePod(pods[index].Id))
                {
                    removed++;
                }
            }

            string message = Readable.Line(
                $"{removed} escape pod(s) taken out — every one in the world. ",
                $"{plan.StowedItems} stowed thing(s) went with them. Run podspawn to lay a group out.");

            Log.Info($"{LmsServerMod.LogPrefix} podclear all: {message}");

            return new PodCommandOutcome(removed, message, Readable.Line(
                $"{removed} pods removed (all), {plan.StowedItems} stowed thing(s) with them"));
        }

        /// <summary>
        ///     Names the players blocking the sweep. Indices are into <paramref name="present"/>, the
        ///     same list the positions were read from.
        /// </summary>
        private static string Names(IReadOnlyList<int> occupants, IReadOnlyList<LmsPlayerReading> present)
        {
            List<string> names = new List<string>(occupants.Count);

            foreach (int occupant in occupants)
            {
                LmsPlayerReading player = present[occupant];
                names.Add(string.IsNullOrWhiteSpace(player.Name)
                    ? Readable.Line($"player {player.Id}")
                    : player.Name);
            }

            return names.Count == 1
                ? Readable.Line($"{names[0]} is")
                : Readable.Line($"{string.Join(", ", names.ToArray())} are");
        }

        /// <summary>
        ///     The layout pods are judged against, computed exactly as <c>podspawn</c> does so the two
        ///     commands agree on which pods belong.
        /// </summary>
        private SpawnLayout Layout(ILmsPodSeating seating, IReadOnlyList<LmsPod> pods)
        {
            if (policy.OnACircle)
            {
                return SpawnCircle.Plan(policy.CircleRadius, PodPoints.Of(pods),
                    policy.GroupSize, policy.MinSeparation);
            }

            return SpawnScatter.Plan(PodPoints.Of(seating.StartPoints), new WorldPoint[0], arena,
                policy.GroupSize, policy.MinSeparation);
        }

        private static string Message(PodClearingPlan plan, int removed)
        {
            if (plan.Clear.Count == 0)
            {
                return plan.ToString();
            }

            string line = Readable.Line($"{removed} derelict pod(s) taken out, {plan.Kept} kept");

            if (plan.StowedItems > 0)
            {
                line += Readable.Line($", and {plan.StowedItems} stowed thing(s) went with them");
            }

            if (plan.SomebodyInside > 0)
            {
                line += Readable.Line(
                    $". {plan.SomebodyInside} was left alone because somebody is standing in it");
            }

            return line;
        }
    }
}
