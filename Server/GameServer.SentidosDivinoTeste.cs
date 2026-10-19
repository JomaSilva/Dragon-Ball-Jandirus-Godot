using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A FAMILIA 5 DA `--sentidosteste` (2026-10-18): O SCOUTER TEM TETO E NAO LE KI DIVINO; O SENSE SO SENTE O DIVINO COM KI DIVINO. O dono:
/// *"coloque um bp maximo no scouter de 100m de bp, acima disso o scouter marca com ???, e pessoas q estao em formas com godki, ou estao em
/// constante uso de ki divino (deuses da destruiçao, anjos, supremo kaioshins npcs e golden apple) n podem ter o poder lido com scouter ou ter o
/// ki sentido. somente pessoas q tb tem ki divino podem dar sense em outras pessoas com ki divino"* -- e *"scouter NUNCA consegue detectar ou
/// sentir ki divino"*. Pelas funcoes de producao (`SentidosDe`, `FichaVisivel`, `ScouterMostra`, `AchoAEnergia`, a lista do Instant
/// Transmission):
///   a. o Scan le exato ate 100 milhoes; acima, a presenca vem com o BP NaN (a tela escreve "???"); contra-exemplo `Scouter.SemTetoDeTeste`;
///   b. o ki divino NAO aparece no Scan: a forma divina (SSG), a classe Golden Apple, o cargo de Anjo e o Supremo Kaioh NPC -- nem no aparelho
///      de quem tem ki divino; contra-exemplo `KiDivinoAparecerDeTeste`;
///   c. a ficha do dono do scouter: o proprio poder acima do teto, ou o proprio ki divino, chega "???";
///   d. as frases com numero perguntam o mesmo (`ScouterMostra`);
///   e. o Sense de um mortal nao sente o divino, o de quem tem ki divino sente -- e a telepatia e o Instant Transmission seguem a mesma regra;
///      contra-exemplo `KiDivinoAparecerDeTeste`.
/// </summary>
public partial class GameServer
{
	private void OScouterTemTetoEKiDivino()
	{
		GD.Print("[sentidos] -- 5) O SCOUTER TEM TETO E NAO LE KI DIVINO; O SENSE SO SENTE O DIVINO COM KI DIVINO (dono, 2026-10-18)");
		Vec2 c = CorredorLivre(28);
		ServerPlayer eu = StCorpo("Vidro", c, 10_000);
		eu.PoderesConcedidos |= Protocol.Poder.Scouter;
		AplicarPoderes(eu);
		ServerPlayer abaixo = StCorpo("Abaixo", Em(c, 3, 0), 60_000_000);
		ServerPlayer acima = StCorpo("Acima", Em(c, 4, 0), 160_000_000);
		ServerPlayer emForma = StCorpo("EmForma", Em(c, 5, 0), 20_000);
		emForma.Forma.Atual = "ssg";
		ServerPlayer dourado = StCorpo("Dourado", Em(c, 6, 0), 20_000);
		dourado.Ficha.Class = Jandirus.Core.Forms.DespertarDivino.ClasseDeKiDivino;
		ServerPlayer anjo = StCorpo("Anjo", Em(c, 7, 0), 20_000);
		string? anjoAntes = _tronos.TryGetValue(Jandirus.Core.Npc.BillsEWhis.CargoDoWhis, out string? a0) ? a0 : null;
		_tronos[Jandirus.Core.Npc.BillsEWhis.CargoDoWhis] = anjo.Conta;
		ServerPlayer? kaioh = null;
		if (_moldes?.Todos.FirstOrDefault(m => m.Id == "supremo_kaioh") is { } mk)
		{
			kaioh = StCorpo("Kaioh", Em(c, 8, 0), 20_000);
			kaioh.Papel = new PapelDeNpc(mk, 3);
			kaioh.Conta = "";
		}
		bool Divinos(List<Protocol.PresencaState> l, bool presentes) =>
			presentes
				? Acha(l, emForma) != null && Acha(l, dourado) != null && Acha(l, anjo) != null && (kaioh == null || Acha(l, kaioh) != null)
				: Acha(l, emForma) == null && Acha(l, dourado) == null && Acha(l, anjo) == null && (kaioh == null || Acha(l, kaioh) == null);

		try
		{
			// ---------------------------------------------------------------- a. o teto
			(bool scan, List<Protocol.PresencaState> l) = SentidosDe(eu);
			AfirmarSn("preparo: o Abaixo expressa ate 100 milhoes e o Acima passa disso; o Kaioh NPC existe no catalogo",
					  abaixo.Ficha.expressedBP <= Scouter.TetoDeLeitura && acima.Ficha.expressedBP > Scouter.TetoDeLeitura && kaioh != null,
					  $"{abaixo.Ficha.expressedBP:N0} / {acima.Ficha.expressedBP:N0}; kaioh {kaioh != null}");
			AfirmarSn("**O SCAN LE EXATO ATE 100 MILHOES**: o Abaixo vem com o BP inteiro",
					  scan && Acha(l, abaixo) is { } pa && pa.Bp == Math.Round(abaixo.Ficha.expressedBP, MidpointRounding.AwayFromZero), Nomes(l));
			AfirmarSn("**...E ACIMA DISSO O SCOUTER MARCA ???** (*\"acima disso o scouter marca com ???\"*): o Acima aparece, com o BP NaN -- a aba "
					  + "Scan escreve ???", Acha(l, acima) is { } pc && double.IsNaN(pc.Bp), Nomes(l));
			Scouter.SemTetoDeTeste = true;
			List<Protocol.PresencaState> semTeto;
			try { semTeto = SentidosDe(eu).Lista; }
			finally { Scouter.SemTetoDeTeste = false; }
			AfirmarSn("   CONTRA-EXEMPLO (o scouter sem teto, o de antes): o Acima vem com o numero -- a regua de cima reprovaria",
					  Acha(semTeto, acima) is { } ps && !double.IsNaN(ps.Bp), Nomes(semTeto));

			// ---------------------------------------------------------------- b. o ki divino fora do Scan
			AfirmarSn("**O KI DIVINO NAO APARECE NO SCAN** (*\"scouter NUNCA consegue detectar ou sentir ki divino\"*): nem quem esta em SSG, nem o "
					  + "Golden Apple, nem o Anjo de cargo, nem o Supremo Kaioh NPC", Divinos(l, presentes: false), Nomes(l));
			Jandirus.Core.Forms.DespertarDivino.Despertar(eu.Ficha);
			List<Protocol.PresencaState> deDeus = SentidosDe(eu).Lista;
			AfirmarSn("...nem no aparelho de quem TEM ki divino (*\"o usuario n influencia no q o scouter sente\"*)", Divinos(deDeus, presentes: false),
					  Nomes(deDeus));
			KiDivinoAparecerDeTeste = true;
			List<Protocol.PresencaState> comDivino;
			try { comDivino = SentidosDe(eu).Lista; }
			finally { KiDivinoAparecerDeTeste = false; }
			AfirmarSn("   CONTRA-EXEMPLO (o ki divino visivel, o de antes): os quatro aparecem no Scan -- a regua de cima reprovaria",
					  Divinos(comDivino, presentes: true), Nomes(comDivino));

			// ---------------------------------------------------------------- c. a ficha de quem tem o scouter
			SheetState fEu = FichaVisivel(eu);
			AfirmarSn("a ficha do dono do scouter: o proprio poder dentro do teto chega em numero (ele despertou, mas esta na forma base: o ki "
					  + "divino nao esta em USO)", !double.IsNaN(fEu.BP) && !double.IsNaN(fEu.ExpressedBP), $"{fEu.BP} / {fEu.ExpressedBP}");
			acima.PoderesConcedidos |= Protocol.Poder.Scouter;
			AplicarPoderes(acima);
			SheetState fAcima = FichaVisivel(acima);
			AfirmarSn("**...e acima do teto chega ???** (o HUD e a aba Stats escrevem ???): o scouter do Acima nao le o proprio poder",
					  double.IsNaN(fAcima.ExpressedBP) && double.IsNaN(fAcima.BP), $"{fAcima.BP} / {fAcima.ExpressedBP}");
			dourado.PoderesConcedidos |= Protocol.Poder.Scouter;
			AplicarPoderes(dourado);
			SheetState fDourado = FichaVisivel(dourado);
			AfirmarSn("**...e o proprio ki divino tambem**: o Golden Apple de scouter ligado le ??? (o aparelho nao sente o divino, nem o do dono)",
					  double.IsNaN(fDourado.BP) && double.IsNaN(fDourado.ExpressedBP), $"{fDourado.BP} / {fDourado.ExpressedBP}");

			// ---------------------------------------------------------------- d. as frases
			AfirmarSn("as FRASES com numero perguntam o mesmo (`ScouterMostra`): 90 milhoes sim, 150 milhoes nao, o poder de um ki divino nao, e "
					  + "sem scouter nunca",
					  ScouterMostra(eu, eu, 90_000_000) && !ScouterMostra(eu, eu, 150_000_000) && !ScouterMostra(eu, dourado, 1_000)
					  && !ScouterMostra(abaixo, abaixo, 1_000), "");

			// ---------------------------------------------------------------- e. o Sense, a telepatia e o Instant Transmission
			ServerPlayer mortal = StCorpo("Mortal", Em(c, 0, 3), 20_000, sense: true);
			ServerPlayer divino = StCorpo("Divino", Em(c, 0, 4), 20_000, sense: true);
			Jandirus.Core.Forms.DespertarDivino.Despertar(divino.Ficha);
			List<Protocol.PresencaState> lm = SentidosDe(mortal).Lista;
			AfirmarSn("**O SENSE DE UM MORTAL NAO SENTE O KI DIVINO** (*\"somente pessoas q tb tem ki divino podem dar sense em outras pessoas com "
					  + "ki divino\"*): o Abaixo aparece, os quatro divinos nao", Acha(lm, abaixo) != null && Divinos(lm, presentes: false), Nomes(lm));
			List<Protocol.PresencaState> ld = SentidosDe(divino).Lista;
			AfirmarSn("**...e o de quem TEM ki divino (despertado) sente os quatro**", Divinos(ld, presentes: true), Nomes(ld));
			KiDivinoAparecerDeTeste = true;
			List<Protocol.PresencaState> lmAntes;
			try { lmAntes = SentidosDe(mortal).Lista; }
			finally { KiDivinoAparecerDeTeste = false; }
			AfirmarSn("   CONTRA-EXEMPLO (o de antes): o mortal sente os divinos -- a regua de cima reprovaria", Divinos(lmAntes, presentes: true),
					  Nomes(lmAntes));
			AfirmarSn("a TELEPATIA e o Observe procuram a mesma energia (`AchoAEnergia`): o mortal nao acha o Dourado, o divino acha, e o Abaixo e "
					  + "achado pelos dois", !AchoAEnergia(mortal, dourado) && AchoAEnergia(divino, dourado) && AchoAEnergia(mortal, abaixo)
											   && AchoAEnergia(divino, abaixo), "");
			mortal.Ficha.teleskill = 100;
			divino.Ficha.teleskill = 100;
			KiDivinoAparecerDeTeste = true;
			bool itAntes;
			try { itAntes = ListaDeAssinaturasG11(mortal).Any(x => x.Quem == dourado); }
			finally { KiDivinoAparecerDeTeste = false; }
			bool itMortal = ListaDeAssinaturasG11(mortal).Any(x => x.Quem == dourado);
			bool itDivino = ListaDeAssinaturasG11(divino).Any(x => x.Quem == dourado);
			AfirmarSn("o INSTANT TRANSMISSION trava na mesma regra: o Dourado (alcancavel pela conta do DM -- o defeito injetado o poe na lista) some "
					  + "da lista do mortal e fica na do divino", itAntes && !itMortal && itDivino,
					  $"com o defeito {itAntes}; mortal {itMortal}; divino {itDivino}");
		}
		finally
		{
			Scouter.SemTetoDeTeste = false;
			KiDivinoAparecerDeTeste = false;
			if (anjoAntes != null) _tronos[Jandirus.Core.Npc.BillsEWhis.CargoDoWhis] = anjoAntes;
			else _tronos.Remove(Jandirus.Core.Npc.BillsEWhis.CargoDoWhis);
		}
	}
}
