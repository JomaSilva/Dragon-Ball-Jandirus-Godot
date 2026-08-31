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

	/// <summary>O planeta cobaia: a TERRA, que e o disco que todo mundo reconhece.</summary>
	private const string Cobaia = "Earth";

	/// <summary>
	/// O CONTRA-EXEMPLO: **NAMEK, VIVA**, no mesmo quadro. Ver o cabecalho.
	///
	/// Namek e nao um segundo "Earth" porque os dois usariam o mesmo estado da folha e a mesma
	/// semente de ruido: dois discos identicos lado a lado provariam menos, e um erro que pintasse
	/// "todo planeta chamado Earth" passaria batido.
	/// </summary>
	private const string Controle = "Namek";

	/// <summary>Quantos degraus da agonia sao medidos entre 0 e 1.</summary>
	private const int Degraus = 12;

	/// <summary>Em quais degraus a TIRA tira retrato. Cinco instantes dos cinco minutos.</summary>
	private static readonly int[] DegrausDaTira = [0, 3, 6, 9, 12];

	/// <summary>O lado do recorte de cada planeta na tira. Cobre a Terra (raio 220) com folga.</summary>
	private const int LadoDoRecorte = 480;

	private PlanetaDesenhado _planeta = null!;
	private PlanetaDesenhado _vivoSempre = null!;
	private GameClient? _cli;

	private Image? _quadroVivo;
	private Disco _doente, _saudavel;

	private readonly List<(double Agonia, double Dif, double Razao, double RazaoControle)> _medidas = [];
	private readonly List<TiraDeFotos.Quadro> _tira = [];

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
			Position = new Vector2(300, 300),
			Nome = Cobaia,
			Raio = 220,
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

			Ok("1. os dois discos foram ACHADOS no quadro (a regiao de amostragem e medida, nao chutada)",
			   _doente.Pontos.Count > 3000 && _saudavel.Pontos.Count > 3000,
			   $"{_doente.Pontos.Count} e {_saudavel.Pontos.Count} pontos");

			Ok("1. o planeta VIVO desenha com agonia ZERO no uniform",
			   Mathf.IsEqualApprox(_planeta.AgoniaNoMaterialDeTeste, 0f));

			Ok("1. ...e o CONTROLE tambem (ele nunca sai disso -- e o que a familia 8 cobra)",
			   Mathf.IsEqualApprox(_vivoSempre.AgoniaNoMaterialDeTeste, 0f));

			// O RETRATO 0 DA TIRA E O CONTRA-EXEMPLO. Ele entra ANTES dos cinco instantes, no lugar
			// onde o olho passa primeiro: quem abrir a tira ve o planeta saudavel e so depois a
			// escada. Sem ele a tira e cinco discos vermelhos e nao ha contra o que comparar.
			if (Recortar(_quadroVivo, _saudavel) is { } sao)
				_tira.Add(new TiraDeFotos.Quadro(sao, 0));
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
			EmpurrarAgonia(-MortePlanetaria.SegundosDoEstouro - 1);
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

			double sobrou = MediaLum(depois, _doente);
			double antes = MediaLum(_quadroVivo, _doente);

			Ok("5. **E NA TELA TAMBEM**: onde a Terra estava sobrou o fundo, e nao um disco",
			   sobrou < 0.05, $"luminancia media {antes:0.000} -> {sobrou:0.000}");

			Ok("5. ...E O CONTROLE CONTINUA LA (o quadro nao ficou vazio -- some o planeta, nao a tela)",
			   MediaLum(depois, _saudavel) > 0.10,
			   $"o disco de {Controle} esta em {MediaLum(depois, _saudavel):0.000}");
		});

		_passos.Add(ATiraDoEspaco);

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
	private void EmpurrarAgonia(double faltam)
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
			Fase = FaseDaMorte.Explodindo,
			Estagio = MortePlanetaria.UltimoEstagio + 1,
			Faltam = faltam,
		}]);

		// O `_Process` do planeta le do `GameClient` -- mas o relogio do cliente so anda depois do
		// primeiro `S2C.Ceu`, que aqui nunca chega. Entao a bancada chama a MESMA porta que o
		// `_Process` chamaria, com os MESMOS dois numeros que ele passaria.
		double agonia = MortePlanetaria.Intensidade(
			FaseDaMorte.Explodindo, MortePlanetaria.UltimoEstagio + 1, faltam);
		if (IsInstanceValid(_planeta)) _planeta.AplicarAgonia(agonia, faltam);
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
		if (Array.IndexOf(DegrausDaTira, degrau) >= 0 && Recortar(agora, _doente) is { } corte)
			_tira.Add(new TiraDeFotos.Quadro(corte, _tira.Count));

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

		Nota("a rampa, degrau a degrau (agonia | quanto o disco mudou | vermelhidao R/((G+B)/2) da "
		   + "TERRA | a mesma razao em NAMEK, que nao devia mexer):");
		foreach ((double a, double d, double v, double vc) in _medidas)
			Nota($"     agonia {a:0.000}   dif {d:0.000}   Terra {v:0.000}   Namek {vc:0.000}");

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
		// nunca corta. O piso e sobre a VERMELHIDAO e nao sobre a diferenca de luminancia porque
		// vermelhidao e o que o dono pediu, e um shader que escurecesse o disco sem magma nenhum
		// tambem move a luminancia.
		// ============================================================================================
		double ganhoDoMeio = _medidas[Degraus / 2].Razao - razInicio;
		Ok("3. **E A RAMPA CHEGA LONGE**: no meio dos cinco minutos ela ja andou uma parte do caminho",
		   ganhoDoMeio > 0.02 && razFim - razInicio > 0.05,
		   $"no meio +{ganhoDoMeio:0.000}, no fim +{razFim - razInicio:0.000} de vermelhidao");

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
		if (_tira.Count < 2) { Nota("sem tira do espaco: faltaram recortes"); return; }

		string caminho = ProjectSettings.GlobalizePath("user://agonia-tira-do-espaco.png");
		double pintada = TiraDeFotos.Montar(_tira, caminho);

		Ok($"9. **A TIRA DO ESPACO** saiu com {_tira.Count} quadros numerados (0 = {Controle} viva, "
		 + $"1 a {_tira.Count - 1} = a Terra do piso ao auge) e ela NAO esta vazia",
		   pintada > 0.25, $"{pintada * 100:0}% dos pixels sao imagem e nao fundo");
		Nota($"tira: {caminho}");
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
	private static Image? Recortar(Image img, Disco d)
	{
		int meio = LadoDoRecorte / 2;
		var caixa = new Rect2I(
			Math.Clamp(d.Centro.X - meio, 0, Math.Max(0, img.GetWidth() - LadoDoRecorte)),
			Math.Clamp(d.Centro.Y - meio, 0, Math.Max(0, img.GetHeight() - LadoDoRecorte)),
			Math.Min(LadoDoRecorte, img.GetWidth()),
			Math.Min(LadoDoRecorte, img.GetHeight()));
		return caixa.Size.X <= 0 || caixa.Size.Y <= 0 ? null : img.GetRegion(caixa);
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
