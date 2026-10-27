using Godot;
using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// A AGUA ULTRA DIVINA DO MIRANTE -- o encanamento do <see cref="AguaSagrada"/> (a regra e o que o original
/// faz estao la). Item 11 do playtest de 2026-10-10.
///
/// O JARRO E MOBILIA DE MAPA, como o banco e a porta da Sala do Tempo: uma obra semeada pelo
/// `z12_Lookout.objetos`, que responde a tecla E pelos dois verbos do `Interacoes`. O `Drink()` do DM e um
/// proc com cinco `sleep` no meio; aqui cada gole e uma linha numa lista, e quem anda as fases e o tique do
/// servidor -- ninguem fica bloqueado esperando, e o que acontece com quem desloga no meio esta escrito
/// (ver <see cref="FecharOGoleDeQuemSai"/>).
/// </summary>
public sealed partial class GameServer
{
	private sealed class GoleDeAgua
	{
		public int Id;
		public int Fase;
		public long Quando;
		public bool Aguenta;
	}

	private readonly List<GoleDeAgua> _golesDeAgua = [];

	/// <summary>A MOEDA DA BANCADA: um sorteio fixo no lugar do `_rng`. Nulo em jogo.</summary>
	internal double? SorteioDaAguaDeTeste;

	private bool ComandoDaAguaSagrada(ServerPlayer pl, string cmd)
	{
		switch (cmd)
		{
			case AguaSagrada.VerboBeber: BeberAAguaSagrada(pl); return true;
			case AguaSagrada.VerboSobre: SobreAAguaSagrada(pl); return true;
			default: return false;
		}
	}

	/// <summary>O `verb/Description()` (`EarthRanks.dm:227-231`): o que ela da e o que ela cobra, ANTES do gole.</summary>
	private void SobreAAguaSagrada(ServerPlayer pl)
	{
		if (ObraQueAceita(pl, AguaSagrada.VerboSobre) == null) { Avisar(pl, "não há jarro da Água Ultra Divina por perto."); return; }
		Avisar(pl, "a Água Ultra Divina solta o potencial que você guardou treinando e lutando, e deixa um pouco mais dele em você -- mas "
				   + "o corpo precisa de resistência física pra aguentar. Sem ela, é meio a meio: metade das vezes a água MATA.");
		Avisar(pl, "na primeira vez que você aguenta, o vigor máximo sobe. Depois de um gole, uma hora até o próximo.");
	}

	/// <summary>O `verb/Drink()` (`EarthRanks.dm:187-226`). As fases seguintes sao do <see cref="TickDaAguaSagrada"/>.</summary>
	private void BeberAAguaSagrada(ServerPlayer pl)
	{
		// PERTO DO JARRO? Quem responde e o servidor: o menu do cliente le a mesma tabela, mas esconder botao
		// nunca foi permissao.
		if (ObraQueAceita(pl, AguaSagrada.VerboBeber) == null) { Avisar(pl, "não há jarro da Água Ultra Divina por perto."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condição de beber nada."); return; }

		// UM GOLE DE CADA VEZ. No DM o verb pode ser chamado de novo durante os `sleep` do anterior, e cada
		// chamada e um proc a mais correndo -- ali e um furo, e aqui nao ha por que copia-lo.
		if (_golesDeAgua.Exists(g => g.Id == pl.Id)) { Avisar(pl, "você ainda está com a água na boca."); return; }

		// (2026-10-26) A AGUA SE BEBE DE NOVO, DE HORA EM HORA -- o dono: *"repetiveis, 1h de recarga e a agua pode matar"*. O risco vale a
		// cada gole. O androide de absorcao nao tira nada dela (so uma tech de upgrade solta o banco dele -- `PorQueNaoLibera`).
		if (PorQueNaoLibera(pl, comRecarga: true, proprio: true, exigeBanco: false) is { } recusa)
		{
			Avisar(pl, "você bebe a Água Ultra Divina... e nada acontece.");
			Avisar(pl, recusa);
			return;
		}

		bool aguenta = AguaSagrada.Aguenta(pl.Ficha.Ephysdef, SorteioDaAguaDeTeste ?? _rng.NextDouble());

		Avisar(pl, "você bebe a Água Ultra Divina...");
		_golesDeAgua.Add(new GoleDeAgua { Id = pl.Id, Fase = 0, Quando = NowMs() + AguaSagrada.MsEntreFalas, Aguenta = aguenta });
		GD.Print($"[server] {pl.Name} bebeu a Agua Ultra Divina ({(aguenta ? "aguenta" : "NAO aguenta")}; resistencia {pl.Ficha.Ephysdef:0.##})");
	}

	/// <summary>Anda as fases de quem bebeu. Uma linha do tique: sem gole pendente, sai na primeira comparacao.</summary>
	private void TickDaAguaSagrada()
	{
		if (_golesDeAgua.Count == 0) return;
		long agora = NowMs();
		for (int i = _golesDeAgua.Count - 1; i >= 0; i--)
		{
			GoleDeAgua g = _golesDeAgua[i];
			if (agora < g.Quando) continue;
			if (!_players.TryGetValue(g.Id, out ServerPlayer? pl) || !PassoDaAguaSagrada(pl, g, agora))
				_golesDeAgua.RemoveAt(i);
		}
	}

	/// <summary>Um passo do gole. Devolve falso quando ele acabou.</summary>
	private bool PassoDaAguaSagrada(ServerPlayer pl, GoleDeAgua g, long agora)
	{
		switch (g.Fase++)
		{
			case 0:
				Avisar(pl, "alguma coisa está acontecendo...");
				g.Quando = agora + AguaSagrada.MsEntreFalas;
				return true;

			case 1:
				Avisar(pl, "hm... acho que não foi nada!");
				g.Quando = agora + AguaSagrada.MsEntreFalas;
				return true;

			case 2:
				if (!g.Aguenta)
				{
					// `usr.Death()`, pela porta unica da morte -- e o retorno e olhado: um seguro contra a morte
					// (ver `CombatState.Morrer`) vale contra a agua como vale contra um soco.
					bool morreu = !pl.Ficha.dead && pl.Combate.Morrer();
					Avisar(pl, morreu ? "a Água Ultra Divina te MATA de repente!"
									  : "a Água Ultra Divina queima por dentro -- e alguma coisa segura você deste lado.");
					return false;
				}
				Avisar(pl, "de repente você desaba, como se tivesse levado um tijolo na cabeça.");
				if (!pl.Ficha.dead) pl.Combate.Nocautear(AguaSagrada.SegundosNoChao);
				g.Quando = agora + (long)(AguaSagrada.SegundosNoChao * 1000);
				return true;

			case 3:
				// `usr.Un_KO()` (`:201`): o corpo levanta AQUI, tenha o relogio do nocaute vencido ou nao.
				if (pl.Ficha.KO && !pl.Ficha.dead) pl.Combate.Levantar();
				Avisar(pl, "você acorda no Mirante, no mesmo lugar, e tudo dói.");
				Avisar(pl, "doer desse jeito prova que não foi um sonho.");
				g.Quando = agora + AguaSagrada.MsEntreFalas;
				return true;

			default:
				DarOQueAAguaDa(pl);
				return false;
		}
	}

	/// <summary>
	/// O QUE A AGUA DA. (2026-10-26) O BANCO DO POTENCIAL, COMO NO DU (`Skills.dm:2770-2773`; o dono, o pedido 95): ela SOLTA o banco 1x
	/// (`LiberarOPotencial`) e POE nele 10% do BP base, minguando a cada gole (`GainKnobs.AguaPoeNoBanco`, dividido pelos goles + 1). O
	/// vigor maximo sobe 20% so na PRIMEIRA agua que se aguenta (o `sacredwater` do DM, `:205`). Era um quinto do `capcheck(BPMod *
	/// relBPmax)`, uma vez na vida -- e a quem tinha bilhoes dava bilhoes.
	/// </summary>
	private void DarOQueAAguaDa(ServerPlayer pl)
	{
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		if (f.sacredwater < 1)
		{
			f.sacredwater = 1;
			f.maxstamina *= AguaSagrada.GanhoDeVigor;
			Avisar(pl, "!!* O seu VIGOR aumentou *!!");
		}

		if (!f.TemPotencialGuardado) Avisar(pl, "não havia potencial guardado pra soltar -- mas a água deixa um pouco dele em você.");
		LiberarOPotencial(pl, pl, Jandirus.Core.Stats.GainKnobs.LiberaAAgua, autor: "a Água Ultra Divina", exigeBanco: false);
		f.hiddenpotential += f.BP * Jandirus.Core.Stats.GainKnobs.AguaPoeNoBanco / (f.golesDaAgua + 1);
		f.golesDaAgua++;
		f.liberarPotencialEm = NowMs() + Jandirus.Core.Stats.GainKnobs.RecargaDaLiberacaoMs;
		MandarFicha(pl);
	}

	/// <summary>
	/// QUEM SAI NO MEIO DO GOLE. No DM o proc morre com o mob: a marca ja foi gasta e o premio se perde -- quem
	/// caisse da conexao nos 42 s do efeito perdia, pra sempre, a unica agua da vida. Aqui quem AGUENTAVA
	/// recebe o que a agua da antes de o personagem ir pro disco; quem ia morrer simplesmente nao morre (a
	/// marca dele nao foi gasta, e ele pode tentar de novo).
	/// </summary>
	private void FecharOGoleDeQuemSai(ServerPlayer pl)
	{
		int i = _golesDeAgua.FindIndex(g => g.Id == pl.Id);
		if (i < 0) return;
		bool aguentava = _golesDeAgua[i].Aguenta;
		_golesDeAgua.RemoveAt(i);
		if (!aguentava) return;
		if (pl.Ficha.KO && !pl.Ficha.dead) pl.Combate.Levantar();
		DarOQueAAguaDa(pl);
	}

	/// <summary>Adianta o relogio de todo gole pendente. SO PRA BANCADA -- o tique de producao faz o resto.</summary>
	internal void AdiantarAAguaDeTeste(long ms)
	{
		foreach (GoleDeAgua g in _golesDeAgua) g.Quando -= ms;
	}
}
