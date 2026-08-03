namespace Jandirus.Core.Stats;

/// <summary>
/// A ENERGIA VOLTANDO SOZINHA -- o `KiRegen()` do original (`Modules/Stats/Ki.dm:19-54`).
///
/// ============================ ISTO NAO EXISTIA NO PORT ============================
/// O `KIregen` era calculado a cada tique no `Statify` e NINGUEM O LIA. Um grep no repositorio
/// inteiro dava dois resultados: a declaracao do campo e a atribuicao. Ou seja, o port so
/// GASTAVA Ki (correr, investir, sustentar forma, tecnicas) e nunca repunha -- a energia so
/// descia, e a unica forma de recuperar era relogar.
///
/// E o mesmo defeito de sempre neste projeto: a conta certa, escrita no lugar certo, sem
/// ninguem do outro lado. Ver o corte do sigilo e as flags de nivel.
/// =================================================================================
///
/// ============================ 3,33 Hz, E POR QUE ============================
/// O `KiRegen()` e chamado pelo `statify()`, que roda dentro do `GlobalStats()` -- um laco que
/// termina em `sleep(3)` (`Stats.dm:65`), ou seja 3 decimos: 3,33 chamadas por segundo.
///
/// O tique de ficha deste port roda a 5 Hz. Copiar os valores POR CHAMADA do DM daria 1,5x de
/// regeneracao calada -- e "calada" e o problema, nao o 1,5x: ninguem descobriria comparando
/// com o original porque o numero no codigo seria identico. Entao aqui tudo vira TAXA POR
/// SEGUNDO e o chamador passa `dt`, do mesmo jeito que a <c>CargaDeKi</c> ja faz.
/// ===========================================================================
/// </summary>
public static class RegenDeKi
{
	/// <summary>Chamadas por segundo do original (`sleep(3)`). Converte "por chamada" em "por segundo".</summary>
	private const double PorSegundo = 10.0 / 3.0;

	/// <summary>
	/// O piso de folego dos ramos gordos: `stamina >= maxstamina/10`.
	///
	/// TEM QUE SER NO `stamina` CRU e nao no `staminapercent`. O `staminapercent` do Statify e
	/// `max(stamina,1)/maxstamina` -- com piso 1 no NUMERADOR, ele nunca chega a zero. Quem
	/// escrever o corte contando que ele zera descobre que a regeneracao nunca para.
	/// </summary>
	private const double FracaoDeFolego = 0.10;

	/// <summary>
	/// FORMA NAO MASTERIZADA QUEIMA KI PRA SE SUSTENTAR: a regeneracao cai a 20% enquanto houver
	/// dreno (`Ki.dm:21-26`). E o que faz o dreno ser LIQUIDO negativo -- sem isto a regeneracao
	/// passa o dreno e o Super Saiyajin vira infinito, que e um defeito ja visto neste projeto.
	/// </summary>
	private const double RegenEmFormaComDreno = 0.2;

	/// <summary>Teto do `extracharge`, igual ao do DM.</summary>
	public const double TetoDaCarga = 50;

	/// <summary>
	/// UM PASSO DE REGENERACAO.
	///
	/// <paramref name="drenoDaForma"/> e quanto a forma atual custa por segundo (0 = forma
	/// masterizada ou forma base). Vem de fora porque quem conhece a escada de transformacao e o
	/// servidor, e este arquivo e do Core.
	/// </summary>
	public static void Passo(Fighter f, double dt, double drenoDaForma)
	{
		if (dt <= 0) return;

		double formRegen = drenoDaForma > 0 ? RegenEmFormaComDreno : 1;
		double sp = f.staminapercent;
		double maxKi = Math.Max(f.MaxKi, 1e-9);

		// O PORTAO DO DM: nao regenera com o tanque cheio, sem folego nenhum, ou nocauteado.
		if (f.Ki < f.MaxKi && f.stamina >= 0 && !f.KO)
		{
			double piso = f.maxstamina * FracaoDeFolego;

			// TANQUE NO FUNDO (<=10%): jato de emergencia, 3x, pago caro em folego. E o que
			// impede alguem de ficar preso sem Ki nenhum -- do fundo do poco sempre se sai.
			if (f.Ki <= f.MaxKi * 0.1 && f.stamina >= piso)
			{
				f.Ki += f.KIregen * 3 * sp * PorSegundo * dt;
				f.stamina -= f.KIregen * sp / 4 * PorSegundo * dt;
			}
			else if (f.stamina >= piso)
			{
				f.Ki += f.KIregen * sp / 6 * formRegen * PorSegundo * dt;
				f.stamina -= f.KIregen * sp / maxKi / 9 * PorSegundo * dt;
			}

			// MEDITAR E O CAMINHO BARATO, e o comentario do DM diz por que: "makes meditation
			// useful for regenning Ki even if you're shit at KIregen, to save on stamina". O
			// `+0.05` e valor de graca -- meditar rende mesmo pra quem nao tem stat nenhum.
			if (f.med && f.MeditateGivesKiRegen != 0)
			{
				f.Ki += (f.KIregen * sp * 6 + 0.05) * PorSegundo * dt;
				f.stamina -= f.KIregen * sp / maxKi / 6 * PorSegundo * dt;
			}

			// EM LUTA REGENERA PELA METADE. Nao e penalidade: e o que faz a energia ser um
			// recurso DENTRO da briga em vez de um poco sem fundo.
			if (f.IsInFight)
			{
				f.Ki += f.KIregen * sp / 2 * formRegen * PorSegundo * dt;
				f.stamina -= f.KIregen * sp / maxKi / 8 * PorSegundo * dt;
			}
			else
			{
				f.Ki += f.KIregen * sp * formRegen * PorSegundo * dt;
				f.stamina -= f.KIregen * sp / maxKi / 3 * PorSegundo * dt;
			}

			// "No matter what, you get a tiny amount of ki regen" (Ki.dm:45). E a rede que
			// garante que ninguem fica com o Ki travado em zero pra sempre por stat ruim.
			f.Ki += 0.1 * PorSegundo * dt;

			// O TETO E OBRIGATORIO. Sem ele a regeneracao empurra o Ki acima do MaxKi, e
			// `Fighter.Power` faz `kiratio = Ki/MaxKi` alimentar o BP expresso -- 200% de Ki
			// viraria 2x de poder de graca. Passar dos 100% tem que continuar sendo privilegio
			// EXCLUSIVO da tecla C (ver CargaDeKi), que cobra folego e machuca no excesso.
			f.Ki = Math.Min(f.MaxKi, f.Ki);
		}

		// ============================ O TROCO DA CARGA ============================
		// FORA do portao acima, de proposito: e o unico ramo do DM que pode passar do MaxKi, e
		// vem DEPOIS do teto. E o que sobra da energia que se comprimiu segurando o C -- ela
		// volta como Ki ao longo dos segundos seguintes, em vez de sumir ao soltar a tecla.
		//
		// A PARABOLA `-(x-25)^2/100 + 6.25` vale ZERO nas pontas (0 e 50) e 6,25 no meio: carga
		// pouca quase nao rende, carga media rende o maximo, e carga cheia rende pouco -- e vai
		// rendendo mais conforme decai pro meio. Nao e curva arbitraria, e a do original.
		if (f.extracharge > 0)
		{
			f.extracharge = Math.Clamp(f.extracharge, 0, TetoDaCarga);
			double x = f.extracharge - 25;
			f.Ki += f.KIregen * (-(x * x) / 100 + 6.25) * PorSegundo * dt;
			f.stamina -= f.KIregen * sp / maxKi / 3 * PorSegundo * dt;
			f.extracharge = Math.Max(0, f.extracharge - PorSegundo * dt);
		}

		f.stamina = Math.Max(0, f.stamina);
	}
}
