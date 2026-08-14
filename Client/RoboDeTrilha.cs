using Godot;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA TRILHA (`--diagtrilha`). Ela roda OS ROTEIROS do dono no jogo de verdade -- olhar a
/// camada de baixo antes de qualquer briga, bater, deixar a tag cair, transformar, transformar
/// DENTRO da briga, deixar a tag cair com o ESC ABERTO, e abrir o ESC fora da briga -- e escreve o
/// DIARIO DE TROCAS DE FAIXA de cada um: instante, arquivo e motivo. Antes de todos vem o roteiro 0,
/// que nao tem relogio: ele esgota os sacos de sorteio e confere DE QUE PASTA cada faixa sai.
///
/// ============================ METADE DELA AFIRMA AUSENCIA, E POR ISSO A OUTRA METADE EXISTE ============================
/// Os roteiros 3 e 4 provam que nada NOVO toca (num planeta sem tema de lugar, que nada toca) -- e
/// uma bancada so de ausencias fica verde com o audio MORTO, que e um defeito pior do que o relatado.
/// Por isso o roteiro 0 e o 7 sao contra-exemplos deliberados: o sorteio de menu devolve faixa de
/// verdade, e o ESC poe som SAINDO dos tocadores. Cada afirmacao de ausencia tem, em algum lugar, a
/// afirmacao contraria feita no mesmo formato.
///
/// E OS ROTEIROS 5 E 6 SAO OS DOIS DESEMPATES do dono, que nao sao silencio nem musica por padrao:
/// a faixa da forma acabando com a tag DE PE volta pra trilha de batalha, e a tag caindo com o ESC
/// ABERTO cai no tema do menu. Os dois foram implementados ao contrario um dia, e nenhum dos dois
/// aparece na tela.
/// ==============================================================================================================
///
/// ============================ POR QUE ELA E DIFERENTE DAS CHECAGENS DE AUDIO DO `--diagforma` ============================
/// Aquelas chamam `audio.Musica(...)` na mao e leem a camada de volta. Elas trancam a PRIORIDADE
/// (transformacao ganha do combate, menu perde do combate), e trancaram bem -- mas nao encostam na
/// unica coisa de que a queixa do dono trata: **o que acontece quando uma faixa CHEGA AO FIM
/// SOZINHA**. Esse caminho e o sinal `Finished` do Godot, e ele nao nasce de chamada nenhuma; nasce
/// de um tocador que tocou ate o fim.
///
/// Duas coisas so aparecem aqui e em lugar nenhum:
///   * o `Finished` esta MESMO ligado nos DOIS tocadores (era um lambda, virou metodo nomeado);
///   * a tag de combate cai sozinha no `_Process` do `World`, com o relogio de verdade, e nao
///     porque uma bancada chamou `PararCamada`.
///
/// E a pergunta que fecha a queixa -- *"e DEPOIS de parar, ficou parado?"* -- nao e uma checagem,
/// e uma ESPERA: ela so se responde ficando calado 45 segundos e contando quantas trocas houve.
/// Zero e a resposta certa. Nenhum teste de chamada direta consegue fazer essa pergunta.
/// ==================================================================================================================
///
/// COMO RODAR (headless serve -- musica nao se olha):
///     Godot --headless --path . --host --rede 7990 --logtrilha --diagtrilha \
///           --raca Saiyan --nome Trilha --conta trilha
///
/// E DE NOVO COM `--raca Demon`, que nasce no INFERNO -- a unica zona do jogo com tema de lugar. As
/// duas rodadas exercitam as mesmas linhas de codigo; o que muda e a resposta da zona, e e por isso
/// que as duas juntas dizem mais do que qualquer uma sozinha (ver o bloco logo acima).
///
/// ============================ "SILENCIO" NAO E A REGRA: A REGRA E "O QUE A ZONA PEDE" ============================
/// Esta bancada afirmava SILENCIO depois que a tag cai, cru, em tres roteiros. E silencio e so o
/// resultado PARTICULAR de um planeta sem tema de lugar: num planeta COM tema (hoje so o Inferno,
/// `Trilha.MusicaDe("Hell")`) o pedido de `Lugar` esta de pe o tempo todo e a tag caindo devolve o
/// tema do lugar -- o que o dono escolheu, com estas palavras: *"volta o tema do lugar"*.
///
/// Entao ela reprovava o jogo por um comportamento CERTO: rodar com `--raca Demon`, que nasce no
/// Inferno, dava `1 FALHA: a tag caiu sozinha em 120s sem ninguem socar` -- a bancada esperando uma
/// linha de silencio que nunca vem. O `--raca Saiyan` do comando acima virara, na pratica, uma
/// condicao escondida do teste.
///
/// Hoje toda afirmacao dessas passa por <see cref="ConferirOQueSobra"/>, que pergunta a ZONA o que
/// devia sobrar (<see cref="TemaDoLugar"/>) e cobra exatamente aquilo: sem tema, SILENCIO; com tema,
/// AQUELE arquivo na camada `Lugar`. As duas rodadas medem a MESMA regra e as duas ficam verdes --
/// e e o par delas que prova que quem manda ali e o pedido de LUGAR e nao acaso.
/// ==============================================================================================================
///
/// SEM `--bpteste`, DE PROPOSITO. As formas dos roteiros 4 e 5 entram pelo `World.AoMudarForma`, que
/// e o lado CLIENTE do pacote e nao pergunta BP a ninguem -- e BP alto tem um custo aqui: o clone da
/// mente nasce com o MEU BP, entao BP alto de os dois lados so encurta a briga. Com o BP de nascenca
/// ela dura o teste inteiro.
/// </summary>
public partial class RoboDeTrilha : Node
{
	private static GameClient? C => GameClient.Instance;
	private static World? Mundo => World.Instancia;

	private static void Nota(string linha) => GD.Print("[trilha-bancada] " + linha);

	private readonly List<string> _falhas = [];

	private void Conferir(bool ok, string oque)
	{
		Nota((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	// =====================================================================
	// O DIARIO
	// =====================================================================
	/// <summary>Uma troca de faixa, como o <see cref="AudioDirector.EspiaoDeMusica"/> a entrega.</summary>
	private readonly record struct Troca(double T, AudioDirector.Camada Camada, string Faixa, string Motivo)
	{
		public bool Silencio => Faixa.Length == 0;
		public string Nome => Silencio ? "SILENCIO" : Faixa.GetFile();

		/// <summary>
		/// NO SILENCIO NAO HA CAMADA. `Reavaliar` devolve `Lugar` quando NENHUMA camada tem pedido --
		/// e o valor de partida do laco, nao uma decisao -- e os despejos desta bancada estavam
		/// imprimindo `SILENCIO ... Lugar`, como se o tema do lugar estivesse tocando um arquivo vazio.
		/// O `--logtrilha` ja fora consertado disso; o diario da bancada, que e o que se cola no
		/// relatorio, tinha ficado com a mentira. Ver `AudioDirector.Anotar`.
		/// </summary>
		public string Dono => Silencio ? "(sem pedido)" : Camada.ToString();
	}

	private readonly List<Troca> _diario = [];

	/// <summary>Onde o roteiro em curso comecou a olhar. Ver <see cref="Desde"/>.</summary>
	private int _marco;

	/// <summary>As trocas desde o ultimo <see cref="Marcar"/>. E o "log deste roteiro" e nao o do dia.</summary>
	private List<Troca> Desde() => _diario.GetRange(_marco, _diario.Count - _marco);

	/// <summary>
	/// COMECA UMA JANELA NOVA. Sem argumento e "daqui pra frente"; com <paramref name="onde"/> e a
	/// partir daquela troca -- o que o roteiro 1 usa pra devolver ao roteiro 2 a subida de tag que
	/// aconteceu debaixo dele (ver o fim do <see cref="OTemaDoLugarEDaZona"/>).
	/// </summary>
	private void Marcar(int onde = -1) => _marco = onde < 0 ? _diario.Count : onde;

	/// <summary>
	/// O DIARIO DO ROTEIRO, IMPRESSO INTEIRO -- que e o que o dono pediu ("responda com o log de
	/// cada roteiro"). Vai depois das checagens de proposito: primeiro o veredito, depois a prova.
	/// </summary>
	private void Despejar(string titulo)
	{
		List<Troca> l = Desde();
		Nota($"  ---- diario de `{titulo}`: {l.Count} troca(s) ----");
		if (l.Count == 0) Nota("  ----   (nenhuma -- o tocador nao mudou de estado nesta janela)");
		foreach (Troca t in l)
			Nota($"  ----   {t.T,8:0.00}s  {t.Nome,-44}  {t.Dono,-14} <- {t.Motivo}");
		Nota("");
	}

	// =====================================================================
	// O ESTADO DA BANCADA
	// =====================================================================
	private int _fase;
	private double _t;          // relogio DENTRO da fase
	private double _proximoSoco;
	private bool _socando;
	private bool _acabou;

	/// <summary>Quantas vezes o encadeamento de combate ja foi provado. Duas, pra nao ser sorte.</summary>
	private int _encadeou;

	private string _faixaDeCombate = "";
	private double _tagSubiuEm;

	/// <summary>A cadencia do soco, que o servidor manda na ficha. Ver `RoboDeSoco`.</summary>
	private double _cadencia = Jandirus.Net.Protocol.AttackPoseMs / 1000.0;

	public override void _Ready()
	{
		// ESTE MUNDO E MEU? Copiado do `RoboDeCena:100` e pelo mesmo motivo escrito la: com a porta
		// tomada o `--host` nao vira servidor nenhum e o cliente entra no mundo DE OUTRA SESSAO -- e
		// esta bancada SOCA um NPC e transforma o corpo duas vezes. Ha outra sessao neste repo agora.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[trilha-bancada] RECUSADO: subi com `--host` mas a porta ja estava tomada -- "
					  + "este mundo e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
			return;
		}

		AudioDirector.EspiaoDeMusica += Anotou;
		if (C is { } cli)
		{
			cli.SheetUpdated += AoReceberFicha;
			cli.SnapshotReceived += Avistou;
		}
		Nota("de pe. Os roteiros do dono, no jogo, com o relogio de verdade.");
		OSorteioNaoMisturaAsPastas();
	}

	// =====================================================================
	// ROTEIRO 0 -- DE ONDE SAI CADA FAIXA (sem relogio: e funcao pura)
	// =====================================================================
	/// <summary>
	/// AS DUAS PASTAS, LIDAS DO DISCO. Elas nao sao escritas na mao aqui de proposito: quem manda e
	/// a pasta (ver <see cref="Trilha"/>), entao uma lista transcrita nesta bancada envelheceria no
	/// dia em que o dono jogasse um .ogg novo em `battle ost` -- e envelheceria calada, dando FALHA
	/// numa faixa legitima.
	/// </summary>
	private static List<string> Pasta(string sub)
	{
		string caminho = $"res://Assets/Sounds/Music/{sub}";
		var achados = new List<string>();
		foreach (string bruto in DirAccess.GetFilesAt(caminho))
		{
			string nome = bruto;
			if (nome.EndsWith(".remap")) nome = nome[..^6];
			if (nome.EndsWith(".import")) nome = nome[..^7];
			if (!nome.EndsWith(".ogg") && !nome.EndsWith(".mp3") && !nome.EndsWith(".wav")) continue;
			if (!achados.Contains($"{caminho}/{nome}")) achados.Add($"{caminho}/{nome}");
		}
		return achados;
	}

	/// <summary>
	/// NENHUMA FAIXA DE MENU PODE CAIR NO SORTEIO DE COMBATE -- a queixa do dono pela raiz de baixo.
	///
	/// ============================ AS OUTRAS CHECAGENS SO OLHAM O QUE ACONTECEU ============================
	/// Todo o resto desta bancada mede UM sorteio de cada vez: o da briga daquela rodada. Se
	/// `Assets/Sounds/Music/Menu ost` fosse uma SUBPASTA varrida junto com a de batalha -- ou se alguem
	/// mudasse `Trilha.Combate()` pra cair na lista errada quando a pasta some --, o defeito apareceria
	/// em uma briga a cada 40 e nenhuma rodada de bancada o pegaria. E exatamente o formato do defeito
	/// que ele ouviu: intermitente, sem rastro, e "eu juro que ouvi a musica do menu".
	///
	/// Entao aqui se ESGOTA o saco duas voltas inteiras e se olha CADA faixa sorteada. E de graca (nao
	/// carrega audio nenhum, so devolve caminho) e e a unica checagem que fala do sorteio INTEIRO.
	/// ==============================================================================================
	///
	/// Consumir o saco aqui nao atrapalha os roteiros seguintes: o `Saco` re-embaralha quando esvazia,
	/// e nenhum roteiro afirma QUAL faixa vai sair -- so de que pasta ela veio.
	/// </summary>
	private void OSorteioNaoMisturaAsPastas()
	{
		Nota("");
		Nota("===== ROTEIRO 0 -- de que pasta sai cada faixa =====");

		List<string> combate = Pasta("battle ost");
		List<string> menu = Pasta("Menu ost");
		Conferir(combate.Count >= 2, $"a pasta `battle ost` tem faixa pra encadear ({combate.Count})");
		Conferir(menu.Count >= 1, $"a pasta `Menu ost` tem faixa pra sortear ({menu.Count})");

		var repetidas = new List<string>();
		foreach (string c in combate) if (menu.Contains(c)) repetidas.Add(c.GetFile());
		Conferir(repetidas.Count == 0,
				 repetidas.Count == 0
					? "as duas listas sao DISJUNTAS -- nenhum arquivo esta nas duas"
					: $"as duas listas sao disjuntas (compartilham: {string.Join(", ", repetidas)})");

		// duas voltas inteiras do saco + folga: assim todo arquivo da pasta sai pelo menos uma vez,
		// e o re-embaralhamento do meio (que e onde uma lista errada entraria) tambem e exercitado
		int rodadas = combate.Count * 2 + 5;
		var forasteiras = new List<string>();
		var deMenu = new List<string>();
		for (int i = 0; i < rodadas; i++)
		{
			string f = Trilha.Combate();
			if (menu.Contains(f)) deMenu.Add(f.GetFile());
			else if (!combate.Contains(f)) forasteiras.Add(f.GetFile());
		}
		Conferir(deMenu.Count == 0,
				 deMenu.Count == 0
					? $"{rodadas} sorteios de combate e NENHUM caiu numa faixa de menu"
					: $"{rodadas} sorteios de combate e nenhum e de menu (saiu: {string.Join(", ", deMenu)})");
		Conferir(forasteiras.Count == 0,
				 forasteiras.Count == 0
					? $"...e os {rodadas} sairam todos de dentro da pasta `battle ost`"
					: $"...e saem todos de `battle ost` (fora da pasta: {string.Join(", ", forasteiras)})");

		// O CONTRA-EXEMPLO. Sem ele, um `Trilha.Combate()` que devolvesse "" pra sempre passaria as
		// duas checagens acima -- "nenhuma de menu, nenhuma de fora" e verdade sobre lista nenhuma --
		// e o jogo estaria MUDO em vez de consertado.
		var forasteirasMenu = new List<string>();
		for (int i = 0; i < menu.Count * 2 + 3; i++)
		{
			string f = Trilha.Menu();
			if (!menu.Contains(f)) forasteirasMenu.Add(f.Length == 0 ? "(vazio)" : f.GetFile());
		}
		Conferir(forasteirasMenu.Count == 0,
				 forasteirasMenu.Count == 0
					? "e o sorteio de MENU so devolve faixa de `Menu ost` (o contra-exemplo: audio vivo)"
					: $"o sorteio de menu so devolve faixa de `Menu ost` (fora: {string.Join(", ", forasteirasMenu)})");
		Nota("");
	}

	/// <summary>A pasta de onde cada faixa DEVE sair, lida uma vez. Ver <see cref="Pasta"/>.</summary>
	private List<string>? _daBatalha, _doMenu;

	private bool EhDeCombate(string caminho) => (_daBatalha ??= Pasta("battle ost")).Contains(caminho);
	private bool EhDeMenu(string caminho) => (_doMenu ??= Pasta("Menu ost")).Contains(caminho);

	// =====================================================================
	// O QUE A ZONA PEDE -- a camada de baixo de tudo
	// =====================================================================
	/// <summary>
	/// O TEMA DESTE LUGAR, ou "" se este lugar nao tem tema.
	///
	/// ============================ NAO SE PERGUNTA AO `AudioDirector` ============================
	/// A resposta sai de `Trilha.MusicaDe(nome da zona)` -- funcao pura, tabela literal -- alimentada
	/// pela zona que o SERVIDOR mandou (`GameClient.Zone`). Ler `_pedidos[Lugar]` seria mais curto e
	/// nao valeria nada: um defeito que apagasse o pedido de lugar deixaria a bancada esperando
	/// silencio, achando silencio, e dando verde. E o cego das duas telas que concordam -- a medida e
	/// a expectativa saindo da mesma fonte.
	///
	/// E a MESMA funcao que o `World` consulta ao chegar na zona, o que deixa um buraco menor de pe:
	/// se a TABELA mentisse (Inferno devolvendo ""), as duas pontas concordariam no vazio. Por isso
	/// <see cref="OTemaDoLugarEDaZona"/> confere tambem que o arquivo apontado EXISTE no disco.
	/// =======================================================================================
	/// </summary>
	private static string TemaDoLugar() => C is { } cli ? Trilha.MusicaDe(cli.ZonaDeTeste.Name) : "";

	private static string NomeDaZona() => C is { } cli ? cli.ZonaDeTeste.Name : "(sem cliente)";

	/// <summary>
	/// O QUE TINHA QUE SOBRAR quando o ultimo pedido PASSAGEIRO saiu de cena -- a tag caindo, o tema
	/// da forma acabando, o ESC fechando. E a mesma pergunta nos tres, e a resposta e da ZONA:
	///
	///   * zona sem tema  -> SILENCIO, que e o que o dono pediu (*"as musicas PARAM"*);
	///   * zona com tema  -> AQUELE arquivo, na camada `Lugar` (*"volta o tema do lugar"*).
	///
	/// A checagem cobra o CAMINHO do arquivo e nao "alguma coisa da camada Lugar": um pedido de lugar
	/// carregando a faixa errada e exatamente o formato do defeito que abriu esta tarefa.
	/// </summary>
	private void ConferirOQueSobra(Troca t, string quando)
	{
		string tema = TemaDoLugar();
		if (tema.Length == 0)
		{
			Conferir(t.Silencio, $"{quando}: `{NomeDaZona()}` nao tem tema de lugar -> SILENCIO (entrou `{t.Nome}`)");
			return;
		}

		Conferir(!t.Silencio && t.Camada == AudioDirector.Camada.Lugar && t.Faixa == tema,
				 $"{quando}: `{NomeDaZona()}` tem tema de lugar -> VOLTOU `{tema.GetFile()}` "
			   + $"(entrou `{t.Nome}`, camada {t.Dono})");
	}

	/// <summary>
	/// O AR CORRESPONDE AO QUE A ZONA PEDE? Nao e a mesma pergunta que "o diario nao mudou": o diario
	/// registra a DECISAO da maquina, e um `Calar()` que parasse so um dos dois tocadores nao
	/// escreveria linha nenhuma. Ver <see cref="AudioDirector.TocandoDeTeste"/>.
	///
	/// Numa zona sem tema isto e "os dois tocadores estao mesmo parados"; numa zona com tema e o
	/// contrario -- ha som saindo --, e as duas sao a mesma afirmacao: o que se ouve e o que a zona
	/// pediu. Cobrar silencio no Inferno seria cobrar que o `Demon World` nao tocasse.
	/// </summary>
	private void ConferirOArDaZona(string onde)
	{
		if (AudioDirector.Instance is not { } audio) { Conferir(false, $"achei o AudioDirector pra conferir o ar em {onde}"); return; }
		bool temTema = TemaDoLugar().Length > 0;
		Conferir(audio.TocandoDeTeste == temTema,
				 temTema
					? $"...e ha som SAINDO dos dois tocadores ({onde}) -- e o tema de `{NomeDaZona()}`, que toca em laco"
					: $"...e os DOIS tocadores estao mesmo parados ({onde}) -- nao so o diario");
	}

	// METODOS NOMEADOS e nao lambdas: o `EspiaoDeMusica` e `static`, entao um lambda esquecido aqui
	// sobreviveria a esta cena e escreveria no diario de uma bancada seguinte. Ver as assinaturas
	// vazadas -- lambda nao se cancela num `-=`.
	public override void _ExitTree()
	{
		AudioDirector.EspiaoDeMusica -= Anotou;
		if (C is not { } cli) return;
		cli.SheetUpdated -= AoReceberFicha;
		cli.SnapshotReceived -= Avistou;
	}

	private void AoReceberFicha(Jandirus.Net.SheetState f) { if (f.SocoMs > 0) _cadencia = f.SocoMs / 1000.0; }

	/// <summary>Marca o primeiro corpo que nao sou eu -- dentro da mente, o clone. O que esta em
	/// teste e a TAG DE COMBATE, e ela nao pergunta quem levou o soco.</summary>
	private void Avistou(List<Jandirus.Net.EntityState> estados)
	{
		if (C is not { } cli) return;

		// O ALVO PODE SUMIR (morreu, andou pra fora do recorte): sem re-marcar, o robo passa o resto
		// da rodada socando o lugar onde alguem esteve.
		bool aindaEsta = false;
		foreach (Jandirus.Net.EntityState e in estados) if (e.Id == _alvo) { aindaEsta = true; break; }
		if (!aindaEsta) { _alvo = 0; _alvoPos = null; }

		foreach (Jandirus.Net.EntityState e in estados)
		{
			if (e.Id == cli.LocalId) continue;
			if (_alvo == 0) { _alvo = e.Id; C?.SendAlvo(e.Id); }
			if (e.Id == _alvo) _alvoPos = new Vector2(e.Pos.X, e.Pos.Y);
		}
	}

	private int _alvo;
	private Vector2? _alvoPos;

	private void Anotou(double t, AudioDirector.Camada camada, string faixa, string motivo)
		=> _diario.Add(new Troca(t, camada, faixa, motivo));

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || Mundo is null || cli.LocalId == 0) return;

		_t += delta;
		Socar(delta);

		switch (_fase)
		{
			case 0: OTemaDoLugarEDaZona(); break;
			case 1: Preparar(); break;
			case 2: SubirATag(); break;
			case 3: DeixarAFaixaAcabar(); break;
			case 4: DeixarATagCair(); break;
			case 5: OQueSobraSeSustenta(); break;
			case 6: Transformar(); break;
			case 7: OQueSobraDaFormaSeSustenta(); break;
			case 8: OCruzamento(); break;
			case 9: OQueSobraDoCruzamento(); break;
			case 10: ATagCaiComOEscAberto(); break;
			case 11: OMenuAindaToca(); break;
			default: Fechar(); break;
		}
	}

	/// <summary>Passa de fase zerando o relogio dela. Uma linha, pra nao esquecer o `_t = 0`.</summary>
	private void Ir(int fase) { _fase = fase; _t = 0; }

	/// <summary>
	/// O SOCO, na cadencia da ficha. E o mesmo `SendAction` do `--socar`: nao ha atalho pra levantar
	/// a tag de combate, porque a tag e justamente o que se esta medindo.
	/// </summary>
	private void Socar(double delta)
	{
		if (!_socando || C is not { Connected: true } cli) { Parar(); return; }

		Aproximar();

		_proximoSoco -= delta;
		if (_proximoSoco > 0) return;
		_proximoSoco = _cadencia;
		cli.SendAction();
	}

	/// <summary>
	/// ANDA ATE O ALVO, apertando as MESMAS teclas do jogador (copiado do `RoboDeSoco.Andar`).
	///
	/// ============================ SEM ISTO A BANCADA MEDE SOCO NO VAZIO ============================
	/// A primeira rodada socou 40 s e a trilha de batalha nunca entrou. Nao era a musica: e o
	/// `World.AplicarGolpe` que so levanta a tag quando `h.Alvo != 0` -- de proposito, pra treinar
	/// sozinho num canto nao por tema de briga no ar. O robo socava o ar a meio mapa do NPC.
	///
	/// PARA UM POUCO ALEM DO ALCANCE (48 px): quem fecha o ultimo palmo e o servidor, que ja tem
	/// regra pra isso. Ver a nota de `RoboDeSoco.PararA`.
	/// ============================================================================================
	/// </summary>
	private void Aproximar()
	{
		Vector2? eu = Mundo?.PosicaoLocal;
		if (eu == null || _alvoPos == null) { Parar(); return; }

		Vector2 d = _alvoPos.Value - eu.Value;
		if (d.Length() < 48f) { Parar(); return; }

		Segurar("move_right", d.X > 8f);
		Segurar("move_left", d.X < -8f);
		Segurar("move_down", d.Y > 8f);
		Segurar("move_up", d.Y < -8f);
		Input.ActionPress("run");
	}

	private static void Parar()
	{
		foreach (string a in Direcoes) Input.ActionRelease(a);
		Input.ActionRelease("run");
	}

	private static void Segurar(string acao, bool sim)
	{
		if (sim) Input.ActionPress(acao);
		else Input.ActionRelease(acao);
	}

	private static readonly string[] Direcoes = ["move_right", "move_left", "move_up", "move_down"];

	// =====================================================================
	// 1. O TEMA DO LUGAR -- a camada de baixo e da ZONA (antes de qualquer briga)
	// =====================================================================
	/// <summary>
	/// A CAMADA `Lugar`, QUE NENHUM ROTEIRO OLHAVA.
	///
	/// ============================ ELA E O CHAO DE TODAS AS OUTRAS AFIRMACOES ============================
	/// Os outros roteiros medem o que acontece quando um pedido PASSAGEIRO sai de cena -- e o que fica
	/// embaixo dele e sempre isto. Enquanto a bancada so rodou na Terra, "o que fica embaixo" foi
	/// **nada**, e a ausencia de tema de lugar virou, sem ninguem escrever, a definicao de "certo": a
	/// mesma bancada rodada no Inferno reprovava o jogo tres vezes por tocar o `Demon World` quando
	/// devia.
	///
	/// O dono desempatou -- *"volta o tema do lugar"* --, e o que este roteiro tranca e que o dono da
	/// camada de baixo e a ZONA. Ele pergunta duas vezes a mesma coisa, em dois lugares diferentes, no
	/// mesmo minuto e sem nenhuma briga no meio:
	///
	///   * NO PLANETA DE NASCENCA: o que esta no ar tem que ser exatamente `Trilha.MusicaDe(zona)` --
	///     o `Demon World.mp3` pra quem nasceu no Inferno, NADA pra todo mundo mais;
	///   * DENTRO DA PROPRIA MENTE (`Interdimension`, que nao tem tema): o mergulho tem que TIRAR o
	///     tema do ar, e o silencio ali e o mesmo silencio da Terra.
	///
	/// Duas zonas, o mesmo codigo, respostas opostas -- e e isso que separa "o pedido de LUGAR mandou"
	/// de "por acaso nao tinha nada tocando". Rodada com `--raca Demon` as duas linhas saem no mesmo
	/// diario, uma embaixo da outra.
	/// ================================================================================================
	///
	/// O ARQUIVO TEM QUE EXISTIR, e essa e a unica checagem daqui que nao olha a maquina de musica:
	/// a expectativa desta bancada sai da mesma tabela que o jogo consulta (ver <see cref="TemaDoLugar"/>),
	/// entao uma tabela apontando pra um arquivo que nao esta la daria silencio dos DOIS lados e verde.
	/// </summary>
	private void OTemaDoLugarEDaZona()
	{
		if (_t < 3) return;   // deixa o mundo assentar: a zona chega antes, mas o menu sai depois
		if (AudioDirector.Instance is not { } audio) return;

		if (!_olheiOPlaneta)
		{
			Nota("");
			Nota("===== ROTEIRO 1 -- O TEMA DO LUGAR: a camada de baixo e da ZONA =====");
			Marcar();
			_olheiOPlaneta = true;

			string tema = TemaDoLugar();
			_temaDoBerco = tema;
			Nota($"  --     MEDIDO: nasci em `{NomeDaZona()}`, e a tabela do lugar diz "
			   + (tema.Length == 0 ? "que aqui NAO HA TEMA." : $"`{tema.GetFile()}`."));

			if (tema.Length == 0)
			{
				Conferir(!audio.TocandoDeTeste && audio.FaixaDeTeste.Length == 0,
						 $"`{NomeDaZona()}` nao tem tema de lugar -> o jogo entra CALADO "
					   + $"(esta `{(audio.FaixaDeTeste.Length == 0 ? "SILENCIO" : audio.FaixaDeTeste.GetFile())}`)");
			}
			else
			{
				Conferir(ResourceLoader.Exists(tema), $"o tema de `{NomeDaZona()}` EXISTE no disco (`{tema}`)");
				Conferir(audio.FaixaDeTeste == tema,
						 $"`{NomeDaZona()}` tem tema de lugar -> ele esta no ar sozinho: `{tema.GetFile()}` "
					   + $"(esta `{(audio.FaixaDeTeste.Length == 0 ? "SILENCIO" : audio.FaixaDeTeste.GetFile())}`)");
				Conferir(audio.CamadaDeTeste == AudioDirector.Camada.Lugar,
						 $"...e quem manda e a camada `Lugar` (esta `{audio.CamadaDeTeste}`)");
				Conferir(audio.TocandoDeTeste, "...e ha som SAINDO de verdade (nao e so o pedido escrito)");
			}

			_marcoDoFim = _diario.Count;
			EntrarNaMente();   // o mergulho que o roteiro 2 precisa e a alavanca da segunda metade deste
			_t = 0;
			return;
		}

		// --- a mente e outra zona, e ela NAO tem tema -----------------------------------------
		// NO QUADRO EM QUE A ZONA CHEGA, e sem esperar nada. O pedido de lugar e escrito dentro do
		// `CarregarZona`, ou seja no mesmo quadro em que esta condicao vira verdade -- e a espera de
		// 1,5 s que havia aqui custava caro: o CLONE nasce junto comigo aqui dentro e me acerta ~2,5 s
		// depois do mergulho, e a tag de combate dele entrava na janela desta medicao (medido: uma
		// linha `1o golpe que me envolve` no diario deste roteiro, e duas falhas que nao eram defeito).
		if (C is not { } cli || cli.ZonaDeTeste.Name != Jandirus.Core.World.DimensaoMental.Zona)
		{
			if (_t > 20) { Conferir(false, "o mergulho na mente trocou a zona do cliente (roteiro 1)"); Ir(99); }
			return;
		}

		Conferir(TemaDoLugar().Length == 0,
				 $"a mente (`{NomeDaZona()}`) nao tem tema de lugar -- e a OUTRA zona desta rodada");

		// AS PASSAGEIRAS SAO POSTAS DE LADO, e nao contadas como intrusas: se o clone chegou primeiro,
		// a trilha de batalha dele e uma resposta CERTA a outra pergunta. O que este roteiro cobra e a
		// camada `Lugar`, e ela nao se defende sozinha de quem esta por cima.
		var doLugar = new List<Troca>();
		int daBriga = -1;
		for (int i = _marcoDoFim; i < _diario.Count; i++)
		{
			if (!_diario[i].Silencio && _diario[i].Camada >= AudioDirector.Camada.Combate)
			{
				if (daBriga < 0) daBriga = i;
				Nota($"  --     (o clone me acertou antes de eu medir: `{_diario[i].Nome}` <- {_diario[i].Motivo})");
				continue;
			}
			doLugar.Add(_diario[i]);
		}
		bool briga = daBriga >= 0;

		if (_temaDoBerco.Length == 0)
		{
			// nao havia o que tirar do ar: o certo aqui e o diario NAO ter linha nenhuma -- a maquina
			// nem reavalia quando o pedido novo e igual ao que ja estava (vazio)
			Conferir(doLugar.Count == 0,
					 doLugar.Count == 0
						? "vim de uma zona SEM tema: mudar de zona nao mexeu na camada `Lugar` (0 trocas)"
						: $"vim de uma zona sem tema e mudar de zona nao devia mexer na camada `Lugar` ({doLugar.Count} troca(s))");
		}
		else
		{
			Conferir(doLugar.Count > 0 && doLugar[0].Silencio && doLugar[0].Motivo.Contains("cheguei em"),
					 doLugar.Count > 0
						? $"mergulhar na mente TIRA o tema do berco do ar: `{doLugar[0].Nome}` <- {doLugar[0].Motivo}"
						: "mergulhar na mente tira o tema do berco do ar (a camada `Lugar` nao mudou de estado)");
		}

		// o ar so responde a esta pergunta se ninguem estiver por cima da camada `Lugar`
		if (briga) Nota("  --     (nao da pra ouvir a camada `Lugar` com a trilha de batalha por cima: ar nao conferido)");
		else ConferirOArDaZona("dentro da propria mente");

		Despejar("ROTEIRO 1 -- o tema do lugar e a troca de zona");

		// ============================ O MARCO DO ROTEIRO 2 NASCE AQUI, E AS VEZES ATRAS ============================
		// Quem levanta a tag de combate pode ser o CLONE -- golpe RECEBIDO conta --, e ele bate antes
		// de o `Preparar` ter alvo marcado. Medido: o mergulho cai no meio do povoamento do mundo (uns
		// 400 NPCs nascendo), o quadro trava ~10 s, e quando a bancada volta a rodar a tag ja subiu.
		//
		// Entao a janela do roteiro 2 comeca NAQUELA troca e nao "daqui pra frente": com o marco na
		// frente dela a subida da tag ficava fora da janela e o roteiro 2 esperava 40 s por uma linha
		// que ja tinha passado -- reprovando o jogo por um golpe que chegou cedo demais.
		// ======================================================================================================
		Marcar(daBriga);
		Ir(1);
	}

	private bool _olheiOPlaneta;

	/// <summary>O tema do planeta onde nasci, lido uma vez. Ver <see cref="OTemaDoLugarEDaZona"/>.</summary>
	private string _temaDoBerco = "";

	// =====================================================================
	// 2. PREPARAR -- arrumar em quem bater
	// =====================================================================
	/// <summary>
	/// ============================ O ADVERSARIO E O CLONE DA MENTE, E NAO UM NPC DO BERCO ============================
	/// A primeira tentativa socava "o primeiro corpo do snapshot", como o `--socar`. Ela morreu com
	/// *"achei alguem pra socar: FALHA"* -- e o motivo nao tinha nada a ver com audio: a classe e
	/// sorteada na criacao, saiu **Legendary**, e o Lendario nasce EXILADO num planeta gerado e vazio
	/// (`ExilioDoLendario`). Zero NPCs. Uma bancada que so funciona quando o dado cai certo nao e uma
	/// bancada.
	///
	/// A mente resolve isso de vez, e e a mesma escolha (e o mesmo comentario) do `RoboDeSoco.--mente`:
	/// *"o clone e um oponente que sempre existe"*. Nao ha gate de skill -- basta meditar --, o clone
	/// nasce com o meu BP (briga longa, ninguem morre no primeiro soco) e, o principal pro roteiro 2,
	/// **da pra sair**: `sairdamente` me devolve sozinho pro planeta, e so ai a tag pode cair. Correr
	/// de um clone que persegue seria correr pra sempre.
	/// ==========================================================================================================
	/// </summary>
	private void Preparar()
	{
		if (C is not { } cli) return;

		// O MERGULHO JA ACONTECEU no roteiro 1, que precisava da mente como segunda zona. Este
		// `EntrarNaMente` fica porque ele e idempotente e porque quem le esta fase nao pode depender
		// de a anterior ter mergulhado -- ele devolve verdadeiro de graca se ja estou la dentro.
		if (!EntrarNaMente()) return;

		if (cli.AlvoId == 0 || _alvo == 0)
		{
			if (_t > 30) { Conferir(false, "o clone da mente apareceu pra socar (sem alvo nao ha tag de combate)"); Ir(99); }
			return;
		}

		Conferir(true, $"tenho um adversario: o clone da mente (id {_alvo})");
		Nota("");
		Nota("===== ROTEIRO 2 -- COMBATE: a faixa acaba com a tag DE PE =====");
		// SEM `Marcar()` AQUI: a janela deste roteiro comeca no fim do roteiro 1, porque a tag de
		// combate pode ter subido por um golpe do CLONE antes de eu ter alvo pra socar. Ver o fim do
		// `OTemaDoLugarEDaZona`.
		_socando = true;
		Ir(2);
	}

	/// <summary>Mergulha na propria mente, uma vez. Devolve falso enquanto o pedido nao foi feito.</summary>
	private bool EntrarNaMente()
	{
		if (_naMente) return true;
		if (C is not { } cli) return false;
		cli.SendActivity(Jandirus.Net.Protocol.Activity.Meditando);
		cli.SendHabilidade("mente");
		_naMente = true;
		Nota("meditando e mergulhando na propria mente -- o clone e o adversario");
		return false;   // um quadro pro servidor trocar a zona e o clone entrar no snapshot
	}

	/// <summary>Abre os olhos: volta pro planeta, SOZINHO. E o que deixa a tag de combate cair.</summary>
	private void SairDaMente()
	{
		if (!_naMente || C is not { } cli) return;
		cli.SendHabilidade("sairdamente");
		cli.SendActivity(Jandirus.Net.Protocol.Activity.Parado);
		_naMente = false;
		_alvo = 0;
		_alvoPos = null;
		Nota("sai da mente -- ninguem mais pra bater em mim, a tag pode cair");
	}

	private bool _naMente;

	// =====================================================================
	// 3. A TAG SOBE
	// =====================================================================
	private void SubirATag()
	{
		Troca? combate = UltimaDe(AudioDirector.Camada.Combate);
		if (combate is not { } c)
		{
			if (_t > 40) { Conferir(false, $"a musica de combate entrou socando ({_t:0}s de socos e nada)"); Ir(99); }
			return;
		}

		_faixaDeCombate = c.Faixa;
		_tagSubiuEm = c.T;
		Conferir(true, $"o 1o golpe poe a trilha de batalha no ar: `{c.Nome}`");
		Conferir(EhDeCombate(c.Faixa), "...e ela sai da pasta `battle ost`");
		Conferir(!EhDeMenu(c.Faixa), "...e nao e uma faixa de menu");
		Ir(3);
	}

	// =====================================================================
	// 4. A FAIXA ACABA COM A TAG DE PE -> ENTRA OUTRA DE COMBATE
	// =====================================================================
	/// <summary>
	/// Adianta o cabecote pro fim e espera o `Finished` de verdade. DUAS VEZES: uma prova que o
	/// encadeamento existe, duas provam que ele nao e um tiro unico -- e as duas juntas provam que
	/// ele funciona nos DOIS tocadores, ja que cada cruzamento troca de tocador.
	/// </summary>
	private void DeixarAFaixaAcabar()
	{
		if (_esperandoFim)
		{
			// O MARCO E GRAVADO NA HORA DE ADIANTAR, e nao lido de novo aqui. Ele estava sendo relido
			// no topo do metodo, ou seja "nada mudou desde agora" -- uma condicao que e verdadeira
			// sempre e que reprovaria o encadeamento por 12s de espera que nunca terminam.
			if (_diario.Count == _marcoDoFim && _t < 12) return;
			if (_diario.Count == _marcoDoFim)
			{
				Conferir(false, "a faixa de combate ADIANTADA ao fim disparou o `Finished` em 12s "
							  + "(sem isso nenhum roteiro de fim natural pode ser medido)");
				Ir(99);
				return;
			}

			Troca t = _diario[^1];
			_esperandoFim = false;
			_encadeou++;
			Conferir(t.Camada == AudioDirector.Camada.Combate && !t.Silencio,
					 $"faixa {_encadeou} acabou com a tag de pe -> entrou OUTRA de combate: `{t.Nome}`");
			// "ALGUMA COISA TOCA" NAO E A REGRA. A camada `Combate` diz quem PEDIU, e nada impede um
			// pedido de combate carregando o arquivo errado -- foi um caminho de menu tocando por
			// engano que abriu esta tarefa. Entao a checagem pergunta pelo ARQUIVO, contra a pasta
			// lida do disco, e nao pelo rotulo da camada.
			Conferir(EhDeCombate(t.Faixa), $"...e ela e MESMO da lista de combate (`{t.Nome}` esta em `battle ost`)");
			Conferir(!EhDeMenu(t.Faixa), $"...e nao e uma faixa de menu");
			Conferir(t.Faixa != _faixaDeCombate,
					 $"...e e uma faixa DIFERENTE da que acabou (`{_faixaDeCombate.GetFile()}` -> `{t.Nome}`)");
			Conferir(t.Motivo.Contains("encadeia"), $"...pelo motivo certo: {t.Motivo}");
			_faixaDeCombate = t.Faixa;

			if (_encadeou < 2) { Ir(3); return; }

			Despejar("ROTEIRO 2 -- combate encadeando");
			Nota("===== ROTEIRO 3 -- a TAG CAI: entra o que a ZONA pedir, e FICA nisso =====");
			Marcar();
			_socando = false;   // para de socar: a tag tem que cair sozinha, no relogio do `World`
			SairDaMente();      // e o clone tem que sumir: golpe RECEBIDO tambem levanta a tag
			Nota($"parei de socar aos {Time.GetTicksMsec() / 1000.0:0.0}s -- a tag dura "
			   + $"{Jandirus.Core.Combat.CombatKnobs.TagDeCombate:0}s a partir do ultimo golpe");
			Ir(4);
			return;
		}

		// O CRUZAMENTO PRECISA ASSENTAR ANTES. Adiantar no mesmo quadro em que a faixa nova entrou
		// mexeria no cabecote no meio do fade de 1,2 s, com os dois tocadores no ar -- e e exatamente
		// a situacao que a guarda `quem != Atual()` existe pra desempatar. Medir a regra e uma coisa;
		// medi-la sempre no pior instante e outra.
		if (_t < 1.5) return;
		if (AudioDirector.Instance is not { } audio) return;
		if (!audio.AdiantarParaOFimDeTeste())
		{
			Conferir(false, "deu pra adiantar a faixa de combate pro fim (tocador parado ou fluxo sem duracao)");
			Ir(99);
			return;
		}
		_esperandoFim = true;
		_marcoDoFim = _diario.Count;
		_t = 0;
	}

	private bool _esperandoFim;

	// =====================================================================
	// 5. A TAG CAI SOZINHA -> ENTRA O QUE A ZONA PEDIR
	// =====================================================================
	/// <summary>
	/// ============================ ENCADEAR ENQUANTO SE ESPERA NAO E DEFEITO, E A REGRA 1 ============================
	/// A primeira versao lia "a primeira troca depois que parei de socar" e reprovou o jogo. Estava
	/// errada, e o numero que a corrigiu saiu desta propria rodada: as faixas da `battle ost` duram
	/// ~46 s e a tag dura 90 -- entao UMA tag comporta duas faixas, e a segunda entra sozinha aos
	/// 55 s enquanto ninguem esta socando. Isso e exatamente o que o dono pediu ("enquanto a tag
	/// estiver de pe, entra OUTRA de combate"), so que acontecendo na janela de espera.
	/// ==========================================================================================================
	///
	/// ============================ E O QUE SE PROCURA E O MOTIVO, E NAO O SILENCIO ============================
	/// A varredura parava na primeira troca MUDA, o que so funciona num planeta sem tema de lugar: no
	/// Inferno a tag caindo devolve o `Demon World` e a linha muda nunca vem -- a bancada varria o
	/// diario inteiro, estourava o limite e reprovava o jogo com *"a tag caiu sozinha em 120s sem
	/// ninguem socar"*, que e uma frase falsa sobre uma tag que caiu na hora certa.
	///
	/// O que separa a troca que interessa das outras nao e o silencio dela: e QUEM a escreveu. O
	/// `World._Process` assina `"a tag de combate CAIU"` ao chamar `PararCamada` -- entao a busca e
	/// por essa assinatura, e o que entrou no lugar fica pro <see cref="ConferirOQueSobra"/> julgar.
	/// ====================================================================================================
	/// </summary>
	private void DeixarATagCair()
	{
		double limite = Jandirus.Core.Combat.CombatKnobs.TagDeCombate + 30;
		List<Troca> l = Desde();

		for (; _lidas < l.Count; _lidas++)
		{
			if (l[_lidas].Motivo.Contains("tag de combate CAIU")) break;
			Nota($"  --     (tag ainda de pe aos {l[_lidas].T:0.0}s: encadeou `{l[_lidas].Nome}`)");
		}

		if (_lidas >= l.Count)
		{
			if (_t > limite) { Conferir(false, $"a tag caiu sozinha em {limite:0}s sem ninguem socar"); Ir(99); }
			return;
		}

		Troca t = l[_lidas];
		Conferir(true, $"a tag caiu sozinha, no relogio do `World`: `{t.Nome}` <- {t.Motivo}");
		ConferirOQueSobra(t, "a tag caiu");
		// `0.0` e nao `0.1`: em formato numerico do .NET so `0` e `#` sao marcadores, entao "0.1"
		// imprimia o inteiro, o ponto e um literal "1" -- e 94,1s saiu no relatorio como "941s".
		Nota($"  --     a musica de combate ficou no ar {t.T - _tagSubiuEm:0.0}s "
		   + $"(o primeiro soco poe a tag de {Jandirus.Core.Combat.CombatKnobs.TagDeCombate:0}s no ar e cada "
		   + "soco seguinte a renova; o relogio so comeca a valer no ULTIMO)");
		Nota("  --     agora fico 45s sem tocar em nada. QUALQUER linha nova aqui e a queixa do dono.");

		_marcoDoFim = _marco + _lidas + 1;   // a primeira troca DEPOIS da queda da tag, em indice absoluto
		Ir(5);
	}

	/// <summary>Quantas trocas deste roteiro ja foram lidas. Ver <see cref="DeixarATagCair"/>.</summary>
	private int _lidas;

	// =====================================================================
	// 6. E DEPOIS DE PARAR, FICOU PARADO? (a queixa, literal)
	// =====================================================================
	/// <summary>
	/// O SUSTENTO VALE NOS DOIS LADOS, e por isso a contagem nao mudou de forma: numa zona sem tema
	/// nada pode assumir o tocador, e numa zona COM tema o pedido de `Lugar` toca EM LACO -- laco nao
	/// escreve troca nenhuma (ver `AudioDirector.EmLaco`). Zero trocas e a resposta certa dos dois
	/// jeitos; o que muda e o que se ouve, e disso cuida o <see cref="ConferirOArDaZona"/>.
	/// </summary>
	private void OQueSobraSeSustenta()
	{
		if (_t < 45) return;

		// TUDO O QUE VEIO DEPOIS e intruso -- e nada devia ter vindo. E esta a pergunta do dono: nao
		// "parou?", e "e depois de parar, ficou parado?".
		int intrusos = _diario.Count - _marcoDoFim;
		Conferir(intrusos == 0, $"45s depois de a tag cair, NADA assumiu o tocador ({intrusos} troca(s) depois dela)");
		for (int i = _marcoDoFim; i < _diario.Count; i++)
			Nota($"  --     INTRUSO: {_diario[i].T:0.00}s `{_diario[i].Nome}` camada={_diario[i].Camada} <- {_diario[i].Motivo}");
		ConferirOArDaZona("45s depois da tag cair");

		Despejar("ROTEIRO 3 -- a tag caindo e os 45s seguintes");

		Nota("===== ROTEIRO 4 -- TRANSFORMACAO FORA DA BRIGA: a faixa acaba e nao entra nada por cima =====");
		Marcar();
		Ir(6);
	}

	// =====================================================================
	// 7. A TRANSFORMACAO
	// =====================================================================
	private void Transformar()
	{
		if (!_cenaPedida)
		{
			if (!EstrearForma("ssj1")) { Ir(99); return; }
			_cenaPedida = true;
			_t = 0;
			return;
		}

		if (_esperandoFim) { FimDaFormaChegou(); return; }

		Troca? cena = UltimaDe(AudioDirector.Camada.Transformacao);
		if (cena is not { } c)
		{
			if (_t > 12) { Conferir(false, "a cena de transformacao poe o tema dela no ar"); Ir(99); }
			return;
		}

		if (_t < 1.5) return;   // deixa o cruzamento assentar antes de mexer no cabecote
		Conferir(true, $"a cinematica poe o tema no ar: `{c.Nome}` (camada {c.Camada})");
		if (AudioDirector.Instance is not { } audio || !audio.AdiantarParaOFimDeTeste())
		{
			Conferir(false, "deu pra adiantar o tema da transformacao pro fim");
			Ir(99);
			return;
		}
		_esperandoFim = true;
		_marcoDoFim = _diario.Count;
		_t = 0;
	}

	private bool _cenaPedida;
	private int _marcoDoFim;

	private void FimDaFormaChegou()
	{
		if (_diario.Count == _marcoDoFim)
		{
			if (_t > 12) { Conferir(false, "o tema da transformacao adiantado disparou o `Finished`"); Ir(99); }
			return;
		}

		Troca t = _diario[^1];
		_esperandoFim = false;
		_cenaPedida = false;

		// FORA DE COMBATE NAO SOBROU ACONTECIMENTO NENHUM, e e a metade LITERAL da regra do dono
		// (*"no momento q ela acabar N TOCA MAIS NADA"*). O que fica embaixo e o pedido PERMANENTE da
		// zona -- nada numa zona sem tema, o tema do lugar numa que tenha --, e nunca uma faixa nova.
		ConferirOQueSobra(t, "o tema da transformacao acabou FORA da briga");
		Conferir(t.Motivo.Contains("apaga os passageiros"), $"...pelo motivo certo: {t.Motivo}");
		Nota("  --     agora fico 45s sem tocar em nada.");
		_marcoDoFim = _diario.Count;
		Ir(7);
	}

	// =====================================================================
	// 8. O QUE SOBRA DA FORMA TAMBEM SE SUSTENTA
	// =====================================================================
	/// <summary>
	/// O CONTRA-EXEMPLO DO "SEMPRE VOLTAR ALGUMA COISA". O roteiro 5 tranca que a faixa da forma
	/// acabando DENTRO da briga devolve o combate -- e uma maquina que devolvesse algo sempre passaria
	/// naquele roteiro e desfaria a regra do dono do outro lado. Aqui a mesma faixa acaba com a briga
	/// ja encerrada, e o relogio anda BASTANTE (45 s, os mesmos do roteiro 3) porque a afirmacao e
	/// sobre o que NAO acontece depois.
	///
	/// A janela conta do instante em que o tema acabou (`_marcoDoFim`, gravado no
	/// <see cref="FimDaFormaChegou"/>) e nao "da primeira linha muda do roteiro": procurar a linha
	/// muda era outra copia da suposicao de que zona nao tem tema.
	/// </summary>
	private void OQueSobraDaFormaSeSustenta()
	{
		if (_t < 45) return;

		int intrusos = _diario.Count - _marcoDoFim;
		for (int i = _marcoDoFim; i < _diario.Count; i++)
			Nota($"  --     INTRUSO: {_diario[i].T:0.00}s `{_diario[i].Nome}` <- {_diario[i].Motivo}");
		Conferir(intrusos == 0, $"45s depois, o que sobrou da transformacao se sustenta ({intrusos} intruso(s))");
		ConferirOArDaZona("45s depois do tema da forma acabar");

		Despejar("ROTEIRO 4 -- transformacao fora da briga e os 45s seguintes");

		Nota("===== ROTEIRO 5 -- O CRUZAMENTO: transformar DENTRO da briga =====");
		Nota("  --     (regra do dono: ao acabar o tema da forma, VOLTA PRA MUSICA DE COMBATE.)");
		Marcar();
		EntrarNaMente();   // preciso do clone de volta pra ter uma briga em que me transformar
		_socando = true;
		_proximoSoco = 0;
		Ir(8);
	}

	// =====================================================================
	// 9. O CRUZAMENTO -- transformar com a tag de combate de pe
	// =====================================================================
	private void OCruzamento()
	{
		if (_esperandoFim) { FimDoCruzamento(); return; }

		if (!_cenaPedida)
		{
			// primeiro a tag: sem ela nao ha cruzamento nenhum, so uma transformacao sozinha
			if (UltimaDe(AudioDirector.Camada.Combate) is not { } c)
			{
				if (_t > 40) { Conferir(false, "a trilha de batalha voltou ao ar pro roteiro 5"); Ir(99); }
				return;
			}
			Conferir(true, $"a briga recomecou e a trilha de batalha voltou: `{c.Nome}`");
			_faixaAntesDoCruzamento = c.Faixa;   // pra saber, depois, se a que VOLTA e esta mesma
			if (!EstrearForma("ssj2")) { Ir(99); return; }
			_cenaPedida = true;
			_t = 0;
			return;
		}

		if (UltimaDe(AudioDirector.Camada.Transformacao) is not { } cena)
		{
			if (_t > 12) { Conferir(false, "a cena do roteiro 5 poe o tema dela no ar por cima do combate"); Ir(99); }
			return;
		}

		if (_t < 1.5) return;
		Conferir(true, $"a transformacao INTERROMPE a trilha de batalha: `{cena.Nome}`");
		if (AudioDirector.Instance is not { } audio || !audio.AdiantarParaOFimDeTeste())
		{
			Conferir(false, "deu pra adiantar o tema do cruzamento pro fim");
			Ir(99);
			return;
		}
		_esperandoFim = true;
		_marcoDoFim = _diario.Count;
		_t = 0;
	}

	private void FimDoCruzamento()
	{
		if (_diario.Count == _marcoDoFim)
		{
			if (_t > 12) { Conferir(false, "o tema do cruzamento adiantado disparou o `Finished`"); Ir(99); }
			return;
		}

		Troca t = _diario[^1];
		_esperandoFim = false;

		// ============================ O DONO DESEMPATOU AS DUAS REGRAS, E E ISTO QUE SE TRANCA ============================
		// A regra 2 ("acabou a transformacao, nao toca mais nada") e a regra 1 ("enquanto a tag estiver
		// de pe, so tocam faixas de combate") mandavam coisas OPOSTAS neste instante, e por um tempo
		// valeu a LITERAL -- a 2 --, com o jogador socando calado o resto da tag. Perguntado, ele
		// escolheu: *"ao terminar a musica de transformacao, VOLTA PRA MUSICA DE COMBATE"*. Nao ha mais
		// escolha em aberto aqui, ha uma regra -- e o que este roteiro tranca e ela.
		// ==========================================================================================================
		Conferir(!t.Silencio && t.Camada == AudioDirector.Camada.Combate,
				 $"o tema da forma acabou com a tag de pe -> VOLTOU a musica de combate (entrou `{t.Nome}`)");
		Conferir(EhDeCombate(t.Faixa), $"...e ela e MESMO de `battle ost` (`{t.Nome}`)");
		Conferir(t.Motivo.Contains("apaga os passageiros"), $"...pelo motivo certo: {t.Motivo}");

		// MEDIDO E RELATADO, NAO JULGADO. O pedido de `Combate` guarda um CAMINHO, e quem sobrevive ao
		// fim do tema da forma e o caminho -- nao um sorteio novo. Entao a faixa que volta e a MESMA que
		// a transformacao interrompeu, recomecando do ZERO (o `Cruzar` da `Play()`, nao `Seek`). Se o
		// dono quiser uma faixa nova aqui, e uma linha no `AoTerminar`; ate ele dizer, isto so se anota.
		Nota(t.Faixa == _faixaAntesDoCruzamento
			? $"  --     MEDIDO: e a MESMA faixa que a forma interrompeu (`{t.Nome}`), do inicio -- "
			+ "o pedido de `Combate` guardava o caminho, e nada sorteou de novo."
			: $"  --     MEDIDO: e uma faixa DIFERENTE da interrompida "
			+ $"(`{_faixaAntesDoCruzamento.GetFile()}` -> `{t.Nome}`).");

		Nota("  --     agora continuo socando 25s: a musica tem que CONTINUAR, e nao cair em silencio.");
		_marcoDoFim = _diario.Count;
		Ir(9);
	}

	/// <summary>A faixa de batalha que a transformacao do roteiro 5 interrompeu. Ver <see cref="FimDoCruzamento"/>.</summary>
	private string _faixaAntesDoCruzamento = "";

	// =====================================================================
	// 10. O QUE SOBRA DO CRUZAMENTO -- socando, com o combate no comando ou nao
	// =====================================================================
	private void OQueSobraDoCruzamento()
	{
		if (_t < 25) return;

		// ============================ PERDER O COMANDO E O DEFEITO AGORA, E NAO SO CALAR ============================
		// O que reprova aqui nao e "houve trocas": encadear e a regra 1, e 25 s de briga cabem uma
		// virada de faixa. O que reprova e o combate DEIXAR DE MANDAR, e o silencio e so a FORMA que
		// isso toma numa zona sem tema de lugar: numa zona com tema, o mesmo defeito (o pedido de
		// `Combate` apagado junto com o da forma) devolveria o tema do lugar -- barulhento, e invisivel
		// pra uma checagem que so procura linha muda.
		//
		// HOJE ISSO NAO ACONTECE, e a distincao esta escrita por precaucao e nao por medida: a briga
		// destes roteiros mora na MENTE (`Interdimension`), que nao tem tema, e com o defeito injetado
		// de proposito a linha que entra e `SILENCIO` nas DUAS rodadas -- inclusive na de Demon. Ela
		// deixa de ser precaucao no dia em que houver briga com adversario num planeta com tema.
		//
		// Entao o que se conta e toda troca que caiu ABAIXO de `Combate`, muda ou nao. E o
		// `TocandoDeTeste` responde a outra metade, porque um `Calar()` que parasse so um tocador
		// tambem nao escreveria linha nenhuma.
		// ==========================================================================================================
		var perdidas = new List<Troca>();
		for (int i = _marcoDoFim; i < _diario.Count; i++)
		{
			if (_diario[i].Silencio || _diario[i].Camada < AudioDirector.Camada.Combate) perdidas.Add(_diario[i]);
			else Nota($"  --     (ainda em briga aos {_diario[i].T:0.0}s: `{_diario[i].Nome}`)");
		}

		Conferir(perdidas.Count == 0,
				 perdidas.Count == 0
					? "25s socando depois do cruzamento e o COMBATE nunca perdeu o tocador (a trilha de batalha seguiu)"
					: $"25s socando depois do cruzamento sem o combate perder o tocador ({perdidas.Count} queda(s) "
					+ $"pra `{perdidas[0].Dono}`: `{perdidas[0].Nome}` -- e a volta do 'socar calado o resto da tag')");

		if (AudioDirector.Instance is { } audio)
		{
			Conferir(audio.TocandoDeTeste, "...e ha som SAINDO dos tocadores 25s depois do cruzamento");
			Conferir(audio.CamadaDeTeste == AudioDirector.Camada.Combate,
					 $"...e quem manda e a camada `Combate` (esta `{audio.CamadaDeTeste}`)");
		}

		Despejar("ROTEIRO 5 -- o cruzamento e os 25s socando depois dele");

		// SIGO SOCANDO, DE PROPOSITO. O roteiro 6 precisa da tag DE PE pra abrir o ESC por baixo dela,
		// e ela ja esta -- pedir outra briga so pra isso seria jogar fora a que esta no ar.
		Nota("===== ROTEIRO 6 -- a TAG CAI com o ESC JA ABERTO =====");
		Nota("  --     (a segunda metade da frase do dono: *\"a nao ser q abra o menu q tenha a musica do menu\"*)");
		Marcar();
		Ir(10);
	}

	// =====================================================================
	// 11. A TAG CAI COM O ESC JA ABERTO -- o caso que a resposta do dono acrescentou
	// =====================================================================
	/// <summary>
	/// A TAG DE COMBATE CAI COM O PAINEL DE PAUSE JA ABERTO.
	///
	/// ============================ NINGUEM TINHA EXERCITADO ESTE CAMINHO ============================
	/// A frase do dono termina em *"acabou a tag, as musicas PARAM (a nao ser q abra o menu q tenha a
	/// musica do menu)"* -- e o parenteses e um caso inteiro que nenhum roteiro tocava. O roteiro 7
	/// abre o ESC com a briga JA ACABADA, e as checagens do `--diagforma` chamam `PararCamada(Menu)` e
	/// `PararCamada(Combate)` na mao e em cima da hora. Nenhum dos dois responde a pergunta de verdade:
	/// *o pedido de `Menu`, estacionado por baixo do combate durante toda a briga, ainda esta la e
	/// inteiro quando a tag cai sozinha 90 s depois?*
	///
	/// E uma pergunta sobre SOBREVIVENCIA, e o desenho diz que sim (`Menu` e permanente, e o
	/// `AoTerminar` so mexe nos passageiros). Mas era exatamente esse "o desenho diz que sim" que
	/// segurava a faixa de menu tocando em laco no meio da luta -- o defeito que abriu esta tarefa era
	/// um pedido estacionado que ninguem tinha ido conferir. Entao aqui se mede.
	/// ==========================================================================================
	///
	/// E O IRMAO, na mesma janela: fechar o ESC DEPOIS disso devolve o tocador a ZONA -- silencio num
	/// planeta sem tema, o tema do lugar num que tenha (ver <see cref="ConferirOQueSobra"/>). A tag ja
	/// caiu, entao o `Menu` era o ultimo pedido de pe.
	/// </summary>
	private void ATagCaiComOEscAberto()
	{
		if (GetTree().Root.FindChild("Pause", true, false) is not PauseMenu pause)
		{
			Conferir(false, "achei o menu de pause na arvore (roteiro 6)");
			Ir(99);
			return;
		}

		// --- 1. abre o ESC COM A TAG DE PE ---------------------------------------------------
		if (!_escAbertoNaBriga)
		{
			_marcoDoFim = _diario.Count;
			pause.Abrir();
			_escAbertoNaBriga = true;
			Nota("abri o ESC com a tag de combate DE PE -- o pedido de `Menu` fica estacionado embaixo");
			_t = 0;
			return;
		}

		// --- 2. o menu NAO pode roubar o tocador do combate -----------------------------------
		if (!_conferiOEscNaBriga)
		{
			if (_t < 1.5) return;
			_conferiOEscNaBriga = true;

			var roubou = new List<Troca>();
			for (int i = _marcoDoFim; i < _diario.Count; i++)
				if (_diario[i].Camada == AudioDirector.Camada.Menu && !_diario[i].Silencio) roubou.Add(_diario[i]);

			Conferir(roubou.Count == 0,
					 roubou.Count == 0
						? "o ESC aberto NAO rouba o tocador da briga (menu perde de combate)"
						: $"o ESC aberto nao rouba o tocador da briga (entrou `{roubou[0].Nome}`)");
			if (AudioDirector.Instance is { } aud)
				Conferir(aud.CamadaDeTeste == AudioDirector.Camada.Combate,
						 $"...e quem manda continua sendo `Combate` (esta `{aud.CamadaDeTeste}`)");

			// agora deixo a tag cair sozinha, no relogio do `World`, com o painel aberto o tempo todo
			_socando = false;
			SairDaMente();   // golpe RECEBIDO tambem renova a tag: o clone tem que sumir
			_marcoDoFim = _diario.Count;
			Nota($"parei de socar com o ESC aberto -- a tag dura "
			   + $"{Jandirus.Core.Combat.CombatKnobs.TagDeCombate:0}s a partir do ultimo golpe");
			_t = 0;
			return;
		}

		// --- 3. a tag cai sozinha -> o MENU assume ------------------------------------------
		if (!_tagCaiuComEsc)
		{
			// ENCADEAR ENQUANTO SE ESPERA NAO E DEFEITO, e a regra 1 -- mesmo motivo escrito no
			// `DeixarATagCair`: a tag dura 90s e as faixas ~46s, entao uma vira sozinha na espera.
			for (; _marcoDoFim < _diario.Count; _marcoDoFim++)
			{
				if (_diario[_marcoDoFim].Motivo.Contains("tag de combate CAIU")) break;
				Nota($"  --     (tag ainda de pe aos {_diario[_marcoDoFim].T:0.0}s: `{_diario[_marcoDoFim].Nome}`)");
			}

			if (_marcoDoFim >= _diario.Count)
			{
				double limite = Jandirus.Core.Combat.CombatKnobs.TagDeCombate + 30;
				if (_t > limite) { Conferir(false, $"a tag caiu sozinha em {limite:0}s com o ESC aberto"); Ir(99); }
				return;
			}

			Troca t = _diario[_marcoDoFim];
			_tagCaiuComEsc = true;

			Conferir(!t.Silencio && t.Camada == AudioDirector.Camada.Menu,
					 $"a tag caiu com o ESC aberto -> o MENU assumiu sozinho: `{t.Nome}` <- {t.Motivo}");
			Conferir(EhDeMenu(t.Faixa), $"...e a faixa e MESMO de `Menu ost` (`{t.Nome}`)");
			if (AudioDirector.Instance is { } aud)
				Conferir(aud.TocandoDeTeste, "...e ha som SAINDO de verdade (nao caiu no silencio)");

			_marcoDoFim = _diario.Count;
			_t = 0;
			return;
		}

		// --- 4. o irmao: fechar o ESC agora devolve o tocador a ZONA -----------------------
		if (_t < 1.5) return;

		int antes = _diario.Count;
		pause.Fechar();
		if (_diario.Count > antes) ConferirOQueSobra(_diario[^1], "fechar o ESC com a tag ja caida");
		else Conferir(false, "fechar o ESC com a tag ja caida muda o tocador (nada mudou -- alguem ficou pedindo)");
		ConferirOArDaZona("com o ESC fechado e a tag ja caida");

		Despejar("ROTEIRO 6 -- a tag caindo com o ESC aberto");

		Nota("===== ROTEIRO 7 -- o MENU ainda toca musica de menu? =====");
		Marcar();
		Ir(11);
	}

	private bool _escAbertoNaBriga, _conferiOEscNaBriga, _tagCaiuComEsc;

	// =====================================================================
	// 12. O MENU -- a regressao que este conserto poderia ter causado
	// =====================================================================
	/// <summary>
	/// O ESC no meio do jogo, com a briga acabada, poe o tema do menu no ar -- e fechar o tira.
	/// Passa pelo `PauseMenu` DE VERDADE (`Abrir`/`Fechar`) e nao por `Musica(...)` na mao: o defeito
	/// original era justamente uma ponta escrevendo o pedido e a outra nao o apagando.
	///
	/// ============================ A TELA DE LOGIN NAO SE MEDE DAQUI ============================
	/// A primeira versao procurava a faixa do login DENTRO deste diario e reprovava -- todo quadro,
	/// 39 vezes. Ela nao podia estar la: este robo nasce no `AoEntrarNoMundo`, ou seja DEPOIS do
	/// login, e um diario que comeca no meio nunca vai conter o comeco.
	///
	/// Quem responde essa pergunta e o `--logtrilha`, cuja PRIMEIRA linha em toda rodada e
	/// `0,57s <faixa do Menu ost> camada=Menu laco <- tela de login montada`. O que cabe aqui e o
	/// que sustenta aquilo: que a pasta de menu tem faixa pra sortear.
	/// ========================================================================================
	/// </summary>
	private void OMenuAindaToca()
	{
		if (_t < 1) return;

		if (GetTree().Root.FindChild("Pause", true, false) is not PauseMenu pause)
		{
			Conferir(false, "achei o menu de pause na arvore");
			Ir(99);
			return;
		}

		if (!_abriu)
		{
			Conferir(Trilha.Menu().Length > 0, "a pasta `Menu ost` tem faixa (e o que a tela de login sorteia)");
			// O MARCO VEM ANTES DO GESTO. Ele estava DEPOIS do `Abrir()`, entao ja contava a troca que
			// o proprio `Abrir` acabara de escrever -- e a janela "o que aconteceu depois de abrir"
			// nascia vazia. A bancada reprovou o ESC com a faixa do menu no log, uma linha acima.
			_marcoDoFim = _diario.Count;
			pause.Abrir();
			_abriu = true;
			_t = 0;
			return;
		}

		if (_t < 1.5) return;

		List<Troca> l = _diario.GetRange(_marcoDoFim, _diario.Count - _marcoDoFim);
		Conferir(l.Count > 0 && l[0].Camada == AudioDirector.Camada.Menu && !l[0].Silencio,
				 l.Count > 0 ? $"ESC fora da briga poe o tema do menu no ar: `{l[0].Nome}`"
							 : "ESC fora da briga poe o tema do menu no ar (nao entrou nada)");
		// O CONTRA-EXEMPLO PRECISA SER TAO ESPECIFICO QUANTO A REGRA. "Entrou alguma coisa na camada
		// Menu" ficaria verde com o tema do Inferno tocando -- e todo o resto desta bancada afirma
		// AUSENCIA de musica. Sem uma unica afirmacao de que uma faixa CERTA soa, um conserto que
		// emudecesse o jogo inteiro passaria com o placar limpo.
		Conferir(l.Count > 0 && EhDeMenu(l[0].Faixa), "...e ela e MESMO da pasta `Menu ost`");
		if (AudioDirector.Instance is { } tocando)
			Conferir(tocando.TocandoDeTeste, "...e ha som SAINDO de verdade (os roteiros 3 e 4 so provam ausencia)");

		int antes = _diario.Count;
		pause.Fechar();
		if (_diario.Count > antes) ConferirOQueSobra(_diario[^1], "fechar o ESC fora da briga");
		else Conferir(false, "fechar o ESC fora da briga o TIRA do ar (nada mudou -- o pedido do menu ficou pendurado)");

		Despejar("ROTEIRO 7 -- o menu");
		Ir(99);
	}

	private bool _abriu;

	// =====================================================================
	private void Fechar()
	{
		_acabou = true;
		_socando = false;
		Nota("");
		// A ZONA VAI NO RODAPE porque e a UNICA coisa que muda entre a rodada de Saiyan e a de Demon,
		// e sem ela os dois relatorios sao indistinguiveis a olho -- duas paginas de "ok" que parecem
		// a mesma medicao feita duas vezes. E o `Trilha.MusicaDe` desta linha e o que responde por que
		// as linhas de dentro dizem `SILENCIO` numa e `Demon World.mp3` na outra.
		Nota($"===== esta rodada correu em `{NomeDaZona()}`, cujo tema de lugar e "
		   + (_temaDoBerco.Length == 0 ? "NENHUM =====" : $"`{_temaDoBerco.GetFile()}` ====="));
		Nota("");
		Nota("===== DIARIO COMPLETO DA SESSAO =====");
		foreach (Troca t in _diario)
			Nota($"  {t.T,8:0.00}s  {t.Nome,-46}  {t.Dono,-14} <- {t.Motivo}");
		Nota("");
		Nota(_falhas.Count == 0
			? "===== BANCADA DA TRILHA: TUDO OK ====="
			: $"===== BANCADA DA TRILHA: {_falhas.Count} FALHA(S) =====\n[trilha-bancada]   "
			  + string.Join("\n[trilha-bancada]   ", _falhas));
		GetTree().Quit();
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	/// <summary>A ultima troca DESTE roteiro nesta camada, se houve alguma.</summary>
	private Troca? UltimaDe(AudioDirector.Camada camada)
	{
		List<Troca> l = Desde();
		for (int i = l.Count - 1; i >= 0; i--)
			if (l[i].Camada == camada && !l[i].Silencio) return l[i];
		return null;
	}

	/// <summary>
	/// ESTREIA UMA FORMA pelo caminho por onde o pacote do servidor entra (`World.AoMudarForma` com
	/// <see cref="Jandirus.Core.Forms.DegrauDeCena.Estreia"/>) -- a mesma escolha do `RoboDeCena`, e
	/// pelo mesmo motivo: uma cena montada na mao provaria que o tocador toca, nao que o JOGO a toca.
	///
	/// Passa pela BASE antes pra segunda cena nao chegar com `de == para`.
	/// </summary>
	private bool EstrearForma(string id)
	{
		if (Mundo is not { } mundo || C is not { } cli) return false;
		if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } def)
		{
			Conferir(false, $"a forma `{id}` existe no catalogo");
			return false;
		}
		if (Jandirus.Core.Forms.Cinematicas.Para(def) is not { Musica.Length: > 0 } cena)
		{
			Conferir(false, $"a cena de `{id}` tem musica (sem musica nao ha o que medir)");
			return false;
		}

		ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);
		mundo.AoMudarForma(cli.LocalId, b, b, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.AoMudarForma(cli.LocalId, b, def.IdRede, Jandirus.Core.Forms.DegrauDeCena.Estreia);
		Nota($"estreando `{id}` -- tema `{cena.Musica.GetFile()}`, cena de {cena.Segundos:0}s");
		return true;
	}
}
