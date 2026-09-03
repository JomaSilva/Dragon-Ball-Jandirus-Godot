using Godot;
using Jandirus.Core;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G6 -- O QUE OS CARGOS DAVAM E NINGUEM RECEBIA (+ o sopro e os dois buffs de Ki).
///
/// ============================ POR QUE ESTAS DEZENOVE ============================
/// O censo deste lote (<see cref="Jandirus.Core.Skills.CensoDeSkills"/>) parte de uma pergunta que
/// a camada anterior deixou por escrito: *"51 skills concedidas pelos cargos | 40 dao verbo (8
/// verbos VIVOS, 39 ainda mudos)"*. Ou seja, o cargo entregava o kit e o kit nao fazia nada.
///
/// ONZE DESTAS DEZENOVE SAO VERBOS DE KIT DE CARGO: Kamehameha (Turtle), Galick Gun (Rei de
/// Vegeta), Death Beam (Capitao), Dodon Ray e Kikoho (Crane), Enkumei (Grande Ancia~o e os quatro
/// cardeais), Buster Barrage (Kaio do Oeste), Heal (cinco cargos), Full Power, e o par do sopro que
/// a arvore Mind concede. Fechar este lote e a outra metade do pedido "rank e skill tree" -- a
/// primeira metade (as portas, a dadiva, as missoes) ja estava de pe e entregava kit MUDO.
///
/// TODAS ENTRAM POR ENCANAMENTO QUE JA EXISTIA, e nenhuma inventa entidade, opcode ou cadeia de
/// dano:
///
///     familia          porta que ja existia                     quantas
///     ---------------  ---------------------------------------  -------
///     raios nomeados   `Canalizar` (lote dos projeteis)              6
///     bola com preco   `Disparar` + `EspalharDanoG3`                 1
///     buffs de Ki      `LigarBuff` / `DesligarBuff` (lote G1)        4
///     sopro            `Arremessar` (knockback) + lista de tiros     4
///     punho            `GolpeG3` + a barragem agendada (lote G3)     2
///     servico          `Body.Curar` + `NiveisDeSkill`                2
/// ============================================================================
///
/// ============================ TRES PERICIAS ACORDADAS ============================
/// `kiaiskill`, `kibuffskill` e `kiawarenessskill` existiam em `niveis.json` (8, 10 e 8 degraus) e
/// nao tinham campo no <see cref="Fighter"/>: caiam em `EfeitosDeSkill.Desconhecidos` e a arvore
/// inteira de sopro/circulacao/percepcao nao fazia diferenca nenhuma. E a QUINTA vez que este
/// projeto encontra dado extraido sem consumidor -- ver o bloco em `Fighter.Tecnicas.cs`.
/// ================================================================================
///
/// ============================ A DIVIDA DO LOTE ANTERIOR CONTINUA DE PE ============================
/// O `Disparar` funde `mods` e `basedamage` e o `ModsDoTiro` de bola usa a formula da tecnica
/// customizada -- `Ekioff` cresce ao QUADRADO no port contra linear no DM. Ver o cabecalho do
/// `GameServer.Tecnicas.G5.cs`. Este lote usa a MESMA convencao pelo mesmo motivo: a ordem relativa
/// que o DM escreveu (Death Beam 5 contra Kamehameha 2,3) chega intacta, e o rebalanceamento
/// continua sendo uma passada propria com bancada propria.
/// =================================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// ESTADO DO LOTE
	// =====================================================================
	/// <summary>
	/// `kiaionCD` -- a recarga COMPARTILHADA das quatro tecnicas de sopro (`Ki2.0/Kiai.dm:4`).
	///
	/// Uma so pras quatro, e no DM e literalmente a mesma variavel: quem soltou um Kiai nao emenda
	/// um Shockwave por cima trocando de verb. A recarga E o balanceamento da familia -- as quatro
	/// derrubam gente de graca, sem tiro pra desviar.
	/// </summary>
	private readonly Dictionary<int, long> _soproPronto = [];

	/// <summary>`healCD` -- e quem esta sendo curado por quem (`Healtarget`).</summary>
	private readonly Dictionary<int, int> _curando = [];

	/// <summary>
	/// `kikohoblasts` -- a silaba em que cada um parou, e ate quando ela ainda vale.
	///
	/// FORA DO <see cref="ServerPlayer"/> de proposito, pela mesma regra que o `_cegoAte` do Solar
	/// Flare ja seguia: e estado de sessao de UMA tecnica, e um campo por tecnica na ficha de todo
	/// mundo daria trezentos campos pra trezentas skills.
	/// </summary>
	private readonly Dictionary<int, (int Silaba, long Ate)> _kikoho = [];

	/// <summary>Um Explosive Roar sendo carregado: id do dono -> quanto ja acumulou.</summary>
	private readonly Dictionary<int, RugidoG6> _rugindo = [];

	/// <summary>
	/// A CARGA DO RUGIDO. Duas fases, como a Final Explosion do lote G3 -- e pelo mesmo motivo: o
	/// DM prende o corpo num `while(charging)` e o port nao tem laco por jogador, tem tique.
	/// </summary>
	private sealed class RugidoG6
	{
		/// <summary>`charge`: quantos segundos inteiros ja acumulou. O DM para em 29.</summary>
		public int Carga;

		/// <summary>Quando o proximo segundo de carga vence, em ms.</summary>
		public long ProximoMs;
	}

	/// <summary>`charge <= 29` -- o teto do laco de carga (`Ki2.0/Kiai.dm:136`).</summary>
	private const int CargaMaximaDoRugido = 29;

	/// <summary>
	/// `charge = min(charge,5)` -- o teto do EFEITO (`:139`), e ele NAO e o teto da carga.
	///
	/// Os dois numeros diferentes sao do original e valem a pena: passar de 5 segundos so gasta Ki
	/// (e o custo por segundo CRESCE), sem nenhum ganho. E uma armadilha do jogo antigo que fica
	/// portada como esta -- mas o aviso na tela, esse e do port: o DM nao dizia nada.
	/// </summary>
	private const int CargaUtilDoRugido = 5;

	// =====================================================================
	// REGISTRO
	// =====================================================================
	private void RegistrarTecnicasG6()
	{
		IniciarLote("G6");
		foreach (string raio in new[] { "Kamehameha", "GalicGun", "Death_Beam", "Dodompa", "Enkumei", "Boom_Wave" })
			Vivo(raio, pl => RaioNomeadoG6(pl, raio));
		Vivo("Kikoho", KikohoG6);
		Vivo("Focus", FocoG6);
		Vivo("Efficiency", EficienciaG6);
		Vivo("Energy_Shield", EscudoDeEnergiaG6);
		Vivo("Full_Power", FullPowerG6);
		Vivo("Kiai", pl => SoproG6(pl, TipoDeSopro.Kiai));
		Vivo("Shockwave", pl => SoproG6(pl, TipoDeSopro.Onda));
		Vivo("Deflection", pl => SoproG6(pl, TipoDeSopro.Deflexao));
		Vivo("Explosive_Roar", RugidoExplosivoG6);
		Vivo("Wolf_Fang_Fist", pl => PresaDoLoboG6(pl, furacao: false));
		Vivo("Wolf_Fang_Hurricane", pl => PresaDoLoboG6(pl, furacao: true));
		Vivo("Heal", CurarG6);
		Vivo("Assess_Ki_Skill", AvaliarOKiG6);
	}

	// =====================================================================
	// 1) OS SEIS RAIOS NOMEADOS
	// =====================================================================
	/// <summary>
	/// A RECEITA DE UM RAIO NOMEADO -- os seis verbos sao O MESMO CODIGO com seis tabelas.
	///
	/// Um metodo so, e nao seis, porque no DM eles sao literalmente copia e cola um do outro: mesma
	/// abertura (`if(beaming) stopbeaming()`), mesma checagem, mesmo `spawn addchargeoverlay()`. Seis
	/// copias aqui seriam seis lugares pra corrigir quando o `Canalizar` mudar -- e ele ja mudou uma
	/// vez, quando ganhou o `custoPorTiro` que faltava.
	/// </summary>
	private readonly record struct ReceitaDeRaioG6(
		string Nome, double MultDeCusto, double Velocidade, double Potencia,
		double Alcance, double Onda);

	/// <summary>
	/// OS DEGRAUS POR `beamskill`, e eles sao a assinatura da familia.
	///
	/// Quatro dos seis raios trocam de numeros conforme a pericia (`Kamehameha.dm:47-77` e irmaos):
	/// quem treinou raio nao ganha "um pouco mais de dano", ganha OUTRA tecnica -- mais forte, mais
	/// longe e ate trinta vezes mais cara por segundo. Os outros dois (Death Beam, Dodon Ray) tem
	/// numero unico, e por isso a tabela deles tem um degrau so.
	///
	/// ============================ O MESMO DEFEITO DO FINAL FLASH, TRES VEZES ============================
	/// Kamehameha, Galick Gun e Enkumei fecham a escada com `else if(usr.beamskill==100)` -- igualdade
	/// exata num float que sobe em fracoes. Acima de 100 nenhum ramo casa, `charging` nunca e setado
	/// e o verb inteiro nao faz nada: o mestre absoluto de raio PERDE as tres tecnicas. E o mesmo
	/// defeito que o lote G5 achou no Final Flash, e o conserto e o mesmo -- o ultimo degrau e `>=`.
	/// ==============================================================================================
	/// </summary>
	private static ReceitaDeRaioG6 DegrauDoRaioG6(string id, double beamskill) => id switch
	{
		// `Kamehameha.dm:29`: kireq 20, chargedelay 5, beamspeed 0.4 em todos os degraus
		"Kamehameha" => beamskill switch
		{
			< 30 => new("Kamehameha", 1, 0.4, 2.3, 40, 1),
			< 70 => new("Kamehameha", 10, 0.4, 2.5, 45, 1),
			< 100 => new("Kamehameha", 20, 0.4, 2.7, 45, 1),
			_ => new("Kamehameha", 30, 0.4, 3.0, 45, 1),
		},

		// `GalicGun.dm:30`: kireq 21. Repare que o ULTIMO degrau muda a velocidade tambem (0.3 -> 0.4)
		"GalicGun" => beamskill switch
		{
			< 30 => new("Galick Ho", 1.2, 0.3, 2.4, 40, 1),
			< 70 => new("Galick Ho", 12, 0.3, 2.6, 45, 1),
			< 100 => new("Galick Ho", 22, 0.3, 2.8, 45, 1),
			_ => new("Galick Ho", 30, 0.4, 3.0, 50, 1),
		},

		// `Enkumei.dm:33`: kireq 22, `wavemult = 1.2` antes da escada e `= 2` no ultimo degrau
		"Enkumei" => beamskill switch
		{
			< 30 => new("Enkumei", 1, 0.3, 2.0, 40, 1.2),
			< 70 => new("Enkumei", 10, 0.3, 2.2, 45, 1.2),
			< 100 => new("Enkumei", 20, 0.2, 2.4, 45, 1.2),
			_ => new("Enkumei", 30, 0.3, 3.0, 45, 2.0),
		},

		"Death_Beam" => new("Death Beam", 25, 0.3, 5.0, 10, 1),   // `DeathBeam.dm:29`
		"Dodompa" => new("Dodon Ray", 29, 0.6, 4.0, 35, 1),       // `Dodompa.dm:27`
		_ => new("Boom Wave", 0, 0.2, 2.3, 5, 1),                 // `beams.dm:472` -- ver abaixo
	};

	/// <summary>
	/// O `kireq` de cada um, em multiplos de `BaseDrain` -- o numero literal do verb.
	/// </summary>
	private static double KireqDoRaioG6(string id) => id switch
	{
		"Kamehameha" => 20,
		"GalicGun" or "Death_Beam" or "Dodompa" => 21,
		"Enkumei" => 22,
		_ => 15,   // Boom Wave
	};

	/// <summary>
	/// A `chargedelay` de cada um. Cinco nos quatro que declaram, o padrao nos outros dois.
	///
	/// O padrao E 1 e nao 0: `chargedelay` e o divisor do <see cref="Projetil.SegundosDeCarga"/>, e
	/// zero ali daria carga instantanea pra dois raios que o DM nao quis instantaneos -- ele
	/// simplesmente nao mexe na variavel, que nasce no valor do Ki Wave.
	/// </summary>
	private static double CargaDoRaioG6(string id) =>
		id is "Kamehameha" or "GalicGun" or "Death_Beam" or "Dodompa" ? 5 : 1;

	/// <summary>
	/// ============================ O QUE ESTE PORTE NAO TROUXE, E ELE E DECLARADO ============================
	/// No DM o segundo aperto durante a carga tem DOIS ramos (`Kamehameha.dm:37-42`):
	///
	///     accum >= 10*chargedelay/3  -> DISPARA na hora, com o raio inteiro
	///     accum  < 10*chargedelay/3  -> `stopcharging()`: cancela
	///
	/// Aqui so existe o segundo: apertar durante a carga CANCELA. O primeiro exigiria carga
	/// parcial-mas-valida, e o `Canalizar` do port e uma contagem regressiva, nao um acumulador --
	/// mudar isso e mexer no encanamento de sete tecnicas ja em producao por causa de seis.
	///
	/// O QUE SE PERDE: quem treinou muito nao consegue mais "soltar antes do fim". O QUE SE GANHA:
	/// a carga anunciada e a carga cobrada; no DM, um terco dela ja disparava o raio COMPLETO --
	/// isto e, a escada de `chargedelay` valia um terco do que promete.
	/// ====================================================================================================
	/// </summary>
	private void RaioNomeadoG6(ServerPlayer pl, string id)
	{
		if (SoltarCanal(pl, id)) return;

		ReceitaDeRaioG6 r = DegrauDoRaioG6(id, pl.Ficha.beamskill);
		double kireq = KireqDoRaioG6(id) * pl.Ficha.BaseDrain();

		// BOOM WAVE: `lastbeamcost = 15/(Ekiskill*2)` -- um numero CRU, e nao um multiplo do
		// `kireq`. E o unico raio do jogo cujo aluguel CAI conforme voce treina Ki, e e isso que faz
		// dele a arma de quem luta colado. O piso de 1 no Ekiskill e do port: `Ekiskill` nasce
		// perto de 1 mas pode ser fracionario, e uma divisao por um numero minusculo daria um
		// aluguel gigante justamente pra quem tem menos.
		double porTiro = id == "Boom_Wave"
			? 15 / (2 * Math.Max(pl.Ficha.Ekiskill, 1))
			: kireq * r.MultDeCusto;

		Canalizar(pl, id, kireq, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam,
			BaseDano = r.Potencia,        // `powmod`
			Velocidade = r.Velocidade,    // `beamspeed`
			AlcanceTiles = r.Alcance,     // `maxdistance`
			MultDeOnda = r.Onda,          // `wavemult`
			Piercer = true,               // `bypass = 1` nos seis
			CargaMinima = CargaDoRaioG6(id),
			Nome = r.Nome,
		}, custoPorTiro: porTiro);
	}

	// =====================================================================
	// 2) KIKOHO -- a bola paga com sangue (`blasts/Kikoho.dm:26`)
	// =====================================================================
	/// <summary>
	/// KIKOHO. `Ki -= 50*BaseDrain * n`, `SpreadDamage(2*n)`, `basedamage = 40`,
	/// `mods = Ekioff*Ekiskill*n`, onde `n` e a silaba: 1 (KI), 2 (KO), 3 (HO), e volta pra 1.
	///
	/// AS TRES SILABAS SAO A TECNICA. Cada uso seguido e mais forte E mais caro em vida, e o terceiro
	/// cobra 6 de dano espalhado por todos os membros -- que num personagem ferido e o que derruba.
	/// O DM ainda sorteia `prob(10)` de MORRER se o HP zerar (`:151`); aqui quem zera vai pelo funil
	/// normal de nocaute e morte do port, que ja sabe fazer as duas coisas e ja avisa a zona.
	///
	/// O CONTADOR NAO ZERA SOZINHO no port. No DM ele volta a zero depois de `sleep(60)` -- 6 s
	/// medidos por um laco que morre junto com o verb. Aqui ele mora numa recarga: passou o tempo,
	/// a proxima silaba e KI de novo. Mesmo efeito, sem laco por jogador.
	/// </summary>
	private void KikohoG6(ServerPlayer pl)
	{
		long agora = NowMs();
		if (EmEspera(pl, _blastPronto, "suas maos ainda estao juntando energia")) return;

		// A SILABA. O `kikohoblasts` do DM e um contador de 1 a 3 que reinicia; o prazo de 6 s
		// (`sleep(60)`) e o que decide se voce ainda esta na mesma sequencia.
		(int anterior, long ate) = _kikoho.GetValueOrDefault(pl.Id);
		int silaba = agora > ate ? 1 : anterior + 1;
		if (silaba > 3) silaba = 1;

		double custo = 50 * pl.Ficha.BaseDrain() * silaba;
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		// `usr.HP>=10` -- a tecnica e recusada a quem nao tem sangue pra pagar, e o `HP` do port e a
		// media do corpo na mesma escala de 0 a 100 (`CombatState.SincronizarVida`).
		//
		// ============================ NAO E `hpratio`, E FOI A BANCADA QUE DISSE ============================
		// A primeira versao desta guarda usava `hpratio < 0.1`, que parece a mesma coisa e nao e:
		// `hpratio = max(HP/100, 0.6)` (`Fighter.Power.cs:41`) tem PISO DE 0,6 -- ele existe pra que
		// um corpo moribundo nao perca 100% do poder, e por causa dele a condicao nunca poderia ser
		// verdadeira. Era uma guarda escrita, compilada, e impossivel de disparar: exatamente a
		// armadilha 1 da casa (*"regra escrita nao e regra ligada"*), e so apareceu porque a bancada
		// tentou disparar o limite em vez de confiar que ele existia.
		//
		// E FICA A NOTA HONESTA: com `HP` certo, esta guarda e RARA neste port. La o HP e uma poca so;
		// aqui e a media dos membros, e a morte vem de um VITAL quebrado -- medindo, um corpo apanhando
		// nao-letal para em ~25 de media e um apanhando letal MORRE com a media ainda em ~34. Ou seja,
		// chegar vivo a 10 quase nao acontece hoje. A guarda continua porque o numero e do DM e porque
		// os caminhos que chegam la (fome, veneno, regeneracao parcial) sao justamente os que estao na
		// fila; a bancada a exerce forjando a vida, e diz por escrito que forjou.
		// ==============================================================================================
		if (pl.Ficha.HP < 10) { Avisar(pl, "voce esta ferido demais pra pagar por isso."); return; }

		pl.Ficha.Ki -= custo;
		_kikoho[pl.Id] = (silaba, agora + 6000);
		_blastPronto[pl.Id] = agora + 1000;   // `sleep(10)` antes de liberar o proximo tiro
		for (int i = 0; i < 3; i++) pl.Ficha.BlastGain(_rng);
		pl.Ficha.blastskill += 0.05;

		Falar(pl, Protocol.Fala.Diz, silaba switch { 1 => "KI", 2 => "KO", _ => "HO" });

		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 40,               // `A.basedamage = 40` -- fixo, e o maior do jogo
			Velocidade = 1,
			AlcanceTiles = AlcanceDeBolaG5,
			MultDeOnda = silaba,         // o `mods *= kikohoblasts` do DM, pela unica via que o port tem
			Nome = "Kikoho",
		}, verbo: "Kikoho");

		// O SANGUE SAI DEPOIS DO TIRO, e a ordem importa: quem se derruba com a propria tecnica
		// ainda ve a bola sair. No DM o `SpreadDamage` vem antes e o `spawn(10)` adia o nocaute --
		// o efeito visivel e o mesmo, e aqui nao ha `spawn`.
		EspalharDanoG3(pl, pl, 2 * silaba, letal: false);
		Avisar(pl, $"o Kikoho cobra {2 * silaba:0} de sangue seu.");
	}

	// =====================================================================
	// 3) OS QUATRO BUFFS
	// =====================================================================
	/// <summary>
	/// FOCUS (`Ki2.0/KiBuffs.dm:8`). `initdrain = initbuff = 1 + (kicirculationskill+kibuffskill)/100`,
	/// `DrainMod *= initdrain`, `Tkioff += initbuff`.
	///
	/// O MESMO NUMERO NOS DOIS LADOS e a tecnica inteira: ela nao e "mais poder", e mais poder pelo
	/// preco exato. Quem treinou circulacao e buff ganha mais dos dois -- e por isso ela nunca fica
	/// obsoleta nem vira escolha obvia.
	///
	/// O DRENO ENTRA PELO CANAL DO MOTOR (`dreno`), e nao por uma cobranca no tique deste lote:
	/// `DrainMod` ja multiplica o custo de TUDO (`Fighter.BaseDrain`), que e literalmente o que o
	/// DM faz. Cobrar tambem aqui seria cobrar duas vezes -- ver o comentario do `TickDosBuffs`.
	/// </summary>
	private void FocoG6(ServerPlayer pl)
	{
		if (DesligarBuff(pl, "Focus")) { Avisar(pl, "voce deixa a mente vagar."); return; }
		if (pl.Ficha.KO) { Avisar(pl, "voce esta caido."); return; }

		double v = 1 + (pl.Ficha.kicirculationskill + pl.Ficha.kibuffskill) / 100;
		LigarBuff(pl, "Focus", "Foco", new Dictionary<string, double> { ["Tkioff"] = v }, dreno: v);
		Avisar(pl, $"voce concentra a circulacao do seu Ki (+{v:0.##} de ofensiva de energia, "
				 + $"e o gasto sobe {v:0.##}x).");
	}

	/// <summary>
	/// EFFICIENCY (`Ki2.0/KiBuffs.dm:35`). `initdrain = 2 + kiefficiencyskill/200`,
	/// `initbuff = 0.5 - kibuffskill/200`, `DrainMod /= initdrain`, `Tkioff -= initbuff`.
	///
	/// REPARE NO SINAL DO `initbuff`: quanto MAIOR o `kibuffskill`, MENOR a penalidade -- e num
	/// `kibuffskill` de 100 ela chega a zero, e a tecnica vira dreno pela metade de graca. Nao e
	/// engano de leitura: e a recompensa por treinar a arvore de buff ate o fim.
	///
	/// A DIVISAO VIRA FATOR, e nao subtracao: o motor de buffs multiplica `DrainMod` pelo `dreno` e
	/// divide na hora de desligar, entao `1/initdrain` diz exatamente a mesma coisa que o `/=` do DM
	/// e desfaz certo (ver o bloco `Fatores` do `BuffAtivo`).
	/// </summary>
	private void EficienciaG6(ServerPlayer pl)
	{
		if (DesligarBuff(pl, "Efficiency")) { Avisar(pl, "voce volta a gastar Ki normalmente."); return; }
		if (pl.Ficha.KO) { Avisar(pl, "voce esta caido."); return; }

		double divisor = 2 + pl.Ficha.kiefficiencyskill / 200;
		double perda = 0.5 - pl.Ficha.kibuffskill / 200;

		LigarBuff(pl, "Efficiency", "Eficiencia",
				  new Dictionary<string, double> { ["Tkioff"] = -perda }, dreno: 1 / divisor);
		Avisar(pl, $"voce racionaliza o gasto de Ki: energia dura {divisor:0.##}x mais, "
				 + $"ofensiva de Ki -{perda:0.##}.");
	}

	/// <summary>
	/// ENERGY SHIELD (`Ki2.0/KiBuffs.dm:67`). Entrada `0.01*MaxKi*(1/kidefenseskill)*BaseDrain`;
	/// `Tkidef += 1.1*max(1.1, Ekiskill/3)*(1 + kidefenseskill/100)`,
	/// `superkiarmor += Ekiskill/2.5 + kidefenseskill/35`, `superkiarmorMod *= 1.2`.
	///
	/// ELE NAO E O KI SHIELD. O Ki Shield (ja portado, `misc.dm`) soma defesa FISICA e de Ki e sai
	/// de graca depois de pago; este soma defesa de Ki e ARMADURA DE ENERGIA -- o `superkiarmor`,
	/// que e a barra que come tiro antes da vida (`Fighter.Statify:84`) -- e cobra aluguel todo
	/// segundo. Os dois convivem, como no DM.
	///
	/// ============================ UM DEFEITO DO ORIGINAL, CONSERTADO ============================
	/// `1/kidefenseskill` com `kidefenseskill = 0` (o valor de fabrica) e divisao por zero: no BYOND
	/// isso da infinito, o `Ki >= kireq` e falso, e o verb nao faz NADA -- sem mensagem. Ou seja, no
	/// jogo antigo o Energy Shield era invisivelmente inutil ate voce apanhar o suficiente de ki pra
	/// treinar defesa. Aqui a pericia tem piso 1, como os logs do resto do arsenal, e um novato paga
	/// 1% do Ki maximo pra levantar o escudo.
	/// ==========================================================================================
	/// </summary>
	private void EscudoDeEnergiaG6(ServerPlayer pl)
	{
		if (DesligarBuff(pl, "Energy_Shield")) { Avisar(pl, "o escudo de energia se apaga."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "agora nao."); return; }
		if (EnraizadoPorKi(pl.Id)) { Avisar(pl, "nao da pra erguer o escudo com um raio na mao."); return; }

		double def = Math.Max(pl.Ficha.kidefenseskill, 1);
		double custo = 0.01 * pl.Ficha.MaxKi * (1 / def) * pl.Ficha.BaseDrain();
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede {custo:0} de energia."); return; }

		pl.Ficha.Ki -= custo;
		double bonus = 1.1 * Math.Max(1.1, pl.Ficha.Ekiskill / 3) * (1 + pl.Ficha.kidefenseskill / 100);
		double armadura = pl.Ficha.Ekiskill / 2.5 + pl.Ficha.kidefenseskill / 35;

		LigarBuff(pl, "Energy_Shield", "Escudo de Energia",
				  new Dictionary<string, double> { ["Tkidef"] = bonus, ["superkiarmor"] = armadura },
				  fatores: new Dictionary<string, double> { ["superkiarmorMod"] = 1.2 });

		Avisar(pl, $"uma casca de energia se fecha (+{bonus:0.##} de defesa de Ki, "
				 + $"+{armadura:0.##} de armadura).");
	}

	/// <summary>
	/// FULL POWER (`Buffs/racial/grays.dm:65`). `Tphysoff/Tkioff/Tspeed += 1.2` cada, dreno
	/// `round(100/jirenskill,1)*BaseDrain` por tique, e cai quando `Ki <= MaxKi/20`.
	///
	/// A PERICIA E O PRECO. `jirenskill` sobe 0,5 por tique enquanto a aura esta de pe, ate 100 --
	/// entao a mesma tecnica custa 100x BaseDrain no primeiro dia e 1x BaseDrain pra quem a segurou
	/// por tempo suficiente. E o unico buff do jogo que fica MAIS BARATO com o uso, e o custo e o
	/// unico contrapeso que ele tem (o bonus e fixo).
	///
	/// O `transBuff = 2` do DM (que dobra o ganho de transformacao com a Ascensao ligada) NAO foi
	/// portado: `transBuff` nao existe neste port. Fica catalogado no censo, com o nome do sistema
	/// que ele espera.
	/// </summary>
	private void FullPowerG6(ServerPlayer pl)
	{
		if (DesligarBuff(pl, "Full_Power")) { Avisar(pl, "voce relaxa a aura."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "agora nao."); return; }

		if (pl.Ficha.Ki < pl.Ficha.MaxKi / 20)
		{
			Avisar(pl, "voce esta cansado demais pra concentrar a aura.");
			return;
		}

		LigarBuff(pl, "Full_Power", "Full Power", new Dictionary<string, double>
		{
			["Tphysoff"] = 1.2, ["Tkioff"] = 1.2, ["Tspeed"] = 1.2,
		});
		Avisar(pl, "voce concentra a aura inteira com uma forca estranha (+1,2 em forca, Ki e velocidade).");
	}

	// =====================================================================
	// 4) O SOPRO -- `Code/Modules/Skills/Ki/Ki2.0/Kiai.dm`
	// =====================================================================
	private enum TipoDeSopro { Kiai, Onda, Deflexao }

	/// <summary>
	/// AS TRES DA MESMA FAMILIA, e no DM sao o mesmo verb com tres numeros trocados:
	///
	///                  Ki       recarga (tiques)   alcance         o que faz
	///     Kiai         50xBD    1000/pericia       arco a frente   arremessa
	///     Shockwave    40xBD    4000/pericia       1 tile em volta arremessa + APAGA tiros ate 2
	///     Deflection   80xBD    8000/pericia       arco a frente   DEVOLVE tiros, 3 investidas
	///
	/// A RECARGA E INVERSA DA PERICIA (`max(round(N/(Ekiskill*10 + kieffusionskill + kiaiskill)), 10)`),
	/// e as tres dividem o MESMO contador: e o unico freio que a familia tem, porque nenhuma delas
	/// da ao alvo um tiro pra desviar.
	/// </summary>
	/// <summary>
	/// O QUE CADA SOPRO COBRA (`Ki2.0/Kiai.dm:11, :66, :74`). **UMA casa so**, e ela virou funcao
	/// quando a IA passou a decidir se vale a pena soprar (`Capacidades.CustoDoSopro`): o cerebro
	/// precisa do preco ANTES de apertar, e um `50` escrito la seria a segunda tabela de precos do
	/// jogo -- a que discorda no dia em que o dono mexer no verb.
	/// </summary>
	private static double CustoDoSopro(ServerPlayer pl, TipoDeSopro qual) =>
		(qual switch { TipoDeSopro.Kiai => 50, TipoDeSopro.Onda => 40, _ => 80 }) * pl.Ficha.BaseDrain();

	private void SoproG6(ServerPlayer pl, TipoDeSopro qual)
	{
		if (EmEspera(pl, _soproPronto, "seu Ki ainda nao se reagrupou")) return;

		double custo = CustoDoSopro(pl, qual);
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		double numerador = qual switch { TipoDeSopro.Kiai => 1000, TipoDeSopro.Onda => 4000, _ => 8000 };
		double pericia = pl.Ficha.Ekiskill * 10 + pl.Ficha.kieffusionskill + pl.Ficha.kiaiskill;
		double tiques = Math.Max(DmMath.Round(numerador / Math.Max(pericia, 1), 1), 10);

		pl.Ficha.Ki -= custo;
		_soproPronto[pl.Id] = NowMs() + (long)(tiques * TempoDoDm.MsPorTique);
		int gains = qual == TipoDeSopro.Kiai ? 1 : 2;
		for (int i = 0; i < gains; i++) pl.Ficha.BlastGain(_rng);
		pl.Ficha.kiaiskill += 0.1 * gains;   // `kiaicounter++` / `+= 2` -- usar treina
		// `Kiai.dm:15,22,27,32` (`kiaicounter++`, o sopro simples) e `:70,76` (`+= 2`, a onda e o
		// grito). O `gains` acima ja E esse delta -- ele vale 1 pro Kiai e 2 pros outros dois.
		CreditarContador(pl, "kiaicounter", gains);

		switch (qual)
		{
			case TipoDeSopro.Kiai: KiaiG6(pl); break;
			case TipoDeSopro.Onda: OndaDeChoqueG6(pl); break;
			default: DeflexaoG6(pl); break;
		}
	}

	/// <summary>
	/// A FORCA DO SOPRO, em passos de arremesso (`Ki2.0/Kiai.dm:15`):
	///
	///     (Ekioff*10 + Ekiskill*10 + kieffusionskill + kiaiskill)
	///   / (max(Ekiskill,Etechnique)*10 + max(Ekidef,Ephysdef)*10 + kicirculationskill + kicontrolskill)
	///   * BPModulus(meu, dele)
	///
	/// O PISO DE 3 VALE PRA TODO MUNDO AQUI, e no DM nao: la ele so aparece no alvo do tile DA
	/// FRENTE, e os dois tiles diagonais recebem a conta crua (`:19` e `:23` nao tem o `max`). Nao
	/// da pra reproduzir isso num mundo de posicao livre -- nao existe "o tile do meio" --, e um
	/// piso que valesse pra uma fatia de 45 graus e nao pra outra seria arbitrario. Fica o piso pra
	/// todos, que e o ramo que o proprio autor escreveu por ULTIMO.
	/// </summary>
	/// <summary>
	/// O ARCO DO SOPRO: os tres tiles da frente do DM (`get_step(dir)` mais as duas diagonais
	/// vizinhas, `Ki2.0/Kiai.dm:17-25`) traduzidos pra posicao livre -- um tile e meio de raio
	/// dentro do arco de 135 graus que o Solar Flare ja definiu.
	///
	/// REUSA O `EstaOlhandoPra` DO SOLAR FLARE (invertido: la o alvo tem que estar olhando pra mim,
	/// aqui eu tenho que estar olhando pro alvo). E a mesma pergunta geometrica, e ela ja mora no
	/// Core, testavel sem subir partida.
	/// </summary>
	private static bool NoArcoDoSoproG6(ServerPlayer pl, ServerPlayer alvo)
		=> (alvo.Pos - pl.Pos).Length <= ZoneCollision.TileSize * 1.5f
		   && Tecnicas.EstaOlhandoPra(pl.Pos, pl.Facing, alvo.Pos);

	private static int ForcaDoSoproG6(Fighter a, Fighter d)
	{
		double meu = a.Ekioff * 10 + a.Ekiskill * 10 + a.kieffusionskill + a.kiaiskill;
		double dele = Math.Max(d.Ekiskill, d.Etechnique) * 10
					  + Math.Max(d.Ekidef, d.Ephysdef) * 10
					  + d.kicirculationskill + d.kicontrolskill;
		double f = meu / Math.Max(dele, 0.0001) * CombatMath.BpModulus(a.expressedBP, d.expressedBP);
		return (int)Math.Max(Math.Round(f), 3);
	}

	/// <summary>
	/// KIAI (`Ki2.0/Kiai.dm:6`). Arremessa quem esta no arco da frente; se nao pegou ninguem, sai
	/// uma LAMINA DE AR (um blast invisivel e denso, `basedamage = 0.5*Ekioff*log_10(blastskill)`).
	///
	/// O `if(!mobaff)` do original e o que faz a tecnica valer alguma coisa a distancia: sem alvo
	/// colado ela nao e desperdicada, vira tiro. Sem essa linha, gastar 50 de Ki no vazio nao daria
	/// nada em troca -- e um jogador que erra o tempo do sopro merece o consolo que o DM deu.
	/// </summary>
	private void KiaiG6(ServerPlayer pl)
	{
		Falar(pl, Protocol.Fala.Diz, "KIAI!");
		int pegos = 0;

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o.Ficha.dead || o.Combate == null) continue;
			if (!NoArcoDoSoproG6(pl, o)) continue;

			Arremessar(o, MeleeArea.Frente(pl.Facing), pl.Ficha.expressedBP,
					   ForcaDoSoproG6(pl.Ficha, o.Ficha));
			MarcarAgressao(o, pl);
			Avisar(o, $"o sopro de {pl.Name} arranca voce do chao.");
			pegos++;
		}

		if (pegos > 0) { Avisar(pl, $"o sopro joga {pegos} pra longe."); return; }

		Disparar(pl, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast,
			BaseDano = 0.5 * pl.Ficha.Ekioff * DanoDeKi.Log10Min(pl.Ficha.blastskill, 10),
			Velocidade = 1,
			AlcanceTiles = AlcanceDeBolaG5,
			Nome = "lamina de ar",
		});
		Avisar(pl, "nao havia ninguem colado: o sopro segue em frente como uma lamina de ar.");
	}

	/// <summary>
	/// SHOCKWAVE (`Ki2.0/Kiai.dm:61`). Arremessa TODO MUNDO a um tile em volta e APAGA os tiros a
	/// dois tiles que voce tenha poder pra apagar.
	///
	/// A CONDICAO DE APAGAR E UM `OU` NO DM, e os dois lados sao diferentes de proposito:
	///   `BPModulus(meu, o do tiro) >= 1` -- eu sou mais forte que o tiro; OU
	///   `Ekioff + Ekiskill + (kieffusionskill + kiaiskill/10)*2 >= mods + basedamage` -- minha
	///   pericia bruta cobre a potencia dele.
	/// O segundo e o que deixa um tecnico apagar o tiro de alguem mais forte, e e o que faz a
	/// tecnica ser da arvore de sopro e nao da de poder.
	/// </summary>
	private void OndaDeChoqueG6(ServerPlayer pl)
	{
		Falar(pl, Protocol.Fala.Diz, "HAAA!");
		float umTile = ZoneCollision.TileSize;
		int pegos = 0;

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o.Ficha.dead || o.Combate == null) continue;
			if ((o.Pos - pl.Pos).Length > umTile * 1.5f) continue;

			// O RUMO E DE MIM PRA ELE, e nao o meu olhar: a onda e RADIAL (`oview(1)` pega os oito
			// lados), e usar o olhar mandaria quem esta atras de mim voar pra dentro de mim.
			Vec2 fora = o.Pos - pl.Pos;
			Vec2 rumo = fora.LengthSquared > 1e-4f ? fora.Normalized() : MeleeArea.Frente(pl.Facing);

			Arremessar(o, rumo, pl.Ficha.expressedBP, ForcaDoSoproG6(pl.Ficha, o.Ficha));
			MarcarAgressao(o, pl);
			Avisar(o, $"a onda de choque de {pl.Name} joga voce longe.");
			pegos++;
		}

		int apagados = 0;
		double poder = pl.Ficha.Ekioff + pl.Ficha.Ekiskill
					   + (pl.Ficha.kieffusionskill + pl.Ficha.kiaiskill / 10) * 2;

		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash).ToList())
		{
			if (!p.Vivo || p.Dono == pl.Id) continue;
			if ((p.Pos - pl.Pos).Length > umTile * 2) continue;
			if (CombatMath.BpModulus(pl.Ficha.expressedBP, p.Bp) < 1
				&& poder < p.ModsBase + p.BaseDano) continue;

			Matar(p, FimDeProjetil.Apagou);
			apagados++;
		}

		Avisar(pl, apagados > 0
			? $"a onda joga {pegos} pra longe e apaga {apagados} tiro(s) no ar."
			: $"a onda joga {pegos} pra longe.");
	}

	/// <summary>
	/// DEFLECTION (`Ki2.0/Kiai.dm:90`). TRES investidas (o `while(repeater)` com `sleep(2)`), cada
	/// uma devolvendo os tiros do arco da frente cuja forca perde pra sua.
	///
	///     strength = round((Ekioff*10 + Ekiskill*10 + kieffusionskill + kiaiskill) * expressedBP
	///                      / (M.BP * M.mods));   devolve se strength > 1
	///
	/// AS TRES INVESTIDAS VIRARAM UMA, e e o unico jeito honesto: no DM elas existem porque o verb
	/// e um laco que dorme 0,2 s entre elas -- uma janela que pega o tiro que estava a caminho. Aqui
	/// nao ha laco por jogador; agendar tres pulsos daria uma tecnica que continua defletindo depois
	/// que o jogador ja fez outra coisa. Fica UMA varredura, e o alcance dela e o arco de dois tiles
	/// que as tres cobriam somadas.
	///
	/// O DEVOLVER E O DO EMBATE DE KI, e nao um `Rumo = -Rumo`: la ja esta resolvido (e documentado)
	/// que inverter a direcao sem trocar o dono produz um tiro que ATRAVESSA quem atirou -- que e o
	/// defeito do proprio DM (`objects.dm:373`). Deflexao que nao acerta ninguem nao e deflexao.
	/// </summary>
	private void DeflexaoG6(ServerPlayer pl)
	{
		Falar(pl, Protocol.Fala.Diz, "HYAH!");
		double meu = (pl.Ficha.Ekioff * 10 + pl.Ficha.Ekiskill * 10
					  + pl.Ficha.kieffusionskill + pl.Ficha.kiaiskill) * pl.Ficha.expressedBP;
		int devolvidos = 0;

		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash).ToList())
		{
			if (!p.Vivo || p.Dono == pl.Id) continue;
			if ((p.Pos - pl.Pos).Length > ZoneCollision.TileSize * 2) continue;
			if (!Tecnicas.EstaOlhandoPra(pl.Pos, pl.Facing, p.Pos)) continue;

			double forca = meu / Math.Max(p.Bp * p.ModsBase, 0.0001);
			if (Math.Round(forca) <= 1) continue;

			// O DONO ANTIGO E O NOVO ALVO. Sem alguem pra mirar o `Devolver` nao tem pra onde
			// apontar o tiro -- e um tiro devolvido a ninguem e so um tiro que muda de dono.
			if (!_players.TryGetValue(p.Dono, out ServerPlayer? atirador)) continue;

			Devolver(p, pl, atirador);
			devolvidos++;
		}

		pl.Ficha.kidefenseskill += 0.4 * devolvidos;   // `kiaicounter += 4` por tiro tocado

		// O CONTADOR E `kiaicounter`, E NAO `kidefensecounter` -- `Kiai.dm:99,107,112,117` faz
		// `usr.kiaicounter += 4` por tiro tocado. A linha de cima escreve a PERICIA de defesa de ki,
		// que e outro sistema (pericia entra em dano; contador entra em exp de skill), e eu NAO a
		// mexi: se ela e a escolha certa ou um engano do port, e pergunta pro dono, nao pra este
		// lote. O exp segue o original.
		CreditarContador(pl, "kiaicounter", 4 * devolvidos);
		Avisar(pl, devolvidos > 0
			? $"voce devolve {devolvidos} tiro(s) a quem os atirou."
			: "voce varre a frente e nao havia nada pra devolver.");
	}

	/// <summary>
	/// EXPLOSIVE ROAR (`Ki2.0/Kiai.dm:127`). Aperta pra carregar, aperta de novo pra soltar.
	///
	/// O custo por segundo CRESCE (`Ki -= kireq + (kireq/10)*charge`), o raio e `max(charge-1,1)`
	/// tiles e a forca do arremesso e multiplicada pela carga -- mas so ate 5 (`charge = min(charge,5)`).
	/// Passar disso e pagar mais caro por nada, e o port avisa quando acontece.
	/// </summary>
	private void RugidoExplosivoG6(ServerPlayer pl)
	{
		if (_rugindo.Remove(pl.Id, out RugidoG6? carga))
		{
			SoltarRugidoG6(pl, carga);
			return;
		}
		if (EmEspera(pl, _soproPronto, "seu Ki ainda nao se reagrupou")) return;

		double custo = 50 * pl.Ficha.BaseDrain();
		if (!PodeAtirar(pl, custo, out string porque)) { Avisar(pl, porque); return; }

		_rugindo[pl.Id] = new RugidoG6 { Carga = 0, ProximoMs = NowMs() + 1000 };
		Avisar(pl, "voce comeca a juntar o rugido -- aperte de novo pra soltar.");
		MandarEfeito(pl, "rugido", -1);
	}

	/// <summary>
	/// O RUGIDO SAI. Arremessa e apaga tiros no raio da carga -- os dois efeitos da Onda de Choque,
	/// com raio e forca multiplicados pelo tempo que se segurou.
	/// </summary>
	private void SoltarRugidoG6(ServerPlayer pl, RugidoG6 carga)
	{
		MandarEfeito(pl, "rugido", 0);

		// `if(!med&&!train&&!KO&&canfight&&!KB&&!stagger)` -- quem foi derrubado no meio da carga
		// perde o rugido. E o unico jeito de a tecnica ser interrompivel: o custo ja foi pago.
		if (pl.Ficha.KO || pl.Ficha.dead || pl.Ficha.med || pl.Ficha.train || pl.TiquesDeVoo > 0)
		{
			Avisar(pl, "o rugido se perde no peito.");
			return;
		}

		int util = Math.Min(carga.Carga, CargaUtilDoRugido);
		float raio = Math.Max(util - 1, 1) * ZoneCollision.TileSize;
		for (int i = 0; i < 2; i++) pl.Ficha.BlastGain(_rng);
		pl.Ficha.kiaiskill += 0.1 * util;
		// `usr.kiaicounter += charge` (`Kiai.dm:146,151`) -- o rugido paga pela CARGA que se segurou,
		// e o `util` daqui e essa carga (ja cortada pelo teto do rugido).
		CreditarContador(pl, "kiaicounter", util);

		// COM "!" A FALA JA VIRA GRITO e o alcance cresce sozinho (`Protocol.Fala.Diz`): nao ha
		// canal separado de grito, e inventar um so pra esta linha seria protocolo novo por enfeite.
		Falar(pl, Protocol.Fala.Diz, "RAAAAAH!!");
		int pegos = 0;

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || o.Ficha.dead || o.Combate == null) continue;
			if ((o.Pos - pl.Pos).Length > raio) continue;

			Vec2 fora = o.Pos - pl.Pos;
			Vec2 rumo = fora.LengthSquared > 1e-4f ? fora.Normalized() : MeleeArea.Frente(pl.Facing);
			Arremessar(o, rumo, pl.Ficha.expressedBP, ForcaDoSoproG6(pl.Ficha, o.Ficha) * Math.Max(util, 1));
			MarcarAgressao(o, pl);
			Avisar(o, $"o rugido de {pl.Name} arranca voce do chao.");
			pegos++;
		}

		int apagados = 0;
		double poder = pl.Ficha.Ekioff + pl.Ficha.Ekiskill
					   + (pl.Ficha.kieffusionskill + pl.Ficha.kiaiskill / 10) * 2;
		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash).ToList())
		{
			if (!p.Vivo || p.Dono == pl.Id || (p.Pos - pl.Pos).Length > raio) continue;
			if (CombatMath.BpModulus(pl.Ficha.expressedBP, p.Bp) < 1
				&& poder < p.ModsBase + p.BaseDano) continue;
			Matar(p, FimDeProjetil.Apagou);
			apagados++;
		}

		// A RECARGA SO COMECA AGORA (`kiaionCD` e escrito DEPOIS do laco de carga, `:154`): segurar
		// o rugido nao consome a espera. Quem carrega vinte segundos ainda espera a recarga inteira.
		double pericia = pl.Ficha.Ekiskill * 10 + pl.Ficha.kieffusionskill + pl.Ficha.kiaiskill;
		double tiques = Math.Max(DmMath.Round(12000 / Math.Max(pericia, 1), 1), 10);
		_soproPronto[pl.Id] = NowMs() + (long)(tiques * TempoDoDm.MsPorTique);

		Avisar(pl, $"o rugido estoura num raio de {Math.Max(util - 1, 1)} tiles: "
				 + $"{pegos} arremessado(s), {apagados} tiro(s) apagado(s).");
	}

	// =====================================================================
	// 5) OS DOIS PUNHOS -- `Physical/Martial Skill Attacks.dm`
	// =====================================================================
	/// <summary>
	/// WOLF FANG FIST (`:153`) e WOLF FANG HURRICANE (`:212`).
	///
	///                          Ki              golpes   o que tem de proprio
	///     Wolf Fang Fist       Ephysoff*10xBD  3        gasta 8 de folego e ARREMESSA no fim
	///     Wolf Fang Hurricane  Ephysoff*9xBD   4        AVANCA um passo entre os golpes; nao arremessa
	///
	/// O `knockbackon = 0` DO FURACAO E A TECNICA. O verb salva o estado do knockback, DESLIGA, dá
	/// os quatro golpes e devolve (`:219` e `:245`) -- ou seja, ele garante que o alvo NAO saia
	/// voando no meio da sequencia, porque a sequencia inteira depende de ele continuar colado. Aqui
	/// isso e o `pl.Knockback` do atacante, que o `TentarEmpurrar` ja consulta.
	///
	/// A CADENCIA SAI DO PULSO DO LOTE G3, como a barragem do Multihit: o primeiro golpe sai na hora
	/// (senao a tecnica parece travada) e o resto entra na agenda de 0,2 s.
	/// </summary>
	private void PresaDoLoboG6(ServerPlayer pl, bool furacao)
	{
		if (!ProntoPraGolpeG3(pl, out string porque)) { Avisar(pl, porque); return; }

		if (EmEspera(pl, _prontoG3, "seus golpes especiais ainda se recompoem")) return;
		long agora = NowMs();
		if (_barragemG3.ContainsKey(pl.Id)) { Avisar(pl, "voce ja esta no meio de uma sequencia."); return; }

		double custo = pl.Ficha.Ephysoff * (furacao ? 9 : 10) * pl.Ficha.BaseDrain();
		if (pl.Ficha.Ki < custo) { Avisar(pl, $"isso pede {custo:0} de energia."); return; }

		// `usr.stamina < 8` -- so o Punho cobra folego, e ele cobra ANTES de escolher o alvo.
		if (!furacao && pl.Ficha.stamina < 8) { Avisar(pl, "voce esta sem folego pra isso."); return; }

		ServerPlayer? alvo = AlvoNaFrente(pl);
		if (alvo == null) { Avisar(pl, "nao ha ninguem colado em voce."); return; }

		pl.Ficha.Ki -= custo;
		if (!furacao) pl.Ficha.stamina -= 8;
		_prontoG3[pl.Id] = agora + BasicCdG3;

		Avisar(pl, furacao
			? $"voce encadeia quatro golpes em {alvo.Name}."
			: $"voce crava tres socos secos em {alvo.Name}.");
		MandarEfeito(pl, "barragem", (furacao ? 4 : 3) * BarragemPassoMs);

		// O PRIMEIRO GOLPE SAI AGORA. O `AddEffect(/effect/stun)` do Punho (`:180`) vira o mesmo
		// atordoamento curto que o resto do port usa -- e o que segura o alvo pros dois seguintes.
		if (!furacao) Travar(alvo, 0.6);
		GolpeG3(pl, alvo, addDano: furacao ? 2 : 0.5, nivel: 2);

		_barragemG3[pl.Id] = new BarragemG3
		{
			Alvo = alvo.Id,
			Faltam = furacao ? 3 : 2,
			AddDano = furacao ? 2 : 0.5,
			ProximoMs = agora + BarragemPassoMs,
			SemEmpurrao = furacao,
			AvancaPx = furacao ? ZoneCollision.TileSize : 0,
			ArremessoNoFim = furacao ? 0 : 4,
		};
		LigarPulso();
	}

	// =====================================================================
	// 6) SERVICO -- curar e ler o outro
	// =====================================================================
	/// <summary>
	/// HEAL (`misc.dm:149` + o `effector` da skill em `:120`). Cinco cargos do jogo entregam esta
	/// skill, e ate agora nenhum deles entregava nada.
	///
	///     entrada:   Ki >= MaxKi/(Ekiskill*20)
	///     por tique: Ki -= MaxKi/(Ekiskill*10) * BaseDrain, e `SpreadHeal(0.2*Ekiskill)` no alvo
	///     enquanto:  o alvo estiver a 1 tile
	///
	/// O TIQUE DO DM E 0,5 s (`accum >= 5` num efetor de 0,2 s -- na verdade 1 s de relogio de
	/// parede; ver `NiveisDeSkill.SegundosPorTique`). Aqui a cura roda no tique de 1 Hz e aplica
	/// DUAS doses de uma vez: mesma taxa por segundo, um relogio a menos no servidor. O que se perde
	/// e a granularidade de meio segundo, que ninguem consegue ver numa barra de vida.
	///
	/// O `prob(1) && prob(1)` de fazer o RABO do curandeiro crescer (`:130`) nao veio: e uma chance
	/// em dez mil por tique, escrita dentro da cura por acidente historico, e o rabo tem dono
	/// proprio neste port (`AjustarGanhoDoRabo`).
	/// </summary>
	private void CurarG6(ServerPlayer pl)
	{
		if (_curando.Remove(pl.Id)) { Avisar(pl, "voce para de curar."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "agora nao."); return; }

		ServerPlayer? alvo = Marcado(pl) ?? AlvoNaFrente(pl);
		if (alvo == null || alvo == pl) { Avisar(pl, "marque quem voce quer curar e fique ao lado dele."); return; }
		if (alvo.Combate == null || alvo.Ficha.dead) { Avisar(pl, "nao ha o que curar ai."); return; }

		double entrada = pl.Ficha.MaxKi / Math.Max(pl.Ficha.Ekiskill * 20, 1);
		if (pl.Ficha.Ki < entrada) { Avisar(pl, $"isso pede pelo menos {entrada:0} de energia."); return; }

		_curando[pl.Id] = alvo.Id;
		Avisar(pl, $"voce comeca a curar {alvo.Name}.");
		Avisar(alvo, $"{pl.Name} comeca a curar suas feridas.");
	}

	/// <summary>
	/// ASSESS KI SKILL (`KiStatsModule.dm:85`). Tres degraus de leitura, e o que muda entre eles e
	/// QUANTO voce enxerga:
	///
	///     kiawarenessskill &lt;= 25   -> so a soma: "ele sabe mais/menos que voce"
	///     kiawarenessskill &lt;= 50   -> pericia por pericia, so o sinal ("voce efunde mais que ele")
	///     kiawarenessskill &gt;  50   -> os NUMEROS dele, como se fosse a sua ficha
	///
	/// A SOMA E `KiTotal()`: o nivel somado das skills `/datum/skill/mind/*` (`:260`). Ela NAO e o
	/// BP nem a pericia de Ki -- e quanto da ARVORE de Ki a pessoa subiu, e por isso um lutador
	/// bruto e enorme pode "sentir" como novato aqui. E de proposito no original.
	///
	/// ISTO NAO E O SCOUTER, e a diferenca importa: o scouter le PODER (e o sigilo de BP se aplica a
	/// ele); isto le TREINO de ki, que o DM nao esconde de ninguem.
	/// </summary>
	private void AvaliarOKiG6(ServerPlayer pl)
	{
		ServerPlayer? alvo = Marcado(pl) ?? AlvoNaFrente(pl);
		if (alvo == null || alvo == pl) { Avisar(pl, "marque alguem primeiro."); return; }

		// `oview(20)`: o verb do DM so lista quem esta a vinte tiles.
		if ((alvo.Pos - pl.Pos).Length > 20 * ZoneCollision.TileSize)
		{
			Avisar(pl, "esta longe demais pra sentir o Ki dele.");
			return;
		}

		double aw = pl.Ficha.kiawarenessskill;
		if (aw <= 25)
		{
			double meu = TotalDeKiG6(pl), dele = TotalDeKiG6(alvo);
			Avisar(pl, meu > dele ? "voce sente que ele tem menos dominio de Ki que voce."
				 : meu < dele ? "voce sente que ele tem mais dominio de Ki que voce."
				 : "voce sente que os dois estao no mesmo ponto.");
			return;
		}

		if (aw <= 50)
		{
			Avisar(pl, $"voce avalia o Ki de {alvo.Name}:");
			ComparaG6(pl, "percepcao", pl.Ficha.kiawarenessskill, alvo.Ficha.kiawarenessskill);
			ComparaG6(pl, "efusao", pl.Ficha.kieffusionskill, alvo.Ficha.kieffusionskill);
			ComparaG6(pl, "circulacao", pl.Ficha.kicirculationskill, alvo.Ficha.kicirculationskill);
			ComparaG6(pl, "controle", pl.Ficha.kicontrolskill, alvo.Ficha.kicontrolskill);
			ComparaG6(pl, "eficiencia", pl.Ficha.kiefficiencyskill, alvo.Ficha.kiefficiencyskill);
			ComparaG6(pl, "acumulo", pl.Ficha.kigatheringskill, alvo.Ficha.kigatheringskill);
			return;
		}

		Avisar(pl, $"as pericias de Ki de {alvo.Name}:");
		Avisar(pl, $"  percepcao {alvo.Ficha.kiawarenessskill:0.#} | efusao {alvo.Ficha.kieffusionskill:0.#} "
				 + $"| circulacao {alvo.Ficha.kicirculationskill:0.#}");
		Avisar(pl, $"  controle {alvo.Ficha.kicontrolskill:0.#} | eficiencia {alvo.Ficha.kiefficiencyskill:0.#} "
				 + $"| acumulo {alvo.Ficha.kigatheringskill:0.#}");
	}

	private void ComparaG6(ServerPlayer pl, string oque, double meu, double dele)
		=> Avisar(pl, meu >= dele ? $"  voce tem mais {oque} que ele." : $"  voce tem menos {oque} que ele.");

	/// <summary>
	/// `KiTotal()` -- a soma dos NIVEIS das skills da arvore Mind (`KiStatsModule.dm:260`).
	///
	/// Le do motor de niveis e nao de um campo proprio: o nivel ja tem uma casa
	/// (<see cref="NiveisDeSkill"/>), e uma segunda copia divergiria no dia em que alguem subisse
	/// de nivel por um caminho novo.
	/// </summary>
	private static double TotalDeKiG6(ServerPlayer pl)
	{
		double t = 0;
		foreach ((string path, int nivel, double _) in pl.Niveis.Todas)
			if (path.StartsWith("/datum/skill/mind/", StringComparison.OrdinalIgnoreCase)) t += nivel;
		return t;
	}

	// =====================================================================
	// A LIMPEZA
	// =====================================================================
	/// <summary>
	/// QUEM SAIU LEVA O ESTADO DELE JUNTO -- e o `EsquecerParalisia` deste lote.
	///
	/// O MOTIVO E O MESMO, E ELE JA MORDEU: **id de jogador se REUSA**. Sem esta linha, o proximo a
	/// entrar herdaria a recarga do sopro de um desconhecido, a silaba do Kikoho dele, e -- pior --
	/// se o que saiu estava CURANDO, o tique continuaria procurando um alvo pra um curandeiro que
	/// nao existe mais, cobrando Ki de uma ficha que ninguem le.
	/// </summary>
	private void EsquecerG6(int id)
	{
		_soproPronto.Remove(id);
		_kikoho.Remove(id);
		_rugindo.Remove(id);
		_curando.Remove(id);

		// E QUEM ESTAVA SENDO CURADO POR ELE tambem para: o valor do dicionario tambem e um id.
		foreach (int curandeiro in _curando.Keys.ToList())
			if (_curando[curandeiro] == id) _curando.Remove(curandeiro);
	}

	// =====================================================================
	// O TIQUE DO LOTE -- roda junto do de 1 Hz
	// =====================================================================
	/// <summary>
	/// COBRA O ALUGUEL do que este lote deixa ligado: o escudo, a aura, a cura e a carga do rugido.
	///
	/// Roda a 1 Hz porque os quatro `Loop()` do DM cobram por segundo. O que NAO esta aqui e o dreno
	/// do Focus e da Efficiency: os dois moram no `DrainMod`, que ja multiplica o custo de tudo --
	/// cobrar de novo seria cobrar duas vezes.
	/// </summary>
	private void TickDasTecnicasG6()
	{
		long agora = NowMs();

		// --- ESCUDO DE ENERGIA: `Loop()` de `Ki2.0/KiBuffs.dm:97` ---
		foreach (int id in _buffs.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			if (!TemBuff(pl, "Energy_Shield")) continue;

			double kireq = 1 / Math.Max(pl.Ficha.kidefenseskill, 1) * pl.Ficha.BaseDrain() + 1;

			// `container.superkiarmor` ZERADO derruba o escudo: a casca existe enquanto houver
			// armadura pra ela sustentar. E o unico jeito de o escudo cair por ter feito o trabalho
			// dele, em vez de por falta de Ki.
			if (pl.Ficha.Ki < kireq || pl.Ficha.KO || pl.Ficha.superkiarmor <= 0)
			{
				DesligarBuff(pl, "Energy_Shield");
				Avisar(pl, "o escudo de energia se desfaz.");
				continue;
			}
			pl.Ficha.Ki -= kireq;
		}

		// --- FULL POWER: o `effector` de `grays.dm:41-48` ---
		foreach (int id in _buffs.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			if (!TemBuff(pl, "Full_Power")) continue;

			if (pl.Ficha.jirenskill < 100) pl.Ficha.jirenskill += 0.5;
			pl.Ficha.Ki -= DmMath.Round(100 / Math.Max(pl.Ficha.jirenskill, 1), 1) * pl.Ficha.BaseDrain();

			if (pl.Ficha.Ki > pl.Ficha.MaxKi / 20) continue;
			DesligarBuff(pl, "Full_Power");
			Avisar(pl, "voce esta cansado demais pra concentrar a aura.");
		}

		// --- A CURA ---
		foreach (int id in _curando.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _curando.Remove(id); continue; }
			if (!_players.TryGetValue(_curando[id], out ServerPlayer? alvo)
				|| alvo.Combate == null || alvo.Ficha.dead
				|| !alvo.Zone.Equals(pl.Zone)
				|| (alvo.Pos - pl.Pos).Length > ZoneCollision.TileSize * 1.5f)
			{
				_curando.Remove(id);
				Avisar(pl, "voce perde o contato e a cura para.");
				continue;
			}

			// ============================ UM DEFEITO DO ORIGINAL, CONSERTADO ============================
			// O efetor CONFERE `Ki >= MaxKi/(Ekiskill*10)` e logo abaixo COBRA o mesmo numero VEZES
			// `BaseDrain` (`misc.dm:124-126`) -- a terceira vez que este port encontra a mesma familia
			// de defeito (Guided Ball, Kill Driver, e agora a cura). Aqui, como nas outras duas, a
			// conferencia e do valor que se cobra.
			//
			// E ele nao e detalhe: `BaseDrain` cresce com a raiz do Ki maximo, entao um curandeiro de
			// 5 milhoes de Ki pagaria ~94 milhoes por tique pra devolver `0,2*Ekiskill` de vida. A cura
			// do jogo antigo era impagavel exatamente pra quem tinha Ki pra cura-la.
			double kireq = pl.Ficha.MaxKi / Math.Max(pl.Ficha.Ekiskill * 10, 1);
			if (pl.Ficha.Ki < kireq * 2 || pl.Ficha.KO || pl.Ficha.dead)
			{
				_curando.Remove(id);
				Avisar(pl, "voce nao tem mais energia pra curar.");
				continue;
			}

			pl.Ficha.Ki -= kireq * 2;
			alvo.Combate.Corpo.Curar(0.2 * pl.Ficha.Ekiskill * 2);   // as duas doses do meio segundo
			alvo.Combate.SincronizarVida();
		}

		// --- A CARGA DO RUGIDO ---
		foreach (int id in _rugindo.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _rugindo.Remove(id); continue; }
			RugidoG6 c = _rugindo[id];

			if (pl.Ficha.KO || pl.Ficha.dead || pl.Ficha.med || pl.Ficha.train)
			{
				_rugindo.Remove(id);
				MandarEfeito(pl, "rugido", 0);
				Avisar(pl, "voce perde a concentracao e o rugido se desfaz.");
				continue;
			}
			if (agora < c.ProximoMs) continue;

			double kireq = 50 * pl.Ficha.BaseDrain();
			double passo = kireq + kireq / 10 * (c.Carga + 1);
			if (pl.Ficha.Ki < passo || c.Carga >= CargaMaximaDoRugido)
			{
				// `while(charging && Ki >= kireq && charge <= 29)` -- o laco acaba e o rugido SAI
				// sozinho. Nao e cancelamento: quem carregou pagou, e o DM solta o que juntou.
				_rugindo.Remove(id);
				SoltarRugidoG6(pl, c);
				continue;
			}

			pl.Ficha.Ki -= passo;
			c.Carga++;
			c.ProximoMs = agora + 1000;
			if (c.Carga == CargaUtilDoRugido + 1)
				Avisar(pl, "o rugido ja esta no maximo -- daqui pra frente so o preco sobe.");
		}
	}
}
