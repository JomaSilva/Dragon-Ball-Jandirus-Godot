using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DE ADMIN (`--diagadmin`). Prova, sem janela, que quem hospeda E administrador -- e
/// continua sendo depois de aprender uma skill.
///
/// ============================ POR QUE ESTA BANCADA PRECISA EXISTIR ============================
/// O defeito que ela persegue era invisivel de todas as maneiras que costumam funcionar:
///
///   * O SERVIDOR IMPRIMIA "entra como administrador" -- e era verdade, por doze linhas. O login
///     acendia `Poder.Admin` e logo depois chamava `AplicarPoderes`, que refaz `Poderes` do zero
///     a partir das skills e apagava a marca. Ler o log era ler a metade certa.
///   * NENHUMA DAS DUAS LINHAS ESTAVA ERRADA sozinha. So a ORDEM entre elas estava.
///   * A bancada do menu (`--diagmenu`) percorria as abas FIXAS, e a de admin nao e fixa: e a
///     unica que depende de um bit vindo do servidor. Justamente a que ninguem exercitava.
///
/// Por isso o teste nao pergunta ao servidor se ele acendeu o bit -- pergunta ao CLIENTE se o
/// bit chegou, que e onde a aba nasce. E depois manda aprender uma skill e pergunta de novo,
/// porque aprender e o gatilho que apagava tudo.
/// =============================================================================================
///
/// COMO RODAR (a mesma maquina, que e o que "host" quer dizer):
///     Godot --headless --path . --host --diagadmin --nome Admin --conta admteste
/// </summary>
public partial class RoboDeAdmin : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	/// <summary>O bit de admin ANTES de aprender uma skill. Ver o passo da regressao.</summary>
	private bool _tinhaAdminAntes;

	/// <summary>Quantas celulas de cenario estavam quebradas antes de mandar refazer.</summary>
	private int _quebradas;

	/// <summary>Quantas voltas de 0,6 s esperar por um estrago antes de dizer que nao houve.</summary>
	private const int EsperasPorEstrago = 50;
	private int _esperas;

	/// <summary>O verb que tem passo proprio e por isso fica de fora do disparo em massa.</summary>
	private const string RefazerCenario = "Restore Scenery";

	private static GameClient? C => GameClient.Instance;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || MenuJogo.Instancia is not { } menu) return;

		_t += delta;
		if (_t < 0.6) return;   // um passo a cada 0,6 s: da tempo de a resposta do servidor chegar
		_t = 0;

		switch (_passo++)
		{
			// -------------------------------------------------------------- o bit chegou?
			case 0:
				// A ficha LENTA e quem carrega os bits de poder. Se ela ainda nao chegou, espera.
				if (cli.Atributos.Raca is not { Length: > 0 }) { _passo = 0; return; }
				Conferir(cli.Atributos.Tem(Protocol.Poder.Admin),
					"o bit Poder.Admin chega ao cliente (host = admin)");
				_tinhaAdminAntes = cli.Atributos.Tem(Protocol.Poder.Admin);
				break;

			case 1:
				Conferir(Array.IndexOf(menu.AbasDeTeste, Verbos.Admin) >= 0,
					"a aba \"Admin\" existe na lista de abas do menu");
				break;

			// -------------------------------------------------------------- a aba desenha?
			case 2:
				menu.Abrir();
				menu.IrPara(Verbos.Admin);
				Conferir(true, "a aba Admin monta sem quebrar (painel + lista de verbs)");
				break;

			case 3:
			{
				int n = Verbos.Da(Verbos.Admin).Count();
				Conferir(n >= 20, $"a aba tem verbs de sobra ({n} registrados)");
				break;
			}

			// -------------------------------------------------------------- os verbs respondem?
			case 4:
			{
				// TODOS de uma vez. O que se testa aqui e a CADEIA -- pacote, `switch` do servidor,
				// resposta -- e nao o efeito de cada um: um verb que nao existisse no `switch`
				// devolveria "esse comando de administrador nao existe", e um que quebrasse
				// derrubaria o servidor, que e o que o `--diagadmin` acusaria no fim.
				//
				// MENOS UM: o que REFAZ O CENARIO tem passo proprio la embaixo, e disparado aqui ele
				// consumia o estrago antes de o passo dedicado poder medi-lo -- o teste dizia "nao
				// houve estrago" justamente porque ele mesmo ja tinha limpado. Custou uma rodada.
				var disparados = Verbos.Da(Verbos.Admin).Where(v => v.Nome != RefazerCenario).ToList();
				foreach (Verbo v in disparados) v.Acionar();
				Conferir(true, $"os {disparados.Count} verbs de admin foram disparados");
				break;
			}

			case 5:
				cli.SendVerbo("admin_contas");
				break;

			case 6:
				Conferir(cli.Contas.Count > 0, $"a lista de contas volta do servidor ({cli.Contas.Count})");
				Conferir(cli.Contas.All(a => a.Conta.Length > 0), "toda conta da lista tem nome");
				break;

			// -------------------------------------------------------------- promover e rebaixar
			case 7:
				cli.SendVerbo("admin_promover", "NaoExisteEstaConta");
				Conferir(true, "promover conta inexistente nao derruba o servidor");
				break;

			case 8:
				// PROMOVE PELO NOME DO PERSONAGEM, nao pelo da conta -- de proposito. E o caminho
				// que o admin usa de verdade (ele ve o nome andando na tela, nao o login), e e o
				// unico que exercita o `AccountStore.Achar` traduzindo personagem -> conta.
				cli.SendVerbo("admin_promover", cli.LocalName);
				break;

			case 9:
				cli.SendVerbo("admin_contas");
				break;

			case 10:
				// A MINHA conta, achada pelo personagem que esta nela. Conferir "alguma conta e
				// admin" passaria de graca numa pasta que ja tem admins de rodadas anteriores.
				Conferir(MinhaConta(cli) is { Admin: true },
					$"promover pelo nome do personagem ('{cli.LocalName}') marca a conta certa");
				break;

			// -------------------------------------------------------------- A REGRESSAO
			case 11:
				// Um marco pra ter o que gastar. E o proprio verb de admin que da.
				cli.SendVerbo("admin_marco", $"{0}|1");
				break;

			case 12:
			{
				// APRENDER E O GATILHO. `Aprender` chama `AplicarPoderes`, que era quem apagava o
				// bit. Se o conserto nao estivesse de pe, a aba sumiria exatamente aqui.
				string? alvo = PrimeiraRaiz(cli);
				if (alvo == null) { Conferir(false, "achei uma skill pra aprender (regressao nao testada)"); break; }
				cli.SendAprender(alvo);
				_passos.Add($"         (aprendendo '{alvo}' pra forcar o recalculo de poderes)");
				break;
			}

			case 13:
				Conferir(!_tinhaAdminAntes || cli.Atributos.Tem(Protocol.Poder.Admin),
					"REGRESSAO: aprender uma skill NAO apaga o bit de admin");
				Conferir(!_tinhaAdminAntes || Array.IndexOf(menu.AbasDeTeste, Verbos.Admin) >= 0,
					"REGRESSAO: a aba Admin continua na lista depois de aprender");
				break;

			// -------------------------------------------------------------- refazer o cenario
			// So vale quando ha estrago -- e ha quando esta bancada roda junto do `--socar --mente`,
			// que produz knockback contra parede. Sem estrago o verb nao tem o que provar, e dizer
			// "ok" ali seria mentir: o passo se declara NAO TESTADO.
			case 14:
				_quebradas = cli.CenarioCaido.Count;
				if (_quebradas == 0)
				{
					// ESPERA O ESTRAGO CHEGAR. Quebrar cenario depende de alguem levar um golpe
					// pesado contra parede, e isso nao acontece no primeiro segundo -- se o outro
					// processo (`--socar`) ainda esta se aproximando, desistir aqui seria declarar
					// "nada a testar" sem ter esperado nada.
					if (++_esperas < EsperasPorEstrago) { _passo = 14; return; }
					_passos.Add("  --     refazer cenario: nao houve estrago em "
							  + $"{EsperasPorEstrago * 0.6:0}s (rode com --quebrarteste 12)");
					_passo = 99;
					break;
				}
				Conferir(true, $"o estrago do servidor chega ao cliente ({_quebradas} celula(s))");
				cli.SendVerbo("admin_consertar_cenario");
				break;

			case 15:
				Conferir(cli.CenarioCaido.Count == 0,
					$"refazer o cenario zera o estrago da zona ({_quebradas} celula(s) antes)");
				break;

			// -------------------------------------------------------------- relatorio
			default:
				_acabou = true;
				menu.Fechar();
				GD.Print("\n[admin] ===== BANCADA DE ADMIN =====");
				foreach (string l in _passos) GD.Print("[admin] " + l);
				GD.Print(_falhas.Count == 0
					? "[admin] ===== TUDO OK ====="
					: $"[admin] ===== {_falhas.Count} FALHA(S) =====\n[admin]   " + string.Join("\n[admin]   ", _falhas));
				break;
		}
	}

	/// <summary>
	/// A conta DESTA sessao, achada na lista pelo nome do personagem que estou jogando.
	///
	/// O cliente nunca guarda o nome da propria conta (ele so o digitou na tela de login, e quem
	/// o conhece dali em diante e o servidor). Achar pelo personagem e o que sobra -- e e o mesmo
	/// caminho que o admin faz de cabeca.
	/// </summary>
	private static GameClient.ContaInfo? MinhaConta(GameClient cli)
	{
		foreach (GameClient.ContaInfo a in cli.Contas)
			if (a.Personagens.Contains(cli.LocalName, StringComparison.OrdinalIgnoreCase)) return a;
		return null;
	}

	/// <summary>
	/// Uma skill que da pra comprar agora: uma ARVORE aberta a qualquer raca.
	///
	/// Arvore de proposito -- o gate do sistema e a arvore, nao a folha, e uma folha exigiria a
	/// arvore comprada antes. O que importa aqui e so provocar o recalculo de poderes.
	/// </summary>
	private static string? PrimeiraRaiz(GameClient cli)
	{
		Jandirus.Core.Skills.SkillCatalog? cat = MenuJogo.CatalogoPublico();
		if (cat == null) return null;
		foreach (string path in cat.ArvoresDeTodos)
			if (!cli.SkillsAprendidas.Contains(path)) return path;
		return null;
	}
}
