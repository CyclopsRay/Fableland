# The owner's picks — one direction per world

**Picked 2026-11-28** in the gallery (https://claude.ai/artifact/BfrSTsTwjZypx4J7aMvW2b), saved
in its database (collection `picks`, document `owner`, version 17, 03:37 the owner's time) and
confirmed by the owner in chat (v0.12.14). The images are in `img/`; the directions and prompts
are in `README.md`; the reasoning behind each recommendation is in `REVIEW.md`.

**What a pick is.** The direction a world's arenas move to when its kit is repainted. **Nothing
shipped changes until then:** every kit and painter described in `RealmStyleBible.md` stays as
it is, and that document's realm sections still describe the shipped kits.

| World | Pick | Also liked | `REVIEW.md` recommended |
|---|---|---|---|
| **VK** — VanillaKindom · Beach | **VK-B** — mid-century gouache picture book | VK-C (torn-paper collage) | A, in Midjourney's handling (VK-A-mj2) |
| **SL** — Starland · Starfield | **SL-A** — flat minimalist poster | — | A — the same |
| **BM** — Banboo Maze · Wulin 武林 | **BM-A** — freehand ink 写意 | BM-C | A — the same |
| **HC** — HollowCastle · Dungeon | **HC-C** — dark fairy-tale gouache | — | C — the same |
| **TD** — TheDeserted · Ghost Town | **TD-C** — toy theatre | — | A (its runner-up was C) |
| **PL** — Palace of LOOING · Loong's Nest 龙宫 | **PL-A** — gongbi, fine line and heavy colour 工笔重彩 | — | A — the same (in Midjourney's sparer handling, mj3) |
| **KH** — Kingdom of Horror · Dark Wood | **KH-C** — silhouette shadow theatre | — | C — the same |
| **TF** — The Forgotten · Forgotten City | **TF-B** — lush modern pixel art | TF-A | A (B only as borrowed effects) |
| **CC** — Cloud Court | **CC-B** — embroidered silk | — | B — the same |
| **OW** — the overworld (the run map) | **OW-B** — torn storybook pages | — | B — the same; it is the view built in v0.11.4 |

The owner gave no reasons (the gallery's notes are empty). Seven picks match the review; VK,
TD and TF are the owner's own departures from it. The gallery's summary names each pick by its
direction and that direction's GPT Image 2.5 picture. Where the review preferred a Midjourney
variant's handling of the same direction (PL-A-mj3), only the direction is picked — the
handling is the art pass's to show and the owner's to approve.

## What the picks meet in the review's notes

Facts for whoever paints, from `REVIEW.md` ("Do the picks still make nine different books?"):

- **The paper family.** TD-C (toy theatre) and KH-C (shadow theatre) are both paper craft, and
  the overworld OW-B is paper too. The review advised at most one paper world besides the
  overworld, so that "different media" does not become "different scissors". Keep the two
  apart by what they cut: TD's flat painted façades on layered planes, KH's black silhouettes
  against light.
- **Two gouache worlds.** VK-B and HC-C are both opaque gouache. The style bible's rule is one
  medium per world (§0). The review's note on the Dungeon applies with more force now: HC-C
  stays thick, dark and free of outline, while the Beach's picture book stays sunlit and flat.
- **Density.** TF-B is one of the painterly GPT images the review found "filling every corner";
  a fighter must still read first (§2.6). The owner also likes TF-A, the review's pick — its
  strict pixels are the measure for how much TF-B may hold.
- **The Dark Wood's colour code carries into KH-C** (the review: backlight moonlight blue and
  violet, yellow and orange only where something gives light, red only for danger). The Black
  Prison's placeholder already draws in that code (`PumpKing.gdd`, TODO(art)).
- **Fine media at 32 px/m.** The review lists thread (CC-B), hatching, halftone and flying-white
  as media that blur at the game's 32 px per metre — the resolution question below.

## Still open — the picks do not decide these

From `REVIEW.md` ("What needs deciding besides the picks"), each gating a repaint:

1. **Grid and collision** — does collision stay on the 2 m grid while the art leaves it (the
   review's recommendation), or does the grid change?
2. **Who paints** — code painters, hand-made, or **AI-made sprites inside a kit** (allowed or not)?
3. **Resolution** — 32 or 64 px per metre.
4. **Fighters keep one house style** in every world.
5. **A light cap on every walkable top**, and nothing hanging into the fighting band.
6. **Order** — pilot one world end to end first. (The review proposed TD-A as the pilot because
   it is all procedural; with TD-C picked, the pilot is the owner's choice again.)

For the overworld (`OverworldStyle.md`): OW-B confirms the direction; whether it becomes the
default view, its fidelity, and the seat symbol's size are still open (HANDOFF §4).
