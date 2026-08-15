using Jandirus.Core.Combat;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS JANELAS DA BANCADA DA BOCA DO CANO (`--diagboca`, `Client/RoboDeBocaDeCano.cs`).
///
/// ============================ POR QUE ELA PRECISA DE JANELA NO SERVIDOR ============================
/// A queixa do dono veio EM FOTO -- *"os beams tao saindo DE CIMA do personagem, deveriam sair DA
/// FRENTE dele, NA FRENTE DO SPRITE deles"* -- e as duas metades dela sao de TELA: de onde a tinta
/// comeca, e quem fica por cima de quem. A familia 1-bis da `--projetilteste` ja mede a primeira em
/// NUMERO (projecao no rumo, deriva transversal, os quatro sentidos), e ela **ficaria verde com o
/// desenho errado**: o `Pos` do servidor pode nascer no lugar certo e o cliente carimbar o quadro em
/// outro (foi o que aconteceu com a ALTURA -- o campo existia, viajava pela colisao e nao chegava ao
/// desenho). Da camada ela nao sabe nada: `ZIndex` nao e um campo do servidor.
///
/// E pra fotografar, a cena tem que ACONTECER: quem tem corpo, projetil e altitude e o servidor.
/// Todas as cenas daqui nascem pela porta de producao -- o tiro pelo <see cref="Disparar"/>, o corpo
/// pelo `PorNoMundo` (via <see cref="ForjarCorpoDeFoto"/>), o voo pelo `AlternarVoo`.
/// ==============================================================================================
///
/// ============================ OS DEFEITOS SAO INJETADOS PELO PARAMETRO DE PRODUCAO ============================
/// Nenhuma linha de producao ganhou um `if (bancada)`. O <see cref="Disparar"/> ja tem o `deOnde` --
/// o parametro que a Hellzone e o Ki Minefield usam pra nascer em volta do ALVO --, e e por ele que
/// os dois defeitos entram: `NoUmbigo` nasce o tiro em `pl.Pos` (o estado de ANTES do conserto) e
/// `DoisTilesAFrente` o nasce alem da vitima colada (pra a prova de colisao poder ficar vermelha).
///
/// A CAUDA PRECISA DA SEGUNDA METADE. Num raio canalizado quem fica perto do corpo nao e a cabeca, e
/// a cauda -- e ela e reescrita a cada tique (`GameServer.Projeteis.cs`, o passo do canal). Sem
/// desfazer TAMBEM essa linha, o defeito injetado durava um tique e a foto sairia igual a boa: ver
/// <see cref="EmpurrarACaudaParaOUmbigo"/>.
/// ========================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O DEFEITO INJETADO NO NASCIMENTO. `Nenhum` e producao -- os outros dois existem pra a bancada
	/// poder provar que sabe ficar vermelha.
	/// </summary>
	internal enum DefeitoDaBoca
	{
		/// <summary>Producao: `BocaDeCano.De(pl.Pos, rumo)`.</summary>
		Nenhum,

		/// <summary>
		/// O ESTADO DE ANTES DO CONSERTO: o tiro nasce em `pl.Pos`, o quadro de 32x32 carimbado
		/// centrado no centro do sprite. E a foto que o dono mandou.
		/// </summary>
		NoUmbigo,

		/// <summary>
		/// UM TILE ALEM DA BOCA -- o tiro nasce depois de quem esta colado. Nao e um bug que existiu:
		/// e o CONTROLE da prova de colisao, o unico jeito de "o tiro colado acerta" poder reprovar.
		/// </summary>
		DoisTilesAFrente,
	}

	/// <summary>O tiro que esta bancada plantou (0 = nenhum), e o dono dele.</summary>
	private int _bocaTiro, _bocaDono;

	/// <summary>Onde o corpo tem que estar em toda foto -- ver <see cref="ReporNaBoca"/>.</summary>
	private Vec2 _bocaAncora;

	/// <summary>
	/// UMA PRACA COM OS QUATRO LADOS LIVRES, e o corpo posto no meio dela.
	///
	/// ============================ UM CORREDOR NAO SERVE, E A BANCADA IRMA JA PAGOU ISSO ============================
	/// Esta bancada atira pros QUATRO sentidos a partir do MESMO ponto: a foto so compara os quatro se
	/// o fundo for o mesmo e se o tiro tiver, nos quatro, o mesmo espaco pra andar. A familia 1-bis da
	/// `--projetilteste` reprovou duas vezes por isto -- media a pedra que estava ao norte e a oeste do
	/// corredor -- e a resposta la foi a mesma: procurar uma PRACA.
	///
	/// A busca e em espiral a partir de onde o corpo esta, e o raio dela tem teto: uma praca a
	/// quinhentos tiles daqui seria outro pedaco de mundo, com outra luz e outro chao.
	/// ==========================================================================================================
	/// </summary>
	/// <param name="tiles">quantos tiles de chao livre cada um dos quatro lados precisa ter.</param>
	/// <returns>verdadeiro se achou (e o corpo ja esta la).</returns>
	internal bool AssentarNaBoca(int id, int tiles)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (MapaDaZonaOuCatalogo(pl.Zone) is not { } mapa) return false;

		const int T = ZoneCollision.TileSize;
		int cx = (int)Math.Floor(pl.Pos.X / T), cy = (int)Math.Floor(pl.Pos.Y / T);

		for (int r = 0; r <= 24; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					// So a CASCA do quadrado de raio r: o miolo ja foi visto nas voltas anteriores.
					if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					if (!QuatroLadosLivres(mapa, cx + dx, cy + dy, tiles)) continue;

					// O CENTRO DA CELULA, e nao a quina: `Pos` e o centro do sprite, e meio tile de
					// desvio poria a boca do cano no limite de duas celulas -- onde a guarda de parede
					// do `Disparar` passa a depender de arredondamento.
					pl.Pos = new Vec2((cx + dx) * T + T / 2f, (cy + dy) * T + T / 2f);
					pl.Moving = false;
					_bocaAncora = pl.Pos;
					return true;
				}

		return false;
	}

	private static bool QuatroLadosLivres(ZoneCollision mapa, int cx, int cy, int tiles)
	{
		if (mapa.BlockedCell(cx, cy)) return false;

		foreach (Facing f in (Facing[])[Facing.North, Facing.South, Facing.East, Facing.West])
		{
			Vec2 d = MeleeArea.Frente(f);
			for (int k = 1; k <= tiles; k++)
				if (mapa.BlockedCell(cx + (int)d.X * k, cy + (int)d.Y * k)) return false;
		}
		return true;
	}

	/// <summary>
	/// O CORPO VOLTA PRO MESMO PONTO ANTES DE CADA FOTO. Sem isto o fundo escorrega entre um sentido
	/// e o outro, e a mascara do tiro passa a incluir o chao que se mexeu -- ver a mesma nota (e o
	/// mesmo estrago medido) na `ReporNaAncora` da bancada da variedade.
	/// </summary>
	internal void ReporNaBoca(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Pos = _bocaAncora;
		pl.Moving = false;
	}

	/// <summary>PRA ONDE ELE OLHA -- o que um jogador faz com as setas antes de atirar.</summary>
	internal void ApontarNaBoca(int id, Facing f)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Facing = f;
	}

	/// <summary>Onde o corpo esta e pra onde ele olha, na conta do SERVIDOR.</summary>
	internal (Vec2 Pos, Facing Olhar, float Altitude, bool Voando) CorpoDaBoca(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl)
			? (pl.Pos, pl.Facing, pl.Altitude, pl.Voando)
			: (default, Facing.South, 0, false);

	/// <summary>
	/// A BOCA DO CANO NA CONTA DO CORE -- o ponto que a foto vai procurar no pixel.
	///
	/// Ela sai do MESMO <see cref="BocaDeCano"/> que o `Disparar` usa. Repetir a conta aqui criaria a
	/// segunda resposta pra "onde fica a mao", e a bancada ficaria verde comparando um erro com ele
	/// mesmo -- que e o modo de falha que esta casa ja pagou com quatro defeitos visuais.
	/// </summary>
	internal Vec2 BocaEsperadaNaBoca(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl)
			? BocaDeCano.De(pl.Pos, MeleeArea.Frente(pl.Facing))
			: default;

	/// <summary>
	/// UM RAIO DE VERDADE SAINDO DA MAO, pela porta unica (<see cref="Disparar"/>).
	///
	/// `Canalizando` na unha pelo mesmo motivo da `--projetilteste` e da `--diagraio`: um beam nascido
	/// direto pelo `Disparar` tem cauda em cima da cabeca e o passo 3(c) do `AndarProjetil` o mata de
	/// `Cessou` no primeiro tique -- antes de haver o que fotografar. `Deflectivel = false` porque um
	/// sorteio no meio de uma foto fotografaria o dado.
	/// </summary>
	/// <param name="piercer">
	/// O raio ATRAVESSA quem ele acerta. E o campo de producao (`ReceitaDeProjetil.Piercer`), e ele so
	/// e ligado na cena da CAMADA: pra provar quem desenha por cima de quem, o feixe precisa continuar
	/// existindo DEPOIS de encostar no corpo. Nas outras cenas ele e falso.
	/// </param>
	internal int RaioDaBoca(int id, Vec2 rumo, double alcanceTiles,
							DefeitoDaBoca defeito = DefeitoDaBoca.Nenhum,
							bool piercer = false, double baseDano = 0.002)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;

		pl.Ficha.Ki = pl.Ficha.MaxKi;

		// O DEFEITO ENTRA PELO `deOnde` -- o parametro que a Hellzone ja usa. Ver o cabecalho.
		Vec2 boca = BocaDeCano.De(pl.Pos, rumo);
		Vec2? deOnde = defeito switch
		{
			DefeitoDaBoca.NoUmbigo => pl.Pos,
			DefeitoDaBoca.DoisTilesAFrente => BocaDeCano.De(boca, rumo),
			_ => null,
		};

		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = baseDano, Velocidade = 1,
			AlcanceTiles = alcanceTiles, Deflectivel = false, Piercer = piercer, Nome = "Onda de Ki",
		}, rumoDado: rumo, deOnde: deOnde);
		p.Canalizando = true;

		_bocaTiro = p.Id;
		_bocaDono = id;
		return p.Id;
	}

	/// <summary>
	/// UMA BOLA, PELA MESMA PORTA -- e e ela que carrega o defeito injetado do nascimento.
	///
	/// ============================ POR QUE A INJECAO E NUMA BOLA, E NAO NO RAIO ============================
	/// A primeira versao desta bancada injetava a CAUDA do raio canalizado (`p.Cauda = dono.Pos`, a linha
	/// exata de antes do conserto) e a foto saiu IGUAL a boa. O motivo esta no jogo, nao na bancada: a
	/// cauda de um raio canalizado e reescrita pelo passo do canal a **cada tique do servidor**, e o
	/// tique roda entre a escrita da bancada e o snapshot. A injecao morria dentro do proprio quadro.
	///
	/// Numa bola nao ha canal e nao ha cauda: o quadro de 32x32 fica onde ele nasceu, e e por isso que
	/// ela e o corpo de prova certo pra o `deOnde`. Fotografada nos primeiros quadros de vida, uma bola
	/// nascida em `pl.Pos` e literalmente a foto do dono -- o quadro carimbado por cima do personagem.
	///
	/// `Velocidade` e a mesma dos blasts da `--projetilteste` (o `max(1, round(4-speed))` do DM); quem
	/// garante que a foto sai cedo e o obturador da bancada, que dispara com 0,05 tile andado.
	/// ================================================================================================
	/// </summary>
	/// <param name="piercer">
	/// ATRAVESSA quem ela acerta. Ligado nas cenas de FOTO por um motivo medido: este berco e um
	/// planeta povoado, e uma bola que morre no primeiro corpo que encosta pode nao chegar viva ao
	/// obturador -- a rodada 4 desta bancada reprovou com `vivo=False, desenhado=False`, porque um NPC
	/// passou na frente. Na cena da COLISAO ele fica desligado, que e onde morrer no alvo E a medida.
	/// </param>
	internal int BolaDaBoca(int id, Vec2 rumo, DefeitoDaBoca defeito = DefeitoDaBoca.Nenhum,
							bool piercer = true, double baseDano = 0.001)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;

		pl.Ficha.Ki = pl.Ficha.MaxKi;

		Vec2? deOnde = defeito switch
		{
			DefeitoDaBoca.NoUmbigo => pl.Pos,
			DefeitoDaBoca.DoisTilesAFrente => BocaDeCano.De(BocaDeCano.De(pl.Pos, rumo), rumo),
			_ => null,
		};

		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = baseDano, Velocidade = 1,
			AlcanceTiles = 8, Deflectivel = false, Piercer = piercer, Nome = "Bola de Ki",
		}, rumoDado: rumo, deOnde: deOnde);

		_bocaTiro = p.Id;
		_bocaDono = id;
		return p.Id;
	}

	/// <summary>
	/// QUEM O FEIXE DA VEZ ESTA LEVANDO, e como o tiro acabou -- as duas leituras CATEGORICAS da cena
	/// da colisao.
	///
	/// Ela existe porque medir "acertou" pela VIDA nao serve aqui: com o dano de bancada a vitima perde
	/// 0,0002 de 100, e a rodada 4 desta casa nao soube distinguir "nao acertou" de "acertou de leve"
	/// -- as duas imprimem `100 -> 100`. Agarrar e morrer sao sim ou nao.
	/// </summary>
	internal (int Arrastando, bool Vivo, FimDeProjetil Fim, double AndouTiles) DesfechoDaBoca(int dono)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? pl)) return (0, false, FimDeProjetil.Apagou, 0);

		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
			if (p.Id == _bocaTiro)
				return (p.Arrastando, p.Vivo, p.Fim, p.AndouTiles);

		// SUMIU DA LISTA: o tiro morreu e foi recolhido. O ultimo desfecho fica guardado no
		// `_bocaFimGuardado`, escrito pelo proprio `TickDosProjeteis` -- ver `GuardarOFimDaBoca`.
		return (0, false, _bocaFimGuardado, _bocaAndouGuardado);
	}

	private FimDeProjetil _bocaFimGuardado = FimDeProjetil.Apagou;
	private double _bocaAndouGuardado;

	/// <summary>
	/// O ULTIMO DESFECHO, ANOTADO ANTES DE O TIRO SER RECOLHIDO.
	///
	/// A bancada chama isto a cada quadro enquanto espera. Sem ela, um tiro que nasce e morre entre dois
	/// quadros da bancada (que e o caso de quem atira em alguem COLADO -- ele morre no primeiro tique)
	/// nao deixaria rastro nenhum, e a bancada leria "sumiu" tanto pra "acertou" quanto pra "errou".
	/// </summary>
	internal void GuardarOFimDaBoca(int dono)
	{
		if (_bocaTiro == 0 || !_players.TryGetValue(dono, out ServerPlayer? pl)) return;

		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
			if (p.Id == _bocaTiro) { _bocaFimGuardado = p.Fim; _bocaAndouGuardado = p.AndouTiles; }
	}

	/// <summary>
	/// O QUE O TIRO DA FOTO ESTA FAZENDO, na conta do SERVIDOR -- a bancada compara isso com o pixel.
	///
	/// ============================ ELA PROCURA O TIRO MAIS NOVO DO DONO, E NAO SO O QUE ELA PLANTOU ============================
	/// Metade das cenas desta bancada NAO chama o <see cref="RaioDaBoca"/>: elas apertam o botao do
	/// Kamehameha (`UsarHabilidade`, pelo `GatilhoDaVariedade`), porque a queixa do dono e sobre a
	/// tecnica que ele usa e nao sobre um tiro forjado. Nesses casos o id nasce dentro do canal, e o
	/// unico jeito de a bancada saber qual e ele e perguntar quem e o mais novo -- exatamente como a
	/// `TiroDaVariedade` ja faz.
	///
	/// O id fica GUARDADO na passagem, e e o que deixa o <see cref="EmpurrarACaudaParaOUmbigo"/> e o
	/// <see cref="IdDoTiroDaBoca"/> saberem de qual tiro se esta falando.
	/// ==================================================================================================================
	/// </summary>
	internal (bool Vivo, Vec2 Cabeca, Vec2 Cauda, float Altitude, double AndouTiles, FimDeProjetil Fim)
		TiroDaBoca(int dono)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? pl))
			return (false, default, default, 0, 0, FimDeProjetil.Apagou);

		Projetil? novo = null;
		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
			if (p.Dono == dono && p.Vivo && (novo == null || p.Id > novo.Id)) novo = p;

		if (novo == null) return (false, default, default, 0, 0, FimDeProjetil.Apagou);

		_bocaTiro = novo.Id;
		_bocaDono = dono;
		return (novo.Vivo, novo.Pos, novo.Cauda, novo.Altitude, novo.AndouTiles, novo.Fim);
	}

	/// <summary>
	/// O ID DO TIRO DA VEZ -- e ele e o NOME do node no cliente (`World.AoNascerTiro` batiza o node de
	/// `Tiro{id}`). E por ele que a bancada acha o desenho pra injetar os dois defeitos de tela.
	/// </summary>
	internal int IdDoTiroDaBoca() => _bocaTiro;

	/// <summary>Limpa a mesa entre uma cena e a outra -- o mesmo caminho da saida do jogo.</summary>
	internal void LimparOsTirosDaBoca(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (_canais.TryGetValue(id, out CanalDeKi? c)) FecharCanal(id, c, null);
		LimparProjeteisDeUmDono(id, pl.Zone.Hash);
		_bocaTiro = 0;
	}

	/// <summary>
	/// O CORPO SOBE -- pelo `AlternarVoo`, o MESMO funil do jogador que aperta o botao.
	///
	/// A ALTURA E ESCRITA, e a subida nao: quem mede quanto tempo se leva pra subir e a `--diagvoo`.
	/// Aqui a altura e uma CONDICAO da foto (o corpo desenhado la em cima), e esperar vinte segundos
	/// de escalada por foto so acrescentaria vinte segundos de escalada.
	/// </summary>
	/// <returns>falso se o funil de producao recusou o voo (sem skill, nocauteado, sem Ki).</returns>
	internal bool VoarNaBoca(int id, float altura)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;

		pl.Ficha.Ki = pl.Ficha.MaxKi;
		if (!pl.Voando) AlternarVoo(pl);
		if (!pl.Voando) return false;

		pl.Altitude = Math.Clamp(altura, 0, Voo.AlturaMaxima);
		return true;
	}

	/// <summary>De volta ao chao, pelo mesmo funil. O tique do voo cuida da descida.</summary>
	internal void PousarNaBoca(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (pl.Voando) AlternarVoo(pl);
		pl.Altitude = 0;
	}

	/// <summary>
	/// UMA VITIMA A `tiles` TILES NO RUMO DO TIRO -- pelo `ForjarCorpoDeFoto`, o mesmo corpo com
	/// aparencia de verdade que as outras bancadas de foto usam (um corpo sem `Visual` nao desenha
	/// sprite nenhum, e a foto da camada mostraria o feixe passando por cima de nada).
	/// </summary>
	internal int VitimaDaBoca(int dono, Vec2 rumo, double tiles, double bp)
		=> ForjarCorpoDeFoto(dono,
			new Vec2((float)(rumo.X * tiles * ZoneCollision.TileSize),
					 (float)(rumo.Y * tiles * ZoneCollision.TileSize)),
			"Foto: a vitima da boca", bp, comEscada: false);

	/// <summary>Onde a vitima esta, quanta vida lhe resta e se ela ja entrou em combate.</summary>
	internal (bool Existe, Vec2 Pos, double Vida, bool EmCombate) VitimaDaFoto(int id)
		=> _players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate is { } c
			? (true, pl.Pos, c.Corpo.Vida(), c.EmCombate > 0)
			: (false, default, 0, false);
}
