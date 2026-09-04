using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Cargos da `--diagabas` (F8). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ============================ O QUE ELA PROVA ============================
/// Que cada cargo da lista do servidor virou UM cartao, e que cada cartao diz a verdade da ficha
/// dele: a pilula VAGO ou o nome do dono, a pilula de aptidao, a linha "exige: ..." quando nao
/// apto, e o botao "Reivindicar" SO no cargo vago e apto -- com o contra-exemplo de que NENHUM
/// texto de um cargo nao apto diz "reivindicar" (era o botao apagado de antes, que dizia o que nao
/// fazia).
///
/// E O CONTRA-EXEMPLO VIVO: o verb de admin outorga um cargo vago a propria bancada, o cartao tem
/// que trocar VAGO pelo nome dela e perder o botao; destituido, tem que voltar a VAGO. As pecas de
/// leitura (cartao por metadado, pilulas) estao em `RoboDasAbas.Formas.cs`.
/// ==========================================================================
/// </summary>
public partial class RoboDasAbas
{
	// =====================================================================
	// AS REGRAS -- puras, pra rodada de injecao chamar as MESMAS
	// =====================================================================
	private static string NomeDoCargoNaBancada(string chave) =>
		Jandirus.Core.Ranks.Cargos.Get(chave)?.Nome ?? chave;

	/// <summary>Nenhum texto (rotulo, botao ou pilula) diz "reivindicar", em qualquer caixa.</summary>
	private static bool SemReivindicar(IEnumerable<string> textos) =>
		!textos.Any(t => t.Contains("reivindicar", StringComparison.OrdinalIgnoreCase));

	/// <summary>Todos os textos desenhados debaixo de um node: rotulos, botoes e pilulas.</summary>
	private static IEnumerable<string> TextosDe(Node raiz) =>
		Todos(raiz).Select(n => n is Label l ? l.Text : n is Button b ? b.Text : n.HasMeta("pilula") ? n.GetMeta("pilula").AsString() : "")
				   .Where(t => t.Length > 0);

	/// <summary>
	/// O VEREDITO DE UM CARTAO DE CARGO contra a ficha dele. Devolve a lista de erros, vazia = ok.
	/// Pura de proposito: a rodada de injecao a chama com uma amostra estragada e cobra o vermelho.
	/// </summary>
	private static string VereditoDoCartaoDeCargo(bool vago, bool apto, string dono, string falta,
												 List<string> pilulas, bool temBotaoReivindicar,
												 IEnumerable<string> textos, IEnumerable<string> rotulos)
	{
		var erros = new List<string>();
		if (vago ? !pilulas.Contains("VAGO") : (!pilulas.Contains(dono) || pilulas.Contains("VAGO")))
			erros.Add("pilula do dono errada");
		if (apto != pilulas.Contains("você cumpre os requisitos"))
			erros.Add("pilula de aptidao errada");
		if (temBotaoReivindicar != (vago && apto))
			erros.Add(temBotaoReivindicar ? "botao Reivindicar onde nao podia" : "sem botao Reivindicar num cargo vago e apto");
		if (!apto && !SemReivindicar(textos))
			erros.Add("'reivindicar' escrito num cargo nao apto");
		if (!apto && !rotulos.Contains(falta))
			erros.Add("sem a linha 'exige: ...'");
		return string.Join(", ", erros);
	}

	/// <summary>O veredito de um cartao DE VERDADE, lido da tela contra a ficha do pacote.</summary>
	private static string VereditoNaTela(PanelContainer card, GameClient.CargoInfo c) =>
		VereditoDoCartaoDeCargo(c.Dono.Length == 0, c.Falta.Length == 0, c.Dono, c.Falta,
								PilulasEm(card), Botao(card, "Reivindicar") != null,
								TextosDe(card), Rotulos(card).Select(l => l.Text));

	// =====================================================================
	// F8 -- CARGOS
	// =====================================================================
	/// <summary>
	/// O QUE O JOGO RESPONDEU, no log da bancada. As familias desta frente mandam verbs de admin
	/// (forma, cargo, lua) e a recusa do servidor chega pelo CHAT -- sem esta linha "o passo nao
	/// aconteceu" nao diz por que (a primeira rodada da outorga gastou uma volta inteira pra descobrir
	/// que o verb queria o ID e nao o nome). Metodo nomeado, pra poder ser desligado com `-=`.
	/// </summary>
	private static void EcoarFalaDoJogo(Jandirus.Net.Protocol.Fala canal, string quem, string texto) =>
		Nota($"          [jogo] {texto}");

	private async System.Threading.Tasks.Task F8_Cargos(MenuJogo menu, GameClient cli)
	{
		Nota("--- F8: Cargos: um cartao por cargo, VAGO/dono, apto/exige, Reivindicar so onde da ---");
		cli.Falou += EcoarFalaDoJogo;
		try { await F8_CargosDeVerdade(menu, cli); }
		finally { cli.Falou -= EcoarFalaDoJogo; }
	}

	private async System.Threading.Tasks.Task F8_CargosDeVerdade(MenuJogo menu, GameClient cli)
	{
		menu.IrPara("Cargos");
		await Quadros(3);
		bool temLista = await Ate(() => cli.Cargos.Count > 0, 8);
		Checa("a lista de cargos chegou do servidor", temLista, $"{cli.Cargos.Count} cargos");
		menu.ForcarRedesenho();
		await Quadros(3);
		Control? pg = menu.PaginaDeTeste("Cargos");
		Checa("a pagina de Cargos existe e esta na tela", pg is { Visible: true });
		if (pg == null || !temLista) return;

		// ---------------- 1. UM CARTAO POR CARGO, cada um dizendo a verdade da ficha ----------------
		List<PanelContainer> cards = CartoesDeSecao(pg, "cargo").ToList();
		Checa("N cartoes de cargo = N cargos na lista", cards.Count == cli.Cargos.Count, $"{cards.Count} cartoes, {cli.Cargos.Count} cargos");

		string? faixa = menu.ValorDesenhado("Cargos", "Cargos");
		int vagos = cli.Cargos.Count(c => c.Dono.Length == 0);
		Checa("a FAIXA 'Cargos' diz quantos estao vagos", faixa != null && faixa.StartsWith($"{vagos} vago", StringComparison.Ordinal), faixa ?? "(nula)");

		int aptos = 0, errados = 0;
		var detalhes = new List<string>();
		foreach (GameClient.CargoInfo c in cli.Cargos)
		{
			string nome = NomeDoCargoNaBancada(c.Chave);
			if (c.Falta.Length == 0) aptos++;
			PanelContainer? card = CartaoComTitulo(pg, nome);
			if (card == null) { errados++; detalhes.Add($"{nome}: sem cartao"); continue; }
			string veredito = VereditoNaTela(card, c);
			if (veredito.Length > 0) { errados++; detalhes.Add($"{nome}: {veredito}"); }
		}
		Checa("cada cartao: pilula VAGO ou o dono; pilula de aptidao; 'exige: ...' quando nao apto; botao Reivindicar SO se vago e apto; "
			  + "e (CONTRA-EXEMPLO) nenhum texto diz 'reivindicar' num cargo nao apto",
			  errados == 0, errados == 0 ? $"{cli.Cargos.Count} cartoes conferidos" : string.Join(" | ", detalhes.Take(4)));
		Nota($"    {cli.Cargos.Count} cargos: {vagos} vagos, {aptos} apto(s) pra bancada");
		if (aptos == 0)
		{
			_pulados++;
			_naoMedidos.Add("Cargos: o botao Reivindicar num cargo APTO (a bancada nasce sem cumprir requisito nenhum)");
			Nota("  PULADA  o botao Reivindicar num cargo apto   [a bancada nao cumpre requisito de cargo nenhum -- a metade positiva fica sem exemplo]");
		}

		await RolarAteVer(cards.FirstOrDefault());
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? f1 = await Foto();
		await Guardar("cargos-01-topo", f1);
		ChapaDoCartaoNaPaleta("a chapa do primeiro cartao de cargo e a clara do tema (Tema.PainelClaro)", f1, cards.FirstOrDefault(), Tema.PainelClaro);

		(bool igual, int nodes) = await RemontaIgual(menu, pg);
		Checa("DETERMINISMO: duas montagens da mesma lista dao a MESMA arvore de nodes", igual, $"{nodes} nodes");

		// ---------------- 2. O CONTRA-EXEMPLO VIVO: a outorga ----------------
		// O verb de admin da um cargo VAGO a propria bancada (o de menor dadiva, pra mexer o minimo no
		// resto da ficha), e o cartao dele tem que trocar de cara; destituido, tem que voltar. A lista
		// e pedida de novo depois de cada verb, porque o que se afirma e a TELA contra o pacote.
		//
		// O ALVO VAI POR ID, e nao por nome: `PorNome` (GameServer.Admin.cs) le o id do jogador marcado
		// -- e o formato "alvoId|chave" do painel de admin. Com o nome, a resposta e "marque alguem antes".
		GameClient.CargoInfo? alvo = cli.Cargos.Where(c => c.Dono.Length == 0).OrderBy(c => c.Da.Length).ThenBy(c => c.Chave).FirstOrDefault();
		if (alvo is { } a && a.Chave.Length > 0)
		{
			string nome = NomeDoCargoNaBancada(a.Chave);
			cli.SendVerbo("admin_cargo_dar", $"{cli.LocalId}|{a.Chave}");
			await Segundos(0.6);
			cli.SendCargo();
			bool ocupou = await Ate(() => cli.Cargos.Any(c => c.Chave == a.Chave && c.Dono.Length > 0), 8);
			menu.ForcarRedesenho();
			await Quadros(3);
			GameClient.CargoInfo depois = cli.Cargos.FirstOrDefault(c => c.Chave == a.Chave);
			PanelContainer? card = CartaoComTitulo(pg, nome);
			Checa($"CONTRA-EXEMPLO VIVO: outorgado '{nome}' a bancada, o pacote traz um dono", ocupou, $"dono='{depois.Dono}'");
			Checa("...e o cartao troca VAGO pelo nome do dono e nao tem botao Reivindicar",
				  ocupou && card != null && VereditoNaTela(card, depois).Length == 0
				  && !PilulasEm(card).Contains("VAGO") && PilulasEm(card).Contains(depois.Dono) && Botao(card, "Reivindicar") == null,
				  card == null ? "sem cartao" : $"pilulas [{string.Join("|", PilulasEm(card))}] veredito '{VereditoNaTela(card, depois)}'");
			await RolarAteVer(card);
			Image? f2 = await Foto();
			await Guardar("cargos-02-com-dono", f2);

			cli.SendVerbo("admin_cargo_tirar", a.Chave);
			await Segundos(0.6);
			cli.SendCargo();
			bool vagou = await Ate(() => cli.Cargos.Any(c => c.Chave == a.Chave && c.Dono.Length == 0), 8);
			menu.ForcarRedesenho();
			await Quadros(3);
			card = CartaoComTitulo(pg, nome);
			Checa("...e destituido, o cartao volta a VAGO", vagou && card != null && PilulasEm(card).Contains("VAGO"),
				  card == null ? "sem cartao" : string.Join("|", PilulasEm(card)));
		}
		else Nota("    (sem cargo vago pra outorgar -- o contra-exemplo vivo nao rodou)");

		// ---------------- 3. AS INJECOES ----------------
		Injeta("a regra reprova 'reivindicar' escrito num cargo NAO apto (o botao apagado de antes)",
			   VereditoDoCartaoDeCargo(true, false, "", "karma 25+", ["VAGO", "requisitos em falta"], false,
									   ["Turtle Hermit", "VAGO · reivindicar", "karma 25+"], ["karma 25+"]).Length > 0);
		Injeta("...reprova um cargo vago e apto SEM o botao",
			   VereditoDoCartaoDeCargo(true, true, "", "", ["VAGO", "você cumpre os requisitos"], false, ["Korin"], []).Length > 0);
		Injeta("...reprova a pilula VAGO num cargo com dono",
			   VereditoDoCartaoDeCargo(false, false, "Goku", "1M de BP", ["VAGO", "requisitos em falta"], false, ["Korin"], ["1M de BP"]).Length > 0);
		Injeta("...e aceita a amostra certa (senao as tres acima nao provariam nada)",
			   VereditoDoCartaoDeCargo(true, false, "", "karma 25+", ["VAGO", "requisitos em falta"], false, ["Turtle Hermit", "karma 25+"], ["karma 25+"]).Length == 0);
	}
}
