using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Lms.Core.Audit;
using Nitrox.Model.Logger;

namespace Lms.Server
{
    /// <summary>
    ///     Writes the tournament's audit records to disk; the format itself is <c>Lms.Core.Audit</c>'s.
    /// </summary>
    /// <remarks>
    ///     One file per server run, so sequence numbers stay monotonic within a file. Flushed per
    ///     record, so a crash costs at most the last line.
    /// </remarks>
    internal sealed class AuditFile
    {
        /// <summary>Audit log folder, relative to the server directory.</summary>
        private const string Directory = "lms-audit";

        private readonly StreamWriter writer;

        /// <summary>Last write failure reason, so a repeated failure is logged once.</summary>
        private string? failing;

        private AuditFile(StreamWriter writer)
        {
            this.writer = writer;
        }

        /// <summary>
        ///     Opens this run's log, or logs why not and returns null; the match runs without a log.
        /// </summary>
        internal static AuditFile? Open(string directory, DateTimeOffset at)
        {
            string path = Path.Combine(directory, Directory,
                "lms-audit-" + at.ToUniversalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".tsv");

            try
            {
                System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // Append: a restart within the same second must not truncate the existing file.
                StreamWriter writer = new StreamWriter(path, append: true) { AutoFlush = true };
                writer.WriteLine(AuditFormat.Header);

                Log.Info($"{LmsServerMod.LogPrefix} writing the audit log to '{path}'");

                return new AuditFile(writer);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"{LmsServerMod.LogPrefix} could not open the audit log at '{path}'. " +
                              "The match will run and nothing will be recorded.");
                return null;
            }
        }

        /// <summary>
        ///     Every earlier run's records, oldest first, from the files already in the directory; a
        ///     restarted server resumes the tournament from these.
        /// </summary>
        /// <remarks>
        ///     Called before this run's file is opened. An unreadable file is logged and skipped rather
        ///     than stopping the server.
        /// </remarks>
        internal static IReadOnlyList<IReadOnlyList<AuditRecord>> Earlier(string directory)
        {
            List<IReadOnlyList<AuditRecord>> runs = new List<IReadOnlyList<AuditRecord>>();
            string folder = Path.Combine(directory, Directory);

            if (!System.IO.Directory.Exists(folder))
            {
                return runs;
            }

            // File names are the UTC start time, so ordinal order is run order.
            string[] files = System.IO.Directory.GetFiles(folder, "lms-audit-*.tsv");
            Array.Sort(files, StringComparer.Ordinal);

            foreach (string path in files)
            {
                try
                {
                    runs.AddRange(AuditFormat.ReadRuns(File.ReadAllLines(path), out int unreadable));

                    if (unreadable > 0)
                    {
                        Log.Warn($"{LmsServerMod.LogPrefix} {unreadable} line(s) of '{path}' did not read and are " +
                                 "left out of the tournament this server picks up.");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, $"{LmsServerMod.LogPrefix} could not read '{path}', so its games are not on " +
                                  "the table this server picks up.");
                }
            }

            return runs;
        }

        /// <summary>Writes one record. A failure is logged once per distinct reason, not per record.</summary>
        internal void Append(AuditRecord record)
        {
            try
            {
                writer.WriteLine(AuditFormat.Write(record));
                failing = null;
            }
            catch (Exception ex)
            {
                Report(ex.Message);
            }
        }

        private void Report(string reason)
        {
            if (reason == failing)
            {
                return;
            }

            failing = reason;
            Log.Error($"{LmsServerMod.LogPrefix} the audit log is not being written: {reason}");
        }
    }
}
