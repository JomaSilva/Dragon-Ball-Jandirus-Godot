using Godot;
using Jandirus.Core.Lingua;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ A LINGUA DO JOGO NESTA MAQUINA (2026-10-22) ============================
/// O dono: *"adicionar uma opçao de mudar a lingua do jogo pra ingles, mudando toda a interface do jogo pra ingles,
/// mensagens q o jogador fala automaticamente tb seriam traduzidas pro ingles, entao mesmo q um personagem q esta com
/// jogo em portugues fale frases automatizadas por raiva ou transformaçao, faça com q o jogo saiba quando é
/// automatizada pra traduzir pro ingles na hora"*.
///
/// E PREFERENCIA DA MAQUINA, como o volume: mora no `config.json` (<see cref="Settings.Lingua"/>) e nao vai ao
/// servidor. O servidor nem precisa saber: tudo o que ele manda continua em portugues, e QUEM LE E QUE TRADUZ. E por
/// isso que o grito de quem joga em portugues chega em ingles a quem joga em ingles -- a frase viaja igual pros dois,
/// e cada tela a le na sua lingua.
///
/// TRES PORTAS POR ONDE O PORTUGUES CHEGA NA TELA, e as tres passam pelo mesmo <see cref="Tradutor"/> do Core:
///   1. O TEXTO DE TODO CONTROLE (Label, Button, dica, item de lista, titulo de janela). O Godot ja tem a porta: o
///      `TranslationServer` passa o texto de todo controle por uma `Translation` antes de desenhar, e a
///      <see cref="TraducaoDoJogo"/> e essa `Translation`. Nenhuma tela precisou mudar, e o `Text` do controle
///      CONTINUA em portugues -- os robos que procuram botao pelo rotulo continuam achando o mesmo rotulo.
///   2. A LINHA DO CHAT que o servidor marcou como AUTOMATICA -- o grito da raiva, a fala da transformacao, o emote do
///      golpe, a fala do NPC -- e todo recado do servidor: <see cref="DoServidor"/>. A fala que uma PESSOA digitou
///      nunca e traduzida: a marca vem no fim do `S2C.Chat`, e quem a poe e o servidor, que sabe de onde a frase veio.
///   3. O TEXTO DESENHADO A MAO (o `DrawString` dos mapas, do radar, do painel da chave): <see cref="T"/>.
/// ===================================================================================================
/// </summary>
public static class Idioma
{
	public const string Portugues = "pt";
	public const string Ingles = "en";

	/// <summary>O dicionario. Mora com os outros dados: o `*.json` ja entra no pacote exportado (`export_presets.cfg`).</summary>
	public const string ArquivoDoIngles = "res://Assets/Data/lingua-en.json";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diaglingua`): a traducao NAO e registrada no Godot -- o chat traduz, mas toda tela
	/// fica em portugues. E o jogo de antes desta porta. Lido pelo <see cref="Aplicar"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool TelaSemTraducaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diaglingua`): a fala que uma pessoa DIGITOU tambem passa pelo tradutor -- o "Agora
	/// nao, estou ocupado." escrito por um jogador sairia em ingles na tela do outro. Lido pelo <see cref="DoServidor"/>.
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool TraduzODigitadoDeTeste;

	/// <summary>A lingua desta tela: <see cref="Portugues"/> (a de fabrica) ou <see cref="Ingles"/>.</summary>
	public static string Atual { get; private set; } = Portugues;

	public static bool EmIngles => Atual == Ingles;

	/// <summary>A lingua mudou -- pra quem desenha texto a mao e precisa redesenhar.</summary>
	public static event Action? Mudou;

	private static Tradutor? _tradutor;

	/// <summary>O tradutor, carregado na primeira vez que alguem pede ingles (quem joga em portugues nunca o carrega).</summary>
	public static Tradutor Tradutor => _tradutor ??= Carregar();

	private static TraducaoDoJogo? _traducao;
	private static bool _registrada;
	private static string _localeDeAntes = "";
	private static bool _ganchoNaArvore;

	/// <summary>
	/// POE A TELA NA LINGUA PEDIDA ("pt" ou "en"; qualquer outra coisa e portugues). Chamado no boot, depois do
	/// `Settings.Carregar`, e pela opcao da tela de opcoes -- e vale NA HORA: o Godot manda toda tela aberta se
	/// retraduzir.
	/// </summary>
	public static void Aplicar(string lingua)
	{
		string nova = lingua == Ingles ? Ingles : Portugues;
		bool mudou = nova != Atual;
		Atual = nova;

		if (EmIngles && !TelaSemTraducaoDeTeste)
		{
			_ = Tradutor;
			_traducao ??= new TraducaoDoJogo { Locale = "en" };
			if (!_registrada)
			{
				if (_localeDeAntes.Length == 0) _localeDeAntes = TranslationServer.GetLocale();
				TranslationServer.AddTranslation(_traducao);
				_registrada = true;
			}
			TranslationServer.SetLocale("en");
			GanchoNaArvore();
		}
		else if (_registrada)
		{
			// SO REGISTRADA EM INGLES, e isto nao e enfeite: o Godot procura a traducao tambem na lingua de RESERVA
			// (`internationalization/locale/fallback`, que de fabrica e "en"). Registrada com o jogo em portugues, toda
			// tela sairia em ingles do mesmo jeito.
			TranslationServer.RemoveTranslation(_traducao);
			_registrada = false;
			if (_localeDeAntes.Length > 0) TranslationServer.SetLocale(_localeDeAntes);
		}

		if (!mudou) return;
		// O `SetLocale` ja avisa a arvore quando a lingua do Godot muda -- mas a lingua do Godot pode nao mudar (a
		// maquina ja era "en", ou o defeito de bancada nao registrou nada), e a tela tem de se refazer igual.
		if (Engine.GetMainLoop() is SceneTree arvore) arvore.Root.PropagateNotification((int)Node.NotificationTranslationChanged);
		Mudou?.Invoke();
	}

	/// <summary>
	/// O NO QUE ENTRA NA ARVORE DEPOIS DA TROCA SE TRADUZ TAMBEM. O aviso de "a lingua mudou" so chega a quem ESTA na
	/// arvore; a pagina guardada fora dela (a aba do menu que se remonta reaproveitando nos) voltaria com o texto da
	/// lingua velha. Um aviso por no que entra, so com o jogo em ingles -- e a escuta so nasce na primeira vez que o
	/// ingles e ligado: quem joga em portugues nao paga nem a chamada.
	/// </summary>
	private static void GanchoNaArvore()
	{
		if (_ganchoNaArvore || Engine.GetMainLoop() is not SceneTree arvore) return;
		arvore.NodeAdded += AoEntrarNaArvore;
		_ganchoNaArvore = true;
	}

	private static void AoEntrarNaArvore(Node n)
	{
		if (EmIngles && n is Control or Window) n.Notification((int)Node.NotificationTranslationChanged);
	}

	/// <summary>O texto na lingua desta tela. Pra quem desenha a mao (`DrawString`) e pra linha que o proprio cliente escreve.</summary>
	public static string T(string pt) => EmIngles ? Tradutor.Traduzir(pt) : pt;

	/// <summary>
	/// A LINHA QUE CHEGOU NO `S2C.Chat`, na lingua desta tela. Traduz o recado do servidor (os canais de `Sistema` pra
	/// cima) e a fala que ele marcou como <paramref name="automatica"/>; a fala digitada por gente passa crua -- inclusive
	/// o OOC, que nunca e automatico.
	/// </summary>
	public static string DoServidor(Protocol.Fala canal, string texto, bool automatica)
	{
		if (!EmIngles || texto.Length == 0) return texto;
		bool recado = canal >= Protocol.Fala.Sistema;
		if (!recado && !automatica && !TraduzODigitadoDeTeste) return ComAEtiquetaDaLingua(texto);
		return Tradutor.Traduzir(texto);
	}

	/// <summary>
	/// (2026-10-27) A ETIQUETA DA LINGUA DO SAY (o pedido 118): a fala digitada passa crua, mas o "[língua Saiyajin] " que o servidor poe
	/// na frente dela (`Linguas.Rotulo`) e dele -- e sai na lingua desta tela ("[Saiyan language] ").
	/// </summary>
	private static string ComAEtiquetaDaLingua(string texto)
	{
		int fim = texto.IndexOf("] ", StringComparison.Ordinal);
		if (fim < 0 || !texto.StartsWith("[língua ", StringComparison.Ordinal)) return texto;
		return "[" + Tradutor.Traduzir(texto[1..fim]) + texto[fim..];
	}

	/// <summary>
	/// O AUTOR DA LINHA: so o NPC do mundo tem o nome traduzido ("Lobo", "Soldado Saiyajin", "Rei Vegeta"). O nome de
	/// quem joga e de quem o escolheu, e um jogador chamado "Lobo" continua "Lobo".
	/// </summary>
	public static string AutorDoServidor(string autor, bool deNpc) => EmIngles && deNpc ? Tradutor.Traduzir(autor) : autor;

	private static Tradutor Carregar()
	{
		try
		{
			if (!Godot.FileAccess.FileExists(ArquivoDoIngles))
			{
				GD.PushWarning($"[lingua] {ArquivoDoIngles} nao existe: o jogo fica em portugues");
				return Tradutor.Vazio;
			}
			Tradutor t = Tradutor.DoJson(Godot.FileAccess.GetFileAsString(ArquivoDoIngles));
			GD.Print($"[lingua] ingles carregado: {t.Exatas} frases inteiras, {t.Moldes} moldes ({t.MoldesSemAncora.Count} fora do indice)");
			return t;
		}
		catch (Exception e)
		{
			GD.PushWarning($"[lingua] o dicionario de ingles nao abriu ({e.Message}): o jogo fica em portugues");
			return Tradutor.Vazio;
		}
	}
}
