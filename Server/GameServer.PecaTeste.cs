using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--pecateste` -- O MEMBRO QUE CAI DO CORPO, COM DOIS CORPOS DE VERDADE.
///
/// ============================ POR QUE ELA EXISTE, DEPOIS DE JA HAVER DUAS ============================
/// Ja havia a `--diagdecalque` (a folha carrega, as dez pecas acham o recorte) e a
/// `--diagmembroperdido` (a FOTO da amputacao numa briga). As duas pulam o mesmo pedaco: entre o
/// `MeleeResolver` decidir que um braco saiu e o desenho aparecer na tela ha um PACOTE, e pacote que
/// saiu no fio nao volta. Nenhuma das duas le o fio.
///
/// Esta le. Ela nao chama efeito nenhum e nao desenha nada: dois corpos brigam pelo caminho de
/// producao (<see cref="Atacar"/>, o mesmo que o pacote do jogador aciona), e o que se afirma e o
/// que saiu dos dois canais -- o `S2C.Hit` (de onde o jato de sangue nasce) e o `S2C.Decalque` (de
/// onde a peca no chao nasce), lidos com os MESMOS leitores do cliente.
/// ====================================================================================================
///
/// ============================ AS CINCO FAMILIAS, E COMO CADA UMA REPROVA ============================
///  1. O JATO SAI, UMA VEZ -- e os DOIS CONTRA-EXEMPLOS. Reprova se o bit `Decepou` nao viajar (o
///     jato nunca sai), se viajar em soco que so machucou (o jato sai em todo golpe), se a metade
///     LETAL do gate sumir (treinar com um amigo passa a arrancar bracos), se a plateia nao receber
///     o bit (o braco cairia do nada pra quem assiste) ou se o membro ja perdido voltar a cair.
///  2. A PECA CERTA. Reprova se o braco e a perna cairem como a MESMA arte -- que e o estado em que
///     uma tabela inteira trocada fica verde em qualquer contagem -- ou se a cascata parar no membro
///     de fora e esquecer o que estava dentro (mao, pe).
///  3. PERTO DE QUEM PERDEU. Reprova se a peca nascer longe do corpo (o `d.Pos` trocado pelo do
///     atacante ou pela origem do mapa) ou se todas cairem no MESMO ponto -- o sintoma de o sorteio
///     do espalhamento ter sumido, que transforma um massacre numa peca so desenhada varias vezes.
///  4. NAO ACUMULA SEM TETO. Reprova se peca sair de golpe que nao amputou: dai o chao passa a
///     encher com a DURACAO da briga e nao com o que aconteceu nela.
///  5. O PACOTE NAO LEVA MAIS NADA. Reprova se o `S2C.Decalque` engordar (vida, dano, dono) ou se o
///     relato da PLATEIA passar a carregar o nome do membro -- que e ficha alheia em todo soco.
/// ==================================================================================================
///
/// O QUE ELA NAO ALCANCA, e mora na `--diagdecalque` (cliente): o JATO DESENHADO (o node nascendo
/// uma vez, com a arte carregada, seguindo o corpo), o TETO de 32 pecas vivas no chao e o rastro da
/// agua. Aqui nao ha tela.
///
///     Godot --headless --path . --host --rede 7974 --pecateste
/// </summary>
public partial class GameServer
{
	private int _pcOk, _pcFalhou;

	/// <summary>
	/// OS RELATOS DE GOLPE QUE SAIRAM, com o FIO de cada um e a marca de qual dos dois pacotes e.
	/// `Cheio` = o dos dois envolvidos (leva dano e membro); falso = o MAGRO, o da plateia.
	///
	/// Guarda bytes e nao a struct pelo mesmo motivo da <see cref="EscutaDeDecalques"/>: a pergunta
	/// desta bancada e sobre o que a plateia RECEBE. Ler `cheio.Decepou` responderia sobre uma
	/// variavel do servidor, que continua verdadeira mesmo se o `Write` deixar o bit pra tras.
	/// Nula em jogo -- uma comparacao contra null por golpe anunciado.
	/// </summary>
	internal static List<(bool Cheio, byte[] Fio)>? EscutaDeGolpes;

	private void AfirmarPeca(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _pcOk++; GD.Print($"[peca]   OK    {oque}   {detalhe}"); return; }
		_pcFalhou++;
		GD.PrintErr($"[peca]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaPeca()
	{
		_pcOk = _pcFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[peca] ================ O MEMBRO QUE CAI DO CORPO ================");

		AfirmarPeca("a zona da bancada tem colisao carregada", _pjMapa != null);

		try
		{
			OJatoEOsContraExemplos();
			APecaCerta();
			PertoDeQuemPerdeu();
			NaoAcumulaSemTeto();
			OPacoteNaoLevaMaisNada();
		}
		finally
		{
			EscutaDeGolpes = null;
			EscutaDeDecalques = null;
			LimparTudoDaBancada();
		}

		GD.Print($"[peca] ================ {_pcOk} passaram, {_pcFalhou} falharam ================");
	}

	// =====================================================================
	// OS DOIS CORPOS
	// =====================================================================
	/// <summary>
	/// UM CARRASCO E UMA VITIMA, colados e se encarando -- o cenario das cinco familias.
	///
	/// ============================ A DIFERENCA DE PODER E O BOTAO DA BANCADA ============================
	/// A amputacao do original tem duas condicoes, e a segunda le o membro DEPOIS do dano
	/// (`MeleeResolver.AplicarNoMembro`: `Ferir(...)` e so entao `if (letal && membro.Vida <= 0 ...)`).
	/// Ou seja: um golpe forte o bastante arranca o membro NO PROPRIO GOLPE -- nao e preciso preparar
	/// estado nenhum, e a primeira versao desta bancada zerava membros a mao por nao ter lido isso.
	///
	/// Entao o unico botao e o PODER: `bp` alto de um lado arranca a cada soco que encosta; parelho,
	/// o soco machuca e nao arranca. As duas situacoes sao de jogo, e e por isso que o contra-exemplo
	/// da familia 1 nao precisa de nenhum atalho.
	/// ================================================================================================
	///
	/// A GUARDA SAI, pelo mesmo motivo da `--punhoteste`: bloqueio vira contra-ataque, o contra-ataque
	/// anuncia OUTRO golpe, e um relato a mais no meio da contagem faria a familia 1 medir a bancada.
	/// </summary>
	private (ServerPlayer A, ServerPlayer D) DuplaDaPeca(string rotulo, double bpA, double bpD)
	{
		Vec2 chao = CorredorLivre(24);
		ServerPlayer a = Forjar("Carrasco" + rotulo, chao, bpA);
		ServerPlayer d = Forjar("Vitima" + rotulo, chao + new Vec2(ZoneCollision.TileSize * 0.9f, 0), bpD);
		a.Facing = Facing.East;
		d.Facing = Facing.West;
		a.AlvoId = d.Id;
		a.Combate.Letal = true;
		d.Combate.Bloqueando = false;

		// ============================ A VITIMA NAO MORRE, E ISSO E GANCHO DE PRODUCAO ============================
		// `NegarMorte` e a porta unica do "este corpo nao morre agora" (quem se pendura nela em jogo e
		// a Aura of Destruction). A bancada precisa dela porque um golpe capaz de arrancar um braco
		// tambem zera um TORSO quando o sorteio o pega, e `Resolver` devolve vazio pra corpo morto:
		// sem isto a briga acabava no terceiro soco e as familias 4 e 5 mediam silencio.
		//
		// Ela NAO encosta em nada do que se mede: o membro continua sendo sorteado, ferido, decepado e
		// anunciado pelo mesmo caminho. O que ela impede e a briga terminar antes da medicao.
		// ======================================================================================================
		d.Combate.NegarMorte = _ => true;
		return (a, d);
	}

	/// <summary>Um carrasco que arranca o membro em qualquer soco que encoste.</summary>
	private (ServerPlayer A, ServerPlayer D) DuplaForte(string rotulo)
		=> DuplaDaPeca(rotulo, bpA: 5_000_000, bpD: 5_000);

	/// <summary>Dois iguais: o soco machuca e NAO arranca. E o contra-exemplo da familia 1.</summary>
	private (ServerPlayer A, ServerPlayer D) DuplaParelha(string rotulo)
		=> DuplaDaPeca(rotulo, bpA: 5_000, bpD: 5_000);

	/// <summary>
	/// UM GOLPE, sem esperar a cadencia. `Recarga` e `AtaqueAte` sao o relogio do soco
	/// (<see cref="CombatState.PodeAtacar"/>) e num teste que roda dentro de um tique so eles
	/// recusariam tudo menos o primeiro. E o mesmo `_prontoG3.Remove` que a `--punhoteste` faz --
	/// zerar relogio nao muda o que o golpe FAZ.
	/// </summary>
	private void SocarUmaVez(ServerPlayer a)
	{
		a.Combate.Recarga = 0;
		a.AtaqueAte = 0;
		Atacar(a, Protocol.Golpe.Leve);
	}

	/// <summary>
	/// Devolve todo membro AINDA PRESENTE a vida cheia -- e o membro decepado NAO volta, que e a
	/// regra do jogo (amputacao e permanente).
	///
	/// Chamado antes de cada soco pra que o dano nao se acumule entre eles: sem isso o segundo soco
	/// arrancaria um membro por causa do primeiro, e a familia 1 nao saberia dizer qual golpe foi o
	/// responsavel pelo bit que ela esta contando.
	/// </summary>
	private static void Curar(ServerPlayer d)
	{
		foreach (BodyPart p in d.Combate.Corpo.Partes)
			if (!p.Decepado) p.Vida = p.VidaMax;
		d.Combate.F.KO = false;
		d.Combate.NocauteRestante = 0;
		d.Combate.SincronizarVida();
	}

	// =====================================================================
	// A LEITURA DO FIO -- os mesmos leitores do cliente
	// =====================================================================
	/// <summary>
	/// Le um `S2C.Hit` do jeito que o `GameClient` le: pula o byte do id e chama o
	/// <see cref="Protocol.HitEvent.Read"/> de producao. Reescrever a leitura aqui seria a segunda
	/// resposta pra "o que este pacote significa", e as duas concordariam ate o dia em que o formato
	/// mudasse -- que e o unico dia em que isto importa.
	/// </summary>
	private static Protocol.HitEvent LerGolpe(byte[] fio)
	{
		var r = new NetDataReader(fio);
		r.GetByte();                       // o id do pacote (`Protocol.Begin`)
		return Protocol.HitEvent.Read(r);
	}

	/// <summary>O `S2C.Decalque` lido byte a byte como o `GameClient` le. Ver o ramo dele la.</summary>
	private static (Protocol.Decal Tipo, Vec2 Onde, Facing Dir, PecaDeCorpo Peca, int Sobrou) LerDecalque(byte[] fio)
	{
		var r = new NetDataReader(fio);
		r.GetByte();
		var tipo = (Protocol.Decal)r.GetByte();
		Vec2 onde = r.GetVec();
		var dir = (Facing)r.GetByte();
		PecaDeCorpo peca = tipo == Protocol.Decal.Membro ? (PecaDeCorpo)r.GetByte() : PecaDeCorpo.Nenhuma;
		// O QUE SOBROU DEPOIS DO ULTIMO CAMPO. E a familia 5 inteira: um pacote que engordou com
		// vida, dano ou dono nao muda nenhum campo acima -- ele deixa bytes pra tras.
		return (tipo, onde, dir, peca, r.AvailableBytes);
	}

	/// <summary>As pecas que sairam no fio nesta escuta, na ordem.</summary>
	private static List<(Vec2 Onde, PecaDeCorpo Peca, int Sobrou)> PecasNoFio()
	{
		var saiu = new List<(Vec2, PecaDeCorpo, int)>();
		foreach ((_, Protocol.Decal tipo, byte[] fio) in EscutaDeDecalques ?? [])
		{
			if (tipo != Protocol.Decal.Membro) continue;
			(_, Vec2 onde, _, PecaDeCorpo peca, int sobrou) = LerDecalque(fio);
			saiu.Add((onde, peca, sobrou));
		}
		return saiu;
	}

	/// <summary>Os relatos do canal CHEIO (o dos dois envolvidos), ja lidos.</summary>
	private static List<Protocol.HitEvent> RelatosCheios()
		=> [.. (EscutaDeGolpes ?? []).Where(g => g.Cheio).Select(g => LerGolpe(g.Fio))];

	/// <summary>Quantos relatos marcaram amputacao, separados por canal.</summary>
	private static (int Cheios, int Magros) GolpesQueDeceparam()
	{
		int cheios = 0, magros = 0;
		foreach ((bool cheio, byte[] fio) in EscutaDeGolpes ?? [])
		{
			if (!LerGolpe(fio).Decepou) continue;
			if (cheio) cheios++; else magros++;
		}
		return (cheios, magros);
	}

	/// <summary>Liga as duas escutas do zero. Toda medicao comeca daqui.</summary>
	private static void Escutar()
	{
		EscutaDeGolpes = [];
		EscutaDeDecalques = [];
	}

	/// <summary>
	/// SOCA ATE ARRANCAR ALGUMA COISA, com a escuta LIMPA a cada soco -- entao o que sobra nela e o
	/// golpe da amputacao e mais nada.
	///
	/// Isolar o soco e o que torna a familia 1 honesta: "um bit por amputacao" contado sobre uma
	/// briga inteira ficaria verde com dois bits num soco e nenhum no seguinte.
	/// </summary>
	/// <param name="membro">Se dado, insiste ate cair um membro com este nome.</param>
	/// <returns>Quantos socos foram precisos, ou 0 se nada saiu.</returns>
	private int SocarAteAmputar(ServerPlayer a, ServerPlayer d, string zona, string? membro = null, int teto = 300)
	{
		a.Combate.ZonaMirada = zona;
		a.Combate.Letal = true;

		for (int i = 1; i <= teto; i++)
		{
			Curar(d);
			Escutar();
			SocarUmaVez(a);

			Protocol.HitEvent? amputou = RelatosCheios().Find(h => h.Decepou) is { Decepou: true } h2 ? h2 : null;
			if (amputou == null) continue;
			if (membro != null && amputou.Value.Membro != membro) continue;
			return i;
		}
		return 0;
	}

	// =====================================================================
	// 1) O JATO SAI, UMA VEZ -- E OS DOIS CONTRA-EXEMPLOS
	// =====================================================================
	/// <summary>
	/// O jato de sangue do cliente nasce de UM bit: `HitEvent.Decepou` (ver `World.AoGolpe`). Aqui se
	/// mede esse bit no fio, nos dois canais, e -- o que importa tanto quanto -- se mede que ele NAO
	/// esta la quando nao houve amputacao.
	///
	/// SEM OS CONTRA-EXEMPLOS, um campo cravado em `true` passaria nesta bancada inteira e o jogo
	/// sairia jorrando sangue em todo soco -- e foi exatamente isso que a injecao de defeito mostrou:
	/// trocar `Decepou = r.Decepou` por `r.Encostou` deixa 26 das 30 provas VERDES, e sao os
	/// contra-exemplos que ficam vermelhos.
	///
	/// E sao DOIS porque o pedido do DM tem duas metades, e cada uma quebra sozinha: o membro tem que
	/// ter cedido (contra-exemplo A) E o golpe tem que ser letal (contra-exemplo B).
	/// </summary>
	private void OJatoEOsContraExemplos()
	{
		GD.Print("[peca] -- 1) O BIT QUE ACENDE O JATO, E OS SOCOS QUE NAO O ACENDEM");

		// ---------- CONTRA-EXEMPLO A: SOCOS QUE ACERTAM E NAO ARRANCAM ----------
		(ServerPlayer fraco, ServerPlayer duro) = DuplaParelha("Fraco");
		fraco.Combate.ZonaMirada = "bracos";
		fraco.Combate.Letal = true;
		Escutar();
		for (int i = 0; i < 200; i++) { Curar(duro); SocarUmaVez(fraco); }

		List<Protocol.HitEvent> relatos = RelatosCheios();
		int acertos = relatos.Count(h => h.Desfecho is (byte)Desfecho.Acertou or (byte)Desfecho.Critico);
		double maior = relatos.Count > 0 ? relatos.Max(h => h.Dano) : 0;
		(int cheios, int magros) = GolpesQueDeceparam();

		AfirmarPeca("entre dois parelhos os socos ACERTAM (a bancada nao vai ficar verde por briga nenhuma)",
					acertos > 0, $"{acertos} acertos em {relatos.Count} relatos, maior dano {maior:0.#}");
		AfirmarPeca("...e nenhum deles marca amputacao em canal nenhum",
					cheios == 0 && magros == 0, $"{cheios} cheios / {magros} magros");
		AfirmarPeca("...e nenhuma peca cai no chao por causa deles",
					PecasNoFio().Count == 0, $"{PecasNoFio().Count} pecas");

		// ---------- CONTRA-EXEMPLO B: PODER DE SOBRA, MAS O GOLPE NAO E LETAL ----------
		// ============================ O QUE A INJECAO DE DEFEITO MOSTROU AQUI ============================
		// "Treinar com um amigo nao arranca braco" e sustentado por DUAS guardas, e elas sao
		// REDUNDANTES entre si: o `letal &&` do gate de amputacao (`MeleeResolver.AplicarNoMembro`) e o
		// PISO do `Body.Ferir` (golpe nao-letal nunca leva o membro abaixo do limiar de quebra).
		//
		// Apagar UMA das duas nao muda nada -- a bancada foi rodada com cada uma removida e ficou
		// verde nas duas vezes, corretamente: o comportamento do jogo nao mudou. Ela so fica vermelha
		// quando as DUAS caem, e foi isso que se mediu. Vale saber: quem mexer numa delas achando que
		// e "a" protecao vai encontrar a outra segurando, e nenhum teste vai avisar.
		// ==============================================================================================
		(ServerPlayer bruto, ServerPlayer vitima) = DuplaForte("NaoLetal");
		bruto.Combate.ZonaMirada = "bracos";
		bruto.Combate.Letal = false;
		Escutar();
		for (int i = 0; i < 60; i++) { Curar(vitima); SocarUmaVez(bruto); }

		(cheios, magros) = GolpesQueDeceparam();
		bool doeu = RelatosCheios().Exists(h => h.Dano > 0);
		AfirmarPeca("o mesmo poder com o golpe NAO letal machuca e nao arranca nada",
					cheios == 0 && magros == 0 && PecasNoFio().Count == 0 && doeu
					&& vitima.Combate.Corpo.Perdidos().Count() == 0,
					$"{cheios}/{magros} bits, {PecasNoFio().Count} pecas, doeu={doeu}");

		// ---------- E AGORA A AMPUTACAO, ISOLADA NUM SOCO ----------
		(ServerPlayer carrasco, ServerPlayer alvo) = DuplaForte("Letal");
		int socos = SocarAteAmputar(carrasco, alvo, "bracos");
		AfirmarPeca("com o golpe LETAL e poder de sobra, o membro SAI", socos > 0, $"{socos} socos");

		(cheios, magros) = GolpesQueDeceparam();
		// UMA VEZ, E NOS DOIS CANAIS. Um relato cheio (pros dois que brigaram) e um magro (pra
		// plateia) -- e sao os dois `Peer.Send` do `AnunciarGolpe`, nao dois eventos.
		AfirmarPeca("o bit do jato viaja UMA vez, e nos DOIS canais (envolvidos e plateia)",
					cheios == 1 && magros == 1, $"{cheios} cheios / {magros} magros");

		// O JATO E PUBLICO. Esta e a linha que sustenta o desenho: quem so assiste tem que ver o
		// sangue jorrar. Se o bit passar a viajar so no pacote cheio, a plateia veria o braco cair do
		// nada -- e o `Membro`, que e ficha, continua sendo o unico campo que ela nao recebe.
		AfirmarPeca("...e a PLATEIA recebe o bit (o jato nao e privilegio de quem brigou)", magros == 1);

		// ---------- DEPOIS DE CAIR, NAO CAI DE NOVO ----------
		// O membro decepado sai do sorteio (`Body.Sortear` pula `Decepado`). Sem isso o mesmo braco
		// jorraria sangue a cada soco -- um jato por golpe num corpo que ja nao tem o que perder.
		string caiu = RelatosCheios().Find(h => h.Decepou).Membro ?? "";
		Escutar();
		for (int i = 0; i < 60; i++) { Curar(alvo); SocarUmaVez(carrasco); }
		int repetiu = RelatosCheios().Count(h => h.Decepou && h.Membro == caiu);
		AfirmarPeca($"e o membro que ja caiu ('{caiu}') nao volta a cair em 60 socos", repetiu == 0,
					$"{repetiu} repeticoes");

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 2) A PECA CERTA -- BRACO E PERNA NAO DESENHAM A MESMA COISA
	// =====================================================================
	/// <summary>
	/// A UNICA FAMILIA QUE PEGA UMA TABELA INTEIRA TROCADA. Contar pecas fica verde com todas
	/// erradas; ate conferir "a peca do braco e `Braco`" fica verde se `PecaDe` devolver sempre a
	/// mesma coisa. O que nao fica verde e exigir que DOIS membros diferentes produzam artes
	/// diferentes -- e e por isso que esta bancada precisa arrancar um braco E uma perna.
	///
	/// UM CORPO NOVO PRA CADA UM: no mesmo corpo, insistir num braco depois de ja ter arrancado uma
	/// perna mediria um corpo que ja nao e o mesmo -- e o sorteio, que ja perdeu candidatos, deixaria
	/// de ser o de uma briga de verdade.
	/// </summary>
	private void APecaCerta()
	{
		GD.Print("[peca] -- 2) BRACO CAI COMO BRACO, PERNA CAI COMO PERNA");

		(ServerPlayer a1, ServerPlayer d1) = DuplaForte("Braco");
		int socos = SocarAteAmputar(a1, d1, "bracos", "Braco esquerdo");
		List<PecaDeCorpo> doBraco = [.. PecasNoFio().Select(p => p.Peca)];
		AfirmarPeca("o BRACO esquerdo arrancado poe DUAS pecas no chao (a cascata leva a mao junto)",
					socos > 0 && doBraco.Count == 2, $"{socos} socos, [{string.Join(", ", doBraco)}]");
		AfirmarPeca("...e sao o BRACO e a MAO, nessa ordem (o membro de fora primeiro)",
					doBraco.Count == 2 && doBraco[0] == PecaDeCorpo.Braco && doBraco[1] == PecaDeCorpo.Mao,
					string.Join(", ", doBraco));

		(ServerPlayer a2, ServerPlayer d2) = DuplaForte("Perna");
		socos = SocarAteAmputar(a2, d2, "pernas", "Perna esquerda");
		List<PecaDeCorpo> daPerna = [.. PecasNoFio().Select(p => p.Peca)];
		AfirmarPeca("a PERNA esquerda arrancada poe a PERNA e o PE no chao",
					socos > 0 && daPerna.Count == 2
					&& daPerna[0] == PecaDeCorpo.Perna && daPerna[1] == PecaDeCorpo.Pe,
					$"{socos} socos, [{string.Join(", ", daPerna)}]");

		// A LINHA DO ROTEIRO DO DONO. Se as duas listas se encostarem, a tabela esta errada -- e
		// nenhuma contagem, nenhum "a folha carregou" e nenhuma foto sozinha teria dito isso.
		AfirmarPeca("e as duas NAO se encostam: braco e perna nao desenham a mesma arte",
					doBraco.Count > 0 && daPerna.Count > 0 && !doBraco.Intersect(daPerna).Any(),
					$"[{string.Join(",", doBraco)}] x [{string.Join(",", daPerna)}]");

		// A ARMADILHA DA PERNA, no fio e nao so na tabela: `PecaDeCorpo.Perna` e o recorte "limb" do
		// original, e e esse simbolo que o cliente le. No dia em que alguem "consertar" o nome, a peca
		// deixa de achar recorte e some da tela sem erro nenhum.
		AfirmarPeca("...e o simbolo que viaja e o da PERNA (o `Limb` do DM), nao um recorte de perna",
					daPerna.Contains(PecaDeCorpo.Perna)
					&& Body.PecaDe("Perna esquerda") == PecaDeCorpo.Perna);

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 3) PERTO DE QUEM PERDEU
	// =====================================================================
	/// <summary>
	/// O `rand(-32,32)` do original (`mobparts.dm:404-405`) em volta do CORPO DA VITIMA -- e a
	/// pergunta e por que ele e do servidor. Sorteado no cliente, cada tela veria o braco num lugar
	/// diferente; e cenario, e cenario que discorda entre telas e a primeira coisa que alguem
	/// fotografa achando que e bug.
	///
	/// A POSICAO E LIDA ANTES DO SOCO de proposito: o empurrao roda DEPOIS do anuncio (ver
	/// `Atacar`), entao a vitima ja pode ter andado quando a chamada volta -- e a peca ficou onde ela
	/// ESTAVA. Ler depois faria a bancada reprovar um acerto perfeito.
	/// </summary>
	private void PertoDeQuemPerdeu()
	{
		GD.Print("[peca] -- 3) A PECA CAI EM VOLTA DO CORPO, E ESPALHADA");

		int t = ZoneCollision.TileSize;
		var relativos = new List<Vec2>();
		int amputacoes = 0;

		// SEIS CORPOS, cada um perdendo ate os quatro membros que o sorteio alcanca. O espalhamento e
		// um sorteio: uma amostra de duas pecas nao distingue "espalhou" de "caiu por acaso em dois
		// pontos", e e por isso que aqui ha dezenas.
		for (int k = 0; k < 6; k++)
		{
			(ServerPlayer a, ServerPlayer d) = DuplaForte($"Perto{k}");
			foreach (string zona in new[] { "bracos", "pernas", "bracos", "pernas" })
			{
				a.Combate.ZonaMirada = zona;
				a.Combate.Letal = true;

				for (int i = 0; i < 120; i++)
				{
					Curar(d);
					Escutar();
					Vec2 antes = d.Pos;              // ver o cabecalho: ANTES, por causa do empurrao
					SocarUmaVez(a);

					List<(Vec2 Onde, PecaDeCorpo Peca, int Sobrou)> pecas = PecasNoFio();
					if (pecas.Count == 0) continue;

					amputacoes++;
					foreach ((Vec2 onde, _, _) in pecas)
						relativos.Add(new Vec2(onde.X - antes.X, onde.Y - antes.Y));
					break;
				}
			}
		}

		AfirmarPeca("a bancada arrancou membros suficientes pra medir o espalhamento",
					amputacoes >= 12, $"{amputacoes} amputacoes, {relativos.Count} pecas");

		int fora = relativos.Count(p => Math.Abs(p.X) > t || Math.Abs(p.Y) > t);
		AfirmarPeca($"toda peca caiu dentro de UM TILE ({t} px) de onde o corpo estava", fora == 0,
					$"{fora} fora de {relativos.Count}");

		// ============================ EM VOLTA DE QUEM PERDEU, E NAO DE QUEM BATEU ============================
		// Conferido pela MEDIA e nao peca a peca, e a razao e geometrica: pra socar, os dois corpos
		// estao a 28,8 px um do outro, e o espalhamento e de +-32 px. Peca a peca as duas hipoteses
		// ("nasceu na vitima" e "nasceu no carrasco") se sobrepoem quase inteiras -- uma conferencia
		// individual passaria com o `d.Pos` trocado pelo do atacante, que e exatamente o defeito.
		//
		// Na MEDIA elas se separam: o sorteio e simetrico, entao o centro das dezenas de pecas cai em
		// cima de quem perdeu (media ~0) ou em cima de quem bateu (media ~28,8 px no X). Com ~48
		// amostras o erro tipico da media e ~2,7 px, e o limite de 12 px fica a quatro desvios das
		// duas respostas.
		// ===================================================================================================
		double mediaX = relativos.Count > 0 ? relativos.Average(p => p.X) : 999;
		double mediaY = relativos.Count > 0 ? relativos.Average(p => p.Y) : 999;
		AfirmarPeca("...e o CENTRO das pecas cai em cima de quem perdeu, nao de quem bateu",
					Math.Abs(mediaX) < 12 && Math.Abs(mediaY) < 12,
					$"media ({mediaX:0.#}, {mediaY:0.#}) px -- o carrasco esta a "
					+ $"{ZoneCollision.TileSize * 0.9f:0.#} px no X");

		int distintos = relativos.Select(p => $"{p.X:0.##},{p.Y:0.##}").Distinct().Count();
		AfirmarPeca("...e ESPALHADA: as pecas nao caem todas no mesmo ponto do corpo",
					relativos.Count > 0 && distintos > relativos.Count / 2,
					$"{distintos} pontos distintos em {relativos.Count} pecas");

		// E O SORTEIO ANDA NOS DOIS EIXOS. Um `rand` que tivesse sobrado so no X daria pecas
		// enfileiradas numa linha -- espalhamento pela metade, que a contagem acima nao distingue.
		bool doisEixos = relativos.Exists(p => Math.Abs(p.X) > 1) && relativos.Exists(p => Math.Abs(p.Y) > 1);
		AfirmarPeca("...nos DOIS eixos (o sorteio nao ficou so no X)", doisEixos);

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 4) NUMA BRIGA LONGA, NAO ACUMULA SEM TETO
	// =====================================================================
	/// <summary>
	/// O TETO DE DESENHO E DO CLIENTE (32 pecas vivas, `Decalques.MaxPecas`) e e la que ele se mede.
	/// O que se afirma AQUI e a FONTE, que e o teto que importa: o servidor so emite peca quando um
	/// membro SAI, e um corpo tem um numero finito de membros -- entao o chao de uma briga cresce com
	/// o que aconteceu nela e para sozinho, sem depender de nenhum teto do outro lado.
	///
	/// Sem esta familia, um `SoltarPecas` que emitisse por golpe (ou que esquecesse de limpar a lista
	/// entre golpes -- o modo de falhar mais provavel, ja que `PecasCaidas` nasce nula justamente pra
	/// economizar isso) encheria o chao em segundos e o teto do cliente ESCONDERIA o defeito: as pecas
	/// continuariam aparecendo, so que as certas seriam despejadas pelas erradas.
	/// </summary>
	private void NaoAcumulaSemTeto()
	{
		GD.Print("[peca] -- 4) A PECA VEM DA AMPUTACAO, NAO DO SOCO");

		(ServerPlayer a, ServerPlayer d) = DuplaForte("Longa");

		Escutar();
		string[] zonas = ["bracos", "pernas"];
		for (int i = 0; i < 400; i++)
		{
			a.Combate.ZonaMirada = zonas[i % 2];
			Curar(d);
			SocarUmaVez(a);
		}

		List<(Vec2 Onde, PecaDeCorpo Peca, int Sobrou)> pecas = PecasNoFio();
		int membros = RelatosCheios().Count(h => h.Decepou);
		int socos = RelatosCheios().Count;

		int perdidas = d.Combate.Corpo.Perdidos().Count();

		AfirmarPeca("400 socos numa briga longa arrancaram os membros que dava pra arrancar",
					membros > 0, $"{membros} membros em {socos} relatos");

		// ============================ UMA PECA POR PARTE PERDIDA, E NAO DUAS POR MEMBRO ============================
		// A primeira versao desta linha exigia `2 x membros`, e a bancada a reprovou com 9 pecas pra 5
		// membros -- estava CERTA a bancada e errada a expectativa. O braco leva a mao e a perna leva o
		// pe, mas o REPRODUTOR (`Vitalidade.Membro`, `isnested = 0` no original, `mobparts.dm`) nao tem
		// nada dentro e cai sozinho.
		//
		// A conta honesta e esta: tudo o que saiu do corpo apareceu no chao, e nada alem disso. Ela
		// vale pro braco, pra perna, pro reprodutor e pro rabo do Saiyajin sem precisar saber de cor
		// quantas partes cada um leva junto.
		// ========================================================================================================
		AfirmarPeca("...e o chao ganhou UMA peca por parte que saiu do corpo -- nem uma a mais, nem a menos",
					pecas.Count == perdidas, $"{pecas.Count} pecas pra {perdidas} partes perdidas");
		AfirmarPeca("...enquanto os socos foram MUITOS mais (a peca nao segue o soco)",
					socos > pecas.Count * 10, $"{socos} socos, {pecas.Count} pecas");

		// O TETO DA FONTE: os CINCO membros que o sorteio alcanca -- dois bracos, duas pernas e o
		// reprodutor (mao, pe, cerebro e visceras sao ANINHADOS e so caem na cascata). Depois deles a
		// briga pode durar o que for que o chao nao ganha mais nada.
		AfirmarPeca("o corpo tem CINCO membros arrancaveis (2 bracos, 2 pernas e o reprodutor), e foram esses",
					membros == 5 && perdidas == 9, $"{membros} arrancados, {perdidas} partes perdidas");

		Escutar();
		for (int i = 0; i < 300; i++) { Curar(d); SocarUmaVez(a); }
		AfirmarPeca("...e 300 socos DEPOIS disso nao poem mais nada no chao",
					PecasNoFio().Count == 0 && RelatosCheios().Count > 0,
					$"{PecasNoFio().Count} pecas em {RelatosCheios().Count} relatos");

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 5) O PACOTE NAO LEVA MAIS NADA
	// =====================================================================
	/// <summary>
	/// A peca e informacao PUBLICA, e isso e deliberado: amputacao ja viaja pra zona inteira nos
	/// quatro bits de membro arrancado do `S2C.Feridas`, e um braco no chao e a mesma informacao com
	/// outro desenho. O que ela nao pode levar e ficha -- vida, dano, de quem era o corpo.
	///
	/// Medido por TAMANHO e nao so por campo: um pacote que engordou nao muda nenhum campo lido
	/// acima, ele deixa bytes pra tras. E o unico jeito de a bancada notar um campo que ela nao sabe
	/// que existe.
	/// </summary>
	private void OPacoteNaoLevaMaisNada()
	{
		GD.Print("[peca] -- 5) O QUE VAI NO FIO, E SO ISSO");

		(ServerPlayer a, ServerPlayer d) = DuplaForte("Fio");
		int socos = SocarAteAmputar(a, d, "bracos", "Braco esquerdo");
		AfirmarPeca("houve amputacao pra medir", socos > 0, $"{socos} socos");

		List<(Vec2 Onde, PecaDeCorpo Peca, int Sobrou)> pecas = PecasNoFio();
		AfirmarPeca("o pacote da peca acaba no byte do recorte: nada sobra depois dele",
					pecas.Count > 0 && pecas.TrueForAll(p => p.Sobrou == 0),
					string.Join(" ", pecas.Select(p => $"{p.Peca}+{p.Sobrou}B")));

		// O TAMANHO EXATO, escrito por extenso: id + tipo + X + Y + direcao + recorte.
		const int esperado = 1 + 1 + 4 + 4 + 1 + 1;
		var tamanhos = new List<int>();
		foreach ((_, Protocol.Decal tipo, byte[] fio) in EscutaDeDecalques!)
			if (tipo == Protocol.Decal.Membro) tamanhos.Add(fio.Length);
		AfirmarPeca($"...e ele tem os {esperado} bytes da declaracao (id, tipo, X, Y, direcao, recorte)",
					tamanhos.Count > 0 && tamanhos.TrueForAll(n => n == esperado),
					string.Join(",", tamanhos));

		// ---------- E O RELATO DA PLATEIA CONTINUA SEM FICHA ----------
		// A razao de a peca vir por OUTRO canal (ver `SoltarPecas`): alargar o `S2C.Hit` pra caber o
		// membro vazaria o membro atingido em TODO soco. Aqui se mede que ele nao vazou.
		Protocol.HitEvent? magro = null, cheio = null;
		foreach ((bool ehCheio, byte[] fio) in EscutaDeGolpes!)
		{
			Protocol.HitEvent h = LerGolpe(fio);
			if (!h.Decepou) continue;
			if (ehCheio) cheio = h; else magro = h;
		}

		AfirmarPeca("a PLATEIA sabe que houve amputacao e NAO sabe qual membro nem quanto doeu",
					magro is { Decepou: true, TemDano: false }
					&& string.IsNullOrEmpty(magro.Value.Membro) && magro.Value.Dano == 0,
					magro == null ? "(nenhum relato magro)"
								  : $"membro='{magro.Value.Membro}' dano={magro.Value.Dano}");

		AfirmarPeca("...e quem BRIGOU recebe o membro pelo nome (o relato confiavel continua inteiro)",
					cheio is { Decepou: true, TemDano: true } && cheio.Value.Membro == "Braco esquerdo",
					cheio == null ? "(nenhum relato cheio)" : $"membro='{cheio.Value.Membro}'");

		// A PECA NAO DIZ DE QUEM E. O pacote nao tem campo de dono -- e a afirmacao por ausencia que o
		// tamanho acima ja sustenta; esta linha existe pra dizer POR QUE, e pra ficar vermelha no dia
		// em que alguem acrescentar o id "so pra depurar".
		AfirmarPeca("...e a peca no chao nao diz de quem era o corpo (nao ha campo de dono no fio)",
					tamanhos.Count > 0 && tamanhos.TrueForAll(n => n == esperado));

		LimparTudoDaBancada();
	}
}
