"""letreiro.py -- a arte do titulo `DRAGON BALL JANDIRUS` (PNG com alfa + versao sobre fundo escuro) e os cartoes de texto do trailer.
Tudo desenhado aqui (Pillow): nenhuma arte de terceiros."""
import math, sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageFont, ImageChops

AQUI = Path(__file__).resolve().parent
OUT = AQUI / "arte"
W, H = 1920, 1080
IMPACT = r"C:\Windows\Fonts\impact.ttf"
BLACK = r"C:\Windows\Fonts\seguibl.ttf"


def gradiente(size, cores):
    """faixa vertical de cores [(pos 0..1, (r,g,b)), ...]"""
    w, h = size
    g = Image.new("RGB", (1, h))
    px = g.load()
    for y in range(h):
        t = y / max(1, h - 1)
        for (p0, c0), (p1, c1) in zip(cores, cores[1:]):
            if p0 <= t <= p1:
                k = (t - p0) / max(1e-6, p1 - p0)
                px[0, y] = tuple(round(c0[i] + (c1[i] - c0[i]) * k) for i in range(3))
                break
    return g.resize((w, h))


def texto_dbz(txt, tam, inclinar=0.18, fonte=IMPACT, espaco=0):
    """letras com degrade amarelo->laranja->vermelho, contorno escuro grosso, brilho no topo e sombra; devolve RGBA recortado"""
    f = ImageFont.truetype(fonte, tam)
    tmp = ImageDraw.Draw(Image.new("L", (10, 10)))
    larg = sum(tmp.textlength(ch, font=f) + espaco for ch in txt) - espaco
    asc, desc = f.getmetrics()
    borda = max(6, tam // 9)
    pad = borda * 3 + int(tam * inclinar)
    cw, ch_ = int(larg) + pad * 2, asc + desc + pad * 2
    mask = Image.new("L", (cw, ch_), 0)
    d = ImageDraw.Draw(mask)
    x = pad
    for c in txt:
        d.text((x, pad), c, font=f, fill=255)
        x += tmp.textlength(c, font=f) + espaco
    # italico por cisalhamento
    if inclinar:
        mask = mask.transform(mask.size, Image.AFFINE, (1, inclinar, -inclinar * ch_ / 2, 0, 1, 0), resample=Image.BICUBIC)
    # contornos: preto grosso + azul-escuro
    fora = mask.filter(ImageFilter.MaxFilter(borda * 2 + 1))
    meio = mask.filter(ImageFilter.MaxFilter(max(3, borda // 2) * 2 + 1))
    img = Image.new("RGBA", (cw, ch_), (0, 0, 0, 0))
    sombra = Image.new("RGBA", (cw, ch_), (0, 0, 0, 0))
    sombra.paste((0, 0, 0, 170), (0, 0), fora)
    sombra = sombra.filter(ImageFilter.GaussianBlur(borda))
    img.alpha_composite(sombra, (0, 0))
    desloc = Image.new("RGBA", (cw, ch_), (0, 0, 0, 0))
    desloc.paste((10, 6, 30, 255), (borda // 2, borda // 2 + 2), fora)
    img.alpha_composite(desloc)
    img.paste((14, 10, 40, 255), (0, 0), fora)
    img.paste((150, 20, 10, 255), (0, 0), meio)
    bbox = mask.getbbox()
    grad = gradiente((cw, ch_), [(0, (255, 250, 170)), (bbox[1] / ch_, (255, 245, 120)), ((bbox[1] + (bbox[3] - bbox[1]) * 0.45) / ch_, (255, 190, 20)),
                                 ((bbox[1] + (bbox[3] - bbox[1]) * 0.75) / ch_, (255, 110, 0)), (bbox[3] / ch_, (215, 30, 0)), (1, (200, 20, 0))])
    img.paste(grad, (0, 0), mask)
    # brilho no topo das letras
    topo = Image.new("L", (cw, ch_), 0)
    ImageDraw.Draw(topo).rectangle((0, bbox[1], cw, bbox[1] + (bbox[3] - bbox[1]) * 0.22), fill=110)
    topo = ImageChops.multiply(topo.filter(ImageFilter.GaussianBlur(tam * 0.04)), mask.filter(ImageFilter.MinFilter(5)))
    img.paste((255, 255, 255, 255), (0, 0), topo)
    return img.crop(img.getbbox())


def estrela(d, cx, cy, r, cor):
    pts = []
    for i in range(10):
        a = -math.pi / 2 + i * math.pi / 5
        rr = r if i % 2 == 0 else r * 0.42
        pts.append((cx + math.cos(a) * rr, cy + math.sin(a) * rr))
    d.polygon(pts, fill=cor)


def esfera(diam, estrelas=4):
    """uma esfera do dragao: bola laranja com brilho e estrelas vermelhas"""
    S = 4
    n = diam * S
    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    base = Image.new("RGB", (n, n))
    px = base.load()
    cx, cy = n * 0.40, n * 0.34
    for y in range(n):
        for x in range(n):
            dd = math.hypot(x - cx, y - cy) / (n * 0.78)
            t = min(1, dd)
            px[x, y] = (round(255 - 55 * t * t), round(205 - 125 * t), round(70 - 60 * t))
    m = Image.new("L", (n, n), 0)
    ImageDraw.Draw(m).ellipse((2, 2, n - 3, n - 3), fill=255)
    img.paste(base, (0, 0), m)
    d = ImageDraw.Draw(img)
    pos = {1: [(0, 0)], 2: [(-.2, 0), (.2, 0)], 3: [(0, -.2), (-.2, .15), (.2, .15)], 4: [(-.19, -.19), (.19, -.19), (-.19, .19), (.19, .19)],
           5: [(0, -.25), (-.25, -.05), (.25, -.05), (-.15, .23), (.15, .23)], 6: [(-.2, -.25), (.2, -.25), (-.27, 0), (.27, 0), (-.2, .25), (.2, .25)],
           7: [(0, 0), (0, -.3), (.26, -.15), (.26, .15), (0, .3), (-.26, .15), (-.26, -.15)]}[estrelas]
    for ox, oy in pos:
        estrela(d, n / 2 + ox * n, n / 2 + oy * n + n * 0.02, n * 0.115, (205, 25, 20, 255))
    # brilho
    br = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(br).ellipse((n * .20, n * .12, n * .50, n * .34), fill=(255, 255, 255, 150))
    img.alpha_composite(br.filter(ImageFilter.GaussianBlur(n * 0.035)))
    borda = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    ImageDraw.Draw(borda).ellipse((2, 2, n - 3, n - 3), outline=(110, 40, 0, 255), width=S * 3)
    img.alpha_composite(borda)
    return img.resize((diam, diam), Image.LANCZOS)


def logo(largura=1500):
    """o titulo inteiro, RGBA: DRAGON BALL em cima (com a esfera de 4 estrelas no lugar do O), JANDIRUS embaixo, maior"""
    a = texto_dbz("DRAG", 190)
    b = texto_dbz("N BALL", 190)
    j = texto_dbz("JANDIRUS", 330, espaco=6)
    bola = esfera(int(a.height * 0.78), 4)
    gap = -34
    w1 = a.width + bola.width + b.width + gap * 2
    Wd = max(w1, j.width) + 80
    Hd = a.height + j.height - 40 + 40
    img = Image.new("RGBA", (Wd, Hd), (0, 0, 0, 0))
    x = (Wd - w1) // 2
    img.alpha_composite(a, (x, 0))
    img.alpha_composite(b, (x + a.width + bola.width + gap * 2, 0))
    # a esfera por cima das duas metades: ela e o "O" de DRAGON
    halo = Image.new("RGBA", (bola.width + 28, bola.height + 28), (0, 0, 0, 0))
    ImageDraw.Draw(halo).ellipse((0, 0, halo.width - 1, halo.height - 1), fill=(14, 10, 40, 255))
    img.alpha_composite(halo, (x + a.width + gap - 14, (a.height - bola.height) // 2 - 10))
    img.alpha_composite(bola, (x + a.width + gap, (a.height - bola.height) // 2 + 4))
    img.alpha_composite(j, ((Wd - j.width) // 2, a.height - 46))
    img = img.crop(img.getbbox())
    r = largura / img.width
    return img.resize((largura, round(img.height * r)), Image.LANCZOS)


def fundo_raios(cor=(255, 140, 20)):
    """fundo escuro com um clarao quente e raios de velocidade saindo do centro"""
    im = Image.new("RGB", (W, H), (6, 5, 14))
    glow = Image.new("RGB", (W, H), (0, 0, 0))
    d = ImageDraw.Draw(glow)
    for i in range(72):
        a = i * math.pi * 2 / 72
        larg = 0.012 + 0.02 * ((i * 7919) % 5) / 5
        r = 1500
        p = [(W / 2, H / 2), (W / 2 + math.cos(a - larg) * r, H / 2 + math.sin(a - larg) * r), (W / 2 + math.cos(a + larg) * r, H / 2 + math.sin(a + larg) * r)]
        k = 0.25 + 0.5 * ((i * 104729) % 7) / 7
        d.polygon(p, fill=tuple(int(c * k * 0.42) for c in cor))
    centro = Image.new('RGB', (W, H), (0, 0, 0))
    ImageDraw.Draw(centro).ellipse((W / 2 - 560, H / 2 - 330, W / 2 + 560, H / 2 + 330), fill=tuple(int(c * 0.6) for c in cor))
    glow = ImageChops.add(glow, centro.filter(ImageFilter.GaussianBlur(120)))
    glow = glow.filter(ImageFilter.GaussianBlur(26))
    im = ImageChops.add(im, glow)
    # vinheta
    vin = Image.new("L", (W, H), 0)
    ImageDraw.Draw(vin).ellipse((-300, -260, W + 300, H + 260), fill=255)
    vin = vin.filter(ImageFilter.GaussianBlur(220))
    preto = Image.new("RGB", (W, H), (0, 0, 0))
    return Image.composite(im, preto, vin)


if __name__ == "__main__":
    OUT.mkdir(exist_ok=True)
    L = logo(1500)
    L.save(OUT / "logo.png")
    bg = fundo_raios().convert("RGBA")
    bg.alpha_composite(L, ((W - L.width) // 2, (H - L.height) // 2 - 10))
    bg.convert("RGB").save(OUT / "titulo.png")
    print("logo", L.size)
