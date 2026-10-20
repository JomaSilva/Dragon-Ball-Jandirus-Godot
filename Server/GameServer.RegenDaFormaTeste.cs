using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--regenformateste` -- A FORMA NAO DOMINADA QUE CONSOME KI NAO REGENERA SOZINHA.
///
/// O pedido do dono (2026-10-19): *"enquanto usa formas nao masterizadas q tem consumo de ki, a regeneracao passiva de ki
/// deveria ser desativada ate masterizar a forma"*. A regra mora em dois lugares -- a DECISAO no servidor
/// (`FormaSemRegenPassiva`, que sabe se a forma consome Ki e se ja foi dominada) e o EFEITO no Core (`RegenDeKi.Passo`, que
/// separa a passiva da meditacao) -- e a bancada mede os dois pelo funil de producao, o `TickDaCarga`:
///
///   1. A PASSIVA SOME, A ATIVA FICA: no SSJ1 a 0% de maestria, com o Ki no meio do tanque, no fundo (o jato de <=10%), em luta
///      e com o folego no piso (onde so a parcela de fora de luta e o fio de 0,1 rendem), 10 s nao mexem no Ki nem no folego --
///      e o mesmo corpo na BASE sobe nos quatro (o controle: a regua enxerga regen quando ela existe). Meditando, o Ki sobe
///      EXATAMENTE a parcela da meditacao.
///   2. A DECISAO: SSJ1 dominado (100%, dreno zero) igual a base; SSJ2 a 99,9% parado e a 100% (dominado e ainda drenando)
///      abaixo da base, os 20% do DM; Ultra Instinct -Sign- com a proficiencia REAL da skill a 99 parado e a 100 regenerando; e
///      o SSJ1 cru com o dreno racial zerado (a Unmatched Ferocity: a forma nao consome) regenerando.
///   3. DE PONTA A PONTA (`TickDaForma` e `TickDaCarga`, na ordem do `TickDosRelogiosDoCorpo`): no SSJ1 nao dominado o Ki cai
///      EXATAMENTE o que o dreno cobra, e a forma se desfaz no tique em que o dreno sozinho esvazia o tanque.
///
/// Contra-exemplo: <see cref="FormaNaoDominadaRegeneraDeTeste"/> (a decisao volta a deixar a passiva a 20%, como antes de
/// 2026-10-19) -- as reguas das familias 2 e 3 REPROVAM com ele. As duas guardas `if (!semPassiva)` do Core se conferem por
/// mutacao, e e pra isso que a familia 1 tem o caso do folego no piso: so ele separa uma guarda da outra.
///
/// Corpos forjados, sem cliente, como os da `--poderteste` -- e corpo sem cliente e o que um NPC e: o tique e o mesmo. Roda no
/// boot e o servidor continua de pe.
///
///     Godot --headless --path . --server --port 8800 --regenformateste
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--regenformateste`): a forma nao dominada que consome Ki volta a regenerar sozinha, a 20%, como
	/// ate 2026-10-19. Lido pela propria linha do conserto, em <see cref="FormaSemRegenPassiva"/>. Mora aqui, e nao ao lado do
	/// conserto, pra este arquivo compilar sozinho contra o repo de antes dele.
	/// </summary>
	internal bool FormaNaoDominadaRegeneraDeTeste;

	private int _rfOk, _rfFalhou;

	/// <summary>O tique do servidor, e 10 s por medida: de sobra pra a regen de qualquer corpo aparecer no Ki.</summary>
	private const double RfDt = Protocol.TickSeconds;
	private const int RfTiques = 300;

	private void AfirmarRf(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _rfOk++; GD.Print($"[regenforma]   OK    {oque}"); return; }
		_rfFalhou++;
		GD.PrintErr($"[regenforma]   FALHA {oque}   {detalhe}");
	}

	public void RodarBancadaDaRegenDaForma()
	{
		_rfOk = _rfFalhou = 0;
		GD.Print("[regenforma] ================ A FORMA NAO DOMINADA NAO REGENERA SOZINHA ================");
		try
		{
			RfAPassivaSome();
			LimparTudoDaBancada();
			RfADecisao();
			LimparTudoDaBancada();
			RfDePontaAPonta();
		}
		catch (Exception e)
		{
			_rfFalhou++;
			GD.PrintErr($"[regenforma]   FALHA a bancada rodou inteira   {e}");
		}
		finally
		{
			// O PLACAR SAI DAQUI, haja o que houver -- e o botao de defeito volta ao lugar.
			FormaNaoDominadaRegeneraDeTeste = false;
			try { LimparTudoDaBancada(); } catch (Exception e) { GD.PrintErr($"[regenforma]   (limpeza) {e.Message}"); }
			GD.Print($"[regenforma] ================ {_rfOk} OK, {_rfFalhou} FALHA(S) ================");
		}
	}

	/// <summary>
	/// DEZ SEGUNDOS DE `TickDaCarga` num corpo posto numa forma, com a maestria dada: quanto o Ki e o folego andaram.
	///
	/// SEM `AplicarForma` nem `Statify`, de proposito: a ficha (`KIregen`, `staminapercent`, `MaxKi`) fica a mesma de um caso pro
	/// outro, entao o que muda o Ki entre eles so pode ser a decisao -- e por isso "igual a base" se cobra com igualdade. A
	/// maestria das quatro formas de disciplina e a proficiencia REAL da skill (o livro as recusa, ver `Maestrias.Por`), e e ela
	/// que o `Dominou` le.
	/// </summary>
	private (double Ki, double Folego) RfDez(ServerPlayer c, string forma, double maestria, double ki = 0.3, double folego = 1,
											 bool meditando = false, bool emLuta = false)
	{
		Fighter f = c.Ficha;
		c.Forma.Entrar(forma);
		if (Disciplinas.DaForma(forma) is { } disc) EstadoDe(c, disc.Def.Tipo).Real = maestria;
		else if (forma != Catalogo.IdBase) c.Forma.Maestria.Por(forma, maestria);
		f.Ki = f.MaxKi * ki;
		f.stamina = f.maxstamina * folego;
		f.med = meditando;
		f.IsInFight = emLuta;
		double ki0 = f.Ki, folego0 = f.stamina;
		try
		{
			for (int i = 0; i < RfTiques; i++) TickDaCarga(c, RfDt);
		}
		finally
		{
			f.med = false;
			f.IsInFight = false;
		}
		return (f.Ki - ki0, f.stamina - folego0);
	}

	private static Vec2 RfOnde(int coluna) => new(coluna * ZoneCollision.TileSize, 40 * ZoneCollision.TileSize);

	// =====================================================================
	// 1) A PASSIVA SOME, A ATIVA FICA
	// =====================================================================

	/// <summary>
	/// O SSJ1 a 0% de maestria (dreno 0,025: consome, e nao e dominado) contra a base, no mesmo corpo, em quatro tanques. O do
	/// FOLEGO NO PISO (5% do maximo) existe pras duas guardas do Core: abaixo de 10% de folego o ramo do jato e o do folego nao
	/// rendem, e sobram so a parcela de luta ou de fora dela e o fio de 0,1 -- a segunda guarda sozinha.
	/// </summary>
	private void RfAPassivaSome()
	{
		GD.Print("[regenforma] -- 1) a passiva some, a ativa fica: o SSJ1 a 0% contra a base --");
		ServerPlayer c = Forjar("Regenerante", RfOnde(40), 50_000);
		Fighter f = c.Ficha;
		f.MeditateGivesKiRegen = 1;   // o efeito do Ki Unlocked: sem ele meditar nao rende Ki (`Ki.dm:35`)
		GD.Print($"[regenforma]   (o corpo: MaxKi {f.MaxKi:0.###}, KIregen {f.KIregen:0.######}, folego {f.maxstamina:0.###})");

		try
		{
			foreach ((string caso, double ki, double folego, bool luta) in new[]
					 {
						 ("NO MEIO DO TANQUE", 0.3, 1.0, false),
						 ("NO FUNDO DO TANQUE", 0.05, 1.0, false),   // <=10%: o jato de emergencia, 3x
						 ("EM LUTA", 0.3, 1.0, true),
						 ("COM O FOLEGO NO PISO", 0.3, 0.05, false),
					 })
			{
				(double dKi, double dFolego) = RfDez(c, "ssj1", 0, ki, folego, emLuta: luta);
				double naBase = RfDez(c, Catalogo.IdBase, 0, ki, folego, emLuta: luta).Ki;
				AfirmarRf($"**SEM A PASSIVA {caso}**: no SSJ1 a 0% de maestria, 10 s de tique nao mexem no Ki", dKi == 0,
						  $"o Ki andou {dKi:0.#########}");
				AfirmarRf($"**NEM O FOLEGO {caso}**: a parcela que nao rende nao cobra", dFolego == 0, $"o folego andou {dFolego:0.#########}");
				AfirmarRf($"...e o controle {caso}: na BASE o mesmo corpo sobe ({naBase:0.######} de Ki)", naBase > 0);
			}

			// A MEDITACAO E A ATIVA, e ela fica. A conta e de soma, e exata: as parcelas so se somam no Ki, e o folego que uma cobra
			// nao muda o que a outra rende em 10 s (o piso de folego fica longe, e o `staminapercent` so anda no `Statify`). No SSJ1
			// cru, meditar rende "meditar na base" menos "a base parada" -- a parcela da meditacao, inteira. De 15% do tanque, acima
			// do jato e longe do teto: encostar no MaxKi cortaria a soma.
			double meditando = RfDez(c, "ssj1", 0, ki: 0.15, meditando: true).Ki;
			double meditandoNaBase = RfDez(c, Catalogo.IdBase, 0, ki: 0.15, meditando: true).Ki;
			double baseParada = RfDez(c, Catalogo.IdBase, 0, ki: 0.15).Ki;
			AfirmarRf($"**MEDITANDO NA FORMA NAO DOMINADA O KI SOBE** ({meditando:0.######}): a regen ATIVA continua", meditando > 0);
			AfirmarRf("...e sobe **SO A PARCELA DA MEDITACAO** (meditar na base, menos a base parada)",
					  Math.Abs(meditando - (meditandoNaBase - baseParada)) <= 1e-9 * f.MaxKi,
					  $"{meditando:0.#########} contra {meditandoNaBase - baseParada:0.#########}");
		}
		finally { c.Forma.Entrar(Catalogo.IdBase); }
	}

	// =====================================================================
	// 2) A DECISAO
	// =====================================================================

	/// <summary>
	/// As duas condicoes do dono, cada uma pelo funil que ja existe: DOMINADA e o `Dominou` (100% de maestria; a skill, nas formas
	/// de disciplina), CONSOME e o dreno que o `TickDaForma` cobra de verdade (o do catalogo vezes o racial).
	/// </summary>
	private void RfADecisao()
	{
		GD.Print("[regenforma] -- 2) a decisao: dominada, quase dominada, de disciplina e sem consumo --");
		ServerPlayer c = Forjar("Transformado", RfOnde(44), 50_000);

		try
		{
			double naBase = RfDez(c, Catalogo.IdBase, 0).Ki;
			AfirmarRf($"na BASE o corpo regenera ({naBase:0.######} de Ki em 10 s) -- a referencia das linhas de baixo", naBase > 0);

			double ssj1Dominado = RfDez(c, "ssj1", 100).Ki;
			AfirmarRf("**SSJ1 DOMINADO REGENERA IGUAL A BASE** (100% de maestria: parou de drenar)", ssj1Dominado == naBase,
					  $"{ssj1Dominado:0.#########} contra {naBase:0.#########}");

			double ssj2Quase = RfDez(c, "ssj2", 99.9).Ki;
			AfirmarRf("**SSJ2 A 99,9% NAO REGENERA**: a regra vale ATE os 100%", ssj2Quase == 0, $"o Ki andou {ssj2Quase:0.#########}");

			double ssj2Dominado = RfDez(c, "ssj2", 100).Ki;
			AfirmarRf($"**SSJ2 DOMINADO QUE AINDA DRENA REGENERA** ({ssj2Dominado:0.######}), abaixo da base -- os 20% do DM (`Ki.dm:21-26`)",
					  ssj2Dominado > 0 && ssj2Dominado < naBase, $"base {naBase:0.######}");

			double uiCru = RfDez(c, "ui_sign", 99).Ki;
			AfirmarRf("**ULTRA INSTINCT COM A SKILL A 99 NAO REGENERA**: a maestria da forma de disciplina e a proficiencia REAL da skill",
					  uiCru == 0, $"o Ki andou {uiCru:0.#########}");
			double uiDominado = RfDez(c, "ui_sign", 100).Ki;
			AfirmarRf($"**ULTRA INSTINCT COM A SKILL A 100 REGENERA** ({uiDominado:0.######}), abaixo da base (o dreno dela nao zera)",
					  uiDominado > 0 && uiDominado < naBase, $"base {naBase:0.######}");

			// A FORMA QUE NAO CONSOME NAO TRAVA: com o dreno racial zerado (a Unmatched Ferocity do Primal) o `TickDaForma` nao cobra
			// nada, e travar a regen ali deixaria o Ki parado pra sempre. O canal e o que o `TickDasRaciais` escreve.
			double semConsumo;
			c.Racial.Efeito.Dreno = 0;
			try { semConsumo = RfDez(c, "ssj1", 0).Ki; }
			finally { c.Racial.Efeito.Dreno = 1; }
			AfirmarRf($"**FORMA QUE NAO CONSOME KI REGENERA**: o SSJ1 a 0% com o dreno racial zerado ({semConsumo:0.######})", semConsumo > 0);

			// O CONTRA-EXEMPLO: a decisao volta a deixar a passiva a 20%, e as mesmas reguas tem de reprovar.
			FormaNaoDominadaRegeneraDeTeste = true;
			try
			{
				double ssj1ComDefeito = RfDez(c, "ssj1", 0).Ki;
				AfirmarRf("(defeito injetado: a forma nao dominada volta a regenerar a 20%) a mesma regua REPROVA o SSJ1 a 0%",
						  ssj1ComDefeito != 0, $"o Ki andou {ssj1ComDefeito:0.#########}");
				double ssj2ComDefeito = RfDez(c, "ssj2", 99.9).Ki;
				AfirmarRf("(defeito injetado) ...e o SSJ2 a 99,9%", ssj2ComDefeito != 0, $"o Ki andou {ssj2ComDefeito:0.#########}");
				double uiComDefeito = RfDez(c, "ui_sign", 99).Ki;
				AfirmarRf("(defeito injetado) ...e o Ultra Instinct a 99", uiComDefeito != 0, $"o Ki andou {uiComDefeito:0.#########}");
			}
			finally { FormaNaoDominadaRegeneraDeTeste = false; }
		}
		finally { c.Forma.Entrar(Catalogo.IdBase); }
	}

	// =====================================================================
	// 3) DE PONTA A PONTA
	// =====================================================================

	/// <summary>
	/// O `TickDaForma` (que cobra o dreno e desfaz a forma sem Ki) e o `TickDaCarga` (que regenera), na ordem do
	/// `TickDosRelogiosDoCorpo`, num SSJ1 a 0% de maestria. E o que o jogador sente: o tanque so desce pelo dreno, e a forma dura
	/// exatamente o Ki dividido pelo dreno.
	/// </summary>
	private void RfDePontaAPonta()
	{
		GD.Print("[regenforma] -- 3) de ponta a ponta: o SSJ1 nao dominado so perde o que o dreno cobra --");
		ServerPlayer c = Forjar("Sustentado", RfOnde(48), 50_000);
		Fighter f = c.Ficha;

		// SEGURAR O SSJ1 CRU a partir de uma fracao do tanque, ate `tiques` ou ate a forma cair: a queda do Ki, o que o dreno
		// cobrou (a conta do `TickDaForma`, feita antes de cada tique) e o tique em que a forma caiu (-1 = nao caiu).
		(double Queda, double Cobrado, int Caiu) Segurar(double fracao, int tiques)
		{
			c.Forma.Entrar("ssj1");
			c.Forma.Maestria.Por("ssj1", 0);
			AplicarForma(c);   // o tanque da forma ANTES de por o Ki (o `TickDaForma` a reaplica a cada tique, no mesmo degrau)
			f.Ki = f.MaxKi * fracao;
			f.stamina = f.maxstamina;
			double ki0 = f.Ki, cobrado = 0;
			for (int i = 0; i < tiques; i++)
			{
				cobrado += c.Forma.DrenoPorSegundo() * f.MaxKi * RfDt * c.Racial.Efeito.Dreno;
				TickDaForma(c, RfDt);
				if (c.Forma.NaBase) return (ki0 - f.Ki, cobrado, i);
				TickDaCarga(c, RfDt);
			}
			return (ki0 - f.Ki, cobrado, -1);
		}

		// A FORMA DURA O KI DIVIDIDO PELO DRENO: com 10,25 s de dreno no tanque (8,2% dele, no fundo, onde o jato de emergencia
		// morava) ela cai no tique 308 -- o 307 contando do zero --, com meio tique de folga pros dois lados, pra conta de ponto
		// flutuante nao decidir nada.
		const double Segundos = 10.25;
		double fracao = Segundos * Catalogo.DrenoPorSegundo("ssj1", new Maestrias()) * c.Racial.Efeito.Dreno;
		int esperado = (int)Math.Ceiling(Segundos / RfDt) - 1;

		try
		{
			(double queda, double cobrado, int caiu) = Segurar(0.5, RfTiques);
			AfirmarRf("no SSJ1 a 0%, 10 s de forma do meio do tanque: **O KI DESCE EXATAMENTE O QUE O DRENO COBRA** -- nada volta sozinho",
					  caiu < 0 && Math.Abs(queda - cobrado) <= 1e-9 * f.MaxKi,
					  $"desceu {queda:0.#########}, o dreno cobrou {cobrado:0.#########}, caiu no tique {caiu}");

			int caiuNoFundo = Segurar(fracao, 2 * esperado).Caiu;
			AfirmarRf($"...e **A FORMA DURA O KI DIVIDIDO PELO DRENO**: com {Segundos} s de dreno no tanque ela cai no tique {esperado}",
					  caiuNoFundo == esperado, $"caiu no tique {caiuNoFundo}");
			AfirmarRf("...e cai pra base", c.Forma.NaBase, c.Forma.Atual);

			// O CONTRA-EXEMPLO: com a passiva de volta a 20% (e o jato do fundo, que nem os 20% cortavam) o tanque desce menos que o
			// dreno e a forma dura mais.
			FormaNaoDominadaRegeneraDeTeste = true;
			try
			{
				(double queda2, double cobrado2, int caiu2) = Segurar(0.5, RfTiques);
				AfirmarRf("(defeito injetado: a forma nao dominada volta a regenerar a 20%) a mesma regua REPROVA: o Ki desce menos que o dreno",
						  !(caiu2 < 0 && Math.Abs(queda2 - cobrado2) <= 1e-9 * f.MaxKi),
						  $"desceu {queda2:0.#########}, o dreno cobrou {cobrado2:0.#########}");
				int caiuNoFundo2 = Segurar(fracao, 2 * esperado).Caiu;
				AfirmarRf("(defeito injetado) ...e a forma dura mais que o Ki dividido pelo dreno", caiuNoFundo2 != esperado,
						  $"caiu no tique {caiuNoFundo2}");
			}
			finally { FormaNaoDominadaRegeneraDeTeste = false; }
		}
		finally { c.Forma.Entrar(Catalogo.IdBase); }
	}
}
