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

## 2. Zones and worlds

The map is a disc cut into **6 zones**: 5 outer worlds + the central **dark zone
(abbr `XX`)**. Each run picks **5 of 6** defined worlds for the outer ring; each outer
world is a **72° fan** (1/5 of the disc). Future updates can add more worlds to the pool.

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

Each world tints its fan (faint background wedge) and its node fills. **Worlds are drawn
with a visible gap between them** (nodes use ~56° of each 72° fan, wedges ~62°) so each
fable reads as its own island; the cross-world **bridge** edges then visibly span those
gaps. The VOID and zone 6 are black. As days pass the VOID eats the map from the rim inward.

## 3. The seed

Every run has an **8-character seed** (`[0-9A-Z]`). **All** map/game randomness flows
through one deterministic PRNG built from that seed (`DetRandom`), so a seed reproduces
the entire map exactly. The debug dice button rolls a fresh seed; typing a seed +
Enter rebuilds that exact map.

## 4. Levels (outer worlds)

From the rim inward, each world has 4 levels; levels 1–2 split into two sublevels.
Full hierarchy (outer → inner): **1-A, 1-B, 2-A, 2-B, 3, 4**, then **5, 6** in zone 6.

Combat nodes are named `ABBR-<level>-<index>`, index running **across sublevels** of
the same numeric level (e.g. 1-A is `SL-1-1..`, 1-B continues `SL-1-5..`). Node counts:

- **1-A:** 3 or 4 (50/50). **1-B:** 3 or 4 (50/50).
- **2-A:** 2–4 (uniform). **2-B:** 2 or 3 (50/50).
- **3:** always 2 (`3-1`, `3-2`).
- **4:** always 1 (`4-1`) — **BOSS** room.

Higher level ⇒ stronger enemies and harder survival tasks, but better rewards.
*(Difficulty/reward content is TODO — noted here only.)*

## 5. Edges

**Within a world:**
- (a) Each node links to the closest higher (next-inner) node(s). Of the 2 nearest:
  20% both; otherwise 70% nearest / 30% farther.
- (b) Sibling links within a sublevel: **lv1 30%**, **lv2 50%** (per adjacent pair).
- (c) Level 3 always links to level 4 **and** its sibling lv3.

An edge stores the **numeric level of its higher/inner node** (a 3↔4 edge is level 4).

**Between worlds (ring):**
- **Level 3** forms a full ring: world *i*'s `3-2` ↔ world *i+1*'s `3-1` (wraps).
- **2-A:** 60% chance to link to the adjacent world. **1-A:** 30%.
- **2-B and 1-B never** cross worlds.
- The **start world must** have a 1-A cross-world edge (forced if none rolled).

**Visuals:** zone 1–5 edges are grey lines; zone 6 edges are invisible.

## 6. Function nodes

Named `<edgeLevel>-<letter>` (`4-a`, `4-b`, `3-a`, …). A function node is a **shelter**
(camp) or a **question mark**. Generation:

1. **Crossings first:** where two edges cross, drop a function node at the intersection
   and replace the 2 edges with 4 into it (those 4 are then "considered"). **40% shelter /
   60% question mark**.
2. **Then per remaining edge, by level:** lv4 **100%** (always shelter), lv3 50%, lv2 25%,
   lv1 10%. If it fires, split the edge in two via the node. Same **40% shelter /
   60% question** split as crossings (the code has always rolled 40/60 here — an
   earlier 80/20 note was stale; see §10).

- **Shelter** (camp icon): generated **Blessed**. Functions: build mod, item assignment,
  trade, joust, plantation (if present), and one day-ending action (Rest / Sharpen
  Weapon / Sharpen Armor / Wish). After the Blessing is consumed the shelter becomes
  **Mundane** — still usable for build mod and plant-tending, but no further day-ending
  actions. Full spec: `NODES.gdd` §5.
  Surprises TODO: `VOID TRADER`, `Treasure box`, `teleport`.
- **Question mark:** other functions — forbidden / teleport / other — **TODO**.

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
  zone-6 disc. Each links to one world's `4-1` with a **shelter** in between.
  The `XX-S` shelters are zone-6 nodes: **stepping onto one already crosses the
  singularity** (intended — they are the last camps before the abyss), and they are
  Blessed with the **basics only** (no plantations/traders/wanderers — `NODES.gdd` §5.2).
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
- **VOID devour schedule** (by day): `1-A`→10, `1-B`→20, `2-A`→30, `2-B`→35, `3`→40,
  `4`→45. On a level's devour day its nodes **flicker**; at the **end** of that day the
  nodes and their edges become unavailable (the VOID has eaten that ring). **Function nodes
  on a devoured ring go too:** once all of a function node's neighbours are eaten it is
  orphaned and the VOID takes it as well (no floating shelters left behind).
  **If the player's current node is devoured at day-end, the run is over** — the VOID
  eats the ground and the player with it (`NODES.gdd` §2.2); the `Finish the Day`
  confirmation warns when standing on flickering ground.
- Player starts on a **random 1-A node in the home/start world**, day 1, stamina 5.

### 8a. Mist (fog of war)

**Always on in the real game; a toggle in the debug harness for now.**
- Entering a world **reveals all of its nodes** (and its internal edges).
- From a revealed world you can also see the **bridge edges** to adjacent worlds **and any
  function node sitting on a bridge** — but **not** the far world's nodes.
- Worlds you've never entered, and the **VOID (zone 6)**, stay dark until you set foot in
  them. The single node you could step into next (an explore frontier) shows as a dim
  "unknown" marker so the map stays playable under fog.

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
  fights / camps / ? breakdown), a `Rest` button (debug — advances the day), and a `Mist` toggle.
- Click a highlighted node to move the smiley token: grey **visited** nodes cost stamina to
  re-walk; a gold-ringed **new** node transitions to Adventure mode (in the full game, the
  day ends via `Finish the Day` confirmation; the debug harness may auto-advance for now).
  Devoured nodes and the zone-6 one-way rule are respected; under mist, unexplored frontiers
  show as dim markers.
- **Icons:** combat = filled circle, boss = diamond, shelter = triangle, question mark =
  circled `?`. Node content is **not** implemented — icons only, per the current scope.

## 10. Decisions / interpretations (revisit)

These were resolved to keep the map functional; flag any you want changed:

- **2-A count** read as **uniform 2–4**; **2-B** as 50/50 between 2 and 3.
- **Devour dawns (10,20,30,35,40,45)** mapped one ring per dawn, outer-first
  (`1-A`→10 … `4`→45).
- **Movement** is shortest-path to any reachable node within stamina (not adjacent-only).
- **River** modeled as a single hub node (drawn as the ring) linking all lv5 + lv6.
- **Dark leader / dark-zone proper name:** still **TBD** (deferred).
- **Home world** always at ring index 0 (Pomo → VanillaKindom); 45-day clock is
  intentional (see Pacing intent above).
- **Fan spread:** nodes use 68° of each 72° fan (2° margin), so adjacent worlds' edge
  nodes sit close together (tightened in v0.2.4 from a wider 6° margin).
- **Function-node split (v0.2.4):** shelter/question ratio is **40/60** (was 80/20);
  lv4-edge shelters and the zone-6 in-between shelters remain forced shelters.
  *(v0.3.7: the stale 80/20 line in §6 was corrected — the code has rolled 40/60 for
  both crossings and mid-edge nodes since v0.2.4.)*
- **(v0.3.7) Devour kills:** a player standing on a devoured ring at day-end loses the
  run (`NODES.gdd` §2.2).
- **(v0.3.7) Destination-only content triggers;** unvisited and unconquered-combat
  nodes cannot be pathed through (`NODES.gdd` §1.3).
- **(v0.3.7) Pixolotl is a non-starter** until her home world exists (TBD registry #24).

## 11. Rendered map (atlas view) — v0.3.2

The topological graph can be viewed two ways, toggled by the **View** button (defaults to
atlas): the original **schematic** diagram, and a rendered **atlas** that reads like a real
world map. Same graph, same gameplay — the atlas is a pure render layer
(`Scripts/Map/MapRenderModel.cs` builds it; `Scripts/Map/MapControllerAtlas.cs` draws it).

**Territories (cities + regime areas).** Each combat/function node becomes a **weighted
Voronoi (power-diagram) cell** — the city and the land it controls. Cell size is set by node
kind: combat/boss claim large areas, shelters / `?` claim small ones. Cells are clipped to
each realm's **island** (a convex wedge); realms are separate landmasses with sea between
them (**zones are not connected**). The map is scaled up (`LayoutScale`) and viewed through a
**pan/zoom camera**.

**Roads and barriers.** Every graph edge becomes a **road** between cities. Where two
territories are adjacent but *not* connected, the shared border is a **barrier**, making
inaccessibility visible. Each world has two barrier flavours (currently **marked, not arted**):

| World | Point barrier (blocked would-be road) | Area barrier (region filler) |
|-------|----------------------------------------|------------------------------|
| Starland (SL) | city debris | meteor-strike warfield |
| HollowCastle (HC) | ruined walls | dark forest |
| VanillaKindom (VK) | burned villages | lake |
| TheDeserted (TD) | giant beast skull | desert |
| Palace of LOOING (PL) | the protected throne | abyss / quagmire (深渊) |
| Banboo Maze (BM) | deserted woodhouse (林中小屋) | bamboo forest |
| Zone 6 (XX) | — | the VOID |

- **Point barriers** land on *candidate-but-failed* edges (a sibling/higher-node link the
  generator rolled and lost — see `MapGraph.FailedCandidates`): "a road could've been here,
  but a landmark blocks it." Drawn as a small marker + label.
- **Area barriers** fill the remaining disconnected frontiers, tinted per theme, with one
  label per realm.

**Between worlds & zone 6.** Cross-realm links render as **golden sea causeways** through the
(un-expanded) bridge nodes. **Zone 6 is a central pentagon**; its 5 lv5 nodes become 5
territories around the VOID lake/river/core. The boss↔lv5 **XX-S shelters are isolated
islets** in the sea ring between the realms and the pentagon.
