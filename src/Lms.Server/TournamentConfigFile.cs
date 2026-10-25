using System;
using System.Collections.Generic;
using System.IO;
using Lms.Core.Configuration;
using Lms.Core.Diagnostics;
using Nitrox.Model.Logger;

namespace Lms.Server
{
    /// <summary>
    ///     Finds and loads the tournament rules from the server's own directory.
    /// </summary>
    /// <remarks>
    ///     No config means no tournament: the server runs as stock Nitrox rather than on defaults.
    /// </remarks>
    internal static class TournamentConfigFile
    {
        internal const string FileName = "lms-tournament.yaml";

        /// <summary>Suffix of the single backup kept when a pushed config replaces the live one.</summary>
        internal const string BackupSuffix = ".bak";

        /// <summary>Loads and validates the config, logging every problem if it is invalid.</summary>
        /// <param name="directory">Where the server is, and where its config file lives.</param>
        /// <param name="yaml">The file's raw text, as the booth will show it; empty when nothing loaded.</param>
        internal static TournamentConfig? Load(string directory, out string yaml)
        {
            string path = Path.Combine(directory, FileName);
            yaml = string.Empty;

            if (!File.Exists(path))
            {
                Log.Info($"{LmsServerMod.LogPrefix} no {FileName} in '{directory}'; " +
                         "this server runs no tournament.");
                return null;
            }

            string text = File.ReadAllText(path);
            TournamentConfigLoadResult result = TournamentConfigLoader.Load(text);
            if (!result.IsValid)
            {
                // Log every problem, not just the first: this is the operator's error report.
                Log.Error($"{LmsServerMod.LogPrefix} {FileName} has {result.Problems.Count} problem(s); " +
                          "no tournament will run:");
                foreach (TournamentConfigProblem problem in result.Problems)
                {
                    Log.Error($"{LmsServerMod.LogPrefix}   {problem}");
                }

                return null;
            }

            TournamentConfig config = result.Config!;

            // Only expose text that actually loaded, so the booth never shows a config not in force.
            yaml = text;

            Log.Info(Readable.Line(
                $"{LmsServerMod.LogPrefix} loaded {FileName}: {config.Zone.AuthoredPhaseCount} phases of ",
                $"{config.Clock.CycleLength} then a collapse, opening {config.Zone.Opening}, ",
                $"final {config.Zone.Phases[config.Zone.AuthoredPhaseCount - 1].Target} ",
                $"closing to nothing over {config.Zone.FinalCollapse.ShrinkFor.TotalSeconds:0}s, ",
                $"waiting for 'lms start'"));

            return config;
        }

        /// <summary>
        ///     Writes a config the booth sent, so a restart runs it too.
        /// </summary>
        /// <param name="directory">Where the server is, and where its config file lives.</param>
        /// <param name="yaml">The document, already validated by the loader.</param>
        /// <param name="reason">Why it could not be written, or empty.</param>
        /// <remarks>
        ///     Written via a temporary file and an atomic replace so a crash never leaves a half-written
        ///     config. Failures are reported, never thrown.
        /// </remarks>
        internal static bool Save(string directory, string yaml, out string reason)
        {
            string path = Path.Combine(directory, FileName);
            string beside = path + ".new";

            try
            {
                File.WriteAllText(beside, yaml);

                // Atomic replace, keeping the outgoing file as the one-generation backup.
                if (File.Exists(path))
                {
                    File.Replace(beside, path, path + BackupSuffix);
                }
                else
                {
                    File.Move(beside, path);
                }

                reason = string.Empty;

                return true;
            }
            catch (IOException ex)
            {
                reason = ex.Message;
            }
            catch (UnauthorizedAccessException ex)
            {
                reason = ex.Message;
            }

            Log.Error($"{LmsServerMod.LogPrefix} could not write {FileName}: {reason}");

            try
            {
                if (File.Exists(beside))
                {
                    File.Delete(beside);
                }
            }
            catch (IOException)
            {
                // Only a stray temp file is left behind; the live config is untouched.
            }

            return false;
        }
    }
}
