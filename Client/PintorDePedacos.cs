using System;
using System.Collections.Generic;
using Godot;

namespace Jandirus.Client;

/// <summary>
/// ENTREGA AO TILEMAP SO O PEDACO DE MAPA QUE A CAMERA ALCANCA -- e tira dele o que ficou longe.
///
/// ============================ O PROBLEMA QUE ISTO RESOLVE ============================
/// Um `TileMapLayer` nao monta o desenho quando entra na arvore: ele monta quando o renderizador
/// pede, no primeiro quadro, e ai monta TUDO que tiver dentro. Na Terra sao 266 mil celulas a
/// ~2,7 us cada -- 708 ms de janela congelada, fora dos 659 ms de parse do `.tscn`. Somados, os
/// tres segundos de travada ao trocar de mapa.
///
/// E nao adiantava otimizar a carga: os marcos de `CarregarZona` mediam 130 ms e o dono via tres
/// segundos. Os dois numeros eram verdadeiros -- o custo estava DEPOIS da funcao, no quadro
/// seguinte, onde nenhuma medicao que comeca e termina dentro da carga chega.
///
/// A PROVA DE QUE A CULPA ERA O TAMANHO veio do proprio jogo: o ESPACO, que e maior que qualquer
/// planeta, entra instantaneo. Ele nao tem cena e desenha so o que cabe na tela (ver
/// `CeuDoEspaco`). Este pintor faz o mesmo com um mapa de verdade.
/// =====================================================================================
///
/// POR QUE PEDACO E NAO CELULA SOLTA (que e o que o `CeuDoEspaco` faz): o ceu tem uma celula por
/// coordenada e a pinta com uma funcao pura; um mapa tem tres camadas, celulas vazias no meio, e
/// os dados vem de arquivo. Um balde de 64x64 e a menor unidade que da pra achar num indice sem
/// guardar um registro por celula -- e 64 tiles (2048 px) e maior que a tela em qualquer zoom
/// jogavel, entao a camera nunca toca mais que 2x2 pedacos.
///
/// QUEM CHAMA: o dono (a raiz do planeta) chama <see cref="Passo"/> todo quadro. Nao e um Node de
/// proposito -- ele nao desenha nem tem estado de cena; virar filho da arvore so acrescentaria um
/// `_Process` a mais pra ordenar.
/// </summary>
public sealed class PintorDePedacos
{
	/// <summary>
	/// De onde saem as celulas de um pedaco. Duas implementacoes: o mapa pre-feito le do
	/// `.pedacos`, o procedural pinta do terreno que ele mesmo gerou.
	///
	/// A CONTA DE CELULAS E UNICA POR PEDACO, somando as camadas: quem retoma uma pintura
	/// interrompida so guarda um numero, e nao um par (camada, indice) que o pintor teria de
	/// entender. Qual camada recebe o que e assunto da fonte.
	/// </summary>
	public interface IFonte
	{
		/// <summary>Tiles por lado de um pedaco.</summary>
		int Lado { get; }

		/// <summary>
		/// A faixa valida de (cx, cy) -- fora dela nao ha mapa. **Nulo = SEM LIMITE**: o espaco de
		/// inteiros inteiro, e ai o pintor so recorta pelo que a camera alcanca.
		///
		/// ============================ POR QUE ELA PRECISOU ACEITAR "SEM LIMITE" ============================
		/// Ate aqui toda fonte tinha fim: o `.pedacos` sabe a faixa pelo indice e o terreno gerado
		/// pelo tamanho do mundo. A Sala do Tempo nao tem -- o quarto desenhado fica no meio de um
		/// vazio branco que continua pra sempre (ver `Core/World/SalaDoTempo.cs`).
		///
		/// UM RETANGULO ENORME NAO SERVIRIA. `PedacosPerto` faz `faixa.End.X - 1`, e `End` e
		/// `Position + Size`: uma faixa "quase infinita" estoura o int e o corte sai NEGATIVO -- o
		/// pintor pararia de pedir pedaco nenhum, calado. Nulo diz a mesma coisa sem aritmetica.
		/// ================================================================================================
		/// </summary>
		Rect2I? Faixa { get; }

		/// <summary>Quantas celulas o pedaco tem ao todo. Zero = nada a desenhar ali.</summary>
		int Celulas(int cx, int cy);

		/// <summary>
		/// Pinta ate <paramref name="maximo"/> celulas do pedaco, comecando na de indice
		/// <paramref name="feitas"/>. Devolve quantas pintou.
		/// </summary>
		int Pintar(int cx, int cy, int feitas, int maximo);

		/// <summary>Tira do tilemap as celulas deste pedaco.</summary>
		void Apagar(int cx, int cy);
	}

	/// <summary>
	/// QUANTO A PINTURA PODE ROUBAR DE UM QUADRO, em microssegundos.
	///
	/// 4 ms num quadro de 16,6 ms deixa folga pro resto do jogo. Nao e um teto de celulas: montar
	/// o desenho custa ~2,7 us por celula numa maquina e outra coisa noutra, e um numero fixo
	/// seria um chute que envelhece mal. O laco olha o relogio e para.
	/// </summary>
	private const ulong OrcamentoUs = 4000;

	/// <summary>
	/// Folga, em tiles, alem da borda da tela na hora de decidir QUAIS pedacos carregar.
	///
	/// Ela existe pra que o pedaco novo entre na fila MUITO antes de aparecer: andando, o jogador
	/// leva segundos pra cruzar 24 tiles, e o pintor precisa de alguns quadros. Sem folga, o
	/// pedaco comecaria a ser montado no instante em que ja deveria estar na tela, e o buraco
	/// apareceria.
	/// </summary>
	private const int FolgaDeCarga = 24;

	/// <summary>
	/// Folga, em tiles, pra DESCARTAR -- maior que a de carga, mas so o necessario.
	///
	/// AS DUAS TEM QUE SER DIFERENTES: com um limiar so, andar pra frente e pra tras em cima de
	/// uma divisa faria o mesmo pedaco ser montado e desmontado a cada passo. A margem util e UM
	/// PEDACO: pra provocar a troca o jogador teria de cruzar 64 tiles inteiros de ida e volta.
	///
	/// ============================ POR QUE ESTE NUMERO E, ANTES, ERA 160 ============================
	/// Com 160 este descarte NUNCA ACONTECIA, e o pintor so parecia limitado. A conta: a tela
	/// mostra ~30 tiles de mundo, entao o retangulo de guarda ia a 15 + 160 = 175 tiles pra cada
	/// lado -- 350 de ponta a ponta, uns 6 a 7 pedacos. Um planeta gerado de 256 tiles tem 4
	/// pedacos de lado INTEIRO: a guarda era maior que o mundo, e nada podia sair dela. Na Terra
	/// (500 tiles, ~8 pedacos) so caia uma coluna com o jogador colado na borda.
	///
	/// O efeito era o pico voltar a ser o mapa todo: passear pela zona remontava as 266 mil
	/// celulas aos poucos, e o cache de zona guardava isso pra proxima visita. O ENGASGO sumia (o
	/// orcamento por quadro dilui), mas o teto nao existia.
	///
	/// Com 64 a guarda fica em 15 + 64 = 79 tiles por lado, ~3 pedacos de ponta a ponta, contra
	/// ~2 da carga: sobra um pedaco de histerese e o descarte passa a valer em qualquer mapa.
	/// ===============================================================================================
	/// </summary>
	private const int FolgaDeDescarte = 64;

	private readonly Node2D _dono;
	private readonly IFonte _fonte;

	/// <summary>Pedacos ja tocados: quantas celulas de cada um ja foram pintadas.</summary>
	private readonly Dictionary<Vector2I, int> _pintados = [];

	/// <summary>Os que ainda faltam terminar, do mais perto da camera pro mais longe.</summary>
	private readonly List<Vector2I> _fila = [];

	private Rect2I _ultimoQuerido = new(0, 0, -1, -1);

	/// <summary>Centro imposto de fora, so durante o <see cref="Urgente"/>. Ver la o porque.</summary>
	private Vector2? _centroForcado;

	/// <summary>Avisa que um pedaco ficou pronto -- e o gancho do estrago (ver `World.ReaplicarEstrago`).</summary>
	public event Action? Pintou;

	public PintorDePedacos(Node2D dono, IFonte fonte)
	{
		_dono = dono;
		_fonte = fonte;
	}

	/// <summary>Quantos pedacos estao montados agora. So pro diagnostico provar que nao cresce.</summary>
	public int PedacosVivos => _pintados.Count;

	/// <summary>Se ainda falta alguem terminar de entrar.</summary>
	public bool Ocupado => _fila.Count > 0;

	/// <summary>
	/// TUDO QUE A CAMERA ALCANCA, AGORA, SEM ORCAMENTO.
	///
	/// Chamado uma vez ao entrar na zona, por baixo da tela de carregamento. Fatiar aqui nao
	/// ajudaria: o jogador ja esta olhando pro lugar, e um chao que chega em pedacos na frente
	/// dele e pior que meio quadro a mais escondido atras da tela de carga.
	/// </summary>
	public void Urgente(Vector2? centro = null)
	{
		// A CAMERA AINDA ESTA NO MAPA ANTERIOR quando isto roda numa troca de zona -- quem a move e
		// o `Teleportar`, que vem depois. Montar em volta dela deixaria o jogador sobre o vazio e o
		// chao do outro lado do planeta.
		_centroForcado = centro;
		try
		{
			Reenfileirar();
			while (_fila.Count > 0) Trabalhar(ulong.MaxValue);
		}
		finally { _centroForcado = null; }
	}

	/// <summary>Um quadro de trabalho: reavalia o que a camera quer e pinta o que couber.</summary>
	public void Passo()
	{
		Reenfileirar();
		if (_fila.Count > 0) Trabalhar(OrcamentoUs);
	}

	/// <summary>Esquece tudo -- o dono vai repovoar do zero.</summary>
	public void Limpar()
	{
		foreach (Vector2I p in _pintados.Keys) _fonte.Apagar(p.X, p.Y);
		_pintados.Clear();
		_fila.Clear();
		_ultimoQuerido = new Rect2I(0, 0, -1, -1);
	}

	// =====================================================================
	// O QUE A CAMERA QUER
	// =====================================================================
	private void Reenfileirar()
	{
		Rect2I quer = PedacosPerto(FolgaDeCarga);
		if (quer == _ultimoQuerido) return;
		_ultimoQuerido = quer;

		Rect2I guarda = PedacosPerto(FolgaDeDescarte);

		// SAI QUEM FICOU LONGE. Percorrer a lista de tras pra frente e o que deixa remover no
		// meio do laco sem pular ninguem.
		var longe = new List<Vector2I>();
		foreach (Vector2I p in _pintados.Keys)
			if (!guarda.HasPoint(p)) longe.Add(p);
		foreach (Vector2I p in longe)
		{
			_fonte.Apagar(p.X, p.Y);
			_pintados.Remove(p);
			_fila.Remove(p);
		}

		// SO FALA QUANDO LARGA ALGO. E o unico jeito de ver de fora que o teto existe: um pintor
		// que nunca descarta e indistinguivel de um que pinta o mapa todo, so mais devagar -- foi
		// exatamente o que aconteceu com a folga em 160.
		if (longe.Count > 0)
			GD.Print($"[pedacos] soltei {longe.Count}, ficam {_pintados.Count}");

		for (int cy = quer.Position.Y; cy < quer.End.Y; cy++)
			for (int cx = quer.Position.X; cx < quer.End.X; cx++)
			{
				var p = new Vector2I(cx, cy);
				int total = _fonte.Celulas(cx, cy);
				if (total == 0) { _pintados.TryAdd(p, 0); continue; }
				if (_pintados.TryGetValue(p, out int feitas) && feitas >= total) continue;
				if (!_fila.Contains(p)) _fila.Add(p);
			}

		// O MAIS PERTO PRIMEIRO. Quando dois pedacos entram juntos (uma quina de tela), o que
		// esta debaixo do jogador tem que chegar antes do que esta na diagonal.
		Vector2I centro = PedacoDaCamera();
		_fila.Sort((a, b) => Longe(a, centro) - Longe(b, centro));
	}

	private static int Longe(Vector2I p, Vector2I centro) =>
		Math.Abs(p.X - centro.X) + Math.Abs(p.Y - centro.Y);

	private void Trabalhar(ulong orcamento)
	{
		ulong inicio = Time.GetTicksUsec();
		while (_fila.Count > 0)
		{
			Vector2I p = _fila[0];
			int total = _fonte.Celulas(p.X, p.Y);
			_pintados.TryGetValue(p, out int feitas);

			// FATIA GROSSA O BASTANTE PRA NAO OLHAR O RELOGIO A CADA CELULA, fina o bastante pra
			// nao estourar o orcamento por muito: 512 celulas custam ~1,4 ms.
			int passo = orcamento == ulong.MaxValue ? int.MaxValue : 512;
			feitas += _fonte.Pintar(p.X, p.Y, feitas, passo);
			_pintados[p] = feitas;

			if (feitas >= total)
			{
				_fila.RemoveAt(0);
				Pintou?.Invoke();
			}

			if (Time.GetTicksUsec() - inicio >= orcamento) return;
		}
	}

	// =====================================================================
	// GEOMETRIA
	// =====================================================================
	private const int Tile = Jandirus.Core.World.ZoneCollision.TileSize;

	private Vector2I PedacoDaCamera()
	{
		Vector2 centro = CentroDaCamera();
		return new Vector2I(
			Mathf.FloorToInt(centro.X / Tile / _fonte.Lado),
			Mathf.FloorToInt(centro.Y / Tile / _fonte.Lado));
	}

	private Vector2 CentroDaCamera()
	{
		if (_centroForcado is { } forcado) return forcado;
		Camera2D? cam = _dono.GetViewport()?.GetCamera2D();
		return cam?.GetScreenCenterPosition() ?? _dono.GlobalPosition;
	}

	/// <summary>Os pedacos que a tela alcanca com <paramref name="folga"/> tiles de sobra, cortados pela faixa do mapa.</summary>
	private Rect2I PedacosPerto(int folga)
	{
		Camera2D? cam = _dono.GetViewport()?.GetCamera2D();
		Vector2 tela = _dono.GetViewportRect().Size;
		if (cam != null && cam.Zoom.X > 0 && cam.Zoom.Y > 0) tela /= cam.Zoom;
		Vector2 centro = CentroDaCamera();

		float meiaX = tela.X * 0.5f + folga * Tile;
		float meiaY = tela.Y * 0.5f + folga * Tile;

		int lado = _fonte.Lado * Tile;
		int x0 = Mathf.FloorToInt((centro.X - meiaX) / lado);
		int y0 = Mathf.FloorToInt((centro.Y - meiaY) / lado);
		int x1 = Mathf.FloorToInt((centro.X + meiaX) / lado);
		int y1 = Mathf.FloorToInt((centro.Y + meiaY) / lado);

		// FONTE SEM LIMITE nao recorta nada: o que a camera alcanca JA e o corte (ver `IFonte.Faixa`).
		if (_fonte.Faixa is { } faixa)
		{
			x0 = Math.Max(x0, faixa.Position.X);
			y0 = Math.Max(y0, faixa.Position.Y);
			x1 = Math.Min(x1, faixa.End.X - 1);
			y1 = Math.Min(y1, faixa.End.Y - 1);
		}

		if (x1 < x0 || y1 < y0) return new Rect2I(x0, y0, 0, 0);
		return new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
	}
}
