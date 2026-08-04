namespace Jandirus.Core.Stats;

/// <summary>
/// O ESTOMAGO -- `Stomach.dm` e `StaminaDrain.dm` do original.
///
/// ============================ O QUE FALTAVA ============================
/// O port ja tinha VIGOR (`stamina`), e ele ja caia ao correr, socar e juntar Ki. O que nao havia
/// era de onde ele VOLTA: a barra subia sozinha, por nada, e comer nao existia. No original o
/// folego nao se recupera do ar -- ele e comprado com COMIDA, e a nutricao e o tanque que paga.
///
/// A cadeia inteira do DM e esta:
///
///     comida  ->  nutricao  ->  vigor (e Ki)  ->  poder de luta
///
/// O ultimo elo ja existia aqui (`staminaratio` entra no `statusBuff`, que multiplica o BP
/// expresso). Os dois primeiros sao o que este arquivo traz.
/// =======================================================================
///
/// ============================ O QUE NAO VEIO, E POR QUE ============================
/// O DM MATA DE FOME -- ou melhor, matava: o bloco de KO e morte por inanicao esta COMENTADO no
/// original, com a anotacao "Trying rebalanced stamina with no death". O que sobrou la e dano
/// continuo com vigor zerado. Portei o que esta LIGADO, e nao o que esta comentado: reviver uma
/// regra que o proprio autor desligou seria portar a intencao dele ao contrario.
/// ===================================================================================
/// </summary>
public static class Nutricao
{
	/// <summary>
	/// O TANQUE CHEIO. `maxNutrition = 50` no DM, e o metabolismo estica: quem digere rapido
	/// carrega mais.
	/// </summary>
	public const double TanqueBase = 50;

	/// <summary>Quanto do tanque um personagem novo ja traz. O DM nasce com ele cheio.</summary>
	public const double TanqueInicial = 50;

	/// <summary>
	/// COM QUE FREQUENCIA O ESTOMAGO TRABALHA, em segundos.
	///
	/// O DM roda `CheckStomach` a cada `30 / Metabolism` ticks, e um tique de BYOND e 0,1 s -- ou
	/// seja tres segundos com metabolismo 1. Manter a cadencia importa: todas as constantes abaixo
	/// sao POR PASSADA, e rodar isto no tique de 30 Hz multiplicaria a digestao por noventa.
	/// </summary>
	public const double SegundosPorPassada = 3;

	/// <summary>
	/// A FOME BATE quando o vigor cai a um quarto -- e o `stamina/maxstamina <= 0.25` do DM, que e
	/// onde o barrigao ronca na tela.
	/// </summary>
	public const double LimiarDeFome = 0.25;

	/// <summary>Acima disto o estomago para de converter: o corpo esta satisfeito.</summary>
	public const double LimiarDeSaciedade = 0.96;

	/// <summary>
	/// O TETO DO TANQUE deste personagem. Metabolismo alto = estomago maior.
	/// `maxNutrition = 50 * Metabolism` (Stomach.dm).
	/// </summary>
	public static double Tanque(double metabolismo) => TanqueBase * Math.Max(metabolismo, 0.1);

	/// <summary>
	/// UMA PASSADA DO ESTOMAGO: queima nutricao e devolve vigor e Ki.
	///
	/// ============================ A ORDEM E A DO ORIGINAL ============================
	/// Primeiro a nutricao DECAI sozinha (digestao custa, mesmo parado), depois vem o pingo de
	/// vigor que nao cobra nada, e so entao a conversao que consome o tanque. Trocar a ordem muda o
	/// saldo: o pingo gratuito antes do desconto e o que faz alguem com o tanque quase vazio ainda
	/// recuperar alguma coisa.
	///
	/// O DECAIMENTO E LOGARITMICO (`log(1.5, nutricao)`), e nao linear: tanque cheio digere mais
	/// rapido que tanque quase vazio. Sem isso, comer muito seria puro lucro -- com isso, o excesso
	/// evapora sozinho e a refeicao grande rende menos por unidade que duas pequenas.
	/// =================================================================================
	/// </summary>
	/// <returns>O quanto de vigor e de Ki esta passada devolveu.</returns>
	public static (double Vigor, double Ki) Digerir(Fighter f, double comidaGlobal = 1)
	{
		// SO DIGERE QUEM PRECISA. O DM exige tanque com algo dentro, vivo, vigor abaixo do maximo e
		// FORA DE COMBATE -- no meio da briga o corpo nao para pra processar comida.
		if (f.dead || f.CurrentNutrition <= 0 || f.stamina >= f.maxstamina || f.IsInFight)
			return (0, 0);

		double metabolismo = Math.Max(f.Metabolism, 0.1);
		double tanque = Tanque(f.Metabolism);

		// 1. a digestao cobra o proprio custo
		f.CurrentNutrition = Math.Max(
			f.CurrentNutrition - Math.Log(Math.Max(f.CurrentNutrition, 1), 1.5) * (tanque / 100) * 0.005, 0);

		// 2. o pingo que nao cobra nada
		double vigor = 0.005 * comidaGlobal * f.maxstamina / metabolismo;

		// 3. a conversao de verdade, que come o tanque
		double custoDeVigor = 0.025 * comidaGlobal * f.maxstamina / 100 / metabolismo;
		if (f.CurrentNutrition >= custoDeVigor) f.CurrentNutrition -= custoDeVigor;
		else
		{
			// O RESTO DO TANQUE DE UMA VEZ. E a saida do DM pra nao deixar migalha presa: com menos
			// que uma dose, gasta-se o que houver e converte-se pelo `satiationMod`.
			vigor += f.CurrentNutrition * f.satiationMod * (f.maxstamina / 100);
			f.CurrentNutrition = 0;
		}

		// 4. e o Ki, que sai do mesmo tanque
		double ki = 0;
		if (f.Ki < f.MaxKi && f.CurrentNutrition > 0)
		{
			double custoDeKi = 0.035 * comidaGlobal / metabolismo;
			if (f.CurrentNutrition >= custoDeKi)
			{
				f.CurrentNutrition -= custoDeKi;
				ki = 0.035 * comidaGlobal * f.MaxKi / 100 / metabolismo;
			}
			else
			{
				ki = f.CurrentNutrition * f.satiationMod * (f.MaxKi / 100);
				f.CurrentNutrition = 0;
			}
		}

		f.stamina = Math.Min(f.stamina + vigor, f.maxstamina);
		f.Ki = Math.Min(f.Ki + ki, f.MaxKi);
		return (vigor, ki);
	}

	/// <summary>
	/// O VIGOR QUEIMA COM O TEMPO -- o dreno passivo de `CheckNutrition`.
	///
	/// Existir custa folego, e lutar custa mais. Vontade (`Ewillpower`) e o stat de aguentar: ele
	/// divide o dreno, entao quem tem cabeca-dura cansa devagar.
	/// </summary>
	public static void QueimarVigor(Fighter f, double dreno = 1)
	{
		if (f.dead) { f.stamina = 0.5 * f.maxstamina; return; }

		double resistencia = Math.Max(200 * f.Ewillpower * f.Estaminamod, 0.01);
		double gasto = 1.5 * f.dashingMod * dreno * (Math.Max(f.Metabolism, 0.1) / 2) / resistencia;

		// LUTAR CANSA MAIS, e por um caminho proprio: a conta do DM soma um segundo termo em vez de
		// multiplicar o primeiro, entao o combate pesa igual pra quem tem vontade alta e pra quem
		// nao tem. E o que impede o personagem de vontade alta lutar de graca.
		if (f.IsInFight)
			gasto += 2 * dreno * f.dashingMod / Math.Max(150 * f.Ewillpower * f.Estaminamod, 0.01);

		f.stamina = Math.Max(f.stamina - gasto, 0);
	}

	/// <summary>
	/// COMER. Devolve o aviso que o jogador le, ou vazio se nao deu.
	///
	/// COMER DEMAIS NAO E PROIBIDO, e o excesso nao sela o tanque: o DM avisa que voce passou do
	/// ponto e deixa o valor estourar, e o decaimento logaritmico de `Digerir` cobra a conta
	/// depois. Travar no teto seria mais arrumado e mais pobre -- a punicao por gula deixaria de
	/// existir.
	/// </summary>
	public static string Comer(Fighter f, double nutricao)
	{
		f.CurrentNutrition += nutricao;
		f.Hungry = false;

		return f.CurrentNutrition > Tanque(f.Metabolism)
			? "você comeu além da conta. Não dá pra ver, mas seria melhor ir mais leve da próxima vez."
			: "";
	}

	/// <summary>
	/// O CORPO PEDE COMIDA? Duas portas, como no DM: vigor no chao, ou tanque vazio com vigor ja
	/// abaixo de 60%. A segunda existe pra o aviso chegar ANTES de o personagem travar.
	/// </summary>
	public static bool TemFome(Fighter f) =>
		f.stamina / Math.Max(f.maxstamina, 1) <= LimiarDeFome
		|| (f.CurrentNutrition <= 0 && f.stamina < f.maxstamina * 0.6);

	/// <summary>
	/// PASSAR FOME MACHUCA. Com o vigor no zero E o tanque vazio, o corpo comeca a se consumir --
	/// e o `SpreadDamage` do DM. Devolve quanto dano espalhar, ou zero.
	///
	/// COM TANQUE, NAO MACHUCA: o DM segura o vigor em 1 enquanto houver comida no estomago. E a
	/// diferenca entre "estou exausto" e "estou morrendo de fome".
	/// </summary>
	public static double DanoDaFome(Fighter f, ref double acumulado)
	{
		if (f.dead || f.stamina >= 1) return 0;

		if (f.CurrentNutrition > 0) { f.stamina = 1; return 0; }

		double dano = Math.Abs(f.stamina) * acumulado;
		acumulado = Math.Min(acumulado + f.stamina, 10);
		f.stamina = 0;
		return dano;
	}
}
