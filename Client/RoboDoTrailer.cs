using Godot;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// ============================ O DIRETOR DO TRAILER (`--trailer <cena>`) ============================
/// O dono pediu um VIDEO do jogo, "mostrando tudo". As bancadas de foto desta casa nao servem de
/// camera: elas pausam a arvore pra tirar o par antes/depois, escondem o tiro pra fazer a mascara,
/// injetam o defeito antigo de proposito, e nascem onde o berco calhou -- dentro de uma casa, debaixo
/// de tempestade, com a HUD e o chat por cima. Otimas pra PROVAR, ruins pra MOSTRAR.
///
/// Este robo so arruma o palco e deixa o jogo rodar: leva o corpo a um lugar aberto, crava a hora e o
/// tempo, veste o heroi, chama quem contracena e move a camera -- e o que acontece em cena acontece
/// pelos mesmos verbos, tecnicas e cerebros de producao. Quem grava e de fora (a captura do monitor).
///
/// ============================ COMO UMA CENA E ESCRITA ============================
/// Cada cena e um iterador que devolve QUANTOS SEGUNDOS ESPERAR ate o passo seguinte. Nao e a maquina
/// de `switch (_passo)` das bancadas porque la cada passo CONFERE alguma coisa e aqui cada passo so
/// espera o jogo: quarenta passos numerados de "espera 3 s" seriam quarenta `case` sem conteudo.
///
/// As MARCAS (`[trailer] @12.34 ...`) saem no console com o relogio do robo: e por elas que a montagem
/// acha o instante de cada coisa dentro da gravacao, em vez de procurar a olho em minutos de video.
/// ==============================================================================================
///
/// COMO RODAR (com `--host`, e a janela no segundo monitor):
///     Godot --path . --position 1920,0 --host --rede 79xx --semfoco --trailer formas \
///           --raca Saiyan --conta &lt;NOVA&gt; --nome Kakaroto
/// </summary>
public partial class RoboDoTrailer : Node
{
	/// <summary>A cena pedida (`--trailer <cena>`).</summary>
	public string Cena = "";

	/// <summary>O argumento livre da cena (`--trailerarg ...`): uma lista de formas, um arquivo de pontos.</summary>
	public string Arg = "";

	private const int T = ZoneCollision.TileSize;

	private static GameClient? C => GameClient.Instance;
	private static GameServer? S => GameServer.Instance;
	private static World? M => World.Instancia;

	/// <summary>A hora em que a cena esta cravada (negativa = o relogio do mundo anda solto). Ver `HoraDoTrailer`.</summary>
	private double _horaCravada = -1, _cravaEm;

	private IEnumerator<double>? _roteiro;
	private double _espera = 3.0, _relogio;

	/// <summary>
	/// A FOTO AUTOMATICA (`fotos=0.5` no argumento da cena): a cada tantos segundos a tela vai pra `user://`
	/// como `trailer-<cena>-NNN.jpg`, com o instante do robo no log. E o jeito de VER uma cena sem depender
	/// da gravacao do monitor -- que precisa do jogo em tela cheia na frente, e ja falhou calada (a
	/// duplicacao de desktop para de entregar quadro com a tela bloqueada ou um jogo em tela cheia do lado).
	/// Negativo = ainda nao lido do argumento; zero = desligada.
	/// </summary>
	private double _fotoACada = -1, _ateAFoto, _ateCalar;
	private int _fotosTiradas;
	private bool _fim;
	private int _eu;

	private static void Nota(string linha) => GD.Print("[trailer] " + linha);

	/// <summary>UMA MARCA DE MONTAGEM: o instante, no relogio do robo, em que isto aconteceu.</summary>
	private void Marca(string oque) => Nota($"@{_relogio:0.00} {oque}");

	public override void _Process(double delta)
	{
		if (_fim) return;

		// ESTE MUNDO E MEU? A mesma guarda do `RoboDeCena`, e pelo mesmo motivo: com a porta tomada o
		// `--host` nao vira servidor e o cliente entra no mundo de OUTRA sessao -- e este robo troca a
		// hora, o clima e o corpo de quem estiver la.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0 && GameServer.Instance is { Running: false })
		{
			_fim = true;
			GD.PrintErr("[trailer] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo e de outra sessao.");
			return;
		}

		if (C is not { Connected: true } cli || M is not { } mundo || S is null || Hud.Instancia is null) return;
		if (mundo.PosicaoLocalDeTeste is null || cli.LocalId == 0) return;
		_eu = cli.LocalId;

		_relogio += delta;
		TickDaCamera(delta);

		if (_fotoACada < 0) _fotoACada = OptN("fotos", 0);
		if (_fotoACada > 0 && _roteiro != null && (_ateAFoto -= delta) <= 0)
		{
			_ateAFoto = _fotoACada;
			FotoAutomatica();
		}
		// `mudo=1`: os NPCs da zona nao soltam a fala automatica de combate. E a tomada da versao em INGLES
		// do trailer -- ver `GameServer.CalarAsBocasNoTrailer`. Repetida porque os atores nascem ao longo da cena.
		if (_roteiro != null && OptN("mudo", 0) > 0 && (_ateCalar -= delta) <= 0)
		{
			_ateCalar = 0.25;
			S?.CalarAsBocasNoTrailer(_eu);
		}
		if (_ceuPorSegundo != 0) S?.AndarOCeuDoTrailer(delta * _ceuPorSegundo);
		else if (_horaCravada >= 0 && (_cravaEm -= delta) <= 0)
		{
			_cravaEm = 0.5;
			S?.HoraDoTrailer(_eu, _horaCravada);
		}

		_espera -= delta;
		if (_espera > 0) return;

		if (_roteiro == null)
		{
			_roteiro = Escolher()?.GetEnumerator();
			if (_roteiro == null) { Nota($"nao existe a cena `{Cena}`. As que existem: {string.Join(", ", Cenas.Keys)}"); Fim(); return; }
			Marca($"COMECA a cena `{Cena}`");
		}

		if (!_roteiro.MoveNext()) { Fim(); return; }
		_espera = _roteiro.Current;
	}

	private void Fim()
	{
		_fim = true;
		Marca("ACABOU");
		Nota("===== FIM =====");
	}

	// =====================================================================
	// O PALCO
	// =====================================================================
	private static Vec2 Centro(float cx, float cy) => new((cx + 0.5f) * T, (cy + 0.5f) * T);
	private static Vector2 CentroV(float cx, float cy) => new((cx + 0.5f) * T, (cy + 0.5f) * T);
	private static Vector2 V(Vec2 p) => new(p.X, p.Y);
	private static Vec2 P(Vector2 p) => new(p.X, p.Y);

	/// <summary>Onde o corpo local esta desenhado.</summary>
	private Vector2 Aqui => M?.PosicaoLocalDeTeste ?? Vector2.Zero;

	/// <summary>A HUD e o chat, na tela ou fora dela. O resto das camadas (clima, nevoa, clarao, QTE) fica.</summary>
	private static void Tela(bool hud, bool chat)
	{
		if (Hud.Instancia is { } h) h.Visible = hud;
		if (Chat.Instancia is { } c) c.Visible = chat;
	}

	private static void Zoom(int z) => M?.AplicarZoom(z);

	/// <summary>
	/// VIAJA A UMA ZONA PRE-FEITA e espera o mundo dela ficar de pe. A troca de zona sempre baixa a
	/// cobertura de carregamento (`World.cs`), entao a espera do fim nao e folga: e a cortina subindo.
	/// </summary>
	private IEnumerable<double> Viajar(string zona, float cx, float cy)
	{
		Vector2 destino = CentroV(cx, cy);
		if (S?.ZonaDoTrailer(_eu) == zona) { Por(cx, cy, Facing.South); yield return 1.2; yield break; }

		S?.MoveToZone(_eu, ZoneKey.Premade(zona), P(destino));
		double t = 0;
		while (t < 20 && (S?.ZonaDoTrailer(_eu) != zona || Aqui.DistanceTo(destino) > 96f)) { t += 0.2; yield return 0.2; }
		Marca($"chegou em {zona} ({cx:0},{cy:0}) depois de {t:0.0}s");
		yield return 3.0;
	}

	/// <summary>MUDA O CORPO DE LUGAR NA MESMA ZONA, sem cortina (ver `PorNoPontoNaFotoDoBorrao`).</summary>
	private void Por(float cx, float cy, Facing olhar) => S?.PorNoPontoNaFotoDoBorrao(_eu, Centro(cx, cy), olhar);

	// =====================================================================
	// A CAMERA
	// =====================================================================
	private Vector2 _panDe, _panAte;
	private double _panT, _panDur;
	private bool _panando;
	private int _seguindo;

	/// <summary>A camera cravada num ponto do mundo (nulo = volta pro corpo).</summary>
	private void Foco(Vector2? ponto)
	{
		_panando = false;
		_seguindo = 0;
		_dupla1 = _dupla2 = 0;
		_focoDaDupla = null;
		if (M is { } m) m.FocoDeTeste = ponto;
	}

	/// <summary>A camera ANDA de um ponto a outro em `seg` segundos, em velocidade constante.</summary>
	private void Pan(Vector2 de, Vector2 ate, double seg)
	{
		_panDe = de; _panAte = ate; _panT = 0; _panDur = Math.Max(seg, 0.01);
		_panando = true;
		_seguindo = 0;
	}

	/// <summary>A camera ACOMPANHA um corpo que nao e o local (0 = solta).</summary>
	private void Seguir(int id)
	{
		_panando = false;
		_seguindo = id;
		if (id == 0 && M is { } m) m.FocoDeTeste = null;
	}

	private void TickDaCamera(double delta)
	{
		if (M is not { } m) return;

		if (_panando)
		{
			_panT += delta;
			float k = (float)Math.Clamp(_panT / _panDur, 0, 1);
			m.FocoDeTeste = NaGrade(_panDe.Lerp(_panAte, k));
			if (k >= 1) _panando = false;
		}
		else if (_seguindo != 0 && m.PosicaoDesenhadaDe(_seguindo) is { } p)
		{
			m.FocoDeTeste = NaGrade(p);
		}
		else if (_desvioDoFoco is { } desvio)
		{
			// O FOCO ANDA COM O CORPO, deslocado: o palco alheio move o corpo de zona sem avisar (a
			// orbita do planeta que vai explodir), e um ponto cravado ficaria apontando pro mundo de antes.
			m.FocoDeTeste = NaGrade(Aqui + desvio);
		}
		else if (_dupla1 != 0 || _dupla2 != 0)
		{
			// O MEIO DOS DOIS, PERSEGUIDO E NAO CRAVADO: numa luta os corpos saltam (a investida, o
			// Zanzoken, o arremesso), e uma camera colada no ponto medio saltaria junto a cada golpe.
			// E o mesmo `Lerp` do espectador do torneio (`World.TickDoEspectador`), pelo mesmo motivo.
			Vector2? a = m.PosicaoDesenhadaDe(_dupla1), b = m.PosicaoDesenhadaDe(_dupla2);
			Vector2? meio = a is { } pa && b is { } pb ? (pa + pb) / 2f : a ?? b;
			if (meio is { } alvo)
			{
				_focoDaDupla = _focoDaDupla is { } f ? f.Lerp(alvo, Mathf.Clamp((float)delta * 4f, 0f, 1f)) : alvo;
				m.FocoDeTeste = NaGrade(_focoDaDupla.Value);
			}

			if (_lenteDaDupla > 0 && a is { } qa && b is { } qb) LenteDaDupla((qa - qb).Length() / T, delta);
		}
	}

	private Vector2? _focoDaDupla;

	/// <summary>A camera olha pra um ponto a esta distancia do corpo local, e o acompanha (nulo = desligado).</summary>
	private Vector2? _desvioDoFoco;

	/// <summary>A lente atual da dupla (0 = a cena nao pediu lente automatica) e ha quanto tempo ela nao muda.</summary>
	private int _lenteDaDupla;
	private double _lenteParada;

	/// <summary>
	/// A LENTE ACOMPANHA A DISTANCIA DOS DOIS: de perto 4x, a meia distancia 3x, de longe 2x. O zoom
	/// deste jogo e INTEIRO (arte de pixel cintila em zoom quebrado -- ver `World.EfeitosDaAltura`),
	/// entao a troca e um degrau; a folga entre subir e descer e o tempo minimo parado existem pra o
	/// degrau nao ficar batendo quando os dois dancam em cima de um limiar.
	/// </summary>
	private void LenteDaDupla(float tiles, double delta)
	{
		_lenteParada += delta;
		if (_lenteParada < 1.2) return;

		int quer = _lenteDaDupla;
		if (_lenteDaDupla >= 4 && tiles > 10.5f) quer = 3;
		else if (_lenteDaDupla == 3 && tiles > 17f) quer = 2;
		else if (_lenteDaDupla == 3 && tiles < 7.5f) quer = 4;
		else if (_lenteDaDupla <= 2 && tiles < 13f) quer = 3;
		if (quer == _lenteDaDupla) return;

		_lenteDaDupla = quer;
		_lenteParada = 0;
		Zoom(quer);
	}

	/// <summary>
	/// O FOCO ASSENTA NA GRADE DE DESENHO -- a mesma do `World.GradeDeDesenho`. Uma camera parada em
	/// meio pixel de tela faz a arte de pixel tremer no pan, que e o defeito que aquela grade conserta.
	/// </summary>
	private static Vector2 NaGrade(Vector2 p)
	{
		float g = World.GradeDeDesenho;
		return new Vector2(MathF.Round(p.X * g) / g, MathF.Round(p.Y * g) / g);
	}

	/// <summary>Uma foto da tela em `user://` -- so pro reconhecimento de lugar; o video e de fora.</summary>
	private void Foto(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto: rode com janela"); return; }
		img.SavePng(ProjectSettings.GlobalizePath($"user://{nome}.png"));
	}

	/// <summary>A foto da vez do modo `fotos=`: JPG (uma tela cheia em PNG passa de 2 MB, e sao dezenas por cena).</summary>
	private void FotoAutomatica()
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota("sem foto: rode com janela"); _fotoACada = 0; return; }
		string nome = $"trailer-{Cena}-{_fotosTiradas++:000}";
		img.SaveJpg(ProjectSettings.GlobalizePath($"user://{nome}.jpg"), 0.9f);
		Nota($"@{_relogio:0.00} foto {nome}");
	}
}
