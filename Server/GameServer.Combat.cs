using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// COMBATE, LADO DO SERVIDOR.
///
/// Aqui e a excecao declarada ao "cliente calcula, servidor valida": o golpe e resolvido
/// SO AQUI. A razao e que a resolucao sorteia (pontaria, membro atingido, critico) e duas
/// pontas sorteando nunca chegam ao mesmo resultado. O cliente pede "socar" e recebe o
/// relato pronto -- ele nem sabe o BP de quem apanhou.
///
/// A CADENCIA E A TRAVA ANTI-CHEAT. Mandar o pacote de soco mil vezes por segundo nao adianta:
/// o servidor so aceita quando a recarga do proprio lutador zerou, e a recarga sai do
/// `Eactspeed` dele. O cliente recebe o mesmo numero na ficha (`SheetState.SocoMs`) pra nao
/// tentar o que vai ser recusado.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// **FULANO BATEU EM SICRANO.** O funil unico de "quem me agrediu, e quando".
	///
	/// ============================ ERAM OITO ATRIBUICOES SOLTAS, E ELE PRECISAVA DE DUAS ============================
	/// `vitima.UltimoAgressor = a.Id` estava escrito em oito lugares (soco, contra-ataque, punho
	/// espiritual, tecnicas G3, dano espalhado, Zanzo Clash), e o Zenkai era o unico leitor. Com o
	/// cidadao pacifico entrou um SEGUNDO campo -- o prazo do rancor --, e "acrescentar a linha nova
	/// em sete dos oito lugares" e literalmente o erro que este port ja registra como o mais
	/// repetido dele.
	///
	/// Entao a atribuicao virou funcao. O que ela escreve continua sendo o que estava escrito; o que
	/// mudou e que o proximo campo de agressao entra em UM lugar, e que um caminho de dano novo que
	/// esqueca de chamar isto e visivel (o cidadao nao revida) em vez de silencioso.
	/// ========================================================================================================
	///
	/// O prazo e o `combat_tag_duration` do original (90 s) -- ver <see cref="Povoamento.SegundosDeRancor"/>.
	/// </summary>
	private void MarcarAgressao(ServerPlayer vitima, ServerPlayer agressor)
	{
		if (agressor.Id == vitima.Id) return;   // ninguem guarda rancor de si mesmo
		vitima.UltimoAgressor = agressor.Id;
		vitima.RancorAte = NowMs() + (long)(Jandirus.Core.Npc.Povoamento.SegundosDeRancor * 1000);

		// ============================ E BATERAM NUM CORPO LARGADO? ELE ACORDA ============================
		// *"se elas TE BATEREM vc ACORDA da meditacao pro corpo real"* / *"caso alguem te bata vc volta
		// pro corpo principal"* -- o pedido do dono, pros dois sistemas, numa linha so.
		//
		// AQUI E NAO NO DANO, e a escolha e do DM: la o gancho e o `refresh_combat_tag()` do
		// `mind_dummy` (`MindMeditate.dm:147-149`), o mesmo proc que esta funcao porta, e o autor
		// explicou por que -- *"so this fires no matter the outcome"*. Golpe APARADO ou ESQUIVADO
		// tambem acorda: quem encostou no seu corpo te trouxe de volta, tenha ferido ou nao.
		//
		// E e por isso que o mecanismo pegou os dois sistemas de uma vez sem uma linha em cada: este
		// e o funil unico de agressao do port, e o proprio cabecalho acima ja tinha previsto que "o
		// proximo campo de agressao entra em UM lugar". Ver `GameServer.CorpoLargado.cs`.
		// ==========================================================================================
		AcordarNoCorpo(vitima);
	}

	/// <summary>
	/// A skill que deixa imagem remanescente (`misc.dm:35`, a `Afterimage Technique`).
	///
	/// O SERVIDOR E QUEM CONSULTA porque a lista de skills do outro e ficha -- ninguem precisa
	/// saber o que o adversario aprendeu, so precisa VER o vulto. Ver `HitEvent.Zanzo`.
	/// </summary>
	private const string PathDoZanzoken = "/datum/skill/ki/Afterimage";

	/// <summary>Segundos de carencia de quem acabou de renascer (ver CombatState.Carencia).</summary>
	private const double SegundosDeCarencia = 6;

	/// <summary>
	/// ALCANCE DO ARRANQUE COM CORRIDA (SHIFT + ESPACO), em pixels. Cinco tiles.
	///
	/// O original aceitava ate 15 tiles, mas la o alvo era ESCOLHIDO (`get_me_a_target`) antes.
	/// Cinco tiles e o que da pra ler na tela como "vou naquele ali" sem o personagem se jogar
	/// em cima de quem passou no canto.
	/// </summary>
	private const float AlcanceDoDash = 160f;

	/// <summary>
	/// ALCANCE DO ARRANQUE **QUANDO HA ALVO MARCADO**, em pixels. QUINZE tiles -- o numero do original.
	///
	/// ============================ O CORTE DE CINCO TILES ERA PELA BUSCA, NAO PELO GESTO ============================
	/// A justificativa escrita no <see cref="AlcanceDoDash"/> logo acima diz por que ele foi cortado:
	/// *"O original aceitava ate 15 tiles, mas la o alvo era ESCOLHIDO (`get_me_a_target`) antes"* --
	/// o medo e o personagem *"se jogar em cima de quem passou no canto"*. Esse medo e real e continua
	/// valendo pra busca por CONE, que adivinha em quem voce quis bater.
	///
	/// Mas o port TEM o alvo escolhido: e o <see cref="Marcado"/>. Quem marcou alguem ja disse "vou
	/// naquele ali", e nao ha canto nenhum de onde alguem possa passar. Entao a condicao do DM se porta
	/// inteira, com o numero dele: `get_dist(src.loc,target.loc) < 15` (`attack_bck.dm:78`).
	///
	/// ============================ E E ISTO QUE FECHA O RELATO DO SOCO FORTE ============================
	/// **Todo golpe pesado que encosta chama o Impact sem sorteio** (`attack cmn.dm:110-118`: o leve
	/// depende de `prob`, o pesado cai direto no `else`), e a bancada mediu o resultado: com BP parelho
	/// o corpo voa **512 px em 0,8 s**, e mais ainda contra quem e mais forte. Depois do voo o inimigo
	/// esta a DEZESSEIS tiles -- fora dos cinco do arranque, fora dos 40 px do soco. Sobrava andar de
	/// volta enquanto o outro recarregava.
	///
	/// O DM nao tinha esse buraco por duas razoes, e esta e a primeira: la o arranque alcanca 15 tiles.
	/// A segunda e o **reencaixe** (`attack cmn.dm:219-222`: depois do teleporte de zanzo, `if(M.target
	/// == src) M.Move(get_step(src,src.dir))` -- ele cola o alvo de volta ao lado dele), e essa NAO foi
	/// portada de proposito: mover o corpo de quem apanhou pra perto de quem bate e o oposto do que o
	/// arremesso acabou de fazer, e daria dois sistemas empurrando o mesmo corpo em sentidos opostos.
	///
	/// SO NO ARRANQUE LONGO (SHIFT + ESPACO). O passo curto continua sendo passo curto: o dono ja
	/// reclamou uma vez de investida grande demais ("achei a distancia minima pro tp pro soco mt
	/// grande"), e um soco leve que atravessa quinze tiles seria exatamente isso de volta.
	/// ================================================================================================
	/// </summary>
	private const float AlcanceDoDashMarcado = 480f;

	/// <summary>
	/// ALCANCE DO ARRANQUE SEM CORRIDA (so ESPACO), em pixels. Dois tiles e meio.
	///
	/// Socar alguem que esta a um passo nao deveria ser um soco no ar: o personagem da o passo
	/// e acerta. Curto de proposito -- e ajuste de posicao, nao investida.
	/// </summary>
	private const float AlcanceDoPasso = 80f;

	/// <summary>
	/// ONDE O ARRANQUE PARA: distancia final do alvo, em pixels. Um TILE.
	///
	/// E a mesma distancia que dois mobs vizinhos tinham no BYOND (turfs adjacentes) e cabe
	/// dentro do alcance do soco. Parar mais perto poe um sprite DENTRO do outro -- os dois
	/// tem 32 px de largura, e a tela vira dois bonecos empilhados.
	/// </summary>
	private const float DistanciaDeParada = 32f;

	/// <summary>
	/// QUANTO A INVESTIDA PRECISA ANDAR pra valer a pena existir. MEIO tile.
	///
	/// Comecou em um tile (o pedido original) e o dono jogou: "achei a distancia minima pro tp pro
	/// soco mt grande, coloque a metade da atual". Meio tile ainda mata a investida de 8 px que era
	/// o defeito -- rasgo, som e miragem por um passo que ninguem via -- sem exigir que o alvo esteja
	/// longe pra o personagem arrancar.
	///
	/// E deslocamento, nao distancia ate o alvo: o arranque para a <see cref="DistanciaDeParada"/>
	/// do outro, entao o que ele efetivamente anda e `dist - 32`. Sem este piso, estar a 40 px do
	/// alvo disparava uma investida de 8 px -- com rasgo, som e miragem por um passo que ninguem viu.
	/// </summary>
	private const float DeslocamentoMinimo = 16f;

	/// <summary>Fracao do Ki maximo que cada arranque custa (`15 * BaseDrain` no original).</summary>
	private const double CustoDashKi = 0.05;

	/// <summary>
	/// Espera minima entre dois arranques, em milissegundos.
	///
	/// No original a trava equivalente e o `post_attack`, que sai com `prob(35)` por volta de
	/// laco -- media de 1/0,35 = 2,86 voltas, ou ~480 ms. 500 e o numero mais proximo disso
	/// que nao depende de sorteio.
	/// </summary>
	private const long RecargaDashMs = 500;

	/// <summary>
	/// ============================ O EIXO DA CURA DESTA RACA, TIRADO DO GENOMA ============================
	/// Isto era uma lista de quatro nomes cravada no codigo
	/// (`raca is "Namekian" or "Majin" or "BioAndroid" or "Shapeshifter"`), e ela **errava nos dois
	/// sentidos** contra o `assign_regen()` do DM:
	///
	///   * **Namekuseijin** estava DENTRO -- mas o rework do original lhe da `DeathRegen = 0` num
	///     `return` proprio (`Genetic_Datum.dm:253-258`), ou seja perder um nucleo o MATA. Quem
	///     devolve membro pra ele e a skill ATIVA, e o comentario do DM diz isso com todas as letras;
	///   * **Shapeshifter** estava DENTRO -- `Regeneration = 2` -> `DeathRegen = 0`;
	///   * **Kai, Meta, Tsujin e Yardrat** estavam FORA -- os quatro tem `Regeneration = 10` ->
	///     `DeathRegen = 1` -> `canheallopped`.
	///
	/// E a lista nao dizia nada sobre a pergunta que o dono fez, que e OUTRA: quem cura EM COMBATE.
	/// Essa e o `fastRegen`, e ela tem um corte proprio (`Regeneration >= 5`). Com o perfil saindo do
	/// `races.json` -- que e o `misc_stats["Regeneration"]` extraido do DM -- as duas perguntas param
	/// de ser uma lista pra manter e passam a ser o dado.
	///
	/// **RACA SEM PROTO CAI NO COMUM** (Humano, `Regeneration = 1`): sem cura em combate, sem membro
	/// de volta. E o default certo -- errar pro lado do privilegio daria regeneracao de Majin a
	/// qualquer corpo que o `races.json` nao conheca.
	/// ================================================================================================
	/// </summary>
	private PerfilDeRegen EixoDeRegen(string raca, Jandirus.Core.Stats.Fighter? ficha = null)
	{
		// Half-Saiyan nao tem proto proprio -- o corpo vem do Saiyajin. Mesma ponte do `Birth.Nascer`.
		string proto = raca == "Halfbreed" ? "Saiyan" : raca;
		double reg = _racas?.Get(proto)?.MiscStat("Regeneration") ?? 1;
		// A PARTE DAS SKILLS SOMA POR CIMA DA RACA -- `genome.add_to_stat("Regeneration", 10)` da
		// Doll Regeneration (spirit-doll.dm:33), `5` da Regenerate (alien.dm:65), `1` da Grace de
		// Ricardo (Bodybuilding.dm:254). O `assign_regen()` do DM relê o genoma inteiro; aqui o
		// `AplicarEfeitos` chama isto de novo e reescreve o perfil do corpo.
		if (ficha != null) reg += ficha.RegenerationDeSkill;
		return PerfilDeRegen.De(raca, reg);
	}

	/// <summary>
	/// Quem nasce com rabo. Saiyajin puro e mesticos.
	///
	/// DESVIO CONSCIENTE: no original SO o Saiyajin puro recebia o rabo, e nao por decisao de
	/// desenho -- a rede de seguranca que garantia o membro testava `Race == "Saiyan"` e o
	/// genoma de Half-Saiyan e IRMAO, nao subtipo, do de Saiyan. Ou seja, o mestico ficava
	/// sem rabo por acidente de tipo. Gohan tinha rabo; aqui o mestico tambem tem.
	/// </summary>
	private static bool TemRabo(string raca) => raca is "Saiyan" or "Halfbreed";

	/// <summary>Monta o corpo de quem acabou de entrar, e devolve a vida ao estado salvo.</summary>
	// NAO E `static`: ele instala o gancho de negar morte, que precisa da instancia do servidor
	// (a explosao varre a zona e manda efeito). Ver `CombatState.NegarMorte`.
	private void PrepararCombate(ServerPlayer pl, CharacterSave? save)
	{
		pl.Combate = new CombatState(pl.Ficha, TemRabo(pl.Race), EixoDeRegen(pl.Race, pl.Ficha));

		// QUEM PODE BARRAR A MORTE DESTE CORPO. Instalado UMA vez, aqui, porque `Morrer()` e a porta
		// unica -- ver `CombatState.NegarMorte`. Sao DOIS agora, e a ORDEM E A DO DM:
		//
		//   1. o BIO-ANDROIDE despertando o SSJ2 -- `Death.dm:10-12`, o **primeiro** `if` do `Death()`,
		//      antes de tudo. Ele nao e um seguro: e a unica forma de aquela transformacao existir,
		//      acontece **uma vez na vida** e exige a forma perfeita + SSJ1 dominado + poder. Perder
		//      essa chance porque uma aura negou o golpe antes seria perde-la pra sempre;
		//   2. a AURA OF DESTRUCTION (`ue_deathsave_try`), que e o seguro de verdade e se gasta.
		//
		// `||` E CURTO-CIRCUITO, e e por isso que a ordem basta: quem despertou o SSJ2 nao chega a
		// gastar a aura, e quem nao e bio cai direto no segundo como sempre caiu.
		pl.Combate.NegarMorte = _ => DespertarSsj2DoBio(pl) || TentarNegarMorte(pl);

		// E QUEM OUVE A MORTE QUE ACONTECEU -- o outro lado da mesma porta. E ele que marca o prazo
		// do cadaver e, no fim dele, a viagem pro Outro Mundo. Ver `GameServer.Alem.cs`.
		pl.Combate.AoMorrer = _ => AMorteAconteceu(pl);

		// E QUEM RESPONDE "ESTE CORPO ESTA EM CINEMATICA AGORA?" -- o escudo da transformacao.
		//
		// **A RESPOSTA JA EXISTIA E E ESTA.** `EmCena` (`GameServer.Formas.cs`) le o `CenaSegundos`,
		// que o `MarcarCena` escreve dentro do `AnunciarForma` -- o funil unico de toda troca de forma
		// -- e que o `TickDaForma` abate. Escrever um segundo "estou transformando" aqui seria criar
		// duas verdades que divergem no dia em que alguem mexer numa: o portao de MOVIMENTO da cena ja
		// pergunta a esta, e o dreno de Ki, a regeneracao e o custo do voo tambem.
		//
		// **DERIVADO, NAO GUARDADO**, e e o item que o dono cobrou: a imunidade morre junto com a
		// cinematica em TODO jeito de ela acabar -- o prazo vencendo, o nocaute no meio dela (que
		// reverte a forma e remarca a cena da base em ZERO), a morte, ou a volta pra base. Nao ha bit
		// pra apagar, entao nao ha bit pra esquecer.
		//
		// **INSTALADO AQUI VALE PRO NPC IGUAL**: `PorNoMundo` monta corpo sem dono por este mesmo
		// `PrepararCombate`. Nao ha crivo por tipo de corpo em lugar nenhum desta regra.
		pl.Combate.EmCinematica = () => EmCena(pl);

		// E QUEM RESPONDE "ESTE CORPO ESTA SENDO ARREMESSADO AGORA?" -- o `canfight -= 1` do efeito de
		// knockback do DM (`Movement Effects.dm:40`), lido pelo `testAttack()` (`attack_bck.dm:175`).
		//
		// A RESPOSTA JA EXISTIA E E ESTA: `TiquesDeVoo` e escrito so pelo `Arremessar` e abatido so pelo
		// `TickDoEmpurrao` -- o mesmo campo que ja deita o corpo (`ServerPlayer.Deitado`) e que ja faz o
		// cliente parar de integrar input. Ver `CombatState.SendoArremessado`.
		pl.Combate.SendoArremessado = () => pl.TiquesDeVoo > 0;

		// A vida dos membros PERSISTE entre sessoes: deslogar com o braco quebrado nao cura.
		// (Deslogar com o corpo DECEPADO tambem nao -- isso e coisa de regeneracao ou de morte.)
		if (save?.Membros is { Count: > 0 })
			foreach (BodyPart p in pl.Combate.Corpo.Partes)
				if (save.Membros.TryGetValue(p.Nome, out double[]? v) && v.Length >= 2)
				{
					p.Vida = Math.Clamp(v[0], 0, p.VidaMax);
					p.Decepado = v[1] > 0.5;
				}

		pl.Combate.SincronizarVida();
		AjustarGanhoDoRabo(pl);

		// ============================ QUEM LOGA MORTO REARMA O RELOGIO ONDE PAROU ============================
		// O gancho `AoMorrer` nao dispara aqui: esta pessoa nao morreu agora, ela ja estava morta
		// quando o save foi gravado. O prazo tem que ser rearmado a mao, e **qual** prazo sai do
		// LUGAR onde ela acordou -- que e a mesma pergunta que o `PassoDaMorte` faz.
		//
		// Deslogar no Outro Mundo e voltar la e o comportamento do DM (`SpawnPoints.dm:170-171`: quem
		// entra morto cai no checkpoint do alem, nao no berco), e ele sai de graca porque o save
		// guarda a zona. O que faltava era o cronometro: sem esta linha o morto logaria com relogio
		// zerado e viajaria (ou renasceria) no primeiro tique.
		// ================================================================================================
		if (pl.Ficha.dead)
		{
			// ============================ UM PALPITE SO, ALIMENTANDO AS DUAS COISAS ============================
			// O prazo e a AUREOLA respondem a mesma pergunta ("em que etapa da morte esta este corpo?"),
			// e aqui -- e SO aqui -- ela e respondida pelo lugar onde a pessoa acordou, porque nao ha
			// viagem em curso pra observar: o save guarda a zona e nao a etapa.
			//
			// **A AUREOLA NAO PODE SER UM SEGUNDO PALPITE.** Ela sai do MESMO ternario: quem deslogou no
			// alem ja tinha viajado e volta com aureola; quem deslogou caido no mundo dos vivos ainda e
			// o cadaver, e volta sem. No dia em que o `KeepsBody` for portado (o morto que ANDA no mundo
			// dos vivos, `OtherworldRankSkills.dm:195-202`), e esta linha que fica errada -- e ela erra
			// as duas coisas juntas, entao conserta-la conserta as duas. Ver `Alem.TemAureola`.
			// ==============================================================================================
			bool jaViajou = Alem.EhOAlem(pl.Zone);
			pl.MorteJaViajou = jaViajou;
			pl.RelogioDaMorte = NowMs() + (jaViajou ? Alem.MsNoAlem : Alem.MsNoChao);
			return;
		}

		// NOCAUTE NAO ATRAVESSA O LOGOUT DE GRACA. O `KO` mora na ficha (que e salva) mas o
		// cronometro que faz levantar mora no estado de combate (que nao e) -- entrar com
		// KO=true e cronometro zerado deixaria o personagem no chao PARA SEMPRE. Quem volta
		// caido recomeca a contagem; quem volta com um nucleo abaixo do limiar cai de novo.
		//
		// E O TIPO DO NOCAUTE SAI DO CORPO, e nao de um palpite: quem volta com um nucleo abaixo da
		// linha esta no COMA de `Injuries.dm:283` e levanta quando o nucleo subir; quem volta so com
		// a bandeira `KO` ligada (o nucleo ja sarou offline) tem um nocaute cronometrado comum. Ver
		// `CombatState.NocautePorVital`.
		if (pl.Ficha.KO || pl.Combate.Corpo.DeveNocautear())
			pl.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: pl.Combate.Corpo.DeveNocautear());
	}

	/// <summary>
	/// O RABO NO RITMO DE TREINO. Com rabo, o Saiyajin ganha METADE (`tailgain = 0.5`); sem
	/// ele, 1,25 -- ou seja, perder o rabo faz treinar 2,5x mais rapido.
	///
	/// E do original (`statsaiyan.dm:267-290`) e nao e detalhe: e o motivo mecanico de o rabo
	/// ser uma escolha e nao so um enfeite. Quem tem rabo tem Oozaru e cresce devagar; quem
	/// perde, cresce rapido e nunca mais vira macaco.
	/// </summary>
	private static void AjustarGanhoDoRabo(ServerPlayer pl)
	{
		BodyPart? rabo = pl.Combate.Corpo.Achar("Rabo");
		if (rabo == null) { pl.Ficha.tailgain = 1; return; }   // raca sem rabo: neutro
		pl.Ficha.tailgain = rabo.Decepado ? 1.25 : 0.5;
	}

	/// <summary>Fotografa o corpo pro savefile: vida e "esta decepado" por membro.</summary>
	public static Dictionary<string, double[]> FotografarCorpo(CombatState c)
	{
		var d = new Dictionary<string, double[]>(c.Corpo.Partes.Count);
		foreach (BodyPart p in c.Corpo.Partes) d[p.Nome] = [p.Vida, p.Decepado ? 1 : 0];
		return d;
	}

	// =====================================================================
	// O GOLPE
	// =====================================================================
	private void Atacar(ServerPlayer a, Protocol.Golpe golpe)
	{
		CombatState ca = a.Combate;
		if (!ca.PodeAtacar()) { ExplicarPorQueNaoSai(a); return; }   // morto, caido, atordoado, arremessado ou em recarga

		// ============================ COM ALGUEM NA MAO, O SOCO VIRA ESTRANGULAMENTO ============================
		// `attack_bck.dm:43-57`: no original o `Attack()` com um `grabbee` de pe **nao soca** -- ele
		// alterna o afogamento. E o que da conteudo ao "segurar": sem isso, o modo 1 do agarrao seria
		// so uma sala de espera ate arremessar.
		//
		// AQUI NO TOPO porque e onde ele esta la, e porque tudo o que vem abaixo (a virada pro alvo
		// marcado, a investida, a recarga, o consumo do combo) e do SOCO, e este gesto nao e um soco.
		// Ver `GameServer.Agarrao.cs`.
		// ====================================================================================================
		if (EstrangularEmVezDeSocar(a)) return;

		// ============================ ALVO MARCADO MANDA VIRAR -- E ESTE E O UNICO LUGAR ============================
		// E o que faz "seus ataques serem focados nele" valer mesmo com o alvo pelas costas: o
		// personagem se volta pra ele ANTES de arrancar. Sem isso, marcar alguem atras de voce e
		// apertar espaco daria um soco no ar na direcao errada.
		//
		// **ESTA LINHA E A REGRA DO "NINGUEM SOCA DE COSTAS", E ELA VALE PROS DOIS LADOS.** O DM nao
		// EXIGE encarar pra bater -- ele CORRIGE, e a correcao mora DENTRO do ataque
		// (`commonAttackProcs`, `attack cmn.dm:158`: `dir = get_dir(loc,M.loc)`). O caminho do GOLPE
		// nao tem recusa nenhuma; o caminho do PASSO tem sete (`PodeMexerOCorpo`). Pendurar a virada
		// no passo -- que foi o que o port fez primeiro, com o `Comando.Olhar` -- deixa de fora
		// justamente quem esta paralisado, enraizado por Ki ou prensado pela gravidade: estados que
		// recusam o passo e NAO recusam o soco (`CombatState.PodeAtacar()`).
		//
		// A IA nao tinha um segundo caminho: ela passou a MARCAR quem esta enfrentando (o `target`
		// do DM), no `Cerebro.Montar`, e cai nesta mesma linha. Nao ha `if` de NPC aqui e nao ha
		// virada escrita no atuador da IA -- se um dia esta regra mudar, muda pros dois de uma vez.
		//
		// O JOGADOR NAO GANHOU NEM PERDEU NADA: quem nao marca ninguem continua socando pra onde
		// esta olhando, e continua podendo socar o ar de costas pro inimigo -- que e o comportamento
		// do DM (o `turn_towards` do `compileRangeMobList`, `CombatMovement.dm:3`, esta morto no
		// original: `!target in L` e lido como `(!target) in L`, que e sempre falso).
		// =========================================================================================================
		// ============================ MENOS COM UM RAIO NA MAO -- O `turnlock` DO ORIGINAL ============================
		// Nao e uma segunda regra de virar: e a UNICA regra de virar, com a unica recusa que ela ainda
		// nao tinha. Todas as outras ja moram no `PodeMexerOCorpo`, e por ele o corpo enraizado por Ki
		// ja nao gira pelo teclado nem pelo atuador da IA -- o `Input` e o `PassoDaIa` recusam o pacote
		// INTEIRO (olhar junto com passo) antes de escrever `Facing`. O caminho do GOLPE e o unico que
		// passa por fora, porque ele "nao tem recusa nenhuma" (o paragrafo acima explica por que).
		//
		// O DM tem a trava com todas as letras -- `turnlock = 1` durante o `beamturndelay`
		// (`beams.dm:75-78`), lido pelas oito linhas `if(!turnlock) src.dir = ...` do
		// `keybind enabler.dm:15-43`, com o comentario do proprio jogo: *"keeps you from spinning in
		// circles with a beam going"*.
		//
		// ---- E AQUI ELA E MAIS DURA QUE LA, POR UMA DIFERENCA REAL DO PORTE ----
		// No original o raio SE REAPONTA: `A.dir = src.dir` e reescrito a cada ciclo do `ShootBeam`
		// (`beams.dm:136`), entao virar o corpo vira o feixe junto e os dois nunca discordam -- a trava
		// de la so encarece o giro (`round(Eactspeed,1)/2` tiques), nao o proibe. Neste port o `Rumo`
		// do projetil e cravado UMA vez no `Disparar` e nunca reescrito: um giro no meio do raio poria
		// a pose apontando pra um lado e o feixe saindo pro outro.
		//
		// Ou seja a trava e o que faz "a pose vira pra direcao do tiro" ser verdade **de graca**: com
		// ela, `Facing` continua sendo a direcao com que o `Disparar` mirou, e a maquina de direcao que
		// ja existe desenha o `blast_<dir>` certo sem nenhum campo de direcao no fio.
		// ==========================================================================================================
		if (!EnraizadoPorKi(a.Id) && Marcado(a) is { } mira)
			a.Facing = MoveRules.FacingFrom(mira.Pos - a.Pos, a.Facing);

		// APROXIMAR VEM ANTES DE SOCAR, e e assim no original: o `Attack()` fecha a distancia e
		// SO ENTAO chama o `MeleeAttack`. E o que faz o combate parecer Dragon Ball em vez de
		// dois bonecos se cutucando -- voce aponta pra alguem e o personagem VAI.
		bool investiu = Aproximar(a, longo: golpe == Protocol.Golpe.Pesado);

		// A IMAGEM REMANESCENTE nasce AQUI, junto da investida, e nao no relato do golpe: ela
		// marca o lugar de ONDE o corpo saiu. Decidida uma vez e carregada nos dois anuncios
		// (acertou e errou) -- errar uma investida deixa vulto igual, porque o vulto e do
		// deslocamento e nao do soco.
		// SO HA MIRAGEM SE HOUVE DESLOCAMENTO. O dono viu o contrario: "se eu usar shift + espaco
		// mesmo sem ninguem perto e eu tenho after image, eu crio uma miragem no meu lugar mesmo
		// nao me movendo". O vulto e do Zanzoken -- do corpo que saiu rapido demais pra vista
		// acompanhar --, entao sem deslocamento nao ha nada que a vista tenha perdido.
		bool zanzo = investiu && a.Livro?.Sabe(PathDoZanzoken) == true;

		// A MIRAGEM VIAJA COM A ORIGEM, num pacote so pros dois casos.
		//
		// Antes o vulto do dash saia do bit `HitEvent.Zanzo`, que NAO carrega posicao -- e o cliente
		// que assistia caia em `corpo.GlobalPosition`, ou seja, desenhava a miragem onde o corpo JA
		// tinha chegado. Quem executava via no lugar certo (ele guarda `_deOndeSai`) e quem assistia
		// via no lugar errado: era esse o "after image dessincronizado".
		//
		// O `S2C.Zanzo` do teleporte ja resolve isso -- ele leva o ponto de PARTIDA. Usar o mesmo
		// canal aqui deixa UM caminho pra miragem em vez de dois que precisam concordar.
		//
		// ============================ E AGORA O ANUNCIO E DA INVESTIDA, NAO DA SKILL ============================
		// O portao era `if (zanzo)` -- ou seja, quem nao sabia a Afterimage arrancava e a zona INTEIRA
		// nao ficava sabendo de nada. Isso estava certo enquanto o unico desenho deste pacote era a
		// miragem (que e da skill); parou de estar quando o BORRAO do deslocamento passou a sair daqui.
		//
		// Borrao nao e tecnica: e o corpo ter passado por ali. Quem responde "houve deslocamento?" e o
		// `Aproximar`, e a resposta dele e o `investiu` -- entao e ele que abre o portao, e a skill vira
		// so o parametro que decide se a MIRAGEM nasce por cima. Um funil, duas camadas.
		//
		// **E POR ISSO QUE NAO HA `if` DE NPC EM LUGAR NENHUM DESTE CONSERTO**: NPC, jogador e corpo
		// possuido (a fera do Oozaru, a furia lendaria) chegam aqui pela mesma linha 366, e saem daqui
		// com o mesmo pacote. Ver `AnunciarZanzo` pro custo em bytes.
		// ======================================================================================================
		// ============================ O INTERRUPTOR DO DEFEITO INJETADO ============================
		// FALSO EM JOGO, SEMPRE -- so a bancada `--borraoteste` o liga, e o que ele reproduz e
		// EXATAMENTE o portao antigo: `if (zanzo)`, ou seja, so quem sabia a Afterimage anunciava. Era
		// esse `if` o defeito inteiro do relato 1 do dono, e um `if` que ninguem consegue por de volta
		// numa bancada e um `if` que a bancada nao sabe estar consertado.
		//
		// Ele mora AQUI e nao dentro do arquivo de teste pela mesma razao escrita no `_dcGradeCega`
		// (`GameServer.Corpos.cs`): o `Mutacao` exige que o criterio seja o MESMO objeto com e sem o
		// defeito. Uma copia da regra escrita na bancada mediria a copia concordando consigo mesma --
		// e este projeto ja catalogou esse cego ("a bancada mede INTENCAO"). Custa uma comparacao de
		// bool por golpe que investiu.
		// ==========================================================================================
		if (_borraoSoComSkill ? zanzo : investiu) AnunciarZanzo(a, a.SaiuDe, vulto: zanzo);

		double tipo = Protocol.PesoDoGolpe(golpe);
		double espera = CombatMath.Cadencia(a.Ficha, tipo);
		ca.Recarga = espera;
		a.AtaqueAte = NowMs() + (long)(espera * 1000);

		// bater com a guarda erguida nao existe: o braco que soca e o que estava aparando
		ca.Guardar(false);

		ServerPlayer? alvo = AlvoNaFrente(a);

		// ZANZO CLASH: os dois se acertaram no mesmo instante? Entao este soco nao acontece -- ele
		// VIRA o embate. Conferido depois de achar o alvo (precisa saber em quem eu ia bater) e
		// antes de resolver (senao o dano sairia alem do embate).
		if (alvo != null && TentarEmbate(a, alvo)) return;

		if (alvo == null)
		{
			// SOCAR O AR AINDA TREINA. E o que o BYOND fazia e o que faz o novato progredir
			// sozinho num canto do mapa -- so que sem o multiplicador de lutar contra alguem.
			a.Ficha.AttackGain(_rng);
			ca.ZerarCombo();
			if (_diagGolpe) ExplicarSocoNoAr(a, investiu);

			// E SE O "AR" FOR ALGUEM TRANSFORMANDO, QUEM SOCOU FICA SABENDO. Ver
			// `AvisarQueOAlvoEstaEmCena`: sem isto o escudo da cinematica se parece com deteccao de
			// acerto quebrada, e o jogador reporta como bug em vez de entender a regra.
			AvisarQueOAlvoEstaEmCena(a);

			// MAS NEM SEMPRE E AR. Antes daqui o golpe sem alvo simplesmente acabava, e socar uma
			// parede de proposito nao fazia nada -- so o corpo ARREMESSADO derrubava cenario. No
			// original quem soca cenario o quebra (`attack_proc.dm:95-112`), e e assim que se abre
			// caminho numa casa ou se derruba uma arvore sem depender de levar um golpe antes.
			//
			// O NIVEL VAI JUNTO porque ele e o que escolhe o baque: um soco leve na parede soa
			// pequeno e um pesado soa grande, igual a bater num corpo. Sem combo -- nao ha combo
			// contra cenario.
			if (SocarCenario(a, (int)Math.Min(3, tipo))) return;

			AnunciarSocoNoAr(a, zanzo, investiu);
			return;
		}

		// ============================ O UNICO SENTIDO QUE A IA TEM DO OPONENTE ============================
		// Chamado ANTES de resolver, e com o golpe que vai ACONTECER -- acertando, sendo aparado ou
		// errando, tanto faz: o que um lutador ve e o GESTO, e nao o resultado. A partir daqui o
		// cerebro estima a cadencia e prepara a guarda pro proximo (`Cerebro.ViuUmGolpe`).
		//
		// **E POR ISSO QUE ELE NAO LE O `alvo.Combate.Recarga`.** O servidor tem na mao o instante
		// exato do proximo soco de qualquer um -- daria uma IA que apara SEMPRE, impossivel de
		// enganar e sem nada pra o jogador descobrir. Aqui ela aprende apanhando, com atraso
		// (`TempoDeReacao`) e com erro, e o jeito de vencê-la e quebrar o proprio ritmo.
		//
		// Vale pro clone da mente, pro NPC e pro corpo possuido -- e nao vale pra jogador nenhum,
		// porque `Cerebro` nulo e a definicao de "quem dirige e o dono".
		alvo.Cerebro?.ViuUmGolpe();

		CombatState cd = alvo.Combate;
		double angulo = MeleeArea.AnguloDeChegada(alvo.Pos, alvo.Facing, a.Pos);

		// O NIVEL DO BAQUE sai daqui, ANTES de resolver: `min(3, tipo + min(1, combo))` do
		// original. Um soco leve isolado soa pequeno; o mesmo soco no meio de uma sequencia
		// soa medio; o pesado soa grande. E o que faz a briga esquentar no ouvido.
		int nivel = (int)Math.Min(3, tipo + Math.Min(1, ca.Combo));

		// O ESTILO ENTRA COMO DANO PLANO, nao como multiplicador -- e o `dmg += compareStyles(M)`
		// do original. Teto de 10: estilo desempata luta parelha, nao vence luta perdida.
		GolpeResultado r = MeleeResolver.Resolver(ca, cd, angulo, _rng, tipo, DanoDeEstilo(a, alvo));
		MarcarAgressao(alvo, a);
		if (_diagGolpe) ExplicarGolpe(a, alvo, r, angulo, investiu);

		// ============================ OS GANCHOS DAS DISCIPLINAS DIVINAS ============================
		// O `ChanceEsquiva` que o resolvedor consultou foi escrito pelo Ultra Instinto, entao uma
		// esquiva com a passiva ligada E uma esquiva do instinto -- e ela paga maestria e empilha o
		// bonus. Sem esta linha a Autonomous Evasion funcionaria e nunca ensinaria nada.
		//
		// LE `EsquivaAtiva`, E NAO O DESFECHO. Desde que a pontaria que falha voltou a ser
		// `Desfecho.Esquivou` (que e o `hit = 0` do `CombatMovement.dm:192`, e sempre foi), o desfecho
		// sozinho diria "instinto" toda vez que alguem rapido saisse da frente -- e o Ultra Instinto
		// ganharia maestria de graca, sem estar ligado. O DM ja separa os dois: a esquiva por
		// velocidade e `:192` e a do instinto e o `ui_try_evade` de `:212`.
		if (r.EsquivaAtiva) AoEsquivarPorInstinto(alvo);

		// E a AURA OF DESTRUCTION filtra o que passou: reduz o dano, devolve recuo em quem bateu e
		// guarda o golpe pra Destruction Explosion. Fica DEPOIS do resolvedor porque o corpo ja foi
		// ferido la dentro -- ver `AuraFiltraDano`, que desconta a diferenca.
		if (r.Dano > 0) AplicarAuraDepoisDoGolpe(alvo, a, r.Dano, ehKi: false);
		// ==========================================================================================

		// A DIRECAO DO SOCO FICA GUARDADA NO ALVO. E ela que deita o corpo se este golpe derrubar --
		// no ar e no chao, o mesmo angulo. Ver `ServerPlayer.DirecaoDeitado`.
		//
		// O VETOR E O MESMO DO ARREMESSO (`MeleeArea.Frente` do atacante), e nao a diferenca entre
		// as duas posicoes: os corpos ficam colados quando o soco acerta, e um vetor de dois pixels
		// e ruido -- a direcao do golpe e pra onde quem bateu estava virado.
		if (r.Encostou) alvo.RumoDoGolpe = MeleeArea.Frente(a.Facing);

		if (r.Encostou) ca.SomarCombo();
		else ca.ZerarCombo();          // errou, foi aparado de longe ou tomou contra: recomeca

		// LUTAR ENSINA OS DOIS. Quem bate ganha pela troca, quem apanha ganha pelo gap de
		// poder -- encarar alguem mais forte e o ganho mais rapido do jogo.
		//
		// ============================ E TREINAR CONTRA O PROPRIO MESTRE PAGA MAIS ============================
		// O segundo parametro do `FightGainMult` existe desde que a funcao foi portada e **nunca
		// tinha sido passado**: sete chamadores, todos com um argumento so -- a API orfa do padrao
		// "sigilo do BP", uma regra escrita e desligada. O `EhMeuMestre` (`GameServer.Mestre.cs`) e
		// o `mst_is_my_master` do DM, lido a cada golpe como la (`combatgains.dm:88-100`).
		//
		// A PERGUNTA E FEITA NOS DOIS SENTIDOS E ELA NAO E SIMETRICA: quem tem mestre ganha ate 3x
		// batendo nele; o mestre batendo no aluno cai no teto geral (2x, e na pratica 1x, porque ele
		// e o mais forte). E o desenho do DM -- o bonus e de quem esta aprendendo.
		// ==============================================================================================
		a.Ficha.AttackGain(_rng, a.Ficha.FightGainMult(alvo.Ficha, EhMeuMestre(a, alvo)));
		if (r.Encostou) alvo.Ficha.AttackGain(_rng, alvo.Ficha.FightGainMult(a.Ficha, EhMeuMestre(alvo, a)));

		// O contra-ataque devolve o golpe: quem bloqueou na hora certa acerta de volta.
		if (r.Desfecho == Desfecho.Contra)
		{
			GolpeResultado devolta = MeleeResolver.Resolver(
				cd, ca, MeleeArea.AnguloDeChegada(a.Pos, a.Facing, alvo.Pos), _rng, tipo);
			MarcarAgressao(a, alvo);
			ResolverDesfecho(alvo, a, devolta);
			AnunciarGolpe(alvo, a, devolta, 2, zanzo: false);   // o contra nao investe
		}

		// QUEM EU SOQUEI E QUANDO -- e o que deixa o proximo golpe DELE ser reconhecido como troca
		// simultanea. Ver `TentarEmbate`.
		a.UltimoAlvo = alvo.Id;
		a.UltimoSocoMs = NowMs();

		ResolverDesfecho(a, alvo, r);
		AnunciarGolpe(a, alvo, r, nivel, zanzo, investiu);

		// O CORPO SAI DO LUGAR. Vem DEPOIS do dano, como no original (`attack cmn.dm:110`, logo
		// apos o `hitProc`): o arremesso e consequencia do golpe que ACERTOU, e o `Impact` le o
		// dano ja calculado. Golpe aparado ou esquivado nao arremessa -- `r.Encostou` diz isso.
		if (r.Encostou) TentarEmpurrar(a, alvo, r.Dano, golpe);

		// O CHAO RACHA sob um golpe pesado ou critico. Ver `RacharChao`.
		if (r.Encostou && (golpe == Protocol.Golpe.Pesado || r.Desfecho == Desfecho.Critico))
			RacharChao(a.Zone, alvo.Pos, Math.Max(a.Ficha.expressedBP, alvo.Ficha.expressedBP));
	}

	/// <summary>As consequencias fora do corpo: nocaute, morte, Zenkai.</summary>
	private void ResolverDesfecho(ServerPlayer a, ServerPlayer d, GolpeResultado r)
	{
		if (r.RaboArrancado)
		{
			AjustarGanhoDoRabo(d);
			GD.Print($"[server] {a.Name} ARRANCOU O RABO de {d.Name}"
					 + $" (ritmo de treino de {d.Name}: x{d.Ficha.tailgain})");
		}

		// O ODIO POR GOLPE (`ENMITY_HIT`, `CombatMovement.dm:225`): so cresce contra quem a vitima
		// ja tinha declarado rival. Fica aqui, e nao no `Atacar`, porque este e o ponto que ja
		// responde "este golpe teve consequencia".
		if (r.Encostou) GolpeDeRival(d, a);

		if (r.Nocauteou)
		{
			GD.Print($"[server] {a.Name} NOCAUTEOU {d.Name} ({r.Membro})");
			// DERROTADO E NOCAUTEADO **OU** MORTO. Sem esta linha o Zenkai nunca aconteceria em
			// luta amistosa (golpe nao-letal e o padrao, e ali ninguem morre) -- justamente onde
			// a mecanica mais faz sentido. A recarga de uma hora impede o dobro quando o nocaute
			// vira morte na sequencia.
			//
			// E DESDE QUE A AMIZADE EXISTE ISTO FAZ MAIS UMA COISA: quem gostava de quem caiu, e
			// estava vendo, entra em RAIVA LENDARIA -- que e o que a linha Legendary pede. Ver
			// `AoPerderALuta`, que e o funil unico das consequencias de derrota.
			AoPerderALuta(d, a, morreu: false);
		}

		if (!r.Morreu) return;

		// O PRAZO NAO E ESCRITO AQUI: `CombatState.Morrer()` ja avisou o `AMorteAconteceu`, que e o
		// unico lugar que arma o relogio da morte. Ver `GameServer.Alem.cs`.
		GD.Print($"[server] {a.Name} MATOU {d.Name} ({r.Membro})");

		// ZENKAI: perder pra alguem mais forte arranca poder do corpo. E pago na hora, direto
		// no BP base -- e recompensa, nao treino, entao nao passa pelo CapCheck. A concessao e
		// a MESMA do nocaute (ver GameServer.Raciais.cs), inclusive a recarga.
		// E o LUTO, aqui com o grau EXTREMO -- e ele que abre o SSJ1.
		AoPerderALuta(d, a, morreu: true);
	}

	// =====================================================================
	// O ARRANQUE (o "dash")
	// =====================================================================
	/// <summary>
	/// FECHAR A DISTANCIA ANTES DE SOCAR. E o `Attack()` do original: aperta ataque, o
	/// personagem VAI ate quem esta na frente e so entao bate.
	///
	/// Dois alcances, e a diferenca e a tecla:
	///   * SHIFT + ESPACO (<paramref name="longo"/>) -- investida de cinco tiles, e o golpe
	///     que sai dela ja e o pesado;
	///   * so ESPACO -- passo curto de dois tiles e meio, so pra nao errar um soco por meio
	///     metro de distancia. Custa metade do Ki.
	///
	/// MORA INTEIRO NO SERVIDOR, e isso e deliberado: um arranque e um salto de posicao, e
	/// salto de posicao calculado no cliente e exatamente o que a validacao de movimento
	/// existe pra recusar. Feito aqui, o cliente so aperta a tecla -- a posicao nova volta
	/// como correcao e o golpe volta como relato, os dois ja decididos.
	/// </summary>
	/// <returns>
	/// Houve deslocamento de verdade. E o que decide a IMAGEM REMANESCENTE: o dono viu que
	/// "shift + espaco mesmo sem ninguem perto" deixava miragem parado no lugar, e miragem de quem
	/// nao saiu do lugar e o oposto do que o Zanzoken quer dizer. Todos os `return` daqui sao
	/// "nao investiu": sem alvo, ja colado, sem Ki, em recarga, ou parede no caminho.
	/// </returns>
	private bool Aproximar(ServerPlayer a, bool longo)
	{
		long agora = NowMs();
		if (a.Combate == null || agora < a.DashLivreEm) return false;

		float alcance = longo ? AlcanceDoDash : AlcanceDoPasso;
		// O MARCADO ESTICA O ARRANQUE LONGO ATE OS 15 TILES DO DM. Ver `AlcanceDoDashMarcado`.
		ServerPlayer? alvo = AlvoParaArranque(a, alcance, longo ? AlcanceDoDashMarcado : alcance);
		if (alvo == null) return false;

		double custo = a.Ficha.MaxKi * CustoDashKi * (longo ? 1 : 0.5);
		if (a.Ficha.Ki < custo) return false;

		// PARA ONDE: a um TILE do alvo, na linha que liga os dois. Encostado, nao POR CIMA --
		// parar dentro do alcance do soco mas colado empilha um sprite no outro, que foi
		// exatamente o que apareceu na primeira versao.
		Vec2 d = alvo.Pos - a.Pos;
		float dist = d.Length;

		// ============================ INVESTIDA PRECISA SER UMA INVESTIDA ============================
		// A conta antiga so exigia `dist > DistanciaDeParada` -- ou seja, a 40 px do alvo ela
		// disparava e deslocava OITO pixels. O jogador via o rasgo, ouvia o som e ganhava a miragem
		// por um passo que nao existiu; e o que o dono descreveu como "o personagem ta dando tp mesmo
		// estando pertinho do outro".
		//
		// Agora o que importa e o DESLOCAMENTO, e ele tem que valer pelo menos um tile. Vale igual
		// pros dois golpes -- o que muda entre leve e pesado e o ALCANCE (ate onde a investida busca
		// alguem), nao o quao curta ela pode ser.
		// =============================================================================================
		if (dist - DistanciaDeParada < DeslocamentoMinimo)
		{
			// PERTO DEMAIS PRO ARRANQUE, LONGE DEMAIS PRO SOCO -- o pior lugar do combate.
			//
			// O dono: "as vezes vc n da tp pq ta perto mas n o suficiente pra bater ai vc soca o nada
			// e e anti climatico". Ele esta certo, e a culpa era do piso que eu mesmo pus: sem
			// investida o corpo ficava parado a 40 px e o soco (que alcanca 40) passava raspando.
			//
			// Entao o corpo ANDA o resto. Nao e investida -- nao ha rasgo, som nem miragem, e a
			// funcao devolve `false` --, e so o passo que faltava pra o punho chegar. A parede
			// continua mandando: o mesmo teste de caminho de sempre.
			// O `modo` VAI JUNTO: quem esta nadando fecha esse meio metro por cima da agua, como faz
			// andando. Sem ele a agua viraria uma parede que so aparece na hora de socar -- a
			// excecao esquecida que a `ClasseDeAgua` existe pra evitar.
			if (dist > CombatKnobs.Alcance && mapaDoAtaque(a) is { } m
				&& !MoveRules.PathOccupied(m, a.Pos, a.Pos + d.Normalized() * (dist - DistanciaDeParada),
										   ModoDeTravessiaDe(a)))
			{
				a.Pos += d.Normalized() * (dist - DistanciaDeParada);
				a.Facing = MoveRules.FacingFrom(d, a.Facing);
				a.LastInputMs = agora;
				a.CorrecaoEsperadaAte = agora + 500;
				a.SeqDoTeleporte = a.SeqInput;
				a.OrcamentoPx = 0;

				var passo = Protocol.Begin(Protocol.S2C.Correction);
				passo.Put(a.SeqInput);
				passo.PutVec(a.Pos);
				a.Peer?.Send(passo, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			}
			return false;
		}

		Vec2 destino = a.Pos + d.Normalized() * (dist - DistanciaDeParada);

		// PAREDE MANDA MAIS QUE O ARRANQUE. Sem esta checagem, investir contra um muro com
		// alguem do outro lado atravessaria a parede -- o unico jeito de andar por dentro dela.
		ZoneCollision? mapa = _catalogo?.Get(a.Zone)?.Mapa;
		if (mapa != null && MoveRules.PathOccupied(mapa, a.Pos, destino, ModoDeTravessiaDe(a))) return false;

		a.Ficha.Ki -= custo;
		a.SaiuDe = a.Pos;   // pra miragem: e daqui que o vulto nasce, e nao de onde ele chegou
		a.Pos = destino;
		a.Facing = MoveRules.FacingFrom(d, a.Facing);   // chega OLHANDO pro alvo
		a.DashLivreEm = agora + RecargaDashMs;
		a.LastInputMs = agora;   // o cliente vai reportar da posicao NOVA: nao conta como passo
		// os pacotes que o cliente ja mandou com a posicao ANTIGA vao chegar e ser corrigidos.
		// Isso e o arranque funcionando, nao um cliente desonesto -- nao conta no medidor.
		a.CorrecaoEsperadaAte = agora + 500;   // + a SEQUENCIA: input montado antes deste instante nao opina sobre onde o corpo esta
		a.SeqDoTeleporte = a.SeqInput;

		// a posicao nova precisa chegar ao dono AGORA, senao o proximo pacote dele parte do
		// lugar antigo e o servidor devolve correcao em cima de correcao
		var w = Protocol.Begin(Protocol.S2C.Correction);
		w.Put(a.SeqInput);   // O PACOTE TEM DUAS PARTES desde o conserto do dash:
		// sequencia + posicao. Escrever so a posicao aqui mandava um pacote MALFORMADO --
		// o cliente leria a coordenada X como se fosse a sequencia.
		w.PutVec(a.Pos);
		a.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		return true;   // investiu de verdade: houve deslocamento
	}

	/// <summary>
	/// Quem esta na frente e vale um arranque. O cone e o mesmo do soco; o que muda e o
	/// comprimento -- e por isso que o arranque pega quem esta "mais ou menos" na frente e
	/// ignora quem esta ao lado.
	/// </summary>
	/// <summary>O mapa da zona de quem ataca. Atalho -- o `Aproximar` consulta duas vezes.</summary>
	private ZoneCollision? mapaDoAtaque(ServerPlayer a) => _catalogo?.Get(a.Zone)?.Mapa;

	/// <param name="alcanceMarcado">
	/// O alcance de quem foi MARCADO, que e maior (ver <see cref="AlcanceDoDashMarcado"/>). Separado do
	/// <paramref name="alcance"/> porque as duas buscas respondem perguntas diferentes: o cone ADIVINHA
	/// em quem voce quis bater (e por isso e curto), e a marca voce DECLAROU.
	/// </param>
	private ServerPlayer? AlvoParaArranque(ServerPlayer a, float alcance, float alcanceMarcado)
	{
		// MARCADO PRIMEIRO. So precisa estar no ALCANCE -- o cone nao entra: quem marcou ja
		// disse em quem quer bater, e obrigar a mirar de novo com o mouse seria pedir a mesma
		// informacao duas vezes.
		if (Marcado(a) is { } mira)
		{
			float d2 = (mira.Pos - a.Pos).LengthSquared;
			if (d2 <= alcanceMarcado * alcanceMarcado && d2 > DistanciaDeParada * DistanciaDeParada) return mira;
			return null;   // marcou alguem longe demais: nao arranca atras de outro sem querer
		}

		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;
		Vec2 frente = MeleeArea.Frente(a.Facing);

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead || o.Combate.Intocavel) continue;
			if (!AlcancaPelaAltura(a, o)) continue;

			Vec2 d = o.Pos - a.Pos;
			float dist2 = d.LengthSquared;
			if (dist2 > alcance * alcance) continue;
			// ja esta de frente: nao ha o que fechar, o soco normal resolve
			if (dist2 <= DistanciaDeParada * DistanciaDeParada) continue;
			if (MeleeArea.Angulo(frente, d) > CombatKnobs.MeioAnguloCone) continue;

			if (dist2 >= melhorDist) continue;
			melhorDist = dist2;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// Quem esta no cone do golpe. O MAIS PROXIMO leva -- socar nao acerta dois de uma vez.
	/// Morto e ignorado: o corpo esta no chao, nao no caminho.
	/// </summary>
	private ServerPlayer? AlvoNaFrente(ServerPlayer a)
	{
		// MARCADO PRIMEIRO, e so a DISTANCIA conta. O `Atacar` ja virou o personagem pra ele,
		// entao normalmente o cone tambem aprovaria -- mas com dois colados o cone aprova os
		// dois, e e ai que marcar tem que decidir.
		if (Marcado(a) is { } mira
			&& (mira.Pos - a.Pos).LengthSquared <= CombatKnobs.Alcance * CombatKnobs.Alcance)
			return mira;

		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead || o.Combate.Intocavel) continue;
			// ============================ QUEM ALCANCA QUEM, POR ANDAR ============================
			// Antes desta linha o alcance era so horizontal, e voar nao mudava NADA no combate --
			// dois corpos a 20 tiles de altura de diferenca socavam um ao outro normalmente, o que
			// tornava a altitude decoracao. As regras (assimetricas!) moram no `Voo.PodeAcertar`.
			// ====================================================================================
			if (!AlcancaPelaAltura(a, o)) continue;
			if (!MeleeArea.NoAlcance(a.Pos, a.Facing, o.Pos)) continue;

			float dist = (o.Pos - a.Pos).LengthSquared;
			if (dist >= melhorDist) continue;
			melhorDist = dist;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>
	/// ============================ "NAO ADIANTOU": O RETORNO PRA QUEM SOCOU UM CORPO EM CINEMATICA ============================
	/// O escudo da transformacao entra pelo `CombatState.Intocavel`, e `Intocavel` tira o corpo da
	/// BUSCA -- o <see cref="AlvoNaFrente"/> pula, o <see cref="AlvoParaArranque"/> pula, o
	/// <see cref="Marcado"/> pula. Sem alvo, o golpe cai no ramo do vazio e quem socou recebe o corte
	/// do punho no ar e **mensagem nenhuma**.
	///
	/// Isso e aceitavel nos poucos segundos da carencia de renascimento, e e inaceitavel nos 140 s da
	/// cena do SSJ3: um jogador socando um corpo parado na frente dele e atravessando por dois minutos
	/// nao le "ele esta protegido", le "a deteccao de acerto quebrou" -- e reporta como bug. O escudo
	/// que ninguem entende e metade do trabalho.
	///
	/// **O MAIS DISCRETO QUE O JOGO JA TEM**: uma linha de texto pra quem socou, e so pra ele. O port
	/// nao tem desfecho de "dano zero" pra reusar -- `Esquivou` e `Aparou` sao gestos do DEFENSOR
	/// (vulto, anel de choque, membro que apara) e um corpo preso na propria cinematica nao esta
	/// desviando nem aparando nada; anunciar um deles seria contar uma historia falsa pra zona inteira.
	/// Nao nasceu pacote novo, nao nasceu efeito novo, e ninguem alem de quem socou ve a linha.
	///
	/// A VARREDURA SO ACONTECE NO SOCO QUE JA SAIU VAZIO -- e ela custa uma volta na zona, com o mesmo
	/// crivo de alcance e de ALTURA do <see cref="AlvoNaFrente"/> que acabou de recusar o corpo. Somar
	/// um crivo diferente aqui faria a mensagem aparecer pra quem nem estava com ele na frente.
	/// =========================================================================================================================
	/// </summary>
	private void AvisarQueOAlvoEstaEmCena(ServerPlayer a)
	{
		long agora = NowMs();
		if (agora < a.AvisoDeCenaMs) return;

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead || o.Combate == null || !EmCena(o)) continue;
			if (!AlcancaPelaAltura(a, o)) continue;
			if (!MeleeArea.NoAlcance(a.Pos, a.Facing, o.Pos)) continue;

			a.AvisoDeCenaMs = agora + MsEntreAvisosDeCena;
			Avisar(a, $"seu golpe atravessa {o.Name}: a transformação o envolve, e nada o alcança.");
			return;
		}
	}

	/// <summary>
	/// Quanto tempo entre duas dessas mensagens pro mesmo atacante. Tres segundos: longo o bastante
	/// pra nao virar enxurrada num soco de 3 Hz, curto o bastante pra quem chegou no meio da cena
	/// ainda receber a explicacao antes de desistir.
	/// </summary>
	private const long MsEntreAvisosDeCena = 3000;

	/// <summary>
	/// ============================ "APERTEI E NAO SAIU NADA" -- O OUTRO SILENCIO DO SOCO ============================
	/// O `Atacar` recusava o golpe na PRIMEIRA linha e voltava sem uma palavra. Enquanto as recusas eram
	/// "morto", "nocauteado" e "recarga", isso estava certo: as tres o jogador ve na tela (o corpo esta
	/// no chao) ou sente no ritmo (a cadencia e a mesma sempre).
	///
	/// As outras duas nao aparecem em lugar nenhum, e sao justamente as que o dono descreveu:
	///
	///   * ARREMESSADO -- o corpo esta voando pelo golpe pesado do outro. O cliente ja o desenha
	///     deitado, mas ate agora ele TAMBEM aceitava o soco: saia animacao e assobio de punho no ar,
	///     e nenhuma mensagem. Le como "meus socos nao acertam ele";
	///   * ATORDOADO -- a faixa `Cambaleia` do <see cref="Empurrao"/> (forca entre metade do limiar e
	///     o limiar) vira 1,0 s de `Stun`, e `Stun` **nao viaja pro cliente**: nao ha campo de
	///     atordoamento no `Protocol`. Um segundo inteiro em que apertar a tecla nao produz nem o som
	///     do soco no ar. Era o unico estado 100% mudo do combate.
	///
	/// UMA LINHA DE TEXTO, SO PRA QUEM APERTOU, com o mesmo freio de tres segundos do aviso de
	/// cinematica -- e pelo mesmo motivo: a tecla sai a 3 Hz. Nao nasceu pacote nem efeito novo.
	///
	/// SO PRA GENTE (`EhPessoa`): o `Atacar` da IA passa por aqui a 30 Hz, e explicar a um NPC por que
	/// ele nao socou seria varrer a zona pra escrever num `Peer` nulo.
	/// ==============================================================================================================
	/// </summary>
	private void ExplicarPorQueNaoSai(ServerPlayer a)
	{
		if (a.Peer == null) return;

		// NO EMBATE NAO SE EXPLICA NADA. O `Comecar` do Zanzo Clash trava o corpo com um `Stun` do
		// tamanho da cena inteira (e de proposito: la quem manda no lugar dele e o servidor), entao sem
		// esta linha o jogador que apertasse espaco no meio do embate leria "seu corpo ainda nao se
		// recompos do ultimo golpe" -- uma explicacao verdadeira sobre o campo e falsa sobre o mundo.
		if (_emEmbate.ContainsKey(a.Id)) return;

		string? razao =
			a.TiquesDeVoo > 0 ? "o golpe te jogou longe: seu corpo ainda está no ar."
			: a.Combate.Stun > 0 ? "seu corpo ainda não se recompôs do último golpe."
			: null;   // morto, nocauteado ou em recarga: isso a tela e o ritmo ja contam

		if (razao == null) return;

		long agora = NowMs();
		if (agora < a.AvisoDoCorpoMs) return;
		a.AvisoDoCorpoMs = agora + MsEntreAvisosDeCena;
		Avisar(a, razao);
	}

	/// <summary>
	/// MARCAR ALGUEM. Zero limpa a mira.
	///
	/// ============================ ISTO ESTAVA DENTRO DO `switch` DO DESPACHO ============================
	/// A validacao ("so vale mirar em quem existe e esta na MESMA zona -- o resto e cliente
	/// inventando") era duas linhas soltas no `case Protocol.C2S.Alvo`. Enquanto so o pacote mirava,
	/// tanto fazia; a partir do momento em que a IA tambem mira -- e ela mira pra poder atirar de
	/// longe --, ou a regra vira funcao ou ela vira DUAS regras, e a segunda e sempre a mais frouxa.
	///
	/// Continua sendo o mesmo funil pros dois lados: `Handle` chama isto, `AplicarComando` chama
	/// isto, e nao ha uma terceira escrita em `AlvoId` no servidor inteiro fora do auto-limpar do
	/// <see cref="Marcado"/>.
	/// ==============================================================================================
	/// </summary>
	private void Mirar(ServerPlayer quem, int alvo) =>
		quem.AlvoId = CorpoNaMinhaZona(quem, alvo) != null ? alvo : 0;

	/// <summary>
	/// UM CORPO DA MINHA ZONA, POR ID -- a resolucao que <see cref="Mirar"/> e <see cref="Marcado"/>
	/// dividem.
	///
	/// ============================ POR QUE NAO BASTA O `_players` ============================
	/// Porque nem todo corpo que existe num lugar e um corpo SIMULADO. O corpo largado por quem esta
	/// meditando (ou ao leme de uma nave) vive so na `ZoneList` -- ele fica fora do `_players` de
	/// proposito, senao a ficha dele, que e a MESMA do dono, seria tiqueada duas vezes (ver
	/// `GameServer.CorpoLargado.cs`).
	///
	/// Sem esta segunda perna, marcar alguem que esta meditando simplesmente nao funcionaria: o
	/// pacote seria recusado como "cliente inventando" e o corpo parado seria o unico alvo do jogo
	/// em que a mira nao pega. E marcar e justamente o que se faz antes de acertar alguem que nao se
	/// mexe.
	///
	/// O DICIONARIO VEM PRIMEIRO porque ele responde por todo mundo menos esses; a varredura da zona
	/// e a excecao, e ela custa uma volta so -- quem falha nas duas some da mira na mesma chamada
	/// (ver o auto-limpar do `Marcado`), entao nao ha varredura repetida por tique.
	/// ====================================================================================
	/// </summary>
	private ServerPlayer? CorpoNaMinhaZona(ServerPlayer quem, int id)
	{
		if (id == 0) return null;
		if (_players.TryGetValue(id, out ServerPlayer? o))
			return o != quem && o.Zone.Hash == quem.Zone.Hash ? o : null;

		foreach (ServerPlayer b in ZoneList(quem.Zone.Hash))
			if (b.Id == id && b != quem) return b;
		return null;
	}

	/// <summary>
	/// O ALVO MARCADO, se ainda valer a pena. Devolve nulo quando ninguem foi marcado ou
	/// quando o marcado saiu de cena (morreu, trocou de zona, virou intocavel) -- e ai o
	/// combate volta a escolher pelo cone, sozinho.
	/// </summary>
	private ServerPlayer? Marcado(ServerPlayer a)
	{
		if (a.AlvoId == 0) return null;
		if (CorpoNaMinhaZona(a, a.AlvoId) is not { } o || o.Ficha.dead || o.Combate.Intocavel)
		{
			a.AlvoId = 0;   // limpa sozinho: alvo morto nao fica preso na mira pra sempre
			return null;
		}
		// A ALTURA VALE ATE PRO MARCADO, e este e o caso que mais precisava: marcar alguem e
		// depois subir dois andares nao pode virar um passe livre pra continuar socando de longe.
		// O alvo NAO e limpo -- ele volta a valer assim que os dois se emparelharem de novo.
		if (!AlcancaPelaAltura(a, o)) return null;
		return o;
	}

	/// <summary>
	/// SOCO NO AR. Anuncia pra zona um golpe que nao achou ninguem -- e o que faz o corte do
	/// punho no vazio (`meleemiss1/2/3`) soar, pra quem soca e pra quem esta perto.
	///
	/// Vai por canal NAO CONFIAVEL: quem treina soca tres vezes por segundo, e reenviar um
	/// "nao aconteceu nada" e o tipo de trafego que nao vale um unico byte de garantia. Perder
	/// um custa um som.
	/// </summary>
	private void AnunciarSocoNoAr(ServerPlayer a, bool zanzo = false, bool investiu = false)
	{
		var e = new Protocol.HitEvent
		{
			Atacante = a.Id, Alvo = 0, Desfecho = (byte)Desfecho.Errou,
			Nivel = 1, Membro = "", Investiu = investiu,
		};
		var w = Protocol.Begin(Protocol.S2C.Hit);
		e.Write(w);

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelState, DeliveryMethod.Unreliable);
	}

	/// <summary>
	/// Conta o golpe pra zona. Os DOIS ENVOLVIDOS recebem o dano; quem so assistiu recebe o
	/// evento sem numero -- ve o impacto e ouve o som, mas nao le a ficha alheia.
	/// </summary>
	/// <param name="zanzo">
	/// Houve imagem remanescente. PADRAO FALSO porque o vulto e da INVESTIDA do soco pesado, e
	/// golpe de tecnica (as quatro chamadas de `Tecnicas.G2/G3`) nao investe -- ele acontece de
	/// onde o corpo ja estava. Deixar o padrao aqui evita que cada tecnica nova tenha que decidir
	/// sobre um efeito que nao e dela.
	/// </param>
	private void AnunciarGolpe(ServerPlayer a, ServerPlayer d, GolpeResultado r, int nivel, bool zanzo = false, bool investiu = false)
	{
		var cheio = new Protocol.HitEvent
		{
			Atacante = a.Id, Alvo = d.Id, Desfecho = (byte)r.Desfecho,
			Nivel = (byte)Math.Clamp(nivel, 1, 3),
			// DE ONDE O SOCO SAIU. `a.Pos` aqui ja e o destino do arranque -- e essa a coordenada
			// com que o golpe FOI resolvido, e e ela que o desenho precisa. Ver `HitEvent.PosAtacante`.
			PosAtacante = a.Pos,
			TemDano = true, Dano = (float)r.Dano, Membro = r.Membro,
			Quebrou = r.Quebrou, Decepou = r.Decepou, Nocauteou = r.Nocauteou, Morreu = r.Morreu,
			Rabo = r.RaboArrancado, Investiu = investiu,
			// A ESQUIVA E DO OUTRO: quem se desviou e quem deixa o vulto.
			//
			// SO NA ESQUIVA ATIVA, e e onde o DM poe o gate: o `haszanzo` aparece UMA vez no ramo de
			// esquiva, no combo dodge (`CombatMovement.dm:298`), e nao na esquiva por velocidade
			// (`:286`, gated so por `!block_ok`). A esquiva por velocidade continua tendo a TROCA DE
			// CORPO -- e o `flick('Zanzoken.dmi', M)`, que o cliente faz com `EsquivaZanzoken.Trocar`
			// pra todo mundo. O que este campo liga e o VULTO DO CORPO (a foto das quatro camadas),
			// caro demais pra nascer em cada soco desviado de uma troca inteira.
			ZanzoEsquiva = r.EsquivaAtiva && d.Livro?.Sabe(PathDoZanzoken) == true,
		};
		Protocol.HitEvent magro = cheio;
		magro.TemDano = false;

		var wCheio = Protocol.Begin(Protocol.S2C.Hit); cheio.Write(wCheio);
		var wMagro = Protocol.Begin(Protocol.S2C.Hit); magro.Write(wMagro);

		// A BANCADA ESCUTA OS DOIS FIOS, e nao a struct. Ver `EscutaDeGolpes`: a pergunta da
		// `--pecateste` e sobre o que a PLATEIA recebe, e ler `cheio.Decepou` responderia sobre a
		// variavel do servidor -- que e verdadeira mesmo se o `Write` deixar o bit pra tras.
		EscutaDeGolpes?.Add((true, wCheio.CopyData()));
		EscutaDeGolpes?.Add((false, wMagro.CopyData()));

		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			// Pros DOIS envolvidos o relato e confiavel: perder o pacote que diz "voce perdeu
			// o braco" nao e opcao. Pra quem so assiste vai sem garantia -- e uma piscada e um
			// som, e reenviar isso pra uma zona cheia de gente e o tipo de trafego que derruba
			// servidor.
			if (o == a || o == d)
				o.Peer?.Send(wCheio, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			else
				o.Peer?.Send(wMagro, Protocol.ChannelState, DeliveryMethod.Unreliable);
		}

		// LOGO EM SEGUIDA, A PECA CAI. Depois do `S2C.Hit` porque a ordem e a do original: o
		// `LopLimb` avisa, joga a peca no chao (`SpawnLop`, `mobparts_logic.dm:110`) e so entao
		// solta o jato -- e aqui o jato nasce do proprio `Hit`, que ja saiu. Ver `SoltarPecas`.
		SoltarPecas(d, r);
	}

	/// <summary>
	/// AS PECAS DO CORPO CAINDO NO CHAO -- o `SpawnLop` do original (`mobparts_logic.dm:110,144-146`).
	///
	/// ============================ POR QUE ISTO NAO VEM COM O `Hit` ============================
	/// O `S2C.Hit` ja diz que houve amputacao (o bit `Decepou`, que chega ate pra quem so assiste) e
	/// e por ele que o jato de sangue dispara no cliente -- zero byte novo. O que ele NAO diz pra
	/// plateia e QUAL membro: o campo `Membro` so e escrito quando `TemDano`, e quem assiste recebe
	/// o pacote magro justamente pra nao ler ficha alheia. Alargar aquele pacote resolveria a peca e
	/// de quebra vazaria o membro atingido em TODO soco -- caro por um evento raro.
	///
	/// Entao a peca vem pelo `S2C.Decalque`, que ja e confiavel e ja vai pra zona inteira. E o canal
	/// certo tambem pelo prazo: uma marca de 60 s que se perde no fio nao volta, enquanto o jato de
	/// meio segundo perdido nao faz falta a ninguem.
	/// ==========================================================================================
	/// </summary>
	private void SoltarPecas(ServerPlayer d, GolpeResultado r)
	{
		if (r.PecasCaidas is not { Count: > 0 } pecas) return;

		foreach (PecaDeCorpo peca in pecas)
		{
			// ESPALHA EM VOLTA, e o sorteio e do SERVIDOR. O original faz
			// `pixel_x/pixel_y += rand(-32,32)` no `New()` de cada peca (`mobparts.dm:404-405`) --
			// um tile em pixels, que e o mesmo `TileSize` daqui. Sorteado no cliente, cada tela
			// veria o braco num lugar; e cenario, e cenario que discorda entre telas e a primeira
			// coisa que alguem fotografa achando que e bug.
			var onde = new Vec2(
				d.Pos.X + _rng.Next(-ZoneCollision.TileSize, ZoneCollision.TileSize + 1),
				d.Pos.Y + _rng.Next(-ZoneCollision.TileSize, ZoneCollision.TileSize + 1));

			// A DIRECAO NAO DIZ NADA AQUI: a folha `Body Parts Bloody` tem um recorte por peca e
			// nenhum sufixo de direcao, e o `Plantar` recebe a animacao por nome. Vai `South` pelo
			// mesmo motivo que o chao danificado vai -- e o campo do pacote, nao uma escolha.
			MandarDecalque(d.Zone, Protocol.Decal.Membro, onde, Facing.South, peca);
		}
	}

	// =====================================================================
	// `--diaggolpe`: A CONTA DO SOCO, EM VOZ ALTA
	// =====================================================================
	/// <summary>
	/// Liga o relato de cada soco no console do servidor. Ver <see cref="ExplicarGolpe"/>.
	/// </summary>
	private bool _diagGolpe;

	/// <summary>
	/// POR QUE O SOCO NAO PEGOU NINGUEM -- a diferenca que o jogador nao consegue ver.
	///
	/// ============================ DOIS ERROS QUE PARECEM UM ============================
	/// Um soco pode falhar de dois jeitos completamente diferentes, e o cliente desenha os dois
	/// igual (o mesmo som de vento):
	///
	///   * ERRO DE GEOMETRIA -- nao havia ninguem no alcance nem no cone. E o que este metodo
	///     relata. Se acontece logo depois de uma investida, ha algo errado: a investida para a
	///     um tile do alvo, bem dentro do alcance do soco.
	///   * ERRO DE PONTARIA -- havia alguem, e o sorteio de `CombatMath.Pontaria` falhou. Isso e
	///     mecanica, nao defeito, e sai pelo outro relato (`ExplicarGolpe`).
	///
	/// O dono relatou "mesmo dando o tp de soco o hit n pega". Sem separar os dois, nao da pra
	/// saber se a queixa e de uma hitbox furada ou da taxa de acerto do personagem dele.
	/// ===================================================================================
	/// </summary>
	private void ExplicarSocoNoAr(ServerPlayer a, bool investiu)
	{
		// QUEM ESTAVA POR PERTO, e por que cada um ficou de fora. Sem isto o relato diria so
		// "nao achei ninguem", que e a pergunta e nao a resposta.
		var perto = new List<string>();
		foreach (ServerPlayer o in ZoneList(a.Zone.Hash))
		{
			if (o == a || o.Ficha.dead) continue;
			Vec2 d = o.Pos - a.Pos;
			float dist = d.Length;
			if (dist > 200) continue;
			double ang = MeleeArea.Angulo(MeleeArea.Frente(a.Facing), d);
			perto.Add($"{o.Name} a {dist:0} px, {ang:0}° do olhar"
					  + $" ({(dist <= CombatKnobs.Alcance ? "no alcance" : "LONGE")},"
					  + $" {(ang <= CombatKnobs.MeioAnguloCone ? "no cone" : "FORA do cone")})");
		}

		GD.Print($"[golpe] {a.Name} socou o AR{(investiu ? " DEPOIS DE INVESTIR" : "")}"
				 + $" | olhando {a.Facing} | alcance {CombatKnobs.Alcance:0} px, cone ±{CombatKnobs.MeioAnguloCone:0}°"
				 + (perto.Count > 0 ? $" | por perto: {string.Join(" ; ", perto)}" : " | ninguem por perto"));
	}

	/// <summary>
	/// A CONTA DO GOLPE QUE ACHOU ALGUEM: quem, quanto, e de onde saiu o numero.
	///
	/// ============================ POR QUE ISTO EXISTE ============================
	/// O dono relatou um admin dando "+100 de dano" com apenas ~10 pontos de diferenca de BP.
	/// Com BP parelho a formula rende uns 3 de dano por soco -- pra chegar a 100 falta um fator
	/// de mais de trinta, e ele tem que estar em ALGUM dos termos.
	///
	/// O suspeito de sempre e o `BpModulus`, que e a RAZAO entre os dois BP EXPRESSOS e nao tem
	/// teto: nocaute derruba o expresso pra um decimo do BP base, invisibilidade o trava em 5, e
	/// um defensor com expresso zerado devolve 999 de multiplicador. Nenhuma dessas tres coisas
	/// aparece na tela de quem esta apanhando, e todas produzem exatamente o que foi relatado.
	///
	/// Por isso o relato imprime os dois BP expressos e o multiplicador SEPARADOS do dano final:
	/// com essas tres colunas na frente, o fator de trinta ou esta ali ou nao esta em lugar nenhum.
	/// =============================================================================
	/// </summary>
	private void ExplicarGolpe(ServerPlayer a, ServerPlayer d, GolpeResultado r, double angulo, bool investiu)
	{
		double bpA = a.Ficha.expressedBP, bpD = d.Ficha.expressedBP;
		GD.Print($"[golpe] {a.Name} -> {d.Name}: {r.Desfecho}, dano {r.Dano:0.##}"
				 + $" | BP expresso {bpA:N0} vs {bpD:N0} (razao x{CombatMath.BpModulus(bpA, bpD):0.##})"
				 + $" | BP base {a.Ficha.BP:N0} vs {d.Ficha.BP:N0}"
				 + $" | physoff {a.Ficha.Ephysoff:0.##} vs physdef {d.Ficha.Ephysdef:0.##}"
				 + $" | tecnica {a.Ficha.Etechnique:0.##}"
				 + $" | angulo {angulo:0}° | dist {(d.Pos - a.Pos).Length:0} px"
				 + $"{(investiu ? " | investiu" : "")}"
				 + $"{(d.Ficha.KO ? " | ALVO NOCAUTEADO" : "")}"
				 + $"{(d.Ficha.isconcealed ? " | ALVO INVISIVEL (expresso travado)" : "")}"
				 + $"{(a.Ficha.isconcealed ? " | EU INVISIVEL (expresso travado)" : "")}"
				 + $" | admin {(EhAdmin(a) ? "sim" : "nao")}");
	}

	// =====================================================================
	// PASSAGEM DE TEMPO
	// =====================================================================
	/// <summary>
	/// Roda a cada tick do servidor (30 Hz): cronometros de recarga, atordoamento, guarda,
	/// saida do nocaute e o renascimento de quem morreu.
	/// </summary>
	private void TickCombate(double dt)
	{
		long agora = NowMs();
		foreach (ServerPlayer pl in _players.Values)
		{
			// ============================ NINGUEM PODE NASCER DENTRO DESTE LACO ============================
			// **A REGRA, PRA QUEM ESCREVER UM CAMINHO NOVO AQUI DENTRO:** nada chamado de dentro deste
			// `foreach` pode PÔR UM CORPO NOVO no `_players` -- nem direto, nem cinco chamadas abaixo. Quem
			// nasce, nasce depois do laco. (Este comentario mora DENTRO do corpo de proposito: assim o
			// `foreach` acima continua na linha 1203, que e a que o rastro abaixo aponta.)
			//
			// E O PERIGO E O CONTRARIO DO QUE PARECE. Desde o .NET Core 3.0 o `Dictionary.Remove` **nao**
			// incrementa a versao da colecao: TIRAR alguem daqui no meio da varredura e legal. Quem
			// invalida o enumerador e a INSERCAO DE CHAVE NOVA -- e ela estoura no `MoveNext` seguinte,
			// mesmo quando o corpo inserido nasceu do ULTIMO da enumeracao. Ou seja: e deterministico,
			// nao e corrida, e nao adianta "testar com um jogador so".
			//
			// JA ACONTECEU, E CUSTOU O SERVIDOR INTEIRO. O caminho era
			// `VenceuOPrazoDaMorte` -> `PassoDaMorte` -> `IrProAlem` -> `DeixarOCadaver` -> `PorNoMundo`,
			// que punha o CADAVER no `_players` (e o tirava na linha seguinte, o que enganava a leitura:
			// "insere e remove" parece um no-op e nao e). Toda morte de jogador derrubava o tique com:
			//
			//     System.InvalidOperationException: Collection was modified; enumeration operation may not execute.
			//        at System.Collections.Generic.Dictionary`2.ValueCollection.Enumerator.MoveNext()
			//        at Jandirus.Server.GameServer.TickCombate(Double dt) in Server\GameServer.Combat.cs:line 1203
			//        at Jandirus.Server.GameServer.Tick() in Server\GameServer.cs:line 4742
			//        at Jandirus.Server.GameServer._Process(Double delta) in Server\GameServer.cs:line 3118
			//
			// (O RASTRO E O DO RELATO, LETRA POR LETRA, E POR ISSO NAO FOI CORRIGIDO. As duas ultimas
			// linhas ja envelheceram -- o `GameServer.cs` cresceu e hoje as mesmas chamadas sao a 4763 e
			// a 3131. So a PRIMEIRA e mantida viva de proposito: o `foreach` acima **continua** na 1203,
			// e e por isso que este comentario mora dentro do corpo do laco. Reproduzido nesta forma
			// pela `--tiquedamorte` com o defeito injetado de volta.)
			//
			// E o estrago nao era "um jogador perde um tique": o `TickCombate` e a PRIMEIRA chamada do
			// `Tick()`, entao os ~60 subsistemas depois dele (fichas, projeteis, feridas, buffs, cadaveres,
			// vacuo, gravidade, esferas, sagas, conquista, ceu, curandeiros...) **e o snapshot por zona**
			// deixavam de rodar naquele quadro -- a zona inteira congelava no instante exato da morte, que
			// e justamente quando todo mundo esta olhando.
			//
			// POR QUE NAO HA UM `ToArray()` AQUI: copiar mascararia o defeito em vez de fecha-lo (o corpo
			// do laco continuaria podendo nascer gente no meio, so que calado) e pagaria uma matriz por
			// tique a 30 Hz sobre TODOS os corpos do mundo -- a mesma conta de lixo que o comentario de
			// `MandarCorpo`, mais abaixo, ja explica por que este laco nao paga.
			//
			// E A SAIDA CERTA JA E O IDIOMA DA CASA, em duas filas que vivem logo abaixo: `_npcsPraTirar`
			// (corpo sem dono que SAI) e `_acordar`/`TickDeQuemVolta` (quem estava fora do corpo e VOLTA).
			// Um caminho novo que precise mexer no `_players` copia o padrao delas: **so enfileira aqui, e
			// aplica depois do `foreach`**.
			// ==========================================================================================
			CombatState c = pl.Combate;
			if (c == null) continue;

			// QUEM ESTA FORA DO CORPO ainda tem corpo pra onde voltar? Ver `BordasDeQuemEstaFora`:
			// caiu, morreu, ou o corpo sumiu do mundo -- as tres respostas sao "acorda", nunca "fica
			// preso". So enfileira; quem tira do mundo e o `TickDeQuemVolta`, la embaixo.
			//
			// O CORPO LARGADO NAO PASSA POR ESTE LACO -- ele nao esta no `_players`, e nem poderia:
			// como ele aponta pra MESMA ficha do dono, ser tiqueado aqui regeneraria, alimentaria e
			// cronometraria o mesmo corpo duas vezes. Ver `GameServer.CorpoLargado.cs`.
			BordasDeQuemEstaFora(pl);

			bool eraKO = pl.Ficha.KO;
			c.Tick(dt);
			if (eraKO && !pl.Ficha.KO)
			{
				c.SincronizarVida();
				GD.Print($"[server] {pl.Name} levantou");
			}

			RegenerarPassivo(pl, dt);

			// VENCEU O PRAZO DA MORTE? A triagem inteira mora em <see cref="VenceuOPrazoDaMorte"/> --
			// tres grupos de corpo, tres destinos diferentes, e o argumento de cada um esta la.
			if (pl.Ficha.dead && agora >= pl.RelogioDaMorte) VenceuOPrazoDaMorte(pl);

			TickDoEstomago(pl, dt);

			// ============================ ESTES TRES SO EXISTEM PRA QUEM TEM TELA ============================
			// Eles "saem sozinhos quando nada mudou" -- mas so DEPOIS de montar a assinatura, e montar
			// custa: `MandarCorpo` aloca uma `List<ParteState>` de 15 itens, um `StringBuilder` e uma
			// string a cada tique; `MandarSkills` interpola outra. Pra um corpo sem `Peer` o
			// `Send` no fim ja era no-op (`pl.Peer?.`), entao tudo isso era lixo puro.
			//
			// Com um clone na mente isso nao aparecia. Com 151 habitantes aparece: a bancada mediu
			// **122 KB por tique** -- 3,7 MB/s pro coletor -- e o povoamento e quem passou a pagar.
			// A regra 0.4 e sobre o tempo do tique, e esta e a outra metade dela: lixo constante numa
			// sessao de horas vira pausa de coletor, e ninguem liga a pausa aos NPCs.
			// ==========================================================================================
			if (pl.Peer == null) continue;

			MandarCorpo(pl);       // barato: sai sozinho quando nada mudou
			MandarAtributos(pl);   // idem: atributo so muda quando se treina
			MandarSkills(pl);
		}

		// QUEM VOLTA PRO PROPRIO CORPO VOLTA AQUI, e nao no meio do golpe que o acordou: `MoveToZone`
		// mexe nas listas de duas zonas, e o `AlvoNaFrente`/`PresaDaFera` que levaram ate a agressao
		// estao percorrendo uma delas. E o mesmo motivo do `_npcsPraTirar` logo abaixo -- e por isso
		// fica ANTES do `return` dele, que so fala de NPC morto.
		TickDeQuemVolta();

		// E DEPOIS DELE, NUNCA ANTES. As duas filas podem vencer no mesmo tique, e a ordem e a regra:
		// a volta SECA (o soco no corpo real, o nocaute, a morte) ganha da onda -- quando isso
		// acontece, o que sobra aqui e so apagar a ondulacao da tela de quem ja voltou. Ver
		// `TickDaOndaDaMente`, que tambem esta aqui em cima do `return` de baixo pelo mesmo motivo
		// que o vizinho: ele mexe nas listas de duas zonas.
		TickDaOndaDaMente();

		if (_npcsPraTirar.Count == 0) return;
		foreach (ServerPlayer morto in _npcsPraTirar)
		{
			GD.Print($"[server] NPC '{morto.Name}' morreu em {morto.Zone.Name} e saiu do mundo");

			// ANTES DE REMOVER, e nao depois: quem cobra a reputacao precisa do `UltimoAgressor` e do
			// `RancorAte` deste corpo, e `RemoverNpc` o tira do `_players`. E este e o UNICO ponto por
			// onde toda morte de corpo sem dono passa -- ver `MorreuUmCorpoSemDono`.
			MorreuUmCorpoSemDono(morto);

			// ============================ O NPC TAMBEM DEIXA CORPO -- `mobDeath.dm:15` ============================
			// **O ORIGINAL CHAMA O MESMO `GenerateCorpse()`** que o jogador chama; a diferenca la e que o
			// mob do NPC nao viaja pro Enma, ele e deletado (`deleteMe()`). Aqui e igual, e por isso a
			// chamada fica NESTE laco: e ele o instante em que o corpo sem dono sai do mundo, exatamente
			// como o `IrProAlem` e o instante em que o do jogador sai.
			//
			// ANTES DO `RemoverNpc`, e a ordem importa pelo mesmo motivo do `MorreuUmCorpoSemDono` acima:
			// o cadaver copia POSICAO e APARENCIA deste corpo, e `RemoverNpc` o tira da `ZoneList` (a
			// posicao ainda estaria la, mas a apresentacao do corpo novo sairia depois do `PeerLeft` do
			// velho -- a mesma piscada que a ordem no `IrProAlem` evita).
			//
			// **E ELE ENTRA NO MESMO TETO POR ZONA** (`Cadaver.TetoPorZona`), que e onde este caminho
			// paga a propria conta: uma invasao de ondas mata dezenas de corpos, e sem o teto uma cidade
			// sitiada acumularia habitante morto ate o fim da sessao.
			// ==================================================================================================
			DeixarOCadaver(morto);

			RemoverNpc(morto);
		}
		_npcsPraTirar.Clear();
	}

	/// <summary>
	/// Os NPCs mortos deste tique. Lista de INSTANCIA e reusada (o `Clear` nao devolve a
	/// capacidade), pelo mesmo argumento do <see cref="_dirigidos"/>: alocar uma lista por tique
	/// num laco de 30 Hz e lixo constante pro coletor mesmo quando ela sai sempre vazia.
	/// </summary>
	private readonly List<ServerPlayer> _npcsPraTirar = [];

	/// <summary>
	/// O ESTOMAGO, na cadencia dele -- ver <see cref="Jandirus.Core.Stats.Nutricao"/>.
	///
	/// ============================ ELE NAO RODA NO TIQUE DO SERVIDOR ============================
	/// As constantes de digestao do original sao POR PASSADA de tres segundos, nao por segundo.
	/// Chamar isto a 30 Hz multiplicaria a digestao por noventa: o tanque esvaziaria em dois
	/// segundos e o vigor encheria no mesmo tempo -- os dois defeitos opostos de uma vez.
	///
	/// Por isso ha um acumulador por jogador. E a mesma disciplina do `_accumulator` do servidor:
	/// um sistema com relogio proprio guarda o relogio proprio.
	/// ===========================================================================================
	/// </summary>
	private readonly Dictionary<int, double> _relogioDoEstomago = [];

	/// <summary>Quanto dano de fome ja se acumulou, por jogador. Ver `Nutricao.DanoDaFome`.</summary>
	private readonly Dictionary<int, double> _fomeAcumulada = [];

	private void TickDoEstomago(ServerPlayer pl, double dt)
	{
		double relogio = _relogioDoEstomago.GetValueOrDefault(pl.Id) + dt;

		// O metabolismo acelera as passadas, e nao so aumenta o tanque: quem digere rapido come
		// mais vezes por minuto. E o `spawn(30 / Metabolism)` do DM.
		double intervalo = Jandirus.Core.Stats.Nutricao.SegundosPorPassada
						 / Math.Max(pl.Ficha.Metabolism, 0.1);
		if (relogio < intervalo) { _relogioDoEstomago[pl.Id] = relogio; return; }
		_relogioDoEstomago[pl.Id] = relogio - intervalo;

		Jandirus.Core.Stats.Nutricao.QueimarVigor(pl.Ficha);
		Jandirus.Core.Stats.Nutricao.Digerir(pl.Ficha);

		// PASSAR FOME MACHUCA, e o dano vai pelo mesmo caminho do combate -- espalhado pelo corpo.
		double acumulado = _fomeAcumulada.GetValueOrDefault(pl.Id);
		double dano = Jandirus.Core.Stats.Nutricao.DanoDaFome(pl.Ficha, ref acumulado);
		_fomeAcumulada[pl.Id] = acumulado;
		if (dano > 0) Espalhar(pl, dano);   // ele ja sincroniza a vida

		// O BARRIGAO RONCA UMA VEZ, e nao a cada tres segundos. O `Hungry` da ficha e o que impede
		// o aviso de virar spam; ele so volta a zero quando o personagem come.
		if (!pl.Ficha.Hungry && Jandirus.Core.Stats.Nutricao.TemFome(pl.Ficha))
		{
			pl.Ficha.Hungry = true;
			Avisar(pl, pl.Ficha.CurrentNutrition <= 0
				? "seu estômago ronca. Você precisa comer ALGUMA COISA."
				: "seu fôlego está no fim. Comer alguma coisa ajudaria.");
		}
	}

	/// <summary>
	/// A FICHA LENTA pro dono: os oito atributos e o que o menu (tecla P) mostra.
	///
	/// Mesma disciplina do corpo -- so sai quando MUDA. Atributo mexe quando se treina, e
	/// treinar leva minutos: mandar isto junto com a vida, cinco vezes por segundo, seria
	/// repetir o mesmo pacote milhares de vezes por sessao.
	/// </summary>
	private static void MandarAtributos(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		var a = new Protocol.AtributosState
		{
			PhysOff = (float)f.Rphysoff, PhysDef = (float)f.Rphysdef,
			KiOff = (float)f.Rkioff, KiDef = (float)f.Rkidef,
			Technique = (float)f.Rtechnique, KiSkill = (float)f.Rkiskill,
			Speed = (float)f.Rspeed, Esoteric = (float)f.Rmagiskill,
			Willpower = (float)f.Ewillpower, Stamina = (float)f.staminapercent,
			Idade = pl.Idade,
			Raca = pl.Race,

			// AINDA ZERO, e de proposito: nenhuma destas habilidades existe no port. Quem
			// acende cada bit e a etapa que trouxer o sistema -- a skill de Sense, o scouter,
			// o nav system. Enquanto estiver zerado, as abas correspondentes NAO aparecem, que
			// e exatamente a regra do original.
			// O NAV so existe NO ESPACO. Em terra firme um mapa estelar nao serve pra nada, e a
			// aba apareceria vazia -- e aba vazia ensina que o sistema nao funciona.
			// A CARTA ESTELAR VALE EM QUALQUER LUGAR -- ver `PoderesVisiveis`, que e quem explica.
			Poderes = (uint)PoderesVisiveis(pl),
			// O FIO CONTINUA FALANDO O MESMO NUMERO de sempre (o `IdRede` do catalogo e o inteiro
			// que o enum antigo tinha), entao cliente velho e save velho leem sem conversao.
			FormaAtual = Jandirus.Core.Forms.Catalogo.Rede(pl.Forma.Atual),
			Maestrias = [.. pl.Forma.Maestria.Todas.Select(t => (Jandirus.Core.Forms.Catalogo.Rede(t.Id), (float)t.V))],
			Disciplina = (byte)(pl.UltraInstinct.Aprendida ? 1 : pl.PoderDaDestruicao.Aprendida ? 2 : 0),
			DiscReal = (float)(pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Real : pl.PoderDaDestruicao.Real),
			DiscAtual = (float)(pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Atual : pl.PoderDaDestruicao.Atual),
			DiscLigada = pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Ligada : pl.PoderDaDestruicao.Ligada,

			// ============================ O MULTIPLICADOR DE TREINO, CALCULADO AQUI ============================
			// O numero sai do Core (`Fighter.MultiplicadorDeGanho`, o `bp_gain_mult()` do DM) e viaja
			// PRONTO. Os quatro pedacos ao lado sao so pra a frase entre parenteses da tela; o
			// `Esmagamento` e o aviso -- e tambem o campo pelo qual o cliente descobre que esta preso
			// no chao, sem precisar de um bit novo num `Estado` que ja nao tem bit livre.
			//
			// ELES ENTRAM NA FICHA LENTA e nao na rapida porque e exatamente esse o perfil: gravidade
			// so muda ao trocar de zona, peso so muda quando o jogador mexe nele, e a Sala so nas duas
			// travessias da porta. A assinatura logo abaixo garante que o pacote so sai quando algum
			// deles mexe de verdade.
			// ================================================================================================
			GanhoDeTreino = (float)f.MultiplicadorDeGanho(),
			Gravidade = (float)Jandirus.Core.Stats.Esmagamento.Gravidade(f),
			GravEfetiva = (float)f.GravidadeEfetiva(),
			PesoMult = (float)Math.Max(f.weight, 1),
			ZonaMult = (float)f.zoneGainMult,
			Esmagamento = (float)Jandirus.Core.Stats.Esmagamento.Razao(f),

			// A SESSAO DA SALA DO TEMPO, na mesma ficha lenta e pelo mesmo criterio: ela muda de
			// fase tres vezes numa visita inteira, e os minutos so precisam de resolucao de MINUTO
			// (a assinatura logo abaixo os arredonda, senao este pacote sairia 30x por segundo pra
			// quem estivesse la dentro). Ver `GameServer.SalaSessao.cs`.
			SalaFase = FaseDaSalaPraTela(pl),
			SalaMinutos = (float)MinutosDaSalaPraTela(pl),
		};

		// assinatura grossa: 1% de resolucao por atributo. Sem isso o ruido de ponto flutuante
		// remandaria o pacote a cada tick.
		//
		// O GANHO ENTRA NELA COM UMA CASA SO (`0.#`), e a grossura aqui e mais importante que nas
		// outras linhas: ele carrega o `Egains` e o folego, que mudam por fracoes a cada tique. Com
		// resolucao fina, este pacote -- que existe justamente pra NAO sair 30x por segundo --
		// passaria a sair 30x por segundo pra qualquer um que estivesse treinando.
		string sig = $"{a.PhysOff:0.##}|{a.PhysDef:0.##}|{a.KiOff:0.##}|{a.KiDef:0.##}|"
				   + $"{a.Technique:0.##}|{a.KiSkill:0.##}|{a.Speed:0.##}|{a.Esoteric:0.##}|"
				   + $"{a.Willpower:0.##}|{a.Stamina:0.#}|{a.Poderes}|{a.Idade}|{a.FormaAtual}|"
				   + $"{a.GanhoDeTreino:0.#}|{a.Gravidade:0.#}|{a.GravEfetiva:0.#}|{a.PesoMult:0.##}|"
				   + $"{a.ZonaMult:0.#}|{a.Esmagamento:0.##}|{a.SalaFase}|{a.SalaMinutos:0}|"
				   + string.Join(',', a.Maestrias.Select(m => $"{m.Forma}:{m.Pct:0.#}"));
		if (sig == pl.SigAtributos) return;
		pl.SigAtributos = sig;

		// A BANCADA ESCUTA A FICHA QUE SAIU. Uma linha, nula em jogo, e pelo mesmo motivo das quatro
		// escutas do `GameServer.FormasTeste.cs`: pacote que saiu no fio nao volta, e a aba de formas
		// (que le a proficiencia das disciplinas) mora do outro lado dele. Ver `EscutaDeAtributos`.
		EscutaDeAtributos?.Add(a);

		var w = Protocol.Begin(Protocol.S2C.Atributos);
		a.Write(w);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}


	// =====================================================================
	// O CORPO PRO DONO
	// =====================================================================
	/// <summary>
	/// Manda o estado de cada membro pro proprio jogador -- e o que alimenta o boneco de dano.
	///
	/// SO QUANDO MUDA. A assinatura e a mesma ideia do `last_sig` do original: com o corpo
	/// parado, isto e zero trafego; com a luta rolando, e um pacote de ~200 bytes por golpe
	/// que altera alguma coisa. Mandar 15 membros a 5 Hz por jogador seria desperdicio puro.
	/// </summary>
	private static void MandarCorpo(ServerPlayer pl)
	{
		if (pl.Combate == null) return;

		var partes = new List<Protocol.ParteState>(pl.Combate.Corpo.Partes.Count);
		var sb = new System.Text.StringBuilder();
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
		{
			// ARREDONDA AO MAIS PROXIMO. O original usa duas contas diferentes e vale saber
			// qual e qual: o HP GERAL usa `round(x)` de um argumento, que no DM e PISO; a
			// porcentagem POR MEMBRO usa `round(x, 1)` de dois, que arredonda. E a de membro
			// que alimenta o boneco.
			byte v = (byte)Math.Clamp(Math.Round(p.Fracao * 100), 0, 100);
			partes.Add(new Protocol.ParteState { Nome = p.Nome, Vida = v, Decepado = p.Decepado });
			sb.Append(p.Decepado ? 'L' : (char)('0' + v / 4));   // 4% de resolucao: nao remanda por ruido
		}

		string sig = sb.ToString();
		if (sig == pl.CorpoEnviado) return;
		pl.CorpoEnviado = sig;

		var w = Protocol.Begin(Protocol.S2C.Corpo);
		w.PutCorpo(partes);
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// ============================ A CURA PASSIVA MUDOU DE CASA ============================
	// Ela era um `private const` daqui. Foi pra `CombatKnobs.RegenPorSegundo`, no Core, e a razao e
	// a regra 0.1 da especificacao: **e uma regra que decide resultado de jogo**, e agora ela decide
	// mais de um. O calor da estrela e calibrado CONTRA ela (o piso do dano existe pra ficar acima da
	// cura -- ver `CalorDaEstrela.FatorMinimo`), e a bancada do AssetPipeline precisa afirmar essa
	// desigualdade sem subir o Godot. Duas copias do 1,67 seriam duas copias que envelhecem separadas.
	// ==================================================================================

	/// <summary>
	/// REGENERACAO PASSIVA: so fora de combate, e so pra quem nao esta morto. Enquanto a tag de
	/// luta esta no ar o corpo nao se recupera -- senao ninguem perde nunca.
	///
	/// ============================ POR QUE ISTO VIROU FUNCAO ============================
	/// Eram cinco linhas soltas dentro do <see cref="TickCombate"/>. Elas sairam de la quando a
	/// estrela passou a queimar, e o motivo e medido: o dano do sol e de 3,0 a 80 por segundo e a
	/// cura e de 1,67 -- ou seja **a cura e um TERCO do dano no caso mais leve**, e o piso de
	/// <see cref="Jandirus.Core.World.CalorDaEstrela.FatorMinimo"/> so faz sentido contra ela.
	///
	/// Uma bancada que medisse "quanto tempo o corpo aguenta dentro do sol" sem regeneracao mediria
	/// outro jogo. E copiar as cinco linhas pra dentro da bancada seria justamente o "atalho
	/// paralelo" que a regra 0.7 recusa: testaria a copia, e no dia em que a cura mudasse aqui, a
	/// bancada continuaria verde medindo a cura antiga. Uma casa so, dois chamadores.
	/// ==============================================================================
	/// </summary>
	private void RegenerarPassivo(ServerPlayer pl, double dt)
	{
		CombatState? c = pl.Combate;
		if (c == null || pl.Ficha.dead) return;

		// ============================ AS TRES GUARDAS QUE SAIRAM DAQUI, E PRA ONDE FORAM ============================
		//   * `pl.Ficha.KO` -- SAIU. Com o nocaute virando o coma do DM, e a cura que decide a hora de
		//     levantar; um corpo que nao regenera caido nao acorda nunca. Ver `CombatState.Tick`.
		//   * `c.EmCombate > 0` -- foi pra dentro do `Regeneracao.PodeCurar`, porque **ela nao vale pra
		//     todo mundo**: o `fastRegen` do genoma a dispensa (`(!combatTag || fastRegen)`,
		//     `Injuries.dm:251/290/293`). E literalmente o pedido do dono -- so quem tem o eixo cura no
		//     meio da briga -- e escrito no unico lugar do port que decide isso.
		//   * `HP >= 99.99` -- foi pra dentro do canal 2 (`Regeneracao.TaxaEspalhada`), que e o unico
		//     que a tem no original. Aqui em cima ela apagava o canal do MEMBRO: um corpo com media 100
		//     e um dedo quebrado nao costurava o dedo.
		// E entrou uma que nao existia: `has_regen_energy()` (fome + vigor), tambem dentro do `PodeCurar`.
		// ======================================================================================================
		Regeneracao.Resultado r = Regeneracao.Tique(
			c.Corpo, c.Corpo.Regen, pl.Ficha, c.EmCombate > 0, ref c.BufferDeMembro, dt, _rng);

		if (!r.Curou && r.MembroDeVolta == null) return;
		c.SincronizarVida();

		if (r.MembroDeVolta == null) return;

		// O MEMBRO QUE VOLTOU SOZINHO -- so racas de `canheallopped` chegam aqui (Majin, Bio, Kai,
		// Meta, Tsujin, Yardrat). Mexe no rabo, entao o ritmo de treino do Saiyajin tem que ser
		// reavaliado pelo mesmo funil que a skill ativa usa.
		AjustarGanhoDoRabo(pl);
		pl.CorpoEnviado = "";   // forca o proximo `MandarCorpo`: um membro que volta nao pode esperar ruido
		Avisar(pl, $"seu {r.MembroDeVolta.Nome} volta a crescer.");
		GD.Print($"[server] {pl.Name} regenerou {r.MembroDeVolta.Nome} SOZINHO "
				 + $"(Regeneration {c.Corpo.Regen.Regeneration:0})");
	}

	/// <summary>
	/// ============================ A VOLTA DO OUTRO MUNDO -- o `ReviveMe()` do DM ============================
	/// `mob/proc/ReviveMe()` (`Death.dm:143-161`). Ele **desfaz tudo o que a morte fez**, e a lista e
	/// conferida item a item contra o `IrProAlem`:
	///
	///   | o que a morte fez                     | o que desfaz                                  |
	///   |---------------------------------------|-----------------------------------------------|
	///   | `dead = true`                         | `Reviver()`                                   |
	///   | a AUREOLA (`overlayList += 'Halo'`)   | e `dead`: apaga junto, sem uma segunda linha  |
	///   | levou o corpo pro alem                | `MandarProBerco`/`Correction` (o funil `DestinoDe`) |
	///   | deitou o corpo (a pose de cadaver)    | `Reviver()` limpa `dead` e `KO`               |
	///   | membros destrocados                   | `Corpo.Restaurar()` dentro do `Reviver`       |
	///   | o rabo, e com ele o ritmo de treino   | `AjustarGanhoDoRabo`                          |
	///   | tirou o preso da Sala do Tempo        | ja limpo no `IrProAlem` -- **nao volta**      |
	///   | o relogio da morte                    | zerado aqui                                   |
	///
	/// O QUE O DM FAZ E ESTE PORT AINDA NAO: `kaiTrainingAllowed = 0` (a licenca do Enma pra cruzar
	/// a barreira espiritual), `hakai_mark = 0`, `pk_karma_taken = 0` e `pk_rep_taken = 0`. Os quatro
	/// campos **nao existem aqui** -- eles nascem com o Enma e com o karma por PK, que sao a etapa
	/// seguinte. Ficam listados pra a ausencia ser uma pendencia e nao um esquecimento.
	///
	/// **VIDA PELA METADE, E NAO INTEIRA.** Divergencia do DM (`SpreadHeal(100,1,1)` la), e ela e do
	/// port desde antes desta mudanca. Quem chega ao Outro Mundo chega curado -- isso E do DM, e
	/// esta no `IrProAlem`; o que a volta a vida entrega e metade, que e o preco que este port cobra.
	/// ====================================================================================================
	///
	/// ============================ ERA A TERRA CRAVADA, E ESSE E O BUG QUE NAO APARECE ============================
	/// Este metodo mandava todo mundo pra `SpawnZone` -- a Terra. Ligar o berco so no NASCIMENTO
	/// teria deixado o jogador nascendo em casa e ressuscitando na Terra, e ninguem perceberia por
	/// semanas: sao dois caminhos diferentes no codigo pra a mesma pergunta do jogador ("onde eu
	/// apareco?"). Por isso os dois saem do mesmo funil (`GameServer.Berco.cs`), que e o que o DM
	/// tinha num lugar so (`mob/proc/Locate()`).
	/// ========================================================================================================
	///
	/// O JULGAMENTO DO ENMA (o revive por Zeni com o debuff de uma hora, a reencarnacao a 10% do BP,
	/// o Inferno por karma negativo) e a etapa seguinte -- e e ele que substitui o prazo automatico
	/// de <see cref="Jandirus.Core.World.Alem.MsNoAlem"/>, nao que se soma a ele.
	/// </summary>
	private void Renascer(ServerPlayer pl)
	{
		// MORRER DEVOLVE O CORPO INTEIRO, rabo incluso -- e o `Restaurar()` do Body. O ritmo
		// de treino precisa voltar junto, senao o Saiyajin ressuscitado ficaria com o bonus
		// de quem nao tem rabo e o rabo de volta no lugar.
		pl.Combate.Reviver(0.5, SegundosDeCarencia);
		AjustarGanhoDoRabo(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi * 0.5;
		pl.RelogioDaMorte = 0;

		// ============================ A SALA DO TEMPO JA FOI DESTRANCADA, LA ATRAS ============================
		// **Decisao do dono, e nao um buraco**: depois de preso, *"so morrendo pra sair"*. A chamada de
		// `AMorteSaiDaSala` MORAVA AQUI, e estava certa enquanto morrer e renascer eram o mesmo
		// instante. Com a viagem no meio ela subiu pro `IrProAlem` (`GameServer.Alem.cs`), que e o
		// primeiro passo que de fato tira o corpo da sala -- deixa-la aqui manteria o morto marcado
		// como preso durante o minuto inteiro no Outro Mundo.
		//
		// NAO A REPONHA AQUI "por seguranca": ela seria a segunda copia, e a `SalaSessao` que ela
		// esquece (`EsquecerSessaoDaSala`) ja teria sido apagada uma vez.
		// ================================================================================================

		// `DestinoDe` e o funil de cima -- ele olha o DOMINIO escolhido como renascimento antes de
		// cair no berco. Ver `GameServer.Conquista.cs`.
		(ZoneKey destino, Vec2 onde) = DestinoDe(pl);

		// O DESTINO VAI JUNTO, e nao e economia: `DestinoDe` conta ao jogador quando o berco dele nao
		// existe mais (o REFUGIO, ver `GameServer.Refugio.cs`), e perguntar duas vezes na mesma morte
		// mandava a mesma frase duas vezes pro chat. Ver a sobrecarga do `MandarProBerco`.
		if (pl.Zone.Hash != destino.Hash) MandarProBerco(pl, (destino, onde));
		else
		{
			// MESMA ZONA: nao ha o que recarregar, so um teleporte dentro dela. O ponto vem do
			// funil do mesmo jeito -- num mundo gerado ele e a chegada que o gerador garantiu
			// livre, e num pre-feito e a celula livre mais perto do (249,250).
			pl.Pos = onde;
			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.Put(pl.SeqInput);   // O PACOTE TEM DUAS PARTES desde o conserto do dash:
			// sequencia + posicao. Escrever so a posicao aqui mandava um pacote MALFORMADO --
			// o cliente leria a coordenada X como se fosse a sequencia.
			w.PutVec(pl.Pos);
			pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}

		GD.Print($"[server] {pl.Name} renasceu em {destino.Name} ({SegundosDeCarencia:0}s de carencia)");
	}
}
