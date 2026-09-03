using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DO VERB DE DEGRAU NO CLIENTE (`--diagdegrau`) ============================
/// O DEFEITO DE ORIGEM: um verb concedido por NIVEL (o Hokuto Hyakuretsu Ken no nivel 2 do Hokuto no
/// Shinken) ou por CASA (o Taunt do degrau 2 da Holy Trinity) tinha corpo no servidor, porta no
/// `niveis.json` e NENHUM botao no cliente -- o menu montava os botoes so do catalogo (`Skill.Verbos`),
/// e o nivel das skills nao viaja. Um verb sem botao e inalcancavel, e "portado" sem alcance e o pedido
/// do dono ("as skills que ja estao na tree mas nao tiveram efeito portado") nao atendido: 16 golpes do
/// lote G10 e 13 do G7 estavam assim.
///
/// ============================ POR QUE UM CLIENTE QUE DISCA, E SEM JANELA ============================
/// O botao nasce do `S2C.Skills` (a cauda de verbs ativos, `Protocol.PorEstadoDeSkills`). Num `--host`
/// o servidor mora no mesmo processo e a lista poderia ser lida por tabela do vizinho; so um cliente
/// que DISCA mede o que chega pelo FIO. E ela nao le pixel nenhum: o que se mede e o REGISTRO de verbs
/// (`Verbos.PorChave`, de onde o menu P e as teclas tiram os botoes) e o que o servidor RESPONDE
/// quando o botao e apertado pela acao dele mesmo (`Verbo.Acionar`).
///
/// ============================ AS TRES FAMILIAS ============================
///   F1  O VERB DE DEGRAU: o servidor sobe (`--nivelteste`) o Hokuto no Shinken ao nivel 2 no login; o
///       botao `hab:Hokuto_Hyakuretsu_Ken` aparece, ACESO, e apertado chega ao lote G10 (a resposta nao
///       e "voce nao sabe" nem "ainda nao tem efeito"). O Revenge_Demon (Beserker nivel 3, nao
///       concedido) NAO tem botao -- o controle negativo.
///   F2  A TRINDADE PONTA A PONTA, pelo funil de producao: o personagem COMPRA (`C2S.Aprender`) o que
///       o Corpo e o Bodybuilding oferecem ate a Holy Trinity entrar; ESCOLHE a casa 1 (Van-sama) pelo
///       verb `skill_escolha`, o mesmo que a ficha do menu manda; o servidor a poe no nivel 2
///       (`--nivelteste` na compra); e SO o Taunt vira botao -- Slap e Counter_Taunt (as outras casas)
///       nao. Apertado, o Taunt responde. Depois a Grace e comprada e o servidor NAO pede casa nenhuma:
///       ela segue a Trindade.
///   F3  O EFEITO SEM DESENHO (a divida nomeada em PROXIMOS-SISTEMAS.md): o Freeze (lote G11) manda o
///       id `timefreeze`; o cliente o recebe (`EfeitosAtivos`), nao estoura, nao desenha -- e diz isso
///       UMA vez no console (`[efeito] ...`).
///
/// COMO RODAR -- pelo `testar-o-degrau-no-cliente.bat` (dois processos, headless, APPDATA desviado):
///     Godot --headless --path . --server --port 7981 --marcosteste 60
///           --skillteste /datum/skill/general/timefreeze,/datum/skill/Assassain/Hokuto_no_Shinken
///           --nivelteste /datum/skill/Assassain/Hokuto_no_Shinken=2,/datum/skill/Bodybuilding/TheHolyTrinity=2
///     Godot --headless --path . --connect 127.0.0.1 --rede 7981 --diagdegrau --raca Saiyan --conta bancada_degrau --nome Degrau
/// O `--nivelteste` NAO CONCEDE: ele poe no nivel a skill que o livro ja tem (o Hokuto vem do
/// `--skillteste`) ou que acabou de ser COMPRADA (a Trindade, pelo funil -- e a F2 cobra que a compra
/// aconteceu de verdade: zero compras e fraude). A SUBIDA DE NIVEL EM SI (o efetor cruzando o degrau)
/// nao e desta bancada: e da `--arvoreteste` e da `niveis`. Aqui o nivel e dado pra que a pergunta seja
/// UMA: o verb concedido vira botao e funciona?
/// ==================================================================================================================
/// </summary>
public partial class RoboDoDegrau : Node
{
	private const string Hokuto = "Hokuto_Hyakuretsu_Ken";
	private const string Trinity = "/datum/skill/Bodybuilding/TheHolyTrinity";
	private const string Grace = "/datum/skill/Bodybuilding/Grace";
	private const string ArvBody = "/datum/skill/tree/Body";
	private const string ArvBodybuilding = "/datum/skill/tree/Bodybuilding";
	private const int MaxCompras = 60;

	private int _ok, _falha;
	private readonly List<string> _reprovadas = [];
	/// <summary>Tudo que chegou pelo chat (o canal Sistema e as falas): e por aqui que o servidor responde.</summary>
	private readonly List<string> _ouvido = [];

	/// <summary>As frases com que o despacho diz "isto nao existe" -- nenhuma conta como resposta de efeito.</summary>
	private static readonly string[] FrasesDeAusencia =
	[
		"nao sabe", "não sabe", "ainda nao tem efeito", "ainda não tem efeito",
		"nao foi portado", "não foi portado", "desconhecida",
	];

	private static void Nota(string linha) => GD.Print("[degrau] " + linha);
	private static GameClient? C => GameClient.Instance;

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	public override void _Ready() => _ = Rodar();

	private async System.Threading.Tasks.Task Rodar()
	{
		Nota("==================================================================================");
		Nota(" O VERB DE DEGRAU (E DE CASA) VIRA BOTAO NUM CLIENTE QUE DISCA");
		Nota("==================================================================================");
		if (C is { } c0) c0.Falou += (_, quem, texto) => _ouvido.Add((quem.Length > 0 ? quem + ": " : "") + texto);

		bool pronto = await Ate(() => C is { Connected: true } c && c.Atributos.Raca is { Length: > 0 }
										&& c.SkillsArvores.Count > 0 && MenuJogo.Instancia != null, 90);
		Checa("o mundo chegou (conexao, raca na ficha lenta, estado das arvores no pacote de skills)", pronto);
		if (!pronto || C is not { } cli) { Placar(); GetTree().Quit(2); return; }

		SkillCatalog? cat = MenuJogo.CatalogoPublico();
		Checa("o catalogo de skills esta carregado neste cliente", cat != null);
		if (cat == null) { Placar(); GetTree().Quit(2); return; }

		try
		{
			await F1(cli, cat);
			await F2(cli, cat);
			await F3(cli);
		}
		catch (Exception e)
		{
			Checa($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}

		Placar();
		GetTree().Quit(_falha == 0 ? 0 : 1);
	}

	// =====================================================================
	// F1) O VERB CONCEDIDO POR DEGRAU
	// =====================================================================
	private async System.Threading.Tasks.Task F1(GameClient cli, SkillCatalog cat)
	{
		Nota("-- F1) O VERB CONCEDIDO POR DEGRAU VIRA BOTAO, ACESO, E RESPONDE --");

		bool chegou = await Ate(() => cli.VerbosAtivos.Contains(Hokuto, StringComparer.OrdinalIgnoreCase), 20);
		Checa($"o pacote de skills traz `{Hokuto}` entre os verbs ativos (o servidor pos o Hokuto no Shinken no nivel 2)",
			  chegou, string.Join(",", cli.VerbosAtivos));

		// O MENU REMONTA OS BOTOES NUM QUADRO ADIADO (`MenuJogo.AoMudarVerbos`): espera-se o registro.
		bool botao = await Ate(() => Verbos.PorChave("hab:" + Hokuto) != null, 10);
		Verbo? v = Verbos.PorChave("hab:" + Hokuto);
		Checa($"...e o botao `hab:{Hokuto}` existe no registro de verbs (de onde o menu P e as teclas tiram os botoes)",
			  botao && v != null);
		Checa("...ACESO -- a tecnica esta portada, nao e o botao cinza de 'nao portada'",
			  v is { PodeAgora: true } && !v.Nome.Contains("nao portada", StringComparison.OrdinalIgnoreCase), v?.Nome ?? "(sem botao)");
		Checa("...e nenhuma skill aprendida o concede por `Skill.Verbos`: SO o degrau o concede (o defeito de origem)",
			  cli.SkillsAprendidas.All(p => cat.Get(p)?.Verbos.Contains(Hokuto, StringComparer.OrdinalIgnoreCase) != true));
		Checa("controle: o Revenge_Demon (Beserker nivel 3, NAO concedido) nao esta no pacote nem tem botao",
			  !cli.VerbosAtivos.Contains("Revenge_Demon") && Verbos.PorChave("hab:Revenge_Demon") == null);

		int antes = _ouvido.Count;
		v?.Acionar?.Invoke();   // a ACAO DO PROPRIO BOTAO -- o `SendHabilidade` que o clique manda
		bool respondeu = await Ate(() => _ouvido.Count > antes, 8);
		string resposta = string.Join(" | ", _ouvido.Skip(antes));
		Checa("apertar o botao chega ao lote G10: o servidor RESPONDE, e nao com 'voce nao sabe' nem 'ainda nao tem efeito'",
			  respondeu && !FrasesDeAusencia.Any(f => resposta.Contains(f, StringComparison.OrdinalIgnoreCase)), resposta);
	}

	// =====================================================================
	// F2) A TRINDADE PONTA A PONTA
	// =====================================================================
	private async System.Threading.Tasks.Task F2(GameClient cli, SkillCatalog cat)
	{
		Nota("-- F2) A TRINDADE: comprar pelo funil, escolher a casa, o verb DA CASA vira botao e responde; a Grace segue --");

		string raca = cli.Atributos.Raca ?? "";
		string classe = cli.Sheet.Class ?? "None";
		var galhos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string arv in new[] { ArvBody, ArvBodybuilding })
			foreach (string g in cat.Get(arv)?.Galhos ?? []) galhos.Add(g);

		// O FUNIL DE PRODUCAO: a mesma pergunta que a ficha do menu faz (`SkillBook.Avaliar` sobre o
		// estado que veio no pacote) e o mesmo pedido que o botao Comprar manda (`C2S.Aprender`).
		Checa("o personagem NASCE sem a Trindade (o `--nivelteste` nao concede: a compra tem que acontecer de verdade)",
			  !cli.SkillsAprendidas.Contains(Trinity), string.Join(",", cli.SkillsAprendidas.Where(p => p.Contains("Bodybuilding"))));
		List<string> compradas = await ComprarAte(cli, cat, galhos, raca, classe, Trinity);
		Checa($"comprando pelo funil (C2S.Aprender) o que o Corpo e o Bodybuilding oferecem, a Holy Trinity entra "
			  + $"({compradas.Count} compras: {string.Join(", ", compradas)})",
			  cli.SkillsAprendidas.Contains(Trinity) && compradas.Contains("TheHolyTrinity"), $"marcos {cli.MarcosLivres}/{cli.MarcosTotais}");
		if (!cli.SkillsAprendidas.Contains(Trinity)) return;

		Checa("...e antes da escolha o pacote NAO traz Taunt, Slap nem Counter_Taunt (nivel 2, `TrinityType` nulo: o switch do DM nao entra em ramo nenhum)",
			  !cli.VerbosAtivos.Contains("Taunt") && !cli.VerbosAtivos.Contains("Slap") && !cli.VerbosAtivos.Contains("Counter_Taunt"),
			  string.Join(",", cli.VerbosAtivos));

		cli.SendVerbo("skill_escolha", $"{Trinity} 1");   // Van-sama -- o mesmo verb que a ficha do menu manda
		bool taunt = await Ate(() => cli.VerbosAtivos.Contains("Taunt"), 10);
		Checa("escolhida a casa 1 (Van-sama) pelo `skill_escolha`, o pacote seguinte traz SO o Taunt -- nem Slap nem Counter_Taunt (outras casas)",
			  taunt && !cli.VerbosAtivos.Contains("Slap") && !cli.VerbosAtivos.Contains("Counter_Taunt"),
			  string.Join(",", cli.VerbosAtivos));

		bool botao = await Ate(() => Verbos.PorChave("hab:Taunt") != null, 10);
		Verbo? v = Verbos.PorChave("hab:Taunt");
		Checa("...e o botao `hab:Taunt` existe e esta aceso; `hab:Slap` e `hab:Counter_Taunt` nao existem",
			  botao && v is { PodeAgora: true } && Verbos.PorChave("hab:Slap") == null && Verbos.PorChave("hab:Counter_Taunt") == null,
			  v?.Nome ?? "(sem botao)");

		int antes = _ouvido.Count;
		v?.Acionar?.Invoke();
		bool respondeu = await Ate(() => _ouvido.Count > antes, 8);
		string resposta = string.Join(" | ", _ouvido.Skip(antes));
		Checa("apertar o Taunt chega ao lote G10 e o servidor responde (a fala ou a recusa por Ki/recarga -- nunca 'voce nao sabe')",
			  respondeu && !FrasesDeAusencia.Any(f => resposta.Contains(f, StringComparison.OrdinalIgnoreCase)), resposta);

		// A GRACE SEGUE A TRINDADE: comprada, o servidor nao pede casa nenhuma (o `EscolhaSegue` do
		// catalogo entra na casa da lider -- `EfeitosDeSkill.CasaEscolhida`).
		int antesDaGrace = _ouvido.Count;
		List<string> maisCompras = await ComprarAte(cli, cat, galhos, raca, classe, Grace);
		string ditoNaGrace = string.Join(" | ", _ouvido.Skip(antesDaGrace).Where(t => t.Contains("Grace", StringComparison.OrdinalIgnoreCase)));
		Checa($"a Grace entra pelo mesmo funil ({maisCompras.Count} compras a mais) e o servidor NAO pede escolha: ela segue a casa da Trindade",
			  cli.SkillsAprendidas.Contains(Grace)
			  && !ditoNaGrace.Contains("escolha uma casa", StringComparison.OrdinalIgnoreCase),
			  ditoNaGrace.Length > 0 ? ditoNaGrace : $"marcos {cli.MarcosLivres}/{cli.MarcosTotais}; compradas: {string.Join(", ", maisCompras)}");
		Checa("...e com a Grace o Taunt continua sendo o unico verb da Trindade no pacote",
			  cli.VerbosAtivos.Contains("Taunt") && !cli.VerbosAtivos.Contains("Slap") && !cli.VerbosAtivos.Contains("Counter_Taunt"));
	}

	/// <summary>
	/// COMPRA, UMA POR VEZ E ESPERANDO O PACOTE, ate a skill-alvo entrar no livro: a alvo se o veredito ja
	/// e `Pode`, senao a mais barata (menor tier) das duas arvores que o veredito libera. Devolve o que
	/// comprou (nomes curtos), pro placar.
	/// </summary>
	private async System.Threading.Tasks.Task<List<string>> ComprarAte(GameClient cli, SkillCatalog cat, HashSet<string> galhos,
																	  string raca, string classe, string alvoFinal)
	{
		var compradas = new List<string>();
		int voltas = 0;
		while (!cli.SkillsAprendidas.Contains(alvoFinal) && voltas++ < MaxCompras)
		{
			SkillBook livro = LivroDoPacote(cli);
			string? alvo = null;
			if (livro.Avaliar(cat, alvoFinal, raca, classe, cli.SouVilao).Motivo == Recusa.Pode) alvo = alvoFinal;
			else
				foreach (Skill s in cat.Todas.Where(s => !s.Arvore && galhos.Contains(s.Path))
											 .OrderBy(s => s.Tier).ThenBy(s => s.Path, StringComparer.Ordinal))
					if (!livro.Sabe(s.Path) && livro.Avaliar(cat, s.Path, raca, classe, cli.SouVilao).Motivo == Recusa.Pode)
					{ alvo = s.Path; break; }
			if (alvo == null) { Nota($"   nada mais compravel no Corpo/Bodybuilding antes de {alvoFinal.Split('/')[^1]} (marcos {cli.MarcosLivres})"); break; }

			cli.SendAprender(alvo);
			string alvoLocal = alvo;
			bool entrou = await Ate(() => cli.SkillsAprendidas.Contains(alvoLocal), 10);
			if (!entrou) { Nota($"   a compra de {alvo} nao voltou no pacote em 10 s"); break; }
			compradas.Add(alvo.Split('/')[^1]);
		}
		return compradas;
	}

	/// <summary>Um livro montado SO do que veio no fio -- o mesmo `SincronizarLivro` do menu.</summary>
	private static SkillBook LivroDoPacote(GameClient cli)
	{
		var livro = new SkillBook { MarcosTotais = cli.MarcosTotais, MarcosLivres = cli.MarcosLivres };
		livro.Carregar(cli.SkillsAprendidas);
		livro.CarregarEstado(cli.SkillsDestravadas, cli.SkillsArvores);
		return livro;
	}

	// =====================================================================
	// F3) O EFEITO SEM DESENHO
	// =====================================================================
	private async System.Threading.Tasks.Task F3(GameClient cli)
	{
		Nota("-- F3) O EFEITO SEM DESENHO: o id chega, entra em EfeitosAtivos e o cliente nao estoura (divida nomeada) --");

		bool freeze = await Ate(() => Verbos.PorChave("hab:Freeze") != null, 10);
		Checa("o Freeze (`/datum/skill/general/timefreeze`, lote G11, dado pelo `--skillteste`) tem botao", freeze);

		int antes = _ouvido.Count;
		Verbos.PorChave("hab:Freeze")?.Acionar?.Invoke();
		bool chegou = await Ate(() => cli.EfeitosAtivos.Contains("timefreeze"), 8);
		string resposta = string.Join(" | ", _ouvido.Skip(antes));
		Checa("apertado, o id `timefreeze` chega pelo S2C.Efeito e entra em `EfeitosAtivos`: o cliente o RECEBE e nao o desenha (ver `[efeito]` no console)",
			  chegou, $"efeitos: {string.Join(",", cli.EfeitosAtivos)} | {resposta}");
		Checa("...e o servidor executou o verb (respondeu com o congelamento, nao com uma ausencia)",
			  resposta.Contains("congela", StringComparison.OrdinalIgnoreCase)
			  && !FrasesDeAusencia.Any(f => resposta.Contains(f, StringComparison.OrdinalIgnoreCase)), resposta);
		Checa("...e o cliente continua de pe depois do id sem desenho (nenhum estouro: o `default` do `AoCairEfeito` so anota)",
			  C is { Connected: true } && GetTree() != null);
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	private async System.Threading.Tasks.Task<bool> Ate(Func<bool> cond, double segundos)
	{
		double fim = Time.GetTicksMsec() / 1000.0 + segundos;
		while (Time.GetTicksMsec() / 1000.0 < fim)
		{
			if (cond()) return true;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		return cond();
	}

	private void Placar()
	{
		Nota("==================================================================================");
		Nota($" PLACAR: {_ok} OK, {_falha} FALHA");
		Nota("==================================================================================");
		foreach (string r in _reprovadas) Nota("  reprovada: " + r);
	}
}
