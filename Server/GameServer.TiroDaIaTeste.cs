using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DO TIRO DA IA (`--tiroiateste`) -- roda no BOOT, sem ninguem em jogo.
///
///     Godot --headless --path . --server --port 7961 --tiroiateste
///
/// ============================ POR QUE ELA EXISTE, SE JA HA DUAS QUE MEDEM A IA ATIRANDO ============================
/// A `--iateste` mede a DECISAO com um <c>Tiro</c> sintetico (ela mesma explica por que: registrar
/// uma tecnica de teste em `TecnicasDeLonge` valeria pro processo inteiro). A `--embatekiteste` mede
/// o ARSENAL num corpo forjado a que a bancada DEU a skill na mao (`DarSkillDoRaio`). As duas estao
/// certas no que afirmam -- e nenhuma das duas responde a pergunta que esta camada faz:
///
///     **um NPC nascido pelo caminho de producao chega a atirar em alguem?**
///
/// Entre o `Tiro` sintetico e o tiro de verdade ha uma cadeia inteira que nenhuma delas atravessa:
/// o molde do `npcs.json`, o `SorteioDeNpc` que compra as skills dele, o `NivelDasSkills` que crava
/// o degrau, o `VerbosAtivos` que traduz degrau em verb, o `SabeTecnica`, o `ArsenalDeLonge`, o
/// cerebro, o `AplicarComando` e finalmente o `UsarHabilidade`. Um elo frouxo em qualquer ponto
/// dessa fila produz o MESMO sintoma -- um NPC que nunca atira -- e nenhuma bancada fica vermelha.
///
/// Foi exatamente o que ela achou na primeira rodada. Ver a familia 1.
/// ==============================================================================================================
///
/// ============================ O QUE ELA AFIRMA ============================
///  1. QUEM ATIRA, POR MOLDE: quantos dos corpos que o `npcs.json` descreve saem do sorteio de
///     producao com arsenal, e com QUAIS verbs -- os tres degraus (25/30/35) medidos contra o
///     `nivelDasSkills` de cada molde.
///  2. DE PONTA A PONTA: um NPC de molde, alvo na frente, tique de producao -- e um projetil de
///     verdade na zona, com o Ki pago pela formula do jogador.
///  3. QUANDO ele atira: as cinco recusas do `EscolherTiro` medidas uma a uma, no tique.
///  4. O CANAL DO RAIO: o pulso abre, o relogio fecha, e o corpo fica plantado enquanto isso.
///  5. DOIS NPCs: os feixes de dois corpos dirigidos se encontram e a disputa roda sozinha, sem
///     teclado nenhum, e ela ACABA.
///  6. O CUSTO, MEDIDO: uma zona de NPCs armados contra a mesma zona de NPCs sem arsenal.
/// =========================================================================
/// </summary>
public partial class GameServer
{
	private int _tiOk, _tiFalhou;

	private void AfirmarTi(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _tiOk++; GD.Print($"[tiroia]   OK    {oque}"); return; }
		_tiFalhou++;
		GD.PrintErr($"[tiroia]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoTiroDaIa()
	{
		_tiOk = _tiFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[tiroia] ================ O TIRO DA IA ================");
		AfirmarTi("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			QuemAtiraPorMolde();
			DePontaAPonta();
			QuandoEleAtira();
			OCanalDoRaio();
			DoisNpcsSeCruzando();
			ACalibragem();
			OCustoDoArsenal();
		}
		finally { LimparEmbatesDaBancada(); }

		GD.Print($"[tiroia] ================ {_tiOk} passaram, {_tiFalhou} falharam ================");
	}

	// =====================================================================
	// 1) QUEM ATIRA, POR MOLDE
	// =====================================================================
	/// <summary>
	/// O SORTEIO DE PRODUCAO, molde por molde, e o arsenal que sai dele.
	///
	/// ============================ ISTO ACHOU O ELO FROUXO ============================
	/// Os tres verbs que voam sao concedidos por DEGRAU DE NIVEL, e nao por compra:
	/// `Basic_Ki_Effusion` 25 -> `Ki_Wave`, `Basic_Ki_Control` 30 -> `Guided_Ball`,
	/// `Ki_Unlocked` 35 -> `Basic_Blast` (`niveis.json`; ver o bloco do `SabeTecnica`).
	///
	/// O molde do NPC crava UM nivel pra TODAS as skills dele (`nivelDasSkills`) -- e o do cidadao
	/// era **20**, tres degraus abaixo do primeiro. Como o cidadao e o unico tipo que nasce hoje
	/// (o recorte do dono barra inimigo comum), o sistema inteiro de ataque de ki era invisivel em
	/// jogo: a tabela preenchida, o gancho aceso, a decisao escrita, e nenhum corpo no mundo com
	/// uma unica linha de arsenal.
	///
	/// A conferencia e por MOLDE e nao "existe algum NPC que atira", de proposito: a resposta util
	/// pra quem edita o `npcs.json` e *qual* molde atira e com o que.
	/// ============================================================================
	/// </summary>
	private void QuemAtiraPorMolde()
	{
		GD.Print("[tiroia] -- 1) QUEM ATIRA, POR MOLDE (sorteio de producao, 24 sementes cada)");

		if (_moldes == null || _racas == null || _skills == null)
		{
			AfirmarTi("os catalogos de molde/raca/skill estao carregados", false);
			return;
		}

		int moldesQueAtiram = 0;
		foreach (MoldeDeNpc molde in _moldes.Todos)
		{
			int comArsenal = 0;
			var verbos = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

			for (ulong i = 0; i < 24; i++)
			{
				ServerPlayer sorteado = CorpoDeMolde(molde, i);
				Arsenal a = ArsenalDeLonge(sorteado);
				if (!a.TemAlguma) continue;
				comArsenal++;
				for (int k = 0; k < a.Quantas; k++) verbos.Add(a[k].Id);
			}

			if (comArsenal > 0) moldesQueAtiram++;
			GD.Print($"[tiroia]     {molde.Id,-24} nivel {molde.NivelDasSkills,3:0}  "
				   + $"{comArsenal,2}/24 com arsenal  [{string.Join(", ", verbos)}]");
		}

		AfirmarTi("ALGUM molde do `npcs.json` produz um corpo que sabe atirar",
				  moldesQueAtiram > 0,
				  "nenhum dos moldes chega aos degraus 25/30/35 -- o sistema de ki nao aparece em jogo");

		// ============================ O QUE NASCE HOJE, E NAO O QUE PODERIA NASCER ============================
		// O recorte do dono (`Povoamento.PodeNascer`) barra inimigo comum. Entao a pergunta que
		// importa pro jogo de hoje nao e "algum molde atira", e sim "algum molde que PODE NASCER
		// atira" -- e essas sao respostas diferentes enquanto o recorte estiver de pe.
		// ================================================================================================
		int nascemEAtiram = 0;
		foreach (MoldeDeNpc molde in _moldes.Todos)
		{
			if (!Povoamento.PodeNascer(molde.Tipo)) continue;
			for (ulong i = 0; i < 24; i++)
				if (ArsenalDeLonge(CorpoDeMolde(molde, i)).TemAlguma) { nascemEAtiram++; break; }
		}
		AfirmarTi("...e ele NASCE hoje (o recorte do dono nao deixa o unico atirador de fora)",
				  nascemEAtiram > 0, "so moldes barrados pelo `PodeNascer` atiram");

		// ============================ O DEGRAU E A REGRA, E ELE E MEDIDO UM A UM ============================
		// A tabela acima diz o que os moldes de HOJE produzem; isto aqui diz POR QUE. Cada verb entra
		// no seu degrau e nao um antes -- e a cadeia medida e a de producao inteira
		// (`NiveisDeSkill.Por` -> `VerbosAtivos` -> `SabeTecnica` -> `ArsenalDeLonge`), a mesma que o
		// `SorteioDeNpc` percorre quando crava o `nivelDasSkills`.
		//
		// A primeira versao disto pedia o degrau a um corpo de MOLDE, e reprovou tres vezes por um
		// defeito da bancada e nao do jogo: subir o nivel de todas as skills de um cidadao nao lhe da
		// `Basic_Ki_Effusion` se ele nunca comprou essa skill. Nivel e degrau de uma skill que se TEM.
		// ================================================================================================
		foreach ((string path, int degrau, string verbo) in new[]
		{
			("/datum/skill/mind/Basic_Ki_Effusion", 25, "Ki_Wave"),
			("/datum/skill/mind/Basic_Ki_Control", 30, "Guided_Ball"),
			("/datum/skill/mind/Ki_Unlocked", 35, "Basic_Blast"),
		})
		{
			ServerPlayer antes = Forjar($"Degrau{degrau}-", CorredorLivre(4), bp: 50_000);
			antes.Livro.Dar(path);
			antes.Niveis.Por(path, degrau - 1);
			AfirmarTi($"um degrau ABAIXO ({degrau - 1}) de `{path.Split('/')[^1]}` nao da `{verbo}`",
					  !ArsenalTem(antes, verbo));

			ServerPlayer no = Forjar($"Degrau{degrau}", CorredorLivre(4), bp: 50_000);
			no.Livro.Dar(path);
			no.Niveis.Por(path, degrau);
			AfirmarTi($"...e no degrau {degrau} ele passa a ter `{verbo}`", ArsenalTem(no, verbo));
		}

		// ============================ E O QUE ELE SABE SEM DEGRAU NENHUM: O `dadas` ============================
		// O DM decide quem atira por uma FLAG do mob (`isBlaster`, NPCAI.dm:39) -- la o NPC nao aprende
		// nada, ele simplesmente atira. Aqui ele passa pelo funil do jogador, entao a flag virou a
		// skill DADA do molde. Esta afirmacao e a que liga as duas pontas: sem ela, "arrumei o
		// `npcs.json`" seria uma frase e nao uma medida.
		// =================================================================================================
		MoldeDeNpc? lutador = _moldes.Get("rival_do_mundo");
		if (lutador != null)
			AfirmarTi("o `dadas` do molde de luta entrega o RAIO -- e e ele o `isBlaster` do DM",
					  ArsenalTem(CorpoDeMolde(lutador, 3), "Ki_Wave"),
					  "sem isto o contra-feixe e a colisao de ki entre NPCs nunca acontecem em jogo");

		MoldeDeNpc? povo = _moldes.Get("cidadao");
		if (povo != null)
			AfirmarTi("...e o HABITANTE continua sem nenhum (pacifico nao ganha raio de brinde)",
					  !ArsenalDeLonge(CorpoDeMolde(povo, 3)).TemAlguma);

		LimparEmbatesDaBancada();
	}

	/// <summary>
	/// UM CORPO PELO SORTEIO DE PRODUCAO -- `SorteioDeNpc.Sortear`, o mesmo que o `NascerNpc` chama.
	///
	/// Ele NAO entra no mundo: esta familia pergunta o que o molde PRODUZ, e por um corpo por molde
	/// por semente o `PorNoMundo`/`RemoverDoMundo` seria trabalho que nao muda a resposta. As
	/// familias que precisam de corpo no mundo usam o `Forjar` das outras bancadas.
	///
	/// `nivel` maior que zero substitui o `NivelDasSkills` do molde -- e como se le o degrau de um
	/// molde hipotetico sem editar o `npcs.json`.
	/// </summary>
	private ServerPlayer CorpoDeMolde(MoldeDeNpc molde, ulong lugar, double nivel = -1)
	{
		ulong semente = SorteioDeNpc.SementeDe(SeedDoUniverso, molde.Id, lugar);
		double guardado = molde.NivelDasSkills;
		if (nivel >= 0) molde.NivelDasSkills = nivel;
		FichaSorteada s = SorteioDeNpc.Sortear(molde, semente, _racas!, _skills, "Earth", MediaDoServidor());
		molde.NivelDasSkills = guardado;

		return new ServerPlayer
		{
			Id = 0, Peer = null, Name = s.Nome, Race = s.Raca, Class = s.Classe,
			Zone = ZonaDaBancadaDeProjetil, Ficha = s.Ficha, Livro = s.Livro, Niveis = s.Niveis,
		};
	}

	private bool ArsenalTem(ServerPlayer pl, string verb)
	{
		Arsenal a = ArsenalDeLonge(pl);
		for (int i = 0; i < a.Quantas; i++)
			if (string.Equals(a[i].Id, verb, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	// =====================================================================
	// 2) DE PONTA A PONTA
	// =====================================================================
	/// <summary>
	/// UM NPC DE MOLDE, UM ALVO NA FRENTE, E O TIQUE DE PRODUCAO. Nada e chamado na mao aqui a nao
	/// ser o proprio `TickDosCorposSemDono` -- se a decisao, o atuador, o verb ou a cobranca
	/// quebrarem, nao aparece projetil nenhum na zona.
	/// </summary>
	private void DePontaAPonta()
	{
		GD.Print("[tiroia] -- 2) DE PONTA A PONTA: DO MOLDE AO PROJETIL NA ZONA");

		(ServerPlayer atirador, ServerPlayer alvo) = DuelistasDeBancada(tiles: 9);
		AfirmarTi("o atirador nasceu com arsenal (senao o resto desta familia nao mede nada)",
				  ArsenalDeLonge(atirador).TemAlguma);

		double kiAntes = atirador.Ficha.Ki;
		Projetil? tiro = AteAlguemAtirar(atirador, segundos: 12);

		AfirmarTi("o NPC atirou -- e o projetil e dele", tiro != null && tiro.Dono == atirador.Id);
		if (tiro != null)
		{
			AfirmarTi("...e o tiro saiu na direcao do alvo",
					  (alvo.Pos - atirador.Pos).Normalized().X * tiro.Rumo.X
					  + (alvo.Pos - atirador.Pos).Normalized().Y * tiro.Rumo.Y > 0.7f,
					  $"rumo {tiro.Rumo}");
			AfirmarTi("...e ele PAGOU o Ki da formula do jogador (`10*BaseDrain`)",
					  kiAntes - atirador.Ficha.Ki >= 10 * atirador.Ficha.BaseDrain() * 0.98,
					  $"pagou {kiAntes - atirador.Ficha.Ki:0.#}, devia {10 * atirador.Ficha.BaseDrain():0.#}");
		}

		// E O TIRO ACERTA. O funil de dano e o de sempre (`Acertar` -> `DanoDeKi` -> `AplicarDanoPronto`).
		//
		// ============================ SEM O DADO DA DEFLEXAO, PELO MESMO MOTIVO DE SEMPRE ============================
		// O `Acertar` sorteia deflexao em todo impacto contra quem esta de pe
		// (`GameServer.Projeteis.cs:1190-1240`), e a chance e a razao entre as duas forcas
		// (`DanoDeKi.ChanceDeDeflexao`, `objects.dm:333`). MEDIDO com os corpos daqui -- dois de 50.000
		// e o feixe do `Ki_Wave` de `base_damage` 1 --: **1% por impacto**. E uma medicao de "o funil
		// chegou ao fim" que as vezes mede o dado, que e o defeito que derrubou a `--tecnicateste` em
		// 2026-09-02 (la a bola dava 0,0999%).
		//
		// AQUI O KNOB NAO CABE NA RECEITA: quem monta o tiro e o ARSENAL de producao, dentro da decisao
		// da IA -- escrever `Deflectivel = false` la seria dar a todo NPC do jogo um raio que ninguem
		// deflete. Entao a bancada desarma o que ja esta no ar, a cada tique: e o mesmo campo, pelo
		// mesmo motivo do `RaioDaBancada` (`GameServer.ProjeteisTeste.cs:2167`), no unico lugar que ela
		// alcanca. Vale pra QUALQUER tiro da zona porque a IA pode atirar mais de uma vez nos 8 s.
		//
		// E nao da pra desligar pelo corpo do alvo: `Fighter.Statify` calcula o `Rkidef` com piso
		// (`max(kidef, 0.1)`, `Core/Stats/Fighter.Statify.cs:88`), entao `Ekidef` de um corpo vivo e
		// sempre maior que zero -- a chance nunca e nula.
		// ============================================================================================================
		double vidaAntes = alvo.Combate.Corpo.Vida();
		for (int i = 0; i < 30 * 8 && alvo.Combate.Corpo.Vida() >= vidaAntes; i++)
		{
			foreach (Projetil no in ProjeteisDaZona(atirador.Zone.Hash)) no.Deflectivel = false;
			TiqueDeMundo();
		}
		AfirmarTi("...e alguem levou dano no fim da linha (o funil de ki de producao)",
				  alvo.Combate.Corpo.Vida() < vidaAntes,
				  $"vida {alvo.Combate.Corpo.Vida():0.###} contra {vidaAntes:0.###}");

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 3) QUANDO ELE ATIRA
	// =====================================================================
	/// <summary>
	/// AS RECUSAS DO `EscolherTiro`, medidas NO TIQUE e nao na tabela. A `--iateste` ja mede as
	/// mesmas cinco com `Tiro` sintetico; o que muda aqui e que o dado vem do molde e o gesto passa
	/// pelo atuador -- ou seja, mede-se tambem que nada no meio do caminho reintroduz a permissao.
	/// </summary>
	private void QuandoEleAtira()
	{
		GD.Print("[tiroia] -- 3) QUANDO: AS RECUSAS, NO TIQUE");

		// (a) COLADO: quem esta a um tile soca, nao atira. `AlcanceMin` do `Ki_Wave` = 4 tiles.
		(ServerPlayer perto, ServerPlayer _) = DuelistasDeBancada(tiles: 1);
		AfirmarTi("colado (1 tile) ele NAO atira -- soca", AteAlguemAtirar(perto, 6) == null);
		LimparEmbatesDaBancada();

		// ============================ (b) LONGE DEMAIS: ELE ANDA, E SO ENTAO ATIRA ============================
		// A primeira versao afirmava "a 26 tiles ele NAO atira" e reprovou -- com razao, e o defeito
		// era da afirmacao. Um NPC nao fica parado esperando o alcance: ele avanca (`Plano.Pressionar`)
		// e atira quando entra na janela. Medir "nao atirou em 6 s" mediria a velocidade de caminhada.
		//
		// O que a regra promete e outra coisa, e e esta que da pra medir: **quando o tiro sai, o alvo
		// esta DENTRO da janela** -- ninguem atira de 26 tiles. O teto e o maior `AlcanceMax` da tabela
		// (o raio, 18 tiles), lido da propria tabela e nao digitado aqui.
		// =================================================================================================
		(ServerPlayer longe, ServerPlayer alvoLonge) = DuelistasDeBancada(tiles: 26);
		float noDisparo = -1;
		for (int i = 0; i < 30 * 20 && noDisparo < 0; i++)
		{
			TiqueDeMundo();
			foreach (Projetil p in ProjeteisDaZona(longe.Zone.Hash))
				if (p.Dono == longe.Id && p.Vivo) { noDisparo = (alvoLonge.Pos - longe.Pos).Length; break; }
		}
		double janela = 0;
		foreach (TecnicasDeLonge.Linha l in TecnicasDeLonge.Todas)
			janela = Math.Max(janela, l.AlcanceMaxTiles * ZoneCollision.TileSize);
		AfirmarTi("comecando a 26 tiles, o tiro so sai depois de ele entrar na janela",
				  noDisparo > 0 && noDisparo <= janela,
				  $"disparou a {noDisparo / ZoneCollision.TileSize:0.#} tiles, janela {janela / ZoneCollision.TileSize:0.#}");
		LimparEmbatesDaBancada();

		// (c) SEM KI: o piso e o custo da propria tecnica.
		(ServerPlayer seco, ServerPlayer _) = DuelistasDeBancada(tiles: 9);
		seco.Ficha.Ki = 5 * seco.Ficha.BaseDrain();
		AfirmarTi("sem Ki pra pagar a tecnica ele NAO atira", AteAlguemAtirar(seco, 6) == null);
		LimparEmbatesDaBancada();

		// (d) ALVO CAIDO: `EscolherTiro` recusa alvo no chao -- nao se gasta raio em quem ja caiu.
		(ServerPlayer contraCaido, ServerPlayer caido) = DuelistasDeBancada(tiles: 9);
		caido.Ficha.KO = true;
		AfirmarTi("contra alvo CAIDO ele NAO atira", AteAlguemAtirar(contraCaido, 6) == null);
		LimparEmbatesDaBancada();

		// (e) CONTROLE: o MESMO corpo, na MESMA janela, com Ki e alvo de pe, atira. Sem esta linha
		// as quatro de cima seriam compativeis com "esta bancada nunca ve tiro nenhum".
		(ServerPlayer controle, ServerPlayer _) = DuelistasDeBancada(tiles: 9);
		AfirmarTi("(controle) na janela, com Ki e alvo de pe, ele atira", AteAlguemAtirar(controle, 12) != null);
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 4) O CANAL DO RAIO
	// =====================================================================
	/// <summary>
	/// O raio e um estado sustentado e o cerebro solta um PULSO -- quem fecha e o relogio
	/// (`TickDoPrazoDeRaioDaIa`, o `npc_beam_loop` do DM). Aqui isso e medido com o corpo dirigido
	/// de verdade, e junto vai a consequencia que o jogador ve: enquanto o raio esta na mao, o
	/// NPC nao anda (`PodeMexerOCorpo` -> `EnraizadoPorKi`).
	/// </summary>
	private void OCanalDoRaio()
	{
		GD.Print("[tiroia] -- 4) O CANAL: O PULSO ABRE, O RELOGIO FECHA");

		(ServerPlayer npc, ServerPlayer _) = DuelistasDeBancada(tiles: 9);
		Projetil? tiro = AteAlguemAtirar(npc, 12);
		AfirmarTi("(preparo) ele atirou", tiro != null);

		if (tiro is { Tipo: TipoDeProjetil.Beam })
		{
			AfirmarTi("com o raio na mao o canal esta aberto", _canais.ContainsKey(npc.Id));
			AfirmarTi("...e o corpo esta PLANTADO pelo mesmo funil do jogador", !PodeMexerOCorpo(npc));

			double segundos = 0;
			for (int i = 0; i < 30 * 30 && _canais.ContainsKey(npc.Id); i++)
			{
				TiqueDeMundo();
				segundos += Protocol.TickSeconds;
			}
			AfirmarTi($"o canal se fecha sozinho no prazo do DM ({EmbateDeKi.SegundosDeFeixeDeNpc}s)",
					  !_canais.ContainsKey(npc.Id) && segundos <= EmbateDeKi.SegundosDeFeixeDeNpc + 1,
					  $"durou {segundos:0.##}s");
			AfirmarTi("...e o corpo volta a andar depois", PodeMexerOCorpo(npc));
		}
		else AfirmarTi("o tiro escolhido foi um raio (senao esta familia nao se aplica)",
					   tiro != null, $"tipo {tiro?.Tipo}");

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 5) DOIS NPCs SE CRUZANDO
	// =====================================================================
	/// <summary>
	/// A PERGUNTA DO DONO: *"se dois NPCs se cruzarem, o que acontece?"*
	///
	/// Nada de especial foi escrito pra isto, e e o ponto: os dois entram pelo mesmo gatilho
	/// (`TentarEmbateDeFeixes`), nenhum tem teclado, os dois empurram pela taxa automatica escalada
	/// pela inteligencia (`BeamClash.dm:138`), e o desfecho sai do mesmo `Resolver`. O que a bancada
	/// afirma e que a disputa COMECA sem jogador nenhum e que ela ACABA -- um encontro de NPCs que
	/// nao termina seria dois corpos plantados pra sempre no meio do mapa.
	/// </summary>
	private void DoisNpcsSeCruzando()
	{
		GD.Print("[tiroia] -- 5) DOIS NPCs: A DISPUTA SEM TECLADO NENHUM");

		(ServerPlayer a, ServerPlayer b) = DoisAtiradoresDeFrente(tiles: 12);

		DisputaDeKi? disputa = null;
		for (int i = 0; i < 30 * 20 && disputa == null; i++)
		{
			TiqueDeMundo();
			disputa = _emEmbateDeKi.GetValueOrDefault(a.Id);
		}

		AfirmarTi("dois NPCs atirando um no outro comecam uma colisao de ki, sem ninguem no teclado",
				  disputa != null);
		if (disputa != null)
		{
			AfirmarTi("...e os dois lados sao corpos SEM `Peer` (a taxa automatica do DM)",
					  disputa.A.Quem.Peer == null && disputa.B.Quem.Peer == null);
			AfirmarTi("...e os dois ficam plantados enquanto ela corre",
					  !PodeMexerOCorpo(disputa.A.Quem) && !PodeMexerOCorpo(disputa.B.Quem));

			double segundos = 0;
			for (int i = 0; i < 30 * 40 && _emEmbateDeKi.ContainsKey(a.Id); i++)
			{
				TiqueDeMundo();
				segundos += Protocol.TickSeconds;
			}
			AfirmarTi("...e ela ACABA (ninguem fica preso pra sempre)",
					  !_emEmbateDeKi.ContainsKey(a.Id), $"passaram {segundos:0.#}s");
			AfirmarTi("...e os dois corpos voltam a se mexer",
					  PodeMexerOCorpo(a) || a.Ficha.KO || a.Ficha.dead);
		}

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 6) A CALIBRAGEM
	// =====================================================================
	/// <summary>
	/// UM MINUTO DE LUTA, CONTADO. Nao ha "certo" nem "errado" aqui -- ha os NUMEROS que respondem a
	/// pergunta do dono (*"um NPC que atira sempre e tao obvio quanto um que nunca atira"*), e uma
	/// unica afirmacao: que ele **nao passa a luta inteira plantado**.
	///
	/// ============================ POR QUE O PLANTADO E O NUMERO QUE IMPORTA ============================
	/// O raio PRENDE quem o segura (`canmove = 0`, `beams.dm:294`) e o canal de um NPC so fecha por
	/// tempo (12 s, `BCL_NPC_BEAM_TIME`). Como o cerebro escolhe o golpe por `(1-risco)*CustoDeKi` e os
	/// dois custam o mesmo `10*BaseDrain`, o desempate e a PRECISAO -- e o raio (0,78) ganha da bola
	/// (0,55) sempre que os dois cabem. Um NPC que so escolhe raio e um NPC que fica de pe, parado,
	/// quase o tempo todo; e isso e calibragem, e calibragem se MEDE antes de se opinar.
	///
	/// O que se faz com o numero, se ele incomodar, e DADO e nao regra: a `Precisao` e o
	/// `TempoDeConjuracao` da linha do raio em `TecnicasDeLonge`. Nada disso e codigo de IA.
	/// ==============================================================================================
	/// </summary>
	private void ACalibragem()
	{
		GD.Print("[tiroia] -- 6) A CALIBRAGEM: UM MINUTO DE LUTA, CONTADO");

		(ServerPlayer npc, ServerPlayer alvo) = DuelistasDeBancada(tiles: 9);

		// O ALVO NAO MORRE NO MEIO: quem esta sendo medido e o atirador, e uma luta que acaba aos 20 s
		// mediria 20 s. Ele e curado a cada volta -- nao ha regra nova, so um alvo que aguenta.
		int tiques = (int)(60 / Protocol.TickSeconds);
		int plantado = 0, atirando = 0;
		var porTecnica = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		var vistos = new HashSet<Projetil>();

		for (int i = 0; i < tiques; i++)
		{
			alvo.Ficha.Ki = alvo.Ficha.MaxKi;
			foreach (BodyPart bp in alvo.Combate.Corpo.Partes) bp.Vida = bp.VidaMax;
			alvo.Ficha.KO = false;

			TiqueDeMundo();

			if (!PodeMexerOCorpo(npc)) plantado++;
			if (npc.Cerebro?.Atual == Plano.Atirar) atirando++;
			foreach (Projetil p in ProjeteisDaZona(npc.Zone.Hash))
			{
				if (p.Dono != npc.Id || !vistos.Add(p)) continue;
				string k = p.Tipo.ToString();
				porTecnica[k] = porTecnica.GetValueOrDefault(k) + 1;
			}
		}

		int total = 0;
		foreach (int n in porTecnica.Values) total += n;
		GD.Print($"[tiroia]     60 s de luta a 9 tiles: {total} tiros "
			   + $"[{string.Join(", ", porTecnica.Select(kv => $"{kv.Key} {kv.Value}"))}]  |  "
			   + $"plantado {100.0 * plantado / tiques:0}% do tempo, plano=Atirar em {100.0 * atirando / tiques:0}%");

		AfirmarTi("ele atirou mais de uma vez no minuto (a rotacao nao trava depois do primeiro)", total > 1,
				  $"{total} tiros");
		AfirmarTi("...e nao passou a luta inteira plantado (sobra corpo pra socar e andar)",
				  plantado < tiques * 0.9, $"{100.0 * plantado / tiques:0}%");

		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 7) O CUSTO
	// =====================================================================
	/// <summary>
	/// O QUE O ARSENAL COBRA POR TIQUE. A leitura de capacidades varre o livro de skills a 1 Hz e a
	/// linha de visao so e tracada por quem tem o que atirar -- as duas coisas foram escritas pra
	/// ficar fora do caminho de 30 Hz, e isto mede se e verdade com corpos que REALMENTE atiram.
	/// </summary>
	private void OCustoDoArsenal()
	{
		GD.Print("[tiroia] -- 7) O CUSTO, MEDIDO");

		foreach (bool armados in new[] { false, true })
		{
			var corpos = new List<ServerPlayer>();
			Vec2 praca = CorredorLivre(10);
			for (int i = 0; i < 12; i++)
			{
				ServerPlayer p = Forjar($"Bicho{i}", new Vec2(praca.X + i % 4 * 64, praca.Y + i / 4 * 64), bp: 50_000);
				p.Ficha.Ki = p.Ficha.MaxKi;
				p.Cerebro = Temperamento.Montar(_moldes?.Get("saibaman") ?? new MoldeDeNpc(), i / 12.0);
				if (armados) DarOsTresDegraus(p);
				corpos.Add(p);
			}

			for (int i = 0; i < 60; i++) TiqueDeMundo();   // aquece: 1 Hz de capacidades ja rodou

			ulong t0 = Time.GetTicksUsec();
			for (int i = 0; i < 300; i++) TiqueDeMundo();
			double us = (Time.GetTicksUsec() - t0) / 300.0;

			GD.Print($"[tiroia]     12 corpos {(armados ? "COM" : "sem")} arsenal -> {us,7:0.0} us/tique"
				   + $"  ({us / (Protocol.TickSeconds * 1e6) * 100:0.00}% do tique)");

			if (armados)
				AfirmarTi("12 NPCs armados cabem em 1/10 do orcamento do tique",
						  us < Protocol.TickSeconds * 1e6 / 10, $"{us:0.0} us");

			LimparEmbatesDaBancada();
		}
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	/// <summary>
	/// UM TIQUE DE MUNDO -- os mesmos cinco laços que o `_Process` do servidor roda, na mesma ordem.
	///
	/// A bancada chama a CADENCIA na mao (e o unico privilegio dela, o mesmo da `--embatekiteste`);
	/// o que roda dentro e producao inteira.
	/// </summary>
	private void TiqueDeMundo()
	{
		TickDosCorposSemDono(Protocol.TickSeconds);
		TickDosCanaisDeKi(Protocol.TickSeconds);
		TickDosProjeteis(Protocol.TickSeconds);
		TickDosEmbatesDeKi(Protocol.TickSeconds);
		TickDoPrazoDeRaioDaIa(Protocol.TickSeconds);
	}

	/// <summary>
	/// UM ATIRADOR DIRIGIDO E UM ALVO, no corredor livre da Terra.
	///
	/// O atirador leva um cerebro de PRODUCAO (`Temperamento.Montar` a partir de um molde de
	/// verdade) e os tres degraus de nivel -- que e como um NPC de molde ganha os verbs que voam.
	/// O alvo nao tem cerebro: quem esta sendo medido e o atirador, e um alvo que revida trocaria
	/// a medicao por uma briga.
	/// </summary>
	private (ServerPlayer, ServerPlayer) DuelistasDeBancada(int tiles)
	{
		Vec2 chao = CorredorLivre(tiles + 6);
		ServerPlayer npc = Forjar("Atirador", chao, bp: 50_000);
		ServerPlayer alvo = Forjar("Alvo", new Vec2(chao.X + tiles * ZoneCollision.TileSize, chao.Y), bp: 50_000);

		npc.Facing = Facing.East;
		alvo.Facing = Facing.West;
		npc.Ficha.Ki = npc.Ficha.MaxKi;
		alvo.Ficha.Ki = alvo.Ficha.MaxKi;

		DarOsTresDegraus(npc);
		npc.Cerebro = Temperamento.Montar(_moldes?.Get("rival_do_mundo") ?? new MoldeDeNpc(), 0);
		return (npc, alvo);
	}

	/// <summary>Dois corpos dirigidos e armados, de frente um pro outro.</summary>
	private (ServerPlayer, ServerPlayer) DoisAtiradoresDeFrente(int tiles)
	{
		(ServerPlayer a, ServerPlayer b) = DuelistasDeBancada(tiles);
		DarOsTresDegraus(b);
		b.Cerebro = Temperamento.Montar(_moldes?.Get("rival_do_mundo") ?? new MoldeDeNpc(), 0.5);
		return (a, b);
	}

	/// <summary>
	/// OS TRES DEGRAUS QUE DAO OS VERBS QUE VOAM -- pelo caminho de producao: a skill entra no livro
	/// e o NIVEL dela e cravado, que e literalmente o que o `SorteioDeNpc` faz com o
	/// `molde.NivelDasSkills`. Dar o verb na mao mediria o atalho.
	/// </summary>
	private static void DarOsTresDegraus(ServerPlayer pl)
	{
		foreach ((string path, int nivel) in new[]
		{
			("/datum/skill/mind/Basic_Ki_Effusion", 25),
			("/datum/skill/mind/Basic_Ki_Control", 30),
			("/datum/skill/mind/Ki_Unlocked", 35),
		})
		{
			pl.Livro.Dar(path);
			pl.Niveis.Por(path, nivel);
		}
	}

	/// <summary>
	/// RODA O MUNDO ATE ESTE CORPO POR UM TIRO NA ZONA. Devolve o projetil, ou nulo se o prazo
	/// venceu -- e o nulo e uma resposta de verdade: e ele que a familia 3 usa pra medir recusa.
	/// </summary>
	private Projetil? AteAlguemAtirar(ServerPlayer quem, double segundos)
	{
		int tiques = (int)(segundos / Protocol.TickSeconds);
		for (int i = 0; i < tiques; i++)
		{
			TiqueDeMundo();
			foreach (Projetil p in ProjeteisDaZona(quem.Zone.Hash))
				if (p.Dono == quem.Id && p.Vivo) return p;
		}
		return null;
	}
}
