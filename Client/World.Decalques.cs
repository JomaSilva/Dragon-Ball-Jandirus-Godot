using Godot;
using Jandirus.Core.Combat;
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

	/// <summary>
	/// O GRAU DE SANGUE que cada corpo tinha na ultima olhada -- pro respingo saber que PIOROU.
	///
	/// Guarda GRAU e nao vida porque vida alheia nao existe mais no cliente: ver o bloco em
	/// <see cref="SangueDaMascara"/>.
	/// </summary>
	private readonly Dictionary<int, int> _sangueAnterior = [];

	private double _relogioDecal;

	/// <summary>
	/// A PARTIR DESTE DEGRAU DE SANGUE o corpo esta "se acabando" e comeca a respingar.
	///
	/// DERIVADO do limiar antigo, pra o efeito nao mudar de lugar: ele disparava abaixo de 35% de
	/// vida, ou seja 65% de dano, e a curva de <see cref="Feridas"/> poe 65% de dano no degrau
	/// `round((0,65 - 0,55) / (0,90 - 0,55) * 15)` = 4. Mexer na curva la move este limiar junto, e
	/// tem que mover mesmo -- os dois descrevem o MESMO estado do corpo.
	/// </summary>
	private const int SangueQueRespinga = 4;

	/// <summary>Segundos entre um respingo e outro do MESMO corpo. Sem isto seria um por quadro.</summary>
	private const double EsperaDoSangue = 0.8;

	private readonly Dictionary<int, double> _sangueAte = [];

	/// <summary>Monta o node dos decalques e liga o canal do servidor. Chamado no `_Ready` do World.</summary>
	private void MontarDecalques()
	{
		_decalques = new Decalques { Name = "Decalques" };
		AddChild(_decalques);
		if (GameClient.Instance is not { } cli) return;

		// METODOS NOMEADOS, e o `-=` de cada um mora no `SoltarDecalques` -- lambda em evento de vida
		// longa vira ouvinte orfao no relog, e este projeto ja pagou 19 por ciclo.
		cli.DecalqueCaiu += AoCairDecalque;
		cli.PecasChegaram += AoChegarRetratoDePecas;
		cli.ZoneChanged += AoTrocarDeZonaDosDecalques;

		// O RETRATO QUE CHEGOU ANTES DE O WORLD EXISTIR: no login o `S2C.Pecas` sai antes do
		// `JoinAccepted`, e nessa hora ninguem estava ouvindo. Mesma razao do `CenarioCaido`.
		_retratoDePecasPendente = cli.PecasDaZona.Zona != 0;
	}

	private void SoltarDecalques()
	{
		if (GameClient.Instance is not { } cli) return;
		cli.DecalqueCaiu -= AoCairDecalque;
		cli.PecasChegaram -= AoChegarRetratoDePecas;
		cli.ZoneChanged -= AoTrocarDeZonaDosDecalques;
	}

	private void AoCairDecalque(Protocol.Decal tipo, Vec2 onde, Facing dir, PecaDeCorpo peca)
		=> _decalques?.Plantar(tipo, new Vector2(onde.X, onde.Y), dir, peca);

	/// <summary>
	/// HA UM RETRATO DE PECAS ESPERANDO O CHAO CERTO. Ver <see cref="PlantarORetratoDePecas"/>: ele
	/// nao e plantado na chegada porque a chegada pode ser antes de a zona dele estar montada.
	/// </summary>
	private bool _retratoDePecasPendente;

	private void AoChegarRetratoDePecas() => _retratoDePecasPendente = true;

	/// <summary>
	/// A TROCA DE ZONA LIMPA O CHAO -- "marca da Terra nao vai pra Namek", que e o que o `Limpar`
	/// sempre prometeu. Ele so era chamado no ramo do ESPACO do `CarregarZona`; nos planetas a
	/// cratera de um mapa ficava desenhada no seguinte, e com o retrato das pecas isso viraria o
	/// braco perdido na Terra aparecendo em Namek ao lado dos que cairam la. Roda ANTES do
	/// `AoMudarZona` do World (assinado depois, no mesmo `_Ready`), e o que o mapa novo tem de
	/// permanente (o chao danificado) o `ReaplicarEstrago` devolve na carga -- sem duplicar.
	///
	/// O retrato pendente da zona VELHA morre junto: o da nova chega logo atras do `ZoneChanged`.
	/// </summary>
	private void AoTrocarDeZonaDosDecalques(ZoneKey zona, Vec2 spawn)
	{
		_decalques?.Limpar();
		_retratoDePecasPendente = false;
	}

	/// <summary>
	/// ============================ O RETRATO DAS PECAS ENTRA QUANDO O CHAO DELE ESTA MONTADO ============================
	/// O `S2C.Pecas` chega logo atras do `ZoneChanged`, e a troca de mapa do World e DIFERIDA por dois
	/// quadros (a tela de carregamento) -- plantar na chegada poria as pecas de Namek no chao da Terra e
	/// as veria sumir no `Limpar` da carga. Entao o retrato fica pendente e e plantado no primeiro quadro
	/// em que `_zonaDoAtual` (escrito pelo `CarregarZona`, depois do `Limpar`) e a zona DELE. No login e
	/// igual: o World nasce, monta a zona, e o retrato guardado no `GameClient` entra em seguida.
	///
	/// O PRAZO E O QUE FALTA, e nao os 600 s cheios -- ver `Decalques.Plantar`. A direcao vai `South`
	/// pelo mesmo motivo do `SoltarPecas` do servidor: a folha nao tem sufixo de direcao.
	/// ==============================================================================================================
	/// </summary>
	private void PlantarORetratoDePecas()
	{
		if (!_retratoDePecasPendente || _decalques == null || GameClient.Instance is not { } cli) return;
		// ANTES DA PRIMEIRA ZONA `_zonaDoAtual` e o `default` do struct, e o `Hash` dele percorre um
		// `Name` nulo. Sem zona montada nao ha chao em que plantar, e o retrato continua pendente.
		if (string.IsNullOrEmpty(_zonaDoAtual.Name)) return;
		(ulong zona, List<GameClient.PecaNoChaoInfo> pecas) = cli.PecasDaZona;
		if (zona != _zonaDoAtual.Hash) return;

		_retratoDePecasPendente = false;
		foreach (GameClient.PecaNoChaoInfo p in pecas)
			_decalques.Plantar(Protocol.Decal.Membro, new Vector2(p.Onde.X, p.Onde.Y), Facing.South,
							   p.Peca, p.RestanteMs / 1000.0);
	}

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

		PlantarORetratoDePecas();   // so faz algo quando ha retrato pendente E a zona dele e esta

		int t = ZoneCollision.TileSize;

		foreach ((int id, Node2D corpo, float altura, MascaraDeFeridas ferida) in CorposParaDecalque())
		{
			var celula = new Vector2I(
				(int)MathF.Floor(corpo.Position.X / t), (int)MathF.Floor(corpo.Position.Y / t));

			// ---------- A AGUA SE AFASTA ----------
			// O DU dispara isto pra quem PASSA por cima da agua (mob andando, `Map 2022.dm:269`) e
			// pra tiro de ki cruzando (`Projectiles.dm:280`). Aqui: quem esta VOANDO por cima --
			// que e o pedido do dono -- e a trava de celula e a mesma (`turf.ki_water`).
			if (altura > 0f && EhAgua(celula)) AbrirOnda(celula, corpo);

			// ---------- O SANGUE ----------
			// Duas condicoes, as duas do dono: ferimento GRAVE e estar no chao ou voando baixo.
			// A altura entra porque respingo de sangue e uma coisa que cai NO CHAO -- de vinte
			// tiles ele nao chegaria la.
			//
			// ============================ ISTO LIA A VIDA, E NAO LE MAIS ============================
			// O gatilho era `vida < 35 && vida < a de antes`, com a vida do corpo alheio saindo do
			// snapshot. Esse campo MORREU (ver `EntityState`): o dono tirou o hp alheio do jogo. O
			// gatilho passou pro GRAU DE FERIDA, que ja viaja pra zona inteira e que e literalmente o
			// que o jogador ve no sprite -- respingar exatamente quando o corpo fica mais sujo e mais
			// honesto do que respingar num numero que ninguem mais enxerga.
			//
			// E ISSO CONSERTOU UMA SEGUNDA LEITURA: o corpo LOCAL media a propria ficha (`Sheet.HP`) e
			// os remotos mediam o snapshot -- duas fontes pro mesmo efeito. Agora os dois saem da
			// mesma mascara, entao duas telas olhando a mesma briga concordam.
			// ===================================================================================
			int sangue = SangueDaMascara(ferida);

			// PRIMEIRA OLHADA NAO SANGRA. Sem isto, quem entra no meu campo de visao ja destrocado
			// respingaria na hora -- e nao aconteceu nada com ele, ele so apareceu.
			if (!_sangueAnterior.TryGetValue(id, out int antes)) { _sangueAnterior[id] = sangue; continue; }
			_sangueAnterior[id] = sangue;

			if (sangue < SangueQueRespinga) continue;
			if (Jandirus.Core.World.Voo.Andar(altura) > 1) continue;
			// SO QUANDO PIOROU. Sem isto um corpo parado e destrocado sangraria pra sempre, sem
			// nada estar acontecendo com ele.
			if (sangue <= antes) continue;
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

		TickDaAguaDosTiros(t);
		PodarAsPocas();
	}

	/// <summary>
	/// O KI QUE CRUZA A AGUA ABRE A MESMA ONDA -- e este e o pedaco que era PORTE e faltava.
	///
	/// ============================ AS DUAS FONTES DO ORIGINAL CONCORDAM ============================
	/// No DM quem molha o lago nao e so o corpo: e o TIRO, e ele dispara o efeito com a PROPRIA
	/// direcao.
	///
	///   * Finale-master, `Turfs.dm:58-61`: `KiWater()` varre `for(var/obj/attack/blast/M in
	///     view(1,src))` e monta `image(icon='KiWater.dmi', dir=M.dir)` -- o `dir` e o do BLAST.
	///   * DU-SOURCE, `Projectiles.dm:279-280`, dentro do `Move()` do tiro: `if(isturf(t) &&
	///     IsWater(t)) t.ki_water(dir)`.
	///
	/// O port tinha portado so a metade do CORPO (`Map 2022.dm:265`, o `Water_Ripple` de quem voa).
	/// Esta e a outra metade.
	/// ==============================================================================================
	///
	/// ============================ E A ALTURA NAO ENTRA AQUI ============================
	/// O corpo so molha VOANDO (`altura > 0`), porque quem esta a pe dentro do lago nada e nao
	/// sobrevoa. O tiro nao tem essa condicao em nenhuma das duas fontes -- e nem teria como: o DM
	/// nao tem altitude, o `Move()` do blast chama `ki_water` sempre. Alem disso a altitude do tiro
	/// NAO VIAJA no `ProjetilState`, entao exigi-la aqui custaria um bit no snapshot pra impor uma
	/// regra que o original nao tem. Fica como esta, e anotado.
	/// ==================================================================================
	/// </summary>
	private void TickDaAguaDosTiros(int t)
	{
		// A PODA MAIS BARATA QUE EXISTE: uma zona sem tiro nenhum nao paga nada. E o caso comum --
		// ki no ar e evento de briga, e briga e minoria do tempo de jogo.
		if (_tiros.Count == 0) return;

		foreach (ProjetilDesenhado tiro in _tiros.Values)
		{
			if (!IsInstanceValid(tiro)) continue;

			var celula = new Vector2I(
				(int)MathF.Floor(tiro.Position.X / t), (int)MathF.Floor(tiro.Position.Y / t));

			// A CELULA NOVA E A COTA DESTE EFEITO -- ver `ProjetilDesenhado.EntrouNaCelula`. Ela vem
			// ANTES do `EhAgua` de proposito: e a pergunta cara (varre as camadas do tilemap) que ela
			// existe pra evitar, e nao a plantacao.
			if (!tiro.EntrouNaCelula(celula)) continue;
			if (!EhAgua(celula)) continue;

			AbrirOnda(celula, tiro);
		}
	}

	/// <summary>
	/// A ONDA DE UMA CELULA, com a trava do DU -- e ela e UMA SO pro corpo e pro tiro.
	///
	/// ============================ A TRAVA E DA CELULA, E NAO DE QUEM PASSOU ============================
	/// No DU a trava mora no proprio turf: `turf/proc/ki_water(d)` faz `if(ki_water) return;
	/// ki_water=1; ...; sleep(20); ki_water=0` (`Unsorted 2.dm:378-388`). Ou seja: enquanto uma
	/// celula esta ondulando, NINGUEM abre onda nova nela -- nem outro tiro, nem um corpo voando por
	/// cima. Este dicionario e o mesmo `turf.ki_water`, e por isso o tiro e o corpo o dividem.
	///
	/// O QUE ISSO CUSTA, E A ESCOLHA: um corpo que acabou de sobrevoar uma celula ENGOLE a onda que o
	/// raio abriria ali 1 s depois, e um raio que fosse rapido o bastante pra cruzar a mesma celula
	/// duas vezes (ida e volta) so desenha uma. E fiel, e e o que se escolhe: a alternativa (uma
	/// trava por projetil) faria dois raios paralelos empilharem duas ondas no mesmo tile, que e
	/// exatamente o que o `sleep(20)` do original existe pra impedir.
	///
	/// E A CADENCIA NAO APERTA O RAIO: ele anda 3,3 tiles/s (`Projetil.SegundosPorTile`, 0,3 s por
	/// tile) contra os 2 s da trava -- cada celula do caminho dele e virgem, entao o rastro sai
	/// inteiro. So a cabeca PARADA (o raio encostado em alguem) deixa de repintar, que e o certo.
	/// ====================================================================================================
	/// </summary>
	private void AbrirOnda(Vector2I celula, Node2D quem)
	{
		if (_decalques == null) return;
		if (_ondaAte.TryGetValue(celula, out double ate) && _relogioDecal < ate) return;

		_ondaAte[celula] = _relogioDecal + SegundosDeOnda;
		float t = ZoneCollision.TileSize;
		_decalques.Plantar(Protocol.Decal.Agua,
			new Vector2((celula.X + 0.5f) * t, (celula.Y + 0.5f) * t), DirecaoDe(quem));
	}

	/// <summary>`sleep(20)` do DU com `tick_lag` de 0,1 s. O mesmo numero que o prazo do desenho.</summary>
	private const double SegundosDeOnda = 2.0;

	/// <summary>
	/// JOGA FORA AS TRAVAS JA VENCIDAS.
	///
	/// ============================ POR QUE ISTO PASSOU A SER PRECISO ============================
	/// O `_ondaAte` guarda uma entrada por celula ja molhada e NUNCA tirava nenhuma. Com so o corpo
	/// abrindo onda isso crescia devagar (um jogador voando pinta ~10 celulas/s e cansa). O raio muda
	/// a escala: ele cruza um lago inteiro em segundos, e varios podem estar no ar.
	///
	/// A PODA E POR TETO E NAO POR RELOGIO: varrer o dicionario todo quadro seria o "pesado no tique"
	/// que a casa proibe. Aqui ela so acontece quando o dicionario passa do teto, e como toda entrada
	/// vence em 2 s, uma passada limpa quase tudo -- na pratica, uma varredura a cada muitos segundos
	/// de briga sobre agua.
	/// ==========================================================================================
	/// </summary>
	private void PodarAsPocas()
	{
		if (_ondaAte.Count < TetoDePocas) return;

		_pocasVencidas.Clear();
		foreach ((Vector2I celula, double ate) in _ondaAte)
			if (_relogioDecal >= ate) _pocasVencidas.Add(celula);
		foreach (Vector2I celula in _pocasVencidas) _ondaAte.Remove(celula);
		_pocasVencidas.Clear();
	}

	/// <summary>
	/// A partir de quantas celulas travadas a poda roda. Generoso: um lago da Terra tem algumas
	/// centenas de celulas, e o custo de uma entrada e um `Vector2I` + um `double`.
	/// </summary>
	private const int TetoDePocas = 1024;

	/// <summary>Reusada entre as podas -- alocar uma lista por passada seria lixo por quadro.</summary>
	private readonly List<Vector2I> _pocasVencidas = [];

	/// <summary>
	/// PRA ONDE ESTE CORPO ESTA VIRADO -- a direcao que a onda da agua e o respingo de sangue usam.
	///
	/// ============================ ISTO RESPONDIA `SOUTH` PRO DONO ============================
	/// Era `corpo is RemotePlayer r ? r.OlharDeTeste : Facing.South`. O corpo LOCAL nao e
	/// `RemotePlayer`: ele caia no `else` e recebia sul FIXO, que o `Escolher` traduz pro eixo "ns".
	/// Resultado fotografado pelo dono: voando da esquerda pra direita sobre a agua, a onda continuava
	/// desenhada em pe. O defeito nao era a arte nem a regra de eixo -- as duas estavam certas; era o
	/// proprio corpo do jogador ser o unico que ninguem sabia ler.
	///
	/// O rastro nao GUARDA rumo nenhum: ele le o que o corpo ja tem (ver `LocalPlayer.OlharDeTeste`,
	/// que devolve o mesmo par de campos com que o `_Process` escolhe a folha do sprite). Dois rumos
	/// divergem -- e o corpo sabe do dele antes de qualquer decalque.
	/// ========================================================================================
	/// </summary>
	/// <remarks>
	/// A TERCEIRA RESPOSTA E O TIRO, e ela precisou existir pelo mesmo motivo que a primeira: um raio
	/// NAO E CORPO -- ele caia no `_ => South` e abria onda em pe atravessando o lago de lado. O rumo
	/// dele nao vem do pacote; sai da cabeca e da cauda que ja viajam (ver
	/// <see cref="ProjetilDesenhado.Mirar"/>).
	/// </remarks>
	private static Facing DirecaoDe(Node2D corpo) => corpo switch
	{
		LocalPlayer l => l.OlharDeTeste,
		RemotePlayer r => r.OlharDeTeste,
		ProjetilDesenhado t => t.OlharDeTeste,
		_ => Facing.South,
	};

	/// <summary>
	/// A direcao com que o rastro do corpo LOCAL sairia agora. So pra bancada -- e chama a MESMA
	/// <see cref="DirecaoDe"/> do desenho, senao o teste mediria uma segunda implementacao (foi assim
	/// que este projeto ja deixou quatro bugs visuais passarem por bancada verde).
	/// </summary>
	public Facing DirecaoDoRastroDeTeste => _local is { } l ? DirecaoDe(l) : Facing.South;

	/// <summary>
	/// A direcao com que a onda de um TIRO sairia. So pra bancada, e chama a MESMA
	/// <see cref="DirecaoDe"/> -- o `switch` e o unico lugar onde um raio deixa de ser "corpo
	/// desconhecido", e ler o `OlharDeTeste` do node direto pularia justamente ele.
	/// </summary>
	public Facing DirecaoDoTiroDeTeste(int id) =>
		_tiros.TryGetValue(id, out ProjetilDesenhado? no) ? DirecaoDe(no) : Facing.South;

	/// <summary>
	/// O PIOR SANGUE DO CORPO, de 0 a <see cref="MascaraDeFeridas.Degraus"/>.
	///
	/// O PIOR e nao a media, pela mesma razao que a mascara ja usa o pior membro de cada zona: quem
	/// esta com o abdomen aberto e as pernas inteiras esta se acabando, e uma media diria "meio bem".
	///
	/// MEMBRO ARRANCADO E O MAXIMO, sem olhar zona: a zona "bracos" de quem perdeu um braco fica com
	/// o sangue do braco que SOBROU, que pode ser zero -- e um corpo com um braco a menos que nao
	/// respinga seria exatamente o caso que o efeito existe pra cobrir.
	/// </summary>
	private static int SangueDaMascara(MascaraDeFeridas m)
	{
		if (m.Amputados != MascaraDeFeridas.Membro.Nenhum) return MascaraDeFeridas.Degraus;

		int pior = 0;
		for (int z = 0; z < MascaraDeFeridas.Zonas; z++) pior = Math.Max(pior, m.Bruto(z) & 0x0F);
		return pior;
	}

	/// <summary>
	/// Todo corpo da zona com o que os decalques precisam: id, node, altura e a MASCARA DE FERIDAS.
	///
	/// A mascara vem do mesmo dicionario que pinta o sprite (`GameClient.Feridas`) -- inclusive a
	/// minha, porque o servidor manda as feridas de cada um pra zona inteira, o dono incluso. Corpo
	/// de quem ainda nao teve ferida nenhuma nao esta no mapa: mascara limpa, e limpa nao respinga.
	/// </summary>
	private IEnumerable<(int Id, Node2D Corpo, float Altura, MascaraDeFeridas Ferida)> CorposParaDecalque()
	{
		if (GameClient.Instance is not { } cli) yield break;

		MascaraDeFeridas Ferida(int id) =>
			cli.Feridas.TryGetValue(id, out MascaraDeFeridas m) ? m : default;

		if (_local != null)
			yield return (cli.LocalId, _local, _local.Altitude, Ferida(cli.LocalId));

		foreach ((int id, RemotePlayer r) in _remotos)
			if (IsInstanceValid(r)) yield return (id, r, r.AlturaDeTeste, Ferida(id));
	}
}
