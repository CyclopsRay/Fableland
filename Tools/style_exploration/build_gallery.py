#!/usr/bin/env python3
"""Build Docs/Art/StyleExploration/index.html - the gallery the owner picks from.

Reads, from Docs/Art/StyleExploration/:
  manifest.json  the images (file, id, world, letter, model, variant, size)
  prompts.json   each direction's title, one-line pitch and full prompt
  review.json    the art-direction notes: per-world verdict, recommended pick and
                 runner-up, and one line per image (the long form is REVIEW.md)
and writes one self-contained page (inline CSS and JS, the data embedded as JSON) that
loads the previews by relative path from img/. It also refreshes ref/<WORLD>.jpg, small
copies of the shipped reference renders, so the page can show "what ships today" beside
the concepts.

    /opt/homebrew/bin/python3 Tools/style_exploration/build_gallery.py

The output is ASCII-only (everything else is escaped), so it reads correctly opened from
disk whatever the browser guesses about encoding. Pillow is used only for ref/ (macOS
`sips` is the fallback). To change a note or a recommendation, edit review.json and re-run.
"""
import datetime
import json
import os
import struct
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
DIR = os.path.join(ROOT, 'Docs', 'Art', 'StyleExploration')


def load(name):
    with open(os.path.join(DIR, name), encoding='utf-8') as f:
        return json.load(f)


def png_size(path):
    with open(path, 'rb') as f:
        head = f.read(24)
    return struct.unpack('>II', head[16:24])


def make_ref(code, src):
    """Copy a shipped reference render to ref/<code>.jpg. Returns {src, w, h} or None."""
    if not src:
        return None
    src_abs = os.path.join(ROOT, src)
    if not os.path.exists(src_abs):
        print('  ! reference not found, skipped:', src)
        return None
    os.makedirs(os.path.join(DIR, 'ref'), exist_ok=True)
    rel = 'ref/%s.jpg' % code
    dst = os.path.join(DIR, rel)
    if not os.path.exists(dst) or os.path.getmtime(dst) < os.path.getmtime(src_abs):
        try:
            from PIL import Image
            im = Image.open(src_abs).convert('RGBA')
            ground = Image.new('RGBA', im.size, (201, 204, 210, 255))  # neutral grey under transparency
            ground.alpha_composite(im)
            ground.convert('RGB').save(dst, quality=86)
        except ImportError:
            done = subprocess.run(['sips', '-s', 'format', 'jpeg', '-s', 'formatOptions', '86',
                                   src_abs, '--out', dst], capture_output=True)
            if done.returncode != 0:
                print('  ! could not convert (no Pillow, no sips):', src)
                return None
    w, h = png_size(src_abs)
    return {'src': rel, 'w': w, 'h': h}


def build():
    manifest, prompts, review = load('manifest.json'), load('prompts.json'), load('review.json')
    entries = {e['id']: e for e in prompts['entries']}
    by_world = {}
    for im in manifest['images']:
        by_world.setdefault(im['world'], []).append(im)

    problems, worlds, count = [], [], 0
    for code in review['order']:
        rv = review['worlds'][code]
        ims = by_world.pop(code, [])

        def item(im):
            stem = os.path.splitext(im['file'])[0]
            entry = entries.get(im['id'])
            if entry is None:
                problems.append('prompts.json has no entry for ' + im['id'])
                entry = {'title': im['id'], 'pitch': ''}
            if not rv['notes'].get(stem):
                problems.append('review.json has no note for ' + stem)
            if not os.path.exists(os.path.join(DIR, 'img', im['file'])):
                problems.append('missing file img/' + im['file'])
            return {'key': stem, 'id': im['id'], 'letter': im['letter'],
                    'variant': im.get('variant') or 0, 'title': entry['title'],
                    'pitch': entry['pitch'], 'model': im['model'], 'src': 'img/' + im['file'],
                    'w': im['size'][0], 'h': im['size'][1], 'note': rv['notes'].get(stem, '')}

        main = sorted((i for i in ims if not i.get('variant')), key=lambda i: i['letter'])
        groups = [{'kind': 'main', 'label': '', 'items': [item(i) for i in main]}]
        for letter in sorted({i['letter'] for i in ims if i.get('variant')}):
            takes = sorted((i for i in ims if i.get('variant') and i['letter'] == letter),
                           key=lambda i: i['variant'])
            groups.append({'kind': 'mj', 'items': [item(i) for i in takes],
                           'label': '%s — %d takes on direction %s, same prompt'
                                    % (takes[0]['model'], len(takes), letter)})
        keys = {it['key'] for g in groups for it in g['items']}
        count += len(keys)
        for field in ('pick', 'runner_up'):
            if rv[field] not in keys:
                problems.append('%s: %s "%s" is not one of its images' % (code, field, rv[field]))
        stale = sorted(set(rv['notes']) - keys)
        if stale:
            problems.append('%s: notes for images not in manifest.json: %s' % (code, ', '.join(stale)))
        worlds.append({'code': code, 'name': rv['name'], 'medium': rv['medium'],
                       'mediumLabel': 'Medium in the style bible' if rv.get('ships_ref') else 'Brief',
                       'canvas': rv.get('canvas', ''), 'verdict': rv['verdict'],
                       'ships': rv.get('ships', ''), 'ref': make_ref(code, rv.get('ships_ref')),
                       'pick': rv['pick'], 'runner': rv['runner_up'], 'groups': groups})
    if by_world:
        problems.append('worlds in manifest.json with no review: ' + ', '.join(sorted(by_world)))
    if problems:
        print('Not built:')
        for p in problems:
            print('  -', p)
        return 1

    data = {'built': datetime.datetime.now().strftime('%Y-%m-%d %H:%M'), 'images': count,
            'worlds': worlds, 'prompts': {k: e['prompt'] for k, e in entries.items()}}
    blob = json.dumps(data, ensure_ascii=True, separators=(',', ':'))
    blob = blob.replace('<', '\\u003c').replace('>', '\\u003e').replace('&', '\\u0026')
    page = TEMPLATE.replace('__DATA__', blob)
    page.encode('ascii')  # fails loudly if a non-ASCII character slipped into the template
    out = os.path.join(DIR, 'index.html')
    with open(out, 'w', encoding='ascii', newline='\n') as f:
        f.write(page)
    refs = sum(1 for w in worlds if w['ref'])
    print('index.html: %d worlds, %d images, %d shipped references, %.0f KB'
          % (len(worlds), count, refs, len(page) / 1024))
    return 0


TEMPLATE = r'''<!doctype html>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="claude:viewport-fit-sides" content="cover">
<title>Fableland Style Picks</title>
<style>
/* Layout: a sticky pick bar over one long column of worlds. Each world is a header (verdict,
   what ships today), a grid of directions, then a strip of Midjourney takes. The pictures
   carry the page; the chrome is neutral grey so that colour is judged fairly. */
:root{
  color-scheme:light;
  --bg:#EDEEF0; --surface:#FFFFFF; --ink:#16181D; --muted:#5A5F6B; --line:#D3D6DD; --well:#DFE1E6;
  --accent:#A61E3C; --accent-ink:#FFFFFF; --accent-soft:#F4D9DF;
  --lb-bg:#0B0C0E; --lb-ink:#F2F3F5; --lb-muted:#A9ADB8; --lb-line:#3A3D45; --lb-btn:rgba(11,12,14,.62);
  --font-ui:-apple-system,BlinkMacSystemFont,"Segoe UI","PingFang SC","Hiragino Sans GB","Microsoft YaHei",Roboto,"Helvetica Neue",Arial,sans-serif;
  --font-display:"Iowan Old Style","Palatino Linotype",Palatino,"Songti SC",Georgia,serif;
  --font-mono:ui-monospace,"SF Mono",Menlo,Consolas,monospace;
  --radius:6px;
}
@media (prefers-color-scheme: dark){
  :root:not([data-theme="light"]){
    color-scheme:dark;
    --bg:#121316; --surface:#1B1D21; --ink:#ECEDF0; --muted:#9CA1AD; --line:#2E3139; --well:#23262C;
    --accent:#F0718A; --accent-ink:#22060C; --accent-soft:#43202A;
  }
}
:root[data-theme="dark"]{
  color-scheme:dark;
  --bg:#121316; --surface:#1B1D21; --ink:#ECEDF0; --muted:#9CA1AD; --line:#2E3139; --well:#23262C;
  --accent:#F0718A; --accent-ink:#22060C; --accent-soft:#43202A;
}
*{box-sizing:border-box}
html{padding:env(safe-area-inset-top,0px) 0 env(safe-area-inset-bottom,0px);-webkit-text-size-adjust:100%}
body{margin:0;background:var(--bg);color:var(--ink);font:15px/1.5 var(--font-ui)}
body.lb-open{overflow:hidden}
body::before{content:"";position:fixed;inset:0 0 auto;height:env(safe-area-inset-top,0px);z-index:30;background:var(--surface)}
[hidden]{display:none!important}
img{max-width:100%}
p{margin:0}
.in{max-width:1560px;margin:0 auto;padding-left:max(16px,env(safe-area-inset-left,0px));padding-right:max(16px,env(safe-area-inset-right,0px))}
.k{font-size:11px;letter-spacing:.08em;text-transform:uppercase;color:var(--muted)}
:focus-visible{outline:2px solid var(--ink);outline-offset:2px}

/* buttons */
.btn{font:500 13px/1.2 var(--font-ui);padding:7px 13px;border:1px solid var(--line);border-radius:999px;background:var(--surface);color:var(--ink);cursor:pointer;white-space:nowrap}
.btn:hover{border-color:var(--muted)}
.btn.pick[aria-pressed="true"],.btn.go{background:var(--accent);border-color:var(--accent);color:var(--accent-ink)}
.btn.like[aria-pressed="true"]{background:var(--accent-soft);border-color:var(--accent);color:var(--ink)}

/* sticky pick bar */
.bar{position:sticky;top:env(safe-area-inset-top,0px);z-index:20;background:var(--surface);border-bottom:1px solid var(--line)}
.bar .in{display:grid;grid-template-columns:minmax(0,1fr) auto;grid-template-areas:"t c" "n n";gap:8px 12px;align-items:center;padding-block:8px}
.bar-t{grid-area:t;min-width:0;display:flex;gap:10px;align-items:baseline;white-space:nowrap}
.bar-t b{font:600 15px/1.2 var(--font-display)}
.bar-t span{font-size:13px;color:var(--muted);font-variant-numeric:tabular-nums}
#copyTop{grid-area:c}
.chips{grid-area:n;display:flex;gap:6px;min-width:0;overflow-x:auto;scrollbar-width:none;padding-bottom:1px}
.chips::-webkit-scrollbar{display:none}
.chip{flex:none;display:inline-flex;gap:6px;align-items:baseline;padding:4px 9px;border:1px solid var(--line);border-radius:999px;color:var(--ink);text-decoration:none;font:600 12px/1.3 var(--font-mono)}
.chip span{font-weight:400;color:var(--muted)}
.chip.on{border-color:var(--accent);background:var(--accent-soft)}
.chip.on span{color:var(--ink)}
@media (min-width:960px){.bar .in{grid-template-columns:auto minmax(0,1fr) auto;grid-template-areas:"t n c";gap:16px}}

/* intro */
.intro{padding-block:36px 28px;display:grid;gap:10px}
.intro h1{margin:0;font:600 30px/1.15 var(--font-display);text-wrap:balance}
.intro p{max-width:68ch}
.intro .how{color:var(--muted)}

/* a world */
.world{padding-block:36px 44px;border-top:1px solid var(--line);scroll-margin-top:calc(env(safe-area-inset-top,0px) + 100px)}
@media (min-width:960px){.world{scroll-margin-top:calc(env(safe-area-inset-top,0px) + 60px)}}
.w-head{display:grid;grid-template-columns:minmax(0,1fr);gap:22px;margin-bottom:26px}
@media (min-width:900px){.w-head.has-ref{grid-template-columns:minmax(0,1fr) 340px;gap:40px;align-items:start}}
.w-text{min-width:0;display:grid;gap:10px;justify-items:start}
.w-code{font:600 12px/1 var(--font-mono);letter-spacing:.08em;color:var(--muted)}
.w-text h2{margin:0;font:600 27px/1.15 var(--font-display);text-wrap:balance}
.w-medium{display:flex;flex-wrap:wrap;gap:4px 8px;align-items:center;font-size:14px}
.swatch{width:14px;height:14px;border-radius:3px;border:1px solid var(--line);margin-left:6px}
.hex{font:12px/1 var(--font-mono);color:var(--muted)}
.w-verdict{display:grid;gap:4px;max-width:72ch;margin-top:4px}
.w-verdict p + p + p{color:var(--muted)}
.w-ref{margin:0;display:grid;grid-template-columns:128px minmax(0,1fr);gap:8px 14px;max-width:560px;min-width:0;justify-items:start;align-items:start}
.w-ref .shot{grid-row:1 / span 2}
.w-ref figcaption{font-size:13px;color:var(--muted)}
@media (min-width:900px){.w-ref{grid-template-columns:minmax(0,1fr);max-width:none}.w-ref .shot{grid-row:auto}}
.g-label{margin:30px 0 12px;font:500 12px/1.3 var(--font-ui);letter-spacing:.06em;text-transform:uppercase;color:var(--muted)}
.grid{display:grid;gap:30px 20px;grid-template-columns:minmax(0,1fr)}
.grid.mj{grid-template-columns:repeat(2,minmax(0,1fr));gap:24px 12px}
@media (min-width:720px){
  .grid.main[data-n="4"],.grid.main[data-n="2"]{grid-template-columns:repeat(2,minmax(0,1fr))}
  .grid.mj{grid-template-columns:repeat(4,minmax(0,1fr));gap:24px 20px}
}
@media (min-width:900px){.grid.main[data-n="3"]{grid-template-columns:repeat(3,minmax(0,1fr))}}

/* a picture and its caption */
.card{min-width:0}
.shot{display:block;width:100%;max-width:100%;padding:0;border:0;border-radius:var(--radius);overflow:hidden;background:var(--well);cursor:zoom-in}
.shot img{display:block;width:100%;height:100%;object-fit:cover}
.card.is-pick .shot{outline:3px solid var(--accent);outline-offset:3px}
.c-top{display:flex;flex-wrap:wrap;gap:6px 10px;align-items:center;margin-top:11px}
.c-id{font:600 12px/1.4 var(--font-mono);letter-spacing:.02em}
.c-model{font-size:12px;color:var(--muted)}
.tag{font-size:10.5px;letter-spacing:.06em;text-transform:uppercase;border:1px solid var(--line);border-radius:999px;padding:1px 8px;color:var(--muted)}
.tag.rec{border-color:var(--ink);color:var(--ink)}
.c-title{margin:5px 0 0;font:600 15px/1.35 var(--font-ui)}
.c-pitch{margin-top:2px;font-size:13px;color:var(--muted)}
.c-note{margin-top:8px;font-size:14px;max-width:64ch}
.c-act{display:flex;flex-wrap:wrap;gap:8px;margin-top:11px}
.c-prompt{margin-top:9px;font-size:12.5px;color:var(--muted)}
.c-prompt summary{cursor:pointer;width:max-content;max-width:100%}
.c-prompt p{margin-top:6px;max-width:84ch;overflow-wrap:anywhere}
.card.sm .c-title{font-size:13.5px}
.card.sm .c-pitch{font-size:12.5px}
.card.sm .c-note{font-size:13px}

/* note per world, and the summary */
.w-note{margin-top:30px;display:grid;gap:6px;max-width:760px}
.w-note label{font-size:13px;color:var(--muted)}
textarea{width:100%;max-width:100%;font:14px/1.45 var(--font-ui);color:var(--ink);background:var(--surface);border:1px solid var(--line);border-radius:var(--radius);padding:9px 11px;resize:vertical}
.out{padding-block:36px 28px;border-top:1px solid var(--line);display:grid;gap:12px;max-width:820px}
.out h2{margin:0;font:600 24px/1.2 var(--font-display)}
.out textarea{font:12.5px/1.5 var(--font-mono);min-height:260px}
.out-row{display:flex;flex-wrap:wrap;gap:10px 14px;align-items:center}
.out small,.foot{font-size:13px;color:var(--muted)}
.foot{padding-block:8px 48px}

/* large view */
.lb{position:fixed;inset:0;z-index:100;display:grid;grid-template-rows:auto minmax(0,1fr) auto;gap:8px;background:var(--lb-bg);color:var(--lb-ink);
  padding:max(10px,env(safe-area-inset-top,0px)) max(12px,env(safe-area-inset-right,0px)) max(12px,env(safe-area-inset-bottom,0px)) max(12px,env(safe-area-inset-left,0px))}
.lb-top{display:flex;justify-content:space-between;align-items:center;gap:12px;font-size:13px;color:var(--lb-muted);font-variant-numeric:tabular-nums}
.lb .btn{background:transparent;border-color:var(--lb-line);color:var(--lb-ink)}
.lb .btn.pick[aria-pressed="true"]{background:var(--accent);border-color:var(--accent);color:var(--accent-ink)}
.lb .btn.like[aria-pressed="true"]{background:var(--accent-soft);border-color:var(--accent);color:var(--ink)}
.lb :focus-visible{outline-color:var(--lb-ink)}
.lb-stage{position:relative;min-height:0}
.lb-stage img{position:absolute;inset:0;width:100%;height:100%;object-fit:contain}
.lb-nav{position:absolute;top:50%;transform:translateY(-50%);width:44px;height:68px;padding:0;border:0;border-radius:6px;background:var(--lb-btn);color:var(--lb-ink);font:400 28px/1 var(--font-ui);cursor:pointer}
.lb-prev{left:0}
.lb-next{right:0}
.lb-cap{width:100%;max-width:920px;margin:0 auto;display:grid;gap:6px;max-height:42vh;overflow-y:auto;font-size:14px}
.lb-head{display:flex;flex-wrap:wrap;gap:4px 10px;align-items:baseline}
.lb-head b{font:600 13px/1.4 var(--font-mono)}
.lb-head span{color:var(--lb-muted);font-size:13px}
.lb-cap .c-prompt{color:var(--lb-muted);margin-top:2px}
</style>

<header class="bar">
  <div class="in">
    <div class="bar-t"><b>Style picks</b><span id="count"></span></div>
    <nav class="chips" id="chips" aria-label="Worlds and your pick for each"></nav>
    <button class="btn go" id="copyTop" type="button">Copy my picks</button>
  </div>
</header>

<main class="in">
  <section class="intro">
    <h1>Realm style exploration</h1>
    <p id="lede">Concept images for nine worlds and the overworld. Nothing here changes a shipped asset; it is a choice of direction for each world.</p>
    <p class="how">For each world, choose one picture as your pick (or keep what ships), mark anything else you like, and leave a note. Then press Copy my picks and paste the text back into the chat. Click any picture to see it large. Recommended and Runner-up are the art director's calls; the reasoning is in REVIEW.md.</p>
    <noscript><p>This page needs JavaScript to show the pictures.</p></noscript>
  </section>
  <div id="worlds"></div>
  <section class="out" id="out">
    <h2>Your picks</h2>
    <textarea id="summary" readonly rows="14" aria-label="Your picks as plain text"></textarea>
    <div class="out-row">
      <button class="btn go" id="copyBottom" type="button">Copy my picks</button>
      <small id="copyStatus" role="status"></small>
    </div>
    <small id="syncNote">Your picks are remembered in this browser.</small>
  </section>
  <p class="foot" id="foot"></p>
</main>

<div class="lb" id="lb" role="dialog" aria-modal="true" aria-label="Picture, large" hidden>
  <div class="lb-top">
    <span id="lbCount"></span>
    <button class="btn" id="lbClose" type="button">Close</button>
  </div>
  <div class="lb-stage" id="lbStage">
    <img id="lbImg" alt="">
    <button class="lb-nav lb-prev" id="lbPrev" type="button" aria-label="Previous picture">&lsaquo;</button>
    <button class="lb-nav lb-next" id="lbNext" type="button" aria-label="Next picture">&rsaquo;</button>
  </div>
  <div class="lb-cap">
    <div class="lb-head"><b id="lbId"></b><span id="lbTitle"></span><span id="lbModel"></span></div>
    <p id="lbNote"></p>
    <div class="c-act">
      <button class="btn pick" id="lbPick" type="button"></button>
      <button class="btn like" id="lbLike" type="button"></button>
    </div>
    <details class="c-prompt" id="lbPrompt"><summary>Prompt</summary><p id="lbPromptText"></p></details>
  </div>
</div>

<script type="application/json" id="data">__DATA__</script>
<script>
(function () {
  'use strict';
  function byId(id) { return document.getElementById(id); }
  function all(sel) { return Array.prototype.slice.call(document.querySelectorAll(sel)); }
  function el(tag, cls, text) {
    var e = document.createElement(tag);
    if (cls) e.className = cls;
    if (text != null) e.textContent = text;
    return e;
  }

  var DATA;
  try { DATA = JSON.parse(byId('data').textContent); }
  catch (err) { byId('worlds').textContent = 'The picture list could not be read.'; return; }

  // Lookups: every picture by key, its world, and the order the large view walks through.
  var items = {}, worldOf = {}, seq = [];
  DATA.worlds.forEach(function (w) {
    if (w.ref) seq.push({ key: 'ref:' + w.code, w: w, ref: true });
    w.groups.forEach(function (g) {
      g.items.forEach(function (it) { items[it.key] = it; worldOf[it.key] = w; seq.push({ key: it.key, w: w, it: it }); });
    });
  });

  // ---- state: one pick per world, any number of likes, a note per world -------------
  var STORE = 'fableland.stylepicks.v1';
  function clean(o) {
    var s = { v: 1, t: (o && typeof o.t === 'number') ? o.t : 0, picks: {}, likes: {}, notes: {} };
    if (!o) return s;
    DATA.worlds.forEach(function (w) {
      var p = o.picks && o.picks[w.code];
      if (p === 'ships' || (typeof p === 'string' && worldOf[p] === w)) s.picks[w.code] = p;
      var n = o.notes && o.notes[w.code];
      if (typeof n === 'string' && n) s.notes[w.code] = n.slice(0, 2000);
    });
    if (o.likes) Object.keys(o.likes).forEach(function (k) { if (items[k] && o.likes[k]) s.likes[k] = true; });
    return s;
  }
  var state = clean(null);
  try { var raw = window.localStorage.getItem(STORE); if (raw) state = clean(JSON.parse(raw)); } catch (err) {}
  function save() { try { window.localStorage.setItem(STORE, JSON.stringify(state)); } catch (err) {} }
  function touch() { state.t = Date.now(); save(); refresh(); pushSoon(); }
  function setPick(code, key) { if (state.picks[code] === key) delete state.picks[code]; else state.picks[code] = key; touch(); }
  function toggleLike(key) { if (state.likes[key]) delete state.likes[key]; else state.likes[key] = true; touch(); }

  // ---- text ---------------------------------------------------------------------------
  // The page is kept ASCII-only, so punctuation outside ASCII is built from its code.
  var DOT = String.fromCharCode(183), EN = String.fromCharCode(8211), EM = String.fromCharCode(8212);
  function shortId(it) { return it.variant ? it.id + ' ' + DOT + ' mj' + it.variant : it.id; }
  function chipText(w) {
    var p = state.picks[w.code];
    if (!p) return EN;
    if (p === 'ships') return 'keep';
    return items[p].letter + (items[p].variant ? DOT + 'mj' + items[p].variant : '');
  }
  function two(n) { return (n < 10 ? '0' : '') + n; }
  function summary() {
    var d = new Date();
    var lines = ['Fableland style picks ' + EM + ' ' + d.getFullYear() + '-' + two(d.getMonth() + 1) + '-' + two(d.getDate()) + ' ' + two(d.getHours()) + ':' + two(d.getMinutes())];
    DATA.worlds.forEach(function (w) {
      var p = state.picks[w.code], pick = '(no pick yet)', likes = [];
      if (p === 'ships') pick = 'keep what ships';
      else if (p) pick = p + ' ' + EM + ' ' + items[p].title + ' (' + items[p].model + ')';
      w.groups.forEach(function (g) { g.items.forEach(function (it) { if (state.likes[it.key] && it.key !== p) likes.push(it.key); }); });
      lines.push('', w.code + '  ' + w.name, '  pick: ' + pick);
      if (likes.length) lines.push('  also like: ' + likes.join(', '));
      if (state.notes[w.code]) lines.push('  note: ' + state.notes[w.code].replace(/\s+/g, ' ').trim());
    });
    return lines.join('\n');
  }

  // ---- building the page --------------------------------------------------------------
  function pickBtn(code, key) {
    var b = el('button', 'btn pick'); b.type = 'button';
    b.setAttribute('data-pick', key); b.setAttribute('data-world', code);
    b.addEventListener('click', function () { setPick(code, key); });
    return b;
  }
  function likeBtn(key) {
    var b = el('button', 'btn like'); b.type = 'button';
    b.setAttribute('data-like', key);
    b.addEventListener('click', function () { toggleLike(key); });
    return b;
  }
  function shot(src, w, h, alt, key) {
    var b = el('button', 'shot'); b.type = 'button';
    b.style.aspectRatio = w + ' / ' + h;
    b.setAttribute('aria-label', 'Open large: ' + alt);
    var img = el('img'); img.loading = 'lazy'; img.decoding = 'async'; img.width = w; img.height = h; img.alt = alt; img.src = src;
    b.appendChild(img);
    b.addEventListener('click', function () { openLb(key, b); });
    return b;
  }
  function card(it, w, small) {
    var a = el('article', 'card' + (small ? ' sm' : ''));
    a.setAttribute('data-key', it.key);
    a.appendChild(shot(it.src, it.w, it.h, shortId(it) + ', ' + it.title, it.key));
    var top = el('div', 'c-top');
    top.appendChild(el('span', 'c-id', shortId(it)));
    top.appendChild(el('span', 'c-model', it.model));
    if (w.pick === it.key) top.appendChild(el('span', 'tag rec', 'Recommended'));
    else if (w.runner === it.key) top.appendChild(el('span', 'tag', 'Runner-up'));
    a.appendChild(top);
    a.appendChild(el('h4', 'c-title', it.title));
    a.appendChild(el('p', 'c-pitch', it.pitch));
    a.appendChild(el('p', 'c-note', it.note));
    var act = el('div', 'c-act');
    act.appendChild(pickBtn(w.code, it.key));
    act.appendChild(likeBtn(it.key));
    a.appendChild(act);
    var d = el('details', 'c-prompt');
    d.appendChild(el('summary', null, 'Prompt'));
    d.appendChild(el('p', null, DATA.prompts[it.id] || ''));
    a.appendChild(d);
    return a;
  }
  var noteFields = {};
  function world(w) {
    var s = el('section', 'world'); s.id = 'w-' + w.code;
    var head = el('header', 'w-head' + (w.ref ? ' has-ref' : '')), text = el('div', 'w-text');
    text.appendChild(el('p', 'w-code', w.code));
    text.appendChild(el('h2', null, w.name));
    var med = el('p', 'w-medium');
    med.appendChild(el('span', 'k', w.mediumLabel));
    med.appendChild(el('span', null, w.medium));
    if (w.canvas) {
      var sw = el('span', 'swatch'); sw.style.background = w.canvas; sw.title = 'Canvas colour in the style bible';
      med.appendChild(sw); med.appendChild(el('span', 'hex', w.canvas));
    }
    text.appendChild(med);
    var v = el('div', 'w-verdict');
    v.appendChild(el('p', 'k', 'Art director'));
    w.verdict.forEach(function (line) { v.appendChild(el('p', null, line)); });
    text.appendChild(v);
    head.appendChild(text);
    var keep = pickBtn(w.code, 'ships');
    if (w.ref) {
      var fig = el('figure', 'w-ref');
      fig.appendChild(shot(w.ref.src, w.ref.w, w.ref.h, w.name + ', what ships today', 'ref:' + w.code));
      var cap = el('figcaption');
      cap.appendChild(el('span', 'k', 'Ships today'));
      cap.appendChild(document.createTextNode(' ' + w.ships));
      fig.appendChild(cap); fig.appendChild(keep);
      head.appendChild(fig);
    } else {
      text.appendChild(keep);
    }
    s.appendChild(head);
    w.groups.forEach(function (g) {
      if (g.label) s.appendChild(el('h3', 'g-label', g.label));
      var grid = el('div', 'grid ' + g.kind);
      grid.setAttribute('data-n', g.items.length);
      g.items.forEach(function (it) { grid.appendChild(card(it, w, g.kind === 'mj')); });
      s.appendChild(grid);
    });
    var nf = el('div', 'w-note');
    var lab = el('label', null, 'Your note on ' + w.code + ': anything to carry across, such as a palette, a motif or a level of looseness');
    var ta = el('textarea'); ta.id = 'note-' + w.code; ta.rows = 2; lab.htmlFor = ta.id;
    ta.value = state.notes[w.code] || '';
    ta.addEventListener('input', function () {
      if (ta.value) state.notes[w.code] = ta.value.slice(0, 2000); else delete state.notes[w.code];
      state.t = Date.now(); save(); byId('summary').value = summary(); pushSoon();
    });
    noteFields[w.code] = ta;
    nf.appendChild(lab); nf.appendChild(ta);
    s.appendChild(nf);
    return s;
  }
  function fillNotes() {
    Object.keys(noteFields).forEach(function (code) {
      if (document.activeElement !== noteFields[code]) noteFields[code].value = state.notes[code] || '';
    });
  }
  function jump(code) {
    var calm = false;
    try { calm = window.matchMedia('(prefers-reduced-motion: reduce)').matches; } catch (err) {}
    byId('w-' + code).scrollIntoView({ behavior: calm ? 'auto' : 'smooth', block: 'start' });
  }
  var chipEls = {};
  DATA.worlds.forEach(function (w) {
    byId('worlds').appendChild(world(w));
    var c = el('a', 'chip', w.code); c.href = '#w-' + w.code;
    var val = el('span'); c.appendChild(val);
    c.addEventListener('click', function (ev) { ev.preventDefault(); jump(w.code); });
    chipEls[w.code] = { a: c, val: val };
    byId('chips').appendChild(c);
  });
  byId('lede').textContent = DATA.images + ' concept images for nine worlds and the overworld. Nothing here changes a shipped asset; it is a choice of direction for each world.';
  byId('foot').textContent = 'Built ' + DATA.built + ' from manifest.json, prompts.json and review.json by Tools/style_exploration/build_gallery.py.';

  function refresh() {
    var n = 0;
    DATA.worlds.forEach(function (w) {
      var on = !!state.picks[w.code]; if (on) n++;
      chipEls[w.code].val.textContent = chipText(w);
      chipEls[w.code].a.className = 'chip' + (on ? ' on' : '');
      chipEls[w.code].a.setAttribute('aria-label', w.name + ': ' + (on ? 'picked ' + chipText(w) : 'no pick yet'));
    });
    all('[data-pick]').forEach(function (b) {
      var key = b.getAttribute('data-pick'), on = state.picks[b.getAttribute('data-world')] === key;
      b.setAttribute('aria-pressed', on ? 'true' : 'false');
      b.textContent = key === 'ships' ? (on ? 'Keeping what ships' : 'Keep what ships') : (on ? 'Your pick' : 'Pick');
    });
    all('[data-like]').forEach(function (b) {
      var on = !!state.likes[b.getAttribute('data-like')];
      b.setAttribute('aria-pressed', on ? 'true' : 'false');
      b.textContent = on ? 'Also liked' : 'Also like';
    });
    all('.card').forEach(function (c) {
      var key = c.getAttribute('data-key');
      c.classList.toggle('is-pick', state.picks[worldOf[key].code] === key);
    });
    byId('count').textContent = n + ' of ' + DATA.worlds.length + ' picked';
    byId('summary').value = summary();
  }

  // ---- copy -----------------------------------------------------------------------------
  function copyPicks(btn) {
    var box = byId('summary'), status = byId('copyStatus'), text = summary();
    box.value = text;
    function ok() {
      status.textContent = 'Copied. Paste it into the chat.';
      var was = btn.textContent; btn.textContent = 'Copied';
      window.setTimeout(function () { btn.textContent = was; }, 1600);
    }
    function byHand() {
      var done = false;
      try { box.focus(); box.select(); box.setSelectionRange(0, text.length); done = document.execCommand('copy'); } catch (err) {}
      if (done) { ok(); return; }
      status.textContent = 'Could not copy automatically. The text is selected in the box: copy it by hand.';
      byId('out').scrollIntoView({ block: 'center' });
    }
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) { navigator.clipboard.writeText(text).then(ok, byHand); return; }
    } catch (err) {}
    byHand();
  }
  byId('copyTop').addEventListener('click', function () { copyPicks(byId('copyTop')); });
  byId('copyBottom').addEventListener('click', function () { copyPicks(byId('copyBottom')); });

  // ---- large view -------------------------------------------------------------------------
  var lb = byId('lb'), lbAt = 0, lbFrom = null;
  function show(i) {
    lbAt = (i + seq.length) % seq.length;
    var s = seq[lbAt], w = s.w, it = s.it;
    byId('lbCount').textContent = (lbAt + 1) + ' / ' + seq.length + ' ' + DOT + ' ' + w.name;
    byId('lbImg').src = s.ref ? w.ref.src : it.src;
    byId('lbImg').alt = s.ref ? w.name + ', what ships today' : shortId(it) + ', ' + it.title;
    byId('lbId').textContent = s.ref ? w.code + ' ' + DOT + ' ships today' : shortId(it);
    byId('lbTitle').textContent = s.ref ? 'In the game now' : it.title;
    byId('lbModel').textContent = s.ref ? '' : it.model;
    byId('lbNote').textContent = s.ref ? w.ships : it.note;
    byId('lbPick').setAttribute('data-world', w.code);
    byId('lbPick').setAttribute('data-pick', s.ref ? 'ships' : it.key);
    byId('lbLike').hidden = !!s.ref;
    byId('lbLike').setAttribute('data-like', s.ref ? '' : it.key);
    byId('lbPrompt').hidden = !!s.ref;
    byId('lbPrompt').open = false;
    byId('lbPromptText').textContent = s.ref ? '' : (DATA.prompts[it.id] || '');
    refresh();
  }
  function openLb(key, from) {
    for (var i = 0; i < seq.length; i++) if (seq[i].key === key) break;
    if (i === seq.length) return;
    lbFrom = from || null;
    lb.hidden = false; document.body.classList.add('lb-open');
    show(i); byId('lbClose').focus();
  }
  function closeLb() {
    lb.hidden = true; document.body.classList.remove('lb-open');
    byId('lbImg').removeAttribute('src');
    if (lbFrom && lbFrom.focus) lbFrom.focus();
  }
  byId('lbClose').addEventListener('click', closeLb);
  byId('lbPrev').addEventListener('click', function () { show(lbAt - 1); });
  byId('lbNext').addEventListener('click', function () { show(lbAt + 1); });
  byId('lbPick').addEventListener('click', function () { setPick(this.getAttribute('data-world'), this.getAttribute('data-pick')); });
  byId('lbLike').addEventListener('click', function () { toggleLike(this.getAttribute('data-like')); });
  document.addEventListener('keydown', function (ev) {
    if (lb.hidden) return;
    if (ev.key === 'Escape') closeLb();
    else if (ev.key === 'ArrowLeft') show(lbAt - 1);
    else if (ev.key === 'ArrowRight') show(lbAt + 1);
  });
  var sx = 0, sy = 0;
  byId('lbStage').addEventListener('touchstart', function (ev) { sx = ev.changedTouches[0].clientX; sy = ev.changedTouches[0].clientY; }, { passive: true });
  byId('lbStage').addEventListener('touchend', function (ev) {
    var dx = ev.changedTouches[0].clientX - sx, dy = ev.changedTouches[0].clientY - sy;
    if (Math.abs(dx) > 48 && Math.abs(dx) > 1.5 * Math.abs(dy)) show(lbAt + (dx < 0 ? 1 : -1));
  }, { passive: true });

  // ---- optional: keep the picks with the published page (Claude artifact "db") -----------
  // Opened from disk there is no window.claude and the picks simply stay in this browser.
  var remote = null, writing = false, dirty = false, timer = 0;
  function setSync(on) {
    byId('syncNote').textContent = on
      ? 'Your picks are saved with this page and follow you to your other devices.'
      : 'Your picks are remembered in this browser.';
  }
  function push() {
    if (!remote) return;
    if (writing) { dirty = true; return; }
    writing = true; dirty = false;
    var body = JSON.parse(JSON.stringify(state)); body.summary = summary();
    remote.set(body).then(function () { writing = false; if (dirty) push(); },
                          function () { writing = false; remote = null; setSync(false); });
  }
  function pushSoon() { if (!remote) return; window.clearTimeout(timer); timer = window.setTimeout(push, 900); }
  function connect() {
    var c = window.claude, p;
    if (!c || typeof c.use !== 'function') return;
    try { p = c.use('db'); } catch (err) { return; }
    Promise.resolve(p).then(function (db) {
      if (!db) return;
      var ref = db.doc('picks/owner'), first = true;
      ref.onSnapshot(function (snap) {
        if (snap.metadata && snap.metadata.hasPendingWrites) return;
        var d = snap.exists ? snap.data() : null;
        if (d && typeof d.t === 'number' && d.t > state.t) { state = clean(d); save(); fillNotes(); refresh(); }
        if (first) {
          first = false; remote = ref; setSync(true);
          if (state.t && !(d && d.t >= state.t)) push();
        }
      }, function () { remote = null; setSync(false); });
    }).catch(function () {});
  }

  refresh();
  connect();
})();
</script>
'''


if __name__ == '__main__':
    sys.exit(build())
