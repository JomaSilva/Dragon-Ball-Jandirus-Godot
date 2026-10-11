using Concentus;
using Godot;
using Jandirus.Core.Social;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ O RADIO DO SCOUTER, NO OUVIDO DE QUEM RECEBE ============================
/// *"...podem escutar sua voz NO OUVIDO ESQUERDO deles com um EFEITO DE RADIO"* -- o dono, 2026-10-10.
///
/// Chega um quadro de 20 ms de alguem que o SERVIDOR ja decidiu que eu posso ouvir pelo radio: scouter
/// ligado nas duas pontas, a mesma frequencia, o mesmo planeta (ou ate 500 tiles, no espaco) -- ver
/// `GameServer.Radio.cs`. Aqui nao ha o que filtrar. O que sobra pra este arquivo:
///
///   | o que                 | como                                                                    |
///   |-----------------------|-------------------------------------------------------------------------|
///   | picote                | a MESMA <see cref="FilaDeJitter"/> da voz de perto, guiada pelo gerador |
///   | o efeito              | <see cref="EfeitoDeRadio"/> no PCM: banda estreita, saturacao, chiado   |
///   | o ouvido esquerdo     | <see cref="EfeitoDeRadio.ParaOOuvidoEsquerdo"/>: o canal direito e ZERO |
///   | abrir e fechar        | um estalo de estatica em cada ponta da transmissao -- na dos outros e na MINHA |
///   | quem esta no radio    | a faixa no alto da tela, e o sinal sobre a cabeca (<see cref="SinalDeVoz"/>) |
///
/// ============================ ELE E IRMAO DO <see cref="VozOuvida"/>, E NAO UM MODO DELE ============================
/// A voz de perto e POSICIONAL: um `AudioStreamPlayer2D` em cima do corpo, e o motor decide volume e lado.
/// O radio nao tem lugar -- quem fala pode estar do outro lado do planeta --, entao o tocador e o
/// `AudioStreamPlayer` comum, e o lado e escrito no proprio quadro. E a mesma pessoa pode chegar pelos DOIS
/// caminhos ao mesmo tempo (as duas teclas seguradas, e eu ao lado dela na mesma frequencia): sao dois
/// fluxos, com duas sequencias, e cada um tem a propria fila e o proprio decodificador. Uma fonte so por
/// pessoa leria o segundo fluxo como "pacote duplicado".
/// ====================================================================================================================
/// </summary>
public partial class RadioOuvido : Node
{
	public static RadioOuvido? Instancia { get; private set; }

	/// <summary>
	/// COMO SE CHAMA ESTE ID -- dado por quem cria o no (o `World`, que guarda os nomes que chegam pelo
	/// `PeerLook` de todo corpo da zona, perto ou longe). Vazio = nao sei; a faixa escreve "alguém".
	/// </summary>
	public Func<int, string>? QuemE;

	/// <summary>A capacidade da fila do gerador: a mesma do <see cref="VozOuvida"/>, pela mesma conta.</summary>
	private const float SegundosDeFila = 0.3f;

	/// <summary>Depois de quanto sem pacote a fonte e desmontada. O mesmo prazo (e o mesmo motivo) do <see cref="VozOuvida"/>.</summary>
	private const ulong MsAteDesmontar = 10_000;

	/// <summary>
	/// DEPOIS DE QUANTO SEM PACOTE A TRANSMISSAO E DADA COMO FECHADA, em ms -- e o estalo de fechar toca.
	///
	/// 300: acima do maior alvo da fila de jitter (200 ms), pra um engasgo de rede que ela ainda cobre nao
	/// virar um "fechou e abriu" no meio da frase. Quem solta a tecla para de mandar na hora; o estalo vem
	/// um terco de segundo depois da ultima silaba.
	/// </summary>
	public const ulong MsAteFechar = 300;

	/// <summary>Uma pessoa que esta no radio comigo: a fila dela, o tocador dela, o efeito dela.</summary>
	private sealed class Fonte
	{
		public required int Id;
		public required AudioStreamPlayer Tocador;
		public FilaDeJitter Fila = null!;
		public AudioStreamGeneratorPlayback? Gerador;
		public int Capacidade;
		public readonly EfeitoDeRadio Efeito = new();

		/// <summary>A transmissao esta aberta: ja tocou voz e ainda nao fechou. E o que arma os dois estalos.</summary>
		public bool NoAr;
		public ulong UltimoPacoteMs, UltimoQuadroMs;
	}

	/// <summary>
	/// O QUE FOI PRO GERADOR -- so bancada, nula em jogo: (quem, o quadro ESTEREO ja com o efeito, quantas
	/// amostras, se e o estalo de fechar). E o sinal exato que a placa vai tocar; a pre-carga de silencio nao
	/// passa aqui. Mesma razao do <see cref="VozOuvida.Espiao"/>: voz nao se fotografa.
	/// </summary>
	public static Action<int, Vector2[], int, bool>? Espiao;

	private readonly Dictionary<int, Fonte> _fontes = [];
	private readonly Vector2[] _saida = new Vector2[VozLocal.AmostrasPorQuadroDeSaida];
	private readonly Vector2[] _saidaMuda = new Vector2[VozLocal.AmostrasPorQuadroDeSaida];
	private readonly short[] _estalo = new short[EfeitoDeRadio.AmostrasDoEstaloDeFechar];
	private readonly Vector2[] _estaloEstereo = new Vector2[EfeitoDeRadio.AmostrasDoEstaloDeFechar];

	private PanelContainer _faixa = null!;
	private Label _texto = null!;

	public override void _Ready()
	{
		Instancia = this;
		ProcessMode = ProcessModeEnum.Always;
		MontarAFaixa();
		// METODO NOMEADO e `-=` na saida: o `GameClient` sobrevive ao logout e este no nao.
		if (GameClient.Instance is { } cli) cli.VozDeRadioRecebida += Receber;
	}

	public override void _ExitTree()
	{
		if (Instancia == this) Instancia = null;
		if (GameClient.Instance is { } cli) cli.VozDeRadioRecebida -= Receber;
		_fontes.Clear();
	}

	/// <summary>
	/// ESTA PESSOA ESTA NO RADIO AGORA (e eu a ouco)? Pelo quadro TOCADO, como o <see cref="VozOuvida.Falando"/>:
	/// nao ha pacote "fulano esta transmitindo", e nao pode haver -- quem nao tem direito de ouvir nao pode
	/// nem saber que ha alguem na frequencia.
	/// </summary>
	public bool Falando(int id) =>
		_fontes.TryGetValue(id, out Fonte? f) && Time.GetTicksMsec() - f.UltimoQuadroMs <= VozLocal.MsAteCalar;

	// =====================================================================
	// CHEGOU UM QUADRO
	// =====================================================================
	public void Receber(int id, ushort seq, byte[] dados, int n)
	{
		if (n <= 0 || n > VozLocal.MaxBytesDeQuadro) return;

		Fonte f = _fontes.TryGetValue(id, out Fonte? achada) ? achada : Nascer(id);
		ulong agora = Time.GetTicksMsec();
		f.UltimoPacoteMs = agora;

		// O TOCADOR FICA TOCANDO ENQUANTO A FONTE VIVER, pelo motivo escrito no `VozOuvida.Receber`.
		if (!f.Tocador.Playing)
		{
			f.Tocador.Play();
			f.Gerador = f.Tocador.GetStreamPlayback() as AudioStreamGeneratorPlayback;
			f.Capacidade = f.Gerador?.GetFramesAvailable() ?? 0;
		}
		if (f.Gerador == null) return;

		f.Fila.Receber(seq, dados.AsSpan(0, n), 0, false, (long)agora);
	}

	private Fonte Nascer(int id)
	{
		// SEM BIBLIOTECA NATIVA -- ver o mesmo bloco em `Microfone.CriarCodificador`.
		OpusCodecFactory.AttemptToUseNativeLibrary = false;

		// `AudioStreamPlayer` E NAO O `2D`: radio nao tem lugar no mapa, e o lado em que ele toca e escrito
		// no quadro (ver `Entregar`). No barramento da VOZ: o controle de volume de "gente falando" e um so.
		var tocador = new AudioStreamPlayer
		{
			Name = $"Radio{id}",
			Bus = AudioDirector.BusVoz,
			Stream = new AudioStreamGenerator { MixRate = VozLocal.TaxaDeSaida, BufferLength = SegundosDeFila },
		};
		AddChild(tocador);

		var f = new Fonte { Id = id, Tocador = tocador };
		f.Fila = new FilaDeJitter(OpusCodecFactory.CreateDecoder(VozLocal.TaxaDeSaida, 1),
			(pcm, tipo, _, _, _) => Entregar(f, pcm, tipo));
		_fontes[id] = f;
		return f;
	}

	/// <summary>
	/// A FILA DECIDIU QUE E A HORA DESTE QUADRO: ele ganha o efeito, vai SO pro canal esquerdo e entra no
	/// gerador. O primeiro quadro de voz de uma transmissao leva o estalo de abrir.
	/// </summary>
	private void Entregar(Fonte f, short[] pcm, FilaDeJitter.Tipo tipo)
	{
		if (f.Gerador == null) return;

		if (tipo == FilaDeJitter.Tipo.Silencio)
		{
			f.Gerador.PushBuffer(_saidaMuda);
			return;
		}

		if (!f.NoAr)
		{
			f.NoAr = true;
			f.Efeito.Abrir();
		}
		f.Efeito.Aplicar(pcm, VozLocal.AmostrasPorQuadroDeSaida);
		EfeitoDeRadio.ParaOOuvidoEsquerdo(pcm, VozLocal.AmostrasPorQuadroDeSaida, _saida);
		f.Gerador.PushBuffer(_saida);
		f.UltimoQuadroMs = Time.GetTicksMsec();

		Espiao?.Invoke(f.Id, _saida, VozLocal.AmostrasPorQuadroDeSaida, false);
	}

	// =====================================================================
	// O RELOGIO DO GERADOR, O FECHAMENTO E A FAIXA
	// =====================================================================
	public override void _Process(double delta)
	{
		AvisarSeATeclaNaoPode();
		EstalarSeEuMudei();

		ulong agora = Time.GetTicksMsec();
		List<int>? mortas = null;
		foreach ((int id, Fonte f) in _fontes)
		{
			if (f.Gerador != null)
			{
				int livres = f.Gerador.GetFramesAvailable();
				f.Fila.Puxar(f.Capacidade - livres, livres, (long)agora);
				if (f.NoAr && agora - f.UltimoPacoteMs > MsAteFechar && f.Fila.Pendentes == 0) Fechar(f);
			}
			if (agora - f.UltimoPacoteMs > MsAteDesmontar) (mortas ??= []).Add(id);
		}
		if (mortas != null)
			foreach (int id in mortas)
			{
				if (!_fontes.Remove(id, out Fonte? f)) continue;
				f.Tocador.Stop();
				f.Tocador.QueueFree();
			}

		EscreverAFaixa();
	}

	/// <summary>A transmissao desta pessoa fechou: o estalo de quem soltou o botao, no mesmo ouvido. Se nao couber inteiro no gerador, nao toca.</summary>
	private void Fechar(Fonte f)
	{
		f.NoAr = false;
		if (f.Gerador == null || f.Gerador.GetFramesAvailable() < _estalo.Length) return;

		f.Efeito.Fechar(_estalo, _estalo.Length);
		EfeitoDeRadio.ParaOOuvidoEsquerdo(_estalo, _estalo.Length, _estaloEstereo);
		f.Gerador.PushBuffer(_estaloEstereo);
		Espiao?.Invoke(f.Id, _estaloEstereo, _estalo.Length, true);
	}

	// =====================================================================
	// O ESTALO DO MEU PROPRIO APARELHO
	// =====================================================================
	/// <summary>
	/// QUEM TRANSMITE TAMBEM OUVE O APARELHO ESTALAR: um estalo curto quando o canal abre (a tecla desceu, com
	/// scouter) e o de fechar quando ela sobe. E o retorno de ouvido de que a tecla pegou -- a faixa do alto da
	/// tela diz o mesmo pro olho, mas quem segura um botao pra falar esta olhando pra luta, nao pra faixa.
	///
	/// No mesmo ouvido esquerdo, pela mesma estatica (<see cref="EfeitoDeRadio"/>) e um pouco mais baixo que a
	/// voz dos outros (<see cref="DbDoEstaloProprio"/>): e aviso, nao conversa. Nao passa pelo <see cref="Espiao"/>
	/// (que e da voz ALHEIA); a bancada o conta em <see cref="EstalosPropriosDeTeste"/>.
	/// </summary>
	private void EstalarSeEuMudei()
	{
		bool agora = Microfone.NoRadio;
		if (agora == _euNoRadio) return;
		_euNoRadio = agora;

		if (_proprio == null)
		{
			_proprio = new AudioStreamPlayer
			{
				Name = "EstaloProprio",
				Bus = AudioDirector.BusVoz,
				VolumeDb = DbDoEstaloProprio,
				Stream = new AudioStreamGenerator { MixRate = VozLocal.TaxaDeSaida, BufferLength = SegundosDeFila },
			};
			AddChild(_proprio);
		}
		if (!_proprio.Playing) _proprio.Play();
		if (_proprio.GetStreamPlayback() is not AudioStreamGeneratorPlayback gerador) return;

		short[] mono = agora ? _abrir : _estalo;
		Vector2[] estereo = agora ? _abrirEstereo : _estaloEstereo;
		if (gerador.GetFramesAvailable() < mono.Length) return;

		if (agora)
		{
			Array.Clear(mono);
			_efeitoProprio.Abrir();
			_efeitoProprio.Aplicar(mono, mono.Length);
		}
		else _efeitoProprio.Fechar(mono, mono.Length);

		EfeitoDeRadio.ParaOOuvidoEsquerdo(mono, mono.Length, estereo);
		gerador.PushBuffer(estereo);
		EstalosPropriosDeTeste++;
	}

	/// <summary>Quanto o estalo do MEU aparelho toca abaixo da voz dos outros, em dB.</summary>
	public const float DbDoEstaloProprio = -6f;

	private AudioStreamPlayer? _proprio;
	private readonly EfeitoDeRadio _efeitoProprio = new(comChiado: false);
	private bool _euNoRadio;

	/// <summary>O estalo de ABRIR do meu aparelho: os 25 ms dele e mais um resto pra o filtro assentar em silencio.</summary>
	private readonly short[] _abrir = new short[VozLocal.TaxaDeSaida * (EfeitoDeRadio.MsDoEstaloDeAbrir + 10) / 1000];
	private readonly Vector2[] _abrirEstereo = new Vector2[VozLocal.TaxaDeSaida * (EfeitoDeRadio.MsDoEstaloDeAbrir + 10) / 1000];

	/// <summary>Quantos estalos o MEU aparelho ja deu (um ao abrir, um ao fechar). So a bancada le.</summary>
	public int EstalosPropriosDeTeste { get; private set; }

	// =====================================================================
	// A TECLA QUE NAO PODE TRANSMITIR DIZ POR QUE
	// =====================================================================
	/// <summary>
	/// UMA linha por aperto, e so quando a tecla do radio nao vai fazer nada: sem scouter ligado, ou com o
	/// microfone desligado nas opcoes. Sem isto a tecla e muda, e o jogador nao tem como saber se falta o
	/// aparelho, a opcao ou a frequencia. (O servidor recusa em silencio de proposito: um aviso por quadro
	/// seriam 50 linhas de chat por segundo.)
	/// </summary>
	private static void AvisarSeATeclaNaoPode()
	{
		if (Foco.AtalhosMudos || GameClient.Instance is not { Connected: true, LocalId: not 0 } cli) return;
		if (!Godot.Input.IsActionJustPressed(Teclas.NomeNoMotor("falar_radio"))) return;

		if (!cli.Atributos.Tem(Protocol.Poder.Scouter))
			Chat.Sistema("o rádio é do scouter: ligue um (menu do jogo, aba Equip) pra transmitir.");
		else if (!Boot.Config.VozLigada)
			Chat.Sistema("o microfone está desligado nas opções (pausa, seção \"Voz local\"): sem ele o rádio só escuta.");
	}

	// =====================================================================
	// A FAIXA: QUEM ESTA NO RADIO
	// =====================================================================
	/// <summary>
	/// A FAIXA DO RADIO, no alto da tela, ao lado do painel de vida. So existe enquanto ha radio no ar:
	/// "RÁDIO 042 · transmitindo" pra quem segura a tecla, "RÁDIO 042 · ouvindo Fulano" pra quem recebe.
	///
	/// Camada propria (e nao um filho do `Hud`): este no nasce e morre com o mundo, e a faixa com ele.
	/// </summary>
	private void MontarAFaixa()
	{
		var camada = new CanvasLayer { Name = "FaixaDoRadio", Layer = 1 };
		AddChild(camada);

		var raiz = new Control { AnchorRight = 1, AnchorBottom = 1, MouseFilter = Control.MouseFilterEnum.Ignore };
		Tema.Aplicar(raiz);
		camada.AddChild(raiz);

		_faixa = Tema.Painel1(6);
		// A DIREITA DO PAINEL DE VIDA, na altura do topo dele. O lugar exato e medido quando a faixa aparece
		// (`EscreverAFaixa`): aquele painel cresce com o nome do personagem.
		_faixa.OffsetLeft = FimDoPainelDeVida() + RespiroDaFaixa;
		_faixa.OffsetTop = 14;
		_faixa.MouseFilter = Control.MouseFilterEnum.Ignore;
		_faixa.Visible = false;
		raiz.AddChild(_faixa);

		_texto = new Label { MouseFilter = Control.MouseFilterEnum.Ignore };
		_texto.AddThemeFontSizeOverride("font_size", 13);
		_texto.AddThemeColorOverride("font_color", CorDoRadio);
		_faixa.AddChild(_texto);
	}

	/// <summary>O verde do radio: a cor da faixa e do sinal sobre a cabeca de quem transmite. (No DM o texto de radio tambem e verde: `.radio {color: #008000}`, `client.dm:249`.)</summary>
	public static readonly Color CorDoRadio = new(0.42f, 0.93f, 0.52f);

	/// <summary>O que a faixa diz agora ("" = fora da tela). Pura: a bancada a chama com o que quiser.</summary>
	public static string TextoDaFaixa(int frequencia, bool transmitindo, IReadOnlyList<string> ouvindo)
	{
		if (!transmitindo && ouvindo.Count == 0) return "";
		string t = $"RÁDIO {frequencia:000}";
		if (transmitindo) t += "  ·  transmitindo";
		if (ouvindo.Count > 0) t += "  ·  ouvindo " + string.Join(", ", ouvindo);
		return t;
	}

	private readonly List<string> _ouvindo = [];

	/// <summary>
	/// O que a faixa mostrava quando foi escrita pela ultima vez, resumido num numero: a frequencia, se eu
	/// transmito, e quem esta falando (com o nome que eu sei de cada um -- ele pode chegar depois da voz).
	/// O texto so e remontado quando isto muda: com alguem no radio, remontar a cada quadro seria uma
	/// string nova 60 vezes por segundo pra escrever a mesma coisa.
	/// </summary>
	private long _assinaturaDaFaixa = -1;

	private void EscreverAFaixa()
	{
		ulong agora = Time.GetTicksMsec();
		int frequencia = GameClient.Instance?.FrequenciaDoRadio ?? RadioDoScouter.FrequenciaPadrao;

		// A BEIRADA DO PAINEL DE VIDA ENTRA NA ASSINATURA: o jogador pode aumentar o painel com a faixa na tela
		// (`Hud.Escala.cs`), e ela tem de andar junto -- sem isto so andaria quando outra pessoa falasse.
		float beirada = FimDoPainelDeVida();
		long assinatura = (frequencia * 2L + (Microfone.NoRadio ? 1 : 0)) * 8191 + (long)(beirada * 4f);
		foreach ((int id, Fonte f) in _fontes)
		{
			if (agora - f.UltimoQuadroMs > VozLocal.MsAteCalar) continue;
			assinatura = (assinatura * 31 + id) * 31 + (QuemE?.Invoke(id) ?? "").GetHashCode();
		}
		if (assinatura == _assinaturaDaFaixa) return;
		_assinaturaDaFaixa = assinatura;

		_ouvindo.Clear();
		foreach ((int id, Fonte f) in _fontes)
		{
			if (agora - f.UltimoQuadroMs > VozLocal.MsAteCalar) continue;
			string nome = QuemE?.Invoke(id) ?? "";
			_ouvindo.Add(nome.Length > 0 ? nome : "alguém");
		}

		_texto.Text = TextoDaFaixa(frequencia, Microfone.NoRadio, _ouvindo);
		_faixa.Visible = _texto.Text.Length > 0;
		if (_faixa.Visible) _faixa.OffsetLeft = beirada + RespiroDaFaixa;
	}

	private const float RespiroDaFaixa = 12f;

	/// <summary>
	/// DEFEITO INJETADO (bancada --diagjanelas): a faixa mede o painel de vida pelo tamanho dele a 100%, sem a
	/// escala que o jogador escolheu -- com o painel aumentado ela fica por cima dele. Falso em jogo, sempre.
	/// </summary>
	public static bool SemAEscalaDoPainelDeTeste;

	/// <summary>
	/// Onde o painel de vida do HUD termina, em pixels de tela: ele cresce com o nome do personagem -- uma posicao
	/// cravada poria a faixa em cima de um nome comprido -- e com a ESCALA que o jogador escolheu pra ele
	/// (`Hud.Escala.cs`). E o retangulo dele NA TELA, e nao posicao mais tamanho: o tamanho de um painel escalado
	/// continua sendo o de 100%. Sem HUD (bancada sem mundo), a beirada de fabrica dele: 14 de margem + 268.
	/// </summary>
	private static float FimDoPainelDeVida()
	{
		if (SemAEscalaDoPainelDeTeste)
			return Hud.Instancia?.GetChildOrNull<Control>(0)?.GetChildOrNull<Control>(0) is { } painel
				? painel.Position.X + painel.Size.X
				: 282f;
		return Hud.RetanguloDoStatus is { } r ? r.End.X : 282f;
	}

	/// <summary>Onde a faixa comeca (a beirada de verdade do painel de vida mais o respiro), em px de tela. So a bancada le.</summary>
	public static float EsquerdaDaFaixaDeTeste => FimDoPainelDeVida() + RespiroDaFaixa;

	/// <summary>O texto que esta na faixa agora ("" = escondida). So a bancada le.</summary>
	public string FaixaDeTeste => _faixa.Visible ? _texto.Text : "";
}
