"""batidas.py -- tempo, grade de batidas e mapa de energia da musica do trailer (numpy puro, audio via ffmpeg)."""
import json, subprocess, sys
import numpy as np

MUSICA = r"E:\Users\Joao\Desktop\dragon-ball-jandirus\Assets\Sounds\Music\Menu ost\Abertura DRAGON BALL KAI DUBLADO (COMPLETA)!!.mp3"
SR = 22050


def carregar():
    raw = subprocess.run(["ffmpeg", "-v", "error", "-i", MUSICA, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
                         capture_output=True).stdout
    return np.frombuffer(raw, dtype=np.float32)


def fluxo(x, n=1024, hop=256):
    """Forca de ataque por fluxo espectral (so aumentos), em quadros de `hop` amostras."""
    jan = np.hanning(n)
    quadros = 1 + (len(x) - n) // hop
    idx = np.arange(n)[None, :] + hop * np.arange(quadros)[:, None]
    S = np.abs(np.fft.rfft(x[idx] * jan, axis=1))
    S = np.log1p(30 * S)
    d = np.diff(S, axis=0)
    f = np.maximum(d, 0).sum(axis=1)
    f = np.concatenate([[0], f])
    # tira a media local pra a grade nao seguir o volume
    k = 43
    med = np.convolve(f, np.ones(k) / k, mode="same")
    return np.maximum(f - med, 0), SR / hop


def tempo(f, taxa, lo=120, hi=200):
    ac = np.correlate(f, f, mode="full")[len(f) - 1:]
    melhor, bpm = -1, 0
    for b in np.arange(lo, hi, 0.05):
        lag = 60.0 / b * taxa
        # soma a autocorrelacao em 1, 2 e 4 batidas (interpolada)
        s = 0
        for m in (1, 2, 4):
            p = lag * m
            i = int(p)
            fr = p - i
            if i + 1 < len(ac):
                s += ac[i] * (1 - fr) + ac[i + 1] * fr
        if s > melhor:
            melhor, bpm = s, b
    return float(bpm)


def fase(f, taxa, bpm):
    lag = 60.0 / bpm * taxa
    melhor, off = -1, 0
    for o in np.linspace(0, lag, 200, endpoint=False):
        pos = np.arange(o, len(f) - 1, lag)
        i = pos.astype(int)
        fr = pos - i
        s = (f[i] * (1 - fr) + f[i + 1] * fr).sum()
        if s > melhor:
            melhor, off = s, o
    return off / taxa


def main():
    x = carregar()
    dur = len(x) / SR
    f, taxa = fluxo(x)
    bpm = tempo(f, taxa)
    t0 = fase(f, taxa, bpm)
    batidas = np.arange(t0, dur, 60.0 / bpm)

    # energia por segundo (RMS) e por faixa grave -- pra ver as secoes
    seg = int(SR)
    n = len(x) // seg
    rms = np.sqrt((x[:n * seg].reshape(n, seg) ** 2).mean(axis=1))
    rms_db = 20 * np.log10(rms + 1e-6)

    # qual das 4 fases de compasso tem mais ataque no grave (bumbo no 1)? so um palpite
    jan = 2048
    graves = []
    for b in batidas:
        i = int(b * SR)
        trecho = x[i:i + jan]
        if len(trecho) < jan:
            graves.append(0)
            continue
        S = np.abs(np.fft.rfft(trecho * np.hanning(jan)))
        graves.append(float(S[:int(150 / (SR / jan))].sum()))
    graves = np.array(graves)
    fases = [float(graves[k::4].mean()) for k in range(4)]

    out = dict(bpm=round(bpm, 3), primeiro=round(float(t0), 4), dur=round(dur, 3), batidas=len(batidas),
               graves_por_fase=[round(v, 1) for v in fases],
               rms_db=[round(float(v), 1) for v in rms_db])
    json.dump(out, open(sys.argv[1] if len(sys.argv) > 1 else "batidas.json", "w"), indent=1)
    print("bpm", out["bpm"], "| primeira batida", out["primeiro"], "| duracao", out["dur"], "| batidas", len(batidas))
    print("graves por fase do compasso:", out["graves_por_fase"])
    # mapa de energia: uma linha por 10 s
    lo, hi = np.percentile(rms_db, 5), np.percentile(rms_db, 98)
    barras = " .:-=+*#%@"
    for i in range(0, n, 10):
        linha = "".join(barras[int(np.clip((v - lo) / (hi - lo), 0, 0.999) * len(barras))] for v in rms_db[i:i + 10])
        print(f"{i // 60}:{i % 60:02d}  {linha}   {np.mean(rms_db[i:i+10]):6.1f} dB")


if __name__ == "__main__":
    main()
