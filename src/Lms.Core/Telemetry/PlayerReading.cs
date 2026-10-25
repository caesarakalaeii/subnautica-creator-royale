using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     One player as the Nitrox server already tracks them (position from <c>PlayerMovement</c>,
    ///     vitals from <c>PlayerStats</c>), handed across by the fork.
    /// </summary>
    public readonly struct PlayerReading
    {
        public PlayerReading(ushort id, string name, WorldPoint position, bool alive,
            double health, double oxygen, double food, double water)
        {
            Id = id;
            Name = name;
            Position = position;
            Alive = alive;
            Health = health;
            Oxygen = oxygen;
            Food = food;
            Water = water;
        }

        /// <summary>Nitrox's own id, which is what a report is keyed on.</summary>
        public ushort Id { get; }

        public string Name { get; }

        public WorldPoint Position { get; }

        /// <summary>
        ///     Whether Nitrox (hardcore permadeath) considers this player alive. Tournament elimination
        ///     is decided client-side and arrives in a <see cref="PlayerReport"/>.
        /// </summary>
        public bool Alive { get; }

        public double Health { get; }

        public double Oxygen { get; }

        public double Food { get; }

        public double Water { get; }
    }
}
