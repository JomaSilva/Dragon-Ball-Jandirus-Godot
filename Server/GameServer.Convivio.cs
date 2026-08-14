using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ O CONVIVIO -- O KNOWN-PEOPLE DO DM, PORTADO ============================
/// Dois arquivos de la moram aqui: `Code/Contacts.dm` (quem eu conheco, a foto da ficha dele, a
/// relacao que declarei) e `Code/Modules/Players/Friendship.dm` (pontos de amizade, o pedido, os
/// rivais e o odio). As REGRAS moram no `Core.Social.Convivio`, que nao conhece rede nem Godot;
/// aqui esta so o que e do servidor: quem esta perto de quem, quem ouviu quem, e o que isso causa.
/// ====================================================================================================
///
/// ============================ E ELE E O QUE DESTRAVA O SUPER SAIYAJIN ============================
/// O tronco Saiyajin (SSJ1/SSJ2), a linha Legendary inteira e o Beast pedem RAIVA
/// (<see cref="Catalogo.RaivaExigida"/>), e a raiva so acende por um gancho:
/// <see cref="AmigoAbatido"/>. Ate esta sessao esse gancho **nao tinha nenhum chamador de
/// producao** -- estava escrito, testado e mudo, e o unico jeito de virar Super Saiyajin era o verb
/// de admin. O que faltava nao era a raiva: era alguem capaz de responder "quem e amigo de quem".
///
/// Quem responde e este arquivo, e o chamador e o <see cref="LutoNaVizinhanca"/>.
/// ==================================================================================================
///
/// ============================ AS QUATRO COISAS QUE FAZEM UM LACO CRESCER ============================
///   * ESTAR PERTO   -> amizade (`accrue_friendship`, 6 tiles, +0,1 a cada 3 s, teto em 49);
///   * CONVERSAR     -> familiaridade (`TestListeners`, 1% por fala ouvida, nos dois sentidos);
///   * PEDIR E ACEITAR -> amizade de verdade (atravessa o teto de 49 e chega em 50);
///   * DECLARAR      -> relacao (`Relation()`), que a familiaridade paga.
///
/// As duas primeiras sao passivas e a terceira e um GESTO -- e e por isso que o teto da proximidade
/// e 49 e a exigencia de amigo e 50. Ver o cabecalho do `Convivio`.
/// ====================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 1. ESTAR PERTO -- o `accrue_friendship()` (Friendship.dm:29-44)
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
	///     ter IDENTIDADE -- e uma amizade nao acaba quando a pessoa desloga. O DM pergunta as duas
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
	/// </summary>
	private static bool EhPessoa(ServerPlayer pl) => pl.Assinatura.Length > 0;

	/// <summary>
	/// UM PASSO DE APROXIMACAO POR JOGADOR, a cada 3 segundos.
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
	/// </summary>
	private void TickDoConvivio()
	{
		long agora = NowMs();
		float raio2 = RaioDeConvivio * RaioDeConvivio;

		foreach (ServerPlayer pl in _players.Values)
		{
			// `if(!client || isNPC || !signature) return` -- `Friendship.dm:30`. Corpo sem dono
			// (clone da meditacao, NPC, corpo forjado de bancada) nao faz amizade nem entra na
			// lista de ninguem: ele nao tem assinatura pra ser lembrado por.
			if (!EhPessoa(pl)) continue;
			if (agora < pl.ProximaAproximacao) continue;
			pl.ProximaAproximacao = agora + (long)(Convivio.SegundosEntreAproximacoes * 1000);

			foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			{
				if (o == pl || !EhPessoa(o)) continue;

				Jandirus.Core.World.Vec2 d = o.Pos - pl.Pos;
				if (d.X * d.X + d.Y * d.Y > raio2) continue;

				// A ORDEM IMPORTA: fotografar primeiro poe a pessoa na minha lista, e e a lista
				// que a amizade e a familiaridade indexam. Invertido, o primeiro passo de amizade
				// de cada dupla ficaria pendurado num nome que ainda nao existe na aba.
				Fotografar(pl, o, agora);
				pl.Social.Aproximar(o.Assinatura);
			}
		}
	}

	/// <summary>
	/// A FOTOGRAFIA DA FICHA DE ALGUEM -- o `update_my_contact()` (`Contacts.dm:6-39`).
	///
	/// Um so lugar monta o `Conhecido` porque a foto e o que a aba People le, e o defeito classico
	/// aqui seria uma segunda copia que fotografa menos campos: a pessoa vista pela conversa
	/// apareceria sem raca, e a vista pela proximidade com raca -- e nada acusaria.
	/// </summary>
	private static void Fotografar(ServerPlayer eu, ServerPlayer outro, long agora) =>
		eu.Social.Fotografar(outro.Assinatura, outro.Name, outro.Race,
							 outro.Ficha?.Class ?? outro.Class, outro.Genero, outro.Idade, agora);

	// =====================================================================
	// 2. CONVERSAR -- o `TestListeners` (Talking.dm:210-219)
	// =====================================================================

	/// <summary>
	/// `prob(1)`: 1% por fala OUVIDA. E devagar de proposito -- familiaridade 50 (o que a relacao
	/// "Bom" pede) sao ~5000 falas trocadas por perto.
	/// </summary>
	private const double ChanceDeFamiliaridade = 0.01;

	/// <summary>
	/// ALGUEM ME OUVIU FALAR. Chamado do <see cref="Mandar"/> -- ou seja, do UNICO ponto por onde
	/// uma fala de personagem chega a alguem.
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
		if (_rng.NextDouble() >= ChanceDeFamiliaridade) return;

		long agora = NowMs();
		Fotografar(ouvinte, falante, agora);
		Fotografar(falante, ouvinte, agora);
		ouvinte.Social.SomarFamiliaridade(falante.Assinatura);
		falante.Social.SomarFamiliaridade(ouvinte.Assinatura);
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
	/// Este metodo e o CHAMADOR que faltava. Ele porta dois lacos do DM que sao o mesmo laco com
	/// listas diferentes:
	///
	///   * `Death.dm:73-85` -- o amigo MORREU: <see cref="NivelDeRaiva.Extrema"/>;
	///   * `KO.dm:29-41`    -- o amigo CAIU:   <see cref="NivelDeRaiva.Lendaria"/>.
	///
	/// E os dois graus valem os dois, e nao so a morte: **so a linha Legendary se contenta com o
	/// nocaute** (`Catalogo.RaivaExigida`). Ligar so a morte deixaria metade da regra do dono sem
	/// chamador -- que e exatamente o defeito que este arquivo existe pra fechar.
	///
	/// ============================ AS TRES CONDICOES, TODAS DO DM ============================
	///   1. **O ALGOZ TEM QUE SER INIMIGO DA VITIMA** (`Death.dm:75`, `KO.dm:32-35`). Sem isto,
	///      dois amigos treinando viram uma fabrica de Super Saiyajin: cada nocaute do treino
	///      enfureceria a plateia inteira. O proprio DM diz por que: *"no rage for friendly duels
	///      or environmental death"*.
	///   2. **QUEM DERRUBOU NAO SE ENFURECE COM O PROPRIO CRIME** (`Death.dm:78`, `KO.dm:35`) --
	///      e a relacao pode ser de mao unica, entao nao adianta confiar na lista dele.
	///   3. **TEM QUE ESTAR VENDO** -- o <see cref="RaioDeTestemunha"/>, e na MESMA zona.
	/// =========================================================================================
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
	/// <param name="morreu">TRUE = morte (furia extrema); FALSE = nocaute (raiva lendaria).</param>
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

		NivelDeRaiva grau = morreu ? NivelDeRaiva.Extrema : NivelDeRaiva.Lendaria;
		float raio2 = RaioDeTestemunha * RaioDeTestemunha;

		foreach (ServerPlayer o in ZoneList(vitima.Zone.Hash).ToList())
		{
			if (o == vitima || o == algoz) continue;              // (2)
			if (!EhPessoa(o)) continue;

			Jandirus.Core.World.Vec2 d = o.Pos - vitima.Pos;      // (3)
			if (d.X * d.X + d.Y * d.Y > raio2) continue;

			// AS DUAS LISTAS, COM UM `||` -- e as duas sao do observador sobre a VITIMA. A da morte
			// e mais larga que a da queda (ver `Convivio.LutoPorMorte` / `LutoPorQueda`).
			bool sente = morreu
				? o.Social.LutoPorMorte(vitima.Assinatura)
				: o.Social.LutoPorQueda(vitima.Assinatura);
			if (!sente) continue;

			// ============================ O GANCHO, ENFIM CHAMADO ============================
			// Ele devolve TRUE so na ERUPCAO (ver `AmigoAbatido`): quem ja estava em luto so tem o
			// prazo renovado, e o anuncio pra zona nao se repete. E o `wasRaging` do DM
			// (`Murder.dm:111`), que existe pra a cena de raiva nao tocar duas vezes seguidas.
			if (!AmigoAbatido(o, vitima.Name, grau)) continue;

			// O QUE OS OUTROS VEEM. As duas frases do DM, com a mesma diferenca de intensidade:
			// `Death.dm:83` ("EXTREMELY enraged") e `KO.dm:40` ("very angry").
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
		string frase = extrema
			? $"você percebe que {enlutado.Name} ficou EXTREMAMENTE furioso!!!"
			: $"você percebe que {enlutado.Name} ficou muito furioso!!!";

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
	/// Perder uma luta tem DUAS consequencias no port, e ate agora so uma delas tinha dono:
	///
	///   * o ZENKAI (`ZenkaiPorDerrota`, `GameServer.Raciais.cs`) -- ja existia, em quatro pontos;
	///   * o LUTO de quem assistiu (<see cref="LutoNaVizinhanca"/>) -- nasce agora.
	///
	/// As duas respondem a mesma pergunta ("fulano acabou de perder pra beltrano"), e nos MESMOS
	/// quatro pontos. Pendurar a segunda ao lado da primeira, uma a uma, seria criar quatro lugares
	/// onde a proxima pessoa pode ligar uma e esquecer a outra -- e esse e, literalmente, o modo de
	/// falha que mais se repetiu neste port.
	///
	/// Por isso os quatro pontos passaram a chamar ISTO, e nao o Zenkai direto. O dia em que a
	/// derrota tiver uma terceira consequencia, ela entra aqui e chega nos quatro de graca.
	/// =================================================================================
	/// </summary>
	/// <param name="morreu">
	/// FALSE = nocaute, TRUE = morte. Um golpe que nocauteia E mata chama as duas vezes (e como o
	/// DM: `Death()` chama `KO(-1)` antes de matar), e o `AmigoAbatido` sabe lidar -- as duas
	/// janelas de raiva sao independentes e a extrema ja satisfaz a lendaria.
	/// </param>
	private void AoPerderALuta(ServerPlayer vitima, ServerPlayer algoz, bool morreu)
	{
		// ============================ NADA DISTO ACONTECE DENTRO DE UMA MENTE ============================
		// *"la dentro os FERIMENTOS NAO SAO TRANSFERIDOS pro corpo real, NAO TEM COMO TER ZENKAI"* --
		// e o pedido nomeia so o Zenkai, mas as OUTRAS TRES consequencias deste funil sao piores se
		// escaparem:
		//
		//   * o ZENKAI -- a regra 2 do pedido, cobrada exatamente aqui. Sem este corte, "perder de
		//     proposito pro proprio reflexo" seria a maneira mais barata de ganhar BP no jogo, uma vez
		//     por hora, pra sempre;
		//   * o LUTO (`LutoNaVizinhanca`) -- **e este e o mais perigoso**. Um amigo "morrendo" numa luta
		//     SIMULADA acenderia raiva de verdade, e raiva de verdade abre SSJ. Duas pessoas na mesma
		//     mente poderiam fabricar a transformacao mais cara do jogo se matando de mentira;
		//   * o ODIO (`AmigoFoiFerido`) -- inimizade permanente por um treino que nao aconteceu;
		//   * a SUCESSAO (`SucessaoPorMorte`) -- o trono de Vegeta trocando de dono porque alguem
		//     "morreu" dentro da propria cabeca. Este seria irreversivel.
		//
		// O CORTE E AQUI, NO FUNIL, e nao um `if` dentro de cada uma das quatro: e literalmente o
		// argumento que criou este metodo ("quatro lugares onde a proxima pessoa pode ligar uma e
		// esquecer a outra"), e a quinta consequencia da derrota ja nasce cortada.
		//
		// PELA ZONA DA VITIMA: o algoz de dentro da mente e sempre um corpo do mesmo bolso (o reflexo,
		// a lembranca, ou a outra pessoa em transe), entao perguntar por um dos dois basta -- e a
		// vitima e quem paga todas as quatro contas.
		//
		// **E O `ZenkaiPorDerrota` NAO GANHOU UM SEGUNDO GATE**, apesar de ter dois chamadores fora
		// deste funil (`GameServer.Tecnicas.G3.cs:1209` e `:1216`). Os dois sao o caso `autor ==
		// vitima` -- morrer da propria explosao --, e ali o `GainZenkai` ja recusa antes de qualquer
		// coisa: `poderInimigo <= meuPoder` quando o inimigo e voce mesmo. Escrever a regra de novo la
		// seria a segunda copia que um dia discorda desta, pra fechar uma porta que nao abre.
		// ============================================================================================
		if (NaMente(vitima)) return;

		ZenkaiPorDerrota(vitima, algoz);
		LutoNaVizinhanca(vitima, algoz, morreu);

		// O ODIO DOS AMIGOS DA VITIMA -- `friend_harmed_by` (`KO.dm:42` e `Murder.dm:82`), com os
		// dois valores do DM. Ja e travado por rival declarado la dentro.
		AmigoFoiFerido(vitima, algoz,
			morreu ? Convivio.InimizadePorAmigoMorto : Convivio.InimizadePorAmigoCaido);

		// A TERCEIRA CONSEQUENCIA, e ela entrou exatamente onde este bloco previu que entraria: a
		// SUCESSAO. Matar quem carrega o trono de Vegeta ou o titulo de Lorde do Gelo troca o dono
		// do cargo (`Murder.dm:85-101`) -- e so a MORTE conta, nunca o nocaute, que e o que o DM faz
		// ao pendurar a regra dentro do `killer_stuff`. Ver `GameServer.CargoPortas.cs`.
		if (morreu) SucessaoPorMorte(vitima, algoz);
	}

	/// <summary>
	/// `friend_harmed_by()` (`Friendship.dm:78-84`): quem estava vendo, e AMIGO da vitima, e tinha
	/// declarado o agressor como rival, ganha odio.
	///
	/// AS DUAS CONDICOES SAO NECESSARIAS, e e isso que separa odio de amizade: amizade cresce
	/// sozinha por convivencia, odio so cresce contra quem voce ESCOLHEU odiar. Sem a segunda
	/// condicao, todo servidor viraria uma teia de inimizades automaticas.
	/// </summary>
	private void AmigoFoiFerido(ServerPlayer vitima, ServerPlayer agressor, double quanto)
	{
		if (!EhPessoa(vitima) || !EhPessoa(agressor)) return;
		float raio2 = RaioDeTestemunha * RaioDeTestemunha;

		foreach (ServerPlayer o in ZoneList(vitima.Zone.Hash).ToList())
		{
			if (o == vitima || o == agressor) continue;
			Jandirus.Core.World.Vec2 d = o.Pos - vitima.Pos;
			if (d.X * d.X + d.Y * d.Y > raio2) continue;
			if (!o.Social.EhAmigo(vitima.Assinatura) || !o.Social.EhRival(agressor.Assinatura)) continue;

			o.Social.SomarInimizade(agressor.Assinatura, quanto);
			Avisar(o, $"ver {vitima.Name} sofrer nas mãos de {agressor.Name} alimenta o seu ódio.");
		}
	}

	/// <summary>
	/// `M.add_enmity(src, ENMITY_HIT)` (`CombatMovement.dm:225` e `:241`): um golpe de um rival
	/// declarado alimenta o odio de quem apanhou. Um ponto por golpe.
	/// </summary>
	private static void GolpeDeRival(ServerPlayer vitima, ServerPlayer autor)
	{
		if (!EhPessoa(vitima) || !EhPessoa(autor)) return;
		vitima.Social.SomarInimizade(autor.Assinatura, Convivio.InimizadePorGolpe);
	}

	// =====================================================================
	// 4. OS VERBOS
	// =====================================================================

	/// <summary>Quantos tiles o pedido de amizade alcanca -- `set src in oview(3)` (`Friendship.dm:89`).</summary>
	private const float AlcanceDoPedido = 3 * Jandirus.Core.World.ZoneCollision.TileSize;

	/// <summary>`set src in oview(5)` -- a declaracao de rival (`Friendship.dm:160`).</summary>
	private const float AlcanceDaRivalidade = 5 * Jandirus.Core.World.ZoneCollision.TileSize;

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
			case "amizade_pedir": VerboPedirAmizade(pl, arg); return true;
			case "amizade_aceitar": VerboResponderAmizade(pl, aceitou: true); return true;
			case "amizade_recusar": VerboResponderAmizade(pl, aceitou: false); return true;
			case "rival": VerboRival(pl, arg); return true;
			case "relacao": VerboRelacao(pl, arg); return true;
			case "esquecer": VerboEsquecer(pl, arg); return true;
			default: return false;
		}
	}

	/// <summary>
	/// Quem esta marcado, se estiver perto o bastante. Nulo (com a recusa ja dita) caso contrario.
	///
	/// O ALVO VEM POR ID e nao por nome digitado, como todo o resto do port: o jogador ja marca
	/// gente com duplo clique, e nome se repete. E o equivalente honesto do `set src in oview(N)`
	/// do BYOND, que tambem era "aquele ali, o que eu estou vendo".
	/// </summary>
	private ServerPlayer? AlvoSocial(ServerPlayer pl, string arg, float alcance, string oque)
	{
		ServerPlayer? o = PorNome(arg);
		if (o == null || o == pl) { Avisar(pl, $"marque alguém antes (duplo clique nele) pra {oque}."); return null; }
		if (!EhPessoa(o))
		{
			// corpo sem dono: nao tem assinatura, entao nao ha o que lembrar dele
			Avisar(pl, "não há nada para lembrar dessa criatura.");
			return null;
		}
		if (!EhPessoa(pl)) { Avisar(pl, "este corpo não tem identidade própria."); return null; }
		if (o.Zone.Hash != pl.Zone.Hash || (o.Pos - pl.Pos).Length > alcance)
		{
			Avisar(pl, $"{o.Name} está longe demais.");
			return null;
		}
		return o;
	}

	/// <summary>O `Set_Contact` (`Contacts.dm:1-4`): anota alguem na mao, sem esperar a convivencia.</summary>
	private void VerboAnotar(ServerPlayer pl, string arg)
	{
		if (AlvoSocial(pl, arg, AlcanceDeAnotar, "anotar") is not { } o) return;
		bool novo = pl.Social.Fotografar(o.Assinatura, o.Name, o.Race, o.Ficha?.Class ?? o.Class,
										 o.Genero, o.Idade, NowMs());
		Avisar(pl, novo ? $"você guarda o rosto de {o.Name}." : $"você já conhecia {o.Name}.");
		MandarConhecidos(pl);
	}

	/// <summary>`Request_Friendship` (`Friendship.dm:86-99`).</summary>
	private void VerboPedirAmizade(ServerPlayer pl, string arg)
	{
		if (AlvoSocial(pl, arg, AlcanceDoPedido, "pedir amizade") is not { } o) return;
		if (pl.Social.EhAmigo(o.Assinatura)) { Avisar(pl, $"você já é amigo de {o.Name}."); return; }

		long agora = NowMs();
		Fotografar(pl, o, agora);
		Fotografar(o, pl, agora);

		o.PedidoDeAmizade = pl.Assinatura;
		o.NomeDeQuemPediu = pl.Name;
		Avisar(pl, $"você pede a {o.Name} que seja seu amigo.");
		Avisar(o, $"{pl.Name} quer ser seu amigo! (aceite ou recuse na aba People)");
	}

	/// <summary>
	/// `friend_request_resolve` (`Friendship.dm:112-129`).
	///
	/// A RESPOSTA VAI PELA ASSINATURA GUARDADA e nao por quem esta na frente: no DM tambem
	/// (`for(var/mob/P in player_list) if(P.signature == asker_sig)`), e a razao esta escrita la --
	/// *"o pedinte pode ter se afastado/relogado"*. Aceitar um pedido nao pode exigir que a pessoa
	/// continue parada ao seu lado.
	///
	/// ============================ OS DOIS LADOS, OU NENHUM -- E AQUI EU DIVIRJO DO DM ============================
	/// La o `if(asker)` da linha 123 escreve o lado de quem aceitou e, se o pedinte tiver saido,
	/// **deixa o lado dele em 49**. O resultado e uma amizade de MAO UNICA: um ve "Amigo" na aba, o
	/// outro ve "Familiar", e -- o que importa aqui -- a raiva de um funciona e a do outro nao.
	///
	/// Isso e exatamente o tipo de meia-verdade que vira *"o SSJ1 as vezes nao vem"* e que ninguem
	/// consegue ligar a um pedido de amizade de tres dias atras. Entao: se o pedinte nao esta mais
	/// no mundo, o pedido nao se resolve e os dois sao mandados pedir de novo quando se encontrarem.
	/// Perder um pedido e barato; um vinculo pela metade nao.
	///
	/// **ESTA DIVERGENCIA FOI ACHADA PELA BANCADA**, e nao por leitura: a primeira versao filtrava o
	/// pedinte por `Peer != null` (copiando o `P.client` do DM) e o `OConvivioAoVivo` reprovou com
	/// `50 / 49` -- os dois numeros do defeito, lado a lado. Quem esta vivo neste servidor e quem
	/// esta no `_players`; o socket nao acrescenta nada a essa pergunta e ainda esconde o corpo
	/// forjado que a bancada usa pra fazer a pergunta.
	/// ================================================================================================================
	/// </summary>
	private void VerboResponderAmizade(ServerPlayer pl, bool aceitou)
	{
		if (pl.PedidoDeAmizade.Length == 0) { Avisar(pl, "ninguém pediu sua amizade."); return; }

		string sig = pl.PedidoDeAmizade, nome = pl.NomeDeQuemPediu;
		ServerPlayer? pedinte = _players.Values.FirstOrDefault(
			p => EhPessoa(p) && p.Assinatura == sig);

		if (pedinte == null)
		{
			Avisar(pl, $"{nome} não está mais por aqui -- peçam de novo quando se encontrarem.");
			return;
		}

		pl.PedidoDeAmizade = "";
		pl.NomeDeQuemPediu = "";

		if (!aceitou)
		{
			Avisar(pl, $"você recusa a oferta de amizade de {nome}.");
			Avisar(pedinte, $"{pl.Name} recusou seu pedido de amizade.");
			return;
		}

		pl.Social.AceitarAmizade(sig);
		pedinte.Social.AceitarAmizade(pl.Assinatura);
		Avisar(pl, $"você e {nome} agora são amigos.");
		Avisar(pedinte, $"{pl.Name} aceitou sua amizade!");
		MandarConhecidos(pedinte);
		MandarConhecidos(pl);
	}

	/// <summary>`Declare_Rival` (`Friendship.dm:157-167`). Alterna: o mesmo verbo tira e poe.</summary>
	private void VerboRival(ServerPlayer pl, string arg)
	{
		if (AlvoSocial(pl, arg, AlcanceDaRivalidade, "declarar rival") is not { } o) return;

		Fotografar(pl, o, NowMs());
		Avisar(pl, pl.Social.AlternarRival(o.Assinatura)
			? $"você declara {o.Name} seu rival. Os golpes dele -- em você ou nos seus amigos -- vão alimentar o ódio."
			: $"você não considera mais {o.Name} um rival.");
		MandarConhecidos(pl);
	}

	/// <summary>
	/// O verb `Relation()` (`Contacts.dm:115-151`): declaro o que sinto por alguem que eu conheco.
	///
	/// ============================ PELA ASSINATURA, E SEM EXIGIR PRESENCA ============================
	/// No DM este verb mora no proprio `/obj/Contact`, que vive na SUA lista -- ou seja, ele
	/// funciona com a pessoa do outro lado do mundo, ou offline. E faz sentido: a relacao e sobre a
	/// sua memoria dela, nao sobre onde ela esta agora. Por isso o argumento aqui e a assinatura
	/// (que a aba People ja tem em maos) e nao o id de rede de alguem marcado.
	/// ================================================================================================
	///
	/// A FAMILIARIDADE E CONFERIDA AQUI, no servidor. O cliente ja apaga o que nao da pra escolher,
	/// mas apagar botao nunca foi permissao -- e a familiaridade e a moeda cara deste sistema
	/// (1% por fala): sem esta linha, "Amor" custaria um pacote na mao.
	/// </summary>
	private void VerboRelacao(ServerPlayer pl, string arg)
	{
		string[] p = arg.Split('|');
		if (p.Length < 2) { Avisar(pl, "uso: relação <pessoa>|<relação>."); return; }

		Conhecido? c = pl.Social.Ficha(p[0]);
		if (c == null) { Avisar(pl, "você não conhece essa pessoa."); return; }

		Relacao r = Convivio.RelacaoPorNome(p[1]);
		if (r == Relacao.Nenhuma) { Avisar(pl, $"'{p[1]}' não é uma relação que dê pra declarar."); return; }

		int pede = Convivio.FamiliaridadeExigida(r);
		if (c.Familiaridade < pede)
		{
			Avisar(pl, $"você precisa de {pede} de familiaridade com {c.Nome} pra declarar"
					   + $" '{Convivio.NomeDaRelacao(r)}' (você tem {c.Familiaridade}).");
			return;
		}

		c.Relacao = r;
		Avisar(pl, $"a partir de agora {c.Nome} é '{Convivio.NomeDaRelacao(r)}' pra você.");
		MandarConhecidos(pl);
	}

	/// <summary>O `Contact/verb/Delete()` (`Contacts.dm:112-114`): tira alguem da lista.</summary>
	private void VerboEsquecer(ServerPlayer pl, string arg)
	{
		if (!pl.Social.Conhecidos.Remove(arg)) { Avisar(pl, "você não conhece essa pessoa."); return; }

		// A AMIZADE E O ODIO FICAM. No DM o `Delete()` so mexe no `known_contact_list` -- as listas
		// `friendship`/`enmity` sao outras, e continuam. Esquecer o rosto nao desfaz o vinculo, e e
		// por isso que a aba mostra quem tem pontos mesmo sem ficha (ver `MandarConhecidos`).
		Avisar(pl, "você tira essa pessoa da sua lista.");
		MandarConhecidos(pl);
	}

	// =====================================================================
	// 5. A ABA
	// =====================================================================

	/// <summary>
	/// A LISTA DE CONHECIDOS, pro dono dela. Ver <see cref="Protocol.S2C.Conhecidos"/>.
	///
	/// ============================ A UNIAO DAS TRES LISTAS, E NAO SO A DOS ROSTOS ============================
	/// A aba do DM varre `known_contact_list` e busca os pontos em `friendship` (`HtmlUI.dm:549-553`).
	/// So que as tres listas nao andam juntas: da pra ter pontos de amizade com quem voce apagou da
	/// lista de rostos (o `Delete()` nao mexe na amizade), e da pra ter odio por um rival que voce
	/// nunca anotou. Mandar so os rostos esconderia justamente essas pessoas -- e elas sao as que
	/// mais importam, porque sao as que a raiva le.
	/// ==========================================================================================================
	///
	/// SO PRO DONO -- e as assinaturas que ele carrega sao OPACAS de proposito (ver
	/// `ServerPlayer.Assinatura`): elas aparecem na tela como `??? (assinatura)` pra quem ainda nao
	/// se conhece, e um `conta#slot` ali entregaria o login de quem esta do outro lado.
	/// </summary>
	private void MandarConhecidos(ServerPlayer pl)
	{
		if (pl.Peer is not { } peer) return;

		// a uniao das chaves das tres listas, na ordem de quem se e mais proximo
		var sigs = new HashSet<string>(pl.Social.Conhecidos.Keys);
		sigs.UnionWith(pl.Social.Amizade.Keys);
		sigs.UnionWith(pl.Social.Inimizade.Keys);
		sigs.UnionWith(pl.Social.Rivais);

		List<string> ordem = [.. sigs.OrderByDescending(pl.Social.PontosDeAmizade)
								 .ThenBy(s => pl.Social.Ficha(s)?.Nome ?? s, StringComparer.OrdinalIgnoreCase)];

		var w = Protocol.Begin(Protocol.S2C.Conhecidos);
		w.Put((ushort)ordem.Count);
		w.Put(pl.NomeDeQuemPediu);   // "" = nenhum pedido pendente
		foreach (string sig in ordem)
		{
			Conhecido? c = pl.Social.Ficha(sig);
			w.Put(sig);
			// SEM FICHA, O NOME E A PROPRIA ASSINATURA -- e o `??? ([signature])` do `HtmlUI.dm:374`,
			// que e como o DM mostra quem voce nao conhece. Aqui isso so acontece com quem tem
			// pontos e nao tem rosto (ver o bloco acima).
			w.Put(c?.Nome ?? "");
			w.Put(c?.Raca ?? "?");
			w.Put(c?.Classe ?? "?");
			w.Put((ushort)Math.Min(ushort.MaxValue, c?.Familiaridade ?? 0));
			w.Put((byte)(c?.Relacao ?? Relacao.Nenhuma));
			w.Put((float)pl.Social.PontosDeAmizade(sig));
			w.Put((float)pl.Social.PontosDeInimizade(sig));
			w.Put(pl.Social.EhRival(sig));
		}
		peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
