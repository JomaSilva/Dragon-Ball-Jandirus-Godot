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
/// Ha TRES roteiros aqui, e quem escolhe e onde o servidor poe o corpo -- `--aguateste` (margem),
/// `--aguadentro` (dentro) e `--aguanoar` (no ar). Eles nao sao variacoes de conforto:
///
///   * o de DENTRO mede o MEIO da travessia (pose, altura zero, sombra que nao nasce). Ele nunca
///     teria pego os dois defeitos de ENTRADA, porque nasce com o problema ja resolvido -- e foi
///     exatamente isso que aconteceu: a bancada ficou verde com os dois caminhos do jogador
///     quebrados;
///   * o da MARGEM mede o gesto de verdade: apertar N na beira e ENTRAR andando;
///   * o do AR mede o POUSO em cima da agua, o unico que passa pelo `DescerAte`.
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

		(int dx, int dy)[] rumos = [(1, 0), (-1, 0), (0, 1), (0, -1)];
		foreach ((int dx, int dy) in rumos)
			for (int d = 1; d <= 4; d++)
			{
				if (!mapa.EhAgua(cx + dx * d, cy + dy * d)) continue;
				int n = 0;
				while (n < 60 && mapa.EhAgua(cx + dx * (d + n), cy + dy * (d + n))) n++;
				rumo = new Vector2(dx, dy);
				largura = n;
				return true;
			}
		return false;
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
				if (SobreAgua()) { _passo = 30; break; }

				Conferir(!SobreAgua(), "a bancada comeca em chao SECO");

				bool achou = AcharTravessia(out _rumo, out _larguraDoLago);
				Conferir(achou, achou
					? $"lago na frente: rumo {_rumo}, {_larguraDoLago} tiles de largura"
					: "nao ha agua nenhuma ao redor -- rode com --aguateste");
				if (!achou) { _passo = 100; break; }

				_saida = mundo.PosicaoLocal ?? Vector2.Zero;
				_deOndeAndou = _saida;
				mundo.AndarDeTeste(_rumo);
				break;
			}

			case 1:
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
				if (!SobreAgua() && mundo.AlturaDeTeste == 0f && _pxNadandoNaAgua > 8f) _chegouNaOutraMargem = true;
				break;

			case 19:
			{
				mundo.PararDeTeste();
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

				Fotografar("user://agua-7-entrou-da-beira.png", "DA BEIRA -- ENTROU NADANDO", out _);
				_passo = 200;   // o roteiro do seco acaba aqui
				break;
			}

			// =============================================================
			// ROTEIRO MOLHADO (`--aguadentro`): itens 2 e 3
			// =============================================================
			// ============================ POR QUE ESTE ROTEIRO EXISTE ============================
			// Os dois caminhos que um jogador tem pra COMECAR a nadar estao quebrados hoje (passos 12
			// e 19), e sem comecar a nadar nao ha o que fotografar -- o item 2 e o item 3 do pedido
			// ficariam sem resposta.
			//
			// Sobra o terceiro estado, e ele NAO e invencao da bancada: o proprio servidor diz que
			// ele existe e o trata de proposito ("deslogar dentro do lago, ser jogado la por um
			// arremesso, nascer perto demais da beira" -- `GameServer.Nado.PodeComecarANadar`). O
			// `--aguadentro` poe o corpo exatamente nesse estado, e o resto e caminho de producao: o
			// mesmo verb, o mesmo tique, o mesmo desenho.
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
				mundo.AndarDeTeste(_rumo);
				break;

			case 34:
			case 35:
			case 36:
				if (!SobreAgua() && mundo.AlturaDeTeste == 0f && _pxNadandoNaAgua > 8f) _chegouNaOutraMargem = true;
				break;

			case 37:
			{
				mundo.PararDeTeste();
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
				mundo.AndarDeTeste(_rumo);
				break;
			}

			case 64:
			case 65:
			case 66:
			case 67:
				if (!SobreAgua() && mundo.AlturaDeTeste == 0f && _pxNadandoNaAgua > 8f) _chegouNaOutraMargem = true;
				break;

			case 68:
			{
				mundo.PararDeTeste();
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
