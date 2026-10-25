using System.Collections.Generic;
using Lms.Core.Spawning;
using Lms.Core.Zones;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Tournament;

namespace Lms.Server
{
    /// <summary>
    ///     Converts Nitrox positions (float) to Core <see cref="WorldPoint"/>s (double) in one place.
    /// </summary>
    internal static class PodPoints
    {
        internal static IReadOnlyList<WorldPoint> Of(IReadOnlyList<NitroxVector3> vectors)
        {
            List<WorldPoint> points = new List<WorldPoint>(vectors.Count);

            foreach (NitroxVector3 vector in vectors)
            {
                points.Add(new WorldPoint(vector.X, vector.Y, vector.Z));
            }

            return points;
        }

        internal static IReadOnlyList<WorldPoint> Of(IReadOnlyList<LmsPod> pods)
        {
            List<WorldPoint> points = new List<WorldPoint>(pods.Count);

            foreach (LmsPod pod in pods)
            {
                points.Add(new WorldPoint(pod.X, pod.Y, pod.Z));
            }

            return points;
        }

        /// <summary>The world's pods as Core reads them, in the same order, so an index means the same pod.</summary>
        internal static IReadOnlyList<PodInTheWorld> Readings(IReadOnlyList<LmsPod> pods)
        {
            List<PodInTheWorld> readings = new List<PodInTheWorld>(pods.Count);

            foreach (LmsPod pod in pods)
            {
                List<int> players = new List<int>(pod.Players.Count);
                foreach (ushort player in pod.Players)
                {
                    players.Add(player);
                }

                readings.Add(new PodInTheWorld(new WorldPoint(pod.X, pod.Y, pod.Z), players, pod.StowedItems));
            }

            return readings;
        }

        internal static IReadOnlyList<WorldPoint> Of(IReadOnlyList<LmsPlayerReading> players)
        {
            List<WorldPoint> points = new List<WorldPoint>(players.Count);

            foreach (LmsPlayerReading player in players)
            {
                points.Add(new WorldPoint(player.X, player.Y, player.Z));
            }

            return points;
        }
    }
}
