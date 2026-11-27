# Māra — art prompts (Mara.gdd §9, §13 step 3)

**Status: drafted in v0.12.3 by Opus; nothing generated yet.** The owner's rule is that
art-style work goes to Fable, so **Fable reviews and may rewrite these prompts before any
Tripo credit is spent.** The in-game art today is a placeholder drawn in code: `MaraFigure`
(the emanation), `WheelOfBecomingField` (the colossus and the wheel), `WheelRealmLight`,
`WheelIndicator`, `OfferingBowl`, `MaraStrike`, `ChainHook`, `MaraNoose`.

How to generate them (Docs/Instructions/50-ART-AUDIO.md, the Tripo memory notes):

- **Tool:** Tripo Studio web, GPT Image 2.5 (the model that made `Z6-AV-6`, `Z6-AV-F` and the
  Asura's sprite). Submit one image at a time.
- **Recording:** record every result in `Docs/Art/StyleExploration/zone6/manifest.json` with
  its id.
- **Sprites:** keyed off flat magenta exactly as the Asura's was. Follow KNOWLEDGE v0.11.4,
  "Keying an AI sprite sheet off flat magenta: three traps".
- **Facing:** drawn facing **left**, and mirrored in the scene (KNOWLEDGE v0.12.1).
- **Ground truth:** the medium is the one `Zone6.gdd` §7.2 chose: nagtang, gold and vermilion
  line on black. The cultural rules are those of `Mara.gdd` §9.5.

## The fix every wheel prompt carries

The concept sheets `Z6-AV-F` and `Z6-AV-6` asked for "five glowing sigils of the poisons on the
wheel's rim". The model filled two of the five with a **Dharma wheel** and an **endless knot**.
Both are auspicious symbols of the teaching and must never mark a poison (`Mara.gdd` §9.3).

Every prompt below therefore names each sigil exactly and adds "no other symbols on the
wheel". The forbidden symbols are not named in the prompt, because naming a thing tends to
draw it. Check every result by eye for:

- a spoked wheel, a knot, a conch, a parasol, a vase, a banner or a pair of fish;
- a buddha or a bodhisattva;
- any legible script.

Reject a sheet that shows any of these.

## The images

| Id | What it is for | Target in game |
|---|---|---|
| `MARA-SPR-EMANATION` | the emanation, his fighting body | 4 m = 128 px tall; drawn at 384 px, keyed |
| `MARA-SPR-CROWN` | the crown's overlays: one skull-flame, one closed gold lotus | small overlays on the five skulls |
| `MARA-BG-COLOSSUS` | the colossus behind the arena, chest up, without the wheel | backdrop layer, about 14 m wide |
| `MARA-BG-WHEEL` | the Wheel of Life alone, straight-on, so it can turn | about 10 m across; must be a clean circle |
| `MARA-BG-CLAWS` | the two claws and the fangs that grip and bite the rim, in front of the wheel | backdrop foreground layer |
| `MARA-PROP-THRONE` | the black throne, a ledge | 3 m wide, 2 m tall |
| `MARA-PROP-LOTUS` | a burning lotus pedestal, a ledge | 2 m wide |
| `MARA-ARENA-FAR` | the far layer: a sea of fire under a black sky | the arena's backdrop plate |
| `MARA-PROP-BOWL` | the Tempter's Offering | 1.2 m |

### `MARA-SPR-EMANATION`

> Game sprite of Mara Papiyas, the demon lord of the Wheel of Life, a final boss for a 2D
> side-view action game: a wrathful demon with black-blue skin, three eyes (the third upright
> on his brow), bared fangs, a crown of five plain white skulls with no flames, a small ring of
> gold and vermilion flames behind his shoulders, a tiger-skin loincloth, bone ornaments, bare
> hands with long claws, a coiled noose of gold-and-red cord hanging at his belt. Nothing in his
> hands. Full body, side view facing left, in a heavy menacing fighting stance, centred, the
> entire figure visible with wide margins. Chunky readable proportions like a chibi
> action-game character but larger and menacing, bold dark outline, clean flat cel shading.
> Background: one perfectly flat solid magenta (#FF00FF), no texture, no gradient, no ground, no
> shadow, nothing else in the frame. Palette: black-blue skin, gold, vermilion, bone white,
> tiger orange and black. No text, no lettering, no watermark.

The skulls are plain on purpose. Their flames are `MARA-SPR-CROWN`, laid over each unsealed
skull.

### `MARA-SPR-CROWN`

> Two small game-sprite icons side by side on one image, each centred in its own half with
> wide margins: on the left, a single small skull with a tall vermilion and gold flame rising
> from its crown; on the right, a single small skull, dull grey, with a closed gold lotus bud
> folded over it like a lid. Bold dark outline, clean flat cel shading, matching a chibi
> action-game boss sprite. Background: one perfectly flat solid magenta (#FF00FF), no texture,
> no gradient, no shadow. No text, no lettering, no watermark.

### `MARA-BG-COLOSSUS`

> Background layer for a 2D side-view boss arena, straight-on view: the colossal upper body of
> Mara, the demon lord, seen from the chest up and centred — black-blue skin, three blazing
> eyes, an open fanged mouth, a crown of five white skulls, a huge aureole of swirling gold and
> vermilion flames behind him. His two great clawed hands are raised to either side at
> shoulder height, open, as if about to grip the rim of something round and huge in front of
> his chest; the space in front of his chest is empty and dark. Medium: Tibetan nagtang
> black-ground thangka painting — fine gold line on a deep black ground, vermilion and mineral
> blue and green accents, hairline detail, a flat iconographic frontal pose. Background: plain
> deep black. No wheel, no other figures, no text, no lettering, no watermark, no border.

### `MARA-BG-WHEEL`

> A single perfectly circular Wheel of Life, seen exactly straight-on and centred, filling the
> image with a margin all round, for a 2D game background. The wheel is divided by six spokes
> into six equal segments, and each segment shows its realm in tiny fine gold-line scenes:
>
> - top segment: the gods, a pale gold palace among clouds;
> - clockwise, the second: the jealous demigods, warriors under a great tree;
> - the third: the hungry ghosts, gaunt grey-green figures with huge bellies;
> - the fourth, at the bottom: hell, deep crimson flames;
> - the fifth: the animals, beasts in a dull ochre field;
> - the sixth: the humans, a small village in warm daylight.
>
> On the outer rim, centred on each segment except the humans', sits one round sigil:
>
> - a yellow wilting flower garland on the gods' segment;
> - a blue coiled snake on the demigods';
> - a red rooster on the hungry ghosts';
> - a green crossed ox horn and horse ear on hell's;
> - a white pig on the animals'.
>
> The humans' segment has a plain open rim with no sigil.
>
> At the hub, three small animals chase each other's tails in a circle: a rooster, a snake and
> a pig. No other symbols anywhere on the wheel.
>
> Medium: Tibetan nagtang black-ground thangka painting — fine gold line on a deep black
> ground, vermilion and mineral blue and green accents, hairline detail. Outside the circle:
> one perfectly flat solid magenta (#FF00FF). No hands, no figure holding it, no text, no
> lettering, no watermark, no border.

The game turns this image, so the realm under his fangs sits at the top.

- **The segment order is gameplay.** `WheelTable.Realms` is clockwise from the top: gods,
  asuras, hungry ghosts, hell, animals, humans. Check the image follows it.
- **A sealed sigil** is the crown's closed gold lotus (`MARA-SPR-CROWN`), laid over the sigil.

### `MARA-BG-CLAWS`

> Two separate game-background elements on one image, each centred in its own half with wide
> margins: on the left, a colossal black-blue demon's left hand with long bone-white claws,
> seen from the front, curled as if gripping the rim of a huge wheel; on the right, the
> matching right hand, mirrored. Medium: Tibetan nagtang black-ground thangka painting — fine
> gold line, black-blue skin, bone-white claws, hairline detail. Background: one perfectly flat
> solid magenta (#FF00FF). No wheel, no text, no lettering, no watermark.

The fangs that bite the top of the rim are part of `MARA-BG-COLOSSUS`'s open mouth. Layer the
image like this:

1. the colossus;
2. the wheel;
3. the claws.

If the mouth will not sit over the rim, ask Fable for an upper-jaw cut.

### `MARA-PROP-THRONE`

> A single game prop on its own, side view, for a 2D platform fighter: a squat black stone
> throne carved with fine gold line, flames licking its base, its flat seat wide enough to
> stand on; heavy and iconographic. Medium: Tibetan nagtang — gold line on black, vermilion
> flames. Background: one perfectly flat solid magenta (#FF00FF), no ground, no shadow. No
> figure on it, no text, no lettering, no watermark.

### `MARA-PROP-LOTUS`

> A single game prop on its own, side view, for a 2D platform fighter: a wide lotus pedestal
> of black and gold petals, burning with small vermilion flames along its rim, its flat top
> wide enough to stand on. Medium: Tibetan nagtang — gold line on black. Background: one
> perfectly flat solid magenta (#FF00FF), no ground, no shadow. No text, no lettering, no
> watermark.

### `MARA-ARENA-FAR`

> Far background layer of a side-view 2D platform-fighter arena, 16:9 widescreen, camera
> straight-on: the bottom of Avici — a sea of churning vermilion and gold fire under a deep
> black sky with stylised gold cloud scrolls; low on the horizon a band of black rock. Empty of
> figures, the upper middle of the frame left dark and open, where a colossal figure will be
> layered. Medium: Tibetan nagtang black-ground thangka painting — fine gold line on deep black,
> vermilion flames, mineral blue and green accents, hairline detail. Palette: black, gold,
> vermilion, azurite blue, malachite green. No figures, no wheel, no text, no lettering, no UI,
> no watermark, no border.

### `MARA-PROP-BOWL`

> A single small game prop: a gold offering bowl brimming with dark red, glowing faintly, side
> view. Bold dark outline, clean flat cel shading. Background: one perfectly flat solid magenta
> (#FF00FF), no ground, no shadow. No text, no lettering, no watermark.

## Before generating — for Fable

- **Rendering the asuras.** The wheel's second segment is called "the jealous demigods". That
  is the traditional English for the asuras, and it keeps the image model away from the
  game's own Asura of Wrath. Keep or change it.
- **Matching the medium.** The emanation is cel-shaded like the Asura's sprite, so he reads
  at 128 px in the fight. The backdrop is in the nagtang medium. Check the two still look like
  one character.
- **The colossus at screen size.** The colossus may be off screen in a wide arena. The
  `WheelIndicator` at the top right of the screen is what the player always sees. Decide
  whether it should use a small cut of `MARA-BG-WHEEL`.
