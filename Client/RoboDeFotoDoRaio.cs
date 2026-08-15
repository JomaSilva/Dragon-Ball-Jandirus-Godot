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
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

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
		_rastro?.Marcar(mundo.PosicaoDesenhadaDe(_npcDaCena));

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

		int id = srv.RaioDeFoto(cli.LocalId, new Vec2(rumo.X, rumo.Y), alcanceTiles: 20, baseDano: 0.002);
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
					Conferir(false, $"o feixe da cena C PEGOU o corpo (arrastando={arrastando}, vivo={vivo})");
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

		// A TIRA DA CENA C -- os tres quadros do arrasto, colados na mesma ordem em que sairam.
		Montar("user://raio-3-levado-tres.png", desde: _quadros.Count - 3, lado: 288, escala: 2);

		srv.LimparAFoto();
		Fechar();
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
	/// UM RUMO EM QUE HA MARGEM DE LAGO A FRENTE: as duas primeiras celulas SECAS (pra haver sulco) e
	/// alguma celula molhada entre a terceira e a decima (pra haver onda).
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
			bool secoPerto = !mundo.EhAgua(minha + d) && !mundo.EhAgua(minha + d * 2);
			if (!secoPerto) continue;

			for (int k = 3; k <= 10; k++)
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
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho}");
			_quadros.Add((destino, img, centro ?? new Vector2(img.GetWidth() / 2f, img.GetHeight() / 2f)));
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); }
	}

	/// <summary>ONDE, NA TELA, ESTE CORPO ESTA -- a posicao do mundo passada pela camera.</summary>
	private Vector2 NaTela(World mundo, int id)
	{
		if (mundo.PosicaoDesenhadaDe(id) is not { } p) return Vector2.Zero;
		return (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * p;
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
