using System;
using Lms.Core.Combat;
using Lms.Core.Configuration;
using Lms.Core.Match;
using Nitrox.Model.Logger;
using Nitrox.Model.Subnautica.Tournament;
using UnityEngine;

namespace Lms.Game
{
    /// <summary>
    ///     Receives the server's match-state messages and hands each to the component that owns it.
    /// </summary>
    /// <remarks>
    ///     The server is the only author of the zone; <see cref="MatchFollower"/> holds what arrived
    ///     and everything else on this client reads the wall from it.
    /// </remarks>
    internal sealed class MatchListener : MonoBehaviour
    {
        private MatchFollower? follower;
        private ZoneEnforcer? zone;
        private LmsRuntime? runtime;
        private GhostCamera? camera;
        private CasterSightings? sightings;
        private RoundKit? kit;
        private RoundRecipes? recipes;
        private SpectatorAdapter? spectating;
        private bool wasInSession;

        /// <summary>
        ///     The enforcer this listener hands a zone, a bill and a hit to.
        /// </summary>
        /// <remarks>
        ///     Resolved on use, not in <see cref="Awake"/>: <c>AddComponent</c> runs <c>Awake</c>
        ///     synchronously, so siblings that <see cref="LmsRuntime"/> adds later do not exist yet.
        /// </remarks>
        private ZoneEnforcer? Zone => zone != null ? zone : zone = GetComponent<ZoneEnforcer>();

        /// <summary>
        ///     The component that empties an inventory to what the round starts with. Resolved on use,
        ///     like <see cref="Zone"/>.
        /// </summary>
        private RoundKit? Kit => kit != null ? kit : kit = GetComponent<RoundKit>();

        /// <summary>
        ///     The component that grants the round's blueprints. Resolved on use, like <see cref="Zone"/>.
        /// </summary>
        private RoundRecipes? Recipes => recipes != null ? recipes : recipes = GetComponent<RoundRecipes>();

        /// <summary>
        ///     The component that hands a body to the free camera. Resolved on use, like <see cref="Zone"/>.
        /// </summary>
        private SpectatorAdapter? Spectating =>
            spectating != null ? spectating : spectating = GetComponent<SpectatorAdapter>();

        /// <summary>
        ///     The component that flies a ghost's camera. Resolved on use, like <see cref="Zone"/>.
        /// </summary>
        /// <remarks>
        ///     Looks go through it because only it knows whether it holds the camera; this must not
        ///     move a developer's own <c>freecam</c>.
        /// </remarks>
        private GhostCamera? Camera =>
            camera != null ? camera : camera = GetComponent<GhostCamera>();

        /// <summary>Where the caster feed and the standing follow are kept.</summary>
        private CasterSightings? Sightings =>
            sightings != null ? sightings : sightings = GetComponent<CasterSightings>();

        private void Awake()
        {
            runtime = GetComponent<LmsRuntime>();
            follower = runtime?.Follower;

            if (follower == null)
            {
                enabled = false;
                return;
            }

            LmsMatchStateRelay.Received += OnMatchState;
        }

        private void OnDestroy()
        {
            LmsMatchStateRelay.Received -= OnMatchState;
        }

        /// <remarks>
        ///     This object outlives a session, so leaving one must clear all match state, or the old
        ///     wall is drawn over the menu and the start of the next session.
        /// </remarks>
        private void Update()
        {
            bool inSession = Player.main != null;
            if (inSession == wasInSession)
            {
                return;
            }

            wasInSession = inSession;

            if (!inSession && follower!.HasMatch)
            {
                follower.Forget();

                // Session state goes with the match, so nothing stale applies to the next session.
                runtime?.ForgetRole();

                RemotePlayerVisibility.Forget();

                Kit?.Forget();
                Recipes?.Forget();

                MedKitGate.Forget();

                Spectating?.Forget();

                Log.Info($"{LmsMod.LogPrefix} left the session; waiting to be told about the next match");
            }
        }

        private void OnMatchState(string payload)
        {
            if (follower == null)
            {
                return;
            }

            // Every message shares one channel and is told apart by its header. Tried before the zone
            // so none of them is reported as an unreadable wall.
            if (GhostBroadcast.IsGhostFlag(payload))
            {
                Role(payload);
                return;
            }

            if (HiddenBroadcast.IsHidden(payload))
            {
                Undrawn(payload);
                return;
            }

            if (RulesBroadcast.IsRules(payload))
            {
                Rules(payload);
                return;
            }

            if (RoundBroadcast.IsRound(payload))
            {
                Starting(payload);
                return;
            }

            if (ArrearsBroadcast.IsArrears(payload))
            {
                Owed(payload);
                return;
            }

            if (HurtBroadcast.IsHurt(payload))
            {
                Hit(payload);
                return;
            }

            if (StrikeRefusedBroadcast.IsRefusal(payload))
            {
                Refused(payload);
                return;
            }

            if (EliminatedBroadcast.IsEliminated(payload))
            {
                Out(payload);
                return;
            }

            if (LookBroadcast.IsLook(payload))
            {
                Looking(payload);
                return;
            }

            if (CasterFeed.IsFeed(payload))
            {
                Filming(payload);
                return;
            }

            if (FollowBroadcast.IsFollow(payload))
            {
                Staying(payload);
                return;
            }

            if (!MatchBroadcast.TryDecode(payload, out MatchBroadcast broadcast))
            {
                // Unreadable means a different server build; say so rather than lose the wall silently.
                Log.Warn($"{LmsMod.LogPrefix} could not read the zone the server sent; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            bool news = !follower.HasMatch || follower.Generation != broadcast.Generation;

            follower.Receive(broadcast);

            if (news)
            {
                Log.Info($"{LmsMod.LogPrefix} following the server's match: {broadcast.StatusAt(WorldClock.Now)}");
            }
        }

        /// <summary>
        ///     Takes what the server says this client's role in the match is; held on the runtime.
        /// </summary>
        /// <remarks>
        ///     An unreadable message leaves the flag alone, so a contestant is never made immortal by a
        ///     parse failure.
        /// </remarks>
        private void Role(string payload)
        {
            if (!GhostBroadcast.TryDecode(payload, out GhostBroadcast role))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read the role the server sent; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            runtime?.Told(role);
        }

        /// <summary>
        ///     Takes the booth's instruction to point this camera at somebody, for <see cref="GhostCamera"/>.
        /// </summary>
        private void Looking(string payload)
        {
            if (!LookBroadcast.TryDecode(payload, out LookBroadcast look))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read where the booth wants the camera; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            Camera?.LookAt(look);
        }

        /// <summary>
        ///     Takes the tick of where every contestant is and how they are doing.
        /// </summary>
        /// <remarks>
        ///     The server sends this only to ghosts; a contestant must never see opponents' positions.
        /// </remarks>
        private void Filming(string payload)
        {
            if (!CasterFeed.TryDecode(payload, out CasterFeed feed))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read the caster feed the server sent; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            Sightings?.Receive(feed);
        }

        /// <summary>
        ///     Takes the booth's standing instruction to keep this camera on somebody.
        /// </summary>
        private void Staying(string payload)
        {
            if (!FollowBroadcast.TryDecode(payload, out FollowBroadcast follow))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read who the booth wants followed; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            Sightings?.Told(follow);
        }

        /// <summary>
        ///     Takes the instruction for a round that is starting, for <see cref="RoundKit"/> and
        ///     <see cref="RoundRecipes"/>.
        /// </summary>
        private void Starting(string payload)
        {
            if (!RoundBroadcast.TryDecode(payload, out RoundBroadcast round))
            {
                // Nothing is emptied: inventory items are destroyed, so a misread is unrecoverable.
                Log.Warn($"{LmsMod.LogPrefix} could not read the round the server sent, so nothing " +
                         $"was cleared; is this the same build? '{payload}'");
                return;
            }

            RoundKit? clearing = Kit;

            if (clearing == null)
            {
                // Logged: otherwise a round that cleared nothing looks like an already-empty inventory.
                Log.Warn($"{LmsMod.LogPrefix} the server sent a round ({round}) and there is nothing " +
                         "on this client to clear an inventory with; is the config readable?");
                return;
            }

            clearing.Told(round);

            // After the kit: the grant spreads over frames, the clear does not. A missing grant
            // component does not stop the clear.
            RoundRecipes? grant = Recipes;

            if (grant == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} the server sent a round ({round}) and there is nothing " +
                         "on this client to grant recipes with; is the config readable?");
                return;
            }

            grant.Told(round);
        }

        /// <summary>
        ///     Takes the list of players this client is not to draw.
        /// </summary>
        /// <remarks>
        ///     Rendering happens on the observer's machine, so contestants must be told ghosts' names;
        ///     a name is not a competitive fact, a position would be.
        /// </remarks>
        private void Undrawn(string payload)
        {
            if (!HiddenBroadcast.TryDecode(payload, out HiddenBroadcast hidden))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read who not to draw; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            RemotePlayerVisibility.Told(hidden.Names);
        }

        /// <summary>
        ///     Takes tournament rules the server has replaced.
        /// </summary>
        /// <remarks>
        ///     Parsed with the same loader as the file. A document this client cannot load is refused
        ///     and the old rules stay: running half a config is worse than running the previous one.
        /// </remarks>
        private void Rules(string payload)
        {
            if (!RulesBroadcast.TryDecode(payload, out RulesBroadcast rules))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read the rules the server sent; " +
                         "is this the same build?");
                return;
            }

            TournamentConfigLoadResult result = TournamentConfigLoader.Load(rules.Yaml);
            if (!result.IsValid)
            {
                // Every problem, with the field that caused it.
                Log.Error($"{LmsMod.LogPrefix} the server sent rules this build cannot read " +
                          $"({result.Problems.Count} problem(s)); this client keeps the ones it has. " +
                          "Are the two builds the same?");
                foreach (TournamentConfigProblem problem in result.Problems)
                {
                    Log.Error($"{LmsMod.LogPrefix}   {problem}");
                }

                return;
            }

            runtime?.Adopt(result.Config!, rules.Revision);
        }

        /// <summary>
        ///     Takes the bill for the time this client was away and hands it to the zone.
        /// </summary>
        private void Owed(string payload)
        {
            if (!ArrearsBroadcast.TryDecode(payload, out ArrearsBroadcast bill))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read the arrears the server sent; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            ZoneEnforcer? enforcer = Zone;

            if (enforcer == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} the server sent arrears ({bill}) and there is no zone " +
                         "on this client to apply them; is the config readable?");
                return;
            }

            enforcer.Owed(bill);
        }

        /// <summary>
        ///     Takes a hit the server judged and hands it to <see cref="ZoneEnforcer"/>, the one owner
        ///     of this player's health.
        /// </summary>
        private void Hit(string payload)
        {
            if (!HurtBroadcast.TryDecode(payload, out HurtBroadcast hit))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read the hit the server sent; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            ZoneEnforcer? enforcer = Zone;

            if (enforcer == null)
            {
                Log.Warn($"{LmsMod.LogPrefix} the server sent a hit ({hit}) and there is nothing on " +
                         "this client to apply it to; is the config readable?");
                return;
            }

            // Runs inside Nitrox's packet pump (Multiplayer.Update): an exception would take the
            // session's packets with it.
            try
            {
                enforcer.Hurt(hit);
            }
            catch (Exception ex)
            {
                Log.Error($"{LmsMod.LogPrefix} could not apply {hit}: {ex}");
            }
        }

        /// <summary>
        ///     Takes the server's word that this player is already out of the match, for
        ///     <see cref="SpectatorAdapter"/>.
        /// </summary>
        /// <remarks>
        ///     An unreadable message leaves the player in the match, the safe direction.
        /// </remarks>
        private void Out(string payload)
        {
            if (!EliminatedBroadcast.TryDecode(payload, out EliminatedBroadcast verdict))
            {
                Log.Warn($"{LmsMod.LogPrefix} could not read an elimination the server sent; " +
                         $"is this the same build? '{payload}'");
                return;
            }

            SpectatorAdapter? spectator = Spectating;

            if (spectator == null)
            {
                // Logged: otherwise this looks like a client with nothing to act on.
                Log.Warn($"{LmsMod.LogPrefix} the server says this player is out of " +
                         $"{verdict.Group}.{verdict.Generation} and there is nothing on this client " +
                         "to hand the camera over with; is the config readable?");
                return;
            }

            spectator.Told(verdict);
        }

        /// <summary>
        ///     Says why a swing this client made did nothing. The arms animate and play the hit sound
        ///     regardless, so without this a refused swing looks like a hit.
        /// </summary>
        private void Refused(string payload)
        {
            if (!StrikeRefusedBroadcast.TryDecode(payload, out StrikeRefusedBroadcast refusal))
            {
                // An unknown reason means a different build; logged, not shown.
                Log.Warn($"{LmsMod.LogPrefix} the server refused a swing for a reason this build " +
                         $"does not know; is this the same build? '{payload}'");
                return;
            }

            try
            {
                ErrorMessage.AddMessage($"LMS: {refusal.Sentence}");
            }
            catch (Exception ex)
            {
                // Runs inside Nitrox's packet pump, like the hit above.
                Log.Error($"{LmsMod.LogPrefix} could not say why a swing was refused: {ex}");
            }
        }
    }
}
