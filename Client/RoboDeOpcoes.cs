using Godot;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DAS OPCOES E DA JANELA (`--diagopcoes`) ============================
/// O PEDIDO DO DONO, literal: *"adicione tb uma opcao no lobby do jogo de sair do jogo, e abrir a
/// tela de opcoes, pq so da pra fazer dentro do jogo isso e as vezes quero mudar o volume no lobby e
/// n da. e percebi q mesmo colocando fullscreen com uma resolucao menor a o jogo n cobre a tela
/// toda, e quando ta no modo janela o jogo n deveria ter a opcao de colocar na resolucao da minha
/// tela, e sim na resolucao do fullscreen do modo janela (q e um pouco menor q o 1920x1080 da minha
/// tela por exemplo, mas outras resolucoes isso varia)"*.
///
/// ============================ ELA NASCE **DEPOIS** DO LOBBY, E NAO NO LUGAR DELE ============================
/// Este projeto ja pagou por bancada que nasce dentro do estado que devia testar: *"nascer DENTRO do
/// estado nunca testa a ENTRADA nele -- 48 provas verdes e o jogador nao conseguia molhar o pe"*.
/// Entao o `--diagopcoes` **nao** substitui o `_Ready` do `Boot` como as outras bancadas fazem: o
/// login e montado normalmente, a `PauseMenu` e a `TelaDeTeclas` nascem pelo caminho de producao, e
/// so entao este node entra na arvore e vai APERTAR os botoes que o dedo do dono apertaria.
///
/// Nada aqui e reconstruido. Os botoes sao achados varrendo a arvore por TEXTO, como quem olha a
/// tela; se alguem trocar o rotulo, a bancada fica vermelha -- que e o certo, porque o dono procura
/// pelo rotulo tambem.
///
/// ============================ TRES CEGOS QUE ESTA BANCADA FOI FEITA PRA NAO TER ============================
/// A licao ja escrita neste projeto: *"uniform escrito nao e pixel desenhado"*, *"`Modulate` nao e
/// tela"* e *"as duas telas concordam fica verde com as duas erradas igual"*. Traduzido pra ca:
///
///   * o VOLUME nao e conferido no campo do `Settings` -- e lido do MISTURADOR
///     (`AudioServer.GetBusVolumeDb`), que e o unico lugar onde ele vira som;
///   * a TELA CHEIA nao e conferida pelo `ContentScaleSize` que acabamos de escrever -- e contada em
///     PIXELS PRETOS na borda da foto, do mesmo jeito que a fase de medicao contou;
///   * a LISTA DE RESOLUCOES nao e comparada com uma segunda copia da mesma conta -- e cobrada
///     contra a geometria que o SISTEMA devolve (`ScreenGetUsableRect` e a moldura medida).
///
/// E cada familia que mede pixel tem INJECAO: o defeito e reposto nos nodes de verdade e a mesma
/// conta tem que ficar vermelha. Medida que nao sabe olhar nao vale nada verde.
/// ==========================================================================================
///
/// COMO RODAR (janela no SEGUNDO monitor -- o dono trabalha no principal):
///     Godot --path . --diagopcoes --position 1920,0 --resolution 1280x720
///
/// **ELA PRECISA DE JANELA, E NAO E FIGURA DE LINGUAGEM.** Em `--headless` nao ha foto (as familias
/// de pixel se anunciam PULADAS e entram no terceiro placar, ver `ChecaNoPixel`) e nao ha geometria
/// de tela (`ScreenGetUsableRect` devolve 0x0), entao as familias de janela ficam vermelhas por
/// falta de tela e nao por defeito. Medido: 43 OK, 10 FALHA, 4 nao medidos em headless -- um placar
/// que nao quer dizer nada. Rode com janela, ou nao rode.
///
/// ============================ RESIDUO: ZERO, E CONFERIDO POR SHA ============================
/// Ela aperta controles de PRODUCAO, e eles gravam (`PauseMenu.AplicarEGravar` chama
/// `Settings.Gravar`) -- ou seja, ela mexe no `config.json` do dono de verdade. Por isso o
/// <see cref="Guardar"/> copia pra memoria todo arquivo pequeno que este caminho pode tocar
/// (`config.json`, `perfis.json` e a pasta `saves/`) e o fechamento devolve byte a byte, com o
/// SHA256 de antes e de depois impresso lado a lado.
/// ==========================================================================================
/// </summary>
public partial class RoboDeOpcoes : Node
{
	// =====================================================================
	// PLACAR -- dois, como manda a casa: o das provas e o das injecoes
	// =====================================================================
	private int _ok, _falha;
	private readonly List<string> _reprovadas = [];

	private int _injOk, _injFalha;
	private readonly List<string> _injPassouBatido = [];

	private readonly List<string> _fotos = [];

	private static void Nota(string linha) => GD.Print("[opcoes] " + linha);

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou " : "  PASSOU") + "  (injecao) " + oque
			 + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _injPassouBatido.Add(oque); }
	}

	// =====================================================================
	// O TERCEIRO PLACAR: O QUE NAO FOI MEDIDO
	// =====================================================================
	/// <summary>
	/// ============================ SEM JANELA NAO E "PASSOU", E "NAO OLHEI" ============================
	/// As familias de pixel liam a foto e, quando nao havia foto (headless), a conta era escrita
	/// `foto.Vazia || <a medida>` -- ou seja, **a ausencia de janela contava como OK**. Rodar esta
	/// bancada com `--headless` devolvia um placar verde inteiro sem ter olhado um pixel sequer, e o
	/// aviso "sem janela: pulado" ia so no detalhe da linha, onde ninguem le.
	///
	/// E o mesmo defeito que esta bancada existe pra caçar, um andar acima: *"medida que nao sabe
	/// olhar nao vale nada verde"*. Aqui ela nem estava olhando.
	///
	/// Agora sem foto a linha e PULADA e o pulo tem contador proprio, impresso no fim junto do
	/// placar. O placar so pode ser lido como prova do L2 quando essa linha diz zero.
	/// ==========================================================================================
	/// </summary>
	private int _pulados;
	private readonly List<string> _naoMedidos = [];

	/// <summary>Uma prova que so existe se houver foto. <paramref name="temFoto"/> falso = pulada.</summary>
	private void ChecaNoPixel(string oque, bool temFoto, bool passou, string detalhe = "")
	{
		if (!temFoto)
		{
			Nota("  PULADA  " + oque + "   [sem janela: nao ha foto pra medir]");
			_pulados++;
			_naoMedidos.Add(oque);
			return;
		}
		Checa(oque, passou, detalhe);
	}

	/// <inheritdoc cref="ChecaNoPixel"/>
	private void InjetaNoPixel(string oque, bool temFoto, bool ficouVermelha, string detalhe = "")
	{
		if (!temFoto)
		{
			Nota("  PULADA  (injecao) " + oque + "   [sem janela: nao ha foto pra medir]");
			_pulados++;
			_naoMedidos.Add("(injecao) " + oque);
			return;
		}
		Injeta(oque, ficouVermelha, detalhe);
	}

	// =====================================================================
	// OS ARQUIVOS DO DONO
	// =====================================================================
	private readonly Dictionary<string, byte[]> _guardados = [];

	/// <summary>Os arquivos pequenos que este caminho pode reescrever. As fotos das bancadas ficam de fora.</summary>
	private static IEnumerable<string> Alvos()
	{
		string raiz = ProjectSettings.GlobalizePath("user://");
		yield return System.IO.Path.Combine(raiz, "config.json");
		yield return System.IO.Path.Combine(raiz, "perfis.json");
		string saves = System.IO.Path.Combine(raiz, "saves");
		if (!System.IO.Directory.Exists(saves)) yield break;
		foreach (string f in System.IO.Directory.GetFiles(saves)) yield return f;
	}

	private static string Sha(byte[] b) =>
		Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();

	/// <summary>
	/// ESTE ARQUIVO PODE TER SIDO ESCRITO POR MIM? So os que o caminho de saida desta bancada
	/// escreve -- o mundo e os cargos do `GameServer.SalvarEParar`, mais os dois arquivos de
	/// configuracao do cliente. Ver o bloco em <see cref="Devolver"/>: a pasta e compartilhada com as
	/// outras bancadas do projeto, entao "nasceu agora" nao prova autoria.
	/// </summary>
	private static bool MeuPorNome(string caminho) =>
		System.IO.Path.GetFileName(caminho).ToLowerInvariant() is
			"mundo.json" or "cargos.txt" or "esferas.json" or "superesferas.json"
			or "conquista.json" or "titulo.txt" or "sagas.json" or "universo.json"
			or "config.json" or "perfis.json";

	private void Guardar()
	{
		foreach (string f in Alvos())
			if (System.IO.File.Exists(f)) _guardados[f] = System.IO.File.ReadAllBytes(f);
		Nota($"guardei {_guardados.Count} arquivo(s) da pasta do dono pra devolver no fim");
	}

	private void Devolver()
	{
		int mexidos = 0, devolvidos = 0, sobrando = 0;

		foreach ((string caminho, byte[] antes) in _guardados)
		{
			byte[] agora = System.IO.File.Exists(caminho) ? System.IO.File.ReadAllBytes(caminho) : [];
			if (Sha(agora) == Sha(antes)) continue;
			mexidos++;
			System.IO.File.WriteAllBytes(caminho, antes);
			if (Sha(System.IO.File.ReadAllBytes(caminho)) == Sha(antes)) devolvidos++;
			Nota($"  devolvido: {System.IO.Path.GetFileName(caminho)}  ({antes.Length} bytes, "
				 + $"sha {Sha(antes)[..16]}...)");
		}

		// ============================ O QUE ELA CRIOU, ELA TIRA -- E SO O QUE ELA PODE TER CRIADO ============================
		// **MEDIDO**: o `SalvarEParar` da F6 escreve o mundo e os cargos, e numa pasta que nao tinha
		// esses arquivos ele CRIA seis (`mundo.json`, `cargos.txt`, `esferas.json`,
		// `superesferas.json`, `conquista.json`, `titulo.txt`). Deixa-los seria plantar um mundo
		// vazio na pasta do dono, e o proximo servidor que subisse leria esse mundo como se fosse o
		// dele.
		//
		// ============================ POR QUE HA UMA LISTA, E NAO "TUDO QUE APARECEU" ============================
		// A regra era *"apague todo arquivo que nao existia quando eu comecei"*, e ela vinha com uma
		// frase que **nao e verdade nesta maquina**: *"nada mais estava escrevendo ali"*. Escrevia --
		// esta pasta e a mesma pra TODA bancada do projeto, e ha outras rodando ao mesmo tempo (as
		// `--fusaoduplateste` gravam `bancada_fusao2_*.json` aqui dentro enquanto esta roda). Um save
		// de OUTRA bancada nascido no meio desta cairia na peneira do "nao existia antes" e seria
		// apagado por mim, calado.
		//
		// Entao a peneira e por NOME, e o nome vem do que este caminho de saida escreve. O que eu nao
		// escrevo, eu nao apago -- mesmo que tenha nascido agora.
		//
		// **NADA DO DONO E APAGADO**: arquivo que ja existia so e reescrito com o conteudo antigo.
		// ==============================================================================
		foreach (string f in Alvos().ToList())
			if (!_guardados.ContainsKey(f) && System.IO.File.Exists(f) && MeuPorNome(f))
			{
				sobrando++;
				try
				{
					System.IO.File.Delete(f);
					Nota($"  apagado (criado por MIM, nao existia antes): {System.IO.Path.GetFileName(f)}");
				}
				catch (Exception e) { Nota($"  SOBROU (nao consegui apagar): {f} -- {e.Message}"); }
			}
			else if (!_guardados.ContainsKey(f) && System.IO.File.Exists(f))
				Nota($"  DEIXADO (nasceu agora mas nao e meu -- outra bancada): "
					 + System.IO.Path.GetFileName(f));

		int aindaNovos = Alvos().Count(f => !_guardados.ContainsKey(f) && System.IO.File.Exists(f)
										    && MeuPorNome(f));
		Checa("a pasta do dono ficou como estava (arquivos devolvidos, os meus apagados)",
			  mexidos == devolvidos && aindaNovos == 0,
			  $"{mexidos} mexido(s), {devolvidos} devolvido(s), {sobrando} criado(s) por mim, "
			  + $"{aindaNovos} sobrando");

		// O SHA DO ARQUIVO QUE MAIS IMPORTA, impresso pra conferir a olho contra o de antes.
		string cfg = System.IO.Path.Combine(ProjectSettings.GlobalizePath("user://"), "config.json");
		if (System.IO.File.Exists(cfg))
			Nota($"  config.json: {System.IO.File.ReadAllBytes(cfg).Length} bytes, "
				 + $"sha256 {Sha(System.IO.File.ReadAllBytes(cfg))}");
	}

	// =====================================================================
	// O QUE ESTAVA GRAVADO ANTES DE EU MEXER
	// =====================================================================
	private int _l0, _a0, _zoom0;
	private bool _cheia0;
	private float _geral0, _musica0;

	public override void _Ready()
	{
		Guardar();

		Settings c = Boot.Config;
		(_l0, _a0, _cheia0, _zoom0) = (c.LarguraJanela, c.AlturaJanela, c.TelaCheia, c.Zoom);
		(_geral0, _musica0) = (c.VolumeGeral, c.VolumeMusica);

		// ============================ DUAS RODADAS DIFERENTES, E A SEGUNDA NAO VOLTA ============================
		// `--saidareal` e a metade que nao cabe na rodada normal: ela aperta o botao de sair DE
		// VERDADE e deixa o processo MORRER. Nao da pra medir isso e continuar medindo -- a prova de
		// que o processo acabou e o processo ter acabado, e quem confere isso e quem lancou (ver
		// `testar-as-opcoes.bat`: codigo de saida, lista de processos e a porta liberada).
		//
		// A F6 da rodada normal cobre a outra metade -- os EFEITOS da limpeza (servidor gravado e
		// desligado, cliente despedido) -- chamando o mesmo `Saida.Encerrar` com a arvore nula.
		// Nenhuma das duas sozinha prova o pedido inteiro.
		// ==========================================================================================
		_roteiro = (Array.IndexOf(OS.GetCmdlineArgs(), "--saidareal") >= 0 ? SoASaida() : Roteiro())
			.GetEnumerator();
	}

	// =====================================================================
	// A SAIDA DE VERDADE -- o botao do lobby, e o processo morre
	// =====================================================================
	/// <summary>
	/// O PEDIDO: *"o de sair encerra o processo sem deixar nada vivo"*.
	///
	/// Aqui nada e simulado: o botao e o `Sair do jogo` que o `BotoesDoLobby` pendurou na tela de
	/// login, o clique e o sinal `Pressed` que o mouse dispara, e o fim e o `Quit()` de verdade.
	///
	/// **HA UM SERVIDOR NO AR DE PROPOSITO.** Sem ele o caminho de saida so despede o cliente, e as
	/// duas coisas que mais interessam nao aconteceriam: o mundo GRAVADO antes de morrer e a PORTA
	/// devolvida ao sistema. Quem confere a porta e quem lancou -- de dentro do processo que morreu
	/// nao da pra olhar.
	///
	/// E o aviso de host e cobrado no meio: com servidor no ar, o PRIMEIRO clique nao sai -- ele
	/// pergunta. Sair dali derruba todo mundo que esta dentro.
	/// </summary>
	private IEnumerable<double> SoASaida()
	{
		Nota("=========== SO A SAIDA: o botao de verdade, e o processo MORRE ===========");
		Nota($"PID {OS.GetProcessId()}");

		Checa("o LOGIN tem o botao 'Sair do jogo'", Botao(Lobby, "Sair do jogo") != null);
		Checa("o X da janela passa pela mesma porta (`auto_accept_quit` desligado)",
			  !GetTree().AutoAcceptQuit);
		Checa("a bancada conta como sessao de jogador (ela atravessa o caminho COM save)",
			  Boot.SessaoDeJogador);

		if (Botao(Lobby, "Sair do jogo") is not { } sair)
		{
			Nota("PLACAR: 0 OK, 1 FALHA (nao achei o botao)");
			GetTree().Quit(3);
			yield break;
		}

		// ---- um servidor NO AR pra ver a saida derruba-lo ----
		if (Jandirus.Server.GameServer.Instance is not { } srv)
		{
			Checa("o servidor local existe pra ser desligado", false);
			GetTree().Quit(3);
			yield break;
		}

		bool subiu = srv.Running || srv.Start(PortaDaSaida);
		Checa($"subi um servidor local na porta {PortaDaSaida} pra ver a saida derruba-lo", subiu);
		yield return 0.6;

		// ---- 1o clique: com servidor no ar ele PERGUNTA, e nao sai ----
		sair.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.5;
		Checa("com servidor no ar o PRIMEIRO clique PERGUNTA em vez de sair",
			  !Saida.SaindoDeTeste && sair.Text != "Sair do jogo", $"o botao diz '{sair.Text}'");
		Checa("...e o servidor continua de pe enquanto ele so perguntou", srv.Running);

		Nota($"PLACAR PARCIAL: {_ok} OK, {_falha} FALHA");
		Nota($"SAIDA: apertando o botao pela segunda vez -- o processo tem que morrer agora "
			 + $"(porta {PortaDaSaida}, pid {OS.GetProcessId()})");

		// ---- 2o clique: sai de verdade ----
		sair.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 3.0;

		// SE CHEGOU AQUI, NAO MORREU. O codigo de saida diz isso pra quem lancou.
		Nota("FALHA: 3 s depois do clique o processo ainda esta vivo -- a saida nao encerrou nada");
		GetTree().Quit(4);
	}

	/// <summary>A porta do servidor que a <see cref="SoASaida"/> sobe. Longe da 7777 do dono.</summary>
	private const int PortaDaSaida = 7912;

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerator<double>? _roteiro;
	private double _espera = 0.6;

	public override void _Process(double delta)
	{
		if (_roteiro == null) return;
		_espera -= delta;
		if (_espera > 0) return;
		if (!_roteiro.MoveNext()) { _roteiro = null; return; }
		_espera = _roteiro.Current;
	}

	private IEnumerable<double> Roteiro()
	{
		Nota("=========== AS OPCOES NO LOBBY, A TELA CHEIA E A LISTA DE RESOLUCOES ===========");
		Nota($"janela {DisplayServer.WindowGetSize()}  |  moldura medida {Settings.Moldura}  |  "
			 + $"tela {DisplayServer.WindowGetCurrentScreen()} de {DisplayServer.GetScreenCount()}");

		foreach (double d in F1_AsTresTelasDoLobby()) yield return d;
		foreach (double d in F2_AbreSemMundoEOVolumeChega()) yield return d;
		foreach (double d in F3_ATrilhaDoLobbySobrevive()) yield return d;
		foreach (double d in F4_TelaCheiaPreenche()) yield return d;
		foreach (double d in F5_AListaParaNaAreaUtil()) yield return d;
		foreach (double d in F7_AJanelaDeVerdadeCabe()) yield return d;
		foreach (double d in F6_SairLimpo()) yield return d;

		foreach (double d in Fechamento()) yield return d;
	}

	// =====================================================================
	// LER A TELA
	// =====================================================================
	private static IEnumerable<T> Todos<T>(Node raiz) where T : Node
	{
		foreach (Node f in raiz.GetChildren())
		{
			if (f is T t) yield return t;
			foreach (T n in Todos<T>(f)) yield return n;
		}
	}

	/// <summary>
	/// Um botao pelo TEXTO, como quem le a tela. Só conta se estiver VISIVEL de verdade.
	///
	/// **EXATO PRIMEIRO, prefixo depois**, e isso nao e capricho: "Fechar" e "Fechar o jogo" convivem
	/// nesta tela, e uma busca por prefixo acharia o botao errado -- a prova de que o "Voltar ao
	/// jogo" virou "Fechar" ficaria verde apontando pro botao de matar o processo.
	/// </summary>
	private static Button? Botao(Node raiz, string texto, bool soVisivel = true)
	{
		List<Button> vistos = Todos<Button>(raiz).Where(b => !soVisivel || b.IsVisibleInTree()).ToList();
		return vistos.FirstOrDefault(b => string.Equals(b.Text, texto, StringComparison.OrdinalIgnoreCase))
			?? vistos.FirstOrDefault(b => b.Text.StartsWith(texto, StringComparison.OrdinalIgnoreCase));
	}

	private Node Lobby => GetParent();   // o `Boot`: as tres telas do lobby sao filhas dele

	private static PauseMenu? Menu => PauseMenu.Instancia;

	/// <summary>O painel da tela de opcoes -- a raiz de tudo que ela desenha.</summary>
	private static Control? Painel =>
		Menu is { } m ? Todos<ColorRect>(m).FirstOrDefault() : null;

	// =====================================================================
	// F1 -- AS TRES TELAS DO LOBBY TEM AS DUAS PORTAS
	// =====================================================================
	/// <summary>
	/// O LOBBY SAO TRES TELAS E ELAS SE ESCONDEM UMA A OUTRA -- login, selecao de personagem e
	/// criacao. Um botao posto so na primeira desapareceria nas outras duas, e a criacao e onde o
	/// jogador passa mais tempo. Por isso a cobranca e nas TRES, e cada uma e montada pelo caminho
	/// dela: o login e o de producao (o `Boot` acabou de monta-lo), as outras duas sao os nodes de
	/// producao montados como o `Boot` os monta.
	/// </summary>
	private IEnumerable<double> F1_AsTresTelasDoLobby()
	{
		Nota("--- F1: as duas portas nas tres telas do lobby ---");

		Checa("a tela de opcoes existe ANTES de entrar no mundo", Menu != null);
		Checa("a tela de teclas existe ANTES de entrar no mundo", TelaDeTeclas.Instancia != null);
		Checa("nao ha mundo nenhum na tela (e o lobby mesmo)", World.Instancia == null);

		// ---- 1. LOGIN (o de producao, montado pelo `Boot._Ready`) ----
		Checa("LOGIN tem o botao 'Opções'", Botao(Lobby, "Opções") != null);
		Checa("LOGIN tem o botao 'Sair do jogo'", Botao(Lobby, "Sair do jogo") != null);
		Fotografar("user://opcoes-1-login.png");

		// ---- 2. SELECAO ----
		var selecao = new CharacterSelect { Name = "SelecaoDaBancada" };
		GetTree().Root.AddChild(selecao);
		selecao.Mostrar([new Jandirus.Net.SlotInfo(), new Jandirus.Net.SlotInfo(), new Jandirus.Net.SlotInfo()]);
		yield return 0.4;
		Checa("SELECAO tem o botao 'Opções'", Botao(selecao, "Opções") != null);
		Checa("SELECAO tem o botao 'Sair do jogo'", Botao(selecao, "Sair do jogo") != null);
		Checa("SELECAO manteve o 'Trocar de servidor' que ja tinha", Botao(selecao, "Trocar de servidor") != null);
		Fotografar("user://opcoes-6-selecao.png");
		selecao.QueueFree();
		yield return 0.3;

		// ---- 3. CRIACAO ----
		var criacao = new CreationScreen { Name = "CriacaoDaBancada" };
		GetTree().Root.AddChild(criacao);
		yield return 0.5;
		Checa("CRIACAO tem o botao 'Opções'", Botao(criacao, "Opções") != null);
		Checa("CRIACAO tem o botao 'Sair do jogo'", Botao(criacao, "Sair do jogo") != null);
		Checa("CRIACAO manteve o 'Avançar' que ja tinha", Botao(criacao, "Avançar") != null);
		// A CRIACAO E A UNICA DAS TRES QUE GANHOU UMA LINHA NUM PAINEL DE TAMANHO FIXO (470x470 de
		// minimo), entao ela e a unica em que a linha nova pode ter empurrado alguma coisa pra fora.
		// A foto responde; o "o botao existe" acima nao responderia.
		if (Todos<PanelContainer>(criacao).FirstOrDefault() is { } painelCriacao)
			Checa("a CRIACAO continua cabendo na tela depois da linha nova",
				  GetViewport().GetVisibleRect().Encloses(painelCriacao.GetGlobalRect()),
				  $"painel {painelCriacao.GetGlobalRect().Size} em {GetViewport().GetVisibleRect().Size}");
		Fotografar("user://opcoes-7-criacao.png");
		criacao.QueueFree();
		yield return 0.3;
	}

	// =====================================================================
	// F2 -- ABRE SEM MUNDO, E O VOLUME CHEGA NO MISTURADOR
	// =====================================================================
	/// <summary>
	/// O CASO DE USO LITERAL DO DONO: *"as vezes quero mudar o volume no lobby e n da"*.
	///
	/// A prova nao para no campo do `Settings`. Ela le o `AudioServer.GetBusVolumeDb` -- o
	/// barramento de verdade, que e onde o numero vira som. Campo escrito nao e som tocado, do mesmo
	/// jeito que uniform escrito nao e pixel desenhado.
	/// </summary>
	private IEnumerable<double> F2_AbreSemMundoEOVolumeChega()
	{
		Nota("--- F2: as opcoes abrem no lobby e o volume chega ao misturador ---");

		if (Botao(Lobby, "Opções") is not { } abrir) { Checa("achei o botao de abrir", false); yield break; }

		abrir.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.4;

		Checa("apertar 'Opções' no login ABRE a tela", Menu?.Aberto == true);
		Checa("e ela aparece POR CIMA do login (camada 20 contra 1)",
			  Menu is { Layer: > 1 } && Painel?.IsVisibleInTree() == true,
			  $"camada {Menu?.Layer}");

		if (Painel is not { } p) { Checa("achei o painel das opcoes", false); yield break; }

		Checa("o titulo diz OPÇÕES, e nao PAUSA",
			  Todos<Label>(p).Any(l => l.Text == "OPÇÕES"),
			  Todos<Label>(p).FirstOrDefault(l => l.Text is "OPÇÕES" or "PAUSA")?.Text ?? "(nenhum)");
		Checa("'Desconectar' esta ESCONDIDO fora do mundo (ele derrubaria a conexao dos slots)",
			  Botao(p, "Desconectar") == null);
		Checa("'Voltar ao jogo' virou 'Fechar' (nao ha jogo pra voltar)",
			  Botao(p, "Fechar") != null && Botao(p, "Voltar ao jogo") == null);
		Checa("'Configurar teclas' esta VIVO no lobby (nao mudo)",
			  Botao(p, "Configurar teclas") is { Disabled: false });

		// ---- a tela de teclas abre mesmo, sem mundo ----
		if (Botao(p, "Configurar teclas") is { } bt)
		{
			bt.EmitSignal(BaseButton.SignalName.Pressed);
			yield return 0.4;
			Checa("e ela ABRE de verdade sem mundo nenhum", TelaDeTeclas.Digitando || TelaAbertaDeTeclas());
			TelaDeTeclas.Instancia?.Fechar();
			yield return 0.2;
		}

		Fotografar("user://opcoes-2-abertas-no-lobby.png");

		// ---- o volume, medido no barramento ----
		foreach ((string rotulo, string barramento) in new[] { ("Geral", "Master"), ("Musica", "Musica") })
		{
			if (Deslizador(p, rotulo) is not { } s)
			{
				Checa($"achei o deslizador de {rotulo}", false);
				continue;
			}

			int bus = AudioServer.GetBusIndex(barramento);
			MexerNo(s, 0.2f);
			yield return 0.15;
			float baixo = AudioServer.GetBusVolumeDb(bus);

			MexerNo(s, 0.9f);
			yield return 0.15;
			float alto = AudioServer.GetBusVolumeDb(bus);

			Checa($"mexer no volume '{rotulo}' MUDA o barramento '{barramento}' sem mundo nenhum",
				  alto > baixo + 0.5f, $"{baixo:0.0} dB a 20% -> {alto:0.0} dB a 90%");
		}

		// ---- injecao: a medida sabe olhar? ----
		int master = AudioServer.GetBusIndex("Master");
		float antes = AudioServer.GetBusVolumeDb(master);
		AudioServer.SetBusVolumeDb(master, antes);   // ninguem mexeu de verdade
		Injeta("com o barramento PARADO no mesmo dB, a conta de 'mudou' fica vermelha",
			   !(AudioServer.GetBusVolumeDb(master) > antes + 0.5f),
			   $"{antes:0.0} dB -> {AudioServer.GetBusVolumeDb(master):0.0} dB");
	}

	private static bool TelaAbertaDeTeclas() =>
		TelaDeTeclas.Instancia is { } t && Todos<Control>(t).Any(c => c.IsVisibleInTree());

	/// <summary>O deslizador da linha com este rotulo. As linhas sao `HBox(Label, HSlider, Label)`.</summary>
	private static HSlider? Deslizador(Node raiz, string rotulo) =>
		Todos<HBoxContainer>(raiz)
			.Where(h => h.GetChildren().OfType<Label>().Any(l => l.Text == rotulo))
			.Select(h => h.GetChildren().OfType<HSlider>().FirstOrDefault())
			.FirstOrDefault(s => s != null);

	private static void MexerNo(HSlider s, float valor)
	{
		s.Value = valor;
		// `Godot.Range` por extenso: `Range` sozinho colide com o `System.Range` do C#.
		s.EmitSignal(Godot.Range.SignalName.ValueChanged, valor);
	}

	// =====================================================================
	// F3 -- A TRILHA DO LOBBY SOBREVIVE A ABRIR E FECHAR AS OPCOES
	// =====================================================================
	/// <summary>
	/// A ARMADILHA QUE NAO SE VE, achada na fase de medicao: o `Fechar` chamava
	/// `PararCamada(Camada.Menu)`, e **a musica do lobby E um pedido da camada `Menu`** (posto pelo
	/// `Boot._Ready` e pelo `VoltarAoLogin`). Abrir e fechar as opcoes no lobby matava a trilha da
	/// tela de login de vez -- `PararCamada` apaga o pedido e ninguem o repoe --, sem nada na tela
	/// dizendo por que.
	/// </summary>
	private IEnumerable<double> F3_ATrilhaDoLobbySobrevive()
	{
		Nota("--- F3: abrir e fechar as opcoes nao mata a trilha do lobby ---");

		if (AudioDirector.Instance is not { } audio)
		{
			Checa("o diretor de audio existe", false);
			yield break;
		}

		string faixaAntes = audio.FaixaDeTeste;
		Checa("a trilha do lobby esta no ar, na camada Menu",
			  faixaAntes.Length > 0 && audio.CamadaDeTeste == AudioDirector.Camada.Menu,
			  $"'{faixaAntes}' na camada {audio.CamadaDeTeste}");

		Menu?.Fechar("bancada fechou");
		yield return 0.3;
		Checa("depois de FECHAR as opcoes, a trilha do lobby continua de pe",
			  audio.FaixaDeTeste == faixaAntes && faixaAntes.Length > 0,
			  $"antes '{faixaAntes}' -> agora '{audio.FaixaDeTeste}'");

		Menu?.Abrir();
		yield return 0.3;
		Menu?.Fechar("bancada fechou de novo");
		yield return 0.3;
		Checa("e continua depois de abrir e fechar de novo",
			  audio.FaixaDeTeste == faixaAntes, $"agora '{audio.FaixaDeTeste}'");

		// ---- injecao: o defeito de origem, reposto ----
		audio.PararCamada(AudioDirector.Camada.Menu, "injecao da bancada");
		yield return 0.3;
		Injeta("com o `PararCamada(Menu)` de volta (o defeito de origem), a trilha do lobby MORRE",
			   audio.FaixaDeTeste != faixaAntes, $"agora '{audio.FaixaDeTeste}'");

		// devolve o que a injecao tirou -- e o lobby de verdade que fica na tela depois da bancada
		audio.Musica(Trilha.Menu(), AudioDirector.Camada.Menu, "bancada devolveu a trilha");
		yield return 0.3;
	}

	// =====================================================================
	// F4 -- TELA CHEIA PREENCHE A TELA (contado em PIXEL)
	// =====================================================================
	/// <summary>
	/// O SEGUNDO PEDIDO: *"mesmo colocando fullscreen com uma resolucao menor o jogo n cobre a tela
	/// toda"*.
	///
	/// A conta e a mesma da fase de medicao: quantas COLUNAS e LINHAS totalmente pretas sobram nas
	/// quatro bordas da foto. Preto porque o `default_clear_color` do projeto e preto e o fundo das
	/// telas e `Tema.Fundo` (#12141C) -- se o desenho nao cobre a janela, o que sobra e preto puro.
	///
	/// E ela cobra o estado que ANTES ERA INALCANCAVEL: tela cheia **com** uma resolucao menor
	/// escolhida pela lista. Eram dois defeitos que se escondiam um no outro -- em tela cheia a
	/// resolucao era ignorada, e escolher resolucao desligava a tela cheia.
	/// </summary>
	private IEnumerable<double> F4_TelaCheiaPreenche()
	{
		Nota("--- F4: tela cheia com resolucao menor PREENCHE a tela ---");

		Window raiz = GetTree().Root;
		Checa("o `project.godot` esta esticando o canvas (era 'disabled', e o `aspect` era letra morta)",
			  raiz.ContentScaleMode == Window.ContentScaleModeEnum.CanvasItems
			  && raiz.ContentScaleAspect == Window.ContentScaleAspectEnum.Expand,
			  $"mode {raiz.ContentScaleMode}, aspect {raiz.ContentScaleAspect}");

		Settings cfg = Boot.Config;
		(int L, int A)[] lista = Settings.ResolucoesPara(true);
		if (lista.Length == 0) { Checa("ha resolucao de tela cheia pra escolher", false); yield break; }
		(int L, int A) pequena = lista[0];

		// PELO CONTROLE DE PRODUCAO, e nao escrevendo no campo: e o `ItemSelected` do `OptionButton`
		// que carregava o `_cfg.TelaCheia = false`. Marcar tela cheia primeiro e escolher a
		// resolucao depois e exatamente o gesto do dono.
		Menu?.Abrir();
		yield return 0.3;
		if (Painel is not { } p) { Checa("as opcoes estao abertas", false); yield break; }

		if (Todos<CheckBox>(p).FirstOrDefault(c => c.Text == "tela cheia") is { } cb)
		{
			cb.ButtonPressed = true;
			cb.EmitSignal(BaseButton.SignalName.Toggled, true);
		}
		yield return 0.8;

		if (Todos<OptionButton>(p).FirstOrDefault() is { } ob)
		{
			int i = Enumerable.Range(0, ob.ItemCount)
							  .FirstOrDefault(k => ob.GetItemText(k).StartsWith($"{pequena.L} x {pequena.A}"));
			ob.Selected = i;
			ob.EmitSignal(OptionButton.SignalName.ItemSelected, i);
		}
		yield return 1.0;

		Checa("escolher uma resolucao NAO desliga mais a tela cheia", cfg.TelaCheia);
		Checa("a resolucao escolhida virou a BASE DE DESENHO (em tela cheia ela era ignorada)",
			  raiz.ContentScaleSize == new Vector2I(pequena.L, pequena.A),
			  $"base {raiz.ContentScaleSize}, pedida {pequena.L}x{pequena.A}");

		Vector2I janela = DisplayServer.WindowGetSize();
		float escala = raiz.GetFinalTransform().Scale.X;
		Checa("e o motor ESTICA de verdade (a base cabe na janela ampliada)",
			  escala > 1.01f, $"janela {janela}, base {raiz.ContentScaleSize}, escala {escala:0.###}x");

		// ============================ A MENOR RESOLUCAO OFERECIDA TEM QUE CABER A PROPRIA TELA DE OPCOES ============================
		// E o que segura o piso da escada em 1280x720 (ver `Settings.Escada`). Uma resolucao menor
		// na lista entregaria uma tela de opcoes cortada -- e cortada justamente na parte de baixo,
		// onde ficam "Fechar" e "Fechar o jogo". A conta e a caixa contra o retangulo visivel, em
		// pixels de canvas, na MENOR resolucao que a lista oferece.
		// ==========================================================================================
		if (Todos<PanelContainer>(p).FirstOrDefault() is { } molduraDoMenu)
		{
			Rect2 caixa = molduraDoMenu.GetGlobalRect();
			Rect2 visivel = GetViewport().GetVisibleRect();
			Checa("a tela de opcoes CABE INTEIRA na menor resolucao oferecida",
				  visivel.Encloses(caixa),
				  $"painel {caixa.Size.X:0}x{caixa.Size.Y:0} em {visivel.Size.X:0}x{visivel.Size.Y:0} "
				  + $"(base {pequena.L}x{pequena.A})");
		}

		// ---- O PIXEL, COM A TELA DE OPCOES FECHADA (senao a foto e o veu preto dela) ----
		Menu?.Fechar("bancada vai fotografar a tela cheia");
		yield return 0.5;

		Cobertura c1 = Medir();
		ChecaNoPixel("O DESENHO COBRE A JANELA INTEIRA (nenhum pixel de sobra em nenhum dos quatro lados)",
					 !c1.Vazia, c1.Cobre && c1.BordasPretas == 0, c1.Conta);
		Fotografar("user://opcoes-3-telacheia-preenchida.png");

		// ============================ A REGUA -- A METADE QUE FALTAVA, E QUE A CONFIG VELHA PROVOU ============================
		// **MEDIDO, e este e o achado da fase de prova**: repondo o `project.godot` DE ORIGEM (sem o
		// `window/stretch/mode`, que era o defeito de verdade), as duas contas acima ficaram VERDES:
		// *"desenhado 1920x1080 numa janela 1920x1080; borda preta esq 0, dir 0, cima 0, baixo 0"*.
		//
		// E ficaram verdes com razao -- com `mode = disabled` o viewport acompanha a janela 1:1,
		// entao ele COBRE a janela e nao ha barra preta nenhuma. So que a resolucao escolhida nao
		// vale nada: o jogo desenha na nativa do monitor e ignora o numero que o dono escolheu. O
		// defeito de origem nunca foi "sobra borda", foi **"a resolucao menor nao faz nada"** -- e
		// nenhuma conta que olhe so pra BORDA pode ver isso, por construcao.
		//
		// A regua ve. E um retangulo de <see cref="LadoDaRegua"/> pixels DE CANVAS, medido em pixels
		// DE TELA na foto. Se a resolucao virou base de desenho e o motor estica, ele sai maior na
		// foto na exata proporcao da esticada; se o `mode` esta morto, ele sai do tamanho que tem.
		// Nada aqui le um campo que nos mesmos escrevemos -- e a licao do *"uniform escrito nao e
		// pixel desenhado"* aplicada a esta familia.
		// ==========================================================================================
		float esperada = Math.Min(janela.X / (float)pequena.L, janela.Y / (float)pequena.A);
		MontarRegua();
		yield return 0.4;
		Medida r1 = LerRegua();
		ChecaNoPixel("A REGUA CONFIRMA A ESTICADA NO PIXEL (a resolucao menor virou base e foi ampliada)",
					 !r1.Vazia, Math.Abs(r1.Escala - esperada) < 0.05f,
					 r1.Conta + $"; esperada {esperada:0.###}x (janela {janela.X} / base {pequena.L})");
		Fotografar("user://opcoes-8-regua.png");

		// ---- INJECAO 1: A CONFIGURACAO DE ORIGEM, reposta na raiz de verdade ----
		// `mode = disabled` e literalmente o que o `project.godot` tinha antes desta tarefa (o campo
		// estava AUSENTE, e ausente e disabled). Rodei a bancada com o arquivo velho no disco e
		// conferi que da o mesmo estado -- por isso ela cabe aqui dentro, como cobertura permanente.
		Window.ContentScaleModeEnum modoBom = raiz.ContentScaleMode;
		raiz.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
		yield return 0.6;
		Medida r2 = LerRegua();
		InjetaNoPixel("com o `stretch/mode` DE ORIGEM ('disabled'), a regua acusa: a resolucao menor nao estica nada",
					  !r2.Vazia, Math.Abs(r2.Escala - esperada) >= 0.05f,
					  r2.Conta + $"; esperada {esperada:0.###}x");
		if (!r2.Vazia) Fotografar("user://opcoes-9-INJETADO-config-velha.png");

		raiz.ContentScaleMode = modoBom;
		yield return 0.4;
		TirarRegua();

		// ============================ A INJECAO QUE JA ME PEGOU UMA VEZ ============================
		// A primeira versao desta familia so contava PIXEL PRETO na borda da foto -- e ficou verde
		// com `aspect = keep` na frente. O motivo so apareceu OLHANDO a foto injetada: ela saiu
		// **1350x1080 num monitor de 1920x1080**. Com letterbox o Godot encolhe o proprio alvo de
		// render, entao a barra preta NAO ESTA NA FOTO -- contar preto ali era cego por construcao,
		// que e o mesmo erro de "uniform escrito nao e pixel desenhado".
		//
		// Por isso a medida agora tem DUAS metades: o tamanho do que foi desenhado contra o tamanho
		// da JANELA (pega letterbox) e a contagem de preto na borda (pega desenho que nao chega na
		// quina). Nenhuma das duas sozinha veria os dois defeitos.
		// ==========================================================================================
		raiz.ContentScaleAspect = Window.ContentScaleAspectEnum.Keep;
		raiz.ContentScaleSize = new Vector2I(1280, 1024);   // 5:4 numa tela 16:9: barra gorda
		yield return 0.7;
		Cobertura c2 = Medir();
		InjetaNoPixel("com `aspect = keep` (a barra preta de volta), a medida de cobertura acusa",
					  !c2.Vazia, !c2.Cobre || c2.BordasPretas > 0, c2.Conta);
		if (!c2.Vazia) Fotografar("user://opcoes-4-INJETADO-barra-preta.png");

		raiz.ContentScaleAspect = Window.ContentScaleAspectEnum.Expand;
		cfg.Aplicar();
		yield return 0.5;
	}

	// =====================================================================
	// A REGUA -- tamanho conhecido em CANVAS, lido em PIXEL DE TELA
	// =====================================================================
	/// <summary>
	/// O LADO DA REGUA, em pixels de canvas. 100 e um numero redondo de proposito: a esticada lida
	/// na foto vira a escala direto, sem conta no meio onde se possa errar o sinal.
	/// </summary>
	private const int LadoDaRegua = 100;

	private CanvasLayer? _regua;

	/// <summary>
	/// PENDURA O RETANGULO DE MEDIDA. Camada 100 (acima de tudo, inclusive das opcoes na 20) e
	/// magenta puro, que e a unica cor que nenhuma tela deste jogo usa -- assim a leitura nao precisa
	/// adivinhar qual mancha e a regua.
	///
	/// Na quina (0,0) de canvas porque e o unico ponto que existe em qualquer base e em qualquer
	/// esticada; o que se le e a LARGURA da mancha, e nao onde ela esta.
	/// </summary>
	private void MontarRegua()
	{
		TirarRegua();
		_regua = new CanvasLayer { Layer = 100, Name = "ReguaDaBancada" };
		_regua.AddChild(new ColorRect
		{
			Color = new Color(1, 0, 1),
			Position = Vector2.Zero,
			Size = new Vector2(LadoDaRegua, LadoDaRegua),
			MouseFilter = Control.MouseFilterEnum.Ignore,
		});
		AddChild(_regua);
	}

	private void TirarRegua()
	{
		if (_regua == null) return;
		_regua.QueueFree();
		_regua = null;
	}

	/// <summary>O que a regua mediu. `Vazia` = headless (nao ha foto).</summary>
	private readonly record struct Medida(bool Vazia, int Largura, int Altura, float Escala, string Conta);

	/// <summary>
	/// LE A REGUA NA FOTO: a caixa que a mancha magenta ocupa, em pixels de tela. A escala e a
	/// largura lida dividida pelo lado que ela tem em canvas.
	/// </summary>
	private Medida LerRegua()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return new Medida(true, 0, 0, 0, "sem janela: pulado");

		int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				Color c = img.GetPixel(x, y);
				if (c.R < 0.8f || c.G > 0.2f || c.B < 0.8f) continue;
				if (x < x0) x0 = x;
				if (y < y0) y0 = y;
				if (x > x1) x1 = x;
				if (y > y1) y1 = y;
			}

		if (x1 < 0) return new Medida(false, 0, 0, 0, "a regua NAO APARECEU na foto");

		int l = x1 - x0 + 1, a = y1 - y0 + 1;
		return new Medida(false, l, a, l / (float)LadoDaRegua,
			$"regua de {LadoDaRegua}x{LadoDaRegua} canvas saiu {l}x{a} px de tela "
			+ $"-> esticada {l / (float)LadoDaRegua:0.###}x");
	}

	/// <summary>
	/// QUANTO DA JANELA O JOGO DESENHOU. `Vazia` = headless (nao ha o que fotografar).
	/// </summary>
	private readonly record struct Cobertura(bool Vazia, bool Cobre, int BordasPretas, string Conta);

	private Cobertura Medir()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		Vector2I janela = DisplayServer.WindowGetSize();
		if (img == null || img.IsEmpty()) return new Cobertura(true, false, 0, "sem janela: pulado");

		int l = img.GetWidth(), a = img.GetHeight();
		bool ColunaPreta(int x)
		{
			for (int y = 0; y < a; y++) if (!Preto(img.GetPixel(x, y))) return false;
			return true;
		}
		bool LinhaPreta(int y)
		{
			for (int x = 0; x < l; x++) if (!Preto(img.GetPixel(x, y))) return false;
			return true;
		}

		int esq = 0; while (esq < l && ColunaPreta(esq)) esq++;
		int dir = 0; while (dir < l - esq && ColunaPreta(l - 1 - dir)) dir++;
		int cima = 0; while (cima < a && LinhaPreta(cima)) cima++;
		int baixo = 0; while (baixo < a - cima && LinhaPreta(a - 1 - baixo)) baixo++;

		bool cobre = l >= janela.X && a >= janela.Y;
		return new Cobertura(false, cobre, esq + dir + cima + baixo,
			$"desenhado {l}x{a} numa janela {janela.X}x{janela.Y}"
			+ (cobre ? "" : $" -- SOBRAM {janela.X - l}x{janela.Y - a} px sem desenho")
			+ $"; borda preta esq {esq}, dir {dir}, cima {cima}, baixo {baixo}");
	}

	private static bool Preto(Color c) => c.R < 0.02f && c.G < 0.02f && c.B < 0.02f;

	// =====================================================================
	// F5 -- A LISTA DE RESOLUCOES PARA NA AREA UTIL
	// =====================================================================
	/// <summary>
	/// O TERCEIRO PEDIDO: *"quando ta no modo janela o jogo n deveria ter a opcao de colocar na
	/// resolucao da minha tela, e sim na resolucao do fullscreen do modo janela"* -- e ele avisa que
	/// *"outras resolucoes isso varia"*, que e por que a lista tem que ser DERIVADA e nunca cravada.
	///
	/// A cobranca e contra a geometria que o SISTEMA devolve, e nao contra uma segunda copia da
	/// mesma conta: `ScreenGetUsableRect` (que ja desconta a barra de tarefas) menos a moldura
	/// medida na propria janela.
	///
	/// E ela cobra a REGRA e nao um calculo unico: trocar de modo com uma resolucao grande ja
	/// escolhida tem que corrigir sozinho -- foi assim que a caixa de apagar personagem saia do
	/// centro, por so calcular na abertura.
	/// </summary>
	private IEnumerable<double> F5_AListaParaNaAreaUtil()
	{
		Nota("--- F5: a lista de resolucoes para na area util, e a janela cabe na tela ---");

		Settings cfg = Boot.Config;
		int tela = DisplayServer.WindowGetCurrentScreen();
		Vector2I monitor = DisplayServer.ScreenGetSize(tela);
		Rect2I util = DisplayServer.ScreenGetUsableRect(tela);
		Vector2I moldura = Settings.Moldura;
		Nota($"  monitor {monitor}  util {util}  moldura {moldura}");

		Checa("a moldura foi MEDIDA numa janela de verdade (nao cravada)", moldura.Y > 0,
			  $"{moldura.X} de largura, {moldura.Y} de altura");

		(int L, int A)[] janela = Settings.ResolucoesPara(false);
		(int L, int A)[] cheia = Settings.ResolucoesPara(true);
		Nota("  janela:     " + string.Join(" | ", janela.Select(r => $"{r.L}x{r.A}")));
		Nota("  tela cheia: " + string.Join(" | ", cheia.Select(r => $"{r.L}x{r.A}")));

		Vector2I teto = new(util.Size.X - moldura.X, util.Size.Y - moldura.Y);
		Checa("TODA entrada do modo janela cabe na area util MENOS a moldura",
			  janela.All(r => r.L <= teto.X && r.A <= teto.Y), $"teto {teto.X}x{teto.Y}");
		Checa("a nativa do monitor NAO esta na lista de janela (era a queixa do dono)",
			  !janela.Any(r => r.L == monitor.X && r.A == monitor.Y) || moldura == Vector2I.Zero,
			  $"nativa {monitor.X}x{monitor.Y}");
		Checa("a maior entrada de janela E o teto ('a resolucao do fullscreen do modo janela')",
			  janela.Length > 0 && janela[^1] == (teto.X, teto.Y),
			  janela.Length > 0 ? $"{janela[^1].L}x{janela[^1].A}" : "(vazia)");

		// DECISAO EXPLICITA: em tela cheia a nativa CONTINUA na lista. A restricao do dono e do
		// modo janela -- em tela cheia nao ha moldura nem barra de tarefas na frente.
		Checa("em TELA CHEIA a nativa continua oferecida (a restricao e so do modo janela)",
			  cheia.Any(r => r.L == monitor.X && r.A == monitor.Y));

		// ---- A REGRA: trocar de modo com uma resolucao grande escolhida se corrige sozinho ----
		cfg.TelaCheia = true;
		cfg.LarguraJanela = monitor.X;
		cfg.AlturaJanela = monitor.Y;
		cfg.Aplicar();
		yield return 0.7;
		Checa("em tela cheia a nativa e aceita como esta", cfg.LarguraJanela == monitor.X);

		cfg.TelaCheia = false;
		cfg.Aplicar();
		yield return 0.8;
		Checa("ao VOLTAR pra janela, a nativa e cortada sozinha pelo teto (regra, nao calculo unico)",
			  cfg.LarguraJanela <= teto.X && cfg.AlturaJanela <= teto.Y,
			  $"ficou {cfg.LarguraJanela}x{cfg.AlturaJanela}, teto {teto.X}x{teto.Y}");

		// ---- A JANELA CABE NA TELA, COM MOLDURA E TUDO ----
		Rect2I comMoldura = new(DisplayServer.WindowGetPositionWithDecorations(),
								DisplayServer.WindowGetSizeWithDecorations());
		Rect2I utilAgora = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
		Checa("a janela INTEIRA (com barra de titulo) cabe na area util -- a barra ficava em y = -31",
			  utilAgora.Encloses(comMoldura),
			  $"janela {comMoldura} dentro de {utilAgora}");
		Fotografar("user://opcoes-5-janela-cabe.png");

		// ---- injecao: a conta antiga de posicao, que arrastava a janela pro monitor errado ----
		var tam = new Vector2I(cfg.LarguraJanela, cfg.AlturaJanela);
		Vector2I antiga = (DisplayServer.ScreenGetSize() - tam) / 2;   // o codigo que estava aqui
		var comoFicaria = new Rect2I(antiga - new Vector2I(moldura.X / 2, moldura.Y - moldura.X / 2),
									 tam + moldura);
		Injeta("com a conta ANTIGA de posicao (sem somar a origem do monitor), a janela sai da area util",
			   !utilAgora.Encloses(comoFicaria),
			   $"ficaria em {comoFicaria}, area util {utilAgora}");
	}

	// =====================================================================
	// F7 -- A JANELA QUE SAI DA MAIOR OPCAO CABE MESMO, E CONTINUA CABENDO
	// =====================================================================
	/// <summary>
	/// A F5 cobra a LISTA. Esta cobra a JANELA QUE SAI DELA -- e a diferenca entre as duas ja custou
	/// caro neste projeto: *"escrever o corte NAO e aplicar o corte"*. Uma lista certa com um
	/// `Aplicar` que ignora o teto entrega exatamente a queixa do dono de volta.
	///
	/// Por isso aqui nada e lido de campo nosso:
	///
	///   * a MAIOR opcao vem do `OptionButton` de producao, lida do TEXTO do item -- e o que o dedo
	///     do dono alcanca, e nao o que a `ResolucoesPara` devolve;
	///   * ela e escolhida pelo `ItemSelected`, que e o sinal que o clique dispara;
	///   * o que se mede depois e `WindowGetPositionWithDecorations` e `WindowGetSizeWithDecorations`
	///     -- a janela VESTIDA, onde ela realmente parou, contra o `ScreenGetUsableRect`.
	///
	/// E as DUAS METADES do pedido, que sao afirmacoes diferentes e podem quebrar separado:
	/// a nativa **fora** da lista de janela, e a nativa **dentro** da lista de tela cheia.
	///
	/// O CASO DE TROCA fecha a familia: escolher a maior em janela, ir pra tela cheia e voltar. E o
	/// caminho em que um corte feito so na montagem da lista deixaria passar -- ao voltar, quem
	/// restaura o tamanho e o `Aplicar`, e nao a lista.
	/// </summary>
	private IEnumerable<double> F7_AJanelaDeVerdadeCabe()
	{
		Nota("--- F7: a maior opcao de janela vira uma janela que cabe (medida onde ela parou) ---");

		Settings cfg = Boot.Config;
		int tela = DisplayServer.WindowGetCurrentScreen();
		Vector2I monitor = DisplayServer.ScreenGetSize(tela);

		cfg.TelaCheia = false;
		cfg.Aplicar();
		yield return 0.6;

		Menu?.Abrir();
		yield return 0.4;
		if (Painel is not { } p || Todos<OptionButton>(p).FirstOrDefault() is not { } lista)
		{
			Checa("achei o seletor de resolucao na tela de opcoes", false);
			yield break;
		}

		// ---- 1. A MAIOR OPCAO QUE O DEDO ALCANCA, EM MODO JANELA ----
		List<(int L, int A)> emJanela = Itens(lista);
		Nota("  o que a tela oferece em janela: " + string.Join(" | ", emJanela.Select(r => $"{r.L}x{r.A}")));
		if (emJanela.Count == 0) { Checa("a lista de janela tem itens", false); yield break; }

		(int L, int A) maior = emJanela[^1];
		Rect2I util = DisplayServer.ScreenGetUsableRect(tela);
		Checa("a MAIOR opcao de janela cabe na area util do monitor",
			  maior.L <= util.Size.X && maior.A <= util.Size.Y,
			  $"maior {maior.L}x{maior.A}, area util {util.Size.X}x{util.Size.Y}");
		Checa("METADE 1: a NATIVA do monitor nao esta na lista de janela",
			  !emJanela.Any(r => r.L == monitor.X && r.A == monitor.Y),
			  $"nativa {monitor.X}x{monitor.Y}");

		// ---- 2. ESCOLHIDA PELO CONTROLE, E MEDIDA ONDE A JANELA PAROU ----
		foreach (double d in Escolher(lista, maior)) yield return d;
		Checa("escolher a maior deu uma janela DESSE tamanho (nao foi cortada por baixo do pano)",
			  DisplayServer.WindowGetSize() == new Vector2I(maior.L, maior.A),
			  $"pedida {maior.L}x{maior.A}, janela {DisplayServer.WindowGetSize()}");
		Checa("A JANELA VESTIDA CABE INTEIRA NA TELA (posicao e tamanho REAIS, com barra de titulo)",
			  Cabe(out string onde), onde);

		// ---- 3. METADE 2: EM TELA CHEIA A NATIVA CONTINUA OFERECIDA ----
		if (Todos<CheckBox>(p).FirstOrDefault(c => c.Text == "tela cheia") is not { } cb)
		{
			Checa("achei a caixa de tela cheia", false);
			yield break;
		}

		cb.ButtonPressed = true;
		cb.EmitSignal(BaseButton.SignalName.Toggled, true);
		yield return 0.9;

		List<(int L, int A)> emCheia = Itens(lista);
		Nota("  o que a tela oferece em tela cheia: " + string.Join(" | ", emCheia.Select(r => $"{r.L}x{r.A}")));
		Checa("METADE 2: em TELA CHEIA a nativa ESTA na lista (a restricao e so do modo janela)",
			  emCheia.Any(r => r.L == monitor.X && r.A == monitor.Y),
			  $"nativa {monitor.X}x{monitor.Y}");
		Checa("e a lista de tela cheia oferece MAIS que a de janela (a moldura nao pesa aqui)",
			  emCheia[^1].L >= emJanela[^1].L && emCheia[^1].A >= emJanela[^1].A,
			  $"cheia {emCheia[^1].L}x{emCheia[^1].A} contra janela {emJanela[^1].L}x{emJanela[^1].A}");

		// ---- 4. A VOLTA: o caso que um corte so-na-lista deixaria passar ----
		cb.ButtonPressed = false;
		cb.EmitSignal(BaseButton.SignalName.Toggled, false);
		yield return 1.0;

		Checa("DEPOIS DA IDA E VOLTA a janela AINDA cabe inteira na tela",
			  Cabe(out string onde2), onde2);
		Checa("...e o tamanho continua dentro do teto do modo janela",
			  cfg.LarguraJanela <= Settings.TetoDeResolucao(false).X
			  && cfg.AlturaJanela <= Settings.TetoDeResolucao(false).Y,
			  $"ficou {cfg.LarguraJanela}x{cfg.AlturaJanela}, teto {Settings.TetoDeResolucao(false)}");
		Checa("...e a lista de janela continua sem a nativa depois da volta",
			  !Itens(lista).Any(r => r.L == monitor.X && r.A == monitor.Y));

		Menu?.Fechar("bancada terminou o F7");
		yield return 0.4;
		Fotografar("user://opcoes-10-janela-depois-da-volta.png");

		// ---- injecao: a medida de "cabe" sabe olhar? ----
		// Empurra a janela DE VERDADE pra fora da area util (o defeito que o `Recentrar` tinha, que
		// deixava a barra de titulo em y = -31) e cobra a mesma conta.
		Vector2I ondeEstava = DisplayServer.WindowGetPosition();
		DisplayServer.WindowSetPosition(new Vector2I(ondeEstava.X, util.Position.Y - 40));
		yield return 0.5;
		Injeta("com a janela empurrada pra cima (a barra de titulo fora da tela), a conta de 'cabe' acusa",
			   !Cabe(out string onde3), onde3);
		DisplayServer.WindowSetPosition(ondeEstava);
		yield return 0.4;
	}

	/// <summary>
	/// A JANELA VESTIDA ESTA INTEIRA DENTRO DA AREA UTIL DO MONITOR EM QUE ELA ESTA? Vestida e nao
	/// nua: a barra de titulo mora ACIMA do cliente, e era justamente ela que ficava fora da tela.
	/// </summary>
	private static bool Cabe(out string conta)
	{
		var vestida = new Rect2I(DisplayServer.WindowGetPositionWithDecorations(),
								 DisplayServer.WindowGetSizeWithDecorations());
		Rect2I util = DisplayServer.ScreenGetUsableRect(DisplayServer.WindowGetCurrentScreen());
		conta = $"janela vestida {vestida} contra area util {util}";
		return util.Encloses(vestida);
	}

	/// <summary>As resolucoes que o `OptionButton` esta MOSTRANDO, lidas do texto do item.</summary>
	private static List<(int L, int A)> Itens(OptionButton lista)
	{
		var saida = new List<(int, int)>();
		for (int i = 0; i < lista.ItemCount; i++)
		{
			string[] partes = lista.GetItemText(i).Split('x', 2);
			if (partes.Length != 2) continue;
			if (int.TryParse(partes[0].Trim(), out int l)
				&& int.TryParse(new string(partes[1].TrimStart().TakeWhile(char.IsDigit).ToArray()), out int a))
				saida.Add((l, a));
		}
		return saida;
	}

	private IEnumerable<double> Escolher(OptionButton lista, (int L, int A) alvo)
	{
		for (int i = 0; i < lista.ItemCount; i++)
		{
			if (!lista.GetItemText(i).StartsWith($"{alvo.L} x {alvo.A}")) continue;
			lista.Selected = i;
			lista.EmitSignal(OptionButton.SignalName.ItemSelected, i);
			yield return 1.0;
			yield break;
		}
		Checa($"achei '{alvo.L} x {alvo.A}' na lista pra escolher", false);
	}

	// =====================================================================
	// F6 -- SAIR LIMPO
	// =====================================================================
	/// <summary>
	/// *"O 'sair' tem que sair LIMPO: nada de processo vivo, servidor local orfao ou save pela
	/// metade."*
	///
	/// A bancada nao pode deixar o processo morrer (ela ainda tem que devolver os arquivos do dono),
	/// entao ela chama o MESMO `Saida.Encerrar` de producao com a arvore NULA: os tres passos de
	/// limpeza rodam inteiros e so o `Quit()` fica de fora. O que se mede sao os efeitos deles.
	/// </summary>
	private IEnumerable<double> F6_SairLimpo()
	{
		Nota("--- F6: sair limpo (servidor gravado e desligado, cliente despedido) ---");

		Checa("o X da janela passa pela nossa porta (`auto_accept_quit` desligado)",
			  !GetTree().AutoAcceptQuit);
		// ESTA BANCADA NASCE DEPOIS DO LOGIN, entao ela E uma sessao de jogador -- e por isso o
		// caminho que ela exercita abaixo e o COM save, e nao o atalho das outras bancadas.
		Checa("a bancada conta como sessao de jogador (ela atravessa o caminho COM save)",
			  Boot.SessaoDeJogador);

		if (Jandirus.Server.GameServer.Instance is not { } srv)
		{
			Checa("o servidor local existe pra ser desligado", false);
			yield break;
		}

		// UMA PORTA QUE NAO E A 7777: a do dono pode estar ocupada por outra bancada, e a intencao
		// aqui e ter um servidor NO AR pra ver o fechamento derruba-lo.
		bool subiu = srv.Running || srv.Start(7911);
		Checa("subi um servidor local pra ver o fechamento derruba-lo", subiu);
		if (!subiu) yield break;
		yield return 0.5;

		Saida.Encerrar(null, "bancada (sem matar o processo)");
		yield return 0.5;

		Checa("o servidor local NAO fica orfao depois do 'Sair'", !srv.Running);
		Checa("o caminho de saida foi marcado (ele nao roda duas vezes)", Saida.SaindoDeTeste);

		// a trava e do processo que esta morrendo; aqui o processo continua vivo pra devolver os arquivos
		Saida.RearmarDeTeste();
		Checa("a trava de saida se rearma pra bancada", !Saida.SaindoDeTeste);
	}

	// =====================================================================
	// FOTO
	// =====================================================================
	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { Nota("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_fotos.Add(caminho);
			Nota("  foto   " + caminho);
		}
		catch (Exception e) { Nota("  --     sem foto: " + e.Message); }
	}

	// =====================================================================
	// FECHAMENTO
	// =====================================================================
	private IEnumerable<double> Fechamento()
	{
		Nota("--- devolvendo a maquina do dono como estava ---");

		Settings cfg = Boot.Config;
		(cfg.LarguraJanela, cfg.AlturaJanela, cfg.TelaCheia, cfg.Zoom) = (_l0, _a0, _cheia0, _zoom0);
		(cfg.VolumeGeral, cfg.VolumeMusica) = (_geral0, _musica0);
		cfg.Aplicar();
		Menu?.Fechar("bancada terminou");
		yield return 0.5;

		Devolver();

		Nota("==================================================================");
		Nota($"PLACAR: {_ok} OK, {_falha} FALHA");
		if (_falha > 0) foreach (string f in _reprovadas) Nota($"   falhou: {f}");
		Nota($"INJECAO: {_injOk} pegou, {_injFalha} PASSOU BATIDO");
		if (_injFalha > 0) foreach (string f in _injPassouBatido) Nota($"   passou batido: {f}");
		// SEM JANELA NAO E "PASSOU", E "NAO OLHEI" -- ver `ChecaNoPixel`. Esta linha tem que dizer
		// zero pra que o placar acima possa ser lido como prova do L2.
		Nota($"NAO MEDIDO (sem janela): {_pulados}");
		if (_pulados > 0) foreach (string f in _naoMedidos) Nota($"   nao medido: {f}");
		if (_fotos.Count > 0) { Nota("FOTOS:"); foreach (string f in _fotos) Nota("   " + f); }
		Nota("==================================================================");
		yield return 0.4;

		GetTree()?.Quit();
	}
}
