using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DOS DECALQUES (`--diagdecalque`).
///
/// ============================ O QUE SO O TESTE RESPONDE ============================
/// O modo de falhar destes seis efeitos e o PIOR que existe: silencio. Um caminho de arte errado,
/// um nome de animacao que nao existe na folha, um `.tres` que nao veio na conversao -- em todos os
/// casos o `Plantar` roda, nao lanca nada, e simplesmente NAO DESENHA. Ninguem percebe, porque
/// "efeito que nao aparece" e indistinguivel de "efeito que ainda nao disparou".
///
/// Entao a pergunta nao e "chamei o Plantar?" -- e "nasceu um node com textura de verdade?".
/// Junto com isso:
///   * o teto de decalques vivos DISPARA? (teto que nunca e atingido = teto nenhum)
///   * a terra revirada aparece em volta do que caiu, e em ALGUNS vizinhos e nao em todos?
///   * o sorteio dos vizinhos e o MESMO em dois clientes? (senao o cenario discorda entre telas)
/// ==================================================================================
///
///     Godot --path . --host --quebrarteste 6 --diagdecalque --nome Marcador --conta decal
/// </summary>
public partial class RoboDeDecalque : Node
{
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _passo;
	private double _t;
	private int _pico;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Process(double delta)
	{
		if (_acabou || GameClient.Instance is not { Connected: true }) return;
		if (World.Instancia is not { } mundo || Decalques.Instancia is not { } dec) return;

		_pico = Math.Max(_pico, Decalques.VivosDeTeste);

		_t += delta;
		if (_t < 0.8) return;
		_t = 0;

		switch (_passo++)
		{
			case 0:
				break;   // deixa a leva do `--quebrarteste` chegar

			case 1:
			{
				// ---------- A TERRA REVIRADA ----------
				// O `--quebrarteste` derruba celulas em volta do nascimento; cada uma pinta terra em
				// alguns vizinhos. Se o numero for zero, ou a arte nao carregou ou o gancho nao
				// esta ligado -- e os dois dao a mesma tela.
				Conferir(Decalques.PedidosDeTeste > 0,
					$"a destruicao pediu decalque ({Decalques.PedidosDeTeste} no total)");
				Conferir(dec.GetChildCount() > 0,
					$"e eles VIRARAM NODE de verdade ({dec.GetChildCount()} na cena) -- arte carregada");
				// O CHAO DANIFICADO E PERMANENTE (decisao do dono): ele nao entra na fila dos que
				// expiram, entao tem que aparecer na OUTRA contagem.
				Conferir(Decalques.PermanentesDeTeste > 0,
					$"e a terra revirada entrou na fila dos PERMANENTES ({Decalques.PermanentesDeTeste})");

				// ---------- TODO DECALQUE DE CHAO CAI NO CENTRO DA CELULA ----------
				// Marca alinhada a grade encosta na vizinha; marca no pixel exato onde o corpo
				// estava deixa vao IRREGULAR entre uma e outra -- foi o rastro picotado que o dono
				// fotografou duas vezes. Conferir a posicao pega a familia inteira de uma vez,
				// inclusive se alguem "otimizar" isso de volta um dia.
				int t = ZoneCollision.TileSize;
				int fora = 0;
				foreach (Node n in dec.GetChildren())
					if (n is Node2D d)
					{
						float rx = Mathf.PosMod(d.Position.X, t), ry = Mathf.PosMod(d.Position.Y, t);
						if (Mathf.Abs(rx - t / 2f) > 0.5f || Mathf.Abs(ry - t / 2f) > 0.5f) fora++;
					}
				Conferir(fora == 0,
					$"toda marca de chao caiu no CENTRO da celula ({fora} fora da grade)");

				// ---------- SORTEIO IGUAL EM TODA TELA ----------
				// Duas passadas pela MESMA celula tem que dar a MESMA resposta. Se `randf()` tivesse
				// escapado pra ca, cada cliente pintaria uma mancha diferente na mesma pedra.
				bool estavel = true;
				for (int i = 0; i < 40 && estavel; i++)
					estavel = VizinhosDe(mundo, 100 + i, 200 - i) == VizinhosDe(mundo, 100 + i, 200 - i);
				Conferir(estavel, "o sorteio dos vizinhos e ESTAVEL: mesma celula, mesma mancha em toda tela");

				// ---------- MAS NAO EM TODOS OS VIZINHOS ----------
				// "Aleatorio" que na pratica pega os oito seria um quadrado perfeito -- que le como
				// bug de tilemap, e nao como estrago.
				int cheios = 0, vazios = 0;
				for (int i = 0; i < 60; i++)
				{
					int n = VizinhosDe(mundo, 300 + i, 400 + i * 3);
					if (n == 8) cheios++;
					if (n == 0) vazios++;
				}
				Conferir(cheios < 15 && vazios < 15,
					$"e e IRREGULAR: de 60 pedras, {cheios} pegaram os 8 vizinhos e {vazios} nenhum");
				break;
			}

			case 2:
			{
				// ---------- TODA ARTE CARREGA ----------
				// Uma por uma, porque cada tipo aponta pra um arquivo diferente e o erro de um nao
				// aparece no outro. O `Blood spray` e o `Damaged Ground` sao .tres do pipeline de
				// .dmi; a fumaca e um PNG solto que nem tinha vindo na conversao.
				int antes = dec.GetChildCount();
				foreach (Protocol.Decal t in Enum.GetValues<Protocol.Decal>())
				{
					int a = dec.GetChildCount();
					// O MEMBRO PRECISA DE CARGA: sem peca ele nao tem recorte e nao planta nada (e
					// e o certo -- ver `Plantar`). Aqui vai uma peca qualquer so pra provar que a
					// folha `Body Parts Bloody` carrega; a TABELA e conferida logo abaixo.
					dec.Plantar(t, new Vector2(9999, 9999), Facing.South,
								t == Protocol.Decal.Membro ? PecaDeCorpo.Braco : PecaDeCorpo.Nenhuma);
					Conferir(dec.GetChildCount() > a, $"a arte de {t} carregou e virou node");
				}
				Conferir(dec.GetChildCount() > antes, "todos os tipos de decalque plantaram");

				// ---------- AS DEZ PECAS DO CORPO TEM RECORTE ----------
				// UMA POR UMA e nao "a folha carregou": a armadilha desta tabela nao e a peca faltar,
				// e o recorte se chamar outra coisa. `Perna` desenha "limb" e `Rabo` desenha "guts"
				// -- dois nomes que ninguem adivinha e que um teste de "a folha existe" nao pega. Uma
				// peca sem recorte nao vira node, e e exatamente isso que se mede aqui.
				foreach (PecaDeCorpo p in Enum.GetValues<PecaDeCorpo>())
				{
					if (p == PecaDeCorpo.Nenhuma) continue;
					int a = dec.GetChildCount();
					dec.Plantar(Protocol.Decal.Membro, new Vector2(9999, 9999), Facing.South, p);
					Conferir(dec.GetChildCount() > a && Decalques.UltimaPecaDeTeste == p,
						$"a peca {p} achou o recorte dela na folha e caiu no chao");
				}

				// ---------- A TABELA DO BYOND, PELOS DOIS NOMES QUE ENGANAM ----------
				// Do lado do Core, e por NOME de membro: e ali que o mapeamento espertinho por
				// minusculas quebraria. Ver `Body.PecaDe`.
				Conferir(Jandirus.Core.Combat.Body.PecaDe("Perna esquerda") == PecaDeCorpo.Perna,
					"PERNA cai como `Limb` e nao como um recorte de perna que nao existe");
				Conferir(Jandirus.Core.Combat.Body.PecaDe("Rabo") == PecaDeCorpo.Visceras,
					"RABO cai como `Guts`, que e o que o `/obj/bodyparts/Tail` do BYOND desenha");
				break;
			}

			case 3:
			{
				// ---------- O TETO DISPARA ----------
				// Regra da casa: teto que nunca e atingido e indistinguivel de teto nenhum. A
				// bancada estoura de proposito.
				int pedidosAntes = Decalques.PedidosDeTeste;
				for (int i = 0; i < Decalques.MaxVivos * 2; i++)
					dec.Plantar(Protocol.Decal.Sulco, new Vector2(i * 8, 0), Facing.East);

				Conferir(Decalques.PedidosDeTeste - pedidosAntes > Decalques.MaxVivos,
					$"a bancada ESTOUROU o teto de proposito: {Decalques.PedidosDeTeste - pedidosAntes}"
					+ $" pedidos contra teto {Decalques.MaxVivos}");
				Conferir(Decalques.VivosDeTeste <= Decalques.MaxVivos,
					$"e o teto SEGUROU: {Decalques.VivosDeTeste} vivos, nunca acima de {Decalques.MaxVivos}");
				break;
			}

			case 4:
			{
				// ---------- O PERMANENTE NAO EXPIROU ----------
				// Ja se passaram ~4 s desde que a terra foi pintada, e o prazo mais curto do sistema
				// (a fumaca) e 1,6 s. Se o chao danificado tivesse prazo, ja teria sumido.
				Conferir(Decalques.PermanentesDeTeste > 0,
					$"a terra revirada CONTINUA la depois de segundos ({Decalques.PermanentesDeTeste})");

				// ---------- A ZONA LIMPA ----------
				// E o unico jeito de ela sumir: o mapa recarregado do zero, que e o mesmo instante
				// em que as paredes derrubadas tambem voltam.
				dec.Limpar();
				Conferir(Decalques.VivosDeTeste == 0 && Decalques.PermanentesDeTeste == 0
						 && dec.GetChildCount() == 0,
					$"recarregar o mapa apaga TUDO, ate os permanentes ({dec.GetChildCount()} nodes)");
				Fotografar("user://decalques.png");
				break;
			}

			// ---------- A ONDA DA AGUA GIRA COM O CORPO DO DONO ----------
			// Este bloco existe por causa de um defeito que passou por toda a bancada acima: a onda
			// nascia, virava node, carregava arte e caia no centro da celula -- e estava VIRADA PRA
			// CIMA sempre, porque a direcao do corpo LOCAL nunca era lida. Tudo o que se media era
			// "plantou?"; ninguem media "plantou o QUE".
			//
			// Por isso aqui a tecla e apertada DE VERDADE e o que se le no fim e o nome da animacao
			// QUE O NODE RECEBEU -- a ponta mais perto do pixel que da pra ler sem foto. Medir
			// `OlharDeTeste` sozinho ficaria verde com a folha escolhendo o recorte errado depois.
			case 5:
				Godot.Input.ActionPress("move_right");
				break;

			case 6:
			{
				Godot.Input.ActionRelease("move_right");
				Facing d = mundo.DirecaoDoRastroDeTeste;
				Conferir(d == Facing.East,
					$"andando pra DIREITA, o rastro do corpo LOCAL le leste (leu {d})");
				_ondaDeLado = AnimDaOnda(dec, d);
				Conferir(_ondaDeLado == "ew",
					$"e a onda que nasce dai e a do eixo leste-oeste (recorte \"{_ondaDeLado}\")");
				Godot.Input.ActionPress("move_up");
				break;
			}

			case 7:
			{
				Godot.Input.ActionRelease("move_up");
				Facing d = mundo.DirecaoDoRastroDeTeste;
				Conferir(d == Facing.North, $"andando pra CIMA, o mesmo rastro le norte (leu {d})");
				string emPe = AnimDaOnda(dec, d);
				Conferir(emPe == "ns",
					$"e agora a onda e a do eixo norte-sul (recorte \"{emPe}\")");
				// O QUE O DONO FOTOGRAFOU: os dois sentidos davam o MESMO desenho. Duas conferencias
				// separadas passariam se a folha devolvesse "ns" nas duas -- esta e a que nao passa.
				Conferir(emPe != _ondaDeLado,
					$"e voar de lado NAO desenha a mesma onda de voar pra cima (\"{_ondaDeLado}\" x \"{emPe}\")");
				break;
			}

			case 8:
			{
				// ---------- PARADO MANTEM O ULTIMO SENTIDO ----------
				// Nenhuma tecla ha ~0,8 s. O rastro tem que continuar norte, e nao voltar pro sul
				// que e o padrao do campo -- corpo pairando sobre a agua nao vira sozinho, e uma onda
				// que gira quando o jogador solta a tecla e um efeito que se mexe sem motivo.
				Facing d = mundo.DirecaoDoRastroDeTeste;
				Conferir(d == Facing.North,
					$"PARADO, o rastro mantem o ultimo sentido em vez de saltar pro padrao (leu {d})");
				break;
			}

			default:
				_acabou = true;
				GD.Print("\n[decal] ===== BANCADA DOS DECALQUES =====");
				foreach (string l in _passos) GD.Print("[decal] " + l);
				GD.Print(_falhas.Count == 0
					? "[decal] ===== TUDO OK ====="
					: $"[decal] ===== {_falhas.Count} FALHA(S) =====\n[decal]   " + string.Join("\n[decal]   ", _falhas));
				break;
		}
	}

	/// <summary>Quantos dos 8 vizinhos esta celula pintaria. Usa a MESMA conta do mundo.</summary>
	private static int VizinhosDe(World mundo, int cx, int cy) => mundo.VizinhosPintadosDeTeste(cx, cy);

	/// <summary>O recorte da onda de lado, guardado pra ser comparado com o da onda em pe.</summary>
	private string _ondaDeLado = "";

	/// <summary>
	/// Planta a onda da agua com esta direcao e devolve a ANIMACAO QUE O NODE RECEBEU.
	///
	/// Le do node e nao do que se pediu de proposito: entre a direcao e o desenho ainda ha a escolha
	/// de recorte (`Decalques.Escolher`), e ela ja errou antes. Um teste que so conferisse a direcao
	/// mediria a INTENCAO e ficaria verde com a folha inteira caindo no recorte errado.
	/// </summary>
	private static string AnimDaOnda(Decalques dec, Facing dir)
	{
		dec.Plantar(Protocol.Decal.Agua, new Vector2(9999, 9999), dir);
		int n = dec.GetChildCount();
		return n > 0 && dec.GetChild(n - 1) is AnimatedSprite2D a ? a.Animation.ToString() : "";
	}

	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add("  ok     foto salva em " + caminho);
		}
		catch (Exception e) { _passos.Add("  --     sem foto: " + e.Message); }
	}
}
