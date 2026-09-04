using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DOS DOIS CORPOS (`--doiscorposteste`) ============================
/// As tres bancadas que este trabalho ja tem medem **um corpo de cada vez**: a `--cadaverteste` prende
/// e arremessa contra corpos forjados que nunca andam, a `--corpo` (do `AssetPipeline`) mede a grade
/// espacial em memoria sem servidor nenhum, e a `--alemteste` nunca teve dois corpos na mesma zona.
///
/// **Nenhum dos tres pedidos do dono e sobre um corpo.** *"agarrar o inimigo"*, *"personagens n
/// consigam passar DENTRO DO OUTRO"*, *"a pessoa q COLIDIU com o corpo voando TB toma dano"*, *"pessoas
/// podem agarrar o corpo e SAIR VOANDO com ele"* -- os quatro sao verbos com dois sujeitos. Esta bancada
/// existe pra medir o par.
/// ========================================================================================================
///
/// ============================ CADA FAMILIA NOS DOIS SENTIDOS ============================
/// Uma bancada que so mede o sentido POSITIVO fica verde com a regra desligada pelo outro lado: "corpo
/// nao atravessa corpo" passaria com uma colisao que barra TODO MUNDO -- inclusive quem voa dez tiles
/// acima --, e o mundo travaria sem uma linha vermelha. Entao aqui toda familia tem o contra-exemplo ao
/// lado da afirmacao:
///
///   1. AGARRAR       -> agarra quem pode / **nao** agarra quem esta preso por outro nem quem e
///                       intocavel nem quem esta fora do andar
///   2. ARREMESSAR    -> andar segurando ARREMESSA / segurar parado **nao** arremessa
///   3. CARREGAR      -> o carregado sobe na MESMA altitude / e ele **nao** paga Ki de voo
///   4. AS SOLTURAS   -> nocaute, zona e logout SOLTAM / o aperto sadio **nao** se desfaz sozinho
///   5. COLISAO       -> a pe, arremessado e por knockback ESBARRA / em andares diferentes ATRAVESSA
///   9. O OCUPADO     -> NO AR, **os DEZ estados do `Ocupacao`, um por linha e com o nome do estado na
///                       linha**, param quem voa contra eles e nao deslizam / e com o mesmo corpo
///                       LIVRE, quem voa atravessa (o `mob/Cross`), e em andar diferente tambem --
///                       ocupado nao vira poste. Cada estado ainda mede que o ARREMESSO continua
///                       empurrando aquele mesmo corpo (senao "ele nao andou" seria verdade de graca)
///                       e que dois corpos nascidos SOBREPOSTOS nao ficam presos. (Roda logo depois da
///                       5: e a mesma pergunta no lugar onde ela faltava.)
///   6. O BAQUE       -> os DOIS se machucam / sem encontro, NENHUM dos dois se machuca
///   7. O CADAVER     -> fica, apanha, e agarrado e levado voando / e some quando enterrado
///   8. A VIAGEM      -> nao regrediu (a regressao mais provavel de todas)
/// ====================================================================================
///
/// ============================ E OITO DEFEITOS INJETADOS ============================
/// A instrucao da tarefa e literal: *"PROVE injetando o defeito"*. Toda afirmacao central desta bancada
/// passa pelo <see cref="Mutacao"/> -- o mesmo helper da `--provateste` --, que mede o criterio, estraga
/// o mundo, exige que **o mesmo criterio** reprove, e desfaz. Uma checagem que so foi vista passando e
/// indistinguivel de `Checa("...", true)`, e este projeto ja pagou esse preco tres vezes.
///
/// **Os defeitos injetados nao sao inventados: sete dos oito sao defeitos que este trabalho JA
/// CONSERTOU**, remontados pelo lado do DADO em vez do lado do codigo:
///
///   * o corpo preso vivendo numa lista que o tique nao le  (o `_players.TryGetValue` do agarrao)
///   * a grade de colisao descrevendo o quadro passado      (a ordem do `MontarAsGrades`)
///   * o corpo no colo deixando de ser "carregado"          (a guarda do `TickDoVoo`)
///   * a varredura orfa nao alcancando quem ficou preso     (a fonte `TodosOsCorpos`)
///   * o corpo destrocado                                    (o `Destroy()` do DM)
///   * a cova recusada com o corpo ja apagado                (a ordem do `Enterrar`)
///   * a viagem que ja aconteceu                             (o `MorteJaViajou`)
/// ============================================================================
///
///     Godot --headless --path . --host --rede 7942 --doiscorposteste --raca Saiyan
///                      --conta bancada_dois --nome Duplo
///
/// TUDO O QUE ELA TOCA E DEVOLVIDO no `finally`: os dois corpos forjados saem do mundo, o host volta
/// vivo, na zona e na posicao em que estava, e as lapides que ela ergueu saem do `mundo.json` -- uma
/// bancada que deixasse tumulos de teste no disco do dono sujaria o mundo dele uma vez por rodada.
/// </summary>
public sealed partial class GameServer
{
	private bool _doisCorposDeTeste;
	private int _dcOk, _dcFalhou;

	/// <summary>
	/// Faixa de ids propria -- longe do `_nextId` e das faixas das outras bancadas (90.100, 90.400,
	/// 90.800, 91.200 da foto, 91.300 do alem). Dois corpos com o mesmo id seriam o pior defeito
	/// possivel numa bancada de COLISAO: a grade guarda id, e `ignorarId` faria um deles atravessar
	/// o outro por identidade em vez de por regra.
	/// </summary>
	private const int IdBaseDosDois = 91_700;

	private void AfirmarDc(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _dcOk++; GD.Print($"[dois]   OK    {oque}"); return; }
		_dcFalhou++;
		GD.PrintErr($"[dois]   FALHA {oque}   {detalhe}");
	}

	// =====================================================================
	// O RELOGIO DA BANCADA -- o tique de producao, na ordem de producao
	// =====================================================================
	/// <summary>
	/// ============================ UM TIQUE DO MUNDO, NA ORDEM DO `Tick()` ============================
	/// Os cinco passos abaixo sao os do <see cref="Tick"/> **na mesma ordem**, e a ordem e metade do
	/// que esta bancada mede: a grade e montada ANTES de qualquer um andar (senao uns leem o quadro de
	/// agora e outros o passado, e o sintoma e "A parou em B mas B atravessou A"), os relogios do corpo
	/// vem antes do passo, e o agarrao e vizinho do arremesso porque e nele que ele desemboca.
	///
	/// ============================ POR QUE NAO CHAMAR O `Tick()` INTEIRO ============================
	/// Seria mais honesto em teoria e inutil na pratica: o `Tick()` roda o POVOAMENTO, e o povoamento
	/// nasce habitante **na zona onde esta bancada esta contando corpos**. Uma familia que mede
	/// "o corpo A parou no corpo B" com um terceiro corpo nascendo no meio do caminho mediria outra
	/// coisa a cada rodada -- e o veredito viraria sorteio. O que ficou de fora nao toca em posicao de
	/// corpo: sagas, projeteis, portas, naves, espaco.
	///
	/// O PASSO E PARAMETRO (<paramref name="andar"/>) porque ele e o que a familia escolhe: no jogo
	/// quem anda e o `TickDosCorposSemDono` (pelo cerebro) ou o cliente (pelo input), e nas duas pontas
	/// o funil e o MESMO <see cref="AplicarComando"/>/<c>MoveRules.Advance</c> que esta bancada chama.
	/// ============================================================================================
	/// </summary>
	private void TiqueDoMundo(Action? andar = null)
	{
		MontarAsGrades();
		TickDosRelogiosDoCorpo(Protocol.TickSeconds);
		andar?.Invoke();
		TickDoEmpurrao();
		TickDoAgarrao(Protocol.TickSeconds);

		// O TIQUE DOS CADAVERES E DE 5 Hz NO JOGO (`_tickCount % TicksPorFicha`), e ele e assim aqui
		// pelo mesmo motivo: rodando a 30 Hz o teto da zona levaria seis corpos por segundo em vez de
		// cinco, e a familia 7 estaria medindo uma cadencia que o jogo nao tem.
		if (++_dcTiques % TicksPorFicha == 0) TickDosCadaveres();
	}

	private int _dcTiques;

	/// <summary>
	/// ============================ QUANTO DURA UM PASSEIO DE MEDIDA ============================
	/// Quarenta tiques -- um segundo e um terco -- valem uns doze tiles no corpo desta bancada. O numero
	/// e uma FAIXA e nao um gosto, e as duas paredes dela sao:
	///
	///   * tem que ser MUITO mais que os 3 tiles ate Beta, senao "nao atravessou" nao se distingue de
	///     "nao deu tempo" -- e e justamente esse o par que o defeito injetado da familia 5 separa;
	///   * tem que caber na reta caminhavel que o `AcharPalco` conseguiu achar (16 tiles). Cento e vinte
	///     tiques andavam 35 tiles e a primeira rodada mediu exatamente isso: o corpo parava numa PAREDE
	///     e a bancada dizia que ele tinha sido barrado por gente.
	/// ======================================================================================
	/// </summary>
	private const int TiquesDoPasseio = 40;

	/// <summary>N tiques do mundo, com o mesmo passo em todos eles.</summary>
	private void Tiques(int n, Action? andar = null) { for (int i = 0; i < n; i++) TiqueDoMundo(andar); }

	// =====================================================================
	// OS CORPOS DA BANCADA
	// =====================================================================
	private ServerPlayer? _alfa, _beta, _gama;
	private readonly List<ServerPlayer> _dcForjados = [];

	/// <summary>
	/// UM CORPO DE VERDADE, PELA SEQUENCIA DE VERDADE. `PorNoMundo` e o unico caminho que monta corpo
	/// sem dono direito (`Statify` -> `SpeedStat` -> `PrepararCombate` -> as duas listas -> gravidade),
	/// e montar a mao aqui seria a segunda copia daquela sequencia -- que e o defeito que aquele metodo
	/// existe pra nao ter.
	///
	/// **SEM CEREBRO, DE PROPOSITO.** Um corpo que decide sozinho pra onde ir daria a mesma foto e o
	/// mesmo numero quer a colisao funcione, quer o plano dele tenha sido "ficar parado" -- e a bancada
	/// nao teria como separar as duas. Quem manda nele e o <see cref="AplicarComando"/>, empurrado de
	/// fora, que e o MESMO atuador que o cerebro usa (e onde mora o `PodeMexerOCorpo`).
	/// </summary>
	private ServerPlayer ForjarDois(string nome, ZoneKey zona, Vec2 onde, double bp = 5_000_000)
	{
		var f = new Jandirus.Core.Stats.Fighter
		{
			Name = nome, Race = "Saiyan", BP = bp,
			physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
			kiskill = 5, speed = 5, magiskill = 5, Idade = 25,
			maxstamina = 100, stamina = 100,
		};
		f.Statify();

		// O `expressedBP` NAO SAI DO `Statify` -- ele sai do `PowerLevel`, que num corpo de verdade
		// roda no `TickFichas`. Sem esta linha o corpo nasce com poder EXPRESSO zero, e a forca do
		// aperto (`Agarrao.Forca` = `Ephysoff * expressedBP`) seria ZERO: a chance de escapar viraria
		// divisao por zero e a familia 1 mediria um agarrao que ninguem consegue dar.
		f.PowerLevel();
		f.Ki = f.MaxKi;

		var c = new ServerPlayer
		{
			Id = IdBaseDosDois + _dcForjados.Count,
			Peer = null,
			Cerebro = null,
			Name = nome,
			Race = "Saiyan", Genero = "Male", Idade = 25,
			Zone = zona, Pos = onde,
			Conta = "bancada_dois",
			LastInputMs = NowMs(),
			Ficha = f,
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		PorNoMundo(c);

		// UM CORPO RECEM-MONTADO PODE ENTRAR NOCAUTEADO por um instante (o `PrepararCombate` monta os
		// membros e o `SincronizarVida` decide), e `PodeMexerOCorpo` recusaria o passo e o agarrao com
		// razao -- a bancada mediria "nao agarra" e estaria medindo o berco. `Reviver` e a mesma funcao
		// que o `--cadaverteste` usa pra por o host de pe.
		c.Combate.Reviver();
		c.Ficha.PowerLevel();

		_dcForjados.Add(c);
		return c;
	}

	/// <summary>
	/// ENSINA A VOAR -- a mesma concessao que o `--vooteste` faz, e pela mesma razao: o que esta
	/// bancada tem que atravessar e o `AlternarVoo` e a altitude, e nao a COMPRA da skill.
	/// </summary>
	private static void DarOVoo(ServerPlayer c)
	{
		c.Livro.Dar(SkillDoKi);
		c.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
	}

	/// <summary>
	/// ============================ ALTURA SE GANHA VOANDO, E ISSO NAO E CERIMONIA ============================
	/// (E O NOME NAO PODE SER `Decolar`: **ja existe um `Decolar(ServerPlayer)` neste servidor**, e ele e
	/// o verbo de SAIR DO PLANETA pro espaco. Com a sobrecarga de um argumento so, o compilador escolhia
	/// o metodo exato -- e a bancada mandava os dois corpos pra orbita no meio da medida. Trinta linhas
	/// vermelhas, e o unico rastro era um `[server] ... decolou de Namek -> chunk (559,-552)` no log.)
	///
	/// A tentacao e escrever `c.Altitude = 6 * TileSize` e seguir a vida. **Nao funciona, e a razao e o
	/// conserto desta fase**: altura com `Voando` falso e a assinatura de QUEM PERDEU O VOO NO AR, e o
	/// ramo de queda do `TickDoVoo` puxa o corpo pro chao a 16 tiles por segundo. A primeira rodada
	/// desta bancada mediu exatamente isso -- as duas familias de "andar diferente" reprovaram com
	/// `andar 0` na mensagem, porque o corpo tinha CAIDO durante os tiques da medida.
	///
	/// Entao aqui se decola pelo caminho do jogador: `AlternarVoo` (o verbo `Fly`) e a tecla de subir
	/// (`Comando.QuerSubir`, o mesmo bit que o `Input` escreve). Um corpo que paira porque a bancada
	/// escreveu um numero nao prova nada sobre voo nenhum.
	/// ====================================================================================================
	/// </summary>
	private void LevantarVoo(ServerPlayer c, int tiques = 90)
	{
		if (!c.Voando) AlternarVoo(c);
		Tiques(tiques, () => AplicarComando(c, new Comando { QuerSubir = true }, Protocol.TickSeconds));
		c.QuerSubir = false;
	}

	/// <summary>Desliga o voo e espera o corpo POUSAR pelo `TickDoVoo` -- ninguem e teleportado pro chao.</summary>
	private void Pousar(ServerPlayer c)
	{
		if (c.Voando) AlternarVoo(c);
		Tiques(260);
	}

	/// <summary>
	/// ============================ ONDE CABE UM PALCO DE CORPOS QUE ANDAM ============================
	/// **A PERGUNTA E A QUE O PROPRIO PASSO FAZ**: <see cref="MoveRules.Occupied"/> com
	/// <see cref="ModoDeTravessia.APe"/> -- a mesma funcao, a mesma caixa dos pes, o mesmo modo. A
	/// primeira versao reusou a <see cref="RumosLivresDaPose"/>, que ja existia, e reprovou tres linhas
	/// com o corpo parando a dois tiles do nada **inclusive com a colisao de corpos desligada**: aquela
	/// funcao pergunta `mapa.BlockedCell` (parede) porque foi escrita pra achar espaco pro RAIO passar, e
	/// raio voa por cima de agua. Este palco e de gente a pe, e em Namek meio mundo e lago. Reusar a
	/// funcao ERRADA por ela ja existir produziu tres vermelhos que nao eram do jogo.
	///
	/// ============================ E O PALCO E PROCURADO, NAO ASSUMIDO ============================
	/// A segunda versao perguntava so pelo ponto de nascimento do host, e reprovou inteira: o berco caiu
	/// na beira de um lago e nenhum dos quatro rumos tinha 14 tiles de chao seco. Uma bancada que so
	/// funciona quando o spawn calha de ser bom nao mede nada nas outras rodadas -- ela so muda de
	/// veredito. Entao ela varre um anel de candidatos ate achar um ponto com uma reta caminhavel, e o
	/// anel comeca a QUATRO tiles porque o proprio host e um corpo na grade.
	///
	/// ============================ O RUMO E O LACO DE FORA, E ISSO NAO E ESTILO ============================
	/// Com o rumo por dentro, "prefiro o eixo X" so valia DENTRO de um ponto: um candidato a quatro tiles
	/// que so tivesse reta pro sul ganhava de um a seis tiles com reta pro leste, e a preferencia nunca
	/// se cumpria. Foi medido -- a foto da colisao saiu no eixo Y duas rodadas seguidas com a ordem ja
	/// invertida. Com o rumo por fora, a ordem quer dizer o que diz.
	///
	/// A AMOSTRAGEM E DE MEIO TILE, como a do arremesso: de tile em tile pula o canto de um lago em que a
	/// caixa dos pes encosta.
	///
	/// ============================ E A RETA TEM DE ESTAR VAZIA DE **GENTE**, E NAO SO DE PAREDE ============================
	/// A versao anterior so afastava o HOST, e por isso ela dependia da sorte: o planeta tem CIDADAOS
	/// (`GameServer.Populacao.cs` povoa cada mundo), e um cidadao parado na reta e um corpo na grade
	/// **que esta bancada nao dirige**. Ele estraga as duas metades de uma vez -- "Alfa parou" vira
	/// "parou em quem?", e a tecla de agarrar pega ELE em vez de nao pegar ninguem.
	///
	/// ISSO FICOU VERMELHO DE VERDADE, e por isso esta escrito aqui: ao esticar a reta de 16 pra 21
	/// tiles o candidato escolhido mudou, caiu ao lado de um cidadao, e a familia 1 reprovou em
	/// *"...e a tecla nao pega ninguem"* -- arrastando 24 linhas depois dela, todas por um aperto que a
	/// bancada nunca pediu. Nenhuma delas tinha nada a ver com o codigo medido.
	///
	/// A CONTA E POR CORPO E NAO POR AMOSTRA (distancia ponto-segmento, uma vez por candidato): a zona
	/// tem dezenas de corpos e o laco de fora tem milhares de candidatos, entao perguntar dentro do laco
	/// das 42 amostras custaria 42x mais pelo mesmo veredito.
	/// ================================================================================================================
	/// </summary>
	private (Vec2 Origem, Facing Rumo)? AcharPalco(ServerPlayer pl, int tiles, Facing[]? ordem = null)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) return null;

		const float T = ZoneCollision.TileSize;
		ordem ??= [Facing.South, Facing.East, Facing.North, Facing.West];

		// TODO CORPO DA ZONA, O HOST INCLUSIVE. A `ZoneList` e a mesma fonte da grade de colisao (ver
		// `MontarAsGrades`) -- se um corpo barra, ele esta aqui. Fotografada UMA vez: nada nasce nem
		// morre durante a varredura.
		Vec2[] corpos = [.. ZoneList(pl.Zone.Hash).Select(c => c.Pos)];

		// A distancia de um corpo ao SEGMENTO do palco -- o `t` preso em [0,1] porque o corpo pode estar
		// atras da origem ou depois da ponta, e ali a distancia certa e a da extremidade.
		static float ateOSegmento(Vec2 p, Vec2 a, Vec2 b)
		{
			Vec2 ab = b - a;
			float len2 = ab.LengthSquared;
			if (len2 < 1e-6f) return (p - a).Length;
			float t = Math.Clamp(((p - a).X * ab.X + (p - a).Y * ab.Y) / len2, 0f, 1f);
			return (p - (a + ab * t)).Length;
		}

		foreach (Facing f in ordem)
		{
			Vec2 dir = MeleeArea.Frente(f);

			for (int raio = 4; raio <= 60; raio += 2)
				for (int ax = -raio; ax <= raio; ax += 2)
					for (int ay = -raio; ay <= raio; ay += 2)
					{
						if (Math.Abs(ax) != raio && Math.Abs(ay) != raio) continue;   // so a borda do anel
						var origem = new Vec2(pl.Pos.X + ax * T, pl.Pos.Y + ay * T);
						if (MoveRules.Occupied(mapa, origem, ModoDeTravessia.APe)) continue;

						// NENHUM CORPO A MENOS DE 2 TILES DA RETA -- o host e os cidadaos pela MESMA
						// linha. Ver o bloco no cabecalho: o corpo que a bancada nao dirige e obstaculo
						// e alvo de tecla ao mesmo tempo.
						Vec2 ponta = origem + dir * (tiles * T);
						bool alguem = false;
						foreach (Vec2 c in corpos)
							if (ateOSegmento(c, origem, ponta) < 2 * T) { alguem = true; break; }
						if (alguem) continue;

						bool livre = true;
						for (int k = 1; k <= tiles * 2 && livre; k++)
						{
							Vec2 p = origem + dir * (k * T * 0.5f);
							if (MoveRules.Occupied(mapa, p, ModoDeTravessia.APe)) livre = false;
						}
						if (livre) return (origem, f);
					}
		}
		return null;
	}

	// =====================================================================
	// A RODADA
	// =====================================================================
	private void RodarBancadaDosDoisCorpos(ServerPlayer pl)
	{
		_dcOk = _dcFalhou = 0;
		GD.Print("[dois] ================ DOIS CORPOS: AGARRAO, COLISAO E O CADAVER ================");

		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		bool mortoGuardado = pl.Ficha.dead, koGuardado = pl.Ficha.KO;
		long relogioGuardado = pl.RelogioDaMorte;
		bool viajouGuardado = pl.MorteJaViajou, aureolaGuardada = pl.EnvAureola;
		float alturaGuardada = pl.Altitude;
		bool voandoGuardado = pl.Voando, nadandoGuardado = pl.Nadando;
		int obrasAntes = _noChao.Count;

		try
		{
			// ---- O PALCO: uma linha reta e livre, medida no mapa de verdade ----
			// Sem isto a bancada montaria os dois corpos contra uma parede e mediria a PAREDE parando
			// quem anda -- verde pelo motivo errado, que e o pior verde que existe.
			// A RETA PRECISA DE 21 TILES, e o numero e o do PIOR percurso que esta bancada encomenda --
			// nao um chute. Sao dois, e o maior manda:
			//   * o passeio da familia 5 anda uns 12 (ver `TiquesDoPasseio`), e o corpo tem que ter
			//     espaco pra PASSAR de Beta quando a grade estiver cega -- sem folga, "nao atravessou" e
			//     "nao teve pra onde ir" dariam a mesma resposta;
			//   * o CONTRA-EXEMPLO da familia 6 arremessa Alfa por 10 tiques, e o arremesso anda
			//     `Empurrao.TilesPorTique` = 2 tiles por tique: **20 tiles**, porque ali Beta paira no
			//     andar 3 e o corpo passa por baixo dele sem parar em nada.
			//
			// COM 16 A BANCADA MEDIA A PAREDE, E ISSO JA FICOU VERMELHO: num mundo em que o mapa fechava
			// logo depois do 16o tile, Alfa terminava o voo NA PEDRA, levava o `Espalhar` da parede que
			// resiste (`GameServer.Empurrao.cs`) e a linha "passando por baixo de Beta o jogado NAO se
			// machuca" reprovava -- por um dano que nao tinha nada a ver com Beta. Verde ou vermelho
			// conforme o spawn do host e o pior tipo de bancada que existe: a que muda de veredito sem o
			// codigo mudar.
			if (AcharPalco(pl, 21) is not { } palco)
			{
				AfirmarDc("PRECONDICAO: ha uma reta de 21 tiles CAMINHAVEL pra montar o palco", false,
						  "nenhum ponto num anel de 60 tiles em volta do host");
				return;
			}
			AfirmarDc($"PRECONDICAO: o palco cabe a {(palco.Origem - pl.Pos).Length / ZoneCollision.TileSize:0} "
					+ $"tiles do host, rumo {palco.Rumo} (21 tiles caminhaveis, agua inclusive)", true);

			Facing rumo = palco.Rumo;
			Vec2 d = MeleeArea.Frente(rumo);
			const float T = ZoneCollision.TileSize;
			Vec2 origem = palco.Origem;

			_alfa = ForjarDois("Alfa (o que pega)", pl.Zone, origem);
			_beta = ForjarDois("Beta (o que apanha)", pl.Zone, origem + d * (3 * T));
			DarOVoo(_alfa);
			DarOVoo(_beta);   // ver `LevantarVoo`: os contra-exemplos de ANDAR exigem um corpo que VOA mesmo

			AfirmarDc("os DOIS corpos existem, na mesma zona e no `_players` (sao corpos SIMULADOS)",
					  _players.ContainsKey(_alfa.Id) && _players.ContainsKey(_beta.Id)
					  && ZoneList(pl.Zone.Hash).Contains(_alfa) && ZoneList(pl.Zone.Hash).Contains(_beta));
			AfirmarDc("...e os dois estao de pe e podem se mexer (senao TUDO abaixo mediria o berco)",
					  PodeMexerOCorpo(_alfa) && PodeMexerOCorpo(_beta));

			OAgarraoNosDoisSentidos(_alfa, _beta, d);
			OArremessoNosDoisSentidos(_alfa, _beta, d, rumo);
			OColoLevaAAltitude(_alfa, _beta, d, rumo);
			AsSoltutasQueLibertam(_alfa, _beta, d, rumo);
			NinguemAtravessaNinguem(_alfa, _beta, d, origem);
			// A 9 VEM AQUI, COLADA NA 5, porque ela e a continuacao dela: a 5 mede "ninguem atravessa
			// ninguem" no CHAO e a 9 mede o mesmo NO AR, que e onde o pedido novo do dono se passa.
			// Ela deixa os dois no chao no fim, que e como a 6 os encontra.
			OCorpoOcupadoNaoEEmpurrado(_alfa, _beta, d, origem);
			OBaqueDoiNosDois(_alfa, _beta, d, origem);
			OCadaverEntreDoisCorpos(pl, _alfa, d);
			OCadaverEAFotoDoMorto(pl, _alfa, _beta, d);

			AfirmarDc("a bancada chegou ao fim (sem esta linha, abortar no meio reportaria '0 falhas')",
					  true);
		}
		catch (Exception e)
		{
			AfirmarDc($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			EscutaDeFeridas = null;
			_cadaverSemFotoDeTeste = false;

			// OS CORPOS FORJADOS SAEM -- e os cadaveres que a rodada produziu junto. `DesfazerOCadaver`
			// nao serve pros forjados (eles nao sao cadaver) e `RemoverNpc` nao serve pros cadaveres
			// (eles nao estao no `_players`), entao a limpeza faz as duas coisas na mao, que e o que a
			// `--cadaverteste` ja faz e pelo mesmo motivo.
			foreach (ServerPlayer f in _dcForjados)
			{
				if (f.AgarrandoId != 0) Soltar(f, MotivoDaSoltura.Tecla);
				if (f.AgarradoPorId != 0) LimparPreso(f);
				_npcsPraTirar.Remove(f);
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
				f.Peer = null;
			}
			// OS CADAVERES QUE **ESTA** RODADA PRODUZIU, e so eles: varrer a zona atras de `ECadaver`
			// levaria junto o corpo de alguem que morreu de verdade enquanto a bancada rodava.
			foreach (ServerPlayer c in _dcCadaveres) DesfazerOCadaver(c);
			_dcForjados.Clear();
			_dcCadaveres.Clear();
			_alfa = _beta = _gama = null;

			// AS LAPIDES QUE ELA ERGUEU SAEM DO DISCO. Elas vao pro `mundo.json` de verdade (e esse e o
			// ponto da familia 7) -- e uma bancada que as deixasse la encheria o mundo do dono de
			// tumulos de teste, um por rodada.
			if (_noChao.Count > obrasAntes)
			{
				_noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
				GravarMundo();
				MandarObras(zonaGuardada);
			}

			// O HOST VOLTA VIVO E NO LUGAR -- mesma disciplina da `--alemteste` e da `--cadaverteste`.
			if (pl.AgarrandoId != 0) Soltar(pl, MotivoDaSoltura.Tecla);
			if (pl.AgarradoPorId != 0) LimparPreso(pl);
			pl.Combate.Reviver();
			pl.Ficha.dead = mortoGuardado;
			pl.Ficha.KO = koGuardado;
			pl.RelogioDaMorte = relogioGuardado;
			pl.MorteJaViajou = viajouGuardado;
			pl.EnvAureola = aureolaGuardada;
			pl.Altitude = alturaGuardada;
			pl.Voando = voandoGuardado;
			pl.Nadando = nadandoGuardado;
			pl.TiquesDeVoo = 0;
			if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);
			else pl.Pos = posGuardada;
			pl.Combate.SincronizarVida();
			MandarAureola(pl);
		}

		GD.Print($"[dois] ================ {_dcOk} passaram, {_dcFalhou} falharam ================");
	}

	/// <summary>Os cadaveres que ESTA bancada produziu -- pra o `finally` recolher so os dela.</summary>
	private readonly List<ServerPlayer> _dcCadaveres = [];

	/// <summary>
	/// AS MASCARAS DE FERIDAS APRESENTADAS A QUEM ENTRA NUMA ZONA (`TrocarFeridas`): pra quem, e o fio
	/// do `S2C.Feridas`. Capturada la pela mesma razao das outras escutas -- o corpo que entra e forjado
	/// e nao tem `Peer`. Nula em jogo.
	/// </summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDeFeridas;

	// =====================================================================
	// 1) AGARRAR -- e NAO agarrar
	// =====================================================================
	/// <summary>
	/// ============================ O SENTIDO POSITIVO: PEGOU, E O APERTO DURA ============================
	/// A segunda metade e a bancada inteira. `Prender` sempre funcionou; o que ja esteve quebrado neste
	/// port era o TIQUE seguinte, que resolvia o preso por `_players.TryGetValue` -- e nem o cadaver nem
	/// o boneco de quem medita estao naquele dicionario. O aperto era desfeito 33 ms depois, calado.
	///
	/// E o gesto e o de PRODUCAO: <see cref="AlternarAgarrao"/>, a mesma funcao que o
	/// `case "agarrar"` do `C2S.Habilidade` chama quando o jogador aperta a tecla. Chamar `Prender`
	/// direto puleria o `PodeMexerOCorpo` e o `CorpoNaFrente`, que sao as duas metades do gesto.
	/// ================================================================================================
	///
	/// ============================ O SENTIDO NEGATIVO: **AS RECUSAS SAO DE ESTADO** ============================
	/// A tarefa pedia *"nao agarra quem nao pode (boneco, reflexo)"*, e a medida contradiz o pedido --
	/// por DESENHO, e o desenho esta escrito em bloco proprio no `GameServer.Agarrao.cs`: o boneco do
	/// corpo largado e o reflexo da mente **PODEM** ser agarrados de proposito. O original tambem os
	/// deixa (`Grabbing.dm:170` so pergunta `!M.grabber`), e recusar o boneco seria abrir a primeira
	/// excecao por IDENTIDADE -- a segunda seria o cadaver, e o cadaver e o pedido 3 do dono.
	///
	/// Entao esta bancada mede as recusas que o jogo REALMENTE tem, que sao tres e todas de ESTADO:
	/// ja preso por outro, intocavel, e fora de alcance/andar. E mede tambem o outro lado da
	/// contradicao -- que o boneco **e** agarravel --, pra que o dia em que alguem "consertar" o
	/// pedido fechando a porta do boneco reprove aqui em vez de apagar o cadaver em silencio.
	/// ======================================================================================================
	/// </summary>
	private void OAgarraoNosDoisSentidos(ServerPlayer a, ServerPlayer b, Vec2 d)
	{
		GD.Print("[dois] -- 1) agarra quem pode, e nao agarra quem nao pode --");

		Encostar(a, b, d);

		// ---- O SENTIDO POSITIVO ----
		AfirmarDc("o `CorpoNaFrente` de Alfa acha Beta (o cone e o alcance sao os mesmos do soco)",
				  CorpoNaFrente(a) == b);

		AlternarAgarrao(a);   // A TECLA, e nao o `Prender`
		AfirmarDc("A TECLA PEGOU: os dois lados apontam um pro outro",
				  a.AgarrandoId == b.Id && b.AgarradoPorId == a.Id,
				  $"agarrando={a.AgarrandoId} agarradoPor={b.AgarradoPorId}");
		AfirmarDc("...e o modo comecou em SEGURANDO (`grabMode=1`, `Grabbing.dm:170-182`)",
				  a.ModoDoAgarrao == ModoDeAgarrao.Segurando);
		AfirmarDc("...e Beta parou de andar pelo funil de vetor (`grabParalysis`, `Grabbing.dm:183`)",
				  !PodeMexerOCorpo(b));

		// ============================ O DEFEITO INJETADO #1 ============================
		// O criterio e "o aperto sobrevive ao tique". O defeito e o HISTORICO deste sistema, remontado
		// pelo lado do dado: **o corpo preso vivendo numa lista que o tique nao le**. Era isso que
		// `_players.TryGetValue` produzia contra o boneco e contra o cadaver -- os dois vivem so na
		// `ZoneList` --, e tirar Beta da `ZoneList` reproduz exatamente a mesma cegueira contra um
		// corpo que a bancada acabou de prender.
		// ==============================================================================
		// ============================ O DEFEITO INJETADO #1 ============================
		// O criterio e "o aperto sobrevive ao tique". O defeito injetado e **as DUAS VERDADES sobre o
		// mesmo aperto**: o lado do preso apontando pra outra pessoa. E o defeito que o desenho de porta
		// unica do `Soltar` existe pra impedir -- oito caminhos de soltura escrevendo quatro campos em
		// dois corpos e a receita pra alguem ficar preso por um campo que sobrou --, e o `TickDoAgarrao`
		// tem uma linha so pra ele (`d.AgarradoPorId != a.Id` -> limpa o meu lado e sai).
		//
		// **O CRITERIO NAO REMONTA O APERTO**, de proposito: se ele o remontasse, a primeira coisa que
		// faria seria sobrescrever a injecao -- e a bancada mediria o proprio setup, verde pra sempre.
		// Quem repoe o estado de partida e o `consertar`.
		// ==============================================================================
		Mutacao(AfirmarDc,
				"O APERTO SOBREVIVE AO TIQUE (tres tiques do mundo depois, os dois ainda apontam um pro outro)",
				"o lado do PRESO apontando pra outra pessoa -- as duas verdades sobre o mesmo aperto",
				() => { Tiques(3); return a.AgarrandoId == b.Id && b.AgarradoPorId == a.Id; },
				() => b.AgarradoPorId = IdBaseDosDois + 999,
				() => { Soltar(a, MotivoDaSoltura.Tecla); LimparPreso(b);
						Encostar(a, b, d); AlternarAgarrao(a); });

		// ---- O SENTIDO NEGATIVO 1: JA PRESO POR OUTRO (`!M.grabber`, a unica recusa do DM) ----
		// ============================ O TERCEIRO NASCE DO OUTRO LADO, E ISSO CUSTOU UMA RODADA ============================
		// Ele nascia com `Encostar(gama, b, d)` -- ou seja, no ponto EXATO em que Alfa ja estava (os dois
		// "encostados em Beta" pelo mesmo lado sao o mesmo pixel). O `CorpoNaFrente` de Gama achava ALFA
		// a distancia zero e o agarrava, e dai pra frente Alfa estava `grabParalysis` e **todas** as
		// familias seguintes mediram um corpo que nao podia fazer nada. Foram doze linhas vermelhas por
		// um sinal.
		// ==============================================================================================================
		_gama = ForjarDois("Gama (o terceiro)", b.Zone, b.Pos);
		Encostar(_gama, b, d * -1);   // do lado OPOSTO ao de Alfa
		AfirmarDc("NAO AGARRA quem ja esta preso por outro (`!M.grabber`, `Grabbing.dm:170`)",
				  CorpoNaFrente(_gama) != b);
		AlternarAgarrao(_gama);
		AfirmarDc("...e a tecla do terceiro nao pega Beta", _gama.AgarrandoId != b.Id,
				  $"gama agarrando={_gama.AgarrandoId}");
		AfirmarDc("...e o aperto do PRIMEIRO continua de pe (o terceiro nao roubou o preso)",
				  a.AgarrandoId == b.Id && b.AgarradoPorId == a.Id);
		if (_gama.AgarrandoId != 0) Soltar(_gama, MotivoDaSoltura.Tecla);

		// E ELE SAI DO PALCO. Um terceiro corpo parado no meio do caminho vira obstaculo silencioso nas
		// familias de COLISAO -- e "Alfa parou" deixaria de dizer em quem.
		_gama.Pos = b.Pos + d * (60 * ZoneCollision.TileSize);

		Soltar(a, MotivoDaSoltura.Tecla);
		Tiques(2);
		AfirmarDc("SOLTOU: os dois lados limpos, e Beta volta a andar",
				  a.AgarrandoId == 0 && b.AgarradoPorId == 0 && PodeMexerOCorpo(b));

		// ---- O SENTIDO NEGATIVO 2: INTOCAVEL (a mesma recusa que o soco ja aplica) ----
		Encostar(a, b, d);
		b.Combate.Carencia = 5;
		AfirmarDc("NAO AGARRA quem esta INTOCAVEL (carencia de renascimento / cinematica)",
				  b.Combate.Intocavel && CorpoNaFrente(a) != b);
		AlternarAgarrao(a);
		AfirmarDc("...e a tecla nao pega ninguem", a.AgarrandoId == 0);
		b.Combate.Carencia = 0;
		AfirmarDc("...e passada a carencia ele volta a ser agarravel (era ESTADO, e nao identidade)",
				  !b.Combate.Intocavel && CorpoNaFrente(a) == b);

		// ---- O SENTIDO NEGATIVO 3: OUTRO ANDAR ----
		// A regra e a do soco (`Voo.PodeAcertar`, assimetrica): quem paira rasante alcanca quem esta no
		// chao, e nao o contrario. Beta a DOIS andares de altura sai do alcance nos dois sentidos.
		b.Altitude = Voo.AlturaQueAtravessa * 6;
		AfirmarDc($"NAO AGARRA quem esta em outro ANDAR (Alfa no andar {Voo.Andar(a.Altitude)}, "
				+ $"Beta no {Voo.Andar(b.Altitude)})",
				  CorpoNaFrente(a) != b);
		b.Altitude = 0;
		AfirmarDc("...e de volta ao chao ele e achavel de novo", CorpoNaFrente(a) == b);

		// ---- O SENTIDO NEGATIVO 4: LONGE ----
		Vec2 longe = b.Pos;
		b.Pos = a.Pos + d * (10 * ZoneCollision.TileSize);
		AfirmarDc("NAO AGARRA quem esta fora do alcance de braco (`MeleeArea.NoAlcance`)",
				  CorpoNaFrente(a) != b);
		b.Pos = longe;

		// ============================ E O QUE **NAO** E RECUSA, MEDIDO PRA NAO VIRAR ============================
		// O boneco de quem esta em transe **pode** ser agarrado, e a linha existe pra que fechar essa
		// porta um dia reprove AQUI -- em vez de apagar o cadaver do jogo em silencio, que e o que
		// aconteceria (o cadaver e a mesma coisa que o boneco, um instante depois).
		// ====================================================================================================
		var boneco = new ServerPlayer
		{
			Id = IdBaseDosDois + 900,
			Peer = null,
			DonoDoCorpoLargado = a.Id,
			Name = "o corpo largado",
			Zone = a.Zone, Pos = a.Pos + d * 8,
			Race = a.Race, Ficha = a.Ficha, Combate = a.Combate,
			Forma = a.Forma, Livro = a.Livro, Niveis = a.Niveis,
			Visual = a.Visual.Copiar(), LastInputMs = NowMs(),
		};
		ZoneList(boneco.Zone.Hash).Add(boneco);
		AfirmarDc("o BONECO do corpo largado CONTINUA agarravel (e a decisao escrita no `Agarrao.cs`: "
				+ "recusa-lo seria a primeira excecao por identidade, e a segunda seria o cadaver)",
				  CorpoNaFrente(a) == boneco);
		ZoneList(boneco.Zone.Hash).Remove(boneco);
	}

	/// <summary>
	/// ============================ QUANTO ESTE VETOR ANDOU **NO RUMO DO PALCO** ============================
	/// A projecao escalar, e ela existe porque o palco desta bancada e montado no primeiro eixo LIVRE do
	/// mapa -- que pode ser X, Y, e nos dois sinais. Perguntar `a.Pos.X < b.Pos.X` daria a resposta certa
	/// num berco e a resposta invertida noutro, e a bancada mudaria de veredito conforme o ponto de
	/// nascimento da conta -- que e o pior tipo de bancada: a que so falha na maquina dos outros.
	///
	/// Vive aqui e nao no `Vec2` do Core de proposito: o jogo inteiro nunca precisou de produto escalar
	/// (as regras de alcance sao por caixa e por cone), e uma API nova no Core com um unico consumidor
	/// de bancada e exatamente o "codigo morto vestido de utilidade" que este repo ja catalogou.
	/// ==================================================================================================
	/// </summary>
	private static float NoEixo(Vec2 v, Vec2 rumo) => v.X * rumo.X + v.Y * rumo.Y;

	/// <summary>
	/// POE UM CORPO ENCOSTADO NO OUTRO, olhando pra ele. Meio tile de distancia -- dentro do cone do
	/// braco e fora da caixa dos pes, que e onde o jogo poe duas pessoas que vao se pegar.
	/// </summary>
	private static void Encostar(ServerPlayer a, ServerPlayer alvo, Vec2 d)
	{
		a.Altitude = 0;
		alvo.Altitude = 0;
		a.Pos = alvo.Pos - d * (ZoneCollision.TileSize * 0.75f);
		a.Facing = MoveRules.FacingFrom(d, a.Facing);
	}

	// =====================================================================
	// 2) ARREMESSAR -- andar segurando joga; segurar parado nao
	// =====================================================================
	/// <summary>
	/// ============================ O ARREMESSO NAO TEM TECLA, E ISSO E DO DM ============================
	/// Segurando (modo 1), o primeiro PASSO arremessa -- `mob/OnStep()`, `Throw.dm:1-3`. Por isso o
	/// sentido positivo desta familia e "andou segurando" e o negativo e "ficou parado segurando": se o
	/// arremesso disparasse sozinho, o modo SEGURANDO nao existiria e o duplo toque (o pedido do dono)
	/// nunca teria como ser alcancado.
	///
	/// **A PROVA E A POSICAO, E NAO O CAMPO.** Um corpo com `TiquesDeVoo > 0` que nao anda e exatamente
	/// o defeito que este trabalho ja consertou (o `TickDoEmpurrao` varrendo `_players`), e ele fecharia
	/// verde numa linha que perguntasse so pelo campo.
	/// ================================================================================================
	/// </summary>
	private void OArremessoNosDoisSentidos(ServerPlayer a, ServerPlayer b, Vec2 d, Facing rumo)
	{
		GD.Print("[dois] -- 2) andar segurando ARREMESSA; parado, nao --");

		Encostar(a, b, d);
		AlternarAgarrao(a);
		AfirmarDc("PRECONDICAO: Alfa esta segurando Beta", a.AgarrandoId == b.Id);

		// ---- O SENTIDO NEGATIVO: PARADO NAO JOGA ----
		a.Moving = false;
		Tiques(5);
		AfirmarDc("SEGURANDO PARADO nao arremessa ninguem (senao o modo 'segurar' nao existiria)",
				  a.AgarrandoId == b.Id && b.TiquesDeVoo == 0);

		// ---- O SENTIDO POSITIVO: ANDOU, JOGOU ----
		Vec2 antes = b.Pos;
		a.Facing = rumo;
		a.Moving = true;
		TiqueDoMundo();

		AfirmarDc("ANDOU SEGURANDO: o aperto se desfez e o voo foi armado (`Throw.dm:1-3`)",
				  a.AgarrandoId == 0 && b.AgarradoPorId == 0 && b.TiquesDeVoo > 0,
				  $"tiques={b.TiquesDeVoo}");
		a.Moving = false;

		Tiques(8);
		AfirmarDc("...E O CORPO ANDOU DE VERDADE (a posicao, e nao o campo)",
				  (b.Pos - antes).Length > ZoneCollision.TileSize,
				  $"andou {(b.Pos - antes).Length:0.0} px");

		Tiques(40);
		AfirmarDc("...e o voo TERMINA (ele nao fica com `TiquesDeVoo` preso, o sintoma antigo)",
				  b.TiquesDeVoo <= 0, $"{b.TiquesDeVoo}");
	}

	// =====================================================================
	// 3) CARREGAR -- a altitude, medida NOS DOIS
	// =====================================================================
	/// <summary>
	/// *"se vc apertar o botao de grab 2 vezes e VOAR, a pessoa agarrada vai ser considerada como VOANDO
	/// tb NA MESMA ALTITUDE q vc"* -- o pedido, medido nos dois corpos.
	///
	/// **A ALTURA DE ALFA NAO E ESCRITA POR ESTA BANCADA.** Ele aprende a voar (a mesma concessao do
	/// `--vooteste`), liga o voo pelo `AlternarVoo` de producao e SOBE segurando a tecla de subir
	/// (`Comando.QuerSubir`, que e o mesmo bit que o `Input` do jogador escreve). O que se mede depois e
	/// se Beta acompanhou -- e um Beta que acompanha uma altura escrita a mao nao provaria nada sobre
	/// o voo.
	/// </summary>
	private void OColoLevaAAltitude(ServerPlayer a, ServerPlayer b, Vec2 d, Facing rumo)
	{
		GD.Print("[dois] -- 3) o duplo toque carrega, e o carregado sobe junto --");

		b.TiquesDeVoo = 0;
		Encostar(a, b, d);
		AlternarAgarrao(a);   // 1o toque: segurando
		AlternarAgarrao(a);   // 2o toque: carregando -- `grabMode=2`, `Grabbing.dm:78-84`
		AfirmarDc("O DUPLO TOQUE CARREGA (`grabMode=2`, \"You pick up [grabbee].\")",
				  a.ModoDoAgarrao == ModoDeAgarrao.Carregando && a.AgarrandoId == b.Id);

		// ---- ALFA DECOLA E SOBE, pelo caminho do jogador ----
		LevantarVoo(a);
		AfirmarDc("Alfa decolou pelo `AlternarVoo` de producao (o mesmo verbo `Fly` do jogador)",
				  a.Voando);
		AfirmarDc($"...e ele SUBIU de verdade (altitude {a.Altitude:0} px = andar {Voo.Andar(a.Altitude)})",
				  a.Altitude > ZoneCollision.TileSize, $"{a.Altitude:0.0} px");

		// ---- A MEDIDA DOS DOIS, LADO A LADO ----
		AfirmarDc($"O CARREGADO ESTA NA MESMA ALTITUDE (Alfa {a.Altitude:0.0} px, Beta {b.Altitude:0.0} px)",
				  Math.Abs(a.Altitude - b.Altitude) < 0.01f, $"{a.Altitude} x {b.Altitude}");
		AfirmarDc($"...e portanto no MESMO ANDAR (Alfa {Voo.Andar(a.Altitude)}, Beta {Voo.Andar(b.Altitude)})",
				  Voo.Andar(a.Altitude) == Voo.Andar(b.Altitude));
		AfirmarDc("...e no mesmo ponto do mapa (`grabbee.loc = locate(x,y,z)`, `Grabbing.dm:203`)",
				  (b.Pos - a.Pos).LengthSquared < 0.01f);
		AfirmarDc("...e o `EntityState.Voando` DELE e verdade -- o cliente desenha pose de voo e sombra "
				+ "por ALTURA, entao o corpo no colo aparece voando sem um bit novo no protocolo",
				  EstadoDe(b, NowMs()).Voando);

		// ---- O SENTIDO NEGATIVO: ELE NAO PAGA KI DE VOO ----
		AfirmarDc("...MAS o campo `Voando` dele continua FALSO: quem paga o Ki e quem carrega, e cobrar "
				+ "dos dois seria cobrar duas vezes pelo mesmo par de pes fora do chao",
				  !b.Voando);
		AfirmarDc("...e o modo de travessia dele acompanha a altura (a agua deixa de barra-lo) -- o "
				+ "\"modo de travessia\" do pedido nao foi escrito: ele e CONSEQUENCIA da altura",
				  ModoDeTravessiaDe(b) == ModoDeTravessia.Voando,
				  ModoDeTravessiaDe(b).ToString());

		// ============================ A GUARDA DO `TickDoVoo`, MEDIDA ONDE ELA AGE ============================
		// **ACHADO POR MUTACAO, E ELE MUDOU ESTA BANCADA.** Apagando a linha `if (SendoCarregado(pl))
		// return` do `TickDoVoo` a mao, a bancada inteira continuou VERDE -- 103 de 103. A razao esta no
		// proprio cabecalho daquela guarda, lida ate o fim: os dois sistemas escrevem a mesma altura no
		// MESMO tique, e o agarrao escreve DEPOIS (`TickDosRelogiosDoCorpo` vem antes de `TickDoAgarrao`
		// no `Tick`). O corpo nao cai; o que existe e um serrilhado que morre dentro do tique.
		//
		// Ou seja: **nenhuma medida de fim de tique pode ver este defeito** -- e o snapshot tambem sai no
		// fim do tique, o que quer dizer que o jogador nao veria. A guarda continua certa (dois donos pro
		// mesmo campo e um defeito latente: basta a ordem do `Tick` mudar um dia), mas ela so tem como
		// ser medida ENTRE os dois passos.
		//
		// Entao esta linha roda o relogio do corpo SOZINHO, sem o agarrao logo atras, e pergunta se a
		// altura do passageiro sobreviveu. Sem a guarda, ele desce ~17 px (16 tiles/s num tique de 30 Hz).
		// ====================================================================================================
		float alturaAntesDoRelogio = b.Altitude;
		TickDosRelogiosDoCorpo(Protocol.TickSeconds);
		AfirmarDc("...e o RELOGIO DO CORPO rodando sozinho NAO derruba o passageiro (a guarda "
				+ "`if (SendoCarregado(pl)) return` do `TickDoVoo` -- o unico ponto onde ela e visivel)",
				  Math.Abs(b.Altitude - alturaAntesDoRelogio) < 0.01f,
				  $"{b.Altitude:0.00} x {alturaAntesDoRelogio:0.00}");

		// ============================ O DEFEITO INJETADO #2 ============================
		// A guarda `if (SendoCarregado(pl)) return` no topo do `TickDoVoo` e o que impede os dois
		// sistemas de brigarem pelo mesmo campo: o carregado tem altura E `Voando` falso, que e
		// exatamente a assinatura de QUEM PERDEU O VOO NO AR. Sem ela o ramo de queda o puxa a 16
		// tiles/s enquanto o agarrao o traz de volta, 30 vezes por segundo.
		//
		// A injecao e pelo dado: com o modo em SEGURANDO, `SendoCarregado` responde falso -- e a
		// altura de Beta passa a ser tratada como queda. E o defeito exato, sem tocar no codigo.
		// ==============================================================================
		// ============================ E O CONSERTO TEM QUE REMONTAR O COLO INTEIRO ============================
		// A primeira versao repunha so o MODO e as duas alturas, e a terceira passada do `Mutacao`
		// reprovou. A causa e a propria regra: com o modo em SEGURANDO e Beta ja caido, o tique aplica a
		// soltura por DISTANCIA (`AlcancaPelaAltura` diz nao entre o andar 3 e o chao) -- ou seja o
		// defeito nao so derruba o passageiro, **ele desfaz o aperto**. Repor tres campos deixava o
		// mundo com um agarrao que nao existe mais.
		//
		// Entao o conserto pousa, larga, pega de novo (duas teclas) e decola de novo -- o caminho de
		// producao inteiro. E a licao e a de sempre: um `consertar` que repoe MENOS do que o defeito
		// estragou faz a terceira passada medir destroco em vez de medir a regra.
		// ==================================================================================================
		Mutacao(AfirmarDc,
				"O CARREGADO SE MANTEM NO AR (dez tiques depois ele continua na altitude de quem o carrega)",
				"o modo do aperto deixando de ser CARREGANDO -- a guarda do `TickDoVoo` cega",
				() => { Tiques(10); return Math.Abs(a.Altitude - b.Altitude) < 0.01f && b.Altitude > 0; },
				() => a.ModoDoAgarrao = ModoDeAgarrao.Segurando,
				() =>
				{
					if (a.AgarrandoId != 0) Soltar(a, MotivoDaSoltura.Tecla);
					LimparPreso(b);
					Pousar(a);
					Encostar(a, b, d);
					AlternarAgarrao(a);
					AlternarAgarrao(a);
					LevantarVoo(a);
				});

		// ---- E LARGADO NO AR, ELE CAI ----
		float laEmCima = b.Altitude;
		Soltar(a, MotivoDaSoltura.Tecla);
		Tiques(5);
		AfirmarDc("LARGADO no ar, o corpo CAI (a altura volta a ser do `TickDoVoo` no instante da soltura)",
				  b.Altitude < laEmCima, $"{b.Altitude:0.0} x {laEmCima:0.0}");
		Tiques(200);
		AfirmarDc("...ate o chao", b.Altitude <= 0f, $"{b.Altitude:0.0}");

		if (a.Voando) AlternarVoo(a);
		a.Altitude = 0;
		Tiques(120);
	}

	// =====================================================================
	// 4) AS SOLTURAS -- ninguem fica preso
	// =====================================================================
	/// <summary>
	/// ============================ NINGUEM FICA PRESO, E SAO DUAS PASSADAS ============================
	/// A passada 1 do <see cref="TickDoAgarrao"/> so alcanca quem AINDA esta na lista de corpos
	/// simulados; a passada 2 -- a varredura orfa -- e a rede de seguranca pra quando o corpo de quem
	/// segurava sai do mundo. Esta familia mede as duas, e mede tambem o contra-exemplo: **um aperto
	/// sadio nao se desfaz sozinho**, senao "ninguem fica preso" seria satisfeito por um agarrao que
	/// nunca funciona.
	/// ============================================================================================
	/// </summary>
	private void AsSoltutasQueLibertam(ServerPlayer a, ServerPlayer b, Vec2 d, Facing rumo)
	{
		GD.Print("[dois] -- 4) quem carrega cai, muda de zona ou some: o carregado e SOLTO --");

		// ---- O CONTRA-EXEMPLO, E ELE VEM PRIMEIRO ----
		Encostar(a, b, d);
		AlternarAgarrao(a);
		AlternarAgarrao(a);
		Tiques(30);
		AfirmarDc("CONTRA-EXEMPLO: com tudo sadio, trinta tiques NAO desfazem o aperto (senao 'ninguem "
				+ "fica preso' seria satisfeito por um agarrao que nunca funciona)",
				  a.AgarrandoId == b.Id && a.ModoDoAgarrao == ModoDeAgarrao.Carregando);

		// ---- A) QUEM CARREGA E NOCAUTEADO ---- o `if(KO)` de `Grabbing.dm:192`
		a.Ficha.KO = true;
		TiqueDoMundo();
		AfirmarDc("NOCAUTE de quem carrega: SOLTOU (braco desmaiado nao segura)",
				  a.AgarrandoId == 0 && b.AgarradoPorId == 0);
		AfirmarDc("...e Beta volta a poder se mexer no MESMO tique",
				  PodeMexerOCorpo(b));
		a.Ficha.KO = false;
		a.Combate.Reviver();

		// ---- B) TROCA DE ZONA ---- o `grabbee.z != usr.z` de `:192`
		Encostar(a, b, d);
		AlternarAgarrao(a);
		AlternarAgarrao(a);
		AfirmarDc("PRECONDICAO: pegou de novo, e carregando", a.ModoDoAgarrao == ModoDeAgarrao.Carregando);

		ZoneKey zonaDeBeta = b.Zone;
		Vec2 posDeBeta = b.Pos;
		ZoneList(b.Zone.Hash).Remove(b);
		b.Zone = ZoneKey.Premade(Alem.ZonaDoOutroMundo);            // qualquer zona diferente serve: o `if` e sobre o hash
		ZoneList(b.Zone.Hash).Add(b);
		TiqueDoMundo();
		AfirmarDc("ZONA DIFERENTE: SOLTOU (a linha cobre passagem, pouso, nave, Sala do Tempo, mente e "
				+ "a viagem pro Outro Mundo de uma vez -- todas terminam noutra zona)",
				  a.AgarrandoId == 0 && b.AgarradoPorId == 0);
		ZoneList(b.Zone.Hash).Remove(b);
		b.Zone = zonaDeBeta;
		b.Pos = posDeBeta;
		ZoneList(b.Zone.Hash).Add(b);

		// ---- C) QUEM CARREGA SOME DO MUNDO (logout / NPC removido) ----
		Encostar(a, b, d);
		AlternarAgarrao(a);
		AfirmarDc("PRECONDICAO: pegou pela terceira vez", a.AgarrandoId == b.Id);

		// ============================ O DEFEITO INJETADO #3 ============================
		// Aqui o criterio e a VARREDURA ORFA, e o defeito injetado e a pergunta que ela existe pra
		// responder: **que lista ela varre?** Varrendo o `_players`, um corpo que so vive na `ZoneList`
		// -- o boneco, o CADAVER -- ficaria com `AgarradoPorId` apontando pra um fantasma pra sempre,
		// e o `CorpoNaFrente` (que recusa quem ja esta preso) o tornaria **impossivel de agarrar** --
		// ou seja, um corpo que ninguem mais consegue tirar do caminho nem levar pra enterrar.
		//
		// A injecao poe Beta fora da `ZoneList`: e a mesma cegueira, do lado do dado.
		// ==============================================================================
		List<ServerPlayer> zonaB = ZoneList(b.Zone.Hash);

		// ============================ O CRITERIO MONTA O PROPRIO ESTADO DE PARTIDA ============================
		// A primeira versao deste bloco prendia FORA e media DENTRO -- e a primeira passada do `Mutacao`
		// consumia o aperto (ela liberta o preso, que e o que ela mede). A segunda passada, ja com o
		// defeito no ar, encontrava `AgarradoPorId == 0` **de antes** e dava a resposta certa pelo motivo
		// errado: verde com o defeito de pe, que e exatamente o que o `Mutacao` existe pra impedir.
		//
		// Toda passada agora prende, mede e desmonta. E se o defeito impedir ate a MONTAGEM (e impede: um
		// corpo fora da `ZoneList` nao e achado pelo `CorpoNaFrente`), a linha reprova pela mesma
		// cegueira -- o preso vivendo numa lista que este sistema nao le, que e o defeito historico dele.
		// ==================================================================================================
		bool AVarreduraLiberta()
		{
			if (a.AgarrandoId != 0) Soltar(a, MotivoDaSoltura.Tecla);
			LimparPreso(b);
			Encostar(a, b, d);
			AlternarAgarrao(a);
			if (a.AgarrandoId != b.Id) return false;   // nem montar deu: o defeito ja mordeu aqui

			_players.Remove(a.Id);       // "deslogou" -- a passada 1 do tique nao o alcanca mais
			Tiques(3);
			bool livre = b.AgarradoPorId == 0;

			_players[a.Id] = a;
			a.AgarrandoId = 0;
			a.ModoDoAgarrao = ModoDeAgarrao.Nenhum;
			LimparPreso(b);
			return livre;
		}

		Mutacao(AfirmarDc,
				"QUEM SEGURAVA SUMIU DO MUNDO: a varredura orfa LIBERTA o preso",
				"Beta fora da `ZoneList` -- o preso vivendo na lista que o sistema nao le",
				AVarreduraLiberta,
				() => zonaB.Remove(b),
				() => { if (!zonaB.Contains(b)) zonaB.Add(b); LimparPreso(b);
						a.AgarrandoId = 0; a.ModoDoAgarrao = ModoDeAgarrao.Nenhum; });

		AfirmarDc("...e ele volta a andar (o `grabParalysis` saiu junto)", PodeMexerOCorpo(b));

		if (a.AgarrandoId != 0) Soltar(a, MotivoDaSoltura.Tecla);
		Tiques(2);
		AfirmarDc("no fim da familia, ninguem ficou preso",
				  a.AgarrandoId == 0 && b.AgarradoPorId == 0 && PodeMexerOCorpo(b));
	}

	// =====================================================================
	// 5) NINGUEM ATRAVESSA NINGUEM -- as tres linhas, e o contra-exemplo
	// =====================================================================
	/// <summary>
	/// *"faca com q personagens N CONSIGAM PASSAR DENTRO DO OUTRO andando ou por KNOCK BACK ou por ser
	/// JOGADO pelo grab"* -- as tres do pedido, uma linha cada, e a quarta que o pedido NAO pede: quem
	/// esta em outro andar de voo **atravessa**.
	///
	/// ============================ POR QUE O CONTRA-EXEMPLO E OBRIGATORIO ============================
	/// Sem ele, "ninguem atravessa ninguem" fica verde com uma colisao que barra todo mundo -- inclusive
	/// quem esta dez tiles acima --, e o resultado e um mundo em que um corpo pairando vira poste
	/// invisivel pra quem anda embaixo. O jogador chamaria isso de travamento, e nenhuma linha desta
	/// bancada teria ficado vermelha.
	/// ============================================================================================
	/// </summary>
	private void NinguemAtravessaNinguem(ServerPlayer a, ServerPlayer b, Vec2 d, Vec2 origem)
	{
		GD.Print("[dois] -- 5) corpo nao atravessa corpo: a pe, arremessado e por knockback --");

		const float T = ZoneCollision.TileSize;

		// ---------------- A PE ----------------
		void Montar()
		{
			a.Altitude = b.Altitude = 0;
			a.Voando = b.Voando = false;
			a.TiquesDeVoo = b.TiquesDeVoo = 0;
			a.Pos = origem;
			b.Pos = origem + d * (3 * T);
			a.Facing = MoveRules.FacingFrom(d, a.Facing);
		}

		bool AtravessouAPe()
		{
			Montar();
			Vec2 alvo = b.Pos;
			Tiques(TiquesDoPasseio, () => AplicarComando(a, new Comando { Rumo = d }, Protocol.TickSeconds));
			// "ATRAVESSOU" e ter passado do outro lado -- projetado no rumo, e nao em X: o palco pode
			// ter sido montado no eixo Y.
			float alem = NoEixo(a.Pos - alvo, d);
			GD.Print($"[dois]   (a pe, grade {(_dcGradeCega ? "CEGA" : "normal")}: Alfa parou "
				   + $"{alem:0.0} px alem de Beta -- andou {NoEixo(a.Pos - origem, d):0.0} px)");
			return alem > 0;
		}

		AfirmarDc($"PRECONDICAO: Alfa anda de verdade quando nao ha ninguem na frente "
				+ "(senao 'ele parou' seria 'ele nunca saiu do lugar')",
				  MediuPasso(a, d, origem));

		// ============================ O DEFEITO INJETADO #4 ============================
		// A grade de corpos ESVAZIADA depois de montada e, ao mesmo tempo, os dois defeitos que este
		// sistema ja teve: a grade montada com a fonte errada (`_players`, que perde o boneco e o
		// cadaver) e a grade montada FORA DE HORA (descrevendo o quadro passado). Nos dois casos a
		// consulta responde "nao ha ninguem aqui" -- e e exatamente isso que o `Recomecar` produz.
		// ==============================================================================
		Mutacao(AfirmarDc,
				"A PE: Alfa anda contra Beta e NAO passa por dentro dele",
				"a grade de corpos esvaziada depois de montada -- a fonte errada / a ordem errada",
				() => !AtravessouAPe(),
				() => _dcGradeCega = true,
				() => _dcGradeCega = false);

		AfirmarDc("...e ele parou ENCOSTADO, e nao a meio mapa de distancia (a caixa dos pes, "
				+ $"{2 * MoveRules.BodyHalfW:0} px de largura)",
				  Math.Abs(NoEixo(b.Pos - a.Pos, d)) < 2.5f * T,
				  $"{NoEixo(b.Pos - a.Pos, d):0.0} px");

		// ---------------- O CONTRA-EXEMPLO: ANDARES DIFERENTES ----------------
		// BETA VOA DE VERDADE (ver `LevantarVoo`): escrever `Altitude` a mao faria o `TickDoVoo` ler
		// "altura sem voo" e derruba-lo durante os proprios tiques da medida -- e a bancada mediria dois
		// corpos no chao chamando isso de "andares diferentes".
		Montar();
		Vec2 alvoDoAlto = b.Pos;
		LevantarVoo(b);
		AfirmarDc($"PRECONDICAO do contra-exemplo: Beta esta MESMO no ar (andar {Voo.Andar(b.Altitude)}, "
				+ $"{b.Altitude:0} px) e voando por conta propria",
				  b.Voando && Voo.Andar(b.Altitude) > 0);
		Tiques(TiquesDoPasseio, () => AplicarComando(a, new Comando { Rumo = d }, Protocol.TickSeconds));
		AfirmarDc($"CONTRA-EXEMPLO: com Beta no andar {Voo.Andar(b.Altitude)} e Alfa no chao, Alfa "
				+ "ATRAVESSA (senao um corpo pairando viraria poste invisivel pra quem anda embaixo)",
				  NoEixo(a.Pos - alvoDoAlto, d) > 0,
				  $"parou {NoEixo(a.Pos - alvoDoAlto, d):0.0} px do ponto");
		Pousar(b);

		// ---------------- ARREMESSADO PELO AGARRAO ----------------
		Montar();
		_gama ??= ForjarDois("Gama (o terceiro)", a.Zone, origem);
		_gama.Altitude = 0;
		_gama.TiquesDeVoo = 0;
		_gama.Pos = origem - d * (0.75f * T);
		_gama.Facing = MoveRules.FacingFrom(d, a.Facing);
		a.Pos = origem;

		// Gama pega ALFA e o joga contra BETA.
		AlternarAgarrao(_gama);
		AfirmarDc("PRECONDICAO: Gama pegou Alfa pra joga-lo contra Beta", _gama.AgarrandoId == a.Id);
		_gama.Facing = MoveRules.FacingFrom(d, a.Facing);
		_gama.Moving = true;
		TiqueDoMundo();
		_gama.Moving = false;
		AfirmarDc("PRECONDICAO: o arremesso do agarrao armou o voo de Alfa", a.TiquesDeVoo > 0);

		// ============================ O ARREMESSO PRECISA ALCANCAR BETA, E ISSO NAO E OBVIO ============================
		// **UM BURACO ACHADO POR MUTACAO**, e nao por leitura: com `ClasseDeCorpo.Bloqueia` desligado a
		// mao, oito linhas desta bancada ficaram vermelhas e ESTA ficou verde. A causa: `TiquesDoArremesso`
		// sorteia e o arremesso do agarrao saiu com **1 tique** -- dois tiles --, e Beta estava a tres. O
		// corpo parava antes de chegar nele, e "nao atravessou" era verdade por nao ter chegado.
		//
		// Agora Beta e posto DENTRO do alcance do arremesso que de fato saiu, e a linha abaixo cobra isso
		// em voz alta. Uma checagem que nao pode falhar nao esta checando nada.
		// ==========================================================================================================
		// O PISO DE 1,25 TILE E DO OUTRO LADO DA MESMA ARMADILHA: colado demais, Alfa e Beta ja se tocam
		// no instante do arremesso -- e o `jaSobrepondo` do `GradeDeCorpos` (que existe pra ninguem ficar
		// travado) ignoraria Beta de proposito. A linha ficaria verde por um mecanismo que nao e o que
		// ela mede.
		float alcance = a.TiquesDeVoo * (float)Empurrao.TilesPorTique * T;
		b.Pos = a.Pos + d * Math.Clamp(alcance * 0.6f, 1.25f * T, alcance - 0.4f * T);
		AfirmarDc($"PRECONDICAO: Beta esta DENTRO do alcance do arremesso ({alcance / T:0.0} tiles de voo, "
				+ $"Beta a {NoEixo(b.Pos - a.Pos, d) / T:0.0}) -- senao 'nao atravessou' seria 'nao chegou'",
				  NoEixo(b.Pos - a.Pos, d) < alcance && NoEixo(b.Pos - a.Pos, d) > T);

		Vec2 betaEstavaEm = b.Pos;
		Tiques(40);
		AfirmarDc("ARREMESSADO PELO GRAB: o corpo jogado NAO passa por dentro de quem esta no caminho",
				  NoEixo(a.Pos - betaEstavaEm, d) < 0,
				  $"parou {NoEixo(a.Pos - betaEstavaEm, d):0.0} px alem de Beta");
		AfirmarDc("...e o voo ACABOU no encontro (o `duration=0` do `Movement Effects.dm:81`): nao "
				+ "ricocheteia e nao continua",
				  a.TiquesDeVoo <= 0);

		// ---------------- KNOCKBACK (o funil do soco) ----------------
		Montar();
		Vec2 betaAqui = b.Pos;
		Arremessar(a, d, Empurrao.ResistenciaPadrao, 8);
		AfirmarDc("PRECONDICAO: o knockback do soco armou o voo (mesmo `Arremessar` do golpe)",
				  a.TiquesDeVoo > 0);
		Tiques(40);
		AfirmarDc("POR KNOCKBACK: o corpo empurrado tambem NAO atravessa",
				  NoEixo(a.Pos - betaAqui, d) < 0,
				  $"parou {NoEixo(a.Pos - betaAqui, d):0.0} px alem de Beta");

		// ---------------- E O DESLIZE: ESBARRAR NAO E TRAVAR ----------------
		// O que separa "esbarrei" de "achei que travei" e o deslize de quina, e ele vem de graca por
		// estar no MESMO funil (`MoveRules.Advance`): em diagonal contra um corpo se CONTORNA.
		Montar();
		var diagonal = new Vec2(d.X + d.Y, d.Y + d.X);   // 45 graus a partir do rumo do palco
		if (diagonal.LengthSquared < 0.01f) diagonal = new Vec2(d.X, 1);
		Vec2 antesDoDeslize = a.Pos;
		Tiques(TiquesDoPasseio, () => AplicarComando(a, new Comando { Rumo = diagonal }, Protocol.TickSeconds));
		float noEixoDoPalco = Math.Abs(NoEixo(a.Pos - antesDoDeslize, d));
		float foraDoEixo = (a.Pos - antesDoDeslize - d * NoEixo(a.Pos - antesDoDeslize, d)).Length;
		AfirmarDc($"O DESLIZE: em diagonal contra um corpo ele CONTORNA em vez de travar "
				+ $"(andou {noEixoDoPalco:0} px no eixo do palco e desviou {foraDoEixo:0} px pro lado)",
				  foraDoEixo > 2 * T);
	}

	/// <summary>
	/// ESTE CORPO ANDA MESMO? O controle que impede toda a familia 5 de ficar verde por ausencia --
	/// um corpo que nao sai do lugar "nao atravessa ninguem" com a colisao inteira desligada.
	/// </summary>
	private bool MediuPasso(ServerPlayer a, Vec2 d, Vec2 origem)
	{
		float alturaB = _beta?.Altitude ?? 0;
		if (_beta != null) _beta.Altitude = Voo.AlturaQueAtravessa * 6;   // tira Beta do caminho pelo ANDAR
		a.Pos = origem;
		a.Altitude = 0;
		Vec2 antes = a.Pos;
		Tiques(30, () => AplicarComando(a, new Comando { Rumo = d }, Protocol.TickSeconds));
		bool andou = (a.Pos - antes).Length > ZoneCollision.TileSize;
		if (_beta != null) _beta.Altitude = alturaB;
		return andou;
	}

	// =====================================================================
	// 9) O CORPO **OCUPADO** NAO E EMPURRADO POR QUEM ANDA
	// =====================================================================
	/// <summary>
	/// ============================ O PEDIDO, LITERAL ============================
	/// *"atualmente ao estar lutando e andando, vc consgue empurrar o inimigo e vise e versa, faca com
	/// q n de pra empurar npcs ou outros players ao andar contra eles enquando eles batem ou fazem
	/// outra coisa."*
	///
	/// ============================ E O PALCO E **NO AR**, QUE E ONDE ELE ACONTECE ============================
	/// A familia 5 ja media "ninguem atravessa ninguem" -- e media **no chao**, onde a regra sempre
	/// valeu. O buraco estava exatamente onde a briga acontece: `ClasseDeCorpo.Bloqueia` abria pra
	/// quem VOA, e numa luta de DBZ os dois estao voando. Entao a regra inteira valia em todo lugar
	/// menos dentro do combate, que e o unico lugar em que o dono a pediu.
	///
	/// Por isso esta familia monta os dois NO AR, no MESMO andar, e a linha que abre e o
	/// contra-exemplo: com Beta LIVRE, Alfa **atravessa** -- e e assim mesmo, e o `mob/Cross` do DM.
	/// O que muda a resposta e uma coisa so: **o que Beta esta fazendo**.
	/// ====================================================================================================
	///
	/// ============================ E ELA MEDE AS DUAS METADES ============================
	/// *"o andador para"* e metade. A outra e *"o ocupado nao desliza"*, e ela precisa de numero
	/// proprio: uma colisao que EMPURRASSE Beta deixaria a primeira linha verde do mesmo jeito (Alfa
	/// teria "parado"... cinco tiles adiante, com Beta na frente dele o caminho todo). Aqui o `b.Pos`
	/// e do mundo, e a bancada le o que sobrou dele.
	///
	/// **E "Beta andou 0,0 px" AINDA NAO E PROVA**, porque ela fica verde de graca num mundo em que
	/// NADA move corpo nenhum -- e ai a familia inteira estaria medindo um jogo morto. Por isso cada
	/// estado tem, logo depois, a linha do ARREMESSO: o MESMO corpo, no MESMO estado, no MESMO ponto,
	/// levando um pesado pelo funil de producao (`TentarEmpurrar` -> `Empurrao.DoSoco` ->
	/// `Arremessar`) e VOANDO 576 px. O pedido do dono e sobre ANDAR; o knockback e outra coisa e
	/// continua empurrando -- e as duas frases so valem juntas.
	/// ==================================================================================
	///
	/// ============================ UMA LINHA POR ESTADO, E O NOME DO ESTADO NA LINHA ============================
	/// A primeira versao desta familia media DOIS estados (socar e guardar) -- os dois que o dono
	/// nomeou. Os outros oito do <see cref="Ocupacao"/> estavam na condicao que este repo ja catalogou
	/// por escrito: **dado extraido sem consumidor medido**. Eles existem no `enum`, sao calculados
	/// pelo `OcupacaoDe`, viajam no snapshot -- e ninguem nunca tinha medido se algum deles de fato
	/// para alguem.
	///
	/// Agora a tabela e a lista INTEIRA, e cada linha:
	///   1. LIGA o estado pelo caminho de producao (e a frase impressa cita a FUNCAO, nao o campo);
	///   2. cobra que a producao (`OcupacaoDe`) responda aquele estado -- e nos N tiques do passeio;
	///   3. mede que o andador PARA e nao atravessa, e que parou ENCOSTADO;
	///   4. mede que o corpo ocupado NAO saiu do lugar;
	///   5. mede que o MESMO corpo, no MESMO estado, VOA quando o knockback o pega;
	///   6. mede que dois corpos nascidos SOBREPOSTOS nao ficam presos;
	///   7. e cobra a DESMONTAGEM, pra o estado nao vazar pro proximo da tabela.
	///
	/// A cobertura da tabela e cobrada por uma linha propria: `tabela.Length` contra o ultimo valor do
	/// `enum`. Um estado novo no <see cref="Ocupacao"/> sem linha aqui reprova ali.
	/// ========================================================================================================
	/// </summary>
	private void OCorpoOcupadoNaoEEmpurrado(ServerPlayer a, ServerPlayer b, Vec2 d, Vec2 origem)
	{
		GD.Print("[dois] -- 9) o corpo OCUPADO nao e empurrado por quem anda --");

		const float T = ZoneCollision.TileSize;
		Vec2 lado = new(-d.Y, d.X);   // o eixo PERPENDICULAR ao palco -- ver o estado `Agarrando`

		// ============================ A TECLA C PRECISA DA SKILL, COMO O VOO ============================
		// Mesma concessao (e mesma razao) do <see cref="DarOVoo"/>: o que esta familia tem que
		// atravessar e o `Carregar` e a colisao, e nao a COMPRA do Ki Unlocked. `MeditateGivesKiRegen`
		// e literalmente o campo que aquela skill escreve (`Fighter.cs:337`) e o unico que o
		// `CargaDeKi.SabeReunir` le -- sem ele a tecla C nao faz nada e o estado `ReunindoKi` nao
		// existiria pra ser medido.
		// ============================================================================================
		b.Ficha.MeditateGivesKiRegen = 1;

		// ============================ O PALCO SOBE ATE O TETO, E QUEM PEDIU FOI O NOCAUTE ============================
		// `Voo.AlturaMaxima` sao 20 tiles e o andar 3 comeca em 13,3 -- ou seja, do teto sobram
		// 6,7 tiles de andar 3. Isso NAO e capricho de altura: um corpo nocauteado **perde o voo na
		// hora** (`TickDoVoo`, o `KO.dm:71` do DM) e desce a 16 tiles por segundo, entao a janela em
		// que existe "um corpo caido NO AR" e a propria queda. Do teto ela dura ~0,42 s (12,5 tiques);
		// dos 18 tiles em que o `LevantarVoo` padrao para, dura 8,7 -- menos do que Alfa leva pra
		// cruzar os 3 tiles do palco antigo. A primeira versao desta familia mediu exatamente isso e o
		// nocaute reprovou com "andares diferentes": Beta ja tinha saido do andar 3 no meio da medida.
		// ========================================================================================================
		void PorNoTeto(ServerPlayer c)
		{
			c.Ficha.Ki = c.Ficha.MaxKi;
			c.Ficha.stamina = c.Ficha.maxstamina;
			// SOBE PELA TECLA, e nao escrevendo `Altitude` -- ver `LevantarVoo`. 130 tiques a 6 tiles/s
			// dao 26 tiles; o teto e do `TickDoVoo` e o resto e recusado por ele.
			LevantarVoo(c, 130);
			c.Ficha.Ki = c.Ficha.MaxKi;
			c.Ficha.stamina = c.Ficha.maxstamina;
		}

		bool NoTeto(ServerPlayer c) => c.Voando && c.Altitude >= Voo.AlturaMaxima - 1f;

		PorNoTeto(a);
		PorNoTeto(b);

		AfirmarDc($"PRECONDICAO: os dois estao MESMO no ar e no MESMO andar "
				+ $"(Alfa {a.Altitude:0} px / andar {Voo.Andar(a.Altitude)}, "
				+ $"Beta {b.Altitude:0} px / andar {Voo.Andar(b.Altitude)})",
				  a.Voando && b.Voando && Voo.Andar(a.Altitude) == Voo.Andar(b.Altitude)
				  && Voo.Andar(a.Altitude) > 0);
		AfirmarDc("PRECONDICAO: e voando o modo de travessia dos dois e `Voando` "
				+ "(e o modo que atravessava corpo -- se nao for este, a familia mede outra coisa)",
				  ModoDeTravessiaDe(a) == ModoDeTravessia.Voando);
		AfirmarDc($"PRECONDICAO: os dois estao no TETO do ceu ({Voo.AlturaMaxima:0} px), que e o que da "
				+ "a janela de andar 3 mais longa possivel -- e o nocaute precisa dela",
				  NoTeto(a) && NoTeto(b), $"Alfa {a.Altitude:0} px, Beta {b.Altitude:0} px");

		// ============================ O TERCEIRO CORPO FICA FORA DO CAMINHO ============================
		// Gama existe desde a familia 5 e ele e um corpo na grade como qualquer outro: deixado na reta
		// do palco, ele seria um obstaculo que esta familia nao dirige -- e "Alfa parou" viraria "parou
		// em quem?". Ele so volta pra perto no estado `Agarrando`, que e o unico que precisa dele.
		// ==========================================================================================
		_gama ??= ForjarDois("Gama (o terceiro)", a.Zone, origem);
		ServerPlayer gama = _gama;
		DarOVoo(gama);
		void GuardarGama() { gama.Pos = origem - d * (10 * T); }

		// ============================ LIVRAR: TODO ESTADO QUE ESTA FAMILIA SABE LIGAR, DESLIGADO ============================
		// A limpeza e do TAMANHO da tabela, e nao do estado que acabou de ser medido, e isso ja custou
		// uma rodada: a pose de soco do passeio anterior sobrevivia a cadencia inteira e a medida da
		// GUARDA comecava com Beta ainda respondendo "no meio de um golpe". Com dez estados a chance de
		// um vazar pro seguinte e dez vezes maior, entao aqui nao se limpa "o ultimo" -- limpa-se tudo.
		//
		// **PELO CAMINHO DE PRODUCAO SEMPRE QUE ELE EXISTE**: `Guardar(false)`, `PararCarga`, `Soltar`,
		// `FecharCanal`, `Reviver`, `Transformar(subir:false)`. Tres sao escritos a mao, e cada um por
		// um motivo dito em voz alta: `train`/`med` porque o "caminho de producao" deles E uma
		// atribuicao (`case Protocol.C2S.Activity` faz `a.Ficha.train = ...` em duas linhas), o prazo da
		// cena pelo mesmo motivo, e o embate pelo motivo do <see cref="TirarDoEmbateSemDesfecho"/>.
		// ==============================================================================================================
		void Livrar(ServerPlayer c)
		{
			c.AtaqueAte = 0;
			c.Combate.Recarga = 0;
			c.Combate.Guardar(false);
			TirarDoEmbateSemDesfecho(c);
			if (_canais.TryGetValue(c.Id, out CanalDeKi? canal)) FecharCanal(c.Id, canal, null);
			if (c.Carregando) PararCarga(c);
			if (c.AgarrandoId != 0) Soltar(c, MotivoDaSoltura.Tecla);
			if (c.AgarradoPorId != 0) LimparPreso(c);
			c.Ficha.train = c.Ficha.med = false;
			c.CenaSegundos = 0;
			c.FuriaExtremaAte = 0;
			c.RaivaLendariaAte = 0;
			c.Ficha.Anger = 100;
			// A FORMA VOLTA PRA BASE PELO `Transformar`, um degrau por vez -- e com a cena zerada antes,
			// senao o proprio congelamento recusaria a descida. Seis voltas cobrem a escada inteira.
			for (int i = 0; i < 6 && !c.Forma.NaBase; i++) { c.CenaSegundos = 0; Transformar(c, subir: false); }
			c.TiquesDeVoo = 0;
			c.Combate.Stun = 0;
			if (c.Ficha.KO || c.Ficha.dead) c.Combate.Reviver();
			c.Ficha.Statify();
			c.Ficha.PowerLevel();
			c.Combate.SincronizarVida();
		}

		// ============================ O PALCO, REMONTADO ANTES DE CADA MEDIDA ============================
		// O combustivel e da MEDIDA e nao da regra: voar cobra Ki por tique e socar cobra folego, e um
		// corpo que cai (ou que para de socar por exaustao) no meio do passeio mediria a queda em vez da
		// colisao. Encher o tanque antes de cada medida deixa a unica variavel sendo a que a familia
		// manipula -- o que Beta esta fazendo.
		// ==============================================================================================
		Vec2 posDeBeta = origem + d * (3 * T);
		void Montar(float tiles)
		{
			Livrar(a); Livrar(b); Livrar(gama);
			GuardarGama();
			if (!NoTeto(a)) PorNoTeto(a);
			if (!NoTeto(b)) PorNoTeto(b);
			a.Ficha.Ki = a.Ficha.MaxKi; b.Ficha.Ki = b.Ficha.MaxKi;
			a.Ficha.stamina = a.Ficha.maxstamina; b.Ficha.stamina = b.Ficha.maxstamina;
			posDeBeta = origem + d * (tiles * T);
			a.Pos = origem;
			b.Pos = posDeBeta;
			a.Facing = MoveRules.FacingFrom(d, a.Facing);
			b.Facing = MoveRules.FacingFrom(d, b.Facing);
		}

		// ============================ O PASSEIO: ALFA VOA CONTRA BETA, BETA FAZ **X** ============================
		// **BETA OLHA PRA `d`, OU SEJA DE COSTAS PRA ALFA**, e isso e obrigatorio: olhando pra Alfa, o
		// soco dispararia o `Aproximar` e Beta ARRANCARIA dois tiles pra frente -- a bancada mediria o
		// dash e chamaria de empurrao. De costas, `AlvoParaArranque` nao acha ninguem no cone e o golpe
		// sai no vazio, que continua sendo "ele esta batendo".
		//
		// A CONTAGEM DE TIQUES NO ESTADO substituiu o "primeiro nao-livre" da primeira versao. Guardar
		// so o primeiro deixaria verde um passeio em que Beta ficou ocupado UM tique e livre os outros
		// trinta e nove -- e Alfa teria atravessado um corpo livre com a familia dizendo o nome do
		// estado na linha. Aqui a afirmacao e "ele esteve nesse estado nos N tiques", e o denominador
		// aparece impresso pra ninguem ter que acreditar.
		// ====================================================================================================
		(bool atravessou, float betaAndou, float alfaParouA, int noEstado, bool mesmoAndar, Ocupacao vista)
			Passeio(Ocupacao qual, Action? oQueBetaFaz, int tiques)
		{
			Vec2 betaAntes = b.Pos;
			Vec2 alvo = b.Pos;
			int noEstado = 0;
			bool mesmoAndar = true;
			var vista = Ocupacao.Livre;
			Tiques(tiques, () =>
			{
				oQueBetaFaz?.Invoke();
				// LIDO DA PRODUCAO, e no meio do passeio: e o MESMO `OcupacaoDe` que a grade consulta.
				Ocupacao agora = OcupacaoDe(b);
				if (agora == qual) noEstado++;
				if (vista == Ocupacao.Livre) vista = agora;
				if (!ClasseDeCorpo.MesmoAndar(Voo.Andar(a.Altitude), Voo.Andar(b.Altitude))) mesmoAndar = false;
				AplicarComando(a, new Comando { Rumo = d }, Protocol.TickSeconds);
			});
			return (NoEixo(a.Pos - alvo, d) > 0, (b.Pos - betaAntes).Length,
					NoEixo(b.Pos - a.Pos, d), noEstado, mesmoAndar, vista);
		}

		// ---------------- O CONTRA-EXEMPLO, E ELE VEM PRIMEIRO ----------------
		// Sem ele "Alfa parou" ficaria verde com um mundo em que voar deixou de funcionar. E ele e o
		// buraco de ontem, em numero: dois corpos no ar, um andando contra o outro, e ele passa.
		Montar(3f);
		var livre = Passeio(Ocupacao.Livre, () => AplicarComando(b, new Comando { Olhar = d }, Protocol.TickSeconds),
							TiquesDoPasseio);
		GD.Print($"[dois]   (Beta LIVRE no ar: Alfa parou {NoEixo(a.Pos - posDeBeta, d):0.0} px alem dele)");
		AfirmarDc("CONTRA-EXEMPLO: com Beta LIVRE, quem voa ATRAVESSA -- e o `mob/Cross` do DM, "
				+ "e era por aqui que o pedido do dono escapava",
				  livre.atravessou, $"ocupacao vista: '{CorpoOcupado.Nome(livre.vista)}'");
		AfirmarDc("...e no contra-exemplo Beta estava mesmo LIVRE os 40 tiques (senao ele mediria outra coisa)",
				  livre.noEstado == TiquesDoPasseio, $"{livre.noEstado}/{TiquesDoPasseio}");

		// =====================================================================
		// A TABELA: **UMA LINHA POR ESTADO**, e o nome do estado na linha
		// =====================================================================
		// A lista e a do `CorpoOcupado` inteira, na ordem dela. Medir so os dois que o dono nomeou
		// ("batem", "guardam") deixaria os outros oito na condicao que este projeto ja catalogou por
		// escrito -- dado extraido sem consumidor: eles estao no `enum`, sao calculados pelo
		// `OcupacaoDe`, viajam no snapshot... e ninguem nunca mediu se algum deles de fato para alguem.
		//
		// CADA LINHA LIGA O ESTADO **PELO CAMINHO DE PRODUCAO**, e o caminho esta escrito no texto que
		// vai pro console: se um dia alguem trocar `Nocautear` por `Ficha.KO = true`, a linha continua
		// verde e a frase passa a mentir -- por isso a frase cita a funcao, e nao o campo.
		(Ocupacao Qual, string Como, Action Ligar, Action? PorTique, float Tiles, int Tiques)[] tabela =
		[
			// ---- NOCAUTEADO: o unico da lista que TIRA o voo, e por isso o palco dele e curto ----
			// `Nocautear` (o verb de admin) chama `CombatState.Nocautear`, que e o MESMO que o
			// `MeleeResolver` chama quando um soco derruba alguem -- e nao `Ficha.KO = true`, que
			// deixaria o corpo sem prazo pra levantar.
			(Ocupacao.Nocauteado,
			 "levou um nocaute de verdade (`Nocautear` -> `CombatState.Nocautear`), e por isso esta CAINDO",
			 () => Nocautear(b), null, 1.5f, 12),

			// ---- NO EMBATE: o Zanzo Clash, com GAMA do outro lado ----
			// `Comecar` e o unico lugar do jogo que escreve `_emEmbate`. O par e Gama e nao Alfa de
			// proposito: um embate com Alfa dentro travaria justamente o corpo que tem que andar.
			(Ocupacao.NoEmbate,
			 "entrou num Zanzo Clash com Gama (`Comecar`, o unico que escreve `_emEmbate`)",
			 () => Comecar(b, gama, NowMs()), null, 3f, TiquesDoPasseio),

			// ---- EM CENA: a cinematica da transformacao, pela transformacao de verdade ----
			// O caminho e o da `--escudoteste`: o luto acende a raiva (o tronco Saiyajin cobra isso pra
			// sair da base) e o `Transformar` de producao passa pelo `AnunciarForma`, que e o UNICO
			// lugar do jogo que anota o prazo da cena. Escrever `CenaSegundos` a mao aqui testaria a
			// bancada, e nao o jogo.
			(Ocupacao.EmCena,
			 "se transformou de verdade (`Transformar` -> `AnunciarForma` -> `MarcarCena`)",
			 () =>
			 {
				 AmigoAbatido(b, "um amigo de bancada", Jandirus.Core.Forms.NivelDeRaiva.Extrema);
				 b.Ficha.Statify();
				 b.Ficha.Ki = b.Ficha.MaxKi;
				 Transformar(b, subir: true);
			 }, null, 3f, TiquesDoPasseio),

			// ---- CANALIZANDO KI: um raio na mao ----
			// `Canalizar` e a porta de todo raio do jogo (o `case C2S.Habilidade` desemboca nela).
			(Ocupacao.CanalizandoKi,
			 "esta com um raio na mao (`Canalizar`, a porta unica do beam)",
			 () => Canalizar(b, "Ki_Wave", 10 * b.Ficha.BaseDrain(), new ReceitaDeProjetil
			 {
				 Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 30,
				 CargaMinima = 1, Nome = "Onda de Ki",
			 }), null, 3f, TiquesDoPasseio),

			// ---- REUNINDO KI: a tecla C, pelo `AplicarComando` ----
			// O Ki cai pra metade antes: carregar com o tanque cheio mediria uma tecla que nao tem o
			// que fazer. Quem liga e o `Carregar` de producao, na transicao, exatamente como o teclado.
			(Ocupacao.ReunindoKi,
			 "esta segurando a tecla C (`Comando.Carregar` -> `Carregar`)",
			 () => { b.Ficha.Ki = b.Ficha.MaxKi * 0.5; },
			 () => AplicarComando(b, new Comando { Carregar = true, Olhar = d }, Protocol.TickSeconds),
			 3f, TiquesDoPasseio),

			// ---- ATACANDO: **o caso que o dono nomeou** ----
			(Ocupacao.Atacando,
			 "esta socando (`Comando.Leve` -> `Atacar`) -- *\"enquando eles batem\"*",
			 () => { },
			 () => AplicarComando(b, new Comando { Leve = true, Olhar = d }, Protocol.TickSeconds),
			 3f, TiquesDoPasseio),

			// ---- GUARDANDO: a decisao escrita desta rodada ----
			(Ocupacao.Guardando,
			 "esta de guarda erguida (`Comando.Guardar` -> `CombatState.Guardar`, o ALT)",
			 () => { },
			 () => AplicarComando(b, new Comando { Guardar = true, Olhar = d }, Protocol.TickSeconds),
			 3f, TiquesDoPasseio),

			// ---- AGARRANDO: Beta segura GAMA, e Gama fica DE LADO ----
			// Gama entra pelo eixo PERPENDICULAR ao palco, e nao na frente nem atras: na frente ele
			// pararia o arremesso de Beta a 0,75 tile (e a metade do "o arremesso continua empurrando"
			// mediria um corpo que bateu em Gama), e atras ele seria o corpo em que Alfa esbarraria
			// primeiro. De lado, os 24 px de afastamento sao maiores que a caixa dos pes nos DOIS eixos
			// (16 x 10 px), entao ele nao toca ninguem -- e continua ao alcance do braco de Beta.
			(Ocupacao.Agarrando,
			 "esta segurando Gama (`AlternarAgarrao` -> `Prender`)",
			 () =>
			 {
				 if (!NoTeto(gama)) PorNoTeto(gama);
				 gama.Pos = b.Pos + lado * (0.75f * T);
				 gama.Altitude = b.Altitude;
				 b.Facing = MoveRules.FacingFrom(lado, b.Facing);
				 AlternarAgarrao(b);
			 }, null, 3f, TiquesDoPasseio),

			// ---- TREINANDO e MEDITANDO: as duas atividades ----
			// O "caminho de producao" delas E uma atribuicao: o `case Protocol.C2S.Activity` do
			// `Handle` faz `a.Ficha.train = q == Activity.Treinando` e a linha seguinte pro `med`. Nao
			// ha funcao pra chamar, e inventar uma pra a bancada seria criar producao pra testar.
			(Ocupacao.Treinando,
			 "esta treinando (`C2S.Activity` -> `Ficha.train`)",
			 () => { b.Ficha.train = true; }, null, 3f, TiquesDoPasseio),

			(Ocupacao.Meditando,
			 "esta meditando (`C2S.Activity` -> `Ficha.med`)",
			 () => { b.Ficha.med = true; }, null, 3f, TiquesDoPasseio),
		];

		AfirmarDc($"PRECONDICAO: a tabela cobre a lista INTEIRA do `CorpoOcupado` "
				+ $"({tabela.Length} estados, e o `enum` tem {(int)Ocupacao.Meditando}) -- um estado novo "
				+ "no `enum` sem linha aqui reprova nesta mesma checagem",
				  tabela.Length == (int)Ocupacao.Meditando
				  && tabela.Select(t => t.Qual).Distinct().Count() == tabela.Length);

		foreach ((Ocupacao qual, string como, Action ligar, Action? porTique, float tiles, int tiques) in tabela)
		{
			string nome = CorpoOcupado.Nome(qual);

			// ---------------- 1. O ANDADOR PARA, E O OCUPADO NAO SAI DO LUGAR ----------------
			// **O `Ligar` E O `PorTique` SAO OS DOIS MEIOS DE ENTRAR NUM ESTADO, E OS DOIS RODAM
			// ANTES DA AFIRMACAO.** Tres estados desta tabela (socar, guardar, a tecla C) nao sao um
			// gesto e sim uma tecla MANTIDA: eles nao existem ate o `AplicarComando` do primeiro
			// tique. A primeira rodada desta tabela reprovou exatamente esses tres, os tres com "a
			// producao respondeu ''" -- a bancada perguntava antes de a tecla ter sido apertada.
			Montar(tiles);
			ligar();
			porTique?.Invoke();
			AfirmarDc($"[{qual}] PRECONDICAO: Beta {como}",
					  OcupacaoDe(b) == qual,
					  $"a producao respondeu '{CorpoOcupado.Nome(OcupacaoDe(b))}'");

			var p = Passeio(qual, porTique, tiques);
			GD.Print($"[dois]   ({qual}: Alfa parou a {p.alfaParouA:0.0} px de Beta, Beta andou "
				   + $"{p.betaAndou:0.0} px, {p.noEstado}/{tiques} tiques '{nome}')");

			AfirmarDc($"[{qual}] PRECONDICAO: Beta ficou '{nome}' nos {tiques} tiques do passeio",
					  p.noEstado == tiques, $"{p.noEstado}/{tiques}");
			AfirmarDc($"[{qual}] PRECONDICAO: os dois no MESMO andar o passeio inteiro "
					+ "(em andares diferentes o corpo nao barra ninguem, ocupado ou nao)",
					  p.mesmoAndar,
					  $"Alfa andar {Voo.Andar(a.Altitude)} / Beta andar {Voo.Andar(b.Altitude)}");
			AfirmarDc($"[{qual}] **O ANDADOR PARA**: Alfa voa contra um corpo '{nome}' e NAO passa por dentro",
					  !p.atravessou, $"parou {NoEixo(a.Pos - posDeBeta, d):0.0} px ALEM de Beta");
			AfirmarDc($"[{qual}] ...e parou ENCOSTADO, e nao a meio mapa (a caixa dos pes tem "
					+ $"{2 * MoveRules.BodyHalfW:0} px)",
					  Math.Abs(p.alfaParouA) < 2.5f * T, $"{p.alfaParouA:0.0} px");
			AfirmarDc($"[{qual}] **A OUTRA METADE**: o corpo '{nome}' NAO saiu do lugar",
					  p.betaAndou < 1f, $"{p.betaAndou:0.00} px");

			// ---------------- 2. E O ARREMESSO CONTINUA EMPURRANDO O MESMO CORPO ----------------
			// *"O arremesso (knockback) e outra coisa e continua empurrando: o pedido e sobre ANDAR."*
			//
			// **E ESTA E A METADE QUE IMPEDE A DE CIMA DE SER VERDADE DE GRACA.** "Beta andou 0,0 px"
			// ficaria verde num mundo em que NADA move corpo nenhum, e a familia inteira estaria
			// medindo um jogo morto. Aqui o MESMO corpo, no MESMO estado, no MESMO ponto, e empurrado
			// pelo funil do soco (`TentarEmpurrar` -> `Empurrao.DoSoco` -> `Arremessar`) e VOA.
			Montar(tiles);
			ligar();
			porTique?.Invoke();
			Vec2 antesDoBaque = b.Pos;
			a.Knockback = true;
			// A OCUPACAO E CONFERIDA **NO INSTANTE DO GOLPE**, e nao antes de montar: sem esta linha o
			// "o arremesso continua empurrando" poderia estar medindo um corpo que ja tinha saido do
			// estado -- e ai ele seria uma frase sobre corpo LIVRE, que ninguem duvidava.
			Ocupacao noBaque = OcupacaoDe(b);
			TentarEmpurrar(a, b, 200, Protocol.Golpe.Pesado, garantido: true);
			bool armou = b.TiquesDeVoo > 0;
			Tiques(40);
			float voou = (b.Pos - antesDoBaque).Length;
			AfirmarDc($"[{qual}] PRECONDICAO: o soco caiu num corpo que ESTAVA '{nome}' naquele instante",
					  noBaque == qual, $"a producao respondeu '{CorpoOcupado.Nome(noBaque)}'");
			AfirmarDc($"[{qual}] PRECONDICAO: o soco pesado de Alfa armou o arremesso de um corpo '{nome}'",
					  armou, $"TiquesDeVoo={b.TiquesDeVoo}");
			AfirmarDc($"[{qual}] **O ARREMESSO CONTINUA EMPURRANDO**: o mesmo corpo '{nome}' que o ombro "
					+ $"nao move VOA {voou:0} px pelo knockback",
					  voou > 2 * T, $"andou {voou:0.0} px");

			// ---------------- 3. E SOBREPOSTOS NINGUEM FICA PRESO ----------------
			// A regra nova e do tipo que reabre travamento: se ela desligasse o escape do
			// `jaSobrepondo`, bastaria alguem fazer QUALQUER uma destas dez coisas em cima de voce pra
			// voce nao andar mais -- e sobrepor acontece o tempo todo (solta-se do colo na posicao
			// EXATA de quem carrega, cai-se nocauteado em cima de quem estava colado, o arremesso para
			// dentro do alcance).
			Montar(tiles);
			ligar();
			porTique?.Invoke();
			Ocupacao noAperto = OcupacaoDe(b);
			a.Pos = b.Pos;   // o pior caso: o MESMO ponto
			Vec2 juntos = b.Pos;
			Tiques(TiquesDoPasseio, () =>
			{
				porTique?.Invoke();
				AplicarComando(a, new Comando { Rumo = d * -1 }, Protocol.TickSeconds);
			});
			AfirmarDc($"[{qual}] PRECONDICAO: o corpo em cima do qual Alfa nasceu ESTAVA '{nome}'",
					  noAperto == qual, $"a producao respondeu '{CorpoOcupado.Nome(noAperto)}'");
			AfirmarDc($"[{qual}] NAO PRENDE: nascido SOBREPOSTO a um corpo '{nome}', Alfa sai andando "
					+ $"({(a.Pos - juntos).Length:0} px)",
					  (a.Pos - juntos).Length > 2 * T, $"{(a.Pos - juntos).Length:0.0} px");

			// ---------------- 4. E A DESMONTAGEM E COBRADA ----------------
			// Sem esta linha um estado que nao soubesse se desligar contaminaria o proximo, e a familia
			// mediria a guarda achando que mede o treino -- que foi exatamente o defeito da primeira
			// rodada com dois estados, e agora sao dez.
			Livrar(b);
			AfirmarDc($"[{qual}] (desmontagem) Beta voltou a ser LIVRE -- o estado nao vaza pro proximo",
					  OcupacaoDe(b) == Ocupacao.Livre,
					  $"ficou '{CorpoOcupado.Nome(OcupacaoDe(b))}'");
		}

		// ---------------- O DEFEITO INJETADO ----------------
		// A MESMA grade cega da familia 5 -- a fonte errada / a ordem errada. Com ela a consulta
		// responde "nao ha ninguem aqui", e a ocupacao de Beta deixa de chegar a quem anda.
		Montar(3f);
		Mutacao(AfirmarDc,
				"o MESMO passeio com Beta socando continua barrado",
				"a grade de corpos esvaziada depois de montada -- a ocupacao nao chega a quem anda",
				() =>
				{
					Montar(3f);
					return !Passeio(Ocupacao.Atacando,
									() => AplicarComando(b, new Comando { Leve = true, Olhar = d }, Protocol.TickSeconds),
									TiquesDoPasseio).atravessou;
				},
				() => _dcGradeCega = true,
				() => _dcGradeCega = false);

		// ---------------- OCUPADO NAO E POSTE ----------------
		// Sem esta linha, "ocupado barra" ficaria verde com um mundo em que socar ergue uma coluna de
		// tres andares -- e o jogador chamaria isso de travamento sem nunca ver uma linha vermelha.
		Montar(3f);
		Pousar(a);
		a.Pos = origem;
		AfirmarDc($"PRECONDICAO do contra-exemplo: Alfa desceu pro chao (andar {Voo.Andar(a.Altitude)}) "
				+ $"e Beta continua no {Voo.Andar(b.Altitude)}",
				  Voo.Andar(a.Altitude) != Voo.Andar(b.Altitude));
		Tiques(TiquesDoPasseio, () =>
		{
			AplicarComando(b, new Comando { Leve = true, Olhar = d }, Protocol.TickSeconds);
			AplicarComando(a, new Comando { Rumo = d }, Protocol.TickSeconds);
		});
		AfirmarDc("CONTRA-EXEMPLO: em ANDAR DIFERENTE, o corpo ocupado continua sendo atravessado "
				+ "-- ocupado nao vira poste invisivel",
				  NoEixo(a.Pos - posDeBeta, d) > 0,
				  $"parou {NoEixo(a.Pos - posDeBeta, d):0.0} px");

		// ---- devolve o palco pra as familias seguintes ----
		// A POSE DE SOCO TAMBEM E PALCO: o ultimo passeio termina com Beta no meio de um golpe e a
		// cadencia dele passa da ultima linha desta familia -- a 6 comecaria medindo um corpo ocupado
		// sem saber. Mesmo motivo do `Montar`.
		Livrar(a); Livrar(b); Livrar(gama);
		Pousar(b);
		Pousar(gama);
		GuardarGama();
		a.Pos = origem;
		b.Pos = origem + d * (3 * T);
		a.Ficha.Ki = a.Ficha.MaxKi; b.Ficha.Ki = b.Ficha.MaxKi;
		a.Ficha.stamina = a.Ficha.maxstamina; b.Ficha.stamina = b.Ficha.maxstamina;
		AfirmarDc("no fim da familia, os dois estao no chao, livres e podem andar",
				  !a.Voando && !b.Voando && OcupacaoDe(b) == Ocupacao.Livre
				  && PodeMexerOCorpo(a) && PodeMexerOCorpo(b),
				  $"Alfa voando={a.Voando} pode={PodeMexerOCorpo(a)} KO={a.Ficha.KO} morto={a.Ficha.dead} "
				+ $"| Beta voando={b.Voando} pode={PodeMexerOCorpo(b)} KO={b.Ficha.KO} morto={b.Ficha.dead} "
				+ $"ocupacao='{CorpoOcupado.Nome(OcupacaoDe(b))}'");
	}

	/// <summary>
	/// TIRA UM CORPO DO ZANZO CLASH **SEM ENCENAR O DESFECHO** -- e a unica concessao de bancada da
	/// familia 9, e ela esta escrita aqui pra nao parecer descuido.
	///
	/// ============================ POR QUE NAO SOLTAR PELO CAMINHO NORMAL ============================
	/// O caminho normal e o `SoltarDoEmbate`, e ele chama o `Terminar` -- que nao "solta": ele **fecha a
	/// cena**. Ele teleporta o vencedor pras costas do perdedor (`Recolocar`) e dispara o
	/// `GolpeDeSaida`, que e um pesado com ARREMESSO GARANTIDO e que ainda `RacharChao` no lugar. Ou
	/// seja: desmontar o estado pelo caminho de producao jogaria um dos dois corpos pra longe, poderia
	/// nocautea-lo e deixaria o CENARIO estragado -- e as familias seguintes mediriam um destroco que a
	/// propria desmontagem produziu.
	///
	/// Entao a divisao e esta, e ela e a de sempre nesta bancada: **o LIGAR e de producao** (o `Comecar`
	/// e o unico lugar do jogo que escreve `_emEmbate`, e e ele que a tabela chama) e so o DESLIGAR e
	/// daqui. O que este metodo faz e exatamente o pedaco de ESTADO do `Terminar` -- as duas listas, o
	/// atordoamento e a invisibilidade --, sem uma linha de desfecho.
	/// ============================================================================================
	/// </summary>
	private void TirarDoEmbateSemDesfecho(ServerPlayer x)
	{
		if (!_emEmbate.TryGetValue(x.Id, out Embate? e)) return;
		_emEmbate.Remove(e.A.Id);
		_emEmbate.Remove(e.B.Id);
		_embates.Remove(e);
		foreach (ServerPlayer p in new[] { e.A, e.B })
		{
			p.Combate.Stun = 0;
			// SO DESFAZ O QUE O EMBATE FEZ, como o `Terminar`: quem chegou ja invisivel continua assim.
			if (p == e.A ? e.SumidoAntesA : e.SumidoAntesB) continue;
			_invisiveis.Remove(p.Id);
			p.Ficha.isconcealed = false;
			MandarEfeito(p, "invisivel", 0);
		}
	}

	// =====================================================================
	// 6) O BAQUE DOI NOS DOIS
	// =====================================================================
	/// <summary>
	/// ============================ `Movement Effects.dm:77-81`, LITERAL ============================
	/// <code>
	/// for(var/mob/M in get_step(target,dir))
	///     if(M &amp;&amp; M != target)
	///         M.SpreadDamage(duration,0)        // quem levou a topada
	///         target.SpreadDamage(duration,0)   // quem vinha voando
	///         duration = 0                      // ...e o voo ACABA
	/// </code>
	/// *"a pessoa JOGADA sofre dano E a pessoa q COLIDIU com o corpo voando TB toma dano"* -- o pedido,
	/// e ele e o original sem uma linha inventada. A dose e `duration` = **o que faltava voar**, entao
	/// bater no comeco do arremesso doi mais que bater no fim.
	///
	/// **UMA LINHA POR LADO**, medida antes e depois, e o contra-exemplo junto: sem encontro (andares
	/// diferentes), NENHUM dos dois se machuca -- senao "os dois tomaram dano" ficaria verde com um
	/// arremesso que fere quem passa voando a dez tiles de distancia.
	/// ==========================================================================================
	/// </summary>
	private void OBaqueDoiNosDois(ServerPlayer a, ServerPlayer b, Vec2 d, Vec2 origem)
	{
		GD.Print("[dois] -- 6) bater doi nos DOIS --");

		const float T = ZoneCollision.TileSize;

		void Curar(ServerPlayer c)
		{
			foreach (BodyPart p in c.Combate.Corpo.Partes) p.Vida = p.VidaMax;
			c.Combate.SincronizarVida();
			c.Ficha.KO = false;
			c.Ficha.dead = false;
		}

		void Montar()
		{
			Curar(a); Curar(b);
			a.Altitude = b.Altitude = 0;
			a.TiquesDeVoo = b.TiquesDeVoo = 0;
			a.Pos = origem;
			b.Pos = origem + d * (3 * T);
		}

		// ---- O CONTRA-EXEMPLO PRIMEIRO: SEM ENCONTRO, NINGUEM SE MACHUCA ----
		// Beta VOA (ver `LevantarVoo`) -- e nao "esta com um numero no campo de altura". O `Curar` vem depois
		// da decolagem porque decolar custa Ki e o voo drena, e o que se mede aqui e VIDA e nao Ki.
		Montar();
		LevantarVoo(b);
		AfirmarDc($"PRECONDICAO do contra-exemplo: Beta paira mesmo (andar {Voo.Andar(b.Altitude)})",
				  b.Voando && Voo.Andar(b.Altitude) > 0);
		Curar(a); Curar(b);
		double vidaA0 = a.Ficha.HP, vidaB0 = b.Ficha.HP;
		Arremessar(a, d, Empurrao.ResistenciaPadrao, 10);
		Tiques(40);
		AfirmarDc($"CONTRA-EXEMPLO: passando por baixo de Beta (andar {Voo.Andar(b.Altitude)}), o jogado "
				+ $"NAO se machuca (vida {vidaA0:0.0} -> {a.Ficha.HP:0.0})",
				  a.Ficha.HP >= vidaA0 - 0.001);
		AfirmarDc($"...e o de cima tambem nao (vida {vidaB0:0.0} -> {b.Ficha.HP:0.0})",
				  b.Ficha.HP >= vidaB0 - 0.001);
		Pousar(b);

		// ---- O SENTIDO POSITIVO: UMA LINHA POR LADO ----
		// ============================ O DEFEITO INJETADO #5 ============================
		// A MESMA grade cega da familia 5, e nao por economia: se ela cegar, o corpo atravessa E
		// ninguem se machuca -- as duas metades do mesmo pedido caem juntas. Medir as duas com o
		// mesmo defeito e o que prova que elas sao a mesma regra e nao duas.
		// ==============================================================================
		double vidaDeA = 0, vidaDeB = 0, antesDeA = 0, antesDeB = 0;

		bool OsDoisSeMachucaram()
		{
			Montar();
			antesDeA = a.Ficha.HP;
			antesDeB = b.Ficha.HP;
			Arremessar(a, d, Empurrao.ResistenciaPadrao, 10);
			Tiques(40);
			vidaDeA = a.Ficha.HP;
			vidaDeB = b.Ficha.HP;
			return vidaDeA < antesDeA && vidaDeB < antesDeB;
		}

		Mutacao(AfirmarDc,
				"O BAQUE MACHUCA OS DOIS (o jogado E quem estava no caminho)",
				"a grade de corpos cega -- sem encontro nao ha `SpreadDamage` nenhum",
				OsDoisSeMachucaram,
				() => _dcGradeCega = true,
				() => _dcGradeCega = false);

		AfirmarDc($"...QUEM FOI JOGADO: vida {antesDeA:0.0} -> {vidaDeA:0.0} ({antesDeA - vidaDeA:0.0} perdidos)",
				  vidaDeA < antesDeA);
		AfirmarDc($"...QUEM ESTAVA NO CAMINHO: vida {antesDeB:0.0} -> {vidaDeB:0.0} "
				+ $"({antesDeB - vidaDeB:0.0} perdidos)",
				  vidaDeB < antesDeB);

		// ---- E A DOSE E O QUE FALTAVA VOAR (bater cedo doi mais que bater tarde) ----
		Montar();
		b.Pos = origem + d * (10 * T);   // longe: o corpo bate no FIM do voo, com pouca inercia
		double antesLonge = b.Ficha.HP;
		Arremessar(a, d, Empurrao.ResistenciaPadrao, 10);
		Tiques(60);
		double doseLonge = antesLonge - b.Ficha.HP;

		Montar();
		double antesPerto = b.Ficha.HP;
		Arremessar(a, d, Empurrao.ResistenciaPadrao, 10);
		Tiques(60);
		double dosePerto = antesPerto - b.Ficha.HP;

		AfirmarDc($"...e a DOSE e o que FALTAVA voar (`duration`): bater a 3 tiles doi {dosePerto:0.00} e "
				+ $"bater a 10 tiles doi {doseLonge:0.00} -- o corpo perde inercia no caminho",
				  dosePerto > doseLonge && doseLonge >= 0,
				  $"perto {dosePerto:0.000} x longe {doseLonge:0.000}");

		Curar(a); Curar(b);
	}

	// =====================================================================
	// 7 e 8) O CADAVER ENTRE DOIS CORPOS, E A VIAGEM
	// =====================================================================
	/// <summary>
	/// ============================ O CADAVER, MEDIDO COM UM SEGUNDO CORPO VIVO AO LADO ============================
	/// A `--cadaverteste` mede o cadaver com o HOST fazendo tudo -- e o host e o proprio morto,
	/// ressuscitado na mao pra poder agarrar o proprio corpo. Aqui e uma pessoa OUTRA que chega,
	/// encontra o corpo, bate nele, carrega pelo ar e enterra: que e a cena que o pedido descreve.
	///
	/// E ela mede primeiro a coisa mais provavel de ter regredido: **a viagem pro Outro Mundo**. As
	/// duas coisas nao brigam -- elas sao o desenho do DM (`GenerateCorpse()` no passo 5 do `Death()`,
	/// `loc = locate(...)` no 11) --, e esta familia e a prova de que continuam nao brigando.
	/// ==========================================================================================================
	/// </summary>
	private void OCadaverEntreDoisCorpos(ServerPlayer pl, ServerPlayer a, Vec2 d)
	{
		GD.Print("[dois] -- 7/8) o cadaver, e a viagem que NAO regrediu --");

		ZoneKey ondeMorreu = pl.Zone;
		Vec2 ondeCaiu = pl.Pos;

		// ---- 8) A VIAGEM, COM O DEFEITO INJETADO ----
		// ============================ O DEFEITO INJETADO #6 ============================
		// **O CRITERIO MONTA A MORTE INTEIRA** (ver o mesmo argumento na familia 4): ele mata, vence o
		// prazo e pergunta. Por isso a injecao tem que atingir algo que o proprio criterio NAO reescreve
		// -- `MorteJaViajou`, que era a escolha obvia, e zerado pelo setup e a injecao evaporava.
		//
		// O que se injeta e o `Peer` nulo: **a triagem so leva pro Outro Mundo quem tem dono na tela**
		// (`VenceuOPrazoDaMorte` -> `EhJogador` -> `PassoDaMorte`), e sem ele o corpo cai no terceiro
		// grupo e o relogio dele para. E a regra real, escrita, e a que separa "o jogador que morreu" do
		// "corpo sem dono que morreu" -- e ela e o unico caminho pelo qual esta viagem pode sumir
		// caladamente do jogo.
		// ==============================================================================
		LiteNetLib.NetPeer? peerGuardado = pl.Peer;

		bool AViagemAconteceu()
		{
			pl.Combate.Reviver();
			pl.Ficha.dead = false;
			pl.MorteJaViajou = false;
			if (!pl.Zone.Equals(ondeMorreu)) MoveToZone(pl.Id, ondeMorreu, ondeCaiu);
			else pl.Pos = ondeCaiu;

			pl.Combate.Morrer(ignorarSeguro: true);
			pl.RelogioDaMorte = NowMs() - 1;
			VenceuOPrazoDaMorte(pl);
			return Alem.EhOAlem(pl.Zone);
		}

		Mutacao(AfirmarDc,
				"A VIAGEM PRO OUTRO MUNDO NAO REGREDIU: o host morre, o prazo vence e ele CHEGA no alem",
				"o corpo sem dono na tela -- a triagem so leva quem tem `Peer`",
				AViagemAconteceu,
				() => pl.Peer = null,
				() => pl.Peer = peerGuardado);

		AfirmarDc("...com auréola (o `Halo.dmi` de `Death.dm:106-108`, que so acende DEPOIS da viagem)",
				  Alem.TemAureola(pl.Ficha.dead, pl.MorteJaViajou));

		// ---- 7) O CORPO FICOU ----
		// ============================ TRES MORTES DEIXAM ATE TRES CORPOS -- E ISSO IMPORTA ============================
		// O `Mutacao` roda o criterio TRES vezes, e cada viagem que acontece deixa um cadaver **no mesmo
		// ponto**. Corpos empilhados nao sao um detalhe de limpeza: o `CorpoNaFrente` e o `CadaverPerto`
		// escolhem O MAIS PROXIMO, e com tres a distancia zero quem ganha e quem estiver primeiro na
		// lista -- que nao e necessariamente o que a bancada guardou. Na primeira rodada isso reprovou
		// cinco linhas seguidas da familia 7 com o corpo "certo" intacto ao lado.
		// ==========================================================================================================
		var corpos = new List<ServerPlayer>();
		foreach (ServerPlayer o in ZoneList(ondeMorreu.Hash))
			if (o.ECadaver && o.NomeDeQuemMorreu == pl.Name) corpos.Add(o);

		AfirmarDc($"E O CORPO FICOU no mundo dos vivos ({corpos.Count} deixado(s) pelas passadas do "
				+ "`Mutacao` -- a bancada fica com um e desfaz os outros)",
				  corpos.Count > 0);
		if (corpos.Count == 0) return;

		for (int i = 0; i < corpos.Count - 1; i++) DesfazerOCadaver(corpos[i]);
		ServerPlayer? cadaver = corpos[^1];
		_dcCadaveres.Add(cadaver);

		AfirmarDc("...sem auréola, com a do host acesa ao mesmo tempo (o cadaver e o corpo EXATO de "
				+ "quem caiu -- o DM o fotografa antes de o `overlayList += 'Halo.dmi'` existir)",
				  !Alem.TemAureola(cadaver.Ficha.dead, cadaver.MorteJaViajou)
				  && Alem.TemAureola(pl.Ficha.dead, pl.MorteJaViajou));
		AfirmarDc("...e ele NAO carrega numero nenhum do morto (a ficha dele e NOVA -- construcao, "
				+ "e nao corte)",
				  cadaver.Ficha.BP < pl.Ficha.BP && !ReferenceEquals(cadaver.Ficha, pl.Ficha));

		// ---- O SEGUNDO CORPO CHEGA ----
		a.Altitude = 0;
		a.TiquesDeVoo = 0;
		a.Ficha.KO = false;
		a.Combate.Reviver();
		if (!a.Zone.Equals(cadaver.Zone)) MoveToZone(a.Id, cadaver.Zone, cadaver.Pos);
		Encostar(a, cadaver, d);

		// ============================ O DEFEITO INJETADO #7 ============================
		// O criterio e "o cadaver FICA -- nao ha prazo". O defeito injetado e o `verb/Destroy()` do DM
		// pelo lado do golpe: bater no corpo ate o fim o desfaz. Ele prova as DUAS metades de uma vez:
		// que o corpo fica (nenhum relogio o leva) e que ele **sofre dano** (o pedido em letra).
		// ==============================================================================
		ServerPlayer c = cadaver;
		List<ServerPlayer> zonaDoCorpo = ZoneList(c.Zone.Hash);
		double[] vidaGuardada = [.. c.Combate.Corpo.Partes.Select(p => p.Vida)];

		Mutacao(AfirmarDc,
				"O CADAVER FICA: cem tiques depois ele continua no chao (nao ha prazo, so lotacao)",
				"o corpo DESTROCADO pelo golpe -- o `verb/Destroy()` do DM, `Corpse.dm:22-26`",
				() => { Tiques(100); return zonaDoCorpo.Contains(c); },
				() => { Espalhar(c, 10_000); Tiques(TicksPorFicha + 1); },
				() =>
				{
					if (!zonaDoCorpo.Contains(c)) zonaDoCorpo.Add(c);
					int i = 0;
					foreach (BodyPart p in c.Combate.Corpo.Partes) p.Vida = vidaGuardada[i++];
					c.Combate.SincronizarVida();
				});

		AfirmarDc("...e ele SOFRE DANO de verdade (o pedido em letra: 'pode sofrer dano')",
				  c.Ficha.HP > 0);

		// ---- AGARRADO E LEVADO VOANDO POR OUTRA PESSOA ----
		Encostar(a, c, d);
		AfirmarDc("o `CorpoNaFrente` de quem chegou acha o CADAVER (ele nao pergunta `Ficha.dead`)",
				  CorpoNaFrente(a) == c);
		AfirmarDc("...e o `AlvoNaFrente` do SOCO nao acha (as duas regras sao diferentes de proposito)",
				  AlvoNaFrente(a) != c);

		AlternarAgarrao(a);
		AlternarAgarrao(a);
		Tiques(3);
		AfirmarDc("O CADAVER E AGARRADO E CARREGADO, e o aperto SOBREVIVE ao tique (ele vive so na "
				+ "`ZoneList` -- era aqui que o `_players.TryGetValue` soltava, calado)",
				  a.AgarrandoId == c.Id && c.AgarradoPorId == a.Id
				  && a.ModoDoAgarrao == ModoDeAgarrao.Carregando);

		LevantarVoo(a);
		AfirmarDc($"...E SAIU VOANDO COM ELE: os dois na mesma altitude (quem carrega {a.Altitude:0.0} px, "
				+ $"o corpo {c.Altitude:0.0} px)",
				  a.Altitude > ZoneCollision.TileSize && Math.Abs(a.Altitude - c.Altitude) < 0.01f);
		AfirmarDc("...e o corpo morto conta como VOANDO pra quem olha (`EntityState.Voando`, derivado "
				+ "da ALTURA)",
				  EstadoDe(c, NowMs()).Voando);

		// ---- LEVADO PRA OUTRO LUGAR E ARREMESSADO ----
		Vec2 antesDoVoo = c.Pos;
		Soltar(a, MotivoDaSoltura.Tecla);
		if (a.Voando) AlternarVoo(a);
		a.Altitude = 0;
		Tiques(200);
		AfirmarDc("...largado no ar, o corpo desce ate o chao sozinho", c.Altitude <= 0f, $"{c.Altitude:0.0}");

		Vec2 antesDoArremesso = c.Pos;
		Arremessar(c, d, Empurrao.ResistenciaPadrao, 6);
		Tiques(10);
		AfirmarDc("...e ARREMESSADO ele voa de verdade (a posicao, e nao o campo)",
				  (c.Pos - antesDoArremesso).Length > ZoneCollision.TileSize,
				  $"andou {(c.Pos - antesDoArremesso).Length:0.0} px");
		Tiques(40);
		AfirmarDc($"...e o corpo foi levado do lugar em que caiu ({(c.Pos - ondeCaiu).Length:0} px) -- "
				+ "*'vc pode AGARRAR o corpo e levar pra outro lugar'*",
				  (c.Pos - ondeCaiu).Length > ZoneCollision.TileSize);

		// ---- ENTERRAR PELA TECLA E ----
		Encostar(a, c, d);
		a.Pos = c.Pos + new Vec2(16, 0);

		// ============================ O DEFEITO INJETADO #8 ============================
		// O criterio e "enterrar ergue a lapide E o corpo some". O defeito injetado e uma OBRA no ponto
		// exato: a cova nao abre (*"ja tem coisa demais neste ponto"*), e o `Enterrar` sai ANTES de
		// desfazer o corpo. E a ordem que o `Enterrar` documenta -- a lapide primeiro, o corpo depois --,
		// porque enterrar sem lapide seria apagar o cadaver e nao dar nada em troca.
		// ==============================================================================
		ZoneKey zonaDaCova = c.Zone;
		Vec2 ondeJaz = c.Pos;
		Obra? entulho = null;

		Mutacao(AfirmarDc,
				"ENTERRAR PELO VERBO DA TECLA E: nasce a lapide E o corpo some do mundo",
				"uma construcao ocupando o ponto exato -- a cova nao abre",
				() =>
				{
					int obras = _noChao.Count;
					ComandoDeInteracao(a, "enterrar", "");
					bool ergueu = _noChao.Count > obras
						&& _noChao[^1].Tipo.StartsWith("Grave_", StringComparison.Ordinal);
					bool sumiu = !ZoneList(zonaDaCova.Hash).Contains(c);
					if (ergueu && sumiu)
					{
						// DESFAZ pra a proxima passada do `Mutacao` medir o mesmo mundo -- senao a
						// terceira chamada mediria uma zona sem corpo nenhum e reprovaria por ausencia.
						_noChao.RemoveAt(_noChao.Count - 1);
						ZoneList(zonaDaCova.Hash).Add(c);
						c.Pos = ondeJaz;
					}
					return ergueu && sumiu;
				},
				() =>
				{
					entulho = new Obra
					{
						Id = _proximaObraId++, Tipo = "Grave_1",
						X = ondeJaz.X, Y = ondeJaz.Y,
						DonoConta = "bancada_dois", DonoNome = "entulho", ErguidaEm = NowMs(),
						ArmaduraMax = Armadura.Padrao, Armadura = Armadura.Padrao,
					};
					entulho.PorZona(zonaDaCova);
					_noChao.Add(entulho);
				},
				() => { if (entulho != null) _noChao.Remove(entulho); entulho = null; });

		AfirmarDc("...e com a cova bloqueada o CORPO CONTINUOU NO MUNDO (a lapide vem antes do corpo "
				+ "sumir: se a cova nao abre, enterrar nao apaga nada)",
				  ZoneList(zonaDaCova.Hash).Contains(c));

		// ---- E ENTAO, DE VERDADE ----
		Encostar(a, c, d);
		a.Pos = c.Pos + new Vec2(16, 0);
		int obrasAgora = _noChao.Count;
		ComandoDeInteracao(a, "enterrar", "");

		Obra? lapide = _noChao.LastOrDefault(o => o.Tipo.StartsWith("Grave_", StringComparison.Ordinal));
		AfirmarDc("ENTERROU: nasceu a lapide", _noChao.Count > obrasAgora && lapide != null);
		AfirmarDc("...com o epitafio de quem jaz ali (`A.desc`, `Corpse.dm:53`)",
				  lapide != null && lapide.Epitafio == Cadaver.EpitafioPadrao(c.NomeDeQuemMorreu),
				  lapide?.Epitafio ?? "");
		AfirmarDc("...E O CORPO SUMIU", !ZoneList(zonaDaCova.Hash).Contains(c));
		_dcCadaveres.Remove(c);

		// ---- LONGE DEMAIS NAO ENTERRA (o outro sentido do mesmo verbo) ----
		ServerPlayer? outro = null;
		pl.Combate.Reviver();
		pl.Ficha.dead = false;
		pl.MorteJaViajou = false;
		MoveToZone(pl.Id, ondeMorreu, ondeCaiu);
		pl.Combate.Morrer(ignorarSeguro: true);
		pl.RelogioDaMorte = NowMs() - 1;
		VenceuOPrazoDaMorte(pl);
		foreach (ServerPlayer o in ZoneList(ondeMorreu.Hash)) if (o.ECadaver) outro = o;

		if (outro != null)
		{
			_dcCadaveres.Add(outro);
			if (!a.Zone.Equals(outro.Zone)) MoveToZone(a.Id, outro.Zone, outro.Pos);
			a.Pos = outro.Pos + new Vec2(400, 0);
			int obras = _noChao.Count;
			ComandoDeInteracao(a, "enterrar", "");
			AfirmarDc("LONGE DEMAIS nao enterra nada (a recusa e do SERVIDOR, e nao do menu)",
					  _noChao.Count == obras && ZoneList(outro.Zone.Hash).Contains(outro));
		}
	}

	// =====================================================================
	// 10) O CADAVER E A FOTO DO MORTO -- angulo, membros, feridas
	// =====================================================================
	/// <summary>
	/// ============================ O RELATO DO DONO, MEDIDO ============================
	/// *"personagens que morreram, eles levantam depois de um tempo (continuam de olho fechado mas eles
	/// giram como se tivessem ficado de pe, o que nao deveria acontecer; deveriam ficar com os
	/// ferimentos e na mesma posicao de quando morreram)"*.
	///
	/// O "levanta e gira" era o instante da troca (os 2 s de `Alem.MsNoChao`): o cadaver nascia com o
	/// angulo padrao (`FacingDaQueda = South`) e um `Body.Novo()` limpo -- o corpo virava pro sul e os
	/// ferimentos sumiam. E depois disso ele girava de novo no fim de qualquer arremesso, porque o
	/// `DirecaoDeitado` voltava pra um `RumoDoGolpe` que o cadaver nunca teve.
	///
	/// A familia mede as quatro coisas do pedido, cada uma com o outro lado:
	///   (i)   o cadaver nasce virado pra onde o morto caiu, sem o membro que ele perdeu e com a MESMA
	///         mascara de feridas -- com o DEFEITO INJETADO (`_cadaverSemFotoDeTeste`: o `Body.Novo()`
	///         de antes) o mesmo criterio reprova;
	///   (ii)  soco no cadaver NAO o gira / (v) o VIVO que apanha continua girando;
	///   (iii) o cadaver arremessado deita pra onde deslizou e FICA (sem o estalo do pouso) -- e o
	///         nocauteado vivo jogado com um rumo de golpe velho tambem;
	///   (iv)  quem entra na zona recebe as feridas do cadaver.
	/// ====================================================================================
	/// </summary>
	private void OCadaverEAFotoDoMorto(ServerPlayer pl, ServerPlayer a, ServerPlayer b, Vec2 d)
	{
		GD.Print("[dois] -- 10) o cadaver e a FOTO do morto: angulo, membros e feridas --");

		const float T = ZoneCollision.TileSize;
		ZoneKey palco = a.Zone;
		Vec2 ondeMorre = a.Pos + d * (4 * T);
		MascaraDeFeridas doMorto = default;
		Facing deitadoAoMorrer = Facing.South;
		ServerPlayer? cadaver = null;

		// ---- (i) A FOTO, COM O DEFEITO INJETADO ----
		// ============================ O DEFEITO INJETADO #9 ============================
		// O criterio monta o morto inteiro (vivo, virado pro LESTE, sem o braco esquerdo, com o torso e
		// a perna marcados), mata pelo funil de producao, vence o prazo e deixa a TRIAGEM erguer o
		// cadaver -- e pergunta se ele e a foto. O que se injeta e o `Body.Novo()` de volta no cadaver
		// (`_cadaverSemFotoDeTeste`, lido no `DeixarOCadaver`): o corpo limpo que o dono viu.
		// ==============================================================================
		bool AFotoBate()
		{
			pl.Combate.Reviver();
			pl.Ficha.dead = false;
			pl.MorteJaViajou = false;
			if (!pl.Zone.Equals(palco)) MoveToZone(pl.Id, palco, ondeMorre);
			else pl.Pos = ondeMorre;

			// PRA ONDE ELE CAIU: virado pro leste, sem rumo de golpe (o olhar da queda e a resposta).
			pl.Facing = Facing.East;
			pl.CongelarDirecaoDeitada(Facing.East);

			// O QUE ELE ERA: braco esquerdo arrancado pelo `LopLimb` de producao, torso e perna feridos.
			Body corpo = pl.Combate.Corpo;
			pl.Combate.Arrancar(corpo.Achar("Braco esquerdo")!);
			corpo.Ferir(corpo.Achar("Torso")!, 60, letal: true);
			corpo.Ferir(corpo.Achar("Perna direita")!, 85, letal: true);
			pl.Combate.SincronizarVida();
			doMorto = Feridas.De(corpo);
			deitadoAoMorrer = pl.DirecaoDeitado;

			pl.Combate.Morrer(ignorarSeguro: true);
			pl.RelogioDaMorte = NowMs() - 1;
			VenceuOPrazoDaMorte(pl);

			// O CADAVER DESTA PASSADA: o ultimo com o nome do morto. Os das passadas anteriores do
			// `Mutacao` se desfazem aqui, pelo mesmo motivo escrito na familia 7 (corpos empilhados).
			ServerPlayer? c = null;
			foreach (ServerPlayer o in ZoneList(palco.Hash))
				if (o.ECadaver && o.NomeDeQuemMorreu == pl.Name) c = o;
			if (c == null) return false;
			if (cadaver != null && cadaver != c) { DesfazerOCadaver(cadaver); _dcCadaveres.Remove(cadaver); }
			cadaver = c;
			if (!_dcCadaveres.Contains(c)) _dcCadaveres.Add(c);

			return deitadoAoMorrer == Facing.East
				&& c.DirecaoDeitado == Facing.East
				&& c.Combate.Corpo.Achar("Braco esquerdo") is { Decepado: true }
				&& Feridas.De(c.Combate.Corpo) == doMorto
				&& c.EnvFeridas == doMorto;
		}

		Mutacao(AfirmarDc,
				"O CADAVER E A FOTO DO MORTO: nasce deitado pro LESTE (como ele caiu), sem o braco esquerdo "
			  + "(como ele estava) e com a MESMA mascara de feridas do instante -- `A.overlays += overlays`",
				"o cadaver nascendo com o `Body.Novo()` limpo -- o corpo que o dono viu",
				AFotoBate,
				() => _cadaverSemFotoDeTeste = true,
				() => _cadaverSemFotoDeTeste = false);

		if (cadaver == null) return;
		ServerPlayer c = cadaver;

		AfirmarDc("...e a mascara do morto tinha sangue e amputacao (senao a igualdade acima seria entre "
				+ "duas mascaras limpas)",
				  Feridas.Grave(doMorto) && doMorto.Perdeu(MascaraDeFeridas.Membro.BracoEsq), doMorto.ToString());
		// A CHEGADA CURA (`Death.dm:86-88,111`; o dono, 2026-09-04: "ao morrer voce acorda no outro mundo
		// 100% curado de tudo"): o morto chega INTEIRO e o cadaver fica SEM o braco -- se a foto fosse a
		// instancia, curar um curaria o outro. E a prova de que `DeixarOCadaver` COPIA o corpo.
		BodyPart? bracoDoMorto = pl.Combate.Corpo.Achar("Braco esquerdo");
		BodyPart? bracoDoCadaver = c.Combate.Corpo.Achar("Braco esquerdo");
		AfirmarDc("...enquanto o MORTO chegou ao Outro Mundo INTEIRO e o cadaver continua sem o braco: a foto e COPIA, nao a instancia",
				  Alem.EhOAlem(pl.Zone) && bracoDoMorto is { Decepado: false } && bracoDoCadaver is { Decepado: true }
				  && !ReferenceEquals(c.Combate.Corpo, pl.Combate.Corpo) && !ReferenceEquals(bracoDoMorto, bracoDoCadaver));
		AfirmarDc("...e o cadaver continua sem numero de ninguem (a ficha e nova; so o CORPO e foto)",
				  c.Ficha.BP < pl.Ficha.BP && !ReferenceEquals(c.Ficha, pl.Ficha));
		AfirmarDc("...e ele nasce SEM rumo de golpe (a direcao e a queda, e a queda e definitiva)",
				  c.RumoDoGolpe.LengthSquared < 1e-6f && c.FacingDaQueda == Facing.East);

		// ---- (iv) QUEM ENTRA NA ZONA RECEBE AS FERIDAS DO CADAVER ----
		a.Combate.Reviver();
		a.Ficha.dead = false;
		a.Altitude = 0;
		a.TiquesDeVoo = 0;
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		EscutaDeFeridas = [];
		MoveToZone(a.Id, alem, MesaDoEnma(alem));
		MoveToZone(a.Id, palco, c.Pos - d * (2 * T));
		var recebidas = new List<MascaraDeFeridas>();
		foreach ((int para, byte[] fio) in EscutaDeFeridas)
		{
			if (para != a.Id) continue;
			var r = new LiteNetLib.Utils.NetDataReader(fio);
			r.GetByte();
			if (r.GetInt() != c.Id) continue;
			recebidas.Add(r.GetFeridas());
		}
		EscutaDeFeridas = null;
		AfirmarDc("QUEM ENTRA NA ZONA recebe a mascara do CADAVER pelo `S2C.Feridas` (o `TrocarFeridas` "
				+ "varre a `ZoneList`, e o cadaver esta nela com o `EnvFeridas` da foto)",
				  recebidas.Count >= 1 && recebidas[^1] == doMorto,
				  recebidas.Count == 0 ? "nenhum pacote do cadaver" : recebidas[^1].ToString());

		// ---- (ii) SOCO NO CADAVER NAO O GIRA ----
		// Alfa fica ao NORTE do corpo, virado pro SUL: se este golpe deitasse alguem, deitaria pro sul.
		Encostar(a, c, new Vec2(0, 1));
		a.AlvoId = 0;
		a.Combate.Letal = true;
		for (int i = 0; i < 10; i++)
		{
			a.Combate.Recarga = 0;
			a.AtaqueAte = 0;
			Atacar(a, Protocol.Golpe.Leve);
		}
		AfirmarDc("SOCO NO CADAVER: dez socos vindos do NORTE nao giram o corpo -- ele continua deitado pro LESTE",
				  c.DirecaoDeitado == Facing.East, c.DirecaoDeitado.ToString());
		c.ApontarRumoDoGolpe(new Vec2(0, 1));
		AfirmarDc("...nem um rumo de golpe escrito DIRETO no corpo morto (`ApontarRumoDoGolpe` recusa `dead`)",
				  c.DirecaoDeitado == Facing.East && c.RumoDoGolpe.LengthSquared < 1e-6f);

		// ---- (v) CONTRA-EXEMPLO: O VIVO QUE APANHA CONTINUA GIRANDO ----
		b.Combate.Reviver();
		b.Ficha.dead = false;
		b.Altitude = 0;
		b.TiquesDeVoo = 0;
		if (!b.Zone.Equals(palco)) MoveToZone(b.Id, palco, c.Pos + d * (3 * T));
		else b.Pos = c.Pos + d * (3 * T);
		b.CongelarDirecaoDeitada(Facing.East);
		b.ApontarRumoDoGolpe(new Vec2(0, 1));
		AfirmarDc("CONTRA-EXEMPLO: o mesmo rumo escrito num corpo VIVO deita pro SUL -- a regra viva nao mudou "
				+ "(`M.dir = get_dir(M,src)`, `CombatMovement.dm:302`)",
				  b.DirecaoDeitado == Facing.South, b.DirecaoDeitado.ToString());

		b.RumoDoGolpe = default;
		Encostar(a, b, new Vec2(0, 1));
		b.Facing = Facing.North;
		int socos = 0;
		for (int i = 0; i < 40 && b.RumoDoGolpe.LengthSquared < 1e-6f; i++)
		{
			Curar(b);
			a.Combate.Recarga = 0;
			a.AtaqueAte = 0;
			Atacar(a, Protocol.Golpe.Leve);
			socos++;
		}
		AfirmarDc("...e por um soco DE VERDADE: o rumo escrito no vivo e a frente de quem bateu",
				  b.RumoDoGolpe.LengthSquared > 0 && MoveRules.FacingFrom(b.RumoDoGolpe, Facing.North) == Facing.South,
				  $"{socos} socos, rumo ({b.RumoDoGolpe.X:0.#},{b.RumoDoGolpe.Y:0.#})");

		// ---- (iii) O CADAVER ARREMESSADO DEITA PRA ONDE DESLIZOU, E FICA ----
		a.Combate.Bloqueando = false;
		Arremessar(c, new Vec2(0, 1), Empurrao.ResistenciaPadrao, 6);
		bool deitouNoRumoDoVoo = c.DirecaoDeitado == Facing.South;
		Tiques(120);
		AfirmarDc("PRECONDICAO: o arremesso do cadaver pro SUL acabou", c.TiquesDeVoo <= 0, $"{c.TiquesDeVoo}");
		AfirmarDc("ARREMESSADO pro SUL, o cadaver deita pro SUL durante o voo (o rumo do voo)...", deitouNoRumoDoVoo);
		AfirmarDc("...E CONTINUA pro SUL depois de pousar -- sem o ESTALO de volta pro angulo antigo "
				+ "(`CongelarDirecaoDeitada` no pouso de quem pousa caido)",
				  c.DirecaoDeitado == Facing.South, c.DirecaoDeitado.ToString());
		Tiques(100);
		AfirmarDc("...e cem tiques depois continua: a direcao do cadaver e definitiva ate o proximo arremesso",
				  c.DirecaoDeitado == Facing.South);

		// E O MESMO ESTALO NO NOCAUTEADO VIVO -- o caso do agarrao: um rumo de golpe VELHO (leste) e um
		// arremesso pro sul. Antes, ao pousar, o corpo voltava pro leste.
		Curar(b);
		b.Combate.Nocautear(100);
		b.CongelarDirecaoDeitada(Facing.North);
		b.RumoDoGolpe = new Vec2(1, 0);   // o soco velho, de minutos atras
		Arremessar(b, new Vec2(0, 1), Empurrao.ResistenciaPadrao, 6);
		Tiques(120);
		AfirmarDc("E O NOCAUTEADO VIVO jogado com um rumo de golpe VELHO (leste): pousa deitado pro SUL em que "
				+ "deslizou, e nao estala de volta pro leste",
				  b.TiquesDeVoo <= 0 && b.DirecaoDeitado == Facing.South, b.DirecaoDeitado.ToString());
		b.Combate.Levantar();
		Curar(b);
	}

	// =====================================================================
	// A GRADE CEGA -- o interruptor do defeito injetado
	// =====================================================================
	/// <summary>
	/// ============================ O UNICO CAMPO QUE ESTA BANCADA ADICIONOU AO SERVIDOR ============================
	/// Ele e lido em UM lugar (o fim do <see cref="MontarAsGrades"/>) e escrito so aqui, e existe pra
	/// que os defeitos injetados das familias 5 e 6 possam ser injetados **pelo dado** -- que e a regra
	/// do `Mutacao`: o criterio tem que ser o MESMO objeto nas tres passadas, senao o que se mede e a
	/// copia.
	///
	/// A alternativa seria a bancada escrever a propria versao da colisao com a grade vazia, e ai ela
	/// mediria o proprio codigo concordando consigo mesmo. Um `bool` que so a flag `--doiscorposteste`
	/// consegue ligar custa uma comparacao por tique e mede o jogo de verdade.
	/// ==========================================================================================================
	/// </summary>
	private bool _dcGradeCega;

	/// <summary>
	/// ============================ O SEGUNDO INTERRUPTOR: O CADAVER SEM A FOTO ============================
	/// Lido em UM lugar (o `DeixarOCadaver`) e escrito so pela familia 10 desta bancada. Ligado, o
	/// cadaver nasce com o `Body.Novo()` que o `PrepararCombate` lhe da e NAO copia o corpo do morto --
	/// que e, letra por letra, o cadaver que o dono viu: inteiro, limpo, sem o braco que faltava.
	///
	/// Mesma regra do `_dcGradeCega` logo acima: o criterio da familia 10 tem que ser o MESMO
	/// `DeixarOCadaver` com e sem o defeito, senao ele mediria uma copia do cadaver escrita na
	/// bancada concordando consigo mesma.
	/// ====================================================================================================
	/// </summary>
	private bool _cadaverSemFotoDeTeste;
}
