using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// OS MARCOS DE GANHO DAS FORMAS, LADO DO SERVIDOR -- a conferencia e a frase.
///
/// A regra e a tabela estao no Core (`Core/Forms/MarcosDeForma.cs`, `Core/Stats/Milestones.cs`, e a
/// etiqueta de cada forma no proprio catalogo: `FormaDef.Marco`). Aqui mora so o que e do servidor: quem
/// pergunta, quando, e o que o jogador le.
///
/// QUEM PERGUNTA E O LACO DE FICHAS (`Treinar`, 5 Hz), ao lado dos marcos de Ascensao e pelo argumento
/// escrito la: e o `Stats()` deste port, e quem sobe de patamar precisa ser AVISADO -- por isso nao e o
/// `Fighter.Tick`. Nenhum dos caminhos que mudam a forma chama isto; todos sao vistos por ele, no tique
/// seguinte (ate 200 ms depois, com a cena da transformacao ainda rolando).
///
/// ============================ SO JOGADOR -- DIVERGENCIA DECLARADA, DECISAO PROPRIA ============================
/// No DM o `bp_milestone_reach` e de todo `mob`, e so a frase e de quem tem cliente (`LinearGain.dm:45`). Aqui
/// o marco de forma e de quem tem DONO, por um motivo que e deste port: o NPC sorteado nasce com a escada
/// inteira ja "estreada" -- a biografia dele sobe degrau por degrau pelo mesmo `Entrar`
/// (`SorteioDeNpc.AbrirFormas`) --, entao pagar por estado daria a todo NPC com escada o marco do topo no
/// primeiro tique de vida. E o `Treinar` paga ganho a qualquer corpo nao pacifico que esteja treinando ou
/// meditando (o habitante foi cortado la por isso mesmo: "quarenta corpos que nunca morrem de velhice
/// subiriam de BP sem parar"): com o marco, o que um NPC rendesse renderia ate seis vezes mais -- uma mudanca
/// de equilibrio que ninguem pediu. O pedido do dono e sobre o personagem de quem joga.
/// ===========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada --marcosdeformateste): ninguem confere os marcos das formas -- o jogo de antes
	/// de 2026-10-11, em que virar Super Saiyajin nao subia o ganho de ninguem. Falso em jogo, sempre.
	/// </summary>
	internal bool MarcosDeFormaDesligadosDeTeste;

	/// <summary>
	/// PAGA O QUE AS FORMAS DEVEM A ESTE CORPO, e conta a ele quando o ganho subiu.
	///
	/// DUAS FRASES, porque sao dois acontecimentos: a estreia de agora (o corpo ESTA na forma que pagou) e a
	/// divida antiga -- o personagem que ja tinha despertado a forma antes de os marcos serem ligados, e a
	/// recebe ao entrar. A forma nao atravessa o logout (`RestaurarFormaEDisciplina` devolve o corpo ao
	/// repouso), entao no login a forma que paga nunca e a de agora.
	///
	/// SEM FRASE QUANDO O GANHO NAO SOBE: a etiqueta entra na ficha e pronto. E o `if(m > bp_milestone_mult)`
	/// do `LinearGain.dm:43` -- quem ja tem o ki divino (x5) nao ouve nada ao virar Super Saiyajin (x2).
	/// </summary>
	private void ConferirOsMarcosDeForma(ServerPlayer pl)
	{
		if (MarcosDeFormaDesligadosDeTeste || !EhJogador(pl) || pl.Forma is not { } forma) return;

		MarcoConcedido m = MarcosDeForma.Conferir(forma, pl.Ficha);
		if (m.Multiplicador <= 0) return;

		Avisar(pl, m.PelaFormaDeAgora
			? $"MARCO DE PODER! o seu corpo rompeu um novo patamar: todo ganho de poder agora é x{m.Multiplicador:0.##}."
			: "MARCO DE PODER! as formas que você já tinha despertado passam a contar como marco: "
			  + $"todo ganho de poder agora é x{m.Multiplicador:0.##}.");
		GD.Print($"[server] {pl.Name}: marco de forma ({(m.PelaFormaDeAgora ? "estreia" : "divida antiga")}) "
			   + $"-> ganho x{m.Multiplicador:0.##} | marcos: {string.Join(",", pl.Ficha.bp_milestones_done)}");
	}

	/// <summary>
	/// A LINHA DO GANHO NO `Assess Target` (`AdminFicha`): o multiplicador de marcos e as etiquetas ja pagas, de
	/// QUALQUER origem (formas, bio, Ascensao, potencial, ki divino). E o unico lugar do jogo que mostra o numero
	/// -- pro jogador ele e so a frase do chat, como no DM -- e e por ele que o admin confere "virou e nao subiu?".
	/// </summary>
	private static string LinhaDoGanhoPraAdmin(ServerPlayer p) =>
		$"  ganho de poder x{p.Ficha.bp_milestone_mult:0.##}"
		+ (p.Ficha.bp_milestones_done.Count > 0
			? " · patamares rompidos: " + string.Join(", ", p.Ficha.bp_milestones_done)
			: " · nenhum patamar rompido");
}
