using System.Collections.Generic;
using RimWorld;
using Verse;

namespace RimColonyTransfer
{
    /// <summary>
    /// Turns a <see cref="Complex"/> into a <see cref="Sketch"/>.
    ///
    /// This is the step that makes cross-save transfer safe: a SketchThing carries
    /// only def, stuff, rotation, quality and hit points — no load IDs — so nothing
    /// in the payload can collide with an object in the destination save.
    /// </summary>
    public static class SketchBuilder
    {
        public class Result
        {
            public Sketch sketch;
            public IntVec3 origin;
            public int thingsAdded;
            public int thingsRefused;
            public int terrainAdded;
            public int terrainRefused;

            public override string ToString() =>
                $"{thingsAdded} things ({thingsRefused} refused), " +
                $"{terrainAdded} terrain ({terrainRefused} refused)";
        }

        public static Result Build(Complex complex, Map map)
        {
            var result = new Result
            {
                sketch = new Sketch(),
                origin = complex.Origin
            };

            // Terrain goes in before things. Sketch.WouldCollide checks incoming
            // terrain against the affordance needs of things already in the sketch,
            // and a floor laid over a bridge doesn't satisfy a wall's Heavy
            // affordance on its own — so adding things first gets the floor refused.
            // Ground before structures is the right order anyway.
            foreach (var cell in complex.bounds)
            {
                AddTerrain(result, map.terrainGrid.FoundationAt(cell), cell);

                var top = map.terrainGrid.TerrainAt(cell);
                if (top != null && (top.layerable || top.bridge))
                    AddTerrain(result, top, cell);
            }

            foreach (var t in complex.things)
            {
                QualityCategory? quality = t.TryGetQuality(out var q) ? q : (QualityCategory?)null;
                int? hitPoints = t.def.useHitPoints ? t.HitPoints : (int?)null;

                bool ok = result.sketch.AddThing(
                    t.def,
                    t.Position - result.origin,
                    t.Rotation,
                    t.Stuff,
                    t.stackCount,
                    quality,
                    hitPoints,
                    wipeIfCollides: false);

                if (ok) result.thingsAdded++;
                else result.thingsRefused++;
            }

            return result;
        }

        private static void AddTerrain(Result result, TerrainDef def, IntVec3 cell)
        {
            if (def == null) return;

            if (result.sketch.AddTerrain(def, cell - result.origin, wipeIfCollides: false))
                result.terrainAdded++;
            else
                result.terrainRefused++;
        }

        public static List<Result> BuildAll(IEnumerable<Complex> complexes, Map map)
        {
            var results = new List<Result>();
            foreach (var c in complexes)
                results.Add(Build(c, map));
            return results;
        }
    }
}
