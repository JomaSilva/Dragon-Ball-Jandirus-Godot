namespace Jandirus.Core.Races;

/// <summary>
/// IDADE, AUGE E VELHICE -- o `Aging.dm` do original.
///
/// ============================ POR QUE ISTO NASCEU AGORA ============================
/// A idade existia no port como um numero de 1 a 200 que o jogador digitava e que NAO FAZIA NADA:
/// `Fighter.AgeDiv` era declarado com valor 1 e nunca era calculado, entao entrava na conta de BP
/// (`netBuff = deBuff * statusBuff * AgeDiv * ...`) como multiplicador neutro pra todo mundo. Um
/// Saiyajin de 8 anos e um de 800 tinham exatamente o mesmo poder, e a caixa de idade era enfeite.
///
/// O dono pediu "idade baseada no lifespan e peak age de cada raca", e as duas coisas existiam no
/// DM -- so nao tinham vindo.
/// ===================================================================================
///
/// ============================ O QUE O `Lifespan` DO PROTO NAO E ============================
/// `races.json` tem um campo `misc.Lifespan` (Human 1, Saiyan 2, Namekian 7, Icer 10, Kai 25), e a
/// tentacao e ler aquilo como anos ou como multiplicador de anos. NAO E: o proprio DM abandonou
/// esse caminho e deixou o aviso escrito (`Genetic_Datum.dm:227-229`) -- "a formula antiga
/// 60*Lifespan^2 dava idades sem controle; o stat Lifespan segue existindo so como legado".
///
/// Quem manda e a TABELA POR RACA abaixo (`race_peak_age`, Aging.dm:50-75). O campo do proto
/// continua no JSON porque outras contas o usam, mas ele nao entra em nada daqui.
/// ===========================================================================================
/// </summary>
public static class Envelhecimento
{
	/// <summary>
	/// A idade em que o corpo para de crescer e passa a so se manter. `InclineAge` do DM
	/// (`mobvars.dm`), igual pra todas as racas: antes disso o personagem ainda esta virando gente.
	/// </summary>
	public const double IdadeAdulta = 25;

	/// <summary>
	/// De quanto o tempo de vida passa do auge. `Aging.dm:45-47`: "LIFESPAN = peak * 1.5 -- o
	/// DeclineMod e calibrado (100/peak) pra o Body cair de 25 a 0 em exatamente peak*0.5 anos =>
	/// morte de velhice em peak*1.5".
	/// </summary>
	public const double VidaAlemDoAuge = 1.5;

	/// <summary>
	/// O AUGE DE CADA RACA, em anos. Tabela literal do `race_peak_age` (Aging.dm:50-75).
	///
	/// E a idade em que o declinio COMECA (o `DeclineAge`), nao a idade da morte -- e tambem o teto
	/// que a criacao de personagem oferece, que e o que o dono pediu.
	///
	/// ZERO QUER DIZER IMORTAL, e nao "auge aos zero anos": o Majin nao envelhece
	/// (`biologicallyimmortal = 1` no DM). Ver <see cref="NaoEnvelhece"/>.
	///
	/// OS NOMES SAO OS DO PORT, e nem sempre os do DM: a raca do Freeza se chama "Icer" aqui
	/// (o extrator de protos pegou a folha do typepath) e "Frost Demon" la. As duas grafias
	/// respondem, porque as duas circulam no codigo -- ver `Ranks.cs`.
	/// </summary>
	public static double AugeDaRaca(string raca) => raca switch
	{
		"Human" => 50,
		"Saiyan" or "Legendary Saiyan" => 80,
		"Half-Saiyan" or "Halfbreed" or "Half-Breed" => 70,
		"Quarter-Saiyan" => 60,
		"Icer" or "Frost Demon" => 2000,
		"Majin" => 0,                       // nao envelhece
		"Namekian" => 200,
		"Android" => 200,
		"Kai" => 75000,
		"Demon" => 75000,
		"Demigod" => 10000,
		"Makyo" => 5000,
		"Gray" => 300,
		"Yardrat" => 150,
		"Tsujin" => 130,
		"Kanassa" or "Kanassa-Jin" => 120,
		"Alien" => 100,
		"Shapeshifter" => 100,
		"Heran" => 90,
		"Arlian" => 40,
		"Saibaman" or "Saibamen" => 10,
		"SpiritDoll" or "Spirit Doll" => 200,
		"Meta" => 200,
		_ => 100,
	};

	/// <summary>Esta raca e biologicamente imortal? (auge zero = Majin, que nunca declina.)</summary>
	public static bool NaoEnvelhece(string raca) => AugeDaRaca(raca) <= 0;

	/// <summary>
	/// Quantos anos esta raca vive, no total. Auge vezes <see cref="VidaAlemDoAuge"/>.
	/// Zero para quem nao envelhece.
	/// </summary>
	public static double TempoDeVida(string raca)
	{
		double auge = AugeDaRaca(raca);
		return auge <= 0 ? 0 : auge * VidaAlemDoAuge;
	}

	/// <summary>
	/// O TETO DA CAIXA DE IDADE NA CRIACAO -- o `agemax` do formulario (CreationUI.dm:126).
	///
	/// E o AUGE, e nao o tempo de vida: o original nao deixa ninguem nascer ja em declinio, e faz
	/// sentido -- comecar velho seria escolher um personagem que so piora. Quem nao envelhece cai
	/// no mesmo 130 que o DM usa como teto de seguranca quando nao ha auge.
	/// </summary>
	public static int IdadeMaximaNaCriacao(string raca)
	{
		double auge = AugeDaRaca(raca);
		return auge > 1 ? (int)Math.Round(auge) : 130;
	}

	/// <summary>
	/// O DIVISOR DE PODER POR IDADE -- o `AgeDiv` que entra no `netBuff` (Stats.dm:230-255).
	///
	/// ============================ A CURVA, E POR QUE ELA E UMA SO ============================
	/// Ha tres faixas da vida e uma unica formula, e isso nao e economia de codigo -- e o que faz a
	/// curva ser CONTINUA nas duas pontas.
	///
	///   * CRIANCA (ate os 25): a razao e `idade / 25`, entao ela sobe de 0 a 1 conforme o corpo
	///     cresce.
	///   * ADULTO (25 ate o auge): a razao e exatamente 1.
	///   * VELHO (do auge em diante): a razao e `auge / idade`, entao ela DESCE de 1 conforme os
	///     anos passam do auge -- a mesma escada, descida.
	///
	/// A formula `(-0,5r + 2)(0,5r + 1) - 1,25` transforma essa razao no multiplicador. Em r = 1
	/// ela vale 1 exatamente ((1,5)(1,5) - 1,25), e e por isso que o adulto nao sente nada e as
	/// emendas nao tem degrau. Nas pontas ela vale 0,75: um recem-nascido e um ancia bem passado
	/// batem com 3/4 do poder que o BP deles diria.
	///
	/// O DM tem um `if(tmpdeclinediv != 1)` em volta da atribuicao. Ele nao muda resultado nenhum
	/// (a formula ja devolve 1 nesse ponto) -- e desvio pra pular conta, e aqui nao vale a pena.
	/// =========================================================================================
	/// </summary>
	public static double DivisorDeIdade(string raca, double idade)
	{
		if (NaoEnvelhece(raca)) return 1;

		double auge = AugeDaRaca(raca);
		double razao = idade <= IdadeAdulta ? idade / IdadeAdulta
					 : idade >= auge ? auge / idade
					 : 1;

		return (-0.5 * razao + 2) * (0.5 * razao + 1) - 1.25;
	}

	/// <summary>
	/// A IDADE DO CORPO (`Body` do DM), que e outra coisa da idade em anos.
	///
	/// Ela cresce junto com a idade ate os 25 e ali TRAVA -- um adulto de 30 e um de 300 tem o
	/// mesmo corpo. Depois do auge ela DESCE, e e essa descida que mata de velhice: quando chega a
	/// zero o personagem morre e so a reencarnacao devolve (`aged_out` no DM).
	///
	/// A queda e calibrada pra levar exatamente meio auge: `DeclineMod = 100 / auge` faz o corpo ir
	/// de 25 a 0 em `auge * 0,5` anos, fechando a morte em `auge * 1,5` -- o
	/// <see cref="TempoDeVida"/>.
	/// </summary>
	public static double IdadeDoCorpo(string raca, double idade)
	{
		if (NaoEnvelhece(raca)) return IdadeAdulta;

		double auge = AugeDaRaca(raca);
		if (idade <= IdadeAdulta) return idade;
		if (idade < auge) return IdadeAdulta;

		double declinioPorAno = 100 / auge;
		return IdadeAdulta - (idade - auge) * 0.5 * declinioPorAno;
	}

	/// <summary>
	/// Morreu de velhice? E o `Body < 0.1` do `AgeCheck` (Aging.dm:176-178) -- SO DEPOIS DO AUGE. O corpo de quem ainda cresce tambem e
	/// pequeno (ele E a idade, ver <see cref="IdadeDoCorpo"/>), e o DM nunca viu um de zero: la todo mundo nascia com 1 ano
	/// (`startingage`, `CharacterCreation.dm:41-44`). O filho nasce com 0 (pedido do dono, 2026-10-17), e sem a porta do auge morreria de
	/// velhice no primeiro login.
	/// </summary>
	public static bool MorreuDeVelhice(string raca, double idade) =>
		!NaoEnvelhece(raca) && idade > AugeDaRaca(raca) && IdadeDoCorpo(raca, idade) < 0.1;

	/// <summary>
	/// COMO O JOGADOR LE A PROPRIA IDADE. Serve a criacao (que precisa explicar o teto) e a ficha.
	/// </summary>
	public static string Fase(string raca, double idade)
	{
		if (NaoEnvelhece(raca)) return "não envelhece";
		double auge = AugeDaRaca(raca);
		if (idade <= IdadeAdulta) return "ainda crescendo";
		return idade >= auge ? "em declínio" : "no auge";
	}

	// =====================================================================
	// O CALENDARIO (dono, 2026-10-17)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada `--calendarioteste`): o calendario nao envelhece ninguem -- o port de antes, em que so a Sala do Tempo e
	/// o Growth Spurt mexiam na idade. Lido por <see cref="ContarMeses"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool CalendarioParadoDeTeste;

	/// <summary>
	/// O CALENDARIO ENVELHECE -- o `AgeCheck` que o `Years()` chama a cada mes pra todo mundo no jogo (`WorldClock.dm:86-92`) e que o
	/// login chama (`Login.dm:345`): `Age += Year - LastYear` (`Aging.dm:143-146`), um decimo de ano por mes vivido. O dono: *"a idade
	/// avança quando apssar 1 ano ingame"*.
	///
	/// ============================ A IDADE DO PORT E INTEIRA, E OS DECIMOS MORAM EM `meses` ============================
	/// `ServerPlayer.Idade` e um inteiro -- o mesmo numero que o DM mostrava (`round(Age)`, `Statistics.dm:13`) --, e os decimos do
	/// `Age` do DM ficam em `meses`, de 0 a 9. O ano entra quando o personagem completa dez meses: cada um faz aniversario contado de
	/// quando nasceu, e nao todo mundo junto no 1 de Luty.
	///
	/// QUEM NAO ENVELHECE DEIXA O TEMPO PASSAR POR ELE (`envelhece` falso): o morto e o Deus da Destruicao com o titulo
	/// (`if(!dead && !IsAVampire && !god_is_god(src))`, `Aging.dm:145`) -- o mes e contado, a idade nao anda, e ninguem recupera depois
	/// os meses em que estava morto. (O vampiro do DM nao existe no port.)
	///
	/// O RELOGIO QUE ANDOU PRA TRAS nao desenvelhece ninguem: a conta espera o calendario passar de novo do mes ja contado -- o
	/// `if(SAge <= newage)` do DM (`:144`), que guarda contra o `Year` mexido pelo admin.
	/// ====================================================================================================================
	/// </summary>
	/// <param name="mesContado">O ultimo mes do calendario ja contado (o `LastYear` do DM).</param>
	/// <param name="mesAgora">O mes do calendario de agora.</param>
	public static ContaDosMeses ContarMeses(int idade, int meses, long mesContado, long mesAgora, bool envelhece)
	{
		if (mesAgora <= mesContado) return new(idade, meses, mesContado, 0);
		if (!envelhece || CalendarioParadoDeTeste) return new(idade, meses, mesAgora, 0);
		(int novaIdade, int novosMeses, int anos) = MaisMeses(idade, meses, mesAgora - mesContado);
		return new(novaIdade, novosMeses, mesAgora, anos);
	}

	/// <summary>
	/// MESES DE IDADE A MAIS: os do calendario, e os de fora dele -- o `Age += 0.1` do Growth Spurt do Saibaman e do Toque do Tempo
	/// (`saibaman.dm`), que e exatamente um mes. Dez meses viram um ano.
	/// </summary>
	public static (int Idade, int Meses, int AnosFechados) MaisMeses(int idade, int meses, long quantos)
	{
		long total = Math.Max(0, meses) + Math.Max(0, quantos);
		int anos = (int)(total / World.Calendario.MesesPorAno);
		return (idade + anos, (int)(total % World.Calendario.MesesPorAno), anos);
	}
}

/// <summary>
/// A CONTA DOS MESES DE UM PERSONAGEM (<see cref="Envelhecimento.ContarMeses"/>): a idade e os meses de agora, o mes do calendario ate o
/// qual ja se contou, e quantos anos fecharam nesta conta.
/// </summary>
public readonly record struct ContaDosMeses(int Idade, int Meses, long MesContado, int AnosFechados);
