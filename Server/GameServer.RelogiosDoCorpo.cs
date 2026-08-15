using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ OS RELOGIOS DO CORPO -- O QUE ACONTECE, E NAO O QUE SE DECIDE ============================
/// Dez varreduras de `_players` que rodavam soltas dentro do `Tick()`, uma embaixo da outra. Elas
/// continuam sendo dez, na mesma ordem e com as mesmas razoes -- o que mudou e que agora tem NOME, e
/// por isso da pra MEDIR.
///
/// ============================ POR QUE ISTO PRECISOU EXISTIR (e o cego que ele fecha) ============================
/// A fase 0 desta tarefa mediu um servidor dedicado com 148 NPCs e ninguem online: **0,717 ms por
/// tique**. Mediu tambem o congelamento da mente (`MenteDormindo`): ele responde por **~0,02 ms** --
/// 3% daquilo. Ou seja: a conta do planeta vazio **nunca foi a IA**, e ninguem sabia porque nenhuma
/// bancada media o tique inteiro; a unica que media pegava `TickDosCorposSemDono` isolado e o via
/// custar quase nada, o que fazia o congelamento parecer decisivo sendo 0,3% do orcamento.
///
/// E a mesma familia de cego do "a bancada mede INTENCAO": um numero verde sobre a peca errada.
/// Enquanto este bloco fosse dez `foreach` anonimos no meio de um metodo de 200 linhas, ele era
/// **inexercitavel** -- nao havia o que chamar de uma bancada. Agora ha, e a `--embaralhoteste`
/// (familia 5) chama.
/// ==========================================================================================================
///
/// ============================ ISTO E MUNDO, E POR ISSO NAO TEM PORTA DE PLATEIA ============================
/// A tentacao obvia, depois de ver de onde vem o custo, e pendurar este bloco no `_zonasComGente` e
/// declarar vitoria. **Seria um bug de jogo, e nao otimizacao.** O dono pediu pra desligar a MENTE
/// (*"os NPCS TEREM SUA MENTE (IA) FUNCIONANDO SOMENTE QUANDO TIVER UM JOGADOR NO PLANETA"*) -- e
/// nada aqui e mente:
///
///   * a FORMA drena Ki e derruba quem ficou sem folego -- e uma conta que ja esta correndo, nao uma
///     escolha. Congelar deixaria um chefe transformado pra sempre num planeta vazio;
///   * a CARGA, o VOO e o NADO sao os outros tres relogios do MESMO Ki: quem congelasse um teria que
///     congelar os quatro, e o saldo passaria a depender de haver plateia;
///   * OOZARU e FROST tem prazo e bateria -- prazo que para de contar e prazo que nao vence;
///   * a FURIA LENDARIA e o caminho de VOLTA de um corpo possuido. Congelada, um jogador que se
///     deslogasse em furia acordaria possuido pra sempre.
///
/// A separacao e essa e ela e a regra da tarefa inteira: **decidir** (a mente, que so importa com
/// plateia) fica no `TicarUmCorpo`; **acontecer** (o mundo, que segue) fica aqui.
/// ====================================================================================================
///
/// ============================ E SAO DEZ LACOS, E NAO UM ============================
/// Juntar os dez num `foreach` so e a economia obvia, e ela nao foi feita de proposito. Hoje TODO
/// corpo passa pela forma antes de QUALQUER corpo passar pela carga; num laco unico o corpo A faria
/// forma+carga+voo antes de o corpo B fazer forma. A ordem DENTRO de um corpo continuaria igual, mas
/// a ordem ENTRE corpos mudaria -- e os cabecalhos abaixo pedem coisas que atravessam corpos (a furia
/// devolve as redeas "ANTES do `TickDosCorposSemDono`", a fera tem precedencia sobre a furia).
///
/// Trocar isso renderia uma fracao de microssegundo e custaria uma classe inteira de bug que so
/// aparece com muita gente na mesma zona. Se um dia valer a pena, vale com medida na mao e com a
/// bancada dos dez lacos ao lado -- nao de graca no meio de outra tarefa.
/// ==============================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Os dez relogios que todo corpo paga por tique, na ordem em que o `Tick()` os pagava.
	/// **Movimento puro**: nenhuma linha mudou de comportamento, so de endereco.
	/// </summary>
	private void TickDosRelogiosDoCorpo(double dt)
	{
		// A FORMA COBRA NO TICK CHEIO. O dreno de Ki e por segundo e derruba quem ficou sem
		// folego -- cobrar so a 5 Hz deixaria a forma sobreviver por fracoes de segundo alem
		// do Ki zerado, e e justamente nesse instante que a luta costuma virar.
		foreach (ServerPlayer pl in _players.Values) TickDaForma(pl, dt);

		// A CARGA ANDA NO TICK CHEIO, junto da forma, porque as duas mexem no MESMO Ki: a forma
		// dreno e a carga enche. Rodar em cadencias diferentes faria o saldo depender da ordem em
		// que os dois relogios se cruzam -- o mesmo SSJ carregando renderia coisas diferentes em
		// tiques diferentes, sem nada no jogo explicando por que.
		foreach (ServerPlayer pl in _players.Values) TickDaCarga(pl, dt);

		// O VOO ANDA COM OS DOIS ACIMA, e pelo mesmo motivo: forma, carga e voo mexem no MESMO Ki.
		// Alem disso a altura e o que decide se o passo consulta o mapa -- rodar a 5 Hz deixaria o
		// corpo ate 200 ms com a colisao de outra altura, e a diferenca entre passar e nao passar
		// por uma parede nao pode depender de qual dos dois relogios acordou primeiro.
		foreach (ServerPlayer pl in _players.Values) TickDoVoo(pl, (float)dt);

		// ============================ E O CADAVER LARGADO NO AR **CAI** ============================
		// O cadaver nao esta no `_players` (ele nao e um corpo simulado -- ver `GameServer.Cadaver.cs`),
		// e sem esta linha ele seria o unico corpo do jogo que nao obedece a gravidade: quem carregasse
		// um morto pelo ceu e o largasse deixaria o corpo **pairando pra sempre** a vinte tiles do chao.
		// E o pedido do dono e literalmente carregar corpo pelo ar -- ou seja, o caso nao e raro, e o do
		// jogo inteiro.
		//
		// **E O UNICO RELOGIO DESTA LISTA QUE ELE PAGA.** Nao ha forma, carga, nado, Oozaru nem furia num
		// cadaver: a ficha dele nasceu zerada e ninguem a move. O que ele tem e ALTURA, e altura e
		// fisica, nao estado de lutador. Dentro do `TickDoVoo` ele so alcanca o ramo da queda -- o
		// `Voando` dele e falso, entao o custo de Ki e o `FlightGain` ficam do outro lado do `if`.
		//
		// **O BONECO DO CORPO LARGADO NAO ENTRA AQUI, E A AUSENCIA E DELIBERADA.** Ele tambem vive so na
		// `ZoneList`, mas ele COMPARTILHA a ficha do dono: tica-lo aqui cobraria o Ki de voo duas vezes
		// no mesmo personagem e chamaria `FlightGain()` na ficha de quem esta meditando -- exatamente o
		// dobro de contagem que o `GameServer.CorpoLargado.cs` o mantem fora do `_players` pra evitar. E
		// ele nao precisa: quem esta em transe nao larga o corpo voando (`LargarOCorpo` sai do corpo
		// parado), e se alguem o arremessar, quem cuida da altura e o `TickDoEmpurrao`.
		// ======================================================================================
		foreach (ServerPlayer c in TodosOsCorpos()) if (c.ECadaver) TickDoVoo(c, (float)dt);

		// O NADO ANDA LOGO DEPOIS DO VOO, e por dois motivos que sao um so:
		//
		//   * ele e o QUARTO relogio que mexe no MESMO Ki (forma, carga, voo, nado) -- e o cabecalho
		//     do `TickDoNado` ja diz que rodar em cadencias diferentes faria o saldo depender de qual
		//     acordou primeiro;
		//   * DEPOIS do voo e nao antes: quem largou o voo em cima do lago desce dentro do
		//     `TickDoVoo` (`DescerAte`), e e a POSICAO ja pousada que o nado precisa consultar pra
		//     decidir se ainda ha agua embaixo. Na ordem inversa o nado julgaria a altura do tique
		//     anterior.
		foreach (ServerPlayer pl in _players.Values) TickDoNado(pl, (float)dt);

		// O OOZARU ANDA COM AS OUTRAS FORMAS: prazo, raiva e pericia sao contados em segundos, e o
		// rabo pode ser arrancado a qualquer golpe -- a 5 Hz a forma sobreviveria ate 200 ms depois
		// de o membro cair.
		foreach (ServerPlayer pl in _players.Values) TickDoOozaru(pl, dt);

		// O MOTOR DO FROST MUTANTE ANDA AQUI, e a posicao e a mesma razao dos tres acima: ele mexe no
		// MESMO Ki (a bateria das supressoes) e no MESMO poder expresso (o vazamento). DEPOIS do
		// `TickDaForma` de proposito -- e ele quem reverte por Ki zerado ou nocaute, e a forma ja
		// revertida e sempre estavel, entao o motor le o estado final e nao um intermediario.
		foreach (ServerPlayer pl in _players.Values) TickDoFrost(pl, dt);

		// A FURIA LENDARIA ANDA LOGO DEPOIS DA FERA, e a ordem entre os tres e a regra:
		//   * DEPOIS do `TickDaForma`, porque e ele quem reverte a forma por Ki zerado ou nocaute --
		//     e e vendo a forma ja revertida que a furia devolve o corpo (sem isto haveria um tique
		//     com a IA dirigindo um jogador na forma base);
		//   * DEPOIS do `TickDoOozaru`, porque as duas possessoes escrevem no MESMO `Cerebro` e a
		//     fera tem precedencia (a furia se recusa a agir em corpo de macaco);
		//   * ANTES do `TickDosCorposSemDono`, que e quem executa -- assim tomar e devolver as redeas
		//     valem no mesmo quadro em que foram decididos.
		foreach (ServerPlayer pl in _players.Values) TickDaFuriaLendaria(pl, dt);

		// AS DISCIPLINAS DIVINAS: dreno/regeneracao das duas energias, maestria por combate, o
		// Unbound Ego (que le o corpo a cada tique) e a queda das pilhas de esquiva.
		foreach (ServerPlayer pl in _players.Values)
		{
			TickDasDisciplinas(pl, dt);
			TickDoUnboundEgo(pl);
			TickDasPilhasDeEsquiva(pl);
		}
	}

	/// <summary>
	/// O BUFFER DE QUEM ESTA NO ESPACO, reusado a cada tique.
	///
	/// ============================ ERA UM `_players.Values.ToList()` A 30 Hz ============================
	/// A linha era `foreach (ServerPlayer pl in _players.Values.ToList()) TickDoEspaco(pl);` -- uma
	/// `List` de TODO corpo do servidor, alocada 30 vezes por segundo, pra chamar um metodo cuja
	/// PRIMEIRA linha e `if (!Espaco.EhEspaco(pl.Zone)) return;`. Com 148 habitantes isso e ~4.400
	/// listas por segundo de lixo pro coletor, e o custo crescia com a quantidade de NPCs no mundo --
	/// justamente o numero que esta tarefa foi aberta pra parar de pagar.
	///
	/// E o defeito ja tinha nome nesta casa: e literalmente o mesmo que o cabecalho de `_dirigidos`
	/// (`GameServer.Clone.cs`) diz ter consertado, vivo na porta ao lado.
	///
	/// A COPIA CONTINUA SENDO NECESSARIA -- `TickDoEspaco` pousa, troca de zona e mexe nas colecoes
	/// no meio da volta --, mas ela passou a ser do tamanho de quem esta MESMO no espaco (quase
	/// sempre zero) em vez do tamanho do servidor. `Clear()` num `List` reusado nao devolve a
	/// capacidade, que e exatamente o que se quer.
	/// ============================================================================================
	/// </summary>
	private readonly List<ServerPlayer> _noEspaco = [];

	/// <summary>
	/// Troca de chunk e pouso por encostar, so pra quem esta la em cima. Ver <see cref="_noEspaco"/>.
	/// </summary>
	private void TickDoEspacoDeTodos()
	{
		_noEspaco.Clear();
		foreach (ServerPlayer pl in _players.Values)
			if (Espaco.EhEspaco(pl.Zone)) _noEspaco.Add(pl);

		foreach (ServerPlayer pl in _noEspaco)
		{
			// MOB-ZUMBI: a volta anterior pode ter pousado, movido ou removido este corpo. A receita
			// e a mesma do `_dirigidos` -- conferir a cada volta que ele ainda esta no mundo.
			if (_players.ContainsKey(pl.Id)) TickDoEspaco(pl);
		}
	}
}
