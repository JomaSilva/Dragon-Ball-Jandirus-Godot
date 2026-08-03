using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// LOTE G2 -- FORMAS, CURA E TEMPO.
///
/// Sete tecnicas do DM que tem em comum a forma de agir sobre o PROPRIO corpo (ou, no caso do
/// Time Touch, sobre o corpo de quem esta do lado): multiplicam poder, somam stat, curam,
/// envelhecem, ou trocam folego por um soco. Nenhuma delas atira nada.
///
/// TRES COISAS QUE VALE LER ANTES DE MEXER AQUI:
///
/// 1. A CADENCIA DO DM NAO E 1 SEGUNDO. O `Loop()` de todo `/obj/buff` roda dentro do
///    `BuffLoop()` (buffs.dm:139), que so dispara a cada 5 voltas do `GlobalStats()`, e o
///    GlobalStats dorme 3 ticks (Stats.dm:69). Isso da 5 x 0,3s = ~1,5 SEGUNDO por volta de
///    buff. Todas as contas de dreno abaixo estao escritas com o numero LITERAL do DM e
///    divididas por <see cref="LoopDmG2"/> pra virarem taxa por segundo -- que e a cadencia do
///    tick do servidor daqui. Copiar o numero do DM direto num tick de 1 Hz cobraria 50% a mais.
///
/// 2. O ALICERCE FAZ O DESFAZER. Tudo que soma stat passa por `LigarBuff`/`DesligarBuff`
///    (GameServer.Buffs.cs), que guarda o que foi aplicado. O DM desfaz recalculando
///    (`Tphysoff -= 1.5`) e por isso funciona so por sorte -- basta a constante mudar entre o
///    ligar e o desligar pra o stat sair negativo. Aqui nao existe essa porta.
///
/// 3. O NOCAUTE DERRUBA TUDO SOZINHO. `TickDosBuffs` chama `DerrubarBuffs` em quem caiu, entao
///    um buff meu pode sumir por baixo do meu proprio dicionario. Quem depende disso (o
///    Kaio-ken) RECONCILIA no tick em vez de confiar na ordem em que os ticks foram ligados.
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// CONSTANTES DO DM
	// =====================================================================
	/// <summary>Segundos entre duas voltas de `Loop()` de buff no DM (buffs.dm:139 + Stats.dm:69).</summary>
	private const double LoopDmG2 = 1.5;

	/// <summary>`var/trans_drain = 0.2` (buffs.dm:160) -- o dreno de folego de toda forma.</summary>
	private const double TransDrainG2 = 0.2;

	private const string BuffKaiokenG2 = "Kaioken";
	private const string BuffGiganteG2 = "Giant_Form";
	private const string BuffElementalG2 = "Elemental";
	private const string BuffTimeTouchG2 = "Time_Touch";

	// =====================================================================
	// ESTADO DE SESSAO (nada disto entra no Fighter nem no ServerPlayer)
	// =====================================================================
	/// <summary>Multiplo de Kaio-ken escolhido por quem esta com ele ligado (o `kaioamount`).</summary>
	private readonly Dictionary<int, double> _kaiokenAmtG2 = [];

	/// <summary>
	/// O `KaiokenMastery`. NAO PERSISTE entre sessoes -- no DM e uma `mob/var` salva, aqui nao ha
	/// onde guardar sem inventar campo. Consequencia real: quem passa horas segurando x20 perde o
	/// progresso de maestria ao deslogar. Fica anotado como divida, nao como decisao.
	/// </summary>
	private readonly Dictionary<int, double> _kaiokenMaestriaG2 = [];

	/// <summary>`bigforming` -- a trava de 1s entre dois toques no Giant Form.</summary>
	private readonly Dictionary<int, long> _giganteTravaG2 = [];

	/// <summary>O `demi_element_type` escolhido. Ver <see cref="Elemental"/> sobre por que mora aqui.</summary>
	private readonly Dictionary<int, string> _elementoG2 = [];

	private readonly Dictionary<int, long> _prontoTimeTouchG2 = [];
	private readonly Dictionary<int, long> _prontoGrowthG2 = [];
	private readonly Dictionary<int, long> _prontoHamonG2 = [];

	/// <summary>`hamon_skill` e `hamon_skill_buffer` (Spirit.dm:263-267). Tambem so de sessao.</summary>
	private readonly Dictionary<int, double> _hamonSkillG2 = [];
	private readonly Dictionary<int, double> _hamonBufferG2 = [];

	/// <summary>
	/// A FRACAO DE ANO acumulada pelas tecnicas que envelhecem (`Age += 0.1`). `ServerPlayer.Idade`
	/// e INTEIRO, entao somar 0,1 nele nao faria nada; o resto fica aqui e vira +1 ano quando
	/// fecha. E o mais perto de fiel que da sem mexer na ficha alheia.
	/// </summary>
	private readonly Dictionary<int, double> _idadeFracaoG2 = [];

	// =====================================================================
	// REGISTRO
	// =====================================================================
	public static void RegistrarTecnicasG2()
	{
		// KAIO-KEN. O DM pergunta o multiplo num `input()` (kaioken.dm:102); aqui nao ha popup,
		// entao o NUMERO VEM NO PROPRIO ID. Cada entrada abaixo e um multiplo fixo.
		//
		// DESVIO CONSCIENTE: no DM `Kaioken_1/2/3` sao ATALHOS configuraveis (Kaioken1Set...), nao
		// multiplos -- o jogador escolhia o valor de cada slot no `Kaioken_Settings`. Aqui o numero
		// do id E o multiplo, porque um menu de configuracao de atalho nao tem canal no port.
		Tecnicas.Registrar("Kaioken", "Kaio-ken", Modo.Sustentada,
			"Sincroniza o Ki com o corpo e MULTIPLICA seu poder de verdade -- ao preco de queimar "
			+ "energia, fôlego e a própria carne, continuamente. Apertar de novo desliga. Usar um "
			+ "múltiplo acima do dobro da sua maestria e ficar sem Ki despedaça o corpo.");

		Registrarkaioken(2); Registrarkaioken(3); Registrarkaioken(5);
		Registrarkaioken(10); Registrarkaioken(20); Registrarkaioken(50); Registrarkaioken(100);

		Tecnicas.Registrar("Giant_Form", "Forma Gigante", Modo.Sustentada,
			"O corpo incha até mais de quatro vezes o tamanho normal: muito mais força e defesa "
			+ "física, e menos velocidade. Consome fôlego enquanto durar. Apertar de novo desliga.");

		Tecnicas.Registrar("Elemental", "Elemental", Modo.Sustentada,
			"Chama o elemento que dorme em você e o veste como armadura. O que ele soma depende de "
			+ "QUAL elemento é. Drena Ki e fôlego, e cai sozinho quando um dos dois acaba.");

		Tecnicas.Registrar("Time_Touch", "Toque do Tempo", Modo.Instantanea,
			"Toca alguém ao seu lado e empurra o tempo dele: um surto curto de velocidade, pago com "
			+ "um pedaço da idade de quem recebe.");

		Tecnicas.Registrar("Growth_Spurt", "Estirão", Modo.Instantanea,
			"Um Saibaman nunca para de crescer. Envelhece um mês de uma vez e, em troca, recebe uma "
			+ "carga de energia de cerca de duas vezes o seu máximo -- e um empurrão de poder.");

		Tecnicas.Registrar("HamonBreathing", "Respiração Hamon", Modo.Instantanea,
			"Respiração de emergência: gasta uma fatia do Ki máximo e devolve vida ao corpo inteiro. "
			+ "Só serve ferido, e demora muito a voltar.");

		Tecnicas.Registrar("Spirit_Fist", "Punho Espiritual", Modo.Instantanea,
			"Um soco carregado de espírito, que sai como golpe CRÍTICO garantido. É pago com FÔLEGO, "
			+ "não com Ki -- e trava seus golpes normais por alguns segundos.");
	}

	private static void Registrarkaioken(int x) => Tecnicas.Registrar($"Kaioken_{x}", $"Kaio-ken x{x}",
		Modo.Sustentada,
		$"Liga o Kaio-ken direto em x{x}. Quanto maior o múltiplo, mais poder e mais rápido o corpo "
		+ "se desfaz. Apertar de novo desliga.");

	// =====================================================================
	// DESPACHO
	// =====================================================================
	/// <summary>
	/// O despacho do lote. Devolve true se o id e meu (mesmo que a tecnica tenha sido RECUSADA --
	/// recusa tratada continua sendo tratamento).
	///
	/// O GATE DE "SABER A TECNICA" E FEITO AQUI DENTRO de proposito. `UsarTecnica` so aceita ids
	/// que aparecem literalmente na lista de verbs de alguma skill, e os aliases `Kaioken_5`,
	/// `Kaioken_20`, `Elemental_Fire` nao existem no DM -- entrariam recusados como
	/// "voce nao sabe". Cada id meu declara qual VERB BASE ele exige.
	/// </summary>
	public bool UsarTecnicasG2(ServerPlayer pl, string id)
	{
		if (id.StartsWith("Kaioken", StringComparison.OrdinalIgnoreCase))
		{
			// o menu de atalhos do DM (`Kaioken_Settings`) nao tem equivalente: era popup em cima
			// de popup. Quem apertar recebe a explicacao em vez de um botao mudo.
			if (id.Equals("Kaioken_Settings", StringComparison.OrdinalIgnoreCase))
			{
				Avisar(pl, "os atalhos de Kaio-ken viraram botões próprios (x2, x3, x5, x10, x20, x50, x100).");
				return true;
			}
			if (!SabeG2(pl, "Kaioken", "Kaio-ken")) return true;
			Kaioken(pl, NumeroDoIdG2(id));
			return true;
		}

		if (id.StartsWith("Elemental", StringComparison.OrdinalIgnoreCase))
		{
			if (!SabeG2(pl, "Elemental", "Elemental")) return true;
			Elemental(pl, SufixoDoIdG2(id));
			return true;
		}

		switch (id)
		{
			case "Giant_Form":
				if (SabeG2(pl, "Giant_Form", "Forma Gigante")) FormaGigante(pl);
				return true;
			case "Time_Touch":
				if (SabeG2(pl, "Time_Touch", "Toque do Tempo")) ToqueDoTempo(pl);
				return true;
			case "Growth_Spurt":
				if (SabeG2(pl, "Growth_Spurt", "Estirão")) Estirao(pl);
				return true;
			case "HamonBreathing":
				if (SabeG2(pl, "HamonBreathing", "Respiração Hamon")) RespiracaoHamon(pl);
				return true;
			case "Spirit_Fist":
				if (SabeG2(pl, "Spirit_Fist", "Punho Espiritual")) PunhoEspiritual(pl);
				return true;
		}
		return false;
	}

	/// <summary>Apelido, caso o dispatch la de fora chame pelo nome no singular.</summary>
	public bool UsarTecnicaG2(ServerPlayer pl, string id) => UsarTecnicasG2(pl, id);

	private bool SabeG2(ServerPlayer pl, string verbBase, string nome)
	{
		if (SabeTecnica(pl, verbBase)) return true;
		Avisar(pl, $"você não sabe {nome}.");
		return false;
	}

	/// <summary>"Kaioken_20" -> 20. Zero quando nao ha numero (o id nu).</summary>
	private static double NumeroDoIdG2(string id)
		=> double.TryParse(SufixoDoIdG2(id), System.Globalization.NumberStyles.Float,
						   System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

	/// <summary>"Elemental_Fire" -> "Fire". Vazio quando o id nao tem sufixo.</summary>
	private static string SufixoDoIdG2(string id)
	{
		int i = id.IndexOf('_');
		return i < 0 || i + 1 >= id.Length ? "" : id[(i + 1)..];
	}

	// =====================================================================
	// 1. KAIO-KEN  (kaioken.dm:98 / SetKaioken :129 / o buff :162)
	// =====================================================================
	/// <summary>
	/// A TABELA DE MULTIPLICADOR REAL, literal de `kaioken.dm:11-19`.
	///
	/// O "x20" do nome NAO e o multiplicador: e o TIER. x2 vale 1,2x de poder, x20 vale 1,75x,
	/// x100 vale 4x. Isso e do rework "kaioken virou multiplicador de verdade" -- antes o numero
	/// do nome multiplicava direto e um x100 dava cem vezes o poder.
	/// </summary>
	private static double KaiokenMultG2(double amt)
	{
		if (amt <= 2) return 1.2;
		if (amt <= 3) return 1.2 + (amt - 2) * 0.1;
		if (amt <= 4) return 1.3 + (amt - 3) * 0.1;
		if (amt <= 10) return 1.4 + (amt - 4) * 0.1 / 6;
		if (amt <= 20) return 1.5 + (amt - 10) * 0.25 / 10;
		if (amt <= 50) return 1.75 + (amt - 20) * 0.25 / 30;
		if (amt <= 100) return 2.0 + (amt - 50) * 2.0 / 50;
		return 4.0;
	}

	/// <summary>
	/// A MAESTRIA INICIAL. `1` e o default da `mob/var` (kaioken.dm:3), mas quem APRENDE a skill
	/// no nivel 1 ganha `+3` no `after_learn` (kaioken.dm:93) -- ou seja, ninguem que possa usar o
	/// verb tem maestria 1. Comecar em 1 aqui faria x3 explodir o corpo de um novato na primeira
	/// vez que o Ki acabasse; 4 e o numero de quem de fato destravou a tecnica.
	/// </summary>
	private const double KaiokenMaestriaInicialG2 = 4;

	/// <summary>Teto do Kaio-ken empilhado sobre o Blue (`GODKI_KAIOKEN_CAP`, 1A Defines.dm:57).</summary>
	private const double KaiokenCapNoBlueG2 = 20;

	private double MaestriaKaiokenG2(ServerPlayer pl)
		=> _kaiokenMaestriaG2.TryGetValue(pl.Id, out double m) ? m : KaiokenMaestriaInicialG2;

	private void Kaioken(ServerPlayer pl, double pedido)
	{
		// TOGGLE: com o Kaio-ken de pe, qualquer botao da familia DESLIGA. E o que o DM faz na
		// primeira linha de todos os cinco verbs (`if(usr.KaioPcnt != 1) usr.KaiokenRevert()`).
		if (TemBuff(pl, BuffKaiokenG2)) { ReverterKaiokenG2(pl, porKO: false); return; }

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "caído não se acende Kaio-ken."); return; }

		// `if(src.powerMod>1)` -- SetKaioken:135. Carregar Ki e Kaio-ken sao a mesma tomada.
		if (pl.Ficha.powerMod > 1)
		{
			Avisar(pl, "solte os aumentos de poder antes: o Kaio-ken não empilha em cima deles.");
			return;
		}

		double amt = pedido > 0 ? pedido : 2;   // id nu ("Kaioken") = o x2 nativo da tecnica

		// KAIO-KEN NO BLUE (SetKaioken:141-147): dentro de forma divina COM SSJ empilhado, so
		// Normal/Low-Class com 50% de maestria aguentam, e no maximo x20.
		if (pl.Ficha.godki is { usage: true } gk && (pl.Ficha.ssj > 0 || pl.Ficha.lssj > 0))
		{
			if (!(pl.Ficha.Class is "Normal" or "Low-Class") || gk.mastery < GodKiState.RoyalePct)
			{
				Avisar(pl, "seu corpo não aguenta o Kaio-ken por cima do ki divino transformado.");
				return;
			}
			if (amt > KaiokenCapNoBlueG2)
			{
				amt = KaiokenCapNoBlueG2;
				Avisar(pl, $"sobre o Blue, o Kaio-ken vai até x{KaiokenCapNoBlueG2:0} -- além disso o corpo se despedaça.");
			}
		}

		// `if(src.kaioamount<2) src.kaioamount=2` e `round()` (SetKaioken:148-150). O `round()` de
		// um argumento no DM e PISO, nao arredondamento -- por isso Math.Floor.
		amt = Math.Floor(Math.Max(amt, 2));
		if (amt > 100) amt = 100;

		double maestria = MaestriaKaiokenG2(pl);
		double mult = KaiokenMultG2(amt);

		// KaioPcnt e da familia 2 do powerlevel (soma na BASE via additiveBoost), e vale 1 quando
		// desligado. Por isso o buff soma (mult - 1): ligado vira `mult`, desligado volta a 1.
		if (!LigarBuff(pl, BuffKaiokenG2, "Kaio-ken", new() { ["KaioPcnt"] = mult - 1 }))
		{
			Avisar(pl, "o Kaio-ken já está aceso.");
			return;
		}

		_kaiokenAmtG2[pl.Id] = amt;
		_kaiokenMaestriaG2[pl.Id] = maestria;

		Falar(pl, Protocol.Fala.Diz, amt < 3 ? "KAIOKEN!!" : $"KAIOKEN TIMES {amt:0}!!");
		Avisar(pl, $"uma aura vermelha estoura em volta de você (x{mult:0.##} de poder).");

		// O DM nao avisa: ele so explode voce quando o Ki acaba (o buff Loop:199). Avisar nao muda
		// regra nenhuma -- so evita que a unica forma de descobrir a regra seja perder o corpo.
		if (amt > maestria * 2)
			Avisar(pl, $"x{amt:0} está acima do dobro da sua maestria (x{maestria:0.#}): se o Ki acabar, "
					 + "seu corpo se despedaça.");

		GD.Print($"[server] {pl.Name}: Kaio-ken x{amt:0} (x{mult:0.##} de poder, maestria {maestria:0.##})");
	}

	/// <summary>
	/// DESLIGA e cobra a conta de desligar (`KaiokenRevert`, kaioken.dm:233).
	///
	/// Duas saidas diferentes, e a diferenca importa: sair por vontade propria e de graca; sair
	/// CAIDO com um multiplo acima de 1,6x a maestria arrebenta o corpo.
	/// </summary>
	private void ReverterKaiokenG2(ServerPlayer pl, bool porKO)
	{
		double amt = _kaiokenAmtG2.GetValueOrDefault(pl.Id, 0);
		double maestria = MaestriaKaiokenG2(pl);
		_kaiokenAmtG2.Remove(pl.Id);
		DesligarBuff(pl, BuffKaiokenG2);   // idempotente: pode ja ter caido no DerrubarBuffs

		if (!porKO) { Avisar(pl, "você para de usar o Kaio-ken."); return; }

		Avisar(pl, "você está cansado demais pra continuar no Kaio-ken.");
		if (amt > maestria * 1.6) EstourarG2(pl, "seu corpo se despedaça de dentro pra fora.");
	}

	// =====================================================================
	// 2. FORMA GIGANTE  (Giant Form.dm:50)
	// =====================================================================
	/// <summary>`spawn(10) bigforming = 0` -- 10 ticks do BYOND, um segundo.</summary>
	private const long GiganteTravaMsG2 = 1000;

	/// <summary>
	/// `giant_form_efficiency` (Giant Form.dm:49). Ela cai pra 0,5 no nivel 1 e 0,15 no nivel 2 da
	/// skill; o port NAO tem niveis de skill, entao fica no valor de nivel 0. Efeito pratico: o
	/// gigante consome mais folego aqui do que consumiria pra quem treinou a forma la.
	/// </summary>
	private const double GiganteEficienciaG2 = 1;

	/// <summary>
	/// Segundos medios entre dois drenos de folego do gigante.
	///
	/// O DM enche um `drainbuffer += rand(1,5)` por volta e so cobra quando passa de 20 (Giant
	/// Form.dm:80). Media de 3 por volta = 6,67 voltas = ~10 segundos. Cobrar a fracao equivalente
	/// todo segundo da a MESMA taxa media sem carregar um sorteio por jogador por tick.
	/// </summary>
	/// <summary>
	/// Quantas voltas do Loop do DM cabem numa cobrança de folego do Gigante: o `drainbuffer`
	/// passa de 20 somando ~3 por volta (Giant Form.dm), logo ~6,67 voltas -- e o Loop e de um
	/// segundo. Estava 10 aqui, e por isso o dreno saia 1,5x mais lento que o do original.
	/// </summary>
	private const double VoltasPorCobrancaG2 = 20.0 / 3.0;

	private void FormaGigante(ServerPlayer pl)
	{
		long agora = NowMs();
		if (_giganteTravaG2.TryGetValue(pl.Id, out long livre) && agora < livre)
		{
			Avisar(pl, $"seu corpo ainda está mudando de tamanho ({(livre - agora) / 1000.0:0.#}s).");
			return;
		}
		_giganteTravaG2[pl.Id] = agora + GiganteTravaMsG2;

		if (DesligarBuff(pl, BuffGiganteG2))
		{
			Avisar(pl, "você solta a energia... e o tamanho.");
			return;
		}

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "caído não se cresce."); return; }

		// Os numeros sao os do `obj/buff/Giant_Form.Buff()` (Giant Form.dm:67-77):
		//   giantFormbuff = 1.55  -> multiplica BP (entra no buffBuff do powerlevel)
		//   Tphysoff += 1.5 / Tphysdef += 1.5 / Tspeed -= 0.5
		// giantFormbuff vale 1 desligado, entao o buff soma 0,55 e o desligar devolve.
		//
		// O QUE NAO DEU PRA PORTAR: `animate(container, transform = matrix()*2, time = 5)` -- o
		// sprite dobrando de tamanho. Isso e do CLIENTE, e o `MandarEfeito` que o `LigarBuff` ja
		// dispara ("Giant_Form", duracao -1) e exatamente o gancho pra ele fazer isso quando
		// alguem escrever a parte visual. Daqui nao ha como.
		LigarBuff(pl, BuffGiganteG2, "Forma Gigante", new()
		{
			["giantFormbuff"] = 0.55,
			["Tphysoff"] = 1.5,
			["Tphysdef"] = 1.5,
			["Tspeed"] = -0.5,
		});

		Avisar(pl, "você concentra a energia e cresce -- muito.");
		GD.Print($"[server] {pl.Name}: Forma Gigante ligada");
	}

	// =====================================================================
	// 3. ELEMENTAL  (demigod.dm:124, o buff em :129)
	// =====================================================================
	/// <summary>
	/// O QUE CADA ELEMENTO SOMA -- tabela literal de `obj/buff/elemental.Buff()` (demigod.dm:136-172).
	///
	/// STEAM E CLOUD SAO IDENTICOS no DM (+0,5 Tspeed, +0,5 Ttechnique), apesar de o texto do
	/// Cloud dizer que ele esta "acima de todos". E copia-e-cola no original, nao desenho -- fica
	/// portado como esta pra nao mudar balanceamento por conta propria.
	/// </summary>
	private static readonly Dictionary<string, Dictionary<string, double>> ElementosG2 =
		new(StringComparer.OrdinalIgnoreCase)
		{
			["Fire"] = new() { ["Tphysoff"] = 1 },
			["Earth"] = new() { ["Tphysdef"] = 1 },
			["Air"] = new() { ["Tspeed"] = 1 },
			["Water"] = new() { ["Ttechnique"] = 1 },
			["Lightning"] = new() { ["Tphysoff"] = 0.5, ["Tspeed"] = 0.5 },
			["Mud"] = new() { ["Ttechnique"] = 0.5, ["Tphysdef"] = 0.5 },
			["Lava"] = new() { ["Tphysoff"] = 0.5, ["Tphysdef"] = 0.5 },
			["Sand"] = new() { ["Tphysdef"] = 0.5, ["Tspeed"] = 0.5 },
			["Steam"] = new() { ["Tspeed"] = 0.5, ["Ttechnique"] = 0.5 },
			["Cloud"] = new() { ["Ttechnique"] = 0.5, ["Tspeed"] = 0.5 },
		};

	/// <summary>
	/// LIGA (ou desliga) o elemento.
	///
	/// DUAS DECISOES QUE PRECISAM ESTAR ESCRITAS:
	///
	/// 1. O ELEMENTO E ESCOLHIDO PELO ID. No DM ele e perguntado num `input()` dentro do
	///    `after_learn` (demigod.dm:81) e guardado em `mob/var/demi_element_type`. O port nao tem
	///    nem popup nem esse campo -- entao a escolha chega como `Elemental_Fire`, `Elemental_Mud`,
	///    e mora num dicionario de SESSAO. NAO existe elemento padrao: sem escolher, a tecnica
	///    recusa e lista as dez opcoes. Inventar um elemento fixo daria stat de graca pra metade
	///    dos Demigods e nenhum pra outra metade, sem ninguem saber por que.
	///
	/// 2. VIROU TOGGLE. O DM so tem `startbuff` (demigod.dm:127) -- nao havia como desligar a nao
	///    ser pelo verb global `Clear_Buffs` ou ficando sem folego. Um buff que drena Ki e nao
	///    desliga e armadilha; aqui apertar de novo desliga.
	///
	/// FORA: o `demi_true_type` (Hellfire/Steel Ice/Atmosphere/Primordial, +1 em dois stats e
	/// `aurasBuff = 1.5`). Ele so e escolhido quando a ASCENSAO comeca (demigod.dm:87), e o port
	/// nao tem Ascensao -- portar o efeito sem a porta que o abre seria dar de graca.
	/// </summary>
	private void Elemental(ServerPlayer pl, string escolha)
	{
		if (escolha.Length > 0)
		{
			if (!ElementosG2.ContainsKey(escolha))
			{
				Avisar(pl, $"'{escolha}' não é um elemento. Escolha entre: {string.Join(", ", ElementosG2.Keys)}.");
				return;
			}
			_elementoG2[pl.Id] = escolha;
			Avisar(pl, $"o elemento que dormia em você tinha nome: {escolha}.");
		}

		if (DesligarBuff(pl, BuffElementalG2)) { Avisar(pl, "o elemento recua."); return; }

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "caído não se chama elemento nenhum."); return; }

		if (!_elementoG2.TryGetValue(pl.Id, out string? elem))
		{
			Avisar(pl, "você ainda não lembrou qual é o seu elemento. Escolha um: "
					 + string.Join(", ", ElementosG2.Keys) + ".");
			return;
		}

		LigarBuff(pl, BuffElementalG2, $"Elemental ({elem})", new(ElementosG2[elem]));
		Avisar(pl, $"o elemento {elem} envolve seu corpo.");
		GD.Print($"[server] {pl.Name}: Elemental ({elem}) ligado");
	}

	// =====================================================================
	// 4. TOQUE DO TEMPO  (kanassa-jin.dm:79)
	// =====================================================================
	/// <summary>
	/// `m.TempBuff("Tspeed"=2, 30)` -- 30 e em ticks do BYOND (deciSEGUNDOS), ou seja TRES
	/// SEGUNDOS. Parece pouco e e pouco mesmo: no DM o Time Touch e um empurrao de um lance, nao
	/// um buff de luta.
	/// </summary>
	private const long TimeTouchDuracaoMsG2 = 3000;

	/// <summary>
	/// RECARGA QUE NAO EXISTE NO DM, e por que ela existe aqui.
	///
	/// La o freio e o `stored_time`: cada toque gasta 1, e o estoque so enche 10 por ANO de jogo
	/// (kanassa-jin.dm:63). O port nao tem calendario nem esse estoque -- sem nenhum freio a
	/// tecnica viraria +2 de velocidade permanente pra um grupo inteiro, bastando marteria-la.
	/// 30 segundos e numero MEU, escolhido pra o buff (3s) ser um lance e nao um estado.
	/// </summary>
	private const long TimeTouchRecargaMsG2 = 30_000;

	/// <summary>
	/// Alcance do toque, em pixels. `view(1)` no DM e o anel de 1 tile em volta -- ate ~1,41 tile
	/// na diagonal. 1,5 tile e o equivalente honesto num mundo de posicao livre.
	/// </summary>
	private const float TimeTouchAlcanceG2 = 48f;

	private void ToqueDoTempo(ServerPlayer pl)
	{
		long agora = NowMs();
		if (_prontoTimeTouchG2.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{
			Avisar(pl, $"o tempo ainda não voltou pra sua mão ({(pronto - agora) / 1000.0:0.#}s).");
			return;
		}
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condições."); return; }

		ServerPlayer? alvo = AoLadoG2(pl, TimeTouchAlcanceG2);
		if (alvo == null) { Avisar(pl, "não há ninguém ao seu alcance pra tocar."); return; }

		if (!LigarBuff(alvo, BuffTimeTouchG2, "Toque do Tempo",
					   new() { ["Tspeed"] = 2 }, duracaoMs: TimeTouchDuracaoMsG2))
		{
			Avisar(pl, $"{alvo.Name} ainda está carregando o seu último toque.");
			return;
		}

		// O PRECO SAI EM IDADE, no ALVO (`m.Age += 0.1`). Ver o comentario do dicionario:
		// ServerPlayer.Idade e inteiro, entao a fracao se acumula e vira ano fechado.
		EnvelhecerG2(alvo, 0.1);

		_prontoTimeTouchG2[pl.Id] = agora + TimeTouchRecargaMsG2;
		Avisar(pl, $"você toca {alvo.Name} e empurra o tempo dele.");
		Avisar(alvo, $"{pl.Name} te toca: o mundo desacelera em volta -- e você envelhece um pouco.");
		GD.Print($"[server] {pl.Name}: Time Touch em {alvo.Name}");
	}

	/// <summary>
	/// Quem esta ao lado: o ALVO MARCADO primeiro (se estiver perto), senao o mais proximo dentro
	/// do alcance. Mesma prioridade do resto do combate -- quem marcou alguem ja disse em quem quer
	/// agir, e pedir a mesma informacao duas vezes e o que o `input() in view(1)` do DM fazia.
	/// </summary>
	private ServerPlayer? AoLadoG2(ServerPlayer pl, float alcance)
	{
		if (Marcado(pl) is { } mira && (mira.Pos - pl.Pos).LengthSquared <= alcance * alcance) return mira;

		ServerPlayer? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			if (o == pl || o.Ficha.dead) continue;
			float d2 = (o.Pos - pl.Pos).LengthSquared;
			if (d2 > alcance * alcance || d2 >= melhorDist) continue;
			melhorDist = d2;
			melhor = o;
		}
		return melhor;
	}

	// =====================================================================
	// 5. ESTIRAO  (saibaman.dm:53)
	// =====================================================================
	/// <summary>
	/// RECARGA QUE NAO EXISTE NO DM. La o unico freio e o `Age += 0.1` -- e envelhecer custa caro
	/// num jogo com morte por velhice. O port nao tem morte por idade, entao sem recarga isto
	/// seria `Ki += MaxKi*2` infinito -- e o Ki acima de 100% entra LINEAR no BP expresso
	/// (`kiratio`, Fighter.Power.cs:40), ou seja: BP infinito com dois cliques.
	/// 120 segundos e numero MEU, e existe so pra tapar esse buraco.
	/// </summary>
	private const long GrowthRecargaMsG2 = 120_000;

	private void Estirao(ServerPlayer pl)
	{
		long agora = NowMs();
		if (_prontoGrowthG2.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{
			Avisar(pl, $"seu corpo ainda está assentando o último estirão ({(pronto - agora) / 1000.0:0.#}s).");
			return;
		}

		// `if(IsAVampire||immortal||biologicallyimmortal||dead||DeathRegen<2)` (saibaman.dm:57).
		//
		// DEFEITO DO DM, NAO PORTADO: a mensagem diz que "grandes quantidades de Regeneração de
		// Morte" bloqueiam, mas a condicao testa `DeathRegen<2` -- ou seja, bloqueia quem tem
		// POUCA. Esta invertida. Como nem DeathRegen nem imortalidade nem vampirismo existem no
		// port, sobra o unico gate que da pra checar: estar morto.
		if (pl.Ficha.dead) { Avisar(pl, "um cadáver não cresce."); return; }

		EnvelhecerG2(pl, 0.1);          // "um mês de idade" -- o texto do proprio verb
		pl.Ficha.Ki += pl.Ficha.MaxKi * 2;
		pl.Ficha.AttackGain(_rng, 5);   // `Attack_Gain(5)`

		_prontoGrowthG2[pl.Id] = agora + GrowthRecargaMsG2;
		pl.Ficha.Statify();
		Avisar(pl, $"você cresce de uma vez: a energia transborda ({pl.Ficha.Ki:0} de {pl.Ficha.MaxKi:0}).");
		GD.Print($"[server] {pl.Name}: Growth Spurt (Ki {pl.Ficha.Ki:0})");
	}

	/// <summary>
	/// `Age += fracao`. `ServerPlayer.Idade` e INTEIRO -- a fracao se acumula aqui e so vira ano
	/// quando fecha. Sem isto, todo `Age += 0.1` do DM seria silenciosamente descartado.
	/// </summary>
	private void EnvelhecerG2(ServerPlayer pl, double fracao)
	{
		double acc = _idadeFracaoG2.GetValueOrDefault(pl.Id) + fracao;
		while (acc >= 1) { acc -= 1; pl.Idade++; }
		_idadeFracaoG2[pl.Id] = acc;
		pl.SigAtributos = "";   // a idade vai no pacote de atributos: forca o proximo a sair
	}

	// =====================================================================
	// 6. RESPIRACAO HAMON  (Spirit.dm:246)
	// =====================================================================
	/// <summary>
	/// `mob/var/HBCost = 25` (Spirit.dm:242). O nivel 1 da skill baixa pra 12 e o nivel 2 pra 5 --
	/// o port nao tem niveis de skill, entao fica o de nivel 0: a cura mais CARA das tres.
	/// </summary>
	private const double HamonCustoDivisorG2 = 25;

	/// <summary>Quantos usos enchem o `hamon_skill_buffer` ate destravar +1 (Spirit.dm:264).</summary>
	private const double HamonUsosPraSkillG2 = 25;

	private void RespiracaoHamon(ServerPlayer pl)
	{
		long agora = NowMs();
		if (_prontoHamonG2.TryGetValue(pl.Id, out long pronto) && agora < pronto)
		{
			Avisar(pl, $"sua respiração Hamon ainda está se refazendo ({(pronto - agora) / 1000.0:0.#}s).");
			return;
		}

		double kireq = pl.Ficha.MaxKi / HamonCustoDivisorG2;

		// `usr.Ki>=kireq && usr.HP<100 && !usr.blasting && !usr.KO` -- Spirit.dm:250.
		// (`blasting` nao existe no port: nao ha projeteis. Cai fora sozinho.)
		if (pl.Ficha.Ki < kireq) { Avisar(pl, $"isso pede pelo menos {kireq:0} de energia."); return; }
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condições."); return; }
		if (pl.Combate == null || pl.Ficha.HP >= 100) { Avisar(pl, "seu corpo já está inteiro."); return; }

		double skill = _hamonSkillG2.GetValueOrDefault(pl.Id);

		// `HBcooldown = 180 - hamon_skill*8` (Spirit.dm:256).
		//
		// UNIDADE -- e aqui o DM se contradiz. A mensagem diz "[HBcooldown] seconds" (180), mas o
		// laco que conta faz `HBcooldown -= 10` a cada `sleep(10)` (Spirit.dm:258-261): subtrai
		// DEZ por segundo, entao a espera REAL no jogo antigo e um decimo disso -- 18 s.
		//
		// VALE O COMPORTAMENTO, NAO O TEXTO. Portar 180 s seria dez vezes a espera que qualquer
		// jogador daquele jogo ja sentiu, e ninguem reconheceria a tecnica. Entre o numero escrito
		// e o numero que o jogo entrega, quem manda e o que o jogo entrega -- o texto era o bug.
		double recargaS = Math.Max((180 - skill * 8) / 10.0, 1);

		// `usr.SpreadHeal(1*willpowerMod + hamon_skill)` -- Spirit.dm:270. A cura vai pra CADA
		// membro; como o HP do port e a media dos membros, o efeito no HP e o mesmo numero.
		double cura = 1 * pl.Ficha.willpowerMod + skill;
		pl.Combate.Corpo.Curar(cura);
		pl.Combate.SincronizarVida();

		pl.Ficha.Ki -= kireq;
		_prontoHamonG2[pl.Id] = agora + (long)(recargaS * 1000);

		// USAR ENSINA: `hamon_skill_buffer += 1`, e aos 25 usos `hamon_skill += 1` UMA unica vez
		// (a condicao `&& !usr.hamon_skill` trava em 1). Portado com a trava.
		if (skill == 0)
		{
			double buffer = _hamonBufferG2.GetValueOrDefault(pl.Id) + 1;
			if (buffer >= HamonUsosPraSkillG2)
			{
				_hamonBufferG2[pl.Id] = 0;
				_hamonSkillG2[pl.Id] = 1;
				Avisar(pl, "seu corpo ondula: a respiração ficou mais profunda.");
			}
			else _hamonBufferG2[pl.Id] = buffer;
		}

		Avisar(pl, $"você respira fundo e a luz corre pelo corpo (+{cura:0.##} de vida).");
	}

	// =====================================================================
	// 7. PUNHO ESPIRITUAL  (Spirit.dm:441)
	// =====================================================================
	/// <summary>`mob/var/SpiritFistCost = 2` (Spirit.dm:438). Os niveis da skill dividem por 2; sem niveis no port, fica 2.</summary>
	private const double SpiritFistCustoG2 = 2;

	/// <summary>`MeleeAttack(SpiritFistDamage*3, TRUE)` com `SpiritFistDamage = 1` -> +3 de dano plano.</summary>
	private const double SpiritFistDanoG2 = 3;

	/// <summary>`usr.basicCD += 45` -- 45 ticks do BYOND, 4,5 segundos.</summary>
	private const double SpiritFistRecargaSG2 = 4.5;

	/// <summary>
	/// SOCO CRITICO PAGO COM FOLEGO.
	///
	/// O CRITICO E FORCADO DE VERDADE, e vale explicar como. O `MeleeResolver.Resolver` decide o
	/// crit sozinho (`ChanceCrit`) e nao tem parametro de forcar -- e o DM forca de fato aqui: o
	/// `MeleeAttack(dano, TRUE)` passa `iscrit` na posicao certa (diferente do soco comum, onde o
	/// `hitProc` recebia cinco argumentos pra seis parametros e o crit nunca chegava; e o defeito 1
	/// documentado no proprio MeleeResolver).
	///
	/// A saida foi levantar o botao global de chance de crit pra 100 em volta da UNICA chamada, com
	/// `finally` garantindo a volta. E feio, e e de proposito: a alternativa era escrever uma
	/// segunda cadeia de dano so pra esta tecnica -- que e exatamente o defeito 4 do original
	/// (`AttackMultiple` com o proprio caminho paralelo, embaralhando argumentos). Duas cadeias de
	/// dano divergem; um knob restaurado no finally, nao.
	/// </summary>
	private void PunhoEspiritual(ServerPlayer pl)
	{
		CombatState ca = pl.Combate;
		if (ca == null) return;

		// `!usr.med && !usr.train && !usr.KO && !usr.basicCD && usr.canfight` -- Spirit.dm:444.
		if (pl.Ficha.med || pl.Ficha.train) { Avisar(pl, "não dá, concentrado desse jeito."); return; }
		if (!ca.PodeAtacar())
		{
			Avisar(pl, ca.Recarga > 0
				? $"seus golpes ainda estão se recompondo ({ca.Recarga:0.#}s)."
				: "você não está em condições de golpear.");
			return;
		}

		// `var/kireq = angerBuff * usr.Ephysoff * SpiritFistCost` -- e sai da STAMINA, nao do Ki
		// (o nome da variavel no DM e "kireq", mas o desconto e `usr.stamina -= kireq`).
		double custo = pl.Ficha.angerBuff * pl.Ficha.Ephysoff * SpiritFistCustoG2;
		if (pl.Ficha.stamina < custo)
		{
			Avisar(pl, $"isso pede {custo:0.#} de fôlego (você tem {pl.Ficha.stamina:0.#}).");
			return;
		}

		if (Marcado(pl) is { } mira)
			pl.Facing = Jandirus.Core.World.MoveRules.FacingFrom(mira.Pos - pl.Pos, pl.Facing);

		// Fechar a distancia ANTES de bater e o que o `Attack()`/`MeleeAttack` do DM faz, e e o
		// mesmo caminho do soco comum daqui -- sem isso o punho sai no vazio por meio tile.
		Aproximar(pl, longo: false);

		// A RECARGA E COBRADA SEMPRE, mesmo errando: `usr.basicCD += 45` acontece ANTES do
		// `if(MeleeAttack(...))`. So o FOLEGO e que depende de ter encontrado alguem.
		ca.Recarga = Math.Max(ca.Recarga, SpiritFistRecargaSG2);
		pl.AtaqueAte = NowMs() + (long)(SpiritFistRecargaSG2 * 1000);
		ca.Guardar(false);

		ServerPlayer? alvo = AlvoNaFrente(pl);
		if (alvo == null)
		{
			pl.Ficha.AttackGain(_rng);
			ca.ZerarCombo();
			AnunciarSocoNoAr(pl);
			Avisar(pl, "o punho espiritual passa pelo vazio.");
			return;
		}

		CombatState cd = alvo.Combate;
		double angulo = MeleeArea.AnguloDeChegada(alvo.Pos, alvo.Facing, pl.Pos);

		GolpeResultado r;
		double critAntes = CombatKnobs.ChanceCrit;
		try
		{
			CombatKnobs.ChanceCrit = 100;
			r = MeleeResolver.Resolver(ca, cd, angulo, _rng, tipo: 1, addDano: SpiritFistDanoG2);
		}
		finally { CombatKnobs.ChanceCrit = critAntes; }

		pl.Ficha.stamina -= custo;
		alvo.UltimoAgressor = pl.Id;

		if (r.Encostou) ca.SomarCombo(); else ca.ZerarCombo();

		pl.Ficha.AttackGain(_rng, pl.Ficha.FightGainMult(alvo.Ficha));
		if (r.Encostou) alvo.Ficha.AttackGain(_rng, alvo.Ficha.FightGainMult(pl.Ficha));

		// O CONTRA CONTINUA VALENDO. Deixar o punho ignorar a guarda perfeita faria dele o jeito
		// de furar o sistema de contra-ataque -- e ai ninguem mais usaria soco.
		if (r.Desfecho == Desfecho.Contra)
		{
			GolpeResultado devolta = MeleeResolver.Resolver(
				cd, ca, MeleeArea.AnguloDeChegada(pl.Pos, pl.Facing, alvo.Pos), _rng, tipo: 1);
			pl.UltimoAgressor = alvo.Id;
			ResolverDesfecho(alvo, pl, devolta);
			AnunciarGolpe(alvo, pl, devolta, 2);
		}

		ResolverDesfecho(pl, alvo, r);
		AnunciarGolpe(pl, alvo, r, 3);   // sempre o baque grande: e um golpe carregado
		Avisar(pl, $"seu punho espiritual estoura em {alvo.Name} (-{custo:0.#} de fôlego).");
	}

	// =====================================================================
	// O TICK DO LOTE -- precisa ser chamado UMA VEZ POR SEGUNDO
	// =====================================================================
	/// <summary>
	/// COBRA O ALUGUEL das tres tecnicas sustentadas do lote e derruba as que nao se pagam mais.
	///
	/// UMA VEZ POR SEGUNDO, junto de <see cref="TickDasTecnicas"/>. As contas abaixo sao as do DM
	/// divididas por <see cref="LoopDmG2"/> -- ver o comentario de cabecalho da classe.
	/// </summary>
	public void TickTecnicasG2()
	{
		TickKaiokenG2();
		TickGiganteG2();
		TickElementalG2();
	}

	/// <summary>
	/// O `Loop()` do buff de Kaio-ken (kaioken.dm:179-204), por segundo.
	///
	/// A RECONCILIACAO NO TOPO NAO E ZELO: `TickDosBuffs` derruba TODO buff de quem caiu, e ele
	/// pode rodar antes ou depois deste tick dependendo de como o dispatch foi ligado. Se o buff
	/// sumiu por baixo do dicionario, o desligar precisa acontecer do mesmo jeito -- inclusive a
	/// explosao de quem estava acima da propria maestria.
	/// </summary>
	private void TickKaiokenG2()
	{
		foreach (int id in _kaiokenAmtG2.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) { _kaiokenAmtG2.Remove(id); continue; }

			double amt = _kaiokenAmtG2[id];
			double maestria = MaestriaKaiokenG2(pl);

			if (pl.Ficha.KO || pl.Ficha.dead || !TemBuff(pl, BuffKaiokenG2))
			{
				ReverterKaiokenG2(pl, porKO: true);
				continue;
			}

			// KAIO-KEN NO BLUE, conferido AO VIVO (Loop:182-187): quem virou Blue DEPOIS de acender
			// o Kaio-ken cai nas mesmas regras de classe e maestria.
			if (pl.Ficha.godki is { usage: true } gk && (pl.Ficha.ssj > 0 || pl.Ficha.lssj > 0)
				&& (!(pl.Ficha.Class is "Normal" or "Low-Class") || gk.mastery < GodKiState.RoyalePct))
			{
				Avisar(pl, "o ki divino transformado expulsa o Kaio-ken do seu corpo!");
				ReverterKaiokenG2(pl, porKO: false);
				continue;
			}

			// `if(container.Ki > (100/container.KiMod))` -- o piso de folego pra sustentar.
			double piso = 100 / Math.Max(pl.Ficha.KiMod, 0.01);
			if (pl.Ficha.Ki <= piso)
			{
				// aqui o DM manda a mensagem de cansaco E explode quem passou de mastery*2
				Avisar(pl, "você está cansado demais pra continuar no Kaio-ken.");
				if (amt > maestria * 2) EstourarG2(pl, "seu corpo se despedaça de dentro pra fora.");
				_kaiokenAmtG2.Remove(id);
				DesligarBuff(pl, BuffKaiokenG2);
				continue;
			}

			// `ssj` do DM = qual degrau da escada Saiyajin. O port guarda isso no `Forma`, cujo id
			// e o MESMO numero x10 (ver o comentario do enum Forma). O `Fighter.ssj` existe mas
			// ninguem escreve nele por aqui -- ler dele deixaria o termo travado em 1 pra sempre.
			double ssj = (int)pl.Forma.Atual / 10.0;

			double razao = amt / Math.Max(maestria, 0.01);

			pl.Ficha.Ki -= 0.05 * razao * pl.Ficha.BaseDrain() / LoopDmG2;

			// `SpreadDamage(max(kaioamount/KaiokenMastery*3, 1) * 0.1 * (ssj+1) * max(log(kaioamount),1))`
			// -- `log()` de um argumento no DM e logaritmo NATURAL.
			double dano = Math.Max(razao * 3, 1) * 0.1 * (ssj + 1)
						* Math.Max(Math.Log(Math.Max(amt, 1)), 1) / LoopDmG2;
			EspalharDanoG2(pl, dano);

			// `staminadrainMod` do DM e reescrito pra 1 todo statify (master.dm:45) -- e knob morto.
			// O equivalente VIVO daqui e o `staminadrainStyle`, que o estilo de luta escreve.
			pl.Ficha.stamina = Math.Max(0, pl.Ficha.stamina
				- razao * 0.03 * (ssj + 1) * pl.Ficha.staminadrainStyle / LoopDmG2);

			// MAESTRIA: 0,001 por volta ate x20, e cem vezes mais devagar depois (teto 50). O
			// comentario do DM e literal: "kaioken is long as fuck to master".
			if (maestria < 20) maestria += 0.001 * amt / LoopDmG2;
			else maestria = Math.Min(maestria + 0.00001 * amt / LoopDmG2, 50);
			_kaiokenMaestriaG2[id] = maestria;
		}
	}

	/// <summary>O `Loop()` do Giant Form (Giant Form.dm:78-83), por segundo.</summary>
	private void TickGiganteG2()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (!TemBuff(pl, BuffGiganteG2)) continue;

			// O DM enche um `drainbuffer` e so cobra quando ele passa de 20, somando ~3 por volta
			// do Loop -- o que da uma cobrança a cada ~6,7 voltas, NAO a cada 10 segundos. Dividir
			// por 10 fazia o folego cair quase sete vezes mais devagar que no original, e forma
			// cara que sai barata e forma que ninguem desliga.
			//
			// Aqui a cobrança e continua e por segundo: mesma quantia no mesmo tempo, sem o
			// serrilhado do buffer. O primeiro pulso do DM demora (o buffer enche), e essa
			// carencia inicial nao existe mais -- e diferenca de um pulso, nao de escala.
			double porDreno = TransDrainG2
							* Math.Max(0.001, pl.Ficha.maxstamina * GiganteEficienciaG2 * 0.005) / 2;
			double porSegundo = porDreno / VoltasPorCobrancaG2;

			double minimo = pl.Ficha.maxstamina * 0.005 * TransDrainG2 * GiganteEficienciaG2;
			if (pl.Ficha.stamina < minimo && !pl.Ficha.dead)
			{
				// O DM nao desliga o gigante por falta de folego: o `if` simplesmente para de
				// cobrar, e a forma fica de graca. Aqui ela CAI -- forma que se sustenta sozinha
				// quando o corpo desiste e forma permanente com passos extras.
				DesligarBuff(pl, BuffGiganteG2);
				Avisar(pl, "o fôlego acaba e você encolhe de volta.");
				continue;
			}

			pl.Ficha.stamina = Math.Max(0, pl.Ficha.stamina - porSegundo);
		}
	}

	/// <summary>O `Loop()` do buff elemental (demigod.dm:199-208), por segundo.</summary>
	private void TickElementalG2()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (!TemBuff(pl, BuffElementalG2)) continue;

			double magi = Math.Max(pl.Ficha.Emagiskill, 0.01);

			if (pl.Ficha.stamina < pl.Ficha.maxstamina * (0.001 / magi))
			{
				DesligarBuff(pl, BuffElementalG2);
				Avisar(pl, "sem fôlego, o elemento se dispersa.");
				continue;
			}

			// `if(prob(20)) container.Ki -= (0.1/Emagiskill)*BaseDrain` -- por volta de loop. Aqui
			// vale o VALOR ESPERADO (20% do custo) por volta, dividido pra virar taxa por segundo:
			// sortear a 1 Hz daria a mesma media com ruido que o jogador leria como bug.
			pl.Ficha.Ki -= 0.2 * (0.1 / magi) * pl.Ficha.BaseDrain() / LoopDmG2;

			pl.Ficha.stamina = Math.Max(0, pl.Ficha.stamina
				- TransDrainG2 * Math.Max(0.001, 0.01 / magi) / LoopDmG2);

			if (pl.Ficha.Ki <= pl.Ficha.MaxKi * (0.001 / magi))
			{
				DesligarBuff(pl, BuffElementalG2);
				Avisar(pl, "você está cansado demais pra sustentar a forma.");
			}
		}
	}

	// =====================================================================
	// FERRAMENTAS DO LOTE
	// =====================================================================
	/// <summary>
	/// `SpreadDamage()` (Injuries.dm:63): o mesmo dano em CADA membro sorteavel. Nao-letal de
	/// proposito -- o dano continuo do Kaio-ken nocauteia, e quem MATA e a explosao separada
	/// (`Body_Parts()`), que so dispara nas duas condicoes escritas no DM.
	/// </summary>
	private static void EspalharDanoG2(ServerPlayer pl, double dano)
	{
		if (dano <= 0 || pl.Combate == null) return;
		foreach (BodyPart p in pl.Combate.Corpo.Partes.ToList())
			if (!p.Decepado && !p.Aninhado) pl.Combate.Corpo.Ferir(p, dano, letal: false);
		pl.Combate.SincronizarVida();

		if (!pl.Ficha.KO && !pl.Ficha.dead && pl.Combate.Corpo.DeveNocautear())
			pl.Combate.Nocautear(MeleeResolver.SegundosDeNocaute);
	}

	/// <summary>
	/// `Body_Parts()` -- o corpo se desfaz. E o preco de usar um Kaio-ken acima do que o corpo
	/// domina e ficar sem Ki no meio.
	///
	/// Nao ha um "gib" no port, entao o equivalente honesto e zerar todo membro com dano LETAL e
	/// deixar as regras normais de corpo decidirem (nucleo zerado = morte; raca que regenera
	/// membro entra em coma em vez de morrer, que e o `canheallopped`).
	/// </summary>
	private void EstourarG2(ServerPlayer pl, string motivo)
	{
		if (pl.Combate == null) return;
		Avisar(pl, motivo);

		foreach (BodyPart p in pl.Combate.Corpo.Partes.ToList())
			if (!p.Decepado) pl.Combate.Corpo.Ferir(p, p.VidaMax * 10, letal: true);
		pl.Combate.SincronizarVida();

		if (pl.Combate.Corpo.DeveMorrer() && !pl.Combate.Corpo.RegeneraDecepado)
		{
			pl.Combate.Morrer();
			pl.RenasceEm = NowMs() + MsAteRenascer;
			GD.Print($"[server] {pl.Name} EXPLODIU com o Kaio-ken");
		}
		else if (!pl.Ficha.KO && pl.Combate.Corpo.DeveNocautear())
		{
			pl.Combate.Nocautear(MeleeResolver.SegundosDeNocaute);
			GD.Print($"[server] {pl.Name} se despedacou com o Kaio-ken (regenera: entrou em coma)");
		}
	}
}
