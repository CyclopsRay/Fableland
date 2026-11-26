# R2 — VFX & game-feel research digest

Input for the VFX GDD, the hit-feedback GDD and tonight's implementation. Written 2026-11-25
against Fableland v0.11.0 (Godot 4.7 .NET, Mobile rendering method).

> **Research-method notice — read this first.**
> `WebSearch` and `WebFetch` were **unavailable**: every call failed with a model-routing error,
> and no search engine answered a plain request (they returned bot challenges, which were not
> circumvented). As a substitute, **ten known primary-source pages were read directly over
> plain HTTP** (twelve requests — the budget the coordinator set). Everything read that way is marked **[verified: key]** with the URL in §6.1.
> **Everything else is from the model's own knowledge and is marked [from memory]**, with a
> confidence word. No search was possible, so several sources the brief names (Sakurai's video,
> Vlambeer, Dead Cells, Hades, Monster Hunter, God of War, Riot, Unreal, Feel) are memory-only.
> §7 lists what could not be verified. Godot facts were read from the `stable` branch of the
> official docs repository on the day of writing; which engine version that branch tracked was
> not checked — confirm anything load-bearing in the 4.7 editor's built-in class reference.

Reading conventions: frames are 60 Hz frames (1 f = 16.7 ms). **Adopt** = use as is, **Adapt** = use
the idea with different numbers, **Skip** = not for Fableland (now). Numbers in §4 are **starting
values to be tuned by eye** (`50-ART-AUDIO.md` §6), not canon.

---

## 1. Verdict in ten lines

1. Build **two small table-driven presentation systems fed by one combat event**: a
   `FeedbackDirector` (hit-stop, shake, flash, zoom, rumble — seven tiers) and an `FxDirector`
   (cue id × style id → pooled recipe). Gameplay raises the event; neither system is queried by it.
2. **Hit-stop is per entity, never `Engine.TimeScale`**: a body-freeze gate that is a *sibling* of
   `LocalTime` (not `LocalTimeRate` itself — §5.3), counted in world delta, applied to attacker and
   victim only; a projectile's owner is never frozen. 33–180 ms, linear in damage (Smash-shaped).
3. **Shake** keeps trauma² but gains per-tier caps, smooth noise instead of white noise, a
   directional kick, optional small rotation and a user scale. Today every hit adds the same 0.35.
4. **Flash** = a 2–5 frame silhouette flash through one tiny `canvas_item` shader, colour chosen
   against the arena's canvas; blinking stays reserved for i-frames. A hard limiter keeps large
   flashes ≤ 3 per second and never saturated red.
5. **Effects** are `CPUParticles2D` one-shot pools (≤ 32 particles each) plus `_Draw`/`Line2D`
   strokes for the marks that make a medium; `GPUParticles2D` only for ambient and loops.
6. **Style follows the owner of the effect** (a hero's home world, a foe's world, the realm for
   dust/ambient/hazards): ten style rows, each fixing shape, palette, timing, texture and spark.
7. **Anti-repetition** = variant shuffle-bag + jitter ranges + context variants + combo
   escalation + rare variants — and restraint: small hits get almost nothing so big ones land.
8. **Budgets**: 512 live particles, 32 burst emitters, 6 new effects per frame, three priorities
   (never-cull / confirm / cosmetic), three quality tiers.
9. **State and pace are data**: every cue carries modifiers (burning, low HP, combo, boss phase,
   fight pace) so effects and hit-stop can change without code.
10. **Upgrade path**: runtime-generated textures → sheets baked by `Tools/realm_art` painters →
    authored flipbooks, all behind the same ids; then world-rate slow-mo, shared-emitter
    `emit_particle`, screen-space distortion, and audio cues on the same cue ids.

---

## 2. Findings

Source keys (URLs in §6.1): **V1** SmashWiki Hitlag · **V2** Celeste `Player.cs` (source published
by its developers) · **V3** Eiserloh GDC 2016 slides · **V4** KidsCanCode screen-shake recipe ·
**V5** WCAG 2.3.1 Understanding · **V6** Xbox Accessibility Guideline 118 · **V7** Godot
`GPUParticles2D` · **V8** Godot `CPUParticles2D` · **V9** Godot `Engine` · **V10** Godot `Camera2D`.
**Repo** = read in this repository. Sources are paraphrased throughout; formulas, numbers and
API names are given exactly as read.

### A1. Hit-stop (frame freeze / hitlag)

| # | Who shipped it | How it works / numbers | Source | Fableland |
|---|---|---|---|---|
| A1.1 | *Super Smash Bros.* series (HAL / Sora / Bandai Namco, 1999–2018) | Hitlag frames from damage `d`: **Melee ⌊d/3 + 3⌋**; **Brawl & Smash 4 ⌊(0.3846·d + 5)·h⌋**; **Ultimate ⌊(0.65·d + 6)·h⌋** (`h` = per-hitbox multiplier, default 1; electric ×1.5; crouch-cancel ×0.67). Caps: **20 f in Melee, 30 f from Brawl on**. A 15 % hit = 8 f in Melee (133 ms), 10 f in Brawl/4 (167 ms), 15 f in Ultimate (250 ms). | [verified: V1] | **Adapt** — same shape (constant + slope × damage, cap), smaller numbers: Smash is a duel of deliberate hits; a roguelike repeats a jab thousands of times against many foes. §4.2 |
| A1.2 | Smash — who freezes | Attacker and victim are both frozen by the same formula (a few multipliers apply to the victim only): the attacker on the frame it connected, the victim on the first frame of its flinch, shaking in place. Only the object that dealt the damage is frozen: a projectile's shooter keeps moving, and the particle effects the hit spawned keep playing. Hitboxes stay active during the freeze. A hit with no damage gives no hitlag; a hit with no knockback gives the victim none. | [verified: V1] | **Adopt** all of it: both freeze, projectile owner exempt, effects keep running, zero-damage hits do nothing |
| A1.3 | Smash — multi-hit, crowds | Per-hitbox multipliers tame multi-hits and add weight: Marth's tipper 1.25×, non-tipper 0.7×; the linking hits of Cloud's Limit Cross Slash **0.3×**; 0× = none. **Ultimate shortens long hitlag as the player count rises**: nothing changes at 10 f or less, and at the 30 f cap the multiplier is 0.925 (3 players), 0.862 (4), 0.75 (8); projectile hits are exempt. Victims can still influence their launch during the freeze (SDI). | [verified: V1] | **Adopt**: a per-hit multiplier on `HitInfo`, 0.3× for linking hits, and a crowd/pace scale. Input stays live during the freeze |
| A1.4 | Smash — stated purpose | The wiki gives two functions: showing that an attack connected, and giving strong attacks more emphasis. It also notes the side effect: an attack that hits takes longer to finish than one that misses. | [verified: V1] | Design consequence: landing a combo slows it slightly (§4.3 rule 8) |
| A1.5 | *Celeste* (Maddy Makes Games, 2018) | A **0.05 s freeze (3 f)** when a dash begins — in one code path skipped unless the engine's time rate is above 0.25, i.e. no freeze while the game is already slowed. A **0.1 s freeze** on a launch event, together with the strongest rumble. | [verified: V2] | **Adopt** the scale (50–100 ms for a player-initiated beat) and the guard (no stop while slow-mo is running) |
| A1.6 | *Celeste* — what the freeze does | The freeze is engine-wide (the whole scene update is skipped), and a new request replaces a shorter pending one rather than adding to it. | [from memory — medium; the engine file was not read] | **Adapt**: take the *max*, never the sum (§4.3 rule 2); do **not** copy the global freeze |
| A1.7 | Masahiro Sakurai, *Creating Games* (YouTube, 2022–24), hit-stop episode | Shows Smash's hit stop: both sides stop, the victim vibrates in place, stronger hits stop longer, and the stop is trimmed when many fighters are on screen. I recall grounded victims shaking horizontally only. | [from memory — medium; title and details unverified] | **Adopt** victim shake during the stop (2–4 px, sprite only, horizontal when grounded) |
| A1.8 | 2D fighters (Arc System Works' *Guilty Gear*; Capcom's *Street Fighter*) | Hitstop is tied to attack strength rather than raw damage. Recalled *Guilty Gear* attack-level table: 11 / 12 / 13 / 14 / 15 f for levels 0–4. *Street Fighter*: light < medium < heavy; exact values not recalled reliably. | [from memory — medium for GG, low for SF] | **Adapt**: tiers named by strength with authored overrides; duel-length stops are too long for a horde game |
| A1.9 | *Hollow Knight* (Team Cherry, 2017) | The longest freeze is reserved for **the player being hit** (on the order of a quarter second, with heavy shake); ordinary nail hits get recoil and a white flash rather than a long stop. Implemented with a global time scale. | [from memory — low on the number] | **Adapt**: the player being hit is the strongest non-kill beat, but ≤ 150 ms here (i-frames already last 0.8 s) |
| A1.10 | *Dead Cells* (Motion Twin, 2018); Sébastien Bénard's public `gameBase` template | The template exposes helpers named `stopFrame()` and `addSlowMo(...)`: short stops for impacts, brief slow-mo for big moments. | [from memory — medium; numbers not recalled] | **Adopt** the split: tiny stops are routine, slow-mo is rare |
| A1.11 | *Hades* (Supergiant, 2020); *Monster Hunter* (Capcom); *God of War* (SIE Santa Monica, 2018) | Hades: slow-mo on the last kill of an encounter, a dramatic freeze on boss kills. Monster Hunter: hitstop grows with the weapon's weight and the attack's motion value, mostly on the attacker's animation. God of War: hit pauses layered with camera shake and rumble. | [from memory — qualitative only, no numbers] | **Adopt** last-kill slow-mo and boss-kill freeze as the top tiers; per-weapon weight = our per-hit multiplier |
| A1.12 | Vlambeer, *The Art of Screenshake* (J. W. Nijman, 2013) | The *sleep* trick: the whole game pauses for a few tens of milliseconds when a shot connects. | [from memory — medium; 20 ms is the figure I recall, unverified] | Confirms that even a 1–2 frame stop reads |
| A1.13 | Why not a global time-scale freeze | Godot's `Engine.time_scale` scales `Timer`, `SceneTreeTimer` and every simulation that uses delta; the docs recommend keeping it above 0.0 because the game may otherwise misbehave; it does not change audio speed. Fableland's own law (`Gameplay.gdd` §A.2.3) already forbids it: it would also scale input and UI, and cannot make one body faster than its world. | [verified: V9] · Repo | **Skip** global freezes. Per-entity gate instead (§5.3) |

### A2. Screen shake

| # | Who shipped it | How it works / numbers | Source | Fableland |
|---|---|---|---|---|
| A2.1 | Squirrel Eiserloh, GDC 2016, *Juicing Your Cameras With Math* | Keep **trauma in [0,1]**; damage or stress **adds 0.2 or 0.5**; trauma **decreases linearly**; **shake = trauma² or trauma³** (the slide's example — trauma .30 / .60 / .90 giving 3 % / 22 % / 73 % shake — is the cube). `angle = maxAngle·shake·rand(-1,1)` and `offset = maxOffset·shake·rand(-1,1)` per axis. | [verified: V3] | **Adopt** — already the model in `ShakeCamera2D` (squared, `MaxOffset` 30, `Decay` 3) |
| A2.2 | same | In 2D, his verdict is that rotation alone is weak, translation alone is good, and the two together are best. (In 3D: rotation only.) | [verified: V3] | **Adapt**: add a *small* roll (≤ 0.015 rad) for heavy tiers only; needs `ignore_rotation = false` (B9.14) |
| A2.3 | same | Use smoothed (Perlin-type) noise instead of a fresh random value each frame. His four reasons: it feels better, it keeps working under pause and slow motion, its frequency is adjustable, and it is easier to reproduce in a replay. One noise seed per channel. | [verified: V3] | **Adopt** — today's shake is `GD.Randf()` per frame (Repo): frame-rate dependent and harsh |
| A2.4 | same | "Camera shake is like salt." — Squirrel Eiserloh, GDC 2016 | [verified: V3] | Budget it by tier (A5) |
| A2.5 | KidsCanCode Godot recipe (follows Eiserloh) | Example values: `decay 0.8`, `max_offset (100, 75)`, `max_roll 0.1` rad (flagged to be used sparingly), `trauma_power` 2 or 3; noise sampled along a moving coordinate. The page warns that misused shake is something players hate. | [verified: V4] | Values are for a looser game; ours stay near 30 px. **Trap:** the page's code is Godot 3 syntax (§5.1) |
| A2.6 | *Celeste* | Directional shake, not only noise: a 0.2 s directional shake along the dash direction, a 0.1 s one on a bounce, a general shake on death. | [verified: V2] (call sites; magnitudes not read) | **Adopt** a directional kick along the knockback vector |
| A2.7 | Vlambeer | Camera kick opposite to the shot plus generic shake, look-ahead toward the aim, camera lerp. | [from memory — high] | Kick: adopt. Look-ahead: defer |
| A2.8 | Fableland today | Every foe hit adds trauma 0.35 (≈ 3.7 px peak); a player hit 0.5 or 0.3; the head explosion 0.35; the tsunami has its own sustained channel. | Repo: `BaseFoe.TakeHit`, `CharacterController.TakeHit`, `PumpKingHead`, `ArenaEnvironmentController` | No dynamic range: a jab and an explosion shake alike. §4.2 |

Shake fatigue and caps: no primary source with numbers was found. Design position (ours):
per-tier trauma caps so light hits can never accumulate past a ceiling, one trauma add per
*event* (not per victim), and a user slider.

### A3. Flash, hit tint and photosensitivity

| # | Who | Rule / numbers | Source | Fableland |
|---|---|---|---|---|
| A3.1 | W3C, WCAG 2.3.1 (Level A) | Nothing may flash more than **three times in any one-second period** unless it is below the thresholds. A *general flash* is a pair of opposing changes in relative luminance of **10 % or more** (with the darker state below 0.80); a *red flash* is any such pair involving a **saturated red** (`R/(R+G+B) ≥ 0.8`). Passing by size: combined flashing area no more than **25 % of any 10° visual field**, the field being estimated as a **341 × 256 px** block on a 1024 × 768 screen — by our arithmetic about 148 × 148 px, ≈ 2.8 % of that screen. Contiguous flashes are added together. | [verified: V5] | **Adopt** as the limiter's definition (§4.4) |
| A3.2 | Microsoft, Xbox Accessibility Guideline 118 | A flash is a 10 % luminance change; it fails when there are more than about **three per second** over about **20 % or more of the screen**; red flashes are stricter; high-contrast alternating bands over about 20 % of the screen fail too. The guideline prefers removing risky content to warning about it, says every game should be tested whether or not it flashes on purpose (it points to the Harding FPA test), and puts the affected population near 1 in 4,000. | [verified: V6] | **Adopt**: make the flashes safe by default; a setting is the second line, not the first |
| A3.3 | **The two sources disagree on area** | WCAG's size test (≈ 2.8 % of a 1024 × 768 screen) is far stricter than XAG's roughly 20 %. | [verified: V5, V6] | Use the stricter: any flash over **2.5 % of the viewport** counts as large |
| A3.4 | Arcade / console convention | A hit victim turns solid white (or a palette-swap colour) for 1–3 frames; i-frames are shown by blinking the sprite. Vlambeer lists the white hit frame among its basics. | [from memory — high; no numbers sourced] | **Adopt**: flash means hit, blink means cannot be hit. Foes blink today although they have no i-frames (Repo: `BaseFoe.TakeHit` sets a 0.24 s blink) — drop the foe blink |
| A3.5 | 2D shader approach | Mix the sprite's colour toward a flash colour by a uniform in a `canvas_item` shader. `Modulate` only multiplies, so it cannot whiten a sprite — which is why today's flash is a pink tint (Repo: `CharacterController`, hit-flash timer → `Modulate`). | [from memory — high on the idea, medium on Godot 4's `COLOR` semantics; §5.2] | **Adopt** |
| A3.6 | Settings shipped | *Celeste*: a photosensitive mode and screen shake at off / 50 % / 100 %. *Vampire Survivors*: toggles for flashing VFX, screen shake and damage numbers. *Hades*: a screen-shake toggle. | [from memory — medium] | §4.4 |

### A4. The wider toolbox

| # | Technique — who | Numbers | Source | Fableland |
|---|---|---|---|---|
| A4.1 | Squash & stretch — *Celeste* | Jump: sprite scale **(0.6, 1.4)**. Landing: scaled by fall speed up to **X 1.6, Y 0.4**. Duck (1.4, 0.6); stand up (0.8, 1.2); bounces (0.5, 1.5) and (1.5, 0.5). | [verified: V2] | **Adapt**, milder for illustrated chibi sprites: jump (0.9, 1.12), land up to (1.2, 0.8), hit recoil (1.12, 0.9) for 2–3 f. Scale the Sprite, pivot at the feet |
| A4.2 | Movement dust — *Celeste* | Dust bursts of **4** particles on a jump or wall jump, **8** on a dash and on a fast landing, **1 every 0.02 s** while skidding. | [verified: V2] | **Adopt** the counts: 3–4 jump, 4–8 land (by speed), 6–8 dash, 2 turn |
| A4.3 | Rumble — *Celeste* | A small vocabulary: strength Light / Medium / Strong (+ Climb) × length Short / Medium. A dash start is Strong + Medium. | [verified: V2] | **Adopt** a 3 × 2 vocabulary keyed by tier; implementation deferred (keyboard-first) |
| A4.4 | Distortion burst — *Celeste* | A displacement (screen-warp) burst is added at the player's centre on a dash and on a launch. Parameter meanings were not checked. | [verified: V2] (existence only) | **Skip tonight** (screen-reading shader, B9.19); fake with an expanding ring |
| A4.5 | Forgiveness timers — *Celeste* | `JumpGraceTime 0.1` s (coyote), `VarJumpTime 0.2`, `DashTime 0.15`, `DashCooldown 0.2`. | [verified: V2] | Fableland's coyote is 0.2 s (Repo). Relevant because hit-stop needs an input buffer (§5.3) |
| A4.6 | *Juice it or lose it* — Jonasson & Purho, 2012 | A Breakout clone made lively by layering tweens and easing, squash & stretch, wobble, particles, trails, colour flashes, screen shake and sound; each layer is cheap. | [from memory — high; talk not re-watched] | The method for tonight: land layers one at a time and look after each |
| A4.7 | Steve Swink, *Game Feel* (2008) | Feel = real-time control + simulated space + polish; polish effects sell the physical properties of objects (weight, hardness). | [from memory — high] | The per-hit multiplier is perceived weight |
| A4.8 | Kill / finish presentation — *Smash Ultimate*'s finishing zoom, *Hades*' last-kill slow-mo | The camera zooms on a decisive hit with brief slow motion; reserved for decisive moments. | [from memory — high on existence, no numbers] | **Adopt** for last-foe kill and boss kill only |
| A4.9 | Vlambeer's list | Bigger bullets, muzzle flash, impact effects, enemy knockback, **permanence** (shells and corpses stay), random spread, more bass, camera kick, sleep. | [from memory — high on the list] | Permanence → capped decal pool. Bass → audio is the missing 30 % (`50-ART-AUDIO.md` §7) |
| A4.10 | Damage numbers | Today: red/green, 18–44 pt for 20–100 damage, rising 46 px in 0.7 s, a new `Label` + `LabelSettings` + `Tween` per hit. | Repo: `DamageNumberManager` | Pool them (`T40` §3 calls them the biggest churner); merge multi-hit ticks into one counting number per victim within 0.35 s; cap 12 live |
| A4.11 | Low-HP vignette, combo escalation, time dilation on ultimates | Common genre conventions. | [from memory — generic; no source] | Vignette: static, HP-driven (A3). Escalation: §4.8 modifiers. Ult dilation: needs world rate — defer |

### A5. Over-juicing and budgeting by importance

| # | Finding | Source | Fableland |
|---|---|---|---|
| A5.1 | Smash builds a hierarchy into one formula: more damage → more hitlag, capped, and trimmed again as the screen gets busier. | [verified: V1] | The tier table *is* the budget (§4.2) |
| A5.2 | Eiserloh treats shake as seasoning (A2.4); the KidsCanCode recipe opens by warning that misused shake is disliked. | [verified: V3, V4] | Caps + slider |
| A5.3 | Kao (2020), *The effects of juiciness in an action RPG*: a large online study in which the **no-juice and extreme-juice versions both did worse** than the medium and high ones. | [from memory — medium; paper not re-read, figures not quoted] | Do not ship everything at maximum |
| A5.4 | Riot Games' League of Legends VFX style guide: gameplay clarity first; a value hierarchy in which the gameplay-relevant part of an effect is the brightest; an effect must show its true hit area; secondary detail kept low-contrast and short-lived. | [from memory — medium] | Same rule as `50-ART-AUDIO.md` §1 (telegraph and hitbox from one number). Priorities in §4.9 |
| A5.5 | Crowded fights (ours, not sourced) | Design position | One camera event per *attack*, a per-frame spawn cap, a flash for every victim (free) but particles only for the nearest few |

### B6. 2D VFX craft, and effects that belong to a medium

| # | Who | What they did | Source | Rule extracted |
|---|---|---|---|---|
| B6.1 | *Smash Ultimate* | Per-franchise effect layers (Mr. Game & Watch moves in held frames; pixel and voxel fighters keep blocky effects) **on top of one universal hit language** (the same hit-spark logic and the same hitlag for everyone). | [from memory — high qualitatively] | Two layers: a universal *feedback* layer (identical in every world) and a per-style *effects* layer. This is the core of §4 |
| B6.2 | *Okami* (Clover, 2006) | Sumi-e look: brush outlines of varying width, paper texture, effects drawn as calligraphic strokes and ink splashes. | [from memory — high] | A medium is its **mark**: change what a mark *is*, not its colour |
| B6.3 | *Cuphead* (Studio MDHR, 2017) | Animation and effects hand-drawn and inked, played at 24 fps while the game runs at 60; film grain and blur as a finishing pass. | [from memory — high] | **Stepped effect timing over smooth gameplay** sells a hand-made medium |
| B6.4 | *Gris* (Nomada, 2018); *Hollow Knight* | Watercolour bleeds and paper grain; hand-drawn high-contrast slashes over soft glows. | [from memory — medium] | Edge quality (wet edge vs hard edge) carries the medium |
| B6.5 | *Dead Cells*; *Celeste*; *Hyper Light Drifter* | Pixel effects on the pixel grid: square particles, few frames, limited palette. *Celeste* colour-codes state through hair colour. | [from memory — high] | Pixel style: snap to the grid, step the frame rate, fade by dither or palette step — never by alpha |
| B6.6 | *Tearaway* (Media Molecule, 2013); *Paper Mario* | Every effect is made of the world's material (paper strips, confetti), with stop-motion timing. | [from memory — medium] | Particles are *pieces of the medium* (chips, strokes, cuts), not generic sparks |
| B6.7 | *Guilty Gear Xrd* (Arc System Works, GDC 2015) | 3D posed frame by frame with no interpolation; effects built with drawn silhouettes rather than simulated particles. | [from memory — medium] | Authored silhouettes beat simulated noise; hold frames |
| B6.8 | Anticipation → impact → dissipation | Impact is the shortest, brightest, largest phase (1–3 frames); dissipation lasts several times longer and loses value before shape. | [from memory — craft convention] | Applied in every spark recipe (§4.5) |

**Five axes define a style** — every row of §4.5 fixes all five: (1) shape language, (2) palette
and blend rule, (3) edge quality, (4) motion and frame stepping, (5) how a mark disappears.

### B7. Anti-repetition

| # | Mechanism | Who / where | Source | Fableland |
|---|---|---|---|---|
| B7.1 | Variant pools with no immediate repeat | Standard in game audio (random containers that avoid the last N); the same idea in VFX flipbooks with a random start frame | [from memory — high] | 3–5 shape variants per cue, shuffle-bag |
| B7.2 | Jitter on rotation, scale, lifetime, count, colour | Every particle system exposes random-between-two-values; Vlambeer: random spread | [from memory — high] | Jitter ranges are fields of the effect definition (§4.8) |
| B7.3 | Context variants | Engines map the surface or material hit to a different impact effect | [from memory — high] | Direction (along knockback), victim type, element, crit/kill, surface (realm ground, water) |
| B7.4 | Escalation and rarity | Combo ranks; rare flourishes | [from memory — generic] | Combo modifier (+1 step, capped at +3), 1-in-20 flourish, 1-in-200 easter egg |
| B7.5 | Restraint | Kao's result (A5.3); Smash's hierarchy (A5.1) | see A5 | T0 hits get nothing but a number: dynamic range is what stops fatigue |

### B8. Performance and architecture

| # | System | What to borrow | Source | Fableland |
|---|---|---|---|---|
| B8.1 | Unreal GameplayCues | Gameplay fires a **tag**; a manager routes it to a cosmetic handler; burst cues (one-shot, not instanced) versus looping cues (instanced, removed on end); a parameter struct carries magnitude, location, normal, instigator. Cosmetic only — may be dropped without changing the game. | [from memory — medium-high] | Vocabulary adopted: *cue id*, *burst vs loop*, *context struct*, cosmetic may be culled |
| B8.2 | Unity Feel / MMFeedbacks (More Mountains) | One player object lists feedbacks (shake, freeze frame, flash, particles, sound, haptics), each with delay, cooldown, chance and an intensity multiplier passed at play time; listeners sit on channels. | [from memory — medium] | A cue = a list of layers with intensity; a per-cue cooldown stops spam |
| B8.3 | Unreal Niagara scalability | Effect types carry budgets per quality level: max instances, max distance, and a *significance* rule deciding which instances to cull when over budget. | [from memory — medium] | Priority + budget + quality tier (§4.9) |
| B8.4 | Pooling | House rule already (`T40` §3): pool, warm at arena load, one `ResetForPool()`. | Repo | Every effect instance is pooled; nothing is created during combat |
| B8.5 | Particle budgets for 2D indie/mobile | **No authoritative public numbers were found.** General knowledge: the GPU cost is overdraw (big translucent quads); the CPU cost is draw calls and per-emitter overhead, not particle count as such. | [from memory — general] | Budgets in §4.9 are our own targets — measure with `T40` §7 |

### B9. Godot 4.x specifics

| # | Fact | Source | Consequence |
|---|---|---|---|
| B9.1 | `GPUParticles2D` with `one_shot`: setting `emitting` to true does not restart the cycle until every active particle has finished; and for a short while after `finished` it may still not restart, because the simulation runs on the GPU. The docs' remedy is to call `restart()` instead. | [verified: V7] | Pooled GPU emitters replay with `Restart()`, never `Emitting = true` |
| B9.2 | `finished` is emitted when all active particles are done, and never when `one_shot` is off. It exists on both node types. `CPUParticles2D` has the same `emitting` / `one_shot` rule but **no GPU-delay note**. | [verified: V7, V8] | Return-to-pool is driven by `finished`; CPU emitters replay predictably |
| B9.3 | `emitting` defaults to **true** on both; `amount` defaults to 8, `lifetime` to 1.0, `one_shot` to false, `local_coords` to false. | [verified: V7, V8] | Build pooled emitters with `Emitting = false`, or they fire once when added |
| B9.4 | Changing `amount` restarts a GPU particle system. `amount_ratio` avoids the restart but **saves nothing**: the full `amount` is still allocated and processed. | [verified: V7] | Quality tiers = separate low-count recipes, not a ratio knob |
| B9.5 | `fixed_fps`: GPU default **30**, CPU default **0**. It fixes the rate at which particles are *rendered*; the docs note it does not slow the simulation. `interpolate` (GPU, default true) smooths motion when `fixed_fps` is below the refresh rate. | [verified: V7, V8] | Stepped styles: `fixed_fps` 8–12 with `interpolate = false`. The look itself must be checked by eye |
| B9.6 | `speed_scale` 0 pauses a particle node (both types). | [verified: V7, V8] | A boss-kill freeze can pause effects; ordinary hit-stop must not (A1.2) |
| B9.7 | `visibility_rect` defaults to `Rect2(-100, -100, 200, 200)`: the region that must be on screen for the system to stay active; the docs say to grow it if particles pop in or out. | [verified: V7] | Size it per recipe; a 3 m explosion needs ± 110 px at least |
| B9.8 | With no `texture`, GPU particles are 1 × 1 px squares (CPU: squares). Flipbooks need a `CanvasItemMaterial` with `particles_animation` and its `particles_anim_h_frames` / `v_frames` / `loop`. | [verified: V7, V8] | Free pixel particles for the pixel style; the flipbook route is how authored sheets replace generated ones |
| B9.9 | `emit_particle(xform, velocity, color, custom, flags)` is supported on Forward+ and Mobile, **not Compatibility**. The default process material overwrites `color` and reads `custom` as (rotation, age, animation, lifetime). | [verified: V7] | A shared emitter is *possible* on our renderer; its behaviour with `emitting` off was not verified — upgrade path, not tonight |
| B9.10 | Trails use a mesh-skinning system (8 sections × 4 subdivisions by default); the node named as `sub_emitter` stops emitting on its own; 2D particles collide only with `LightOccluder2D`, not with physics bodies. Trails and sub-emitters are GPU-only as far as I recall. | [verified: V7] (last sentence from memory) | No bouncing sparks; trails only on a few hero projectiles |
| B9.11 | `preprocess` can be very expensive; the docs mention GPU crashes in extreme cases. | [verified: V7] | Never use a large `preprocess` to pre-warm |
| B9.12 | `CPUParticles2D` is the CPU-based emitter; the docs present `GPUParticles2D` as the hardware-accelerated equivalent that may not run on older devices. Its parameters (gravity, curves, colour ramp, fixed seed) live on the node itself. | [verified: V8] | No process material to share or pre-compile; configured from a table in code |
| B9.13 | `Engine.time_scale` — see A1.13. | [verified: V9] | — |
| B9.14 | `Camera2D.ignore_rotation` defaults to **true**: the rendered view ignores the node's rotation. | [verified: V10] | Rotational shake does nothing until it is set false |
| B9.15 | `Camera2D.offset` is documented for look-around and shake, and may push the view past the camera limits. `zoom`: higher values zoom in. | [verified: V10] | Shake and kick go on `Offset` (as today); punch-zoom = `Zoom` slightly above 1 |
| B9.16 | Zooming blurs or pixelates bitmap and non-MSDF dynamic fonts unless the text sits on a `CanvasLayer`. | [verified: V10] | Damage numbers are world-space `Label`s (Repo): keep punch-zoom ≤ 4 % or move numbers to a CanvasLayer |
| B9.17 | 2D batching reached the Forward+/Mobile renderers in 4.4; items batch only while texture, material and blend mode match. | [from memory — medium-high] | One atlas per style; avoid a unique material per particle node |
| B9.18 | Per-instance shader uniforms in `canvas_item` shaders (4.4+). | [from memory — medium] | If absent: one duplicated `ShaderMaterial` per fighter (≤ 13) is fine |
| B9.19 | Screen-reading shaders (`hint_screen_texture`), `BackBufferCopy`, `CanvasGroup`: each forces a copy of the screen (or a region) and breaks batching. | [from memory — medium; cost on Mobile unmeasured] | **Skip tonight**: shockwave, heat haze, black-hole lensing |
| B9.20 | `SceneTree.create_timer(time, process_always, process_in_physics, ignore_time_scale)`; `Tween` can ignore time scale (4.4+); `FastNoiseLite` replaced `OpenSimplexNoise`; `Input.start_joy_vibration(device, weak, strong, duration)`; `GradientTexture2D`, `ImageTexture.create_from_image`, `NoiseTexture2D` (generated asynchronously); `Line2D` with `width_curve`, `gradient`, texture modes. | [from memory — medium-high; signatures unverified] | Let the compiler confirm each signature; none is load-bearing for the design |

**CPU or GPU particles?** For bursts of ≤ 32 particles: **`CPUParticles2D`** — no GPU restart
latency (B9.1–2), no process-material first-use hitch (`T40` §4 already warns of it), parameters
set straight from a table (B9.12), and cost at these counts is negligible. **`GPUParticles2D`** for
arena ambience and sustained loops with larger counts, or where trails are wanted. This is a
judgement from the verified behaviours above, not a measured result — profile it (`T40` §7).

---

## 3. Requirements map

| Owner's requirement | Mechanisms that satisfy it |
|---|---|
| **a) Low resource use** | Pools warmed at arena load, nothing created in combat (B8.4) · CPU bursts ≤ 32 particles (B9) · one atlas per style, textures ≤ 64 px · hard budgets and a per-frame spawn cap with priority culling (§4.9) · no screen-reading shaders, no lights, no glow pass (§4.10) · trails only on ≤ 8 hero projectiles · flash is one uniform, not a node · feedback is arithmetic on timers — zero allocations (`T40` §2) |
| **b) No fatigue from repetition** | Seven tiers with real dynamic range; T0 hits get almost nothing (A5) · variant shuffle-bag, jitter ranges, context variants, combo escalation, rare flourishes (B7) · hit-stop and shake shrink as the fight gets busy (A1.3) · per-tier shake caps, shake used as seasoning (A2.4) · linking hits at 0.3× (A1.3) |
| **c) Matches the world it comes from** | Style id resolved from the **owner of the effect** (§4.5 rule) · ten style rows fixing five axes each (B6) · the universal feedback layer is identical everywhere so the game still reads the same (B6.1) · a contrast guard against the arena's canvas colour (§4.5) |
| **d) Editable, extensible, state-aware** | Cue id → definition table; recipes are data (`T20` §1–2) · per-cue state modifiers: burning, frozen, low HP, combo, boss phase, pace (§4.8) · tier numbers in one table, with a per-hit multiplier on `HitInfo` · textures and whole cues replaceable by id with authored files (§4.8) · the same cue ids later drive audio (`50-ART-AUDIO.md` §7) |

---

## 4. Recommended design for Fableland

### 4.1 Shape of the system

```
GAMEPLAY      TakeHit (pipeline stage 6–7, T10 §4)
                ├─ FeedbackTable.Resolve(hit, victim, flags) → FeedbackSpec     (pure data, DOMAIN)
                ├─ attacker.ApplyHitStop(spec.AttackerStop) / victim.ApplyHitStop(spec.VictimStop)
                └─ raise CombatEvents.HitLanded(in HitEvent)                    (C# event, T10 §1)
PRESENTATION  FeedbackDirector  ← HitLanded: shake, kick, flash, zoom, rumble, vignette, flash limiter
              FxDirector        ← HitLanded + other cues: resolve (cueId, styleId) → pooled recipe
              (later) AudioManager ← the same cue ids
```

- Hit-stop is the **only** feel element that changes the simulation, so gameplay applies it —
  with numbers from the same pure function presentation reads. One source of truth, no upward call.
- Everything else observes. Dropping an effect (budget, quality, the reduce-flashing setting) never
  changes the fight.
- Effect randomness uses its **own** random source, never a seeded gameplay stream (`T40` §6).

### 4.2 Feedback tier table (starting numbers)

Impact score **D** = post-mitigation damage × the hit's multiplier (default 1). For a
**player-side victim** use `D = 150 × damage / MaxHP`, so a hit is felt by what it costs.
Continuous form: **victim stop (ms) = clamp(20 + 1.6·D, 33, 180)**; **attacker stop = 0.7 × that,
melee only**. For scale: Smash Melee is 50 ms + 5.6 ms per damage-% and Ultimate 100 ms + 10.8 ms
[verified: V1]; with a Fableland hit worth roughly 2.5–3× a Smash percentage this sits a little
under Melee. In Smash both sides stop equally; the shorter attacker stop is our choice for
responsiveness — set them equal if the follow-through looks wrong.

| Tier | When (D) | Victim stop | Attacker stop (melee) | Shake: add / cap | Kick | Flash | Zoom | Slow-mo | Rumble weak/strong, time | Particles |
|---|---|---|---|---|---|---|---|---|---|---|
| **T0 graze / tick** | < 8, or flagged tick | 0 | 0 | 0 | 0 | tint pulse 1 f, no white | – | – | – | 0–2 |
| **T1 light** | 8–24 | 33–58 ms (≈ 3 f) | 23–40 ms (≈ 2 f) | +0.15 / 0.30 | 2 px | white 2 f (33 ms) | – | – | 0.2 / 0, 50 ms | 5–8 |
| **T2 medium** | 25–59 | 60–114 ms (≈ 5 f) | 42–80 ms (≈ 3 f) | +0.25 / 0.50 | 4 px | 3 f (50 ms) | – | – | 0.4 / 0.2, 80 ms | 8–12 |
| **T3 heavy** | 60–149 | 116–180 ms (≈ 8 f) | 81–126 ms (≈ 6 f) | +0.40 / 0.75 | 7 px | 4 f (67 ms) | 1.02× for 80 ms, back in 160 ms | – | 0.6 / 0.5, 120 ms | 12–18 + 1 stroke |
| **T4 ultimate / finisher** | ≥ 150 or authored | 180 ms (11 f) | 125 ms (7–8 f) | +0.55 / 0.90, roll ≤ 0.015 rad | 10 px | 5 f (83 ms) | 1.04× | deferred | 0.8 / 0.8, 200 ms | 18–28 + ring |
| **Kill** (added to the killing hit) | victim dies | holds its flash frame, then bursts | +33 ms, at least 67 ms in all | +0.10 | – | held until the burst | last foe: 1.05× | last foe: 0.35 s at 0.25× (needs world rate — deferred) | +100 ms | death burst 10–16 |
| **Boss kill** | boss dies | every fighter frozen 400–500 ms (effects keep running) | same | 1.0, then decay | – | **one** large flash at most, ≤ 100 ms | 1.10–1.15× toward the boss | 1.2–1.5 s easing 0.3× → 1× (deferred; freeze-only tonight) | 1.0 / 1.0, 400 ms | 30–40, staged over 0.6 s |

Shake notes: offset = trauma² × `MaxOffset` (30 px), so +0.15 alone is under a pixel and the
T1 cap gives 2.7 px; T3's cap gives 17 px. That is the intended dynamic range — the kick, not
the noise, carries small hits. Today's flat 0.35 was tuned up until it was visible, when shake was
the only signal (`KNOWLEDGE.md` v0.2.2); re-tune by eye once stop, flash and sparks exist.

Authored hints (suggested extension of `HitInfo`): a hit-stop multiplier and a small flag set.
Suggested starting multipliers: Pome slash 3 ×1.2 · Pangda's Tiger Kick ×1.5 · PumpKing's
explosion ×1.3 · linking hits (tornado ticks, black-hole ticks) ×0.3 · hazard ticks = tick flag.

### 4.3 Rules

1. **Who stops.** Melee: attacker and victim. Projectile, explosion, hazard: victim only — the
   owner is never frozen (A1.2). A projectile that persists through its target (bubble) does not stop.
2. **Stops never add.** A new request replaces a shorter remaining one: `remaining = max(remaining, new)`.
3. **Many victims, one attack.** Attacker stop = the largest single request. Shake = one add at
   the highest tier, +0.03 per extra victim (≤ +0.15). Every victim gets its own stop, flash
   and number (cheap); particle bursts obey the per-frame cap, nearest to the camera centre first,
   the rest one frame later or dropped.
4. **Multi-hit attacks.** The first hit of an attack instance on a victim gets its tier; later
   hits within 0.4 s are T0 with a 0.3× stop (A1.3). Attacker stop is budgeted to ≤ 120 ms in
   any 500 ms. Hits on one victim in the same frame are coalesced: one event, summed number,
   tier of the largest (+1 tier if three or more, capped at T2 for projectiles).
5. **Hazard and DoT ticks** (0.25 s cadence): T0 — no stop, no shake, tint pulse, merged number.
   The first tick after entering a hazard is T1, at most once per second per victim.
6. **Player being hit.** Tier from the HP-fraction score; stop at least 50 ms and at most
   150 ms; shake one tier above the table; kick *away* from the source; brief vignette pulse.
   The foe that hit also stops (melee). The existing 0.8 s i-frame blink stays.
7. **Player hitting.** Table as written. Foes flash and do not blink.
8. **Hit-stop and clocks.** A stopped body's motion, animation and attack timing freeze
   together. Following Smash this lengthens an attack that connects (A1.4): Pome's full combo
   gains ≈ 0.1 s (+9 %) when every swing lands. Accept and balance, or keep cooldown and reload
   clocks running during the stop — a design decision for the hit-feedback GDD.
9. **Input** is read in real time during a stop (`Gameplay.gdd` §A.2.3) and must be buffered for
   at least the longest stop (200 ms). Today a press reaches a kit as a one-frame edge
   (`CharacterControls`) and no time-window buffer was found — add one before shipping hit-stop.
10. **Pace scale.** Keep a moving average of hit events per second. Above 3 per second scale
    all stops down, to ×0.6 at 10 per second or more (Smash trims hitlag as players are added —
    A1.3). Boss phases and low HP may push the scale back up.
11. **No stop while slowed** (Celeste's guard, A1.5): skip T1–T2 stops during slow-mo or a
    boss-kill freeze.
12. **Effects keep running during a stop** (A1.2); only the bodies freeze. The victim's sprite
    shakes 2–4 px while stopped (A1.7).

### 4.4 Accessibility settings to ship

| Setting | Values | Effect |
|---|---|---|
| Screen shake | 0–100 % (default 100) | scales `MaxOffset`, kick and roll |
| Reduce flashing | off / on | flash becomes a 35 % tint; no large flash, no impact frames, no blink faster than 3 Hz |
| Hit-stop | on / reduced (×0.5) / off | scales every stop |
| Camera zoom & slow-motion | on / off | disables punch-zoom and slow-mo |
| Effects density | low / medium / high | quality tier (§4.9); also the performance switch |
| Damage numbers | on / off | — |
| Rumble | 0–100 % | when implemented |
| Low-HP vignette | on / off | — |

Rules that hold **whatever the settings** (A3): a *large flash* is any ≥ 10 % luminance change
over more than 2.5 % of the viewport; the director logs them and turns a fourth within one
second into a soft tint; no large flash uses saturated red (`R/(R+G+B) ≥ 0.8`); the low-HP
vignette is static or pulses below 1 Hz with under 10 % luminance swing; when more than six
sprites would flash in one frame, the surplus get the tint (contiguous flashes add up — A3.1).
A fighter-sized flash (48 × 64 px, ≈ 0.4 % of a 1152 × 648 view) is far below the size
threshold; a 3 m explosion disc (≈ 29,000 px², ≈ 3.9 %) is not — it counts.

### 4.5 Per-world VFX style table

**Whose style?** The effect belongs to whoever caused it: a protagonist's effects use their
home world's style in every arena; a foe's use its world's; dust, splashes, ambience and
hazards use the realm's. The universal feedback layer (§4.2) is the same everywhere.
**Contrast guard** (`RealmStyleBible.md` §2.6 — fighters read first): on light canvases
(Wulin `#E4E9E8`, Ghost Town `#F1E3C4`) marks and flashes are **dark**; on dark canvases they are
**light** and may be additive; the beach is mid-tone, so marks carry their outline. Each style
therefore defines its mark colour twice (on light / on dark). Palette values are from
`RealmStyleBible.md`; the recipes are this document's proposals.

| World · medium | Shape language | Palette / blend rule | Motion and timing | How a mark ends |
|---|---|---|---|---|
| **VK Beach** · storybook watercolour | Round blobs with a darker wet rim, teardrops, puffy sand clouds; dark-brown ink on the key shape only | Translucent washes (α 0.5–0.8), cream highlights, never pure white or black; normal blend | Fast bloom (80 ms), then a slow bleed; smooth, 30 fps is enough | Spreads 5–10 % while fading; a faint stain for big ones |
| **SL Starfield** · flat poster | 4-point stars, diamonds, triangles, hairlines, flat rings; two-tone shapes split by a hard terminator; no outlines | 2–3 flat colours: gold `#FBE08A` / `#D9A441`, teal `#35B3A8`, star white `#FFF8E0`, plum `#5B3A63`; no gradients | Few, large, geometric; linear scale and rotation; **3–5 pieces at most** (the bible forbids small clutter) | Shrinks to nothing or steps alpha in three bands — no soft fade |
| **BM Wulin** · freehand ink | Brush strokes that swell and taper, flying-white tails, splats with satellite dots; mist is bare paper | Five ink dilutions `#14171A` → `#BEC6C8`, indigo-cyan, ink green; **no warm hue**; dark on paper | Stroke *writes on* in 3–4 held frames (12–15 fps), then holds | Bleeds: edge grows, alpha drops over 300–400 ms |
| **HC Dungeon** · lit realism | Velocity-stretched sparks, embers, soft smoke, stone chips, glow | Fire ramp `#C22A06` → `#F26A0E` → `#FFB02E` → `#FFF2B0` for anything hot; cold glint `#C9D6E6` for steel; additive for light, alpha for smoke | Continuous, physical: gravity, drag; 60 fps, interpolated | Cools along the ramp, then fades; smoke lingers 0.5 s |
| **TD Ghost Town** · linocut | Carved wedges, jagged starbursts, broken rings; dust as *hatched* puffs | Key black `#1F1A17` + **one** spot ink (rust `#B5482E`, ochre `#D9A13B` or teal `#5E8C87`) printed 1–2 px out of register | Stamped at 8–12 fps, hard pops, ± 1 px wobble per frame | Breaks up by ink-skip speckle; never an alpha fade |
| **PL Loong's Nest** · gongbi | Even thin-line rings, outlined bubbles, scrolls, petals, pearls; closed and symmetric | Azurite `#2C63CC` / `#7FB0F5`, purple `#5B3FA8`, cyan `#33C7D6`, pearl `#EAFBFF`, line `#05060F`; graded fills; no warm hue | Slow, viscous, ease-in-out; particles decelerate, then rise (buoyancy) | Ring thins at constant width and vanishes; bubbles drift up and pop |
| **KH Dark Wood** · scratchboard | Thin scratch lines only: radiating hatches, cross-hatch, nervous curves; no fills | The realm's code: moon blue `#3F6FD8` / `#9DBEFF` neutral, **yellow `#FFD23F` = gives light**, **red `#D8323A` = danger**; light on black | Scratch-in over 2–3 frames, line boil at 10–12 fps | Lines drop out one by one |
| **TF Forgotten City** · pixel art | Squares, 1-px lines, plus signs, pixel rings, 2×2 and 4×4 blocks | ≤ 4 colours per effect: neon cyan `#3DF6FF`, magenta `#FF3DBE`, yellow `#FFE14A`, green `#9BFF4A`, white; Pixolotl pink `#FF8FCF` | **12 fps steps**, whole-pixel positions, 90° rotation steps, integer scale, nearest filter | Dither dropout or palette step (white → cyan → dark → off) |
| **Cloud Court** · cloisonné | Closed enamel cells with a gold rim: petals, scales, ruyi curls; symmetric rosettes; gold filigree lines | One enamel per piece — lapis `#1E4FA3`, turquoise `#2CB5A8`, cinnabar `#C5382C`, jade `#74C095`, ivory — always inside gold `#E3B341`; no gradient across a cell | Crisp, ceremonial: pop with a small overshoot, slow rotation; a glint sweep instead of glow | Shatters into 3–4 chips that fall, or shrinks |
| **VOID / zone 6** · black | Erasure: black discs with a hairline white rim, white outline shards; **implosion** — pieces converge instead of scattering | Black, white, plus one desaturated accent of the acting character | Sudden cuts, held frames, the occasional dropped frame | Swallowed toward a point |

| World | Procedural texture (≤ 64 px) | Hit-spark recipe (T2) | Arena ambience (cosmetic priority) |
|---|---|---|---|
| **VK** | Noise-warped radial blob with a darker ring near its edge; a teardrop | 1 splat blob (scale 0.4 → 1.2) + 5–7 droplets along the hit, with gravity + 6 short ink ticks for 2 f | Sea-spray specks, drifting petals; ≤ 12 |
| **SL** | None — flat polygons drawn in code; optional hard 4-point star | 1 gold star pops at 130 % for a frame, turns 45° and shrinks in 150 ms + 2–3 hairline rays + a flat ring | Sparse star twinkles (hard on/off, slow), stardust motes; ≤ 8 |
| **BM** | 64 × 16 dry-brush strip (taper × noise threshold rising toward the tail); 32 px ink blot with satellites | Ink blot at contact (3 variants) + 2–3 flicked strokes + a bare-paper disc for 2 f | Falling bamboo leaves, mist puffs, ink motes; ≤ 12 |
| **HC** | Soft radial gradient; 2 × 8 streak; soft noise puff | 8–12 additive streak sparks in a cone, gravity + 1 glow flash (80 ms) + 2–3 dust puffs | Embers rising over lava, dust in light shafts; ≤ 24 |
| **TD** | 32 px 1-bit wedge burst with speckle holes; hatch strip | Black jagged stamp for 2 f with a rust echo offset (2, 1) px + 4–6 wedge chips turning in 45° steps | Hatched dust devils, 1-bit specks; ≤ 10 |
| **PL** | 24 px bubble: 1-px dark ring, graded inside, pearl highlight; scroll curl; petal | Thin ripple ring (250 ms) + 5–8 outlined bubbles drifting up; heavy: a 6-petal pearl flash | Rising bubble streams, pearl motes; ≤ 16 |
| **KH** | None — polylines from pre-generated variant sets; or 4 hatch patches | 10–14 short scratches in a cone + a cross-hatched X for 2 f; explosions switch to yellow | Fireflies (slow yellow blinks), fog scratches, falling leaves; ≤ 12 |
| **TF** | The built-in 1 × 1 px square (B9.8); 8 px plus sign; 16 px ring in 4 frames | 4-frame burst (block → plus → ring of 8 → 4 scattered) + 6 squares on eight fixed directions | Neon motes climbing glyph lines, swamp bubble pops; ≤ 12 |
| **CC** | 16 px enamel chip: flat fill, gold rim, one glint; 5 colours × 3 shapes in one small atlas | Gold-rimmed lotus rosette (2 f) → 6 enamel chips falling + a thin gold ring | Gold dust, ruyi wisps, petals; ≤ 12 |
| **VOID** | Disc and shard polygons, no texture | A black disc with a white rim eats the contact point for 2 f, then 6 shards converge inward | A few specks drifting toward the VOID; ≤ 6 |

The VOID row has no art bible behind it — it is a proposal and needs the owner's sign-off.

### 4.6 Protagonist signatures

Home styles: Pomegraknight → VK (her GDD). PumpKing → KH (his GDD). Pixolotl → TF (the style
bible calls it her home; her GDD lists no realm yet). **Two are undecided**: Cleopastar's
GDD says Palace of LOOING (gongbi), while her kit is Egyptian stars and the Starfield realm is
Egyptian dusk under stars — this digest assumes **SL flat poster**. Sifu Pangda's GDD says Gui Lin, which
has no world yet — this digest assumes **BM ink**. Both are one field in the style table.
Every shape below takes its size from the skill's own parameters (`50-ART-AUDIO.md` §1).

| Hero · style | Basic attack | Skills | Ultimate / state |
|---|---|---|---|
| **Pomegraknight** · watercolour | Pome Slash: a ruby wash crescent filling the real 4 m / 80° cone, dark-brown ink on its leading edge, 3 variants; stage 3 is a double crescent that flings seed-shaped juice drops. Hit: juice splat with a wet rim | **Blush**: two cheek washes bloom into a ring; loop of 6–8 rising flame teardrops (watercolour, not glow); ends in a steam puff. **Fire Tornado**: two wound ribbons (yellow core → red rim) over the 3 × 2 m box, a pulse ring per tick, ink speed-lines pushed outward. **Seed Eruption**: cream-and-ruby pop at the crown; seeds are stretched teardrops with no trails (24 of them); burning seeds carry an orange halo; seeds leave a juice stain (pooled decals) | No ultimate in her GDD. While burning: wash colour shifts to orange and every slash gains flame tongues — a state modifier, not a new effect |
| **Cleopastar** · flat poster (assumed) | Star Shot: a gold hairline from the ankh and a 4-point pop; the star is a two-tone flat star; every 2 m it stamps a shrinking diamond instead of a trail. Explosion: a flat disc cut by a star-shaped hole, five triangles fly off; ring exactly at 2 m | **Nut's Decree**: a constellation hairline from each star to the aim point for 150 ms, then flat speed-triangles. **Apep's Maw**: the star becomes a black disc with a teal/gold rim; the 6 m pull ring is a dashed rotating hairline; ≤ 12 diamonds spiral inward; the four ticks contract the ring in four hard steps; execute = the victim's silhouette cut to black with a star-hole pop | Ultimate deferred in her GDD. Black-hole lensing (art backlog) is a screen shader — fake it with inward rings |
| **Pixolotl** · pixel art | Branquias: a 3-frame gill puff of 2 × 2 pixels; each bubble a 2-px pixel ring, 64 px across, with one highlight cluster; a hit flashes the ring for one step and sheds 4 pixels; cracking = ring → 8 arcs → 8 pixels at 12 fps. No trails (18 live) | **Papel-picado ghosts**: marigold 1-bit silhouettes with perforations; fade by dither density, not alpha. **RANIBOBER**: scanline tear (rows shifted ± 2 px) on each rewind step; a dotted marigold route line consumed as she travels; arrival = an expanding **square** pixel outline, 3 × 3 m like the hitbox; **Trapped** = a small pixel banner over the victim. **SOBRECARGA**: cyan 1-bit afterimages every third step (pool of 6), 1-px cyan dashes | Ultimate TBD. Time-rate state drives the afterimage cadence |
| **PumpKing** · scratchboard | Fire Head: 3–4 curved scratch arcs trail the rolling head, re-drawn every 0.1 s; a bounce throws a small yellow scratch burst. **Explosion** — the roster's heaviest beat: a yellow radial scratch burst to exactly 3 m, a black core disc with a jagged bright rim for 2 f, 16–20 scratch lines thrown out | **Pump Up**: the head squashes and swells; a yellow hatch ring per pump; the shield is drawn as **one scratched arc per stack** (five = full); breaking scatters the arcs. **Soul Form**: the body scratches itself out bottom-up; the soul is an outline figure in pale blue with 3–4 wisp curls; lines fill back on exit | As BOSS: the danger code — his tells and head glow turn **red**; defeat = lines drop out until only the outline is left, then it falls apart |
| **Sifu Pangda** · freehand ink (assumed) | Fuhu Palm: one bold brush dab at the far edge of the 4 × 3 m box with a flying-white tail from his hand; an ink blot and a bare-paper disc on the victim; stun = ink dots circling. Falling Crane: two vertical dry-brush speed strokes; landing = a 4 m horizontal splash line with upward flicks and mist puffs | **Tiger Kick**: during the 1 s windup ink dots spiral in and dry-brush corners mark the true 6 × 4 m box; release = one sweeping stroke across it and a black/paper impact frame on the victim only. **Crane Flight**: a long dry-brush line behind the dash (one `Line2D`). **Drink**: the Chi shield is an **enso** — a brushed ring whose thickness is the shield and which grows drier as it decays. **Bottle Throw**: three dabs of trail, angular ink shards; **Trapped** = two brush rings at the feet | Ultimate deferred in his GDD. A 9 × 9 field: ink grid lines written stroke by stroke, a seal stamp on a triggered cell |

### 4.7 Foes, hazards, volumes, movement

| Subject | Effect idea |
|---|---|
| **Crab** (VK watercolour) | Contact: two short ink arcs snapping shut + a sand puff. Soft Shell: a hit from the weak side uses a cracked-shell variant (context variant). Jump: sand blobs at take-off and landing. Death: the body wash bursts into 5–6 droplets and shell chips; each baby crab emerges from a droplet |
| **Seagull** (VK watercolour) | Poop: cream blob with an ink outline; its splat decal is exactly the lingering hazard area. Dash tell: keep the red tint and add a dashed ink line along the real 15 m path. Dash: three stamped afterimages. Death: 5–7 feather teardrops swaying down |
| **Evolution** | A rising ring bloom in the foe's style; scale 1.0 → 1.2 → 1.1; tint held for 0.5 s. Never-cull priority: it is a gameplay tell |
| **Hazards** | In the realm's style: lava and torches (lit fire), will-o'-the-wisp (yellow scratch tongues), security beam (pixel flicker at 12 fps), frost pearl (ivory and turquoise chips), stakes/spikes/traps (a 2-frame snap mark). Ticks follow rule 5 |
| **Soft volumes** | Entering or leaving water: a ring and droplets in the realm's style (gongbi ripple, pixel splash, ink splash). Foliage: 3–5 leaf marks. Cloud: one puff. At most one burst per volume per 0.25 s |
| **Movement dust** (realm's style) | Jump 3–4, land 4–8 by fall speed, dash 6–8, turn 2 (Celeste's counts, A4.2). Under water: bubbles instead |
| **Status loops** | On fire: 6–8 particles in the style of whoever lit it. Frozen: tint plus 3–4 shard polygons, a shatter burst on thaw. Never-cull priority |
| **Telegraphs** | Fill at 15–25 % alpha + an outline in the owner's style, drawn from the hitbox numbers; hostile = the realm's danger colour; at least 0.25 s of warning for T2 and above |

### 4.8 Data model

Follows `T20`: a registry keyed by snake_case id, definitions as pure data, recipes as code.

```csharp
public enum FxPriority : byte { Critical, Confirm, Cosmetic }   // Critical is never culled
public enum FxKind : byte { Burst, Loop, Stroke, Decal, Trail, Overlay }

public sealed class FxDef                      // Scripts/Data — no Godot types
{
    public string Id { get; init; }            // "hit_spark", "move_land", "status_burning", "pome_slash_arc"
    public string Style { get; init; }         // "watercolour", "ink", … or "*" = fallback recipe
    public FxKind Kind { get; init; }
    public string Recipe { get; init; }        // builder: "cpu_burst", "draw_stroke", "ring", "stamp"
    public string[] Textures { get; init; }    // keys: an authored file if present, else the generator
    public int Variants { get; init; } = 3;    // shuffle-bag, never the last one twice
    public FloatRange Count, Scale, RotationDeg, Lifetime, Speed, HueShift;   // jitter
    public string[] PaletteKeys { get; init; } // resolved per style, on-light / on-dark
    public int FixedFps { get; init; }         // 0 smooth; 8–12 stepped
    public int PoolSize { get; init; } = 8;
    public FxPriority Priority { get; init; }
    public int MinQuality { get; init; }       // 0 low, 1 medium, 2 high
    public float CooldownSec { get; init; }    // per source: stops spam
    public float RareChance { get; init; }  public string RareId { get; init; }
    public FxModifier[] Modifiers { get; init; }
}
public readonly record struct FxModifier(string When, float CountMul, float ScaleMul,
    float SpeedMul, float LifeMul, string TintKey, string AddCue);   // When: "burning", "low_hp", "combo>=3", "boss_phase>=2", "pace>=0.7"

public struct FxContext    // passed by value; no allocation
{ public Vector2 Position, Direction; public float Magnitude; public string Style;
  public uint State; public int Combo; public float Pace; public Node2D Follow; /* + shape: radius / box / cone */ }
```

- **Lookup**: (id, style) → (id, `*`) → nothing, with one debug warning. Never throw: effects are cosmetic.
- **Replace by id**: a texture key resolves to `res://Assets/Vfx/<style>/<key>.png` when the file
  exists, else to the generator; a whole cue resolves to `…/<id>.tscn` when it exists. Same
  contract as tiles: keep the name and the footprint (`RealmStyleBible.md` §1).
- `FeedbackTable` is the same idea for §4.2: one row per tier, plus the per-hit hints.
- **Trap**: a new `float` field on the `HitInfo` struct defaults to **0, not 1** — store the
  multiplier as an *extra* (0 = ×1) or treat 0 as unset.

### 4.9 Budgets (our targets — no external numbers exist, B8.5)

| Budget | Value |
|---|---|
| Live particles, all emitters | 512 hard cap; ordinary fights should sit under 150 |
| One-shot burst emitters alive | 32 (each ≤ 32 particles) |
| Status loops / ambient emitters | 16 / 4 per arena |
| Stroke (`_Draw` / `Line2D`) effects alive | 24, of which ≤ 8 trails |
| Decals | 24 pooled, oldest recycled |
| New effect instances per frame | 6; surplus waits ≤ 2 frames, then is dropped by priority |
| Damage numbers alive | 12, pooled |
| Texture memory | each ≤ 64 × 64; one atlas ≤ 512 × 512 per style; < 8 MB in all |
| CPU | both directors under 0.5 ms a frame; zero steady-state allocations (`T40` §2) |

Priorities: **Critical** (telegraphs, status loops, evolution, the player's own hurt, kill
confirms) is never culled. **Confirm** (hit sparks on foes) is shortened or skipped over
budget. **Cosmetic** (dust, ambience, decals, trails, flourishes) goes first. Off-screen
Confirm and Cosmetic effects are not spawned. Quality: low = half-count recipes, no ambience,
decals or trails; medium = as written; high = denser ambience.

### 4.10 What to defer

| Deferred | Why |
|---|---|
| World-rate slow-mo (last kill, boss kill, ultimates) | Needs the planned `worldRate` wired through foes, projectiles, hazards and effects (§5.3). Tonight: freeze only |
| Screen-space distortion (shockwave, heat haze, black-hole lensing), `CanvasGroup` outlines, 2D lights, glow | Cost on the Mobile renderer unmeasured; not needed for any style row |
| Shared emitters through `emit_particle` | Supported on Mobile (B9.9), behaviour unverified — needs a spike |
| Rumble | Keyboard-first today; the vocabulary is in §4.2 |
| Sheets baked by `Tools/realm_art`, authored flipbooks | New PNGs need an editor import pass; the ids are ready for them |
| Trails on seeds, bubbles, poops | 40+ trails is the one thing in this design that could cost |
| Full-screen impact frames | Photosensitivity risk; victim-only frames are enough |
| Camera look-ahead, per-world number fonts | Not feedback-critical |
| **Audio** | Out of scope tonight, but it is the largest missing part of feel (`50-ART-AUDIO.md` §7). Give every cue an audio slot now |

---

## 5. Godot 4.7 implementation notes and traps

### 5.1 Verified against the docs this session

1. Replay a pooled one-shot `GPUParticles2D` with `Restart()`, never `Emitting = true` (B9.1).
2. Create pooled emitters with `Emitting = false`; the default is true (B9.3).
3. `finished` fires only for `one_shot` emitters; use it to return to the pool (B9.2).
4. Do not change `amount` on a live GPU emitter (it restarts); `amount_ratio` saves nothing (B9.4).
5. Grow `visibility_rect` per recipe; the default is ± 100 px (B9.7).
6. GPU particles default to `fixed_fps = 30` with interpolation; CPU particles default to 0 (B9.5).
7. `speed_scale = 0` pauses a particle node (B9.6) — not for ordinary hit-stop.
8. A null particle texture gives 1 × 1 px squares; flipbooks need a `CanvasItemMaterial` (B9.8).
9. `emit_particle` works on Mobile and Forward+, not Compatibility (B9.9) — a renderer change
   (a web export, say) would break anything built on it.
10. `Camera2D`: rotation is ignored until `IgnoreRotation = false`; `Offset` may pass the limits;
    zoom above 1 zooms in; world-space text blurs under zoom (B9.14–16).
11. `Engine.TimeScale`: scales timers and every delta; keep above 0; no effect on audio (A1.13).
12. The KidsCanCode recipe sits under a 4.x path but its code is Godot 3 (`export var`,
    `OpenSimplexNoise`, `rand_range`) — take the method, not the text (A2.5).

### 5.2 From memory — confirm in the editor before relying on it

- **Flash shader.** `shader_type canvas_item; uniform vec4 flash_color : source_color; uniform
  float flash : hint_range(0,1); void fragment() { COLOR.rgb = mix(COLOR.rgb, flash_color.rgb,
  flash); }` — this assumes `COLOR` in `fragment()` already holds texture × modulate, as I
  believe it does in Godot 4. Test: at `flash = 0` the sprite must look unchanged. Alpha is
  untouched, so the flash keeps the silhouette.
- **One material per fighter** unless per-instance uniforms are confirmed (B9.18): a shared
  `ShaderMaterial` makes every fighter flash together.
- `FastNoiseLite`'s default frequency is very low: raise it until the shake offset reverses
  about 15–25 times a second.
- Additive blend is `CanvasItemMaterial.BlendMode`; additive marks vanish on light canvases
  (Wulin, Ghost Town) — hence the contrast guard.
- `NoiseTexture2D` generates on a thread: wait for `changed`, or build an `Image` yourself.
  Generate every texture at arena load, never on the first hit.
- `Line2D` rebuilds its mesh whenever a point changes — fine for 8 trails, not for 40.
- `Tween` + lambda callbacks allocate (`T40` §2): drive pooled effects from a manual update
  with struct state. `DamageNumberManager` does this per hit today.
- Whether `Restart()` on a `CPUParticles2D` also sets `Emitting` was not verified — check once.
- First-use hitch: play each recipe once off-screen at arena load (`T40` §4).

### 5.3 Project-specific (read in the repo)

Cited by member name: several of these files were being edited by another session while this
was written, so line numbers would not hold.

- **Do not implement hit-stop by changing `LocalTimeRate`.** It is a `virtual` property that
  Pixolotl overrides; her bubbles read the owner's `GetActDelta` (`PixolotlBubble`) and the
  arena ticks the item runtime with the player's act delta (`GameManager`) — a stopped Pixolotl would freeze her bubbles in mid-air and pause
  item cooldowns. Use a separate body-freeze factor applied inside the body's own `_Process`
  and `_PhysicsProcess`, leaving `GetActDelta()` as it is.
- **Count the stop down in world delta, not in the frozen body's own delta**, or the body never
  wakes. World delta, not wall-clock time, so the headless checks that speed the engine up stay
  deterministic (`KNOWLEDGE.md`, v0.10.9 reference on measuring physics).
- `LocalTime.ClampRate` floors at 0.01: a zero rate through `LocalTime` is 1 % speed, not a freeze.
- **`BaseFoe` does not use `LocalTime` at all** (its `_PhysicsProcess` takes the raw delta), nor
  do projectiles and hazards. Victim stop on foes needs the same gate added there; a world
  rate needs it everywhere — which is why slow-mo is deferred.
- The victim's animation already freezes for the stun window while knockback moves it
  (`CharacterController._Process` zeroes the animation speed while stunned). Hit-stop adds what is missing: position hold (knockback
  starts when the stop ends), the attacker's freeze, sprite shake and the flash.
- Subscribe the directors to the combat event in `_EnterTree`, unsubscribe in `_ExitTree`
  (`T10` §1) — a static event plus `ReloadCurrentScene` is the project's known crash class.
- A Foe-side fighter built from a protagonist scene must not claim `ShakeCamera2D.Instance`
  (`KNOWLEDGE.md` v0.10.11) — route camera feedback through the director, not the body.
- Z order: actors 0, closeview layers 20 (`MapVisualBuilder.CloseviewZIndex`), numbers 100.
  Suggested: world effects 10 (hidden by foreground like the fighter), must-read effects 30.
- Pivot for squash and stretch is the feet: the Sprite's native size and the collision shape
  are independent (`50-ART-AUDIO.md` §1) — scale the Sprite only.
- Give the headless checks (`BossFightCheck`, `RunWalkCheck`, the content check) a switch that
  sets every stop to zero, so timing-based assertions do not move.

---

## 6. Source list

### 6.1 Read this session (plain HTTP, 2026-11-25)

| Key | URL | What it is |
|---|---|---|
| V1 | https://www.ssbwiki.com/Hitlag | SmashWiki: hitlag formulas per game, caps, multipliers, player-count scaling, who is frozen |
| V2 | https://raw.githubusercontent.com/NoelFB/Celeste/master/Source/Player/Player.cs | Celeste's player class as published by its developers: freezes, shakes, rumble, squash, dust counts, timers |
| V3 | http://www.mathforgameprogrammers.com/gdc2016/GDC2016_Eiserloh_Squirrel_JuicingYourCameras.pdf | Eiserloh's GDC 2016 slides: trauma model, translational vs rotational, noise vs random |
| V4 | https://kidscancode.org/godot_recipes/4.x/2d/screen_shake/ | Godot recipe implementing Eiserloh's model, with example values (Godot 3 syntax) |
| V5 | https://www.w3.org/WAI/WCAG22/Understanding/three-flashes-or-below-threshold.html | WCAG 2.3.1: three-flashes rule, flash definitions, area threshold |
| V6 | https://learn.microsoft.com/en-us/gaming/accessibility/xbox-accessibility-guidelines/118 | Xbox Accessibility Guideline 118: photosensitivity failure conditions and mitigations |
| V7 | https://raw.githubusercontent.com/godotengine/godot-docs/stable/classes/class_gpuparticles2d.rst | Godot class reference, `GPUParticles2D` (read twice) |
| V8 | https://raw.githubusercontent.com/godotengine/godot-docs/stable/classes/class_cpuparticles2d.rst | Godot class reference, `CPUParticles2D` (read twice) |
| V9 | https://raw.githubusercontent.com/godotengine/godot-docs/stable/classes/class_engine.rst | Godot class reference, `Engine.time_scale` |
| V10 | https://raw.githubusercontent.com/godotengine/godot-docs/stable/classes/class_camera2d.rst | Godot class reference, `Camera2D` offset, zoom, rotation |

### 6.2 Not read this session — cited from memory; check before quoting

| Reference | Used for |
|---|---|
| Masahiro Sakurai on Creating Games (YouTube channel), hit-stop episode | A1.7 |
| Jan Willem Nijman (Vlambeer), *The Art of Screenshake*, 2013 talk (YouTube) | A1.12, A2.7, A4.9 |
| Martin Jonasson & Petri Purho, *Juice it or lose it*, 2012 talk; code at github.com/grapefrukt/juicy-breakout | A4.6 |
| Steve Swink, *Game Feel* (2008) | A4.7 |
| Sébastien Bénard, github.com/deepnight/gameBase and talks on *Dead Cells* | A1.10 |
| Dustloop wiki, *Guilty Gear* attack-level tables | A1.8 |
| Dominic Kao, *The effects of juiciness in an action RPG*, Entertainment Computing 34 (2020) | A5.3 |
| Riot Games, League of Legends VFX style guide | A5.4, B6.8 |
| Junya C. Motomura, *GuiltyGearXrd's Art Style: The X Factor Between 2D and 3D*, GDC 2015 | B6.7 |
| Thomas Vasseur, art deep-dive on *Dead Cells*' 3D-to-pixel pipeline (Game Developer) | B6.5 |
| Unreal Engine GameplayCue documentation; tranek's GASDocumentation (GitHub) | B8.1 |
| More Mountains, Feel / MMFeedbacks documentation | B8.2 |
| Unreal Engine Niagara scalability and effect-type documentation | B8.3 |
| Godot docs: `Line2D`, `CanvasGroup`, `BackBufferCopy`, screen-reading shaders, `Tween`, `SceneTree`, `Input`, `FastNoiseLite`, `GradientTexture2D`, `NoiseTexture2D`; 4.4 release notes | B9.17–20, §5.2 |

---

## 7. What could not be verified

- **No web search at all**, so nothing was discovered — only pages whose address was already known were read.
- Sakurai's hit-stop video, Vlambeer's talk, *Juice it or lose it*: content recalled, not re-checked; no figures from them should be treated as exact.
- *Street Fighter*, *Guilty Gear*, *Hollow Knight*, *Dead Cells*, *Hades*, *Monster Hunter*, *God of War*: every number is a recollection or absent. The Hollow Knight quarter-second and the Guilty Gear 11–15 f table in particular.
- How Celeste's `Freeze` is implemented (global, max-not-sum): the engine file was not read; only the call sites were.
- Kao's study and Riot's style guide: described from memory, no figures quoted.
- Art-direction claims about *Okami*, *Cuphead*, *Gris*, *Tearaway*, *Guilty Gear Xrd* and the rest of B6.
- Unreal GameplayCues, Feel and Niagara: described from memory.
- Godot: 2D batching on the Mobile renderer, per-instance uniforms in 2D, the cost of screen-reading shaders, `COLOR` semantics in a `canvas_item` fragment shader, `Tween`/timer time-scale options, vibration and texture-generation signatures, `CPUParticles2D.Restart()` details, and `emit_particle` with `emitting` off.
- Which Godot version the docs `stable` branch tracked on the day (4.7 or later) was not checked.
- **No particle or draw-call budget for 2D games was found in any source**; §4.9 is this document's own target.
- Nothing here was run: no build, no in-engine look. Every number in §4 is a starting value.
- Three style assignments are assumptions awaiting the owner: Cleopastar (SL vs PL), Sifu Pangda (BM vs a future Gui Lin), and the whole VOID row.
