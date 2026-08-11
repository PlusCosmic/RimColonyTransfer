using System.Linq;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse;

namespace RimColonyTransfer
{
    public static class DebugActions
    {
        private const string Category = "Colony Transfer";

        [DebugAction(Category, "Find complexes", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void FindComplexes()
        {
            var map = Find.CurrentMap;
            var complexes = ComplexFinder.Find(map);

            var sb = new StringBuilder();
            sb.AppendLine($"Found {complexes.Count} complexes on {map}:");
            for (int i = 0; i < complexes.Count; i++)
            {
                var c = complexes[i];
                var top = c.things
                    .GroupBy(t => t.def)
                    .OrderByDescending(g => g.Count())
                    .Take(5)
                    .Select(g => $"{g.Key.defName}({g.Count()})");
                sb.AppendLine($"  [{i}] {c.Count} things, {c.bounds.Width}x{c.bounds.Height} " +
                              $"at {c.Origin} — {string.Join(", ", top)}");
            }
            Log.Message(sb.ToString());
        }

        /// <summary>
        /// The offline analysis picked a gap tolerance by finding plateaus in this
        /// sweep. Re-running it in game confirms the port agrees with that result.
        /// </summary>
        [DebugAction(Category, "Sweep gap tolerance", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void SweepTolerance()
        {
            var map = Find.CurrentMap;
            var sb = new StringBuilder("Gap tolerance sweep (tolerance: component sizes)\n");
            for (int tol = 1; tol <= 15; tol++)
            {
                var sizes = ComplexFinder.Find(map, tol).Select(c => c.Count);
                sb.AppendLine($"  tol {tol,2}: [{string.Join(", ", sizes)}]");
            }
            Log.Message(sb.ToString());
        }

        /// <summary>
        /// Export, then immediately read the file back and report what survived.
        /// Loading exercises the whole cross-ref path, so this reveals which
        /// references actually resolve before any of it is pointed at a real
        /// destination save — and it touches nothing but a file on disk.
        /// </summary>
        [DebugAction(Category, "Export colony (round-trip)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ExportRoundTrip()
        {
            var map = Find.CurrentMap;
            var payload = ColonyExporter.Export(map);
            string path = PayloadIO.PathFor(GenText.SanitizeFilename(payload.colonyName));

            PayloadIO.Save(payload, path);
            Log.Message($"[ColonyTransfer] Wrote {path}\n  exported: {payload.Summary()}");

            var loaded = PayloadIO.Load(path);
            if (loaded == null)
            {
                Log.Error("[ColonyTransfer] Round-trip failed: payload came back null.");
                return;
            }

            var sb = new StringBuilder("[ColonyTransfer] Round-trip result\n");
            sb.AppendLine($"  reloaded: {loaded.Summary()}");
            sb.AppendLine($"  complexes: {loaded.complexes.Count} " +
                          $"(things {loaded.complexes.Sum(c => c.thingCount)})");
            sb.AppendLine($"  ideo:      {loaded.ideo?.name ?? "<null>"}");
            sb.AppendLine($"  policies:  {loaded.apparelPolicies.Count} apparel, " +
                          $"{loaded.foodPolicies.Count} food, {loaded.readingPolicies.Count} reading");
            sb.AppendLine($"  relatives: {loaded.relatedWorldPawns.Count}");

            int humanlike = loaded.pawns.Count(p => p.RaceProps.Humanlike);
            int animals = loaded.pawns.Count(p => p.RaceProps.Animal);
            int nullIdeo = loaded.pawns.Count(p => p.Ideo == null && p.ShouldHaveIdeo);
            sb.AppendLine($"  pawns:     {loaded.pawns.Count} " +
                          $"({humanlike} humanlike, {animals} animal), " +
                          $"{nullIdeo} wrongly missing an ideo");

            // Faction isn't a Scribe_References field — Thing.ExposeData resolves it
            // against the LIVE FactionManager by load ID. Round-tripping in the same
            // save therefore always resolves; in a destination save the same ID would
            // silently match a different faction. The importer must overwrite it.
            var factions = loaded.pawns.Select(p => p.Faction?.Name ?? "<null>").Distinct();
            sb.AppendLine($"  factions:  {string.Join(", ", factions)} " +
                          $"(resolved against the live game, NOT the payload)");

            // Anyone in a caravan or on another map isn't on this map, so the export
            // never saw them.
            int allColonists = PawnsFinder
                .AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Count;
            if (allColonists != humanlike)
            {
                sb.AppendLine($"  WARNING:   game has {allColonists} free colonists but only " +
                              $"{humanlike} humanlike were exported — the rest are off-map " +
                              $"(caravan, other map, or transport).");
            }
            Log.Message(sb.ToString());
        }

        [DebugAction(Category, "Build sketches", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void BuildSketches()
        {
            var map = Find.CurrentMap;
            var complexes = ComplexFinder.Find(map);
            var results = SketchBuilder.BuildAll(complexes, map);

            var sb = new StringBuilder($"Built {results.Count} sketches:\n");
            for (int i = 0; i < results.Count; i++)
                sb.AppendLine($"  [{i}] origin {results[i].origin} — {results[i]}");
            Log.Message(sb.ToString());
        }
    }
}
