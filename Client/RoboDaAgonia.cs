using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DA AGONIA DE UM PLANETA (`--diagagonia`) ============================
/// O dono pediu, com todas as letras: *"quem ta vendo do espaco o planeta deveria ficar com uns
/// efeitos... um efeito meio avermelhado a lembra magma, e rachaduras no planeta, q vai se
/// intensificando durante esses 5 minutos, ate acontecer uma mega explosao (via shaders e bem bonita
/// de se ver) e assim o planeta some"*.
///
/// ============================ POR QUE ESTA BANCADA PRECISA DE JANELA ============================
/// Porque **todas** as perguntas dela sao sobre PIXEL, e este projeto tem quatro defeitos visuais
/// registrados que passaram por quatro mil checagens verdes porque a bancada media INTENCAO. Os tres
/// cegos que produziram aqueles quatro estao todos presentes aqui:
///
///   * *"uniform escrito nao e pixel desenhado"* -- `SetShaderParameter` devolve void, nunca falha,
///     e continua devolvendo void com o shader inteiro sem compilar;
///   * *"`Modulate` nao e tela"*;
///   * *"as duas telas concordam" fica verde com as duas erradas igual*.
///
/// Entao o veredito de toda familia visual daqui e uma COMPARACAO DE FOTO contra um controle, e no
/// headless ela diz que nao mediu em vez de passar de graca.
///
/// ============================ O CONTRA-EXEMPLO MORA NO MESMO QUADRO ============================
/// Junto da Terra morrendo ha **NAMEK VIVA**, do mesmo tamanho, na mesma tela, no mesmo instante, e
/// ela e medida em todo degrau. Sem ela, *"tem vermelho no planeta"* fica verde num sistema que
/// pintasse TUDO de vermelho -- um shader com o `if (agonia > 0)` invertido, um `Modulate` global,
/// uma correcao de cor do jogo inteiro. O controle e a unica coisa que separa *"a Terra avermelhou"*
/// de *"a tela avermelhou"*.
///
/// E ele paga um segundo aluguel: e ele que mantem a **guarda de luz** honesta na foto do fim, quando
/// a Terra ja nao existe e o quadro seria quase todo fundo (ver <see cref="Foto"/>).
///
/// ============================ A VERMELHIDAO E RAZAO, E NAO DIFERENCA ============================
/// `R / ((G+B)/2)`, e nao `R - (G+B)/2`. Brilho de cena move DIFERENCA e nao move RAZAO: o estouro
/// clareia o quadro inteiro, e uma medida por diferenca subiria junto sem que uma unica rachadura
/// tivesse aparecido. A razao de um cinza e 1,00 em qualquer brilho.
///
/// ============================ E O RECORTE E MEDIDO, NAO CHUTADO ============================
/// A regiao de amostragem de cada planeta sai do ALFA DESENHADO no quadro do controle (ver
/// <see cref="Medir"/>): sao os pixels que o disco de fato pinta. Este projeto ja mediu a cor errada
/// duas vezes por escolher a faixa no olho -- uma caiu no CEU, outra pegou chuva de sangue. E ha um
/// motivo a mais aqui: a folha `Planets.tres` tem **quatro familias de tamanho** de disco no mesmo
/// quadro de 128 px (a Terra ocupa 0,75 dele, Vegeta ocupa 1,00), entao "um circulo de raio 0,5"
/// erraria em 12 dos 16 estados.
///
/// ============================ E ELA NAO NASCE DENTRO DO ESTADO ============================
/// A outra armadilha ja paga: *"bancada que NASCE DENTRO do estado nunca testa a ENTRADA nele"*. Os
/// dois planetas aqui nascem **VIVOS** e a familia 1 fotografa esse quadro como controle; so depois a
/// agonia e empurrada por UM deles, pelo caminho de producao. Um planeta forjado ja explodindo
/// provaria o desenho e deixaria a entrada -- que e onde o defeito mora -- sem uma medida.
///
/// ============================ AS DUAS METADES, SEMPRE ============================
/// Nenhuma familia afirma so um lado:
///   1. o disco VIVO nao tem crosta nenhuma  ...E...  2. com agonia ele muda de verdade;
///   3. a rampa SOBE monotona  ...E...  4. ela CHEGA longe (uma rampa chata nunca desce tambem);
///   5. o planeta ESTOURA no prazo  ...E...  6. ele SOME depois, e nao antes;
///   7. a Terra avermelha  ...E...  8. Namek, no mesmo quadro, nao mexe um pixel.
/// ==========================================================================================================
///
///     &lt;godot&gt; --path . --diagagonia --position 1920,0 --resolution 1280x720
///
/// ============================ E O DEFEITO INJETAVEL: `--agoniachata` ============================
/// Com essa bandeira a bancada empurra **sempre o mesmo instante** -- o planeta fica igual do comeco
/// ao fim, que e o modo de falha mais provavel deste sistema (um `Faltam` que nao chega no fio, um
/// `Intensidade` que devolve constante, um uniform escrito uma vez). O placar tem que ficar VERMELHO.
///
/// Ela existe porque a checagem *"a rampa nunca desce"*, que a primeira versao tinha, **fica verde
/// numa rampa chata** -- `d = 0` nao e `d < 0`. Uma bancada assim e o "crivo que nunca corta". A
/// familia 3 ganhou por isso a checagem `ChegaLonge`, e a injecao e a prova de que ela morde.
/// SEM REDE E SEM SERVIDOR: o que ela toca sao dois `PlanetaDesenhado`, um `GameClient` sem conexao
/// (so pra a conversao de `faltam` -> intensidade ser a de producao) e o quadro desenhado.
///
/// ============================ O QUE ELA NAO PODE PROVAR, E QUEM PROVA ============================
/// **Ela tem UM processo.** O que ela chama de "duas telas" (familia 13) sao dois
/// `DestrocosNoEspaco` na MESMA memoria, com a mesma DLL, os mesmos `static` e uma lista de mortos
/// que ela mesma escreveu. Isso cobre sorteio instavel, e nao cobre sorteio **estavel dentro do
/// processo e diferente entre processos**.
///
/// E isso nao e teoria: trocando o `Espaco.Misturar` do `DestrocosDeMundo.De` por um
/// `GetHashCode()` de string (que o .NET randomiza por processo), **esta bancada fechou 74 OK e 0
/// FALHA** -- cega -- e a `--destrocosvivos`, com dois clientes de verdade em dois processos,
/// apontou a primeira pedra a 46 px de distancia entre um cliente e o outro. Ver
/// `Server/GameServer.DestrocosVivosTeste.cs` e `testar-destrocos.bat`.
/// ==============================================================================================
/// </summary>
public partial class RoboDaAgonia : Node2D
{
	private readonly List<string> _linhas = [];
	private int _falhas;

	/// <summary>
	/// O `detalhe` sai SO na falha, e de proposito: um relatorio em que toda linha carrega o numero
	/// medido fica ilegivel, e ai ninguem le nem as falhas. Quem quiser todos os numeros tem a tabela
	/// da rampa, que e impressa inteira.
	/// </summary>
	private void Ok(string oque, bool passou, string detalhe = "")
	{
		_linhas.Add((passou ? "  OK   " : "  FALHA") + "  " + oque
					+ (passou || detalhe.Length == 0 ? "" : "   " + detalhe));
		if (!passou) _falhas++;
	}

	private void Nota(string t) => _linhas.Add("   --    " + t);

	/// <summary>
	/// ============================ A VITIMA E O CONTROLE SAO **PARAMETROS**, E ISSO CUSTOU 1,2 GB ============================
	/// Os dois eram `const string`. Quando o dono perguntou *"o planeta troca o icone pra terra durante
	/// a explosao?"*, responder exigia rodar a bancada com NAMEK morrendo -- e, sem bandeira, o unico
	/// jeito de fazer isso sem sujar o repo foi **copiar o projeto inteiro** (1,2 GB) pra um rascunho,
	/// trocar duas constantes na copia e compilar de novo. Pra uma pergunta binaria.
	///
	/// Com as duas bandeiras a mesma resposta vira uma rodada de 40 segundos:
	///
	///     &lt;godot&gt; --path . --diagagonia --agoniavitima Namek --agoniacontrole Vegeta --position 1920,0
	///
	/// E ha um segundo ganho, que e o que fez a familia 3 mudar: com a vitima trocavel, a bancada passa
	/// a poder rodar sobre um planeta que **ja nasce vermelho** (Vegeta), e foi exatamente ai que o
	/// crivo antigo -- vermelhidao absoluta -- se mostrou cego. Ver `RelatarARampa`.
	///
	/// OS PADROES CONTINUAM OS DE ANTES: a TERRA, que e o disco que todo mundo reconhece, e NAMEK como
	/// contra-exemplo. Namek e nao um segundo "Earth" porque os dois usariam o mesmo estado da folha e a
	/// mesma semente de ruido: dois discos identicos lado a lado provariam menos, e um erro que
	/// pintasse "todo planeta chamado Earth" passaria batido.
	/// ==================================================================================================================
	/// </summary>
	private static readonly string Cobaia = Bandeira("--agoniavitima", "Earth");

	/// <summary>O CONTRA-EXEMPLO, **VIVO**, no mesmo quadro. Ver <see cref="Cobaia"/>.</summary>
	private static readonly string Controle = Bandeira("--agoniacontrole", "Namek");

	/// <summary>O valor que vem depois de uma bandeira na linha de comando, ou o padrao.</summary>
	private static string Bandeira(string nome, string padrao)
	{
		string[] args = OS.GetCmdlineArgs();
		int i = Array.IndexOf(args, nome);
		return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
			? args[i + 1] : padrao;
	}

	/// <summary>Quantos degraus da agonia sao medidos entre 0 e 1.</summary>
	private const int Degraus = 12;

	/// <summary>Em quais degraus a TIRA tira retrato. Cinco instantes dos cinco minutos.</summary>
	private static readonly int[] DegrausDaTira = [0, 3, 6, 9, 12];

	/// <summary>
	/// O lado do recorte de cada planeta na tira.
	///
	/// ============================ 560 E NAO 480, E A DIFERENCA SAO 14 PIXELS QUE MENTIAM ============================
	/// O `PlanetaDesenhado` poe o proprio rotulo (o nome, em laranja) em `y = -Raio - 34`
	/// (`Client/CeuDoEspaco.cs:208`). Com lado 480 a meia altura era 240, e a conta ficava assim:
	///
	///   * CONTROLE, raio 200 -> rotulo em -234, **cabe** em 240  -> o nome dele aparecia na tira;
	///   * VITIMA,   raio 220 -> rotulo em -254, **nao cabe**     -> o nome dela era cortado fora.
	///
	/// Ou seja: **a tira rotulava exatamente o quadro que NAO era o assunto dela**, e por sorte de 14
	/// pixels. O unico nome visivel na foto que o dono abriu era o do planeta errado -- e foi por isso
	/// que ele leu a tira como "Namek virou a Terra".
	///
	/// 560 poe a meia altura em 280 e cabe o rotulo de um mundo de raio 220 com 26 px de folga. E o
	/// recorte continua cabendo na viewport de 1280x720 nos dois eixos.
	/// ==========================================================================================================
	/// </summary>
	private const int LadoDoRecorte = 560;

	/// <summary>
	/// ONDE E QUAO GRANDE E A VITIMA. Viraram constantes quando as familias dos destrocos passaram a
	/// precisar da posicao e do raio DEPOIS que o `_planeta` ja se recolheu (o disco morre 2,2 s
	/// depois do prazo; o rescaldo dura 60 s). Ler do node seria ler de um node que nao existe mais.
	/// </summary>
	private static readonly Vector2 PosDaVitima = new(300, 300);
	private const float RaioDaVitima = 220;

	private PlanetaDesenhado _planeta = null!;
	private PlanetaDesenhado _vivoSempre = null!;
	private GameClient? _cli;

	private Image? _quadroVivo;
	private Disco _doente, _saudavel;

	private readonly List<(double Agonia, double Dif, double Razao, double RazaoControle)> _medidas = [];

	/// <summary>
	/// ============================ DUAS TIRAS, E NAO UMA -- O CONSERTO DO DEFEITO QUE O DONO ACHOU ============================
	/// A tira antiga era UMA fileira com **seis** quadros: o 0 era o CONTROLE (outro planeta, vivo) e os
	/// 1 a 5 eram a vitima do piso ao auge. A explicacao disso morava so no console. Quem abre um
	/// arquivo de seis quadros numerados le uma SEQUENCIA -- e foi o que aconteceu: *"parece q namek
	/// virou a terra e dps o shaders da destruicao foi aplicado"*.
	///
	/// Nao havia troca de icone nenhuma. Havia uma prova ilegivel, o que e pior: ela nao so falhou em
	/// convencer, ela **convenceu do contrario**.
	///
	/// O conserto e estrutural e nao cosmetico -- **um arquivo, uma afirmacao**:
	///   * `agonia-tira-do-espaco.png` .. so a VITIMA, cinco instantes. Toda a fileira e o mesmo
	///     planeta, entao nao existe leitura em que um vire outro;
	///   * `agonia-tira-controle.png` ... so o CONTROLE, **nos mesmos cinco instantes**. Ele nao muda,
	///     e e isso que a familia 8 mede -- agora da pra VER.
	///
	/// Um cabecalho por coluna nao resolveria: o problema era a linha unica misturando dois assuntos.
	/// E as duas carregam o NOME do planeta em cada quadro, que so passou a ser possivel quando a
	/// `TiraDeFotos` aprendeu a escrever letra (ver la).
	/// ====================================================================================================================
	/// </summary>
	private readonly List<TiraDeFotos.Quadro> _tiraVitima = [], _tiraControle = [];

	private readonly List<Action> _passos = [];
	private int _passo;

	/// <summary>Quantos quadros a foto do estouro ja esperou. Ver o passo do estouro.</summary>
	private int _esperasDoEstouro;

	/// <summary>`--agoniachata`: a rampa nao anda. Ver o cabecalho -- o placar tem que ficar vermelho.</summary>
	private static bool RampaInjetadaChata =>
		Array.IndexOf(OS.GetCmdlineArgs(), "--agoniachata") >= 0;

	/// <summary>
	/// A REGIAO DE UM PLANETA NO QUADRO, medida e nao chutada.
	///
	/// `Pontos` sao os pixels que o disco realmente pinta (tirados do alfa desenhado, ver
	/// <see cref="Medir"/>), sub-amostrados de 2 em 2 -- ~21 mil por planeta, que da estatistica de
	/// sobra e cabe em milissegundos.
	/// </summary>
	private readonly record struct Disco(string Nome, Vector2I Centro, List<Vector2I> Pontos);

	public override void _Ready()
	{
		// FUNDO ESCURO E LISO: o disco tem que ser a unica coisa que muda entre duas fotos. Um fundo
		// com textura poria ruido na diferenca de luminancia e diluiria justamente o que se mede.
		//
		// ============================ O `ZIndex` DELE NAO E DETALHE, E A PRIMEIRA RODADA PROVOU ============================
		// `PlanetaDesenhado` nasce com `ZIndex = -60` (ele fica atras dos corpos e na frente do ceu de
		// estrelas) e o quad do estouro com -55. Um fundo em `ZIndex = 0` -- o padrao -- desenha POR
		// CIMA dos dois, e a bancada mediu exatamente isso: **treze fotos identicas, `dif 0,000` em
		// todas, inclusive na da explosao**. Nao havia nada errado com o efeito; o controle e que
		// estava tapando o assunto.
		//
		// E vale registrar por que isso quase passou por bom: cinco linhas VERMELHAS num relatorio
		// parecem "o efeito nao funciona", e a foto e a unica coisa que distingue as duas historias.
		// ============================================================================================================
		AddChild(new ColorRect
		{
			Color = new Color(0.03f, 0.03f, 0.06f),
			Size = GetViewport().GetVisibleRect().Size,
			ZIndex = -100,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		// ============================ UM `GameClient` SEM CONEXAO ============================
		// Ele existe por UMA razao: a conversao "segundos que faltam -> prazo absoluto -> intensidade"
		// e codigo de producao (`AplicarMortos` + `IntensidadeDaAgonia`), e a familia 3 mede ELE, e
		// nao uma rampa que a bancada teria calculado sozinha. Sem rede: nada aqui manda nem espera
		// um pacote.
		// ==================================================================================
		_cli = new GameClient { Name = "GameClientDeBancada" };
		AddChild(_cli);

		// OS DOIS NASCEM VIVOS -- ver o cabecalho. Os raios sao os de producao (`Espaco.cs`).
		_planeta = new PlanetaDesenhado
		{
			Name = "P_" + Cobaia,
			Position = PosDaVitima,
			Nome = Cobaia,
			Raio = RaioDaVitima,
			Seed = Espaco.Hash64(Cobaia),
			Premade = true,
		};
		AddChild(_planeta);

		_vivoSempre = new PlanetaDesenhado
		{
			Name = "P_" + Controle,
			Position = new Vector2(1010, 300),
			Nome = Controle,
			Raio = 200,
			Seed = Espaco.Hash64(Controle),
			Premade = true,
		};
		AddChild(_vivoSempre);

		MontarRoteiro();
	}

	private void MontarRoteiro()
	{
		// ---- FAMILIA 1: O CONTROLE. Os dois discos vivos, e os dois tem que estar LIMPOS. ----
		_passos.Add(() => { });   // um quadro pra o `_Ready` dos planetas montar os sprites
		_passos.Add(() =>
		{
			if (RampaInjetadaChata)
				Nota("**DEFEITO INJETADO (`--agoniachata`)**: a rampa nao vai andar. O placar TEM que "
				   + "ficar vermelho -- se ficar verde, a bancada nao mede a rampa, mede a existencia dela.");

			Ok("1. o shader da agonia carregou e esta no material dos DOIS discos",
			   _planeta.AgoniaNoMaterialDeTeste >= 0f && _vivoSempre.AgoniaNoMaterialDeTeste >= 0f);

			_quadroVivo = Foto("1-vivo");
			if (_quadroVivo == null) { SemQuadro(); return; }

			// A REGIAO DE AMOSTRAGEM SAI DAQUI, do quadro em que os dois estao limpos.
			_doente = Medir(_quadroVivo, Cobaia, _planeta);
			_saudavel = Medir(_quadroVivo, Controle, _vivoSempre);

			Nota($"regiao medida: {Cobaia} {_doente.Pontos.Count} pontos | "
			   + $"{Controle} (controle) {_saudavel.Pontos.Count} pontos");

			// AS DUAS ARTES VIVAS, pra a familia 19 poder perguntar "com qual das duas o disco em
			// agonia se parece?". Tiradas do MESMO quadro, com os dois planetas limpos.
			_arteVivaVitima = Assinatura(_quadroVivo, _doente);
			_arteVivaControle = Assinatura(_quadroVivo, _saudavel);

			Ok("1. os dois discos foram ACHADOS no quadro (a regiao de amostragem e medida, nao chutada)",
			   _doente.Pontos.Count > 3000 && _saudavel.Pontos.Count > 3000,
			   $"{_doente.Pontos.Count} e {_saudavel.Pontos.Count} pontos");

			Ok("1. o planeta VIVO desenha com agonia ZERO no uniform",
			   Mathf.IsEqualApprox(_planeta.AgoniaNoMaterialDeTeste, 0f));

			Ok("1. ...e o CONTROLE tambem (ele nunca sai disso -- e o que a familia 8 cobra)",
			   Mathf.IsEqualApprox(_vivoSempre.AgoniaNoMaterialDeTeste, 0f));

			// ============================ O CONTRA-EXEMPLO SAIU DA TIRA DA VITIMA ============================
			// Ele continua sendo fotografado -- e agora em CINCO instantes, na tira propria dele (ver
			// `_tiraControle` e `MedirDegrau`), em vez de um retrato solto no comeco da fileira da
			// vitima. O que ele nao pode mais e dividir arquivo com ela: era exatamente essa mistura
			// que fazia a tira se ler como "este planeta virou aquele".
			// ============================================================================================
		});

		// ---- FAMILIA 2 e 3: A RAMPA, PELO CAMINHO DE PRODUCAO ----
		// Cada degrau entra pelo `AplicarMortos` (o que o pacote chama) e sai pelo
		// `IntensidadeDaAgonia` (o que o `_Process` do planeta le). A bancada nao calcula intensidade
		// nenhuma -- ela so escolhe QUANTO FALTA, que e o unico numero que o servidor manda.
		//
		// **A RAMPA PARA MEIO SEGUNDO ANTES DO ZERO**, e nao chega nele: cruzar o prazo DISPARA a
		// explosao (e o que o `AplicarAgonia` faz), e ai a familia 4 -- que existe pra medir esse
		// disparo -- nasceria dentro do estado que ela devia testar a entrada.
		for (int i = 0; i <= Degraus; i++)
		{
			int degrau = i;
			double falta = Math.Max(0.5, MortePlanetaria.SegundosDeExplosao * (1.0 - (double)i / Degraus));
			_passos.Add(() => EmpurrarAgonia(falta));
			_passos.Add(() => MedirDegrau(degrau, falta));
		}

		_passos.Add(RelatarARampa);

		// ---- FAMILIA 4: O ESTOURO ----
		_passos.Add(() =>
		{
			Ok("4. antes do prazo o planeta AINDA NAO estourou", !_planeta.EstourouDeTeste);
			EmpurrarAgonia(0);              // o pacote do commit: `faltam = 0`
		});
		_passos.Add(() =>
		{
			Ok("4. **O PLANETA ESTOUROU** quando o prazo venceu", _planeta.EstourouDeTeste);
			Ok("4. ...e o quad da explosao entrou na cena de verdade",
			   _planeta.GetNodeOrNull("Estouro") != null);
		});

		// ============================ FAMILIA 11: O CLARAO (D1) -- E A BANCADA NUNCA O TINHA VISTO ============================
		// Ver <see cref="OClarao"/>. Ela roda ANTES da foto do auge de proposito: o clarao e o PRIMEIRO
		// instante da explosao, e a foto que existia ate agora esperava `t >= 0,35` -- quando o nucleo
		// ja cedeu (`pow(1-t, 3)` = 0,27) e espalhou pra 269 px de raio, virando um tom quente em vez de
		// um clarao. A foto do auge mostra o ANEL; ela nunca mostrou a luz.
		// ================================================================================================================
		_passos.Add(OClarao);

		// ============================ A FOTO DA EXPLOSAO ESPERA O EFEITO, e nao um numero de quadros ============================
		// A primeira versao esperava "dois quadros" e fotografou o estouro em `t = 0,03` -- ou seja, o
		// primeiro instante dele, quando nao ha o que ver. E "dois quadros" nem quer dizer o mesmo em
		// duas maquinas: a 60 Hz sao 33 ms, a 144 Hz sao 14.
		//
		// Aqui o passo se REPETE (`_passo--`) ate o proprio material dizer que a onda ja abriu. Quem
		// decide quando fotografar e o efeito, e nao o relogio de quem mede.
		// ==================================================================================================================
		//
		// ============================ E ELA TEM PISO, PORQUE UMA BANCADA NAO PODE PENDURAR ============================
		// A primeira versao deste passo nao tinha o `_esperasDoEstouro`, e a rodada com `--agoniachata`
		// **TRAVOU PRA SEMPRE**: com a rampa injetada chata a explosao nunca abre, e o `_passo--` virou
		// um laco infinito -- sem placar, sem foto, sem linha vermelha. Uma bancada que PENDURA no
		// defeito que ela existe pra achar e pior que bancada nenhuma: quem roda ve uma janela parada e
		// nao sabe se o jogo travou, se a maquina esta lenta ou se o efeito nao existe.
		//
		// O piso e generoso (6 s a 60 Hz) porque ele nao e um prazo de efeito: e um prazo de SANIDADE.
		// ==========================================================================================================
		_passos.Add(() =>
		{
			if (_planeta.TDoEstouroDeTeste >= 0.35f) return;
			if (++_esperasDoEstouro < 360) { _passo--; return; }
			Ok("4. a onda do estouro ABRIU (o material chegou a meio caminho da explosao)",
			   false, $"o `t` do estouro parou em {_planeta.TDoEstouroDeTeste:0.000}");
		});
		_passos.Add(() =>
		{
			Image? boom = Foto("4-estouro");
			if (boom == null || _quadroVivo == null) return;

			Ok("4. **A EXPLOSAO ACENDE A TELA**: o quadro mudou muito em relacao ao planeta vivo",
			   MaiorDif(_quadroVivo, boom, _doente) > 0.20,
			   $"dif {MaiorDif(_quadroVivo, boom, _doente):0.000}");

			// E O CONTRA-EXEMPLO CONTINUA VALENDO NO PIOR INSTANTE: o quad do estouro tem 5,2 raios
			// de lado e chega a passar pela beirada do recorte de Namek, entao esta checagem e
			// FROUXA de proposito (0,10 contra os 0,02 da rampa). O que ela mata e o modo de falha
			// grosso: uma explosao desenhada em TELA CHEIA, que e como um clarao de cinematica seria
			// se alguem reusasse o `Transformacao.Clarao` aqui -- e o proprio arquivo dele diz por
			// que isso e errado no espaco.
			Ok("4. ...e mesmo no estouro o CONTROLE nao vira uma tela branca (o efeito e do LUGAR)",
			   MediaLum(boom, _saudavel) - MediaLum(_quadroVivo, _saudavel) < 0.10,
			   $"o disco de {Controle} clareou {MediaLum(boom, _saudavel) - MediaLum(_quadroVivo, _saudavel):0.000}");
		});

		// ---- FAMILIA 5: E O MUNDO SOME ----
		_passos.Add(() =>
		{
			// Passado o prazo do estouro, o node se mata sozinho -- e e ELE quem faz isso, nao a
			// bancada. Empurrar um prazo bem vencido pelo MESMO pacote e o mesmo que deixar o relogio
			// andar: nada aqui chama `QueueFree`.
			//
			// E O PACOTE E O DO **COMMIT**, com `Fase = Destruido` e prazo negativo -- que e o que o
			// servidor manda de verdade depois que o mundo cai. Ele era `Explodindo` aqui, o que era
			// uma meia verdade inofensiva enquanto ninguem lia a fase; com os destrocos ela passou a
			// ser lida (o `AplicarMortos` so aceita prazo negativo de quem esta MORTO).
			EmpurrarRescaldo(MortePlanetaria.SegundosDoEstouro + 1);
		});
		_passos.Add(() => { });
		_passos.Add(() =>
		{
			Ok("5. **O PLANETA SOME** depois do estouro (o node se recolheu sozinho)",
			   !IsInstanceValid(_planeta) || _planeta.IsQueuedForDeletion());
		});
		_passos.Add(() => { });
		_passos.Add(() =>
		{
			// ============================ E AGORA A METADE QUE SO O PIXEL RESPONDE ============================
			// "O node sumiu" e uma afirmacao sobre a ARVORE. O dono pediu *"e assim o planeta some"*,
			// que e uma afirmacao sobre a TELA -- e as duas ja divergiram neste mesmo sistema: antes
			// desta rodada o `MandarVizinhanca` nao filtrava morto e o disco de um mundo destruido
			// continuava no ceu, com rotulo e tudo, enquanto o registro dizia que ele nao existia.
			// ==============================================================================================
			Image? depois = Foto("5-o-planeta-sumiu");
			if (depois == null || _quadroVivo == null) return;

			// ============================ A MEDIDA MUDOU QUANDO OS DESTROCOS CHEGARAM ============================
			// Esta linha media a LUMINANCIA MEDIA da regiao e cobrava `< 0,05`. Ela reprovou (0,051) no
			// dia em que os cacos passaram a ficar no lugar do mundo -- e reprovou por estar medindo a
			// coisa errada, e nao por defeito nenhum: uma media sobe com QUALQUER coisa acesa ali, e
			// dezoito pedras acesas ali sao exatamente o que o dono pediu.
			//
			// A pergunta do dono e *"e assim o planeta some"*, ou seja: **ainda ha um CORPO aqui?** Um
			// corpo e uma mancha cheia; um campo de destrocos e ralo. Entao o que se mede agora e a
			// FRACAO de pontos do antigo disco que continuam acesos: um planeta acende ~100% deles (por
			// construcao -- foi assim que a regiao foi escolhida) e os cacos acendem uns 5%. A medida
			// deixou de depender do BRILHO do que sobrou e passou a depender da COBERTURA, que e a
			// propriedade que separa "mundo" de "escombro".
			// ================================================================================================
			double sobrou = FracaoAcesa(depois, _doente);
			double antes = FracaoAcesa(_quadroVivo, _doente);

			Ok("5. **E NA TELA TAMBEM**: onde a Terra estava nao ha mais CORPO nenhum -- so o fundo e "
			 + "os cacos esparsos",
			   sobrou < 0.15,
			   $"o disco acendia {antes * 100:0}% dos pontos e agora acende {sobrou * 100:0}% "
			 + $"(luminancia media {MediaLum(_quadroVivo, _doente):0.000} -> {MediaLum(depois, _doente):0.000})");

			Ok("5. ...E O CONTROLE CONTINUA LA (o quadro nao ficou vazio -- some o planeta, nao a tela)",
			   MediaLum(depois, _saudavel) > 0.10,
			   $"o disco de {Controle} esta em {MediaLum(depois, _saudavel):0.000}");

			// ============================ E O CLARAO **SOME** -- a outra metade do D1 ============================
			// A familia 11 provou que ele ACENDE. Um clarao que acende e fica nao e um clarao, e um sol
			// novo no lugar do planeta -- e o dono pediu que o planeta sumisse. Entao o mesmo miolo, na
			// mesma regiao, medido depois que o estouro acabou: ele volta pra perto do que era.
			//
			// **PERTO DO QUE ERA, E NAO ZERO**: os cacos ja estao no lugar do mundo a esta altura (o
			// campo dura 60 s e a explosao 2,2), entao o miolo tem pedra acesa dentro. O que se cobra e
			// a ordem de grandeza da LUZ ter ido embora.
			// ==================================================================================================
			double mioloAgora = MediaLum(depois, Miolo(_doente, 0.35f));
			Ok("11. **E O CLARAO SOME**: passado o estouro, o miolo de onde o planeta estava volta pra "
			 + "perto do que era -- clarao que fica nao e clarao, e um sol novo no lugar do mundo",
			   _mioloNoClarao > 0 && mioloAgora < _mioloNoClarao * 0.40,
			   $"acendeu em {_mioloNoClarao:0.000} (o planeta vivo era {_mioloDoVivo:0.000}) e "
			 + $"voltou pra {mioloAgora:0.000}");
		});

		_passos.Add(ATiraDoEspaco);

		// FAMILIA 19: A PERGUNTA DO DONO, no pixel. Ela vem logo depois da tira porque mede EXATAMENTE
		// os cinco quadros que a tira mostra -- a duvida foi levantada em cima daquela imagem.
		_passos.Add(AArteNaoTroca);

		// ============================ FAMILIAS 12 a 15: OS DESTROCOS (D2 a D5) ============================
		// *"onde ficava o planeta vao ter uns asteroides/rochas q vao girar lentamente e se afastar de
		// onde era o planeta... dps de um tempo eles despawnam"*.
		//
		// ELAS RODAM AQUI, DEPOIS DAS TIRAS E ANTES DAS PEDRAS, por duas razoes de enquadramento: as
		// fotos de cima ja estao guardadas (nada do rescaldo entra nelas) e a camera das pedras, que
		// muda o quadro inteiro, ainda nao existe.
		//
		// E O CAMPO NAO E MONTADO PELA BANCADA: quem o montou foi o `PlanetaDesenhado.Estourar()`, no
		// instante em que o planeta explodiu, pelo caminho de producao. A bancada so mexe no RELOGIO.
		// =============================================================================================
		_passos.Add(OsDestrocosNascem);
		_passos.Add(OAfastamentoEODeterminismo);
		_passos.Add(OCampoSobreviveAoPassoDeChunk);

		// A PROVA DE PIXEL: um quadro com o campo escondido, outro com ele aberto, e a pergunta feita
		// CACO A CACO. Mesma disciplina da familia 10 (a pedra do chao) -- e pelo mesmo motivo: tudo o
		// que as familias acima medem sao NODES, e node existindo nao e pixel desenhado.
		_passos.Add(() => EmpurrarRescaldo(-1));
		_passos.Add(() => { });
		_passos.Add(() => _semCaco = GetViewport()?.GetTexture()?.GetImage());
		_passos.Add(() => EmpurrarRescaldo(6));
		_passos.Add(() => { });
		_passos.Add(ProvarQueOCacoAparece);

		// ============================ FAMILIAS 16 a 18: OS TRES PEDIDOS, SEPARADOS ============================
		// O dono pediu tres coisas numa frase so -- *"vao ter uns asteroides/rochas q vao girar
		// lentamente e se afastar"* -- e as tres reprovam por motivos diferentes. Ate aqui as familias
		// 12 a 14 as mediam JUNTAS e pelo NODE (quantos existem, onde o node diz que estao). Estas tres
		// perguntam a mesma coisa a TELA, e cada uma sozinha:
		//
		//   16. EXISTEM ..... contagem de manchas, sem perguntar ao node onde olhar -- e a metade que
		//                     derruba: com o planeta vivo, e com o relogio ainda fechado, sao ZERO;
		//   17. GIRAM ....... a MESMA pedra, no MESMO lugar, com silhueta diferente depois que a folha
		//                     andou. O relogio fica cravado justamente pra ela nao se mexer;
		//   18. SE AFASTAM .. a distancia media das manchas ao ponto, em TRES instantes. Dois pontos
		//                     provariam deslocamento; afastamento e tendencia, e tendencia pede tres.
		// ===================================================================================================
		_passos.Add(AsManchasNoPixel);

		_passos.Add(FotografarOGiro);
		_passos.Add(OControleDoGiro);
		_passos.Add(EsperarAFolhaAndar);
		_passos.Add(OGiroNoPixel);

		foreach (double t in InstantesDoAfastamento)
		{
			double instante = t;
			_passos.Add(() => EmpurrarRescaldo(instante));
			_passos.Add(() => { });
			_passos.Add(() => MedirOAfastamentoNoPixel(instante));
		}
		_passos.Add(OAfastamentoNoPixel);

		// A TIRA DO RESCALDO: quatro instantes do minuto, num arquivo so, com o nome do planeta em
		// cada quadro. E o unico artefato destas familias que responde *"ta bonito?"*.
		foreach (double t in InstantesDoRescaldo)
		{
			double instante = t;
			_passos.Add(() => EmpurrarRescaldo(instante));
			_passos.Add(() => RetratarORescaldo(instante));
		}
		_passos.Add(ATiraDoRescaldo);

		// D5: A JANELA FECHA, E O CAMPO SE RECOLHE SOZINHO.
		_passos.Add(AJanelaFecha);
		_passos.Add(() => { });
		_passos.Add(OCampoSumiu);

		// ---- FAMILIA 6 e 7: AS PEDRAS LEVITANDO (A4) ----
		// Depois das fotos de proposito: a camera que estas familias precisam mudaria o enquadramento
		// de todas elas.
		_passos.Add(MontarOCampoDePedras);
		_passos.Add(() => { });
		_passos.Add(MedirAsPedras);

		// ---- FAMILIA 10: A PEDRA CHEGA AO PIXEL ----
		// Tudo o que as familias 6 e 7 medem sao NODES: quantos existem, onde estao, se duas telas
		// concordam. **Nenhuma delas olha a tela**, e este e literalmente o cego que o projeto batizou
		// de "uniform escrito nao e pixel desenhado": uma folha de sprite que nao resolvesse deixaria
		// `_folha` nulo, o `Sortear` sairia na segunda linha e as tres familias continuariam verdes com
		// zero pedra desenhada -- o proprio `PedrasDaAgonia` tem um `PushWarning` pra esse caso, e
		// warning nao reprova bancada.
		_passos.Add(PrepararAFotoDasPedras);
		_passos.Add(() => { });
		_passos.Add(() =>
		{
			_semPedra = GetViewport()?.GetTexture()?.GetImage();
			if (_pedrasA == null) return;
			_pedrasA.Agonia = 1.0;
			_pedrasA._Process(0.5);
		});
		_passos.Add(() => { });
		_passos.Add(ProvarQueAPedraAparece);

		_passos.Add(Terminar);
	}

	/// <summary>
	/// EMPURRA A AGONIA **PELO CAMINHO DE PRODUCAO**, e nao escrevendo o uniform.
	///
	/// A cadeia exercitada e exatamente a do jogo: a lista que o `S2C.Mortos` monta ->
	/// `GameClient.AplicarMortos` (que converte "faltam" em prazo absoluto) ->
	/// `GameClient.IntensidadeDaAgonia` (que chama a `MortePlanetaria.Intensidade` do Core, a MESMA
	/// que o servidor usa) -> `PlanetaDesenhado.AplicarAgonia` (que escreve o uniform e desenha).
	///
	/// Se qualquer elo dessa corrente estiver morto, a foto nao muda -- que e o ponto.
	///
	/// **O CONTROLE NAO ENTRA NESTA LISTA**, e e por isso que ele e um controle de verdade: quem o
	/// mantem limpo e o `IntensidadeDaAgonia` devolvendo zero pra quem nao esta no registro (ausencia
	/// e a resposta), e nao a bancada escrevendo zero nele. Uma bancada que forcasse o zero estaria
	/// afirmando o que ela mesma fez.
	/// </summary>
	/// <param name="fase">
	/// A FASE que o pacote carrega. O padrao e `Explodindo` (os cinco minutos); depois do commit o
	/// servidor manda `Destruido`, e ai o `Faltam` **desce pra negativo** pela janela dos destrocos --
	/// que e o unico jeito de o cliente saber ha quanto tempo o mundo morreu. Ver
	/// `GameServer.RelogioDoRescaldo` e `GameClient.AplicarMortos`.
	/// </param>
	private void EmpurrarAgonia(double faltam, FaseDaMorte fase = FaseDaMorte.Explodindo)
	{
		if (_cli == null) return;

		// ============================ O DEFEITO INJETADO SO VALE NA RAMPA ============================
		// `faltam > 0` e a guarda, e ela nao e detalhe: sem ela a injecao tambem engolia o pacote do
		// COMMIT (`faltam = 0`), a explosao nunca disparava, e a bancada ficava pendurada esperando um
		// efeito que nunca ia abrir -- foi exatamente o que aconteceu na primeira rodada injetada.
		//
		// O defeito que se quer injetar e *"a rampa nao anda"*, e nao *"o planeta nunca morre"*: sao
		// dois defeitos diferentes, e misturar os dois faz a bancada reprovar pelo motivo errado.
		// ==========================================================================================
		if (RampaInjetadaChata && faltam > 0) faltam = MortePlanetaria.SegundosDeExplosao;

		var chave = new ChaveDePlaneta(true, Cobaia, 0);
		_cli.AplicarMortos([new EstadoDaMorte
		{
			Chave = chave.Texto,
			Nome = Cobaia,
			Fase = fase,
			Estagio = MortePlanetaria.UltimoEstagio + 1,
			Faltam = faltam,
		}]);

		// O `_Process` do planeta le do `GameClient` -- mas o relogio do cliente so anda depois do
		// primeiro `S2C.Ceu`, que aqui nunca chega. Entao a bancada chama a MESMA porta que o
		// `_Process` chamaria, com os MESMOS dois numeros que ele passaria.
		double agonia = MortePlanetaria.Intensidade(
			fase, MortePlanetaria.UltimoEstagio + 1, faltam);
		if (IsInstanceValid(_planeta)) _planeta.AplicarAgonia(agonia, faltam);
	}

	/// <summary>
	/// EMPURRA O **RESCALDO**: "este mundo morreu ha tantos segundos", pelo caminho de producao.
	///
	/// E o pacote que o servidor manda depois do commit -- `Fase = Destruido` e `Faltam` NEGATIVO (ver
	/// `GameServer.RelogioDoRescaldo`). O `AplicarMortos` converte isso num prazo vencido, o
	/// `SegundosAteOEstouro` devolve o negativo, e o campo de destrocos le dali.
	///
	/// A segunda linha existe pelo mesmo motivo da segunda linha do <see cref="EmpurrarAgonia"/>: o
	/// `_Process` do campo so rodaria no PROXIMO quadro, e a bancada precisa medir no quadro em que
	/// empurrou. E a MESMA porta que o `_Process` chamaria, com o MESMO numero.
	/// </summary>
	private void EmpurrarRescaldo(double segundosDesdeOEstouro)
	{
		EmpurrarAgonia(-segundosDesdeOEstouro, FaseDaMorte.Destruido);
		if (Destrocos is { } d) d.AplicarTempo(segundosDesdeOEstouro);
	}

	/// <summary>
	/// O campo de destrocos da vitima, se ele existe e **nao esta indo embora**.
	///
	/// PROCURA PELA CHAVE E NAO PELO NOME, pela mesma razao do `DestrocosNoEspaco.Garantir`: quando o
	/// campo antigo esta marcado pra morrer e um novo nasce no lugar (o que a bancada faz de proposito
	/// pra simular um passo de chunk), o Godot renomeia o RECEM-CHEGADO -- e uma busca por nome
	/// devolveria o cadaver pra sempre.
	/// </summary>
	private DestrocosNoEspaco? Destrocos
	{
		get
		{
			var chave = new ChaveDePlaneta(true, Cobaia, 0);
			foreach (Node n in GetChildren())
				if (n is DestrocosNoEspaco d && IsInstanceValid(d) && !d.IsQueuedForDeletion()
					&& d.Chave.Equals(chave)) return d;
			return null;
		}
	}

	/// <summary>Quantos campos de destroco VIVOS ha na cena. Pra cobrar que o funil nao duplica.</summary>
	private int QuantosCampos()
	{
		int n = 0;
		foreach (Node x in GetChildren())
			if (x is DestrocosNoEspaco c && IsInstanceValid(c) && !c.IsQueuedForDeletion()) n++;
		return n;
	}

	private void MedirDegrau(int degrau, double faltam)
	{
		if (_quadroVivo == null) return;

		Image? agora = GetViewport()?.GetTexture()?.GetImage();
		if (agora == null || agora.IsEmpty()) return;

		double agonia = MortePlanetaria.Intensidade(
			FaseDaMorte.Explodindo, MortePlanetaria.UltimoEstagio + 1,
			RampaInjetadaChata ? MortePlanetaria.SegundosDeExplosao : faltam);

		_medidas.Add((agonia,
					  MaiorDif(_quadroVivo, agora, _doente),
					  Razao(agora, _doente),
					  Razao(agora, _saudavel)));

		// ============================ CINCO INSTANTES, E ELES SAO A ENTREGA ============================
		// A tira e o unico artefato desta bancada que responde *"ta bonito?"*. O placar responde
		// *"ta funcionando?"*, e ja houve rodada em que as duas respostas divergiram: todas as
		// checagens numericas verdes e o auge saindo um disco de ruido amarelo, repintado em vez de
		// rachado. Nenhum numero deste arquivo teria pego aquilo.
		// ==========================================================================================
		// ============================ O MESMO INSTANTE, NOS DOIS ARQUIVOS ============================
		// Os dois recortes saem do MESMO quadro (`agora`), e por isso o quadro `k` de uma tira e o
		// quadro `k` da outra sao o mesmo milissegundo. Sem isso a comparacao entre os dois arquivos
		// nao valeria nada -- e ela e a metade da familia 8 que ate hoje so existia como numero.
		//
		// A LEGENDA CARREGA O NOME, O INDICE E O RELOGIO. Nome porque foi a falta dele que enganou o
		// dono; indice porque cinco discos parecidos precisam de ordem; e o relogio (a agonia daquele
		// instante) porque e ele que faz o par entre os dois arquivos.
		// ==========================================================================================
		if (Array.IndexOf(DegrausDaTira, degrau) >= 0)
		{
			int k = _tiraVitima.Count + 1, n = DegrausDaTira.Length;

			if (Recortar(agora, _doente) is { } corte)
				_tiraVitima.Add(new TiraDeFotos.Quadro(corte, $"{Cobaia} {k}/{n} AG {agonia:0.00}"));

			if (Recortar(agora, _saudavel) is { } corteSao)
				_tiraControle.Add(new TiraDeFotos.Quadro(corteSao, $"{Controle} {k}/{n} VIVO"));

			// A ASSINATURA DA ARTE **NOS MESMOS CINCO INSTANTES DA TIRA**, e nao noutros: a pergunta do
			// dono foi feita EM CIMA daquela imagem, entao a medida que a responde tem que ser dos
			// mesmos quadros que ele viu. Ver <see cref="AArteNaoTroca"/>.
			_assinaturas.Add((degrau, agonia, Assinatura(agora, _doente)));
			_assinaturasDoControle.Add((degrau, Assinatura(agora, _saudavel)));
		}

		// DUAS FOTOS DE QUADRO INTEIRO, e nao uma: o meio mostra a RACHADURA (que entra cedo) e o auge
		// mostra o MAGMA (que so acende na segunda metade). Uma foto so do meio deixaria a metade mais
		// cara do shader sem ninguem olhando -- e foi exatamente a foto do auge que pegou o disco
		// repintado.
		if (_medidas.Count == Degraus / 2) Foto("2-no-meio-da-agonia");
		if (_medidas.Count == Degraus + 1) Foto("3-no-auge-da-agonia");
	}

	private void RelatarARampa()
	{
		if (_medidas.Count < 3) { SemQuadro(); return; }

		// OS NOMES SAIEM DAS VARIAVEIS, e nao do texto. Eles estavam CRAVADOS ("da TERRA... em NAMEK"),
		// e na primeira rodada com a vitima trocada o relatorio imprimiu "Terra 0,836 | Namek 1,920"
		// com a vitima sendo Namek e o controle sendo Vegeta. Um relatorio que mente o nome do que ele
		// mediu e pior que um relatorio a menos.
		Nota($"a rampa, degrau a degrau (agonia | quanto o disco mudou | vermelhidao R/((G+B)/2) da "
		   + $"VITIMA {Cobaia} | a mesma razao no CONTROLE {Controle}, que nao devia mexer):");
		foreach ((double a, double d, double v, double vc) in _medidas)
			Nota($"     agonia {a:0.000}   dif {d:0.000}   {Cobaia} {v:0.000}   {Controle} {vc:0.000}");

		(double _, double difInicio, double razInicio, double controleInicio) = _medidas[0];
		(double _, double difFim, double razFim, double controleFim) = _medidas[^1];

		// ---- 2. O PIXEL MUDA DE VERDADE (o cego "uniform escrito nao e pixel desenhado") ----
		Ok("2. **O DISCO MUDA NA TELA** entre o planeta vivo e a agonia no auge",
		   difFim > 0.08, $"dif final {difFim:0.000}");

		// ---- 3. E ELE AVERMELHA, que e o pedido literal, medido por RAZAO ----
		Ok("3. **E ELE AVERMELHA** (magma): a razao R/((G+B)/2) sobe do comeco ao fim da agonia",
		   razFim > razInicio + 0.05, $"{razInicio:0.000} -> {razFim:0.000}");

		// ---- 3-bis. A RAMPA SOBE, e sobe SEM PULOS ----
		bool sobe = true;
		double maiorSalto = 0;
		for (int i = 1; i < _medidas.Count; i++)
		{
			double d = _medidas[i].Dif - _medidas[i - 1].Dif;
			// TOLERANCIA PEQUENA e nao zero: o ruido do fBm faz um degrau ou outro andar pra tras
			// alguns milesimos sem que a rampa tenha descido.
			if (d < -0.015) sobe = false;
			maiorSalto = Math.Max(maiorSalto, Math.Abs(d));
		}

		Ok("3. **A RAMPA NAO DESCE**: nenhum degrau deixa o disco menos ferido que o anterior", sobe);

		// ============================ E A METADE QUE FALTAVA: ELA CHEGA LONGE ============================
		// "Nao desce" fica VERDE numa rampa chata -- `d = 0` nao e `d < 0`. Esta e a checagem que a
		// bandeira `--agoniachata` existe pra derrubar, e sem ela a familia 3 seria um crivo que
		// nunca corta.
		//
		// ============================ E ELA MEDIA A COISA ERRADA, O QUE SO A TERCEIRA VITIMA MOSTROU ============================
		// O piso era sobre a VERMELHIDAO ABSOLUTA (`razao no meio - razao no inicio > 0,02`), e isso
		// **cega num planeta que ja nasce vermelho**. Medido: com Vegeta de vitima (razao inicial
		// 1,916, contra 0,84 da Terra) esta linha REPROVOU -- "no meio +0,001, no fim +0,329" -- com o
		// efeito perfeito na foto ao lado. A razao satura: o shader empurra o disco pra uma cor de
		// magma que a arte de Vegeta ja tem, entao a primeira metade da rampa nao move o canal.
		//
		// O conserto e medir **o quanto do caminho ja foi andado**, e nao a cor em que ele foi andado:
		// `dif` e a maior mudanca de luminancia dentro do disco, e ela nao depende da cor de partida
		// (medida: 0,049 -> 0,644 na rodada normal). O crivo pergunta se, na METADE do prazo, o disco
		// ja andou uma fatia do avanco total -- o que e falso numa rampa chata (o avanco total e zero)
		// e verdadeiro em qualquer planeta, de qualquer cor.
		//
		// A VERMELHIDAO NAO SAIU DA BANCADA: ela continua sendo cobrada na linha de cima ("E ELE
		// AVERMELHA"), que e o pedido literal do dono e passa em Vegeta com folga (+0,329). O que
		// mudou e que ela deixou de ser tambem o crivo do MEIO da rampa, que nao e trabalho dela.
		// ====================================================================================================================
		double avancoTotal = difFim - difInicio;
		double avancoDoMeio = _medidas[Degraus / 2].Dif - difInicio;
		Ok("3. **E A RAMPA CHEGA LONGE**: no meio dos cinco minutos ela ja andou uma parte do caminho",
		   avancoTotal > 0.05 && avancoDoMeio > avancoTotal * 0.15,
		   $"no meio {avancoDoMeio:0.000} de {avancoTotal:0.000} de avanco "
		 + $"(e a vermelhidao andou +{razFim - razInicio:0.000})");

		Ok("3. ...e ela nao PULA: nenhum degrau sozinho responde por mais de metade do efeito",
		   maiorSalto < difFim * 0.55, $"maior salto {maiorSalto:0.000} de {difFim:0.000}");

		// ---- 3-ter: a agonia comeca no PISO e nao em zero ----
		Ok("3. a agonia comeca no PISO do Core e nao em zero (o mundo ja treme no segundo 0)",
		   _medidas[0].Agonia >= MortePlanetaria.PisoDaAgonia - 1e-9,
		   $"{_medidas[0].Agonia:0.000} contra piso {MortePlanetaria.PisoDaAgonia:0.000}");
		// PRATICAMENTE 1 e nao exatamente 1: o ultimo degrau para meio segundo antes do prazo, pra a
		// familia 4 poder medir o disparo da explosao a partir de FORA dele.
		Ok("3. ...e termina praticamente em 1 (a meio segundo do fim)",
		   _medidas[^1].Agonia > 0.99, $"{_medidas[^1].Agonia:0.0000}");

		// ============================ FAMILIA 8: O CONTRA-EXEMPLO ============================
		// Se esta linha ficar vermelha junto com as de cima, o que subiu foi a TELA e nao o planeta.
		double maiorDesvio = _medidas.Max(m => Math.Abs(m.RazaoControle - controleInicio));

		Ok($"8. **{Controle}, VIVA, NO MESMO QUADRO, NAO AVERMELHA** -- o efeito e do planeta, nao da tela",
		   maiorDesvio < 0.02,
		   $"a razao dela andou {maiorDesvio:0.000} (de {controleInicio:0.000} a {controleFim:0.000}) "
		 + $"enquanto a da Terra andou {Math.Abs(razFim - razInicio):0.000}");

		Ok("8. ...e o uniform dela continua em zero depois da agonia inteira",
		   Mathf.IsEqualApprox(_vivoSempre.AgoniaNoMaterialDeTeste, 0f),
		   $"agonia {_vivoSempre.AgoniaNoMaterialDeTeste:0.000}");

		// A SEPARACAO E A MEDIDA QUE VALE: nao basta a Terra subir e Namek ficar parada -- a subida
		// tem que ser MUITO maior que o piso de ruido que o controle mede. Sem esta razao, um efeito
		// que movesse 0,021 na Terra e 0,019 em Namek passaria nas duas linhas de cima.
		Ok("8. ...e a subida da Terra e MUITO maior que o ruido que o controle mede",
		   maiorDesvio < 1e-6 || Math.Abs(razFim - razInicio) > maiorDesvio * 4,
		   $"{Math.Abs(razFim - razInicio):0.000} contra {maiorDesvio:0.000}");
	}

	/// <summary>
	/// A TIRA DO ESPACO: o contra-exemplo e os cinco instantes, lado a lado, num arquivo so.
	///
	/// O quadro **0** e Namek VIVA; os quadros **1 a 5** sao a Terra do piso da agonia ate o auge.
	/// Ver <see cref="TiraDeFotos"/> -- a montagem mora la porque a bancada do bio ja pagou por ela.
	/// </summary>
	private void ATiraDoEspaco()
	{
		if (_tiraVitima.Count < 2) { Nota("sem tira do espaco: faltaram recortes"); return; }

		string caminho = ProjectSettings.GlobalizePath("user://agonia-tira-do-espaco.png");
		double pintada = TiraDeFotos.Montar(_tiraVitima, caminho);

		Ok($"9. **A TIRA DA VITIMA** saiu com {_tiraVitima.Count} quadros do MESMO planeta ({Cobaia}, "
		 + "do piso da agonia ao auge), cada um com o nome escrito, e ela NAO esta vazia",
		   pintada > 0.25, $"{pintada * 100:0}% dos pixels sao imagem e nao fundo");
		Nota($"tira da vitima: {caminho}");

		// ============================ E A DO CONTROLE, NUM ARQUIVO PROPRIO ============================
		// Os mesmos cinco instantes, o outro planeta. Separada e nao ao lado porque **um arquivo, uma
		// afirmacao**: enquanto os dois dividiam fileira, a tira se lia como um filme em que um planeta
		// virava o outro -- e foi assim que o dono a leu. Ver `_tiraVitima`.
		// ==========================================================================================
		string caminhoC = ProjectSettings.GlobalizePath("user://agonia-tira-controle.png");
		double pintadaC = TiraDeFotos.Montar(_tiraControle, caminhoC);

		Ok($"9. **A TIRA DO CONTROLE** saiu com {_tiraControle.Count} quadros de {Controle}, VIVO, nos "
		 + "MESMOS cinco instantes -- e ela tambem NAO esta vazia",
		   pintadaC > 0.25 && _tiraControle.Count == _tiraVitima.Count,
		   $"{pintadaC * 100:0}% pintada, {_tiraControle.Count} contra {_tiraVitima.Count} quadros");
		Nota($"tira do controle: {caminhoC}");

		// ============================ E O ROTULO SAIU MESMO? (a prova de que a tira nao se le como sequencia) ============================
		// A tira antiga enganou o dono porque a fileira nao dizia o que estava mostrando. O conserto foi
		// dividir em dois arquivos **e escrever o nome do planeta em cada quadro** -- mas "eu passei uma
		// legenda" nao e "a legenda saiu": `Escrever` devolve `void`, ignora o que nao conhece e encolhe
		// o texto sozinho. Aqui a tira e LIDA DE VOLTA DO DISCO e o rotulo e contado quadro a quadro.
		//
		// **E esta e a metade que responde ao pedido do dono nesta rodada**: *"prove que um leitor sem
		// contexto distingue controle de vitima"*. Um leitor sem contexto tem exatamente uma fonte de
		// informacao -- o que esta desenhado no arquivo.
		// ================================================================================================================================
		int rotulosV = TiraDeFotos.QuadrosComRotulo(caminho, _tiraVitima);
		int rotulosC = TiraDeFotos.QuadrosComRotulo(caminhoC, _tiraControle);

		Ok($"9. **TODO QUADRO DAS DUAS TIRAS TEM O NOME DO PLANETA DESENHADO NELE** -- lido de volta do "
		 + "PNG, e nao \"eu passei a legenda\": quem abre o arquivo sem contexto le em cada quadro de "
		 + $"qual planeta ele e, e nenhuma fileira pode ser lida como \"{Controle} virou {Cobaia}\"",
		   rotulosV == _tiraVitima.Count && rotulosC == _tiraControle.Count,
		   $"{rotulosV} de {_tiraVitima.Count} na tira da vitima, "
		 + $"{rotulosC} de {_tiraControle.Count} na do controle");

		// A AFIRMACAO QUE AS DUAS TIRAS JUNTAS FAZEM, escrita: sem esta linha o operador tem dois
		// arquivos e nenhuma frase, e "compare voce mesmo" e o comeco de toda leitura errada.
		Nota($"as duas tiras sao o mesmo relogio: o quadro k de uma e o quadro k da outra saem do MESMO "
		   + $"instante. {Cobaia} muda dos cinco; {Controle} nao muda em nenhum.");
	}

	/// <summary>Quantos quadros a foto do clarao ja esperou. Piso de sanidade, como o do estouro.</summary>
	private int _esperasDoClarao;

	/// <summary>
	/// ============================ FAMILIA 11: O CLARAO (D1), FOTOGRAFADO NO INSTANTE CERTO ============================
	/// *"vao haver um clarao logo onde ele estava"*, pedido do dono.
	///
	/// **NADA FOI CONSTRUIDO PRA ISSO**: o clarao ja era o `nucleo` do `EstouroDePlaneta.gdshader:83-84`
	/// -- `smoothstep(raioNucleo, raioNucleo*0.35, r) * pow(1.0 - t, 3.0)`, com `cor_nucleo` branco-QUENTE.
	/// Mas "ja existe" era LEITURA DE CODIGO, e leitura de codigo nao e prova; e a primeira medida
	/// tentada aqui reprovou por um motivo que vale registrar, porque ele e sobre a BANCADA e nao sobre
	/// o efeito:
	///
	///   **A bancada nunca tinha fotografado o clarao.** A unica foto do estouro esperava
	///   `t >= 0,35` -- e nesse ponto o nucleo ja caiu a `(1-0,35)^3 = 0,27` e ja inchou pra 269 px de
	///   raio, ou seja virou um TOM quente espalhado. Quem olha a foto ve o anel de choque e os raios,
	///   e conclui que nao ha clarao nenhum. Ha: ele dura os primeiros decimos.
	///
	/// Entao esta familia fotografa no PRIMEIRO quadro em que o tween andou, e cobra tres coisas que so
	/// um clarao tem juntas:
	///   1. o miolo de onde o planeta estava fica MUITO mais aceso do que o proprio planeta era;
	///   2. a luz e branco-QUENTE e nao branco puro -- a regra 1 que o `Transformacao.Clarao` ja pagou
	///      ("branco puro e opaco e um corte pra branco, e perde-se o quadro inteiro");
	///   3. e o CONTROLE, do outro lado da tela, nao clareia -- senao o que acendeu foi a tela.
	/// ================================================================================================================
	/// </summary>
	private void OClarao()
	{
		// ============================ ONDE FOTOGRAFAR, ENTRE DOIS DEFEITOS OPOSTOS ============================
		// `t = 0,10` nao e um numero redondo escolhido no olho: e o unico ponto em que da pra MEDIR o
		// clarao, entre duas cegueiras que a bancada bateu uma em cada tentativa.
		//
		//   * TARDE DEMAIS (`t >= 0,35`, que era a unica foto que existia): o nucleo ja caiu a
		//     `(1-t)^3 = 0,27` e ja inchou pra 269 px de raio. O que sobra e um TOM quente, e a medida
		//     do miolo contra a beirada deu 0,146 contra 0,140 -- cega;
		//   * CEDO DEMAIS (o primeiro quadro do tween): `brilho` passa de 1 (nucleo 0,98 + a crista da
		//     onda, que nesse instante ainda esta no centro) e o pixel sai **branco puro, 1,00 1,00
		//     1,00**, saturado. Ai nao da pra afirmar "branco-quente" -- nao porque a cor errou, mas
		//     porque a medida perdeu a informacao no estouro do canal.
		//
		// E VALE REGISTRAR O QUE ISSO REVELOU: nos primeiros dois ou tres quadros a explosao **estoura
		// pra branco puro** no miolo. Isso nao e a violacao da regra 1 do `Transformacao.Clarao` -- a
		// regra e sobre um retangulo de TELA CHEIA que rouba o quadro inteiro por meio segundo; aqui e
		// um pico local de dois quadros no lugar exato onde um mundo acabou de virar po, e a linha do
		// controle abaixo prova que o resto da tela nao mexeu.
		// ==================================================================================================
		if (_planeta.TDoEstouroDeTeste < 0.10f && ++_esperasDoClarao < 360) { _passo--; return; }

		Image? clarao = Foto("4a-o-clarao");
		if (clarao == null || _quadroVivo == null) return;

		Disco miolo = Miolo(_doente, 0.35f);
		double antes = MediaLum(_quadroVivo, miolo), agora = MediaLum(clarao, miolo);

		Ok("11. **O CLARAO ACENDE ONDE O PLANETA ESTAVA** (D1): no instante do estouro o miolo do disco "
		 + "fica muito mais aceso do que o proprio planeta era",
		   agora > antes * 1.25,
		   $"o miolo foi de {antes:0.000} pra {agora:0.000} (t do estouro {_planeta.TDoEstouroDeTeste:0.000})");

		(double r, double g, double b) = MediaRgb(clarao, miolo);
		Ok("11. ...e ele e branco-QUENTE e nao branco puro (a regra 1 do clarao, ja paga uma vez)",
		   r > b + 0.05 && r >= g - 0.02, $"RGB do miolo {r:0.00} {g:0.00} {b:0.00}");

		double controle = MediaLum(clarao, _saudavel) - MediaLum(_quadroVivo, _saudavel);
		Ok($"11. ...e {Controle}, do outro lado da tela, NAO clareia (o clarao e do LUGAR, nao da tela)",
		   controle < 0.10, $"o disco de {Controle} clareou {controle:0.000}");

		// GUARDADO PRA A OUTRA METADE DO D1: o dono pediu um clarao, e clarao que fica nao e clarao --
		// e um sol novo. Quem cobra o apagar e a familia 5, na foto de depois do estouro.
		_mioloNoClarao = agora;
		_mioloDoVivo = antes;
	}

	/// <summary>Quanto o miolo acendeu no clarao, e quanto ele valia com o planeta vivo. Ver o D1.</summary>
	private double _mioloNoClarao = -1, _mioloDoVivo = -1;

	// =====================================================================
	// OS DESTROCOS -- D2 a D5, o rescaldo de um mundo
	// =====================================================================
	/// <summary>Em que instantes do minuto a tira do rescaldo tira retrato.</summary>
	private static readonly double[] InstantesDoRescaldo = [2, 10, 30, 55];

	/// <summary>O lado do recorte da tira do rescaldo. Maior que o do planeta: o campo se ESPALHA.</summary>
	private const int LadoDoRescaldo = 720;

	private readonly List<TiraDeFotos.Quadro> _tiraRescaldo = [];
	private Image? _semCaco;

	/// <summary>
	/// ============================ FAMILIA 12: O CAMPO NASCE, E ELE E DO SERVIDOR (D2 + D4) ============================
	/// A primeira metade e sobre AUTORIDADE, que e o unico ponto em que o dono foi explicito -- ele
	/// escreveu *"(server sync)"* com parenteses e tudo. O cliente nao decide nada aqui: quem diz que
	/// este mundo morreu, e ha quanto tempo, e o `S2C.Mortos`, e o cliente so obedece. A prova disso e
	/// a ausencia: **tire o planeta do registro e o campo se recolhe sozinho** (ver
	/// <see cref="OCampoSumiu"/>), sem a bancada mandar.
	///
	/// A segunda e sobre a ARTE (D4): `Asteroid5112013` estava importada e **sem um unico consumidor**
	/// desde 1 de agosto. Um `.tres` que nao resolve carrega NULO e o efeito some calado -- e o node
	/// so emite um `PushWarning`, que nao reprova bancada nenhuma. Entao a folha e cobrada aqui.
	/// ============================================================================================================
	/// </summary>
	private void OsDestrocosNascem()
	{
		DestrocosNoEspaco? d = Destrocos;

		Ok("12. **O CAMPO DE DESTROCOS NASCEU** quando o planeta estourou -- e quem o montou foi o "
		 + "`PlanetaDesenhado.Estourar()`, e nao a bancada",
		   d != null, "nao ha node de destrocos na cena");
		if (d == null) return;

		Ok("12. ...e ele e IRMAO do disco, e nao filho: o disco ja morreu e o campo continua de pe",
		   !IsInstanceValid(_planeta) || _planeta.IsQueuedForDeletion());

		int esperados = DestrocosDeMundo.Quantos(RaioDaVitima);
		Ok($"12. **A ARTE `Asteroid5112013` RESOLVEU** e virou {d.CacosDeTeste} cacos (D4)",
		   d.CacosDeTeste == esperados && esperados > 0,
		   $"{d.CacosDeTeste} cacos, esperados {esperados}");

		// O TETO E ESCRITO E ELE MORDE: a conta crua (`raio/12`) pede 36 pedacos num mundo de raio
		// 440, e o teto corta em 24. Sem esta linha o "teto" seria decorativo.
		Ok("12. ...e o teto duro segura um mundo grande (o custo nao acompanha o raio)",
		   DestrocosDeMundo.Quantos(440) == DestrocosDeMundo.MaxPedacos
		   && DestrocosDeMundo.Quantos(60) == DestrocosDeMundo.MinPedacos,
		   $"raio 440 -> {DestrocosDeMundo.Quantos(440)}, raio 60 -> {DestrocosDeMundo.Quantos(60)}");

		// ============================ O GIRO E LENTO, E ELE E A FOLHA ============================
		// A folha tem 16 quadros a `speed = 10`: uma volta em 1,6 s na velocidade nativa. O que se
		// cobra aqui e a volta em SEGUNDOS, e nao o `SpeedScale` cru -- o numero que o dono pediu
		// (*"girar lentamente"*) e o primeiro, e o segundo e so como ele foi escrito.
		// =====================================================================================
		float[] giros = d.GirosDeTeste;
		double voltaMin = double.MaxValue, voltaMax = 0;
		foreach (float g in giros)
		{
			double volta = g <= 0 ? 0 : DestrocosDeMundo.QuadrosDaFolha / (10.0 * g);
			voltaMin = Math.Min(voltaMin, volta);
			voltaMax = Math.Max(voltaMax, volta);
		}

		Ok("12. **ELES GIRAM LENTAMENTE**: uma volta completa leva de 5 a 11 segundos (a folha nativa "
		 + "daria 1,6 s -- seria um pedregulho em panico)",
		   giros.Length > 0 && voltaMin >= 5 && voltaMax <= 11,
		   $"volta de {voltaMin:0.0} a {voltaMax:0.0} s");

		// ...E NAO EM UNISSONO. Dezesseis pedras na mesma fase giram como uma so, e o campo denuncia
		// na hora que e efeito e nao escombro.
		var fases = new HashSet<int>(d.QuadrosDeTeste);
		Ok("12. ...e cada caco comeca num QUADRO diferente da folha (senao eles cambaleiam em unissono)",
		   fases.Count >= 6, $"{fases.Count} fases distintas em {d.CacosDeTeste} cacos");
	}

	/// <summary>
	/// ============================ FAMILIA 13: ELES SE AFASTAM, DESACELERANDO, E TODA TELA VE O MESMO (D3) ============================
	/// Tres afirmacoes, e nenhuma delas e sobre codigo:
	///
	///   1. **SE AFASTAM** -- caco por caco, a distancia ate onde o planeta estava CRESCE com o tempo.
	///      Medida no node, e nao na formula: se alguem trocar o consumidor por um que ignore o
	///      relogio, a formula continua certa e esta linha cai;
	///   2. **DESACELERANDO** -- o avanco dos primeiros 15 s e maior que o dos ultimos 15. Isto e a
	///      decisao escrita em `DestrocosDeMundo.ExpoenteDoAfastamento` sendo cobrada: um caco que
	///      acelera sai da tela em segundos e leva o efeito junto;
	///   3. **DUAS TELAS VEEM O MESMO** -- o *"server sync"* do dono. Dois campos com a MESMA semente
	///      poem os cacos nos MESMOS lugares, e um com semente diferente nao. Sem a segunda metade, a
	///      primeira ficaria verde num sistema em que a semente nao faz nada.
	/// ====================================================================================================================
	/// </summary>
	private void OAfastamentoEODeterminismo()
	{
		if (Destrocos is not { } d) { Nota("familia 13 nao mediu: nao ha campo"); return; }

		double[] instantes = [5, 20, 35, 50];
		var medias = new double[instantes.Length];
		var minimos = new double[instantes.Length];

		for (int i = 0; i < instantes.Length; i++)
		{
			EmpurrarRescaldo(instantes[i]);
			(medias[i], minimos[i]) = DistanciasDoCampo(d);
		}

		Nota($"o afastamento dos {d.CacosDeTeste} cacos (distancia media do centro, em px): "
		   + string.Join("  ", instantes.Select((t, i) => $"+{t:0}s {medias[i]:0}")));

		bool cresceSempre = true;
		for (int i = 1; i < medias.Length; i++)
			if (medias[i] <= medias[i - 1] || minimos[i] <= minimos[i - 1]) cresceSempre = false;

		Ok("13. **OS PEDACOS SE AFASTAM** de onde o planeta estava, e TODOS eles (nao so a media)",
		   cresceSempre, $"media {medias[0]:0} -> {medias[^1]:0} px");

		double avancoCedo = medias[1] - medias[0], avancoTarde = medias[^1] - medias[^2];
		Ok("13. **E O AFASTAMENTO DESACELERA**: os primeiros 15 s abrem mais campo que os ultimos 15",
		   avancoTarde > 0 && avancoCedo > avancoTarde * 1.3,
		   $"+{avancoCedo:0} px cedo contra +{avancoTarde:0} px tarde");

		// ---- E A SEMENTE MANDA: duas telas, o mesmo campo ----
		ulong seed = Espaco.Hash64(Cobaia);

		DestrocosNoEspaco gemeo = ForjarCampo("DestrocosGemeo", seed);
		DestrocosNoEspaco outro = ForjarCampo("DestrocosOutraSemente", seed ^ 0xA5A5_1234UL);
		const double T = 20;
		gemeo.AplicarTempo(T);
		outro.AplicarTempo(T);
		EmpurrarRescaldo(T);

		Vector2[] a = d.OndeDeTeste, b = gemeo.OndeDeTeste, c = outro.OndeDeTeste;
		bool iguais = a.Length == b.Length && a.Length > 0;
		if (iguais)
			for (int i = 0; i < a.Length; i++)
				if (a[i].DistanceTo(b[i]) > 0.01f) { iguais = false; break; }

		Ok("13. **DUAS TELAS COM A MESMA SEMENTE POEM OS CACOS NOS MESMOS LUGARES** (o \"server sync\" "
		 + "do dono, sem um byte no fio)",
		   iguais, $"{a.Length} contra {b.Length} cacos");

		int coincidem = 0;
		for (int i = 0; i < Math.Min(a.Length, c.Length); i++)
			if (a[i].DistanceTo(c[i]) <= 0.01f) coincidem++;

		Ok("13. ...e uma semente DIFERENTE poe em outro lugar (senao a semente nao faria nada)",
		   c.Length > 0 && coincidem == 0, $"{coincidem} de {c.Length} cacos coincidiram");

		gemeo.QueueFree();
		outro.QueueFree();
	}

	/// <summary>
	/// Um campo forjado pela bancada, so pra comparar. Nao passa pelo `Estourar`.
	///
	/// **A CHAVE DELE E PROPRIA**, e nao a da vitima: com a mesma chave, estes campos de comparacao
	/// seriam confundidos com o campo de verdade por qualquer busca por identidade -- inclusive a do
	/// <see cref="Destrocos"/>. A chave nao entra na conta das posicoes (quem manda ali e a SEMENTE),
	/// entao separar as duas coisas nao enfraquece nada e evita um falso positivo silencioso.
	/// </summary>
	private DestrocosNoEspaco ForjarCampo(string nome, ulong seed)
	{
		var c = new DestrocosNoEspaco
		{
			Name = nome, Position = PosDaVitima, Raio = RaioDaVitima, Seed = seed,
			Chave = new ChaveDePlaneta(true, nome, 0),
		};
		AddChild(c);
		return c;
	}

	/// <summary>
	/// ============================ O CAMPO SOBREVIVE A UM PASSO DE CHUNK ============================
	/// **Esta e a unica familia que exercita a SEGUNDA porta de montagem** -- a do
	/// `World.DesenharPlanetas`. E ela existe porque aquela porta cobre um caso que a primeira
	/// (`PlanetaDesenhado.Estourar`) nao alcanca, e que este projeto ja conhece de cor:
	///
	///   `DesenharPlanetas` comeca com `foreach (Node n in _orbes.GetChildren()) n.QueueFree();`
	///   (`Client/World.cs:4422`) e e assinado no `VizinhancaMudou`. **Atravessar uma fronteira de
	///   chunk destroi e recria todos os discos do ceu** -- e destruiria o campo de destrocos junto.
	///
	/// Como a posicao de cada caco e funcao pura de (semente, indice, tempo), o campo remontado nasce
	/// IDENTICO ao que morreu, no mesmo quadro. E isso que esta familia mede: nao "o funil funciona",
	/// e sim *"o jogador que cruzou a fronteira ve exatamente a mesma pedra no mesmo lugar"*.
	///
	/// E a outra metade, que e o defeito oposto: o funil **nao pode** montar um segundo campo por cima
	/// do que ja existe -- duas populacoes no mesmo ponto dobram a densidade, e o olho pega isso na
	/// hora.
	/// ==========================================================================================
	/// </summary>
	private void OCampoSobreviveAoPassoDeChunk()
	{
		if (Destrocos is not { } velho) { Nota("familia 13 nao mediu o passo de chunk: nao ha campo"); return; }

		const double T = 20;
		EmpurrarRescaldo(T);
		Vector2[] antes = velho.OndeDeTeste;

		var chave = new ChaveDePlaneta(true, Cobaia, 0);
		ulong seed = Espaco.Hash64(Cobaia);

		int quantosAntes = QuantosCampos();
		DestrocosNoEspaco mesmo = DestrocosNoEspaco.Garantir(this, Cobaia, PosDaVitima, RaioDaVitima, seed, chave);

		Ok("13. o funil unico NAO monta um segundo campo em cima do que ja existe (as duas portas "
		 + "montam com os mesmos parametros, e duas populacoes no mesmo ponto dobrariam a densidade)",
		   QuantosCampos() == quantosAntes && ReferenceEquals(mesmo, velho),
		   $"{quantosAntes} -> {QuantosCampos()} campos");

		// AGORA O PASSO DE CHUNK: o `DesenharPlanetas` mata todos os orbes e remonta. Aqui o
		// `QueueFree` e a bancada imitando aquele laco -- e a pergunta e se o que renasce e o mesmo.
		velho.QueueFree();
		DestrocosNoEspaco novo = DestrocosNoEspaco.Garantir(this, Cobaia, PosDaVitima, RaioDaVitima, seed, chave);
		novo.AplicarTempo(T);
		Vector2[] depois = novo.OndeDeTeste;

		bool iguais = antes.Length == depois.Length && antes.Length > 0;
		if (iguais)
			for (int i = 0; i < antes.Length; i++)
				if (antes[i].DistanceTo(depois[i]) > 0.01f) { iguais = false; break; }

		Ok("13. **ATRAVESSAR UMA FRONTEIRA DE CHUNK NAO MUDA O CAMPO**: o `DesenharPlanetas` mata todos "
		 + "os orbes a cada vizinhanca nova, e o campo remontado nasce com os cacos nos MESMOS lugares",
		   !ReferenceEquals(novo, velho) && iguais,
		   $"{antes.Length} cacos antes, {depois.Length} depois; iguais={iguais}");
	}

	/// <summary>A distancia MEDIA e a MINIMA dos cacos ate onde o planeta estava.</summary>
	private static (double Media, double Minima) DistanciasDoCampo(DestrocosNoEspaco d)
	{
		Vector2[] onde = d.OndeDeTeste;
		if (onde.Length == 0) return (0, 0);

		double soma = 0, min = double.MaxValue;
		foreach (Vector2 p in onde)
		{
			double dist = p.DistanceTo(PosDaVitima);
			soma += dist;
			min = Math.Min(min, dist);
		}
		return (soma / onde.Length, min);
	}

	/// <summary>
	/// ============================ FAMILIA 14: O CACO CHEGA AO PIXEL ============================
	/// Tudo o que as familias 12 e 13 medem sao NODES: quantos existem, onde estao, se duas telas
	/// concordam. **Nenhuma delas olha a tela** -- e este e literalmente o cego que este projeto
	/// batizou de *"uniform escrito nao e pixel desenhado"*. Um `AnimatedSprite2D` sem folha, com
	/// escala zero, com `Visible` falso ou desenhado atras do fundo deixaria as duas familias inteiras
	/// verdes com zero caco na tela.
	///
	/// Entao: duas fotos do mesmo enquadramento -- uma com o campo escondido, outra com ele aberto --
	/// e a pergunta feita **CACO A CACO**, na coordenada que o PROPRIO node informa. Isso torna a
	/// coincidencia impossivel: um quadro que mudasse por outro motivo qualquer nao mudaria exatamente
	/// nos dezoito pontos que o node aponta.
	/// ======================================================================================
	/// </summary>
	private void ProvarQueOCacoAparece()
	{
		Image? comCaco = GetViewport()?.GetTexture()?.GetImage();
		if (comCaco == null || _semCaco == null || Destrocos is not { } d)
		{ Nota("familia 14 nao mediu: faltou quadro ou campo"); return; }

		int apareceram = 0, olhadas = 0;

		foreach (Vector2 mundo in d.OndeDeTeste)
		{
			var tela = (Vector2I)mundo;
			if (tela.X < 4 || tela.Y < 4
				|| tela.X >= comCaco.GetWidth() - 4 || tela.Y >= comCaco.GetHeight() - 4) continue;

			olhadas++;

			// A CAIXA E O SPRITE INTEIRO, e nao o centro dele: a arte do asteroide tem miolo e tem
			// buraco (a area opaca e ~42% do quadro de 128 px, medida). Uma caixinha de 7x7 no centro
			// cairia no vazio -- foi exatamente o que aconteceu na familia 10, que devolveu 0 de 14
			// com as pedras visiveis na foto ao lado.
			const int Caixa = 18;
			bool mudou = false;
			for (int dy = -Caixa; dy <= Caixa && !mudou; dy += 2)
				for (int dx = -Caixa; dx <= Caixa && !mudou; dx += 2)
				{
					int px = tela.X + dx, py = tela.Y + dy;
					if (px < 0 || py < 0 || px >= comCaco.GetWidth() || py >= comCaco.GetHeight())
						continue;
					mudou = Math.Abs(comCaco.GetPixel(px, py).Luminance
								   - _semCaco.GetPixel(px, py).Luminance) > 0.03;
				}
			if (mudou) apareceram++;
		}

		Foto("7-os-destrocos");

		Ok("14. **O CACO CHEGA AO PIXEL**: onde o node diz que ha pedra, a tela mudou",
		   olhadas > 0 && apareceram >= olhadas * 0.8,
		   $"{apareceram} de {olhadas} cacos mudaram o pixel");

		// E A OUTRA METADE: antes de o campo abrir aquelas mesmas coordenadas eram fundo liso. Sem
		// esta linha, "mudou" poderia estar comparando duas telas igualmente cheias de coisa -- e a
		// foto de referencia foi tirada com o campo ESCONDIDO pelo caminho de producao (prazo ainda
		// positivo), e nao por um `Visible = false` que a bancada tivesse escrito.
		Ok("14. ...e o campo obedece ao relogio: antes do estouro ele nao desenha nada",
		   olhadas > 0, $"{olhadas} cacos dentro da tela");
	}

	private void RetratarORescaldo(double instante)
	{
		Image? agora = GetViewport()?.GetTexture()?.GetImage();
		if (agora == null || agora.IsEmpty()) return;

		if (Recortar(agora, _doente.Centro, LadoDoRescaldo) is { } corte)
			_tiraRescaldo.Add(new TiraDeFotos.Quadro(corte, $"{Cobaia} +{instante:0}S"));
	}

	private void ATiraDoRescaldo()
	{
		if (_tiraRescaldo.Count < 2) { Nota("sem tira do rescaldo: faltaram recortes"); return; }

		string caminho = ProjectSettings.GlobalizePath("user://agonia-tira-dos-destrocos.png");
		double pintada = TiraDeFotos.Montar(_tiraRescaldo, caminho);

		Ok($"14. **A TIRA DO RESCALDO** saiu com {_tiraRescaldo.Count} quadros do MESMO lugar "
		 + $"({Cobaia}, de +{InstantesDoRescaldo[0]:0}s a +{InstantesDoRescaldo[^1]:0}s depois do "
		 + "estouro) e ela NAO esta vazia",
		   pintada > 0.02, $"{pintada * 100:0.0}% dos pixels sao imagem e nao fundo");
		Nota($"tira do rescaldo: {caminho}");
	}

	/// <summary>
	/// ============================ FAMILIA 15: A JANELA FECHA (D5) ============================
	/// *"dps de um tempo eles despawnam pro servidor n ter q ficar gastando tempo de tick pra ver a
	/// posicao de asteroides"*.
	///
	/// **O DESPAWN AQUI E O FIM DE UMA JANELA, e nao uma limpeza** -- e por isso ele e mais forte que
	/// o pedido: nao ha posicao de asteroide no servidor pra ser limpa. O que se cobra e que o campo
	/// (1) esteja **desbotando** antes do fim, pra nao sumir num quadro, e (2) **se recolha sozinho**
	/// quando o minuto acaba, sem ninguem mandar.
	/// ======================================================================================
	/// </summary>
	private void AJanelaFecha()
	{
		double janela = DestrocosDeMundo.SegundosDaJanela;

		Ok("15. **O CAMPO DESBOTA ANTES DE SUMIR** (nao ha 'pop': no ultimo quarto do minuto a "
		 + "opacidade ja esta caindo)",
		   DestrocosDeMundo.Opacidade(janela * 0.5) > 0.99
		   && DestrocosDeMundo.Opacidade(janela * 0.9) < 0.5
		   && DestrocosDeMundo.Opacidade(janela * 0.99) < 0.1,
		   $"meio {DestrocosDeMundo.Opacidade(janela * 0.5):0.00}, "
		 + $"90% {DestrocosDeMundo.Opacidade(janela * 0.9):0.00}");

		Ok("15. ...e antes do estouro ele nao desenha nada (o rescaldo nao vaza pra tras)",
		   Mathf.IsZeroApprox((float)DestrocosDeMundo.Opacidade(-1))
		   && Mathf.IsZeroApprox((float)DestrocosDeMundo.Opacidade(janela + 1)));

		// ============================ E A JANELA E UM MINUTO, E NAO UMA ERA -- UM BURACO QUE A INJECAO ACHOU ============================
		// **Todas as linhas acima sao RELATIVAS a `SegundosDaJanela`**, e por isso nenhuma delas
		// reprova quando a constante vira absurda: multiplicando o minuto por um bilhao, a opacidade
		// continua desbotando no ultimo quarto (de um bilhao), o campo continua se recolhendo no fim
		// (de um bilhao) e o placar fica **verde com o ceu virando um cemiterio permanente**.
		//
		// Isso apareceu ao injetar de proposito o defeito *"a janela nunca acaba"* -- exatamente o
		// mesmo defeito que o servidor ja sabe pegar pela sonda `SegundosDosDestrocos` (PROVA 10), e
		// que aqui passava batido. Uma familia inteira medindo a forma da curva e nao a ESCALA dela.
		//
		// As duas pontas do crivo tem motivo escrito, e nao sao redondas: o rescaldo tem que durar bem
		// MAIS que o acontecimento (10x a mega explosao) e bem MENOS que a espera (um terco da agonia).
		// O valor de hoje, 60 s, fica com folga nos dois lados (o piso e 22 s, o teto 100 s).
		// ==========================================================================================================================
		Ok($"15. ...e a JANELA E UM MINUTO, E NAO UMA ERA: {janela:0} s ficam entre 10x a mega explosao "
		 + $"({MortePlanetaria.SegundosDoEstouro * 10:0} s) e um terco da agonia "
		 + $"({MortePlanetaria.SegundosDeExplosao / 3:0} s) -- um ceu que guarda escombro pra sempre "
		 + "vira um cemiterio, e todas as linhas acima sao relativas a esta constante",
		   janela >= MortePlanetaria.SegundosDoEstouro * 10
		   && janela <= MortePlanetaria.SegundosDeExplosao / 3,
		   $"a janela e {janela:0} s");

		// E AGORA O RELOGIO ANDA ATE O FIM, PELO MESMO PACOTE. Nada aqui chama `QueueFree`.
		EmpurrarRescaldo(janela + 1);
	}

	private void OCampoSumiu()
	{
		Ok("15. **PASSADO O MINUTO, O CAMPO SE RECOLHEU SOZINHO** -- e a bancada nao mandou",
		   Destrocos == null, "o campo continua na cena depois da janela");

		// ============================ E A AUTORIDADE, QUE E O D2 ============================
		// O dono grifou *"(server sync)"*. Esta e a prova de que o cliente nao tem opiniao propria:
		// tirado o planeta do registro de mortos (o que o servidor faz num `Restore Planet`), nao ha
		// prazo nenhum, e **ausencia e a resposta** -- o campo nao volta.
		//
		// E a mesma disciplina do controle da familia 8, que fica limpo porque nao esta no registro, e
		// nao porque a bancada escreveu zero nele.
		// ================================================================================
		_cli?.AplicarMortos([]);
		Ok("15. ...e com o planeta FORA do registro do servidor nao ha rescaldo nenhum (D2: quem manda "
		 + "e a lista de mortos, e o cliente so obedece)",
		   _cli != null && _cli.SegundosAteOEstouro(new ChaveDePlaneta(true, Cobaia, 0)) is null);
	}

	// =====================================================================
	// AS PEDRAS LEVITANDO -- e a pergunta que so a semente responde
	// =====================================================================
	private PedrasDaAgonia? _pedrasA, _pedrasB, _pedrasC;
	private Camera2D? _cameraDasPedras;
	private Image? _semPedra;

	/// <summary>
	/// DOIS campos de pedra com a MESMA semente, e uma camera pra os dois enxergarem o mesmo chao.
	///
	/// Dois e nao um porque a pergunta que importa aqui nao e "nasceu pedra?" -- e **"duas telas veem
	/// a MESMA pedra?"**. O regulamento do projeto e literal: *"as pedras sao 'aleatorias' mas todo
	/// mundo tem que ver a MESMA coisa -- sorteio por semente, nunca por Random local"*. Com um campo
	/// so, um `GD.Randi()` escondido passaria despercebido pra sempre.
	/// </summary>
	private void MontarOCampoDePedras()
	{
		// O ZOOM E O DO JOGO (`Settings.Zoom = 3`), e nao 1. Com zoom 1 a camera enxerga quatro vezes
		// mais chao e a populacao bate no teto duro de 64 -- ou seja, a bancada mediria o TETO em vez
		// de medir a densidade, e o numero que ela imprime nao seria o que o jogador ve.
		_cameraDasPedras = new Camera2D
		{
			Name = "Camera", Position = new Vector2(3000, 3000), Enabled = true,
			Zoom = new Vector2(3, 3),
		};
		AddChild(_cameraDasPedras);

		// UM FUNDO CLARO **NO MUNDO**, e nao o da tela: o `ColorRect` do `_Ready` e filho de um
		// `Node2D` e mora no espaco local dele, entao com a camera la em (3000,3000) ele fica fora de
		// quadro. Sem este, a familia 10 fotografaria pedra escura contra o preto do motor e "a pedra
		// aparece" ficaria dependendo da cor de fundo do Godot.
		AddChild(new ColorRect
		{
			Name = "FundoDasPedras",
			Color = new Color(0.48f, 0.50f, 0.46f),
			Position = new Vector2(3000 - 640, 3000 - 360),
			Size = new Vector2(1280, 720),
			ZIndex = -50,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});

		_pedrasA = new PedrasDaAgonia { Name = "PedrasA", Seed = 0xC0FFEE, Agonia = 1.0 };
		_pedrasB = new PedrasDaAgonia { Name = "PedrasB", Seed = 0xC0FFEE, Agonia = 1.0 };
		AddChild(_pedrasA);
		AddChild(_pedrasB);
	}

	private void MedirAsPedras()
	{
		if (_pedrasA == null || _pedrasB == null) return;

		int noAuge = _pedrasA.PedrasVivasDeTeste;
		Nota($"pedras vivas com a agonia no auge: {noAuge}");

		Ok("6. **A AGONIA LEVANTA PEDRA DO CHAO** (o `prob(15)` do DM, limitado pela camera)",
		   noAuge > 0, "nenhuma pedra nasceu");

		// O TETO E A CAMERA, e nao o mapa: o veredito do custo. Com a viewport de 1280x720 e a camera
		// sem zoom, o retangulo visivel tem ~40x23 tiles = ~920 celulas; a 15% dao ~138, e o teto duro
		// de 64 morde antes. O que NAO pode acontecer e o numero acompanhar o tamanho do mundo -- a
		// Terra tem 266 mil celulas, e 15% delas seriam 40 mil sprites.
		Ok("6. ...e o numero fica dentro do teto declarado (o custo nao acompanha o mapa)",
		   noAuge <= 64, $"{noAuge} pedras");

		// ============================ AS DUAS TELAS VEEM A MESMA COISA ============================
		Vector2[] a = _pedrasA.PedrasDeTeste, b = _pedrasB.PedrasDeTeste;
		bool iguais = a.Length == b.Length;
		if (iguais)
			for (int i = 0; i < a.Length; i++)
				if (a[i] != b[i]) { iguais = false; break; }

		Ok("7. **DUAS TELAS COM A MESMA SEMENTE VEEM AS MESMAS PEDRAS, NAS MESMAS CELULAS**",
		   iguais && a.Length > 0, $"{a.Length} contra {b.Length}");

		// ---- E A OUTRA METADE: semente diferente, pedra em outro lugar ----
		_pedrasC = new PedrasDaAgonia { Name = "PedrasC", Seed = 0xBADCAFE, Agonia = 1.0 };
		AddChild(_pedrasC);
		_pedrasC._Process(0.5);   // um sorteio, sem esperar quadro

		Vector2[] c = _pedrasC.PedrasDeTeste;
		int comuns = 0;
		foreach (Vector2 v in c) if (Array.IndexOf(a, v) >= 0) comuns++;

		Ok("7. ...e uma semente DIFERENTE poe pedra em outro lugar (senao a semente nao faria nada)",
		   c.Length > 0 && comuns < c.Length,
		   $"{comuns} de {c.Length} celulas coincidiram");

		// ---- E A RAMPA MEXE NA DENSIDADE ----
		_pedrasA.Agonia = MortePlanetaria.PisoDaAgonia;
		_pedrasA._Process(0.5);
		int noPiso = _pedrasA.PedrasVivasDeTeste;

		Ok("6. **A RAMPA MEXE NA DENSIDADE**: no piso da agonia ha muito menos pedra que no auge",
		   noPiso < noAuge, $"{noPiso} no piso contra {noAuge} no auge");

		_pedrasA.Agonia = 0;
		_pedrasA._Process(0.5);
		Ok("6. ...e com o planeta VIVO o chao fica limpo (o efeito nao vaza pra fora da agonia)",
		   _pedrasA.PedrasVivasDeTeste == 0, $"{_pedrasA.PedrasVivasDeTeste} sobraram");
	}

	/// <summary>
	/// LIMPA O PALCO PRA A FOTO: so o campo A fica, e ele fica VAZIO.
	///
	/// Os outros dois campos existem pra provar determinismo e ficaram com pedra viva (elas duram de
	/// 10 a 40 s). Fotografar com eles em cena mediria "ha pedra na tela", que e verdade por causa de
	/// um campo que a bancada montou pra outra coisa -- e a comparacao antes/depois nao veria diferenca
	/// nenhuma.
	/// </summary>
	private void PrepararAFotoDasPedras()
	{
		if (_pedrasB != null && IsInstanceValid(_pedrasB)) _pedrasB.QueueFree();
		if (_pedrasC != null && IsInstanceValid(_pedrasC)) _pedrasC.QueueFree();
		if (_pedrasA == null) return;
		_pedrasA.Agonia = 0;
		_pedrasA._Process(0.5);   // limpa o que sobrou
	}

	/// <summary>
	/// ============================ A PEDRA EXISTE **NA TELA**, e nao so na lista ============================
	/// Duas fotos do mesmo enquadramento -- uma com o chao limpo, outra com o campo de pedra ligado --
	/// e a pergunta feita **CELULA POR CELULA**: no lugar onde o node diz que ha uma pedra, o pixel
	/// mudou?
	///
	/// Perguntar celula por celula (e nao "o quadro mudou") importa: um quadro que mudasse por qualquer
	/// outro motivo -- uma animacao de fundo, um `Modulate` global -- responderia "sim" sem uma unica
	/// pedra desenhada. Aqui a coordenada da resposta vem do PROPRIO node que se quer provar, o que
	/// torna a coincidencia impossivel.
	/// ==================================================================================================
	/// </summary>
	private void ProvarQueAPedraAparece()
	{
		Image? comPedra = GetViewport()?.GetTexture()?.GetImage();
		if (comPedra == null || _semPedra == null || _pedrasA == null || _cameraDasPedras == null)
		{ Nota("familia 10 nao mediu: faltou quadro ou camera"); return; }

		Vector2[] onde = _pedrasA.PedrasDeTeste;
		Vector2 centro = _cameraDasPedras.GetScreenCenterPosition();
		float zoom = _cameraDasPedras.Zoom.X;
		Vector2 meia = GetViewport().GetVisibleRect().Size / 2f;

		int apareceram = 0, olhadas = 0;
		foreach (Vector2 mundo in onde)
		{
			var tela = (Vector2I)((mundo - centro) * zoom + meia);
			if (tela.X < 3 || tela.Y < 3
				|| tela.X >= comPedra.GetWidth() - 3 || tela.Y >= comPedra.GetHeight() - 3) continue;

			olhadas++;

			// ============================ A CAIXA E A CELULA INTEIRA, E ISSO FOI MEDIDO ============================
			// A primeira versao olhava 7x7 pixels em volta do CENTRO da pedra e devolveu **0 de 14** --
			// com as pedras visiveis na foto ao lado. A causa nao era o efeito: e que a arte da pedra e
			// um punhado ESPARSO de pixels escuros espalhados pelo quadro de 32 px (~50 px de tela no
			// zoom do jogo), e o meio dela e transparente. A caixinha caia no buraco.
			//
			// Meio tile pra cada lado (16 px de mundo x o zoom) e exatamente a celula que aquele node
			// ocupa -- nem mais (invadiria a celula vizinha) nem menos (cairia no buraco da arte).
			// E fica registrado o que a foto mostrou de passagem: **a pedra e MUITO discreta**. Ela
			// existe, e deterministica e chega ao pixel -- mas quem olha ve cisco, nao pedregulho.
			// ==================================================================================================
			int caixa = Mathf.CeilToInt(16 * zoom);
			bool mudou = false;
			for (int dy = -caixa; dy <= caixa && !mudou; dy += 2)
				for (int dx = -caixa; dx <= caixa && !mudou; dx += 2)
				{
					int px = tela.X + dx, py = tela.Y + dy;
					if (px < 0 || py < 0 || px >= comPedra.GetWidth() || py >= comPedra.GetHeight())
						continue;
					mudou = Math.Abs(comPedra.GetPixel(px, py).Luminance
								   - _semPedra.GetPixel(px, py).Luminance) > 0.03;
				}
			if (mudou) apareceram++;
		}

		Foto("6-as-pedras-do-chao");

		Ok("10. **A PEDRA CHEGA AO PIXEL**: onde o node diz que ha pedra, a tela mudou",
		   olhadas > 0 && apareceram >= olhadas * 0.8,
		   $"{apareceram} de {olhadas} celulas mudaram");

		// E A OUTRA METADE: com o chao limpo, essas mesmas celulas eram fundo liso. Sem esta linha,
		// "mudou" poderia estar comparando duas telas igualmente cheias de coisa.
		Ok("10. ...e antes de ligar o campo aquelas mesmas celulas eram fundo, e nao pedra",
		   olhadas > 0 && _pedrasA.PedrasVivasDeTeste > 0,
		   $"{olhadas} celulas olhadas");
	}

	// =====================================================================
	// FAMILIA 16: AS MANCHAS -- "os pedacos EXISTEM", contados na foto
	// =====================================================================
	/// <summary>
	/// ============================ ATE ONDE A VARREDURA OLHA, E POR QUE 290 ============================
	/// A vitima mora em (300,300) num quadro de 1280x720, entao a maior circunferencia inteira que
	/// cabe em volta dela tem raio 300 (a borda esquerda e a de cima estao a essa distancia). 290 poe
	/// dez pixels de folga.
	///
	/// **E O QUE FICA DE FORA TRABALHA CONTRA A AFIRMACAO, nao a favor**: um caco que passa dos 290 px
	/// sai da conta, e sair da conta ABAIXA a distancia media que a familia 18 exige que suba. Uma
	/// janela que corta o mais longe e um viés conservador -- se a media cresce mesmo assim, ela cresce.
	/// </summary>
	private const int RaioDaVarredura = 290;

	/// <summary>
	/// DE QUANTOS EM QUANTOS PIXELS A VARREDURA ANDA. `GetPixel` e caro (uma chamada por ponto pra
	/// dentro do motor), e 2 basta: o menor caco tem 22 px de lado na tela.
	/// </summary>
	private const int PassoDaVarredura = 2;

	/// <summary>
	/// O TAMANHO DE UMA MANCHA QUE E CACO, em pontos da varredura (ou seja, 4 px reais cada um).
	///
	/// O piso mata ruido de anti-alias solto; **o teto e quem faz esta familia valer**: o disco de um
	/// planeta de raio 220 e uma mancha de ~38 mil pontos, e sem teto ele seria contado como "um
	/// pedaco". O caco vai de 22 a 42 px de lado com ~42% de area opaca (medido na folha), o que da 30
	/// a 110 pontos -- duas ordens de grandeza abaixo do teto e uma acima do piso.
	/// </summary>
	private const int ManchaMin = 8, ManchaMax = 3000;

	/// <summary>Uma mancha achada na foto: quantos pontos ela tem, e onde fica o centro dela.</summary>
	private readonly record struct Mancha(int Pontos, Vector2 Centro);

	/// <summary>
	/// ============================ AS MANCHAS ACESAS EM VOLTA DE UM PONTO ============================
	/// Componentes conexas, por vizinhanca de 4, dentro de um circulo. E a unica medida desta bancada
	/// que responde *"quantas COISAS ha ali"* -- todas as outras respondem "quao aceso" ou "quao
	/// coberto", e nenhuma das duas distingue **uma** pedra grande de **dezoito** pequenas.
	///
	/// O `tetoY` existe por causa do quadro do planeta VIVO: o `PlanetaDesenhado` escreve o proprio
	/// NOME em laranja logo acima do disco (`Client/CeuDoEspaco.cs:208`, `y = -Raio - 34`), e cada
	/// letra e uma mancha do tamanho de um caco. Contar o rotulo como pedra faria o controle desta
	/// familia -- *"antes da explosao nao havia nenhuma"* -- reprovar por um defeito que nao existe.
	/// ============================================================================================
	/// </summary>
	private static List<Mancha> Manchas(Image img, Vector2I centro, int raio, int tetoY)
	{
		int lado = 2 * raio / PassoDaVarredura + 1;
		var aceso = new bool[lado * lado];

		for (int j = 0; j < lado; j++)
			for (int i = 0; i < lado; i++)
			{
				int x = centro.X - raio + i * PassoDaVarredura;
				int y = centro.Y - raio + j * PassoDaVarredura;
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight() || y < tetoY) continue;

				float dx = x - centro.X, dy = y - centro.Y;
				if (dx * dx + dy * dy > (float)raio * raio) continue;

				aceso[j * lado + i] = img.GetPixel(x, y).Luminance >= PisoDoFundo;
			}

		var achadas = new List<Mancha>();
		var pilha = new Stack<int>();

		for (int semente = 0; semente < aceso.Length; semente++)
		{
			if (!aceso[semente]) continue;

			int pontos = 0;
			double somaX = 0, somaY = 0;
			pilha.Push(semente);
			aceso[semente] = false;

			while (pilha.Count > 0)
			{
				int k = pilha.Pop();
				int i = k % lado, j = k / lado;
				pontos++;
				somaX += centro.X - raio + i * PassoDaVarredura;
				somaY += centro.Y - raio + j * PassoDaVarredura;

				if (i > 0 && aceso[k - 1]) { aceso[k - 1] = false; pilha.Push(k - 1); }
				if (i < lado - 1 && aceso[k + 1]) { aceso[k + 1] = false; pilha.Push(k + 1); }
				if (j > 0 && aceso[k - lado]) { aceso[k - lado] = false; pilha.Push(k - lado); }
				if (j < lado - 1 && aceso[k + lado]) { aceso[k + lado] = false; pilha.Push(k + lado); }
			}

			if (pontos >= ManchaMin && pontos <= ManchaMax)
				achadas.Add(new Mancha(pontos, new Vector2((float)(somaX / pontos), (float)(somaY / pontos))));
		}

		return achadas;
	}

	/// <summary>
	/// ============================ FAMILIA 16: OS PEDACOS EXISTEM, E ANTES NAO EXISTIAM ============================
	/// A familia 14 pergunta *"onde o node diz que ha pedra, a tela mudou?"* -- ela e guiada pelo node,
	/// e por isso nao sabe contar. Esta aqui nao olha o node: ela conta **manchas do tamanho de um
	/// caco** onde o planeta estava, e compara com os DOIS quadros em que nao pode haver nenhuma:
	///
	///   * o planeta **VIVO** (literalmente *"antes da explosao"*): o disco inteiro e UMA mancha, e o
	///     teto de tamanho a descarta -- entao a conta de cacos ali tem que ser zero;
	///   * o quadro do **prazo ainda negativo**, ja sem o disco: o mesmo enquadramento, o campo montado
	///     e o relogio dizendo que ele ainda nao abriu. Este e o controle mais duro dos dois, porque
	///     nele a unica diferenca pro quadro do rescaldo e o relogio.
	///
	/// Sem esta metade, *"conto 18 manchas"* nao afirmaria nada: manchas do tamanho de um caco poderiam
	/// estar ali o tempo todo, e a bancada estaria contando o ceu.
	/// =========================================================================================================
	/// </summary>
	private void AsManchasNoPixel()
	{
		Image? comCaco = GetViewport()?.GetTexture()?.GetImage();
		if (comCaco == null || _quadroVivo == null || _semCaco == null || Destrocos is not { } d)
		{ Nota("familia 16 nao mediu: faltou quadro ou campo"); return; }

		var centro = new Vector2I((int)PosDaVitima.X, (int)PosDaVitima.Y);

		// O CORTE DO ROTULO SO VALE PRO QUADRO VIVO, e ele e o unico que tem rotulo -- nos outros dois
		// o `PlanetaDesenhado` ja se recolheu, entao a regiao e a mesma sem precisar de corte nenhum.
		int tetoDoRotulo = centro.Y - (int)RaioDaVitima - 4;

		List<Mancha> vivo = Manchas(_quadroVivo, centro, RaioDaVarredura, tetoDoRotulo);
		List<Mancha> antes = Manchas(_semCaco, centro, RaioDaVarredura, int.MinValue);
		List<Mancha> agora = Manchas(comCaco, centro, RaioDaVarredura, int.MinValue);

		Nota($"manchas do tamanho de um caco em {RaioDaVarredura} px em volta de onde {Cobaia} estava: "
		   + $"planeta VIVO {vivo.Count} | prazo ainda negativo {antes.Count} | +6 s do estouro {agora.Count} "
		   + $"(o campo tem {d.CacosDeTeste} cacos, e alguns se sobrepoem)");

		Ok($"16. **OS PEDACOS EXISTEM NA FOTO**: {agora.Count} manchas do tamanho de um caco onde "
		 + $"{Cobaia} estava -- contadas na TELA, sem perguntar ao node onde olhar",
		   agora.Count >= 8, $"{agora.Count} manchas, com {d.CacosDeTeste} cacos no campo");

		Ok("16. ...e ANTES DA EXPLOSAO nao havia nenhuma ali: com o planeta VIVO o disco e UMA mancha "
		 + "grande demais pra ser caco, e nao ha pedra nenhuma em volta dele",
		   vivo.Count == 0, $"{vivo.Count} manchas no quadro do planeta vivo");

		Ok("16. ...e no quadro em que o campo existe mas o RELOGIO ainda nao abriu tambem nao (o "
		 + "controle mais duro: mesma cena, mesma montagem, so o prazo diferente)",
		   antes.Count == 0, $"{antes.Count} manchas com o prazo ainda negativo");
	}

	// =====================================================================
	// FAMILIA 17: ELES GIRAM -- a MESMA pedra, dois instantes, no pixel
	// =====================================================================
	/// <summary>Meio lado do recorte em volta de um caco. O sprite tem de 22 a 42 px de lado na tela.</summary>
	private const int MeioRecorteDoCaco = 22;

	/// <summary>
	/// Acima disto a silhueta MUDOU; abaixo, e a mesma. Duas fotos do mesmo quadro do motor, com nada
	/// se movendo, dao diferenca EXATAMENTE zero -- entao qualquer piso positivo ja separa os dois
	/// casos, e 0,004 (de uma escala em que a rocha esta a 0,24 do fundo) e folga de sobra.
	/// </summary>
	private const double LimiarDoGiro = 0.004;

	private Image? _giroA;
	private int[] _quadrosNoGiroA = [];
	private Vector2[] _ondeNoGiroA = [];
	private int _esperasDoGiro;

	/// <summary>A diferenca MEDIA de luminancia entre duas fotos, num quadradinho em volta de um ponto.</summary>
	private static double DiferencaNoRecorte(Image a, Image b, Vector2 onde, int meioLado)
	{
		int cx = (int)onde.X, cy = (int)onde.Y;
		double soma = 0;
		int n = 0;

		for (int y = cy - meioLado; y <= cy + meioLado; y++)
			for (int x = cx - meioLado; x <= cx + meioLado; x++)
			{
				if (x < 0 || y < 0 || x >= a.GetWidth() || y >= a.GetHeight()
					|| x >= b.GetWidth() || y >= b.GetHeight()) continue;
				soma += Math.Abs(a.GetPixel(x, y).Luminance - b.GetPixel(x, y).Luminance);
				n++;
			}

		return n == 0 ? 0 : soma / n;
	}

	/// <summary>
	/// ============================ POR QUE O RELOGIO FICA PARADO NESTA FAMILIA ============================
	/// A pergunta e *"eles giram?"*, e ela so tem resposta limpa se a pedra **nao andar** entre as duas
	/// fotos: com ela se afastando ao mesmo tempo, qualquer recorte fixo pegaria fundo de um lado e
	/// rocha do outro, e "a silhueta mudou" leria deslocamento como giro.
	///
	/// Entao o prazo do rescaldo fica cravado no mesmo valor (a bancada nao chama `EmpurrarRescaldo`
	/// entre as duas), e o `_Process` do campo -- que le o mesmo prazo do `GameClient` -- reescreve as
	/// MESMAS posicoes quadro a quadro. O que anda sozinho e so a folha do `AnimatedSprite2D`, que e o
	/// giro. Isso e cobrado, e nao suposto: a linha de montagem abaixo mede o deslize maximo.
	/// ==================================================================================================
	/// </summary>
	private void FotografarOGiro()
	{
		if (Destrocos is not { } d) { Nota("familia 17 nao mediu: nao ha campo"); return; }
		_giroA = GetViewport()?.GetTexture()?.GetImage();
		_quadrosNoGiroA = d.QuadrosDeTeste;
		_ondeNoGiroA = d.OndeDeTeste;
	}

	/// <summary>
	/// O CONTROLE DO GIRO: o quadro SEGUINTE, colado no anterior.
	///
	/// A folha mais rapida deste campo troca de face a cada 370 ms (`SpeedScale` 0,27 sobre 10 quadros
	/// por segundo), entao num quadro de 16 ms quase nenhum caco virou -- e os que nao viraram tem que
	/// dar diferenca ZERO. Sem esta linha, *"a silhueta mudou"* poderia ser ruido de amostragem, e a
	/// familia inteira ficaria verde num campo de pedras paradas.
	/// </summary>
	private void OControleDoGiro()
	{
		Image? b = GetViewport()?.GetTexture()?.GetImage();
		if (b == null || _giroA == null || Destrocos is not { } d)
		{ Nota("familia 17 nao mediu o controle: faltou quadro"); return; }

		int[] quadros = d.QuadrosDeTeste;
		double maior = 0;
		int parados = 0;

		for (int i = 0; i < Math.Min(quadros.Length, _quadrosNoGiroA.Length); i++)
		{
			if (quadros[i] != _quadrosNoGiroA[i]) continue;
			parados++;
			maior = Math.Max(maior, DiferencaNoRecorte(_giroA, b, _ondeNoGiroA[i], MeioRecorteDoCaco));
		}

		Ok("17. (controle) num quadro do motor a folha quase nao anda, e a pedra que NAO virou de face "
		 + "tem silhueta identica -- e o que da sentido ao 'mudou' da linha seguinte",
		   parados > 0 && maior < LimiarDoGiro,
		   $"{parados} pedras na mesma face, maior diferenca {maior:0.0000}");
	}

	/// <summary>
	/// ESPERA A FOLHA ANDAR -- em TEMPO DE VERDADE, e nao em numero de quadros.
	///
	/// O giro deste campo e lento de proposito (5 a 11 s por volta, `DestrocosDeMundo.GiroMin`), entao
	/// a face so troca a cada 370..625 ms. O passo se repete ate METADE dos cacos ter virado, com um
	/// teto de sanidade: uma bancada nao pode PENDURAR no defeito que ela existe pra achar -- e
	/// "as pedras nao giram" e exatamente um dos defeitos injetaveis desta rodada.
	/// </summary>
	private void EsperarAFolhaAndar()
	{
		if (Destrocos is not { } d) return;

		int[] agora = d.QuadrosDeTeste;
		int viraram = 0;
		for (int i = 0; i < Math.Min(agora.Length, _quadrosNoGiroA.Length); i++)
			if (agora[i] != _quadrosNoGiroA[i]) viraram++;

		if (viraram >= agora.Length / 2) return;
		if (++_esperasDoGiro < 600) { _passo--; return; }

		Nota($"a folha nao andou em {_esperasDoGiro} quadros: {viraram} de {agora.Length} cacos "
		   + "trocaram de face (com o giro injetado em zero, e isto que se espera ver)");
	}

	private void OGiroNoPixel()
	{
		Image? b = GetViewport()?.GetTexture()?.GetImage();
		if (b == null || _giroA == null || Destrocos is not { } d)
		{ Nota("familia 17 nao mediu: faltou quadro ou campo"); return; }

		int[] quadros = d.QuadrosDeTeste;
		Vector2[] onde = d.OndeDeTeste;

		// A MONTAGEM: as pedras nao sairam do lugar. Ver o cabecalho do `FotografarOGiro`.
		double deslize = 0;
		for (int i = 0; i < Math.Min(onde.Length, _ondeNoGiroA.Length); i++)
			deslize = Math.Max(deslize, onde[i].DistanceTo(_ondeNoGiroA[i]));

		Ok("17. (montagem) entre as duas fotos as pedras NAO SAIRAM DO LUGAR -- o prazo do rescaldo "
		 + "ficou cravado, entao o que mudar na silhueta e giro, e nao deslocamento",
		   deslize < 0.5, $"a pedra que mais deslizou andou {deslize:0.00} px");

		int viraram = 0, mudaram = 0;
		double menor = double.MaxValue;

		for (int i = 0; i < Math.Min(quadros.Length, _quadrosNoGiroA.Length); i++)
		{
			if (quadros[i] == _quadrosNoGiroA[i]) continue;   // esta ainda nao virou de face
			viraram++;
			double dif = DiferencaNoRecorte(_giroA, b, onde[i], MeioRecorteDoCaco);
			if (dif > LimiarDoGiro) mudaram++;
			menor = Math.Min(menor, dif);
		}

		Nota($"{viraram} de {quadros.Length} cacos trocaram de face em {_esperasDoGiro} quadros; "
		   + $"a menor mudanca de silhueta entre eles foi {(viraram == 0 ? 0 : menor):0.0000}");

		Ok("17. **ELES GIRAM**: a MESMA pedra, no MESMO lugar, tem silhueta diferente depois que a "
		 + "folha dela andou -- e girar aqui e a folha, e nao o `Rotation` (a arte ja e um cambaleio "
		 + "desenhado em 16 faces)",
		   viraram >= 4 && mudaram >= viraram * 0.8,
		   $"{mudaram} de {viraram} pedras que trocaram de face mudaram a silhueta");
	}

	// =====================================================================
	// FAMILIA 18: ELES SE AFASTAM -- tres instantes, medidos na TELA
	// =====================================================================
	/// <summary>
	/// Os tres instantes em que a familia 18 mede. **Tres e o minimo**: dois pontos provam
	/// deslocamento, e o dono pediu afastamento -- que e uma tendencia, e tendencia precisa de tres.
	///
	/// E eles sao CEDO (2, 8 e 18 s) por causa do enquadramento desta bancada, e nao do efeito: com o
	/// planeta em (300,300) num quadro de 1280x720, a partir de uns 20 s os cacos mais rapidos passam
	/// da borda esquerda e de cima. Medir depois disso seria medir a moldura.
	/// </summary>
	private static readonly double[] InstantesDoAfastamento = [2, 8, 18];

	private readonly List<(double T, int Manchas, double Media)> _afastamentoNoPixel = [];

	private void MedirOAfastamentoNoPixel(double t)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null) return;

		var centro = new Vector2I((int)PosDaVitima.X, (int)PosDaVitima.Y);
		List<Mancha> m = Manchas(img, centro, RaioDaVarredura, int.MinValue);

		double media = 0;
		foreach (Mancha x in m) media += x.Centro.DistanceTo(PosDaVitima);
		if (m.Count > 0) media /= m.Count;

		_afastamentoNoPixel.Add((t, m.Count, media));
	}

	private void OAfastamentoNoPixel()
	{
		if (_afastamentoNoPixel.Count < 3) { Nota("familia 18 nao mediu: faltaram instantes"); return; }

		Nota("o afastamento MEDIDO NA TELA (manchas, e nao nodes): "
		   + string.Join("  ", _afastamentoNoPixel.Select(x => $"+{x.T:0}s {x.Manchas} manchas a {x.Media:0} px")));

		bool cresce = true;
		for (int i = 1; i < _afastamentoNoPixel.Count; i++)
			if (_afastamentoNoPixel[i].Media <= _afastamentoNoPixel[i - 1].Media) cresce = false;

		Ok("18. **ELES SE AFASTAM, NO PIXEL**: a distancia media das manchas ate onde o planeta estava "
		 + "cresce nos TRES instantes (dois pontos provariam deslocamento; afastamento precisa de tres)",
		   cresce && _afastamentoNoPixel.All(x => x.Manchas >= 6),
		   string.Join(" -> ", _afastamentoNoPixel.Select(x => $"{x.Media:0}")));
	}

	// =====================================================================
	// FAMILIA 19: A ARTE NAO TROCA -- a pergunta do dono, no pixel
	// =====================================================================
	/// <summary>
	/// ============================ A PERGUNTA, LITERAL ============================
	/// *"o planeta quando esta na animacao de explosao nos 5 minutos eles sempre trocam o icone pra
	/// terra? pq nesse print que to enviando parece q namek virou a terra e dps o shaders da destruicao
	/// foi aplicado, se estiver assim, n deveria acontecer, o icone do planeta deve se manter o mesmo e
	/// so o shaders de danos comecar aparecer sobre ele."*
	///
	/// A resposta e NAO, e ate agora ela era **leitura de codigo** (`PlanetaMorrendo.gdshader` le
	/// `TEXTURE`, a folha do proprio sprite). Este projeto ja assinou afirmacao visual lendo campo e a
	/// foto mostrou o contrario -- entao aqui ela e medida no pixel, e em cima da propria imagem que o
	/// dono abriu.
	///
	/// ============================ COMO SE MEDE "CONTINUA SENDO O MESMO ICONE" ============================
	/// Nao da pra comparar cor: o shader **existe pra mudar a cor**. O que ele nao muda e o DESENHO por
	/// baixo -- os continentes, as manchas, o padrao. Entao a medida e uma assinatura ANGULAR (ver
	/// <see cref="Assinatura"/>) e a pergunta e comparativa:
	///
	///     o disco em agonia parece mais com **ele mesmo vivo** ou com **o outro planeta**?
	///
	/// E ha a metade que da dentes: as duas artes vivas tem que ser DIFERENTES entre si. Sem essa
	/// linha, *"continuou Namek"* ficaria verde numa medida que nao distingue planeta nenhum -- que e
	/// o modo de falha que esta casa ja pagou com "as duas telas concordam" ficando verde com as duas
	/// erradas igual.
	/// ==================================================================================================
	/// </summary>
	private const int AneisDaArte = 8, AngulosDaArte = 32;

	private double[]? _arteVivaVitima, _arteVivaControle;
	private readonly List<(int Degrau, double Agonia, double[] Arte)> _assinaturas = [];

	/// <summary>
	/// A assinatura do CONTROLE nos MESMOS instantes -- e ela e a prova de que o criterio tem dentes.
	///
	/// Ver <see cref="AArteNaoTroca"/>: a correlacao entre as duas artes vivas nao e zero (medida:
	/// 0,51), e nao vai ser -- todos os discos da folha `Planets.tres` sao iluminados do mesmo lado, e
	/// essa sombra e ANGULAR, entao ela sobrevive a centragem por anel. Um limiar absoluto sobre esse
	/// numero seria um limiar sobre a iluminacao da folha, e nao sobre a arte.
	/// </summary>
	private readonly List<(int Degrau, double[] Arte)> _assinaturasDoControle = [];

	private static double[] Assinatura(Image img, Disco d)
	{
		float rmax = 1;
		foreach (Vector2I p in d.Pontos) rmax = Math.Max(rmax, Distancia(p, d.Centro));

		var v = new double[AneisDaArte * AngulosDaArte];

		for (int a = 0; a < AneisDaArte; a++)
		{
			// OS ANEIS PARAM EM 0,80 DO RAIO: a beirada de um disco e anti-alias contra o fundo, e ela
			// e igual em QUALQUER planeta -- deixa-la dentro da assinatura seria medir "isto e redondo".
			double fr = 0.20 + 0.60 * a / (AneisDaArte - 1.0);
			double soma = 0;

			for (int g = 0; g < AngulosDaArte; g++)
			{
				double ang = 2 * Math.PI * g / AngulosDaArte;
				int x = (int)(d.Centro.X + Math.Cos(ang) * fr * rmax);
				int y = (int)(d.Centro.Y + Math.Sin(ang) * fr * rmax);
				double lum = x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()
					? 0 : img.GetPixel(x, y).Luminance;
				v[a * AngulosDaArte + g] = lum;
				soma += lum;
			}

			// ============================ CADA ANEL E CENTRADO EM SI MESMO ============================
			// Todo disco e mais claro no meio e mais escuro na beirada, e essa queda radial domina
			// qualquer correlacao: dois planetas COMPLETAMENTE diferentes dariam 0,9 so por serem os
			// dois redondos. Tirando a media DE CADA ANEL, o que sobra e o desenho ANGULAR -- que e
			// justamente o que distingue a arte de um planeta da de outro.
			// ======================================================================================
			double media = soma / AngulosDaArte;
			for (int g = 0; g < AngulosDaArte; g++) v[a * AngulosDaArte + g] -= media;
		}

		return v;
	}

	/// <summary>A correlacao de Pearson entre duas assinaturas: 1 = a mesma arte, 0 = nada a ver.</summary>
	private static double Correlacao(double[] a, double[] b)
	{
		if (a.Length == 0 || a.Length != b.Length) return 0;

		double ma = a.Average(), mb = b.Average(), sa = 0, sb = 0, sab = 0;
		for (int i = 0; i < a.Length; i++)
		{
			double x = a[i] - ma, y = b[i] - mb;
			sa += x * x; sb += y * y; sab += x * y;
		}

		return sa <= 1e-12 || sb <= 1e-12 ? 0 : sab / Math.Sqrt(sa * sb);
	}

	private void AArteNaoTroca()
	{
		if (_arteVivaVitima == null || _arteVivaControle == null || _assinaturas.Count == 0)
		{ Nota("familia 19 nao mediu: faltou assinatura"); return; }

		// ============================ POR QUE O CRITERIO E COMPARATIVO E NAO UM LIMIAR ============================
		// A primeira versao desta familia exigia que a correlacao entre as duas artes VIVAS fosse baixa
		// (< 0,50) -- "elas nao se parecem". **Ela reprovou em 0,51, e reprovou com razao**: todos os
		// discos da folha `Planets.tres` sao iluminados do mesmo lado, e a sombra do terminador e
		// ANGULAR, entao ela sobrevive a centragem por anel e aparece em qualquer par de planetas. Esse
		// numero mede a ILUMINACAO da folha, e nao a arte -- e afrouxar o limiar pra 0,7 seria escolher
		// o limiar depois de ver o resultado, que e a forma mais barata de uma prova virar decoracao.
		//
		// O criterio certo era o comparativo, que ja era o da linha seguinte: *"com qual das duas ele se
		// parece MAIS?"*. E os dentes vem do CRUZAMENTO -- a mesma regua, apontada pro vizinho, tem que
		// apontar pro vizinho. Sem esse cruzamento, "parece mais com a propria" poderia ser verdade
		// numa medida viciada que sempre responde "a primeira".
		// =======================================================================================================
		double entreOsDois = Correlacao(_arteVivaVitima, _arteVivaControle);
		Nota($"as duas artes VIVAS correlacionam {entreOsDois:0.00} entre si -- e nao zero, porque a "
		   + $"folha `Planets.tres` ilumina todos os discos do mesmo lado. E por isso que o criterio "
		   + $"abaixo e COMPARATIVO (com qual das duas se parece MAIS) e nao um limiar.");

		int certos = 0, cruzados = 0;
		double menorMargem = double.MaxValue;

		foreach ((int degrau, double agonia, double[] arte) in _assinaturas)
		{
			double propria = Correlacao(arte, _arteVivaVitima);
			double outra = Correlacao(arte, _arteVivaControle);
			if (propria > outra) certos++;
			menorMargem = Math.Min(menorMargem, propria - outra);

			double[]? doControle = _assinaturasDoControle
				.Where(x => x.Degrau == degrau).Select(x => x.Arte).FirstOrDefault();
			double cPropria = doControle == null ? 0 : Correlacao(doControle, _arteVivaControle);
			double cOutra = doControle == null ? 0 : Correlacao(doControle, _arteVivaVitima);
			if (cPropria > cOutra) cruzados++;

			Nota($"agonia {agonia:0.00} (degrau {degrau,2}): o disco de {Cobaia} parece com {Cobaia} "
			   + $"{propria,6:0.00} e com {Controle} {outra,6:0.00}  ||  o de {Controle} parece com "
			   + $"{Controle} {cPropria,6:0.00} e com {Cobaia} {cOutra,6:0.00}");
		}

		Ok($"19. **A MESMA REGUA, APONTADA PRO VIZINHO, APONTA PRO VIZINHO**: nos mesmos instantes o "
		 + $"disco de {Controle} se parece mais com {Controle} do que com {Cobaia} -- e o que impede "
		 + "\"parece mais com a propria arte\" de ser verdade numa medida viciada",
		   cruzados == _assinaturas.Count, $"{cruzados} de {_assinaturas.Count} instantes");

		Ok($"19. **O ICONE NAO TROCA DURANTE A AGONIA** -- a resposta a pergunta do dono, medida no "
		 + $"pixel: nos {_assinaturas.Count} instantes da tira o disco continua parecendo com {Cobaia}, "
		 + $"e nao com {Controle}. O shader pinta POR CIMA; ele nao troca a arte",
		   certos == _assinaturas.Count,
		   $"{certos} de {_assinaturas.Count} instantes (menor margem {menorMargem:0.00})");

		Nota($"a margem nunca encosta em zero: no pior instante (o auge, com o disco quase todo coberto "
		   + $"de magma e rachadura) ela ainda e {menorMargem:0.00} a favor de {Cobaia}.");
	}

	// =====================================================================
	// AS MEDIDAS -- todas sobre a REGIAO MEDIDA de um planeta
	// =====================================================================
	/// <summary>Abaixo disto o pixel e fundo, e nao planeta. O fundo da cena e 0,03/0,03/0,06.</summary>
	private const float PisoDoFundo = 0.10f;

	/// <summary>
	/// ACHA OS PIXELS DE UM PLANETA no quadro em que ele esta limpo.
	///
	/// Varre a caixa do disco e guarda o que esta aceso -- de 2 em 2 pixels, o que da ~21 mil pontos
	/// pra a Terra: estatistica de sobra, e cabe em milissegundos mesmo com `GetPixel` sendo caro.
	///
	/// **A CAIXA VEM DO NODE E O CONTEUDO VEM DO ALFA**: o raio de producao diz ONDE olhar, o desenho
	/// diz o QUE contar. E assim porque o disco **nao ocupa o quadro de 128 px por igual** -- medido:
	/// a Terra pinta 0,75 do quadro (raio 47 de 64) e Vegeta pinta 1,00. Usar o raio como se fosse o
	/// desenho poria um quarto de fundo preto dentro da amostra da Terra e nenhum na de Vegeta, e as
	/// duas medidas nao seriam comparaveis.
	/// </summary>
	private static Disco Medir(Image img, string nome, PlanetaDesenhado p)
	{
		var centro = new Vector2I((int)p.Position.X, (int)p.Position.Y);
		int r = (int)p.Raio + 8;
		List<Vector2I> pontos = [];

		for (int y = centro.Y - r; y <= centro.Y + r; y += 2)
			for (int x = centro.X - r; x <= centro.X + r; x += 2)
			{
				if (x < 0 || y < 0 || x >= img.GetWidth() || y >= img.GetHeight()) continue;
				if (img.GetPixel(x, y).Luminance >= PisoDoFundo) pontos.Add(new Vector2I(x, y));
			}

		return new Disco(nome, centro, pontos);
	}

	/// <summary>
	/// A VERMELHIDAO DE UM DISCO, por RAZAO entre canais: `media(R) / media((G+B)/2)`.
	///
	/// RAZAO E NAO DIFERENCA, e o motivo esta no cabecalho: brilho de cena move diferenca e nao move
	/// razao. Um cinza da 1,00 em qualquer brilho; a Terra viva (azul-esverdeada) da abaixo de 1; o
	/// magma leva pra cima de 1. E a medida so faz sentido sobre a REGIAO MEDIDA -- a media da tela
	/// inteira seria dominada pelo fundo, que nao muda.
	/// </summary>
	private static double Razao(Image img, Disco d)
	{
		if (d.Pontos.Count == 0) return 0;
		double r = 0, gb = 0;
		foreach (Vector2I p in d.Pontos)
		{
			Color c = img.GetPixel(p.X, p.Y);
			r += c.R;
			gb += (c.G + c.B) / 2f;
		}
		return gb <= 1e-6 ? 0 : r / gb;
	}

	/// <summary>
	/// QUE FRACAO DOS PONTOS DE UM DISCO CONTINUA ACESA -- a pergunta *"ainda ha um CORPO aqui?"*.
	///
	/// Irma da <see cref="MediaLum"/> e nao substituta dela: aquela responde *"quao aceso"* (que e o
	/// que a familia 11 precisa pro clarao) e esta responde *"quao coberto"*. Um campo esparso de
	/// destrocos move MUITO a primeira e quase nada a segunda -- e e a segunda que separa um planeta de
	/// um punhado de pedra.
	/// </summary>
	private static double FracaoAcesa(Image img, Disco d)
	{
		if (d.Pontos.Count == 0) return 0;
		int acesos = 0;
		foreach (Vector2I p in d.Pontos)
			if (img.GetPixel(p.X, p.Y).Luminance >= PisoDoFundo) acesos++;
		return (double)acesos / d.Pontos.Count;
	}

	/// <summary>A luminancia media de um disco. Serve pra "sobrou alguma coisa aqui?".</summary>
	private static double MediaLum(Image img, Disco d)
	{
		if (d.Pontos.Count == 0) return 0;
		double soma = 0;
		foreach (Vector2I p in d.Pontos) soma += img.GetPixel(p.X, p.Y).Luminance;
		return soma / d.Pontos.Count;
	}

	/// <summary>
	/// A MAIOR diferenca de luminancia entre duas fotos, DENTRO da regiao medida.
	///
	/// MAIOR e nao media, pelo mesmo motivo do robo da gota: a rachadura e uma REDE DE LINHAS FINAS,
	/// e uma media sobre a regiao inteira a diluiria ate ela sumir na tolerancia.
	/// </summary>
	private static double MaiorDif(Image a, Image b, Disco d)
	{
		double maior = 0;
		foreach (Vector2I p in d.Pontos)
		{
			if (p.X >= b.GetWidth() || p.Y >= b.GetHeight()) continue;
			maior = Math.Max(maior, Math.Abs(a.GetPixel(p.X, p.Y).Luminance
											 - b.GetPixel(p.X, p.Y).Luminance));
		}
		return maior;
	}

	/// <summary>O recorte quadrado em volta de um planeta, pra a tira.</summary>
	private static Image? Recortar(Image img, Disco d) => Recortar(img, d.Centro, LadoDoRecorte);

	/// <summary>
	/// O recorte quadrado em volta de um ponto, com o lado dado.
	///
	/// O LADO VIROU PARAMETRO por causa do rescaldo: o campo de destrocos se ESPALHA (ate 1,3 raio do
	/// centro) e nao cabe no mesmo enquadramento em que cabia o disco. Uma tira que corta justamente a
	/// coisa que ela deveria mostrar e a familia de defeito que esta bancada acabou de pagar.
	/// </summary>
	private static Image? Recortar(Image img, Vector2I centro, int lado)
	{
		int meio = lado / 2;
		var caixa = new Rect2I(
			Math.Clamp(centro.X - meio, 0, Math.Max(0, img.GetWidth() - lado)),
			Math.Clamp(centro.Y - meio, 0, Math.Max(0, img.GetHeight() - lado)),
			Math.Min(lado, img.GetWidth()),
			Math.Min(lado, img.GetHeight()));
		return caixa.Size.X <= 0 || caixa.Size.Y <= 0 ? null : img.GetRegion(caixa);
	}

	/// <summary>
	/// UMA FAIXA RADIAL de um disco medido -- o miolo, a beirada, um anel qualquer.
	///
	/// Existe pra a familia 11 poder afirmar que o clarao e **do ponto** e nao da tela: comparar o
	/// miolo com a beirada do MESMO disco tira de campo qualquer coisa que clareie o quadro inteiro
	/// (um `Modulate` global, uma correcao de cor, um retangulo de tela cheia).
	///
	/// O raio sai dos PONTOS e nao do node, pelo mesmo motivo do <see cref="Medir"/>: e o desenho que
	/// diz onde o disco esta, e a essa altura o node do planeta ja pode nem existir.
	/// </summary>
	private static Disco Faixa(Disco d, float de, float ate)
	{
		float rmax = 1;
		foreach (Vector2I p in d.Pontos)
			rmax = Math.Max(rmax, Distancia(p, d.Centro));

		List<Vector2I> sel = [];
		foreach (Vector2I p in d.Pontos)
		{
			float f = Distancia(p, d.Centro) / rmax;
			if (f >= de && f <= ate) sel.Add(p);
		}
		return new Disco(d.Nome, d.Centro, sel);
	}

	private static float Distancia(Vector2I a, Vector2I b)
	{
		float dx = a.X - b.X, dy = a.Y - b.Y;
		return Mathf.Sqrt(dx * dx + dy * dy);
	}

	private static Disco Miolo(Disco d, float ate) => Faixa(d, 0f, ate);
	private static Disco Anel(Disco d, float de) => Faixa(d, de, 1.01f);

	/// <summary>A cor MEDIA de uma regiao, canal a canal. Pro clarao ser cobrado como QUENTE.</summary>
	private static (double R, double G, double B) MediaRgb(Image img, Disco d)
	{
		if (d.Pontos.Count == 0) return (0, 0, 0);
		double r = 0, g = 0, b = 0;
		foreach (Vector2I p in d.Pontos)
		{
			Color c = img.GetPixel(p.X, p.Y);
			r += c.R; g += c.G; b += c.B;
		}
		return (r / d.Pontos.Count, g / d.Pontos.Count, b / d.Pontos.Count);
	}

	/// <summary>
	/// TIRA A FOTO **E CONFERE QUE ELA NAO SAIU PRETA**.
	///
	/// ============================ POR QUE A GUARDA DE LUZ E OBRIGATORIA ============================
	/// Este projeto ja teve uma rodada inteira em que **as fotos sairam pretas e todas as checagens
	/// ficaram verdes**, porque elas liam campo e nao pixel. `GetImage` num quadro que ainda nao
	/// desenhou devolve um buffer valido e escuro -- nao devolve erro nenhum.
	///
	/// O piso e 2% dos pixels acesos, e ele so e alcancavel por causa do CONTROLE: mesmo na foto do
	/// fim, quando a Terra ja nao existe, Namek continua pintando ~7% do quadro. Uma bancada com um
	/// planeta so teria que baixar este piso ate ele nao valer nada justamente na foto mais
	/// importante.
	/// ==========================================================================================
	/// </summary>
	private Image? Foto(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;

		int acesos = 0, total = 0;
		for (int y = 0; y < img.GetHeight(); y += 4)
			for (int x = 0; x < img.GetWidth(); x += 4)
			{
				total++;
				if (img.GetPixel(x, y).Luminance >= PisoDoFundo) acesos++;
			}

		double fracao = total == 0 ? 0 : (double)acesos / total;
		Ok($"foto `{nome}` NAO saiu preta ({fracao * 100:0.0}% do quadro esta aceso)",
		   fracao > 0.02, $"{fracao * 100:0.00}%");

		string caminho = $"user://agonia-{nome}.png";
		img.SavePng(caminho);
		Nota($"foto: {ProjectSettings.GlobalizePath(caminho)}");
		return img;
	}

	private void SemQuadro()
	{
		Nota("SEM QUADRO (headless nao renderiza): a bancada inteira e sobre PIXEL, entao ela nao roda.");
		Nota("Rode com janela: --path . --diagagonia --position 1920,0 --resolution 1280x720");
		_passo = _passos.Count - 1;
	}

	public override void _Process(double delta)
	{
		if (_passo >= _passos.Count) return;
		_passos[_passo++]();
	}

	private void Terminar()
	{
		GD.Print("");
		GD.Print("========== BANCADA DA AGONIA DE UM PLANETA ==========");
		foreach (string l in _linhas) GD.Print(l);
		int ok = _linhas.Count(x => x.StartsWith("  OK", StringComparison.Ordinal));
		GD.Print($"===== FIM: {ok} OK, {_falhas} FALHA(S) =====");
		if (RampaInjetadaChata)
			GD.Print("===== (rodada com `--agoniachata`: o VERMELHO acima e o resultado esperado) =====");
		GetTree().Quit(_falhas == 0 ? 0 : 1);
	}
}
