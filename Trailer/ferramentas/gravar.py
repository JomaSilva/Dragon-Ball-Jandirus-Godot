"""gravar.py -- roda o jogo NO SEGUNDO MONITOR (tela cheia 1080p, sem roubar o foco, sem som), com o user://
DESVIADO pra uma pasta de rascunho, e grava esse monitor (ddagrab + NVENC) enquanto um robo dirige a cena.

    python gravar.py NOME [--secs 60] [--marker REGEX] [--espera 0] [--server "ARGS DO SERVIDOR"] [--zoom 3]
                     [--janela 1280x720] -- ARGS DO CLIENTE...

Mata SO os PIDs que abriu. Saida: takes/NOME.mkv + takes/NOME.log (+ .err, + NOME.srv.log se houver servidor).
"""
import argparse, ctypes, json, os, re, shutil, subprocess, sys, time
from ctypes import wintypes
from pathlib import Path

AQUI = Path(__file__).resolve().parent
REPO = r"E:\Users\Joao\Desktop\dragon-ball-jandirus"
GODOT = r"E:\Users\Joao\Desktop\Godot_v4.7-stable_mono_win64\Godot_v4.7.1-stable_mono_win64.exe"
TAKES = AQUI / "takes"
APPS = AQUI / "appdata"
FFMPEG = shutil.which("ffmpeg")
MONITOR = 1   # indice DDA do SEGUNDO monitor (1920,0), onde o jogo abre em tela cheia

user32 = ctypes.windll.user32


class Gravador:
    """O SEGUNDO MONITOR inteiro, por Desktop Duplication (`ddagrab`), direto pro NVENC. Sem cursor.
    (O ffmpeg 8.0.1 instalado nao tem o `gfxcapture` por janela; o jogo em tela cheia no monitor 2 da no mesmo.)"""

    def __init__(self, base: str, fps: int = 60):
        self.base, self.fps, self.p = base, fps, None

    def start(self):
        self.log = open(self.base + ".ffmpeg.log", "w")
        self.p = subprocess.Popen(
            [FFMPEG, "-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i",
             f"ddagrab=output_idx={MONITOR}:framerate={self.fps}:draw_mouse=0",
             "-c:v", "h264_nvenc", "-preset", "p5", "-cq", "18", "-g", str(self.fps // 2), self.base + ".mkv"],
            stdin=subprocess.PIPE, stderr=self.log)
        return self

    def stop(self):
        if not self.p:
            return
        try:
            self.p.stdin.write(b"q")
            self.p.stdin.flush()
        except OSError:
            pass
        try:
            self.p.wait(20)
        except subprocess.TimeoutExpired:
            self.p.kill()
        self.log.close()


def janela_do_pid(pid: int, espera: float = 60.0) -> int:
    """O HWND da janela VISIVEL de nivel superior do processo (a do jogo, e nao a do console)."""
    achado = []

    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(hwnd, _):
        p = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(p))
        if p.value == pid and user32.IsWindowVisible(hwnd) and user32.GetWindowTextLengthW(hwnd) > 0:
            r = wintypes.RECT()
            user32.GetWindowRect(hwnd, ctypes.byref(r))
            if r.right - r.left > 300 and r.bottom - r.top > 200:
                achado.append(hwnd)
        return True

    fim = time.time() + espera
    while time.time() < fim:
        achado.clear()
        user32.EnumWindows(cb, 0)
        if achado:
            return achado[0]
        time.sleep(0.25)
    return 0


def preparar(nome: str, zoom: int, janela: str | None, manter: bool = False) -> dict:
    """APPDATA proprio (nunca a pasta de saves real) + config.json: tela cheia 1080p, volumes ZERADOS."""
    pasta = APPS / nome
    user = pasta / "Godot" / "app_userdata" / "Dragon ball Jandirus"
    # MUNDO NOVO A CADA TOMADA: e esta pasta de rascunho (nunca a de saves de verdade) que guarda o personagem, e a
    # cinematica de estreia de uma forma so toca na PRIMEIRA vez dele nela. Refilmar em cima do save antigo daria
    # a versao encurtada.
    if user.exists() and APPS in user.parents and not manter:
        shutil.rmtree(user, ignore_errors=True)
    user.mkdir(parents=True, exist_ok=True)
    cfg = dict(LarguraJanela=1920, AlturaJanela=1080, TelaCheia=True, Zoom=zoom, Grafico=2,
               VolumeGeral=0.0, VolumeMusica=0.0, VolumeEfeitos=0.0, VolumeAmbiente=0.0, VolumeVoz=0.0)
    if janela:
        w, h = (int(x) for x in janela.lower().split("x"))
        cfg.update(LarguraJanela=w, AlturaJanela=h, TelaCheia=False)
    atual = {}
    arq = user / "config.json"
    if arq.exists():
        try:
            atual = json.loads(arq.read_text(encoding="utf-8"))
        except Exception:
            atual = {}
    atual.update(cfg)
    arq.write_text(json.dumps(atual, indent=2), encoding="utf-8")
    env = dict(os.environ)
    env["APPDATA"] = str(pasta)
    return env


def lancar(args: list[str], env: dict, log: Path) -> subprocess.Popen:
    return subprocess.Popen([GODOT, *args], cwd=REPO, env=env, stdout=open(log, "w"), stderr=open(str(log) + ".err", "w"))


def main():
    # TUDO DEPOIS DO `--` E DO CLIENTE. (O `REMAINDER` do argparse engolia as opcoes escritas depois do nome
    # -- `--secs 600` caia nos args do jogo e a gravacao parava nos 60 s do padrao.)
    argv = sys.argv[1:]
    cliente = []
    if "--" in argv:
        k = argv.index("--")
        argv, cliente = argv[:k], argv[k + 1:]
    ap = argparse.ArgumentParser()
    ap.add_argument("nome")
    ap.add_argument("--secs", type=float, default=60)
    ap.add_argument("--marker", default=r"\[trailer\] ===== FIM", help="regex no log do cliente que encerra a gravacao")
    ap.add_argument("--depois", type=float, default=1.0, help="segundos a mais depois do marcador")
    ap.add_argument("--espera", type=float, default=0.0, help="segundos entre o servidor e o cliente")
    ap.add_argument("--server", default=None, help="args do processo servidor (headless), antes do cliente")
    ap.add_argument("--convidado", default=None, help="args de um SEGUNDO cliente (headless) que entra depois do principal")
    ap.add_argument("--atraso", type=float, default=12.0, help="segundos entre o cliente principal e o convidado")
    ap.add_argument("--zoom", type=int, default=3)
    ap.add_argument("--janela", default=None, help="LxA pra rodar em janela em vez de tela cheia")
    ap.add_argument("--fps", type=int, default=60)
    ap.add_argument("--semgravar", action="store_true")
    ap.add_argument("--manter", action="store_true", help="nao apaga o save de rascunho desta tomada")
    a = ap.parse_args(argv)

    TAKES.mkdir(parents=True, exist_ok=True)
    env = preparar(a.nome, a.zoom, a.janela, a.manter)
    log = TAKES / f"{a.nome}.log"
    procs = []
    rec = None
    marcas, lidas, desvio = [], 0, None
    try:
        if a.server:
            srv = lancar(["--headless", "--path", ".", *a.server.split()], env, TAKES / f"{a.nome}.srv.log")
            procs.append(srv)
            time.sleep(max(a.espera, 3.0))
        cli = lancar(["--path", ".", "--position", "1920,0", "--semfoco", *cliente], env, log)
        procs.append(cli)
        hwnd = janela_do_pid(cli.pid)
        if not hwnd:
            print("SEM JANELA: o cliente nao abriu janela em 60 s")
            return 2
        t0 = time.time()
        if not a.semgravar:
            rec = Gravador(str(TAKES / a.nome), a.fps).start()
        achou = None
        convidado_em = (t0 + a.atraso) if a.convidado else None
        while time.time() - t0 < a.secs and cli.poll() is None:
            time.sleep(0.1)
            if convidado_em and time.time() >= convidado_em:
                convidado_em = None
                procs.append(lancar(["--headless", "--path", ".", *a.convidado.split()], env, TAKES / f"{a.nome}.convidado.log"))
            try:
                texto = log.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            agora = time.time() - t0
            linhas = texto.split("\n")
            # AS MARCAS DO DIRETOR (`[trailer] @12,34 ...`): o relogio do robo -> o tempo do video
            for ln in linhas[lidas:len(linhas) - 1]:
                m = re.match(r"\[trailer\] @(\d+)[,.](\d+) (.*)", ln)
                if m:
                    r = float(m.group(1) + "." + m.group(2))
                    marcas.append([r, m.group(3).strip()])
                    desvio = agora - r if desvio is None else min(desvio, agora - r)
            lidas = max(lidas, len(linhas) - 1)
            if a.marker and re.search(a.marker, texto):
                achou = agora
                time.sleep(a.depois)
                break
        dur = time.time() - t0
        print(f"[gravar] {a.nome}: {dur:.1f} s" + (f" (marcador em {achou:.1f} s)" if achou else "")
              + (" (o processo saiu sozinho)" if cli.poll() is not None else ""), flush=True)
    finally:
        inicio = 0.0
        if rec:
            t_parou = time.time() - t0
            rec.stop()
            # O ffmpeg leva um instante pra comecar a capturar: o video e MAIS CURTO que o relogio de parede, e a
            # diferenca e o atraso do comeco. Sem descontar, toda marca cairia esse tanto adiante no video.
            try:
                dv = float(subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0",
                                           str(TAKES / (a.nome + ".mkv"))], capture_output=True, text=True).stdout.strip())
                inicio = max(0.0, t_parou - dv)
            except Exception:
                inicio = 0.0
        for p in reversed(procs):
            if p.poll() is None:
                p.kill()
        if marcas:
            d = (desvio or 0.0) - inicio
            (TAKES / f"{a.nome}.marcas.json").write_text(json.dumps(
                dict(desvio=round(d, 2), marcas=[dict(robo=r, video=round(r + d, 2), oque=o) for r, o in marcas]),
                indent=1, ensure_ascii=False), encoding="utf-8")
    return 0


if __name__ == "__main__":
    sys.exit(main())
