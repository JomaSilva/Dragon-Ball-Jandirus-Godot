using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Server;

/// <summary>
/// ============================ O FUNIL UNICO DO KARMA ============================
/// Porte do bloco "ALINHAMENTO / KARMA" do original (`Code/Modules/NPCs/SkyNPCs.dm:96-146`). A
/// aritmetica mora no <see cref="Karma"/> (Core); o que mora aqui e QUANDO ela e chamada.
///
/// **O BURACO QUE ISTO FECHA ESTA ESCRITO NO CABECALHO DO CORE, E VALE REPETIR A METADE DE JOGO:**
/// o campo `ServerPlayer.Karma` tinha tres leitores e um unico produtor -- `+5 por tarefa de cargo`
/// --, e tarefa de cargo so existe pra quem ja tem cargo. Nove dos cargos reivindicaveis pediam
/// karma e nenhum caminho do jogo o produzia. O sistema de cargos inteiro estava ligado, verde na
/// bancada, e recusava todo mundo pra sempre.
/// ============================================================================
///
/// ============================ POR QUE UM FUNIL, E NAO TRES `pl.Karma +=` ============================
/// Sao quatro fatos do mundo que mexem no karma, e cada um mora num arquivo diferente (a derrota, a
/// morte de habitante, o chefe de saga, a tarefa de cargo). Escrever a soma nos quatro seria quatro
/// lugares pra alguem esquecer o piso, o teto ou o aviso -- e o defeito apareceria como *"matei
/// quinze inocentes e continuo podendo ser Guardiao"*, que ninguem liga ao `Math.Clamp` faltante.
///
/// E o mesmo argumento que criou o <see cref="AoPerderALuta"/>, dito pelo proprio: *"o dia em que a
/// derrota tiver uma terceira consequencia, ela entra aqui e chega nos quatro de graca"*. Esta e a
/// quarta, e ela entrou por ali.
/// ================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// MEXE NO KARMA DE ALGUEM, E CONTA PRA ELE.
	///
	/// **O AVISO NAO E ENFEITE, E NAO E OPCIONAL.** O original escreve uma linha de chat em CADA uma
	/// das quatro contas (`SkyNPCs.dm:114, 117, 122, 129, 147`), sempre com o numero novo entre
	/// parenteses, e o motivo e que este e o unico eixo do jogo que nao aparece em nenhuma barra: sem
	/// a frase, o jogador descobriria que tem karma no dia em que um cargo o recusasse por causa dele.
	///
	/// SO GENTE DE VERDADE. Corpo sem dono nao tem moral e nao le mensagem: dar karma a um habitante
	/// que matou outro habitante encheria o save de ninguem com um numero que nada consulta.
	/// </summary>
	/// <param name="motivo">O que aconteceu, na voz do jogo. Vira a frase que ele le.</param>
	private void SomarKarma(ServerPlayer pl, int delta, string motivo)
	{
		if (delta == 0 || !EhJogador(pl)) return;

		int antes = pl.Karma;
		pl.Karma = Karma.Somar(antes, delta);

		// O TETO ENGOLIU A CONTA? Entao nao houve mudanca, e mentir "+30" com o numero parado seria
		// pior que calar. O original nao faz esta guarda (ele imprime sempre), e a diferenca e
		// deliberada: la o teto e invisivel porque a fala vem de um NPC que so aparece uma vez; aqui
		// a mesma frase chega a cada chefe derrubado, e "IMACULADO (100/100)" repetido dez vezes
		// ensina mais do que "+30" que nao sobe.
		if (pl.Karma == antes)
		{
			Avisar(pl, delta > 0
				? $"seu coração já está no limite da bondade ({motivo}). karma {pl.Karma} -- {Karma.Faixa(pl.Karma)}."
				: $"não há mais escuridão pra caber nesse coração ({motivo}). karma {pl.Karma} -- {Karma.Faixa(pl.Karma)}.");
			return;
		}

		Avisar(pl, $"{motivo} karma {(delta > 0 ? "+" : "")}{delta} → {pl.Karma} ({Karma.Faixa(pl.Karma)}).");
		GD.Print($"[server] karma de {pl.Name}: {antes} -> {pl.Karma} ({motivo})");
	}

	/// <summary>
	/// ============================ MATOU UM JOGADOR ============================
	/// `gain_kill_karma`, chamado do `killer_stuff` (`Murder.dm:83`). Aqui ele entra pelo
	/// <see cref="AoPerderALuta"/> com `morreu: true`, que e o mesmo ponto por onde a SUCESSAO ja
	/// entra -- e pela mesma razao que o comentario dela da: so a MORTE conta, nunca o nocaute.
	///
	/// ============================ AS TRES GUARDAS SAO AS TRES DO DM ============================
	/// `if(!victim || !victim.Player || victim == src) return` (`SkyNPCs.dm:109`) mais o
	/// `if(src.Player)` de quem chama (`Murder.dm:83`):
	///
	///   1. **os dois tem que ser JOGADOR** -- matar um habitante ou um chefe tem conta propria
	///      (as duas outras funcoes deste arquivo), e um NPC que mata nao ganha nada porque nada le
	///      o karma dele;
	///   2. **ninguem se pune por morrer** (`victim == src`) -- a Explosao Final e o Kaio-ken que
	///      estoura passam pelo funil de derrota com autor igual a vitima;
	///   3. **uma morte, uma conta** (`pk_karma_taken`) -- ver
	///      <see cref="ServerPlayer.KarmaDaMorteContado"/>. No original a trava existe porque o
	///      `killer_stuff` roda duas vezes na mesma morte; aqui ela existe porque
	///      <see cref="AoPerderALuta"/> tem dois chamadores com `morreu: true` (o golpe, em
	///      `GameServer.Combat.cs:500`, e a absorcao, em `GameServer.Absorcao.cs:111`).
	/// ======================================================================================
	/// </summary>
	private void KarmaPorMatarJogador(ServerPlayer vitima, ServerPlayer algoz)
	{
		if (vitima == algoz) return;
		if (!EhJogador(vitima) || !EhJogador(algoz)) return;
		if (vitima.KarmaDaMorteContado) return;
		vitima.KarmaDaMorteContado = true;

		// A PERGUNTA E SOBRE A VITIMA, e as duas metades do `||` sao coisas diferentes -- ver
		// `Karma.PorMorteDeJogador`. `EhVilao` e o `isVillain` deste port (`GameServer.Destruicao.cs:815`).
		int delta = Karma.PorMorteDeJogador(vitima.Karma, EhVilao(vitima));

		SomarKarma(algoz, delta, delta > 0
			? $"você abateu uma alma perversa ({vitima.Name})."
			: $"você tirou uma vida inocente ({vitima.Name}).");
	}

	/// <summary>
	/// ============================ MATOU UM HABITANTE ============================
	/// `lose_npc_kill_karma` (`SkyNPCs.dm:120-122`). No original o gancho mora no
	/// `PlanetReputation.dm:113`, ou seja: **na mesma linha que paga a reputacao**. Aqui vale igual --
	/// ele e chamado de dentro do `MorreuUmCorpoSemDono`, encostado no `SomarReputacao`
	/// ("matou-habitante").
	///
	/// AS DUAS CONTAS SAO SEPARADAS E TEM QUE CONTINUAR SENDO: reputacao e o que UM PLANETA pensa de
	/// voce (some quando voce muda de mundo); karma e o que a sua alma virou (te segue ate a mesa do
	/// Enma). Matar dez terraqueos fecha a Terra pra voce e fecha o Guardiao no universo inteiro.
	/// ======================================================================================
	/// </summary>
	private void KarmaPorMatarHabitante(ServerPlayer matador) =>
		SomarKarma(matador, -Karma.PorMatarInocente, "você ceifou um inocente que passava.");

	/// <summary>
	/// ============================ DERRUBOU UM CHEFE DE SAGA ============================
	/// `gain_boss_kill_karma` (`SkyNPCs.dm:145-147`), pendurado no `bev_hero_credit` do original --
	/// que e exatamente o <see cref="PagarOHeroi"/> deste port, o mesmo lugar de onde sai a
	/// reputacao de heroi.
	///
	/// **E ELE E O CAMINHO LIMPO PRO CARGO.** Sem esta linha, a unica maneira de subir karma no jogo
	/// seria assassinar quem ja tem karma negativo -- ou seja, a porta do Eremita Tartaruga (karma
	/// 25+, a escola que "forma protetores") so se abriria matando gente. Com ela, uma saga
	/// derrubada vale +30 e o cargo abre pelo motivo certo.
	/// ==========================================================================================
	/// </summary>
	private void KarmaPorDerrotarChefe(ServerPlayer matador) =>
		SomarKarma(matador, Karma.PorDerrotarChefe, "você livrou o universo de um monstro.");

	/// <summary>
	/// O VERB `Karma` -- a ficha moral, que no original nao existe.
	///
	/// **A AUSENCIA LA E UM DEFEITO E NAO UM ESTILO**: o DM so mostra o numero dentro das falas do
	/// Enma e do Sr. Kaioh, os dois no Outro Mundo, os dois so alcancaveis por quem ja morreu. Quem
	/// nunca morreu joga o jogo inteiro sem saber que este eixo existe -- e ele e o que decide nove
	/// cargos.
	///
	/// A LISTA DO QUE O KARMA ABRE SAI DA TABELA DE CARGOS, e nao de um texto escrito aqui: no dia em
	/// que um cargo mudar de exigencia, esta tela muda junto. Uma lista a mao continuaria prometendo
	/// o numero velho -- que e, literalmente, a familia de defeito que o `Concede` dos cargos ja e.
	/// </summary>
	private void VerboVerKarma(ServerPlayer pl)
	{
		Avisar(pl, $"-- sua alma: karma {pl.Karma} ({Karma.Faixa(pl.Karma)}), numa escala de "
				 + $"{Karma.Piso} a {Karma.Teto} --");
		Avisar(pl, $"sobe: derrotar um chefe de saga (+{Karma.PorDerrotarChefe}), abater um assassino "
				 + $"(+{Karma.PorMatarJogador}), cumprir dever de cargo bom "
				 + $"(+{Jandirus.Core.Ranks.MissoesDeCargo.KarmaPorTarefa}).");
		Avisar(pl, $"desce: matar um inocente (-{Karma.PorMatarJogador}), ceifar um habitante "
				 + $"(-{Karma.PorMatarInocente}).");

		// OS CARGOS QUE ESTE NUMERO ABRE **E OS QUE ELE FECHA**. Os dois lados, porque a escala tem
		// dois: o Lorde Demonio pede karma -50 ou pior, e quem esta em +80 precisa saber que a porta
		// dele nao e "faltam pontos", e sim "voce e bom demais".
		var abre = new List<string>();
		foreach (Jandirus.Core.Ranks.RankDef r in Jandirus.Core.Ranks.Cargos.Todos)
			foreach (Jandirus.Core.Ranks.Regra g in r.Regras)
				foreach (Jandirus.Core.Ranks.Exigencia[] op in g.Opcoes)
					foreach (Jandirus.Core.Ranks.Exigencia e in op)
					{
						if (e.Campo != Jandirus.Core.Ranks.CampoDeRank.Karma) continue;
						bool passa = e.Op == ">=" ? pl.Karma >= e.Valor : pl.Karma <= e.Valor;
						string linha = $"{r.Nome}: karma {e.Op} {e.Valor:0}{(passa ? " ✔" : "")}";
						if (!abre.Contains(linha)) abre.Add(linha);
					}

		if (abre.Count == 0) return;
		Avisar(pl, "cargos que olham o seu karma:");
		foreach (string l in abre) Avisar(pl, $"  {l}");
	}
}
