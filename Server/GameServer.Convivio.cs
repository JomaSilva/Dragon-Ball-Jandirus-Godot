using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Ranks;
using Jandirus.Core.Social;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ O CONVIVIO -- O KNOWN-PEOPLE DO DM, PORTADO ============================
/// Dois arquivos de la moram aqui: `Code/Contacts.dm` (quem eu conheco, a foto da ficha dele, o que
/// declarei que ele e pra mim) e o que sobrou de `Code/Modules/Players/Friendship.dm` (o passo de
/// proximidade). As REGRAS moram no `Core.Social.Convivio`, que nao conhece rede nem Godot; aqui esta so
/// o que e do servidor: quem esta perto de quem, quem ouviu quem, quem bateu em quem, e o que isso causa.
/// ====================================================================================================
///
/// ============================ SAO DUAS COISAS, E CADA UMA TEM UM DONO SO ============================
///   * o CONVIVIO -- o JOGO conta: ficar perto (um ponto a cada 30 s a menos de 6 tiles), conversar
///     (1% por fala ouvida) e lutar (golpe, queda, morte). Ele nao tem rotulo e nao conclui nada;
///   * a DECLARACAO -- o JOGADOR escolhe o que cada conhecido e pra ele (aba People), e o convivio paga.
///
/// A AFINIDADE (os pontos de amizade automaticos, o "inimigo" que o jogo declarava sozinho, o odio, os
/// rivais e o pedido de amizade) SAIU em 2026-10-10, a pedido do dono -- ver o cabecalho do `Convivio`,
/// onde esta escrito o que cada leitor dela virou.
/// ====================================================================================================
///
/// ============================ E ELE E O QUE DESTRAVA O SUPER SAIYAJIN ============================
/// O tronco Saiyajin (SSJ1/SSJ2), a linha Legendary inteira e o Beast pedem RAIVA
/// (<see cref="Catalogo.RaivaExigida"/>), e a raiva so acende por um gancho:
/// <see cref="AmigoAbatido"/>. Quem responde "quem e amigo de quem" e este arquivo -- pela DECLARACAO de
/// quem assiste --, e o chamador e o <see cref="LutoNaVizinhanca"/>.
/// ==================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 1. ESTAR PERTO -- o `accrue_friendship()` (Friendship.dm:29-44), rendendo CONVIVIO
	// =====================================================================

	/// <summary>Os 6 tiles do `FRIEND_RANGE`, em pixels.</summary>
	private const float RaioDeConvivio =
		Convivio.AlcanceDeConvivioTiles * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// ESTE CORPO ENTRA NA VIDA SOCIAL DE ALGUEM?
	///
	/// ============================ POR QUE A ASSINATURA, E NAO O `Peer` ============================
	/// A pergunta parece ser "e um jogador de verdade?", e a resposta obvia seria `Peer != null`.
	/// Ela esta ERRADA por dois lados:
	///
	///   * e FRACA demais como criterio: o que faz alguem lembravel nao e ter uma conexao aberta, e
	///     ter IDENTIDADE -- e um vinculo nao acaba quando a pessoa desloga. O DM pergunta as duas
	///     coisas (`if(!client || isNPC || !signature)`) porque la o laco varre `view()` e pega
	///     corpos que nem existem aqui;
	///   * e o `!signature` de la ja cobre o caso que importa neste port: **corpo sem dono nao tem
	///     conta**, entao clone da meditacao, NPC e Oozaru selvagem tem assinatura vazia por
	///     construcao (ver `GameServer.Clone.cs:95`, `Peer = null` e nenhuma `Conta`).
	///
	/// E ha uma terceira razao, pratica: `Peer` e um socket, e bancada nenhuma consegue forjar um.
	/// Com o teste amarrado nele, o luto -- que e o que destrava o SSJ1 -- seria justamente a parte
	/// do sistema que nenhuma bancada consegue exercitar.
	/// ==============================================================================================
	///
	/// ============================ E O CIDADAO? **NAO, E A DECISAO E ESCRITA AQUI** ============================
	/// O dono perguntou se NPC entra nisso. Nao entra, e por tres razoes que se somam:
	///
	///   * ele nao TEM identidade: `Assinatura` sai de conta+slot (`GameServer.cs:1223`), e cidadao,
	///     Rei, chefe de saga e clone nascem sem conta -- a primeira metade ja os corta;
	///   * ele nao SOBREVIVE: o povoamento repoe habitante (`Povoamento`), entao a lista de conhecidos de
	///     um jogador viraria um cemiterio de gente que o mundo reciclou;
	///   * e -- o que decide -- **declarar amigo um NPC seria uma fabrica de Super Saiyajin**. O luto e a
	///     porta do SSJ1, e ha cidadao infinito e barato de matar. Um jogador com dois amigos NPC no
	///     quintal acenderia furia sempre que quisesse.
	///
	/// A SEGUNDA DEFESA E O <see cref="Jandirus.Core.Npc.Gente.EhNpcDoMundo"/>, e ela e a mesma que o
	/// DM tem: la as varreduras sociais correm sobre `player_list` (so jogador) E ainda levam um
	/// `!M.client` por cima (`BossEvents.dm:373-374`) -- **duas** defesas. Aqui a assinatura era a
	/// unica, e "corpo do mundo com conta" e exatamente o tipo de corpo que uma bancada ou um verb de
	/// admin cria sem querer. Ver o cabecalho de `Core/Npc/Gente.cs`.
	///
	/// (O `EhJogador` do mesmo arquivo **nao serve aqui**, e por escrito: ele exige dono na tela, e
	/// um vinculo nao acaba quando a pessoa desloga -- ver o bloco acima.)
	/// ==========================================================================================================
	/// </summary>
	private static bool EhPessoa(ServerPlayer pl) =>
		pl.Assinatura.Length > 0
		&& !Jandirus.Core.Npc.Gente.EhNpcDoMundo(pl.Peer != null, pl.Papel);

	/// <summary>
	/// UM PASSO DE PROXIMIDADE POR JOGADOR, a cada 3 segundos: quem esta a menos de 6 tiles entra na
	/// lista (a foto da ficha) e o par da um passo de convivio -- dez passos, um ponto
	/// (`Convivio.ConviverPerto`).
	///
	/// ============================ POR QUE A CADENCIA E POR PESSOA ============================
	/// No DM cada mob roda o proprio `GlobalStats` e o proprio `friend_tick`; aqui ha um laco so.
	/// Um contador do servidor faria todos os pares renderem no MESMO tique -- o que nao muda o
	/// resultado, mas faz o custo do sistema inteiro cair sempre no mesmo quadro. Com o prazo no
	/// jogador, os passos se espalham sozinhos conforme cada um entra.
	///
	/// A VARREDURA E POR ZONA e nao pelo mundo: o `ZoneList` ja e a lista de quem pode se ver, e
	/// varrer `_players` inteiro pra depois medir a distancia seria comparar quem esta em planetas
	/// diferentes -- N^2 sobre o servidor todo por causa de um raio de 6 tiles.
	/// =========================================================================================
	///
	/// A ABA SO E REFEITA QUANDO ALGO QUE ELA DESENHA MUDOU (alguem novo na lista, um ponto de convivio):
	/// uma lista por passo seriam vinte pacotes por minuto pra cada jogador com alguem ao lado, e a pagina
	/// da aba remontada debaixo do mouse a cada 3 s.
	/// </summary>
	private void TickDoConvivio()
	{
		long agora = NowMs();
		float raio2 = RaioDeConvivio * RaioDeConvivio;

		foreach (ServerPlayer pl in _players.Values)
		{
			// `if(!client || isNPC || !signature) return` -- `Friendship.dm:30`. Corpo sem dono
			// (clone da meditacao, NPC, corpo forjado de bancada) nao convive nem entra na
			// lista de ninguem: ele nao tem assinatura pra ser lembrado por.
			if (!EhPessoa(pl)) continue;
			if (agora < pl.ProximaAproximacao) continue;
			pl.ProximaAproximacao = agora + (long)(Convivio.SegundosEntreAproximacoes * 1000);

			bool mudou = false;
			foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			{
				if (o == pl || !EhPessoa(o)) continue;

				Jandirus.Core.World.Vec2 d = o.Pos - pl.Pos;
				if (d.X * d.X + d.Y * d.Y > raio2) continue;

				// A ORDEM IMPORTA: fotografar primeiro poe a pessoa na minha lista, e e a FICHA que
				// guarda o convivio. Invertido, o primeiro passo de cada dupla nao teria onde cair.
				mudou |= Fotografar(pl, o, agora);

				// CADA LADO DA O SEU PASSO: o prazo (`ProximaAproximacao`) e por pessoa, entao os dois
				// ganham o ponto com segundos de diferenca -- e cada um le a propria frase quando a porta
				// do "amigo" abre pra ele.
				if (!pl.Social.ConviverPerto(o.Assinatura, out bool abriu)) continue;
				mudou = true;
				if (abriu) AvisarAPortaDoAmigo(pl, o.Name);

				// O PONTO QUE ACABOU DE ENTRAR PODE SER O QUE PAGA OS DETALHES: a foto de cima rodou com o
				// convivio de antes, e sem isto quem abrisse o cartao nos 3 s seguintes leria "voce ainda nao
				// ouviu a historia" de alguem que esta ao lado.
				AprenderDetalhes(pl, o, agora);
			}
			if (mudou) MandarConhecidos(pl);
		}
	}

	/// <summary>
	/// A FOTOGRAFIA DA FICHA DE ALGUEM -- o `update_my_contact()` (`Contacts.dm:6-39`). Devolve TRUE no
	/// primeiro encontro (a pessoa acabou de entrar na lista).
	///
	/// Um so lugar monta o `Conhecido` porque a foto e o que a aba People le, e o defeito classico
	/// aqui seria uma segunda copia que fotografa menos campos: a pessoa vista pela conversa
	/// apareceria sem raca, e a vista pela proximidade com raca -- e nada acusaria.
	///
	/// E A FOTO LEVA OS DETALHES JUNTO (a historia, o cargo) pra quem o convivio ja paga -- ver
	/// <see cref="AprenderDetalhes"/>.
	/// </summary>
	private bool Fotografar(ServerPlayer eu, ServerPlayer outro, long agora)
	{
		// SEM A CLASSE, de proposito: ela nunca aparece pra ninguem (`GameServer.Sigilo.cs`, regra 2), e o que
		// a foto nao guarda nao tem como vazar. Ver o cabecalho do `Conhecido`.
		bool novo = eu.Social.Fotografar(outro.Assinatura, outro.Name, outro.Race, outro.Genero, outro.Idade, agora);
		AprenderDetalhes(eu, outro, agora);
		return novo;
	}

	/// <summary>
	/// O QUE SE APRENDE DE ALGUEM ESTANDO COM ELE: a historia que ele escreveu na criacao e o cargo que
	/// carrega entram na ficha que EU guardo dele -- e so se o meu convivio com ele ja paga os detalhes
	/// (`Convivio.ConvivioParaDetalhes`; abaixo disso a historia de ninguem e copiada pra lugar nenhum).
	///
	/// E FOTOGRAFIA, como o resto da ficha: o que fica guardado e o que a pessoa ERA neste encontro. Os
	/// detalhes que a aba mostra saem DAQUI (`VerboDetalhes`), e nao do corpo vivo dela -- senao abrir a aba
	/// contaria, de qualquer lugar do mundo, que alguem foi promovido ou envelheceu.
	///
	/// O CARGO E PUBLICO (o painel de cargos ja diz o nome de quem ocupa cada um); o que NAO entra aqui e
	/// tudo o que o `GameServer.Sigilo.cs` esconde: poder, atributos, classe, lugar, mochila.
	/// </summary>
	private void AprenderDetalhes(ServerPlayer eu, ServerPlayer outro, long agora)
	{
		if (!eu.Social.PodeVerDetalhes(outro.Assinatura)) return;
		eu.Social.AprenderDetalhes(outro.Assinatura, outro.Historia,
								   Cargos.Get(CargoDe(outro.Conta))?.Nome ?? "", agora);
	}

	/// <summary>
	/// "JA DA PRA DIZER QUE ELE E SEU AMIGO" -- dito UMA vez por pessoa, no ponto de convivio que atravessa
	/// o preco do "amigo" (o `Convivio` so devolve TRUE nessa travessia). E a frase que ocupa o lugar do
	/// antigo *"voce o considera um amigo"*: o jogo nao considera mais nada, ele avisa que a escolha abriu.
	///
	/// So esta porta e anunciada (e nao as seis): e a que muda regra quando usada -- a furia. E o texto
	/// fala das DUAS declaracoes que os 50 pagam, porque o convivio tambem sobe apanhando.
	/// </summary>
	private void AvisarAPortaDoAmigo(ServerPlayer pl, string nome) =>
		Avisar(pl, $"você já conviveu com {nome} o bastante pra dizer o que ele é pra você -- amigo, "
				   + "ou inimigo mortal. A escolha é sua, na aba People (tecla P).");

	// =====================================================================
	// 2. CONVERSAR E LUTAR -- as outras duas fontes do convivio
	// =====================================================================

	/// <summary>
	/// ALGUEM ME OUVIU FALAR -- o `TestListeners` (`Talking.dm:210-219`). Chamado do <see cref="Mandar"/>,
	/// ou seja, do UNICO ponto por onde uma fala de personagem chega a alguem.
	///
	/// ============================ POR QUE AQUI DENTRO, E NAO NO `Falar` ============================
	/// O `Falar` tem quatro saidas (OOC, sussurro em dois raios, grito, e o resto), e este port ja
	/// pagou varias vezes o preco de "ligar num caminho e esquecer o outro". O `Mandar` e o funil:
	/// se a mensagem chegou em alguem, ela passou por aqui. Nao ha caminho de fala que escape.
	///
	/// E OS DOIS CANAIS QUE NAO CONTAM: <see cref="Protocol.Fala.Sistema"/> (recado do servidor, nao
	/// ha ninguem falando) e <see cref="Protocol.Fala.Ooc"/> (conversa de JOGADOR, atravessa planeta
	/// -- deixa-lo contar faria duas pessoas em galaxias diferentes se conhecerem por escrito).
	/// ================================================================================================
	///
	/// NOS DOIS SENTIDOS, como no DM (`add_familiarity(usr)` e `usr.add_familiarity(src)`): ouvir
	/// alguem aproxima os dois, nao so o ouvinte.
	/// </summary>
	private void Ouviu(ServerPlayer ouvinte, ServerPlayer falante)
	{
		if (ouvinte == falante || !EhPessoa(ouvinte) || !EhPessoa(falante)) return;
		if (_rng.NextDouble() >= Convivio.ChanceDeFamiliaridadePorFala) return;
		ConhecerMelhor(ouvinte, falante, 1);
	}

	/// <summary>
	/// TROCAR GOLPES TAMBEM E CONHECER (dono, 2026-09-05): 10% por golpe que encosta, nos dois lados --
	/// ver `Convivio.ChanceDeFamiliaridadePorGolpe`. Chamado do desfecho do golpe (`GameServer.Combat.cs`).
	/// </summary>
	private void ConvivioPorGolpe(ServerPlayer vitima, ServerPlayer autor)
	{
		if (!EhPessoa(vitima) || !EhPessoa(autor) || vitima == autor) return;
		if (_rng.NextDouble() >= Convivio.ChanceDeFamiliaridadePorGolpe) return;
		ConhecerMelhor(vitima, autor, 1);
	}

	/// <summary>
	/// CAIR PELA MAO DE ALGUEM E CONHECE-LO (dono, 2026-09-05): a queda vale 10 e a morte 20, nos dois
	/// lados -- e vale igual entre amigos que treinam e entre inimigos, porque o convivio so conta o que
	/// foi vivido. O que a pessoa E pra voce depois disso continua sendo escolha sua.
	/// </summary>
	private void ConvivioPorDerrota(ServerPlayer vitima, ServerPlayer algoz, bool morreu)
	{
		if (!EhPessoa(vitima) || !EhPessoa(algoz) || vitima == algoz) return;
		ConhecerMelhor(vitima, algoz, morreu ? Convivio.FamiliaridadePorMorte : Convivio.FamiliaridadePorQueda);
	}

	/// <summary>
	/// OS DOIS PASSAM A SE CONHECER MELHOR, nos dois sentidos: a foto da ficha (sem ela nao ha onde somar),
	/// os pontos de convivio, o aviso da porta do amigo pra quem a atravessou -- e a aba People dos dois e
	/// refeita. E o funil unico da conversa e da luta.
	/// </summary>
	private void ConhecerMelhor(ServerPlayer a, ServerPlayer b, int quanto)
	{
		long agora = NowMs();
		Fotografar(a, b, agora);
		Fotografar(b, a, agora);
		if (a.Social.SomarFamiliaridade(b.Assinatura, quanto)) AvisarAPortaDoAmigo(a, b.Name);
		if (b.Social.SomarFamiliaridade(a.Assinatura, quanto)) AvisarAPortaDoAmigo(b, a.Name);
		// DE NOVO, DEPOIS DA SOMA: a foto de cima rodou com o convivio de ANTES, e uma queda pode ser o
		// ponto que paga os detalhes -- os dois estao um na frente do outro agora.
		AprenderDetalhes(a, b, agora);
		AprenderDetalhes(b, a, agora);
		MandarConhecidos(a);
		MandarConhecidos(b);
	}

	// =====================================================================
	// 3. O LUTO -- **O CHAMADOR DE PRODUCAO DO `AmigoAbatido`**
	// =====================================================================

	/// <summary>
	/// A QUE DISTANCIA SE "VE" ALGUEM CAIR.
	///
	/// ============================ E O ALCANCE DA FALA, E NAO UM NUMERO NOVO ============================
	/// O dono disse "morre NA FRENTE dele", e o DM escreve isso como `for(var/mob/M in view())` --
	/// o `view()` sem argumento, que e o `world.view` do servidor. Este port ja traduziu esse mesmo
	/// `view()` uma vez, pro alcance da fala: <see cref="RaioDaVista"/> = 22 tiles = 704 px
	/// (`GameServer.Chat.cs:28`, com a conta explicada la). Entao a resposta pra "qual alcance?" e:
	/// **o mesmo em que voce ouviria a pessoa dizer alguma coisa** -- que e o significado de estar
	/// na mesma cena.
	///
	/// Cravar um numero proprio aqui daria duas verdades sobre "quem estava presente", e a primeira
	/// vez que alguem mudasse a vista do jogo so uma das duas mudaria.
	/// ====================================================================================================
	/// </summary>
	private const float RaioDeTestemunha = RaioDaVista;

	/// <summary>
	/// ============================ ALGUEM CAIU, E QUEM GOSTAVA DELE ESTAVA VENDO ============================
	/// Ele porta dois lacos do DM que sao o mesmo laco com listas diferentes:
	///
	///   * `Death.dm:73-85` -- o amigo MORREU: <see cref="NivelDeRaiva.Extrema"/>;
	///   * `KO.dm:29-41`    -- o amigo CAIU:   <see cref="NivelDeRaiva.Comum"/>.
	///
	/// E os dois graus valem os dois, e nao so a morte: **o nocaute abre as mesmas formas de raiva que a
	/// morte, por um preco de BP maior** (`FormaDef.AgioNaRaivaComum`, dono 2026-10-11 -- ate ali so a
	/// linha Legendary se contentava com ele). Ligar so a morte deixaria metade da regra do dono sem
	/// chamador.
	///
	/// O QUE CADA GRAU VALE e outra regra do dono (2026-10-11): o do nocaute e a raiva COMUM, o da morte
	/// e o mais alto -- so ele tem cinematica, tema e o salto de Ki. Ver `Core.Combat.RaivaEmDegraus`.
	///
	/// ============================ AS TRES CONDICOES, TODAS DO DM ============================
	///   1. **O ALGOZ TEM QUE SER INIMIGO DA VITIMA** (`Death.dm:75`, `KO.dm:32-35`). Sem isto,
	///      dois amigos treinando viram uma fabrica de Super Saiyajin: cada nocaute do treino
	///      enfureceria a plateia inteira. O proprio DM diz por que: *"no rage for friendly duels
	///      or environmental death"*.
	///   2. **QUEM DERRUBOU NAO SE ENFURECE COM O PROPRIO CRIME** (`Death.dm:78`, `KO.dm:35`) --
	///      e a declaracao pode ser de mao unica, entao nao adianta confiar na lista dele.
	///   3. **TEM QUE ESTAR VENDO** -- o <see cref="RaioDeTestemunha"/>, e na MESMA zona.
	/// =========================================================================================
	///
	/// ============================ E QUEM RESPONDE E A DECLARACAO DE CADA UM ============================
	/// "Era inimigo?" e perguntado a lista da VITIMA; "eu gostava dela?" a de quem ASSISTE. As duas
	/// respostas saem do que cada jogador DECLAROU (`Convivio.AlgozEhInimigo`, `LutoPorMorte`,
	/// `LutoPorQueda`): o jogo nao deduz amizade de nada -- quem nao disse que alguem e amigo nao entra em
	/// furia por ele, nem que tenham passado o dia juntos.
	/// ====================================================================================================
	///
	/// ============================ POR QUE ELE NAO PRECISA CONFERIR "ESTAVA EM COMBATE" ============================
	/// O DM precisa: la o `KO()` e chamado tambem por gravidade, fome e desmaio, e o `lastDamager`
	/// pode ser de dez minutos atras -- por isso `if(koFoe &amp;&amp; (combatTag || IsInFight))`.
	/// Aqui quem chama isto e o <see cref="AoPerderALuta"/>, e ele so e chamado de dentro da
	/// resolucao de um golpe que ACABOU de acontecer, com o autor na mao. O nocaute por gravidade
	/// nao passa por aqui. A guarda do DM esta portada -- ela e o proprio ponto de chamada.
	/// ================================================================================================================
	/// </summary>
	/// <param name="vitima">Quem caiu ou morreu.</param>
	/// <param name="algoz">Quem a derrubou. Pode ser um corpo sem dono -- e ai ele e inimigo.</param>
	/// <param name="morreu">TRUE = morte (furia extrema); FALSE = nocaute (a raiva comum, `NivelDeRaiva.Comum`).</param>
	private void LutoNaVizinhanca(ServerPlayer vitima, ServerPlayer? algoz, bool morreu)
	{
		// SEM ALGOZ NAO HA LUTO: `deathKiller` nulo no DM quer dizer morte ambiental, e o
		// `killedByEnemy` de la comeca com `killer &&`. Ninguem se enfurece com um despenhadeiro.
		if (algoz == null || algoz == vitima) return;

		// E SEM ASSINATURA TAMBEM NAO: um corpo sem dono (clone, NPC) nao esta na lista social de
		// ninguem, entao ninguem tinha como gostar dele -- e todo `LutoPor...` daria falso de
		// qualquer jeito. Sair aqui poupa a varredura da zona.
		if (!EhPessoa(vitima)) return;

		// 1. O ALGOZ ERA INIMIGO **AOS OLHOS DA VITIMA**. E a lista dela que decide, e nao a de quem
		//    assiste: quem morre e que sabe se aquilo foi um duelo entre amigos ou um assassinato.
		if (!vitima.Social.AlgozEhInimigo(algoz.Assinatura)) return;

		NivelDeRaiva grau = morreu ? NivelDeRaiva.Extrema : NivelDeRaiva.Comum;
		float raio2 = RaioDeTestemunha * RaioDeTestemunha;

		foreach (ServerPlayer o in ZoneList(vitima.Zone.Hash).ToList())
		{
			if (o == vitima || o == algoz) continue;              // (2)
			if (!EhPessoa(o)) continue;

			Jandirus.Core.World.Vec2 d = o.Pos - vitima.Pos;      // (3)
			if (d.X * d.X + d.Y * d.Y > raio2) continue;

			// A DECLARACAO DO OBSERVADOR SOBRE A VITIMA. A lista da morte e mais larga que a da queda
			// (ver `Convivio.EnfurecePorMorte` / `EnfurecePorQueda`).
			bool sente = morreu
				? o.Social.LutoPorMorte(vitima.Assinatura)
				: o.Social.LutoPorQueda(vitima.Assinatura);
			if (!sente) continue;

			// ============================ O GANCHO, ENFIM CHAMADO ============================
			// Ele devolve TRUE so na ERUPCAO (ver `AmigoAbatido`): quem ja estava em luto so tem o
			// prazo renovado, e o anuncio pra zona nao se repete. E o `wasRaging` do DM
			// (`Murder.dm:111`), que existe pra a cena de raiva nao tocar duas vezes seguidas.
			if (!AmigoAbatido(o, vitima.Name, grau)) continue;

			// O QUE OS OUTROS VEEM. Uma frase por degrau -- as do DM sao `Death.dm:83` ("EXTREMELY
			// enraged") e `KO.dm:38` ("very angry"), e a do nocaute desceu um tom com o degrau dele
			// (ver `FraseDeQuemVeARaiva`, `GameServer.Raiva.cs`).
			AnunciarRaiva(o, morreu);
		}
	}

	/// <summary>
	/// "VOCE PERCEBE QUE FULANO FICOU FURIOSO" -- pra quem estiver vendo o enlutado.
	///
	/// No DM sao duas linhas por evento (`view(M) &lt;&lt; output(...)` e `chatcast(view(M), ...)`),
	/// porque la o chat tem dois canais fisicos. Aqui o <see cref="Avisar"/> ja e o canal do
	/// cliente, entao e um envio por pessoa que ve.
	///
	/// O RAIO E O DE QUEM SE ENFURECEU, e nao o da vitima: o DM escreve `view(M)`, com M sendo o
	/// enlutado. A diferenca aparece quando alguem esta perto do amigo furioso mas longe do corpo.
	/// </summary>
	private void AnunciarRaiva(ServerPlayer enlutado, bool extrema)
	{
		float raio2 = RaioDeTestemunha * RaioDeTestemunha;
		string frase = FraseDeQuemVeARaiva(enlutado.Name, extrema);

		foreach (ServerPlayer o in ZoneList(enlutado.Zone.Hash).ToList())
		{
			if (o == enlutado) continue;
			Jandirus.Core.World.Vec2 d = o.Pos - enlutado.Pos;
			if (d.X * d.X + d.Y * d.Y > raio2) continue;
			Avisar(o, frase);
		}
	}

	/// <summary>
	/// ============================ O FUNIL UNICO DA DERROTA ============================
	/// Perder uma luta tem VARIAS consequencias no port (Zenkai, luto, convivio, sucessao, karma...), e
	/// todas respondem a mesma pergunta ("fulano acabou de perder pra beltrano"), nos MESMOS quatro
	/// pontos. Pendurar cada uma ao lado das outras, ponto a ponto, seria criar quatro lugares onde a
	/// proxima pessoa pode ligar uma e esquecer a outra -- e esse e, literalmente, o modo de falha que
	/// mais se repetiu neste port.
	///
	/// Por isso os quatro pontos chamam ISTO. O dia em que a derrota tiver mais uma consequencia, ela
	/// entra aqui e chega nos quatro de graca.
	/// =================================================================================
	/// </summary>
	/// <param name="morreu">
	/// FALSE = nocaute, TRUE = morte. Um golpe que nocauteia E mata chama as duas vezes (e como o
	/// DM: `Death()` chama `KO(-1)` antes de matar), e o `AmigoAbatido` sabe lidar -- as duas
	/// janelas de raiva sao independentes e a extrema ja satisfaz a comum.
	/// </param>
	private void AoPerderALuta(ServerPlayer vitima, ServerPlayer algoz, bool morreu)
	{
		// ============================ NADA DISTO ACONTECE DENTRO DE UMA MENTE ============================
		// *"la dentro os FERIMENTOS NAO SAO TRANSFERIDOS pro corpo real, NAO TEM COMO TER ZENKAI"* --
		// e o pedido nomeia so o Zenkai, mas as OUTRAS consequencias deste funil sao piores se
		// escaparem:
		//
		//   * o ZENKAI -- a regra 2 do pedido, cobrada exatamente aqui. Sem este corte, "perder de
		//     proposito pro proprio reflexo" seria a maneira mais barata de ganhar BP no jogo, uma vez
		//     por hora, pra sempre;
		//   * o LUTO (`LutoNaVizinhanca`) -- **e este e o mais perigoso**. Um amigo "morrendo" numa luta
		//     SIMULADA acenderia raiva de verdade, e raiva de verdade abre SSJ. Duas pessoas na mesma
		//     mente poderiam fabricar a transformacao mais cara do jogo se matando de mentira;
		//   * o CONVIVIO (`ConvivioPorDerrota`) -- pontos de vida em comum por uma luta que nao aconteceu;
		//   * a SUCESSAO (`SucessaoPorMorte`) -- o trono de Vegeta trocando de dono porque alguem
		//     "morreu" dentro da propria cabeca. Este seria irreversivel.
		//
		// O CORTE E AQUI, NO FUNIL, e nao um `if` dentro de cada uma: e literalmente o
		// argumento que criou este metodo ("quatro lugares onde a proxima pessoa pode ligar uma e
		// esquecer a outra"), e a proxima consequencia da derrota ja nasce cortada.
		//
		// PELA ZONA DA VITIMA: o algoz de dentro da mente e sempre um corpo do mesmo bolso (o reflexo,
		// a lembranca, ou a outra pessoa em transe), entao perguntar por um dos dois basta -- e a
		// vitima e quem paga todas as contas.
		//
		// **E O `ZenkaiPorDerrota` NAO GANHOU UM SEGUNDO GATE**, apesar de ter dois chamadores fora
		// deste funil (`GameServer.Tecnicas.G3.cs:1209` e `:1216`). Os dois sao o caso `autor ==
		// vitima` -- morrer da propria explosao --, e ali o `GainZenkai` ja recusa antes de qualquer
		// coisa: `poderInimigo <= meuPoder` quando o inimigo e voce mesmo. Escrever a regra de novo la
		// seria a segunda copia que um dia discorda desta, pra fechar uma porta que nao abre.
		// ============================================================================================
		if (NaMente(vitima)) return;

		// NEM NUM TREINO COM O BILLS OU O WHIS (dono, 2026-10-11): la o adversario tem sempre o triplo do poder de
		// quem treina, e cair e a aula -- com as consequencias de derrota, apanhar de proposito viraria fabrica de
		// Zenkai. Pelo mesmo argumento do corte de cima, ele mora no funil. Ver `GameServer.BillsTreino.cs`.
		if (TreinoDivinoEntre(vitima, algoz)) return;

		// O VILAO AVULSO QUE CAIU: nocauteado ou morto por um jogador, o karma vai pra esse jogador e o evento
		// acaba. Sai na primeira linha quando a vitima nao e vilao de evento nenhum. Ver `GameServer.Viloes.cs`.
		VilaoFoiDerrotado(vitima, algoz, morreu);

		// ============================ CAIR SEPARA A FUSAO ============================
		// `defuse_on_downed()` (`Fusion.dm:75`), chamado do `KO()` do original. Ela entra AQUI e nao
		// num `if` no `ResolverDesfecho` pelo mesmo argumento que criou este funil -- e com um agravante
		// proprio: nocaute e morte sao DOIS caminhos, e a fusao tem que se desfazer nos dois. Escrita
		// em um so, morrer fundido deixaria a fusao de pe sobre um cadaver.
		//
		// VEM ANTES DO ZENKAI de proposito. O Zenkai le o poder da vitima pra decidir quanto dar, e
		// **o corpo fundido nao e a vitima**: sem separar primeiro, quem apanhou de uma fusao levaria
		// o `(A+B)*2` como "o poder do inimigo" e o corpo que caiu ganharia Zenkai sobre um numero que
		// deixou de existir no instante seguinte.
		// ==============================================================================
		FusaoAoCair(vitima, morreu ? "o corpo fundido tombou" : "o corpo fundido foi derrubado");

		// COM BICHO NO MEIO A DERROTA ACABA AQUI (2026-10-10): apanhar de um lobo nao da Zenkai nem acende o
		// luto de quem viu, e abater um bicho nao e matar ninguem -- sem karma, sem convivio, sem sucessao. A fusao
		// que se desfaz na queda (acima) continua valendo. Ver `DerrotaComBicho` (`GameServer.Animais.cs`).
		// E O BICHO QUE CAIU MORRE AQUI, no instante do nocaute -- ver `BichoCaiu`.
		if (DerrotaComBicho(vitima, algoz))
		{
			if (!morreu) BichoCaiu(vitima);
			return;
		}

		ZenkaiPorDerrota(vitima, algoz);
		LutoNaVizinhanca(vitima, algoz, morreu);

		// O CONVIVIO DOS DOIS SOBE -- e so isso: a derrota nao muda o que um E pro outro (nao ha mais
		// pontos de amizade pra cair nem inimigo declarado sozinho; ver o cabecalho do `Convivio`).
		// DEPOIS DO LUTO de proposito: a furia desta queda e decidida pelo que ja estava declarado
		// antes dela.
		ConvivioPorDerrota(vitima, algoz, morreu);

		// A SUCESSAO. Matar quem carrega o trono de Vegeta ou o titulo de Lorde do Gelo troca o dono
		// do cargo (`Murder.dm:85-101`) -- e so a MORTE conta, nunca o nocaute, que e o que o DM faz
		// ao pendurar a regra dentro do `killer_stuff`. Ver `GameServer.CargoPortas.cs`.
		if (morreu) SucessaoPorMorte(vitima, algoz);

		// E O KARMA, que vem do MESMO `killer_stuff` (`Murder.dm:83`). Matar um inocente
		// suja a alma, matar um assassino a limpa -- e e este eixo que decide nove dos cargos
		// reivindicaveis. So a MORTE conta, pelo mesmo motivo da sucessao. Ver `GameServer.Karma.cs`.
		if (morreu) KarmaPorMatarJogador(vitima, algoz);
	}

	// =====================================================================
	// 4. OS VERBOS
	// =====================================================================

	/// <summary>`set src in view(10)` -- o `Set_Contact` (`Contacts.dm:3`).</summary>
	private const float AlcanceDeAnotar = 10 * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>
	/// Os verbos sociais. Devolve `true` quando o comando era deste canal -- inclusive quando foi
	/// RECUSADO, pela mesma razao do `ComandoDeInteracao`: senao o funil segue e o jogador le
	/// "comando desconhecido" depois de ja ter lido "longe demais".
	/// </summary>
	private bool ComandoDeConvivio(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "conhecidos": MandarConhecidos(pl); return true;
			case "conhecer": VerboAnotar(pl, arg); return true;
			case "relacao": VerboRelacao(pl, arg); return true;
			case "detalhes": VerboDetalhes(pl, arg); return true;
			case "esquecer": VerboEsquecer(pl, arg); return true;
			default: return false;
		}
	}

	/// <summary>
	/// O `Set_Contact` (`Contacts.dm:1-4`): anota alguem na mao, sem esperar a convivencia.
	///
	/// O ALVO VEM POR ID e nao por nome digitado, como todo o resto do port: o jogador ja marca
	/// gente com duplo clique, e nome se repete. E o equivalente honesto do `set src in view(10)`
	/// do BYOND, que tambem era "aquele ali, o que eu estou vendo".
	/// </summary>
	private void VerboAnotar(ServerPlayer pl, string arg)
	{
		ServerPlayer? o = PorNome(arg);
		if (o == null || o == pl) { Avisar(pl, "marque alguém antes (duplo clique nele) pra anotar."); return; }
		// corpo sem dono: nao tem assinatura, entao nao ha o que lembrar dele
		if (!EhPessoa(o)) { Avisar(pl, "não há nada para lembrar dessa criatura."); return; }
		if (!EhPessoa(pl)) { Avisar(pl, "este corpo não tem identidade própria."); return; }
		if (o.Zone.Hash != pl.Zone.Hash || (o.Pos - pl.Pos).Length > AlcanceDeAnotar)
		{
			Avisar(pl, $"{o.Name} está longe demais.");
			return;
		}

		bool novo = Fotografar(pl, o, NowMs());
		Avisar(pl, novo ? $"você guarda o rosto de {o.Name}." : $"você já conhecia {o.Name}.");
		MandarConhecidos(pl);
	}

	/// <summary>
	/// O verb `Relation()` (`Contacts.dm:115-151`): eu digo o que alguem que eu conheco e pra mim.
	///
	/// ============================ PELA ASSINATURA, E SEM EXIGIR PRESENCA ============================
	/// No DM este verb mora no proprio `/obj/Contact`, que vive na SUA lista -- ou seja, ele
	/// funciona com a pessoa do outro lado do mundo, ou offline. E faz sentido: a declaracao e sobre a
	/// sua memoria dela, nao sobre onde ela esta agora. Por isso o argumento aqui e a assinatura
	/// (que a aba People ja tem em maos) e nao o id de rede de alguem marcado.
	/// ================================================================================================
	///
	/// O CONVIVIO E CONFERIDO NO SERVIDOR (`Convivio.Declarar`). O cliente ja apaga o que nao da pra
	/// escolher, mas apagar botao nunca foi permissao -- e o convivio e o que separa "amigo" de "alguem
	/// que acabou de aparecer": sem o portao, a furia custaria um pacote na mao.
	/// </summary>
	private void VerboRelacao(ServerPlayer pl, string arg)
	{
		string[] p = arg.Split('|');
		if (p.Length < 2) { Avisar(pl, "uso: relação <pessoa>|<o que ela é pra você>."); return; }

		Relacao r = Convivio.RelacaoPorNome(p[1]);
		Conhecido? c = pl.Social.Ficha(p[0]);
		switch (pl.Social.Declarar(p[0], r))
		{
			case Declaracao.NaoConhece:
				Avisar(pl, "você não conhece essa pessoa.");
				return;
			case Declaracao.NaoSeDeclara:
				Avisar(pl, $"'{p[1]}' não é algo que dê pra dizer de alguém.");
				return;
			case Declaracao.FaltaConvivio:
				Avisar(pl, $"você ainda não conviveu o bastante com {c!.Nome} pra chamá-lo de "
						   + $"'{Convivio.NomeDaRelacao(r)}': precisa de {Convivio.FamiliaridadeExigida(r)} "
						   + $"de convívio (vocês têm {c.Familiaridade}).");
				return;
		}

		Avisar(pl, $"a partir de agora {c!.Nome} é '{Convivio.NomeDaRelacao(r)}' pra você.");
		MandarConhecidos(pl);
	}

	/// <summary>
	/// O `Contact/verb/Delete()` (`Contacts.dm:112-114`): tira alguem da lista -- e com a ficha vao o
	/// convivio e a declaracao, que moram nela. Esquecer e esquecer.
	/// </summary>
	private void VerboEsquecer(ServerPlayer pl, string arg)
	{
		if (!pl.Social.Conhecidos.Remove(arg)) { Avisar(pl, "você não conhece essa pessoa."); return; }
		Avisar(pl, "você tira essa pessoa da sua lista.");
		MandarConhecidos(pl);
	}

	/// <summary>
	/// ============================ OS DETALHES DE UM CONHECIDO -- PEDIDO DO DONO (2026-10-10) ============================
	/// *"ao abrir na aba people e clicar em pessoas q vc conhece (tem q ter um certo nivel de convivio minimo) vc
	/// pode ver mais detalhes da pessoa, como idade, a backstory dela etc"*.
	///
	/// O CLIENTE PEDE (uma pessoa por vez, pela assinatura que a lista ja lhe deu) E O SERVIDOR CONFERE: abaixo
	/// do `Convivio.ConvivioParaDetalhes` NAO SAI PACOTE NENHUM -- so a frase dizendo o que falta. A aba ja
	/// tranca o botao por conta propria, mas apagar botao nunca foi permissao; e a lista de conhecidos, que vai
	/// inteira a cada mudanca, nao carrega nada disto justamente pra nao mandar a quem nao pode ler.
	///
	/// O QUE SAI E A MINHA FICHA DA PESSOA (a fotografia), e nao o corpo vivo dela: funciona com ela offline, e
	/// nao conta nada que eu nao tenha visto. O corpo do pacote e a lista do que e publico moram em
	/// `Net/FioDosConhecidos.cs`.
	/// ====================================================================================================================
	/// </summary>
	private void VerboDetalhes(ServerPlayer pl, string assinatura)
	{
		if (pl.Social.Detalhes(assinatura) is not { } c)
		{
			Conhecido? ficha = pl.Social.Ficha(assinatura);
			Avisar(pl, ficha == null
				? "você não conhece essa pessoa."
				: $"você e {ficha.Nome} ainda não conviveram o bastante pra isso: precisa de "
				  + $"{Convivio.ConvivioParaDetalhes} de convívio (vocês têm {ficha.Familiaridade}).");
			return;
		}

		// A HISTORIA E LIMPA DE NOVO NA SAIDA. Ela ja entra limpa na ficha (`Convivio.AprenderDetalhes`), mas a
		// ficha mora num save, e o que esta num arquivo pode ter sido escrito por outra mao: e o unico texto
		// livre de OUTRO jogador neste pacote, e um caractere invalido nele faz o escritor do fio lancar excecao.
		long agora = NowMs();
		var w = Protocol.Begin(Protocol.S2C.DetalhesDeConhecido);
		FioDosConhecidos.EscreverDetalhes(w, new DetalhesDeConhecido(
			c.Assinatura, c.Genero, c.Idade, c.Titulo, c.DetalhesEm != 0, Convivio.HistoriaLegivel(c.Historia),
			SegundosDesde(c.DesdeMs, agora), SegundosDesde(c.VistoEm, agora)));

		// A BANCADA ESCUTA AQUI, e o lugar e o ponto todo: e ESTE writer que vira os bytes do fio -- o que ela
		// confere e que o pacote acaba no ultimo campo publico, e que abaixo do convivio ele nem nasce. Nulo em
		// jogo. Ver `GameServer.ConvivioTeste.cs`.
		EscutaDeDetalhes?.Add((pl.Id, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Os pacotes de detalhes que sairam (pra quem, e os bytes). So a bancada liga. Ver <see cref="VerboDetalhes"/>.</summary>
	internal static List<(int Para, byte[] Pacote)>? EscutaDeDetalhes;

	/// <summary>Ha quantos segundos foi este instante (no minimo 1). Zero quer dizer "nao sei": o carimbo nunca foi escrito.</summary>
	private static uint SegundosDesde(long quandoMs, long agoraMs) =>
		quandoMs <= 0 ? 0u : (uint)Math.Clamp((agoraMs - quandoMs) / 1000, 1, uint.MaxValue);

	// =====================================================================
	// 5. A ABA
	// =====================================================================

	/// <summary>
	/// A LISTA NO LOGIN -- e, pra quem vem de um save de antes de 2026-10-10, o instante em que a
	/// afinidade antiga vira convivio (`Convivio.MigrarAfinidade`) e o jogador e avisado de que a aba
	/// mudou: quem era "Amigo" pelos pontos do jogo deixou de ser ate ELE dizer que e.
	///
	/// AQUI e nao na carga do save (`AccountStore.ParaJogador`): so aqui ha um jogador com canal pra ler
	/// o aviso, e o campo antigo continua intacto no disco pros personagens da conta que nao logaram.
	/// </summary>
	private void ConhecidosNoLogin(ServerPlayer pl)
	{
		if (pl.Social.MigrarAfinidade())
			Avisar(pl, "a aba People mudou: o jogo não diz mais quem é seu amigo ou inimigo -- quem diz é "
					   + "você. Abra o menu (tecla P), vá em People e escolha o que cada pessoa é pra você.");
		MandarConhecidos(pl);
	}

	/// <summary>
	/// A LISTA DE CONHECIDOS, pro dono dela. Ver <see cref="Protocol.S2C.Conhecidos"/>.
	///
	/// POR PESSOA VAO O NOME E A RACA (a linha da lista) E AS DUAS COISAS QUE A ABA DESENHA: o convivio e a
	/// declaracao. A ordem e a do `Convivio.EmOrdem` (quem eu classifiquei primeiro) e vem pronta daqui -- o
	/// cliente desenha na ordem em que recebeu.
	///
	/// O QUE NAO VAI AQUI: a CLASSE (nunca aparece -- `GameServer.Sigilo.cs`, regra 2) e os DETALHES (idade,
	/// genero, cargo, historia), que so saem pessoa a pessoa e pra quem conviveu o bastante
	/// (<see cref="VerboDetalhes"/>). Esta lista vai inteira a cada mudanca: o que entrasse nela iria pra todo
	/// mundo que cruzou com a pessoa uma vez.
	///
	/// SO PRO DONO -- e as assinaturas que ele carrega sao OPACAS de proposito (ver
	/// `ServerPlayer.Assinatura`): e por elas que a aba pede uma declaracao de volta, e um `conta#slot`
	/// ali entregaria o login de quem esta do outro lado.
	/// </summary>
	private void MandarConhecidos(ServerPlayer pl)
	{
		if (pl.Peer is not { } peer) return;

		List<Conhecido> ordem = pl.Social.EmOrdem();

		var w = Protocol.Begin(Protocol.S2C.Conhecidos);
		w.Put((ushort)Math.Min(ushort.MaxValue, ordem.Count));
		foreach (Conhecido c in ordem.Take(ushort.MaxValue))
		{
			w.Put(c.Assinatura);
			w.Put(c.Nome);
			w.Put(c.Raca);
			w.Put((ushort)Math.Clamp(c.Familiaridade, 0, ushort.MaxValue));
			w.Put((byte)c.Relacao);
		}
		peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
