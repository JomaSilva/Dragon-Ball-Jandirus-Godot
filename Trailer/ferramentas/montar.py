"""montar.py -- monta o trailer a partir do roteiro (`roteiro.py`), cortando NA BATIDA da musica.

    python montar.py [--previa] [--de N --ate M] [--saida ARQ]

Cada plano do roteiro dura um numero de BATIDAS; a fronteira de cada corte e arredondada pro quadro mais proximo do
instante da batida (e nao somada plano a plano), entao o erro nunca passa de meio quadro e nao acumula ao longo dos
quatro minutos. Cortes secos, emendados sem recodificar; a musica entra por cima no fim.
"""
import argparse, json, shutil, subprocess, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont

AQUI = Path(__file__).resolve().parent
TAKES = AQUI / "takes"
MUSICA = r"E:\Users\Joao\Desktop\dragon-ball-jandirus\Assets\Sounds\Music\Menu ost\Abertura DRAGON BALL KAI DUBLADO (COMPLETA)!!.mp3"
BPM, T0 = 161.5, 0.2675            # medidos em batidas.py
BATIDA = 60.0 / BPM
IMPACT = r"C:\Windows\Fonts\impact.ttf"
W, H = 1920, 1080


def ff(*args):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-v", "error", "-y", *[str(a) for a in args]], capture_output=True, text=True)
    if r.returncode != 0:
        sys.exit("ffmpeg falhou:\n" + " ".join(str(a) for a in args)[:1500] + "\n" + r.stderr[-2500:])


_marcas = {}


def marca(tomada, trecho, n=0):
    """o instante (no VIDEO da tomada) da n-esima marca do diretor que contem `trecho`"""
    if tomada not in _marcas:
        f = TAKES / f"{tomada}.marcas.json"
        _marcas[tomada] = json.loads(f.read_text(encoding="utf-8"))["marcas"] if f.exists() else []
    achadas = [m["video"] for m in _marcas[tomada] if trecho in m["oque"]]
    if len(achadas) <= n:
        if TOLERANTE:
            FALTAS.append(f"{tomada}: `{trecho}` (n={n})")
            return float("nan")
        sys.exit(f"a tomada `{tomada}` nao tem a marca `{trecho}` (n={n})")
    return achadas[n]


TOLERANTE, FALTAS = False, []


def legenda(texto, saida, escala=1.0):
    """a legenda do plano: letras brancas inclinadas com contorno escuro e uma barra laranja, embaixo a esquerda"""
    tam = round(64 * escala)
    f = ImageFont.truetype(IMPACT, tam)
    img = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    tmp = ImageDraw.Draw(img)
    larg = tmp.textlength(texto, font=f)
    x0, y0 = round(84 * escala), H - round(150 * escala)
    camada = Image.new("L", (W, H), 0)
    ImageDraw.Draw(camada).text((x0, y0), texto, font=f, fill=255)
    camada = camada.transform(camada.size, Image.AFFINE, (1, 0.14, -0.14 * (y0 + tam / 2), 0, 1, 0), resample=Image.BICUBIC)
    borda = camada.filter(ImageFilter.MaxFilter(11))
    sombra = Image.new("RGBA", (W, H), (0, 0, 0, 0))
    sombra.paste((0, 0, 0, 190), (5, 6), borda)
    img.alpha_composite(sombra.filter(ImageFilter.GaussianBlur(5)))
    img.paste((16, 12, 44, 255), (0, 0), borda)
    img.paste((255, 255, 255, 255), (0, 0), camada)
    d = ImageDraw.Draw(img)
    yb = y0 + tam + round(18 * escala)
    d.polygon([(x0 - 6, yb), (x0 + larg + 34, yb), (x0 + larg + 26, yb + 12), (x0 - 14, yb + 12)], fill=(255, 150, 20, 255))
    d.polygon([(x0 - 6, yb), (x0 + larg + 34, yb), (x0 + larg + 31, yb + 4), (x0 - 9, yb + 4)], fill=(255, 225, 90, 255))
    img.save(saida)


def render(i, p, quadros, fps, largura, altura, pasta, previa):
    """um plano -> `quadros` quadros exatos em `pasta/p_iii.mp4`"""
    out = pasta / f"p_{i:03d}.mp4"
    dur = quadros / fps
    enc = (["-c:v", "libx264", "-preset", "veryfast", "-crf", "27"] if previa else ["-c:v", "libx264", "-preset", "medium", "-crf", "16"]) \
        + ["-pix_fmt", "yuv420p", "-r", str(fps), "-g", str(fps), "-video_track_timescale", "15360", "-an"]
    filtros, entradas = [], []

    if "tomada" in p and (p["em"] != p["em"] or not (TAKES / f"{p['tomada']}.mkv").exists()):
        p = dict(cor="0x301010", batidas=p["batidas"])      # FALTA a tomada ou a marca: um plano vermelho-escuro no lugar
    if "cartao" in p:
        entradas = ["-loop", "1", "-framerate", fps, "-t", f"{dur + 0.5:.3f}", "-i", p["cartao"]]
        z = p.get("empurra", 0.05)
        filtros.append(f"scale={W * 2}:{H * 2},zoompan=z='1+{z}*on/{quadros}':x='iw/2-(iw/zoom/2)':y='ih/2-(ih/zoom/2)':d={quadros + 30}:s={W}x{H}:fps={fps}")
    elif "cor" in p:
        entradas = ["-f", "lavfi", "-t", f"{dur + 0.5:.3f}", "-i", f"color=c={p['cor']}:s={W}x{H}:r={fps}"]
    else:
        vel = float(p.get("vel", 1.0))
        ini = p["em"]
        entradas = ["-ss", f"{max(0, ini):.3f}", "-t", f"{dur * vel + 1.0:.3f}", "-i", TAKES / f"{p['tomada']}.mkv"]
        filtros.append(f"setpts=(PTS-STARTPTS)/{vel}" if vel != 1.0 else "setpts=PTS-STARTPTS")
        filtros.append(f"fps={fps}")
        zoom = float(p.get("zoom", 1.0))
        if zoom != 1.0 or "centro" in p:
            cw, ch = round(W / zoom / 2) * 2, round(H / zoom / 2) * 2
            cx, cy = p.get("centro", (0.5, 0.5))
            x = min(max(0, round(cx * W - cw / 2)), W - cw)
            y = min(max(0, round(cy * H - ch / 2)), H - ch)
            filtros.append(f"crop={cw}:{ch}:{x}:{y},scale={W}:{H}:flags=neighbor")
        if p.get("claro"):
            c = p["claro"]
            filtros.append(f"eq=brightness={c[0]}:contrast={c[1]}:saturation={c[2] if len(c) > 2 else 1.0}:gamma={c[3] if len(c) > 3 else 1.0}")

    grafo = f"[0:v]{','.join(filtros) if filtros else 'null'}[b]"
    ult, n = "b", 1
    if p.get("texto"):
        png = pasta / f"t_{i:03d}.png"
        legenda(p["texto"], png)
        entradas += ["-loop", "1", "-framerate", fps, "-t", f"{dur + 0.5:.3f}", "-i", png]
        a0 = p.get("texto_em", 0.12)
        a1 = max(a0 + 0.5, dur - 0.22)
        grafo += f";[{n}:v]format=rgba,fade=t=in:st={a0:.2f}:d=0.18:alpha=1,fade=t=out:st={a1 - 0.18:.2f}:d=0.18:alpha=1[t];[{ult}][t]overlay=x='-60*pow(1-min(max((t-{a0:.2f})/0.3\\,0)\\,1)\\,3)':y=0[o]"
        ult, n = "o", n + 1
    fim = []
    if p.get("entra"):          # ("white"|"black", segundos)
        fim.append(f"fade=t=in:st=0:d={p['entra'][1]}:color={p['entra'][0]}")
    if p.get("sai"):
        fim.append(f"fade=t=out:st={dur - p['sai'][1]:.3f}:d={p['sai'][1]}:color={p['sai'][0]}")
    fim.append(f"scale={largura}:{altura}:flags=lanczos" if (largura, altura) != (W, H) else "null")
    fim.append("format=yuv420p")
    grafo += f";[{ult}]{','.join(fim)}[v]"
    ff(*entradas, "-filter_complex", grafo, "-map", "[v]", "-frames:v", quadros, *enc, out)
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--previa", action="store_true", help="meia resolucao, 30 fps, rapido")
    ap.add_argument("--de", type=int, default=0)
    ap.add_argument("--ate", type=int, default=10 ** 6)
    ap.add_argument("--saida", default=None)
    ap.add_argument("--roteiro", default="roteiro")
    ap.add_argument("--semear", default=None, help="indices a refazer (a,b,c); os outros planos ja renderizados sao aceitos como estao")
    ap.add_argument("--tolerante", action="store_true", help="plano sem tomada/marca vira um plano liso em vez de parar a montagem")
    a = ap.parse_args()
    global TOLERANTE
    TOLERANTE = a.tolerante

    planos = __import__(a.roteiro).roteiro(marca)
    fps = 30 if a.previa else 60
    larg, alt = (960, 540) if a.previa else (W, H)
    pasta = AQUI / ("planos_previa" if a.previa else "planos")
    pasta.mkdir(exist_ok=True)

    # as fronteiras, em quadros, cravadas na grade de batidas
    b, cortes = 0.0, [0]
    for p in planos:
        b += p["batidas"]
        cortes.append(round((T0 + b * BATIDA) * fps))
    total_s = cortes[-1] / fps
    print(f"{len(planos)} planos, {b:.0f} batidas ({b / 4:.1f} compassos), {total_s:.2f} s")

    # SO REFAZ O PLANO QUE MUDOU: a ficha de cada plano renderizado fica guardada ao lado dele
    fichas_arq = pasta / "fichas.json"
    fichas = json.loads(fichas_arq.read_text(encoding="utf-8")) if fichas_arq.exists() else {}
    if a.semear is not None:
        # os planos ja renderizados por uma versao sem fichas valem como estao, menos os listados
        refazer = {int(x) for x in a.semear.split(",") if x.strip()}
        for i, p in enumerate(planos):
            if i not in refazer and (pasta / f"p_{i:03d}.mp4").exists():
                fichas[str(i)] = json.dumps([p, cortes[i + 1] - cortes[i]], sort_keys=True, default=str)

    arquivos, ini = [], 0.0
    for i, p in enumerate(planos):
        quadros = cortes[i + 1] - cortes[i]
        if a.de <= i <= a.ate:
            if quadros <= 0:
                sys.exit(f"plano {i} sem duracao")
            ficha = json.dumps([p, quadros], sort_keys=True, default=str)
            f = pasta / f"p_{i:03d}.mp4"
            if fichas.get(str(i)) != ficha or not f.exists():
                f = render(i, p, quadros, fps, larg, alt, pasta, a.previa)
                fichas[str(i)] = ficha
                fichas_arq.write_text(json.dumps(fichas), encoding="utf-8")
            arquivos.append(f)
            print(f"  {i:3d}  {cortes[i] / fps:7.2f}s  {quadros / fps:5.2f}s  {p.get('tomada', p.get('cartao', p.get('cor', '')))!s:<28.28}  {p.get('texto', '')}", flush=True)
            if not arquivos[:-1]:
                ini = cortes[i] / fps

    lista = pasta / "lista.txt"
    lista.write_text("".join(f"file '{f.as_posix()}'\n" for f in arquivos), encoding="utf-8")
    mudo = pasta / "mudo.mp4"
    ff("-f", "concat", "-safe", "0", "-i", lista, "-c", "copy", mudo)
    dur = sum(cortes[i + 1] - cortes[i] for i in range(len(planos)) if a.de <= i <= a.ate) / fps
    saida = Path(a.saida) if a.saida else AQUI / ("trailer_previa.mp4" if a.previa else "trailer.mp4")
    some = 2.5 if a.ate >= len(planos) - 1 else 0.3
    ff("-i", mudo, "-ss", f"{ini:.3f}", "-t", f"{dur:.3f}", "-i", MUSICA, "-map", "0:v", "-map", "1:a", "-c:v", "copy",
       "-af", f"afade=t=in:st=0:d=0.05,afade=t=out:st={max(0, dur - some):.3f}:d={some}", "-c:a", "aac", "-b:a", "256k", "-t", f"{dur:.3f}",
       "-movflags", "+faststart", saida)
    print(f"gravado {saida} ({dur:.2f} s, {larg}x{alt}@{fps})")
    for f in FALTAS:
        print("  FALTA", f)


if __name__ == "__main__":
    main()
