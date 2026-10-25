using System;
using System.Globalization;

namespace Lms.Core.Match
{
    /// <summary>The health, food and water a round starts a contestant with, as percentages.</summary>
    /// <remarks>
    ///     Every in-game vital scale tops out at a hundred. Oxygen is not included: its maximum
    ///     depends on the tank, and the wire already carries it in seconds.
    /// </remarks>
    public readonly struct RoundVitals : IEquatable<RoundVitals>
    {
        /// <summary>The top of every in-game vital scale, health, food and water alike.</summary>
        public const int Maximum = 100;

        /// <summary>The bottom of the range. Zero would be dead or starving on arrival.</summary>
        public const int Minimum = 1;

        public RoundVitals(int health, int food, int water)
        {
            if (health < Minimum || health > Maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(health), health,
                    $"Health is a percentage from {Minimum} to {Maximum}.");
            }

            if (food < Minimum || food > Maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(food), food,
                    $"Food is a percentage from {Minimum} to {Maximum}.");
            }

            if (water < Minimum || water > Maximum)
            {
                throw new ArgumentOutOfRangeException(nameof(water), water,
                    $"Water is a percentage from {Minimum} to {Maximum}.");
            }

            Health = health;
            Food = food;
            Water = water;
        }

        /// <summary>The health a contestant starts the round with, as a percentage.</summary>
        public int Health { get; }

        /// <summary>The food a contestant starts the round with, as a percentage.</summary>
        public int Food { get; }

        /// <summary>The water a contestant starts the round with, as a percentage.</summary>
        public int Water { get; }

        public bool Equals(RoundVitals other) =>
            Health == other.Health && Food == other.Food && Water == other.Water;

        public override bool Equals(object? obj) => obj is RoundVitals other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Health;
                hash = hash * 397 ^ Food;
                return hash * 397 ^ Water;
            }
        }

        /// <summary>One scale by its own name, formatted with the invariant culture as the wire carries it.</summary>
        public string Field(string name) => name switch
        {
            nameof(Health) => Health.ToString(CultureInfo.InvariantCulture),
            nameof(Food) => Food.ToString(CultureInfo.InvariantCulture),
            nameof(Water) => Water.ToString(CultureInfo.InvariantCulture),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name,
                "Not one of these vitals' scales.")
        };

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "health {0}, food {1}, water {2}",
                Health, Food, Water);
    }
}
