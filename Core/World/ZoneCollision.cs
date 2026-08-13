namespace Jandirus.Core.World;

/// <summary>
/// A geometria de uma zona, do jeito que o SERVIDOR consegue usar: 1 bit por celula.
///
/// O cliente colide pelo TileMap do Godot (fisica local, resposta imediata). O servidor nao
/// pode instanciar uma cena de 250 mil tiles pra cada planeta -- entao le este mesmo dado
/// numa forma compacta (um andar de 500x500 = ~31 KB) e faz a conferencia por conta propria.
///
/// Como o Core nao conhece o Godot, quem carrega os BYTES e cada ponta do seu jeito
/// (File.ReadAllBytes no servidor, FileAccess do res:// no cliente).
/// </summary>
public sealed class ZoneCollision
{
	public const int TileSize = 32;

	public int Width { get; }
	public int Height { get; }
	private readonly byte[] _bits;

	/// <summary>
	/// QUE TILE E CADA CELULA -- um byte por celula, ou nulo se este arquivo nao trouxer o plano.
	///
	/// ============================ PRA QUE SERVE ============================
	/// So pra SOMBRA, e por um pedido preciso do dono: um muro contiguo e um obstaculo so, mas
	/// **so quando e o mesmo tile**. Sem isso, uma cerca de madeira encostada num muro de pedra
	/// vira um bloco unico e a sombra atravessa os dois -- foi o que ele fotografou.
	///
	/// O NUMERO NAO SIGNIFICA NADA SOZINHO. E um indice numa paleta por mapa, criada na ordem em
	/// que os tiles apareceram. So a IGUALDADE importa: "esta celula e o mesmo desenho daquela?".
	/// Comparar entre mapas diferentes nao quer dizer nada.
	///
	/// ZERO E RESERVADO pra "celula cega sem tile" -- a borda do mundo (`/turf/Other/Blank`), que
	/// e densa e nao tem icone. Sao mais de um milhao de celulas assim nos 40 mapas, e sem um id
	/// proprio elas se juntariam a qualquer parede vizinha.
	/// =======================================================================
	/// </summary>
	private readonly byte[]? _grupo;

	private ZoneCollision(int w, int h, byte[] bits, byte[]? grupo)
	{
		Width = w; Height = h; _bits = bits; _grupo = grupo;
	}

	/// <summary>
	/// Cabecalho "JCOL" + uint16 largura + uint16 altura + bitset em ordem de linha, e OPCIONALMENTE
	/// um plano de 1 byte por celula logo depois.
	///
	/// O PLANO E OPCIONAL DE PROPOSITO: o `.col` (que o servidor le) nao o carrega, e um `.vis`
	/// antigo continua abrindo. Quem nao tiver o plano simplesmente nao tem a regra de tile -- a
	/// sombra volta a tratar cega como cega, que era o comportamento anterior.
	/// </summary>
	public static ZoneCollision? Load(byte[] data)
	{
		if (data.Length < 8 || data[0] != 'J' || data[1] != 'C' || data[2] != 'O' || data[3] != 'L')
			return null;
		int w = data[4] | (data[5] << 8);
		int h = data[6] | (data[7] << 8);
		int precisa = (w * h + 7) / 8;
		if (w <= 0 || h <= 0 || data.Length < 8 + precisa) return null;

		var bits = new byte[precisa];
		Array.Copy(data, 8, bits, 0, precisa);

		byte[]? grupo = null;
		if (data.Length >= 8 + precisa + w * h)
		{
			grupo = new byte[w * h];
			Array.Copy(data, 8 + precisa, grupo, 0, w * h);
		}
		return new ZoneCollision(w, h, bits, grupo);
	}

	/// <summary>
	/// Monta em MEMORIA (o planeta procedural nao tem arquivo). O <paramref name="grupo"/> pode ser
	/// nulo; se vier, tem que ter exatamente w*h bytes.
	/// </summary>
	public static ZoneCollision Montar(int w, int h, byte[] bits, byte[]? grupo = null) =>
		new(w, h, bits, grupo != null && grupo.Length == w * h ? grupo : null);

	/// <summary>
	/// O grupo visual desta celula. 255 = nao sei (arquivo sem plano) -- e um valor que NUNCA
	/// casa consigo mesmo no teste de continuidade, entao "sem plano" degrada pra regra antiga em
	/// vez de juntar tudo num bloco so.
	/// </summary>
	public byte Grupo(int cx, int cy)
	{
		if (_grupo == null) return SemGrupo;
		if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return BordaDoMundo;
		return _grupo[cy * Width + cx];
	}

	/// <summary>O plano de identidade existe neste mapa?</summary>
	public bool TemGrupos => _grupo != null;

	/// <summary>Celula cega sem tile -- a borda do mundo.</summary>
	public const byte BordaDoMundo = 0;

	/// <summary>"Nao sei": arquivo sem o plano. Ver <see cref="Grupo"/>.</summary>
	public const byte SemGrupo = 255;

	/// <summary>
	/// Quantas celulas da beirada sao INTOCAVEIS.
	///
	/// Duas, e nao uma: a destruicao trabalha em `view(1)` -- as nove celulas em volta do impacto --
	/// entao um golpe na penultima coluna ja alcanca a ultima. Com margem de 1 daria pra abrir a
	/// borda batendo colado nela.
	/// </summary>
	public const int MargemDaBorda = 2;

	/// <summary>
	/// ESTA CELULA E BEIRADA DO MAPA?
	///
	/// ============================ POR QUE NAO BASTA O GRUPO ============================
	/// O plano do conversor marca `BordaDoMundo` so nas celulas DENSAS E CEGAS -- o `/turf/Other/Blank`
	/// que cerca os mapas. Um mapa que termina em AGUA ou AREIA nao tem nada marcado na ultima
	/// coluna, e o `RacharChao` transformava a beirada do oceano em terra batida. Foi o que o dono
	/// fotografou: o personagem no limite do mundo, com uma mancha de chao quebrado ao lado.
	///
	/// Isto aqui nao depende de plano nenhum: e aritmetica com a largura e a altura, e vale em toda
	/// zona -- inclusive nas geradas por semente, que nao tem plano de grupo.
	/// ==================================================================================
	/// </summary>
	public bool NaBorda(int cx, int cy, int margem = MargemDaBorda) =>
		cx < margem || cy < margem || cx >= Width - margem || cy >= Height - margem;

	/// <summary>A mesma pergunta, em pixels.</summary>
	public bool NaBordaEm(Vec2 pos, int margem = MargemDaBorda) =>
		NaBorda((int)MathF.Floor(pos.X / TileSize), (int)MathF.Floor(pos.Y / TileSize), margem);

	/// <summary>
	/// CELULAS ABERTAS EM RUNTIME. Hoje so as portas usam isto.
	///
	/// ============================ POR QUE PRECISA EXISTIR ============================
	/// O `.col` e o `.vis` sao IMUTAVEIS e cacheados por sessao (`World.MapaCacheado` le cada um
	/// UMA vez, e o servidor guarda o mesmo objeto em `ZoneEntry.Mapa`). Uma porta que "abre"
	/// limpando o bit direto no bitset abriria pra sempre: o dado nunca mais e relido, entao
	/// sair do planeta e voltar traria a porta ainda escancarada.
	///
	/// Aqui a alteracao mora numa camada SEPARADA do dado do disco. Fechar e tirar da camada, e
	/// <see cref="FecharTudo"/> devolve o mapa ao estado de arquivo -- que e o que o cliente
	/// chama ao entrar numa zona, antes de aplicar a lista de portas abertas que o servidor
	/// mandou. Assim o que vale e sempre o estado do SERVIDOR, e nunca o resto da visita passada.
	/// =================================================================================
	/// </summary>
	private HashSet<int>? _abertas;

	/// <summary>Esta celula deixa de bloquear (e de cegar, se este for o mapa de visao).</summary>
	public void Abrir(int cx, int cy)
	{
		if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return;
		(_abertas ??= []).Add(cy * Width + cx);
	}

	/// <summary>Volta a valer o que o arquivo diz desta celula.</summary>
	public void Fechar(int cx, int cy)
	{
		if (_abertas == null || cx < 0 || cy < 0 || cx >= Width || cy >= Height) return;
		_abertas.Remove(cy * Width + cx);
	}

	/// <summary>Todas as celulas voltam ao estado do arquivo.</summary>
	public void FecharTudo() => _abertas = null;

	/// <summary>
	/// CELULAS BLOQUEADAS EM RUNTIME -- hoje as construcoes que os jogadores erguem.
	///
	/// O simetrico das aberturas, e pelo mesmo motivo: o arquivo nao muda, e uma bancada erguida
	/// depois do boot nao existe nele. Fica numa camada por cima e some com <see cref="LimparObras"/>,
	/// que e o que o cliente chama ao entrar na zona antes de aplicar a lista do servidor.
	///
	/// AS DUAS CAMADAS NAO SE CRUZAM, e por construcao: a abertura so e consultada em celula que o
	/// ARQUIVO ja dizia ser parede, e a construcao so em celula que o arquivo dizia ser chao. Uma
	/// obra sobre uma porta nao existe -- pra erguer algo e preciso ESTAR no lugar, e no lugar de
	/// uma porta fechada nao se esta. Cada consulta paga um teste so.
	/// </summary>
	private HashSet<int>? _obras;

	/// <summary>Esta celula passa a bloquear, mesmo que o arquivo diga que e chao.</summary>
	public void Bloquear(int cx, int cy)
	{
		if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return;
		(_obras ??= []).Add(cy * Width + cx);
	}

	/// <summary>Todas as construcoes somem: volta a valer o arquivo (mais as aberturas).</summary>
	public void LimparObras() => _obras = null;

	/// <summary>Esta celula esta aberta por cima do arquivo?</summary>
	public bool Aberta(int cx, int cy) =>
		_abertas != null && cx >= 0 && cy >= 0 && cx < Width && cy < Height
		&& _abertas.Contains(cy * Width + cx);

	public bool BlockedCell(int cx, int cy)
	{
		// fora do mapa conta como parede: ninguem sai pela borda
		if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return true;
		int i = cy * Width + cx;
		// O CAMINHO COMUM NAO PAGA QUASE NADA: em chao livre e sem construcao nenhuma na zona, isto
		// e um teste de bit e uma comparacao com nulo. O campo de visao chama este metodo centenas
		// de milhares de vezes por quadro.
		if ((_bits[i >> 3] & (1 << (i & 7))) == 0)
			return _obras != null && _obras.Contains(i);
		return _abertas == null || !_abertas.Contains(i);
	}

	public bool BlockedAt(Vec2 pos) =>
		BlockedCell((int)MathF.Floor(pos.X / TileSize), (int)MathF.Floor(pos.Y / TileSize));

	/// <summary>
	/// O PONTO LIVRE MAIS PROXIMO DE UM DESEJADO -- o "onde da pra por um corpo aqui".
	///
	/// ============================ E ISTO FALTAVA, E O DEFEITO JA ESTAVA EM JOGO ============================
	/// O servidor tem UM ponto de chegada pra todo mapa pre-feito: o (249,250) do BYOND
	/// (`locate(rand(240,260),rand(240,260),1)`), que e o campo aberto do meio da TERRA. Ele foi
	/// escrito pra a Terra e passou a ser usado em todo pouso pre-feito -- e em **Icer o (249,250) e
	/// PAREDE** (medido no proprio `z04_Icer.col`: 309 das 441 celulas do quadrado 21x21 em volta
	/// sao densas). Ou seja, quem pousava em Icer chegava dentro da rocha; so ninguem tinha reparado
	/// porque nenhuma regra do jogo mandava alguem pra la. O berco manda -- e o Frost Demon nasce em
	/// Icer --, entao o buraco deixa de ser teorico.
	///
	/// A alternativa era extrair os 14 `/obj/SpawnPoint` do `.dmm` e ter uma coordenada por planeta.
	/// Nao foi por dois motivos: seria dado NOVO pra manter em sincronia com mapas que mudam, e nao
	/// resolveria o caso do mapa cujo spawnpoint cai numa construcao levantada por um jogador (a
	/// colisao tem `Bloquear`/`Abrir` em runtime). Perguntar a colisao responde as duas coisas.
	/// ==================================================================================================
	///
	/// Anel por anel a partir do desejado, e devolve o CENTRO do tile: um ponto na quina de uma
	/// celula livre encostada em parede ja nasce em violacao pro `MoveRules`.
	///
	/// Custo: uma vez por nascimento/morte, e o primeiro anel resolve em todo mapa menos um. O pior
	/// caso medido (Icer) para no anel 8 -- 289 celulas olhadas, microssegundos. Nada disto encosta
	/// no tique de ninguem.
	/// </summary>
	/// <param name="raioMax">
	/// Teto de aneis. 64 tiles cobre metade de um mapa de 500 -- se nao houver chao livre a essa
	/// distancia, o mapa e que esta errado, e devolver o desejado deixa o defeito VISIVEL (o corpo
	/// preso na pedra) em vez de escondido atras de uma busca que varre o mundo inteiro.
	/// </param>
	public Vec2 PontoLivrePerto(Vec2 desejado, int raioMax = 64)
	{
		int cx = (int)MathF.Floor(desejado.X / TileSize);
		int cy = (int)MathF.Floor(desejado.Y / TileSize);

		if (Serve(cx, cy)) return desejado;

		for (int r = 1; r <= raioMax; r++)
		{
			// So a BORDA do quadrado de raio r: o miolo ja foi olhado nos aneis anteriores.
			for (int dx = -r; dx <= r; dx++)
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					if (Serve(cx + dx, cy + dy))
						return new Vec2((cx + dx) * TileSize + TileSize / 2f,
										(cy + dy) * TileSize + TileSize / 2f);
				}
		}

		return desejado;

		// A BEIRADA NAO SERVE DE CHAO. `NaBorda` e o que impede sair do mapa (o `MoveRules` recusa
		// o passo la), entao um corpo posto na beirada nasceria numa celula de onde ele nao pode se
		// mover -- livre pela colisao e presa pela regra.
		bool Serve(int x, int y) => !BlockedCell(x, y) && !NaBorda(x, y);
	}

	/// <summary>
	/// O caminho de <paramref name="from"/> ate <paramref name="to"/> passa por parede?
	///
	/// Amostra o segmento a cada meio tile. Nao e um raycast exato de proposito: o objetivo
	/// e pegar quem ATRAVESSA parede, nao disputar o pixel com a fisica do cliente -- e uma
	/// checagem cara demais rodaria 30x por segundo por jogador.
	/// </summary>
	public bool PathBlocked(Vec2 from, Vec2 to)
	{
		Vec2 d = to - from;
		float dist = d.Length;
		if (dist < 0.01f) return BlockedAt(to);

		int passos = (int)MathF.Ceiling(dist / (TileSize * 0.5f));
		for (int i = 1; i <= passos; i++)
		{
			Vec2 p = from + d * (i / (float)passos);
			if (BlockedAt(p)) return true;
		}
		return false;
	}
}
