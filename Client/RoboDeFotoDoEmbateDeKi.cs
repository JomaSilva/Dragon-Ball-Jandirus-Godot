using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// AS FOTOS DA COLISAO DE KI (`--diagembateki`) -- os feixes se empurrando, e a explosao do empate.
///
/// ============================ O QUE SO A FOTO RESPONDE ============================
/// A `--embatekiteste` (87 afirmacoes, sem janela) ja mede tudo o que se mede em numero: o gatilho, a
/// fisica do medidor, o pixel que cada acerto empurra, o encontro CHEGANDO ao corpo, o preco do empate
/// e as tres bordas. Nada aqui repete aquilo.
///
/// O que fica de fora dela e o caminho inteiro entre o `Feixe.Pos` do servidor e o feixe DESENHADO: o
/// snapshot, o `ProjetilDesenhado`, a interpolacao do `_Process` e a camada. Uma bancada de servidor
/// fica verde com os dois feixes desenhados do mesmo tamanho, com um deles invisivel, ou com a
/// explosao do empate acontecendo fora da tela -- e o pedido do dono e sobre a IMAGEM: *"CADA ACERTO
/// EMPURRA O BEAM DO INIMIGO PRA TRAS"*, *"acontece uma EXPLOSAO... e sao JOGADOS PRA TRAS"*.
///
/// Por isso as conferencias daqui leem o DESENHO e nao o servidor: <see cref="World.TirosDesenhados"/>
/// (a `Position` do node depois da interpolacao) e <see cref="World.PosicaoDesenhadaDe"/>. O servidor
/// so e consultado pra saber EM QUE INSTANTE apertar o obturador -- que e coisa de quem dirige a cena,
/// nao de quem a mede.
/// =================================================================================
///
/// COMO RODAR -- um processo so, hospedando, e ele PRECISA de janela (no headless o `GetImage` volta
/// vazio):
///
///     Godot --path . --host --rede 7910 --diagembateki --position 1920,0 --resolution 1600x900 ^
///           --horateste 0.5 --raca Human --conta bancada_embateki --nome Embate
///
/// `--horateste 0.5` crava meio-dia: a hora do mundo e sorteada, e uma foto de duelo as 3 da manha
/// mostra dois vultos. As fotos saem em `user://embateki-*.png`.
///
/// O CEU A BANCADA CRAVA SOZINHA (2026-10-08): o `--horateste` so mexe na hora, e o clima natural muda de
/// bloco a cada seis minutos e de sorteio a cada dia do jogo -- a mesma linha de comando fotografava
/// tempestade numa hora e ceu aberto na outra. Antes da primeira cena ela pede ceu limpo ao servidor
/// (`EmbateDeFoto_CeuLimpo`), e a cena 3 confere que foi atendida. So um `--climateste` explicito passa por
/// cima, e e esse o contra-exemplo do palco: com `--climateste Tempestade` a cena 3 volta a reprovar.
/// </summary>
public partial class RoboDeFotoDoEmbateDeKi : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];
	private readonly List<(Image Foto, Vector2 Centro)> _tira = [];

	private bool _acabou;
	private double _t, _vida;
	private int _passo, _apertos;
	private int _duelistaA, _duelistaB;

	/// <summary>Depois disto ela desiste -- o mapa pode nao ter corredor de chao pra a cena caber.</summary>
	private const double Paciencia = 180;

	/// <summary>O que a tela mostrava no encontro (meter 50) -- a regua da foto do empurrao.</summary>
	private float _feixeDeANoComeco, _feixeDeBNoComeco;

	/// <summary>O ultimo retrato dos dois corpos ANTES do empate: e com ele que o preco se le.</summary>
	private (double VidaA, double VidaB, Vec2 PosA, Vec2 PosB) _antesDoEstouro;
	private double _corridosVistos;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Ready()
	{
		// A ARVORE E PAUSADA PRA FOTOGRAFAR A ESTRELA (ver `Obturador`). Sem isto a propria bancada
		// congelaria junto e a pausa seria eterna.
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagembateki` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }

		_t += delta;

		// A CAMERA VAI PRA CENA assim que os dois duelistas dela estao desenhados -- ver `World.FocoDeTeste`.
		// Uma vez por cena, e nao a cada quadro: a foto e o recorte dela sao do MESMO enquadramento.
		if (_enquadrar && mundo.PosicaoDesenhadaDe(_duelistaA) is { } pa && mundo.PosicaoDesenhadaDe(_duelistaB) is { } pb)
		{
			mundo.FocoDeTeste = (pa + pb) / 2f;
			_enquadrar = false;
		}

		switch (_passo)
		{
			case 0: MontarOEmpurrao(srv, cli); break;
			case 1: EsperarOEncontro(srv, mundo); break;
			case 2: Empurrar(srv, mundo); break;
			case 3: MontarOEmpate(srv, cli); break;
			case 4: EsperarOEmpate(srv, mundo); break;
			case 5: DepoisDaOnda(srv, mundo); break;
			case 6: MontarOsFinalFlash(srv, cli); break;
			case 7: MedirOsFinalFlash(srv, mundo); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	/// <summary>Uma cena nova entrou no mundo e a camera ainda nao foi ate ela.</summary>
	private bool _enquadrar;

	/// <summary>O pedido de ceu limpo ja foi feito ao servidor (uma vez, antes da primeira cena).</summary>
	private bool _ceuCravado;

	// =====================================================================
	// CENA 1: OS FEIXES SE EMPURRANDO
	// =====================================================================
	/// <summary>
	/// O LADO A NASCE 1,5x MAIS FORTE, e isso e o que a foto precisa e nao um favor.
	///
	/// Em forcas exatamente parelhas um jogador que acerta TODAS as letras empata com a taxa automatica
	/// do outro lado -- e de proposito, e a calibragem do `ApertosPorLetra` (medida na
	/// `--embatekiteste`). O empate e a CENA 2. Pra haver foto de EMPURRAO, tem que haver quem empurre.
	/// </summary>
	private void MontarOEmpurrao(Jandirus.Server.GameServer srv, GameClient cli)
	{
		// O CEU E CRAVADO ANTES DE TUDO -- o `--horateste` so crava a hora, e a mesma linha de comando
		// fotografava tempestade num dia do jogo e ceu aberto no outro (ver `EmbateDeFoto_CeuLimpo`).
		if (!_ceuCravado) { srv.EmbateDeFoto_CeuLimpo(cli.LocalId); _ceuCravado = true; }

		if (_t < 3) return;   // deixa o mundo assentar, a camera achar o corpo e a chuva que ja caia sair da tela

		(_duelistaA, _duelistaB) = srv.EmbateDeFoto_Montar(
			cli.LocalId, tiles: 5, bpDeA: 7_500, bpDeB: 5_000, tecladoEmA: true, tilesAbaixo: 3);

		Conferir(_duelistaA != 0 && _duelistaB != 0,
			"os dois duelistas entraram no mundo, num corredor de chao de verdade");
		if (_duelistaA == 0) { Nota("sem corredor livre: a cena nao cabe neste pedaco de mapa"); Fechar(); return; }

		_enquadrar = true;
		Virar(1);
	}

	/// <summary>
	/// A FOTO SO VALE COM OS DOIS FEIXES JA ENCOSTADOS. Ela nao sai no relogio: sai quando o GATILHO de
	/// producao juntou as duas cabecas -- antes disso o que ha na tela sao dois tiros a caminho, que e
	/// outra imagem.
	/// </summary>
	private void EsperarOEncontro(Jandirus.Server.GameServer srv, World mundo)
	{
		// A FOTO JA SAIU: falta ler as duas pontas no pixel, com a arvore pausada enquanto as chapas saem.
		if (_encontroFotografado)
		{
			if (!ObturadorDasPontas(mundo, comDefeito: false)) return;
			AfirmarAsPontas("CENA 1", $"folga de {_folgaDosCampos:0.0} px entre os dois nodes");
			GuardarAsPontas("CENA 1", "user://embateki-1-pontas", naTira: false);
			Virar(2);
			return;
		}

		(bool existe, double medidor, Vec2 ponto, _, _, float fa, float fb) = srv.EmbateDeFoto_Estado();
		if (!existe)
		{
			if (_t > 20) { Conferir(false, "os dois raios se encontraram e viraram disputa"); Fechar(); }
			return;
		}

		// ============================ O DESENHO CHEGA DEPOIS DO GATILHO (2026-09-23) ============================
		// O gatilho e do servidor, e o cliente desenha cada cabeca um pacote atras (o `Lerp` do
		// `ProjetilDesenhado`): no quadro do gatilho as cabecas DESENHADAS ainda estavam a caminho, e a foto
		// "do encontro" saia com grama entre elas -- justamente a imagem de que o dono reclamou, ao contrario.
		// Espera-se o desenho encostar, com prazo curto (o medidor comeca a andar), e a folga vira regra.
		// ======================================================================================================
		if (_encontroEm < 0) _encontroEm = _vida;
		float folga = FolgaEntreAsCabecasDesenhadas(mundo, ponto);
		if (MathF.Abs(folga) > ToleranciaDoEncontro && _vida - _encontroEm < 0.6) return;

		// ============================ HA ALGUEM DENTRO DA FOTO? ============================
		// A mesma linha que a `--diagraio` pagou caro pra aprender: um corpo forjado sem `Visual` nao
		// desenha sprite nenhum, e TODAS as checagens de posicao ficam verdes porque a posicao existe
		// de qualquer jeito. A foto sairia com dois feixes saindo do nada.
		// =================================================================================
		Conferir(mundo.CorpoDeTeste(_duelistaA) != null && mundo.CorpoDeTeste(_duelistaB) != null,
			"os dois duelistas tem CORPO DESENHADO na tela (e nao so uma posicao)");

		int naTela = ContarFeixes(mundo, out float cabecaDeA, out float cabecaDeB);
		Conferir(naTela >= 2, $"os DOIS feixes estao desenhados na tela ({naTela} tiro(s) no quadro)");

		_feixeDeANoComeco = cabecaDeA;
		_feixeDeBNoComeco = cabecaDeB;

		Fotografar("user://embateki-1-encontro.png",
				   $"CENA 1: as duas cabecas se encontram (medidor {medidor:0}, feixes de "
				   + $"{cabecaDeA:0} e {cabecaDeB:0} px na tela)", NaTela(ponto));
		Conferir(MathF.Abs(folga) <= ToleranciaDoEncontro,
			$"CENA 1: pelos CAMPOS as duas cabecas ja se encostaram na hora da foto -- a distancia entre os dois nodes "
			+ $"menos as duas frentes do Core (folga {folga:0.0} px; negativo e sobreposicao). Quem le a FOTO sao as "
			+ "linhas das pontas, logo abaixo");
		_folgaDosCampos = folga;
		_encontroFotografado = true;
	}

	/// <summary>O instante (no relogio da bancada) em que o gatilho juntou as cabecas da cena 1.</summary>
	private double _encontroEm = -1;

	/// <summary>A foto do encontro da cena 1 ja saiu, e o que os campos diziam da folga naquele quadro.</summary>
	private bool _encontroFotografado;
	private float _folgaDosCampos;

	/// <summary>Quanto a folga desenhada pode passar de zero: meio pixel de arredondamento de cada lado e um de sobra.</summary>
	private const float ToleranciaDoEncontro = 2f;

	/// <summary>
	/// A FOLGA DOS CAMPOS: a distancia entre os NODES das duas cabecas perto do encontro, menos a frente de
	/// cada uma pelo Core (`Feixe.AlcanceDaCabeca`). Positiva e vao, negativa e uma dentro da outra. Sem
	/// exatamente duas cabecas por perto, e infinita.
	///
	/// ELA ESCOLHE O INSTANTE DA FOTO (o desenho de cada cabeca chega um pacote depois do gatilho) e nao diz
	/// onde a tinta acaba: quem le a foto e o <see cref="LerAsPontas"/>.
	/// </summary>
	private static float FolgaEntreAsCabecasDesenhadas(World mundo, Vec2 ponto)
	{
		var perto = new Vector2(ponto.X, ponto.Y);
		var cabecas = new List<(Vector2 Onde, float Frente)>();
		foreach ((int _, Jandirus.Core.Combat.ArteDeKi arte, Jandirus.Core.Combat.TipoDeProjetil tipo,
				  Vector2 onde, float escala) in mundo.TirosDesenhados())
			if (tipo == Jandirus.Core.Combat.TipoDeProjetil.Beam && onde.DistanceTo(perto) < 6 * ZoneCollision.TileSize)
				cabecas.Add((onde, Jandirus.Core.Combat.Feixe.AlcanceDaCabeca(arte, escala)));
		if (cabecas.Count != 2) return float.PositiveInfinity;
		return cabecas[0].Onde.DistanceTo(cabecas[1].Onde) - cabecas[0].Frente - cabecas[1].Frente;
	}

	/// <summary>
	/// O EMPURRAO, E ELE E DIRIGIDO PELA LETRA. O robo responde TODO quadro -- ou seja ele e o "cliente
	/// que responde em 1 ms" contra o qual o piso de cadencia existe (ver `--pressateste`, familia 6):
	/// o servidor so entrega letra nova a cada 300 ms, e e por isso que a cena dura o que dura em vez
	/// de acabar em dois segundos.
	/// </summary>
	private void Empurrar(Jandirus.Server.GameServer srv, World mundo)
	{
		if (srv.EmbateDeFoto_Apertar()) _apertos++;

		(bool existe, double medidor, Vec2 ponto, double corridos, _, _, _) = srv.EmbateDeFoto_Estado();

		// ---- A FOTO DO EMPURRAO: com o medidor bem fora do meio, os dois feixes ficam desiguais ----
		if (existe && medidor >= 78 && _tira.Count == 1)
		{
			int naTela = ContarFeixes(mundo, out float cabecaDeA, out float cabecaDeB);
			Fotografar("user://embateki-2-empurrando.png",
					   $"CENA 1: {_apertos} acertos depois -- medidor {medidor:0}, feixes de "
					   + $"{cabecaDeA:0} e {cabecaDeB:0} px", NaTela(ponto));

			// ============================ A LEITURA E DO DESENHO, E NAO DO SERVIDOR ============================
			// O que se afirma aqui e o pedido do dono na moeda dele: **na TELA**, o feixe de quem acerta
			// esticou e o de quem apanha encolheu. O `Feixe.Pos` do servidor ja tem afirmacao propria na
			// `--embatekiteste` (familia 3b, em pixel) -- e ela ficaria verde com o cliente desenhando os
			// dois do mesmo tamanho.
			// ============================================================================================
			Conferir(naTela >= 2 && cabecaDeA > _feixeDeANoComeco + 16
					 && cabecaDeB < _feixeDeBNoComeco - 16,
				$"NA TELA, o feixe de quem acerta ESTICOU ({_feixeDeANoComeco:0} -> {cabecaDeA:0} px) e o "
				+ $"do outro ENCOLHEU ({_feixeDeBNoComeco:0} -> {cabecaDeB:0} px)");
			return;
		}

		if (existe) { _corridosVistos = corridos; _pontoDoEstouro = ponto; return; }

		// ---- A DISPUTA ACABOU: a vitoria de quem empurrou ----
		// O ENQUADRAMENTO E O ULTIMO PONTO VISTO, e nao o que o estado devolve agora: acabada a disputa
		// o `EmbateDeFoto_Estado` devolve zerado, e `NaTela(0,0)` recorta a QUINA da tela -- foi assim
		// que o terceiro quadro da primeira tira saiu mostrando o painel de vida.
		Fotografar("user://embateki-3-vitoria.png",
				   $"CENA 1: o feixe vencedor avanca ({_apertos} acertos em {_corridosVistos:0.#}s)",
				   NaTela(_pontoDoEstouro));

		Conferir(_tira.Count >= 3, $"as tres fotos da cena 1 sairam ({_tira.Count})");
		Conferir(_apertos > 3, $"o quick time event foi respondido pelo funil do pacote: {_apertos} acertos");
		Conferir(_corridosVistos < 15, $"...e a disputa foi decidida ANTES do prazo ({_corridosVistos:0.#}s)");

		Colar("user://embateki-tira-do-empurrao.png", desde: 0, larguraDoCorte: 460, alturaDoCorte: 280, escala: 2);
		srv.EmbateDeFoto_Limpar();
		Virar(3);
	}

	// =====================================================================
	// CENA 2: O EMPATE
	// =====================================================================
	/// <summary>
	/// AGORA OS DOIS SAO IGUAIS E NINGUEM RESPONDE LETRA NENHUMA: a deriva em forcas parelhas e
	/// exatamente zero, o medidor nao sai de 50, e a disputa so pode acabar pelo PRAZO. E o empate de
	/// manual -- o desfecho que o dono descreveu: *"se NINGUEM VENCER em 15 segundos, acontece uma
	/// EXPLOSAO, e AMBOS os jogadores sofrem um DANO e sao JOGADOS PRA TRAS"*.
	/// </summary>
	private void MontarOEmpate(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1.5) return;   // deixa o feixe vencedor da cena 1 sumir do snapshot

		(_duelistaA, _duelistaB) = srv.EmbateDeFoto_Montar(
			cli.LocalId, tiles: 5, bpDeA: 5_000, bpDeB: 5_000, tecladoEmA: false, tilesAbaixo: 3);

		Conferir(_duelistaA != 0 && _duelistaB != 0, "a cena do empate entrou no mundo");
		if (_duelistaA == 0) { Fechar(); return; }

		_enquadrar = true;
		_corridosVistos = 0;   // o relogio e da CENA, e a cena 1 deixou o dela escrito aqui
		Virar(4);
	}

	private void EsperarOEmpate(Jandirus.Server.GameServer srv, World mundo)
	{
		(bool existe, double medidor, Vec2 ponto, double corridos, _, _, _) = srv.EmbateDeFoto_Estado();

		if (existe)
		{
			_corridosVistos = corridos;
			(double va, double vb, _, _, Vec2 pa, Vec2 pb) = srv.EmbateDeFoto_Corpos();
			_antesDoEstouro = (va, vb, pa, pb);
			_pontoDoEstouro = ponto;
			_medidorNoFim = medidor;
			return;
		}

		if (_corridosVistos < 1)
		{
			if (_t > 25) { Conferir(false, "a cena do empate virou disputa"); Fechar(); }
			return;
		}

		// ============================ O OBTURADOR DISPARA NO QUADRO DO ESTOURO ============================
		// A explosao e um INSTANTE: `Estourar(forte)` racha o chao, manda o baque (faisca, poeira e
		// tremor no cliente) e a onda arremessa os dois. Um quadro depois o baque ja virou poeira caindo.
		// Por isso a foto sai aqui, no primeiro quadro em que o servidor diz que a disputa acabou.
		// ============================================================================================
		(double vidaA, double vidaB, int vooA, int vooB, _, _) = srv.EmbateDeFoto_Corpos();
		Fotografar("user://embateki-4-empate-explosao.png",
				   $"CENA 2: EMPATE aos {_corridosVistos:0.#}s (medidor {_medidorNoFim:0}) -- os dois "
				   + $"estouram em pe de igualdade", NaTela(_pontoDoEstouro));

		Conferir(_corridosVistos >= 14 && _corridosVistos <= 16,
			$"ninguem venceu, e a disputa foi ate o PRAZO: {_corridosVistos:0.#}s");
		Conferir(vidaA < _antesDoEstouro.VidaA && vidaB < _antesDoEstouro.VidaB,
			$"OS DOIS sofreram dano na explosao: {_antesDoEstouro.VidaA:0.##} -> {vidaA:0.##} e "
			+ $"{_antesDoEstouro.VidaB:0.##} -> {vidaB:0.##}");
		Conferir(vooA > 0 && vooB > 0,
			$"...e OS DOIS foram jogados pra tras pela onda de choque ({vooA} e {vooB} tiques de voo)");

		Virar(5);
	}

	private Vec2 _pontoDoEstouro;
	private double _medidorNoFim;

	/// <summary>
	/// MEIO SEGUNDO DEPOIS: os dois corpos JA VOANDO, cada um pro seu lado. E a segunda metade do
	/// pedido, e ela nao cabe na mesma foto que a explosao -- no quadro do estouro eles ainda estao no
	/// lugar (o voo comeca no tique seguinte).
	/// </summary>
	private void DepoisDaOnda(Jandirus.Server.GameServer srv, World mundo)
	{
		if (_t < 0.5) return;

		(_, _, _, _, Vec2 pa, Vec2 pb) = srv.EmbateDeFoto_Corpos();
		Vector2 daA = mundo.PosicaoDesenhadaDe(_duelistaA) ?? Vector2.Zero;
		Vector2 daB = mundo.PosicaoDesenhadaDe(_duelistaB) ?? Vector2.Zero;

		Fotografar("user://embateki-5-arremesso.png",
				   "CENA 2: a onda de choque joga os dois pra tras", NaTela(_pontoDoEstouro));
		Colar("user://embateki-tira-do-empate.png", desde: _tira.Count - 2, larguraDoCorte: 460, alturaDoCorte: 280, escala: 2);

		// ============================ PRA FORA DO ESTOURO -- E ESTA E A MEDIDA QUE O MAPA NAO ESTRAGA ============================
		// A leitura obvia seria "os dois vetores de fuga apontam pra lados opostos", e ela reprovou aqui
		// por causa do CHAO: o duelista da ponta foi empurrado contra o que havia atras dele e nao saiu do
		// lugar (`cos 1; 101 px e 0 px`). Isso e o `MoveRules` funcionando -- quem tem parede atras para
		// nela --, e nao a onda de choque errada; a `--embatekiteste` mede o par de vetores no lugar certo
		// pra isso (com `folgaAtras`, num corredor escolhido pra caber o voo).
		//
		// O que esta bancada pode afirmar sem depender do mapa e o que a onda PROMETE: ninguem e puxado
		// PRA DENTRO dela. Um empurrao com o rumo trocado (os dois pro mesmo lado) poe um dos dois mais
		// perto do ponto do estouro do que ele estava -- e e exatamente isso que a linha abaixo pega.
		// ==================================================================================================================
		var pontoDoMundo = new Vector2(_pontoDoEstouro.X, _pontoDoEstouro.Y);
		float antesA = new Vector2(_antesDoEstouro.PosA.X, _antesDoEstouro.PosA.Y).DistanceTo(pontoDoMundo);
		float antesB = new Vector2(_antesDoEstouro.PosB.X, _antesDoEstouro.PosB.Y).DistanceTo(pontoDoMundo);
		float depoisA = new Vector2(pa.X, pa.Y).DistanceTo(pontoDoMundo);
		float depoisB = new Vector2(pb.X, pb.Y).DistanceTo(pontoDoMundo);

		Conferir(depoisA >= antesA - 1 && depoisB >= antesB - 1
				 && Math.Max(depoisA - antesA, depoisB - antesB) > 8,
			$"os dois foram jogados PRA FORA do estouro, nenhum pra dentro "
			+ $"({antesA:0} -> {depoisA:0} px e {antesB:0} -> {depoisB:0} px do ponto)");

		// ============================ E NENHUM FEIXE SOBROU NA TELA ============================
		// A leitura de "feixe orfao" do lado do CLIENTE: um node que ninguem mais alimenta e nada mais
		// mata. A `--embatekiteste` mede o BIT (`EmEmbate`) no servidor; aqui se mede o NODE.
		//
		// MEIO SEGUNDO DEPOIS, e nao no quadro do estouro. A primeira versao perguntava no mesmo quadro
		// em que o servidor encerrou a disputa e reprovava com "2" -- o anuncio de morte das duas
		// cabecas ainda estava no fio. Medir ali era medir a latencia do proprio pacote, e chamar isso
		// de feixe orfao seria acusar o jogo de um defeito da bancada.
		// ==================================================================================
		int sobrou = ContarFeixes(mundo, out _, out _);
		Conferir(sobrou == 0, $"...e meio segundo depois nao ha feixe nenhum desenhado ({sobrou})");
		Conferir(mundo.CorpoDeTeste(_duelistaA) != null && mundo.CorpoDeTeste(_duelistaB) != null
				 && daA.DistanceTo(daB) > 1,
			"...e os dois continuam com CORPO DESENHADO depois do estouro (ninguem sumiu na explosao)");

		srv.EmbateDeFoto_Limpar();
		Virar(6);
	}

	// =====================================================================
	// CENA 3: DOIS FINAL FLASH -- AS CABECAS DESENHADAS FRENTE COM FRENTE (dono, 2026-09-23)
	// =====================================================================
	/// <summary>
	/// *"elas ainda estao se sobrepondo as vezes"*. O as vezes era a TECNICA: o servidor mantinha as cabecas
	/// a 32 px de centro a centro pra qualquer uma, e o Final Flash e desenhado com a folha de 64 px vezes a
	/// escala 4 -- 128 px de frente. As cenas 1 e 2 usam o Ki Wave (16 px) e nunca veriam isso.
	/// </summary>
	/// <summary>Quando (no relogio `_vida`) a disputa dos Final Flash comecou; -1 = ainda nao.</summary>
	private double _disputaDoFinalFlashEm = -1;

	private void MontarOsFinalFlash(Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 1) return;
		// CINCO TILES ABAIXO DE QUEM ASSISTE, e nao tres: a cabeca do Final Flash encosta num corpo a ate 136 px
		// do centro dela (a frente de 128 px + a meia largura do corpo) -- e com o fotografo a 4 tiles (128 px)
		// da linha, o primeiro corpo que o feixe encontrava era o da camera. E a regra nova funcionando: o
		// desenho daquela cabeca cobre quem esta a 4 tiles de lado.
		(_duelistaA, _duelistaB) = srv.EmbateDeFoto_Montar(
			cli.LocalId, tiles: 14, bpDeA: 5_000, bpDeB: 5_000, tecladoEmA: true, tilesAbaixo: 5,
			verbo: "Final_Flash", escala: 4);
		Conferir(_duelistaA != 0 && _duelistaB != 0, "CENA 3: os dois duelistas do Final Flash entraram no mundo");
		if (_duelistaA == 0) { Nota("sem corredor livre de 14 tiles: a cena 3 nao cabe neste pedaco de mapa"); Fechar(); return; }
		_enquadrar = true;
		Virar(7);
	}

	/// <summary>
	/// ============================ A MEDIDA E DO DESENHO, E O DESENHO MUDOU ============================
	/// Esta cena lia a FOLHA: a frente de cada cabeca saia do alpha de `head_east` vezes a escala, e a
	/// soma das duas era comparada com a distancia entre os centros. Nao ha mais folha -- o raio e
	/// desenhado por shader (`ProjetilDesenhado`), com a ponta posta em `posicao + rumo * frente`.
	///
	/// O que continua sendo medido e a MESMA pergunta do dono (*"as cabecas ainda estao se sobrepondo as
	/// vezes"*, 2026-09-23), agora no que o cliente de fato desenha:
	///
	///   * as duas CABECAS se encontram frente com frente NA FOTO (nem chao entre elas, nem uma dentro da
	///     outra), e a tinta de cada uma acaba na ponta que o node anuncia -- ver <see cref="ObturadorDasPontas"/>;
	///   * os dois feixes estao PRENSADOS -- o bit chegou pelo fio (e por ele que o cliente poe a estrela);
	///   * ha UMA estrela de embate no encontro, e ela esta ACESA NA FOTO (pixel, e nao "o node existe") --
	///     ver <see cref="MedirAEstrela"/>.
	///
	/// A CONFERENCIA DA TABELA DO CORE CONTRA O DESENHO, arte por arte, saiu daqui: ela mede o desenho
	/// de um raio SOLTO num fundo liso, e isso e assunto da `--diagartedeki` (familia 6), que tem o
	/// fundo liso. Aqui ha mapa atras, e a estrela do choque fica por cima das duas pontas.
	/// ==================================================================================================
	/// </summary>
	private void MedirOsFinalFlash(Jandirus.Server.GameServer srv, World mundo)
	{
		(bool existe, _, Vec2 ponto, _, _, _, _) = srv.EmbateDeFoto_Estado();
		if (!existe)
		{
			if (_t > 20) { Conferir(false, "CENA 3: os dois Final Flash se encontraram e viraram disputa"); Fechar(); }
			return;
		}
		// MEIO SEGUNDO DEPOIS DE A DISPUTA COMECAR, e nao da cena: o `_t` ja correu enquanto as cabecas vinham.
		// A primeira versao media no MESMO quadro do gatilho -- o desenho ainda um pacote atras, com as duas
		// cabecas a caminho (19 px por tique cada) -- e acusou 38 px de vao que o servidor nao tinha (a
		// `--embatekiteste` mede 0,0 px de folga na disputa).
		if (_disputaDoFinalFlashEm < 0) _disputaDoFinalFlashEm = _vida;
		if (_vida - _disputaDoFinalFlashEm < 0.6) return;

		// AS QUATRO CHAPAS DO MESMO INSTANTE. A arvore fica PAUSADA daqui ate o fim da medida (ver `Obturador`):
		// tudo o que se le abaixo -- pontas, estrela, camera -- e o que as chapas mostram, e nao um quadro depois.
		if (!Obturador(mundo)) return;
		// ...E AS CHAPAS DAS PONTAS, do mesmo instante: cada cabeca sozinha na tela (ver `ObturadorDasPontas`).
		if (!ObturadorDasPontas(mundo, comDefeito: true)) return;

		var raios = new List<(Vector2 Ponta, float Frente, bool Prensado)>();
		foreach ((int _, Vector2 ponta, float frente, bool prensado) in mundo.RaiosDesenhadosDeTeste())
			raios.Add((ponta, frente, prensado));

		// A TIRA DA ESTRELA comeca aqui, na ordem das outras bancadas: a esquerda o defeito injetado, depois a
		// producao -- e a mascara, que o `MedirAEstrela` guarda por ultimo.
		int tiraDaEstrela = _tira.Count;
		if (_chapaSemPintura != null)
			Guardar(_chapaSemPintura, "user://embateki-6-estrela-sem-pintura.png",
					"CENA 3, COM O DEFEITO INJETADO (`ChoqueDeKi.SemPinturaDeTeste`): a estrela esta na arvore e nao pinta",
					NaTela(ponto));
		if (_chapaCom != null)
			Guardar(_chapaCom, "user://embateki-6-final-flash.png",
					$"CENA 3: dois Final Flash se encontram ({raios.Count} raios desenhados)", NaTela(ponto));
		else Nota("CENA 3: sem foto (headless nao renderiza)");

		Conferir(raios.Count == 2, $"CENA 3: os dois raios estao desenhados ({raios.Count})");
		if (raios.Count == 2)
		{
			float frentes = raios[0].Frente + raios[1].Frente;
			Conferir(frentes > 100f,
				$"CENA 3: a cabeca do Final Flash e GRANDE mesmo -- a soma das duas frentes desenhadas e {frentes:0} px");

			// O ENCONTRO E LIDO NA FOTO. O campo fica como testemunha: ele dizia "0,0 px entre elas" com uma
			// cabeca desenhada dentro da outra (ver `ObturadorDasPontas`).
			float vao = raios[0].Ponta.DistanceTo(raios[1].Ponta);
			AfirmarAsPontas("CENA 3", $"as duas `PontaDesenhada` estao a {vao:0.0} px uma da outra");

			Conferir(raios[0].Prensado && raios[1].Prensado,
				"CENA 3: os dois feixes estao PRENSADOS -- o bit chegou pelo fio, e e por ele que o cliente "
				+ "sabe onde por a estrela do choque");
		}

		// A ESTRELA: primeiro o que a ARVORE diz (ha uma, e ela esta no encontro), depois o que a FOTO diz.
		var bolas = new List<(Vector2 Onde, float Raio)>(mundo.ChoquesDeKiDesenhados());
		Conferir(bolas.Count == 1, $"CENA 3: ha UMA estrela de embate no encontro -- um node na arvore ({bolas.Count})");
		if (bolas.Count == 1)
		{
			float longe = raios.Count == 2 ? bolas[0].Onde.DistanceTo((raios[0].Ponta + raios[1].Ponta) * 0.5f) : 999f;
			Conferir(longe <= 3f, $"CENA 3: a estrela esta EM CIMA do encontro das pontas ({longe:0.0} px dele)");

			if (_chapaCom is { } com && _chapaSem is { } sem && _chapaSemPintura is { } semPintura && _chapaSem2 is { } sem2)
			{
				MedirAEstrela(mundo, bolas[0].Onde, bolas[0].Raio, NaTela(ponto), com, sem, semPintura, sem2);
				Nota("a tira da estrela, da esquerda pra direita: o defeito injetado (o node na arvore, sem pintar), "
				   + "a producao, e a mascara do que a estrela pintou");
				// A ALTURA INTEIRA da foto, e nao o corte deitado das outras tiras: o que a estrela mostra entre
				// duas cabecas deste tamanho sao as pontas que saem por cima e por baixo.
				Colar("user://embateki-tira-da-estrela.png", desde: tiraDaEstrela, larguraDoCorte: 420, alturaDoCorte: 720, escala: 1);
			}
			else Nota("CENA 3: a estrela NAO foi medida no pixel -- faltou chapa (headless nao renderiza)");
		}

		// DEPOIS da tira da estrela, pra as duas tiras nao se misturarem: cada uma e um trecho seguido da lista.
		GuardarAsPontas("CENA 3", "user://embateki-6-pontas", naTira: true);

		SoltarOObturador();
		srv.EmbateDeFoto_Limpar();
		Fechar();
	}

	// =====================================================================
	// A ESTRELA DO EMBATE, NO PIXEL
	// =====================================================================
	/// <summary>
	/// O PISO DA TINTA DA ESTRELA, em pixel de foto (1280x720, 3 px de foto por px de mundo).
	///
	/// Medido de ceu limpo em cinco rodadas (2026-10-08, com as cabecas ja frente com frente): escondido o
	/// node, mudam de 45 a 51 mil px -- o encontro das duas cabecas e o clarao em volta dele, que a lente
	/// branca tapa, e as pontas que saem por cima e por baixo (a estrela e sorteada, dai a faixa). Com o
	/// defeito injetado (o node na arvore, sem pintar) mudam de 38 a 174: as faiscas, que sao filhas dele. O
	/// piso fica 11 vezes acima do pior defeito e 22 vezes abaixo da menor producao.
	///
	/// (Com uma cabeca desenhada dentro da outra -- ver <see cref="ObturadorDasPontas"/> -- eram de 16 a 29
	/// mil: as duas ja pintavam de branco quase tudo o que a lente cobre.)
	/// </summary>
	private const int PisoDaTinta = 2000;

	/// <summary>
	/// QUAO BRANCO O MIOLO TEM QUE SAIR NA FOTO. De ceu aberto ele le 1,00 cravado -- (255,255,255) --, e o
	/// veu de qualquer clima, na espessura media dele, tira dali de 8% (neve) a 17% (tempestade).
	/// </summary>
	private const float BrancoDoMiolo = 0.95f;

	/// <summary>
	/// O DISCO DO MIOLO, em fracao do corpo da estrela: o branco e lido na MEDIANA da luminancia dentro dele.
	///
	/// Um quinto do corpo cabe com folga no branco -- o `R_miolo` do `ChoqueDeKi.gdshader` nunca e menor que
	/// 0,66 do corpo vezes o pulso minimo (0,95), e nesta cena a lente e redonda (o teto de 56 px vale pros
	/// dois eixos). E e maior que a maior pedra do embate (13 px): as que sobem do lado de ca passam NA FRENTE
	/// do clarao, por desenho (`ChoqueDeKi.LevantarUmaPedra`), e lendo um pixel so uma delas viraria a leitura.
	/// </summary>
	private const float DiscoDoMiolo = 0.2f;

	/// <summary>
	/// A ESTRELA ESTA ACESA NA FOTO? -- "ha um node `ChoqueDeKi`" e uma afirmacao sobre a ARVORE, e o dono
	/// pediu um efeito NA TELA.
	///
	/// ============================ A REGUA VELHA NAO MEDIA A ESTRELA (2026-10-08) ============================
	/// Ela lia UM pixel -- a luminancia no centro da estrela -- e pedia mais que 0,85, com o argumento de que
	/// o miolo e branco e cobre. Reprovou com 0,83 quatro rodadas seguidas, e o que ela media eram duas
	/// coisas que nao sao a estrela:
	///
	///   * O CEU. O 0,83 e branco puro por baixo do veu da tempestade (a hora era cravada e o clima nao: ver
	///     `EmbateDeFoto_CeuLimpo`, no servidor). De ceu aberto o mesmo pixel le 1,00.
	///   * AS CABECAS DOS FEIXES. Nesta cena o centro e branco COM a estrela, com o node dela ESCONDIDO e com
	///     ela na arvore SEM PINTAR: (255,255,255) nas tres chapas de ceu aberto, (210,211,211) nas tres de
	///     tempestade. As duas cabecas do Final Flash ja sao brancas ali -- a regua nao tinha como ficar
	///     vermelha por falta de estrela, so por excesso de nuvem.
	///
	/// ============================ O QUE SE MEDE AGORA: A TINTA ============================
	/// A receita da `--diagboca`: com a arvore PAUSADA, a mesma tela e fotografada com a estrela e com o node
	/// dela escondido. Nada mais mudou entre as chapas -- nem os feixes, nem o chao, nem a camera --, entao o
	/// que difere e, por construcao, O QUE A ESTRELA PINTOU. Aqui isso e o encontro das duas cabecas (elas se
	/// tocam num ponto so, e a lente branca tapa a cunha de clarao que sobra em cima e embaixo dele) e as
	/// pontas que saem por cima e por baixo.
	///
	/// O CONTRA-EXEMPLO e o `ChoqueDeKi.SemPinturaDeTeste`: o node fica na arvore -- a contagem continua 1 --
	/// e nao pinta. A tinta cai pra umas dezenas de px (as faiscas, que sao filhas do node) e a regua
	/// reprova, com o centro ainda em 1,00.
	///
	/// O BRANCO DO MIOLO continua sendo lido, pelo que ele de fato responde: que NADA escurece o ki entre o
	/// desenho e a tela -- nem o ambiente (o material e `unshaded`), nem o ceu. E a leitura que volta a
	/// reprovar com `--climateste Tempestade`, o contra-exemplo do palco.
	/// ======================================================================================================
	/// </summary>
	/// <param name="onde">O centro da estrela, em px de mundo.</param>
	/// <param name="corpo">O semi-eixo de traves do corpo dela, em px de mundo.</param>
	/// <param name="centroDaTira">Em volta de que ponto da tela a tira da estrela e recortada.</param>
	private void MedirAEstrela(World mundo, Vector2 onde, float corpo, Vector2 centroDaTira,
							   Image com, Image sem, Image semPintura, Image sem2)
	{
		// ---- o palco: de ceu aberto, e sem luz pendurada ----
		// Nao e so pelo branco. Com a cena escura a `LuzDeKi` da estrela acende o chao em volta, a luz some
		// junto com o node e a mascara passa a contar LUZ como tinta: debaixo de tempestade o defeito injetado
		// "pinta" quase 20 mil px. E o mesmo aviso da `--diagboca`.
		string ceu = mundo.TempoQueFaz is { Ativo: true } tq
			? $"{Jandirus.Core.World.Clima.Nome(tq.Tipo)} a {tq.Forca:P0}" : "ceu limpo";
		Conferir(mundo.TempoQueFaz is not { Ativo: true },
			$"CENA 3: o ceu DESENHADO esta limpo na hora da foto ({ceu}) -- a bancada pede ceu aberto ao "
			+ "servidor, e so um `--climateste` na linha de comando passa por cima dele");
		if (_obtNo != null && IsInstanceValid(_obtNo) && _obtNo.GetNodeOrNull<Node2D>(LuzDeKi.NomeDoNode) != null)
			Nota("ATENCAO: a estrela tem LUZ pendurada (a cena esta escura) -- ela some junto com o node, e a "
			   + "mascara mede tinta E luz");

		int largura = com.GetWidth(), altura = com.GetHeight();
		byte[] bCom = Rgba(com), bSem = Rgba(sem), bSemPintura = Rgba(semPintura), bSem2 = Rgba(sem2);
		if (bSem.Length != bCom.Length || bSemPintura.Length != bCom.Length || bSem2.Length != bCom.Length)
		{
			Conferir(false, "CENA 3: as quatro chapas da estrela sairam do mesmo tamanho (a janela mudou no meio da medida)");
			return;
		}

		// ---- a tinta: o que muda quando o node e escondido ----
		byte[] marcada = (byte[])bCom.Clone();
		int tinta = Tinta(bCom, bSem, bSem2, marcada);
		int tintaSemPintura = Tinta(bSemPintura, bSem, bSem2, null);
		Guardar(Image.CreateFromData(largura, altura, false, Image.Format.Rgba8, marcada),
				"user://embateki-6-estrela-mascara.png",
				$"CENA 3: A MASCARA da estrela (magenta = os {tinta} px que ela pintou)", centroDaTira);

		Conferir(tinta >= PisoDaTinta,
			$"CENA 3: a ESTRELA esta acesa NA FOTO, e nao so o node na arvore -- escondido o node dela, {tinta} px "
			+ $"da tela mudam (o piso e {PisoDaTinta}): o encontro das duas cabecas, que ela tapa, e as pontas");
		Conferir(_nodesSemPintura == 1 && tintaSemPintura < PisoDaTinta,
			$"[injecao] CENA 3: com `ChoqueDeKi.SemPinturaDeTeste` a arvore continua com {_nodesSemPintura} estrela e a "
			+ $"regua da tinta REPROVA -- {tintaSemPintura} px mudam, contra o piso de {PisoDaTinta}");

		// ---- o branco do miolo: nada entre o desenho e a tela ----
		Vector2 centro = NaFoto(onde);
		float escala = NaFoto(onde + Vector2.Right).DistanceTo(centro);
		float disco = corpo * DiscoDoMiolo * escala;
		float branco = MedianaDeLuz(bCom, largura, altura, centro, disco);
		float brancoSemEla = MedianaDeLuz(bSem, largura, altura, centro, disco);
		Conferir(branco >= BrancoDoMiolo,
			$"CENA 3: o MIOLO da estrela sai BRANCO na foto -- luminancia {branco:0.00} (mediana de um disco de "
			+ $"{corpo * DiscoDoMiolo:0} px no centro dela, corpo de {corpo:0} px): nada escurece o ki entre o "
			+ "desenho e a tela, nem o ambiente nem o ceu");
		if (MathF.Abs(branco - brancoSemEla) < 0.02f)
			Nota($"...e esse branco, sozinho, NAO prova a estrela: com o node dela escondido o mesmo disco le "
			   + $"{brancoSemEla:0.00} -- nesta cena as duas cabecas do Final Flash ja cobrem o centro. Quem prova a "
			   + "estrela e a tinta.");
	}

	/// <summary>
	/// DIFERENCA DE CANAL a partir da qual dois pixels contam como DIFERENTES, em 255 avos -- o 0,12 das
	/// bancadas irmas (`--diagboca`, `--diagvariedade`): abaixo disso e ruido do viewport.
	/// </summary>
	private const int Epsilon = 30;

	private static bool Difere(byte[] p, byte[] q, int i)
		=> Math.Abs(p[i] - q[i]) > Epsilon || Math.Abs(p[i + 1] - q[i + 1]) > Epsilon || Math.Abs(p[i + 2] - q[i + 2]) > Epsilon;

	/// <summary>
	/// QUANTOS PIXELS ESTA CHAPA TEM QUE AS DUAS CHAPAS SEM ESTRELA NAO TEM -- a regra das tres fotos da
	/// `--diagboca`, igual: so conta o pixel que difere das DUAS chapas sem o node, e so onde essas duas
	/// concordam entre si (onde o fundo se mexeu sozinho nao da pra dizer de quem e a tinta). `marcar`, se
	/// vier, sai com esses pixels em magenta: uma sonda que conta pixels tem que mostrar QUAIS ela contou.
	/// </summary>
	private static int Tinta(byte[] chapa, byte[] sem, byte[] sem2, byte[]? marcar)
	{
		int n = 0;
		for (int i = 0; i + 3 < chapa.Length; i += 4)
		{
			if (!PintouAqui(chapa, sem, sem2, i)) continue;
			n++;
			if (marcar != null) { marcar[i] = 255; marcar[i + 1] = 0; marcar[i + 2] = 255; }
		}
		return n;
	}

	/// <summary>A regra das tres fotos, num pixel: ver <see cref="Tinta"/>.</summary>
	private static bool PintouAqui(byte[] chapa, byte[] sem, byte[] sem2, int i)
		=> !Difere(sem, sem2, i) && Difere(chapa, sem, i) && Difere(chapa, sem2, i);

	/// <summary>A luminancia MEDIANA de um disco da chapa (centro e raio em pixel de foto), ou -1 se ele caiu fora dela.</summary>
	private static float MedianaDeLuz(byte[] chapa, int largura, int altura, Vector2 centro, float raio)
	{
		var luzes = new List<float>();
		int r = Mathf.CeilToInt(raio);
		for (int dy = -r; dy <= r; dy++)
			for (int dx = -r; dx <= r; dx++)
			{
				if (dx * dx + dy * dy > raio * raio) continue;
				int x = (int)centro.X + dx, y = (int)centro.Y + dy;
				if (x < 0 || y < 0 || x >= largura || y >= altura) continue;
				int i = 4 * (y * largura + x);
				luzes.Add((0.2126f * chapa[i] + 0.7152f * chapa[i + 1] + 0.0722f * chapa[i + 2]) / 255f);
			}
		if (luzes.Count == 0) return -1f;
		luzes.Sort();
		return luzes[luzes.Count / 2];
	}

	/// <summary>
	/// A chapa em bytes RGBA, quatro por pixel. Pelo vetor, e nao pelo `GetPixel`: sao quatro chapas de 900 mil
	/// pixels, e uma chamada por pixel e um engasgo de segundos com a arvore parada.
	/// </summary>
	private static byte[] Rgba(Image img)
	{
		var copia = (Image)img.Duplicate();
		copia.Convert(Image.Format.Rgba8);
		return copia.GetData();
	}

	// =====================================================================
	// O OBTURADOR -- quatro chapas do mesmo instante
	// =====================================================================
	private int _obtFase, _obtQuadros;
	private Image? _chapaCom, _chapaSem, _chapaSemPintura, _chapaSem2;
	private ChoqueDeKi? _obtNo;

	/// <summary>Quantas estrelas a ARVORE tinha na chapa do defeito injetado -- contar nodes nao ve aquele defeito.</summary>
	private int _nodesSemPintura;

	/// <summary>
	/// TIRA AS QUATRO CHAPAS, e devolve verdadeiro quando acabou (ate la, quem chama volta no quadro seguinte):
	///
	///   1. COM          -- a producao, como esta na tela;
	///   2. SEM          -- o node da estrela escondido (`Visible = false`);
	///   3. SEM PINTURA  -- o node de volta, com o defeito injetado (`ChoqueDeKi.SemPinturaDeTeste`);
	///   4. SEM de novo  -- a peneira do fundo que se mexe sozinho, como na `--diagboca`.
	///
	/// ============================ A ARVORE FICA PAUSADA ============================
	/// O encontro anda (um lado empurra o outro), a estrela troca de desenho doze vezes por segundo e as
	/// pedras sobem: sem pausar, a diferenca entre duas chapas seria o MOVIMENTO, e nao a estrela. Pausada, o
	/// que muda de uma pra outra e so o que este metodo mexe. Ela so e solta no `SoltarOObturador`, depois de
	/// quem chamou ler o que precisava -- e e por isso que este robo roda em `ProcessModeEnum.Always`.
	///
	/// DOIS QUADROS DE FOLGA A CADA TROCA: `GetImage` devolve o ULTIMO quadro renderizado, e um pedido feito
	/// no mesmo quadro da mudanca fotografa o estado de ANTES dela.
	/// ===========================================================================
	/// </summary>
	private bool Obturador(World mundo)
	{
		switch (_obtFase)
		{
			case 0:
				GetTree().Paused = true;
				_obtNo = NoDaEstrela(mundo);
				break;

			case 1:
				if (_obtQuadros++ < 2) return false;
				_chapaCom = Tela();
				MostrarAEstrela(false);
				break;

			case 2:
				if (_obtQuadros++ < 2) return false;
				_chapaSem = Tela();
				ChoqueDeKi.SemPinturaDeTeste = true;
				MostrarAEstrela(true);
				break;

			case 3:
				if (_obtQuadros++ < 2) return false;
				_chapaSemPintura = Tela();
				_nodesSemPintura = mundo.ChoquesDeKiDesenhados().Count();
				MostrarAEstrela(false);
				ChoqueDeKi.SemPinturaDeTeste = false;
				break;

			case 4:
				if (_obtQuadros++ < 2) return false;
				_chapaSem2 = Tela();
				MostrarAEstrela(true);
				break;

			default:
				return true;
		}

		_obtFase++;
		_obtQuadros = 0;
		return _obtFase > 4;
	}

	/// <summary>
	/// Esconde ou devolve o node da estrela. Ao devolver pede o redesenho: e nele que o `_Draw` le o
	/// `SemPinturaDeTeste` de novo. (Um node que volta a ser visivel ja se redesenha sozinho; o pedido e
	/// pra a troca do defeito nao depender disso.)
	/// </summary>
	private void MostrarAEstrela(bool visivel)
	{
		if (_obtNo == null || !IsInstanceValid(_obtNo)) return;
		_obtNo.Visible = visivel;
		if (visivel) _obtNo.QueueRedraw();
	}

	/// <summary>
	/// Desfaz tudo o que o obturador mexe: o defeito, o node escondido e a pausa. O `Fechar` tambem passa
	/// por aqui, pra uma saida no meio da medida nao deixar a arvore parada.
	/// </summary>
	private void SoltarOObturador()
	{
		ChoqueDeKi.SemPinturaDeTeste = false;
		MostrarAEstrela(true);
		_obtNo = null;
		if (GetTree() is { Paused: true } arvore) arvore.Paused = false;
	}

	/// <summary>
	/// O NODE DA ESTRELA, achado na ARVORE pelo nome que o `World.TickDosChoquesDeKi` da a ele (`Choque` + o
	/// id do feixe) -- pela razao escrita no `NoDoTiro` da `--diagboca`: um acessador novo no `World` so pra
	/// isto seria uma porta de bancada dentro do jogo. As que ja estao apagando, de uma cena anterior, nao servem.
	/// </summary>
	private static ChoqueDeKi? NoDaEstrela(World mundo)
	{
		foreach (Node n in mundo.FindChildren("Choque*", "", true, false))
			if (n is ChoqueDeKi { SaindoDeTeste: false } estrela) return estrela;
		return null;
	}

	// =====================================================================
	// AS PONTAS, NO PIXEL -- cada cabeca sozinha na chapa (2026-10-08)
	// =====================================================================
	private int _ptFase, _ptQuadros;
	private bool _ptPausou;
	private ProjetilDesenhado? _ptA, _ptB;
	private Variant _ptHaloDeA, _ptHaloDeB;
	private readonly List<Node2D> _ptLuzes = [];
	private ChoqueDeKi? _ptEstrela;
	private Image? _ptNada, _ptSoA, _ptSoB, _ptSoAComDefeito, _ptSoBComDefeito, _ptNada2;

	/// <summary>O que o <see cref="AfirmarAsPontas"/> deixa pro <see cref="GuardarAsPontas"/> gravar.</summary>
	private Image? _ptRetrato, _ptRetratoComDefeito;
	private Vector2 _ptEncontroNaFoto;
	private float _ptMeioRecorte;

	/// <summary>
	/// TIRA AS CHAPAS DAS PONTAS, e devolve verdadeiro quando acabou (ate la, quem chama volta no quadro
	/// seguinte): a tela sem raio nenhum, so com um, so com o outro, e sem nenhum de novo.
	///
	/// ============================ A PONTA QUE SE LIA ERA UM CAMPO ============================
	/// "As pontas desenhadas se encontram no mesmo ponto" saia do `ProjetilDesenhado.PontaDesenhada` --
	/// `Position + rumo * Frente`, a ponta que o node ANUNCIA. Dois raios numa disputa anunciam a mesma ponta
	/// por construcao (o servidor os planta frente com frente), entao a linha ficava verde com QUALQUER
	/// desenho.
	///
	/// E FICOU: em 2026-10-08 ela dizia "0,0 px entre elas" com cada cabeca do Final Flash a 4x pintada de 7 a
	/// 20 px alem da propria ponta -- uma de 22 a 38 px DENTRO da outra, debaixo da estrela. Era a bola da
	/// MAO, que nao cabia no feixe de 208 px desta cena e alcancava o plano da ponta (o conserto e a conta
	/// estao no `FeixeDeKi.gdshader`). O Ki Wave da cena 1, com 64 px de feixe e 7 de bola, nunca passou de
	/// 0,2 px.
	///
	/// ============================ O QUE SE LE AGORA: A TINTA DE CADA CABECA ============================
	/// A receita das tres fotos da estrela (ver <see cref="Tinta"/>), um raio de cada vez: com a arvore
	/// PAUSADA, o que a chapa de um raio tem que as duas chapas vazias nao tem e, por construcao, O QUE
	/// AQUELE RAIO PINTOU. Dali saem as duas leituras do <see cref="LerAsPontas"/>.
	///
	/// ============================ SEM A ESTRELA E SEM O HALO ============================
	/// A estrela fica escondida -- ela tapa justamente o encontro. E o halo dos dois raios e apagado no
	/// material enquanto as chapas saem, a mesma escrita da `--diagartedeki` (familia 7): ele e luz somada,
	/// passa da ponta por desenho (`PintorDeKi.FolgaNaPonta`) e nao encosta em ninguem. O que sobra e a tinta
	/// OPACA -- o corpo, o leque e as fitas --, e e essa que nao pode passar da ponta.
	///
	/// E SEM AS LUZES DOS DOIS RAIOS, que so existem com a cena escura (`LuzDeKi`): elas sao filhas do node e
	/// somem com ele, e a chapa "so com um raio" contaria como tinta dele o chao que a luz dele clareia. Com
	/// `--climateste Tempestade` a primeira versao leu a cabeca do Ki Wave 46 px alem da ponta -- era o clarao
	/// no capim. Apagadas nas quatro chapas, o que muda de uma pra outra volta a ser so o desenho.
	///
	/// `comDefeito` tira mais duas chapas com o <see cref="ProjetilDesenhado.DefeitoDoFeixe.PontaAdiante"/>
	/// ligado: e o contra-exemplo, e a arvore ja esta parada pra ele sair do mesmo instante.
	///
	/// SE A ARVORE JA ESTAVA PAUSADA (a cena 3, que vem do <see cref="Obturador"/>), ela continua: quem
	/// pausou e quem solta.
	/// =====================================================================================================
	/// </summary>
	private bool ObturadorDasPontas(World mundo, bool comDefeito)
	{
		switch (_ptFase)
		{
			case 0:
				(_ptA, _ptB) = OsDoisRaios(mundo);
				if (_ptA == null || _ptB == null) { _ptFase = 7; return true; }
				if (!GetTree().Paused) { GetTree().Paused = true; _ptPausou = true; }
				_ptEstrela = NoDaEstrela(mundo);
				if (_ptEstrela != null) _ptEstrela.Visible = false;
				_ptHaloDeA = ApagarOHalo(_ptA);
				_ptHaloDeB = ApagarOHalo(_ptB);
				ApagarAsLuzes(_ptA);
				ApagarAsLuzes(_ptB);
				MostrarORaio(_ptA, false);
				MostrarORaio(_ptB, false);
				break;

			case 1:
				if (_ptQuadros++ < 2) return false;
				_ptNada = Tela();
				MostrarORaio(_ptA, true);
				break;

			case 2:
				if (_ptQuadros++ < 2) return false;
				_ptSoA = Tela();
				MostrarORaio(_ptA, false);
				MostrarORaio(_ptB, true);
				break;

			case 3:
				if (_ptQuadros++ < 2) return false;
				_ptSoB = Tela();
				MostrarORaio(_ptB, false);
				if (!comDefeito) { _ptFase = 5; break; }   // direto pra segunda chapa vazia
				ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoFeixe.PontaAdiante;
				MostrarORaio(_ptA, true);
				break;

			case 4:
				if (_ptQuadros++ < 2) return false;
				_ptSoAComDefeito = Tela();
				MostrarORaio(_ptA, false);
				MostrarORaio(_ptB, true);
				break;

			case 5:
				if (_ptQuadros++ < 2) return false;
				_ptSoBComDefeito = Tela();
				MostrarORaio(_ptB, false);
				ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoFeixe.Nenhum;
				break;

			case 6:
				if (_ptQuadros++ < 2) return false;
				_ptNada2 = Tela();
				SoltarAsPontas();
				break;

			default:
				return true;
		}

		_ptFase++;
		_ptQuadros = 0;
		return _ptFase > 6;
	}

	/// <summary>
	/// OS DOIS RAIOS DA CENA, achados na ARVORE pelo nome que o `World.AoNascerTiro` da a eles (`Tiro` + o
	/// id) -- pela mesma razao do <see cref="NoDaEstrela"/>. O primeiro e o que aponta pro leste: a cabeca
	/// da ESQUERDA na foto. Sem exatamente dois, nenhum: a leitura e de um par.
	/// </summary>
	private static (ProjetilDesenhado? A, ProjetilDesenhado? B) OsDoisRaios(World mundo)
	{
		var raios = new List<ProjetilDesenhado>();
		foreach (Node n in mundo.FindChildren("Tiro*", "", true, false))
			if (n is ProjetilDesenhado { Tipo: Jandirus.Core.Combat.TipoDeProjetil.Beam, Visible: true } raio)
				raios.Add(raio);
		if (raios.Count != 2) return (null, null);
		return raios[0].Rumo.X >= raios[1].Rumo.X ? (raios[0], raios[1]) : (raios[1], raios[0]);
	}

	/// <summary>
	/// Esconde ou devolve um raio. Ao devolver pede o redesenho: e no `_Draw` que ele le o
	/// `ProjetilDesenhado.DefeitoDeTeste` de novo, e com a arvore pausada ninguem mais pede.
	/// </summary>
	private static void MostrarORaio(ProjetilDesenhado? no, bool visivel)
	{
		if (no == null || !IsInstanceValid(no)) return;
		no.Visible = visivel;
		if (visivel) no.QueueRedraw();
	}

	/// <summary>Apaga o halo deste raio no material dele e devolve o que estava escrito, pra o <see cref="AcenderOHalo"/>.</summary>
	private static Variant ApagarOHalo(ProjetilDesenhado no)
	{
		if (no.Material is not ShaderMaterial m) return default;
		Variant antes = m.GetShaderParameter("alcance_do_halo");
		// (0,001 e nao zero: o shader divide a distancia por ele)
		m.SetShaderParameter("alcance_do_halo", 0.001f);
		return antes;
	}

	private static void AcenderOHalo(ProjetilDesenhado? no, Variant antes)
	{
		if (no != null && IsInstanceValid(no) && no.Material is ShaderMaterial m && antes.VariantType != Variant.Type.Nil)
			m.SetShaderParameter("alcance_do_halo", antes);
	}

	/// <summary>
	/// Esconde as luzes acesas deste raio (a da cabeca e a do tronco) e as anota pro <see cref="SoltarAsPontas"/>
	/// devolver. So as que estavam visiveis: a do tronco nasce escondida, e quem a mostra e o proprio raio.
	/// </summary>
	private void ApagarAsLuzes(ProjetilDesenhado no)
	{
		foreach (string nome in new[] { LuzDeKi.NomeDoNode, LuzDeKi.NomeDoTronco })
			if (no.GetNodeOrNull<Node2D>(nome) is { Visible: true } luz)
			{
				luz.Visible = false;
				_ptLuzes.Add(luz);
			}
	}

	/// <summary>
	/// Desfaz tudo o que o obturador das pontas mexe: o defeito, o halo, as luzes, os raios escondidos, a estrela
	/// e -- se foi ele que pausou -- a pausa. As chapas e os dois nodes ficam, pra quem chamou ler. O `Fechar`
	/// tambem passa por aqui.
	/// </summary>
	private void SoltarAsPontas()
	{
		ProjetilDesenhado.DefeitoDeTeste = ProjetilDesenhado.DefeitoDoFeixe.Nenhum;
		AcenderOHalo(_ptA, _ptHaloDeA);
		AcenderOHalo(_ptB, _ptHaloDeB);
		_ptHaloDeA = _ptHaloDeB = default;
		foreach (Node2D luz in _ptLuzes) if (IsInstanceValid(luz)) luz.Visible = true;
		_ptLuzes.Clear();
		MostrarORaio(_ptA, true);
		MostrarORaio(_ptB, true);
		if (_ptEstrela != null && IsInstanceValid(_ptEstrela)) { _ptEstrela.Visible = true; _ptEstrela.QueueRedraw(); }
		_ptEstrela = null;
		if (_ptPausou && GetTree() is { Paused: true } arvore) arvore.Paused = false;
		_ptPausou = false;
	}

	private void ZerarAsPontas()
	{
		_ptFase = _ptQuadros = 0;
		_ptA = _ptB = null;
		_ptNada = _ptSoA = _ptSoB = _ptSoAComDefeito = _ptSoBComDefeito = _ptNada2 = null;
		_ptRetrato = _ptRetratoComDefeito = null;
	}

	/// <summary>O que as chapas disseram das duas pontas, em px de mundo. Ver <see cref="LerAsPontas"/>.</summary>
	private readonly record struct Pontas(float Folga, float PassaA, float PassaB, int Sobrepostos);

	/// <summary>
	/// QUANTOS PIXELS FAZEM UMA FRENTE. A frente de uma mancha de tinta e a maior projecao no eixo que tem
	/// pelo menos este tanto de pixels nela ou adiante dela: a ponta de um desenho e uma fileira, e um pixel
	/// sozinho que a peneira das tres fotos deixasse passar viraria a medida inteira. Numa cabeca redonda de
	/// 7 px de raio (a menor daqui), quatro pixels da fileira da frente ficam a menos de um decimo de pixel
	/// de foto do mais adiantado.
	/// </summary>
	private const int PixelsDeFrente = 4;

	/// <summary>
	/// Quanto a tinta de um raio pode acabar antes ou depois da ponta que ele anuncia: um pixel de MUNDO, que
	/// nesta foto sao tres. O que a regua tem que engolir e a grade da foto e o arredondamento dos vertices
	/// (`snap_2d_vertices_to_pixel`), ate um pixel de FOTO cada. Medido: de -0,2 a +0,2 px, em sete rodadas do Ki
	/// Wave da cena 1 e cinco do Final Flash da cena 3 -- e de +7 a +20 no Final Flash de antes do conserto.
	/// </summary>
	private const float ToleranciaDaPonta = 1f;

	private static float[] NovaFrente()
	{
		var maiores = new float[PixelsDeFrente];
		Array.Fill(maiores, float.NegativeInfinity);
		return maiores;
	}

	/// <summary>Mais um pixel de tinta, nesta projecao. `maiores` guarda os <see cref="PixelsDeFrente"/> maiores, do maior pro menor.</summary>
	private static void Ver(float[] maiores, float s)
	{
		if (s <= maiores[^1]) return;
		int k = maiores.Length - 1;
		while (k > 0 && maiores[k - 1] < s) { maiores[k] = maiores[k - 1]; k--; }
		maiores[k] = s;
	}

	/// <summary>
	/// AS DUAS LEITURAS, das quatro chapas:
	///
	///   * a FOLGA NA FOTO -- da frente da tinta de uma cabeca a frente da tinta da outra, pelo eixo da
	///     primeira. Positiva e chao entre as duas; negativa e uma desenhada dentro da outra. E a pergunta
	///     do dono (*"as cabecas ainda estao se sobrepondo as vezes"*), sem campo nenhum no meio alem do
	///     rumo;
	///   * quanto cada cabeca PASSA DA PONTA QUE ANUNCIA (`PontaDesenhada`). A ponta e regra do servidor --
	///     ele encosta as coisas nela -- e e onde o `World` poe a estrela: se a tinta nao acaba ali, quem
	///     mente e o campo.
	///
	/// E devolve o RETRATO: o fundo, a tinta de cada raio como ela e, e em MAGENTA o que os dois pintaram.
	/// Uma sonda que conta pixels tem que mostrar quais.
	///
	/// Nulo = nao deu pra ler: as chapas sairam de tamanhos diferentes, ou um dos raios nao deixou tinta.
	/// </summary>
	private Pontas? LerAsPontas(Image nada, Image soA, Image soB, Image nada2, out Image? retrato)
	{
		retrato = null;
		if (_ptA is not { } a || _ptB is not { } b) return null;

		int largura = nada.GetWidth(), altura = nada.GetHeight();
		byte[] bNada = Rgba(nada), bA = Rgba(soA), bB = Rgba(soB), bNada2 = Rgba(nada2);
		if (bA.Length != bNada.Length || bB.Length != bNada.Length || bNada2.Length != bNada.Length) return null;

		// DE PIXEL DE FOTO PRA PIXEL DE MUNDO: o inverso do `NaFoto`.
		Viewport? v = GetViewport();
		Transform2D doMundo = v == null ? Transform2D.Identity : (v.GetFinalTransform() * v.CanvasTransform).AffineInverse();

		Vector2 pontaDeA = a.PontaDesenhada, rumoDeA = a.Rumo, pontaDeB = b.PontaDesenhada, rumoDeB = b.Rumo;
		float[] frenteDeA = NovaFrente(), frenteDeB = NovaFrente(), deBContraA = NovaFrente();
		int sobrepostos = 0;
		byte[] pintado = (byte[])bNada.Clone();

		for (int y = 0, i = 0; y < altura; y++)
			for (int x = 0; x < largura; x++, i += 4)
			{
				bool deA = PintouAqui(bA, bNada, bNada2, i), deB = PintouAqui(bB, bNada, bNada2, i);
				if (!deA && !deB) continue;

				Vector2 noMundo = doMundo * new Vector2(x + 0.5f, y + 0.5f);
				if (deA) Ver(frenteDeA, (noMundo - pontaDeA).Dot(rumoDeA));
				if (deB)
				{
					Ver(frenteDeB, (noMundo - pontaDeB).Dot(rumoDeB));
					// a frente de B vista do eixo de A: B vem de frente, entao e a MENOR projecao dela
					Ver(deBContraA, -(noMundo - pontaDeA).Dot(rumoDeA));
				}

				if (deA && deB) { sobrepostos++; pintado[i] = 255; pintado[i + 1] = 0; pintado[i + 2] = 255; continue; }
				byte[] de = deA ? bA : bB;
				pintado[i] = de[i];
				pintado[i + 1] = de[i + 1];
				pintado[i + 2] = de[i + 2];
			}

		float passaA = frenteDeA[^1], passaB = frenteDeB[^1];
		if (float.IsNegativeInfinity(passaA) || float.IsNegativeInfinity(passaB)) return null;

		retrato = Image.CreateFromData(largura, altura, false, Image.Format.Rgba8, pintado);
		return new Pontas(-deBContraA[^1] - passaA, passaA, passaB, sobrepostos);
	}

	/// <summary>
	/// LE AS CHAPAS E AFIRMA. `oCampoDiz` entra no rotulo como testemunha: e o que a leitura de antes dizia
	/// deste mesmo quadro.
	/// </summary>
	private void AfirmarAsPontas(string cena, string oCampoDiz)
	{
		if (_ptA is not { } a || _ptB is not { } b || !IsInstanceValid(a) || !IsInstanceValid(b))
		{
			Conferir(false, $"{cena}: ha exatamente DOIS raios desenhados pra ler as pontas no pixel");
			return;
		}
		if (_ptNada is not { } nada || _ptSoA is not { } soA || _ptSoB is not { } soB || _ptNada2 is not { } nada2)
		{
			Nota($"{cena}: as pontas NAO foram medidas no pixel -- faltou chapa (headless nao renderiza)");
			return;
		}
		if (LerAsPontas(nada, soA, soB, nada2, out _ptRetrato) is not { } p)
		{
			Conferir(false, $"{cena}: as quatro chapas das pontas sairam do mesmo tamanho, e cada raio deixou tinta na dele");
			return;
		}

		Vector2 encontro = (a.PontaDesenhada + b.PontaDesenhada) * 0.5f;
		_ptEncontroNaFoto = NaFoto(encontro);
		_ptMeioRecorte = ((a.Medidas.Frente + b.Medidas.Frente) * 0.75f + 24f) * NaFoto(encontro + Vector2.Right).DistanceTo(_ptEncontroNaFoto);

		Conferir(MathF.Abs(p.Folga) <= ToleranciaDoEncontro,
			$"{cena}: NA FOTO as duas cabecas se encontram frente com frente, sem chao entre elas e sem uma dentro da "
			+ $"outra -- folga de {p.Folga:0.0} px da tinta de uma a tinta da outra (negativo e sobreposicao; "
			+ $"{p.Sobrepostos} px de foto pintados pelas duas). Os campos, deste mesmo quadro: {oCampoDiz}");
		Conferir(MathF.Abs(p.PassaA) <= ToleranciaDaPonta && MathF.Abs(p.PassaB) <= ToleranciaDaPonta,
			$"{cena}: a tinta de cada raio ACABA NA PONTA QUE ELE ANUNCIA (`PontaDesenhada`: onde o servidor encosta as "
			+ $"coisas, e onde o `World` poe a estrela) -- a cabeca da esquerda passa {p.PassaA:+0.0;-0.0;0.0} px dela e a "
			+ $"da direita {p.PassaB:+0.0;-0.0;0.0}");

		if (_ptSoAComDefeito is not { } soAComDefeito || _ptSoBComDefeito is not { } soBComDefeito) return;

		// O CONTRA-EXEMPLO: as duas cabecas pintadas adiante da ponta que anunciam. O campo e o mesmo (nenhum
		// node se mexeu); a folga na foto tem que cair o tanto do defeito, uma vez de cada lado.
		float defeito = 2f * ProjetilDesenhado.PixelsDaPontaAdiante;
		Pontas? comDefeito = LerAsPontas(nada, soAComDefeito, soBComDefeito, nada2, out _ptRetratoComDefeito);
		Conferir(comDefeito is { } d && d.Folga < -ToleranciaDoEncontro && MathF.Abs(p.Folga - d.Folga - defeito) <= ToleranciaDoEncontro,
			$"[injecao] {cena}: com `DefeitoDoFeixe.PontaAdiante` (cada cabeca pintada "
			+ $"{ProjetilDesenhado.PixelsDaPontaAdiante:0} px adiante da ponta que anuncia) os campos dizem o mesmo e a regua "
			+ (comDefeito is { } dd
				? $"da foto REPROVA -- a folga cai de {p.Folga:0.0} pra {dd.Folga:0.0} px, os {defeito:0} px do defeito "
				  + $"(as cabecas passam {dd.PassaA:+0.0;-0.0;0.0} e {dd.PassaB:+0.0;-0.0;0.0} px da ponta)"
				: "da foto nao conseguiu ler as chapas"));
	}

	/// <summary>
	/// GRAVA OS RETRATOS que o <see cref="AfirmarAsPontas"/> deixou, e esquece as chapas. Na tira (a cena 3)
	/// eles vao inteiros, como as outras chapas dela, e saem colados na ordem das outras bancadas: a esquerda
	/// o defeito injetado, depois a producao. Fora dela (a cena 1, de cabecas de 7 px) vai um recorte ampliado
	/// em volta do encontro -- inteira, a foto mostraria uma manchinha.
	/// </summary>
	private void GuardarAsPontas(string cena, string destino, bool naTira)
	{
		const string Legenda = "cada cabeca sozinha, sem estrela e sem halo -- em MAGENTA, o que as DUAS pintaram";
		int desde = _tira.Count;
		if (_ptRetratoComDefeito is { } comDefeito)
			Guardar(Enquadrar(comDefeito, naTira), destino + "-com-defeito.png",
					$"{cena}, AS PONTAS COM O DEFEITO INJETADO (`DefeitoDoFeixe.PontaAdiante`): {Legenda}", _ptEncontroNaFoto, naTira);
		if (_ptRetrato is { } retrato)
			Guardar(Enquadrar(retrato, naTira), destino + ".png", $"{cena}, AS PONTAS: {Legenda}", _ptEncontroNaFoto, naTira);
		if (naTira && _tira.Count > desde)
			Colar("user://embateki-tira-das-pontas.png", desde, larguraDoCorte: 420, alturaDoCorte: 720, escala: 1);
		ZerarAsPontas();
	}

	/// <summary>O retrato inteiro, ou um recorte deitado em volta do encontro, ampliado tres vezes.</summary>
	private Image Enquadrar(Image retrato, bool inteiro)
	{
		if (inteiro) return retrato;

		int meia = Mathf.CeilToInt(_ptMeioRecorte);
		var r = new Rect2I((int)_ptEncontroNaFoto.X - meia, (int)_ptEncontroNaFoto.Y - meia / 2, 2 * meia, meia)
			.Intersection(new Rect2I(0, 0, retrato.GetWidth(), retrato.GetHeight()));
		if (r.Size.X < 16 || r.Size.Y < 16) return retrato;

		Image pedaco = retrato.GetRegion(r);
		pedaco.Resize(pedaco.GetWidth() * 3, pedaco.GetHeight() * 3, Image.Interpolation.Nearest);
		return pedaco;
	}

	// =====================================================================
	// A LEITURA DO DESENHO
	// =====================================================================
	/// <summary>
	/// QUANTOS FEIXES ESTAO DESENHADOS AGORA, e qual o COMPRIMENTO de cada um NA TELA.
	///
	/// O comprimento e a distancia entre a cabeca desenhada (`World.TirosDesenhados`, que e a
	/// `Position` do node depois da interpolacao) e o CORPO desenhado do dono. Enquanto alguem alimenta
	/// o raio, a cauda dele e a mao do dono -- entao esta distancia e o feixe inteiro, e e exatamente o
	/// que estica e encolhe na foto.
	///
	/// A cabeca e atribuida ao duelista MAIS PERTO dela porque o cliente nao guarda dono de tiro (o
	/// pacote de nascimento tem, mas o node nao) -- e no embate as duas cabecas estao no MESMO ponto,
	/// entao a unica pergunta que sobra e "de qual das duas maos sai o rastro ate aqui", que e uma
	/// pergunta de distancia.
	/// </summary>
	private int ContarFeixes(World mundo, out float deA, out float deB)
	{
		deA = deB = 0;
		Vector2 corpoA = mundo.PosicaoDesenhadaDe(_duelistaA) ?? Vector2.Zero;
		Vector2 corpoB = mundo.PosicaoDesenhadaDe(_duelistaB) ?? Vector2.Zero;

		int n = 0;
		var soma = Vector2.Zero;
		foreach ((int _, Jandirus.Core.Combat.ArteDeKi _, Jandirus.Core.Combat.TipoDeProjetil tipo,
				  Vector2 onde, float _) in mundo.TirosDesenhados())
		{
			if (tipo != Jandirus.Core.Combat.TipoDeProjetil.Beam) continue;
			n++;
			soma += onde;
		}
		if (n == 0) return 0;

		// ============================ A MEDIA DAS DUAS CABECAS E O ENCONTRO -- E ESSA E A REGRA ============================
		// A primeira versao atribuia cada cabeca ao duelista MAIS PERTO dela, e isso desmoronava
		// justamente na foto que a familia existe pra tirar: com o medidor em 79 o encontro ja caminhou
		// pra perto de B, entao as DUAS cabecas ficam mais perto de B e o feixe de A media zero.
		//
		// A leitura certa nao precisa saber de quem e cada cabeca: desde 2026-09-07 elas ficam um RAIO
		// de cada lado do ponto de contato (`MoverOEncontro`, "se empurrando" e nao uma em cima da
		// outra), e a MEDIA das duas e exatamente o encontro. O comprimento do feixe de cada um e a
		// distancia do encontro ate o CORPO dele. Enquanto o dono alimenta o raio, a cauda e a mao dele
		// -- entao essa distancia e o feixe inteiro, e e exatamente o que estica e encolhe na foto.
		// ==============================================================================================================
		Vector2 encontro = soma / n;
		deA = encontro.DistanceTo(corpoA);
		deB = encontro.DistanceTo(corpoB);
		return n;
	}

	// =====================================================================
	// A FOTO
	// =====================================================================
	private void Fotografar(string destino, string rotulo, Vector2 centro)
	{
		if (Tela() is { } img) Guardar(img, destino, rotulo, centro);
		else Nota($"{rotulo}: sem foto (headless nao renderiza)");
	}

	/// <summary>A tela como esta agora -- o ULTIMO quadro renderizado --, ou nulo sem janela.</summary>
	private Image? Tela()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	/// <summary>
	/// Grava uma chapa ja tirada e a guarda pra a tira. `naTira` falso = so grava: a cena 1 conta as chapas
	/// da tira dela pra saber qual foto falta, e um recorte a mais ali trocaria a foto do empurrao de lugar.
	/// </summary>
	private void Guardar(Image img, string destino, string rotulo, Vector2 centro, bool naTira = true)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho}");
			if (naTira) _tira.Add((img, centro));
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	/// <summary>
	/// EM QUE PIXEL DA FOTO este ponto do mundo caiu. E o `NaTela` mais o esticamento da janela
	/// (`GetFinalTransform`): a foto sai no tamanho da JANELA, e com o modo `canvas_items` do projeto
	/// uma janela maior que 1280x720 desenha tudo ampliado. Pra recortar uma tira o erro nao importa;
	/// pra ler UM pixel, importa.
	/// </summary>
	private Vector2 NaFoto(Vector2 mundo)
	{
		Viewport? v = GetViewport();
		return v == null ? mundo : v.GetFinalTransform() * (v.CanvasTransform * mundo);
	}

	/// <summary>ONDE, NA TELA, ESTE PONTO DO MUNDO ESTA -- passado pela camera.</summary>
	private Vector2 NaTela(Vec2 mundo)
		=> (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * new Vector2(mundo.X, mundo.Y);

	/// <summary>
	/// A TIRA, COLADA -- os quadros da mesma cena lado a lado, no MESMO tamanho e em volta do MESMO
	/// ponto. E a unica forma de a comparacao ("este feixe esticou, aquele encolheu") ser feita pelo
	/// olho: tres arquivos de 1600x900 com a acao ocupando um centesimo da area obrigam quem le a abrir
	/// tres janelas e comparar de cabeca. Mesma receita da tira da `--diagraio`.
	///
	/// Os originais continuam salvos: um recorte e sempre uma escolha de quem recortou.
	/// </summary>
	private void Colar(string destino, int desde, int larguraDoCorte, int alturaDoCorte, int escala)
	{
		if (desde < 0) desde = 0;
		List<(Image Foto, Vector2 Centro)> tira = _tira.GetRange(desde, _tira.Count - desde);
		if (tira.Count == 0) return;

		const int Vao = 6;
		int largura = (larguraDoCorte * tira.Count + Vao * (tira.Count - 1)) * escala;
		Image colagem = Image.CreateEmpty(largura, alturaDoCorte * escala, false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));

		for (int i = 0; i < tira.Count; i++)
		{
			(Image foto, Vector2 centro) = tira[i];
			// O CORTE E DEITADO (mais largo que alto) porque a cena e: os dois duelistas ficam um de
			// frente pro outro na HORIZONTAL, e um quadrado ou corta os corpos ou desperdica meia foto
			// de chao. A JANELA E EMPURRADA PRA DENTRO, e nao cortada: cortar devolve pedacos de
			// tamanhos diferentes quando a acao esta perto da borda, e ai os quadros deixam de ser
			// comparaveis -- que e a unica coisa que a tira existe pra permitir.
			int x0 = Math.Clamp((int)centro.X - larguraDoCorte / 2, 0, Math.Max(0, foto.GetWidth() - larguraDoCorte));
			int y0 = Math.Clamp((int)centro.Y - alturaDoCorte / 2, 0, Math.Max(0, foto.GetHeight() - alturaDoCorte));
			var r = new Rect2I(x0, y0, larguraDoCorte, alturaDoCorte)
				.Intersection(new Rect2I(0, 0, foto.GetWidth(), foto.GetHeight()));
			if (r.Size.X < 16 || r.Size.Y < 16) continue;

			Image pedaco = foto.GetRegion(r);
			// O `BlitRect` EXIGE O MESMO FORMATO e CALA quando nao tem (a tira sai um retangulo preto).
			pedaco.Convert(Image.Format.Rgba8);
			pedaco.Resize(pedaco.GetWidth() * escala, pedaco.GetHeight() * escala, Image.Interpolation.Nearest);
			colagem.BlitRect(pedaco, new Rect2I(Vector2I.Zero, pedaco.GetSize()),
							 new Vector2I(i * (larguraDoCorte + Vao) * escala, 0));
		}

		try
		{
			colagem.SavePng(ProjectSettings.GlobalizePath(destino));
			_passos.Add($"  ok     a tira dos {tira.Count} quadros, colada: {ProjectSettings.GlobalizePath(destino)}");
		}
		catch (Exception e) { Nota($"tira: {e.Message}"); }
	}

	private void Fechar()
	{
		_acabou = true;
		SoltarAsPontas();
		SoltarOObturador();
		S?.EmbateDeFoto_Limpar();
		if (World.Instancia is { } mundo) mundo.FocoDeTeste = null;
		GD.Print("\n[embatekifoto] ===== AS FOTOS DA COLISAO DE KI =====");
		foreach (string l in _passos) GD.Print("[embatekifoto] " + l);
		GD.Print(_falhas.Count == 0
			? "[embatekifoto] ===== TUDO OK ====="
			: $"[embatekifoto] ===== {_falhas.Count} FALHA(S) =====\n[embatekifoto]   "
			  + string.Join("\n[embatekifoto]   ", _falhas));
		GetTree().Quit();
	}
}
