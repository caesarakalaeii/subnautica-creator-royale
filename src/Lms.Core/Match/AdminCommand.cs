namespace Lms.Core.Match
{
    /// <summary>What an operator can do to a live match, from the console, settings buttons or backend.</summary>
    public enum AdminCommand
    {
        /// <summary>Report where the match is. Changes nothing.</summary>
        Status,

        /// <summary>Start the match in the lobby.</summary>
        Start,

        /// <summary>Hold a running match. The phase that was coming is still coming.</summary>
        Pause,

        /// <summary>Carry on from where the pause left it.</summary>
        Resume,

        /// <summary>Start the next phase now and restart the cadence from this moment.</summary>
        Skip,

        /// <summary>Replay this group: back to the lobby, no phases fired, the opening sphere back.</summary>
        Reset,

        /// <summary>Move on to the next group of five.</summary>
        NextGroup,

        /// <summary>
        ///     Lay the group out again: everybody present onto the pods the placement wants them on.
        ///     Does nothing to the clock.
        /// </summary>
        /// <remarks>
        ///     A pod is otherwise assigned only on first join, so a group that connected during another
        ///     match would start wherever it happens to be standing.
        /// </remarks>
        PodSpawn,

        /// <summary>Take out the escape pods the layout has no use for and nobody is in.</summary>
        /// <remarks>
        ///     Its own verb so deleting entities is never a side effect of <see cref="PodSpawn"/>.
        ///     Derelicts left by a changed <c>spawn.circleRadius</c> count towards the layout's separation.
        /// </remarks>
        PodClear,

        /// <summary>
        ///     Take out every escape pod the world holds, so the next <see cref="PodSpawn"/> starts from
        ///     nothing (<c>lms podclear all</c>).
        /// </summary>
        /// <remarks>
        ///     Typed only, with no settings-tab button. Refuses, naming them, while anybody is standing in
        ///     a pod, since removing it would leave them treading water.
        /// </remarks>
        PodClearAll,

        /// <summary>
        ///     Prepare a round and start it when the hold ends (<c>lms round</c>): sweep pods, lay out the
        ///     circle, clear inventories to the kit, grant recipes, seat everybody and hold them.
        /// </summary>
        /// <remarks>
        ///     Every step is destructive, so it refuses up front rather than half-going: outside the lobby,
        ///     or when the group is not <c>spawn.groupSize</c>.
        /// </remarks>
        Round,

        /// <summary>
        ///     Prepare a round with the wrong number of players (<c>lms round force</c>). Overrides the
        ///     count only, never the lobby check.
        /// </summary>
        RoundForce,

        /// <summary>
        ///     Take one player out of the match without taking them out of the world
        ///     (<c>lms ghost &lt;player&gt;</c>).
        /// </summary>
        /// <remarks>
        ///     For someone who joins unexpectedly; the planned crew lives in <c>ghosts:</c>. A ghost is a
        ///     live non-contestant, not a spectator (a dead contestant). Nothing is recorded.
        /// </remarks>
        Ghost,

        /// <summary>Put a ghost back in the match (<c>lms unghost &lt;player&gt;</c>).</summary>
        /// <remarks>
        ///     Ghosts stay in survival and get immunity from the mod, not creative mode, because Nitrox
        ///     persists game mode per player and creative would survive into a later match.
        /// </remarks>
        Unghost,

        /// <summary>Jump every ghost's camera to one contestant (<c>lms look &lt;player&gt;</c>).</summary>
        /// <remarks>
        ///     A one-off placement; see <see cref="Follow"/> to stay on them. Refuses by name an unknown
        ///     contestant or another ghost. Nothing is recorded.
        /// </remarks>
        Look,

        /// <summary>Keep every ghost's camera on one contestant (<c>lms follow &lt;player&gt;</c>).</summary>
        /// <remarks>
        ///     Refuses what <see cref="Look"/> refuses. The camera is leashed, not pinned
        ///     (<see cref="CasterShot"/>), so each caster keeps their own angle.
        /// </remarks>
        Follow,

        /// <summary>Give the cameras back to the people flying them (<c>lms unfollow</c>). Never refuses.</summary>
        Unfollow,

        /// <summary>
        ///     Point one ghost's camera at one contestant
        ///     (<c>lms followOne &lt;ghost&gt; &lt;target&gt;</c>).
        /// </summary>
        /// <remarks>
        ///     Takes two names, so it is typed only and has no settings-tab button. Refuses what
        ///     <see cref="Follow"/> refuses, then checks the ghost.
        /// </remarks>
        FollowOne,
    }
}
