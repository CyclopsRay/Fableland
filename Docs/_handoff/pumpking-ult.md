# Hand-off — PumpKing's ultimate, the Black Prison (黑狱)

Branch `claude/pumpking-black-prison-f31091`, cut from `claude/game-prototype-completion-d9792d`
at v0.12.7. **No version bump** and no edits to `Migration.md`, `KNOWLEDGE.md` or `HANDOFF.md`:
the integrator lands these lines there and picks the version.

## 1. Changelog lines

- **PumpKing's ultimate, the Black Prison (Q, the full ring).** It is the game's first ultimate.
  - **The prison:** a 3 m circle (his head's blast) where he aims, at most 8 m away; 1 s of
    warning that touches nobody.
  - **4 s held:** every body of the other side inside it moves at half speed, jumps half as
    high, starts no skill (the basic attack still goes) and sees only one map cell (3 m).
  - **Pumpkins:** one of random size every 0.5 s, flung out of it for 10–25 damage.
  - **The recast (free, once):** PumpKing's big mouth opens at the centre, he blinks into it,
    and his head goes off at its biggest (3 m, 100 damage, no shove).
  - **The art is placeholder**, drawn in code, with every TODO(art) listed in `PumpKing.gdd`.
  - **To try it from F5:** debug page (key 4) → PumpKing → "Fill the ult ring (then Q)".
- **Crowd control is a reaction on `ICombatTarget`.** `SetMoveSpeedScale`,
  `SetJumpHeightScale`, `SetSilenced`, `SetSightLimit` / `ClearSightLimit`, each held by a
  named source, on `CharacterController` and `BaseFoe` (a Māra image takes them as nothing).
  - **Silence:** a fighter's Skill 1, Skill 2 and ult are not read; a foe's skills don't start.
  - **Sight cap:** a capped foe drops the player at once, and a hit doesn't turn it; a boss
    brain finds no target past the cap.
  - **For the items work:** THE VOID's "all foes lose vision for 10 s" is
    `SetSightLimit(source, 0)` on each foe, and `ClearSightLimit` after.
- **Soul Form obeys `MoveSpeedScale`.** A soul is slowed by a prison, and by a zone-6 seal that
  slows movement; before this it ignored both.
- **The controls card's ult line** names the Black Prison.
- **The boss check gains the Black Prison** — 192 → **259 checks**; every rule was broken once
  and caught (§4).

## 2. Caveats hit (symptom → rule → why) — for `KNOWLEDGE.md`

### A release and the body's reaction share a physics frame — a probe counted a jump "during" the prison (caught in the check)
- **Symptom:** "a silenced crab beside him jumped". It jumped on the tick the prison let go: the
  prison (earlier in the tree) released it, and the crab, later in the same physics frame,
  jumped. The probe read the crab after that frame and the prison's state of the frame before.
- **Rule:** a probe that watches a held body stops on the tick the holder lets go: check the
  holder's state *after* each wait, before reading the body.
- **Why:** the holder and the held body each run their own `_PhysicsProcess` in tree order;
  one frame can contain both "freed" and "acted".

### A crab doubles a blow from above — a pumpkin's 25 read 48 (caught in the check)
- **Symptom:** pumpkin hits of 38–48 against a 10–25 rule.
- **Rule:** a probe that measures damage on a crab must allow its Soft Shell (FOES §3, ×2 from a
  source in the 60° cone above), or strike it from the side. The prison probe puts the blast
  centre at a crab's height for the same reason.
- **Why:** the hit is right. The source position is part of a hit, and crabs read it.

### A virtual pad nobody steps never presses anything (designed around)
- **Symptom (avoided):** `StillPlayer`'s pad in the boss check is never stepped. It is a pad
  "never pressed" by design, and a tap on it would never land.
- **Rule:** a probe that presses a pad no brain owns registers it with the check, which steps
  it before the stage, as `FlowCheck` and `FeedbackCheck` do. `BossFightCheck` now has
  `_pads`.

### A polygon with a repeated point is not drawn (caught before it ran)
- **Symptom (avoided):** the mouth's two lips met at both corners, written as four points on two
  spots. Godot's triangulation refuses a polygon with a repeated vertex and draws nothing,
  with an error. Headless runs would not have shown it.
- **Rule:** when two polylines are joined into one polygon, give each shared point once.

### A sight cap must reach a boss's senses, or the boss is blind in name only (designed around)
- **Rule:** a boss brain sees through `BossSenses.Sample` alone, so the cap is enforced there.
  `CharacterController.SightLimitPx` past the target means "no target".
- **Why:** a kit never reads the player. Only the brain does.

### A press made while stunned is lost — a mutant survived because the probe's PumpKing was stunned (found by the mutation run)
- **Symptom:** "the recast twice" survived. The probe pressed Q once more after the blink and
  saw no second blink. With the mutant, the press should have opened a second mouth.
- **Cause:** the blink lands him beside the penned crab. Its body blow stuns him for 0.25 s,
  and a stunned body does not read buttons (`HandleAbilities` runs only when not `Frozen`). A
  `JustPressed` lasts one frame, so the press was simply gone and the assertion was vacuous.
- **Rule:** when a probe presses a button on a body that something can hit, take its stuns away
  (`SetStunTakenScale("probe.…", 0)`, a poise source). Press more than once, and count the cue
  the press raises (`ult.recast`), not only the effect.
- **Why:** "nothing happened" passes for two reasons: the rule held, or the press never arrived.
  Only the cue of the press tells them apart.

### Reading a freed node: C# members still answer, native ones throw (found by the mutation run)
- **Symptom:** under one mutant the check threw `ObjectDisposedException` and stopped 16 checks
  early. The waits ran long, the prison faded and freed itself, then `prison.VeilShown` read its
  freed veil's `Visible`.
- **Rule:** a getter a check (or a brain) may call after the node is gone guards each native
  read with `IsInstanceValid`. `Over`, `EndedAt` and other plain C# members answer from the
  wrapper even after the node is freed.
- **Why:** the C# wrapper outlives the native object. Only calls into the engine fail.

### Tooling: a copy of `HEAD` for a baseline needs the git-ignored `.import` files
- **Symptom:** `git archive HEAD | tar -x` plus a copied `.godot`, then the boss check: "No
  loader found for resource: …png", and every PumpKing scene failed to load.
- **Rule:** copy every `*.import` from the checkout too; then `dotnet build` and run.
  - That gives the baseline count without a second worktree.
  - The baseline here is **192** at v0.12.7.

## 3. TBDs (for `10-DESIGN.md` §6 / the owner)

1. **BOSS PumpKing's brain does not use the ult.** His kit can cast it (the check proves it).
   He needs a rule:
   - open it when the player is within 8 m and his ring is full;
   - recast when the player is near its centre.
2. **The realm view belongs behind the battlefield** (TODO(eng) with the art).
   - The view inside the prison replaces the *background*. Today the placeholder is drawn over
     the fight, translucent.
   - A kit cannot reach the arena's tree. The arena needs a "behind the battlefield" slot a kit
     can ask for by an event, like Māra's `GameManager.AddBackdrop`.
3. **Every TODO(art) in `PumpKing.gdd`** (the border, the warning, the cardboard houses, the
   vines, the pumpkins, the mouth, the blink and blast, the view from inside, the aim preview),
   plus animation clips and VFX/SFX rows for the `ult.*` cues (`Feedback.gdd` §6.6).
4. **Radius 3 m may feel small** for an ultimate. One number: give the prison its own radius
   instead of `ExplosionRadius`.
5. **The ult ring is not kept across a Tab switch.** This is pre-existing:
   - `UltCharge` lives on the body;
   - `ProtagonistState` does not save it;
   - a swapped-in body starts at 0.

   - Every fight's body is new too, so the ring starts empty in every fight: 1500 charge is
     1500 damage dealt, or 750 taken, inside one fight.

   With the first ult built, the owner will notice. Should the ring be per protagonist and
   persisted, like cooldowns? Should the rates be retuned?
6. **Items are not silenced** (decided here: they are not skills). The owner may want them to be.
7. **Pre-existing, not changed:**
   - **The crab's Jump** aims with `Gravity`, not `Gravity × GravityScale`, so under a gravity
     law it misses the player's height.
   - **Every headless boss check prints 16 "Command or Control autoremapping is enabled"
     engine errors.** They are present at v0.12.7 too.

## 4. Check counts

| Check | Before (v0.12.7) | After |
|---|---|---|
| Boss (`BossFightCheck`) | 192 | **259**, OK |
| Feedback | 251 | 251, OK |
| Flow | 37 | 37, OK (`FlowCheck.cs` not touched) |
| Zone 6 (boss brains' senses changed) | 115 | 115, OK |
| VFX (new cue names) | 1298 | 1298, OK |
| Audio (new cue names) | 1108 | 1108, OK |
| `dotnet build` | 0 warnings, 0 errors | 0 warnings, 0 errors |

The boss check now takes about 23 s (it simulates the prison probes too). The 16 "autoremapping"
engine errors it prints are there at v0.12.7 as well.

### 4a. The mutation run — every rule broken once

33 mutants, each built and run against the boss check. **All 33 are caught by name.** One
survived the first run (the recast twice): the probe's fault, not the rule's (§2, "A press made
while stunned is lost"). The probe was fixed and the mutant re-run.

| Rule broken | Caught by |
|---|---|
| no warning · a 3 s hold · no slow · no low jump · no silence · no sight cap | the timeline and "held" assertions, the crab's speed, the player's jump |
| silence also takes the BA · a fighter ignores silence · a foe ignores it | "the swing allowed" / "his head still flies" · Skill 1 / Pump Up refused · the level-6 crab's Jump |
| a foe ignores the cap · a blow aggros a blind foe · a blind foe keeps chasing (3 looks) · no fresh look when capped · the foe ignores `MoveSpeedScale` | the level-3 crab ("loses him at once", "struck or not") · the crab's speed |
| `BossSenses` ignores the cap | the boss's senses still find the player 6 m off |
| a pumpkin every second · harmless pumpkins | the count and spacing · the damage band |
| no blink · a blink into the floor · a weak blast · a blast that shoves | where he lands (body and cue) · the blast's damage and shove |
| no range clamp · the recast needs a full ring · the recast twice · a recast in the warning | the cast-range checks · the blink · "once a prison" · "refused in the warning" |
| never released · leaving does not free · holds far outside · holds its own side | "let go" · "runs out of it" · "a crab 12 m off" · "the other side only" |
| no veil · a soul at full speed · an empty ring casts · the ring not spent | the veil · "a soul flies" · "an empty ring" · "spend the whole ring" |

The runner script is not committed. It lives in the session scratchpad and is described here:
it does one exact string replacement per mutant, builds, runs the check, and restores the
file. Do not edit sources while it runs: it restores each file from its own copy.

## 5. Decisions made for the owner (all in `PumpKing.gdd`, decisions log)

- **The cast:**
  - **Cast range 8 m**, centre to centre: about a level head throw. A farther aim is pulled
    back along its line.
  - **Radius 3 m** (his head's blast; pumping does not grow it).
- **Who is held:**
  - **"Inside" = the blast's own test** (the hit circle touches the circle), checked every
    tick.
  - **The border is not a wall:** out frees, in catches.
  - The fallen, the invincible and his own side are never held.
- **What a prisoner loses:**
  - **Silence:** skills, not the basic attack. The seagull's poop is its basic attack. A skill
    under way runs its course; items are not skills.
  - **Slow:** walking, running, swimming, flying (Soul Form included), not a skill's own speed.
  - **Sight:** "one cell around you" = the 3 × 3 cells centred on the body = **3 m**. A blinded
    foe drops the player at once and is not told where a blow came from.
- **Pumpkins:**
  - eight per prison, one as it forms and every 0.5 s, each growing for 0.25 s first (a tell);
  - 10–25 damage by size, no shove;
  - flung outward from the centre at 9–13 m/s plus 4 up;
  - they can hit outside the prison and outlive it.
- **The recast:**
  - once, free, only after the warning (Q during the warning is refused, not queued);
  - a 0.4 s mouth windup, then the blink to the nearest spot his body fits;
  - **the blast: 3 m, 100 damage (2× the head's), no shove.** The shove would throw the
    prisoners out of the prison. It costs no head;
  - the blast does not end the prison;
  - a late recast is waited for.
- **Ownership:**
  - **one prison at a time;**
  - the prison outlives a Tab switch or his death (like his head); the recast needs him.
