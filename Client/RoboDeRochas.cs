using System.Collections;
using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DAS ROCHAS (`--diagrochas`) -- a metade do CLIENTE do pedido do dono de 2026-10-10: a rocha de
/// minerio que aparece, barra, acende o "[E]" e some quando e colhida; e a rocha grande dos mundos sorteados,
/// que barra na BASE e entra na ordem do desenho pela base (quem esta na frente aparece, quem passa por tras
/// some atras dela).
///
/// A metade do servidor -- onde e quantas nascem, a reserva, a reposicao, o fio -- e da `--rochasteste`. Aqui
/// se mede o que so a tela tem: o tile pintado, o mapa de colisao do cliente, o passo previsto que para na
/// pedra sem o servidor corrigir, o menu, e o pixel da foto.
///
/// TUDO PELA ENTRADA DE VERDADE: o corpo anda pelo piloto automatico (o mesmo de quem clica pra andar), a
/// tecla E e empurrada no viewport e o botao do menu e apertado pelo sinal. O que se afirma e lido de DOIS
/// lados -- o servidor do mesmo processo (modo host) e a tela.
///
/// O PALCO: primeiro um campo aberto da Terra (`--campoteste 8`), com uma rocha posta a tres celulas do
/// corpo pelo caminho de producao; depois um mundo sorteado, pousado pelo funil de producao, com o corpo
/// tres celulas abaixo de uma rocha grande.
///
/// O CONTRA-EXEMPLO DO DESENHO e uma segunda rodada, com `--rochagrandeantiga`: a rocha grande volta pra
/// camada comum da cobertura (`PlanetaProcedural.RochaGrandeNaCamadaComumDeTeste`), dois tiles acima da
/// celula que a carrega, e as duas checagens do desenho NA BASE reprovam -- "a pedra encosta em quem para na
/// base dela" e "o desenho da pedra cobre a base em que o corpo para". O corpo para onde sempre parou; e a
/// pedra que aparece longe dele.
///
/// Rode com janela (no segundo monitor) -- as fotos e os pixels precisam de desenho:
///   --host --rede 8132 --campoteste 8 --horateste 0.5 --diagrochas --raca Human --conta bancada_rochas_ag --nome Mineiro
/// </summary>
public partial class RoboDeRochas : Node
{
	private const int T = ZoneCollision.TileSize;

	/// <summary>
	/// QUANTAS CELULAS DEPOIS DA ROCHA O PILOTO MIRA quando a checagem e "o corpo atravessou a celula dela".
	/// O piloto automatico se desliga a 64 px do alvo (`LocalPlayer`: "voce chegou"), ou seja, duas celulas
	/// antes dele: com quatro, o corpo para a mais de uma celula inteira do outro lado.
	/// </summary>
	private const int AlemDaRocha = 4;

	/// <summary>
	/// OS DOIS LIMIARES DO PIXEL (soma das diferencas dos tres canais, cada um de 0 a 1). "Mudou" e pedra
	/// contra chao ou pedra contra corpo -- cores diferentes, a distancia passa de 0,3. "Igual" e o MESMO quadro
	/// com uma camada a menos: so a compressao de nada, bem abaixo de 0,06. Entre os dois fica a folga.
	/// </summary>
	private const float LimiarDeMudanca = 0.12f, LimiarDeIgual = 0.06f;

	/// <summary>Cinco pontos do desenho do corpo, a partir do centro dele: tronco, cabelo, calcao e os dois bracos.</summary>
	private static readonly Vector2[] PontosDoCorpo = [new(0, 0), new(0, -10), new(0, 8), new(-5, -2), new(5, -2)];

	private int _ok, _falhou, _correcoes;
	private readonly List<string> _vermelhas = [];
	private IEnumerator? _roteiro;
	private double _espera = 2.5;
	private bool _acabou;

	private static GameClient? C => GameClient.Instance;
	private static World? M => World.Instancia;
	private static GameServer? S => GameServer.Instance;

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[drochas]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; _vermelhas.Add(nome); GD.PrintErr($"[drochas]   FALHA {nome}   [{detalhe}]"); }
	}

	private static void Nota(string s) => GD.Print("[drochas] " + s);

	public override void _Ready()
	{
		// ANTES de qualquer planeta ser montado: a camada da rocha grande e escolhida no nascimento da cena.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--rochagrandeantiga") >= 0)
		{
			PlanetaProcedural.RochaGrandeNaCamadaComumDeTeste = true;
			Nota("DEFEITO INJETADO (--rochagrandeantiga): a rocha grande desenha na camada comum, dois tiles acima.");
		}
		if (C is { } cli) cli.Corrected += AoSerCorrigido;
	}

	public override void _ExitTree()
	{
		if (C is { } cli) cli.Corrected -= AoSerCorrigido;
	}

	private void AoSerCorrigido(Vec2 _) => _correcoes++;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } || M?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		// O PLACAR SAI DE QUALQUER JEITO: um passo do roteiro que estoura vira uma vermelha e encerra a rodada
		// -- sem isto o lancador ficaria esperando ate o prazo por uma linha de placar que nao viria.
		_roteiro ??= Roteiro().GetEnumerator();
		bool segue;
		try { segue = _roteiro.MoveNext(); }
		catch (Exception e) { Afirmar("(o roteiro chegou ao fim sem estourar)", false, e.GetType().Name + ": " + e.Message); segue = false; }
		if (!segue) { Fim(); return; }
		_espera = _roteiro.Current is double d ? d : 0;
	}

	private void Fim()
	{
		_acabou = true;
		RochaDeMinerio.SemColisaoDeTeste = false;
		PlanetaProcedural.RochaGrandeNaCamadaComumDeTeste = false;
		GD.Print($"\n[drochas] ===== {_ok} OK, {_falhou} FALHA(S) =====");
		if (_falhou > 0) GD.PrintErr("[drochas] vermelhas: " + string.Join(" | ", _vermelhas));
		Nota("fim.");
	}

	private void Tecla(Key k) =>
		GetViewport().PushInput(new InputEventKey { PhysicalKeycode = k, Keycode = k, Pressed = true });

	/// <summary>O quadro que esta na tela agora, sem gravar. Nulo = sem desenho (headless).</summary>
	private Image? Capturar()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		return img == null || img.IsEmpty() ? null : img;
	}

	private Image? Fotografar(string nome)
	{
		if (Capturar() is not { } img) { Nota("sem foto (headless nao renderiza): rode com janela"); return null; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		Nota($"foto {caminho} ({img.GetWidth()}x{img.GetHeight()})");
		return img;
	}

	/// <summary>A cor da foto num ponto do MUNDO (media de um quadrado de 3x3 pixels). Nulo = fora da foto.</summary>
	private Color? CorEm(Image foto, Vector2 noMundo)
	{
		Viewport v = GetViewport();
		Vector2 p = v.GetFinalTransform() * (v.CanvasTransform * noMundo);
		int x = (int)p.X, y = (int)p.Y;
		if (x < 1 || y < 1 || x >= foto.GetWidth() - 1 || y >= foto.GetHeight() - 1) return null;
		float r = 0, g = 0, b = 0;
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
			{
				Color c = foto.GetPixel(x + dx, y + dy);
				r += c.R; g += c.G; b += c.B;
			}
		return new Color(r / 9f, g / 9f, b / 9f);
	}

	private static float Distancia(Color a, Color b) =>
		MathF.Abs(a.R - b.R) + MathF.Abs(a.G - b.G) + MathF.Abs(a.B - b.B);

	private static Vector2 V(Vec2 p) => new(p.X, p.Y);

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private IEnumerable Roteiro()
	{
		GameClient cli = C!;
		World mundo = M!;
		if (S is not { } srv) { Afirmar("(montagem) o robo roda no modo host, com o servidor no mesmo processo", false); yield break; }
		if (RochasNaTela.Instancia is not { } tela) { Afirmar("(montagem) a camada das rochas existe no mundo", false); yield break; }
		MenuDeInteracao? menu = MenuDeInteracao.Instancia;

		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);
		yield return 1.5;

		// ------------------------------------------------------------ 1) o retrato da Terra
		Nota("-- 1) as rochas de minerio da Terra chegam, aparecem e barram --");
		ZoneKey terra = cli.Zone;
		int noServidor = srv.RochasDeTeste(terra).Count;
		Afirmar("o retrato chegou: a lista do cliente e da zona em que ele esta, com as rochas que o servidor tem",
				cli.RochasDaZona == terra.Hash && noServidor > 0 && cli.Rochas.Count == noServidor, $"cliente {cli.Rochas.Count}, servidor {noServidor}");
		int semBarrar = cli.Rochas.Count(c => mundo.Colisao?.TemRocha(c.X, c.Y) != true || mundo.Colisao?.BlockedCell(c.X, c.Y) != true);
		Afirmar("cada uma e UM tile da camada, e o mapa de colisao do cliente barra a celula de todas (as duas pontas concordam)",
				tela.PintadasDeTeste == cli.Rochas.Count && semBarrar == 0, $"{tela.PintadasDeTeste} pintadas, {semBarrar} sem barrar");

		// ------------------------------------------------------------ 2) uma rocha na frente do corpo
		Nota("-- 2) andar contra ela: o passo PARA, o [E] acende, a coleta leva --");
		Vector2 eu = mundo.PosicaoLocal!.Value;
		(int ex, int ey) = CelulaInterna.CelulaDoCorpo(new Vec2(eu.X, eu.Y));
		(int X, int Y) rocha = (ex + 3, ey);
		srv.PorRochaDeTeste(terra, rocha.X, rocha.Y);
		yield return 0.5;
		var naCelula = new Vector2I(rocha.X, rocha.Y);
		Afirmar("a rocha que NASCE chega sozinha (um pacote `Nasceu`): o tile aparece e a celula passa a barrar no cliente",
				cli.Rochas.Contains(rocha) && tela.GetCellSourceId(naCelula) >= 0 && mundo.Colisao?.BlockedCell(rocha.X, rocha.Y) == true);

		_correcoes = 0;
		mundo.IrAteDeTeste(RochaDeMinerio.Ponto(rocha.X + 3, rocha.Y));
		yield return 2.5;
		mundo.PararDeTeste();
		yield return 0.3;
		Vector2 parou = mundo.PosicaoLocal!.Value;
		Afirmar("**O CORPO ANDA ATE ELA E PARA**: encosta na celula e nao entra -- e o servidor nao corrige nada (as duas pontas barram no mesmo lugar)",
				parou.X + MoveRules.BodyHalfW <= rocha.X * T + 0.5f && parou.X > eu.X + T && _correcoes == 0,
				$"saiu de x={eu.X:0}, parou em x={parou.X:0}; a rocha comeca em x={rocha.X * T}; {_correcoes} correcoes");
		Afirmar("encostado nela, a dica do pe da tela ensina a tecla", menu?.DicaNaTela == $"[E] {RochaDeMinerio.Nome}", menu?.DicaNaTela ?? "");
		yield return 0.2;
		Fotografar("rochas-01-minerio-encostado");

		Tecla(Key.E);
		yield return 0.4;
		Afirmar("a tecla E abre o menu DELA, com o botao de coletar",
				menu is { NaTela: true } && menu.TituloDesenhado == RochaDeMinerio.Nome && menu.BotoesDesenhados().Contains("Quebrar e coletar"),
				menu != null ? $"{menu.TituloDesenhado}: {string.Join(" | ", menu.BotoesDesenhados())}" : "");
		Fotografar("rochas-02-o-menu-da-rocha");

		double saldoAntes = srv.RecursosDeTeste(cli.LocalId);
		menu?.ApertarDesenhado("Quebrar e coletar");
		yield return 0.7;
		double veio = srv.RecursosDeTeste(cli.LocalId) - saldoAntes;
		Afirmar("**COLETADA**: o saldo de recursos sobe (dentro da faixa do rendimento) e a rocha some das duas pontas -- sem tile e sem barrar",
				veio >= RochaDeMinerio.RendimentoMinimo && veio <= RochaDeMinerio.RendimentoMaximo
				&& !srv.RochasDeTeste(terra).Contains(rocha) && !cli.Rochas.Contains(rocha)
				&& tela.GetCellSourceId(naCelula) < 0 && mundo.Colisao?.BlockedCell(rocha.X, rocha.Y) == false,
				$"+{veio:0} recursos");
		Afirmar("...e a dica apaga: nao ha mais o que colher ali", menu?.DicaNaTela == "", menu?.DicaNaTela ?? "");

		// O ALVO FICA `AlemDaRocha` CELULAS DEPOIS DELA, e nao duas: o piloto da a viagem por feita a 64 px do
		// alvo, e com o alvo perto o corpo parava no MEIO da celula que se queria ver atravessada -- a checagem
		// reprovava um caminho que estava aberto (foi a primeira rodada desta bancada).
		_correcoes = 0;
		mundo.IrAteDeTeste(RochaDeMinerio.Ponto(rocha.X + AlemDaRocha, rocha.Y));
		yield return 2.5;
		mundo.PararDeTeste();
		Afirmar("sem a rocha o caminho abre: o corpo atravessa a celula em que ela estava e sai do outro lado",
				mundo.PosicaoLocal!.Value.X - MoveRules.BodyHalfW >= (rocha.X + 1) * T && _correcoes == 0,
				$"a celula dela ia de x={rocha.X * T} a x={(rocha.X + 1) * T}; o corpo esta em x={mundo.PosicaoLocal!.Value.X:0}; {_correcoes} correcoes");

		// o contra-exemplo: a rocha que e so desenho
		eu = mundo.PosicaoLocal!.Value;
		(ex, ey) = CelulaInterna.CelulaDoCorpo(new Vec2(eu.X, eu.Y));
		(int X, int Y) falsa = (ex - 2, ey);
		RochaDeMinerio.SemColisaoDeTeste = true;
		srv.PorRochaDeTeste(terra, falsa.X, falsa.Y);
		yield return 0.5;
		mundo.IrAteDeTeste(RochaDeMinerio.Ponto(falsa.X - AlemDaRocha, falsa.Y));
		yield return 2.5;
		mundo.PararDeTeste();
		Afirmar("(defeito injetado: a rocha e so desenho) ela aparece na tela e o corpo ATRAVESSA a celula dela -- a regua sabe reprovar",
				tela.GetCellSourceId(new Vector2I(falsa.X, falsa.Y)) >= 0 && mundo.PosicaoLocal!.Value.X + MoveRules.BodyHalfW <= falsa.X * T,
				$"a rocha falsa vai de x={falsa.X * T} a x={(falsa.X + 1) * T}; o corpo esta em x={mundo.PosicaoLocal!.Value.X:0}");
		RochaDeMinerio.SemColisaoDeTeste = false;
		srv.TirarRochaDeTeste(terra, falsa.X, falsa.Y);
		yield return 0.4;

		// ------------------------------------------------------------ 3) a rocha grande
		Nota("-- 3) a rocha grande de um mundo sorteado: barra na base, e a ordem do desenho e a da base --");
		if (srv.PousarJuntoDeUmaRochaGrandeDeTeste(cli.LocalId) is not { } ancora)
		{
			Afirmar("(montagem) ha um mundo sorteado com uma rocha grande e chao livre na frente e atras dela", false);
			yield break;
		}
		for (int i = 0; i < 40 && (mundo.ZonaMontadaDeTeste != cli.Zone.Hash || mundo.Colisao == null || cli.Zone.Hash == terra.Hash); i++)
			yield return 0.25;
		// O SOL A PINO E O CEU LIMPO, PELO CAMINHO QUE AVISA A TELA (`CravarMeioDiaDaVariedade`: a conta do verb
		// de admin, que so anda pra FRENTE, o clima limpo e o `MandarCeu` na hora). A segunda rodada acertava o
		// relogio pela correcao minima (`SolAPinoNaFotoDeFusao`), que pode andar pra TRAS -- e a sincronia do
		// ceu ("passou do intervalo desde a ultima?") nao dispara com o relogio recuando: o servidor dizia
		// meio-dia, a tela continuava no poente, e a checagem daqui lia o lado errado.
		srv.CravarMeioDiaDaVariedade(cli.LocalId);
		yield return 2.0;
		Nota($"pousou em {cli.Zone.Name}; a rocha grande esta ancorada em ({ancora.X},{ancora.Y})");

		// FOTO ESCURA COM LOG VERDE NAO SERVE: no escuro a pedra, o chao e o corpo sao quase a mesma cor, e os
		// limiares do pixel ficam com um terco da folga. Quem responde e o ceu DA TELA (o que a foto vai
		// mostrar), e nao o do servidor. O palco ja escolhe um mundo que tem dia; esta linha e quem reprova
		// se um dia ele deixar de escolher.
		EstadoDoCeu? ceu = mundo.Ceu;
		Afirmar("(montagem) na TELA o mundo das fotos esta de dia, com o sol a pino",
				ceu is { Noite: false } dia && Math.Abs(dia.Hora - Ceu.MeioDia) < 0.05,
				ceu is { } visto ? $"hora na tela {visto.Hora:0.00} ({Ceu.NomeDaHora(visto.Hora)})" : "a tela esta sem ceu");

		// O PALCO E SO A PEDRA: a rocha de minerio que nasceu por perto sai (ela barraria a reta do corpo), e o
		// corpo e posto de novo no pe da pedra -- a primeira vez cruzou com a troca de zona, e o cliente pode
		// ter ficado no ponto de chegada do planeta.
		foreach ((int X, int Y) perto in cli.Rochas.Where(r => Math.Abs(r.X - ancora.X) <= 6 && Math.Abs(r.Y - ancora.Y) <= 6).ToList())
			srv.TirarRochaDeTeste(cli.Zone, perto.X, perto.Y);
		srv.CravarDeTeste(cli.LocalId, RochaDeMinerio.Ponto(ancora.X, ancora.Y + 3));
		yield return 1.2;

		ZoneCollision? mapa = mundo.Colisao;
		int abertas = RochaGrande.Pegada.Count(d => mapa?.BlockedCell(ancora.X + d.Dx, ancora.Y + d.Dy) != true || mapa?.Indestrutivel(ancora.X + d.Dx, ancora.Y + d.Dy) != true);
		Afirmar("**NO MAPA DO CLIENTE A PEGADA DA ROCHA GRANDE BARRA E NAO SE QUEBRA** (ele gera o mesmo chao que o servidor, da mesma seed)",
				mapa != null && abertas == 0, $"{abertas} de {RochaGrande.Pegada.Length} celulas abertas");
		Afirmar("...e o topo dela fica livre: as duas fileiras do alto nao barram", mapa?.BlockedCell(ancora.X, ancora.Y - 2) == false && mapa?.BlockedCell(ancora.X, ancora.Y - 3) == false);

		// de frente: sobe contra a base
		_correcoes = 0;
		Vector2 saida = mundo.PosicaoLocal!.Value;
		mundo.IrAteDeTeste(RochaDeMinerio.Ponto(ancora.X, ancora.Y - 3));
		yield return 3.0;
		mundo.PararDeTeste();
		yield return 0.4;
		Vector2 naBase = mundo.PosicaoLocal!.Value;
		float pes = naBase.Y + MoveRules.FeetOffsetY;
		Afirmar("**ANDANDO CONTRA A ROCHA GRANDE O CORPO PARA NA BASE DELA**, e o servidor nao corrige nada",
				pes - MoveRules.BodyHalfH >= (ancora.Y + 1) * T - 0.5f && naBase.Y < saida.Y - T && _correcoes == 0,
				$"pes em y={pes:0}; a base da pedra em y={(ancora.Y + 1) * T}; {_correcoes} correcoes");

		// =============================== A ORDEM DO DESENHO, PELO PIXEL ===============================
		// DOIS PONTOS: a CABECA do corpo parado na base (ela cai em cima do desenho da pedra) e o corpo posto na
		// fileira de cima da pegada (cai no meio do desenho).
		//
		// CADA LUGAR DO CORPO RENDE DOIS QUADROS COM A CAMERA NO MESMO LUGAR: um com a cobertura do planeta
		// desenhada (as camadas que ordenam por Y, a da rocha grande entre elas) e um com ela escondida. O que
		// muda de um pro outro e so o que a pedra pinta -- e a pergunta vira "a pedra pinta POR CIMA do corpo
		// aqui?":
		//   na frente, NAO: o pixel da cabeca e o mesmo com a pedra ou sem ela;
		//   por tras, SIM: tirar a pedra revela o corpo.
		//
		// A COR E LIDA NA HORA DA FOTO, e nao no fim: `CorEm` converte o ponto do mundo com a camera de AGORA, e
		// a camera segue o corpo -- lida depois de ele mudar de lugar, a conta cairia noutro pixel.
		//
		// A PRIMEIRA VERSAO comparava fotos tiradas de lugares diferentes, e a terceira (o corpo tres celulas
		// abaixo da pedra) ja nao tinha o topo dela na tela -- a camera mostra 120 px pra cima do corpo. A
		// leitura voltava nula e a bancada dizia "sem pixels" sem medir nada.
		// ==============================================================================================
		List<TileMapLayer> cobertura = [.. mundo.CamadasDoCenarioDeTeste.Where(l => l.YSortEnabled && l.Visible)];
		Vector2 cabeca = naBase + new Vector2(0, -10);

		Color? cabecaCom = Fotografar("rochas-03-rocha-grande-de-frente") is { } f3 ? CorEm(f3, cabeca) : null;
		foreach (TileMapLayer l in cobertura) l.Visible = false;
		yield return 0.3;
		Color? cabecaSem = Capturar() is { } f3sem ? CorEm(f3sem, cabeca) : null;
		foreach (TileMapLayer l in cobertura) l.Visible = true;

		// por tras: o corpo e posto na fileira de cima da pegada (anda-se ali; a pedra e mais alta que ele)
		Vec2 atras = RochaDeMinerio.Ponto(ancora.X, ancora.Y - 2);
		srv.CravarDeTeste(cli.LocalId, atras);
		yield return 1.2;
		Vector2 corpo = mundo.PosicaoLocal!.Value;
		Afirmar("(montagem) o corpo esta na fileira de cima da pegada, atras do topo da pedra", corpo.DistanceTo(V(atras)) < 4f, $"corpo em {corpo}, a celula em {V(atras)}");

		Image? f4 = Fotografar("rochas-04-rocha-grande-por-tras");
		Color? pedraNaCabeca = f4 != null ? CorEm(f4, cabeca) : null;
		Color?[] corpoCom = f4 != null ? [.. PontosDoCorpo.Select(d => CorEm(f4, corpo + d))] : [];
		foreach (TileMapLayer l in cobertura) l.Visible = false;
		yield return 0.3;
		Image? f4sem = Capturar();
		Color? chaoNaCabeca = f4sem != null ? CorEm(f4sem, cabeca) : null;
		Color?[] corpoSem = f4sem != null ? [.. PontosDoCorpo.Select(d => CorEm(f4sem, corpo + d))] : [];
		foreach (TileMapLayer l in cobertura) l.Visible = true;

		if (cabecaCom is { } cc && cabecaSem is { } cs && pedraNaCabeca is { } pc && chaoNaCabeca is { } xc
			&& corpoCom.Length == PontosDoCorpo.Length && corpoSem.Length == PontosDoCorpo.Length)
		{
			// O corpo saiu dali: se a pedra pinta naquele ponto, tirar a cobertura muda o pixel. Sem esta, a
			// checagem da frente passaria de graca num ponto em que nao ha pedra nenhuma.
			Afirmar("**A PEDRA ENCOSTA EM QUEM PARA NA BASE DELA**: o desenho dela passa pelo ponto da cabeca do corpo (tirar a cobertura muda o pixel)",
					Distancia(pc, xc) > LimiarDeMudanca, $"com a pedra {pc}, sem ela {xc} (distancia {Distancia(pc, xc):0.00})");
			Afirmar("**NA FRENTE DA PEDRA O CORPO APARECE**: o pixel da cabeca dele e o mesmo com a pedra desenhada ou escondida (ela nao pinta por cima dele)",
					Distancia(cc, cs) < LimiarDeIgual, $"com a pedra {cc}, sem ela {cs} (distancia {Distancia(cc, cs):0.00})");

			// CINCO PONTOS do corpo, e vale o que mais muda: o cabelo e o calcao sao escuros e a pele e clara --
			// um ponto so podia cair numa pele da cor da pedra e medir "nao mudou" com o corpo escondido.
			float revelado = 0;
			for (int i = 0; i < PontosDoCorpo.Length; i++)
				if (corpoCom[i] is { } com && corpoSem[i] is { } sem)
					revelado = MathF.Max(revelado, Distancia(com, sem));
			Afirmar("**POR TRAS DO TOPO ELE SOME ATRAS DELA**: onde o corpo esta o que se ve e a pedra, e esconder a pedra o revela",
					revelado > LimiarDeMudanca, $"o ponto do corpo que mais muda quando a pedra some: distancia {revelado:0.00}");
		}
		else Nota("sem pixels pra comparar (headless): rode com janela");

		// e longe: a mesma pedra sem ninguem perto
		srv.CravarDeTeste(cli.LocalId, RochaDeMinerio.Ponto(ancora.X, ancora.Y + 3));
		yield return 1.2;
		Image? f5 = Fotografar("rochas-05-rocha-grande-sozinha");

		// O DESENHO ESTA EM CIMA DA PEGADA? Um ponto no miolo da celula da ancora, com a cobertura desenhada e
		// escondida: se a pedra cobre a base, o ponto muda. (E a checagem que o `--rochagrandeantiga` reprova.)
		var naAncora = new Vector2((ancora.X + 0.5f) * T, (ancora.Y + 0.4f) * T);
		Color? comPedra = f5 != null ? CorEm(f5, naAncora) : null;
		foreach (TileMapLayer l in cobertura) l.Visible = false;
		yield return 0.3;
		Image? f6 = Fotografar("rochas-06-o-chao-sem-a-cobertura");
		Color? semPedra = f6 != null ? CorEm(f6, naAncora) : null;
		foreach (TileMapLayer l in cobertura) l.Visible = true;
		yield return 0.2;
		if (comPedra is { } cp && semPedra is { } sp)
			Afirmar("**O DESENHO DA PEDRA COBRE A BASE EM QUE O CORPO PARA**: na celula da ancora o que se ve e a pedra (o pixel muda quando a cobertura e escondida)",
					Distancia(cp, sp) > LimiarDeMudanca, $"com a pedra {cp}, so o chao {sp} (distancia {Distancia(cp, sp):0.00})");
		else Nota("sem o pixel da ancora (headless): rode com janela");
	}
}
