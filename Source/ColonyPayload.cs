using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimColonyTransfer
{
    /// <summary>One detected base, as a sketch that can be placed independently.</summary>
    public class ComplexRecord : IExposable
    {
        public Sketch sketch;
        public IntVec3 sourceOrigin;
        public int thingCount;
        public int terrainCount;

        public void ExposeData()
        {
            Scribe_Deep.Look(ref sketch, "sketch");
            Scribe_Values.Look(ref sourceOrigin, "sourceOrigin");
            Scribe_Values.Look(ref thingCount, "thingCount", 0);
            Scribe_Values.Look(ref terrainCount, "terrainCount", 0);
        }
    }

    /// <summary>
    /// A loose item on the map, positioned relative to its complex.
    /// Sketches carry no items, so without these the colony arrives with perfect
    /// buildings and nothing in them.
    /// </summary>
    public class ItemRecord : IExposable
    {
        public Thing thing;
        public IntVec3 relativePos;
        public int complexIndex;

        public void ExposeData()
        {
            Scribe_Deep.Look(ref thing, "thing");
            Scribe_Values.Look(ref relativePos, "relativePos");
            Scribe_Values.Look(ref complexIndex, "complexIndex", 0);
        }
    }

    /// <summary>
    /// Contents of a building that genuinely holds things internally — graves and
    /// caskets. Storage buildings don't need this: their items sit on map cells and
    /// travel as ItemRecords.
    /// </summary>
    public class ContainerRecord : IExposable
    {
        public IntVec3 relativePos;
        public int complexIndex;
        public string containerDefName;
        public List<Thing> contents = new List<Thing>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref relativePos, "relativePos");
            Scribe_Values.Look(ref complexIndex, "complexIndex", 0);
            Scribe_Values.Look(ref containerDefName, "containerDefName");
            Scribe_Collections.Look(ref contents, "contents", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                contents ??= new List<Thing>();
        }
    }

    /// <summary>
    /// The exchange file: everything needed to rebuild a colony in another save.
    ///
    /// This graph must be CLOSED. During a load session, CrossRefHandler populates
    /// its LoadedObjectDirectory only from objects loaded in that same session — the
    /// destination game's own objects are not in it — so any reference pointing at
    /// something absent from this payload resolves to null with a warning.
    ///
    /// That constraint is also what makes the transfer safe. Once resolution
    /// finishes, references are live object pointers rather than ID strings, so
    /// incoming objects can simply be renumbered from the destination's
    /// UniqueIDsManager. No ID rewriting, and no possibility of binding to an
    /// unrelated object that happened to share a number.
    ///
    /// Deliberately absent: the player Faction. Bringing it would give the
    /// destination two player factions. Pawns' faction references are expected to
    /// null out on load and are reassigned to the destination's player faction.
    /// </summary>
    public class ColonyPayload : IExposable
    {
        public const int CurrentFormatVersion = 2;
        public const string RootElement = "colonyPayload";
        public const string FileExtension = ".rwcolony";

        // --- provenance, for validating the destination before anything is applied
        public int formatVersion = CurrentFormatVersion;
        public string colonyName;
        public string gameVersion;
        public List<string> modIds = new List<string>();

        // --- the colony itself
        public List<ComplexRecord> complexes = new List<ComplexRecord>();
        public List<Pawn> pawns = new List<Pawn>();
        public List<ItemRecord> items = new List<ItemRecord>();
        public List<ContainerRecord> containers = new List<ContainerRecord>();

        // --- the closure: everything the pawns reference that isn't map-local
        public List<Pawn> relatedWorldPawns = new List<Pawn>();
        public Ideo ideo;                       // precepts are owned by the Ideo
        public List<ApparelPolicy> apparelPolicies = new List<ApparelPolicy>();
        public List<FoodPolicy> foodPolicies = new List<FoodPolicy>();
        public List<ReadingPolicy> readingPolicies = new List<ReadingPolicy>();

        // Tales are deliberately not carried. They exist to back art descriptions,
        // and SketchThing has no comps — so sculptures arrive anonymous regardless
        // and there would be nothing left to reference them.

        public void ExposeData()
        {
            Scribe_Values.Look(ref formatVersion, "formatVersion", CurrentFormatVersion);
            Scribe_Values.Look(ref colonyName, "colonyName");
            Scribe_Values.Look(ref gameVersion, "gameVersion");
            Scribe_Collections.Look(ref modIds, "modIds", LookMode.Value);

            Scribe_Collections.Look(ref complexes, "complexes", LookMode.Deep);
            Scribe_Collections.Look(ref pawns, "pawns", LookMode.Deep);
            Scribe_Collections.Look(ref items, "items", LookMode.Deep);
            Scribe_Collections.Look(ref containers, "containers", LookMode.Deep);

            Scribe_Collections.Look(ref relatedWorldPawns, "relatedWorldPawns", LookMode.Deep);
            Scribe_Deep.Look(ref ideo, "ideo");
            Scribe_Collections.Look(ref apparelPolicies, "apparelPolicies", LookMode.Deep);
            Scribe_Collections.Look(ref foodPolicies, "foodPolicies", LookMode.Deep);
            Scribe_Collections.Look(ref readingPolicies, "readingPolicies", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                complexes ??= new List<ComplexRecord>();
                pawns ??= new List<Pawn>();
                items ??= new List<ItemRecord>();
                containers ??= new List<ContainerRecord>();
                relatedWorldPawns ??= new List<Pawn>();
                apparelPolicies ??= new List<ApparelPolicy>();
                foodPolicies ??= new List<FoodPolicy>();
                readingPolicies ??= new List<ReadingPolicy>();
                modIds ??= new List<string>();
            }
        }

        public string Summary() =>
            $"{colonyName}: {pawns.Count} pawns, {complexes.Count} complexes " +
            $"({complexes.Sum(c => c.thingCount)} things), {items.Count} item stacks, " +
            $"{containers.Count} containers, {modIds.Count} mods";
    }
}
