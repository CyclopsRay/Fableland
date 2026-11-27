# HANDOFF — read this instead of the last session

Written 2026-11-27, at the end of a long multi-agent session (v0.11.1 → v0.11.8). Fable's
usage limit ran out, so the session was wrapped up on Opus. **Replace this file at every
hand-off:** it describes *now*; `Migration.md` §0 describes history.

## 1. Where the work is

- **Branch `claude/game-prototype-completion-d9792d`, v0.11.8**, eight commits on top of
  v0.11.0. **Not pushed.** Worktree: `.claude/worktrees/sheik-shark-medical-city-1ade9e`.
- `main` was fast-forwarded to this commit (v0.11.8) on 2026-11-27, **locally, not pushed**.
  `LocalTimeScale` (the main checkout, `/Users/cyclops/fableland`) and `origin/*` are still at
  **v0.11.0** (`b861921`). If a new session's worktree starts older than v0.11.8, first run
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
| 0.11.8 | — | This hand-off | — |

**Checks.** All are headless, each with a scratch `HOME`; commands are in `40-QA.md`. Counts at v0.11.7, all passing:

| Check | Scene | Count |
|---|---|---|
| Feedback | `Scenes/Feedback/FeedbackCheck.tscn` | 249 |
| VFX | `Scenes/Feedback/VfxCheck.tscn` | 1298 |
| Audio | `Scenes/Feedback/AudioCheck.tscn` | 1098 |
| Map content | `Scenes/MapCreation/MapContentCheck.tscn` | 1170 |
| Boss | `Scenes/Bosses/BossFightCheck.tscn` | 109 |
| Run | `Scenes/Run/RunWalkCheck.tscn` | 1328 |

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
2. **Gameplay bugs from the audit** (Part 2). Entries #12, #13, #16 and #17 were found by reading, not by running: reproduce each with a check first.

   | # | Bug | Caveats to read first |
   |---|---|---|
   | 13 | Pixolotl's Shift after a mission resolves drops the end-of-mission invincibility | "Mission resolution must gate player damage at the shared controller"; "External arena actions must ask the active character's skill-state policy"; "A control lock does not freeze physics by itself" |
   | 17 | Cleopastar's star jump never fires | "A special one-way support must not silently refresh ordinary jump charges"; "An enterable character-specific field needs a membership filter" |
   | 12 | Yukai's Rope against a solid wall | — |
   | 16 | The Eidolon Shelter can be re-entered and has no exit | — |
   | 14 | Kashaya's passive and Pome's Bravery heal miss triggers | — |
   | 15 | Fire Tornado's numbers differ from the GDD | — |

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
4. **`arena.enter` should report the realm on screen** (TBD #49, #50).
   - The tag becomes `MapRealm.Of(map)`, so Cloud Court reports `CC` and gets its enamel effects and music.
   - A VOID-touched arena reports the VOID key, using the backdrop's lightness after the treatment. The flash colour and the medium choice then match the picture.
   - Today Cloud Court gets gongbi effects, and zone 6 gets the borrowed world's.
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
