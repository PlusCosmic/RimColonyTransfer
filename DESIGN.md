# Design notes

Moving an established colony into a brand-new world (a different save), not a
different tile in the same world. That distinction drives everything below: the
destination is a separate game with its own object graph, so nothing carrying a
load ID can travel safely.

## Why this is hard

A `Map` in a save is not a self-contained unit. It's a leaf on a dependency graph
whose roots are game-level singletons — `researchManager`, `taleManager`,
`outfitDatabase`, `questManager`, `factionManager`, `ideoManager`, `worldPawns` —
and it references them by unresolved string IDs.

The failure mode is what makes naive approaches dangerous. From
`Verse.LoadedObjectDirectory`:

- An unresolved reference logs a **warning** and returns `default(T)`. Not a crash —
  a silently nulled field.
- If a `Thing` load ID misses, it retries **by numeric ID alone, ignoring the
  defName**, and can bind to an unrelated object that happens to share the number.
  Also only a warning.
- The duplicate-load-ID guard is `if (Prefs.DevMode)`. In normal play a collision
  throws inside `Add`, gets logged, and the second object never registers — so every
  reference to it resolves to the first.

So a botched transfer loads fine and corrupts quietly. That ruled out raw XML
surgery on the save: one save measured 46,781 reference occurrences across 96
distinct element names, and reference form doesn't even match declaration form
(`<pawn>Thing_Human3379118</pawn>` points at `<id>Human3379118</id>`; factions wrap
an integer as `Faction_23`). Shape alone can't distinguish a reference from a
defName — `<def>Skin_Melanin2</def>` matches the same pattern.

## The approach

Split the colony into three independent layers, so the risky part is as small as
possible.

### Buildings — via `Sketch`

`RimWorld.Sketch` is the game's own system for placing arbitrary structures
(monument quests, ancient complexes). A `SketchThing` carries exactly:

    def, stuff, stackCount, rot, quality, hitPoints

**No load IDs.** Buildings therefore cannot collide with anything in the destination
save, which removes the entire corruption hazard from the largest part of the
payload. `Sketch : IExposable` and deep-scribes its entities, so it drops into an
exchange file with no custom serialization.

`Sketch.Spawn` takes `forceTerrainAffordance` and `defaultAffordanceTerrain`, which
is the answer to terrain mismatch: rather than adapt the blueprint to the tile, lay
down terrain that supports the blueprint. Water carries the `Bridgeable` affordance
and `Bridge` is a real terrain def, so a base that crossed a river can land
somewhere that doesn't have one.

### Pawns — deep-scribed, with a small remap

Deep-scribing pawns keeps skills, traits, hediffs, genes, relationships, apparel and
inventory intact. Measuring a real colony's outbound references showed the
out-of-map dependency set is small and closed — roughly seven categories:

- Ideo + its Precepts
- Abilities
- ApparelPolicy / FoodPolicy / ReadingPolicy
- Tales (referenced by art)
- Combat log entries
- The player Faction
- `otherPawn` refs to relatives living in `worldPawns`

Verb and comp load IDs (`Thing_Human4690062_0_Smash`) are *derived* from the parent
thing's ID, so they follow automatically and need no handling.

### Settings — a positional manifest

Bills, storage filters, growing zone crops and similar aren't in a sketch and have no
load IDs worth keeping. Plain data, keyed by position within the complex.

## Faction is not a cross-reference

Found by round-trip testing, and it contradicts the model above. `Thing.ExposeData`
does **not** save faction via `Scribe_References`. It writes the load ID as a plain
value and resolves it against the **live game**:

    string facID = ((factionInt != null) ? factionInt.GetUniqueLoadID() : "null");
    Scribe_Values.Look(ref facID, "faction", "null");
    ...
    factionInt = Find.FactionManager.AllFactions
        .FirstOrDefault(fa => fa.GetUniqueLoadID() == facID);

This bypasses `LoadedObjectDirectory` completely, so the closed-graph guarantee does
not cover it. Round-tripping inside the source save always resolves correctly and
looks fine — which is exactly what makes it dangerous.

In a destination save, `Faction_21` matches whatever faction happens to hold load ID
21 *there*: a pirate band, the Empire, anything. Not null — **silently wrong**, and
it is the same numeric-collision hazard the design was supposed to have eliminated,
arriving through a different mechanism.

**The importer must explicitly overwrite faction on every incoming Thing and Pawn.**
It cannot rely on the field being null and filling it in.

## Complex detection

The colony is split into connected components and each is placed independently, so a
base spanning a river doesn't need matching terrain at the destination.

Two findings from running this on real saves:

**Linear infrastructure must not vote on connectivity.** On an industrial save,
2,092 `HiddenConduit` running between bases welded 97% of buildings into a single
component. Conduits are excluded from the connectivity graph, then reattached to the
nearest complex afterwards.

**Gap tolerance is chosen from plateaus, not picked.** Sweeping the tolerance and
watching where the component count stops changing identifies the natural split. On
the reference colony:

    tol  5-10: [479, 159, 78, 34]     main base / curtain wall / keep / outbuilding
    tol 11-15: [479, 237, 34]         main base / castle / outbuilding

Both are legitimate. The looser one matches how the player describes the colony; the
tighter one yields smaller sketches, which place more easily on a new tile.

## Constraints

- **Both saves must run the same mod list.** Defs resolve by name. The reference
  colony is full of `DankPyon_*`, `KK_MedievalWall`, `VFEM2_*` — it cannot move into
  a vanilla world at all.
- **Frames and blueprints don't travel.** `SketchThing` has no work-completed field,
  so they'd arrive as fresh 0-progress frames. Excluded via `IConstructible`.
- **Terrain is captured before things.** `Sketch.WouldCollide` checks incoming
  terrain against the affordance needs of things already in the sketch, so adding
  things first gets floors refused.
- `shortHash` is `StableStringHash(defName) % 65535` with linear-probe collision
  resolution that depends on def iteration order. Reconstructing the terrain grid
  outside the game is therefore fragile; in-game `DefDatabase` has it for free. This
  is why analysis moved from offline scripts into the mod.
