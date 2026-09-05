using Godot;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Admin da `--diagabas` (F16). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// O QUE ELA COBRA: que os controles que as outras bancadas acham POR TEXTO continuem la ("Anunciar",
/// "promover", "banir", "Forcar", "Voltar ao natural", "Preparar limpeza...", os tres campos com os
/// placeholders de sempre, o `MaxLength` do fio), que cada secao virou um cartao, que os verbs de
/// admin vieram em cartoes por tema, que a ZONA DE PERIGO e o ULTIMO cartao e tem a borda vermelha
/// no pixel -- e que nada perigoso e apertado: nenhuma previa de limpeza e pedida, nunca.
/// </summary>
public partial class RoboDasAbas
{
	private async System.Threading.Tasks.Task F16_Admin(MenuJogo menu, GameClient cli)
	{
		Nota("--- F16: Admin: cartoes, os controles de contrato, e a zona de perigo por ultimo (sem apertar nada perigoso) ---");
		if (!cli.Atributos.Tem(Protocol.Poder.Admin))
		{
			ProvaNaoMedida("a aba Admin", "este personagem nao e admin (o bit Poder.Admin nao chegou): nao ha aba pra medir");
			return;
		}
		Button? aba = Botao(menu, Verbos.Admin);
		if (aba == null) { Checa("achei a aba Admin na barra", false); return; }
		await Clicar(aba);
		await Quadros(2);
		Control? pg = menu.PaginaDeTeste(Verbos.Admin);
		Checa("a aba Admin esta na tela", pg is { Visible: true } && menu.AbaDeTeste == Verbos.Admin, menu.AbaDeTeste);
		if (pg == null) return;

		// ---- os controles de contrato, por texto
		string[] botoes =
		[
			"Anunciar", "Forcar", "Voltar ao natural", "Lua cheia agora", "Meio-dia agora",
			"Voltar ao normal", "Liberar skills de Ki", "promover", "rebaixar", "banir", "perdoar",
			"atualizar lista", "dar skill", "tirar skill", "dar cargo", "tirar cargo", "PM",
		];
		List<string> faltando = botoes.Where(t => BotaoDeTextoExato(pg, t) == null).ToList();
		Checa($"CONTRATO: os {botoes.Length} botoes do painel existem com o texto de sempre", faltando.Count == 0, string.Join(", ", faltando));
		Button? preparar = Botao(pg, "Preparar limpeza");
		Checa("o botao 'Preparar limpeza total do servidor...' existe -- e a bancada NAO o aperta",
			  preparar != null && preparar.Text.StartsWith("Preparar limpeza"), preparar?.Text ?? "(nao achei)");
		Checa("CONTRA-EXEMPLO: sem previa pedida nao existe 'APAGAR O SERVIDOR' (os dois passos continuam sendo dois)",
			  BotaoDeTextoExato(pg, "APAGAR O SERVIDOR") == null && cli.Limpeza.Codigo.Length == 0);

		string[] campos = ["escreva e aperte Enter (ou o botao)", "conta ou nome de personagem", "nome da skill / chave do cargo / mensagem"];
		List<LineEdit> lineEdits = Todos(pg).OfType<LineEdit>().Where(l => l.IsVisibleInTree()).ToList();
		List<string> semCampo = campos.Where(p => !lineEdits.Any(l => l.PlaceholderText == p)).ToList();
		Checa("CONTRATO: os tres campos de texto existem com os placeholders de sempre (a --diagmudez e a --diagtecla acham um LineEdit aqui)",
			  semCampo.Count == 0, string.Join(" | ", semCampo));
		LineEdit? avisoCampo = lineEdits.FirstOrDefault(l => l.PlaceholderText == campos[0]);
		Checa("o campo do aviso guarda o teto do fio (MaxLength = Protocol.MaxArgDeVerbo)",
			  avisoCampo != null && avisoCampo.MaxLength == Protocol.MaxArgDeVerbo, $"{avisoCampo?.MaxLength}");
		Checa("o seletor de clima e o slider de forca existem",
			  Todos(pg).OfType<OptionButton>().Any(o => o.IsVisibleInTree()) && Todos(pg).OfType<HSlider>().Any(s => s.IsVisibleInTree()));
		Checa("cada forma do catalogo e um botao (Super Saiyajin, Super Saiyajin 3...)",
			  Botao(pg, "Super Saiyajin") != null && Botao(pg, "Super Saiyajin 3") != null);

		// ---- os verbs de admin em cartoes por tema
		List<Verbo> verbos = Verbos.Da(Verbos.Admin).ToList();
		List<string> semBotao = verbos.Where(v => BotaoDeTextoExato(pg, v.Nome) == null).Select(v => v.Nome).ToList();
		Checa($"CONTRATO: cada um dos {verbos.Count} verbs de Admin e um Button visivel com o texto exato", semBotao.Count == 0, string.Join(", ", semBotao));
		Checa("cada verb tem a frase do que faz visivel", verbos.All(v => v.Descricao.Length == 0 || Rotulo(pg, v.Descricao) != null));
		List<PanelContainer> temas = CartoesDeTema(pg);
		Checa("os verbs de admin estao em 3+ cartoes por tema, como na Other", temas.Count >= 3, string.Join(" | ", temas.Select(c => c.GetMeta("titulo").AsString())));
		Checa("CONTRA-EXEMPLO: nenhum verb de Other ('Toggle Knockback', 'Who') aparece na Admin",
			  BotaoDeTextoExato(pg, "Toggle Knockback") == null && BotaoDeTextoExato(pg, "Who") == null);

		// ---- os cartoes, e o ultimo e o de perigo
		List<PanelContainer> cartoes = Todos(pg).OfType<PanelContainer>()
			.Where(c => c.IsVisibleInTree() && c.HasMeta("cartao") && c.GetMeta("cartao").AsString() == "secao").ToList();
		List<string> titulos = cartoes.Select(c => c.GetMeta("titulo").AsString()).ToList();
		string[] esperados = ["Aviso ao servidor", "Clima deste planeta", "Forçar transformação", "Contas deste servidor", "Sobre o alvo marcado", "ZONA DE PERIGO"];
		List<string> semCartao = esperados.Where(e => !titulos.Contains(e)).ToList();
		Checa("cada secao da aba virou um cartao com titulo", semCartao.Count == 0, string.Join(", ", semCartao));
		Checa("a ZONA DE PERIGO e o ULTIMO cartao da aba (e preciso rolar ate o fim pra achar o botao)",
			  titulos.Count > 0 && titulos[^1] == "ZONA DE PERIGO", titulos.Count > 0 ? titulos[^1] : "(sem cartoes)");
		Checa("...e os cartoes de verbs vem ANTES dela", temas.Count > 0 && temas.All(t => cartoes.IndexOf(t) >= 0 && cartoes.IndexOf(t) < cartoes.Count - 1));
		PanelContainer? perigo = cartoes.LastOrDefault();
		Checa("...e ela carrega a marca de perigo (metadado)", perigo != null && perigo.HasMeta("perigo"));

		PanelContainer? painel = null;
		for (Node? p = pg.GetParent(); p != null; p = p.GetParent()) if (p is PanelContainer pc) painel = pc;
		Checa("o painel do menu continua com a largura de sempre nesta aba (a fileira do clima o alargava pra 880 px)",
			  painel != null && painel.Size.X <= 780, $"{painel?.Size.X} px");

		// ---- fotos e pixel: o topo, e o fim com a zona de perigo
		if (Ancestral<ScrollContainer>(pg) is { } rol) rol.ScrollVertical = 0;
		await Quadros(2);
		Image? topo = await Foto();
		await Guardar("depois-13-admin", topo);
		if (cartoes.Count > 0)
		{
			(Color cor, float frac) = Moda(topo, Caixa(cartoes[0].GetGlobalRect(), 4));
			ChecaNoPixel("o primeiro cartao e pintado com a paleta do tema (moda de pixel)", topo != null, NaPaleta(cor), $"moda {Hex(cor)} ({frac * 100:0}%)");
			string borda = CorDaBordaMaisProxima(CorDaBordaDeCima(topo, cartoes[0]));
			ChecaNoPixel("CONTRA-EXEMPLO: a borda do primeiro cartao NAO e vermelha", topo != null, borda != "Perigo", borda);
		}
		if (perigo != null && Ancestral<ScrollContainer>(pg) is { } rol2)
		{
			rol2.EnsureControlVisible(perigo);
			await Quadros(3);
			Image? fim = await Foto();
			await Guardar("depois-13b-admin-perigo", fim);
			Checa("a zona de perigo cabe na tela depois de rolar ate o fim", GetViewport().GetVisibleRect().Encloses(perigo.GetGlobalRect()), $"{perigo.GetGlobalRect()}");
			Color borda = CorDaBordaDeCima(fim, perigo);
			ChecaNoPixel("a borda da ZONA DE PERIGO e VERMELHA (Tema.Perigo) no pixel", fim != null,
						 CorDaBordaMaisProxima(borda) == "Perigo", $"{Hex(borda)} -> {CorDaBordaMaisProxima(borda)}");
		}
		// ---------------- F16b: verb de alvo SEM alvo abre a lista de quem esta online (dono, 2026-09-05) ----------------
		Nota("--- F16b: verbs de alvo sem alvo (ou com a marca em mim) abrem a lista de online ---");
		Checa("REGRA PURA: sem marca -> lista; marca em mim -> lista; marca em outro -> vai direto",
			  MenuJogo.PrecisaEscolherAlvo(0, 5) && MenuJogo.PrecisaEscolherAlvo(5, 5) && !MenuJogo.PrecisaEscolherAlvo(7, 5));
		Checa("PREMISSA: nao ha alvo marcado", cli.AlvoId == 0, $"{cli.AlvoId}");
		Button? matar = BotaoDeTextoExato(pg, "Kill Target");
		Checa("o botao 'Kill Target' existe e esta HABILITADO mesmo sem alvo (a lista e o caminho)", matar is { Disabled: false });
		if (matar != null)
		{
			bool vivoAntes = !cli.Sheet.Morto;
			await Clicar(matar);
			await Quadros(15);   // o servidor responde o `admin_online`
			pg = menu.PaginaDeTeste(Verbos.Admin);
			PanelContainer? escolha = pg == null ? null : Todos(pg).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("escolha_de_alvo"));
			Checa("apertar 'Kill Target' sem alvo NAO mata ninguem: abre o cartao 'Quem leva «Kill Target»?'",
				  escolha != null && escolha.GetMeta("escolha_de_alvo").AsString() == "admin_matar"
				  && escolha.GetMeta("titulo").AsString().Contains("Kill Target"),
				  escolha == null ? "sem cartao" : escolha.GetMeta("titulo").AsString());
			Checa("...o cartao e o PRIMEIRO da aba (a pergunta fica onde o olho esta)",
				  escolha != null && pg != null && Todos(pg).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("cartao")) == escolha);
			Checa("...a lista traz quem esta online -- eu, marcado '(você)'",
				  escolha != null && BotaoDeTextoExato(escolha, $"{cli.LocalName} (você)") != null, string.Join(",", cli.Online.Select(o => o.Nome)));
			Button? cancelar = escolha == null ? null : BotaoDeTextoExato(escolha, "cancelar");
			Checa("...e um botao 'cancelar'", cancelar != null);
			if (cancelar != null) { await Clicar(cancelar); await Quadros(2); }
			pg = menu.PaginaDeTeste(Verbos.Admin);
			Checa("cancelar fecha o cartao sem mandar nada (continuo vivo)",
				  pg != null && !Todos(pg).OfType<PanelContainer>().Any(c => c.HasMeta("escolha_de_alvo")) && vivoAntes == !cli.Sheet.Morto);
		}
		Button? heal = pg == null ? null : BotaoDeTextoExato(pg, "Heal");
		if (heal != null)
		{
			await Clicar(heal);
			await Quadros(15);
			pg = menu.PaginaDeTeste(Verbos.Admin);
			PanelContainer? escolha = pg == null ? null : Todos(pg).OfType<PanelContainer>().FirstOrDefault(c => c.HasMeta("escolha_de_alvo"));
			Button? eu = escolha == null ? null : BotaoDeTextoExato(escolha, $"{cli.LocalName} (você)");
			Checa("'Heal' sem alvo tambem pergunta (antes curava a mim por padrao)", escolha != null && eu != null);
			if (eu != null)
			{
				await Clicar(eu);
				await Quadros(6);
				pg = menu.PaginaDeTeste(Verbos.Admin);
				Checa("escolher a mim mesmo MANDA o verb e fecha o cartao",
					  pg != null && !Todos(pg).OfType<PanelContainer>().Any(c => c.HasMeta("escolha_de_alvo")));
			}
		}
		Checa("a bancada terminou sem pedir previa de limpeza (nenhum codigo chegou)", cli.Limpeza.Codigo.Length == 0);
	}
}
