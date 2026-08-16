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

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagembateki` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }

		_t += delta;

		switch (_passo)
		{
			case 0: MontarOEmpurrao(srv, cli); break;
			case 1: EsperarOEncontro(srv, mundo); break;
			case 2: Empurrar(srv, mundo); break;
			case 3: MontarOEmpate(srv, cli); break;
			case 4: EsperarOEmpate(srv, mundo); break;
			case 5: DepoisDaOnda(srv, mundo); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

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
		if (_t < 3) return;   // deixa o mundo assentar e a camera achar o corpo

		(_duelistaA, _duelistaB) = srv.EmbateDeFoto_Montar(
			cli.LocalId, tiles: 5, bpDeA: 7_500, bpDeB: 5_000, tecladoEmA: true, tilesAbaixo: 3);

		Conferir(_duelistaA != 0 && _duelistaB != 0,
			"os dois duelistas entraram no mundo, num corredor de chao de verdade");
		if (_duelistaA == 0) { Nota("sem corredor livre: a cena nao cabe neste pedaco de mapa"); Fechar(); return; }

		Virar(1);
	}

	/// <summary>
	/// A FOTO SO VALE COM OS DOIS FEIXES JA ENCOSTADOS. Ela nao sai no relogio: sai quando o GATILHO de
	/// producao juntou as duas cabecas -- antes disso o que ha na tela sao dois tiros a caminho, que e
	/// outra imagem.
	/// </summary>
	private void EsperarOEncontro(Jandirus.Server.GameServer srv, World mundo)
	{
		(bool existe, double medidor, Vec2 ponto, _, _, float fa, float fb) = srv.EmbateDeFoto_Estado();
		if (!existe)
		{
			if (_t > 20) { Conferir(false, "os dois raios se encontraram e viraram disputa"); Fechar(); }
			return;
		}

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
		Virar(2);
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
		Fechar();
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

		// ============================ AS DUAS CABECAS ESTAO NO MESMO PONTO -- E ESSA E A REGRA ============================
		// A primeira versao atribuia cada cabeca ao duelista MAIS PERTO dela, e isso desmoronava
		// justamente na foto que a familia existe pra tirar: com o medidor em 79 o encontro ja caminhou
		// pra perto de B, entao as DUAS cabecas ficam mais perto de B e o feixe de A media zero.
		//
		// A leitura certa nao precisa saber de quem e cada cabeca, porque elas sao o MESMO ponto (o
		// `MoverOEncontro` escreve `Feixe.Pos = d.Ponto` nas duas): o comprimento do feixe de cada um e a
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
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho}");
			_tira.Add((img, centro));
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
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
		S?.EmbateDeFoto_Limpar();
		GD.Print("\n[embatekifoto] ===== AS FOTOS DA COLISAO DE KI =====");
		foreach (string l in _passos) GD.Print("[embatekifoto] " + l);
		GD.Print(_falhas.Count == 0
			? "[embatekifoto] ===== TUDO OK ====="
			: $"[embatekifoto] ===== {_falhas.Count} FALHA(S) =====\n[embatekifoto]   "
			  + string.Join("\n[embatekifoto]   ", _falhas));
		GetTree().Quit();
	}
}
