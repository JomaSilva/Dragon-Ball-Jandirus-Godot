namespace Jandirus.Core.World;

/// <summary>
/// ============================ A DIMENSAO MENTAL, COMO LUGAR ============================
/// *"a mecanica da MEDITACAO, onde vc podia meditar profundamente e enfrentar um CLONE seu na sua
/// mente, e se um player meditasse AO SEU LADO ele entraria na sua mente e poderia lutar com vc."*
///
/// Este arquivo e so a resposta a **"que lugar e este?"**. As regras de dentro (o clone, o
/// visitante, os chefes, a ferida que nao volta) moram em `GameServer.Mente.cs`; o que mora aqui e
/// o que as duas pontas precisam concordar sem se falar.
/// =====================================================================================
///
/// ============================ A CHAVE DA ZONA JA CARREGA O ANFITRIAO ============================
/// A mente e `ZoneKey.Interior("Interdimension", id)`: o NOME resolve a cena no catalogo (o mapa
/// Interdimension existe desde o clone) e a SEED **e o id do dono da mente**.
///
/// E e por isso que nao ha campo nenhum de "estou na mente de fulano". Havia uma pergunta parecida
/// no servidor -- `pl.CloneId != 0` -- e ela e uma **segunda verdade** que quebra em dois casos que
/// a Camada 2 acabou de criar:
///
///   * o VISITANTE nao tem clone (quando alguem entra na mente do outro, o reflexo se desfaz -- e
///     literalmente o `if(clone) del(C)` do `add_member`, `MindMeditate.dm:201-204`). Pelo campo
///     antigo ele "nao estava na mente", e o golpe que acordasse o corpo dele la fora o levaria pelo
///     caminho errado;
///   * o ANFITRIAO tambem fica sem clone assim que o visitante chega.
///
/// A zona nao tem esse problema: quem esta la dentro **esta la dentro**, com clone ou sem.
/// ==========================================================================================
/// </summary>
public static class DimensaoMental
{
	/// <summary>
	/// O NOME DA ZONA. Ele e so IDENTIDADE -- ver <see cref="Planta"/> pro lugar em si.
	///
	/// ============================ ELE JA FOI UM ENDERECO, E ERA O DEFEITO ============================
	/// Este comentario dizia *"e o mesmo mapa branco do original (`turf/MindFloor`, `'White.dmi'`), que
	/// neste port ja existia no catalogo"* -- e era falso das duas pontas. O catalogo resolve zona pelo
	/// NOME (`ZoneCatalog.Get(ZoneKey k) => Get(k.Name)`), entao "Interdimension" casava com a entrada
	/// `z24_Interdimension` do manifesto: o z24 REAL do BYOND, 500x500, meio mosaico azul-petroleo
	/// (`Tile44`) e meio nebulosa roxa com estrelas (`dimensiontile3`, o `/turf/HDTurfs/InbetweenDimension2`
	/// de `NewTurfs.dm:176-179`). O nascimento caia bem na emenda dos dois, e o dono reparou:
	/// *"a MEDITACAO PROFUNDA ta me levando pra um LUGAR NADA A VER, era pra ser a DIMENSAO BRANCA
	/// assim como era no byond"*.
	///
	/// O z24 e um lugar de verdade do jogo -- ele so nao e este. A mente do DM nao e mapa nenhum: e um
	/// z-level construido em tempo de execucao (`build_mind_z`, `MindMeditate.dm:55-61`), celula por
	/// celula, e por isso nao existe `.dmm` pra converter. Ver <see cref="Planta"/>.
	/// ============================================================================================
	/// </summary>
	public const string Zona = "Interdimension";

	/// <summary>Esta zona e uma mente? So `Interior` com este nome pode ser.</summary>
	public static bool EhAMente(ZoneKey z) =>
		z.Kind == ZoneKey.KindInterior && string.Equals(z.Name, Zona, StringComparison.Ordinal);

	/// <summary>
	/// A MENTE DE QUEM. Derivado da seed da chave -- ver o cabecalho.
	///
	/// Zero pra qualquer zona que nao seja uma mente, e o zero e util: ele e a resposta a
	/// "este lugar tem dono?" sem exigir que o chamador pergunte duas coisas.
	/// </summary>
	public static int Anfitriao(ZoneKey z) => EhAMente(z) ? (int)z.Seed : 0;

	/// <summary>A mente DESTA pessoa. UM bolso por dono, e o mesmo bolso em toda entrada.</summary>
	public static ZoneKey De(int anfitriaoId) => ZoneKey.Interior(Zona, (ulong)anfitriaoId);

	/// <summary>
	/// A MESMA MENTE? Compara o bolso e nao o dono -- e a pergunta que o visitante faz, e a resposta
	/// tem que ser verdadeira pra ele mesmo estando na mente de outra pessoa.
	/// </summary>
	public static bool MesmaMente(ZoneKey a, ZoneKey b) => EhAMente(a) && a.Hash == b.Hash;

	// =====================================================================
	// QUEM NAO PODE MERGULHAR
	// =====================================================================
	/// <summary>
	/// POR QUE ESTA PESSOA NAO PODE MERGULHAR AGORA -- a frase pronta, ou "" quando pode.
	///
	/// ============================ POR QUE ISTO MORA NO CORE ============================
	/// Porque agora ha DUAS pontas perguntando a mesma coisa, e elas nao se falam:
	///
	///   * o SERVIDOR, que e a autoridade -- ele recusa a entrada de verdade (`EntrarNaMente`);
	///   * a TELINHA do meditar, que precisa APAGAR o botao da meditacao profunda **antes** do
	///     clique, com o motivo escrito na tela (*"e recusado na telinha, com a razao na tela --
	///     nao depois"*).
	///
	/// Com duas copias, o dia em que uma regra mudasse produziria o defeito classico deste port: o
	/// botao aceso que o servidor recusa, ou -- pior -- o botao apagado sem regra nenhuma por tras.
	/// Aqui ha uma lista so, e a telinha nao INVENTA nada: ela repete o que o servidor vai dizer.
	/// ================================================================================
	///
	/// ============================ SAO EXATAMENTE AS TRES DO ORIGINAL ============================
	/// `if(mind_session || !client || dead || KO) return` (`MindMeditate.dm:355`) -- ja em transe,
	/// morto, nocauteado. O `!client` nao tem par aqui (um corpo sem dono nao aperta tecla nenhuma;
	/// quem responde por ele e o `pl.Peer == null` do servidor).
	///
	/// **ESTAR EM COMBATE NAO ESTA NA LISTA, E ISSO E DELIBERADO.** Nem o DM nem este servidor
	/// recusam quem acabou de trocar socos -- e o desenho inteiro do corpo largado depende disso:
	/// quem medita fica no mapa e **acorda no primeiro golpe** (`GameServer.CorpoLargado.cs`,
	/// `MarcarAgressao`). Meditar no meio de uma briga e uma ma ideia, nao uma coisa proibida.
	/// A telinha AVISA (ver `TelaDeMeditacao`); recusar seria o cliente inventando uma regra que a
	/// autoridade nao tem.
	/// ======================================================================================
	/// </summary>
	public static string PorQueNaoMergulhar(bool jaEmTranse, bool ko, bool morto)
	{
		if (jaEmTranse) return "voce ja esta em transe.";
		if (morto) return "um morto nao mergulha na propria mente.";
		if (ko) return "voce esta caido -- nao da pra entrar em transe assim.";
		return "";
	}

	// =====================================================================
	// A GOTA -- QUANTO TEMPO A TELA ONDULA ANTES DA VIAGEM
	// =====================================================================
	/// <summary>
	/// ============================ A ONDA E UM PRAZO DO SERVIDOR, E NAO UM ENFEITE DO CLIENTE ============================
	/// *"faca um SHADER legal q deixa a TELA ONDULANDO IGUAL UMA GOTA quando cai na agua quando a
	/// pessoa vai entrar na meditacao profunda [...] a tela vai ter esse efeito por uns segundos e ai o
	/// jogador vai pra dimensao da mente dele"*, e *"quando ele DERROTAR O CLONE dele tb faca isso mas
	/// pra VOLTAR pro mundo real, pq atualmente a transicao ta MT RAPIDA E MT SECA"*.
	///
	/// O numero mora AQUI, no Core, porque as duas coisas que ele governa sao de donos diferentes e
	/// tem que casar: o SERVIDOR segura a viagem por ele (a mudanca de zona so sai no fim), e o
	/// CLIENTE ondula por ele (o valor viaja no proprio pacote de efeito, entao a tela nunca inventa
	/// uma duracao propria). Se fossem duas constantes, o dia em que uma mudasse produziria
	/// exatamente o defeito irmao que o dono acabou de relatar no bio -- *"o corpo troca antes da
	/// hora"*: o jogador veria o DESTINO ondulando, e nao a saida.
	///
	/// 1,8 s e "uns segundos" sem virar pedagio. A onda tem que dar tempo de tres aneis atravessarem
	/// a tela (ver `Assets/Shaders/Gota.gdshader`); abaixo de ~1,2 s vira um solavanco, e acima de
	/// ~2,5 s quem medita dez vezes numa sessao passa a esperar.
	/// ================================================================================================================
	/// </summary>
	public const long MsDaOnda = 1800;

	// =====================================================================
	// O LUGAR -- `build_mind_cell` (`MindMeditate.dm:70-86`)
	// =====================================================================
	/// <summary>
	/// O LADO DA CHAPA PINTADA. **Nao e o tamanho da mente -- a mente nao tem tamanho.**
	///
	/// ============================ A SALA FECHADA EXISTIU, E O DONO A DERRUBOU ============================
	/// Ate ontem isto era o quarto inteiro: `#define MIND_CELL 100` (`MindMeditate.dm:16`) com um anel
	/// de `turf/MindWall` em volta, montado logo abaixo, no molde do interior da Capital Ship. O pedido
	/// que a desfez veio com a razao junto, e a razao e boa:
	///
	///   *"faca tb o MAPA DA MENTE ser INFINITO SEM BORDAS e CARREGAR POR CHUNK, tb o FUNDO BRANCO, pq
	///   as BORDAS PRETAS sao estranhas e as vezes o NPC VOA PRA FORA e ele TELEPORTA DE VOLTA e fica
	///   mt estranho e perde a imersao"*.
	///
	/// **E O "TELEPORTA DE VOLTA" NAO ERA TELEPORTE NENHUM.** Nao ha reposicionamento na mente: a volta
	/// do planeta (`GameServer.Volta`) desiste em `!Espaco.EhPlaneta`, e interior esta fora. Quem
	/// produzia o sintoma era a PAREDE, por um caminho que so aparece medindo o voo: quem voa alto anda
	/// com `mapa = null` (ver <see cref="Voo"/>, `AtravessaCenario`) e ATRAVESSA o anel; ao pousar, a
	/// colisao volta a valer com o corpo dentro do muro e o servidor o crava pra fora. Sem parede a
	/// causa nao existe, e nao ha o que consertar em cima dela.
	/// ================================================================================================
	///
	/// ============================ ENTAO PRA QUE UM NUMERO AINDA ============================
	/// Porque o infinito precisa de uma ORIGEM. Este retangulo e a unica parte da mente que existe como
	/// DADO -- e dele que sai a celula do nascimento (<see cref="CelDeQuemMedita"/>), e e ele que o
	/// pintor do cliente considera "o pedaco que veio da planta"; todo o resto ele pinta de branco
	/// sozinho, por pedaco, sem que exista byte nenhum por tras (ver
	/// `Client/PlanetaProcedural.FonteDoTerreno`, o modo sem beirada).
	///
	/// Cem continua sendo o numero do DM e custa 10.000 celulas pagas UMA vez no processo inteiro. Ele
	/// poderia ser 1 sem mudar o que o jogador ve -- e fica em 100 justamente porque nao muda nada:
	/// mexer nele so pra economizar 10 KB seria trocar a rastreabilidade ao original por nada.
	/// ==================================================================================
	/// </summary>
	public const int Lado = 100;

	// =====================================================================
	// A COLEIRA -- O QUE SUBSTITUIU A PAREDE
	// =====================================================================
	/// <summary>
	/// A QUE DISTANCIA DO DONO O REFLEXO REAPARECE NA FRENTE DELE, em pixels.
	///
	/// ============================ A PAREDE SEGURAVA VOCE; A IA NUNCA SEGUROU ELE ============================
	/// Derrubar o muro abriu um buraco que nao era obvio, e ele nao esta no desenho -- esta na
	/// aritmetica. O reflexo persegue **incondicionalmente** (`GameServer.Clone`, o ramo
	/// `DonoDoClone != 0`: `presa = dono; destino = dono.Pos;`, sem raio e sem desistencia), so que ele
	/// copia a SUA ficha (`EspelharODono`) e portanto o seu `SpeedStat`. **Perseguidor com a mesma
	/// velocidade nunca alcanca quem foge.** Num quarto de 100 tiles isso nao importava: a parede
	/// devolvia voce. Num plano sem fim, quem nao quiser lutar foge pra sempre e o combate mental
	/// **nunca fecha por vitoria** -- so pelas saidas que ja existem (o verb de sair, o nocaute do corpo
	/// la fora, o golpe no corpo real). Um combate que nao fecha e pior que uma borda feia.
	///
	/// ============================ POR QUE REAPARECER, E NAO ACELERAR ============================
	/// A outra resposta barata era dar ao reflexo um multiplicador de velocidade enquanto longe. Ela e
	/// PIOR na tela: o jogador ve o proprio reflexo ficando mais rapido que ele sem explicacao, e o
	/// numero teria de crescer sem teto pra funcionar contra quem voa em linha reta. Reaparecer nao
	/// precisa de desculpa nenhuma -- **e a mente dele**, e "voce nao corre mais rapido que o proprio
	/// reflexo" e a coisa que o lugar ja diz. O DM nao precisou escolher porque la havia muro.
	///
	/// O SALTO NAO PISCA FEIO: o cliente ja crava (sem interpolar) qualquer deslocamento acima de 3
	/// tiles num intervalo de pacote (`Client/RemotePlayer.LimiteDeSalto`), que e exatamente o que se
	/// quer aqui -- um reflexo que reaparece, e nao um corpo deslizando pela tela.
	/// ====================================================================================
	/// </summary>
	public const float RaioDaColeira = 40 * ZoneCollision.TileSize;

	/// <summary>
	/// O REFLEXO FUGIU DEMAIS? -- a pergunta que substitui a parede, e ela mora aqui e nao no servidor
	/// porque e uma regra **do lugar**: e a mente que deixou de ter beirada, entao e ela que responde
	/// pelo que a beirada fazia.
	///
	/// QUARENTA TILES e folgado de proposito. A tela mostra ~30 tiles de mundo, entao ele pode sumir da
	/// vista por um instante (e isso e bom -- um oponente colado na camera nao da espaco pra rajada);
	/// o que ele nao pode e virar um ponto no horizonte. Nenhuma distancia de combate normal chega
	/// perto disto: a maior area de melee do jogo tem 2 tiles e a rajada mais longa nao passa de 20.
	/// </summary>
	public static bool FugiuDoDono(Vec2 doReflexo, Vec2 doDono) =>
		(doReflexo - doDono).Length > RaioDaColeira;

	/// <summary>
	/// ONDE QUEM MEDITA NASCE -- `locate(cx-3, cy, mz)` (`MindMeditate.dm:196-198`), tres celulas a
	/// OESTE do centro, virado pro leste.
	///
	/// As tres celulas nao sao enfeite: o oponente (reflexo, chefe ou visitante) nasce a
	/// `GameServer.Mente.DistanciaDoOponente` = 96 px = 3 tiles a leste, entao ele cai EXATAMENTE no
	/// centro da chapa. Quem medita olha pro meio da propria mente, e a luta comeca no eixo.
	///
	/// O "centro" continua querendo dizer alguma coisa mesmo sem parede: e a ORIGEM do plano infinito
	/// (ver <see cref="Lado"/>), e nao o meio de um quarto.
	/// </summary>
	public static readonly (int X, int Y) CelDeQuemMedita = (Lado / 2 - 3, Lado / 2);

	/// <summary>O centro em pixels de uma celula da planta -- gemeo do `NaveGrande.PixelDe`.</summary>
	public static Vec2 PixelDe((int X, int Y) cel) => new(
		(cel.X + 0.5f) * ZoneCollision.TileSize,
		(cel.Y + 0.5f) * ZoneCollision.TileSize);

	/// <summary>
	/// O PINCEL. **UM SO**, e e essa a coisa toda: `turf/MindFloor` e `turf/MindWall` declaram os dois
	/// `icon = 'White.dmi'` (`MindMeditate.dm:27` e `:34`), e o comentario do proprio autor na parede
	/// diz *"identica ao chao: o limite da mente e invisivel (so esbarra)"*. O original ja tinha chegado
	/// onde o dono chegou -- ele so nao pode tirar o "esbarra".
	///
	/// O `White.dmi` ja estava convertido neste port desde sempre (`Assets/Data/tiles.json:161`,
	/// atlas `White`, estado unico sem nome) -- zero byte novo de asset, zero conversao.
	/// </summary>
	private static readonly TileVisual Branco = new("White", "");

	/// <summary>
	/// A PALETA -- montada a mao, como a `NaveGrande.PaletaDoCasco` e pelo mesmo motivo: e o que
	/// permite reusar o `TerrenoGerado` inteiro (o pintor por pedaco do cliente, a colisao do
	/// servidor) sem inventar um sexto lugar que sabe desenhar chao.
	///
	/// AS DEZ ENTRADAS APONTAM PRO MESMO BRANCO de proposito, e nao pro vazio: o
	/// `PlanetaProcedural.MontarCamadas` resolve os dez pinceis no nascimento, e um `TileVisual` vazio
	/// faria dez avisos amarelos toda vez que alguem medita.
	/// </summary>
	private static readonly PaletaDeBioma PaletaDaMente = new()
	{
		// SO UM ROTULO -- a `TerrenoGerado` exige um bioma e nenhum deles e "vazio branco". `Morto` e o
		// mais perto, e ele nao entra em conta nenhuma: aqui nao ha ruido, nao ha sorteio, nao ha
		// altitude. E a mesma escolha (e o mesmo motivo) da planta da nave.
		Bioma = BiomaDeTerreno.Morto,
		LimiarAgua = 0, LimiarPraia = 0, LimiarColina = 1, LimiarMontanha = 1,
		VegetacaoPct = 0, PlantaPct = 0, MinerioPct = 0,
		Agua = Branco, Praia = Branco, Planicie = Branco, Colina = Branco, Montanha = Branco,
		Arvore = Branco, ArvoreAcento = Branco, Planta = Branco, Minerio = Branco, Gema = Branco,
	};

	private static TerrenoGerado? _planta;

	/// <summary>
	/// ============================ A DIMENSAO BRANCA, COMO FUNCAO ============================
	/// *"era pra ser a DIMENSAO BRANCA assim como era no byond"*.
	///
	/// No DM ela nao e um mapa: `build_mind_z()` faz `world.maxz + 1` e assenta a celula a mao --
	/// miolo `turf/MindFloor`, borda `turf/MindWall` (`MindMeditate.dm:55-86`). Nao ha `.dmm` pra
	/// converter porque nao ha `.dmm`: o lugar nasce no boot da sessao.
	///
	/// AQUI ISSO NAO E UMA PERDA, E O CAMINHO QUE O PROJETO JA TEM. `ZoneKey.Interior` + uma
	/// `TerrenoGerado` pura no Core e exatamente a receita do interior da Capital Ship
	/// (`Core/Tech/NaveGrande.Planta`), e ela ja provou o desenho inteiro: o servidor tira daqui a
	/// COLISAO e o cliente tira o DESENHO, zero byte de mapa na rede, e o que separa a mente de uma
	/// pessoa da mente de outra continua sendo o hash da `ZoneKey` -- nao a planta, que e uma so.
	///
	/// ============================ E NAO HA UMA CELULA DENSA AQUI DENTRO ============================
	/// O DM cerca o quarto de `turf/MindWall`, que recusa **todo** mob, inclusive quem voa
	/// (`MindWall.Enter()`, `:39-41`). **Esta divergencia e deliberada e e o pedido do dono** -- ver o
	/// comentario de <see cref="Lado"/> pra o porque (a parede era a causa do "voa pra fora e teleporta
	/// de volta", e nao a vitima dele). O bitset sobe inteiro em zero, e o que apaga a beirada do
	/// RETANGULO -- que existiria mesmo sem anel nenhum, porque fora do bitset e parede em todo mapa --
	/// e a linha do `SemBorda` la embaixo.
	///
	/// GRAVIDADE 1: no DM a `area/MindDim` tem `Planet = "Mind Dimension"`, que cai no `else` do
	/// `Grav()` (`:43-45`). Do lado do cliente o `PlanetaProcedural` ja fixa 1 pra todo terreno
	/// injetado -- nao ha nada a fazer.
	///
	/// CUSTO: 10.000 celulas, um laco sem ruido, pago UMA vez no processo inteiro (a planta e
	/// compartilhada -- o que separa as mentes e a chave). O infinito em volta nao acrescenta um byte:
	/// ele nao e dado nenhum, e um pincel repetido (ver `FonteDoTerreno`).
	/// ====================================================================================
	/// </summary>
	public static TerrenoGerado Planta() => _planta ??= Montar();

	private static TerrenoGerado Montar()
	{
		const int n = Lado;
		var chao = new byte[n * n];
		var cobertura = new byte[n * n];

		// BITSET INTEIRO EM ZERO: nao ha uma celula densa na mente. Ele continua existindo (e o
		// `TerrenoGerado` o exige) porque e ele que carrega a LARGURA e a ALTURA da chapa -- o que o
		// pintor usa pra saber onde a planta acaba e o branco de graca comeca.
		var bits = new byte[(n * n + 7) / 8];

		for (int i = 0; i < n * n; i++)
		{
			chao[i] = (byte)ClasseDeTerreno.Planicie;
			cobertura[i] = (byte)CoberturaDeTerreno.Nada;
		}

		byte[] jcol = GeradorDeTerreno.MontarJcol(n, n, bits);
		ZoneCollision colisao = ZoneCollision.Load(jcol)!;

		// ============================ A LINHA QUE FAZ A MENTE SER INFINITA -- E E UMA SO ============================
		// `SemBorda` muda DUAS respostas de uma vez (ver `ZoneCollision.SemBorda`): fora do bitset passa
		// a ser CHAO em vez de parede, e o `NaBorda` para de existir -- sem a segunda, o jogador andaria
		// pelo vazio com uma cerca invisivel de duas celulas em volta da chapa, imposta pela regra que
		// protege a beirada de um mapa que aqui nao tem beirada.
		//
		// **ELA NAO PRECISA DE ENCANAMENTO NENHUM NAS DUAS PONTAS, E ESSA E A ESCOLHA.** O bit e uma
		// propriedade DESTE objeto, e este objeto e literalmente o mesmo nos dois lados: o servidor o
		// pega em `GameServer.Mente.MapaDaMente` e o cliente em `PlanetaProcedural.Colisao =>
		// Terreno?.Colisao`, os dois chamando `Planta()`. A alternativa era a da Sala do Tempo -- uma
		// pergunta por NOME de zona (`SalaDoTempo.SemBorda`) respondida em dois carregadores diferentes
		// --, e ela existe la porque a Sala vem de ARQUIVO e cada ponta le o arquivo por conta propria.
		// A mente nao vem de arquivo: ela e esta funcao. Repetir o criterio por nome aqui seria criar a
		// segunda verdade de graca, e o sintoma de as duas discordarem e o pior deste projeto (o corpo
		// tremendo na costura, cliente e servidor achando parede em lugares diferentes).
		// ========================================================================================================
		colisao.SemBorda = true;

		return new TerrenoGerado
		{
			Largura = n,
			Altura = n,
			Bioma = BiomaDeTerreno.Morto,
			Seed = 0,               // a planta e uma so; quem separa as mentes e a `ZoneKey`
			Paleta = PaletaDaMente,
			Chao = chao,
			Cobertura = cobertura,
			BytesDeColisao = jcol,
			// NAO HA AGUA NA MENTE. Plano vazio = `ZoneCollision.TemAgua == false`, sem custo por
			// celula -- e sem lago pra o nado descobrir aqui dentro.
			BytesDeAgua = new byte[(n * n + 7) / 8],
			Colisao = colisao,
			SpawnCelX = CelDeQuemMedita.X,
			SpawnCelY = CelDeQuemMedita.Y,
			ClareiraEscavada = false,
		};
	}
}
