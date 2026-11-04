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

		// (2026-10-30, o pedido 184) o aviso que o login deixou (`RevisarOsMarcosNoLogin`): no primeiro tique o cliente ja tem chat
		if (_avisoDosMarcosNoLogin.Remove(pl.Id, out string? doLogin) && doLogin != null) Avisar(pl, doLogin);

		// (2026-10-30, o pedido 185) A METADE DO BLUE PAGA O BLUE KAIO-KEN: a maestria do Blue so sobe dentro dele, entao e no tique de quem esta
		// no Blue que os 50% dela chegam -- os 50% do ki divino tem o tique deles (`TickDaMaestriaDivina`), e o save tem o login
		if (forma.Atual == Catalogo.IdBlue) PagarOBlueKaioken(pl, avisar: true);

		MarcoConcedido m = MarcosDeForma.Conferir(forma, pl.Ficha);
		if (m.Multiplicador <= 0) return;

		Avisar(pl, m.PelaFormaDeAgora
			? $"MARCO DE PODER! o seu corpo rompeu um novo patamar: todo ganho de poder agora é x{m.Multiplicador:0.##}."
			: "MARCO DE PODER! as formas que você já tinha despertado passam a contar como marco: "
			  + $"todo ganho de poder agora é x{m.Multiplicador:0.##}.");
		GD.Print($"[server] {pl.Name}: marco de forma ({(m.PelaFormaDeAgora ? "estreia" : "divida antiga")}) "
			   + $"-> ganho x{m.Multiplicador:0.##} | marcos: {string.Join(",", pl.Ficha.bp_milestones_done)}");
	}

	// =====================================================================
	// (2026-10-30, o pedido 184) OS NIVEIS DIVINOS: O BLUE KAIO-KEN E O LOGIN
	// =====================================================================
	/// <summary>
	/// O AVISO QUE O LOGIN DEIXA PRO PRIMEIRO TIQUE: no `RestaurarFormaEDisciplina` o cliente ainda nao tem chat (o `JoinAccepted` vem
	/// depois), entao a frase espera aqui e sai na primeira conferencia.
	/// </summary>
	private readonly Dictionary<int, string> _avisoDosMarcosNoLogin = [];

	/// <summary>
	/// OS MARCOS NO LOGIN. Os niveis divinos do dono subiram o valor de etiquetas que o save ja tinha (o ki divino de 5 pra 8, o Blue de 6 pra
	/// 10, o SSJ4, o FP, o LSSJ3), e o `ReachMilestone` nao paga de novo o que ja esta na ficha: o ganho e revisto aqui, uma vez por entrada
	/// (`Fighter.ReajustarOsMarcos`). E o Blue Kaio-ken de quem ja tinha passado dos 50% antes desta regra. SO NO LOGIN, e nao a cada tique:
	/// bancada que zera o ganho pra medir (a `sala`, a `ganho`, a `gravmeta`) nao o ve voltar sozinho no meio da medida.
	/// </summary>
	private void RevisarOsMarcosNoLogin(ServerPlayer pl)
	{
		double revisto = pl.Ficha.ReajustarOsMarcos();
		double kaioken = PagarOBlueKaioken(pl, avisar: false);
		if (kaioken > 0)
			_avisoDosMarcosNoLogin[pl.Id] = $"MARCO DE PODER! o seu ki divino chegou ao nível do Blue com Kaio-ken: todo ganho de poder agora é x{kaioken:0.##}.";
		else if (revisto > 0)
			_avisoDosMarcosNoLogin[pl.Id] = $"MARCO DE PODER! os patamares que você já tinha rompido valem mais agora: todo ganho de poder agora é x{revisto:0.##}.";
	}

	/// <summary>
	/// QUEM TEM O BLUE KAIO-KEN: 50% de maestria divina (o dono: *"ao atingir 50% de god ki mastery ativa o milestone do blue kaioken"*) e o
	/// Kaio-ken por cima do Blue -- o Saiyajin de classe baixa ou media (a mesma pergunta do veto do Kaio-ken, `Kaioken.VetoDaForma`) com a
	/// METADE DO BLUE DOMADA (`MaestriaNoBlueDoKaioken`, o pedido 185) --, e a raca que ascende, cujo nivel do Blue com o Kaio-ken x20 e o mesmo
	/// 56x (o pedido 183). As outras classes tem o 56x delas por forma (Blue Evolution, Rose 2, Beast, Limit Breaker), e o marco vem com a forma.
	/// </summary>
	private static bool TemOBlueKaioken(ServerPlayer pl)
	{
		if (pl.Ficha.godki is not { awakened: true } g || g.mastery < Catalogo.GodkiRoyalePct) return false;
		if (Jandirus.Core.Races.Ascensao.Ascende(pl.Race, pl.Ficha.Class)) return true;
		if (MaestriaDaForma(pl, Catalogo.IdBlue) < MaestriaNoBlueDoKaioken) return false;
		return Jandirus.Core.Skills.Kaioken.VetoDaForma(Catalogo.Def(Catalogo.IdBlue), pl.Ficha.Class, g.mastery)
			   == Jandirus.Core.Skills.Kaioken.Veto.Nenhum;
	}

	/// <summary>
	/// (2026-10-30, o pedido 185) A METADE DO BLUE QUE O MARCO DO BLUE KAIO-KEN PEDE -- o dono: *"o blue kaioken e liberado (blue evolution tb)
	/// quando vc tem o nivel certo de god ki e pelomenos 50% de maestria no ssj blue"*. A barra do Blue so sobe dentro dele, entao os 50% tambem
	/// dizem que o Blue e deste corpo (a linhagem e a classe que o abrem): a pergunta pelo `Avaliar` que o 184 fazia saiu. A raca que ascende nao
	/// tem Blue pra domar e fica so nos 50% de ki divino (escolha minha). E E SO O MARCO: a TECNICA do Kaio-ken no Blue continua abrindo com o
	/// ki divino (`Kaioken.VetoDaForma`), e o Blue Evolution com o Grade 2.
	/// </summary>
	private const double MaestriaNoBlueDoKaioken = 50;

	/// <summary>
	/// Paga o marco do Blue Kaio-ken a quem o tem (`TemOBlueKaioken`) e devolve o novo multiplicador quando o ganho subiu, ou 0. Quem chama: o
	/// tique da maestria divina ao cruzar os 50% (`TickDaMaestriaDivina`, so jogador), o tique de quem esta no Blue (`ConferirOsMarcosDeForma`,
	/// o pedido 185) e o login.
	/// </summary>
	private double PagarOBlueKaioken(ServerPlayer pl, bool avisar)
	{
		if (!TemOBlueKaioken(pl)) return 0;
		double m = pl.Ficha.ReachMilestone(Jandirus.Core.Stats.Milestones.BlueKaioken);
		if (m > 0 && avisar)
			Avisar(pl, $"MARCO DE PODER! o seu ki divino chegou ao nível do Blue com Kaio-ken: todo ganho de poder agora é x{m:0.##}.");
		return m;
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
