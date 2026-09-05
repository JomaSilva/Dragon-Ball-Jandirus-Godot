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
		_naoMedidos.Add("People: o convivio REAL (familiaridade que so o servidor concede; NPC nao entra no convivio) -- o cartao e as declaracoes sao medidos com conhecidos PLANTADOS na F10b");
		Nota("  PULADA  o convivio real   [a bancada nao tem como conhecer alguem: NPC nao e pessoa pro convivio; o cartao em si e medido a seguir, plantado]");

		// ---------------- 2b. O RETRATO ("a foto da ultima vez que voce a viu") ----------------
		// A bancada nao conhece ninguem pelo convivio, entao a lista de conhecidos e PLANTADA pelo
		// mesmo evento que o fio dispara (`ConhecidosDeTeste`), e a memoria da aparencia tambem
		// (`VistosDeGente.AnotarDeTeste`) -- com a MINHA propria aparencia, que e a unica que este
		// processo tem em maos sem inventar uma. O que se mede e o cartao: com memoria, um boneco
		// vestido com ela; sem memoria, a frase "sem foto". Tudo desfeito no fim.
		{
			Nota("--- F10b: o retrato da ultima aparencia vista ---");
			var minha = cli.Visual;
			VistosDeGente.AnotarDeTeste("Retratada", "Saiyan", "Male", minha);
			cli.ConhecidosDeTeste([
				new GameClient.ConhecidoInfo("sigA", "Retratada", "Human", "Normal", 3, 0, 10f, 0f, false),
				new GameClient.ConhecidoInfo("sigB", "NuncaVista", "Saiyan", "Normal", 1, 0, 2f, 0f, false),
			]);
			try
			{
				menu.ForcarRedesenho();
				await Quadros(3);
				pg = menu.PaginaDeTeste("People");
				List<Control> cartoes = pg != null ? [.. CartoesDeSecao(pg, "pessoa")] : [];
				Checa("plantados dois conhecidos, a aba desenha DOIS cartoes de pessoa", cartoes.Count == 2, $"{cartoes.Count}");
				Control? cartaoVista = cartoes.FirstOrDefault(c => Rotulos(c).Any(l => l.Text == "Retratada"));
				Control? cartaoNunca = cartoes.FirstOrDefault(c => Rotulos(c).Any(l => l.Text == "NuncaVista"));
				CharacterVisual? boneco = cartaoVista != null ? Todos(cartaoVista).OfType<CharacterVisual>().FirstOrDefault() : null;
				Checa("a pessoa que eu JA VI tem um RETRATO: um `CharacterVisual` vestido dentro do cartao dela",
					  boneco != null, boneco != null ? "" : cartaoVista == null ? "cartao nao achado" : "cartao sem boneco");
				Checa("...vestido com a aparencia GUARDADA (as mesmas pecas de roupa que a minha, que foi a plantada)",
					  boneco != null && boneco.RoupasNoCorpoDeTeste().SequenceEqual(RoupasDe(minha)),
					  boneco == null ? "" : string.Join(",", boneco.RoupasNoCorpoDeTeste()));
				Checa("...dentro de uma moldura que RECORTA (o boneco e Node2D e escorreria por cima do nome)",
					  boneco?.GetParent() is Control { ClipContents: true });
				Checa("...e a legenda diz que e 'como eu vi da última vez'",
					  cartaoVista != null && Rotulos(cartaoVista).Any(l => l.Text == "como eu vi da última vez"));
				Checa("a pessoa que eu NUNCA VI nao tem boneco, e o cartao diz 'sem foto'",
					  cartaoNunca != null && !Todos(cartaoNunca).OfType<CharacterVisual>().Any()
					  && Rotulos(cartaoNunca).Any(l => l.Text.StartsWith("sem foto", StringComparison.Ordinal)));
				Checa("o cartao plantado traz a FILEIRA DE DECLARACOES (botoes de relacao, 'declarar' ou 'exige N de convivio')",
					  cartaoVista != null && Todos(cartaoVista).OfType<Button>()
						.Any(b => b.TooltipText == "declarar" || b.TooltipText.StartsWith("exige", StringComparison.Ordinal)));
				// AS TRES MEDIDAS COM NOME (dono, 2026-09-05: "explique melhor nessa pagina o que cada coisa quer dizer").
				Checa("a aba abre com a legenda 'Como ler esta aba' (afinidade automatica x convivio x declaracao)",
					  pg != null && Todos(pg).OfType<PanelContainer>().Any(c => c.HasMeta("titulo") && c.GetMeta("titulo").AsString() == "Como ler esta aba")
					  && Rotulos(pg).Any(l => l.Text.StartsWith("AFINIDADE (automática)", StringComparison.Ordinal))
					  && Rotulos(pg).Any(l => l.Text.StartsWith("CONVÍVIO", StringComparison.Ordinal))
					  && Rotulos(pg).Any(l => l.Text.StartsWith("DECLARAÇÃO", StringComparison.Ordinal)));
				Checa("no cartao, a barra se chama 'afinidade (automática)' e nao mais 'proximidade'",
					  cartaoVista != null && Rotulos(cartaoVista).Any(l => l.Text.StartsWith("afinidade (automática)", StringComparison.Ordinal))
					  && !Rotulos(cartaoVista).Any(l => l.Text == "proximidade"));
				Checa("...e a linha do convivio diz o que ele JA libera e a proxima porta (convivio 3: neutro, proxima 'ruim' aos 10)",
					  cartaoVista != null && Rotulos(cartaoVista).Any(l => l.Text.Contains("convívio 3") && l.Text.Contains("libera até: neutro")
																	  && l.Text.Contains("próxima: ruim (10)") && l.Text.Contains("sua declaração: nenhuma")),
					  string.Join(" | ", cartaoVista == null ? [] : Rotulos(cartaoVista).Select(l => l.Text).Where(t => t.Contains("convívio"))));
				Image? f2 = await Foto();
				await Guardar("people-02-retrato", f2);

				// O RELOGIO DO RETRATO E O DO MUNDO: remontar a aba nao reinicia a piscada (dono, 2026-09-05).
				double fase1 = boneco?.FaseDoCorpoDeTeste ?? -1, ciclo = boneco?.CicloDoCorpoDeTeste ?? 0;
				ulong t1 = Time.GetTicksMsec();
				Checa("o retrato anda no relogio do MUNDO (flag ligada) e o corpo dele tem um ciclo",
					  boneco is { RelogioDoMundo: true } && ciclo > 0, $"ciclo {ciclo:0.00} s");
				await Quadros(20);
				menu.ForcarRedesenho();
				await Quadros(2);
				pg = menu.PaginaDeTeste("People");
				cartoes = pg != null ? [.. CartoesDeSecao(pg, "pessoa")] : [];
				CharacterVisual? boneco2 = cartoes.Select(c => Todos(c).OfType<CharacterVisual>().FirstOrDefault()).FirstOrDefault(b => b != null);
				double fase2 = boneco2?.FaseDoCorpoDeTeste ?? -1;
				double passou = (Time.GetTicksMsec() - t1) / 1000.0;
				double esperado = ciclo > 0 ? (fase1 + passou) % ciclo : 0;
				double erro = ciclo > 0 ? Math.Abs(((fase2 - esperado) % ciclo + ciclo * 1.5) % ciclo - ciclo * 0.5) : 99;
				Checa("REMONTADA a aba, o retrato NOVO esta na fase que o antigo teria agora (a piscada nao recomeca a cada redesenho)",
					  boneco2 != null && !ReferenceEquals(boneco2, boneco) && erro < 0.12,
					  $"fase {fase1:0.00} -> {fase2:0.00} s apos {passou:0.00} s (esperado {esperado:0.00}, erro {erro:0.00})");
				var noMundo = new CharacterVisual();   // sem a flag: e o boneco do mundo
				AddChild(noMundo);
				noMundo.Vestir(VistosDeGente.Catalogo!, minha, "Saiyan", "Male");
				Checa("CONTRA-EXEMPLO: um corpo do MUNDO (sem a flag) nasce com o relogio em ZERO -- o soco recomeca do primeiro quadro",
					  noMundo.FaseDoCorpoDeTeste == 0, $"{noMundo.FaseDoCorpoDeTeste}");
				noMundo.QueueFree();
			}
			finally
			{
				cli.ConhecidosDeTeste([]);
				VistosDeGente.EsquecerDeTeste();
				menu.ForcarRedesenho();
				await Quadros(2);
			}
		}

		// ---------------- 3. AS INJECOES ----------------
		Injeta("a regra das pilulas reprova uma pilula a mais ('Fantasma')",
			   !PilulasBatemComOsVisiveis([.. visiveis, "Fantasma"], visiveis));
		Injeta("...reprova uma pilula a menos",
			   !PilulasBatemComOsVisiveis(visiveis.Count > 0 ? [.. visiveis.Skip(1)] : ["x"], visiveis));
		if (visiveis.Count >= 2)
			Injeta("...e reprova a ORDEM trocada (a lista do mundo vem ordenada, a tela tem que repetir a ordem)",
				   !PilulasBatemComOsVisiveis([.. visiveis.AsEnumerable().Reverse()], visiveis));
	}

	/// <summary>As pecas de roupa de uma aparencia, na ordem em que o `CharacterVisual` as veste -- pra comparar com `RoupasNoCorpoDeTeste`.</summary>
	private static List<string> RoupasDe(Jandirus.Core.Appearance.Appearance ap) => [.. ap.Roupa.Select(r => r.Caminho)];
}
