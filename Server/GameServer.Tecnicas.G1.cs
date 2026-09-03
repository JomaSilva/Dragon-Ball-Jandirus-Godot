using Godot;
using Jandirus.Core.Skills;

namespace Jandirus.Server;

/// <summary>
/// LOTE G1 -- OS SETE TOGGLES DE BUFF.
///
/// Sao as tecnicas que nao "acontecem": elas FICAM. O jogador liga, alguma coisa sobe num `T*`, e
/// o Ki passa a ir embora mais rapido ate ele desligar. Todo o desfazer mora no alicerce
/// (<see cref="LigarBuff"/> / <see cref="DesligarBuff"/>), que guarda o que aplicou -- aqui so
/// entram o PRECO, a RECUSA e os numeros do DM.
///
/// A DESCOBERTA QUE MUDOU O LOTE: no original todo buff ocupa um SLOT, e `startbuff` recusa
/// calado quando o slot ja esta tomado (`buffs.dm:112-118`). Os SEIS primeiros aqui declaram
/// `slot=sBUFF` -- ou seja, no jogo antigo eles NUNCA se somam: quem esta com Fighting Power
/// ligado simplesmente nao consegue ligar Extreme Burst. Sem isso portado, os quatro +5 e as duas
/// laminas empilhariam num unico personagem (+20 de stat e duas somas logaritmicas), que e
/// exatamente o "liga tudo e nunca desliga" que o dreno multiplicativo tenta impedir. O Super
/// Majin declara `slot=sFORM`, entao ele E cumulativo com um dos seis -- e isso tambem e do DM.
///
/// O QUE O ALICERCE JA FAZ E NAO SE REPETE AQUI: derrubar tudo no nocaute e na morte, cobrar o
/// dreno via `DrainMod`, e derrubar o que tem dreno quando o Ki zera.
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// REGISTRO
	// =====================================================================
	/// <summary>Anuncia as sete no catalogo. Chamado uma vez, no boot do servidor.</summary>
	private void RegistrarTecnicasG1()
	{
		IniciarLote("G1");
		Vivo("Brutal_Clarity", pl => ToggleDeBuffG1(pl, "Brutal_Clarity", "Brutal Clarity", "Ttechnique",
			"você começa a fluir.", "seu fluxo se desfaz."));
		Vivo("Extreme_Burst", pl => ToggleDeBuffG1(pl, "Extreme_Burst", "Extreme Burst", "Tspeed",
			"você estoura a própria velocidade!", "você deixa a velocidade afrouxar."));
		Vivo("Fighting_Power", pl => ToggleDeBuffG1(pl, "Fighting_Power", "Fighting Power", "Tphysoff",
			"seu poder de luta dispara!", "você deixa o poder afrouxar."));
		Vivo("Ultradense_Body", pl => ToggleDeBuffG1(pl, "Ultradense_Body", "Ultradense Body", "Tphysdef",
			"seu corpo vira algo como aço!", "seu corpo perde a densidade."));
		Vivo("Ki_Blade", pl => ArmaDeKiG1(pl, "Ki_Blade", "Ki Blade", baseDoLog: 8.3, divisorDaMordida: 10));
		Vivo("Ki_Sword", pl => ArmaDeKiG1(pl, "Ki_Sword", "Ki Sword", baseDoLog: 8, divisorDaMordida: 9));
		Vivo("Super_Majin", SuperMajinG1);
	}

	// =====================================================================
	// OS NUMEROS DO DM
	// =====================================================================
	/// <summary>`container.initdrain = 5` -- multiplica o `DrainMod`. (Striker.dm:59, Tank.dm:58,
	/// Speed.dm:61, Martial Skill.dm:248 -- os quatro sao o mesmo numero.)</summary>
	private const double DrenoDosQuatroG1 = 5;

	/// <summary>`container.initbuff = 5` -- o que entra no `T*`. Mesmas quatro linhas.</summary>
	private const double BonusDosQuatroG1 = 5;

	/// <summary>
	/// `KiBladeDrain` / `KiSwordDrain`, que comecam em 1 (Cultivation.dm:88 e :207).
	///
	/// NO DM ELE CAI COM O USO: o `effector()` da skill baixa pra 0,5 no nivel 1 e pra 0 no nivel 2
	/// (Cultivation.dm:69-79). O port nao tem exp/nivel POR SKILL -- o <c>SkillBook</c> so sabe se
	/// a skill foi aprendida -- entao a mordida fica sempre no preco cheio. Quando o nivel de skill
	/// existir, e este numero que vira variavel.
	/// </summary>
	private const double MordidaDaArmaG1 = 1;

	/// <summary>
	/// OS SEIS QUE BRIGAM PELO MESMO SLOT (`slot=sBUFF`). Ver o cabecalho: no DM `startbuff`
	/// recusa quando o slot ja esta ocupado, entao eles nunca se somaram.
	/// </summary>
	private static readonly string[] SlotBuffG1 =
	[
		"Brutal_Clarity", "Extreme_Burst", "Fighting_Power", "Ultradense_Body", "Ki_Blade", "Ki_Sword",
		// O EXPAND BODY (lote G11) declara o MESMO `slot=sBUFF` (`Body Expansion.dm:15`): entra na
		// briga pelo slot nas duas direcoes -- nem ele liga por cima dos seis, nem os seis por cima dele.
		"Expand_Body",
	];

	/// <summary>
	/// ANTI-MARTELO. Nao existe recarga nenhuma nestes verbs do DM, e nao inventei uma de
	/// balanceamento: meio segundo so impede que um cliente batendo no canal de habilidade 30
	/// vezes por segundo faca o servidor ligar/desligar buff (e recalcular a ficha inteira) 30
	/// vezes por segundo. Quem joga com a mao nunca vai encostar nisso.
	/// </summary>
	private readonly Dictionary<int, long> _prontoG1 = [];

	private const long EsperaG1 = 500;

	// =====================================================================
	// OS QUATRO GEMEOS
	// =====================================================================
	/// <summary>
	/// LIGA/DESLIGA um dos quatro buffs de +5. O campo e o unico parametro que muda entre eles.
	///
	/// A ORDEM DAS RECUSAS E A DO DM: `if(!isBuffed && !usr.KO)` liga, `else if(isBuffed)`
	/// desliga -- ou seja, desligar vale mesmo caido, ligar nao. A diferenca e que o DM nao dizia
	/// NADA quando o jogador caido apertava o botao (o `if` simplesmente nao entrava em ramo
	/// nenhum): um botao que nao responde ensina o jogador a achar que a tecnica quebrou.
	/// </summary>
	private void ToggleDeBuffG1(ServerPlayer pl, string id, string nome, string campo,
								string ligou, string desligou)
	{
		if (EsperandoG1(pl)) return;

		if (DesligarBuff(pl, id)) { Avisar(pl, desligou); return; }

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, $"{nome} pede um corpo de pé."); return; }

		string? ocupado = OcupanteDoSlotG1(pl, id);
		if (ocupado != null)
		{
			Avisar(pl, $"você já está sustentando {ocupado} -- só dá para manter um desses por vez.");
			return;
		}

		// SEM ENERGIA NAO ADIANTA LIGAR. Com dreno x5 e Ki zerado, o `TickDosBuffs` derruba no
		// primeiro segundo -- ligar seria so um piscar. O DM nao checava porque o Ki dele nunca
		// ficava exatamente em zero; aqui a recusa e mais honesta que um buff de um segundo.
		if (pl.Ficha.Ki <= 0)
		{
			Avisar(pl, $"você não tem energia nenhuma para alimentar {nome} (o dreno fica 5x maior).");
			return;
		}

		LigarBuff(pl, id, nome,
			new Dictionary<string, double> { [campo] = BonusDosQuatroG1 },
			dreno: DrenoDosQuatroG1);
		Avisar(pl, ligou);
	}

	// =====================================================================
	// KI BLADE / KI SWORD
	// =====================================================================
	/// <summary>
	/// A LAMINA DE KI. Uma mordida de energia pra nascer e um bonus que sobe com a sua ofensiva
	/// de Ki -- depois disso ela nao custa mais nada (o `Buff()` das duas nao mexe no `DrainMod`,
	/// Cultivation.dm:113-122 e :235-244; o texto da skill Ki Knight promete dreno continuo, o
	/// codigo nunca cobrou, e eu portei o CODIGO).
	///
	/// O BONUS: `max(log(8.3, Ekioff*10), 0)` na lamina (Cultivation.dm:116) e `log(8, ...)` na
	/// espada (Cultivation.dm:238) -- base menor da log maior, e por isso a espada e mais
	/// generosa. Ele entra em `Tphysoff` E `Tkioff`, o mesmo valor nos dois.
	///
	/// A MORDIDA: `(MaxKi/10) * KiBladeDrain / KiMod` (Cultivation.dm:101) e `(MaxKi/9) * ... `
	/// na espada (Cultivation.dm:220).
	///
	/// DOIS DEFEITOS DO DM CONSERTADOS AQUI:
	///  1) o original faz `startbuff(...)` e SO DEPOIS testa `if(Ki >= custo) Ki -= custo` -- quem
	///     nao tinha energia ganhava a lamina de graca. Aqui a energia e condicao, nao consequencia.
	///  2) o `DeBuff()` das duas termina com `container.Ki = 0`: guardar a lamina zerava toda a
	///     sua energia, e como o Loop tambem chama DeBuff no nocaute, cada KO com lamina na mao
	///     custava o Ki inteiro. Nao portei isso -- ver "O QUE FICOU DE FORA" na entrega.
	/// </summary>
	private void ArmaDeKiG1(ServerPlayer pl, string id, string nome, double baseDoLog, double divisorDaMordida)
	{
		if (EsperandoG1(pl)) return;

		if (DesligarBuff(pl, id)) { Avisar(pl, $"seu {nome} se dissipa."); return; }

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "não dá para moldar Ki assim."); return; }

		// A MAO E UMA SO. No DM isso vem de graca pelo slot (`sBUFF`) mais o `KiWeaponOn`; aqui e
		// a mesma regra escrita a mao, porque o alicerce de buff do port nao tem slot.
		string? ocupado = OcupanteDoSlotG1(pl, id);
		if (ocupado != null)
		{
			Avisar(pl, $"suas mãos já estão ocupadas com {ocupado}.");
			return;
		}

		// `log(base, Ekioff*10)`. O piso de 1 dentro do log e a mesma protecao do Ki Shield: em
		// DM, log de zero ou de negativo e runtime; aqui seria NaN entrando direto num stat.
		double bonus = Math.Max(Math.Log(Math.Max(pl.Ficha.Ekioff * 10, 1)) / Math.Log(baseDoLog), 0);

		// KiMod nasce protegido contra zero no `Fighter.FromGenome`, mas um save antigo pode
		// trazer qualquer coisa -- e uma divisao por zero aqui viraria custo infinito.
		double kiMod = Math.Max(pl.Ficha.KiMod, 0.01);
		double custo = pl.Ficha.MaxKi / divisorDaMordida * MordidaDaArmaG1 / kiMod;

		if (pl.Ficha.Ki < custo)
		{
			Avisar(pl, $"moldar {nome} pede {custo:0} de energia e você tem {pl.Ficha.Ki:0}.");
			return;
		}

		pl.Ficha.Ki -= custo;
		LigarBuff(pl, id, nome,
			new Dictionary<string, double> { ["Tphysoff"] = bonus, ["Tkioff"] = bonus });

		Avisar(pl, $"o Ki toma forma: {nome} (+{bonus:0.##} de ofensiva física e de Ki).");
		GD.Print($"[G1] {pl.Name} formou {nome} (+{bonus:0.##}, custou {custo:0} de Ki)");
	}

	// =====================================================================
	// SUPER MAJIN
	// =====================================================================
	/// <summary>
	/// SUPER MAJIN (majin.dm:112-147). Toggle de FORMA, nao de buff de corpo: no DM ele ocupa o
	/// `slot=sFORM`, entao ele convive com um dos seis de cima -- e por isso nao entra na
	/// checagem de slot.
	///
	/// OS DOIS RAMOS, os dois portados:
	///   COM Ascensao: `transBuff=3`, `trueKiMod=2` (e o MaxKi dobra por consequencia) e NENHUMA
	///     penalidade de defesa.
	///   SEM Ascensao: `Tphysdef -= 0.1` e `Tkidef -= 0.1`, e nada de poder extra.
	/// Nos dois: `Ttechnique += 1.2`, `Tkioff += 1.2`, `Tspeed += 1.2` (majin.dm:133-135).
	///
	/// O QUE E "ESTAR ASCENDIDO" AQUI: no DM, `AscensionStarted` e uma flag GLOBAL do mundo que o
	/// admin liga (ascensioncontrols.dm:33) e que faz o `Auto_Gain` levantar o `BPBoost` de cada
	/// personagem. O port nao tem esse evento de mundo; o que ele tem e o resultado dele, o
	/// `BPBoost` por personagem. Entao `BPBoost > 1` E o estado de ascensao aqui -- e a leitura
	/// que bate com a descricao da propria skill ("If you're ascended...").
	///
	/// POR QUE SOMA E NAO ATRIBUICAO em `transBuff` e `trueKiMod`: os dois valem 1 em repouso,
	/// entao +2 e +1 dao exatamente os 3 e 2 do DM -- so que compondo com quem mais estiver
	/// mexendo neles, e desfazendo exatamente o que somaram. O DM atribui (`transBuff=3`,
	/// e no DeBuff `transBuff=1`, `trueKiMod=1`) -- isso APAGA o fator de qualquer outra forma
	/// ativa, e o DeBuff zera ate o que ele nunca ligou (o ramo sem Ascensao nunca tocou o
	/// trueKiMod e mesmo assim o forca pra 1 ao sair).
	///
	/// O `container.MaxKi *= 2` do DM (majin.dm:128) NAO foi portado por ser redundante: o
	/// `UpdateMaxKi` recalcula `MaxKi = baseKi * KiMod * kiAmp * trueKiMod * ...`
	/// (Fighter.Statify.cs:118) a cada tick, entao o trueKiMod ja dobra o MaxKi sozinho. No DM
	/// aquilo era uma dobra a mais, desfeita na mao pela divisao do DeBuff.
	/// </summary>
	private void SuperMajinG1(ServerPlayer pl)
	{
		const string id = "Super_Majin";
		if (EsperandoG1(pl)) return;

		if (DesligarBuff(pl, id)) { Avisar(pl, "você relaxa e volta ao normal."); return; }

		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não está em condições de assumir a forma."); return; }

		bool ascendido = pl.Ficha.BPBoost > 1;

		var somas = new Dictionary<string, double>
		{
			["Ttechnique"] = 1.2,
			["Tkioff"] = 1.2,
			["Tspeed"] = 1.2,
		};

		if (ascendido)
		{
			somas["transBuff"] = 2;    // 1 -> 3
			somas["trueKiMod"] = 1;    // 1 -> 2 (e o MaxKi dobra no proximo Statify)
		}
		else
		{
			somas["Tphysdef"] = -0.1;
			somas["Tkidef"] = -0.1;
		}

		LigarBuff(pl, id, "Super Majin", somas);

		Avisar(pl, ascendido
			? "você se torna um Super Majin! A Ascensão paga a conta: nada de perda de defesa, e sua energia dobra."
			: "você se torna um Super Majin! Sem a Ascensão, a força vem às custas da sua defesa.");
		GD.Print($"[G1] {pl.Name} virou Super Majin (ascendido: {ascendido})");
	}

	// =====================================================================
	// APOIO
	// =====================================================================
	/// <summary>
	/// Qual dos SEIS do slot `sBUFF` esta ligado agora (fora `menos`)? Nulo quando o slot esta
	/// livre. Devolve o NOME legivel porque quem le e o jogador, na mensagem de recusa.
	/// </summary>
	private string? OcupanteDoSlotG1(ServerPlayer pl, string menos)
	{
		foreach (string outro in SlotBuffG1)
		{
			if (outro == menos || !TemBuff(pl, outro)) continue;
			return Tecnicas.Get(outro)?.Nome ?? outro;
		}
		return null;
	}

	/// <summary>
	/// Passou o meio segundo desde o ultimo toggle deste jogador? Avisa com o tempo que falta e
	/// marca a proxima janela quando deixa passar.
	/// </summary>
	private bool EsperandoG1(ServerPlayer pl)
	{
		if (EmEspera(pl, _prontoG1, "espere um instante")) return true;
		_prontoG1[pl.Id] = NowMs() + EsperaG1;
		return false;
	}

	/// <summary>QUEM SAIU LEVA O ESTADO DELE JUNTO -- inscrito no `EsquecerTecnicas`, porque id se reusa.</summary>
	private void EsquecerG1(int id) => _prontoG1.Remove(id);
}
