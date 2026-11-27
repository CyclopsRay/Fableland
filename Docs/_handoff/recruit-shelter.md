# Hand-off — recruitment and the shelter as a place

Branch `claude/recruitment-shelter-map-4f6988`, cut from `claude/game-prototype-completion-d9792d`
at v0.12.7. **The version trio is not bumped**, and `Migration.md`, `KNOWLEDGE.md` and
`HANDOFF.md` are untouched: their text is below, for the integrator to place. "v0.12.8" in the
docs and code comments means "the version this lands as" — renumber if it lands later.

The owner's request: 做招募系统，然后给休息站做一个地图（之后每个世界的shelter都不一样，现在先做一个），
把现在的按钮功能融合到地图里。

## 1. Changelog lines (for `Migration.md` §0)

- **Recruitment** (`NODES.gdd` §3.1, §4.5). A run starts with its home world's protagonist
  alone — Pomegraknight, fielded, slots 2 and 3 empty; the prototype roster (all five from day
  one, a team Pomegraknight was not in) is gone. A realm's 4-1 recruits **that realm's own**
  protagonist, whichever boss kit holds it: the handshake carries `AdventureContext.RecruitId`
  (looked up by `RunState.RecruitOf` from the world data) and `BossMission` pays it — the
  stand-in BOSS PumpKing beaten in Starland brings Cleopastar; HollowCastle, TheDeserted and the
  Palace of LOOING bring nobody. Homes are world data (`WorldDef.HomeProtagonistId`). A recruit
  takes the first empty team slot (else the bench), joins at full HP with the Blessings the
  roster shares, once; the toast, the reward line and the day's summary say so. Saves keep their
  roster (loading no longer tops a save up to five). Debug page (key 4): *Recruit all five into
  this run* and *New runs start with all five* (also `FABLELAND_ROSTER=all`); the page now
  scrolls. Team Build's locked faces say where each one can be recruited this run.
- **The shelter is a room** (`NODES.gdd` §5.0). `Scenes/Shelter.tscn` is a walkable side-view
  room (root `Node2D`, UI in its `Ui` layer). The team's lead walks it on their own walk and
  jump — the kit shut by `ShelterWalkControls` — and uses a **station** with **E** (also W / ↑:
  the new `interact` action, registered in `InputDefaults`): the road (Back / Continue into the
  VOID), the team board (Team Build), the lantern (Finish the Day), the campfire (Rest), the
  whetstone (Sharpen Weapon), the anvil (Sharpen Armor). Stations are data (`ShelterTable`: rows,
  and one room per world — the default for now) with behaviours (`ShelterStations`); a station
  with no behaviour is not built, so the plantation, the enchanting altar and the wanderer keep
  empty spots until their systems register one. `ShelterController.Exits` stays the one rule for
  the ways out; confirmations and "a Blessing ends the day" are unchanged. All three kinds:
  Transportation Hub, Eidolon Shelter (no lantern), Last Shelter. Everything is drawn in code —
  TODO(art) list in `NODES.gdd` §5.0.
- **Fixed in passing:** a spent Eidolon Shelter entered with no stamina was a dead end (its Back
  was enabled and did nothing); an Eidolon Shelter's Back read "Continue into the VOID"; every key
  on the pause menu's controls card and under the countdown read "… - Physical".
- **New check** `Scenes/Run/RecruitShelterCheck.tscn` (398 checks, ~15 s; `-- --shots=dir` in a
  window saves pictures of each kind of shelter). The run check now expects each capital to
  recruit its realm's own and sets a party for the zone-6 walk (2010 checks).

## 2. Caveats (for `KNOWLEDGE.md`, "Caveats / gotchas")

### Godot 4.3+ names a physical key "E - Physical", not "E (Physical)" (v0.12.8, controls card)
- **Symptom:** the shelter's prompt read "[E - Physical]". Looking further: since v0.12.5 every
  line of the pause menu's controls card and the countdown's key line read "Space - Physical",
  "J - Physical / Left Mouse Button" — `ControlsCard.KeysOf` stripped only " (Physical)", the old
  format, and the new one passed through.
- **Rule:** name a key for the player through `ControlsCard.KeysOf` / `FirstKeyOf` (they strip
  both forms) — never `InputEvent.AsText()` directly.
- **Why:** the engine's text for an event is for the editor, and its format changed between
  versions without any error; only a check that reads the words (the recruit check now does)
  notices.

### An exit's action must ask the same rule that opened it (v0.12.8, the Eidolon Shelter's dead end)
- **Symptom:** a spent Eidolon Shelter, entered on the last stamina point (or reopened as the
  current node with none left), showed Back enabled — `Exits()` opens Back at any stamina once
  the crossing is spent — and pressing it did nothing: `OnBack` still refused when stamina was 0.
  No Finish the Day there either: a screen with no way out, audit #16 again.
- **Rule:** the rule lives in one place (`ShelterController.Exits`); the action asks it
  (`RequestLeave` leaves when `Exits().Back`, nothing else).
- **Why:** v0.12.4 fixed the rule and its check tested the rule — not the button, which kept a
  second copy of an older rule.

### An Eidolon Shelter is a `NodeKind.Shelter` too — ask the bridge first (v0.12.8)
- **Symptom:** in every Eidolon Shelter the old screen's Back read "Continue into the VOID".
- **Rule:** `ShelterController.KindOf(node)`: a node with a `RealityBridgeId` is an Eidolon
  Shelter; any other Shelter node is the Last Shelter; everything else a hub.
- **Why:** the bridge's midpoint reused the zone-6 shelter's kind; title and status asked the
  bridge first, the button's label did not.

### A fighter who joins later must get the global Blessings (v0.12.8, designed around)
- **Symptom (avoided):** `AddAtk`, `AddDef` and `RestBlessing` write to every fighter *on the
  roster at the time*. With five owned from day one nobody could miss one; with recruiting, a
  fighter met on day 20 would have joined without the ATK bought on day 5.
- **Rule:** a joining fighter starts with each pool's minimum over the roster
  (`RunState.JoiningState`) — the part every fighter shares.
- **Why:** NODES §3.3 says these stats are global, but they are stored per fighter; a stored
  copy of a global value must be seeded when a new holder appears.

### A lookup's check must ask for "none" as well as for each hit (v0.12.8, recruit check)
- **Symptom:** a mutation that let the nearest station answer from anywhere (reach ignored)
  survived: walking past the stations in order still met each one in order.
- **Rule:** when a check tests a lookup, also stand somewhere it must answer nothing (the
  shelter's arrival point is out of every station's reach).

### Two mutations in one batch can hide each other (v0.12.8, recruit check)
- **Symptom:** "a party of one shows a Switch slot" looked caught — but in that batch "a run
  starts with five" also ran, so the party was three and the slot showed anyway.
- **Rule:** batch only mutations whose preconditions are independent; rerun any whose catching
  line could have been tripped by another one, alone. (It was caught alone.)

### Reading the day's summary in a check: right after `EndDay()` (v0.12.8, check-writing)
- `LastDayEndSummary` is read and cleared by the map when it loads. Read it after `EndDay()`
  and before waiting for the map scene, or it is "".

### The shelter's UI is the `Ui` layer, not the root (v0.12.8, reference)
- `Shelter.tscn`'s root is a `Node2D` now (a room with a camera). A `Control` added to it would
  scroll with the room: add panels to `Ui` — `ShelterController.ShowPanel` does, and freezes the
  walker while the panel shows. Dialogs go there too.

## 3. TBDs (new — for `10-DESIGN.md` §6; numbers are the integrator's)

Rows #2, #20, #24 and #29 were annotated in place (#29 resolved).
- **Wanderers and the joust** (NODES §5.4): designed — the protagonists whose home realm the run
  did not draw wait at hubs (the wanderer station), drawn from a stream `seed:wanderers` — not
  built: it needs AI kits for them (or the stand-in rule) and the arena's mock-fight mode.
- **The three worlds with nobody** (HollowCastle, TheDeserted, Palace of LOOING): their 4-1
  recruits nobody. New protagonists for them, or something else at those capitals?
- **Each world's shelter** — its own room row and its art (NODES §5.0's TODO(art) table); the
  Fable model's direction. Room size, station positions, the arrival point are first-pass.
- **The Last Shelter kind** is served but no generator makes one today (only the check builds
  one). Keep it, or drop the kind.
- **Shelter feel:** no footsteps or station sounds beyond the UI cues (`ui/hover` on reaching an
  enabled station, `ui/press` / `ui/back` on use); the kit's debug cone outline is drawn round
  the walker in the shelter as in the arena (the open VFX question in HANDOFF §4).
- **Interact keys** E / W / ↑ — the owner may prefer others (rebind in `project.godot`: an
  `interact` entry there wins).

## 4. Check counts (all headless, scratch `HOME`, this branch)

| Check | Count | Note |
|---|---|---|
| RecruitShelterCheck (new) | 398 | 18 mutations applied in five runs, each caught by its line |
| RunWalkCheck | 2010 | was 1993: +8 "the arena recruits the realm's own", +7 "the stand-in's own did not join", +2 the zone-6 walk's party |
| FlowCheck | 37 | unchanged (`Exits` kept as is) |
| BossFightCheck | 192 | unchanged (a staged realm boss recruits its own world's — PumpKing) |
| Zone6Check | 115 | unchanged |
| FeedbackCheck | 251 | unchanged (run because the shelter raises UI and flow cues) |
| AudioCheck | 1108 | unchanged |

`dotnet build Fableland.csproj`: 0 warnings, 0 errors.

## 5. Decisions made for the owner (all written into the GDDs)

1. **A run starts with its home protagonist alone** — Pomegraknight (every run's home is
   VanillaKindom today).
2. **The recruit belongs to the realm, not the boss kit**: the stand-in PumpKing recruits each
   realm's own; a realm's own boss row must name the realm's home protagonist (checked).
3. **Homes:** VK Pomegraknight, KH PumpKing, TF Pixolotl, **SL Cleopastar** and **BM Sifu Pangda**
   (provisional — the same calls her effects and his sounds already make, TBD #37/#38; her GDD
   says the Palace of LOOING, his Gui Lin). One home per fighter, held to the presentation's
   `Medium.HomeOf` by the check.
4. **HC, TD, PL recruit nobody** (they still pay Yukai's Rope).
5. **A recruit takes the first empty team slot**, else the bench (the GDD said "added to the
   bench" — with a team of one that meant a shelter detour). The opener of the next battle does
   not change.
6. **A recruit joins at full HP** with the roster's shared Blessings.
7. **Saves keep their roster**; a save from the prototype roster keeps its five; a save with
   nobody valid gets the run's starter (was Pixolotl).
8. **Debug:** "Recruit all five into this run" and "New runs start with all five" (off by
   default; `FABLELAND_ROSTER=all` turns it on) — the old team Sifu Pangda · Pixolotl · Cleopastar.
9. **One fighter: no Switch slot**, Tab does nothing (NODES §3.3 said "greyed out"; the code has
   always hidden it — the GDD now says so).
10. **The shelter is walked as the team's lead** on the fighter's own scene (walk, jump, drop;
    attacks, skills and the ultimate shut); a new lead from Team Build walks on from the same spot.
11. **Interact = E**, also W / ↑ ("go in"); shown bottom left, in the prompt and over the station;
    on the pause menu's controls card too.
12. **The stations:** the road (a door), the team board, the lantern (Finish the Day), the
    campfire (Rest), the whetstone (Sharpen Weapon), the anvil (Sharpen Armor); reserved: a
    plantation (two plots), an enchanting altar, a wanderer. The Eidolon Shelter has no lantern.
13. **One room** (48 × 16 m) for every world until each has its own row; drawn toward the world's
    map colour (violet on a bridge, drained past the singularity).
14. **A closed station stays, dimmed, and says why**; the road asks nothing (as Back did); the
    lantern and the Blessings keep their confirmations.
15. **The road opens whenever the exits rule says Back** — which closed the Eidolon dead end.
16. **Wanderers** are the protagonists whose home the run did not draw (designed, not built).
17. Team Build's locked faces say "beat the boss of <world>" or "<world> is not one of this
    run's worlds".

## 6. For the items session — adding the plantation or the enchanting altar

Their spots are in `ShelterTable.Default` already. Add the behaviour — one line in
`ShelterStations.Builtins` (or `ShelterStations.Register` from your own code before a shelter
opens):

```csharp
[ShelterTable.Plantation] = new()
{
    Offer = visit => StationOffer.Open("Tend the plots"),             // or Closed(line, reason)
    Use = visit => visit.Shelter.ShowPanel(new PlantationPanel(visit)), // a Control; the walker waits
},
```

`ShelterVisit` carries the run, the node, the kind, Blessed, stamina and `Exits`; the screen
offers `RequestBlessing`, `RequestFinishDay`, `RequestLeave`, `OpenTeamBuild`, `Say` and
`ShowPanel`. Change the row's `OfferedAt` if it should stand in other kinds. The recruit check's
"added later" step registers whichever reserved station has no behaviour; once all three have
one it says so and skips.

## 7. Merge notes

- `RunState.cs` edits are confined to: `_EnterTree` (the env switch, the table validation),
  `NewRun` (one call: `StartRoster`), `BeginAdventure` (the `RecruitId` line), the roster block
  after `FindProtagonist` (`GrantProtagonist` and the new helpers; `EnsurePrototypeRoster` is
  gone), and `HydrateSave`'s roster block. No item code touched.
- `GameManager.cs`: the `RecruitId` property and three lines next to `BossId`. `BossMission.cs`:
  `Setup`, `Reward`, `SuccessToast`. `ShelterController.cs`: everything above Team Build is new;
  Team Build is as it was except the panel's parent (`_ui`), public `OpenTeamBuild` /
  `CloseTeamBuild` (modal + new lead) and the locked-face hint.
- `FlowCheck` is untouched; `ShelterController.Exits` has the same signature and meaning.
- New `.uid` files are committed with their scripts.
