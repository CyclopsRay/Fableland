#!/usr/bin/env python3
"""Save the concept images generated in Tripo Studio (web) next to their prompts.

Tripo Studio has no export for a batch of generations, and its image links are signed
per file. The harvest step (run in the owner's signed-in browser pane) lists, for every
generation, the scene it was asked for, the model, and each image's path + expiry +
signature. This script rebuilds the signed links from that listing, downloads the files,
matches each to its entry in prompts.json, and writes:

  Docs/Art/StyleExploration/_originals/<ID>[-mjN].png   full size, git-ignored
  Docs/Art/StyleExploration/img/<ID>[-mjN].jpg          preview, tracked
  Docs/Art/StyleExploration/manifest.json               id -> files, model, prompt, source

Usage:
  fetch_tripo_images.py <harvest.json | session-transcript.jsonl> [--only-missing] [--set kit]

--set NAME writes previews and the manifest under StyleExploration/NAME/ instead (the
overworld kit sheets use "kit", so they stay out of the concept gallery). A harvest entry
that carries its own "id" and prompt "p" needs no entry in prompts.json.

A session transcript is accepted because that is where the harvest lands when it is read
out of the page: the last "TRIPO_HARVEST_V1{...}" block in the file is used.
Needs Pillow (use /usr/bin/python3 or /opt/homebrew/bin/python3 on the dev Mac).
"""
import base64, json, os, sys, time, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Docs", "Art", "StyleExploration")
BASE = "https://tripo-data.rg1.data.tripo3d.com/generate-image/"
MARK = "TRIPO_HARVEST_V1"
MODEL_NAMES = {"G": "GPT Image 2.5", "M": "Midjourney v8.2"}


def find_harvest(path):
    """Return the harvest dict from a plain JSON file or from the last marker in a JSONL."""
    raw = open(path, encoding="utf-8").read()
    if MARK not in raw:
        return json.loads(raw)
    found = []

    def walk(node):
        if isinstance(node, str):
            if MARK in node:
                found.append(node)
        elif isinstance(node, dict):
            for v in node.values():
                walk(v)
        elif isinstance(node, list):
            for v in node:
                walk(v)

    for line in raw.splitlines():
        if MARK not in line:
            continue
        try:
            walk(json.loads(line))
        except Exception:
            continue
    best = None
    for text in found:
        # the tool result may be a JSON-quoted string: unquote until the marker leads
        for _ in range(3):
            stripped = text.strip()
            if stripped.startswith(MARK):
                break
            if stripped.startswith('"'):
                try:
                    # raw_decode: the quoted result is followed by the tool's own footer text
                    text, _ = json.JSONDecoder().raw_decode(stripped)
                    continue
                except Exception:
                    pass
            cut = text.find(MARK)
            text = text[cut:]
        body = text.strip()[len(MARK):]
        try:
            data, _ = json.JSONDecoder().raw_decode(body)
        except Exception:
            continue
        if isinstance(data, dict) and "e" in data:
            best = data  # keep the last valid one
    if best is None:
        raise SystemExit("no readable %s block in %s" % (MARK, path))
    return best


def signed_url(kp, record):
    path, epoch, signature = record.split("|")
    resource = BASE + path
    policy = json.dumps({"Statement": [{"Resource": resource, "Condition": {"DateLessThan": {"AWS:EpochTime": int(epoch)}}}]},
                        separators=(",", ":"))
    b64 = base64.b64encode(policy.encode()).decode().replace("+", "-").replace("=", "_").replace("/", "~")
    return "%s?Key-Pair-Id=%s&Policy=%s&Signature=%s" % (resource, kp, b64, signature), int(epoch), path


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    only_missing = "--only-missing" in sys.argv
    out_set = sys.argv[sys.argv.index("--set") + 1] if "--set" in sys.argv else ""
    harvest = find_harvest(sys.argv[1])
    prompts = json.load(open(os.path.join(OUT, "prompts.json"), encoding="utf-8"))
    entries = prompts["entries"]
    os.makedirs(os.path.join(OUT, "_originals"), exist_ok=True)
    img_dir = os.path.join(OUT, out_set, "img") if out_set else os.path.join(OUT, "img")
    os.makedirs(img_dir, exist_ok=True)
    manifest_path = os.path.join(OUT, out_set, "manifest.json") if out_set else os.path.join(OUT, "manifest.json")
    manifest = json.load(open(manifest_path, encoding="utf-8")) if os.path.exists(manifest_path) else {"images": []}
    known = {m["file"]: m for m in manifest["images"]}

    from PIL import Image

    failed_generations, saved, skipped, errors = [], 0, 0, []
    for e in harvest["e"]:
        if e.get("id"):
            # the harvest names the entry itself and carries the prompt as typed
            parts = e["id"].split("-")
            p = {"id": e["id"], "world": parts[1] if len(parts) > 2 else parts[0], "letter": parts[-1],
                 "prompt": e.get("p", ""), "scene": e.get("p", "")}
        else:
            matches = [p for p in entries if p["scene"].startswith(e["s"].rstrip())]
            if len(matches) != 1:
                errors.append("scene %r matches %d prompts" % (e["s"], len(matches)))
                continue
            p = matches[0]
        model = MODEL_NAMES.get(e["m"], e["m"])
        if e.get("failed") or not e["i"]:
            failed_generations.append("%s (%s, %s)" % (p["id"], model, e.get("t", "")))
            continue
        for n, record in enumerate(e["i"]):
            url, epoch, src_path = signed_url(harvest["kp"], record)
            stem = p["id"] if e["m"] == "G" else "%s-mj%d" % (p["id"], n + 1)
            original = os.path.join(OUT, "_originals", stem + ".png")
            preview = os.path.join(img_dir, stem + ".jpg")
            if only_missing and os.path.exists(original) and os.path.exists(preview):
                skipped += 1
                continue
            if epoch < time.time():
                errors.append("%s: link expired" % stem)
                continue
            try:
                req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
                with urllib.request.urlopen(req, timeout=120) as resp:
                    data = resp.read()
            except Exception as ex:
                errors.append("%s: %s" % (stem, ex))
                continue
            with open(original, "wb") as f:
                f.write(data)
            im = Image.open(original).convert("RGB")
            w, h = im.size
            limit = 1600 if w >= h * 1.2 else 1100
            if max(w, h) > limit:
                scale = limit / float(max(w, h))
                im_small = im.resize((round(w * scale), round(h * scale)), Image.LANCZOS)
            else:
                im_small = im
            im_small.save(preview, "JPEG", quality=86, optimize=True, progressive=True)
            prompt_sent = p["prompt"]
            if e["m"] == "M":
                # what was typed for Midjourney: the same prompt without the "16:9 widescreen"
                # clause (its output is always square here) — except the first trial, which
                # kept it and added "--ar 16:9" (ignored by this integration).
                if e.get("w"):
                    prompt_sent = prompt_sent + (" --ar 16:9" if e.get("ar") else "")
                else:
                    prompt_sent = prompt_sent.replace(", 16:9 widescreen,", ",")
            known[stem + ".jpg"] = {
                "file": stem + ".jpg", "original": "_originals/" + stem + ".png", "id": p["id"], "world": p["world"],
                "letter": p["letter"], "model": model, "variant": n + 1 if e["m"] == "M" else 0,
                "size": [w, h], "bytes": len(data), "generated": "2026-11-25 " + e.get("t", ""),
                "source": "Tripo Studio web / generate-image/" + src_path, "prompt": prompt_sent,
            }
            saved += 1
            print("saved %-12s %4dx%-4d %5.1f MB  (%s)" % (stem, w, h, len(data) / 1e6, model), flush=True)

    manifest["images"] = sorted(known.values(), key=lambda m: m["file"])
    manifest["note"] = "Concept images for the owner to choose from. Originals are not in git (see .gitignore)."
    json.dump(manifest, open(manifest_path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print("\nsaved %d, skipped %d, manifest has %d images" % (saved, skipped, len(manifest["images"])))
    if failed_generations:
        print("generations that failed in Tripo (nothing to download): " + ", ".join(failed_generations))
    if errors:
        print("ERRORS:\n  " + "\n  ".join(errors))
        sys.exit(1)


if __name__ == "__main__":
    main()
