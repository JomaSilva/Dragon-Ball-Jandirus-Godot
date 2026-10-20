using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// ============================ O MOTOR DO FROST DEMON MUTANTE ============================
/// O `effector()` da arvore racial do original (`Skills/Skill Trees/Race Trees/icer.dm:29-88`), que
/// la roda a cada ciclo de stats. Tudo o que o Frost Demon NORMAL precisa ja e dado -- multiplicador,
/// corpo, porta de BP, cinematica -- e sai do catalogo sozinho; este arquivo existe pro **Mutante**,
/// que e a unica raca do jogo cujo poder pode SAIR do controle.
///
/// ============================ A REGRA, INTEIRA, EM QUATRO FRASES ============================
///   1. O Mutante nasce lacrado na 1a supressao (25% do proprio poder) com o BP de fabrica QUATRO
///      vezes maior por causa disso. Abrir a casca e o jogo dele.
///   2. Ele SEGURA pra sempre ate a forma que a maestria da base destrancou
///      (<see cref="FormasDeFrost.DegrauEstavel"/>: 0/25/50/75/100 -> 1a/2a/3a/4a/base). Acima
///      disso ele PODE entrar -- e ai um fusivel comeca a queimar.
///   3. Queimado o fusivel, o ki TRAVA (a tecla C morre, `CargaDeKi.Passo`) e a liberacao de poder
///      DESPENCA 1,2% por segundo ate o piso de 10% -- e o `powerlevel()` ja consome isso
///      (`Fighter.Power.cs`, `fd_release`).
///   4. Recuando pra uma forma estavel ele recupera 4% por segundo; e com a base 100% masterizada
///      as supressoes viram BATERIA, regenerando Ki por grau de casca fechada.
///
/// ============================ POR QUE ISTO NAO CABIA NO CATALOGO ============================
/// Porque nada disso e propriedade de uma FORMA -- e um estado que atravessa as sete. O catalogo
/// responde "quanto vale a 4a Forma"; este arquivo responde "por quanto tempo ainda". Sao perguntas
/// de dono diferente, e a prova e que a mesma forma responde as duas de jeitos opostos conforme a
/// maestria de quem esta dentro dela.
///
/// **O QUE ELE **NAO** FAZ, e por que**: nao escreve multiplicador (quem escreve e o `AplicarForma`,
/// pelo `Mult` da entrada), nao troca sprite (quem troca e o cliente, pelo `CorpoDeForma`) e nao
/// decide quem pode virar o que (quem decide e o `EstadoDeForma.Avaliar`). Ele mexe em exatamente
/// tres campos: <see cref="Jandirus.Core.Stats.Fighter.fd_release"/>,
/// <see cref="Jandirus.Core.Stats.Fighter.fd_ki_locked"/> e o Ki.
/// ========================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Quantos segundos o aviso de "o controle esta escapando" espera pra repetir.</summary>
	private const double FrostAvisoAntes = 10;

	/// <summary>E o de "o poder esta vazando", que e mais longo porque a situacao ja e clara.</summary>
	private const double FrostAvisoVazando = 15;

	/// <summary>
	/// EM QUE FRACAO DO FUSIVEL O AVISO SAI -- `world.time - fd_unstable_since >= limit * 0.6`
	/// (`icer.dm:79`). Nao e enfeite: e o unico sinal que da pro jogador escolher entre recuar e
	/// apostar, e sem ele a punicao chegaria do nada.
	/// </summary>
	private const double FrostAvisaEm = 0.6;

	/// <summary>
	/// QUANTO A MAESTRIA ALONGA O FUSIVEL: `(1 + fd_base_mastery / 50)` (`icer.dm:76`) -- 100% de
	/// maestria triplica o tempo antes de perder o controle. Escrito como divisor pra ficar igual ao
	/// original em vez de virar "x3 aos 100%", que e o resultado e nao a conta.
	/// </summary>
	private const double FrostMaestriaAlonga = 50;

	/// <summary>
	/// ESTE CORPO E UM FROST DEMON MUTANTE?
	///
	/// Pela CLASSE, que e onde o original poe (`fd_is_mutant()`: `Class == "Mutant Frost Demon"`) e
	/// onde o berco deste port ja sorteia. A raca entra junto porque "Mutant Frost Demon" e nome de
	/// classe de uma raca so -- e o dia em que nao for, a pergunta continua certa.
	/// </summary>
	private static bool EhMutanteDeFrost(ServerPlayer pl) =>
		FormasDeFrost.EhFrost(pl.Race)
		&& string.Equals(pl.Ficha.Class, FormasDeFrost.ClasseMutante, StringComparison.OrdinalIgnoreCase);

	/// <summary>
	/// A MAESTRIA DA FORMA BASE -- o `fd_base_mastery`.
	///
	/// Sai do livro de maestrias como qualquer outra, e nao de um campo novo: a linha inteira do
	/// Frost Demon compartilha UMA barra (ver `Catalogo.ChaveDaMaestria`), que e exatamente o que o
	/// original tem. De quebra ela ja persiste, ja vai pro save e ja aparece na aba de formas sem
	/// ninguem ligar nada.
	/// </summary>
	private static double MaestriaDaBaseDeFrost(ServerPlayer pl) =>
		pl.Forma.Maestria.De(Catalogo.IdDaBaseDoFrost);

	/// <summary>
	/// O TIQUE. Roda junto do <see cref="TickDaForma"/> e no MESMO tique cheio, porque as duas coisas
	/// que ele mexe -- o Ki e o poder expresso -- sao as mesmas que a forma cobra.
	///
	/// ============================ EM CENA ELE PARA, COMO TODOS OS OUTROS RELOGIOS ============================
	/// A cinematica da 2a Evolucao prende o corpo por 28 s, e o fusivel dela e de 22,5 s (90 x 0,25).
	/// Sem esta guarda, o Mutante PERDERIA o controle no meio da propria estreia -- e sairia da cena
	/// com o ki travado e o poder vazando, sem nunca ter tido um instante pra agir. E a mesma regra
	/// que ja para o dreno da forma, a carga e o custo do voo (ver `GameServer.Formas.EmCena`).
	/// ====================================================================================================
	/// </summary>
	private void TickDoFrost(ServerPlayer pl, double dt)
	{
		if (dt <= 0 || !EhMutanteDeFrost(pl)) return;

		if (pl.FrostAvisoEmSegundos > 0) pl.FrostAvisoEmSegundos = Math.Max(0, pl.FrostAvisoEmSegundos - dt);

		// (2026-10-19) OS QUATRO MARCOS DA BASE, ANTES da guarda da cena: a barra so sobe fora dela (o `TickDaForma` para em
		// cena), entao aqui so se le o que o tique de forma ja escreveu -- e este roda logo depois dele.
		AvisarMarcosDaBase(pl);
		if (EmCena(pl)) return;

		int forma = Catalogo.DegrauDoFrost(pl.Forma.Def);

		// FORA DA ESCADA DELE (base, ou um estado que nem devia acontecer): nada a governar. Nao
		// "conserta" o estado nem reverte ninguem -- quem cuida de onde o corpo repousa e o
		// `Catalogo.PisoDaEscada`, no login e no recuo.
		if (forma == 0) return;

		double maestria = MaestriaDaBaseDeFrost(pl);
		int estavel = FormasDeFrost.DegrauEstavel(maestria);

		if (forma <= estavel) EstavelNoFrost(pl, dt, forma, maestria);
		else InstavelNoFrost(pl, dt, forma, maestria);
	}

	/// <summary>
	/// FORMA QUE ELE SEGURA: o relogio zera, a liberacao volta e -- com a base dominada -- a casca
	/// fechada vira bateria.
	/// </summary>
	private void EstavelNoFrost(ServerPlayer pl, double dt, int forma, double maestria)
	{
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		pl.FrostInstavelSegundos = 0;

		if (f.fd_release < 1)
			f.fd_release = Math.Min(1, f.fd_release + FormasDeFrost.RecuperacaoPorSegundoPct / 100 * dt);

		// O DESTRAVAMENTO SO VEM COM A LIBERACAO CHEIA, e nao no instante em que ele recua: recuperar
		// custa segundos (4%/s, ou seja ate 22,5 s se ele foi ate o piso), e e esse tempo que faz
		// perder o controle DOER. Destravar a tecla C na hora transformaria a punicao num susto.
		if (f.fd_ki_locked && f.fd_release >= 1)
		{
			f.fd_ki_locked = false;
			Avisar(pl, "seu ki se acalma -- você recuperou o controle do seu poder.");
			// (2026-10-19) E A AURA VERMELHA SAI do corpo, na tela de todo mundo (`removeOverlay`, `icer.dm:50`).
			AnunciarDescontrole(pl);
		}

		// ============================ A BATERIA -- `icer.dm:60-62` ============================
		// Com a base 100% masterizada, a supressao deixa de ser so prejuizo: cada GRAU de casca
		// fechada regenera 0,2% do Ki maximo por segundo, entao a 1a Forma (quatro graus abaixo da
		// base) enche 0,8%/s. E o que transforma o fundo do poco num LUGAR -- o Mutante que dominou
		// o proprio corpo se recolhe pra recarregar e volta a abrir.
		//
		// `MaxKi` E O TETO, e nao o teto de carga: isto e regeneracao passiva, nao power-up. Quem
		// passa dos 100% e a tecla C, e ela cobra folego.
		if (maestria >= 100 && forma < FormasDeFrost.Base && !CaidoPelaMorte(pl) && f.Ki < f.MaxKi)
			f.Ki = Math.Min(f.MaxKi,
				f.Ki + f.MaxKi * (FormasDeFrost.RegeneracaoPorGrauPct / 100)
					 * (FormasDeFrost.Base - forma) * dt);
	}

	/// <summary>
	/// FORMA ACIMA DO QUE ELE SEGURA: o fusivel queima, o ki trava, e o poder comeca a escapar.
	/// </summary>
	private void InstavelNoFrost(ServerPlayer pl, double dt, int forma, double maestria)
	{
		Jandirus.Core.Stats.Fighter f = pl.Ficha;

		// ============================ NOCAUTEADO O FUSIVEL CONTINUA ANDANDO (2026-10-19) ============================
		// O dono: *"nocaute deveria manter a forma atual"* -- e o corpo caido continua na forma que nao segura (ver
		// `FrostAtravessaONocaute`). No DM o relogio dela e o do MUNDO (`world.time - fd_unstable_since`, no `effector` do
		// `icer.dm`): ele nao para no chao, e o ki pode travar e vazar com o Mutante nocauteado. Esta guarda parava o fusivel no
		// nocaute porque o nocaute desfazia a forma no tique seguinte, e ela so cobria essa janela. Quem para o fusivel agora e a
		// morte: o cadaver ja perde a forma.
		if ((f.KO && FusivelParaNoNocauteDeTeste) || CaidoPelaMorte(pl)) return;

		pl.FrostInstavelSegundos += dt;

		double limite = FormasDeFrost.SegundosAtePerderOControle
					  * FormasDeFrost.FatorDoFusivel(forma)
					  * (1 + maestria / FrostMaestriaAlonga);

		if (!f.fd_ki_locked)
		{
			if (pl.FrostInstavelSegundos >= limite)
			{
				f.fd_ki_locked = true;
				pl.FrostAvisoEmSegundos = FrostAvisoVazando;
				Avisar(pl, "SEU KI SAIU DO CONTROLE! Você não consegue mais reunir energia -- e seu "
						 + "poder vai começar a VAZAR. Recue para uma forma de supressão estável!");
				// (2026-10-19) E A AURA VERMELHA ACENDE no corpo, na tela de todo mundo (`updateOverlay(fd_menacing_red)` +
				// `chargeaura.wav`, `icer.dm:62-63` -- o som o cliente toca na virada do bit).
				AnunciarDescontrole(pl);
				// QUEM ESTAVA SEGURANDO C PARA DE REUNIR: o `Energy_Draw` do DM sai na primeira linha a partir daqui, e a carga aberta
				// antes ficaria com a aura e o zumbido acesos sem o Ki subir.
				if (pl.Carregando && !CargaDoDescontroleAcendeDeTeste) PararCarga(pl);
			}
			else if (pl.FrostInstavelSegundos >= limite * FrostAvisaEm && pl.FrostAvisoEmSegundos <= 0)
			{
				pl.FrostAvisoEmSegundos = FrostAvisoAntes;
				Avisar(pl, "seu ki treme -- o controle está escapando...");
			}
			return;
		}

		// ============================ O VAZAMENTO -- E ELE NAO E UM DESCONTO NO FIM ============================
		// `fd_release` e campo do `Fighter` e o `powerlevel()` o consome na FAMILIA 3 do funil
		// (`Fighter.Power.cs`, "aplicam no fim"), exatamente como o `base.dm:144` do original. Escrever
		// aqui e so mover o numero; quem o aplica e a conta de poder, uma vez, pra todo mundo que le
		// `expressedBP` -- o dano do soco, a guarda, o arremesso, a quebra de cenario e o scouter.
		//
		// Foi essa a licao que o Ultra Ego ja deixou anotada neste projeto ("reducao de dano tem que
		// ser campo do Core e nao desconto depois"): um `expressedBP *= 0,1` escrito num chamador
		// pegaria o soco e deixaria o arremesso passar.
		// ==================================================================================================
		if (f.fd_release <= FormasDeFrost.PisoDaLiberacao) return;

		f.fd_release = Math.Max(FormasDeFrost.PisoDaLiberacao,
			f.fd_release - FormasDeFrost.VazamentoPorSegundoPct / 100 * dt);

		if (pl.FrostAvisoEmSegundos <= 0)
		{
			pl.FrostAvisoEmSegundos = FrostAvisoVazando;
			Avisar(pl, $"seu poder está VAZANDO! (liberação: {f.fd_release * 100:0}%)");
		}
	}

	// =====================================================================
	// (2026-10-19) O QUE O DM FAZ EM VOLTA DO MOTOR E O PORT NAO FAZIA
	// =====================================================================
	// O dono: *"ta todo bugado e sem as cinematicas q deveriam ter assim como era no Finale-master"* e *"tinha tb efeitos de ki e
	// release quando um frost demon ficava na forma base dele nao masterizada, mas no codigo DM explica tudo isso, so ler la"*.
	// Lido la: o motor de cima (fusivel, travamento, vazamento, recuperacao, bateria) ja estava aqui; faltava o que o jogador VE e
	// ouve dele -- a cena e o aviso de quem sobe pra uma forma que nao segura (`Frost_Demon_Forms`), o relogio que rearma a cada
	// troca de forma, os quatro avisos de maestria (`fd_mastery_msg`), a aura vermelha do descontrole (`fd_menacing_red`), o aviso
	// de quem tenta reunir energia com o ki travado (`Energy_Draw`), a maestria e a liberacao no painel (`HtmlUI.dm:195-200`), a
	// taxa propria da maestria do Mutante (`FD_MASTERY_PER_MIN`), o Frost que nao cai por falta de Ki nem por nocaute (o dono:
	// *"nocaute deveria manter a forma atual"*) e o menu de re-escolher os corpos (`Icer_Form_Settings`). E, na criacao, a classe
	// sorteada antes da tela de formas (`statfrost`).
	// =====================================================================

	/// <summary>
	/// O MUTANTE ESTA SUBINDO PRA UMA FORMA QUE NAO SEGURA? -- o `if(fd_is_mutant() && next > fd_stable_gate())` do
	/// `Frost_Demon_Forms` (`IcerTransform.dm:97`). So na SUBIDA: o `revertIcer()` recolhe a casca sem cena e sem aviso
	/// (`IcerTransform.dm:116-128`). E o bit que vira a cena vermelha (`Cinematicas.NoDegrau`) e o aviso do <see cref="AoMudarFormaDoFrost"/>.
	/// </summary>
	private static bool SobeProDescontrole(ServerPlayer pl, string de, string para)
	{
		if (!EhMutanteDeFrost(pl)) return false;
		int alvo = Catalogo.DegrauDoFrost(Catalogo.Def(para));
		return alvo > Catalogo.DegrauDoFrost(Catalogo.Def(de))
			&& alvo > FormasDeFrost.DegrauEstavel(MaestriaDaBaseDeFrost(pl));
	}

	/// <summary>
	/// A TROCA DE FORMA DO MUTANTE, no funil `AnunciarForma` -- as duas coisas que o `Frost_Demon_Forms` e o `revertIcer` fazem alem
	/// de trocar o corpo:
	///   * REARMAM O RELOGIO da forma instavel (`fd_unstable_since = 0`, `IcerTransform.dm:110` e `:125`): cada forma tem o PROPRIO
	///     fusivel, contado de quando se entrou nela. Sem isto, quem passasse 60 s na forma base e subisse pra 1a Evolucao (fusivel
	///     de 45 s) travaria o ki no mesmo tique -- o relogio da base atravessava a troca;
	///   * AVISAM quem sobe pra uma forma que nao segura (`:107`) -- com a cena ou sem ela: a partir de 50% de maestria a
	///     transformacao e instantanea, e o aviso continua sendo verdade.
	/// </summary>
	private void AoMudarFormaDoFrost(ServerPlayer pl, string de, string para, bool instavel)
	{
		if (de == para || !EhMutanteDeFrost(pl)) return;
		if (!RelogioAtravessaATrocaDeTeste) pl.FrostInstavelSegundos = 0;
		if (instavel && !Cinematicas.DescontroleSemCenaDeTeste)
			Avisar(pl, $"este poder é demais para segurar por muito tempo! (maestria da base: {MaestriaDaBaseDeFrost(pl):0}%)");
	}

	/// <summary>DEFEITO INJETADO (`--frostteste`): o relogio da forma instavel atravessa a troca de forma -- o jogo de antes.</summary>
	internal static bool RelogioAtravessaATrocaDeTeste;

	/// <summary>
	/// AS MARCAS DO MUTANTE NO `S2C.Forma` (ver `Protocol.MarcaInstavel`): a forma em que ele entrou nao se segura (a cena e a
	/// vermelha) e o ki dele esta descontrolado (a aura vermelha fica colada no corpo). Zero pra todo o resto. A raca vem ANTES da
	/// ficha: o corpo forjado da `--diagforma` (`MedirAlcanceDoAnuncio`) anuncia forma sem ficha nenhuma.
	/// </summary>
	private static byte MarcasDoFrost(ServerPlayer pl, bool instavel) =>
		(byte)((instavel ? Jandirus.Net.Protocol.MarcaInstavel : 0)
			 | (EhMutanteDeFrost(pl) && pl.Ficha.fd_ki_locked && !AuraDoDescontroleMudaDeTeste
				? Jandirus.Net.Protocol.MarcaDescontrolado : 0));

	/// <summary>
	/// DEFEITO INJETADO (`--frostteste`, `--diagmutante`): o descontrole nao vai pro fio -- o corpo travado continua sem a aura
	/// vermelha, o jogo de antes. Lido pela marca do pacote (aqui) e pelo anuncio da virada (<see cref="AnunciarDescontrole"/>).
	/// </summary>
	internal static bool AuraDoDescontroleMudaDeTeste;

	/// <summary>
	/// O KI TRAVOU OU DESTRAVOU: a zona inteira precisa ver a aura vermelha acender ou apagar. Sai no formato do pacote de ESTADO da
	/// forma ("ele ESTA em", `de == para`: sem cena e sem estalo) -- o mesmo que quem chega na zona recebe pelo `PacotesDeEstado`, e
	/// por isso o recem-chegado ve a aura de quem ja estava descontrolado sem uma linha a mais. A diferenca e uma so: este leva a
	/// `MarcaViradaDoDescontrole`, e e ela que toca o `chargeaura` no cliente.
	/// </summary>
	private void AnunciarDescontrole(ServerPlayer pl)
	{
		if (pl.Forma.NaBase || AuraDoDescontroleMudaDeTeste) return;   // o Mutante nunca descansa na base -- ver `Catalogo.PisoDaEscada`
		NetDataWriter w = PacoteDeForma(pl.Id, pl.Forma.Atual, pl.Forma.Atual, DegrauDeCena.Nenhuma, Dominou(pl, pl.Forma.Atual),
			(byte)(MarcasDoFrost(pl, instavel: false) | Jandirus.Net.Protocol.MarcaViradaDoDescontrole));
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			o.Peer?.Send(w, Jandirus.Net.Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		EscutaDoDescontrole?.Add((pl.Id, pl.Ficha.fd_ki_locked));
	}

	/// <summary>
	/// A BANCADA ESCUTA O ANUNCIO DO DESCONTROLE: quem, e se ele acendeu ou apagou. Nula em jogo. Ver `GameServer.FrostTeste.cs`.
	/// </summary>
	internal List<(int Quem, bool Aceso)>? EscutaDoDescontrole;

	/// <summary>
	/// A TAXA DE MAESTRIA DA FORMA EM QUE ESTE CORPO ESTA, por segundo -- a do `Catalogo` (as ~3 h da escada) pra todo mundo, menos o
	/// MUTANTE na escada dele, que treina a base a `FD_MASTERY_PER_MIN` (`FormasDeFrost.MaestriaDaBasePorMinuto`, ~50 min).
	/// </summary>
	private static double TaxaDeMaestriaDaForma(ServerPlayer pl) =>
		EhMutanteDeFrost(pl) && Catalogo.DegrauDoFrost(pl.Forma.Def) > 0 && !MutanteNaTaxaDaEscadaDeTeste
			? FormasDeFrost.MaestriaDaBasePorMinuto / 60.0
			: Catalogo.MaestriaPorSegundo;

	/// <summary>DEFEITO INJETADO (`--frostteste`): o Mutante volta a treinar a base na taxa da escada (~3 h) -- o jogo de antes.</summary>
	internal static bool MutanteNaTaxaDaEscadaDeTeste;

	/// <summary>
	/// A ESCADA DO FROST DEMON NAO CAI POR FALTA DE KI. No DM as formas dele nao mexem no Ki (`IcerTransform.dm:12-13`, *"Formas NAO
	/// mexem mais no pool de Ki"*) e so a tecla X as recolhe (`revertIcer()`, que so o `Power_Revert` chama, `Power Control.dm:66`).
	/// Aqui o `TickDaForma` derrubava QUALQUER forma com o Ki em zero -- e o Mutante que gastasse o Ki todo numa briga voltava da
	/// forma base (1x) pra 1a Forma (0,25x) no meio dela, sem ter apertado nada. (2026-10-19) E o nocaute tambem nao -- ver o
	/// <see cref="FrostAtravessaONocaute"/>.
	/// </summary>
	private static bool FrostNaoCaiPorKi(ServerPlayer pl) =>
		Catalogo.DegrauDoFrost(pl.Forma.Def) > 0 && !FrostCaiPorKiDeTeste;

	/// <summary>DEFEITO INJETADO (`--frostteste`): a escada do Frost Demon volta a cair com o Ki em zero -- o jogo de antes.</summary>
	internal static bool FrostCaiPorKiDeTeste;

	/// <summary>
	/// (2026-10-19) O NOCAUTE NAO DESFAZ A ESCADA DO FROST DEMON. O dono: *"nocaute deveria manter a forma atual"*. No DM o `fd_form` so
	/// muda pela subida (`Frost_Demon_Forms`) e pelo X (`revertIcer`) -- o nocaute nao toca nele. Aqui o `TickDaForma` derrubava a forma de
	/// todo corpo que caia, e o Mutante nocauteado na forma base acordava na 1a Forma, com um quarto do poder. A MORTE continua desfazendo:
	/// o cadaver nao segura forma nenhuma (e o Mutante cai na 1a supressao, onde o corpo descansa). O normal fica tambem -- a escada e uma.
	/// </summary>
	private static bool FrostAtravessaONocaute(ServerPlayer pl) =>
		!NocauteDesfazOFrostDeTeste && Catalogo.DegrauDoFrost(pl.Forma.Def) > 0 && !EhCadaver(pl);

	/// <summary>DEFEITO INJETADO (`--frostteste`): o nocaute volta a desfazer a escada do Frost Demon -- o jogo de antes.</summary>
	internal static bool NocauteDesfazOFrostDeTeste;

	/// <summary>
	/// (2026-10-19) ...E A FORMA QUE FICA NAO TREINA NO CHAO: a maestria da base so cresce com o corpo de pe (`!S.KO && !S.dead`, no
	/// `effector` do `icer.dm`). Antes nao precisava -- o nocaute desfazia a forma --, e sem esta guarda o Mutante nocauteado na forma base
	/// treinaria deitado.
	/// </summary>
	private static bool FrostNaoTreinaCaido(ServerPlayer pl) =>
		pl.Ficha.KO && Catalogo.DegrauDoFrost(pl.Forma.Def) > 0 && !NocauteadoTreinaAFormaDeTeste;

	/// <summary>DEFEITO INJETADO (`--frostteste`): a escada do Frost Demon treina com o corpo nocauteado.</summary>
	internal static bool NocauteadoTreinaAFormaDeTeste;

	/// <summary>DEFEITO INJETADO (`--frostteste`): o fusivel da forma instavel volta a parar com o corpo nocauteado -- o jogo de antes.</summary>
	internal static bool FusivelParaNoNocauteDeTeste;

	/// <summary>
	/// OS QUATRO MARCOS DA MAESTRIA DA BASE -- o `fd_mastery_msg()` (`IcerTransform.dm:261-266`), chamado quando a barra cruza 25, 50,
	/// 75 e 100 (`icer.dm:42-43`). Cada um destranca uma casca pra sempre, e e o aviso que diz ao jogador que ja pode ficar nela sem o
	/// ki fugir. Os numeros sao os do <see cref="FormasDeFrost.MarcosDaBase"/>, que foi declarado pra isto e estava sem leitor.
	///
	/// A BARRA E LIDA, E NAO SUBIDA AQUI: quem a sobe e o `TickDaForma`, que roda antes deste, e a barra ja vista fica no corpo
	/// (`ServerPlayer.FrostMaestriaVista`). A primeira leitura so guarda -- quem loga com 60% nao ouve os marcos de 25 e 50 de novo.
	/// </summary>
	private void AvisarMarcosDaBase(ServerPlayer pl)
	{
		double agora = MaestriaDaBaseDeFrost(pl);
		double antes = pl.FrostMaestriaVista;
		pl.FrostMaestriaVista = agora;
		if (antes < 0 || MarcosDaBaseMudosDeTeste) return;

		foreach (double marco in FormasDeFrost.MarcosDaBase)
		{
			if (antes >= marco || agora < marco) continue;
			int degrau = FormasDeFrost.DegrauEstavel(marco);
			Avisar(pl, degrau == FormasDeFrost.Base
				? $"maestria da base {marco:0}%: a FORMA BASE é sua! Você pode ficar nela pra sempre -- e as formas de supressão "
				  + "agora regeneram Ki passivamente (quanto mais suprimida, mais regenera)."
				: $"maestria da base {marco:0}%: a {FormasDeFrost.Nome(degrau)} agora é estável pra sempre.");
		}
	}

	/// <summary>DEFEITO INJETADO (`--frostteste`): os quatro marcos ficam mudos -- o jogo de antes.</summary>
	internal static bool MarcosDaBaseMudosDeTeste;

	/// <summary>
	/// O QUE O PAINEL DO MUTANTE MOSTRA -- as duas linhas do `HtmlUI.dm:197-200`: a maestria da base com a casca que ela ja segura, e
	/// a liberacao quando ela nao esta cheia (com o "KI DESCONTROLADO" quando ele travou). Vai junto das linhas raciais da ficha
	/// (`SheetState.Raciais`), que e por onde o HUD ja recebe o que so o servidor sabe. Vazio pra quem nao e Mutante.
	/// </summary>
	private static string LinhasDoFrost(ServerPlayer pl)
	{
		if (!EhMutanteDeFrost(pl) || PainelDoMutanteMudoDeTeste) return "";
		double maestria = MaestriaDaBaseDeFrost(pl);
		string linhas = $"MAESTRIA DA BASE · {maestria:0}% · estável até a {FormasDeFrost.Nome(FormasDeFrost.DegrauEstavel(maestria))}";
		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		if (f.fd_ki_locked)
			linhas += $"\nKI DESCONTROLADO · liberação {f.fd_release * 100:0}% · recue para uma forma estável";
		else if (f.fd_release < 1)
			linhas += $"\nLIBERAÇÃO · {f.fd_release * 100:0}% · o controle está voltando";
		return linhas;
	}

	/// <summary>As linhas raciais da ficha com as do Mutante embaixo -- a linha que o `TickFichas` monta.</summary>
	private static string ComAsLinhasDoFrost(ServerPlayer pl, string raciais)
	{
		string frost = LinhasDoFrost(pl);
		return frost.Length == 0 ? raciais : raciais.Length == 0 ? frost : raciais + "\n" + frost;
	}

	/// <summary>DEFEITO INJETADO (`--frostteste`): o painel do Mutante some -- o jogo de antes.</summary>
	internal static bool PainelDoMutanteMudoDeTeste;

	/// <summary>
	/// QUEM TENTA REUNIR ENERGIA COM O KI DESCONTROLADO -- o `Energy_Draw()` do DM sai na primeira linha e avisa (`Power
	/// Control.dm:141-145`), no mesmo `fd_warn_cd` dos outros avisos do motor. O `Carregar` pergunta isto e nao liga a carga: sem a
	/// guarda la, o corpo acendia a aura e o zumbido da carga e o Ki nao subia (o `CargaDeKi.Passo` ja devolvia "nada") -- uma tecla
	/// que parecia funcionar e nao funcionava.
	/// </summary>
	private bool RecusaACargaDoDescontrole(ServerPlayer pl)
	{
		if (!pl.Ficha.fd_ki_locked || CargaDoDescontroleAcendeDeTeste) return false;
		if (pl.FrostAvisoEmSegundos <= 0)
		{
			pl.FrostAvisoEmSegundos = FrostAvisoDaCarga;
			Avisar(pl, "seu ki está DESCONTROLADO -- você não consegue reunir energia! Recue para uma forma de supressão estável.");
		}
		return true;
	}

	/// <summary>`fd_warn_cd = world.time + 60` (`Power Control.dm:143`): o aviso da carga travada repete a cada 6 s.</summary>
	private const double FrostAvisoDaCarga = 6;

	/// <summary>
	/// DEFEITO INJETADO (`--frostteste`): a tecla C volta a acender a carga com o ki travado, e a carga que ja estava acesa atravessa o
	/// travamento -- o jogo de antes. Lido pelas duas linhas: a guarda do `Carregar` e o `PararCarga` do <see cref="InstavelNoFrost"/>.
	/// </summary>
	internal static bool CargaDoDescontroleAcendeDeTeste;

	// =====================================================================
	// (2026-10-19) A CLASSE DO FROST DEMON SAI ANTES DA TELA DE FORMAS -- o `statfrost()` do DM
	// =====================================================================

	/// <summary>
	/// A CLASSE QUE UM FROST DEMON NASCIDO NESTA VAGA TERIA -- sorteada UMA vez pelo `Class_Spread` do `races.json` (o
	/// `rand(1,100) <= 1` do `staticer.dm:5`) e guardada na conta (`AccountSave.ClassesDoFrost`).
	///
	/// ============================ POR QUE O SORTEIO SAIU DO NASCIMENTO ============================
	/// O dono: *"frost demon mutante deveria ter como escolher todas as transformaçoes na criaçao de personagem assim como era nesse
	/// projeto"*. No DM a classe do Frost Demon e sorteada NA ESCOLHA DA RACA e dita na cara (*"You were born a Mutant Frost Demon."*,
	/// `staticer.dm:7`), e so DEPOIS a tela de formas abre -- com tres slots pro normal e SETE pro Mutante (`body_custom.dm:197-203`).
	/// Aqui a classe so saia no `Nascer`, depois da tela: o Mutante escolhia os tres corpos do normal e ganhava as quatro supressoes no
	/// padrao.
	///
	/// GRAVADA NA CONTA, e nao sorteada a cada pergunta: sem isso bastaria reabrir a criacao (ou reiniciar o servidor do "Hospedar")
	/// ate sair Mutante. Gasta quando um Frost Demon nasce na vaga -- apagar o personagem e criar outro sorteia de novo, como sempre
	/// sorteou.
	/// ==============================================================================================
	/// </summary>
	private string ClasseDoFrostDaVaga(AccountSave acc, int vaga)
	{
		if (acc.ClassesDoFrost == null || acc.ClassesDoFrost.Length < AccountStore.Slots)
			Array.Resize(ref acc.ClassesDoFrost, AccountStore.Slots);
		if (acc.ClassesDoFrost[vaga] is { Length: > 0 } ja) return ja;

		string sorteada = _racas == null
			? FormasDeFrost.ClasseNormal
			: Birth.RollClass(_racas, FormasDeFrost.Raca, "", _rng);
		acc.ClassesDoFrost[vaga] = sorteada;
		_store?.Gravar(acc);
		return sorteada;
	}

	/// <summary>
	/// A CLASSE QUE A CRIACAO MOSTRA PRA ESTA VAGA: a da vaga -- ou a do Mutante, quando o admin garantiu a classe mais rara da raca pro
	/// proximo personagem (`AccountSave.ProximaClasseRara`; o `GastarAClasseGarantida` a gasta no nascimento, e pro Icer ela E o
	/// Mutante). Vai na lista de vagas (`SlotInfo.ClasseDoFrost`); a tela so a le se a raca escolhida for o Frost Demon.
	/// </summary>
	private string ClasseDoFrostNaCriacao(AccountSave acc, int vaga) =>
		acc.ProximaClasseRara && !ClasseRaraIgnoradaDeTeste && _racas != null
		&& Birth.ClasseMaisRara(_racas, FormasDeFrost.Raca, "", _rng) is { Length: > 0 } rara
			? rara
			: ClasseDoFrostDaVaga(acc, vaga);

	/// <summary>
	/// O NASCIMENTO GASTA A CLASSE DA VAGA: devolve a que a criacao mostrou (vazio pra quem nao e Frost Demon) e esvazia a vaga, pra o
	/// proximo Frost Demon dela sortear de novo. Quem chama e o `CreateChar`, depois das recusas -- daqui pra baixo o personagem nasce.
	/// A garantia do admin passa na frente (o chamador a pergunta primeiro), e o sorteio da vaga e gasto do mesmo jeito: um Frost
	/// Demon nasceu ali. Quem grava a conta e o `CreateChar`, logo depois.
	/// </summary>
	private string GastarAClasseDoFrost(AccountSave acc, int vaga, Jandirus.Core.Races.CharacterDraft ficha)
	{
		if (!FormasDeFrost.EhFrost(ficha.Race) || ClasseDoFrostSorteadaDeNovoDeTeste) return "";
		string classe = ClasseDoFrostDaVaga(acc, vaga);
		acc.ClassesDoFrost[vaga] = null;
		return classe;
	}

	/// <summary>
	/// DEFEITO INJETADO (`--frostteste`): o nascimento ignora a classe que a criacao mostrou e sorteia de novo -- o jogo de antes (a
	/// tela de sete corpos poderia nascer normal, e a de tres, Mutante).
	/// </summary>
	internal static bool ClasseDoFrostSorteadaDeNovoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--frostteste`): o login nao desentorta a lista do Mutante criado antes do conserto -- o jogo de antes. Lido
	/// pela linha da migracao no `AccountStore.ParaJogador`. Falso em jogo, sempre.
	/// </summary>
	internal static bool ListaTortaFicaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (`--diagmutante`): o personagem novo nasce sem a versao das listas de corpos (`CharacterSave.CorposVersao`) -- e
	/// o Mutante que escolheu as tres primeiras cascas e deixou o resto no padrao tem a lista legitima "desentortada" pelo login, como
	/// se fosse a torta de antes do conserto. Lido pela linha do `CreateChar`. Falso em jogo, sempre.
	/// </summary>
	internal static bool NasceSemAVersaoDosCorposDeTeste;

	// =====================================================================
	// (2026-10-19) RE-ESCOLHER OS CORPOS EM JOGO -- o `Icer_Form_Settings` do DM
	// =====================================================================

	/// <summary>
	/// "RE-ESCOLHER CORPOS DAS FORMAS" -- o `Icer_Form_Settings` (`icer.dm:77-83`, aba Other), que reabre a tela de formas da criacao
	/// com os slots da classe e devolve o corpo a forma inicial. O verb `formas_frost`, com os corpos em INDICES do
	/// `FormasDeFrost.Catalogo` separados por `|`: o argumento de verb tem 256 letras (`Protocol.MaxArgDeVerbo`), sete nomes inteiros
	/// quase o enchem, e o indice e o mesmo Core nas duas pontas.
	///
	/// E A SAIDA DE QUEM JA NASCEU COM OS CORPOS ERRADOS: o Mutante criado antes desta leva ganhou os tres corpos do normal nas
	/// supressoes (ver `FormasDeFrost.Sanear`) e nao havia como consertar -- a tela da criacao ate prometia ("pode re-escolher em jogo
	/// depois") um menu que nao existia.
	///
	/// QUEM CONFERE E O SERVIDOR: a lista passa pelo `Sanear` da classe DESTE corpo (o cliente nao decide quantos slots ha), e so um
	/// Frost Demon o usa.
	/// </summary>
	private void ComandoDasFormasDoFrost(ServerPlayer pl, string arg)
	{
		if (!FormasDeFrost.EhFrost(pl.Race)) { Avisar(pl, "só um Frost Demon escolhe os corpos das formas."); return; }
		if (EmCena(pl)) { Avisar(pl, "espere a transformação terminar pra trocar de corpo."); return; }

		var escolhidos = new List<string>();
		foreach (string pedaco in arg.Split('|'))
			escolhidos.Add(int.TryParse(pedaco, out int i) && i >= 0 && i < FormasDeFrost.Catalogo.Length
				? FormasDeFrost.Catalogo[i] : "");
		pl.Visual.FormasDeFrost = FormasDeFrost.Sanear(pl.Ficha.Class, escolhidos);

		// DE VOLTA A FORMA INICIAL, como o `formchoose` do DM ("aplica e reverte pra forma inicial", `icer.dm:82`). Sem cena: trocar de
		// corpo pelo menu nao e um gesto de luta, e o repouso nao tem cinematica.
		string antes = pl.Forma.Atual, piso = Catalogo.IdDoPiso(Perfil(pl));
		if (antes != piso)
		{
			bool estreou = pl.Forma.Entrar(piso);
			AplicarForma(pl);
			AnunciarForma(pl, antes, piso, estreou, semCena: true);
		}

		ReapresentarAparencia(pl);
		Persistir(pl);
		Avisar(pl, "os corpos das suas formas foram trocados.");
		GD.Print($"[server] {pl.Name} trocou os corpos das formas: {string.Join(", ", pl.Visual.FormasDeFrost)}");
	}
}
