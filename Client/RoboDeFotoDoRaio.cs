using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// AS TRES FOTOS DOS TRES PEDIDOS (`--diagraio`).
///
/// ============================ POR QUE ELA EXISTE, SE AS OUTRAS TRES JA MEDEM ============================
/// Os tres pedidos do dono sao visuais ou de sensacao. As bancadas sem foto ja respondem tudo o que
/// da pra responder em numero:
///
///   * `--projetilteste` (familias 8, 9 e 10) le os BYTES do rastro e mede o arrasto em px/s;
///   * `--iateste` (7b, 7c, 7d) prova o portao da cinematica nos dois sentidos, com defeito injetado;
///   * `--diagdecalque` le o RECORTE que a onda recebeu e o teto da fila.
///
/// O que sobra e o que nenhum campo responde, e e a metade que o dono ve:
///
///   1. o NPC parado na cinematica esta parado NA TELA -- e nao so no `Pos` do servidor;
///   2. o mesmo disparo risca a terra E ondula a agua, cada celula com a marca do que ela e;
///   3. o corpo levado pelo feixe ANDA JUNTO, em quadros seguidos, e nao "aparece adiante".
///
/// Cada foto vem com o CONTRA-EXEMPLO ao lado, porque foto de coisa parada e o efeito mais facil de
/// falsificar que existe: um NPC que nao anda porque o comando nunca chegou da a MESMA foto de um
/// NPC preso pela cena. Por isso a cena A fotografa TRES momentos -- andando antes, preso durante,
/// andando depois -- e o rastro de posicao aparece nas tres.
/// =====================================================================================================
///
/// ============================ E DUAS CENAS DO DESENHO QUE ANDA ATRAS (2026-10-08) ============================
/// O node do tiro e desenhado um tempo atras do servidor, e duas coisas que o servidor ANUNCIA chegavam antes
/// de o desenho chegar la (ver `ProjetilDesenhado.AnuncioSemEsperaDeTeste`):
///
///   E. a marca no chao nasce debaixo da cabeca que se VE, e nao a frente da ponta do raio;
///   F. a bola e desenhada ATE o ponto em que estoura, em vez de sumir quase um tile antes.
///
/// Cada uma voa DUAS vezes -- a segunda com o defeito injetado, a mesma regua reprovando -- e as fotos dos
/// dois voos saem lado a lado.
/// =============================================================================================================
///
/// ============================ O RASTRO DE POSICAO E DESENHADO, E E LIDO DO DESENHO ============================
/// O ponto que ele marca nao e o `Pos` do servidor: e o <see cref="World.PosicaoDesenhadaDe"/> -- a
/// posicao em que o corpo FOI DESENHADO naquele quadro, depois da interpolacao do cliente. E de
/// proposito: o defeito que o dono descreveu ("npcs se movendo enquanto transformam") e um defeito de
/// TELA, e um servidor que segurasse o corpo enquanto o cliente o desliza continuaria errado pra quem
/// joga.
/// ==========================================================================================================
///
/// COMO RODAR -- um processo so, e ele PRECISA de janela (no headless o `GetImage` volta vazio):
///
///     Godot --path . --host --rede 7952 --aguateste --vooteste --bpteste 3000000 --horateste 0.5 \
///           --diagraio --resolution 1280x720 --raca Human --conta bancada_raio --nome Raio
///
/// `--aguateste` poe o corpo na BEIRA de um lago, no seco e virado pra agua -- e o unico berco em que
/// a cena B (terra E agua no mesmo disparo) cabe na tela. `--horateste 0.5` crava meio-dia: a hora do
/// mundo e sorteada, e uma foto de lago as 3 da manha nao responde nada pra ninguem.
///
/// As fotos saem em `user://raio-*.png`.
/// </summary>
public partial class RoboDeFotoDoRaio : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];

	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	/// <summary>Depois disto ela desiste. O berco pode nao ter achado lago nenhum.</summary>
	private const double Paciencia = 180;

	/// <summary>O corpo da cena A (o que transforma) e o da cena C (o que e levado).</summary>
	private int _npcDaCena, _vitima;

	/// <summary>O rumo em que ha agua a frente -- achado no mapa, nao escolhido.</summary>
	private Vector2 _rumoDaAgua = Vector2.Right;

	/// <summary>Onde a cena C comecou, e os tres quadros que ela guardou.</summary>
	private readonly List<(Vector2 Corpo, Vector2 Cabeca)> _tresQuadros = [];

	private RastroDePosicao? _rastro;

	/// <summary>
	/// A FAISCA DOS ACERTOS DA CENA C (dono, 2026-09-23: *"o efeito de hit tem que ser na cabeca do beam e
	/// nao no meio dele"*). Contadas no quadro em que cada uma nasce, contra os corpos desenhados daquele
	/// quadro: quantas estouraram na frente da cabeca (a beirada de quem apanha) e quantas no meio do raio.
	/// </summary>
	private (Vector2 Onde, int Alvo)? _faiscaVista;
	private int _faiscasNaCabeca, _faiscasNoMeio, _faiscasFora;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Ready()
	{
		// A REGUA DAS CENAS E E F LE NO FIM DO QUADRO -- ver `FimDoQuadro`.
		AddChild(new FimDoQuadro { Name = "FimDoQuadro", Agora = NoFimDoQuadro });
		if (C is { } cli) cli.Golpe += AoGolpeNoAlvoDaBola;
	}

	/// <summary>
	/// O DEFEITO INJETADO NAO SOBREVIVE A BANCADA: e um campo ESTATICO de producao, e se este node sair da
	/// arvore no meio de um voo injetado (janela fechada na mao, excecao num passo) o jogo seguinte neste
	/// processo plantaria todo sulco adiantado.
	/// </summary>
	public override void _ExitTree()
	{
		ProjetilDesenhado.AnuncioSemEsperaDeTeste = false;
		if (C is { } cli) cli.Golpe -= AoGolpeNoAlvoDaBola;
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;
		if (Decalques.Instancia is not { } dec) return;
		if (S is not { } srv) { Nota("sem servidor no processo (`--diagraio` precisa de `--host`)"); Fechar(); return; }

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }

		_t += delta;

		switch (_passo)
		{
			case 0: Assentar(mundo, srv, cli); break;
			case 1: CenaA_Antes(mundo, srv, delta); break;
			case 2: CenaA_Durante(mundo, srv, delta); break;
			case 3: CenaA_Depois(mundo, srv, delta); break;
			case 4: CenaB_Plantar(mundo, srv, cli); break;
			case 5: CenaB_Fotografar(mundo, srv, cli); break;
			case 6: CenaC_Plantar(mundo, srv, cli); break;
			case 7: CenaC_TresQuadros(mundo, srv, cli); break;
			case 8: CenaD_Plantar(mundo, srv, cli); break;
			case 9: CenaD_Fotografar(mundo, srv, cli); break;
			case 10: CenaE_Plantar(mundo, srv, cli); break;
			case 11: CenaE_Voar(srv, cli); break;
			case 12: CenaF_Plantar(mundo, srv, cli); break;
			case 13: CenaF_Voar(srv); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	/// <summary>O dobro do teto do atraso de desenho do corpo remoto -- ver a perna `CenaA_Durante`.</summary>
	private const double EsperaDoDesenho = 0.5;

	/// <summary>
	/// UMA FAISCA NOVA NESTE QUADRO? Ela e medida contra os corpos DESENHADOS agora: o ponto certo e a frente
	/// da cabeca, que e a beirada de quem apanha (meia largura antes do centro, vindo do atirador); o errado,
	/// o da foto do dono, e o meio entre os dois.
	/// </summary>
	private void ContarAFaisca(World mundo, GameClient cli)
	{
		if (mundo.UltimaFaiscaDeTeste is not { } ultima || ultima == _faiscaVista) return;
		_faiscaVista = ultima;

		// SO A FAISCA DE QUEM A CENA LEVA (2026-09-23). O berco e o lago de verdade, com cidadaos de verdade: o
		// raio da cena B atravessa o lago e acerta a vizinha do outro lado, e a faisca DELA chegava aqui e era
		// medida contra o corpo da cena C -- "3 noutro lugar" que nao eram deste raio. Ela fica anotada, sem contar.
		if (ultima.Alvo != _vitima)
		{
			Nota($"faisca em OUTRO corpo (id {ultima.Alvo}), fora da regua: {ultima.Onde}");
			return;
		}
		Vector2 faisca = ultima.Onde;

		Vector2 vitima = mundo.PosicaoDesenhadaDe(_vitima) ?? Vector2.Zero;
		Vector2 atirador = mundo.PosicaoDesenhadaDe(cli.LocalId) ?? Vector2.Zero;
		Vector2 rumo = (vitima - atirador).Normalized();
		Vector2 naBeirada = vitima - rumo * Jandirus.Core.Combat.Feixe.MeioCorpo;
		Vector2 meio = (vitima + atirador) * 0.5f;

		// UM TILE DE FOLGA: o ponto do impacto chega em coordenada do SERVIDOR, e o corpo levado e desenhado
		// interpolado, um pacote atras -- andando na velocidade do feixe. O meio fica a varios tiles dali.
		if (faisca.DistanceTo(naBeirada) < ZoneCollision.TileSize) _faiscasNaCabeca++;
		else if (faisca.DistanceTo(meio) < ZoneCollision.TileSize) _faiscasNoMeio++;
		else
		{
			_faiscasFora++;
			Nota($"faisca fora dos dois lugares: {faisca} (beirada {naBeirada}, meio {meio})");
		}
	}

	// =====================================================================
	// 0) O BERCO ASSENTA
	// =====================================================================
	private void Assentar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 3) return;

		_rastro ??= RastroDePosicao.Pendurar(mundo);

		// O NPC DA CENA A nasce tres tiles ao lado, com a escada aberta em maestria ZERO -- que e a
		// unica condicao em que ha cinematica (ver `ForjarCorpoDeFoto`).
		_npcDaCena = srv.ForjarCorpoDeFoto(cli.LocalId, new Vec2(0, 3 * ZoneCollision.TileSize),
										   "Foto: o que transforma", 5e9, comEscada: true);
		Conferir(_npcDaCena != 0, "o corpo da cena A entrou no mundo pelo `PorNoMundo`");
		if (_npcDaCena == 0) { Fechar(); return; }

		Virar(1);
	}

	// =====================================================================
	// 1) CENA A, ANTES: ele quer andar E ANDA
	// =====================================================================
	/// <summary>
	/// O CONTROLE DA FOTO. Sem esta perna, a foto do meio nao prova nada: um corpo que nunca quis
	/// andar da exatamente a mesma imagem de um corpo preso pela cena.
	/// </summary>
	private void CenaA_Antes(World mundo, Jandirus.Server.GameServer srv, double delta)
	{
		srv.EmpurrarCorpoDeFoto(_npcDaCena, new Vec2(1, 0), delta);
		_rastro?.Marcar(mundo.PosicaoDesenhadaDe(_npcDaCena));

		if (_t < 1.2) return;

		// ============================ HA ALGUEM DENTRO DA FOTO? ============================
		// A primeira rodada saiu com o rastro certo e NENHUM corpo desenhado nele -- o corpo forjado
		// nao tinha `Visual`, e o cliente nao desenha sprite nenhum pra quem nao tem. Todas as
		// checagens ficaram verdes, porque elas leem a POSICAO desenhada (que existe de qualquer
		// jeito) e nao o pixel. Esta linha e a que nao deixa isso voltar calado.
		// =================================================================================
		Conferir(mundo.CorpoDeTeste(_npcDaCena) != null,
			"o NPC da cena A tem CORPO DESENHADO na tela (e nao so uma posicao)");

		float espalhou = _rastro?.Espalhamento ?? 0;
		Fotografar("user://raio-1a-npc-andando.png",
				   $"CENA A (controle): o NPC ANDANDO na base -- rastro de {espalhou:0} px",
				   NaTela(mundo, _npcDaCena));
		Conferir(espalhou > 8f,
			$"(controle) na base, com o mesmo comando, o NPC ANDA na tela ({espalhou:0.0} px de rastro)");

		double cena = srv.TransformarCorpoDeFoto(_npcDaCena);
		Conferir(cena > 0, $"...e a transformacao pelo funil de producao abriu cinematica ({cena:0.#}s)");
		if (cena <= 0) { Nota("sem cinematica: a foto do meio nao teria o que mostrar"); Fechar(); return; }

		_rastro?.Limpar();
		Virar(2);
	}

	// =====================================================================
	// 2) CENA A, DURANTE: o mesmo comando, e ele NAO SAI DO LUGAR
	// =====================================================================
	private void CenaA_Durante(World mundo, Jandirus.Server.GameServer srv, double delta)
	{
		srv.EmpurrarCorpoDeFoto(_npcDaCena, new Vec2(1, 0), delta);

		// ============================ O DESENHO CHEGA DEPOIS DO SERVIDOR (2026-09-23) ============================
		// O corpo remoto e desenhado NO PASSADO: tres tiques no minimo, ate `AtrasoTeto` (250 ms) com rede ruim
		// (`RemotePlayer.AjustarAtraso`). Esta perna comecava a marcar o rastro no mesmo quadro em que a perna de
		// cima mandou transformar -- e o que ela media era a caminhada de ANTES terminando de chegar na tela: 30 px
		// de rastro com o funil recusando todo passo desde o primeiro tique. Espera-se o dobro do teto; a cena dura
		// 10 s, e o que se pergunta e se o corpo anda DURANTE ela, nao se o desenho alcancou o servidor.
		// ======================================================================================================
		if (_t >= EsperaDoDesenho) _rastro?.Marcar(mundo.PosicaoDesenhadaDe(_npcDaCena));

		srv.RegarOKiDeFoto(_npcDaCena);   // o dreno da forma nao e o assunto -- ver `RegarOKiDeFoto`
		if (_t < 2.0) return;

		(double cena, bool podeAndar, string forma, _) = srv.CenaDeFoto(_npcDaCena);
		float espalhou = _rastro?.Espalhamento ?? 0;

		Fotografar("user://raio-1b-npc-na-cinematica.png",
				   $"CENA A: NA CINEMATICA ({forma}, faltam {cena:0.#}s) -- rastro de {espalhou:0} px",
				   NaTela(mundo, _npcDaCena));

		Conferir(cena > 0 && !podeAndar,
			$"a foto foi tirada COM a cinematica rolando e o funil recusando o passo ({cena:0.#}s)");
		Conferir(espalhou < 1f,
			$"...e o rastro de posicao na TELA e um ponto so: ele nao saiu do lugar ({espalhou:0.00} px)");

		_rastro?.Limpar();
		Virar(3);
	}

	// =====================================================================
	// 3) CENA A, DEPOIS: acabada a cena, ele anda de novo
	// =====================================================================
	/// <summary>
	/// A OUTRA METADE, e ela e a que impede o conserto de virar uma IA travada: "nunca anda" passaria
	/// verde na foto do meio e quebraria o jogo muito mais do que o defeito original.
	///
	/// ============================ ELE VOLTA PELO CAMINHO QUE ELE MESMO ABRIU ============================
	/// O rumo aqui e o CONTRARIO do das duas pernas de cima, e nao e capricho: o berco `--aguateste`
	/// poe todo mundo na BEIRA DE UM LAGO, e a perna de controle andou 65 px pra leste ate encostar na
	/// agua -- que barra quem esta a pe. Com o rumo repetido, esta terceira perna reprovava com
	/// `0,0 px` e `funil=livre`: o portao estava aberto e quem segurava o corpo era o lago.
	///
	/// Voltando pra oeste ele anda por celulas que ACABOU de atravessar, e portanto sabidamente
	/// livres. A bancada nao pode escolher chao que ela nao sabe que existe.
	/// ================================================================================================
	/// </summary>
	private void CenaA_Depois(World mundo, Jandirus.Server.GameServer srv, double delta)
	{
		srv.RegarOKiDeFoto(_npcDaCena);   // ver `RegarOKiDeFoto`: o SSJ de maestria zero queima Ki
		(double cena, _, string forma, string motivo) = srv.CenaDeFoto(_npcDaCena);

		// ESPERA A CENA VENCER SOZINHA, no relogio do servidor -- nao ha campo escrito aqui.
		if (cena > 0)
		{
			srv.EmpurrarCorpoDeFoto(_npcDaCena, new Vec2(-1, 0), delta);
			if (_t > 150) { Conferir(false, "a cinematica venceu sozinha dentro da paciencia"); Fechar(); }
			return;
		}

		srv.EmpurrarCorpoDeFoto(_npcDaCena, new Vec2(-1, 0), delta);
		_rastro?.Marcar(mundo.PosicaoDesenhadaDe(_npcDaCena));

		// ============================ ELE PRECISA SAIR DE DENTRO DA PROPRIA FUMACA ============================
		// A cinematica do degrau termina com CRATERA e FUMACA, e a fumaca desenha POR CIMA do corpo (e
		// o pedido do dono, `Decalques.Plantar`). Meio segundo de caminhada nao tira o boneco de dentro
		// dela: a foto saia com o rastro certo e o NPC invisivel debaixo de um borrao marrom.
		//
		// Noventa quadros (~1,5 s) bastam pra ele andar pra fora. O numero e de QUADROS e nao de
		// segundos porque e um ponto de rastro por quadro -- e o rastro e o que a foto tem que mostrar.
		// ==================================================================================================
		if (_rastro is not { Pontos: >= 90 }) return;

		float espalhou = _rastro.Espalhamento;
		Fotografar("user://raio-1c-npc-depois.png",
				   $"CENA A: ACABADA a cinematica -- rastro de {espalhou:0} px",
				   NaTela(mundo, _npcDaCena));

		// A TIRA DA CENA A: andando / preso / andando, os tres no mesmo recorte e em volta do MESMO
		// corpo. E a leitura que o dono pediu, num arquivo so.
		Montar("user://raio-1-cena-a-tres.png", desde: 0, lado: 320, escala: 2);
		Conferir(espalhou > 8f,
			$"...e ACABADA a cena ele volta a andar na tela ({espalhou:0.0} px de rastro"
			+ $"; forma={forma}, funil={(motivo.Length == 0 ? "livre" : motivo)})");

		// O RASTRO NAO E DESLIGADO: ele e reaproveitado na CENA C, onde a pergunta e a mesma feita ao
		// contrario -- "este corpo se moveu na tela?" -- so que a resposta certa la e SIM.
		_rastro.Limpar();
		Virar(4);
	}

	// =====================================================================
	// 4) CENA B: o raio atravessando TERRA e AGUA
	// =====================================================================
	private void CenaB_Plantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (mundo.PosicaoLocal is not { } eu) { Nota("cena B: sem corpo local"); Virar(6); return; }

		if (!AcharAMargem(mundo, eu, out _rumoDaAgua))
		{
			Conferir(false, "achei uma margem de lago a frente do corpo (o berco `--aguateste`)");
			Virar(6);
			return;
		}

		dec_limpar();
		int id = srv.RaioDeFoto(cli.LocalId, new Vec2(_rumoDaAgua.X, _rumoDaAgua.Y),
								alcanceTiles: 12, baseDano: 0.001);
		Conferir(id != 0, $"o raio da cena B saiu pelo `Disparar` de producao, rumo {_rumoDaAgua}");
		Virar(5);
	}

	/// <summary>
	/// A FOTO SO VALE COM O FEIXE JA DENTRO DA AGUA. Ela nao sai no relogio: sai quando a CABECA do
	/// tiro passou da margem -- o resto do caminho ja e terra riscada, e e essa a imagem do pedido.
	/// </summary>
	private void CenaB_Fotografar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool vivo, Vec2 cabeca, _, _) = srv.RaioDaFoto(cli.LocalId);
		bool naAgua = vivo && srv.EhAguaDeFoto(cli.LocalId, cabeca);

		LerOChao(mundo, out int sulcosNoSeco, out int sulcosNaAgua, out int ondas);

		// ============================ A CABECA DO SERVIDOR CHEGA ANTES DA DO CLIENTE ============================
		// A primeira rodada desta cena reprovou com `0 ondas`, e o defeito era da bancada: ela
		// disparava o obturador no instante em que a cabeca do SERVIDOR entrava na agua. A do cliente
		// e interpolada -- ela ainda estava no seco --, e a onda so nasce quando o node DESENHADO
		// cruza a celula molhada (`TickDaAguaDosTiros` le `tiro.Position`).
		//
		// Entao a foto espera as DUAS marcas estarem na tela, com prazo: passado ele, ela fotografa o
		// que houver e reprova com o numero na mao. Prazo generoso (o feixe vive ~3,6 s pelos 12 tiles
		// de alcance) e curto o bastante pra caber dentro da vida de uma onda (2 s).
		// =====================================================================================================
		// E DUAS ONDAS, E NAO UMA. Com uma so, a foto sai no instante em que a cabeca ENCOSTA na
		// margem -- e a cabeca do feixe e larga e clara, entao ela tapa a unica onda que existe. Com
		// duas, a primeira ja ficou pra tras e aparece limpa na agua, com a fileira de sulcos atras
		// dela no seco: a imagem inteira do pedido num quadro so.
		bool aFotoTemOsDois = sulcosNoSeco > 0 && ondas >= 2;
		if (!aFotoTemOsDois && (naAgua || vivo) && _t < 9) return;

		Fotografar("user://raio-2-terra-e-agua.png",
				   $"CENA B: o raio cruzando a margem ({sulcosNoSeco} sulcos no seco, {ondas} ondas na agua)");

		Conferir(sulcosNoSeco > 0, $"o raio RISCOU a terra do caminho ({sulcosNoSeco} sulcos em celula seca)");
		Conferir(ondas > 0, $"...e ONDULOU a agua do mesmo disparo ({ondas} ondas em celula molhada)");
		Conferir(sulcosNaAgua == 0,
			$"...e nenhum sulco de terra ficou BOIANDO no lago ({sulcosNaAgua} sobre agua)");

		srv.LimparAFoto();
		Virar(6);
	}

	// =====================================================================
	// 6) CENA C: o corpo levado pelo feixe
	// =====================================================================
	/// <summary>
	/// O DANO FINAL DE CADA TIRO QUE PRECISA SER GOLPE, em vida de membro: pouco acima do corte dos fracos
	/// (`DanoDeKi.CorteDoFraco`, 10). Abaixo dele o tiro encosta sem ferir -- o raio planta na frente do corpo e
	/// nao leva ninguem, a bola estoura sem relato de golpe (`GameServer.EstourarSemFerir`) --, e as cenas C e F
	/// medem justamente o arrasto e o relato. As outras medem geometria e continuam atirando de cocegas.
	/// Um golpe destes nao marca o sprite: a ferida so aparece com 15% do membro, que tem 100 de vida.
	/// </summary>
	private const double DanoQueFere = 12;

	private void CenaC_Plantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;   // deixa o raio da cena B sumir do snapshot

		if (mundo.PosicaoLocal is not { } eu) { Nota("cena C: sem corpo local"); Fechar(); return; }

		// SEIS TILES: alem dos 4 do arremesso (que ARREMESSA em vez de levar) e dentro dos 10 do
		// arrasto. O rumo e o SECO -- o oposto do da agua --, pra que o que se fotografe aqui seja o
		// corpo andando e nao a beira do lago.
		Vector2 rumo = -_rumoDaAgua;
		var desloc = new Vec2(rumo.X * 6 * ZoneCollision.TileSize, rumo.Y * 6 * ZoneCollision.TileSize);

		_vitima = srv.ForjarCorpoDeFoto(cli.LocalId, desloc, "Foto: o levado", 200_000, comEscada: false);
		Conferir(_vitima != 0, "o corpo da cena C entrou no mundo");
		if (_vitima == 0) { Fechar(); return; }

		int id = srv.RaioDeFotoQueFere(cli.LocalId, _vitima, new Vec2(rumo.X, rumo.Y), alcanceTiles: 20, danoFinal: DanoQueFere);
		Conferir(id != 0, "o raio da cena C saiu pelo `Disparar` de producao");
		Virar(7);
	}

	/// <summary>
	/// TRES QUADROS, e eles so comecam a valer quando o feixe DE FATO pegou o corpo (`Arrastando`).
	/// Fotografar no relogio pegaria os quadros de antes do encontro -- corpo parado, feixe a caminho
	/// -- e a sequencia mostraria exatamente nada.
	/// </summary>
	private void CenaC_TresQuadros(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool vivo, Vec2 cabeca, int arrastando, double andou) = srv.RaioDaFoto(cli.LocalId);

		if (_tresQuadros.Count == 0)
		{
			if (arrastando != _vitima)
			{
				if (!vivo || _t > 10)
				{
					Conferir(false, $"o feixe da cena C PEGOU o corpo (arrastando={arrastando}, vivo={vivo}; o raio {srv.FimDoRaioDaFoto()})");
					Fechar();
				}
				return;
			}
			Conferir(true, "o feixe da cena C pegou o corpo -- os tres quadros comecam agora");
			Conferir(mundo.CorpoDeTeste(_vitima) != null,
				"...e o corpo levado tem SPRITE na tela (ver a mesma linha na cena A)");
			_t = 999;   // o primeiro quadro sai JA
		}

		// O RASTRO DO LEVADO, marcado a CADA quadro (e nao so nos tres do obturador): e ele que faz a
		// sequencia de fotos AFIRMAR o movimento em vez de pedir que se compare tres imagens a olho.
		_rastro?.Marcar(mundo.PosicaoDesenhadaDe(_vitima));
		ContarAFaisca(mundo, cli);

		if (_t < 0.16) return;

		Vector2 corpo = mundo.PosicaoDesenhadaDe(_vitima) ?? Vector2.Zero;
		_tresQuadros.Add((corpo, new Vector2(cabeca.X, cabeca.Y)));
		int n = _tresQuadros.Count;

		Fotografar($"user://raio-3-levado-{n}.png",
				   $"CENA C, quadro {n}/3: o corpo levado pelo feixe ({andou:0.0} tiles andados)",
				   NaTela(mundo, _vitima));
		_t = 0;

		if (n < 3) return;

		// ---------- O QUE OS TRES QUADROS TEM QUE MOSTRAR ----------
		float andouCorpo = _tresQuadros[2].Corpo.DistanceTo(_tresQuadros[0].Corpo);
		float andouCabeca = _tresQuadros[2].Cabeca.DistanceTo(_tresQuadros[0].Cabeca);
		bool monotono = _tresQuadros[1].Corpo.DistanceTo(_tresQuadros[0].Corpo) > 1f
						&& _tresQuadros[2].Corpo.DistanceTo(_tresQuadros[1].Corpo) > 1f;

		Conferir(monotono,
			$"o corpo andou em CADA um dos tres quadros -- e nao 'apareceu adiante' de uma vez "
			+ $"({_tresQuadros[1].Corpo.DistanceTo(_tresQuadros[0].Corpo):0.0} px e "
			+ $"{_tresQuadros[2].Corpo.DistanceTo(_tresQuadros[1].Corpo):0.0} px)");
		Conferir(andouCorpo > 8f && Mathf.Abs(andouCorpo - andouCabeca) < andouCabeca * 0.35f + 4f,
			$"...e ele andou JUNTO com a cabeca do feixe, na tela: corpo {andouCorpo:0.0} px, "
			+ $"cabeca {andouCabeca:0.0} px");

		Conferir(_faiscasNaCabeca > 0 && _faiscasNoMeio == 0 && _faiscasFora == 0,
			$"a FAISCA de cada acerto do feixe estoura na CABECA dele, na beirada de quem apanha -- e nao no meio "
			+ $"do raio ({_faiscasNaCabeca} na cabeca, {_faiscasNoMeio} no meio, {_faiscasFora} noutro lugar)");

		// A TIRA DA CENA C -- os tres quadros do arrasto, colados na mesma ordem em que sairam.
		Montar("user://raio-3-levado-tres.png", desde: _quadros.Count - 3, lado: 288, escala: 2);

		srv.LimparAFoto();
		Virar(8);
	}

	// =====================================================================
	// 8) CENA D: o tronco cortado por quem pisa nele (dono, 2026-09-07)
	// =====================================================================
	/// <summary>
	/// *"caso um jogador encoste no TRONCO de um beam, o beam vai ser cortado e a cabeca nova vai
	/// colidir com essa pessoa, e a outra parte continua normalmente"*. E a unica cena que atravessa o
	/// FIO inteiro do corte: o servidor parte o feixe, manda `Nasceu` + `Cortou`, e o que se le aqui e
	/// o que o cliente DESENHOU -- dois feixes, a cabeca nova na frente de quem pisou, a parte de la
	/// seguindo. A `--projetilteste` (familia 12) prova o corte no servidor; sem esta cena, um pacote
	/// errado de `Cortou` deixaria a bancada verde e a tela com o feixe inteiro.
	/// </summary>
	private int _raioDaCenaD, _pisou;

	/// <summary>Quantos tiles a cabeca tinha andado quando alguem pisou no tronco -- a medida de ANTES do corte.</summary>
	private double _andouAntesDoCorte;

	private void CenaD_Plantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.5) return;   // o feixe da cena C some do snapshot
		if (mundo.PosicaoLocal is null) { Nota("cena D: sem corpo local"); Fechar(); return; }

		Vector2 rumo = -_rumoDaAgua;
		_pisou = 0;
		_raioDaCenaD = srv.RaioDeFoto(cli.LocalId, new Vec2(rumo.X, rumo.Y), alcanceTiles: 24, baseDano: 0.002);
		Conferir(_raioDaCenaD != 0, "o raio da cena D saiu pelo `Disparar` de producao");
		if (_raioDaCenaD == 0) { Fechar(); return; }
		Virar(9);
	}

	private void CenaD_Fotografar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		const int T = ZoneCollision.TileSize;
		Vector2 rumo = -_rumoDaAgua;
		(bool vivo, _, _, double andou) = srv.RaioDaFoto(cli.LocalId);

		// PRIMEIRO O TRONCO: so quando a cabeca ja esta a 8 tiles e que alguem pisa a 4.
		if (_pisou == 0)
		{
			if (!vivo || _t > 6) { Conferir(false, $"o raio da cena D ganhou tronco antes de morrer (vivo={vivo}, {andou:0.0} tiles)"); Fechar(); return; }
			if (andou < 8) return;
			var desloc = new Vec2(rumo.X * 4 * T, rumo.Y * 4 * T);
			_pisou = srv.ForjarCorpoDeFoto(cli.LocalId, desloc, "Foto: pisou no tronco", 200_000, comEscada: false);
			_andouAntesDoCorte = andou;
			Conferir(_pisou != 0, $"o corpo que pisa no tronco entrou no mundo, a 4 tiles da mao (cabeca a {andou:0.0} tiles)");
			if (_pisou == 0) { Fechar(); return; }
			_t = 0;
			return;
		}

		// DEPOIS O CORTE, com tempo pra `Nasceu` + `Cortou` + um snapshot chegarem e serem desenhados.
		if (_t < 0.4) return;

		Vector2 corpo = mundo.PosicaoDesenhadaDe(_pisou) ?? Vector2.Zero;
		// ============================ SO OS FEIXES DESTA CENA, PELO ID (2026-09-23) ============================
		// O da cena C ainda esta no ar (ele esvazia devagar), e quem pisou esta no caminho dele TAMBEM: ele e
		// cortado junto, e a sua metade de ca para na frente do mesmo corpo. A primeira versao desta regua
		// contava "o que esta a ate 12 tiles de quem pisou" e passava ou nao conforme a metade de la da cena C
		// ja tivesse saido do raio. Quem sabe quais feixes sao desta cena e o servidor: o raio dela e os pedacos
		// que o corte fez dele (`NascidoDoCorte`).
		// ======================================================================================================
		HashSet<int> desta = srv.PedacosDoRaioDaFoto(cli.LocalId);
		var feixes = new List<(int Id, Vector2 Cabeca)>();
		foreach ((int id, Jandirus.Core.Combat.ArteDeKi _, Jandirus.Core.Combat.TipoDeProjetil tipo, Vector2 onde, float _) in mundo.TirosDesenhados())
			if (tipo == Jandirus.Core.Combat.TipoDeProjetil.Beam && desta.Contains(id)) feixes.Add((id, onde));

		Fotografar("user://raio-4-corte.png",
				   $"CENA D: o tronco cortado por quem pisou nele ({feixes.Count} feixe(s) na tela)",
				   NaTela(mundo, _pisou));

		(bool vivoDepois, Vec2 cabecaDeCa, _, double andouDepois) = srv.RaioDaFoto(cli.LocalId);
		Conferir(vivoDepois && andouDepois < _andouAntesDoCorte - 3,
				 $"no servidor a cabeca do feixe de ca RECUOU ate quem pisou ({_andouAntesDoCorte:0.0} -> {andouDepois:0.0} tiles)");
		Conferir(feixes.Count == 2, $"na TELA ha DOIS feixes desenhados em volta de quem pisou, depois do corte ({feixes.Count})");

		(int _, Vector2 cabecaDesenhada) = feixes.Find(f => f.Id == _raioDaCenaD);
		float naFrente = (corpo - cabecaDesenhada).Dot(rumo);
		Conferir(feixes.Exists(f => f.Id == _raioDaCenaD)
				 && Mathf.Abs(naFrente - Jandirus.Core.Combat.Feixe.DistanciaDeContato) < 4f,
				 $"...a cabeca NOVA do feixe de ca esta desenhada NA FRENTE de quem pisou ({naFrente:0.0} px; servidor em {cabecaDeCa})");
		Conferir(feixes.Exists(f => f.Id != _raioDaCenaD && (f.Cabeca - corpo).Dot(rumo) > 2 * T),
				 "...e a parte de LA esta desenhada adiante, seguindo viagem");

		srv.LimparAFoto();
		Virar(10);
	}

	// =====================================================================
	// 10) CENA E: a marca no chao nasce debaixo da cabeca QUE SE VE (dono, 2026-10-08)
	// =====================================================================
	/// <summary>
	/// O SEGUNDO VOO DE CADA CENA NOVA E O DO DEFEITO INJETADO (`ProjetilDesenhado.AnuncioSemEsperaDeTeste`):
	/// o mesmo corredor, o mesmo tiro, a mesma regua -- e ela tem que reprovar.
	/// </summary>
	private bool _comDefeito;

	/// <summary>Quantos tiles o raio da cena E estica: o corredor seco em que as cenas C e D ja voam.</summary>
	private const double TilesDoSulco = 10;

	/// <summary>Com menos marcas medidas que isto a cena E nao afirma nada.</summary>
	private const int MarcasQueBastam = 4;

	/// <summary>Quanto um anuncio pode aparecer adiantado, em px: o meio pixel da regra e outro de arredondamento.</summary>
	private const float FolgaDoAnuncio = 1f;

	private int _raioDaCenaE, _fotosDoSulco;
	private double _voouAte = -1;
	private readonly HashSet<ulong> _marcasVistas = [];
	private int _marcasMedidas;

	/// <summary>Quanto a marca mais adiantada nasceu ADIANTE da cabeca desenhada, em px pelo eixo do tiro. Negativo e atras.</summary>
	private float _maiorAvanco;

	/// <summary>A marca cuja foto se quer (o quadro em que ela nasceu), e a pior ja fotografada deste voo.</summary>
	private (float Avanco, Vector2 Cabeca)? _fotoDaMarca;
	private (Image Foto, Vector2 Centro, float Avanco)? _piorMarca;

	/// <summary>
	/// O desenho do raio anda atras do servidor, e o sulco e carimbado debaixo da cabeca do SERVIDOR: plantada
	/// na chegada do pacote, a terra revirada aparecia A FRENTE da ponta enquanto o raio esticava (a foto do
	/// dono). Agora a marca espera a cabeca desenhada -- ver `ProjetilDesenhado.AnuncioSemEsperaDeTeste`.
	///
	/// ============================ A REGUA E DO QUADRO EM QUE A MARCA NASCE ============================
	/// Pra cada marca nova no corredor do raio, mede-se no MESMO quadro (ver <see cref="FimDoQuadro"/>) quanto
	/// ela esta adiante da cabeca desenhada, pelo eixo do tiro. O servidor a carimbou com a cabeca dele EM
	/// CIMA dela; o certo na tela e zero ou menos, e qualquer coisa acima disso e chao revirado onde o raio
	/// ainda nao chegou.
	///
	/// O RAIO E O DAS OUTRAS CENAS (`RaioDeFoto`, pelo `Disparar` de producao) e o corredor e o SECO, o oposto
	/// do lago: e o que as cenas C e D ja provaram que tem chao livre.
	/// ================================================================================================
	/// </summary>
	private void CenaE_Plantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.6) return;   // o feixe de antes some do snapshot, e a fila de marcas dele esvazia
		if (mundo.PosicaoLocal is null) { Nota("cena E: sem corpo local"); Fechar(); return; }

		ProjetilDesenhado.AnuncioSemEsperaDeTeste = _comDefeito;
		dec_limpar();
		_rastro?.Limpar();   // o rastro de posicao da cena C nao e desta foto

		// OS BONECOS DAS CENAS C E D CONTINUAM CONGELADOS NA TELA: o `LimparAFoto` os tira das listas do servidor
		// sem avisar a zona, e o cliente nunca os recolhe. O da cena D fica a 4 tiles da mao, no MEIO deste
		// corredor -- nas fotos das cenas E e F ele aparecia parado no caminho do tiro, e a bola parecia sumir em
		// cima dele. Eles saem da foto por aqui, do lado do cliente: no servidor ja nao existem.
		foreach (int id in (int[])[_vitima, _pisou])
			if (mundo.CorpoDeTeste(id) is { } fantasma) fantasma.Visible = false;

		_marcasVistas.Clear();
		_marcasMedidas = 0;
		_maiorAvanco = float.NegativeInfinity;
		_fotoDaMarca = null;
		_piorMarca = null;
		_voouAte = -1;

		Vector2 rumo = -_rumoDaAgua;
		_raioDaCenaE = srv.RaioDeFoto(cli.LocalId, new Vec2(rumo.X, rumo.Y), alcanceTiles: TilesDoSulco, baseDano: 0.001);
		Conferir(_raioDaCenaE != 0,
			$"o raio da cena E saiu pelo `Disparar` de producao{(_comDefeito ? " (segundo voo: com o defeito injetado)" : "")}");
		if (_raioDaCenaE == 0) { Fechar(); return; }
		Virar(11);
	}

	/// <summary>
	/// A REGUA DA CENA E, no fim de cada quadro do voo: toda marca que acabou de nascer, contra a cabeca
	/// desenhada NESTE quadro.
	/// </summary>
	private void MedirOSulco(World mundo)
	{
		// A FOTO PEDIDA NO QUADRO PASSADO sai agora: `GetImage` devolve o ULTIMO quadro renderizado, e o
		// ultimo e justamente aquele em que a marca nasceu. So a PIOR de cada voo fica.
		if (_fotoDaMarca is { } pedida)
		{
			_fotoDaMarca = null;
			if ((_piorMarca is not { } pior || pedida.Avanco > pior.Avanco) && Tela() is { } foto)
				_piorMarca = (foto, NaTela(pedida.Cabeca), pedida.Avanco);
		}

		if (Decalques.Instancia is not { } dec) return;

		Vector2? cabeca = null;
		foreach ((int id, Jandirus.Core.Combat.ArteDeKi _, Jandirus.Core.Combat.TipoDeProjetil _, Vector2 onde, float _) in mundo.TirosDesenhados())
			if (id == _raioDaCenaE) { cabeca = onde; break; }

		Vector2 rumo = -_rumoDaAgua;
		for (int i = 0; i < dec.GetChildCount(); i++)
		{
			if (dec.GetChild(i) is not AnimatedSprite2D a
				|| !a.Animation.ToString().StartsWith("crater", StringComparison.Ordinal)
				|| !_marcasVistas.Add(a.GetInstanceId())) continue;

			// SEM RAIO NA TELA NAO HA CABECA COM QUE COMPARAR (as ultimas marcas, soltas quando ele e recolhido).
			// E a marca fora do corredor e de outro: o berco e a Terra de verdade, e um arremesso alheio risca o chao.
			if (cabeca is not { } c) continue;
			Vector2 daCabeca = a.Position - c;
			if (Mathf.Abs(daCabeca.Cross(rumo)) > ZoneCollision.TileSize / 2f) continue;

			float avanco = daCabeca.Dot(rumo);
			_marcasMedidas++;
			_maiorAvanco = Mathf.Max(_maiorAvanco, avanco);

			// AS DUAS PRIMEIRAS NAO SE FOTOGRAFAM: nascem junto da mao, com o raio ainda sem tronco -- uma bola e
			// outra bola, e nao a ponta de um feixe esticando. E fora da tela nao ha foto.
			if (_marcasMedidas >= 3 && CabeNaFoto(c) && (_fotoDaMarca is not { } f || avanco > f.Avanco))
				_fotoDaMarca = (avanco, c);
		}
	}

	private void CenaE_Voar(Jandirus.Server.GameServer srv, GameClient cli)
	{
		(bool vivo, _, _, double andou) = srv.RaioDaFoto(cli.LocalId);

		// O VOO ACABA quando a cabeca chega ao alcance (ou morre antes, num muro). E a cena espera mais um pouco:
		// a ultima marca ainda esta na fila ate a cabeca DESENHADA chegar nela, e a foto pedida sai um quadro depois.
		if (vivo && andou < TilesDoSulco - 0.25 && _t < 6) return;
		if (_voouAte < 0) _voouAte = _t;
		if (_t - _voouAte < 0.4 || _fotoDaMarca != null) return;

		bool defeito = _comDefeito;
		string medida = $"{_marcasMedidas} marcas; a mais adiantada nasceu a {_maiorAvanco:0.0} px da cabeca desenhada, e negativo e ATRAS dela";
		try
		{
			if (defeito)
				Conferir(_marcasMedidas >= MarcasQueBastam && _maiorAvanco > FolgaDoAnuncio,
					$"(defeito injetado: o sulco plantado na CHEGADA do pacote) a mesma regua REPROVA ({medida})");
			else
				Conferir(_marcasMedidas >= MarcasQueBastam && _maiorAvanco <= FolgaDoAnuncio,
					$"CENA E: nenhuma marca do raio nasce A FRENTE da cabeca que se ve ({medida})");
		}
		finally { ProjetilDesenhado.AnuncioSemEsperaDeTeste = false; }

		if (_piorMarca is { } pior)
		{
			Guardar(pior.Foto, defeito ? "user://raio-5b-sulco-com-defeito.png" : "user://raio-5a-sulco.png",
					$"CENA E{(defeito ? " (DEFEITO INJETADO)" : "")}: o quadro em que a marca mais adiantada nasceu ({pior.Avanco:0.0} px da cabeca)",
					pior.Centro);
			_fotosDoSulco++;
		}

		srv.LimparAFoto();
		if (!defeito) { _comDefeito = true; Virar(10); return; }

		_comDefeito = false;
		// A TIRA: a esquerda a regra, a direita o defeito -- os dois em volta da cabeca do raio.
		if (_fotosDoSulco == 2) Montar("user://raio-5-sulco-dois.png", desde: _quadros.Count - 2, lado: 384, escala: 2);
		Virar(12);
	}

	// =====================================================================
	// 12) CENA F: a bola chega ao ponto em que estoura (dono, 2026-10-08)
	// =====================================================================
	private int _alvoDaBola, _bolaDaCenaF, _fotosDaBola;
	private readonly HashSet<ulong> _estourosVistos = [];

	/// <summary>Onde a bola foi desenhada pela ultima vez, e a que distancia disso o estouro nasceu (nulo = ainda nao nasceu).</summary>
	private Vector2? _ultimaBola;
	private float? _faltouPraBola;
	private Vector2 _ondeEstourou;

	/// <summary>Os dois quadros seguidos da cena: o ultimo com a bola e o primeiro com o estouro.</summary>
	private Image? _quadroDaBola, _quadroDoEstouro;
	private bool _fotoDoEstouroPendente;

	/// <summary>
	/// Em que quadro o relato do golpe chegou e em que quadro o estouro nasceu -- a nota do fim da cena. O
	/// tempo e o de JOGO no COMECO de cada um dos dois quadros (a soma dos `delta`), e nao o relogio de
	/// parede: o primeiro estouro do processo engasga o quadro em que nasce (material, particulas), e esse
	/// engasgo entraria na conta como se fosse espera.
	/// </summary>
	private long _quadroDoGolpe = -1, _quadroDoEstouroNascido = -1;
	private double _tempoDeJogo, _tempoDoGolpe, _tempoDoEstouroNascido;

	/// <summary>
	/// A outra metade do mesmo defeito: o pacote de morte traz o ponto de verdade em que a bola acabou, e o
	/// node ainda estava desenhado quase um tile antes dele -- a bola sumia no ar e o estouro nascia adiante.
	/// Agora ela voa o ultimo trecho e so entao e recolhida (`ProjetilDesenhado.VoarAteOFim`).
	///
	/// A REGUA: a distancia entre a ULTIMA posicao em que a bola foi desenhada e o ponto em que o node do
	/// estouro nasceu, as duas lidas no fim do quadro (ver <see cref="FimDoQuadro"/>). Zero e a bola chegando.
	/// As duas fotos sao esses dois quadros seguidos.
	///
	/// A BOLA E A COMUM (`BolaDeFoto`, 16 tiles por segundo) e o alvo e um corpo parado no corredor seco. Ele
	/// fica DENTRO da tela: o zoom mostra 6,6 tiles pros lados e 3,7 pra cima e pra baixo.
	/// </summary>
	private void CenaF_Plantar(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		if (_t < 0.6) return;   // o que a cena de antes deixou no ar some do snapshot
		if (mundo.PosicaoLocal is null) { Nota("cena F: sem corpo local"); Fechar(); return; }

		Vector2 rumo = -_rumoDaAgua;
		if (_alvoDaBola == 0)
		{
			int tiles = Mathf.Abs(rumo.X) > 0.5f ? 5 : 3;
			_alvoDaBola = srv.ForjarCorpoDeFoto(
				cli.LocalId, new Vec2(rumo.X * tiles * ZoneCollision.TileSize, rumo.Y * tiles * ZoneCollision.TileSize),
				"Foto: o alvo da bola", 200_000, comEscada: false);
			Conferir(_alvoDaBola != 0, $"o corpo da cena F entrou no mundo, a {tiles} tiles da mao");
			if (_alvoDaBola == 0) { Fechar(); return; }
			_t = 0;   // e mais uma espera, pro sprite dele nascer na tela
			return;
		}
		if (mundo.CorpoDeTeste(_alvoDaBola) == null)
		{
			if (_t > 5) { Conferir(false, "o alvo da cena F tem SPRITE na tela"); Fechar(); }
			return;
		}

		ProjetilDesenhado.AnuncioSemEsperaDeTeste = _comDefeito;
		dec_limpar();   // a fileira de sulcos da cena E sai da foto: a bola voa sobre grama limpa
		_ultimaBola = null;
		_faltouPraBola = null;
		_quadroDaBola = _quadroDoEstouro = null;
		_fotoDoEstouroPendente = false;
		_quadroDoGolpe = _quadroDoEstouroNascido = -1;
		// OS ESTOUROS QUE JA ESTAVAM NA TELA nao sao desta bola.
		foreach ((ulong no, Vector2 _) in mundo.EstourosDeKiDesenhados()) _estourosVistos.Add(no);

		_bolaDaCenaF = srv.BolaDeFoto(cli.LocalId, new Vec2(rumo.X, rumo.Y), alcanceTiles: 12, baseDano: 0.002);
		Conferir(_bolaDaCenaF != 0,
			$"a bola da cena F saiu pelo `Disparar` de producao{(_comDefeito ? " (segundo voo: com o defeito injetado)" : "")}");
		if (_bolaDaCenaF == 0) { Fechar(); return; }
		Virar(13);
	}

	/// <summary>
	/// A REGUA DA CENA F, no fim de cada quadro: onde a bola esta desenhada e, no quadro em que o estouro
	/// nasce, a que distancia ele nasceu do ultimo lugar em que ela foi vista.
	/// </summary>
	private void MedirABola(World mundo)
	{
		// O QUADRO DO ESTOURO sai um quadro depois de ele nascer, pelo motivo de sempre (`GetImage` e o anterior).
		if (_fotoDoEstouroPendente)
		{
			_fotoDoEstouroPendente = false;
			_quadroDoEstouro = Tela();
		}
		if (_faltouPraBola != null) return;

		foreach ((int id, Jandirus.Core.Combat.ArteDeKi _, Jandirus.Core.Combat.TipoDeProjetil _, Vector2 onde, float _) in mundo.TirosDesenhados())
			if (id == _bolaDaCenaF) { _ultimaBola = onde; break; }

		foreach ((ulong no, Vector2 onde) in mundo.EstourosDeKiDesenhados())
		{
			if (!_estourosVistos.Add(no)) continue;

			// SO O ESTOURO EM CIMA DO ALVO DESTA CENA: o berco e a Terra de verdade, e tiro alheio tambem estoura.
			if (_ultimaBola is not { } bola || mundo.PosicaoDesenhadaDe(_alvoDaBola) is not { } alvo
				|| onde.DistanceTo(alvo) > 2 * ZoneCollision.TileSize) continue;

			_faltouPraBola = bola.DistanceTo(onde);
			_ondeEstourou = onde;
			_quadroDoEstouroNascido = (long)Engine.GetProcessFrames();
			_tempoDoEstouroNascido = _tempoDeJogo - GetProcessDeltaTime();   // o comeco DESTE quadro
			_quadroDaBola = Tela();   // o ULTIMO quadro renderizado: o ultimo em que a bola foi desenhada
			_fotoDoEstouroPendente = true;
			break;
		}
	}

	/// <summary>O RELATO DO GOLPE DA BOLA chegou neste quadro -- so pra nota do fim da cena F (o clarao em quem apanha sai dele).</summary>
	private void AoGolpeNoAlvoDaBola(Protocol.HitEvent h)
	{
		if (_passo != 13 || !h.TemPonto || h.Alvo != _alvoDaBola || _quadroDoGolpe >= 0) return;
		_quadroDoGolpe = (long)Engine.GetProcessFrames();
		_tempoDoGolpe = _tempoDeJogo;   // a rede e lida antes do fim do quadro: a soma ainda e a do comeco deste
	}

	private void CenaF_Voar(Jandirus.Server.GameServer srv)
	{
		// A REGUA FECHA SOZINHA (o estouro nasceu e as duas fotos sairam) -- ou o prazo vence e ela reprova.
		if ((_faltouPraBola == null || _fotoDoEstouroPendente) && _t < 5) return;

		bool defeito = _comDefeito;
		string medida = _faltouPraBola is { } falta
			? $"a bola foi desenhada pela ultima vez a {falta:0.0} px de onde o estouro nasceu"
			: "nenhum estouro nasceu em cima do alvo";
		try
		{
			if (defeito)
				Conferir(_faltouPraBola is > FolgaDoAnuncio,
					$"(defeito injetado: a bola recolhida na CHEGADA do pacote de morte) a mesma regua REPROVA ({medida})");
			else
				Conferir(_faltouPraBola is <= FolgaDoAnuncio, $"CENA F: a bola CHEGA ao ponto em que estoura ({medida})");
		}
		finally { ProjetilDesenhado.AnuncioSemEsperaDeTeste = false; }

		// O QUE O CONSERTO NAO MEXEU, dito em numero: o relato do golpe (o clarao e a faisca em quem apanha) continua
		// saindo na chegada do pacote. Com a bola voando o ultimo trecho, ele passa a vir ANTES do estouro.
		if (_quadroDoGolpe >= 0 && _quadroDoEstouroNascido >= 0)
			Nota($"cena F{(defeito ? " (defeito injetado)" : "")}: o relato do golpe chegou "
				 + $"{_quadroDoEstouroNascido - _quadroDoGolpe} quadro(s) ({(_tempoDoEstouroNascido - _tempoDoGolpe) * 1000:0} ms de jogo) "
				 + "antes de o estouro nascer");

		if (_quadroDaBola != null && _quadroDoEstouro != null)
		{
			string qual = defeito ? " (DEFEITO INJETADO)" : "";
			Vector2 centro = NaTela(_ondeEstourou);
			Guardar(_quadroDaBola, defeito ? "user://raio-6c-bola-com-defeito.png" : "user://raio-6a-bola.png",
					$"CENA F{qual}: o ULTIMO quadro com a bola", centro);
			Guardar(_quadroDoEstouro, defeito ? "user://raio-6d-estouro-com-defeito.png" : "user://raio-6b-estouro.png",
					$"CENA F{qual}: o quadro seguinte, com o estouro", centro);
			_fotosDaBola += 2;
		}

		if (!defeito) { _comDefeito = true; Virar(12); return; }

		_comDefeito = false;
		// A TIRA: bola e estouro com a regra, bola e estouro com o defeito -- os quatro em volta do ponto do estouro.
		if (_fotosDaBola == 4) Montar("user://raio-6-bola-quatro.png", desde: _quadros.Count - 4, lado: 320, escala: 2);
		srv.LimparAFoto();
		Fechar();
	}

	/// <summary>O que a regua do fim do quadro mede em cada passo. Ver <see cref="FimDoQuadro"/>.</summary>
	private void NoFimDoQuadro()
	{
		_tempoDeJogo += GetProcessDeltaTime();
		if (_acabou || World.Instancia is not { } mundo) return;
		switch (_passo)
		{
			case 11: MedirOSulco(mundo); break;
			case 13: MedirABola(mundo); break;
		}
	}

	// =====================================================================
	// O CHAO, LIDO DOS NODES
	// =====================================================================
	/// <summary>
	/// QUANTAS MARCAS DE CADA TIPO O CHAO TEM, E EM QUE ESPECIE DE CELULA ELAS CAIRAM.
	///
	/// Le do NODE (nome da animacao e posicao) e cruza com o `World.EhAgua` -- o MESMO mapa com que o
	/// tilemap foi desenhado. Ler "quantos pacotes chegaram" mediria o fio, que a `--projetilteste` ja
	/// mede; o que so daqui se responde e se a marca caiu na especie certa de chao.
	///
	/// O sulco e um `AnimatedSprite2D` com animacao `crater*` (a folha `craterseries`); a onda e o
	/// mesmo tipo de node com animacao `ns` ou `ew` (a folha `KiWater`).
	/// </summary>
	private static void LerOChao(World mundo, out int sulcosNoSeco, out int sulcosNaAgua, out int ondas)
	{
		sulcosNoSeco = sulcosNaAgua = ondas = 0;
		if (Decalques.Instancia is not { } dec) return;

		const int T = ZoneCollision.TileSize;
		for (int i = 0; i < dec.GetChildCount(); i++)
		{
			if (dec.GetChild(i) is not AnimatedSprite2D a) continue;
			string nome = a.Animation.ToString();
			var celula = new Vector2I((int)Mathf.Floor(a.Position.X / T), (int)Mathf.Floor(a.Position.Y / T));
			bool molhada = mundo.EhAgua(celula);

			if (nome is "ns" or "ew") { if (molhada) ondas++; }
			else if (nome.StartsWith("crater", StringComparison.Ordinal))
			{
				if (molhada) sulcosNaAgua++;
				else sulcosNoSeco++;
			}
		}
	}

	private static void dec_limpar() => Decalques.Instancia?.Limpar();

	/// <summary>
	/// UM RUMO EM QUE HA MARGEM DE LAGO A FRENTE: a celula do corpo e a primeira a frente SECAS (pra haver
	/// sulco) e alguma celula molhada entre a segunda e a decima (pra haver onda).
	///
	/// ============================ O BERCO DEIXA UMA CELULA SECA SO (2026-09-23) ============================
	/// O `--aguateste` poe o corpo UM TILE ANTES da margem (`PorNaBeiraDoLago`): a celula dele e seca, a
	/// seguinte e a margem seca, e a agua comeca na segunda. Esta busca pedia DUAS secas a frente, e isso nunca
	/// casou com o rumo do berco -- ela so passava por sorte do terreno, achando outro lago noutro rumo. Quando o
	/// nascimento foi pro ponto do BYOND (`MarcoDeBerco`), o lago mais perto mudou e a sorte acabou: "achei uma
	/// margem" reprovou sem nada no raio ter mudado. O sulco nasce na celula da CABECA (`MarcarSulcoDoTiro`), e
	/// a cabeca sai da mao, dentro da celula do corpo: uma seca a frente ja da o risco no chao que a foto mede.
	/// ======================================================================================================
	///
	/// A pergunta e feita ao mapa do CLIENTE, que e o mesmo com que ele desenha -- e por isso a foto
	/// mostra o que a conta disse. O servidor tem a versao dele (`EhAguaDeFoto`) e ela e usada pra
	/// saber quando a CABECA entrou na agua; as duas existem de proposito.
	/// </summary>
	private static bool AcharAMargem(World mundo, Vector2 eu, out Vector2 rumo)
	{
		const int T = ZoneCollision.TileSize;
		var minha = new Vector2I((int)Mathf.Floor(eu.X / T), (int)Mathf.Floor(eu.Y / T));

		foreach (Vector2I d in (Vector2I[])[new(1, 0), new(-1, 0), new(0, 1), new(0, -1)])
		{
			bool secoPerto = !mundo.EhAgua(minha) && !mundo.EhAgua(minha + d);
			if (!secoPerto) continue;

			for (int k = 2; k <= 10; k++)
			{
				// O CAMINHO TEM QUE ESTAR LIVRE ATE LA. Agua nao bloqueia (ela e a terceira classe
				// de celula), mas pedra e arvore bloqueiam -- e um feixe que morre numa pedra a tres
				// tiles nunca chega no lago. A primeira rodada nao perguntava isto, e a foto saia com
				// o rastro certo e onda nenhuma.
				if (Bloqueado(mundo, minha + d * k)) break;

				if (mundo.EhAgua(minha + d * k))
				{
					rumo = new Vector2(d.X, d.Y);
					return true;
				}
			}
		}

		rumo = Vector2.Right;
		return false;
	}

	/// <summary>Cenario que para o tiro -- lido do MESMO mapa de colisao com que o cliente anda.</summary>
	private static bool Bloqueado(World mundo, Vector2I celula)
		=> mundo.Colisao is { } mapa && mapa.BlockedCell(celula.X, celula.Y);

	// =====================================================================
	// A FOTO
	// =====================================================================
	/// <summary>Os quadros ja tirados, com o ponto da tela em que a cena estava. Ver <see cref="Montar"/>.</summary>
	private readonly List<(string Nome, Image Foto, Vector2 Centro)> _quadros = [];

	private void Fotografar(string destino, string rotulo, Vector2? centro = null)
	{
		if (Tela() is not { } img) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return; }
		Guardar(img, destino, rotulo, centro ?? new Vector2(img.GetWidth() / 2f, img.GetHeight() / 2f));
	}

	/// <summary>O ULTIMO QUADRO RENDERIZADO, ou nulo sem janela. E o quadro ANTERIOR ao que esta sendo processado.</summary>
	private Image? Tela() => GetViewport()?.GetTexture()?.GetImage() is { } img && !img.IsEmpty() ? img : null;

	/// <summary>Grava a foto e a guarda pras tiras (<see cref="Montar"/>), com o ponto da tela em que a cena estava.</summary>
	private void Guardar(Image img, string destino, string rotulo, Vector2 centro)
	{
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho}");
			_quadros.Add((destino, img, centro));
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	/// <summary>ONDE, NA TELA, ESTE CORPO ESTA -- a posicao do mundo passada pela camera.</summary>
	private Vector2 NaTela(World mundo, int id)
		=> mundo.PosicaoDesenhadaDe(id) is { } p ? NaTela(p) : Vector2.Zero;

	/// <summary>Onde, na tela, este ponto do mundo esta.</summary>
	private Vector2 NaTela(Vector2 ponto) => (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * ponto;

	/// <summary>Este ponto do mundo aparece na foto, com folga pra um recorte em volta dele?</summary>
	private bool CabeNaFoto(Vector2 ponto)
	{
		const float Margem = 96f;
		Vector2 naTela = NaTela(ponto);
		Vector2 tela = GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero;
		return naTela.X > Margem && naTela.Y > Margem && naTela.X < tela.X - Margem && naTela.Y < tela.Y - Margem;
	}

	/// <summary>
	/// A TIRA DE TRES QUADROS, COLADA -- e ela e o formato do pedido, nao enfeite.
	///
	/// O dono pediu a cena "em tres quadros". Tres arquivos separados de 1920x1080, cada um com a
	/// acao ocupando um centesimo da area, obrigam quem le a abrir tres janelas, achar o ponto certo
	/// em cada uma e comparar de cabeca. A tira poe os tres lado a lado, recortados no MESMO tamanho
	/// e em volta do MESMO corpo -- que e o unico jeito de a comparacao ser feita pelo olho.
	///
	/// Ela nao substitui os originais: os tres 1920x1080 continuam salvos, porque um recorte e sempre
	/// uma escolha de quem recortou.
	/// </summary>
	private void Montar(string destino, int desde, int lado, int escala)
	{
		var tira = _quadros.GetRange(desde, _quadros.Count - desde);
		if (tira.Count == 0) return;

		const int Vao = 6;
		int largura = (lado * tira.Count + Vao * (tira.Count - 1)) * escala;
		Image colagem = Image.CreateEmpty(largura, lado * escala, false, Image.Format.Rgba8);
		colagem.Fill(new Color(0.06f, 0.06f, 0.06f));

		for (int i = 0; i < tira.Count; i++)
		{
			(_, Image foto, Vector2 centro) = tira[i];
			// A JANELA E EMPURRADA PRA DENTRO, e nao cortada. Cortar (`Intersection`) devolve pedacos de
			// tamanhos diferentes quando o corpo esta perto da borda da tela -- e ai os tres quadros da
			// tira deixam de ser comparaveis, que e a unica coisa que a tira existe pra permitir.
			int x0 = Math.Clamp((int)centro.X - lado / 2, 0, Math.Max(0, foto.GetWidth() - lado));
			int y0 = Math.Clamp((int)centro.Y - lado / 2, 0, Math.Max(0, foto.GetHeight() - lado));
			var r = new Rect2I(x0, y0, lado, lado)
				.Intersection(new Rect2I(0, 0, foto.GetWidth(), foto.GetHeight()));
			if (r.Size.X < 16 || r.Size.Y < 16) continue;

			Image pedaco = foto.GetRegion(r);

			// O `BlitRect` EXIGE O MESMO FORMATO nos dois lados, e cala quando nao tem: a primeira
			// tira saiu um retangulo PRETO inteiro, sem erro nenhum no log. A foto vem do viewport
			// (RGB8 ou RGBAH, depende do renderizador); a colagem e RGBA8. Converte-se o pedaco.
			pedaco.Convert(Image.Format.Rgba8);
			pedaco.Resize(pedaco.GetWidth() * escala, pedaco.GetHeight() * escala, Image.Interpolation.Nearest);
			colagem.BlitRect(pedaco, new Rect2I(Vector2I.Zero, pedaco.GetSize()),
							 new Vector2I(i * (lado + Vao) * escala, 0));
		}

		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			colagem.SavePng(caminho);
			_passos.Add($"  ok     a tira dos {tira.Count} quadros, colada: {caminho}");
		}
		catch (Exception e) { Nota($"tira: {e.Message}"); }
	}

	private void Fechar()
	{
		_acabou = true;
		_rastro?.Desligar();
		S?.LimparAFoto();
		GD.Print("\n[raio] ===== AS TRES FOTOS DOS TRES PEDIDOS =====");
		foreach (string l in _passos) GD.Print("[raio] " + l);
		GD.Print(_falhas.Count == 0
			? "[raio] ===== TUDO OK ====="
			: $"[raio] ===== {_falhas.Count} FALHA(S) =====\n[raio]   " + string.Join("\n[raio]   ", _falhas));
		GetTree().Quit();
	}
}

/// <summary>
/// O FIM DO QUADRO: um node que roda DEPOIS de todos os outros (`ProcessPriority` alto) e avisa.
///
/// ============================ POR QUE A REGUA NAO MORA NO `_Process` DO ROBO ============================
/// As cenas E e F comparam duas coisas no quadro em que uma delas NASCE -- a marca com a cabeca do raio,
/// o estouro com a ultima posicao da bola. O robo e filho do `Boot` e roda ANTES do `World` e dos tiros:
/// no quadro em que um pacote chega (o `GameClient` e autoload, le a rede primeiro) ele veria o efeito
/// ja plantado e a cabeca ainda na posicao do quadro ANTERIOR -- um passo de `Lerp` de erro a favor do
/// defeito, e um passo desses (8 a 13 px) e do tamanho do que se mede. Lido no fim do quadro, o par e
/// o que a tela vai mostrar.
/// =======================================================================================================
/// </summary>
public partial class FimDoQuadro : Node
{
	public Action? Agora;

	public FimDoQuadro() => ProcessPriority = 1000;

	public override void _Process(double delta) => Agora?.Invoke();
}

/// <summary>
/// O RASTRO DE POSICAO DESENHADO NA TELA -- um ponto por quadro, no lugar em que o corpo FOI
/// DESENHADO.
///
/// ============================ POR QUE ELE E DESENHADO, E NAO SO CONTADO ============================
/// O numero (`Espalhamento`) ja bastaria pra a checagem. A foto e o pedido do dono, e uma foto de um
/// NPC parado nao se distingue de uma foto de um NPC que nao existe -- os pontos sao o que faz a
/// imagem AFIRMAR alguma coisa: se estao empilhados num borrao so, ele nao saiu do lugar durante
/// aqueles dois segundos; se viram uma fileira, ele andou.
///
/// `ZAsRelative = false` e o que tira este node da camada do pai -- sem isso ele herdaria a ordem do
/// mundo e ficaria atras do cenario, que e o mesmo detalhe que a fumaca do `Decalques` ja tinha
/// ensinado.
/// ================================================================================================
/// </summary>
public partial class RastroDePosicao : Node2D
{
	private readonly List<Vector2> _pontos = [];

	public int Pontos => _pontos.Count;

	/// <summary>A maior distancia entre dois pontos do rastro. Zero = o corpo nao saiu do lugar.</summary>
	public float Espalhamento
	{
		get
		{
			float maior = 0;
			for (int i = 1; i < _pontos.Count; i++)
				maior = Mathf.Max(maior, _pontos[i].DistanceTo(_pontos[0]));
			return maior;
		}
	}

	public static RastroDePosicao Pendurar(World mundo)
	{
		var r = new RastroDePosicao { Name = "RastroDePosicao", ZIndex = 300, ZAsRelative = false };
		mundo.AddChild(r);
		return r;
	}

	public void Marcar(Vector2? onde)
	{
		if (onde is not { } p) return;
		_pontos.Add(p);
		QueueRedraw();
	}

	public void Limpar() { _pontos.Clear(); QueueRedraw(); }

	public void Desligar() { if (IsInstanceValid(this)) QueueFree(); }

	public override void _Draw()
	{
		if (_pontos.Count == 0) return;

		// A LINHA primeiro (pra os pontos ficarem por cima) e so quando ha o que ligar.
		if (_pontos.Count > 1) DrawPolyline([.. _pontos], new Color(1f, 0.35f, 0.1f, 0.55f), 2f);

		foreach (Vector2 p in _pontos) DrawCircle(p, 2f, new Color(1f, 0.85f, 0.1f, 0.85f));

		// O PRIMEIRO E O ULTIMO marcados por fora: e a leitura que a foto tem que permitir de longe.
		//
		// O ANEL E LARGO DE PROPOSITO (20 px, o corpo tem 32): ele tem que EMOLDURAR o boneco, e nao
		// tapa-lo. Com 9 px ele caia em cima do rosto do NPC -- e a foto existe pra mostrar o NPC.
		DrawArc(_pontos[0], 20f, 0, Mathf.Tau, 32, new Color(0.2f, 1f, 0.3f, 0.9f), 2f);
		DrawArc(_pontos[^1], 20f, 0, Mathf.Tau, 32, new Color(1f, 0.2f, 0.2f, 0.9f), 2f);
	}
}
