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
		Raiva: NivelDaRaivaDe(pl));

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
	/// Quem tem ALGUMA escada de transformacao. Era `pl.Race is "Saiyan" or "Halfbreed"`, e passou a
	/// ser derivado: quem tem linha aberta no catalogo tem escada, inclusive o nao-Saiyajin que
	/// despertou o ki divino -- que antes ouvia "sua raca nao tem essa escada" com o SSG na mao.
	/// </summary>
	private static bool TemEscada(ServerPlayer pl) => Catalogo.LinhasAbertas(Perfil(pl)).Count > 0;

	/// <summary>Sangue diluido puxa a base do SSJ1 de 2 pra 1,35 (`ssj1base` nerfado).</summary>
	private static bool SangueDiluido(ServerPlayer pl) => pl.Race == "Halfbreed";

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
	/// Quem responde "e amigo?" e o `Core.Social.Convivio` (`is_friend()`, `FRIEND_REQ = 50`, e os
	/// pontos so passam de `ACQUAINTANCE_CAP = 49` com um pedido ACEITO -- tudo portado).
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
	/// ============================ O QUE ELE **NAO** FAZ: O BUFF DE BP DA RAIVA ============================
	/// O `Do_Anger_Stuff` do DM faz duas coisas -- abre a janela E poe `Anger = MaxAnger`, que no
	/// port viraria ate 2x de BP pelo `angerBuff` (`Fighter.Power.cs:56`). Aqui so a primeira
	/// acontece, e a razao e concreta: **o port nao tem decaimento de raiva**. No DM o `Anger` cai
	/// sozinho no laco `Stats()` (`Stats.dm:449`); aqui ninguem o escreve nem o baixa, entao
	/// escrever `MaxAnger` daria 2x PERMANENTE ao primeiro enlutado da historia do servidor. O
	/// buff de raiva e um sistema proprio por portar, e este gancho nao e o lugar de improvisa-lo.
	/// ==============================================================================================
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

		// `=` E NAO `max(...)`: o prazo REINICIA a cada evento, e nunca soma. Somar era a anomalia
		// do 20x que o DM ja pagou uma vez (`Murder.dm:112`, *"never stack, sum, or multiply"*).
		//
		// E CADA GRAU MEXE **SO NA PROPRIA JANELA**: um nocaute nao encurta nem prolonga um luto em
		// andamento, e uma morte nao precisa reacender a janela lendaria porque a extrema ja a
		// satisfaz (`NivelDeRaiva` e ordenado). Sem essa separacao, "um amigo caiu" logo depois de
		// "um amigo morreu" rebaixaria a raiva de quem esta de luto.
		long prazo = agora + (long)(SegundosDeRaiva * 1000);
		if (grau == NivelDeRaiva.Extrema) enlutado.FuriaExtremaAte = prazo;
		else enlutado.RaivaLendariaAte = prazo;

		GD.Print($"[server] {enlutado.Name}: RAIVA {grau} ({nomeDoAmigo})"
				 + (jaEstava ? "  <- prolongada" : ""));
		Avisar(enlutado, grau == NivelDeRaiva.Extrema
			? $"{nomeDoAmigo} se foi. Alguma coisa dentro de voce se PARTE."
			: $"{nomeDoAmigo} cai na sua frente, e voce nao chegou a tempo.");
		return !jaEstava;
	}

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
			if (est.NaBase) return;
			string antes = est.Atual;
			est.Entrar(Catalogo.IdBase);
			AplicarForma(pl);
			Avisar(pl, "voce volta ao normal.");
			AnunciarForma(pl, antes, Catalogo.IdBase, estreia: false);
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "nao da, caido."); return; }

		FormaDef? alvo = est.Proxima(pl.Ficha.BP, perfil);
		if (alvo == null)
		{
			Avisar(pl, PorQueNao(est, pl, perfil));
			return;
		}

		string anterior = est.Atual;
		bool primeira = est.Entrar(alvo.Id);
		AplicarForma(pl);

		// KI CHEIO AO DESPERTAR. E o que o original faz nas primeiras vezes e o que transforma a
		// cena: a forma nova nao pode nascer sem folego pra ser usada.
		if (primeira) pl.Ficha.Ki = pl.Ficha.MaxKi;

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
	private static string PorQueNao(EstadoDeForma est, ServerPlayer pl, PerfilDeFormas perfil)
	{
		// procura o degrau mais barato a partir daqui e conta o que falta NELE
		FormaDef? candidato = null;
		RecusaForma pior = RecusaForma.JaEsta;
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Id == est.Atual || d.Id == Catalogo.IdBase) continue;
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

		// ============================ TODO NOME DESTA FUNCAO SAI DO `Catalogo.NomeDe` ============================
		// Sao SETE frases nomeando forma no mesmo `switch`, e elas falam com quem tem o livro de
		// maestrias na mao (`est.Maestria`) -- a sobrecarga do Core resolve o "dominada" a partir dele,
		// e nenhuma destas linhas precisa saber que o teto e 100.
		//
		// Importa mais aqui do que parece: a recusa por Ki baixo pode cair sobre o SSJ1 de quem ja o
		// domina, e ai a mensagem certa e "Super Saiyajin Grade 4 ...". Uma recusa que chama a forma
		// por um nome que a aba Formas nao usa e o jogador procurando na tela uma forma que nao existe.
		// ====================================================================================================
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
			RecusaForma.SemKi => "Ki baixo demais pra sustentar a forma.",
			RecusaForma.Caido => "nao da, caido.",
			_ => "ainda nao.",
		};
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
		pl.Ficha.trueKiMod = Jandirus.Core.Forms.Catalogo.TetoDeKi(
			Jandirus.Core.Forms.Catalogo.Def(pl.Forma.Atual), pl.Ficha);
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
		bool dominavaAntes = Dominou(pl, est.Atual);
		if (est.Maestria.Subir(est.Atual, Catalogo.MaestriaPorSegundo * dt, out string marco))
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

	private void Reverter(ServerPlayer pl, string motivo)
	{
		string antes = pl.Forma.Atual;
		pl.Forma.Entrar(Catalogo.IdBase);
		AplicarForma(pl);
		Avisar(pl, motivo);
		AnunciarForma(pl, antes, Catalogo.IdBase, estreia: false);
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
}
