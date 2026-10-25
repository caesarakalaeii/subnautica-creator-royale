namespace Lms.Core.Audit
{
    /// <summary>What one line of the audit log is about.</summary>
    /// <remarks>
    ///     Written to the log by name, so renaming one is a wire change; append new kinds and
    ///     classify them in <see cref="AuditRecord.IsAboutAPlayer"/>.
    /// </remarks>
    public enum AuditRecordKind
    {
        /// <summary>An operator started the match, or <c>flags.autoStart</c> did.</summary>
        MatchStarted,

        /// <summary>Paused. Nothing bites while paused (<c>MatchDirector.IsLive</c>).</summary>
        MatchPaused,

        MatchResumed,

        /// <summary>An operator brought a phase forward.</summary>
        MatchSkipped,

        /// <summary>The match was restarted for the same group, which invalidates per-player state.</summary>
        MatchReset,

        /// <summary>The next group is up. <c>Group</c> on every later record says which.</summary>
        GroupAdvanced,

        /// <summary>The warning window for the next phase opened.</summary>
        PhaseWarned,

        /// <summary>A phase is due: the wall starts closing on a smaller sphere.</summary>
        PhaseStarted,

        /// <summary>The final collapse finished and there is nowhere left to stand.</summary>
        ZoneClosed,

        /// <summary>
        ///     The match is over. Names the winner in <c>Player</c> when exactly one is left.
        /// </summary>
        MatchEnded,

        PlayerJoined,

        /// <summary>Left the server. Not the same as being eliminated, and scored differently.</summary>
        PlayerLeft,

        /// <summary>Where somebody was, sampled on a cadence.</summary>
        PlayerPosition,

        /// <summary>Inside the wall, outside where it is closing to: the warning before damage.</summary>
        ZoneWarned,

        /// <summary>One tick of damage for standing outside the wall. <c>Amount</c> is what it cost.</summary>
        ZoneBite,

        /// <summary>
        ///     Zone damage charged to a player on return for the time they were away. <c>Amount</c>
        ///     is the bill, <c>Health</c> what it leaves, <c>Detail</c> how long they were away.
        /// </summary>
        /// <remarks>
        ///     Derived by the server from its own log, so clients may not report it
        ///     (<c>PlayerEvent.CanBeReported</c>). <c>Position</c> is where the log last put them.
        /// </remarks>
        ZoneArrears,

        /// <summary>
        ///     One player damaged another. <c>Player</c> is the victim, <c>Actor</c> the attacker,
        ///     <c>Amount</c> the damage, <c>Health</c> what is left, <c>Detail</c> the tool.
        /// </summary>
        /// <remarks>
        ///     Written by the server, never a client, because it names another player. A refused
        ///     swing writes nothing.
        /// </remarks>
        PlayerHurt,

        /// <summary>
        ///     Out of the match. <c>Detail</c> is the cause; <c>Actor</c> names the player
        ///     responsible when the cause is unambiguous.
        /// </summary>
        PlayerEliminated,

        /// <summary>
        ///     An operator's ruling on who killed whom. <c>Player</c> is the victim and
        ///     <c>Actor</c> the killer, or null for nobody.
        /// </summary>
        /// <remarks>The last one for a victim wins; corrections are appended, never edited.</remarks>
        KillAttributed,

        /// <summary>Somebody fired a stasis field ring.</summary>
        /// <remarks>
        ///     <c>Amount</c> is the trigger charge from 0 to 1, not damage; it sets the field's size
        ///     and hold time.
        /// </remarks>
        RingFired,

        /// <summary>Caught by a ring. <c>Actor</c> is whoever fired it.</summary>
        PlayerCaged,

        /// <summary>The ring that was holding them expired.</summary>
        PlayerReleased,

        /// <summary>
        ///     An operator re-seated the group onto their pods. <c>Detail</c> says how many moved.
        /// </summary>
        /// <remarks>The <see cref="PlayerPosition"/> samples that follow record where they ended up.</remarks>
        PodsPlaced,

        /// <summary>
        ///     An operator removed the derelict escape pods. <c>Detail</c> counts the pods and the
        ///     stowed items removed with them.
        /// </summary>
        PodsCleared,

        /// <summary>
        ///     Where this match was drawn to finish. <c>Detail</c> is the destination name and
        ///     <c>Amount</c> the seed the tournament's order came from.
        /// </summary>
        ZoneDestinationDrawn,

        /// <summary>
        ///     A round was prepared by <c>lms round</c>. <c>Detail</c> names the contestants and the
        ///     kit; <c>Amount</c> is the hold time in seconds.
        /// </summary>
        /// <remarks>
        ///     The only record of who was in a match. One per group, so it is about the match rather
        ///     than a player.
        /// </remarks>
        RoundPrepared,

        /// <summary>
        ///     The rules were replaced from the casting booth. <c>Amount</c> is the revision;
        ///     <c>Detail</c> says who wrote it and what changed at the top level.
        /// </summary>
        /// <remarks>Written only when a match adopts the rules, not when they are merely offered.</remarks>
        RulesAdopted,

        /// <summary>
        ///     A contestant was put back into survival before a round. <c>Player</c> is who;
        ///     <c>Detail</c> is the mode they had.
        /// </summary>
        /// <remarks>Nitrox persists game mode per player, so a creative-mode body would ignore zone damage.</remarks>
        GameModeCorrected,

        /// <summary>A contestant made a beacon visible again and the rule hid it.</summary>
        /// <remarks>Reported by the client, which is the only side that draws beacons. Not proof of intent.</remarks>
        BeaconRefused,

        /// <summary>A client reported its world loaded and precache done.</summary>
        /// <remarks>Once per attempt at a match; the server derives readiness from these.</remarks>
        PlayerReady
    }
}
