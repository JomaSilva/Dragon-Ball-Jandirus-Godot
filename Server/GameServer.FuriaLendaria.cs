using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// A FURIA LENDARIA, LADO DO SERVIDOR -- porte de `lssjbuff.dm:562-660`
/// (`legendary_berserk_check` / `legendary_berserk_loop`).
///
/// As regras puras moram no <see cref="FuriaLendaria"/> (Core). Aqui mora o que e ESTADO e DECISAO:
/// quanto falta pra o controle escapar, quanto falta pra ele voltar, e quem esta dirigindo.
///
/// ============================ QUASE NADA DESTE ARQUIVO E CODIGO NOVO ============================
/// O dono pediu *"acontece o mesmo q o oozaru"*, e o proprio DM ja dizia que o berserk e a *"receita
/// do rampage do Oozaru selvagem"*. Entao o que este arquivo faz e ARMAR UM RELOGIO e chamar o que
/// ja existia:
///
///   * quem dirige o corpo -- <see cref="Jandirus.Core.Ai.Cerebro"/> + `TickDosCorposSemDono`;
///   * quem escolhe a vitima -- `PresaDaFera` ("QUALQUER corpo da zona, sem dono, sem faccao");
///   * quem barra os dedos do jogador -- `ComandoDeCorpo` + `SemAsRedeas`, no despacho de pacote;
///   * quem conta pros outros clientes -- o bit `EntityState.SemRedeas`, que ja viaja pra zona;
///   * quem larga o input pendurado / devolve as redeas -- `LargarOInput` / `DevolverAsRedeas`.
///
/// O QUE TEVE DE MUDAR NO MOTOR DO OOZARU, e foram tres coisas pequenas:
///
///   1. `LargarOInput` foi EXTRAIDO do `TomarAsRedeas` (as seis linhas que zeram o rastro de input);
///   2. `DevolverAsRedeas` MUDOU DE ARQUIVO (Oozaru -> Clone), porque ganhou um segundo chamador;
///   3. o `else` do `TickDosCorposSemDono` deixou de se chamar "a fera" -- ele sempre foi "o corpo
///      possuido", e agora ha dois.
/// Nenhuma delas mexeu no COMPORTAMENTO do macaco.
/// ============================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// O TIQUE DA FURIA. Um relogio so, com dois sentidos -- ver <see cref="ServerPlayer.FuriaAte"/>.
	///
	/// ============================ O CICLO INTEIRO MORA NUM METODO SO ============================
	/// Armar o prazo poderia ter ido pro `AnunciarForma` (o funil da transformacao), e seria um erro:
	/// `AnunciarForma` sabe quando a forma COMECA, mas nao sabe quando ela acaba por Ki zerado, por
	/// nocaute, por cena que prendeu o corpo, nem quando a maestria cruza os 100% no meio dela. Cada
	/// um desses e um lugar onde alguem esqueceria de desarmar -- e o esquecimento seria um jogador
	/// possuido na forma base, sem nada na tela explicando.
	///
	/// Com o ciclo aqui, quem arma e quem desarma sao a MESMA linha lida em dois tiques, e o unico
	/// jeito de a regra sumir e apagar o metodo. E o mesmo desenho do `TickDoOozaru`.
	/// ========================================================================================
	/// </summary>
	private void TickDaFuriaLendaria(ServerPlayer pl, double dt)
	{
		// CLONE NAO, FERA NAO. As duas exclusoes sao pelo mesmo motivo -- os dois ja tem dono do
		// `Cerebro`, e este metodo nao pode devolver redeas que nao tomou. A fera em particular tem o
		// prazo DELA (`TickDoOozaru`, passo 5); e enquanto se e macaco a escada esta na base, entao
		// nem chegariamos a uma forma lendaria.
		if (pl.DonoDoClone != 0 || pl.Oozaru != FormaOozaru.Nao) { pl.FuriaAte = 0; return; }

		FormaDef? d = pl.Forma.Def;
		double maestria = pl.Forma.Maestria.De(pl.Forma.Atual);

		// ============================ 1. FORA DE ALCANCE -- E ESTE E O CAMINHO QUE DEVOLVE O CORPO ============================
		// Sao cinco saidas e nenhuma precisa de linha propria em lugar nenhum:
		//   * a forma nao e lendaria (voltou pra base, subiu pra outra linha, o Ki acabou, foi
		//     nocauteado -- o `TickDaForma` ja reverteu ANTES deste laco, ver a ordem em `GameServer.cs`);
		//   * a forma esta DOMINADA (a maestria cruzou os 100% DENTRO da posse: a furia solta na hora,
		//     que e a promessa do dono e o `legendary_cur_mastery() >= 100` do `while` do DM);
		//   * o corpo caiu (o `while` do DM tambem tem `!KO && !dead`);
		//   * o corpo esta preso numa CENA. Aqui e desarme e nao pausa de proposito: a cinematica da
		//     estreia lendaria prende o boneco por dezenas de segundos, e contar esse tempo como
		//     "controle" gastaria o prazo inteiro do jogador assistindo a propria transformacao. O
		//     relogio comeca quando o corpo e dele.
		// ================================================================================================================
		if (!FuriaLendaria.EhDescontrolavel(d) || FuriaLendaria.Dominou(maestria)
			|| pl.Ficha.KO || pl.Ficha.dead || EmCena(pl))
		{
			if (pl.FuriaAte != 0 && SemAsRedeas(pl))
			{
				DevolverAsRedeas(pl);
				Avisar(pl, "a furia se esvai, e o corpo volta a ser seu.");
				GD.Print($"[server] {pl.Name}: a furia lendaria soltou o corpo (fim de forma/dominio)");
			}
			pl.FuriaAte = 0;
			return;
		}

		// 2. O PRIMEIRO TIQUE DENTRO DA FORMA arma o prazo de controle.
		if (pl.FuriaAte == 0) { ArmarOControle(pl, d, maestria, voltando: false); return; }

		if (NowMs() < pl.FuriaAte) return;

		// 3. O RELOGIO VIROU. Quem estava dirigindo diz o que acontece agora -- ver `FuriaAte`.
		if (SemAsRedeas(pl)) ArmarOControle(pl, d, maestria, voltando: true);
		else TomarAsRedeasDaFuria(pl, d, maestria);
	}

	/// <summary>
	/// O JOGADOR (VOLTA A) DIRIGIR, e o relogio passa a contar quanto falta pra ele perder.
	///
	/// O PRAZO E DITO EM VOZ ALTA, e nao so quando ele vence: uma regra que so se anuncia depois de
	/// te punir e uma armadilha, nao uma regra. E o mesmo argumento (e quase a mesma frase) do
	/// `VirarFera`.
	/// </summary>
	/// <param name="voltando">
	/// TRUE quando isto e o FIM de uma posse, e nao a entrada na forma. Existe pra a frase nao mentir:
	/// "voce segura as redeas por 6 s" logo depois de tres minutos possuido leria como se nada tivesse
	/// acontecido. Uma mensagem, dois casos -- e nao um `Avisar` extra no chamador, que seria a
	/// segunda linha que alguem esquece de mexer no dia em que o texto mudar.
	/// </param>
	private void ArmarOControle(ServerPlayer pl, FormaDef? d, double maestria, bool voltando)
	{
		double prazo = FuriaLendaria.SegundosDeControle(d, maestria);
		pl.FuriaAte = NowMs() + (long)(prazo * 1000);

		if (voltando)
		{
			DevolverAsRedeas(pl);
			GD.Print($"[server] {pl.Name}: a furia lendaria devolveu o corpo (maestria {maestria:0.0})");
		}

		Avisar(pl, (voltando ? "a furia se esvai e voce volta a controlar o proprio corpo -- " : "")
				 + $"voce segura as redeas por cerca de {prazo:0} s "
				 + $"(dominio {maestria:0.#}% de 100%).");
	}

	/// <summary>
	/// ============================ A FURIA ASSUME O CORPO ============================
	/// *"A FURIA LENDARIA TOMA O SEU CORPO! Voce nao controla mais o que ele faz"* -- a frase e a do
	/// DM (`lssjbuff.dm:608`), traduzida de volta.
	///
	/// Nao ha uma linha de IA aqui pelo mesmo motivo que nao ha no `TomarAsRedeas` da fera: dar um
	/// <see cref="Jandirus.Core.Ai.Cerebro"/> ao corpo ja o poe no `TickDosCorposSemDono`, que o dirige
	/// pelas MESMAS funcoes de movimento e golpe do jogador -- e escolhe a vitima pelo `PresaDaFera`,
	/// que e literalmente *"QUALQUER corpo da zona, sem dono, sem faccao, sem alvo marcado"*, ou seja
	/// o *"ataca TUDO que ve (player OU NPC)"* do original.
	///
	/// ============================ O CEREBRO VEM TEMPERADO PRA FURIA, E NAO PRA FERA ============================
	/// Os tres campos sao os mesmos; os valores nao, e cada um responde a uma linha do berserk do DM:
	///
	///   * `VidaCautelosa = 0` -- IGUAL a fera, e pela regra do DM e nao por copia: o
	///     `legendary_berserk_loop` nao tem um unico ramo de vida, guarda ou recuo. Ele persegue,
	///     avanca e bate ate a condicao do `while` cair. Furia que se protege nao e furia.
	///   * `IntervaloDeDecisao = 0.2` -- **numero literal do DM**: `LEGB_TICK 2` (2 tiques = 0,2 s) e o
	///     `sleep` do laco. Metade do meio segundo da fera, e a diferenca e a coisa certa: o macaco e
	///     pesado e lento pra mudar de ideia, a furia e um corpo de Saiyajin em velocidade normal.
	///   * `ChanceDePesado = 0.6` -- **este e escolha, e o DM nao tem opiniao**: la existe um
	///     `MeleeAttack()` so, sem leve/pesado (`attack_proc.dm:1`). O valor fica entre o clone (0,35 --
	///     que e VOCE lutando, medindo o golpe) e a fera (0,85 -- *"braco de macaco gigante nao da
	///     jab"*). A furia bate forte porque nao mede, e nao porque e grande.
	/// ========================================================================================================
	/// </summary>
	private void TomarAsRedeasDaFuria(ServerPlayer pl, FormaDef? d, double maestria)
	{
		pl.Cerebro = new Jandirus.Core.Ai.Cerebro
		{
			VidaCautelosa = 0,
			ChanceDePesado = 0.6,
			IntervaloDeDecisao = 0.2,
		};

		// O rastro de input do dono morre aqui -- ver `LargarOInput` pro porque de cada campo.
		LargarOInput(pl);

		double posse = FuriaLendaria.SegundosDePosse(d, maestria);
		pl.FuriaAte = NowMs() + (long)(posse * 1000);

		// ============================ A PUNICAO TEM QUE DIZER A SAIDA ============================
		// Mesma regra do Oozaru, com uma diferenca que importa: aqui a saida nao e um GESTO (a fera se
		// solta meditando), e sim ESPERAR -- o `while` do DM so sai quando a raiva passa, e o jogador
		// possuido nao pode nem reverter a forma (`Transformar` e `ComandoDeCorpo`). Entao o que a
		// mensagem tem que entregar e o PRAZO, senao o jogador conclui a mesma coisa que concluiria com
		// o corpo travado e sem numero: que o jogo travou.
		//
		// E ELA DIZ O QUE ENCURTA O PRAZO DA PROXIMA VEZ. A maestria continua subindo durante a posse
		// (`TickDaForma` cobra o Ki e paga a maestria enquanto a forma esta de pe, com ou sem redeas) --
		// ou seja este tempo nao e perdido, ele e o preco de aprender a forma. Dizer isso e a diferenca
		// entre uma punicao e uma progressao.
		// ====================================================================================
		Avisar(pl, "A FURIA LENDARIA TOMA O SEU CORPO. Voce nao controla mais o que ele faz.");
		Avisar(pl, $"aguente cerca de {posse:0} s -- e o tempo todo dentro da forma paga maestria, "
				 + $"que e o que encurta isto ate ela nunca mais te pegar (dominio {maestria:0.#}%).");

		GD.Print($"[server] {pl.Name}: a furia lendaria assumiu {pl.Forma.Atual} "
				 + $"(maestria {maestria:0.00}, posse {posse:0.#}s)");
	}
}
