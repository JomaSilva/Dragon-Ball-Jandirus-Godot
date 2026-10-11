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

	/// <summary>A chave do mundo de mentira que o palco destruiu. Ver os passos da restauracao pela lista.</summary>
	private string _chaveDoMorto = "";

	/// <summary>O nome que a bancada da ao planeta em que esta, e o que o lugar se chamava antes. Ver os passos do batismo.</summary>
	private const string NomeDeBancada = "Mundo da Bancada";
	private string _lugarAntes = "";

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
				// A PORTA DO LOGIN DO BATISMO: nenhum verbo saiu ainda e este corpo nao trocou de zona, entao o
				// unico pacote que pode ter trazido o direito e o que o login manda (`AoPisarNoMundo`). Sem ele
				// o admin so veria a caixa de texto do cartao depois de mudar de planeta.
				Conferir(cli.DireitoDeBatismo == Jandirus.Core.World.Batismo.Direito.Admin,
					$"o direito de batizar o chao em que estou chega NO LOGIN, antes de qualquer verbo (admin: {cli.DireitoDeBatismo})");
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

			// -------------------------------------------------------------- restaurar planeta PELA LISTA
			// O dono (2026-10-10): "queria q fosse possivel eu ver a lista de planetas destruidos e
			// selecionar os q quero restaurar" -- sem ir ate o planeta. A REGRA do servidor esta na
			// `--planetateste` (familia 16); aqui se prova o que so existe PELO FIO e NA TELA: o planeta
			// destruido chega pelo `S2C.Mortos`, vira uma linha do cartao, o botao devolve a CHAVE certa, o
			// servidor restaura um planeta em que o admin NAO esta, e o cartao esvazia sozinho.
			//
			// O PLANETA E DE MENTIRA (um gerado de seed escolhida a mao) e nasce dentro do `PalcoDeMortes`
			// do servidor: nada disto encosta no `planetas-mortos.json` de quem roda a bancada.
			case 23:
			{
				_esperas = 0;
				if (Jandirus.Server.GameServer.Instance is not { } srv)
				{
					// O palco e montado NO SERVIDOR; discando pra outro processo nao ha como monta-lo daqui.
					_passos.Add("  --     restaurar planeta pela lista: NAO TESTADO (rode hospedando: --host)");
					_passo = 27;
					break;
				}
				_chaveDoMorto = srv.DestruirPlanetaDeMentira();
				break;
			}

			case 24:
			{
				Jandirus.Server.GameServer srv = Jandirus.Server.GameServer.Instance!;
				bool chegou = cli.Mortos.Todos.Any(e => e.Chave == _chaveDoMorto);
				if (!chegou && ++_esperas < 10) { _passo = 24; return; }

				Conferir(chegou, "o planeta destruido chega ao cliente pelo S2C.Mortos");
				Conferir(MenuJogo.PlanetasDestruidos(cli.Mortos).Count(e => e.Chave == _chaveDoMorto) == 1,
					"REGRA PURA: a lista do cartao traz o destruido (uma vez)");
				Button? restaurar = BotaoDeRestaurar(menu, _chaveDoMorto);
				Conferir(restaurar != null,
					"o cartao \"Planetas destruídos\" da aba Admin lista o planeta com o botao 'restaurar' -- e o admin esta em OUTRO lugar");

				// A CONTRAPROVA PRIMEIRO: com o defeito injetado, o MESMO clique nao restaura nada.
				srv.RestauraSoOndeEstaDeTeste = true;
				restaurar?.EmitSignal(BaseButton.SignalName.Pressed);
				break;
			}

			case 25:
			{
				Jandirus.Server.GameServer srv = Jandirus.Server.GameServer.Instance!;
				Conferir(srv.PlanetaDeMentiraEstaMorto(),
					"(defeito injetado: o servidor so restaura onde o admin esta) o clique na lista NAO restaura -- a regua sabe reprovar");
				srv.RestauraSoOndeEstaDeTeste = false;

				_esperas = 0;
				BotaoDeRestaurar(menu, _chaveDoMorto)?.EmitSignal(BaseButton.SignalName.Pressed);
				break;
			}

			case 26:
			{
				Jandirus.Server.GameServer srv = Jandirus.Server.GameServer.Instance!;
				bool saiu = !cli.Mortos.Todos.Any(e => e.Chave == _chaveDoMorto);
				if (!saiu && ++_esperas < 10) { _passo = 26; return; }

				Conferir(!srv.PlanetaDeMentiraEstaMorto(),
					"apertar 'restaurar' na lista RESTAURA o planeta no servidor -- de onde o admin esta");
				Conferir(saiu, "...a lista do cliente esvazia (o S2C.Mortos voltou)");
				Conferir(BotaoDeRestaurar(menu, _chaveDoMorto) == null,
					"...e o cartao deixa de oferecer o planeta (a aba se redesenhou sozinha)");
				break;
			}

			// -------------------------------------------------------------- o batismo, pelo FIO e na TELA
			// O dono (2026-10-10): "adicione tb a opçao de nomear o planeta". As REGRAS (descobridor, dono,
			// nomes que servem) estao na `--batismoteste`; aqui se prova o que so existe pelo fio: o admin
			// batiza o planeta em que esta pelo verbo do cartao, o nome chega pelo `S2C.NomesDePlanetas`, a
			// faixa "Lugar" da aba World passa a dize-lo, e tirar o nome desfaz tudo.
			//
			// SO NA MEMORIA: o palco do servidor impede que o `planetas-nomes.json` de quem roda seja escrito.
			case 27:
			{
				if (Jandirus.Server.GameServer.Instance is not { } srv)
				{
					_passos.Add("  --     batismo pelo fio: NAO TESTADO (rode hospedando: --host)");
					_passo = 31;
					break;
				}
				srv.BatismosSoNaMemoriaDeTeste = true;
				_lugarAntes = cli.NomeDoLugar;
				_esperas = 0;
				menu.IrPara("World");
				cli.SendVerbo("planeta_nomear", NomeDeBancada);
				break;
			}

			case 28:
			{
				if (cli.NomeDoLugar != NomeDeBancada && ++_esperas < 10) { _passo = 28; return; }
				Conferir(cli.DireitoDeBatismo == Jandirus.Core.World.Batismo.Direito.Admin,
					$"o DIREITO de batizar chega ao cliente no cabecalho do pacote (admin: {cli.DireitoDeBatismo})");
				Conferir(cli.NomeDoLugar == NomeDeBancada && cli.Zone.Name != NomeDeBancada,
					$"o planeta BATIZADO chega pelo S2C.NomesDePlanetas: '{_lugarAntes}' agora se mostra '{cli.NomeDoLugar}' (a zona continua '{cli.Zone.Name}' por dentro)");
				string? faixa = menu.ValorDesenhado("World", "Lugar");
				Conferir(faixa != null && faixa.StartsWith(NomeDeBancada, StringComparison.Ordinal),
					$"...e a faixa \"Lugar\" da aba World se redesenhou sozinha com o nome novo ('{faixa}')");
				_esperas = 0;
				cli.SendVerbo("planeta_desnomear");
				break;
			}

			case 29:
			{
				if (cli.NomeDoLugar == NomeDeBancada && ++_esperas < 10) { _passo = 29; return; }
				string? faixa = menu.ValorDesenhado("World", "Lugar");
				Conferir(cli.NomeDoLugar == _lugarAntes && faixa != null && faixa.StartsWith(_lugarAntes, StringComparison.Ordinal),
					$"TIRAR O NOME desfaz: o lugar volta a se mostrar '{_lugarAntes}' no cliente e na faixa ('{faixa}')");
				break;
			}

			case 30:
				menu.IrPara(Verbos.Admin);
				break;

			// -------------------------------------------------------------- relatorio
			default:
				_acabou = true;
				menu.Fechar();
				if (Jandirus.Server.GameServer.Instance is { } servidorDoBatismo)
					servidorDoBatismo.BatismosSoNaMemoriaDeTeste = false;

				// O PALCO E O DEFEITO INJETADO NAO SOBREVIVEM A BANCADA, mesmo que um passo tenha quebrado
				// no meio do caminho.
				if (Jandirus.Server.GameServer.Instance is { } servidor)
				{
					servidor.RestauraSoOndeEstaDeTeste = false;
					servidor.FecharOPalcoDaListaDeAdmin();
				}
				GD.Print("\n[admin] ===== BANCADA DE ADMIN =====");
				foreach (string l in _passos) GD.Print("[admin] " + l);
				GD.Print(_falhas.Count == 0
					? "[admin] ===== TUDO OK ====="
					: $"[admin] ===== {_falhas.Count} FALHA(S) =====\n[admin]   " + string.Join("\n[admin]   ", _falhas));
				break;
		}
	}

	/// <summary>
	/// O botao "restaurar" DESTE planeta no cartao da aba Admin. Achado pelo metadado da chave -- o texto e
	/// o mesmo em todas as linhas -- e so se estiver NA TELA (as paginas das outras abas continuam vivas e
	/// escondidas, e uma pagina remontada deixa a antiga pra ser liberada no fim do quadro).
	/// </summary>
	private static Button? BotaoDeRestaurar(MenuJogo menu, string chave)
	{
		if (menu.PaginaDeTeste(Verbos.Admin) is not { } pagina) return null;

		var fila = new Queue<Node>();
		fila.Enqueue(pagina);
		while (fila.Count > 0)
		{
			Node n = fila.Dequeue();
			if (n.IsQueuedForDeletion()) continue;
			if (n is Button b && b.IsVisibleInTree() && b.HasMeta("restaurar_planeta")
				&& b.GetMeta("restaurar_planeta").AsString() == chave)
				return b;
			foreach (Node filho in n.GetChildren()) fila.Enqueue(filho);
		}
		return null;
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
