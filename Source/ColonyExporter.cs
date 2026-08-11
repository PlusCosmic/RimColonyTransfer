using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimColonyTransfer
{
    /// <summary>
    /// Builds a <see cref="ColonyPayload"/> from the live game.
    ///
    /// Read-only: nothing here modifies the source save. The colony only actually
    /// leaves when the player abandons it or starts the destination game.
    /// </summary>
    public static class ColonyExporter
    {
        public static ColonyPayload Export(Map map, int gapTolerance = ComplexFinder.DefaultGapTolerance)
        {
            var player = Faction.OfPlayer;
            var payload = new ColonyPayload
            {
                colonyName = player.Name,
                gameVersion = VersionControl.CurrentVersionStringWithRev,
                modIds = LoadedModManager.RunningMods.Select(m => m.PackageId).ToList()
            };

            var complexes = ComplexFinder.Find(map, gapTolerance);
            foreach (var complex in complexes)
            {
                var built = SketchBuilder.Build(complex, map);
                payload.complexes.Add(new ComplexRecord
                {
                    sketch = built.sketch,
                    sourceOrigin = built.origin,
                    thingCount = built.thingsAdded,
                    terrainCount = built.terrainAdded
                });
            }

            payload.pawns = map.mapPawns.SpawnedPawnsInFaction(player)
                .Where(p => !p.Dead)
                .ToList();

            GatherItems(payload, map, complexes);
            GatherContainers(payload, complexes);
            GatherClosure(payload);
            return payload;
        }

        /// <summary>
        /// Items are scoped to the complexes: anything standing inside a base
        /// travels, anything out in the wild doesn't. Stone chunks are skipped —
        /// they're natural map litter that respawns anywhere.
        /// </summary>
        private static void GatherItems(ColonyPayload payload, Map map, List<Complex> complexes)
        {
            foreach (var thing in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (thing is Pawn) continue;
                if (thing.def.IsWithinCategory(ThingCategoryDefOf.Chunks)) continue;

                int index = IndexOfContaining(complexes, thing.Position);
                if (index < 0) continue;

                payload.items.Add(new ItemRecord
                {
                    thing = thing,
                    relativePos = thing.Position - complexes[index].Origin,
                    complexIndex = index
                });
            }
        }

        /// <summary>
        /// Graves and caskets hold their contents internally, so a sketch would
        /// place the building empty. Storage buildings need no help — their items
        /// sit on map cells and are picked up as ordinary ItemRecords.
        /// </summary>
        private static void GatherContainers(ColonyPayload payload, List<Complex> complexes)
        {
            for (int i = 0; i < complexes.Count; i++)
            {
                foreach (var thing in complexes[i].things)
                {
                    if (!(thing is IThingHolder holder)) continue;

                    var held = holder.GetDirectlyHeldThings();
                    if (held == null || held.Count == 0) continue;

                    payload.containers.Add(new ContainerRecord
                    {
                        relativePos = thing.Position - complexes[i].Origin,
                        complexIndex = i,
                        containerDefName = thing.def.defName,
                        contents = held.ToList()
                    });
                }
            }
        }

        private static int IndexOfContaining(List<Complex> complexes, IntVec3 pos)
        {
            for (int i = 0; i < complexes.Count; i++)
                if (complexes[i].bounds.Contains(pos))
                    return i;
            return -1;
        }

        /// <summary>
        /// The payload graph has to be closed — anything a pawn references that
        /// isn't in the file resolves to null on load. Walk the pawns and pull in
        /// what they point at.
        /// </summary>
        private static void GatherClosure(ColonyPayload payload)
        {
            var apparel = new HashSet<ApparelPolicy>();
            var food = new HashSet<FoodPolicy>();
            var reading = new HashSet<ReadingPolicy>();
            var ideos = new HashSet<Ideo>();
            var related = new HashSet<Pawn>();
            var exported = new HashSet<Pawn>(payload.pawns);

            foreach (var pawn in payload.pawns)
            {
                if (pawn.outfits?.CurrentApparelPolicy != null)
                    apparel.Add(pawn.outfits.CurrentApparelPolicy);
                if (pawn.foodRestriction?.CurrentFoodPolicy != null)
                    food.Add(pawn.foodRestriction.CurrentFoodPolicy);
                if (pawn.reading?.CurrentPolicy != null)
                    reading.Add(pawn.reading.CurrentPolicy);
                if (pawn.Ideo != null)
                    ideos.Add(pawn.Ideo);

                if (pawn.relations == null) continue;
                foreach (var other in pawn.relations.RelatedPawns)
                    if (other != null && !exported.Contains(other))
                        related.Add(other);
            }

            payload.apparelPolicies = apparel.ToList();
            payload.foodPolicies = food.ToList();
            payload.readingPolicies = reading.ToList();
            payload.relatedWorldPawns = related.ToList();

            // One ideo travels. A colony running several is rare; taking the most
            // common one and letting the rest null out is better than shipping a
            // partial IdeoManager into the destination.
            payload.ideo = ideos
                .OrderByDescending(i => payload.pawns.Count(p => p.Ideo == i))
                .FirstOrDefault();

            if (ideos.Count > 1)
            {
                Log.Warning(
                    $"[ColonyTransfer] Colony has {ideos.Count} ideoligions; exporting " +
                    $"'{payload.ideo?.name}' only. Pawns of other ideoligions will be " +
                    $"reassigned on import.");
            }
        }
    }
}
