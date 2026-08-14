using System.Collections;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DA TECLA E NAS NAVES (`--diagembarque`) ============================
/// O pedido do dono, inteiro: *"na aba NAV vc colocou varios verbs, a MAIORIA deles nem eram pra ser
/// verbs do menu, e sim INTERACAO com as naves (ao apertar E perto delas, assim como e com a arvore
/// de maca e a research table) abrindo um menu dela, e ai vai ter a opcao de entrar ou sair etc. e
/// vai ter a SALA DO COMPUTADOR PRINCIPAL da capital ship, q la ao apertar E vai ter as opcoes de
/// pilotar, observar etc, e ao apertar E DNV vc volta pra dentro da nave"*.
///
/// Isso e um GESTO, e um gesto se prova andando. Esta bancada faz o percurso inteiro num jogo de
/// verdade: fabrica a nave pela tela do jogador, anda ate ela, aperta E, embarca, atravessa a sala,
/// acha a ponte, pilota, aperta E de novo pra voltar, desembarca -- e conta quantos passos deram
/// certo.
/// ========================================================================================================
///
/// ============================ ELA NAO LE A TABELA DE INTERACOES. NENHUMA VEZ ============================
/// `Interacoes.De("Capital_Ship")` tem cinco linhas, e afirmar isso e a coisa mais facil e mais inutil
/// que uma bancada deste assunto pode fazer. O arquivo `dbclimax-port-bancada-mede-intencao` conta o
/// preco dessa facilidade: **quatro defeitos visuais passaram por mais de quatro mil checagens verdes**,
/// e a causa foi sempre a mesma -- a bancada media a INTENCAO (o valor que o codigo mandou pro widget)
/// e o defeito morava no widget.
///
/// Aqui, entao, tudo o que se afirma sai de um node que estava na tela:
///
///   * "o menu abriu"        -> `MenuDeInteracao.NaTela` (o `Visible` do painel);
///   * "tem a opcao Embarcar"-> `BotoesDesenhados()` (o `Text` dos `Button` que estavam nele);
///   * "eu apertei"          -> `ApertarDesenhado()`, que EMITE o sinal `Pressed` do botao -- e nao
///                              chama o tratador por dentro, senao a bancada pularia a ligacao que
///                              pode estar faltando;
///   * "o jogo me disse"     -> o evento `GameClient.Falou`, ou seja o pacote de chat que CHEGOU;
///   * "a tecla E"           -> um `InputEventKey` empurrado no viewport, que passa pelo `Foco`, pelo
///                              registro de teclas religaveis e pelo `_UnhandledInput` de verdade.
///
/// E o corpo ANDA. Nada de teletransportar pra perto: `Input.ActionPress("move_right")`, os mesmos
/// quatro botoes do jogador, ate a distancia cair -- que e a unica forma de medir onde o menu comeca
/// a responder sem perguntar isso pra a constante que ele proprio usa.
/// ====================================================================================================
///
/// ============================ AS SEIS FAMILIAS, E COMO CADA UMA REPROVA ============================
///   F1  O CICLO INTEIRO PELO E. Reprova se qualquer elo nao acontecer: o menu nao abrir perto da
///       nave, faltar o botao, apertar nao mudar de zona, o console nao responder na ponte, o leme
///       nao oferecer a volta, ou a volta nao devolver o corpo pra dentro.
///   F2  LONGE, O E NAO ABRE NADA. Reprova se o menu (ou a dica "[E] ...") aparecer sem alvo por
///       perto -- e o contra-exemplo sem o qual "o menu existe" ficaria verde com ele abrindo de
///       qualquer lugar. Inclui a BORDA medida na caminhada: o ultimo passo que NAO abriu tem que
///       estar alem do alcance do Core, e o primeiro que abriu, dentro dele.
///   F3  AS RECUSAS CONTINUAM, PALAVRA POR PALAVRA. Reprova se a frase mudar, se a recusa deixar de
///       sair, ou se o menu deixar de OFERECER o botao recusado (esconder botao nunca foi permissao
///       nesta casa: quem recusa e o servidor, e com o motivo).
///   F4  O QUE SAIU DA ABA NAV SUMIU. Reprova varrendo: se sobrar um botao com palavra de nave em
///       QUALQUER aba, ou se apertar todos os botoes da aba Nav fizer sair um verbo `nave_*`.
///   F5  O QUE FICOU NA ABA CONTINUA FUNCIONANDO. Reprova se a carta sumir, se o zoom nao mudar o
///       enquadramento, ou se as legendas que desceram pra debaixo dela nao estiverem escritas.
///   F6  O TECLADO CABE NO QUE A ACAO PROMETE. Reprova se o numero digitado no teclado DESENHADO nao
///       chegar ao `Max` que a acao declarou -- que era o caso da senha da nave (6 digitos prometidos,
///       4 aceitos, sem uma linha de aviso).
///
/// E DEPOIS DA RODADA REAL VEM A DE INJECAO: cada regra acima e chamada de novo, com uma amostra
/// deliberadamente estragada, e EXIGE-SE QUE ELA FIQUE VERMELHA. Regra que nao reprova o proprio
/// defeito que existe pra pegar e uma linha verde que nao significa nada.
/// ==============================================================================================
///
/// ============================ E A INJECAO NAO FOI SO DE AMOSTRA: FOI NO CODIGO ============================
/// A rodada interna prova que a REGRA sabe reprovar. Ela nao prova que a regra esta LIGADA no lugar
/// certo do percurso -- pra isso o defeito tem que estar no jogo, e esteve. Duas rodadas com defeito de
/// verdade compilado, em familias que nao se atrapalham:
///
///   RODADA A (o alcance do menu virou 3x o do Core / a frase da tranca reescrita / o teclado de volta
///   aos 4 algarismos): vermelho em **F2.1, F2.2 e F2.3** ("abriu a 214 px (alcance 64)") e em **F6.2**
///   ("visor mostrou 2718, pedi 271828"). O F3.2 reprovou junto dizendo *"não há nenhuma Capital Ship
///   por perto"* -- que e o defeito da banda morta se mostrando por inteiro: o menu abre, o jogador
///   aperta, e o servidor recusa por distancia. Exatamente o modo de falha que este gesto existe pra
///   impedir.
///
///   RODADA B ("Voltar à ponte" deixa de ser a primeira opcao do leme / um botao "Lançar" volta pra aba
///   Nav mandando `nave_lancar` / o "+" da carta vira botao morto / a frase da tranca reescrita):
///   **56 OK, 6 FALHA** -- vermelho em **F1.10** ("Lançar | Voltar à ponte | ..."), **F4.2** (pelo
///   rotulo), **F4.4** (pelo VERBO, que e a prova que nao depende de rotulo), **F5.2 e F5.3**, e
///   **F3.2** ("Capital Ship nao abre."). As outras 56 continuaram verdes: ela nao reprova por contagio.
/// ======================================================================================================
///
/// ============================ UM AVISO PRA QUEM FOR RODAR ISTO ============================
/// As naves vivem no `naves.json`, que sobrevive ao processo. Uma rodada interrompida no meio deixa uma
/// Capital Ship de pe no mesmo pedaco de Terra, e a proxima tropeca nela: o `posicionar` recusa o ponto
/// ("ja tem coisa demais"), o `Embarcar` do servidor pega a nave MAIS PERTO (que pode ser a de ontem) e
/// a medida do alcance vira a distancia ate o entulho.
///
/// A bancada se defende do que da: exige um id de nave NOVO, tenta os quatro lados, anda na direcao da
/// PROPRIA nave e diz em voz alta quando havia coisa interativa mais perto. O que ela nao pode e
/// recolher nave de outra conta. Se as linhas de F0 e F2 comecarem a reprovar em sequencia, o lugar de
/// olhar e o `naves.json` -- e o conserto e apagar as Capital Ship das contas de bancada.
/// ==========================================================================================
///
/// COMO RODAR (porta propria, conta NOVA, sem janela):
///     Godot --headless --path . --host --rede 7995 --embarqueteste --diagembarque
///           --conta bancemb --nome BancEmb
/// </summary>
public partial class RoboDeEmbarque : Node
{
	private static GameClient? C => GameClient.Instance;
	private static MenuDeInteracao? E => MenuDeInteracao.Instancia;

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private int _ok, _falhou;
	private readonly List<string> _vermelhas = [];

	private void Checa(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"  ok    {nome}"); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"  FALHA {nome}   {detalhe}"); }
	}

	private static void Nota(string linha) => GD.Print("[embarque] " + linha);

	// =====================================================================
	// AS REGRAS -- funcoes puras, pra a rodada de injecao poder chamar as MESMAS
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE AS REGRAS SAO FUNCOES, E NAO `if` SOLTOS ============================
	/// Uma checagem escrita direto no percurso (`Checa("tem Embarcar", botoes.Contains("Embarcar"))`) so
	/// pode ser exercitada pelo percurso -- ou seja, so da pra saber se ela sabe reprovar quando o jogo
	/// quebrar de verdade. Sendo funcao, a rodada de injecao no fim do arquivo chama exatamente a mesma
	/// linha com uma amostra estragada e cobra o vermelho.
	/// ======================================================================================================
	/// </summary>
	private static bool TemBotao(IReadOnlyList<string> desenhados, string rotulo) =>
		desenhados.Contains(rotulo);

	private static bool PrimeiroBotaoE(IReadOnlyList<string> desenhados, string rotulo) =>
		desenhados.Count > 0 && desenhados[0] == rotulo;

	/// <summary>O alcance e o do CORE, e a bancada nunca escreve 64 na mao.</summary>
	private static bool DentroDoAlcance(float distancia) => distancia <= Interacoes.Alcance;

	private static bool AlemDoAlcance(float distancia) => distancia > Interacoes.Alcance;

	/// <summary>O jogo me disse ISTO? Compara o trecho fixado, e nao "alguma coisa foi dita".</summary>
	private static bool Disse(IReadOnlyList<string> falas, string trecho) =>
		falas.Any(f => f.Contains(trecho, StringComparison.Ordinal));

	/// <summary>
	/// AS PALAVRAS QUE NAO PODEM SOBRAR EM BOTAO NENHUM DO MENU P.
	///
	/// Sao as dos oito botoes deletados (entrar/sair, lancar, melhorar, ver estado, largar o leme,
	/// observar, desembarcar, recondicionar), reduzidas ao radical pra pegar tambem uma redacao nova.
	/// "observar" e "ver estado" ficaram DE FORA da lista de proposito: sao palavras genericas demais,
	/// e um botao chamado "Observar" noutro sistema faria esta varredura reprovar o jogo inteiro. O que
	/// prende esses dois e a varredura por VERBO, que e exata.
	/// </summary>
	private static readonly string[] PalavrasDeNave =
		["nave", "leme", "desembarc", "recondicion", "lançar", "lancar", "pilotar", "embarc"];

	private static bool SemPalavraDeNave(IEnumerable<string> rotulos) =>
		!rotulos.Any(r => PalavrasDeNave.Any(p =>
			r.Contains(p, StringComparison.OrdinalIgnoreCase)));

	private static bool SemVerboDeNave(IEnumerable<string> verbos) =>
		!verbos.Any(v => v.StartsWith("nave_", StringComparison.Ordinal));

	/// <summary>
	/// MUDOU? -- por RAZAO, e nao por diferenca absoluta.
	///
	/// ============================ A ESCALA DA CARTA E UM NUMERO MINUSCULO ============================
	/// O zoom da carta estelar vive na casa de 1e-4 (o teto e 0,15 e o piso e 1/40000). Um corte
	/// absoluto de 1e-4 -- que parece apertado -- e MAIOR que o proprio numero: aproximar 1,6x levava
	/// 0,0001 pra 0,00016, e a regra dizia "nao mudou". A primeira rodada limpa desta bancada reprovou
	/// o "+" com o "+" funcionando, que e o pior tipo de vermelho: o que faz consertar o que nao esta
	/// quebrado.
	/// ============================================================================================
	/// </summary>
	private static bool Mudou(double antes, double depois) =>
		Math.Abs(antes - depois) > Math.Abs(antes) * 0.001 + 1e-12;

	private static bool ZonaMudou(ZoneKey antes, ZoneKey depois) => !antes.Equals(depois);

	/// <summary>O visor do teclado chegou no numero pedido? (o defeito dos quatro algarismos).</summary>
	private static bool CabeNoTeclado(string visor, double pedido) =>
		double.TryParse(visor, out double v) && Math.Abs(v - pedido) < 0.5;

	// =====================================================================
	// O MOTOR DO ROTEIRO
	// =====================================================================
	private IEnumerator? _roteiro;
	private double _espera = 2.5;   // deixa o mundo nascer antes do primeiro passo
	private bool _acabou;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } || World.Instancia?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext()) { Fim(); return; }
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fim()
	{
		_acabou = true;
		SoltarTudo();
		GD.Print($"\n[embarque] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[embarque] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}

	// =====================================================================
	// AS FERRAMENTAS DO CORPO
	// =====================================================================
	private static Vector2 Eu() => World.Instancia?.PosicaoLocal ?? Vector2.Zero;

	private static void SoltarTudo()
	{
		foreach (string a in new[] { "move_up", "move_down", "move_left", "move_right" })
			Input.ActionRelease(a);
	}

	/// <summary>
	/// A TECLA E DE VERDADE: um evento de teclado empurrado no viewport.
	///
	/// NAO chama `Abrir()`: o caminho que interessa passa pelo `Foco.Digitando`, pelo
	/// `Teclas.Bate("ui_interagir", ...)` (a tecla e religavel) e pelo `_UnhandledInput`. Um atalho
	/// aqui deixaria de fora justamente a parte que pode ter sido desligada.
	/// </summary>
	private void ApertarE()
	{
		var ev = new InputEventKey { PhysicalKeycode = Key.E, Keycode = Key.E, Pressed = true };
		GetViewport().PushInput(ev);
	}

	/// <summary>Fecha o menu se ele estiver aberto -- pelo mesmo E, que alterna.</summary>
	private void FecharMenu()
	{
		if (E is { NaTela: true }) ApertarE();
	}

	/// <summary>
	/// APERTA UM BOTAO DO MENU, e REPROVA se ele nao estava la.
	///
	/// ============================ POR QUE ISTO NAO PODE SER SILENCIOSO ============================
	/// A primeira rodada desta bancada pediu "Melhorar velocidade" no menu da nave POUSADA, onde essa
	/// acao nao existe (ela e do console). O `ApertarDesenhado` devolveu falso, ninguem olhou, e o
	/// menu ficou ABERTO -- entao o passo seguinte, que apertava E pra abrir, na verdade FECHOU, e a
	/// checagem dele reprovou por um motivo que nao tinha nada a ver com o que ela mede.
	///
	/// Um botao que nao foi encontrado e sempre uma falha, e tem que ser dita na hora e no lugar.
	/// =========================================================================================
	/// </summary>
	private void Apertar(string rotulo)
	{
		if (E?.ApertarDesenhado(rotulo) == true) return;
		Checa($"(apertar \"{rotulo}\")", false,
			  E is { NaTela: true } ? $"o menu tem: {string.Join(" | ", E.BotoesDesenhados())}"
									: "o menu nem estava aberto");
	}

	/// <summary>A distancia do jeito que o menu mede: caixa por eixo, e nao raio.</summary>
	private static float DistanciaDeMenu(Vector2 a, Vector2 b) =>
		Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

	private static double Agora => Time.GetTicksMsec() / 1000.0;

	/// <summary>
	/// ANDA ATE UM PONTO, um eixo de cada vez.
	///
	/// UM EIXO DE CADA VEZ porque as paredes desta planta sao retas: com os dois eixos juntos o corpo
	/// encosta na diagonal de uma quina e para, e a bancada acusaria "nao cheguei" onde o jogador
	/// contornaria sem pensar. Os pontos de passagem (ver o percurso da ponte) sao escolhidos pra que
	/// cada perna seja uma reta livre.
	///
	/// E ELA DESISTE ALTO: parada por mais de tres segundos ou estourado o prazo, ela solta as teclas
	/// e devolve `false` -- quem chamou reprova a linha com a distancia que sobrou, em vez de a
	/// bancada travar pra sempre esperando um corpo preso.
	/// </summary>
	/// <param name="tolerancia">
	/// QUANTO DE DESALINHO O EIXO TRAVESSO PODE TER antes de o corpo avancar no outro.
	///
	/// ============================ O VAO DA PONTE TEM UM TILE, E O CORPO TEM DEZESSEIS PIXELS ============================
	/// Com os 10 px de folga que esta funcao usava, o corpo chegava a coluna do vao com 9 px de
	/// desalinho -- dentro da tolerancia, entao ele parava de corrigir X e passava a andar pro norte.
	/// So que a caixa dos pes tem `BodyHalfW = 8`: 9 px fora do centro poem a quina dentro da celula
	/// vizinha, que ali e PAREDE. Resultado na rodada 4: "parei sem chegar -- faltavam (-9,-301) px",
	/// e as sete checagens da ponte reprovaram por um corpo entalado, e nao por defeito nenhum.
	///
	/// Nas pernas que atravessam o vao a tolerancia e 4 px, que cabe nos 8 de meia-largura com folga.
	/// ==============================================================================================================
	/// </param>
	private IEnumerable AndarAte(Vector2 alvo, double limiteSeg, float tolerancia = 10)
	{
		double t0 = Agora, tMexeu = Agora;
		Vector2 ultima = Eu();

		while (Agora - t0 < limiteSeg)
		{
			Vector2 eu = Eu();

			Vector2 d = alvo - eu;
			if (Math.Abs(d.X) < tolerancia && Math.Abs(d.Y) < tolerancia) break;

			if ((eu - ultima).Length() > 2) { ultima = eu; tMexeu = Agora; }
			else if (Agora - tMexeu > 3) { Nota($"parei sem chegar -- faltavam ({d.X:0},{d.Y:0}) px"); break; }

			SoltarTudo();
			if (Math.Abs(d.X) >= tolerancia) Input.ActionPress(d.X > 0 ? "move_right" : "move_left");
			else Input.ActionPress(d.Y > 0 ? "move_down" : "move_up");

			yield return 0.0;
		}
		SoltarTudo();
	}

	/// <summary>Espera uma condicao ficar verdadeira, com prazo. Devolve o controle a cada quadro.</summary>
	private IEnumerable Ate(Func<bool> cond, double limiteSeg)
	{
		double t0 = Agora;
		while (!cond() && Agora - t0 < limiteSeg) yield return 0.0;
	}

	private static Vector2 Pixel((int X, int Y) cel)
	{
		Vec2 v = NaveGrande.PixelDe(cel);
		return new Vector2(v.X, v.Y);
	}

	// =====================================================================
	// O QUE O JOGO ME DISSE
	// =====================================================================
	private readonly List<string> _falas = [];

	private void Escutar()
	{
		if (C is not { } cli) return;
		// E TUDO VAI PRO LOG. Uma recusa que a bancada nao esperava e a pista mais barata que existe --
		// sem esta linha, "o passo nao aconteceu" nao diz POR QUE, e a primeira rodada desta bancada
		// gastou um ciclo inteiro pra descobrir que a resposta era "longe demais".
		cli.Falou += (_, _, texto) => { _falas.Add(texto); GD.Print("       [jogo] " + texto); };
	}

	/// <summary>Zera a escuta antes de um gesto, pra a frase medida ser a DELE.</summary>
	private List<string> Ouvindo() { _falas.Clear(); return _falas; }

	// =====================================================================
	// AS FRASES FIXADAS -- copiadas do servidor, e e esse o ponto
	// =====================================================================
	/// <summary>
	/// ============================ ELAS SAO O CONTRATO, E POR ISSO ESTAO ESCRITAS AQUI ============================
	/// O pedido foi que as recusas continuem *"com a mesma mensagem de antes"*. Uma bancada que
	/// perguntasse a frase ao proprio servidor (lendo a constante de la) aprovaria qualquer reescrita:
	/// ela compararia o texto novo com ele mesmo.
	///
	/// Entao o trecho fica CRAVADO aqui, copiado de `GameServer.NaveGrande.cs` e `GameServer.Nave.cs`, e
	/// o dia em que alguem reescrever a recusa esta linha cai e diz qual mudou. Sao TRECHOS e nao a frase
	/// inteira porque o nome da nave e do dono entram no meio delas.
	/// =========================================================================================================
	/// </summary>
	private const string TrechoDaTranca = "está trancada. Digite o código em";
	private const string TrechoDoDono = "é de " + GameServerFrases.DonoAlheio;
	/// <summary>
	/// A frase de quem estava DENTRO quando o casco cedeu (`DestruirNave`).
	///
	/// NAO e a do resgate no relog ("a nave em que você estava não existe mais"): aquela e do
	/// `ResgatarDeInteriorMorto`, pra quem deslogou dentro e voltou depois. A bancada pinou a errada
	/// na primeira rodada limpa e reprovou uma frase que estava certa -- as duas contam a mesma
	/// desgraca por caminhos diferentes, e so uma delas passa por quem esta em jogo naquele instante.
	/// </summary>
	private const string TrechoDaExplosao = "EXPLODE. Você é arremessado pra fora.";
	private const string TrechoDoCodigoAnotado = "código anotado. Aperte E na nave";

	/// <summary>Os dois nomes que a fixture do servidor usa. Uma casa so pros dois lados.</summary>
	private static class GameServerFrases
	{
		public const string DonoAlheio = Jandirus.Server.GameServer.DonoAlheioDaBancada;
		public const string Senha = Jandirus.Server.GameServer.SenhaDaBancada;
	}

	// =====================================================================
	// O PERCURSO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		Escutar();
		GameClient cli = C!;

		GD.Print("\n[embarque] ===== A TECLA E NAS NAVES: O PERCURSO =====");
		Nota($"conta '{cli.LocalName}' em {cli.Zone}, alcance do menu = {Interacoes.Alcance:0} px");

		// ---------------------------------------------------------------- F2.1  SEM NADA POR PERTO
		GD.Print("\n-- F2: LONGE, O E NAO ABRE NADA --");
		FecharMenu();
		yield return 0.2;
		ApertarE();
		yield return 0.2;
		Checa("F2.1 sem nada por perto, o E nao abre menu nenhum",
			  E is { NaTela: false }, $"titulo: \"{E?.TituloDesenhado}\"");
		Checa("F2.1b ...e a dica \"[E] ...\" nao esta acesa",
			  string.IsNullOrEmpty(E?.DicaNaTela), $"dica: \"{E?.DicaNaTela}\"");

		// ---------------------------------------------------------------- F0  FABRICAR E ASSENTAR
		// PELA TELA DO JOGADOR, e pelo canal que ela usa. Este passo nao e cerimonia: ele foi o que
		// achou que `tech_construir`/`tech_posicionar` nao existiam do outro lado -- a grade abria, o
		// botao acendia e o clique no chao nao fazia nada. Ver `TelaDeConstrucao.Abrir`.
		GD.Print("\n-- F0: A NAVE CHEGA AO CHAO PELO CAMINHO DO JOGADOR --");

		var verbosDaTela = new List<string>();
		GameClient.EspiaoDeVerbos = (c, _) => { verbosDaTela.Add(c); return true; };
		TelaDeConstrucao.Instancia?.Abrir();
		GameClient.EspiaoDeVerbos = null;
		// E FECHA PELO ESC, que e a unica porta de saida dela (`_UnhandledInput`): deixar a grade
		// aberta poria um painel por cima do resto do percurso.
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = Key.Escape, Keycode = Key.Escape, Pressed = true });
		Checa("F0.1 a grade de construcao nao fala pelo canal de VERBOS (o `tech_*` que ninguem ouvia)",
			  TelaDeConstrucao.Instancia != null
			  && !verbosDaTela.Exists(v => v.StartsWith("tech_", StringComparison.Ordinal)),
			  TelaDeConstrucao.Instancia == null ? "a tela nem existe" : string.Join(",", verbosDaTela));

		// ============================ A NAVE TEM QUE SER NOVA, E NAO "UMA NAVE" ============================
		// Naves ficam no `naves.json`, que sobrevive ao processo. Uma rodada anterior interrompida no meio
		// deixa uma Capital Ship de pe -- e uma bancada que so pergunte "ha uma nave aqui?" da VERDE sobre
		// o entulho da rodada passada, com o assentamento recusado e ninguem sabendo. Aconteceu: a rodada
		// 3 desta bancada passou o F0.2 lendo a nave que a rodada 2 tinha deixado, enquanto o servidor
		// dizia "ja tem coisa demais neste ponto" na linha de cima.
		//
		// Por isso: guarda os ids de ANTES e exige um id que nao estava la. E, como o entulho tambem ocupa
		// o ponto, tenta os quatro lados -- tres tiles e o teto do `AlcanceDePosicionar` (96 px).
		// =============================================================================================
		var idsAntes = new HashSet<int>(cli.Obras.Where(o => o.Tipo == NaveGrande.Tipo).Select(o => o.Id));
		Vector2 ondeNasci = Eu();

		cli.SendTech("construir", NaveGrande.Tipo);
		yield return 0.8;

		foreach (Vector2 lado in new[] { new Vector2(96, 0), new Vector2(0, 96),
										 new Vector2(-96, 0), new Vector2(0, -96) })
		{
			Vector2 ponto = ondeNasci + lado;
			cli.SendTech("posicionar", $"{NaveGrande.Tipo}/{ponto.X:0}/{ponto.Y:0}");
			foreach (object _ in Ate(() => AchaNave(cli, idsAntes) != null, 3)) yield return 0.0;
			if (AchaNave(cli, idsAntes) != null) break;
			Nota($"o ponto {lado} nao serviu -- tentando o proximo lado");
		}

		GameClient.ObraInfo? naveNaTela = AchaNave(cli, idsAntes);
		Checa("F0.2 fabricar e assentar poem uma Capital Ship NOVA no chao, visivel pro cliente",
			  naveNaTela != null,
			  $"{cli.Obras.Count} construcao(oes) na zona, {idsAntes.Count} nave(s) ja estavam la");
		if (naveNaTela is not { } nave) { Nota("sem nave: o resto do percurso nao tem o que medir."); yield break; }

		int idDaNave = -nave.Id;   // as naves viajam com id negativo no pacote de construcoes
		ZoneKey dentro = NaveGrande.ZonaDoInterior(idDaNave);
		// O NOME VIAJA NO PACOTE, e nao sai do catalogo local: o do cliente vem do `Ofertas`, que
		// esconde mobilia de mapa. Sem ele o menu cairia no `tipo.Replace('_',' ')`.
		Checa("F0.3 ...e o NOME dela veio junto no pacote (e nao o typepath cru)",
			  nave.Nome.Length > 0 && nave.Nome != nave.Tipo, $"nome: \"{nave.Nome}\"");

		// ---------------------------------------------------------------- F2.2..F2.4  A BORDA
		// ANDA PRA TRAS ANTES DE APROXIMAR. A nave nasce a tres tiles (o limite do `posicionar`), e
		// medir a borda a partir dai daria dois ou tres passos de amostra. Recuando quatro tiles a
		// aproximacao vira uma caminhada de verdade -- e o "longe demais" fica longe de verdade.
		// ============================ O RUMO SAI DA NAVE, E NAO DE UM EIXO ESCOLHIDO NO CODIGO ============================
		// O ponto de assentamento pode ter sido qualquer um dos quatro lados (entulho ocupa o primeiro),
		// e a rodada 4 desta bancada andou pra LESTE atras de uma nave que estava ao SUL: o menu abriu a
		// 96 px -- da nave VELHA que estava a leste -- e a checagem reprovou o alcance sem ter medido o
		// alcance. Andar na direcao da nave nao e detalhe de conveniencia: e o que faz a distancia medida
		// ser a distancia do alvo.
		// ============================================================================================
		Vector2 rumo = nave.Pos - ondeNasci;
		bool porX = Math.Abs(rumo.X) >= Math.Abs(rumo.Y);
		float sinal = porX ? Math.Sign(rumo.X) : Math.Sign(rumo.Y);
		string acaoIr = porX ? (sinal > 0 ? "move_right" : "move_left")
							 : (sinal > 0 ? "move_down" : "move_up");
		Vector2 recuo = porX ? new Vector2(-sinal * 128, 0) : new Vector2(0, -sinal * 128);

		foreach (object _ in AndarAte(ondeNasci + recuo, 12)) yield return 0.0;

		FecharMenu();
		yield return 0.2;
		ApertarE();
		yield return 0.2;
		float longe = DistanciaDeMenu(Eu(), nave.Pos);
		Checa("F2.2 com a nave a sete tiles, o E ainda nao abre nada",
			  E is { NaTela: false } && AlemDoAlcance(longe), $"a {longe:0} px");

		// A CAMINHADA QUE MEDE A BORDA. A cada quadro: mede, aperta, e para no quadro em que abriu.
		float abriuA = -1, ultimoNaoA = -1;
		bool eraAMinha = false;
		double t0 = Agora;
		while (Agora - t0 < 14)
		{
			float d = DistanciaDeMenu(Eu(), nave.Pos);
			ApertarE();
			if (E is { NaTela: true }) { abriuA = d; eraAMinha = d <= MaisPertoQueMim(cli, nave) + 0.5f; break; }
			ultimoNaoA = d;
			Input.ActionPress(acaoIr);
			yield return 0.0;
		}
		SoltarTudo();

		Checa("F2.3 andando, o menu abre DENTRO do alcance do Core",
			  abriuA >= 0 && DentroDoAlcance(abriuA) && eraAMinha,
			  $"abriu a {abriuA:0} px (alcance {Interacoes.Alcance:0})"
			  + (eraAMinha ? "" : " -- e havia coisa interativa MAIS PERTO: a medida seria de outro alvo"));
		Checa("F2.4 ...e o ultimo passo em que ele NAO abriu estava alem dele (o corte existe)",
			  ultimoNaoA > 0 && AlemDoAlcance(ultimoNaoA), $"ultimo \"nao\" a {ultimoNaoA:0} px");

		// ---------------------------------------------------------------- F1.1..F1.3  O MENU DA NAVE
		GD.Print("\n-- F1: O CICLO INTEIRO PELA TECLA E --");
		List<string> botoes = E?.BotoesDesenhados() ?? [];
		Checa("F1.2 o menu aberto tem o NOME da nave no titulo",
			  (E?.TituloDesenhado ?? "") == nave.Nome, $"titulo: \"{E?.TituloDesenhado}\"");
		Checa("F1.3 ...e o botao de EMBARCAR esta desenhado nele",
			  TemBotao(botoes, "Embarcar"), string.Join(" | ", botoes));

		// A DICA SO EXISTE COM O MENU FECHADO (o menu ja e a resposta -- ver `_Process` de la), entao
		// ela e medida DEPOIS de fechar. Ler as duas no mesmo instante mediria a que nao esta na tela.
		FecharMenu();
		yield return 0.3;
		Checa("F1.1 perto da nave, a dica \"[E] ...\" acende com o nome dela",
			  (E?.DicaNaTela ?? "").Contains(nave.Nome, StringComparison.Ordinal),
			  $"dica: \"{E?.DicaNaTela}\"");
		ApertarE();
		yield return 0.2;

		// ---------------------------------------------------------------- F3  AS RECUSAS
		GD.Print("\n-- F3: AS RECUSAS CONTINUAM, PALAVRA POR PALAVRA --");
		FecharMenu();
		yield return 0.2;
		cli.SendVerbo("emb_alienar");         // a nave passa a ser de Fulano, trancada
		yield return 0.8;

		ApertarE();
		yield return 0.2;
		List<string> alheios = E?.BotoesDesenhados() ?? [];
		Checa("F3.1 a nave alheia AINDA oferece Embarcar (o menu nunca foi permissao)",
			  TemBotao(alheios, "Embarcar"), string.Join(" | ", alheios));

		ZoneKey zonaAntes = cli.Zone;
		Ouvindo();
		Apertar("Embarcar");
		yield return 1.0;
		Checa("F3.2 ...e apertar recusa com a frase da tranca, palavra por palavra",
			  Disse(_falas, TrechoDaTranca), string.Join(" | ", _falas));
		Checa("F3.3 ...e o corpo nao mudou de zona",
			  !ZonaMudou(zonaAntes, cli.Zone), $"{zonaAntes} -> {cli.Zone}");

		// A RECUSA DE DONO, DE FORA. "Melhorar velocidade" nao esta neste menu (ela e do CONSOLE, la
		// dentro) -- a de fora que confere dono e o `Pegar`. A outra e cobrada na ponte, mais adiante,
		// com a nave ainda no nome de Fulano.
		ApertarE();
		yield return 0.2;
		Ouvindo();
		Apertar("Pegar");
		yield return 0.8;
		Checa("F3.4 recolher a nave alheia recusa dizendo de quem ela e",
			  Disse(_falas, TrechoDoDono), string.Join(" | ", _falas));

		// ---------------------------------------------------------------- F6  O TECLADO DA SENHA
		GD.Print("\n-- F6: O TECLADO CABE NO QUE A ACAO PROMETE --");
		ApertarE();
		yield return 0.2;
		Apertar("Trancar com senha");
		yield return 0.3;
		Checa("F6.1 a acao de senha abre o teclado numerico", E is { TecladoNaTela: true });

		Ouvindo();
		string visor = E?.DigitarNoTeclado(GameServerFrases.Senha) ?? "";
		yield return 0.8;
		Checa($"F6.2 o teclado aceita os {GameServerFrases.Senha.Length} algarismos que o botao promete",
			  CabeNoTeclado(visor, double.Parse(GameServerFrases.Senha)),
			  $"visor mostrou \"{visor}\", pedi \"{GameServerFrases.Senha}\"");
		Checa("F6.3 ...e o servidor anota o codigo de quem nao e dono",
			  Disse(_falas, TrechoDoCodigoAnotado), string.Join(" | ", _falas));

		// ---------------------------------------------------------------- F3.6  COM O CODIGO, ENTRA
		ApertarE();
		yield return 0.2;
		Ouvindo();
		Apertar("Embarcar");
		foreach (object _ in Ate(() => cli.Zone.Equals(dentro), 6)) yield return 0.0;
		Checa("F3.6 com o codigo certo, a MESMA porta deixa entrar",
			  cli.Zone.Equals(dentro), $"zona: {cli.Zone}");

		// ---------------------------------------------------------------- F1.4..F1.7  ACHAR A PONTE
		Checa("F1.4 embarcar levou o corpo pra dentro da nave (uma zona que nao existia)",
			  ZonaMudou(zonaAntes, cli.Zone) && cli.Zone.Equals(dentro), $"zona: {cli.Zone}");

		FecharMenu();
		yield return 0.5;
		ApertarE();
		yield return 0.3;
		List<string> naChegada = E?.BotoesDesenhados() ?? [];
		// O CORPO NASCE UM TILE AO SUL DA PLATAFORMA -- entao aqui o E TEM alvo, e o que se cobra e
		// QUAL: o console esta a quarenta e cinco tiles, e "o mais perto ganha" e a regra do menu.
		// Cobrar "nao abre nada" aqui era erro da bancada, e a primeira rodada reprovou por isso.
		Checa("F1.4b ao chegar, o E abre a PLATAFORMA (o mais perto ganha) e nao o console de longe",
			  TemBotao(naChegada, "Desembarcar") && !TemBotao(naChegada, "Pilotar"),
			  $"\"{E?.TituloDesenhado}\": {string.Join(" | ", naChegada)}");

		// O PERCURSO ATE A PONTE. Tres pernas retas: oeste pelo corredor, norte pelo vao da parede da
		// sala (coluna 7, a unica passagem), e oeste de novo ate o lado do console.
		FecharMenu();
		Nota("andando ate a ponte -- oeste, norte pelo vao, oeste");
		foreach (object _ in AndarAte(Pixel((NaveGrande.ColunaDoVao, NaveGrande.CelDaChegada.Y)), 40, 4))
			yield return 0.0;

		// NO MEIO DA SALA nao ha nada: a plataforma ficou quarenta tiles atras e o console esta
		// quarenta a frente. E o contra-exemplo DENTRO da nave -- sem ele, "o console respondeu"
		// poderia ser "o console responde de qualquer lugar".
		FecharMenu();
		yield return 0.3;
		ApertarE();
		yield return 0.3;
		Checa("F2.5 no meio da sala, longe das duas pecas, o E nao abre nada",
			  E is { NaTela: false }, $"titulo: \"{E?.TituloDesenhado}\"");

		foreach (object _ in AndarAte(Pixel((NaveGrande.ColunaDoVao, NaveGrande.CelDoConsole.Y + 1)), 40, 4))
			yield return 0.0;
		foreach (object _ in AndarAte(Pixel((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1)), 25))
			yield return 0.0;

		Vector2 console = Pixel(NaveGrande.CelDoConsole);
		float doConsole = DistanciaDeMenu(Eu(), console);
		Checa("F1.5 o corpo atravessou a sala e chegou ao console andando",
			  DentroDoAlcance(doConsole), $"a {doConsole:0} px do console");

		ApertarE();
		yield return 0.3;
		List<string> doConsoleBotoes = E?.BotoesDesenhados() ?? [];
		Checa("F1.6 no console, o E abre o menu DELE (e em portugues)",
			  E is { NaTela: true } && (E.TituloDesenhado.Contains("Console", StringComparison.OrdinalIgnoreCase)
									 || E.TituloDesenhado.Contains("Ponte", StringComparison.OrdinalIgnoreCase)),
			  $"titulo: \"{E?.TituloDesenhado}\"");
		Checa("F1.7 ...com Pilotar e Observar, que sao as opcoes que o dono pediu la",
			  TemBotao(doConsoleBotoes, "Pilotar") && TemBotao(doConsoleBotoes, "Observar"),
			  string.Join(" | ", doConsoleBotoes));

		// ---------------------------------------------------------------- F3.5  A SEGUNDA RECUSA DE DONO
		// AQUI E NAO LA FORA: "Melhorar velocidade" e acao do CONSOLE, e a nave ainda esta no nome de
		// Fulano. O menu a oferece (esconder botao nunca foi permissao) e o servidor recusa com a mesma
		// frase do `Pegar` -- que e o ponto: uma frase so pra a mesma regra, em duas portas diferentes.
		Ouvindo();
		Apertar("Melhorar velocidade");
		yield return 0.8;
		Checa("F3.5 melhorar a nave alheia recusa com a MESMA frase, agora pelo console",
			  Disse(_falas, TrechoDoDono), string.Join(" | ", _falas));

		cli.SendVerbo("emb_devolver");   // dali em diante a nave e minha de novo
		yield return 0.6;

		// ---------------------------------------------------------------- F1.8..F1.12  O LEME E A VOLTA
		ApertarE();
		yield return 0.3;
		ZoneKey noInterior = cli.Zone;
		Apertar("Pilotar");
		foreach (object _ in Ate(() => !cli.Zone.Equals(noInterior), 6)) yield return 0.0;
		Checa("F1.8 Pilotar tira o corpo do interior e o poe junto do casco",
			  ZonaMudou(noInterior, cli.Zone), $"zona: {cli.Zone}");

		FecharMenu();
		yield return 0.5;
		ApertarE();
		yield return 0.3;
		List<string> doLeme = E?.BotoesDesenhados() ?? [];
		Checa("F1.9 ao leme, o E tem alvo -- o proprio VEICULO, que nao esta no chao",
			  E is { NaTela: true }, $"titulo: \"{E?.TituloDesenhado}\"");
		Checa("F1.10 ...e a PRIMEIRA opcao e voltar pra dentro (o pedido do dono, literal)",
			  PrimeiroBotaoE(doLeme, "Voltar à ponte"), string.Join(" | ", doLeme));
		// O `doLeme.Count > 0` NAO E ENFEITE: sem ele esta linha ficaria VERDE com o menu fechado, que
		// e o pior jeito de passar -- afirmar que uma opcao nao esta numa lista que nao existe.
		Checa("F1.11 ...e ele nao oferece Observar, que e do console e recusaria daqui",
			  doLeme.Count > 0 && !TemBotao(doLeme, "Observar"), string.Join(" | ", doLeme));

		Apertar("Voltar à ponte");
		foreach (object _ in Ate(() => cli.Zone.Equals(dentro), 6)) yield return 0.0;
		Checa("F1.12 apertar E de novo devolve o corpo pra DENTRO da nave",
			  cli.Zone.Equals(dentro), $"zona: {cli.Zone}");

		FecharMenu();
		yield return 0.5;
		ApertarE();
		yield return 0.3;
		Checa("F1.13 ...ao lado do console, que volta a responder (o ciclo fecha)",
			  TemBotao(E?.BotoesDesenhados() ?? [], "Pilotar"),
			  string.Join(" | ", E?.BotoesDesenhados() ?? []));

		// ---------------------------------------------------------------- F1.14..F1.15  SAIR
		FecharMenu();
		Nota("andando ate a plataforma de saida -- leste pelo vao, sul, leste");
		foreach (object _ in AndarAte(Pixel((NaveGrande.ColunaDoVao, NaveGrande.CelDoConsole.Y + 1)), 25, 4))
			yield return 0.0;
		foreach (object _ in AndarAte(Pixel((NaveGrande.ColunaDoVao, NaveGrande.CelDaChegada.Y)), 40, 4))
			yield return 0.0;
		foreach (object _ in AndarAte(Pixel(NaveGrande.CelDaChegada), 40)) yield return 0.0;

		ApertarE();
		yield return 0.3;
		List<string> naPlataforma = E?.BotoesDesenhados() ?? [];
		Checa("F1.14 na plataforma, o E oferece DESEMBARCAR",
			  TemBotao(naPlataforma, "Desembarcar"), string.Join(" | ", naPlataforma));

		Apertar("Desembarcar");
		foreach (object _ in Ate(() => !cli.Zone.Equals(dentro), 8)) yield return 0.0;
		Checa("F1.15 ...e o corpo sai pra onde a nave esta",
			  !cli.Zone.Equals(dentro), $"zona: {cli.Zone}");

		FecharMenu();
		yield return 0.6;
		ApertarE();
		yield return 0.3;
		List<string> depoisDeSair = E?.BotoesDesenhados() ?? [];
		Checa("F2.6 fora da nave, o menu do VEICULO some e volta o da nave parada",
			  TemBotao(depoisDeSair, "Embarcar") && !TemBotao(depoisDeSair, "Voltar à ponte"),
			  string.Join(" | ", depoisDeSair));

		// ---------------------------------------------------------------- F3.7  A NAVE DESTRUIDA
		Apertar("Embarcar");
		foreach (object _ in Ate(() => cli.Zone.Equals(dentro), 8)) yield return 0.0;
		Checa("F3.7 embarquei de novo pra o teste da destruicao", cli.Zone.Equals(dentro), $"zona: {cli.Zone}");

		Ouvindo();
		cli.SendVerbo("emb_estragar");
		foreach (object _ in Ate(() => !cli.Zone.Equals(dentro), 8)) yield return 0.0;
		Checa("F3.8 com o casco destruido, ninguem fica preso na zona que deixou de existir",
			  !cli.Zone.Equals(dentro), $"zona: {cli.Zone}");

		// A FRASE VEM DEPOIS DA MUDANCA DE ZONA no `DestruirNave` (primeiro o `MoveToZone`, depois o
		// `Avisar`), e o pacote de chat e outro pacote. Cobrar as duas no mesmo instante media uma
		// corrida, e a corrida ganhou: a rodada 5 reprovou uma frase que chegou seis linhas depois.
		foreach (object _ in Ate(() => Disse(_falas, TrechoDaExplosao), 3)) yield return 0.0;
		Checa("F3.9 ...e o jogo diz por que, com a frase de antes",
			  Disse(_falas, TrechoDaExplosao), string.Join(" | ", _falas));

		yield return 0.8;
		FecharMenu();
		yield return 0.2;
		ApertarE();
		yield return 0.3;
		Checa("F3.10 ...e ali o E nao abre mais nada: a nave saiu do mundo E da tela",
			  E is { NaTela: false }, $"titulo: \"{E?.TituloDesenhado}\"");

		// ---------------------------------------------------------------- F4 e F5  A ABA NAV
		foreach (object o in AbaNav(cli)) yield return o;

		// ---------------------------------------------------------------- A INJECAO
		Injetar();
	}

	/// <summary>
	/// A DISTANCIA DA COISA INTERATIVA MAIS PROXIMA QUE NAO SEJA A MINHA NAVE.
	///
	/// Guarda de SANIDADE DA BANCADA, e nao regra do jogo: as naves ficam no `naves.json` e uma rodada
	/// interrompida deixa entulho no mesmo pedaco de Terra. Sem esta leitura, a medida do alcance pode
	/// ser a distancia ate a nave de ontem -- que foi exatamente o que aconteceu na rodada 4.
	/// </summary>
	private static float MaisPertoQueMim(GameClient cli, GameClient.ObraInfo minha)
	{
		Vector2 eu = Eu();
		float melhor = float.MaxValue;
		foreach (GameClient.ObraInfo o in cli.Obras)
		{
			if (o.Id == minha.Id || !Interacoes.Interativo(o.Tipo)) continue;
			melhor = Math.Min(melhor, DistanciaDeMenu(eu, o.Pos));
		}
		return melhor;
	}

	/// <summary>A Capital Ship da zona cujo id NAO estava na lista de antes -- a que acabou de nascer.</summary>
	private static GameClient.ObraInfo? AchaNave(GameClient cli, HashSet<int> jaEstavam)
	{
		foreach (GameClient.ObraInfo o in cli.Obras)
			if (o.Tipo == NaveGrande.Tipo && !jaEstavam.Contains(o.Id)) return o;
		return null;
	}

	// =====================================================================
	// F4 e F5 -- A ABA NAV
	// =====================================================================
	private IEnumerable AbaNav(GameClient cli)
	{
		GD.Print("\n-- F4: O QUE SAIU DA ABA NAV SUMIU DELA --");

		if (MenuJogo.Instancia is not { } m)
		{
			Checa("F4.0 o menu P existe pra ser varrido", false, "MenuJogo.Instancia nulo");
			yield break;
		}

		m.Abrir();
		yield return 0.3;

		// A VARREDURA DE ROTULOS, EM TODAS AS ABAS. Percorre a arvore montada de cada pagina e junta
		// o `Text` de todo `Button` -- e nao a lista de abas nem uma tabela: o que se procura e um
		// botao que tenha SOBRADO, e sobra e coisa que so a arvore sabe.
		var todosOsRotulos = new List<string>();
		foreach (string aba in m.AbasDeTeste)
		{
			m.IrPara(aba);
			yield return 0.15;
			if (m.PaginaDeTeste(aba) is { } pg) todosOsRotulos.AddRange(Rotulos(pg));
		}

		Checa("F4.1 a varredura viu botoes de verdade (senao \"nao achei nave\" seria trivial)",
			  todosOsRotulos.Count > 10, $"{todosOsRotulos.Count} botao(oes) em {m.AbasDeTeste.Length} abas");
		Checa("F4.2 nenhum botao do menu P fala de nave, leme, lancar, pilotar ou recondicionar",
			  SemPalavraDeNave(todosOsRotulos),
			  string.Join(" | ", todosOsRotulos.Where(r => !SemPalavraDeNave(new[] { r }))));

		// A VARREDURA POR VERBO, na aba Nav -- que e onde os oito moravam.
		//
		// APERTA TUDO, com o espiao ENGOLINDO: rotulo nao prova nada (um botao "Ir" pode mandar
		// `nave_lancar`), e mandar de verdade dispararia viagem interestelar e invasao de planeta.
		m.IrPara("Nav");
		yield return 0.4;

		var saiu = new List<string>();
		GameClient.EspiaoDeVerbos = (c, a) => { saiu.Add(c + (a.Length > 0 ? ":" + a : "")); return false; };
		int apertados = 0;
		if (m.PaginaDeTeste("Nav") is { } nav)
			foreach (Button b in Botoes(nav)) { b.EmitSignal(BaseButton.SignalName.Pressed); apertados++; }
		GameClient.EspiaoDeVerbos = null;
		yield return 0.3;

		Checa("F4.3 a varredura apertou os botoes que a aba Nav tem",
			  apertados > 0, $"{apertados} botao(oes) apertado(s)");
		Checa("F4.4 e NENHUM deles manda um verbo `nave_*` -- nao ha segundo caminho",
			  SemVerboDeNave(saiu), string.Join(" | ", saiu));
		Nota($"a aba Nav manda hoje: {(saiu.Count == 0 ? "(nada -- os botoes dela sao de camera)" : string.Join(", ", saiu))}");

		// ---------------------------------------------------------------- F5  O QUE FICOU FUNCIONA
		GD.Print("\n-- F5: O QUE FICOU NA ABA CONTINUA FUNCIONANDO --");
		m.IrPara("Nav");
		yield return 0.4;

		Checa("F5.1 a carta estelar continua montada na aba", m.MapaDeTeste != null);
		if (m.MapaDeTeste is { } mapa)
		{
			float antes = mapa.EscalaDeTeste;
			Apertar(m, "Nav", "+");
			yield return 0.2;
			float depois = mapa.EscalaDeTeste;
			Checa("F5.2 o \"+\" aproxima de verdade (a escala do desenho muda)",
				  Mudou(antes, depois), $"{antes:0.0000} -> {depois:0.0000}");

			Apertar(m, "Nav", "-");
			yield return 0.2;
			Checa("F5.3 e o \"-\" desfaz", Math.Abs(mapa.EscalaDeTeste - antes) < antes * 0.01,
				  $"{depois:0.0000} -> {mapa.EscalaDeTeste:0.0000}");

			// ============================ "VER TUDO" SO PROVA ALGUMA COISA SE SAIR DE OUTRO LUGAR ============================
			// A carta ja NASCE enquadrada nos pre-feitos, entao apertar "ver tudo" numa carta recem-aberta
			// nao move nada -- e a checagem reprovava um botao que funciona. Aqui ela primeiro CENTRALIZA
			// EM MIM (que joga o centro pra minha estrela) e so entao pede "ver tudo": o que se cobra e o
			// botao trazer a carta de volta, e nao o numero ser diferente por acaso.
			// ============================================================================================
			Apertar(m, "Nav", "centralizar em mim");
			yield return 0.2;
			Vector2 emMim = mapa.CentroDeTeste;
			float escalaEmMim = mapa.EscalaDeTeste;

			Apertar(m, "Nav", "ver tudo");
			yield return 0.2;
			Checa("F5.4 \"ver tudo\" reenquadra a carta (sai de cima de mim)",
				  Mudou(emMim.X, mapa.CentroDeTeste.X) || Mudou(emMim.Y, mapa.CentroDeTeste.Y)
				  || Mudou(escalaEmMim, mapa.EscalaDeTeste),
				  $"centro {emMim} -> {mapa.CentroDeTeste}, escala {escalaEmMim:0.000000} -> {mapa.EscalaDeTeste:0.000000}");

			Vector2 verTudo = mapa.CentroDeTeste;
			Apertar(m, "Nav", "centralizar em mim");
			yield return 0.2;
			Checa("F5.5 ...e \"centralizar em mim\" traz a carta de volta pra onde eu estou",
				  (mapa.CentroDeTeste - emMim).Length() < 1f && Mudou(verTudo.X, mapa.CentroDeTeste.X),
				  $"centro {verTudo} -> {mapa.CentroDeTeste} (eu: {emMim})");
		}

		// AS DUAS LEGENDAS que desceram pra debaixo da carta. Elas sao a metade do bloco velho que
		// NAO era acao -- e a prova de que "sumiu" nao virou "sumiu tudo".
		string textos = m.PaginaDeTeste("Nav") is { } pgNav ? string.Join("\n", Etiquetas(pgNav)) : "";
		Checa("F5.6 a legenda da ESCALA (Terra->Namek, a pe x de nave) continua escrita embaixo da carta",
			  textos.Contains("Namek", StringComparison.OrdinalIgnoreCase)
			  && textos.Contains("min", StringComparison.OrdinalIgnoreCase),
			  textos.Length > 160 ? textos[..160] + "..." : textos);
		List<string> rotulosNav = m.PaginaDeTeste("Nav") is { } p2 ? Rotulos(p2) : [];
		Checa("F5.7 e os botoes de dominio planetario continuam la (fora do pedido, e nao sumiram junto)",
			  rotulosNav.Any(r => r.Contains("planeta", StringComparison.OrdinalIgnoreCase)
							   || r.Contains("domínios", StringComparison.OrdinalIgnoreCase)),
			  string.Join(" | ", rotulosNav));

		m.Fechar();
		yield return 0.2;
	}

	private static void Apertar(MenuJogo m, string aba, string rotulo)
	{
		if (m.PaginaDeTeste(aba) is not { } pg) return;
		foreach (Button b in Botoes(pg))
			if (b.Text == rotulo) { b.EmitSignal(BaseButton.SignalName.Pressed); return; }
	}

	private static List<string> Rotulos(Node raiz) => [.. Botoes(raiz).Select(b => b.Text)];

	private static IEnumerable<Button> Botoes(Node raiz)
	{
		foreach (Node n in raiz.GetChildren())
		{
			if (n is Button b) yield return b;
			foreach (Button f in Botoes(n)) yield return f;
		}
	}

	private static IEnumerable<string> Etiquetas(Node raiz)
	{
		foreach (Node n in raiz.GetChildren())
		{
			if (n is Label l) yield return l.Text;
			foreach (string f in Etiquetas(n)) yield return f;
		}
	}

	// =====================================================================
	// A RODADA DE INJECAO
	// =====================================================================
	/// <summary>
	/// ============================ TODA REGRA TEM QUE SABER REPROVAR ============================
	/// A rodada de cima passou num jogo que funciona -- e uma checagem que nunca viu vermelho e
	/// indistinguivel de uma checagem que nao sabe ficar vermelha. Aqui cada regra e chamada DE NOVO,
	/// a mesma funcao, com a amostra estragada de proposito, e o que se cobra e o NAO.
	///
	/// As amostras nao sao inventadas: cada uma e a forma exata do defeito que a regra existe pra
	/// pegar -- o botao que sumiu do menu, o menu que abre a tres tiles, a recusa que foi reescrita,
	/// o botao de nave que voltou pra aba, o teclado que engole o quinto algarismo.
	/// =======================================================================================
	/// </summary>
	private void Injetar()
	{
		GD.Print("\n-- INJECAO: cada regra contra o defeito que ela existe pra pegar --");

		int reprovou = 0, deixouPassar = 0;
		void Deve(string oque, bool regraDisseNao)
		{
			if (regraDisseNao) { reprovou++; GD.Print($"  ok    a regra reprova: {oque}"); }
			else { deixouPassar++; GD.PrintErr($"  FALHA a regra DEIXOU PASSAR: {oque}"); }
		}

		// F1/F3: o botao sumiu do menu desenhado
		Deve("o menu da nave sem o botao Embarcar",
			 !TemBotao(["Ver estado", "Pegar"], "Embarcar"));
		// F1.10: a volta pra ponte deixou de ser a primeira opcao (ou sumiu)
		Deve("o menu do leme sem \"Voltar à ponte\" na frente",
			 !PrimeiroBotaoE(["Lançar", "Voltar à ponte"], "Voltar à ponte"));
		// F2: o menu abrindo alem do alcance -- a banda morta de 16 px, de novo
		Deve($"o menu abrindo a {Interacoes.Alcance + 1:0} px (alem do alcance)",
			 !DentroDoAlcance(Interacoes.Alcance + 1));
		Deve("o \"nao abriu\" acontecendo DENTRO do alcance (o menu que nunca abre)",
			 !AlemDoAlcance(Interacoes.Alcance - 1));
		// F3: a recusa reescrita
		Deve("a recusa da tranca reescrita (\"nave trancada!\")",
			 !Disse(["nave trancada!"], TrechoDaTranca));
		Deve("a recusa de dono que nao saiu (nada foi dito)",
			 !Disse([], TrechoDoDono));
		// F4: o botao de nave que voltou pra aba
		Deve("um botao \"Lançar\" de volta em alguma aba",
			 !SemPalavraDeNave(["Stats", "Lançar", "Ver tudo"]));
		Deve("um botao \"Largar o leme\" de volta em alguma aba",
			 !SemPalavraDeNave(["Largar o leme"]));
		// F4: o segundo caminho pelo verbo, com rotulo inocente
		Deve("um botao de rotulo inocente mandando `nave_lancar`",
			 !SemVerboDeNave(["conq_ver", "nave_lancar"]));
		// F5: o zoom que nao mexe no desenho
		Deve("o \"+\" que nao muda o enquadramento",
			 !Mudou(1.0, 1.0));
		// F1: o embarque que nao muda de zona
		Deve("o Embarcar que nao tira o corpo do lugar",
			 !ZonaMudou(ZoneKey.Premade("Earth"), ZoneKey.Premade("Earth")));
		// F6: o teclado de quatro algarismos comendo o quinto e o sexto
		Deve("o teclado parando em 4 algarismos pra uma senha de 6",
			 !CabeNoTeclado("2718", 271828));

		GD.Print($"  injecao: {reprovou} regra(s) reprovaram o defeito, {deixouPassar} deixaram passar");
		if (deixouPassar > 0)
		{
			_falhou += deixouPassar;
			_vermelhas.Add($"INJECAO: {deixouPassar} regra(s) nao sabem reprovar");
		}
		else _ok += reprovou;
	}
}
