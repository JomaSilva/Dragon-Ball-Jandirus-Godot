using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O ATUADOR: onde o <see cref="Comando"/> da IA vira o MESMO gesto que o pacote do jogador.
///
/// ============================ A REGRA DESTE ARQUIVO INTEIRO, E ELA E CURTA ============================
/// **O atuador nunca confere pre-condicao. Ele chama e deixa o jogo recusar.**
///
/// Nao ha um `if (PodeVoar(npc))` antes do <see cref="AlternarVoo"/>, nem um `if (temKi)` antes do
/// <see cref="Carregar"/>, nem um `if (Proxima() != null)` antes do <see cref="Transformar"/>.
/// Cada uma dessas funcoes JA sabe recusar -- ela recusa o jogador -- e escrever a conferencia aqui
/// criaria a segunda copia da regra: a copia que um dia discorda, e discorda EM FAVOR DA IA, que e
/// o pior jeito de errar.
///
/// O corolario e que este arquivo nao tem numero nenhum de jogo. Todo custo, todo prazo e toda
/// recusa moram na funcao chamada.
/// ================================================================================================
///
/// ============================ A LISTA DE PARIDADE ============================
/// Pra cada comando de corpo que o jogador manda (`ComandoDeCorpo`, GameServer.Clone.cs) existe um
/// campo no <see cref="Comando"/> e uma chamada aqui **pra a mesma funcao que o `switch` do
/// `Handle` chama**:
///
///   C2S.Action      -> Comando.Leve / .Pesado    -> Atacar(npc, golpe)
///   C2S.Guard       -> Comando.Guardar           -> npc.Combate.Guardar(bool)
///   C2S.Carregar    -> Comando.Carregar          -> Carregar(npc, bool)
///   C2S.Transformar -> Comando.SubirForma/.Descer-> Transformar(npc, subir)
///   C2S.InputState  -> Comando.Rumo/.Correndo/
///                      .QuerSubir/.QuerDescer    -> PassoDaIa (as MESMAS tres politicas do `Input`)
///   C2S.Habilidade  -> Comando.Habilidade        -> UsarHabilidade(npc, id)
///   C2S.Alvo        -> Comando.Marcar            -> Mirar(npc, id)
///   (o voo do jogador entra pelo verbo `Fly`)    -> AlternarVoo(npc)
///
/// As duas ultimas nasceram com o GANCHO DO ATAQUE DE LONGE, e hoje nenhum corpo do jogo as usa: o
/// arsenal de todo mundo esta vazio porque nenhuma tecnica portada viaja entre tiques. Elas existem
/// escritas, pelo funil certo e exercitadas na bancada, pra que o dia do beam seja "registrar uma
/// linha de dado" e nao "ensinar a IA a atirar". Ver `Core/Skills/TecnicasDeLonge.cs`.
///
/// Fica de fora, e de proposito: `C2S.Zanzoken` -- a IA nao pisca, e o respondedor de QTE do
/// ZanzoClash continua sendo uma decisao do dono (ou a IA ganha o lado dela, ou `TentarEmbate`
/// passa a exigir `Peer != null` nos dois lados).
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// `--diagia`: despeja no console TODA troca de plano de TODO corpo dirigido, com o motivo e os
	/// numeros que a produziram.
	///
	/// Desligado por padrao, e nao so por causa do ruido no console: com ele ligado o cerebro monta
	/// as frases de <see cref="Cerebro.Porque"/>, que sao strings interpoladas a 4 Hz por corpo.
	/// Mesmo desenho do `_diagGolpe`.
	/// </summary>
	private bool _diagIa;

	/// <summary>
	/// APLICA O COMANDO. A ordem importa e cada passo diz por que.
	/// </summary>
	private void AplicarComando(ServerPlayer npc, in Comando c, double dt)
	{
		// --- 1. PULSOS DE FORMA, ANTES DE TUDO --------------------------------
		// Transformar muda `MaxKi`, `Espeed` e o `SpeedStat`; feito depois do passo, o tique
		// inteiro andaria com a velocidade da forma velha. E o `Transformar` e quem cobra as
		// recusas todas -- inclusive o "tem a forma e nao a usa" do chefe (`AscendePorDecisao`).
		if (c.SubirForma) Transformar(npc, subir: true);
		else if (c.DescerForma) Transformar(npc, subir: false);

		// --- 2. O TOGGLE DO VOO ----------------------------------------------
		// `AlternarVoo` COBRA o Ki de decolagem (`Voo.CustoParaLigar`) e recusa quem nao sabe voar,
		// quem esta caido e quem nao tem folego. E a mesma funcao do verbo `Fly` do jogador.
		if (c.AlternarVoo) AlternarVoo(npc);

		// --- 3. AS TECLAS DE ALTURA ------------------------------------------
		// PEDIDOS continuos, escritos como o `Input` os escreve. Quem os obedece e o `TickDoVoo`, e
		// so pra quem esta voando -- afirmar o bit no chao nao levanta ninguem, aqui como la.
		npc.QuerSubir = c.QuerSubir;
		npc.QuerDescer = c.QuerDescer;

		// --- 4. A CARGA, SO NA TRANSICAO -------------------------------------
		// `Carregar(pl,true)` e idempotente por um `if (pl.Carregando) return`, mas `Carregar(pl,false)`
		// NAO e: ele chama `PararCarga`, que manda dois efeitos pro cliente. Chamado a 30 Hz seriam
		// 60 pacotes por segundo pra dizer que nada mudou.
		if (c.Carregar != npc.Carregando) Carregar(npc, c.Carregar);

		// --- 5. A GUARDA, SO NA TRANSICAO ------------------------------------
		// ============================ ISTO ERA UMA CHAMADA A 30 Hz ============================
		// `npc.Combate.Guardar(d.Guardar)` rodava todo tique e era inofensivo POR SORTE: o `if` de
		// `CombatState.Guardar` (`CombatState.cs:185`) so age na SUBIDA da guarda, entao o
		// `ContraPronto` nao era rearmado. Bastava alguem mexer naquele `if` -- pra consertar
		// qualquer outra coisa -- pra a IA passar a ter contra-ataque perfeito 30 vezes por segundo,
		// e ninguem ligaria o defeito a este arquivo.
		// ==================================================================================
		if (c.Guardar != npc.Combate.Bloqueando) npc.Combate.Guardar(c.Guardar);

		// --- 6. A MIRA, ANTES DE QUALQUER GOLPE ------------------------------
		// `Mirar` e a MESMA funcao do `C2S.Alvo` (ver `GameServer.Combat.cs`), com a mesma validacao
		// de zona e de existencia. Zero quer dizer "nao mexe" -- ver o campo no `Comando`.
		//
		// E ela vem antes do soco de proposito: o `Atacar` vira o corpo pro marcado antes de
		// arrancar, entao marcar depois de bater seria mirar pro golpe do tique QUE JA PASSOU.
		if (c.Marcar != 0 && c.Marcar != npc.AlvoId) Mirar(npc, c.Marcar);

		// --- 7. O PASSO ------------------------------------------------------
		PassoDaIa(npc, c, dt);

		// --- 8. A TECNICA ----------------------------------------------------
		// ============================ HOJE ESTA LINHA NUNCA RECEBE NADA ============================
		// O cerebro so preenche `Habilidade` na receita de atirar, e ela exige um arsenal que hoje e
		// vazio em todo corpo do jogo (`TecnicasDeLonge` esta vazia porque nenhuma tecnica portada
		// viaja). A linha existe porque e o CANAL, e ele e o mesmo do jogador: `UsarHabilidade` e
		// literalmente a funcao que o `case Protocol.C2S.Habilidade` chama.
		//
		// Como todo o resto deste arquivo, ela nao confere nada. Nao ha `if (SabeTecnica)` nem
		// `if (temKi)`: quem responde "voce nao sabe isso", "ainda se recompoe" e "isso pede pelo
		// menos N de energia" e a tecnica -- e essas tres frases sao as MESMAS que o jogador ouve.
		// ======================================================================================
		if (c.Habilidade is { Length: > 0 } tecnica) UsarHabilidade(npc, tecnica);

		// --- 9. O SOCO, POR ULTIMO -------------------------------------------
		// Depois do passo porque o `Atacar` faz o `Aproximar` a partir da posicao ATUAL: socar antes
		// de andar mediria a distancia do tique passado. E ele que cobra recarga, estamina e
		// derruba a guarda (`ca.Guardar(false)`) -- nada disso e repetido aqui.
		//
		// SOCO E TECNICA NAO SE EXCLUEM AQUI, e a falta do `else` e deliberada: escolher entre os
		// dois seria uma DECISAO, e decisao e do cerebro (que nunca manda os dois no mesmo tique --
		// a receita de atirar nao soca). Um `else` aqui esconderia um defeito de decisao pra sempre.
		if (c.Leve) Atacar(npc, Protocol.Golpe.Leve);
		else if (c.Pesado) Atacar(npc, Protocol.Golpe.Pesado);
	}

	/// <summary>
	/// O PASSO DA IA -- as MESMAS TRES POLITICAS do <see cref="Input"/>, na mesma ordem.
	///
	/// ============================ POR QUE NAO DA PRA CHAMAR "A FUNCAO DE PASSO DO JOGADOR" ============================
	/// Porque ela nao existe, e juntar as duas produziria uma funcao pior. O jogador vai por
	/// `MoveRules.ValidateStep` -- que **confere uma posicao que o cliente afirmou** -- e a IA vai
	/// por `MoveRules.Advance` -- que **gera a posicao**. Sao perguntas opostas (`MoveRules.cs:91`
	/// vs `:159`), e uni-las daria uma funcao com dois modos e um `if` no meio.
	///
	/// O que E comum, e e o que estava faltando, sao as tres POLITICAS que decidem COMO o passo
	/// acontece. As tres estao aqui, com a mesma chamada e o mesmo custo:
	///
	///   1. `MarchaDeVoo(pl, shift)`  -- o Superflight;
	///   2. `PodeCorrer(pl, dt)`      -- **e ele que COBRA** `MaxKi * 0,02` por segundo de corrida;
	///   3. o mapa nulo em altura      -- `AtravessandoCenario`, o `isflying` do original.
	/// ==========================================================================================================
	///
	/// ============================ DOIS DEFEITOS QUE MORAVAM AQUI ============================
	///   * O MAPA ERA SEMPRE PASSADO (`_catalogo?.Get(npc.Zone)?.Mapa`, direto). Um NPC voando a 15
	///     tiles de altura batia em paredes que o jogador na mesma altura atravessa -- e nao havia
	///     nada na tela explicando por que o boneco parou no ar.
	///   * A CORRIDA NAO EXISTIA. `Advance` tem `correndo = false` por padrao (`MoveRules.cs:92`) e
	///     a IA nunca passava o argumento. Ligar so o campo `npc.Correndo` (a tentacao obvia) seria
	///     PIOR: daria 60% de velocidade **de graca** -- literalmente o buraco que o comentario do
	///     `Input` diz existir pra cliente modificado -- ou, ao contrario, um corpo que a rede diz
	///     que corre e que anda devagar. Os tres passos ou nenhum.
	/// ====================================================================================
	/// </summary>
	private void PassoDaIa(ServerPlayer npc, in Comando c, double dt)
	{
		// O MESMO PORTAO DO JOGADOR: caido, carregando ou em embate, ninguem anda. Ver
		// `PodeMexerOCorpo` -- a funcao e uma so, e o `Input` a chama tambem.
		if (!PodeMexerOCorpo(npc)) { npc.Moving = false; npc.Correndo = false; npc.Ficha.dashing = false; return; }

		MarchaDeVoo(npc, c.Correndo);

		bool andando = c.Rumo.LengthSquared > 1e-6f;
		bool correndo = c.Correndo && andando && (npc.Voando || PodeCorrer(npc, (float)dt));

		if (!andando)
		{
			npc.Moving = false;
			npc.Correndo = false;
			npc.Ficha.dashing = false;
			return;
		}

		ZoneCollision? mapa = AtravessandoCenario(npc) ? null : MapaDaZonaOuCatalogo(npc.Zone);

		Vec2 antes = npc.Pos;
		npc.Pos = MoveRules.Advance(npc.Pos, c.Rumo, (float)dt, npc.SpeedStat, mapa, out _, correndo);
		npc.Moving = (npc.Pos - antes).LengthSquared > 0.01f;
		npc.Facing = MoveRules.FacingFrom(c.Rumo, npc.Facing);
		npc.Correndo = correndo;
		npc.Ficha.dashing = correndo;   // entra na conta de dano, igualzinho ao do jogador
	}

	/// <summary>
	/// ============================ ESTE CORPO PODE ANDAR AGORA? ============================
	/// As quatro condicoes que o portao do <see cref="Input"/> ja cobrava, extraidas pra que a IA
	/// obedeca as MESMAS -- e nao "as mesmas menos uma", que e o jeito classico de o NPC ficar op
	/// sem ninguem notar.
	///
	/// A que mais importa e o `Carregando`: reunir energia PRENDE o corpo, e sem esta linha o NPC
	/// andaria carregando -- exatamente o "op demais" que o dono cortou do jogador.
	///
	/// O que NAO esta aqui e a possessao (`SemAsRedeas`): ela nao e sobre o CORPO poder andar, e
	/// sim sobre QUEM manda nele. Pro jogador possuido ela e uma recusa; pra a IA ela e a licenca.
	/// Por isso o `Input` a pergunta separado.
	/// ==================================================================================
	/// </summary>
	private bool PodeMexerOCorpo(ServerPlayer pl) =>
		!pl.Ficha.dead && !pl.Ficha.KO && !pl.Carregando && !_emEmbate.ContainsKey(pl.Id);

	/// <summary>
	/// O QUE O JOGO RESPONDE QUE ESTE CORPO PODE FAZER. Lido a 1 Hz por corpo (ver
	/// <see cref="Cerebro.PrecisaLerCapacidades"/>) porque varre o catalogo de formas.
	///
	/// **Todo campo aqui e o retorno de uma funcao de recusa da producao.** Nao ha uma regra
	/// escrita nesta funcao -- ela so pergunta e anota. E o que faz uma recusa nova (uma skill, um
	/// cargo, um debuff) valer pra a IA no mesmo instante em que passa a valer pro jogador.
	/// </summary>
	private Capacidades LerCapacidades(ServerPlayer pl)
	{
		double hab = HabilidadeDeVoo(pl);
		double kiFrac = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : 1;

		// A ESCADA, PELO MESMO SELETOR DA TECLA C. `Proxima` avalia com `kiFracao: 1` -- ela diz que
		// degrau este corpo ALCANCA. A recusa REAL (com o Ki de agora) sai da segunda chamada, e e
		// ela que transforma `SemKi` em pre-condicao reparavel em vez de fracasso. Ver `Capacidades`.
		//
		// UM `Perfil` SO, e nao tres. Ele e `readonly record struct` (nao aloca), mas o `TemEscada`
		// que eu chamava antes monta um `HashSet` por chamada (`Catalogo.LinhasAbertas`) -- e ele e
		// REDUNDANTE aqui: sem linha aberta, `Proxima` ja devolve nulo, porque `Avaliar` recusa todo
		// degrau com `LinhaFechada`. Uma pergunta a menos com a mesma resposta.
		PerfilDeFormas perfil = Perfil(pl);
		FormaDef? proxima = pl.Forma.Proxima(pl.Ficha.BP, perfil);
		RecusaForma recusa = proxima == null
			? RecusaForma.JaEsta
			: pl.Forma.Avaliar(proxima.Id, pl.Ficha.BP, kiFrac,
							   pl.Ficha.KO || pl.Ficha.dead, perfil);

		return new Capacidades
		{
			PodeVoar = PodeVoar(pl),
			CustoDeDecolar = Voo.CustoParaLigar(hab, pl.VooRapido),
			CustoDoVooPorSegundo = Voo.CustoPorSegundo(hab, pl.VooRapido),
			SabeReunirKi = CargaDeKi.SabeReunir(pl.Ficha),
			HaDegrauAcima = proxima != null,
			RecusaDaForma = recusa,
			AscendePorDecisao = AscendePorDecisao(pl),
			TemComQueAparar = TemComQueAparar(pl),
			CustoDaGuarda = pl.Ficha.MaxKi * CombatKnobs.CustoKiDaGuarda,
			DeLonge = ArsenalDeLonge(pl),
		};
	}

	/// <summary>
	/// O QUE ESTE CORPO PODE ATIRAR DE LONGE. **Devolve vazio pra todo mundo, hoje.**
	///
	/// ============================ A PODA VEM ANTES DA VARREDURA, E ELA E O PONTO ============================
	/// A pergunta "quais das minhas skills sao ataque a distancia?" custa varrer o livro inteiro e,
	/// pra cada skill, a lista de verbs dela -- e o `SabeTecnica` ja faz exatamente isso e ja e o
	/// caminho caro do sistema de tecnicas. Pago 1 vez por segundo por corpo, tudo bem; mas hoje nao
	/// se paga NADA, porque a tabela de tecnicas de longe esta vazia e a primeira linha sai fora.
	///
	/// Esta e a resposta direta ao risco que este desenho carrega desde a camada 2: *a percepcao vai
	/// engordar, e cada campo novo e uma varredura*. Aqui a varredura nasce ja atras de um portao, e
	/// atras do relogio de 1 Hz -- e nao no caminho de 30 Hz onde ela nao apareceria em medicao
	/// nenhuma ate alguem cronometrar o quadro certo.
	/// ==================================================================================================
	///
	/// E O ARRAY SO NASCE SE HOUVER ALGO NELE: `Arsenal.Vazio` nao aloca, entao o tique de leitura de
	/// capacidades continua com o mesmo lixo por tique que a bancada ja afirma hoje.
	/// </summary>
	private Arsenal ArsenalDeLonge(ServerPlayer pl)
	{
		if (!TecnicasDeLonge.Alguma || _skills == null) return Arsenal.Vazio;

		List<Tiro>? achados = null;
		foreach (string verb in TecnicasDe(pl))
		{
			// A MESMA lista de verbs que o menu do cliente recebe e que o `SabeTecnica` confere --
			// ou seja, o arsenal da IA nao pode conter uma tecnica que o jogo recusaria por "voce
			// nao sabe isso". Se puder, e porque as duas leituras divergiram, e ai o defeito e uma
			// IA timida (ela ignora um golpe que tem), nunca uma IA que atira sem saber.
			if (TecnicasDeLonge.Get(verb) is not { } linha) continue;

			(achados ??= []).Add(new Tiro
			{
				Id = linha.Id,
				AlcanceMin = linha.AlcanceMinTiles * ZoneCollision.TileSize,
				AlcanceMax = linha.AlcanceMaxTiles * ZoneCollision.TileSize,
				TempoDeConjuracao = linha.TempoDeConjuracao,

				// O PRECO SAI DA FUNCAO DA PROPRIA TECNICA -- ver o cabecalho de `TecnicasDeLonge`.
				// Nenhum numero de preco e escrito aqui, e essa e a unica forma de a IA nao decidir
				// com uma tabela de custos paralela que envelhece.
				CustoDeKi = linha.CustoDeKi(pl.Ficha),

				PrecisaDeLinhaLivre = linha.PrecisaDeLinhaLivre,
				Precisao = linha.Precisao,
			});
		}
		return achados == null ? Arsenal.Vazio : new Arsenal([.. achados]);
	}

	/// <summary>
	/// SOBROU BRACO OU PERNA PRA APARAR? A MESMA pergunta que o `MeleeResolver.EscolherGuarda` faz
	/// no instante do golpe (`MeleeResolver.cs:95`: sem membro, a guarda cai sozinha).
	///
	/// Ela existe aqui pra a IA nao INSISTIR numa guarda que o resolvedor vai derrubar -- e nao pra
	/// substituir a checagem de la. Um NPC com os dois bracos quebrados que continuasse erguendo a
	/// guarda pagaria o Ki e nao apararia nada.
	/// </summary>
	private static bool TemComQueAparar(ServerPlayer pl)
	{
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
			if (!p.Aninhado && p.Zona is "bracos" or "pernas" && !p.Decepado && !p.Quebrado)
				return true;
		return false;
	}

	/// <summary>
	/// O MUNDO COMO ELE VE. Nada aqui e varredura nova: todo campo ja existia por outro motivo, e
	/// o alvo ja vem escolhido de fora (o <see cref="PresaDaFera"/>, que era quem ja escolhia).
	///
	/// A UNICA EXCECAO E A LINHA DE VISAO, e ela e paga so por quem tem o que atirar -- ver
	/// <see cref="LinhaDeVisaoLivre"/>. Com o jogo de hoje, ninguem tem: `quemAtira` e sempre falso
	/// e o metodo nem e chamado.
	/// </summary>
	private Percepcao LerPercepcao(ServerPlayer npc, ServerPlayer? alvo, Vec2 destino, bool quemAtira)
	{
		Fighter f = npc.Ficha;
		bool temAlvo = alvo != null;

		return new Percepcao
		{
			IdDoAlvo = alvo?.Id ?? 0,
			AlvoSeMovendo = alvo?.Moving ?? false,
			LinhaLivre = quemAtira && alvo != null && LinhaDeVisaoLivre(npc, alvo),

			Minha = npc.Pos,
			MinhaAltitude = npc.Altitude,
			EstouVoando = npc.Voando,
			VidaFrac = f.HP / 100.0,
			KiFrac = f.MaxKi > 0 ? f.Ki / f.MaxKi : 1,
			Ki = f.Ki,
			FolegoFrac = f.maxstamina > 0 ? f.stamina / f.maxstamina : 1,
			Caido = f.KO || f.dead,
			Atordoado = npc.Combate.Stun > 0,
			MeuPoder = Finito(f.expressedBP),

			// SEM ALVO, O DESTINO E UM PONTO (o vagar da fera). Ele entra como "alvo" no chao pra a
			// mesma conta de rumo servir aos dois casos -- foi sempre assim neste laco --, mas
			// `TemAlvo` continua falso, entao nenhuma decisao de luta se aplica a um ponto.
			TemAlvo = temAlvo,
			DoAlvo = alvo?.Pos ?? destino,
			AltitudeDoAlvo = alvo?.Altitude ?? 0f,
			AlvoVoando = alvo?.Voando ?? false,
			AlvoCaido = alvo is { } a && (a.Ficha.KO || a.Ficha.dead),
			VidaDoAlvo = (alvo?.Ficha.HP ?? 100) / 100.0,
			PoderDoAlvo = Finito(alvo?.Ficha.expressedBP ?? 0),
		};
	}

	/// <summary>
	/// TEM PAREDE ENTRE OS DOIS? A pergunta cara da percepcao, e por isso ela e feita SOB DEMANDA --
	/// so pra quem tem ataque de longe no arsenal, ou seja: hoje, ninguem.
	///
	/// ============================ ALTURA ABRE A LINHA, E ISSO NAO E ATALHO ============================
	/// Quem esta alto o bastante ja ATRAVESSA o cenario (`Voo.AtravessaCenario` -- e a mesma regra
	/// que o passo obedece, e que consertou o NPC batendo em paredes que o jogador voando cruza).
	/// Perguntar por colisao de chao pra um corpo que esta 15 tiles acima do muro daria "sem linha"
	/// pra um golpe que passaria por cima do muro tranquilamente -- seria a geometria do jogo
	/// respondendo uma coisa e a IA acreditando noutra.
	///
	/// Basta UM dos dois estar em altura de travessia: se eu estou em cima, atiro por cima; se ele
	/// esta em cima, o alvo esta exposto. O caso que sobra -- os dois no chao, com um muro no meio --
	/// e o unico em que a parede importa de verdade, e ai vale o `PathBlocked`, que e o mesmo
	/// amostrador que o movimento usa.
	/// ============================================================================================
	/// </summary>
	private bool LinhaDeVisaoLivre(ServerPlayer npc, ServerPlayer alvo)
	{
		if (AtravessandoCenario(npc) || AtravessandoCenario(alvo)) return true;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(npc.Zone);
		return mapa == null || !mapa.PathBlocked(npc.Pos, alvo.Pos);
	}

	/// <summary>
	/// ============================ O PODER PODE VIR NaN, E A CONTA INTEIRA APODRECE ============================
	/// `expressedBP` passa por caminhos que ja produziram `NaN` (o sigilo de BP mordeu a bancada de
	/// voo exatamente assim). Um `NaN` aqui contamina a `RazaoDePoder`, e comparacao com `NaN` e
	/// SEMPRE falsa -- entao o NPC simplesmente pararia de escalar, calado e pra sempre.
	///
	/// Zero e a resposta certa pra "nao sei": a razao de poder cai pra 1 (parelho) e a decisao passa
	/// a depender so da vida, que e um numero em que da pra confiar.
	/// ====================================================================================================
	/// </summary>
	private static double Finito(double x) => double.IsFinite(x) && x > 0 ? x : 0;
}
