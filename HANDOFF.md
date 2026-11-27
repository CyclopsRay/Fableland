# HANDOFF — read this instead of the last session

Written 2026-11-27, at the end of a long multi-agent session (v0.11.1 → v0.11.8); updated
the same day for **v0.12.0** (zone 6 as a stage, realm laws), **v0.12.1** (the first two
zone-6 kits), **v0.12.2** (Māra's design; the dark leader settled), **v0.12.3** (Māra built:
his wheel and his kit, art in placeholder) and **v0.12.4** (the audit's run-stopping bugs and
kit/GDD mismatches fixed, `FlowCheck`). **Replace this file at every hand-off:** it describes
*now*; `Migration.md` §0 describes history.

## 1. Where the work is

- **Branch `claude/game-prototype-completion-d9792d`, v0.12.4**, thirteen commits on top of
  v0.11.0. **Not pushed.** Worktree: `.claude/worktrees/sheik-shark-medical-city-1ade9e`.
- `main` was fast-forwarded to v0.12.3 (`7afe440`) on 2026-11-27, **locally, not pushed**. The
  owner asks for each fast-forward; until then it trails the branch.
  `LocalTimeScale` (the main checkout, `/Users/cyclops/fableland`) and `origin/*` are still at
  **v0.11.0** (`b861921`). If a new session's worktree starts older than v0.12.4, first run
  `git merge --ff-only claude/game-prototype-completion-d9792d`.
- The owner's Godot editor (and a running game) was open on
  `.claude/worktrees/game-map-editor-ui-b1ced6`: v0.10.9 plus the old **uncommitted** swimming
  edits. v0.11.1 supersedes them. **Never commit them**, because they read `Input` directly and the
  player's keys would swim the boss. To see the new work, open the session worktree in Godot, or
  merge the branch into the one the owner runs (ask first).
- **Not in git, only on disk:**
  `.claude/worktrees/sheik-shark-medical-city-1ade9e/Docs/Art/StyleExploration/_originals/`
  (466 MB). It holds the full-size Tripo images, including the 16 `KIT-*.png` that
  `Tools/overworld_art/build_kits.py` cuts the overworld page kits from. Back it up before that
  worktree is removed.
- **Worktrees this session made, all merged or empty** (remove only with the owner's OK):
  - `agent-a0feba80d7732ab82` (VFX)
  - `agent-a272b2cf0b2e74806` (audio)
  - `agent-a679b849bac4d2de6` (maps)
  - `agent-a7d1fdec8e4ff2493` (overworld)
  - `agent-a8a04a0d965c1fa02` (integrator)
  - `agent-acf48b0d9f4c33cbf` (shell; died before writing anything)

## 2. What exists now

| v | Commit | What landed | Read before touching it |
|---|---|---|---|
| 0.11.1 | `d94b4c5` | A fighter swims in every soft volume, through `Controls` (a boss swims on its own pad) | `MapCreation.gdd` §2.2a |
| 0.11.2 | `10581c8` | **Cue bus**: gameplay raises named moments and never names an effect. Per-body hit-stop. `FeelDirector` adds shake, kick, punch and the hit flash. Pomegraknight is wired as the reference kit | `Docs/Feedback.gdd` (§6 = the cue vocabulary), `Docs/Tech/T10` §4b |
| 0.11.3 | `4cf7465` | Every shipped map is reachable. Cloud Court is the Palace of LOOING's high ground. Zone 6 deals maps from the run's own worlds, drained by a VOID treatment | `MapGDD.md` §2, §7 |
| 0.11.4 | `a7fad6c` | Overworld as **torn storybook pages**: a third map view (off by default), a page kit per world, a demo | `Docs/Art/OverworldStyle.md`, `MapGDD.md` §11a |
| 0.11.5 | `9a7972f` | Style exploration archived: 78 concept previews, `REVIEW.md`, the pick gallery, research digests R1 (audio) and R2 (VFX/feel) | `Docs/Art/StyleExploration/README.md` |
| 0.11.6 | `1fdd7f7` | **VfxDirector**: every cue drawn in its owner's art medium (ten media), pooled and budgeted | `Docs/VFX.gdd` |
| 0.11.7 | `d4c48ec` | **AudioDirector**: all sound and music synthesised from data, 14 music profiles, intensity stems, a combo ladder in the realm's scale. Also the GDD-vs-code audit | `Docs/Audio.gdd`, `Docs/Research/AUDIT-v0.11.1.md` |
| 0.11.8 | `9f97e87` | This hand-off | — |
| 0.12.0 | `1490004` | **Zone 6 is a stage that rotates; realms have laws.** Two variants — the Void Frontier (gravity ladder, the Unwritten's Styx Tide) and Avīci (the five poisons' seals, Māra). Gates are mini-bosses whose defeat the run survives. Wulin's Qinggong. One rules record (`ArenaRules`) applied to every body. Twelve boss rows on the stand-in kit. Art direction confirmed (cyanotype / nagtang) | `Docs/Zone6.gdd`, `MapGDD.md` §2.3, §7 |
| 0.12.1 | `219bfed` | **The Ferryman and the Asura of Wrath on their own kits** (`Zone6BossFighter`; sprites; Undertow, obol toll, Skiff Ram; flurry, Wrath Fire, vajra, Rising Wrath) | `Zone6.gdd` §4.1, §5.1, `FOES.gdd` §8.7 |
| 0.12.2 | `1350e9d` | **The dark leader is each variant's final boss** (TBD #1 closed by the owner). **Māra's full design, proposed for review:** the Wheel of Becoming, the human realm as the only interval, the last 5 % falling only there, his kit, arena, look and build plan. Docs only | `Docs/Mara.gdd` |
| 0.12.3 | `7afe440` | **Māra built** (`Mara.gdd` steps 1–2, the owner's "先做…占位先"): the **Wheel of Becoming** as his signature (rests, skips, stacks, the realms' modifiers, the lock, the Last Turn, Liberation, the Offering) and **his own kit** (Claw Rake, Noose, Hand of the Wheel, every realm's arrival strike, Doubt's false tells, Delusion's images). Placeholders drawn in code; **art prompts written, not generated** | `Docs/Mara.gdd` §13.4, `Docs/Art/MaraArtPrompts.md` |
| 0.12.4 | — | **The audit's bugs that could end or stall a run, and kits back to their GDDs** (Part 2 #11–#17): resolution invincibility held by sources (Pixolotl's Shift), the Eidolon Shelter's dead end, Yukai's Rope letting go, Cleopastar's star jump, damage credited to Pangda's blows and to thrown stars and heads, the Fire Tornado replacing Slash 1 and spinning in place, the seagull's puddle and dash; the Asura's flurry lands every arm. New **`FlowCheck`**, each fix broken once | `Docs/Research/AUDIT-v0.11.1.md` Part 2 (its status line), KNOWLEDGE v0.12.4 |

**Checks.** All are headless, each with a scratch `HOME`; commands are in `40-QA.md`. Counts at v0.12.4, all passing:

| Check | Scene | Count |
|---|---|---|
| Feedback | `Scenes/Feedback/FeedbackCheck.tscn` | 251 |
| VFX | `Scenes/Feedback/VfxCheck.tscn` | 1298 |
| Audio | `Scenes/Feedback/AudioCheck.tscn` | 1108 |
| Map content | `Scenes/MapCreation/MapContentCheck.tscn` | 1171 |
| Boss | `Scenes/Bosses/BossFightCheck.tscn` | 192 |
| Zone 6 | `Scenes/Run/Zone6Check.tscn` | 115 |
| Run | `Scenes/Run/RunWalkCheck.tscn` | 1993 without `--saves` (2005 at v0.12.1 with `-- --saves=<copy of the owner's slots>`) |
| Flow (new, v0.12.4) | `Scenes/Run/FlowCheck.tscn` | 25 — the audit's Part 2 fixes, each on real bodies |

**Look and listen without a fight:**
- `Scenes/Dev/VfxGallery.tscn`
- `Scenes/Dev/AudioAudition.tscn`
- `Scenes/Dev/OverworldArtDemo.tscn`

## 3. Not done — in the order the owner's request implies

1. **The other four kits' feedback.** Cleopastar, Pixolotl, PumpKing and Sifu Pangda raise almost
   no cues. Through the fallback they get their home medium's generic effects and sounds.
   - **Work order:** `AUDIT-v0.11.1.md` Part 1, 161 rows, one per gameplay moment, with file → method and geometry.
   - **Hard spots:** its §1.12.
   - **New cue words** the audit proposes must be added to `Feedback.gdd` §6 first: `ba.cast`, `ba.throw`, `ba.reload`, `status.shielded`, `shield.break`, `passive.*`, `zone`, `storm`, `wave.start`, item `cast`/`proc`/`drag`.
   - Then add rows in `VfxTable` / `SfxTable`. Art direction: the signature-effect sections of `VFX.gdd` and `Audio.gdd`.
   - Remove the five direct `ShakeCamera()` calls (4 in Sifu Pangda, 1 in Pixolotl).
   - Audio memory will pass its 8 MB budget once the kits are wired; the owner must choose a policy (§4).
2. **Gameplay bugs from the audit** (Part 2) — **#11–#17 fixed in v0.12.4**, each reproduced
   and pinned by `Scenes/Run/FlowCheck.tscn`. Still open from that list: the ambient foes that
   keep arriving during Collection's grace, and Pixolotl's magazine reloading only from empty
   (its GDD is silent).

3. **Shell polish** (audit Part 2 #1–4, #6, #20). Not started.
   - A controls card built from the `InputMap`.
   - Rewards named by the arena and shown by the HUD.
   - Wonder cores shown, display only.
   - The debug UI only in debug mode; nothing restarts a run without asking.
   - The HUD shows the magazine, a consistent switch slot and a face per character.
   - A Settings screen for `FeedbackSettings` and the four volumes, persisted under `user://`. It must honour `SaveGameService`'s redirection, so checks never write the owner's folder.
   - Quit; a short beat before RUN OVER; one shared paper-and-ink `Theme`.
   - Caveats to read first:
     - "A full-screen background `Control` eats clicks"
     - "Folding a UI panel"
     - "A bare-key InputMap action swallows…"
     - "Debug roster selection must relinquish GUI focus"
     - "Testing anything that writes to `user://`"
     - "An unreadable save slot is not an empty save slot"
     - "The HP bar is block-based"
4. **`arena.enter` should report the realm on screen** (TBD #50). *Zone 6's half is done
   (v0.12.0): it reports its variant at the drained lightness.* Left: the tag becomes
   `MapRealm.Of(map)`, so Cloud Court reports `CC` and gets its enamel effects and music.
6. **Zone 6's kits** (TBD #64).
   - **Built:** the Ferryman and the Asura of Wrath (v0.12.1) and Māra (v0.12.3), each a
     `Zone6BossFighter` in `Scripts/Bosses/Zone6/`. The other nine field BOSS PumpKing's kit
     under their own rows.
   - **Next:** the other eight guardians and poisons, and the Unwritten's Borrowed Shapes and
     Blank Page. Their designs are in `Zone6.gdd` §4.1 and §5.1. The concept art is
     `Z6-VF-2…5` and `Z6-AV-1, 3…5`; there are no keyed sprites yet.
   - **Māra's step 3:** Fable reviews `Docs/Art/MaraArtPrompts.md`; then generate the art in
     Tripo; then key the sheets and swap out the placeholders (`MaraFigure`, the backdrop in
     `WheelOfBecomingField`). Also left: his arena `Z6-AV-F` as a map, his music profile, and
     the run-over screen naming the dark leader (it still says "defeated the dark leader").
   - **A known gap, not changed:** the Asura's Six-Arm Flurry shoves on every arm, so the
     player's post-hit grace lets only its first arm land. See KNOWLEDGE v0.12.3, "A blow of
     several hits against the player: only the last may shove".
   - **Read first:** `FOES.gdd` §8.7, `Zone6.gdd` §8–§9, and the v0.12.1 and v0.12.3 caveats
     in `KNOWLEDGE.md`.
5. **Nobody has watched or listened to anything.** Play in the real renderer, then tune the data:
   - `FeedbackTable` / `FeelTable`: light hits barely shake; the flash colour.
   - VFX: mark sizes (4–23 px) and ambience; status loops are timid.
   - Audio: levels; Starland's "pure tones", the Palace's low-pass, the boomy HC/VOID mixes.
   - Audio: with sound on (`FABLELAND_AUDIO=trace`) the run check reports 38 first sounds of an arena dropped while its sound bank renders. Not investigated.
   - Open TBDs from this session: #36–#61 in `10-DESIGN.md` §6.

## 4. Waiting on the owner

- **Style picks.** Gallery: https://claude.ai/artifact/BfrSTsTwjZypx4J7aMvW2b. Picks save to its database (collection `picks`, doc `owner`). Recommendations are in `REVIEW.md`; three decisions gate any repaint:
  - grid collision versus painterly art;
  - 32 versus 64 px/m;
  - whether AI art may enter kits.
- **Overworld pages:**
  - is this the direction, at this fidelity?
  - should it be the default view?
  - may the realm's seat symbol be about 2× its size (reserve a lot for it)?
  - which next improvement comes first (`OverworldStyle.md`)?
- **Provisional calls used everywhere:** Cleopastar → Starland (poster style) and Sifu Pangda → Banboo Maze (ink), for both effects and sound.
- **VFX:**
  - the i-frame blink fix in `CharacterController` (kept);
  - the kit's debug cone outline still drawn under slashes;
  - light-hit shake almost invisible;
  - the VOID and Cloud Court styles have no art bible.
- **Audio:**
  - listen and retune;
  - UI sounds use the beach's watercolour medium;
  - memory policy once kits are wired: party-only cache, or a bigger budget.
- **`seashore_5`** needs a second Level Goal marker before it can serve as a boss arena.
- **Māra** (`Mara.gdd`). Built in v0.12.3 as designed, so the six questions in §14 are still
  his to answer:
  - his name;
  - the last-5 % finish;
  - the Offering;
  - what five gates won should buy;
  - the story frame;
  - the numbers.

  A different answer changes code, not only the document. Play him from the debug page: key 4
  → Zone 6 → Māra.
- **Zone 6 (v0.12.0), every call provisional** (`Zone6.gdd` §10): the twelve bosses; the
  defeat rule (25 % floor, unlimited retries —
  #65); every number (#66); the Frontier's core on Starland's sea-level plain until its own
  arena. Laws for the other seven realms (#62). Play both variants: the debug page (key 4 →
  Zone 6) forces a variant, stages any zone-6 boss, and sets gravity and seals.
- **Design gaps** (audit Part 2):
  - the ultimate does nothing for all five fighters;
  - every realm's boss is PumpKing, and beating him recruits nobody;
  - 8 of 14 items cannot be obtained;
  - "?" nodes have 3 placeholder events.

## 5. How to work here (each of these cost hours)

- **Models on this Mac.** In the desktop app, subagent models `opus` / `sonnet` / `haiku`, the
  default subagent model, `WebSearch` and `WebFetch` are routed to DeepSeek and fail. Spawn agents
  with `model: "fable"`, or fork: a fork inherits the session's model, so an Opus session can still
  delegate. Read the web through the browser pane.
- **Budget.** Fable and the 5-hour window both ran out repeatedly.
  - Resume a stopped agent with `SendMessage`; do not start a new one.
  - Keep briefs targeted: name the files and caveat headings to read.
  - Commit in versioned pieces as each piece is verified.
- **Reading.** `KNOWLEDGE.md` is 185 KB (155 caveats) and `Migration.md` 129 KB; do not read
  either whole. Read KNOWLEDGE's Conventions, run `grep -n '^### ' KNOWLEDGE.md`, and open only
  the caveats for your area (named per task above).
- **Python:** `/opt/homebrew/bin/python3` only. The `python3` on the PATH is an x86 Anaconda that
  starts Godot under Rosetta and hangs loops.
- **Godot runs:** every run gets a scratch `HOME`, because `user://` is one folder shared with
  the owner's saves and editor maps.
- **Fresh worktrees:** a fresh worktree has no import cache; import first (KNOWLEDGE caveat).
- **Parallel branches:** have one integrator land them.
  - Each agent writes its changelog, caveats and TBDs to `Docs/_handoff/<name>.md`; only the integrator edits `Migration.md`, `KNOWLEDGE.md` and the version trio.
  - A branch that re-applied a base from a patch merges cleanly only with `git cherry-pick -n <base>..<tip>`.
- **Images:** Tripo Studio web in the browser pane (memory note). The month's free images are
  94/100 used.
