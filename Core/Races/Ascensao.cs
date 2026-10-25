using Jandirus.Core.Forms;

namespace Jandirus.Core.Races;

/// <summary>
/// ============================ A ASCENSAO DAS RACAS SEM TRANSFORMACAO (dono, 2026-10-23) ============================
/// O pedido: *"raças q nao tem transformaçao racial coloque o sistema de ascencion nelas (saibamen, kaio , tuffle, etc) , mas q
/// aumente o bp delas proporcionalmente ao nivel de transformaçao q um saiyajin teria no bp dela"*. A escolha dele, perguntada:
/// "Rampa suave, so jogadores" -- um bonus de BP SEMPRE LIGADO (sem dreno, sem maestria), que sobe AOS POUCOS pelo BP BASE: 2x na
/// porta do SSJ1, 4x na do SSJ2, 16x na do SSJ3, sem degrau seco entre elas. NPC fica como esta.
///
/// ONDE ELE MORA NA CONTA: no `Fighter.BPBoost`, o campo da Ascensao do DM (`ascensioncontrols.dm:2`) que o `powerlevel()` ja
/// multiplicava na familia 1 (`Fighter.Power.cs`: `tempBP *= BPBoost * formBuff * racialBp`) e que ninguem escrevia. Quem escreve e
/// o servidor (`GameServer.AfirmarAAscensao`), com a conta daqui; o `racialBp` NAO serve, as passivas o reescrevem a cada tique.
///
/// A FORMULA DO DM FICOU DE FORA, e de proposito: o `Auto_Gain()` (`ascensioncontrols.dm:73-109`) sobe ate 17x aos ~6 milhoes,
/// depende de uma bandeira de mundo (`AscensionStarted`) e de um `log2(Ascension Mod)` por raca. A regra do dono e outra -- os
/// degraus do Saiyajin --, e ela manda.
///
/// ============================ QUEM ASCENDE ============================
/// A raca que nao tem ESCADA RACIAL no catalogo de formas: o mesmo `Catalogo.LinhasAbertas` que decide o que a tecla C sobe, com o
/// perfil de quem nunca despertou o ki divino (as escadas divinas se aprendem, nao sao de raca), e uma linha conta como escada se
/// tem algum degrau que se sobe (o Oozaru nao: ele acontece). Hoje isso da as 14 do dono -- Android, Arlian, Demigod, Demon, Gray,
/// Kai, Kanassa, Makyo, Meta, Saibaman, Shapeshifter, SpiritDoll, Tsujin e Yardrat -- e a bancada `--ascensaoteste` confere a lista
/// contra o `races.json` inteiro. DERIVADO e nao uma lista escrita aqui: a raca que ganhar escada amanha sai da Ascensao sozinha, que e
/// o que "racas q nao tem transformacao" quer dizer.
///
/// O MAJIN FICA DE FORA, todas as classes, por decisao do dono: o que nao e Corrupted ganha a linha dele (o Super Majin), e o
/// Corrupted tem a saga. Fora dito aqui, e nao derivado, porque o Corrupted nao tem escada no `LinhasAbertas` (a Forma Pura e posta
/// pela saga) e cairia na regra.
///
/// A RACA E A GRAVADA (`ServerPlayer.Race`, a do perfil de formas): o meio-sangue conta pela raca que o personagem tem, e nao pela
/// do pai -- decisao minha, a mesma pergunta que o perfil de formas faz.
/// ======================================================================================================================
/// </summary>
public static class Ascensao
{
	/// <summary>
	/// A ANCORA DE 1x -- onde a rampa comeca, abaixo da porta do SSJ1. DECISAO MINHA (o dono deu as tres portas de cima): e o ponto em
	/// que a Ascensao do DM comeca, `if(BP>=1000000 ...)` (`ascensioncontrols.dm:86`), e abaixo dele o DM poe `BPBoost = 1`
	/// (`:108-109`). Entre ela e a porta do SSJ1 o bonus vai de 1x a 2x.
	/// </summary>
	public const double BpDoComeco = 1_000_000;

	/// <summary>
	/// O QUE O SAIYAJIN TEM NA PORTA DE CADA FORMA -- o primeiro multiplicador da entrada do catalogo, o de maestria zero (o SSJ1 e
	/// 2x ate dominar, o SSJ2 comeca em 4x, o SSJ3 e 16x fixo). LIDO DO CATALOGO e nao copiado: "proporcionalmente ao nivel de
	/// transformacao q um saiyajin teria" -- se a escada do Saiyajin mudar, a Ascensao acompanha (e a bancada, que conhece os numeros
	/// do dono, fica vermelha pra alguem decidir).
	/// </summary>
	public static readonly double NaPortaDoSsj1 = PrimeiroMult(Catalogo.IdSsj1),
								  NaPortaDoSsj2 = PrimeiroMult("ssj2"),
								  NaPortaDoSsj3 = PrimeiroMult("ssj3");

	/// <summary>
	/// AS ANCORAS DA RAMPA, em ordem: (BP base, multiplicador). As portas sao as CONSTANTES do catalogo (`Catalogo.PortaSsj1..3`),
	/// e nao o limiar sorteado de ninguem: o `LimiaresPessoais.Rolar` so sorteia pras racas que tem escada, entao as que ascendem
	/// ficam nas portas de fabrica. Acima da ultima o bonus fica no teto (16x).
	/// </summary>
	public static readonly (double Bp, double Mult)[] Ancoras =
	[
		(BpDoComeco, 1),
		(Catalogo.PortaSsj1, NaPortaDoSsj1),
		(Catalogo.PortaSsj2, NaPortaDoSsj2),
		(Catalogo.PortaSsj3, NaPortaDoSsj3),
	];

	private static double PrimeiroMult(string id) => Catalogo.Def(id)!.Mult[0];

	/// <summary>
	/// O BONUS PELO BP BASE: 1x ate a ancora de baixo, LINEAR NO LOGARITMO DO BP entre duas ancoras, e o teto acima da ultima. Linear
	/// no log porque o BP deste jogo anda em ordens de grandeza -- de 1,5 milhao a 583 milhoes sao 389 vezes --, e uma rampa linear no
	/// BP cru ficaria parada perto da porta de baixo e daria o salto inteiro colado na de cima, que e o degrau seco que o dono nao quer.
	/// Nas ancoras o valor e exato (2, 4, 16), e entre elas nao ha salto: so muda a inclinacao.
	/// </summary>
	public static double Rampa(double bpBase)
	{
		(double Bp, double Mult)[] a = Ancoras;
		// (o `!(>)` deixa o NaN no piso, e nao no teto)
		if (!(bpBase > a[0].Bp)) return a[0].Mult;
		for (int i = 1; i < a.Length; i++)
		{
			if (bpBase >= a[i].Bp) continue;
			(double b0, double m0) = a[i - 1];
			(double b1, double m1) = a[i];
			if (DegrauSecoDeTeste) return m0;
			return m0 + (m1 - m0) * Math.Log(bpBase / b0) / Math.Log(b1 / b0);
		}
		return a[^1].Mult;
	}

	/// <summary>
	/// ESTA RACA (com esta classe) ASCENDE? Nao e Majin e nao tem escada racial no catalogo -- ver o cabecalho. Guardado por
	/// (raca, classe): a resposta so depende dos dois e do catalogo, que e fixo, e o servidor pergunta a cada tique de quem joga.
	/// </summary>
	public static bool Ascende(string? raca, string? classe)
	{
		raca ??= "";
		classe ??= "";
		if (string.Equals(raca, SonoDoMajin.Raca, StringComparison.OrdinalIgnoreCase) && !MajinAscendeDeTeste) return false;
		if (EscadaNaoContaDeTeste) return true;
		lock (_semEscada)
		{
			if (!_semEscada.TryGetValue((raca, classe), out bool sem))
			{
				var perfil = new PerfilDeFormas(Raca: raca, Classe: classe);   // GodKi -1: o ki divino nao e escada de raca
				sem = !Catalogo.LinhasAbertas(perfil).Any(l => Catalogo.DaLinha(l).Any(d => !Catalogo.NaoSeSobePraEla(d)));
				_semEscada[(raca, classe)] = sem;
			}
			return sem;
		}
	}

	private static readonly Dictionary<(string, string), bool> _semEscada = [];

	/// <summary>
	/// O `BPBoost` DESTE CORPO AGORA. 1 pra quem nao ascende; a rampa pra quem ascende -- MENOS EM FORMA ABSOLUTA.
	///
	/// ============================ A FORMA ABSOLUTA TOMA O LUGAR DA ASCENSAO (decisao minha, perguntar ao dono) ============================
	/// As formas `Absoluta` do catalogo (as divinas, o Mistico, as disciplinas divinas, a Fury) SUBSTITUEM a escada em vez de se
	/// somar a ela -- `FormaDef.Absoluta`, do `Fusion.dm:33` e do `base.dm:132`: o Saiyajin no Mistico nao e "SSJ3 vezes Mistico".
	/// A Ascensao e o equivalente da escada pra quem nao tem uma (o "proporcionalmente" do dono), entao a mesma regra vale: um Kai de
	/// 2 bilhoes no Mistico expressa os 16x do Mistico, como o Saiyajin de 2 bilhoes, e nao 16 x 16. Somadas, a raca sem escada
	/// passaria a ser 16 vezes mais forte que o Saiyajin em toda forma divina -- o contrario de "proporcional".
	/// ==============================================================================================================================
	/// </summary>
	public static double Fator(string? raca, string? classe, double bpBase, bool emFormaAbsoluta)
	{
		if (!Ascende(raca, classe)) return 1;
		if (emFormaAbsoluta && !FormaAbsolutaSomaDeTeste) return 1;
		return Rampa(bpBase);
	}

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--ascensaoteste`) -- falsos em jogo, sempre
	// =====================================================================

	/// <summary>DEFEITO INJETADO: a rampa vira DEGRAU SECO -- vale o numero da porta de baixo ate cruzar a de cima. Lido pela <see cref="Rampa"/>.</summary>
	public static bool DegrauSecoDeTeste;

	/// <summary>DEFEITO INJETADO: o Majin deixa de ficar de fora (ele nao tem escada no catalogo e cairia na regra). Lido pelo <see cref="Ascende"/>.</summary>
	public static bool MajinAscendeDeTeste;

	/// <summary>DEFEITO INJETADO: a escada racial deixa de contar -- o Saiyajin e o Namekuseijin ascendem junto. Lido pelo <see cref="Ascende"/>.</summary>
	public static bool EscadaNaoContaDeTeste;

	/// <summary>DEFEITO INJETADO: a forma absoluta SOMA com a Ascensao (16 x 16 no Mistico). Lido pelo <see cref="Fator"/>.</summary>
	public static bool FormaAbsolutaSomaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO: os marcos de ganho da Ascensao voltam aos degraus do DM (`BPBoost` 5/10/20, `LinearGain.dm:48-51`), que a
	/// rampa do dono so alcanca nos ~646 milhoes, ~1,08 bilhao e nunca. Lido pelo `Fighter.CheckAscensionMilestone`.
	/// </summary>
	public static bool MarcosNoDegrauDoDmDeTeste;
}
