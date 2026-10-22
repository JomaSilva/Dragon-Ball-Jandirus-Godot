using System.Text.Json;
using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O CALENDARIO DO MUNDO, E A IDADE QUE ANDA COM ELE (dono, 2026-10-17) -- o `WorldClock.dm` e o `AgeCheck` do original. O pedido:
/// *"tinha um sprite de ovo, a idade avança quando apssar 1 ano ingame, se nao me engano no dm o ano eram 10 meses de 30 dias ingame
/// algo assim e tinhas os meses e o ano, faça tudo isso"* -- e logo depois, *"n esqueça q nas salas do tempo a pessoa envelhece mais
/// rapido mesmo com esse sistema de calendario"*.
///
/// A conta da data e do Core (<see cref="Calendario"/>), e a dos meses de idade tambem (<see cref="Envelhecimento.ContarMeses"/>). Aqui
/// ficam as tres pecas do servidor:
///
///   1. A ORIGEM deste mundo (`calendario.json`): o dia da Terra do 1 de Luty do ano 1 -- o arquivo `Year` do DM. Nasce no primeiro
///      boot e morre na limpeza (`ZerarCalendario`, inscrito em `GameServer.Limpeza.cs`).
///   2. O TIQUE (`TickDoCalendario`, chamado pelo do ceu, uma vez por segundo): o dia que vira e anunciado a quem esta no jogo -- o
///      `"It is now [dia], [mes] the [n]"` do DM (`WorldClock.dm:34`) --, e o mes que vira anuncia a data com o ano e conta os meses de
///      idade de todo mundo no jogo (o `Years()`, `:86-92`, que chama o `AgeCheck(1)` de cada um).
///   3. O LOGIN (`ContarOCalendarioNoLogin`): os meses que o personagem passou FORA tambem contam -- no DM o `LastYear` e gravado na
///      saida (`Login.dm:229`) e o `AgeCheck()` do login soma a diferenca (`:345`) --, e ele ouve a data de hoje (o
///      `src<<checkthetimeidiot()` do `AgeCheck`, `Aging.dm:112`).
///
/// ============================ A SALA DO TEMPO ENVELHECE POR CIMA ============================
/// La dentro o ano fecha a cada dia de jogo (`EnvelhecerNaSala`, o `HTC_AGE_PER_GAME_DAY` do DM), e o calendario de fora segue contando
/// os meses de quem esta la -- as duas contas se somam, como no DM (o `Years()` chama o `AgeCheck` de todo mundo no jogo, dentro ou fora
/// da sala). Uma nao mexe no campo da outra: a sala soma anos inteiros, o calendario soma meses.
/// ==========================================================================================
///
/// O SAVE GUARDA OS DOIS NUMEROS (`ServerPlayer.MesesDaIdade` e `MesDaIdade`), e o save de antes do calendario volta com zero e zero: o
/// calendario nasce no mes zero, entao a idade dele comeca a contar dali -- ninguem envelhece no dia em que o calendario nasce.
/// </summary>
public sealed partial class GameServer
{
	private sealed class CalendarioNoDisco
	{
		/// <summary>O dia da Terra (`Calendario.DiaDaTerra`) em que este mundo nasceu: o 1 de Luty do ano 1.</summary>
		public long Origem;
	}

	private CalendarioNoDisco _calendario = new();

	private const string NomeDoArquivoDoCalendario = "calendario.json";
	private string CaminhoDoCalendario => System.IO.Path.Combine(_store?.Pasta ?? ".", NomeDoArquivoDoCalendario);

	/// <summary>
	/// O ULTIMO DIA E O ULTIMO MES QUE O TIQUE VIU. Nao vao pro disco: o boot anota onde o calendario esta sem anunciar nada, e o mes que
	/// virou com o servidor desligado e contado no login de cada um (`ContarOCalendarioNoLogin`).
	/// </summary>
	private long _diaVistoNoCalendario, _mesVistoNoCalendario;

	/// <summary>Que dia da Terra e agora (o dia vira na meia-noite dela).</summary>
	private long DiaDaTerraAgora => Calendario.DiaDaTerra(RelogioDaZona(ZoneKey.Premade("Earth")), TempoDoMundo);

	/// <summary>Quantos dias o calendario deste mundo ja correu (0 = o primeiro dia).</summary>
	internal long DiaDoCalendario => DiaDaTerraAgora - _calendario.Origem;

	/// <summary>A data de hoje.</summary>
	internal DataDoCalendario Hoje => Calendario.De(DiaDoCalendario);

	/// <summary>O mes do calendario de agora (0 = Luty do ano 1) -- a regua de `ServerPlayer.MesDaIdade`.</summary>
	internal long MesDoCalendario => Calendario.MesDe(DiaDoCalendario);

	/// <summary>A origem que vai pro cliente no `S2C.Ceu`: ele refaz a data com a mesma conta do Core.</summary>
	internal long OrigemDoCalendario => _calendario.Origem;

	/// <summary>O calendario deste mundo ja correu? A contagem binaria da limpeza: zero e o primeiro dia de um mundo novo.</summary>
	private bool CalendarioAndou => DiaDoCalendario > 0;

	// =====================================================================
	// O DISCO
	// =====================================================================
	/// <summary>
	/// LE A ORIGEM. O leitor ESVAZIA antes de ler (a devolucao da caixa da `--wipeteste` le de novo por cima). SEM ARQUIVO E MUNDO NOVO:
	/// hoje vira o 1 de Luty do ano 1, e a origem vai pro disco na hora -- como a semente (`CarregarSemente`), senao cada reinicio
	/// recomecaria o calendario. Vem DEPOIS da semente no boot e na devolucao: e ela que pergunta se a pasta e de um mundo velho.
	/// </summary>
	private void CarregarCalendario()
	{
		_calendario = new CalendarioNoDisco();
		bool leu = false;
		try
		{
			if (System.IO.File.Exists(CaminhoDoCalendario)
				&& JsonSerializer.Deserialize<CalendarioNoDisco>(System.IO.File.ReadAllText(CaminhoDoCalendario),
					   new JsonSerializerOptions { IncludeFields = true }) is { } lido)
			{
				_calendario = lido;
				leu = true;
			}
		}
		catch (Exception e)
		{
			GD.PushWarning($"[server] {NomeDoArquivoDoCalendario} ilegivel ({e.Message}) -- o calendario recomeca hoje, no 1 de Luty do ano 1");
		}

		if (!leu)
		{
			_calendario.Origem = DiaDaTerraAgora;
			SalvarCalendario();
		}
		_diaVistoNoCalendario = DiaDoCalendario;
		_mesVistoNoCalendario = MesDoCalendario;
		GD.Print($"[server] calendario: {(leu ? "" : "mundo novo -- ")}hoje e {Hoje.Extenso} (dia {DiaDoCalendario} deste mundo)");
	}

	private void SalvarCalendario()
	{
		if (_store == null) return;
		try
		{
			string tmp = CaminhoDoCalendario + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(_calendario,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDoCalendario, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar {NomeDoArquivoDoCalendario}: {e.Message}"); }
	}

	/// <summary>
	/// A LIMPEZA: o mundo novo comeca hoje, no 1 de Luty do ano 1 -- o `fdel("Year")` da limpeza do DM (`SettingsDatum.dm:225`). So a
	/// memoria; o disco e da vassoura e da proxima gravacao.
	/// </summary>
	private void ZerarCalendario() => PorAOrigemDoCalendario(DiaDaTerraAgora);

	/// <summary>A ORIGEM NOVA, e o tique passa a ver dali -- o que mudou de data por fora do relogio nao se anuncia.</summary>
	private void PorAOrigemDoCalendario(long origem)
	{
		_calendario.Origem = origem;
		_diaVistoNoCalendario = DiaDoCalendario;
		_mesVistoNoCalendario = MesDoCalendario;
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O DIA E O MES QUE VIRAM, perguntados pelo tique do ceu (uma vez por segundo). O dia novo e anunciado a quem esta no jogo; o mes
	/// novo anuncia a data com o ano (o ano novo, com o nome dele) e conta os meses de idade de cada um -- o `Years()` do DM. Um salto de
	/// varios dias (o relogio adiantado) anuncia so o dia de chegada, e os meses pulados entram todos de uma vez na conta de cada um.
	/// O RELOGIO QUE VOLTA (um reinicio que perdeu o adianto do ceu) nao anuncia nada de novo: o mes ja visto so vira de novo quando o
	/// calendario passar dele.
	/// </summary>
	private void TickDoCalendario()
	{
		long dia = DiaDoCalendario;
		if (dia == _diaVistoNoCalendario) return;
		bool voltou = dia < _diaVistoNoCalendario;
		_diaVistoNoCalendario = dia;
		if (voltou) return;

		long mes = Calendario.MesDe(dia);
		bool mesNovo = mes > _mesVistoNoCalendario;
		if (mesNovo) _mesVistoNoCalendario = mes;

		DataDoCalendario hoje = Calendario.De(dia);
		string frase = !mesNovo ? $"Hoje é {hoje.NomeDoDia}, {hoje.Dia} de {hoje.NomeDoMes}."
					 : hoje.Mes == 1 ? $"Começa o ano {hoje.Ano}! Hoje é {hoje.Extenso}."
					 : $"Começa o mês de {hoje.NomeDoMes}. Hoje é {hoje.Extenso}.";
		foreach (ServerPlayer pl in Jogadores.ToList())
		{
			Avisar(pl, frase);
			if (mesNovo) ContarOCalendario(pl, avisar: true);
		}
	}

	// =====================================================================
	// A IDADE
	// =====================================================================
	/// <summary>
	/// CONTA OS MESES DESTE PERSONAGEM ate o mes de agora (<see cref="Envelhecimento.ContarMeses"/>) -- o `AgeCheck` do DM. Quem nao
	/// envelhece: o morto e o Deus da Destruicao com o titulo (`Aging.dm:145`). O ano que fecha soma o potencial oculto
	/// (`Fighter.SomarPotencialOcultoDaIdade`, `Aging.dm:125-139`) e passa pela pergunta da velhice
	/// (`ConferirMorteDeVelhice`), uma vez, com a idade inteira de uma vez -- como o DM, que soma `Year - LastYear` e so entao olha o
	/// corpo. Devolve quantos anos fecharam.
	/// </summary>
	private int ContarOCalendario(ServerPlayer pl, bool avisar)
	{
		ContaDosMeses c = Envelhecimento.ContarMeses(pl.Idade, pl.MesesDaIdade, pl.MesDaIdade, MesDoCalendario,
			envelhece: !pl.Ficha.dead && !EhDeusDaDestruicao(pl));
		pl.MesDaIdade = c.MesContado;
		if (c.Meses != pl.MesesDaIdade)
		{
			pl.MesesDaIdade = c.Meses;
			pl.SigAtributos = "";   // os meses vao na ficha lenta (o cartao do calendario diz quanto falta pro aniversario)
		}
		if (c.AnosFechados == 0) return 0;

		pl.Idade = c.Idade;
		pl.Ficha.Idade = c.Idade;
		// O estoque do ano (`Aging.dm:125-139`), antes da velhice: o ano que mata tambem soma.
		pl.Ficha.SomarPotencialOcultoDaIdade(c.AnosFechados);
		pl.Ficha.Statify();
		pl.SigAtributos = "";
		if (avisar) Avisar(pl, $"Você faz {pl.Idade} anos.");
		ConferirMorteDeVelhice(pl);
		return c.AnosFechados;
	}

	/// <summary>O LOGIN CONTA OS MESES DE FORA, calado: o aviso sai depois, com o mundo ja na tela (<see cref="AvisarDoCalendarioNoLogin"/>).</summary>
	private int ContarOCalendarioNoLogin(ServerPlayer pl) => ContarOCalendario(pl, avisar: false);

	/// <summary>A DATA DE HOJE pra quem entra, e os anos que passaram com ele fora.</summary>
	private void AvisarDoCalendarioNoLogin(ServerPlayer pl, int anosLaFora)
	{
		Avisar(pl, $"Hoje é {Hoje.Extenso}.");
		if (anosLaFora > 0 && !pl.Ficha.dead)
			Avisar(pl, anosLaFora == 1
				? $"Um ano se passou enquanto você esteve fora: você tem {pl.Idade} anos agora."
				: $"{anosLaFora} anos se passaram enquanto você esteve fora: você tem {pl.Idade} anos agora.");
	}

	/// <summary>
	/// MESES DE IDADE A MAIS, fora do calendario -- o `Age += 0.1` do Growth Spurt e do Toque do Tempo (G2), que e exatamente um mes.
	/// Moram nos MESMOS meses que o calendario conta (`ServerPlayer.MesesDaIdade`), e por isso vao pro disco: ate 2026-10-17 a fracao
	/// vivia num dicionario da sessao e morria no logout. MORTO NAO ENVELHECE (o dono, 2026-10-17: *"lembrando q morto n envelhece"*): nem pelo
	/// calendario (`ContarOCalendario`) nem por aqui.
	/// </summary>
	private void EnvelhecerMeses(ServerPlayer pl, int meses)
	{
		if (pl.Ficha.dead) return;
		(int idade, int restantes, int anos) = Envelhecimento.MaisMeses(pl.Idade, pl.MesesDaIdade, meses);
		pl.MesesDaIdade = restantes;
		pl.SigAtributos = "";
		if (anos == 0) return;
		pl.Idade = idade;
		pl.Ficha.Idade = idade;
		pl.Ficha.SomarPotencialOcultoDaIdade(anos);
		pl.Ficha.Statify();
		ConferirMorteDeVelhice(pl);
	}
}
