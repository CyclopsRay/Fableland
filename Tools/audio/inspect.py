#!/usr/bin/env python3
"""Look at the sound.

Nobody can listen while the audio layer is built, so its sounds are judged from pictures
and numbers (Docs/Audio.gdd §12). This tool reads a dump written by

    Godot --headless --path . res://Scenes/Dev/AudioDump.tscn -- --out=/abs/dump

and draws, for each of the ten media, a sheet of waveforms and spectrograms, plus a text
report: where each medium's energy sits, how fast its sounds start, which media are too
alike, which sounds are harsh (energy piled above 8 kHz) or boomy, and whether a sound's
variants are siblings (alike, never identical).

    /opt/homebrew/bin/python3 Tools/audio/inspect.py /abs/dump --out /abs/sheets
           [--docs Docs/Audio/sheets]     # also write the JPEG summary sheets kept in the repo

Needs numpy and Pillow (the Anaconda python on the dev Mac has a broken numpy — KNOWLEDGE).
"""
import os
import sys

# This file is called inspect.py and numpy imports the standard library's `inspect`. Python
# puts a script's own folder first on the module path, so numpy would import THIS file and
# fail half-way ("partially initialized module 'numpy'"). Take our folder off the path first.
_HERE = os.path.dirname(os.path.abspath(__file__))
sys.path[:] = [p for p in sys.path if os.path.abspath(p or os.getcwd()) != _HERE]

import argparse  # noqa: E402
import csv  # noqa: E402
import wave  # noqa: E402

import numpy as np  # noqa: E402
from PIL import Image, ImageDraw

MEDIA = ["VK", "SL", "BM", "HC", "TD", "PL", "KH", "TF", "CC", "VOID"]
NAMES = {
    "VK": "storybook watercolour", "SL": "flat poster", "BM": "freehand ink", "HC": "lit realism",
    "TD": "linocut", "PL": "gongbi", "KH": "scratchboard", "TF": "pixel art", "CC": "cloisonné enamel",
    "VOID": "the VOID's black",
}
# The events every medium renders, shown on every summary sheet: (event id, seconds shown).
CORE = [
    ("any.jump", 0.6), ("any.land", 0.6), ("foe.death", 0.9), ("any.hit", 0.6),
    ("foe.hit.taken", 0.6), ("any.combo.note", 0.9), ("hazard.tick.fire", 0.6), ("hazard.tick.spike", 0.6),
    ("world.soft.enter.water", 0.9), ("any.status.burning", 1.2), ("flow.countdown.go", 1.0), ("flow.mission.success", 2.6),
]
TOP_HZ = 11025.0
BG = (18, 18, 22)
INK = (232, 232, 226)
DIM = (120, 122, 130)


def read_wav(path):
    with wave.open(path, "rb") as w:
        sr = w.getframerate()
        x = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float32) / 32768.0
    return x, sr


def stft_db(x, sr, n_fft=1024, hop=128):
    """Magnitude spectrogram in dB (frames × bins), 0 dB = full scale."""
    if len(x) < n_fft:
        x = np.pad(x, (0, n_fft - len(x)))
    frames = 1 + (len(x) - n_fft) // hop
    idx = np.arange(n_fft)[None, :] + hop * np.arange(frames)[:, None]
    win = np.hanning(n_fft).astype(np.float32)
    mag = np.abs(np.fft.rfft(x[idx] * win, axis=1)) / (n_fft / 4.0)
    return 20.0 * np.log10(np.maximum(mag, 1e-6))


def palette():
    """A dark-to-bright map: black → violet → orange → cream."""
    stops = np.array([[0, 0, 4], [40, 11, 84], [120, 28, 109], [188, 55, 84], [237, 105, 37], [251, 180, 26], [252, 255, 164]], dtype=np.float32)
    t = np.linspace(0, len(stops) - 1, 256)
    lo = np.floor(t).astype(int).clip(0, len(stops) - 2)
    f = (t - lo)[:, None]
    return (stops[lo] * (1 - f) + stops[lo + 1] * f).astype(np.uint8)


PAL = palette()


def spec_image(x, sr, width, height, seconds, floor=-84.0):
    """Spectrogram, 0…TOP_HZ on a linear axis, `seconds` wide whatever the sound's length."""
    db = stft_db(x, sr)
    hop_s = 128.0 / sr
    cols = max(1, int(seconds / hop_s))
    canvas = np.full((cols, db.shape[1]), floor, dtype=np.float32)
    n = min(cols, db.shape[0])
    canvas[:n] = db[:n]
    nyq = sr / 2.0
    bins = int(db.shape[1] * min(1.0, TOP_HZ / nyq))
    img = np.full((cols, int(db.shape[1] * TOP_HZ / nyq)), floor, dtype=np.float32)
    img[:, :bins] = canvas[:, :bins]
    norm = ((img - floor) / (0.0 - floor)).clip(0, 1)
    rgb = PAL[(norm.T[::-1] * 255).astype(np.uint8)]
    return Image.fromarray(rgb, "RGB").resize((width, height), Image.BILINEAR)


def wave_image(x, sr, width, height, seconds):
    img = Image.new("RGB", (width, height), (10, 10, 13))
    d = ImageDraw.Draw(img)
    n = int(seconds * sr)
    y = np.zeros(n, dtype=np.float32)
    y[: min(n, len(x))] = x[:n]
    per = max(1, n // width)
    trimmed = y[: per * width].reshape(width, per) if n >= width else np.pad(y, (0, width - n)).reshape(width, 1)
    hi, lo = trimmed.max(axis=1), trimmed.min(axis=1)
    mid = height // 2
    d.line([(0, mid), (width, mid)], fill=(40, 40, 46))
    for i in range(width):
        d.line([(i, mid - int(hi[i] * (mid - 1))), (i, mid - int(lo[i] * (mid - 1)))], fill=(110, 200, 170))
    return img


def tile(x, sr, seconds, label, note="", w_wave=150, w_spec=250, h=58):
    """One sound: its name, its waveform, its spectrogram with 5 and 8 kHz marked."""
    t = Image.new("RGB", (w_wave + w_spec + 6, h + 14), BG)
    d = ImageDraw.Draw(t)
    d.text((2, 1), label, fill=INK)
    if note:
        d.text((w_wave + 8, 1), note, fill=DIM)
    t.paste(wave_image(x, sr, w_wave, h, seconds), (0, 14))
    t.paste(spec_image(x, sr, w_spec, h, seconds), (w_wave + 6, 14))
    for hz, colour in ((5000, (70, 70, 80)), (8000, (150, 60, 60))):
        yy = 14 + h - int(h * hz / TOP_HZ)
        d.line([(w_wave + 6, yy), (w_wave + 12, yy)], fill=colour)
    return t


def bands(x, sr, n=24):
    """Average spectrum in n log-spaced bands, dB — a sound's colour as a vector."""
    db = stft_db(x, sr)
    power = (10.0 ** (db / 10.0)).mean(axis=0)
    freqs = np.fft.rfftfreq(1024, 1.0 / sr)
    edges = np.geomspace(60.0, TOP_HZ, n + 1)
    out = np.zeros(n, dtype=np.float32)
    for i in range(n):
        m = (freqs >= edges[i]) & (freqs < edges[i + 1])
        out[i] = power[m].sum() if m.any() else 0.0
    out = 10.0 * np.log10(np.maximum(out, 1e-9))
    return np.maximum(out - out.max(), -50.0)


def envelope(x, sr, n=16, seconds=0.6):
    m = int(seconds * sr)
    y = np.zeros(m, dtype=np.float32)
    y[: min(m, len(x))] = np.abs(x[:m])
    e = y[: (m // n) * n].reshape(n, -1).max(axis=1)
    e = 20.0 * np.log10(np.maximum(e, 1e-4))
    return np.maximum(e - e.max(), -50.0)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("dump")
    ap.add_argument("--out", default=None, help="folder for sheets and report (default: <dump>/sheets)")
    ap.add_argument("--docs", default=None, help="also write the per-medium JPEG summary sheets here (under 400 KB each)")
    args = ap.parse_args()
    out = args.out or os.path.join(args.dump, "sheets")
    os.makedirs(out, exist_ok=True)

    rows = list(csv.DictReader(open(os.path.join(args.dump, "index.tsv")), delimiter="\t"))
    by = {}
    for r in rows:
        by.setdefault((r["kind"], r["medium"], r["id"]), []).append(r)
    cache = {}

    def load(r):
        if r["file"] not in cache:
            cache[r["file"]] = read_wav(os.path.join(args.dump, r["file"]))
        return cache[r["file"]]

    report = []
    say = report.append

    # ── 1. Per-medium summary sheets ────────────────────────────────────────────────
    for m in MEDIA:
        tiles = []
        for event, seconds in CORE:
            rs = by.get(("sfx", m, event))
            if not rs:
                continue
            x, sr = load(rs[0])
            r = rs[0]
            tiles.append(tile(x, sr, seconds, event, f"{float(r['seconds']):.2f}s  {float(r['centroid_hz']) / 1000:.1f}k  >8k {float(r['above8k']) * 100:.0f}%"))
        own = [k for k in by if k[0] == "sfx" and k[1] == m and not k[2].split(".")[0] in ("any", "foe", "fighter", "boss", "objective", "hazard", "world", "flow", "ui")]
        for k in sorted(own)[:6]:
            x, sr = load(by[k][0])
            tiles.append(tile(x, sr, 0.9, k[2][:34], f"{float(by[k][0]['seconds']):.2f}s"))
        cols = 2
        tw, th = tiles[0].size
        music = [by.get(("music", m, f"{m}@{i}")) for i in ("0.15", "0.50", "0.90")]
        rows_n = (len(tiles) + cols - 1) // cols
        width = cols * tw + (cols + 1) * 8
        height = 34 + rows_n * (th + 6) + (3 * 86 if music[0] else 0) + 8
        sheet = Image.new("RGB", (width, height), BG)
        d = ImageDraw.Draw(sheet)
        d.text((8, 6), f"{m} — {NAMES[m]}", fill=INK)
        d.text((8, 19), "left: waveform   right: spectrogram 0–11 kHz (ticks at 5 and 8 kHz)   note: length · centroid · share of energy above 8 kHz", fill=DIM)
        for i, t in enumerate(tiles):
            sheet.paste(t, (8 + (i % cols) * (tw + 8), 34 + (i // cols) * (th + 6)))
        y0 = 34 + rows_n * (th + 6)
        for j, rs in enumerate(music):
            if not rs:
                continue
            x, sr = load(rs[0])
            d.text((8, y0 + j * 86), f"music at intensity {('0.15', '0.50', '0.90')[j]} — 30 s   peak {rs[0]['peak_db']} dB   rms {rs[0]['rms_db']} dB", fill=INK)
            sheet.paste(spec_image(x, sr, width - 16, 68, 30.0), (8, y0 + j * 86 + 14))
        sheet.save(os.path.join(out, f"medium_{m}.png"))
        if args.docs:
            os.makedirs(args.docs, exist_ok=True)
            path = os.path.join(args.docs, f"{m}.jpg")
            for quality in (82, 74, 66, 58, 50, 42):
                sheet.save(path, "JPEG", quality=quality, optimize=True)
                if os.path.getsize(path) < 400 * 1024:
                    break

    # ── 2. One event across the ten media ───────────────────────────────────────────
    for event, seconds in CORE:
        tiles = []
        for m in MEDIA:
            rs = by.get(("sfx", m, event))
            if rs:
                x, sr = load(rs[0])
                tiles.append(tile(x, sr, seconds, f"{m}  {NAMES[m]}", "", 220, 420, 50))
        if not tiles:
            continue
        tw, th = tiles[0].size
        sheet = Image.new("RGB", (tw + 16, 24 + len(tiles) * (th + 4)), BG)
        ImageDraw.Draw(sheet).text((8, 6), f"{event} — the same event in every medium ({seconds} s shown)", fill=INK)
        for i, t in enumerate(tiles):
            sheet.paste(t, (8, 24 + i * (th + 4)))
        sheet.save(os.path.join(out, f"event_{event}.png"))

    # ── 3. The numbers ──────────────────────────────────────────────────────────────
    say("AUDIO INSPECTION REPORT")
    say("=======================")
    say("")
    say("Per medium, over the class-level and realm-level events every medium renders")
    say("(first variant of each). centroid = where the spectrum's weight sits; >5k / >8k = share")
    say("of energy above 5 and 8 kHz; <120 = share below 120 Hz; attack = time to half the peak.")
    say("")
    common = None
    for m in MEDIA:
        ids = {k[2] for k in by if k[0] == "sfx" and k[1] == m}
        common = ids if common is None else common & ids
    common = sorted(common)
    say(f"{len(common)} events are rendered by all ten media.")
    say("")
    say(f"{'medium':<6} {'name':<22} {'centroid':>9} {'>5k':>6} {'>8k':>6} {'<120':>6} {'attack':>8} {'length':>7}  flags")
    feats = {}
    for m in MEDIA:
        cen, a5, a8, b1, att, ln = [], [], [], [], [], []
        vec = []
        for e in common:
            r = by[("sfx", m, e)][0]
            cen.append(float(r["centroid_hz"])); a5.append(float(r["above5k"])); a8.append(float(r["above8k"]))
            b1.append(float(r["below120"])); att.append(float(r["attack_ms"])); ln.append(float(r["seconds"]))
            x, sr = load(r)
            vec.append(np.concatenate([bands(x, sr), envelope(x, sr)]))
        feats[m] = np.stack(vec)
        flags = []
        if np.mean(a8) > 0.20: flags.append("HARSH: a fifth of the energy is above 8 kHz")
        if np.mean(b1) > 0.45: flags.append("BOOMY: nearly half the energy is below 120 Hz")
        say(f"{m:<6} {NAMES[m]:<22} {np.mean(cen):8.0f}Hz {np.mean(a5) * 100:5.1f}% {np.mean(a8) * 100:5.1f}% {np.mean(b1) * 100:5.1f}% {np.median(att):6.1f}ms {np.mean(ln):6.2f}s  {'; '.join(flags)}")
    say("")
    say("Worst single sounds (any medium): share of energy above 8 kHz")
    worst = sorted((r for r in rows if r["kind"] == "sfx"), key=lambda r: -float(r["above8k"]))[:10]
    for r in worst:
        say(f"  {float(r['above8k']) * 100:5.1f}%  {r['medium']:<5} {r['id']} v{r['variant']}")
    say("")
    say("Distance between media (RMS difference, dB, of each event's band spectrum and envelope;")
    say("under 6 dB two media are hard to tell apart on paper):")
    say("       " + " ".join(f"{m:>5}" for m in MEDIA))
    close = []
    for a in MEDIA:
        line = f"{a:<6} "
        for b in MEDIA:
            dist = float(np.sqrt(((feats[a] - feats[b]) ** 2).mean()))
            line += f"{dist:5.1f} "
            if a < b and dist < 6.0:
                close.append((dist, a, b))
        say(line)
    say("")
    say("Pairs under 6 dB: " + (", ".join(f"{a}/{b} {d:.1f}" for d, a, b in sorted(close)) if close else "none"))
    say("")

    # Variants: siblings must be alike in colour and never the same waveform.
    same, apart, pairs = [], [], 0
    for (kind, m, e), rs in by.items():
        if kind != "sfx" or len(rs) < 2:
            continue
        (x1, sr), (x2, _) = load(rs[0]), load(rs[1])
        n = min(len(x1), len(x2))
        if n < 64:
            continue
        pairs += 1
        denom = float(np.linalg.norm(x1[:n]) * np.linalg.norm(x2[:n])) or 1.0
        corr = abs(float(np.dot(x1[:n], x2[:n])) / denom)
        dist = float(np.sqrt(((bands(x1, sr) - bands(x2, sr)) ** 2).mean()))
        if corr > 0.985:
            same.append((corr, m, e))
        if dist > 9.0:
            apart.append((dist, m, e))
    say(f"Variants: {pairs} first-pairs compared. Identical waveforms (correlation > 0.985): {len(same)}. "
        f"Not siblings (band spectra over 9 dB apart): {len(apart)}.")
    for c, m, e in sorted(same, reverse=True)[:12]:
        say(f"  same   {c:.3f}  {m:<5} {e}")
    for dd, m, e in sorted(apart, reverse=True)[:12]:
        say(f"  apart  {dd:4.1f}dB {m:<5} {e}")
    say("")

    say("Loudest and quietest music mixes (peak / rms dB):")
    for r in sorted((r for r in rows if r["kind"] == "music"), key=lambda r: r["id"]):
        say(f"  {r['id']:<14} peak {float(r['peak_db']):6.1f}  rms {float(r['rms_db']):6.1f}  centroid {float(r['centroid_hz']):6.0f} Hz  >8k {float(r['above8k']) * 100:4.1f}%  <120 {float(r['below120']) * 100:4.1f}%")

    text = "\n".join(report) + "\n"
    open(os.path.join(out, "report.txt"), "w").write(text)
    sys.stdout.write(text)
    print(f"sheets and report.txt written to {out}")


if __name__ == "__main__":
    main()
