using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimColonyTransfer
{
    /// <summary>
    /// A connected group of player buildings — one base. Each complex is placed
    /// independently at the destination, so a colony split across a river doesn't
    /// need matching terrain to land on.
    /// </summary>
    public class Complex
    {
        public readonly List<Thing> things = new List<Thing>();
        public CellRect bounds;

        public IntVec3 Origin => bounds.Min;
        public int Count => things.Count;

        public override string ToString() =>
            $"Complex({Count} things, {bounds.Width}x{bounds.Height} at {Origin})";
    }

    public static class ComplexFinder
    {
        /// <summary>
        /// Cells of slack allowed between two buildings before they're considered
        /// separate bases. Derived empirically: sweeping this on a real save shows
        /// plateaus, and the value sits inside the plateau that separates bases
        /// across a river while keeping each base whole.
        /// </summary>
        public const int DefaultGapTolerance = 11;

        /// <summary>Complexes smaller than this are stray walls, not bases.</summary>
        public const int DefaultMinSize = 5;

        /// <summary>
        /// Power conduits run between bases and would weld them into one component,
        /// so they don't get a vote on connectivity. They're reattached afterwards.
        /// Standable rules out powered walls and turrets, which are real structure.
        /// </summary>
        private static bool IsSpanningInfrastructure(ThingDef def) =>
            def.EverTransmitsPower && def.passability != Traversability.Impassable;

        public static List<Complex> Find(
            Map map,
            int gapTolerance = DefaultGapTolerance,
            int minSize = DefaultMinSize)
        {
            var player = Faction.OfPlayer;
            var all = map.listerThings
                .ThingsInGroup(ThingRequestGroup.BuildingArtificial)
                .Where(t => t.Faction == player)
                // Frames and blueprints are work-in-progress, and SketchThing has no
                // field for work done — they'd arrive as fresh 0-progress frames.
                .Where(t => !(t is IConstructible))
                .ToList();

            // Connectivity is seeded from structural buildings only.
            var structural = all.Where(t => !IsSpanningInfrastructure(t.def)).ToList();
            if (structural.Count == 0)
                return new List<Complex>();

            var owner = new Dictionary<IntVec3, int>();
            for (int i = 0; i < structural.Count; i++)
                foreach (var c in structural[i].OccupiedRect())
                    owner[c] = i;

            var uf = new UnionFind(structural.Count);
            foreach (var pair in owner)
            {
                for (int dx = -gapTolerance; dx <= gapTolerance; dx++)
                for (int dz = -gapTolerance; dz <= gapTolerance; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    if (owner.TryGetValue(pair.Key + new IntVec3(dx, 0, dz), out int other))
                        uf.Union(pair.Value, other);
                }
            }

            var groups = new Dictionary<int, Complex>();
            for (int i = 0; i < structural.Count; i++)
            {
                int root = uf.Find(i);
                if (!groups.TryGetValue(root, out var complex))
                    groups[root] = complex = new Complex();
                complex.things.Add(structural[i]);
            }

            var result = groups.Values.Where(c => c.Count >= minSize).ToList();
            if (result.Count == 0)
                return result;

            foreach (var c in result)
                c.bounds = Enclose(c.things);

            ReattachInfrastructure(all.Where(t => IsSpanningInfrastructure(t.def)), result, gapTolerance);

            foreach (var c in result)
                c.bounds = Enclose(c.things);

            return result.OrderByDescending(c => c.Count).ToList();
        }

        /// <summary>
        /// Conduits didn't shape the components, but they're still part of the base.
        /// Give each one to the complex it's nearest to, and drop the long runs
        /// between bases — those are what the player will re-lay on arrival.
        /// </summary>
        private static void ReattachInfrastructure(
            IEnumerable<Thing> infrastructure, List<Complex> complexes, int gapTolerance)
        {
            foreach (var t in infrastructure)
            {
                Complex best = null;
                int bestDist = int.MaxValue;
                foreach (var c in complexes)
                {
                    int d = DistanceTo(c.bounds, t.Position);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        best = c;
                    }
                }
                if (best != null && bestDist <= gapTolerance)
                    best.things.Add(t);
            }
        }

        private static int DistanceTo(CellRect rect, IntVec3 c)
        {
            int dx = Max3(rect.minX - c.x, 0, c.x - rect.maxX);
            int dz = Max3(rect.minZ - c.z, 0, c.z - rect.maxZ);
            return dx > dz ? dx : dz;
        }

        private static int Max3(int a, int b, int c)
        {
            int m = a > b ? a : b;
            return m > c ? m : c;
        }

        private static CellRect Enclose(List<Thing> things)
        {
            var first = things[0].OccupiedRect();
            int minX = first.minX, maxX = first.maxX;
            int minZ = first.minZ, maxZ = first.maxZ;
            foreach (var t in things)
            {
                var r = t.OccupiedRect();
                if (r.minX < minX) minX = r.minX;
                if (r.maxX > maxX) maxX = r.maxX;
                if (r.minZ < minZ) minZ = r.minZ;
                if (r.maxZ > maxZ) maxZ = r.maxZ;
            }
            return CellRect.FromLimits(minX, minZ, maxX, maxZ);
        }

        private class UnionFind
        {
            private readonly int[] parent;

            public UnionFind(int n)
            {
                parent = new int[n];
                for (int i = 0; i < n; i++) parent[i] = i;
            }

            public int Find(int a)
            {
                while (parent[a] != a)
                    a = parent[a] = parent[parent[a]];
                return a;
            }

            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb) parent[rb] = ra;
            }
        }
    }
}
