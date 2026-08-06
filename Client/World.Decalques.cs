using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// OS QUATRO DECALQUES QUE O CLIENTE DECIDE SOZINHO.
///
/// O rastro de arremesso vem do servidor (ver `Protocol.S2C.Decalque`) porque depende da distancia
/// TOTAL do voo, que so quem lancou o corpo sabe. Estes quatro nao: sao consequencia de coisas que
/// o cliente JA VE acontecer -- uma celula caiu, um corpo esta sobre agua, um corpo esta se
/// acabando. Mandar isso pela rede seria pagar bytes por uma conta que as duas pontas fazem igual.
///
/// ============================ MAS ENTAO CADA UM VE UMA COISA? ============================
/// Nao: as escolhas "aleatorias" saem de um EMBARALHADOR DA CELULA, nao de `randf()`. Dois clientes
/// olhando a mesma pedra caida sorteiam os mesmos vizinhos, sem trocar um byte -- e a mesma regra
/// que ja vale pro ceu, pro clima e pro universo (funcao pura da coordenada/seed).
///
/// O SANGUE E A EXCECAO e esta anotado como tal: ele depende do INSTANTE em que a vida caiu, e nao
/// so de posicao, entao dois clientes podem respingar em vizinhos diferentes. E respingo de sangue,
/// nao geometria -- ninguem toma decisao de jogo olhando pra ele.
/// =========================================================================================
/// </summary>
public partial class World : Node2D
{
	private Decalques? _decalques;

	/// <summary>
	/// AS FOLHAS QUE SAO AGUA, pelo NOME.
	///
	/// O `.col` nao diz "isto e agua" -- ele so tem bloqueia/nao bloqueia. Quem sabe e a ARTE, e a
	/// arte se identifica pelo nome do atlas, que e como o `CatalogoDeTiles` ja fala de arte
	/// ("nomes sao o unico jeito honesto de o Core falar de arte").
	///
	/// A lista e por SUBSTRING de proposito: as folhas de agua deste projeto sao "Waters",
	/// "CartoonWater2017", "WaterBlue2017", "namekwater", "ToxicWater" -- todas contem "water". Um
	/// mapa novo com "Ocean.png" nao seria pego, e o sintoma seria so a onda nao aparecer; por isso
	/// ha o `--diagdecalque`, que CONTA quantas celulas de agua a zona tem.
	/// </summary>
	private static readonly string[] MarcasDeAgua = ["water", "agua", "oceano", "lake"];

	private readonly Dictionary<int, bool> _fonteEhAgua = [];

	/// <summary>Celulas de agua que ja estao com onda -- o `turf.ki_water` do DU, que evita empilhar.</summary>
	private readonly Dictionary<Vector2I, double> _ondaAte = [];

	/// <summary>A vida que cada corpo tinha na ultima olhada -- pro sangue saber que PIOROU.</summary>
	private readonly Dictionary<int, byte> _vidaAnterior = [];

	private double _relogioDecal;

	/// <summary>Abaixo disto o corpo esta "se acabando" e comeca a respingar.</summary>
	private const byte VidaQueSangra = 35;

	/// <summary>Segundos entre um respingo e outro do MESMO corpo. Sem isto seria um por quadro.</summary>
	private const double EsperaDoSangue = 0.8;

	private readonly Dictionary<int, double> _sangueAte = [];

	/// <summary>Monta o node dos decalques e liga o canal do servidor. Chamado no `_Ready` do World.</summary>
	private void MontarDecalques()
	{
		_decalques = new Decalques { Name = "Decalques" };
		AddChild(_decalques);
		if (GameClient.Instance is { } cli) cli.DecalqueCaiu += AoCairDecalque;
	}

	private void SoltarDecalques()
	{
		if (GameClient.Instance is { } cli) cli.DecalqueCaiu -= AoCairDecalque;
	}

	private void AoCairDecalque(Protocol.Decal tipo, Vec2 onde, Facing dir)
		=> _decalques?.Plantar(tipo, new Vector2(onde.X, onde.Y), dir);

	/// <summary>
	/// TERRA REVIRADA EM VOLTA DO QUE CAIU -- em ALGUNS vizinhos, nao todos.
	///
	/// "Todos" desenharia um quadrado perfeito de terra em volta do buraco, que le como bug de
	/// tilemap e nao como estrago. O sorteio e do EMBARALHADOR DA CELULA (ver o cabecalho): a mesma
	/// pedra da os mesmos vizinhos em todo cliente, sem trocar byte.
	/// </summary>
	private void ChaoDanificadoEmVolta(int cx, int cy)
	{
		if (_decalques == null || _colisao == null) return;

		int t = ZoneCollision.TileSize;
		for (int dx = -1; dx <= 1; dx++)
		for (int dy = -1; dy <= 1; dy++)
		{
			if (dx == 0 && dy == 0) continue;   // o centro e o buraco, nao a borda dele
			int x = cx + dx, y = cy + dy;
			if (x < 0 || y < 0 || x >= _colisao.Width || y >= _colisao.Height) continue;
			// SO EM CHAO LIVRE: pintar terra revirada sobre uma parede que continua de pe poe a
			// marca por baixo dela, e o jogador ve uma sombra saindo de dentro da pedra.
			if (_colisao.BlockedCell(x, y)) continue;

			// 45% -- o bastante pra a mancha ser irregular sem ficar rala.
			if (Embaralhar(x, y, 7) % 100 >= 45) continue;

			_decalques.Plantar(Protocol.Decal.ChaoDanificado,
				new Vector2((x + 0.5f) * t, (y + 0.5f) * t), Facing.South);
		}
	}

	/// <summary>
	/// EMBARALHADOR DA CELULA: funcao pura de (x, y, tempero) -> numero.
	///
	/// Nao e `randf()` de proposito. Sorteio local faria cada cliente ver uma mancha diferente em
	/// volta da MESMA pedra -- e cenario que discorda entre telas e a primeira coisa que alguem
	/// fotografa achando que e bug. E o mesmo argumento que ja pos o ceu e o universo no cliente.
	/// </summary>
	private static uint Embaralhar(int x, int y, uint tempero)
	{
		unchecked
		{
			uint h = 2166136261u;
			h = (h ^ (uint)x) * 16777619u;
			h = (h ^ (uint)y) * 16777619u;
			h = (h ^ tempero) * 16777619u;
			h ^= h >> 13;
			return h;
		}
	}

	/// <summary>
	/// QUANTOS dos 8 vizinhos esta celula pintaria. So pras bancadas -- e usa a MESMA conta do
	/// desenho, senao o teste mediria uma segunda implementacao e nao esta.
	/// </summary>
	public int VizinhosPintadosDeTeste(int cx, int cy)
	{
		int n = 0;
		for (int dx = -1; dx <= 1; dx++)
		for (int dy = -1; dy <= 1; dy++)
		{
			if (dx == 0 && dy == 0) continue;
			if (Embaralhar(cx + dx, cy + dy, 7) % 100 < 45) n++;
		}
		return n;
	}

	/// <summary>Esta celula e agua? Ver <see cref="MarcasDeAgua"/>.</summary>
	public bool EhAgua(Vector2I celula)
	{
		for (int i = _veu.Camadas.Length - 1; i >= 0; i--)
		{
			TileMapLayer camada = _veu.Camadas[i];
			if (!IsInstanceValid(camada) || camada.TileSet is not { } ts) continue;

			int fonte = camada.GetCellSourceId(celula);
			if (fonte < 0) continue;

			if (_fonteEhAgua.TryGetValue(fonte, out bool pronto)) { if (pronto) return true; continue; }

			bool agua = false;
			try
			{
				if (ts.GetSource(fonte) is TileSetAtlasSource a && a.Texture is { } tex)
				{
					string nome = tex.ResourcePath.GetFile().ToLowerInvariant();
					foreach (string m in MarcasDeAgua)
						if (nome.Contains(m, StringComparison.Ordinal)) { agua = true; break; }
				}
			}
			catch { /* fonte sem textura legivel: nao e agua, e nao vale derrubar o quadro por isso */ }

			_fonteEhAgua[fonte] = agua;
			if (agua) return true;
		}
		return false;
	}

	/// <summary>
	/// O TIQUE DOS DECALQUES DE CORPO: a agua se afastando e o sangue de quem esta se acabando.
	///
	/// Roda por quadro porque os dois seguem corpos em movimento -- mas nenhum dos dois PLANTA por
	/// quadro: a agua tem a trava de celula do DU e o sangue tem espera por corpo.
	/// </summary>
	private void TickDosDecalques(double delta)
	{
		if (_decalques == null) return;
		_relogioDecal += delta;

		int t = ZoneCollision.TileSize;

		foreach ((int id, Node2D corpo, float altura, byte vida) in CorposParaDecalque())
		{
			var celula = new Vector2I(
				(int)MathF.Floor(corpo.Position.X / t), (int)MathF.Floor(corpo.Position.Y / t));

			// ---------- A AGUA SE AFASTA ----------
			// O DU dispara isto pra quem PASSA por cima da agua (mob andando, `Map 2022.dm:269`) e
			// pra tiro de ki cruzando (`Projectiles.dm:280`). Aqui: quem esta VOANDO por cima --
			// que e o pedido do dono -- e a trava de celula e a mesma (`turf.ki_water`).
			if (altura > 0f && EhAgua(celula))
			{
				if (!_ondaAte.TryGetValue(celula, out double ate) || _relogioDecal >= ate)
				{
					_ondaAte[celula] = _relogioDecal + 2.0;   // `sleep(20)` do DU
					_decalques.Plantar(Protocol.Decal.Agua,
						new Vector2((celula.X + 0.5f) * t, (celula.Y + 0.5f) * t), DirecaoDe(corpo));
				}
			}

			// ---------- O SANGUE ----------
			// Duas condicoes, as duas do dono: ferimento GRAVE e estar no chao ou voando baixo.
			// A altura entra porque respingo de sangue e uma coisa que cai NO CHAO -- de vinte
			// tiles ele nao chegaria la.
			_vidaAnterior.TryGetValue(id, out byte antes);
			_vidaAnterior[id] = vida;

			if (vida >= VidaQueSangra || vida == 0) continue;
			if (Jandirus.Core.World.Voo.Andar(altura) > 1) continue;
			// SO QUANDO PIOROU. Sem isto um corpo parado com 20% de vida sangraria pra sempre, sem
			// nada estar acontecendo com ele.
			if (vida >= antes) continue;
			if (_sangueAte.TryGetValue(id, out double espera) && _relogioDecal < espera) continue;
			_sangueAte[id] = _relogioDecal + EsperaDoSangue;

			// UM VIZINHO SORTEADO. Aqui o sorteio E local (ver o cabecalho): o respingo depende do
			// instante em que a vida caiu, nao so da posicao, entao nao ha coordenada estavel pra
			// embaralhar. E respingo -- ninguem decide nada olhando pra ele.
			int vx = celula.X + GD.RandRange(-1, 1);
			int vy = celula.Y + GD.RandRange(-1, 1);
			if (_colisao != null && (vx < 0 || vy < 0 || vx >= _colisao.Width || vy >= _colisao.Height)) continue;

			_decalques.Plantar(Protocol.Decal.Sangue,
				new Vector2((vx + 0.5f) * t, (vy + 0.5f) * t), DirecaoDe(corpo));
		}
	}

	private static Facing DirecaoDe(Node2D corpo)
		=> corpo is RemotePlayer r ? r.OlharDeTeste : Facing.South;

	/// <summary>Todo corpo da zona com o que os decalques precisam: id, node, altura e vida.</summary>
	private IEnumerable<(int Id, Node2D Corpo, float Altura, byte Vida)> CorposParaDecalque()
	{
		if (_local != null && GameClient.Instance is { } cli)
			yield return (cli.LocalId, _local, _local.Altitude, (byte)Mathf.Clamp(cli.Sheet.HP, 0, 100));

		foreach ((int id, RemotePlayer r) in _remotos)
			if (IsInstanceValid(r)) yield return (id, r, r.AlturaDeTeste, r.VidaDeTeste);
	}
}
