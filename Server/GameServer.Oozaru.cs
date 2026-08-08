using Godot;
using Jandirus.Core.Forms;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O OOZARU, LADO DO SERVIDOR -- porte de `Code/Modules/Skills/Buffs/racial/Oozaru.dm`.
///
/// As regras puras moram no <see cref="Oozaru"/> (Core). Aqui mora o que e ESTADO e DECISAO: quando
/// a forma comeca, quanto falta pra ela cair, quem esta no controle e o que acontece ao sair.
///
/// ============================ ESTA E A DIVIDA DO SISTEMA DE LUAS ============================
/// O ceu com fases (`GameServer.Ceu.cs`) foi construido em outra sessao EXATAMENTE pra isto, e o
/// comentario de `LuaCheiaNasceu` ja dizia: "quando o Oozaru for portado, ele entra NESTA funcao e
/// em mais lugar nenhum". A lua artificial e as ondas de Blutz, quando vierem, chamam
/// <see cref="Apeshit"/> tambem -- tres gatilhos, uma regra.
/// ==========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A LUA CHEIA PEGOU ESTE CORPO. E o `Apeshit()` do DM, e o unico caminho de entrada.
	/// </summary>
	/// <summary>
	/// ============================ OLHAR PRA LUA E UMA ESCOLHA ============================
	/// O botao vermelho embaixo da lua pede; QUEM DECIDE E AQUI. O cliente calcula a fase por
	/// funcao pura do mesmo instante, mas quem digita o id no fio e ele -- nada impede mandar
	/// `oozaru_olhar` ao meio-dia, ou de dentro de uma caverna, ou sem rabo.
	///
	/// Toda recusa FALA o motivo. Recusa muda e o mesmo que travar: o jogador aperta, nada
	/// acontece, e ele nao tem como saber se e regra ou defeito.
	/// ================================================================================
	/// </summary>
	private void OlharParaALua(ServerPlayer pl)
	{
		if (pl.Oozaru != Jandirus.Core.Forms.FormaOozaru.Nao) { Avisar(pl, "você já é a fera."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não consegue nem levantar a cabeça."); return; }

		if (!TemRaboInteiro(pl))
		{
			// O RABO E A CONDICAO, no DM tambem (`Oozaru.dm:39`): sem ele a lua nao diz nada ao
			// corpo. Cortar o rabo e como se sai da forma -- e como se evita entrar nela.
			Avisar(pl, "sem rabo, a lua é só uma pedra bonita no céu.");
			return;
		}

		Jandirus.Core.World.EstadoDoCeu ceu = CeuDe(pl);
		if (!ceu.Cheia || !ceu.LuaNoCeu)
		{
			Avisar(pl, "não há lua cheia no céu daqui.");
			return;
		}

		Apeshit(pl);
	}

	private void Apeshit(ServerPlayer pl)
	{
		// O `Osetting` do DM (um liga/desliga de "eu olho pra lua?") foi DELETADO junto com esta
		// guarda: com o botao, olhar deixou de ser uma preferencia guardada e virou um gesto. Quem
		// nao quer virar Oozaru simplesmente nao aperta.

		double genoma = pl.Ficha.Genoma?.RacePercent("Saiyan")
			?? (string.Equals(pl.Race, "Saiyan", StringComparison.OrdinalIgnoreCase) ? 100 : 0);

		RecusaOozaru r = Oozaru.PodeVirar(genoma, TemRaboInteiro(pl),
			pl.Ficha.KO || pl.Ficha.dead, pl.Oozaru);
		if (r != RecusaOozaru.Pode) return;

		bool emSsj = !pl.Forma.NaBase;
		FormaOozaru vai = Oozaru.QualSai(emSsj, pl.Ficha.SaiyanLineage, pl.Forma.Maestria,
										 pl.Ficha.BP, pl.Forma.Limiares);

		// QUEM ESTA EM SSJ E NAO TEM O DOURADO NAO VIRA NADA, e isso e do original: o `Apeshit()`
		// manda pro `GoldenApeshit()`, que volta sem fazer nada se a linhagem nao bate. Nao e furo
		// -- e o que separa as duas linhagens Saiyajin.
		//
		// A RECUSA FALA O MOTIVO, e aqui isso passou a importar de verdade: com o gate novo, quem
		// ouvir a frase vaga vai ficar em SSJ olhando pra lua achando que o jogo travou, quando o
		// que falta e dominar o SSJ1. E a mesma economia de tempo da mensagem do SSJ4.
		if (vai == FormaOozaru.Nao)
		{
			RecusaOozaru porque = Oozaru.PodeDourado(pl.Ficha.SaiyanLineage, pl.Forma.Maestria,
													 pl.Ficha.BP, pl.Forma.Limiares);
			Avisar(pl, porque switch
			{
				RecusaOozaru.SemMaestriaSsj =>
					$"a lua ferve o seu sangue, e o Super Saiyajin ainda nao e SEU o bastante pra "
					+ $"aguentar isso (maestria {pl.Forma.Maestria.De("ssj1"):0.#}% de 100%).",

				// A PORTA DE PODER FALA O NUMERO DELA, e o numero e o DESTE personagem (o `ultrassjat`
				// sorteado no nascimento) -- nao a constante de fabrica. Sem dizer quanto falta, o
				// jogador que ja dominou o SSJ1 fica olhando pra lua achando que o gate da maestria
				// nao pegou. E a mesma economia de tempo do ramo de cima.
				RecusaOozaru.SemPoder =>
					$"a lua ferve o seu sangue, mas o seu corpo ainda nao aguenta o que ela pede "
					+ $"(BP base {pl.Ficha.BP:N0} de {PortaDoDourado(pl):N0}).",

				_ => "a lua puxa alguma coisa em voce, e nao encontra resposta.",
			});
			return;
		}

		VirarFera(pl, vai);
	}

	/// <summary>
	/// O BP BASE QUE O DOURADO PEDE **DESTE** PERSONAGEM. So a mensagem de recusa usa isto -- quem
	/// DECIDE e o `Oozaru.PodeDourado`, que faz a mesma conta por dentro.
	///
	/// Existe pra a frase nao mentir: o limiar e sorteado no nascimento (`ultrassjat`, ver
	/// `Catalogo.PortaOozaruDourado`), entao imprimir a constante de fabrica daria um numero que nao
	/// e o do jogador -- ele treinaria ate o valor errado e continuaria sendo recusado.
	/// </summary>
	private static double PortaDoDourado(ServerPlayer pl)
	{
		FormaDef? d = Catalogo.Def(Oozaru.IdDourado);
		if (d == null) return 0;
		return pl.Forma.Limiares?.Porta(d) is > 0 and var p ? p : d.PortaBp;
	}

	/// <summary>
	/// O FUNIL DA FERA: entra no Oozaru JA DECIDIDO, sem perguntar se pode.
	///
	/// ============================ POR QUE ISTO SE SEPAROU DO `Apeshit` ============================
	/// Porque agora ha DOIS jeitos de virar macaco e so um deles passa pelas portas: a lua (que
	/// confere rabo, genoma, fase e linhagem) e o `admin_forma` (que existe justamente pra testar as
	/// formas TRANCADAS -- ver `AdminForcarForma`). Copiar estas dez linhas pro caminho do admin
	/// faria o admin testar um macaco que o jogo nao produz: sem `Liberar` a fera nao abriria o SSJ4,
	/// sem `OozaruAte` ela nunca cairia, sem `AplicarOozaru` nao haveria pacote pra zona.
	///
	/// E o mesmo desenho do `AplicarForma`/`AnunciarForma` da escada, e pelo mesmo motivo escrito la:
	/// "ligar a regra num chamador e esquecer do outro" e o erro que mais se repetiu neste port.
	/// ==========================================================================================
	/// </summary>
	private void VirarFera(ServerPlayer pl, FormaOozaru vai)
	{
		// O DM DESFAZ O SSJ ANTES (`Revert()` nos tres caminhos). Faz sentido: os dois ocupam o
		// mesmo slot de forma la (`slot = sFORM`), e o multiplicador do Dourado ja e absoluto.
		if (!pl.Forma.NaBase) Reverter(pl, "a besta toma o lugar da sua forma.");

		pl.Oozaru = vai;

		// ============================ A FERA ENTRA PELAS DUAS PORTAS ============================
		// A forma do Oozaru e paralela e nao entra em `EstadoDeForma.Atual` -- mas ela precisa
		// ficar LIBERADA, porque a entrada do SSJ4 no catalogo pede
		// `PedeFormaDespertada = "oozaru_dourado"`. Sem esta linha o campo de dado existiria e
		// nunca casaria com nada: exatamente o defeito recorrente deste port, escrever a regra e
		// esquecer de liga-la. (Liberar NAO basta pro SSJ4 -- ele tambem pede o Dourado a 100% --,
		// mas e a primeira das duas fechaduras.)
		//
		// O `EstreiaVista.Add` DEVOLVE se e a estreia daquele macaco, e e ele quem responde o
		// `primeira` do pacote. As duas chamadas juntas sao exatamente o que o
		// `EstadoDeForma.Entrar` faz pras formas da escada; o que o Oozaru nao pode usar do
		// `Entrar` e o `Atual =`, porque o slot da escada nao e dele.
		pl.Forma.Liberar(Oozaru.Id(vai));
		bool primeira = pl.Forma.EstreiaVista.Add(Catalogo.Rede(Oozaru.Id(vai)));

		pl.OozaruAte = NowMs() + (long)(Oozaru.Duracao(vai) * 1000);
		pl.RaivaDoOozaru = Oozaru.SegundosMeditandoAteCair;
		AplicarOozaru(pl, primeira);

		// QUANTO TEMPO ELE VAI DIRIGIR. Dito na hora da transformacao e nao so quando o controle
		// escapa: uma regra que so se anuncia quando ja te puniu e uma armadilha, nao uma regra.
		double prazo = Oozaru.SegundosDeControle(pl.MaestriaDaFera, vai);

		Avisar(pl, vai == FormaOozaru.Dourado
			? "voce olha pra lua e vira um MACACO GIGANTE DOURADO."
			: "voce olha pra lua e vira um macaco gigante.");
		Avisar(pl, double.IsPositiveInfinity(prazo)
			? "a fera e SUA: ela nao vai mais fugir do seu controle."
			: $"voce segura as redeas por cerca de {prazo:0} s -- depois ela e dela "
				+ $"(dominio {pl.MaestriaDaFera:0.#}% de 100%).");

		GD.Print($"[server] {pl.Name} virou {vai} (maestria {pl.MaestriaDaFera:0.00}, "
				 + $"controle {prazo:0.#}s)");
	}

	/// <summary>
	/// O QUE A FORMA FAZ NO CORPO -- `obj/buff/Oozaru/Buff()`.
	///
	/// Repare no que NAO esta aqui: `OozaruBuff` continua 1. Ele multiplicava kicapacity e
	/// powerupcap por 6 e enchia o tanque, e um rework anterior deste projeto tirou isso ("Oozaru
	/// NAO aumenta mais o ki"). O poder da forma vem SO do multiplicador de BP.
	///
	/// DEIXOU DE SER `static` porque agora ela ANUNCIA: contar pra zona exige `ZoneList`, que e do
	/// servidor. E o anuncio mora aqui, e nao no `Apeshit`, pelo mesmo motivo que o
	/// <see cref="AnunciarForma"/> mora no fim de `Transformar` -- funil unico. Quando a lua
	/// artificial e as ondas de Blutz chegarem, elas entram pelo `Apeshit` e o pacote sai sozinho.
	/// </summary>
	private void AplicarOozaru(ServerPlayer pl, bool primeira)
	{
		double m = Oozaru.Multiplicador(pl.Oozaru, pl.Class);

		// O DOURADO USA `ssjBuff` E ZERA O `giantFormbuff`, e isso nao e detalhe: sem zerar, o 1,5
		// herdado do regular multiplicaria o 18 e daria 27. O DM faz `giantFormbuff = 1` na linha
		// seguinte ao `ssjBuff = 18` exatamente por isso.
		if (pl.Oozaru == FormaOozaru.Dourado)
		{
			pl.Ficha.ssjBuff = m;
			pl.Ficha.giantFormbuff = 1;
		}
		else
		{
			pl.Ficha.giantFormbuff = m;
		}

		pl.Ficha.Tphysoff += Oozaru.SomaPhysoff;
		pl.Ficha.Tspeed += Oozaru.SomaSpeed;
		pl.Ficha.Ttechnique += Oozaru.SomaTecnica;

		// VIRAR MACACO INTERROMPE O QUE ESTAVA FAZENDO. `train=0; med=0` no DM -- e coerente:
		// nao se medita sem juizo.
		pl.Ficha.train = false;
		pl.Ficha.med = false;

		pl.Ficha.Statify();
		// O MACACO ESCREVE NO MESMO CAMPO DE MULTIPLICADOR QUE A ESCADA, entao ele tem o mesmo
		// atraso: sem esta linha o corpo vira Oozaru e continua BATENDO (e quebrando parede) com o
		// poder da forma base ate o proximo tique de ficha. Ver `RepercutirPoder`.
		RepercutirPoder(pl);
		pl.SigAtributos = "";

		AnunciarOozaru(pl, pl.Oozaru, primeira);
	}

	/// <summary>
	/// CONTA PRA ZONA que este corpo virou (ou deixou de ser) macaco. Mesmo molde do
	/// <see cref="AnunciarForma"/>: canal confiavel e ordenado, pra todo mundo do `ZoneHash`.
	///
	/// A ORDEM IMPORTA e e o canal confiavel que a garante. Ao entrar, o `Apeshit` derruba o SSJ
	/// antes (`Reverter` -> `S2C.Forma` pra base) e so depois este pacote sai; ao sair pro SSJ4, e
	/// o contrario. Fossem canais diferentes, o cliente poderia acender a aura do SSJ4 e receber o
	/// "deixei de ser macaco" depois -- que apagaria o que acabou de acender.
	/// </summary>
	private void AnunciarOozaru(ServerPlayer pl, FormaOozaru forma, bool primeira)
	{
		// ============================ O DEGRAU E DERIVADO AQUI TAMBEM ============================
		// Mesma escolha do <see cref="AnunciarForma"/>, e pelo mesmo motivo: o degrau nao se pede aos
		// chamadores. Quem responde e o Core -- `Cinematicas.Degrau` desvia a LINHA do macaco e devolve
		// `Estreia` em qualquer maestria (pedido do dono: "isso serve pra TODAS as formas do jogo,
		// menos as oozaru e golden oozaru"). Escrever `DegrauDeCena.Estreia` cravado aqui seria copiar
		// aquela regra pra um segundo lugar, e o `Oozaru LSSJ` que ainda vai entrar no catalogo ja
		// nasceria fora dela.
		//
		// `FormaOozaru.Nao` cai em def nulo -> `Nenhuma`, que e o certo: desfazer a fera nunca teve cena.
		DegrauDeCena degrau = Cinematicas.Degrau(Catalogo.Def(Oozaru.Id(forma)), primeira, 0);

		// E O PRAZO DA CENA TAMBEM, pelo mesmo motivo do <see cref="AnunciarForma"/>: a cena do macaco
		// prende o corpo como qualquer outra, e o que prende congela o Ki (ver `GameServer.Formas.EmCena`).
		// A ordem do `Apeshit` -- `Reverter` do SSJ ANTES desta chamada -- e o que faz isto funcionar sem
		// caso especial: o `AnunciarForma` da base marca zero e este marca o prazo do macaco por cima.
		// Desfazer a fera cai em def nulo -> zero, que e o certo: sair do Oozaru nunca teve cena.
		MarcarCena(pl, Catalogo.Def(Oozaru.Id(forma)), degrau);

		// Mesma escuta do <see cref="AnunciarForma"/>, e pelo mesmo motivo. Nula em jogo.
		EscutaDeFeras?.Add((pl.Id, forma, primeira, degrau));

		NetDataWriter w = PacoteDeOozaru(pl.Id, forma, primeira, degrau);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O PACOTE EM SI. Extraido pelo mesmo motivo do <see cref="PacoteDeForma"/>: alem do anuncio pra
	/// zona, ele sai unicast pra quem acaba de entrar (<see cref="SincronizarFormas"/>) -- e ali o
	/// degrau e <see cref="DegrauDeCena.Nenhuma"/>, porque a fera do outro ja nasceu antes de eu chegar.
	///
	/// ============================ POR QUE O DEGRAU VIAJA, JA QUE O MACACO E SEMPRE CENA CHEIA ============================
	/// Porque "sempre cena cheia" e verdade pra o ACONTECIMENTO e falso pra o ESTADO. Sem este byte o
	/// unico jeito de contar pro recem-chegado que fulano e um macaco de dez metros seria mandar o
	/// pacote de sempre -- e ele assistiria, preso, a uma transformacao que aconteceu antes de ele
	/// entrar na zona. Isso e pior que o defeito que a sincronia veio consertar.
	///
	/// O `primeira` CONTINUA ao lado dele porque sao coisas diferentes: `primeira` e o carimbo `** Nome **`
	/// no chat do dono, e a cena do macaco toca toda vez (ver `World.AoVirarOozaru`). A combinacao
	/// "primeira E sem cena" nunca e escrita: sincronia manda `primeira: false`.
	/// ==============================================================================================================
	/// </summary>
	private static NetDataWriter PacoteDeOozaru(int id, FormaOozaru forma, bool primeira, DegrauDeCena degrau)
	{
		var w = Protocol.Begin(Protocol.S2C.Oozaru);
		w.Put(id);
		w.Put((byte)forma);
		w.Put(primeira);
		w.Put((byte)degrau);
		return w;
	}

	/// <summary>
	/// SAIR DA FORMA -- `obj/buff/Oozaru/DeBuff()`.
	///
	/// As somas temporarias sao DESFEITAS pelo valor que foi aplicado, e nao recalculadas: e a
	/// mesma regra de todo buff deste projeto (guarde o que aplicou, nunca recalcule pra desfazer).
	/// Aqui as somas sao constantes, entao o desfazer e simetrico -- mas o `ssjBuff` NAO e: o DM
	/// tem um comentario dizendo que o Golden setava `ssjBuff` e ninguem o resetava, e a forma
	/// seguinte herdava 18x. Por isso ele volta pro multiplicador da ESCADA, e nao pra 1 cego.
	/// </summary>
	private void DesfazerOozaru(ServerPlayer pl, string motivo)
	{
		if (pl.Oozaru == FormaOozaru.Nao) return;
		FormaOozaru era = pl.Oozaru;
		double dominio = pl.MaestriaDaFera;   // LIDO ANTES: `Oozaru = Nao` zera o derivado

		pl.Oozaru = FormaOozaru.Nao;
		pl.OozaruAte = 0;

		// AS REDEAS VOLTAM PRA MAO DO DONO. Sem esta linha o corpo continuaria no
		// `TickDosCorposSemDono` -- um jogador de volta a forma base assistindo o proprio boneco
		// andar sozinho, e sem nenhum jeito de recuperar o controle a nao ser deslogar.
		DevolverAsRedeas(pl);
		pl.Ficha.giantFormbuff = 1;
		// PELO `MultiplicadorDaForma`, como o `AplicarForma`: e o segundo ponto do servidor que escreve
		// `ssjBuff`, e escrever a conta CRUA aqui apagaria o fator do cargo de quem saisse do macaco
		// (ver `MultiplicadorDaForma`). Hoje ninguem sai do Oozaru pra uma forma da destruicao -- e e
		// exatamente por isso que a divergencia passaria calada no dia em que alguem pudesse.
		pl.Ficha.ssjBuff = MultiplicadorDaForma(pl);

		pl.Ficha.Tphysoff -= Oozaru.SomaPhysoff;
		pl.Ficha.Tspeed -= Oozaru.SomaSpeed;
		pl.Ficha.Ttechnique -= Oozaru.SomaTecnica;

		pl.Ficha.Statify();
		RepercutirPoder(pl);   // a QUEDA tambem: sair da fera com o poder dela por 200 ms e o mesmo defeito ao contrario
		pl.SigAtributos = "";
		Avisar(pl, motivo);

		// O ANUNCIO VEM ANTES DO SSJ4, e nao na ultima linha do metodo. O bloco abaixo manda um
		// `S2C.Forma` proprio; se este saisse depois, o cliente acenderia a aura do SSJ4 e logo em
		// seguida receberia "deixei de ser macaco", que e o gancho onde a Camada 2 vai apagar a
		// aura -- e o jogador viraria SSJ4 apagado.
		AnunciarOozaru(pl, FormaOozaru.Nao, primeira: false);

		// ============================ O PRE-REQUISITO DO SSJ4 ============================
		// `ApeshitRevert`: sair do Dourado tendo o SSJ4 despertado NAO devolve a forma base -- vira
		// Super Saiyajin 4. E o unico caminho pra aquele degrau, que ja estava na escada esperando
		// alguem construir a porta.
		//
		// E A PRIMEIRA VEZ E POR MAESTRIA, que e o que o dono pediu: "ao chegar no 100% de
		// maestria, alem de controlar ele, o personagem vai se transformar no ssj4". Ver o
		// comentario de `Oozaru.ViraSsj4AoSair` -- ate aqui a regra pedia o SSJ4 pra dar o SSJ4.
		// ================================================================================
		if (Oozaru.ViraSsj4AoSair(era, pl.Race, pl.Forma.Despertou("ssj4"), dominio))
		{
			// ============================ LIBERAR SEM CONSUMIR A ESTREIA ============================
			// `Liberar` e nao `Entrar`, e e A razao pela qual os dois bits foram separados: `Entrar`
			// tambem marcaria a ESTREIA e a cinematica do SSJ4 nunca mais tocaria -- o jogador
			// perderia a cena mais cara da linha Saiyajin sem nada na tela dizendo que perdeu.
			//
			// O dono foi explicito nas duas metades: aqui "n tem cinematica, apenas o ozaru e
			// desfeito e o player ao inves de voltar pra forma base ele cai no estagio de ssj4"; e
			// depois "a cinematica q fizemos toca na primeira vez q ele se transformar em ssj4
			// apertando o C". Sao dois eventos diferentes e agora sao dois bits diferentes.
			//
			// O `Atual =` continua sendo escrita direta pelo mesmo motivo de antes: `Entrar` faria
			// as duas coisas de uma vez, e so uma delas e verdade neste ponto.
			// ==================================================================================
			if (pl.Forma.Liberar("ssj4"))
				Avisar(pl, "DOMINANDO A FERA VOCE ENTENDEU O QUE ELA E: o Super Saiyajin 4 esta ao "
						 + "seu alcance. Suba ate ele quando quiser.");

			pl.Forma.Atual = "ssj4";
			AplicarForma(pl);
			Avisar(pl, "voce tenta voltar ao normal e para como SUPER SAIYAJIN 4.");

			// ============================ A ZONA NAO SABIA DISTO ============================
			// `AplicarForma` so escreve na ficha -- quem conta pros outros clientes e o
			// `AnunciarForma`, e este caminho nunca o chamava. O corpo virava SSJ4 no servidor e
			// continuava com sprite, cabelo e aura de base em TODAS as telas, inclusive na do dono.
			// E o defeito que mais se repete neste port: ligar a regra num chamador e esquecer do
			// outro (aqui, o funil normal da escada anunciava e o do macaco nao).
			//
			// ============================ E ESTA E A UNICA `semCena: true` DO JOGO ============================
			// Antes bastava `primeira: false`, porque "nao e a estreia" e "nao ha cena" eram a mesma
			// coisa. Deixaram de ser: com os tres degraus, uma transformacao que nao e estreia AINDA
			// GANHA a cena encurtada enquanto a maestria nao passa dos 50% -- e aqui o dono pediu
			// nada ("n tem cinematica, apenas o ozaru e desfeito e o player cai no estagio de ssj4").
			//
			// O motivo de fundo e que isto NAO E UM GESTO DO JOGADOR: ele nao apertou nada, ele so
			// parou de ser macaco e o corpo pousou um degrau acima. Prender esse corpo por 2 s de cena
			// encurtada seria cobrar espera por uma transformacao que ele nem pediu -- e ele acabou de
			// sair de uma forma que ja tem cena propria.
			//
			// A estreia continua devendo (`Liberar` e nao `Entrar`, ver acima): a cena cheia do SSJ4
			// toca na primeira vez que ele SUBIR ate la apertando o C.
			// ==========================================================================================
			AnunciarForma(pl, Catalogo.IdBase, "ssj4", estreia: false, semCena: true);
			GD.Print($"[server] {pl.Name} saiu do Oozaru Dourado direto pro SSJ4");
		}
	}

	/// <summary>
	/// O TIQUE DA FORMA -- `obj/buff/Oozaru/Loop()`.
	///
	/// Quatro coisas do DM, e a ordem importa: o rabo derruba na hora, o prazo derruba sozinho,
	/// meditar derruba pela raiva, e a maestria sobe so enquanto se esta transformado. A quinta e
	/// nova e e do dono: o prazo de CONTROLE vencendo entrega o corpo ao servidor.
	/// </summary>
	private void TickDoOozaru(ServerPlayer pl, double dt)
	{
		if (pl.Oozaru == FormaOozaru.Nao) return;

		// 1. SEM RABO NAO HA OOZARU. `if(!container.Tail) DeBuff()` -- e a jogada classica contra
		//    Saiyajin, e ela funciona NO MEIO da forma, nao so na entrada.
		if (!TemRaboInteiro(pl)) { DesfazerOozaru(pl, "seu rabo se foi, e com ele a besta."); return; }
		if (pl.Ficha.dead) { DesfazerOozaru(pl, "o corpo cede."); return; }

		// 2. MEDITAR E O FREIO. `angertick = 1000` caindo de um em um enquanto `med` -- e a unica
		//    saida de quem nao tem pericia, e por isso ela precisa existir: sem ela, a paralisia
		//    seria punicao sem resposta.
		if (pl.Ficha.med)
		{
			pl.RaivaDoOozaru -= dt;
			if (pl.RaivaDoOozaru <= 0)
			{
				DesfazerOozaru(pl, "voce respira fundo e recobra a razao.");
				return;
			}
		}

		// 3. O PRAZO. `spawn(3000)` / `spawn(1000)`.
		if (NowMs() >= pl.OozaruAte)
		{
			DesfazerOozaru(pl, "a fera se cansa e voce volta a si.");
			return;
		}

		// 4. A MAESTRIA SOBE SO TRANSFORMADO, e so acordado (`if(!KO && ticker > 5)`). E o unico
		//    jeito de ganha-la -- e ela usa o MESMO livro e o MESMO `Subir` das formas da escada,
		//    inclusive o aviso de marco ("forma DOMINADA"), que aqui e o aviso mais importante do
		//    sistema: e a hora em que a fera para de ser um acidente.
		if (!pl.Ficha.KO
			&& pl.Forma.Maestria.Subir(Oozaru.Id(pl.Oozaru), Oozaru.MaestriaPorSegundo * dt, out string marco))
		{
			Avisar(pl, $"{Catalogo.Def(Oozaru.Id(pl.Oozaru))?.Nome}: {marco}.");
			pl.SigAtributos = "";   // a aba Formas mostra maestria: forca o proximo pacote a sair
			GD.Print($"[server] {pl.Name}: {Oozaru.Id(pl.Oozaru)} -> {marco}");
		}

		// 5. O PRAZO DE CONTROLE. E o `ctrlParalysis` do DM virado numa rampa -- ver
		//    `Oozaru.SegundosDeControle` pra curva e o porque dela.
		//
		//    A CONDICAO E `Cerebro == null`, e ela E o registro de "ainda nao perdi o controle":
		//    perder e de mao unica DENTRO de uma transformacao, e possuir o corpo ja e o estado que
		//    diz isso. Um campo `bool JaPerdeu` seria a mesma verdade escrita duas vezes -- e a
		//    segunda copia e sempre a que alguem esquece de limpar.
		//
		//    Repare que a maestria sobe ACIMA desta linha: o tempo em que a fera te carrega tambem
		//    ensina. Quem cruzar os 100% no meio da possessao nao recupera o corpo nesta noite (o
		//    `Cerebro` ja esta la), mas na proxima ele nao a perde mais.
		if (pl.Cerebro == null && SegundosNaForma(pl) >= Oozaru.SegundosDeControle(pl.MaestriaDaFera, pl.Oozaru))
			TomarAsRedeas(pl);
	}

	/// <summary>
	/// HA QUANTO TEMPO ESTE CORPO E FERA, em segundos. DERIVADO do prazo de fim -- nao ha campo de
	/// "quando comecou", porque `OozaruAte` e a duracao ja sao dois pontos da mesma reta.
	/// </summary>
	private double SegundosNaForma(ServerPlayer pl)
		=> Oozaru.Duracao(pl.Oozaru) - (pl.OozaruAte - NowMs()) / 1000.0;

	/// <summary>
	/// ============================ O SERVIDOR ASSUME A FERA ============================
	/// "oozaru nao masterizado vc n tem controle, ele sai batendo em qualquer coisa e atacando
	/// tudo". Isso e, letra por letra, o que o clone da Dimensao Mental ja faz -- e por isso aqui
	/// nao ha uma linha de IA: da um <see cref="Jandirus.Core.Ai.Cerebro"/> ao corpo e o
	/// <see cref="TickDosCorposSemDono"/> passa a dirigi-lo pelas MESMAS funcoes de movimento,
	/// guarda e golpe que o jogador usa.
	///
	/// O CEREBRO VEM TEMPERADO PRA FERA, e sao tres campos que ja existiam publicos:
	///
	///   * `VidaCautelosa = 0` -- ela nunca guarda e nunca recua. O cerebro do clone guarda e
	///     respira quando esta machucado porque ele e VOCE lutando; a fera nao se preserva, e essa
	///     e a diferenca entre um oponente e um desastre. (Tambem e o que a mantem punitiva: quem
	///     perdeu o controle esta num corpo de 1,5x de BP que apanha sem defender.)
	///   * `ChanceDePesado = 0.85` -- braco de macaco gigante nao da jab.
	///   * `IntervaloDeDecisao = 0.5` -- pesada e lenta pra mudar de ideia. Meio segundo e o que da
	///     ao alvo a chance de sair de perto, e o que evita que ela troque de vitima a cada quadro
	///     numa zona cheia.
	/// ==============================================================================
	/// </summary>
	private void TomarAsRedeas(ServerPlayer pl)
	{
		pl.Cerebro = new Jandirus.Core.Ai.Cerebro
		{
			VidaCautelosa = 0,
			ChanceDePesado = 0.85,
			IntervaloDeDecisao = 0.5,
		};

		// O CORPO PARA DE FAZER O QUE O DONO MANDOU antes de a fera assumir. As seis linhas que moravam
		// aqui viraram o <see cref="LargarOInput"/> (`GameServer.Clone.cs`) quando a furia lendaria
		// virou a segunda possessao do jogo -- o porque de cada uma esta escrito la.
		LargarOInput(pl);

		// ============================ A PUNICAO TEM QUE DIZER A SAIDA ============================
		// "VOCE PERDE O CONTROLE" sozinho e a metade ruim do aviso: ele conta o que aconteceu e nao o
		// que fazer. O corpo para de responder a TODAS as teclas de corpo (ver `ComandoDeCorpo`), e um
		// jogador que aperta tudo e nao ve nada acontecer conclui a mesma coisa que o dono concluiu
		// hoje sobre outra coisa -- que o jogo travou. A saida existe, esta desbloqueada de proposito e
		// e a UNICA: era invisivel.
		//
		// O PRAZO E LIDO, e nao escrito como constante: `RaivaDoOozaru` e o `angertick` do DM caindo
		// enquanto se medita, e quem ja meditou um pouco antes de perder as redeas tem menos a pagar.
		// Repetir `SegundosMeditandoAteCair` aqui seria mentir pra esse jogador -- e seria a mesma
		// verdade escrita em dois lugares, que e como uma delas fica velha.
		// ====================================================================================
		Avisar(pl, "VOCE PERDE O CONTROLE. A fera nao te ouve mais.");
		Avisar(pl, $"MEDITE (tecla M) e aguente cerca de {pl.RaivaDoOozaru:0} s: respirar fundo e o "
				 + "unico jeito de recobrar a razao antes da hora.");

		GD.Print($"[server] {pl.Name}: o servidor assumiu o Oozaru "
				 + $"(maestria {pl.MaestriaDaFera:0.0})");
	}

	// O `DevolverAsRedeas` MORAVA AQUI e foi pra `GameServer.Clone.cs`, junto do `SemAsRedeas` e do
	// `LargarOInput`: ele nunca teve nada de macaco dentro (so a guarda contra clone), e a furia
	// lendaria virou o segundo chamador.

	/// <summary>
	/// O VERB `Oozaru Revert`. Regular exige a fera DOMADA (100%); o Dourado nao volta por vontade
	/// -- ele vence o prazo ou vira SSJ4.
	/// </summary>
	private void ReverterOozaru(ServerPlayer pl)
	{
		if (pl.Oozaru == FormaOozaru.Nao) { Avisar(pl, "voce nao esta transformado."); return; }

		if (!Oozaru.PodeReverterSozinho(pl.Oozaru, pl.MaestriaDaFera))
		{
			Avisar(pl, pl.Oozaru == FormaOozaru.Dourado
				? "a besta dourada nao obedece: espere ela se cansar."
				: $"voce tenta voltar e nao consegue -- ainda falta dominio "
					+ $"({pl.MaestriaDaFera:0.#}% de 100%).");
			return;
		}
		DesfazerOozaru(pl, "voce domina a fera e volta ao normal.");
	}

	// O `ctrlParalysis` do DM -- "este corpo obedece o dono agora?" -- mora em
	// `GameServer.Clone.cs` (`SemAsRedeas`), junto do laco que dirige os corpos sem dono: a regra
	// nunca foi do macaco, e sim de qualquer corpo que o servidor esteja dirigindo.
}
