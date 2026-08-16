using Godot;
using Jandirus.Core.Forms;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// TRANSFORMACOES, LADO DO SERVIDOR.
///
/// Aqui e onde o multiplicador da forma vira poder de verdade: o <c>Fighter.ssjBuff</c> ja existia
/// no port da Etapa 3b (ele alimenta `formBuff` e o `tempBP *= BPBoost * formBuff` do
/// `powerlevel()`), e o que faltava era alguem escrever nele. Quem escreve e este arquivo, e so
/// ele -- o cliente nunca manda multiplicador nenhum, so pede "subir" ou "descer".
///
/// O CICLO DE UMA FORMA, por tick:
///   1. cobra o Ki (fracao do MaxKi por segundo, ver <see cref="Catalogo.DrenoPorSegundo"/>);
///   2. sobe a maestria (so se ganha DENTRO da forma -- e o unico eixo que nao se compra);
///   3. recalcula o multiplicador (maestria muda ele em degraus, ao vivo);
///   4. se o Ki acabou, DERRUBA pra base.
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// QUEM E ESTE PERSONAGEM PARA O CATALOGO -- e o funil UNICO dos gates.
	///
	/// ============================ POR QUE UM PERFIL E NAO SEIS PARAMETROS ============================
	/// Antes do rework o gate perguntava duas coisas (BP e maestria) e o resto era `if` de raca
	/// espalhado. Com nove linhas de transformacao seriam nove condicoes em cada um dos tres lugares
	/// que decidem forma (subir, DirectSSJ do admin, aba Formas do cliente) -- e o jeito de errar e
	/// classico: acrescentar a linha nova em dois dos tres e passar meses sem notar o terceiro.
	///
	/// Com o perfil, quem sabe responder "de quem e esta linha" e o <see cref="Catalogo"/>, uma vez.
	/// ============================================================================================
	/// </summary>
	private static PerfilDeFormas Perfil(ServerPlayer pl) => new(
		Raca: pl.Race,
		Classe: pl.Ficha.Class,
		Linhagem: pl.Ficha.SaiyanLineage,
		Diluido: SangueDiluido(pl),

		// `legendary` no DM e a CLASSE do Saiyajin (Birth.RollClass a sorteia do spread). O
		// "Legendary Primal Saiyan" NAO entra aqui de proposito: ele tem ladder proprio, e o
		// Catalogo desempata pela classe exata.
		Legendary: pl.Ficha.Class.Contains("Legendary", StringComparison.OrdinalIgnoreCase)
				   && !pl.Ficha.Class.Contains("Primal", StringComparison.OrdinalIgnoreCase),

		// `stathalfbreed.dm:73`: `if(Class == "Future Lineage") FutureLineage = 1`. E classe de
		// meio-Saiyajin, nao linhagem de Saiyajin puro -- por isso le o Class e nao o SaiyanLineage.
		Futuro: pl.Ficha.Class.Equals("Future Lineage", StringComparison.OrdinalIgnoreCase),

		// -1 = NAO DESPERTOU. O zero seria "despertou com 0% de maestria", que e outra coisa: o SSG
		// pede exatamente 0%, entao confundir os dois daria a forma divina pra quem nunca a viu.
		GodKi: pl.Ficha.godki is { awakened: true } g ? g.mastery : -1,
		// AS DUAS ENERGIAS SAO A **REAL**, e nao a atual: o catalogo pergunta "esta forma esta
		// destravada?", e destravar e permanente. A precisao atual escala BONUS, nao abre portas --
		// senao a forma cairia sozinha quando a energia drenasse no meio da luta.
		EnergiaUe: pl.PoderDaDestruicao.Aprendida ? pl.PoderDaDestruicao.Real : 0,
		ProficienciaUi: pl.UltraInstinct.Aprendida ? pl.UltraInstinct.Real : 0,

		// A UNICA LINHA DESTE PERFIL QUE OLHA O RELOGIO -- e o oposto das duas de cima: as duas
		// energias sao a REAL porque destravar e permanente, e a raiva e um INSTANTE que expira.
		// Derivada do prazo e nao de um campo guardado: um campo precisaria de alguem pra apaga-lo,
		// e o alguem seria um tick -- que e onde uma raiva esquecida ficaria acesa pra sempre.
		// Passado o prazo, isto ja e `Nenhuma` sem ninguem fazer nada.
		Raiva: NivelDaRaivaDe(pl),

		// ============================ AS FLAGS QUE AS SKILLS ESCREVERAM NESTE CORPO ============================
		// O quarto canal do `EfeitosDeSkill` -- ATRIBUICAO -- ja depositava tudo aqui e **ninguem lia**:
		// `Fighter.FlagsDeSkill` guarda ate os nomes que o `Fighter` nao tem como campo, e `snamek` e
		// `hasayyform` sao exatamente esses dois. Ver `FormaDef.PedeFlag`.
		//
		// PASSA-SE O DICIONARIO INTEIRO e nao dois booleanos, e a razao e a mesma do perfil existir: a
		// proxima forma comprada nao vai precisar de campo novo aqui. E ele e o objeto VIVO do lutador
		// -- `Aplicar` o TROCA por um novo a cada recalculo em vez de mutar o antigo (`EfeitosDeSkill`,
		// ultima linha), entao o perfil nao guarda referencia pra nada que mude debaixo dele.
		// ====================================================================================================
		FlagsDeSkill: pl.Ficha.FlagsDeSkill,

		// O DEGRAU DO BIO-ANDROIDE -- a metade "forma perfeita" do gate da Super Perfeita. Zero pra
		// todo mundo que nao saiu de um tanque, que e o lado seguro (ver `PerfilDeFormas.EstagioBio`).
		EstagioBio: pl.Ficha.bio_stage,

		// E O BYPASS DE SUPER SAIYAJIN. Ele nao e "tem sangue Saiyajin": e o `canSSJ` do DM, que hoje
		// so o bio nascido de DNA Saiyajin recebe. Um Saiyajin de verdade continua entrando pela
		// raca, duas linhas acima do consumidor disto.
		CanSsj: pl.Ficha.canSSJ);

	/// <summary>
	/// EM QUE RAIVA ESTE CORPO ESTA -- a mais alta das duas janelas que ainda estiver aberta.
	///
	/// A ORDEM DOS DOIS `if` E A REGRA: a furia extrema vem primeiro porque ela SATISFAZ a lendaria
	/// (ver <see cref="NivelDeRaiva"/>). Invertidos, um corpo com as duas acesas se anunciaria
	/// `Lendaria` e o SSJ1 seria recusado a quem acabou de ver um amigo morrer.
	/// </summary>
	private static NivelDeRaiva NivelDaRaivaDe(ServerPlayer pl)
	{
		long agora = NowMs();
		if (pl.FuriaExtremaAte > agora) return NivelDeRaiva.Extrema;
		if (pl.RaivaLendariaAte > agora) return NivelDeRaiva.Lendaria;
		return NivelDeRaiva.Nenhuma;
	}

	/// <summary>
	/// QUANTO TEMPO A FOLGA INTEIRA DE RAIVA LEVARIA PRA ESCORRER -- 1600 segundos.
	///
	/// `Stats.dm:443` tira `((MaxAnger-100)/8000)` por volta do laco `mob/proc/Stats()`, e aquele
	/// laco dorme `sleep(sleep_tiem)` com `sleep_tiem = 2` (`Stats.dm:125` e `:511`), ou seja 0,2 s
	/// por volta -- **nao** e o `GlobalStats()` de `sleep(3)`. 8000 voltas x 0,2 s = 1600 s.
	///
	/// E E DE PROPOSITO QUE ISTO SEJA MUITO MAIOR QUE <see cref="SegundosDeRaiva"/>: no DM o
	/// gotejamento quase nao aparece (26,7 min contra 2 min de prazo), e quem realmente derruba a
	/// raiva e o `rageExpire`. Portar so o gotejamento e esquecer o prazo daria uma raiva que dura
	/// meia hora; portar so o prazo e esquecer o gotejamento daria um degrau perfeitamente reto que
	/// cai de 2x pra 1x sem aviso. Os dois estao aqui, e o prazo continua mandando.
	/// </summary>
	private const double SegundosDeGotejamento = 1600;

	/// <summary>
	/// ============================ A RAIVA COMO **NUMERO** -- E ELA E DERIVADA DAS JANELAS ============================
	/// <see cref="NivelDaRaivaDe"/> devolve o DEGRAU (quem pode virar o que); isto devolve a
	/// MAGNITUDE (quanto de poder a raiva vale), que e o `Anger` do DM e a unica entrada do
	/// `angerBuff` (`Fighter.Power.cs:56`). As duas leem exatamente o mesmo estado -- as duas
	/// janelas --, e por isso nao ha como uma dizer "calmo" enquanto a outra da 2x.
	///
	/// ============================ POR QUE A SETA APONTA **DESTE** LADO ============================
	/// A pergunta que sobrou desta sessao foi: as janelas viram derivadas do `Anger`, ou o `Anger`
	/// vira derivado das janelas? Tem que ser este lado, por duas razoes que nao sao de gosto:
	///
	///   1. **O DEGRAU NAO CABE NUM NUMERO.** No DM `Do_Anger_Stuff(0)` e `Do_Anger_Stuff(1)`
	///      escrevem o MESMO `Anger = max(Anger, MaxAnger)` (`Murder.dm:112`) -- o que muda entre
	///      "vi um amigo cair" e "vi um amigo morrer" e a FONTE, nao a intensidade. Derivar o degrau
	///      de um limiar (`Anger >= X` = Extrema) seria reintroduzir a regua de cinco degraus do
	///      `Emotion` (`Stats.dm:445-449`), que e justamente o que o <see cref="NivelDeRaiva"/>
	///      substituiu a pedido do dono (o Legendary compra o dele mais barato, ver
	///      <see cref="Catalogo.RaivaExigida"/>). Ou seja: derivar pro outro lado nao unificaria
	///      nada -- desfaria uma decisao ja tomada.
	///
	///   2. **O `Fighter` NAO TEM RELOGIO.** O Core nao conhece `NowMs()` nem o `ServerPlayer`. Um
	///      `Anger` autoritativo teria que ser BAIXADO por alguem, e esse alguem seria um tique --
	///      que e exatamente onde uma raiva esquecida fica acesa pra sempre. Foi por isso que a
	///      versao anterior deste arquivo se RECUSOU a escrever `Anger` (o paragrafo esta preservado
	///      no cabecalho do <see cref="AmigoAbatido"/>), e a recusa estava certa. O que mudou nao foi
	///      a coragem: e que agora o numero **nao e guardado** -- ele e recalculado do prazo. Uma
	///      janela vencida devolve 100 sem ninguem apagar nada, que e a mesma propriedade pela qual o
	///      <see cref="Perfil"/> ja lia o degrau do relogio.
	///
	/// FUNCAO PURA DO RELOGIO, e isso importa alem da elegancia: como o resultado nasce do PRAZO e
	/// nao de uma subtracao acumulada, chamar isto a 5 Hz, a 30 Hz ou duas vezes no mesmo quadro da
	/// o mesmo numero. Um decaimento por acumulacao (`Anger -= passo`) mudaria de velocidade com a
	/// cadencia de quem chama -- e este port ja pagou esse preco uma vez (ver a nota de unidade de
	/// tempo do `sleep(N)`).
	/// ==================================================================================================================
	/// </summary>
	private static double RaivaComoNumero(ServerPlayer pl)
	{
		long agora = NowMs();

		// A MAIOR DAS DUAS JANELAS, pelo mesmo motivo do `NivelDaRaivaDe`: um nocaute chegando no
		// meio de um luto nao pode encurtar o luto, e a raiva que vale e a que ainda corre.
		long ate = Math.Max(pl.FuriaExtremaAte, pl.RaivaLendariaAte);

		// PRAZO VENCIDO = CALMA SECA, e nao um decaimento suave ate la. E literalmente
		// `Stats.dm:438-441`: `rageExpire = 0; Anger = 100`. O corte e do DM, e ele e o que impede
		// que "ver um amigo morrer" vire 2x de BP permanente -- o motivo pelo qual este sistema
		// ficou parado.
		if (ate <= agora) return 100;

		// E O GOTEJAMENTO POR CIMA (`Stats.dm:443`). A folga e `MaxAnger - 100` porque 100 e o piso
		// (`Stats.dm:444`) e tambem o "1,0x" do `angerBuff` -- raiva nenhuma nao e zero, e 100.
		double folga = Math.Max(0, pl.Ficha.MaxAnger - 100);
		double decorrido = Math.Max(0, SegundosDeRaiva - (ate - agora) / 1000.0);
		return Math.Max(100, pl.Ficha.MaxAnger - folga * decorrido / SegundosDeGotejamento);
	}

	/// <summary>
	/// ESCREVE A RAIVA NA FICHA -- **o unico lugar do jogo que toca o `Fighter.Anger`**.
	///
	/// O Core precisa do numero em campo porque e la que a conta de poder mora
	/// (`Fighter.Power.cs:56`), e o Core nao pode perguntar as horas. Entao este metodo e a ponte:
	/// ele copia a funcao pura de <see cref="RaivaComoNumero"/> pra dentro da ficha, e nada mais.
	/// Ninguem "acumula" raiva aqui; sobrescrever com o valor derivado e o ponto.
	///
	/// ONDE ELE E CHAMADO, e por que sao esses dois lugares:
	///   * <see cref="TickFichas"/>, a 5 Hz -- que e exatamente a cadencia do `Stats()` do DM
	///     (`sleep(2)`), e o tique que roda `Statify` + `PowerLevel`;
	///   * <see cref="RepercutirPoder"/> -- o funil que recalcula o poder FORA do tique (toda
	///     transformacao passa por ele). Sem isto, quem se transformasse no instante seguinte a um
	///     luto calcularia o `expressedBP` com a raiva de antes, por ate 200 ms -- a mesma familia de
	///     defeito que aquele metodo existe pra fechar.
	/// E o <see cref="AmigoAbatido"/> chama tambem, pra a raiva valer NO INSTANTE do luto: no DM o
	/// `Anger` e escrito dentro do proprio `Do_Anger_Stuff` (`Murder.dm:112`), antes mesmo de o
	/// `anger_will_transform()` da linha seguinte ler o resultado.
	///
	/// A FRASE DA CALMA SAI NA **BORDA**, e a borda e detectada sem campo novo: o valor que ainda
	/// esta na ficha e o da ultima passada. `Stats.dm:441` so imprime no tique em que o prazo vence,
	/// e nao em todo tique de calma -- imprimir por estado encheria o chat de "voce se sente calmo"
	/// pra sempre.
	/// </summary>
	private void ProjetarRaiva(ServerPlayer pl)
	{
		double antes = pl.Ficha.Anger;
		double depois = RaivaComoNumero(pl);
		pl.Ficha.Anger = depois;

		if (antes > 100 && depois <= 100)
			Avisar(pl, "sua fúria se apaga e você se sente calmo de novo.");
	}

	/// <summary>
	/// Quem tem ALGUMA escada de transformacao. Era `pl.Race is "Saiyan" or "Halfbreed"`, e passou a
	/// ser derivado: quem tem linha aberta no catalogo tem escada, inclusive o nao-Saiyajin que
	/// despertou o ki divino -- que antes ouvia "sua raca nao tem essa escada" com o SSG na mao.
	/// </summary>
	private static bool TemEscada(ServerPlayer pl) => Catalogo.LinhasAbertas(Perfil(pl)).Count > 0;

	/// <summary>
	/// Sangue diluido puxa a base do SSJ1 de 2 pra 1,35 (`ssj1base` nerfado).
	///
	/// PELA CONSTANTE DO CATALOGO e nao pelo literal: a grafia `"Halfbreed"` ja custou a escada
	/// inteira do meio-Saiyajin uma vez (ver `Catalogo.EhSaiyajin`), e duas copias dela sao duas
	/// chances de a proxima pessoa escrever "Half-Saiyan" num dos dois lugares e diluir metade das
	/// contas de metade dos personagens.
	/// </summary>
	/// <remarks>
	/// ============================ O `canSSJ` ENTRA AQUI, E ISTO E UMA DECISAO DECLARADA ============================
	/// `nerfSSJ()` (`Transformation Controls.dm:108-117`) e o que o DM roda em TODA passada da escada
	/// quando a via e `canSSJ`, e ele rebaixa os multiplicadores base: `ssjmult = 1.35`,
	/// `ultrassjmult = 1.45`, `ssj2mult = 1.75`, `ssj3mult = 2`, `ssj4mult = 1.75`. O comentario da
	/// linha 2 daquele arquivo diz o proposito numa frase: *"If this is ticked to 1, SSJ is weaker."*
	///
	/// **O PRIMEIRO NUMERO DELE E EXATAMENTE O `Ssj1BaseDiluido` QUE ESTE PORT JA TEM: 1,35.** Nao e
	/// coincidencia -- o meio-Saiyajin do DM e nerfado pelo mesmo mecanismo (`stathalfbreed.dm`
	/// reescreve os mesmos `mob/var`), e o `FormaDef.MultDiluido` do catalogo nasceu pra ele. Entao o
	/// bio via `canSSJ` percorre a linha DILUIDA: o SSJ1 e os dois grades saem em 1,35 / 2,03 / 2,70
	/// em vez de 2 / 3 / 4, com uma linha em vez de uma segunda tabela de nerf.
	///
	/// **O QUE ISSO CUSTA, DITO EM VOZ ALTA:** do SSJ2 pra cima o port nao tem multiplicador
	/// diluido em entrada nenhuma -- os valores sao fixos pra todo mundo (SSJ2 4-10x, SSJ3 16x, SSJ4
	/// 20-40x) porque foi assim que a escada foi portada, ANTES desta sessao e pra todas as racas.
	/// Um `MultCanSsj` proprio so pro bio seria uma terceira tabela paralela a duas que ja divergem
	/// do DM do mesmo jeito, e valeria pra uma forma (o SSJ2 do bio) que so se alcanca morrendo com
	/// a forma perfeita e o SSJ1 100% dominado. Fica anotado como divida, e nao fingido como porte.
	/// ==========================================================================================================
	/// </remarks>
	private static bool SangueDiluido(ServerPlayer pl) =>
		string.Equals(pl.Race, Catalogo.RacaMeioSaiyajin, StringComparison.OrdinalIgnoreCase)
		|| pl.Ficha.canSSJ;

	/// <summary>
	/// CONCEDE O MISTICO. **E ESTE O GANCHO DO RITUAL DO KAIOSHIN** -- o ritual em si nao existe
	/// ainda, e quando existir e daqui que ele fala.
	///
	/// ============================ O QUE O RITUAL VAI PRECISAR FAZER, INTEIRO ============================
	/// Chamar isto. So isto. A forma e `SoPorConcessao` (ver `FormaDef`), entao ela e recusada com
	/// `RecusaForma.NaoConcedida` por qualquer caminho -- tecla C, aba Formas, `Proxima` -- ate esta
	/// linha rodar, e passa a ser oferecida no instante seguinte, pra QUALQUER raca. Nao ha porta de
	/// BP, nem maestria, nem degrau anterior: e um dom.
	///
	/// E ele nao precisa de campo novo no save: `Liberadas` ja vai pro disco (`CharacterStore`,
	/// `FormasDespertadas`) e ja volta na carga. E o MESMO caminho que o Oozaru Dourado dominado usa
	/// pra abrir o SSJ4, e foi escolhido por isso -- um jeito so de "esta forma e sua" no projeto.
	///
	/// `Liberar` E NAO `Entrar`, e a diferenca importa: `Entrar` poria o corpo na forma na hora e
	/// **queimaria a estreia** (o `EstreiaVista`), ou seja o jogador perderia a cinematica do
	/// Mistico no proprio ritual que a merece. Concedido, ele assume quando quiser -- e a cena roda
	/// na primeira vez que ele subir sozinho.
	///
	/// Devolve FALSE quando o alvo ja tinha o dom, pra o ritual nao anunciar duas vezes.
	/// ================================================================================================
	/// </summary>
	internal bool ConcederMistico(ServerPlayer alvo, string dequem)
	{
		if (!alvo.Forma.Liberar(Catalogo.IdMistico)) return false;

		GD.Print($"[server] {alvo.Name} recebeu o MISTICO ({dequem})");
		Avisar(alvo, "algo em voce se abre: o Mistico e seu -- todo o potencial, de uma vez.");
		return true;
	}

	/// <summary>
	/// Quanto tempo uma janela de raiva dura -- as DUAS, e o mesmo prazo. O `rageExpire` do DM
	/// (`Murder.dm:113`): 1200 decimos de segundo. La o prazo tambem nao muda com o `extreme`; o
	/// que o parametro muda e o `Emotion` que sai e a cinematica. Constante e nao numero solto
	/// porque a bancada precisa dizer o mesmo prazo.
	/// </summary>
	internal const double SegundosDeRaiva = 120;

	/// <summary>
	/// ============================ UM AMIGO FOI ABATIDO -- **E ESTE E O GANCHO DA AMIZADE** ============================
	/// Este metodo e o ponto UNICO por onde a raiva entra no jogo, e desde a sessao do Known-People
	/// ele tem dono: quem o chama e o <see cref="LutoNaVizinhanca"/> (`GameServer.Convivio.cs`), a
	/// partir do funil `AoPerderALuta` -- ou seja, de dentro da resolucao de um golpe que derrubou ou
	/// matou alguem. Quem responde "e amigo?" e o `Core.Social.Convivio`.
	///
	/// Os paragrafos abaixo foram escritos quando ele ainda era mudo, e continuam valendo: eles
	/// explicam por que ele e UM gancho com o grau como parametro, e por que ele NAO mexe no BP.
	///
	/// ============================ UM GANCHO SO, COM O GRAU COMO PARAMETRO ============================
	/// Porque no DM tambem e um so: `mob/proc/Do_Anger_Stuff(var/extreme = 0)` (`Murder.dm:110`).
	/// Dois metodos (`AmigoMorreu` / `AmigoCaiu`) seriam dois lugares pra o prazo divergir e dois
	/// lugares pra a amizade lembrar de ligar -- e o modo de falha deste port, repetido em varias
	/// sessoes, e exatamente esse: ligar num chamador e esquecer do outro.
	///
	/// ============================ O QUE FOI LIGADO, EXATAMENTE ============================
	/// Duas chamadas, as duas dentro do <see cref="LutoNaVizinhanca"/>, e as duas nascendo do funil
	/// unico da derrota (`AoPerderALuta`, que os quatro pontos de "fulano perdeu pra beltrano"
	/// chamam):
	///
	///   * um jogador MORREU: <see cref="NivelDeRaiva.Extrema"/> pra cada amigo que estava vendo.
	///     No DM e `Death.dm:73-85` (`M.Do_Anger_Stuff(1)`, *"friend was KILLED by an enemy ->
	///     EXTREMELY enraged"*);
	///   * um jogador foi NOCAUTEADO: <see cref="NivelDeRaiva.Lendaria"/>. No DM e `KO.dm:29-41`.
	///
	/// FICOU DE FORA o `MajinSaga.dm:173` (ver um amigo ser ABSORVIDO vale o mesmo que ve-lo
	/// morrer): a absorcao do Majin **nao existe neste port** -- nao ha `majin_absorb`, bolso
	/// dimensional nem verb de absorver. E chamada sem chamador, e o dia em que a saga vier ela
	/// chama isto com `Extrema` e acabou.
	///
	/// Quem responde "e amigo?" e o `Core.Social.Convivio` (`is_friend()`, `FRIEND_REQ = 50`). **E a
	/// PORTA DESTE GANCHO MUDOU DE PRECO a pedido do dono**: o `ACQUAINTANCE_CAP = 49` do DM nao foi
	/// portado, entao hoje a convivencia sozinha atravessa o 50 -- **25 minutos ao lado de alguem
	/// bastam pra que a morte dele possa acender o SSJ1**, sem pedido nenhum. Ver o cabecalho do
	/// `Convivio`, onde a divergencia esta escrita com o que se ganha e o que se perde.
	///
	/// ============================ E O QUE **NAO** DISPARA ISTO ============================
	/// Morte sem algoz. O DM so enfurece com um `deathKiller` de combate na mao (`Death.dm:75`,
	/// *"no rage for friendly duels or environmental death"*), e aqui a guarda e o proprio ponto de
	/// chamada: quem cai de fome, por gravidade ou na propria explosao nao passa pelo
	/// `AoPerderALuta`. Nem duelo entre amigos: a vitima e quem diz se aquilo foi um assassinato
	/// (`Convivio.AlgozEhInimigo`), e sem isso o treino viraria fabrica de Super Saiyajin.
	///
	/// A bancada `raiva` [8] continua VARRENDO OS FONTES, agora pro avesso: ela reprova se o gancho
	/// ficar sem chamador de producao, ou se aparecer um segundo. E o `OConvivioAoVivo`
	/// (`GameServer.ConvivioTeste.cs`) percorre a corrente inteira, do convivio ate a tecla C.
	///
	/// ============================ O BUFF DE BP DA RAIVA -- A DIVIDA QUE ESTE CABECALHO ANUNCIAVA ============================
	/// O paragrafo que ficou aqui por varias sessoes dizia, com razao, que este gancho abria a
	/// janela mas **nao** dava o buff: o `Do_Anger_Stuff` do DM faz as duas coisas
	/// (`Murder.dm:112-113`, `Anger = max(Anger, MaxAnger)` + `rageExpire`), e escrever `MaxAnger`
	/// aqui daria ate 2x de BP PERMANENTE ao primeiro enlutado da historia do servidor, porque
	/// **o port nao tinha decaimento de raiva**. A recusa estava certa.
	///
	/// **A divida esta fechada, e o que faltava era menor do que aquele paragrafo supunha.** O
	/// decaimento do DM nao e um sistema: e `Anger = 100` no instante em que o prazo vence
	/// (`Stats.dm:438-441`) -- e o prazo ja estava portado aqui, com a mesma constante
	/// (<see cref="SegundosDeRaiva"/>). O que o gotejamento de `Stats.dm:443` faz por cima leva
	/// 26,7 min pra drenar a folga inteira, contra 2 min de janela; ele arredonda a curva e nunca
	/// decide nada. Hoje os dois estao em <see cref="RaivaComoNumero"/>, e o `Anger` **nao e
	/// guardado**: ele e recalculado do prazo a cada leitura, entao a janela vencida devolve 100
	/// sozinha. Nao ha 2x permanente possivel -- nao ha onde ele ficaria guardado.
	///
	/// Por isso a linha do <see cref="ProjetarRaiva"/> logo abaixo NAO e um segundo caminho de
	/// poder: ela nao soma nada, so faz o numero derivado chegar a ficha no MESMO instante em que a
	/// janela abriu (no DM o `Anger` ja esta escrito quando a linha seguinte, o
	/// `anger_will_transform()`, o le). Sem ela o buff so apareceria no proximo tique de ficha.
	/// ==================================================================================================================
	/// </summary>
	/// <param name="enlutado">Quem viu. E ele que entra em raiva -- nao quem caiu.</param>
	/// <param name="nomeDoAmigo">Nome do amigo, so pra mensagem.</param>
	/// <param name="grau">
	/// <see cref="NivelDeRaiva.Extrema"/> pra morte/absorcao, <see cref="NivelDeRaiva.Lendaria"/>
	/// pra nocaute. <see cref="NivelDeRaiva.Nenhuma"/> nao faz nada -- e o `return` que impede um
	/// chamador distraido de "acender" calma e zerar a janela de quem estava em luto.
	/// </param>
	/// <returns>
	/// TRUE quando a raiva daquele grau acabou de ERUPCIONAR; FALSE quando ela ja estava acesa e
	/// isto foi so um prolongamento (ou quando o grau foi `Nenhuma`). E a distincao do `wasRaging`
	/// do DM (`Murder.dm:112`), que existe pra a cinematica de raiva nao tocar duas vezes seguidas
	/// -- quem ligar a cena que leia isto.
	/// </returns>
	internal bool AmigoAbatido(ServerPlayer enlutado, string nomeDoAmigo, NivelDeRaiva grau)
	{
		if (grau == NivelDeRaiva.Nenhuma) return false;

		long agora = NowMs();
		bool jaEstava = NivelDaRaivaDe(enlutado) >= grau;

		// ============================ O `wasRaging` DO DM E OUTRA PERGUNTA -- E ELA E DA CENA ============================
		// `jaEstava` logo acima e "ja estava neste GRAU" e serve ao valor de retorno (erupcao deste
		// grau x prolongamento dele). O `wasRaging` de `Murder.dm:111` e mais largo: `rageExpire >
		// world.time`, ou seja **qualquer** raiva aberta -- e e ele, e nao o outro, que decide a
		// cinematica na linha `:119`.
		//
		// A diferenca aparece num caso real e comum: um amigo cai (janela lendaria abre, sem cena) e
		// dez segundos depois outro morre. `jaEstava` da falso -- e uma erupcao EXTREMA de verdade, o
		// grau subiu --, mas o corpo ja estava enfurecido e o DM nao toca a cena de novo. Sao dois
		// conceitos e por isso sao duas leituras, as duas do mesmo estado e nenhuma guardada.
		// =========================================================================================================
		bool estavaEmFuria = NivelDaRaivaDe(enlutado) != NivelDeRaiva.Nenhuma;

		// `=` E NAO `max(...)`: o prazo REINICIA a cada evento, e nunca soma. Somar era a anomalia
		// do 20x que o DM ja pagou uma vez (`Murder.dm:112`, *"never stack, sum, or multiply"*).
		//
		// E CADA GRAU MEXE **SO NA PROPRIA JANELA**: um nocaute nao encurta nem prolonga um luto em
		// andamento, e uma morte nao precisa reacender a janela lendaria porque a extrema ja a
		// satisfaz (`NivelDeRaiva` e ordenado). Sem essa separacao, "um amigo caiu" logo depois de
		// "um amigo morreu" rebaixaria a raiva de quem esta de luto.
		AcenderJanelaDeRaiva(enlutado, grau, agora + (long)(SegundosDeRaiva * 1000));

		GD.Print($"[server] {enlutado.Name}: RAIVA {grau} ({nomeDoAmigo})"
				 + (jaEstava ? "  <- prolongada" : ""));
		Avisar(enlutado, grau == NivelDeRaiva.Extrema
			? $"{nomeDoAmigo} se foi. Alguma coisa dentro de voce se PARTE."
			: $"{nomeDoAmigo} cai na sua frente, e voce nao chegou a tempo.");

		// A CENA, DEPOIS DO AVISO -- a ordem e a do DM (`Murder.dm:119` vem depois das tres escritas
		// de `:112-114`), e ela importa por um motivo so: a `AFuriaVaiVirarForma` la dentro le a raiva
		// que acabou de ser escrita, exatamente como o `anger_will_transform()` do original.
		TalvezACenaDaFuria(enlutado, grau, estavaEmFuria, agora);

		return !jaEstava;
	}

	/// <summary>
	/// ACENDE A JANELA DE RAIVA. **E O UNICO LUGAR DO SERVIDOR QUE ESCREVE ESSES DOIS PRAZOS.**
	///
	/// ============================ POR QUE ISTO VIROU FUNCAO ============================
	/// Ate aqui as duas linhas moravam soltas dentro do <see cref="AmigoAbatido"/>, e isso bastava
	/// enquanto o luto era o unico jeito de alguem se enfurecer. **O discipulado abriu o segundo**:
	/// o mestre que PROVOCA o aluno (`mst_ignite_anger`, `MasterStudent.dm:540`) acende a mesma
	/// janela sem que ninguem tenha caido -- nao ha amigo abatido, nao ha luto, nao ha cena.
	///
	/// Podiam ter virado duas escritas em dois arquivos. Nao viraram, e a razao esta no proprio
	/// <see cref="Jandirus.Core.Stats.Fighter"/> (`:225`): a raiva e **campo derivado**, e o
	/// contrato dele e "quem escreve e UM lugar so". Dois escritores e onde o
	/// <see cref="ProjetarRaiva"/> passa a ser esquecido num deles -- e uma raiva escrita sem
	/// projetar so vale no tique seguinte, o que em combate quer dizer "as vezes o SSJ1 nao vem".
	/// ==============================================================================
	/// </summary>
	/// <param name="grau">
	/// <see cref="NivelDeRaiva.Nenhuma"/> nao faz nada -- o mesmo `return` de guarda do
	/// <see cref="AmigoAbatido"/>, e aqui ele importa mais: o despertar assistido pergunta ao
	/// catalogo qual raiva a forma exige, e a maioria das formas nao exige nenhuma.
	/// </param>
	private void AcenderJanelaDeRaiva(ServerPlayer pl, NivelDeRaiva grau, long prazo)
	{
		if (grau == NivelDeRaiva.Nenhuma) return;
		if (grau == NivelDeRaiva.Extrema) pl.FuriaExtremaAte = prazo;
		else pl.RaivaLendariaAte = prazo;

		// E A RAIVA VALE JA. Ver o bloco do buff no cabecalho do `AmigoAbatido`: isto nao acumula
		// nada, so traz o numero derivado (que a janela acima acabou de mudar) pra dentro da ficha
		// antes que qualquer coisa leia o poder deste corpo neste mesmo quadro.
		ProjetarRaiva(pl);
	}

	/// <summary>
	/// ============================ A CINEMATICA DA FURIA EXTREMA -- `AngerCinematic()`, `Murder.dm:136` ============================
	/// Quatro condicoes, e as quatro sao literais do DM (`Murder.dm:119`):
	/// `if(extreme &amp;&amp; !wasRaging &amp;&amp; client &amp;&amp; !anger_will_transform()) AngerCinematic()`, mais a recarga
	/// que o proprio proc guarda (`:139-140`).
	///
	///   1. **GRAU EXTREMO.** Ver um amigo CAIR aplica o buff e nao toca cena nenhuma; o original diz
	///      isso no comentario do `Do_Anger_Stuff` -- *"A friend simply going down ('very angry')
	///      applies the rage buff but plays NO cinematic/music (extreme=0)"*.
	///   2. **NAO ESTAVA JA ENFURECIDO** (o `wasRaging`, ver o bloco no <see cref="AmigoAbatido"/>):
	///      uma briga em grupo com quatro amigos caindo nao vale quatro cenas.
	///   3. **NAO E CORPO SEM DONO.** O `client` do DM -- NPC nao assiste a cinematica. Aqui a
	///      pergunta e o `Peer` nulo, que e a mesma coisa neste port (ver o cabecalho do clone).
	///   4. **A RAIVA NAO VAI VIRAR TRANSFORMACAO** -- ver <see cref="AFuriaVaiVirarForma"/>.
	///
	/// ============================ E POR QUE ELA MORA AQUI, E NAO NO CHAMADOR ============================
	/// O cabecalho do <see cref="AmigoAbatido"/> dizia *"quem ligar a cena que leia isto"*, apontando o
	/// valor de retorno. Ligar a cena LA seria o defeito que aquele mesmo cabecalho ja explica pro
	/// gancho ser um so: o `LutoNaVizinhanca` nao e o unico chamador possivel (a absorcao Majin entra
	/// por aqui no dia em que a saga vier), e cada chamador novo teria que lembrar de tocar a cena.
	///
	/// No DM ela tambem mora dentro do `Do_Anger_Stuff`, e pelo mesmo motivo.
	///
	/// O RETORNO CONTINUA VALENDO e continua sendo outra coisa: ele responde "esta raiva e nova?" pra
	/// quem chamou, e o `Convivio` o usa. Nao e o gatilho da cena -- se fosse, a condicao 2 estaria
	/// escrita com a pergunta errada (ver o bloco do `wasRaging`).
	/// ============================================================================================
	/// </summary>
	private void TalvezACenaDaFuria(ServerPlayer enlutado, NivelDeRaiva grau, bool estavaEmFuria,
									long agora)
	{
		if (grau != NivelDeRaiva.Extrema || estavaEmFuria || enlutado.Peer == null) return;

		// A RECARGA (`rageCinematicCD`). O prazo mora no Core porque e prazo de CENA -- ver
		// `Cinematicas.SegundosEntreFurias`, que explica por que ele nao e o `SegundosDeRaiva`.
		if (agora < enlutado.FuriaCenaAte) return;

		if (AFuriaVaiVirarForma(enlutado)) return;

		enlutado.FuriaCenaAte = agora + (long)(Cinematicas.SegundosEntreFurias * 1000);

		// PRA ZONA INTEIRA, como o anuncio de forma -- as ondas de choque, o tremor e a musica sao do
		// MUNDO e nao do enlutado (no DM: `to_chat(view(src))`, `emit_RageMusic` pra cada `mob` do
		// `view`, e os `createShockwavemisc` sao objetos no chao). Ver `Protocol.S2C.Furia`.
		var w = Protocol.Begin(Protocol.S2C.Furia);
		w.Put(enlutado.Id);
		foreach (ServerPlayer o in ZoneList(enlutado.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		EscutaDeFurias?.Add(enlutado.Id);
		GD.Print($"[server] {enlutado.Name}: CINEMATICA DE FURIA");
	}

	/// <summary>
	/// ESTA RAIVA VAI VIRAR TRANSFORMACAO? -- o `anger_will_transform()` do DM (`Murder.dm:124-131`).
	///
	/// Quando SIM, a cena da furia nao toca: *"if this rage can power them UP a form (SSJ/SSJ2), the
	/// transformation owns the moment"*. E o mesmo instante disputado por duas cinematicas, e o
	/// original decide pela mais rara.
	///
	/// ============================ E AQUI ELA E DERIVADA, ENQUANTO LA E UMA LISTA ============================
	/// O DM enumera: Heran ou Saiyajin, `ssj==0 &amp;&amp; BP&gt;=ssjat &amp;&amp; hasssj`, `ssj==1 &amp;&amp; BP&gt;=ssj2at/6
	/// &amp;&amp; hasssj2`. Sao dois degraus escritos a mao, e um terceiro (o Beast, que tambem nasce de
	/// raiva) ja ficava de fora daquela lista.
	///
	/// Aqui a pergunta ja tem dono: <see cref="EstadoDeForma.Proxima"/> devolve o degrau que este
	/// corpo pode assumir AGORA -- com a raiva ja escrita, ou seja com o passo 9 do `Avaliar` (o
	/// `RecusaForma.SemFuria`) ja satisfeito --, e `Catalogo.NasceDaRaiva` responde se aquele degrau e
	/// dos que a raiva abre. Duas funcoes que ja existem, nenhum id digitado, e um degrau novo de
	/// Legendary (ou o Beast) entra sozinho.
	///
	/// ============================ NAO CONFUNDIR COM "VAI TRANSFORMAR AGORA" ============================
	/// Nem no DM nem aqui a raiva transforma ninguem sozinha: quem aperta a tecla e o jogador. Isto e
	/// uma PREVISAO -- "o proximo gesto dele vai ser uma transformacao, e ela merece o momento" --, e e
	/// por isso que ela pode errar sem custo: no maximo o jogador fica sem uma cena de 5 s.
	/// ====================================================================================================
	/// </summary>
	private static bool AFuriaVaiVirarForma(ServerPlayer pl) =>
		Catalogo.NasceDaRaiva(pl.Forma.Proxima(pl.Ficha.BP, Perfil(pl)));

	/// <summary>
	/// "Quero subir" (ou descer). O cliente NAO escolhe a forma -- ele pede a direcao e o servidor
	/// decide qual degrau cabe. E o comportamento da tecla "C" do original, que sobe a escada
	/// sozinha, e evita o cliente pedir SSJ3 direto.
	/// </summary>
	private void Transformar(ServerPlayer pl, bool subir)
	{
		if (!TemEscada(pl))
		{
			Avisar(pl, "sua raca nao tem essa escada de transformacao.");
			return;
		}

		EstadoDeForma est = pl.Forma;
		PerfilDeFormas perfil = Perfil(pl);

		if (!subir)
		{
			string antes = est.Atual;
			string recuo = ParaOndeSeRecua(est, perfil);
			if (recuo == antes) return;

			bool estreou = est.Entrar(recuo);
			AplicarForma(pl);
			Avisar(pl, recuo == Catalogo.IdBase
				? "voce volta ao normal."
				: $"voce recua para {Catalogo.NomeDe(Catalogo.Def(recuo), Dominou(pl, recuo))}.");
			AnunciarForma(pl, antes, recuo, estreou);
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "nao da, caido."); return; }

		// ============================ O CHEFE TEM A FORMA E NAO A USA ============================
		// Vale so pra corpo com ficha ROTEIRIZADA (`ServerPlayer.Papel`): jogador nao tem papel e
		// `AscendePorDecisao` devolve verdadeiro pra ele. Ver `GameServer.Npc.cs` -- e a resposta ao
		// *"o freeza do planeta vegeta nao transforma enquanto o freeza de namek transforma"*: ele
		// TEM a forma (o `Despertou()` dele e verdadeiro, o multiplicador existe), so nao sobe
		// sozinho. Quem o move e o `TickDoRoteiro`, pelo dano no membro mais ferido.
		//
		// A GUARDA MORA NO FUNIL, e nao no futuro cerebro. Aqui passam a tecla C, o `DirectSSJ` do
		// admin e -- quando existir -- a decisao da IA; escrever a regra so no lado de quem decide
		// seria mais uma API orfa, defeito que este projeto ja pagou inteiro (o sigilo de BP).
		// DESCER continua livre: quem barra e o `subir`, e o ramo de descida ja voltou la em cima.
		if (!AscendePorDecisao(pl)) return;

		FormaDef? alvo = est.Proxima(pl.Ficha.BP, perfil);
		if (alvo == null)
		{
			Avisar(pl, PorQueNao(est, pl, perfil));
			return;
		}

		EntrarNaForma(pl, alvo);
	}

	/// <summary>
	/// LIGA E DESLIGA OS GRADES NO CAMINHO DA TECLA C, e diz em que pe ficou. E o verb `graus`, da
	/// aba Other -- pedido do dono: *"um verb no OTHER q ao clicar fala se desativei ou nao os
	/// grades; com eles LIGADOS, no ssj1 (masterizado ou nao) apertar C duas vezes passa pelos grades
	/// antes do ssj2; DESLIGADOS, pula direto pro ssj2"*.
	///
	/// ============================ ELE NAO TRANSFORMA NINGUEM, NEM PRA CIMA NEM PRA BAIXO ============================
	/// O caso que obriga a decidir: **e se eu ja estiver num grade e desligar?** Fica onde esta, e o
	/// proximo C e que o tira de la (com os grades fora do caminho, o seletor pula pro SSJ2). As duas
	/// alternativas sao piores, e cada uma quebra uma regra que ja esta escrita neste arquivo:
	///
	///   * **cair pro SSJ1** seria uma transformacao que ninguem pediu. Quem transforma e o jogador
	///     apertando a tecla (`AFuriaVaiVirarForma` diz isso em voz alta: nem a raiva transforma
	///     sozinha), e alem disso o recuo tem regra propria -- `ParaOndeSeRecua` --, entao um
	///     `Entrar` escrito aqui seria a segunda copia dela;
	///   * **subir pro SSJ2** seria pior ainda: a subida cobra gate (porta de BP, maestria, Ki), e um
	///     botao de preferencia que atropela isso e exatamente o "atalho que pula o portao" que o
	///     `TransformarPara` recusou ser. Sem contar que o SSJ2 pode nem estar aberto.
	///
	/// Ficar parado tambem e o unico dos tres que nao mente: a preferencia diz por onde o C ANDA, e
	/// nao onde o corpo esta. Por isso a frase avisa, quando o caso acontece -- o silencio aqui faria
	/// o jogador achar que o botao nao funcionou.
	/// ============================================================================================================
	///
	/// **A ESCRITA VAI PRO DISCO NA HORA** (`Persistir`), como as tecnicas customizadas: e uma escolha
	/// do jogador, e escolha que morre num desligamento do servidor vira "por que isso voltou sozinho?".
	/// </summary>
	private void VerboGrades(ServerPlayer pl)
	{
		// `!= false` E NAO `== true`: `null` (ninguem opinou) conta como LIGADO, que e o lado que o
		// jogo ja tinha -- ver `EstadoDeForma.GradesLigados`. O login de jogador ja resolve o nulo,
		// mas quem le esta linha nao deveria precisar saber disso pra ter certeza do que ela faz.
		bool ligados = pl.Forma.GradesLigados != false;
		pl.Forma.GradesLigados = !ligados;
		Persistir(pl);

		if (!ligados)
		{
			Avisar(pl, "os graus do Super Saiyajin voltam pro seu caminho: no Super Saiyajin, "
					 + "subir passa pelo Grade 2 e pelo Grade 3 antes do Super Saiyajin 2.");
			return;
		}

		Avisar(pl, "os graus do Super Saiyajin saem do seu caminho: no Super Saiyajin, subir vai "
				 + "direto pro Super Saiyajin 2.");

		// E SE ELA JA ESTIVER NUM GRADE -- ver o cabecalho. A pergunta e `ForaDoTronco` e nao uma
		// lista de ids pelo mesmo motivo do `NoCaminhoDoC`: quem acrescentar um ramo lateral novo nao
		// precisa vir reescrever esta frase.
		if (pl.Forma.Def is { ForaDoTronco: true } atual)
			Avisar(pl, $"voce continua em {Catalogo.NomeDe(atual, Dominou(pl, atual.Id))} -- isto e "
					 + "uma preferencia, nao uma transformacao. O proximo C te tira dai.");
	}

	/// <summary>
	/// "QUERO ESTA FORMA" -- o gesto que a tecla de forma do jogador estreou.
	///
	/// ============================ POR QUE ELE PRECISOU EXISTIR ============================
	/// Nao havia como PEDIR uma forma. O toque duplo no C manda uma DIRECAO (`Transformar(subir)`) e
	/// o `Proxima` escolhe sozinho o degrau MAIS FORTE que couber -- o que e certo pra escada, e
	/// deixa um buraco: quem tem SSJ2 e Blue abertos nunca consegue pedir o Blue, porque o seletor
	/// so oferece o mais forte. O unico caso que hoje escapa disso e o Blue Evolution, e escapa por
	/// acidente feliz (`PedeFormaAtual` aceita a camada de baixo, `EstadoDeForma.cs:315`).
	///
	/// O outro caminho pra uma forma nomeada era o `admin_forma`, e ele **nao serve**: o proprio
	/// arquivo dele diz que ignora todos os requisitos, porque "as formas que interessa testar SAO as
	/// trancadas" (`GameServer.Admin.cs:906`). E ferramenta de teste. Uma tecla de jogador que
	/// caisse nela seria o pior tipo de atalho: o que pula o portao.
	/// ======================================================================================
	///
	/// ============================ E ELE E ESCOLHA, NAO SALTO ============================
	/// O `Avaliar` cobra o degrau anterior (passo 5), e essa cobranca fica de pe aqui. "Tecla 3 =
	/// SSJ3" NAO leva da base ao SSJ3: recusa por `ForaDeOrdem`, com a frase que diz isso em voz
	/// alta (ver `FraseDaRecusa`). O que a tecla resolve e "quero Blue e nao SSJ2" -- que ate hoje
	/// nao tinha como ser pedido.
	///
	/// TUDO O QUE O `Transformar` CONFERE ANTES, ELE CONFERE TAMBEM: a escada da raca, o caido, e o
	/// `AscendePorDecisao` (o chefe roteirizado que TEM a forma e nao a usa). Sao as guardas de
	/// topo, e sao do GESTO e nao do degrau -- por isso nao estao dentro do `Avaliar` e precisam
	/// estar aqui. E quem entra na forma continua sendo o `EntrarNaForma`, o mesmo do C e o mesmo do
	/// despertar assistido: escrever `pl.Forma.Atual` na mao daria uma transformacao sem buff, sem
	/// teto de Ki novo, sem cena e sem aura -- ver o cabecalho dele.
	/// ================================================================================
	/// </summary>
	private void TransformarPara(ServerPlayer pl, string id)
	{
		if (!TemEscada(pl)) { Avisar(pl, "sua raca nao tem essa escada de transformacao."); return; }

		// VOLTAR AO NORMAL E O RAMO DE DESCIDA DE SEMPRE, e nao um `Entrar(base)` escrito aqui: o
		// recuo tem regra propria (o Frost Demon recolhe a casca um degrau por vez, ver
		// `ParaOndeSeRecua`), e uma segunda escrita dela daria ao jogador com tecla um recuo
		// diferente do que a tecla X faz.
		if (id == Catalogo.IdBase) { Transformar(pl, subir: false); return; }

		if (Catalogo.Def(id) is not { } d) { Avisar(pl, "essa forma nao existe."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "nao da, caido."); return; }
		if (!AscendePorDecisao(pl)) return;

		EstadoDeForma est = pl.Forma;
		PerfilDeFormas perfil = Perfil(pl);
		double kiFracao = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : 1;

		RecusaForma r = est.Avaliar(id, pl.Ficha.BP, kiFracao, caido: false, perfil);
		if (r != RecusaForma.Pode) { Avisar(pl, FraseDaRecusa(est, d, r)); return; }

		EntrarNaForma(pl, d);
	}

	/// <summary>
	/// ============================ PRA ONDE ESTE CORPO RECUA -- E POR QUE NAO E SEMPRE A BASE ============================
	/// Duas respostas, e a segunda so existe por causa do Frost Demon:
	///
	///   * **UM DEGRAU**, quando o degrau imediatamente abaixo nao e mais forte que a base. E o
	///     `revertIcer()` do original (`IcerTransform.dm:116-127`, `fd_form--`): o Mutante escapa do
	///     descontrole recolhendo a casca aos poucos -- da forma base pra 4a, da 4a pra 3a --, e cada
	///     parada dessas e uma forma em que ele consegue viver enquanto a maestria nao alcanca a de
	///     cima. Saltar direto pro fundo do poco tiraria dele exatamente as tres formas que a
	///     maestria destrava (`FormasDeFrost.DegrauEstavel`);
	///   * **O PISO** (<see cref="Catalogo.PisoDaEscada"/>, que pra quase todo mundo e a propria
	///     base) em todo o resto.
	///
	/// ============================ A PERGUNTA E `PodeSerRepouso`, E ELA JA ERA UMA -- SO NAO ERA ESTA ============================
	/// Ela le como uma frase: **"o degrau de baixo e casca (ou e a propria base)? entao recuar e
	/// afrouxar um ponto. Senao, recuar e sair da transformacao."**
	///
	/// ESTA CONDICAO ERA `abaixo.Mult[0] &lt;= 1` ESCRITO A MAO AQUI, e vinha acompanhada da promessa
	/// *"conferido entrada por entrada no catalogo de hoje: nenhum deles passa"*. **A promessa
	/// envelheceu e a bancada pegou** (`GameServer.RaciaisTeste`, secao 6): o `heran1` tem
	/// `Mult[0] == 1` -- porque na linha Heran o `Mult` **nao e o multiplicador**, e a CURVA dele
	/// (`FormaDef.BaseDaClasse` e quem diz por quanto ela multiplica: 1,30x no Omega, 3x no
	/// Low-Class). Ler o array cru chamava de "casca" a forma Max Power, e recuar do True Max Power
	/// parava nela em vez de voltar ao normal -- o que o DM nao faz: o Heran usa a MESMA var `ssj`
	/// do Saiyajin (`HeranBuff.dm:97` e `:193` fazem `ssj=1` / `ssj=2`), e sair e `ssj = 0`.
	///
	/// O CONSERTO E TROCAR A COPIA PELA PERGUNTA QUE JA EXISTIA. `Catalogo.PodeSerRepouso` responde
	/// exatamente "este degrau e casca?" -- e ela nao le so o `Mult`: exige tambem que o degrau nao
	/// cobre NADA (porta de BP, maestria, ki divino, flag de skill, raiva). O `heran1` cobra
	/// `ssjat`, entao cai fora por onde deveria ter caido desde o comeco, e as quatro supressoes do
	/// Frost Demon continuam passando por serem o que sao. Duas escritas da mesma regra viraram uma.
	///
	/// Ou seja: esta funcao muda o comportamento de EXATAMENTE uma raca, sem citar raca nenhuma.
	/// ======================================================================================================================
	/// </summary>
	private static string ParaOndeSeRecua(EstadoDeForma est, PerfilDeFormas perfil)
	{
		string piso = Catalogo.IdDoPiso(perfil);
		if (est.Def is not { } agora) return piso;

		if (Catalogo.DegrauAbaixo(agora) is { } abaixo
			&& Catalogo.PodeSerRepouso(abaixo)
			&& est.Avaliar(abaixo.Id, bpBase: double.MaxValue, kiFracao: 1,
						   caido: false, perfil) == RecusaForma.Pode)
			return abaixo.Id;

		return piso;
	}

	/// <summary>
	/// ASSUMIR A FORMA -- tudo o que acontece DEPOIS de o degrau ter sido escolhido e aprovado.
	///
	/// ============================ POR QUE ISTO SAIU DO `Transformar` ============================
	/// Porque agora ha DOIS gestos que chegam ate aqui: a tecla C (`Transformar`) e o **despertar
	/// assistido por um mestre** (`GameServer.Mestre.DespertarAssistido`). O DM tem o mesmo par --
	/// la o `mst_form_apply` (`MasterStudent.dm:361`) chama a proc canonica (`SSj()`, `SSj2()`,
	/// `Restrained_SSj()`) em vez de escrever `ssj = 1` na mao, e o comentario dele diz o porque:
	/// quem seta a forma por fora perde tudo o que a proc faz.
	///
	/// Aqui o "tudo" e concreto e caro de reescrever: o Ki cheio na estreia, o multiplicador que sai
	/// pela MESMA conta do buff, o log, o nome com maestria, o anuncio pra zona (que carrega
	/// cinematica, clima, cabelo e aura). Um segundo caminho que fizesse metade disso daria um
	/// despertar assistido sem cena e sem aura -- e ninguem ligaria o defeito ao mestre.
	/// ======================================================================================
	/// </summary>
	private void EntrarNaForma(ServerPlayer pl, FormaDef alvo)
	{
		EstadoDeForma est = pl.Forma;
		string anterior = est.Atual;
		bool primeira = est.Entrar(alvo.Id);
		AplicarForma(pl);

		// KI CHEIO AO DESPERTAR. E o que o original faz nas primeiras vezes e o que transforma a
		// cena: a forma nova nao pode nascer sem folego pra ser usada.
		//
		// ============================ ENCHER NAO PODE ESVAZIAR ============================
		// Isto era `pl.Ficha.Ki = pl.Ficha.MaxKi` seco, e ERA o defeito que o dono relatou: *"ao
		// travar o ki ao se transformar, ele volta pro 100%, oq n deveria acontecer"*. Medido no
		// funil, com o corpo a 190%: 266/140 antes, 532/280 depois do `AplicarForma` (a razao
		// atravessa intacta, como ele promete) e 280/280 DEPOIS DESTA LINHA. Nao "cai pra 40%",
		// nao "some" -- volta pro 100% cravado, exatamente a frase do dono. E como toda conta nova
		// estreia todo degrau, ele via em toda subida.
		//
		// A DOENCA E A MESMA DO `Nutricao.cs:134`: um PRESENTE escrito como atribuicao absoluta
		// vira um CORTE pra quem ja estava acima do teto. O original nunca corta -- `Power
		// Control.dm:151` (`Energy_Draw`) deixa o Ki passar do `MaxKi` de proposito, e o
		// `CheckPowerMod` (`Power Control.dm:113-134`) so faz o excesso VAZAR devagar, com dano
		// acima do `kicapacity`. Sobrecarga se perde pagando, nunca por decreto.
		//
		// `Math.Max` E NAO UM PORTAO `if (Ki < MaxKi)`: os dois dao o mesmo numero, mas o `Max`
		// diz a regra numa expressao so -- "o despertar nao deixa ninguem abaixo do cheio" -- em
		// vez de esconde-la num `if` que a proxima pessoa le como "as vezes nao enche".
		//
		// E O TETO ABSOLUTO CONTINUA DE PE, porque nao e este: quem limita a sobrecarga e o dono
		// dela, o `CargaDeKi.TetoDeCarga` (`MaxKi * powerupcap`) e o `PrecoDoExcesso` (que cobra
		// folego e machuca acima do `kicapacity`). 100% nunca foi teto de Ki -- e so o tamanho
		// nominal do tanque.
		// =================================================================================
		if (primeira) pl.Ficha.Ki = Math.Max(pl.Ficha.MaxKi, pl.Ficha.Ki);

		// PELO `MultiplicadorDaForma` E NAO PELO `est.Multiplicador`: o `ssjBuff` logo acima ja saiu
		// com o fator do cargo, e duas contas diferentes pro mesmo numero e como o chat passaria a
		// anunciar 60x num corpo que esta em 75x. Ver `MultiplicadorDaForma`.
		double mostrado = MultiplicadorDaForma(pl);

		// `BP x mostrado` NAO DA O `expressedBP` DA LINHA ABAIXO, e nao ha nada a consertar: um e o BP
		// base e o outro e o poder, com idade, estado do corpo, gravidade e raiva no meio (taxonomia no
		// topo do `Fighter.Power.cs`). E subir a MESMA forma duas vezes tambem nao repete o numero: o
		// `kiratio` entra na conta, a forma nao masterizada drena Ki e a razao atravessa a volta pra
		// base -- o segundo Super Saiyajin do dia sai mais fraco que o primeiro, de proposito.
		GD.Print($"[server] {pl.Name}: {anterior} -> {alvo.Id} "
				 + $"(x{mostrado:0.##}, BP {pl.Ficha.BP:N0} -> {pl.Ficha.expressedBP:N0})"
				 + (primeira ? "  <- PRIMEIRA VEZ" : ""));

		// O NOME PELO FUNIL. Este e o texto que o jogador mais le no jogo inteiro -- toda subida de
		// forma passa por aqui --, e e a segunda metade do pedido do dono: quem dominou o Super
		// Saiyajin tem que ouvir "Super Saiyajin Grade 4" no mesmo instante em que o cabelo vira o
		// `SSjFP`. Os dois saem do mesmo fato (`Dominou`), que e o mesmo que vai no `S2C.Forma`.
		string nome = Catalogo.NomeDe(alvo, Dominou(pl, alvo.Id));
		Avisar(pl, primeira
			? $"VOCE DESPERTA: {nome}!"
			: $"{nome} (x{mostrado:0.##}).");

		AnunciarForma(pl, anterior, alvo.Id, primeira);
	}

	/// <summary>A mensagem que explica o que falta. Ver o comentario de `Avaliar`.</summary>
	// DEIXOU DE SER `static` porque a recusa `SemHabilidade` nomeia a SKILL, e o catalogo de skills
	// mora no `_skills` da instancia. Passa-lo por parametro so empurraria o mesmo acoplamento pro
	// unico chamador.
	private string PorQueNao(EstadoDeForma est, ServerPlayer pl, PerfilDeFormas perfil)
	{
		// procura o degrau mais barato a partir daqui e conta o que falta NELE
		FormaDef? candidato = null;
		RecusaForma pior = RecusaForma.JaEsta;
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == est.Atual || d.Id == Catalogo.IdBase) continue;

			// O DEGRAU QUE O JOGADOR TIROU DO CAMINHO NAO EXPLICA NADA. Com os grades desligados, o
			// Grade 2 vinha antes do SSJ2 nesta varredura (ordem 15 contra 20) e respondia por ele:
			// quem pedisse o SSJ2 sem maestria ouviria "voce precisa de 50% de maestria no Super
			// Saiyajin", que e a porta do GRADE. Mesmo funil do `Proxima` -- ver `NoCaminhoDoC`.
			if (!est.NoCaminhoDoC(d)) continue;

			RecusaForma r = est.Avaliar(d.Id, pl.Ficha.BP,
										pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : 1,
										pl.Ficha.KO || pl.Ficha.dead, perfil);
			// LinhaFechada NAO E RECUSA A MOSTRAR: aquela escada nao e desta pessoa, e dizer "voce
			// precisa ser Legendary Primal" pra um Saiyajin comum e contar de um jogo que nao e o
			// dele. ForaDeOrdem tambem sai -- e so "voce ainda nao chegou ali".
			//
			// ============================ E `Pode` NAO E UMA RECUSA ============================
			// Achado pela bancada (`--formasteste`, "a recusa manda virar OOZARU DOURADO"): esta funcao so
			// e chamada quando `Proxima` devolveu NULO, ou seja quando nada que este corpo pode assumir e
			// mais forte do que a forma em que ele ja esta. Mas `Avaliar` nao sabe disso -- ela responde
			// `Pode` pra QUALQUER degrau alcancavel, inclusive os que ficaram pra tras. Varrendo o catalogo
			// de cima, o primeiro achado a partir do SSJ3 era o proprio SSJ1: recusa nenhuma, `break`, e a
			// mensagem caia no `_ => "ainda nao."`.
			//
			// O estrago era exatamente o que o comentario do `SemFormaAnterior` la embaixo diz que a
			// mensagem existe pra evitar: quem tinha BP de sobra pro SSJ4 apertava C, ouvia "ainda nao" e
			// nao tinha como saber que o que faltava era ter virado Oozaru Dourado. A frase estava escrita,
			// testada no Core e INALCANCAVEL em jogo -- a falha assinatura deste port, mais uma vez.
			// ==============================================================================
			//
			// `NaoConcedida` SAI PELO MESMO MOTIVO DA `LinhaFechada`, e o caso e o Mistico: ele e um
			// dom que um Kaioshin da, e quem nao recebeu nao tem como saber que existe. Deixa-la
			// passar poria TODO jogador que aperta C sem ter pra onde subir a ouvir o nome de uma
			// forma que ele nao pode buscar -- e pior, a busca-la.
			if (r is RecusaForma.Pode or RecusaForma.ForaDeOrdem or RecusaForma.JaEsta
					or RecusaForma.LinhaFechada or RecusaForma.SemLinhagem or RecusaForma.SemClasse
					or RecusaForma.NaoConcedida) continue;
			candidato = d; pior = r; break;
		}
		if (candidato == null) return "nao ha mais degrau acima deste.";

		return FraseDaRecusa(est, candidato, pior);
	}

	/// <summary>
	/// A FRASE DE UMA RECUSA -- o texto que o jogador le quando uma forma nao vem.
	///
	/// ============================ ELA SAIU DO `PorQueNao` PORQUE AGORA HA DOIS PERGUNTADORES ============================
	/// O `PorQueNao` responde a pergunta ABERTA da tecla C ("nao subi, por que?"): ele varre o catalogo
	/// atras do degrau mais barato e conta o que falta NELE. O `TransformarPara` responde a pergunta
	/// FECHADA da tecla de forma ("por que nao virei Blue?"), e ali o degrau ja e conhecido -- nao ha
	/// o que varrer.
	///
	/// O que os dois compartilham e exatamente isto: as frases. Duplica-las teria dado ao jogador dois
	/// textos pra a mesma falta, divergindo no primeiro ajuste -- e o pior deles seria o `SemPoder`,
	/// que e escrito SEM NUMERO de proposito (o limiar e sorteado por personagem, e dize-lo entrega de
	/// graca o que o jogo quer que se descubra tentando). Uma copia esquecida teria vazado o numero.
	/// ==============================================================================================================
	/// </summary>
	private string FraseDaRecusa(EstadoDeForma est, FormaDef candidato, RecusaForma pior)
	{
		string nome = Catalogo.NomeDe(candidato, est.Maestria);
		string anterior = Catalogo.Anterior(candidato) is { } ant
			? Catalogo.NomeDe(ant, est.Maestria) : "a forma anterior";

		// ============================ A MAESTRIA NEM SEMPRE E DO DEGRAU ANTERIOR ============================
		// `PedeMaestriaDe` existe justamente pra apontar pra OUTRA forma, e o SSJ4 e o caso: ele
		// cobra 100% no Oozaru Dourado, que nao e o degrau anterior dele (o anterior e o SSJ3).
		// A mensagem lia o anterior fixo e teria mandado o jogador dominar o SSJ3 -- horas na forma
		// errada, com o jogo dizendo que era essa. Sem esta linha o gate novo do SSJ4 nasceria
		// mentindo, que e pior que nao existir.
		// ================================================================================================
		FormaDef? cobra = Catalogo.Def(candidato.PedeMaestriaDe) ?? Catalogo.Anterior(candidato);
		string deQuem = cobra != null ? Catalogo.NomeDe(cobra, est.Maestria) : anterior;
		double tenho = est.Maestria.De(cobra?.Id);

		return pior switch
		{
			// SEM NUMERO. Esta frase desfazia sozinha a reforma da aba Forms: o limiar saia da tela e
			// voltava pela mensagem de erro. E o limiar e SORTEADO por personagem -- dize-lo entrega
			// de graca o que o jogo quer que se descubra tentando.
			RecusaForma.SemPoder => $"{nome} ainda esta alem do seu alcance.",
			RecusaForma.SemMaestria => $"{nome} pede {candidato.PedeMaestria:0}% de maestria em "
									 + $"{deQuem} (voce tem {tenho:0.#}%).",
			// ESTA E A MENSAGEM QUE MAIS ECONOMIZA TEMPO DO JOGADOR: sem ela, quem tem BP de sobra
			// pro SSJ4 treina por horas sem saber que o que falta e ter virado Oozaru Dourado.
			RecusaForma.SemFormaAnterior =>
				$"{nome} exige que voce ja tenha assumido "
				+ $"{(Catalogo.Def(candidato.PedeFormaDespertada) is { } exigida
					   ? Catalogo.NomeDe(exigida, est.Maestria) : "outra forma")} ao menos uma vez.",
			RecusaForma.SemGodKi => $"{nome} pede {candidato.PedeGodKi:0}% de maestria no ki divino.",
			// ESTA FRASE TEM QUE DESENSINAR, e por isso ela nao diz "falta X": quem a ouve ja tem
			// tudo o que se treina (poder, degrau anterior, ki divino) e a unica coisa que falta
			// nao se busca. Dizer "voce precisa de furia" mandaria a pessoa procurar briga; dizer
			// que a forma nao se alcanca querendo e o que o despertar e no original.
			// Ela NAO e filtrada como a `NaoConcedida`: aqui o jogador ja sabe que a forma existe.
			//
			// SAO DUAS FRASES PORQUE SAO DOIS PRECOS (ver `Catalogo.RaivaExigida`), e uma frase so
			// mentiria pra metade de quem a ouve: mandar o Legendary esperar a morte de um amigo
			// esconde dele que basta uma luta em que alguem seu caia. E a diferenca e invisivel na
			// tela -- nao ha numero, nao ha barra --, entao ou ela esta na frase ou nao existe.
			RecusaForma.SemFuria => Catalogo.RaivaExigida(candidato) == NivelDeRaiva.Extrema
				? $"{nome} nao se alcanca querendo -- ele sai de uma dor que voce ainda "
				+ "nao teve."
				: $"{nome} nao vem de treino -- ele vem de ver alguem seu cair na sua "
				+ "frente e nao chegar a tempo.",
			RecusaForma.SemEnergia => $"{nome} pede mais energia de Ultra Ego.",

			// ============================ A UNICA RECUSA QUE SE RESOLVE NA LOJA ============================
			// Todas as outras mandam TREINAR, ESPERAR ou NAO PROCURAR. Esta manda gastar um ponto de
			// marco, e por isso ela nomeia a skill em vez de descrever a falta: "voce ainda nao
			// aprendeu isso" deixaria o Namekuseijin procurando na aba errada. `NomesLegiveis` ja
			// traduz o path do DM (`/datum/skill/namek/SuperNamek`) pro nome que a loja mostra, e usar
			// outra fonte aqui daria dois nomes pra a mesma compra.
			//
			// ELA NAO E FILTRADA como a `NaoConcedida` e a `LinhaFechada`: ao contrario do Mistico
			// (dom de outro jogador) e das linhas de outra raca, esta forma E deste personagem e esta
			// a um clique -- esconde-la seria esconder o proprio caminho.
			// ==========================================================================================
			RecusaForma.SemHabilidade =>
				$"{nome} pede uma habilidade que voce ainda nao comprou"
				+ (SkillDaFlag(candidato) is { } skill ? $" -- {skill}." : "."),
			RecusaForma.SemKi => "Ki baixo demais pra sustentar a forma.",
			RecusaForma.Caido => "nao da, caido.",

			// ============================ AS CINCO DE BAIXO SO SAEM PELA PERGUNTA FECHADA ============================
			// O `PorQueNao` FILTRA todas elas antes de chegar aqui (ver o `continue` la em cima), e por
			// bons motivos: `LinhaFechada` e `NaoConcedida` contariam de um jogo que nao e o desta
			// pessoa, e `ForaDeOrdem` num sorteio de degrau e so "voce ainda nao chegou ali".
			//
			// Na pergunta FECHADA elas invertem de valor, porque o jogador NOMEOU a forma: quem ligou
			// uma tecla ao Super Saiyajin 3 e apertou na base precisa ouvir que falta o degrau do meio,
			// e nao "ainda nao". E essa e a frase mais importante desta tela inteira -- ela e o que
			// impede a tecla de forma de ser lida como um SALTO. Ver `TransformarPara`.
			// ==================================================================================================
			RecusaForma.ForaDeOrdem => $"{nome} vem depois de {anterior} -- a tecla escolhe a forma, "
									 + "nao pula degrau.",
			RecusaForma.JaEsta => $"voce ja esta em {nome}.",
			RecusaForma.LinhaFechada => $"{nome} nao e uma forma sua.",
			RecusaForma.NaoConcedida => $"{nome} nao se treina: alguem precisa te conceder.",
			RecusaForma.SemLinhagem or RecusaForma.SemClasse =>
				$"{nome} nao e da sua linhagem.",

			_ => "ainda nao.",
		};
	}

	/// <summary>
	/// ============================ QUE SKILL ESCREVE A FLAG QUE ESTA FORMA PEDE ============================
	/// Nulo quando a forma nao pede flag nenhuma, ou quando nenhuma skill do catalogo a escreve.
	///
	/// **E UMA BUSCA E NAO UMA TABELA**, e essa e a unica coisa interessante desta funcao. A tentacao
	/// era um `switch (flag) { "snamek" => "Super Namekuseijin", ... }` -- tres linhas, imediato, e
	/// uma SEGUNDA verdade sobre quem destrava o que. O `skills.json` sai do DM pelo pipeline: no dia
	/// em que o nome da skill mudar la, ou em que outra skill passar a escrever a mesma flag, a
	/// tabela mentiria e ninguem notaria, porque a mensagem so aparece pra quem ainda NAO tem a
	/// forma -- ou seja, pra quem nao sabe qual e o nome certo.
	///
	/// Varrer o catalogo inteiro e barato aqui: isto roda quando um jogador aperta C e nao ha degrau
	/// acima, nunca no tique.
	/// =================================================================================================
	/// </summary>
	private string? SkillDaFlag(FormaDef d)
	{
		if (d.PedeFlag is not { } flag || _skills == null) return null;

		// A MAIOR DAS CANDIDATAS, e nao a primeira: a flag do Alien vale 2 e uma skill que escrevesse
		// `hasayyform = 1` nao seria a que abre a 2a forma. `Totalizar` ja resolve empate pelo maior
		// valor (`EfeitosDeSkill`, canal de ATRIBUICAO) -- a mesma regra, aqui.
		Jandirus.Core.Skills.Skill? melhor = null;
		foreach (Jandirus.Core.Skills.Skill s in _skills.Todas)
		{
			if (!s.Flags.TryGetValue(flag.Campo, out double v) || v < flag.Minimo) continue;
			if (melhor == null || v > melhor.Flags[flag.Campo]) melhor = s;
		}
		return melhor?.Nome;
	}

	/// <summary>
	/// Escreve o multiplicador na ficha. E o unico lugar que mexe no `ssjBuff`.
	///
	/// INTERNAL e nao private porque a proporcao de Ki (abaixo) so se prova rodando ESTE metodo: ela
	/// nasce e morre dentro dele, e um teste que refizesse a conta provaria a conta do teste. Ver a
	/// bancada `--diagforma` (`RoboDeForma.AProporcaoDeKi`).
	///
	/// DEIXOU DE SER `static` quando o fator do cargo entrou: o multiplicador passou a depender de
	/// QUEM PORTA O TITULO (`MultiplicadorDaForma` -> `CargoDe`), e os cargos moram no `_tronos` da
	/// instancia do servidor. Reproduzir a leitura num campo do `ServerPlayer` daria uma segunda
	/// verdade sobre o trono, que e exatamente o que o `CargoDe` existe pra impedir.
	/// </summary>
	internal void AplicarForma(ServerPlayer pl)
	{
		// ============================ A PROPORCAO DE KI ATRAVESSA A TROCA DE FORMA ============================
		// Regra do dono: *"se o ki do player no ssj1 for 200/200 ou seja 100% ao voltar pra base ele vai
		// ficar com 100/100 pq ainda e 100%"*.
		//
		// A razao e lida ANTES de qualquer coisa deste metodo -- antes do `ssjBuff` inclusive --, porque
		// o `MaxKi` e derivado dos dois (`Statify` o recalcula a partir do BP e do `trueKiMod`), e ler
		// depois seria medir a razao contra um teto que ja mudou.
		//
		// `MaxKi <= 0` E CASO REAL, nao paranoia -- mas NAO pelo motivo que estava escrito aqui. Este
		// comentario dizia "um personagem que ainda nao passou por `Statify` nenhum tem teto zero", e
		// a bancada `--diagforma` reprovou em cima disso: `Fighter.MaxKi` NASCE em 100
		// (`Fighter.cs:38`), entao um lutador cru ja tem tanque.
		//
		// O caminho de verdade e outro e o projeto ja pisou nele: o `MaxKi` e um PRODUTO
		// (`Fighter.Statify.cs:118`) e basta um dos fatores zerar. O `KiMod = 0` esta documentado
		// logo abaixo daquela formula ("um KiMod zerado fazia log(8, 0) derrubar o statify a cada
		// tick"). Dividir por esse teto plantaria `NaN` no Ki -- que dali em diante contamina toda
		// comparacao (`NaN > x` e sempre falso, entao a forma nunca cairia por Ki zerado). O -1 marca
		// "nao ha razao a repor" sem inventar 0% nem 100%.
		// ================================================================================================
		double razaoDeKi = pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : -1;

		// O FATOR DO CARGO ENTRA AQUI, e so aqui: ver `MultiplicadorDaForma`. No DM ele e escrito no
		// mesmo campo (`container.formsBuff = ue_form_mult(...)`, `UltraEgo.dm:552`) e re-afirmado no
		// `Loop()` do buff (`:558`) -- e este metodo E o loop deste port, chamado de todo ponto que
		// muda o estado. Um Deus da Destruicao que perde o trono transformado volta a 60x no tique
		// seguinte, como la.
		pl.Ficha.ssjBuff = MultiplicadorDaForma(pl);

		// O TETO DE KI DA FORMA -- e este era o elo que faltava. O catalogo tinha a escada inteira
		// e ninguem consumia: as 36 entradas valiam 1,0x de teto, entao virar Super Saiyajin nao
		// aumentava o tanque em nada. `Statify` ja multiplica o `MaxKi` por `trueKiMod`
		// (`Fighter.Statify.cs:118`); so faltava alguem escrever nele.
		//
		// Escrito AQUI porque este e "o unico lugar que mexe no ssjBuff" -- teto e multiplicador
		// sao a mesma decisao (que forma esta ativa) e separa-los criaria duas verdades.
		Jandirus.Core.Forms.FormaDef? def = Jandirus.Core.Forms.Catalogo.Def(pl.Forma.Atual);
		pl.Ficha.trueKiMod = Jandirus.Core.Forms.Catalogo.TetoDeKi(def, pl.Ficha);

		// ============================ E OS STATS DA FORMA, PELA MESMA REGRA ============================
		// Terceira coisa que este metodo afirma, ao lado do `ssjBuff` e do `trueKiMod`, e pelo mesmo
		// motivo: forma, teto de Ki e stats sao UMA decisao so (qual entrada do catalogo esta ativa),
		// e separa-los criaria duas verdades. O dono: *"o ssj grade 2 e grade 3 n estao dando o DEBUFF
		// DE VELOCIDADE ... e tb n estao tendo os buffs de OFFENSIVE"*. Nao estavam porque o catalogo
		// nao tinha onde guardar isso -- agora tem (`FormaDef.Mods`), e vale pra qualquer entrada.
		//
		// AFIRMA, NAO ACUMULA -- e e isto que o torna seguro num metodo que roda TODO TIQUE
		// (`TickDaForma` reavalia o degrau de maestria a cada passada). Nao ha "desfazer": voltar pra
		// base escreve 1 em tudo porque `Mods` da base e nulo. Ver a regra de ouro do
		// `GameServer.Buffs` -- aqui ela nem chega a ser necessaria, porque nao existe delta guardado
		// pra divergir do estado real.
		//
		// FORA DO CANAL DE BUFF DE PROPOSITO: `DerrubarBuffs` limpa tudo no nocaute, e um buff de
		// forma limpo por ali deixaria os stats no valor da base com a forma ainda de pe -- calado,
		// e so ate o proximo tique reescrever. Campo afirmado nao tem esse estado intermediario.
		// ==============================================================================================
		Jandirus.Core.Forms.ModsDeForma m = def?.Mods ?? Jandirus.Core.Forms.Catalogo.SemMods;
		pl.Ficha.formaPhysoff = m.Physoff;
		pl.Ficha.formaPhysdef = m.Physdef;
		pl.Ficha.formaKioff = m.Kioff;
		pl.Ficha.formaKidef = m.Kidef;
		pl.Ficha.formaTecnica = m.Tecnica;
		pl.Ficha.formaSpeed = m.Speed;
		pl.Ficha.formaCadencia = m.Cadencia;

		pl.Ficha.Statify();   // `MaxKi` e derivado: sem isto o teto novo so valeria no proximo tique

		// ============================ E A RAZAO VOLTA, CONTRA O TETO NOVO ============================
		// AQUI MORAVA UM CORTE, e ele foi DELETADO em vez de somado a isto -- acumular os dois daria
		// duas respostas pra a mesma pergunta. O corte aparava o Ki no teto da carga
		// (`MaxKi * powerupcap`) quando descer a escada derrubava o `MaxKi`; ele resolvia o sintoma
		// ("ao voltar pra base o ki fica brilhando de forma estranha") mas mentia sobre a causa: quem
		// estava com 200/200 em Super Saiyajin voltava pra base com 140/100, ou seja 140% -- ainda
		// sobrecarregado, so que menos. E quem SUBIA a escada mantinha o numero absoluto e via a barra
		// despencar de 100% pra 40% de graca.
		//
		// A regra do dono resolve os dois sentidos com uma conta so: a forma muda o TAMANHO do tanque,
		// nao o quanto dele esta cheio. 100% em SSJ1 e 100% na base; 30% subindo continua 30%.
		//
		// E DELETAR O CORTE E SEGURO PORQUE A RAZAO JA E O QUE O `powerupcap` LIMITA. O teto da carga e
		// `MaxKi * powerupcap`, ou seja ele e um teto de RAZAO disfarcado de valor absoluto -- e o
		// `powerupcap` sai das skills, nao da forma. Quem estava em 140% so pode ter chegado la porque
		// o `CargaDeKi.Passo` deixou; repor 140% do teto novo cai exatamente no mesmo lugar da regua.
		// Nao ha sobrecarga a ganhar na troca, que era tudo o que o corte protegia.
		//
		// ISTO TAMBEM RODA NO TICK (o `TickDaForma` chama este metodo todo tique pra reavaliar o
		// degrau de maestria), e ali e inofensivo de proposito: com o `MaxKi` inalterado a conta
		// devolve exatamente o Ki que entrou. O que ela faz no tique e acompanhar o teto quando a
		// maestria sobe um degrau no meio da luta -- que e a mesma regra, so que ao vivo.
		// ================================================================================================
		if (razaoDeKi >= 0) pl.Ficha.Ki = razaoDeKi * pl.Ficha.MaxKi;
		pl.SigAtributos = "";   // a aba Forms mostra maestria: forca o proximo pacote a sair

		// O PODER SAI JUNTO COM O MULTIPLICADOR -- ver `RepercutirPoder`. Depois da razao de Ki, e nao
		// antes: o `kiratio` e um dos fatores da conta (`Fighter.Power.cs:50`), e recalcular com o
		// tanque do teto ANTIGO daria o poder de um corpo que nao existe mais.
		RepercutirPoder(pl);
	}

	/// <summary>
	/// O `expressedBP` ACOMPANHA O `ssjBuff` NA HORA -- e nao no proximo tique de ficha.
	///
	/// ============================ POR QUE ISTO PRECISA EXISTIR ============================
	/// `AplicarForma` escreve o multiplicador e termina em `Statify()`, que cuida dos ATRIBUTOS e nao
	/// toca no poder. Quem transforma `ssjBuff` em `expressedBP` e o `PowerLevel()`, e ele so roda no
	/// `TickFichas` -- a cada 6 tiques, 5 Hz. Ou seja: por ate 200 ms depois de uma transformacao, o
	/// campo que o mundo inteiro le como "o poder desta pessoa" ainda era o da forma ANTERIOR.
	///
	/// Foi assim que o defeito apareceu (o `GD.Print` do `admin_forma` imprimindo o poder da forma
	/// velha), mas o print e o menor dos leitores. Nessa janela leem o mesmo campo:
	///   * o dano de soco e a guarda (`MeleeResolver` -> `BpModulus`);
	///   * o arremesso e o limiar de empurrao (`Empurrao.Limiar`, `ForcaDoVoo`);
	///   * a quebra de cenario, que e um LIMIAR (`Armadura`/`Estrago`: parede com `Resistance` acima
	///     do `expressedBP` simplesmente nao cai);
	///   * a ficha que vai pro cliente (`MandarFicha` -> HUD e aba Stats).
	/// Quem virasse SSJ e batesse no mesmo quadro batia com o punho da base. Consertar so o print
	/// deixaria os outros quatro de pe.
	///
	/// ============================ O QUE ENTRA E O QUE FICA DE FORA ============================
	/// `ClampAnger` + `PowerLevel` sao a ORDEM do `Fighter.Tick` (`Fighter.cs:392`), e e de proposito:
	/// o que este metodo promete e que o numero lido agora e IGUAL ao que o proximo tique calcularia.
	/// Uma ordem diferente daria um terceiro valor, e ai haveria duas verdades de novo.
	///
	/// `WeightTick` NAO entra, e nao por economia: ele tem marca d'agua (`weight_cap_hw = max(...)`,
	/// `Fighter.Training.cs:261`) alimentada pelo `expressedBP`. Chama-lo aqui deixaria a marca subir
	/// no pico da transformacao e ficar la -- um efeito PERMANENTE no sistema de peso a cada vez que
	/// alguem virasse Super Saiyajin. Acumulador so anda no tique dele.
	/// ======================================================================================
	/// </summary>
	private void RepercutirPoder(ServerPlayer pl)
	{
		// A RAIVA ANTES DO CORTE E DO PODER, que e a ordem do `Stats()` do DM: la o decaimento
		// (`Stats.dm:438-443`) roda no mesmo laco e ANTES de o `powerlevel()` ler o `angerBuff`.
		// Depois do `ClampAnger` o valor projetado seria usado sem teto por um quadro; depois do
		// `PowerLevel` ele so valeria no proximo. Ver `ProjetarRaiva`.
		ProjetarRaiva(pl);
		pl.Ficha.ClampAnger();
		pl.Ficha.PowerLevel(agoraMs: NowMs());
	}

	/// <summary>
	/// POR QUANTO TEMPO ESTE CORPO FICA PRESO PELA CENA QUE ACABOU DE COMECAR.
	///
	/// ============================ A MESMA CONTA DO CLIENTE, E SO ELA ============================
	/// `NoDegrau(def, degrau)` e literalmente a linha que o `World.AoMudarForma` roda pra escolher o
	/// roteiro, e `SegundosPreso` e o campo que a `Transformacao` usa pra soltar o corpo. Somar prazos
	/// aqui -- ou guardar a duracao TOTAL da cena (`Cinematica.Segundos`, que e maior) -- criaria uma
	/// segunda verdade sobre um numero que ja existe: o servidor descongelaria o Ki num instante e o
	/// cliente devolveria o controle noutro, e a diferenca so apareceria como "as vezes o Ki some".
	///
	/// ZERO EM TODO CAMINHO SEM CENA, e isso e regra e nao sobra: `DegrauDeCena.Nenhuma` (maestria
	/// >= 50%), a volta pra base, a saida do Oozaru Dourado pro SSJ4 (`semCena`) e a sincronia de quem
	/// entra na zona nao prendem corpo nenhum -- e o que nao prende nao congela.
	/// ========================================================================================
	/// </summary>
	private static void MarcarCena(ServerPlayer pl, FormaDef? d, DegrauDeCena degrau) =>
		pl.CenaSegundos = d != null && Cinematicas.NoDegrau(d, degrau) is { } cena
			? cena.SegundosPreso
			: 0;

	/// <summary>
	/// O CORPO DESTE JOGADOR ESTA PRESO POR UMA CINEMATICA AGORA?
	///
	/// E o portao dos TRES relogios que mexem no Ki sozinhos -- o dreno da forma (aqui), a
	/// regeneracao e a carga (<see cref="TickDaCarga"/>) e o custo do voo (<see cref="TickDoVoo"/>).
	/// Os tres ja rodavam juntos no tique cheio "porque mexem no MESMO Ki"; agora eles param juntos
	/// pelo mesmo motivo.
	/// </summary>
	private static bool EmCena(ServerPlayer pl) => pl.CenaSegundos > 0;

	/// <summary>
	/// QUANTO ESTE CORPO DOMINA ESTA FORMA, de 0 a 100 -- **e pras formas de disciplina a resposta e a
	/// proficiencia da DISCIPLINA**.
	///
	/// ============================ O LEITOR PERGUNTA, ELE NAO ESCOLHE ============================
	/// Desde que o livro de maestrias recusou as quatro formas divinas (ver `Maestrias.Por`), quem
	/// lesse `Maestria.De("ui_sign")` leria ZERO pra sempre -- e um leitor lendo zero pra sempre e a
	/// regra sumindo sem ninguem ver. Sao dois leitores que importam de verdade, e os dois passam por
	/// aqui:
	///
	///   * a CINEMATICA (<see cref="Cinematicas.Degrau"/>, que dispensa a cena a partir de 50%): sem
	///     este funil, toda transformacao de Ultra Instinto fora da estreia ficaria presa nos 8,8 s da
	///     cena curta ETERNAMENTE, porque o numero que a dispensa nunca mais subiria;
	///   * o bit de DOMINADA que vai pra zona (<see cref="PacoteDeForma"/>).
	///
	/// E a substituicao e honesta: a proficiencia REAL da disciplina tambem e 0-100, tambem so cresce
	/// USANDO (o `emForma` do `TickDasDisciplinas`, que e o `ui_form`/`ue_form` do DM) e tambem nao se
	/// compra. Trocou-se o que a barra mede, nao a escala.
	/// ========================================================================================
	/// </summary>
	private static double MaestriaDaForma(ServerPlayer pl, string forma) =>
		Disciplinas.DaForma(forma) is { } disc
			? EstadoDe(pl, disc.Def.Tipo).Real
			: pl.Forma.Maestria.De(forma);

	/// <summary>
	/// ESTE JOGADOR DOMINOU (100%) A FORMA <paramref name="forma"/>?
	///
	/// Uma linha, mas com nome, porque ela e lida em TRES pontos que precisam concordar -- o anuncio,
	/// a sincronia de quem chega na zona e o reanuncio do instante em que a barra cruza os 100%. Os
	/// 100 aqui sao o mesmo teto do `Maestrias.Subir`, que para de subir nesse numero.
	/// </summary>
	private static bool Dominou(ServerPlayer pl, string forma) => MaestriaDaForma(pl, forma) >= 100;

	/// <summary>
	/// O TICK DA FORMA: cobra Ki, sobe maestria, derruba quem ficou sem folego.
	/// </summary>
	private void TickDaForma(ServerPlayer pl, double dt)
	{
		EstadoDeForma est = pl.Forma;

		// O RELOGIO DE COMBATE CORRE FORA DA FORMA TAMBEM: o `combatTime` do DM e do CORPO, nao da
		// transformacao, e quem entra em Legendary no meio de uma briga longa ja entra com a rampa
		// cheia -- e o que faz a forma da furia valer a pena justamente quando a furia existe.
		est.CombateSegundos = pl.Combate is { EmCombate: > 0 }
			? est.CombateSegundos + dt
			: 0;

		// O RELOGIO DA CENA CORRE FORA DA FORMA TAMBEM, e por um motivo a mais que o de combate: a
		// cena do OOZARU prende o corpo com a ESCADA NA BASE (o `Apeshit` derruba o SSJ antes de
		// chamar a fera). Contar o prazo depois do `NaBase` logo abaixo deixaria aquele corpo
		// congelado ate o fim da sessao -- o congelamento nunca acabaria por nao ser contado.
		if (pl.CenaSegundos > 0) pl.CenaSegundos = Math.Max(0, pl.CenaSegundos - dt);

		if (est.NaBase) return;

		// O NOCAUTE VEM ANTES DO CONGELAMENTO, e a ordem E a regra do item 4: cair no meio da
		// cinematica desfaz a forma como sempre desfez. O `Reverter` sai pelo `AnunciarForma`, que
		// marca a cena da BASE (zero) -- ou seja o congelamento cai junto com a forma, no mesmo gesto,
		// sem uma segunda linha pra alguem esquecer.
		if (pl.Ficha.KO || pl.Ficha.dead) { Reverter(pl, "voce cai, e a forma se desfaz."); return; }

		// ============================ EM CENA O TANQUE NAO SE MEXE SOZINHO ============================
		// O dono: *"o ki continua caindo em cinematica, faça o ki ficar parado enquanto esta na
		// cinematica"*. Com os prazos do DM de volta, o SSJ3 prende o corpo por 140 s -- tempo de
		// sobra pra o dreno esvaziar o tanque e DERRUBAR o jogador da forma no meio da estreia dela.
		// Uma transformacao que se desfaz sozinha durante a propria cinematica nao e um custo, e um
		// defeito: ninguem tinha como agir pra evitar.
		//
		// A MAESTRIA PARA JUNTO, e nao por simetria -- e a mesma transacao. O que paga a maestria e o
		// dreno ("sustentar a transformacao E o treino dela", tres linhas abaixo); congelar o preco e
		// manter o ganho daria 116 s de maestria de graca justamente na cena mais longa do jogo, e a
		// maestria e o que DISPENSA a cena (>= 50%): a estreia estaria pagando pela propria remocao.
		//
		// E `AplicarForma` nao roda: ele so existe neste tique pra acompanhar o degrau de maestria, e
		// maestria parada e multiplicador parado.
		// ========================================================================================
		if (EmCena(pl)) return;

		double dreno = est.DrenoPorSegundo() * pl.Ficha.MaxKi * dt;
		pl.Ficha.Ki -= dreno;

		if (pl.Ficha.Ki <= 0)
		{
			pl.Ficha.Ki = 0;
			Reverter(pl, "o Ki acaba e a forma se desfaz.");
			return;
		}

		// MAESTRIA SO CRESCE DENTRO DA FORMA. Sustentar a transformacao E o treino dela.
		//
		// **MENOS NAS FORMAS DE DISCIPLINA**, e a recusa mora no `Maestrias.Por` e nao numa guarda
		// aqui -- de proposito, porque este nao e o unico escritor (ver o cabecalho dele). Numa forma
		// de Ultra Instinto ou de Destruicao o `Subir` abaixo nao escreve nada e devolve false; quem
		// paga o uso dela e o `TickDasDisciplinas`, que credita a proficiencia REAL da SKILL pelo
		// `emForma` -- o `ui_form`/`ue_form` do DM (`UltraInstinct.dm:209-216`, `UltraEgo.dm:330`).
		// ============================ E NEM TODA FORMA SE TREINA SENDO SUSTENTADA ============================
		// A pergunta e do Core (`Catalogo.SustentarTreina`) e hoje ela recusa exatamente uma coisa: as
		// quatro SUPRESSOES do Frost Demon. Elas nao sao transformacao -- sao o corpo se recolhendo --,
		// e o original poe a barra pra andar so da forma base pra cima (`icer.dm:45`). Sem esta guarda o
		// Mutante masterizaria a propria base sentado na casca mais apertada, que e onde ele NAO corre
		// risco nenhum: o motor de instabilidade (a coisa que a maestria existe pra vencer) nunca
		// chegaria a rodar.
		//
		// No `if` e nao dentro do `Subir` porque quem sabe que este tique E o pagamento e este arquivo;
		// o livro de maestrias so guarda numero. (O caso vizinho -- forma de disciplina -- mora la
		// dentro por outro motivo: la sao TRES escritores e a recusa tem que valer pros tres.)
		// ================================================================================================
		bool dominavaAntes = Dominou(pl, est.Atual);
		if (Catalogo.SustentarTreina(est.Def)
			&& SubirMaestriaDaZona(pl, est.Atual, Catalogo.MaestriaPorSegundo, dt, out string marco))
		{
			// O NOME SAI DO FUNIL, e aqui isso muda o texto de verdade: quem acaba de cruzar os 100%
			// no SSJ1 le "Super Saiyajin Grade 4: forma DOMINADA" -- o nome novo chega junto com o
			// marco que o criou, e nao uma transformacao depois.
			Avisar(pl, $"{Catalogo.NomeDe(est.Def, Dominou(pl, est.Atual))}: {marco}.");
			GD.Print($"[server] {pl.Name}: {est.Atual} -> {marco}");
		}

		// ============================ DOMINAR REDESENHA O CORPO NA HORA ============================
		// A maestria cruza os 100% DENTRO da forma -- e o unico eixo do jogo assim, porque so se
		// treina uma forma estando nela. Sem esta linha o cabelo de Grade 4 so apareceria na PROXIMA
		// transformacao: o jogador veria "forma DOMINADA" no chat e nada mudar na tela, teria que
		// voltar pra base e subir de novo pra ver o que acabou de conquistar.
		//
		// REUSA O `PacotesDeEstado` (o mesmo caminho de quem entra na zona) e nao um `AnunciarForma`:
		// nao houve mudanca de forma nenhuma, entao nao pode haver cinematica, nem estalo, nem clima,
		// nem `MarcarCena`. Aquele pacote diz "ele ESTA em" (`de == para`) e e exatamente esta a
		// frase -- o corpo e o mesmo, o que mudou foi o que ele sabe fazer com ele.
		// ======================================================================================
		if (!dominavaAntes && Dominou(pl, est.Atual))
			foreach (ServerPlayer o in ZoneList(pl.Zone.Hash)) MandarEstadoDeForma(pl, o);

		// o multiplicador muda EM DEGRAUS conforme a maestria sobe, e o degrau pode cair no meio da
		// luta -- por isso recalcula por tick em vez de so na transformacao
		AplicarForma(pl);
	}

	/// <summary>
	/// O FUNIL UNICO DA MAESTRIA POR TEMPO NA FORMA -- e a porta por onde o ritmo da ZONA entra.
	///
	/// ============================ POR QUE UM FUNIL, E NAO DOIS MULTIPLIQUES ============================
	/// Ha DOIS tiques que creditam maestria por sustentar uma forma: o da escada
	/// (<see cref="TickDaForma"/>) e o do Oozaru (`GameServer.Oozaru.cs`). Os dois chamavam
	/// `Maestria.Subir` direto, cada um com a propria taxa. Ligar o `zoneMasteryMult` "nos dois
	/// lugares" e exatamente a forma de falha que os comentarios deste port mais repetem: liga-se num,
	/// esquece-se do outro, e o defeito e mudo -- a maestria de macaco simplesmente nao acelerava
	/// dentro da Sala, e ninguem teria como notar sem cronometrar as duas.
	///
	/// Aqui a multiplicacao acontece UMA vez. Um terceiro tique de forma que apareça amanha ganha o
	/// ritmo de zona de graca -- ou nao compila, se chamar o `Subir` direto e alguem reparar.
	/// ==============================================================================================
	///
	/// **4x, e nao 280x** -- a razao esta escrita em <see cref="Jandirus.Core.World.SalaDoTempo.MaestriaMult"/>.
	/// </summary>
	private static bool SubirMaestriaDaZona(ServerPlayer pl, string id, double porSegundo, double dt,
											out string marco) =>
		pl.Forma.Maestria.Subir(id, porSegundo * dt * Math.Max(pl.Ficha.zoneMasteryMult, 0), out marco);

	/// <summary>
	/// A FORMA SE DESFAZ (Ki zerado, nocaute). Vai direto ao PISO e nao um degrau -- ver
	/// <see cref="ParaOndeSeRecua"/> pro caminho voluntario.
	///
	/// A diferenca e a intencao: recuar e uma DECISAO (afrouxa um ponto e para onde quiser), e cair
	/// nao e -- quem desmaia larga tudo. Pro Frost Demon isso quer dizer que o Mutante nocauteado
	/// volta a primeira supressao, que e onde o corpo dele descansa, e nao a base que ele ainda nao
	/// segura.
	/// </summary>
	private void Reverter(ServerPlayer pl, string motivo)
	{
		string antes = pl.Forma.Atual;
		string piso = Catalogo.IdDoPiso(Perfil(pl));
		if (antes == piso) return;
		pl.Forma.Entrar(piso);
		AplicarForma(pl);
		Avisar(pl, motivo);
		AnunciarForma(pl, antes, piso, estreia: false);
	}

	/// <summary>
	/// Conta pra ZONA que alguem mudou de forma. E o que acende a aura e pinta o cabelo nos outros
	/// clientes -- e o que decide QUANTA cinematica o dono vai ver.
	///
	/// ============================ O DEGRAU E DERIVADO AQUI, E NAO PEDIDO AOS CHAMADORES ============================
	/// Sao SEIS chamadores hoje (subir, descer, o `Reverter` do Ki zerado, o DirectSSJ do Grade 4 nas
	/// duas pontas dele, e a saida do Oozaru Dourado). Passar o degrau como parametro seria pedir a
	/// seis lugares que lembrem da mesma regra -- e "ligar a regra num chamador e esquecer do outro" e,
	/// escrito nos comentarios deste proprio arquivo, o erro que mais se repetiu neste port.
	///
	/// Entao o que os chamadores dizem continua sendo o que eles SABEM (`estreia`: o `Entrar()` disse
	/// que e a primeira vez?), e quem cruza isso com a maestria e <see cref="Cinematicas.Degrau"/>,
	/// uma vez, aqui.
	/// ======================================================================================================
	/// </summary>
	/// <param name="estreia">O retorno do <see cref="EstadoDeForma.Entrar"/> -- a ESTREIA desta forma.</param>
	/// <param name="semCena">
	/// FORCA a transformacao a ser instantanea, ignorando maestria. Existe por UM caminho: sair do
	/// Oozaru Dourado dominado cai em SSJ4 sem cena nenhuma, e isso e pedido do dono ("n tem
	/// cinematica, apenas o ozaru e desfeito e o player cai no estagio de ssj4"). Nao e a porta dos
	/// fundos da regra: quem passar `true` aqui esta dizendo "esta mudanca de forma nao e um gesto do
	/// jogador", e hoje so existe uma dessas.
	/// </param>
	private void AnunciarForma(ServerPlayer pl, string de, string para, bool estreia,
							   bool semCena = false)
	{
		// A MAESTRIA E DA FORMA ALVO, e lida AGORA -- o `Entrar()` ja rodou, mas ele nao mexe em
		// maestria (so em `Atual`, `Liberadas` e `EstreiaVista`), entao o numero aqui e o mesmo que
		// o jogador tinha um instante antes de apertar a tecla. Vale igual pras formas de disciplina:
		// o `RenovarEnergiaNaForma` mexe na energia ATUAL, e quem responde aqui e a REAL.
		DegrauDeCena degrau = semCena
			? DegrauDeCena.Nenhuma
			: Cinematicas.Degrau(Catalogo.Def(para), estreia, MaestriaDaForma(pl, para));

		// E O SERVIDOR ANOTA O PRAZO NO MESMO GESTO. Aqui, e nao no `Transformar`, pelo motivo que o
		// bloco acima ja explica pro degrau: este e o funil unico -- subir, descer, cair por Ki zerado
		// e o DirectSSJ do admin passam todos por esta linha. Descer marca ZERO (a base nao tem cena),
		// e e assim que o congelamento acaba junto com a forma quando alguem e nocauteado no meio.
		MarcarCena(pl, Catalogo.Def(para), degrau);

		// A BANCADA ESCUTA O DEGRAU AQUI. Ele nasce nesta linha e some no fio -- e as duas regras que
		// mais custam pra o jogador sao exatamente sobre ele: a estreia do SSJ4 que TEM que tocar, e a
		// queda do Oozaru Dourado que NAO pode ter cena. Nulo em jogo. Ver `GameServer.FormasTeste.cs`.
		EscutaDeAnuncios?.Add((pl.Id, de, para, degrau));

		// O CEU RESPONDE A ALGUMAS FORMAS. Pendurado AQUI e nao no `Transformar` porque este e o
		// funil unico -- subir, descer e cair por Ki zerado passam todos por esta linha. Ver
		// `GameServer.Clima.ClimaPorTransformacao`, que e o gancho e a tabela.
		ClimaPorTransformacao(pl, de, para);

		// A MESMA LEITURA DE MAESTRIA QUE DEU O DEGRAU, e por isso ela nao se repete: o `Degrau` acima
		// pergunta se passou de 50%, este bit pergunta se chegou a 100%. Duas faixas da mesma barra.
		NetDataWriter w = PacoteDeForma(pl.Id, de, para, degrau, Dominou(pl, para));
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			// A BANCADA ESCUTA PRA QUEM O ANUNCIO FOI, e ela escuta DE DENTRO do laco de proposito.
			// O alcance da cena (musica, tremor, a cena em si) nao e uma regra do cliente: e esta
			// linha do `foreach`. Anotar os destinos de fora -- perguntando `ZoneList` de novo num
			// metodo de teste -- provaria que `ZoneList` devolve o que `ZoneList` devolve, e passaria
			// verde no dia em que alguem trocasse ESTE laco por `_players.Values`. Ver
			// `GameServer.FormasTeste.EscutaDeDestinos`.
			EscutaDeDestinos?.Add((pl.Id, o.Id));

			// QUEM ESTA PERTO O BASTANTE **VIU** ESTA FORMA, e ver e o pre-requisito de ser
			// ensinado nela. E o `mst_note_form` do DM (`MasterStudent.dm:207`), que la esta
			// repetido em sete buffs raciais e aqui e uma linha no funil. O RAIO e conferido dentro
			// do metodo -- este laco e da zona inteira. Ver `GameServer.Mestre.NotarFormaVista`.
			NotarFormaVista(o, pl, para);

			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}

	/// <summary>
	/// O PACOTE EM SI, sem decidir pra quem ele vai.
	///
	/// Extraido porque agora ha DOIS destinos possiveis: o anuncio (zona inteira, e um ACONTECIMENTO)
	/// e a sincronia de quem acaba de chegar (uma pessoa so, e um ESTADO -- ver
	/// <see cref="SincronizarFormas"/>). Montar o writer nos dois lugares seria duas verdades sobre o
	/// formato do pacote, e a segunda envelheceria calada no dia em que um campo entrasse.
	/// </summary>
	private static NetDataWriter PacoteDeForma(int id, string de, string para, DegrauDeCena degrau,
											   bool dominada)
	{
		var w = Protocol.Begin(Protocol.S2C.Forma);
		w.Put(id);
		w.Put(Catalogo.Rede(de));
		w.Put(Catalogo.Rede(para));
		// O BYTE DO DEGRAU no lugar do `bool primeira`. Um bit nao comporta tres estados, e mandar um
		// segundo bool ao lado criaria a combinacao invalida "primeira E instantanea" -- ver
		// `DegrauDeCena`.
		w.Put((byte)degrau);

		// ============================ E O DOMINIO, QUE E UM BIT E NAO O NUMERO ============================
		// O cliente precisa saber se ESTE corpo dominou a forma que esta assumindo, porque o Super
		// Saiyajin a 100% troca o cabelo (`Catalogo.SufixoDoCabeloDe` -- o Grade 4). E ele nao tem como
		// deduzir: `Protocol.AtributosState.Maestrias` e a ficha PESSOAL de quem esta na frente da tela,
		// e ninguem sabe a maestria de mais ninguem.
		//
		// UM BOOL E NAO A PORCENTAGEM, e nao e economia de banda (seria um byte contra um byte): mandar
		// o numero publicaria a progressao alheia inteira -- de quanto em quanto tempo o vizinho treina
		// --, e o unico fato que a tela precisa e "cruzou os 100%". E o mesmo criterio do `BitCorrendo`
		// do snapshot: nao se manda a velocidade pra dizer o que um bit diz.
		//
		// NAO CABIA NO BYTE DO DEGRAU: o cliente valida aquele byte com `Enum.IsDefined`, entao um bit
		// alto la faria o pacote inteiro cair em `DegrauDeCena.Nenhuma`.
		// =============================================================================================
		w.Put(dominada);
		return w;
	}

	/// <summary>
	/// QUEM CHEGA TEM QUE VER QUEM JA ESTA TRANSFORMADO -- e quem ja esta tem que ver o que chegou.
	///
	/// ============================ POR QUE ISTO FALTAVA, E O QUE ELE CUSTAVA ============================
	/// `S2C.Forma` e `S2C.Oozaru` sao pacotes de ACONTECIMENTO: saem uma vez, pra quem estava na zona
	/// naquele instante. Quem entra depois -- por login ou por troca de planeta -- nunca soube, e
	/// recebia so o `PeerLook`, que e a aparencia BASE. Resultado: um Super Saiyajin 3 desenhado como
	/// lutador comum, e o corpo proprio do SSJ4 (a pelagem) invisivel. E a mesma familia de defeito das
	/// construcoes, das portas e das feridas -- todas ja resolvidas por reenvio na entrada da zona.
	///
	/// O DONO RELATOU PELO SSJ4, mas o defeito nunca foi dele: vale pra toda forma que muda o desenho.
	/// ================================================================================================
	///
	/// ============================ E A CENA NAO PODE TOCAR ============================
	/// Este e o degrau perigoso. `DegrauDeCena.Nenhuma` nao e economia de banda: e a diferenca entre
	/// "voce chegou e ele ja era SSJ3" e "voce chegou e ficou preso assistindo a estreia de um
	/// desconhecido, que aconteceu antes de voce existir naquela zona". No cliente esse degrau cai no
	/// caminho direto do `World.AoMudarForma` -- cabelo, corpo proprio, contorno e raios no mesmo
	/// quadro, sem `Transformacao` nenhuma e sem prender corpo (`Cinematicas.NoDegrau` devolve nulo).
	/// ================================================================================
	///
	/// SO SAI PACOTE DE QUEM TEM O QUE DIZER: corpo em forma base e sem fera nasce certo pelo caminho
	/// normal, e mandar "voce esta na base" seria pagar banda por quadro identico.
	/// </summary>
	private void SincronizarFormas(ServerPlayer novo)
	{
		// O PROPRIO NOVO ENTRA NO LACO (o `ZoneList` ja o contem quando isto roda), e nao e detalhe:
		// relogar ou trocar de planeta transformado destroi e recria o boneco LOCAL tambem. Sem o
		// par (novo, novo) o dono do SSJ4 veria todo mundo certo e a si mesmo careca e sem pelagem --
		// que foi exatamente metade do relato.
		foreach (ServerPlayer outro in ZoneList(novo.Zone.Hash))
		{
			if (outro != novo) MandarEstadoDeForma(outro, novo);   // o que ja estava, pra quem chegou
			MandarEstadoDeForma(novo, outro);                      // e quem chegou, pra quem ja estava
		}
	}

	/// <summary>O estado (forma e fera) de <paramref name="quem"/> na tela de <paramref name="para"/>.</summary>
	private static void MandarEstadoDeForma(ServerPlayer quem, ServerPlayer para)
	{
		if (para.Peer == null) return;

		foreach (NetDataWriter w in PacotesDeEstado(quem))
		{
			// A BANCADA ESCUTA AQUI, e nao no `PacotesDeEstado`, de proposito: e este ponto que sabe as
			// tres coisas que a sincronia pode errar juntas -- QUEM ela descreve, PRA QUEM ela manda e o
			// que o pacote diz. Nulo em jogo. Ver `GameServer.FormasTeste.cs`.
			EscutaDeSincronia?.Add((quem.Id, para.Id, w));
			para.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
	}

	/// <summary>
	/// O QUE HA A DIZER SOBRE O CORPO DE <paramref name="quem"/> -- os pacotes prontos, sem escolher
	/// destinatario.
	///
	/// Separado do envio porque sao duas decisoes diferentes e so uma delas depende de quem esta
	/// olhando: o CONTEUDO e do corpo descrito, o DESTINO e da zona. Juntas, a bancada teria que
	/// arrancar bytes de um `NetPeer` pra conferir a regra que mais importa aqui (o degrau).
	///
	/// ============================ `de` == `para`: "ELE ESTA EM", E NAO "ELE MUDOU PRA" ============================
	/// Nao existe forma anterior pra quem acaba de chegar, e a escolha do que por no `de` nao e
	/// decorativa: o cliente USA a diferenca. O caminho instantaneo do `World.AoMudarForma` toca um
	/// `Trilha.Dash` no fim -- o estalo que marca o instante da transformacao --, e um recem-chegado
	/// numa zona com tres transformados ouviria tres estalos de coisas que aconteceram antes de ele
	/// existir ali. E a mesma familia do erro da cinematica, so que no ouvido.
	///
	/// Escrever `IdBase` no `de` NAO resolveria: `base -> ssj1` e uma transformacao de verdade, e
	/// suprimir o som dela apagaria o estalo do jogo inteiro. Ja "de X pra X" nao e uma mudanca que
	/// existe -- e por isso ele pode significar estado sem tirar nada de ninguem.
	/// =========================================================================================================
	///
	/// SO SAI PACOTE DE QUEM TEM O QUE DIZER: corpo em forma base e sem fera devolve LISTA VAZIA. Ele
	/// nasce certo pelo caminho normal, e mandar "voce esta na base" seria pagar banda por quadro
	/// identico.
	/// </summary>
	private static List<NetDataWriter> PacotesDeEstado(ServerPlayer quem)
	{
		var saida = new List<NetDataWriter>(2);

		if (!quem.Forma.NaBase)
			saida.Add(PacoteDeForma(quem.Id, quem.Forma.Atual, quem.Forma.Atual, DegrauDeCena.Nenhuma,
									Dominou(quem, quem.Forma.Atual)));

		// A FERA DEPOIS DA ESCADA, e a ordem e a mesma do jogo (ver `AnunciarOozaru`): o macaco cobre o
		// que a forma desenhou. Invertido, o `S2C.Forma` chegaria por cima e devolveria o lutador de
		// 32 px por baixo de um corpo que devia ter 96.
		if (quem.Oozaru != FormaOozaru.Nao)
			saida.Add(PacoteDeOozaru(quem.Id, quem.Oozaru, primeira: false, DegrauDeCena.Nenhuma));

		return saida;
	}
	/// <summary>
	/// O QUE O DISCO DEVOLVE AO CORPO: forma, maestria, limiares, o lado pessoal do DISCIPULADO e a
	/// disciplina divina. Devolve os nomes das maestrias descartadas, pra quem chamou avisar.
	///
	/// ============================ POR QUE ISTO E UM METODO ============================
	/// Era um bloco no meio do <see cref="Entrar"/>, e continua sendo chamado so de la em producao.
	/// Virou metodo porque a bancada `--mestrevivo` precisa RELOGAR de verdade (o vinculo de mestre,
	/// a porta cortada, a recarga de 5 min e o `wastaught` sao todos coisas que so o relogin prova),
	/// e um `ServerPlayer` sem `NetPeer` nao pode passar pelo `Entrar` -- ele manda pacote na
	/// primeira linha.
	///
	/// A alternativa seria a bancada reescrever estas trinta linhas. Seria a duplicata da PARTE 3
	/// ("duas casas pra uma formula"), com o agravante de que a copia estaria justamente no lugar
	/// que deveria vigiar a original: o dia em que alguem esquecesse de restaurar `PortasCortadas`
	/// no login, a bancada continuaria verde restaurando a sua.
	/// ==============================================================================
	///
	/// ============================ NOTA DE RESTAURO (2026-08-14) ============================
	/// O CORPO ABAIXO E O ORIGINAL DE 23:07, nao uma reconstrucao. A versao que estava aqui era uma
	/// reconstrucao de memoria feita depois do `git checkout` -- e ela DIVERGIA em sete pontos:
	/// nao restaurava `GradesLigados` do jeito certo, perdia `FormasVistas`, nao migrava
	/// `PortasCortadas`, esquecia `RecargaDeEnsino` e `ChefesVistos`, nao punha o corpo no PISO
	/// (`Catalogo.IdDoPiso`) e nao zerava `fd_release`/`fd_ki_locked`. O ultimo deixava o Frost Demon
	/// Mutante logar em `base`, com quatro vezes o poder devido.
	/// ======================================================================================
	/// </summary>
	private List<string> RestaurarFormaEDisciplina(ServerPlayer pl, CharacterSave? c)
	{
		// A FORMA NAO ATRAVESSA O LOGOUT: quem sai SSJ3 volta na base. O que persiste e a
		// MAESTRIA (semanas de jogo) e quais formas ja despertaram (a cinematica so roda uma vez).
		pl.Forma = new Jandirus.Core.Forms.EstadoDeForma();
		// O DESCARTE DAS FORMAS DE DISCIPLINA VOLTA NOMEADO, e e avisado pelo `Entrar` (depois do
		// `JoinAccepted`, quando o cliente ja tem chat). Ver `Maestrias.DoSave` pro porque de
		// descartar em vez de migrar -- e pro porque de nao dar pra fazer isso calado.
		List<string> maestriasDescartadas = pl.Forma.Maestria.DoSave(c?.Maestrias);
		// OS LIMIARES DESTE PERSONAGEM vem do disco -- sorteados no nascimento, nunca de novo.
		pl.Forma.Limiares = c?.Limiares;

		// ============================ A PREFERENCIA DOS GRADES, E O NULO QUE VIRA `true` ============================
		// **AQUI E QUE UM CORPO GANHA OPINIAO.** `EstadoDeForma.GradesLigados` nasce NULO de proposito
		// -- corpo sem dono (NPC sorteado, cerebro da IA) sobe a escada pela regra de sempre --, e este
		// metodo e o unico caminho "save -> JOGADOR em jogo". Quem passa por aqui tem alguem apertando
		// o C, entao a preferencia deixa de ser nula: o que o disco disser, e LIGADO quando ele nao diz
		// nada (save de antes do verb existir, ou personagem novo). Ligado e o que o jogo ja fazia.
		//
		// COMO REPROVA SE ESTA LINHA SUMIR: o verb `graus` continuaria respondendo no chat e a escolha
		// morreria no logout -- o defeito que este arquivo ja pagou com o `wastaught` e com a escolha de
		// skill do Metamoriano. A bancada `--formasteste` relogo e conferiu (ver `OsGradesNoCaminhoDoC`).
		// ========================================================================================================
		pl.Forma.GradesLigados = c?.GradesLigados ?? true;

		// ============================ O NUMERO DA FORMA PASSA PELA MIGRACAO ============================
		// O disco guarda forma por `FormaDef.IdRede`, e um numero que saiu do catalogo nao casa com
		// entrada nenhuma -- vira lixo silencioso no `HashSet`. `Catalogo.RedeDoSave` traduz os que
		// mudaram de dono (hoje: o `306`, do Mistico Ascendido que virou um ponto da curva do `305`)
		// e devolve igual todo o resto. Ver o cabecalho de `RedeDoSave` pro porque de nao dar pra
		// simplesmente deixar o numero velho passar.
		//
		// E A TRADUCAO E UMA CHAMADA SO, PRAS DUAS LISTAS (`Catalogo.RedesDoSave`): a expressao estava
		// escrita duas vezes aqui, e duas copias da mesma migracao e onde a proxima fusao de formas
		// passa a valer so pra metade -- ver o cabecalho da funcao.
		if (c?.FormasDespertadas is { Count: > 0 })
			pl.Forma.Liberadas = Jandirus.Core.Forms.Catalogo.RedesDoSave(c.FormasDespertadas);

		// ============================ A MIGRACAO DOS DOIS BITS ============================
		// `FormasEstreadas` nasceu depois de `FormasDespertadas` (ver `EstadoDeForma`). Num save
		// gravado antes dela o campo NAO EXISTE no JSON e chega **null** -- e null aqui quer dizer
		// "este personagem e de antes da separacao, tudo que ele tem liberado ele ja viu".
		//
		// O teste e `is null` e nao `Count == 0` de proposito: lista VAZIA e uma resposta legitima
		// de um save novo (personagem que nunca se transformou), e trata-la como "save antigo"
		// copiaria as liberadas por cima -- exatamente no unico caso em que isso apaga uma estreia
		// devida, que e o do SSJ4 liberado pelo Oozaru Dourado e ainda nao assistido.
		// ==============================================================================
		pl.Forma.EstreiaVista = c?.FormasEstreadas is { } vistas
			? Jandirus.Core.Forms.Catalogo.RedesDoSave(vistas)
			: [.. pl.Forma.Liberadas];

		// O DISCIPULADO, LADO DO PERSONAGEM. As tres passam pela MESMA migracao de numero de forma
		// que as duas de cima -- sao ids de rede, e um id que saiu do catalogo viraria lixo mudo no
		// `HashSet`. A recarga e prazo de relogio de parede e vem crua. Ver
		// `GameServer.Mestre.cs` (o vinculo em si mora no `mestres.txt`, nao aqui).
		pl.FormasVistas = c?.FormasVistas is { Count: > 0 } vv
			? Jandirus.Core.Forms.Catalogo.RedesDoSave(vv) : [];
		pl.Forma.PortasCortadas = c?.PortasCortadas is { Count: > 0 } pc
			? Jandirus.Core.Forms.Catalogo.RedesDoSave(pc) : [];
		pl.RecargaDeEnsino = c?.MestreRecargaAte ?? 0;

		// OS CHEFES JA VISTOS -- crus, sem migracao nenhuma: sao ids de molde do `npcs.json`, e um
		// molde que saiu do arquivo continua sendo uma coisa que a pessoa VIU (o dono pode estar so
		// renomeando uma saga). Quem confere o catalogo e a convocacao. Ver `GameServer.Mente.cs`.
		pl.ChefesVistos = c?.ChefesVistos is { Count: > 0 } cv
			? new HashSet<string>(cv, StringComparer.OrdinalIgnoreCase)
			: new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		// A SALA DO TEMPO **NAO ENTRA AQUI**, e a ausencia e uma decisao: os tres campos dela
		// (recarga, chave do Guardiao, prisao) sao lidos no `AccountStore.ParaJogador`, que se
		// declara o funil unico "save -> jogador em jogo". Le-los nos dois lugares seria a mesma
		// migracao escrita duas vezes -- e a segunda a envelhecer seria esta, calada.

		// A DISCIPLINA DIVINA. O toggle NAO volta ligado: uma passiva que drena e reacende sozinha
		// no login gastaria a precisao de quem so entrou pra conversar.
		if (c is { Disciplina: 1 })
		{
			pl.UltraInstinct.Aprendida = true;
			pl.UltraInstinct.Real = c.DiscReal;
			pl.UltraInstinct.Atual = Math.Min(c.DiscAtual, c.DiscReal);
			pl.Disciplina = Jandirus.Core.Forms.TipoDeDisciplina.UltraInstinct;
		}
		else if (c is { Disciplina: 2 })
		{
			pl.PoderDaDestruicao.Aprendida = true;
			pl.PoderDaDestruicao.Real = c.DiscReal;
			pl.PoderDaDestruicao.Atual = Math.Min(c.DiscAtual, c.DiscReal);
			pl.Disciplina = Jandirus.Core.Forms.TipoDeDisciplina.PoderDaDestruicao;
		}

		// ============================ E O CORPO VOLTA PRO **PISO** DELE, QUE NEM SEMPRE E A BASE ============================
		// "A forma nao atravessa o logout" continua valendo -- quem sai em 2a Evolucao volta na forma
		// de repouso. O que mudou e que repouso deixou de ser sinonimo de `IdBase`: o Frost Demon
		// descansa numa ENTRADA da escada dele (a 5 pro normal, a 1 pro Mutante, ver
		// `Catalogo.PisoDaEscada`), porque a forma de repouso dele tem sprite proprio e -- no caso do
		// Mutante -- multiplicador 0,25x sobre um BP de fabrica quadruplicado.
		//
		// Sem esta linha o Mutante logaria em `base`: 1x sobre esse BP, ou seja QUATRO VEZES o poder
		// que ele deve ter, do primeiro segundo em diante e sem nada na tela dizendo por que.
		//
		// `Atual =` E NAO `Entrar()`: entrar LIBERA a forma e QUEIMA a estreia dela (`EstadoDeForma`),
		// e nenhuma das duas coisas descreve logar. O repouso nao se conquista e nao tem cinematica.
		//
		// O VAZAMENTO E O TRAVAMENTO ZERAM JUNTO, e nao e generosidade: o piso e, por construcao, a
		// forma mais fraca que o corpo alcanca -- ou seja SEMPRE estavel (`fd_stable_gate` nunca fica
		// abaixo da primeira supressao). O motor devolveria os dois campos em ~25 s de qualquer jeito;
		// zerar aqui e so nao comecar a sessao mentindo. Ver `GameServer.Frost.cs`.
		pl.Forma.Atual = Jandirus.Core.Forms.Catalogo.IdDoPiso(Perfil(pl));
		pl.Ficha.fd_release = 1;
		pl.Ficha.fd_ki_locked = false;

		AplicarForma(pl);

		return maestriasDescartadas;
	}
}
