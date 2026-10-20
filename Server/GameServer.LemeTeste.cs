using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--lemeteste` -- O CASCO ISOLA QUEM PILOTA (dono, 2026-10-19). O pedido e as escolhas dele estao no cabecalho de
/// `GameServer.Leme.cs`. Um cliente cru entra pela porta (o portao do despacho so existe pra quem manda pacote) e embarca num pod parado
/// aos pes dele -- tudo pelas portas de PRODUCAO (o verbo da tecla E, o `AplicarGravidade`, o tique do esmagamento, o `LerOsCampos`, o
/// `OlharProCeu`, o `TickDoCampoBio`, a busca do soco, o funil do dano, o fio, o `Verbo`, o console da nave grande):
///   E. NA TERRA, O EMBARQUE APAGA AS TECNICAS -- em duas rodadas (o agarrao pede um corpo que ande, e o raio na mao barra o escudo): o
///      Kaio-ken, o escudo de energia, as miragens do After Image, a guarda, a carga, o treino e a postura de absorver ki; depois o raio na
///      mao e o agarrao. Uma afirmacao por tecnica (cada linha do embarque tem a sua).
///   G. EM VEGETA (10x, dez vezes o que um Humano novo aguenta) -- a gravidade do chao nao entra: nao esmaga, nao prende, o tique nao fere
///      nem cansa, nao treina. Contra-exemplo: `LemeSenteAGravidadeDeTeste`.
///   M. A MAQUINA DE GRAVIDADE -- o campo de 10x aos pes do piloto nao o alcanca. Contra-exemplo: `LemeNoCampoDeTeste`.
///   L. A LUA CHEIA -- ao leme ela nao se anuncia: o casco e teto. Contra-exemplo: `LemeSemTetoDeTeste`.
///   D. O AR DIVINO -- num chao divino, ao leme nao se respira o de fora. Contra-exemplo: `LemeRespiraOArDeTeste`.
///   B. O CAMPO BIO -- a torre nao cura quem pilota. Contra-exemplo: `LemeNoCampoBioDeTeste`.
///   A. OS GOLPES -- o soco de quem esta colado nao acha o piloto, e o dano em area nao o fere. Contra-exemplo: `LemeApanhaDeTeste`.
///   F. PELO FIO -- a habilidade (o escudo), a guarda, o soco e o duplo clique do Zanzoken (o After Image do pedido) sao engolidos, com a
///      frase uma vez; soltar a guarda passa calado. Contra-exemplo: `LemeUsaPoderDeTeste`.
///   V. OS VERBOS DE PODER -- a tecla de uma forma, o gatilho da arma, o Materializar, a Genkidama e as ordens das copias recusam ao leme.
///      Mesmo contra-exemplo.
///   S. O DESEMBARQUE -- o chao volta a pesar na hora, com o aviso, e o corpo volta a ser alcancavel.
///   N. A NAVE GRANDE PARADA EM VEGETA -- pegar o leme nao pesa nem avisa peso; as tecnicas caem; o casco e o corpo da ponte ficam
///      intocaveis juntos e voltam a ser alcancaveis ao largar o leme; a nave que explode devolve o chao ao piloto.
///   C. O CONTRA-EXEMPLO DO EMBARQUE (`LemeLevaAsTecnicasDeTeste`), de volta na Terra: tudo aceso segue aceso ao leme.
///
/// O RELOGIO NAO ANDA: a bancada roda dentro de uma chamada, e cada tique de que ela precisa e chamado pela porta dele. Cada afirmacao
/// principal tem um rotulo proprio em negrito: e por ele que a contraprova por mutacao (o `ciclo.py` do agente `pilotoleme`) sabe onde
/// cada linha tirada do conserto tem de reprovar.
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDoLeme = "bancada_leme_ag";

	/// <summary>O Energy Shield: um buff de habilidade que um novato ergue (o `C2S.Habilidade` que a familia F manda pelo fio).</summary>
	private const string PathDoEscudoDaBancadaDoLeme = "/datum/skill/ki/EnergyShield";
	private const string VerboDoEscudoDaBancadaDoLeme = "Energy_Shield";

	private bool _lemeRodando, _lemeJaRodou;
	private int _lemeOk, _lemeFalhou;

	private void AfirmarLeme(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _lemeOk++; GD.Print($"[leme]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _lemeFalhou++; GD.PrintErr($"[leme]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>O que a bancada pos no mundo e tem de tirar no fim: o pod, as obras forjadas e a nave grande.</summary>
	private sealed class LemeDaBancada
	{
		public Nave? Pod;
		public readonly List<Obra> Obras = [];
		public Nave? Grande;
	}

	public void RodarBancadaDoLeme(int porta)
	{
		if (_lemeRodando || _lemeJaRodou) return;
		_lemeRodando = _lemeJaRodou = true;
		_lemeOk = _lemeFalhou = 0;
		GD.Print("[leme] ================ O CASCO ISOLA QUEM PILOTA (dono, 2026-10-19) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		ServerPlayer? pl = null;
		var palco = new LemeDaBancada();
		try
		{
			pl = mesa.EntraNoMundo(cli, ContaDaBancadaDoLeme, "Piloto");
			AfirmarLeme("(montagem) o cliente entra pela porta e e jogador", pl != null && EhJogador(pl) && pl.Combate != null && pl.Livro != null);
			if (pl == null || pl.Combate == null || pl.Livro == null) return;

			// AS SKILLS DO PEDIDO: o escudo (o buff que o fio pede), a Afterimage Technique (o duplo clique) e o Ki Unlocked, que e o que deixa
			// reunir energia (a carga da tecla C) -- o `SabeReunir` le este campo.
			pl.Livro.Dar(PathDoEscudoDaBancadaDoLeme);
			pl.Livro.Dar(PathDoZanzoken);
			pl.Ficha.MeditateGivesKiRegen = 1;

			ZoneKey terra = ZoneKey.Premade("Earth"), vegeta = ZoneKey.Premade("Vegeta");
			LevarAoPod(pl, palco, terra);
			OEmbarqueApagaAsTecnicas(pl, palco);
			OEmbarqueApagaORaioEOAgarrao(pl, palco);

			LevarAoPod(pl, palco, vegeta);
			AfirmarLeme("(montagem) de pe em Vegeta, o Humano novo e ESMAGADO e PRESO no chao -- a condicao ruim que o leme tem de desfazer",
				pl.Zone.Equals(vegeta) && Esmagamento.Gravidade(pl.Ficha) >= 10 && EsmagadoAgora(pl) && PrensadoPelaGravidade(pl),
				$"zona {pl.Zone.Name}; sente {Esmagamento.Gravidade(pl.Ficha):0.##}x contra {pl.Ficha.GravMastered:0.##}x");
			ComandoDeInteracao(pl, "nave_usar", "");
			AfirmarLeme("(montagem) o verbo da tecla E poe o corpo ao leme do pod em Vegeta", palco.Pod!.PilotoId == pl.Id && CascoIsola(pl),
				$"piloto {palco.Pod.PilotoId}");
			if (!CascoIsola(pl)) return;

			AGravidadeAoLeme(pl);
			AMaquinaNaoAlcanca(pl, palco);
			ALuaNaoPega(pl);
			OArDivinoNaoEntra(pl);
			ATorreNaoCura(pl, palco);
			OsGolpesNaoPegam(pl);
			OFioNaoLigaPoder(pl, cli, mesa);
			OsVerbosDePoder(pl);
			ODesembarqueDevolveOChao(pl, palco);
			ANaveGrandeAoLeme(pl, vegeta, palco);

			LevarAoPod(pl, palco, terra);
			OContraExemploDoEmbarque(pl, palco);
		}
		catch (Exception e)
		{
			AfirmarLeme("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			LemeSenteAGravidadeDeTeste = LemeNoCampoDeTeste = LemeSemTetoDeTeste = LemeRespiraOArDeTeste = false;
			LemeNoCampoBioDeTeste = LemeApanhaDeTeste = LemeUsaPoderDeTeste = LemeLevaAsTecnicasDeTeste = false;
			GodKiEmQualquerChaoDeTeste = false;
			AtividadeSemConferenciaDeTeste = false;
			if (pl != null && NaveDoPiloto(pl) is { } aoLeme)
			{
				if (NaveGrande.EhNaveGrande(aoLeme.Tipo)) PararDePilotar(pl, aoLeme);
				else Desembarcar(pl, aoLeme);
			}
			if (palco.Grande != null && _naves.Contains(palco.Grande)) DestruirNave(palco.Grande, null);
			if (pl != null) LimparAsTecnicasDaBancadaDoLeme(pl);
			foreach (Obra o in palco.Obras) _noChao.Remove(o);
			LerOsCampos();
			if (palco.Pod != null) _naves.Remove(palco.Pod);
			GravarNaves();
			LimparTudoDaBancada();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDoLeme + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[leme] nao apaguei '{ContaDaBancadaDoLeme}': {e.Message}"); }
			_lemeRodando = false;
			GD.Print($"[leme] ================ {_lemeOk} OK, {_lemeFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	/// <summary>
	/// O PILOTO NO PONTO DE NASCER DESTA ZONA, e o pod aos pes dele (parado, sem ninguem ao leme). A primeira chamada cria o pod; as
	/// seguintes o mudam de lugar -- e um so pod a bancada inteira.
	/// </summary>
	private void LevarAoPod(ServerPlayer pl, LemeDaBancada palco, ZoneKey zona)
	{
		if (!pl.Zone.Equals(zona)) MoveToZone(pl.Id, zona, PontoDeNascimento(zona));
		palco.Pod ??= new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs() };
		palco.Pod.PorZona(zona);
		palco.Pod.X = pl.Pos.X;
		palco.Pod.Y = pl.Pos.Y;
		if (!_naves.Contains(palco.Pod)) _naves.Add(palco.Pod);
	}

	/// <summary>Uma obra aparafusada aos pes de alguem, na zona dele -- a maquina de gravidade, a torre do Campo Bio.</summary>
	private Obra ObraAosPesDoPiloto(ServerPlayer pl, string tipo, LemeDaBancada palco)
	{
		var o = new Obra
		{
			Id = _proximaObraId++, Tipo = tipo, X = pl.Pos.X, Y = pl.Pos.Y, DonoConta = pl.Conta, DonoNome = pl.Name,
			Aparafusada = true, ErguidaEm = NowMs(),
		};
		o.PorZona(pl.Zone);
		_noChao.Add(o);
		palco.Obras.Add(o);
		return o;
	}

	/// <summary>O corpo inteiro de novo, fora de combate: o que uma cena feriu nao contamina a seguinte.</summary>
	private static void CurarOPilotoDaBancada(ServerPlayer pl)
	{
		foreach (BodyPart p in pl.Combate!.Corpo.Partes)
			if (!p.Decepado) p.Vida = p.VidaMax;
		pl.Combate.SincronizarVida();
		pl.Combate.EmCombate = 0;
		pl.Ficha.stamina = pl.Ficha.maxstamina;
	}

	/// <summary>
	/// A PRIMEIRA RODADA DO QUE O EMBARQUE APAGA, cada uma pela porta de producao e nesta ordem (o raio na mao barraria o escudo): o
	/// Kaio-ken, o escudo de energia (`UsarHabilidade`), as miragens do After Image (a porta da `--diagmiragem`), a guarda, a carga, o
	/// treino e a postura de absorver ki (a ficha de um androide de absorcao, que e quem a tem). Devolve se ficou tudo aceso.
	/// </summary>
	private bool AcenderAsTecnicasDoPiloto(ServerPlayer pl)
	{
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Ficha.stamina = pl.Ficha.maxstamina;
		if (!TemBuff(pl, BuffKaiokenG2)) Kaioken(pl, 2);
		if (!TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)) UsarHabilidade(pl, VerboDoEscudoDaBancadaDoLeme);
		LigarAsMiragensNaBancada(pl.Id);
		ErguerAGuarda(pl, true);
		if (!pl.Carregando) Carregar(pl, true);
		AtividadeSemConferenciaDeTeste = true;   // o pedido de treino confere o chao e o corpo: aqui so importa que o treino esteja ligado
		try { DefinirAtividade(pl, Protocol.Activity.Treinando); }
		finally { AtividadeSemConferenciaDeTeste = false; }
		pl.Ficha.AndroideAbsorcao = true;
		pl.Ficha.ki_absorb_stance = true;
		return TemBuff(pl, BuffKaiokenG2) && _kaiokenAmtG2.ContainsKey(pl.Id) && TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)
			&& _miragensG14.ContainsKey(pl.Id) && pl.Combate!.Bloqueando && pl.Carregando && pl.Ficha.train && pl.Ficha.ki_absorb_stance;
	}

	/// <summary>
	/// A SEGUNDA RODADA: o agarrao (um corpo forjado colado a leste -- quem agarra tem de poder andar) e o raio na mao (um Kamehameha na
	/// carga, pelo `Canalizar` de producao). Devolve quem foi agarrado, ou nulo.
	/// </summary>
	private ServerPlayer? AgarrarEAbrirORaio(ServerPlayer pl)
	{
		const float T = ZoneCollision.TileSize;
		ServerPlayer preso = Forjar("bancada: agarrado pelo piloto", pl.Pos + new Vec2(T, 0), 5_000);
		if (!preso.Zone.Equals(pl.Zone)) MoveToZone(preso.Id, pl.Zone, pl.Pos + new Vec2(T, 0));
		pl.Facing = Facing.East;
		AlternarAgarrao(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Canalizar(pl, "Kamehameha", 0, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 0.001, Velocidade = 1, AlcanceTiles = 6, Deflectivel = false, Nome = "Onda de Ki",
		});
		return pl.AgarrandoId == preso.Id ? preso : null;
	}

	/// <summary>
	/// APAGA TUDO O QUE A BANCADA ACENDEU, por fora do embarque -- e e isso que deixa cada cena comecar limpa mesmo quando a contraprova
	/// tirou uma linha do embarque (um Kaio-ken que sobrasse no pod da Terra mudaria a cena da gravidade em Vegeta).
	/// </summary>
	private void LimparAsTecnicasDaBancadaDoLeme(ServerPlayer pl)
	{
		if (TemBuff(pl, BuffKaiokenG2)) ReverterKaiokenG2(pl, porKO: false);
		_kaiokenAmtG2.Remove(pl.Id);
		DerrubarBuffs(pl);
		if (_miragensG14.TryGetValue(pl.Id, out MiragensDePeG14? miragens)) { miragens.Ate = 0; miragens.ProximaEm = long.MaxValue; }
		TickG14();
		if (_canais.TryGetValue(pl.Id, out CanalDeKi? canal)) FecharCanal(pl.Id, canal, null);
		if (pl.Carregando) Carregar(pl, false);
		pl.Combate?.Guardar(false);
		if (pl.Ficha.train || pl.Ficha.med) DefinirAtividade(pl, Protocol.Activity.Parado);
		pl.Ficha.ki_absorb_stance = false;
		pl.Ficha.AndroideAbsorcao = false;
		if (pl.AgarrandoId != 0) Soltar(pl, MotivoDaSoltura.Tecla);
		CurarOPilotoDaBancada(pl);
	}

	private string AcesasDoPiloto(ServerPlayer pl) =>
		$"kaio-ken {TemBuff(pl, BuffKaiokenG2)}/{_kaiokenAmtG2.ContainsKey(pl.Id)}, escudo {TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)}, "
		+ $"miragens {_miragensG14.ContainsKey(pl.Id)}, guarda {pl.Combate?.Bloqueando}, carga {pl.Carregando}, treino {pl.Ficha.train}, "
		+ $"postura {pl.Ficha.ki_absorb_stance}, raio {EnraizadoPorKi(pl.Id)}, agarrando {pl.AgarrandoId}";

	/// <summary>Desce do pod pelo mesmo verbo, se estiver nele.</summary>
	private void DescerDoPodDaBancada(ServerPlayer pl)
	{
		if (NaveDoPiloto(pl) is { } n && !NaveGrande.EhNaveGrande(n.Tipo)) ComandoDeInteracao(pl, "nave_usar", "");
	}

	// =====================================================================
	// E) NA TERRA, O EMBARQUE APAGA AS TECNICAS
	// =====================================================================
	private void OEmbarqueApagaAsTecnicas(ServerPlayer pl, LemeDaBancada palco)
	{
		GD.Print("[leme] -- E) na Terra, o embarque apaga as tecnicas acesas (rodada 1) --");
		double deflexaoBase = pl.Combate!.Deflexao;
		bool acesas = AcenderAsTecnicasDoPiloto(pl);
		AfirmarLeme("(montagem) antes de embarcar: o Kaio-ken, o escudo de energia, as miragens do After Image, a guarda, a carga, o treino e a "
				  + "postura de absorver ki acesos", acesas, AcesasDoPiloto(pl));

		List<string> ditos = Ouvir(() => ComandoDeInteracao(pl, "nave_usar", ""));
		TickG14();   // o relogio do efeito das miragens: as vencidas saem, e a deflexao emprestada volta
		AfirmarLeme("(montagem) o verbo da tecla E poe o corpo ao leme do pod", palco.Pod!.PilotoId == pl.Id && CascoIsola(pl), $"piloto {palco.Pod.PilotoId}");

		// UMA AFIRMACAO POR TECNICA: cada linha do embarque (`ApagarAsTecnicasAoLeme`) tem a sua (*"n da pra usar nenhuma skill ou poder
		// enquanto esta pilotando"*; o dono escolheu que as acesas CAEM, e que a forma fica)
		AfirmarLeme("**EMBARQUE: O KAIO-KEN APAGA** -- o buff e o multiplicador guardado", !TemBuff(pl, BuffKaiokenG2) && !_kaiokenAmtG2.ContainsKey(pl.Id),
			AcesasDoPiloto(pl));
		AfirmarLeme("**EMBARQUE: O ESCUDO DE ENERGIA CAI** (os buffs)", !TemBuff(pl, VerboDoEscudoDaBancadaDoLeme), AcesasDoPiloto(pl));
		AfirmarLeme("**EMBARQUE: AS MIRAGENS DO AFTER IMAGE PARAM** -- e a deflexao que elas emprestavam volta (pelo `Removed` do proprio efeito)",
			!_miragensG14.ContainsKey(pl.Id) && Math.Abs(pl.Combate.Deflexao - deflexaoBase) < 1e-9,
			$"miragens {_miragensG14.ContainsKey(pl.Id)}; deflexao {deflexaoBase} -> {pl.Combate.Deflexao}");
		AfirmarLeme("**EMBARQUE: A GUARDA BAIXA**", !pl.Combate.Bloqueando, AcesasDoPiloto(pl));
		AfirmarLeme("**EMBARQUE: A CARGA DE KI PARA**", !pl.Carregando, AcesasDoPiloto(pl));
		AfirmarLeme("**EMBARQUE: O TREINO PARA**", !pl.Ficha.train, AcesasDoPiloto(pl));
		AfirmarLeme("**EMBARQUE: A POSTURA DE ABSORVER KI DESLIGA** (ela fincaria a nave no chao, e desliga-la pede a tecla que o leme engole)",
			!pl.Ficha.ki_absorb_stance, AcesasDoPiloto(pl));
		AfirmarLeme("**EMBARQUE: O PILOTO OUVE O QUE SE APAGOU**", ditos.Contains(Leme.AsTecnicasSeApagam), string.Join(" | ", ditos));

		DescerDoPodDaBancada(pl);
		LimparAsTecnicasDaBancadaDoLeme(pl);
	}

	private void OEmbarqueApagaORaioEOAgarrao(ServerPlayer pl, LemeDaBancada palco)
	{
		GD.Print("[leme] -- E) na Terra, o embarque apaga o raio na mao e solta o agarrao (rodada 2) --");
		ServerPlayer? preso = AgarrarEAbrirORaio(pl);
		AfirmarLeme("(montagem) antes de embarcar: segurando um corpo, e com um Kamehameha na carga", preso != null && EnraizadoPorKi(pl.Id),
			AcesasDoPiloto(pl));

		ComandoDeInteracao(pl, "nave_usar", "");
		AfirmarLeme("(montagem) de novo ao leme do pod", palco.Pod!.PilotoId == pl.Id && CascoIsola(pl), $"piloto {palco.Pod.PilotoId}");
		AfirmarLeme("**EMBARQUE: O RAIO NA MAO SE APAGA** (ele enraizaria o corpo e travaria a altura da nave)", !EnraizadoPorKi(pl.Id), AcesasDoPiloto(pl));
		AfirmarLeme("**EMBARQUE: O AGARRAO SOLTA** (ninguem vai no colo de quem pilota)", pl.AgarrandoId == 0, AcesasDoPiloto(pl));

		DescerDoPodDaBancada(pl);
		LimparAsTecnicasDaBancadaDoLeme(pl);
		LimparTudoDaBancada();   // o corpo agarrado sai do mundo
	}

	// =====================================================================
	// G) A GRAVIDADE DO CHAO, EM VEGETA
	// =====================================================================
	private void AGravidadeAoLeme(ServerPlayer pl)
	{
		GD.Print("[leme] -- G) a gravidade do chao: Vegeta puxa 10x, e ao leme o corpo sente a do casco --");
		Fighter f = pl.Ficha;
		double chao = FichaDoChao(pl.Zone).Gravidade;
		AfirmarLeme("**AO LEME A GRAVIDADE DO CHAO NAO ENTRA** (*\"planetas com gravidade quando pilota a nave o personagem piloto sofre os efeitos "
				  + "da gravidade, oq n deveria acontecer\"*): o chao puxa 10x e o corpo sente 0",
			chao >= 10 && Esmagamento.Gravidade(f) == 0, $"chao {chao:0.##}x; sente {Esmagamento.Gravidade(f):0.##}x");
		AfirmarLeme("**AO LEME NAO HA CASTIGO DA GRAVIDADE**: nao e esmagado, nao e prensado no chao, e o corpo anda (a nave nao trava)",
			!EsmagadoAgora(pl) && !PrensadoPelaGravidade(pl) && PodeMexerOCorpo(pl),
			$"esmagado {EsmagadoAgora(pl)}; prensado {PrensadoPelaGravidade(pl)}; anda {PodeMexerOCorpo(pl)}");

		double vida = pl.Combate!.Corpo.Vida(), folego = f.stamina;
		for (int i = 0; i < 5; i++) TickDoEsmagamento();
		AfirmarLeme("**AO LEME O TIQUE DO ESMAGAMENTO NAO FERE NEM CANSA**: cinco segundos dele, e a vida e o folego nao andam",
			Math.Abs(pl.Combate.Corpo.Vida() - vida) < 1e-9 && Math.Abs(f.stamina - folego) < 1e-9,
			$"vida {vida:0.###} -> {pl.Combate.Corpo.Vida():0.###}; folego {folego:0.###} -> {f.stamina:0.###}");

		double maestria = f.GravMastered;
		f.GravGain();
		AfirmarLeme("**AO LEME NAO SE TREINA PELA GRAVIDADE DE FORA**: o fator do ganho e 1 e a maestria nao sobe",
			Math.Abs(f.FatorDaGravidade() - 1) < 1e-9 && f.GravMastered == maestria,
			$"fator {f.FatorDaGravidade():0.###}; maestria {maestria:0.######} -> {f.GravMastered:0.######}");

		LemeSenteAGravidadeDeTeste = true;
		bool esmagadoNoDefeito;
		double folegoNoDefeito;
		try
		{
			AplicarGravidade(pl);
			esmagadoNoDefeito = Esmagamento.Gravidade(f) >= 10 && EsmagadoAgora(pl) && PrensadoPelaGravidade(pl);
			TickDoEsmagamento();
			folegoNoDefeito = f.stamina;
		}
		finally
		{
			LemeSenteAGravidadeDeTeste = false;
			AplicarGravidade(pl);
		}
		AfirmarLeme("(defeito injetado: ao leme o corpo sente o chao) o piloto volta a ser esmagado e preso, e o tique tira o folego -- a regua de cima REPROVA",
			esmagadoNoDefeito && folegoNoDefeito < folego - 1e-9, $"esmagado e preso {esmagadoNoDefeito}; folego {folego:0.###} -> {folegoNoDefeito:0.###}");
		CurarOPilotoDaBancada(pl);
	}

	// =====================================================================
	// M) A MAQUINA DE GRAVIDADE
	// =====================================================================
	private void AMaquinaNaoAlcanca(ServerPlayer pl, LemeDaBancada palco)
	{
		GD.Print("[leme] -- M) a maquina de gravidade ligada aos pes de quem pilota --");
		Obra maquina = ObraAosPesDoPiloto(pl, "Gravity", palco);
		maquina.Gravidade = new GravidadeDaObra { Grav = 10, Range = 2 };
		LerOsCampos();
		bool ligada = _camposLigados.Contains(maquina);
		AfirmarLeme("**O CAMPO DA MAQUINA TAMBEM NAO ALCANCA O LEME**: uma maquina de 10x ligada aos pes do piloto, e o campo dele segue zero",
			ligada && pl.Ficha.gravmult == 0, $"ligada {ligada}; campo {pl.Ficha.gravmult:0.##}x");

		LemeNoCampoDeTeste = true;
		double noDefeito;
		try { LerOsCampos(); noDefeito = pl.Ficha.gravmult; }
		finally { LemeNoCampoDeTeste = false; LerOsCampos(); }
		AfirmarLeme("(defeito injetado: o campo alcanca o leme) o piloto sente os 10x da maquina -- a regua de cima REPROVA",
			noDefeito >= 10, $"campo {noDefeito:0.##}x");

		_noChao.Remove(maquina);
		palco.Obras.Remove(maquina);
		LerOsCampos();
	}

	// =====================================================================
	// L) A LUA CHEIA
	// =====================================================================
	private void ALuaNaoPega(ServerPlayer pl)
	{
		GD.Print("[leme] -- L) a lua cheia no ceu de Vegeta --");
		var cheia = new EstadoDoCeu { Fase = Ceu.Cheia, Altura = 0.8, Noite = true, Aceso = 1, Tamanho = 1 };
		void Esquecer() { pl.LuaEstavaNoCeu = false; pl.LuaVista = 0; }

		// AO AR LIVRE: o ponto de nascer de Vegeta fica debaixo de um TETO do mapa (a primeira rodada achou -- o contra-exemplo ficou mudo, e a
		// regua de cima passava verde sem o casco). A cena vai pro chao descoberto mais perto, e a montagem diz que ele e descoberto.
		Vec2 deOnde = pl.Pos;
		pl.Pos = AoArLivrePertoDoPiloto(pl);
		try
		{
			AfirmarLeme("(montagem) o piloto esta ao ar livre: o chao dele nao tem teto", !SobTeto(pl.Zone, pl.Pos),
				$"de ({deOnde.X:0},{deOnde.Y:0}) pra ({pl.Pos.X:0},{pl.Pos.Y:0})");

			Esquecer();
			List<string> aoLeme = Ouvir(() => OlharProCeu(pl, cheia));
			AfirmarLeme("**A LUA CHEIA NAO ALCANCA QUEM PILOTA** (o casco e teto): nenhuma fala de lua, e a memoria dela segue zerada pra hora de desembarcar",
				!Disse(aoLeme, "lua cheia") && !pl.LuaEstavaNoCeu, $"{string.Join(" | ", aoLeme)}; estava no ceu {pl.LuaEstavaNoCeu}");

			LemeSemTetoDeTeste = true;
			List<string> semTeto;
			try { Esquecer(); semTeto = Ouvir(() => OlharProCeu(pl, cheia)); }
			finally { LemeSemTetoDeTeste = false; Esquecer(); }
			AfirmarLeme("(defeito injetado: o casco nao e teto) a lua cheia se anuncia ao piloto -- a regua de cima REPROVA",
				Disse(semTeto, "lua cheia"), string.Join(" | ", semTeto));
		}
		finally { pl.Pos = deOnde; }
	}

	/// <summary>O ponto descoberto (sem teto no mapa, nem caido) mais perto do piloto, em aneis de um tile; o proprio ponto se nada servir em 40.</summary>
	private Vec2 AoArLivrePertoDoPiloto(ServerPlayer pl)
	{
		const float T = ZoneCollision.TileSize;
		for (int r = 0; r <= 40; r++)
			for (int dx = -r; dx <= r; dx++)
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;
					Vec2 p = pl.Pos + new Vec2(dx * T, dy * T);
					if (!SobTeto(pl.Zone, p)) return p;
				}
		return pl.Pos;
	}

	// =====================================================================
	// D) O AR DIVINO
	// =====================================================================
	private void OArDivinoNaoEntra(ServerPlayer pl)
	{
		GD.Print("[leme] -- D) o ar de um mundo divino --");
		GodKiEmQualquerChaoDeTeste = true;   // a porta da `--godkiteste`: o chao de Vegeta passa a ter atmosfera divina
		try
		{
			bool aoLeme = EmAtmosferaDivina(pl);
			LemeRespiraOArDeTeste = true;
			bool noDefeito;
			try { noDefeito = EmAtmosferaDivina(pl); }
			finally { LemeRespiraOArDeTeste = false; }
			AfirmarLeme("**O AR DIVINO NAO ENTRA NO CASCO**: num chao de atmosfera divina, ao leme nao se respira o de fora (o medidor e o bit da tela saem daqui)",
				!aoLeme, $"na atmosfera {aoLeme}");
			AfirmarLeme("(defeito injetado: ao leme se respira o ar de fora) o piloto esta na atmosfera divina -- a regua de cima REPROVA", noDefeito);
		}
		finally { GodKiEmQualquerChaoDeTeste = false; }
	}

	// =====================================================================
	// B) O CAMPO BIO
	// =====================================================================
	private void ATorreNaoCura(ServerPlayer pl, LemeDaBancada palco)
	{
		GD.Print("[leme] -- B) a torre do Campo Bio colada no piloto ferido --");
		Obra torre = ObraAosPesDoPiloto(pl, "Bio_Field", palco);
		BodyPart ferido = pl.Combate!.Corpo.Partes.First(p => !p.Decepado && !p.Aninhado && p.VidaMax > 40);
		ferido.Vida = ferido.VidaMax - 30;
		pl.Combate.SincronizarVida();
		pl.Combate.EmCombate = 0;
		double antes = pl.Combate.Corpo.Vida();
		AfirmarLeme("(montagem) o piloto esta ferido e fora de combate -- o corpo que a torre curaria", pl.Ficha.HP < 99.99 && pl.Combate.EmCombate <= 0,
			$"HP {pl.Ficha.HP:0.##}");

		TickDoCampoBio(1);
		AfirmarLeme("**A TORRE DO CAMPO BIO NAO CURA QUEM PILOTA**: um segundo colado nela, e a vida nao anda",
			Math.Abs(pl.Combate.Corpo.Vida() - antes) < 1e-9, $"{antes:0.###} -> {pl.Combate.Corpo.Vida():0.###}");

		LemeNoCampoBioDeTeste = true;
		try { TickDoCampoBio(1); }
		finally { LemeNoCampoBioDeTeste = false; }
		AfirmarLeme("(defeito injetado: a torre alcanca o leme) o mesmo segundo cura o piloto -- a regua de cima REPROVA",
			pl.Combate.Corpo.Vida() > antes + 1e-9, $"{antes:0.###} -> {pl.Combate.Corpo.Vida():0.###}");

		_noChao.Remove(torre);
		palco.Obras.Remove(torre);
		CurarOPilotoDaBancada(pl);
	}

	// =====================================================================
	// A) OS GOLPES
	// =====================================================================
	private void OsGolpesNaoPegam(ServerPlayer pl)
	{
		GD.Print("[leme] -- A) quem esta colado bate no piloto --");
		const float T = ZoneCollision.TileSize;
		Vec2 aOeste = pl.Pos - new Vec2(T, 0);
		ServerPlayer agressor = Forjar("bancada: agressor do leme", aOeste, 5_000_000);
		if (!agressor.Zone.Equals(pl.Zone)) MoveToZone(agressor.Id, pl.Zone, aOeste);   // a forja nasce na zona das bancadas de projetil
		agressor.Facing = Facing.East;
		pl.Facing = Facing.West;
		AfirmarLeme("(montagem) o agressor esta colado no piloto, de frente pra ele", agressor.Zone.Equals(pl.Zone) && (agressor.Pos - pl.Pos).Length <= T + 1,
			$"{agressor.Zone.Name} ({agressor.Pos.X:0},{agressor.Pos.Y:0}) x ({pl.Pos.X:0},{pl.Pos.Y:0})");

		double antes = pl.Combate!.Corpo.Vida();
		ServerPlayer? alvo = AlvoNaFrente(agressor);
		EspalharDanoG3(pl, agressor, 5, letal: true);
		AfirmarLeme("**GOLPE NENHUM PEGA EM QUEM PILOTA** (o dono escolheu: os golpes de outros nao pegam): a busca do soco de quem esta colado nao "
				  + "o acha, e o dano em area -- o funil de todo dano -- nao o fere",
			pl.Combate.Intocavel && alvo != pl && Math.Abs(pl.Combate.Corpo.Vida() - antes) < 1e-9,
			$"intocavel {pl.Combate.Intocavel}; o soco acha {alvo?.Name ?? "ninguem"}; vida {antes:0.###} -> {pl.Combate.Corpo.Vida():0.###}");

		LemeApanhaDeTeste = true;
		ServerPlayer? alvoNoDefeito;
		try
		{
			alvoNoDefeito = AlvoNaFrente(agressor);
			EspalharDanoG3(pl, agressor, 5, letal: true);
		}
		finally { LemeApanhaDeTeste = false; }
		AfirmarLeme("(defeito injetado: o casco nao blinda) o soco acha o piloto e o dano o fere -- a regua de cima REPROVA",
			alvoNoDefeito == pl && pl.Combate.Corpo.Vida() < antes - 1e-9,
			$"o soco acha {alvoNoDefeito?.Name ?? "ninguem"}; vida {antes:0.###} -> {pl.Combate.Corpo.Vida():0.###}");

		LimparTudoDaBancada();   // o agressor sai do mundo: o fio da familia F nao tem em quem bater
		CurarOPilotoDaBancada(pl);
	}

	// =====================================================================
	// F) PELO FIO
	// =====================================================================
	private void OFioNaoLigaPoder(ServerPlayer pl, ClienteCruDeTeste cli, MesaDeClientesCrus mesa)
	{
		GD.Print("[leme] -- F) pelo fio: o despacho engole o que o piloto manda --");
		const float T = ZoneCollision.TileSize;
		void Mandar(NetDataWriter w) { cli.Mandar(w); mesa.Bombeia(() => false, 150); }
		NetDataWriter Guarda(bool erguer) { NetDataWriter w = Protocol.Begin(Protocol.C2S.Guard); w.Put(erguer); return w; }
		NetDataWriter Escudo() { NetDataWriter w = Protocol.Begin(Protocol.C2S.Habilidade); w.Put(VerboDoEscudoDaBancadaDoLeme); return w; }
		NetDataWriter Soco() { NetDataWriter w = Protocol.Begin(Protocol.C2S.Action); w.Put((byte)Protocol.Golpe.Leve); return w; }
		NetDataWriter Piscar(Vec2 d) { NetDataWriter w = Protocol.Begin(Protocol.C2S.Zanzoken); w.PutVec(d); return w; }

		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Facing = Facing.East;
		Vec2 onde = pl.Pos, destino = pl.Pos + new Vec2(2 * T, 0);
		long ataqueAntes = pl.AtaqueAte;
		string recusa = Leme.NaoSeUsaPoder("Spacepod");

		_avisosDoLeme.Remove(pl);   // o relogio do aviso zerado: a regua quer ouvir esta recusa, e nao uma de antes
		List<string> ditos = Ouvir(() => { Mandar(Escudo()); Mandar(Guarda(true)); Mandar(Soco()); Mandar(Piscar(destino)); });
		AfirmarLeme("**AO LEME O FIO NAO LIGA PODER NENHUM** (*\"tb ta dando pra usar after image enquanto pilota a nave\"*): o escudo pedido, a guarda, "
				  + "o soco e o duplo clique do Zanzoken chegam ao despacho e nada acontece",
			!TemBuff(pl, VerboDoEscudoDaBancadaDoLeme) && !pl.Combate!.Bloqueando && pl.AtaqueAte == ataqueAntes && (pl.Pos - onde).Length < 1,
			$"escudo {TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)}; guarda {pl.Combate?.Bloqueando}; soco {pl.AtaqueAte != ataqueAntes}; "
			+ $"andou {(pl.Pos - onde).Length:0.#} px");
		AfirmarLeme("**O PILOTO OUVE POR QUE, UMA VEZ** pelos quatro pacotes (o aviso espera tres segundos)",
			ditos.Count(d => d == recusa) == 1, string.Join(" | ", ditos));

		_avisosDoLeme.Remove(pl);
		List<string> soltou = Ouvir(() => Mandar(Guarda(false)));
		AfirmarLeme("**SOLTAR A GUARDA AO LEME PASSA CALADO**: desligar nao e usar poder (e engolir a tecla solta so faria as duas pontas discordarem)",
			!soltou.Contains(recusa), string.Join(" | ", soltou));

		// O CONTRA-EXEMPLO COMECA LIMPO: o que a regua de cima deixou passar (se deixou) nao pode decidir o que este mede
		if (TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)) DesligarBuff(pl, VerboDoEscudoDaBancadaDoLeme);
		pl.Combate!.Guardar(false);
		pl.Combate.Recarga = 0;
		pl.Ficha.Ki = pl.Ficha.MaxKi;   // o escudo e a piscada de cima, se passaram, cobraram Ki
		pl.Pos = onde;
		long socoAntes = pl.AtaqueAte;

		LemeUsaPoderDeTeste = true;
		bool escudo, guarda, socou, piscou;
		try
		{
			Mandar(Escudo());
			escudo = TemBuff(pl, VerboDoEscudoDaBancadaDoLeme);
			Mandar(Guarda(true));
			guarda = pl.Combate.Bloqueando;
			Mandar(Guarda(false));
			Mandar(Soco());
			socou = pl.AtaqueAte != socoAntes;
			Mandar(Piscar(destino));
			piscou = (pl.Pos - onde).Length > T / 2;   // o alcance de um novato pode ser de um tile so (`ZanzorangeG10`): longe demais encurta
		}
		finally { LemeUsaPoderDeTeste = false; }
		AfirmarLeme("(defeito injetado: o despacho deixa o piloto agir) o escudo sobe pelo fio -- a regua de cima REPROVA", escudo);
		AfirmarLeme("   (o mesmo defeito) a guarda ergue", guarda);
		AfirmarLeme("   (o mesmo defeito) o soco sai", socou, $"ataque {socoAntes} -> {pl.AtaqueAte}");
		AfirmarLeme("   (o mesmo defeito) o duplo clique do Zanzoken pisca o piloto -- o After Image do pedido", piscou,
			$"de ({onde.X:0},{onde.Y:0}) pra ({pl.Pos.X:0},{pl.Pos.Y:0})");

		if (TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)) DesligarBuff(pl, VerboDoEscudoDaBancadaDoLeme);
		pl.Combate.Guardar(false);
		pl.Pos = onde;
		CurarOPilotoDaBancada(pl);
	}

	// =====================================================================
	// V) OS VERBOS DE PODER
	// =====================================================================
	private void OsVerbosDePoder(ServerPlayer pl)
	{
		GD.Print("[leme] -- V) os poderes que viajam pelo canal de verbos --");
		string recusa = Leme.NaoSeUsaPoder("Spacepod");
		(string Cmd, string Arg, string Nome)[] poderes =
		[
			("forma", "base", "a tecla de uma forma"),
			(ArmasDeFogo.VerboAtirar, "", "o gatilho da arma de fogo"),
			(Materializacao.VerboDeCanal, "Senzu", "o Materializar"),
			("genkidama_doar", "", "a energia doada a Genkidama"),
			("splitform_seguir", "", "as ordens das copias do Split Form"),
		];
		foreach ((string cmd, string arg, string nome) in poderes)
		{
			_avisosDoLeme.Remove(pl);
			List<string> aoLeme = Ouvir(() => Verbo(pl, cmd, arg));
			LemeUsaPoderDeTeste = true;
			List<string> noDefeito;
			try { noDefeito = Ouvir(() => Verbo(pl, cmd, arg)); }
			finally { LemeUsaPoderDeTeste = false; }
			AfirmarLeme($"**AO LEME, {nome.ToUpperInvariant()} RECUSA** pelo canal de verbos (`{cmd}`), com a frase do leme e so ela",
				aoLeme.Count == 1 && aoLeme[0] == recusa, string.Join(" | ", aoLeme));
			AfirmarLeme($"   (defeito injetado: o verbo passa) {nome} cai no proprio sistema, sem a frase do leme -- a regua de cima REPROVA",
				!noDefeito.Contains(recusa), string.Join(" | ", noDefeito));
		}
	}

	// =====================================================================
	// S) O DESEMBARQUE
	// =====================================================================
	private void ODesembarqueDevolveOChao(ServerPlayer pl, LemeDaBancada palco)
	{
		GD.Print("[leme] -- S) o desembarque em Vegeta --");
		List<string> desceu = Ouvir(() => ComandoDeInteracao(pl, "nave_usar", ""));
		AfirmarLeme("**DESEMBARCADO, O CHAO VOLTA A PESAR NA HORA**: Vegeta de novo a 10x, esmagando -- e o aviso do chao diz",
			!CascoIsola(pl) && palco.Pod!.PilotoId == 0 && Esmagamento.Gravidade(pl.Ficha) >= 10 && EsmagadoAgora(pl) && Disse(desceu, "puxa"),
			$"ao leme {CascoIsola(pl)}; sente {Esmagamento.Gravidade(pl.Ficha):0.##}x; {string.Join(" | ", desceu)}");
		AfirmarLeme("...e desembarcado ele volta a ser alcancavel (o escudo do casco caiu no mesmo gesto)", !pl.Combate!.Intocavel);
	}

	// =====================================================================
	// N) A NAVE GRANDE
	// =====================================================================
	private void ANaveGrandeAoLeme(ServerPlayer pl, ZoneKey vegeta, LemeDaBancada palco)
	{
		GD.Print("[leme] -- N) a nave grande parada em Vegeta: o leme, a ponte e a explosao --");
		const float T = ZoneCollision.TileSize;
		var grande = new Nave
		{
			Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, X = pl.Pos.X + 2 * T, Y = pl.Pos.Y, DonoConta = pl.Conta, DonoNome = pl.Name,
			ErguidaEm = NowMs(), ArmaduraMax = 1000, Armadura = 1000,
		};
		grande.PorZona(vegeta);
		_naves.Add(grande);
		palco.Grande = grande;
		// O POD SAI DA FRENTE: ele esta aos pes do piloto, e a tecla E acha a nave parada MAIS PERTO (a primeira rodada embarcava no pod). Ele
		// volta pra lista no `LevarAoPod` da familia C.
		if (palco.Pod != null) _naves.Remove(palco.Pod);

		ComandoDeInteracao(pl, "nave_embarcar", "");
		bool dentro = NaveGrande.EhInterior(pl.Zone, out int idDoInterior) && idDoInterior == grande.Id;
		AfirmarLeme("(montagem) o dono embarca na Capital Ship parada em Vegeta, pelo verbo da tecla E", dentro, $"zona {pl.Zone}");
		if (!dentro) return;

		pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		UsarHabilidade(pl, VerboDoEscudoDaBancadaDoLeme);
		bool escudoNaPonte = TemBuff(pl, VerboDoEscudoDaBancadaDoLeme);

		List<string> pegou = Ouvir(() => ComandoDeInteracao(pl, "nave_pilotar", ""));
		ServerPlayer? ponte = pl.BonecoLargado;
		AfirmarLeme("(montagem) do console, o leme: a atencao vai pra fora, em Vegeta, e o corpo fica de pe na ponte",
			grande.PilotoId == pl.Id && pl.Zone.Equals(vegeta) && ponte != null, $"piloto {grande.PilotoId}; zona {pl.Zone.Name}; corpo na ponte {ponte != null}");
		AfirmarLeme("**PEGAR O LEME NUM CHAO DE 10x NAO PESA NEM UM INSTANTE**: o piloto ja chega la fora sentindo 0, sem o aviso \"o chao de Vegeta puxa\"",
			Esmagamento.Gravidade(pl.Ficha) == 0 && !Disse(pegou, "puxa"), $"sente {Esmagamento.Gravidade(pl.Ficha):0.##}x; {string.Join(" | ", pegou)}");
		AfirmarLeme("**AS TECNICAS ACESAS CAEM TAMBEM AO LEME DA NAVE GRANDE** (o escudo erguido na ponte)",
			escudoNaPonte && !TemBuff(pl, VerboDoEscudoDaBancadaDoLeme) && pegou.Contains(Leme.AsTecnicasSeApagam),
			$"escudo na ponte {escudoNaPonte}, ao leme {TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)}");
		AfirmarLeme("**O CASCO E O CORPO DA PONTE FICAM FORA DE ALCANCE AO LEME** (o dono: \"blindar os dois ao leme\"): os dois dividem o estado de "
				  + "combate, e os dois estao intocaveis",
			pl.Combate!.Intocavel && ponte is { Combate.Intocavel: true });

		ComandoDeInteracao(pl, "nave_pilotar", "");   // o mesmo verbo larga o leme: volta pro corpo na ponte
		AfirmarLeme("...e largou o leme, o corpo volta a ser alcancavel, de pe na ponte",
			!CascoIsola(pl) && !pl.Combate.Intocavel && pl.BonecoLargado == null && NaveGrande.EhInterior(pl.Zone, out _),
			$"ao leme {CascoIsola(pl)}; intocavel {pl.Combate.Intocavel}; zona {pl.Zone}");

		if (TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)) DesligarBuff(pl, VerboDoEscudoDaBancadaDoLeme);
		ComandoDeInteracao(pl, "nave_pilotar", "");   // de novo ao leme...
		AfirmarLeme("(montagem) de novo ao leme da nave grande", grande.PilotoId == pl.Id, $"piloto {grande.PilotoId}");
		DestruirNave(grande, null);                   // ...e ela explode com ele la
		palco.Grande = null;
		AfirmarLeme("**A NAVE QUE EXPLODE COM O PILOTO AO LEME DEVOLVE O CHAO NA HORA**: ele fica onde o casco estava, em Vegeta, e volta a sentir os 10x",
			!CascoIsola(pl) && pl.Zone.Equals(vegeta) && Esmagamento.Gravidade(pl.Ficha) >= 10 && !pl.Combate.Intocavel,
			$"zona {pl.Zone.Name}; sente {Esmagamento.Gravidade(pl.Ficha):0.##}x");
	}

	// =====================================================================
	// C) O CONTRA-EXEMPLO DO EMBARQUE, DE VOLTA NA TERRA
	// =====================================================================
	private void OContraExemploDoEmbarque(ServerPlayer pl, LemeDaBancada palco)
	{
		GD.Print("[leme] -- C) o embarque que levasse as tecnicas (o contra-exemplo das duas rodadas) --");
		bool acesas = AcenderAsTecnicasDoPiloto(pl);
		LemeLevaAsTecnicasDeTeste = true;
		try { ComandoDeInteracao(pl, "nave_usar", ""); TickG14(); }
		finally { LemeLevaAsTecnicasDeTeste = false; }
		AfirmarLeme("(defeito injetado: o embarque leva as tecnicas) ao leme seguem acesos o Kaio-ken, o escudo, as miragens, a guarda, a carga, o treino "
				  + "e a postura -- as reguas do embarque REPROVAM",
			acesas && CascoIsola(pl) && TemBuff(pl, BuffKaiokenG2) && _kaiokenAmtG2.ContainsKey(pl.Id) && TemBuff(pl, VerboDoEscudoDaBancadaDoLeme)
			&& _miragensG14.ContainsKey(pl.Id) && pl.Combate!.Bloqueando && pl.Carregando && pl.Ficha.train && pl.Ficha.ki_absorb_stance,
			AcesasDoPiloto(pl));
		DescerDoPodDaBancada(pl);
		LimparAsTecnicasDaBancadaDoLeme(pl);

		ServerPlayer? preso = AgarrarEAbrirORaio(pl);
		LemeLevaAsTecnicasDeTeste = true;
		try { ComandoDeInteracao(pl, "nave_usar", ""); }
		finally { LemeLevaAsTecnicasDeTeste = false; }
		AfirmarLeme("   (o mesmo defeito) o raio segue na mao e o corpo agarrado segue preso",
			preso != null && CascoIsola(pl) && EnraizadoPorKi(pl.Id) && pl.AgarrandoId == preso.Id, AcesasDoPiloto(pl));
		DescerDoPodDaBancada(pl);
		LimparAsTecnicasDaBancadaDoLeme(pl);
		LimparTudoDaBancada();
	}
}
