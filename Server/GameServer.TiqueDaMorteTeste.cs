using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ A BANCADA DO QUADRO DA MORTE (`--tiquedamorte`) ============================
/// *"Confirmar com uma bancada que exercite MORTE em combate, e nao so a leitura do log."* -- o pedido
/// do dono, e a razao dele: **um log limpo tambem sai quando nada aconteceu.**
///
/// ============================ O DEFEITO QUE ELA EXISTE PRA PEGAR ============================
/// O cadaver do jogador nascia com `_players[id] = corpo` de dentro do
/// `foreach (ServerPlayer pl in _players.Values)` do <see cref="TickCombate"/> -- e a INSERCAO de
/// chave nova e a unica operacao de `Dictionary` que invalida um enumerador em andamento (desde o
/// .NET Core 3.0 o `Remove` NAO invalida nada, o que fazia o par "insere e remove na linha seguinte"
/// parecer um no-op). Toda morte de jogador derrubava o tique INTEIRO:
///
///     System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
///        at Dictionary`2.ValueCollection.Enumerator.MoveNext()
///        at GameServer.TickCombate(Double dt) in Server\GameServer.Combat.cs:line 1203
///        at GameServer.Tick() in Server\GameServer.cs:line 4742
///        at GameServer._Process(Double delta) in Server\GameServer.cs:line 3118
///
/// (O rastro e o do relato do dono. As duas ultimas linhas envelheceram -- o `GameServer.cs` cresceu
/// e hoje as mesmas chamadas sao a 4763 e a 3131 --, e a PRIMEIRA continua exata: o `foreach` mora
/// na 1203. Com o defeito injetado, esta bancada o reproduz nesta forma.)
///
/// `TickCombate` e a PRIMEIRA chamada do `Tick()`: os ~60 subsistemas depois dele -- fichas,
/// projeteis, feridas, buffs, cadaveres, vacuo, gravidade, esferas, sagas, conquista, ceu,
/// curandeiros -- **e o snapshot por zona** perdiam o quadro. A zona inteira congelava no instante
/// exato da morte, que e justamente quando todo mundo esta olhando.
/// ==========================================================================================
///
/// ============================ POR QUE ELA NAO AFIRMA "NAO HOUVE EXCECAO" ============================
/// Porque isso ficaria verde com o estrago inteiro de pe: bastaria um `catch` em qualquer ponto do
/// caminho pra a excecao sumir do log **sem** os subsistemas voltarem a rodar. Entao o que se mede
/// aqui sao CONSEQUENCIAS, e todas as quatro moram DEPOIS do ponto onde o tique morria:
///
///   1. **A TESTEMUNHA CAIU** -- `TickDosRelogiosDoCorpo`, a PRIMEIRA chamada depois do combate. Um
///      corpo com altura e sem voo desce; se este quadro nao passou do `TickCombate`, ele fica
///      pairando;
///   2. **O PROJETIL ANDOU** -- `TickDosProjeteis`, no meio do tique. Um tiro de ki no ar avanca;
///   3. **A FERIDA SINCRONIZOU** -- `TickDasFeridas`, no bloco de 5 Hz, ja perto do fim: a mascara do
///      corpo ferido e recalculada e o cache `EnvFeridas` (o que a zona ja recebeu) e reescrito;
///   4. **O QUADRO CHEGOU AO FIM** -- `_quadrosInteiros`, escrito na ULTIMA linha do `Tick()`, depois
///      do snapshot. Ver o bloco que o escreve: e a unica coisa observavel depois do ultimo passo.
///
/// E as duas metades do pedido: as quatro sao medidas **nos quadros ANTES da morte** (senao "chegou
/// ao fim" ficaria verde num servidor que nunca roda o tique inteiro) e **no quadro da morte**.
/// ================================================================================================
///
/// ============================ A MORTE E DE APANHAR, E O TIQUE E O DE VERDADE ============================
/// Ninguem chama `Morrer()`, `IrProAlem` nem `DeixarOCadaver` aqui: um algoz soca a vitima pelo
/// <see cref="Atacar"/> -- o mesmo funil da tecla de espaco -- ate um membro VITAL zerar. E o que
/// roda entre um golpe e outro e o `Tick()` inteiro do servidor, e nao um subsistema escolhido a
/// dedo: a recarga do golpe so anda porque o `TickCombate` a abate.
///
/// A UNICA COISA ADIANTADA E O RELOGIO. O `AMorteAconteceu` (producao) marca 15 s de corpo no chao;
/// a bancada empurra esse relogio pro passado pra nao dormir quinze segundos. **O percurso nao muda**
/// -- quem decide o que fazer com o morto continua sendo a triagem, chamada de dentro do laco.
/// ====================================================================================================
///
/// ============================ AS OITO FAMILIAS ============================
///   1. A LINHA DE BASE          -- tres quadros com todo mundo vivo: as quatro consequencias.
///   2. A MORTE EM COMBATE       -- dano de verdade ate o vital zerar, com a luta ativa.
///   3. O QUADRO ANTERIOR        -- o corpo ja morto no chao, o prazo ainda correndo.
///   4. O QUADRO DA MORTE        -- o cadaver NASCE dentro do laco, e as quatro consequencias valem.
///   5. NINGUEM PRESO NEM ZUMBI  -- o morto sai do lugar, o cadaver fica, as duas filas esvaziam.
///   6. O IRMAO DA FILA DE NPC   -- `_npcsPraTirar`: a guarda contra a entrada em dobro, e UM cadaver.
///   7. O IRMAO DA FILA DE VOLTA -- `_acordar`/`TickDeQuemVolta`: o boneco apanha e o dono volta.
///   8. O DEFEITO INJETADO       -- o cadaver de volta no `_players`, e as quatro consequencias
///      REPROVANDO. Sem esta familia as sete de cima sao frases que eu escrevi e que a bancada
///      repetiu.
/// ==========================================================
///
///     Godot --headless --path . --host --rede 7981 --tiquedamorte
///                      --raca Saiyan --conta bancada_tique --nome Cronista
///
/// TUDO O QUE ELA POE NO MUNDO SAI no `finally` -- corpos forjados, cadaveres, tiros no ar --, e o
/// que ela mexe no servidor (a cadencia do tique, o interruptor do defeito) e devolvido junto.
/// </summary>
public partial class GameServer
{
	private bool _tiqueDaMorteDeTeste;
	private int _tmOk, _tmFalhou;

	/// <summary>O `lugar` de cada corpo forjado -- mesma disciplina do `_lugarDaBancadaDeBio`.</summary>
	private ulong _lugarDaBancadaDoTique = 8_700_000;

	/// <summary>
	/// ============================ O DEFEITO INJETAVEL: O CADAVER DE VOLTA NO `_players` ============================
	/// FALSO EM JOGO, SEMPRE. Lido em UM lugar (<see cref="DeixarOCadaver"/>), onde ele reproduz letra
	/// por letra o par que existia antes do conserto -- inserir no `_players` e tirar na linha
	/// seguinte. Ver o bloco la, e o `_borraoSoComSkill` pro mesmo padrao.
	/// ==========================================================================================================
	/// </summary>
	private bool _cadaverNoPlayersDeTeste;

	private void AfirmarTm(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _tmOk++; GD.Print($"[tique]   OK    {oque}"); return; }
		_tmFalhou++;
		GD.PrintErr($"[tique]   FALHA {oque}   {detalhe}");
	}

	// =====================================================================
	// O QUE UM QUADRO DEIXA PRA TRAS
	// =====================================================================
	/// <summary>
	/// AS QUATRO CONSEQUENCIAS DE UM QUADRO, mais o que aquele quadro fez de proprio.
	///
	/// `Estouro` existe pra o console e pro relato -- **ele nao e criterio de nada**. Ver o cabecalho:
	/// "nao houve excecao" fica verde com um `catch` no meio do caminho e o estrago intacto.
	/// </summary>
	private readonly record struct QuadroMedido(
		bool ChegouAoFim, bool ATestemunhaCaiu, bool OProjetilAndou, bool AFeridaSincronizou,
		bool AMascaraEraVisivel, bool OTiroNasceuVivo, int CadaveresNovos, string Estouro);

	/// <summary>
	/// ============================ A CADENCIA, PINADA -- E ISSO E UMA DECISAO ============================
	/// O bloco de 5 Hz do `Tick()` (`if (++_tickCount % TicksPorFicha == 0)`) e onde moram o
	/// `TickFichas` e o `TickDasFeridas` -- ou seja, a consequencia numero 3. Deixar a cadencia correr
	/// solta faria a afirmacao valer em um quadro de cada seis, e a bancada mediria a SORTE do resto.
	///
	/// `TicksPorFicha - 1` faz o `++` cair num multiplo de 6 que **nao** e multiplo de 30 (o bloco de
	/// 1 Hz) nem de 3600 (o salvamento em disco). Isso e de proposito e nos dois sentidos: o bloco de
	/// 1 Hz avanca relogios do MUNDO em passos de um segundo (gestacao, estudo, destruicao) e rodar
	/// centenas deles em meio segundo de bancada seria envelhecer o mundo do dono de graca; e o
	/// `Persistir` escreveria no disco por conta de uma medicao.
	/// ================================================================================================
	/// </summary>
	/// <param name="comBlocoDeFicha">
	/// FALSO no meio da surra (nada e medido la, e cada volta paga o `Treinar` do servidor inteiro),
	/// VERDADEIRO nos quadros que a bancada MEDE.
	/// </param>
	private void PinarACadencia(bool comBlocoDeFicha) =>
		_tickCount = comBlocoDeFicha ? TicksPorFicha - 1 : 0;

	/// <summary>
	/// ============================ UM QUADRO DE VERDADE, E O QUE SOBROU DELE ============================
	/// Ela arma as tres sondas, chama o <see cref="Tick"/> **inteiro** (nao um subsistema escolhido) e
	/// pergunta depois o que aconteceu. As tres sondas sao repostas a cada quadro de proposito: a
	/// testemunha volta pro ar (senao ela chega ao chao e para de cair), a mascara de feridas e
	/// reescrita (a regeneracao passiva a apaga aos poucos) e um tiro novo nasce (o anterior morre por
	/// alcance).
	///
	/// O `try` NAO e o criterio -- ele so existe pra a bancada continuar de pe e ARRUMAR o mundo
	/// quando o quadro morre no meio. O que reprova sao as quatro consequencias.
	/// ==============================================================================================
	/// </summary>
	private QuadroMedido RodarUmQuadro(ServerPlayer testemunha, ZoneKey ondeOlhar)
	{
		// ---- SONDA 1: um corpo no ar, sem voo. `TickDoVoo` o derruba (a primeira chamada depois do combate)
		testemunha.Voando = false;
		testemunha.Nadando = false;
		testemunha.Altitude = 10 * ZoneCollision.TileSize;
		float alturaAntes = testemunha.Altitude;

		// ---- SONDA 3: o corpo ferido, e o cache do que a zona ja viu ZERADO ----
		// Ferir e producao (`CombatState.Ferir`, o mesmo funil do soco) e NAO-LETAL de proposito: a
		// testemunha nao pode morrer no meio da medicao.
		FerirOsBracos(testemunha);
		testemunha.EnvFeridas = default;
		MascaraDeFeridas mascara = Feridas.De(testemunha.Combate.Corpo);

		// ---- SONDA 2: um tiro de ki no ar ----
		// Pra CIMA (o rumo dado) e da altura da testemunha: assim ele nao encosta em ninguem no unico
		// quadro que interessa, e o que se mede e o AVANCO e nao um acerto.
		Projetil tiro = Disparar(testemunha, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 30,
		}, rumoDado: new Vec2(0, -1));
		Vec2 tiroAntes = tiro.Pos;

		int cadaveresAntes = QuantosCadaveres(ondeOlhar);
		long fimAntes = _quadrosInteiros;

		PinarACadencia(comBlocoDeFicha: true);

		string estouro = "";
		try { Tick(); }
		catch (Exception e) { estouro = e.Message; }

		return new QuadroMedido(
			ChegouAoFim: _quadrosInteiros == fimAntes + 1,
			ATestemunhaCaiu: testemunha.Altitude < alturaAntes,
			OProjetilAndou: (tiro.Pos - tiroAntes).LengthSquared > 0.01f,
			// **`!= default` E NAO "igual a mascara de agora"**: o proprio quadro cura um pouco o corpo
			// (`RegenerarPassivo`), entao a mascara depois do tique pode ja nao ser a de antes dele. O
			// que prova que o `TickDasFeridas` rodou e o cache ter deixado de estar zerado.
			AFeridaSincronizou: testemunha.EnvFeridas != default,
			AMascaraEraVisivel: mascara != default,
			OTiroNasceuVivo: tiro.Vivo || (tiro.Pos - tiroAntes).LengthSquared > 0.01f,
			CadaveresNovos: QuantosCadaveres(ondeOlhar) - cadaveresAntes,
			Estouro: estouro);
	}

	/// <summary>As quatro consequencias de um quadro, cada uma na sua linha.</summary>
	private void AfirmarOQuadroInteiro(string rotulo, QuadroMedido q)
	{
		AfirmarTm($"{rotulo}: PRECONDICAO -- havia ferida pra sincronizar e tiro no ar",
				  q.AMascaraEraVisivel && q.OTiroNasceuVivo,
				  $"mascara visivel={q.AMascaraEraVisivel} tiro={q.OTiroNasceuVivo}");
		AfirmarTm($"{rotulo}: O QUADRO CHEGOU AO FIM (a ultima linha do `Tick()` rodou)",
				  q.ChegouAoFim, q.Estouro);
		AfirmarTm($"{rotulo}: ...o corpo no ar CAIU (`TickDosRelogiosDoCorpo`, logo depois do combate)",
				  q.ATestemunhaCaiu, q.Estouro);
		AfirmarTm($"{rotulo}: ...o projetil ANDOU (`TickDosProjeteis`, no meio do quadro)",
				  q.OProjetilAndou, q.Estouro);
		AfirmarTm($"{rotulo}: ...a ferida SINCRONIZOU (`TickDasFeridas`, no bloco de 5 Hz, perto do fim)",
				  q.AFeridaSincronizou, q.Estouro);
	}

	// =====================================================================
	// AS FERRAMENTAS DO PALCO
	// =====================================================================
	/// <summary>
	/// FERE OS DOIS BRACOS, sem matar. Pelo funil de producao (<see cref="CombatState.Ferir"/>, o
	/// mesmo do soco) e NAO-LETAL: golpe nao-letal tem piso de vida, entao a mascara fica visivel e o
	/// corpo continua de pe.
	/// </summary>
	private static void FerirOsBracos(ServerPlayer pl)
	{
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
			if (p.Nome is "Braco esquerdo" or "Braco direito" && !p.Decepado)
				pl.Combate.Ferir(p, p.VidaMax * 0.9, letal: false);
	}

	private int QuantosCadaveres(ZoneKey zona)
	{
		int n = 0;
		foreach (ServerPlayer o in ZoneList(zona.Hash)) if (o.ECadaver) n++;
		return n;
	}

	private int CadaveresDe(ZoneKey zona, string nome)
	{
		int n = 0;
		foreach (ServerPlayer o in ZoneList(zona.Hash))
			if (o.ECadaver && o.NomeDeQuemMorreu == nome) n++;
		return n;
	}

	/// <summary>
	/// UM CORPO NO PALCO. `NascerNpc` e a sequencia unica de nascimento (molde -> `Statify` ->
	/// `PrepararCombate` -> listas): montar um `ServerPlayer` a mao aqui seria a segunda copia dela.
	///
	/// ============================ A RACA E REESCRITA, E O CORPO REFEITO ============================
	/// O molde `cidadao` sorteia a raca, e duas coisas dependem dela no caminho da morte: o EIXO DE
	/// REGENERACAO (que o `CombatState` recebe no construtor) e o `RegeneraDecepado` -- e um
	/// Namekuseijin **entra em coma em vez de morrer** quando o vital zera (`MeleeResolver.cs:392`).
	/// Uma bancada que sorteasse a raca da vitima passaria ou falharia por sorte.
	///
	/// Por isso a raca vira "Human" e o <see cref="PrepararCombate"/> e chamado DE NOVO: e ele quem
	/// constroi o corpo a partir da raca, e reescrever `Ficha.Race` depois nao refaz membro nenhum.
	/// ==========================================================================================
	/// </summary>
	/// <param name="comDono">
	/// Empresta o `Peer` do host e apaga o `Papel`: com os dois, `Gente.EhJogador` responde SIM e a
	/// triagem da morte leva o corpo pro Outro Mundo -- que e o caminho que derrubava o tique. Sem
	/// eles o corpo e NPC do mundo e cai na outra fila (a familia 6).
	/// </param>
	private ServerPlayer? ForjarPraTique(ServerPlayer host, string nome, Vec2 palco, bool comDono,
										 List<ServerPlayer> forjados)
	{
		ServerPlayer? p = NascerNpc("cidadao", host.Zone, host.Pos + palco, ++_lugarDaBancadaDoTique);
		if (p == null) return null;

		forjados.Add(p);

		// ============================ SEM CEREBRO -- E ISSO E A ARENA, NAO O SISTEMA ============================
		// `NascerNpc` entrega o corpo COM IA (`Cerebro`), que e o certo pra um cidadao. Aqui ela
		// atrapalha por dois caminhos que a primeira rodada desta bancada mostrou:
		//
		//   * a vitima REVIDA, e dois golpes simultaneos viram **Zanzo Clash** -- que por desenho
		//     CANCELA o soco (`TentarEmbate`, `GameServer.Combat.cs`). Foram 26 golpes em 900 quadros
		//     sem uma morte, e o console dizia `ZANZO CLASH: Vitima do Tique x Algoz do Tique`;
		//   * os dois ANDAM sozinhos, e um alvo que sai do alcance faz o soco cair no ar.
		//
		// Nada disso e o que esta sendo medido: o defeito mora no CAMINHO DA MORTE (triagem -> viagem
		// -> cadaver, de dentro do laco), e esse caminho nao pergunta quem dirigia o corpo. Sem
		// `Cerebro` o corpo e inerte -- o `TickDosCorposSemDono` filtra por `Cerebro != null` --, e
		// isso tambem e o que deixa a familia 7 existir: `LargarOCorpo` recusa quem esta possuido
		// (`SemAsRedeas`), e um corpo com IA responde SIM a essa pergunta.
		// ====================================================================================================
		p.Cerebro = null;

		p.Pos = host.Pos + palco;
		p.Name = nome;
		p.Race = "Human";
		p.Ficha.Race = "Human";
		p.Ficha.ParentRace = "Human";
		p.Ficha.Statify();
		PrepararCombate(p, null);
		p.Combate.SincronizarVida();

		if (comDono)
		{
			p.Peer = host.Peer;
			p.Papel = null;
			// CONTA PROPRIA E `Slot` INTOCADO (-1, o padrao): sem a conta, regras que procuram o dono
			// por `Conta` acertariam o host; com `Slot >= 0` o `Persistir` gravaria este boneco DENTRO
			// do personagem de quem roda a bancada. E o achado que o `ForjarCorpo` da `--diagbio` ja
			// registra.
			p.Conta = $"bancada-tique-{p.Id}";
		}
		return p;
	}

	/// <summary>
	/// ============================ O ALGOZ, E O NUMERO QUE O SOCO REALMENTE LE ============================
	/// O dano do melee sai da RAZAO entre os dois BP **EXPRESSOS** (`CombatMath.BpModulus`) -- e
	/// `expressedBP` **nao e escrito pelo `Statify`**: quem o escreve e o `PowerLevel`, dentro do
	/// <see cref="Jandirus.Core.Stats.Fighter.Tick"/>, que em jogo so roda no `TickFichas` (5 Hz).
	///
	/// ---- E FOI ASSIM QUE A PRIMEIRA RODADA DESTA BANCADA FALHOU ----
	/// Ela escrevia `BP = 5e8` e chamava `Statify()`, e o `--diaggolpe` mostrou o resultado em uma
	/// linha: *"dano 4,47 | BP expresso 4.197 vs 4.883 (razao x0,85) | BP base 500.000.000"*. Ou seja
	/// **o algoz de meio bilhao era o mais fraco dos dois**, porque o numero que o combate le ainda era
	/// o do nascimento. Dez socos em 300 quadros, nenhuma morte, e nenhuma das afirmacoes seguintes
	/// tinha como ficar verde.
	///
	/// A licao e do projeto e nao desta bancada: **escrever o campo nao e aplicar o campo**. Aqui a
	/// conta e refeita pelo funil de producao (o mesmo `Ficha.Tick` do servidor), e nao a mao.
	/// ================================================================================================
	/// </summary>
	private void ArmarOAlgoz(ServerPlayer algoz)
	{
		algoz.Ficha.BP = 5e8;
		algoz.Ficha.Statify();
		algoz.Ficha.Tick(agoraMs: NowMs());
		algoz.Combate.SincronizarVida();
		algoz.Combate.Letal = true;
	}

	/// <summary>
	/// ============================ MATA DE APANHAR, PELO FUNIL DE PRODUCAO ============================
	/// O algoz chama <see cref="Atacar"/> -- a mesma porta da tecla de espaco -- e o que corre entre um
	/// golpe e outro e o <see cref="Tick"/> INTEIRO: e o `TickCombate` que abate a recarga, entao a
	/// cadencia dos socos aqui e a cadencia do jogo.
	///
	/// A BANCADA SO CUIDA DA ARENA: ela reencosta os dois quando o arremesso do golpe joga a vitima
	/// longe (`TentarEmpurrar` e consequencia do soco, e ele afasta o alvo do alcance). O golpe, o
	/// sorteio do membro, o dano e a morte sao todos de la.
	/// ==========================================================================================
	/// </summary>
	/// <returns>Quantos golpes sairam, e se a luta estava ATIVA no golpe que matou.</returns>
	private (int Golpes, bool LutaAtiva, int Quadros) MatarNoSoco(ServerPlayer algoz, ServerPlayer vitima,
																 int maxQuadros = 300)
	{
		algoz.Combate.Letal = true;
		int golpes = 0, quadros = 0;

		while (!vitima.Ficha.dead && quadros < maxQuadros)
		{
			algoz.Facing = Facing.East;
			algoz.Altitude = vitima.Altitude = 0;
			vitima.Voando = algoz.Voando = false;
			if ((vitima.Pos - algoz.Pos).LengthSquared > 24 * 24)
				vitima.Pos = algoz.Pos + new Vec2(20, 0);

			if (algoz.Combate.PodeAtacar()) { Atacar(algoz, Protocol.Golpe.Pesado); golpes++; }

			PinarACadencia(comBlocoDeFicha: false);
			Tick();
			quadros++;
		}

		// ============================ A TAG DE COMBATE, LIDA EM QUEM SOBROU ============================
		// `MeleeResolver.Resolver` chama `EntrarEmCombate()` **nos dois lados** (`MeleeResolver.cs:131`)
		// antes de aplicar qualquer dano: 90 s de tag e 10 s de "luta de verdade". Entao a tag acesa no
		// ALGOZ, depois da morte, quer dizer que o golpe passou pelo resolvedor -- ou seja, que houve
		// LUTA, e nao uma chamada de `Morrer()` de bancada.
		//
		// ---- POR QUE NAO SE PERGUNTA A VITIMA, E POR QUE NAO SE PERGUNTA ANTES ----
		// A vitima nao serve porque `Morrer()` zera os dois relogios dela (`CombatState.cs:482-483`) --
		// ela SEMPRE responderia "nao". E perguntar antes do golpe tambem nao serve: a primeira rodada
		// desta bancada fazia isso e ficou vermelha por sorte, porque com o algoz de meio bilhao **a
		// morte acontece no PRIMEIRO soco** -- e antes dele ninguem tinha entrado em combate ainda.
		// ==========================================================================================
		bool lutaAtiva = algoz.Combate.EmCombate > 0 && algoz.Combate.LutandoDeVerdade > 0;
		return (golpes, lutaAtiva, quadros);
	}

	// =====================================================================
	// A BANCADA
	// =====================================================================
	private void RodarBancadaDoTiqueDaMorte(ServerPlayer host)
	{
		_tmOk = _tmFalhou = 0;
		GD.Print("[tique] ================ O QUADRO DA MORTE ================");

		int tickGuardado = _tickCount;
		var forjados = new List<ServerPlayer>();
		ZoneKey arena = host.Zone;

		try
		{
			ServerPlayer? testemunha = ForjarPraTique(host, "Testemunha do Tique", new Vec2(-260, -120),
													  comDono: false, forjados);
			if (testemunha == null)
			{
				AfirmarTm("sem molde de NPC nao ha palco -- a bancada nao mediu nada", false);
				return;
			}

			ALinhaDeBase(testemunha, arena);
			AMorteEmCombate(host, testemunha, arena, forjados);
			OIrmaoDaFilaDeNpc(host, arena, forjados);
			OIrmaoDaFilaDeVolta(host, arena, forjados);
			ODefeitoInjetado(host, testemunha, arena, forjados);

			AfirmarTm("a bancada chegou ao fim (sem esta linha, abortar no meio reportaria '0 falhas')",
					  true);
		}
		catch (Exception e)
		{
			AfirmarTm($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			// O INTERRUPTOR PRIMEIRO: se a familia 8 estourou no meio, tudo o que vem abaixo (e o
			// servidor inteiro depois da bancada) rodaria com o defeito ligado.
			_cadaverNoPlayersDeTeste = false;
			_tickCount = tickGuardado;

			// OS CADAVERES QUE ELA ERGUEU. Pelo funil de producao (`DesfazerOCadaver`), que tambem
			// avisa a zona -- deixa-los seria enfeitar o mapa do dono com um monte de corpo de teste.
			var nomes = new HashSet<string>(StringComparer.Ordinal);
			foreach (ServerPlayer f in forjados) nomes.Add(f.Name);
			foreach (ServerPlayer o in ZoneList(arena.Hash).ToArray())
				if (o.ECadaver && nomes.Contains(o.NomeDeQuemMorreu)) DesfazerOCadaver(o);

			foreach (ServerPlayer f in forjados)
			{
				// O TIRO NO AR SAI JUNTO DE QUEM O DEU: sem isto os projeteis da bancada continuariam
				// voando pela zona do host depois que os corpos sumissem.
				LimparProjeteisDeUmDono(f.Id, f.Zone.Hash);
				if (f.BonecoLargado is { } b) { ZoneList(b.Zone.Hash).Remove(b); f.BonecoLargado = null; }
				_npcsPraTirar.Remove(f);
				_acordar.Remove(f.Id);
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
				f.Peer = null;
			}
		}

		GD.Print($"[tique] ================ {_tmOk} passaram, {_tmFalhou} falharam ================");
	}

	// =====================================================================
	// 1) A LINHA DE BASE -- o quadro ANTES de qualquer morte
	// =====================================================================
	/// <summary>
	/// ============================ A METADE DO PEDIDO QUE E FACIL DE ESQUECER ============================
	/// *"prove que ele chega ao fim agora, E prove que o quadro anterior a morte tambem chegava"*.
	///
	/// Sem esta familia, "o quadro da morte chegou ao fim" ficaria verde num servidor que **nunca**
	/// roda o tique inteiro -- as quatro consequencias poderiam estar mortas por qualquer outro
	/// motivo, e a bancada estaria medindo um servidor quebrado concordando consigo mesmo.
	/// ================================================================================================
	/// </summary>
	private void ALinhaDeBase(ServerPlayer testemunha, ZoneKey arena)
	{
		GD.Print("[tique] -- 1) a linha de base: tres quadros com todo mundo vivo --");

		for (int i = 1; i <= 3; i++)
		{
			QuadroMedido q = RodarUmQuadro(testemunha, arena);
			AfirmarOQuadroInteiro($"quadro vivo {i}", q);
			AfirmarTm($"quadro vivo {i}: ...e ninguem morreu nele (nao ha cadaver novo)",
					  q.CadaveresNovos == 0, $"{q.CadaveresNovos} cadaveres");
		}
	}

	// =====================================================================
	// 2 a 5) A MORTE EM COMBATE, O QUADRO ANTERIOR, O QUADRO DA MORTE, E O DEPOIS
	// =====================================================================
	private void AMorteEmCombate(ServerPlayer host, ServerPlayer testemunha, ZoneKey arena,
								 List<ServerPlayer> forjados)
	{
		GD.Print("[tique] -- 2) a morte em combate, com dano de verdade --");

		ServerPlayer? algoz = ForjarPraTique(host, "Algoz do Tique", new Vec2(220, 0),
											 comDono: false, forjados);
		ServerPlayer? vitima = ForjarPraTique(host, "Vitima do Tique", new Vec2(240, 0),
											  comDono: true, forjados);
		if (algoz == null || vitima == null) { AfirmarTm("sem palco pra familia 2", false); return; }

		ArmarOAlgoz(algoz);

		AfirmarTm("PRECONDICAO: a vitima tem dono na tela (sem isso a triagem nao a leva pro alem)",
				  EhJogador(vitima), $"peer={vitima.Peer != null} papel={vitima.Papel != null}");
		AfirmarTm("PRECONDICAO: os dois estao vivos e o algoz acha a vitima na frente",
				  !vitima.Ficha.dead && AlvoNaFrente(SituarParaOSoco(algoz, vitima)) == vitima);

		(int golpes, bool lutaAtiva, int quadros) = MatarNoSoco(algoz, vitima);

		AfirmarTm("A VITIMA MORREU DE APANHAR (`Atacar` -> `MeleeResolver` -> `Morrer`)",
				  vitima.Ficha.dead, $"{golpes} golpes em {quadros} quadros");
		AfirmarTm("...com um membro VITAL zerado (foi o dano que matou, e nao uma chamada de bancada)",
				  vitima.Combate.Corpo.DeveMorrer());
		AfirmarTm("...e foi LUTA: quem matou saiu do golpe com a tag de combate acesa "
				+ "(`EntrarEmCombate`, chamado pelo resolvedor nos dois lados)", lutaAtiva,
				  $"tag {algoz.Combate.EmCombate:0.#}s / luta {algoz.Combate.LutandoDeVerdade:0.#}s");
		AfirmarTm("...e a do MORTO foi zerada pelo proprio `Morrer()` (o funil unico da morte rodou)",
				  vitima.Combate.EmCombate == 0 && vitima.Combate.LutandoDeVerdade == 0);
		AfirmarTm("...e quem armou o prazo foi o caminho de producao (`AMorteAconteceu`, 15 s de chao)",
				  vitima.RelogioDaMorte > NowMs(), $"faltam {vitima.RelogioDaMorte - NowMs()} ms");
		AfirmarTm("...e o corpo AINDA esta no `_players` e na zona (a viagem e do fim do prazo)",
				  _players.ContainsKey(vitima.Id) && ZoneList(arena.Hash).Contains(vitima));

		// ---- 3) O QUADRO ANTERIOR AO DA MORTE -------------------------------
		GD.Print("[tique] -- 3) o quadro ANTERIOR ao da morte (o corpo ja caido, o prazo correndo) --");
		QuadroMedido antes = RodarUmQuadro(testemunha, arena);
		AfirmarOQuadroInteiro("quadro com o morto no chao", antes);
		AfirmarTm("quadro com o morto no chao: ...e o cadaver ainda NAO nasceu (o prazo nao venceu)",
				  antes.CadaveresNovos == 0 && vitima.Zone.Equals(arena),
				  $"{antes.CadaveresNovos} cadaveres novos");

		// ---- 4) O QUADRO DA MORTE ------------------------------------------
		GD.Print("[tique] -- 4) O QUADRO DA MORTE --");

		// **A UNICA COISA ADIANTADA E O RELOGIO** -- ver o cabecalho. A triagem continua sendo chamada
		// de dentro do laco do `TickCombate`, que e o ponto do defeito.
		Vec2 ondeCaiu = vitima.Pos;
		vitima.RelogioDaMorte = NowMs() - 1;
		QuadroMedido morte = RodarUmQuadro(testemunha, arena);

		AfirmarTm("O CAMINHO FOI MESMO EXERCITADO: o cadaver NASCEU neste quadro",
				  morte.CadaveresNovos == 1, $"{morte.CadaveresNovos} cadaveres novos");
		AfirmarTm("...e quem morreu VIAJOU pro Outro Mundo neste mesmo quadro",
				  Alem.EhOAlem(vitima.Zone), vitima.Zone.Name);
		AfirmarOQuadroInteiro("QUADRO DA MORTE", morte);
		AfirmarTm("QUADRO DA MORTE: ...e nao houve estouro (linha de console -- NAO e o criterio acima)",
				  morte.Estouro.Length == 0, morte.Estouro);

		// ---- 5) NINGUEM FICOU PRESO NEM ZUMBI -------------------------------
		GD.Print("[tique] -- 5) ninguem preso, ninguem zumbi --");

		AfirmarTm("o morto SAIU do lugar onde caiu (ninguem fica de pe no proprio velorio)",
				  !ZoneList(arena.Hash).Contains(vitima));

		ServerPlayer? cadaver = null;
		foreach (ServerPlayer o in ZoneList(arena.Hash))
			if (o.ECadaver && o.NomeDeQuemMorreu == vitima.Name) cadaver = o;

		AfirmarTm("...e o corpo FICOU: ha um cadaver dele na zona", cadaver != null);
		AfirmarTm("...no ponto exato onde ele caiu",
				  cadaver != null && (cadaver.Pos - ondeCaiu).LengthSquared < 0.01f,
				  cadaver == null ? "" : $"({cadaver.Pos.X:0},{cadaver.Pos.Y:0}) x ({ondeCaiu.X:0},{ondeCaiu.Y:0})");
		AfirmarTm("...e o cadaver NAO entrou no `_players` (era exatamente ele o defeito)",
				  cadaver != null && !_players.ContainsKey(cadaver.Id));

		// AS DUAS FILAS SAO O DEFEITO SEGUINTE, E ELE E SILENCIOSO: uma fila que ninguem consome nao
		// aparece como erro -- aparece como corpo que tica pra sempre e como reputacao cobrada em dobro.
		AfirmarTm("...e a fila de corpos sem dono foi DRENADA no mesmo quadro (`_npcsPraTirar` vazia)",
				  _npcsPraTirar.Count == 0, $"{_npcsPraTirar.Count} presos");
		AfirmarTm("...e a fila de quem volta pro corpo tambem (`_acordar` vazia)",
				  _acordar.Count == 0, $"{_acordar.Count} presos");

		// E MAIS TRES QUADROS: a morte nao pode ficar se repetindo sozinha.
		int cadaveresAgora = CadaveresDe(arena, vitima.Name);
		bool seguiuInteiro = true;
		for (int i = 0; i < 3; i++)
		{
			QuadroMedido q = RodarUmQuadro(testemunha, arena);
			seguiuInteiro &= q.ChegouAoFim;
		}
		AfirmarTm("...e os tres quadros SEGUINTES tambem chegam ao fim", seguiuInteiro);
		AfirmarTm("...sem nascer um segundo cadaver do mesmo morto",
				  CadaveresDe(arena, vitima.Name) == cadaveresAgora,
				  $"{CadaveresDe(arena, vitima.Name)} x {cadaveresAgora}");
	}

	/// <summary>
	/// Encosta os dois pra a PRECONDICAO valer sem depender de onde o palco os deixou. Devolve o
	/// proprio algoz pra a linha caber em uma afirmacao.
	/// </summary>
	private static ServerPlayer SituarParaOSoco(ServerPlayer algoz, ServerPlayer alvo)
	{
		algoz.Facing = Facing.East;
		alvo.Pos = algoz.Pos + new Vec2(20, 0);
		alvo.Altitude = algoz.Altitude = 0;
		return algoz;
	}

	// =====================================================================
	// 6) O IRMAO DA FILA DE NPC -- `_npcsPraTirar`
	// =====================================================================
	/// <summary>
	/// ============================ A GUARDA CONTRA A ENTRADA EM DOBRO ============================
	/// O ramo do NPC na triagem **nao rearma o relogio da morte** -- de proposito: o corpo sai do
	/// mundo no dreno do fim deste mesmo tique. So que o `_npcsPraTirar.Clear()` e a ULTIMA linha do
	/// `TickCombate`, e qualquer excecao no meio do tique o pula (era o que a morte de jogador fazia).
	/// Ai o mesmo corpo continua morto e vencido, cai na triagem de novo no quadro seguinte, e a fila
	/// fica com DUAS entradas: `MorreuUmCorpoSemDono` cobrado em dobro e `DeixarOCadaver` chamado duas
	/// vezes -- **dois cadaveres empilhados pra um NPC so**, e isso o jogador ve.
	///
	/// A bancada reproduz o cenario pelo unico jeito honesto: chama a triagem DUAS vezes sem drenar no
	/// meio -- que e literalmente o que dois tiques seguidos sem `Clear` fazem -- e depois deixa o
	/// tique de verdade drenar. O criterio nao e o tamanho da fila: e **quantos cadaveres nasceram**.
	/// ========================================================================================
	/// </summary>
	private void OIrmaoDaFilaDeNpc(ServerPlayer host, ZoneKey arena, List<ServerPlayer> forjados)
	{
		GD.Print("[tique] -- 6) o irmao da fila de NPC (`_npcsPraTirar`) --");

		ServerPlayer? algoz = ForjarPraTique(host, "Algoz de NPC", new Vec2(220, 200),
											 comDono: false, forjados);
		ServerPlayer? npc = ForjarPraTique(host, "Cidadao do Tique", new Vec2(240, 200),
										   comDono: false, forjados);
		if (algoz == null || npc == null) { AfirmarTm("sem palco pra familia 6", false); return; }

		ArmarOAlgoz(algoz);

		AfirmarTm("PRECONDICAO: o corpo e NPC DO MUNDO (`Gente.EhNpcDoMundo`), e nao jogador",
				  EhNpcDoMundo(npc) && !EhJogador(npc));

		(int golpes, _, _) = MatarNoSoco(algoz, npc);
		AfirmarTm("o NPC morreu de apanhar", npc.Ficha.dead, $"{golpes} golpes");

		// A TRIAGEM DUAS VEZES, SEM DRENO NO MEIO -- a linha exata que o `TickCombate` roda.
		npc.RelogioDaMorte = NowMs() - 1;
		int filaAntes = _npcsPraTirar.Count;
		VenceuOPrazoDaMorte(npc);
		VenceuOPrazoDaMorte(npc);
		AfirmarTm("A TRIAGEM CHAMADA DUAS VEZES SEM DRENO ENFILEIRA **UMA** (a guarda nova)",
				  _npcsPraTirar.Count == filaAntes + 1, $"{_npcsPraTirar.Count - filaAntes} entradas");

		int cadaveresAntes = CadaveresDe(arena, npc.Name);
		long fimAntes = _quadrosInteiros;
		PinarACadencia(comBlocoDeFicha: true);
		string estouro = "";
		try { Tick(); }
		catch (Exception e) { estouro = e.Message; }

		AfirmarTm("...um tique depois, ha EXATAMENTE UM cadaver dele (e nao dois)",
				  CadaveresDe(arena, npc.Name) - cadaveresAntes == 1,
				  $"{CadaveresDe(arena, npc.Name) - cadaveresAntes} cadaveres");
		AfirmarTm("...o corpo SAIU do mundo (nem no `_players`, nem na zona)",
				  !_players.ContainsKey(npc.Id) && !ZoneList(arena.Hash).Contains(npc));
		AfirmarTm("...a fila esvaziou (o dreno rodou, e ninguem ficou pra tras)",
				  _npcsPraTirar.Count == 0, $"{_npcsPraTirar.Count} presos");
		AfirmarTm("...e o quadro que fez tudo isso CHEGOU AO FIM",
				  _quadrosInteiros == fimAntes + 1, estouro);
	}

	// =====================================================================
	// 7) O IRMAO DA FILA DE VOLTA -- `_acordar` / `TickDeQuemVolta`
	// =====================================================================
	/// <summary>
	/// ============================ A OUTRA FILA, COM A REMOCAO PROPRIA DELA ============================
	/// Aqui nao morre ninguem: o que SAI DO MUNDO e o **boneco** de quem largou o corpo, e quem o tira
	/// e o `TickDeQuemVolta` -- o laco que passou a andar por INDICE (numa `List<T>` o `Add` invalida o
	/// enumerador, ao contrario do `Dictionary.Remove`).
	///
	/// O gatilho e de producao e e o pedido do dono em uma linha: *"se elas TE BATEREM vc ACORDA da
	/// meditacao pro corpo real"*. Um soco pelo <see cref="Atacar"/> no corpo parado enfileira o dono
	/// (`MarcarAgressao` -> `AcordarNoCorpo` -> `PorNaFilaDeVolta`), e o dreno acontece no fim do
	/// MESMO tique -- que e o que a fila promete ("na hora" = 33 ms).
	/// ============================================================================================
	/// </summary>
	private void OIrmaoDaFilaDeVolta(ServerPlayer host, ZoneKey arena, List<ServerPlayer> forjados)
	{
		GD.Print("[tique] -- 7) o irmao da fila de volta (`_acordar`) --");

		ServerPlayer? algoz = ForjarPraTique(host, "Algoz do Transe", new Vec2(-240, 240),
											 comDono: false, forjados);
		ServerPlayer? meditante = ForjarPraTique(host, "Meditante do Tique", new Vec2(-220, 240),
												 comDono: true, forjados);
		if (algoz == null || meditante == null) { AfirmarTm("sem palco pra familia 7", false); return; }

		Vec2 ondeFicouOCorpo = meditante.Pos;
		bool largou = LargarOCorpo(meditante, arena, meditante.Pos + new Vec2(900, 0));
		AfirmarTm("PRECONDICAO: o corpo foi largado (o dono foi pra longe, o boneco ficou)",
				  largou && meditante.BonecoLargado != null);
		if (meditante.BonecoLargado is not { } boneco) return;

		AfirmarTm("...o boneco esta na zona e NAO no `_players` (ele nao e simulado)",
				  ZoneList(arena.Hash).Contains(boneco) && !_players.ContainsKey(boneco.Id));

		algoz.Pos = ondeFicouOCorpo - new Vec2(20, 0);
		boneco.Pos = ondeFicouOCorpo;
		algoz.Facing = Facing.East;
		algoz.Combate.Letal = false;
		algoz.Combate.Recarga = 0;
		AfirmarTm("...e o soco acha o boneco na frente", AlvoNaFrente(algoz) == boneco);

		Atacar(algoz, Protocol.Golpe.Leve);
		AfirmarTm("UM SOCO DE PRODUCAO NO CORPO PARADO ENFILEIRA O DONO (`_acordar`)",
				  _acordar.Contains(meditante.Id), $"fila com {_acordar.Count}");

		long fimAntes = _quadrosInteiros;
		PinarACadencia(comBlocoDeFicha: true);
		string estouro = "";
		try { Tick(); }
		catch (Exception e) { estouro = e.Message; }

		AfirmarTm("...e no MESMO tique o dono voltou pro corpo", meditante.BonecoLargado == null);
		AfirmarTm("...o boneco SAIU do mundo", !ZoneList(arena.Hash).Contains(boneco));
		AfirmarTm("...o dono esta onde o corpo estava",
				  (meditante.Pos - ondeFicouOCorpo).LengthSquared < 1f,
				  $"({meditante.Pos.X:0},{meditante.Pos.Y:0}) x ({ondeFicouOCorpo.X:0},{ondeFicouOCorpo.Y:0})");
		AfirmarTm("...a fila esvaziou (drenagem completa, ninguem preso)",
				  _acordar.Count == 0, $"{_acordar.Count} presos");
		AfirmarTm("...e o quadro CHEGOU AO FIM", _quadrosInteiros == fimAntes + 1, estouro);
	}

	// =====================================================================
	// 8) O DEFEITO INJETADO -- e a bancada tem que ficar VERMELHA
	// =====================================================================
	/// <summary>
	/// ============================ UMA AFIRMACAO QUE SO FOI VISTA PASSANDO E UMA CONSTANTE ============================
	/// As sete familias acima medem o servidor consertado e ficam verdes. Verde e barato. Esta poe o
	/// MESMO criterio -- as quatro consequencias, medidas pelo MESMO `RodarUmQuadro` -- contra o
	/// defeito que ele existe pra pegar: `_cadaverNoPlayersDeTeste`, que faz o `DeixarOCadaver` voltar
	/// a inserir o cadaver no `_players` e tira-lo na linha seguinte, letra por letra.
	///
	/// **E A REPROVACAO TEM QUE SER NAS CONSEQUENCIAS**, e nao numa linha de excecao: e por isso que
	/// as quatro sao afirmadas invertidas aqui. Uma bancada que so olhasse "estourou?" ficaria verde
	/// no dia em que alguem pusesse um `try/catch` no `_Process` -- com os ~60 subsistemas continuando
	/// a perder o quadro a cada morte.
	///
	/// A VITIMA E NOVA: quem ja morreu na familia 2 esta no Outro Mundo, e um cadaver so nasce uma vez.
	/// =============================================================================================================
	/// </summary>
	private void ODefeitoInjetado(ServerPlayer host, ServerPlayer testemunha, ZoneKey arena,
								  List<ServerPlayer> forjados)
	{
		GD.Print("[tique] -- 8) O DEFEITO INJETADO: o cadaver de volta no `_players` --");

		ServerPlayer? algoz = ForjarPraTique(host, "Algoz Injetado", new Vec2(220, -220),
											 comDono: false, forjados);
		ServerPlayer? vitima = ForjarPraTique(host, "Vitima Injetada", new Vec2(240, -220),
											  comDono: true, forjados);
		if (algoz == null || vitima == null) { AfirmarTm("sem palco pra familia 8", false); return; }

		ArmarOAlgoz(algoz);

		(_, _, _) = MatarNoSoco(algoz, vitima);
		AfirmarTm("PRECONDICAO: a segunda vitima tambem morreu de apanhar", vitima.Ficha.dead);

		QuadroMedido q;
		try
		{
			_cadaverNoPlayersDeTeste = true;
			vitima.RelogioDaMorte = NowMs() - 1;
			q = RodarUmQuadro(testemunha, arena);
		}
		finally { _cadaverNoPlayersDeTeste = false; }

		AfirmarTm("DEFEITO INJETADO: o quadro NAO chega ao fim", !q.ChegouAoFim, q.Estouro);
		AfirmarTm("DEFEITO INJETADO: ...o corpo no ar NAO cai (o relogio do voo perdeu o quadro)",
				  !q.ATestemunhaCaiu);
		AfirmarTm("DEFEITO INJETADO: ...o projetil NAO anda (o tiro congela no ar)",
				  !q.OProjetilAndou);
		AfirmarTm("DEFEITO INJETADO: ...a ferida NAO sincroniza (a zona nao recebe o corpo ferido)",
				  !q.AFeridaSincronizou);
		AfirmarTm("DEFEITO INJETADO: ...e o estouro e o do relato do dono (linha de console)",
				  q.Estouro.Contains("Collection was modified"), q.Estouro);

		// ---- E DESFEITO O DEFEITO, TUDO VOLTA A PASSAR ----------------------
		// A prova de que o que reprovou foi a CAUSA, e nao um estrago que ficou pra tras. Terceira
		// vitima porque a segunda ja esta morta -- e, com o defeito, ela ficou no meio do caminho.
		ServerPlayer? algoz3 = ForjarPraTique(host, "Algoz Limpo", new Vec2(-240, -220),
											  comDono: false, forjados);
		ServerPlayer? vitima3 = ForjarPraTique(host, "Vitima Limpa", new Vec2(-220, -220),
											   comDono: true, forjados);
		if (algoz3 == null || vitima3 == null) { AfirmarTm("sem palco pra a volta da familia 8", false); return; }

		ArmarOAlgoz(algoz3);
		MatarNoSoco(algoz3, vitima3);

		vitima3.RelogioDaMorte = NowMs() - 1;
		QuadroMedido limpo = RodarUmQuadro(testemunha, arena);
		AfirmarTm("DESFEITO O DEFEITO: o cadaver volta a nascer sem derrubar nada",
				  limpo.CadaveresNovos == 1, $"{limpo.CadaveresNovos} cadaveres novos");
		AfirmarOQuadroInteiro("quadro limpo depois da injecao", limpo);
	}
}
