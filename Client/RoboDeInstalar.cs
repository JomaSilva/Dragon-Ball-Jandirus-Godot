using System.Collections;
using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DO CICLO FABRICAR -> MOCHILA -> INSTALAR (`--diaginstalar`) ============================
/// O pedido do dono, inteiro: *"faca q todo item q vc produzir na research table, va parar no
/// inventario do personagem, ao qual ele pode clicar em instalar (caso seje algo q coloque no chao
/// e nao um item equipavel de uso pessoal como scouter, armaduras e pesos), e ao clicar em instalar
/// nesse objeto, basicamente uma versao transparente dele vai ficar no mouse como um preview de como
/// vai ficar quando instalar naquele local (isso claramente so aparece pro jogador local) ao clicar
/// o objeto vai ser instalado nesse local (e nesse momento q o server vai sincronizar com o resto
/// dos jogadores) e todos vao poder ver."*
///
/// Sao QUATRO regras, e cada uma tem DUAS metades. Esta bancada existe porque a metade fraca de cada
/// uma passa despercebida: "o item foi pra mochila" fica verde num jogo que TAMBEM largou uma copia
/// no chao; "a previa aparece" fica verde numa previa que o servidor ja sincronizou; "o servidor
/// valida" fica verde num servidor que aceita tudo. Entao toda familia aqui afirma o SIM **e** o
/// NAO.
/// =========================================================================================================================
///
/// ============================ AS SEIS FAMILIAS ============================
///   F0  A CLASSIFICACAO (regra 2, o dado). Percorre o catalogo inteiro e pergunta a MESMA lista de
///       acoes que o menu desenha (`AcoesDoItem`). Exige os dois lados por NOME: Armadura, Scouter,
///       Pesos, as armas de fogo e o Nav System **nao** ganham instalar; Maquina de Gravidade,
///       Research Station, Saco de Pancada e Telepad **ganham**. E cobra que os dois grupos existam
///       de verdade -- uma regra que respondesse "pessoal" pra tudo passaria por metade das provas.
///
///   F1  FABRICAR (regra 1). Compra pela bancada, pelo canal do jogador, e exige as duas metades:
///       o item ESTA na mochila **e** nada apareceu no chao. A segunda e a que importa: no original
///       (`TechSupport.dm:50`) a coisa nasce no chao, e um port que fizesse as duas ficaria verde
///       numa prova que so olhasse a mochila.
///
///   F2  A PREVIA E LOCAL (regra 3). Segurar cria um `Sprite2D` **filho do `World`**, translucido,
///       que anda com o mouse -- e, enquanto ele anda, **nada muda no mundo**: nem a lista de
///       construcoes (o que os outros veem) nem a mochila. E a leitura possivel de "nenhum pacote
///       sai" com um processo so: o que o servidor nao soube, ele nao sincronizou.
///
///   F3  A REGRA 2, DOS DOIS LADOS. Na TELA: abre a mochila de verdade (tecla do registro), aperta
///       o slot e le o TEXTO DOS BOTOES DESENHADOS -- item pessoal nao pode ter "Instalar no chão".
///       No FIO: manda `posicionar Armor/...` como um cliente mexido faria e exige que o servidor
///       recuse e que nada apareca no chao. Era a metade que faltava inteira.
///
///   F4  INSTALAR (regra 4). Um clique de mouse DE VERDADE empurrado no viewport. Exige as tres
///       coisas juntas: a obra aparece na lista que o servidor manda pra todo mundo, o item sai da
///       mochila, e a previa some.
///
///   F5  A RECUSA, E O QUE ELA CUSTA. Clica onde o servidor recusa e exige que **nada se perca**: o
///       item continua na mochila, o servidor diz o motivo, e a previa VOLTA pra mao. E o Esc, que
///       cancela sem gastar nada.
/// =========================================================================
///
/// ============================ E DEPOIS VEM A RODADA DE INJECAO ============================
/// As regras de F0 e da previa sao FUNCOES puras, e no fim a bancada chama as mesmas com amostras
/// deliberadamente estragadas e exige o vermelho. Regra que nao reprova o defeito que existe pra
/// pegar e uma linha verde que nao significa nada.
/// =======================================================================================
///
/// COMO RODAR (sem janela, conta e porta proprias, e com a pasta de saves DESVIADA -- ver
/// `testar-instalar.bat`, que faz o desvio pelo `APPDATA`):
///     Godot --headless --path . --host --rede 7975 --techteste --diaginstalar
///           --conta bancinst --nome BancInst
/// </summary>
public partial class RoboDeInstalar : Node
{
	private static GameClient? C => GameClient.Instance;

	/// <summary>Uma construcao de CHAO barata e com arte -- o cobaia do ciclo inteiro.</summary>
	private const string DoChao = "Punching_Bag";

	/// <summary>E o contra-exemplo que o dono citou pelo nome.</summary>
	private const string Pessoal = "Armor";

	/// <summary>
	/// SEGUNDOS ATE COMECAR -- vem do `--instalaratraso`. Padrao 3, que e o que sempre foi.
	///
	/// Existe por causa do SEGUNDO CORPO: a <see cref="RoboDeOlhoNoInstalar"/> roda noutro processo e
	/// precisa estar conectada e DENTRO DA ZONA antes de o roteiro daqui comecar, senao ela perde os
	/// marcos e a metade "os outros nao viram" ficaria verde por ausencia -- que e o pior jeito de
	/// ficar verde.
	/// </summary>
	public double Atraso = 3.0;

	/// <summary>
	/// ============================ O MARCO: COMO A BANCADA FALA COM O SEGUNDO CORPO ============================
	/// O que a regra 3 pede ("nenhum outro jogador ve a previa") e a regra 4 pede ("depois do clique
	/// todos veem") sao afirmacoes sobre a TELA DE OUTRA PESSOA. Um processo so nao alcanca isso: ele
	/// mede o efeito no proprio cliente e infere o resto.
	///
	/// O outro processo entao precisa saber QUANDO olhar -- e o sinal viaja pelo canal OOC, que e
	/// jogo de verdade: sai do `SendChat`, passa pelo servidor, e volta pelo `Falou` do outro. Um
	/// combinado por relogio (`aos 12 s eu seguro a previa`) ficaria verde numa rodada em que o
	/// roteiro atrasou, e as duas metades estariam medindo instantes diferentes.
	///
	/// OOC E O UNICO CANAL QUE ATRAVESSA DISTANCIA (`GameServer.Chat.cs`: os outros medem raio), e o
	/// servidor engole falas a menos de 400 ms uma da outra -- os marcos daqui ficam segundos
	/// separados, mas por isso eles nunca sao mandados em rajada.
	/// =====================================================================================================
	/// </summary>
	private static void Marco(string texto)
	{
		Nota("marco -> " + texto);
		C?.SendChat(Jandirus.Net.Protocol.Fala.Ooc, RoboDeOlhoNoInstalar.Prefixo + texto);
	}

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

	private static void Nota(string linha) => GD.Print("[instalar] " + linha);

	private readonly List<string> _falas = [];
	private List<string> Ouvindo() { _falas.Clear(); return _falas; }

	// =====================================================================
	// AS REGRAS -- funcoes puras, pra a injecao do fim chamar as MESMAS
	// =====================================================================
	/// <summary>
	/// ESTE ITEM OFERECE INSTALAR? -- pela lista de acoes, que e o que o menu percorre pra montar
	/// os botoes (`TelaDeInventario.AbrirAcoes`). Nao ha aqui nenhuma copia da regra: a bancada
	/// pergunta ao mesmo lugar que a tela pergunta.
	/// </summary>
	private static bool OfereceInstalar(ItemDef? def) =>
		def != null && Array.IndexOf(def.AcoesDoItem, CatalogoDeItens.AcaoPosicionar) >= 0;

	/// <summary>A previa e LOCAL: o node existe, esta na arvore, e o pai dele e o mundo do cliente.</summary>
	private static bool PreviaEhLocal(Sprite2D? f, Node? mundo) =>
		f != null && f.IsInsideTree() && mundo != null && f.GetParent() == mundo;

	/// <summary>...e ela e TRANSPARENTE, que e a palavra do pedido.</summary>
	private static bool PreviaEhTransparente(Sprite2D? f) => f != null && f.Modulate.A is > 0f and < 1f;

	/// <summary>A previa esta ancorada NA CELULA que o ponto pedido ocupa.</summary>
	private static bool PreviaNaCelula(Sprite2D? f, int cx, int cy)
	{
		if (f?.Texture == null) return false;
		const int t = ZoneCollision.TileSize;
		return Mathf.IsEqualApprox(f.Position.X, cx * t)
			&& Mathf.IsEqualApprox(f.Position.Y, (cy + 1) * t - f.Texture.GetHeight());
	}

	/// <summary>Nenhum botao desenhado oferece instalar.</summary>
	private static bool SemBotaoDeInstalar(IEnumerable<string> rotulos) =>
		!rotulos.Any(r => r.Contains("Instalar", StringComparison.OrdinalIgnoreCase));

	private static bool TemBotaoDeInstalar(IEnumerable<string> rotulos) =>
		rotulos.Any(r => r.Contains("Instalar", StringComparison.OrdinalIgnoreCase));

	/// <summary>O jogo me disse ISTO? Trecho fixado, e nao "alguma coisa foi dita".</summary>
	private static bool Disse(IEnumerable<string> falas, string trecho) =>
		falas.Any(f => f.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// O QUE ESTA ESCRITO NA CAIXA DE CHAT -- lido do `RichTextLabel`, que e o que o jogador le.
	///
	/// ============================ POR QUE O `Falou` NAO BASTA ============================
	/// `_falas` so recebe o que veio pela REDE (`GameClient.Falou`). As frases do cancelamento --
	/// *"você guarda X de volta"* -- e as do "nao cabe aqui" nascem no CLIENTE, pelo `Chat.Sistema`,
	/// e nunca passam por ali. A primeira versao desta prova cobrava a frase do botao direito no
	/// `_falas` e ficava vermelha com o codigo certo.
	///
	/// Ler o texto DESENHADO tambem e mais forte do que espiar o `Chat.Sistema`: uma frase montada e
	/// engolida por um filtro de canal continuaria "dita" e nao estaria na tela. Aqui, se o jogador
	/// nao pode ler, a bancada nao acha.
	/// ==================================================================================
	///
	/// ============================ E E `GetParsedText()`, NAO `.Text` -- ISSO CUSTOU DUAS VERMELHAS ============================
	/// A primeira versao lia `r.Text`, e `r.Text` **e sempre vazio nesta tela**: o `Chat.Somar`
	/// enche o painel com `AppendText`, que escreve na arvore interna de itens do `RichTextLabel` e
	/// **nao** na propriedade `Text` (ela so guarda o que alguem atribuir com `Text = ...`, e
	/// ninguem atribui). O metodo prometia no proprio cabecalho ler "o texto DESENHADO" e lia um
	/// campo que ninguem escreve.
	///
	/// O resultado foi o pior formato de bancada que este projeto conhece, e nos dois sentidos de
	/// uma vez: as duas provas POSITIVAS do cancelamento (F5.5b e F6.8) ficavam **vermelhas com o
	/// codigo certo** -- o jogo escrevia a frase, o jogador a lia na tela, e a bancada dizia que
	/// nao --, e qualquer prova NEGATIVA que alguem viesse a escrever aqui ("o jogo NAO disse X")
	/// ficaria **verde por vazio**, afirmando sobre uma string que nunca teve nada dentro.
	///
	/// `GetParsedText()` devolve o texto realmente montado, com o BBCode ja tirado -- que e
	/// literalmente o que o jogador le. E o mesmo acessor que o `RoboDeTecla` (`:226`) ja usava pra
	/// esta mesma caixa: a resposta certa estava escrita do lado, e a copia foi feita da propriedade
	/// errada.
	/// ==========================================================================================================================
	/// </summary>
	private static string TextoDoChat()
	{
		if (Chat.Instancia is not { } chat) return "";
		var fila = new Queue<Node>();
		fila.Enqueue(chat);
		while (fila.Count > 0)
		{
			Node n = fila.Dequeue();
			if (n is RichTextLabel r) return r.GetParsedText();
			foreach (Node f in n.GetChildren()) fila.Enqueue(f);
		}
		return "";
	}

	// =====================================================================
	// O MOTOR
	// =====================================================================
	private IEnumerator? _roteiro;
	private double _espera = -1;
	private bool _acabou;

	public override void _Ready()
	{
		_espera = Atraso;
		if (C is { } cli)
			cli.Falou += (_, _, texto) => { _falas.Add(texto); GD.Print("       [jogo] " + texto); };
	}

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
		Injecao();
		GD.Print($"\n[instalar] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[instalar] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}

	// =====================================================================
	// FERRAMENTAS
	// =====================================================================
	private static Vector2 Eu() => World.Instancia?.PosicaoLocal ?? Vector2.Zero;

	private static TelaDeConstrucao? Obra => TelaDeConstrucao.Instancia;

	/// <summary>
	/// O CLIQUE DE VERDADE: um `InputEventMouseButton` empurrado no viewport, no ponto do MUNDO
	/// pedido.
	///
	/// Nao chama `_Input` na mao: o caminho que interessa passa pelo viewport, e um atalho aqui
	/// pularia justamente a ligacao que pode estar faltando (foi assim que "clicar no chao nao fazia
	/// nada" sobreviveu -- o verbo `tech_posicionar` nao existia do outro lado e ninguem via).
	///
	/// A conversao mundo->tela e a INVERSA da que o `_Input` faz pra voltar; se uma das duas mudar,
	/// a bancada erra o tile e reprova, que e o desfecho certo.
	/// </summary>
	private void ClicarNoMundo(Vector2 pontoDoMundo, MouseButton botao = MouseButton.Left)
	{
		if (World.Instancia is not { } mundo) return;
		Vector2 naTela = mundo.GetCanvasTransform() * pontoDoMundo;

		// ============================ `inLocalCoords: true`, E ELE NAO E DETALHE ============================
		// Sem ele o `Viewport.PushInput` roda o `_make_input_local` e transforma a posicao **de novo**
		// pelo inverso do `final_transform` -- ou seja, aplica a conta uma vez a mais que a que este
		// metodo ja fez. O clique chegava alguns tiles fora, caia numa celula ocupada da cidade, e o
		// cliente recusava: a bancada lia isso como "instalar nao funciona" com o instalar funcionando.
		//
		// A coordenada que o `GetGlobalMousePosition` usa ja e a do viewport (e `gui.last_mouse_pos`),
		// entao a que este metodo monta tambem e -- e e por isso que o certo aqui e dizer que ela ja
		// esta local.
		// ===============================================================================================
		GetViewport().PushInput(
			new InputEventMouseMotion { Position = naTela, GlobalPosition = naTela }, true);
		GetViewport().PushInput(new InputEventMouseButton
		{
			ButtonIndex = botao,
			Pressed = true,
			Position = naTela,
			GlobalPosition = naTela,
		}, true);
	}

	private void Tecla(Key k) =>
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });

	/// <summary>Os textos de TODOS os botoes desenhados debaixo de um node. E o que o olho ve.</summary>
	private static List<string> BotoesDesenhados(Node? raiz)
	{
		var l = new List<string>();
		if (raiz == null) return l;
		var fila = new Queue<Node>();
		fila.Enqueue(raiz);
		while (fila.Count > 0)
		{
			Node n = fila.Dequeue();
			if (n is Button { Visible: true } b) l.Add(b.Text);
			foreach (Node f in n.GetChildren()) fila.Enqueue(f);
		}
		return l;
	}

	/// <summary>
	/// A TELA DA MOCHILA, achada por TIPO e nao por nome.
	///
	/// A primeira versao procurava `FindChild("TelaDeInventario")` e voltava nula sempre: no `Boot` o
	/// node se chama "Inventario". Procurar pelo tipo nao depende de ninguem manter um apelido.
	/// </summary>
	private TelaDeInventario? Mochila => Procurar<TelaDeInventario>(GetTree().Root);

	private static T? Procurar<T>(Node raiz) where T : Node
	{
		var fila = new Queue<Node>();
		fila.Enqueue(raiz);
		while (fila.Count > 0)
		{
			Node n = fila.Dequeue();
			if (n is T achado) return achado;
			foreach (Node f in n.GetChildren()) fila.Enqueue(f);
		}
		return null;
	}

	/// <summary>
	/// ABRE A MOCHILA E APERTA O SLOT DO ITEM -- pelo caminho do jogador: a tecla do REGISTRO e o
	/// sinal `Pressed` do botao desenhado. Devolve os rotulos do painel de acoes que apareceu.
	/// </summary>
	private List<string> AcoesDesenhadasDe(string id)
	{
		if (Mochila is not { } tela) return [];

		// A TECLA VEM DO REGISTRO e nao esta digitada aqui: quem religou a mochila continua sendo
		// medido pelo caminho dele.
		Key[] teclas = Jandirus.Client.Teclas.Teclado("ui_mochila");
		Tecla(teclas.Length > 0 ? teclas[0] : Key.I);

		string nome = CatalogoDeItens.Get(id)?.Nome ?? id;

		// O SLOT E UM BOTAO DESENHADO. Achar pelo TOOLTIP/texto seria fragil; o slot desenha o nome
		// do item, entao e por ele que se acha -- e se ele deixar de estar la, a bancada reprova.
		Button? slot = null;
		var fila = new Queue<Node>();
		fila.Enqueue(tela);
		while (fila.Count > 0 && slot == null)
		{
			Node n = fila.Dequeue();
			if (n is Button b && (b.TooltipText.Contains(nome, StringComparison.OrdinalIgnoreCase)
								  || b.Text.Contains(nome, StringComparison.OrdinalIgnoreCase)))
				slot = b;
			foreach (Node f in n.GetChildren()) fila.Enqueue(f);
		}
		if (slot == null) return [];

		List<string> antes = BotoesDesenhados(tela);
		slot.EmitSignal(BaseButton.SignalName.Pressed);
		// SO OS BOTOES NOVOS: o painel de acoes nasce por cima da grade, e devolver a grade inteira
		// faria "tem Instalar" ficar verde por causa de outro slot.
		return [.. BotoesDesenhados(tela).Where(r => !antes.Contains(r))];
	}

	private static int ObrasNoChao() => C?.Obras.Count ?? 0;
	private static int Tenho(string id) => C?.Mochila.Quantos(id) ?? 0;

	/// <summary>
	/// QUANTOS DESENHOS DE CONSTRUCAO EXISTEM NO MUNDO -- o que o olho veria, e nao o que o pacote
	/// disse.
	///
	/// `ObrasNoChao` conta a lista RECEBIDA; esta conta os nodes que o `World.DesenharObras` plantou.
	/// As duas responderem juntas e o que separa "o servidor nao mandou nada" de "o servidor mandou e
	/// o cliente nao desenhou" -- e a segunda ja aconteceu neste projeto com as portas.
	/// </summary>
	private static int ObrasDesenhadas()
	{
		if (World.Instancia is not { } mundo) return -1;
		int n = 0;
		var fila = new Queue<Node>();
		fila.Enqueue(mundo);
		while (fila.Count > 0)
		{
			Node no = fila.Dequeue();
			if (no is ObraDesenhada) n++;
			foreach (Node f in no.GetChildren()) fila.Enqueue(f);
		}
		return n;
	}

	/// <summary>
	/// UMA CELULA QUE NAO SERVE DE CHAO, DENTRO DO ALCANCE -- parede, agua, nuvem ou beirada.
	///
	/// ============================ POR QUE PROCURAR EM VEZ DE CRAVAR ============================
	/// O berco da Terra e uma cidade, e o que cerca o jogador muda com o mapa. Uma bancada que
	/// cravasse "a celula (x+2, y)" mediria o MAPA e nao o codigo -- e a `--diagembarque` e o F4
	/// daqui ja pagaram exatamente esse preco.
	///
	/// A PROCURA E DE DENTRO PRA FORA e para no primeiro achado dentro do <see cref="Assentamento.Alcance"/>:
	/// fora dele a recusa que sai e "longe demais", que e outra prova e nao esta.
	/// ======================================================================================
	/// </summary>
	private static (int cx, int cy, RecusaDeAssento porque)? CelulaQueNaoServe()
	{
		if (World.Instancia is not { } mundo || mundo.Colisao is not { } mapa) return null;
		if (mundo.PosicaoDesenhadaDe(C?.LocalId ?? 0) is not { } eu) return null;

		const int t = ZoneCollision.TileSize;
		(int mx, int my) = CatalogoDeObras.Celula(eu.X, eu.Y);
		int raio = (int)(Assentamento.Alcance / t);   // 3 tiles

		for (int d = 1; d <= raio; d++)
			for (int dy = -d; dy <= d; dy++)
				for (int dx = -d; dx <= d; dx++)
				{
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != d) continue;
					int cx = mx + dx, cy = my + dy;
					if (mapa.ServeDeChao(cx, cy)) continue;

					// A MESMA ORDEM DE FRASE do Core: o motivo tem que ser o especifico.
					RecusaDeAssento r = mapa.EhAgua(cx, cy) ? RecusaDeAssento.EmCimaDagua
									  : mapa.EhNuvem(cx, cy) ? RecusaDeAssento.EmCimaDeNuvem
									  : mapa.NaBorda(cx, cy) ? RecusaDeAssento.BeiradaDoMapa
									  : RecusaDeAssento.DentroDeParede;
					return (cx, cy, r);
				}
		return null;
	}

	// =====================================================================
	// O CAMINHO DE PRODUCAO: A BANCADA DE PESQUISA, PELOS BOTOES
	// =====================================================================
	/// <summary>
	/// ============================ FABRICAR PELO CAMINHO DO JOGADOR, E NAO PELO FIO ============================
	/// A versao anterior desta bancada chamava `SendTech("construir", id)` -- que e o que o botao faz,
	/// mas nao e o botao. A diferenca ja custou caro neste arquivo mesmo: por meses a tela mandava
	/// `SendVerbo("tech_construir")`, um nome que **nao existe do outro lado**, e todas as bancadas
	/// ficavam verdes porque nenhuma apertava o botao.
	///
	/// Entao o caminho aqui e o inteiro: abrir a grade (`Abrir`, que e o que o menu da tecla E chama),
	/// achar o CARTAO do item pelo nome desenhado nele, apertar, e apertar "Fabricar" na caixa de
	/// confirmacao. Se qualquer elo estiver solto, nao ha item na mochila e a familia reprova.
	/// =====================================================================================================
	/// </summary>
	private IEnumerable FabricarPelaTela(string id, Action<bool, string> conta)
	{
		if (TelaDeConstrucao.Instancia is not { } tela)
		{ conta(false, "a tela da bancada nem existe"); yield break; }

		// FECHAR TAMBEM E BOTAO. `Fechar()` e privado de proposito (so o Esc e o botao chamam), e
		// abrir a visibilidade dele so pra bancada seria criar um segundo caminho pra fechar a tela.
		void Sair() => AcharBotao(tela, b => b.Text.StartsWith("Fechar", StringComparison.Ordinal))
						 ?.EmitSignal(BaseButton.SignalName.Pressed);

		tela.Abrir();
		// A GRADE SO NASCE COM O PACOTE: o `Abrir` pede a lista e o desenho vem no `TechMudou`.
		foreach (object _ in Ate(() => BotoesDesenhados(tela).Count > 2, 3)) yield return 0.0;
		yield return 0.2;

		string nome = CatalogoDeItens.Get(id)?.Nome ?? id;

		// O CARTAO E ACHADO PELO QUE ESTA ESCRITO NELE. O botao do cartao mostra so o icone, mas o
		// tooltip dele carrega o nome e o preco (ver `TelaDeConstrucao.Cartao`) -- e o tooltip e o que
		// o jogador le ao passar o mouse. Procurar pelo id seria procurar por dado interno.
		Button? cartao = AcharBotao(tela, b => b.TooltipText.StartsWith(nome, StringComparison.Ordinal));
		if (cartao == null)
		{ conta(false, $"nao achei o cartao de '{nome}' na grade da bancada"); Sair(); yield break; }
		if (cartao.Disabled)
		{ conta(false, $"o cartao de '{nome}' esta apagado: {cartao.TooltipText}"); Sair(); yield break; }

		cartao.EmitSignal(BaseButton.SignalName.Pressed);
		yield return 0.2;

		Button? fabricar = AcharBotao(tela, b => b.Text == "Fabricar");
		if (fabricar == null)
		{ conta(false, "a caixa de confirmacao nao apareceu (ou o botao mudou de nome)"); Sair(); yield break; }

		fabricar.EmitSignal(BaseButton.SignalName.Pressed);
		conta(true, "");
		yield return 0.2;
		Sair();
	}

	private static Button? AcharBotao(Node raiz, Func<Button, bool> serve)
	{
		var fila = new Queue<Node>();
		fila.Enqueue(raiz);
		while (fila.Count > 0)
		{
			Node n = fila.Dequeue();
			if (n is Button { Visible: true } b && serve(b)) return b;
			foreach (Node f in n.GetChildren()) fila.Enqueue(f);
		}
		return null;
	}

	private static IEnumerable Ate(Func<bool> cond, double limiteSeg)
	{
		double t0 = Time.GetTicksMsec() / 1000.0;
		while (!cond() && Time.GetTicksMsec() / 1000.0 - t0 < limiteSeg) yield return 0.0;
	}

	/// <summary>Um ponto a UM tile de mim, na direcao pedida -- dentro do alcance do Core.</summary>
	private static Vector2 PertoDeMim(int dx, int dy) =>
		Eu() + new Vector2(dx * ZoneCollision.TileSize, dy * ZoneCollision.TileSize);

	/// <summary>
	/// UM PONTO QUE A PREVIA APROVA, procurado nos oito vizinhos.
	///
	/// ============================ POR QUE PROCURAR, E POR QUE ISSO NAO E CIRCULAR ============================
	/// A primeira versao cravava "um tile a direita" e reprovou tudo em F4: o berco da Terra e uma
	/// CIDADE, e o tile ao lado tinha mobilia do mapa -- o cliente recusou por "ja tem coisa demais",
	/// certissimo, e a bancada leu isso como "instalar nao funciona". Uma bancada que exige um mundo
	/// vazio mede o mundo, nao o codigo. (A `--diagembarque` tem a mesma nota e a mesma saida: ela
	/// tenta os quatro lados.)
	///
	/// A ESCOLHA USA A REGRA DO CLIENTE **de proposito**, e e por isso que ela nao e circular: a
	/// afirmacao que vem depois nao e "a regra concorda com ela mesma", e sim **"onde a previa diz
	/// branco, o SERVIDOR assenta e todo mundo passa a ver"**. As duas metades, e uma so nao valeria
	/// nada. O contrario -- onde a previa diz vermelho, nada sai -- e o que F3 e F5 cobram.
	/// =====================================================================================================
	/// </summary>
	private static Vector2? PontoQueAPreviaAprova()
	{
		foreach ((int dx, int dy) in new[] { (1, 0), (0, 1), (-1, 0), (0, -1),
											(1, 1), (-1, 1), (1, -1), (-1, -1) })
		{
			Vector2 p = PertoDeMim(dx, dy);
			(int cx, int cy) = CatalogoDeObras.Celula(p.X, p.Y);
			RecusaDeAssento r = TelaDeConstrucao.RecusaEm(cx, cy);
			if (r == RecusaDeAssento.Pode) return p;
			Nota($"  ({dx},{dy}) nao serve: {r}");
		}
		return null;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		Nota("comecando.");

		// -----------------------------------------------------------------
		// F0 -- A CLASSIFICACAO
		// -----------------------------------------------------------------
		GD.Print("\n--- F0: quem ganha \"Instalar no chão\" (regra 2, o dado) ---");
		yield return 0.0;

		CatalogoDeObras? cat = CatalogoDeItens.Obras;
		Checa("F0.0 o catalogo de construcoes esta ligado no de itens", cat != null);
		if (cat != null)
		{
			int chao = 0, pes = 0;
			foreach (Construcao c in cat.Todas)
			{
				if (!c.Construivel) continue;
				if (OfereceInstalar(CatalogoDeItens.Get(c.Id))) chao++; else pes++;
			}
			Nota($"do catalogo compravel: {chao} instalaveis, {pes} de uso pessoal");

			// OS DOIS GRUPOS TEM QUE EXISTIR. Sem estas duas linhas, uma regra que respondesse
			// sempre a mesma coisa passaria por metade das provas abaixo.
			Checa("F0.1 ha construcoes instalaveis", chao > 10, $"{chao}");
			Checa("F0.2 ha itens de uso pessoal", pes > 10, $"{pes}");
		}

		// OS NOMES QUE O DONO CITOU, um por linha -- e as armas, que eram o maior grupo errado.
		foreach (string id in new[] { "Armor", "Scouter", "Weights", "Handgun", "Shotgun",
									  "Nav_System", "Spacesuit", "Energy_Drain_Gloves" })
			Checa($"F0.3 {id} NAO oferece instalar", !OfereceInstalar(CatalogoDeItens.Get(id)),
				  string.Join("/", CatalogoDeItens.Get(id)?.AcoesDoItem ?? []));

		foreach (string id in new[] { "Gravity", "Research_Station", "Punching_Bag", "Telepad",
									  "Fridge", "Clone_Machine" })
			Checa($"F0.4 {id} oferece instalar", OfereceInstalar(CatalogoDeItens.Get(id)),
				  string.Join("/", CatalogoDeItens.Get(id)?.AcoesDoItem ?? []));

		// -----------------------------------------------------------------
		// F1 -- FABRICAR VAI PRA MOCHILA, E SO PRA ELA
		// -----------------------------------------------------------------
		GD.Print("\n--- F1: fabricar cai na mochila e NAO no chao (regra 1) ---");
		Marco("comecou");
		int obrasAntes = ObrasNoChao();
		int tinha = Tenho(DoChao);
		Ouvindo();

		// ============================ E O QUE SAI DO BOTAO E ESPIADO ============================
		// A pergunta nao e "a bancada mandou construir?" -- e "APERTAR O BOTAO manda construir?". O
		// espiao e o funil unico do canal (ver `GameClient.EspiaoDeTech`), entao o que ele nao viu nao
		// saiu por ali. Ele deixa passar (`true`): o ciclo tem que acontecer de verdade.
		//
		// Isto nao e preciosismo: por meses esta tela mandou `SendVerbo("tech_construir")` -- um nome
		// que nao existe do outro lado -- e nenhuma bancada notou, porque todas chamavam o `SendTech`
		// direto em vez de apertar o botao.
		var doBotao = new List<string>();
		GameClient.EspiaoDeTech = (cmd, arg) => { doBotao.Add(cmd + ":" + arg); return true; };

		bool apertou = false;
		string porqueNao = "";
		foreach (object _ in FabricarPelaTela(DoChao, (ok, p) => { apertou = ok; porqueNao = p; }))
			yield return 0.0;
		Checa("F1.0 a grade da bancada fabrica pelo BOTAO desenhado", apertou, porqueNao);
		Checa("F1.0b e o botao mandou `construir` pelo canal de tecnologia",
			  doBotao.Contains("construir:" + DoChao), string.Join(" | ", doBotao));
		GameClient.EspiaoDeTech = null;

		foreach (object _ in Ate(() => Tenho(DoChao) > tinha, 4)) yield return 0.0;
		yield return 0.4;

		Checa("F1.1 o item entrou na mochila", Tenho(DoChao) == tinha + 1,
			  $"tinha {tinha}, tenho {Tenho(DoChao)}");
		Checa("F1.2 NADA foi parar no chao", ObrasNoChao() == obrasAntes,
			  $"{obrasAntes} -> {ObrasNoChao()}");

		// ...E O CHAO CONTINUA VAZIO NA TELA, e nao so na lista. `ObrasNoChao` conta o que o cliente
		// RECEBEU; esta linha conta os `ObraDesenhada` que existem como node dentro do `World` -- o que
		// o olho veria. Sao duas metades do mesmo "nada apareceu": um pacote perdido nao desenha, e um
		// node vazado desenharia sem pacote nenhum.
		Checa("F1.2b e nenhum desenho de construcao apareceu no mundo",
			  ObrasDesenhadas() == obrasAntes,
			  $"{obrasAntes} -> {ObrasDesenhadas()} node(s) ObraDesenhada");

		Checa("F1.3 o jogo avisou que esta na mochila", Disse(_falas, "mochila"),
			  string.Join(" | ", _falas));

		// E O CONTRA-EXEMPLO: a armadura tambem cai na mochila, e a frase dela NAO promete instalar.
		// Ela vem pelo MESMO botao -- se so a de chao passasse pela tela, metade da regra 1 estaria
		// medida pelo fio.
		Ouvindo();
		int tinhaArmadura = Tenho(Pessoal);
		bool apertouArmadura = false;
		string porqueNaoArmadura = "";
		foreach (object _ in FabricarPelaTela(Pessoal, (ok, p) => { apertouArmadura = ok; porqueNaoArmadura = p; }))
			yield return 0.0;
		Checa("F1.3b a armadura tambem sai do botao desenhado", apertouArmadura, porqueNaoArmadura);
		foreach (object _ in Ate(() => Tenho(Pessoal) > tinhaArmadura, 4)) yield return 0.0;
		yield return 0.4;
		Checa("F1.4 a armadura tambem vai pra mochila", Tenho(Pessoal) == tinhaArmadura + 1);
		Checa("F1.5 e a frase dela NAO promete instalar", !Disse(_falas, "Instalar no chão"),
			  string.Join(" | ", _falas));
		Checa("F1.6 e ela tambem nao caiu no chao", ObrasNoChao() == obrasAntes,
			  $"{obrasAntes} -> {ObrasNoChao()}");
		// -----------------------------------------------------------------
		// F2 -- A PREVIA E LOCAL, TRANSPARENTE E SEGUE O MOUSE
		// -----------------------------------------------------------------
		GD.Print("\n--- F2: a previa e do jogador local (regra 3) ---");
		Obra?.Segurar(DoChao);
		yield return 0.2;

		Sprite2D? f = Obra?.FantasmaNaTela;
		Checa("F2.1 a previa nasceu", f != null);
		Checa("F2.2 ela e desenho LOCAL (filha do World do cliente)", PreviaEhLocal(f, World.Instancia));
		Checa("F2.3 ela e transparente", PreviaEhTransparente(f), $"alfa {f?.Modulate.A:0.00}");

		// ...E ELA ESTA ANCORADA NA CELULA DO CURSOR, seja qual for o cursor.
		//
		// ============================ O MOUSE NAO ANDA NO HEADLESS ============================
		// A primeira versao empurrava um `InputEventMouseMotion` e cobrava que o desenho fosse junto.
		// Sem tela, `Viewport.GetMousePosition()` nao se move -- o `_Process` continuava lendo o mesmo
		// ponto e a prova reprovava um codigo certo. Entao a pergunta virou a que da pra medir aqui e
		// que e a que importa: **o desenho esta na celula que o cursor ocupa**. O `_Process` recalcula
		// isso todo quadro a partir do mouse, entao "acompanha" e consequencia de "esta ancorado".
		// A prova de que ele acompanha em movimento fica pro olho, com janela.
		// =================================================================================
		yield return 0.2;
		Vector2 cursor = mundoOuZero();
		(int acx, int acy) = CatalogoDeObras.Celula(cursor.X, cursor.Y);
		Checa("F2.4 a previa esta ancorada na celula do cursor",
			  PreviaNaCelula(Obra?.FantasmaNaTela, acx, acy),
			  $"cursor {cursor} -> celula ({acx},{acy}), previa em {Obra?.FantasmaNaTela?.Position}");

		static Vector2 mundoOuZero() =>
			World.Instancia?.GetGlobalMousePosition() ?? Vector2.Zero;

		// ============================ E AGORA A METADE FORTE DA REGRA 3: NENHUM BYTE SAI ============================
		// A afirmacao antiga era "o mundo nao mudou", e ela e FRACA: um cliente que mandasse
		// `posicionar` a cada quadro e levasse recusa a cada quadro tambem nao mudaria a lista de
		// obras. Ficaria verde tagarelando.
		//
		// O espiao e o funil unico do canal onde `posicionar` viaja. Zero passadas nele com o fantasma
		// na mao **e** a lista intacta sao duas perguntas diferentes, e as duas precisam da mesma
		// resposta. (A terceira, "e ninguem MAIS viu", so um segundo processo responde -- e e o que a
		// `RoboDeOlhoNoInstalar` faz com o marco `previa` mandado logo abaixo.)
		// =========================================================================================================
		Marco("previa");
		yield return 0.5;

		var vazou = new List<string>();
		GameClient.EspiaoDeTech = (cmd, arg) => { vazou.Add(cmd + ":" + arg); return true; };

		int obrasComPrevia = ObrasNoChao();
		int desenhosComPrevia = ObrasDesenhadas();
		int mochilaComPrevia = Tenho(DoChao);
		for (int i = 0; i < 20; i++)
		{
			Vector2 p = PertoDeMim(i % 3 - 1, i % 2);
			if (World.Instancia is { } m3)
			{
				Vector2 nt = m3.GetCanvasTransform() * p;
				GetViewport().PushInput(new InputEventMouseMotion { Position = nt, GlobalPosition = nt });
			}
			yield return 0.0;
		}
		yield return 0.6;
		GameClient.EspiaoDeTech = null;

		Checa("F2.5 previa no mouse NAO mexeu no que os outros veem", ObrasNoChao() == obrasComPrevia,
			  $"{obrasComPrevia} -> {ObrasNoChao()}");
		Checa("F2.5b nem no que esta DESENHADO no mundo", ObrasDesenhadas() == desenhosComPrevia,
			  $"{desenhosComPrevia} -> {ObrasDesenhadas()}");
		Checa("F2.6 previa no mouse NAO mexeu na mochila", Tenho(DoChao) == mochilaComPrevia);
		Checa("F2.7 e NENHUM pacote de tecnologia saiu enquanto ela andava", vazou.Count == 0,
			  string.Join(" | ", vazou));

		// A OUTRA METADE DO ESPIAO: ele PRECISA saber ver. Um espiao que nunca dispara deixa a linha
		// acima verde num cliente que berra -- e "afirmacao de um lado so fica verde num sistema
		// morto" e a armadilha catalogada deste projeto. Entao aqui ele engole um pacote de mentira e
		// exige te-lo visto.
		var provaDoEspiao = new List<string>();
		GameClient.EspiaoDeTech = (cmd, arg) => { provaDoEspiao.Add(cmd + ":" + arg); return false; };
		C?.SendTech("lista", "prova-do-espiao");
		GameClient.EspiaoDeTech = null;
		Checa("F2.8 (o espiao do canal sabe ver: sem isto, F2.7 nao valeria nada)",
			  provaDoEspiao.Count == 1 && provaDoEspiao[0] == "lista:prova-do-espiao",
			  string.Join(" | ", provaDoEspiao));

		// A JANELA DO SILENCIO FECHA AQUI, e o segundo corpo tira a foto do "antes do clique" neste
		// instante. Ela precisa fechar ANTES de F3/F4 porque o clique da regra 4 esta la dentro: uma
		// janela que engolisse o clique diria "nada mudou" sobre o momento em que tudo muda.
		Marco("previafim");
		yield return 0.5;

		// -----------------------------------------------------------------
		// F3 -- A REGRA 2, DOS DOIS LADOS
		// -----------------------------------------------------------------
		GD.Print("\n--- F3: item pessoal nao oferece nem aceita instalar (regra 2) ---");
		Obra?.Largar();
		yield return 0.2;

		List<string> acoesDaArmadura = AcoesDesenhadasDe(Pessoal);
		yield return 0.1;
		Checa("F3.1 a armadura tem painel de acoes desenhado", acoesDaArmadura.Count > 0,
			  "nao achei o slot dela na grade");
		Checa("F3.2 e NENHUM botao dela oferece instalar", SemBotaoDeInstalar(acoesDaArmadura),
			  string.Join(" | ", acoesDaArmadura));

		List<string> acoesDoSaco = AcoesDesenhadasDe(DoChao);
		yield return 0.1;
		Checa("F3.3 o saco de pancada OFERECE instalar", TemBotaoDeInstalar(acoesDoSaco),
			  string.Join(" | ", acoesDoSaco));

		// fecha a mochila (a mesma tecla)
		Key[] tk = Jandirus.Client.Teclas.Teclado("ui_mochila");
		Tecla(tk.Length > 0 ? tk[0] : Key.I);
		yield return 0.2;

		// E O FIO: um cliente mexido manda o verbo direto. O servidor tem que recusar.
		Ouvindo();
		int obrasAntesDaArmadura = ObrasNoChao();
		Vector2 pt = PertoDeMim(0, 1);
		(int pcx, int pcy) = CatalogoDeObras.Celula(pt.X, pt.Y);
		const int T = ZoneCollision.TileSize;
		C?.SendTech("posicionar", $"{Pessoal}/{pcx * T + T / 2f:0}/{pcy * T + T / 2f:0}");
		yield return 0.6;

		Checa("F3.4 o servidor RECUSOU assentar a armadura", Disse(_falas, "uso pessoal"),
			  string.Join(" | ", _falas));
		Checa("F3.5 e nada apareceu no chao", ObrasNoChao() == obrasAntesDaArmadura,
			  $"{obrasAntesDaArmadura} -> {ObrasNoChao()}");
		Checa("F3.6 a armadura continua na mochila", Tenho(Pessoal) == tinhaArmadura + 1);

		// -----------------------------------------------------------------
		// F4 -- INSTALAR: O CLIQUE SINCRONIZA
		// -----------------------------------------------------------------
		GD.Print("\n--- F4: o clique instala e o servidor sincroniza (regra 4) ---");
		Obra?.Segurar(DoChao);
		yield return 0.2;

		int obrasAntesDoClique = ObrasNoChao();
		int tinhaAntesDoClique = Tenho(DoChao);
		Ouvindo();

		Vector2? escolhido = PontoQueAPreviaAprova();
		Checa("F4.0 achei um ponto que a previa aprova", escolhido != null,
			  "os oito vizinhos foram recusados -- o berco esta entulhado, veja as notas acima");
		Vector2 ondeInstalo = escolhido ?? PertoDeMim(1, 0);
		(int qcx, int qcy) = CatalogoDeObras.Celula(ondeInstalo.X, ondeInstalo.Y);
		Nota($"clicando em {ondeInstalo} = celula ({qcx},{qcy}); a previa diz {TelaDeConstrucao.RecusaEm(qcx, qcy)}");
		ClicarNoMundo(ondeInstalo);
		foreach (object _ in Ate(() => ObrasNoChao() > obrasAntesDoClique, 4)) yield return 0.0;
		yield return 0.5;

		Checa("F4.1 a obra apareceu na lista que o servidor manda pra todos",
			  ObrasNoChao() == obrasAntesDoClique + 1, $"{obrasAntesDoClique} -> {ObrasNoChao()}");
		Checa("F4.2 o item saiu da mochila", Tenho(DoChao) == tinhaAntesDoClique - 1,
			  $"tinha {tinhaAntesDoClique}, tenho {Tenho(DoChao)}");
		Checa("F4.3 a previa sumiu depois do aceite", Obra?.NaMao == "",
			  $"na mao: '{Obra?.NaMao}'");

		(int icx, int icy) = CatalogoDeObras.Celula(ondeInstalo.X, ondeInstalo.Y);
		bool naCelulaPedida = C?.Obras.Any(o =>
			CatalogoDeObras.Celula(o.Pos.X, o.Pos.Y) == (icx, icy) && o.Tipo == DoChao) ?? false;
		Checa("F4.4 ela ficou na CELULA que a previa mostrava", naCelulaPedida);

		// ...E ELA FOI DESENHADA. Estar na lista e "o servidor mandou"; existir como node e "o
		// jogador ve". As duas juntas sao a regra 4 inteira do lado de ca.
		Checa("F4.5 e o desenho dela apareceu no mundo",
			  ObrasDesenhadas() == obrasAntesDoClique + 1,
			  $"{obrasAntesDoClique} -> {ObrasDesenhadas()} node(s) ObraDesenhada");

		// O MARCO DO CLIQUE leva a CELULA: o segundo corpo tem que achar a bancada no mesmo lugar, e
		// nao so contar mais uma. "Apareceu alguma coisa" ficaria verde com a obra do lado errado do
		// mapa. Ver `RoboDeOlhoNoInstalar`.
		Marco($"clicou {icx} {icy} {DoChao}");
		yield return 0.6;

		// -----------------------------------------------------------------
		// F5 -- A RECUSA NAO CUSTA NADA
		// -----------------------------------------------------------------
		GD.Print("\n--- F5: quando o servidor recusa, nada se perde ---");
		int tinhaOutro = Tenho(DoChao);
		if (tinhaOutro == 0)
		{
			C?.SendTech("construir", DoChao);
			foreach (object _ in Ate(() => Tenho(DoChao) > 0, 4)) yield return 0.0;
			tinhaOutro = Tenho(DoChao);
		}

		Obra?.Segurar(DoChao);
		yield return 0.2;
		Ouvindo();
		int obrasAntesDaRecusa = ObrasNoChao();

		// EM CIMA DA QUE ACABEI DE ASSENTAR: o cliente TAMBEM sabe recusar isso (a lista dele ja tem
		// a obra), entao esta prova mede o caminho local. O importante e o mesmo dos dois jeitos:
		// nada some.
		ClicarNoMundo(ondeInstalo);
		yield return 0.8;

		Checa("F5.1 nada foi assentado por cima", ObrasNoChao() == obrasAntesDaRecusa,
			  $"{obrasAntesDaRecusa} -> {ObrasNoChao()}");
		Checa("F5.2 o item CONTINUA na mochila", Tenho(DoChao) == tinhaOutro,
			  $"tinha {tinhaOutro}, tenho {Tenho(DoChao)}");
		Checa("F5.3 a previa VOLTOU pra mao", Obra?.NaMao == DoChao && Obra?.EsperandoOServidor == false,
			  $"na mao '{Obra?.NaMao}', esperando {Obra?.EsperandoOServidor}");

		// E O ESC: cancela e nao gasta nada.
		int chatAntesDoEsc = TextoDoChat().Length;
		Tecla(Key.Escape);
		yield return 0.3;
		Checa("F5.4 o Esc cancelou a previa", Obra?.NaMao == "");
		Checa("F5.5 e o item continua na mochila depois do Esc", Tenho(DoChao) == tinhaOutro);
		string depoisDoEsc = TextoDoChat()[Math.Min(chatAntesDoEsc, TextoDoChat().Length)..];
		Checa("F5.5b e o jogo ESCREVEU na tela que guardou de volta",
			  depoisDoEsc.Contains("guarda", StringComparison.OrdinalIgnoreCase), depoisDoEsc.Trim());
		Checa("F5.5c e nada ficou no chao depois do Esc", ObrasNoChao() == obrasAntesDaRecusa,
			  $"{obrasAntesDaRecusa} -> {ObrasNoChao()}");

		// -----------------------------------------------------------------
		// LIMPEZA -- a obra desta rodada nao pode ficar de pe pra sempre
		// -----------------------------------------------------------------
		// Ela vai pro `mundo.json`, que sobrevive ao processo. Deixa-la ali faz a proxima rodada
		// tropecar nela ("ja tem coisa demais neste ponto") -- foi exatamente o que aconteceu com as
		// naves da `--diagembarque`.
		C?.SendTech("pegar", "");
		yield return 0.5;
		Checa("F5.6 a bancada recolheu o que ergueu", ObrasNoChao() == obrasAntesDoClique,
			  $"{ObrasNoChao()} obra(s) na zona -- confira o mundo.json da pasta de bancada");

		Marco("recolheu");
		yield return 0.6;

		// -----------------------------------------------------------------
		// F6 -- PAREDE E AGUA, PEDIDAS PELO FIO: O SERVIDOR RECUSA E NADA SE PERDE
		// -----------------------------------------------------------------
		// ============================ POR QUE ESTA FAMILIA PRECISA IR PELO FIO ============================
		// O F5 clica num lugar que o CLIENTE ja sabe recusar, e por isso ele mede o caminho local: o
		// pacote nem sai. Isso deixa a guarda do SERVIDOR sem prova nenhuma -- e ela e a que importa,
		// porque e a unica que um cliente mexido nao consegue pular. (Foi assim que a regra 2 viveu
		// meses so na tela: o menu nao desenhava o botao, e o `Posicionar` aceitava scouter.)
		//
		// Entao aqui o pedido vai como um cliente mexido faria: `posicionar` cru, na celula de parede
		// ou de agua que a <see cref="CelulaQueNaoServe"/> achou de verdade no mapa em volta.
		// ============================================================================================
		GD.Print("\n--- F6: parede/agua pedidas pelo fio (regra 4, a guarda do servidor) ---");

		int tinhaPraParede = Tenho(DoChao);
		if (tinhaPraParede == 0)
		{
			C?.SendTech("construir", DoChao);
			foreach (object _ in Ate(() => Tenho(DoChao) > 0, 4)) yield return 0.0;
			tinhaPraParede = Tenho(DoChao);
		}
		Checa("F6.0 tenho um saco de pancada pra tentar", tinhaPraParede > 0);

		(int cx, int cy, RecusaDeAssento porque)? ruim = CelulaQueNaoServe();
		Nota(ruim is { } rr
			 ? $"celula que nao serve, dentro do alcance: ({rr.cx},{rr.cy}) -- {rr.porque}"
			 : "NAO ACHEI parede/agua/beirada a tres tiles; F6 vai pelo 'longe demais'");

		// SEM PAREDE POR PERTO, A PROVA NAO SOME -- ela troca de recusa. O que nao pode e sumir em
		// silencio: um mapa liso faria a guarda do servidor ficar sem prova e ninguem saberia.
		int fcx, fcy;
		string frasePedida;
		if (ruim is { } achou)
		{
			(fcx, fcy) = (achou.cx, achou.cy);
			frasePedida = Assentamento.Motivo(achou.porque, "x");
		}
		else
		{
			(int mcx, int mcy) = CatalogoDeObras.Celula(Eu().X, Eu().Y);
			(fcx, fcy) = (mcx + 40, mcy);
			frasePedida = Assentamento.Motivo(RecusaDeAssento.LongeDemais, "x");
		}

		// A previa fica na mao, como ficaria depois de um clique que o cliente deixou passar.
		Obra?.Segurar(DoChao);
		yield return 0.2;
		Ouvindo();
		int obrasAntesDaParede = ObrasNoChao();
		const int T6 = ZoneCollision.TileSize;
		C?.SendTech("posicionar", $"{DoChao}/{fcx * T6 + T6 / 2f:0}/{fcy * T6 + T6 / 2f:0}");
		yield return 0.8;

		Checa("F6.1 o servidor recusou o lugar invalido", Disse(_falas, frasePedida),
			  $"esperava '{frasePedida}'; ouvi: " + string.Join(" | ", _falas));
		Checa("F6.2 e nada foi assentado", ObrasNoChao() == obrasAntesDaParede,
			  $"{obrasAntesDaParede} -> {ObrasNoChao()}");
		Checa("F6.3 nem desenhado", ObrasDesenhadas() == obrasAntesDaParede,
			  $"{obrasAntesDaParede} -> {ObrasDesenhadas()}");
		Checa("F6.4 e o item CONTINUA na mochila", Tenho(DoChao) == tinhaPraParede,
			  $"tinha {tinhaPraParede}, tenho {Tenho(DoChao)}");

		Checa("F6.5 e a previa continua na mao", Obra?.NaMao == DoChao,
			  $"na mao '{Obra?.NaMao}'");

		// ============================ O FANTASMA VOLTANDO DE UMA RECUSA -- O CAMINHO DE VERDADE ============================
		// A terceira promessa do cabecalho do `Posicionar`: *"o fantasma volta pra mao"*. Ela vive no
		// `ResolverEspera`, e ate aqui NENHUMA prova passava por ele pelo ramo da recusa -- porque o
		// unico jeito de o cliente mandar e o servidor recusar e um DESACORDO entre os dois, que e
		// justamente o que o resto do trabalho tenta tornar impossivel. Uma prova que so chamasse
		// `Segurar` e conferisse `NaMao` seria verdadeira por construcao e nao mediria nada.
		//
		// ENTAO O PACOTE E ENGOLIDO NO FUNIL. O clique acontece de verdade, `_esperandoResposta` liga
		// de verdade, `_tinhaAoMandar` e anotado de verdade -- e o servidor nunca ouve. Do lado de ca
		// isso e **exatamente** o que uma recusa parece: o catalogo volta (o `ComandoDeTech` reenvia
		// depois de todo comando) e o item continua na mochila. E ai o `ResolverEspera` tem que
		// desarmar a espera e devolver o fantasma ao mouse.
		// ============================================================================================================
		Vector2? praEngolir = PontoQueAPreviaAprova();
		if (praEngolir is { } pe)
		{
			GameClient.EspiaoDeTech = (_, _) => false;
			ClicarNoMundo(pe);
			GameClient.EspiaoDeTech = null;
			yield return 0.2;
			Checa("F6.5b o clique armou a espera pela resposta", Obra?.EsperandoOServidor == true);

			// O SINO E O `TechMudou`, e ele chega com qualquer comando de tecnologia -- o mesmo canal
			// confiavel e ordenado por onde a resposta viria.
			C?.SendTech("lista", "");
			foreach (object _ in Ate(() => Obra?.EsperandoOServidor == false, 4)) yield return 0.0;
			yield return 0.3;

			Checa("F6.5c com o item ainda na mochila, a previa VOLTOU pra mao",
				  Obra?.NaMao == DoChao && Obra?.EsperandoOServidor == false,
				  $"na mao '{Obra?.NaMao}', esperando {Obra?.EsperandoOServidor}");
			Checa("F6.5d e nada foi assentado nesse meio-tempo", ObrasNoChao() == obrasAntesDaParede,
				  $"{obrasAntesDaParede} -> {ObrasNoChao()}");
		}
		else Checa("F6.5b achei um ponto pra o clique engolido", false, "os oito vizinhos foram recusados");

		// E O CANCELAMENTO PELO BOTAO DIREITO -- o F5 ja cobre o Esc; o dono pediu os dois.
		int tinhaAntesDoDireito = Tenho(DoChao);
		int chatAntes = TextoDoChat().Length;
		Ouvindo();
		ClicarNoMundo(Eu(), MouseButton.Right);
		yield return 0.4;
		Checa("F6.6 o botao direito cancelou a previa", Obra?.NaMao == "", $"na mao '{Obra?.NaMao}'");
		Checa("F6.7 e o item continua na mochila depois dele", Tenho(DoChao) == tinhaAntesDoDireito);

		// A FRASE E LIDA DA CAIXA DE CHAT, e nao do `_falas`: ela nasce no cliente (`Chat.Sistema`) e
		// nunca passa pela rede. Ver `TextoDoChat`.
		string novoNoChat = TextoDoChat()[Math.Min(chatAntes, TextoDoChat().Length)..];
		Checa("F6.8 e o jogo ESCREVEU na tela que guardou de volta",
			  novoNoChat.Contains("guarda", StringComparison.OrdinalIgnoreCase), novoNoChat.Trim());
		Checa("F6.9 e nada ficou no chao", ObrasNoChao() == obrasAntesDaParede,
			  $"{obrasAntesDaParede} -> {ObrasNoChao()}");

		Marco("fim");
		yield return 0.5;
	}

	// =====================================================================
	// A RODADA DE INJECAO
	// =====================================================================
	/// <summary>
	/// AS MESMAS REGRAS, COM AMOSTRAS ESTRAGADAS -- e cada uma tem que ficar VERMELHA.
	///
	/// Sem isto, uma regra escrita ao contrario (`!Contains` onde devia ser `Contains`) fica verde no
	/// percurso inteiro e a bancada vira decoracao.
	/// </summary>
	private void Injecao()
	{
		GD.Print("\n--- injecao: as regras sabem reprovar? ---");

		var pessoalDeMentira = new ItemDef("X", "X", "", "", "", false,
										   Acoes: [CatalogoDeItens.AcaoPosicionar]);
		Checa("I.1 'oferece instalar' pega um pessoal com posicionar", OfereceInstalar(pessoalDeMentira));
		Checa("I.2 ...e recusa um sem acao nenhuma",
			  !OfereceInstalar(new ItemDef("X", "X", "", "", "", false, Acoes: [])));
		Checa("I.3 ...e recusa um item que nem existe", !OfereceInstalar(null));

		Checa("I.4 'previa local' reprova previa nenhuma", !PreviaEhLocal(null, World.Instancia));
		Checa("I.5 'previa local' reprova previa de outro pai",
			  !PreviaEhLocal(new Sprite2D(), World.Instancia));
		Checa("I.6 'transparente' reprova alfa cheio",
			  !PreviaEhTransparente(new Sprite2D { Modulate = new Color(1, 1, 1, 1) }));
		Checa("I.7 'transparente' reprova invisivel",
			  !PreviaEhTransparente(new Sprite2D { Modulate = new Color(1, 1, 1, 0) }));

		Checa("I.8 'sem botao de instalar' reprova quando ele esta la",
			  !SemBotaoDeInstalar(["Equipar / tirar", "Instalar no chão", "Jogar fora"]));
		Checa("I.9 'tem botao de instalar' reprova quando ele nao esta",
			  !TemBotaoDeInstalar(["Equipar / tirar", "Jogar fora"]));

		Checa("I.10 'previa na celula' reprova sem textura", !PreviaNaCelula(new Sprite2D(), 0, 0));
		Checa("I.11 'disse' reprova o que nao foi dito", !Disse(["olá"], "uso pessoal"));

		// ============================ O OLHO DO CHAT ESTA VIVO? ============================
		// As duas provas do cancelamento (F5.5b, F6.8) sao da forma "o chat CRESCEU e o que entrou
		// tem tal palavra". Uma sonda que devolvesse sempre `""` deixaria as duas vermelhas pra
		// sempre -- foi exatamente o que aconteceu enquanto o `TextoDoChat` lia `.Text` em vez de
		// `GetParsedText()` (ver o cabecalho dele). E o espelho e pior: a mesma sonda morta faria
		// QUALQUER prova negativa ("o jogo nao disse X") passar por vazio.
		//
		// A esta altura da rodada o chat ja recebeu dezenas de linhas (as do servidor e as do
		// cliente). Se ele estiver vazio aqui, o problema e a SONDA e nao o jogo -- e esta linha e
		// quem diz isso, em vez de deixar a duvida no meio das outras.
		// ==================================================================================
		Checa("I.16 a sonda do chat le a tela de verdade (nao devolve vazio)",
			  TextoDoChat().Length > 0, $"{TextoDoChat().Length} letras lidas");
		Checa("I.17 ...e ela le o que o CLIENTE escreveu, e nao so o que veio da rede",
			  TextoDoChat().Contains("guarda", StringComparison.OrdinalIgnoreCase),
			  "nenhum 'guarda' na tela -- o Chat.Sistema do cancelamento nao chegou ao painel");

		// E A REGRA DO CORE: ela sabe recusar agua, parede e distancia? (as tres que o fantasma e o
		// servidor leem juntos). Sem mapa a resposta e "pode" -- e isso tambem precisa estar dito.
		Checa("I.12 o Core recusa longe demais",
			  Assentamento.DoLugar(null, new Vec2(0, 0), 100, 100) == RecusaDeAssento.LongeDemais);
		Checa("I.13 o Core aceita perto, sem mapa",
			  Assentamento.DoLugar(null, new Vec2(16, 16), 0, 0) == RecusaDeAssento.Pode);
		Checa("I.14 a folga entre obras pega o que esta colado",
			  Assentamento.TemCoisaEm([new Vec2(100, 100)], new Vec2(110, 100)));
		Checa("I.15 ...e deixa passar o que esta longe",
			  !Assentamento.TemCoisaEm([new Vec2(100, 100)], new Vec2(200, 100)));
	}
}
