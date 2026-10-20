using Jandirus.Core.Ai;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Skills;

/// <summary>
/// AS TECNICAS QUE NAO VIAJAM, NA MAO DA IA -- os golpes de perto, as investidas, os reforcos e os socorros.
///
/// ============================ O PEDIDO (dono, 2026-10-13) ============================
/// *"IAs q controlam o personagem (oozaru, formas de lssj descontroladas etc) deveriam usar todas as skills
/// disponiveis do jogador descontrolado, se a IA n sabe usar, melhore ela pra deixa-la mais inteligente"*.
///
/// Ate a v0.2.0 a IA conhecia quatro tiros e o sopro. Um corpo com o livro cheio -- e o corpo possuido de um jogador
/// quase sempre tem -- lutava a soco. A outra metade do pedido esta na `TecnicasDeLonge` (todo verb que atira); esta
/// tabela e o resto: tudo o que tem corpo no jogo e serve numa luta sem ser um tiro.
/// ======================================================================================
///
/// ============================ O QUE CADA COLUNA E, E O QUE ELA NAO E ============================
/// Pela regra da tabela irma: a linha descreve a TECNICA, e nunca decide. O que ela diz:
///   * o USO (<see cref="Uso"/>) -- de que receita do cerebro ela participa;
///   * a JANELA em tiles -- de que distancia ela serve (o golpe colado, a corrida de meia distancia);
///   * o PRECO nas duas moedas, espelhando a conta do verb (a `--catalogoteste` aperta cada uma e confere que a
///     tabela nunca promete mais barato do que o jogo cobra);
///   * o que ela EXIGE da cena (<see cref="Exige"/>) e de quanto em quanto tempo o cerebro a repete.
/// Quem recusa de verdade continua sendo o verb: o preco e piso, a janela e julgamento, e a tecnica que pedir uma
/// condicao que a tabela nao conhece (a mao livre do Hokuto, o elemento escolhido do Elemental) responde "nao" e
/// descansa -- sem frase pra ninguem, porque o corpo dirigido nao tem a quem dizer (`GameServer.Avisar`).
/// ================================================================================================
///
/// ============================ E O QUE FICOU DE FORA TEM NOME E MOTIVO ============================
/// <see cref="ForaDaLuta"/> lista todo verb com corpo que a IA NAO usa, cada um com a razao. A bancada cobra as
/// duas pontas: verb vivo sem linha em tabela nenhuma reprova, e linha de verb que nao existe mais tambem.
/// ===============================================================================================
/// </summary>
public static class TecnicasDeLuta
{
	/// <summary>
	/// UMA LINHA -- a descricao estatica de uma tecnica de luta. O que sobra dela depois de resolvida pra um corpo e
	/// um <see cref="Tiro"/> do repertorio (`Capacidades.DeLuta`).
	/// </summary>
	public readonly record struct Linha(
		/// <summary>O id que se APERTA -- o que viaja no canal de habilidade (`Expand_Body:1`, `Kickup`).</summary>
		string Id,
		/// <summary>O verb que precisa ser SABIDO pra apertar (o `SabeTecnica`). Quase sempre o proprio id.</summary>
		string Gate,
		Uso Uso,
		/// <summary>De que distancia ela serve, em TILES.</summary>
		float AlcanceMinTiles,
		float AlcanceMaxTiles,
		/// <summary>O preco em Ki, espelhando a conta do verb.</summary>
		Func<Fighter, double> CustoDeKi,
		/// <summary>O preco em FOLEGO. Nulo = nao cobra.</summary>
		Func<Fighter, double>? CustoDeFolego = null,
		Exige Exige = Exige.Nada,
		/// <summary>Segundos sem repetir. Zero = o padrao do cerebro pro uso.</summary>
		double Descanso = 0,
		/// <summary>(Reforco) Os do mesmo numero nao se sustentam juntos. Zero = sem grupo.</summary>
		byte Grupo = 0,
		/// <summary>Segundos ate o segundo aperto, pras que carregam e soltam (o Rugido). Ver `TecnicasDeLonge.Linha`.</summary>
		double SegundoAperto = 0,
		/// <summary>(Reforco) O id que DESLIGA, quando nao e o que liga. Nulo = o mesmo.</summary>
		string? Desliga = null);

	/// <summary>`kireq = Ephysoff * BaseDrain * N` -- a abertura de todo punho nomeado (`GameServer.AbrirPunhoG7`).</summary>
	private static Func<Fighter, double> Punho(double n) => f => f.Ephysoff * f.BaseDrain() * n;

	private static double DeGraca(Fighter f) => 0;

	// Os tres alcances de que os verbs de punho falam, em tiles (`GameServer.Tecnicas.G7`: `ColadoG7` e
	// `PassoDeAproximacaoG7`): COLADO e onde o golpe pega; com o PASSO o verb anda um tile antes de bater.
	private const float Colado = 1.5f, ComOPasso = 2.5f;

	private static readonly Linha[] Tudo =
	[
		// =====================================================================
		// OS GOLPES DE PERTO -- saem no lugar de um soco
		// =====================================================================
		// ---- o boxe e os chutes (lote G7, `Physical Skills.dm`) ----
		Golpe("One_Two", ComOPasso, Punho(8)),
		Golpe("One_Two_Five", ComOPasso, Punho(12)),
		Golpe("Two_One_Four", Colado, Punho(9)),            // o unico combo que nao da o passo
		Golpe("KO_Punch", ComOPasso, Punho(15)),
		Golpe("Kickup", ComOPasso, Punho(12)),
		Golpe("Falling_Kick", ComOPasso, Punho(12)),        // (contra quem voa ele arranca do ar; no chao e um chute)
		Golpe("Spin_Attack", Colado, Punho(15)),            // pega todo mundo em volta
		Golpe("Takedown", ComOPasso, Punho(20)),
		Golpe("Cutthroat", ComOPasso, Punho(15), descanso: 8),
		Golpe("Backstab", ComOPasso, Punho(15), descanso: 8),

		// ---- o assassino, o berserker e as artes marciais (lote G10) ----
		Golpe("Shock", ComOPasso, Punho(8)),
		Golpe("Reverb", ComOPasso, Punho(12)),
		Golpe("Precise_Explosion", ComOPasso, Punho(15), descanso: 8),
		Golpe("Trip", ComOPasso, Punho(15), exige: Exige.AlvoNoChao),
		Golpe("Revenge_Demon", ComOPasso, Punho(15)),
		Golpe("Seismic_Press", ComOPasso, Punho(18)),
		// O HOKUTO cobra as DUAS moedas (18 de folego e depois o punho) e tem recarga propria, longa.
		Golpe("Hokuto_Hyakuretsu_Ken", Colado, Punho(20), folego: _ => 18, descanso: 20),
		Golpe("Slap", Colado, Punho(1)),                     // (da casa Ricardo da Trindade: o verb confere e nao cobra)

		// ---- os mais velhos (lotes G2, G3 e G6) ----
		Golpe("Special_Multihit", Colado, Punho(6)),
		Golpe("Wolf_Fang_Fist", Colado, Punho(10), folego: _ => 8),
		Golpe("Wolf_Fang_Hurricane", Colado, Punho(9)),
		Golpe("Spirit_Fist", Colado, DeGraca, folego: f => f.angerBuff * f.Ephysoff * f.SpiritFistCost),
		// AS DUAS VARREDURAS pedem a arma de Ki na mao (a Ki Sword, o Ki Shield): sao reforcos desta mesma tabela,
		// e o corpo que sabe os dois liga um e passa a ter o outro. Sem a arma o verb recusa.
		Golpe("Sword_Strike", Colado, f => f.Ephysoff * 4 + 20),
		Golpe("Shield_Bash", Colado, f => f.Ephysoff * 4 + 20),
		Golpe("Bite", Colado, DeGraca),
		Golpe("Solar_Flare", Colado, Tecnicas.SolarCustoKi, descanso: 15),
		Golpe("Shockwave", Colado, f => 40 * f.BaseDrain(), descanso: 8),
		// O RUGIDO carrega e solta: o primeiro aperto comeca a juntar (o corpo fica plantado), o segundo estoura.
		Golpe("Explosive_Roar", Colado, f => 120 * f.BaseDrain(), descanso: 15, segundoAperto: 1.5),
		// A DEFLEXAO so serve com tiro alheio no ar, vindo pra cima do corpo.
		Golpe("Deflection", 99f, f => 80 * f.BaseDrain(), exige: Exige.TiroVindo, descanso: 4),

		// ---- os AGARROES (lote G10): o verb pega quem esta na frente, ou usa quem ja esta nos bracos ----
		Golpe("Clench", Colado, Punho(9), exige: Exige.Agarra),
		Golpe("Hold", Colado, Punho(12), exige: Exige.Agarra),
		Golpe("Power_Slam", Colado, Punho(20), exige: Exige.Agarra),
		Golpe("Suplex", Colado, Punho(15), exige: Exige.Agarra),
		Golpe("Gigantic_Spike", Colado, Punho(12), exige: Exige.Agarra),
		Golpe("Power_Drag", Colado, Punho(12), exige: Exige.Agarra),

		// =====================================================================
		// AS INVESTIDAS E O QUE PEGA DE MEIA DISTANCIA
		// =====================================================================
		// As corridas do G7 erram CARO (*"voce cai sozinho -- ficou atordoado"*): a janela e curta de proposito, e
		// o cerebro so as pede com a linha livre. O alcance de verdade cresce com os atributos (a Voadora corre
		// `Espeed + Etechnique + Ephysoff/2` tiles, a Investida isso mais cinco); o teto daqui e o do corpo fraco.
		Investida("Dropkick", 1.5f, 3f, Punho(15)),
		Investida("Dash_Attack", 2.5f, 7f, Punho(8)),
		Investida("Stun_Attack", 2f, 9f, Punho(20), descanso: 10),
		Investida("Lariat", 3f, 20f, f => f.angerBuff * 1.5 / Math.Max(f.Ephysoff + f.Etechnique, 0.01) * f.BaseDrain()),
		Investida("Light_Buster", 1.5f, 4f, DeGraca, descanso: 8),
		Investida("Zanzoken_Combo", 1.5f, 3f, Punho(4)),
		Investida("Zanzoken_Rush", 3f, 15f, f => f.angerBuff * 5 / Math.Max(f.Ephysoff + f.Etechnique, 0.01) * f.BaseDrain(), descanso: 12),
		Investida("Zanzoken_Dash", 4f, 18f, f => 10 * f.BaseDrain() / Math.Max(f.speed, 0.01)),
		Investida("Rapid_Movement", 4f, 18f, f => 10 * f.BaseDrain() / Math.Max(f.speed, 0.01)),
		// O GRILHAO e a CONTRA-PROVOCACAO nao correm: pegam o alvo MARCADO de onde estao.
		Investida("Shackle", 3f, 14f, f => 900 * f.BaseDrain(), descanso: 15),
		Investida("Counter_Taunt", 2f, 9f, Punho(1), descanso: 10),
		// O CONGELAMENTO pega quem esta a vista e leva METADE do Ki que houver -- o preco daqui e a metade do tanque.
		Investida("Freeze", 0f, 8f, f => f.MaxKi * 0.5, descanso: 30),

		// =====================================================================
		// OS REFORCOS -- ligam quando a luta comeca, desligam quando ela acaba
		// =====================================================================
		// OS QUATRO DO G1 dividem um slot (*"so da para manter um desses por vez"*) e quintuplicam o dreno.
		Reforco("Brutal_Clarity", DeGraca, grupo: 1),
		Reforco("Extreme_Burst", DeGraca, grupo: 1),
		Reforco("Fighting_Power", DeGraca, grupo: 1),
		Reforco("Ultradense_Body", DeGraca, grupo: 1),
		// AS DUAS ARMAS DE KI dividem a mao, e cobram uma mordida do tanque pra nascer.
		Reforco("Ki_Blade", f => f.MaxKi / 10 / Math.Max(f.KiMod, 0.01), grupo: 2),
		Reforco("Ki_Sword", f => f.MaxKi / 9 / Math.Max(f.KiMod, 0.01), grupo: 2),
		Reforco("Giant_Form", DeGraca),
		Reforco("Elemental", DeGraca),
		Reforco("Majin", DeGraca),
		Reforco("Focus", DeGraca),
		Reforco("Efficiency", DeGraca),
		Reforco("Energy_Shield", f => 0.01 * f.MaxKi * f.BaseDrain()),
		Reforco("Full_Power", f => f.MaxKi / 20),              // (a porta do verb: um vigesimo do tanque)
		Reforco("Ki_Shield", Tecnicas.EscudoCustoKi),
		// A EXPANSAO tem graus: a IA usa o primeiro, e quem desliga e o grau zero.
		new("Expand_Body:1", "Expand_Body", Uso.Reforco, 0, 0, f => 25 / Math.Max(f.Ekiskill, 0.01) + 5, Desliga: "Expand_Body:0"),
		// AS MIRAGENS duram cinco segundos: o "desligar" delas e o prazo, e o descanso e o que as repete.
		new("Zanzoken_Afterimage", "Zanzoken_Afterimage", Uso.Reforco, 0, 0, Punho(0.4), _ => 15, Exige.NaoSeDesliga, Descanso: 12),
		// A DIVISAO DO CORPO poe uma copia que luta ao lado: de pe, nao se aperta de novo.
		new("SplitForm", "SplitForm", Uso.Reforco, 0, 0, f => f.MaxKi * 0.5, null, Exige.NaoSeDesliga, Descanso: 60),

		// =====================================================================
		// OS SOCORROS
		// =====================================================================
		// O REGENERAR e do corpo do Namekuseijin ou da skill -- quem responde e o `PodeRegenerar`, pela porta `regenerar`.
		new("regenerar", "Regenerate", Uso.Socorro, 0, 0, f => f.MaxKi * 0.7, Descanso: 12),
		// O ESTIRAO devolve Ki (dois tanques, a cada dois minutos) e cobra um mes de idade: e o socorro de quem secou.
		new("Growth_Spurt", "Growth_Spurt", Uso.Socorro, 0, 0, DeGraca, null, Exige.KiNoFim, Descanso: 120),
	];

	private static Linha Golpe(string id, float alcance, Func<Fighter, double> ki, Func<Fighter, double>? folego = null,
							   Exige exige = Exige.Nada, double descanso = 0, double segundoAperto = 0) =>
		new(id, id, Uso.Golpe, 0, alcance, ki, folego, exige, descanso, 0, segundoAperto);

	private static Linha Investida(string id, float de, float ate, Func<Fighter, double> ki, double descanso = 0) =>
		new(id, id, Uso.Investida, de, ate, ki, null, Exige.Nada, descanso);

	private static Linha Reforco(string id, Func<Fighter, double> ki, byte grupo = 0) =>
		new(id, id, Uso.Reforco, 0, 0, ki, null, Exige.Nada, 0, grupo);

	/// <summary>As linhas, na ordem em que estao escritas. Um array: o `foreach` da leitura de 1 Hz nao aloca.</summary>
	public static ReadOnlySpan<Linha> Todas => Tudo;

	public static int Quantas => Tudo.Length;

	/// <summary>A linha de um id (o que se aperta) ou de um gate. Nulo = esta tecnica nao e de luta.</summary>
	public static Linha? Get(string id)
	{
		foreach (Linha l in Tudo)
			if (string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(l.Gate, id, StringComparison.OrdinalIgnoreCase)) return l;
		return null;
	}

	/// <summary>
	/// ============================ O QUE A IA NAO USA, E POR QUE ============================
	/// Todo verb com corpo que nao esta em tabela nenhuma da IA mora aqui, com o motivo. Nao e uma lista de divida:
	/// sao as tecnicas que nao sao gesto de luta (falar, viajar, ensinar, criar), as que pedem uma escolha que so
	/// quem joga faz, e as que custam ao DONO do corpo algo que nao volta.
	///
	/// A ultima familia e a que importa: o corpo possuido e de um jogador. A fera pode gastar o Ki dele, o folego
	/// dele e apanhar por ele -- e o preco de ter perdido o controle. Ela nao pode explodir o corpo dele, selar
	/// outro jogador num pote, apagar um planeta nem devorar a alma de um caido: essas decisoes nao se desfazem
	/// quando a lua sai do ceu.
	/// ======================================================================================
	/// </summary>
	public static readonly IReadOnlyDictionary<string, string> ForaDaLuta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		// ---- o que nao se desfaz ----
		["Final_Explosion"] = "leva o corpo de quem detona (e, no maximo, o planeta)",
		["Self_Destruct"] = "mata quem esta agarrado e fere o proprio corpo em cada membro",
		["Planet_Destroy"] = "apaga um planeta",
		["Mafuba"] = "sela alguem num pote",
		["Open_Dead_Zone"] = "sela alguem na Dead Zone",
		["Life_Suck"] = "e execucao de quem ja caiu: a IA nao executa (so o roteiro do Deus da Destruicao executa)",
		["Soul_Absorb"] = "e execucao de quem ja caiu",
		["Absorb_Android"] = "so age em quem ja caiu",
		["Kaioken"] = "a dose acima da maestria quebra o corpo quando o Ki acaba: e aposta de quem joga",
		// (2026-10-18) o Sleep do Majin: 5 s dormindo no meio da briga e entregar o corpo -- e decisao de quem joga (e NPC nenhum o tem)
		["Sleep"] = "dormir 5 s parado no meio da briga e entregar o corpo: e decisao de quem joga (e NPC nenhum o tem)",
		["Chrono_Trigger"] = "crava e volta um ponto no tempo: e decisao de quem joga",
		["Unlock_Potential"] = "uma vez por vida, e depende de o outro aceitar",
		["Restore_Youth"] = "mexe na idade de outra pessoa, que precisa aceitar",

		// ---- o que seria ajudar o inimigo ----
		["Time_Touch"] = "ACELERA quem e tocado: num inimigo seria um presente",
		["Heal"] = "cura OUTRA pessoa marcada",
		["Give_Power"] = "doa o proprio Ki a outra pessoa",
		["Revive"] = "ressuscita um morto ao lado",
		["Keep_Body"] = "guarda o corpo de um morto",

		// ---- o que pede uma escolha (menu, argumento, palpite) ----
		["Rock_Paper_Scissors"] = "e um palpite sobre o que o outro vai fazer: a IA nao le intencao",
		["Power_Control"] = "BAIXA o poder (e a tecnica de quem quer se esconder)",
		["Imitation"] = "veste o rosto de outra pessoa",
		["Magic_Words"] = "sao frases de efeito, ditas por escolha",
		["DirectSSJ"] = "e atalho de forma: quem sobe de forma na luta e a escada do cerebro (`Plano.Escalar`)",
		["Psycho_Thread"] = "troca o que o clique no chao faz, e a IA nao clica no chao",
		["Afterimage_Toggle"] = "e preferencia de quem joga (guardar a miragem do Zanzoken)",

		// ---- o que e esconder-se, e nao lutar ----
		["Invisibility"] = "sumir da vista nao e lutar: a fera quer ser vista",
		["Sneak"] = "um segundo fora da vista, pra emboscada de quem planeja",
		["Flip"] = "so serve pra quem esta agarrado -- e quem se solta do agarrao, na IA, e o corpo se debatendo (`LutaPraEscapar`)",

		// ---- o que e viajar, falar, perceber ----
		["Telepathy"] = "e conversa",
		["Observe"] = "projeta a mente pra longe do corpo",
		["Assess_Ki_Skill"] = "e leitura: o efeito dela e uma frase",
		["Dead"] = "e leitura: lista os mortos",
		["Detect_Shard"] = "e leitura",
		["RiftTeleport"] = "leva pra outro planeta",
		["Devil_Bringer"] = "leva pra outro planeta",
		["Kai_Kai"] = "leva pra outro planeta",
		["Instant_Transmission"] = "leva ate uma assinatura conhecida",
		["Go_To_Heaven_Or_Hell"] = "leva pro Outro Mundo",
		["Holy_Shortcut"] = "leva pra Arconia",
		["Space_Flight"] = "deixa o planeta",

		// ---- o que e treinar, ensinar, criar ----
		["Ki_Targets"] = "e treino",
		["Study_Other"] = "e estudo",
		["Focus_Skill"] = "e estudo",
		["Write_Teachings"] = "e escrita",
		["Materialize"] = "cria um item no chao",
		["Grow_Senzu_Bean"] = "cultiva uma semente, em um minuto",
		["Taunt"] = "chama pra si os NPCs em volta -- o corpo dirigido ja tem a presa dele",

		// ---- o que ja tem receita propria no cerebro ----
		["Kiai"] = "tem receita propria: o sopro de quem esta encurralado (`Capacidades.SabeSopro`)",
		// (2026-10-16) a ativa da Forma Pura do Corrupted Majin -- quem a aperta e a loucura, e nao a tabela
		["Sycophantic_Rage"] = "tem receita propria: a loucura do Corrupted Majin a aperta quando ha um tiro carregando "
			+ "(`GameServer.SagaDoMajin`, no tique da insana)",
	};
}
