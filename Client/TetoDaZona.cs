using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O QUE ESTA SOB TETO NESTA ZONA, AGORA -- a copia do cliente, num byte por celula.
///
/// A regra e do Core (`CelulaInterna`, com as linhas do DM): o plano do mapa, menos o que caiu.
/// Esta classe existe porque o CLIENTE faz essa pergunta de dois jeitos que o Core nao atende:
///
///   * POR PIXEL, dentro de shader -- o clima nao e desenhado sobre celula interna, e quem decide
///     isso e o fragmento. Shader le textura, e por isso o plano vira uma (<see cref="Textura"/>:
///     um texel por celula, 255 = sob teto);
///   * MILHARES DE VEZES POR QUADRO, por celula -- e o `caiu` do Core, no cliente, sairia de uma
///     `List` (`GameClient.CenarioCaido`), que nao se consulta nesse ritmo.
///
/// Entao o desconto do que caiu e feito UMA vez, quando a celula cai, e o que sobra e um vetor
/// que responde com um indice.
///
/// ============================ UM DONO, E UM FUNIL DE ESCRITA ============================
/// Quem tem a instancia e o `World`, e so tres lugares de la escrevem nela: a carga da zona
/// (<see cref="Montar"/>), o `AplicarEstrago` (<see cref="Destelhar"/>) -- o mesmo ponto em que a
/// celula caida abre na colisao e deixa de cegar -- e o `AplicarBloco` (<see cref="Recontar"/>),
/// onde o bloco erguido por jogador cobre e descobre a celula. Pendurar a conta nesses pontos e o
/// que impede o teto de discordar do resto do que o cliente acha da celula.
///
/// A ESCRITA PASSA PELO `CelulaInterna.SobTeto`, e nao por uma atribuicao direta, de proposito:
/// os dois defeitos de bancada do Core (`SemPlanoDeTeste`, `CaidaContinuaInternaDeTeste`) valem
/// aqui sem uma linha a mais, e uma bancada de desenho consegue reproduzir o "antes" no mesmo
/// binario.
/// ==========================================================================================
/// </summary>
public sealed class TetoDaZona
{
	private ZoneCollision? _mapa;

	/// <summary>
	/// O mapa da zona montada, MESMO QUANDO ELA NAO TEM INTERIOR NENHUM. O <see cref="_mapa"/> fica nulo
	/// nesse caso (e a saida barata de quem pergunta), e o primeiro piso erguido num mundo sem teto
	/// precisa de onde tirar o tamanho -- ver <see cref="Recontar"/>.
	/// </summary>
	private ZoneCollision? _zona;
	private byte[] _dados = [];
	private Image? _imagem;
	private bool _sujo;

	/// <summary>O tamanho da zona, em celulas. Zero em zona sem interior.</summary>
	public int Largura { get; private set; }
	public int Altura { get; private set; }

	/// <summary>
	/// UM TEXEL POR CELULA, formato L8: 255 = sob teto agora, 0 = ar livre. Nula em zona sem
	/// interior nenhum (e sem janela) -- e quem desenha trata nula como "nada a recortar", sem
	/// amostrar nada. Quem quer saber se a zona TEM interior pergunta a <see cref="Largura"/>.
	/// </summary>
	public ImageTexture? Textura { get; private set; }

	/// <summary>
	/// O teto desta zona mudou: carregou outra zona, ou uma celula interna caiu. Quem guarda a
	/// <see cref="Textura"/> num material reassina aqui (a referencia TROCA a cada zona).
	/// </summary>
	public event Action? Mudou;

	/// <summary>
	/// (RE)MONTA A PARTIR DO MAPA DA ZONA. Mapa nulo ou sem plano = zona sem interior (mundo gerado,
	/// espaco, metade dos andares pre-feitos).
	///
	/// So o plano entra aqui: o estrago que ja estava feito chega logo depois pelo `AplicarEstrago`
	/// de cada celula caida (o `ReaplicarEstrago` do `World`), como chega pra colisao.
	/// </summary>
	public void Montar(ZoneCollision? mapa)
	{
		_zona = mapa;
		_mapa = mapa is { TemInterior: true } ? mapa : null;
		_sujo = false;
		int n = 0;

		if (_mapa != null)
		{
			Largura = _mapa.Width;
			Altura = _mapa.Height;

			// O VETOR E REAPROVEITADO quando o tamanho bate (os mapas pre-feitos sao quase todos
			// 500x500): sao 250 KB, que alocados a cada carga de zona iriam direto pro monte de objetos
			// grandes do .NET -- lixo que so sai numa coleta cheia.
			if (_dados.Length == Largura * Altura) Array.Clear(_dados);
			else _dados = new byte[Largura * Altura];

			// SO AS CELULAS QUE O PLANO MARCA SAO VISITADAS (`ParaCadaCelulaQueNasceuDentro` pula os
			// bytes zerados): perguntar de todas custava ~5 ms por carga de zona na Terra. Cada uma
			// ainda passa pelo `CelulaInterna.SobTeto` -- o funil de que o cabecalho fala.
			ZoneCollision mapaDaqui = _mapa;
			byte[] dados = _dados;
			int largura = Largura;
			mapaDaqui.ParaCadaCelulaQueNasceuDentro((x, y) =>
			{
				if (CelulaInterna.SobTeto(mapaDaqui, x, y, caiu: false)) { dados[y * largura + x] = 255; n++; }
			});

			// E O QUE JA ESTAVA ERGUIDO quando a zona montou (a camada de runtime do mapa). A mesma
			// celula pode estar nas duas fontes -- por isso a contagem so sobe se o byte estava zerado.
			mapaDaqui.ParaCadaCelulaCoberta((x, y) =>
			{
				int i = y * largura + x;
				if (dados[i] == 0 && CelulaInterna.SobTeto(mapaDaqui, x, y, caiu: false)) { dados[i] = 255; n++; }
			});
		}

		if (n == 0)
		{
			// SEM NENHUMA CELULA: nao ha textura, e o desenho nem amostra. (Cai aqui tambem o
			// defeito `SemPlanoDeTeste`, que e exatamente "o plano nao vale".)
			_mapa = null;
			_dados = [];
			_imagem = null;
			Largura = Altura = 0;
			Textura = null;
		}
		else if (SemTela)
		{
			// SEM JANELA NAO HA TEXTURA: o renderizador de mentira do `--headless` recusa cria-la e
			// escreve um ERROR no log a cada zona carregada (visto na primeira rodada da
			// `--tetoteste`). O vetor continua valendo -- quem pergunta por celula nao depende dela.
			_imagem = null;
			Textura = null;
		}
		else
		{
			_imagem = Image.CreateFromData(Largura, Altura, false, Image.Format.L8, _dados);
			Textura = ImageTexture.CreateFromImage(_imagem);
		}
		Mudou?.Invoke();
	}

	private static bool? _semTela;
	private static bool SemTela => _semTela ??= DisplayServer.GetName() == "headless";

	/// <summary>
	/// UMA CELULA CAIU. No DM o turf destruido volta pra area de fora (`NewTurfs.dm:13-17`).
	///
	/// NAO SOBE A TEXTURA NA HORA: a reaplicacao do estrago de uma zona chama isto uma vez por
	/// celula caida, e subir 250 KB pra GPU a cada uma seria a troca de zona travando num mapa muito
	/// destruido. Quem chamou fecha o lote com <see cref="Assentar"/>.
	/// </summary>
	public void Destelhar(int cx, int cy)
	{
		if (_mapa == null || cx < 0 || cy < 0 || cx >= Largura || cy >= Altura) return;

		int i = cy * Largura + cx;
		byte novo = CelulaInterna.SobTeto(_mapa, cx, cy, caiu: true) ? (byte)255 : (byte)0;
		if (_dados[i] == novo) return;   // o caso comum: a celula que caiu ja era ar livre
		_dados[i] = novo;
		_sujo = true;
	}

	/// <summary>
	/// UM BLOCO FOI ERGUIDO OU DEMOLIDO NESTA CELULA: o teto dela e refeito pela regra do Core, que ja
	/// olha a camada de runtime do mapa (`ZoneCollision.Coberta`). Quem chama mexe no mapa ANTES.
	///
	/// NASCE AQUI O TETO DE UMA ZONA QUE NAO TINHA NENHUM: num mundo gerado o vetor nem existe ate
	/// alguem assentar o primeiro piso.
	///
	/// Nao sobe a textura na hora, pelo motivo do <see cref="Destelhar"/>: a lista de blocos de uma zona
	/// chega inteira, e quem a aplica fecha o lote com <see cref="Assentar"/>.
	/// </summary>
	/// <param name="caiu">O CENARIO desta celula ja caiu? So pesa onde o mapa a trouxe sob teto.</param>
	public void Recontar(int cx, int cy, bool caiu)
	{
		if (_zona == null || cx < 0 || cy < 0 || cx >= _zona.Width || cy >= _zona.Height) return;

		byte novo = CelulaInterna.SobTeto(_zona, cx, cy, caiu) ? (byte)255 : (byte)0;
		if (_mapa == null)
		{
			if (novo == 0) return;   // zona sem teto, e esta celula tambem nao ganhou um
			_mapa = _zona;
			Largura = _zona.Width;
			Altura = _zona.Height;
			_dados = new byte[Largura * Altura];
		}

		int i = cy * Largura + cx;
		if (_dados[i] == novo) return;
		_dados[i] = novo;
		_sujo = true;
	}

	/// <summary>Sobe pra textura o que o <see cref="Destelhar"/> e o <see cref="Recontar"/> mudaram, e avisa. Nao faz nada se nada mudou.</summary>
	public void Assentar()
	{
		if (!_sujo) return;
		_sujo = false;
		if (_imagem != null && Textura != null)
		{
			_imagem.SetData(Largura, Altura, false, Image.Format.L8, _dados);
			Textura.Update(_imagem);
		}
		else if (!SemTela && _dados.Length > 0)
		{
			// A PRIMEIRA CELULA SOB TETO DESTA ZONA (ver `Recontar`): a textura nasce agora.
			_imagem = Image.CreateFromData(Largura, Altura, false, Image.Format.L8, _dados);
			Textura = ImageTexture.CreateFromImage(_imagem);
		}
		Mudou?.Invoke();
	}

	/// <summary>ESTA CELULA ESTA SOB TETO AGORA? Fora do mapa, e em zona sem interior, nao.</summary>
	public bool SobTeto(int cx, int cy) =>
		cx >= 0 && cy >= 0 && cx < Largura && cy < Altura && _dados[cy * Largura + cx] != 0;
}
