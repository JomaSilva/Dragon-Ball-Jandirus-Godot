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
	public void Semear(string arquivo, Vector2? centro = null)
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

			var fonte = new FonteDoArquivo(dados, this);
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

		public Rect2I Faixa => new(dados.Cx0, dados.Cy0,
								   Math.Max(0, dados.Cx1 - dados.Cx0),
								   Math.Max(0, dados.Cy1 - dados.Cy0));

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
}
