using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Gente (People) da `--diagabas` (F10). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ============================ O CONTRATO DA ABA, DESDE 2026-10-10 ============================
/// O dono pediu o revamp (*"a galera ta se confundindo MUITO ... so deixar convivio e declaracao"*), e o
/// contrato daqui virou o do redesenho: cada cartao de pessoa tem o CONVIVIO (um numero, uma barra) e a
/// REGUA das nove declaracoes com exatamente uma acesa -- e nenhum rotulo que o jogo deduz (pilula de
/// estado, barra de afinidade, pedido de amizade). E cada cartao tem o botao que o ABRE (os detalhes da
/// pessoa: so com convivio minimo; abaixo dele o cartao abre trancado). As REGRAS do cartao moram no
/// `RoboDeConhecidos` (`ProblemasDoCartao`, `ProblemasDosDetalhes`, `PalavrasDaAfinidade`), que e a bancada
/// propria da aba (`--diagconhecidos`: a lista pelo fio, o clique, o portao do servidor, o pacote dos
/// detalhes); esta familia as usa sobre conhecidos PLANTADOS.
///
/// ============================ O QUE ELA PROVA, E O QUE NAO TEM COMO ============================
/// A bancada nasce sozinha e os NPCs em volta nao sao "pessoas" pro convivio (`GameServer.Convivio`:
/// corpo sem conta nao entra na lista de ninguem), entao ela NUNCA conhece alguem -- o que da pra
/// provar aqui e a metade vazia (a faixa em 0, a frase de vazio, nenhum cartao de pessoa) e a metade
/// viva de "quem esta por perto": as pilulas tem que ser EXATAMENTE os nomes que o
/// `World.NomesVisiveis()` devolve, na mesma ordem, e nunca o proprio nome da bancada. O cartao e a regua
/// sao medidos com conhecidos plantados no CLIENTE (F10b) -- o convivio de verdade vai pro placar de
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
		Nota("--- F10: Gente: a faixa de conhecidos, 'como funciona', cartoes de pessoa (convivio + regua), quem esta por perto ---");
		menu.IrPara("People");
		await Quadros(3);
		menu.ForcarRedesenho();
		await Quadros(3);
		Control? pg = menu.PaginaDeTeste("People");
		Checa("a pagina de People existe e esta na tela", pg is { Visible: true });
		if (pg == null) return;

		// ---------------- 1. A METADE VAZIA (a bancada nao conhece ninguem) ----------------
		Checa("PRECONDICAO: a bancada nao conhece ninguem", cli.Conhecidos.Count == 0, $"{cli.Conhecidos.Count} conhecidos");
		string? faixa = menu.ValorDesenhado("People", "Conhecidos");
		Checa("a FAIXA 'Conhecidos' diz 0", faixa != null && faixa.StartsWith("0", StringComparison.Ordinal), faixa ?? "(nula)");
		Checa("sem conhecidos: a frase de vazio esta escrita",
			  Rotulos(pg).Any(l => l.Text.StartsWith("Você ainda não conviveu com ninguém", StringComparison.Ordinal)));
		Checa("...e nao ha cartao de pessoa nem regua de declaracoes",
			  !CartoesDeSecao(pg, "pessoa").Any() && !Todos(pg).OfType<Button>().Any(b => b.HasMeta("declaracao")));
		Checa("a aba abre com 'Como funciona': o CONVIVIO (o jogo conta) e O QUE ELA E PRA VOCE (voce escolhe)",
			  CartaoComTitulo(pg, "Como funciona") is { } como
			  && Rotulo(como, MenuJogo.TituloDoConvivio) != null && Rotulos(como).Any(l => l.Text.StartsWith("o jogo conta", StringComparison.Ordinal))
			  && Rotulo(como, MenuJogo.TituloDaEscolha) != null && Rotulos(como).Any(l => l.Text.StartsWith("você escolhe", StringComparison.Ordinal)));
		Checa("o pedido de amizade SAIU com a afinidade: nem o cartao de destaque, nem os botoes Aceitar/Recusar",
			  CartaoComTitulo(pg, "Pedido de amizade") == null && Botao(pg, "Aceitar") == null && Botao(pg, "Recusar") == null);

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
		_naoMedidos.Add("People: o convivio REAL (que so o servidor concede; NPC nao entra no convivio) -- o cartao e a regua sao medidos com conhecidos PLANTADOS na F10b, e o caminho pelo fio na `--diagconhecidos`");
		Nota("  PULADA  o convivio real   [a bancada nao tem como conhecer alguem: NPC nao e pessoa pro convivio; o cartao em si e medido a seguir, plantado]");

		// ---------------- 2b. O CARTAO DE PESSOA: O RETRATO, O CONVIVIO E A REGUA ----------------
		// A bancada nao conhece ninguem pelo convivio, entao a lista de conhecidos e PLANTADA pelo
		// mesmo evento que o fio dispara (`ConhecidosDeTeste`), e a memoria da aparencia tambem
		// (`VistosDeGente.AnotarDeTeste`) -- com a MINHA propria aparencia, que e a unica que este
		// processo tem em maos sem inventar uma. Tudo desfeito no fim.
		{
			Nota("--- F10b: o cartao de pessoa (retrato, convivio, regua de declaracoes) ---");
			var minha = cli.Visual;
			VistosDeGente.AnotarDeTeste("Retratada", "Saiyan", "Male", minha);
			// A Retratada: 62 de convivio e declarada AMIGA. A NuncaVista: 3 de convivio, nada declarado, sem
			// foto. A SoDeVista: convivio zero -- nao ganha cartao.
			cli.ConhecidosDeTeste([
				new GameClient.ConhecidoInfo("sigA", "Retratada", "Human", 62, (byte)Relacao.Bom),
				new GameClient.ConhecidoInfo("sigB", "NuncaVista", "Saiyan", 3, (byte)Relacao.Nenhuma),
				new GameClient.ConhecidoInfo("sigC", "SoDeVista", "Namekian", 0, (byte)Relacao.Nenhuma),
			]);
			try
			{
				await Quadros(3);   // SEM forcar: a lista nova tem que mudar a assinatura e refazer a pagina sozinha
				pg = menu.PaginaDeTeste("People");
				List<Control> cartoes = pg != null ? [.. CartoesDeSecao(pg, "pessoa")] : [];
				Checa("plantados tres conhecidos, a aba se refaz SOZINHA e desenha DOIS cartoes de pessoa (quem tem convivio)",
					  cartoes.Count == 2, $"{cartoes.Count}");
				Control? cartaoVista = cartoes.FirstOrDefault(c => Rotulos(c).Any(l => l.Text == "Retratada"));
				Control? cartaoNunca = cartoes.FirstOrDefault(c => Rotulos(c).Any(l => l.Text == "NuncaVista"));
				PanelContainer? soDeVista = pg != null ? CartaoComTitulo(pg, "Só de vista") : null;
				Checa("...e quem tem convivio ZERO vai por nome no cartao 'Só de vista', sem regua",
					  soDeVista != null && PilulasEm(soDeVista).SequenceEqual(["SoDeVista"])
					  && !cartoes.Any(c => Rotulos(c).Any(l => l.Text == "SoDeVista")),
					  soDeVista == null ? "sem o cartao" : string.Join(",", PilulasEm(soDeVista)));

				CharacterVisual? boneco = cartaoVista != null ? Todos(cartaoVista).OfType<CharacterVisual>().FirstOrDefault() : null;
				Checa("a pessoa que eu JA VI tem um RETRATO: um `CharacterVisual` vestido dentro do cartao dela",
					  boneco != null, boneco != null ? "" : cartaoVista == null ? "cartao nao achado" : "cartao sem boneco");
				Checa("...vestido com a aparencia GUARDADA (as mesmas pecas de roupa que a minha, que foi a plantada)",
					  boneco != null && boneco.RoupasNoCorpoDeTeste().SequenceEqual(RoupasDe(minha)),
					  boneco == null ? "" : string.Join(",", boneco.RoupasNoCorpoDeTeste()));
				Checa("...dentro de uma moldura que RECORTA (o boneco e Node2D e escorreria por cima do nome)",
					  boneco?.GetParent() is Control { ClipContents: true });
				Checa("...e a ficha diz que e 'como você viu da última vez'",
					  cartaoVista != null && Rotulos(cartaoVista).Any(l => l.Text.EndsWith("como você viu da última vez", StringComparison.Ordinal)));
				Checa("a pessoa que eu NUNCA VI nao tem boneco, e a moldura diz 'sem foto'",
					  cartaoNunca != null && !Todos(cartaoNunca).OfType<CharacterVisual>().Any()
					  && Rotulos(cartaoNunca).Any(l => l.Text.StartsWith("sem foto", StringComparison.Ordinal)));

				// O CONTRATO DO REDESENHO, pelas regras da bancada propria da aba.
				List<string> ruimA = cartaoVista != null ? RoboDeConhecidos.ProblemasDoCartao(RoboDeConhecidos.Ler(cartaoVista), 62, Relacao.Bom) : ["cartao nao achado"];
				Checa("o cartao da Retratada (62 de convivio, declarada amiga): UM numero de convivio, UMA barra, a regua das 9 com "
					  + "'Amigo' aceso, as que 62 pagam abertas, 'Melhor amigo (100)' e 'Amor (200)' fechadas com o preco, e a linha de efeito do amigo",
					  ruimA.Count == 0, string.Join("; ", ruimA));
				List<string> ruimB = cartaoNunca != null ? RoboDeConhecidos.ProblemasDoCartao(RoboDeConhecidos.Ler(cartaoNunca), 3, Relacao.Nenhuma) : ["cartao nao achado"];
				Checa("o cartao da NuncaVista (3 de convivio, nada declarado): o NEUTRO aceso e as outras oito fechadas com o preco",
					  ruimB.Count == 0, string.Join("; ", ruimB));
				Checa("...a linha de efeito do amigo fala de FÚRIA; a do neutro diz que nao muda regra nenhuma",
					  cartaoVista != null && RoboDeConhecidos.Ler(cartaoVista).Efeito.Contains("FÚRIA")
					  && cartaoNunca != null && RoboDeConhecidos.Ler(cartaoNunca).Efeito.Contains("não muda nenhuma regra"));
				List<string> velhas = pg != null ? RoboDeConhecidos.PalavrasDaAfinidade(RoboDeConhecidos.TextosDe(pg)) : ["sem pagina"];
				Checa("NADA da afinidade antiga na pagina: nem a palavra, nem os rotulos que o jogo deduzia, nem 'RIVAL', nem o pedido de amizade",
					  velhas.Count == 0, string.Join("; ", velhas));
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

				// ---------------- 2c. OS DETALHES: O BOTAO, O CARTAO TRANCADO E O PEDIDO ----------------
				// Segundo pedido do dono (2026-10-10): clicar numa pessoa abre o que eu sei dela, e so com convivio
				// minimo (`Convivio.ConvivioParaDetalhes`). Os plantados NAO existem no servidor, entao o espiao
				// ENGOLE o pedido: o que se mede aqui e o que a TELA faz. O caminho inteiro -- o servidor que so
				// manda a quem conviveu, o pacote, o cartao aberto com a historia -- e da `--diagconhecidos`.
				{
					int minimo = Convivio.ConvivioParaDetalhes;
					var mandados = new List<string>();
					Func<string, string, bool>? espiaoDeAntes = GameClient.EspiaoDeVerbos;
					// SO O `detalhes` E ENGOLIDO (e contado): os outros verbs que o cliente mandar nesse meio tempo passam.
					GameClient.EspiaoDeVerbos = (cmd, arg) =>
					{
						if (cmd != "detalhes") return espiaoDeAntes?.Invoke(cmd, arg) ?? true;
						mandados.Add($"{cmd}:{arg}");
						return false;
					};
					try
					{
						Control? PessoaNaTela(string nome) => menu.PaginaDeTeste("People") is { } pagina
							? CartoesDeSecao(pagina, "pessoa").FirstOrDefault(c => Rotulos(c).Any(l => l.Text == nome))
							: null;
						Button? BotaoDeDetalhesDe(string nome) => PessoaNaTela(nome) is { } cartao
							? Todos(cartao).OfType<Button>().FirstOrDefault(b => b.HasMeta("detalhes"))
							: null;

						Checa($"o cartao da Retratada (62 de convivio) tem o botao 'Ver detalhes'; o da NuncaVista (3) tem 'Detalhes ({minimo})', com o preco",
							  BotaoDeDetalhesDe("Retratada") is { Text: "Ver detalhes", Disabled: false }
							  && BotaoDeDetalhesDe("NuncaVista") is { Disabled: false } fechado && fechado.Text == $"Detalhes ({minimo})",
							  $"'{BotaoDeDetalhesDe("Retratada")?.Text}' / '{BotaoDeDetalhesDe("NuncaVista")?.Text}'");

						BotaoDeDetalhesDe("NuncaVista")?.EmitSignal(BaseButton.SignalName.Pressed);
						await Quadros(3);
						RoboDeConhecidos.CartaoLido? trancado = PessoaNaTela("NuncaVista") is { } naTela ? RoboDeConhecidos.Ler(naTela) : null;
						List<string> ruimDoTrancado = trancado != null ? RoboDeConhecidos.ProblemasDoCartao(trancado, 3, Relacao.Nenhuma) : ["cartao nao achado"];
						Checa($"clicar em quem o convivio NAO paga abre o cartao TRANCADO: a frase diz o que falta ({minimo} de convivio, voces tem 3), "
							  + "nada dela aparece, e o resto do cartao continua no contrato",
							  trancado is { Detalhes.Estado: "trancados" } && ruimDoTrancado.Count == 0,
							  $"estado '{trancado?.Detalhes.Estado}'; {string.Join("; ", ruimDoTrancado)}");
						Checa("...e a tela NAO manda pedido nenhum (ela ja sabe a resposta)", mandados.Count == 0, string.Join(",", mandados));
						await RolarAteVer(PessoaNaTela("NuncaVista"));
						Image? f3 = await Foto();
						await Guardar("people-03-detalhes-trancados", f3);

						BotaoDeDetalhesDe("Retratada")?.EmitSignal(BaseButton.SignalName.Pressed);
						await Quadros(3);
						RoboDeConhecidos.CartaoLido? buscando = PessoaNaTela("Retratada") is { } naTela2 ? RoboDeConhecidos.Ler(naTela2) : null;
						List<string> ruimDoBuscando = buscando != null ? RoboDeConhecidos.ProblemasDoCartao(buscando, 62, Relacao.Bom) : ["cartao nao achado"];
						Checa("clicar em quem o convivio PAGA manda UM verb `detalhes` com a assinatura dela, e o cartao fica BUSCANDO ate a resposta chegar",
							  mandados.SequenceEqual(["detalhes:sigA"]) && buscando is { Detalhes.Estado: "buscando" } && ruimDoBuscando.Count == 0,
							  $"verbs [{string.Join(",", mandados)}]; estado '{buscando?.Detalhes.Estado}'; {string.Join("; ", ruimDoBuscando)}");
						Checa("...e so UM cartao fica aberto por vez: o da NuncaVista fechou",
							  PessoaNaTela("NuncaVista") is { } outra && RoboDeConhecidos.Ler(outra).Detalhes.Estado == "");
						menu.ForcarRedesenho();
						await Quadros(2);
						Checa("...e a pagina redesenhada NAO repete o pedido (um por abertura, e nao um por redesenho)",
							  mandados.Count == 1, string.Join(",", mandados));

						if (buscando != null)
							Injeta("o contrato dos detalhes reprova o cartao BUSCANDO de quem o convivio nao paga (o mesmo cartao, com 3 de convivio)",
								   RoboDeConhecidos.ProblemasDosDetalhes(buscando.Detalhes, 3).Count > 0);
						if (trancado != null)
							Injeta("...e reprova o cartao TRANCADO que mostra a idade assim mesmo",
								   RoboDeConhecidos.ProblemasDosDetalhes(trancado.Detalhes with { Fatos = [("Idade", "30 anos")] }, 3).Count > 0);

						// FECHA O QUE ABRIU: as injecoes abaixo leem o cartao da Retratada fechado.
						BotaoDeDetalhesDe("Retratada")?.EmitSignal(BaseButton.SignalName.Pressed);
						await Quadros(3);
						pg = menu.PaginaDeTeste("People");
					}
					finally { GameClient.EspiaoDeVerbos = espiaoDeAntes; }
				}

				// ---------------- 3. AS INJECOES DO CARTAO ----------------
				// O cartao e RELIDO: o redesenho forcado logo acima liberou os nodes de antes.
				Control? vivo = pg != null ? CartoesDeSecao(pg, "pessoa").FirstOrDefault(c => Rotulos(c).Any(l => l.Text == "Retratada")) : null;
				if (vivo != null)
				{
					RoboDeConhecidos.CartaoLido certo = RoboDeConhecidos.Ler(vivo);
					Injeta("o contrato do cartao reprova a PILULA de estado de volta (o desenho antigo: grau de afinidade + declaracao + RIVAL)",
						   RoboDeConhecidos.ProblemasDoCartao(certo with { Pilulas = 2 }, 62, Relacao.Bom).Count > 0);
					Injeta("...reprova a segunda BARRA (a de afinidade ao lado da de convivio)",
						   RoboDeConhecidos.ProblemasDoCartao(certo with { Barras = 2 }, 62, Relacao.Bom).Count > 0);
					Injeta("...reprova o cartao que diz 'amigo' quando o servidor guarda 'neutro'",
						   RoboDeConhecidos.ProblemasDoCartao(certo, 62, Relacao.Neutro).Count > 0);
					Injeta("...e reprova a opcao que 62 de convivio nao paga aparecendo aberta (o mesmo cartao, com 200)",
						   RoboDeConhecidos.ProblemasDoCartao(certo, 200, Relacao.Bom).Count > 0);
				}
				else Checa("o cartao da Retratada continua na tela pras injecoes", false);
				Injeta("a regra das palavras reprova 'afinidade (automática)' e o rotulo deduzido 'Mal conhecido' -- e deixa passar 'Inimigo mortal', que hoje e declaracao",
					   RoboDeConhecidos.PalavrasDaAfinidade(["afinidade (automática)"]).Count > 0
					   && RoboDeConhecidos.PalavrasDaAfinidade(["Mal conhecido"]).Count > 0
					   && RoboDeConhecidos.PalavrasDaAfinidade(["Inimigo mortal"]).Count == 0);
			}
			finally
			{
				cli.ConhecidosDeTeste([]);
				VistosDeGente.EsquecerDeTeste();
				menu.ForcarRedesenho();
				await Quadros(2);
			}
		}

		// ---------------- 4. AS INJECOES DAS PILULAS ----------------
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
