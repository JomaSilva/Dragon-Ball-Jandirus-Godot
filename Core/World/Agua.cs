namespace Jandirus.Core.World;

/// <summary>
/// COMO ESTE CORPO ESTA ATRAVESSANDO O MUNDO AGORA.
///
/// Nao e um estado do personagem -- e o ARGUMENTO de uma pergunta ("esta celula me para?"). Os
/// quatro valores sao literalmente os quatro ramos do `testWaters()` do original
/// (`Code/Modules/Movement Improvement/Swim.dm:26-38`):
///
///     if(M:flight|M:swim|M:KB|M:boat) return 1
///     else return 0
///
/// <see cref="Voando"/> cobre o `flight` E o `boat`: no DM pilotar liga `pilot.flight = 1`
/// (`ShipVessel.dm`, e o comentario de `GameServer.Nave.cs:438` ja diz isso), e neste port quem
/// voa alto nem chega a consultar mapa -- o `AtravessandoCenario` manda `mapa = null`.
/// <see cref="Arremessado"/> e o `M.KB`: quem esta sendo jogado atravessa o lago no ar.
/// </summary>
public enum ModoDeTravessia : byte
{
	/// <summary>De pe, no chao. O unico modo que a agua para.</summary>
	APe = 0,

	/// <summary>Nadando -- o `swim` do DM. Nao sobe, nao faz sombra, e passa por cima da agua.</summary>
	Nadando = 1,

	/// <summary>Voando (ou pilotando) -- o `flight`/`boat` do DM.</summary>
	Voando = 2,

	/// <summary>Sendo arremessado -- o `KB` do DM (`Swim.dm:31`).</summary>
	Arremessado = 3,
}

/// <summary>
/// A AGUA COMO **TERCEIRA CLASSE DE CELULA** -- e as cinco respostas dela num lugar so.
///
/// ============================ POR QUE UMA CLASSE, E NAO UM BIT NO `.col` ============================
/// O jogo tinha DUAS classes de celula: livre e parede, um bit do `.col`. A agua nao e nenhuma
/// das duas, e o dono foi item por item ao dizer o que ela NAO e:
///
///     "todo TILE DE AGUA n da pra passar andando, so NADANDO ou usando o FLIGHT, ela e tipo uma
///      parede, mas N DA PRA SOCAR (igual o chao), n tem SOMBRA nem nada disso, simplesmente e um
///      chao q n da pra andar por cima, so voando ou nadando"
///
/// O `.col` nao serve porque ele responde QUATRO perguntas ao mesmo tempo, e a agua so quer uma:
///
///     | pergunta                        | quem responde hoje                     | agua |
///     |---------------------------------|----------------------------------------|------|
///     | para o corpo a pe?              | `ZoneCollision.BlockedCell`            | SIM  |
///     | da pra socar / derrubar?        | `GameServer.Estrago.SocarCenario`      | nao  |
///     | a IA contorna?                  | `MoveRules.Advance` (mesmo bitset)     | so a pe |
///     | pode nascer aqui?               | `ZoneCollision.PontoLivrePerto`        | nao  |
///
/// Marcar agua no `.col` e "abrir excecao" nos outros tres seria pedir pra uma ficar de fora, e o
/// sintoma de UMA ficar de fora e calado: lago socavel, lago que a IA circunda, gente nascendo no
/// meio do oceano. Entao a agua ganha um PLANO PROPRIO (ver `ZoneCollision.EhAgua`) e as respostas
/// dela moram aqui, juntas, onde uma contradicao entre duas fica visivel na mesma tela.
///
/// A SOMBRA SAI DE GRACA, e vale dizer por que: o `.vis` e um arquivo SEPARADO do `.col`
/// (`MapConverter:289-292`) e so recebe celula com `density` no DM. Agua tem `density = 0`
/// (`Turfs.dm:1602-1607`), entao ela nunca entrou no `.vis` e continua nao entrando -- quem esta
/// do outro lado do lago e visto. Ver <see cref="Cega"/>.
/// ====================================================================================================
/// </summary>
public static class ClasseDeAgua
{
	/// <summary>
	/// ESTA CELULA PARA ESTE CORPO?
	///
	/// A regra inteira do `testWaters()` (`Swim.dm:26-38`) em uma linha: para so quem esta a pe.
	///
	/// O QUE NAO FOI PORTADO, e de proposito: o `if(B) return 0` do original -- uma celula de agua
	/// so cabe UM mob, e nem quem voa entra numa que ja tem alguem. Isso e regra de FILA (o BYOND
	/// move por tile), e este port move em pixel: nao ha "a celula ja tem alguem" a que responder
	/// sem inventar uma ocupacao que o jogo nao tem. Fica anotado como divergencia declarada.
	/// </summary>
	public static bool Bloqueia(ModoDeTravessia modo) => modo == ModoDeTravessia.APe;

	/// <summary>
	/// A AGUA ESCONDE ALGUEM ATRAS DELA? Nao -- e nao por escolha de design.
	///
	/// Nenhum turf de agua do original declara `opacity` (as 45 declaracoes do jogo estao em muros,
	/// portas e `Void_Wall`). Este campo existe pra que o dia em que alguem "resolver" a agua
	/// marcando `Density = true` no `DmTurfScanner` tenha um lugar dizendo que aquilo esta errado:
	/// densidade entra no `.vis` por `MapConverter:1251`, e o lago ganharia leque preto de muro em
	/// 1,36 milhao de celulas.
	/// </summary>
	public const bool Cega = false;

	/// <summary>
	/// DA PRA SOCAR? Nao.
	///
	/// No DM isso nao e uma excecao escrita, e a densidade decidindo: o soco em cenario exige
	/// `if(T.Resistance && T.density)` (`attack_proc.dm:96`) e agua tem `density = 0` -- o punho
	/// nunca a alcanca. `/turf/Water` ainda declara `destroyable = 0` por cima (`Turfs.dm:1602`).
	///
	/// (As aguas HD do original NAO declaram `destroyable`, e por isso uma explosao as vira terra
	/// batida. Isso e um descuido do original, nao um recurso, e o dono pediu explicitamente o
	/// contrario -- "N DA PRA SOCAR (igual o chao)". Divergencia declarada.)
	/// </summary>
	public const bool Destrutivel = false;

	/// <summary>
	/// O PUNHO ALCANCA O CENARIO DESTA CELULA? -- o funil do soco em cenario.
	///
	/// ============================ POR QUE ISTO SAIU DO SERVIDOR E VEIO PRA CA ============================
	/// A guarda ja existia, escrita a mao no `GameServer.Estrago.SocarCenario`, e ela estava CERTA. O
	/// problema era que ela nao dava pra medir: o soco mora no assembly do Godot, e a bancada (que e
	/// C# puro) so conseguia testar uma COPIA da regra -- ou seja, testar a si mesma. Uma copia que
	/// concorda consigo mesma fica verde para sempre, inclusive no dia em que a de producao mudar.
	///
	/// Trazendo a pergunta pra ca, quem responde e o mesmo objeto que responde as outras quatro (ver
	/// o quadro no cabecalho desta classe), e a bancada mede a funcao que o servidor chama.
	///
	/// A ORDEM IMPORTA e e a mesma de antes: a agua responde ANTES do bitset. Hoje isso nao muda
	/// desfecho nenhum -- agua nao esta no `.col`, entao o `BlockedCell` ja diria nao --, e e de
	/// proposito: no dia em que alguem marcar agua no `.col` (a tentacao obvia, "e tipo uma parede"),
	/// e esta linha que impede o lago de virar cenario socavel e o `DerrubarCelula` de o abrir em
	/// chao seco permanente.
	///
	/// No original o punho tambem nao a alcanca, e tampouco por excecao escrita: `attack_proc.dm:96`
	/// exige `T.Resistance && T.density`, e agua tem `density = 0` (`Turfs.dm:1602-1607`).
	/// ====================================================================================================
	/// </summary>
	public static bool SocoAlcanca(ZoneCollision mapa, int cx, int cy)
	{
		if (mapa.EhAgua(cx, cy) && !Destrutivel) return false;
		return mapa.BlockedCell(cx, cy);
	}

	/// <summary>
	/// DA PRA NASCER / POUSAR AQUI? Nao.
	///
	/// Nao e o mesmo que <see cref="Bloqueia"/>: um corpo posto na agua por um teleporte, por um
	/// pouso ou pelo berco ficaria PRESO -- livre pela colisao e parado pela regra --, que e
	/// exatamente o defeito que o `PontoLivrePerto` foi escrito pra evitar em Icer.
	/// </summary>
	public const bool ServeDeChao = false;

	/// <summary>
	/// COMO ESTE CORPO ESTA ATRAVESSANDO AGORA -- **a pergunta tem um dono so**.
	///
	/// ============================ POR QUE UMA FUNCAO, E NAO UM `?:` EM CADA CHAMADOR ============================
	/// Sao QUATRO lugares que precisam da mesma resposta e que estao em arquivos diferentes: o passo
	/// do cliente (`LocalPlayer`), a validacao do servidor (`GameServer.Input`), o passo da IA
	/// (`GameServer.Ia`) e o arremesso (`GameServer.Empurrao`). Escrever a condicao em cada um e a
	/// receita conhecida deste projeto pra um deles ficar pra tras -- e o sintoma de UM ficar pra
	/// tras e o pior que existe aqui: o cliente e o servidor discordando sobre onde da pra andar.
	///
	/// O <paramref name="noAr"/> E ALTURA E NAO A FLAG DE VOO, e essa diferenca importa exatamente
	/// numa faixa: entre soltar o chao e passar de <see cref="Voo.AlturaQueAtravessa"/> o corpo ainda
	/// consulta o mapa (`AtravessandoCenario` e falso), e sem isto ele levaria PAREDE DE AGUA na
	/// decolagem feita da beira do lago -- e tambem enquanto CAI de volta, que e quando ninguem
	/// esta apertando nada. Quem esta no ar passa por cima da agua, tenha ligado o voo ou nao.
	/// ========================================================================================================
	/// </summary>
	public static ModoDeTravessia ModoDe(bool arremessado, bool noAr, bool nadando)
	{
		// A ORDEM NAO MUDA A RESPOSTA -- os quatro ramos do `testWaters()` sao um OU (`flight|swim|
		// KB|boat`), e a agua deixa passar qualquer um deles. Ela e so a leitura: quem esta sendo
		// arremessado nao esta voando por vontade, e quem esta no ar nao esta nadando.
		if (arremessado) return ModoDeTravessia.Arremessado;
		if (noAr) return ModoDeTravessia.Voando;
		return nadando ? ModoDeTravessia.Nadando : ModoDeTravessia.APe;
	}
}

/// <summary>
/// O NADO -- porte literal do `swim` do original.
///
/// ============================ ONDE ELE MORA NO DM, LINHA POR LINHA ============================
///   * o verb          -- `Code/Modules/Movement Improvement/Swim.dm:5-23`
///   * o custo/maestria-- `Code/Modules/Stats/Stats.dm:397-410` (dentro do laco `Stats()`)
///   * a velocidade    -- `Code/Modules/Movement Improvement/movement handler.dm:65-67`
///   * o ganho de BP   -- `Code/Modules/Stats/Training/Movement.dm:1-4` (`Swim_Gain`)
///   * o desligar sozinho -- `movement handler.dm:283-287` (o `unitimer`)
///   * a maestria inicial -- `Code/Modules/Stats/mobvars.dm:59` (`swimmastery = 0.01`)
/// ==========================================================================================
///
/// ============================ NADAR NAO E VOAR RENTE AO CHAO ============================
/// O dono foi exato: *"tem uma animacao PARECIDA COM A DO FLY, porem N CRIA A SOMBRA em baixo e NEM
/// FICA MAIS ALTO, ele so entra na animacao do fly SEM SAIR DO CHAO"*. No vocabulario deste port
/// isso e uma frase so: **altitude ZERO com pose de voo**. Nao ha "voo baixinho", nao ha altura
/// pequena, nao ha sombra transparente -- a sombra e um node proprio (`Client/SombraDeVoo.cs`) e
/// ele simplesmente nao nasce, porque quem o cria e a altura mudar de zero.
///
/// E o custo NAO e o do voo: sao formulas diferentes, com pisos diferentes (o voo cai com `Ki > 5`,
/// o nado para com `Ki > MaxKi * 1%`) e com uma maestria propria no denominador.
/// ====================================================================================
/// </summary>
public static class Nado
{
	/// <summary>
	/// `swimmastery = 0.01` de nascenca (`mobvars.dm:59`).
	///
	/// NAO E "1% de pericia": e o valor que faz a divisao do custo comecar valendo 1. Ela esta no
	/// DENOMINADOR (`KiMod/(100*swimmastery)`), entao maestria baixa e custo ALTO.
	/// </summary>
	public const double MaestriaInicial = 0.01;

	/// <summary>
	/// `if(swimmastery <= 0.99)` (`Stats.dm:401`) -- e este teto faz DUAS coisas de uma vez.
	///
	/// Ele fecha o `if` que sobe a maestria **e** o que cobra o Ki: passando de 0,99, nadar fica de
	/// graca pra sempre. Isso e o original, nao uma suavizada minha, e esta escrito aqui porque
	/// parece bug de tao generoso -- sao ~196 s de nado acumulado (0,001 por tique, 5 tiques por
	/// segundo) pra sair de 0,01 e chegar la.
	/// </summary>
	public const double MaestriaMaxima = 0.99;

	/// <summary>`swimmastery += 0.001` por tique do laco `Stats()` (`Stats.dm:402`).</summary>
	public const double GanhoPorTiqueDoDm = 0.001;

	/// <summary>
	/// QUANTAS VOLTAS DO LACO DO DM CABEM NUM SEGUNDO -- **o mesmo laco do voo**.
	///
	/// O nado e o voo moram no MESMO `Stats()` (`:397` e `:411`), que dorme `sleep(2)` = 0,2 s. Nao
	/// ha uma segunda constante aqui de proposito: duas copias do mesmo 5 divergiriam no dia em que
	/// alguem descobrisse que o `sleep` do BYOND e decimo de segundo e nao quadro (foi o que ja
	/// aconteceu neste port -- ver <see cref="Voo.TiquesDoDmPorSegundo"/>).
	/// </summary>
	public const double TiquesDoDmPorSegundo = Voo.TiquesDoDmPorSegundo;

	/// <summary>
	/// ABAIXO DISTO O CORPO PARA DE NADAR: `if(Ki > MaxKi*0.01)` (`Stats.dm:398`).
	///
	/// E RAZAO, e o voo e ABSOLUTO (`Ki > 5`). Nao unifiquei os dois: com MaxKi grande o nado exige
	/// muito mais Ki de reserva que o voo, e essa assimetria e do original.
	/// </summary>
	public static double KiQuePara(double maxKi) => maxKi * 0.01;

	/// <summary>
	/// O CUSTO POR TIQUE DO DM, literal (`Stats.dm:403`):
	/// <code>Ki -= max(1 + (MaxKi*0.001*(KiMod/(100*swimmastery))), 1)</code>
	///
	/// O `max(...,1)` nunca morde de verdade (a parcela somada e sempre >= 0), e mesmo assim fica
	/// escrito: e o piso do original, e o dia em que alguem puser `KiMod` negativo por engano e ele
	/// que impede o nado de RECUPERAR Ki.
	///
	/// DEVOLVE ZERO ACIMA DE <see cref="MaestriaMaxima"/> -- ver o comentario de la.
	/// </summary>
	public static double CustoPorTiqueDoDm(double maxKi, double kiMod, double maestria)
	{
		if (maestria > MaestriaMaxima) return 0;
		double m = maestria <= 0 ? MaestriaInicial : maestria;
		return Math.Max(1 + maxKi * 0.001 * (kiMod / (100 * m)), 1);
	}

	/// <summary>O mesmo custo, na unidade que o tique do servidor usa.</summary>
	public static double CustoPorSegundo(double maxKi, double kiMod, double maestria)
		=> CustoPorTiqueDoDm(maxKi, kiMod, maestria) * TiquesDoDmPorSegundo;

	/// <summary>
	/// A MAESTRIA SOBE NADANDO, e so nadando. Devolve o valor NOVO.
	///
	/// No DM ela sobe ANTES de o custo ser calculado, no mesmo `if` -- entao quem chama tem que
	/// subir primeiro e cobrar depois, senao o primeiro tique de cada nado sai mais caro do que o
	/// original cobra.
	/// </summary>
	public static double SubirMaestria(double maestria, double segundos)
	{
		double m = maestria <= 0 ? MaestriaInicial : maestria;
		if (m > MaestriaMaxima) return m;
		return Math.Min(MaestriaMaxima + GanhoPorTiqueDoDm,
						m + GanhoPorTiqueDoDm * TiquesDoDmPorSegundo * segundos);
	}

	/// <summary>
	/// QUANTOS SEGUNDOS O CORPO TEM PRA **ENTRAR** NA AGUA depois de ligar o nado.
	///
	/// ============================ ISTO NAO ESTA NO DM, E TEM QUE ESTAR AQUI ============================
	/// No original o verb aceita quem tem agua A FRENTE e, no MESMO gesto, EMPURRA o corpo pra dentro
	/// dela (`step(usr, usr.dir)`, `Swim.dm:15`) -- a distancia entre "ligou" e "molhou" e zero, e o
	/// `unitimer` que desliga por chao seco (`movement handler.dm:283-287`) nunca ve o corpo seco.
	/// Aquele passo existe porque o BYOND anda por TILE: sem ele, ligar o nado de frente pro lago nao
	/// faria nada ate o jogador apertar a direcao.
	///
	/// AQUI O CORPO ENTRA ANDANDO, pelo movimento do proprio jogador -- e andar 32 px leva ~0,2 s. Nao
	/// ha empurrao (ver `GameServer.Nado.AguaAFrente`), entao o "zero" do original vira um PRAZO. Ele
	/// e generoso de proposito: 3 s cobrem quem gira o corpo, quem esta esmagado pela gravidade e quem
	/// esta com o passo do nado (que ja e ate 36% mais lento).
	///
	/// O QUE ELE **NAO** E: uma licenca pra nadar em terra firme. Enquanto o corpo nao molhou, nadar
	/// nao cobra Ki, nao sobe maestria e nao muda nada na colisao alem da agua -- e passado o prazo o
	/// nado cai sozinho. E ele NAO REARMA: ver `ServerPlayer.NadoJaMolhou`.
	/// =============================================================================================
	/// </summary>
	public const double PrazoParaEntrar = 3.0;

	/// <summary>
	/// `mobTime -= 0.3` (`movement handler.dm:66`) -- o "Swim Delay".
	///
	/// NAO E UM NUMERO SOLTO: 0,3 e exatamente a parcela FIXA que todo corpo ganha por tique
	/// (`mobTime += 0.3` na linha 46). Nadar tira ela inteira e deixa so a parcela que vem da
	/// velocidade -- ou seja, quem e lento perde muito mais do que quem e rapido, que e o original.
	/// </summary>
	public const double AtrasoDoNado = 0.3;

	/// <summary>
	/// A FRACAO DO PASSO QUE SOBRA NADANDO -- irmao do <c>Esmagamento.AtrasoDoPeso</c>, e pela
	/// mesma razao dele: no DM `mobTime` E o numero de passos por tique (o laco consome
	/// `OMEGA_RATE = 1` por passo), entao a razao entre dois `mobTime` E a razao entre duas
	/// velocidades.
	///
	/// <code>base = 0.3 + max(log(3.6, Epspeed), 0.1)   (movement handler.dm:46-47)</code>
	/// e nadando o passo vale `base - 0.3`. Com `Epspeed = 2` (o corpo comum) isso e 0,54 de 0,84 --
	/// 36% mais devagar; com `Epspeed = 10`, 1,80 de 2,10 -- 14%. O piso `mobTime < 0.1 -> 0.1` do
	/// DM esta escrito, ainda que nunca morda: o `max(...,0.1)` do proprio termo ja garante que o
	/// que sobra depois de tirar 0,3 nunca fica abaixo de 0,1.
	/// </summary>
	public static double FatorDePasso(double epspeed)
	{
		double baseDoPasso = 0.3 + Math.Max(
			Jandirus.Core.Stats.DmMath.Log(3.6, Math.Max(epspeed, 1e-9)), 0.1);
		return Math.Max(baseDoPasso - AtrasoDoNado, 0.1) / baseDoPasso;
	}
}
