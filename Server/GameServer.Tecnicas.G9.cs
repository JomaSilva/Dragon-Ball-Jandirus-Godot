using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G9 -- O SELO, E O SIGILO PELO LADO DE QUEM ESCONDE.
///
/// ============================ DE ONDE VEIO ESTE LOTE ============================
/// Ele nao nasceu de uma lista de desejos: nasceu de um BURACO DO EXTRATOR. Tres skills de cargo
/// (`Mafuba`, `Open Dead Zone`, `Superior Seal`) declaram o `after_learn()` com o typepath na
/// propria linha, num arquivo (`Modules/Magic/Sealing.dm`) que o extrator lia 175 arquivos ANTES
/// daquele onde os typepaths moram (`ordered/EarthRanks.dm`). O corpo caia no chao em silencio, as
/// tres saiam do `skills.json` com `verbos: []`, e o censo as classificava como "folha muda de
/// nascenca" -- o mesmo carimbo de quem o DM tambem nao faz nada.
///
/// O RESULTADO EM JOGO era uma mentira com duas bocas: o painel do Eremita Tartaruga anunciava o
/// Mafuba entre o que o cargo ENTREGA (`GameServer.Ranks.OQueOCargoEntrega`) e o recado de posse
/// dizia "o cargo te entrega: ... Mafuba" (`GameServer.CargoPortas.ContarOQueOCargoDeu`). O
/// jogador cumpria karma 25 e 1 milhao de BP, lia as duas frases, e nao havia botao nenhum.
///
/// Consertado o extrator (`DmSkillScanner.CorposDeAprendizado`), os tres verbos entraram no
/// catalogo -- e entrar no catalogo e o que os torna EXIGIVEIS: a partir dali o censo os cobra.
/// ==============================================================================
///
/// ============================ AS QUATRO, E POR QUE ESTAS QUATRO ============================
///     verbo             quem concede                              fonte no DM
///     ----------------  ----------------------------------------  ------------------------------
///     Mafuba            Eremita Tartaruga (skill `Mafuba`)        `Magic/Sealing.dm:156-190`
///     Open_Dead_Zone    Guardiao da Terra (skill `DeadZone`)      `Magic/Sealing.dm:239-253`
///     Conceal_Power     Basic Ki Control nivel 5 (degrau)         `Stats/BP/Power Control.dm:71-83`
///     Power_Control     Basic Ki Control nivel 5 (degrau)         `Stats/BP/Power Control.dm:176-186`
///
/// **O `Seal_Mob` (Superior Seal) NAO ENTRA, e a recusa e a regra do dono**: *"se um verbo depender
/// de sistema que nao existe, NAO o entregue pela metade"*. Ele nao usa o motor de selo pelo
/// caminho de cima -- monta um `/obj/Ritual`, converte Ki em mana (`convert_ki_to_magic_e`) e
/// delega a `do_tietary("e_seal_s")`, cuja forca e uma disputa de MANA e nao de BP
/// (`Rituals_Manipulation.dm:327-343`). Deste port o sistema de magia tem so o TETO
/// (`Fighter.MagicCap`). Ele fica catalogado no `CensoDeSkills.Esperando` como `SistMagia`, junto
/// dos outros sete verbos de magia -- que ja e um avanco sobre ontem, quando ele nem aparecia.
/// =========================================================================================
///
/// ============================ AS DUAS DO SIGILO FECHAM UM SISTEMA PELA METADE QUE FALTANTE ============================
/// `Conceal_Power` e `Power_Control` estavam no `Esperando` como *"sigilo de poder pelo lado de quem
/// ESCONDE (o port so tem o de quem LE)"* -- e a etiqueta estava CERTA, o que e raro o bastante pra
/// valer nota. Tudo do lado de quem le ja existia: `Fighter.isconcealed` trava o BP expresso em 5
/// (`Fighter.Power.cs:86`) e troca a defesa (`Fighter.Statify.cs:42`), e o `powerMod` ja entra na
/// conta do BP (`Fighter.Power.cs:127`). O que nao existia era **um botao do jogador que escrevesse
/// esses dois campos**: hoje quem escreve `isconcealed` e a invisibilidade, o admin e o ZanzoClash,
/// e `powerMod` nao era baixado por NADA no port inteiro.
///
/// E ISSO DEIXAVA UM ESTAGIO INTEIRO DA TECLA C INALCANCAVEL: `EstagioDaCarga.Retomando`
/// (`CargaDeKi.cs:14`, `:205-209`) existe pra trazer o `powerMod` de volta a 1 -- e como nada o
/// baixava, ele nunca era menor que 1 e o estagio nunca acontecia. Com o `Power_Control` portado, a
/// segunda etapa do power-up do anime passa a existir de verdade.
/// ==================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 0. O REGISTRO E O DESPACHO
	// =====================================================================
	/// <summary>
	/// AS QUATRO TECNICAS DESTE LOTE. Chamado do `RegistrarTecnicas` junto dos outros lotes.
	///
	/// AS LINHAS-ESPELHO ESTAO EM `Core/Skills/Tecnicas.Portadas.cs`, e nao e opcional: a
	/// `--catalogoteste` compara as duas bocas nas duas direcoes toda rodada.
	/// </summary>
	private void RegistrarTecnicasG9()
	{
		IniciarLote("G9");
		Vivo("Mafuba", MafubaG9);
		Vivo("Open_Dead_Zone", AbrirDeadZoneG9);
		Vivo("Conceal_Power", OcultarPoderG9);
		Vivo("Power_Control", ControleDePoderG9);
	}

	// =====================================================================
	// 1. MAFUBA -- `Magic/Sealing.dm:156-190`
	// =====================================================================
	/// <summary>O id de catalogo do pote (`construcoes.json`, `/obj/Creatables/Sealing_Jar`).</summary>
	private const string TipoDoPote = "Sealing_Jar";

	/// <summary>
	/// A ONDA SELANTE.
	///
	/// ============================ AS DUAS CAIXAS DO ORIGINAL VIRARAM DUAS ESCOLHAS AUTOMATICAS ============================
	/// O verb do DM abre DOIS `input()` bloqueantes: primeiro o pote (`:158-161`, lista dos
	/// `SealingItem` em `view()`), depois o alvo (`:165`, os mobs em `view()`). Num servidor
	/// autoritativo nao existe "travar o jogador esperando uma caixa" -- e a solucao ja e a da casa
	/// (lotes G4 e G8): o alvo vem do MARCADO (o duplo clique), e o pote e o mais proximo a vista.
	///
	/// **A LISTA VAZIA CONTINUA SENDO UMA RECUSA**, e isso e fidelidade e nao zelo: sem pote em
	/// vista, o `input()` do DM abre sem nenhuma opcao e o `isnull(choice)` cancela (`:162-164`). O
	/// Mafuba SEM POTE nao existe no jogo original, e nao pode existir aqui.
	/// ===================================================================================================================
	///
	/// ============================ O CUSTO E PAGO NA HORA, E NAO NA CHEGADA ============================
	/// No DM o `SpreadDamage(90)` e o vinculo pote-preso acontecem no corpo do VERB (`:185-188`),
	/// logo depois de a fita nascer -- nao no `checkradius`. Ou seja: quem lanca paga os 90 de dano
	/// em cada membro (e pode morrer disso, `:189-190`) mesmo que a fita nunca alcance ninguem.
	/// Portado assim de proposito; e o que faz o Mafuba ser a aposta que a `desc` promete.
	/// ================================================================================================
	/// </summary>
	private void MafubaG9(ServerPlayer pl)
	{
		if (pl.Ficha.dead || pl.Ficha.KO) { Avisar(pl, "voce nao esta em condicoes."); return; }
		if (pl.Selo.Preso) { Avisar(pl, "daqui de dentro nao."); return; }

		// `for(var/obj/items/SealingItem/A in view())` (`:159-160`) -- o mais proximo a vista.
		Obra? pote = _noChao
			.Where(o => o.Tipo == TipoDoPote && o.Zona.Equals(pl.Zone))
			.Where(o => Vec2.Distance(new Vec2(o.X, o.Y), pl.Pos) <= RaioDaVista)
			.OrderBy(o => Vec2.Distance(new Vec2(o.X, o.Y), pl.Pos))
			.FirstOrDefault(o => !PoteEstaSelado(o));

		if (pote == null)
		{
			Avisar(pl, "nao ha Pote Selante vazio a vista -- sem pote nao ha Mafuba.");
			return;
		}

		ServerPlayer? alvo = AlvoDeTecnica(pl, RaioDaVista);
		if (alvo == null) { Avisar(pl, "nao ha ninguem a vista pra selar."); return; }
		if (alvo.Selo.Preso) { Avisar(pl, $"{alvo.Name} ja esta selado."); return; }

		// ---- a fita nasce no lancador e sai atras do alvo (`:170-184`) ----
		_fitas.Add(new FitaDoMafuba
		{
			Dono = pl.Id,
			Alvo = alvo.Id,
			PoteId = pote.Id,
			Zona = pl.Zone,
			Pos = pl.Pos,
			Vida = VidaDaFita,
		});

		Falar(pl, Protocol.Fala.Diz, "MAFUBA!!");
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			Avisar(o, $"{pl.Name} lança a Onda Selante em {alvo.Name}!");

		// ---- o preco, `:188-190` ----
		//
		// `SpreadDamage(90)` seguido de `if(HP<=0) src.Death()`. As duas linhas cabem numa chamada:
		// o `EspalharDanoG3` com `letal: true` ja passa pelo funil da derrota quando o corpo cede.
		// O autor e a propria vitima -- ninguem "abateu" quem se sangrou pra selar, e o DM tambem
		// nao preenche `deathKiller` aqui.
		EspalharDanoG3(pl, pl, 90, letal: true);
		Avisar(pl, "a Onda leva quase toda a sua vida junto.");

		GD.Print($"[server] MAFUBA: {pl.Name} -> {alvo.Name} (pote #{pote.Id})");
	}

	// =====================================================================
	// 2. OPEN DEAD ZONE -- `Magic/Sealing.dm:239-253`
	// =====================================================================
	/// <summary>
	/// RASGA A REALIDADE.
	///
	/// ============================ TRES COISAS DO ORIGINAL QUE PARECEM ENGANO E NAO SAO ============================
	///   * **O PORTAL NASCE AO NORTE, SEMPRE.** `A.loc = locate(usr.x, usr.y+5, usr.z)` (`:250`) --
	///     a direcao de quem abre nao entra na conta. Portado ao pe da letra;
	///   * **A GUARDA DE PORTAL DUPLICADO NAO FUNCIONA.** `if(/obj/DeadZone in view()) return`
	///     (`:241`) compara um TYPEPATH com uma lista de atomos: nunca casa, e na pratica da pra
	///     empilhar portais. Nao esta "consertado" aqui de proposito -- consertar mudaria o jogo, e
	///     essa escolha e do dono. O custo de Ki (91% do tanque) ja e o freio de fato;
	///   * **O CUSTO E COBRADO DUAS VEZES NA LEITURA E UMA NA CONTA.** `if(usr.Ki>=MaxKi/1.1)` abre o
	///     verb (`:243`) e a MESMA condicao aparece de novo antes de subtrair (`:252-253`). E o
	///     mesmo teste escrito duas vezes; uma subtracao so.
	/// ===========================================================================================================
	/// </summary>
	private void AbrirDeadZoneG9(ServerPlayer pl)
	{
		if (pl.Ficha.dead || pl.Ficha.KO) { Avisar(pl, "voce nao esta em condicoes."); return; }
		if (pl.Selo.Preso) { Avisar(pl, "daqui de dentro nao."); return; }

		// `if(usr.Ki>=MaxKi/1.1)` (`:243`) -- 90,9% do tanque, e nao "quase tudo" arredondado.
		double custo = pl.Ficha.MaxKi / 1.1;
		if (pl.Ficha.Ki < custo)
		{
			Avisar(pl, $"energia de menos: a fenda pede {custo:0} e voce tem {pl.Ficha.Ki:0}.");
			return;
		}
		pl.Ficha.Ki -= custo;

		var onde = new Vec2(pl.Pos.X, pl.Pos.Y - TilesAoNorteDoPortal * ZoneCollision.TileSize);
		_portais.Add(new PortalDaDeadZone
		{
			Dono = pl.Id,
			// `A.makerBP = expressedBP` (`:251`): o selo vale o poder de quem abriu NAQUELE instante.
			// Fica congelado -- ficar mais forte depois nao reforca o que ja esta preso.
			BpDeQuemAbriu = pl.Ficha.expressedBP,
			Zona = pl.Zone,
			Pos = onde,
			Vida = VidaDoPortal,
		});

		// `to_chat(view(6), "[usr] rips open reality...")` (`:247`)
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			Avisar(o, $"{pl.Name} rasga a realidade e um portal pra Dead Zone se abre!!");

		GD.Print($"[server] DEAD ZONE: {pl.Name} abriu em {pl.Zone} (selo {pl.Ficha.expressedBP:N0} BP)");
	}

	// =====================================================================
	// 3. CONCEAL POWER -- `Stats/BP/Power Control.dm:71-83`
	// =====================================================================
	/// <summary>Quando cada um pode tornar a mexer no sigilo. O `canconceal` do DM.</summary>
	private readonly Dictionary<int, long> _sigiloPronto = [];

	/// <summary>QUEM SAIU LEVA O ESTADO DELE JUNTO -- inscrito no `EsquecerTecnicas`, porque id se reusa.</summary>
	private void EsquecerG9(int id) => _sigiloPronto.Remove(id);

	/// <summary>
	/// `sleep(50)` (`Power Control.dm:76` e `:81`) -- CINCO segundos entre ligar e desligar.
	///
	/// **A MENSAGEM DO ORIGINAL MENTE**: as duas frases dizem *"It'll take a minute"* e o codigo
	/// dorme 50 tiques, que sao 5 s. Portado o CODIGO. O texto deste port diz cinco segundos, que e
	/// o que de fato acontece -- prometer um minuto seria copiar o erro de digitacao do autor pra
	/// dentro da interface.
	/// </summary>
	private const long EsperaDoSigiloMs = 5000;

	/// <summary>
	/// LIGA E DESLIGA O SIGILO -- o alterna-estado de `isconcealed` com trava de 5 s.
	///
	/// O `if(canconceal)` do DM e uma trava de UMA ponta: ela e checada antes de qualquer troca e
	/// re-armada 5 s depois, valendo pros dois sentidos. Aqui e um instante por jogador, que da o
	/// mesmo comportamento sem prender uma linha de execucao dormindo.
	/// </summary>
	private void OcultarPoderG9(ServerPlayer pl)
	{
		// `else to_chat(src, "You can't do that yet.")` (`:83`)
		if (EmEspera(pl, _sigiloPronto, "ainda nao da")) return;
		long agora = NowMs();

		_sigiloPronto[pl.Id] = agora + EsperaDoSigiloMs;
		pl.Ficha.isconcealed = !pl.Ficha.isconcealed;

		Avisar(pl, pl.Ficha.isconcealed
			? "voce esconde o seu poder. Leva cinco segundos pra voltar a mostra-lo."
			: "voce deixa de esconder o seu poder. Leva cinco segundos pra esconde-lo de novo.");

		// A FICHA VAI JUNTO: `isconcealed` muda o BP que o mundo LE (`Fighter.Power.cs:86`) e a
		// defesa (`Fighter.Statify.cs:42`). Sem o reenvio o proprio dono continuaria vendo o numero
		// velho ate a proxima ficha lenta.
		MandarFicha(pl);
	}

	// =====================================================================
	// 4. POWER CONTROL -- `Stats/BP/Power Control.dm:176-186`
	// =====================================================================
	/// <summary>
	/// SEGURA O PROPRIO PODER numa porcentagem.
	///
	/// ============================ O `while` DO ORIGINAL NAO E UMA RAMPA ============================
	/// O corpo do DM parece uma descida gradual:
	///
	///     while(!is_drawing &amp;&amp; usr.powerMod &gt; numb &amp;&amp; usr.powerMod &gt; 0)
	///         usr.powerMod -= 0.01 * (Ekiskill+(kicontrolskill+kigatheringskill)/25)
	///         usr.powerMod = max(numb, usr.powerMod)
	///     usr.powerMod = numb
	///
	/// **Nao ha `sleep` dentro do laco.** Ele roda inteiro dentro de um tique e a linha de baixo
	/// escreve `numb` de qualquer jeito -- o efeito observavel e uma atribuicao. (O `max(numb,...)`
	/// no lugar de um `max(1,...)` e um conserto que ja esta comentado no proprio DM: com o `1`, a
	/// condicao nunca ficava falsa e o laco sem `sleep` TRAVAVA o servidor.) Portado o efeito.
	///
	/// O `!is_drawing` e o "cancele carregando" que a caixa de texto promete, e ele ja existe deste
	/// lado: `CargaDeKi` tem o estagio <see cref="EstagioDaCarga.Retomando"/>, que sobe o `powerMod`
	/// de volta a 1 enquanto a tecla C estiver em baixo. Nao ha o que escrever aqui pra isso.
	/// ============================================================================================
	///
	/// `numb = min(numb,100)` e `max(1,numb)` (`:179-180`): a caixa aceita qualquer numero e a
	/// faixa util e 1..100. So BAIXA -- pedir mais do que se tem agora nao levanta nada (`:182`).
	/// </summary>
	private void ControleDePoderG9(ServerPlayer pl, string arg)
	{
		if (!double.TryParse(arg, System.Globalization.NumberStyles.Float,
							 System.Globalization.CultureInfo.InvariantCulture, out double pedido))
		{
			Avisar(pl, $"seu poder esta em {pl.Ficha.powerMod * 100:0}%. Use Power_Control:<1 a 100>.");
			return;
		}

		double alvo = Math.Max(1, Math.Min(pedido, 100)) / 100;

		// `while(... usr.powerMod > numb ...)` (`:181`): pedir MAIS do que se expressa agora nao faz
		// nada. Quem quer subir carrega -- e a segunda etapa do power-up.
		if (alvo >= pl.Ficha.powerMod)
		{
			Avisar(pl, $"isso nao BAIXA nada: voce ja expressa {pl.Ficha.powerMod * 100:0}%. "
					 + "Pra subir, carregue (tecla C).");
			return;
		}

		pl.Ficha.powerMod = alvo;
		Avisar(pl, $"voce segura o proprio poder em {alvo * 100:0}%.");
		MandarFicha(pl);
		GD.Print($"[server] {pl.Name} baixou o powerMod pra {alvo:0.##}");
	}
}
