# Fableland — Production Department

Production's job in a one-person studio: make sure the *sequence* is right, the *scope*
is honest, and the *artifacts* (repo, docs, versions) stay trustworthy enough that any
future session — human or AI — can pick up the project cold and continue safely.

---

## 1. Operating model: departments as hats, docs as handoffs

Work moves in one direction per task: **Design → Data → Engineering → QA → (Art) →
Ship**, exactly the change workflow in `00-OVERVIEW.md` §4. The discipline that makes
this real solo:

- A hat only consumes **written** input. If you-the-engineer can't find the number in
  a GDD/table, the task bounces back to design (write it, even if it takes 2 minutes)
  rather than getting invented inline.
- A hat leaves **written** output: GDD deltas, table entries, KNOWLEDGE caveats,
  changelog lines. The repo is the studio; nothing lives only in your head, because
  "you" next month is effectively a new hire.
- Timebox hat-switches. Design sessions produce specs without opening the editor;
  engineering sessions implement without redesigning. When implementation reveals a
  design flaw, log it and make the *smallest* safe call (note it in the Decisions log),
  then revisit in a design session.

## 2. Source control & versioning

- **Branches:** feature work on branches like `prototype-0-playable`; `main` receives
  milestone-quality merges (PRs via `gh`). Never commit the GitHub PAT or bake it into
  a remote.
- **Every commit:** patch bump (+0.0.1) with the trio in sync — `Scripts/Foundation/GameVersion.cs`,
  root `VERSION`, HUD `VersionLabel` in `Scenes/Hud.tscn`. The on-screen stamp is the
  cheapest build-provenance tool we have; playtest notes always cite it.
- **Minor version = GDD milestone** (0.4 foes, 0.5 nodes, 0.6 items — already reserved).
- **Changelog** lives in `Migration.md` §0, newest first, written for a reader who
  didn't watch the work (what + why, not just files touched).
- Commit messages honest about verification level ("static checks only, no toolchain").

## 3. Milestones & acceptance criteria

A milestone is done when its acceptance list passes on two seeds (40-QA §5), not when
its code exists. Draft acceptance criteria **before** starting the milestone; store
them in the milestone's tracking note. Baselines:

- **v0.4.0 Foes** — Crab + Seagull with full FSM (patrol/sight/aggro/de-aggro),
  day-driven levels via `FoeStats`, evolution, spawn-on-death (no grandchildren),
  cap 6 respected by the spawner and ignored by spawn-on-death, loot hook firing,
  debug foe-level override + sight draw. Old `Enemy.cs` retired.
- **v0.5.0 Nodes** — Map ⇄ Arena scene flow through RunState; ≥1 mission type *fun*
  (Collection first: it's 60% of nodes), Finish-the-Day button rules per node type,
  full day-end resolution pipeline, shelter free actions + Rest/Sharpen, permadeath +
  boss-timer death + devour death, node completed/re-attempt semantics.
- **v0.6.0 Items** — Item instances with slots/backpack, both CD axes, all five tags
  enforced (incl. Eternal×Convertible validation), 3+ catalog items live end-to-end
  (suggest: FanChen's Heart [static combat], Pome's Bravery→Seed [convert], A Weird
  Mushroom [perish + RNG outcomes]), shelter build-mod UI.

Within a milestone, sequence by **risk first** (the thing most likely to invalidate
the design), then by **loop completeness** (playable end-to-end), then polish.

## 4. Scope control (the solo-dev survival section)

- The GDDs already flag the scope bombs; treat these as *separate milestones with
  their own acceptance lists*, never as line items: boss-as-protagonist (NODES §4.5
  scope note), Pixolotl day-rewind (undo semantics TBD — ITEMS §7.8), ?-node event
  content, plantation system, meta-progression.
- **Default answer to new ideas mid-milestone: TBD registry** (10-DESIGN §6), not the
  current sprint. The registry converts enthusiasm into backlog instead of scope creep.
- Cut scope by *depth*, not by *breaking the loop*: one great mission type beats five
  stubs; the prototype-0 precedent (playability over parity) is the house style.
- Watch dependency order: items (v0.6) reference shelters (v0.5) which reference foes
  (v0.4) — resist starting upstream work "because it's more fun" than finishing
  downstream acceptance.

## 5. Definition of done (any task)

- [ ] GDD reflects what was built (including decisions made during implementation)
- [ ] Numbers live in Balance tables, not literals; tier-3 values go through the
      modifier stack (30-DATA §1)
- [ ] Static checks pass; real build when toolchain exists; affected loop played
- [ ] Bugs fixed en route → KNOWLEDGE.md caveats
- [ ] Debug harness exists for the new system
- [ ] Version trio bumped, changelog line written
- [ ] TBDs created for anything punted

## 6. Session hygiene (for AI-assisted development)

This project is largely built in AI coding sessions; production owns making them safe:
- Session start: read `CLAUDE.md` → `KNOWLEDGE.md` → the GDD for the day's system.
- One milestone-task per session where possible; end sessions at committable states.
- Anything discovered-but-not-fixed goes into the TBD registry or a KNOWLEDGE caveat
  *within the session* — context evaporates when the session ends.
- The docs (not chat history) are the handoff. If a session made a decision, a file
  changed; if no file changed, the decision didn't happen.
