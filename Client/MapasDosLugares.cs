using System.Diagnostics;
using System.Threading.Tasks;
using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// **A IMAGEM DO MAPA DE CADA LUGAR** -- montada UMA vez por zona, fora do quadro, e guardada.
///
/// O dono (2026-10-10): *"adicione na aba world o mapa do mundo atual q vc ta pra vc saber onde ta no
/// mundo e suas coordenadas"*. A regra da cor e a composicao moram no Core (`MapaDoLugar`); aqui mora so
/// o QUANDO e o ONDE.
///
/// ============================ POR QUE UMA THREAD, E O QUE FICA NO QUADRO ============================
/// Um planeta desenhado tem 250 mil celulas e um gerado chega a um milhao: compor o mapa custa de 8 a 40
/// ms (medido pela bancada `mapa` do pipeline), e isso dentro de um quadro e um engasgo ao abrir a aba. As
/// entradas sao todas IMUTAVEIS -- o `.pedacos` ja lido, o terreno gerado, os bitsets do arquivo da
/// colisao -- e a conta e C# puro, sem uma chamada ao motor: vai inteira pra uma thread de trabalho
/// (`Task.Run`). No quadro fica so o que TEM de ser dele: entregar a imagem pronta a placa de video
/// (meio megabyte a um megabyte; o custo sai no log, `[mapa] ... subir N ms`).
///
/// Os dois catalogos que a conta consulta (`tiles.json` e `tiles-cores.json`) sao LIDOS na thread
/// principal (o `res://` e do motor) e DESMONTADOS na de trabalho, uma vez por processo.
///
/// SO QUANDO ALGUEM OLHA: nada disto roda ao entrar na zona. Quem pede e o painel da aba World, quando
/// aparece; quem nunca abre a aba nao paga nada. O painel mostra "desenhando o mapa" pelos poucos quadros
/// que a thread leva.
///
/// O MAPA E A CARTA DO LUGAR COMO ELE NASCEU: cratera, obra e base erguida depois nao entram -- a imagem
/// nao e refeita quando o chao muda. E de proposito que ele nao mostre o que os outros construiram.
/// ====================================================================================================
/// </summary>
public static class MapasDosLugares
{
	public enum Estado
	{
		/// <summary>Este lugar nao tem mapa -- ver <see cref="Mapa.Aviso"/>.</summary>
		SemMapa,
		/// <summary>A thread de trabalho esta compondo a imagem.</summary>
		Montando,
		/// <summary>A textura esta pronta.</summary>
		Pronto,
	}

	/// <summary>O mapa de UM lugar, no estado em que esta.</summary>
	public sealed class Mapa
	{
		public Estado Estado { get; internal set; }

		/// <summary>Por que nao ha mapa aqui, na frase que o painel escreve. Vazio fora de <see cref="Estado.SemMapa"/>.</summary>
		public string Aviso { get; internal set; } = "";

		/// <summary>O tamanho do lugar em tiles. 0 = este lugar nao tem tamanho (o espaco).</summary>
		public int LarguraEmTiles { get; internal set; }
		public int AlturaEmTiles { get; internal set; }

		public ImageTexture? Textura { get; internal set; }

		/// <summary>Quantos tiles de lado cabem num pixel da <see cref="Textura"/> (1 ate 512 tiles; 2 num mundo de 1000).</summary>
		public int Bloco { get; internal set; } = 1;

		/// <summary>Quanto a thread levou pra compor, e quanto a thread principal levou pra entregar a imagem a placa.</summary>
		public double MsDeMontar { get; internal set; }
		public double MsDeSubir { get; internal set; }

		/// <summary>Celulas pintadas com a cor de reserva (tile sem cor publicada). 0 no jogo em dia.</summary>
		public int NaReserva { get; internal set; }

		internal Task<(ImagemDoLugar Imagem, double Ms)>? Tarefa;
		internal string Nome = "";
	}

	/// <summary>Quantos mapas ficam guardados. Cada um e meio megabyte a um megabyte de textura; seis cobrem ir e voltar entre os mundos de uma sessao.</summary>
	private const int Teto = 6;

	private const string CaminhoDosTiles = "res://Assets/Data/tiles.json";
	private const string CaminhoDasCores = "res://Assets/Data/tiles-cores.json";

	private static readonly Dictionary<ulong, Mapa> PorZona = [];
	private static readonly List<ulong> Ordem = [];

	/// <summary>Quantas imagens este processo ja COMECOU a compor. So pras bancadas: abrir a aba dez vezes no mesmo lugar tem de contar uma.</summary>
	public static int MontadosDeTeste { get; private set; }

	/// <summary>DEFEITO INJETADO (bancada `--diagmapa`): a imagem e composta NA THREAD PRINCIPAL, dentro do quadro em que a aba pede -- o engasgo que a thread existe pra tirar.</summary>
	public static bool NoQuadroDeTeste;

	/// <summary>Esquece tudo o que esta guardado. So pras bancadas (o contra-exemplo do cache: depois disto, a proxima abertura compoe de novo).</summary>
	public static void EsquecerDeTeste()
	{
		PorZona.Clear();
		Ordem.Clear();
	}

	// =====================================================================
	// OS CATALOGOS
	// =====================================================================
	private static string? _textoDosTiles, _textoDasCores;

	private static readonly Lazy<(CatalogoDeTiles Tiles, CoresDeTiles Cores, CoresPorFonte PorFonte)> Catalogos = new(() =>
	{
		CatalogoDeTiles tiles = CatalogoDeTiles.Parse(_textoDosTiles ?? "");
		CoresDeTiles cores = CoresDeTiles.Parse(_textoDasCores ?? "");
		return (tiles, cores, CoresPorFonte.Montar(tiles, cores));
	}, LazyThreadSafetyMode.ExecutionAndPublication);

	private static bool _avisouDasCores;

	/// <summary>Le os dois textos, na thread principal, uma vez. Arquivo ausente vira texto vazio: o mapa sai nas cores de reserva.</summary>
	private static void LerOsTextos()
	{
		if (_textoDosTiles != null) return;
		_textoDosTiles = Godot.FileAccess.FileExists(CaminhoDosTiles) ? Godot.FileAccess.GetFileAsString(CaminhoDosTiles) : "";
		_textoDasCores = Godot.FileAccess.FileExists(CaminhoDasCores) ? Godot.FileAccess.GetFileAsString(CaminhoDasCores) : "";
		if (_textoDasCores.Length == 0)
			GD.PushWarning($"[mapa] sem '{CaminhoDasCores}' -- rode o comando 'cores' do Tools/AssetPipeline. O mapa sai nas cores de reserva.");
	}

	// =====================================================================
	// O PEDIDO
	// =====================================================================
	/// <summary>
	/// O mapa do lugar em que estou. Chamado a cada quadro pelo painel, e barato: com a imagem guardada e
	/// uma consulta de dicionario. Nulo = a cena ainda esta trocando de zona (pergunte de novo).
	/// </summary>
	public static Mapa? Do(World mundo, GameClient cli)
	{
		(ZoneKey zona, Node2D? raiz) = mundo.LugarMontado;
		if (zona.Name.Length == 0 || zona.Hash != cli.Zone.Hash) return null;

		if (PorZona.TryGetValue(zona.Hash, out Mapa? guardado))
		{
			if (guardado.Estado == Estado.Montando) Colher(guardado);
			return guardado;
		}

		if (Comecar(zona, raiz, mundo, cli) is not { } novo) return null;

		PorZona[zona.Hash] = novo;
		Ordem.Add(zona.Hash);
		while (Ordem.Count > Teto)
		{
			ulong velha = Ordem[0];
			Ordem.RemoveAt(0);
			if (PorZona.Remove(velha, out Mapa? saiu)) saiu.Textura?.Dispose();
		}
		if (novo.Estado == Estado.Montando) Colher(novo);
		return novo;
	}

	private static Mapa SemMapa(string aviso, int w = 0, int h = 0) =>
		new() { Estado = Estado.SemMapa, Aviso = aviso, LarguraEmTiles = w, AlturaEmTiles = h };

	/// <summary>Decide o que este lugar tem de mapa e, havendo, poe a composicao pra rodar. Nulo = ainda nao da pra saber.</summary>
	private static Mapa? Comecar(ZoneKey zona, Node2D? raiz, World mundo, GameClient cli)
	{
		if (Espaco.EhEspaco(zona))
			return SemMapa("Sem mapa aqui: o espaço não cabe num. Quem mostra os mundos em volta é a carta estelar (aba Nav, com o Nav System).");

		Func<ImagemDoLugar> compor;
		int largura, altura;
		switch (raiz)
		{
			case PlanetaProcedural { Terreno: null }:
				return null;   // o terreno ainda nao nasceu

			case PlanetaProcedural { Terreno: { } t }:
				// LUGAR SEM BEIRADA E SEM DESENHO (a mente): a chapa e um quadrado branco, e o lugar continua
				// pra sempre em volta dela. Nao ha o que mapear -- as coordenadas bastam.
				if (t.Colisao.SemBorda)
					return SemMapa("Sem mapa aqui: este lugar não tem fim, nem nada que caiba num mapa.");
				largura = t.Largura;
				altura = t.Altura;
				compor = () => MapaDoLugar.DoTerreno(t, Catalogos.Value.Tiles, Catalogos.Value.Cores);
				break;

			case PlanetaPreFeito { Pedacos: { } dados } when mundo.TamanhoDoPreFeito is { } tam:
				largura = tam.Largura;
				altura = tam.Altura;
				// A COLISAO SO ENTRA PELOS BITSETS DO ARQUIVO (agua e parede, pra cor de reserva): eles nao
				// mudam depois da carga, e por isso podem ser lidos de outra thread.
				ZoneCollision? planos = mundo.Colisao;
				compor = () => MapaDoLugar.DoPreFeito(dados, largura, altura, Catalogos.Value.PorFonte, planos);
				break;

			default:
				return SemMapa("Sem mapa aqui: o chão deste lugar não tem desenho pra mostrar.");
		}

		LerOsTextos();
		MontadosDeTeste++;
		var novo = new Mapa { Estado = Estado.Montando, LarguraEmTiles = largura, AlturaEmTiles = altura, Nome = cli.NomeDaZona(zona) };

		(ImagemDoLugar, double) Rodar()
		{
			var relogio = Stopwatch.StartNew();
			ImagemDoLugar imagem = compor();
			return (imagem, relogio.Elapsed.TotalMilliseconds);
		}
		novo.Tarefa = NoQuadroDeTeste ? Task.FromResult(Rodar()) : Task.Run(Rodar);
		return novo;
	}

	/// <summary>A thread acabou? Entao a imagem vira textura -- aqui, na thread principal, que e a unica que fala com a placa.</summary>
	private static void Colher(Mapa m)
	{
		if (m.Tarefa is not { IsCompleted: true } tarefa) return;
		m.Tarefa = null;

		if (tarefa.IsFaulted)
		{
			m.Estado = Estado.SemMapa;
			m.Aviso = "Sem mapa aqui: não deu pra desenhar o chão deste lugar.";
			GD.PushWarning($"[mapa] {m.Nome}: a composicao falhou -- {tarefa.Exception?.InnerException?.Message}");
			return;
		}

		(ImagemDoLugar imagem, double ms) = tarefa.Result;
		var relogio = Stopwatch.StartNew();
		using Image bruta = Image.CreateFromData(imagem.Largura, imagem.Altura, false, Image.Format.Rgba8, imagem.Rgba);
		m.Textura = ImageTexture.CreateFromImage(bruta);
		m.MsDeSubir = relogio.Elapsed.TotalMilliseconds;
		m.MsDeMontar = ms;
		m.NaReserva = imagem.NaReserva;
		m.Bloco = imagem.Bloco;
		m.Estado = Estado.Pronto;

		GD.Print($"[mapa] {m.Nome}: {imagem.LarguraEmTiles}x{imagem.AlturaEmTiles} tiles -> {imagem.Largura}x{imagem.Altura} px"
				 + $" | compor {ms:0.0} ms ({(NoQuadroDeTeste ? "NO QUADRO: defeito de bancada" : "fora do quadro")}) | subir {m.MsDeSubir:0.00} ms"
				 + (imagem.NaReserva > 0 ? $" | {imagem.NaReserva} celula(s) na cor de RESERVA" : ""));

		if (!_avisouDasCores && Catalogos.Value.PorFonte.SemCor.Count > 0)
		{
			_avisouDasCores = true;
			GD.PushWarning($"[mapa] {Catalogos.Value.PorFonte.SemCor.Count} folha(s) do tileset sem cor publicada "
						 + $"({string.Join(", ", Catalogos.Value.PorFonte.SemCor.Take(6))}...) -- rode o comando 'cores' do Tools/AssetPipeline.");
		}
	}
}
