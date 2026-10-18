using Jandirus.Core.Ai;
using Jandirus.Core.Races;

namespace Jandirus.Server;

/// <summary>
/// AS PASSIVAS E AS ATIVAS RACIAIS DO POVO DO MUNDO E DOS CHEFES, E NA MAO DA IA (o dono, 2026-10-17: *"n esqueça de fazer as IAs saberem
/// usar as passivas e ativas da raça, e os boss de saga tb vao ter todas essas passivas e ativas a depender de sua raça"*).
///
///   * QUEM TEM KIT (<see cref="TemKitRacial"/>): a PESSOA, como na v0.4.0, e agora o CORPO DO MUNDO -- o povo e os chefes de saga. As
///     PASSIVAS nao pedem IA nenhuma: o tique racial (`TickDasRaciais`) ja rodava em todo corpo, e com o kit elas valem pra ele.
///   * O QUE A IA PODE APERTAR (<see cref="AtivasRaciaisNaMao"/>): as linhas de `AtivasRaciaisNaIa` que o kit do corpo da e que nao estao
///     em recarga -- e, das que se ligam e desligam, se estao ligadas. Entram no repertorio de luta (`ArsenalDeLuta`), no relogio de 1 Hz
///     das capacidades.
///   * AS TRES DE RECEITA PROPRIA (`Cerebro.Raciais.cs`): o Demonic pronto, a execucao na mao e o transbordar que ja da pra desabafar.
///
/// QUEM RECUSA CONTINUA SENDO O VERB (`UsarAtivaRacial`): nada aqui liga ativa nenhuma. O cerebro pede pelo `Comando.Habilidade`, e o
/// atuador aperta pelo canal do jogador.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// QUEM TEM O KIT DA RACA: a PESSOA (o jogador, o corpo de bancada com conta) e o CORPO DO MUNDO (o povo e os chefes --
	/// `EhNpcDoMundo`). Fica de fora o corpo sem papel e sem conta: a copia do dono (o reflexo da mente, a Divisao do Corpo), o boneco
	/// forjado de bancada.
	/// </summary>
	private static bool TemKitRacial(ServerPlayer pl) => EhPessoa(pl) || (!Passivas.SoPessoaTemKitDeTeste && EhNpcDoMundo(pl));

	/// <summary>O KIT DESTE CORPO pela raca e pela classe (`Passivas.KitDe`) -- nenhum pra quem nao tem (<see cref="TemKitRacial"/>).</summary>
	private static KitRacial KitDoCorpo(ServerPlayer pl) =>
		Passivas.SemKitDeTeste || !TemKitRacial(pl) ? KitRacial.Nenhum : Passivas.KitDe(pl.Race, pl.Ficha.Class, pl.Ficha.SaiyanLineage);

	/// <summary>
	/// (2026-10-17) AS SKILLS DOS KITS DE RACA QUE VALEM NESTE CORPO (`SkillRacial`): o kit inteiro da raca, ou -- no BIO-ANDROIDE -- as que ele
	/// escolheu do sangue (`SkillsRaciais.DoCorpoDoBio`, o adendo do dono). Nenhuma pra quem nao tem kit (<see cref="TemKitRacial"/>).
	/// </summary>
	private static SkillRacial SkillsDoCorpo(ServerPlayer pl) =>
		Passivas.SemKitDeTeste || !TemKitRacial(pl) ? SkillRacial.Nenhuma
		: SangueDoBio.EhBio(pl.Ficha) ? SkillsRaciais.DoCorpoDoBio(pl.Ficha)
		: SkillsRaciais.DoKit(Passivas.KitDe(pl.Race, pl.Ficha.Class, pl.Ficha.SaiyanLineage));

	/// <summary>
	/// AS ATIVAS RACIAIS NA MAO DA IA, uma marca por linha de `AtivasRaciaisNaIa.Todas`: 0 = fora (o corpo nao a tem -- o kit, ou a escolha do
	/// bio, `SkillsRaciais.TemAtiva` --, ou ela esta em
	/// recarga -- as de prazo, que pagam a recarga ao ligar, ficam fora enquanto correm), 1 = na mao, 2 = ligada (a que se liga e desliga,
	/// de pe agora). Devolve quantas entram. Sem lista e sem texto: roda no relogio de 1 Hz de todo corpo dirigido.
	/// </summary>
	private int AtivasRaciaisNaMao(ServerPlayer pl, Span<byte> naMao)
	{
		naMao.Clear();
		if (AtivasRaciaisNaIa.IaSemAtivasDeTeste || pl.Combate == null) return 0;
		KitRacial kit = KitDoCorpo(pl);
		SkillRacial skills = SkillsDoCorpo(pl);
		if (kit == KitRacial.Nenhum && skills == SkillRacial.Nenhuma) return 0;
		EstadoRacial est = pl.Racial;
		long agora = NowMs();
		ReadOnlySpan<AtivasRaciaisNaIa.Linha> linhas = AtivasRaciaisNaIa.Todas;
		int quantas = 0;
		for (int i = 0; i < linhas.Length; i++)
		{
			string id = linhas[i].Id;
			if (!SkillsRaciais.TemAtiva(kit, skills, id)) continue;
			if (AtivaRacialLigada(est, id)) naMao[i] = 2;
			else if (EstadoRacial.FaltaDaRecarga(pl.Ficha.RecargasRaciais, id, agora) <= 0) naMao[i] = 1;
			else continue;
			quantas++;
		}
		return quantas;
	}

	/// <summary>As quatro que se ligam e desligam -- e o estado de cada uma, onde o verb o escreve.</summary>
	private static bool AtivaRacialLigada(EstadoRacial est, string id) => id switch
	{
		Passivas.Transbordar => est.TransbordarDesde >= 0,
		Passivas.TransbordarKi => est.KiTransbordandoDesde >= 0,
		Passivas.PoderMaximo => est.PoderMaximo,
		Passivas.Ira => est.IraDesde >= 0,
		_ => false,
	};

	/// <summary>
	/// O DEMONIC SAIYAN SAI AGORA? O kit, a recarga, e as perguntas do proprio verb (<see cref="PorQueNaoDemonico"/>). A luta de 2 minutos e
	/// conferida ANTES, como poda: e a unica recusa do verb que monta texto (os segundos que faltam), e a leitura de capacidades nao aloca.
	/// </summary>
	private bool DemonicoNaMaoDaIa(ServerPlayer pl)
	{
		if (AtivasRaciaisNaIa.IaSemAtivasDeTeste || pl.Combate == null || (KitDoCorpo(pl) & KitRacial.LendarioPrimal) == 0) return false;
		if (pl.Forma.CombateSegundos < Passivas.LutaPraDemonico) return false;
		return EstadoRacial.FaltaDaRecarga(pl.Ficha.RecargasRaciais, Passivas.Demonico, NowMs()) <= 0
			   && PorQueNaoDemonico(pl, pl.Racial).Length == 0;
	}

	/// <summary>
	/// A EXECUCAO DO RUTHLESS MIND ESTA NA MAO? O kit do mutante, o golpe LETAL ligado (o verb exige -- `ComecarExecucao`), nenhuma
	/// execucao correndo e a recarga vencida. Quem esta caido na frente e pergunta do verb, na hora do aperto.
	/// </summary>
	private bool ExecucaoNaMaoDaIa(ServerPlayer pl) =>
		!AtivasRaciaisNaIa.IaSemAtivasDeTeste && pl.Combate is { Letal: true } && pl.Racial.ExecucaoAlvo == 0
		&& (KitDoCorpo(pl) & KitRacial.Mutante) != 0
		&& EstadoRacial.FaltaDaRecarga(pl.Ficha.RecargasRaciais, Passivas.MenteCruel, NowMs()) <= 0;

	/// <summary>
	/// O TRANSBORDAR QUE JA DA PRA DESABAFAR: o verb dele, ligado ha mais que o prazo do segundo aperto (`DesabafarRacial`), e
	/// <paramref name="ha"/> segundos alem do prazo. Nulo = nenhum.
	/// </summary>
	private string? DesabafoNaMaoDaIa(ServerPlayer pl, out double ha)
	{
		ha = 0;
		if (AtivasRaciaisNaIa.IaSemAtivasDeTeste) return null;
		EstadoRacial est = pl.Racial;
		long agora = NowMs();
		if (est.TransbordarDesde >= 0 && (agora - est.TransbordarDesde) / 1000.0 >= Passivas.SegundosPraDesabafar)
		{
			ha = (agora - est.TransbordarDesde) / 1000.0 - Passivas.SegundosPraDesabafar;
			return Passivas.Transbordar;
		}
		if (est.KiTransbordandoDesde >= 0 && (agora - est.KiTransbordandoDesde) / 1000.0 >= Passivas.SegundosPraDesabafarKi)
		{
			ha = (agora - est.KiTransbordandoDesde) / 1000.0 - Passivas.SegundosPraDesabafarKi;
			return Passivas.TransbordarKi;
		}
		return null;
	}
}
