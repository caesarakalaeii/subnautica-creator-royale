using Lms.Core.Results;
using Lms.Core.Zones;

namespace Lms.Core.Telemetry
{
    /// <summary>
    ///     Serialises a <see cref="TelemetrySnapshot"/> to the telemetry contract JSON. The only place
    ///     that knows the wire shape.
    /// </summary>
    /// <remarks>
    ///     Every field is written every time, null ones included, so readers handle a single shape.
    /// </remarks>
    public static class TelemetryJson
    {
        public static string Write(TelemetrySnapshot snapshot)
        {
            JsonText json = new JsonText();

            json.OpenObject();
            json.Number("v", TelemetrySnapshot.Version);
            json.Time("at", snapshot.At);
            WriteMatch(json, snapshot.Match);
            WriteZone(json, snapshot.Zone);

            // The revision precedes the block it describes, so a streaming reader sees it first.
            json.Number("standingsRevision", snapshot.StandingsRevision);
            WriteStandings(json, snapshot.Standings);

            json.OpenArray("players");
            foreach (PlayerTelemetry player in snapshot.Players)
            {
                WritePlayer(json, player);
            }

            json.CloseArray();

            json.OpenArray("events");
            foreach (EventTelemetry item in snapshot.Events)
            {
                WriteEvent(json, item);
            }

            json.CloseArray();

            // Appended last so older readers that skip unknown keys are unaffected.
            json.OpenArray("pods");
            foreach (PodTelemetry pod in snapshot.Pods)
            {
                json.OpenObject();
                json.Number("x", pod.X);
                json.Number("y", pod.Y);
                json.Number("z", pod.Z);
                json.Number("assigned", pod.Assigned);
                json.CloseObject();
            }

            json.CloseArray();
            json.CloseObject();

            return json.ToString();
        }

        private static void WriteMatch(JsonText json, MatchTelemetry? match)
        {
            if (match == null)
            {
                json.Null("match");
                return;
            }

            json.OpenObject("match");
            json.Number("group", match.Group);
            json.Number("generation", match.Generation);
            json.Text("state", match.State.ToString());
            json.Number("elapsedMs", (long)match.Elapsed.TotalMilliseconds);
            json.Number("phase", match.Phase);
            json.Number("phases", match.Phases);

            if (match.UntilNextPhase.HasValue)
            {
                json.Number("untilNextMs", (long)match.UntilNextPhase.Value.TotalMilliseconds);
            }
            else
            {
                json.Null("untilNextMs");
            }

            json.Number("alive", match.Alive);
            json.CloseObject();
        }

        /// <summary>
        ///     Writes the standings in the order <see cref="Lms.Core.Results.Standings"/> produced; nothing
        ///     here or downstream re-sorts them.
        /// </summary>
        private static void WriteStandings(JsonText json, StandingsTelemetry? standings)
        {
            if (standings == null)
            {
                json.Null("standings");
                return;
            }

            json.OpenObject("standings");
            json.Number("matches", standings.Matches);

            // Table-level: the booth uses it to add its own kill attributions.
            json.Number("pointsPerKill", standings.PointsPerKill);

            json.OpenArray("entries");
            foreach (StandingsLine line in standings.Entries)
            {
                json.OpenObject();
                json.Number("place", line.Place);
                json.Text("name", line.Name);
                json.Number("points", line.Points);
                json.Number("played", line.Played);
                json.Number("wins", line.Wins);
                json.Number("best", line.BestPlacement);
                json.Number("kills", line.Kills);
                json.Number("outlasted", line.Outlasted);
                json.CloseObject();
            }

            json.CloseArray();

            // Per-match results, for bracket seeding the summed rows cannot provide. Always present.
            json.OpenArray("games");
            foreach (GameResult game in standings.Games)
            {
                json.OpenObject();
                json.Number("group", game.Group);
                json.Number("generation", game.Generation);

                json.OpenArray("entries");
                foreach (GameResult.GameLine line in game.Lines)
                {
                    json.OpenObject();
                    json.Text("name", line.Name);
                    json.Number("points", line.Points);
                    json.Number("placement", line.Placement);
                    json.Number("kills", line.Kills);
                    json.Number("outlasted", line.Outlasted);
                    json.CloseObject();
                }

                json.CloseArray();
                json.CloseObject();
            }

            json.CloseArray();
            json.CloseObject();
        }

        private static void WriteZone(JsonText json, ZoneTelemetry? zone)
        {
            if (zone == null)
            {
                json.Null("zone");
                return;
            }

            json.OpenObject("zone");
            WriteSphere(json, "now", zone.Now);

            if (zone.Next.HasValue)
            {
                WriteSphere(json, "next", zone.Next.Value);
            }
            else
            {
                json.Null("next");
            }

            json.Number("damagePerTick", zone.DamagePerTick);
            json.CloseObject();
        }

        private static void WriteSphere(JsonText json, string name, SphereTelemetry sphere)
        {
            json.OpenObject(name);
            json.Number("x", sphere.X);
            json.Number("y", sphere.Y);
            json.Number("z", sphere.Z);
            json.Number("r", sphere.Radius);
            json.CloseObject();
        }

        private static void WritePlayer(JsonText json, PlayerTelemetry player)
        {
            json.OpenObject();
            json.Text("name", player.Name);
            json.Flag("alive", player.Alive);
            json.Number("x", player.Position.X);
            json.Number("y", player.Position.Y);
            json.Number("z", player.Position.Z);
            json.Number("depth", player.Depth);
            json.Text("biome", player.Biome);
            json.Number("health", player.Health);
            json.Number("oxygen", player.Oxygen);
            json.Number("food", player.Food);
            json.Number("water", player.Water);
            json.Text("vehicle", player.Vehicle);
            json.Text("held", player.HeldItem);

            // Stasis rifle state, beside the held item.
            json.Flag("rifle", player.Rifle);
            json.Number("rifleCharge", player.RifleCharge);
            json.Flag("frozen", player.Frozen);

            json.Flag("insideZone", player.InsideZone);
            json.Number("distanceOutside", player.DistanceOutside);

            json.OpenArray("inventory");
            foreach (InventoryLine line in player.Inventory)
            {
                json.OpenObject();
                json.Text("item", line.Item);
                json.Number("count", line.Count);
                json.CloseObject();
            }

            json.CloseArray();

            json.OpenArray("equipment");
            foreach (EquipmentLine line in player.Equipment)
            {
                json.OpenObject();
                json.Text("slot", line.Slot);
                json.Text("item", line.Item);
                json.CloseObject();
            }

            json.CloseArray();
            json.CloseObject();
        }

        private static void WriteEvent(JsonText json, EventTelemetry item)
        {
            json.OpenObject();
            json.Number("seq", item.Sequence);
            json.Number("group", item.Group);
            json.Number("generation", item.Generation);
            json.Time("at", item.At);
            json.Number("elapsedMs", (long)item.Elapsed.TotalMilliseconds);
            json.Text("kind", item.Kind.ToString());
            json.Text("player", item.Player);
            json.Text("actor", item.Actor);
            Optional(json, "phase", item.Phase);
            Optional(json, "amount", item.Amount);
            json.Text("detail", item.Detail);
            WhereItHappened(json, item.Position);
            json.Text("biome", item.Biome);
            json.CloseObject();
        }

        /// <summary>
        ///     Writes a flattened position, or nulls: (0, 0, 0) is a real place, so it cannot mean "none".
        /// </summary>
        private static void WhereItHappened(JsonText json, WorldPoint? position)
        {
            if (!position.HasValue)
            {
                json.Null("x");
                json.Null("y");
                json.Null("z");
                return;
            }

            json.Number("x", position.Value.X);
            json.Number("y", position.Value.Y);
            json.Number("z", position.Value.Z);
        }

        private static void Optional(JsonText json, string name, int? value)
        {
            if (value.HasValue)
            {
                json.Number(name, value.Value);
            }
            else
            {
                json.Null(name);
            }
        }

        private static void Optional(JsonText json, string name, double? value)
        {
            if (value.HasValue)
            {
                json.Number(name, value.Value);
            }
            else
            {
                json.Null(name);
            }
        }
    }
}
