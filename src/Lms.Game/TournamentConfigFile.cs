using System.IO;
using System.Reflection;
using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Nitrox.Model.Logger;

namespace Lms.Game
{
    /// <summary>
    ///     Finds and loads the tournament rules from <c>lms-tournament.yaml</c> next to this assembly.
    /// </summary>
    /// <remarks>
    ///     Zone timing and geometry come from the server; the client uses only its own settings
    ///     (damage, colours, ring, flags) and ignores the <c>match</c> and <c>zone</c> phase sections.
    /// </remarks>
    internal static class TournamentConfigFile
    {
        internal const string FileName = "lms-tournament.yaml";

        internal static TournamentConfig? Load()
        {
            string directory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
            string path = Path.Combine(directory, FileName);

            if (!File.Exists(path))
            {
                Log.Warn($"{LmsMod.LogPrefix} no {FileName} in '{directory}'. Biome readout only: " +
                         "no wall drawn, no damage, no ring, whatever the server is running.");
                return null;
            }

            TournamentConfigLoadResult result = TournamentConfigLoader.Load(File.ReadAllText(path));
            if (!result.IsValid)
            {
                // Log every problem, not just the first: this is the operator's error report.
                Log.Error($"{LmsMod.LogPrefix} {FileName} has {result.Problems.Count} problem(s); running with no zone:");
                foreach (TournamentConfigProblem problem in result.Problems)
                {
                    Log.Error($"{LmsMod.LogPrefix}   {problem}");
                }

                return null;
            }

            TournamentConfig config = result.Config!;
            Log.Info(Readable.Line(
                $"{LmsMod.LogPrefix} loaded {FileName}: a bite every ",
                $"{config.ZonePolicy.DamageInterval.TotalSeconds:0.#}s ramping over ",
                $"{config.ZonePolicy.FullExposureAt:0} m, wall {config.Colours.Now} closing to ",
                $"{config.Colours.Next}, damage {(config.Flags.EnforceDamage ? "ENABLED" : "disabled")}, ",
                $"containment {(config.Flags.EnforceContainment ? "ENABLED" : "disabled")}. ",
                $"The zone itself comes from the server."));
            return config;
        }
    }
}
