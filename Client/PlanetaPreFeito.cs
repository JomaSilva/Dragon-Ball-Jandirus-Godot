using System;
using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// UM PLANETA DESENHADO A MAO -- a Terra, Namek, Vegeta, o Inferno.
///
/// O chao dele foi convertido do `.dmm` do jogo antigo. Esta classe nao GERA nada; ela existe pra
/// carregar a ficha do lugar (nome, gravidade, tipo) junto do mapa e pra ALIMENTAR o tilemap com
/// os pedacos de cenario que a camera alcanca.
///
/// ============================ O CHAO NAO VEM MAIS DENTRO DO .tscn ============================
/// Vinha, e custava caro: 9,6 MB de celulas em texto na Terra, 659 ms de parse e 708 ms montando o
/// desenho no primeiro quadro. As camadas agora nascem VAZIAS na cena e as celulas moram num
/// `.pedacos` ao lado, em blocos de 64x64 (ver <see cref="PedacosDoMapa"/>). Quem os entrega ao
/// tilemap, conforme a camera anda, e o <see cref="PintorDePedacos"/>.
/// =============================================================================================
///
/// POR QUE UMA SUBCLASSE, e nao so o bool `Procedural = false`: porque as duas coisas divergem no
/// que precisam guardar e no que sabem fazer. Um pre-feito le pedaco de arquivo; um procedural
/// pinta do terreno que ele mesmo gerou. Deixar tudo na mesma classe daria um node com metade dos
/// campos sempre vazios no inspetor -- e campo vazio no inspetor e convite pra alguem preencher e
/// esperar que funcione.
/// </summary>
[GlobalClass]
public partial class PlanetaPreFeito : Planeta
{
	private PintorDePedacos? _pintor;

	public override void _Ready()
	{
		Procedural = false;   // e o que ele E; nao da pra marcar no inspetor e virar outra coisa
		base._Ready();
	}

	/// <summary>
	/// Um pre-feito nao nasce -- ele ja estava la. O ponto de chegada e o que o servidor mandar,
	/// entao aqui nao ha o que fazer, e isso e um comportamento, nao um esquecimento.
	/// </summary>
	protected override void Nascer() { }

	/// <summary>
	/// LIGA O CENARIO. Le o `.pedacos` da zona e monta na hora o que a camera ja alcanca.
	///
	/// Chamado pelo carregador de zona logo depois do `AddChild`, por baixo da tela de
	/// carregamento -- e por isso a primeira leva vai sem orcamento (ver `PintorDePedacos.Urgente`):
	/// o jogador nao pode aparecer sobre o vazio, e nada disso esta na tela ainda.
	///
	/// Idempotente: reentrar numa zona que ficou no cache so repovoa o que a camera quer agora.
	/// </summary>
	public void Semear(string arquivo, Vector2? centro = null, Vector2I? vazioEmVoltaDe = null)
	{
		if (_pintor == null)
		{
			if (arquivo.Length == 0 || !Godot.FileAccess.FileExists(arquivo))
			{
				GD.PushWarning($"[planeta] {Name}: sem '{arquivo}' -- rode o Tools/AssetPipeline. "
							   + "O cenario NAO vai aparecer.");
				return;
			}

			PedacosDoMapa? dados = PedacosDoMapa.Ler(Godot.FileAccess.GetFileAsBytes(arquivo));
			if (dados == null)
			{
				GD.PushWarning($"[planeta] {Name}: '{arquivo}' nao e um .pedacos valido -- reconverta os mapas.");
				return;
			}

			var doArquivo = new FonteDoArquivo(dados, this);

			// O VAZIO SO EXISTE ONDE ALGUEM PEDIU. Toda outra zona continua com a fonte de sempre,
			// e sem pagar um `if` por celula por causa de um lugar so.
			PintorDePedacos.IFonte fonte = vazioEmVoltaDe is { } tam
				? FonteComVazio.Montar(doArquivo, dados, this, tam)
				: doArquivo;

			_pintor = new PintorDePedacos(this, fonte);
			_pintor.Pintou += AvisarPedaco;
		}

		_pintor.Urgente(centro);
		SetProcess(true);
	}

	/// <summary>Quantos pedacos estao montados. So pro diagnostico provar que nao cresce.</summary>
	public int PedacosVivos => _pintor?.PedacosVivos ?? 0;

	public override void _Process(double delta)
	{
		// ZONA GUARDADA NAO STREAMA. O cache mantem o planeta na arvore, invisivel, justamente pra
		// nao remontar nada ao voltar -- deixar o pintor rodando ali seria pagar por um cenario
		// que ninguem esta vendo, e pior, DESCARTAR pedacos por causa de uma camera que ja esta
		// noutro mundo.
		if (!Visible) return;
		_pintor?.Passo();
	}

	// =====================================================================
	/// <summary>
	/// A ponte entre o arquivo e as tres camadas da cena.
	///
	/// A ORDEM DOS NOMES NO ARQUIVO E A VERDADE, e nao a ordem dos filhos do node: quem escreveu o
	/// `.pedacos` gravou "Chao, Decor, Objetos" junto dos dados (ver `MapConverter`). Procurar por
	/// nome e o que impede um filho a mais na cena (uma luz, um marcador) de deslocar tudo.
	/// </summary>
	private sealed class FonteDoArquivo(PedacosDoMapa dados, Node pai) : PintorDePedacos.IFonte
	{
		private readonly TileMapLayer?[] _camadas = Achar(dados, pai);

		private static TileMapLayer?[] Achar(PedacosDoMapa dados, Node pai)
		{
			var achadas = new TileMapLayer?[dados.Camadas.Length];
			for (int i = 0; i < achadas.Length; i++)
			{
				achadas[i] = pai.GetNodeOrNull<TileMapLayer>(dados.Camadas[i]);
				if (achadas[i] == null)
					GD.PushWarning($"[planeta] a cena nao tem a camada '{dados.Camadas[i]}' que o .pedacos espera");
			}
			return achadas;
		}

		public int Lado => dados.Lado;

		/// <summary>A faixa sai do INDICE do arquivo -- este mapa acaba onde acaba o desenho.</summary>
		public Rect2I? Faixa => new Rect2I(dados.Cx0, dados.Cy0,
										   Math.Max(0, dados.Cx1 - dados.Cx0),
										   Math.Max(0, dados.Cy1 - dados.Cy0));

		/// <summary>A camada de CHAO, pra quem compoe este mapa com um vazio em volta.</summary>
		public TileMapLayer? Chao => _camadas.Length > 0 ? _camadas[0] : null;

		public int Celulas(int cx, int cy)
		{
			int n = 0;
			for (int c = 0; c < _camadas.Length; c++)
				if (dados.Achar(cx, cy, c, out int _, out int q)) n += q;
			return n;
		}

		public int Pintar(int cx, int cy, int feitas, int maximo)
		{
			int inicioDaCamada = 0, pintadas = 0;

			for (int c = 0; c < _camadas.Length; c++)
			{
				if (!dados.Achar(cx, cy, c, out int inicio, out int quantas)) continue;

				// ONDE RETOMAR. `feitas + pintadas` e a posicao atual na contagem que atravessa as
				// tres camadas; subtrair o comeco desta camada da o indice dentro dela.
				int i = feitas + pintadas - inicioDaCamada;
				if (i < quantas)
				{
					if (i < 0) i = 0;
					TileMapLayer? camada = _camadas[c];
					for (; i < quantas && pintadas < maximo; i++, pintadas++)
					{
						CelulaDePedaco cel = dados.Celula(inicio, i);
						camada?.SetCell(new Vector2I(cel.X, cel.Y), cel.Fonte,
										new Vector2I(cel.Ax, cel.Ay));
					}
				}

				inicioDaCamada += quantas;
				if (pintadas >= maximo) break;
			}

			return pintadas;
		}

		public void Apagar(int cx, int cy)
		{
			for (int c = 0; c < _camadas.Length; c++)
			{
				if (!dados.Achar(cx, cy, c, out int inicio, out int quantas)) continue;
				TileMapLayer? camada = _camadas[c];
				if (camada == null) continue;
				for (int i = 0; i < quantas; i++)
				{
					CelulaDePedaco cel = dados.Celula(inicio, i);
					camada.EraseCell(new Vector2I(cel.X, cel.Y));
				}
			}
		}
	}

	// =====================================================================
	/// <summary>
	/// O MAPA DESENHADO NO MEIO DE UM VAZIO QUE NAO ACABA -- a fonte COMPOSTA da Sala do Tempo.
	///
	/// ============================ O QUE ELA E, E O QUE ELA NAO E ============================
	/// Dentro do retangulo do mapa ela **delega** ao `.pedacos`: o quarto desenhado a mao continua
	/// exatamente igual, com as paredes e a passagem de saida. Fora dele ela devolve UM tile, o
	/// mesmo chao branco que a sala ja usa, ate onde a camera for.
	///
	/// Nao ha mapa novo, nem arquivo novo, nem conversao nova -- e esse era o ponto. A sala e o caso
	/// mais facil de terreno gerado que existe: um tile so, sem relevo, sem vegetacao e sem colisao.
	/// Uma funcao constante nao precisa de Perlin, de bioma nem de seed, e nao precisa nem de um
	/// `TerrenoGerado` -- ela precisa de um `SetCell` com o mesmo argumento sempre.
	/// ======================================================================================
	///
	/// ============================ A ARMADILHA DA `Faixa` ============================
	/// O `PedacosDoMapa` guarda `Cx0/Cy0/Cx1/Cy1` DERIVADOS DO INDICE, e a composta **nao pode
	/// reportar essa faixa**: o pintor recorta o que pede por ela, entao devolver o retangulo do
	/// mapa faria o vazio nunca ser pedido -- o codigo todo aqui existiria e nao rodaria uma vez.
	/// Por isso <see cref="Faixa"/> e nula (sem limite), e por isso a `IFonte` precisou aprender o
	/// que e "sem limite" (ver `PintorDePedacos.IFonte.Faixa`).
	/// ==============================================================================
	///
	/// A CONTA DE CELULAS DE UM PEDACO SOMA AS DUAS PARTES, nesta ordem: primeiro as do arquivo,
	/// depois as do vazio. E o contrato do pintor (uma contagem unica por pedaco, retomavel de um
	/// indice); qual metade responde por qual indice e assunto desta classe e de mais ninguem.
	/// </summary>
	private sealed class FonteComVazio : PintorDePedacos.IFonte
	{
		private readonly FonteDoArquivo _mapa;
		private readonly TileMapLayer? _chao;
		private readonly int _w, _h;          // o mapa desenhado, em TILES
		private readonly ushort _fonte, _ax, _ay;

		private FonteComVazio(FonteDoArquivo mapa, TileMapLayer? chao, int w, int h,
							  ushort fonte, ushort ax, ushort ay)
		{
			_mapa = mapa; _chao = chao; _w = w; _h = h;
			_fonte = fonte; _ax = ax; _ay = ay;
		}

		/// <summary>
		/// Monta a composta DESCOBRINDO qual e o tile do vazio -- o mais repetido no chao do mapa.
		///
		/// ============================ POR QUE NAO UM NUMERO CRAVADO ============================
		/// O tile branco da sala e a fonte 102 do tileset, e daria pra escrever 102 aqui. Nao: esse
		/// numero e um indice no `tileset.tres`, gerado pelo pipeline na ordem em que os atlas
		/// apareceram -- a proxima conversao pode renumerar tudo, e o sintoma seria o vazio pintando
		/// PEDRA em volta da sala, sem erro nenhum. A pergunta certa nao e "qual e o tile 102", e sim
		/// "qual chao esta sala usa", e o proprio `.pedacos` responde.
		///
		/// Medido no z13: 248.616 das 249.500 celulas de chao sao o mesmo tile (99,6%), e o segundo
		/// colocado tem 700. Nao ha ambiguidade a resolver -- a moda E o chao da sala.
		/// ====================================================================================
		/// </summary>
		public static PintorDePedacos.IFonte Montar(FonteDoArquivo mapa, PedacosDoMapa dados,
													Node pai, Vector2I tamanhoEmTiles)
		{
			ulong t0 = Time.GetTicksUsec();

			// A MODA DA CAMADA 0 (Chao). Um dicionario de chave empacotada em long: sao 6 tiles
			// distintos no z13, entao ele nunca passa de um punhado de entradas.
			var conta = new Dictionary<long, int>();
			int melhorN = 0;
			long melhor = -1;
			for (int cy = dados.Cy0; cy < dados.Cy1; cy++)
				for (int cx = dados.Cx0; cx < dados.Cx1; cx++)
				{
					if (!dados.Achar(cx, cy, 0, out int inicio, out int quantas)) continue;
					for (int i = 0; i < quantas; i++)
					{
						CelulaDePedaco cel = dados.Celula(inicio, i);
						long chave = ((long)cel.Fonte << 32) | ((long)cel.Ax << 16) | cel.Ay;
						conta.TryGetValue(chave, out int n);
						conta[chave] = ++n;
						if (n > melhorN) { melhorN = n; melhor = chave; }
					}
				}

			if (melhor < 0)
			{
				// FALHAR ALTO. Sem chao no arquivo nao ha o que repetir, e um vazio de tile
				// inexistente seria um mapa que nao desenha e nao reclama.
				GD.PushWarning($"[planeta] {pai.Name}: o .pedacos nao tem chao -- o vazio em volta "
							   + "nao vai ser pintado.");
				return mapa;
			}

			var pronta = new FonteComVazio(mapa, mapa.Chao, tamanhoEmTiles.X, tamanhoEmTiles.Y,
										   (ushort)(melhor >> 32), (ushort)((melhor >> 16) & 0xFFFF),
										   (ushort)(melhor & 0xFFFF));
			GD.Print($"[planeta] {pai.Name}: vazio em volta com o tile {pronta._fonte}"
					 + $"[{pronta._ax},{pronta._ay}] ({melhorN} de {dados.TotalDeCelulas} celulas, "
					 + $"{conta.Count} distintos) -- achado em {(Time.GetTicksUsec() - t0) / 1000.0:0.0} ms");
			return pronta;
		}

		public int Lado => _mapa.Lado;

		/// <summary>SEM LIMITE. Ver a armadilha da `Faixa` no cabecalho da classe.</summary>
		public Rect2I? Faixa => null;

		public int Celulas(int cx, int cy) => DoArquivo(cx, cy) + DoVazio(cx, cy);

		public int Pintar(int cx, int cy, int feitas, int maximo)
		{
			int doArquivo = DoArquivo(cx, cy);
			int pintadas = 0;

			if (feitas < doArquivo)
			{
				pintadas = _mapa.Pintar(cx, cy, feitas, maximo);
				if (pintadas >= maximo) return pintadas;
			}

			// O VAZIO COMECA ONDE O ARQUIVO ACABOU, na contagem unica do pedaco.
			int i = feitas + pintadas - doArquivo;
			int total = DoVazio(cx, cy);
			for (; i < total && pintadas < maximo; i++, pintadas++)
				_chao?.SetCell(Vazia(cx, cy, i), _fonte, new Vector2I(_ax, _ay));

			return pintadas;
		}

		public void Apagar(int cx, int cy)
		{
			if (Encosta(cx, cy)) _mapa.Apagar(cx, cy);
			int total = DoVazio(cx, cy);
			for (int i = 0; i < total; i++) _chao?.EraseCell(Vazia(cx, cy, i));
		}

		// =====================================================================
		// A GEOMETRIA DO VAZIO
		// =====================================================================
		/// <summary>
		/// Quantas celulas deste pedaco o ARQUIVO responde. Pedaco que nao encosta no mapa nem
		/// pergunta -- o `PedacosDoMapa` indexa a chave por `short`, e mandar coordenada de pedaco
		/// muito longe pra ele seria pedir uma chave que nem devia existir.
		/// </summary>
		private int DoArquivo(int cx, int cy) => Encosta(cx, cy) ? _mapa.Celulas(cx, cy) : 0;

		private bool Encosta(int cx, int cy)
		{
			int x0 = cx * Lado, y0 = cy * Lado;
			return x0 < _w && y0 < _h && x0 + Lado > 0 && y0 + Lado > 0;
		}

		/// <summary>
		/// Quantas celulas deste pedaco caem FORA do retangulo do mapa.
		///
		/// A conta e por LINHA e nao por celula: a linha inteira e vazio quando o `y` esta fora, e
		/// senao sobram so as colunas de fora, que sao as mesmas em todas as linhas do pedaco.
		/// </summary>
		private int DoVazio(int cx, int cy)
		{
			int linhasFora = ForaDaFaixa(cy * Lado, _h);
			return linhasFora * Lado + (Lado - linhasFora) * ForaDaFaixa(cx * Lado, _w);
		}

		/// <summary>Quantos dos <see cref="Lado"/> indices a partir de <paramref name="de"/> caem fora de [0, fim).</summary>
		private int ForaDaFaixa(int de, int fim)
		{
			int antes = Math.Clamp(0 - de, 0, Lado);
			int depois = Math.Clamp(de + Lado - fim, 0, Lado);
			return Math.Min(Lado, antes + depois);
		}

		/// <summary>
		/// A i-esima celula de VAZIO do pedaco, em ordem de linha.
		///
		/// Percorre as linhas somando quantas cada uma tem (no maximo <see cref="Lado"/> voltas)
		/// ate achar a que contem `i`. Poderia ser uma formula fechada, mas os pedacos com as duas
		/// metades sao so os da beirada do mapa; o resto e vazio puro e sai na primeira conta. Uma
		/// formula fechada aqui seria dificil de ler pra economizar 64 somas.
		/// </summary>
		private Vector2I Vazia(int cx, int cy, int i)
		{
			int x0 = cx * Lado, y0 = cy * Lado;
			int naLinha = ForaDaFaixa(x0, _w);

			for (int dy = 0; dy < Lado; dy++)
			{
				int y = y0 + dy;
				bool linhaInteira = y < 0 || y >= _h;
				int quantas = linhaInteira ? Lado : naLinha;
				if (i >= quantas) { i -= quantas; continue; }

				if (linhaInteira) return new Vector2I(x0 + i, y);

				// SO AS COLUNAS DE FORA: as de antes do mapa primeiro, as de depois em seguida.
				int antes = Math.Clamp(0 - x0, 0, Lado);
				return i < antes
					? new Vector2I(x0 + i, y)
					: new Vector2I(Math.Max(x0, _w) + (i - antes), y);
			}

			// inalcancavel enquanto `i < DoVazio(cx,cy)`, que e o unico jeito de chamar isto
			return new Vector2I(x0, y0);
		}
	}
}
