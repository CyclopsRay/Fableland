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

**Every playable character has a home world, and it is always the start zone.** So the
home world is guaranteed to be one of the 5, placed at ring index 0 (top); the other 4
are random. **Pomegraknight's home is VanillaKindom (pink).** When other starting
characters unlock, they begin in their own home world.

| World            | Abbr | Palette       |
|------------------|------|---------------|
| Starland         | SL   | orange        |
| HollowCastle     | HC   | grey          |
| VanillaKindom    | VK   | pink          |
| TheDeserted      | TD   | sandy yellow  |
| Palace of LOOING | PL   | purple        |
| Banboo Maze      | BM   | dark green    |
| **The VOID / dark zone** | **XX** | **black** |

Each world tints its fan (faint background wedge) and its node fills. The VOID and
zone 6 are black. As days pass the VOID eats the map from the rim inward.

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
   and replace the 2 edges with 4 into it (those 4 are then "considered"). 80% shelter /
   20% question mark.
2. **Then per remaining edge, by level:** lv4 **100%** (always shelter), lv3 50%, lv2 25%,
   lv1 10%. If it fires, split the edge in two via the node. 80% shelter / 20% question.

- **Shelter** (camp icon): rest / build / upgrade / wish. Surprises TODO: `VOID TRADER`,
  `Treasure box`, `teleport`.
- **Question mark:** other functions — forbidden / teleport / other — **TODO**.

## 7. Zone 6 — the dark zone (`XX`)

Two layers + a river between; **entering zone 6 is one-way** (outer zones get devoured).
- **Level 5:** 5 nodes around the circular **lake of the VOID** (`XX-5-1..5`). Each links
  to one world's `4-1` with a **shelter** in between.
- **River of the VOID:** every lv5 node connects to the river, so the player can hop
  between lv5 nodes in one step. It's a shelter-and-beyond, drawn as a shining dark ring
  flowing **counter-clockwise**, wrapping level 6.
- **Level 6:** the core (`XX-6-1`), a single edge to the river. The final confrontation.

## 8. Time, stamina, and the VOID

- **Stamina:** 5 steps/day; traversing one edge = **1 step**. Refreshes to 5 each dawn.
- **Rest** ends the current day.
- **VOID devour schedule** (by day): `1-A`→10, `1-B`→20, `2-A`→30, `2-B`→35, `3`→40,
  `4`→45. On a level's devour day its nodes **flicker**; at the **end** of that day the
  nodes and their edges become unavailable (the VOID has eaten that ring).
- Player starts on a **random 1-A node in the home/start world**, day 1, stamina 5.

**Pacing intent (design target, informs the 45-day clock).** A full run is budgeted at
**≤ ~90 min**, and usually shorter:
- ~**25 combats** @ 1.5–2 min, ~**15 loot/rest** stops @ ~1 min, up to **5 boss** fights
  @ 2–3 min, plus **level 5–6** @ ~20 min.
- The generous 45-day clock exists so the VOID doesn't force-end a normal-paced run; it's a
  cap on **deliberate stagnation** (farming loot), not the expected length.
- **TODO:** acceleration mechanisms to discourage stagnation/over-farming — to be tuned
  against real playtests.

## 9. Debug harness (current build)

- **Menu:** a Start button → Map scene.
- **Map, top-left:** `Dice` button (reroll seed + restart), seed field (type + Enter to
  rebuild), a Day / Stamina readout, and a `Rest` button.
- Click a node to move the smiley token there along the shortest available path, if
  stamina covers the step cost. Devoured nodes and the zone-6 one-way rule are respected.
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
