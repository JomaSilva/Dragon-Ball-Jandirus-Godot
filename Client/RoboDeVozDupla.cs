using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A BANCADA DA VOZ COM DOIS CORPOS -- `--vozdupla &lt;a|b&gt;` ============================
/// O lado do CLIENTE da bancada que JULGA. O servidor dirige a cena e conta o que mandou
/// (`Server/GameServer.VozDuplaTeste.cs`); aqui:
///
///   * o `a` **fala** -- aperta a tecla de verdade (evento de teclado injetado no motor, e nao
///     `Input.ActionPress`), poe uma onda conhecida no lugar da captura e obedece as fases;
///   * o `b` **ouve, conta BYTES e da o veredito** das dez familias.
///
/// ============================ POR QUE O VEREDITO CONTA BYTES ============================
/// A afirmacao central e *"quem esta fora do alcance nao recebe byte nenhum"*. Uma bancada que medisse
/// VOLUME ficaria verde com a sala inteira recebendo tudo -- o volume de quem esta longe e baixo dos
/// dois jeitos, e o defeito e justamente que os dois soam igual. Entao a linha central desta bancada e
/// um contador de bytes na entrada do cliente, e ele mora ANTES do decodificador.
///
/// E o contador nao basta sozinho: um contador que sempre marque zero tambem ficaria verde. Por isso
/// cada familia tem o contra-exemplo dela na cena (dentro do alcance CHEGA) e um MUTANTE (o mesmo
/// cenario com o defeito posto no lugar da regra), que tem que deixar a linha VERMELHA.
/// ================================================================================
///
/// ============================ ONDE CADA NUMERO E LIDO ============================
///   | pergunta                          | onde                                  |
///   |-----------------------------------|---------------------------------------|
///   | chegou byte? quantos?             | `GameClient.VozRecebida` (o pacote cru)|
///   | como soa (abafada)                | `VozOuvida.Espiao` (PCM POS-filtro)   |
///   | o volume que o MOTOR aplicou      | `AudioEffectCapture` no bus `Voz`     |
///   | ha parede, segundo a MINHA VISTA  | `World.VeuDeTeste.Mapa` -- o `.vis` com que eu desenho a sombra |
/// ============================================================================
/// </summary>
public partial class RoboDeVozDupla : Node
{
	/// <summary>`a` = quem fala; `b` = quem ouve, conta e julga (e e o anfitriao/admin).</summary>
	public string Papel = "";

	private bool Falante => Papel == "a";

	// =====================================================================
	// PLACAR
	// =====================================================================
	private int _ok, _falha;
	private readonly List<string> _reprovadas = [];

	private void Nota(string linha) => GD.Print($"[vozdupla:{Papel}] {linha}");

	private void Checa(string oque, bool passou, string detalhe = "")
	{
		Nota((passou ? "  OK    " : "  FALHA ") + oque + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (passou) _ok++;
		else { _falha++; _reprovadas.Add(oque); }
	}

	/// <summary>
	/// O PLACAR DA INJECAO -- separado do outro de proposito (a mesma disciplina da `--diagtecla`).
	///
	/// Uma checagem verde na rodada limpa e uma checagem que **nao viu defeito**; uma que tambem fica
	/// vermelha com o defeito na frente dela e uma checagem que **sabe olhar**. Somar as duas no mesmo
	/// numero esconderia exatamente a diferenca entre as duas coisas.
	/// </summary>
	private int _injOk, _injFalha;
	private readonly List<string> _injPassouBatido = [];

	private void Injeta(string oque, bool ficouVermelha, string detalhe = "")
	{
		Nota((ficouVermelha ? "  pegou " : "  PASSOU") + "  (injecao) " + oque
			 + (detalhe.Length > 0 ? $"   [{detalhe}]" : ""));
		if (ficouVermelha) _injOk++;
		else { _injFalha++; _injPassouBatido.Add(oque); }
	}

	/// <summary>O que a bancada NAO conseguiu medir. Sai dito, e nao sai como zero.</summary>
	private readonly List<string> _naoMedido = [];

	// =====================================================================
	// O BALDE DE CADA FASE
	// =====================================================================
	private sealed class Balde
	{
		public string Nome = "";
		public int Numero;
		public float DistPedida;
		public bool ParedePedida, Mutante;
		public ulong ComecouMs, FechouMs;

		// --- o pacote, cru: a linha central ---
		/// <summary>
		/// O QUE CHEGOU NOS PRIMEIROS 300 ms, contado A PARTE.
		///
		/// A fase muda porque o servidor teleportou os corpos (ou porque o falante acabou de soltar a
		/// tecla). O que ja estava voando chega depois, carimbado com a fase ANTERIOR. Somar estragaria
		/// as duas medidas que mais importam (o zero de F1 e o zero de F6); **apagar seria pior** --
		/// um retardatario que virasse dez seria defeito de verdade e ninguem veria. Sai em coluna
		/// propria, e o placar diz o que ele e.
		/// </summary>
		public int PacotesNaVirada;
		public long BytesNaVirada;

		public int Pacotes;
		public long BytesDeAudio;
		public readonly HashSet<int> Falantes = [];
		public long SomaDist;
		public int DistMin = 999, DistMax = -1;
		public int MarcadosComParede, MarcadosSemParede;

		// --- o PCM que vai pro alto-falante (depois da abafada) ---
		public int Quadros;
		public double SomaRms, SomaEGrave, SomaEAgudo;

		// --- o que o motor mixou (ja com a atenuacao 2D) ---
		public double PicoDoBarramento, SomaQuadradoDoBarramento;
		public long AmostrasDoBarramento;

		// --- F5: a parede do pacote contra a parede da MINHA vista ---
		public int VistaConcordou, VistaDivergiu, VistaSemCorpo;
		public int VistaDisseParede, VistaDisseLivre;
		/// <summary>Ver <see cref="RoboDeVozDupla.MsDeTransito"/>: o corpo ainda estava chegando.</summary>
		public int VistaEmTransito;

		// --- o que EU mandei (so no papel `a`) ---
		public int QuadrosMandados;
		public long BytesMandados;

		public double Segundos => (FechouMs - ComecouMs) / 1000.0;
		/// <summary>Os segundos que contaram -- os da virada nao contam pra taxa.</summary>
		public double SegundosMedidos => Math.Max(Segundos - MsDeVirada / 1000.0, 0.001);
		public double PacotesPorSegundo => Pacotes / SegundosMedidos;
		public double RazaoAgudoGrave => SomaEGrave > 0 ? SomaEAgudo / SomaEGrave : 0;
		public double Rms => Quadros > 0 ? SomaRms / Quadros : 0;
		public double DistMedia => Pacotes > 0 ? SomaDist / (double)Pacotes : -1;
		public double BusRms => AmostrasDoBarramento > 0
			? Math.Sqrt(SomaQuadradoDoBarramento / AmostrasDoBarramento) : -1;
	}

	private const ulong MsDeVirada = 300;

	private readonly List<Balde> _baldes = [];
	private Balde? _agora;
	private bool NaVirada => _agora != null && Time.GetTicksMsec() - _agora.ComecouMs < MsDeVirada;

	private Balde? Fase(string nome) => _baldes.FirstOrDefault(b => b.Nome == nome);

	// =====================================================================
	// LIGAR
	// =====================================================================
	private AudioEffectCapture? _escutaDoBarramento;
	private int _busDaVoz = -1;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		if (GameClient.Instance is { } cli) cli.Falou += AoOuvirOAnuncio;

		if (Falante) LigarAFala();
		else LigarAEscuta();
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli)
		{
			cli.Falou -= AoOuvirOAnuncio;
			cli.VozRecebida -= AoChegarPacote;
		}
		// METODO NOMEADO E `-=`, nunca lambda: os dois espioes sao ESTATICOS e sobreviveriam a este no.
		// E a licao das assinaturas vazadas -- e aqui um espiao vivo depois da bancada continuaria
		// contando quadros de uma partida de verdade.
		Microfone.FonteDeTeste = null;
		Microfone.Espiao -= AoMandarQuadro;
		VozOuvida.Espiao -= AoSairDoFiltro;

		if (_escutaDoBarramento != null && _busDaVoz >= 0
			&& AudioServer.GetBusEffectCount(_busDaVoz) > 0)
			AudioServer.RemoveBusEffect(_busDaVoz, AudioServer.GetBusEffectCount(_busDaVoz) - 1);

		DevolverOConfig();
	}

	/// <summary>
	/// O FALANTE: liga a opcao, poe a onda no lugar da captura e **aperta a tecla de verdade**.
	///
	/// ============================ EVENTO DE TECLADO, E NAO `Input.ActionPress` ============================
	/// A `--vozviva` aperta a ACAO (`ActionPress("falar_voz")`), o que basta pra ela -- ela mede audio. Aqui
	/// nao basta: duas das dez familias sao sobre a TECLA (o V remapeavel, e o F do voo), e `ActionPress`
	/// pula justamente a ligacao tecla->acao que elas afirmam. Com o evento injetado, religar a voz pro J
	/// e apertar o V deixa de mandar -- e essa e a prova, em bytes, de que o microfone le a ACAO e nao a
	/// tecla. E a mesma tecnica da `--diagtecla`.
	/// =================================================================================================
	/// </summary>
	private void LigarAFala()
	{
		GuardarOConfig();
		AsTeclas();

		Boot.Config.VozLigada = true;
		Boot.Config.VozApertarParaFalar = true;
		Microfone.Espiao += AoMandarQuadro;
		Microfone.FonteDeTeste = Encher;
		Apertar(Key.V);

		Nota($"falando com a onda conhecida ({RoboDeVozViva.HzGrave:0} Hz + {RoboDeVozViva.HzAgudo:0} Hz), "
		   + $"tecla fisica V, {VozLocal.QuadrosPorSegundo} quadros/s");
	}

	private void LigarAEscuta()
	{
		// A VOZ FICA DESLIGADA NO OUVINTE: ele nao pode ter microfone nenhum aberto, e isso prova de
		// graca que ouvir nao depende de falar.
		Boot.Config.VozLigada = false;
		VozOuvida.Espiao += AoSairDoFiltro;
		if (GameClient.Instance is { } cli) cli.VozRecebida += AoChegarPacote;

		// O BARRAMENTO E A UNICA FORMA DE VER A ATENUACAO POR DISTANCIA: o `VozOuvida.Espiao` entrega o
		// PCM depois da abafada mas ANTES do volume que o `AudioStreamPlayer2D` aplica -- e esse volume e
		// do motor, nao do nosso codigo. Sem esta escuta, "chega mais fraco" so poderia ser afirmado
		// relendo a nossa constante de atenuacao, que e o cego que este projeto ja pagou caro.
		_busDaVoz = AudioServer.GetBusIndex(AudioDirector.BusVoz);
		if (_busDaVoz >= 0)
		{
			_escutaDoBarramento = new AudioEffectCapture { BufferLength = 0.5f };
			AudioServer.AddBusEffect(_busDaVoz, _escutaDoBarramento);
		}
		Nota($"escutando e contando BYTES. bus 'Voz'={_busDaVoz}, volume da voz={Boot.Config.VolumeVoz:0.00}");
	}

	// =====================================================================
	// A CADENCIA DA INJECAO (papel `a`)
	// =====================================================================
	private ulong _proximoQuadroMs;

	/// <summary>
	/// QUANTAS VEZES O RITMO HONESTO. 1 em quase todas as fases; 5 nas duas da torneira -- que e o
	/// cliente modificado que o teto por falante existe pra conter.
	/// </summary>
	private int _vezesORitmo = 1;

	private bool Encher(short[] q)
	{
		ulong agora = Time.GetTicksMsec();
		if (_proximoQuadroMs == 0) _proximoQuadroMs = agora;

		// UM ENGASGO LONGO NAO VIRA RAJADA -- esta fonte e um relogio, nao uma captura: sem isto um
		// travamento de meio segundo devolveria 25 quadros de uma vez e o anel do `Microfone`
		// (`QuadrosProntos`, o mesmo remendo em producao) derrubaria 20, com a bancada medindo o
		// descarte de engasgo em vez da conversa.
		if (agora > _proximoQuadroMs + 200) _proximoQuadroMs = agora;

		if (agora < _proximoQuadroMs) return false;
		_proximoQuadroMs += (ulong)Math.Max(1, VozLocal.MsPorQuadro / _vezesORitmo);
		RoboDeVozViva.OndaConhecida(q);
		return true;
	}

	// =====================================================================
	// AS ESCUTAS
	// =====================================================================
	private void AoMandarQuadro(int bytes, int amostras)
	{
		if (_agora == null) return;
		_agora.QuadrosMandados++;
		_agora.BytesMandados += bytes;
	}

	/// <summary>O PACOTE, CRU. E aqui que a linha central desta bancada e contada.</summary>
	private void AoChegarPacote(int quem, ushort seq, byte dist, bool parede, byte[] dados, int n)
	{
		if (_agora == null) return;
		if (NaVirada) { _agora.PacotesNaVirada++; _agora.BytesNaVirada += n; return; }

		_agora.Pacotes++;
		_agora.BytesDeAudio += n;
		_agora.Falantes.Add(quem);
		_agora.SomaDist += dist;
		_agora.DistMin = Math.Min(_agora.DistMin, dist);
		_agora.DistMax = Math.Max(_agora.DistMax, dist);
		if (parede) _agora.MarcadosComParede++; else _agora.MarcadosSemParede++;

		CompararComAMinhaVista(quem, parede);
	}

	/// <summary>
	/// ============================ F5: A PAREDE DA VOZ CONTRA A PAREDE DA VISTA ============================
	/// A pergunta do dono e *"a consulta e uma so?"*. Ela nao se responde lendo o codigo do servidor: o
	/// defeito que ela existe pra pegar e alguem escrever um SEGUNDO tracador de raios, e dois tracadores
	/// leem igual e respondem diferente.
	///
	/// Entao aqui o ouvinte pergunta ao mapa DELE -- o `.vis` que ele usa pra desenhar a sombra, carregado
	/// pelo caminho do cliente -- e compara com o bit que veio carimbado no pacote. Batem em toda
	/// geometria, ou a familia reprova. Um servidor que perguntasse ao `.col` (o que BLOQUEIA) em vez do
	/// `.vis` (o que CEGA) divergiria na primeira porta; um que nao tivesse carregado mapa nenhum
	/// divergiria em toda parede.
	///
	/// NAO E DE GRACA QUE ELA E BARATA: `PathBlocked` e a MESMA funcao nos dois lados. E de proposito --
	/// e ela que este sistema promete usar. O que a comparacao prova e que o servidor a chamou sobre o
	/// MESMO mapa, com a MESMA convencao de olho (os pes), na MESMA zona.
	/// ==================================================================================================
	/// </summary>
	/// <summary>
	/// QUANTO TEMPO O CORPO DE QUEM FALA PRECISA PRA ASSENTAR depois de se mexer. Ver o bloco dentro do
	/// <see cref="CompararComAMinhaVista"/>.
	///
	/// 250 ms. O snapshot corre a 30 Hz e o boneco desenha com atraso fixo de interpolacao; um quarto de
	/// segundo cobre os dois com folga, e ainda deixa a maior parte de cada parada da varredura (600 ms)
	/// sendo medida.
	/// </summary>
	private const ulong MsDeTransito = 250;

	private Vector2 _ondeEleEstava;
	private ulong _assentaEm;

	/// <summary>O servidor deste processo -- o juiz e o host, e e a ele que a guarda de transito pergunta onde o falante esta.</summary>
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance as Jandirus.Server.GameServer;

	private void CompararComAMinhaVista(int quem, bool paredeDoPacote)
	{
		if (_agora == null) return;
		if (World.Instancia is not { } mundo || GameClient.Instance is not { } cli) return;

		// ZONA SEM MAPA DE VISTA nao esconde nada, nem pra voz nem pra sombra (ver `MapaDaVista`). Nao ha
		// o que comparar, e contar isso como concordancia inflaria o placar com nada.
		if (mundo.VeuDeTeste is not { Mapa: { } mapa }) return;

		Node2D? meu = mundo.CorpoDeTeste(cli.LocalId);
		Node2D? dele = mundo.CorpoDeTeste(quem);
		if (meu == null || dele == null) { _agora.VistaSemCorpo++; return; }

		Vector2 a = meu.GlobalPosition, b = dele.GlobalPosition;

		// ============================ CORPO EM TRANSITO NAO SE COMPARA -- ACHADO RODANDO ============================
		// A primeira rodada desta bancada acusou **uma** divergencia em 773 comparacoes, e ela estava na
		// varredura -- a unica fase em que o falante MUDA DE LUGAR sem mudar de fase. E o defeito nao era
		// do sistema: o servidor carimba o pacote com a posicao NOVA no mesmo instante em que a manda, e
		// o corpo aqui ainda esta chegando na posicao nova (o snapshot vem com atraso fixo e o boneco
		// interpola). Por um par de quadros, os dois lados estao falando de dois lugares diferentes.
		//
		// Comparar ali seria medir o atraso da interpolacao e chamar de "a voz e a vista discordam".
		// Entao a comparacao espera o corpo assentar -- e **o que foi pulado sai numa coluna propria**,
		// pelo mesmo motivo dos pacotes da virada: um "em transito" que virasse a maioria seria um
		// defeito de verdade (o corpo nunca assentando), e apagado ninguem veria.
		// ========================================================================================================
		ulong agora = Time.GetTicksMsec();
		if ((b - _ondeEleEstava).LengthSquared() > 16f) { _ondeEleEstava = b; _assentaEm = agora + MsDeTransito; }
		if (agora < _assentaEm) { _agora.VistaEmTransito++; return; }
		// ============================ E A OUTRA METADE DO TRANSITO: O SERVIDOR JA O MOVEU, A TELA AINDA NAO ============================
		// O pacote de voz e carimbado com a posicao do SERVIDOR no instante em que sai; o corpo desenhado chega
		// um snapshot depois (~100 ms). A guarda acima so via o transito DEPOIS de a tela mexer. Ela bastava
		// enquanto os inputs em voo do falante arrastavam o corpo de volta a cada teleporte da bancada (o
		// vaivem reiniciava a guarda o tempo todo); desde que o arranque deixou de ser desfeito por eles
		// (`AplicarInput`, 2026-09-05) o teleporte crava o corpo no ponto na hora, e essa janela apareceu:
		// 2 a 7 pacotes por rodada com a parede NOVA contra a tela VELHA. O juiz e o host: pergunta ao servidor
		// onde o falante ESTA e so compara quando a tela concorda com ele -- e conta o que pulou na mesma
		// coluna de "em transito", pra um transito que virasse maioria continuar aparecendo.
		// ==================================================================================================================
		// (a posicao do servidor e a MESMA que o node desenha -- `RemotePlayer.Desenhar` so a prende a grade; nao e o pe)
		if (S is { } srv && (srv.EstadoDoCorpoNaFoto(quem).Pos - new Vec2(b.X, b.Y)).Length > 4f)
		{ _agora.VistaEmTransito++; return; }
		bool paredeDaVista = mapa.PathBlocked(new Vec2(a.X, a.Y), new Vec2(b.X, b.Y));

		if (paredeDaVista) _agora.VistaDisseParede++; else _agora.VistaDisseLivre++;
		if (paredeDaVista == paredeDoPacote) _agora.VistaConcordou++;
		else
		{
			_agora.VistaDivergiu++;
			// CADA DIVERGENCIA SAI COM O QUE A EXPLICA (e nao so a soma): foi assim que a janela "o servidor ja
			// o moveu, a tela ainda nao" apareceu. No mutante a divergencia e o esperado -- nao vale imprimir.
			if (!_agora.Mutante)
				GD.Print($"[vozdupla:b]   ...  F5 DIVERGIU: pacote parede={paredeDoPacote} vista={paredeDaVista} eu=({a.X:0},{a.Y:0}) ele=({b.X:0},{b.Y:0}) assentou ha {agora - (_assentaEm - MsDeTransito)} ms na fase {_agora.Nome}");
		}
	}

	/// <summary>O QUADRO QUE VAI PRO ALTO-FALANTE -- ja com a abafada aplicada. E aqui que F4 e medida.</summary>
	private void AoSairDoFiltro(int quem, short[] pcm, int n, byte dist, bool parede)
	{
		if (_agora == null || n <= 0 || NaVirada) return;

		_agora.Quadros++;

		double soma = 0;
		for (int i = 0; i < n; i++)
		{
			double v = pcm[i] / (double)short.MaxValue;
			soma += v * v;
		}
		_agora.SomaRms += Math.Sqrt(soma / n);
		_agora.SomaEGrave += RoboDeVozViva.Energia(pcm, n, RoboDeVozViva.HzGrave, VozLocal.TaxaDeSaida);   // PCM de SAIDA
		_agora.SomaEAgudo += RoboDeVozViva.Energia(pcm, n, RoboDeVozViva.HzAgudo, VozLocal.TaxaDeSaida);
	}

	public override void _Process(double delta)
	{
		if (_escutaDoBarramento == null || _agora == null) return;

		int ha = _escutaDoBarramento.GetFramesAvailable();
		if (ha <= 0) return;
		Vector2[] mix = _escutaDoBarramento.GetBuffer(ha);

		// NA VIRADA, TIRA E JOGA FORA: o `AudioStreamGenerator` guarda 0,25 s de fila, entao o que o motor
		// mixa agora e o que chegou ate um quarto de segundo atras -- a fase anterior. Deixar no buffer
		// seria pior que descartar: entraria inteiro no balde seguinte, num lote so.
		if (NaVirada) return;

		foreach (Vector2 s in mix)
		{
			double v = (Math.Abs(s.X) + Math.Abs(s.Y)) * 0.5;
			_agora.PicoDoBarramento = Math.Max(_agora.PicoDoBarramento, v);
			_agora.SomaQuadradoDoBarramento += v * v;
		}
		_agora.AmostrasDoBarramento += mix.Length;
	}

	// =====================================================================
	// AS FASES
	// =====================================================================
	/// <summary>
	/// O ANUNCIO DE FASE, PELO CANAL DE TEXTO DO SERVIDOR -- a unica sincronia entre os dois processos.
	/// Ela precisa ser do servidor: com cada cliente contando o proprio relogio, o `b` estaria medindo a
	/// fase seguinte enquanto o servidor ainda nao tinha movido ninguem.
	/// </summary>
	private void AoOuvirOAnuncio(Protocol.Fala canal, string autor, string texto)
	{
		if (canal != Protocol.Fala.Sistema || !texto.StartsWith("[vozdupla] fase", StringComparison.Ordinal))
			return;

		Fechar();

		if (texto.Contains("nome=fim", StringComparison.Ordinal)) { Placar(); return; }

		var b = new Balde { ComecouMs = Time.GetTicksMsec() };
		foreach (string parte in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			string[] kv = parte.Split('=', 2);
			if (kv.Length != 2) continue;
			switch (kv[0])
			{
				case "fase": int.TryParse(kv[1], out b.Numero); break;
				case "nome": b.Nome = kv[1]; break;
				case "d":
					float.TryParse(kv[1], System.Globalization.NumberStyles.Float,
								   System.Globalization.CultureInfo.InvariantCulture, out b.DistPedida);
					break;
				case "parede": b.ParedePedida = kv[1] == "1"; break;
				case "mut": b.Mutante = kv[1] == "1"; break;
			}
		}
		_baldes.Add(b);
		_agora = b;

		if (Falante) ObedecerAFase(b.Nome);
		Nota($"--> fase {b.Numero} {b.Nome}{(b.Mutante ? " [MUTANTE]" : "")} "
		   + $"(d={b.DistPedida:0} px, parede={b.ParedePedida})");
	}

	private void Fechar()
	{
		if (_agora == null) return;
		_agora.FechouMs = Time.GetTicksMsec();
		_agora = null;
	}

	/// <summary>
	/// O QUE O FALANTE FAZ EM CADA FASE. Fora destas, ele so segura a tecla e fala.
	///
	/// Tudo aqui passa pelos caminhos de PRODUCAO: a tecla e um evento de teclado, a religacao e a
	/// `Teclas.Religar` da tela de teclas, e o microfone aberto e a opcao `VozApertarParaFalar` que
	/// existe no `Settings`. Nao ha atalho de bancada nenhum -- o que ela exercita e o que o jogador tem.
	/// </summary>
	private void ObedecerAFase(string nome)
	{
		_vezesORitmo = nome is "torneira" or "torneira_solta" ? 5 : 1;

		switch (nome)
		{
			case "soltar":
				// F6: SOLTAR PARA DE MANDAR. Sem religar nada e sem mexer em opcao: so a tecla subindo.
				Soltar(Key.V);
				break;

			case "mic_aberto":
				// O MUTANTE DE F6, e ele e um caminho de PRODUCAO: o modo de microfone aberto. E a forma
				// real deste defeito -- a tecla solta e o aparelho seguindo --, e por isso o padrao de
				// fabrica e o aperta-pra-falar.
				Soltar(Key.V);
				Boot.Config.VozApertarParaFalar = false;
				break;

			case "remapeado":
				// F10: a voz vai pro J pelo caminho da tela de teclas, e o dedo vai pro J.
				Boot.Config.VozApertarParaFalar = true;
				Soltar(Key.V);
				Teclas.Religar("falar_voz", Key.J);
				Apertar(Key.J);
				break;

			case "remapeado_v":
				// A OUTRA METADE DE F10, e a que prova alguma coisa: religada pro J, o V nao pode mandar
				// nada. So o par das duas separa "a tecla funciona" de "o microfone le a tecla V crua".
				Soltar(Key.J);
				Apertar(Key.V);
				break;

			default:
				// A VOLTA AO NORMAL, e ela e ativa: as fases mexem em tecla e em opcao, e deixar qualquer
				// uma ligada faria a fase seguinte medir outra coisa sem dizer.
				Boot.Config.VozApertarParaFalar = true;
				if (Array.IndexOf(Teclas.Teclado("falar_voz"), Key.V) < 0) Teclas.Restaurar("falar_voz");
				Soltar(Key.J);
				Apertar(Key.V);
				break;
		}
	}

	// =====================================================================
	// TECLADO DE MENTIRA -- eventos de verdade, injetados no motor
	// =====================================================================
	/// <summary>
	/// AS DUAS METADES PREENCHIDAS: `PhysicalKeycode` e o que o registro le e o que o `InputMap` casa;
	/// `Keycode` e o que as telas leem. Um teclado de verdade manda os dois. Mesma receita da
	/// `--diagtecla`.
	/// </summary>
	private static void Tecla(Key k, bool apertada) =>
		Godot.Input.ParseInputEvent(new InputEventKey
		{
			Keycode = k,
			PhysicalKeycode = k,
			Pressed = apertada,
		});

	private static void Apertar(Key k) => Tecla(k, true);
	private static void Soltar(Key k) => Tecla(k, false);

	// =====================================================================
	// F9 e F10: AS TECLAS (papel `a`)
	// =====================================================================
	/// <summary>
	/// ============================ F9: O VOO ESTA NO F, E QUEM REMAPEOU CONTINUA COM A TECLA DELE ============================
	/// A primeira metade e o pedido literal do dono. A segunda e o defeito que trocar o padrao destapou:
	/// ate a mudanca, o padrao e o save eram escritos um por cima do outro **sem ninguem perguntar se
	/// batiam**. Enquanto o padrao nao mudava dava no mesmo; no instante em que ele muda, quem tivesse o
	/// `descer` gravado no F ficaria com o descer (do arquivo) E o pairar (do padrao novo) na MESMA
	/// tecla, os dois no `InputMap` -- e CALADO, porque `DonoDe` devolve o primeiro da tabela.
	///
	/// **A CHECAGEM LE O `InputMap` E NAO A TABELA.** E de proposito: o `InputMap` e o que o jogo obedece
	/// (o `LocalPlayer` e o `Microfone` leem de la), e e nele que o defeito aparece. Ler a tabela mediria
	/// a intencao; ler a projecao mede o que o dedo vai encontrar. E e por isso que o defeito pode ser
	/// INJETADO: basta acrescentar o segundo dono no `InputMap` de verdade.
	/// ====================================================================================================================
	/// </summary>
	private void AsTeclas()
	{
		Teclas.Aplicar(Boot.Config);

		Checa("F9 o pairar esta no F de fabrica",
			Array.IndexOf(Teclas.Teclado("voar"), Key.F) >= 0, Teclas.NomeDaAcao("voar"));
		Checa("F9 ...e ele saiu do V (que e onde a voz entrou)",
			Array.IndexOf(Teclas.Teclado("voar"), Key.V) < 0, Teclas.NomeDaAcao("voar"));
		Checa("F10 a voz esta no V de fabrica",
			Array.IndexOf(Teclas.Teclado("falar_voz"), Key.V) >= 0, Teclas.NomeDaAcao("falar_voz"));

		Checa("F9 o F tem UM dono so no InputMap (e nao dois calados)",
			DonosNoInputMap(Key.F).Count == 1, string.Join(" + ", DonosNoInputMap(Key.F)));
		Checa("F10 o V tem UM dono so no InputMap",
			DonosNoInputMap(Key.V).Count == 1, string.Join(" + ", DonosNoInputMap(Key.V)));

		// ---- quem remapeou continua com a tecla dele ----
		// UM SAVE DE MENTIRA, EM MEMORIA: o do jogador que gravou o `descer` no F antes de o padrao
		// mudar. E o caso exato que a mudanca de padrao criou, e ele nao existe em maquina nenhuma pra
		// ser encontrado -- so forjando o save da pra pergunta-lo.
		var save = new Settings();
		save.LigacoesDeTecla.Add(new Settings.LigacaoDeTecla { Acao = "descer", Tecla = "F" });
		Teclas.Aplicar(save);

		Checa("F9 quem GRAVOU o descer no F continua com o descer no F",
			Teclas.Teclado("descer") is [Key.F], Teclas.NomeDaAcao("descer"));
		Checa("F9 ...e o padrao novo NAO se instala junto na mesma tecla",
			DonosNoInputMap(Key.F).Count == 1, string.Join(" + ", DonosNoInputMap(Key.F)));
		Checa("F9 ...e o pairar fica visivelmente SEM tecla, em vez de perder calado",
			Teclas.Teclado("voar").Length == 0, Teclas.NomeDaAcao("voar"));

		// O CONFIG DE VERDADE VOLTA ANTES DE QUALQUER OUTRA COISA: `Aplicar` guarda a referencia, e o
		// proximo `Religar` gravaria o save de mentira por cima do arquivo do dono.
		Teclas.Aplicar(Boot.Config);

		// ---- a injecao: o defeito posto na frente da checagem ----
		// O DEFEITO E O QUE ACONTECIA ANTES DA REGRA: os dois donos na mesma tecla, no `InputMap`, sem
		// nada na tela dizendo. Posto na mao, no mapa de verdade.
		InputMap.ActionAddEvent("descer", new InputEventKey { PhysicalKeycode = Key.F });
		Injeta("F9 a checagem de dono unico pega os dois donos na mesma tecla",
			DonosNoInputMap(Key.F).Count > 1, string.Join(" + ", DonosNoInputMap(Key.F)));
		Teclas.Aplicar(Boot.Config);   // reprojetar apaga os eventos e devolve o mapa
		Checa("F9 (volta) reaplicado o registro, o F volta a ter um dono so",
			DonosNoInputMap(Key.F).Count == 1, string.Join(" + ", DonosNoInputMap(Key.F)));

		// ---- F10: religa e restaura, pelo caminho da tela de teclas ----
		bool foi = Teclas.Religar("falar_voz", Key.J);
		Checa("F10 a voz se religa como qualquer outra acao",
			foi && DonosNoInputMap(Key.J).Contains("falar_voz"), Teclas.NomeDaAcao("falar_voz"));
		Checa("F10 ...e o V fica livre nesse instante",
			DonosNoInputMap(Key.V).Count == 0, string.Join(" + ", DonosNoInputMap(Key.V)));
		Teclas.Restaurar("falar_voz");
		Checa("F10 ...e 'restaurar' devolve o V",
			Array.IndexOf(Teclas.Teclado("falar_voz"), Key.V) >= 0, Teclas.NomeDaAcao("falar_voz"));
	}

	/// <summary>
	/// QUEM MANDA NESTA TECLA **NO `InputMap`** -- pode ser mais de um, e e isso que a bancada quer saber.
	///
	/// `Teclas.DonoDe` devolve o PRIMEIRO da tabela e por isso nao serve aqui: ela e a pergunta do
	/// jogador ("de quem e esta tecla?"), e o defeito que F9 persegue e justamente o segundo dono que
	/// aquela pergunta nunca revela.
	/// </summary>
	private static List<string> DonosNoInputMap(Key k)
	{
		var donos = new List<string>();
		foreach (AcaoDeTecla a in Teclas.Todas)
		{
			if (!a.NoInputMap || !InputMap.HasAction(a.Id)) continue;
			foreach (InputEvent e in InputMap.ActionGetEvents(a.Id))
				if (e is InputEventKey ik && Teclas.Fisica(ik) == k) { donos.Add(a.Id); break; }
		}
		return donos;
	}

	// =====================================================================
	// O CONFIG DA MAQUINA
	// =====================================================================
	/// <summary>
	/// A COPIA DE SEGURANCA EM DISCO -- a mesma disciplina (e a mesma licao) da `--diagtecla`.
	///
	/// Ligacao de tecla e preferencia de MAQUINA: nao ha config de bancada, o arquivo que ela grava e o
	/// do dono. E guardar em variavel nao basta -- uma rodada interrompida no meio morre com a ligacao
	/// de teste gravada, e a rodada seguinte copiaria ESSA como se fosse a original.
	/// </summary>
	private const string ArquivoDeConfig = "user://config.json";
	private const string CopiaDeSeguranca = "user://config.json.bancada-vozdupla";
	private const string NaoExistia = "(nao existia)";
	private string _configOriginal = "";

	private void GuardarOConfig()
	{
		if (Godot.FileAccess.FileExists(CopiaDeSeguranca))
		{
			Nota("AVISO: a rodada anterior nao devolveu o config. Usando a copia de seguranca dela.");
			_configOriginal = Godot.FileAccess.GetFileAsString(CopiaDeSeguranca);
			return;
		}
		_configOriginal = Godot.FileAccess.FileExists(ArquivoDeConfig)
			? Godot.FileAccess.GetFileAsString(ArquivoDeConfig) : NaoExistia;
		using Godot.FileAccess? f = Godot.FileAccess.Open(CopiaDeSeguranca, Godot.FileAccess.ModeFlags.Write);
		f?.StoreString(_configOriginal);
	}

	private void DevolverOConfig()
	{
		if (!Falante || _configOriginal.Length == 0) return;

		if (_configOriginal == NaoExistia) DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(ArquivoDeConfig));
		else
		{
			using Godot.FileAccess? f = Godot.FileAccess.Open(ArquivoDeConfig, Godot.FileAccess.ModeFlags.Write);
			f?.StoreString(_configOriginal);
		}
		DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(CopiaDeSeguranca));
		_configOriginal = "";
	}

	// =====================================================================
	// O PLACAR
	// =====================================================================
	private void Placar()
	{
		if (Falante) { PlacarDoFalante(); return; }
		Tabelas();
		Julgar();

		GD.Print($"[vozdupla:b] ============ {_ok} OK, {_falha} FALHA(S)"
			   + (_falha > 0 ? $": {string.Join(" | ", _reprovadas)}" : "")
			   + $" | injecao: {_injOk} pegou, {_injFalha} passou batido"
			   + (_injFalha > 0 ? $": {string.Join(" | ", _injPassouBatido)}" : "")
			   + " ============");
		foreach (string s in _naoMedido) GD.Print($"[vozdupla:b] NAO MEDIDO: {s}");

		GetTree().CreateTimer(1.5).Timeout += () => GetTree().Quit(_falha == 0 && _injFalha == 0 ? 0 : 1);
	}

	private void PlacarDoFalante()
	{
		GD.Print($"[vozdupla:a] ============ O QUE SAIU DAQUI ({_ok} OK, {_falha} FALHA(S) nas teclas) ============");
		GD.Print("[vozdupla:a] fase             quadros   B audio   quadros/s");
		foreach (Balde b in _baldes)
			GD.Print($"[vozdupla:a] {b.Nome,-16} {b.QuadrosMandados,7} {b.BytesMandados,9} "
				   + $"{b.QuadrosMandados / Math.Max(b.Segundos, 0.001),11:0.0}");
		if (_falha > 0) GD.PrintErr($"[vozdupla:a] FALHAS: {string.Join(" | ", _reprovadas)}");
		if (_injFalha > 0) GD.PrintErr($"[vozdupla:a] INJECAO PASSOU BATIDO: {string.Join(" | ", _injPassouBatido)}");
		GetTree().CreateTimer(1.5).Timeout += () => GetTree().Quit(_falha == 0 && _injFalha == 0 ? 0 : 1);
	}

	private void Tabelas()
	{
		GD.Print("[vozdupla:b] --- O QUE CHEGOU (o pacote, cru -- a linha central conta BYTES) ---");
		GD.Print("[vozdupla:b] fase             mut  pacotes   B audio   pct/s   dist      marcado        virada");
		foreach (Balde b in _baldes)
		{
			string marca = b.MarcadosComParede > 0 && b.MarcadosSemParede > 0
				? $"MISTO {b.MarcadosComParede}/{b.MarcadosSemParede}"
				: b.MarcadosComParede > 0 ? "parede" : b.Pacotes > 0 ? "limpo" : "-";
			string dist = b.Pacotes > 0 ? $"{b.DistMedia:0}({b.DistMin}-{b.DistMax})" : "-";
			GD.Print($"[vozdupla:b] {b.Nome,-16} {(b.Mutante ? "M" : " "),3} {b.Pacotes,8} {b.BytesDeAudio,9} "
				   + $"{b.PacotesPorSegundo,7:0.0} {dist,-10} {marca,-14} {b.PacotesNaVirada}p/{b.BytesNaVirada}B");
		}

		GD.Print("[vozdupla:b] --- COMO SOA (PCM pos-abafada) e O QUE O MOTOR MIXOU (bus 'Voz', pos-atenuacao) ---");
		GD.Print("[vozdupla:b] fase             quadros     rms    E(300)    E(3k)  agudo/grave   bus rms");
		foreach (Balde b in _baldes)
		{
			double q = Math.Max(b.Quadros, 1);
			GD.Print($"[vozdupla:b] {b.Nome,-16} {b.Quadros,7} {b.Rms,7:0.0000} {b.SomaEGrave / q,9:0.0000} "
				   + $"{b.SomaEAgudo / q,8:0.0000} {b.RazaoAgudoGrave,12:0.000} "
				   + $"{(b.BusRms >= 0 ? b.BusRms.ToString("0.0000") : "nao medido"),9}");
		}

		GD.Print("[vozdupla:b] --- A PAREDE DA VOZ CONTRA A PAREDE DA MINHA VISTA (o mesmo .vis da sombra) ---");
		GD.Print("[vozdupla:b] fase             concordou  divergiu  a vista disse parede/livre  em transito  sem corpo");
		foreach (Balde b in _baldes)
			GD.Print($"[vozdupla:b] {b.Nome,-16} {b.VistaConcordou,9} {b.VistaDivergiu,9} "
				   + $"{b.VistaDisseParede,14}/{b.VistaDisseLivre,-10} {b.VistaEmTransito,11} {b.VistaSemCorpo,9}");
	}

	// =====================================================================
	// O VEREDITO
	// =====================================================================
	private void Julgar()
	{
		Balde? perto = Fase("perto"), longe = Fase("longe"), fora = Fase("fora");
		Balde? vazando = Fase("fora_vazando");
		Balde? parede = Fase("parede"), aberto = Fase("aberto"), cega = Fase("parede_cega");
		Balde? varrida = Fase("vistavarrida");
		Balde? soltar = Fase("soltar"), micAberto = Fase("mic_aberto");
		Balde? remap = Fase("remapeado"), remapV = Fase("remapeado_v");
		Balde? torneira = Fase("torneira"), solta = Fase("torneira_solta");
		Balde? calado = Fase("calado"), esquecido = Fase("calado_esquecido");

		if (_baldes.Count < 17)
		{
			Checa("a cena inteira rodou (17 fases)", false, $"{_baldes.Count} fases");
			return;
		}

		// ---------------------------------------------------------- F1
		// A LINHA CENTRAL. Conta BYTES: uma que contasse volume ficaria verde com a sala inteira
		// recebendo tudo, porque quem esta longe soa baixo dos dois jeitos.
		Checa("F1 fora do alcance NAO RECEBE -- zero pacotes e zero bytes",
			fora!.Pacotes == 0 && fora.BytesDeAudio == 0,
			$"{fora.Pacotes} pacotes / {fora.BytesDeAudio} B (+{fora.PacotesNaVirada} na virada, que sao da fase anterior)");

		Injeta("F1 com o servidor mandando pra zona inteira, a linha do ZERO fica vermelha",
			!(vazando!.Pacotes == 0 && vazando.BytesDeAudio == 0),
			$"chegaram {vazando.Pacotes} pacotes / {vazando.BytesDeAudio} B a 30 tiles");

		// ============================ E ESTA E A LINHA QUE JUSTIFICA CONTAR BYTES ============================
		// No vazamento o corpo continua a 30 tiles, entao o motor atenua a voz ate o silencio: o barramento
		// marca praticamente ZERO. Uma bancada que perguntasse *"da pra ouvir?"* -- ou que lesse volume, ou
		// que ouvisse o alto-falante -- passaria VERDE com a sala inteira recebendo tudo. Os bytes chegaram
		// assim mesmo, e quem le pacote nao precisa do nosso volume.
		//
		// A afirmacao so vale se o barramento tiver mixado alguma coisa nesta rodada; se nao, sai dita.
		if (perto!.BusRms > 0 && vazando.BusRms >= 0)
			Injeta("F1 (a razao de contar BYTES) no vazamento o VOLUME e ~zero e mesmo assim os bytes chegaram",
				vazando.BusRms < perto.BusRms * 0.05 && vazando.BytesDeAudio > 0,
				$"bus rms {vazando.BusRms:0.0000} (perto={perto.BusRms:0.0000}) com {vazando.BytesDeAudio} B entregues");
		else
			_naoMedido.Add("o volume do vazamento no barramento (o motor nao mixou nada neste processo)");

		// ---------------------------------------------------------- F2
		// O CONTRA-EXEMPLO: sem ele, um servidor que nunca mandasse nada passaria verde na bancada
		// inteira -- inclusive em F1.
		Checa("F2 dentro do alcance RECEBE",
			perto!.Pacotes > 0 && perto.BytesDeAudio > 0,
			$"{perto.Pacotes} pacotes / {perto.BytesDeAudio} B");
		Checa("F2 ...e o audio chega decodificavel (o PCM saiu do decodificador)",
			perto.Quadros > 0 && perto.Rms > 0.05, $"{perto.Quadros} quadros, rms {perto.Rms:0.0000}");
		Checa("F2 ...e o remetente e o falante da cena, e mais ninguem",
			perto.Falantes.Count == 1, string.Join(",", perto.Falantes));

		// ---------------------------------------------------------- F3
		Checa("F3 mais longe chega mais fraco: a distancia carimbada CRESCE",
			longe!.DistMedia > perto.DistMedia,
			$"perto={perto.DistMedia:0} longe={longe.DistMedia:0}");
		Checa("F3 ...e o volume derivado dela CAI",
			VozLocal.VolumePelaDistancia(VozLocal.FracaoDaDistancia((byte)perto.DistMedia))
			> VozLocal.VolumePelaDistancia(VozLocal.FracaoDaDistancia((byte)longe.DistMedia)));

		// O QUE O MOTOR MIXOU e a unica medida que nao le constante nossa nenhuma. Quando ela nao existe
		// (driver Dummy sem mixagem), sai DITA e nao sai zero.
		if (perto.BusRms > 0 && longe.BusRms >= 0)
			Checa("F3 ...e o que o MOTOR mixou cai junto (atenuacao 2D, medida no barramento)",
				longe.BusRms < perto.BusRms, $"perto={perto.BusRms:0.0000} longe={longe.BusRms:0.0000}");
		else
			_naoMedido.Add("a atenuacao 2D no barramento (o motor nao mixou nada neste processo)");

		Injeta("F3 com o corte vazado, a distancia carimbada MENTE (mais longe, e chega como 0)",
			vazando.Pacotes > 0 && vazando.DistMedia < longe.DistMedia,
			$"a 30 tiles chegou dist={vazando.DistMedia:0}, contra {longe.DistMedia:0} a 20 tiles");

		// ---------------------------------------------------------- F4
		// A COMPARACAO E CONTRA A MESMA DISTANCIA SEM PAREDE -- senao mede distancia e chama de parede.
		Checa("F4 a fase da parede e a do controle estao a MESMA distancia",
			Math.Abs(parede!.DistMedia - aberto!.DistMedia) <= 8,
			$"parede={parede.DistMedia:0} aberto={aberto.DistMedia:0} (em bytes de distancia)");
		Checa("F4 atras da parede o quadro sai MARCADO, e no aberto sai LIMPO",
			parede.MarcadosComParede > 0 && parede.MarcadosSemParede == 0
			&& aberto.MarcadosComParede == 0 && aberto.MarcadosSemParede > 0,
			$"parede {parede.MarcadosComParede}/{parede.MarcadosSemParede}, "
			+ $"aberto {aberto.MarcadosComParede}/{aberto.MarcadosSemParede}");
		Checa("F4 e a ABAFADA e de verdade: o agudo morre e o grave sobrevive",
			parede.RazaoAgudoGrave < aberto.RazaoAgudoGrave * 0.5,
			$"agudo/grave: parede={parede.RazaoAgudoGrave:0.000} aberto={aberto.RazaoAgudoGrave:0.000}");
		Checa("F4 ...e o volume total cai junto (uma parede tambem atenua)",
			parede.Rms < aberto.Rms, $"rms parede={parede.Rms:0.0000} aberto={aberto.Rms:0.0000}");

		Injeta("F4 com o corte respondendo 'sem parede', a abafada some e a linha fica vermelha",
			!(cega!.RazaoAgudoGrave < aberto.RazaoAgudoGrave * 0.5),
			$"agudo/grave na mesma parede: cega={cega.RazaoAgudoGrave:0.000} contra aberto={aberto.RazaoAgudoGrave:0.000}");

		// ---------------------------------------------------------- F5
		// A CONSULTA E UMA SO: o bit do pacote contra o `.vis` com que EU desenho a sombra.
		int concordou = _baldes.Where(b => !b.Mutante).Sum(b => b.VistaConcordou);
		int divergiu = _baldes.Where(b => !b.Mutante).Sum(b => b.VistaDivergiu);
		int disseParede = _baldes.Where(b => !b.Mutante).Sum(b => b.VistaDisseParede);
		int disseLivre = _baldes.Where(b => !b.Mutante).Sum(b => b.VistaDisseLivre);

		Checa("F5 a parede que a VOZ enxerga e a mesma que a MINHA VISTA enxerga",
			divergiu == 0 && concordou > 0, $"{concordou} concordaram, {divergiu} divergiram");
		Checa("F5 ...e a comparacao viu os DOIS casos (senao ela so provaria um 'sempre')",
			disseParede > 0 && disseLivre > 0, $"a vista disse parede em {disseParede} e livre em {disseLivre}");
		Checa("F5 ...inclusive na varredura, com o falante parando em varios pontos",
			varrida!.VistaDivergiu == 0 && varrida.VistaConcordou > 0,
			$"{varrida.VistaConcordou} concordaram, {varrida.VistaDivergiu} divergiram");

		Injeta("F5 com o corte respondendo 'sem parede', a voz e a vista DISCORDAM",
			cega.VistaDivergiu > 0, $"{cega.VistaDivergiu} divergencias em {cega.Pacotes} pacotes");

		// ---------------------------------------------------------- F6
		Checa("F6 soltar a tecla PARA DE MANDAR -- zero bytes depois de soltar",
			soltar!.Pacotes == 0 && soltar.BytesDeAudio == 0,
			$"{soltar.Pacotes} pacotes / {soltar.BytesDeAudio} B (+{soltar.PacotesNaVirada} na virada)");
		Injeta("F6 com o microfone ABERTO (a tecla solta e o aparelho seguindo), a linha fica vermelha",
			micAberto!.Pacotes > 0, $"{micAberto.Pacotes} pacotes com a tecla solta");

		// ---------------------------------------------------------- F7
		Checa("F7 calado NAO E OUVIDO (o admin calou pelo funil de verdade)",
			calado!.Pacotes == 0 && calado.BytesDeAudio == 0,
			$"{calado.Pacotes} pacotes / {calado.BytesDeAudio} B");
		Injeta("F7 esquecida a marca do mute, o quadro do calado atravessa",
			esquecido!.Pacotes > 0, $"{esquecido.Pacotes} pacotes");

		// ---------------------------------------------------------- F8
		Checa("F8 o teto por falante segura quem manda 5x demais",
			torneira!.PacotesPorSegundo <= VozLocal.QuadrosPorSegundo * 1.3,
			$"{torneira.PacotesPorSegundo:0.0} pacotes/s (o teto e {VozLocal.QuadrosPorSegundo})");
		Injeta("F8 com o balde de credito esvaziado a cada tique, o excesso passa",
			solta!.PacotesPorSegundo > VozLocal.QuadrosPorSegundo * 1.3,
			$"{solta.PacotesPorSegundo:0.0} pacotes/s");

		// ---------------------------------------------------------- F10 (em bytes)
		Checa("F10 religada a voz pro J, apertar o J MANDA",
			remap!.Pacotes > 0, $"{remap.Pacotes} pacotes / {remap.BytesDeAudio} B");
		Checa("F10 ...e apertar o V (que nao e mais dela) NAO MANDA NADA",
			remapV!.Pacotes == 0,
			$"{remapV.Pacotes} pacotes -- se saiu algo, o microfone le a tecla V e nao a acao");
	}
}
