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
				// SEM ACAO FICA DE FORA -- ver `Verbo.Acionar`. Nenhum verb de admin nasce assim
				// hoje, e o filtro existe pra que o primeiro que nascer nao derrube a bancada.
				var disparados = Verbos.Da(Verbos.Admin)
					.Where(v => v.Nome != RefazerCenario && v.Acionar != null).ToList();
				foreach (Verbo v in disparados) v.Acionar!();
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

			// -------------------------------------------------------------- o clima
			// O PAINEL DE CLIMA E FERRAMENTA DE DEPURACAO, e por isso ele tem que funcionar mesmo
			// quando o resto esta quebrado -- se ele falhar calado, o proximo defeito de clima vai
			// ser investigado com uma ferramenta que nao responde.
			case 14:
				// NEVASCA de proposito: a Terra tem nevasca na lista do DM, mas sortea-la pelo
				// ciclo natural pode levar meia hora. E exatamente o caso de uso do painel.
				cli.SendVerbo("admin_clima", "Nevasca|1.00");
				break;

			case 15:
			{
				Jandirus.Core.World.EstadoDoClima agora = World.Instancia?.TempoQueFaz ?? default;
				Conferir(agora.Tipo == Jandirus.Core.World.TipoDeClima.Nevasca,
					$"o painel FORCA o clima pedido (pedi nevasca, veio {Jandirus.Core.World.Clima.Nome(agora.Tipo)})");
				Conferir(agora.Forcado, "e o cliente sabe que ele foi forcado (nao confundiu com o natural)");
				break;
			}

			case 16:
				// FORCAR UM CLIMA QUE NAO CAI AQUI TEM QUE FUNCIONAR. Recusar tiraria justamente o
				// caso pra que a ferramenta existe: olhar o desenho da chuva de sangue sem ter de
				// viajar cinco dias in-game ate Vegeta.
				cli.SendVerbo("admin_clima", "ChuvaDeSangue|0.50");
				break;

			case 17:
			{
				Jandirus.Core.World.EstadoDoClima agora = World.Instancia?.TempoQueFaz ?? default;
				Conferir(agora.Tipo == Jandirus.Core.World.TipoDeClima.ChuvaDeSangue,
					"forcar um clima que NAO cai neste planeta funciona (e o ponto da ferramenta)");
				cli.SendVerbo("admin_clima_natural");
				break;
			}

			case 18:
				Conferir(!(World.Instancia?.TempoQueFaz?.Forcado ?? false),
					"'voltar ao natural' solta o ceu de volta pro ciclo do relogio");
				cli.SendVerbo("admin_clima", "NaoExisteEsteClima");
				Conferir(true, "clima inexistente nao derruba o servidor");
				break;

			// -------------------------------------------------------------- refazer o cenario
			// So vale quando ha estrago -- e ha quando esta bancada roda junto do `--socar --mente`,
			// que produz knockback contra parede. Sem estrago o verb nao tem o que provar, e dizer
			// "ok" ali seria mentir: o passo se declara NAO TESTADO.
			case 19:
				_quebradas = cli.CenarioCaido.Count;
				if (_quebradas == 0)
				{
					// ESPERA O ESTRAGO CHEGAR. Quebrar cenario depende de alguem levar um golpe
					// pesado contra parede, e isso nao acontece no primeiro segundo -- se o outro
					// processo (`--socar`) ainda esta se aproximando, desistir aqui seria declarar
					// "nada a testar" sem ter esperado nada.
					if (++_esperas < EsperasPorEstrago) { _passo = 19; return; }
					_passos.Add("  --     refazer cenario: nao houve estrago em "
							  + $"{EsperasPorEstrago * 0.6:0}s (rode com --quebrarteste 12)");
					// PULA SO O PASSO DO CENARIO, e nao o resto da bancada. Aqui estava um
					// `_passo = 99`, que caia direto no relatorio -- e enquanto o cenario era a
					// ultima coisa medida isso era inofensivo. Deixou de ser no minuto em que uma
					// familia nova (a previa da limpeza) entrou depois dele: sem estrago -- o caso
					// COMUM de rodar o `--diagadmin` sozinho --, os passos novos nunca rodariam, e
					// a bancada diria "TUDO OK" sem ter medido dois tercos do que ela agora mede.
					_passo = 21;
					break;
				}
				Conferir(true, $"o estrago do servidor chega ao cliente ({_quebradas} celula(s))");
				cli.SendVerbo("admin_consertar_cenario");
				break;

			case 20:
				Conferir(cli.CenarioCaido.Count == 0,
					$"refazer o cenario zera o estrago da zona ({_quebradas} celula(s) antes)");
				break;

			// -------------------------------------------------------------- a previa da limpeza total
			// ============================ SO O PASSO 1, E NUNCA O PASSO 2 ============================
			// O segundo verb (`admin_limpar_ja`) APAGA O SERVIDOR, e ele nao entra em bancada nenhuma
			// que rode contra a pasta de verdade -- quem exercita a limpeza inteira e a `--wipeteste`,
			// que faz isso numa pasta de mentira. Aqui se prova a UNICA parte que so existe no fio: a
			// previa sai do servidor, atravessa o `S2C.Limpeza` e chega inteira no cliente.
			//
			// E POR QUE ISSO PRECISA DE ROBO: a `--wipeteste` roda dentro do servidor e nunca ve um
			// pacote. Se o leitor do outro lado errasse a ORDEM dos campos (codigo, segundos, linhas),
			// o painel de perigo desenharia a lista vazia e o botao nunca acenderia -- e nada, em lugar
			// nenhum, ficaria vermelho. E o mesmo cego que este projeto ja registrou: "uniform escrito
			// nao e pixel desenhado".
			//
			// A PREVIA NAO APAGA NADA (a `--wipeteste` afirma exatamente isso), entao rodar aqui,
			// contra o mundo de verdade, e seguro.
			// ====================================================================================
			case 21:
				_esperas = 0;   // o contador vem usado do passo do cenario
				cli.SendVerbo("admin_limpar");
				break;

			case 22:
			{
				GameClient.PreviaDeLimpeza p = cli.Limpeza;
				// A resposta pode nao ter chegado ainda: o pacote sai por caminho confiavel, mas o
				// robo anda a cada 0,6 s e a varredura da pasta de contas leva o tempo que leva.
				if (p.Codigo.Length == 0 && ++_esperas < 10) { _passo = 22; return; }

				Conferir(p.Codigo.Length == 4, $"a previa da limpeza chega ao cliente com codigo ('{p.Codigo}')");
				Conferir(p.Segundos > 0, $"...com o prazo de validade ({p.Segundos}s)");
				Conferir(p.Linhas.Count > 0, $"...e com o inventario do que sumiria ({p.Linhas.Count} linha(s))");
				// AS LINHAS TEM QUE TER CONTEUDO. Um `GetString` com teto curto devolve VAZIO (e nao
				// truncado) -- foi assim que a lista de personagens do painel de contas virou uma
				// linha em branco uma vez. Uma lista de oito strings vazias passaria na conferencia
				// de cima e nao diria nada a quem vai apagar o servidor.
				Conferir(p.Linhas.All(l => l.Trim().Length > 0), "...e nenhuma linha veio vazia (teto do GetString)");
				Conferir(p.Linhas.Any(l => l.Contains("conta")), "...com a contagem de contas dentro");

				// O PAINEL DE PERIGO DESENHA. `Redesenhar` monta a aba inteira; se o painel quebrasse
				// (uma `LineEdit` sem pai, um `Tema` faltando), seria aqui.
				menu.IrPara(Verbos.Admin);
				Conferir(true, "a aba Admin remonta com o painel de perigo aberto");
				break;
			}

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
