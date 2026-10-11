# O BRILHO DO TITULO DO JOGO: a silhueta do logo borrada, em laranja -- a mesma conta do titulo animado do trailer
# (`Trailer/bruto/t2/titulo_anim.py`: o alfa do logo de 1180 px de largura, com 150 px de margem em volta, passado
# por um borrao gaussiano de 38 px e multiplicado por 0,85; cor 255,150,30).
#
# Sai ASSADO num PNG pequeno porque e um borrao: nao ha detalhe menor que ~40 px nele, e a tela o estica com filtro
# linear. Quem o desenha e o `Client/TituloDoJogo.cs` (o cabecalho dos menus) e o `Client/Abertura.cs` (a abertura).
#
#   python Tools/brilho_do_titulo.py [Assets/UI/Titulo.png] [Assets/UI/TituloBrilho.png]
#
# Rode de novo se a arte do titulo mudar, e depois o import do Godot (`--import`).
import sys
from pathlib import Path
from PIL import Image, ImageFilter

RAIZ = Path(__file__).resolve().parent.parent
ENTRADA = Path(sys.argv[1]) if len(sys.argv) > 1 else RAIZ / "Assets" / "UI" / "Titulo.png"
SAIDA = Path(sys.argv[2]) if len(sys.argv) > 2 else RAIZ / "Assets" / "UI" / "TituloBrilho.png"

# OS NUMEROS DA RECEITA DO TRAILER (em px de um logo de 1180 de largura). O `TituloDoJogo.cs` repete os dois
# primeiros (`LarguraDaReceita`, `MargemDoBrilho`): mudou aqui, muda la.
LARGURA = 1180      # a largura do logo assentado num quadro de 1920x1080
MARGEM = 150        # a folga em volta: o borrao tem de morrer ANTES da beirada, senao sai um retangulo
BORRAO = 38         # o raio do borrao gaussiano
FORCA = 0.85        # quanto do alfa borrado fica
COR = (255, 150, 30)
REDUCAO = 4         # o PNG sai 4x menor que a conta (um borrao de 38 px nao perde nada com isso)

logo = Image.open(ENTRADA).convert("RGBA")
logo = logo.resize((LARGURA, round(logo.height * LARGURA / logo.width)), Image.LANCZOS)
tela = Image.new("L", (logo.width + 2 * MARGEM, logo.height + 2 * MARGEM), 0)
tela.paste(logo.split()[3], (MARGEM, MARGEM))
alfa = tela.filter(ImageFilter.GaussianBlur(BORRAO)).point(lambda v: min(255, int(v * FORCA)))
alfa = alfa.resize((round(tela.width / REDUCAO), round(tela.height / REDUCAO)), Image.LANCZOS)

brilho = Image.new("RGBA", alfa.size, COR + (0,))
brilho.putalpha(alfa)
SAIDA.parent.mkdir(parents=True, exist_ok=True)
brilho.save(SAIDA, optimize=True)
print(f"{SAIDA}  {brilho.size[0]}x{brilho.size[1]}  (logo {logo.size[0]}x{logo.size[1]} + {MARGEM} de margem, /{REDUCAO})")
