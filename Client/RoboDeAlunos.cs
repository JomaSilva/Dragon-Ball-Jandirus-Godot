using System.Collections;
using System.Text;
using System.Text.RegularExpressions;
using Godot;
using Jandirus.Core.Appearance;
using Jandirus.Core.Items;
using Jandirus.Core.Skills;
using Jandirus.Core.Verbos;
using Jandirus.Net;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DA ABA "ALUNOS" / "MESTRE" (`--diagalunos`) -- a tela do discipulado andada inteira e FOTOGRAFADA.
///
/// ============================ O PEDIDO DO DONO (2026-10-11) ============================
/// *"ter tipo uma aba people so q de alunos (caso vc seja o mestre) e mestre (caso vc seja aluno) ao qual vc pode
/// ver seus alunos e o quao fortes eles estao progredindo no q vc ensinou. e o aluno pode ver o mestre e os outros
/// alunos pra ver o quao bem ele ta progredindo em relacao aos outros alunos"* -- e, sobre a forca: *"o mestre nao
/// pode ver o bp dos alunos ... oq vc sabe e se alguem e mais forte ou mais fraco q vc em porcentagem"*.
///
/// O QUE SE MEDE (o contrato da tela), nas duas pontas do mesmo processo:
///
///   * sem vinculo a aba NAO existe, e o pedido forjado do painel nao rende pacote nenhum;
///   * quem guia alguem ganha a aba "Alunos" sem reabrir o menu: um cartao por aluno, do mais adiantado pro menos,
///     e cada cartao diz EXATAMENTE o que o servidor guarda -- a forma (despertou? maestria), a licao (nivel), o
///     progresso geral, ha quanto tempo; e, depois dos cartoes, quem esta mais adiantado em cada ensinamento;
///   * quem e guiado ganha a aba "Mestre": o cartao do mestre com a posicao entre os colegas ("2º de 3") em cada
///     ensinamento, e um cartao por colega com as duas barras lado a lado -- e NADA mais do colega;
///   * A FORCA: nenhum numero de poder em lugar nenhum, com scouter ou sem. A unica leitura e a linha "poder
///     relativo" da aba Sense, igual a dela, e so quando ela a mostraria (a skill, sem scouter, a pessoa ao alcance);
///   * os gestos dos cartoes (ajudar a transformar, dispensar, deixar o mestre) sao os verbs de sempre, apontados pra
///     AQUELA pessoa, e quem valida e o servidor -- inclusive o pacote forjado;
///   * a tela aberta acompanha: o progresso que muda no servidor chega sozinho; num mundo parado nao chega nada.
///
/// TUDO PELO CAMINHO DE PRODUCAO: os fatos chegam pelo `S2C.Fatos`, o painel pelo `S2C.MestreEAlunos`, a leitura de
/// forca pelo `S2C.Sentidos`, os cliques sao o sinal do botao. So o ELENCO e plantado (no servidor,
/// `PlantarAlunosDeTeste` / `PlantarMestreDeTeste`): a bancada nasce sozinha, e um vinculo de verdade pede dois
/// jogadores, tres vezes de diferenca de poder e meses de treino.
/// =========================================================================================
///
/// COMO RODAR -- um processo so (modo host), com JANELA no segundo monitor (no headless o `GetImage` volta vazio e as
/// fotos sao puladas):
///
///     --host --rede 8301 --horateste 0.5 --bpteste 3000000 --diagalunos --raca Saiyan
///     --conta bancada_alunos_ag --nome Goku --pasta &lt;pasta das fotos&gt;
///
/// As fotos saem em `--pasta` (ou em `user://`): `alunos-1-mestre`, `-1b-quem-esta-mais-adiantado`, `-2-com-sense`
/// (e o recorte de um cartao, `-2-cartao`), `-3-menu-pequeno`, `-4-menu-grande`, `-4b-dispensar-armado` (e
/// `-4b-cartao-armado`), `-5-aluno` (e `-5-cartao-do-mestre`), `-5b-colegas` (e `-5b-cartao-de-colega`),
/// `-6-aluno-pequeno`, `-7-aluno-grande`, `-8-as-duas-partes`, `-8b-as-duas-partes-fim` e `-0-historia` (as telas
/// lado a lado).
/// </summary>
public partial class RoboDeAlunos : Node
{
	// =====================================================================
	// O QUE SE LE DE UM CARTAO -- puro, pra bancada das abas (`--diagabas` F17) usar o mesmo
	// =====================================================================

	/// <summary>Uma linha rotulo/valor com barra, como esta na tela (`Barra` = a razao cheia, -1 sem barra).</summary>
	internal readonly record struct LinhaLida(string Rotulo, string Valor, double Barra);

	/// <summary>Um ensinamento num cartao: o nome, as pilulas, as linhas de progresso, a posicao (`2/3`) e a linha de quando.</summary>
	internal sealed record EnsinamentoLido(string Chave, string Nome, List<string> Pilulas, List<LinhaLida> Linhas, string Lugar, string Quando);

	/// <summary>Um botao de gesto de um cartao.</summary>
	internal readonly record struct BotaoLido(string Acao, string Texto, bool Desligado, bool Armado);

	/// <summary>
	/// Um cartao da aba, como esta na tela. <c>Tipo</c>: "aluno", "mestre" ou "colega". <c>Forca</c> e o caso da leitura
	/// de forca ("" = o cartao nao fala de forca) e <c>PoderRelativo</c> o texto da linha (so no caso "sentida").
	/// <c>Avisos</c> sao os metadados `discipulado` soltos no cartao ("datas", "sem-ensinamentos", "sem-comum"...).
	/// </summary>
	internal sealed record CartaoLido(string Tipo, string Nome, List<string> PilulasDoTopo, string Datas, string Forca, string PoderRelativo,
									  string Geral, double BarraDoGeral, List<EnsinamentoLido> Ensinamentos, List<BotaoLido> Botoes,
									  bool TemBoneco, bool SemFoto, List<string> Avisos, List<string> Textos);

	/// <summary>Os dois rotulos do progresso geral (o do cartao de aluno e o do cartao do mestre).</summary>
	internal const string GeralDoAluno = "Progresso no que você ensinou", GeralDoMestre = "Seu progresso no que ele ensinou";

	internal static IEnumerable<Node> Todos(Node raiz)
	{
		yield return raiz;
		foreach (Node f in raiz.GetChildren())
			foreach (Node n in Todos(f)) yield return n;
	}

	/// <summary>Todo texto que um pedaco da pagina mostra: rotulos e botoes visiveis.</summary>
	internal static List<string> TextosDe(Node raiz) =>
	[
		.. Todos(raiz).OfType<Label>().Where(l => l.IsVisibleInTree()).Select(l => l.Text),
		.. Todos(raiz).OfType<Button>().Where(b => b.IsVisibleInTree()).Select(b => b.Text),
	];

	private static LinhaLida LerLinha(HBoxContainer h)
	{
		string valor = h.GetChildCount() >= 2 && h.GetChild(1) is Label v ? v.Text : "";
		ProgressBar? barra = h.GetParent()?.GetChildren().OfType<ProgressBar>().FirstOrDefault();
		return new LinhaLida(h.GetMeta("linha").AsString(), valor, barra != null && barra.MaxValue > 0 ? barra.Value / barra.MaxValue : -1);
	}

	private static List<LinhaLida> LinhasDe(Node raiz) =>
		[.. Todos(raiz).OfType<HBoxContainer>().Where(h => h.HasMeta("linha")).Select(LerLinha)];

	/// <summary>Este node mora dentro de um bloco de ensinamento do cartao? (As pilulas e as linhas dele nao sao as do topo.)</summary>
	private static bool DentroDeEnsinamento(Node n, Node cartao)
	{
		for (Node? p = n.GetParent(); p != null && p != cartao; p = p.GetParent())
			if (p.HasMeta("ensinamento")) return true;
		return false;
	}

	private static EnsinamentoLido LerEnsinamento(Node bloco)
	{
		string quando = Todos(bloco).OfType<Label>()
			.FirstOrDefault(l => l.HasMeta("discipulado") && l.GetMeta("discipulado").AsString() == "quando")?.Text ?? "";
		return new EnsinamentoLido(
			bloco.GetMeta("ensinamento").AsString(),
			Todos(bloco).OfType<Label>().FirstOrDefault()?.Text ?? "",
			[.. Todos(bloco).OfType<PanelContainer>().Where(p => p.HasMeta("pilula")).Select(p => p.GetMeta("pilula").AsString())],
			LinhasDe(bloco),
			bloco.HasMeta("lugar") ? bloco.GetMeta("lugar").AsString() : "",
			quando);
	}

	/// <summary>
	/// Le um cartao da aba pelos metadados que ela escreve (`cartao`, `titulo`, `pilula`, `linha`, `barra`, `forca`,
	/// `discipulado`, `ensinamento`, `lugar`, `acao`, `armado`).
	/// </summary>
	internal static CartaoLido Ler(Control cartao)
	{
		var pilulas = new List<string>();
		var ensinamentos = new List<EnsinamentoLido>();
		var botoes = new List<BotaoLido>();
		var avisos = new List<string>();
		string datas = "", forca = "", poder = "", geral = "";
		double barraDoGeral = -1;
		bool boneco = false, semFoto = false;

		foreach (Node n in Todos(cartao))
		{
			bool solto = !DentroDeEnsinamento(n, cartao);
			switch (n)
			{
				case VBoxContainer v when v.HasMeta("ensinamento"): ensinamentos.Add(LerEnsinamento(v)); break;
				case VBoxContainer v when v.HasMeta("forca"):
					forca = v.GetMeta("forca").AsString();
					poder = LinhasDe(v).FirstOrDefault(l => l.Rotulo == MenuJogo.RotuloDoPoderRelativo).Valor ?? "";
					break;
				case PanelContainer p when p.HasMeta("pilula") && solto: pilulas.Add(p.GetMeta("pilula").AsString()); break;
				case Button b when b.HasMeta("acao"):
					botoes.Add(new BotaoLido(b.GetMeta("acao").AsString(), b.Text, b.Disabled, b.HasMeta("armado") && b.GetMeta("armado").AsBool()));
					break;
				case CharacterVisual: boneco = true; break;
				case HBoxContainer h when solto && h.HasMeta("linha") && h.GetMeta("linha").AsString() is GeralDoAluno or GeralDoMestre:
				{
					LinhaLida l = LerLinha(h);
					geral = l.Valor;
					barraDoGeral = l.Barra;
					break;
				}
				case Label l:
					if (l.Name.ToString() == "SemFoto") semFoto = true;
					if (l.HasMeta("forca")) forca = l.GetMeta("forca").AsString();
					if (solto && l.HasMeta("discipulado"))
					{
						string d = l.GetMeta("discipulado").AsString();
						avisos.Add(d);
						if (d == "datas") datas = l.Text;
					}
					break;
			}
		}
		return new CartaoLido(cartao.HasMeta("cartao") ? cartao.GetMeta("cartao").AsString() : "",
							  cartao.HasMeta("titulo") ? cartao.GetMeta("titulo").AsString() : "",
							  pilulas, datas, forca, poder, geral, barraDoGeral, ensinamentos, botoes, boneco, semFoto, avisos, TextosDe(cartao));
	}

	/// <summary>O que se le de um cartao que nao esta na tela: nada bate, e a checagem que o usar reprova dizendo isso.</summary>
	internal static readonly CartaoLido SemCartao = new("(sem cartao)", "", [], "", "", "", "", -1, [], [], false, false, [], []);

	// =====================================================================
	// O CONTRATO -- o que o cartao TEM que dizer, escrito aqui e nao lido da pagina
	// =====================================================================

	/// <summary>
	/// O QUE UM PROGRESSO VIRA NA TELA: (a pilula de estado, o rotulo e o valor da linha, a razao da barra). Escrito
	/// AQUI -- e nao chamando as funcoes da pagina -- porque a regra de uma bancada e dizer o contrato com as proprias
	/// palavras: se a pagina mudar o que escreve, e esta tabela que reprova.
	/// </summary>
	internal static (string Pilula, string Rotulo, string Valor, double Razao) Esperado(EnsinamentoNoPainel e, string comQuem)
	{
		ProgressoNoEnsinamento p = e.Progresso;
		double razao = p.Etapa switch { EtapaDoEnsinamento.AindaNao => 0, EtapaDoEnsinamento.Dominado => 1, _ => p.Milesimos / 1000.0 };
		if (e.Tipo == TipoDeEnsinamento.Forma)
			return p.Etapa switch
			{
				EtapaDoEnsinamento.AindaNao => ("AINDA NÃO DESPERTOU", "maestria", "ainda não despertou", razao),
				EtapaDoEnsinamento.Dominado => ("DOMINADA", "maestria", "100%", razao),
				_ => (e.Assistida ? "DESPERTOU " + comQuem : "DESPERTOU", "maestria", $"{p.Milesimos / 10.0:0.#}%", razao),
			};
		if (p.Etapa == EtapaDoEnsinamento.AindaNao) return ("ESQUECEU", "nível", "esqueceu a lição", razao);
		if (p.Teto == 0) return ("APRENDEU", "lição", "aprendida (não tem níveis)", razao);
		return (p.Etapa == EtapaDoEnsinamento.Dominado ? "DOMINOU" : "APRENDEU", "nível", $"{p.Nivel} de {p.Teto}", razao);
	}

	/// <summary>O mesmo progresso numa frase so, como as linhas "de alguem" o escrevem (o ranking, o par de barras do colega).</summary>
	internal static string EsperadoEmUmaFrase(TipoDeEnsinamento tipo, ProgressoNoEnsinamento p)
	{
		if (tipo == TipoDeEnsinamento.Forma)
			return p.Etapa switch
			{
				EtapaDoEnsinamento.AindaNao => "ainda não despertou",
				EtapaDoEnsinamento.Dominado => "dominada",
				_ => $"maestria {p.Milesimos / 10.0:0.#}%",
			};
		if (p.Etapa == EtapaDoEnsinamento.AindaNao) return "esqueceu a lição";
		if (p.Teto == 0) return "aprendida";
		return p.Etapa == EtapaDoEnsinamento.Dominado ? $"nível {p.Teto} de {p.Teto}" : $"nível {p.Nivel} de {p.Teto}";
	}

	private static double RazaoDe(ProgressoNoEnsinamento p) =>
		p.Etapa switch { EtapaDoEnsinamento.AindaNao => 0, EtapaDoEnsinamento.Dominado => 1, _ => p.Milesimos / 1000.0 };

	/// <summary>
	/// A barra esta onde devia? A barra da casa (`MenuJogo.LinhaComBarra`) anda de 1 em 1% -- o passo de fabrica do
	/// `ProgressBar` --, entao 0,833 se desenha em 0,83: a folga e meio passo. (O NUMERO escrito ao lado e conferido
	/// inteiro, sem folga.)
	/// </summary>
	private static bool MesmaBarra(double lida, double esperada) => Math.Abs(lida - esperada) <= 0.0051;

	/// <summary>
	/// A REGRA DA FORCA NUM CARTAO (dono, 2026-10-11). Devolve o que esta errado:
	///   * cartao de COLEGA nao fala de forca -- nem leitura, nem frase;
	///   * cartao de aluno ou de mestre esta em UM dos cinco casos, e so o "sentida" tem a linha "poder relativo", que e
	///     uma porcentagem inteira e mais nada;
	///   * quem nao esta no mundo so pode estar no caso "offline" (nao ha como sentir quem nao esta la);
	///   * a FRASE de forca ("força: ...") so existe no cartao do caso "nao-sentida" -- o unico motivo que e DAQUELA
	///     pessoa. Os do jogador ("sem-sense", "scouter") vao uma vez no alto da pagina
	///     (<see cref="ProblemasDaForcaNoAlto"/>), e o "offline" ja esta na pilula.
	/// </summary>
	internal static List<string> ProblemasDeForca(CartaoLido c, bool online)
	{
		var ruim = new List<string>();
		bool temFrase = c.Textos.Any(t => t.StartsWith("força:", StringComparison.OrdinalIgnoreCase));
		if (c.Tipo == "colega")
		{
			if (c.Forca.Length > 0 || c.PoderRelativo.Length > 0 || temFrase) ruim.Add($"cartao de COLEGA falando de forca ('{c.Forca}' {c.PoderRelativo})");
			return ruim;
		}

		string[] casos = ["sentida", "offline", "scouter", "sem-sense", "nao-sentida"];
		if (!casos.Contains(c.Forca)) { ruim.Add($"caso de forca desconhecido: '{c.Forca}'"); return ruim; }
		if (temFrase != (c.Forca == "nao-sentida"))
			ruim.Add(temFrase ? $"frase de forca no cartao no caso '{c.Forca}' (so o motivo que e da PESSOA se escreve no cartao dela)"
							  : "cartao de quem o sentido de Ki nao le sem a frase que diz isso");
		if (c.Forca == "sentida")
		{
			if (!online) ruim.Add("poder relativo de quem NAO esta no mundo");
			if (!Regex.IsMatch(c.PoderRelativo, @"^\d+%$")) ruim.Add($"a leitura sentida nao e uma porcentagem inteira: '{c.PoderRelativo}'");
		}
		else
		{
			if (c.PoderRelativo.Length > 0) ruim.Add($"linha de poder relativo ({c.PoderRelativo}) num cartao sem leitura do sentido de Ki ('{c.Forca}')");
			if ((c.Forca == "offline") == online) ruim.Add($"caso '{c.Forca}' num cartao de quem {(online ? "ESTA" : "NAO esta")} no mundo");
		}
		return ruim;
	}

	/// <summary>As notas de forca do ALTO da pagina (o metadado `forca_da_pagina`), como estao na tela.</summary>
	internal static List<string> ForcaNoAltoDaPagina(Node pagina) =>
		[.. Todos(pagina).OfType<Label>().Where(l => l.IsVisibleInTree() && l.HasMeta("forca_da_pagina"))
			.Select(l => l.GetMeta("forca_da_pagina").AsString())];

	/// <summary>
	/// A REGRA DO ALTO DA PAGINA: o motivo de nao haver leitura que e do JOGADOR aparece UMA vez ("scouter" ganha de
	/// "sem-sense"), e quem sente Ki sem scouter nao tem nota nenhuma. Devolve o que esta errado.
	/// </summary>
	internal static List<string> ProblemasDaForcaNoAlto(IReadOnlyList<string> notas, bool temSense, bool scouterLigado)
	{
		string[] esperado = scouterLigado ? ["scouter"] : !temSense ? ["sem-sense"] : [];
		return notas.SequenceEqual(esperado)
			? []
			: [$"notas de forca no alto da pagina [{string.Join(",", notas)}], deviam ser [{string.Join(",", esperado)}]"];
	}

	/// <summary>
	/// TEXTO QUE CONTA PODER: devolve os que aparecem nestes textos -- o nome de um medidor ("Battle Power", "BP",
	/// "poder de luta") ou um NUMERO GRANDE (cinco digitos ou mais, ou milhares agrupados: "3.000.000"). A aba nao
	/// tem numero nenhum desse tamanho: dias, niveis e porcentagens cabem em tres digitos.
	/// </summary>
	internal static List<string> PoderEscrito(IEnumerable<string> textos)
	{
		var achados = new List<string>();
		foreach (string t in textos)
			if (Regex.IsMatch(t, @"\bBP\b|battle power|poder de luta|power level", RegexOptions.IgnoreCase)
				|| Regex.IsMatch(t, @"\d{1,3}(?:[.,  ]\d{3})+|\d{5,}"))
				achados.Add(t.Length > 60 ? t[..60] + "..." : t);
		return achados;
	}

	/// <summary>Os blocos de ensinamento de um cartao contra a verdade: as chaves na ordem, e de cada um o nome, as pilulas, a linha e a barra.</summary>
	private static void ProblemasDosEnsinamentos(List<string> ruim, IReadOnlyList<EnsinamentoLido> lidos, IReadOnlyList<EnsinamentoNoPainel> verdade,
												 string comQuem, Func<int, (int Posicao, int De, bool Empate)?> lugarDe)
	{
		List<string> chaves = [.. verdade.Select(e => MenuJogo.ChaveDoEnsinamento(e.Tipo, e.Id))];
		if (!lidos.Select(l => l.Chave).SequenceEqual(chaves))
		{
			ruim.Add($"os ensinamentos do cartao [{string.Join(",", lidos.Select(l => l.Chave))}] nao sao os do servidor, na ordem [{string.Join(",", chaves)}]");
			return;
		}
		for (int i = 0; i < verdade.Count; i++)
		{
			EnsinamentoNoPainel e = verdade[i];
			EnsinamentoLido l = lidos[i];
			(string pilula, string rotulo, string valor, double razao) = Esperado(e, comQuem);
			var pilulas = new List<string> { e.Tipo == TipoDeEnsinamento.Forma ? "FORMA" : "LIÇÃO", pilula };
			string lugar = "";
			if (lugarDe(i) is { } posto)
			{
				pilulas.Add($"{posto.Posicao}º de {posto.De}{(posto.Empate ? " · empate" : "")}");
				lugar = $"{posto.Posicao}/{posto.De}";
			}
			if (!l.Pilulas.SequenceEqual(pilulas)) ruim.Add($"{l.Chave}: pilulas [{string.Join("|", l.Pilulas)}], deviam ser [{string.Join("|", pilulas)}]");
			if (l.Lugar != lugar) ruim.Add($"{l.Chave}: posicao '{l.Lugar}', devia ser '{lugar}'");
			if (l.Linhas.Count != 1 || l.Linhas[0].Rotulo != rotulo || l.Linhas[0].Valor != valor || !MesmaBarra(l.Linhas[0].Barra, razao))
				ruim.Add($"{l.Chave}: linha [{string.Join(";", l.Linhas.Select(x => $"{x.Rotulo}={x.Valor} barra {x.Barra:0.000}"))}], devia ser {rotulo}={valor} barra {razao:0.000}");
			if (l.Nome.Length == 0 || l.Nome.StartsWith('/')) ruim.Add($"{l.Chave}: o nome escrito e '{l.Nome}' (a chave crua, ou nada)");
		}
	}

	/// <summary>O topo de um cartao de pessoa (aluno ou mestre) contra a verdade: a presenca, as datas, a forca, o progresso geral.</summary>
	private static void ProblemasDoTopo(List<string> ruim, CartaoLido c, PessoaNoPainel q, IReadOnlyList<ProgressoNoEnsinamento> progressos)
	{
		if (c.Nome != q.Nome) ruim.Add($"o cartao e de '{c.Nome}', o servidor diz '{q.Nome}'");
		string presenca = q.Online ? "ONLINE" : "OFFLINE";
		if (!c.PilulasDoTopo.SequenceEqual([presenca])) ruim.Add($"pilula(s) do topo [{string.Join("|", c.PilulasDoTopo)}], devia ser so '{presenca}'");
		if (c.Datas.Length == 0) ruim.Add("sem a linha de quem e a pessoa e desde quando");
		if (q.Online && c.Datas.Contains("visto por último")) ruim.Add("'visto por último' em quem ESTA no mundo");
		if (!q.Online && q.VistoHaSegundos > 0 && !c.Datas.Contains("visto por último")) ruim.Add("quem saiu do mundo sem o 'visto por último'");
		if (c.TemBoneco == c.SemFoto) ruim.Add("o retrato tem que ser o boneco OU 'sem foto' (nem os dois, nem nenhum)");
		ruim.AddRange(ProblemasDeForca(c, q.Online));

		if (progressos.Count == 0)
		{
			if (c.Geral.Length > 0) ruim.Add($"progresso geral '{c.Geral}' sem ensinamento nenhum");
			if (!c.Avisos.Contains("sem-ensinamentos")) ruim.Add("sem ensinamento nenhum e sem a frase que diz isso");
			return;
		}
		double media = ProgressoDeAluno.Geral(progressos).Media;
		if (c.Geral != $"{media * 100:0}%" || !MesmaBarra(c.BarraDoGeral, media))
			ruim.Add($"progresso geral '{c.Geral}' (barra {c.BarraDoGeral:0.000}), devia ser {media * 100:0}% ({media:0.000})");
	}

	/// <summary>
	/// O CONTRATO DO CARTAO DE UM ALUNO: devolve o que esta ERRADO nele (vazio = certo), dado o que o servidor guarda.
	/// Pura -- as injecoes forjam um `CartaoLido` torto e cobram que ela reclame.
	/// </summary>
	internal static List<string> ProblemasDoCartaoDeAluno(CartaoLido c, AlunoNoPainel a)
	{
		var ruim = new List<string>();
		if (c.Tipo != "aluno") ruim.Add($"o cartao e do tipo '{c.Tipo}', devia ser 'aluno'");
		ProblemasDoTopo(ruim, c, a.Quem, [.. a.Ensinamentos.Select(e => e.Progresso)]);
		ProblemasDosEnsinamentos(ruim, c.Ensinamentos, a.Ensinamentos, "COM VOCÊ", _ => null);

		// OS DOIS GESTOS, e so eles: ajudar (apagado pra quem nao esta no mundo) e dispensar (de dois cliques).
		BotaoLido ajudar = c.Botoes.FirstOrDefault(b => b.Acao == "ensinar"), dispensar = c.Botoes.FirstOrDefault(b => b.Acao == "dispensar");
		if (!c.Botoes.Select(b => b.Acao).SequenceEqual(["ensinar", "dispensar"]))
			ruim.Add($"os botoes do cartao sao [{string.Join(",", c.Botoes.Select(b => b.Acao))}], deviam ser ensinar e dispensar");
		else
		{
			if (ajudar.Texto != "Ajudar a transformar" || ajudar.Desligado == a.Quem.Online)
				ruim.Add($"'{ajudar.Texto}' {(ajudar.Desligado ? "apagado" : "aceso")} com o aluno {(a.Quem.Online ? "no mundo" : "fora dele")}");
			string texto = dispensar.Armado ? $"Confirmar: dispensar {a.Quem.Nome}" : "Dispensar aluno";
			if (dispensar.Texto != texto || dispensar.Desligado) ruim.Add($"o botao de dispensar diz '{dispensar.Texto}' (armado={dispensar.Armado})");
		}
		ruim.AddRange(PoderEscrito(c.Textos).Select(t => $"texto que conta poder: '{t}'"));
		return ruim;
	}

	/// <summary>O CONTRATO DO CARTAO DO MESTRE: o que ele me ensinou, o meu progresso, e a minha posicao entre os colegas em cada coisa.</summary>
	internal static List<string> ProblemasDoCartaoDoMestre(CartaoLido c, MestreNoPainel m)
	{
		var ruim = new List<string>();
		if (c.Tipo != "mestre") ruim.Add($"o cartao e do tipo '{c.Tipo}', devia ser 'mestre'");
		ProblemasDoTopo(ruim, c, m.Quem, [.. m.Ensinamentos.Select(e => e.Meu.Progresso)]);
		ProblemasDosEnsinamentos(ruim, c.Ensinamentos, [.. m.Ensinamentos.Select(e => e.Meu)], "COM O MESTRE",
								 i => m.Ensinamentos[i].Colegas.Count > 0 ? ProgressoDeAluno.Posicao(m.Ensinamentos[i].Meu.Progresso, m.Ensinamentos[i].Colegas) : null);

		if (!c.Botoes.Select(b => b.Acao).SequenceEqual(["largar"]))
			ruim.Add($"os botoes do cartao sao [{string.Join(",", c.Botoes.Select(b => b.Acao))}], devia ser so o de deixar o mestre");
		else
		{
			BotaoLido largar = c.Botoes[0];
			string texto = largar.Armado ? $"Confirmar: deixar {m.Quem.Nome}" : "Deixar meu mestre";
			if (largar.Texto != texto || largar.Desligado) ruim.Add($"o botao de deixar o mestre diz '{largar.Texto}' (armado={largar.Armado})");
		}
		ruim.AddRange(PoderEscrito(c.Textos).Select(t => $"texto que conta poder: '{t}'"));
		return ruim;
	}

	/// <summary>
	/// O CONTRATO DO CARTAO DE UM COLEGA: o NOME, e em cada ensinamento que NOS DOIS recebemos as duas barras (a dele e
	/// a minha). Mais nada dele: nem presenca, nem raca, nem datas, nem forca, nem botao, nem o que so ele recebeu.
	/// </summary>
	internal static List<string> ProblemasDoCartaoDeColega(CartaoLido c, MestreNoPainel m, int indice)
	{
		var ruim = new List<string>();
		string nome = indice >= 0 && indice < m.Colegas.Count ? m.Colegas[indice] : "(fora da lista)";
		if (c.Tipo != "colega") ruim.Add($"o cartao e do tipo '{c.Tipo}', devia ser 'colega'");
		if (c.Nome != nome) ruim.Add($"o cartao e de '{c.Nome}', o colega {indice} e '{nome}'");
		if (c.PilulasDoTopo.Count > 0) ruim.Add($"pilula(s) no topo do cartao de colega: [{string.Join("|", c.PilulasDoTopo)}] (a presenca dele nao e assunto meu)");
		if (c.Datas.Length > 0) ruim.Add($"linha de raca/datas no cartao de colega: '{c.Datas}'");
		if (c.Botoes.Count > 0) ruim.Add($"botao no cartao de colega: [{string.Join(",", c.Botoes.Select(b => b.Texto))}]");
		if (c.Geral.Length > 0) ruim.Add("linha de progresso geral no cartao de colega");
		if (c.TemBoneco == c.SemFoto) ruim.Add("o retrato tem que ser o boneco OU 'sem foto'");
		ruim.AddRange(ProblemasDeForca(c, online: false));

		var emComum = new List<(EnsinamentoNoPainel Meu, ProgressoNoEnsinamento Dele)>();
		foreach (EnsinamentoDoMestre e in m.Ensinamentos)
			foreach (ColegaNoEnsinamento x in e.Colegas)
				if (x.Colega == indice) { emComum.Add((e.Meu, x.Progresso)); break; }

		List<string> chaves = [.. emComum.Select(x => MenuJogo.ChaveDoEnsinamento(x.Meu.Tipo, x.Meu.Id))];
		if (!c.Ensinamentos.Select(l => l.Chave).SequenceEqual(chaves))
			ruim.Add($"os ensinamentos do cartao [{string.Join(",", c.Ensinamentos.Select(l => l.Chave))}] nao sao os EM COMUM [{string.Join(",", chaves)}]");
		else
			for (int i = 0; i < emComum.Count; i++)
			{
				(EnsinamentoNoPainel meu, ProgressoNoEnsinamento dele) = emComum[i];
				EnsinamentoLido l = c.Ensinamentos[i];
				bool certo = l.Linhas.Count == 2
					&& l.Linhas[0].Rotulo == nome && l.Linhas[0].Valor == EsperadoEmUmaFrase(meu.Tipo, dele) && MesmaBarra(l.Linhas[0].Barra, RazaoDe(dele))
					&& l.Linhas[1].Rotulo == "você" && l.Linhas[1].Valor == EsperadoEmUmaFrase(meu.Tipo, meu.Progresso) && MesmaBarra(l.Linhas[1].Barra, RazaoDe(meu.Progresso));
				if (!certo)
					ruim.Add($"{l.Chave}: as duas barras [{string.Join(";", l.Linhas.Select(x => $"{x.Rotulo}={x.Valor} barra {x.Barra:0.000}"))}], deviam ser "
							 + $"{nome}={EsperadoEmUmaFrase(meu.Tipo, dele)} e você={EsperadoEmUmaFrase(meu.Tipo, meu.Progresso)}");
			}
		if (emComum.Count == 0 && !c.Avisos.Contains("sem-comum")) ruim.Add("nenhum ensinamento em comum e sem a frase que diz isso");
		ruim.AddRange(PoderEscrito(c.Textos).Select(t => $"texto que conta poder: '{t}'"));
		return ruim;
	}

	/// <summary>Um cartao do conjunto ("quem esta mais adiantado"), como esta na tela: a chave do ensinamento e as linhas (rotulo "1º  Fulano", valor, barra).</summary>
	internal static List<(string Chave, List<LinhaLida> Linhas)> LerConjunto(Node pagina) =>
		[.. Cartoes(pagina, "ranking").Select(c => (c.HasMeta("ranking") ? c.GetMeta("ranking").AsString() : "", LinhasDe(c)))];

	/// <summary>O CONTRATO DO CONJUNTO: um cartao por ensinamento, e em cada um quem o recebeu do mais adiantado pro menos, com a posicao.</summary>
	internal static List<string> ProblemasDoConjunto(List<(string Chave, List<LinhaLida> Linhas)> lido, IReadOnlyList<AlunoNoPainel> alunos)
	{
		var ruim = new List<string>();
		List<RankingDoEnsinamento> conjunto = alunos.Count >= 2 ? ProgressoDeAluno.Conjunto(alunos) : [];
		List<string> chaves = [.. conjunto.Select(r => MenuJogo.ChaveDoEnsinamento(r.Tipo, r.Id))];
		if (!lido.Select(l => l.Chave).SequenceEqual(chaves))
		{
			ruim.Add($"os rankings na tela [{string.Join(",", lido.Select(l => l.Chave))}] nao sao os ensinamentos do painel [{string.Join(",", chaves)}]");
			return ruim;
		}
		for (int i = 0; i < conjunto.Count; i++)
		{
			RankingDoEnsinamento r = conjunto[i];
			List<string> esperadas = [.. r.Lugares.Select(l => $"{l.Posicao}º  {alunos[l.Aluno].Quem.Nome}={EsperadoEmUmaFrase(r.Tipo, l.Progresso)}")];
			List<string> lidas = [.. lido[i].Linhas.Select(l => $"{l.Rotulo}={l.Valor}")];
			if (!lidas.SequenceEqual(esperadas)) ruim.Add($"{lido[i].Chave}: [{string.Join(" | ", lidas)}], devia ser [{string.Join(" | ", esperadas)}]");
			for (int j = 0; j < Math.Min(r.Lugares.Count, lido[i].Linhas.Count); j++)
				if (!MesmaBarra(lido[i].Linhas[j].Barra, RazaoDe(r.Lugares[j].Progresso)))
					ruim.Add($"{lido[i].Chave}: a barra de '{lido[i].Linhas[j].Rotulo}' esta em {lido[i].Linhas[j].Barra:0.000}, devia estar em {RazaoDe(r.Lugares[j].Progresso):0.000}");
			// a posicao nunca cresce de baixo pra cima, e o progresso nunca sobe de cima pra baixo
			for (int j = 1; j < r.Lugares.Count; j++)
				if (r.Lugares[j].Posicao < r.Lugares[j - 1].Posicao || r.Lugares[j].Progresso.Pontos > r.Lugares[j - 1].Progresso.Pontos + 1e-9)
					ruim.Add($"{lido[i].Chave}: a ordem do ranking nao e do mais adiantado pro menos");
		}
		return ruim;
	}

	internal static IEnumerable<PanelContainer> Cartoes(Node pagina, string tipo) =>
		Todos(pagina).OfType<PanelContainer>()
			.Where(p => p.IsVisibleInTree() && p.HasMeta("cartao") && p.GetMeta("cartao").AsString() == tipo);

	internal static PanelContainer? Cartao(Node pagina, string tipo, string nome) =>
		Cartoes(pagina, tipo).FirstOrDefault(p => p.HasMeta("titulo") && p.GetMeta("titulo").AsString() == nome);

	internal static Button? BotaoDe(Node? cartao, string acao) =>
		cartao == null ? null : Todos(cartao).OfType<Button>().FirstOrDefault(b => b.HasMeta("acao") && b.GetMeta("acao").AsString() == acao);

	// =====================================================================
	// O ANDAIME
	// =====================================================================
	private int _ok, _falhou, _injOk, _injFalhou;
	private readonly List<string> _vermelhas = [];
	private readonly List<TiraDeFotos.Quadro> _historia = [];
	private readonly List<string> _fotos = [];
	private readonly List<string> _doServidor = [];
	/// <summary>Os verbs `alunos*` que a TELA mandou (`cmd:arg`). O espiao deixa passar: e so um ouvido.</summary>
	private readonly List<string> _verbos = [];
	/// <summary>Os paineis que o SERVIDOR pos no fio (pra quem, e os bytes).</summary>
	private readonly List<(int Para, byte[] Pacote)> _pacotes = [];
	private IEnumerator? _roteiro;
	private double _espera = 2.5;
	private bool _acabou, _ouvindo;
	private string _pasta = "";

	/// <summary>O arranjo do menu de antes da bancada (ela muda o tamanho pra duas fotos, e devolve).</summary>
	private (bool Mexido, float X, float Y, float L, float A)? _arranjoDeAntes;

	private static GameClient? C => GameClient.Instance;
	private static GameServer? S => GameServer.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dalunos]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[dalunos]   FALHA {nome}   [{detalhe}]"); }
	}

	private void Injeta(string nome, bool ficouVermelha, string detalhe = "")
	{
		if (ficouVermelha) { _injOk++; GD.Print($"[dalunos]   pegou  (injecao) {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _injFalhou++; _vermelhas.Add("(injecao) " + nome); GD.PrintErr($"[dalunos]   FALHA (injecao que PASSOU) {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[dalunos] " + s);

	private static string Arg(string chave)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, chave);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : "";
	}

	/// <summary>O que o servidor me DIZ (canal de sistema): a recusa e a confirmacao de um gesto sao frases.</summary>
	private void AoFalar(Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Protocol.Fala.Sistema) _doServidor.Add(texto);
	}

	private bool Ouvi(string pedaco) => _doServidor.Any(t => t.Contains(pedaco, StringComparison.OrdinalIgnoreCase));

	public override void _ExitTree()
	{
		if (_ouvindo && C is { } cli) cli.Falou -= AoFalar;
		DesligarOsBotoesDeBancada();
	}

	/// <summary>
	/// Os defeitos injetados, a escuta dos paineis e o espiao dos verbs nao podem sobrar ligados quando o roteiro acaba
	/// (ou estoura no meio): o processo e host, e o servidor continua servindo com eles. O palco e o arranjo do menu
	/// voltam junto.
	/// </summary>
	private void DesligarOsBotoesDeBancada()
	{
		GameServer.EscutaDoDiscipulado = null;
		GameClient.EspiaoDeVerbos = null;
		if (S is { } srv)
		{
			srv.PainelSemVinculoDeTeste = srv.CartaoSemMiraDeTeste = srv.PoderNoPainelDeTeste = srv.FotoDoAlunoNaoSeGuardaDeTeste = false;
			srv.RecolherDiscipuladoDeTeste();
		}
		ProgressoDeAluno.ColegaVeTudoDeTeste = ProgressoDeAluno.SemOrdemDeTeste = false;
		DevolverOArranjo();
	}

	private void DevolverOArranjo()
	{
		if (_arranjoDeAntes is not { } a) return;
		_arranjoDeAntes = null;
		ArranjoDaInterface arr = Janelas.Arranjo;
		(arr.MenuMexido, arr.MenuX, arr.MenuY, arr.MenuLargura, arr.MenuAltura) = (a.Mexido, a.X, a.Y, a.L, a.A);
		Janelas.Avisar();
	}

	/// <summary>Poe o menu neste retangulo (sem gravar no `config.json`: e so pro tempo da foto).</summary>
	private void MenuEm(Rect2 r)
	{
		ArranjoDaInterface arr = Janelas.Arranjo;
		_arranjoDeAntes ??= (arr.MenuMexido, arr.MenuX, arr.MenuY, arr.MenuLargura, arr.MenuAltura);
		Janelas.GuardarMenu(arr, r, GetViewport().GetVisibleRect().Size);
		Janelas.Avisar();
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia?.PosicaoLocal == null || M == null) return;
		if (!_ouvindo) { cli.Falou += AoFalar; _ouvindo = true; }

		_espera -= delta;
		if (_espera > 0) return;

		try
		{
			_roteiro ??= Roteiro().GetEnumerator();
			if (!_roteiro.MoveNext()) { Fim(); return; }
			_espera = _roteiro.Current is double d ? d : 0;
		}
		catch (Exception e)
		{
			// UMA EXCECAO NO ROTEIRO E UM VERMELHO, NAO UM SILENCIO: sem isto o processo ficaria vivo, sem placar,
			// ate o prazo do lancador.
			Afirmar("o roteiro correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
			Fim();
		}
	}

	private void Fim()
	{
		_acabou = true;
		DesligarOsBotoesDeBancada();
		VistosDeGente.EsquecerDeTeste();
		foreach (string f in _fotos) Nota("foto  " + f);
		Nota($"injecoes: {_injOk} pegas, {_injFalhou} passaram batido");
		if (_falhou + _injFalhou > 0) GD.PrintErr("[dalunos] vermelhas: " + string.Join(" | ", _vermelhas));
		GD.Print(_falhou + _injFalhou == 0
			? $"\n[dalunos] ===== TUDO OK ({_ok} ok, {_injOk} injecoes pegas) ====="
			: $"\n[dalunos] ===== {_falhou + _injFalhou} FALHA(S) ({_ok} ok) =====");
	}

	/// <summary>Espera ate a condicao valer (ou o prazo vencer), sem travar o quadro.</summary>
	private static IEnumerable<double> Ate(Func<bool> cond, double segundos)
	{
		for (double t = 0; t < segundos && !cond(); t += 0.1) yield return 0.1;
	}

	private Image? Fotografar(string nome, string legenda)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"sem foto pra {nome} (headless nao renderiza): rode com janela"); return null; }
		string caminho = _pasta + nome + ".png";
		img.SavePng(caminho);
		_fotos.Add(caminho);

		// A HISTORIA vai em meia escala: oito telas inteiras lado a lado dariam uma tira de 10 mil px.
		var pequena = (Image)img.Duplicate();
		pequena.Resize(img.GetWidth() / 2, img.GetHeight() / 2, Image.Interpolation.Lanczos);
		_historia.Add(new TiraDeFotos.Quadro(pequena, legenda));
		return img;
	}

	/// <summary>O recorte de UM cartao de uma foto ja tirada, ampliado duas vezes (sem borrar o pixel art do retrato).</summary>
	private void Recortar(Image? foto, Control? cartao, string nome)
	{
		if (foto == null || cartao == null || !IsInstanceValid(cartao)) return;
		Rect2 r = cartao.GetGlobalRect();
		Rect2I area = new Rect2I(Math.Max(0, (int)r.Position.X - 6), Math.Max(0, (int)r.Position.Y - 6),
								 (int)r.Size.X + 12, (int)r.Size.Y + 12)
			.Intersection(new Rect2I(0, 0, foto.GetWidth(), foto.GetHeight()));
		if (area.Size.X <= 8 || area.Size.Y <= 8) return;

		Image recorte = foto.GetRegion(area);
		recorte.Resize(area.Size.X * 2, area.Size.Y * 2, Image.Interpolation.Nearest);
		string caminho = _pasta + nome + ".png";
		recorte.SavePng(caminho);
		_fotos.Add(caminho);
	}

	private static void RolarPraCima(MenuJogo menu) => menu.RolagemDeTeste.ScrollVertical = 0;

	private static void TrazerPraTela(MenuJogo menu, Control? alvo)
	{
		if (alvo != null && IsInstanceValid(alvo)) menu.RolagemDeTeste.EnsureControlVisible(alvo);
	}

	/// <summary>O botao de uma aba na barra do menu (os botoes de aba sao os de alternar, pelo texto).</summary>
	private static Button? BotaoDaAba(MenuJogo menu, string aba) =>
		Todos(menu).OfType<Button>().FirstOrDefault(b => b.ToggleMode && b.Text == aba && b.IsVisibleInTree());

	/// <summary>A pagina da aba do discipulado que estiver aberta (ela tem dois nomes), ou nula.</summary>
	private static Control? Pagina(MenuJogo menu) =>
		menu.AbaDeTeste is MenuJogo.AbaDeAlunos or MenuJogo.AbaDeMestre ? menu.PaginaDeTeste(menu.AbaDeTeste) : null;

	private static CartaoLido Lido(MenuJogo menu, string tipo, string nome) =>
		Pagina(menu) is { } pg && Cartao(pg, tipo, nome) is { } c ? Ler(c) : SemCartao;

	/// <summary>
	/// O que a aba SENSE escreve no "poder relativo" desta pessoa, ou nulo. O cartao de presenca dela la tem por titulo
	/// "??? (assinatura)" enquanto eu nao a conheco e o NOME depois (uns segundos ao lado bastam pra ela entrar na
	/// minha lista de conhecidos).
	/// </summary>
	private static string? PoderNaAbaSense(MenuJogo menu, string assinatura, string nome)
	{
		if (menu.PaginaDeTeste("Sense") is not { } pg) return null;
		foreach (PanelContainer card in Todos(pg).OfType<PanelContainer>().Where(p => p.HasMeta("cartao")))
			if (Todos(card).OfType<Label>().Any(l => l.Text == $"??? ({assinatura})" || l.Text == nome))
				return LinhasDe(card).FirstOrDefault(l => l.Rotulo == MenuJogo.RotuloDoPoderRelativo).Valor;
		return null;
	}

	/// <summary>
	/// O que a LISTA do sentido de Ki diz desta pessoa ("80%"), ou "" se ela nao esta na lista -- a procura da BANCADA,
	/// escrita aqui e nao pedida ao cliente: pela assinatura (quem eu nao conheco) ou pelo nome (quem eu conheco; no
	/// palco nao ha homonimos).
	/// </summary>
	private static string NaLista(GameClient cli, string sig, string nome) =>
		cli.Sentidos.Where(p => p.Assinatura.Length > 0 ? p.Assinatura == sig : p.Nome == nome)
			.Select(p => $"{p.PoderRelativo:0}%").FirstOrDefault() ?? "";

	/// <summary>Os cartoes de um tipo lado a lado numa linha? (dois com o mesmo Y) -- ou um por linha (todos com o mesmo X)?</summary>
	private static (bool LadoALado, bool UmPorLinha, float MaiorLargura) Arrumacao(Control pagina, string tipo)
	{
		List<Rect2> r = [.. Cartoes(pagina, tipo).Select(c => c.GetGlobalRect())];
		bool lado = false;
		for (int i = 0; i < r.Count; i++)
			for (int j = i + 1; j < r.Count; j++)
				if (Math.Abs(r[i].Position.Y - r[j].Position.Y) < 1f && Math.Abs(r[i].Position.X - r[j].Position.X) > 8f) lado = true;
		bool um = r.Count > 0 && r.All(x => Math.Abs(x.Position.X - r[0].Position.X) < 1f);
		return (lado, um, r.Count > 0 ? r.Max(x => x.Size.X) : 0);
	}

	/// <summary>Planta no CLIENTE a lembranca de um rosto (a aparencia como eu o vi): o retrato do cartao sai dela.</summary>
	private static void LembrarORosto(GameClient cli, string nome, string raca, string cabelo)
	{
		if (VistosDeGente.Catalogo == null) return;
		Appearance ap = cli.Visual.Copiar();
		ap.Cabelo = cabelo;
		ap.CorCabelo = null;
		VistosDeGente.AnotarDeTeste(nome, raca, "Male", ap);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable<double> Roteiro()
	{
		GameClient cli = C!;
		MenuJogo menu = M!;

		_pasta = Arg("--pasta");
		if (_pasta.Length == 0) _pasta = ProjectSettings.GlobalizePath("user://");
		_pasta = _pasta.Replace('\\', '/');
		if (!_pasta.EndsWith('/')) _pasta += "/";
		DirAccess.MakeDirRecursiveAbsolute(_pasta);

		Nota("================ A ABA ALUNOS / MESTRE: O DISCIPULADO NA TELA ================");
		Nota($"fotos em {_pasta}; janela {DisplayServer.WindowGetSize()}");

		GameServer? srv = S;
		Afirmar("ha servidor no processo (`--diagalunos` precisa de `--host`)", srv != null);
		if (srv == null) yield break;
		int id = cli.LocalId;
		string minhaSig = srv.AssinaturaDeTeste(id);
		Afirmar("(montagem) o personagem da bancada tem assinatura, e as tres licoes do palco existem no catalogo",
				minhaSig.Length > 0 && srv.LicoesDoPalcoDeAlunos() != null, $"sig '{minhaSig}'");
		if (minhaSig.Length == 0 || srv.LicoesDoPalcoDeAlunos() is not { } licoes) yield break;

		// MEIO-DIA E CEU LIMPO: o menu e de vidro, e a foto sai com a mesma luz atras em toda rodada.
		srv.CravarMeioDiaDaVariedade(id);
		GameServer.EscutaDoDiscipulado = _pacotes;
		GameClient.EspiaoDeVerbos = (cmd, arg) =>
		{
			if (cmd.StartsWith("alunos", StringComparison.Ordinal)) _verbos.Add($"{cmd}:{arg}");
			return true;
		};
		yield return 1.0;

		foreach (double s in SemVinculo(cli, menu, srv, id)) yield return s;
		foreach (double s in QuemGuia(cli, menu, srv, id, licoes)) yield return s;
		foreach (double s in QuemEGuiado(cli, menu, srv, id, minhaSig, licoes)) yield return s;
		foreach (double s in AsDuasPartes(cli, menu, srv, id)) yield return s;
		AsRegrasSabemReprovar();

		// ---------------------------------------------------------------- A HISTORIA
		if (_historia.Count > 0)
		{
			string tira = _pasta + "alunos-0-historia.png";
			double pintada = TiraDeFotos.Montar(_historia, tira);
			_fotos.Add(tira);
			Afirmar($"a tira da historia saiu pintada (as {_historia.Count} telas lado a lado)", pintada > 0.5, $"{pintada:P0} pintada");
		}
		else Nota("sem janela: nenhuma foto foi tirada (as checagens de arvore valem; as fotos nao)");

		menu.Fechar();
	}

	// ---------------------------------------------------------------- 0. SEM VINCULO
	private IEnumerable<double> SemVinculo(GameClient cli, MenuJogo menu, GameServer srv, int id)
	{
		Nota("--- 0. quem nao tem mestre nem aluno: a aba nao existe, e o servidor nao conta nada ---");
		Afirmar("PRECONDICAO: a bancada nasce sem mestre e sem aluno (os fatos do servidor nao trazem nenhum dos dois)",
				(cli.Fatos & (Fatos.TemMestre | Fatos.TemAluno)) == 0, $"{cli.Fatos}");
		menu.Abrir();
		menu.IrPara("People");
		yield return 0.6;
		string[] abas = menu.AbasDeTeste;
		Afirmar("sem vinculo a aba NAO esta na barra (nem 'Alunos', nem 'Mestre'), e nao ha pagina montada pra ela",
				!abas.Contains(MenuJogo.AbaDeAlunos) && !abas.Contains(MenuJogo.AbaDeMestre)
				&& menu.PaginaDeTeste(MenuJogo.AbaDeAlunos) == null && menu.PaginaDeTeste(MenuJogo.AbaDeMestre) == null, string.Join(",", abas));

		// O PEDIDO FORJADO: o verb do painel na mao, por cima da aba que nao existe.
		bool NaoContou() => _pacotes.Count == 0 && cli.MestreEAlunos == null;
		_doServidor.Clear();
		_pacotes.Clear();
		cli.PedirMestreEAlunos(seguir: false);
		foreach (double s in Ate(() => Ouvi("não há discipulado pra mostrar"), 4)) yield return s;
		Afirmar("**o servidor RECUSA o pedido forjado do painel**: pacote nenhum sai, e ele diz por que",
				NaoContou() && Ouvi("não há discipulado pra mostrar"), $"{_pacotes.Count} pacote(s); {string.Join(" | ", _doServidor)}");

		_doServidor.Clear();
		cli.PedirMestreEAlunos(seguir: true);
		cli.DispensarAluno("9999999999");
		cli.AjudarAlunoATransformar("9999999999");
		foreach (double s in Ate(() => _doServidor.Count(t => t.Contains("só vale pra um aluno seu")) >= 2, 4)) yield return s;
		Afirmar("...o `seguir` de quem nao tem vinculo e recusado CALADO, e os dois gestos de cartao forjados ouvem 'isso só vale pra um aluno seu'",
				NaoContou() && !Ouvi("não há discipulado") && _doServidor.Count(t => t.Contains("só vale pra um aluno seu")) == 2,
				string.Join(" | ", _doServidor));

		// O MESMO PEDIDO COM O DEFEITO INJETADO no portao do servidor: o pacote sai, e a checagem de cima reprova.
		try
		{
			srv.PainelSemVinculoDeTeste = true;
			cli.PedirMestreEAlunos(seguir: false);
			foreach (double s in Ate(() => _pacotes.Count > 0, 4)) yield return s;
			Injeta("(defeito injetado: o painel sai pra quem nao tem vinculo) o MESMO pedido rende um pacote, e a checagem da recusa REPROVA",
				   _pacotes.Count > 0, $"{_pacotes.Count} pacote(s)");
		}
		finally { srv.PainelSemVinculoDeTeste = false; }
		yield return 0.4;
		Afirmar("...e mesmo com o pacote VAZADO (pelo defeito), a tela nao ganha a aba: quem a acende sao os fatos, e o cliente joga fora o painel de quem nao tem vinculo",
				cli.MestreEAlunos == null && !menu.AbasDeTeste.Contains(MenuJogo.AbaDeAlunos) && !menu.AbasDeTeste.Contains(MenuJogo.AbaDeMestre));
		_pacotes.Clear();
	}

	// ---------------------------------------------------------------- 1. QUEM GUIA
	private IEnumerable<double> QuemGuia(GameClient cli, MenuJogo menu, GameServer srv, int id, (string ComNiveis, string SemNiveis, string SoDeUm) licoes)
	{
		Nota("--- 1. o host vira MESTRE de quatro (Gohan, Trunks e Goten no mundo; Kuririn fora dele) ---");
		// (o Kuririn fica SEM rosto guardado: ele esta fora do mundo, e este cliente nunca o viu)
		LembrarORosto(cli, "Gohan", "Saiyan", "Kid Gohan");
		LembrarORosto(cli, "Trunks", "Saiyan", "Vegeta");

		Afirmar("(montagem) o servidor plantou o palco: quatro vinculos, cada um com o diario dele", srv.PlantarAlunosDeTeste(id));
		foreach (double s in Ate(() => (cli.Fatos & Fatos.TemAluno) != 0, 6)) yield return s;
		Afirmar("o FATO chega pelo fio (`S2C.Fatos`: tem aluno; nao tem mestre)",
				(cli.Fatos & Fatos.TemAluno) != 0 && (cli.Fatos & Fatos.TemMestre) == 0, $"{cli.Fatos}");
		yield return 0.5;

		string[] abas = menu.AbasDeTeste;
		int people = Array.IndexOf(abas, "People");
		Afirmar("**a aba 'Alunos' APARECE na barra sem reabrir o menu, logo depois da People** (e nao ha aba 'Mestre': o nome segue o papel)",
				people >= 0 && people + 1 < abas.Length && abas[people + 1] == MenuJogo.AbaDeAlunos && !abas.Contains(MenuJogo.AbaDeMestre),
				string.Join(",", abas));
		Button? naBarra = BotaoDaAba(menu, MenuJogo.AbaDeAlunos);
		Afirmar("...com botao na barra", naBarra != null);
		if (naBarra == null) yield break;

		_pacotes.Clear();
		_verbos.Clear();
		naBarra.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => cli.MestreEAlunos != null, 5)) yield return s;
		yield return 0.6;
		Control? pg = menu.PaginaDeTeste(MenuJogo.AbaDeAlunos);
		Afirmar("clicar nele abre a aba, e a pagina esta na tela", menu.AbaDeTeste == MenuJogo.AbaDeAlunos && pg is { Visible: true }, menu.AbaDeTeste);
		if (pg == null) yield break;
		Afirmar("**abrir a aba PEDE o painel (um verb `alunos`) e o servidor MANDA**: um pacote, pra mim",
				_verbos.Count(v => v == "alunos:") == 1 && _pacotes.Count == 1 && _pacotes[0].Para == id,
				$"verbs [{string.Join(",", _verbos)}]; {_pacotes.Count} pacote(s)");

		PainelDoDiscipulado? verdade = srv.PainelDoDiscipuladoDeTeste(id);
		Afirmar("o painel que chegou e o que o servidor monta pra mim (o mesmo conteudo)",
				verdade != null && cli.MestreEAlunos is { } chegou && ProgressoDeAluno.Conteudo(chegou) == ProgressoDeAluno.Conteudo(verdade));
		if (verdade == null) yield break;

		string? faixa = menu.ValorDesenhado(MenuJogo.AbaDeAlunos, "Alunos");
		Afirmar($"a faixa conta os alunos e o teto ('4 de {Discipulado.MaxAlunos}') e diz o que a aba mostra",
				faixa != null && faixa.StartsWith($"4 de {Discipulado.MaxAlunos}", StringComparison.Ordinal) && faixa.Contains("no que VOCÊ ensinou"), faixa ?? "(nula)");

		List<string> ordem = [.. Cartoes(pg, "aluno").Select(c => c.GetMeta("titulo").AsString())];
		Afirmar("**um cartao por aluno, do MAIS ADIANTADO pro menos** (Gohan, Kuririn, Trunks, Goten) -- a ordem do servidor",
				ordem.SequenceEqual(["Gohan", "Kuririn", "Trunks", "Goten"]) && ordem.SequenceEqual(verdade.Alunos.Select(a => a.Quem.Nome)),
				string.Join(",", ordem));
		Afirmar("...e nenhum cartao de mestre nem de colega (eu nao sou aluno de ninguem)", !Cartoes(pg, "mestre").Any() && !Cartoes(pg, "colega").Any());

		foreach (AlunoNoPainel a in verdade.Alunos)
		{
			CartaoLido lido = Lido(menu, "aluno", a.Quem.Nome);
			List<string> ruim = ProblemasDoCartaoDeAluno(lido, a);
			Afirmar($"[{a.Quem.Nome}] o cartao diz EXATAMENTE o que o servidor guarda: {(a.Quem.Online ? "no mundo" : "fora dele")}, "
					+ $"{a.Ensinamentos.Count} ensinamento(s), progresso geral {lido.Geral}", ruim.Count == 0, string.Join("; ", ruim));
		}

		// O MESMO, DITO NO CASO CONCRETO (o que o olho procura em cada cartao).
		string chaveDaForma = MenuJogo.ChaveDoEnsinamento(TipoDeEnsinamento.Forma, "ssj1");
		string chaveComNiveis = MenuJogo.ChaveDoEnsinamento(TipoDeEnsinamento.Licao, licoes.ComNiveis);
		string chaveSemNiveis = MenuJogo.ChaveDoEnsinamento(TipoDeEnsinamento.Licao, licoes.SemNiveis);
		EnsinamentoLido? Ens(CartaoLido c, string chave) => c.Ensinamentos.FirstOrDefault(e => e.Chave == chave);

		CartaoLido gohan = Lido(menu, "aluno", "Gohan");
		EnsinamentoLido? ssjDoGohan = Ens(gohan, chaveDaForma), vooDoGohan = Ens(gohan, chaveComNiveis), kameDoGohan = Ens(gohan, chaveSemNiveis);
		Afirmar("o Gohan: a FORMA despertou COM VOCÊ, maestria 62% (a barra em 0,62), ha 10 dias -- e nada de 'você tentou' (foi de primeira)",
				ssjDoGohan is { Linhas.Count: 1 } && ssjDoGohan.Pilulas.SequenceEqual(["FORMA", "DESPERTOU COM VOCÊ"])
				&& ssjDoGohan.Linhas[0] is { Rotulo: "maestria", Valor: "62%" } && MesmaBarra(ssjDoGohan.Linhas[0].Barra, 0.62)
				&& ssjDoGohan.Quando == "a primeira há 10 dias",
				ssjDoGohan == null ? "sem o bloco" : $"{string.Join("|", ssjDoGohan.Pilulas)} / {string.Join(";", ssjDoGohan.Linhas)} / '{ssjDoGohan.Quando}'");
		Afirmar("...a LIÇÃO que sobe de nivel esta no nivel 2, a meio caminho do 3 (a barra anda DENTRO do nivel), ensinada ha 8 dias",
				vooDoGohan is { Linhas.Count: 1 } && vooDoGohan.Pilulas.SequenceEqual(["LIÇÃO", "APRENDEU"])
				&& vooDoGohan.Linhas[0].Rotulo == "nível" && vooDoGohan.Linhas[0].Valor.StartsWith("2 de ", StringComparison.Ordinal)
				&& vooDoGohan.Linhas[0].Barra > 0.3 && vooDoGohan.Linhas[0].Barra < 1 && vooDoGohan.Quando == "ensinada há 8 dias",
				vooDoGohan == null ? "sem o bloco" : $"{vooDoGohan.Nome}: {string.Join(";", vooDoGohan.Linhas)} / '{vooDoGohan.Quando}'");
		Afirmar("...e a licao que NAO tem niveis diz isso em vez de inventar um 'nível 0 de 3': aprendida, barra cheia",
				kameDoGohan is { Linhas.Count: 1 } && kameDoGohan.Linhas[0] is { Rotulo: "lição", Valor: "aprendida (não tem níveis)" }
				&& MesmaBarra(kameDoGohan.Linhas[0].Barra, 1),
				kameDoGohan == null ? "sem o bloco" : $"{kameDoGohan.Nome}: {string.Join(";", kameDoGohan.Linhas)}");
		Afirmar("...o topo: ONLINE, 'seu aluno há 12 dias', a raca, e o retrato como eu o vi",
				gohan.PilulasDoTopo.SequenceEqual(["ONLINE"]) && gohan.Datas.Contains("seu aluno há 12 dias") && gohan.Datas.Contains("Saiyan") && gohan.TemBoneco,
				gohan.Datas);

		CartaoLido trunks = Lido(menu, "aluno", "Trunks"), goten = Lido(menu, "aluno", "Goten"), kuririn = Lido(menu, "aluno", "Kuririn");
		Afirmar("o Trunks despertou na TERCEIRA tentativa, e o cartao conta ('você tentou 3 vezes'); maestria 12%",
				Ens(trunks, chaveDaForma) is { Linhas.Count: 1 } ssjT && ssjT.Linhas[0].Valor == "12%" && ssjT.Quando.StartsWith("você tentou 3 vezes", StringComparison.Ordinal),
				Ens(trunks, chaveDaForma)?.Quando ?? "sem o bloco");
		EnsinamentoLido? ssjDoGoten = Ens(goten, chaveDaForma);
		Afirmar("o Goten AINDA NÃO DESPERTOU (duas tentativas): a forma esta no cartao assim mesmo, com a barra vazia -- tentar tambem e progresso",
				ssjDoGoten is { Linhas.Count: 1 } && ssjDoGoten.Pilulas.SequenceEqual(["FORMA", "AINDA NÃO DESPERTOU"])
				&& ssjDoGoten.Linhas[0].Valor == "ainda não despertou" && MesmaBarra(ssjDoGoten.Linhas[0].Barra, 0)
				&& ssjDoGoten.Quando.StartsWith("você tentou 2 vezes", StringComparison.Ordinal),
				ssjDoGoten == null ? "sem o bloco" : $"{string.Join("|", ssjDoGoten.Pilulas)} / '{ssjDoGoten.Quando}'");
		EnsinamentoLido? vooDoKuririn = Ens(kuririn, chaveComNiveis);
		Afirmar("**o Kuririn esta FORA do mundo e aparece com a ultima fotografia**: OFFLINE, 'visto por último há 2 h', a licao DOMINADA "
				+ "(o ultimo nivel), 'sem foto' (este cliente nunca o viu), o 'Ajudar a transformar' apagado e o 'Dispensar aluno' aceso",
				kuririn.PilulasDoTopo.SequenceEqual(["OFFLINE"]) && kuririn.Datas.Contains("visto por último há 2 h") && kuririn.Datas.Contains("seu aluno há 30 dias")
				&& kuririn.SemFoto && !kuririn.TemBoneco
				&& vooDoKuririn is { Linhas.Count: 1 } && vooDoKuririn.Pilulas.SequenceEqual(["LIÇÃO", "DOMINOU"]) && MesmaBarra(vooDoKuririn.Linhas[0].Barra, 1)
				&& kuririn.Botoes.Any(b => b is { Acao: "ensinar", Desligado: true }) && kuririn.Botoes.Any(b => b is { Acao: "dispensar", Desligado: false }),
				$"{kuririn.Datas} / {(vooDoKuririn == null ? "sem o bloco" : string.Join("|", vooDoKuririn.Pilulas))}");

		// QUEM ESTA MAIS ADIANTADO EM CADA ENSINAMENTO.
		List<(string Chave, List<LinhaLida> Linhas)> conjunto = LerConjunto(pg);
		List<string> ruimDoConjunto = ProblemasDoConjunto(conjunto, verdade.Alunos);
		Afirmar($"**depois dos cartoes, 'Quem está mais adiantado'**: um cartao por ensinamento ({conjunto.Count}), e em cada um quem o recebeu do primeiro ao ultimo",
				conjunto.Count == 3 && ruimDoConjunto.Count == 0 && TextosDe(pg).Contains(MenuJogo.TituloDoConjunto.ToUpperInvariant()),
				string.Join("; ", ruimDoConjunto));
		List<string> Rotulos(string chave) => [.. conjunto.Where(c => c.Chave == chave).SelectMany(c => c.Linhas).Select(l => l.Rotulo)];
		Afirmar("...na forma: 1º Gohan, 2º Trunks, 3º Goten (o Kuririn nao a recebeu: nao entra); na licao de niveis: 1º Kuririn, 2º Gohan, 3º Trunks, 4º Goten",
				Rotulos(chaveDaForma).SequenceEqual(["1º  Gohan", "2º  Trunks", "3º  Goten"])
				&& Rotulos(chaveComNiveis).SequenceEqual(["1º  Kuririn", "2º  Gohan", "3º  Trunks", "4º  Goten"]),
				$"{string.Join(",", Rotulos(chaveDaForma))} / {string.Join(",", Rotulos(chaveComNiveis))}");
		Afirmar("...e na licao sem niveis os dois que a tem EMPATAM no primeiro lugar (1º Gohan, 1º Kuririn): empate nao vira segundo lugar por sorteio",
				Rotulos(chaveSemNiveis).SequenceEqual(["1º  Gohan", "1º  Kuririn"]), string.Join(",", Rotulos(chaveSemNiveis)));

		// A FORCA, SEM O SENTIDO DE KI.
		Afirmar("(montagem) o personagem nasce sem a skill Sense e sem scouter",
				!cli.Atributos.Tem(Protocol.Poder.Sense) && !cli.Atributos.Tem(Protocol.Poder.Scouter));
		Afirmar("**sem saber sentir Ki, nenhum cartao le forca** -- e o motivo (falta a skill Sense) esta escrito UMA vez, no alto da pagina, "
				+ "e nao repetido em cada cartao (nenhum deles tem frase de forca)",
				gohan.Forca == "sem-sense" && trunks.Forca == "sem-sense" && goten.Forca == "sem-sense" && kuririn.Forca == "offline"
				&& new[] { gohan, trunks, goten, kuririn }.All(c => c.PoderRelativo.Length == 0 && !c.Textos.Any(t => t.StartsWith("força:", StringComparison.OrdinalIgnoreCase)))
				&& ForcaNoAltoDaPagina(pg).SequenceEqual(["sem-sense"]) && TextosDe(pg).Count(t => t.StartsWith("Força: você ainda não sabe sentir Ki", StringComparison.Ordinal)) == 1,
				$"{gohan.Forca},{trunks.Forca},{goten.Forca},{kuririn.Forca}; no alto [{string.Join(",", ForcaNoAltoDaPagina(pg))}]");
		List<string> poder = PoderEscrito(TextosDe(pg));
		Afirmar("**NENHUM numero de poder na pagina** (nem 'Battle Power', nem 'BP', nem numero de cinco digitos ou mais)", poder.Count == 0, string.Join("; ", poder));

		RolarPraCima(menu);
		yield return 0.3;
		Fotografar("alunos-1-mestre", "1 quem guia: os alunos");
		TrazerPraTela(menu, Cartoes(pg, "ranking").LastOrDefault());
		yield return 0.3;
		Fotografar("alunos-1b-quem-esta-mais-adiantado", "1b quem esta mais adiantado");
		RolarPraCima(menu);

		// ---------------------------------------------------------------- A FORCA, COM O SENTIDO DE KI
		Nota("--- 1b. com a skill Sense: a linha 'poder relativo' da aba Sense, e so de quem ela alcanca ---");
		Afirmar("(montagem) a bancada entra como admin (o host), pra ganhar a skill pelo verb de admin", cli.Atributos.Tem(Protocol.Poder.Admin));
		cli.SendVerbo("admin_skill_dar", "|Sense");   // alvo vazio = eu mesmo; por NOME, como o painel de admin manda
		string sigGohan = verdade.Alunos.First(a => a.Quem.Nome == "Gohan").Quem.Assinatura, sigTrunks = verdade.Alunos.First(a => a.Quem.Nome == "Trunks").Quem.Assinatura;
		string sigGoten = verdade.Alunos.First(a => a.Quem.Nome == "Goten").Quem.Assinatura, sigKuririn = verdade.Alunos.First(a => a.Quem.Nome == "Kuririn").Quem.Assinatura;
		foreach (double s in Ate(() => cli.Atributos.Tem(Protocol.Poder.Sense) && NaLista(cli, sigGohan, "Gohan").Length > 0 && NaLista(cli, sigTrunks, "Trunks").Length > 0, 8)) yield return s;
		Afirmar("(montagem) a skill acende, e a lista do sentido de Ki (`S2C.Sentidos`) chega com o Gohan e o Trunks -- e SEM o Goten (40 tiles: fora do alcance)",
				cli.Atributos.Tem(Protocol.Poder.Sense) && !cli.SentidosSaoDoScouter && NaLista(cli, sigGohan, "Gohan").Length > 0 && NaLista(cli, sigTrunks, "Trunks").Length > 0
				&& NaLista(cli, sigGoten, "Goten").Length == 0, $"{cli.Sentidos.Count} presenca(s)");
		foreach (double s in Ate(() => Lido(menu, "aluno", "Gohan").Forca == "sentida", 3)) yield return s;
		gohan = Lido(menu, "aluno", "Gohan");
		trunks = Lido(menu, "aluno", "Trunks");
		goten = Lido(menu, "aluno", "Goten");
		kuririn = Lido(menu, "aluno", "Kuririn");
		Afirmar("**o cartao do Gohan e o do Trunks ganham a linha 'poder relativo', com o numero da lista do sentido de Ki** (e a aba se refez sozinha)",
				gohan.Forca == "sentida" && gohan.PoderRelativo == NaLista(cli, sigGohan, "Gohan") && trunks.Forca == "sentida" && trunks.PoderRelativo == NaLista(cli, sigTrunks, "Trunks"),
				$"Gohan {gohan.PoderRelativo} (lista {NaLista(cli, sigGohan, "Gohan")}), Trunks {trunks.PoderRelativo} (lista {NaLista(cli, sigTrunks, "Trunks")})");
		Afirmar("...quem e mais fraco que eu le ABAIXO de 100% (o Gohan tem 80% do meu poder, o Trunks 45%) -- e e so isso que se sabe",
				float.TryParse(gohan.PoderRelativo.TrimEnd('%'), out float pg1) && float.TryParse(trunks.PoderRelativo.TrimEnd('%'), out float pt1)
				&& pg1 < 100 && pt1 < pg1, $"{gohan.PoderRelativo} / {trunks.PoderRelativo}");
		Afirmar("**...o Goten esta no mundo mas LONGE: o cartao nao da a leitura de graca** (diz que o sentido de Ki nao o esta lendo); o Kuririn, fora do mundo, "
				+ "continua sem leitura; e a nota do alto da pagina SAIU (quem sente Ki nao precisa dela)",
				goten.Forca == "nao-sentida" && goten.PoderRelativo.Length == 0 && goten.Textos.Contains(MenuJogo.FraseDeNaoSentida)
				&& kuririn.Forca == "offline" && kuririn.PoderRelativo.Length == 0 && ForcaNoAltoDaPagina(pg).Count == 0,
				$"Goten '{goten.Forca}', Kuririn '{kuririn.Forca}', no alto [{string.Join(",", ForcaNoAltoDaPagina(pg))}]");
		Afirmar("...e os quatro cartoes continuam no contrato (a forca e uma linha a mais, nao outra tela)",
				verdade.Alunos.All(a => ProblemasDoCartaoDeAluno(Lido(menu, "aluno", a.Quem.Nome), a).Count == 0),
				string.Join("; ", verdade.Alunos.SelectMany(a => ProblemasDoCartaoDeAluno(Lido(menu, "aluno", a.Quem.Nome), a))));

		TrazerPraTela(menu, Cartao(pg, "aluno", "Gohan"));
		yield return 0.3;
		Image? fotoComSense = Fotografar("alunos-2-com-sense", "2 com o sentido de Ki");
		Recortar(fotoComSense, Cartao(pg, "aluno", "Gohan"), "alunos-2-cartao");

		// A MESMA LEITURA DA ABA SENSE -- lida na tela dela, pra mesma pessoa, NO MESMO QUADRO (as duas paginas se
		// desenham na hora, da mesma lista; com um segundo de intervalo a razao pode ter andado um ponto com o Ki).
		menu.IrPara("Sense");
		string? naSense = PoderNaAbaSense(menu, sigGohan, "Gohan");
		menu.IrPara(MenuJogo.AbaDeAlunos);
		menu.ForcarRedesenho();
		string noCartao = Lido(menu, "aluno", "Gohan").PoderRelativo;
		Afirmar("**e a MESMA leitura da aba Sense**: o cartao de presenca do Gohan la escreve o mesmo 'poder relativo' que o cartao dele aqui",
				naSense != null && naSense == noCartao && noCartao == NaLista(cli, sigGohan, "Gohan"), $"Sense '{naSense}', Alunos '{noCartao}', lista '{NaLista(cli, sigGohan, "Gohan")}'");
		yield return 0.5;

		// ---------------------------------------------------------------- O SCOUTER NAO VIRA NUMERO AQUI
		Nota("--- 1c. com o scouter ligado: a aba Scan tem o numero; esta tela continua sem ---");
		Afirmar("(montagem) um scouter na mochila", srv.ScouterNaMochilaDeTeste(id));
		cli.SendVerbo("item_equipar", CatalogoDeItens.Scouter);
		foreach (double s in Ate(() => cli.Atributos.Tem(Protocol.Poder.Scouter) && cli.SentidosSaoDoScouter && cli.Sentidos.Any(p => !double.IsNaN(p.Bp)), 8)) yield return s;
		Afirmar("(montagem) o scouter liga pelo verb de producao, e a lista vira a do Scan: cada presenca com o BP exato",
				cli.Atributos.Tem(Protocol.Poder.Scouter) && cli.SentidosSaoDoScouter && cli.Sentidos.Any(p => !double.IsNaN(p.Bp)), $"{cli.Sentidos.Count} leitura(s)");
		foreach (double s in Ate(() => Lido(menu, "aluno", "Gohan").Forca == "scouter", 3)) yield return s;
		gohan = Lido(menu, "aluno", "Gohan");
		List<string> textos = Pagina(menu) is { } pgScan ? TextosDe(pgScan) : [];
		List<string> numerosDoScan = [.. cli.Sentidos.Where(p => !double.IsNaN(p.Bp))
			.SelectMany(p => new[] { p.Bp.ToString("N0"), ((long)p.Bp).ToString(), ((long)p.Bp).ToString("N0", System.Globalization.CultureInfo.InvariantCulture) })];
		List<string> noAlto = Pagina(menu) is { } pgAlto ? ForcaNoAltoDaPagina(pgAlto) : [];
		Afirmar("**com o scouter ligado a tela continua SEM NUMERO**: o alto da pagina diz (uma vez) que a leitura de poder e a da aba Scan, os cartoes ficam sem "
				+ "linha de forca, e nenhum BP que o scouter le esta escrito aqui",
				gohan.Forca == "scouter" && gohan.PoderRelativo.Length == 0 && PoderEscrito(textos).Count == 0
				&& noAlto.SequenceEqual(["scouter"]) && textos.Count(t => t.StartsWith("Força: com o scouter ligado", StringComparison.Ordinal)) == 1
				&& !textos.Any(t => numerosDoScan.Any(n => n.Length >= 4 && t.Contains(n))),
				$"Gohan '{gohan.Forca}'; no alto [{string.Join(",", noAlto)}]; {string.Join("; ", PoderEscrito(textos))}");
		cli.SendVerbo("item_equipar", CatalogoDeItens.Scouter);   // o mesmo verb desliga
		foreach (double s in Ate(() => !cli.Atributos.Tem(Protocol.Poder.Scouter) && !cli.SentidosSaoDoScouter && NaLista(cli, sigGohan, "Gohan").Length > 0, 8)) yield return s;
		foreach (double s in Ate(() => Lido(menu, "aluno", "Gohan").Forca == "sentida", 3)) yield return s;
		Afirmar("...e desligado o scouter, a leitura do sentido de Ki volta ao cartao", Lido(menu, "aluno", "Gohan").Forca == "sentida", Lido(menu, "aluno", "Gohan").Forca);

		// ---------------------------------------------------------------- A TELA ABERTA ACOMPANHA
		Nota("--- 1d. a tela aberta acompanha o que muda no servidor; num mundo parado nao chega nada ---");
		_pacotes.Clear();
		_verbos.Clear();
		yield return 5.0;
		Afirmar("num mundo PARADO a tela aberta continua perguntando (`alunos seguir`, a cada 2 s) e o servidor NAO manda pacote nenhum",
				_verbos.Count(v => v == "alunos:seguir") >= 2 && _verbos.All(v => v == "alunos:seguir") && _pacotes.Count == 0,
				$"verbs [{string.Join(",", _verbos)}]; {_pacotes.Count} pacote(s)");

		Afirmar("(montagem) o Trunks 'treinou': a maestria dele vai de 12% pra 90% no servidor", srv.MaestriaDeTeste(sigTrunks, "ssj1", 90));
		foreach (double s in Ate(() => Ens(Lido(menu, "aluno", "Trunks"), chaveDaForma) is { Linhas.Count: 1 } e && e.Linhas[0].Valor == "90%", 6)) yield return s;
		trunks = Lido(menu, "aluno", "Trunks");
		Afirmar("**o cartao do Trunks passa a dizer 90% SOZINHO** (a tela perguntou, o conteudo tinha mudado, o pacote veio)",
				Ens(trunks, chaveDaForma) is { Linhas.Count: 1 } novo && novo.Linhas[0].Valor == "90%" && MesmaBarra(novo.Linhas[0].Barra, 0.9) && _pacotes.Count >= 1,
				$"{Ens(trunks, chaveDaForma)?.Linhas.FirstOrDefault().Valor}; {_pacotes.Count} pacote(s)");
		conjunto = Pagina(menu) is { } pgNova ? LerConjunto(pgNova) : [];
		Afirmar("...e o ranking da forma se refaz: agora 1º Trunks, 2º Gohan, 3º Goten",
				Rotulos(chaveDaForma).SequenceEqual(["1º  Trunks", "2º  Gohan", "3º  Goten"]), string.Join(",", Rotulos(chaveDaForma)));
		verdade = srv.PainelDoDiscipuladoDeTeste(id)!;
		Afirmar("...com a pagina inteira no contrato de novo (cartoes e conjunto)",
				verdade.Alunos.All(a => ProblemasDoCartaoDeAluno(Lido(menu, "aluno", a.Quem.Nome), a).Count == 0) && ProblemasDoConjunto(conjunto, verdade.Alunos).Count == 0,
				string.Join("; ", verdade.Alunos.SelectMany(a => ProblemasDoCartaoDeAluno(Lido(menu, "aluno", a.Quem.Nome), a)).Concat(ProblemasDoConjunto(conjunto, verdade.Alunos))));

		// ---------------------------------------------------------------- O MENU E UMA JANELA
		Nota("--- 1e. o menu pequeno e o menu grande ---");
		foreach (double s in DoisTamanhos(menu, "aluno", "alunos-3-menu-pequeno", "3 quem guia, menu minimo", "alunos-4-menu-grande", "4 quem guia, menu grande")) yield return s;

		// ---------------------------------------------------------------- OS GESTOS DO CARTAO
		Nota("--- 1f. 'Ajudar a transformar' e 'Dispensar aluno', no cartao de CADA aluno ---");
		pg = menu.PaginaDeTeste(MenuJogo.AbaDeAlunos)!;
		int TentativasDoGoten() => srv.PainelDoDiscipuladoDeTeste(id)?.Alunos.FirstOrDefault(a => a.Quem.Nome == "Goten")?
			.Ensinamentos.FirstOrDefault(e => e.Tipo == TipoDeEnsinamento.Forma).Tentativas ?? -1;
		int tentativas = TentativasDoGoten();

		// SEM NINGUEM NA FRENTE: o botao do Gohan (a 4 tiles) recusa, e nao gasta nada.
		_doServidor.Clear();
		BotaoDe(Cartao(pg, "aluno", "Gohan"), "ensinar")?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => Ouvi("fique de frente para Gohan"), 4)) yield return s;
		Afirmar("'Ajudar a transformar' no cartao do Gohan, com ele a 4 tiles: o servidor pede 'fique de frente para Gohan', e o folego de ensinar NAO e gasto",
				Ouvi("fique de frente para Gohan") && srv.RecargaDeEnsinoDeTeste(id) <= 0, string.Join(" | ", _doServidor));

		// A MIRA: o Goten esta colado na minha frente, e eu clico no cartao do TRUNKS.
		Afirmar("(montagem) o Goten vem pra frente do host (colado, ao alcance do gesto)", srv.PorNaFrenteDeTeste(id, sigGoten));
		bool OGotenFicouEmPaz() => srv.OfertaDeDespertarDeTeste(sigGoten).Length == 0 && TentativasDoGoten() == tentativas && srv.RecargaDeEnsinoDeTeste(id) <= 0;
		_doServidor.Clear();
		BotaoDe(Cartao(pg, "aluno", "Trunks"), "ensinar")?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => Ouvi("fique de frente para Trunks"), 4)) yield return s;
		Afirmar("**o botao do cartao e MIRADO**: com o Goten na frente, clicar no cartao do TRUNKS nao provoca o Goten -- ninguem recebe oferta, "
				+ "nenhuma tentativa e anotada e o folego nao e gasto",
				Ouvi("fique de frente para Trunks") && OGotenFicouEmPaz(),
				$"oferta '{srv.OfertaDeDespertarDeTeste(sigGoten)}', tentativas {TentativasDoGoten()}, recarga {srv.RecargaDeEnsinoDeTeste(id):0} s; {string.Join(" | ", _doServidor)}");
		try
		{
			srv.CartaoSemMiraDeTeste = true;
			BotaoDe(Cartao(pg, "aluno", "Trunks"), "ensinar")?.EmitSignal(BaseButton.SignalName.Pressed);
			foreach (double s in Ate(() => !OGotenFicouEmPaz(), 4)) yield return s;
			Injeta("(defeito injetado: o gesto do cartao nao confere quem esta na frente) o MESMO clique provoca o Goten, e a checagem da mira REPROVA",
				   !OGotenFicouEmPaz(), $"oferta '{srv.OfertaDeDespertarDeTeste(sigGoten)}', tentativas {TentativasDoGoten()}");
		}
		finally { srv.CartaoSemMiraDeTeste = false; }
		srv.EsquecerOfertaDeTeste(sigGoten);
		srv.ZerarRecargaDeEnsinoDeTeste(id);
		tentativas = TentativasDoGoten();

		// O GESTO CERTO: o Goten na frente, o botao do cartao DELE.
		_doServidor.Clear();
		BotaoDe(Cartao(pg, "aluno", "Goten"), "ensinar")?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => srv.OfertaDeDespertarDeTeste(sigGoten).Length > 0, 4)) yield return s;
		Afirmar("**clicar no cartao do GOTEN, com ele na frente, e o 'Ajudar a transformar' de sempre**: a oferta de despertar vai pra mesa dele (minha), "
				+ "a tentativa entra no diario e o meu folego de ensinar comeca a correr",
				srv.OfertaDeDespertarDeTeste(sigGoten) == srv.AssinaturaDeTeste(id) && TentativasDoGoten() == tentativas + 1
				&& srv.RecargaDeEnsinoDeTeste(id) > Discipulado.RecargaDeEnsinoSegundos - 30 && Ouvi("Goten"),
				$"oferta '{srv.OfertaDeDespertarDeTeste(sigGoten)}', tentativas {tentativas} -> {TentativasDoGoten()}, recarga {srv.RecargaDeEnsinoDeTeste(id):0} s; {string.Join(" | ", _doServidor)}");
		foreach (double s in Ate(() => Ens(Lido(menu, "aluno", "Goten"), chaveDaForma)?.Quando.StartsWith($"você tentou {tentativas + 1} vezes", StringComparison.Ordinal) == true, 6)) yield return s;
		Afirmar($"...e o cartao dele passa a contar a tentativa nova ('você tentou {tentativas + 1} vezes'), sozinho",
				Ens(Lido(menu, "aluno", "Goten"), chaveDaForma)?.Quando.StartsWith($"você tentou {tentativas + 1} vezes", StringComparison.Ordinal) == true,
				Ens(Lido(menu, "aluno", "Goten"), chaveDaForma)?.Quando ?? "sem o bloco");
		_doServidor.Clear();
		BotaoDe(Cartao(pg, "aluno", "Goten"), "ensinar")?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => Ouvi("sem folego pra ensinar"), 4)) yield return s;
		Afirmar("...a recarga e a MESMA do verb (regra que ja existia): o segundo clique ouve que ainda nao ha folego, e nada mais e anotado",
				Ouvi("sem folego pra ensinar") && TentativasDoGoten() == tentativas + 1, string.Join(" | ", _doServidor));
		srv.EsquecerOfertaDeTeste(sigGoten);
		srv.ZerarRecargaDeEnsinoDeTeste(id);

		// DISPENSAR: dois cliques, e vale pra quem esta fora do mundo.
		Afirmar("(montagem) o Kuririn ainda e meu aluno", srv.MestreDeTeste(sigKuririn) == srv.AssinaturaDeTeste(id));
		TrazerPraTela(menu, Cartao(pg, "aluno", "Kuririn"));
		_verbos.Clear();
		BotaoDe(Cartao(pg, "aluno", "Kuririn"), "dispensar")?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.5;
		kuririn = Lido(menu, "aluno", "Kuririn");
		Afirmar("**o primeiro clique em 'Dispensar aluno' so ARMA**: o botao vira 'Confirmar: dispensar Kuririn', nenhum verb sai, o vinculo continua",
				kuririn.Botoes.Any(b => b is { Acao: "dispensar", Armado: true, Texto: "Confirmar: dispensar Kuririn" })
				&& !_verbos.Any(v => v.StartsWith("alunos_dispensar", StringComparison.Ordinal)) && srv.MestreDeTeste(sigKuririn) == srv.AssinaturaDeTeste(id),
				string.Join(",", kuririn.Botoes.Select(b => b.Texto)));
		Afirmar("...e arma SO aquele: o 'Dispensar aluno' dos outros cartoes continua desarmado",
				new[] { "Gohan", "Trunks", "Goten" }.All(n => Lido(menu, "aluno", n).Botoes.Any(b => b is { Acao: "dispensar", Armado: false, Texto: "Dispensar aluno" })));
		Recortar(Fotografar("alunos-4b-dispensar-armado", "4b dispensar: o segundo clique"), Cartao(pg, "aluno", "Kuririn"), "alunos-4b-cartao-armado");
		yield return 4.4;
		kuririn = Lido(menu, "aluno", "Kuririn");
		Afirmar("...quem nao confirma em 4 s ve o botao DESARMAR sozinho (um clique perdido nao fica esperando o proximo)",
				kuririn.Botoes.Any(b => b is { Acao: "dispensar", Armado: false, Texto: "Dispensar aluno" }), string.Join(",", kuririn.Botoes.Select(b => b.Texto)));

		_doServidor.Clear();
		_pacotes.Clear();
		BotaoDe(Cartao(pg, "aluno", "Kuririn"), "dispensar")?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.4;
		BotaoDe(Cartao(pg, "aluno", "Kuririn"), "dispensar")?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => srv.MestreDeTeste(sigKuririn).Length == 0 && Cartao(pg, "aluno", "Kuririn") == null, 5)) yield return s;
		yield return 0.4;
		Afirmar("**o segundo clique DISPENSA -- com o Kuririn fora do mundo** (o gesto do painel do original: pela pessoa, de qualquer lugar): "
				+ "um verb `alunos_dispensar` com a assinatura dele, o vinculo some no servidor, e o servidor diz",
				_verbos.Count(v => v == $"alunos_dispensar:{sigKuririn}") == 1 && srv.MestreDeTeste(sigKuririn).Length == 0 && Ouvi("Kuririn nao e mais seu aluno"),
				$"verbs [{string.Join(",", _verbos.Where(v => !v.StartsWith("alunos:", StringComparison.Ordinal)))}]; {string.Join(" | ", _doServidor)}");
		ordem = [.. Cartoes(pg, "aluno").Select(c => c.GetMeta("titulo").AsString())];
		faixa = menu.ValorDesenhado(MenuJogo.AbaDeAlunos, "Alunos");
		Afirmar($"...e a tela se acerta NA HORA: o cartao dele sai, a faixa diz '3 de {Discipulado.MaxAlunos}', e os outros tres continuam alunos",
				!ordem.Contains("Kuririn") && ordem.Count == 3 && faixa != null && faixa.StartsWith($"3 de {Discipulado.MaxAlunos}", StringComparison.Ordinal)
				&& srv.MestreDeTeste(sigGohan) == srv.AssinaturaDeTeste(id) && _pacotes.Count >= 1, $"{string.Join(",", ordem)} / {faixa}");

		// O GESTO FORJADO sobre quem NAO e meu aluno (o Kuririn, agora): recusa, e nada muda.
		_doServidor.Clear();
		cli.DispensarAluno(sigKuririn);
		cli.AjudarAlunoATransformar(sigKuririn);
		foreach (double s in Ate(() => _doServidor.Count(t => t.Contains("só vale pra um aluno seu")) >= 2, 4)) yield return s;
		Afirmar("o pacote forjado dos dois gestos sobre quem NAO e (mais) meu aluno ouve 'isso só vale pra um aluno seu'",
				_doServidor.Count(t => t.Contains("só vale pra um aluno seu")) == 2, string.Join(" | ", _doServidor));
	}

	/// <summary>
	/// O MENU NO TAMANHO MINIMO (520 x 320) E QUASE NA TELA INTEIRA: os cartoes de pessoa sao um por linha no pequeno e
	/// lado a lado no grande, sem rolagem horizontal em nenhum dos dois -- e a foto de cada um. No fim o menu volta ao
	/// arranjo de antes.
	/// </summary>
	private IEnumerable<double> DoisTamanhos(MenuJogo menu, string tipo, string fotoPequena, string legendaPequena, string fotoGrande, string legendaGrande)
	{
		Vector2 tela = GetViewport().GetVisibleRect().Size;

		MenuEm(new Rect2(40, 40, Janelas.MenuMinimo));
		yield return 0.6;
		Control? pg = Pagina(menu);
		RolarPraCima(menu);
		yield return 0.3;
		(bool lado, bool um, float largura) = pg != null ? Arrumacao(pg, tipo) : (false, false, 0);
		float pagina = menu.RolagemDeTeste.GetGlobalRect().Size.X;
		Afirmar($"no menu MINIMO ({Janelas.MenuMinimo.X:0} x {Janelas.MenuMinimo.Y:0}) os cartoes ficam um por linha, cabem na largura da pagina e nao ha rolagem horizontal",
				pg != null && Math.Abs(menu.RetanguloDeTeste.Size.X - Janelas.MenuMinimo.X) < 1f && um && !lado && largura <= pagina + 0.75f
				&& !menu.RolagemDeTeste.GetHScrollBar().Visible,
				$"menu {menu.RetanguloDeTeste.Size}, maior cartao {largura:0} px, pagina {pagina:0} px, barra horizontal {menu.RolagemDeTeste.GetHScrollBar().Visible}");
		Fotografar(fotoPequena, legendaPequena);

		MenuEm(new Rect2(20, 20, tela.X - 40, tela.Y - 40));
		yield return 0.6;
		pg = Pagina(menu);
		RolarPraCima(menu);
		yield return 0.3;
		(lado, um, largura) = pg != null ? Arrumacao(pg, tipo) : (false, false, 0);
		pagina = menu.RolagemDeTeste.GetGlobalRect().Size.X;
		Afirmar($"no menu GRANDE ({tela.X - 40:0} x {tela.Y - 40:0}) os cartoes ficam LADO A LADO (a pagina nao foi remontada: a fila quebra linha sozinha), sem rolagem horizontal",
				pg != null && lado && !um && largura <= pagina + 0.75f && !menu.RolagemDeTeste.GetHScrollBar().Visible,
				$"menu {menu.RetanguloDeTeste.Size}, maior cartao {largura:0} px, pagina {pagina:0} px");
		Fotografar(fotoGrande, legendaGrande);

		DevolverOArranjo();
		yield return 0.5;
	}

	// ---------------------------------------------------------------- 2. QUEM E GUIADO
	private IEnumerable<double> QuemEGuiado(GameClient cli, MenuJogo menu, GameServer srv, int id, string minhaSig,
											(string ComNiveis, string SemNiveis, string SoDeUm) licoes)
	{
		Nota("--- 2. o vinculo acaba, e a aba vai embora com ele ---");
		srv.RecolherDiscipuladoDeTeste();
		foreach (double s in Ate(() => (cli.Fatos & (Fatos.TemMestre | Fatos.TemAluno)) == 0 && !menu.AbasDeTeste.Contains(MenuJogo.AbaDeAlunos), 6)) yield return s;
		yield return 0.4;
		Afirmar("**sem aluno nenhum a aba 'Alunos' SAI da barra, o menu cai numa aba que existe (Sense, pra quem tem o sentido de Ki), e o cliente esquece o painel**",
				!menu.AbasDeTeste.Contains(MenuJogo.AbaDeAlunos) && !menu.AbasDeTeste.Contains(MenuJogo.AbaDeMestre) && menu.AbaDeTeste is "Sense" or "Stats"
				&& menu.AbasDeTeste.Contains(menu.AbaDeTeste) && cli.MestreEAlunos == null,
				$"{string.Join(",", menu.AbasDeTeste)} / {menu.AbaDeTeste}");

		Nota("--- 2b. o host vira ALUNO do Bardock, que tem outros tres (Vegeta no mundo; Raditz e Nappa fora dele) ---");
		LembrarORosto(cli, "Bardock", "Saiyan", "Raditz");
		LembrarORosto(cli, "Vegeta", "Saiyan", "Vegeta");
		Afirmar("(montagem) o servidor plantou o palco do aluno", srv.PlantarMestreDeTeste(id));
		foreach (double s in Ate(() => (cli.Fatos & Fatos.TemMestre) != 0, 6)) yield return s;
		yield return 0.5;
		string[] abas = menu.AbasDeTeste;
		int people = Array.IndexOf(abas, "People");
		Afirmar("**quem so e guiado ganha a aba com o nome 'Mestre'** (no mesmo lugar, depois da People) -- e nao ha aba 'Alunos'",
				(cli.Fatos & Fatos.TemAluno) == 0 && people >= 0 && people + 1 < abas.Length && abas[people + 1] == MenuJogo.AbaDeMestre && !abas.Contains(MenuJogo.AbaDeAlunos),
				string.Join(",", abas));
		Button? naBarra = BotaoDaAba(menu, MenuJogo.AbaDeMestre);
		if (naBarra == null) { Afirmar("a aba 'Mestre' tem botao na barra", false); yield break; }

		_pacotes.Clear();
		_verbos.Clear();
		naBarra.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => cli.MestreEAlunos?.Mestre != null, 5)) yield return s;
		yield return 0.6;
		Control? pg = menu.PaginaDeTeste(MenuJogo.AbaDeMestre);
		PainelDoDiscipulado? verdade = srv.PainelDoDiscipuladoDeTeste(id);
		Afirmar("clicar nele abre a aba, o painel chega (um pacote, pra mim) e e o que o servidor monta",
				menu.AbaDeTeste == MenuJogo.AbaDeMestre && pg is { Visible: true } && _pacotes.Count == 1 && _pacotes[0].Para == id
				&& verdade?.Mestre != null && cli.MestreEAlunos is { } chegou && ProgressoDeAluno.Conteudo(chegou) == ProgressoDeAluno.Conteudo(verdade),
				$"{_pacotes.Count} pacote(s)");
		if (pg == null || verdade?.Mestre is not { } m) yield break;

		string? faixa = menu.ValorDesenhado(MenuJogo.AbaDeMestre, "Mestre");
		Afirmar("a faixa diz quem e o mestre e o que a aba mostra", faixa != null && faixa.StartsWith("Bardock", StringComparison.Ordinal) && faixa.Contains("outros alunos dele"), faixa ?? "(nula)");
		Afirmar("nenhum cartao de ALUNO nem ranking de conjunto (eu nao guio ninguem)", !Cartoes(pg, "aluno").Any() && !Cartoes(pg, "ranking").Any());

		// O CARTAO DO MESTRE.
		CartaoLido doMestre = Lido(menu, "mestre", "Bardock");
		List<string> ruim = ProblemasDoCartaoDoMestre(doMestre, m);
		Afirmar("**o cartao do mestre diz o que ele ME ensinou, o MEU progresso em cada coisa e a minha posicao entre os colegas**",
				ruim.Count == 0 && m.Ensinamentos.Count == 3, string.Join("; ", ruim));
		string chaveDaForma = MenuJogo.ChaveDoEnsinamento(TipoDeEnsinamento.Forma, "ssj1");
		string chaveComNiveis = MenuJogo.ChaveDoEnsinamento(TipoDeEnsinamento.Licao, licoes.ComNiveis);
		string chaveSemNiveis = MenuJogo.ChaveDoEnsinamento(TipoDeEnsinamento.Licao, licoes.SemNiveis);
		string chaveSoDeUm = MenuJogo.ChaveDoEnsinamento(TipoDeEnsinamento.Licao, licoes.SoDeUm);
		EnsinamentoLido? Ens(CartaoLido c, string chave) => c.Ensinamentos.FirstOrDefault(e => e.Chave == chave);
		Afirmar("...na forma eu sou o **2º de 3** (o Vegeta, com 62%, esta na frente dos meus 30%; o Raditz ainda nao despertou), e ela despertou COM O MESTRE",
				Ens(doMestre, chaveDaForma) is { Lugar: "2/3", Linhas.Count: 1 } ssj && ssj.Pilulas.SequenceEqual(["FORMA", "DESPERTOU COM O MESTRE", "2º de 3"])
				&& ssj.Linhas[0].Valor == "30%" && ssj.Quando.StartsWith("ele tentou 2 vezes", StringComparison.Ordinal),
				Ens(doMestre, chaveDaForma) is { } e1 ? $"{string.Join("|", e1.Pilulas)} / {e1.Quando}" : "sem o bloco");
		Afirmar("...na licao de niveis tambem 2º de 3; e na licao que SO EU recebi nao ha posicao (nao ha com quem comparar)",
				Ens(doMestre, chaveComNiveis) is { Lugar: "2/3" } && Ens(doMestre, chaveSemNiveis) is { Lugar: "" } so && so.Pilulas.SequenceEqual(["LIÇÃO", "APRENDEU"]),
				$"{Ens(doMestre, chaveComNiveis)?.Lugar} / '{Ens(doMestre, chaveSemNiveis)?.Lugar}'");
		Afirmar("...o topo: ONLINE, 'seu mestre há 9 dias', a linha do treino (ate 3x) e o botao 'Deixar meu mestre'",
				doMestre.PilulasDoTopo.SequenceEqual(["ONLINE"]) && doMestre.Datas.Contains("seu mestre há 9 dias")
				&& doMestre.Textos.Any(t => t.StartsWith("Treinar lutando contra o seu mestre", StringComparison.Ordinal))
				&& doMestre.Botoes.SequenceEqual([new BotaoLido("largar", "Deixar meu mestre", false, false)]), doMestre.Datas);

		// A FORCA: so do mestre, e so pelo sentido de Ki.
		string sigDoMestre = m.Quem.Assinatura;
		foreach (double s in Ate(() => NaLista(cli, sigDoMestre, "Bardock").Length > 0 && Lido(menu, "mestre", "Bardock").Forca == "sentida", 6)) yield return s;
		doMestre = Lido(menu, "mestre", "Bardock");
		Afirmar("**a forca do mestre e a leitura do meu sentido de Ki** (ele esta a 4 tiles): 'poder relativo' ACIMA de 100% -- ele e mais forte que eu, e e so isso que se sabe",
				doMestre.Forca == "sentida" && doMestre.PoderRelativo == NaLista(cli, sigDoMestre, "Bardock")
				&& float.TryParse(doMestre.PoderRelativo.TrimEnd('%'), out float pm) && pm > 100,
				$"cartao {doMestre.PoderRelativo}, lista {NaLista(cli, sigDoMestre, "Bardock")}");

		// OS COLEGAS.
		List<string> colegas = [.. Cartoes(pg, "colega").Select(c => c.GetMeta("titulo").AsString())];
		Afirmar("**um cartao por colega** (Nappa, Raditz, Vegeta -- por nome), debaixo de 'Os outros alunos de Bardock'",
				colegas.SequenceEqual(["Nappa", "Raditz", "Vegeta"]) && colegas.SequenceEqual(m.Colegas) && TextosDe(pg).Contains("OS OUTROS ALUNOS DE BARDOCK"),
				string.Join(",", colegas));
		for (int i = 0; i < m.Colegas.Count; i++)
		{
			CartaoLido lido = Lido(menu, "colega", m.Colegas[i]);
			List<string> ruimDele = ProblemasDoCartaoDeColega(lido, m, i);
			Afirmar($"[{m.Colegas[i]}] do colega so o NOME e o progresso nos ensinamentos em comum ({lido.Ensinamentos.Count}), com as duas barras lado a lado: "
					+ "nem presenca, nem raca, nem datas, nem forca, nem botao", ruimDele.Count == 0, string.Join("; ", ruimDele));
		}
		CartaoLido vegeta = Lido(menu, "colega", "Vegeta"), raditz = Lido(menu, "colega", "Raditz"), nappa = Lido(menu, "colega", "Nappa");
		Afirmar("o Vegeta: na forma 'maestria 62%' ao lado do meu 'maestria 30%'; e a licao que so eu recebi NAO esta no cartao dele",
				Ens(vegeta, chaveDaForma) is { Linhas.Count: 2 } par && par.Linhas[0] is { Rotulo: "Vegeta", Valor: "maestria 62%" } && par.Linhas[1] is { Rotulo: "você", Valor: "maestria 30%" }
				&& Ens(vegeta, chaveSemNiveis) == null && vegeta.Ensinamentos.Count == 2,
				string.Join(" / ", vegeta.Ensinamentos.Select(e => $"{e.Chave}: {string.Join(";", e.Linhas.Select(l => $"{l.Rotulo}={l.Valor}"))}")));
		List<(float Y, float Largura)> metades = Cartao(pg, "colega", "Raditz") is { } doRaditz
			? [.. Todos(doRaditz).OfType<HFlowContainer>().Where(h => h.HasMeta("discipulado") && h.GetMeta("discipulado").AsString() == "par")
				.SelectMany(h => h.GetChildren().OfType<Control>()).Select(c => (c.GetGlobalRect().Position.Y, c.GetGlobalRect().Size.X))]
			: [];
		Afirmar("**as duas barras de cada par ficam LADO A LADO e com a MESMA largura** (so assim se comparam de relance): no cartao do Raditz, "
				+ "'Raditz ... ainda não despertou' e 'você ... maestria 30%' tem textos de tamanhos bem diferentes",
				metades.Count == 4 && Math.Abs(metades[0].Y - metades[1].Y) < 1f && Math.Abs(metades[2].Y - metades[3].Y) < 1f
				&& Math.Abs(metades[0].Largura - metades[1].Largura) <= 1f && Math.Abs(metades[2].Largura - metades[3].Largura) <= 1f,
				string.Join(" | ", metades.Select(m2 => $"y {m2.Y:0} largura {m2.Largura:0}")));
		Afirmar("o Raditz (fora do mundo): 'ainda não despertou' ao lado do meu 30% -- a fotografia dele, sem dizer onde ele esta",
				Ens(raditz, chaveDaForma) is { Linhas.Count: 2 } parR && parR.Linhas[0] is { Rotulo: "Raditz", Valor: "ainda não despertou" }
				&& !raditz.Textos.Any(t => t.Contains("OFFLINE") || t.Contains("visto por último")),
				string.Join(" / ", raditz.Ensinamentos.Select(e => string.Join(";", e.Linhas.Select(l => $"{l.Rotulo}={l.Valor}")))));
		Afirmar("**o Nappa so recebeu uma licao que EU nao recebi: o cartao dele diz 'nenhum ensinamento em comum' e essa licao nao aparece em lugar nenhum da pagina**",
				nappa.Ensinamentos.Count == 0 && nappa.Avisos.Contains("sem-comum") && Ens(doMestre, chaveSoDeUm) == null
				&& !Todos(pg).Any(n => n.HasMeta("ensinamento") && n.GetMeta("ensinamento").AsString() == chaveSoDeUm)
				&& !TextosDe(pg).Contains(MenuJogo.NomeDoEnsinamento(TipoDeEnsinamento.Licao, licoes.SoDeUm)),
				$"{nappa.Ensinamentos.Count} bloco(s); avisos [{string.Join(",", nappa.Avisos)}]");
		Afirmar("**NENHUM numero de poder na pagina do aluno** (nem o do mestre, nem o dos colegas, nem o meu)", PoderEscrito(TextosDe(pg)).Count == 0,
				string.Join("; ", PoderEscrito(TextosDe(pg))));

		// O FIO: dos colegas so o NOME viaja.
		byte[] bytes = _pacotes.Count > 0 ? _pacotes[0].Pacote : [];
		bool Tem(string texto) => texto.Length > 0 && bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(texto)) >= 0;
		string sigDoVegeta = srv.AssinaturaDoPalcoDeTeste("Vegeta");
		Afirmar("no pacote: o nome dos colegas vai ('Vegeta', 'Nappa'), a ASSINATURA de um colega nao vai, e a licao que so o Nappa recebeu nao vai",
				Tem("Vegeta") && Tem("Nappa") && Tem(sigDoMestre) && !Tem(sigDoVegeta) && !Tem(licoes.SoDeUm) && srv.MestreDeTeste(sigDoVegeta) == sigDoMestre,
				$"{bytes.Length} bytes; assinatura do Vegeta '{sigDoVegeta}'");

		RolarPraCima(menu);
		yield return 0.3;
		Image? fotoDoAluno = Fotografar("alunos-5-aluno", "5 quem e guiado: o mestre");
		Recortar(fotoDoAluno, Cartao(pg, "mestre", "Bardock"), "alunos-5-cartao-do-mestre");
		TrazerPraTela(menu, Cartao(pg, "colega", "Vegeta"));
		yield return 0.3;
		Image? fotoDosColegas = Fotografar("alunos-5b-colegas", "5b os outros alunos");
		Recortar(fotoDosColegas, Cartao(pg, "colega", "Vegeta"), "alunos-5b-cartao-de-colega");

		foreach (double s in DoisTamanhos(menu, "colega", "alunos-6-aluno-pequeno", "6 quem e guiado, menu minimo", "alunos-7-aluno-grande", "7 quem e guiado, menu grande")) yield return s;

		// OS GESTOS DE MESTRE, FORJADOS POR UM ALUNO: sobre um colega.
		Nota("--- 2c. o aluno nao dispensa colega; 'Deixar meu mestre' pede dois cliques ---");
		_doServidor.Clear();
		cli.DispensarAluno(sigDoVegeta);
		cli.AjudarAlunoATransformar(sigDoVegeta);
		foreach (double s in Ate(() => _doServidor.Count(t => t.Contains("só vale pra um aluno seu")) >= 2, 4)) yield return s;
		Afirmar("**o pacote forjado de um ALUNO tentando dispensar (ou guiar) um COLEGA e recusado**: 'isso só vale pra um aluno seu', e o Vegeta continua aluno do Bardock",
				_doServidor.Count(t => t.Contains("só vale pra um aluno seu")) == 2 && srv.MestreDeTeste(sigDoVegeta) == sigDoMestre, string.Join(" | ", _doServidor));

		pg = menu.PaginaDeTeste(MenuJogo.AbaDeMestre)!;
		RolarPraCima(menu);
		BotaoDe(Cartao(pg, "mestre", "Bardock"), "largar")?.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.5;
		doMestre = Lido(menu, "mestre", "Bardock");
		Afirmar("o primeiro clique em 'Deixar meu mestre' so ARMA ('Confirmar: deixar Bardock'), e o vinculo continua",
				doMestre.Botoes.SequenceEqual([new BotaoLido("largar", "Confirmar: deixar Bardock", false, true)]) && srv.MestreDeTeste(minhaSig) == sigDoMestre,
				string.Join(",", doMestre.Botoes.Select(b => b.Texto)));
		_doServidor.Clear();
		BotaoDe(Cartao(pg, "mestre", "Bardock"), "largar")?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => srv.MestreDeTeste(minhaSig).Length == 0 && !menu.AbasDeTeste.Contains(MenuJogo.AbaDeMestre), 6)) yield return s;
		yield return 0.4;
		Afirmar("**o segundo clique DEIXA o mestre (o verb de sempre, `mst_largar`)**: o vinculo some no servidor, que diz; a aba 'Mestre' sai da barra e o menu cai numa aba que existe",
				srv.MestreDeTeste(minhaSig).Length == 0 && Ouvi("o aluno seguiu o proprio caminho")
				&& !menu.AbasDeTeste.Contains(MenuJogo.AbaDeMestre) && menu.AbaDeTeste is "Sense" or "Stats" && menu.AbasDeTeste.Contains(menu.AbaDeTeste)
				&& cli.MestreEAlunos == null,
				$"{string.Join(" | ", _doServidor)} / {menu.AbaDeTeste}");
	}

	// ---------------------------------------------------------------- 3. AS DUAS PARTES
	private IEnumerable<double> AsDuasPartes(GameClient cli, MenuJogo menu, GameServer srv, int id)
	{
		Nota("--- 3. mestre de uns e aluno de outro: a aba 'Alunos' mostra as duas partes ---");
		srv.RecolherDiscipuladoDeTeste();
		yield return 0.5;
		Afirmar("(montagem) o servidor plantou os dois palcos de uma vez", srv.PlantarAlunosDeTeste(id) && srv.PlantarMestreDeTeste(id));
		foreach (double s in Ate(() => (cli.Fatos & Fatos.TemMestre) != 0 && (cli.Fatos & Fatos.TemAluno) != 0, 6)) yield return s;
		yield return 0.5;
		string[] abas = menu.AbasDeTeste;
		Afirmar("com os dois fatos a aba se chama 'Alunos' (uma so: nao ha uma segunda aba 'Mestre')",
				abas.Count(a => a is MenuJogo.AbaDeAlunos or MenuJogo.AbaDeMestre) == 1 && abas.Contains(MenuJogo.AbaDeAlunos), string.Join(",", abas));
		BotaoDaAba(menu, MenuJogo.AbaDeAlunos)?.EmitSignal(BaseButton.SignalName.Pressed);
		foreach (double s in Ate(() => cli.MestreEAlunos is { Mestre: not null, Alunos.Count: 4 }, 5)) yield return s;
		Vector2 tela = GetViewport().GetVisibleRect().Size;
		MenuEm(new Rect2(20, 20, tela.X - 40, tela.Y - 40));
		yield return 0.8;

		Control? pg = menu.PaginaDeTeste(MenuJogo.AbaDeAlunos);
		PainelDoDiscipulado? verdade = srv.PainelDoDiscipuladoDeTeste(id);
		if (pg == null || verdade?.Mestre is not { } m) { Afirmar("a pagina e o painel das duas partes existem", false); yield break; }
		List<string> secoes = [.. TextosDe(pg).Where(t => t is "SEUS ALUNOS" or "SEU MESTRE" or "OS OUTROS ALUNOS DE BARDOCK" || t == MenuJogo.TituloDoConjunto.ToUpperInvariant())];
		Afirmar("**a pagina tem as duas partes, nesta ordem**: 'Seus alunos' (4 cartoes), 'Quem está mais adiantado', 'Seu mestre' (1) e 'Os outros alunos de Bardock' (3)",
				secoes.SequenceEqual(["SEUS ALUNOS", MenuJogo.TituloDoConjunto.ToUpperInvariant(), "SEU MESTRE", "OS OUTROS ALUNOS DE BARDOCK"])
				&& Cartoes(pg, "aluno").Count() == 4 && Cartoes(pg, "mestre").Count() == 1 && Cartoes(pg, "colega").Count() == 3,
				$"{string.Join(" > ", secoes)}; {Cartoes(pg, "aluno").Count()}/{Cartoes(pg, "mestre").Count()}/{Cartoes(pg, "colega").Count()}");
		List<string> ruim = [.. verdade.Alunos.SelectMany(a => ProblemasDoCartaoDeAluno(Lido(menu, "aluno", a.Quem.Nome), a)),
							 .. ProblemasDoCartaoDoMestre(Lido(menu, "mestre", "Bardock"), m),
							 .. m.Colegas.SelectMany((nome, i) => ProblemasDoCartaoDeColega(Lido(menu, "colega", nome), m, i)),
							 .. ProblemasDoConjunto(LerConjunto(pg), verdade.Alunos)];
		Afirmar("...e cada cartao das duas partes esta no contrato dele (os meus alunos NAO entram no ranking do meu mestre, nem os colegas no meu)",
				ruim.Count == 0, string.Join("; ", ruim));
		Afirmar("...e continua sem numero de poder nenhum", PoderEscrito(TextosDe(pg)).Count == 0, string.Join("; ", PoderEscrito(TextosDe(pg))));

		RolarPraCima(menu);
		yield return 0.3;
		Fotografar("alunos-8-as-duas-partes", "8 mestre de uns, aluno de outro");
		TrazerPraTela(menu, Cartoes(pg, "colega").LastOrDefault());
		yield return 0.3;
		Fotografar("alunos-8b-as-duas-partes-fim", "8b a parte de aluno");
		DevolverOArranjo();
		yield return 0.4;
	}

	// ---------------------------------------------------------------- 4. AS REGRAS SABEM REPROVAR
	private void AsRegrasSabemReprovar()
	{
		Nota("--- 4. o contrato sabe ficar vermelho ---");
		var gohan = new PessoaNoPainel("111", "Gohan", "Saiyan", true, 0, 86_400 * 12);
		EnsinamentoNoPainel forma = new(TipoDeEnsinamento.Forma, "ssj1", 86_400 * 10, 1, true, ProgressoDeAluno.DaForma(true, 62));
		EnsinamentoNoPainel licao = new(TipoDeEnsinamento.Licao, "/datum/skill/flying", 86_400 * 8, 0, false, ProgressoDeAluno.DaLicao(true, 3, 2, 50, 100));
		var aluno = new AlunoNoPainel(gohan, [forma, licao]);
		double media = ProgressoDeAluno.Geral([forma.Progresso, licao.Progresso]).Media;

		EnsinamentoLido Bloco(EnsinamentoNoPainel e, string comQuem, string lugar = "", string pilulaDoLugar = "")
		{
			(string pilula, string rotulo, string valor, double razao) = Esperado(e, comQuem);
			List<string> pilulas = [e.Tipo == TipoDeEnsinamento.Forma ? "FORMA" : "LIÇÃO", pilula];
			if (pilulaDoLugar.Length > 0) pilulas.Add(pilulaDoLugar);
			return new EnsinamentoLido(MenuJogo.ChaveDoEnsinamento(e.Tipo, e.Id), "Nome", pilulas, [new LinhaLida(rotulo, valor, razao)], lugar, "");
		}
		var certo = new CartaoLido("aluno", "Gohan", ["ONLINE"], "Saiyan  ·  seu aluno há 12 dias", "sentida", "80%", $"{media * 100:0}%", media,
								   [Bloco(forma, "COM VOCÊ"), Bloco(licao, "COM VOCÊ")],
								   [new BotaoLido("ensinar", "Ajudar a transformar", false, false), new BotaoLido("dispensar", "Dispensar aluno", false, false)],
								   true, false, ["datas"], ["Gohan", "maestria", "62%", "poder relativo", "80%"]);
		Afirmar("CONTROLE: o cartao certo de um aluno nao tem problema nenhum (a regra nao reprova tudo)",
				ProblemasDoCartaoDeAluno(certo, aluno).Count == 0, string.Join("; ", ProblemasDoCartaoDeAluno(certo, aluno)));

		// ---- a forca ----
		Injeta("pega a linha 'Battle Power' num cartao (o numero que esta tela nao pode ter)",
			   ProblemasDoCartaoDeAluno(certo with { Textos = [.. certo.Textos, "Battle Power", "2.400.000"] }, aluno).Count > 0);
		Injeta("...pega um numero grande solto ('2400000'), mesmo sem o nome do medidor",
			   ProblemasDoCartaoDeAluno(certo with { Textos = [.. certo.Textos, "2400000"] }, aluno).Count > 0);
		Injeta("...pega o 'poder relativo' escrito como NUMERO e nao como porcentagem ('2.400.000')",
			   ProblemasDoCartaoDeAluno(certo with { PoderRelativo = "2.400.000" }, aluno).Count > 0);
		Injeta("...pega a leitura de forca de quem NAO esta no mundo (a tela dando de graca o que o sentido de Ki nao alcanca)",
			   ProblemasDoCartaoDeAluno(certo with { PilulasDoTopo = ["OFFLINE"] }, aluno with { Quem = gohan with { Online = false } }).Count > 0);
		Injeta("...pega a linha de poder relativo num cartao que diz que o sentido de Ki nao le a pessoa (ou que falta a skill)",
			   ProblemasDoCartaoDeAluno(certo with { Forca = "nao-sentida" }, aluno).Count > 0 && ProblemasDoCartaoDeAluno(certo with { Forca = "sem-sense" }, aluno).Count > 0);
		Afirmar("CONTROLE: as porcentagens e os tempos da tela NAO sao 'numero de poder' ('62%', 'há 12 dias', '2 de 3', '400%', 'você tentou 3 vezes')",
				PoderEscrito(["62%", "seu aluno há 12 dias", "2 de 3", "400%", "você tentou 3 vezes", "4 de 5", "poder relativo"]).Count == 0,
				string.Join("; ", PoderEscrito(["62%", "seu aluno há 12 dias", "2 de 3", "400%", "você tentou 3 vezes", "4 de 5", "poder relativo"])));
		Injeta("a regra dos textos pega 'BP 3.000.000', 'Battle Power', 'poder de luta: 9001' e '18000'",
			   PoderEscrito(["BP 3.000.000"]).Count == 1 && PoderEscrito(["Battle Power"]).Count == 1 && PoderEscrito(["poder de luta: 9001"]).Count == 1
			   && PoderEscrito(["18000"]).Count == 1);

		// ---- o progresso ----
		Injeta("pega a maestria escrita diferente da do servidor (61% onde ele guarda 62%)",
			   ProblemasDoCartaoDeAluno(certo with { Ensinamentos = [Bloco(forma with { Progresso = ProgressoDeAluno.DaForma(true, 61) }, "COM VOCÊ"), certo.Ensinamentos[1]] }, aluno).Count > 0);
		Injeta("...pega a BARRA que nao acompanha o numero (62% escrito, barra pela metade)",
			   ProblemasDoCartaoDeAluno(certo with { Ensinamentos = [certo.Ensinamentos[0] with { Linhas = [new LinhaLida("maestria", "62%", 0.5)] }, certo.Ensinamentos[1]] }, aluno).Count > 0);
		Injeta("...pega 'DESPERTOU COM VOCÊ' numa forma que o aluno ainda nao despertou",
			   ProblemasDoCartaoDeAluno(certo, aluno with { Ensinamentos = [forma with { Progresso = ProgressoDeAluno.DaForma(false, 0) }, licao] }).Count > 0);
		Injeta("...pega um ensinamento A MAIS no cartao (o que o aluno aprendeu com outra pessoa nao e 'o que voce ensinou')",
			   ProblemasDoCartaoDeAluno(certo with { Ensinamentos = [.. certo.Ensinamentos, Bloco(licao with { Id = "/datum/skill/outra" }, "COM VOCÊ")] }, aluno).Count > 0);
		Injeta("...pega os ensinamentos fora da ordem em que o mestre os deu",
			   ProblemasDoCartaoDeAluno(certo with { Ensinamentos = [certo.Ensinamentos[1], certo.Ensinamentos[0]] }, aluno).Count > 0);
		Injeta("...pega o progresso geral errado, e a chave crua no lugar do nome ('/datum/skill/flying')",
			   ProblemasDoCartaoDeAluno(certo with { Geral = "99%" }, aluno).Count > 0
			   && ProblemasDoCartaoDeAluno(certo with { Ensinamentos = [certo.Ensinamentos[0], certo.Ensinamentos[1] with { Nome = "/datum/skill/flying" }] }, aluno).Count > 0);

		// ---- os gestos ----
		Injeta("pega o 'Ajudar a transformar' ACESO pra quem esta fora do mundo, e o cartao de aluno sem o botao de dispensar",
			   ProblemasDoCartaoDeAluno(certo with { PilulasDoTopo = ["OFFLINE"], Forca = "offline", PoderRelativo = "" }, aluno with { Quem = gohan with { Online = false, VistoHaSegundos = 0 } }).Count > 0
			   && ProblemasDoCartaoDeAluno(certo with { Botoes = [certo.Botoes[0]] }, aluno).Count > 0);

		// ---- o cartao do mestre e o do colega ----
		var bardock = new PessoaNoPainel("222", "Bardock", "Saiyan", true, 0, 86_400 * 9);
		EnsinamentoNoPainel minha = forma with { Progresso = ProgressoDeAluno.DaForma(true, 30) };
		var mestre = new MestreNoPainel(bardock,
			[new EnsinamentoDoMestre(minha, [new ColegaNoEnsinamento(1, ProgressoDeAluno.DaForma(true, 62)), new ColegaNoEnsinamento(0, ProgressoDeAluno.DaForma(false, 0))])],
			["Raditz", "Vegeta"]);
		double minhaMedia = ProgressoDeAluno.Geral([minha.Progresso]).Media;
		var doMestre = new CartaoLido("mestre", "Bardock", ["ONLINE"], "Saiyan  ·  seu mestre há 9 dias", "sentida", "400%", $"{minhaMedia * 100:0}%", minhaMedia,
									  [Bloco(minha, "COM O MESTRE", "2/3", "2º de 3")], [new BotaoLido("largar", "Deixar meu mestre", false, false)],
									  true, false, ["datas"], ["Bardock", "400%"]);
		Afirmar("CONTROLE: o cartao certo do mestre nao tem problema nenhum", ProblemasDoCartaoDoMestre(doMestre, mestre).Count == 0,
				string.Join("; ", ProblemasDoCartaoDoMestre(doMestre, mestre)));
		Injeta("pega a POSICAO errada entre os colegas ('1º de 3' pra quem esta atras do Vegeta)",
			   ProblemasDoCartaoDoMestre(doMestre with { Ensinamentos = [Bloco(minha, "COM O MESTRE", "1/3", "1º de 3")] }, mestre).Count > 0);
		Injeta("...pega a posicao que conta um colega A MENOS ('2º de 2')",
			   ProblemasDoCartaoDoMestre(doMestre with { Ensinamentos = [Bloco(minha, "COM O MESTRE", "2/2", "2º de 2")] }, mestre).Count > 0);
		Injeta("...pega o cartao do mestre com um botao de MESTRE ('Dispensar aluno') ou sem o de deixar o mestre",
			   ProblemasDoCartaoDoMestre(doMestre with { Botoes = [.. doMestre.Botoes, new BotaoLido("dispensar", "Dispensar aluno", false, false)] }, mestre).Count > 0
			   && ProblemasDoCartaoDoMestre(doMestre with { Botoes = [] }, mestre).Count > 0);

		EnsinamentoLido par = new(MenuJogo.ChaveDoEnsinamento(TipoDeEnsinamento.Forma, "ssj1"), "Nome", ["FORMA"],
								  [new LinhaLida("Vegeta", "maestria 62%", 0.62), new LinhaLida("você", "maestria 30%", 0.30)], "", "");
		var colega = new CartaoLido("colega", "Vegeta", [], "", "", "", "", -1, [par], [], true, false, ["em-comum"], ["Vegeta", "maestria 62%"]);
		Afirmar("CONTROLE: o cartao certo de um colega nao tem problema nenhum", ProblemasDoCartaoDeColega(colega, mestre, 1).Count == 0,
				string.Join("; ", ProblemasDoCartaoDeColega(colega, mestre, 1)));
		Injeta("pega a FORCA no cartao de um colega (a linha de poder relativo, ou so a frase)",
			   ProblemasDoCartaoDeColega(colega with { Forca = "sentida", PoderRelativo = "150%" }, mestre, 1).Count > 0
			   && ProblemasDoCartaoDeColega(colega with { Forca = "nao-sentida" }, mestre, 1).Count > 0);
		Injeta("...pega a pilula de presenca, a linha de raca/datas e um botao no cartao de um colega",
			   ProblemasDoCartaoDeColega(colega with { PilulasDoTopo = ["ONLINE"] }, mestre, 1).Count > 0
			   && ProblemasDoCartaoDeColega(colega with { Datas = "Saiyan  ·  aluno há 20 dias" }, mestre, 1).Count > 0
			   && ProblemasDoCartaoDeColega(colega with { Botoes = [new BotaoLido("dispensar", "Dispensar aluno", false, false)] }, mestre, 1).Count > 0);
		Injeta("...pega um ensinamento que NAO e em comum no cartao do colega (o que o mestre ensinou so a ele)",
			   ProblemasDoCartaoDeColega(colega with { Ensinamentos = [par, par with { Chave = "2:/datum/skill/so_dele" }] }, mestre, 1).Count > 0);
		Injeta("...pega a barra do colega com o progresso de OUTRO (o Vegeta com o 'ainda não despertou' do Raditz)",
			   ProblemasDoCartaoDeColega(colega with { Ensinamentos = [par with { Linhas = [new LinhaLida("Vegeta", "ainda não despertou", 0), par.Linhas[1]] }] }, mestre, 1).Count > 0);

		// ---- o conjunto ----
		var trunks = new AlunoNoPainel(new PessoaNoPainel("333", "Trunks", "Saiyan", true, 0, 0), [forma with { Progresso = ProgressoDeAluno.DaForma(true, 12) }]);
		List<AlunoNoPainel> dois = [aluno, trunks];
		List<(string Chave, List<LinhaLida> Linhas)> conjunto =
		[
			("1:ssj1", [new LinhaLida("1º  Gohan", "maestria 62%", 0.62), new LinhaLida("2º  Trunks", "maestria 12%", 0.12)]),
			("2:/datum/skill/flying", [new LinhaLida("1º  Gohan", EsperadoEmUmaFrase(TipoDeEnsinamento.Licao, licao.Progresso), RazaoDe(licao.Progresso))]),
		];
		Afirmar("CONTROLE: o conjunto certo nao tem problema nenhum", ProblemasDoConjunto(conjunto, dois).Count == 0, string.Join("; ", ProblemasDoConjunto(conjunto, dois)));
		Injeta("pega o ranking com a ORDEM trocada (o Trunks, com 12%, na frente do Gohan, com 62%)",
			   ProblemasDoConjunto([("1:ssj1", [new LinhaLida("1º  Trunks", "maestria 12%", 0.12), new LinhaLida("2º  Gohan", "maestria 62%", 0.62)]), conjunto[1]], dois).Count > 0);
		Injeta("...pega um aluno A MAIS num ranking (quem nao recebeu aquele ensinamento), e um ranking que falta",
			   ProblemasDoConjunto([conjunto[0], ("2:/datum/skill/flying", [.. conjunto[1].Linhas, new LinhaLida("2º  Trunks", "nível 0 de 3", 0)])], dois).Count > 0
			   && ProblemasDoConjunto([conjunto[0]], dois).Count > 0);
		Injeta("...e pega o conjunto desenhado pra UM aluno so (nao ha quem comparar)", ProblemasDoConjunto(conjunto, [aluno]).Count > 0);

		// ---- a procura na lista do sentido de Ki (a funcao que o cartao usa) ----
		// Quem eu NAO conheco vem pela assinatura; quem eu conheco vem so pelo NOME, e de quem e o nome quem diz e a
		// minha lista de conhecidos (a da aba People).
		List<Protocol.PresencaState> lista =
		[
			new() { Nome = "", Assinatura = "111", PoderRelativo = 80, Bp = double.NaN },
			new() { Nome = "Yamcha", Assinatura = "", PoderRelativo = 55, Bp = double.NaN },
			new() { Nome = "Chaos", Assinatura = "", PoderRelativo = 20, Bp = double.NaN },
			new() { Nome = "Chaos", Assinatura = "", PoderRelativo = 21, Bp = double.NaN },
			new() { Nome = "Gohan", Assinatura = "", PoderRelativo = 33, Bp = double.NaN },
		];
		(string, string)[] conhecidos = [("444", "Yamcha"), ("555", "Chaos"), ("666", "Gohan")];
		Afirmar("a procura acha quem eu NAO conheco pela assinatura (80%) e quem eu conheco pelo nome (55%: o unico Yamcha que eu conheco e ele)",
				GameClient.PoderRelativoNaLista(lista, false, "111", "Gohan", conhecidos) == 80f
				&& GameClient.PoderRelativoNaLista(lista, false, "444", "Yamcha", conhecidos) == 55f);
		Afirmar("**um HOMONIMO nao empresta a leitura**: a presenca 'Gohan' (33%) e do Gohan que eu conheco (o 666), e nao do meu aluno Gohan (o 111, que eu sinto "
				+ "pela assinatura: 80%) -- nem de um terceiro Gohan (o 777), que nao esta na lista",
				GameClient.PoderRelativoNaLista(lista, false, "666", "Gohan", conhecidos) == 33f
				&& GameClient.PoderRelativoNaLista(lista, false, "111", "Gohan", conhecidos) == 80f
				&& GameClient.PoderRelativoNaLista(lista, false, "777", "Gohan", conhecidos) == null);
		Afirmar("...um nome que eu NAO tenho na lista de conhecidos nao casa com presenca nenhuma, e quem nao esta na lista do sentido nao tem leitura",
				GameClient.PoderRelativoNaLista(lista, false, "444", "Yamcha", []) == null
				&& GameClient.PoderRelativoNaLista(lista, false, "888", "Tenshinhan", [("888", "Tenshinhan")]) == null);
		Afirmar("...com DUAS presencas do mesmo nome (ou dois conhecidos com ele) nao da pra dizer qual e a pessoa: nenhuma leitura",
				GameClient.PoderRelativoNaLista(lista, false, "555", "Chaos", conhecidos) == null
				&& GameClient.PoderRelativoNaLista(lista, false, "444", "Yamcha", [("444", "Yamcha"), ("445", "Yamcha")]) == null);
		Afirmar("...e no modo SCOUTER a funcao nao devolve nada, mesmo com a pessoa na lista (a tela nao e um medidor)",
				GameClient.PoderRelativoNaLista(lista, true, "111", "Gohan", conhecidos) == null
				&& GameClient.PoderRelativoNaLista([new() { Nome = "Yamcha", Assinatura = "", PoderRelativo = float.NaN, Bp = 2_400_000 }], true, "444", "Yamcha", conhecidos) == null
				&& GameClient.PoderRelativoNaLista([new() { Nome = "Yamcha", Assinatura = "", PoderRelativo = float.NaN, Bp = 2_400_000 }], false, "444", "Yamcha", conhecidos) == null);
		Afirmar("os quatro motivos de nao haver leitura, na ordem: fora do mundo > scouter ligado > sem a skill > o sentido nao le a pessoa -- e nenhuma das frases tem numero",
				MenuJogo.CasoSemLeitura(false, true, true) == "offline" && MenuJogo.CasoSemLeitura(true, true, true) == "scouter"
				&& MenuJogo.CasoSemLeitura(true, false, false) == "sem-sense" && MenuJogo.CasoSemLeitura(true, true, false) == MenuJogo.CasoNaoSentida
				&& MenuJogo.FraseDaForcaNaPagina(true, false).Length == 0
				&& PoderEscrito([MenuJogo.FraseDeNaoSentida, MenuJogo.FraseDaForcaNaPagina(false, false), MenuJogo.FraseDaForcaNaPagina(true, true)]).Count == 0);

		// ---- onde o motivo se escreve: o do jogador UMA vez no alto, o da pessoa no cartao dela ----
		CartaoLido semSkill = certo with { Forca = "sem-sense", PoderRelativo = "" };
		Afirmar("CONTROLE: o cartao de quem NAO sabe sentir Ki (sem linha e sem frase de forca) nao tem problema, e o alto da pagina com UMA nota tambem nao",
				ProblemasDoCartaoDeAluno(semSkill, aluno).Count == 0 && ProblemasDaForcaNoAlto(["sem-sense"], false, false).Count == 0
				&& ProblemasDaForcaNoAlto(["scouter"], false, true).Count == 0 && ProblemasDaForcaNoAlto([], true, false).Count == 0,
				string.Join("; ", ProblemasDoCartaoDeAluno(semSkill, aluno)));
		Injeta("pega a frase do motivo que e do JOGADOR repetida num cartao ('força: você ainda não sabe sentir Ki' em cada aluno)",
			   ProblemasDoCartaoDeAluno(semSkill with { Textos = [.. semSkill.Textos, "força: você ainda não sabe sentir Ki"] }, aluno).Count > 0);
		Injeta("...pega o cartao de quem o sentido NAO le (esta no mundo, eu sinto Ki) sem a frase que diz isso",
			   ProblemasDoCartaoDeAluno(semSkill with { Forca = "nao-sentida" }, aluno).Count > 0
			   && ProblemasDoCartaoDeAluno(semSkill with { Forca = "nao-sentida", Textos = [.. semSkill.Textos, MenuJogo.FraseDeNaoSentida] }, aluno).Count == 0);
		Injeta("...e a regra do alto da pagina pega a nota REPETIDA, a nota que falta, a nota de quem sente Ki sem scouter, e a nota errada com o scouter ligado",
			   ProblemasDaForcaNoAlto(["sem-sense", "sem-sense"], false, false).Count > 0 && ProblemasDaForcaNoAlto([], false, false).Count > 0
			   && ProblemasDaForcaNoAlto(["sem-sense"], true, false).Count > 0 && ProblemasDaForcaNoAlto(["sem-sense"], false, true).Count > 0);
	}
}
