using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A FAMILIA 6 DA `--sentidosteste` (2026-10-19): O ANDROIDE NAO TEM KI -- NEM NO SENSE, NEM NO SCOUTER. O dono: *"androides nem deveriam
/// aparecer no sense ou scouter por serem androides (faça isso pra player androide tb, ambos os tipos de androide)"*. Tres androides -- o
/// jogador de ABSORCAO e o de ENERGIA INFINITA (os dois que o Android Lab faz: a raca "Android" e o bit do tipo) e o ANDROIDE 17 NPC (o molde
/// da saga do Cell, que nasce "Human") -- e um humano de controle, pelas funcoes de producao (`SentidosDe`, `ScouterMostra`, `FichaVisivel`,
/// `AchoAEnergia`, a lista do Instant Transmission):
///   a. o Scan nao lista nenhum dos tres, e lista o humano com o BP; contra-exemplo `AndroideAparecerDeTeste` (os tres aparecem);
///   b. a frase com o numero do poder de um androide sai sem numero (`ScouterMostra`), e a do humano com; contra-exemplo idem;
///   c. o PROPRIO androide de scouter ligado le o proprio poder (a ficha e a frase sobre si): a leitura de si nao foi pedida;
///   d. o Sense nao sente nenhum dos tres, e sente o humano; o contra-exemplo poe o 17 NPC de volta (o jogador androide ja ficava de fora
///      pela raca, literal do DM -- `HtmlUI.dm:369`);
///   e. a telepatia e o Observe (`AchoAEnergia`) nao acham nenhum dos tres; o Instant Transmission nao trava em nenhum dos dois jogadores
///      androides (NPC nunca entrou na lista dele) -- contra-exemplo idem.
/// </summary>
public partial class GameServer
{
	private void OsAndroidesSemKi()
	{
		GD.Print("[sentidos] -- 6) O ANDROIDE NAO TEM KI: NEM NO SENSE, NEM NO SCOUTER (dono, 2026-10-19)");
		Vec2 c = CorredorLivre(28);
		ServerPlayer olho = StCorpo("Olho6", c, 20_000);
		olho.PoderesConcedidos |= Protocol.Poder.Scouter;
		AplicarPoderes(olho);
		ServerPlayer vidente = StCorpo("Vidente6", Em(c, 0, 2), 20_000, sense: true);
		ServerPlayer humano = StCorpo("Humano6", Em(c, 3, 0), 30_000);
		ServerPlayer absorcao = StCorpo("Absorcao6", Em(c, 4, 0), 30_000);
		absorcao.Race = "Android";
		absorcao.Ficha.AndroideAbsorcao = true;
		ServerPlayer infinito = StCorpo("Infinito6", Em(c, 5, 0), 30_000);
		infinito.Race = "Android";
		infinito.Ficha.AndroideInfinito = true;
		ServerPlayer? dezessete = null;
		if (_moldes?.Get("androide_17") is { } m17)
		{
			dezessete = StCorpo("Dezessete6", Em(c, 6, 0), 30_000);
			dezessete.Papel = new PapelDeNpc(m17, 6);
			dezessete.Conta = "";
		}
		bool Nenhum(List<Protocol.PresencaState> l) =>
			Acha(l, absorcao) == null && Acha(l, infinito) == null && (dezessete == null || Acha(l, dezessete) == null);
		bool Todos(List<Protocol.PresencaState> l) =>
			Acha(l, absorcao) != null && Acha(l, infinito) != null && (dezessete == null || Acha(l, dezessete) != null);

		try
		{
			(bool scan, List<Protocol.PresencaState> l) = SentidosDe(olho);
			AfirmarSn("preparo: o Olho le pelo scouter; os dois jogadores androides tem a raca Android e o bit do tipo; o 17 NPC existe no catalogo "
					  + "e nasce Human (a raca nao o acha)",
					  scan && dezessete != null && absorcao.Ficha.AndroideAbsorcao && infinito.Ficha.AndroideInfinito && dezessete.Race != "Android",
					  $"scan {scan}; 17 {dezessete?.Race ?? "sem molde"}");
			if (dezessete == null) return;

			// ---------------------------------------------------------------- a. o Scan
			AfirmarSn("**O ANDROIDE NAO APARECE NO SCAN** (*\"androides nem deveriam aparecer no sense ou scouter\"*): nem o de absorcao, nem o de "
					  + "energia infinita, nem o 17 NPC -- e o Humano aparece, com o BP", Acha(l, humano) is { } ph && !double.IsNaN(ph.Bp) && Nenhum(l), Nomes(l));
			AndroideAparecerDeTeste = true;
			List<Protocol.PresencaState> scanAntes;
			try { scanAntes = SentidosDe(olho).Lista; }
			finally { AndroideAparecerDeTeste = false; }
			AfirmarSn("   CONTRA-EXEMPLO (o scouter de antes): os tres aparecem no Scan, com o BP -- a regua de cima reprovaria",
					  Todos(scanAntes) && Acha(scanAntes, absorcao) is { } pa && !double.IsNaN(pa.Bp), Nomes(scanAntes));

			// ---------------------------------------------------------------- b. as frases
			AfirmarSn("**AS FRASES COM NUMERO PERGUNTAM O MESMO** (`ScouterMostra`): o poder do Humano sai em numero, o de cada androide nao",
					  ScouterMostra(olho, humano, 1_000) && !ScouterMostra(olho, absorcao, 1_000) && !ScouterMostra(olho, infinito, 1_000)
					  && !ScouterMostra(olho, dezessete, 1_000), "");
			AndroideAparecerDeTeste = true;
			bool frasesAntes;
			try { frasesAntes = ScouterMostra(olho, absorcao, 1_000) && ScouterMostra(olho, infinito, 1_000) && ScouterMostra(olho, dezessete, 1_000); }
			finally { AndroideAparecerDeTeste = false; }
			AfirmarSn("   CONTRA-EXEMPLO (o de antes): as tres frases saem com o numero -- a regua de cima reprovaria", frasesAntes, "");

			// ---------------------------------------------------------------- c. a leitura de si
			absorcao.PoderesConcedidos |= Protocol.Poder.Scouter;
			AplicarPoderes(absorcao);
			SheetState fa = FichaVisivel(absorcao);
			AfirmarSn("...e o PROPRIO androide de scouter ligado le o proprio poder (a ficha e a frase sobre si): o dono pediu que ele nao aparecesse "
					  + "pros outros", !double.IsNaN(fa.BP) && !double.IsNaN(fa.ExpressedBP) && ScouterMostra(absorcao, absorcao, 1_000),
					  $"{fa.BP} / {fa.ExpressedBP}");

			// ---------------------------------------------------------------- d. o Sense
			List<Protocol.PresencaState> ls = SentidosDe(vidente).Lista;
			AfirmarSn("**O SENSE NAO SENTE ANDROIDE NENHUM**: nem os dois jogadores (a raca, como no DM) nem o 17 NPC -- e o Humano e sentido",
					  Acha(ls, humano) != null && Nenhum(ls), Nomes(ls));
			AndroideAparecerDeTeste = true;
			List<Protocol.PresencaState> lsAntes;
			try { lsAntes = SentidosDe(vidente).Lista; }
			finally { AndroideAparecerDeTeste = false; }
			AfirmarSn("   CONTRA-EXEMPLO (o de antes): o 17 NPC, que nasce Human pelo molde, e sentido -- a regua de cima reprovaria",
					  Acha(lsAntes, dezessete) != null, Nomes(lsAntes));

			// ---------------------------------------------------------------- e. a telepatia, o Observe e o Instant Transmission
			AfirmarSn("a TELEPATIA e o Observe procuram a mesma energia (`AchoAEnergia`): o Humano e achado, nenhum dos tres androides",
					  AchoAEnergia(vidente, humano) && !AchoAEnergia(vidente, absorcao) && !AchoAEnergia(vidente, infinito)
					  && !AchoAEnergia(vidente, dezessete), "");
			vidente.Ficha.teleskill = 100;
			AndroideAparecerDeTeste = true;
			bool itAntes;
			try
			{
				var antes = ListaDeAssinaturasG11(vidente);
				itAntes = antes.Any(x => x.Quem == absorcao) && antes.Any(x => x.Quem == infinito);
			}
			finally { AndroideAparecerDeTeste = false; }
			var it = ListaDeAssinaturasG11(vidente);
			AfirmarSn("o INSTANT TRANSMISSION nao trava em androide: os dois jogadores androides (alcancaveis pela conta do DM -- o defeito injetado "
					  + "os poe na lista) somem dela, e o Humano fica", itAntes && it.Any(x => x.Quem == humano)
					  && !it.Any(x => x.Quem == absorcao || x.Quem == infinito),
					  $"com o defeito {itAntes}; lista {string.Join(", ", it.Select(x => x.Quem.Name))}");
		}
		finally
		{
			AndroideAparecerDeTeste = false;
		}
	}
}
