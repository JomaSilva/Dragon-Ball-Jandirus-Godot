"""revisar.py TOMADA [--de S] [--ate S] [--passo S] [--cols N] [--w LARG] -- folha de quadros de uma tomada, com as marcas do diretor por cima."""
import json, subprocess, sys
from pathlib import Path
from PIL import Image, ImageDraw
a = sys.argv[1:]
def opt(k, d):
    if k in a:
        i = a.index(k); v = a[i + 1]; del a[i:i + 2]; return type(d)(v)
    return d
de, ate, passo, cols, w = opt("--de", 0.0), opt("--ate", -1.0), opt("--passo", 3.0), opt("--cols", 4), opt("--w", 480)
nome = a[0]
dur = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", f"takes/{nome}.mkv"], capture_output=True, text=True).stdout.strip())
if ate < 0: ate = dur
mk = Path(f"takes/{nome}.marcas.json")
marcas = json.loads(mk.read_text(encoding="utf-8"))["marcas"] if mk.exists() else []
ts = []
t = de
while t < ate - 0.05:
    ts.append(t); t += passo
h = w * 9 // 16
rows = (len(ts) + cols - 1) // cols
sheet = Image.new("RGB", (cols * w, rows * h)); dr = ImageDraw.Draw(sheet)
Path("quadros").mkdir(exist_ok=True)
for i, t in enumerate(ts):
    f = f"quadros/r_{nome}_{i}.png"
    subprocess.run(["ffmpeg", "-v", "error", "-y", "-ss", f"{t:.3f}", "-i", f"takes/{nome}.mkv", "-frames:v", "1", "-vf", f"scale={w}:{h}:flags=lanczos", f])
    x, y = (i % cols) * w, (i // cols) * h
    try: sheet.paste(Image.open(f), (x, y))
    except Exception: pass
    rot = f"{t:.1f}s"
    perto = [m for m in marcas if t - passo < m["video"] <= t]
    if perto: rot += " | " + perto[-1]["oque"][:44]
    dr.rectangle((x, y, x + 8 + len(rot) * 6, y + 13), fill=(0, 0, 0)); dr.text((x + 3, y + 1), rot, fill=(255, 255, 0))
out = f"sheets/r_{nome}.jpg"
sheet.save(out, quality=84)
print(out, sheet.size, f"{dur:.1f}s")
for m in marcas: print(f"  {m['video']:7.2f}  {m['oque'][:150]}")
