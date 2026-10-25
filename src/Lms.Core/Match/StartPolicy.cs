using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lms.Core.Diagnostics;

namespace Lms.Core.Match
{
    /// <summary>
    ///     How a round begins: what everybody starts with, what they can build, and how long they are
    ///     held in a shrinking stasis field before the wall is live.
    /// </summary>
    public sealed class StartPolicy
    {
        private static readonly ReadOnlyCollection<string> Nothing =
            new ReadOnlyCollection<string>(new string[0]);

        /// <summary>
        ///     What <c>recipes:</c> is written as to mean "everything the game has". No <c>TechType</c>
        ///     has this name; it may not be combined with other names.
        /// </summary>
        public const string Everything = "all";

        public StartPolicy(TimeSpan hold, double fieldRadius,
            IReadOnlyList<string>? kit = null, IReadOnlyList<string>? lockedRecipes = null,
            IReadOnlyList<string>? recipes = null, RoundVitals? vitals = null)
        {
            if (hold < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(hold), hold,
                    "A hold does not run backwards. Write 0s to start the match as the round is prepared.");
            }

            if (fieldRadius <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldRadius), fieldRadius,
                    "A field with no radius holds nobody.");
            }

            Hold = hold;
            FieldRadius = fieldRadius;
            Kit = Named(kit);
            LockedRecipes = Named(lockedRecipes);
            Recipes = Named(recipes);
            Vitals = vitals ?? new RoundVitals(100, 100, 100);

            if (Recipes.Count > 1 && GrantsEverything)
            {
                throw new ArgumentException(
                    $"start.recipes says '{Everything}' and then names {Recipes.Count - 1} more. " +
                    "Everything already includes them; write one or the other.", nameof(recipes));
            }
        }

        /// <summary>
        ///     How long a contestant is held before the match starts. The clock starts when the hold
        ///     ends, not when it goes up. Zero means no hold.
        /// </summary>
        public TimeSpan Hold { get; }

        /// <summary>The field's nominal radius. What a player actually sees is the shrink of it.</summary>
        public double FieldRadius { get; }

        /// <summary>
        ///     What each contestant's inventory is cleared to. Empty by default, like a fresh player
        ///     whose supplies are in the pod's locker.
        /// </summary>
        public IReadOnlyList<string> Kit { get; }

        /// <summary>
        ///     What the recipe grant leaves out, as the game's tech type names. Only matters when
        ///     <see cref="Recipes"/> is <c>all</c>.
        /// </summary>
        public IReadOnlyList<string> LockedRecipes { get; }

        /// <summary>
        ///     What a round grants: the blueprint floor a contestant starts every match on. Empty is the
        ///     vanilla baseline; <see cref="Everything"/> grants every blueprint.
        /// </summary>
        /// <remarks>
        ///     The game re-grants vanilla blueprints on every load (<c>KnownTech.Initialize</c>), so the
        ///     floor can never go below vanilla. An unwritten section grants the least, not the most.
        /// </remarks>
        public IReadOnlyList<string> Recipes { get; }

        /// <summary>Whether the floor is every blueprint the game has.</summary>
        public bool GrantsEverything =>
            Recipes.Count > 0 &&
            string.Equals(Recipes[0], Everything, StringComparison.OrdinalIgnoreCase);

        /// <summary>The health, food and water a contestant starts the round with. Defaults to full.</summary>
        public RoundVitals Vitals { get; }

        /// <summary>Whether anybody is held at all.</summary>
        public bool Holds => Hold > TimeSpan.Zero;

        /// <summary>
        ///     Thirty seconds in a twelve-metre bubble, empty hands, and no vehicle. The hold is
        ///     deliberately longer than the game's own twenty-second stasis cap.
        /// </summary>
        public static StartPolicy Default { get; } = new StartPolicy(
            TimeSpan.FromSeconds(30), 12,
            kit: null,
            lockedRecipes: new[] { "Seamoth", "Exosuit", "Cyclops" });

        public override string ToString() =>
            Readable.Line(
                $"{(Holds ? $"held {Hold.TotalSeconds:0.#}s in a {FieldRadius:0.#}m field" : "no hold")}, ",
                $"{(Kit.Count == 0 ? "empty handed" : $"{Kit.Count} item(s)")}, ",
                $"{Granted()}");

        /// <summary>What a contestant gets, phrased for the operator running <c>lms round</c>.</summary>
        private string Granted()
        {
            if (GrantsEverything)
            {
                return LockedRecipes.Count == 0
                    ? "every recipe"
                    : $"every recipe but {LockedRecipes.Count}";
            }

            return Recipes.Count == 0
                ? "the vanilla recipes"
                : $"the vanilla recipes and {Recipes.Count} more";
        }

        private static ReadOnlyCollection<string> Named(IReadOnlyList<string>? names)
        {
            if (names == null || names.Count == 0)
            {
                return Nothing;
            }

            List<string> kept = new List<string>(names.Count);
            foreach (string name in names)
            {
                if (!string.IsNullOrWhiteSpace(name))
                {
                    kept.Add(name.Trim());
                }
            }

            return new ReadOnlyCollection<string>(kept);
        }
    }
}
