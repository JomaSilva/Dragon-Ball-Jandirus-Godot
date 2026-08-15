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

			// ---------- OS QUATRO SENTIDOS, UM POR PASSO ----------
			// A ORDEM NAO E ARBITRARIA: o SUL fica no MEIO e nunca por ultimo. O defeito que o dono
			// fotografou era `DirecaoDe` devolvendo sul FIXO, e o passo do "parado" pergunta se o
			// rastro guardou o ultimo sentido -- terminando no sul, "guardou o ultimo" e "saltou pro
			// padrao" dariam a MESMA leitura e o passo ficaria verde com o bug de volta.
			case 6:
				Godot.Input.ActionRelease("move_right");
				Medir(mundo, dec, Facing.East, "DIREITA", "ew");
				Godot.Input.ActionPress("move_up");
				break;

			case 7:
				Godot.Input.ActionRelease("move_up");
				Medir(mundo, dec, Facing.North, "CIMA", "ns");
				Godot.Input.ActionPress("move_down");
				break;

			case 8:
				Godot.Input.ActionRelease("move_down");
				Medir(mundo, dec, Facing.South, "BAIXO", "ns");
				Godot.Input.ActionPress("move_left");
				break;

			case 9:
			{
				Godot.Input.ActionRelease("move_left");
				Medir(mundo, dec, Facing.West, "ESQUERDA", "ew");

				// ---------- OS QUATRO COMPARADOS ENTRE SI ----------
				// ============================ POR QUE NAO SAO QUATRO DESENHOS ============================
				// O roteiro pediu "quatro resultados diferentes entre si". A folha nao tem isso e nao
				// deveria ter: o `KiWater` do DU tem DUAS animacoes, e a regra e do proprio original
				// (`Unsorted 2.dm:375-380`) -- "NS" pra quem cruza no eixo norte-sul, "EW" pro
				// leste-oeste. A onda que um corpo abre indo pra leste e a mesma que ele abre voltando
				// pra oeste; ela e simetrica no eixo, e mandar desenhar quatro seria inventar arte que
				// o jogo original nao tem.
				//
				// Entao o que se afirma e o que da pra afirmar sem mentir, e ainda assim reprova o
				// defeito do dono: os quatro sentidos produzem EXATAMENTE DOIS desenhos. Um so (o
				// caso da foto: tudo "ns") reprova aqui; quatro tambem reprovaria, e seria sinal de
				// que alguem trocou a folha por baixo.
				// ======================================================================================
				string[] quatro = [_onda[0], _onda[1], _onda[2], _onda[3]];
				int distintos = quatro.Distinct().Count();
				Conferir(distintos == 2,
					$"os quatro sentidos dao exatamente DOIS desenhos, e nao um so "
					+ $"(leste \"{quatro[0]}\", norte \"{quatro[1]}\", sul \"{quatro[2]}\", oeste \"{quatro[3]}\")");

				// E OS PARES SAO OS DO EIXO, e nao dois pares quaisquer. Sem esta linha, um mapeamento
				// que trocasse norte com leste daria "dois desenhos" e passaria na de cima.
				Conferir(quatro[0] == quatro[3] && quatro[1] == quatro[2] && quatro[0] != quatro[1],
					$"...e eles se agrupam pelo EIXO: leste=oeste (\"{quatro[0]}\"), norte=sul (\"{quatro[1]}\")");
				break;
			}

			case 10:
			{
				// ---------- PARADO MANTEM O ULTIMO SENTIDO ----------
				// Nenhuma tecla ha ~0,8 s. O rastro tem que continuar OESTE, e nao voltar pro sul que
				// e o padrao do campo -- corpo pairando sobre a agua nao vira sozinho, e uma onda que
				// gira quando o jogador solta a tecla e um efeito que se mexe sem motivo.
				Facing d = mundo.DirecaoDoRastroDeTeste;
				Conferir(d == Facing.West,
					$"PARADO, o rastro mantem o ultimo sentido em vez de saltar pro padrao (leu {d})");
				Conferir(AnimDaOnda(dec, d) == _onda[3],
					"...e a onda que ele desenharia parado e a mesma de quando ele andava");
				break;
			}

			// ---------- O JATO DE SANGUE, COM DOIS CORPOS ----------
			case 11:
				DoisCorpos(mundo);
				break;

			case 12:
				OJatoNasceUmaVez(mundo);
				break;

			case 13:
				OTetoDasPecas(dec);
				break;

			case 14:
				OTiroTambemTemRumo(mundo, dec);
				break;

			case 15:
				OTetoDaAgua(dec);
				break;

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

	/// <summary>
	/// O recorte que a onda recebeu em cada sentido, na ordem leste, norte, sul, oeste. Guardado
	/// porque a afirmacao que interessa e a COMPARACAO entre os quatro -- ver o passo 9.
	/// </summary>
	private readonly string[] _onda = ["", "", "", ""];

	private int _sentidos;

	/// <summary>
	/// Anda um sentido, le a direcao do rastro do corpo LOCAL e o recorte que a onda recebeu.
	///
	/// As DUAS leituras porque sao duas pontes diferentes e as duas ja quebraram: a direcao vem do
	/// corpo (foi ela que respondia sul fixo pro dono) e o recorte vem da folha (`Decalques.Escolher`).
	/// Medir so a direcao ficaria verde com a folha inteira caindo no desenho errado.
	/// </summary>
	private void Medir(World mundo, Decalques dec, Facing esperado, string tecla, string recorte)
	{
		Facing d = mundo.DirecaoDoRastroDeTeste;
		Conferir(d == esperado, $"andando pra {tecla}, o rastro do corpo LOCAL le {esperado} (leu {d})");
		string anim = AnimDaOnda(dec, d);
		Conferir(anim == recorte,
			$"...e a onda que nasce dai e a do eixo \"{recorte}\" (recorte \"{anim}\")");
		if (_sentidos < _onda.Length) _onda[_sentidos++] = anim;
	}

	// =====================================================================
	// O JATO DE SANGUE -- DOIS CORPOS
	// =====================================================================
	/// <summary>Ids fora de qualquer faixa de jogador ou de bancada de servidor.</summary>
	private const int IdCarrasco = 970_100, IdVitima = 970_101;

	/// <summary>
	/// DOIS CORPOS NA TELA, pelo unico caminho por onde um corpo remoto nasce.
	///
	/// Montar os `RemotePlayer` na mao testaria o boneco que a bancada construiu; o `AoReceberSnapshot`
	/// e o que o servidor aciona, e e ele que poe o corpo na camada de atores -- que e justamente a
	/// camada onde o jato tem que nascer.
	/// </summary>
	private void DoisCorpos(World mundo)
	{
		Vector2 onde = mundo.PosicaoLocal ?? Vector2.Zero;
		mundo.AoReceberSnapshot([
			new EntityState
			{
				Id = IdCarrasco, Pos = new Vec2(onde.X + 48, onde.Y),
				Facing = (byte)Jandirus.Core.World.Facing.West, Pose = Protocol.Pose.Normal,
			},
			new EntityState
			{
				Id = IdVitima, Pos = new Vec2(onde.X + 80, onde.Y),
				Facing = (byte)Jandirus.Core.World.Facing.East, Pose = Protocol.Pose.Normal,
			},
		]);
		Conferir(mundo.CorpoDeTeste(IdCarrasco) != null && mundo.CorpoDeTeste(IdVitima) != null,
			"os DOIS corpos entraram na camada de atores (o jato precisa de um pra seguir)");
	}

	/// <summary>
	/// O JATO SAI DE UMA AMPUTACAO, UMA VEZ -- e NAO sai do soco que nao amputou.
	///
	/// ============================ POR QUE O CONTRA-EXEMPLO VEM PRIMEIRO ============================
	/// "Sair sempre" e a forma mais comum deste efeito quebrar (um `if` que some, um bit lido do
	/// campo errado), e ela fica VERDE em qualquer bancada que so pergunte "saiu?". Entao o primeiro
	/// golpe daqui e um acerto normal, com dano e membro atingido, e o que se afirma e que NADA
	/// nasceu. So depois vem o golpe que decepa.
	///
	/// O relato entra pelo `World.GolpeDeTeste`, que e o proprio `AoGolpe` de producao -- o mesmo
	/// metodo que o `S2C.Hit` do servidor aciona. A bancada nao chama `CombatFx.JatoDeSangue`: se ela
	/// chamasse, mediria a funcao e nao o gatilho, e o gatilho e o que estava em duvida.
	/// ============================================================================================
	/// </summary>
	private void OJatoNasceUmaVez(World mundo)
	{
		if (mundo.CorpoDeTeste(IdVitima) is not { } vitima)
		{
			Conferir(false, "a vitima forjada continua na camada de atores");
			return;
		}
		Node pai = vitima.GetParent();

		var comum = new Protocol.HitEvent
		{
			Atacante = IdCarrasco, Alvo = IdVitima,
			Desfecho = (byte)Jandirus.Core.Combat.Desfecho.Acertou, Nivel = 1,
			TemDano = true, Dano = 12.5f, Membro = "Braco esquerdo",
		};

		CombatFx.JatosDeTeste = 0;
		int antes = pai.GetChildCount();
		mundo.GolpeDeTeste(comum);

		// ============================ O CONTRA-EXEMPLO NAO CONTA NODES ============================
		// A primeira versao desta linha exigia que a camada de atores nao ganhasse node NENHUM, e a
		// bancada a reprovou: um acerto comum ja nasce com a FAISCA do impacto (`CombatFx.Impacto`) e
		// com o piscar do corpo. Estava certa a bancada e errada a expectativa -- "nao nasceu nada" e
		// uma afirmacao sobre o soco inteiro, e o que se mede aqui e o JATO.
		//
		// O contador serve porque ele e incrementado DEPOIS de a folha do `Blood Spray` carregar (ver
		// `CombatFx.JatosDeTeste`): ele nao conta chamadas, conta jatos que teriam aparecido.
		// =======================================================================================
		Conferir(CombatFx.JatosDeTeste == 0,
			$"um acerto COMUM (com dano e membro) nao faz jato nenhum ({CombatFx.JatosDeTeste} jatos,"
			+ $" {pai.GetChildCount() - antes} outros efeitos nasceram)");

		// ...e agora o mesmo golpe, com o bit que o servidor marca quando o membro sai.
		Protocol.HitEvent decepou = comum;
		decepou.Decepou = true;
		mundo.GolpeDeTeste(decepou);

		Conferir(CombatFx.JatosDeTeste == 1,
			$"a amputacao faz o jato nascer UMA vez ({CombatFx.JatosDeTeste})");

		// NASCEU NODE COM ARTE, e nao so um contador. O modo de falhar deste efeito e o silencio: a
		// folha `Blood Spray.tres` sumir da conversao devolve `null` no `Load` e o `JatoDeSangue`
		// volta sem desenhar nada -- e uma bancada que so contasse chamadas ficaria verde.
		AnimatedSprite2D? jato = null;
		foreach (Node n in pai.GetChildren())
			if (n is AnimatedSprite2D s && s.Animation == "default" && s.SpriteFrames != null) jato = s;
		Conferir(jato != null, "e ele VIROU NODE com a folha do `Blood Spray` carregada");

		// E ELE SEGUE O CORPO. O elo e um `RemoteTransform2D` filho da vitima -- ver `CombatFx`, que
		// explica por que o jato nao pode ser filho do corpo (perder um membro pode matar, e o efeito
		// morreria junto com o node do morto no meio da animacao).
		RemoteTransform2D? elo = null;
		foreach (Node n in vitima.GetChildren())
			if (n is RemoteTransform2D rt) elo = rt;
		Conferir(elo != null && jato != null && elo.RemotePath == jato.GetPath(),
			"e o elo que faz o jato ACOMPANHAR o corpo aponta pra ele (e nao e filho dele)");

		// SO NA VITIMA. Se o jato nascesse no atacante, o dono veria o sangue jorrar de quem bateu.
		Conferir(jato != null && jato.Position.DistanceTo(vitima.GlobalPosition) < 1f,
			"e ele nasce em cima de QUEM PERDEU o membro, nao de quem bateu");
	}

	// =====================================================================
	// O TETO DAS PECAS NUMA BRIGA LONGA
	// =====================================================================
	/// <summary>
	/// AS DUAS GUARDAS DA FILA DAS PECAS, e elas apontam pra lados opostos (ver `Decalques.MaxPecas`).
	///
	/// A `--pecateste` do servidor ja prova que peca so nasce de amputacao. O que sobra e o cliente:
	/// numa briga de dez pessoas se destrocando, o chao nao pode encher sem fim -- e, ao mesmo tempo,
	/// a peca nao pode ser varrida pela poeira, que nasce as dezenas por segundo e e sempre mais nova.
	/// A segunda metade e a que ninguem lembra de testar, e e a que faz a peca durar os 60 s dela.
	///
	/// AS DUAS FORAM MEDIDAS POR INJECAO DE DEFEITO, e cada uma tem a sua linha vermelha: sem a cota
	/// das pecas o chao ficou com 64 delas; sem a preferencia do despejo, as 32 viraram ZERO depois
	/// de uma leva de poeira. Uma linha so nao pegaria as duas.
	/// </summary>
	private void OTetoDasPecas(Decalques dec)
	{
		dec.Limpar();

		for (int i = 0; i < Decalques.MaxPecas * 2; i++)
			dec.Plantar(Protocol.Decal.Membro, new Vector2(i * 8, 500), Facing.South, PecaDeCorpo.Braco);

		Conferir(dec.PecasVivasDeTeste == Decalques.MaxPecas,
			$"o dobro do teto de pecas foi pedido e o chao segurou {Decalques.MaxPecas}"
			+ $" ({dec.PecasVivasDeTeste} vivas)");

		// ---------- E A POEIRA NAO VARRE AS PECAS ----------
		// Estourar o teto GERAL com marcas que nao sao peca: na fila crua o mais velho sai, e o mais
		// velho e sempre a peca. Este e o defeito que o despejo por criterio existe pra impedir.
		int pecasAntes = dec.PecasVivasDeTeste;
		for (int i = 0; i < Decalques.MaxVivos * 2; i++)
			dec.Plantar(Protocol.Decal.Sulco, new Vector2(i * 8, 600), Facing.East);

		Conferir(dec.PecasVivasDeTeste == pecasAntes,
			$"e {Decalques.MaxVivos * 2} marcas de poeira depois, as {pecasAntes} pecas continuam no chao"
			+ $" ({dec.PecasVivasDeTeste})");
		Conferir(Decalques.VivosDeTeste <= Decalques.MaxVivos,
			$"...sem furar o teto geral ({Decalques.VivosDeTeste} de {Decalques.MaxVivos})");

		// ...E A PECA TAMBEM NAO AFOGA A POEIRA: as 32 sao uma COTA dentro dos 120, e o que sobra
		// continua sendo da marca comum. Um teto de pecas igual ao geral tiraria a poeira do jogo.
		Conferir(Decalques.VivosDeTeste - dec.PecasVivasDeTeste > Decalques.MaxVivos / 2,
			$"e a poeira ficou com o resto dos lugares ({Decalques.VivosDeTeste - dec.PecasVivasDeTeste}"
			+ $" de {Decalques.MaxVivos})");

		dec.Limpar();
	}

	// =====================================================================
	// O TIRO TAMBEM TEM RUMO
	// =====================================================================
	/// <summary>Ids fora de qualquer faixa de tiro de verdade.</summary>
	private const int IdRaioDeTeste = 970_200, IdBolaDeTeste = 970_201;

	/// <summary>
	/// UM RAIO NAO E CORPO -- e ate agora ele nao tinha por onde ser lido.
	///
	/// ============================ ESTE E O MESMO DEFEITO, NA TERCEIRA PORTA ============================
	/// O `World.DirecaoDe` responde a partir do node. Ele tinha DUAS respostas (corpo local e corpo
	/// remoto) e um `_ => South` no fim. O corpo do DONO ja caiu nesse `else` uma vez, e o dono
	/// fotografou: voando de lado sobre a agua, a onda continuava em pe. Um tiro cai no MESMO buraco --
	/// e um raio atravessando um lago de leste a oeste desenharia riscos verticais o caminho inteiro.
	///
	/// Entao aqui a leitura e a de sempre: o node nasce pelo caminho de producao (`TiroDeTeste` chama
	/// o mesmo `AoNascerTiro`/`AoMoverTiros` do servidor), a direcao sai do MESMO `DirecaoDe`, e o que
	/// se afirma no fim e o RECORTE que a folha devolveu -- nao a intencao.
	/// ==================================================================================================
	///
	/// E OS DOIS TIPOS, porque o rumo sai de fontes diferentes: o RAIO tem cauda no snapshot (rumo =
	/// cabeca - cauda, exato); a BOLA nao tem, e o rumo sai do PASSO entre dois pacotes. Medir so o
	/// raio deixaria a metade mais fragil sem teste.
	/// </summary>
	private void OTiroTambemTemRumo(World mundo, Decalques dec)
	{
		Vector2 onde = mundo.PosicaoLocal ?? Vector2.Zero;

		// ---------- O RAIO, PELA CAUDA ----------
		// Cabeca a leste da cauda: o feixe vai pra LESTE, e o eixo tem que ser "ew".
		mundo.TiroDeTeste(IdRaioDeTeste, onde + new Vector2(96, 0), onde,
						  Jandirus.Core.Combat.TipoDeProjetil.Beam);
		Facing dirRaio = mundo.DirecaoDoTiroDeTeste(IdRaioDeTeste);
		Conferir(dirRaio == Facing.East,
			$"o RAIO indo pra leste le East pela cauda que ja viaja (leu {dirRaio})");
		Conferir(AnimDaOnda(dec, dirRaio) == "ew", "...e a onda que ele abre e a do eixo \"ew\"");

		// E VIRANDO PRO NORTE, o mesmo raio muda de eixo. Sem esta metade, um rumo travado em East
		// passaria na de cima.
		mundo.TiroDeTeste(IdRaioDeTeste, onde + new Vector2(0, -96), onde,
						  Jandirus.Core.Combat.TipoDeProjetil.Beam);
		Facing dirNorte = mundo.DirecaoDoTiroDeTeste(IdRaioDeTeste);
		Conferir(dirNorte == Facing.North, $"...e o mesmo raio apontado pro norte le North (leu {dirNorte})");
		Conferir(AnimDaOnda(dec, dirNorte) == "ns", "...com a onda no OUTRO eixo (\"ns\")");

		// ---------- A BOLA, PELO PASSO ----------
		// Ela nasce parada (cauda = cabeca) e so o segundo pacote diz pra onde ela vai.
		mundo.TiroDeTeste(IdBolaDeTeste, onde, onde, Jandirus.Core.Combat.TipoDeProjetil.Blast);
		mundo.TiroDeTeste(IdBolaDeTeste, onde + new Vector2(0, 64), onde + new Vector2(0, 64),
						  Jandirus.Core.Combat.TipoDeProjetil.Blast);
		Facing dirBola = mundo.DirecaoDoTiroDeTeste(IdBolaDeTeste);
		Conferir(dirBola == Facing.South,
			$"a BOLA, que nao tem cauda no fio, tira o rumo do PASSO entre dois pacotes (leu {dirBola})");

		// ============================ OS DOIS RECORTES SAO DIFERENTES, E ISSO E UMA AFIRMACAO PROPRIA ============================
		// As duas linhas de cima cobram cada recorte contra um nome escrito ("ew", "ns"), e isso ja
		// pega o eixo travado. Falta a afirmacao que nao depende de nome nenhum: **o raio deitado e o
		// raio em pe nao podem receber o MESMO desenho**.
		//
		// Ela existe porque o nome e o desenho sao coisas separadas. A folha `KiWater` pode um dia ter
		// as duas animacoes apontando pro mesmo recorte, ou o `Escolher` pode devolver o mesmo node
		// pras duas -- e as duas linhas de cima continuariam verdes, lendo "ew" e "ns" de dois
		// desenhos identicos. E a mesma queixa que o dono mandou EM FOTO uma vez: os quatro sentidos
		// com o mesmo risco na tela.
		// =====================================================================================================================
		string recorteLeste = AnimDaOnda(dec, dirRaio);
		string recorteNorte = AnimDaOnda(dec, dirNorte);
		Conferir(EixosDiferentes(recorteLeste, recorteNorte),
			$"o raio DEITADO e o raio EM PE recebem recortes DIFERENTES (\"{recorteLeste}\" x \"{recorteNorte}\")");

		// ---------- A INJECAO: O EIXO TRAVADO ----------
		// O defeito de verdade tem nome e endereco: o `_ => South` do `World.DirecaoDe`, o `else` em
		// que o tiro caia antes de ele ganhar a terceira fonte. Com ele no lugar, os DOIS raios --
		// o de leste e o do norte -- respondiam a mesma coisa, e a onda saia no mesmo eixo o caminho
		// inteiro. Aqui o MESMO detector da linha de cima recebe essas duas leituras, e tem que
		// ficar vermelho.
		const Facing RumoDoDefeitoAntigo = Facing.South;   // o `_ => South`, que nao olha pro tiro
		string travadoA = AnimDaOnda(dec, RumoDoDefeitoAntigo);
		string travadoB = AnimDaOnda(dec, RumoDoDefeitoAntigo);
		Conferir(!EixosDiferentes(travadoA, travadoB),
			$"[injecao] com o rumo travado no `_ => South`, os dois raios dao o MESMO recorte "
			+ $"(\"{travadoA}\" x \"{travadoB}\") -- e a comparacao de cima fica VERMELHA");

		mundo.TirarTiroDeTeste(IdRaioDeTeste);
		mundo.TirarTiroDeTeste(IdBolaDeTeste);
	}

	/// <summary>
	/// O DETECTOR DO EIXO, numa funcao so -- e e por isso que a injecao acima vale: ela alimenta ESTA
	/// funcao com o que o defeito antigo produzia, em vez de reescrever a comparacao ao lado dela.
	/// Comparacao escrita duas vezes concorda sempre consigo mesma.
	/// </summary>
	private static bool EixosDiferentes(string a, string b)
		=> a.Length > 0 && b.Length > 0 && a != b;

	// =====================================================================
	// A COTA DA AGUA
	// =====================================================================
	/// <summary>
	/// A AGUA NAO PODE VARRER O RESTO DO CHAO.
	///
	/// A cota da peca existe pra PROTEGER a peca; esta existe pra CONTER a agua -- ver
	/// `Decalques.MaxAgua`. Um raio cruzando um lago abre uma onda por celula, e como cada uma vive
	/// 2 s, meia duzia de raios encheria sozinha os 120 lugares e a cratera e a poeira da briga ao
	/// lado sumiriam da tela. As duas afirmacoes sao as duas metades disso: a agua PARA no teto dela,
	/// e o que sobra continua sendo dos outros.
	/// </summary>
	private void OTetoDaAgua(Decalques dec)
	{
		dec.Limpar();

		for (int i = 0; i < Decalques.MaxAgua * 3; i++)
			dec.Plantar(Protocol.Decal.Agua, new Vector2(i * 8, 800), Facing.East);

		Conferir(dec.AguasVivasDeTeste == Decalques.MaxAgua,
			$"o triplo da cota de agua foi pedido e o chao segurou {Decalques.MaxAgua}"
			+ $" ({dec.AguasVivasDeTeste} vivas)");
		Conferir(Decalques.VivosDeTeste <= Decalques.MaxVivos,
			$"...sem furar o teto geral ({Decalques.VivosDeTeste} de {Decalques.MaxVivos})");

		// E SOBRA CHAO PRO RESTO: a cratera plantada depois da enxurrada continua nascendo.
		int antes = dec.GetChildCount();
		dec.Plantar(Protocol.Decal.Cratera, new Vector2(0, 900), Facing.South);
		Conferir(dec.GetChildCount() > antes,
			"e depois da enxurrada ainda ha lugar pra marca de briga (a cratera nasceu)");

		dec.Limpar();
		ORaioLongoNaoEntulhaAFila(dec);
	}

	// =====================================================================
	// O RAIO LONGO CONTRA A FILA DOS OUTROS
	// =====================================================================
	/// <summary>
	/// UM RAIO LONGO CARIMBA UMA MARCA POR TILE, E O CHAO TEM 120 LUGARES.
	///
	/// ============================ POR QUE O SULCO NAO GANHOU COTA PROPRIA ============================
	/// A agua ganhou (`MaxAgua`) porque ela e BARATA de refazer -- dura 2 s e o proximo tiro que passar
	/// a repinta. O sulco do raio nao: ele e a marca do que aconteceu, e cortar a cota dele faria o
	/// rastro sumir pelo meio enquanto o raio ainda esta desenhado por cima. Entao ele entra na fila
	/// geral, e o que protege o resto da tela e a cota da PECA (`MaxPecas`), que existe justamente pra
	/// isso: um braco arrancado nao pode ser varrido por efeito de passagem.
	///
	/// As tres afirmacoes sao as tres metades do pedido, e a primeira e a que impede as outras duas de
	/// ficarem verdes por ausencia: (1) o raio pediu MAIS que o teto -- senao nao houve enxurrada
	/// nenhuma; (2) o teto segurou; (3) **o que estava protegido continua na tela**.
	/// ==============================================================================================
	/// </summary>
	private void ORaioLongoNaoEntulhaAFila(Decalques dec)
	{
		dec.Limpar();

		// A BRIGA QUE JA ESTAVA NA TELA: oito membros no chao, dentro da cota deles.
		const int Membros = 8;
		for (int i = 0; i < Membros; i++)
			dec.Plantar(Protocol.Decal.Membro, new Vector2(i * 32, 1200), Facing.South,
						PecaDeCorpo.Braco);
		int pecasAntes = dec.PecasVivasDeTeste;

		// E AGORA O RAIO: uma marca por tile, o dobro do teto de decalques vivos. Sao ~3 raios de
		// alcance cheio cruzando a mesma tela, que e o pior caso realista de uma briga de ki.
		int pedidosAntes = Decalques.PedidosDeTeste;
		for (int i = 0; i < Decalques.MaxVivos * 2; i++)
			dec.Plantar(Protocol.Decal.Sulco, new Vector2(i * ZoneCollision.TileSize, 1300), Facing.East);

		Conferir(Decalques.PedidosDeTeste - pedidosAntes > Decalques.MaxVivos,
			$"(controle) o raio longo pediu MAIS marcas que o teto ({Decalques.PedidosDeTeste - pedidosAntes}"
			+ $" pedidas, teto {Decalques.MaxVivos}) -- houve enxurrada de verdade");
		Conferir(Decalques.VivosDeTeste <= Decalques.MaxVivos,
			$"...e o teto do chao segurou ({Decalques.VivosDeTeste} de {Decalques.MaxVivos})");
		Conferir(dec.PecasVivasDeTeste == pecasAntes && pecasAntes == Membros,
			$"...e o rastro do raio NAO varreu o que estava protegido: os {Membros} membros continuam"
			+ $" na tela ({dec.PecasVivasDeTeste} de {pecasAntes})");

		dec.Limpar();
	}

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
