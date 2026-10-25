using System;

namespace Lms.Core.Biomes
{
    /// <summary>
    /// A biome as the tournament knows it, identified by the name the game uses.
    /// </summary>
    /// <remarks>
    /// Identity is case-insensitive because the game's data mixes casings (<c>CragField</c> vs
    /// <c>camelCase</c>); the given spelling is kept for display.
    /// </remarks>
    public sealed class TournamentBiome : IEquatable<TournamentBiome>
    {
        /// <summary>Creates a biome from a game biome name.</summary>
        /// <exception cref="ArgumentException">The identifier is null, empty or whitespace.</exception>
        public TournamentBiome(string identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
            {
                throw new ArgumentException("A biome identifier cannot be null, empty or whitespace.", nameof(identifier));
            }

            Identifier = identifier.Trim();
        }

        /// <summary>The biome name as given, trimmed.</summary>
        public string Identifier { get; }

        public bool Equals(TournamentBiome? other) =>
            other is not null && StringComparer.OrdinalIgnoreCase.Equals(Identifier, other.Identifier);

        public override bool Equals(object? obj) => Equals(obj as TournamentBiome);

        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Identifier);

        public override string ToString() => Identifier;

        public static bool operator ==(TournamentBiome? left, TournamentBiome? right) =>
            left is null ? right is null : left.Equals(right);

        public static bool operator !=(TournamentBiome? left, TournamentBiome? right) => !(left == right);
    }
}
