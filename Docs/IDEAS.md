# Fableland — The Idea Ledger

The design department's dream file. **Nothing in here is canon** — canon lives in the
GDDs. This is where ideas are kept alive, cheap, and slightly too ambitious, so that
when a milestone needs content the shelf is already full.

**How to use it:** ideas enter as a few lines (a *spark*). When one gets real numbers
and edge cases, it becomes a *sketch* here. When it's scheduled, it **graduates**: gets
a GDD section via the change workflow (`Instructions/00-OVERVIEW.md` §4) and its entry
here shrinks to a pointer. Prune dead ideas; never delete the lesson in them.
The boundary of our imagination is the boundary of our success — but the boundary of a
*milestone* is its acceptance list. Both are true; this file is where the first lives
without breaking the second.

---

## 1. New protagonists (food × fable, one per culture, mechanics from the culture)

| Name | Race / fable | Realm | Role | Kit hook |
|------|--------------|-------|------|----------|
| **Lycheenix** | Lychee × phoenix | Starland? | Burst mage | Peel = armor that burns away into wings. Once per run, death → rebirth at 30% HP but permanent −20% max HP. *A legal, costly cheat of permadeath — the single most valuable design space a roguelike has. Balance with fear.* |
| **Kernel Pop** | Corn × colonel | TheDeserted | Artillery / summoner | Lobs kernels that pop into popcorn AoE **when heated** — any fire source detonates them. Built-in duo synergy with Pomegraknight's everything. Ult "Butter Up": slicks the floor, foes slide through momentum. |
| **Bobaba** | Tapioca pearl × witch | night-market world (§2) | Controller | Pearls are her Glow-like ammo; her straw fires them out and **sucks them back through enemies** (every shot hits twice — out and return). Spilled milk-tea puddles slow. |
| **Mochizuki** | Mochi × ninja | Gui Lin or new moon world | Assassin | Stretch-dash that leaves a sticky trail; sticks to walls/ceilings (new movement verb); hits stack "Sticky" (single-timer buff model) slowing foes until they tear free. |
| **Sir Shallot** | Shallot × musketeer | HollowCastle | Duelist | **Quantized shield:** 7 onion layers; each layer absorbs one hit entirely regardless of size (vs. our pool shields — a genuinely different defense). Losing a layer releases a tear-gas ring: foes' sight checks auto-miss for 2 s (plugs straight into the FOES sight FSM). |
| **Maracoco** | Coconut × island shaman | new sea world (§2) | Support / rhythm | Abilities are stronger **on the beat** (visible metronome pulse). First character whose skill expression is timing, not aiming. Coconut-water heal doubles as a throwable puddle. |
| **El Saguaro** | Cactus × mariachi | TheDeserted (native hero!) | Thorns tank | Needle spray BA; damage-reflect passive (mind overlap with Forgotten Kashaya — differentiate: he reflects *ranged*, Kashaya reflects melee); stores "water HP" as an over-heal battery drained by the desert sun. |
| **Tofush** | Tofu × ghost | Kingdom of Horror | Tank / splitter | Soft body: immune to knockback (external impulses absorbed, not applied). Big hits **split him into cubes** the player briefly controls as a swarm; reassemble to heal. Silken vs. firm stance toggle (evasion vs. defense). |
| **Banshana** | Banana × banshee | Kingdom of Horror | CC specialist | Scream cone (fear: foes flee = inverted aggro, reuses FSM); dropped peels are player-made slip hazards (the Hazard system, friendly-fire flavored). |
| **Gingersnap** | Gingerbread × the Gingerbread Man | VanillaKindom | Speedster | "Can't catch me": movement charges a crumb trail — crumbs distract foe aggro (fake player position for sight checks) or can be eaten back for tiny heals. Pomegraknight's realm-mate; natural second starter. |
| **QiongFeng, Emperor of LOONG** | Dragon × emperor | Palace of LOOING | **LV4 boss → protagonist** | Already exists in item lore (his Claw). Make him the PL world boss and the flagship proof of boss-as-protagonist. Player kit: serpentine flight segments, claw sweeps, hoard mechanic (buffs scale with carried wonder cores). |
| **The Inkling** | Living ink blot | The Margins (§2) | Secret character | Unlocked by ???. Abilities are literally unfinished — skill names render as `TBD_SKILL_2` on purpose; power is copying the last skill that hit it. Meta-jokes must ship polished or not at all. |

Standing rule from these: every new character should bring **one new engine verb**
(wall-stick, beat-timing, quantized shields, swarm control) — if a kit is only new
numbers on old verbs, it's a skin, not a character.

## 2. New realms / worlds (each needs: palette, terrain/coast identity, foe pair, node gimmick)

| World | Palette | Landmark / terrain identity | Gimmick |
|-------|---------|------------------------------|---------|
| **Gui Lin** (owed to Pangda — TBD #19) | ink-wash green/grey | broken moon bridge / mist sea | Mist: nodes hide their type icon until adjacent (mist-of-war *inside* a revealed world). |
| **Kingdom of Horror** (owed to PumpKing — TBD #20) | orange/black | scarecrow effigy / graveyard marsh | Day/night flip: its nodes are stronger-but-richer if entered on even days. |
| **Night-Market of Wan** (Bobaba) | neon on dark | overturned food cart / lantern river | Trading world: extra VOID traders, joustable food-stall champions, prices haggled via a minigame. |
| **The Whipped Peaks** (dessert mountains) | cream/mint | collapsed sugar bridge / whipped-cream glacier | Slippery-floor combat modifier on all its arenas (friction table swap). |
| **Coralline Shoals** (sea world; Maracoco) | teal/coral | shipwreck figurehead / kelp forest | Arenas are half water: SoftVolume tech *is* water — we already built swimming and never used it as a biome. |
| **The Margins** | white/ink-black, unfinished linework | an author's note ("fix this later") / blank unwritten paper | **Secret 7th world**, rare replacement for a pool slot: page-ruled floors, sketch-styled foes, loot skewed to wonder cores. The storybook meta made playable. |
| **PAGE NOT FOUND** | magenta/black checkerboard | `missing_asset.png` / the checkerboard void | Pixolotl's "Not Found" realm as a glitch-aesthetic event world reachable only by a `?`-node teleporter accident. Debug aesthetic as content. |

## 3. Foes & elites

- **Per-world foe skins with one twist each** (crab/gull kits re-fleshed): VK candied
  crab (drops a heal orb but explodes sticky), TD sand gull (dash leaves a dust wall),
  HC armored crab (soft-shell cone narrows to 30°), XX void crab (spawns *phantom*
  babies that expire in 5 s).
- **Elite prefixes** (registry-friendly: a modifier list on spawn, exactly like item
  passives): *Gilded* (2× loot, +1 level), *Inked* (hits blind your minimap),
  *Mirrored* (copies the player's last skill's damage as its skill 1), *Evolved*
  (spawns already-evolved, can't evolve again), *Blessed-Eater* (killing it re-blesses
  the nearest Mundane shelter — foes that interact with the map layer!).
- **Mini-boss: the Wandering Phantom** — THE VOID's phantom-contact boss (TBD #13):
  fights as a shadow copy of the *player's current build* (kit mirror-match tech —
  expensive, spectacular, reusable for jousting AI).
- **Foes that hold items** (ITEMS TODO): an item-holding foe glows with the item's
  palette; killing it drops that exact item. Instant "worth the detour"読み.

## 4. Wonder items (sketches, tag-legal)

| Item | Anatomy sketch |
|------|----------------|
| **Sourdough Starter** | `Plantable` `Perishable(3d)` — planted, it *duplicates* every 3 days instead of fruiting; an economy engine that fights the devour clock for garden space. |
| **The Dog-Eared Corner** | Map skill, `Convertible` — bookmark the current node; single use: teleport back to the bookmark. Converts to a spent bookmark (dormant, plantable → grows a new one in 6 days). |
| **Candle Burnt at Both Ends** | `Possession` — passive: +1 stamina per day, but every day-end deals 5% max-HP to the whole party. Tempo vs. attrition. |
| **Crab-Shell Buckler** | Combat passive: damage from **above** (the crab's own 60° cone, inverted) −50%. Teaches the foe-design language back to the player. |
| **Seagull Whistle** | Combat skill, 30 s CD: summon a friendly seagull (foe AI, player faction) for 15 s. Foe-as-ally plumbing; reused by any future summoner. |
| **VOID Compass** | `Eternal` — always shows phantom positions and the devour countdown per ring… and the VOID can see *you* (phantoms move 2 steps per node instead of 1). Information for pressure. |
| **The Understudy's Script** | Skill (map, 3d CD): swap two of your protagonists' held items without a shelter. Quality-of-life as a *drop*, so the convenience is earned. |
| **Fellowship Bands** (set of 3) | First **set items**: each is weak alone (+5 DEF); if all three party members hold one, party-wide +15% ATK and joust wanderers always join on a draw. Set-bonus tech: a party-scope modifier that checks the registry. |
| **A Second Weird Mushroom** | Exactly A Weird Mushroom, but the outcome table is *visible* and you pick the odds arrangement yourself before eating. Sequel-item pattern: same body, inverted information. |
| **Pangda's Spare Gourd** | Character-crossover item: 20 s CD, grant 30 Chi-style shield. Every protagonist eventually gets a signature item — it's the merch line, in-game. |

## 5. `?`-node event seeds (for the EventRunner verb system, T30 §7)

The Weeping Statue (donate HP → permanent DEF); The Backwards Merchant (he buys, never
sells — turn junk items into cores); A Door in a Tree (SoftVolume dungeon micro-map);
The Census Taker (answers about your run so far — the run-performance counters as quiz
material — right answers = loot); A Smaller VOID (feed it an item, it eats a devour
day); The Understudies (meet the *characters you didn't pick* as NPCs; roster as world);
Talent Show (win a joust with BA only); The Plagiarist (copies your held item — a fake
appears in your backpack; one is `Perishable(1d)`, guess which).

## 6. Run-level features & modes

- **Daily Seed** — everyone plays the same map; local time + score; determinism makes
  it nearly free. First community feature to ship.
- **Torn Pages (ascension ladder)** — each victory unlocks the next run-mutator seal:
  devour −5 days, foes start level 2, shelters 30% Mundane, boss timers −30 s… Named
  difficulty with bragging rights.
- **The Storybook (codex + run epilogue)** — every run ends with a generated
  illustrated "page": route drawn on the atlas, days, cause of death as a storybook
  sentence ("…and on the 31st day, the seagulls learned to dash."). Share-image =
  marketing loop; codex collects them. *Highest charm-per-effort item in this file.*
- **Second Edition (NG+)** — beat the game: worlds get "revised text" variants,
   remixed node tables, one extra devour ring. Content reuse with narrative dignity.
- **After the End (endless)** — post-victory, the VOID inverts: the map regrows
  outward from the center, rings respawn at foe level 8+, leaderboard on rings
  survived.
- **Gallery of Illustrations (boss rush)** — refight recruited bosses back-to-back
  with fixed loadouts; unlocked per boss recruited.
- **Weather** — daily map-wide roll: Rain (plants auto-watered, fire skills −20%),
  Heatwave (OnFire +2 s everywhere — Pomegraknight festival), Ashfall (mist everywhere,
  trader prices drop). One roll, three systems touched: exactly the cross-system
  spice the day loop wants.
- **Canvas Weather Modes** — the arena-event foundation and tsunami vertical slice have
  graduated to `Gameplay.gdd` §A.6. Future modes remain idea-stage content: *Meteor Night*
  (meteors glide across, occasional screen flash, rare real falling-star hazard), *Flooding
  Lava* (rising glow, periodic ember rain), *Deep Sea* (caustic light, drifting bubbles —
  pairs with Coralline Shoals). The daily **Weather** roll above may eventually select a
  canvas mode, but that run-layer bridge is not part of the first arena implementation.
- **Mirage Platforms** — fable-flavored parallax trickery: a battlefield tile whose
  collider is authored in battlefield space but whose sprite renders in a farview
  sublayer, so a "distant" mountaintop is impossibly, deterministically standable.
  Ships the "parallax as gameplay" fantasy without camera-dependent colliders
  (see `Docs/MapCreation.gdd` §3).
- **Phantom Chess** — expand THE VOID's phantoms into typed movers (a Rook-phantom
  slides whole rows per step; a Knight-phantom jumps worlds). The map is already a
  board; lean in.
- **Wish (the reserved shelter action, TBD #4)** — candidates: delay one devour day;
  re-bless a chosen Mundane shelter; reroll an unvisited node's mission; summon a
  trader tomorrow; ask the VOID one true question (reveal a world's nodes). Wish
  should touch the *map*, not stats — Rest is body, Sharpen is power, Wish is world.
- **Co-op (couch, 2P)** — second player controls the second protagonist slot in
  combat, hands the pad over on the map. Enormous; park it until after 1.0 — but
  every party-scope system we build (modifiers, benching) should quietly not assume
  party size 1 in code.
- **Photo mode / "My Atlas" export** — render the run's map as a clean poster PNG
  (seed + route + storybook page). Feeds the merch thesis directly
  (`70-MERCHANDISING.md` §4: "your seed as a print").

## 7. Systems glue (small ideas, outsized cohesion)

- **DD evolutions:** DD, if never lost across 3 runs, upgrades (bigger bug, new hat).
  Meta-progression through *care*, not currency.
- **Foe levels visible as costume**, not just stats: level 4+ crabs wear tiny armor;
  level 8 wears a crown. Readability & merch in one stroke.
- **Shelter keepers:** each Blessed shelter has a one-line NPC (a teapot, a lantern)
  whose dialogue reflects run state — cheap narrative surface via the counters.
- **WonderPages as literal pages:** the level-up currency is *pages of your own
  story*; the final boss should have something to say about that. (Dark leader
  identity candidate: **The Editor** — the one who decided the fables needed
  "revision". Name TBD #1 candidate.) *(Canon note: **WonderPages were renamed
  wonder cores** and in-run character leveling was cut in v0.3.7 — cores are now the
  run currency foes drop, not a level-up cost. This idea would re-theme that currency,
  not resurrect leveling.)*

---

*Graduated so far: (none — file created v0.3.5). Move entries to their GDDs via the
change workflow and leave a pointer here.*
