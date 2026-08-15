using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA AGUA (`--diagagua`) -- **a que OLHA**.
///
/// ============================ POR QUE ELA E DE FOTO, E NAO DE NUMERO ============================
/// As bancadas de Core ja provam as formulas do nado e a regra de passagem celula a celula. Nenhuma
/// delas responde as perguntas que o dono fez, porque as cinco sao sobre a TELA:
///
///   1. a pe o corpo PARA na beira -- e da pra ver que aquilo ali e agua?
///   2. nadando ele atravessa -- na pose do voo, SEM sombra e SEM subir?
///   3. o que nada e o que voa saem DIFERENTES na mesma agua? (se sairem iguais, o pedido nao foi
///      cumprido -- e essa e a unica pergunta que exige DUAS fotos do MESMO lugar)
///   4. socar a agua nao faz nada com ela?
///   5. quem esta do outro lado do lago APARECE? (agua nao cega)
///
/// Uma checagem que le `pl.Altitude == 0` prova que o campo esta zerado, nao que o boneco nao subiu
/// na tela -- e esse e o cego que ja deixou quatro defeitos visuais passarem por 4000 verdes neste
/// projeto. Entao aqui cada afirmacao anda em par: o NUMERO (que envelhece bem e diz por que) e a
/// FOTO (que e o juiz). E o item 3 ganha um terceiro juiz, PIXEL: os recortes das duas fotos sao
/// comparados, e "sao a mesma imagem" reprova.
/// ============================================================================================
///
/// ============================ E O BERCO E METADE DA PERGUNTA ============================
/// Ha QUATRO roteiros aqui, e quem escolhe e onde o servidor poe o corpo -- `--aguateste` (margem),
/// `--aguadentro` (dentro), `--aguanoar` (no ar) e `--aguaparede` (colado num muro). Eles nao sao
/// variacoes de conforto:
///
///   * o de DENTRO mede o MEIO da travessia (pose, altura zero, sombra que nao nasce). Ele nunca
///     teria pego os dois defeitos de ENTRADA, porque nasce com o problema ja resolvido -- e foi
///     exatamente isso que aconteceu: a bancada ficou verde com os dois caminhos do jogador
///     quebrados;
///   * o da MARGEM mede o gesto de verdade: apertar N na beira e ENTRAR andando, atravessar o rio
///     inteiro e sair do outro lado -- e, no seco, o verb ser RECUSADO;
///   * o do AR mede o POUSO em cima da agua, o unico que passa pelo `DescerAte`;
///   * o do MURO mede se a parede ainda para quem nada. Ele nao usa o lago dos outros tres, e isso
///     foi MEDIDO: o muro mais proximo daquele lago esta a 15 tiles, o corpo anda ~35 px/s e o nado
///     cobra Ki por segundo -- as duas tentativas de alcanca-lo (a nado e de voo) terminaram na
///     EXAUSTAO antes de o muro caber na foto.
///
/// Uma bancada que so nasce no lugar certo nao mede como se CHEGA nele.
/// ====================================================================================
///
/// COMO RODAR (uma janela so -- headless nao renderiza e as fotos saem vazias):
///     Godot --path . --host --rede 7980 --aguateste --vooteste --bpteste 100000 --horateste 0.5 ^
///           --diagagua --raca Human --conta bancada_agua --nome Nadador
///
///   * `--aguateste` poe o corpo na beira de um lago estreito e alguem na outra margem (sem ele a
///     bancada nasceria em terra seca e nao teria o que medir);
///   * `--vooteste` da a skill de voo -- o item 3 e uma COMPARACAO, e sem voo ela nao existe;
///   * `--bpteste` da forca pra derrubar cenario: e o CONTRA-EXEMPLO do item 4 (um soco que nao
///     quebra a agua nao prova nada se aquele soco nao quebra nada em lugar nenhum);
///   * `--horateste 0.5` crava meio-dia. A hora do mundo e sorteada, e uma foto de lago as 3 da
///     manha nao responde "da pra ver que e agua?" pra ninguem.
/// </summary>
public partial class RoboDeAgua : Node
{
	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	private bool _acabou;
	private int _passo;
	private double _t;

	/// <summary>Quantas celulas de cenario cairam desde que a bancada comecou -- ver o item 4.</summary>
	private readonly HashSet<(int, int)> _caidas = [];

	/// <summary>O que o servidor falou. O nado so avisa por chat, e e assim que o jogador sabe.</summary>
	private readonly List<string> _ditos = [];

	private Vector2 _rumo = Vector2.Right;
	private int _larguraDoLago;
	private Vector2 _saida, _entradaNaAgua;
	private Vector2 _deOndeAndou;

	/// <summary>O meio do lago, em pixels -- e la que as fotos do item 2 e do item 3 sao tiradas.</summary>
	private Vector2 _meioDoLago;

	/// <summary>Pra onde o piloto automatico esta indo AGORA; nulo quando nao ha destino marcado.</summary>
	private Vector2? _mirando;

	/// <summary>A outra margem, em pixels -- e por perto dela que o vizinho do item 5 nasceu.</summary>
	private Vector2 _outraMargem;

	/// <summary>
	/// O VERB DO SECO AGUENTOU UM TIQUE? Ver o passo 11 -- e a unica conferencia desta bancada que
	/// nasceu de um defeito achado por ela mesma.
	/// </summary>
	private bool _nadoDoSecoAguentou;

	/// <summary>Chegou no seco da OUTRA margem nadando?</summary>
	private bool _chegouNaOutraMargem;

	/// <summary>
	/// O DIARIO DO BIT DE NADO -- cada vez que ele acende ou apaga, com altura e chao debaixo.
	///
	/// Uma conferencia que diz "nao estava nadando" nao serve pra achar POR QUE: o bit apaga entre
	/// dois passos da bancada e o motivo (a agua acabou? o Ki? o pouso?) fica invisivel. O diario
	/// diz o instante e o estado do corpo naquele instante, que e o que separa as tres causas.
	/// </summary>
	private readonly List<string> _diarioDoNado = [];
	private bool _nadandoAntes;
	private double _relogio;

	/// <summary>Alguma vez o corpo ficou EM CIMA da agua estando A PE? E o item 1, medido por quadro.</summary>
	private bool _pisouNaAguaAPe;

	/// <summary>...e nadando, alguma vez ele chegou a estar sobre a agua? E o item 2.</summary>
	private bool _nadouSobreAgua;

	/// <summary>A maior altura vista NADANDO. Tem que ser zero -- o dono pediu "nem fica mais alto".</summary>
	private float _maiorAlturaNadando;

	/// <summary>A sombra apareceu alguma vez enquanto ele nadava? Tem que ser nunca.</summary>
	private bool _sombraNadando;

	/// <summary>
	/// QUANTOS PIXELS ELE PERCORREU **NADANDO E EM CIMA DA AGUA**, somados por quadro.
	///
	/// ============================ POR QUE UMA SOMA, E NAO "chegou do outro lado" ============================
	/// "Saiu no seco do outro lado" fica VERDE num corpo que atravessou andando pela margem -- e foi
	/// exatamente o falso verde que a rodada anterior desta bancada deu, com o nado tendo apagado
	/// dez segundos antes. Distancia percorrida com o bit aceso E os pes na agua nao tem como ser
	/// forjada por quem esta andando no seco: a segunda metade da condicao e falsa o tempo todo.
	/// ==================================================================================================
	/// </summary>
	private float _pxNadandoNaAgua;

	/// <summary>Onde o corpo estava no quadro anterior -- so serve pra somar <see cref="_pxNadandoNaAgua"/>.</summary>
	private Vector2? _ondeEuEstavaNoQuadroAnterior;

	/// <summary>
	/// A TRAVESSIA DA MARGEM ESTA EM CURSO -- so entre o passo 12 e o 19.
	///
	/// As tres fotos abaixo sao POR QUADRO, e sem esta chave elas disparariam tambem no roteiro
	/// molhado (que nasce em cima da agua e liga o nado no passo 30): o arquivo "os pes encostam na
	/// agua" sairia de um corpo que ja estava dentro dela desde antes de existir bancada.
	/// </summary>
	private bool _olhandoATravessia;

	/// <summary>Ja saiu a foto da ENTRADA, a do MEIO e a da SAIDA? Cada uma vale um quadro so.</summary>
	private bool _fotoDaEntrada, _fotoDoMeio, _fotoDaSaida;

	/// <summary>
	/// UMA TRAVESSIA ESTA EM CURSO -- vale nos TRES roteiros (margem, molhado e do ar).
	///
	/// ============================ ELE EXISTE PORQUE A CHEGADA ERA LIDA UMA VEZ POR SEGUNDO ============================
	/// "Saiu no seco do outro lado" era conferido nos passos de espera (um por segundo). Sair da agua
	/// dura UM quadro, e a saida caiu na fresta entre o ultimo passo de espera e o veredito: a bancada
	/// reprovou uma travessia que o proprio diario dela mostrava inteira ("12,1s LIGOU / 19,9s
	/// APAGOU", com 208 px nadados e 230 px de avanco). Um instante nao se le por amostragem.
	/// ==============================================================================================================
	/// </summary>
	private bool _medindoTravessia;

	/// <summary>Quantos segundos a mais o veredito da travessia ja concedeu -- ver o uso.</summary>
	private int _esperasDaTravessia;

	// =====================================================================
	// A PAREDE, NADANDO  (o buraco mais provavel do conserto do modo)
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ISTO PRECISA SER MEDIDO, E NA AGUA ============================
	/// O conserto que fez o jogador entrar no lago foi mandar o MODO junto do passo
	/// (`GameServer.cs`, `ValidateStep(..., ModoDeTravessiaDe(pl))`). O `ZoneCollision.Bloqueia` diz
	/// que parede para em todo modo -- mas nao e ai que mora o risco, e sim na SAIDA DE EMERGENCIA do
	/// <c>MoveRules.Advance</c>: *"ja preso dentro de parede: deixa sair"* devolve o passo CHEIO, sem
	/// conferir nada. Com o modo `APe` (o de antes), todo corpo em cima de agua caia nessa saida --
	/// entao qualquer passo dele passava sem checagem, inclusive atraves de muro. Com `Nadando` a agua
	/// deixa de o prender, a saida nao dispara mais e a checagem volta a ser de verdade.
	///
	/// A prova disso e um corpo NADANDO -- em cima da agua, com o bit aceso -- empurrando um muro de
	/// verdade. Feita no seco ela nao provaria nada: no seco a saida de emergencia nunca disparava
	/// nem antes.
	/// ================================================================================================
	/// </summary>
	private bool _medindoParede;

	/// <summary>Alguma vez, empurrando, os pes ficaram DENTRO da celula de parede? Tem que ser nunca.</summary>
	private bool _entrouEmParede;

	/// <summary>Houve quadro empurrando a parede com o bit de nado aceso E os pes na agua?</summary>
	private bool _empurrouNadando;

	/// <summary>A celula de parede escolhida, o ponto de encosto e o rumo do empurrao.</summary>
	private Vector2I _celulaDaParede;
	private Vector2 _encostoDaParede, _rumoDaParede;

	/// <summary>Onde o corpo estava quando o empurrao comecou -- o avanco depois dele tem que ser ~zero.</summary>
	private Vector2 _antesDoEmpurrao;

	/// <summary>Onde ele estava quando a andada contra o muro comecou -- so pro diario do trajeto.</summary>
	private Vector2 _antesDaViagem;

	/// <summary>
	/// Onde ele estava um segundo antes do veredito do muro.
	///
	/// O avanco TOTAL nao separa "o muro parou" de "a bancada soltou o piloto": o corpo nasce um tile
	/// antes e esse tile e avanco legitimo. O que separa e o ULTIMO segundo, com a tecla ainda
	/// apertada -- ali o numero honesto e zero.
	/// </summary>
	private Vector2 _ondeParouNoPenultimo;

	/// <summary>Quantas vezes a fase do "longe da agua" ja pediu mais um segundo de caminhada.</summary>
	private int _tentativasLonge;

	private Image? _fotoNadando, _fotoVoando;
	private Vector2 _naTelaNadando, _naTelaVoando;

	private int _idDoVizinho;
	private int _quadrosVendoOVizinho, _quadrosComVizinho;

	private static GameClient? C => GameClient.Instance;

	// =====================================================================
	// RELATORIO
	// =====================================================================
	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	// =====================================================================
	// A FOTO
	// =====================================================================
	/// <summary>
	/// Salva a tela inteira MAIS um recorte ampliado em volta do corpo.
	///
	/// A tela cheia prova o ENQUADRAMENTO (o lago, as duas margens, quem esta do outro lado); o
	/// recorte prova o CORPO (a pose, a ausencia da sombra, o vao ate o chao). Nenhuma das duas
	/// responde as duas coisas: no zoom do jogo o boneco tem 32 px numa tela de 1600.
	/// </summary>
	private Image? Fotografar(string destino, string rotulo, out Vector2 naTela)
	{
		naTela = OndeEstouNaTela();
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"{rotulo}: sem foto (headless nao renderiza)"); return null; }
		try
		{
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add($"  ok     {rotulo}: {caminho} ({img.GetWidth()}x{img.GetHeight()})");
			if (Recorte(img, naTela) is { } lupa)
			{
				string zoom = caminho.Replace(".png", "-zoom.png");
				lupa.SavePng(zoom);
				_passos.Add($"  ok     {rotulo}: recorte 3x em {zoom}");
			}
			return img;
		}
		catch (Exception e) { Nota($"{rotulo}: sem foto: {e.Message}"); return null; }
	}

	private static Image? Recorte(Image cheia, Vector2 tela)
	{
		if (tela == Vector2.Zero) return null;
		const int lado = 224;
		var r = new Rect2I((int)tela.X - lado / 2, (int)tela.Y - lado / 2, lado, lado);
		r = r.Intersection(new Rect2I(0, 0, cheia.GetWidth(), cheia.GetHeight()));
		if (r.Size.X < 32 || r.Size.Y < 32) return null;
		Image corte = cheia.GetRegion(r);
		corte.Resize(corte.GetWidth() * 3, corte.GetHeight() * 3, Image.Interpolation.Nearest);
		return corte;
	}

	private Vector2 OndeEstouNaTela()
	{
		if (C is not { } cli || World.Instancia?.PosicaoDesenhadaDe(cli.LocalId) is not { } eu)
			return Vector2.Zero;
		return (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * eu;
	}

	/// <summary>Um ponto do MUNDO em pixels de TELA -- e assim que se sabe onde a agua foi desenhada.</summary>
	private Vector2 NaTela(Vector2 mundo)
		=> (GetViewport()?.CanvasTransform ?? Transform2D.Identity) * mundo;

	/// <summary>
	/// A COR MEDIA DE UM QUADRADINHO DA TELA. Nulo quando o ponto caiu fora da janela.
	///
	/// E media e nao pixel unico de proposito: um pixel pega o contorno de uma pedra ou a espuma da
	/// margem e responde qualquer coisa. A media de 12x12 responde "de que cor e aquele chao ali".
	/// </summary>
	private static Color? CorMedia(Image img, Vector2 tela, int lado = 12)
	{
		var r = new Rect2I((int)tela.X - lado / 2, (int)tela.Y - lado / 2, lado, lado)
			.Intersection(new Rect2I(0, 0, img.GetWidth(), img.GetHeight()));
		if (r.Size.X < 4 || r.Size.Y < 4) return null;
		float rr = 0, gg = 0, bb = 0;
		int n = 0;
		for (int y = r.Position.Y; y < r.Position.Y + r.Size.Y; y++)
			for (int x = r.Position.X; x < r.Position.X + r.Size.X; x++)
			{
				Color c = img.GetPixel(x, y);
				rr += c.R; gg += c.G; bb += c.B; n++;
			}
		return n == 0 ? null : new Color(rr / n, gg / n, bb / n);
	}

	private static string Hex(Color c) => $"({c.R * 255:0},{c.G * 255:0},{c.B * 255:0})";

	// =====================================================================
	// O CORPO
	// =====================================================================
	private Node2D? MeuCorpo() => C is { } cli ? World.Instancia?.CorpoDeTeste(cli.LocalId) : null;

	/// <summary>
	/// A SOMBRA ESTA DESENHANDO? Ela e um node proprio (`SombraDeVoo`) e some por `Visible`.
	///
	/// A pergunta e sobre o node ESTAR VISIVEL e nao sobre ele existir: no corpo LOCAL ele nasce
	/// junto do personagem (`LocalPlayer._Ready`) e vive apagado; e so no corpo REMOTO que ele nasce
	/// sob demanda. Perguntar "existe?" reprovaria o nado do corpo local com o desenho perfeito.
	/// </summary>
	private bool SombraVisivel()
		=> MeuCorpo()?.GetNodeOrNull<SombraDeVoo>("SombraDeVoo") is { Visible: true };

	/// <summary>Quantos pixels separam o DESENHO do corpo da posicao do no. Nadando tem que ser 0.</summary>
	private float Vao()
	{
		if (C is not { } cli || World.Instancia is not { } mundo) return 0f;
		if (mundo.PosicaoLocal is not { } no || mundo.PosicaoDesenhadaDe(cli.LocalId) is not { } desenho) return 0f;
		return no.DistanceTo(desenho);
	}

	private bool SobreAgua()
	{
		if (World.Instancia is not { Colisao: { } mapa } mundo || mundo.PosicaoLocal is not { } p) return false;
		return MoveRules.NaAgua(mapa, new Vec2(p.X, p.Y));
	}

	// =====================================================================
	// ONDE ESTA O LAGO
	// =====================================================================
	/// <summary>
	/// POR ONDE SE ATRAVESSA, olhando dos quatro lados. Devolve o rumo e a largura em tiles.
	///
	/// A bancada NAO recebe isto do servidor de proposito: ela le o mesmo `.agua` que o cliente usa
	/// pra desenhar e pra prever o passo. Se as duas pontas discordarem sobre onde tem agua, e aqui
	/// que aparece -- o corpo pararia num lugar que a bancada acha seco.
	/// </summary>
	private bool AcharTravessia(out Vector2 rumo, out int largura)
	{
		rumo = Vector2.Right;
		largura = 0;
		if (World.Instancia is not { Colisao: { } mapa } mundo || mundo.PosicaoLocal is not { } p) return false;

		int t = ZoneCollision.TileSize;
		int cx = (int)MathF.Floor(p.X / t), cy = (int)MathF.Floor((p.Y + MoveRules.FeetOffsetY) / t);

		// ============================ A TRAVESSIA TEM QUE TER OS TRES PEDACOS ============================
		// A primeira versao aceitava a PRIMEIRA direcao com agua a ate 4 tiles, na ordem leste, oeste,
		// sul, norte. Numa curva do rio isso escolheu OESTE (3 tiles de agua) enquanto o berco tinha
		// posto o corpo pra atravessar ao NORTE -- e a oeste havia parede entre o corpo e a agua. O
		// robo ligou o nado, andou 6 s contra o muro sem molhar o pe, e a bancada reprovou dez
		// conferencias de uma vez com o jogo intacto. (O `_outraMargem` tambem saiu no eixo errado, e
		// por isso o vizinho do item 5 "nao nasceu".)
		//
		// Entao aqui se exige a MESMA forma que o berco do servidor exige (`AcharTravessia`, no
		// `GameServer.AguaTeste.cs`): chao livre ate a agua, agua por N tiles, e chao SECO E PISAVEL do
		// outro lado. E entre as direcoes que servem, ganha a que comeca mais PERTO -- que e a que o
		// corpo alcanca andando.
		// ==============================================================================================
		(int dx, int dy)[] rumos = [(1, 0), (-1, 0), (0, 1), (0, -1)];
		int melhorD = int.MaxValue;
		bool achou = false;

		foreach ((int dx, int dy) in rumos)
			for (int d = 1; d <= 4; d++)
			{
				// PAREDE NO CAMINHO MATA ESTA DIRECAO: a agua atras dela nao e travessia nenhuma.
				if (mapa.BlockedCell(cx + dx * d, cy + dy * d)) break;
				if (!mapa.EhAgua(cx + dx * d, cy + dy * d)) continue;

				int n = 0;
				while (n < 60 && mapa.EhAgua(cx + dx * (d + n), cy + dy * (d + n))) n++;

				// ...E TEM QUE HAVER ONDE SAIR do outro lado: sem isto a "travessia" pode ser um braco
				// de rio que so acaba fora do mapa, e o robo nadaria pro nada.
				int fx = cx + dx * (d + n), fy = cy + dy * (d + n);
				if (mapa.BlockedCell(fx, fy) || mapa.EhAgua(fx, fy)) break;

				if (d < melhorD)
				{
					melhorD = d;
					rumo = new Vector2(dx, dy);
					largura = n;
					achou = true;
				}
				break;   // esta direcao ja deu a sua resposta
			}
		return achou;
	}

	// =====================================================================
	// VIDA DO NO
	// =====================================================================
	public override void _Ready()
	{
		if (C is not { } cli) return;
		cli.Falou += AoOuvir;
		// O ESTRAGO E O ITEM 4, e ele so se mede escutando: o cliente nao adivinha que uma celula
		// caiu, ele RECEBE o aviso. Escutar desde o inicio pega inclusive um estrago que a bancada
		// nao pediu.
		cli.CenarioCaiu += (cx, cy) => _caidas.Add((cx, cy));
		cli.SnapshotReceived += Avistou;
	}

	public override void _ExitTree()
	{
		if (C is not { } cli) return;
		cli.Falou -= AoOuvir;
		cli.SnapshotReceived -= Avistou;
	}

	/// <summary>Tudo que o servidor disse, do inicio ao fim -- <see cref="_ditos"/> e zerado a cada fase.</summary>
	private readonly List<string> _tudoQueFoiDito = [];

	private void AoOuvir(Protocol.Fala canal, string quem, string texto)
	{
		_ditos.Add(texto);
		_tudoQueFoiDito.Add($"{_relogio:0.0}s: {texto}");
	}

	private bool Disse(string trecho) => _ditos.Any(d => d.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	/// <summary>
	/// MARCA QUEM ESTA DO OUTRO LADO -- o corpo mais perto da OUTRA MARGEM, e nao "o primeiro que
	/// aparecer".
	///
	/// A primeira versao pegava o primeiro do snapshot e marcou alguem a 1637 px -- um NPC de
	/// conversa do outro canto do mapa, fora da tela. A conferencia de visao reprovou (o veu so
	/// responde por quem esta no enquadramento) com a agua se comportando perfeitamente. O alvo tem
	/// que ser escolhido pelo LUGAR: quem esta na margem de la, e mais ninguem.
	/// </summary>
	private void Avistou(List<EntityState> estados)
	{
		if (C is not { } cli || _idDoVizinho != 0 || _outraMargem == Vector2.Zero) return;
		float melhor = ZoneCollision.TileSize * 4f;
		foreach (EntityState e in estados)
		{
			if (e.Id == cli.LocalId) continue;
			float d = new Vector2(e.Pos.X, e.Pos.Y).DistanceTo(_outraMargem);
			if (d < melhor) { melhor = d; _idDoVizinho = e.Id; }
		}
	}

	public override void _Process(double delta)
	{
		if (_acabou || C is not { Connected: true } cli) return;
		if (World.Instancia is not { } mundo) return;

		// ============================ AS PROVAS DE "NUNCA" SAO POR QUADRO ============================
		// "A pe ele nao entra na agua" e "nadando ele nunca sobe" sao afirmacoes sobre TODO instante.
		// Lidas um passo por segundo, elas passariam por cima de meio segundo dentro do lago -- que e
		// exatamente o tamanho do defeito que se procura.
		// =========================================================================================
		_relogio += delta;
		bool nadando = cli.Sheet.Nadando;
		if (nadando != _nadandoAntes)
		{
			_nadandoAntes = nadando;
			_diarioDoNado.Add($"{_relogio:0.0}s: nado {(nadando ? "LIGOU" : "APAGOU")}"
							  + $" | altura {mundo.AlturaDeTeste:0} px | sobre agua: {(SobreAgua() ? "sim" : "NAO")}"
							  + $" | Ki {cli.Sheet.Ki:0}/{cli.Sheet.MaxKi:0}"
							  + $" | ultimo aviso: '{_ditos.LastOrDefault() ?? ""}'");
		}
		if (SobreAgua())
		{
			if (nadando) _nadouSobreAgua = true;
			else if (mundo.AlturaDeTeste <= 0f) _pisouNaAguaAPe = true;
		}
		if (nadando)
		{
			_maiorAlturaNadando = Math.Max(_maiorAlturaNadando, mundo.AlturaDeTeste);
			if (SombraVisivel()) _sombraNadando = true;
		}

		// A DISTANCIA PERCORRIDA NADANDO SOBRE A AGUA -- ver o campo.
		if (mundo.PosicaoLocal is { } agora2)
		{
			if (_ondeEuEstavaNoQuadroAnterior is { } antes && nadando && SobreAgua()
				&& agora2.DistanceTo(antes) < 64f)   // salto grande e teleporte (correcao), nao nado
				_pxNadandoNaAgua += agora2.DistanceTo(antes);
			_ondeEuEstavaNoQuadroAnterior = agora2;
		}

		// A CHEGADA NO SECO, POR QUADRO -- ver `_medindoTravessia`.
		if (_medindoTravessia && !SobreAgua() && mundo.AlturaDeTeste == 0f && _pxNadandoNaAgua > 8f)
			_chegouNaOutraMargem = true;

		// ============================ AS TRES FOTOS DA TRAVESSIA SAO POR QUADRO ============================
		// O dono pediu tres instantes: o quadro em que os PES ENCOSTAM na agua, o MEIO e a SAIDA. Nenhum
		// dos tres cai num passo desta bancada (que anda de segundo em segundo) -- a entrada dura um
		// quadro, e a foto tirada um segundo depois mostra um corpo ja dentro do lago, que e outra
		// pergunta ("ele esta nadando?") e nao a que se fez ("ele CONSEGUIU entrar?").
		//
		// A DA SAIDA E A MAIS SENSIVEL DAS TRES: o desligamento por chao seco chega no tique seguinte
		// (ate 100 ms), entao o unico jeito de fotografar o corpo no ultimo quadro em que ainda nadava
		// e olhar todo quadro. Um passo de bancada perderia a transicao inteira.
		// ================================================================================================
		if (_olhandoATravessia)
		{
			if (!_fotoDaEntrada && nadando && SobreAgua())
			{
				_fotoDaEntrada = true;
				Fotografar("user://agua-11-entrada.png", "TRAVESSIA 1/3 -- OS PES ENCOSTAM NA AGUA", out _);
			}
			else if (!_fotoDoMeio && _fotoDaEntrada && nadando && SobreAgua()
					 && _pxNadandoNaAgua > _larguraDoLago * ZoneCollision.TileSize * 0.5f)
			{
				_fotoDoMeio = true;
				Fotografar("user://agua-12-meio.png", "TRAVESSIA 2/3 -- NO MEIO DO RIO", out _);
			}
			else if (!_fotoDaSaida && _fotoDaEntrada && !SobreAgua() && _pxNadandoNaAgua > 8f)
			{
				_fotoDaSaida = true;
				Fotografar("user://agua-13-saida.png", "TRAVESSIA 3/3 -- OS PES SAEM NA OUTRA MARGEM", out _);
			}
		}

		// ---------- E O EMPURRAO NA PAREDE, TAMBEM POR QUADRO ----------
		// "Nunca entrou na parede" e afirmacao sobre TODO instante: lida um passo por segundo, ela
		// passaria por cima de meio segundo dentro do muro -- que e o tamanho exato do defeito.
		if (_medindoParede)
		{
			if (ParedeSobOsPes()) _entrouEmParede = true;
			if (nadando && SobreAgua()) _empurrouNadando = true;
		}

		// O VIZINHO, TAMBEM POR QUADRO: o veu se recalcula a cada passo do olho, e "da pra ver" tem
		// que valer sempre e nao num instante escolhido a dedo.
		if (_idDoVizinho != 0 && mundo.CorpoDeTeste(_idDoVizinho) is { } corpoDele)
		{
			_quadrosComVizinho++;
			if (mundo.VeDeTeste(corpoDele.GlobalPosition)) _quadrosVendoOVizinho++;
		}

		// ============================ PARAR NO MEIO DO LAGO E POR QUADRO ============================
		// O piloto automatico anda pro infinito, e um passo de bancada (1 s) atravessa o lago inteiro
		// -- as fotos do item 2 e do item 3 sairiam da OUTRA MARGEM, e a comparacao entre elas
		// mediria dois lugares diferentes. O ponto de parada e mirado, e quem o alcanca e o quadro.
		// ========================================================================================
		if (_mirando is { } alvo && mundo.PosicaoLocal is { } aqui && aqui.DistanceTo(alvo) < 10f)
		{
			mundo.PararDeTeste();
			_mirando = null;
		}

		_t += delta;
		if (_t < 1.0) return;
		_t = 0;

		switch (_passo++)
		{
			// =============================================================
			// ITEM 1 -- A PE, A AGUA BARRA
			// =============================================================
			case 0:
			{
				Conferir(mundo.Colisao is { TemAgua: true },
					$"a zona tem plano de agua carregado no CLIENTE (zona '{cli.Zone.Name}')");

				// ============================ QUAL ROTEIRO: O SECO, O MOLHADO OU O DO AR ============================
				// Quem decide e ONDE O CORPO NASCEU, e nao uma flag propria da bancada. E de proposito:
				// as flags de linha de comando sao do servidor (`--aguateste`, `--aguadentro`,
				// `--aguanoar`), e uma segunda flag no cliente poderia discordar delas -- a bancada
				// rodaria o roteiro de terra firme com o corpo dentro do lago e reprovaria tudo.
				// Perguntando ao mundo, discordar e impossivel.
				//
				// A ALTURA VEM ANTES DA AGUA na pergunta, e a ordem importa: quem nasceu NO AR sobre o
				// lago responde "sim" as duas, e o roteiro dele nao e o do mergulhado -- ele mede o
				// POUSO, que e o unico caminho que passa pelo `DescerAte`.
				// ================================================================================================
				if (mundo.AlturaDeTeste > 0f) { _passo = 60; break; }
				if (SobreAgua())
				{
					// ...E O QUARTO BERCO SE DENUNCIA PELO MAPA, e nao por flag: `--aguaparede` poe o
					// corpo na agua com um MURO a dois tiles, e `--aguadentro` no meio de um lago cujo
					// muro mais proximo esta a quinze. Quem responde e o `.col` que o cliente ja leu --
					// uma segunda flag deste lado poderia discordar da do servidor.
					_passo = AcharParedeNaAgua(out _, out _, out _, 2) ? 100 : 30;
					break;
				}

				Conferir(!SobreAgua(), "a bancada comeca em chao SECO");

				bool achou = AcharTravessia(out _rumo, out _larguraDoLago);
				Conferir(achou, achou
					? $"lago na frente: rumo {_rumo}, {_larguraDoLago} tiles de largura"
					: "nao ha agua nenhuma ao redor -- rode com --aguateste");
				if (!achou) { _passo = 100; break; }

				_saida = mundo.PosicaoLocal ?? Vector2.Zero;

				// ============================ UM SEGUNDO DE RECUO ANTES DE ANDAR CONTRA A AGUA ============================
				// O berco poe o corpo um tile antes da margem, mas "um tile" varia com o lago sorteado:
				// numa rodada ele nasceu a 7 px do ponto em que a agua barra, e a conferencia "...e ele
				// ANDOU de verdade ate la" ficou vermelha com a agua se comportando perfeitamente --
				// nao havia caminhada nenhuma a fazer.
				//
				// Recuar um segundo cria a pista, e ela nao contamina nada: o veredito do passo 4 mede
				// a partir de ONDE A CAMINHADA COMECOU (`_deOndeAndou`, recarregado no passo 1), e as
				// duas provas de "nunca" (`_pisouNaAguaAPe`, o veu do vizinho) valem por quadro do
				// inicio ao fim.
				// ======================================================================================================
				mundo.AndarDeTeste(-_rumo);
				break;
			}

			case 1:
				mundo.PararDeTeste();
				_deOndeAndou = mundo.PosicaoLocal ?? _saida;
				mundo.AndarDeTeste(_rumo);
				break;

			case 2:
			case 3:
				break;   // andando contra a agua

			case 4:
			{
				mundo.PararDeTeste();
				Vector2 parou = mundo.PosicaoLocal ?? _deOndeAndou;
				_entradaNaAgua = parou;

				Conferir(!_pisouNaAguaAPe,
					$"A PE A AGUA BARRA: andou {parou.DistanceTo(_deOndeAndou):0} px contra ela e NUNCA "
					+ "ficou com os pes dentro de celula de agua");
				Conferir(parou.DistanceTo(_deOndeAndou) > 8f,
					$"...e ele ANDOU de verdade ate la ({parou.DistanceTo(_deOndeAndou):0} px) -- "
					+ "parar por nao ter saido do lugar nao provaria nada");

				// PAROU **NA BEIRA**, e nao dez tiles antes por causa de um muro no caminho.
				float ateAAgua = DistanciaAteAAgua(parou);
				Conferir(ateAAgua <= ZoneCollision.TileSize * 1.5f,
					$"ele parou COLADO na beira: {ateAAgua:0} px ate a primeira celula de agua");

				// A OUTRA MARGEM, calculada uma vez: e por perto dela que o vizinho do item 5 nasceu, e
				// e ela que o `Avistou` usa pra escolher QUEM e o vizinho (o mais perto DELA, e nao o
				// primeiro do snapshot -- ver o metodo).
				const int t = ZoneCollision.TileSize;
				_outraMargem = parou + _rumo * (ateAAgua + (_larguraDoLago + 0.5f) * t);

				// ---------- a foto, e a cor ----------
				Image? foto = Fotografar("user://agua-1-parou-na-beira.png", "ITEM 1 -- A PE, PAROU NA BEIRA", out _);
				if (foto != null) MedirAsDuasMargens(foto, parou);
				break;
			}

			// =============================================================
			// ITEM 4 -- SOCAR A AGUA
			// =============================================================
			case 5:
				// Virado pra agua (o servidor ja nasceu assim, e o passo de aproximacao manteve o
				// rumo). Quatro socos: a chance de derrubar cenario e 34% por golpe, entao quatro
				// socos numa parede de verdade quase nunca saem todos em branco -- e a agua tem que
				// sair em branco SEMPRE.
				_caidas.Clear();
				cli.SendAction(Protocol.Golpe.Leve);
				break;

			case 6:
			case 7:
			case 8:
				cli.SendAction(Protocol.Golpe.Leve);
				break;

			case 9:
			{
				Conferir(_caidas.Count == 0,
					$"SOCAR A AGUA NAO FAZ NADA: 4 socos nela e {_caidas.Count} celula(s) derrubada(s)");
				Conferir(AguaAindaNaFrente(),
					"...e a celula de agua CONTINUA sendo agua depois dos socos");
				Conferir(!Disse("cede sob o seu punho"),
					"...e o servidor nunca disse que o cenario cedeu");
				Fotografar("user://agua-4-socando.png", "ITEM 4 -- SOCANDO A AGUA", out _);
				break;
			}

			// =============================================================
			// ITEM 5 -- QUEM ESTA DO OUTRO LADO
			// =============================================================
			case 10:
			{
				if (_idDoVizinho == 0)
				{
					Nota("ITEM 5: ninguem apareceu do outro lado -- o vizinho do --aguateste nao nasceu");
					break;
				}
				Node2D? dele = mundo.CorpoDeTeste(_idDoVizinho);
				Vector2 pos = dele?.GlobalPosition ?? Vector2.Zero;
				float lonjura = (mundo.PosicaoLocal ?? Vector2.Zero).DistanceTo(pos);

				Conferir(dele is { Visible: true },
					$"do outro lado do lago ha um corpo DESENHADO (id {_idDoVizinho}, a {lonjura:0} px)");
				// O JUIZ E O VEU, e nao o `Visible` do node: quem esconde corpo atras de parede e o
				// `Visao.Ve` (e a lista de "People" sai dele). Agua nao entra no `.vis`, entao a
				// resposta tem que ser SIM em todo quadro.
				Conferir(_quadrosComVizinho > 0 && _quadrosVendoOVizinho == _quadrosComVizinho,
					$"A AGUA NAO CEGA: o veu enxergou o vizinho em {_quadrosVendoOVizinho} de "
					+ $"{_quadrosComVizinho} quadros");
				Conferir(mundo.NomesVisiveis().Count > 0,
					$"...e ele esta na lista de quem se ve daqui: [{string.Join(", ", mundo.NomesVisiveis())}]");

				Fotografar("user://agua-5-do-outro-lado.png", "ITEM 5 -- ALGUEM DO OUTRO LADO", out _);
				break;
			}

			// =============================================================
			// O VERB, DA BEIRA -- o gesto que o jogador vai fazer
			// =============================================================
			// ============================ ESTAS CONFERENCIAS NASCERAM DE UM DEFEITO ============================
			// A primeira rodada desta bancada mandou `nadar` da beira, recebeu "voce comeca a nadar."
			// e um segundo depois o bit estava APAGADO -- o diario mostrava "12,1s nado LIGOU | 12,1s
			// nado APAGOU" nas duas linhas seguidas, e o corpo passou o teste inteiro a pe. Nao era a
			// bancada que errava: o verb aceitava quem tem agua A FRENTE (`PodeComecarANadar`) e o
			// tique desligava quem nao esta EM CIMA dela (`TickDoNado` -> `SobreAgua`), sem nada entre
			// os dois -- no DM quem preenche esse vao e o `step(usr, usr.dir)` (`Swim.dm:15`), que
			// este porte nao portou.
			//
			// O conserto foi deixar o MODO valer antes de o corpo molhar (o jogador entra andando) e
			// dar ao desligamento por chao seco uma leitura a mais: "ele ja esteve na agua?". Entao a
			// bancada pergunta as TRES coisas separadas -- ligou? aguentou? ENTROU? --, porque um
			// "sim" nas duas primeiras com "nao" na terceira e exatamente o defeito antigo com outra
			// roupa: o nado que vive so enquanto ninguem tenta usa-lo.
			// ==============================================================================================
			case 11:
				_ditos.Clear();
				cli.SendHabilidade("nadar");
				break;

			case 12:
				Conferir(Disse("comeca a nadar"),
					"da BEIRA o verb e ACEITO (o servidor responde 'voce comeca a nadar')");
				_nadoDoSecoAguentou = cli.Sheet.Nadando;
				Conferir(_nadoDoSecoAguentou,
					"...e o nado AGUENTA o primeiro tique do servidor"
					+ (_nadoDoSecoAguentou ? "" : " -- NAO AGUENTOU: o tique desligou antes de o corpo"
						+ " entrar na agua, e o servidor ja disse " + $"'{_ditos.LastOrDefault()}'"));

				// ---------- E AGORA ELE ANDA PRA DENTRO, que e o gesto que faltava ----------
				// O corpo entra pelo PROPRIO movimento do jogador: nao ha empurrao do servidor, e por
				// isso a medida honesta e a distancia percorrida com o bit aceso E os pes na agua.
				_deOndeAndou = mundo.PosicaoLocal ?? Vector2.Zero;
				_pxNadandoNaAgua = 0;
				_chegouNaOutraMargem = false;
				_olhandoATravessia = true;   // dai pra frente as tres fotos por quadro valem
				_medindoTravessia = true;
				_esperasDaTravessia = 0;
				mundo.AndarDeTeste(_rumo);
				break;

			case 13:
			case 14:
			case 15:
			case 16:
			case 17:
			case 18:
				// SEIS SEGUNDOS DE FOLGA: nadando o passo e ate 36% mais lento que andar
				// (`Nado.FatorDePasso`), e o lago escolhido pode ter 12 tiles. Quem chega antes so
				// continua andando no seco -- o que a conferencia pede e o percorrido DENTRO da agua.
				// (A CHEGADA em si e lida por QUADRO -- ver `_medindoTravessia`.)
				break;

			case 19:
			{
				// A FOLGA E ELASTICA, E NAO CRAVADA. Seis segundos era o que bastava pro lago de 5
				// tiles medido na primeira rodada; com 6 tiles e as tres fotos custando quadro, a
				// chegada passou a cair um segundo depois do veredito. Um teste que reprova por ter
				// perguntado cedo demais mede a maquina, e nao o jogo.
				if (!_chegouNaOutraMargem && _esperasDaTravessia++ < 5)
				{
					mundo.AndarDeTeste(_rumo);
					_passo = 18;
					break;
				}

				mundo.PararDeTeste();
				_medindoTravessia = false;
				Vector2 fim = mundo.PosicaoLocal ?? Vector2.Zero;
				float avanco = (fim - _deOndeAndou).Dot(_rumo);

				Conferir(_pxNadandoNaAgua > ZoneCollision.TileSize,
					$"DA BEIRA ELE **ENTRA** NADANDO: {_pxNadandoNaAgua:0} px percorridos com o bit de "
					+ "nado aceso E os pes em cima da agua -- e a unica medida que um corpo parado na "
					+ "margem nao consegue forjar");
				Conferir(_chegouNaOutraMargem && avanco > ZoneCollision.TileSize,
					$"...e saiu no seco da outra margem, {avanco:0} px adiante");
				Conferir(!cli.Sheet.Nadando && Disse("para de nadar"),
					"...e no seco o nado se DESLIGOU sozinho, com aviso (o `unitimer` do DM)");
				Conferir(!Disse("nao chegou a entrar"),
					"...e o prazo de entrada NUNCA venceu (a carencia e pra entrar, nao pra nadar no seco)");

				// AS TRES FOTOS DA TRAVESSIA, conferidas COMO FOTOS: um "ok" de numero com o arquivo
				// faltando e a bancada dizendo que olhou sem ter olhado.
				Conferir(_fotoDaEntrada, "saiu a foto do quadro em que os PES ENCOSTAM na agua");
				Conferir(_fotoDoMeio, "saiu a foto do MEIO do rio");
				Conferir(_fotoDaSaida, "saiu a foto da SAIDA na outra margem");

				Fotografar("user://agua-7-entrou-da-beira.png", "DA BEIRA -- ENTROU NADANDO", out _);
				_olhandoATravessia = false;
				_passo = 70;   // ...e agora o contrario: longe da agua o verb tem que recusar
				break;
			}

			// =============================================================
			// O OPOSTO DO DEFEITO -- longe da agua o verb tem que RECUSAR
			// =============================================================
			// ============================ POR QUE ESTA FASE EXISTE ============================
			// O conserto que fez o jogador entrar no lago foi deixar o modo `Nadando` valer ANTES de o
			// corpo molhar. Isso abre uma pergunta que nao existia: e se der pra ligar o nado em
			// qualquer lugar? Quem segura essa porta e a clausula "agua a um tile" do
			// `PodeComecarANadar`, mais o prazo de entrada de `Nado.PrazoParaEntrar` segundos que nao
			// rearma.
			//
			// Sem esta fase, os dois defeitos opostos ficariam verdes na mesma bancada: o de nao
			// conseguir entrar (que era o de ontem) e o de virar um botao de andar por cima de todo
			// lago do mapa a partir de qualquer lugar.
			// =================================================================================
			case 70:
				_tentativasLonge = 0;
				mundo.AndarDeTeste(_rumo);   // adiante, pro seco, de costas pro lago
				break;

			case 71:
			case 72:
			case 73:
				break;   // andando pra longe da beira

			case 74:
			{
				mundo.PararDeTeste();
				int tiles = TilesAteAAguaMaisProxima();

				// MAIS UM SEGUNDO, ATE TRES VEZES: a distancia que se quer e do MAPA, e nao do relogio.
				// Cravar "quatro segundos de caminhada" faria a fase medir a velocidade do personagem.
				if (tiles <= 2 && _tentativasLonge++ < 3)
				{
					mundo.AndarDeTeste(_rumo);
					_passo = 73;
					break;
				}

				Conferir(tiles > 2,
					$"o corpo esta LONGE DA AGUA: a celula de agua mais proxima esta a {tiles} tiles");
				_ditos.Clear();
				cli.SendHabilidade("nadar");
				break;
			}

			case 75:
			{
				Conferir(!cli.Sheet.Nadando,
					"LONGE DA AGUA O VERB NAO LIGA NADA: o bit `Nadando` continua apagado");
				Conferir(Disse("nao da pra nadar aqui"),
					$"...e o servidor disse por que ('{_ditos.LastOrDefault() ?? ""}') -- e o "
					+ "\"You can't swim there!\" do `Swim.dm:23`");
				Conferir(mundo.AnimacaoLocalDeTeste.StartsWith("idle") || mundo.AlturaDeTeste == 0f,
					$"...e o corpo continua a pe no chao (animacao \"{mundo.AnimacaoLocalDeTeste}\", "
					+ $"altura {mundo.AlturaDeTeste:0} px)");
				Fotografar("user://agua-14-longe-da-agua.png", "LONGE DA AGUA -- O VERB RECUSA", out _);
				_passo = 200;   // o roteiro do seco acaba aqui
				break;
			}

			// =============================================================
			// ROTEIRO MOLHADO (`--aguadentro`): itens 2 e 3
			// =============================================================
			// ============================ POR QUE ESTE ROTEIRO EXISTE ============================
			// Ele nasceu como MULETA: os dois caminhos que um jogador tem pra comecar a nadar (a
			// margem, no passo 12, e o ar, no passo 19) estavam quebrados, e sem comecar a nadar nao
			// havia o que fotografar -- os itens 2 e 3 do pedido ficariam sem resposta. Os dois ja
			// foram consertados e os dois roteiros medem verde, entao esta muleta nao e mais a unica
			// porta pro nado.
			//
			// E MESMO ASSIM ELE FICA, por dois motivos. Primeiro, o estado que ele monta NAO e
			// invencao da bancada: o proprio servidor diz que existe e o trata de proposito
			// ("deslogar dentro do lago, ser jogado la por um arremesso, nascer perto demais da
			// beira" -- `GameServer.Nado.PodeComecarANadar`). Segundo, ele e o unico que mede o MEIO
			// da travessia sem gastar quadro chegando la, que e onde os itens 2 e 3 vivem.
			//
			// O QUE ELE NAO SABE MEDIR ESTA ESCRITO AQUI DE PROPOSITO: nascendo com o corpo ja
			// molhado, ele ficou VERDE durante os dois defeitos de ENTRADA. Um roteiro que nasce
			// dentro do estado nunca testa a porta de entrada dele -- e por isso os outros dois
			// bercos existem.
			// =================================================================================
			case 30:
				Conferir(SobreAgua(), "o corpo comeca DENTRO do lago (o estado que o servidor preve)");
				Conferir(!cli.Sheet.Nadando, "...e ainda nao esta nadando -- e o verb que vai ligar");
				_meioDoLago = mundo.PosicaoLocal ?? Vector2.Zero;
				AcharTravessia(out _rumo, out _larguraDoLago);
				_ditos.Clear();
				cli.SendHabilidade("nadar");
				break;

			case 31:
			{
				Conferir(cli.Sheet.Nadando, "NADANDO no meio do lago (bit `Nadando` na ficha)");
				Conferir(Disse("comeca a nadar"), "...e o servidor avisou por chat");
				Conferir(SobreAgua() && _nadouSobreAgua,
					"...em cima da agua, onde a pe ele nao pisa (e isso valeu por quadros)");

				Conferir(mundo.AnimacaoLocalDeTeste.StartsWith("flight"),
					$"A POSE E A DO VOO (animacao \"{mundo.AnimacaoLocalDeTeste}\")");
				Conferir(mundo.AlturaDeTeste == 0f && _maiorAlturaNadando == 0f,
					$"NAO SUBIU: altura {mundo.AlturaDeTeste:0} px agora, e a maior vista nadando foi "
					+ $"{_maiorAlturaNadando:0} px");
				Conferir(Vao() < 1f,
					$"...e o DESENHO nao descolou do no ({Vao():0.0} px) -- e ai que a subida apareceria");
				Conferir(!SombraVisivel() && !_sombraNadando,
					"SEM SOMBRA: o node `SombraDeVoo` nunca ficou visivel enquanto ele nadava");

				// ============================ AS DUAS FOTOS TEM QUE OLHAR PRO MESMO LADO ============================
				// A folha tem quatro direcoes, e a primeira rodada saiu com o nado de FRENTE
				// (`flight_south`, so a cabeca fora d'agua) e o voo de PERFIL (`flight_west`). As duas
				// fotos ficam diferentes -- mas por causa da DIRECAO, e a pergunta do dono e sobre a
				// sombra e a altura. Uma comparacao que muda duas coisas de uma vez nao responde qual
				// delas fez a diferenca.
				//
				// Entao os dois retratos sao tirados com o corpo virado pro rumo da travessia. Aqui um
				// arranque de menos de um tile (o freio e por quadro, ver `_mirando`) so pra girar.
				// ==============================================================================================
				_mirando = (mundo.PosicaoLocal ?? Vector2.Zero) + _rumo * 20f;
				mundo.AndarDeTeste(_rumo);
				break;
			}

			case 32:
				mundo.PararDeTeste();
				_mirando = null;
				Conferir(SobreAgua(), "ainda em cima da agua na hora da foto");
				_fotoNadando = Fotografar("user://agua-2-nadando.png", "ITEM 2 -- NADANDO NO MEIO DA AGUA",
										  out _naTelaNadando);
				break;

			// ---------- A TRAVESSIA, ainda nadando ----------
			// ELA VEM ANTES DA FOTO DO VOO, e a ordem e obrigatoria: ligar o voo derruba o nado, e
			// voltar pro nado depois cai no defeito do passo 19 (o pouso devolve o corpo pra margem).
			// Medida na ordem errada, a travessia sai VERDE com o corpo andando no seco -- foi o que a
			// rodada anterior desta bancada mediu, e o numero que a desmente e o `_pxNadandoNaAgua`.
			case 33:
				_deOndeAndou = mundo.PosicaoLocal ?? Vector2.Zero;
				_pxNadandoNaAgua = 0;
				_medindoTravessia = true;
				_esperasDaTravessia = 0;
				mundo.AndarDeTeste(_rumo);
				break;

			case 34:
			case 35:
			case 36:
				break;   // atravessando (a chegada e lida por QUADRO -- ver `_medindoTravessia`)

			case 37:
			{
				// A MESMA FOLGA ELASTICA DO PASSO 19, e pelo mesmo motivo.
				if (!_chegouNaOutraMargem && _esperasDaTravessia++ < 5)
				{
					mundo.AndarDeTeste(_rumo);
					_passo = 36;
					break;
				}

				mundo.PararDeTeste();
				_medindoTravessia = false;
				Vector2 fim = mundo.PosicaoLocal ?? Vector2.Zero;
				float avanco = (fim - _deOndeAndou).Dot(_rumo);
				Conferir(_pxNadandoNaAgua > ZoneCollision.TileSize,
					$"ATRAVESSOU **NADANDO**: {_pxNadandoNaAgua:0} px percorridos com o bit de nado "
					+ "aceso E os pes em cima da agua -- e a unica medida que um corpo andando no "
					+ "seco nao consegue forjar");
				Conferir(_chegouNaOutraMargem && avanco > ZoneCollision.TileSize,
					$"...e saiu no seco do outro lado, {avanco:0} px adiante");
				Conferir(!cli.Sheet.Nadando && Disse("para de nadar"),
					"...e no seco o nado se DESLIGOU sozinho, com aviso (o `unitimer` do DM)");
				Fotografar("user://agua-6-outra-margem.png", "A TRAVESSIA -- CHEGOU NA MARGEM", out _);
				break;
			}

			// ---------- ITEM 3: o voo, no MESMO ponto e olhando pro MESMO lado ----------
			case 38:
				_ditos.Clear();
				cli.SendHabilidade("voar");
				break;

			case 39:
				Conferir(mundo.AlturaDeTeste > 0f, $"decolou pra voltar ao ponto da foto ({mundo.AlturaDeTeste:0} px)");
				// PASSA DO PONTO DE PROPOSITO: voltar so ate o meio deixaria o corpo virado pra tras.
				// Ele volta um pouco alem e faz a ultima aproximacao no rumo da travessia, que e o
				// mesmo lado pro qual a foto do nado olha.
				_mirando = _meioDoLago;
				mundo.AndarDeTeste(-_rumo);
				break;

			// A VOLTA E LONGA: a travessia terminou uns 500 px adiante e o voo faz uns 100 px por
			// segundo. Cinco passos com folga -- quem chega antes fica parado pelo freio de quadro
			// (`_mirando`), e quem chegasse depois tiraria a foto no lugar errado, que foi a rodada
			// em que o retrato do voo saiu em cima do CAPIM e a comparacao mediu dois terrenos.
			case 40:
			case 41:
			case 42:
			case 43:
			case 44:
				break;

			case 45:
			{
				mundo.PararDeTeste();
				float erro = (mundo.PosicaoLocal ?? Vector2.Zero).DistanceTo(_meioDoLago);
				Conferir(erro < ZoneCollision.TileSize,
					$"voltou pro MESMO ponto da foto do nado (erro {erro:0} px)");
				// O MESMO ARRANQUE CURTO DA FOTO DO NADO, so pra virar o corpo pro mesmo lado.
				_mirando = (mundo.PosicaoLocal ?? Vector2.Zero) + _rumo * 20f;
				mundo.AndarDeTeste(_rumo);
				break;
			}

			case 46:
			{
				mundo.PararDeTeste();
				_mirando = null;
				Conferir(SobreAgua(), "esta de volta EM CIMA DA AGUA, agora voando");
				Conferir(mundo.AlturaDeTeste > 0f,
					$"VOANDO SOBRE A MESMA AGUA o corpo SAIU do chao ({mundo.AlturaDeTeste:0} px)");
				Conferir(!cli.Sheet.Nadando, "...e o voo desligou o nado (os dois se excluem)");
				Conferir(SombraVisivel(), "VOANDO A SOMBRA APARECE -- e ela que nadando nunca apareceu");
				Conferir(Vao() > 8f,
					"...e o desenho subiu " + $"{Vao():0}" + " px acima do no (nadando eram 0)");
				Conferir(mundo.AnimacaoLocalDeTeste.StartsWith("flight"),
					$"...com a MESMA pose de folha (\"{mundo.AnimacaoLocalDeTeste}\") -- e por isso a "
					+ "sombra e a altura sao a unica diferenca que resta");

				_fotoVoando = Fotografar("user://agua-3-voando.png", "ITEM 3 -- VOANDO NA MESMA AGUA",
										 out _naTelaVoando);
				CompararAsDuasFotos();
				LadoALado();
				_passo = 200;
				break;
			}

			// =============================================================
			// ROTEIRO DO MURO (`--aguaparede`): nadando, a parede ainda para?
			// =============================================================
			// ============================ POR QUE ELE E UM BERCO PROPRIO ============================
			// O conserto que fez o jogador entrar no lago mandou o MODO junto do passo. O risco dele nao e
			// o `ZoneCollision.Bloqueia` (parede para em todo modo, e isso e uma linha so): e a SAIDA DE
			// EMERGENCIA do `MoveRules.Advance`, que ANTES devolvia o passo CHEIO sem conferir nada. Com o
			// modo `APe`, todo corpo em cima de agua caia nela, e qualquer passo dele passava sem checagem,
			// muro incluso. Com `Nadando` a agua nao o prende mais e a checagem volta a valer -- e e isso
			// que uma foto de um nadador barrado por um muro prova.
			//
			// (A saida de emergencia deixou de aprovar tudo: hoje ela e `MoveRules.Escapar`, que nao
			// existe pra quem esta parado pelo MODO e so libera o passo que APROXIMA de um lugar valido.
			// Esta bancada continua valendo -- ela mede o nadador barrado pelo muro, que e outro eixo.)
			//
			// E ELE NAO CABIA NOS OUTROS TRES, o que foi medido e nao suposto: o muro mais proximo do lago
			// daqueles bercos esta a 15 tiles, o corpo anda ~35 px/s e o nado cobra Ki por segundo. As duas
			// tentativas de alcancar aquele muro (a nado e de voo) terminaram iguais, na EXAUSTAO, antes
			// de o muro caber na foto.
			// =======================================================================================
			case 100:
			{
				Conferir(SobreAgua() && mundo.AlturaDeTeste == 0f,
					"o corpo comeca DENTRO da agua, a um tile de um muro (berco `--aguaparede`)");
				Conferir(!cli.Sheet.Nadando, "...e ainda nao esta nadando -- e o verb que vai ligar");

				if (!AcharParedeNaAgua(out Vector2 encosto, out Vector2 rumo, out Vector2I parede, 2))
				{
					// HONESTO E DIZER QUE NAO MEDIU. Um "ok" aqui seria verde por ausencia -- a mesma
					// doenca do berco que nasce dentro do estado que devia testar.
					Nota("A PAREDE NAO FOI MEDIDA: o berco nao poe muro nenhum ao alcance");
					_passo = 200;
					break;
				}

				_encostoDaParede = encosto;
				_rumoDaParede = rumo;
				_celulaDaParede = parede;
				_antesDaViagem = mundo.PosicaoLocal ?? Vector2.Zero;
				Nota($"muro em ({parede.X},{parede.Y}), rumo do empurrao {rumo}; o corpo comeca em "
					 + $"({_antesDaViagem.X:0},{_antesDaViagem.Y:0}), a {_antesDaViagem.DistanceTo(encosto):0} px "
					 + "da ultima celula de agua");

				_ditos.Clear();
				cli.SendHabilidade("nadar");
				break;
			}

			case 101:
			{
				Conferir(cli.Sheet.Nadando && SobreAgua(),
					"NADANDO na agua colada no muro (bit `Nadando` na ficha)");
				Conferir(Disse("comeca a nadar"), "...e o servidor avisou por chat");
				Fotografar("user://agua-15-antes-do-muro.png", "O MURO 1/2 -- NADANDO, UM TILE ANTES", out _);

				// ---------- E AGORA ANDA CONTRA ELE ----------
				_antesDoEmpurrao = mundo.PosicaoLocal ?? Vector2.Zero;
				_entrouEmParede = false;
				_empurrouNadando = false;
				_medindoParede = true;
				mundo.AndarDeTeste(_rumoDaParede);
				break;
			}

			case 102:
				// O TILE DE ARRANQUE. Um tile a ~35 px/s custa um segundo -- e a primeira versao desta
				// fase leu o "ultimo segundo" JA NESTE PONTO, com o corpo ainda chegando: ela mediu
				// 12 px de avanco e reprovou o muro por ter perguntado cedo demais.
				break;

			case 103:
				// AGORA ELE JA ESTA ENCOSTADO. Daqui pro veredito, com a tecla apertada o tempo todo, o
				// numero honesto e zero.
				_ondeParouNoPenultimo = mundo.PosicaoLocal ?? Vector2.Zero;
				break;

			case 104:
			{
				Vector2 fim = mundo.PosicaoLocal ?? Vector2.Zero;
				float avanco = (fim - _antesDoEmpurrao).Dot(_rumoDaParede);
				float noUltimoSegundo = (fim - _ondeParouNoPenultimo).Dot(_rumoDaParede);

				// A FOTO SAI COM O CORPO AINDA EMPURRANDO, e nao depois de parar: e o encosto que se quer
				// ver, e soltar o piloto antes tiraria o retrato de um corpo so parado perto de um muro.
				Fotografar("user://agua-16-parado-no-muro.png", "O MURO 2/2 -- A PAREDE PARA QUEM NADA", out _);
				mundo.PararDeTeste();
				_medindoParede = false;

				Conferir(_empurrouNadando,
					"o empurrao foi dado NADANDO e em cima da agua -- e o unico estado em que a saida de "
					+ "emergencia do `Advance` chegou a engolir a checagem");
				Conferir(!_entrouEmParede,
					"A PAREDE PARA QUEM NADA: em nenhum quadro os pes ficaram dentro da celula de muro");
				// O AVANCO TOTAL TEM UM TILE DE FOLGA LEGITIMA: o corpo nasce um tile antes e ANDA contra o
				// muro -- e esse passo que a bancada queria. O que nao pode e ele seguir depois de encostar.
				Conferir(avanco < ZoneCollision.TileSize * 1.75f,
					$"...e parou no muro: {avanco:0} px de avanco no total (o tile de arranque e ~40 px)");
				Conferir(Math.Abs(noUltimoSegundo) < 4f,
					$"...e no ULTIMO segundo, ainda com a tecla apertada, ele andou {noUltimoSegundo:0.0} px "
					+ "-- e isto e o muro parando, e nao a bancada tendo soltado o piloto");
				// O JUIZ AQUI E O `BlockedCell` E NAO O `DentroDeParedeDeTeste`: aquele pergunta pelo
				// `MoveRules.Occupied` SEM modo, ou seja, A PE -- e a pe a agua conta como bloqueio. Ele
				// responderia "esta dentro de parede" pra todo corpo em cima do lago, com o muro intacto a
				// um tile de distancia.
				Conferir(!ParedeSobOsPes(), "...e ele terminou FORA do muro");
				Conferir(SobreAgua() && cli.Sheet.Nadando,
					"...e continua NADANDO em cima da agua no fim (nao foi jogado pro seco)");

				_passo = 200;
				break;
			}

			// =============================================================
			// ROTEIRO DO AR (`--aguanoar`): o POUSO em cima da agua
			// =============================================================
			// ============================ POR QUE ESTE ROTEIRO EXISTE ============================
			// Sao DOIS os caminhos que um jogador tem pra comecar a nadar -- da margem e do ar --, e
			// eles nao passam pelo mesmo codigo. O do ar passa pelo `DescerAte` (`GameServer.Voo.cs`),
			// que decide se o chao debaixo do corpo serve: era ali que morava o `Occupied` SEM MODO,
			// a pergunta A PE, e a pe a agua barra. O pouso do nadador caia no desvio do "pousou
			// dentro da pedra", o corpo era teleportado pra margem e o tique seguinte apagava o nado.
			//
			// Nenhum dos outros dois bercos alcanca isso: o mergulhado (`--aguadentro`) ja esta no
			// chao, e o da margem teria que decolar e atravessar antes de pousar -- tres gestos, e um
			// vermelho ambiguo entre eles.
			// =================================================================================
			case 60:
			{
				Conferir(mundo.AlturaDeTeste > 0f,
					$"o corpo comeca NO AR ({mundo.AlturaDeTeste:0} px) -- rode com --aguanoar");
				Conferir(SobreAgua(), "...e por cima da agua (a mira do berco caiu no meio do lago)");
				Conferir(!cli.Sheet.Nadando, "...e ainda nao esta nadando -- e o verb que vai ligar");
				Conferir(SombraVisivel(),
					"VOANDO A SOMBRA APARECE (o contra-exemplo: e ela que depois do pouso tem que sumir)");

				// O RUMO DA SAIDA sai do MESMO `.agua` que o cliente usa pra desenhar, e nao do
				// servidor: se as duas pontas discordarem sobre onde acaba o lago, e aqui que aparece.
				bool achou = AcharTravessia(out _rumo, out _larguraDoLago);
				Conferir(achou, achou
					? $"e ha por onde sair nadando: rumo {_rumo}, {_larguraDoLago} tiles"
					: "nao ha agua nenhuma ao redor -- rode com --aguanoar");
				if (!achou) { _passo = 200; break; }

				_ditos.Clear();
				cli.SendHabilidade("nadar");   // no ar o verb pega, e o voo cai junto
				break;
			}

			case 61:
			case 62:
				break;   // caindo do voo pra dentro da agua (a queda e de 16 tiles/s: acaba num piscar)

			case 63:
			{
				Conferir(Disse("comeca a nadar"), "sobre a agua o verb e ACEITO tambem no ar");
				Conferir(mundo.AlturaDeTeste == 0f,
					$"o corpo POUSOU de verdade (altura {mundo.AlturaDeTeste:0} px)");

				bool sobreviveu = cli.Sheet.Nadando && SobreAgua();
				Conferir(sobreviveu,
					"O NADO SOBREVIVE AO POUSO EM CIMA DA AGUA"
					+ (sobreviveu ? "" : " -- NAO SOBREVIVEU: o pouso tratou a agua como chao ruim"
						+ $" e devolveu o corpo pra margem ('{_tudoQueFoiDito.LastOrDefault()}')"));

				// O DESVIO TEM MENSAGEM PROPRIA, e e por ela que se separa "o nado caiu" de "o pouso
				// TELEPORTOU o corpo". Sem esta linha, um desvio que por acaso caisse noutra celula de
				// agua passaria despercebido -- e o defeito continuaria vivo esperando um lago maior.
				Conferir(!Disse("desce ao lado"),
					"...e o pouso NAO desviou o corpo pra margem (o `DescerAte` perguntou com o MODO)");
				// SO O INSTANTANEO, e nao o acumulado `_sombraNadando`: neste roteiro o corpo passa uns
				// poucos quadros CAINDO ja com o nado ligado, e nesses quadros a sombra e do VOO que
				// ainda nao acabou. O acumulado ficaria vermelho com o pouso perfeito.
				Conferir(!SombraVisivel(),
					"SEM SOMBRA depois do pouso: o node `SombraDeVoo` apagou junto com o voo");

				Fotografar("user://agua-9-pouso-na-agua.png", "DO AR -- POUSOU NADANDO", out _);

				// ---------- e daqui ele sai nadando, que e o que o pouso tinha impedido ----------
				_deOndeAndou = mundo.PosicaoLocal ?? Vector2.Zero;
				_pxNadandoNaAgua = 0;
				_chegouNaOutraMargem = false;
				_medindoTravessia = true;
				_esperasDaTravessia = 0;
				mundo.AndarDeTeste(_rumo);
				break;
			}

			case 64:
			case 65:
			case 66:
			case 67:
				break;   // saindo do pouso (a chegada e lida por QUADRO -- ver `_medindoTravessia`)

			case 68:
			{
				// A MESMA FOLGA ELASTICA DO PASSO 19, e pelo mesmo motivo.
				if (!_chegouNaOutraMargem && _esperasDaTravessia++ < 5)
				{
					mundo.AndarDeTeste(_rumo);
					_passo = 67;
					break;
				}

				mundo.PararDeTeste();
				_medindoTravessia = false;
				Conferir(_pxNadandoNaAgua > ZoneCollision.TileSize,
					$"...e do pouso ele SEGUE nadando: {_pxNadandoNaAgua:0} px com o bit aceso e os pes "
					+ "na agua");
				Conferir(_chegouNaOutraMargem && !cli.Sheet.Nadando && Disse("para de nadar"),
					"...ate sair no seco, onde o nado se DESLIGA sozinho");
				Fotografar("user://agua-10-saiu-do-ar.png", "DO AR -- SAIU NA MARGEM", out _);
				_passo = 200;
				break;
			}

			default:
			{
				_acabou = true;
				GD.Print("\n[agua] ===== BANCADA DA AGUA =====");
				foreach (string l in _passos) GD.Print("[agua] " + l);
				GD.Print("[agua] ---- diario do bit de nado ----");
				foreach (string l in _diarioDoNado) GD.Print("[agua]   " + l);
				GD.Print("[agua] ---- o que o servidor falou ----");
				foreach (string l in _tudoQueFoiDito) GD.Print("[agua]   " + l);
				GD.Print(_falhas.Count == 0
					? "[agua] ===== TUDO OK ====="
					: $"[agua] ===== {_falhas.Count} FALHA(S) =====\n[agua]   " + string.Join("\n[agua]   ", _falhas));
				GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit();
				break;
			}
		}
	}

	// =====================================================================
	// MEDIDAS DE APOIO
	// =====================================================================
	/// <summary>Quantos pixels ate a primeira celula de agua no rumo da travessia.</summary>
	private float DistanciaAteAAgua(Vector2 de)
	{
		if (World.Instancia?.Colisao is not { } mapa) return float.MaxValue;
		const int t = ZoneCollision.TileSize;
		for (int i = 1; i <= 240; i++)
		{
			Vector2 p = de + _rumo * i;
			if (mapa.EhAgua((int)MathF.Floor(p.X / t), (int)MathF.Floor((p.Y + MoveRules.FeetOffsetY) / t)))
				return i;
		}
		return float.MaxValue;
	}

	/// <summary>
	/// OS PES ESTAO DENTRO DE UMA CELULA DE **MURO**? -- a caixa dos pes, os quatro cantos.
	///
	/// A pergunta e so pelo `BlockedCell`, e nao pelo `MoveRules.Occupied`: aquele responde "isto para
	/// um corpo A PE", e a pe a agua tambem para -- ele diria "dentro de parede" pra todo nadador, com
	/// o muro a dez tiles de distancia. Os quatro cantos e a MESMA caixa que o `Advance` consulta
	/// (`BodyHalfW`/`BodyHalfH`, ja descontado o `FeetOffsetY`).
	/// </summary>
	private bool ParedeSobOsPes()
	{
		if (World.Instancia is not { Colisao: { } mapa } mundo || mundo.PosicaoLocal is not { } p) return false;
		const int t = ZoneCollision.TileSize;
		float y = p.Y + MoveRules.FeetOffsetY;
		foreach ((float dx, float dy) in new[]
				 {
					 (-MoveRules.BodyHalfW, -MoveRules.BodyHalfH), (MoveRules.BodyHalfW, -MoveRules.BodyHalfH),
					 (-MoveRules.BodyHalfW, MoveRules.BodyHalfH), (MoveRules.BodyHalfW, MoveRules.BodyHalfH),
				 })
			if (mapa.BlockedCell((int)MathF.Floor((p.X + dx) / t), (int)MathF.Floor((y + dy) / t)))
				return true;
		return false;
	}

	/// <summary>
	/// A CELULA DE AGUA MAIS PROXIMA, EM TILES -- busca por aneis, ate 12 tiles.
	///
	/// Serve pra fase do "longe da agua": o que faz o verb recusar e a clausula "agua a um tile"
	/// (`PodeComecarANadar`), entao a fase precisa PROVAR que o corpo esta longe em vez de supor que
	/// andar quatro segundos basta. Devolve 99 quando nao ha agua nenhuma no raio.
	/// </summary>
	private int TilesAteAAguaMaisProxima(int raio = 12)
	{
		if (World.Instancia is not { Colisao: { } mapa } mundo || mundo.PosicaoLocal is not { } p) return 99;
		const int t = ZoneCollision.TileSize;
		int cx = (int)MathF.Floor(p.X / t), cy = (int)MathF.Floor((p.Y + MoveRules.FeetOffsetY) / t);
		for (int r = 0; r <= raio; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r) continue;   // so a casca
					if (mapa.EhAgua(cx + dx, cy + dy)) return r;
				}
		return 99;
	}

	/// <summary>
	/// ACHA UM MURO DE VERDADE QUE DE PRA ENCOSTAR **SEM SAIR DA AGUA**.
	///
	/// ============================ POR QUE O CAMINHO TEM QUE SER TODO MOLHADO ============================
	/// O que se quer medir e a parede parando um corpo que esta NADANDO. Um caminho que passe por um
	/// pedaco de chao seco desliga o nado no meio dele (`TickDoNado`, "ja molhou -> chao seco desliga
	/// neste tique"), e o que chegaria no muro seria um pedestre -- medindo uma coisa que nunca esteve
	/// em duvida.
	///
	/// Por isso a reta ate o encosto e amostrada de 4 em 4 px e TODA celula do caminho tem que ser
	/// agua. O piloto automatico anda em linha reta (ver `AndarDeTeste`), entao a reta amostrada aqui
	/// e o caminho de verdade, e nao uma aproximacao.
	///
	/// Devolve o PONTO DE ENCOSTO (a posicao do corpo que poe os pes no centro da ultima celula de
	/// agua), o RUMO do empurrao (a cardinal que aponta pro muro) e a celula do muro.
	/// ================================================================================================
	/// </summary>
	private bool AcharParedeNaAgua(out Vector2 encosto, out Vector2 rumo, out Vector2I parede, int raio = 24)
	{
		encosto = Vector2.Zero;
		rumo = Vector2.Zero;
		parede = Vector2I.Zero;
		if (World.Instancia is not { Colisao: { } mapa } mundo || mundo.PosicaoLocal is not { } p) return false;

		const int t = ZoneCollision.TileSize;
		var pe = new Vector2(p.X, p.Y + MoveRules.FeetOffsetY);
		int cx = (int)MathF.Floor(pe.X / t), cy = (int)MathF.Floor(pe.Y / t);
		(int dx, int dy)[] cardeais = [(1, 0), (-1, 0), (0, 1), (0, -1)];

		// POR ANEIS: o muro escolhido e o MAIS PERTO, e nao o primeiro que a varredura encontrar. Um
		// alvo do outro lado do lago custaria meio minuto de nado e sairia da tela na hora da foto.
		for (int r = 1; r <= raio; r++)
			for (int dy0 = -r; dy0 <= r; dy0++)
				for (int dx0 = -r; dx0 <= r; dx0++)
				{
					if (Math.Abs(dx0) != r && Math.Abs(dy0) != r) continue;
					int ax = cx + dx0, ay = cy + dy0;
					if (!mapa.EhAgua(ax, ay)) continue;

					foreach ((int dx, int dy) in cardeais)
					{
						if (!mapa.BlockedCell(ax + dx, ay + dy)) continue;
						var centro = new Vector2(ax * t + t / 2f, ay * t + t / 2f);
						if (!SoAguaNaReta(mapa, pe, centro)) continue;

						encosto = new Vector2(centro.X, centro.Y - MoveRules.FeetOffsetY);
						rumo = new Vector2(dx, dy);
						parede = new Vector2I(ax + dx, ay + dy);
						return true;
					}
				}
		return false;
	}

	/// <summary>Toda celula tocada pela reta (de 4 em 4 px) e agua?</summary>
	private static bool SoAguaNaReta(ZoneCollision mapa, Vector2 de, Vector2 ate)
	{
		const int t = ZoneCollision.TileSize;
		int n = Math.Max(1, (int)(de.DistanceTo(ate) / 4f));
		for (int i = 0; i <= n; i++)
		{
			Vector2 q = de.Lerp(ate, i / (float)n);
			if (!mapa.EhAgua((int)MathF.Floor(q.X / t), (int)MathF.Floor(q.Y / t))) return false;
		}
		return true;
	}

	/// <summary>A celula que o punho alcanca continua sendo agua?</summary>
	private bool AguaAindaNaFrente()
	{
		if (World.Instancia is not { Colisao: { } mapa } mundo || mundo.PosicaoLocal is not { } p) return false;
		const int t = ZoneCollision.TileSize;
		Vector2 alvo = new Vector2(p.X, p.Y + MoveRules.FeetOffsetY) + _rumo * t;
		return mapa.EhAgua((int)MathF.Floor(alvo.X / t), (int)MathF.Floor(alvo.Y / t));
	}

	/// <summary>
	/// ============================ "DA PRA VER QUE E AGUA?" ============================
	/// A pergunta do dono nao e sobre o dado, e sobre o DESENHO -- e a resposta honesta e comparar
	/// dois pedacos da MESMA foto: o chao onde o corpo esta e o lago logo a frente. Se os dois
	/// derem a mesma cor, o jogador nao tem como saber onde acaba o chao.
	///
	/// A bancada NAO exige "azul": lava tambem e agua neste porte (`LavaHD` chama `testWaters`), e
	/// um teste que exigisse azul reprovaria um lago de lava desenhado perfeitamente. O que ela
	/// exige e DIFERENCA, e o relatorio leva as duas cores pra quem quiser julgar o tom.
	/// ==============================================================================
	/// </summary>
	private void MedirAsDuasMargens(Image foto, Vector2 onde)
	{
		const int t = ZoneCollision.TileSize;
		Vector2 pe = new(onde.X, onde.Y + MoveRules.FeetOffsetY);
		Color? seco = CorMedia(foto, NaTela(pe - _rumo * t));                       // um tile atras
		Color? agua = CorMedia(foto, NaTela(pe + _rumo * (DistanciaAteAAgua(onde) + t)));

		if (seco is not { } cs || agua is not { } ca)
		{
			Nota("nao deu pra medir a cor das margens (a mira caiu fora da janela)");
			return;
		}

		float dif = Math.Abs(cs.R - ca.R) + Math.Abs(cs.G - ca.G) + Math.Abs(cs.B - ca.B);
		Conferir(dif > 0.10f,
			$"DA PRA VER QUE E AGUA: o chao seco {Hex(cs)} e o lago {Hex(ca)} sao cores diferentes "
			+ $"(diferenca {dif:0.00} de 3,00)");
		Nota($"a agua e {(ca.B > ca.R ? "mais AZUL que vermelha" : "mais vermelha que azul")}"
			 + $" -- azul {ca.B * 255:0}, vermelho {ca.R * 255:0}");
	}

	/// <summary>
	/// AS DUAS FOTOS COLADAS NUMA IMAGEM SO -- nadando a esquerda, voando a direita.
	///
	/// O dono pediu "compare LADO A LADO", e duas imagens em pastas diferentes obrigam quem olha a
	/// alternar entre elas de memoria. Coladas, a sombra que aparece de um lado e nao do outro e a
	/// diferenca de altura ficam na mesma sacada de olho. E a mesma mira e o mesmo tamanho dos dois
	/// recortes -- se um deles estiver deslocado, isso tambem aparece aqui.
	/// </summary>
	private void LadoALado()
	{
		if (_fotoNadando is not { } a || _fotoVoando is not { } b) return;
		if (Recorte(a, _naTelaNadando) is not { } ra || Recorte(b, _naTelaVoando) is not { } rb) return;

		int w = Math.Min(ra.GetWidth(), rb.GetWidth()), h = Math.Min(ra.GetHeight(), rb.GetHeight());
		const int vinco = 6;   // um vinco escuro no meio, pra a juncao nao passar por continuacao da agua
		var juntas = Image.CreateEmpty(w * 2 + vinco, h, false, ra.GetFormat());
		juntas.Fill(new Color(0, 0, 0));
		juntas.BlitRect(ra, new Rect2I(0, 0, w, h), Vector2I.Zero);
		juntas.BlitRect(rb, new Rect2I(0, 0, w, h), new Vector2I(w + vinco, 0));

		try
		{
			string caminho = ProjectSettings.GlobalizePath("user://agua-8-nadar-x-voar.png");
			juntas.SavePng(caminho);
			_passos.Add($"  ok     ITEM 3 -- LADO A LADO (nadando | voando): {caminho}");
		}
		catch (Exception e) { Nota("nao deu pra colar as duas fotos: " + e.Message); }
	}

	/// <summary>
	/// ============================ O ITEM 3 TEM QUE SAIR DIFERENTE NA TELA ============================
	/// O dono foi explicito: *"se sairem iguais, o pedido nao foi cumprido"*. Numeros dizem que a
	/// altura mudou e que a sombra acendeu; o que eles NAO dizem e se aquilo virou pixel. Um efeito
	/// escrito num campo que nenhum desenho le passa nas duas conferencias de cima e nao muda nada
	/// na tela -- ja aconteceu neste projeto (a tinta que morria na fronteira node->material).
	///
	/// Entao os dois recortes -- mesmo tamanho, MESMA mira, o corpo no meio -- sao comparados pixel
	/// a pixel. A camera nao para no mesmo lugar nas duas fotos, entao o que se compara e o RECORTE
	/// centrado no corpo, e nao a tela inteira.
	/// ============================================================================================
	/// </summary>
	private void CompararAsDuasFotos()
	{
		if (_fotoNadando is not { } a || _fotoVoando is not { } b)
		{
			Nota("ITEM 3: nao deu pra comparar as fotos (uma delas nao saiu)");
			return;
		}
		if (Recorte(a, _naTelaNadando) is not { } ra || Recorte(b, _naTelaVoando) is not { } rb)
		{
			Nota("ITEM 3: nao deu pra recortar as duas fotos no mesmo tamanho");
			return;
		}
		int w = Math.Min(ra.GetWidth(), rb.GetWidth()), h = Math.Min(ra.GetHeight(), rb.GetHeight());
		long diferentes = 0, total = 0;
		double soma = 0;
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				Color ca = ra.GetPixel(x, y), cb = rb.GetPixel(x, y);
				float d = Math.Abs(ca.R - cb.R) + Math.Abs(ca.G - cb.G) + Math.Abs(ca.B - cb.B);
				soma += d;
				if (d > 0.08f) diferentes++;
				total++;
			}
		double pct = total == 0 ? 0 : 100.0 * diferentes / total;
		Conferir(pct > 3.0,
			$"NADAR E VOAR SAEM DIFERENTES NA TELA: {pct:0.0}% dos pixels do recorte mudaram "
			+ $"(media {soma / Math.Max(1, total):0.000})");
	}
}
