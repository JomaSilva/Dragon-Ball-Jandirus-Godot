using Jandirus.Core.World;

namespace Jandirus.Core.Ai;

/// <summary>
/// A AGUA, PRA QUEM NAO TEM TECLADO -- o que um NPC faz quando cai num lago ou quando o lago esta no
/// caminho. Pedido do dono (2026-09-07): *"faca com que a IA saiba nadar quando precisar (quando e
/// jogada na agua ou quando precisa atravessar a agua) mas se ela souber voar ela de preferencia pra
/// voo"*.
///
/// ============================ E UM FILTRO, COMO A ARENA ============================
/// Nao e um plano. Um plano "sair da agua" competiria com "pressionar" e "fugir" na escolha do
/// cerebro e perderia justamente quando importa (um corpo fugindo por dentro do lago). Entao isto e
/// aplicado POR CIMA do comando que o plano montou -- o mesmo desenho do `Cerebro.NaArena` -- e vale
/// tambem pra quem nem tem plano: a rotina dos habitantes passa pelo mesmo filtro antes do passo.
/// Uma regra so pros dois caminhos, porque a alternativa e a de sempre: duas copias, uma delas
/// esquecida, e o cidadao boiando pra sempre onde o lutador nada.
///
/// ============================ O QUE ELA DECIDE ============================
/// A agua so para quem esta A PE (`ClasseDeAgua.Bloqueia`); nadando ou voando ela deixa de existir.
/// Entao a pergunta inteira e "que MODO este corpo escolhe?", e a resposta tem a ordem do pedido:
///
///   1. VOAR, se pode (capacidade E perfil, `Capacidades.PodeVoar`) e tem Ki pra decolar
///      (`CustoDeDecolar`, o mesmo que o verb cobra). E o `flight` do `testWaters()` (`Swim.dm:31`).
///   2. NADAR, se tem folego (`KiParaNadar`, o mesmo `Nado.KiParaComecar` que o verb `Swim` exige).
///      Pelo MESMO canal do jogador: `Comando.Habilidade = "nadar"` -> `UsarHabilidade` ->
///      `AlternarNado`, com a recusa, o aviso e o custo dele.
///   3. BOIAR, senao. Parado, sem gastar nada, ate o Ki voltar -- exatamente o que o dono pediu pro
///      jogador exausto ("ficar preso la tendo que recarregar o ki"). Um NPC nao ganha atalho.
///
/// E DUAS REGRAS DE QUEM JA ESTA NO MODO CERTO:
///   * NADANDO OU VOANDO SEM RUMO (o habitante ocioso que caiu no lago): vai pra margem mais perto
///     (`Percepcao.RumoDaMargem`). Sem isto ele nadaria parado, pagando Ki, ate boiar.
///   * VOANDO POR CIMA DA AGUA: nao pousa (veta `QuerDescer`/`AlternarVoo`), pelo mesmo motivo do
///     `NaArena`: pousar no lago e cair no desvio "nao dava pra pousar ai" do servidor. Quem so
///     levantou voo pra sair da agua (<paramref name="pousarNoSeco"/>, a rotina) pousa assim que o
///     chao embaixo for seco -- o habitante nao e um voador, e um cidadao que saiu do lago.
///
/// A CARENCIA (<see cref="Carencia"/>) e entre duas trocas de modo decididas aqui: sem ela o corpo
/// na beira decolaria e pousaria a cada tique, pagando a decolagem a cada um -- o mesmo defeito que a
/// arena ja tinha pego.
/// ==========================================================================================
/// </summary>
public static class Travessia
{
	/// <summary>Segundos entre duas trocas de modo decididas por esta regra.</summary>
	public const double Carencia = 2.0;

	/// <summary>
	/// DESLIGA A REGRA -- so pra bancada, como defeito injetado: com isto ligado o corpo jogado no
	/// lago fica la, que e o que a bancada precisa VER pra provar que mede a regra e nao a sorte.
	/// </summary>
	public static bool DesligadaDeTeste;

	/// <summary>
	/// O FILTRO. Devolve o comando ajustado; <paramref name="carencia"/> e o relogio do corpo (o
	/// chamador o abate por tique); <paramref name="porque"/> so vem preenchido quando algo mudou.
	/// </summary>
	public static Comando Atravessar(in Percepcao p, in Capacidades cap, Comando c, ref double carencia,
									 bool pousarNoSeco, out string? porque)
	{
		porque = null;
		if (DesligadaDeTeste || p.Caido) return c;
		// NO AR POR UM GOLPE nao se decide nada: o arremesso atravessa a agua sozinho (`M.KB` do
		// `testWaters`), e ligar o nado no meio do voo seria nadar no ar. A pergunta volta no pouso.
		if (p.Arremessado) return c;

		bool semRumo = c.Rumo.LengthSquared <= 1e-6f;
		bool temMargem = p.RumoDaMargem.LengthSquared > 1e-6f;

		if (p.EstouVoando)
		{
			// NAO DESCER: por cima do lago, e tambem nos primeiros segundos depois de decolar pra
			// cruza-lo (a carencia ainda correndo). Sem a segunda metade a bancada viu o voador decolar
			// na beira e POUSAR no tique seguinte -- o cerebro desce pra bater num alvo que esta no chao
			// (`DeveDescerDoCeu`), e na beira o chao embaixo ainda e seco. Dois segundos de voo garantido
			// o poem sobre a agua, e dali quem segura e a primeira metade.
			bool cruzando = p.NaAgua || carencia > 0;
			if (cruzando && (c.QuerDescer || c.AlternarVoo))
			{
				porque = p.NaAgua ? "agua: voando por cima do lago, nao descer" : "agua: acabou de decolar pra cruzar, nao descer ainda";
				return c with { QuerDescer = false, AlternarVoo = false };
			}
			if (p.NaAgua && semRumo && temMargem)
			{
				porque = "agua: voando sem rumo, ir pra margem";
				return c with { Rumo = p.RumoDaMargem, Correndo = false };
			}
			if (pousarNoSeco && !p.NaAgua && carencia <= 0 && !c.AlternarVoo)
			{
				carencia = Carencia;
				porque = "agua: chao seco embaixo, pousar";
				return c with { AlternarVoo = true };
			}
			return c;
		}

		if (p.EstouNadando)
		{
			// Nadando o corpo ja atravessa; so falta rumo a quem nao tem nenhum. Chegar no seco
			// desliga o nado sozinho (o tique do servidor), sem esta regra precisar saber.
			if (semRumo && temMargem)
			{
				porque = "agua: nadando sem rumo, ir pra margem";
				return c with { Rumo = p.RumoDaMargem, Correndo = false };
			}
			return c;
		}

		// A PE. Ou caiu na agua (esta nela e nao anda), ou quer entrar nela (o passo bateu na agua).
		if (!p.NaAgua && !p.BarradoPelaAgua) return c;

		if (carencia > 0)
			return p.NaAgua ? c with { Rumo = Vec2.Zero, Correndo = false } : c;

		if (cap.PodeVoar && p.Ki >= cap.CustoDeDecolar)
		{
			carencia = Carencia;
			porque = p.NaAgua ? "agua: caiu no lago, decolar (voo antes de nado)" : "agua no caminho: decolar (voo antes de nado)";
			return c with { AlternarVoo = true, Correndo = false, QuerDescer = false };
		}
		if (p.Ki >= cap.KiParaNadar)
		{
			carencia = Carencia;
			porque = p.NaAgua ? "agua: caiu no lago, nadar" : "agua no caminho: nadar";
			return c with { Habilidade = "nadar", Correndo = false, Rumo = semRumo && temMargem ? p.RumoDaMargem : c.Rumo };
		}

		// SEM FOLEGO. Na agua, boia parado ate o Ki voltar; na beira, fica onde esta (a agua barra).
		porque = p.NaAgua ? "agua: sem folego, boiando ate o Ki voltar" : "agua no caminho: sem folego pra nadar nem voar";
		return p.NaAgua ? c with { Rumo = Vec2.Zero, Correndo = false } : c;
	}
}
