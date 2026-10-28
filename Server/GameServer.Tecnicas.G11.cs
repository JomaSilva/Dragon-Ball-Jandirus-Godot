using Godot;
using Jandirus.Core;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G11 -- "UMA FUNCAO SOBRE PECA EXISTENTE": as skills que JA estavam na arvore e cujo efeito
/// nunca tinha sido trazido, cada uma pendurada numa peca que o port ja tinha.
///
/// ============================ DE ONDE VEIO ESTE LOTE ============================
/// Do censo de 2026-09-02 (`audit_final.md`, TABELA 1 e o grupo F2): 41 skills alcancaveis que o
/// port nao aplicava NADA, e 25 delas classificadas como "uma funcao sobre peca existente" -- o
/// alicerce de buffs (`GameServer.Buffs.cs`), o salto de zona do `RiftTeleport` (G4), a paralisia
/// do G5, o `Fighter.AttackGain` do G2, o `nave_observar`, o `Ceu.cs`, o agarrao e a Final
/// Explosion (G3). O dono pediu com estas palavras: *"tem umas skills q ja estao na tree mas n
/// tiveram efeito portado, faca o port delas"*.
/// ================================================================================
///
/// ============================ AS FAMILIAS, E A PECA DE CADA UMA ============================
///     familia                    verbs / skills                              peca
///     -------------------------  ------------------------------------------  ----------------------------
///     buff/debuff COM PRAZO      Sneak, Expand_Body, Majin, Shackle,          `LigarBuff`/`DesligarBuff`
///                                Sun, Moon, Above_All (metade solar)          (guarda o que aplicou)
///     teleporte com carona       Devil_Bringer, Kai_Kai, Instant_Transmission `MoveToZone` + carona (G8)
///     campo + uma condicao       Flip, Perfect_Metabolism, Stretchy_Arms,     agarrao, estomago, G3
///                                Self_Destruct
///     ganho no tique de combate  Heran_Power                                  `Fighter.AttackGain`
///     paralisia                  Psycho_Thread, Freeze                        `_paralisadoAte` (G5)
///     camera                     Observe                                      o molde do `nave_observar`
///     funcao pura                Unlock_Potential, Time_Store, Give_Power,    `CapCheck`, `Ceu`, o Heal
///                                brainpower, meditatepower                    do G6 ao contrario
/// ==========================================================================================
///
/// ============================ O QUE VALE PARA O LOTE INTEIRO ============================
///   * TODO buff com prazo TERMINA pelo alicerce (`BuffAtivo.ExpiraEm` + `TickDosBuffs`), e o
///     desligar desfaz EXATAMENTE o que o ligar somou/multiplicou -- a regra de ouro de
///     `GameServer.Buffs.cs`. Nenhum buff deste lote e persistente: no DM todos os `/obj/buff`
///     envolvidos nascem com `persistant = FALSE` (`buffs.dm:15`) e os `T*` "cannot last past
///     logoff" (`master.dm:84`). Os buffs PERIODICOS (Sun/Moon/Above_All) se re-erguem sozinhos no
///     primeiro tique depois do login, porque o que persiste e a MAESTRIA e nao o buff.
///   * O que NAO cabe numa peca existente NAO foi inventado: a Estrela Makyo (a metade noturna do
///     Above_All e o 4o grau do Expand_Body), o olho remoto do Observe, o ritual de God Ki que o
///     Give_Power alimenta. Cada um esta escrito no verb que o cita.
///   * Bug OBVIO do DM nasceu mantido com o numero visivel no comentario. Em 2026-09-02 o dono mandou
///     consertar os citados (*"corrija esses bugs q vc citou"*) e tres deles passaram a cobrar o que a
///     descricao promete: o `Ki*=0.5` POR ALVO do Freeze (agora UMA vez), o `Ki>=700 / Ki-=100` do
///     Psycho Thread (agora confere e cobra `100*BaseDrain`), o `kireq*BaseDrain` do Instant
///     Transmission que zerava o Ki (agora cobra `kireq`). Cada verb guarda a citacao do DM ao lado do
///     conserto. FICA o `switch(currentMoonlight==5)` da Moon que nunca casa (o dono nao o citou). O
///     que nunca se manteve e defeito de DESFAZER (o `Tspeed -=` no lugar de `+=` do Expand), porque
///     o alicerce desfaz por construcao e reproduzir o vazamento exigiria escreve-lo na mao.
/// =====================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 0. REGISTRO E DESPACHO
	// =====================================================================
	/// <summary>
	/// OS CATORZE VERBS DESTE LOTE. As linhas-espelho estao em `Core/Skills/Tecnicas.Portadas.cs`
	/// (a `--catalogoteste` compara as duas bocas nas duas direcoes).
	///
	/// AS NOVE SKILLS SEM VERB (Sun, Moon, Above_All, Heran_Power, Perfect_Metabolism,
	/// Stretchy_Arms, Time_Store, brainpower, meditatepower) nao entram aqui de proposito: elas sao
	/// `effector()`/`after_learn()` e nao botao -- o efeito delas mora no <see cref="TickDoEfetorG11"/>
	/// e nos ganchos do agarrao. Registra-las como tecnica inflaria o "portadas" sem botao nenhum.
	/// </summary>
	/// <summary>
	/// (2026-10-16) O FRAGMENTED MIND NOS TELETRANSPORTES -- *"lose the ability to use ... all variants of Instant Transmission"*: a
	/// Transmissao Instantanea e as duas de planeta (Kai Kai, Devil Bringer). Ver `GameServer.SagaDoMajin.MenteFragmentada`.
	/// </summary>
	private bool RecusarPelaMenteFragmentada(ServerPlayer pl)
	{
		if (!MenteFragmentada(pl)) return false;
		Avisar(pl, "a sua mente partida não acha caminho nenhum: sem teletransporte enquanto a loucura durar.");
		return true;
	}

	private void RegistrarTecnicasG11()
	{
		IniciarLote("G11");
		Vivo("Sneak", SneakG11);
		Vivo("Expand_Body", ExpandirCorpoG11);
		Vivo("Majin", MajinG11);
		Vivo("Shackle", GrilhaoG11);
		Vivo("Devil_Bringer", DevilBringerG11);
		Vivo("Kai_Kai", KaiKaiG11);   // (2026-10-23) a tela dos planetas conhecidos -- ver GameServer.KaiKai.cs
		Vivo("Instant_Transmission", TeletransporteG11);
		Vivo("Flip", CambalhotaG11);
		Vivo("Self_Destruct", AutodestruirG11);
		Vivo("Psycho_Thread", AlternarFioPsiquicoG11);
		Vivo("Freeze", CongelarG11);
		Vivo("Observe", ObservarG11);
		Vivo("Unlock_Potential", OferecerPotencialG11);
		Vivo("Give_Power", DarPoderG11);
	}

	// =====================================================================
	// 1. OS CAMINHOS DAS SKILLS SEM VERB (lidos pelo efetor)
	// =====================================================================
	private const string SkillSunG11 = "/datum/skill/makyo/Sun";
	private const string SkillMoonG11 = "/datum/skill/makyo/Moon";
	private const string SkillAboveAllG11 = "/datum/skill/makyo/Above_All";
	private const string SkillHeranPowerG11 = "/datum/skill/heran/Heran_Power";
	private const string SkillTimeStoreG11 = "/datum/skill/kanassajin/Time_Store";
	private const string SkillBrainPowerG11 = "/datum/skill/gray/brainpower";
	private const string SkillMeditatePowerG11 = "/datum/skill/gray/meditatepower";
	private const string SkillPsychoThreadG11 = "/datum/skill/heran/Psycho_Thread";

	// =====================================================================
	// 2. ESTADO DO LOTE
	// =====================================================================
	/// <summary>Quando a invisibilidade do Sneak acaba, por jogador (ms).</summary>
	private readonly Dictionary<int, long> _sneakAteG11 = [];

	/// <summary>O `majining` do DM (`Majin.dm:11-18`): dois segundos entre um toggle e outro.</summary>
	private readonly Dictionary<int, long> _prontoMajinG11 = [];

	/// <summary>Um Instant Transmission em concentracao.</summary>
	private sealed class TransmissaoG11
	{
		public int Alvo;
		public long QuandoMs;
		public Vec2 Ancora;
		public double Kireq;
	}

	private readonly Dictionary<int, TransmissaoG11> _transmissaoG11 = [];

	/// <summary>Uma doacao de poder em andamento -- o `givingpower` (`givepower.dm:27`).</summary>
	private sealed class DoacaoG11
	{
		public int Alvo;
		public int Deu;          // `gaveamount`
		public bool Parar;       // o segundo aperto (`givingpower = 0`)
	}

	private readonly Dictionary<int, DoacaoG11> _doacaoG11 = [];

	/// <summary>
	/// O `gavepower = 100` (`givepower.dm:39`): enquanto corre, nao se doa de novo (`!gavepower`, `:36`). E SO
	/// isso -- o laco de andar apenas o DESCONTA (`if(gavepower) gavepower = max(0,gavepower - 1)`,
	/// `movement handler.dm:139`), sem zerar o `mobTime`: quem doa anda. Ver `TickDaDoacaoG11`.
	/// </summary>
	private readonly Dictionary<int, long> _poderDadoAteG11 = [];

	/// <summary>Ofertas de Unlock Potential: conta do alvo -> (conta de quem ofereceu, prazo).</summary>
	private readonly Dictionary<string, (string Quem, long Ate)> _ofertasDePotencialG11 = new(StringComparer.Ordinal);

	/// <summary>`ki_boost_buffer` (`heran.dm:65`), por jogador.</summary>
	private readonly Dictionary<int, double> _bufferHeranG11 = [];

	/// <summary>O fator de forma que o astro esta aplicando agora, por (jogador, buff). Ver <see cref="AplicarAstroG11"/>.</summary>
	private readonly Dictionary<(int, string), double> _astroFatorG11 = [];

	/// <summary>Relogios por jogador: a agua do Perfect Metabolism (0,3 s) e o Loop do Expand (1 s).</summary>
	private readonly Dictionary<int, double> _relogioDaAguaG11 = [], _relogioDoExpandG11 = [];

	/// <summary>O relogio de 1 s do decaimento do `CooldownAmount` (`base.dm:212-227`).</summary>
	private double _relogioDoCooldownG11;

	/// <summary>O dia da Terra em que o Time Store creditou pela ultima vez, por jogador.</summary>
	private readonly Dictionary<int, double> _diaDoTimeStoreG11 = [];

	/// <summary>Quem esta segurando alguem com o BRACO ESTICADO (a forca vale um terco). Ver <see cref="FatorDoBracoG11"/>.</summary>
	private readonly HashSet<int> _agarroesEsticadosG11 = [];

	/// <summary>Contador pra dar id unico a cada Shackle: no DM cada `spawn` e um debuff independente e eles EMPILHAM.</summary>
	private int _seqShackleG11;

	// =====================================================================
	// 3. SNEAK -- `Assassain Skills.dm:165-174`
	// =====================================================================
	/// <summary>`basicCD += 60` -- seis segundos, a recarga compartilhada dos golpes de assassino.</summary>
	private const long RecargaDoSneakMsG11 = 6000;

	/// <summary>
	/// A FURTIVIDADE: `kireq = Ephysoff*BaseDrain*12`, `basicCD += 60`, e
	/// `TempBuff(list("invisibility"), 10 + Etechnique)`.
	///
	/// ============================ O SNEAK DO DM NUNCA DEIXOU NINGUEM INVISIVEL ============================
	/// `TempBuff` recebe `list("invisibility")` -- uma lista SEM valor associado -- e le a magnitude
	/// como `TempList[S]` (`tempeffects.dm:45`), que numa lista dessas e `null`. O efeito nasce com
	/// `magnitude = null + 0 = 0`, e o `Added()` da classe base espera `while(!magnitude) sleep(1)`
	/// (`tempeffects.dm:62`) -- pra sempre. O `target.invisibility = 1` (`:204`) vem depois do `..()`
	/// e nunca roda. O jogador pagava o Ki e a recarga e continuava a vista.
	///
	/// Portei a INTENCAO (o comentario do proprio verb: "temporarily gives you an invisiblity buff")
	/// com o numero do CODIGO: `10 + Etechnique`, na unidade em que o motor de efeitos le a duracao --
	/// DECISSEGUNDOS (`Effects Master.dm:66`, `world.time - time > duration`). O comentario do verb
	/// promete "10 seconds + technique"; o codigo entrega um segundo mais um decimo por ponto de
	/// Tecnica. Vale o que o codigo entrega.
	///
	/// CONFERIDO em 2026-09-02, quando o dono mandou consertar os defeitos citados: este ja estava
	/// portado pela INTENCAO (a `--g11teste` mede o corpo sumindo, durando e voltando), e nada mudou
	/// alem desta nota.
	/// ======================================================================================================
	///
	/// A INVISIBILIDADE E A DO PORT (`_invisiveis` + `isconcealed`, ver `GameServer.Tecnicas.cs`): e o
	/// unico caminho por onde um corpo some dos snapshots dos outros. A diferenca pra tecnica
	/// Invisibility e que o Sneak NAO paga aluguel por segundo -- o `TickDasTecnicas` pula quem esta
	/// em Sneak, do mesmo jeito que pula o Zanzo Clash.
	/// </summary>
	private void SneakG11(ServerPlayer pl)
	{
		// `!invisibility` -- ja invisivel (pelo Sneak ou pela Invisibility) nao empilha.
		if (_invisiveis.Contains(pl.Id)) { Avisar(pl, "voce ja esta fora da vista."); return; }
		if (!AbrirPunhoG7(pl, 12, RecargaDoSneakMsG11, out double kireq)) return;
		long agora = NowMs();

		long duracao = (long)((10 + pl.Ficha.Etechnique) * 100);   // decissegundos -> ms
		_sneakAteG11[pl.Id] = agora + duracao;
		_invisiveis.Add(pl.Id);
		pl.Ficha.isconcealed = true;
		MandarEfeito(pl, "invisivel", duracao);
		Avisar(pl, $"voce some da vista por {duracao / 1000.0:0.#}s.");
		GD.Print($"[G11] {pl.Name} usou Sneak ({duracao} ms, custou {kireq:0} de Ki)");
	}

	// =====================================================================
	// 4. EXPAND BODY -- `Buffs/globals/Body Expansion.dm:118-167` e o buff `:13-92`
	// =====================================================================
	/// <summary>
	/// `lastpower` por grau (`Body Expansion.dm:53,60,66`): 1,12 / 1,25 / 1,50. O 4o grau (1,75) so
	/// existe pra Makyo debaixo da Estrela Makyo (`:131`), e a Estrela nao existe neste port -- ver
	/// <see cref="ExpandirCorpoG11"/>.
	/// </summary>
	private static readonly double[] PoderDoGrauG11 = [0, 1.12, 1.25, 1.50];

	/// <summary>O custo de cada grau (`Body Expansion.dm:150-156`): `(25/Ekiskill)*1+5`, `(35/Ekiskill)*2+5`, `(40/Ekiskill)*3+5`.</summary>
	private static double CustoDoGrauG11(Fighter f, int grau)
	{
		double ks = Math.Max(f.Ekiskill, 0.01);
		return grau switch
		{
			1 => 25 / ks * 1 + 5,
			2 => 35 / ks * 2 + 5,
			3 => 40 / ks * 3 + 5,
			_ => 25 / ks + 5,   // o `kireq` de entrada (`:123`)
		};
	}

	/// <summary>
	/// A EXPANSAO DO CORPO, por grau.
	///
	/// ============================ O QUE CADA GRAU SOMA (`Body Expansion.dm:75-80`) ============================
	///     Tphysoff += lastpower
	///     Tphysdef += 1 + (lastpower-1)/2
	///     Tspeed   -= 1 - 1/(1 + (lastpower-1)/2)        (maior = mais lento)
	/// Grau 1: +1,12 / +1,06 / -0,057. Grau 3: +1,50 / +1,25 / -0,20.
	/// ================================================================================================
	///
	/// ============================ O DEFEITO DE DESFAZER DO DM, E POR QUE ELE NAO VEIO ============================
	/// Ao trocar de grau o `Loop()` "desfaz" o grau anterior com `container.Tspeed -= firstspeedbuff`
	/// (`:41`) -- o MESMO sinal da aplicacao (`:80`). Ou seja: cada troca de grau tira a velocidade
	/// DUAS vezes e devolve uma so no `DeBuff()` (`:87`). Quem alterna 1->2->3 sai mais lento do que
	/// entrou ate o proximo relog. Nao reproduzi: o alicerce desfaz por construcao (guarda o que
	/// aplicou), e o vazamento so existiria se eu o escrevesse na mao. Trocar de grau aqui e desligar
	/// o grau velho (exato) e ligar o novo. CONFERIDO em 2026-09-02 (a `--g11teste` mede 2 -> 3 -> 0 e
	/// o Tspeed volta exato ao de antes): nada a consertar.
	/// ==========================================================================================================
	///
	/// A ESCOLHA VIROU ARGUMENTO (`Expand_Body:2`), como todo `input()` deste port; sem argumento lista.
	/// O grau 0 relaxa. O slot e o `sBUFF` (`:15`): nao convive com os seis buffs de corpo do G1 nem
	/// com as laminas (`if(KiBladeOn)`, `:120`). O 4o grau pede a Estrela Makyo (`HellStar`, `:131`),
	/// que este port nao tem -- e recusado dizendo isso, e nao inventado.
	///
	/// NO DM OS NUMEROS SO ENTRAM NO PRIMEIRO `Loop()`, um segundo depois do `startbuff`; aqui entram
	/// no aperto. O DM tambem COBRA o Ki quando o `startbuff` falha (`:163-165`, cobra e nao entrega);
	/// aqui recusa nao cobra, que e a regra da casa desde o G1.
	/// </summary>
	private void ExpandirCorpoG11(ServerPlayer pl, string arg)
	{
		const string id = "Expand_Body";
		Fighter f = pl.Ficha;

		// `if(KiBladeOn) "You can't use this with Ki Blades!"` (`:120`)
		if (TemBuff(pl, "Ki_Blade") || TemBuff(pl, "Ki_Sword"))
		{
			Avisar(pl, "voce nao consegue expandir o corpo com uma lamina de Ki na mao.");
			return;
		}

		bool ligado = TemBuff(pl, id);
		int atual = (int)f.expandlevel;

		if (arg.Length == 0)
		{
			Avisar(pl, $"grau atual: {atual}. Use Expand_Body:<grau> --");
			for (int g = 1; g <= 3; g++)
				Avisar(pl, $"  {g}: custa {CustoDoGrauG11(f, g):0.#} de Ki (+{PoderDoGrauG11[g]:0.##} de ofensiva fisica)");
			Avisar(pl, "  0: relaxa o corpo. O 4o grau pede a Estrela Makyo, que nao existe neste mundo.");
			return;
		}

		if (!int.TryParse(arg, out int grau) || grau < 0 || grau > 4)
		{
			Avisar(pl, "grau invalido: use 0, 1, 2 ou 3.");
			return;
		}

		// `if(Choice=="0th Degree")` (`:138-147`)
		if (grau == 0)
		{
			f.expandlevel = 0;
			if (DesligarBuff(pl, id)) Avisar(pl, "voce relaxa o corpo.");   // o efeito sai pelo proprio `DesligarBuff`
			else Avisar(pl, "seu corpo ja esta relaxado.");
			return;
		}

		if (grau == 4)
		{
			Avisar(pl, "o quarto grau so existe debaixo da Estrela Makyo -- e ela nao passa por este mundo.");
			return;
		}

		if (RecusarCaido(pl, "voce nao esta em condicoes de expandir o corpo.")) return;

		// o menu do DM so LISTA o grau que da pra pagar e que nao e o atual (`:128-130`)
		if (ligado && grau == atual) { Avisar(pl, $"voce ja esta no {grau}o grau."); return; }

		double custo = CustoDoGrauG11(f, grau);
		if (f.Ki < custo)
		{
			// `"You don't have enough control over your Ki to be able to do that!"` (`:167`)
			Avisar(pl, $"voce nao tem controle de Ki suficiente pra isso: o {grau}o grau pede {custo:0.#} e voce tem {f.Ki:0}.");
			return;
		}

		if (!ligado && OcupanteDoSlotG1(pl, id) is { } ocupado)
		{
			Avisar(pl, $"voce ja esta sustentando {ocupado} -- so da para manter um buff de corpo por vez.");
			return;
		}

		double poder = PoderDoGrauG11[grau];
		double physoff = poder;
		double physdef = 1 + (poder - 1) / 2;
		double speed = 1 - 1 / (1 + (poder - 1) / 2);

		if (ligado) DesligarBuff(pl, id);   // o grau velho sai EXATO, e so entao o novo entra
		LigarBuff(pl, id, "Expansao do Corpo", new Dictionary<string, double>
		{
			["Tphysoff"] = physoff,
			["Tphysdef"] = physdef,
			["Tspeed"] = -speed,
		});
		f.expandlevel = grau;
		f.Ki -= custo;
		_relogioDoExpandG11[pl.Id] = 0;

		Avisar(pl, grau switch
		{
			1 => "voce expande os musculos ao 1o grau!",
			2 => "voce expande os musculos ao 2o grau!",
			_ => "voce expande os musculos ao 3o grau!",
		});
		GD.Print($"[G11] {pl.Name} expandiu o corpo ao grau {grau} (custou {custo:0.#})");
	}

	// =====================================================================
	// 5. MAJIN -- `Magic/Majin.dm:9-50`
	// =====================================================================
	/// <summary>`MajinMod = 1.2` (`Majin.dm:2`): o unico numero da forma.</summary>
	private const double MajinModG11 = 1.2;

	/// <summary>`sleep(20)` do `majining` (`Majin.dm:17`): dois segundos entre um toggle e outro.</summary>
	private const long EsperaDoMajinMsG11 = 2000;

	/// <summary>
	/// A FORMA MAJIN -- liga/desliga em SI MESMO o `/obj/buff/Majin` (`Majin.dm:25-50`). E um buff de
	/// FORMA (`slot=sFORM`), entao convive com os buffs de corpo -- e NAO marca ninguem: a etiqueta
	/// "majinizacao: a marca do M em outro jogador" que o censo dava a este verb estava errada (o
	/// `obj/Majinize` que marca os outros esta COMENTADO no DM, `demon.dm:131-150`).
	///
	/// O QUE O `Buff()` FAZ (`:33-38`), e o que o `DeBuff()` desfaz (`:43-47`):
	///     MajinPcnt   = MajinMod              (1 -> 1,2: aqui soma +0,2)
	///     BPadd      += BP * MajinMod * (MaxAnger/100) / 10
	///     angerMod   /= 1.2
	///     physoffMod *= 1.3
	///     kiregenMod += 0.5
	/// O `BPadd` e o que da a forma o sabor de "poder fixo": ele e uma SOMA de zeni cru, calculada
	/// com o BP DO INSTANTE em que a forma ligou -- ficar mais forte com ela de pe nao a reforca, e
	/// e por isso que o alicerce guardar o numero aplicado e o unico jeito de desliga-la sem sobra.
	/// </summary>
	private void MajinG11(ServerPlayer pl)
	{
		const string id = "Majin";
		if (EmEspera(pl, _prontoMajinG11, "espere um instante antes de mexer na forma de novo")) return;
		long agora = NowMs();
		_prontoMajinG11[pl.Id] = agora + EsperaDoMajinMsG11;

		if (DesligarBuff(pl, id))
		{
			Avisar(pl, "sua raiva se aquieta e voce deixa o poder Majin.", Jandirus.Net.Protocol.Fala.AvisoDeRaiva);
			return;
		}

		if (RecusarCaido(pl, "voce nao esta em condicoes de assumir a forma.")) return;

		Fighter f = pl.Ficha;
		double majinAdd = f.BP * MajinModG11 * (f.MaxAnger / 100) / 10;   // `Majin.dm:34`

		LigarBuff(pl, id, "Majin",
			new Dictionary<string, double>
			{
				["MajinPcnt"] = MajinModG11 - 1,
				["BPadd"] = majinAdd,
				["kiregenMod"] = 0.5,
			},
			fatores: new Dictionary<string, double>
			{
				["angerMod"] = 1 / 1.2,
				["physoffMod"] = 1.3,
			});
		Avisar(pl, "voce canaliza os proprios demonios na forma Majin!");
		GD.Print($"[G11] {pl.Name} virou Majin (+{majinAdd:0} de BPadd)");
	}

	// =====================================================================
	// 6. SHACKLE -- `Ki2.0/Debuffs.dm:47-80`
	// =====================================================================
	/// <summary>
	/// O GRILHAO: um debuff de VELOCIDADE com prazo -- a etiqueta "magia" que o censo lhe dava
	/// estava errada; ele e irmao da Paralysis (mesmo arquivo, mesma recarga `debuffCD`).
	///
	///     custo   900 * BaseDrain        (`:53`; o `if(Ki>=900)` de `:50` confere sem o BaseDrain --
	///                                     a mesma familia de defeito que o G5 e o G6 consertaram:
	///                                     aqui se confere o que se cobra)
	///     prazo   round(Ekiskill + kidebuffskill/10) segundos        (`:54`, um `sleep(10)` por unidade)
	///     debuff  max(1 / log10(Ekiskill * kidebuffskill / 10), 0.5)  (`:56`)
	///     piso    Tspeed nunca desce de 0,1 (`:57-61`)
	///     recarga Eactspeed * 10 tiques  (`:62`)
	///
	/// NO DM CADA GRILHAO E UM `spawn` INDEPENDENTE E ELES EMPILHAM (o piso de 0,1 e o unico teto).
	/// Cada aplicacao aqui ganha um id proprio (`Shackle:n`) pra que o alicerce guarde e desfaca
	/// cada uma separadamente, no prazo de cada uma -- um id so faria o segundo grilhao ser recusado
	/// e mudaria a tecnica.
	///
	/// `log10` DE ZERO E RUNTIME NO DM: com `kidebuffskill = 0` o verb estoura depois de cobrar o Ki.
	/// Aqui a conta cai no piso de 0,5 (o `max` que o proprio DM escreve pra o caso de log pequeno).
	/// </summary>
	private void GrilhaoG11(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (f.med || f.train) { Avisar(pl, "nao da pra moldar um debuff meditando ou treinando."); return; }
		if (RecusarCaido(pl)) return;
		if (_canais.ContainsKey(pl.Id)) { Avisar(pl, "voce ja esta com um raio na mao."); return; }
		if (EmEspera(pl, _debuffPronto, "voce ainda nao consegue moldar outro debuff")) return;

		ServerPlayer? alvo = Marcado(pl);
		if (alvo == null) { Avisar(pl, "voce precisa de um alvo marcado pra usar isto."); return; }

		double custo = 900 * f.BaseDrain();
		if (f.Ki < custo) { Avisar(pl, $"o grilhao pede {custo:0} de energia e voce tem {f.Ki:0}."); return; }

		f.Ki -= custo;
		f.kidebuffskill += 0.6;                            // `kidebuffcounter += 6` (`:52`)
		CreditarContador(pl, "kidebuffcounter", 6);

		double prazoS = DmMath.Round(f.Ekiskill + f.kidebuffskill / 10, 1);   // `:54`
		double x = f.Ekiskill * f.kidebuffskill / 10;
		double l = Math.Log10(Math.Max(x, 1e-9));
		double debuff = l > 0 ? Math.Max(1 / l, 0.5) : 0.5;   // `:56`; log <= 0 vira o piso

		// `if(target.Tspeed - debuff > 0.1) ... else debuff = target.Tspeed - 0.1` (`:57-61`)
		if (alvo.Ficha.Tspeed - debuff <= 0.1) debuff = alvo.Ficha.Tspeed - 0.1;

		_debuffPronto[pl.Id] = NowMs() + (long)(Math.Max(f.Eactspeed * 10, 2) * TempoDoDm.MsPorTique);
		GanhoDeKiNoAlvo(pl, alvo, 4);   // `:69-72` -- o grilhao ja pegou o alvo: paga na hora, com o peso da luta

		string id = $"Shackle:{++_seqShackleG11}";
		long ms = (long)(Math.Max(prazoS, 1) * 1000);
		LigarBuff(alvo, id, "Grilhao", new Dictionary<string, double> { ["Tspeed"] = -debuff }, duracaoMs: ms);

		Avisar(pl, $"voce prende as pernas de {alvo.Name} por {prazoS:0}s (-{debuff:0.##} de velocidade).");
		Avisar(alvo, $"{pl.Name} prende suas pernas numa aura de interferencia: voce fica mais lento por {prazoS:0}s.");
		GD.Print($"[G11] {pl.Name} grilhou {alvo.Name} (-{debuff:0.##} Tspeed por {prazoS:0}s)");
	}

	/// <summary>
	/// Os doze do Kai Kai (`kai.dm:117`): onze mundos mais o Ceu. Desde 2026-10-23 sao a primeira parte da tela do Kai Kai (os mundos
	/// gerados que algum jogador ja pisou vem depois) -- ver `GameServer.KaiKai.cs`.
	/// </summary>
	private static readonly string[] DestinosDoKaiKaiG11 =
	[
		"Earth", "Namek", "Vegeta", "Icer", "Arconia", "Desert", "Arlia",
		"Large_Space_Station", "Small_Space_Station", "Afterlife", "Hell", "Heaven",
	];

	/// <summary>Os onze do Devil Bringer (`demon.dm:174`): a MESMA lista sem o Ceu -- e nao uma segunda copia dela.</summary>
	private static readonly string[] DestinosDoDevilBringerG11 = [.. DestinosDoKaiKaiG11.Where(d => d != "Heaven")];

	/// <summary>
	/// O DEVIL BRINGER -- o salto de planeta com carona do demonio (`demon.dm:174-201`), sobre o <see cref="SaltarDePlaneta"/> que o
	/// RiftTeleport e o Atalho Sagrado tambem usam: a porta (`:176` muda, `:177` fala: "You need full ki and total concentration"),
	/// o preco `usr.Ki = 0` (`:181`), a carona `for(var/mob/V in oview(1))` (`:185-194`) e a chegada no spawn (o `GotoPlanet` do DM
	/// cai no spawn do planeta tambem). O som (`demonteleport.wav`) vai como efeito pro cliente pelo id do verb.
	///
	/// ERA TAMBEM O CORPO DO KAI KAI (`kai.dm:112-139` e copia deste), ate o Kai Kai ganhar a tela dos planetas conhecidos e o clarao
	/// do Instant Transmission (2026-10-23, `GameServer.KaiKai.cs`). O Devil Bringer nao foi pedido e fica como era.
	/// </summary>
	private void DevilBringerG11(ServerPlayer pl, string destino)
	{
		if (RecusarPelaMenteFragmentada(pl)) return;
		// `demon.dm` nao tem o Ceu na lista, e a recusa e propria: um poder demoniaco nao entra la.
		if (destino.Equals("Heaven", StringComparison.OrdinalIgnoreCase))
		{
			Avisar(pl, "um poder demoniaco como este nao entra no Ceu.");
			return;
		}

		// O "parece estar se concentrando..." (`:180`) e o rasgo sao um emote so: no DM havia um `sleep` entre os dois, e sem ele as
		// duas frases sairiam no mesmo instante de qualquer jeito.
		if (!SaltarDePlaneta(pl, destino, DestinosDoDevilBringerG11, "o poder demoniaco", "Devil_Bringer",
							 kiCheio: true, comCarona: true,
							 emote: "parece estar se concentrando... rasga um buraco na realidade e some!",
							 fraseDaCarona: "te leva junto no teleporte.", pontoDeChegada: null,
							 out string escolhido, out int levou))
			return;

		MandarEfeito(pl, "devilbringer", RecargaDoSaltoMs);
		Avisar(pl, levou == 0
			? $"voce reaparece em {escolhido}, sem uma gota de energia."
			: $"voce reaparece em {escolhido} com {levou} pessoa{(levou == 1 ? "" : "s")} a tiracolo, sem uma gota de energia.");
		GD.Print($"[G11] {pl.Name} usou Devil_Bringer ate {escolhido} (+{levou} de carona)");
	}

	// =====================================================================
	// 8. INSTANT TRANSMISSION -- o corpo mudou pra GameServer.Transmissao.cs (2026-10-21).
	// O registro do verbo, a classe TransmissaoG11, o tique e o PontoAoLadoG11 ficaram aqui.
	// =====================================================================

	private Vec2 PontoAoLadoG11(ServerPlayer alvo)
		=> PontoLivre(alvo.Zone, new Vec2(alvo.Pos.X + ZoneCollision.TileSize, alvo.Pos.Y)) ?? alvo.Pos;

	// =====================================================================
	// 9. FLIP -- `Martial Skill Attacks.dm:289-323`
	// =====================================================================
	/// <summary>
	/// A CAMBALHOTA: preso num agarrao, uma tentativa de escapar paga em Ki.
	///
	///     custo    Ephysoff * 12 * BaseDrain     (`:291`)
	///     recarga  basicCD += 5  (meio segundo)  (`:293`)
	///     chance   (Ephysoff * expressedBP * 4) / grabberSTR
	///              * stamina/maxstamina * grabber.maxstamina/grabber.stamina
	///              * 2 se carregado (grabMode 2), * 6 se Majin      (`:298-303`)
	///     e quem segura CANSA: `grabber.stamina -= min(0.10*chance, 2)` (`:302`)
	///     sorteio  prob(chance * grabCounter)   (`:304`) -- na primeira tentativa o contador e 0
	///              e a chance e ZERO: a cambalhota so vinga depois de se debater
	///     escapou  dano em quem segurava: NormDamageCalc(grabber) + grabCounter (`:315-316`)
	///     falhou   grabCounter += 5  (`:320`)
	///
	/// O `if(!grabbee) grabParalysis = 0` de `:296` (o proprio flipper nao estar segurando ninguem
	/// zera a PROPRIA paralisia de agarrao, antes de qualquer sorteio) nao tem como existir aqui: o
	/// port guarda "estou preso" num campo so (`AgarradoPorId`), e nao ha meia-liberdade pra dar.
	/// </summary>
	private void CambalhotaG11(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		// SEM `canfight` (`:290` nao o pergunta): preso E atordoado ainda se debate -- a cambalhota e a
		// unica saida de quem esta nos bracos de alguem. `basicCD += 5`: meio segundo.
		if (!AbrirPunhoG7(pl, 12, 500, out _, exigirCanfight: false)) return;

		if (pl.AgarradoPorId == 0) { Avisar(pl, "voce da uma cambalhota no lugar -- ninguem estava te segurando."); return; }

		ServerPlayer? quem = QuemMeSegura(pl);
		if (quem == null)
		{
			LimparPreso(pl);   // o aperto ja tinha sido desfeito do outro lado
			Avisar(pl, "voce se sacode e percebe que ninguem te segurava mais.");
			return;
		}

		double str = Math.Max(pl.ForcaDeQuemMeSegura, 1e-9);
		double chance = f.Ephysoff * f.expressedBP * 4 / str;
		chance *= f.maxstamina > 0 ? f.stamina / f.maxstamina : 1;
		chance *= quem.Ficha.stamina > 0 ? quem.Ficha.maxstamina / quem.Ficha.stamina : 1;
		if (quem.ModoDoAgarrao == ModoDeAgarrao.Carregando) chance *= 2;
		quem.Ficha.stamina = Math.Max(0, quem.Ficha.stamina - Math.Min(0.10 * chance, 2));
		if (string.Equals(f.Race, "Majin", StringComparison.OrdinalIgnoreCase)) chance *= 6;

		if (DmMath.Prob(_rng, chance * pl.ContadorDaLuta))
		{
			double dano = CombatMath.DanoBase(f, quem.Ficha) + pl.ContadorDaLuta;
			Soltar(quem, MotivoDaSoltura.Escapou);
			// `damage_mob(grabber,dmg)` (`Martial Skill Attacks.dm:316`): o golpe e o de quem se solta -- nao-letal, para no piso.
			Espalhar(quem, dano, pl.Combate?.Letal == true);
			AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} se solta do aperto de {quem.Name} com uma cambalhota!");
			GD.Print($"[G11] {pl.Name} escapou de {quem.Name} pelo Flip (chance {chance:0.##} x {pl.ContadorDaLuta:0})");
			return;
		}

		pl.ContadorDaLuta += 5;
		AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} se debate contra o aperto de {quem.Name}!");
	}

	// =====================================================================
	// 10. SELF DESTRUCT -- `Ki/misc.dm:166-270`
	// =====================================================================
	/// <summary>`spawn(25)` entre dois incrementos do `chargecounter` (`misc.dm:257-259`).</summary>
	/// <summary>`if(chargecounter>20) if(prob(75))` (`misc.dm:210-211`).</summary>
	private const int CargaLetalDaAutodestruicaoG11 = 20;
	private const double ChanceDeMorrerNaAutodestruicaoG11 = 75;

	/// <summary>
	/// A AUTODESTRUICAO -- duas fases, e ela EXIGE ALGUEM AGARRADO (`if(!grabbee) "You need to be
	/// grabbing someone!"`, `:223-225`): e a Final Explosion de quem vai junto com o inimigo nos bracos.
	///
	/// PRIMEIRO APERTO (`:231-270`): `chargecounter = 1`, `move = 0` (aqui: ancora), duas narracoes
	/// (2 s e 6 s) e, a cada 2,5 s, `chargecounter += 5` enquanto houver agarrado e o corpo estiver de
	/// pe; perder o agarrado ou cair cancela ("lost control of the situation").
	///
	/// SEGUNDO APERTO (`:171-222`):
	///     power = (expressedBP*Ekioff) / (Mz.expressedBP*Mz.Ekidef) * 5 * chargecounter
	///     nos outros a ate 3 tiles:  SpreadDamage(power / dist)  (dist 0 e runtime no DM; piso 1, como o G3)
	///     em si:                      SpreadDamage(power)   -- SEM checagem de morte (`:199`)
	///     Ki = 0
	///     no agarrado:                SpreadDamage(power) + morte se HP <= 0  (`:201-209`)
	///     com carga > 20:             prob(75) de o proprio usuario morrer  (`:210-214`)
	/// O agarrado e os outros entram LETAIS (o DM imprime "was killed by" e chama `Death()`); o
	/// proprio usuario entra NAO-letal, porque o DM so o mata pelo sorteio da carga -- nunca pelo dano.
	///
	/// O `usr.DeathRegen` que divide o poder (`:186-187`, a regeneracao de morte do Majin) nao existe
	/// neste port; sem ele o divisor e 1, que e o caso de todo mundo que nao e Majin. `sdingtype == 2`
	/// e a Final Explosion carregando (`_cargaG3`): "You can't use Final Explosion with this".
	/// </summary>
	private void AutodestruirG11(ServerPlayer pl)
	{
		if (_cargaG3.TryGetValue(pl.Id, out CargaG3? carga))
		{
			// `sdingtype == 2` e a Final Explosion carregando: "You can't use Final Explosion with this"
			if (carga.Verbo != "Self_Destruct") { Avisar(pl, $"voce nao consegue usar isto junto com a {carga.Nome}."); return; }
			DetonarG11(pl, carga);
			return;
		}

		if (pl.AgarrandoId == 0) { Avisar(pl, "voce precisa estar agarrando alguem!"); return; }
		if (pl.Ficha.KO) { Avisar(pl, "voce nao consegue fazer isso caido!"); return; }
		if (pl.Ficha.dead) { Avisar(pl, "morto nao usa isto!"); return; }

		// A CARGA E A MESMA DA FINAL EXPLOSION (a classe, o dicionario e o pulso de 10 Hz): o que e desta
		// e o passo de 5 (`chargecounter += 5`, `:259`), o limiar de 20, e cair ao soltar o agarrao.
		long agora = NowMs();
		_cargaG3[pl.Id] = new CargaG3
		{
			Verbo = "Self_Destruct", Nome = "autodestruicao",
			Contador = 1, Passo = 5, Limiar = CargaLetalDaAutodestruicaoG11,
			AvisoLetal = "a carga passou de vinte: detonar agora tem 75% de chance de te levar junto.",
			PrecisaAgarrar = true,                // `if(sding && (!grabbee || KO))` -> "lost control" (`:238-246`, `:263-268`)
			AoCair = CancelarAutodestruicaoG11,
			Ancora = pl.Pos,                      // `move = 0` (`:237`) virou ancora, como a Final Explosion
			ComecouMs = agora, ProximoMs = agora + CargaPassoMs,
		};
		LigarPulso();
		MandarEfeito(pl, "carga_final", -1);
		Avisar(pl, "voce comeca a carregar a sua autodestruicao!");
		Avisar(pl, "aperte Self Destruct de novo pra detonar. Quanto mais carregar, mais forte a explosao.");
		GD.Print($"[G11] {pl.Name} comecou a carregar a autodestruicao");
	}

	private void CancelarAutodestruicaoG11(ServerPlayer pl)
	{
		_cargaG3.Remove(pl.Id);
		MandarEfeito(pl, "carga_final", 0);
		AvisarPertoG3(pl, 20 * ZoneCollision.TileSize, $"{pl.Name} perde o controle da situacao.");
	}

	private void DetonarG11(ServerPlayer pl, CargaG3 carga)
	{
		_cargaG3.Remove(pl.Id);
		MandarEfeito(pl, "carga_final", 0);

		ServerPlayer? mz = QuemEuSeguro(pl);
		if (mz == null)
		{
			AvisarPertoG3(pl, 20 * ZoneCollision.TileSize, $"{pl.Name} perde o controle da situacao.");
			return;
		}

		Fighter f = pl.Ficha;
		double baixo = Math.Max(mz.Ficha.expressedBP * mz.Ficha.Ekidef, 1e-9);
		double power = f.expressedBP * f.Ekioff / baixo * (5 * carga.Contador);   // `:184-185`

		AvisarPertoG3(pl, 20 * ZoneCollision.TileSize, $"{pl.Name} esta explodindo!!");
		MandarEfeito(pl, "explosao_final", 600);

		int pegos = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o == mz || EhCadaver(o) || o.Combate == null) continue;
			double distTiles = Vec2.Distance(o.Pos, pl.Pos) / ZoneCollision.TileSize;
			if (distTiles > 3) continue;
			EspalharDanoG3(o, pl, power / Math.Max(distTiles, 1), letal: true, porKi: true);
			MandarEfeito(o, "explosao_final", 600);
			pegos++;
		}

		EspalharDanoG3(pl, pl, power, letal: false);   // `usr.SpreadDamage(power)` sem `Death()` atras
		f.Ki = 0;
		EspalharDanoG3(mz, pl, power, letal: true, porKi: true);     // `Mz.SpreadDamage(power)` + `if(Mz.HP<=0) Mz.Death()`
		MandarEfeito(mz, "explosao_final", 600);

		bool morreu = false;
		if (carga.Contador > CargaLetalDaAutodestruicaoG11
			&& DmMath.Prob(_rng, ChanceDeMorrerNaAutodestruicaoG11)
			&& !f.dead && pl.Combate.Morrer())
		{
			morreu = true;
			AvisarPertoG3(pl, 20 * ZoneCollision.TileSize, $"{pl.Name} morre na propria autodestruicao!");
		}

		// `usr.grabbee=null; grabMode=0` (`:216-218`) -- a unica porta de soltura do port
		if (pl.AgarrandoId != 0) Soltar(pl, MotivoDaSoltura.Tecla);

		Avisar(pl, $"a explosao sai com carga {carga.Contador} (poder {power:0.#}) e pega {pegos} pessoa{(pegos == 1 ? "" : "s")} em volta.");
		GD.Print($"[G11] autodestruicao de {pl.Name}: carga {carga.Contador}, poder {power:0.#}, {pegos} em volta, morreu={morreu}");
	}

	// =====================================================================
	// 11. PSYCHO THREAD -- `heran.dm:162-169` e o `turf/Click` de `click.dm:1-43`
	// =====================================================================
	/// <summary>O toggle (`heran.dm:164-169`): alterna `psythre`.</summary>
	private void AlternarFioPsiquicoG11(ServerPlayer pl)
	{
		pl.Ficha.psythre = pl.Ficha.psythre > 0 ? 0 : 1;
		Avisar(pl, pl.Ficha.psythre > 0
			? "Fio Psiquico LIGADO: o duplo clique no chao passa a armar um fio embaixo de voce."
			: "Fio Psiquico desligado: o duplo clique volta a ser o Zanzoken.");
	}

	/// <summary>
	/// O FIO NO CLIQUE DO CHAO -- chamado do topo do `Zanzoken` (`GameServer.Zanzoken.cs`), que e o
	/// unico "clique no chao" que o cliente manda. Devolve true quando consumiu o clique.
	///
	/// ============================ O QUE O CLICK DO DM FAZ DE VERDADE (`click.dm:3-43`) ============================
	/// Nao e um tiro na direcao do clique. O blast nasce em `locate(usr.x, usr.y, usr.z)` (`:26`) --
	/// o tile DO PROPRIO USUARIO --, com `A.dir = usr.dir` e NENHUM `walk()` (compare com a Paralysis,
	/// `Debuffs.dm:37`, que anda). Ele fica parado onde nasceu ate o `Burnout()` padrao de 50 tiques
	/// (`objects.dm:655`) explodi-lo: e uma ARMADILHA de paralisia de cinco segundos aos pes de quem
	/// clicou, exatamente o que a skill descreve ("click on the ground to place them").
	///
	/// OS NUMEROS: o DM entrava com `Ki >= 700*BaseDrain` e cobrava `Ki -= 100*BaseDrain` (`:5` e `:8`)
	/// -- conferia SETE vezes o que cobrava. Por decisao do dono (2026-09-02, "corrija esses bugs q vc
	/// citou") a porta e o custo sao o MESMO numero, o real: `100*BaseDrain` (a familia "conferia X e
	/// cobrava Y" que o G5/G6/G7 ja fecharam). `kidebuffcounter += 5`, `BP = expressedBP *
	/// log(11, max(kidebuffskill, 10))` (`:29`, base ONZE, como o Stunlock), `deflectable = 0`,
	/// `paralysis = 1`, `basedamage = 0.1`, quatro `Blast_Gain()` e `debuffCD = Eactspeed*6`.
	/// O `sleep(Eactspeed)` de preparo (`:16`) nao veio, como no G5.
	/// ==================================================================================================
	///
	/// NO DM O CLIQUE SIMPLES E O FIO E O DUPLO E O ZANZOKEN, e os dois convivem. Este port so tem o
	/// duplo clique; o toggle decide qual dos dois ele e. Um Heran que quer o Zanzoken desliga o fio.
	/// </summary>
	private bool FioPsiquicoNoCliqueG11(ServerPlayer pl, Vec2 destino)
	{
		Fighter f = pl.Ficha;
		if (f.psythre <= 0 || pl.Livro?.Sabe(SkillPsychoThreadG11) != true) return false;

		// `if(!usr.med&&!usr.train)` -- meditando, o clique nao faz nada (e nao vira Zanzoken)
		if (f.med || f.train) { Avisar(pl, "com o fio ligado, meditando ou treinando o clique nao arma nada."); return true; }
		if (Caido(pl)) return true;
		double custo = 100 * f.BaseDrain();   // `:8`; a porta do DM (`:5`) pedia 700x -- ver o cabecalho
		if (f.Ki < custo) { Avisar(pl, $"o fio pede {custo:0} de Ki."); return true; }
		if (_canais.ContainsKey(pl.Id)) { Avisar(pl, "voce esta com um raio na mao."); return true; }
		if (EmEspera(pl, _debuffPronto, "voce ainda nao consegue armar outro fio")) return true;

		f.Ki -= custo;
		f.kidebuffskill += 0.5;
		CreditarContador(pl, "kidebuffcounter", 5);

		double mult = Math.Log(Math.Max(f.kidebuffskill, 10)) / Math.Log(11);
		Projetil p = Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 0.1,
			Velocidade = 1,
			AlcanceTiles = 200,      // quem apaga o fio e o prazo, nao o alcance -- ele nao anda
			Deflectivel = false,
			Paralisia = true,
			Empurra = false,         // sem `kiforceful`, como a Paralysis
			MultDeOnda = mult,
			Nome = "Fio Psiquico",
		}, rumoDado: Vec2.Zero, deOnde: pl.Pos, verbo: "Psycho_Thread");
		if (p.Vivo) p.VidaRestante = 5;   // `Burnout()` sem argumento: 50 tiques

		GanhoDeKi(pl, vezes: 4);   // no pacote do fio: so quando alguem pisa nele
		_debuffPronto[pl.Id] = NowMs() + (long)(Math.Max(f.Eactspeed * 6, 2) * TempoDoDm.MsPorTique);

		Avisar(pl, "voce arma um fio psiquico embaixo dos seus pes.");
		GD.Print($"[G11] {pl.Name} armou um Fio Psiquico em {pl.Pos}");
		return true;
	}

	// =====================================================================
	// 12. FREEZE -- `Misc/TimeStop.dm:5-25`
	// =====================================================================
	/// <summary>
	/// O CONGELAMENTO: todo mundo com cliente a vista fica `Frozen` por `(20*Ekiskill)/A.Ephysoff`
	/// decimos (`:22`); no `movement handler.dm:138` o `Frozen` zera o `mobTime` -- e o mesmo lugar
	/// da paralisia, entao aqui ele entra pela mesma peca (`_paralisadoAte`, com a fresta de 1 em 12
	/// que a paralisia do port tem e o Frozen do DM nao tinha).
	///
	/// ============================ O DEFEITO DO DM: METADE DO KI POR ALVO -- CONSERTADO ============================
	/// `usr.Ki *= 0.5` esta DENTRO do `for` (`:14-16`): quem congelava tres pessoas ficava com um oitavo
	/// do Ki. A descricao promete metade; por decisao do dono (2026-09-02, "corrija esses bugs q vc
	/// citou") a metade sai UMA vez, depois do laco, e so se alguem foi congelado -- a porta continua
	/// `Ki <= MaxKi*0.25` (`:7`), e sem ninguem a vista continua nao cobrando nada (como no DM).
	/// ==========================================================================================================
	///
	/// O `sleep(10)` entre um alvo e outro (`:15`) nao veio (despacho sincrono): todos congelam no
	/// mesmo instante. `!A.Frozen` -- quem ja esta com as pernas trancadas nao renova e nao paga.
	/// </summary>
	private void CongelarG11(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		if (RecusarCaido(pl)) return;
		if (f.Ki <= f.MaxKi * 0.25) { Avisar(pl, "voce nao consegue congelar o tempo com tao pouco Ki!"); return; }

		int pegos = 0;
		long agora = NowMs();
		foreach (ServerPlayer a in ZoneList(pl.Zone.Hash).ToList())
		{
			if (a == pl || !EhPessoa(a) || EhCadaver(a)) continue;
			if (Vec2.Distance(a.Pos, pl.Pos) > RaioDaVista) continue;
			if (_paralisadoAte.ContainsKey(a.Id)) continue;   // `!A.Frozen`

			long ms = (long)(20 * f.Ekiskill / Math.Max(a.Ficha.Ephysoff, 1e-9) * 100);
			_paralisadoAte[a.Id] = agora + ms;
			MandarEfeito(a, "paralisia", ms);
			Avisar(a, $"{pl.Name} congela o tempo em volta de voce: suas pernas param ({ms / 1000.0:0.#}s).");
			pegos++;
		}
		if (pegos > 0) f.Ki *= 0.5;   // UMA vez (o `:16` cobrava dentro do `for`) -- ver o cabecalho

		MandarEfeito(pl, "timefreeze", 1000);
		Avisar(pl, pegos == 0
			? "voce congela o tempo... e nao havia ninguem pra sentir."
			: $"voce congela o tempo: {pegos} pessoa{(pegos == 1 ? "" : "s")} para{(pegos == 1 ? "" : "m")}. Seu Ki: {f.Ki:0}.");
		GD.Print($"[G11] {pl.Name} congelou {pegos} pessoa(s)");
	}

	// =====================================================================
	// 13. OBSERVE -- `observe.dm:1-14`
	// =====================================================================
	/// <summary>
	/// OBSERVAR: no DM e uma camera (`client.eye = M`, `client.perspective = EYE_PERSPECTIVE`) que vale
	/// pra qualquer distancia e qualquer andar. Este port NAO TEM OLHO REMOTO -- o corte de interesse
	/// do servidor e por ZONA (so quem compartilha o hash recebe snapshot), e o `nave_observar` ja
	/// escolheu, por escrito (`GameServer.NaveGrande.ObservarDaPonte`), responder a pergunta que a
	/// camera responderia com TEXTO em vez de abrir um segundo canal de snapshot. Este verb segue o
	/// mesmo molde: diz ONDE a pessoa esta, COMO esta e QUEM esta em volta dela.
	///
	/// AS TRES RECUSAS SAO AS DA TELEPATIA (`:9`): quem esconde o poder, Android e `expressedBP <= 5`
	/// -- "You can't find their energy!" (o `AchoAEnergiaG4`). Observar a si mesmo, ou mandar sem
	/// nome, solta (`:4-8`: `M == usr` reseta a perspectiva). O `observingnow` e o campo que o
	/// `Advanced_Ki_Awareness` le pra render mais exp (`Mind.dm:461-462`) -- e desde o lote G13 o
	/// motor de niveis AVALIA essa condicao (`RegraDeNivel.Estado.Observando`): projetar a mente
	/// treina Percepcao de Ki a 3 por tique em vez de 1.
	/// </summary>
	private void ObservarG11(ServerPlayer pl, string arg)
	{
		if (arg.Length == 0 || string.Equals(arg, pl.Name, StringComparison.OrdinalIgnoreCase))
		{
			if (pl.Ficha.observingnow > 0)
			{
				pl.Ficha.observingnow = 0;
				Avisar(pl, "voce volta a olhar pelos proprios olhos.");
				return;
			}
			var nomes = new List<string>();
			foreach (ServerPlayer o in _players.Values)
				if (o != pl && EhPessoa(o) && AchoAEnergia(pl, o)) nomes.Add(o.Name);
			Avisar(pl, nomes.Count == 0
				? "nao ha nenhuma energia que de pra seguir agora."
				: $"energias que voce consegue seguir: {string.Join(", ", nomes)}. Use Observe:<nome>.");
			return;
		}

		ServerPlayer? alvo = null;
		foreach (ServerPlayer o in _players.Values)
			if (EhPessoa(o) && string.Equals(o.Name, arg, StringComparison.OrdinalIgnoreCase)) { alvo = o; break; }
		if (alvo == null) { Avisar(pl, $"nao ha ninguem chamado '{arg}' no mundo agora."); return; }
		if (!AchoAEnergia(pl, alvo)) { Avisar(pl, "voce nao consegue achar a energia dessa pessoa!"); return; }   // (2026-10-18) e o ki divino

		pl.Ficha.observingnow = 1;
		EstadoDoCeu ceu = CeuDe(alvo);
		Fighter f = alvo.Ficha;
		string condicao = f.dead ? "MORTO" : f.KO ? "caido" : f.IsInFight ? "em combate" : f.med ? "meditando" : f.train ? "treinando" : "de pe";

		Avisar(pl, $"-- voce projeta a mente ate {alvo.Name} --");
		Avisar(pl, $"  esta em {alvo.Zone.Name}, tile ({alvo.Pos.X / ZoneCollision.TileSize:0}, {alvo.Pos.Y / ZoneCollision.TileSize:0}), "
				 + $"{Ceu.NomeDaHora(ceu.Hora)}{(alvo.Voando ? ", voando" : "")}.");
		Avisar(pl, $"  condicao: {condicao}; vida {f.HP:0}%, Ki {(f.MaxKi > 0 ? f.Ki / f.MaxKi * 100 : 0):0}%.");

		var perto = new List<string>();
		foreach (ServerPlayer o in ZoneList(alvo.Zone.Hash))
			if (o != alvo && !EhCadaver(o) && Vec2.Distance(o.Pos, alvo.Pos) <= RaioDaVista) perto.Add(o.Name);
		Avisar(pl, perto.Count == 0 ? "  nao ha ninguem em volta." : $"  em volta: {string.Join(", ", perto)}.");
		Avisar(pl, "  (Observe sem nome solta a projecao.)");
	}

	// =====================================================================
	// 14. UNLOCK POTENTIAL -- `Magic/UnlockPotential.dm:10-52`
	// =====================================================================
	/// <summary>Um minuto pra responder a oferta -- o mesmo prazo do `Restore_Youth` (G8).</summary>
	private const long PrazoDaOfertaDePotencialMsG11 = 60_000;

	/// <summary>
	/// A OFERTA: o verb do DM abre `input("Who?") as mob in view(1)` e depois pergunta AO ALVO se ele quer (`:12-19`). Aqui o alvo e o
	/// marcado a um tile, e a resposta vem pelos verbs `potencial_aceitar`/`potencial_recusar` da aba Other (o molde do `Restore_Youth`).
	///
	/// (2026-10-26) SO EM OUTRA PESSOA, como no DU (`if(!A || A == usr) return`, `Skills.dm:2281`) -- o dono: *"player pra player 1x, e o
	/// jogador pode aceitar ou recusar o potencial recebido de outro player"*. O proprio banco se solta pelos outros liberadores; e o que
	/// impediria a oferta (o androide de absorcao, a recarga, o banco vazio) e dito a quem oferece, antes de o alvo ser perguntado.
	/// </summary>
	private void OferecerPotencialG11(ServerPlayer pl)
	{
		if (RecusarCaido(pl)) return;

		ServerPlayer? alvo = Marcado(pl);
		if (alvo == null || alvo == pl)
		{
			Avisar(pl, "o Unlock Potential se usa em OUTRA pessoa: marque quem vai receber, a um tile de você.");
			return;
		}
		if (Vec2.Distance(alvo.Pos, pl.Pos) > RaioDeUmTileG4)
		{
			Avisar(pl, $"{alvo.Name} precisa estar ao seu lado (um tile).");
			return;
		}
		if (!EhPessoa(alvo)) { Avisar(pl, "so gente tem potencial adormecido pra despertar."); return; }
		if (PorQueNaoLibera(alvo, comRecarga: true, proprio: false) is { } recusa) { Avisar(pl, recusa); return; }

		_ofertasDePotencialG11[alvo.Conta] = (pl.Conta, NowMs() + PrazoDaOfertaDePotencialMsG11);
		_ofertasDeRitual.Remove(alvo.Conta);   // (2026-10-23) um convite so por alvo: o do ritual usa este cartao (RitualDoKaioshin.cs)
		Avisar(pl, $"voce oferece a {alvo.Name} despertar o potencial adormecido. Agora e com {alvo.Name}.");
		Avisar(alvo, $"{pl.Name} quer despertar os seus poderes escondidos. "
				   + $"(aceite ou recuse na aba Other, em {PrazoDaOfertaDePotencialMsG11 / 1000}s)");
	}

	/// <summary>A resposta a oferta -- chamada do roteador dos cargos (`potencial_aceitar` / `potencial_recusar`).</summary>
	private void ResponderPotencialG11(ServerPlayer pl, bool aceitou)
	{
		// (2026-10-23) O CONVITE DO RITUAL MISTICO e respondido pelos MESMOS botoes: aceitar comeca o ritual. Ver `RitualDoKaioshin.cs`.
		if (ResponderAoRitual(pl, aceitou)) return;
		if (!_ofertasDePotencialG11.TryGetValue(pl.Conta, out (string Quem, long Ate) o) || NowMs() > o.Ate)
		{
			_ofertasDePotencialG11.Remove(pl.Conta);
			Avisar(pl, "ninguem te ofereceu despertar potencial nenhum (ou a oferta ja venceu).");
			return;
		}
		_ofertasDePotencialG11.Remove(pl.Conta);

		ServerPlayer? ofertante = OnlinePorConta(o.Quem);
		if (!aceitou)
		{
			Avisar(pl, "voce recusa a oferta.");
			if (ofertante != null) Avisar(ofertante, $"{pl.Name} recusou a sua oferta.");
			return;
		}
		LiberarOPotencial(pl, ofertante ?? pl, GainKnobs.LiberaOJogador, autor: ofertante?.Name ?? o.Quem);
	}

	/// <summary>
	/// POR QUE O BANCO DESTE CORPO NAO SE SOLTA AGORA -- nulo = solta. <paramref name="proprio"/> diz se a frase vai pro dono do corpo
	/// ("o seu") ou pra quem tenta nele ("o de Fulano"). Ver <see cref="LiberarOPotencial"/>.
	/// </summary>
	private string? PorQueNaoLibera(ServerPlayer alvo, bool comRecarga, bool proprio, bool exigeBanco = true, bool upgradeDeAndroide = false)
	{
		Fighter f = alvo.Ficha;
		// O ANDROIDE DE ABSORCAO GUARDA, MAS NAO SOLTA POR ESTES CAMINHOS -- o dono: *"os androides de absorcao eles tem uma pool de hidden q
		// so libera com tech q da um 'upgrade' neles liberando esse potencial, nao pelos metodos tradicionais"*. O de energia infinita solta.
		// (2026-10-26) A PORTA DELE e o Android Upgrade (`upgradeDeAndroide`, o pedido 95c) -- ver `KitsDeAndroide.Upgrade`.
		if (f.AndroideAbsorcao && !upgradeDeAndroide && !AbsorcaoLiberaDeTeste)
			return proprio ? "o seu corpo de androide de absorção não libera potencial assim -- só um upgrade de tecnologia solta o que ele guarda."
						   : $"o corpo de androide de absorção de {alvo.Name} não libera potencial assim -- só um upgrade de tecnologia solta o que ele guarda.";
		long falta = f.liberarPotencialEm - NowMs();
		if (comRecarga && falta > 0)
			return proprio ? $"o seu potencial ainda se assenta da última liberação: espere {Jandirus.Core.Ranks.RitualDoKaioshin.HorasEMinutos(falta)}."
						   : $"o potencial de {alvo.Name} ainda se assenta da última liberação: espere {Jandirus.Core.Ranks.RitualDoKaioshin.HorasEMinutos(falta)}.";
		if (exigeBanco && !f.TemPotencialGuardado)
			return proprio ? "não há potencial guardado em você pra soltar -- ele se acumula treinando, meditando e lutando."
						   : $"não há potencial guardado em {alvo.Name} pra soltar -- ele se acumula treinando, meditando e lutando.";
		return null;
	}

	/// <summary>DEFEITO INJETADO (`--patriarcateste`): o androide de absorcao solta o banco pelos caminhos de sempre. Falso em jogo, sempre.</summary>
	internal static bool AbsorcaoLiberaDeTeste;

	/// <summary>
	/// (2026-10-26) SOLTAR O BANCO DO POTENCIAL -- o `unlockedBP += unusedPotential; unusedPotential = 0` do DU (`Skills.dm:2283-2285`), por
	/// QUALQUER liberador: o Unlock Potential de um jogador em outro (1x), o Grande Patriarca (1,25x), a Agua Ultra Divina (1x), o desejo ao
	/// dragao (1,5x) e o ritual do Kaioshin (2x) -- ver `GainKnobs.LiberaOGuru` e os outros. O que sai (o banco vezes o multiplicador) vai
	/// pro BP base e fica; o banco zera. Devolve se soltou.
	///
	/// AS RECUSAS (`PorQueNaoLibera`) sao ditas a <paramref name="quem"/> e nao gastam nada. <paramref name="comRecarga"/> pede e marca a
	/// hora entre duas liberacoes (`GainKnobs.RecargaDaLiberacaoMs`; o ritual e o desejo nao a pedem). A PRIMEIRA liberacao da vida ainda da
	/// o que o despertar do DM dava, uma vez (`UnlockPotential.dm:21-52`): o marco "potential" (todo ganho x1,5), +0,4 de Ki skill e a
	/// pele do Namekuseijin (`Catalogo.PotencialMarcaAPele`).
	///
	/// O QUE SAIU DO DESPERTAR DE ANTES: `BP * 0,25 * max(Potencial da raca, 1)` (1,8x na classe Prodigial) e mais metade do potencial
	/// oculto, cada um pelo `CapCheck` -- o ganho lia o PROPRIO BP, e a quem tinha bilhoes dava bilhoes (o relato do dono de 2026-10-25). O
	/// Prodigial ficou com o banco que enche 1,5x (`GainKnobs.BancoDoProdigial`); a raca, sem bonus.
	/// </summary>
	private bool LiberarOPotencial(ServerPlayer alvo, ServerPlayer quem, double vezes, string? autor = null, bool comRecarga = true,
								   bool exigeBanco = true, bool upgradeDeAndroide = false)
	{
		// (2026-10-27) O POTENCIAL NAO SE LIBERA ENTRE PERSONAGENS DO MESMO IP (o pedido 110 -- o dono tirou ate as coisas de profissao). Ver
		// `GameServer.MesmoIp.cs`.
		if (alvo != quem && MesmoIp(alvo, quem)) { Avisar(quem, FraseDoMesmoIp); return false; }
		if (PorQueNaoLibera(alvo, comRecarga, proprio: alvo == quem, exigeBanco, upgradeDeAndroide) is { } recusa)
		{
			Avisar(quem, recusa);
			if (quem != alvo && PorQueNaoLibera(alvo, comRecarga, proprio: true, exigeBanco, upgradeDeAndroide) is { } naVozDele) Avisar(alvo, naVozDele);
			return false;
		}
		Fighter f = alvo.Ficha;
		double antes = f.BP;
		double banco = Math.Max(f.hiddenpotential, 0);
		f.BP += banco * vezes;
		f.hiddenpotential = 0;
		if (comRecarga) f.liberarPotencialEm = NowMs() + GainKnobs.RecargaDaLiberacaoMs;

		bool primeira = f.unlockPotential < 1;
		double marco = 0;
		if (primeira)
		{
			f.unlockPotential = 1;
			marco = f.ReachMilestone("potential");
			f.kiskill += 0.4;
		}
		f.Statify();
		alvo.SigAtributos = "";
		MandarFicha(alvo);
		if (EhJogador(alvo)) Persistir(alvo);

		// (2026-10-13) O POTENCIAL DESPERTO APARECE NA PELE DO NAMEKUSEIJIN -- ver `Catalogo.PotencialMarcaAPele`.
		if (primeira) ReapresentarOCorpo(alvo);

		AvisarPertoG3(alvo, RaioDaVista, $"{autor ?? quem.Name} desperta o potencial de {alvo.Name}!");
		if (banco >= 1)
		{
			Avisar(alvo, $"o potencial que você guardou se liberta: {vezes:0.##}x o que estava guardado.");
			// O GANHO SE SENTE, NAO SE LE (a frase da Agua, 2026-10-16): o numero so com scouter; sem ele, quanto o corpo cresceu.
			Avisar(alvo, Jandirus.Core.Stats.PoderNaFrase.EmNumero(ScouterMostra(alvo, alvo, antes, f.BP))
				? $"!!* O seu PODER DE LUTA aumentou: {antes:N0} -> {f.BP:N0} *!!"
				: $"!!* Você se sente {Jandirus.Core.Stats.PoderNaFrase.QuantoMaisForte(antes, f.BP)} *!!");
		}
		if (marco > 0) Avisar(alvo, $"MARCO DE PODER! todo ganho agora e x{marco:0.##}.");
		GD.Print($"[potencial] {alvo.Name}: banco de {banco:0} solto {vezes:0.##}x por {autor ?? quem.Name}: +{banco * vezes:0}");
		return true;
	}

	// =====================================================================
	// 15. GIVE POWER -- `Misc/givepower.dm:28-53`
	// =====================================================================
	/// <summary>`gavepower = 100` (`:39`): cem tiques do laco de andar sem doar de novo -- dez segundos.</summary>
	private const long TravaDaDoacaoMsG11 = 10_000;

	/// <summary>
	/// DAR PODER -- o Heal do G6 ao contrario: em vez de vida, e a SUA energia que vai pro outro.
	///
	///     por ciclo (`sleep(2)`, 0,2 s):  M.Ki += MaxKi*0.01 ; Ki -= MaxKi*0.01 ; M.SpreadHeal(1)
	///                                     CooldownAmount += M.MaxKi / MaxKi        (`:42-47`)
	///     enquanto:                       givingpower && Ki >= 0.01*MaxKi
	///     no fim:                         spawn usr.KO()  -- quem doou DESMAIA (`:52`), tanto quando o
	///                                     Ki acaba quanto quando o proprio doador para
	///
	/// ============================ O `CooldownAmount` E UM DEFEITO VISIVEL DO DM ============================
	/// A linha `:47` soma `M.MaxKi / usr.MaxKi` no `CooldownAmount` DO DOADOR (o verb roda em `usr`),
	/// e `CooldownAmount` entra direto no BP expresso (`base.dm:109`) -- quem DA poder ganha um
	/// adendo de BP, que depois decai 0,1% por segundo ate cair abaixo de 1 (`PowerCooldown`,
	/// `base.dm:212-227`). O port tinha o campo e a soma no `PowerLevel`, mas nao o decaimento: sem
	/// ele o adendo seria eterno. Os dois vieram: a soma (o bug, com o numero visivel) e o
	/// decaimento (a regra que o limita).
	/// ==================================================================================================
	///
	/// O `got_power_count`/`count_got_power()` (`:49-51`) e a contabilidade do RITUAL DE GOD KI
	/// (`Godki/GodRitual.dm`), que este port nao tem -- fica catalogado. O alvo do DM e "player ou
	/// pet" (`input in oview(5)`); aqui e o marcado ou o jogador mais perto a cinco tiles.
	/// </summary>
	private void DarPoderG11(ServerPlayer pl)
	{
		if (_doacaoG11.TryGetValue(pl.Id, out DoacaoG11? emCurso))
		{
			emCurso.Parar = true;   // `givingpower = 0` -- o laco acaba e o KO vem no tique
			Avisar(pl, "voce para de transferir energia.");
			return;
		}

		// O `if(usr.cangivepower ...)` do DM (`:36`) NAO e conferido de novo aqui: a flag e escrita no
		// `after_learn` e apagada no `before_forget` (`givepower.dm:13,17`) -- ela e "sabe a skill", que o
		// `SabeTecnica` do despacho ja perguntou. (O campo continua chegando pelo extrator, como dado.)
		Fighter f = pl.Ficha;
		if (RecusarCaido(pl)) return;
		if (EmEspera(pl, _poderDadoAteG11, "seu corpo ainda se recompoe da ultima doacao")) return;
		long agora = NowMs();

		ServerPlayer? alvo = Marcado(pl) ?? AlvoDeTecnica(pl, 5 * ZoneCollision.TileSize);
		if (alvo == null || alvo == pl || !EhPessoa(alvo) || alvo.Combate == null)
		{
			Avisar(pl, "marque um jogador a ate cinco tiles pra receber a sua energia.");
			return;
		}
		if (f.Ki < 0.01 * f.MaxKi) { Avisar(pl, "voce nao tem energia nem pra uma dose."); return; }

		var doacao = new DoacaoG11 { Alvo = alvo.Id };
		_doacaoG11[pl.Id] = doacao;
		_poderDadoAteG11[pl.Id] = agora + TravaDaDoacaoMsG11;
		MandarEfeito(pl, "dando_poder", -1);
		AvisarPertoG3(pl, 12 * ZoneCollision.TileSize, $"{pl.Name} transfere a propria energia pra {alvo.Name}!!");
		// A PRIMEIRA DOSE SAI NO APERTO: o `while` do DM roda a primeira volta ANTES do primeiro
		// `sleep(2)` (`:41-48`); as seguintes vem do efetor, a cada 0,2 s.
		DoseDaDoacaoG11(pl, doacao, alvo);
		GD.Print($"[G11] {pl.Name} comecou a dar poder a {alvo.Name}");
	}

	/// <summary>Uma dose (`:42-47`): 1% do MaxKi do doador vai pro alvo, com `SpreadHeal(1)`, e o `CooldownAmount` do DOADOR sobe.</summary>
	private void DoseDaDoacaoG11(ServerPlayer pl, DoacaoG11 d, ServerPlayer alvo)
	{
		Fighter f = pl.Ficha;
		double dose = f.MaxKi * 0.01;
		alvo.Ficha.Ki += dose;
		f.Ki -= dose;
		d.Deu++;
		alvo.Combate!.Corpo.Curar(1);
		alvo.Combate.SincronizarVida();
		f.CooldownAmount += alvo.Ficha.MaxKi / Math.Max(f.MaxKi, 1e-9);   // `:47` -- ver o cabecalho
		// (2026-10-19) E A CORRENTE DO DU NA TELA DE QUEM ESTA PERTO: um folego por dose. Ver `GameServer.CorrenteDePoder.cs`.
		AnunciarACorrenteDePoder(pl, alvo);
	}

	/// <summary>O fim da doacao (`givepower.dm:49-53`): o doador desmaia.</summary>
	private void EncerrarDoacaoG11(ServerPlayer pl, DoacaoG11 d)
	{
		_doacaoG11.Remove(pl.Id);
		MandarEfeito(pl, "dando_poder", 0);
		if (!EhCadaver(pl) && !pl.Ficha.KO && pl.Combate != null)
			pl.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: false);   // `spawn usr.KO()`
		Avisar(pl, $"voce deu {d.Deu} dose{(d.Deu == 1 ? "" : "s")} de energia e desmaia de exaustao.");
		GD.Print($"[G11] {pl.Name} encerrou a doacao ({d.Deu} doses) e desmaiou");
	}

	// =====================================================================
	// 16. O EFETOR DO LOTE -- 5 Hz, o `effector()` de 0,2 s (`skill.dm:39-42`)
	// =====================================================================
	/// <summary>
	/// TUDO QUE E `effector()` NESTE LOTE roda aqui, no bloco de 5 Hz do servidor -- a MESMA cadencia
	/// do `spawn while(savant) { effector(); sleep(2) }` do DM. O que tem cadencia propria (a agua a
	/// 0,3 s, o Loop do Expand a 1 s, o decaimento do CooldownAmount a 1 s) acumula o dt e dispara no
	/// prazo -- a disciplina do `_relogioDoEstomago`. A carga da autodestruicao NAO mora aqui: ela e uma
	/// `CargaG3` e anda no pulso de 10 Hz do G3, onde os prazos dela estao escritos.
	/// </summary>
	private void TickDoEfetorG11()
	{
		double dt = TicksPorFicha / (double)TicksPorSegundo;
		long agora = NowMs();

		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			// QUEM TEM EFETOR: todo corpo com livro que nao e NPC do mundo nem eco (clone da mente, boneco
			// do corpo largado). No DM o `effector()` roda em qualquer mob com a skill; o crivo `EhJogador`
			// (que exige tela) barraria os corpos forjados da bancada, que e quem prova este arquivo.
			if (EhNpcDoMundo(pl) || pl.DonoDoClone != 0 || pl.DonoDoCorpoLargado != 0 || pl.Livro == null) continue;
			Fighter f = pl.Ficha;

			// --- os pendentes que existem mesmo com o corpo caido ---
			if (_sneakAteG11.TryGetValue(pl.Id, out long sneakAte) && agora >= sneakAte)
			{
				_sneakAteG11.Remove(pl.Id);
				if (_invisiveis.Remove(pl.Id))
				{
					f.isconcealed = false;
					MandarEfeito(pl, "invisivel", 0);
					Avisar(pl, "voce volta a ser visto.");
				}
			}

			if (_transmissaoG11.TryGetValue(pl.Id, out TransmissaoG11? t)) TickDaTransmissaoG11(pl, t, agora);
			if (_doacaoG11.TryGetValue(pl.Id, out DoacaoG11? d)) TickDaDoacaoG11(pl, d, agora);

			if (CaidoPelaMorte(pl)) continue;

			// (2026-10-16) O ZENKAI E A UNICA REGRA DE MORTO QUE FICA (dono: *"unica restriçao de morto deveria ser zenkai"*): o resto
			// deste laco (os astros do Makyo, o Time Store, a meditacao dos Grays, o metabolismo, o Expand) roda no morto de pe.
			if (!f.dead) ZenkaiEmCombateG11(pl, f);
			AstrosDoMakyoG11(pl, f);
			TimeStoreG11(pl, f);
			MeditacaoDosGraysG11(pl, f);
			MetabolismoPerfeitoG11(pl, f, dt);
			LoopDoExpandG11(pl, f, dt);
		}

		DecairCooldownAmountG11(dt);
	}

	// ---------------------------------------------------------------
	// 16a. HERAN POWER -- `heran.dm:66-80`
	// ---------------------------------------------------------------
	/// <summary>
	/// O ZENKAI EM COMBATE DO HERAN. Em luta e com alguem mais forte a vista, um contador sobe por tique;
	/// quando passa de `10*level`, `Attack_Gain(level + 0,05)` (`heran.dm:79`) e `Ki += 10*BaseDrain`
	/// (`:80`). Ele compara o BP EXPRESSO com o maior expresso em luta a vista (`highestebp`).
	///
	/// `highestebp` e lido no DM no instante do ataque, entre os `IsInFight` em `view()`
	/// (`UpdateFightingList.dm:35-41`); aqui e lido por tique entre os corpos em combate a vista -- a mesma
	/// pergunta, feita mais vezes. A exp da skill (`exp++` por tique na condicao e `exp += SparMod ** level`
	/// por surto) e creditada no motor de niveis, que e quem sobe o nivel.
	///
	/// (ESTA FUNCAO TINHA UMA SEGUNDA METADE: a `Saiyan Power`, o mesmo contador com `Attack_Gain(level+2)`
	/// contra o BP base. O dono a tirou do jogo em 2026-10-10 -- ver `SkillsAposentadas`.)
	/// </summary>
	private void ZenkaiEmCombateG11(ServerPlayer pl, Fighter f)
	{
		if (!f.IsInFight || !pl.Livro.Sabe(SkillHeranPowerG11)) return;

		int level = Math.Max(1, pl.Niveis.Nivel(SkillHeranPowerG11));
		double buf = _bufferHeranG11.GetValueOrDefault(pl.Id);
		if (f.expressedBP < MaiorPoderEmLutaAVistaG11(pl))
		{
			buf++;
			pl.Niveis.Creditar(SkillHeranPowerG11, 1);
		}
		if (buf > 10 * level)
		{
			buf -= 10 * level;
			pl.Niveis.Creditar(SkillHeranPowerG11, Math.Pow(f.SparMod, level));
			f.AttackGain(_rng, level + 0.05);
			f.Ki += 10 * f.BaseDrain();
		}
		_bufferHeranG11[pl.Id] = buf;
	}

	/// <summary>O maior BP EXPRESSO entre os OUTROS corpos em combate a vista. Zero se nao houver.</summary>
	private double MaiorPoderEmLutaAVistaG11(ServerPlayer pl)
	{
		double maior = 0;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			if (o == pl || EhCadaver(o) || !o.Ficha.IsInFight) continue;
			if (Vec2.Distance(o.Pos, pl.Pos) > RaioDaVista) continue;
			if (o.Ficha.expressedBP > maior) maior = o.Ficha.expressedBP;
		}
		return maior;
	}

	// ---------------------------------------------------------------
	// 16b. SUN / MOON / ABOVE ALL -- `makyo.dm:71-89`, `:115-133`, `:156-195`
	// ---------------------------------------------------------------
	/// <summary>
	/// O `hours_to_daylight()` do DM (`Weather.dm:138-150`) sobre a hora do <see cref="Ceu"/>: o estagio
	/// 1..5 e dia (3 = meio-dia), 6..10 e noite, e 11 e o crepusculo eterno de quem nao tem dia nem
	/// noite (`sync_area_daylight`, `:160-161`: espaco e interiores). A hora do Ceu ja vem grampeada
	/// pelo `HasDay`/`HasNight` do planeta (`Ceu.HoraVisivel`): mundo sem noite fica no meio-dia (3),
	/// que e exatamente o `if(target>=5) target=3` do DM.
	/// </summary>
	private static int EstagioDoDiaG11(RelogioDoPlaneta r, EstadoDoCeu ceu)
	{
		if (!r.TemDia && !r.TemNoite) return 11;
		int h = (int)Math.Floor(ceu.Hora * 24) % 24;
		int hours = h == 0 ? 24 : h;
		return hours switch
		{
			5 or 6 => 1,
			>= 7 and <= 10 => 2,
			>= 11 and <= 13 => 3,
			>= 14 and <= 17 => 4,
			18 or 19 => 5,
			20 or 21 => 6,
			22 or 23 => 7,
			24 => 8,
			1 or 2 => 9,
			_ => 10,
		};
	}

	/// <summary>
	/// `makyo_bonus(mastery, maxlvl)` (`makyo.dm:21-24`): `stepped_mastery_mult(mastery, [1..maxlvl])`
	/// (`supersaiyanbuff.dm:591-598`) -- o degrau k (1 = base) acende em `mastery >= k*100/n`; o
	/// maximo so em 100%. Com maestria zero o bonus e +1, e nao zero: e o "+1 (base)" do proprio
	/// comentario do DM.
	/// </summary>
	private static double BonusDoAstroG11(double mastery, int maxlvl)
	{
		int idx = 1;
		for (int k = 2; k <= maxlvl; k++)
			if (mastery >= k * 100.0 / maxlvl) idx = k;
		return idx;
	}

	/// <summary>
	/// OS TRES ASTROS DO MAKYO. O que eles fazem no DM e escrever `ssjBuff` por atribuicao a cada
	/// tique do efetor; aqui o fator entra em `formsBuff` -- o slot generico de forma, que e o MESMO
	/// fator do `formBuff` (`Fighter.Power.cs:45`) -- pelo alicerce de buffs, por dois motivos:
	///   * `GameServer.Formas.cs:1048` reescreve `ssjBuff` a cada troca de forma, e a atribuicao do
	///     DM seria apagada no primeiro `MultiplicadorDaForma`;
	///   * pelo alicerce o fator e DESFEITO exatamente quando o astro se poe -- no DM ninguem zera o
	///     `ssjBuff` a noite, e o Sun deixava o ultimo valor do dia (1,2 + bonus) de pe ate o amanhecer.
	///     O dono pediu "de dia um vale e de noite o outro", e e isso que sai daqui.
	///
	///     Sun (`:77-89`, so sem Above_All):  estagio 1..5 -> 1,2 / 1,6 / 2 / 1,6 / 1,2  + bonus(maestria, 4)
	///                                        ao meio-dia (3) a maestria sobe 0,02 por tique
	///     Moon (`:121-124`):                 estagio 6..10 -> 1,2 + bonus(maestria, 4); maestria sobe a noite
	///     Above_All (`:162-195`, so o dia):  os mesmos 1,2..2 + bonus(maestria, 5); Ki passivo
	///                                        `Ki += k * MaxKi * bonus` ate 2,5x o tanque (k = 0,0005 / 0,0006 /
	///                                        0,0009 / 0,0006 / 0,0005), `overcharge = 1`, e ao meio-dia
	///                                        `tgains *= 5` (desfeito ao sair do meio-dia, `:162-165`)
	///
	/// O `Tmagimon` da Moon por fase da lua (`:125-133`) NAO acontece, e nao e omissao minha: o
	/// `switch(savant.currentMoonlight==5)` avalia uma COMPARACAO (0 ou 1) e os ramos sao `if(2)`..
	/// `if(8)` -- nenhum casa, nunca. Bug visivel do DM, mantido: a Lua nao mexe na magia.
	///
	/// A METADE NOTURNA DO ABOVE_ALL ("less BP than normal during nighttime") mora no `HellStar`
	/// (`Stats.dm:331-349`, `hellstar_disabled`): a Estrela Makyo passando pela area. Ela NAO existe
	/// neste port (`HellstarBuff` nunca e calculado) e nao foi inventada aqui.
	/// </summary>
	private void AstrosDoMakyoG11(ServerPlayer pl, Fighter f)
	{
		bool sun = pl.Livro.Sabe(SkillSunG11), moon = pl.Livro.Sabe(SkillMoonG11), above = pl.Livro.Sabe(SkillAboveAllG11);
		if (!sun && !moon && !above) return;

		RelogioDoPlaneta relogio = RelogioDaZona(pl.Zone);
		int estagio = EstagioDoDiaG11(relogio, Ceu.De(relogio, TempoDoMundo));

		// ---- SUN (`if(!locate(/datum/skill/makyo/Above_All) in savant.learned_skills)`, `:77`) ----
		if (sun && !above)
		{
			double? basePorEstagio = estagio switch { 1 => 1.2, 2 => 1.6, 3 => 2, 4 => 1.6, 5 => 1.2, _ => null };
			if (basePorEstagio is { } b)
			{
				if (estagio == 3 && f.makyosunmastery < 100) f.makyosunmastery += 0.02;
				AplicarAstroG11(pl, "Makyo_Sun", "Sol do Makyo", b + BonusDoAstroG11(f.makyosunmastery, 4));
			}
			else DesligarAstroG11(pl, "Makyo_Sun", "Sol do Makyo");
		}
		else DesligarAstroG11(pl, "Makyo_Sun", "Sol do Makyo");

		// ---- MOON (`:121-124`) ----
		if (moon && estagio is >= 6 and <= 10)
		{
			if (f.makyomoonmastery < 100) f.makyomoonmastery += 0.02;
			AplicarAstroG11(pl, "Makyo_Moon", "Lua do Makyo", 1.2 + BonusDoAstroG11(f.makyomoonmastery, 4));
		}
		else DesligarAstroG11(pl, "Makyo_Moon", "Lua do Makyo");

		// ---- ABOVE ALL, a metade solar (`:166-195`) ----
		if (above)
		{
			double baseAa = 0, k = 0;
			bool dia = true;
			switch (estagio)
			{
				case 1: baseAa = 1.2; k = 0.0005; break;
				case 2: baseAa = 1.6; k = 0.0006; break;
				case 3: baseAa = 2.0; k = 0.0009; break;
				case 4: baseAa = 1.6; k = 0.0006; break;
				case 5: baseAa = 1.2; k = 0.0005; break;
				default: dia = false; break;
			}
			if (dia)
			{
				double bonus = BonusDoAstroG11(f.makyoaamastery, 5);
				if (estagio == 3 && f.makyoaamastery < 100) f.makyoaamastery += 0.02;
				AplicarAstroG11(pl, "Makyo_Above_All", "Sol Supremo do Makyo", baseAa + bonus);
				if (f.Ki < f.MaxKi * 2.5) f.Ki += k * f.MaxKi * bonus;   // passive Ki boost
				f.overcharge = true;
				// `if(!gaingot) tgains *= 5` ao meio-dia; `tgains /= 5` fora dele (`:162-165`, `:180-182`)
				if (estagio == 3) { if (!TemBuff(pl, "Makyo_Gains")) LigarBuff(pl, "Makyo_Gains", "Ganhos do Sol Supremo", new(), fatores: new() { ["tgains"] = 5 }); }
				else DesligarBuff(pl, "Makyo_Gains");
			}
			else
			{
				DesligarAstroG11(pl, "Makyo_Above_All", "Sol Supremo do Makyo");
				DesligarBuff(pl, "Makyo_Gains");
			}
		}
	}

	/// <summary>Liga (ou re-liga com o fator novo) o buff de forma de um astro. Sem churn: se o fator nao mudou, nada acontece.</summary>
	private void AplicarAstroG11(ServerPlayer pl, string id, string nome, double fator)
	{
		if (TemBuff(pl, id) && _astroFatorG11.TryGetValue((pl.Id, id), out double atual) && Math.Abs(atual - fator) < 1e-9) return;
		DesligarBuff(pl, id);
		LigarBuff(pl, id, nome, new(), fatores: new() { ["formsBuff"] = fator });
		_astroFatorG11[(pl.Id, id)] = fator;
	}

	/// <summary>O nome vem por parametro, e nao de `Tecnicas.Get(id)`: os astros NAO sao tecnicas do catalogo, e o `Get` SINTETIZA uma entrada "nao portada" pra todo id desconhecido.</summary>
	private void DesligarAstroG11(ServerPlayer pl, string id, string nome)
	{
		if (DesligarBuff(pl, id)) Avisar(pl, $"{nome}: o astro se poe e o poder dele vai embora.");
		_astroFatorG11.Remove((pl.Id, id));
	}

	// ---------------------------------------------------------------
	// 16c. TIME STORE -- `kanassa-jin.dm:51-70`
	// ---------------------------------------------------------------
	/// <summary>Um mes do DM: `Year += 0.1` a cada 28 dias (`WorldClock.dm:18-27`), e `stored_time += 10*(Year - last_age)` = 1 por mes.</summary>
	private const double DiasPorTempoGuardadoG11 = 28;

	/// <summary>
	/// O CORPO PRESO NO TEMPO: `stuckage = Age` ao aprender (`:68-69`) e `savant.Age = stuckage` a cada
	/// tique (`:66`) -- nada envelhece um Kanassa-jin com esta skill, nem o Toque do Tempo. E o
	/// `stored_time` enche 1 por MES do calendario (`:62-65`; `Year` anda 0,1 por mes de 28 dias):
	/// aqui o calendario e o da Terra do <see cref="Ceu"/>, que e a regua do universo.
	///
	/// O `stored_time` e a moeda que o Toque do Tempo (G2) gasta no DM (`stored_time--`, `:89`); o G2
	/// trocou a moeda por uma recarga de 30 s por nao haver moeda. Agora ha: ligar o `stored_time--`
	/// e uma linha no G2, deixada la pra quem reequilibrar o toque.
	/// </summary>
	private void TimeStoreG11(ServerPlayer pl, Fighter f)
	{
		if (!pl.Livro.Sabe(SkillTimeStoreG11)) { _diaDoTimeStoreG11.Remove(pl.Id); return; }

		if (f.stuckage <= 0) f.stuckage = pl.Idade;
		if (pl.Idade != (int)f.stuckage || Math.Abs(f.Idade - f.stuckage) > 1e-9)
		{
			pl.Idade = (int)f.stuckage;
			f.Idade = f.stuckage;
			pl.SigAtributos = "";
		}

		double dia = RelogioDaZona(ZoneKey.Premade("Earth")).DiaLocal(TempoDoMundo);
		if (!_diaDoTimeStoreG11.TryGetValue(pl.Id, out double ultimo)) { _diaDoTimeStoreG11[pl.Id] = dia; return; }
		int meses = 0;
		while (dia - ultimo >= DiasPorTempoGuardadoG11) { ultimo += DiasPorTempoGuardadoG11; meses++; }
		if (meses == 0) return;
		_diaDoTimeStoreG11[pl.Id] = ultimo;
		f.stored_time += meses;
		Avisar(pl, $"voce tem {f.stored_time:0} de tempo guardado.");
	}

	// ---------------------------------------------------------------
	// 16d. MEDITATE POWER / BRAIN POWER -- `gray.dm:63-65`, `:82-86`
	// ---------------------------------------------------------------
	/// <summary>
	/// O GANHO DE BP MEDITANDO dos Grays, por tique de 0,2 s:
	///     meditatepower nivel 3:  prob(10) -> BP += capcheck(bp_gain_base() * BPTick * 1/35)
	///     brainpower:             prob(15) -> BP += capcheck(bp_gain_base() * BPTick * max(log4(techskill*10), 1) * 1/75)
	/// Os `MedMod *= 2 / 1.1` e os genes do nivel 3 ja entram pelo extrator (sao "so buff ja
	/// aplicado" no censo); o que faltava era este pulso.
	/// </summary>
	private void MeditacaoDosGraysG11(ServerPlayer pl, Fighter f)
	{
		if (!f.med || f.BP >= f.relBPmax) return;

		if (pl.Livro.Sabe(SkillMeditatePowerG11) && pl.Niveis.Nivel(SkillMeditatePowerG11) >= 3 && DmMath.Prob(_rng, 10))
			f.BP += f.GanhoDeAtividade(f.CapCheck(f.BpGainBase() * GainKnobs.BPTick * (1.0 / 35)));   // (2026-10-26) guarda no banco

		if (pl.Livro.Sabe(SkillBrainPowerG11) && DmMath.Prob(_rng, 15))
		{
			double log4 = f.techskill > 0 ? Math.Log(f.techskill * 10) / Math.Log(4) : 0;
			f.BP += f.GanhoDeAtividade(f.CapCheck(f.BpGainBase() * GainKnobs.BPTick * Math.Max(log4, 1) * (1.0 / 75)));
		}
	}

	// ---------------------------------------------------------------
	// 16e. PERFECT METABOLISM -- `alien.dm:131` e `StaminaDrain.dm:45-51`
	// ---------------------------------------------------------------
	/// <summary>O laco `GlobalStats()` do DM dorme `sleep(3)` (`Stats.dm:67`): a agua e conferida a cada 0,3 s.</summary>
	private const double SegundosPorPassadaDaAguaG11 = 0.3;

	/// <summary>
	/// "You can survive off of just water now": `partplant` (escrito pelo extrator ao aprender) e
	/// lido no `CheckNutrition` -- meditando, fora de luta, `prob(10)` a cada passada de 0,3 s, conta
	/// os tiles de AGUA em `view(1)` (a caixa 3x3) e repoe `n * (maxNutrition * 0.01)` enquanto o
	/// tanque nao estiver cheio (`StaminaDrain.dm:45-51`). A agua e a terceira classe de celula do
	/// port (`ZoneCollision.EhAgua`), entao a pergunta e a mesma do nado.
	/// </summary>
	private void MetabolismoPerfeitoG11(ServerPlayer pl, Fighter f, double dt)
	{
		if (f.partplant <= 0 || !f.med || f.IsInFight) { _relogioDaAguaG11.Remove(pl.Id); return; }

		double relogio = _relogioDaAguaG11.GetValueOrDefault(pl.Id) + dt;
		if (relogio < SegundosPorPassadaDaAguaG11) { _relogioDaAguaG11[pl.Id] = relogio; return; }
		_relogioDaAguaG11[pl.Id] = relogio - SegundosPorPassadaDaAguaG11;
		if (!DmMath.Prob(_rng, 10)) return;

		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null || !mapa.TemAgua) return;
		int cx = (int)Math.Floor(pl.Pos.X / ZoneCollision.TileSize), cy = (int)Math.Floor(pl.Pos.Y / ZoneCollision.TileSize);
		int agua = 0;
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
				if (mapa.EhAgua(cx + dx, cy + dy)) agua++;
		if (agua == 0) return;

		double tanque = Nutricao.Tanque(f.Metabolism);
		if (f.CurrentNutrition < tanque) f.CurrentNutrition += agua * (tanque * 0.01);
	}

	// ---------------------------------------------------------------
	// 16f. O LOOP DO EXPAND -- `Body Expansion.dm:28-33`, a cada 5 `BuffLoop()` = 1 s
	// ---------------------------------------------------------------
	/// <summary>
	/// `doescost = rand(1,3); if(3) Ki -= expandlevel/Etechnique` -- um terco das vezes, por segundo;
	/// e `if(Ki <= 10) stopbuff` -- com o Ki no fim o corpo relaxa sozinho.
	/// </summary>
	private void LoopDoExpandG11(ServerPlayer pl, Fighter f, double dt)
	{
		if (!TemBuff(pl, "Expand_Body")) { _relogioDoExpandG11.Remove(pl.Id); return; }

		double relogio = _relogioDoExpandG11.GetValueOrDefault(pl.Id) + dt;
		if (relogio < 1) { _relogioDoExpandG11[pl.Id] = relogio; return; }
		_relogioDoExpandG11[pl.Id] = relogio - 1;

		if (_rng.Next(1, 4) == 3) f.Ki -= f.expandlevel / Math.Max(f.Etechnique, 1e-9);
		if (f.Ki <= 10)
		{
			DesligarBuff(pl, "Expand_Body");
			f.expandlevel = 0;
			Avisar(pl, "sem energia, seu corpo relaxa sozinho.");
		}
	}

	// ---------------------------------------------------------------
	// 16g. OS PENDENTES: transmissao, doacao, CooldownAmount
	// ---------------------------------------------------------------
	private void TickDaTransmissaoG11(ServerPlayer pl, TransmissaoG11 t, long agora)
	{
		// `if(src.loc == loctest)` -- saiu do tile, "You moved!" (`yardrat.dm:137`, `:160-164`)
		if (Vec2.Distance(pl.Pos, t.Ancora) > 4 || pl.Ficha.KO || CaidoPelaMorte(pl))   // (v0.3.2) o morto de pe se concentra
		{
			_transmissaoG11.Remove(pl.Id);
			Avisar(pl, "voce se mexeu! A concentracao se perde.");
			return;
		}
		if (agora < t.QuandoMs) return;
		_transmissaoG11.Remove(pl.Id);
		ChegarPelaTransmissaoG11(pl, t);
	}

	private void TickDaDoacaoG11(ServerPlayer pl, DoacaoG11 d, long agora)
	{
		Fighter f = pl.Ficha;
		ServerPlayer? alvo = _players.GetValueOrDefault(d.Alvo);
		// `while(givingpower && Ki >= 0.01*MaxKi)` (`:41`) -- e SO isso no DM. DIVERGENCIA DECLARADA: aqui a doacao
		// tambem acaba com o doador caido ou morto, com o alvo morto ou fora do mundo, e com os dois em zonas
		// diferentes. O laco do DM nao pergunta nada disso -- continuaria transferindo de um corpo desmaiado, ou
		// pra outro planeta --, e o port nao tem como mandar Ki pra um corpo que nao esta na lista da zona.
		bool continua = !d.Parar && !EhCadaver(pl) && !f.KO && f.Ki >= 0.01 * f.MaxKi
						&& alvo != null && alvo.Combate != null && !EhCadaver(alvo)
						&& alvo.Zone.Hash == pl.Zone.Hash;
		if (!continua) { EncerrarDoacaoG11(pl, d); return; }

		// ============================ SEM ANCORA: QUEM DOA ANDA ============================
		// Aqui havia um `Ancorar(pl, d.Ancora)` com o comentario *"o `gavepower` prende o doador no lugar
		// enquanto doa (`movement handler.dm:139`)"* -- e a linha citada diz o contrario: ela so DESCONTA o
		// contador (`if(gavepower) gavepower = max(0,gavepower - 1)`), sem zerar o `mobTime` como fazem o
		// `canmove` e o `move` duas linhas acima (`:130-131`). O `gavepower` e a trava de DOAR DE NOVO
		// (`!gavepower`, `givepower.dm:36`); o corpo de quem doa nunca foi preso no DM, e o laco (`:41-48`) nao
		// olha distancia. A ancora puxava de volta o doador que andava -- e o que PISCAVA, com o Ki e o salto
		// ja pagos (o `turf/DblClick` nao le `gavepower`, `click.dm:54`).
		// ====================================================================================
		DoseDaDoacaoG11(pl, d, alvo!);
	}

	/// <summary>`PowerCooldown()` (`base.dm:212-227`): por segundo, `CooldownAmount -= CooldownAmount/1000` enquanto >= 1; abaixo disso, zero.</summary>
	private void DecairCooldownAmountG11(double dt)
	{
		_relogioDoCooldownG11 += dt;
		if (_relogioDoCooldownG11 < 1) return;
		_relogioDoCooldownG11 -= 1;
		foreach (ServerPlayer pl in _players.Values)
		{
			Fighter f = pl.Ficha;
			if (f.CooldownAmount == 0) continue;
			if (f.CooldownAmount >= 1) f.CooldownAmount -= f.CooldownAmount / 1000;
			else f.CooldownAmount = 0;
		}
	}

	// =====================================================================
	// 17. STRETCHY ARMS -- `namekian.dm:139` e `Grabbing.dm:97-99`
	// =====================================================================
	/// <summary>
	/// O ALVO DO BRACO ESTICADO: `if(can_stretch_arms && target in view(screenx) && get_dist >= 3)
	/// stretch_arms(target)` (`Grabbing.dm:97`) -- com a flag e com um alvo marcado a tres tiles ou
	/// mais, ainda a vista, o agarrao vai ate ele. Chamado do `AlternarAgarrao`, ANTES da busca por
	/// quem esta na frente, como no DM.
	///
	/// O que o `stretch_arms()` (`namekian.dm:220-280`) faz por cima do agarrao normal e uma coisa
	/// so: a forca do aperto e `(Ephysoff*expressedBP)/3` (`:241`) em vez do inteiro, e ela nao e
	/// recalculada. E o que <see cref="FatorDoBracoG11"/> devolve ao tique do agarrao. O braco-objeto
	/// que voa 25 tiques ate o alvo e o `stretch_bring` que o puxa passo a passo nao vieram: o
	/// segundo toque na tecla (levantar no colo) ja traz o preso ate voce.
	/// </summary>
	private ServerPlayer? AlvoDoBracoEsticadoG11(ServerPlayer pl)
	{
		if (pl.Ficha.can_stretch_arms <= 0) return null;
		if (Marcado(pl) is not { } alvo) return null;
		float dist = Vec2.Distance(alvo.Pos, pl.Pos);
		if (dist < 3 * ZoneCollision.TileSize || dist > RaioDaVista) return null;
		if (alvo.AgarradoPorId != 0 || alvo.Combate.IntocavelPara(pl.Combate)) return null;   // (2026-10-27, o pedido 129) `IntocavelPara`
		return alvo;
	}

	/// <summary>Por quanto a forca do aperto e DIVIDIDA enquanto este agarrao for de braco esticado (3), senao 1.</summary>
	private double FatorDoBracoG11(ServerPlayer a) => _agarroesEsticadosG11.Contains(a.Id) ? 3 : 1;

	// =====================================================================
	// 18. A LIMPEZA
	// =====================================================================
	/// <summary>Quem saiu leva o estado dele junto -- id de jogador se REUSA (ver `EsquecerG6`).</summary>
	private void EsquecerG11(int id)
	{
		_sneakAteG11.Remove(id);
		_prontoMajinG11.Remove(id);
		_transmissaoG11.Remove(id);
		_doacaoG11.Remove(id);
		_poderDadoAteG11.Remove(id);
		_bufferHeranG11.Remove(id);
		_relogioDaAguaG11.Remove(id);
		_relogioDoExpandG11.Remove(id);
		_diaDoTimeStoreG11.Remove(id);
		_agarroesEsticadosG11.Remove(id);
		foreach ((int quem, string buff) in _astroFatorG11.Keys.ToList())
			if (quem == id) _astroFatorG11.Remove((quem, buff));
		foreach (int doador in _doacaoG11.Keys.ToList())
			if (_doacaoG11[doador].Alvo == id) _doacaoG11[doador].Parar = true;
	}
}
