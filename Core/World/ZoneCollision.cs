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

	/// <summary>
	/// FORA DO BITSET E CHAO LIVRE, E NAO O FIM DO MUNDO.
	///
	/// ============================ POR QUE ISTO E UM CAMPO E NAO UM CASO ============================
	/// O bitset tem tamanho fixo (o mapa desenhado: 500x500), e a regra padrao e que fora dele tudo
	/// bloqueia -- e isso NAO e uma consequencia acidental, e o que impede sair do mapa. A Sala do
	/// Tempo inverte a regra: o quarto desenhado fica no meio de um vazio branco que continua pra
	/// sempre (ver <see cref="SalaDoTempo"/>), e ali a beirada do bitset nao significa nada.
	///
	/// O campo mora AQUI, e nao num `if` em cada chamador, porque "onde ha parede" ja tem um funil
	/// so -- <see cref="BlockedCell"/> -- e ele e consultado por caminhos que nunca vao ouvir falar
	/// da Sala: o passo do `MoveRules` nas duas pontas, o raio do `PathBlocked`, o `PontoLivrePerto`
	/// do nascimento, o DDA da sombra. Espalhar a excecao seria pedir pra um deles ficar de fora, e
	/// o sintoma de UM ficar de fora e o pior deste projeto: o cliente e o servidor discordando
	/// sobre onde da pra andar, com o corpo tremendo na costura.
	///
	/// ELE MUDA DUAS RESPOSTAS, e as duas precisam mudar juntas:
	///   * <see cref="BlockedCell"/> -- fora do bitset passa a ser livre;
	///   * <see cref="NaBorda"/> -- deixa de existir beirada, e com ela o chao indestrutivel da
	///     margem e a recusa que faria o corpo parar dois tiles antes do fim do mapa.
	/// Mudar so a primeira deixaria o jogador andando pelo vazio com uma cerca invisivel de duas
	/// celulas em volta do quarto, imposta pela regra que existe pra proteger a borda de um mapa
	/// que aqui nao tem borda.
	/// ==============================================================================================
	///
	/// QUEM LIGA: quem CARREGA o mapa, nas duas pontas (o `CarregarZonas` do servidor e o
	/// `MapaCacheado` do cliente), perguntando ao <see cref="SalaDoTempo.SemBorda(string)"/>. O
	/// arquivo nao carrega o bit porque ele nao e do desenho do mapa, e sim do lugar: o mesmo `.col`
	/// de 500x500 descreve a Sala e descreveria qualquer quarto fechado.
	/// </summary>
	public bool SemBorda { get; set; }

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

	/// <summary>
	/// O PLANO DA AGUA -- 1 bit por celula, ou nulo se esta zona nao tiver agua nenhuma.
	///
	/// ============================ POR QUE UM PLANO E NAO UM BIT DO `_bits` ============================
	/// Porque `_bits` significa quatro coisas ao mesmo tempo -- "para o corpo", "da pra socar", "a IA
	/// contorna", "nao da pra nascer aqui" -- e a agua so quer a primeira, e ainda assim so pra quem
	/// esta a pe. Ver <see cref="ClasseDeAgua"/>, onde as cinco respostas dela moram juntas.
	///
	/// MORA NO MESMO OBJETO que o bitset, e nao numa tabela ao lado, pelo mesmo motivo que o
	/// `_abertas` das portas mora aqui: "onde da pra andar" ja tem um funil so, e espalhar a
	/// pergunta e pedir pra um chamador ficar de fora -- com o cliente e o servidor discordando
	/// sobre onde se anda, que e o pior sintoma deste projeto.
	///
	/// DE ONDE VEM: nos mapas pre-feitos, do arquivo `.agua` gravado ao lado do `.col` (ver
	/// <see cref="CarregarAgua"/> -- arquivo SEPARADO de proposito, pra que um `.col` antigo
	/// continue abrindo e pra que o servidor nao pague o plano de quem nao tem agua). Nos planetas
	/// gerados, do proprio `ClasseDeTerreno.Agua` do <see cref="GeradorDeTerreno"/>.
	/// ================================================================================================
	/// </summary>
	private byte[]? _agua;

	/// <summary>
	/// O PLANO DO QUE NAO SE QUEBRA -- 1 bit por celula, ou nulo se esta zona nao tiver nada duro.
	///
	/// ============================ ELE E O `destroyable` DO ORIGINAL, E FALTAVA ============================
	/// No DM `turf/proc/Destroy()` (`Modules/Turfs/NewTurfs.dm:2-4`) abre com `if(src.destroyable)` --
	/// um funil unico por onde passa TODA destruicao de cenario. O padrao e 1 (`Turfs.dm:199`), e
	/// quem desliga sao a borda do mundo (`/turf/Other/Blank`), os teleportes, a arena, o `Void_Wall`
	/// e o espaco.
	///
	/// O port aproximava isso pelo anel de duas celulas do <see cref="NaBorda"/>. A aproximacao e
	/// boa pra beirada de mapa e nao vale nada pro MIOLO: o Templo tem costuras de `Blank` em
	/// x=274, x=434, y=326 e y=54 -- centenas de tiles longe de qualquer margem. 1.690 celulas que
	/// no BYOND nao cedem nunca caiam aqui com poeira, som e a celula virando terra batida.
	/// Era a queixa do dono: *"eu soco, ele QUEBRA e faz TODOS OS EFEITOS, mas nao tinha nada la"*.
	/// ======================================================================================================
	///
	/// ============================ POR QUE UM PLANO, E NAO UM BIT DO `_bits` ============================
	/// Pelo mesmo motivo da agua, e a nota dela vale palavra por palavra: `_bits` ja significa quatro
	/// coisas ao mesmo tempo, e "e indestrutivel" nao muda NENHUMA delas. Uma parede dura continua
	/// parando o corpo, continuando cegando e continuando sendo parede pro caminho -- a unica
	/// pergunta que ela responde diferente e a do <see cref="Jandirus.Core.World.ZoneCollision"/>
	/// que ninguem mais faz: "isto pode cair?".
	///
	/// MORA NO MESMO OBJETO pelo motivo de sempre neste sistema: "o que da pra fazer com esta celula"
	/// tem um funil so, e espalhar a pergunta e pedir pra um chamador ficar de fora.
	/// ==================================================================================================
	///
	/// DE ONDE VEM: do arquivo `.duro` gravado ao lado do `.col` -- arquivo SEPARADO pelo mesmo
	/// motivo que o `.agua` (a cauda do `.col` ja tem dono: o plano de grupo da sombra), e ausente
	/// significa "nada duro nesta zona", que e a verdade nas zonas geradas por semente.
	/// </summary>
	private byte[]? _duro;

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

	// =====================================================================
	// A AGUA -- a terceira classe de celula. Ver ClasseDeAgua.
	// =====================================================================

	/// <summary>
	/// O bitset da agua, cru (mesma ordem de linha do `_bits`). Devolve false se o tamanho nao
	/// bater -- calar aqui deixaria a zona sem agua nenhuma sem ninguem saber por que.
	/// </summary>
	public bool DefinirAgua(byte[]? bits)
	{
		if (bits == null) { _agua = null; return true; }
		if (bits.Length != (Width * Height + 7) / 8) return false;
		_agua = bits;
		return true;
	}

	/// <summary>
	/// Le o `.agua` -- MESMO cabecalho do `.col` ("JCOL" + largura + altura + bitset), de
	/// proposito: e o mesmo formato, so que respondendo outra pergunta, e reusar o cabecalho
	/// evita uma segunda serializacao pra manter em dia.
	///
	/// E um arquivo SEPARADO e nao uma cauda do `.col` porque a cauda do `.col` ja tem dono: o
	/// <see cref="Load"/> le qualquer coisa depois do bitset como o plano de GRUPO da sombra.
	/// Anexar agua ali faria todo lago virar identidade de tile.
	///
	/// Devolve false (e nao lanca) quando o arquivo nao existe, nao e JCOL, ou descreve um mapa de
	/// outro tamanho -- os tres casos em que a resposta honesta e "esta zona nao tem agua marcada".
	/// </summary>
	public bool CarregarAgua(byte[]? data)
	{
		if (data == null || data.Length < 8) return false;
		if (data[0] != 'J' || data[1] != 'C' || data[2] != 'O' || data[3] != 'L') return false;
		int w = data[4] | (data[5] << 8);
		int h = data[6] | (data[7] << 8);
		if (w != Width || h != Height) return false;

		int precisa = (w * h + 7) / 8;
		if (data.Length < 8 + precisa) return false;

		var bits = new byte[precisa];
		Array.Copy(data, 8, bits, 0, precisa);
		_agua = bits;
		return true;
	}

	/// <summary>Esta zona tem agua marcada?</summary>
	public bool TemAgua => _agua != null;

	// =====================================================================
	// A NUVEM -- a quarta classe de celula (ver Core/World/Ceu.cs)
	// =====================================================================

	/// <summary>
	/// O PLANO DA NUVEM -- 1 bit por celula, ou nulo se esta zona nao tiver ceu.
	///
	/// Arquivo SEPARADO (`.ceu`) pelo mesmo motivo do `.agua` e do `.duro`, e a nota deles vale
	/// palavra por palavra: a cauda do `.col` ja tem dono (o plano de grupo da sombra), e "isto e
	/// nuvem" nao muda nenhuma das quatro perguntas que o `_bits` ja responde.
	/// </summary>
	private byte[]? _nuvem;

	/// <summary>
	/// O CEU DESTA ZONA DERRUBA, OU SO BARRA? -- ver <see cref="ClasseDeNuvem.DestinoDaQueda"/>.
	///
	/// ============================ POR QUE ELE MORA AQUI, JUNTO DO PLANO ============================
	/// A resposta e da ZONA e o <see cref="ZoneCollision"/> **e** a zona -- ele ja guarda o plano, a
	/// borda e as portas abertas dela. A alternativa era cada chamador de <see cref="Bloqueia"/>
	/// carregar o nome da zona ate la dentro pra perguntar ao Core; sao quatro chamadores em arquivos
	/// diferentes (o passo do cliente, a validacao do servidor, a IA e o arremesso -- a mesma lista do
	/// `ClasseDeAgua.ModoDe`), e o sintoma de UM ficar pra tras e o pior deste projeto: o cliente e o
	/// servidor discordando sobre onde da pra andar. Aqui a zona responde por si.
	///
	/// **QUEM O ESCREVE E O PROPRIO `CarregarNuvem`, a partir do NOME** -- e nao um `bool` que o
	/// chamador passa. Ver o cabecalho de la: e o que impede os dois carregadores (cliente e
	/// servidor) de responderem diferente pra mesma zona.
	/// </summary>
	private bool _nuvemDerruba;

	/// <summary>
	/// Le o `.ceu` -- MESMO cabecalho do `.col`, do `.agua` e do `.duro` ("JCOL" + largura + altura +
	/// bitset), e pelo mesmo motivo: e o mesmo formato respondendo outra pergunta.
	///
	/// ============================ O NOME DA ZONA E PARAMETRO, E ISSO E DELIBERADO ============================
	/// A assinatura obvia era `CarregarNuvem(byte[] data, bool derruba)` -- e ela poe a mesma decisao em
	/// dois lugares. Sao DOIS carregadores (`Client/World.cs` e `Server/GameServer.cs`, os mesmos que
	/// chamam o <see cref="CarregarAgua"/>), e no dia em que uma zona nova ganhasse destino, um dos
	/// dois ficaria pra tras. O sintoma seria calado e feio: uma nuvem que deixa entrar num lado e
	/// barra no outro, ou seja o jogador ANDANDO NO CEU com o servidor puxando ele de volta.
	///
	/// Pedindo o NOME, os dois chamadores passam o que ja tem na mao e a derivacao acontece uma vez
	/// so, aqui, pelo <see cref="ClasseDeNuvem.Derruba"/>. Nao ha o que sincronizar.
	/// ======================================================================================================
	///
	/// Devolve false (e nao lanca) quando o arquivo nao existe, nao e JCOL, ou descreve um mapa de
	/// outro tamanho -- os tres casos em que a resposta honesta e "esta zona nao tem ceu marcado".
	/// </summary>
	public bool CarregarNuvem(byte[]? data, string nomeDaZona)
	{
		if (data == null || data.Length < 8) return false;
		if (data[0] != 'J' || data[1] != 'C' || data[2] != 'O' || data[3] != 'L') return false;
		int w = data[4] | (data[5] << 8);
		int h = data[6] | (data[7] << 8);
		if (w != Width || h != Height) return false;

		int precisa = (w * h + 7) / 8;
		if (data.Length < 8 + precisa) return false;

		var bits = new byte[precisa];
		Array.Copy(data, 8, bits, 0, precisa);
		_nuvem = bits;
		_nuvemDerruba = ClasseDeNuvem.Derruba(nomeDaZona);
		return true;
	}

	/// <summary>Esta zona tem nuvem marcada?</summary>
	public bool TemNuvem => _nuvem != null;

	/// <summary>
	/// O CEU DESTA ZONA DERRUBA? Publico pra bancada e pro servidor, que precisa saber se ha queda
	/// pra despachar antes de ir procurar destino.
	/// </summary>
	public bool NuvemDerruba => _nuvemDerruba;

	/// <summary>
	/// ESTA CELULA E NUVEM?
	///
	/// FORA DO MAPA NAO E NUVEM, pelo mesmo motivo do <see cref="EhAgua"/>: quem responde pelo vazio
	/// e o <see cref="BlockedCell"/>, e devolver "ceu" aqui faria a borda do mundo derrubar todo mundo
	/// que a encostasse no Alem.
	/// </summary>
	public bool EhNuvem(int cx, int cy)
	{
		if (_nuvem == null) return false;
		if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return false;
		int i = cy * Width + cx;
		return (_nuvem[i >> 3] & (1 << (i & 7))) != 0;
	}

	/// <summary>A mesma pergunta, em pixels.</summary>
	public bool EhNuvemEm(Vec2 pos) =>
		EhNuvem((int)MathF.Floor(pos.X / TileSize), (int)MathF.Floor(pos.Y / TileSize));

	/// <summary>
	/// ESTA CELULA E AGUA?
	///
	/// FORA DO MAPA NAO E AGUA -- e o vazio, e quem responde por ele e o <see cref="BlockedCell"/>
	/// (parede, ou chao livre na Sala do Tempo). Devolver "agua" aqui faria o mundo inteiro fora do
	/// bitset virar um oceano pra quem nada.
	/// </summary>
	public bool EhAgua(int cx, int cy)
	{
		if (_agua == null) return false;
		if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return false;
		int i = cy * Width + cx;
		return (_agua[i >> 3] & (1 << (i & 7))) != 0;
	}

	/// <summary>A mesma pergunta, em pixels.</summary>
	public bool EhAguaEm(Vec2 pos) =>
		EhAgua((int)MathF.Floor(pos.X / TileSize), (int)MathF.Floor(pos.Y / TileSize));

	// =====================================================================
	// O QUE NAO SE QUEBRA -- o `destroyable` do original. Ver `_duro`.
	// =====================================================================

	/// <summary>
	/// Le o `.duro` -- MESMO cabecalho do `.col` e do `.agua` ("JCOL" + largura + altura + bitset),
	/// e pelo mesmo motivo: e o mesmo formato respondendo outra pergunta, e uma terceira
	/// serializacao seria uma terceira coisa pra manter em dia.
	///
	/// Devolve false (e nao lanca) quando o arquivo nao existe, nao e JCOL, ou descreve um mapa de
	/// outro tamanho -- os tres casos em que a resposta honesta e "esta zona nao tem nada marcado".
	/// </summary>
	public bool CarregarDuro(byte[]? data)
	{
		if (data == null || data.Length < 8) return false;
		if (data[0] != 'J' || data[1] != 'C' || data[2] != 'O' || data[3] != 'L') return false;
		int w = data[4] | (data[5] << 8);
		int h = data[6] | (data[7] << 8);
		if (w != Width || h != Height) return false;

		int precisa = (w * h + 7) / 8;
		if (data.Length < 8 + precisa) return false;

		var bits = new byte[precisa];
		Array.Copy(data, 8, bits, 0, precisa);
		_duro = bits;
		return true;
	}

	/// <summary>O bitset cru, pra quem monta em memoria (o planeta gerado). Ver <see cref="DefinirAgua"/>.</summary>
	public bool DefinirDuro(byte[]? bits)
	{
		if (bits == null) { _duro = null; return true; }
		if (bits.Length != (Width * Height + 7) / 8) return false;
		_duro = bits;
		return true;
	}

	/// <summary>Esta zona tem alguma celula marcada como indestrutivel?</summary>
	public bool TemDuro => _duro != null;

	/// <summary>
	/// ESTA CELULA NAO PODE SER QUEBRADA?
	///
	/// FORA DO MAPA E DURO, e isso nao e detalhe: o lado de fora do bitset e a borda do mundo, e o
	/// unico jeito de encostar nele e o `SemBorda` da Sala do Tempo -- onde fora do bitset e chao
	/// livre e nao ha o que quebrar de qualquer forma. Responder "da pra quebrar" ali seria abrir
	/// permissao pra derrubar o vazio.
	/// </summary>
	public bool Indestrutivel(int cx, int cy)
	{
		if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return !SemBorda;
		if (_duro == null) return false;
		int i = cy * Width + cx;
		return (_duro[i >> 3] & (1 << (i & 7))) != 0;
	}

	/// <summary>
	/// ESTA CELULA PARA ESTE CORPO? -- parede E agua, na mesma pergunta.
	///
	/// E o metodo que o movimento consulta (via `MoveRules.Occupied`). O <see cref="BlockedCell"/>
	/// de uma so pergunta continua existindo e continua significando **parede**, porque e dele que
	/// dependem o soco em cenario, o raio da voz e o `NaBorda` -- coisas em que a agua nao entra.
	///
	/// O CAMINHO COMUM CONTINUA BARATO: numa zona sem agua o `_agua` e nulo e isto e um teste de
	/// referencia a mais por celula.
	///
	/// ============================ E A NUVEM ENTROU AQUI, MAS SO A QUE **BARRA** ============================
	/// A nuvem que DERRUBA nao bloqueia nada, e isso nao e esquecimento -- e a regra. Ver
	/// <see cref="ClasseDeNuvem.Bloqueia"/>: quem cai precisa ENTRAR na celula, porque e a entrada que
	/// dispara a queda (no DM, literalmente o corpo do `Enter()`). Uma nuvem que barrasse E derrubasse
	/// nunca derrubaria ninguem -- o corpo pararia na beirada e o servidor jamais veria o pe dele em
	/// cima do ceu.
	///
	/// Entao o Ceu e o Reino dos Deuses ganham parede de nuvem (que e o `Enter()` deles) e o Caminho
	/// da Serpente e o Templo ficam abertos (que e o `Enter()` deles). Uma linha, os dois desfechos.
	/// ======================================================================================================
	/// </summary>
	public bool Bloqueia(int cx, int cy, ModoDeTravessia modo) =>
		BlockedCell(cx, cy)
		|| (ClasseDeAgua.Bloqueia(modo) && EhAgua(cx, cy))
		|| (ClasseDeNuvem.Bloqueia(modo, _nuvemDerruba) && EhNuvem(cx, cy));

	/// <summary>A mesma pergunta, em pixels.</summary>
	public bool BloqueiaEm(Vec2 pos, ModoDeTravessia modo) =>
		Bloqueia((int)MathF.Floor(pos.X / TileSize), (int)MathF.Floor(pos.Y / TileSize), modo);

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
		!SemBorda
		&& (cx < margem || cy < margem || cx >= Width - margem || cy >= Height - margem);

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
		// fora do mapa conta como parede: ninguem sai pela borda -- MENOS onde nao ha borda
		// (a Sala do Tempo, ver `SemBorda`), e ai fora do bitset e o vazio branco, que e chao.
		if (cx < 0 || cy < 0 || cx >= Width || cy >= Height) return !SemBorda;

		// ============================ E O ANEL DE BORDA DO .dmm TAMBEM NAO E PAREDE ============================
		// So abrir o lado de fora nao bastava, e isto so se descobre medindo o mapa. O `.dmm` cerca o
		// retangulo com `/turf/Other/Blank` -- denso e sem icone --, que e o jeito do BYOND de dizer
		// "o mapa acaba aqui". No z13 esse anel e a coluna x=499 INTEIRA: 500 das 668 celulas densas
		// da zona, e as 168 restantes sao as paredes do quarto desenhado (x 129..162, y 337..362).
		//
		// Sem esta clausula o vazio ficaria com uma parede invisivel de um tile a leste e nenhuma nos
		// outros tres lados -- o jogador andaria mil tiles pro norte e esbarraria no nada indo pro
		// leste. E o anel nao e geometria de lugar nenhum: ele existe porque o retangulo precisava
		// terminar, e aqui o retangulo nao termina.
		//
		// A LARGURA E A MESMA <see cref="MargemDaBorda"/> que ja protege a beirada da destruicao,
		// porque as duas falam da mesma coisa. As paredes de verdade do quarto estao a 336 tiles da
		// margem mais proxima -- nao ha o que confundir.
		// ====================================================================================================
		if (SemBorda && (cx < MargemDaBorda || cy < MargemDaBorda
						 || cx >= Width - MargemDaBorda || cy >= Height - MargemDaBorda))
			return false;

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

		if (ServeDeChao(cx, cy)) return desejado;

		for (int r = 1; r <= raioMax; r++)
		{
			// So a BORDA do quadrado de raio r: o miolo ja foi olhado nos aneis anteriores.
			for (int dx = -r; dx <= r; dx++)
				for (int dy = -r; dy <= r; dy++)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					if (ServeDeChao(cx + dx, cy + dy)) return CentroDaCelula(cx + dx, cy + dy);
				}
		}

		return desejado;
	}

	/// <summary>
	/// ============================ "DA PRA UM CORPO A PE FICAR NESTA CELULA?" ============================
	/// A pergunta que o <see cref="PontoLivrePerto"/> faz anel por anel, e ela e **PUBLICA porque tem
	/// um segundo consumidor**: quem nao quer o ponto mais proximo, e sim saber se um ponto QUE ELE JA
	/// SORTEOU serve (o embaralho dos habitantes -- `GameServer.Embaralho.cs`). Sem ela, esse chamador
	/// teria que recompor `!BlockedCell &amp;&amp; !NaBorda &amp;&amp; !agua` na mao, e seria a copia que nao soube
	/// da agua no dia em que a agua entrou -- que e exatamente o que este metodo existe pra impedir.
	///
	/// AS TRES RECUSAS, e nenhuma delas e a mesma coisa que as outras:
	///
	///   * PAREDE   -- o bitset do arquivo mais as camadas de runtime (obra levantada, porta fechada);
	///   * BEIRADA  -- livre pela colisao e presa pela regra: `NaBorda` e o que impede sair do mapa, e
	///     um corpo posto la nasceria numa celula de onde ele nao pode dar um passo;
	///   * AGUA     -- livre pela colisao tambem (agua nao e parede, nao esta no bitset) e parada pela
	///     regra de personagem. Sem esta clausula um Namekuseijin nasce no meio do oceano de Namek e
	///     um piloto pousa dentro de um lago. Ver <see cref="ClasseDeAgua.ServeDeChao"/>.
	///   * NUVEM    -- a quarta, e ela e a que o pedido do dono cobra por escrito (*"quem cai pela
	///     nuvem nao pode ficar preso"*). Vale pros DOIS ceus e nao so pro que barra: pousar numa
	///     nuvem que derruba nao prende o corpo, cospe ele pro Inferno no tique seguinte sem que ele
	///     tenha feito nada. Ver <see cref="ClasseDeNuvem.ServeDeChao"/>.
	///
	/// **E E POR ISTO QUE A QUEDA NAO PRECISA DE CUIDADO PROPRIO**: quem cai chega pelo
	/// `MoveToZone`, que passa pelo <see cref="PontoLivrePerto"/> da zona de destino -- e o destino
	/// do Templo e a Terra, que nao tem nuvem nenhuma. A clausula esta escrita pro dia em que alguem
	/// puser um destino DENTRO de uma zona com ceu, que e o unico jeito de isto morder.
	/// ================================================================================================
	/// </summary>
	public bool ServeDeChao(int cx, int cy) =>
		!BlockedCell(cx, cy) && !NaBorda(cx, cy)
		&& !(EhAgua(cx, cy) && !ClasseDeAgua.ServeDeChao)
		&& !(EhNuvem(cx, cy) && !ClasseDeNuvem.ServeDeChao);

	/// <summary>O CENTRO da celula -- ver a nota de quina do <see cref="PontoLivrePerto"/>.</summary>
	public Vec2 CentroDaCelula(int cx, int cy) =>
		new(cx * TileSize + TileSize / 2f, cy * TileSize + TileSize / 2f);

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
