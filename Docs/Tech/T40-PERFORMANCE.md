# T40 — Performance, Lag & Optimization

The performance philosophy: **this game has no excuse to lag.** 2D, ≤6 foes, one
player, a static map scene. If it stutters, it's one of the specific traps below —
almost always allocation churn, unpooled projectiles, or accidental per-frame work.
Optimize by measurement against budgets, never by folklore.

---

## 1. Budgets (design-time contract, not aspiration)

| Thing | Budget |
|-------|--------|
| Frame (60 fps floor on modest hardware) | 16.6 ms; gameplay logic target < 4 ms |
| Physics tick | 60 Hz default; all combat logic in `_PhysicsProcess` fits alongside |
| Live projectiles | Pomegraknight seeds 24 + Pixolotl bubbles 18 + poops ≈ **~64 cap**, enforced, pooled |
| Foes | 6 (design cap) + spawn-on-death bursts ⇒ engineer for **12** |
| GC | zero steady-state allocations in combat (§2); a GC pause *is* a dropped input in a fighter |
| Scene swap (Map ⇄ Arena) | < 200 ms perceived; mask with a fade if over |
| Map redraw | only on state change / camera move — never unconditional per-frame `QueueRedraw` |

## 2. C# / GC discipline (the #1 lag source in Godot C#)

Steady-state combat code (`_Process`/`_PhysicsProcess` and anything they call) must
not allocate. The usual offenders and their fixes:

- **LINQ and `foreach` over Godot collections** in hot paths → plain `for` loops;
  cache `GetChildren()` results when iterating repeatedly.
- **Closures/lambdas** created per frame (e.g. subscribing inline, `Tween` callbacks
  in loops) → hoist to fields/methods.
- **String work per frame** (`$"HP {hp}"` every tick for HUD) → update labels only on
  the change events (already the event-driven pattern — keep HUD dumb and reactive).
- **`new` of Lists/arrays per frame** (hit registries, overlap results) → preallocate
  and `Clear()`; per-swing `HashSet<int>` hit registries are fine *if reused*, not
  re-newed each swing.
- **Boxing:** avoid `object`-typed variants crossing into Godot APIs in hot loops;
  watch `Godot.Collections.*` (marshalling cost) — prefer plain C# collections
  internally, converting only at the Godot boundary.
- **Physics queries** allocate result arrays → use the `ShapeCast2D`/direct-space
  queries with reused parameter objects, or accept the allocation but only on *timer*
  cadence (foe sight checks at 1–2 s are the house pattern — never per-frame vision).

## 3. Object pooling (mandatory for projectiles & popups)

`QueueFree` + instantiate per shot is fine at 1/s, deadly at seed-eruption rates.
One generic `Pool<T>` (stack of deactivated instances; `Rent()` re-Inits, `Return()`
hides + resets):

- Pool: Pome seeds (24/burst), Pixolotl bubbles (18 live), damage-number popups
  (biggest churner in the game — every hit), ghost afterimages (8 + fade-outs),
  poops, explosion VFX, baby crabs (spawn-on-death bursts).
- Reset discipline: `Return()` must clear velocity, statuses, event subscriptions,
  and per-instance registries — a pool that leaks state produces the spookiest bugs
  in the genre ("my second Blush tornado inherited the first one's hit list").
  Write the reset as a single `ResetForPool()` per pooled type, reviewed against its
  field list.
- Warm pools at arena load (spawn+return N instances) so first combat has no
  instantiation hitch.

## 4. Known project-specific traps

- **Event-subscription leaks across scene reloads** (T10 §1). `ReloadCurrentScene` on
  R-restart + autoload singletons + C# events = disposed-object crashes and phantom
  work. Unsubscribe in `_ExitTree`, always. This is a perf *and* correctness rule —
  leaked handlers keep whole freed scenes reachable.
- **The atlas map:** `MapRenderModel.Build` precomputes all polygons once per
  generation — protect that property. Never move clipping/Voronoi math into `_Draw`.
  `_Draw` should be dumb submission of cached convex polygons; `QueueRedraw()` fires
  on pan/zoom/state change only. If map interaction ever stutters, suspect an
  unconditional redraw first.
- **`_Draw`-based text** (labels via `DrawString`) re-shapes text each draw — cache
  what's static, and keep node-count text out of the per-frame path.
- **Hazard tick storms:** hazards tick per-overlapping-body per 0.25 s — cheap. But a
  future "many lingering poops" scenario multiplies Area2D monitoring; prefer fewer,
  merged hazard areas, and disable `Monitoring` on dormant hazards.
- **SVG imports** are rasterized at import, not runtime — free. But texture *scale*
  abuse (huge SVG scaled down per-node) wastes VRAM; export placeholder SVGs at
  target pixel size.
- **Shader/material first-use hitch:** when real VFX arrive, pre-instantiate one of
  each particle material off-screen during scene load.

## 5. Scene-flow smoothness (Map ⇄ Arena)

- `PackedScene`s for the swap are `preload`-equivalent (load once at boot via a
  ScenePreloader autoload) — `ResourceLoader.Load` mid-click is a visible hitch.
- If arena construction grows heavy (tilemaps, pools, foe warmup), use
  `ResourceLoader.LoadThreadedRequest` for the *resources* and keep node
  instantiation on main thread across ≤2 frames behind a fade.
- Save writes (day-end snapshot) are small JSON — still, write via a deferred call
  after the transition, not inside the button handler.

## 6. Determinism × performance

- Never "optimize" seeded generation with parallelism, caching keyed on wall-clock,
  or early-outs that consume different amounts of randomness per code path. Any
  change to `MapGenerator` must preserve *the count and order of Rng draws* or it's
  a map-breaking change (document it as such and bump minor).
- Sub-seed per subsystem (`seed+"R"` pattern) exists precisely so one system's draw
  count can change without shifting others — new consumers always take a fresh
  sub-seed, never share an existing stream.

## 7. Profiling workflow (with and without tools)

- **With editor:** Godot profiler (script time, physics), monitor tab (draw calls,
  objects, orphan nodes — orphans are leak evidence), `--gpu-profile` if render-bound.
- **Without toolchain (this host):** instrument suspects with
  `Time.GetTicksUsec()` pairs behind `Dev.Enabled`, accumulate min/avg/max per label,
  dump to `user://perf.log` on exit; plus an on-screen frame-time graph in the debug
  overlay (cheap: last 120 frame deltas as a line). Ship these tools in the debug HUD
  early — you can't fix what you can't see on the machine that stutters.
- Rule for optimization PRs: the commit message quotes numbers before/after from one
  of the above. No numbers, no merge — "feels faster" is how regressions ship.

## 8. When performance and clarity fight

Default to clarity everywhere except inside the §1 budgets' hot paths (combat tick,
projectile update, `_Draw`). A micro-optimized map generator is wasted effort (runs
once per run); an allocating bubble updater is a real defect (runs 18×60/s). Optimize
the loop, not the setup.
