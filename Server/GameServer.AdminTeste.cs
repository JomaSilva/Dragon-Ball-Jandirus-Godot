using Godot;
using Jandirus.Core.Forms;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DAS FERRAMENTAS DE ADMIN -- `admin_forma` e `admin_liberar_ki`.
///
/// Roda dentro do `--formasteste`, no mesmo login e com o mesmo `Checa` (ver
/// <see cref="RodarBancadaDeFormas"/>).
///
/// ============================ POR QUE ELA ENTRA PELO `Verbo` E NAO PELO `AdminForcarForma` ============================
/// Porque as duas coisas que estas ferramentas podem quebrar NAO moram dentro delas:
///
///   * a PERMISSAO mora no funil (`GameServer.Verbos.cs`, o `default:` do switch). Chamar
///     `AdminForcarForma` direto provaria que ela forca a forma -- e provaria isso tambem no dia em
///     que qualquer um pudesse chama-la. O cliente esconde a aba de quem nao e admin, e esconder
///     botao nunca foi permissao;
///   * o REGISTRO no `admin.log` tambem mora no funil (uma linha no `VerboDeAdmin`, pra cobrir
///     inclusive os verbs que ainda nao existem).
///
/// E a regra desta sessao inteira: a checagem tem que passar pelo MESMO caminho que o jogo usa.
/// `Verbo(adm, "admin_forma", arg)` e literalmente o que chega do fio quando o dono clica o botao.
/// ================================================================================================================
///
/// ============================ E POR QUE ELA MEDE PELAS ESCUTAS ============================
/// Uma ferramenta de admin que escrevesse `pl.Forma.Atual` na mao passaria em qualquer teste que
/// so olhasse `pl.Forma.Atual` -- e daria um jogo em que o dono ve o NOME da forma e o mesmo BP, com
/// a zona inteira (a tela dele inclusive) desenhando o corpo base. Esse defeito ja aconteceu neste
/// arquivo (ver "A ZONA NAO SABIA DISTO" no `DesfazerOozaru`).
///
/// Entao alem do estado, toda checagem daqui pergunta ao PACOTE: `EscutaDeAnuncios` (o funil da
/// escada) e `EscutaDeFeras` (o funil do macaco). Sao os dois canais que separam "mudou de forma" de
/// "mudou um campo".
/// ======================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// AS DUAS FERRAMENTAS DE TESTE QUE O DONO PEDIU, exercitadas pelo canal de verbs.
	///
	/// MEXE NO PERSONAGEM DE PROPOSITO -- e o ponto da ferramenta e forcar o que o jogo cobra caro,
	/// entao o corpo de teste tem que estar POBRE (BP de recem-nascido, maestria zero, nenhuma forma
	/// liberada, sem linhagem e sem classe). Tudo o que ela zera esta guardado no topo e reposto no
	/// `finally`: o bit de admin em primeiro lugar, porque perde-lo deixaria o host sem a aba pelo
	/// resto da sessao e faria o proximo `--diagadmin` acusar uma regressao que nao existe.
	/// </summary>
	/// <summary>O id do corpo forjado do bloco "curar nao e reviver" -- fora de toda faixa das outras bancadas.</summary>
	private const int IdDaCobaiaDaCura = 93_100;

	private void AsFerramentasDeAdmin(ServerPlayer adm, Action<string, bool, string> Checa)
	{
		// ---------------------------------------------------------------- o que sera reposto
		Protocol.Poder poderesAntes = adm.Poderes, concedidosAntes = adm.PoderesConcedidos;
		double bpAntes = adm.Ficha.BP;
		string classeAntes = adm.Ficha.Class, linhagemAntes = adm.Ficha.SaiyanLineage;
		Dictionary<string, double> maestriaAntes =
			Catalogo.Todas.ToDictionary(d => d.Id, d => adm.Forma.Maestria.De(d.Id));
		var liberadasAntes = new HashSet<int>(adm.Forma.Liberadas);
		var estreiasAntes = new HashSet<int>(adm.Forma.EstreiaVista);
		bool uiAntes = adm.UltraInstinct.Aprendida, ueAntes = adm.PoderDaDestruicao.Aprendida;
		bool godAntes = adm.Ficha.godki?.awakened ?? false;
		bool sabiaUnlock = adm.Livro.Sabe(SkillKiUnlocked), sabiaControl = adm.Livro.Sabe(SkillKiControl);
		int nivelKiAntes = adm.Niveis.Nivel(SkillKiControl);

		// ---------------------------------------------------------------- o disparo, e o que ele fez sair
		// UM SO LUGAR ARMA E DESARMA AS TRES ESCUTAS. Escuta estatica esquecida ligada acumula o resto
		// da bancada e faz uma checagem passar por barulho de outro bloco -- o modo de falha classico
		// de teste com estado global, e o motivo de o `RodarBancadaDeFormas` ter o mesmo cuidado.
		(List<string> Avisos,
		 List<(int Quem, string De, string Para, DegrauDeCena Degrau)> Escada,
		 List<(int Quem, FormaOozaru Forma, bool Primeira, DegrauDeCena Degrau)> Feras)
		Mandar(string cmd, string arg)
		{
			EscutaDeAvisos = [];
			EscutaDeAnuncios = [];
			EscutaDeFeras = [];

			Verbo(adm, cmd, arg);   // <- O CANAL DO JOGO, com a checagem de admin e o registro dentro

			var a = EscutaDeAvisos ?? [];
			var e = EscutaDeAnuncios ?? [];
			var f = EscutaDeFeras ?? [];
			EscutaDeAvisos = null;
			EscutaDeAnuncios = null;
			EscutaDeFeras = null;
			return (a, e, f);
		}

		try
		{
			// =====================================================================
			// 0. A BANCADA PRECISA SER ADMIN
			// =====================================================================
			// Em `--host` o bit ja vem do login. Num servidor dedicado nao vem -- e ai a bancada
			// CONCEDE e diz que concedeu, em vez de reprovar: provar que o host e admin e trabalho do
			// `--diagadmin`, e reprovar aqui apontaria o dedo pro lugar errado.
			if (!EhAdmin(adm))
			{
				adm.PoderesConcedidos |= Protocol.Poder.Admin;
				AplicarPoderes(adm);
				GD.Print("  --   ADMIN: o corpo de bancada nao era admin (servidor dedicado?) -- concedido pra este bloco");
			}
			Checa("a bancada roda com o bit de admin aceso", EhAdmin(adm), "");

			// ONDE O `admin.log` ESTAVA ANTES DE TUDO. O bloco 7 le SO o que foi escrito daqui pra
			// frente: o arquivo e cumulativo, e a pasta de contas desta maquina ja tem centenas de
			// rodadas dentro. Uma varredura do arquivo inteiro acharia `admin_forma` de uma rodada
			// ANTERIOR e passaria verde com o registro desligado nesta -- historico fazendo o papel de
			// prova, que e o mesmo defeito de forma que o `--diagadmin` ja teve com "alguma conta e admin".
			string caminhoDoLog = _store != null ? Path.Combine(_store.Pasta, "admin.log") : "";
			int logAntes = caminhoDoLog.Length > 0 && File.Exists(caminhoDoLog)
				? File.ReadAllLines(caminhoDoLog).Length : 0;

			// =====================================================================
			// 1. A LISTA COBRE O CATALOGO INTEIRO
			// =====================================================================
			// ============================ POR QUE ELA E LIDA DO TEXTO ============================
			// Porque o texto E a resposta. `admin_forma` sem argumento e o unico jeito de o dono
			// descobrir o que existe pra forcar, e uma forma que nao aparece ali e, na pratica, uma
			// forma que ele nao vai testar. Conferir a funcao que monta a frase provaria a funcao;
			// conferir a frase prova a ferramenta.
			//
			// COMO REPROVA SE A REGRA SUMIR: acrescente uma entrada ao `Catalogo` e a contagem
			// esperada sobe sozinha (ela sai do proprio catalogo, nao de um numero escrito aqui) --
			// se o `ListarFormas` deixar de nomea-la, a linha cai apontando qual falta. Troque o
			// `GroupBy` por um `Take(20)`, ou o `d.Id` por `d.Nome`, e cai do mesmo jeito.
			// ================================================================================
			var (listagem, _, _) = Mandar("admin_forma", "");

			// AS LINHAS DE GRUPO COMECAM COM DOIS ESPACOS (`$"  {g.Key}: "`); o cabecalho nao. A
			// distincao importa porque o cabecalho TAMBEM tem ": " (o "volta ao normal: base") e um
			// `IndexOf(": ")` cego contaria `base` como se fosse um degrau da lista.
			var listados = new HashSet<string>(StringComparer.Ordinal);
			var linhasVistas = new HashSet<string>(StringComparer.Ordinal);
			foreach (string l in listagem)
			{
				if (!l.StartsWith("  ", StringComparison.Ordinal)) continue;
				int dp = l.IndexOf(": ", StringComparison.Ordinal);
				if (dp < 0) continue;
				linhasVistas.Add(l[..dp].Trim());
				foreach (string id in l[(dp + 2)..]
						 .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
					listados.Add(id);
			}

			List<FormaDef> esperadas = Catalogo.Todas.Where(d => d.Id != Catalogo.IdBase).ToList();
			string faltando = string.Join(", ", esperadas.Select(d => d.Id).Where(i => !listados.Contains(i)));
			string sobrando = string.Join(", ", listados.Where(i => !esperadas.Any(d => d.Id == i)));

			Checa($"a lista do admin nomeia as {esperadas.Count} formas do catalogo, uma a uma",
				  listados.Count == esperadas.Count && faltando.Length == 0 && sobrando.Length == 0,
				  $"listou {listados.Count}; falta [{faltando}]; sobra [{sobrando}]");

			// A BASE NAO E UM DEGRAU, e ela precisa estar no cabecalho: e o caminho de volta. As duas
			// metades numa linha so porque sao a mesma decisao (`.Where(d => d.Id != IdBase)` mais o
			// `Avisar` do topo), e apagar qualquer uma delas quebra a ferramenta de um jeito diferente:
			// sem o filtro o dono ve `base` como se fosse uma transformacao; sem o cabecalho ele nao
			// tem como saber que existe volta.
			int linhasDoCatalogo = esperadas.Select(d => d.Linha).Distinct().Count();
			Checa("...e nao a oferece como degrau -- a base e o caminho de VOLTA, e ele vem no cabecalho",
				  !listados.Contains(Catalogo.IdBase)
				  && listagem.Any(l => !l.StartsWith("  ", StringComparison.Ordinal)
									&& l.Contains(Catalogo.IdBase, StringComparison.Ordinal)),
				  string.Join(" | ", listagem));

			Checa($"...e agrupa por TODAS as {linhasDoCatalogo} linhas do catalogo (a pergunta e sempre 'quais sao os degraus da escada X')",
				  linhasVistas.Count == linhasDoCatalogo,
				  $"{linhasVistas.Count} grupos: {string.Join(", ", linhasVistas)}");

			// UM NOME QUE NAO EXISTE. Sem a guarda do `AcharForma == null`, a linha seguinte le
			// `d.Linha` num nulo e derruba o SERVIDOR por um erro de digitacao do admin.
			var (semNome, escadaSemNome, ferasSemNome) = Mandar("admin_forma", "NaoExisteEstaForma");
			Checa("um nome inexistente e recusado APONTANDO A LISTA, e nao derruba o servidor",
				  semNome.Any(a => a.Contains("nao existe forma") && a.Contains("admin_forma"))
				  && escadaSemNome.Count == 0 && ferasSemNome.Count == 0,
				  string.Join(" | ", semNome));

			// =====================================================================
			// 2. O CORPO POBRE -- e o ponto da ferramenta inteira
			// =====================================================================
			// ============================ O QUE ESTE BLOCO ZERA, E POR QUE ============================
			// Uma varredura que forcasse 35 formas num personagem de 10 trilhoes de BP com o ki divino
			// desperto (que e como o `RodarBancadaDeFormas` deixa o corpo) passaria verde tambem se o
			// `admin_forma` RESPEITASSE as portas -- porque nao haveria porta fechada nenhuma. Seria a
			// bancada mais inutil possivel: verde exatamente no caso em que a ferramenta nao serve.
			//
			// Por isso o corpo e esvaziado ate onde o gate consegue enxergar: BP de recem-nascido,
			// maestria zero em tudo, nada liberado, sem linhagem, sem classe, sem ki divino e sem as
			// duas disciplinas. E a checagem logo abaixo MEDE que sobrou porta fechada -- ela e a que
			// da sentido a todas as seguintes.
			// ======================================================================================
			adm.Ficha.BP = 9;                       // o BP com que um personagem nasce
			adm.Ficha.Class = "";
			adm.Ficha.SaiyanLineage = "Saiyan";     // a linhagem comum: nem Primal, nem Legendary
			adm.UltraInstinct.Aprendida = false;
			adm.PoderDaDestruicao.Aprendida = false;
			if (adm.Ficha.godki != null) adm.Ficha.godki.awakened = false;
			foreach (FormaDef d in Catalogo.Todas) adm.Forma.Maestria.Por(d.Id, 0);
			adm.Forma.Liberadas.Clear();
			adm.Forma.EstreiaVista.Clear();
			adm.Ficha.KO = false;
			adm.Ficha.dead = false;
			adm.Ficha.Statify();
			Mandar("admin_forma", Catalogo.IdBase);   // parte da base, e pela propria ferramenta
			Medir(adm);
			double bpPelado = adm.Ficha.expressedBP;

			PerfilDeFormas pobre = Perfil(adm);
			List<FormaDef> abertas = esperadas
				.Where(d => adm.Forma.Avaliar(d.Id, adm.Ficha.BP, 1, false, pobre) == RecusaForma.Pode)
				.ToList();
			Checa($"com BP {adm.Ficha.BP:0}, maestria 0 e sem linhagem, o caminho LEGITIMO recusa as {esperadas.Count} formas",
				  abertas.Count == 0, "abertas: " + string.Join(", ", abertas.Select(d => d.Id)));

			// ---------------------------------------------------------------- A VARREDURA
			// ============================ SEIS PERGUNTAS POR FORMA, SOMADAS ============================
			// Sao 35 formas: uma linha de relatorio por forma por pergunta seriam duzentas linhas de
			// bancada pra ninguem ler. Entao cada pergunta vira UM contador e UMA linha, que diz quantas
			// passaram de quantas e nomeia a primeira que falhou -- o mesmo desenho do
			// `NenhumaFormaCaiEmFallback` na bancada do cliente.
			// ======================================================================================
			int chegou = 0, funil = 0, multiplicou = 0, estreou = 0, misturou = 0;
			int kiCheio = 0, bpSubiu = 0, escadaN = 0, escadaQueSobe = 0, bpDesceu = 0, repousoN = 0;
			string exChegou = "", exFunil = "", exMult = "", exEstreia = "", exMistura = "", exKi = "", exBp = "";

			foreach (FormaDef d in esperadas)
			{
				// ============================ O TANQUE E ESVAZIADO ANTES DE CADA FORCA ============================
				// Sem esta linha a checagem de "Ki cheio" la embaixo NAO CONSEGUE FALHAR, e a sabotagem
				// mostrou isso: `AplicarForma` preserva a RAZAO de Ki na troca, o corpo chega aqui com o
				// tanque cheio, e 100% de um tanque vira 100% do proximo pra sempre. Apagar o
				// `p.Ficha.Ki = p.Ficha.MaxKi` do verb deixava a bancada verde.
				//
				// Com 20% plantado a cada volta, cheio no fim so pode ter vindo do verb.
				// ============================================================================================
				adm.Ficha.Ki = adm.Ficha.MaxKi * 0.2;

				var (_, escada, feras) = Mandar("admin_forma", d.Id);
				Medir(adm);
				bool ehFera = d.Linha == LinhaDeForma.Oozaru;

				if (ehFera)
				{
					FormaOozaru quer = d.Id == Oozaru.IdDourado ? FormaOozaru.Dourado : FormaOozaru.Regular;
					double multFera = Oozaru.Multiplicador(quer, adm.Class);

					if (adm.Oozaru == quer) chegou++;
					else if (exChegou.Length == 0) exChegou = $"{d.Id}: virou {adm.Oozaru}";

					// O FUNIL DA FERA E O `VirarFera`, e ele deixa TRES marcas que uma atribuicao a mao
					// nao deixaria: o pacote de fera, o prazo da forma e a raiva que a meditacao gasta.
					if (feras.Any(f => f.Forma == quer) && adm.OozaruAte > NowMs() && adm.RaivaDoOozaru > 0) funil++;
					else if (exFunil.Length == 0)
						exFunil = $"{d.Id}: {feras.Count} pacote(s), ate={adm.OozaruAte - NowMs()}ms, raiva={adm.RaivaDoOozaru:0.#}";

					// O DOURADO ESCREVE NO `ssjBuff` E ZERA O `giantFormbuff` (senao 1,5 x 18 = 27); o
					// regular faz o contrario. Sao os dois campos que o `AplicarOozaru` toca.
					bool multOk = quer == FormaOozaru.Dourado
						? Math.Abs(adm.Ficha.ssjBuff - multFera) < 1e-6 && Math.Abs(adm.Ficha.giantFormbuff - 1) < 1e-9
						: Math.Abs(adm.Ficha.giantFormbuff - multFera) < 1e-6;
					if (multOk) multiplicou++;
					else if (exMult.Length == 0)
						exMult = $"{d.Id}: ssjBuff={adm.Ficha.ssjBuff:0.###} giant={adm.Ficha.giantFormbuff:0.###} (queria {multFera:0.###})";

					if (feras.Any(f => f.Forma == quer && f.Primeira)) estreou++;
					else if (exEstreia.Length == 0) exEstreia = $"{d.Id}: a fera nao anunciou estreia";

					// E O SLOT DA ESCADA NAO PODE TER SIDO TOCADO. Esta e a linha que reprova no dia em
					// que alguem "simplificar" o verb mandando a linha do Oozaru pelo `Entrar`.
					if (adm.Forma.Atual == d.Id || escada.Any(a => a.Para == d.Id))
					{
						misturou++;
						if (exMistura.Length == 0) exMistura = $"{d.Id} entrou no slot da ESCADA (`Forma.Atual`)";
					}
					continue;
				}

				escadaN++;

				if (adm.Forma.Atual == d.Id) chegou++;
				else if (exChegou.Length == 0) exChegou = $"{d.Id}: ficou em {adm.Forma.Atual}";

				// O FUNIL DA ESCADA E O `AnunciarForma`. Sem ele o servidor sabe da forma nova e NENHUMA
				// tela do jogo sabe -- inclusive a do proprio dono.
				if (escada.Any(a => a.Para == d.Id)) funil++;
				else if (exFunil.Length == 0)
					exFunil = $"{d.Id}: a zona nao foi avisada ({escada.Count} anuncio(s))";

				// O `AplicarForma` E QUEM ESCREVE O `ssjBuff`. A conta e a MESMA expressao que ele usa --
				// refazer a formula aqui provaria a formula da bancada.
				if (Math.Abs(adm.Ficha.ssjBuff - adm.Forma.Multiplicador(Perfil(adm))) < 1e-9) multiplicou++;
				else if (exMult.Length == 0) exMult = $"{d.Id}: ssjBuff={adm.Ficha.ssjBuff:0.####}";

				// ESTREIA: `Liberadas`/`EstreiaVista` foram esvaziadas la em cima, entao toda forma da
				// varredura e a primeira vez. E a decisao desta fase -- forcar NAO suprime a cinematica.
				if (escada.Any(a => a.Para == d.Id && a.Degrau == DegrauDeCena.Estreia)) estreou++;
				else if (exEstreia.Length == 0)
					exEstreia = $"{d.Id}: " + string.Join(" ", escada.Select(a => $"{a.Para}/{a.Degrau}"));

				// KI CHEIO: a divergencia documentada do verb. Sem ela o `TickDaForma` derrubaria a forma
				// forcada em segundos e o dono leria "nao funcionou".
				if (adm.Ficha.Ki >= adm.Ficha.MaxKi - 1e-6) kiCheio++;
				else if (exKi.Length == 0)
					exKi = $"{d.Id}: {adm.Ficha.Ki:N0} de {adm.Ficha.MaxKi:N0}";

				// E O PODER TEM QUE TER SUBIDO DE VERDADE. `ssjBuff` certo com `expressedBP` parado seria
				// um campo escrito que o `powerlevel()` nao le -- a falha assinatura deste projeto.
				// ============================ "SUBIR" NAO VALE PRA TODA FORMA -- E O FROST DEMON PROVA ============================
				// Esta linha era `bpSubiu == escadaN` e reprovava em CINCO entradas: as quatro supressoes do
				// Frost Demon (0,25x a 0,90x) e a forma base dele (1x). Nao era defeito do jogo -- era a
				// checagem afirmando uma coisa que deixou de ser verdade no dia em que o catalogo ganhou
				// degraus ABAIXO da base. Uma casca que APERTA o poder e o desenho do Mutante, e exigir que
				// ela suba o `expressedBP` e exigir que ela nao funcione.
				//
				// O CORTE E A MESMA PERGUNTA DO CATALOGO (`PodeSerRepouso`: vale 1x ou menos e nao cobra
				// nada), e nao uma lista de cinco ids -- pelo motivo de sempre: a proxima raca com casca
				// nasce medida sozinha. E a metade de baixo NAO e uma isencao: ela cobra o CONTRARIO, que
				// e o que faz a supressao valer alguma coisa como teste.
				// ============================================================================================================
				if (Catalogo.PodeSerRepouso(d))
				{
					repousoN++;
					if (adm.Ficha.expressedBP <= bpPelado) bpDesceu++;
					else if (exBp.Length == 0)
						exBp = $"{d.Id} (casca): {adm.Ficha.expressedBP:N0} SUBIU contra {bpPelado:N0} na base";
				}
				else
				{
					escadaQueSobe++;
					if (adm.Ficha.expressedBP > bpPelado) bpSubiu++;
					else if (exBp.Length == 0)
						exBp = $"{d.Id}: {adm.Ficha.expressedBP:N0} contra {bpPelado:N0} na base";
				}

				if (adm.Oozaru != FormaOozaru.Nao)
				{
					misturou++;
					if (exMistura.Length == 0) exMistura = $"{d.Id}: ficou macaco ({adm.Oozaru})";
				}
			}

			int feraN = esperadas.Count - escadaN;
			Checa($"forcar alcanca as {esperadas.Count} formas do catalogo, uma por uma, com o corpo pobre",
				  chegou == esperadas.Count, $"{chegou}/{esperadas.Count} -- {exChegou}");
			Checa($"...e todas pelo FUNIL do jogo ({escadaN} pela escada/`AnunciarForma`, {feraN} pelo `VirarFera`)",
				  funil == esperadas.Count, $"{funil}/{esperadas.Count} -- {exFunil}");
			Checa("...e os dois estados paralelos NUNCA se misturam (a fera nao escreve no slot da escada)",
				  misturou == 0, exMistura);
			Checa("...e o multiplicador chega na ficha em todas (o `AplicarForma`/`AplicarOozaru` rodou)",
				  multiplicou == esperadas.Count, $"{multiplicou}/{esperadas.Count} -- {exMult}");
			Checa("...e a CINEMATICA DE ESTREIA e anunciada em todas (forcar nao suprime a cena)",
				  estreou == esperadas.Count, $"{estreou}/{esperadas.Count} -- {exEstreia}");
			Checa($"...e as {escadaN} formas da escada entram com o tanque de Ki CHEIO",
				  kiCheio == escadaN, $"{kiCheio}/{escadaN} -- {exKi}");
			Checa($"...e o `expressedBP` sobe nas {escadaQueSobe} que multiplicam (o `powerlevel()` LE o que foi escrito)",
				  bpSubiu == escadaQueSobe, $"{bpSubiu}/{escadaQueSobe} -- {exBp}");
			Checa($"...e DESCE nas {repousoN} que sao CASCA (a supressao do Frost Demon aperta o poder)",
				  repousoN > 0 && bpDesceu == repousoN, $"{bpDesceu}/{repousoN} -- {exBp}");

			// ---------------------------------------------------------------- a SEGUNDA vez
			// A estreia so vale uma vez, e isso e o que separa "a cena toca" de "a cena toca sempre".
			// Com maestria 0 a regra normal manda a ENCURTADA -- se aparecer `Nenhuma` aqui, alguem
			// pos `semCena: true` no verb; se aparecer `Estreia`, o `Entrar` parou de marcar.
			Mandar("admin_forma", Catalogo.IdBase);
			var (_, deNovo, _) = Mandar("admin_forma", "ssj1");
			Checa("forcar a MESMA forma de novo cai na regra normal (cena ENCURTADA), nao na estreia",
				  deNovo.Any(a => a.Para == "ssj1" && a.Degrau == DegrauDeCena.Curta),
				  string.Join(" | ", deNovo.Select(a => $"{a.Para}/{a.Degrau}")));

			// E O NOME TAMBEM SERVE, SEM CAIXA. O `Catalogo.Def` e Ordinal e o dono digita olhando pra a
			// aba Formas, que mostra "Super Saiyajin" -- ninguem decora id de catalogo.
			Mandar("admin_forma", Catalogo.IdBase);
			FormaDef ssj2 = Catalogo.Def("ssj2")!;
			Mandar("admin_forma", ssj2.Nome.ToUpperInvariant());
			Checa($"o NOME da forma tambem serve, sem caixa ('{ssj2.Nome.ToUpperInvariant()}')",
				  adm.Forma.Atual == "ssj2", adm.Forma.Atual);

			var (jaEsta, escadaJa, _) = Mandar("admin_forma", "ssj2");
			Checa("forcar a forma em que o alvo JA esta nao dispara pacote nenhum",
				  escadaJa.Count == 0 && jaEsta.Any(a => a.Contains("ja esta")),
				  $"{escadaJa.Count} anuncio(s) | {string.Join(" | ", jaEsta)}");

			// A partir daqui o estado volta a ser o pobre: sem o SSJ4 liberado a queda do Oozaru Dourado
			// nao dispara, e os blocos abaixo medem `base` e a fera sem essa terceira coisa acontecendo
			// no meio. (O que a queda faz esta coberto no `RodarBancadaDeFormas`.)
			Mandar("admin_forma", Catalogo.IdBase);
			adm.Forma.Liberadas.Clear();
			adm.Forma.EstreiaVista.Clear();

			// =====================================================================
			// 2b. A SEQUENCIA DO DONO, VIRADA EM RELACAO
			// =====================================================================
			// ============================ O RELATO, E POR QUE ELE NAO VIRA UM NUMERO ============================
			// O dono forcou `base->ssj1` (x6, 560), `base->ssj1` de novo (x6, 545), `base->grade2`
			// (x3, 545) e `grade2->ssj1` (x6, 1.586) em 16 segundos, no mesmo personagem. Duas coisas
			// ali sao impossiveis: o MESMO multiplicador dando dois poderes, e multiplicadores
			// DIFERENTES dando o mesmo poder.
			//
			// A causa foi o `expressedBP` em atraso -- `AplicarForma` escrevia o `ssjBuff` e so o
			// `TickFichas` (5 Hz) o convertia em poder, entao o print (e o dano, e a quebra de parede, e
			// o HUD) lia a foto da forma ANTERIOR. Ver `RepercutirPoder`.
			//
			// ESTAS TRES LINHAS NAO GUARDAM NUMERO NENHUM do catalogo, e e a unica forma de elas
			// sobreviverem ao proximo rebalanceamento: o que elas afirmam e uma RELACAO -- a foto e
			// fresca; o mesmo comando duas vezes da o mesmo poder; e mais multiplicador da mais poder.
			// Trocar o x6 do SSJ1 por x7 amanha nao mexe em nenhuma delas.
			// ==============================================================================================

			// COMO O PRINT LE: logo depois do verb, sem recalcular nada. Ler de outro jeito mediria a
			// bancada, e nao o jogo.
			double ForcarELer(string id)
			{
				Mandar("admin_forma", id);
				return adm.Ficha.expressedBP;
			}

			Mandar("admin_forma", Catalogo.IdBase);
			PassarACena(adm);
			Medir(adm);

			// ---------------------------------------------------------------- (1) a foto e FRESCA
			// A checagem raiz: o poder logo depois do verb tem que ser o MESMO que o proximo tique de
			// ficha calcularia. Na medicao que abriu esta investigacao ela dava 9.800.383 contra
			// 6.662.097 -- o primeiro era o poder do grade2, ou seja da forma anterior.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire o `RepercutirPoder(pl)` do fim do `AplicarForma`.
			double impresso = ForcarELer("ssj1");
			Medir(adm);
			double recalculado = adm.Ficha.expressedBP;
			Checa("o poder lido logo depois do verb E o poder da forma NOVA (e nao a foto do tique anterior)",
				  Math.Abs(impresso - recalculado) <= Math.Max(recalculado, 1) * 1e-9,
				  $"impresso {impresso:N0} contra {recalculado:N0} recalculado");

			// ---------------------------------------------------------------- (2) o MESMO comando duas vezes
			// O segundo defeito do relato: o mesmo x6 dando 560 e depois 545. O que se movia era o
			// `kiratio` -- a forma nao masterizada drena Ki, e a razao ATRAVESSA a volta pra base, entao
			// o segundo SSJ1 nascia com o tanque pela metade. O verb enche o tanque, mas enchia DEPOIS
			// do `AplicarForma`, ou seja depois da conta de poder.
			//
			// O TEMPO ENTRE OS DOIS COMANDOS E DRENO DE VERDADE (`TickDaForma`), e nao um salto de
			// relogio: e justamente o que se move nesses segundos que fazia os dois numeros diferirem. A
			// cena e queimada antes porque `EmCena` faz o tique sair no comeco -- dentro da estreia nao
			// drena um pingo, e a checagem passaria sem nada ter acontecido.
			//
			// COMO REPROVA SE A REGRA SUMIR: mova o `p.Ficha.Ki = Math.Max(MaxKi, Ki)` do
			// `AdminForcarForma` pra depois do `AplicarForma` (a ordem antiga) -- o tanque drenado
			// vira poder drenado.
			//
			// A TOLERANCIA DE 1% e por causa da maestria, que sobe com o tempo em forma e pode mudar o
			// multiplicador em DEGRAU. Dez segundos rendem 0,09% de maestria e nao cruzam degrau
			// nenhum, mas a folga deixa isso escrito em vez de sorteado.
			//
			// ============================ E AQUI O CORPO PRECISA DE BP DE VERDADE ============================
			// Esta e a UNICA checagem do bloco que nao roda no corpo pobre, e a primeira versao dela
			// passou verde com a sabotagem ligada por causa disso: com BP 9 o `expressedBP` da base e
			// **19**, e o `DmMath.Round` do fim do `PowerLevel` (`Fighter.Power.cs:107`) e um piso de
			// resolucao. Os 1,7% de tanque drenado moviam o numero de 19 pra 18,7 -- que arredonda de
			// volta pra 19. A checagem estava medindo o arredondamento, nao a regra.
			//
			// O BP escolhido e o do relato (`--bpteste 3000000`), e ele e reposto no fim do bloco: o
			// corpo pobre e o que da sentido a TODAS as outras linhas desta bancada.
			// ============================================================================================
			adm.Ficha.BP = 3_000_000;
			adm.Ficha.Statify();

			Mandar("admin_forma", Catalogo.IdBase);
			PassarACena(adm);
			Medir(adm);

			double primeiraVez = ForcarELer("ssj1");
			PassarACena(adm);          // a estreia nao drena: queimar a cena e o que faz os segundos valerem
			// DEZ SEGUNDOS, E NAO OS DOIS DO RELATO. O que a checagem cobra e a RELACAO, e a janela so
			// precisa ser grande o bastante pra o defeito aparecer acima da tolerancia: o SSJ1 nao
			// masterizado deste corpo drena 0,84%/s, entao 2 s dariam 1,7% -- em cima do 1% de folga.
			// Dez segundos dao ~8%, uma ordem de grandeza acima. (No corpo do dono os 2 s bastavam: o
			// 545/560 do relato e 2,7%.)
			TickDaForma(adm, 10.0);
			Medir(adm);                // o tique de ficha de 5 Hz, que e quem escreve o `expressedBP`
			Mandar("admin_forma", Catalogo.IdBase);
			Medir(adm);
			double segundaVez = ForcarELer("ssj1");

			Checa("forcar a MESMA forma duas vezes da o MESMO poder -- o verb enche o tanque ANTES da conta",
				  Math.Abs(primeiraVez - segundaVez) <= Math.Max(primeiraVez, 1) * 0.01,
				  $"{primeiraVez:N0} na primeira contra {segundaVez:N0} na segunda "
				  + $"({(segundaVez - primeiraVez) / Math.Max(primeiraVez, 1) * 100:+0.0;-0.0}%)");

			// ---------------------------------------------------------------- (3) mais multiplicador, mais poder
			// A terceira impossibilidade do relato: `x3` e `x6` imprimindo o MESMO 545. O par sai do
			// proprio catalogo e e ORDENADO na hora -- se um rebalanceamento inverter os dois, a
			// checagem inverte junto e continua perguntando a mesma coisa.
			PerfilDeFormas perfilAgora = Perfil(adm);
			double multSsj1 = Catalogo.Multiplicador("ssj1", adm.Forma.Maestria, perfilAgora);
			double multGrade2 = Catalogo.Multiplicador("grade2", adm.Forma.Maestria, perfilAgora);

			if (Math.Abs(multSsj1 - multGrade2) < 1e-9)
			{
				// EMPATE NAO SE MEDE: com os dois multiplicadores iguais a pergunta nao tem resposta
				// certa, e passar verde aqui seria barulho. Nao acontece hoje (2x contra 3x).
				GD.Print($"  --   ESCADA: pulado -- ssj1 e grade2 empatam em x{multSsj1:0.##} neste corpo");
			}
			else
			{
				(string forte, string fraco) = multSsj1 > multGrade2 ? ("ssj1", "grade2") : ("grade2", "ssj1");
				double xForte = Math.Max(multSsj1, multGrade2), xFraco = Math.Min(multSsj1, multGrade2);

				Mandar("admin_forma", Catalogo.IdBase);
				Medir(adm);
				double poderForte = ForcarELer(forte);
				Mandar("admin_forma", Catalogo.IdBase);
				Medir(adm);
				double poderFraco = ForcarELer(fraco);

				Checa($"a forma de x{xForte:0.##} ({forte}) expressa MAIS poder que a de x{xFraco:0.##} ({fraco})",
					  poderForte > poderFraco,
					  $"{forte} deu {poderForte:N0} e {fraco} deu {poderFraco:N0}");
			}

			// E O CORPO POBRE VOLTA. O BP, a maestria que os segundos em forma renderam e a estreia
			// gasta -- os tres sao estado que os blocos seguintes leem: sem repor o BP, o `ssj3` do
			// bloco 3 rodaria num corpo que nao e o que esta documentado no topo do bloco 2.
			Mandar("admin_forma", Catalogo.IdBase);
			adm.Ficha.BP = 9;
			adm.Ficha.Statify();
			adm.Forma.Liberadas.Clear();
			adm.Forma.EstreiaVista.Clear();
			foreach (FormaDef d in Catalogo.Todas) adm.Forma.Maestria.Por(d.Id, 0);
			AplicarForma(adm);

			// =====================================================================
			// 3. `base` VOLTA AO NORMAL
			// =====================================================================
			Mandar("admin_forma", "ssj3");
			var (voltou, escadaVolta, _) = Mandar("admin_forma", Catalogo.IdBase);
			Checa("forcar `base` volta ao normal, e o multiplicador com ele",
				  adm.Forma.NaBase && Math.Abs(adm.Ficha.ssjBuff - 1) < 1e-9,
				  $"{adm.Forma.Atual}, ssjBuff {adm.Ficha.ssjBuff:0.####}");
			Checa("...e a ZONA e avisada da volta (senao o corpo fica dourado na tela de todo mundo)",
				  escadaVolta.Any(a => a.Para == Catalogo.IdBase) && voltou.Any(a => a.Contains("voltou a forma base")),
				  string.Join(" | ", escadaVolta.Select(a => $"{a.De}->{a.Para}")));

			// QUEM JA ESTA NA BASE NAO GERA NADA. Sem esta linha, um `Reverter` incondicional passaria em
			// tudo acima e mandaria um `base -> base` pra zona inteira a cada clique no botao.
			var (jaBase, escadaJaBase, ferasJaBase) = Mandar("admin_forma", Catalogo.IdBase);
			Checa("...e quem JA esta na base nao dispara pacote nenhum",
				  escadaJaBase.Count == 0 && ferasJaBase.Count == 0
				  && jaBase.Any(a => a.Contains("ja estava na forma base")),
				  $"{escadaJaBase.Count} anuncio(s), {ferasJaBase.Count} fera(s) | {string.Join(" | ", jaBase)}");

			// ============================ OS DOIS ESTADOS DE UMA VEZ ============================
			// `EstadoDeForma.Atual` e `ServerPlayer.Oozaru` sao paralelos, e o `base` do verb e a UNICA
			// entrada que precisa mexer nos dois. Em jogo eles nao convivem (o `VirarFera` derruba o SSJ
			// antes), entao o slot da escada e escrito a mao aqui -- e dito.
			//
			// COMO REPROVA SE A REGRA SUMIR: apague o `if (p.Oozaru != Nao) DesfazerOozaru(...)` e o dono
			// fica preso no macaco ate o prazo vencer; apague o `if (!p.Forma.NaBase) Reverter(...)` e ele
			// sai da fera direto pra um SSJ2 que nunca pediu. Cada `if` derruba uma metade desta linha.
			// ================================================================================
			Mandar("admin_forma", Oozaru.IdRegular);   // a fera pelo funil de verdade (as somas batem)
			adm.Forma.Atual = "ssj2";                  // e o slot da escada a mao
			var (_, escadaDois, ferasDois) = Mandar("admin_forma", Catalogo.IdBase);
			Checa("`base` desfaz os DOIS estados paralelos de uma vez (a fera E a escada)",
				  adm.Oozaru == FormaOozaru.Nao && adm.Forma.NaBase,
				  $"oozaru={adm.Oozaru}, forma={adm.Forma.Atual}");
			Checa("...e cada um pelo seu proprio funil (um pacote de fera E um de forma saem)",
				  ferasDois.Any(f => f.Forma == FormaOozaru.Nao)
				  && escadaDois.Any(a => a.Para == Catalogo.IdBase),
				  $"{ferasDois.Count} fera(s), {escadaDois.Count} anuncio(s)");

			// =====================================================================
			// 4. A FERA VAI PELO CAMINHO DA FERA
			// =====================================================================
			// Esta e a metade do verb que mais facilmente iria pro lugar errado: o Oozaru e o Dourado
			// MORAM no catalogo (pra terem nome, corpo e cor), e a leitura natural de "forca a forma do
			// catalogo" e mandar tudo pelo `Entrar`. Se isso acontecesse, o servidor teria um jogador com
			// `Forma.Atual == "oozaru"` e `ServerPlayer.Oozaru == Nao`: o multiplicador do macaco sem o
			// corpo do macaco, sem prazo, sem raiva e sem o pacote que faz a zona desenhar o bicho.
			var (_, escadaO, ferasO) = Mandar("admin_forma", Oozaru.IdRegular);
			Checa("forcar `oozaru` passa pelo caminho da FERA (`VirarFera` -> `AplicarOozaru`)",
				  adm.Oozaru == FormaOozaru.Regular && ferasO.Any(f => f.Forma == FormaOozaru.Regular),
				  $"oozaru={adm.Oozaru}, {ferasO.Count} pacote(s) de fera");
			Checa("...e NAO pela escada: `Forma.Atual` continua na base e nenhum `S2C.Forma` fala em macaco",
				  adm.Forma.NaBase && !escadaO.Any(a => a.Para == Oozaru.IdRegular),
				  $"forma={adm.Forma.Atual} | {string.Join(" ", escadaO.Select(a => a.Para))}");
			Checa("...e o prazo e a raiva foram armados (o `VirarFera` rodou inteiro, nao meia dúzia de campos)",
				  adm.OozaruAte > NowMs() && adm.RaivaDoOozaru > 0,
				  $"ate={adm.OozaruAte - NowMs()}ms, raiva={adm.RaivaDoOozaru:0.#}");

			var (_, escadaD, ferasD) = Mandar("admin_forma", Oozaru.IdDourado);
			Checa("forcar `oozaru_dourado` TROCA a fera (desfaz a anterior antes), e nao empilha",
				  adm.Oozaru == FormaOozaru.Dourado
				  && ferasD.Any(f => f.Forma == FormaOozaru.Nao) && ferasD.Any(f => f.Forma == FormaOozaru.Dourado),
				  $"oozaru={adm.Oozaru} | {string.Join(" -> ", ferasD.Select(f => f.Forma.ToString()))}");
			Checa("...e o Dourado escreve no `ssjBuff` com o `giantFormbuff` zerado (senao 1,5 x 18 = 27)",
				  Math.Abs(adm.Ficha.ssjBuff - Oozaru.Multiplicador(FormaOozaru.Dourado, adm.Class)) < 1e-6
				  && Math.Abs(adm.Ficha.giantFormbuff - 1) < 1e-9,
				  $"ssjBuff={adm.Ficha.ssjBuff:0.###} giant={adm.Ficha.giantFormbuff:0.###}");
			Checa("...e o slot da escada continua intocado tambem no Dourado",
				  adm.Forma.NaBase && !escadaD.Any(a => a.Para == Oozaru.IdDourado), adm.Forma.Atual);

			// ---------------------------------------------------------------- e a fera SEM RABO
			// A divergencia documentada: o verb AVISA em vez de RECUSAR. Recusar tiraria da ferramenta
			// justamente o corpo sem rabo, que tambem e um caso a olhar -- e o `TickDoOozaru` derruba a
			// fera no tique seguinte, o que sem aviso nenhum se le como "a forca nao pegou".
			//
			// COMO REPROVA SE A REGRA SUMIR: troque o `if (!TemRaboInteiro(p)) Avisar(...)` por um
			// `return` e a primeira metade cai (a fera nao nasce); apague o `Avisar` e cai a segunda.
			Mandar("admin_forma", Catalogo.IdBase);
			if (adm.Combate?.Corpo.Achar("Rabo") is { } rabo)
			{
				bool eraDecepado = rabo.Decepado;
				rabo.Decepado = true;
				var (semRabo, _, _) = Mandar("admin_forma", Oozaru.IdRegular);
				Checa("sem rabo a fera e forcada ASSIM MESMO -- e o admin e avisado de que o tique vai derruba-la",
					  adm.Oozaru == FormaOozaru.Regular
					  && semRabo.Any(a => a.Contains("AVISO") && a.Contains("rabo")),
					  $"oozaru={adm.Oozaru} | {string.Join(" | ", semRabo)}");
				rabo.Decepado = eraDecepado;
				Mandar("admin_forma", Catalogo.IdBase);
			}
			else
			{
				GD.Print("  --   RABO: pulado (o corpo de bancada nao tem o membro 'Rabo')");
			}

			// =====================================================================
			// 5. `admin_liberar_ki` LIGA OS DOIS CANAIS
			// =====================================================================
			// ============================ POR QUE OS DOIS, EM LINHAS SEPARADAS ============================
			// `canPower` vem de um DEGRAU do `niveis.json` (`Basic_Ki_Control` nivel 5, escrito pelo
			// `NiveisDeSkill.Aplicar`); `MeditateGivesKiRegen` vem de uma SKILL do `skills.json`
			// (`Ki_Unlocked`, escrito pelo `AplicarEfeitos`). Sao arquivos diferentes, funcoes diferentes
			// e chamadas diferentes -- e ter um sem o outro e o modo de falhar SILENCIOSO deste sistema:
			// ou meditar rende e o C nao carrega, ou o C carrega e meditar nao rende.
			//
			// Uma checagem so (o `ok` do `ConferirKiLiberado`, que ja e um `&&`) esconderia qual metade
			// caiu -- e sao consertos em lugares opostos do servidor.
			// ==========================================================================================
			adm.Livro.Esquecer(SkillKiUnlocked);
			adm.Livro.Esquecer(SkillKiControl);
			adm.Niveis.Por(SkillKiControl, 0);
			AplicarEfeitos(adm);          // termina em `Statify`: o `kicapacity` volta ao valor sem a skill
			adm.Niveis.Aplicar(adm.Ficha);
			// FLAG SE ESCREVE, NAO SE ACUMULA: nem `AplicarEfeitos` nem `Aplicar` DESFAZEM o que ja
			// estava aceso no `Fighter`. Apagar na mao e o unico jeito de a bancada comecar trancada --
			// e sem isso "liberou" seria indistinguivel de "ja estava liberado", que e exatamente o
			// estado em que o `--kiteste` deixa este corpo.
			adm.Ficha.canPower = 0;
			adm.Ficha.MeditateGivesKiRegen = 0;

			// ============================ O TETO DE CARGA E PLANTADO A MAO ============================
			// Esquecer a skill NAO desfaz o buff dela: `EfeitosDeSkill.Aplicar` escreve valores
			// ABSOLUTOS e nao tem passo de desfazer -- a mesma natureza do `canPower` logo acima. Entao
			// o `kicapacity` fica nos 202,8 mesmo com o livro vazio, e a primeira versao desta checagem
			// ("o teto CRESCEU") media uma coisa que nao se mexe: a rodada 1 desta bancada imprimiu
			// `202,80 -> 202,80` e reprovou uma regra que estava certa.
			//
			// O sentinela e o `Fighter.kicapacity` cru (`Fighter.cs:96`). Ele so volta ao valor de
			// verdade se alguem chamar `Statify` -- e quem chama, no `AdminLiberarKi`, e o
			// `AplicarEfeitos`, DEPOIS do `LiberarOKi`.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire o `AplicarEfeitos(p)` do `AdminLiberarKi`, ou mova-o
			// pra antes do `LiberarOKi`, e o teto fica no sentinela -- o C so passaria de 100% no
			// proximo tique de stats, que e o tipo de atraso que se le como "o verb nao pegou".
			// ======================================================================================
			const double tetoCru = 1.25;
			adm.Ficha.kicapacity = tetoCru;

			Checa("a bancada do Ki comeca TRANCADA (senao 'liberou' nao se distingue de 'ja estava')",
				  adm.Ficha.canPower == 0 && adm.Ficha.MeditateGivesKiRegen == 0
				  && !adm.Livro.Sabe(SkillKiUnlocked) && !adm.Livro.Sabe(SkillKiControl),
				  $"canPower={adm.Ficha.canPower} med={adm.Ficha.MeditateGivesKiRegen}");

			var (dito, _, _) = Mandar("admin_liberar_ki", "");

			Checa("`admin_liberar_ki` acende o canal do NIVEL -- `canPower` (degrau 5 do niveis.json)",
				  adm.Ficha.canPower != 0, $"canPower={adm.Ficha.canPower}");
			Checa("...E o canal da SKILL -- `MeditateGivesKiRegen` (do skills.json). Sao dois; um so e meio Ki",
				  adm.Ficha.MeditateGivesKiRegen != 0, $"med={adm.Ficha.MeditateGivesKiRegen}");
			Checa("...e a confirmacao volta com os DOIS numeros, e nao com 'pronto'",
				  dito.Any(a => a.Contains("canPower=1") && a.Contains("MeditateGivesKiRegen=1")),
				  string.Join(" | ", dito));
			Checa("...e o teto de carga volta DENTRO do mesmo gesto (o `AplicarEfeitos` termina em `Statify`)",
				  adm.Ficha.kicapacity > tetoCru + 1e-9,
				  $"kicapacity ficou em {adm.Ficha.kicapacity:0.00} -- o sentinela cru sobreviveu ao verb");
			Checa("...e o `Basic_Ki_Control` entrou no LIVRO, e nao so no contador de niveis",
				  adm.Livro.Sabe(SkillKiControl) && adm.Livro.Sabe(SkillKiUnlocked),
				  $"unlock={adm.Livro.Sabe(SkillKiUnlocked)} control={adm.Livro.Sabe(SkillKiControl)}");

			// ============================ A REGRESSAO QUE A FASE 1 ACHOU ============================
			// `NiveisDeSkill.Efetor` COMECA chamando `Sincronizar(livro)`, que apaga todo path que o
			// livro nao conhece. A versao anterior desta concessao (`--kiteste`) escrevia so o NIVEL: o
			// degrau 5 era removido no primeiro tique do efetor e nunca chegava ao save. Nao aparecia
			// como defeito porque o `canPower` ja escrito no `Fighter` nao e desfeito por ninguem -- ate
			// o relog seguinte, onde o admin perderia a carga sem uma linha explicando.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire o `pl.Livro.Dar(SkillKiControl)` do `LiberarOKi`. O
			// `canPower` continua aceso (as duas linhas acima passam!) e SO esta cai -- que e exatamente
			// o que fazia o defeito ser invisivel.
			// ====================================================================================
			if (_skills != null)
			{
				adm.Niveis.Efetor(new Random(20260807), _skills, adm.Livro);
				Checa("REGRESSAO: o nivel 5 sobrevive ao `Sincronizar` do efetor (senao o Ki se perde no relog)",
					  adm.Niveis.Nivel(SkillKiControl) >= 5, $"nivel {adm.Niveis.Nivel(SkillKiControl)}");
			}
			else GD.Print("  --   KI: efetor pulado (servidor sem catalogo de skills)");

			// =====================================================================
			// 6. SO ADMIN ALCANCA OS DOIS
			// =====================================================================
			// ============================ POR QUE ISTO SE TESTA NO SERVIDOR ============================
			// O menu esconde a aba de quem nao e admin -- e esconder botao nunca foi permissao: um
			// cliente modificado manda o mesmo pacote de verb. A guarda de verdade e uma linha so, no
			// `default:` do `Verbo`, e ela vale pros verbs que ainda nao existem tambem.
			//
			// COMO REPROVA SE A REGRA SUMIR: mova `admin_forma` (ou `admin_liberar_ki`) pro switch de
			// cima do `Verbo` -- o refactor "obvio" pra quem quer um atalho de teste -- e a checagem cai
			// na hora. Apagar o `if (!EhAdmin(pl))` derruba as tres de uma vez.
			// ======================================================================================
			adm.Livro.Esquecer(SkillKiUnlocked);
			adm.Livro.Esquecer(SkillKiControl);
			adm.Niveis.Por(SkillKiControl, 0);
			adm.Ficha.canPower = 0;
			adm.Ficha.MeditateGivesKiRegen = 0;
			Mandar("admin_forma", Catalogo.IdBase);

			adm.Poderes = Protocol.Poder.Nenhum;
			adm.PoderesConcedidos = Protocol.Poder.Nenhum;

			var (negaForma, escadaNeg, ferasNeg) = Mandar("admin_forma", "ssj3");
			Checa("SEM o bit de admin, `admin_forma` e recusado no funil -- nada muda e nada sai no fio",
				  adm.Forma.NaBase && escadaNeg.Count == 0 && ferasNeg.Count == 0
				  && negaForma.Any(a => a.Contains("administrador")),
				  $"forma={adm.Forma.Atual}, {escadaNeg.Count} anuncio(s) | {string.Join(" | ", negaForma)}");

			var (negaFera, _, ferasNeg2) = Mandar("admin_forma", Oozaru.IdRegular);
			Checa("...e o segundo caminho do mesmo verb (a FERA) tambem para na mesma guarda",
				  adm.Oozaru == FormaOozaru.Nao && ferasNeg2.Count == 0
				  && negaFera.Any(a => a.Contains("administrador")),
				  $"oozaru={adm.Oozaru}, {ferasNeg2.Count} fera(s)");

			var (negaKi, _, _) = Mandar("admin_liberar_ki", "");
			Checa("...e `admin_liberar_ki` idem -- os DOIS canais continuam apagados e o livro vazio",
				  adm.Ficha.canPower == 0 && adm.Ficha.MeditateGivesKiRegen == 0
				  && !adm.Livro.Sabe(SkillKiControl) && !adm.Livro.Sabe(SkillKiUnlocked)
				  && negaKi.Any(a => a.Contains("administrador")),
				  $"canPower={adm.Ficha.canPower} med={adm.Ficha.MeditateGivesKiRegen} | {string.Join(" | ", negaKi)}");

			adm.Poderes = poderesAntes;
			adm.PoderesConcedidos = concedidosAntes;

			// =====================================================================
			// 6b. CURAR NAO E REVIVER (dono, 2026-09-05)
			// =====================================================================
			// "heal all ou heal em alguem especifico faz ela reviver, o que nao deveria acontecer". O DM
			// (`Admin.dm:409-413`, World_Heal) nunca escreve `dead = 0`. A cobaia e um corpo forjado (`Peer`
			// nulo) posto no mundo pelo `PorNoMundo`. O `admin_curar_todos` so alcanca JOGADORES (`Jogadores`
			// exige Peer), entao a metade "Heal Everyone" e medida no proprio admin com a MARCA de morto
			// escrita a seco -- sem os ganchos da morte, porque e so a marca que o Heal nao pode apagar.
			{
				var cobaia = new ServerPlayer
				{
					Id = IdDaCobaiaDaCura, Peer = null, Name = "CobaiaDaCura", Race = "Human", Genero = "Male", Idade = 25,
					Zone = adm.Zone, Pos = adm.Pos + new Jandirus.Core.World.Vec2(2 * Jandirus.Core.World.ZoneCollision.TileSize, 0),
					Conta = "bancada_admin_cura", Slot = 0,
					Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 1000 },
					Livro = new Jandirus.Core.Skills.SkillBook(),
				};
				cobaia.Ficha.Class = "Normal";
				PorNoMundo(cobaia);
				bool admMorto = adm.Ficha.dead;
				// O CORPO DO ADMIN VOLTA COMO ESTAVA: o "Heal Everyone" cura o proprio admin de verdade (membros,
				// rabo, carencia, Ki), e a porta do Beast, familias adiante, le esse corpo -- com o rabo de volta
				// o Super Saiyajin 4 vira candidato antes do Beast e a recusa deixa de falar de dor. Foi assim
				// que esta bancada ficou vermelha numa checagem que nao era dela.
				var partesDoAdm = adm.Combate.Corpo.Partes.Select(p => (p.Vida, p.Decepado)).ToList();
				double carenciaDoAdm = adm.Combate.Carencia, kiDoAdm = adm.Ficha.Ki, tailgainDoAdm = adm.Ficha.tailgain;
				bool koDoAdm = adm.Ficha.KO;
				try
				{
					cobaia.Combate.Morrer();
					Checa("PREMISSA: a cobaia esta morta", cobaia.Ficha.dead, "");
					cobaia.Combate.Corpo.Partes[0].Vida = 1;
					Mandar("admin_curar", cobaia.Id.ToString());
					Checa("Heal num MORTO nao o revive (DM `World_Heal`: Un_KO + SpreadHeal + Ki, nunca `dead = 0`)", cobaia.Ficha.dead, "");
					Checa("...mas o corpo dele fica INTEIRO -- a cura curou, so nao ressuscitou",
						  cobaia.Combate.Corpo.Partes.All(p => p.Vida >= p.VidaMax - 1e-9 && !p.Decepado),
						  string.Join(",", cobaia.Combate.Corpo.Partes.Select(p => $"{p.Vida:0}/{p.VidaMax:0}")));

					adm.Ficha.dead = true;
					Mandar("admin_curar_todos", "");
					Checa("Heal Everyone tambem nao revive quem esta morto (o admin, com a marca de morto, continua morto)", adm.Ficha.dead, "");
					adm.Ficha.dead = admMorto;

					Mandar("admin_reviver", cobaia.Id.ToString());
					Checa("CONTRA-EXEMPLO: Revive Target continua revivendo (a cobaia volta a viver)", !cobaia.Ficha.dead, "");

					Nocautear(cobaia);
					Checa("PREMISSA: a cobaia esta nocauteada", cobaia.Ficha.KO, "");
					Mandar("admin_curar", cobaia.Id.ToString());
					Checa("Heal num NOCAUTEADO o poe de pe (o `Un_KO()` do World_Heal) sem mexer no `dead`", !cobaia.Ficha.KO && !cobaia.Ficha.dead, "");
				}
				finally
				{
					adm.Ficha.dead = admMorto;
					for (int i = 0; i < partesDoAdm.Count && i < adm.Combate.Corpo.Partes.Count; i++)
					{
						adm.Combate.Corpo.Partes[i].Vida = partesDoAdm[i].Vida;
						adm.Combate.Corpo.Partes[i].Decepado = partesDoAdm[i].Decepado;
					}
					adm.Combate.SincronizarVida();
					adm.Combate.Carencia = carenciaDoAdm;
					adm.Ficha.Ki = kiDoAdm;
					adm.Ficha.tailgain = tailgainDoAdm;
					adm.Ficha.KO = koDoAdm;
					DespedirCorpo(cobaia);
				}
			}

			// =====================================================================
			// 7. O RASTRO
			// =====================================================================
			// O `Registrar` mora no funil (`VerboDeAdmin`, uma linha), e nao dentro de cada verb --
			// justamente pra cobrir os que ninguem lembra de logar. Os dois verbs desta fase sao novos:
			// esta linha e o que prova que eles herdaram o rastro em vez de ficarem de fora.
			//
			// COMO REPROVA SE A REGRA SUMIR: acrescente `admin_forma` a excecao do `cmd != "admin_contas"`
			// (a coisa que alguem faria pra "nao poluir o log com teste") e a linha cai.
			if (caminhoDoLog.Length > 0)
			{
				string[] tudo = File.Exists(caminhoDoLog) ? File.ReadAllLines(caminhoDoLog) : [];
				string[] novas = tudo.Skip(logAntes).ToArray();
				Checa("os dois verbs novos deixam rastro no `admin.log` (o registro mora no funil)",
					  novas.Any(l => l.Contains("admin_forma")) && novas.Any(l => l.Contains("admin_liberar_ki")),
					  $"{novas.Length} linha(s) novas de {tudo.Length}");
			}
			else GD.Print("  --   LOG: pulado (servidor sem pasta de contas)");
		}
		finally
		{
			// ---------------------------------------------------------------- e o corpo volta
			// O BIT DE ADMIN EM PRIMEIRO LUGAR: perde-lo deixaria o host sem a aba pelo resto da sessao
			// e faria o proximo `--diagadmin` acusar uma regressao que esta bancada mesma causou.
			adm.Poderes = poderesAntes;
			adm.PoderesConcedidos = concedidosAntes;
			adm.Ficha.BP = bpAntes;
			adm.Ficha.Class = classeAntes;
			adm.Ficha.SaiyanLineage = linhagemAntes;
			adm.UltraInstinct.Aprendida = uiAntes;
			adm.PoderDaDestruicao.Aprendida = ueAntes;
			if (adm.Ficha.godki != null) adm.Ficha.godki.awakened = godAntes;
			foreach ((string id, double v) in maestriaAntes) adm.Forma.Maestria.Por(id, v);
			adm.Forma.Liberadas.Clear();
			foreach (int r in liberadasAntes) adm.Forma.Liberadas.Add(r);
			adm.Forma.EstreiaVista.Clear();
			foreach (int r in estreiasAntes) adm.Forma.EstreiaVista.Add(r);

			if (adm.Oozaru != FormaOozaru.Nao) DesfazerOozaru(adm, "bancada: fim das ferramentas de admin");
			if (!adm.Forma.NaBase) Reverter(adm, "bancada: fim das ferramentas de admin");

			if (sabiaUnlock) adm.Livro.Dar(SkillKiUnlocked); else adm.Livro.Esquecer(SkillKiUnlocked);
			if (sabiaControl) adm.Livro.Dar(SkillKiControl); else adm.Livro.Esquecer(SkillKiControl);
			adm.Niveis.Por(SkillKiControl, nivelKiAntes);
			AplicarPoderes(adm);
			AplicarEfeitos(adm);
			adm.Niveis.Aplicar(adm.Ficha);
			AplicarForma(adm);

			// NINGUEM SAI DAQUI COM ESCUTA LIGADA -- mesma regra do `RodarBancadaDeFormas`.
			EscutaDeAvisos = null;
			EscutaDeAnuncios = null;
			EscutaDeFeras = null;
		}
	}

	/// <summary>
	/// ============================ O ESTOMAGO, PRA BANCADA QUE FOTOGRAFA A BARRA (`--diagmostrador`) ============================
	/// Esta e a UNICA escrita de bancada neste arquivo -- as duas janelas do `GameServer.FormasTeste.cs`
	/// sao de leitura e dizem, com razao, que uma porta que devolvesse algo mutavel seria a porta dos
	/// fundos. Entao vale explicar por que esta precisa escrever.
	///
	/// A BARRA DE NUTRICAO NOVA NASCE EM 100% E FICA LA. Um personagem novo comeca com o tanque cheio
	/// (`Nutricao.TanqueInicial`), e a digestao natural queima MEDIDO ~0,08 de 50 a cada passada de 3 s
	/// -- ou seja cerca de 0,2% do tanque por MINUTO. Nao ha, no caminho de producao, nada que mova
	/// aquela barra dentro do tempo de uma bancada: comer pede um item que so nasce de colheita ou da
	/// Sala do Tempo, e a fome pede uma tarde inteira.
	///
	/// E BARRA PARADA EM 100% E EXATAMENTE O DEFEITO QUE ESTA RODADA VEIO CACAR. Uma barra congelada e
	/// uma barra correta desenham o MESMO pixel enquanto o valor for 1,0; foi assim que o corte de Ki
	/// em 100% viveu quatro mil checagens verdes. Fotografar a nutricao cheia provaria que ela existe e
	/// **nada** sobre ela estar ligada no dado. Por isso a bancada empurra o tanque e volta a olhar.
	///
	/// ESCREVE NO TANQUE E MAIS NADA, e manda a ficha pelo `MandarFicha` de producao -- o caminho que a
	/// barra usa. Nao mexe em vigor, Ki nem BP: quem quiser ver esses tres se mexerem tem os caminhos
	/// do jogo (a tecla C, o dreno da forma) e a bancada os usa.
	/// ==========================================================================================================
	/// </summary>
	internal bool EstomagoDeTeste(int id, double nutricao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		pl.Ficha.CurrentNutrition = Math.Clamp(
			nutricao, 0, Jandirus.Core.Stats.Nutricao.Tanque(pl.Ficha.Metabolism));
		MandarFicha(pl);
		return true;
	}
}
