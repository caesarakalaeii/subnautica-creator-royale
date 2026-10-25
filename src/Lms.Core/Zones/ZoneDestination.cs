using System;

namespace Lms.Core.Zones
{
    /// <summary>One place a match can finish: a named point the squeeze walks towards.</summary>
    /// <remarks>
    ///     The name is written to the audit log and read out on stream, so it must be unique within
    ///     a pool (<see cref="ZoneSqueeze"/> enforces this).
    /// </remarks>
    public sealed class ZoneDestination
    {
        public ZoneDestination(string name, WorldPoint centre)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A destination needs a name: it is what the log records and what the caster says.", nameof(name));
            }

            Name = name.Trim();
            Centre = centre;
        }

        /// <summary>What the tournament calls this place.</summary>
        public string Name { get; }

        /// <summary>Where the last authored sphere closes, and where the collapse ends.</summary>
        public WorldPoint Centre { get; }

        public override string ToString() => $"{Name} at {Centre}";
    }
}
