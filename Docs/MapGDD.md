# Fableland — World Map GDD

Design doc for the rogue-like meta-map (the overworld the player traverses between
fights). This is the *map layer*; the per-node content (fights, shelters, events) is
tracked separately and mostly **TODO** at this stage.

Implementation: `Scripts/Map/` (generation + view) and `Scenes/Menu.tscn`,
`Scenes/Map.tscn`. First landed in **v0.2.3**.

---

## 1. Worldview

Originally each fable's heroes lived in their own separate world inside **Fableland**.
One day a **magic turbulence** swept every fable and fused the worlds from different
storybooks together. **Pomegraknight** — guardian of her kingdom, and the starting
protagonist (others unlock on completing a run) — sets out to discover why the worlds
are now connected. On the road she meets friends and enemies and finally uncovers the
cause.

The game is a **rogue-like**. The goal of a run is to reach the **dark zone** at the
center and confront the **dark leader** (final boss — *name TBD*). Reaching the center
means crossing the ring of fused worlds while the **VOID** closes in from outside.

## 2. Zones and realms

The map contains **6 zones**: five outer storybook **realms** plus the central dark zone
(`XX`). Each run picks five of the six defined worlds, keeping the home world first in a
deterministic angular order. The five worlds still provide their palette, terrain identity,
foe pool, capital, and complete node roster; a *realm* is their physical region on this run.

The outer map is one connected island with a roughly circular, organically irregular coastline.
It has no detached outer islets. A static, regular pentagonal **VOID** sits near its geometric
centre; the generator derives its radius from the generated island area so it occupies roughly
**1/30** of it. The pentagon's position and orientation never vary with the seed.

Five seeded **realm-divider rivers** run from the VOID boundary to the coast. They are visibly
variable in width, never cross, and each joins both the VOID and open sea. Together they cut the
outer island into exactly five disconnected land regions: one per selected realm. The rivers are
real geographical barriers: outer-realm roads never cross them. The only detached land allowed
by the old zone-6 construction is inside the central VOID, not in the outer island.

**Every starter character has a home world, and it is always the start zone.** So the
home world is guaranteed to be one of the 5, placed at ring index 0 (top); the other 4
are random. **Pomegraknight's home is VanillaKindom (pink).** When other starting
characters unlock, they begin in their own home world. (Pixolotl is currently a
**non-starter** — recruit-only; her home world is deliberately unresolved. TBD
registry #24.)

| World            | Abbr | Palette       |
|------------------|------|---------------|
| Starland         | SL   | orange        |
| HollowCastle     | HC   | grey          |
| VanillaKindom    | VK   | pink          |
| TheDeserted      | TD   | sandy yellow  |
| Palace of LOOING | PL   | purple        |
| Banboo Maze      | BM   | dark green    |
| **The VOID / dark zone** | **XX** | **black** |

Each realm receives its world's palette and remains a distinct kingdom, but its boundary is now
the river system rather than a sea gap. The central VOID and the day clock are **run-wide
systems shared by every realm**. As days pass the VOID eats the outer map from the rim inward;
zone 6 retains its separate generation rule.

### 2.1 Combat terrain and altitude (v0.9.0)

Every combat node also carries one terrain label used only to select an authored combat map;
it does not change the node's two-axis difficulty. The single outer island samples one
deterministic altitude field and classifies nodes as **`low-ground`**, **`sea-level`**, or
**`high-ground`**. A given seed may naturally contain only one or two labels. Function nodes
inherit the altitude at their road position; the central VOID uses neutral sea-level until it has
dedicated combat maps.

The atlas remains flat: low land is tinted darker and high land lighter, with clear contour lines
at the lowland/plain and plain/highland thresholds. These contours are decoration only — they do
not block roads or alter movement.

## 3. The seed

Every run has an **8-character seed** (`[0-9A-Z]`). **All** map/game randomness flows
through one deterministic PRNG built from that seed (`DetRandom`), so a seed reproduces
the entire map exactly. The debug dice button rolls a fresh seed; typing a seed +
Enter rebuilds that exact map.

## 4. Levels (outer realms)

From the rim inward, each realm has 4 levels; levels 1–2 split into two sublevels.
Full hierarchy (outer → inner): **1-A, 1-B, 2-A, 2-B, 3, 4**, then **5, 6** in zone 6.

Combat nodes are named `ABBR-<level>-<index>`, index running **across sublevels** of
the same numeric level (e.g. 1-A is `SL-1-1..`, 1-B continues `SL-1-5..`). Level 1
and level 2 always contain the **same total number of cities**, and both retain A/B
sublevels:

- **1-A / 1-B:** each rolls 3 or 4 (50/50), making the level-1 total 6–8.
- **2-A / 2-B:** split that exact level-1 total as evenly as possible: 3/3 for six,
  seeded 3/4 or 4/3 for seven, and 4/4 for eight.
- **3:** always 2 (`3-1`, `3-2`).
- **4:** always 1 (`4-1`) — **BOSS** room.

Level assignment happens **independently within each realm** after all of its combat positions
are scattered. Sorting those positions by distance to the central VOID (nearest first) assigns:
`4-1`, then both LV3 nodes, then `2-B`, `2-A`, `1-B`, and `1-A`. This guarantees every realm's
capital and final approaches occupy its inner landmark area without forcing long roads through an
arbitrary placement band.

Higher level ⇒ stronger enemies and harder survival tasks, but better rewards.
*(Difficulty/reward content is TODO — noted here only.)*

## 5. Roads and cross-realm travel

**Within a realm:**
- Combat nodes scatter deterministically inside their own river-bounded land polygon. Their level
  tags are then derived from their sorted VOID distance (§4), rather than imposed by pre-selected
  radial bands. A reserved buffer around the central VOID keeps the capitals and the rest of the
  outer realms away from the singularity, leaving a clear approach for the zone-6 Shelter.
- The LV4 capital is an unobstructed inner landmark. A constrained spatial spanning tree grows
  from the two LV3 nodes and connects every other city; 1–3 short local roads then add route
  choice. Therefore **every city in a realm is reachable from its `4-1` capital**.
- Every route is sampled and checked against its owning realm polygon. It cannot enter the VOID,
  a realm-divider river, or another realm. A new road also cannot cross or overlap an existing
  road except at a shared node; a proposed hub spoke is simply omitted when no legal route
  remains.
- The two LV3→LV4 approaches remain separate. Exactly one contains a Transportation Hub (§6);
  the other remains an ordinary route. An edge stores the highest numeric level at either end;
  final-approach edges are level 4.

**Between realms — `TwistedReality`, not bridges:**
- There are **no cross-realm road edges, bridges, causeways, or bridge function nodes**. The
  river barrier stays meaningful in both topology and art.
- Defeating the LV4 boss of the starting realm grants the unique wonder item
  **`TwistedReality`** as that boss's first-success reward. The player cannot visit another
  realm before earning it.
- While holding `TwistedReality` and standing on a **completed `1-A` combat node** (the
  realm's periphery), the player may spend its map skill to transport to the closest available
  node in another realm. "Closest" is geometric distance; ties break by deterministic realm
  order and node id. Devoured nodes are ineligible. The move costs **one stamina**, enters the
  destination normally, and immediately forms a permanent **reality bridge** between those two
  nodes. That bridge is an ordinary traversable map edge thereafter: it needs no item use and
  costs normal movement stamina. Both endpoints are locked after the bridge forms and can never
  create or receive another reality bridge.
- `TwistedReality` starts ready when earned and has a **5-day day-based cooldown**. If no valid
  destination exists, it cannot be activated and no cooldown is spent. Its full item contract is
  in `ITEMS.gdd` §7.12.
- If the VOID devours either endpoint, that reality bridge breaks. Its surviving endpoint is
  released and may form a new reality bridge later; the spent cooldown is not refunded.

**Visuals:** zone 1–5 edges are grey lines; zone 6 edges are invisible.

## 6. Functional nodes

Function nodes occupy little map space and never create a sixth land region.

- **Transportation Hub** (the former shelter map node) is placed on a road where city density is
  high, splits that road, and gains legal local spokes to the nearby city cluster. It connects
  all of those cities without crossing a river or another path. One hub is compulsory on one
  LV3→LV4 approach in each realm. **Each realm** then rolls **3–5 hubs total**; extra hubs split
  dense local roads and may add nearby legal spokes, stopping as soon as a spoke would cover or
  cross an existing path.
- **Event node** (the former question-mark map node) occurs on a single ordinary road only: it
  splits that edge, has degree two, and creates no extra roads. **Each realm** generates **4–6
  event nodes**, distributed deterministically within its own road network.

Runtime ids remain realm-prefixed (`ABBR-H-n` for hubs and `ABBR-E-n` for events) so map/run
state is unambiguous. No bridge-function ids exist because reality bridges are direct edges.

**Hub service contract:** Transportation Hubs are deliberately narrow: **Rest**, **Sharpen
Weapon**, **Sharpen Armor**, **Team Build**, and leaving/finishing the day. They do not roll or
host plantations, traders, jousting, or other optional functions. Event nodes retain the existing
question-mark event contract.

## 7. Zone 6 — the dark zone (`XX`)

**The singularity (lore + mechanic).** Zone 6 is a **black hole that eats information**. Stepping
into it means crossing the **singularity** — there is **no turning back** (movement is one-way in).
The instant you cross, the whole outer ring (**zones 1-5) is devoured at once**: outside the hole,
the world evolves so fast that everything you left behind is already gone. And because the black
hole eats information, **time itself becomes unknowable inside** — the day counter stops showing a
number and reads **`???`**. (In the rendered map the devoured outer ring stays faintly visible as
dim "dead ruins" so you can still read where you've been — you just can't go there.)

Two layers + a river between; **entering zone 6 is one-way** (outer zones get devoured).
Zone 6 is drawn as a **dark disc**, and **level 5 sits inside that disc** (the lv5 ring is
within the VOID, not out on the rim next to the boss rooms). From the disc edge inward:
lv5 ring → lake → river ring → lv6 core.
- **Level 5:** 5 nodes around the circular **lake of the VOID** (`XX-5-1..5`), inside the
  zone-6 disc. Each links to one world's displaced `4-1` through an `XX-S` **Shelter**. This is
  the only use of the Shelter label: it is distinct from a Transportation Hub, occupies the
  reserved gap between LV4 and LV5, and stepping on it crosses the singularity immediately.
  It cannot route back to zones 1–5 and offers only **Rest**, **Sharpen Weapon**, **Sharpen
  Armor**, and **Team Build** — no plantation, trader, joust, or optional service.
- **Foe levels inside:** the five `XX-5` fights are level 7, the `XX-6-1` core is
  level 8 (`FOES.gdd` §2). The hidden day counter keeps advancing in-void (item CDs,
  perish timers, stamina all normal) — only the display is `???` (`NODES.gdd` §7.4).
- **River of the VOID:** every lv5 node connects to the river, so the player can hop
  between lv5 nodes in one step. It's a shelter-and-beyond, drawn as a shining dark ring
  wrapping level 6, with a **selectable node marker on the ring** (you route lv5 → river →
  lv6 through it).
- **Level 6:** the core (`XX-6-1`), a single edge to the river. The final confrontation.

## 8. Time, stamina, and the VOID

- **Stamina:** 5 per day; traversing one edge = **1 step**.
- **Day model:** the day ends when the player clicks **`Finish the Day`** (or a day-ending
  shelter action triggers it). Stepping onto a **new (never-visited) node** transitions to
  Adventure mode — the node's content is resolved, then the player confirms "Finish the Day"
  to advance the clock. When **stamina reaches 0**, movement is **blocked** (no further
  edge traversal), but the day does not automatically end — the player can still use shelter
  functions or must complete an in-progress combat node before finishing the day.
  See `NODES.gdd` §7 for the full day-cycle spec and day-end resolution sequence.
- **Visited nodes render grey.** You may re-walk them freely (costing stamina).
- **Pathing:** movement is shortest-path within stamina; **only the destination node
  triggers content**. Paths may not pass *through* unvisited nodes or unconquered
  combat nodes — those are destination-only (`NODES.gdd` §1.3). Entering any combat
  node depletes all stamina (`NODES.gdd` §2.3).
- **Rest** (debug harness): the `Rest` button in the current debug build manually ends the day
  (wait in place) — same day-advance + devour + refresh. In the full game this is replaced by
  the `Finish the Day` button and shelter Rest/Sharpen/Wish actions (`NODES.gdd` §5.5).
- **Controlled fields and VOID devour:** every spot of an outer realm is assigned to one city
  by a city-control field. The map can show those field boundaries; when a city is devoured its
  whole field becomes VOID. On day **10**, each realm loses the furthest half of its LV1 cities;
  on day **20**, all remaining LV1 cities; on day **30**, the furthest half of LV2; on day
  **40**, all LV2; on day **43**, all LV3; and on day **45**, every remaining outer city
  (including LV4). At the end of each listed day, affected cities and their edges become
  unavailable. Function nodes are devoured only when **more than half** of the cities directly
  connected to them have been devoured. A reality bridge breaks when either endpoint is devoured,
  releasing any surviving endpoint (§5).
  **If the player's current node is devoured at day-end, the run is over** — the VOID
  eats the ground and the player with it (`NODES.gdd` §2.2); the `Finish the Day`
  confirmation warns when standing on flickering ground.
- Player starts on a **random 1-A node in the home/start world**, day 1, stamina 5.

### 8a. Mist (fog of war)

**Always on in the real game; a toggle in the debug harness for now.**
- Entering a realm **reveals all of its nodes** (and its internal roads).
- Other realms and the **VOID (zone 6)** stay dark until the player sets foot in them.
  Realm-divider rivers are visible geography, not a source of bridge-edge previews.
- A `TwistedReality` arrival counts as entering its target realm and reveals that realm's nodes
  and roads normally. The target node remains an ordinary unexplored node if it was not already
  visited.

**Pacing intent (design target, informs the 45-day clock).** A full run is budgeted at
**≤ ~90 min**, and usually shorter:
- ~**25 combats** @ 1.5–2 min, ~**15 loot/rest** stops @ ~1 min, up to **5 boss** fights
  @ 2–3 min, plus **level 5–6** @ ~20 min.
- The generous 45-day clock exists so the VOID doesn't force-end a normal-paced run; it's a
  cap on **deliberate stagnation** (farming loot), not the expected length.
- **Anti-stagnation mechanism (decided, not yet implemented):** the **enemies in zone 6
  scale up with elapsed time** — the longer a player farms the outer worlds, the more
  powerful the VOID's defenders become, so over-farming is paid for at the finale rather
  than hard-blocked. Exact scaling curve TBD; tune against real playtests.

## 9. Debug harness (current build)

- **Menu:** a Start button → Map scene.
- **Map, top-left:** `Dice` button (reroll seed + restart), seed field (type + Enter to
  rebuild), a Day / Stamina readout with a **traversed tracker** (total visited + a
  fights / hubs / shelter / ? breakdown), a `Rest` button (debug — advances the day), a `Mist`
  toggle, and a **Fields** toggle that outlines every city's controlled field.
- Click a highlighted node to move the smiley token: grey **visited** nodes cost stamina to
  re-walk; a gold-ringed **new** node transitions to Adventure mode (in the full game, the
  day ends via `Finish the Day` confirmation; the debug harness may auto-advance for now).
  Devoured nodes and the zone-6 one-way rule are respected; under mist, unexplored frontiers
  show as dim markers.
- **Icons:** combat = filled circle, boss = diamond, Transportation Hub = triangle, special
  Shelter = square, Event = circled `?`. Devoured city fields remain visibly dark even while the
  Fields overlay is off.

## 10. Decisions / interpretations (revisit)

These were resolved to keep the map functional; flag any you want changed:

- **2-A count** read as **uniform 2–4**; **2-B** as 50/50 between 2 and 3.
- **(v0.9.1) City fields and devour:** fields partition each realm around its cities. Days
  10/20 remove the furthest half/all LV1, days 30/40 the furthest half/all LV2, day 43 all LV3,
  and day 45 all remaining outer cities; function nodes require a strict majority of connected
  cities to be devoured.
- **Movement** is shortest-path to any reachable node within stamina (not adjacent-only).
- **River** modeled as a single hub node (drawn as the ring) linking all lv5 + lv6.
- **Dark leader / dark-zone proper name:** still **TBD** (deferred).
- **Home world** always at ring index 0 (Pomo → VanillaKindom); 45-day clock is
  intentional (see Pacing intent above).
- **(v0.3.7) Devour kills:** a player standing on a devoured ring at day-end loses the
  run (`NODES.gdd` §2.2).
- **(v0.3.7) Destination-only content triggers;** unvisited and unconquered-combat
  nodes cannot be pathed through (`NODES.gdd` §1.3).
- **(v0.3.7) Pixolotl is a non-starter** until her home world exists (TBD registry #24).
- **(v0.9.0) One island, five realms:** a single seeded organic coast contains all outer land.
  A fixed central pentagon occupies roughly 1/30 of its area; five non-crossing, variable-width
  rivers join that VOID to the coast and create exactly five river-isolated realm polygons.
- **(v0.9.0) Per-realm level roster:** each realm independently keeps one LV4 capital and two
  LV3 cities. Level 1 and level 2 have equal 6–8 city totals, each retaining A/B sublevels.
  Distance to the VOID ranks cities as LV4, LV3, `2-B`, `2-A`, `1-B`, then `1-A`.
- **(v0.9.1) Realm-local roads and functions:** roads remain inside their realm and cannot cross
  or cover another path. Every realm has 3–5 Transportation Hubs (including the mandatory
  LV3→LV4 hub) and 4–6 degree-two Event nodes. Hubs offer only Rest, both Sharpen actions, and
  Team Build; the gap between LV4 and LV5 instead holds a one-way special Shelter.
- **(v0.9.0) TwistedReality travel:** the first realm's LV4 victory grants the unique, held,
  5-day map key. From a completed `1-A` node it moves the player one stamina to the closest
  available node of another realm, then creates a permanent bridge between the two locked
  endpoints. Later traversal uses that edge normally and never consumes the item.
- **(v0.9.1) Reality-bridge collapse:** devouring either endpoint removes its bridge and releases
  the surviving endpoint for a future TwistedReality bridge.
- **(v0.9.0) Terrain is live map-selection data:** outer nodes sample one island height field and
  use `low-ground` / `sea-level` / `high-ground`; the threshold contours are decorative only.

## 11. Rendered map (atlas view) — v0.9.0

The topological graph can be viewed two ways, toggled by the **View** button (defaults to
atlas): the original **schematic** diagram, and a rendered **atlas** that reads like a real
world map. Same graph, same gameplay — the atlas is a pure render layer
(`Scripts/Map/MapRenderModel.cs` builds it; `Scripts/Map/MapControllerAtlas.cs` draws it).

**One island, river realms.** The outer silhouette is one roughly circular, softly irregular
coastline. The static pentagonal VOID sits near the centre; five river surfaces widen and wander
from it to the coast, cleanly separating the five coloured realm landforms. There are no outer
islets, petal gaps, causeways, or hard territorial walls. The rivers themselves make the
unavailable cross-realm route legible.

**Altitude.** The single island is filled with height-tint patches and readable contour strokes at
the lowland/plain and plain/highland boundaries, all sampled from the field that assigns node
terrain. Both are decorative and never participate in pathing, collision, or visibility.

**Roads, fields, and controls.** Every realm-local graph edge becomes a non-crossing road routed
around the VOID and rivers; Transportation Hubs read as small junctions and Event nodes as small
single-road stops. The **Fields** control outlines each city-control boundary; devoured fields
stay dark even when the overlay is hidden. `TwistedReality` is drawn as the violet reality bridge
it creates; it is the only allowed cross-realm route, breaks if the VOID devours either endpoint,
and otherwise locks those endpoints. The central VOID retains its pentagon, LV5 territories,
lake, river, and one-way `XX-S` Shelters. Wheel zoom is unchanged. A short left click selects a
node, left drag pans the map with the cursor, and right drag rotates it around the current player
token after centering that token as the pivot.
