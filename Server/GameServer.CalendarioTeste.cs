using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--calendarioteste` -- O CALENDARIO E A IDADE QUE ANDA COM ELE (2026-10-17). O dono: *"a idade avança quando apssar 1 ano
/// ingame, se nao me engano no dm o ano eram 10 meses de 30 dias ingame algo assim e tinhas os meses e o ano, faça tudo isso"* -- e *"n
/// esqueça q nas salas do tempo a pessoa envelhece mais rapido mesmo com esse sistema de calendario"*.
///
/// O tempo anda pela MESMA manivela de producao das outras bancadas do ceu (`_adiantoDoCeu`, o relogio do universo), e quem vira o dia e
/// o mes e o tique de producao (`TickDoCeu`). Os jogadores entram pela porta (clientes crus), e a ficha lenta chega neles pelo leitor de
/// producao.
///
///   A. A CONTA DO CORE: a data de cada dia (28 por mes, 10 meses por ano, os nomes do DM), o mes corrido, e a conta dos meses de idade
///      (dez meses fecham um ano; quem nao envelhece deixa o tempo passar; o relogio que volta nao desenvelhece).
///   B. A ORIGEM: o boot deste mundo e o 1 de Luty do ano 1, o `calendario.json` guarda o dia da Terra e a leitura devolve o mesmo.
///   C. O JOGADOR NOVO comeca a contar no mes em que nasce (vai no save), e o dia que vira e anunciado.
///   D. O MES QUE VIRA anuncia a data e conta um mes na idade de quem esta no jogo -- e a ficha lenta leva os meses pelo fio.
///   E. O ANO: dez meses fecham um ano ("Você faz N anos."), e o 1 de Luty anuncia o ano novo.
///   F. O TEMPO LA FORA: a saida grava os meses; quem volta 25 meses depois volta 2 anos e 5 meses mais velho, e ouve a data e o tempo.
///   G. QUEM NAO ENVELHECE: o morto (que nao recupera os meses ao voltar) e o Deus da Destruicao com o titulo.
///   H. A VELHICE: o Humano de 74 anos e 9 meses morre de velhice no mes que fecha os 75.
///   I. A SALA DO TEMPO POR CIMA: o ano da sala e o mes do calendario se somam.
///   J. O GROWTH SPURT E UM MES, nos mesmos meses do calendario; o DESEJO DE JUVENTUDE poe uma idade redonda.
///   K. CONTRA-EXEMPLO (`Envelhecimento.CalendarioParadoDeTeste`, o port de antes): dez meses passam e a idade nao anda.
/// </summary>
public sealed partial class GameServer
{
	private bool _calendarioRodando, _calendarioJaRodou;
	private int _caOk, _caFalhou;

	private static readonly string[] ContasDaBancadaDoCalendario = ["bancada_calendario_um", "bancada_calendario_velho"];

	private void AfirmarCa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _caOk++; GD.Print($"[calendario]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _caFalhou++; GD.PrintErr($"[calendario]   FALHA {nome}   [{detalhe}]"); }
	}

	private void ApagarContasDoCalendario()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaBancadaDoCalendario)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[calendario] nao apaguei '{conta}': {e.Message}"); }
	}

	/// <summary>
	/// A MANIVELA DO TEMPO: o relogio do universo anda ate um pouco depois da meia-noite da Terra que abre o dia pedido do calendario, e o
	/// tique de producao do ceu roda uma vez -- e ele que vira o dia e o mes.
	/// </summary>
	private void AndarAteODiaDoCalendario(long alvo)
	{
		RelogioDoPlaneta terra = RelogioDaZona(ZoneKey.Premade("Earth"));
		double agora = terra.DiaLocal(TempoDoMundo) - _calendario.Origem;
		if (alvo > agora) _adiantoDoCeu += (alvo - agora + 0.01) * terra.SegundosPorDia;
		TickDoCeu();
	}

	/// <summary>Anda ate o primeiro dia do mes que fica tantos meses a frente do de hoje.</summary>
	private void AndarMesesDoCalendario(int meses) => AndarAteODiaDoCalendario((MesDoCalendario + meses) * Calendario.DiasPorMes);

	private static string IdadeEscrita(ServerPlayer? p) => p == null ? "(sem corpo)" : $"{p.Idade}a{p.MesesDaIdade}m (mes contado {p.MesDaIdade})";

	public void RodarBancadaDoCalendario(int porta)
	{
		if (_calendarioRodando || _calendarioJaRodou) return;
		_calendarioRodando = _calendarioJaRodou = true;
		_caOk = _caFalhou = 0;
		GD.Print("[calendario] ================ O CALENDARIO E A IDADE (dono, 2026-10-17) ================");

		ApagarContasDoCalendario();
		var c = new ClienteCruDeTeste[ContasDaBancadaDoCalendario.Length];
		for (int i = 0; i < c.Length; i++) c[i] = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, c);
		string um = ContasDaBancadaDoCalendario[0], velho = ContasDaBancadaDoCalendario[1];
		string cargoDoDeus = Jandirus.Core.Ranks.KitDoDeus.Cargo;
		bool tinhaDeus = _tronos.TryGetValue(cargoDoDeus, out string? deusAntes);
		try
		{
			// ============================================================ A) a conta do Core
			GD.Print("[calendario] -- A) a conta do Core --");
			AfirmarCa("o PRIMEIRO DIA e Suntag, 1 de Luty do ano 1; o setimo e Sondin; o oitavo volta a Suntag (a semana de `DayNames`)",
				Calendario.De(0).Extenso == "Suntag, 1 de Luty do ano 1" && Calendario.De(6).NomeDoDia == "Sondin"
				&& Calendario.De(7).Extenso == "Suntag, 8 de Luty do ano 1",
				$"{Calendario.De(0).Extenso} | {Calendario.De(6).Extenso} | {Calendario.De(7).Extenso}");
			AfirmarCa("O MES TEM 28 DIAS (`if(Days>=29)`): o dia 28 e Sondin, 28 de Luty, e o seguinte e o 1 de Sakavik",
				Calendario.De(27).Extenso == "Sondin, 28 de Luty do ano 1" && Calendario.De(28).Extenso == "Suntag, 1 de Sakavik do ano 1",
				$"{Calendario.De(27).Extenso} | {Calendario.De(28).Extenso}");
			AfirmarCa("O ANO TEM 10 MESES (`if(Month>=11)`): o ultimo dia e 28 de Alacritas do ano 1, o seguinte e o 1 de Luty do ano 2 -- e o mes corrido passa de 9 a 10",
				Calendario.De(279).Extenso == "Sondin, 28 de Alacritas do ano 1" && Calendario.De(280).Extenso == "Suntag, 1 de Luty do ano 2"
				&& Calendario.MesDe(279) == 9 && Calendario.MesDe(280) == 10,
				$"{Calendario.De(279).Extenso} | {Calendario.De(280).Extenso}");
			AfirmarCa("OS NOMES SAO OS DO DM: os sete dias e os dez meses, na ordem de `DayNames` e `MonthNames`",
				string.Join(",", Calendario.DiasDaSemana) == "Suntag,Veindin,Thordin,Kaidin,Dedin,Fradin,Sondin"
				&& string.Join(",", Calendario.Meses) == "Luty,Sakavik,Frieze,Saiya,Augustus,Plant,Yule,Propositus,Zenkin,Alacritas");
			ContaDosMeses nove = Envelhecimento.ContarMeses(20, 0, 0, 9, envelhece: true);
			ContaDosMeses dez = Envelhecimento.ContarMeses(20, 0, 0, 10, envelhece: true);
			ContaDosMeses longe = Envelhecimento.ContarMeses(20, 7, 4, 29, envelhece: true);
			AfirmarCa("DEZ MESES FECHAM UM ANO: 20 anos e 9 meses depois de nove; 21 e zero depois de dez; e 20a7m mais 25 meses = 23a2m, tres anos fechados",
				nove is { Idade: 20, Meses: 9, MesContado: 9, AnosFechados: 0 } && dez is { Idade: 21, Meses: 0, MesContado: 10, AnosFechados: 1 }
				&& longe is { Idade: 23, Meses: 2, MesContado: 29, AnosFechados: 3 },
				$"{nove} | {dez} | {longe}");
			ContaDosMeses morto = Envelhecimento.ContarMeses(20, 3, 0, 12, envelhece: false);
			ContaDosMeses voltou = Envelhecimento.ContarMeses(20, 3, 12, 8, envelhece: true);
			AfirmarCa("QUEM NAO ENVELHECE deixa o tempo passar (a idade fica e o mes contado anda), e o RELOGIO QUE VOLTA nao desenvelhece (a conta espera ele passar)",
				morto is { Idade: 20, Meses: 3, MesContado: 12, AnosFechados: 0 } && voltou is { Idade: 20, Meses: 3, MesContado: 12, AnosFechados: 0 },
				$"{morto} | {voltou}");

			// ============================================================ B) a origem
			GD.Print("[calendario] -- B) a origem deste mundo --");
			long origemDoBoot = _calendario.Origem;
			bool noDisco = System.IO.File.Exists(CaminhoDoCalendario);
			_calendario = new CalendarioNoDisco();   // a memoria some...
			CarregarCalendario();                    // ...e volta do arquivo
			AfirmarCa("O MUNDO NOVO NASCE NO 1 DE LUTY DO ANO 1: o boot gravou a origem (o dia da Terra) no `calendario.json`, e a leitura devolve a mesma",
				noDisco && origemDoBoot > 0 && _calendario.Origem == origemDoBoot && DiaDoCalendario is 0 or 1 && Hoje is { Ano: 1, Mes: 1 },
				$"origem {origemDoBoot} -> {_calendario.Origem}, hoje {Hoje.Extenso}");
			PorAOrigemDoCalendario(DiaDaTerraAgora);   // daqui em diante a bancada parte do primeiro dia, cravado

			// ============================================================ C) o jogador novo e o dia que vira
			GD.Print("[calendario] -- C) o jogador novo comeca a contar no mes em que nasce; o dia que vira e anunciado --");
			AndarAteODiaDoCalendario(2 * Calendario.DiasPorMes + 5);   // o mundo ja esta em Frieze quando ele nasce
			ServerPlayer? pl = mesa.EntraNoMundo(c[0], um, "Calendario");
			AfirmarCa("PRECONDICAO: o jogador entrou pela porta -- Humano, 18 anos, num mundo que ja correu dois meses",
				pl is { Race: "Human", Idade: 18 } && MesDoCalendario == 2, $"{pl?.Race} {IdadeEscrita(pl)}, mes do calendario {MesDoCalendario}");
			if (pl == null) return;
			AfirmarCa("O JOGADOR NOVO COMECA A CONTAR NO MES EM QUE NASCE: o mes do calendario (2, e nao o zero de um save antigo) vai no corpo e no save, e os meses da idade sao zero",
				pl is { MesDaIdade: 2, MesesDaIdade: 0 } && mesa.NoDisco(um) is { MesDaIdade: 2, MesesDaIdade: 0 },
				$"corpo {IdadeEscrita(pl)}, save mes {mesa.NoDisco(um)?.MesDaIdade}");
			List<string> diaNovo = Ouvir(() => AndarAteODiaDoCalendario(DiaDoCalendario + 1));
			DataDoCalendario amanha = Hoje;
			AfirmarCa("O DIA QUE VIRA E ANUNCIADO a quem esta no jogo -- o `It is now [dia], [mes] the [n]` do DM, sem o ano",
				diaNovo.Contains($"Hoje é {amanha.NomeDoDia}, {amanha.Dia} de {amanha.NomeDoMes}."), Ultimos(diaNovo));

			// ============================================================ D) o mes que vira
			GD.Print("[calendario] -- D) o mes que vira --");
			List<string> mesNovo = Ouvir(() => AndarMesesDoCalendario(1));
			AfirmarCa("O MES QUE VIRA ANUNCIA A DATA COM O ANO (o `Years()` do DM) e conta UM MES na idade de quem esta no jogo: 18 anos e 1 mes",
				mesNovo.Contains($"Começa o mês de {Hoje.NomeDoMes}. Hoje é {Hoje.Extenso}.") && pl is { Idade: 18, MesesDaIdade: 1 } && pl.MesDaIdade == MesDoCalendario,
				$"{Ultimos(mesNovo)} || {IdadeEscrita(pl)}");
			mesa.TiqueEEntrega();
			bool chegou = mesa.Bombeia(() => c[0].UltimaFicha is { MesesDaIdade: 1 }, 3000);
			AfirmarCa("...e a FICHA LENTA leva os meses pelo fio: o leitor de producao do cliente le 18 anos e 1 mes",
				chegou && c[0].UltimaFicha is { Idade: 18, MesesDaIdade: 1 }, $"{c[0].UltimaFicha?.Idade}a{c[0].UltimaFicha?.MesesDaIdade}m");

			// ============================================================ E) o ano
			GD.Print("[calendario] -- E) o ano que fecha, e o ano novo --");
			List<string> aniversario = Ouvir(() => AndarMesesDoCalendario(9));
			AfirmarCa("DEZ MESES DEPOIS, UM ANO: 19 anos e zero meses -- no corpo e na ficha de poder -- e ele ouve 'Você faz 19 anos.'",
				pl is { Idade: 19, MesesDaIdade: 0 } && Math.Abs(pl.Ficha.Idade - 19) < 1e-9 && aniversario.Contains("Você faz 19 anos."),
				$"{IdadeEscrita(pl)} | {Ultimos(aniversario)}");
			long ateOAnoNovo = (MesDoCalendario / Calendario.MesesPorAno + 1) * Calendario.MesesPorAno - MesDoCalendario;
			List<string> anoNovo = Ouvir(() => AndarMesesDoCalendario((int)ateOAnoNovo));
			AfirmarCa("O 1 DE LUTY ANUNCIA O ANO NOVO: 'Começa o ano N!' com a data",
				Hoje is { Mes: 1, Dia: 1 } && anoNovo.Contains($"Começa o ano {Hoje.Ano}! Hoje é {Hoje.Extenso}."), Ultimos(anoNovo));

			// ============================================================ F) o tempo la fora
			GD.Print("[calendario] -- F) o tempo la fora --");
			int idadeNaSaida = pl.Idade, mesesNaSaida = pl.MesesDaIdade;
			bool saiu = mesa.SaiDoJogo(c[0], um);
			CharacterSave? salvo = mesa.NoDisco(um);
			AfirmarCa("A SAIDA GRAVA A IDADE, OS MESES E O MES CONTADO (o `LastYear = Year` da saida do DM)",
				saiu && salvo != null && salvo.Idade == idadeNaSaida && salvo.MesesDaIdade == mesesNaSaida && salvo.MesDaIdade == MesDoCalendario,
				$"{idadeNaSaida}a{mesesNaSaida}m, save {salvo?.Idade}a{salvo?.MesesDaIdade}m mes {salvo?.MesDaIdade}");
			AndarMesesDoCalendario(25);
			ServerPlayer? deVolta = null;
			List<string> volta = Ouvir(() => deVolta = mesa.EntraNoMundo(c[0], um, "Calendario"));
			(int esperadaIdade, int esperadosMeses, int anosFora) = Envelhecimento.MaisMeses(idadeNaSaida, mesesNaSaida, 25);
			AfirmarCa($"QUEM VOLTA 25 MESES DEPOIS VOLTA 2 ANOS E 5 MESES MAIS VELHO ({idadeNaSaida}a{mesesNaSaida}m -> {esperadaIdade}a{esperadosMeses}m): o login conta os meses de fora",
				deVolta != null && deVolta.Idade == esperadaIdade && deVolta.MesesDaIdade == esperadosMeses && deVolta.MesDaIdade == MesDoCalendario,
				IdadeEscrita(deVolta));
			AfirmarCa("...e ouve a DATA DE HOJE e o tempo que passou (o `src<<checkthetimeidiot()` do `AgeCheck`)",
				volta.Contains($"Hoje é {Hoje.Extenso}.") && volta.Any(x => x.Contains($"{anosFora} anos se passaram enquanto você esteve fora")),
				Ultimos(volta));
			if (deVolta == null) return;
			pl = deVolta;

			// ============================================================ G) quem nao envelhece
			GD.Print("[calendario] -- G) quem nao envelhece --");
			pl.Combate.Morrer(ignorarSeguro: true);   // a porta unica de toda morte (`AdminMatar`)
			MandarFicha(pl);
			int idadeDoMorto = pl.Idade, mesesDoMorto = pl.MesesDaIdade;
			AndarMesesDoCalendario(10);
			AfirmarCa("O MORTO NAO ENVELHECE (`if(!dead ...)`, `Aging.dm:145`): dez meses passam, a idade fica -- e o mes contado anda",
				pl.Ficha.dead && pl.Idade == idadeDoMorto && pl.MesesDaIdade == mesesDoMorto && pl.MesDaIdade == MesDoCalendario, IdadeEscrita(pl));
			EnvelhecerMeses(pl, Calendario.MesesPorAno);   // os meses de FORA do calendario: o Toque do Tempo num corpo morto
			AfirmarCa("...nem pelos meses de FORA do calendario (o Toque do Tempo num morto) -- o dono: *\"lembrando q morto n envelhece\"*",
				pl.Idade == idadeDoMorto && pl.MesesDaIdade == mesesDoMorto, IdadeEscrita(pl));
			Restaurar(pl, reviver: true);   // a volta do admin (`AdminReviver`)
			AndarMesesDoCalendario(1);
			(int idadeVivo, int mesesVivo, _) = Envelhecimento.MaisMeses(idadeDoMorto, mesesDoMorto, 1);
			AfirmarCa("...e quem volta a vida NAO RECUPERA os meses de morto: um mes depois, so um mes a mais",
				!pl.Ficha.dead && pl.Idade == idadeVivo && pl.MesesDaIdade == mesesVivo, IdadeEscrita(pl));
			_tronos[cargoDoDeus] = pl.Conta;
			int idadeDoDeus = pl.Idade, mesesDoDeus = pl.MesesDaIdade;
			AndarMesesDoCalendario(10);
			AfirmarCa("O DEUS DA DESTRUICAO NAO ENVELHECE enquanto carrega o titulo (`!god_is_god(src)`): dez meses e a idade fica",
				EhDeusDaDestruicao(pl) && pl.Idade == idadeDoDeus && pl.MesesDaIdade == mesesDoDeus, IdadeEscrita(pl));
			if (tinhaDeus) _tronos[cargoDoDeus] = deusAntes!; else _tronos.Remove(cargoDoDeus);

			// ============================================================ H) a velhice
			GD.Print("[calendario] -- H) a velhice --");
			ServerPlayer? ancia = mesa.EntraNoMundo(c[1], velho, "Anciao");
			AfirmarCa("PRECONDICAO: o segundo jogador entrou pela porta (Humano)", ancia is { Race: "Human" }, ancia?.Race ?? "(nao entrou)");
			if (ancia != null)
			{
				ancia.Idade = 74;
				ancia.Ficha.Idade = 74;
				ancia.MesesDaIdade = 9;
				ancia.Ficha.Statify();
				List<string> fim = Ouvir(() => AndarMesesDoCalendario(1));
				AfirmarCa("A VELHICE CHEGA NO MES QUE FECHA O ANO: o Humano de 74 anos e 9 meses faz 75 -- o fim da vida dele (auge 50 x 1,5) -- e morre de velhice, sem volta (`aged_out`)",
					ancia.Idade == 75 && ancia.Ficha.dead && ancia.Ficha.aged_out && fim.Contains("Você faz 75 anos."),
					$"{IdadeEscrita(ancia)} morto={ancia.Ficha.dead} aged_out={ancia.Ficha.aged_out} | {Ultimos(fim)}");
			}

			// ============================================================ I) a Sala do Tempo por cima
			GD.Print("[calendario] -- I) a Sala do Tempo por cima --");
			pl.MesesDaIdade = 3;
			int idadeDaSala = pl.Idade;
			EnvelhecerNaSala(pl);          // um ano da sala fechou (`HTC_AGE_PER_GAME_DAY`)...
			AndarMesesDoCalendario(1);     // ...e o calendario de fora seguiu
			AfirmarCa("A SALA DO TEMPO ENVELHECE POR CIMA (o dono: *\"nas salas do tempo a pessoa envelhece mais rapido mesmo com esse sistema de calendario\"*): o ano da sala e o mes do calendario se somam -- um ano e um mes a mais",
				pl.Idade == idadeDaSala + 1 && pl.MesesDaIdade == 4, $"{idadeDaSala}a3m -> {IdadeEscrita(pl)}");

			// ============================================================ J) o Growth Spurt e o desejo
			GD.Print("[calendario] -- J) o Growth Spurt e um mes; o desejo de juventude poe idade redonda --");
			pl.MesesDaIdade = 9;
			int idadeDoEstirao = pl.Idade;
			EnvelhecerMeses(pl, 1);   // o `Age += 0.1` do Growth Spurt e do Toque do Tempo
			AfirmarCa("O GROWTH SPURT E UM MES (`Age += 0.1`), nos mesmos meses do calendario: 9 meses mais 1 fecham o ano",
				pl.Idade == idadeDoEstirao + 1 && pl.MesesDaIdade == 0 && Math.Abs(pl.Ficha.Idade - pl.Idade) < 1e-9, IdadeEscrita(pl));
			pl.MesesDaIdade = 6;
			PorIdade(pl, 16);
			AfirmarCa("O DESEJO DE JUVENTUDE poe uma idade REDONDA: 16 anos e zero meses (o `Age = N` do DM nao guarda decimo)",
				pl is { Idade: 16, MesesDaIdade: 0 }, IdadeEscrita(pl));

			// ============================================================ K) o contra-exemplo
			GD.Print("[calendario] -- K) contra-exemplo --");
			int idadeParada = pl.Idade, mesesParados = pl.MesesDaIdade;
			Envelhecimento.CalendarioParadoDeTeste = true;
			try { AndarMesesDoCalendario(10); }
			finally { Envelhecimento.CalendarioParadoDeTeste = false; }
			AfirmarCa("CONTRA-EXEMPLO (defeito injetado: o calendario parado, o port de antes): dez meses passam e a idade NAO anda -- a prova do ano (E) reprovaria",
				pl.Idade == idadeParada && pl.MesesDaIdade == mesesParados, IdadeEscrita(pl));
			AndarMesesDoCalendario(10);
			AfirmarCa("...e o mesmo salto, com o calendario de volta, fecha o ano",
				pl.Idade == idadeParada + 1 && pl.MesesDaIdade == mesesParados, IdadeEscrita(pl));
		}
		catch (Exception e)
		{
			AfirmarCa("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			Envelhecimento.CalendarioParadoDeTeste = false;
			if (tinhaDeus) _tronos[cargoDoDeus] = deusAntes!; else _tronos.Remove(cargoDoDeus);
			mesa.Fecha();
			ApagarContasDoCalendario();
			_calendarioRodando = false;
			GD.Print($"[calendario] ================ {_caOk} OK, {_caFalhou} FALHA(S) ================");
		}
	}
}
