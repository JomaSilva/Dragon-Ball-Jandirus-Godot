using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.Verbos;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A familia de provas da aba Alunos / Mestre da `--diagabas` (F17). Ver o cabecalho de `RoboDasAbas.cs`.
///
/// ============================ O CONTRATO DA ABA (2026-10-11) ============================
/// O dono pediu *"uma aba people so q de alunos (caso vc seja o mestre) e mestre (caso vc seja aluno)"*, e a regra
/// da forca veio junto: *"o mestre nao pode ver o bp dos alunos"*. O contrato daqui e o da aba nova: ela so EXISTE
/// pra quem tem o vinculo (a tabela de requisitos, com os fatos que o servidor conta), chama-se "Alunos" pra quem
/// guia alguem e "Mestre" pra quem so e guiado, tem um cartao por pessoa com o progresso NO QUE FOI ENSINADO, os
/// gestos dos cartoes mandam o verb certo pra pessoa certa -- e nao ha numero de poder em lugar nenhum. As REGRAS de
/// cada cartao moram no `RoboDeAlunos` (`ProblemasDoCartaoDeAluno`, `...DoMestre`, `...DeColega`, `...DoConjunto`,
/// `PoderEscrito`), que e a bancada propria da aba (`--diagalunos`: os fatos e o painel pelo fio, o sentido de Ki de
/// verdade, os gestos no servidor); esta familia as usa sobre um painel PLANTADO.
///
/// ============================ O QUE ELA PROVA, E O QUE NAO TEM COMO ============================
/// A bancada nasce sozinha: nao tem mestre nem aluno, e por isso prova primeiro a metade vazia (a aba nao esta na
/// barra). O resto e plantado no CLIENTE, pelas duas portas de bancada: o personagem que o menu acha que e
/// (`PosseDoCliente.DeTeste`, com os fatos "tem aluno" / "tem mestre") e o painel (`GameClient.MestreEAlunosDeTeste`,
/// pelo mesmo evento que o fio dispara). Os verbs `alunos*` que a tela manda sao ENGOLIDOS e contados -- no servidor
/// este personagem nao tem vinculo nenhum. O que vem do servidor de verdade (quem recebe o painel, a recusa do
/// pacote forjado, a leitura do sentido de Ki no cartao) vai pro placar de nao-medidos: e da `--diagalunos`.
/// ============================================================================================
/// </summary>
public partial class RoboDasAbas
{
	/// <summary>
	/// O personagem de verdade, com os dois fatos do discipulado trocados pelos da bancada. Tudo o mais (raca,
	/// tecnicas, itens, lugar) continua o dele: o resto do menu nao pode mudar por causa desta familia.
	/// </summary>
	private sealed class PosseComVinculo(IPosse real, Fatos doVinculo) : IPosse
	{
		public string Raca => real.Raca;
		public string Classe => real.Classe;
		public bool SabeTecnica(string verbo) => real.SabeTecnica(verbo);
		public bool SabeSkill(string typepath) => real.SabeSkill(typepath);
		public bool TemItem(string id) => real.TemItem(id);
		public bool TemCargo(string chave) => real.TemCargo(chave);
		public Fatos Fatos => (real.Fatos & ~(Fatos.TemMestre | Fatos.TemAluno)) | doVinculo;
		public Lugar Lugar => real.Lugar;
	}

	private async System.Threading.Tasks.Task F17_Alunos(MenuJogo menu, GameClient cli)
	{
		Nota("--- F17: Alunos / Mestre: a aba so de quem tem o vinculo, um cartao por pessoa, os gestos, e nenhum numero de poder ---");
		const string alunos = MenuJogo.AbaDeAlunos, mestre = MenuJogo.AbaDeMestre;

		// ---------------- 1. SEM VINCULO, SEM ABA ----------------
		menu.IrPara("Stats");
		await Quadros(2);
		string[] abas = menu.AbasDeTeste;
		Checa("PRECONDICAO: a bancada nao tem mestre nem aluno (os fatos do servidor), e nenhuma posse de bancada esta ligada",
			  (cli.Fatos & (Fatos.TemMestre | Fatos.TemAluno)) == 0 && PosseDoCliente.DeTeste == null, $"{cli.Fatos}");
		Checa("...e por isso NAO ha aba 'Alunos' nem 'Mestre' na barra, nem pagina montada pra elas (o contrato do Abas())",
			  !abas.Contains(alunos) && !abas.Contains(mestre) && menu.PaginaDeTeste(alunos) == null && menu.PaginaDeTeste(mestre) == null,
			  string.Join(",", abas));
		Checa("a linha `alunos` da tabela de requisitos e a que decide (tem um mestre OU um aluno), e os dois gestos de cartao pedem 'tem aluno'",
			  RequisitosDosVerbos.Linha(RequisitosDosVerbos.ChaveDoVerbo(MenuJogo.VerboDoDiscipulado)) is { } linha && linha.Onde == RequisitosDosVerbos.AbaDoDiscipulado
			  && !RequisitosDosVerbos.DoVerbo("alunos").Atende(new PosseEscrita())
			  && RequisitosDosVerbos.DoVerbo("alunos").Atende(new PosseEscrita { Fatos = Fatos.TemMestre })
			  && RequisitosDosVerbos.DoVerbo("alunos").Atende(new PosseEscrita { Fatos = Fatos.TemAluno })
			  && !RequisitosDosVerbos.DoVerbo("alunos_dispensar").Atende(new PosseEscrita { Fatos = Fatos.TemMestre })
			  && RequisitosDosVerbos.DoVerbo("alunos_ensinar").Atende(new PosseEscrita { Fatos = Fatos.TemAluno }));

		// O ELENCO PLANTADO. A Retratada esta no mundo e eu ja a vi (a minha propria aparencia: e a unica que este
		// processo tem em maos sem inventar uma); o Ausente saiu ha duas horas e eu nunca o vi.
		const string licao = "/datum/skill/flying";
		var retratada = new AlunoNoPainel(
			new PessoaNoPainel("9100000001", "Retratada", "Saiyan", true, 0, 86_400 * 12),
			[
				new EnsinamentoNoPainel(TipoDeEnsinamento.Forma, "ssj1", 86_400 * 10, 3, true, ProgressoDeAluno.DaForma(true, 40)),
				new EnsinamentoNoPainel(TipoDeEnsinamento.Licao, licao, 86_400 * 8, 0, false, ProgressoDeAluno.DaLicao(true, 3, 1, 50, 100)),
			]);
		var ausente = new AlunoNoPainel(
			new PessoaNoPainel("9100000002", "Ausente", "Human", false, 7200, 86_400 * 30),
			[new EnsinamentoNoPainel(TipoDeEnsinamento.Licao, licao, 86_400 * 25, 0, false, ProgressoDeAluno.DaLicao(true, 3, 3, 0, 0))]);
		// O Novato virou aluno ha uma hora e ainda nao recebeu nada; o Antigo e de um vinculo de ANTES de o diario existir
		// (sem data): o que lhe foi ensinado naquele tempo ninguem anotou.
		var semNada = new AlunoNoPainel(new PessoaNoPainel("9100000003", "Novato", "Human", true, 0, 3600), []);
		var antigo = new AlunoNoPainel(new PessoaNoPainel("9100000004", "Antigo", "Human", true, 0, 0), []);
		var deQuemGuia = new PainelDoDiscipulado(null, [retratada, ausente, semNada, antigo]);

		EnsinamentoNoPainel minhaForma = new(TipoDeEnsinamento.Forma, "ssj1", 86_400 * 7, 2, true, ProgressoDeAluno.DaForma(true, 30));
		EnsinamentoNoPainel minhaLicao = new(TipoDeEnsinamento.Licao, licao, 86_400 * 6, 0, false, ProgressoDeAluno.DaLicao(true, 3, 1, 50, 100));
		var oMestre = new MestreNoPainel(
			new PessoaNoPainel("9100000009", "Bardock", "Saiyan", true, 0, 86_400 * 9),
			[
				new EnsinamentoDoMestre(minhaForma, [new ColegaNoEnsinamento(1, ProgressoDeAluno.DaForma(true, 62)), new ColegaNoEnsinamento(0, ProgressoDeAluno.DaForma(false, 0))]),
				new EnsinamentoDoMestre(minhaLicao, []),
			],
			["Raditz", "Vegeta", "Nappa"]);
		var deQuemEGuiado = new PainelDoDiscipulado(oMestre, []);

		IPosse real = PosseDoCliente.Atual;
		var mandados = new List<string>();
		Func<string, string, bool>? espiaoDeAntes = GameClient.EspiaoDeVerbos;
		// SO OS `alunos*` SAO ENGOLIDOS (e contados): os outros verbs que o cliente mandar nesse meio tempo passam.
		GameClient.EspiaoDeVerbos = (cmd, arg) =>
		{
			if (!cmd.StartsWith("alunos", StringComparison.Ordinal)) return espiaoDeAntes?.Invoke(cmd, arg) ?? true;
			mandados.Add($"{cmd}:{arg}");
			return false;
		};
		List<string> Gestos() => [.. mandados.Where(v => !v.StartsWith("alunos:", StringComparison.Ordinal))];
		VistosDeGente.AnotarDeTeste("Retratada", "Saiyan", "Male", cli.Visual);

		try
		{
			// ---------------- 2. QUEM GUIA: A ABA "ALUNOS" ----------------
			PosseDoCliente.DeTeste = new PosseComVinculo(real, Fatos.TemAluno);
			menu.ForcarRedesenho();
			await Quadros(3);
			abas = menu.AbasDeTeste;
			int people = Array.IndexOf(abas, "People");
			Checa("com o fato 'tem aluno' a aba 'Alunos' NASCE na barra, logo depois da People -- e nao ha aba 'Mestre' (o nome segue o papel)",
				  people >= 0 && people + 1 < abas.Length && abas[people + 1] == alunos && !abas.Contains(mestre), string.Join(",", abas));
			Button? b = Botao(menu, alunos);
			Checa("a aba 'Alunos' tem botao na barra", b != null);
			if (b == null) return;
			await Clicar(b);
			Control? pg = menu.PaginaDeTeste(alunos);
			Checa("clicar nele abre 'Alunos'", menu.AbaDeTeste == alunos && pg is { Visible: true }, menu.AbaDeTeste);
			if (pg == null) return;
			Checa("sem painel no cliente a pagina diz que esta BUSCANDO e manda UM pedido (o verb `alunos`, sem argumento)",
				  Rotulos(pg).Any(l => l.HasMeta("discipulado") && l.GetMeta("discipulado").AsString() == "buscando") && mandados.SequenceEqual(["alunos:"]),
				  string.Join(",", mandados));
			menu.ForcarRedesenho();
			Checa("...e a pagina redesenhada NAO repete o pedido (um por estado do que o cliente guarda, e nao um por redesenho)",
				  mandados.SequenceEqual(["alunos:"]), string.Join(",", mandados));

			cli.MestreEAlunosDeTeste(deQuemGuia);
			await Quadros(3);   // SEM forcar: o painel novo tem que refazer a pagina sozinho (o evento do fio)
			List<string> ordem = [.. RoboDeAlunos.Cartoes(pg, "aluno").Select(c => c.GetMeta("titulo").AsString())];
			Checa("plantado o painel, a aba se refaz SOZINHA: um cartao por aluno, na ordem em que o painel os traz",
				  ordem.SequenceEqual(["Retratada", "Ausente", "Novato", "Antigo"]), string.Join(",", ordem));
			string? faixa = menu.ValorDesenhado(alunos, "Alunos");
			Checa($"a FAIXA 'Alunos' diz '4 de {Discipulado.MaxAlunos}'", faixa != null && faixa.StartsWith($"4 de {Discipulado.MaxAlunos}", StringComparison.Ordinal), faixa ?? "(nula)");

			foreach (AlunoNoPainel a in deQuemGuia.Alunos)
			{
				RoboDeAlunos.CartaoLido lido = RoboDeAlunos.Cartao(pg, "aluno", a.Quem.Nome) is { } c ? RoboDeAlunos.Ler(c) : RoboDeAlunos.SemCartao;
				List<string> ruim = RoboDeAlunos.ProblemasDoCartaoDeAluno(lido, a);
				Checa($"o cartao de '{a.Quem.Nome}' esta no contrato: a presenca, as datas, o progresso geral ({lido.Geral}), {a.Ensinamentos.Count} ensinamento(s) com pilula, linha e barra, e os dois gestos",
					  ruim.Count == 0, string.Join("; ", ruim));
			}
			RoboDeAlunos.CartaoLido daRetratada = RoboDeAlunos.Cartao(pg, "aluno", "Retratada") is { } cr ? RoboDeAlunos.Ler(cr) : RoboDeAlunos.SemCartao;
			RoboDeAlunos.CartaoLido doAusente = RoboDeAlunos.Cartao(pg, "aluno", "Ausente") is { } ca ? RoboDeAlunos.Ler(ca) : RoboDeAlunos.SemCartao;
			RoboDeAlunos.CartaoLido doNovato = RoboDeAlunos.Cartao(pg, "aluno", "Novato") is { } cn ? RoboDeAlunos.Ler(cn) : RoboDeAlunos.SemCartao;
			Checa("a Retratada: a forma 'DESPERTOU COM VOCÊ' com maestria 40% e 'você tentou 3 vezes'; a licao no nivel '1 de 3'; o retrato e o boneco que eu vi",
				  daRetratada.Ensinamentos is [{ } forma, { } nivel] && forma.Pilulas.SequenceEqual(["FORMA", "DESPERTOU COM VOCÊ"])
				  && forma.Linhas is [{ Rotulo: "maestria", Valor: "40%" }] && forma.Quando.StartsWith("você tentou 3 vezes", StringComparison.Ordinal)
				  && nivel.Linhas is [{ Rotulo: "nível", Valor: "1 de 3" }] && daRetratada.TemBoneco && !daRetratada.SemFoto,
				  string.Join(" / ", daRetratada.Ensinamentos.Select(e => $"{string.Join("|", e.Pilulas)} {string.Join(";", e.Linhas.Select(l => l.Valor))} '{e.Quando}'")));
			Checa("o Ausente: OFFLINE, 'visto por último há 2 h', a licao 'DOMINOU' (3 de 3), 'sem foto', e o 'Ajudar a transformar' APAGADO",
				  doAusente.PilulasDoTopo.SequenceEqual(["OFFLINE"]) && doAusente.Datas.Contains("visto por último há 2 h") && doAusente.SemFoto
				  && doAusente.Ensinamentos is [{ } dominou] && dominou.Pilulas.SequenceEqual(["LIÇÃO", "DOMINOU"]) && dominou.Linhas is [{ Valor: "3 de 3" }]
				  && doAusente.Botoes.Any(x => x is { Acao: "ensinar", Desligado: true }), doAusente.Datas);
			RoboDeAlunos.CartaoLido doAntigo = RoboDeAlunos.Cartao(pg, "aluno", "Antigo") is { } cant ? RoboDeAlunos.Ler(cant) : RoboDeAlunos.SemCartao;
			Checa("o Novato (aluno ha uma hora, nada ensinado ainda): a frase diz isso e o que fazer, e nao ha barra de progresso geral",
				  doNovato.Avisos.Contains("sem-ensinamentos") && doNovato.Geral.Length == 0 && doNovato.Ensinamentos.Count == 0
				  && doNovato.Datas.Contains("seu aluno há") && doNovato.Textos.Any(t => t.StartsWith("Você ainda não ensinou nada a Novato.", StringComparison.Ordinal)),
				  doNovato.Datas);
			Checa("o Antigo (vinculo de ANTES do diario, sem data): as datas nao inventam um 'há', e a frase NAO afirma que nada foi ensinado -- diz que o diario comeca agora",
				  doAntigo.Avisos.Contains("sem-ensinamentos") && doAntigo.Datas.Contains("seu aluno") && !doAntigo.Datas.Contains("há")
				  && doAntigo.Textos.Any(t => t.StartsWith("O diário deste vínculo começa agora", StringComparison.Ordinal))
				  && !doAntigo.Textos.Any(t => t.Contains("ainda não ensinou nada")),
				  doAntigo.Datas);
			// (o motivo de quem esta no mundo depende do que a bancada carrega AGORA: sem a skill Sense, ou com o scouter ligado)
			bool temSense = cli.Atributos.Tem(Protocol.Poder.Sense), comScouter = cli.Atributos.Tem(Protocol.Poder.Scouter) || cli.SentidosSaoDoScouter;
			string semLeitura = MenuJogo.CasoSemLeitura(true, temSense, comScouter);
			Checa($"a FORCA sem leitura do sentido de Ki (os plantados nao estao em lista nenhuma): quem esta no mundo fica no caso '{semLeitura}', "
				  + "quem esta fora no 'offline' -- e ninguem tem a linha 'poder relativo'",
				  daRetratada.Forca == semLeitura && doNovato.Forca == semLeitura && doAusente.Forca == "offline"
				  && daRetratada.PoderRelativo.Length == 0 && menu.ValorDesenhado(alunos, MenuJogo.RotuloDoPoderRelativo) == null,
				  $"{daRetratada.Forca},{doAusente.Forca},{doNovato.Forca}");
			List<string> noAlto = RoboDeAlunos.ForcaNoAltoDaPagina(pg);
			List<string> ruimNoAlto = RoboDeAlunos.ProblemasDaForcaNoAlto(noAlto, temSense, comScouter);
			Checa("...e o motivo que e do JOGADOR (falta a skill Sense, ou o scouter esta ligado) esta escrito UMA vez, no alto da pagina -- nao em cada cartao",
				  ruimNoAlto.Count == 0 && (noAlto.Count == 1 || (temSense && !comScouter)), string.Join("; ", ruimNoAlto) + $" [{string.Join(",", noAlto)}]");

			List<(string Chave, List<RoboDeAlunos.LinhaLida> Linhas)> conjunto = RoboDeAlunos.LerConjunto(pg);
			List<string> ruimDoConjunto = RoboDeAlunos.ProblemasDoConjunto(conjunto, deQuemGuia.Alunos);
			Checa("depois dos cartoes, 'Quem está mais adiantado': um cartao por ensinamento, com quem o recebeu do primeiro ao ultimo (na licao: 1º Ausente, 2º Retratada)",
				  ruimDoConjunto.Count == 0 && conjunto.Count == 2
				  && conjunto[1].Linhas.Select(l => l.Rotulo).SequenceEqual(["1º  Ausente", "2º  Retratada"])
				  && Rotulo(pg, MenuJogo.TituloDoConjunto.ToUpperInvariant()) != null,
				  string.Join("; ", ruimDoConjunto) + " | " + string.Join(" / ", conjunto.Select(c => string.Join(",", c.Linhas.Select(l => l.Rotulo)))));
			List<string> poder = RoboDeAlunos.PoderEscrito(RoboDeAlunos.TextosDe(pg));
			Checa("NENHUM numero de poder na pagina (nem 'Battle Power', nem 'BP', nem numero de cinco digitos ou mais) -- e nenhuma linha 'Battle Power'",
				  poder.Count == 0 && menu.ValorDesenhado(alunos, "Battle Power") == null, string.Join("; ", poder));

			if (Ancestral<ScrollContainer>(pg) is { } rolagem) rolagem.ScrollVertical = 0;
			await Quadros(2);
			Image? f1 = await Foto();
			await Guardar("alunos-01-quem-guia", f1);
			ChapaDoCartaoNaPaleta("a chapa do cartao de um aluno e a clara do tema (Tema.PainelClaro)", f1, RoboDeAlunos.Cartao(pg, "aluno", "Retratada"), Tema.PainelClaro);

			(bool igual, int nodes) = await RemontaIgual(menu, pg);
			Checa("DETERMINISMO: duas montagens do mesmo painel dao a MESMA arvore de nodes", igual, $"{nodes} nodes");

			// ---------------- 3. OS GESTOS DO CARTAO ----------------
			mandados.Clear();
			Button? ajudar = RoboDeAlunos.BotaoDe(RoboDeAlunos.Cartao(pg, "aluno", "Retratada"), "ensinar");
			if (ajudar != null) await Clicar(ajudar);
			Checa("'Ajudar a transformar' no cartao da Retratada manda `alunos_ensinar` com a assinatura DELA",
				  Gestos().SequenceEqual(["alunos_ensinar:9100000001"]), string.Join(",", Gestos()));

			mandados.Clear();
			Button? dispensar = RoboDeAlunos.BotaoDe(RoboDeAlunos.Cartao(pg, "aluno", "Ausente"), "dispensar");
			if (dispensar != null) await Clicar(dispensar);
			RoboDeAlunos.CartaoLido armado = RoboDeAlunos.Cartao(pg, "aluno", "Ausente") is { } c2 ? RoboDeAlunos.Ler(c2) : RoboDeAlunos.SemCartao;
			Checa("o primeiro clique em 'Dispensar aluno' so ARMA: o botao vira 'Confirmar: dispensar Ausente', nenhum verb sai, e o cartao continua no contrato",
				  armado.Botoes.Any(x => x is { Acao: "dispensar", Armado: true, Texto: "Confirmar: dispensar Ausente" }) && Gestos().Count == 0
				  && RoboDeAlunos.ProblemasDoCartaoDeAluno(armado, ausente).Count == 0, string.Join(",", armado.Botoes.Select(x => x.Texto)));
			Checa("...e arma SO aquele (o da Retratada continua 'Dispensar aluno')",
				  RoboDeAlunos.Cartao(pg, "aluno", "Retratada") is { } c3 && RoboDeAlunos.Ler(c3).Botoes.Any(x => x is { Acao: "dispensar", Armado: false, Texto: "Dispensar aluno" }));
			Button? confirmar = RoboDeAlunos.BotaoDe(RoboDeAlunos.Cartao(pg, "aluno", "Ausente"), "dispensar");
			if (confirmar != null) await Clicar(confirmar);
			Checa("o segundo clique manda `alunos_dispensar` com a assinatura do Ausente -- vale pra quem esta fora do mundo -- e o botao desarma",
				  Gestos().SequenceEqual(["alunos_dispensar:9100000002"])
				  && RoboDeAlunos.Cartao(pg, "aluno", "Ausente") is { } c4 && RoboDeAlunos.Ler(c4).Botoes.Any(x => x is { Acao: "dispensar", Armado: false }),
				  string.Join(",", Gestos()));

			// ---------------- 4. QUEM E GUIADO: A ABA "MESTRE" ----------------
			PosseDoCliente.DeTeste = new PosseComVinculo(real, Fatos.TemMestre);
			cli.MestreEAlunosDeTeste(deQuemEGuiado);
			menu.ForcarRedesenho();
			await Quadros(3);
			abas = menu.AbasDeTeste;
			people = Array.IndexOf(abas, "People");
			Checa("trocado o papel (so 'tem mestre'), a aba MUDA DE NOME pra 'Mestre' no mesmo lugar -- e quem estava nela continua nela",
				  people >= 0 && people + 1 < abas.Length && abas[people + 1] == mestre && !abas.Contains(alunos) && menu.AbaDeTeste == mestre,
				  $"{string.Join(",", abas)} / {menu.AbaDeTeste}");
			pg = menu.PaginaDeTeste(mestre);
			if (pg == null) { Checa("a pagina de 'Mestre' esta montada", false); return; }
			faixa = menu.ValorDesenhado(mestre, "Mestre");
			Checa("a FAIXA 'Mestre' diz quem ele e", faixa != null && faixa.StartsWith("Bardock", StringComparison.Ordinal), faixa ?? "(nula)");

			RoboDeAlunos.CartaoLido doMestre = RoboDeAlunos.Cartao(pg, "mestre", "Bardock") is { } cm ? RoboDeAlunos.Ler(cm) : RoboDeAlunos.SemCartao;
			List<string> ruimDoMestre = RoboDeAlunos.ProblemasDoCartaoDoMestre(doMestre, oMestre);
			Checa("o cartao do mestre esta no contrato: o que ele me ensinou, o meu progresso, a posicao entre os colegas e o botao 'Deixar meu mestre'",
				  ruimDoMestre.Count == 0, string.Join("; ", ruimDoMestre));
			Checa("...na forma eu sou o '2º de 3' (ha um colega na frente e um atras); na licao que SO EU recebi nao ha posicao",
				  doMestre.Ensinamentos is [{ Lugar: "2/3" } f, { Lugar: "" }] && f.Pilulas.SequenceEqual(["FORMA", "DESPERTOU COM O MESTRE", "2º de 3"]),
				  string.Join(" / ", doMestre.Ensinamentos.Select(e => $"'{e.Lugar}' {string.Join("|", e.Pilulas)}")));

			List<string> colegas = [.. RoboDeAlunos.Cartoes(pg, "colega").Select(c => c.GetMeta("titulo").AsString())];
			Checa("um cartao por colega, na ordem do painel (Raditz, Vegeta, Nappa), e nenhum cartao de aluno nem ranking de conjunto",
				  colegas.SequenceEqual(oMestre.Colegas) && !RoboDeAlunos.Cartoes(pg, "aluno").Any() && !RoboDeAlunos.Cartoes(pg, "ranking").Any(),
				  string.Join(",", colegas));
			var ruimDosColegas = new List<string>();
			for (int i = 0; i < oMestre.Colegas.Count; i++)
				ruimDosColegas.AddRange(RoboDeAlunos.ProblemasDoCartaoDeColega(
					RoboDeAlunos.Cartao(pg, "colega", oMestre.Colegas[i]) is { } cc ? RoboDeAlunos.Ler(cc) : RoboDeAlunos.SemCartao, oMestre, i));
			Checa("do colega so o NOME e o progresso nos ensinamentos em comum, com as duas barras (a dele e a minha): nem presenca, nem raca, nem forca, nem botao",
				  ruimDosColegas.Count == 0, string.Join("; ", ruimDosColegas));
			RoboDeAlunos.CartaoLido doVegeta = RoboDeAlunos.Cartao(pg, "colega", "Vegeta") is { } cv ? RoboDeAlunos.Ler(cv) : RoboDeAlunos.SemCartao;
			RoboDeAlunos.CartaoLido doNappa = RoboDeAlunos.Cartao(pg, "colega", "Nappa") is { } cnp ? RoboDeAlunos.Ler(cnp) : RoboDeAlunos.SemCartao;
			Checa("o Vegeta: 'maestria 62%' ao lado do meu 'maestria 30%'; o Nappa, sem nada em comum comigo, diz isso e nao mostra barra nenhuma",
				  doVegeta.Ensinamentos is [{ Linhas: [{ Rotulo: "Vegeta", Valor: "maestria 62%" }, { Rotulo: "você", Valor: "maestria 30%" }] }]
				  && doNappa.Ensinamentos.Count == 0 && doNappa.Avisos.Contains("sem-comum"),
				  string.Join(" / ", doVegeta.Ensinamentos.Select(e => string.Join(";", e.Linhas.Select(l => $"{l.Rotulo}={l.Valor}")))));
			poder = RoboDeAlunos.PoderEscrito(RoboDeAlunos.TextosDe(pg));
			Checa("NENHUM numero de poder na pagina do aluno", poder.Count == 0, string.Join("; ", poder));

			if (Ancestral<ScrollContainer>(pg) is { } rolagem2) rolagem2.ScrollVertical = 0;
			await Quadros(2);
			Image? f2 = await Foto();
			await Guardar("alunos-02-quem-e-guiado", f2);

			mandados.Clear();
			Button? largar = RoboDeAlunos.BotaoDe(RoboDeAlunos.Cartao(pg, "mestre", "Bardock"), "largar");
			if (largar != null) await Clicar(largar);
			RoboDeAlunos.CartaoLido armadoM = RoboDeAlunos.Cartao(pg, "mestre", "Bardock") is { } cm2 ? RoboDeAlunos.Ler(cm2) : RoboDeAlunos.SemCartao;
			Checa("o primeiro clique em 'Deixar meu mestre' so ARMA ('Confirmar: deixar Bardock'), e nenhum verb `alunos*` sai (o segundo clique e o `mst_largar` de sempre: medido na `--diagalunos`)",
				  armadoM.Botoes.SequenceEqual([new RoboDeAlunos.BotaoLido("largar", "Confirmar: deixar Bardock", false, true)]) && Gestos().Count == 0,
				  string.Join(",", armadoM.Botoes.Select(x => x.Texto)));

			// ---------------- 5. AS INJECOES (sobre os cartoes LIDOS da tela) ----------------
			Injeta("o contrato do cartao de aluno reprova a linha 'Battle Power' de volta no cartao (a aba do original dava a razao de poder, e o numero com scouter)",
				   RoboDeAlunos.ProblemasDoCartaoDeAluno(daRetratada with { Textos = [.. daRetratada.Textos, "Battle Power", "2.400.000"] }, retratada).Count > 0);
			Injeta("...reprova a leitura de forca no cartao de quem esta FORA do mundo",
				   RoboDeAlunos.ProblemasDoCartaoDeAluno(doAusente with { Forca = "sentida", PoderRelativo = "80%" }, ausente).Count > 0);
			Injeta("...reprova o cartao que diz 40% quando o servidor guarda 62%",
				   RoboDeAlunos.ProblemasDoCartaoDeAluno(daRetratada, retratada with
				   {
					   Ensinamentos = [retratada.Ensinamentos[0] with { Progresso = ProgressoDeAluno.DaForma(true, 62) }, retratada.Ensinamentos[1]],
				   }).Count > 0);
			Injeta("...reprova o 'Ajudar a transformar' ACESO no cartao de quem esta fora do mundo (o mesmo cartao da Retratada, lido contra um aluno ausente)",
				   RoboDeAlunos.ProblemasDoCartaoDeAluno(daRetratada, retratada with { Quem = retratada.Quem with { Online = false } }).Count > 0);
			Injeta("o contrato do cartao do mestre reprova a posicao que nao bate com os colegas (o cartao diz '2º de 3'; com os dois colegas atras de mim seria '1º de 3')",
				   RoboDeAlunos.ProblemasDoCartaoDoMestre(doMestre, oMestre with
				   {
					   Ensinamentos = [new EnsinamentoDoMestre(minhaForma, [new ColegaNoEnsinamento(0, ProgressoDeAluno.DaForma(false, 0)), new ColegaNoEnsinamento(1, ProgressoDeAluno.DaForma(false, 0))]),
									   oMestre.Ensinamentos[1]],
				   }).Count > 0);
			Injeta("o contrato do cartao de colega reprova a FORCA nele, a pilula de presenca e um ensinamento que nao e em comum",
				   RoboDeAlunos.ProblemasDoCartaoDeColega(doVegeta with { Forca = "sentida", PoderRelativo = "150%" }, oMestre, 1).Count > 0
				   && RoboDeAlunos.ProblemasDoCartaoDeColega(doVegeta with { PilulasDoTopo = ["ONLINE"] }, oMestre, 1).Count > 0
				   && RoboDeAlunos.ProblemasDoCartaoDeColega(doVegeta, oMestre, 0).Count > 0);
			Injeta("o contrato do conjunto reprova o ranking de um aluno so, e a ordem trocada",
				   RoboDeAlunos.ProblemasDoConjunto(conjunto, [retratada]).Count > 0
				   && RoboDeAlunos.ProblemasDoConjunto([conjunto[0], (conjunto[1].Chave, [.. conjunto[1].Linhas.AsEnumerable().Reverse()])], deQuemGuia.Alunos).Count > 0);

			// ---------------- 6. O VINCULO ACABA, E A ABA VAI EMBORA ----------------
			PosseDoCliente.DeTeste = null;
			cli.MestreEAlunosDeTeste(null);
			menu.ForcarRedesenho();
			await Quadros(3);
			Checa("sem os fatos a aba SAI da barra e o menu cai numa aba que existe (a aba aberta DEIXOU de existir)",
				  !menu.AbasDeTeste.Contains(alunos) && !menu.AbasDeTeste.Contains(mestre) && menu.AbasDeTeste.Contains(menu.AbaDeTeste),
				  $"{string.Join(",", menu.AbasDeTeste)} / {menu.AbaDeTeste}");
		}
		finally
		{
			PosseDoCliente.DeTeste = null;
			GameClient.EspiaoDeVerbos = espiaoDeAntes;
			cli.MestreEAlunosDeTeste(null);
			VistosDeGente.EsquecerDeTeste();
			menu.ForcarRedesenho();
			await Quadros(2);
		}

		ProvaNaoMedida("Alunos/Mestre: o que vem do SERVIDOR",
					   "a bancada nao tem mestre nem aluno de verdade: quem recebe o painel, a recusa do pacote forjado, a leitura do sentido de Ki no cartao e os gestos no servidor sao da `--diagalunos` e da `--alunosteste`");
	}
}
