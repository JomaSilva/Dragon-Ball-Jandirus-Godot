using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Gente (People) da `--diagabas` (F10). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ============================ O QUE ELA PROVA, E O QUE NAO TEM COMO ============================
/// A bancada nasce sozinha e os NPCs em volta nao sao "pessoas" pro convivio (`GameServer.Convivio`:
/// corpo sem conta nao entra na lista de ninguem), entao ela NUNCA conhece alguem -- o que da pra
/// provar aqui e a metade vazia (a faixa em 0, a frase de vazio, nenhum cartao de pessoa, nenhum
/// pedido pendente) e a metade viva de "quem esta por perto": as pilulas tem que ser EXATAMENTE os
/// nomes que o `World.NomesVisiveis()` devolve, na mesma ordem, e nunca o proprio nome da bancada.
/// O cartao de pessoa e a fileira de declaracoes ficam sem exemplo vivo, e isso vai pro placar de
/// nao-medidos em vez de virar uma linha verde por omissao.
/// ============================================================================================
/// </summary>
public partial class RoboDasAbas
{
	/// <summary>As pilulas sao exatamente os nomes visiveis, na mesma ordem. Pura, pra injecao.</summary>
	private static bool PilulasBatemComOsVisiveis(IReadOnlyList<string> pilulas, IReadOnlyList<string> visiveis) =>
		pilulas.SequenceEqual(visiveis, StringComparer.Ordinal);

	private async System.Threading.Tasks.Task F10_Gente(MenuJogo menu, GameClient cli)
	{
		Nota("--- F10: Gente: pedido em destaque, faixa de conhecidos, cartoes de pessoa, quem esta por perto ---");
		menu.IrPara("People");
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
		Control? pg = menu.PaginaDeTeste("People");
		Checa("a pagina de People existe e esta na tela", pg is { Visible: true });
		if (pg == null) return;

		// ---------------- 1. A METADE VAZIA (a bancada nao conhece ninguem) ----------------
		Checa("PRECONDICAO: a bancada nao conhece ninguem e nao tem pedido pendente",
			  cli.Conhecidos.Count == 0 && cli.PedidoDeAmizade.Length == 0,
			  $"{cli.Conhecidos.Count} conhecidos, pedido '{cli.PedidoDeAmizade}'");
		string? faixa = menu.ValorDesenhado("People", "Conhecidos");
		Checa("a FAIXA 'Conhecidos' diz 0", faixa != null && faixa.StartsWith("0", StringComparison.Ordinal), faixa ?? "(nula)");
		Checa("sem conhecidos: a frase de vazio esta escrita",
			  Rotulos(pg).Any(l => l.Text.StartsWith("Você ainda não conviveu com ninguém", StringComparison.Ordinal)));
		Checa("...e nao ha cartao de pessoa nem fileira de declaracoes",
			  !CartoesDeSecao(pg, "pessoa").Any() && Botao(pg, "amor") == null && Botao(pg, "neutro") == null);
		Checa("sem pedido pendente: nao ha o cartao de destaque nem os botoes Aceitar/Recusar",
			  CartaoComTitulo(pg, "Pedido de amizade") == null && Botao(pg, "Aceitar") == null);

		// ---------------- 2. QUEM ESTA POR PERTO, em pilulas, contra o mundo ----------------
		PanelContainer? perto = CartaoComTitulo(pg, "Quem está por perto");
		Checa("ha o cartao 'Quem está por perto'", perto != null);
		List<string> visiveis = World.Instancia?.NomesVisiveis() ?? [];
		List<string> pilulas = perto != null ? PilulasEm(perto) : [];
		Checa("as pilulas do cartao sao EXATAMENTE os nomes visiveis do mundo, na mesma ordem",
			  perto != null && PilulasBatemComOsVisiveis(pilulas, visiveis),
			  $"pilulas [{string.Join("|", pilulas)}] visiveis [{string.Join("|", visiveis)}]");
		if (visiveis.Count == 0)
			Checa("...ninguem visivel: a frase 'Ninguém no seu campo de visão.' no lugar das pilulas",
				  perto != null && Rotulos(perto).Any(l => l.Text == "Ninguém no seu campo de visão."));
		else Nota($"    {visiveis.Count} por perto: {string.Join(", ", visiveis)}");
		Checa("CONTRA-EXEMPLO: o proprio nome da bancada nao esta entre as pilulas", !pilulas.Contains(cli.LocalName), cli.LocalName);

		await RolarAteVer(perto);
		Image? f1 = await Foto();
		await Guardar("people-01", f1);
		ChapaDoCartaoNaPaleta("a chapa do cartao 'Quem está por perto' e a clara do tema (Tema.PainelClaro)", f1, perto, Tema.PainelClaro);

		(bool igual, int nodes) = await RemontaIgual(menu, pg);
		Checa("DETERMINISMO: duas montagens da mesma ficha dao a MESMA arvore de nodes", igual, $"{nodes} nodes");

		_pulados++;
		_naoMedidos.Add("People: o cartao de pessoa e a fileira de declaracoes (a bancada nunca conhece alguem: NPC nao entra no convivio)");
		Nota("  PULADA  o cartao de pessoa e as declaracoes   [a bancada nao tem como conhecer alguem: NPC nao e pessoa pro convivio]");

		// ---------------- 3. AS INJECOES ----------------
		Injeta("a regra das pilulas reprova uma pilula a mais ('Fantasma')",
			   !PilulasBatemComOsVisiveis([.. visiveis, "Fantasma"], visiveis));
		Injeta("...reprova uma pilula a menos",
			   !PilulasBatemComOsVisiveis(visiveis.Count > 0 ? [.. visiveis.Skip(1)] : ["x"], visiveis));
		if (visiveis.Count >= 2)
			Injeta("...e reprova a ORDEM trocada (a lista do mundo vem ordenada, a tela tem que repetir a ordem)",
				   !PilulasBatemComOsVisiveis([.. visiveis.AsEnumerable().Reverse()], visiveis));
	}
}
