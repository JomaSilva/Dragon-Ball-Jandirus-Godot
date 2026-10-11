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
		Avisar(pl, "a Água Ultra Divina aumenta o seu poder de luta e o seu vigor máximo -- mas o corpo precisa de resistência "
				   + "física pra aguentar. Sem ela, é meio a meio: metade das vezes a água MATA.");
		Avisar(pl, "só funciona uma vez na vida. Quem já bebeu e sobreviveu só sente gosto de água.");
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

		if (pl.Ficha.sacredwater >= 1)
		{
			Avisar(pl, "você bebe a Água Ultra Divina... e nada acontece.");
			Avisar(pl, "você já bebeu esta água. Pra você, agora, ela é só água -- com um leve gosto de xixi de gato. Argh.");
			return;
		}

		bool aguenta = AguaSagrada.Aguenta(pl.Ficha.Ephysdef, SorteioDaAguaDeTeste ?? _rng.NextDouble());

		// A MARCA E GASTA NO GOLE, E SO POR QUEM AGUENTA: `usr.sacredwater = 1` e a primeira linha do ramo do
		// sucesso (`:190`). Quem morre pode voltar e tentar de novo.
		if (aguenta) pl.Ficha.sacredwater = 1;

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
	/// O QUE A AGUA DA (`:205-210`): o vigor maximo sobe 20% e, pra quem esta abaixo do proprio teto, o BP sobe
	/// um quinto do `capcheck(BPMod * relBPmax)` -- a mesma porta de ganho do treino, com os mesmos freios.
	/// </summary>
	private void DarOQueAAguaDa(ServerPlayer pl)
	{
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		f.maxstamina *= AguaSagrada.GanhoDeVigor;
		Avisar(pl, "!!* O seu VIGOR aumentou *!!");

		if (f.BP < f.relBPmax)
		{
			f.BP += f.CapCheck(f.BPMod * f.relBPmax) / AguaSagrada.DivisorDoBp;
			Avisar(pl, "!!* O seu PODER DE LUTA aumentou *!!");
		}
		else Avisar(pl, "você é forte demais pra tirar alguma coisa desta água...");

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
