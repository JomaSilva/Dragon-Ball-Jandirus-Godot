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
/// ============================ AS TRES, E POR QUE ESTAS TRES ============================
///     verbo             quem concede                              fonte no DM
///     ----------------  ----------------------------------------  ------------------------------
///     Mafuba            Eremita Tartaruga (skill `Mafuba`)        `Magic/Sealing.dm:156-190`
///     Open_Dead_Zone    Guardiao da Terra (skill `DeadZone`)      `Magic/Sealing.dm:239-253`
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
/// ============================ O `Power_Control` FECHA UM SISTEMA QUE ESTAVA PELA METADE ============================
/// Ele e o `Conceal_Power` (do mesmo degrau) estavam no `Esperando` como *"sigilo de poder pelo lado de quem
/// ESCONDE (o port so tem o de quem LE)"* -- e a etiqueta estava CERTA, o que e raro o bastante pra valer nota.
/// Tudo do lado de quem le ja existia: `Fighter.isconcealed` trava o BP expresso em 5 (`Fighter.Power.cs`), e o
/// `powerMod` ja entra na conta do BP. O que nao existia era **um botao do jogador que escrevesse esses campos**:
/// `powerMod` nao era baixado por NADA no port inteiro.
///
/// **O `Conceal_Power` MOROU AQUI ATE 2026-10-12**, com botao proprio e a trava de 5 s do `canconceal`. O dono jogou
/// e o fundiu no relaxar (*"esconder o ki e relaxar é redundante [...] tire o botao de esconder o ki e faça com q o
/// de relaxar faca o poder do personagem cair pra 5 de poder de luta sempre, ficando invisivel pra scouters etc"*):
/// o que o verb do DM faz quem faz agora e o `prontidao`, e o censo o da por atendido la
/// (`CensoDeSkills.PorOutroCanal`). O RESTO DO SISTEMA (relaxar / ficar pronto, o retrato do estado pra tela, o
/// login e o controle na aba Ki) mora em `GameServer.PoderEmUso.cs`.
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
	/// AS TRES TECNICAS DESTE LOTE. Chamado do `RegistrarTecnicas` junto dos outros lotes.
	///
	/// AS LINHAS-ESPELHO ESTAO EM `Core/Skills/Tecnicas.Portadas.cs`, e nao e opcional: a
	/// `--catalogoteste` compara as duas bocas nas duas direcoes toda rodada.
	/// </summary>
	private void RegistrarTecnicasG9()
	{
		IniciarLote("G9");
		Vivo("Mafuba", MafubaG9);
		Vivo("Open_Dead_Zone", AbrirDeadZoneG9);
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
		if (EhCadaver(pl) || pl.Ficha.KO) { Avisar(pl, "voce nao esta em condicoes."); return; }
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
		if (EhCadaver(pl) || pl.Ficha.KO) { Avisar(pl, "voce nao esta em condicoes."); return; }
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
	// 3. QUEM SAI
	// =====================================================================
	/// <summary>QUEM SAIU LEVA O ESTADO DELE JUNTO -- inscrito no `EsquecerTecnicas`, porque id se reusa.</summary>
	private void EsquecerG9(int id) => EsquecerOPoderEmUso(id);   // o retrato do poder em uso que a tela dele recebeu -- ver GameServer.PoderEmUso.cs

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
	/// faixa util e 1..100. So BAIXA -- e isto e DIVERGENCIA DECLARADA de `:185`: la a ultima linha escreve
	/// `numb` de qualquer jeito, e pedir mais do que se expressa SOBE na hora, contra a propria caixa (*"You can
	/// only use this to set your power lower than it is"*). Aqui vale a caixa. A conta mora em
	/// `Core.Stats.PoderEmUso` (<see cref="Jandirus.Core.Stats.PoderEmUso.Baixar"/>).
	///
	/// O VERBO SEM NUMERO (o botao da aba Skills, uma tecla ligada a ele) nao mexe em nada: o numero se escolhe
	/// na faixa "Poder em uso" da aba Ki (`Client/MenuJogo.PoderEmUso.cs`), e a resposta diz isso.
	/// </summary>
	private void ControleDePoderG9(ServerPlayer pl, string arg)
	{
		bool relaxado = Jandirus.Core.Stats.PoderEmUso.Relaxado(pl.Ficha);
		if (!double.TryParse(arg, System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out double pedido)
			|| !NumeroFinito(pedido))   // `Power_Control:NaN` escrevia `powerMod = NaN`: ver `NumeroFinito`
		{
			string estado = relaxado
				? "você está relaxado. "
				: $"você está usando {Jandirus.Core.Stats.PoderEmUso.Porcento(pl.Ficha)}% do seu poder. ";
			Avisar(pl, estado + "Pra escolher quanto usar, abra a aba Ki do menu.");
			return;
		}

		// (caido nao mexe no proprio poder -- ver `PorQueNaoMexeNoPoder`)
		if (PorQueNaoMexeNoPoder(pl) is { Length: > 0 } nao) { Avisar(pl, nao); return; }

		// RELAXADO A BARRA NAO VALE: o corpo soltou o poder todo (5 de poder, escondido), e ficar pronto devolve os
		// 100% de qualquer jeito. Aceitar o pedido seria dizer "voce segura o poder em 40%" a quem continua em 5.
		if (relaxado)
		{
			Avisar(pl, "relaxado você não está usando poder nenhum: fique pronto antes de escolher quanto usar.");
			return;
		}

		double alvo = Jandirus.Core.Stats.PoderEmUso.FracaoDoPedido(pedido);

		// Pedir MAIS do que se expressa agora nao faz nada. Quem quer subir carrega -- e a segunda etapa do
		// power-up -- ou fica pronto (`AlternarProntidao`).
		if (!Jandirus.Core.Stats.PoderEmUso.Baixar(pl.Ficha, alvo))
		{
			Avisar(pl, $"isso não BAIXA nada: você já usa {Jandirus.Core.Stats.PoderEmUso.Porcento(pl.Ficha)}% do seu poder. "
					 + "Pra subir, carregue (tecla C) -- ou fique pronto.");
			return;
		}

		Avisar(pl, $"você segura o próprio poder em {alvo * 100:0}%.");
		AoMexerNoPoder(pl);   // o poder expresso, a ficha e o retrato acompanham NA HORA
		GD.Print($"[server] {pl.Name} baixou o powerMod pra {alvo:0.##}");
	}
}
