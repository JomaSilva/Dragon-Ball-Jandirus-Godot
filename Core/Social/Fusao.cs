using Jandirus.Core.Appearance;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Social;

/// <summary>
/// QUE FUSAO E ESTA. Os numeros sao os `FType` do DM (`Fusion.dm:268-271`), e nao uma numeracao
/// nova: e por eles que o original escolhe a energia, a roupa e se a fusao e permanente.
/// </summary>
public enum TipoDeFusao
{
	/// <summary>A Danca (Metamoro). `FType == 1`. 900 de energia, roupa `Metamoran Vest`.</summary>
	Danca = 1,

	/// <summary>Os brincos Potara. `FType == 2`. 1800 de energia, sem QTE, qualquer raca.</summary>
	Potara = 2,

	/// <summary>
	/// A Namekuseijin. `FType == 3` (`Fusion.dm:549-569`).
	///
	/// ============================ ELA E O UNICO TIPO QUE NAO PRODUZ UMA FUSAO ============================
	/// Os outros dois criam um corpo compartilhado que acaba. Esta **absorve**: o poder e assado no
	/// personagem de quem convidou, o outro perde o dele PARA SEMPRE, e nao ha objeto de fusao nenhum
	/// pra separar, drenar ou gravar. E o pedido do dono (*"o outro namek se for jogador, perde o
	/// personagem pra sempre (a fusao e eterna)"*) e tambem o estado terminal que o proprio DM alcanca
	/// em `Fusion.dm:301-310`.
	///
	/// **O tipo continua existindo, e continua sendo este**, porque tudo o que vem ANTES da consumacao
	/// e o mesmo dos outros dois: o verb, o convite, o aceite revalidado, a distancia de um tile, a
	/// recarga de 1 h e a cinematica. A divergencia mora num `if` so, na virada da cena
	/// (`GameServer.TickDaCenaDeFusao`). Ver `Core/Social/AbsorcaoNamekuseijin.cs`, onde a regra e os
	/// numeros dela moram.
	/// ================================================================================================
	/// </summary>
	Namek = 3,
}

/// <summary>Por que a fusao nao aconteceu. <see cref="Pode"/> = aconteceu.</summary>
public enum RecusaDeFusao
{
	Pode = 0,
	SemAssinatura,   // corpo sem dono (NPC, clone): nao tem identidade pra fundir
	EleMesmo,
	JaFundido,       // `in_active_fusion()` (`Fusion.dm:58`)
	NaRecarga,       // `fusion_on_cooldown()` (`Fusion.dm:55`)
	Caido,           // KO ou morto
	Longe,
	OutraZona,
	SemSkill,        // EU nao sei dancar
	OutroSemSkill,   // ELE nao sabe dancar -- pedido do dono, nao existe no DM
	RacaDiferente,   // pedido do dono, nao existe no DM

	/// <summary>
	/// UM DOS DOIS NAO E NAMEKUSEIJIN. **Este e do DM**, e das duas pontas: `Fusion.dm:556-557` recusa
	/// com `M.Race!="Namekian"` e `usr.Race!="Namekian"`. Ele e diferente do
	/// <see cref="RacaDiferente"/>, que so pergunta se os dois sao IGUAIS -- dois Saiyajins passariam
	/// naquele e tem que cair neste.
	/// </summary>
	NaoEhNamekuseijin,
	PoderDesigual,   // o `PowerEqual` morto do `Fusion.dm:95`
	JaTemPedido,     // ja ha um convite na mesa dele
}

/// <summary>
/// A FUSAO -- as regras puras. Porte de `Code/Modules/Magic/Fusion.dm`.
///
/// ============================ O QUE E PORTE E O QUE E PEDIDO NOVO ============================
/// O levantamento desta etapa mediu isso arquivo por arquivo, e a divisao importa porque ela diz
/// de onde cada numero pode sair:
///
///   * **PORTE LITERAL** -- a energia (900 / 1800), o dreno (`1 + mult/50`), a recarga de 1 h, o
///     BP `(A+B)*2`, quem controla, o Pass Control, o nocaute que separa. Tudo isso ja existe e
///     esta bem-feito no `Fusion.dm`, e nada aqui inventa um decimo.
///   * **PEDIDO NOVO** -- o QTE, a fusao estragada, o portao de raca, o portao de poder proximo, o
///     convite da Potara, o nome que difere entre os dois tipos. **Nada disso existe no DM.** A
///     frase do dono (*"as mecanicas ja estao no byond"*) vale pro motor e nao pro pedido.
///
/// E ha UM caso intermediario, que e o mais importante deste arquivo: o `PowerEqual` e o
/// `FusionSkill` (`Fusion.dm:95-96`). Os dois sao declarados, comentados... e **lidos por
/// ninguem** -- `grep` em todo o `Code/` nao acha um segundo uso. O comentario que os acompanha e,
/// palavra por palavra, o pedido do dono:
///
///   `var/PowerEqual = 1 //Any number greater or lower will cause the transformation's power to`
///   `decrease. Below &lt;0.5 will mean the fusion itself will become botched.`
///
/// Ou seja: o autor original planejou "poder proximo" e "fusao estragada", escreveu o LIMIAR
/// (0,5) e nunca implementou. Este arquivo implementa -- e por isso o 0,5 nao e um numero meu:
/// e o unico numero que o original escreveu ao lado da palavra *botched*.
/// ============================================================================================
/// </summary>
public static class Fusao
{
	// =====================================================================
	// A ENERGIA DE FUSAO -- `Fusion.dm:3-6` e `:337-357`
	// =====================================================================
	/// <summary>`FUSION_DANCE_ENERGY = 900` (`Fusion.dm:4`): 15 minutos no dreno base.</summary>
	public const double EnergiaDaDanca = 900;

	/// <summary>`FUSION_POTARA_ENERGY = 1800` (`Fusion.dm:5`): 30 minutos no dreno base.</summary>
	public const double EnergiaDaPotara = 1800;

	/// <summary>
	/// `FUSION_COOLDOWN = 36000` decimos (`Fusion.dm:6`) = **1 hora**, por pessoa, depois que a
	/// fusao acaba. Aqui em segundos porque o relogio deste port e em ms de tempo real.
	/// </summary>
	public const double RecargaSegundos = 3600;

	/// <summary>
	/// O DRENO, POR SEGUNDO: `1 + (mult da forma / 50)` -- `Fusion.dm:349`, literal.
	///
	/// ============================ A TABELA DO DONO FECHA NOS ONZE PONTOS ============================
	/// Ele mandou a tabela do jogo junto com o pedido, e ela e exatamente esta linha:
	/// 2x -> 1,04/s (Potara 28,85 min, Danca 14,42); 4x -> 1,08; 8x -> 1,16; 12x -> 1,24;
	/// 14x -> 1,28; 24x -> 1,48; 42x -> 1,84; 45x -> 1,90; 50x -> 2,00 (Potara 15 min, Danca 7,5);
	/// 55x -> 2,10. **Nao ha outra formula a inventar.**
	/// ==============================================================================================
	/// </summary>
	public static double DrenoPorSegundo(double multiplicadorDaForma) =>
		1 + (multiplicadorDaForma > 1 ? multiplicadorDaForma / 50 : 0);

	/// <summary>
	/// A ENERGIA COM QUE ESTE TIPO NASCE. **Zero = permanente** -- e o `else FusionEnergyMax = 0`
	/// do `switch` do DM (`Fusion.dm:271`), que la e a Namekuseijin.
	///
	/// **AQUI NINGUEM MAIS CAI NO ZERO**, e o `_` sobrou como o `default` que a linguagem exige: a
	/// Namekuseijin nao produz `FusaoAtiva` nenhuma desde que virou ABSORCAO (ver
	/// `Core/Social/AbsorcaoNamekuseijin.cs`), entao so a Danca e a Potara chegam a este metodo. O zero
	/// fica como a resposta certa pra um tipo que um dia nasca sem energia -- e nao como a descricao de
	/// um caso que existe.
	/// </summary>
	public static double EnergiaMaxima(TipoDeFusao t) => t switch
	{
		TipoDeFusao.Danca => EnergiaDaDanca,
		TipoDeFusao.Potara => EnergiaDaPotara,
		_ => 0,
	};

	/// <summary>
	/// `current_form_mult()` (`Fusion.dm:31-34`), literal -- e o que faz a forma acelerar o dreno.
	///
	/// AS FORMAS DIVINAS NAO MORAM NO `ssjBuff`, e por isso o ramo separado: o proprio DM escreve
	/// isso no comentario da linha 33, e este port ja o repete em dois lugares
	/// (`Core/Forms/Formas.cs:768` e `:4050`). Lido do `ssjBuff` sozinho, um Blue drenaria como um
	/// SSJ1 -- 30 minutos de Potara em vez de 15.
	/// </summary>
	public static double MultiplicadorDaFormaAtual(Fighter f)
	{
		double g = f.godki is { usage: true } ? f.GodFormMult() : 0;
		return Math.Max(g != 0 ? g : f.ssjBuff * f.transBuff * f.formsBuff, 1);
	}

	// =====================================================================
	// O PODER DA FUSAO
	// =====================================================================
	/// <summary>
	/// O BP BASE DA FUSAO BEM-FEITA: `(BP_A + BP_B) * 2` -- `Fusion.dm:264`, literal, e tambem o
	/// pedido do dono com as mesmas palavras.
	///
	/// **E BP REAL nos dois lados**, e nao o expresso: o expresso ja carrega forma, raiva, Ki e
	/// nocaute, e somar dois numeros ja inflados dobraria cada um desses fatores. O DM le
	/// `Keeper.BP + Loser.BP`, que sao os campos crus.
	/// </summary>
	public static double BpDaFusao(double bpA, double bpB) => (bpA + bpB) * 2;

	/// <summary>
	/// QUANTO SOBRA DA FUSAO ESTRAGADA: **metade do MAIS FRACO dos dois**.
	///
	/// ============================ DE ONDE SAI ESTE NUMERO ============================
	/// O pedido do dono e uma DESIGUALDADE, e nao um valor: *"fica EXTREMAMENTE FRACA -- mais fraca
	/// que os personagens separados"*. Qualquer numero que a satisfaca cumpre o pedido; o que este
	/// arquivo deve e dizer de onde tirou o que escolheu.
	///
	/// O 0,5 e o unico numero que o original escreveu ao lado da palavra *botched*
	/// (`Fusion.dm:95`, o `PowerEqual` morto). O que ele nao disse foi *metade de que*, e a
	/// resposta e forcada pela propria desigualdade:
	///
	///   * **metade da SOMA** (`(A+B)/2`) nao serve: com A = B ela da exatamente A -- a fusao
	///     empata com cada metade em vez de ficar mais fraca;
	///   * **metade da fusao boa** (`(A+B)`) e mais forte que os dois, que e o oposto do pedido;
	///   * **metade do MAIS FRACO** (`min(A,B) * 0,5`) e a unica das tres que fica abaixo dos dois
	///     **sempre**, sem depender de quao parelhos eles sao. Dois lutadores de 1000 saem com 500.
	///
	/// E ela e coerente com o resto do desenho: o portao de proximidade ja garante que o mais fraco
	/// vale pelo menos metade do mais forte, entao "metade do mais fraco" e no minimo um quarto do
	/// mais forte -- uma queda que o jogador sente na primeira troca de socos, que e o ponto.
	/// ==================================================================================
	/// </summary>
	public const double FraquezaDaFusaoEstragada = 0.5;

	/// <summary>O BP base da fusao estragada. Ver <see cref="FraquezaDaFusaoEstragada"/>.</summary>
	public static double BpDaFusaoEstragada(double bpA, double bpB) =>
		Math.Max(Math.Min(bpA, bpB) * FraquezaDaFusaoEstragada, 1);

	/// <summary>O BP base, com ou sem estrago -- o funil unico, pra nao haver duas contas.</summary>
	public static double BpBase(double bpA, double bpB, bool estragada) =>
		estragada ? BpDaFusaoEstragada(bpA, bpB) : BpDaFusao(bpA, bpB);

	// =====================================================================
	// OS PORTOES DO DONO -- raca e poder proximo
	// =====================================================================
	/// <summary>
	/// A RAIZ RACIAL: o que conta como "a mesma raca" pro portao da Danca.
	///
	/// *"so entre pessoas da MESMA RACA -- meio saiyajin com saiyajin puro ainda funciona"*. No
	/// catalogo deste port o meio-Saiyajin e a raca `Halfbreed` (`races.json`), e ele nao tem
	/// prototipo proprio: `Birth.cs:123` ja resolve o corpo dele **pelo Saiyajin**
	/// (`protoRaca = raca == "Halfbreed" ? "Saiyan" : raca`). Esta funcao e a mesma resposta na
	/// mesma forma -- e por ser uma funcao, no dia em que outra mestiçagem entrar, ela entra aqui e
	/// em nenhum outro lugar.
	/// </summary>
	public static string RaizDaRaca(string raca) =>
		raca.Equals("Halfbreed", StringComparison.OrdinalIgnoreCase)
		|| raca.Equals("Half-Saiyan", StringComparison.OrdinalIgnoreCase)
			? "Saiyan"
			: raca;

	/// <summary>A mesma raca, pela <see cref="RaizDaRaca"/>.</summary>
	public static bool MesmaRaca(string a, string b) =>
		RaizDaRaca(a).Equals(RaizDaRaca(b), StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// O `PowerEqual` do `Fusion.dm:95`, finalmente calculado: a razao entre o MENOR e o MAIOR dos
	/// dois poderes. 1 = identicos; quanto menor, mais desigual.
	///
	/// **E o BP EXPRESSO**, e o dono foi explicito (*"BP expresso PROXIMO"*). E e o certo: o que
	/// duas pessoas conseguem comparar uma da outra e o poder que elas SENTEM -- e por isso um dos
	/// dois estar suprimindo o proprio poder faz a conta recusar. Nao e efeito colateral: quem
	/// esconde o proprio poder nao tem como ser medido.
	/// </summary>
	public static double RazaoDePoder(double expressoA, double expressoB)
	{
		double maior = Math.Max(expressoA, expressoB);
		if (maior <= 0) return 1;
		return Math.Max(Math.Min(expressoA, expressoB), 0) / maior;
	}

	/// <summary>
	/// O LIMIAR DE PROXIMIDADE: `0.5`. **E do DM** -- `Fusion.dm:95`, *"Below &lt;0.5 will mean the
	/// fusion itself will become botched"*.
	///
	/// AQUI ELE E PORTA E NAO ESTRAGO, e a diferenca e do pedido do dono: la o desequilibrio
	/// ESTRAGAVA a fusao; aqui quem estraga e o QTE, e o desequilibrio **impede o convite**. Sao os
	/// dois unicos jeitos de usar o mesmo numero, e o dono escolheu o primeiro ao listar "BP
	/// expresso proximo" entre os REQUISITOS da Metamoro, ao lado da raca.
	/// </summary>
	public const double LimiarDeProximidade = 0.5;

	// =====================================================================
	// O QUICK TIME EVENT -- pedido novo, nao existe no DM
	// =====================================================================
	/// <summary>
	/// QUANTAS LETRAS CADA UM TEM QUE ACERTAR: **tres**.
	///
	/// Nao ha numero pra portar (o DM nao tem QTE nenhum), entao ele sai da coisa que o QTE
	/// representa: a Danca da Fusao e uma coreografia de TRES tempos -- *"Fu... sion... HA!"* --, e
	/// errar qualquer um deles e o que produz a fusao gorda do desenho. Uma letra seria sorte; dez
	/// seria um minigame de digitacao no meio de uma cena de dois segundos.
	/// </summary>
	public const int LetrasDaDanca = 3;

	/// <summary>
	/// QUANTO TEMPO A DANCA INTEIRA TEM, em segundos, contando do comeco.
	///
	/// E teto e nao ritmo: quem acerta rapido termina em ~1 s (a letra seguinte nasce assim que a
	/// anterior e acertada -- ver o piso de cadencia do motor de embate). O teto existe pra que
	/// **ninguem fique dancando pra sempre** se largar o teclado: passado o prazo, o que faltou
	/// conta como erro e a fusao sai estragada. E a mesma disciplina do prazo do convite.
	/// </summary>
	public const double SegundosDaDanca = 4;

	// =====================================================================
	// O CONVITE
	// =====================================================================
	/// <summary>
	/// QUANTO TEMPO UM CONVITE DE FUSAO FICA DE PE, em segundos.
	///
	/// NAO EXISTE NO DM, e nao existe porque la nao precisa: o `input()` (`Fusion.dm:566` e `:725`)
	/// CONGELA o convidado ate ele responder. Aqui nao ha caixa modal -- o molde e o
	/// <see cref="Jandirus.Core.Skills.Discipulado.PrazoDoConviteSegundos"/>, e o argumento e o
	/// mesmo, palavra por palavra: um pendente sem prazo vira caixa de entrada, e aceitar um
	/// convite de meia hora atras, do outro lado do mundo, nao quer dizer nada.
	///
	/// **E ELE E A RESPOSTA A "e se o outro simplesmente IGNORAR?"**: nao ha caminho em que alguem
	/// fique preso num pedido eterno, porque o pedido morre sozinho.
	/// </summary>
	public const double PrazoDoConviteSegundos = 60;

	// =====================================================================
	// A DISTANCIA -- e ela e DIFERENTE por tipo, porque o DM tambem a faz diferente
	// =====================================================================
	/// <summary>
	/// COLADO: **um tile**, e ele e o `get_dist(...) &gt; 1` do original em TRES lugares --
	/// `Metamoran Fusion.dm:92` (antes da caixa de pergunta) e `:122` (**depois** do aceite), mais o
	/// `oview(1)` do Finale (`Fusion.dm:553` na Namekuseijin e `:716` na Danca).
	///
	/// ============================ ELE VALIA 4 E ESTAVA ERRADO ============================
	/// Havia aqui um unico `TilesDoConvite = 4` pros tres tipos, justificado por um argumento que o
	/// pedido do dono derrubou: *"exigir que continuem colados faria o aceite falhar por um passo"*.
	/// O dono foi literal -- *"na metamoro os jogadores tem q estar perto um do outro (no tile ao
	/// lado)"* -- e o DU cobra a distancia **duas vezes**, no convite E no aceite, exatamente como
	/// este port faz. Quem se afastou entre uma coisa e outra ouve a recusa, que e o comportamento do
	/// original.
	///
	/// A Potara nao entra aqui: la o original NAO exige proximidade nenhuma, ele **puxa os dois**
	/// (ver <see cref="PuxaOsCorpos"/> e <see cref="TilesDaPotara"/>).
	/// ====================================================================================
	/// </summary>
	public const int TilesColados = 1;

	/// <summary>
	/// A POTARA ENXERGA LONGE: **20 tiles**, o `oview(usr,20)` do `Potara_Fusion.dm:96`.
	///
	/// E o numero do original, e ele existe por causa do puxao: la o brinco e ATIRADO (*"You toss a
	/// Potara Earring to [B.name]!"*, `:114`) e so depois os dois andam um pro outro. Cobrar tile ao
	/// lado aqui seria pedir que estivessem colados pra poderem ser aproximados -- o puxao nao teria
	/// o que fechar.
	///
	/// Neste port o alvo e o MARCADO (duplo clique), que ja e um gesto de distancia -- ver
	/// `GameServer.OferecerOsBrincos`.
	/// </summary>
	public const int TilesDaPotara = 20;

	/// <summary>
	/// A QUE DISTANCIA ESTE TIPO DE FUSAO DA PRA CONVIDAR (e pro convite continuar valendo no "sim").
	///
	/// UMA FUNCAO E NAO TRES CONSTANTES SOLTAS: o convite e o aceite fazem a MESMA pergunta, e a
	/// bancada precisa dela pra montar o caso de "longe demais" sem recopiar a tabela.
	/// </summary>
	public static int TilesDoConvite(TipoDeFusao t) =>
		t == TipoDeFusao.Potara ? TilesDaPotara : TilesColados;

	/// <summary>
	/// A DISTANCIA COMO O BYOND A MEDE -- `get_dist()`, que e a **distancia de tabuleiro** (Chebyshev)
	/// entre os dois TURFS, e nao a linha reta entre dois pixels.
	///
	/// ============================ POR QUE ISTO PRECISOU DE FUNCAO PROPRIA ============================
	/// O port media `Vec2.Distance(a,b) / TileSize` -- linha reta. Com o portao valendo 4 tiles a
	/// diferenca era invisivel; com ele valendo **1**, ela decide: dois corpos em tiles DIAGONALMENTE
	/// vizinhos estao a 1,41 em linha reta e a **1** no `get_dist`. Pela conta antiga, o vizinho de
	/// canto seria recusado -- e o `oview(1)` do original o inclui, porque `oview` e o quadrado de
	/// nove celulas.
	///
	/// A celula sai do CENTRO do corpo (`Pos` e o centro do boneco de 32x32), que e o mesmo `loc` que
	/// o BYOND usaria.
	/// </summary>
	public static int DistanciaEmTilesDoDm(
		Jandirus.Core.World.Vec2 a, Jandirus.Core.World.Vec2 b, int tamanhoDoTile)
	{
		int ax = (int)Math.Floor(a.X / (double)tamanhoDoTile);
		int ay = (int)Math.Floor(a.Y / (double)tamanhoDoTile);
		int bx = (int)Math.Floor(b.X / (double)tamanhoDoTile);
		int by = (int)Math.Floor(b.Y / (double)tamanhoDoTile);
		return Math.Max(Math.Abs(ax - bx), Math.Abs(ay - by));
	}

	// =====================================================================
	// O PUXAO -- `Potara_Fusion.dm:122-129`
	// =====================================================================
	/// <summary>
	/// ESTE TIPO PUXA OS DOIS CORPOS UM PRO OUTRO ANTES DA CENA? -- **so a Potara**.
	///
	/// ============================ O ORIGINAL, LINHA POR LINHA ============================
	/// `Code/Combat/Skills/Ki/Fusion/Potara_Fusion.dm:122-131`:
	///
	///     C.mob.AlterInputDisabled(1)
	///     B.AlterInputDisabled(1)
	///     while(get_dist(C.mob,B) &gt; 1 &amp;&amp; C.mob.z == B.z)
	///         step_to(B,C.mob,0,32)
	///         B.AlignToTile()
	///         step_to(C.mob,B,0,32)
	///         C.mob.AlignToTile()
	///         sleep(world.tick_lag)
	///     sleep(1)
	///     C.mob.AlterInputDisabled(-1)
	///     B.AlterInputDisabled(-1)
	///
	/// Os DOIS andam (nao e um indo ate o outro), o input dos dois fica desligado enquanto isso, e o
	/// laco so acaba quando `get_dist &lt;= 1` -- ou seja **quando se encostam**. E o pedido do dono
	/// palavra por palavra: *"na potara quando ela comecar eles sao puxados um pro lado do outro e
	/// quando se encostarem a cinematica comeca"*.
	///
	/// A Metamoro e a Namekuseijin nao puxam porque nao precisam: elas ja exigem
	/// <see cref="TilesColados"/> no convite E no aceite.
	/// </summary>
	public static bool PuxaOsCorpos(TipoDeFusao t) => t == TipoDeFusao.Potara;

	/// <summary>
	/// A VELOCIDADE DO PUXAO, em pixels por segundo, **de cada um dos dois corpos**.
	///
	/// ============================ ELA E CALCULADA DO DM, E NAO ESCOLHIDA ============================
	/// `step_to(B,C.mob,0,32)` anda **32 px** (o quarto argumento do `step_to` e a velocidade em
	/// pixels, e 32 e um tile), e o laco dorme `world.tick_lag`. O DU roda a **40 quadros por
	/// segundo** (`Code/Movement/Move Delay.dm:9`, `world/fps = 40`), entao `tick_lag` = 0,025 s:
	///
	///     32 px / 0,025 s = **1280 px/s** por corpo -- 2560 px/s de aproximacao, porque os dois andam.
	///
	/// E rapido de proposito la e continua rapido aqui: no pior caso deste port (os
	/// <see cref="TilesDaPotara"/> tiles = 640 px do convite) o puxao inteiro dura um quarto de
	/// segundo. Nao ha numero meu nesta linha -- ha o `32`, o `40` e a divisao.
	///
	/// **O `AlignToTile` NAO foi portado**: ele existe no DU porque la o movimento e por pixel e o
	/// jogo inteiro se alinha a grade depois de cada passo. Aqui nao ha grade a que voltar, e
	/// grudar o corpo no tile no meio do deslize seria o solavanco que o arremesso ja pagou pra
	/// tirar (ver `GameServer.TickDoEmpurrao`).
	/// </summary>
	public const double VelocidadeDoPuxao = 32.0 / 0.025;

	/// <summary>
	/// QUANTO TEMPO O PUXAO PODE FICAR SEM ENCURTAR A DISTANCIA ANTES DE DESISTIR, em segundos.
	///
	/// ============================ DESENHO NOVO, E O ORIGINAL PRECISA DELE ============================
	/// O `while` do `Potara_Fusion.dm:124` **nao tem saida**: com uma parede entre os dois, ou com um
	/// deles preso, aquele laco gira pra sempre com o input dos DOIS desligado. E travar dois
	/// jogadores pra sempre e o pior defeito que este sistema pode ter -- a mesma frase que o teto da
	/// cinematica ja carrega.
	///
	/// A saida NAO e um relogio global ("desista em N segundos"), e sim um FATO: a bancada deste
	/// projeto ja aprendeu que *"espere por fato, nao por relogio"*. O que se guarda e a MENOR
	/// distancia ja alcancada; enquanto ela melhorar, o puxao continua o tempo que precisar. Parou de
	/// melhorar por este tanto -- parede, corpo preso, um deles voando pro outro lado -- e a fusao
	/// **nao comeca**, que e o item que o dono pediu por escrito.
	///
	/// UM SEGUNDO e o prazo, e ele e generoso pelo numero de cima: a 2560 px/s de aproximacao, um
	/// segundo sem encurtar nada quer dizer que 2560 px de caminho nao existiram.
	/// </summary>
	public const double SegundosSemAproximarParaDesistir = 1.0;

	/// <summary>
	/// TUDO O QUE A FUSAO PERGUNTA, sem nada do servidor dentro -- o molde e o
	/// <see cref="Jandirus.Core.Skills.Discipulado.AvaliarVinculo"/>, e pelo mesmo motivo: o
	/// convite e o aceite fazem **exatamente as mesmas** checagens, e duas copias delas divergiriam
	/// no dia em que um portao mudasse.
	/// </summary>
	/// <param name="tipo">
	/// A Potara nao pergunta raca nem poder proximo (*"qualquer raca"*, e sem QTE) e nao pergunta
	/// skill: o que ela cobra e o ITEM, e item nao e pergunta pura -- quem confere a mochila e o
	/// servidor, antes de chamar.
	/// </param>
	/// <param name="distanciaTiles">
	/// A distancia **na moeda do BYOND** (<see cref="DistanciaEmTilesDoDm"/>). Negativo = nao conferir.
	/// O teto e por TIPO -- ver <see cref="TilesDoConvite"/>.
	/// </param>
	public static RecusaDeFusao Avaliar(
		TipoDeFusao tipo,
		bool convidaTemAssinatura, bool convidadoTemAssinatura,
		bool mesmaPessoa,
		bool algumJaFundido, bool algumNaRecarga, bool algumCaido,
		bool mesmaZona, double distanciaTiles,
		bool euSeiDancar, bool eleSabeDancar,
		string racaA, string racaB,
		double expressoA, double expressoB,
		bool eleJaTemPedido,
		bool convidadoEhNpcAbsorvivel = false)
	{
		// ============================ QUEM CONVIDA E SEMPRE PESSOA. O ALVO, NA NAMEKUSEIJIN, PODE NAO SER ============================
		// O portao de assinatura e o mesmo de sempre pro CONVIDADOR, e ele nao se abre: e a linha que
		// impede um corpo sem dono (NPC, clone, boneco) de iniciar uma fusao -- e, junto com o
		// `Gente.EhNpcDoMundo` do servidor, a que impede um pacote forjado de IA de faze-lo.
		//
		// O ALVO ganhou uma excecao, e ela e a regra N4 do dono: *"fundir com npc namek ganha BEM menos
		// bp e outros bonus e nao ganha o super namek"*. Pra "fundir com NPC" existir, o NPC precisa
		// poder ser alvo -- e ate aqui ele levava `SemAssinatura` sempre. Quem decide o que conta como
		// NPC absorvivel e o servidor (`GameServer.EhNamekNpcAbsorvivel`), porque a pergunta e sobre o
		// CORPO e nao sobre a regra: o Core nao sabe o que e um clone.
		//
		// **E ela e so pra Namekuseijin.** A Danca e a Potara continuam recusando corpo sem dono, e o
		// caminho do NPC nem passa por convite (ninguem pede permissao a um NPC) -- ver
		// `GameServer.ConvidarParaAFusaoNamekuseijin`.
		// ==========================================================================================================================
		if (!convidaTemAssinatura) return RecusaDeFusao.SemAssinatura;
		if (!convidadoTemAssinatura
			&& !(tipo == TipoDeFusao.Namek && convidadoEhNpcAbsorvivel))
			return RecusaDeFusao.SemAssinatura;
		if (mesmaPessoa) return RecusaDeFusao.EleMesmo;
		if (algumJaFundido) return RecusaDeFusao.JaFundido;
		if (algumNaRecarga) return RecusaDeFusao.NaRecarga;
		if (algumCaido) return RecusaDeFusao.Caido;
		if (!mesmaZona) return RecusaDeFusao.OutraZona;
		// O TETO E POR TIPO, e essa e a mudanca deste passe: a Danca e a Namekuseijin cobram o tile ao
		// lado (`TilesColados`, o `get_dist > 1` do DU e o `oview(1)` do Finale) e a Potara cobra os 20
		// tiles do `oview(usr,20)`, porque e ela que PUXA os dois. Ver `TilesDoConvite`.
		if (distanciaTiles >= 0 && distanciaTiles > TilesDoConvite(tipo)) return RecusaDeFusao.Longe;
		if (eleJaTemPedido) return RecusaDeFusao.JaTemPedido;

		// ============================ OS TRES PORTOES QUE SO A DANCA TEM ============================
		// A skill nos DOIS e pedido do dono e diverge do DM de olho aberto: la o verb mora no `obj`
		// que a skill concede (`SpaceRankSkills.dm:146`), entao **so o convidador precisa saber** e o
		// convidado so responde Sim/Nao. O dono pediu *"ambos precisam ter a SKILL pra aprender a
		// fazer"* -- e faz sentido: a Danca e uma coreografia A DOIS, e um dos dois nao conhecer os
		// passos e exatamente o que produz a fusao estragada no desenho.
		// ============================================================================================
		if (tipo == TipoDeFusao.Danca)
		{
			if (!euSeiDancar) return RecusaDeFusao.SemSkill;
			if (!eleSabeDancar) return RecusaDeFusao.OutroSemSkill;
			if (!MesmaRaca(racaA, racaB)) return RecusaDeFusao.RacaDiferente;
			if (RazaoDePoder(expressoA, expressoB) < LimiarDeProximidade) return RecusaDeFusao.PoderDesigual;
		}

		// ============================ O PORTAO DA NAMEKUSEIJIN, E ELE E DO DM ============================
		// `Fusion.dm:556-557`, as duas linhas literais:
		//
		//     if(M.Race!="Namekian") return
		//     if(usr.Race!="Namekian") return
		//
		// E SO ISSO. O verb do original nao pede skill, nao pede poder proximo e nao pede
		// <see cref="MesmaRaca"/> -- pede que os DOIS sejam Namekuseijin, que e outra pergunta.
		// (Ele pede `oview(1)`, e desde este passe o port cobra o mesmo: a Namekuseijin entra no
		// <see cref="TilesColados"/> junto com a Danca -- ver <see cref="TilesDoConvite"/>.)
		if (tipo == TipoDeFusao.Namek && !(EhNamekuseijin(racaA) && EhNamekuseijin(racaB)))
			return RecusaDeFusao.NaoEhNamekuseijin;

		return RecusaDeFusao.Pode;
	}

	/// <summary>
	/// ESTA RACA E NAMEKUSEIJIN? -- pelo id do catalogo (<see cref="Forms.Catalogo.RacaNamekuseijin"/>),
	/// e nao por uma string escrita aqui.
	///
	/// As tres tribos de Namek sao CLASSES do mesmo proto e nao racas irmas (ver
	/// `Combat/Regeneracao.cs:75-76`), entao uma comparacao so responde por todas -- que e o mesmo
	/// motivo pelo qual o DM tambem escreve `Race != "Namekian"` e mais nada.
	/// </summary>
	public static bool EhNamekuseijin(string raca) =>
		raca.Equals(Forms.Catalogo.RacaNamekuseijin, StringComparison.OrdinalIgnoreCase);

	// =====================================================================
	// A FUSAO NAMEKUSEIJIN -- a PERMANENTE
	// =====================================================================
	/// ============================ A PERGUNTA ABERTA FOI RESPONDIDA, E A CONSTANTE MORREU ============================
	/// Morava aqui uma `HerancaNaFusaoNamekuseijin = false`, escrita como pergunta em aberto: *"a
	/// Metamoro e a Potara herdam as skills dos dois e o maior stat de cada; o dono nunca disse se isso
	/// vale pra fusao PERMANENTE"*. Ela tinha dois consumidores de producao (o `Fundir` e o
	/// `PassarOControle`) e dois de bancada, e todos liam a CONSTANTE em vez de cravar zero, de
	/// proposito, esperando a resposta.
	///
	/// **A resposta veio por outro caminho, e ela apagou a pergunta.** O dono pediu a fusao
	/// Namekuseijin inteira -- e nela o outro **perde o personagem**, o que faz da fusao uma ABSORCAO e
	/// nao uma fusao viva (ver `Core/Social/AbsorcaoNamekuseijin.cs`). Com a Namekuseijin nao produzindo
	/// mais uma `FusaoAtiva`, o `Fundir` so recebe Danca e Potara: os dois `if` que liam a constante
	/// viraram sempre-verdadeiros e sairam, e ela ficou orfa. A regra da casa manda DELETAR ramo morto,
	/// e e o que esta linha registra pra quem vier procurar a constante pelo nome.
	///
	/// O que herda hoje, e de quem, esta em `AbsorcaoNamekuseijin.HerdaOsStats` / `HerdaAsSkills` -- e
	/// a resposta la e "so quando o absorvido e JOGADOR", que e a regra N4 do dono.
	/// ==========================================================================================================

	// =====================================================================
	// O NOME
	// =====================================================================
	/// <summary>
	/// O NOME DA FUSAO -- e ele **difere entre os dois tipos**, que e pedido explicito do dono:
	/// *"Potara e Metamoro dos MESMOS jogadores tem nomes DIFERENTES -- ex.: a metamoro pega a 1a
	/// metade do 1o + a 2a metade do 2o, e a potara o inverso"*.
	///
	/// ============================ E AQUI EU DIVIRJO DO DM, PORQUE O DM ESTA ERRADO ============================
	/// O original (`Fusion.dm:176-180`) faz:
	///
	///   `var/namecontrlos = copytext(Loser.name, round(lenglos/2))`
	///   `var/namecontrkep = copytext(Keeper.name, round(lengkep/2))`
	///   `FuseName = "[namecontrkep][namecontrlos]"`
	///
	/// `copytext(T, Start)` **sem o terceiro argumento vai do inicio ate o fim da string** -- entao
	/// as duas variaveis sao SEGUNDAS metades, e o DM cola a 2a com a 2a. Goku + Vegeta vira
	/// **"kueta"**, e nao "Gogeta". Alem disso a formula e UMA SO pros dois tipos, o que faria a
	/// Potara e a Metamoro dos mesmos dois jogadores terem o mesmo nome.
	///
	/// A regra do dono da "Goeta" na Danca e "Vegku" na Potara -- dois nomes, e os dois legiveis.
	/// ======================================================================================================
	/// </summary>
	/// <param name="convidou">Quem chamou. E ele quem controla, e e a 1a metade dele que abre o nome da Danca.</param>
	public static string NomeDaFusao(TipoDeFusao tipo, string convidou, string convidado)
	{
		string a = convidou.Trim(), b = convidado.Trim();
		if (a.Length == 0) a = "?";
		if (b.Length == 0) b = "?";

		// A POTARA E O INVERSO, literalmente: troca quem entra com a primeira metade.
		string nome = tipo == TipoDeFusao.Potara
			? PrimeiraMetade(b) + SegundaMetade(a)
			: PrimeiraMetade(a) + SegundaMetade(b);

		// O DM tem esta rede de seguranca (`Fusion.dm:181-184`) e ela continua fazendo falta: dois
		// nomes de uma letra podem produzir uma string vazia dependendo do arredondamento.
		return nome.Length > 0 ? char.ToUpperInvariant(nome[0]) + nome[1..] : $"{a} Fusion";
	}

	/// <summary>A primeira metade, com o meio ficando pra ela: "Goku" -> "Go", "Gohan" -> "Goh".</summary>
	public static string PrimeiraMetade(string s) => s[..((s.Length + 1) / 2)];

	/// <summary>A segunda metade: "Vegeta" -> "eta", "Gohan" -> "han".</summary>
	public static string SegundaMetade(string s) => s[(s.Length / 2)..];

	// =====================================================================
	// A APARENCIA DA FUSAO -- roupa e cabelo
	// =====================================================================
	/// <summary>
	/// A ROUPA DA METAMORO: **so o colete**, e o dono foi explicito (*"roupa: apenas `Metamoran
	/// Vest.png`"*).
	///
	/// ============================ O DM VESTE OUTRA COISA, E E ENGANO DELE ============================
	/// `Fusion.dm:217` poe `/obj/overlay/clothes/FusionPads` (`Clothes_FusionPads.dmi`) -- as OMBREIRAS,
	/// e nao o colete. O `Metamoran Vest.dmi` existe no disco do original e **nao e citado por uma linha
	/// de codigo em todo o `Code/`**: e arte comprada e nunca ligada. O dono pediu a peca certa.
	/// ============================================================================================
	///
	/// NOME DE ARQUIVO E NAO CAMINHO, pela mesma regra que o <see cref="VisualCatalog.Peca"/> ja
	/// defende: a fonte e o DM, e la a peca e citada por arquivo. Quem traduz nome em `res://` e o
	/// catalogo -- e no dia em que a pasta mudar, nada aqui envelhece.
	/// </summary>
	public const string PecaDoColeteMetamoran = "Metamoran Vest";

	/// <summary>
	/// A PECA DA POTARA -- `potara.dmi` (`Fusion.dm:218` e `:692`), a MESMA do original.
	///
	/// ============================ ELA NAO ESTA NO GUARDA-ROUPA, E DE PROPOSITO ============================
	/// O extrator recusa este arquivo por nome: `DmAppearanceScanner.Varrer` tem uma lista `fora` com
	/// `"Potara"`/`"potara"` dentro, e o comentario dela explica por que -- a pasta `Clothes/` do jogo e o
	/// deposito de **tudo** que vira overlay de corpo (olho, halo, rabo, brinco), e varre-la inteira poria
	/// "olhos" na grade de camisas da criacao de personagem.
	///
	/// Ou seja: `VisualCatalog.Peca("potara")` devolve NULO de proposito, e isso e a coisa certa -- ela
	/// **nao e roupa de vestir**, e um overlay de fusao. E e por isso que a aparencia da fusao nao passa
	/// pelo `Sanear` (ver `GameServer.Fusao.AparenciaDaFusao`): se passasse, o brinco seria descartado em
	/// silencio por uma regra que esta certa.
	/// ==================================================================================================
	/// </summary>
	public const string PecaDosBrincosPotara = "potara";

	/// <summary>
	/// A PECA QUE ESTE TIPO DE FUSAO VESTE POR CIMA, pelo NOME do arquivo.
	/// Vazio = nenhuma. (A Namekuseijin tambem nao vestia nada -- `Fusion.dm:271` nao tem ramo de roupa
	/// pra ela --, mas hoje ela nem chega aqui: ela ABSORVE, e quem absorve continua com a propria
	/// roupa. Ver `Core/Social/AbsorcaoNamekuseijin.cs`.)
	/// </summary>
	public static string PecaDe(TipoDeFusao t) => t switch
	{
		TipoDeFusao.Danca => PecaDoColeteMetamoran,
		TipoDeFusao.Potara => PecaDosBrincosPotara,
		_ => "",
	};

	/// <summary>
	/// A ROUPA DA FUSAO, montada. As duas regras do dono, e elas sao OPOSTAS uma da outra:
	///
	///   * **Metamoro** -- *"apenas `Metamoran Vest.png`"*: a peca SUBSTITUI o guarda-roupa inteiro. O
	///     colete metamoriano e um uniforme, e quem funde dancando veste o uniforme;
	///   * **Potara** -- *"`potara.png` + a roupa de quem convidou"*: a peca SOMA. O brinco e um
	///     acessorio, e o corpo continua vestido do que quem convidou estava vestindo.
	///
	/// <paramref name="caminhoDaPeca"/> nulo (arte que nao resolveu) NAO estraga a fusao: ela sai com a
	/// roupa de quem convidou, e quem denuncia a falta e o servidor -- em log, uma vez, e nao com um
	/// personagem pelado que ninguem sabe explicar.
	///
	/// **O TETO DO GUARDA-ROUPA E RESPEITADO** (<see cref="Appearance.MaxRoupa"/>): a peca da fusao entra
	/// SEMPRE, e o que sobrar de baixo e o que couber. Sem isto, um convidador com quatro pecas vestidas
	/// receberia a quinta e a fusao Potara sairia sem o brinco -- que e a coisa que a define.
	/// </summary>
	public static List<PecaDeRoupa> RoupaDaFusao(
		TipoDeFusao tipo, IReadOnlyList<PecaDeRoupa> doConvidador, string? caminhoDaPeca)
	{
		var fora = new List<PecaDeRoupa>();
		if (!string.IsNullOrEmpty(caminhoDaPeca)) fora.Add(new PecaDeRoupa(caminhoDaPeca));

		// A DANCA NAO HERDA NADA. Sem a peca (arte sumida) ela cai na roupa de quem convidou em vez de
		// deixar o corpo pelado -- ver o <summary>.
		bool herda = tipo != TipoDeFusao.Danca || fora.Count == 0;
		if (herda)
			foreach (PecaDeRoupa p in doConvidador)
			{
				// QUALIFICADO ATE O FIM: `Appearance` e o nome do NAMESPACE **e** o da classe, e a
			// resolucao de dentro de `Jandirus.Core.Social` acha o namespace primeiro.
			if (fora.Count >= Jandirus.Core.Appearance.Appearance.MaxRoupa) break;
				if (!fora.Any(x => x.Caminho == p.Caminho)) fora.Add(p);
			}

		return fora;
	}

	/// <summary>
	/// O ESTILO DE CABELO DO VEGITO, como ele se chama no catalogo deste port
	/// (`visual.json` -> `{"nome": "Vegito", "sprite": "res://Assets/Sprites/Hair/VegitoHairPVP.tres"}`).
	///
	/// **O NOME DE ESTILO E NAO O ARQUIVO**, e a diferenca importa: `Appearance.Cabelo` guarda o NOME, e
	/// e por ele que o `VisualCatalog.SpriteDoCabelo` acha a folha e que o `Sanear` reconhece o penteado.
	/// Escrever `VegitoHairPVP` aqui daria um cabelo que o catalogo nao conhece -- e o saneamento o
	/// trocaria por `Bald`, deixando a fusao CARECA.
	/// </summary>
	public const string EstiloDoVegito = "Vegito";

	/// <summary>
	/// ESTE PENTEADO E DA FAMILIA DO GOKU / DO VEGETA?
	///
	/// Por SUBSTRING e nao por igualdade, e o precedente e do proprio resolvedor de cabelo do cliente
	/// (`CabelosDeForma.Universal`, que ja pergunta `nome.Contains("Vegeta")` pra dar o SSJ4 do widow's
	/// peak): o catalogo tem `Vegeta`, `GT Vegeta` -- e sao o mesmo formato de cabeca. O mesmo vale pro
	/// dia em que entrar um `Goku Jr`.
	/// </summary>
	public static bool EhCabeloDeGoku(string estilo) =>
		estilo.Contains("Goku", StringComparison.OrdinalIgnoreCase);

	/// <inheritdoc cref="EhCabeloDeGoku"/>
	public static bool EhCabeloDeVegeta(string estilo) =>
		estilo.Contains("Vegeta", StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// O CABELO DA FUSAO. Pedido do dono, literal: *"se um tiver o cabelo do Vegeta e o outro do Goku, a
	/// fusao usa `VegitoHairPVP.png`"*.
	///
	/// ============================ O PADRAO E O CABELO DE QUEM CONVIDOU, E NAO UM TERCEIRO ============================
	/// A regra do Vegito e uma EXCECAO nomeada, e nao a regra geral. Fora dela vale a mesma linha que
	/// vale pra raca e pras transformacoes -- *"a fusao e da RACA de quem convidou e tem as
	/// TRANSFORMACOES de quem convidou"* --, e o cabelo e do mesmo lado: a fusao **e o corpo de quem
	/// convidou**, com o outro dentro. Inventar um penteado sorteado pros outros 3.363 pares possiveis
	/// seria desenho novo que ninguem pediu.
	/// ==========================================================================================================
	///
	/// NAO EXISTE NO DM: `VegitoHairPVP.dmi` esta la como escolha normal de criacao (`HairChoose.dm:255`,
	/// `:344`, `:470`) e nenhuma linha de `Fusion.dm` encosta em cabelo.
	/// </summary>
	public static string CabeloDaFusao(string cabeloDoConvidou, string cabeloDoConvidado)
	{
		bool par = (EhCabeloDeGoku(cabeloDoConvidou) && EhCabeloDeVegeta(cabeloDoConvidado))
				|| (EhCabeloDeVegeta(cabeloDoConvidou) && EhCabeloDeGoku(cabeloDoConvidado));
		return par ? EstiloDoVegito : cabeloDoConvidou;
	}

	/// <summary>
	/// O SUFIXO DE CABELO DO SSJ4 -- o mesmo texto que as SEIS entradas do catalogo de formas escrevem
	/// (`Formas.cs:3762`, `:3770`, `:3788` e as tres Primal Lendarias em `:4030`, `:4038`, `:4045`).
	///
	/// Constante aqui, e conferida contra aquele campo pela bancada, porque uma segunda grafia (`SSj4`)
	/// nao quebraria nada: o cabelo vermelho simplesmente **nao apareceria**, calado.
	/// </summary>
	public const string SufixoDoSsj4 = "SSJ4";

	/// <summary>
	/// ============================ O VERMELHO DO SSJ4 -- E SO NA DANCA ============================
	/// Pedido do dono, em duas voltas. Na primeira ele disse *"a fusao usa `Hair SSJ4 Gogeta.png` PINTADO
	/// DE VERMELHO -- e **toda** fusao tem cabelo vermelho no SSJ4, tendo ou nao o cabelo do Vegito"*, e
	/// era isso que estava escrito aqui. Na segunda ele CORRIGIU, e a correcao e a regra de hoje:
	///
	///   *"o ssj4 (e suas variantes) quando esta na fusao potara, o cabelo nao fica vermelho e sim na cor
	///   normal de cabelo q seria se n fosse uma fusao, so a fusao metamoro/danca q muda a cor do cabelo
	///   no ssj4"*
	///
	/// Ou seja o vermelho continua existindo, com o mesmo hexa e a mesma operacao -- ele so deixou de
	/// valer pra fusao inteira e passou a valer pra <see cref="TipoDeFusao.Danca"/>. A Potara volta ao
	/// cabelo normal, e a Namekuseijin cai naturalmente no mesmo lado (o dono nem precisou dize-lo: ela e
	/// de Namekuseijin, que nao tem SSJ4 nenhum).
	///
	/// ============================ ISTO NAO TEM UMA LINHA DE BYOND ATRAS ============================
	/// **Nem a regra velha, nem a correcao.** O `Fusion.dm` nao pinta cabelo de fusao em lugar nenhum: o
	/// unico cabelo colorido de fusao que o original tem e MANUAL (`Customize()`, `Fusion.dm:127-172`, um
	/// `input(... ) as color` que o proprio jogador escolhe), e ele nao olha forma nem `FType`. E o
	/// `Hair_SSJ4Gogeta.dmi` aparece uma vez so no DM inteiro (`HairChoose.dm:441`), como um dos tres
	/// cabelos de SSJ4 sorteados no NASCIMENTO de qualquer homem -- nada a ver com fundir.
	///
	/// Quem vier procurar a linha do DM que justifica isto **nao vai achar, e nao e por falta de olhar**:
	/// a fonte e o pedido do dono, transcrito acima. O que o DM da e o PRECEDENTE ESTRUTURAL de "efeito
	/// visual diferente por `FType`" -- `Fusion.dm:217-218` veste o colete so na Danca e o brinco so na
	/// Potara, e desfaz os dois em `:242-243`. E exatamente a forma desta funcao.
	/// ==============================================================================================
	///
	/// ============================ O HEXA E O DO PROPRIO JOGO, E NAO UM VERMELHO MEU ============================
	/// `e2331c` e `rgb(226,51,28)` -- o vermelho que o DM ja usa pra pintar cabelo, no `gdki_me()` do
	/// `/obj/overlay/hairs/hair` (`HairObject.dm:73-75`), que e o Super Saiyajin God. Este port ja o
	/// carrega como o vermelho do SSG. Usar o MESMO e o que o pedido manda ("o mesmo caminho de tinta que
	/// o resto do jogo"), e evita a segunda definicao de "vermelho" que divergiria da primeira.
	/// =====================================================================================================
	///
	/// ============================ E ELA SOMA, NAO E MATIZ -- MEDIDO NA FOLHA ============================
	/// A regra derivada do cliente e *"matiz quando a tinta cai num sprite que a FORMA trouxe"*
	/// (`CharacterVisual.VestirCabeloDaForma`), e o motivo escrito la e que o sprite trazido pela forma e
	/// sempre a arte DOURADA de Super Saiyajin -- somar cor em dourado da branco.
	///
	/// **A folha do SSJ4 nao e dourada.** Medidos os quatro tons de `Hair_SSJ4Gogeta.png` (7.091 pixels
	/// opacos), com o `luz*2` que o `tinta_modo = 1` multiplica:
	///
	///     #080808 (47,4%) -> 0,063     #4a4a4a (23,0%) -> 0,580
	///     #363636 (19,4%) -> 0,424     #505050 (10,2%) -> 0,627
	///
	/// Em MATIZ o piso -- que e **quase metade do cabelo** -- sairia a 6,3% da tinta, ou seja PRETO, e o
	/// teto nao passaria de 63% dela: um SSJ4 vermelho-escuro-quase-preto. E o mesmo tombo que este
	/// projeto ja levou duas vezes calibrando rampa so pelo topo (o Blue marinho, o Rose vinho) -- **o
	/// hexa escrito e o pixel mais ESCURO**, e aqui o mais escuro seria fundo de poco.
	///
	/// Em SOMA (o `ICON_ADD`, que e o que o DM faz) a mesma folha desenha, com este hexa:
	///
	///     #ea3b24  ->  #ff6952  ->  #ff7d66  ->  #ff836c
	///
	/// Quatro degraus distintos, do vermelho fundo ao claro, com o sombreado do desenho inteiro
	/// preservado -- que e exatamente o que "pintado de vermelho" quer dizer. E o piso continua sendo o
	/// hexa escrito (+8 do proprio desenho), entao a disciplina da rampa vale igual: quem quiser mexer
	/// nesta cor esta escolhendo o TOM MAIS ESCURO do cabelo.
	/// ==============================================================================================
	/// </summary>
	public const string VermelhoDoCabeloDaFusao = "e2331c";

	/// <summary>
	/// A TINTA QUE **ESTE TIPO DE** FUSAO POE NO CABELO DESTA FORMA, ou nulo (a esmagadora maioria).
	///
	/// UMA FUNCAO E NAO UM `if` NO CLIENTE: e o Core quem decide, e e aqui que entra a segunda forma no
	/// dia em que o dono pedir uma. O cliente so pergunta.
	///
	/// ============================ O TIPO E PARAMETRO, E ELE CUSTOU O PROTOCOLO ============================
	/// Ate a correcao do dono este metodo recebia so o sufixo, porque a resposta nao dependia do tipo --
	/// e por isso o tipo nem chegava aqui: o `S2C.PeerLook` carregava um BOOL ("este corpo e uma fusao"),
	/// o `World` guardava um `HashSet<int>` e o `CharacterVisual` um `bool _ehFusao`. Pra Potara e Danca
	/// darem respostas diferentes, o dado teve que ser levado ate o pixel -- o bool virou o TIPO nas
	/// quatro camadas. Ver `GameServer.TrocarAparencias`, `GameClient.PeerLooked`, `World._fusaoDaZona` e
	/// `CharacterVisual.MarcarFusao`.
	///
	/// **A FOLHA NAO MUDA JUNTO, E ISSO E DELIBERADO.** A fusao troca DUAS coisas no SSJ4: o penteado
	/// (todo mundo vira `Hair_SSJ4Gogeta`, ver `CabelosDeForma.FolhaDoSsj4DaFusao`) e a tinta. O dono
	/// falou de COR nas duas metades da frase -- *"o cabelo nao fica vermelho e sim na cor normal de
	/// cabelo"* e *"so a fusao metamoro/danca q muda a COR do cabelo"* --, entao so a tinta e condicional.
	/// A Potara em SSJ4 continua com a cabeca da fusao, na cor natural que a folha ja tem.
	/// =================================================================================================
	/// </summary>
	public static string? TintaDoCabeloDaFusao(TipoDeFusao tipo, string sufixoDaForma) =>
		tipo == TipoDeFusao.Danca
		&& string.Equals(sufixoDaForma, SufixoDoSsj4, StringComparison.OrdinalIgnoreCase)
			? VermelhoDoCabeloDaFusao
			: null;
}
