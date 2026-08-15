using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A FOTO DA DIMENSAO MENTAL (`--fotodamente`) -- o relato A do dono, na tela.
///
/// ============================ POR QUE ESTA BANCADA E UMA FOTO E NAO UM NUMERO ============================
/// A queixa era visual e so isso:
///
///     *"a MEDITACAO PROFUNDA ta me levando pra um LUGAR NADA A VER, era pra ser a DIMENSAO BRANCA
///      assim como era no byond"*
///
/// A bancada de regras (`--presoteste`, familia 3) ja prova tudo o que da pra provar sem tela: a zona
/// resolve pra planta de 100x100 e nao pro z24 de 500x500, o nascimento cai na celula do `locate(cx-3,
/// cy)`, a paleta inteira e o mesmo branco, nenhuma celula e densa e a colisao diz `SemBorda`.
/// **Nenhuma dessas linhas responde "e branco?"** -- uma chapa de 100x100 pintada de roxo passa em
/// todas elas.
///
/// Entao esta bancada entra pelo caminho do jogador (medita, mergulha) e SALVA O PIXEL.
///
/// ============================ E A FOTO QUE IMPORTA E A DE FORA DA CHAPA ============================
/// A mente deixou de ser um quarto -- *"faca tb o MAPA DA MENTE ser INFINITO SEM BORDAS e CARREGAR POR
/// CHUNK, tb o FUNDO BRANCO"* --, e o infinito e a unica parte dela que **nao existe como dado**: fora
/// dos 100x100 nao ha byte nenhum, ha um pincel repetido que o `PlanetaProcedural.FonteDoTerreno`
/// resolve por pedaco. Todas as medidas de colisao ficam VERDES com esse pincel errado, ou nulo, ou
/// desenhado na camada de baixo -- o jogador atravessaria o vazio andando por cima de PRETO.
///
/// E por isso a fase 4 existe e por isso ela e longa: ela leva o corpo a **60 tiles alem da beirada da
/// chapa** e fotografa la. Uma foto tirada no meio da chapa (as fases 2 e 3) mede o chao que sempre
/// existiu e nao encosta na unica coisa que esta fase construiu.
/// ================================================================================================
///
/// ============================ E A FOTO SO VALE EM PAR ============================
/// Uma foto de um lugar branco nao prova que ele MUDOU. O par e a chave `--menteantiga`, que desliga
/// as duas linhas do conserto e devolve o jogo ao estado em que o dono o encontrou -- o z24 real do
/// BYOND, mosaico azul-petroleo de um lado e nebulosa roxa do outro, com o nascimento na emenda. As
/// duas rodadas fazem o MESMO gesto no MESMO binario; so a chave muda.
///
/// Ela mede o pixel tambem, e nao so o salva: a cor MEDIA e a fracao de pixels quase-brancos saem no
/// relatorio. Uma foto que ninguem olha e um arquivo; um numero ao lado dela e uma prova que
/// sobrevive ao dia em que ninguem lembrar de abrir a pasta.
/// ==========================================================================================
///
///     Godot --path . --host --rede 7926 --fotodamente --resolution 1600x900 ...
///     Godot --path . --host --rede 7926 --fotodamente --menteantiga ...     (o "antes")
/// </summary>
public partial class RoboDaMente : Node2D
{
	private static GameClient? C => GameClient.Instance;

	private double _t;
	private int _fase;
	private bool _pediu;
	private readonly List<string> _linhas = [];

	/// <summary>O sufixo dos arquivos: `mente-depois-*.png` ou `mente-antes-*.png`.</summary>
	private static string Rotulo => World.MenteAntiga ? "antes" : "depois";

	public override void _Process(double delta)
	{
		if (C is not { Connected: true } cli) return;
		_t += delta;

		switch (_fase)
		{
			// ---- 0: espera o mundo assentar. O primeiro quadro depois do login ainda esta
			// montando pedaco de cenario, e uma foto ali sai meio pintada.
			case 0:
				if (_t < 4) return;
				Nota($"=== A DIMENSAO MENTAL, {(World.MenteAntiga ? "ANTES" : "DEPOIS")} DO CONSERTO ===");
				Fotografar($"user://mente-{Rotulo}-0-no-planeta.png", "0 -- no planeta, antes de meditar");
				_fase = 1;
				return;

			// ---- 1: o gesto do jogador. Meditar (tecla M) e mergulhar -- os MESMOS dois canais que
			// o `Habilidades.cs` liga na tecla, e nao um atalho de bancada.
			case 1:
				if (_pediu) { if (_t > 9) _fase = 2; return; }
				cli.SendActivity(Protocol.Activity.Meditando);
				cli.SendHabilidade("mente");
				_pediu = true;
				Nota("meditei e mergulhei na mente (os dois canais da tecla)");
				return;

			// ---- 2: a foto que responde a pergunta.
			case 2:
				Relatar();
				Fotografar($"user://mente-{Rotulo}-1-dentro.png", "1 -- DENTRO DA MENTE");
				_fase = 3;
				_t = 0;
				return;

			// ---- 3: e uma segunda, depois de andar. Uma foto so pega um enquadramento, e o defeito
			// do dono era JUSTAMENTE uma emenda entre dois mosaicos: o corpo nascia bem nela, e o
			// quadro parado nao mostra o que ha tres tiles ao lado.
			case 3:
				World.Instancia?.AndarDeTeste(new Vector2(1, 0));
				if (_t < 6) return;
				World.Instancia?.PararDeTeste();
				Fotografar($"user://mente-{Rotulo}-2-andando.png", "2 -- DENTRO DA MENTE, 6 s a leste");
				_fase = 4;
				_t = 0;
				return;

			// ---- 4: A FOTO QUE ESTA FASE EXISTE PRA TIRAR -- fora da chapa, no branco que nao e dado.
			//
			// O ALVO E ABSOLUTO (`IrAteDeTeste`) e nao um rumo: e a diferenca entre "ande pro leste por
			// N segundos e torca" e "chegue nesta celula". Sessenta tiles alem da beirada e folgado --
			// um pedaco tem 64 tiles de lado, entao a camera esta inteira dentro de pedacos que **so**
			// existem por causa do modo sem beirada, e nao na costura com a chapa.
			//
			// COM `--menteantiga` ELE NAO CHEGA, e isso e a prova em si: la a mente e o z24 e ha mapa
			// naquela coordenada; sem a chave, o corpo caminha por cima de nada construido. O relatorio
			// escreve a celula em que ele parou pros dois casos serem lidos no log e nao no olho.
			case 4:
				if (!_pediuLonge)
				{
					World.Instancia?.IrAteDeTeste(new Jandirus.Core.World.Vec2(AlvoLonge, MeioDaChapa));
					_pediuLonge = true;
					Nota($"indo a pe ate x={AlvoLonge:0} px (celula {AlvoLonge / ZoneCollision.TileSize:0}) "
						 + $"-- {ForaDaChapaEmTiles} tiles ALEM da beirada leste da chapa");
				}
				if (_t < 40 && !ChegouLonge()) return;
				World.Instancia?.PararDeTeste();
				RelatarOVazio();
				Fotografar($"user://mente-{Rotulo}-3-fora-da-chapa.png",
						   "3 -- FORA DA CHAPA (o branco que nao e dado nenhum)");
				_fase = 5;
				return;

			case 5:
				Fim();
				_fase = 6;
				return;
		}
	}

	// =====================================================================
	// A VIAGEM PRO VAZIO
	// =====================================================================
	/// <summary>Quantos tiles alem da beirada da chapa a fase 4 vai. Ver o comentario da fase.</summary>
	private const int ForaDaChapaEmTiles = 60;

	private const float MeioDaChapa = (DimensaoMental.Lado / 2 + 0.5f) * ZoneCollision.TileSize;

	private const float AlvoLonge =
		(DimensaoMental.Lado + ForaDaChapaEmTiles + 0.5f) * ZoneCollision.TileSize;

	private bool _pediuLonge;

	/// <summary>Ja passou da beirada com folga? Meio tile de tolerancia -- o piloto para "perto".</summary>
	private static bool ChegouLonge() =>
		World.Instancia?.PosicaoLocal is { } p && p.X >= AlvoLonge - ZoneCollision.TileSize;

	// =====================================================================
	// O QUE A TELA DIZ, EM NUMERO
	// =====================================================================
	private void Relatar()
	{
		if (C is not { } cli) return;
		ZoneKey z = cli.Zone;

		Confere(DimensaoMental.EhAMente(z), $"a zona atual e uma MENTE (`{z.Name}` / interior)");

		if (World.Instancia?.Colisao is { } m)
		{
			bool planta = m.Width == DimensaoMental.Lado && m.Height == DimensaoMental.Lado;
			Confere(planta || World.MenteAntiga,
					$"o mapa desenhado tem {m.Width}x{m.Height}"
					+ (planta ? " (a planta do quarto branco)" : " -- NAO e a planta"));
			if (World.MenteAntiga && !planta)
				Nota($"   (esperado: com `--menteantiga` o mapa E o z24, {m.Width}x{m.Height})");
		}
		else Nota("sem colisao carregada no cliente -- o mapa nao montou");
	}

	/// <summary>
	/// ONDE ELE PAROU, E SE ISSO E FORA DA CHAPA -- o texto que acompanha a foto do vazio.
	///
	/// **A FOTO SOZINHA NAO DIZ ONDE FOI TIRADA.** Branco fotografado no meio da chapa e branco
	/// fotografado a 60 tiles dela sao a mesma imagem, e a segunda e a unica que prova alguma coisa: um
	/// corpo que nao saiu do lugar (porque bateu numa parede que voltou, ou porque o piloto desistiu)
	/// entregaria uma foto perfeita da coisa errada. A celula escrita aqui e o que separa as duas.
	/// </summary>
	private void RelatarOVazio()
	{
		if (World.Instancia?.PosicaoLocal is not { } p)
		{
			Nota("sem posicao local -- nao da pra dizer onde a foto do vazio foi tirada");
			return;
		}

		int cx = (int)MathF.Floor(p.X / ZoneCollision.TileSize);
		int cy = (int)MathF.Floor(p.Y / ZoneCollision.TileSize);
		bool fora = cx >= DimensaoMental.Lado;

		Confere(fora || World.MenteAntiga,
				$"o corpo andou ate a celula ({cx},{cy}) -- "
				+ (fora
					? $"FORA da chapa de {DimensaoMental.Lado}x{DimensaoMental.Lado}, no infinito"
					: $"AINDA DENTRO da chapa: alguma coisa o segurou (a beirada e {DimensaoMental.Lado})"));

		if (World.MenteAntiga && !fora)
			Nota("   (esperado: com `--menteantiga` a mente e o z24 e esta viagem nao mede nada)");
	}

	// =====================================================================
	// A FOTO
	// =====================================================================
	/// <summary>
	/// Salva a tela e mede a cor dela.
	///
	/// A MEDIDA E O PONTO. "Branco" e uma palavra; `98% dos pixels acima de 0,90 em RGB` e uma
	/// afirmacao que reprova sozinha no dia em que a paleta mudar -- e ninguem precisa abrir a pasta
	/// pra descobrir. A foto continua salva porque o dono pediu pra VER.
	/// </summary>
	private void Fotografar(string destino, string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			(float claro, Color media) = Medir(img);
			Nota($"{rotulo}: {caminho} ({img.GetWidth()}x{img.GetHeight()})");
			Nota($"   cor media R{media.R:0.00} G{media.G:0.00} B{media.B:0.00} | "
				 + $"{claro * 100:0.0}% dos pixels sao quase-brancos");
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	/// <summary>
	/// A fracao de pixels quase-brancos e a cor media -- amostrando de 4 em 4 pixels.
	///
	/// A AMOSTRAGEM E DE PROPOSITO: ler 1,4 milhao de pixels um a um trava o quadro em que a foto e
	/// tirada, e o que se quer daqui e a MASSA da imagem, nao um pixel especifico. De 4 em 4 sobram
	/// ~90 mil amostras, que e mais do que suficiente pra separar "branco" de "mosaico azul".
	/// </summary>
	private static (float Claros, Color Media) Medir(Image img)
	{
		double r = 0, g = 0, b = 0;
		int n = 0, claros = 0;
		for (int y = 0; y < img.GetHeight(); y += 4)
			for (int x = 0; x < img.GetWidth(); x += 4)
			{
				Color c = img.GetPixel(x, y);
				r += c.R; g += c.G; b += c.B; n++;
				if (c.R > 0.85f && c.G > 0.85f && c.B > 0.85f) claros++;
			}
		if (n == 0) return (0, Colors.Black);
		return (claros / (float)n, new Color((float)(r / n), (float)(g / n), (float)(b / n)));
	}

	// =====================================================================
	// RELATORIO
	// =====================================================================
	private readonly List<string> _falhas = [];

	private void Confere(bool ok, string oque)
	{
		_linhas.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _linhas.Add("  --     " + oque);

	private void Fim()
	{
		GD.Print("\n===== FOTO DA DIMENSAO MENTAL =====");
		foreach (string l in _linhas) GD.Print(l);
		GD.Print(_falhas.Count == 0
			? "  ---- nenhuma falha ----"
			: $"  ---- {_falhas.Count} FALHA(S) ----");
		GD.Print("  (a comparacao e entre `mente-antes-*.png` e `mente-depois-*.png`; "
				 + "rode as duas vezes, uma com `--menteantiga`)");
		GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit();
	}
}
