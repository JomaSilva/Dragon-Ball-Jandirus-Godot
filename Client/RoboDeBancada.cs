using System.Globalization;
using System.Text.RegularExpressions;
using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DO MOSTRADOR (`--diagbancada`) ============================
/// Ela cobre os quatro pedidos do dono sobre a tela que ele olha o tempo todo -- a barra de Ki que
/// travava em 100%, a barra de nutricao que nao existia, a % de BP efetivo ao lado do BP e o
/// multiplicador total na aba Forms -- e cobre tambem o sigilo do poder de luta, que e a regra que
/// essas duas leituras novas poderiam ter furado.
///
/// ============================ POR QUE ELA NAO E MAIS UMA LISTA DE `Conferir` ============================
/// Esta casa ja perdeu quatro defeitos visuais para mais de quatro mil checagens verdes, e a causa
/// escrita nos arquivos e sempre a mesma: **a bancada media a INTENCAO do codigo**. O corte de Ki em
/// 100% sobreviveu porque a checagem lia `Sheet.Ki / Sheet.MaxKi` e chamava aquilo de "o que a barra do
/// HUD desenha" -- e o corte morava depois, dentro do widget.
///
/// Entao aqui valem tres regras, e elas moldam o arquivo inteiro:
///
///   1. **SO SE AFIRMA O QUE FOI DESENHADO.** Todo numero desta bancada sai de um `Label` que estava na
///      tela -- `Barra.TextoDeTeste` de um lado, `MenuJogo.ValorDesenhado` do outro -- e volta pra
///      numero por um parser. A ficha entra na comparacao como TERCEIRA opiniao, nunca como as duas.
///
///   2. **AS DUAS TELAS SE COMPARAM UMA COM A OUTRA.** A queixa do dono foi literalmente "o menu do P
///      mostra certo e a barra do jogo mostra 100%". Uma bancada que leia o mesmo campo dos dois lados
///      prova que a FICHA e uma so -- e a ficha ja era uma so quando o bug estava vivo.
///
///   3. **TODA REGRA TEM QUE SABER REPROVAR, E ISSO SE PROVA.** Depois da rodada real a bancada INJETA
///      vinte e dois defeitos conhecidos nas amostras e exige que as regras nomeadas fiquem VERMELHAS. Uma
///      regra que nao reprova o proprio defeito que ela existe pra pegar e uma linha verde que nao
///      significa nada -- e uma injecao que passa inteira e uma falha DA BANCADA, relatada como tal.
/// ====================================================================================================
///
/// ============================ O QUE SEPARA A % DO MULTIPLICADOR (e por que os dois juntos) ============================
/// O dono definiu a % de BP efetivo assim: *"ela so mudaria caso a stamina ou ki caissem, peso,
/// gravidade etc"* -- ou seja ela NAO PODE se mexer ao transformar. E pediu, depois, o multiplicador
/// total, que e o oposto: ele existe pra pular quando a forma entra.
///
/// Provar so um dos dois nao prova nada. Uma linha congelada em "100%" passaria em qualquer teste de
/// invariancia; um multiplicador congelado passaria em qualquer teste de "nao mudou com o Ki". Por isso
/// as duas familias sao cobradas no MESMO par de amostras (base e SSJ1) e em sentidos opostos: uma tem
/// que ficar parada enquanto a outra pula, e a rodada de injecao mostra as duas maneiras de mentir.
/// ================================================================================================================
///
/// ============================ ELA PRECISA DE JANELA, E ISSO TEM UM CUSTO QUE SE PAGA ============================
/// Trinta e uma das trinta e duas regras rodam `--headless` sem perder nada: texto de `Label`, valor de
/// preenchimento e ancoradouro do segmento de sobrecarga sao estado de node, e o Godot os calcula sem
/// tela (medido -- a rodada headless bate numero por numero com a de janela). A UNICA que nao roda e a
/// F3.1, que pergunta se a barra de nutricao esta DESENHADA DENTRO DA JANELA: sem viewport de verdade
/// o retangulo dela nasce vazio, e a regra reprova por falta de tela, nao por defeito. Como o pedido
/// do dono foi *"n tem a barra de nutricao"*, essa e justamente a regra que nao da pra abrir mao.
///
/// O CUSTO E ESTE, e vale escreve-lo: o corpo desta bancada e o HOST, ou seja ADMIN, e o menu P tem uma
/// aba de admin com uma ZONA DE PERIGO que apaga a pasta de saves inteira. Uma janela de jogo aberta e
/// desacompanhada, com esse menu na tela, e um console de administracao por cima de tudo.
///
/// NA PRIMEIRA RODADA ISSO DEIXOU DE SER HIPOTESE. O `admin.log` do processo registrou, no meio da
/// espera do dreno: `admin_limpar` e, trinta segundos depois, `admin_limpar_ja [5ffk]` -- limpeza
/// total, 872 contas apagadas, so o `admin.log` sobrevivendo. Esta bancada nao manda nenhum dos dois
/// verbs (procure: ela nao os cita), e no mesmo trecho o servidor registrou um "golpe LETAL", que e a
/// tecla K e que ela tambem nao manda. Entrou INPUT na janela.
///
/// A DEFESA POSSIVEL DAQUI e o menu ficar aberto SO ENQUANTO SE MEDE (ver `Capturar`): a exposicao
/// caiu de tres minutos continuos pra alguns segundos por amostra, e as duas esperas longas -- a
/// cinematica de 40 s e o dreno de ate 120 s, que foi exatamente quando aconteceu -- passaram a correr
/// com o menu fechado. Quem quiser o resto da bancada sem janela nenhuma pode rodar `--headless` e ler
/// as 31; a F3.1 vai dizer, na propria linha, que faltou tela.
/// ============================================================================================================
///
/// COMO RODAR (porta propria, conta NOVA, com janela):
///     Godot --path . --host --rede 7985 --conta bancadaki --nome BancadaKi
///           --kiteste --bpteste 3000000 --diagbancada
/// </summary>
public partial class RoboDeBancada : Node
{
	private static GameClient? C => GameClient.Instance;
	private static MenuJogo? M => MenuJogo.Instancia;
	private static Hud? H => Hud.Instancia;

	private double _t, _espera = 1.5;
	private int _passo;
	private bool _acabou, _carregando;

	private int _percursoOk, _percursoFalha;
	private readonly List<string> _falhasDePercurso = [];
	private readonly List<Quadro> _quadros = [];

	/// <summary>O que as janelas de condicao DEVOLVERAM. A checagem cobra o valor real, nao o pedido.</summary>
	private double _hpFerido = double.NaN, _hpDeReferencia = double.NaN, _razaoDeEstamina = double.NaN;

	private static void Nota(string linha) => GD.Print("[bancada] " + linha);

	private void Percurso(bool ok, string oque)
	{
		Nota((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (ok) _percursoOk++;
		else { _percursoFalha++; _falhasDePercurso.Add(oque); }
	}

	// =====================================================================
	// O QUADRO -- um instante das DUAS telas, lado a lado
	// =====================================================================
	/// <summary>
	/// UM INSTANTE DO MOSTRADOR INTEIRO. Guarda tres coisas separadas de proposito:
	///
	///   `Fi*`   -- o que a FICHA trouxe do servidor (a terceira opiniao);
	///   `Hud*`  -- o que a HUD DESENHOU (texto e desenho, nao o valor que se mandou pra ela);
	///   `Menu*` -- o que a aba do menu P DESENHOU.
	///
	/// Sao campos e nao propriedades calculadas porque a rodada de injecao os REESCREVE (`with`): um
	/// defeito injetado precisa poder mentir exatamente onde o defeito de verdade mentiria, e nao no
	/// lugar onde a bancada acharia mais facil de pegar.
	/// </summary>
	private record struct Quadro
	{
		public string Rotulo;

		// ---- a ficha
		public double FiKi, FiTeto, FiInteireza, FiMult, FiNutricao, FiHp, FiBp, FiExpBp;
		public bool TemScouter;

		// ---- a HUD desenhada
		public double HudKi, HudPreench, HudSobrecarga, HudMarca, HudTrilho;
		public double HudNutricao, HudNutPreench, HudNutTrilho;
		public bool HudNutNaTela;
		public double HudVidaTrilho, HudVigorTrilho;
		public double HudEfetivo;
		public string HudBp;

		// ---- o menu P desenhado
		public double MenuKi, MenuEfetivo, MenuMult, MenuNutricao, MenuKiDaAbaKi, MenuTetoDaAbaKi;
		public double MenuBpExpresso, MenuBpBase;
		public bool MenuBpEscondido;
	}

	private static Quadro Q(Quadro[] qs, string rotulo) => Array.Find(qs, x => x.Rotulo == rotulo);

	// =====================================================================
	// A CAPTURA
	// =====================================================================
	/// <summary>
	/// LE AS DUAS TELAS NO MESMO QUADRO.
	///
	/// ============================ POR QUE O REDESENHO E FORCADO AQUI ============================
	/// A pagina do menu so e remontada quando a `Assinatura` dela muda, e a assinatura e grosseira de
	/// proposito (numeros arredondados). Entre dois pacotes o Ki anda meio ponto: a HUD, que redesenha a
	/// cada ficha, ja escreveu o numero novo, e a aba ainda esta com o texto do instante anterior.
	/// Comparar nesse intervalo acusaria uma discordancia que nao existe -- e uma bancada que acusa
	/// falso e uma bancada que se aprende a ignorar.
	///
	/// Forcando, as duas telas leem literalmente o MESMO `SheetState`, e a comparacao pode exigir
	/// igualdade EXATA, que e o que se quer afirmar.
	///
	/// E A ABA PRESA CONTINUA COBERTA, por outra amostra: a `acima-sem-forcar` le a aba SEM tocar nela
	/// depois de o Ki ter virado, e e ela que pega o dia em que um campo novo ficar de fora da
	/// assinatura e a aba congelar na tela. As duas leituras respondem perguntas diferentes.
	/// ========================================================================================
	///
	/// A VOLTA PARA "Stats" NO FIM NAO E ARRUMACAO: e ela que deixa o menu parado na aba certa pra a
	/// leitura crua do passo seguinte valer alguma coisa.
	/// </summary>
	private Quadro Capturar(string rotulo)
	{
		SheetState f = C?.Sheet ?? default;
		MenuJogo m = M!;

		// ============================ O MENU SO FICA ABERTO ENQUANTO SE MEDE ============================
		// Ele precisa estar aberto pra a aba desenhar, e so. Deixa-lo aberto pelos tres minutos da
		// rodada poe a aba de ADMIN -- e a ZONA DE PERIGO dentro dela, que apaga a pasta de saves --
		// a um clique numa janela que o robo deixou por cima de tudo. Isso deixou de ser hipotese na
		// primeira rodada desta bancada: ver o cabecalho da classe.
		//
		// Abrir e fechar por amostra nao muda medida nenhuma (o `ForcarRedesenho` monta a pagina do
		// zero), e tira o menu da tela justamente nas esperas longas -- a cinematica de 40 s e o dreno
		// de ate 120 s --, que e onde a exposicao morava.
		// ==========================================================================================
		m.Abrir();

		foreach (string aba in new[] { "Ki", "Forms", "Stats" })
		{
			m.IrPara(aba);
			m.ForcarRedesenho();
		}

		Barra ki = H!.BarraDeKi, nut = H.BarraDeNutricao;
		string bpNoMenu = m.ValorDesenhado("Stats", "Battle Power") ?? "";

		var q = new Quadro
		{
			Rotulo = rotulo,

			FiKi = f.RazaoDeKi, FiTeto = f.TrilhoDeKi, FiInteireza = f.Inteireza,
			FiMult = f.MultTotal, FiNutricao = f.RazaoDeNutricao, FiHp = f.HP,
			FiBp = f.BP, FiExpBp = f.ExpressedBP,
			TemScouter = C?.Atributos.Tem(Protocol.Poder.Scouter) ?? false,

			HudKi = Razao(ki.TextoDeTeste), HudPreench = ki.PreenchimentoDeTeste,
			HudSobrecarga = ki.SobrecargaDeTeste, HudMarca = ki.MarcaDeTeste,
			HudTrilho = ki.TetoDeTeste,
			HudNutricao = Razao(nut.TextoDeTeste), HudNutPreench = nut.PreenchimentoDeTeste,
			HudNutTrilho = nut.TetoDeTeste, HudNutNaTela = NaTela(nut),
			HudVidaTrilho = H.BarraDeVida.TetoDeTeste, HudVigorTrilho = H.BarraDeVigor.TetoDeTeste,
			HudEfetivo = Razao(H.TextoDeEfetivoDeTeste), HudBp = H.TextoDeBpDeTeste,

			MenuKi = RazaoEntreParenteses(m.ValorDesenhado("Stats", "Ki")),
			MenuEfetivo = Razao(m.ValorDesenhado("Stats", "Poder efetivo")),
			MenuNutricao = RazaoEntreParenteses(m.ValorDesenhado("Stats", "Nutrição")),
			MenuMult = Multiplicador(m.ValorDesenhado("Forms", "Multiplicador total")),
			MenuKiDaAbaKi = Razao(m.ValorDesenhado("Ki", "Percentual")),
			MenuTetoDaAbaKi = Razao(m.ValorDesenhado("Ki", "Teto de carga")),
			MenuBpEscondido = bpNoMenu.Contains("???", StringComparison.Ordinal),
			MenuBpExpresso = PrimeiroNumero(bpNoMenu),
			MenuBpBase = NumeroDepoisDe(bpNoMenu, "base"),
		};

		_quadros.Add(q);

		// FECHA DE NOVO -- ver o bloco la em cima. A leitura ja saiu; o menu nao tem mais o que fazer
		// na tela ate a proxima amostra. A UNICA excecao e a leitura "sem forcar", que precisa do menu
		// aberto ENQUANTO o Ki muda: aquele passo reabre por conta propria.
		m.Fechar();

		Nota($"  --     [{rotulo}]  HUD: Ki {q.HudKi * 100:0.0}% (trilho {q.HudPreench:0.000}, sobra "
		   + $"{q.HudSobrecarga:0.000})  efetivo {q.HudEfetivo * 100:0.0}%  nutri {q.HudNutricao * 100:0.0}%  "
		   + $"BP \"{q.HudBp}\"");
		Nota($"  --              MENU: Ki {q.MenuKi * 100:0.0}%  efetivo {q.MenuEfetivo * 100:0.0}%  "
		   + $"mult {q.MenuMult:0.###}x  nutri {q.MenuNutricao * 100:0.0}%   |   FICHA: Ki {q.FiKi * 100:0.0}%  "
		   + $"efetivo {q.FiInteireza * 100:0.0}%  mult {q.FiMult:0.###}x  HP {q.FiHp:0.0}");
		return q;
	}

	/// <summary>
	/// A BARRA ESTA MESMO NA TELA? Visivel na arvore E com area dentro da janela.
	///
	/// A pergunta parece boba e nao e: "a barra de nutricao nao existe" tambem seria o sintoma de uma
	/// barra montada fora do retangulo visivel, ou dentro de um pai invisivel -- e nesses dois casos
	/// TODA leitura de texto e de preenchimento continua devolvendo o numero certo.
	/// </summary>
	private static bool NaTela(Control c)
	{
		if (!c.IsVisibleInTree()) return false;
		Rect2 r = c.GetGlobalRect();
		Rect2 janela = new(Vector2.Zero, c.GetViewportRect().Size);
		return r.Size.X > 0 && r.Size.Y > 0 && janela.Intersects(r);
	}

	// =====================================================================
	// OS PARSERS -- o caminho de volta do TEXTO pro numero
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE TUDO PASSA POR TEXTO ============================
	/// Dar a volta pelo texto desenhado e o ponto desta bancada, e nao um custo dela. Um `double` lido
	/// de um campo diz o que o codigo QUIS desenhar; a string do `Label` e o que o jogador leu. Entre um
	/// e outro cabe um `Clamp`, um formato errado, uma tela que nao redesenhou -- que sao exatamente os
	/// quatro defeitos que esta casa ja deixou passar.
	///
	/// A CULTURA VEM PRIMEIRO E A INVARIANTE DEPOIS: o jogo formata em pt-BR ("×2,77", "3.000.000"), e
	/// um parser invariante leria "2,77" como 277. Tentar a corrente antes cobre os dois mundos sem que
	/// a bancada precise saber em qual esta rodando.
	/// ==============================================================================
	/// </summary>
	private static double Numero(string? s)
	{
		if (s is null) return double.NaN;
		s = s.Trim();
		if (double.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out double v)) return v;
		if (double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v)) return v;
		return double.NaN;
	}

	private static readonly Regex TokenDeNumero = new(@"[-+]?\d[\d.,]*", RegexOptions.Compiled);

	/// <summary>O primeiro numero de uma frase. "1.234   (base 567)" -> 1234.</summary>
	private static double PrimeiroNumero(string? txt)
	{
		if (txt is null) return double.NaN;
		Match m = TokenDeNumero.Match(txt);
		return m.Success ? Numero(m.Value) : double.NaN;
	}

	/// <summary>O numero que vem DEPOIS de uma palavra. "(base 567)" -> 567.</summary>
	private static double NumeroDepoisDe(string? txt, string marca)
	{
		if (txt is null) return double.NaN;
		int i = txt.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
		if (i < 0) return double.NaN;
		Match m = TokenDeNumero.Match(txt, i + marca.Length);
		return m.Success ? Numero(m.Value) : double.NaN;
	}

	/// <summary>"118%" -> 1,18. E o formato da barra do HUD e das linhas de porcentagem do menu.</summary>
	private static double Razao(string? txt) => PrimeiroNumero(txt) / 100.0;

	/// <summary>"1.234 / 1.000   (123%)" -> 1,23. O numero que interessa e o do PARENTESES.</summary>
	private static double RazaoEntreParenteses(string? txt)
	{
		if (txt is null) return double.NaN;
		int i = txt.LastIndexOf('(');
		return i < 0 ? double.NaN : Razao(txt[(i + 1)..]);
	}

	/// <summary>"×2,77" -> 2,77;  "×1,2 mil" -> 1200. Desfaz o encurtamento do `MultTexto`.</summary>
	private static double Multiplicador(string? txt)
	{
		double v = PrimeiroNumero(txt);
		if (double.IsNaN(v) || txt is null) return v;
		if (txt.Contains(" mil", StringComparison.Ordinal)) return v * 1e3;
		if (txt.Contains(" M", StringComparison.Ordinal)) return v * 1e6;
		if (txt.Contains(" B", StringComparison.Ordinal)) return v * 1e9;
		return v;
	}

	/// <summary>
	/// O BP DA HUD, que vem ENCURTADO ("BP 3 M") ou escondido ("BP ???").
	///
	/// Devolve NaN quando esta escondido -- e ai NaN quer dizer "a tela recusou o numero", que e
	/// justamente o que a regra de sigilo quer afirmar.
	/// </summary>
	private static double BpDaHud(string? txt)
	{
		if (txt is null || txt.Contains("???", StringComparison.Ordinal)) return double.NaN;
		double v = PrimeiroNumero(txt);
		if (double.IsNaN(v)) return double.NaN;
		if (txt.Contains(" k", StringComparison.Ordinal)) return v * 1e3;
		if (txt.Contains(" M", StringComparison.Ordinal)) return v * 1e6;
		if (txt.Contains(" B", StringComparison.Ordinal)) return v * 1e9;
		if (txt.Contains(" T", StringComparison.Ordinal)) return v * 1e12;
		return v;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ============================ ESTE MUNDO E MEU? SE NAO FOR, NAO ENCOSTA ============================
		// Mesmo guarda do `RoboDeOlhada` e do `RoboDeMostrador`, e pelo mesmo motivo: com a porta tomada
		// o `--host` nao vira servidor nenhum e o cliente entra no mundo DA OUTRA SESSAO -- e esta
		// bancada transforma o corpo, machuca membros e mexe no estomago.
		// =================================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[bancada] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo "
					  + "e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
			return;
		}

		if (C is not { Connected: true } || H is null || M is null) return;

		_t += delta;
		if (_t < _espera) return;
		_t = 0;

		switch (_passo++)
		{
			case 0: Preparar(); break;
			case 1: OQueVaiSerCobrado(); break;

			// o barato primeiro: se a rodada morrer no meio (ha varias sessoes no ar), o que ja
			// mediu continua valendo
			case 2: _ = Capturar("base-cheio"); break;
			case 3: EsvaziarOEstomago(); break;
			case 4: _ = Capturar("nutri-baixa"); break;
			case 5: EncherOEstomago(); break;
			case 6: _ = Capturar("nutri-cheia"); break;

			case 7: BaixarOKi(); break;
			case 8: _ = Capturar("base-baixo"); break;
			case 9: ComecarACarga(); break;
			case 10: EsperarOKiPassarDe100(); break;
			case 11: LerAAbaSemForcar(); break;
			case 12: _ = Capturar("base-acima"); break;

			case 13: Transformar(); break;
			case 14: EsperarAForma(); break;
			case 15: _ = Capturar("ssj-alto"); break;
			case 16: EsperarOKiCair(); break;
			case 17: _ = Capturar("ssj-drenado"); break;

			case 18: VoltarAoNormal(); break;
			case 19: _hpDeReferencia = Capturar("base-de-volta").FiHp; break;
			case 20: Machucar(); break;
			case 21: _ = Capturar("ferido"); break;
			case 22: Curar(); break;
			case 23: TirarOFolego(); break;
			case 24: _ = Capturar("sem-folego"); break;
			case 25: DevolverOFolego(); break;

			case 26: PorOScouter(); break;
			case 27: _ = Capturar("scouter-on"); break;
			// ============================ A SEGUNDA AMOSTRA COM SCOUTER NAO E LUXO ============================
			// A primeira rodada desta bancada mostrou por que ela precisa existir: com o corpo na BASE e
			// em repouso, o multiplicador total e a % efetivo sao NUMERICAMENTE O MESMO NUMERO (os dois
			// valem 0,98, que e so o `AgeDiv`). Nesse estado a regra F5.1 -- que mede o multiplicador
			// contra `expressedBP / BP` -- nao consegue distinguir um do outro, e a injecao
			// `mult-igual-efetivo` passou por ela. Nao era um defeito do jogo: era a UNICA amostra em que
			// F5.1 podia medir estando cega justamente pra esse defeito.
			//
			// Com o Ki carregado os dois se separam (o excedente e multiplicador e nao entra na %), e a
			// mesma regra passa a enxergar. Escolhi carregar em vez de transformar porque a
			// transformacao custa 40 s de cinematica e esta rodada ja e longa.
			// ==============================================================================================
			case 28: CarregarComOScouter(); break;
			case 29: EsperarACargaComOScouter(); break;
			case 30: _ = Capturar("scouter-carregado"); break;
			// ============================ E AINDA FALTAVA UMA, COM FORMA ============================
			// A segunda rodada mostrou que carregar o Ki NAO basta pra separar o multiplicador da %
			// efetivo, e a razao e de construcao: num corpo SEM FORMA o `expressedBP / BP` e exatamente
			// o produto dos fatores de condicao -- que e a propria definicao da % desde que ela deixou
			// de saturar. Os dois numeros COINCIDEM em qualquer estado da base, carregado ou nao, e a
			// injecao `mult-igual-efetivo` continuou passando por F5.1.
			//
			// O unico fator que entra num e nao no outro e a FORMA. Entao a terceira amostra com
			// scouter e transformada -- e e ela que da a F5.1 uma medida em que a troca aparece.
			// ====================================================================================
			case 31: TransformarComOScouter(); break;
			case 32: EsperarAFormaComOScouter(); break;
			case 33: _ = Capturar("scouter-forma"); break;
			case 34: DesfazerAFormaComOScouter(); break;
			case 35: TirarOScouter(); break;
			case 36: _ = Capturar("scouter-off"); break;

			default: Fechar(); break;
		}
	}

	// =====================================================================
	// 1. PREPARO
	// =====================================================================
	private void Preparar()
	{
		// MEIO-DIA: um dia dura 24 minutos e esta bancada leva alguns. De noite a HUD inteira sai sob
		// filtro escuro -- nao muda um numero sequer, mas muda toda foto que alguem tire pra conferir.
		C?.SendVerbo("admin_meio_dia");

		M!.Abrir();
		foreach (string aba in new[] { "Stats", "Ki", "Forms" }) { M.IrPara(aba); M.ForcarRedesenho(); }
		M.IrPara("Stats");

		Percurso(M.Visible, "o menu P abre -- sem ele nao ha segunda tela pra comparar");
		Percurso(M.ValorDesenhado("Stats", "Ki") != null,
				 "e as tres abas montaram (Stats/Ki/Forms) -- a bancada le o TEXTO delas, nao a ficha");

		M.Fechar();   // ver o bloco do `Capturar`: ele so fica na tela enquanto se mede

		Nota($"  --     janela={GetViewport().GetVisibleRect().Size}  teto de carga do Ki "
		   + $"(powerupcap) = {C?.Sheet.TrilhoDeKi:0.###}");
		_espera = 1.0;
	}

	/// <summary>
	/// O QUE VAI SER COBRADO, dito ANTES de qualquer medida sair.
	///
	/// Nao e checagem: e o enunciado. Escrever a expectativa depois do resultado deixa qualquer numero
	/// ser lido como confirmacao do que quer que ele mostre.
	/// </summary>
	private static void OQueVaiSerCobrado()
	{
		Nota("");
		Nota("  --     AS SEIS FAMILIAS, e como cada uma REPROVA:");
		Nota("  --     F1 CONCORDANCIA  as duas telas escrevem o MESMO numero, em Ki baixo, cheio e");
		Nota("  --        acima de 100%. Reprova quando uma satura, quando uma arredonda diferente da");
		Nota("  --        outra, e quando a aba fica PRESA no valor antigo. Era o defeito do dono.");
		Nota("  --     F2 REPRESENTACAO  o DESENHO da barra, nao o texto. Reprova quando o");
		Nota("  --        preenchimento nao acompanha o valor, quando ele encosta no talo acima de");
		Nota("  --        100%, quando o segmento de sobrecarga nao aparece, e quando o trilho com");
		Nota("  --        folga vaza pras barras que nao podem passar de 100%.");
		Nota("  --     F3 NUTRICAO  reprova quando a barra nao esta na tela, quando o texto dela nao e");
		Nota("  --        o da ficha, e -- a que importa -- quando ela NAO SE MEXE entre cheio e vazio.");
		Nota("  --     F4 A %  reprova nos DOIS sentidos: se mexer ao transformar, e se NAO se mexer");
		Nota("  --        quando cai o Ki, a vida ou a estamina. Cada queda e cobrada NA MEDIDA.");
		Nota("  --     F5 MULTIPLICADOR  reprova quando nao bate com `expressedBP / BP` medido, quando");
		Nota("  --        nao pula ao transformar, e quando ele e a % efetivo com outro nome.");
		Nota("  --     F6 SIGILO  reprova quando o BP vira numero sem scouter, quando as duas razoes");
		Nota("  --        somem COM o corte, e quando o \"???\" fica preso mesmo com o aparelho.");
		Nota("");
	}

	// =====================================================================
	// 2. NUTRICAO
	// =====================================================================
	/// <summary>
	/// A digestao natural queima ~0,2% do tanque por MINUTO (medido). Barra parada em 100% e barra
	/// correta desenham o mesmo pixel -- e foi assim que o corte de Ki viveu quatro mil checagens
	/// verdes. Por isso o tanque e empurrado, pelos dois lados.
	/// </summary>
	private void EsvaziarOEstomago()
	{
		bool foi = Jandirus.Server.GameServer.Instance?.EstomagoDeTeste(C?.LocalId ?? 0, 15) ?? false;
		Percurso(foi, "a bancada alcancou o estomago deste corpo no servidor");
		_espera = 1.2;
	}

	private void EncherOEstomago()
	{
		Jandirus.Server.GameServer.Instance?.EstomagoDeTeste(C?.LocalId ?? 0, 50);
		_espera = 1.2;
	}

	// =====================================================================
	// 3. OS TRES ESTADOS DE KI
	// =====================================================================
	/// <summary>
	/// KI BAIXO. Esta e a UNICA das tres alturas de Ki que a bancada POSA em vez de percorrer, e vale
	/// dizer por que: gastar Ki de verdade pede tecnica, alvo e combate -- e o assunto aqui nao e como
	/// o Ki desce, e sim se as duas telas escrevem o mesmo numero quando ele esta baixo.
	///
	/// As outras duas sao caminho de jogo: acima de 100% pela tecla C (`SendCarregar`), e a QUEDA pelo
	/// dreno da forma, que e o que a familia F4 cobra.
	/// </summary>
	private void BaixarOKi()
	{
		double r = Jandirus.Server.GameServer.Instance?.KiDeTeste(C?.LocalId ?? 0, 0.40) ?? double.NaN;
		Percurso(r is > 0.35 and < 0.45, $"o Ki foi posto em {r * 100:0.0}% pra a leitura de Ki BAIXO");
		_espera = 1.0;
	}

	private void ComecarACarga()
	{
		// ============================ AQUI O MENU FICA ABERTO DE PROPOSITO ============================
		// E a unica janela da rodada em que ele precisa ficar: a leitura "sem forcar" do passo seguinte
		// so vale se a aba tiver tido a chance de se redesenhar SOZINHA enquanto o Ki sobe -- e o
		// `Redesenhar` do menu so roda com ele visivel. Sao ~15 s, contra os tres minutos de antes.
		// ==========================================================================================
		M?.Abrir();
		M?.IrPara("Stats");

		// primeiro devolve o tanque, senao a carga comeca de 40% e demora o dobro
		Jandirus.Server.GameServer.Instance?.KiDeTeste(C?.LocalId ?? 0, 1.0);
		C?.SendCarregar(true);
		_carregando = true;
		Nota("  --     segurando C (canal `SendCarregar`, o mesmo da tecla) ate o Ki passar de 110%");
		_espera = 1.0;
	}

	private double _esperandoACarga;

	private void EsperarOKiPassarDe100()
	{
		_esperandoACarga += 1.0;
		double r = C?.Sheet.RazaoDeKi ?? 0;
		if (r < 1.10 && _esperandoACarga < 60) { _passo = 10; _espera = 1.0; return; }

		Percurso(r >= 1.10, $"a tecla C levou o Ki acima de 100% ({r * 100:0.0}%) pelo canal do jogo");
		_espera = 0.4;
	}

	/// <summary>
	/// ============================ A ABA LIDA SEM SE TOCAR NELA ============================
	/// Esta e a unica leitura da bancada que NAO forca o redesenho, e ela existe pra pegar uma familia
	/// que o redesenho forcado esconderia: a aba PRESA. A pagina do menu so remonta quando a
	/// `Assinatura` muda -- e no dia em que um campo novo ficar de fora dessa assinatura, a aba vai
	/// desenhar o valor de um minuto atras e continuar concordando com a HUD em todo teste que force
	/// a remontagem antes de olhar.
	///
	/// Aqui o Ki acabou de saltar de 40% pra mais de 110% por conta propria. Se a aba nao acompanhou
	/// sozinha, ela esta congelada -- e isso e um defeito da MESMA familia do que o dono relatou.
	/// ================================================================================
	/// </summary>
	private void LerAAbaSemForcar()
	{
		Quadro q = default;
		q.Rotulo = "acima-sem-forcar";
		q.FiKi = C?.Sheet.RazaoDeKi ?? double.NaN;
		q.MenuKi = RazaoEntreParenteses(M!.ValorDesenhado("Stats", "Ki"));
		q.HudKi = Razao(H!.BarraDeKi.TextoDeTeste);
		_quadros.Add(q);

		Nota($"  --     [acima-sem-forcar] a aba Stats, sem ninguem mandar redesenhar: {q.MenuKi * 100:0.0}% "
		   + $"(HUD {q.HudKi * 100:0.0}%, ficha {q.FiKi * 100:0.0}%)");

		M!.Fechar();   // a janela em que ele precisava ficar aberto acabou aqui
		_espera = 0.3;
	}

	// =====================================================================
	// 4. A FORMA -- e a prova contraria
	// =====================================================================
	private void Transformar()
	{
		// PELO VERB DE ADMIN, que e o caminho de producao com cinematica e tudo. Pintar a forma no
		// node daria o boneco certo e o BP de antes -- e o assunto aqui e justamente o BP.
		C?.SendVerbo("admin_forma", "ssj1");
		Nota("  --     `admin_forma ssj1` mandado -- a cinematica de estreia leva ~22 s");
		_espera = 3.0;
	}

	private double _esperandoAForma;

	private void EsperarAForma()
	{
		_esperandoAForma += 3.0;
		if ((C?.Sheet.MultTotal ?? 1) < 2.5 && _esperandoAForma < 75) { _passo = 14; _espera = 3.0; return; }

		// SOLTA O C SO AGORA: a amostra `base-acima` tinha que sair com o Ki no excedente, e a
		// `ssj-alto` sai com o Ki ainda alto de proposito -- e o par que prova que a % nao viu a forma
		// enquanto o multiplicador pulou.
		C?.SendCarregar(false);
		_carregando = false;

		// E AQUI **NAO** SE DOMINA A FORMA: o SSJ1 zera o dreno aos 100% de maestria, e e o dreno que
		// o passo seguinte usa pra derrubar o Ki pelo caminho do jogo.
		_espera = 1.0;
	}

	private double _esperandoODreno;

	private void EsperarOKiCair()
	{
		_esperandoODreno += 2.0;
		double r = C?.Sheet.RazaoDeKi ?? 1;

		// ============================ O ALVO SAI DA MEDIDA, E JA ERROU DUAS VEZES ============================
		// A aba escreve "Dreno de Ki: 0,8% do Ki por segundo", e dai se tira o prazo -- so que o dreno
		// disputa com a REGENERACAO, entao a queda real e mais lenta que a conta seca. Medido nesta
		// rodada: saindo de 131,9%, noventa segundos chegaram a 92,5% -- e o portao estava em 92,0%.
		// Reprovou por meio ponto um jogo que estava certo, que e o pior tipo de falha que uma bancada
		// pode dar: ela ensina a ignorar o vermelho.
		//
		// O portao agora e 95% com 120 s de prazo. Continua sendo uma queda de quase quarenta pontos em
		// cima do Ki carregado -- de sobra pra a % efetivo ter que segui-la, que e o que F4.2 cobra.
		// ==================================================================================================
		if (r > 0.95 && _esperandoODreno < 120) { _passo = 16; _espera = 2.0; return; }

		Percurso(r <= 0.95, $"o dreno da forma derrubou o Ki sozinho ({r * 100:0.0}%) -- caminho do "
						  + "jogo, sem escrever campo nenhum");
		_espera = 0.4;
	}

	// =====================================================================
	// 5. AS OUTRAS PERNAS DA CONDICAO -- vida e estamina
	// =====================================================================
	private void VoltarAoNormal()
	{
		C?.SendVerbo("admin_forma", Jandirus.Core.Forms.Catalogo.IdBase);
		// Ki de volta ao tanque cheio: as tres amostras seguintes (referencia, ferido, sem folego)
		// precisam ter o MESMO Ki, senao a queda da % nao pode ser atribuida a perna que se mexeu.
		Jandirus.Server.GameServer.Instance?.KiDeTeste(C?.LocalId ?? 0, 1.0);
		_espera = 2.0;
	}

	private void Machucar()
	{
		_hpFerido = Jandirus.Server.GameServer.Instance?.MachucarDeTeste(C?.LocalId ?? 0, 75) ?? double.NaN;
		Percurso(_hpFerido is > 60 and < 95,
				 $"o corpo levou dano de verdade pelo `Corpo.Ferir` e parou em HP {_hpFerido:0.0} -- "
			   + "acima do piso de 0,6 do `hpratio`, que e onde a % pararia de responder");
		_espera = 1.0;
	}

	private void Curar()
	{
		Jandirus.Server.GameServer.Instance?.CurarDeTeste(C?.LocalId ?? 0);
		Jandirus.Server.GameServer.Instance?.KiDeTeste(C?.LocalId ?? 0, 1.0);
		_espera = 1.0;
	}

	private void TirarOFolego()
	{
		_razaoDeEstamina = Jandirus.Server.GameServer.Instance?.EstaminaDeTeste(C?.LocalId ?? 0, 70)
						   ?? double.NaN;
		Percurso(_razaoDeEstamina is > 0.65 and < 0.75,
				 $"a estamina da conta de poder foi a {_razaoDeEstamina:0.000} -- e ela e uma perna MORTA "
			   + "no jogo (nada abaixa `staminadeBuff` hoje), entao so a CONTA esta sendo provada");
		_espera = 1.0;
	}

	private void DevolverOFolego()
	{
		Jandirus.Server.GameServer.Instance?.EstaminaDeTeste(C?.LocalId ?? 0, 100);
		Jandirus.Server.GameServer.Instance?.KiDeTeste(C?.LocalId ?? 0, 1.0);
		_espera = 1.0;
	}

	// =====================================================================
	// 6. O SIGILO
	// =====================================================================
	/// <summary>
	/// O SCOUTER ENTRA PELO VERBO DO JOGADOR (`item_equipar`), e a janela de teste so poe o item na
	/// mochila. Acender o bit direto provaria o corte do `FichaVisivel` e nao provaria o PORTAO -- e e
	/// o portao que decide quem le poder de luta.
	/// </summary>
	private void PorOScouter()
	{
		bool naMochila = Jandirus.Server.GameServer.Instance?.ScouterNaMochilaDeTeste(C?.LocalId ?? 0) ?? false;
		Percurso(naMochila, "o scouter foi parar na mochila deste corpo");
		C?.SendVerbo("item_equipar", "Scouter");
		_espera = 1.5;
	}

	private void CarregarComOScouter()
	{
		C?.SendCarregar(true);
		_carregando = true;
		_espera = 1.0;
	}

	private double _esperandoACargaComScouter;

	private void EsperarACargaComOScouter()
	{
		_esperandoACargaComScouter += 1.0;
		double r = C?.Sheet.RazaoDeKi ?? 0;
		if (r < 1.10 && _esperandoACargaComScouter < 60) { _passo = 29; _espera = 1.0; return; }

		C?.SendCarregar(false);
		_carregando = false;
		Percurso(r >= 1.10, $"e com o aparelho no rosto o Ki subiu de novo ({r * 100:0.0}%) -- e nesse "
						  + "estado o multiplicador e a % deixam de ser o mesmo numero");
		_espera = 0.5;
	}

	private void TransformarComOScouter()
	{
		C?.SendVerbo("admin_forma", "ssj1");
		// SEM CINEMATICA DESTA VEZ: a estreia desta forma ja foi vestida la atras, e a cena cheia so
		// roda uma vez por forma. Por isso o prazo aqui e curto perto do da primeira.
		_espera = 2.0;
	}

	private double _esperandoAFormaComScouter;

	private void EsperarAFormaComOScouter()
	{
		_esperandoAFormaComScouter += 2.0;
		if ((C?.Sheet.MultTotal ?? 1) < 2.0 && _esperandoAFormaComScouter < 60)
		{
			_passo = 32;
			_espera = 2.0;
			return;
		}

		Percurso((C?.Sheet.MultTotal ?? 1) >= 2.0,
				 $"e transformou COM o aparelho no rosto ({C?.Sheet.MultTotal:0.###}x) -- e a unica "
			   + "amostra em que o multiplicador e a % efetivo sao numeros distintos e os DOIS lados "
			   + "da fracao chegam ao cliente");
		_espera = 0.5;
	}

	private void DesfazerAFormaComOScouter()
	{
		C?.SendVerbo("admin_forma", Jandirus.Core.Forms.Catalogo.IdBase);
		_espera = 1.5;
	}

	private void TirarOScouter()
	{
		C?.SendVerbo("item_equipar", "Scouter");   // o mesmo verbo alterna
		_espera = 1.5;
	}

	// =====================================================================
	// AS REGRAS
	// =====================================================================
	/// <summary>
	/// UMA AFIRMACAO SOBRE AS AMOSTRAS, e nada mais.
	///
	/// ============================ POR QUE ELAS SAO FUNCOES PURAS ============================
	/// Uma regra escrita dentro do passo que a produz so pode ser verificada rodando o jogo -- e nunca
	/// pode ser verificada CONTRA UM DEFEITO, porque o defeito precisaria estar no jogo. Sendo funcao
	/// pura sobre as amostras, a mesma regra que julga a rodada real julga tambem a rodada com o
	/// defeito injetado, sem uma linha diferente entre as duas. E o unico jeito de responder "esta
	/// regra sabe reprovar?" sem quebrar o jogo de proposito.
	///
	/// `Precisa` diz de quais amostras ela depende: faltando uma, a regra REPROVA dizendo que faltou --
	/// nunca passa por falta de dado, que e a maneira mais silenciosa de uma bancada ficar verde.
	/// ==================================================================================
	/// </summary>
	private sealed record Regra(string Codigo, string Diz, string[] Precisa,
								Func<Quadro[], (bool Ok, string Detalhe)> Julgar);

	private static (bool, string) Perto(double a, double b, double tol, string oque) =>
		(Math.Abs(a - b) <= tol, $"{oque}: {a:0.####} contra {b:0.####} (tolerancia {tol:0.####})");

	private List<Regra> Regras()
	{
		// A % efetivo dividida pelo Ki: o produto dos fatores de condicao que NAO se mexem durante a
		// rodada (idade, gravidade, peso). E a grandeza que a forma nao pode alterar.
		//
		// ============================ O DIVISOR E O `kiratio`, E ELE NAO E `min(1, Ki)` ============================
		// Estava `Math.Min(1, q.FiKi)`, e aquilo casava com uma `Inteireza` que SATURAVA em 100%. Ela
		// nao satura mais (pedido do dono: *"o bp efetivo pode SUBIR caso eu tenha um ki acima de
		// 100%"*) -- ver `Fighter.Inteireza`. Com o teto fora, dividir por 1 num quadro de Ki 140%
		// deixaria `SemOKi` 40% maior que no quadro de Ki cheio, e F4.1/F4.2 reprovariam o jogo certo.
		//
		// O divisor correto e exatamente o fator que a `Inteireza` contem, que e o `kiratio` do
		// `PowerLevel`: `max(Ki/MaxKi, 0.6)`. O piso de 0,6 e do jogo (`Fighter.Power.cs:40`) e
		// tambem faltava aqui -- sem ele um quadro drenado abaixo de 60% inflava o quociente.
		// ========================================================================================================
		static double SemOKi(Quadro q) => q.FiInteireza / Math.Max(q.FiKi, 0.6);

		return
		[
			// ================= F1 -- AS DUAS TELAS DIZEM O MESMO NUMERO =================
			new("F1.1", "HUD e aba Stats escrevem o MESMO Ki nos tres estados (baixo, cheio, acima)",
				["base-baixo", "base-cheio", "base-acima"], qs =>
				{
					foreach (string r in new[] { "base-baixo", "base-cheio", "base-acima" })
					{
						Quadro q = Q(qs, r);
						// os dois formatam com zero casas a partir do MESMO double: o que se cobra e
						// igualdade de INTEIRO ESCRITO, e nao "parecido".
						if (Math.Round(q.HudKi * 100) != Math.Round(q.MenuKi * 100))
							return (false, $"[{r}] HUD escreveu {q.HudKi * 100:0}% e a aba escreveu "
										 + $"{q.MenuKi * 100:0}%");
					}
					return (true, "os tres estados batem ao inteiro");
				}),

			new("F1.2", "e a aba Ki (terceira tela) concorda com as duas",
				["base-baixo", "base-cheio", "base-acima"], qs =>
				{
					foreach (string r in new[] { "base-baixo", "base-cheio", "base-acima" })
					{
						Quadro q = Q(qs, r);
						// A FOLGA E O ARREDONDAMENTO, e ela e calculada e nao chutada: a HUD escreve
						// zero casas (erro maximo 0,5 ponto) e a aba Ki escreve uma (0,05) -- 0,55 no
						// pior caso. Qualquer coisa acima disso e divergencia de verdade.
						if (Math.Abs(q.MenuKiDaAbaKi - q.HudKi) > 0.0056)
							return (false, $"[{r}] aba Ki {q.MenuKiDaAbaKi * 100:0.0}% x HUD {q.HudKi * 100:0.0}%");
					}
					return (true, "tres telas, um numero");
				}),

			new("F1.3", "acima de 100% NENHUMA das duas satura -- e a queixa do dono, literal",
				["base-acima"], qs =>
				{
					Quadro q = Q(qs, "base-acima");
					if (q.FiKi <= 1.001) return (false, $"a amostra nem passou de 100% (ficha {q.FiKi * 100:0.0}%)");
					if (q.HudKi <= 1.001) return (false, $"a HUD travou em {q.HudKi * 100:0.0}% com a ficha em "
														+ $"{q.FiKi * 100:0.0}%");
					if (q.MenuKi <= 1.001) return (false, $"a aba travou em {q.MenuKi * 100:0.0}%");
					return (true, $"HUD {q.HudKi * 100:0.0}% e aba {q.MenuKi * 100:0.0}% com a ficha em "
								+ $"{q.FiKi * 100:0.0}%");
				}),

			new("F1.4", "a aba nao fica PRESA: ela acompanhou o salto do Ki sem ninguem mandar redesenhar",
				["acima-sem-forcar"], qs =>
				{
					Quadro q = Q(qs, "acima-sem-forcar");
					return Math.Abs(q.MenuKi - q.FiKi) <= 0.02
						? (true, $"a aba estava em {q.MenuKi * 100:0.0}% com a ficha em {q.FiKi * 100:0.0}%")
						: (false, $"a aba ficou em {q.MenuKi * 100:0.0}% com a ficha ja em {q.FiKi * 100:0.0}% "
								+ "-- pagina congelada");
				}),

			new("F1.5", "e a % efetivo tambem concorda entre as duas telas",
				["base-cheio", "base-acima", "ssj-drenado", "ferido"], qs =>
				{
					foreach (string r in new[] { "base-cheio", "base-acima", "ssj-drenado", "ferido" })
					{
						Quadro q = Q(qs, r);
						// mesma folga calculada da F1.2: inteiro contra uma casa da 0,55 ponto no pior
						// caso. E o dono ve as duas lado a lado -- "87% efetivo" e "87,3%" sao o mesmo
						// numero em duas precisoes, e 88 contra 87,3 nao seriam.
						if (Math.Abs(q.HudEfetivo - q.MenuEfetivo) > 0.0056)
							return (false, $"[{r}] HUD {q.HudEfetivo * 100:0.0}% x aba {q.MenuEfetivo * 100:0.0}%");
					}
					return (true, "as duas telas contam a mesma condicao");
				}),

			new("F1.6", "e a nutricao tambem", ["nutri-baixa", "nutri-cheia"], qs =>
				{
					foreach (string r in new[] { "nutri-baixa", "nutri-cheia" })
					{
						Quadro q = Q(qs, r);
						if (Math.Abs(q.HudNutricao - q.MenuNutricao) > 0.011)
							return (false, $"[{r}] HUD {q.HudNutricao * 100:0.0}% x aba {q.MenuNutricao * 100:0.0}%");
					}
					return (true, "barra e aba dizem o mesmo tanque");
				}),

			// ================= F2 -- O DESENHO REPRESENTA (nao satura) =================
			new("F2.1", "acima de 100% o preenchimento tem FOLGA -- a barra nao esta no talo",
				["base-acima"], qs =>
				{
					Quadro q = Q(qs, "base-acima");
					return q.HudPreench is > 0 and < 0.999
						? (true, $"{q.HudPreench:0.000} do trilho")
						: (false, $"preenchimento {q.HudPreench:0.000} -- o desenho encostou na ponta");
				}),

			new("F2.2", "o preenchimento E o valor sobre o trilho, medido nos tres estados",
				["base-baixo", "base-cheio", "base-acima"], qs =>
				{
					foreach (string r in new[] { "base-baixo", "base-cheio", "base-acima" })
					{
						Quadro q = Q(qs, r);
						double esperado = Math.Clamp(q.FiKi, 0, q.FiTeto) / q.FiTeto;
						if (Math.Abs(q.HudPreench - esperado) > 0.01)
							return (false, $"[{r}] desenhou {q.HudPreench:0.000} e o valor pedia {esperado:0.000}");
					}
					return (true, "o desenho segue o valor, e nao um teto proprio");
				}),

			new("F2.3", "e ele CRESCE de baixo pra cheio e de cheio pra acima de 100%",
				["base-baixo", "base-cheio", "base-acima"], qs =>
				{
					double a = Q(qs, "base-baixo").HudPreench;
					double b = Q(qs, "base-cheio").HudPreench;
					double c = Q(qs, "base-acima").HudPreench;
					// E ESTA A REGRA QUE MATA O CORTE NO DESENHO: com o clamp, `cheio` e `acima`
					// desenhavam o MESMO pixel, e nenhuma leitura de texto separaria os dois.
					return a < b - 0.01 && b < c - 0.01
						? (true, $"{a:0.000} -> {b:0.000} -> {c:0.000}")
						: (false, $"{a:0.000} -> {b:0.000} -> {c:0.000}: o desenho parou de crescer");
				}),

			new("F2.4", "o segmento de sobrecarga so existe acima de 100%, e ele mede o excedente",
				["base-cheio", "base-acima"], qs =>
				{
					Quadro cheio = Q(qs, "base-cheio"), acima = Q(qs, "base-acima");
					if (cheio.HudSobrecarga > 0.001)
						return (false, $"a 100% ja havia sobrecarga desenhada ({cheio.HudSobrecarga:0.000})");
					double esperado = (Math.Min(acima.FiKi, acima.FiTeto) - 1) / acima.FiTeto;
					return Math.Abs(acima.HudSobrecarga - esperado) <= 0.01 && acima.HudSobrecarga > 0
						? (true, $"acima de 100% ele ocupa {acima.HudSobrecarga:0.000} do trilho "
							   + $"(o excedente pedia {esperado:0.000})")
						: (false, $"sobrecarga {acima.HudSobrecarga:0.000}, excedente {esperado:0.000}");
				}),

			new("F2.5", "e o traco dos 100% cai onde acaba o tanque", ["base-acima"], qs =>
				{
					Quadro q = Q(qs, "base-acima");
					return Perto(q.HudMarca, 1 / q.FiTeto, 0.005, "marca no trilho");
				}),

			new("F2.6", "o trilho com folga NAO vazou pras barras que nao passam de 100%",
				["base-acima"], qs =>
				{
					Quadro q = Q(qs, "base-acima");
					if (q.HudTrilho <= 1.001)
						return (false, $"a propria barra de Ki ficou com trilho {q.HudTrilho:0.###}");
					return q.HudVidaTrilho <= 1.001 && q.HudVigorTrilho <= 1.001 && q.HudNutTrilho <= 1.001
						? (true, $"Ki {q.HudTrilho:0.###}; vida/vigor/nutricao em 1,0")
						: (false, $"vida {q.HudVidaTrilho:0.###}, vigor {q.HudVigorTrilho:0.###}, "
								+ $"nutricao {q.HudNutTrilho:0.###} -- o teto opt-in deixou de ser opt-in");
				}),

			// ================= F3 -- A NUTRICAO =================
			new("F3.1", "a barra de nutricao esta MESMO na tela (visivel e dentro da janela)",
				["nutri-baixa"], qs =>
				{
					Quadro q = Q(qs, "nutri-baixa");
					if (q.HudNutNaTela) return (true, "visivel na arvore e dentro do retangulo da janela");
					// A DISTINCAO IMPORTA: "a barra nao esta na tela" e "nao ha tela" sao coisas
					// diferentes, e so a primeira e defeito. Sem viewport de verdade o retangulo de
					// qualquer Control nasce vazio -- ver o cabecalho da classe.
					return (false, DisplayServer.GetName() == "headless"
						? "NAO MEDIDA: rodou --headless e nao ha janela onde a barra pudesse estar. Esta "
						+ "regra e a unica que precisa de tela; rode sem --headless pra cobra-la"
						: "a barra existe mas nao esta desenhada na tela");
				}),

			new("F3.2", "e o que ela escreve e o tanque da ficha", ["nutri-baixa", "nutri-cheia"], qs =>
				{
					foreach (string r in new[] { "nutri-baixa", "nutri-cheia" })
					{
						Quadro q = Q(qs, r);
						if (Math.Abs(q.HudNutricao - q.FiNutricao) > 0.011)
							return (false, $"[{r}] escreveu {q.HudNutricao * 100:0.0}% e a ficha diz "
										 + $"{q.FiNutricao * 100:0.0}%");
					}
					return (true, "texto e ficha batem nos dois extremos");
				}),

			new("F3.3", "e ela SE MEXE: barra cheia e barra congelada desenham o mesmo pixel",
				["nutri-baixa", "nutri-cheia"], qs =>
				{
					Quadro baixa = Q(qs, "nutri-baixa"), cheia = Q(qs, "nutri-cheia");
					double d = cheia.HudNutricao - baixa.HudNutricao;
					double dp = cheia.HudNutPreench - baixa.HudNutPreench;
					return d > 0.30 && dp > 0.30
						? (true, $"de {baixa.HudNutricao * 100:0.0}% a {cheia.HudNutricao * 100:0.0}% no texto, "
							   + $"e {baixa.HudNutPreench:0.00} -> {cheia.HudNutPreench:0.00} no desenho")
						: (false, $"texto andou {d * 100:0.0} pontos e desenho andou {dp:0.000} -- barra parada");
				}),

			new("F3.4", "e o desenho dela acompanha o valor", ["nutri-baixa", "nutri-cheia"], qs =>
				{
					foreach (string r in new[] { "nutri-baixa", "nutri-cheia" })
					{
						Quadro q = Q(qs, r);
						if (Math.Abs(q.HudNutPreench - Math.Clamp(q.FiNutricao, 0, 1)) > 0.01)
							return (false, $"[{r}] desenhou {q.HudNutPreench:0.000} pra um tanque de "
										 + $"{q.FiNutricao:0.000}");
					}
					return (true, "desenho e valor andam juntos");
				}),

			// ================= F4 -- A % (nao muda ao transformar; muda com a condicao) =================
			new("F4.0", "as duas amostras de forma sao MESMO formas diferentes (senao F4.1 nao prova nada)",
				["base-acima", "ssj-alto"], qs =>
				{
					double b = Q(qs, "base-acima").FiMult, s = Q(qs, "ssj-alto").FiMult;
					return s > b * 1.8
						? (true, $"o corpo saiu de {b:0.###}x pra {s:0.###}x")
						: (false, $"{b:0.###}x -> {s:0.###}x: a forma nao entrou, e a invariancia seria vazia");
				}),

			new("F4.1", "a % NAO muda ao transformar -- a forma multiplica os DOIS lados da fracao",
				["base-acima", "ssj-alto"], qs =>
				{
					// COBRA-SE O QUOCIENTE, e nao "antes == depois": entre as duas amostras o Ki tambem
					// anda um pouco, e um teste de igualdade crua reprovaria o jogo certo. O quociente
					// vale o produto dos fatores PARADOS -- e ele reprova nos dois sentidos, inclusive
					// uma linha congelada, que passaria de bandeja num "antes == depois".
					double a = SemOKi(Q(qs, "base-acima")), b = SemOKi(Q(qs, "ssj-alto"));
					return Perto(b, a, 0.02, "inteireza/razaoKi (base -> forma)");
				}),

			new("F4.2", "mas ela CAI quando o Ki cai -- e cai na medida do Ki",
				["base-cheio", "ssj-alto", "ssj-drenado"], qs =>
				{
					Quadro alto = Q(qs, "ssj-alto"), seco = Q(qs, "ssj-drenado");
					if (seco.FiInteireza >= Q(qs, "base-cheio").FiInteireza - 0.03)
						return (false, $"a % nao desceu: {seco.FiInteireza * 100:0.0}% com o Ki em "
									 + $"{seco.FiKi * 100:0.0}% -- linha congelada");
					return Perto(SemOKi(seco), SemOKi(alto), 0.02, "inteireza/razaoKi (Ki alto -> Ki seco)");
				}),

			new("F4.3", "e cai quando a VIDA cai, tambem na medida",
				["base-de-volta", "ferido"], qs =>
				{
					Quadro r = Q(qs, "base-de-volta"), f = Q(qs, "ferido");
					if (r.FiHp <= 0) return (false, "sem HP de referencia");
					// a vida entra como `hpratio = HP/100` (com piso em 0,6): a % nova tem que ser a
					// antiga vezes a razao das duas vidas, e nada mais.
					double esperado = r.FiInteireza * (f.FiHp / r.FiHp);
					if (f.FiInteireza >= r.FiInteireza - 0.01)
						return (false, $"a % nao desceu com a ferida ({r.FiInteireza * 100:0.0}% -> "
									 + $"{f.FiInteireza * 100:0.0}%, HP {r.FiHp:0.0} -> {f.FiHp:0.0})");
					return Perto(f.FiInteireza, esperado, 0.015,
								 $"% com HP {f.FiHp:0.0} (era {r.FiInteireza * 100:0.0}% com HP {r.FiHp:0.0})");
				}),

			new("F4.4", "e cai quando a ESTAMINA cai (a perna morta do jogo -- so a conta e provada)",
				["base-de-volta", "sem-folego"], qs =>
				{
					Quadro r = Q(qs, "base-de-volta"), s = Q(qs, "sem-folego");
					if (double.IsNaN(_razaoDeEstamina)) return (false, "a janela de estamina nao respondeu");
					double esperado = r.FiInteireza * _razaoDeEstamina;
					if (s.FiInteireza >= r.FiInteireza - 0.01)
						return (false, $"a % nao desceu sem folego ({r.FiInteireza * 100:0.0}% -> "
									 + $"{s.FiInteireza * 100:0.0}%)");
					return Perto(s.FiInteireza, esperado, 0.015,
								 $"% com estamina {_razaoDeEstamina:0.00} (era {r.FiInteireza * 100:0.0}%)");
				}),

			new("F4.5", "e as duas telas escrevem a % que a ficha trouxe",
				["base-cheio", "ssj-alto", "ferido"], qs =>
				{
					foreach (string r in new[] { "base-cheio", "ssj-alto", "ferido" })
					{
						Quadro q = Q(qs, r);
						if (Math.Abs(q.HudEfetivo - q.FiInteireza) > 0.006)
							return (false, $"[{r}] a HUD escreveu {q.HudEfetivo * 100:0.0}% e a ficha diz "
										 + $"{q.FiInteireza * 100:0.0}%");
						if (Math.Abs(q.MenuEfetivo - q.FiInteireza) > 0.006)
							return (false, $"[{r}] a aba escreveu {q.MenuEfetivo * 100:0.0}% e a ficha diz "
										 + $"{q.FiInteireza * 100:0.0}%");
					}
					return (true, "o que esta na tela e o que o servidor calculou");
				}),

			// ================= F5 -- O MULTIPLICADOR TOTAL =================
			new("F5.1", "o multiplicador BATE com `expressedBP / BP` MEDIDO do pacote (so da pra medir "
					  + "com scouter -- sem ele os dois lados nao chegam)",
				["scouter-on", "scouter-carregado", "scouter-forma"], qs =>
				{
					// ============================ TRES ESTADOS, E O DA FORMA E O QUE MORDE ============================
					// Num corpo SEM FORMA o `expressedBP / BP` E o produto dos fatores de condicao, que e
					// exatamente o que a % efetivo vale -- os dois numeros coincidem por construcao, em
					// repouso e carregado. Nessas duas amostras esta regra mede que o multiplicador bate
					// com a razao, mas nao consegue distingui-lo da %: a injecao `mult-igual-efetivo`
					// passou por ela duas rodadas seguidas por causa disso.
					//
					// So a FORMA entra num e nao no outro. A terceira amostra e transformada, e e nela
					// que trocar o multiplicador pela % vira divergencia medida.
					// ==========================================================================================
					foreach (string r in new[] { "scouter-on", "scouter-carregado", "scouter-forma" })
					{
						Quadro q = Q(qs, r);
						if (double.IsNaN(q.FiBp) || double.IsNaN(q.FiExpBp) || q.FiBp <= 0)
							return (false, $"[{r}] o pacote com scouter nao trouxe os dois lados da fracao");
						double medido = q.FiExpBp / q.FiBp;
						// tolerancia RELATIVA: o BP viaja arredondado, e um erro de 0,5% em cima de um
						// numero da casa dos milhoes nao e divergencia de conta.
						if (Math.Abs(q.FiMult - medido) / Math.Max(medido, 1e-9) > 0.005)
							return (false, $"[{r}] a ficha diz {q.FiMult:0.####}x e `expressedBP/BP` da "
										 + $"{medido:0.####}x");
					}
					return (true, $"nos tres estados o multiplicador E a razao medida -- repouso "
								+ $"{Q(qs, "scouter-on").FiMult:0.####}x, carregado "
								+ $"{Q(qs, "scouter-carregado").FiMult:0.####}x, transformado "
								+ $"{Q(qs, "scouter-forma").FiMult:0.####}x (e neste ultimo a % efetivo "
								+ $"vale {Q(qs, "scouter-forma").FiInteireza:0.####}, que e outro numero)");
				}),

			new("F5.2", "e o que a aba Forms DESENHA e esse mesmo numero",
				["base-acima", "ssj-alto", "scouter-on"], qs =>
				{
					foreach (string r in new[] { "base-acima", "ssj-alto", "scouter-on" })
					{
						Quadro q = Q(qs, r);
						// o texto e encurtado por faixa (`MultTexto`), entao a folga acompanha a
						// grandeza: 0,01 abaixo de 10x, 0,1 abaixo de 100x, 1 acima disso.
						double tol = q.FiMult < 10 ? 0.01 : q.FiMult < 100 ? 0.1 : q.FiMult * 0.01;
						if (Math.Abs(q.MenuMult - q.FiMult) > tol)
							return (false, $"[{r}] a aba desenhou {q.MenuMult:0.###}x e a ficha diz "
										 + $"{q.FiMult:0.###}x");
					}
					return (true, "o desenho decodifica no numero do servidor");
				}),

			new("F5.3", "ele MUDA ao transformar -- ao contrario da % de F4.1, que fica parada",
				["base-acima", "ssj-alto"], qs =>
				{
					Quadro b = Q(qs, "base-acima"), s = Q(qs, "ssj-alto");
					if (s.MenuMult <= b.MenuMult * 1.8)
						return (false, $"a aba foi de {b.MenuMult:0.###}x pra {s.MenuMult:0.###}x -- o "
									 + "multiplicador nao viu a forma");
					// e a mesma amostra em que a % ficou parada: as duas linhas juntas contam a
					// historia inteira, e separadas nao contam nenhuma.
					double dq = Math.Abs(SemOKi(s) - SemOKi(b));
					return dq <= 0.02
						? (true, $"mult {b.MenuMult:0.###}x -> {s.MenuMult:0.###}x enquanto a % ficou "
							   + $"parada (desvio de {dq:0.0000})")
						: (false, $"o mult pulou mas a % andou junto ({dq:0.0000}) -- os dois nao podem "
								+ "responder a mesma causa");
				}),

			new("F5.4", "e ele NAO e a % efetivo com outro nome", ["ssj-alto"], qs =>
				{
					Quadro q = Q(qs, "ssj-alto");
					return Math.Abs(q.MenuMult - q.MenuEfetivo) > 0.5
						? (true, $"{q.MenuMult:0.###}x contra {q.MenuEfetivo * 100:0.0}% -- numeros de "
							   + "grandezas diferentes")
						: (false, $"mult {q.MenuMult:0.###} e % {q.MenuEfetivo:0.###} sao praticamente o "
								+ "mesmo numero");
				}),

			new("F5.5", "e a forma continua de pe embaixo da queda de Ki (o total INCLUI o Ki)",
				["base-cheio", "ssj-drenado"], qs =>
				{
					Quadro b = Q(qs, "base-cheio"), s = Q(qs, "ssj-drenado");
					// contra o corpo em REPOUSO e sem forma, e nao contra a amostra carregada: o total
					// caiu junto com o Ki, e isso esta certo -- o que se afirma aqui e que a forma
					// sobreviveu a queda.
					return s.FiMult > b.FiMult * 1.4
						? (true, $"{s.FiMult:0.###}x com o Ki a {s.FiKi * 100:0.0}%, contra {b.FiMult:0.###}x "
							   + "do mesmo corpo em repouso e sem forma")
						: (false, $"{s.FiMult:0.###}x contra {b.FiMult:0.###}x -- a forma sumiu junto com o Ki");
				}),

			// ================= F6 -- O SIGILO DO PODER DE LUTA =================
			new("F6.1", "sem scouter o SERVIDOR nao manda poder: BP e BP expresso chegam como ausencia",
				["scouter-off"], qs =>
				{
					Quadro q = Q(qs, "scouter-off");
					if (q.TemScouter) return (false, "a amostra ficou COM scouter -- nao prova o corte");
					return double.IsNaN(q.FiBp) && double.IsNaN(q.FiExpBp)
						? (true, "os dois campos chegaram NaN, como manda o `FichaVisivel`")
						: (false, $"chegou BP {q.FiBp:0} e expresso {q.FiExpBp:0} sem aparelho nenhum");
				}),

			new("F6.2", "e as duas telas escrevem \"???\"", ["scouter-off"], qs =>
				{
					Quadro q = Q(qs, "scouter-off");
					bool hud = q.HudBp.Contains("???", StringComparison.Ordinal);
					return hud && q.MenuBpEscondido
						? (true, $"HUD \"{q.HudBp}\" e a aba tambem recusou")
						: (false, $"HUD escreveu \"{q.HudBp}\" e a aba "
								+ (q.MenuBpEscondido ? "recusou" : "imprimiu numero"));
				}),

			new("F6.3", "MAS as duas razoes continuam aparecendo -- multiplicador relativo nao se esconde",
				["scouter-off"], qs =>
				{
					Quadro q = Q(qs, "scouter-off");
					if (double.IsNaN(q.FiInteireza) || double.IsNaN(q.FiMult))
						return (false, "as razoes vieram cortadas junto com o BP -- o corte passou do ponto");
					return q.HudEfetivo > 0 && q.MenuMult > 0
						? (true, $"sem aparelho a tela ainda diz {q.HudEfetivo * 100:0.0}% efetivo e "
							   + $"{q.MenuMult:0.###}x")
						: (false, "a ficha traz as razoes mas a tela nao as desenha");
				}),

			new("F6.4", "e COM scouter o BP vira numero nas duas telas (senao o \"???\" seria so texto preso)",
				["scouter-on"], qs =>
				{
					Quadro q = Q(qs, "scouter-on");
					if (!q.TemScouter) return (false, "o verbo `item_equipar` nao acendeu o bit");
					double hud = BpDaHud(q.HudBp);
					if (double.IsNaN(hud) || q.MenuBpEscondido)
						return (false, $"com scouter a HUD diz \"{q.HudBp}\" e a aba "
									 + (q.MenuBpEscondido ? "continua em ???" : "imprimiu"));
					// o da HUD vem encurtado ("3 M"): o que se cobra e que os dois falem do MESMO
					// poder, nao que tenham os mesmos digitos.
					double erro = Math.Abs(hud - q.MenuBpExpresso) / Math.Max(q.MenuBpExpresso, 1);
					return erro <= 0.01
						? (true, $"HUD \"{q.HudBp}\" e aba {q.MenuBpExpresso:N0} -- mesmo poder")
						: (false, $"HUD leu {hud:N0} e a aba leu {q.MenuBpExpresso:N0}");
				}),

			new("F6.5", "e o pacote sem scouter nao da pra reconstruir: falta o outro lado das duas fracoes",
				["scouter-off"], qs =>
				{
					Quadro q = Q(qs, "scouter-off");
					// A TENTATIVA E ESCRITA DE VERDADE, e nao afirmada: se algum dia um campo de poder
					// voltar a viajar, uma destas contas para de dar NaN e a regra reprova sozinha.
					double porMult = q.FiExpBp / q.FiMult;          // expresso / multiplicador = BP base
					double porInteireza = q.FiExpBp / q.FiInteireza; // expresso / % = o pico
					double pelaBase = q.FiBp * q.FiMult;
					return double.IsNaN(porMult) && double.IsNaN(porInteireza) && double.IsNaN(pelaBase)
						? (true, "as tres reconstrucoes possiveis dao NaN -- a razao sozinha nao vira poder")
						: (false, $"deu pra reconstruir: {porMult:0}, {porInteireza:0}, {pelaBase:0}");
				}),
		];
	}

	// =====================================================================
	// OS DEFEITOS INJETADOS
	// =====================================================================
	/// <summary>
	/// UM DEFEITO CONHECIDO, aplicado sobre as amostras REAIS.
	///
	/// ============================ POR QUE INJETAR NAS AMOSTRAS ============================
	/// Provar que uma regra reprova exige um mundo em que o defeito exista. Ha dois jeitos: quebrar o
	/// jogo, compilar, rodar, consertar -- uma vez por defeito, vinte e duas vezes, com outras sessoes
	/// compilando o mesmo repositorio ao lado; ou reproduzir o defeito no unico lugar em que ele se
	/// manifesta pra bancada, que e o que as telas ENTREGARAM.
	///
	/// O segundo caminho e o desta bancada, e ele prova exatamente uma coisa -- **que a regra tem
	/// dentes** --, que e o que estava faltando. Nao prova que o jogo tem o defeito, e a bancada nao
	/// diz que prova.
	///
	/// COM UMA EXCECAO, e ela e a do defeito que o dono relatou: o `corte-em-100` nao e aritmetica
	/// sobre o registro, e sim um `Barra` DE VERDADE, o widget de producao, alimentado com o valor ja
	/// cortado -- que e literalmente o que o `Mathf.Clamp(value, 0, 1)` no setter fazia. O texto e o
	/// preenchimento que a regra julga sao os que aquele widget desenhou.
	/// =================================================================================
	///
	/// `Alvo` sao as regras que TEM que ficar vermelhas. Uma que continue verde e uma regra sem dentes,
	/// e a bancada relata isso como falha DELA -- nao do jogo.
	/// </summary>
	private sealed record Defeito(string Nome, string Conta, string[] Alvo, Func<Quadro[], Quadro[]> Aplicar);

	/// <summary>Mexe so nas amostras nomeadas, deixando o resto intacto.</summary>
	private static Quadro[] Mexer(Quadro[] qs, Func<Quadro, Quadro> f, params string[] rotulos)
	{
		var saida = (Quadro[])qs.Clone();
		for (int i = 0; i < saida.Length; i++)
			if (rotulos.Length == 0 || Array.IndexOf(rotulos, saida[i].Rotulo) >= 0)
				saida[i] = f(saida[i]);
		return saida;
	}

	/// <summary>
	/// O CORTE EM 100% RECONSTRUIDO NUM WIDGET DE VERDADE. Ver o cabecalho de <see cref="Defeito"/>.
	///
	/// ============================ E O WIDGET COMO ELE ERA, NAO SO O VALOR CORTADO ============================
	/// A barra defeituosa nao tinha `Teto`: o trilho acabava em 1,0 e nao havia segmento de sobrecarga
	/// nenhum -- o `Teto` NASCEU do conserto. Injetar so o `Clamp` num widget que ja tem trilho de 1,4
	/// daria um desenho a 71% do trilho, com folga, que **nunca existiu na tela do dono**; e uma regra
	/// calibrada contra esse meio-defeito estaria calibrada contra nada.
	///
	/// Entao aqui a barra e construida sem teto e alimentada com o valor cortado, que sao as duas
	/// linhas do widget antigo. O texto, o preenchimento e a sobrecarga que as regras julgam sao os que
	/// esse widget desenhou -- e o desenho e literalmente o que o dono via: barra no talo, "100%"
	/// escrito, e o excedente sem existir.
	/// ====================================================================================================
	///
	/// A barra nao entra na arvore de proposito: `Teto`, `Valor` e os ancoradouros do segmento de
	/// sobrecarga sao escritos no `Reaplicar()` do proprio widget, sem depender de layout -- e uma
	/// barra a mais na tela no meio da rodada estragaria qualquer foto tirada em cima dela.
	/// </summary>
	private static Quadro CortarNaBarraDeVerdade(Quadro q)
	{
		var b = new Barra("KI", Tema.Ki);      // sem `Teto`: o trilho acaba em 1,0, como antes
		b.Valor = Math.Clamp(q.FiKi, 0, 1);    // <<< O DEFEITO, na letra em que ele existia
		Quadro saida = q with
		{
			HudKi = Razao(b.TextoDeTeste),
			HudPreench = b.PreenchimentoDeTeste,
			HudSobrecarga = b.SobrecargaDeTeste,
			HudMarca = b.MarcaDeTeste,
			HudTrilho = b.TetoDeTeste,
		};
		b.QueueFree();
		return saida;
	}

	private List<Defeito> Defeitos() =>
	[
		// ---------------------------------------------------------------- F1 e F2
		new("corte-em-100", "o bug que o dono relatou: `Mathf.Clamp(value, 0, 1)` no setter da barra "
						  + "-- corta o desenho E o numero escrito (widget REAL)",
			["F1.1", "F1.3", "F2.1", "F2.3", "F2.4"],
			qs => Mexer(qs, CortarNaBarraDeVerdade, "base-cheio", "base-acima")),

		new("menu-preso", "a aba do menu escreve sempre o mesmo numero, qualquer que seja o Ki",
			["F1.1"],
			qs => Mexer(qs, x => x with { MenuKi = 1.0 })),

		new("aba-congelada", "a assinatura da aba deixou o campo de fora e a pagina nao remontou",
			["F1.4"],
			qs => Mexer(qs, x => x with { MenuKi = 0.40 }, "acima-sem-forcar")),

		new("hud-arredonda-diferente", "a HUD passou a truncar onde a aba arredonda -- meio ponto de "
									 + "diferenca permanente entre as duas telas",
			["F1.1"],
			qs => Mexer(qs, x => x with { HudKi = x.HudKi - 0.012 })),

		new("efetivo-so-na-hud", "a aba parou de acompanhar a % e ficou com o valor de corpo inteiro",
			["F1.5"],
			qs => Mexer(qs, x => x with { MenuEfetivo = 1.0 })),

		new("barra-cega", "o numero certo e o desenho no talo: a barra ignora o valor e enche sempre",
			["F2.1", "F2.2", "F2.3"],
			qs => Mexer(qs, x => x with { HudPreench = 1.0 })),

		new("sobrecarga-invisivel", "o excedente existe no numero e nao e desenhado -- a barra de quem "
								  + "esta a 130% fica igual a de quem esta a 100%",
			["F2.4"],
			qs => Mexer(qs, x => x with { HudSobrecarga = 0 })),

		new("trilho-pra-todas", "o teto de carga vazou pras barras de vida, vigor e nutricao",
			["F2.6"],
			qs => Mexer(qs, x => x with
			{
				HudVidaTrilho = x.HudTrilho, HudVigorTrilho = x.HudTrilho, HudNutTrilho = x.HudTrilho,
			})),

		// ---------------------------------------------------------------- F3
		new("nutricao-congelada", "a barra existe, esta na tela e nunca sai de 100%",
			["F3.2", "F3.3"],
			qs => Mexer(qs, x => x with { HudNutricao = 1.0, HudNutPreench = 1.0 })),

		new("nutricao-fora-da-tela", "a barra foi montada fora do retangulo visivel -- todo numero "
								   + "continua certo e ninguem ve nada",
			["F3.1"],
			qs => Mexer(qs, x => x with { HudNutNaTela = false })),

		new("nutricao-so-no-texto", "o texto acompanha e o desenho nao", ["F3.4"],
			qs => Mexer(qs, x => x with { HudNutPreench = 1.0 })),

		// ---------------------------------------------------------------- F4
		// A REDACAO DESTA LINHA JA MENTIU UMA VEZ, e o log foi quem pegou: ela dizia "REPARE: a
		// invariancia de F4.1 continua VERDE". Isso valia quando a `Inteireza` saturava em 100%; depois
		// que ela passou a subir com o Ki (mudanca de outra sessao), uma % congelada passou a divergir
		// tambem em F4.1, e o relatorio mostrou a regra vermelha ao lado da frase dizendo que ela
		// estaria verde. O exemplo daquela licao mudou de dono -- e hoje e o `efetivo-so-o-ki`.
		new("efetivo-congelado", "a % nunca se mexe -- nem com o Ki, nem com a ferida, nem sem folego",
			["F4.2", "F4.3", "F4.4"],
			qs => Mexer(qs, x => x with { FiInteireza = 0.98, HudEfetivo = 0.98, MenuEfetivo = 0.98 })),

		// AS TRES LEITURAS MENTEM JUNTAS de proposito: um defeito que so mexesse na ficha seria pego
		// pela F4.5 (tela x ficha) e a F4.1 nunca precisaria ter dentes. Assim so a invariancia pega.
		new("efetivo-vendo-a-forma", "a % foi calculada dividindo pelo pico da BASE, entao transformar "
								   + "a empurra pra cima",
			["F4.1"],
			qs => Mexer(qs, x => x with
			{
				FiInteireza = x.FiInteireza * 1.9, HudEfetivo = x.HudEfetivo * 1.9,
				MenuEfetivo = x.MenuEfetivo * 1.9,
			}, "ssj-alto")),

		// O KI SOZINHO, com o piso do `kiratio` -- e nao `min(1, Ki)`: a % nao satura mais em 100%
		// (ver `SemOKi`), entao a conta ingenua que alguem escreveria hoje e esta.
		new("efetivo-so-o-ki", "alguem trocou o `peakexBP` por uma segunda conta que so olha o Ki -- "
							 + "vida e estamina somem da leitura. REPARE NA LISTA ABAIXO: a invariancia "
							 + "de F4.1 continua VERDE com este defeito na mao, e e por isso que ela "
							 + "sozinha nao prova nada",
			["F4.3", "F4.4"],
			qs => Mexer(qs, x => x with
			{
				FiInteireza = Math.Max(x.FiKi, 0.6), HudEfetivo = Math.Max(x.FiKi, 0.6),
				MenuEfetivo = Math.Max(x.FiKi, 0.6),
			})),

		new("efetivo-nao-desenhado", "a ficha traz a % certa e a tela desenha outra coisa",
			["F4.5"],
			qs => Mexer(qs, x => x with { HudEfetivo = x.FiInteireza * 0.8 })),

		// ---------------------------------------------------------------- F5
		new("mult-congelado", "o multiplicador nao ve a forma", ["F5.1", "F5.3"],
			qs => Mexer(qs, x => x with { FiMult = 1.11, MenuMult = 1.11 })),

		new("mult-igual-efetivo", "o multiplicador virou a % efetivo com outro nome",
			["F5.1", "F5.3", "F5.4"],
			qs => Mexer(qs, x => x with { FiMult = x.FiInteireza, MenuMult = x.FiInteireza })),

		new("mult-produto-ingenuo", "o total foi montado multiplicando os fatores um a um, em vez de "
								  + "sair de `expressedBP / BP` -- os que SOMAM na base foram contados "
								  + "como se multiplicassem",
			["F5.1"],
			qs => Mexer(qs, x => x with { FiMult = x.FiMult * 1.333, MenuMult = x.MenuMult * 1.333 })),

		// ---------------------------------------------------------------- F6
		new("bp-vazado", "a tela passou a imprimir poder de luta sem aparelho", ["F6.2"],
			qs => Mexer(qs, x => x with { HudBp = "BP 3 M", MenuBpEscondido = false }, "scouter-off")),

		new("razoes-cortadas", "o corte de sigilo passou do ponto e levou as duas razoes junto",
			["F6.3"],
			qs => Mexer(qs, x => x with
			{
				FiInteireza = double.NaN, FiMult = double.NaN, HudEfetivo = double.NaN, MenuMult = double.NaN,
			}, "scouter-off")),

		new("scouter-preso", "o \"???\" nao e regra, e texto cravado: nem com o aparelho ele sai",
			["F6.4"],
			qs => Mexer(qs, x => x with { HudBp = "BP ???", MenuBpEscondido = true }, "scouter-on")),

		new("corte-pela-metade", "so o BP base foi cortado e o expresso continuou viajando -- ai a "
							   + "razao e o expresso reconstroem o resto",
			["F6.1", "F6.5"],
			qs => Mexer(qs, x => x with { FiExpBp = 3_000_000 }, "scouter-off")),
	];

	// =====================================================================
	// O JULGAMENTO
	// =====================================================================
	private int _regrasOk, _regrasFalha, _injecoesOk, _injecoesFalha;
	private readonly List<string> _falhasDeRegra = [], _falhasDeInjecao = [];

	private List<string> Vermelhas(Quadro[] qs, List<Regra> regras, bool relatar)
	{
		var vermelhas = new List<string>();
		foreach (Regra r in regras)
		{
			string? faltou = Array.Find(r.Precisa, p => Q(qs, p).Rotulo is null);
			(bool ok, string detalhe) = faltou != null
				? (false, $"faltou a amostra `{faltou}` -- a rodada nao chegou la")
				: r.Julgar(qs);

			if (!ok) vermelhas.Add(r.Codigo);
			if (!relatar) continue;

			Nota((ok ? "  ok   " : "  FALHA") + $"  {r.Codigo}  {r.Diz}");
			Nota($"  --            {detalhe}");
			if (ok) _regrasOk++;
			else { _regrasFalha++; _falhasDeRegra.Add($"{r.Codigo}  {r.Diz}  --  {detalhe}"); }
		}
		return vermelhas;
	}

	private void Fechar()
	{
		_acabou = true;
		if (_carregando) C?.SendCarregar(false);

		// ============================ O MENU FECHA, E ISSO NAO E ARRUMACAO ============================
		// Esta bancada roda com o menu P ABERTO do comeco ao fim -- ela precisa dele pra ler a segunda
		// tela. E este corpo e o HOST, ou seja e admin: com o menu aberto, a aba de admin (com a ZONA DE
		// PERIGO, que apaga a pasta de saves inteira) fica a um clique de distancia numa janela que o
		// robo deixou por cima de tudo.
		//
		// NA PRIMEIRA RODADA DESTA BANCADA ISSO ACONTECEU: no meio da espera do dreno, o `admin.log`
		// registrou `admin_limpar` e, trinta segundos depois, `admin_limpar_ja [5ffk]` -- a limpeza
		// total, 872 contas apagadas. A bancada nao manda nenhum desses dois verbs (procure: ela nao os
		// cita), e no mesmo trecho o servidor registrou um "golpe LETAL", que e a tecla K. Ou seja
		// entrou INPUT na janela. Fechar o menu SO no fim nao teria evitado aquilo -- foi no meio --, e
		// por isso o `Capturar` passou a abrir e fechar por amostra. Esta linha e o ultimo fecho.
		// ==========================================================================================
		M?.Fechar();

		Quadro[] qs = [.. _quadros];
		List<Regra> regras = Regras();

		Nota("");
		Nota("=================== A RODADA REAL: as 32 regras contra o jogo ===================");
		Vermelhas(qs, regras, relatar: true);

		Nota("");
		Nota("=================== A PROVA DE QUE AS REGRAS REPROVAM ===================");
		Nota("  --     cada linha injeta UM defeito nas amostras e exige que as regras nomeadas fiquem");
		Nota("  --     vermelhas. Regra que continua verde com o proprio defeito na mao e regra sem");
		Nota("  --     dentes -- e isso e falha DA BANCADA, nao do jogo.");
		Nota("");

		foreach (Defeito d in Defeitos())
		{
			List<string> vermelhas = Vermelhas(d.Aplicar(qs), regras, relatar: false);
			string[] escaparam = [.. d.Alvo.Where(a => !vermelhas.Contains(a))];

			bool ok = escaparam.Length == 0 && vermelhas.Count > 0;
			Nota((ok ? "  ok   " : "  FALHA") + $"  [{d.Nome}] {d.Conta}");
			Nota($"  --            pegaram: {(vermelhas.Count > 0 ? string.Join(", ", vermelhas) : "NENHUMA")}");
			if (ok) _injecoesOk++;
			else
			{
				_injecoesFalha++;
				_falhasDeInjecao.Add(escaparam.Length > 0
					? $"[{d.Nome}] passou por {string.Join(", ", escaparam)}"
					: $"[{d.Nome}] passou por TODAS as regras");
			}
		}

		Relatorio();
	}

	private void Relatorio()
	{
		Nota("");
		Nota("=================== AS AMOSTRAS ===================");
		Nota("amostra              Ki(HUD)  Ki(aba)  trilho  sobra  efet(HUD)  efet(aba)   mult(aba)  nutri  HP");
		foreach (Quadro q in _quadros)
			Nota($"{q.Rotulo,-19}  {q.HudKi * 100,6:0.0}%  {q.MenuKi * 100,6:0.0}%  {q.HudPreench,6:0.000}  "
			   + $"{q.HudSobrecarga,5:0.000}  {q.HudEfetivo * 100,8:0.0}%  {q.MenuEfetivo * 100,8:0.0}%  "
			   + $"{q.MenuMult,9:0.###}x  {q.HudNutricao * 100,4:0}%  {q.FiHp,5:0.0}");

		int ok = _percursoOk + _regrasOk + _injecoesOk;
		int mal = _percursoFalha + _regrasFalha + _injecoesFalha;

		Nota("");
		Nota("=================== O PLACAR ===================");
		Nota($"  percurso : {_percursoOk} ok / {_percursoFalha} falhas   (a rodada chegou aonde precisava)");
		Nota($"  regras   : {_regrasOk} ok / {_regrasFalha} falhas   (o jogo contra as 6 familias)");
		Nota($"  injecoes : {_injecoesOk} ok / {_injecoesFalha} falhas   (as regras contra os defeitos)");
		Nota($"  TOTAL    : {ok} ok / {mal} falhas");

		foreach (string f in _falhasDePercurso) Nota("  FALHA (percurso)  " + f);
		foreach (string f in _falhasDeRegra) Nota("  FALHA (regra)     " + f);
		foreach (string f in _falhasDeInjecao) Nota("  FALHA (injecao)   " + f);

		Nota("");
		Nota("=================== O QUE ELA NAO COBRE ===================");
		Nota("  --     PESO e GRAVIDADE: entram na % pelo mesmo funil da vida (`deBuff` e o `gravDiv`),");
		Nota("  --     e nenhum dos dois se mexe dentro de uma rodada num planeta so. A conta deles esta");
		Nota("  --     provada na bancada de Core; o MOSTRADOR delas nao esta.");
		Nota("  --     A COR do texto e do segmento: a bancada le posicao e tamanho, nao pixel. Fundo");
		Nota("  --     claro contra texto branco ja escapou por aqui uma vez -- isso e foto, nao numero.");
		Nota("  --     A ESTAMINA e perna MORTA no jogo: F4.4 prova a conta, e nada mais que isso.");
	}
}
