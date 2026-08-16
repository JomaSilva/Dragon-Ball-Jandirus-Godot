using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Ranks;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--seloteste` -- O SELO, O POTE, A DEAD ZONE E O SIGILO DE PODER (lote G9).
///
///     Godot --headless --path . --host --rede 7981 --seloteste
///
/// ============================ POR QUE ELA EXISTE, E O QUE ELA NAO E ============================
/// O lote G9 nasceu de um BURACO DE EXTRACAO -- tres skills perdiam o `after_learn()` em silencio
/// e o jogo ANUNCIAVA como entregue o que nao tinha botao. Um conserto assim tem duas metades: a
/// primeira e o dado voltar (o extrator), e a segunda e o dado virar efeito. Esta bancada mede a
/// segunda, e mede o CODIGO DE PRODUCAO: quem sela e o `MafubaG9` que o jogador aperta, quem
/// arrasta e o `PassoDosPortais` que o tique chama, e quem solta e o `Estragar` que o soco chama.
///
/// **Ela nao afirma sobre o extrator.** Aquilo o `dotnet run -- skills` afirma sozinho, e agora com
/// alarme proprio (`after_learn de caminho absoluto SEM DONO: N`, com saida nao-zero).
/// ==============================================================================================
///
/// ============================ O QUE ELA TENTA REPROVAR ============================
///  1. NUCLEO   -- as tres regras do `TestEscape` (`Sealing.dm:44-66`) como funcao pura: a razao de
///     fuga de 1,25, a corrosao `0.001/dur` e o piso de 0,25 do pote sumido. Inclusive a divisao
///     por zero do original, que aqui e uma guarda.
///  2. MAFUBA   -- **sem pote nao ha Mafuba** (o `input()` de lista vazia do DM), com pote a fita
///     nasce, e o custo de 90 por membro e cobrado NA HORA e nao na chegada.
///  3. FUGA     -- o preso que fica 25% mais forte sai sozinho no tique, e volta pro ponto guardado.
///  4. POTE     -- quebrar o pote SOLTA quem estava dentro (`SealingItem/Del()`), e um pote com
///     gente dentro nao se carrega.
///  5. DEAD ZONE-- cobra 90,9% do Ki, arrasta quem esta a 12 tiles, e o selo dela NAO tem pote --
///     ou seja, nao ha o que quebrar.
///  6. DISCO    -- selo gravado e selo relido: logout nao e a chave mestra da prisao.
///  7. SIGILO   -- `Conceal_Power` alterna e trava 5 s; `Power_Control` SO BAIXA.
///  8. CENSO    -- as tres skills de selo deixaram de ser "folha muda de nascenca": duas respondem
///     PRONTA e a terceira nomeia o sistema que falta. E a afirmacao que fecha o buraco original.
///  9. O PAINEL, LIDO DO PACOTE -- a pergunta do dono ("o painel do Eremita Tartaruga ainda anuncia o
///     Mafuba?") respondida nos BYTES que sairiam no fio, e nao na tabela que os gera. Com o efeito
///     do Mafuba ARRANCADO no meio, pra provar que o painel e derivado do censo.
/// 10. O EFEITO NOMEADO, COM O CARGO NO TRONO -- os quatro verbos do lote disparados pelo caminho de
///     producao (coroacao -> dadiva -> livro -> botao), e nao com a skill escrita a mao.
/// ==================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O PACOTE DE CARGOS QUE SAIU -- (pra quem, os bytes). Anotado dentro do
	/// <see cref="MandarCargos"/>, com o `NetDataWriter` inteiro.
	///
	/// ============================ POR QUE A BANCADA LE OS BYTES ============================
	/// A `--cargoportas` ja pergunta ao `OQueOCargoEntrega` e isso prova a TABELA. A pergunta desta
	/// fase e outra: *"o painel do Eremita Tartaruga ainda lista o Mafuba?"* -- e painel e o que
	/// CHEGA. Entre a tabela e a tela ha um `Put`/`GetString` com limite de tamanho, uma ordem de
	/// campos e um laco por cargo; qualquer um dos tres pode entregar uma linha vazia com a tabela
	/// perfeita, e a bancada que le a funcao nao ve nada disso.
	///
	/// Nula em jogo -- uma comparacao contra null no caminho do envio. Mora NESTE arquivo pelo
	/// mesmo motivo das escutas da `--formasteste`: nasce e morre com a bancada.
	/// ====================================================================================
	/// </summary>
	internal static List<(int Para, NetDataWriter Pacote)>? EscutaDeCargos;

	private int _seOk, _seFalhou;

	private void AfirmarSe(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _seOk++; GD.Print($"[selo]   OK    {oque}"); return; }
		_seFalhou++;
		GD.PrintErr($"[selo]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDoSelo()
	{
		_seOk = _seFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[selo] ================ O SELO, O POTE E A DEAD ZONE (lote G9) ================");

		// A FOTO DOS TRONOS DE VERDADE. As familias 9 e 10 COROAM gente pelo caminho de producao, e
		// `Outorgar`/`Destronar` gravam o `cargos.txt` -- rodar a bancada nao pode custar o cargo de
		// ninguem. Mesma precaucao da `--cargoportas`, que fotografa os tres arquivos do mundo.
		var tronosReais = new Dictionary<string, string>(_tronos, StringComparer.OrdinalIgnoreCase);

		try
		{
			ONucleoDoSelo();
			OMafubaDePontaAPonta();
			AFugaPorPoder();
			OPoteQueQuebra();
			ADeadZone();
			OSeloNoDisco();
			OSigiloDePoder();
			OCensoNaoMenteMais();
			OPainelLidoDoPacote();
			OEfeitoComOCargoNoTrono();
		}
		catch (Exception e)
		{
			_seFalhou++;
			GD.PrintErr($"[selo] EXCECAO: {e}");
		}
		finally
		{
			EscutaDeCargos = null;
			RegistrarTecnicasG9();   // desfaz qualquer arrancada de efeito da familia 9

			_tronos.Clear();
			foreach ((string k, string v) in tronosReais) _tronos[k] = v;
			SalvarCargos();

			LimparOSelo();
		}

		GD.Print($"[selo] ================ {_seOk} OK, {_seFalhou} FALHA ================");
	}

	// =====================================================================
	// 1) O NUCLEO -- `Sealing.dm:44-66` como funcao pura
	// =====================================================================
	private void ONucleoDoSelo()
	{
		GD.Print("[selo] -- 1) O NUCLEO: as tres regras do TestEscape");

		// `if(SealingPersonBP==0) SealerBP = 4.0e10` (`:33-34`) -- e o caminho do Mafuba.
		AfirmarSe("selar com BP zero vale o TETO DURO de 40 bilhoes (o defeito do `SealStrength`)",
				  Selo.BpDoSelo(0) == Selo.TetoDuro, $"{Selo.BpDoSelo(0)}");
		AfirmarSe("...e selar com BP declarado vale o BP declarado (o caminho da Dead Zone)",
				  Math.Abs(Selo.BpDoSelo(50_000) - 50_000) < 1e-9);

		// `BPModulus(expressedBP,SealerBP)>=1.25` (`:46`)
		AfirmarSe("empatado com o selo NAO sai", !Selo.PodeArrebentar(1_000, 1_000));
		AfirmarSe("20% mais forte que o selo ainda NAO sai", !Selo.PodeArrebentar(1_200, 1_000));
		AfirmarSe("25% mais forte SAI -- e o degrau, nao 'ficar mais forte'",
				  Selo.PodeArrebentar(1_250, 1_000));

		// `SealHP -= 0.001/SealedContainerDur` (`:49`, `:61`)
		AfirmarSe("pote inteiro (dur 1) corroi 0,001 por passo",
				  Math.Abs(Selo.Corrosao(1) - 0.001) < 1e-12, $"{Selo.Corrosao(1)}");
		AfirmarSe("pote no piso (dur 0,25) corroi QUATRO vezes mais",
				  Math.Abs(Selo.Corrosao(Selo.DuracaoSemPote) - 0.004) < 1e-12);

		// ============================ A DIVISAO POR ZERO DO ORIGINAL ============================
		// Com `dur == 0` a linha do DM e `0.001/0` -- runtime que ABORTA o proc. O efeito observavel
		// e "nao ha corrosao", e e isso que a guarda tem que devolver. Um `Infinity` aqui zeraria a
		// vida do selo no primeiro passo e soltaria TODO preso da Dead Zone que passasse do selador.
		// ======================================================================================
		AfirmarSe("selo SEM POTE (dur 0) nao corroi -- a divisao por zero do DM virou guarda",
				  Selo.Corrosao(0) == 0);

		// o passo inteiro, com o objeto de producao
		var s = new Selo();
		s.Selar(1_000, 1, poteId: 7, ZoneKey.Premade("Earth"), 100, 200);
		AfirmarSe("selado guarda o ponto de volta inteiro (zona + x + y)",
				  s.ZonaDeVolta.Name == "Earth" && s.VoltaX == 100 && s.VoltaY == 200);
		AfirmarSe("...e o pote", s.PoteId == 7 && Math.Abs(s.DuracaoDoPote - 1) < 1e-9);

		AfirmarSe("preso fraco: continua", s.Passo(900, poteExisteNoMundo: true) == FimDoSelo.Continua);
		AfirmarSe("...e a vida do selo NEM ARRANHA (so corroi quem ja passou o selador)",
				  s.VidaDoSelo == 100, $"{s.VidaDoSelo}");

		AfirmarSe("preso 10% acima do selador: ainda preso", s.Passo(1_100, true) == FimDoSelo.Continua);
		AfirmarSe("...mas a vida do selo COMECOU a ceder", s.VidaDoSelo < 100, $"{s.VidaDoSelo}");

		AfirmarSe("preso 25% acima: SOLTA", s.Passo(1_250, true) == FimDoSelo.Solta);

		// o pote DANIFICADO que sumiu do mundo -- `:51-62`
		var d = new Selo();
		d.Selar(1_000_000_000, 0.5, poteId: 9, ZoneKey.Premade("Earth"), 0, 0);
		AfirmarSe("pote danificado e SUMIDO: o preso e avisado uma vez",
				  d.Passo(1, poteExisteNoMundo: false) == FimDoSelo.Enfraqueceu);
		AfirmarSe("...e a durabilidade cai pro piso de 0,25",
				  Math.Abs(d.DuracaoDoPote - Selo.DuracaoSemPote) < 1e-9, $"{d.DuracaoDoPote}");
		AfirmarSe("...e o aviso NAO se repete no passo seguinte",
				  d.Passo(1, poteExisteNoMundo: false) == FimDoSelo.Continua);
	}

	// =====================================================================
	// 2) MAFUBA -- `Sealing.dm:156-190`
	// =====================================================================
	private void OMafubaDePontaAPonta()
	{
		GD.Print("[selo] -- 2) MAFUBA: sem pote nao ha Mafuba; com pote a fita sai e cobra na hora");

		Vec2 chao = CorredorLivre(20);
		ServerPlayer quemSela = ForjarSelador("Mafubeiro", chao, bp: 5_000);
		ServerPlayer vitima = ForjarSelador("Selado", new Vec2(chao.X + 5 * ZoneCollision.TileSize, chao.Y), bp: 900);
		quemSela.AlvoId = vitima.Id;   // o duplo clique do jogador -- ver `Marcado`

		// ---- SEM POTE ----
		int fitasAntes = _fitas.Count;
		UsarHabilidade(quemSela, "Mafuba");
		AfirmarSe("sem Pote Selante a vista, o Mafuba NAO sai (o input de lista vazia do DM)",
				  _fitas.Count == fitasAntes, $"{_fitas.Count}");
		AfirmarSe("...e nao custa vida nenhuma", quemSela.Combate.Corpo.Vida() > 90,
				  $"{quemSela.Combate.Corpo.Vida():0.#}");

		// ---- COM POTE ----
		Obra pote = PorUmPote(quemSela.Zone, chao.X + 2 * ZoneCollision.TileSize, chao.Y);
		double vidaAntes = quemSela.Combate.Corpo.Vida();

		UsarHabilidade(quemSela, "Mafuba");
		AfirmarSe("com pote a vista, a fita NASCE", _fitas.Count == fitasAntes + 1);

		// `SpreadDamage(90)` no corpo do VERB (`:188`), e nao na chegada
		AfirmarSe("o preco (90 em CADA membro) e cobrado NA HORA -- e nao quando a fita chega",
				  quemSela.Combate.Corpo.Vida() < vidaAntes,
				  $"{vidaAntes:0.#} -> {quemSela.Combate.Corpo.Vida():0.#}");
		AfirmarSe("...e a vitima AINDA NAO esta selada (a fita tem que viajar)", !vitima.Selo.Preso);

		// a fita anda no tique de producao
		for (int i = 0; i < 200 && !vitima.Selo.Preso; i++) TickDoSelo(0.05);

		AfirmarSe("a fita alcanca e SELA", vitima.Selo.Preso);
		AfirmarSe("...o selo do Mafuba vale o TETO DURO (o `SealStrength` que o DM nunca preenche)",
				  Math.Abs(vitima.Selo.BpDoSelador - Selo.TetoDuro) < 1, $"{vitima.Selo.BpDoSelador:N0}");
		AfirmarSe("...o preso foi pro chao dos selados", EhOSelo(vitima.Zone), vitima.Zone.Name);
		AfirmarSe("...e o pote ficou amarrado a ele", vitima.Selo.PoteId == pote.Id);

		// `if(Planet!="Sealed") GotoPlanet("Sealed")` (`:63-64`)
		MoveToZone(vitima.Id, ZonaDaBancadaDeProjetil, chao);
		for (int i = 0; i < 20 && !EhOSelo(vitima.Zone); i++) TickDoSelo(0.1);
		AfirmarSe("tirar o preso do bolso a forca NAO adianta: o tique o traz de volta",
				  EhOSelo(vitima.Zone), vitima.Zone.Name);

		LimparOSelo();
	}

	// =====================================================================
	// 3) A FUGA POR PODER -- `Sealing.dm:46-47`
	// =====================================================================
	private void AFugaPorPoder()
	{
		GD.Print("[selo] -- 3) A FUGA: 25% acima do selo, e o corpo volta pro lugar de onde saiu");

		Vec2 chao = CorredorLivre(20);
		ServerPlayer preso = ForjarSelador("Fujao", chao, bp: 1_000);
		ZoneKey zonaAntes = preso.Zone;

		// selado por um poder MODESTO -- e o caminho da Dead Zone (`SealMob(makerBP, 0)`)
		Selar(preso, 1_000, duracaoDoPote: 0, poteId: 0);
		AfirmarSe("selado: saiu do mundo", EhOSelo(preso.Zone));

		for (int i = 0; i < 20; i++) TickDoSelo(0.1);
		AfirmarSe("com o mesmo poder do selo, continua preso depois de 2 s de tique", preso.Selo.Preso);

		// ficar 25% mais forte -- e o unico jeito
		preso.Ficha.expressedBP = 1_300;
		for (int i = 0; i < 20 && preso.Selo.Preso; i++) TickDoSelo(0.1);

		AfirmarSe("25% mais forte que o selo: SAI sozinho no tique", !preso.Selo.Preso);
		AfirmarSe("...e volta pra ZONA de onde saiu", preso.Zone.Equals(zonaAntes), preso.Zone.Name);
		AfirmarSe("...no PONTO de onde saiu", Vec2.Distance(preso.Pos, chao) < 1,
				  $"{preso.Pos.X:0}, {preso.Pos.Y:0}");

		LimparOSelo();
	}

	// =====================================================================
	// 4) O POTE -- `Sealing.dm:117-144`
	// =====================================================================
	private void OPoteQueQuebra()
	{
		GD.Print("[selo] -- 4) O POTE: quebrar solta, e com gente dentro ele nao sai do lugar");

		Vec2 chao = CorredorLivre(20);
		ServerPlayer preso = ForjarSelador("Enjarrado", chao, bp: 1_000);
		ServerPlayer dono = ForjarSelador("Dono", chao, bp: 1_000);
		Obra pote = PorUmPote(preso.Zone, chao.X, chao.Y);

		Selar(preso, 0, duracaoDoPote: 1, poteId: pote.Id);
		AfirmarSe("preso num pote", preso.Selo.Preso && preso.Selo.PoteId == pote.Id);

		// `checkdur` (`:121`): a durabilidade do pote e copiada pro preso
		pote.Armadura = pote.ArmaduraMax * 0.4;
		for (int i = 0; i < 120; i++) TickDoSelo(0.1);   // 12 s: passa por dois tiques de pote
		AfirmarSe("o pote empurra a propria durabilidade pro preso (o `checkdur` do DM)",
				  Math.Abs(preso.Selo.DuracaoDoPote - 0.4) < 0.01, $"{preso.Selo.DuracaoDoPote:0.###}");

		// pote com gente dentro nao se recolhe -- desvio DECLARADO, ver `GameServer.Tech.PegarObra`
		AfirmarSe("o servidor sabe que este pote esta ocupado", PoteEstaSelado(pote));

		// `SealingItem/Del()` (`:140-144`): quebrar solta -- e quem quebra e o `Estragar` de producao
		Estragar(pote, dano: pote.ArmaduraMax * 100, autor: dono);
		AfirmarSe("o pote foi ao chao", !_noChao.Contains(pote));
		AfirmarSe("QUEBRAR O POTE SOLTA O PRESO -- a interacao principal do Mafuba", !preso.Selo.Preso);
		AfirmarSe("...e ele sai do bolso", !EhOSelo(preso.Zone), preso.Zone.Name);

		LimparOSelo();
	}

	// =====================================================================
	// 5) A DEAD ZONE -- `Sealing.dm:239-298`
	// =====================================================================
	private void ADeadZone()
	{
		GD.Print("[selo] -- 5) A DEAD ZONE: 90,9% do Ki, arrasto de 12 tiles, selo sem pote");

		Vec2 chao = CorredorLivre(20);
		ServerPlayer quemAbre = ForjarSelador("Garlic", chao, bp: 40_000);

		// `if(usr.Ki>=MaxKi/1.1) ... else "You don't have enough Ki."` (`:243-246`)
		quemAbre.Ficha.Ki = quemAbre.Ficha.MaxKi * 0.5;
		int portaisAntes = _portais.Count;
		UsarHabilidade(quemAbre, "Open_Dead_Zone");
		AfirmarSe("com metade do tanque, a fenda NAO abre (ela pede 90,9%)",
				  _portais.Count == portaisAntes);

		quemAbre.Ficha.Ki = quemAbre.Ficha.MaxKi;
		double kiAntes = quemAbre.Ficha.Ki;
		UsarHabilidade(quemAbre, "Open_Dead_Zone");
		AfirmarSe("com o tanque cheio, a fenda ABRE", _portais.Count == portaisAntes + 1);
		AfirmarSe("...e cobra exatamente MaxKi/1.1",
				  Math.Abs((kiAntes - quemAbre.Ficha.Ki) - quemAbre.Ficha.MaxKi / 1.1) < 1,
				  $"{kiAntes - quemAbre.Ficha.Ki:0.#}");

		// `A.loc = locate(usr.x, usr.y+5, usr.z)` (`:250`) -- CINCO tiles ao NORTE (que aqui e -Y)
		PortalDaDeadZone p = _portais[^1];
		AfirmarSe("a fenda nasce 5 tiles ao NORTE de quem a abriu, e nao a frente dele",
				  Math.Abs(p.Pos.Y - (chao.Y - 5 * ZoneCollision.TileSize)) < 1 && Math.Abs(p.Pos.X - chao.X) < 1,
				  $"{p.Pos.X:0}, {p.Pos.Y:0}");

		// ============================ O ARRASTO, E A ARMADILHA DE MEDI-LO POR DISTANCIA ============================
		// A primeira versao desta afirmacao comparava a distancia ao portal depois de 4 s e exigia
		// que ela tivesse ENCOLHIDO. Ela ficou vermelha com o arrasto funcionando: em 4 s o corpo foi
		// puxado 8 tiles, CAIU DENTRO, foi selado e mudou de ZONA -- e a distancia passou a ser medida
		// contra uma coordenada de outro mundo (1836 px, o meio do bolso do selo).
		//
		// A licao e a de sempre neste projeto: **medir uma coordenada atraves de uma troca de zona nao
		// mede nada**. O criterio certo e o que o DM descreve -- ou o corpo se aproximou, ou o portal
		// ja o engoliu, que e a forma mais forte de "foi arrastado".
		// =========================================================================================================
		ServerPlayer coitado = ForjarSelador("Puxado", new Vec2(p.Pos.X, p.Pos.Y + 8 * ZoneCollision.TileSize), bp: 500);
		float distAntes = Vec2.Distance(coitado.Pos, p.Pos);
		for (int i = 0; i < 40; i++) TickDoSelo(0.1);
		AfirmarSe("quem esta a 8 tiles e ARRASTADO pra fenda (chegou mais perto, ou ja foi engolido)",
				  coitado.Selo.Preso || Vec2.Distance(coitado.Pos, p.Pos) < distAntes,
				  $"{distAntes:0} -> {Vec2.Distance(coitado.Pos, p.Pos):0}, preso={coitado.Selo.Preso}");

		// e quem cai dentro e selado SEM POTE (se o arrasto ja o levou, o selo dele e o mesmo)
		if (!coitado.Selo.Preso)
		{
			coitado.Pos = p.Pos;
			for (int i = 0; i < 5 && !coitado.Selo.Preso; i++) TickDoSelo(0.1);
		}
		AfirmarSe("quem cai dentro e SELADO", coitado.Selo.Preso);
		AfirmarSe("...sem pote nenhum -- nao ha o que quebrar pra solta-lo",
				  coitado.Selo.PoteId == 0 && coitado.Selo.DuracaoDoPote == 0);
		AfirmarSe("...e o selo vale o BP de quem ABRIU, congelado (`A.makerBP = expressedBP`)",
				  Math.Abs(coitado.Selo.BpDoSelador - p.BpDeQuemAbriu) < 1,
				  $"{coitado.Selo.BpDoSelador:N0} vs {p.BpDeQuemAbriu:N0}");

		// `spawn(100) del(src)` (`:298`): dez segundos
		for (int i = 0; i < 120; i++) TickDoSelo(0.1);
		AfirmarSe("a fenda se fecha sozinha em ~10 s", _portais.Count == portaisAntes);

		LimparOSelo();
	}

	// =====================================================================
	// 6) O DISCO -- `Sealing.dm:22-28` (as vars NAO sao `tmp`) + `Login.dm:258`
	// =====================================================================
	private void OSeloNoDisco()
	{
		GD.Print("[selo] -- 6) O DISCO: deslogar nao e a chave mestra da prisao");

		Vec2 chao = CorredorLivre(20);
		ServerPlayer preso = ForjarSelador("Gravado", chao, bp: 1_000);
		Selar(preso, 12_345, duracaoDoPote: 0.6, poteId: 42);

		CharacterSave save = AccountStore.DeJogador(preso, NowMs());
		AfirmarSe("o selo vai pro save", save.Selo is { Preso: true });
		AfirmarSe("...com o BP do selo, o pote e a durabilidade",
				  save.Selo!.BpDoSelador == 12_345 && save.Selo.PoteId == 42
				  && Math.Abs(save.Selo.DuracaoDoPote - 0.6) < 1e-9);

		// e a volta: um corpo novo, o mesmo save
		ServerPlayer devolta = ForjarSelador("Relogado", chao, bp: 1_000);
		AccountStore.ParaJogador(save, devolta);
		AfirmarSe("relido do save, o corpo continua SELADO", devolta.Selo.Preso);
		AfirmarSe("...com o mesmo selo e o mesmo pote",
				  devolta.Selo.BpDoSelador == 12_345 && devolta.Selo.PoteId == 42);

		// e um save de ANTES disto existir (campo ausente) tem que voltar SOLTO
		var antigo = new CharacterSave();
		ServerPlayer velho = ForjarSelador("SaveVelho", chao, bp: 1_000);
		velho.Selo.Selar(1, 1, 1, ZoneKey.Premade("Earth"), 0, 0);
		AccountStore.ParaJogador(antigo, velho);
		AfirmarSe("save sem o campo (personagem de antes do lote) volta SOLTO, sem migracao nenhuma",
				  !velho.Selo.Preso);

		LimparOSelo();
	}

	// =====================================================================
	// 7) O SIGILO -- `Stats/BP/Power Control.dm:71-83` e `:176-186`
	// =====================================================================
	private void OSigiloDePoder()
	{
		GD.Print("[selo] -- 7) O SIGILO: esconder o poder, e segurar o proprio poder");

		Vec2 chao = CorredorLivre(10);
		ServerPlayer pl = ForjarSelador("Escondido", chao, bp: 100_000);

		// ---- Conceal_Power ----
		_sigiloPronto.Remove(pl.Id);
		AfirmarSe("nasce mostrando o poder", !pl.Ficha.isconcealed);

		UsarHabilidade(pl, "Conceal_Power");
		AfirmarSe("apertar ESCONDE", pl.Ficha.isconcealed);

		UsarHabilidade(pl, "Conceal_Power");
		AfirmarSe("apertar de novo NA HORA nao faz nada -- a trava de 5 s do `canconceal`",
				  pl.Ficha.isconcealed);

		_sigiloPronto[pl.Id] = 0;   // o relogio venceu
		UsarHabilidade(pl, "Conceal_Power");
		AfirmarSe("passados os 5 s, apertar MOSTRA de novo", !pl.Ficha.isconcealed);

		// E O LADO DE QUEM LE, que ja existia e agora tem quem o acione: `isconcealed` trava o BP
		// expresso em 5 (`Fighter.Power.cs:86`). Sem esta afirmacao o botao poderia ligar um campo
		// que nao chega a lugar nenhum -- que e exatamente o estado em que o port estava.
		_sigiloPronto[pl.Id] = 0;
		UsarHabilidade(pl, "Conceal_Power");
		pl.Ficha.Tick(agoraMs: NowMs());
		AfirmarSe("escondido, o BP que o mundo LE cai pro piso do sigilo",
				  pl.Ficha.expressedBP <= 5, $"{pl.Ficha.expressedBP:N0}");

		_sigiloPronto[pl.Id] = 0;
		UsarHabilidade(pl, "Conceal_Power");
		pl.Ficha.Tick(agoraMs: NowMs());
		AfirmarSe("...e volta ao normal ao desligar", pl.Ficha.expressedBP > 5);

		// ---- Power_Control ----
		AfirmarSe("nasce expressando 100% (`powerMod` 1)", Math.Abs(pl.Ficha.powerMod - 1) < 1e-9);

		UsarHabilidade(pl, "Power_Control:40");
		AfirmarSe("pedir 40% BAIXA o powerMod pra 0,4", Math.Abs(pl.Ficha.powerMod - 0.4) < 1e-9,
				  $"{pl.Ficha.powerMod}");

		UsarHabilidade(pl, "Power_Control:80");
		AfirmarSe("pedir MAIS do que se expressa nao levanta nada (`while ... powerMod > numb`)",
				  Math.Abs(pl.Ficha.powerMod - 0.4) < 1e-9, $"{pl.Ficha.powerMod}");

		UsarHabilidade(pl, "Power_Control:0");
		AfirmarSe("zero e preso ao minimo de 1% (`max(1,numb)`)",
				  Math.Abs(pl.Ficha.powerMod - 0.01) < 1e-9, $"{pl.Ficha.powerMod}");

		// ============================ O ESTAGIO QUE ERA INALCANCAVEL ============================
		// `EstagioDaCarga.Retomando` existe desde a tecla C e NUNCA acontecia: nada no port inteiro
		// baixava o `powerMod`. Com o verb portado, a segunda etapa do power-up passa a existir.
		// ======================================================================================
		pl.Ficha.canPower = 1;
		pl.Ficha.MeditateGivesKiRegen = 1;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AfirmarSe("com o poder segurado, a tecla C entra no estagio RETOMANDO -- que ate hoje era "
				  + "inalcancavel porque nada baixava o powerMod",
				  CargaDeKi.Passo(pl.Ficha, 0.1, mexendo: false) == EstagioDaCarga.Retomando,
				  $"{pl.Ficha.powerMod}");

		LimparOSelo();
	}

	// =====================================================================
	// 8) O CENSO -- a afirmacao que fecha o buraco de origem
	// =====================================================================
	/// <summary>
	/// ============================ ESTA FAMILIA E O MOTIVO DE TODO O RESTO ============================
	/// Antes do conserto do extrator, as tres skills de selo saiam do `skills.json` com `verbos: []`,
	/// e o `CensoDeSkills.SistemaQueFalta` respondia `null` pras tres -- que quer dizer "esta skill
	/// FAZ alguma coisa". Dai o painel do Eremita Tartaruga as listava entre o que o cargo ENTREGA e
	/// o recado de posse dizia "o cargo te entrega: ... Mafuba". Nao havia botao nenhum.
	///
	/// Agora `null` so sai pras duas que de fato tem efeito, e a terceira NOMEIA o sistema que falta
	/// -- que e o que o jogador ve na tela, com todas as letras, em vez do silencio.
	/// ==============================================================================================
	/// </summary>
	private void OCensoNaoMenteMais()
	{
		GD.Print("[selo] -- 8) O CENSO: a promessa do cargo parou de mentir");

		if (_skills == null) { AfirmarSe("o servidor tem catalogo de skills", false); return; }

		Skill? mafuba = _skills.Get("/datum/skill/rank/Mafuba");
		Skill? deadZone = _skills.Get("/datum/skill/rank/DeadZone");
		Skill? superior = _skills.Get("/datum/skill/rank/SuperiorSeal");

		AfirmarSe("as tres skills de selo existem no catalogo",
				  mafuba != null && deadZone != null && superior != null);
		if (mafuba == null || deadZone == null || superior == null) return;

		// O CONSERTO DO EXTRATOR, VISTO DO OUTRO LADO: os verbos chegaram.
		AfirmarSe("`Mafuba` concede o verb `Mafuba` (o `after_learn` que o extrator perdia)",
				  mafuba.Verbos.Contains("Mafuba", StringComparer.OrdinalIgnoreCase),
				  string.Join(",", mafuba.Verbos));
		AfirmarSe("`Open Dead Zone` concede o verb `Open_Dead_Zone`",
				  deadZone.Verbos.Contains("Open_Dead_Zone", StringComparer.OrdinalIgnoreCase),
				  string.Join(",", deadZone.Verbos));
		AfirmarSe("`Superior Seal` concede o verb `Seal_Mob`",
				  superior.Verbos.Contains("Seal_Mob", StringComparer.OrdinalIgnoreCase),
				  string.Join(",", superior.Verbos));

		// E O CENSO PASSA A RESPONDER A VERDADE SOBRE AS TRES.
		AfirmarSe("o censo diz que o Mafuba esta PRONTO", CensoDeSkills.SistemaQueFalta(mafuba) == null,
				  CensoDeSkills.SistemaQueFalta(mafuba) ?? "");
		AfirmarSe("...e a Dead Zone tambem", CensoDeSkills.SistemaQueFalta(deadZone) == null,
				  CensoDeSkills.SistemaQueFalta(deadZone) ?? "");

		string? falta = CensoDeSkills.SistemaQueFalta(superior);
		AfirmarSe("...e o Superior Seal e MUDO DE VERDADE, com o sistema NOMEADO",
				  falta != null && falta.Contains("magia", StringComparison.OrdinalIgnoreCase),
				  falta ?? "(null -- ele voltou a mentir)");

		// A PROMESSA DO CARGO, na frase que o jogador le.
		string entrega = OQueOCargoEntrega("turtle");
		AfirmarSe("o painel do Eremita Tartaruga cita o Mafuba entre os PRONTOS",
				  entrega.Contains("Mafuba", StringComparison.OrdinalIgnoreCase)
				  && !entrega.Contains("ainda mudo neste servidor: Mafuba", StringComparison.OrdinalIgnoreCase),
				  entrega);
	}

	// =====================================================================
	// 9) O PAINEL DO CARGO, LIDO DO PACOTE
	// =====================================================================
	/// <summary>
	/// ============================ A PERGUNTA DO DONO, MEDIDA ONDE ELA ACONTECE ============================
	/// *"o painel do cargo Eremita Tartaruga NAO pode mais listar o Mafuba entre o que o cargo entrega,
	/// a menos que o Mafuba faca alguma coisa."*
	///
	/// A familia 8 responde isso lendo a funcao que monta a linha, e a `--cargoportas` faz o mesmo com
	/// o Kaio do Norte. **Nenhuma das duas ve o painel.** Entre a tabela e a tela ha o `Put` de cada
	/// campo, a ordem deles, um laco por cargo e o `GetString(400)` do cliente -- e uma linha grande
	/// demais volta VAZIA do outro lado com a tabela impecavel. Esta familia le os BYTES que sairiam no
	/// fio, com os mesmos limites que o `GameClient` usa (32/32/160/200/400).
	///
	/// E ELA ARRANCA O EFEITO NO MEIO, que e a metade que prova o "a menos que": com o Mafuba
	/// registrado como NAO-PORTADO, o MESMO caminho de producao tem que mover o nome pro lado dos
	/// mudos. Sem essa injecao, a afirmacao de cima ficaria verde ate num painel que imprime uma lista
	/// escrita a mao.
	/// ==================================================================================================
	/// </summary>
	private void OPainelLidoDoPacote()
	{
		GD.Print("[selo] -- 9) O PAINEL: lido do PACOTE, e com o efeito do Mafuba arrancado no meio");

		if (_skills == null) { AfirmarSe("o servidor tem catalogo de skills", false); return; }

		RankDef? eremitaDef = Cargos.Get("turtle");
		RankDef? guardiaoDef = Cargos.Get("guardian");
		if (eremitaDef == null || guardiaoDef == null)
		{
			AfirmarSe("os cargos `turtle` e `guardian` existem no catalogo de cargos", false);
			return;
		}

		// O `Forjar` CRU, e nao o `ForjarSelador`: quem le o painel nao pode ter as skills escritas
		// no livro, senao a linha medida seria a de um sujeito que ja tem tudo.
		Vec2 chao = CorredorLivre(6);
		ServerPlayer quemOlha = Forjar("Quem olha o painel", chao, bp: 1_000);
		quemOlha.Conta = "bancada_selo_painel";

		// ---- o painel de HOJE ----
		(int quantos, string turtle) = PainelDoPacote(quemOlha, "turtle");
		AfirmarSe("o pacote de cargos chega inteiro e em ordem (todas as linhas se leem)",
				  quantos == Cargos.Todos.Length, $"{quantos} de {Cargos.Todos.Length}");
		AfirmarSe("a linha do Eremita Tartaruga NAO chega vazia no cliente "
				  + "(ela passa pelo `GetString(400)` inteira)", turtle.Length > 0);
		AfirmarSe("o painel do Eremita Tartaruga cita o Mafuba DO LADO PRONTO",
				  NoLadoPronto(turtle, "Mafuba"), turtle);
		AfirmarSe("...e nao o cita do lado dos mudos", !NoLadoMudo(turtle, "Mafuba"), turtle);

		// ============================ A INJECAO: ARRANCA O EFEITO DO MAFUBA ============================
		// O verb continua no catalogo de skills (o extrator nao mudou); o que sai e o CORPO -- que e a
		// unica pergunta que o censo faz. Se o painel fosse uma lista escrita a mao, nada mudaria aqui.
		// ==========================================================================================
		Tecnicas.Registrar("Mafuba", "Mafuba", Modo.NaoPortada, "arrancado pela bancada", aba: "Outros");
		(_, string semEfeito) = PainelDoPacote(quemOlha, "turtle");

		AfirmarSe("INJECAO: sem efeito, o MESMO painel passa a marcar o Mafuba como AINDA MUDO",
				  NoLadoMudo(semEfeito, "Mafuba"), semEfeito);
		AfirmarSe("...e ele sai do lado dos prontos -- o painel e derivado do censo, "
				  + "e nao de uma lista escrita a mao", !NoLadoPronto(semEfeito, "Mafuba"), semEfeito);
		AfirmarSe("...e o resto do kit continua do lado pronto (a injecao mexeu em UM verbo)",
				  NoLadoPronto(semEfeito, "Kame Style"), semEfeito);

		RegistrarTecnicasG9();
		(_, string devolta) = PainelDoPacote(quemOlha, "turtle");
		AfirmarSe("devolvido o efeito, o Mafuba volta sozinho pro lado pronto", NoLadoPronto(devolta, "Mafuba"),
				  devolta);

		// ---- o Guardiao da Terra: os dois lados na MESMA linha ----
		(_, string guardiao) = PainelDoPacote(quemOlha, "guardian");
		AfirmarSe("o painel do Guardiao da Terra cita a Open Dead Zone do lado PRONTO",
				  NoLadoPronto(guardiao, "Open Dead Zone"), guardiao);
		AfirmarSe("...e o Superior Seal do lado MUDO, que e a verdade (falta o motor de magia)",
				  NoLadoMudo(guardiao, "Superior Seal"), guardiao);

		// ---- E O PACOTE SAI SOZINHO NA COROACAO ----
		// Nao adianta a linha estar certa se ninguem a manda: quem pede a lista e o cliente com a aba
		// VAZIA, e quem a empurra e a coroacao (`AnunciarCargo` -> `MandarCargos` pra todo mundo).
		var ouvido = new List<(int Para, NetDataWriter Pacote)>();
		EscutaDeCargos = ouvido;
		try { Outorgar(eremitaDef, quemOlha); }
		finally { EscutaDeCargos = null; }

		AfirmarSe("coroar alguem EMPURRA o painel novo pra quem esta no mundo, sem ninguem pedir",
				  ouvido.Count > 0, $"{ouvido.Count} pacotes");
		AfirmarSe("...e o painel empurrado ja tem o Mafuba do lado pronto",
				  ouvido.Count > 0 && NoLadoPronto(LinhaDe(ouvido[^1].Pacote, "turtle").Da, "Mafuba"),
				  ouvido.Count > 0 ? LinhaDe(ouvido[^1].Pacote, "turtle").Da : "");

		_tronos.Remove("turtle");
		ReconciliarDadiva(quemOlha);
		LimparOSelo();
	}

	/// <summary>A marca que separa as duas metades da linha do painel -- ver `OQueOCargoEntrega`.</summary>
	private const string MarcaDoMudo = "ainda mudo neste servidor:";

	private static bool NoLadoPronto(string linha, string nome)
	{
		int corte = linha.IndexOf(MarcaDoMudo, StringComparison.OrdinalIgnoreCase);
		string prontos = corte < 0 ? linha : linha[..corte];
		return prontos.Contains(nome, StringComparison.OrdinalIgnoreCase);
	}

	private static bool NoLadoMudo(string linha, string nome)
	{
		int corte = linha.IndexOf(MarcaDoMudo, StringComparison.OrdinalIgnoreCase);
		return corte >= 0 && linha[corte..].Contains(nome, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// MANDA O PAINEL PELO CAMINHO DE PRODUCAO E LE OS BYTES DE VOLTA.
	///
	/// O corpo tem `Peer` nulo, entao nada vai pra rede -- e a escuta esta ANTES do `Send`, de
	/// proposito: o que se mede e o pacote MONTADO, que e o mesmo em qualquer destino.
	/// </summary>
	private (int Quantos, string Da) PainelDoPacote(ServerPlayer pl, string chave)
	{
		var ouvido = new List<(int Para, NetDataWriter Pacote)>();
		EscutaDeCargos = ouvido;
		try { MandarCargos(pl); }
		finally { EscutaDeCargos = null; }

		if (ouvido.Count == 0) return (0, "");
		return LinhaDe(ouvido[^1].Pacote, chave);
	}

	/// <summary>
	/// DESMONTA O PACOTE DE CARGOS PELO MESMO CAMINHO QUE O CLIENTE USA -- byte a byte, com os
	/// MESMOS limites de string (`GameClient.cs:1475-1476`).
	///
	/// Os limites nao sao decoracao: `GetString(n)` do LiteNetLib devolve VAZIO quando o texto passa
	/// de `n`. Uma bancada que lesse com limite proprio (ou sem limite) daria verde numa linha que
	/// chega em branco na tela -- que e exatamente o tipo de defeito que so o pacote mostra.
	/// </summary>
	private static (int Quantos, string Da) LinhaDe(NetDataWriter w, string chave)
	{
		var r = new NetDataReader(w.Data, 0, w.Length);
		if ((Protocol.S2C)r.GetByte() != Protocol.S2C.Cargos) return (0, "");

		int n = r.GetByte();
		string achado = "";
		int lidos = 0;
		for (int i = 0; i < n; i++)
		{
			string ch = r.GetString(32);
			r.GetString(32);        // dono
			r.GetString(160);       // o que falta PRA MIM
			r.GetString(200);       // o que o cargo E
			string da = r.GetString(400);   // o que o cargo DA
			lidos++;
			if (string.Equals(ch, chave, StringComparison.OrdinalIgnoreCase)) achado = da;
		}
		return (lidos, achado);
	}

	// =====================================================================
	// 10) O EFEITO NOMEADO, COM O CARGO NO TRONO
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE O CARGO, E NAO O LIVRO ESCRITO A MAO ============================
	/// A familia 2 escreve as skills no livro do sujeito (`ForjarSelador`) e mede o Mafuba. Isso prova o
	/// VERBO, e nao a corrente -- e a corrente e o que este lote conserta: extrator -> `skills.json` ->
	/// `DadivaDeCargo` -> `ReconciliarDadiva` -> `SkillBook` -> `SabeTecnica` -> efeito. Um elo roto no
	/// meio deixa a familia 2 verde e o jogador sem botao, que e literalmente o estado de ontem.
	///
	/// Aqui o unico gesto e a COROACAO (`Outorgar`, o caminho de producao da outorga), e o que se mede
	/// depois e o efeito NOMEADO de cada verbo -- dito por extenso, conferivel contra o DM sem abrir
	/// uma linha de C#:
	///
	///   MAFUBA (`Magic/Sealing.dm:156-190`)
	///       quem lanca perde NOVENTA de vida em CADA membro, na hora do lancamento; o alvo sai do
	///       mundo e passa a viver dentro do pote, amarrado a ele.
	///
	///   OPEN DEAD ZONE (`Magic/Sealing.dm:239-253`)
	///       custa MaxKi/1.1 (90,9% do tanque); a fenda nasce CINCO tiles ao NORTE de quem abriu;
	///       quem cai dentro e selado SEM POTE, com o selo valendo o BP de quem abriu.
	///
	///   CONCEAL POWER (`Stats/BP/Power Control.dm:71-83`)
	///       o BP que o mundo LE vira 5 -- e nao o BP de verdade.
	///
	///   POWER CONTROL (`Stats/BP/Power Control.dm:176-186`)
	///       segurar em 40% faz o BP expresso valer 40% do que valia.
	///
	/// E CADA UM CARREGA A METADE QUE O DERRUBA: antes do cargo o botao NAO existe, e ao perder o
	/// cargo ele some de novo. Afirmacao de um lado so fica verde num sistema morto.
	/// ==================================================================================================
	///
	/// AS DUAS DO SIGILO NAO VEM DE CARGO NENHUM, e dizer isso e parte da prova: elas sao concedidas
	/// pelo DEGRAU 5 do `Basic_Ki_Control` (`niveis.json`, `Mind.dm:281`). O caminho de producao delas e
	/// o `LiberarOKi` -- o mesmo que o verb de admin usa --, e a bancada afirma que os verbos chegaram
	/// pelo canal do NIVEL e nao pelo livro.
	/// </summary>
	private void OEfeitoComOCargoNoTrono()
	{
		GD.Print("[selo] -- 10) O EFEITO NOMEADO, com o cargo NO TRONO (e nao a skill escrita a mao)");

		if (_skills == null) { AfirmarSe("o servidor tem catalogo de skills", false); return; }
		RankDef? eremitaDef = Cargos.Get("turtle");
		RankDef? guardiaoDef = Cargos.Get("guardian");
		if (eremitaDef == null || guardiaoDef == null)
		{
			AfirmarSe("os cargos `turtle` e `guardian` existem", false);
			return;
		}

		OMafubaDoEremita(eremitaDef);
		ADeadZoneDoGuardiao(guardiaoDef);
		OSigiloQueVemDoDEGRAU();
	}

	/// <summary>O EREMITA TARTARUGA: coroa, e o Mafuba passa a existir -- com o preco do DM.</summary>
	private void OMafubaDoEremita(RankDef cargo)
	{
		Vec2 chao = CorredorLivre(20);

		// CORPO SEM NADA NO LIVRO -- o `Forjar` cru, e nao o `ForjarSelador`: aquele ESCREVE as tres
		// skills de selo, que e exatamente o atalho que esta familia existe pra nao usar.
		ServerPlayer eremita = Forjar("Eremita de bancada", chao, bp: 5_000_000);
		eremita.Conta = "bancada_selo_eremita";

		AfirmarSe("antes do cargo, o Eremita NAO sabe o Mafuba (livro vazio, nenhum atalho)",
				  !eremita.Livro.Sabe("/datum/skill/rank/Mafuba") && !SabeTecnica(eremita, "Mafuba"));

		Outorgar(cargo, eremita);
		AfirmarSe("a COROACAO poe a skill no livro pela dadiva (`ReconciliarDadiva`)",
				  eremita.Livro.Sabe("/datum/skill/rank/Mafuba"));
		AfirmarSe("...e o botao passa a existir pro servidor (`SabeTecnica`)",
				  SabeTecnica(eremita, "Mafuba"));

		// ---- o efeito, com o pote e o alvo ----
		Obra pote = PorUmPote(eremita.Zone, chao.X + 2 * ZoneCollision.TileSize, chao.Y);
		ServerPlayer vitima = Forjar("Selado do Eremita",
									 new Vec2(chao.X + 5 * ZoneCollision.TileSize, chao.Y), bp: 900);
		eremita.AlvoId = vitima.Id;

		var antes = eremita.Combate!.Corpo.Partes
			.Where(p => !p.Aninhado && !p.Decepado).Select(p => (p, p.Vida)).ToList();

		UsarHabilidade(eremita, "Mafuba");

		// ============================ EFEITO NOMEADO 1: `SpreadDamage(90)` (`Sealing.dm:188`) ============================
		// NOVENTA em cada membro de fora, na hora do lancamento -- membro de 100 de vida sai com 10.
		//
		// A CONTA SE DIVIDE EM DOIS, e a divisao e do proprio corpo: quem tem DONO (o Reprodutor mora
		// dentro do Abdomen) leva os 90 diretos MAIS os 20% que o pai propaga (`Body.Propagacao`), e
		// 108 num membro de 100 e o membro a zero. A primeira versao desta linha exigia 90 em todos e
		// ficou vermelha com o Mafuba funcionando -- ela media a soma de dois canais como se fosse um.
		// =============================================================================================================
		var soltos = antes.Where(x => string.IsNullOrEmpty(x.p.Dono)).ToList();
		var sobUmPai = antes.Where(x => !string.IsNullOrEmpty(x.p.Dono)).ToList();

		var erradas = soltos.Where(x => Math.Abs((x.Vida - x.p.Vida) - 90) > 0.001).ToList();
		AfirmarSe("EFEITO: quem lanca perde NOVENTA de vida em CADA membro solto, na hora do lancamento",
				  soltos.Count > 0 && erradas.Count == 0,
				  string.Join(", ", erradas.Select(x => $"{x.p.Nome} perdeu {x.Vida - x.p.Vida:0.#}")));
		AfirmarSe("...e o membro que mora DENTRO de outro leva os 90 mais a propagacao do pai",
				  sobUmPai.Count > 0 && sobUmPai.TrueForAll(x => x.Vida - x.p.Vida > 90),
				  string.Join(", ", sobUmPai.Select(x => $"{x.p.Nome} perdeu {x.Vida - x.p.Vida:0.#}")));

		for (int i = 0; i < 200 && !vitima.Selo.Preso; i++) TickDoSelo(0.05);

		// EFEITO NOMEADO 2: o alvo sai do mundo e passa a viver DENTRO do pote.
		AfirmarSe("EFEITO: o alvo sai do mundo e passa a viver no bolso do selo",
				  vitima.Selo.Preso && EhOSelo(vitima.Zone), vitima.Zone.Name);
		AfirmarSe("...amarrado AO POTE que estava a vista (e por isso quebrar o pote o solta)",
				  vitima.Selo.PoteId == pote.Id, $"{vitima.Selo.PoteId} vs {pote.Id}");

		// ---- E A METADE QUE DERRUBA: perder o cargo leva o botao junto ----
		Destronar("turtle", "a bancada devolveu o trono");
		AfirmarSe("perder o cargo TIRA a skill do livro (o `treeshrink` do DM)",
				  !eremita.Livro.Sabe("/datum/skill/rank/Mafuba"));
		AfirmarSe("...e o botao deixa de existir pro servidor", !SabeTecnica(eremita, "Mafuba"));

		LimparOSelo();
	}

	/// <summary>O GUARDIAO DA TERRA: coroa, e a fenda passa a abrir -- com o preco e o lugar do DM.</summary>
	private void ADeadZoneDoGuardiao(RankDef cargo)
	{
		Vec2 chao = CorredorLivre(20);
		ServerPlayer guardiao = Forjar("Guardiao de bancada", chao, bp: 40_000);
		guardiao.Conta = "bancada_selo_guardiao";

		AfirmarSe("antes do cargo, o Guardiao NAO abre a Dead Zone",
				  !SabeTecnica(guardiao, "Open_Dead_Zone"));

		Outorgar(cargo, guardiao);
		AfirmarSe("a COROACAO entrega a `Open Dead Zone` pela dadiva",
				  guardiao.Livro.Sabe("/datum/skill/rank/DeadZone") && SabeTecnica(guardiao, "Open_Dead_Zone"));

		guardiao.Ficha.Ki = guardiao.Ficha.MaxKi;
		double kiAntes = guardiao.Ficha.Ki;
		int portaisAntes = _portais.Count;

		UsarHabilidade(guardiao, "Open_Dead_Zone");

		AfirmarSe("EFEITO: a fenda abre", _portais.Count == portaisAntes + 1);
		if (_portais.Count != portaisAntes + 1) { LimparOSelo(); return; }

		// EFEITO NOMEADO 1: `if(usr.Ki>=MaxKi/1.1)` e `usr.Ki -= MaxKi/1.1` (`Sealing.dm:243`, `:253`).
		AfirmarSe("EFEITO: ela custa MaxKi/1.1 -- 90,9% do tanque, e nao 'quase tudo'",
				  Math.Abs((kiAntes - guardiao.Ficha.Ki) - guardiao.Ficha.MaxKi / 1.1) < 1,
				  $"{kiAntes - guardiao.Ficha.Ki:0.#} de {guardiao.Ficha.MaxKi / 1.1:0.#}");

		// EFEITO NOMEADO 2: `A.loc = locate(usr.x, usr.y+5, usr.z)` (`:250`) -- CINCO tiles ao NORTE.
		PortalDaDeadZone p = _portais[^1];
		AfirmarSe("EFEITO: ela nasce CINCO tiles ao NORTE de quem abriu (e nao a frente dele)",
				  Math.Abs(p.Pos.Y - (chao.Y - 5 * ZoneCollision.TileSize)) < 1
				  && Math.Abs(p.Pos.X - chao.X) < 1, $"{p.Pos.X:0}, {p.Pos.Y:0}");

		// EFEITO NOMEADO 3: quem cai dentro e selado SEM POTE, pelo BP de quem abriu (`:251`).
		// NASCE NO CORREDOR E ANDA ATE A FENDA NA MAO: o `CorredorLivre` so promete chao livre pra
		// DIREITA, e nascer 5 tiles ao norte podia cair em pedra -- a bancada reprovaria pelo mapa.
		ServerPlayer coitado = Forjar("Engolido", chao, bp: 500);
		coitado.Pos = p.Pos;
		for (int i = 0; i < 20 && !coitado.Selo.Preso; i++) TickDoSelo(0.1);

		AfirmarSe("EFEITO: quem cai dentro e selado SEM POTE -- nao ha o que quebrar pra solta-lo",
				  coitado.Selo.Preso && coitado.Selo.PoteId == 0,
				  $"preso={coitado.Selo.Preso} pote={coitado.Selo.PoteId}");
		AfirmarSe("...e o selo vale o BP de quem ABRIU, congelado naquele instante",
				  Math.Abs(coitado.Selo.BpDoSelador - p.BpDeQuemAbriu) < 1,
				  $"{coitado.Selo.BpDoSelador:N0} vs {p.BpDeQuemAbriu:N0}");

		Destronar("guardian", "a bancada devolveu o trono");
		AfirmarSe("perder o cargo tira a Dead Zone junto", !SabeTecnica(guardiao, "Open_Dead_Zone"));

		LimparOSelo();
	}

	/// <summary>
	/// AS DUAS DO SIGILO VEM DO DEGRAU, E NAO DE CARGO -- e a bancada prova o CANAL antes do efeito.
	///
	/// `Basic_Ki_Control` no nivel 5 e o unico dono destes dois verbos no jogo inteiro (`Mind.dm:281`,
	/// hoje no `niveis.json`), e o caminho de producao que chega la e o <see cref="LiberarOKi"/> --
	/// as mesmas duas linhas que o verb de admin roda (`GameServer.Admin.cs:1123-1124`).
	/// </summary>
	private void OSigiloQueVemDoDEGRAU()
	{
		Vec2 chao = CorredorLivre(10);
		ServerPlayer pl = Forjar("Aprendiz de Ki", chao, bp: 100_000);
		pl.Conta = "bancada_selo_sigilo";

		AfirmarSe("antes de destravar o Ki, o sigilo NAO tem botao",
				  !SabeTecnica(pl, "Conceal_Power") && !SabeTecnica(pl, "Power_Control"));

		// O CAMINHO DE PRODUCAO, as duas linhas do verb de admin.
		LiberarOKi(pl);
		pl.Niveis.Aplicar(pl.Ficha);
		pl.Ficha.Statify();

		AfirmarSe("destravado o Ki, os dois verbos chegam PELO DEGRAU de nivel 5, e nao pelo livro",
				  pl.Niveis.VerbosAtivos().Contains("Conceal_Power")
				  && pl.Niveis.VerbosAtivos().Contains("Power_Control"),
				  string.Join(",", pl.Niveis.VerbosAtivos()));
		AfirmarSe("...e nenhuma skill do livro concede os dois (o canal e mesmo o do nivel)",
				  !pl.Livro.Aprendidas.Any(
					  path => _skills!.Get(path) is { } s
							  && (s.Verbos.Contains("Conceal_Power", StringComparer.OrdinalIgnoreCase)
								  || s.Verbos.Contains("Power_Control", StringComparer.OrdinalIgnoreCase))));

		// ---- CONCEAL POWER: o BP que o mundo LE vira 5 ----
		_sigiloPronto.Remove(pl.Id);
		pl.Ficha.Tick(agoraMs: NowMs());
		double lidoAntes = pl.Ficha.expressedBP;
		AfirmarSe("o mundo LE o poder de verdade antes do sigilo", lidoAntes > 5, $"{lidoAntes:N0}");

		UsarHabilidade(pl, "Conceal_Power");
		pl.Ficha.Tick(agoraMs: NowMs());
		AfirmarSe("EFEITO: escondido, o BP que o mundo LE vira CINCO -- e nao o BP de verdade",
				  Math.Abs(pl.Ficha.expressedBP - 5) < 1e-9, $"{pl.Ficha.expressedBP:N0}");

		_sigiloPronto[pl.Id] = 0;
		UsarHabilidade(pl, "Conceal_Power");
		pl.Ficha.Tick(agoraMs: NowMs());
		AfirmarSe("...e desligar devolve o numero de verdade",
				  Math.Abs(pl.Ficha.expressedBP - lidoAntes) < 1, $"{pl.Ficha.expressedBP:N0}");

		// ---- POWER CONTROL: o expresso vira a porcentagem pedida ----
		UsarHabilidade(pl, "Power_Control:40");
		pl.Ficha.Tick(agoraMs: NowMs());
		AfirmarSe("EFEITO: segurar o poder em 40% faz o BP EXPRESSO valer 40% do que valia",
				  Math.Abs(pl.Ficha.expressedBP - lidoAntes * 0.4) < Math.Max(1, lidoAntes * 0.001),
				  $"{pl.Ficha.expressedBP:N0} vs {lidoAntes * 0.4:N0}");

		LimparOSelo();
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// Um corpo de bancada com as tres skills de selo no livro e o degrau do sigilo nos niveis.
	///
	/// USA O `Forjar` DA BANCADA DOS PROJETEIS, com a faixa de id dela, e nao uma faixa propria: e o
	/// mesmo corpo, na mesma zona, e a limpeza tambem e a de la (<see cref="LimparTudoDaBancada"/>).
	/// Uma faixa propria obrigaria a REINDEXAR o corpo depois de o `Forjar` ja o ter posto no mundo
	/// -- que e a chance de deixar um corpo no `_players` com um id e na `ZoneList` com outro.
	/// </summary>
	private ServerPlayer ForjarSelador(string nome, Vec2 onde, double bp)
	{
		ServerPlayer pl = Forjar(nome, onde, bp);

		foreach (string path in new[]
				 {
					 "/datum/skill/rank/Mafuba", "/datum/skill/rank/DeadZone",
					 "/datum/skill/rank/SuperiorSeal",
				 })
			pl.Livro.Dar(path);

		// `Conceal_Power` e `Power_Control` vem por DEGRAU (Basic Ki Control nivel 5, `Mind.dm:281`),
		// e nao por skill comprada -- e por isso o livro sozinho nao basta.
		var save = new NivelSave();
		save.Skills["/datum/skill/mind/Basic_Ki_Control"] = [5, 0];
		pl.Niveis.DoSave(save);

		return pl;
	}

	/// <summary>Assenta um Pote Selante de verdade, pelo mesmo caminho de dado que o jogador usa.</summary>
	private Obra PorUmPote(ZoneKey zona, float x, float y)
	{
		var pote = new Obra
		{
			Id = _proximaObraId++,
			Tipo = TipoDoPote,
			X = x,
			Y = y,
			DonoConta = "bancada_selo",
			DonoNome = "bancada",
			ErguidaEm = NowMs(),
			ArmaduraMax = 1_000,
			Armadura = 1_000,
		};
		pote.PorZona(zona);
		_noChao.Add(pote);
		return pote;
	}

	/// <summary>Tira do mundo tudo que esta bancada pos nele.</summary>
	private void LimparOSelo()
	{
		_fitas.Clear();
		_portais.Clear();

		foreach (int id in _players.Keys.Where(k => k >= IdBaseDeProjetil).ToList())
			_sigiloPronto.Remove(id);
		LimparTudoDaBancada();

		foreach (Obra o in _noChao.Where(o => o.DonoConta == "bancada_selo").ToList())
			_noChao.Remove(o);
	}
}
