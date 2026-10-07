"""lote.py NOME [NOME...] -- grava as tomadas pedidas, UMA DE CADA VEZ (segundo monitor), e faz a folha de contato de cada."""
import json, subprocess, sys
from pathlib import Path
from tomadas import TOMADAS, LOTES

AQUI = Path(__file__).resolve().parent
UM = r"C:\Users\Joao\.claude\plugins\marketplaces\local-desktop-app-uploads\universal-modder\bin\um"
porta = 7860

nomes = []
for a in sys.argv[1:]:
    nomes += LOTES[a[5:]] if a.startswith('lote:') else [a]

for i, nome in enumerate(nomes):
    print(f'=== {nome} ({i + 1}/{len(nomes)})', flush=True)
    t = TOMADAS[nome]
    conta = "trailer_" + "".join(c if c.isalpha() else "_" for c in nome)
    rede = str(porta + sum(map(ord, nome)) % 35)
    if t.get("cru"):
        cmd = [sys.executable, str(AQUI / "gravar.py"), nome, "--secs", str(t["secs"]), "--zoom", str(t["zoom"]), "--marker", t["marker"]]
        if t.get("server"): cmd += ["--server", t["server"], "--espera", str(t.get("espera", 14))]
        if t.get("convidado"): cmd += ["--convidado", t["convidado"], "--atraso", str(t.get("atraso", 12))]
        cmd += ["--", *t["cliente"]]
    else:
        cmd = [sys.executable, str(AQUI / "gravar.py"), nome, "--secs", str(t["secs"]), "--zoom", str(t["zoom"])]
        if t.get("convidado"): cmd += ["--convidado", t["convidado"].replace("{rede}", rede), "--atraso", str(t.get("atraso", 12))]
        cmd += ["--", "--host", "--rede", rede, *t["flags"], "--trailer", t["cena"]]
        if t["arg"]:
            cmd += ["--trailerarg", t["arg"]]
        cmd += ["--raca", t["raca"], "--conta", conta, "--nome", t["nome"], *t["extra"]]
    r = subprocess.run(cmd, cwd=AQUI, capture_output=True, text=True)
    print((r.stdout or "").strip(), flush=True)
    mk = AQUI / "takes" / f"{nome}.marcas.json"
    if mk.exists():
        for m in json.loads(mk.read_text(encoding="utf-8"))["marcas"]:
            print(f"   {m['video']:7.2f}  {m['oque']}", flush=True)
    log = (AQUI / "takes" / f"{nome}.log").read_text(encoding="utf-8", errors="replace")
    for ln in log.split("\n"):
        if ln.startswith("[trailer]") and "@" not in ln[:12] or "RECUSADO" in ln or "ERROR" in ln[:8] or "Exception" in ln:
            print("   !! " + ln[:300], flush=True)
    video = AQUI / "takes" / f"{nome}.mkv"
    if video.exists():
        subprocess.run(["bash", UM.replace("\\", "/"), "video", "contact", str(video), str(AQUI / "sheets" / f"{nome}.png"), "--every", "3", "--cols", "6"],
                       capture_output=True, text=True)
