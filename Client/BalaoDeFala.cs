using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O QUE A PESSOA ACABOU DE DIZER, SOBRE A CABECA DELA.
///
/// ============================ POR QUE ISTO EXISTE ============================
/// O chat conta TUDO e nao diz QUEM. Numa briga de tres, "Zx diz, 'sai da frente'" e uma linha
/// que passa no canto da tela enquanto os olhos estao no meio do mapa -- e quando se olha pro
/// canto, ja tem outras quatro linhas por cima. O balao resolve a unica pergunta que o painel de
/// chat nunca respondeu bem: *de qual daqueles corpos saiu essa frase*.
///
/// O DM NAO TINHA ISTO. O `sayType()` mandava tudo pro painel, com um `\icon[usr]` na LINHA do
/// chat (o boneco desenhado ao lado do texto) -- que e a mesma intencao resolvida do outro lado.
/// O unico balao sobre corpo do original e o `create_bubb()` do Sense (`Sense3.0.dm:92`), e ele
/// mostra NOME, nao fala. Entao aqui e desenho novo, e nao porte.
/// =============================================================================
///
/// ============================ NEM TODO CANAL E DO CORPO ============================
/// OOC e LOOC sao do JOGADOR, nao do personagem -- por baixo do balao esta um boneco que nao
/// disse aquilo. Sistema nem autor tem. O sussurro de longe chega com o texto VAZIO (e o
/// "alguem sussurrou alguma coisa" do painel) e um balao vazio seria pior que nenhum.
/// Ver <see cref="EhDeCorpo"/> -- e o unico portao, e ele e estatico de proposito pra que o
/// chamador nao precise repetir a regra.
/// ==================================================================================
///
/// ============================ SUBSTITUI, COM PISO DE LEITURA ============================
/// Fala nova NAO EMPILHA. Empilhar cresce pra cima sem limite, e o que fica em cima da pilha e
/// justamente o corpo de quem esta atras (o Y-sort poe quem esta mais ao norte atras, e o balao
/// sobe na direcao dele): tres frases seguidas taparaim um personagem inteiro. E o historico ja
/// tem dono -- o painel de chat, com hora e cor, guardando 300 linhas. O balao nao e o registro,
/// e o "quem esta falando AGORA".
///
/// O risco da substituicao pura e a frase que some antes de ser lida: o servidor deixa falar a
/// cada 400 ms (`MsEntreFalas`), entao duas linhas seguidas apagariam a primeira em menos de
/// meio segundo. Por isso ha o PISO (<see cref="PisoDeLeitura"/>): quem chega antes dele espera
/// numa fila curta e entra quando a anterior cumpriu o minimo. A fila guarda no maximo
/// <see cref="MaxNaFila"/>; passando disso, a MAIS VELHA cai (ela ja esta no chat, e o balao
/// deve mostrar o presente, nao o atraso acumulado).
/// ======================================================================================
///
/// ============================ NITIDO EM QUALQUER ZOOM (pedido do dono, 2026-09-06) ============================
/// *"texto nitido (fonte vetorial, sem filtro nearest no texto), balao escala com o texto e
/// quebra linha, pra jogadores e NPCs"*. O balao e filho do corpo e o corpo vive no mundo 2D, que
/// a camera amplia de 2x a 6x com o filtro NEAREST do projeto (o cenario e pixel art). O texto era
/// rasterizado a 8 px de mundo e esticado pela camera: cada pixel do glifo virava um bloco de
/// zoom x zoom, e a letra saia serrilhada -- foi o que o dono viu.
///
/// O CONSERTO NAO TIRA O BALAO DO CORPO. Ele continua filho (anda com o dono, sobe com o voo,
/// some no Zanzoken pelas regras de sempre); o que muda e a ESCALA: o no se encolhe por 1/zoom e
/// desenha tudo em PIXELS DE TELA -- a fonte em `Tamanho x zoom`, a caixa em `LarguraMaxima x
/// zoom`. Um pixel local vira exatamente um pixel da tela, o glifo e rasterizado pela fonte
/// vetorial no tamanho final, e o filtro do no e LINEAR (o nearest do projeto e do cenario, nao do
/// texto). O tamanho VISTO e o mesmo de antes (8 px de mundo, 24 na tela no zoom 3): escrever num
/// tamanho fixo de tela deixaria a letra minuscula ao lado de um boneco de 192 px no zoom 6.
///
/// Uma camada de tela (`CanvasLayer` + `Label`) daria a mesma nitidez e custaria refazer a mao o
/// "anda com o corpo" e o "sobe com quem voa" -- que a bancada `--diagbalao` mede justamente
/// porque sao as duas coisas que esse conserto quebraria.
/// ================================================================================================
///
/// Desenhado em `_Draw` e nao com nodes de UI: um `Label` dentro do mundo 2D traria tema, layout e
/// ancoragem pra resolver o que um retangulo e um `DrawString` resolvem -- e traria a fonte do
/// TEMA, que e de UI e nao acompanha o zoom do mapa.
/// </summary>
/// <remarks>
/// <see cref="INaoSomeComOCorpo"/>: a fala e ROTULO SOBRE o dono, nao pixel do dono. Quando o
/// corpo e trocado pelas listras do Zanzoken (<see cref="EsquivaZanzoken"/>) o balao continua --
/// no original ele nem era do mob (a fala vai pro chat e as barras do HUD sao `/obj/screen`), e o
/// `flick()` so mexia no `icon`. Uma frase cortada no ar por 0,30 s de esquiva seria o efeito
/// comendo informacao que nao e dele.
/// </remarks>
public partial class BalaoDeFala : Node2D, ISobeComOCorpo, INaoSomeComOCorpo
{
	/// <summary>
	/// A base do balao, em pixels acima da origem do corpo.
	///
	/// O sprite e 32x32 e `Centered`, entao o topo da cabeca esta em -16. -26 deixa dez pixels de
	/// folga sobre ela, e o balao cresce pra CIMA -- nunca pra baixo, pra nao cobrir o proprio dono.
	/// (A folga era maior por causa da barra de vida, que ocupava de -23 a -18; ela foi deletada a
	/// pedido do dono. O valor fica onde esta: mexer nele so pra fechar o vao moveria toda fala do
	/// jogo pra ganhar cinco pixels.)
	/// </summary>
	internal const float AlturaBase = -26f;

	/// <summary>
	/// A largura maxima do texto, em pixels de MUNDO -- pouco mais de tres tiles.
	///
	/// E o que separa um balao de uma FAIXA: sem teto, uma frase de cem letras vira uma linha
	/// unica atravessando a tela inteira, e no zoom do jogo (2x a 6x) ela sai pelas duas bordas.
	/// Na tela ela vale `LarguraMaxima x zoom` pixels -- ver o cabecalho sobre a escala.
	/// </summary>
	internal const float LarguraMaxima = 108f;

	/// <summary>
	/// O tamanho da fonte em pixels de MUNDO. Na tela a fonte e rasterizada em `Tamanho x zoom`
	/// (24 px no zoom 3): o tamanho visto e este, e o glifo nasce nitido no tamanho final em vez
	/// de nascer com 8 px e ser esticado pela camera.
	/// </summary>
	private const int Tamanho = 8;

	/// <summary>
	/// Quantas linhas cabem. Tres e o limite fisico do desenho: a quarta ja passa da altura de um
	/// corpo e comeca a tapar quem esta atras. O que nao coube vira reticencia -- e a frase
	/// inteira esta no chat.
	/// </summary>
	internal const int MaxLinhas = 3;

	/// <summary>Quanto tempo a frase mais curta fica na tela, e quanto cada letra soma.</summary>
	internal const double DuracaoMinima = 2.2, PorLetra = 0.055, DuracaoMaxima = 7.0;

	/// <summary>Os ultimos instantes viram desvanecimento em vez de sumico seco.</summary>
	private const double Desvanecer = 0.35;

	/// <summary>
	/// O MINIMO que uma frase fica antes de ser trocada pela seguinte. Ver o cabecalho.
	/// </summary>
	internal const double PisoDeLeitura = 0.9;

	/// <summary>Quantas frases esperam a vez. Ver o cabecalho sobre por que a mais velha cai.</summary>
	private const int MaxNaFila = 2;

	/// <summary>
	/// O INTERRUPTOR DO DEFEITO INJETADO da bancada: desligado, o balao volta a desenhar em pixels
	/// de mundo (escala 1, fonte de 8 px esticada pela camera) -- a letra serrilhada que o dono viu.
	/// So a `--diagbalao` mexe nisto, pra provar que a foto nitida e nitida POR CAUSA da escala.
	/// </summary>
	public static bool NitidoDeTeste = true;

	private readonly record struct Dita(Protocol.Fala Canal, string Texto);

	private readonly List<Dita> _fila = [];
	private readonly List<string> _linhas = [];
	private Protocol.Fala _canal;
	private string _texto = "";
	private double _restante, _duracao, _naTela;

	/// <summary>A caixa do texto em PIXELS DE TELA (o espaco local do no, ja encolhido por 1/zoom).</summary>
	private Vector2 _caixa;
	private float _alturaLinha = Tamanho + 2;

	/// <summary>O zoom com que as linhas e a caixa de agora foram medidas. Ver <see cref="AjustarAoZoom"/>.</summary>
	private int _zoom = 1;

	/// <summary>
	/// O DESLOCAMENTO DE ALTITUDE, escrito pela varredura do voo (<see cref="SubirComOVoo.Aplicar"/>),
	/// que chega aqui porque esta classe declara <see cref="ISobeComOCorpo"/>.
	///
	/// PROPRIEDADE, e nao `Position` na mao: quem escrevia direto na posicao apagava a
	/// <see cref="AlturaBase"/> junto, e o balao ia parar no umbigo assim que a pessoa subisse --
	/// defeito que ninguem via porque no chao o deslocamento e zero e a conta da no mesmo por
	/// acidente. Este e hoje o UNICO node com altura propria, entao a bancada `--diagbalao` e a
	/// unica guarda que sobrou dessa regra.
	///
	/// A posicao cai na GRADE DE DESENHO (multiplos de 1/zoom de pixel de mundo): e o que poe a
	/// origem do balao num pixel INTEIRO da tela -- fora dela, a nitidez conquistada pela escala
	/// se perderia num meio pixel de deslocamento.
	/// </summary>
	public Vector2 Deslocamento
	{
		set => Position = LocalPlayer.NoPontoDaGrade(new Vector2(0, AlturaBase) + value, World.GradeDeDesenho);
	}

	/// <summary>
	/// ESTE CANAL SAI DA BOCA DE UM CORPO?
	///
	/// Ver o cabecalho da classe. `texto` participa porque o sussurro tem duas formas no fio: com
	/// conteudo pra quem esta colado, e VAZIO pra quem so nota que houve um sussurro.
	/// </summary>
	public static bool EhDeCorpo(Protocol.Fala canal, string texto) => canal switch
	{
		Protocol.Fala.Diz or Protocol.Fala.Emote or Protocol.Fala.Pensa => texto.Length > 0,
		Protocol.Fala.Sussurro => texto.Length > 0,
		_ => false,
	};

	/// <summary>O que esta escrito agora (as linhas emendadas). So a bancada le.</summary>
	public string TextoDeTeste => string.Join(' ', _linhas);
	public int NaFilaDeTeste => _fila.Count;
	public int LinhasDeTeste => _linhas.Count;

	/// <summary>A largura do texto em pixels de MUNDO -- a mesma regua de sempre (`LarguraMaxima`).</summary>
	public float LarguraDeTeste => _caixa.X / _zoom;

	/// <summary>A caixa do texto em pixels de TELA, e o zoom, a escala e a fonte com que ela foi medida.</summary>
	public Vector2 CaixaNaTelaDeTeste => _caixa;
	public int ZoomDeTeste => _zoom;
	public float EscalaDeTeste => Scale.X;
	public int TamanhoDaFonteDeTeste => Tamanho * _zoom;

	/// <summary>
	/// O RETANGULO DO TEXTO em pixels de TELA, relativo a origem do no -- pra bancada recortar a
	/// foto exatamente onde as letras estao (sem a moldura e sem o rabicho).
	/// </summary>
	public Rect2 TextoNaTelaDeTeste
	{
		get
		{
			float folga = 3f * _zoom, rabo = 4f * _zoom;
			return new Rect2(-_caixa.X / 2, -_caixa.Y - folga - rabo, _caixa.X, _caixa.Y);
		}
	}

	public override void _Ready()
	{
		Position = new Vector2(0, AlturaBase);
		// Acima de tudo que desenha no corpo (a barra de vida ocupava o 20 antes de ser deletada)
		// pra que alguem passando na frente nao coma o texto, e MUITO abaixo da cinematica (90), que
		// tem que continuar sendo o que domina a tela.
		ZIndex = 21;
		// O FILTRO E DO TEXTO, nao do cenario: o projeto e NEAREST (pixel art), e um glifo vetorial
		// amostrado com nearest serrilha. Ver o cabecalho.
		TextureFilter = TextureFilterEnum.Linear;
		Visible = false;
		// Corpo calado nao gasta quadro. E o no mais instanciado do jogo depois do proprio corpo
		// (um por pessoa em campo) e a esmagadora maioria passa a partida inteira sem dizer nada.
		SetProcess(false);
	}

	/// <summary>
	/// FALA. Quem filtra canal e o chamador, por <see cref="EhDeCorpo"/> -- aqui ja chegou o que
	/// e do corpo.
	/// </summary>
	public void Dizer(Protocol.Fala canal, string texto)
	{
		texto = texto.Trim();
		if (texto.Length == 0) return;

		// A ANTERIOR AINDA NAO CUMPRIU O PISO: entra na fila em vez de apagar uma frase que
		// ninguem teve tempo de ler. Ver o cabecalho.
		if (Visible && _naTela < PisoDeLeitura)
		{
			_fila.Add(new Dita(canal, texto));
			if (_fila.Count > MaxNaFila) _fila.RemoveAt(0);
			return;
		}

		Assumir(new Dita(canal, texto));
	}

	private void Assumir(Dita d)
	{
		_canal = d.Canal;
		_texto = Enfeitar(d.Canal, d.Texto);
		AjustarAoZoom(forcar: true);

		_duracao = Mathf.Clamp(DuracaoMinima + d.Texto.Length * PorLetra, DuracaoMinima, DuracaoMaxima);
		_restante = _duracao;
		_naTela = 0;

		Visible = true;
		SetProcess(true);
		Modulate = Colors.White;
		QueueRedraw();
	}

	/// <summary>
	/// O zoom que a camera esta usando AGORA -- a grade de desenho do mundo, que ja e o zoom
	/// inteiro (2 a 6) ou 1 sem camera. Com o defeito injetado ligado, 1: pixels de mundo.
	/// </summary>
	private static int ZoomDaTela() =>
		NitidoDeTeste ? Math.Max(1, (int)MathF.Round(World.GradeDeDesenho)) : 1;

	/// <summary>
	/// ENCOLHE O NO POR 1/ZOOM E MEDE O TEXTO EM PIXELS DE TELA. Chamado ao assumir uma frase e a
	/// cada quadro em que o balao esta na tela: o zoom muda pelo menu de pausa e pela altitude (a
	/// camera se afasta de quem voa), e um balao ja aberto precisa acompanhar sem esperar a
	/// proxima fala.
	/// </summary>
	private void AjustarAoZoom(bool forcar)
	{
		int z = ZoomDaTela();
		if (!forcar && z == _zoom) return;
		_zoom = z;
		Scale = Vector2.One / _zoom;
		// O defeito injetado e FIEL ao que havia: pixels de mundo com o filtro NEAREST do projeto --
		// cada pixel do glifo de 8 px vira um bloco de zoom x zoom na tela, que e o que a foto mede.
		TextureFilter = NitidoDeTeste ? TextureFilterEnum.Linear : TextureFilterEnum.Nearest;
		Quebrar(_texto);
		QueueRedraw();
	}

	/// <summary>
	/// A FORMA DA FRASE, que e a do `sayType()` menos o nome.
	///
	/// O nome sai porque o balao JA APONTA pra quem falou -- "* Zx cerra os punhos" em cima do Zx
	/// diz o nome dele duas vezes. O painel de chat continua com o nome, porque la ele e a unica
	/// coisa que identifica quem foi.
	/// </summary>
	private static string Enfeitar(Protocol.Fala canal, string texto) => canal switch
	{
		Protocol.Fala.Emote => $"* {texto}",
		Protocol.Fala.Pensa => $"( {texto} )",
		Protocol.Fala.Sussurro => $"“{texto}”",
		_ => texto,
	};

	/// <summary>
	/// A COR DO CANAL, a MESMA do painel de chat (ver `Chat.Formatar`). Duas paletas pra mesma
	/// informacao seria obrigar o jogador a aprender o codigo duas vezes.
	/// </summary>
	private Color CorDoTexto() => _canal switch
	{
		Protocol.Fala.Emote => new Color("e8d06f"),
		Protocol.Fala.Pensa => new Color("9d8fc4"),
		Protocol.Fala.Sussurro => new Color("9aa4bd"),
		// "!" e grito -- a MESMA regra que estica o alcance no servidor e que pinta a linha de
		// laranja no painel.
		_ when _linhas.Exists(l => l.Contains('!')) => new Color("f0a041"),
		_ => Colors.White,
	};

	public override void _Process(double delta)
	{
		AjustarAoZoom(forcar: false);

		_naTela += delta;
		_restante -= delta;

		// ALGUEM ESPERA A VEZ: a frase atual sai assim que cumpre o PISO, sem esperar a duracao
		// inteira. Segurar os 2,2 s de cada uma faria a conversa atrasar mais a cada linha -- o
		// balao mostraria o que foi dito ha cinco segundos enquanto a pessoa ja falou outras tres.
		bool troca = _fila.Count > 0 && _naTela >= PisoDeLeitura;

		if (!troca && _restante > 0)
		{
			// Desvanece no fim. `Modulate` e nao um alfa dentro do `_Draw` porque assim a moldura,
			// o rabicho e o texto somem juntos, sem precisar passar o alfa por cada desenho.
			float a = _restante < Desvanecer ? (float)(_restante / Desvanecer) : 1f;
			Modulate = new Color(1, 1, 1, a);
			return;
		}

		// ACABOU: se alguem esperava a vez, ela entra agora; senao o balao apaga e o no volta a
		// nao gastar quadro.
		if (_fila.Count > 0)
		{
			Dita proxima = _fila[0];
			_fila.RemoveAt(0);
			Assumir(proxima);
			return;
		}

		Visible = false;
		SetProcess(false);
		_linhas.Clear();
	}

	/// <summary>
	/// QUEBRA EM LINHAS, por palavra, ate <see cref="LarguraMaxima"/> -- medida em pixels de TELA,
	/// com a fonte no tamanho da tela (`Tamanho x zoom`), porque e assim que ela vai ser desenhada.
	///
	/// A mao mesmo, e nao o `width` do `DrawString`: aquele parametro alinha e recorta, mas quem
	/// desenha precisa saber a ALTURA final antes de desenhar -- e a moldura, o rabicho e o
	/// empurrao pra cima saem todos do numero de linhas.
	///
	/// Palavra unica maior que a largura (uma URL, um "AAAAAAAAAA") e cortada na forca: o
	/// contrario e voltar a faixa atravessando a tela por causa de uma palavra.
	/// </summary>
	private void Quebrar(string texto)
	{
		Font fonte = ThemeDB.FallbackFont;
		int tam = Tamanho * _zoom;
		float largMax = LarguraMaxima * _zoom;
		_linhas.Clear();

		// ---- 1) NENHUMA PALAVRA PODE SER MAIOR QUE A CAIXA ----
		// As gigantes (um "AAAAAAAAAAAA", um endereco colado) sao cortadas na forca ANTES do
		// preenchimento. Sem isto o laco guloso abaixo receberia uma palavra que nao cabe em
		// linha nenhuma e ficaria abrindo linha vazia atras de linha vazia.
		var palavras = new List<string>();
		foreach (string p in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			string resto = p;
			while (Largura(fonte, resto, tam) > largMax && resto.Length > 1)
			{
				int corte = resto.Length - 1;
				while (corte > 1 && Largura(fonte, resto[..corte], tam) > largMax) corte--;
				palavras.Add(resto[..corte]);
				resto = resto[corte..];
			}
			palavras.Add(resto);
		}

		// ---- 2) PREENCHIMENTO GULOSO, COM TETO DE LINHAS ----
		bool sobrou = false;
		var atual = new System.Text.StringBuilder();
		foreach (string p in palavras)
		{
			string tentativa = atual.Length == 0 ? p : $"{atual} {p}";
			if (Largura(fonte, tentativa, tam) <= largMax)
			{
				atual.Clear().Append(tentativa);
				continue;
			}

			// A linha que esta sendo montada ja e a ULTIMA permitida: o que vem depois nao entra.
			if (_linhas.Count == MaxLinhas - 1) { sobrou = true; break; }

			_linhas.Add(atual.ToString());
			atual.Clear().Append(p);
		}
		if (atual.Length > 0) _linhas.Add(atual.ToString());

		// PASSOU DO TETO: a ultima linha ganha a reticencia. A frase inteira esta no chat -- o
		// balao nunca foi o registro.
		if (sobrou && _linhas.Count > 0) _linhas[^1] = Encurtar(fonte, _linhas[^1], tam, largMax);

		_alturaLinha = fonte.GetHeight(tam);
		float larg = 0;
		foreach (string l in _linhas) larg = MathF.Max(larg, Largura(fonte, l, tam));
		_caixa = new Vector2(larg, _linhas.Count * _alturaLinha);
	}

	private static float Largura(Font f, string s, int tam) =>
		f.GetStringSize(s, HorizontalAlignment.Left, -1, tam).X;

	private static string Encurtar(Font f, string linha, int tam, float largMax)
	{
		while (linha.Length > 1 && Largura(f, linha + "…", tam) > largMax) linha = linha[..^1];
		return linha + "…";
	}

	/// <summary>
	/// A moldura, o rabicho e o texto -- tudo em pixels de TELA (o no ja esta encolhido por
	/// 1/zoom), com folga, rabicho, contorno e traco multiplicados pelo zoom pra ficarem do mesmo
	/// tamanho VISTO de sempre.
	///
	/// ============================ LEGIVEL SOBRE QUALQUER CENARIO ============================
	/// Duas defesas, porque uma so falha em algum lugar do jogo: a MOLDURA escura resolve texto
	/// branco sobre neve e sobre o clarao de uma aura, e o CONTORNO preto do proprio glifo resolve
	/// o que sobra (a moldura e translucida de proposito -- opaca, ela tapa o cenario e o corpo de
	/// quem esta atras, que e justamente o que o balao nao pode fazer).
	/// ====================================================================================
	/// </summary>
	public override void _Draw()
	{
		if (_linhas.Count == 0) return;

		Font fonte = ThemeDB.FallbackFont;
		int tam = Tamanho * _zoom;
		float folga = 3f * _zoom, rabo = 4f * _zoom;

		// O balao cresce PRA CIMA a partir da base (0,0), que ja esta acima da cabeca.
		float altura = _caixa.Y + folga * 2;
		var caixa = new Rect2(-_caixa.X / 2 - folga, -altura - rabo, _caixa.X + folga * 2, altura);

		Color tinta = CorDoTexto();
		DrawRect(caixa, new Color(0.04f, 0.05f, 0.08f, 0.62f));
		DrawRect(caixa, new Color(tinta, 0.30f), filled: false, width: _zoom);

		// O RABICHO. Sem ele, dois corpos colados dao dois retangulos flutuando e some justamente
		// a informacao que o balao existe pra dar: de quem e a frase.
		DrawColoredPolygon(
			[new Vector2(-3 * _zoom, -rabo), new Vector2(3 * _zoom, -rabo), new Vector2(0, 0)],
			new Color(0.04f, 0.05f, 0.08f, 0.62f));

		// A LINHA DE BASE, e nao o topo: `DrawString` desenha o glifo APOIADO no ponto que recebe.
		// Sem somar o `GetAscent` o texto sairia inteiro acima da moldura.
		float y = caixa.Position.Y + folga + fonte.GetAscent(tam);
		foreach (string linha in _linhas)
		{
			var onde = new Vector2(MathF.Round(-Largura(fonte, linha, tam) / 2f), MathF.Round(y));
			DrawStringOutline(fonte, onde, linha, HorizontalAlignment.Left, -1, tam, 3 * _zoom,
							  new Color(0, 0, 0, 0.85f));
			DrawString(fonte, onde, linha, HorizontalAlignment.Left, -1, tam, tinta);
			y += _alturaLinha;
		}
	}
}
