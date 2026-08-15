using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA DESTRUICAO DE PLANETA (`--planetateste`).
///
/// ============================ O QUE SO DAQUI SE RESPONDE ============================
///   1. **A SEQUENCIA TEM OS NUMEROS DO DM?** Os quatro estagios de 200/300/300/400 s, a explosao de
///      310 s, a carga de 30 s. O relogio e adiantado pela MESMA funcao de producao
///      (`TickDaDestruicao`), um segundo por volta -- nao ha atalho que pule direto pro commit.
///   2. **O PAVIO RETOMA, E NAO REINICIA, DEPOIS DE UM REINICIO?** Esta e A checagem deste arquivo.
///      O defeito do original (`Weather.dm:72-74`) e que `planet_dying` persiste e
///      `planet_death_stage` nao, entao **todo boot recomeca a morte do estagio 0**. A bancada grava,
///      apaga o registro em memoria, le do disco e confere que o estagio e o resto do relogio
///      voltaram IGUAIS.
///   3. **SERVIDOR VAZIO ADIA O PAVIO -- e NAO adia a explosao?** As duas metades. So a primeira
///      seria um sistema que nunca acontece; so a segunda seria o planeta explodindo de madrugada.
///   4. **O COMMIT SEPARA QUEM E MAIS FORTE?** Quem tem BP expresso acima do algoz atravessa o fim do
///      mundo ileso; quem esta abaixo leva 99 em cada membro; quem esta NOCAUTEADO morre.
///   5. **A MORTE PASSA PELA PORTA UNICA?** Um `NegarMorte` pendurado no corpo tem que barrar a morte
///      do commit -- se nao barrar, esta e a unica morte do jogo que ignora a Aura of Destruction.
///   6. **A EVACUACAO POE A PESSOA NO ESPACO, FORA DO RAIO?** E, portanto, sem re-pousar no quadro
///      seguinte -- e com o filtro de planeta morto impedindo a volta.
///   7. **OS CONSUMIDORES ESQUECIDOS ESTAO LIGADOS?** Povoamento, berco e pouso.
///   8. **O FIM DO MUNDO SEM PLATEIA CHEGA AO MESMO LUGAR?** Destruir um planeta VAZIO entra na lista
///      igual -- e a explosao **para na borda da zona**: uma testemunha fraca noutro planeta nao leva
///      um arranhao. Sem ela, um commit que varresse `_players` passaria despercebido.
///   9. **O CADAVER SOBREVIVE AO BOOT?** O bloco 2 mede o PAVIO voltando do disco; este mede o
///      estado FINAL voltando -- e o tique nao mexendo mais nele.
///  10. **O PLANETA GERADO RENASCE DESTRUIDO?** Ele nao existe entre dois boots: e refeito da seed
///      toda vez. E o irmao de MESMO NOME e outra seed continua vivo -- a linha que faz a
///      `ChaveDePlaneta` valer o que ela custou.
///  11. **OS GATES TEM OS DOIS SENTIDOS?** Vilao, os 10 marcos, o Ki, o BP contra a gravidade e os
///      30 s -- cada um visto recusando E deixando passar. Aqui o verbo entra por `UsarTecnica`, que
///      e a porta do cliente: e por ela que o custo de 10 marcos vira consequencia.
/// ================================================================================
///
///     Godot --headless -- --server --planetateste
/// </summary>
public partial class GameServer
{
	private bool _planetaDeTeste;

	/// <summary>
	/// Roda uma vez, no primeiro login. **MEXE NO MUNDO** (mata um planeta de verdade e escreve o
	/// `planetas-mortos.json`), entao tudo o que ela toca e fotografado e devolvido no `finally`.
	/// </summary>
	private void RodarBancadaDePlaneta(ServerPlayer pl)
	{
		GD.Print("\n===== BANCADA DA DESTRUICAO DE PLANETA =====");

		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"  OK   {nome}"); }
			else { falhou++; GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
		}

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		// Ela destroi um planeta PRE-FEITO de verdade. Sem esta fotografia, rodar a bancada uma vez
		// deixaria Arlia morta pra sempre no save do dono.
		// ==============================================================================
		var guardado = _mortos.Todos.ToList();
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		double bpGuardado = pl.Ficha.BP, hpGuardado = pl.Ficha.HP, kiGuardado = pl.Ficha.Ki;
		bool viloGuardado = pl.Ficha.isVillain;
		bool mortoGuardado = pl.Ficha.dead, koGuardado = pl.Ficha.KO;

		// ============================ O LIVRO DE SKILLS E A GRAVIDADE TAMBEM VOLTAM ============================
		// A familia dos gates COMPRA a skill de vilao com marcos de mentira e mexe no `Planetgrav` pra
		// medir o limiar. Sem esta fotografia, quem rodasse a bancada uma vez ficaria com Planet
		// Destroy no livro e com o peso de um planeta que ele nao esta pisando -- e o `Persistir` la
		// embaixo gravaria as duas coisas no disco.
		// ================================================================================================
		var skillsGuardadas = pl.Livro.Aprendidas.ToList();
		int marcosGuardados = pl.Livro.MarcosLivres, totaisGuardados = pl.Livro.MarcosTotais;
		double gravGuardada = pl.Ficha.Planetgrav;

		// ============================ O CORPO PRECISA COMECAR VIVO -- E ISSO CUSTOU UMA RODADA ============================
		// A segunda rodada desta bancada caiu em DOZE checagens, e a causa nao estava em nenhuma
		// delas: o personagem voltou **morto** do save da rodada anterior (ela o matou no commit e o
		// `Persistir` gravou). Com ele morto, `AlguemOnline()` responde falso -- e o "servidor vazio
		// adia o pavio", que e regra de PRODUCAO e estava funcionando, congelou o pavio inteiro.
		//
		// Vale guardar por dois motivos. Primeiro: a bancada que MEXE no mundo tem que devolver
		// tambem o que ela quebrou no proprio operador, e nao so o mundo. Segundo, e mais util: o
		// sintoma ("o pavio nao anda") apontava pra um lugar onde nao havia defeito nenhum -- foi a
		// checagem de MONTAGEM logo abaixo que mostrou onde olhar.
		// ==========================================================================================================
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();

		try
		{
			Checa("(montagem) quem roda a bancada esta vivo -- senao o 'servidor vazio' congela tudo",
				  AlguemOnline());

			MedirASequencia(Checa);
			MedirOReinicio(Checa);
			MedirOReinicioDoDestruido(Checa, pl);
			MedirOServidorVazio(Checa);
			MedirOCommit(Checa, pl);
			MedirOPlanetaVazio(Checa, pl);
			MedirOProcedural(Checa, pl);
			MedirOsConsumidores(Checa);
			MedirOVerb(Checa, pl);
			MedirOsGates(Checa, pl);
		}
		finally
		{
			_mortos.Limpar();
			foreach (EstadoDaMorte e in guardado) _mortos.Por(e);
			_proximoTremor.Clear();
			_cargaDoPlanetDestroy.Clear();
			SalvarPlanetasMortos();
			MandarMortosPraTodos();

			pl.Ficha.isVillain = viloGuardado;
			pl.Ficha.BP = bpGuardado;
			pl.Ficha.HP = hpGuardado;
			pl.Ficha.Ki = kiGuardado;
			pl.Ficha.dead = mortoGuardado;
			pl.Ficha.KO = koGuardado;
			pl.Ficha.Planetgrav = gravGuardada;
			pl.Livro.Carregar(skillsGuardadas);
			pl.Livro.MarcosLivres = marcosGuardados;
			pl.Livro.MarcosTotais = totaisGuardados;
			pl.Ficha.Tick(agoraMs: NowMs());
			if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);

			// E O SAVE VOLTA JUNTO. Sem isto, o BP de 1e12 e o bit de vilao que a bancada usou
			// ficariam no disco -- e a rodada seguinte comecaria de um personagem que ela mesma
			// inventou, que e como ela se enganou uma vez.
			Persistir(pl);

			GD.Print($"===== BANCADA DA DESTRUICAO: {ok} OK, {falhou} FALHA =====\n");
		}
	}

	/// <summary>O planeta cobaia. Arlia: pre-feito, no manifesto, e ninguem nasce nele por padrao.</summary>
	private static ZoneKey Cobaia => ZoneKey.Premade("Arlia");

	// =====================================================================
	// 1. A SEQUENCIA
	// =====================================================================
	private void MedirASequencia(Checagem Checa)
	{
		Checa("os quatro estagios do pavio sao 200/300/300/400 s (Area_Death.dm:30,34,39,44)",
			  MortePlanetaria.SegundosDoEstagio.Length == 4
			  && MortePlanetaria.SegundosDoEstagio[0] == 200
			  && MortePlanetaria.SegundosDoEstagio[1] == 300
			  && MortePlanetaria.SegundosDoEstagio[2] == 300
			  && MortePlanetaria.SegundosDoEstagio[3] == 400,
			  string.Join("/", MortePlanetaria.SegundosDoEstagio));

		Checa("...o que da 20 minutos de pavio",
			  Math.Abs(MortePlanetaria.PavioInteiro - 1200) < 0.001,
			  $"{MortePlanetaria.PavioInteiro}");

		Checa("a explosao sao 310 s (o `sleep(3100)`, e nao os 'five minutes' do comentario)",
			  Math.Abs(MortePlanetaria.SegundosDeExplosao - 310) < 0.001);

		Checa("a chance do limit_life e 25/50/75/100 por estagio",
			  MortePlanetaria.ChanceDeMorrerPct(0) == 25 && MortePlanetaria.ChanceDeMorrerPct(1) == 50
			  && MortePlanetaria.ChanceDeMorrerPct(2) == 75 && MortePlanetaria.ChanceDeMorrerPct(3) == 100);

		// ---- O PAVIO ANDANDO, PELO CODIGO DE PRODUCAO ----
		_mortos.Limpar();
		Checa("acender o pavio lento em Arlia funciona",
			  ComecarMorteLenta(Cobaia, 5000, "bancada"));
		Checa("...e reacender NAO cria um segundo pavio (o `if(death_proc_running) return`)",
			  !ComecarMorteLenta(Cobaia, 5000, "bancada"));

		EstadoDaMorte e = MorteDaZona(Cobaia)!;
		Checa("ele nasce no estagio 0", e is { Fase: FaseDaMorte.Morrendo, Estagio: 0 },
			  $"{e.Fase}/{e.Estagio}");

		// O RELOGIO E ADIANTADO PELO TIQUE DE PRODUCAO, um segundo por volta. Chamar `Faltam = 0` na
		// mao mediria o campo, e nao a maquina que o le -- a armadilha 1 da PARTE 3.
		for (int i = 0; i < 200 && e.Estagio == 0; i++) TickDaDestruicao(1);
		Checa("depois de 200 s ele sobe pro estagio 1 (a noite que nao acaba)", e.Estagio == 1,
			  $"estagio {e.Estagio}");

		Checa("...e o ceu de Arlia virou o da DESTRUICAO",
			  ClimaAgora(Cobaia).Tipo == TipoDeClima.Destruicao,
			  $"{Clima.Nome(ClimaAgora(Cobaia).Tipo)}");

		for (int i = 0; i < 300 && e.Estagio == 1; i++) TickDaDestruicao(1);
		Checa("300 s depois, estagio 2 -- e daqui pra frente o chao se abre", e.Estagio == 2);

		for (int i = 0; i < 700 && e.Fase == FaseDaMorte.Morrendo; i++) TickDaDestruicao(1);
		Checa("terminado o pavio, ele vira EXPLODINDO sozinho (o `goto destroy`)",
			  e.Fase == FaseDaMorte.Explodindo, $"{e.Fase}/{e.Estagio}");
		Checa("...e a explosao nasce com os 310 s inteiros na frente",
			  Math.Abs(e.Faltam - MortePlanetaria.SegundosDeExplosao) < 1.001, $"{e.Faltam:0.#}");
	}

	// =====================================================================
	// 2. O REINICIO -- a armadilha do original
	// =====================================================================
	/// <summary>
	/// ============================ A CHECAGEM QUE JUSTIFICA O ARQUIVO ============================
	/// No DM, `planet_dying` volta do disco e `planet_death_stage` nao -- e o `Ticker` da area re-arma
	/// o `Planet_Death` **do zero**. Um planeta com o pavio aceso reinicia a morte a cada boot, pra
	/// sempre, e o remendo foi uma faxina no `Boss_Events_Init` (`BossEvents.dm:966-971`).
	///
	/// A bancada faz o caminho inteiro: grava, **apaga o registro em memoria** (que e o que um boot
	/// faz), le do disco, e confere que o estagio e o relogio voltaram iguais. Sem o `Limpar()` no
	/// meio, ela leria o objeto que ja estava na mao e passaria com o disco vazio.
	/// ========================================================================================
	/// </summary>
	private void MedirOReinicio(Checagem Checa)
	{
		_mortos.Limpar();
		ComecarMorteLenta(Cobaia, 7777, "bancada-reinicio");
		for (int i = 0; i < 260; i++) TickDaDestruicao(1);   // entra no estagio 1 e anda 60 s dele

		EstadoDaMorte antes = MorteDaZona(Cobaia)!;
		int estagioAntes = antes.Estagio;
		double faltamAntes = antes.Faltam;
		double bpAntes = antes.BpDoAlgoz;

		Checa("antes do 'reinicio' ele esta no estagio 1, no meio do relogio",
			  estagioAntes == 1 && faltamAntes is > 0 and < 300,
			  $"estagio {estagioAntes}, faltam {faltamAntes:0.#}");

		SalvarPlanetasMortos();
		_mortos.Limpar();                 // <- isto E o reinicio
		Checa("...e o registro em memoria some (simulando o boot)", MorteDaZona(Cobaia) == null);

		CarregarPlanetasMortos();
		EstadoDaMorte? depois = MorteDaZona(Cobaia);

		Checa("**O PAVIO VOLTA DO DISCO** (nao virou planeta vivo)", depois != null);
		Checa("**E ELE NAO REINICIA**: o estagio voltou o MESMO, e nao 0 (o bug do Weather.dm:72-74)",
			  depois?.Estagio == estagioAntes, $"{depois?.Estagio} vs {estagioAntes}");
		Checa("...e o relogio do estagio tambem voltou de onde parou",
			  depois != null && Math.Abs(depois.Faltam - faltamAntes) < 0.001,
			  $"{depois?.Faltam:0.###} vs {faltamAntes:0.###}");
		Checa("...e o BP do algoz sobreviveu (e ele que decide quem morre no fim)",
			  depois != null && Math.Abs(depois.BpDoAlgoz - bpAntes) < 0.001);

		// E O PAVIO CONTINUA ANDANDO depois do boot -- carregar sem retomar seria um planeta
		// congelado a meio caminho, que e um modo de falha tao ruim quanto reiniciar.
		double antesDoTique = depois!.Faltam;
		TickDaDestruicao(1);
		Checa("...e ele CONTINUA andando depois do boot (nao congelou)",
			  depois.Faltam < antesDoTique, $"{depois.Faltam:0.#} vs {antesDoTique:0.#}");
	}

	// =====================================================================
	// 3. SERVIDOR VAZIO
	// =====================================================================
	/// <summary>
	/// AS DUAS METADES. A bancada roda com UM jogador online (quem a disparou), entao pra medir o
	/// servidor vazio ela **tira o `Peer` dele por dois tiques** -- o mesmo estado que um logout
	/// produz, pelo campo que o codigo de producao le (`AlguemOnline`).
	/// </summary>
	private void MedirOServidorVazio(Checagem Checa)
	{
		_mortos.Limpar();
		ComecarMorteLenta(Cobaia, 5000, "bancada-vazio");
		EstadoDaMorte e = MorteDaZona(Cobaia)!;

		var peers = new List<(ServerPlayer P, LiteNetLib.NetPeer Peer)>();
		foreach (ServerPlayer p in _players.Values)
			if (p.Peer is { } pe) { peers.Add((p, pe)); p.Peer = null; }

		double antes = e.Faltam;
		TickDaDestruicao(1);
		TickDaDestruicao(1);
		Checa("**SERVIDOR VAZIO ADIA O PAVIO**: o relogio nao andou",
			  Math.Abs(e.Faltam - antes) < 0.001, $"{e.Faltam:0.###} vs {antes:0.###}");

		// E A EXPLOSAO NAO ESPERA. Mesma fotografia, fase diferente.
		ComecarDestruicao(Cobaia, 5000, "bancada-vazio");
		EstadoDaMorte x = MorteDaZona(Cobaia)!;
		double antesX = x.Faltam;
		TickDaDestruicao(1);
		Checa("**MAS A EXPLOSAO NAO ESPERA NINGUEM**: uma vez comecada, ela vai ate o fim",
			  x.Faltam < antesX, $"{x.Faltam:0.###} vs {antesX:0.###}");

		foreach ((ServerPlayer p, LiteNetLib.NetPeer pe) in peers) p.Peer = pe;

		// E COM GENTE DE VOLTA, o pavio volta a andar -- senao a checagem acima ficaria verde com
		// um pavio que nao anda NUNCA, que e o "teto que nunca dispara" ao contrario.
		_mortos.Limpar();
		ComecarMorteLenta(Cobaia, 5000, "bancada-vazio-2");
		EstadoDaMorte v = MorteDaZona(Cobaia)!;
		double antesV = v.Faltam;
		TickDaDestruicao(1);
		Checa("...e com alguem online ele volta a andar (a guarda nao trava o pavio pra sempre)",
			  v.Faltam < antesV, $"{v.Faltam:0.###} vs {antesV:0.###}");
	}

	// =====================================================================
	// 4. O COMMIT
	// =====================================================================
	/// <summary>
	/// Tres corpos forjados na zona cobaia -- um FRACO, um FORTE e um NOCAUTEADO -- e o quarto e o
	/// jogador de verdade, que mede a evacuacao.
	/// </summary>
	private void MedirOCommit(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();

		// O HABITANTE (com `Papel`) e forte de proposito: e ele que prova que o `isNPC` do commit
		// nao tem checagem de BP nenhuma.
		ServerPlayer? habitante = ForjarHabitante("Cidadao", 999_999);

		// E OS TRES SEM `Papel` sao "jogadores" aos olhos do commit -- e so por isso a regra do BP
		// pode ser medida. Forjados sem `Peer` porque nao ha como forjar um `NetPeer`; o que separa
		// jogador de habitante nesta regra e o `Papel`, nao o teclado.
		ServerPlayer fraco = ForjarNaCobaia("Fraco", 100);
		ServerPlayer forte = ForjarNaCobaia("Forte", 1e11);
		ServerPlayer caido = ForjarNaCobaia("Caido", 100);
		ServerPlayer seguro = ForjarNaCobaia("Segurado", 100);

		caido.Combate!.Nocautear(120);

		// O SEGURO DA PORTA UNICA: se o commit matar este corpo, ele nao esta passando pelo
		// `CombatState.Morrer()` -- e a Aura of Destruction nao valeria contra o fim do mundo.
		int negacoes = 0;
		seguro.Combate!.NegarMorte = _ => { negacoes++; return true; };

		// ============================ O JOGADOR TEM QUE SOBREVIVER PRA SER EVACUADO ============================
		// A primeira versao desta bancada punha o jogador com `BP = 1` -- ou seja, ABAIXO do algoz --,
		// e as tres checagens de evacuacao caiam. Estavam certas: quem esta abaixo do algoz leva 99 em
		// cada membro e MORRE, e um morto nao evacua (`:132` do original pula quem ja esta no Outro
		// Mundo). O defeito era a montagem, nao a regra.
		//
		// A evacuacao e sobre quem SOBRA. Entao o jogador entra forte -- e assim ele mede as duas
		// coisas de uma vez: que o forte atravessa o fim do mundo, e que o mundo o cospe pro espaco.
		//
		// `Tick` e nao `Statify`: e o `PowerLevel()` que escreve `expressedBP`, e e o `expressedBP`
		// que o commit compara. Com so `Statify` o numero fica velho, e a bancada mediria a
		// comparacao errada sem nada acusando (a armadilha 6 da PARTE 3).
		// ================================================================================================
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, Cobaia, PontoDeNascimento(Cobaia));
		pl.Ficha.BP = 1e11;
		pl.Ficha.Tick(agoraMs: NowMs());

		// O ALGOZ fica ACIMA do fraco e ABAIXO do forte e de mim -- e as duas metades sao afirmadas
		// abaixo, senao um algoz forte demais deixaria "o forte sobreviveu" verde por acidente.
		double bpDoAlgoz = fraco.Ficha.expressedBP + 1;
		Checa("(montagem) o algoz esta acima do fraco e abaixo do forte e de mim",
			  bpDoAlgoz > fraco.Ficha.expressedBP && bpDoAlgoz < forte.Ficha.expressedBP
			  && bpDoAlgoz < pl.Ficha.expressedBP,
			  $"algoz {bpDoAlgoz:0} | fraco {fraco.Ficha.expressedBP:0} | "
			  + $"forte {forte.Ficha.expressedBP:0} | eu {pl.Ficha.expressedBP:0}");

		ComecarDestruicao(Cobaia, bpDoAlgoz, "bancada-commit");
		Checa("a explosao poe o ceu de DESTRUICAO na zona",
			  ClimaAgora(Cobaia).Tipo == TipoDeClima.Destruicao);

		for (int i = 0; i < 400 && MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);

		EstadoDaMorte e = MorteDaZona(Cobaia)!;
		Checa("passados os 310 s, o planeta esta DESTRUIDO", e.Fase == FaseDaMorte.Destruido, $"{e.Fase}");
		Checa("...e ele entrou na lista de mortos (a `PlanetDisableList`)", ZonaMorta(Cobaia));

		Checa("**O HABITANTE MORRE MESMO SENDO MAIS FORTE QUE O ALGOZ** (o `isNPC` do :134 nao "
			  + "olha BP nenhum)",
			  habitante is { Ficha.dead: true },
			  habitante == null ? "sem molde 'cidadao' no npcs.json"
								: $"dead={habitante.Ficha.dead}, BP {habitante.Ficha.expressedBP:0}");

		Checa("o fraco morreu (99 em cada membro -- o `SpreadDamage(99)` do :138)", fraco.Ficha.dead);
		Checa("o NOCAUTEADO morreu (Area_Death.dm:140-142)", caido.Ficha.dead);

		Checa("**QUEM E MAIS FORTE QUE O ALGOZ ATRAVESSA O FIM DO MUNDO** (o `<=` do :137)",
			  !forte.Ficha.dead, $"BP {forte.Ficha.expressedBP:0} vs algoz {bpDoAlgoz:0}");

		Checa("**A MORTE PASSA PELA PORTA UNICA**: o `NegarMorte` foi consultado...",
			  negacoes > 0, $"{negacoes} consultas");
		Checa("...e quem tem seguro NAO morreu no fim do mundo", !seguro.Ficha.dead);

		// ---- A EVACUACAO ----
		Checa("o jogador (mais forte que o algoz) sobreviveu ao fim do mundo", !pl.Ficha.dead);
		Checa("**O JOGADOR FOI EVACUADO PRO ESPACO**", Espaco.EhEspaco(pl.Zone), $"{pl.Zone.Name}");

		PlanetaNoEspaco? arlia = null;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (p.Nome == "Arlia") { arlia = p; break; }

		if (arlia is { } disco)
		{
			double d = (pl.Pos - disco.Pos).Length;
			Checa("...e FORA do raio do disco -- ninguem re-pousa no mesmo quadro",
				  d > disco.Raio, $"{d:0} px contra raio {disco.Raio:0}");
			Checa("...e o carimbo de chunk foi feito (senao a vizinhanca sairia duplicada)",
				  pl.ChunkAtual == ChunkId.De(pl.Pos));
		}

		// ---- E ELE NAO CONSEGUE VOLTAR ----
		// Sem esta checagem a evacuacao seria um empurraozinho de 90 px: o `TickDoEspaco` roda a
		// 30 Hz e o corpo esta a 90 px do disco.
		if (arlia is { } alvo)
		{
			pl.Pos = alvo.Pos;   // colado no centro do planeta morto
			TickDoEspaco(pl);
			Checa("**E ELE NAO CONSEGUE VOLTAR**: encostar num planeta morto nao pousa",
				  Espaco.EhEspaco(pl.Zone), $"{pl.Zone.Name}");
		}

		// `ServerPlayer?` no array: o habitante e nulo quando o `npcs.json` nao trouxe o molde
		// `cidadao`, e a checagem dele ja disse isso em voz alta la em cima.
		foreach (ServerPlayer? forjado in new ServerPlayer?[] { fraco, forte, caido, seguro, habitante })
			if (forjado != null) Recolher(forjado);
		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	/// <summary>
	/// Um corpo sem dono na zona cobaia, pelo caminho do clone/NPC de bancada. **Sem `Peer`** -- o
	/// que faz dele um habitante aos olhos do commit.
	/// </summary>
	private ServerPlayer ForjarNaCobaia(string nome, double bp) => ForjarEm(Cobaia, nome, bp);

	/// <summary>O mesmo corpo sem dono, em qualquer zona -- a bancada das sagas tambem precisa dele.</summary>
	private ServerPlayer ForjarEm(ZoneKey zona, string nome, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoPlanetaDeTeste + _forjadosDoPlaneta++,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = PontoDeNascimento(zona),
			Conta = $"bancada_planeta_{nome}",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = bp },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;

		// `PorNoMundo` chama `Statify` (os ATRIBUTOS) e nao `PowerLevel` (o PODER). Sem esta linha o
		// `expressedBP` nasce ZERO -- e o commit compara justamente `expressedBP <= mexpressedBP`,
		// entao a bancada mediria todo mundo, do fraco ao de um milhao, caindo no mesmo lado.
		novo.Ficha.Tick(agoraMs: NowMs());
		return novo;
	}

	/// <summary>
	/// UM HABITANTE de verdade na cobaia -- com `Papel`, que e o `isNPC` deste port.
	///
	/// Ele precisa do molde de producao: o `limit_life` e o commit perguntam pelo `Papel`, e um corpo
	/// forjado sem ele seria um jogador aos olhos das duas regras. Devolve nulo quando o `npcs.json`
	/// nao carregou -- e ai a checagem que depende dele diz isso em voz alta, em vez de passar.
	/// </summary>
	private ServerPlayer? ForjarHabitante(string nome, double bp) =>
		ForjarHabitanteEm(Cobaia, nome, bp);

	/// <summary>O habitante, em qualquer zona. Ver <see cref="ForjarHabitante"/>.</summary>
	private ServerPlayer? ForjarHabitanteEm(ZoneKey zona, string nome, double bp)
	{
		if (_moldes?.Get("cidadao") is not { } molde) return null;

		ServerPlayer c = ForjarEm(zona, nome, bp);
		c.Papel = new Jandirus.Core.Npc.PapelDeNpc(molde, (ulong)c.Id);
		return c;
	}

	/// <summary>Faixa de ids propria, longe da dos jogadores e da das outras bancadas.</summary>
	private const int IdBaseDoPlanetaDeTeste = 940_000;
	private int _forjadosDoPlaneta;

	// =====================================================================
	// 5. OS CONSUMIDORES ESQUECIDOS
	// =====================================================================
	/// <summary>
	/// ============================ AS TRES PORTAS QUE DESFARIAM O SISTEMA SOZINHAS ============================
	///   * **o POVOAMENTO**: a manutencao repovoa ate o alvo a cada 5 min. Sem o corte, os 40
	///     cidadaos que o `limit_life` matou voltam sozinhos e o planeta morto fica cheio de gente.
	///   * **o BERCO**: a saga 1 destroi **Vegeta**, o berco dos Saiyajin. Sem o corte, todo Saiyajin
	///     que nascesse ou morresse acordaria num planeta que nao existe.
	///   * **o POUSO**: ja medido no commit.
	/// ==================================================================================================
	/// </summary>
	private void MedirOsConsumidores(Checagem Checa)
	{
		_mortos.Limpar();
		ComecarDestruicao(ZoneKey.Premade("Vegeta"), 1, "bancada-consumidores");
		MorteDaZona(ZoneKey.Premade("Vegeta"))!.Fase = FaseDaMorte.Destruido;

		// ============================ A FILA PRECISA ESTAR VAZIA PRA MEDIR A FILA ============================
		// A primeira versao desta bancada nao limpava, e a checagem caiu com "40 na fila". Ela estava
		// medindo o povoamento do BOOT: a manutencao inicial enfileira os 148 cidadaos do plano e o
		// dreno solta 1 por tique -- no primeiro login a fila ainda esta cheia. Ou seja, ela via os
		// 40 de Vegeta que o servidor pediu ANTES de o planeta morrer e chamava isso de repovoamento.
		//
		// E a mesma familia de erro que a bancada do povoamento ja tinha registrado ("a fila envelhece
		// e o mundo anda"): quem mede fila tem que saber de onde cada item veio.
		// ================================================================================================
		_filaDoPovoamento.Clear();

		// ---- POVOAMENTO ----
		int antes = ContarHabitantes("Vegeta", "cidadao");
		Manutencao();
		int naFila = _filaDoPovoamento.Count(f => f.Item2.Hash == ZoneKey.Premade("Vegeta").Hash);
		int outros = _filaDoPovoamento.Count - naFila;
		Checa("**PLANETA MORTO NAO SE REPOVOA**: a manutencao nao enfileirou ninguem em Vegeta",
			  naFila == 0, $"{naFila} na fila (havia {antes} vivos)");

		// E O CORTE E SO DE VEGETA. Sem esta metade, um bug que desligasse o povoamento INTEIRO
		// deixaria a checagem acima verde -- o "teto que nunca dispara" na forma inversa.
		Checa("...mas os outros planetas continuam se povoando (o corte e so do morto)",
			  outros > 0, $"{outros} enfileirados fora de Vegeta");

		// ---- BERCO ----
		var bercoSaiyajin = new Jandirus.Core.Races.Berco
		{
			Planeta = "Vegeta",
			PreFeito = true,
		};
		(ZoneKey zona, _) = DestinoDoBerco(bercoSaiyajin);
		Checa("**NINGUEM NASCE NUM CADAVER**: o berco Saiyajin (Vegeta) foi desviado",
			  !zona.Equals(ZoneKey.Premade("Vegeta")), $"caiu em {zona.Name}");
		Checa("...e o desvio caiu num planeta VIVO", !ZonaMorta(zona), zona.Name);

		// ---- E O DESFAZER ----
		Checa("restaurar tira da lista (o `Restaurar_Planeta`, a unica volta que existe)",
			  RessuscitarPlaneta(ZoneKey.Premade("Vegeta")));
		Checa("...e Vegeta volta a ser um berco valido",
			  DestinoDoBerco(bercoSaiyajin).Zona.Equals(ZoneKey.Premade("Vegeta")));

		// A FILA E LIMPA: a manutencao acima pode ter enfileirado corpos de OUTROS planetas, e
		// deixa-los nascer encheria o mundo do dono por causa da bancada.
		_filaDoPovoamento.Clear();
	}

	// =====================================================================
	// 6. O VERB DO JOGADOR
	// =====================================================================
	private void MedirOVerb(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();
		_cargaDoPlanetDestroy.Clear();

		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, Cobaia, PontoDeNascimento(Cobaia));

		// ============================ O CORPO PRECISA ESTAR DE PE PRA APERTAR O BOTAO ============================
		// O verb recusa quem esta morto ou nocauteado, e o bloco anterior acabou de explodir um planeta
		// debaixo deste jogador. Sem esta linha, TODAS as checagens do verb caiam -- e caiam pela razao
		// certa (um morto nao destroi planeta), medindo a montagem em vez da regra.
		//
		// `Tick` e nao `Statify` pelo mesmo motivo do bloco do commit: e ele que escreve `expressedBP`,
		// que e o numero que o verb compara contra `10000 * Planetgrav`.
		// ==================================================================================================
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();
		pl.Ficha.BP = 1e12;
		pl.Ficha.Tick(agoraMs: NowMs());
		pl.Ficha.Ki = 99_999;

		// ---- 1. SO VILAO ----
		pl.Ficha.isVillain = false;

		PlanetDestroy(pl);
		Checa("**SEM SER VILAO NAO SAI** (o `if(!usr.isVillain)` de Planets.dm:323)",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id));

		// ---- 2. O CUSTO ----
		pl.Ficha.isVillain = true;
		double kiGuardado = pl.Ficha.Ki;
		pl.Ficha.Ki = MortePlanetaria.KiDaDestruicao - 1;
		PlanetDestroy(pl);
		Checa("...nem com 999 de Ki (o `PDESTROY_KI_COST 1000`)",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id));

		pl.Ficha.Ki = kiGuardado;
		PlanetDestroy(pl);
		Checa("vilao com Ki e BP sobrando COMECA a carga",
			  _cargaDoPlanetDestroy.ContainsKey(pl.Id));
		Checa("...e os 1000 de Ki saem NA HORA (o `usr.Ki -= ...` do :338, antes da confirmacao)",
			  Math.Abs(kiGuardado - pl.Ficha.Ki - MortePlanetaria.KiDaDestruicao) < 0.001,
			  $"gastou {kiGuardado - pl.Ficha.Ki:0}");

		// ---- 3. O NOCAUTE INTERROMPE ----
		pl.Combate!.Nocautear(120);
		TickDaDestruicao(1);
		Checa("**NOCAUTEA-LO NA JANELA INTERROMPE** e o planeta nao e condenado",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id) && MorteDaZona(Cobaia) == null);

		// ---- 4. A CARGA INTEIRA ACENDE A DESTRUICAO ----
		pl.Ficha.KO = false;
		pl.Ficha.Ki = 99_999;
		PlanetDestroy(pl);
		for (int i = 0; i < (int)MortePlanetaria.SegundosDeCarga + 2; i++) TickDaDestruicao(1);
		Checa($"esperados os {MortePlanetaria.SegundosDeCarga:0} s de carga, a destruicao comeca",
			  MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo,
			  $"{MorteDaZona(Cobaia)?.Fase}");

		Checa("abortar antes do fim devolve o planeta", AbortarMorte(Cobaia, "bancada"));
		Checa("...e ele nao esta mais condenado", MorteDaZona(Cobaia) == null);

		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	// =====================================================================
	// 7. NINGUEM DENTRO -- e a explosao para na BORDA DA ZONA
	// =====================================================================
	/// <summary>
	/// ============================ O FIM DO MUNDO SEM PLATEIA ============================
	/// O bloco 4 mede o commit **com gente dentro**. Este mede a outra metade, e ela nao e a mesma
	/// afirmacao ao contrario: um commit que nao encontra ninguem percorre um caminho diferente --
	/// nenhum `EspalharDanoG3`, nenhum `Morrer`, nenhum `EvacuarParaOEspaco` -- e mesmo assim tem que
	/// chegar ao mesmo lugar. **A lista e o efeito; a plateia nao e.**
	///
	/// E ELA TEM DENTES POR CAUSA DA TESTEMUNHA DISTANTE: um corpo FRACO (abaixo do algoz, portanto
	/// alcancavel pela regra do `<=`) parado **noutro planeta**. Se o commit varresse `_players` em vez
	/// da lista da zona, ele levaria 99 em cada membro na Terra por causa de uma explosao em Arlia --
	/// e o resto da bancada nao notaria, porque todo o resto dela mede gente que ESTA na cobaia.
	///
	/// A COBAIA E ESVAZIADA DE PROPOSITO. Arlia tem 12 cidadaos no plano de povoamento
	/// (`npcs.json`), e "ninguem dentro" precisa ser um FATO e nao uma sorte de cronometragem: no
	/// primeiro login a fila do povoamento ainda esta cheia e a zona esta vazia por acaso -- medir isso
	/// seria medir o relogio do boot. Os corpos voltam pela `Manutencao`, que e a mesma porta que
	/// enche o mundo no boot.
	/// ================================================================================
	/// </summary>
	private void MedirOPlanetaVazio(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();

		foreach (ServerPlayer c in ZoneList(Cobaia.Hash).ToList()) Recolher(c);
		Checa("(montagem) a cobaia esta VAZIA -- nenhum corpo, nem habitante",
			  ZoneList(Cobaia.Hash).Count == 0, $"{ZoneList(Cobaia.Hash).Count} corpo(s)");

		// A TESTEMUNHA DISTANTE: fraca, viva e NOUTRO PLANETA.
		ZoneKey longe = ZoneKey.Premade("Earth");
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, longe, PontoDeNascimento(longe));
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();
		pl.Ficha.BP = 100;
		pl.Ficha.Tick(agoraMs: NowMs());

		double bpDoAlgoz = pl.Ficha.expressedBP * 1000;
		double hpAntes = pl.Ficha.HP;
		Checa("(montagem) a testemunha esta ABAIXO do algoz -- ou seja, o `<=` do commit a alcancaria",
			  pl.Ficha.expressedBP <= bpDoAlgoz,
			  $"testemunha {pl.Ficha.expressedBP:0} vs algoz {bpDoAlgoz:0}");

		ComecarDestruicao(Cobaia, bpDoAlgoz, "bancada-vazio-total");
		for (int i = 0; i < 400 && MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);

		Checa("**UM PLANETA SEM NINGUEM EXPLODE ATE O FIM** (o commit nao depende de achar corpo)",
			  MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Destruido, $"{MorteDaZona(Cobaia)?.Fase}");
		Checa("...e entra na lista de mortos do mesmo jeito -- a lista e o efeito, nao a plateia",
			  ZonaMorta(Cobaia));

		Checa("**E A EXPLOSAO PARA NA BORDA DA ZONA**: a testemunha na Terra nao levou um arranhao",
			  !pl.Ficha.dead && Math.Abs(pl.Ficha.HP - hpAntes) < 0.001,
			  $"HP {pl.Ficha.HP:0.##} (era {hpAntes:0.##}), dead={pl.Ficha.dead}");
		Checa("...e nao foi evacuada: quem nao estava no planeta nao e jogado no vacuo",
			  pl.Zone.Equals(longe), $"{pl.Zone.Name}");

		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	// =====================================================================
	// 8. O PLANETA GERADO -- e a prova de que a chave e a SEED
	// =====================================================================
	/// <summary>
	/// ============================ "O PLANETA PROCEDURAL RENASCE DESTRUIDO" ============================
	/// A frase e do original (o planeta volta destruido se o nome estiver na `PlanetDisableList`), e
	/// aqui ela vale mais forte: um mundo gerado **nao existe** entre um boot e outro. Nao ha arquivo,
	/// nao ha zona viva, nao ha nada -- ele e refeito do zero a partir da seed toda vez que alguem
	/// olha. Se o registro de mortos nao o alcancasse, destruir um mundo gerado seria destruir a copia
	/// em memoria e nada mais.
	///
	/// ============================ E A CHECAGEM DO GEMEO E O ARQUIVO INTEIRO NUMA LINHA ============================
	/// `ChaveDePlaneta` existe porque o nome procedural **nao e unico**: ele sai de
	/// `{bioma}-{|Sx|%1000}{|Sy|%1000}{k}`, o `%1000` colide e a concatenacao e ambigua. Chavear por
	/// nome -- que e literalmente o que o DM faz (`PlanetDisableList += P.planetType`) -- mataria
	/// planetas do outro lado da galaxia, **calados**.
	///
	/// Entao esta familia destroi um mundo gerado e pergunta de um IRMAO de mesmo nome e outra seed se
	/// ele esta vivo. E a unica checagem da bancada que fica vermelha se alguem "simplificar" a chave.
	/// ==========================================================================================================
	/// </summary>
	private void MedirOProcedural(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();

		if (AcharPlanetaGerado(SeedDoUniverso) is not { } p)
		{
			Checa("ha um planeta GERADO no mapa do universo pra medir", false,
				  "nenhum procedural nas chunks varridas");
			return;
		}

		var zona = ZoneKey.Procedural(p.Nome, p.Seed);
		Checa($"(montagem) '{p.Nome}' e gerado, e a chave dele e a SEED e nao o nome",
			  !p.Premade && ChaveDePlaneta.Da(zona)?.Texto == $"#{p.Seed}",
			  $"{ChaveDePlaneta.Da(zona)?.Texto}");

		ComecarDestruicao(zona, 1e9, "bancada-procedural");
		for (int i = 0; i < 400 && MorteDaZona(zona)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);

		Checa("um mundo GERADO tambem morre (a lista nao e so dos sete pre-feitos)",
			  ZonaMorta(zona), $"{MorteDaZona(zona)?.Fase}");

		// ---- O REINICIO ----
		SalvarPlanetasMortos();
		_mortos.Limpar();
		Checa("(o registro em memoria some -- isto E o boot)", !ZonaMorta(zona));
		CarregarPlanetasMortos();

		// E A RE-ENUMERACAO: o corpo e refeito do ZERO a partir da seed do universo, pelo MESMO
		// caminho que a carta estelar e o pouso usam. Nao ha estado carregado aqui -- so a funcao.
		PlanetaNoEspaco? denovo = null;
		foreach (PlanetaNoEspaco q in Espaco.PorPerto(SeedDoUniverso, ChunkId.De(p.Pos)))
			if (q.Seed == p.Seed) { denovo = q; break; }

		Checa("...e o corpo volta a ser enumerado da seed (a funcao pura nao guarda rancor)",
			  denovo != null);
		Checa("**O PLANETA GERADO RENASCE DESTRUIDO**: enumerado de novo depois do boot, ja vem morto",
			  denovo is { } d && PlanetaMorto(d));

		// ---- O GEMEO DE MESMO NOME ----
		var gemeo = ZoneKey.Procedural(p.Nome, p.Seed ^ 0xA5A5_A5A5_A5A5_A5A5UL);
		Checa("**E O IRMAO DE MESMO NOME E OUTRA SEED CONTINUA VIVO** -- chavear por nome (o "
			+ "`PlanetDisableList` do DM) mataria os dois",
			  !ZonaMorta(gemeo), $"{ChaveDePlaneta.Da(gemeo)?.Texto}");

		// ---- E NAO SE POUSA NO CADAVER GERADO ----
		// O filtro do `TickDoEspaco` e o MESMO dos pre-feitos; sem esta linha o "renasce destruido"
		// seria um bit no arquivo sem consequencia nenhuma no jogo.
		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, ZonaDoEspaco, p.Pos);
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		TickDoEspaco(pl);
		Checa("...e encostar nele no espaco NAO pousa (o `testPlanetbump` vale pro gerado tambem)",
			  Espaco.EhEspaco(pl.Zone), $"{pl.Zone.Name}");

		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	/// <summary>
	/// O PRIMEIRO PLANETA GERADO QUE APARECER, varrendo chunks longe dos pre-feitos.
	///
	/// A varredura e por CELULA de sistema e nao aleatoria: 37,6% das celulas nao tem estrela nenhuma
	/// (<see cref="Sistemas.VaziosPor256"/>), entao olhar uma chunk so daria nulo com frequencia e a
	/// familia inteira sumiria num "nao havia o que medir".
	///
	/// A SEMENTE ENTRA POR PARAMETRO desde que ela deixou de ser constante: quem chama passa a
	/// `SeedDoUniverso` **deste** servidor, porque o corpo achado aqui vai ser destruido no mundo em
	/// que o servidor esta de pe. Ler uma constante aqui dentro acharia planeta noutro universo -- e
	/// a bancada mediria a morte de um planeta que nao existe.
	/// </summary>
	private static PlanetaNoEspaco? AcharPlanetaGerado(ulong semente)
	{
		for (int i = 1; i <= 600; i++)
			foreach (PlanetaNoEspaco p in Espaco.PorPerto(semente, new ChunkId(i * 37, i * 53)))
				if (!p.Premade) return p;
		return null;
	}

	// =====================================================================
	// 9. O DESTRUIDO SOBREVIVE AO REINICIO
	// =====================================================================
	/// <summary>
	/// ============================ O BLOCO 2 MEDE O PAVIO; ESTE MEDE O CADAVER ============================
	/// Sao estados diferentes e caminhos de save diferentes: `Morrendo` carrega estagio e relogio,
	/// `Destruido` e um estado FINAL, com `Faltam = 0` e sem nada pra retomar. Um save que so soubesse
	/// gravar o pavio deixaria o bloco 2 verde e **ressuscitaria todo planeta destruido a cada boot** --
	/// que e o pior modo de falha deste sistema inteiro, porque ele apaga trabalho de saga.
	///
	/// E a ultima checagem e a que fecha o par com o bloco 2: **o tique nao mexe mais nele.** No DM o
	/// `Ticker` da area le `planet_dying` e re-arma a morte; aqui um planeta ja destruido tem que ser
	/// invisivel pro tique, ou o cadaver "morreria" de novo todo boot.
	/// ================================================================================================
	/// </summary>
	private void MedirOReinicioDoDestruido(Checagem Checa, ServerPlayer pl)
	{
		_mortos.Limpar();
		ComecarDestruicao(Cobaia, 4321, "bancada-reinicio-destruido");
		for (int i = 0; i < 400 && MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo; i++)
			TickDaDestruicao(1);
		Checa("(montagem) a cobaia chegou ao estado DESTRUIDO", ZonaMorta(Cobaia));

		SalvarPlanetasMortos();
		_mortos.Limpar();
		Checa("...e o registro em memoria some (isto E o boot)", !ZonaMorta(Cobaia));

		CarregarPlanetasMortos();
		EstadoDaMorte? e = MorteDaZona(Cobaia);

		Checa("**O PLANETA CONTINUA MORTO DEPOIS DE REINICIAR** (a `PlanetDisableList` persistente)",
			  ZonaMorta(Cobaia));
		Checa("...e volta como DESTRUIDO, e nao 'morrendo outra vez'",
			  e?.Fase == FaseDaMorte.Destruido, $"{e?.Fase}");
		Checa("...e o BP do algoz veio junto (o cadaver guarda quem o matou)",
			  e != null && Math.Abs(e.BpDoAlgoz - 4321) < 0.001, $"{e?.BpDoAlgoz:0}");

		// ---- E O TIQUE NAO MEXE MAIS NELE ----
		for (int i = 0; i < 10; i++) TickDaDestruicao(1);
		Checa("**E O TIQUE NAO O RESSUSCITA NEM O MATA DE NOVO**: morto e estado final, nao relogio",
			  MorteDaZona(Cobaia) is { Fase: FaseDaMorte.Destruido, Faltam: <= 0 },
			  $"{MorteDaZona(Cobaia)?.Fase}/{MorteDaZona(Cobaia)?.Faltam:0.#}");

		// ---- E OS CONSUMIDORES CONTINUAM VENDO O CADAVER DEPOIS DO BOOT ----
		// Sem isto, "continua morto" seria uma linha no arquivo sem efeito nenhum no jogo.
		PlanetaNoEspaco? arlia = null;
		foreach (PlanetaNoEspaco q in Espaco.PreFeitos())
			if (q.Nome == "Arlia") { arlia = q; break; }

		if (arlia is { } disco)
		{
			ZoneKey voltaZona = pl.Zone;
			Vec2 voltaPos = pl.Pos;
			MoveToZone(pl.Id, ZonaDoEspaco, disco.Pos);
			pl.ChunkAtual = ChunkId.De(pl.Pos);
			TickDoEspaco(pl);
			Checa("...e depois do boot ele CONTINUA recusando pouso (o filtro le o registro relido)",
				  Espaco.EhEspaco(pl.Zone), $"{pl.Zone.Name}");
			MoveToZone(pl.Id, voltaZona, voltaPos);
		}
	}

	// =====================================================================
	// 10. OS GATES DA SKILL -- OS DOIS SENTIDOS EM CADA
	// =====================================================================
	/// <summary>
	/// ============================ UM GATE SO VISTO RECUSANDO NAO E UM GATE ============================
	/// O bloco 6 media as recusas do verbo. Ele nao media nenhum "sim" isolado -- e uma porta que so
	/// foi vista fechada e indistinguivel de uma parede. Este bloco fecha os DOIS sentidos de cada
	/// portao e, de quebra, cobre dois que ninguem media:
	///
	///   * **OS 10 MARCOS.** O bloco 6 chamava `PlanetDestroy(pl)` direto -- ou seja, pulava o
	///     `UsarTecnica`, que e onde mora o `SabeTecnica`. Com isso, o custo de 10 marcos da skill
	///     (`skills.json`: `custo: 10`, `custofixo: 1`) nao era medido em lugar nenhum: um servidor que
	///     desse Planet Destroy de graça a todo mundo passava na bancada inteira. Aqui o verbo entra
	///     pela porta de producao, com o id que o cliente manda.
	///   * **O BP CONTRA A GRAVIDADE.** Nao havia checagem nenhuma. E ele tem uma armadilha propria:
	///     gravidade maior sobe o limiar E derruba o `expressedBP` (`StatCurves.GravFelt`), entao
	///     "recusou num planeta pesado" sozinho nao prova nada. A medida aqui e feita em duas metades
	///     separadas -- a conta (`MortePlanetaria.BpExigido`, uma casa so, no Core) e a obediencia do
	///     verbo a ela -- e o par vive/morre com o MESMO BP e o MESMO Ki, mudando so a gravidade.
	/// ============================================================================================
	/// </summary>
	private void MedirOsGates(Checagem Checa, ServerPlayer pl)
	{
		const string Path = "/datum/skill/Ki_Control/Planet_Destroy";

		if (_skills == null) { Checa("o catalogo de skills carregou", false); return; }

		_mortos.Limpar();
		_cargaDoPlanetDestroy.Clear();

		ZoneKey voltaZona = pl.Zone;
		Vec2 voltaPos = pl.Pos;
		MoveToZone(pl.Id, Cobaia, PontoDeNascimento(Cobaia));

		pl.Ficha.dead = false;
		pl.Ficha.KO = false;
		pl.Combate?.Corpo.Curar(1e9);
		pl.Combate?.SincronizarVida();

		// ---- 1. VILAO, NA PORTA DO APRENDIZADO (que e onde o DM o poe) ----
		// `villainonly = 1 //only an admin-designated Villain can learn it` (`Planets.dm:382`): a
		// palavra e **learn**. O bloco 6 media o bit no verbo; aqui ele e medido onde ele nasce.
		pl.Livro.Esquecer(Path);
		pl.Livro.MarcosLivres = 99;
		pl.Ficha.isVillain = false;

		Aprender(pl, Path);
		Checa("**QUEM NAO E VILAO NAO APRENDE** Planet Destroy (o `villainonly` de Planets.dm:382)",
			  !pl.Livro.Sabe(Path));

		// ---- 2. OS 10 MARCOS, os dois sentidos ----
		int custo = SkillCatalog.CustoDe(_skills.Get(Path)!);

		pl.Ficha.isVillain = true;
		pl.Livro.MarcosLivres = custo - 1;
		Aprender(pl, Path);

		// O NUMERO E LIDO ANTES DA CHAMADA, e nao dentro da mensagem: com o gate quebrado o
		// `Aprender` GASTA os marcos, e a linha vermelha sairia dizendo "-1 marcos" -- descrevendo o
		// estrago em vez da montagem.
		Checa($"...e nem o vilao aprende com {custo - 1} marcos (a skill custa {custo})",
			  !pl.Livro.Sabe(Path));

		pl.Livro.MarcosLivres = custo;
		Aprender(pl, Path);
		Checa("**E COM OS MARCOS NA MAO ELE APRENDE** (o portao abre, e nao e uma parede)",
			  pl.Livro.Sabe(Path));
		Checa("...e os marcos foram COBRADOS (aprender nao e de graca)",
			  pl.Livro.MarcosLivres == 0, $"restaram {pl.Livro.MarcosLivres}");

		// ---- 3. O VERBO PELA PORTA DE PRODUCAO, e o gate do `SabeTecnica` ----
		// Daqui pra baixo tudo entra por `UsarTecnica(pl, "Planet_Destroy")` -- o mesmo id que o
		// cliente manda. E o que liga os 10 marcos acima ao botao de verdade.
		pl.Ficha.BP = 1e12;
		pl.Ficha.Tick(agoraMs: NowMs());
		pl.Ficha.Ki = 99_999;

		pl.Livro.Esquecer(Path);
		_cargaDoPlanetDestroy.Clear();
		UsarTecnica(pl, "Planet_Destroy");
		Checa("**SEM TER COMPRADO A SKILL O VERBO NAO SAI** -- e e por AQUI que os 10 marcos mordem",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id));

		pl.Livro.Dar(Path);
		pl.Ficha.Ki = 99_999;
		UsarTecnica(pl, "Planet_Destroy");
		Checa("...e com a skill na mao ele sai pelo mesmo caminho do cliente",
			  _cargaDoPlanetDestroy.ContainsKey(pl.Id));

		// ---- 4. A CONTA DA GRAVIDADE, isolada da ficha ----
		Checa("o exigido e `10000 x Planetgrav` (Planets.dm:326): 1x pede 10 mil, 10x pede 100 mil",
			  Math.Abs(MortePlanetaria.BpExigido(1) - 10_000) < 0.001
			  && Math.Abs(MortePlanetaria.BpExigido(10) - 100_000) < 0.001,
			  $"{MortePlanetaria.BpExigido(1):0} / {MortePlanetaria.BpExigido(10):0}");
		Checa("...e gravidade abaixo de 1 nao barateia (o piso -- no espaco `Planetgrav` e 0)",
			  Math.Abs(MortePlanetaria.BpExigido(0) - 10_000) < 0.001);

		// ---- 5. E O VERBO OBEDECE A ELA: mesmo BP, mesmo Ki, so a gravidade muda ----
		// ============================ A JANELA ENTRE OS DOIS LIMIARES ============================
		// O BP e escolhido pra cair ENTRE `BpExigido(leve)` e `BpExigido(pesado)`, **medido ja no chao
		// pesado**. Assim o unico fato que muda de um lado pro outro e o limiar: no chao pesado o
		// `expressedBP` esta abaixo do exigido, e no leve -- com a MESMA ficha -- ele esta acima, porque
		// alem de o limiar cair, a gravidade menor devolve poder. As duas coisas puxam pro mesmo lado, e
		// e por isso que a conta acima e afirmada em separado: aqui se mede obediencia, nao aritmetica.
		// ====================================================================================
		double gravLeve = 1, gravPesada = 10;
		double alvo = (MortePlanetaria.BpExigido(gravLeve) + MortePlanetaria.BpExigido(gravPesada)) / 2;

		pl.Ficha.Planetgrav = gravPesada;
		AjustarExpressoPara(pl, alvo);
		double bpNaJanela = pl.Ficha.BP;

		Checa("(montagem) com a ficha assim, o BP expresso cai ENTRE os dois limiares",
			  pl.Ficha.expressedBP > MortePlanetaria.BpExigido(gravLeve)
			  && pl.Ficha.expressedBP < MortePlanetaria.BpExigido(gravPesada),
			  $"expresso {pl.Ficha.expressedBP:0} entre {MortePlanetaria.BpExigido(gravLeve):0} "
			  + $"e {MortePlanetaria.BpExigido(gravPesada):0}");

		pl.Ficha.Ki = 99_999;
		_cargaDoPlanetDestroy.Clear();
		UsarTecnica(pl, "Planet_Destroy");
		Checa($"**NUM CHAO {gravPesada:0}x MAIS PESADO ESSE BP NAO BASTA** (o `10000 * Planetgrav`)",
			  !_cargaDoPlanetDestroy.ContainsKey(pl.Id));

		pl.Ficha.BP = bpNaJanela;
		pl.Ficha.Planetgrav = gravLeve;
		pl.Ficha.Tick(agoraMs: NowMs());
		pl.Ficha.Ki = 99_999;
		UsarTecnica(pl, "Planet_Destroy");
		Checa("...e **O MESMO BP E O MESMO KI BASTAM** num chao de gravidade 1 -- so a gravidade mudou",
			  _cargaDoPlanetDestroy.ContainsKey(pl.Id),
			  $"BP {pl.Ficha.BP:0}, expresso {pl.Ficha.expressedBP:0}");

		// ---- 6. OS 30 S, OS DOIS SENTIDOS ----
		// O bloco 6 so via o "sim" no 30o segundo. Sem o "ainda nao" no 29o, um `SegundosDeCarga`
		// zerado -- ou um tique que acendesse a destruicao na hora -- passaria despercebido, e a janela
		// pra nocautear o vilao (que e a unica defesa que existe contra isto) nao existiria.
		_mortos.Limpar();
		_cargaDoPlanetDestroy.Clear();
		pl.Ficha.BP = 1e12;
		pl.Ficha.Tick(agoraMs: NowMs());
		pl.Ficha.Ki = 99_999;
		UsarTecnica(pl, "Planet_Destroy");

		for (int i = 0; i < (int)MortePlanetaria.SegundosDeCarga - 1; i++) TickDaDestruicao(1);
		Checa($"**NO {MortePlanetaria.SegundosDeCarga - 1:0}o SEGUNDO A CARGA AINDA NAO CONDENOU NADA** "
			+ "(o outro sentido dos 30 s: a janela e uma janela)",
			  _cargaDoPlanetDestroy.ContainsKey(pl.Id) && MorteDaZona(Cobaia) == null,
			  $"carga={_cargaDoPlanetDestroy.ContainsKey(pl.Id)}, planeta={MorteDaZona(Cobaia)?.Fase}");

		TickDaDestruicao(1);
		TickDaDestruicao(1);
		Checa($"...e no {MortePlanetaria.SegundosDeCarga:0}o ela acende (o portao abre, nao trava)",
			  MorteDaZona(Cobaia)?.Fase == FaseDaMorte.Explodindo, $"{MorteDaZona(Cobaia)?.Fase}");

		AbortarMorte(Cobaia, "bancada-gates");
		MoveToZone(pl.Id, voltaZona, voltaPos);
	}

	/// <summary>
	/// PROCURA O BP QUE PRODUZ ESTE `expressedBP`, por bisseccao geometrica na cadeia de PRODUCAO.
	///
	/// Por que bisseccao e nao uma formula: `PowerLevel()` e uma cadeia de dezenas de fatores
	/// (gravidade, idade, Ki, HP, forma, raiva...) e inverte-la aqui seria escrever uma SEGUNDA casa da
	/// mesma conta -- exatamente o que a regra 0.4 proibe. A bisseccao pergunta a casa que existe.
	///
	/// `Tick` e barato e puro pra este fim (`Statify` + `PowerLevel` + `WeightTick`): ele nao treina,
	/// nao envelhece e nao regenera. Chamar 90 vezes nao move o personagem um milimetro.
	/// </summary>
	private void AjustarExpressoPara(ServerPlayer pl, double alvo)
	{
		double baixo = 1, alto = 1e18;
		for (int i = 0; i < 90; i++)
		{
			double meio = Math.Sqrt(baixo * alto);
			pl.Ficha.BP = meio;
			pl.Ficha.Tick(agoraMs: NowMs());
			if (pl.Ficha.expressedBP > alvo) alto = meio; else baixo = meio;
		}
		pl.Ficha.BP = baixo;
		pl.Ficha.Tick(agoraMs: NowMs());
	}
}
