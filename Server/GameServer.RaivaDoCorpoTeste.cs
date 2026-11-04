using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A FAMILIA 13 DA `--raivateste` (2026-10-18): A FURIA AO ACHAR O CORPO DO AMIGO. O dono: *"faça com q vc n precise apenas ver seu amigo
/// morrer na hora, caso seu amigo morra pra outra pessoa (ao qual n seja seu amigo) e vc ache o corpo morto dele pela primeira vez, seu
/// personagem vai entrar em furia e vai falar algo relacionando tipo chamar pelo nome algumas vezes e dps disso comeca a cinematica de furia
/// podendo começar a cinematica de transformação caso a furia desperte a transformação"*.
///
/// Pelo funil de producao da derrota (`AoPerderALuta`), pelo `DeixarOCadaver` de producao e pelo pulso de producao do corpo achado
/// (`PulsoDoCorpoAchado`), com o relogio adiantado (`AdiantoDoRelogioDeTeste`) e o host posto ao lado do corpo:
///   a. o amigo morre LONGE do host: nada acende na hora, e a morte deixa o registro no corpo;
///   b. o host chega ao corpo: chama o nome (os chamados, um a cada intervalo) e so DEPOIS a furia -- a cena e o grito dela com o nome;
///      contra-exemplos `SoVerAMorteEnfureceDeTeste` (achar nao faz nada) e `FuriaSemChamarONomeDeTeste` (a furia sem chamar o nome);
///   c. pela primeira vez: o mesmo corpo de novo nao enfurece, e quem VIU a morte nao o "acha" depois; contra-exemplo
///      `AchaOMesmoCorpoDeNovoDeTeste`;
///   d. o algoz amigo do host: achar o corpo nao enfurece -- a mesma regra da raiva; contra-exemplo `RaivaSoPelaVitimaDeTeste`;
///   e. o CADAVER que fica depois da viagem leva o registro; contra-exemplo `CadaverSemOLutoDeTeste`;
///   f. o amigo que ja voltou a vida: o corpo velho dele nao enfurece; contra-exemplo `AmigoVivoAindaEChoradoDeTeste`;
///   h. (2026-10-30, o pedido 192) SO NA TELA: a 10 tiles pro lado ou 5 pra cima (dentro dos 22 da fala, fora da tela) nao acha, e o corpo
///      continua nunca visto; ao entrar na tela, chama o nome e se enfurece -- contraprova pelo binario do agente `corpoavista`;
///   i. (2026-10-30, o pedido 192) DA MESMA CONEXAO NADA RENDE (o pedido 110): o corpo da amiga do mesmo IP do host, e o do amigo que um
///      personagem desse IP matou, nao enfurecem;
///   j. (2026-10-31, o pedido 193) A MORTE SO SE VE NA TELA: a amiga que morre a 10 tiles pro lado ou 5 pra cima do host (dentro dos 22 da
///      fala, fora da tela) nao o enfurece na hora, e o corpo dela continua nunca visto por ele -- ao entrar na tela, ele o acha; contra-exemplo
///      `MorteVistaNosVinteEDoisDeTeste`;
///   g. a furia que desperta uma forma NOVA: depois dos chamados a cena e a da TRANSFORMACAO (a estreia), e a da furia nao toca.
/// </summary>
public partial class GameServer
{
	private void AFuriaAoAcharOCorpo(ServerPlayer pl, Action<string, bool, string> checa, List<ServerPlayer> forjados)
	{
		GD.Print("[raiva] -- 13) a furia ao ACHAR o corpo do amigo: o nome chamado, e so depois a cena (dono, 2026-10-18) --");
		const float T = ZoneCollision.TileSize;
		Vec2 casa = pl.Pos;
		long relogioDeFora = AdiantoDoRelogioDeTeste;
		var falas = new List<(int Quem, Protocol.Fala Canal, string Texto)>();
		List<(int Quem, Protocol.Fala Canal, string Texto)>? falasDeFora = EscutaDeFalas;
		EscutaDeFalas = falas;

		List<string> DoHost() => [.. falas.Where(x => x.Quem == pl.Id && x.Canal == Protocol.Fala.Diz).Select(x => x.Texto)];
		int CenasDoHost() => EscutaDeFurias?.Count(id => id == pl.Id) ?? 0;
		NivelDeRaiva Nivel() => NivelDaRaivaDe(pl);
		void EmPaz()
		{
			pl.FuriaExtremaAte = 0;
			pl.RaivaComumAte = 0;
			pl.FuriaCenaAte = 0;
			ProjetarRaiva(pl);
		}
		// A MESA LIMPA entre os casos: nenhum registro nem chamado de um caso vaza pro seguinte, e o host volta pra casa calmo e calado.
		void DoZero()
		{
			_mortesComCorpo.Clear();
			_chamando.Clear();
			_gritosQueFaltam.Remove(pl.Id);
			pl.Pos = casa;
			EmPaz();
			falas.Clear();
			_ultimaFala.Remove(pl.Id);
		}
		ServerPlayer Pessoa(int i, string nome, float tiles)
		{
			ServerPlayer p = ForjarPessoaDaRaiva(pl, i, nome, tiles);
			forjados.Add(p);
			return p;
		}
		// O CADAVER, pelo `DeixarOCadaver` de producao -- o que a viagem do jogador faz no fim dos 2 s. A bancada desfaz no fim os que ergueu.
		var cadaveres = new List<ServerPlayer>();
		ServerPlayer Cadaver(ServerPlayer morto)
		{
			ServerPlayer c = DeixarOCadaver(morto);
			cadaveres.Add(c);
			return c;
		}
		// O GOLPE QUE MATA, como o `ResolverDesfecho` o resolve: o corpo cai, morre, e o funil da derrota roda com `morreu`.
		void Matar(ServerPlayer vitima, ServerPlayer algoz)
		{
			vitima.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
			_ = vitima.Combate.Morrer();
			AoPerderALuta(vitima, algoz, morreu: true);
		}
		// O AMIGO DECLARADO morre LONGE (40 tiles; a vista e 22), pelas maos de quem pra ele e o host e um estranho.
		ServerPlayer MorteLonge(int i, string nome, ServerPlayer algoz)
		{
			ServerPlayer amigo = Pessoa(i, nome, 40);
			DeclararDeTeste(pl, amigo, Relacao.Bom);
			Matar(amigo, algoz);
			return amigo;
		}
		// O PULSO DE PRODUCAO, com o relogio do servidor adiantado: os chamados e a furia saem no tempo deles.
		void Pulso(double segundos)
		{
			AdiantoDoRelogioDeTeste += (long)(segundos * 1000);
			PulsoDoCorpoAchado();
		}
		// O host CHEGA ao corpo (tres tiles a oeste dele) e o tempo passa ate os chamados acabarem.
		void Achar(ServerPlayer corpo)
		{
			pl.Pos = corpo.Pos - new Vec2(3 * T, 0);
			Pulso(0.1);
			for (int i = 0; i < 12 && _chamando.Exists(c => c.Quem == pl); i++) Pulso(RaivaEmDegraus.SegundosEntreChamados);
		}

		string[] chamados = RaivaEmDegraus.ChamadosAoAcharOCorpo("Kuririn");
		ServerPlayer freeza = Pessoa(30, "bancada: o Freeza", 42);
		checa("(montagem) o algoz e um estranho pro host e um inimigo pra vitima (ninguem o declarou)",
			  !pl.Social.Conhece(freeza.Assinatura), "");
		try
		{
			// ---------------------------------------------------------------- a. longe, nada; o registro fica no corpo
			DoZero();
			int cenasAntes = CenasDoHost();
			ServerPlayer kuririn = MorteLonge(31, "Kuririn", freeza);
			checa("(montagem) o amigo morreu a 40 tiles do host -- fora da vista (22)", kuririn.Ficha.dead
				  && (kuririn.Pos - pl.Pos).LengthSquared > RaioDeTestemunha * RaioDeTestemunha, $"{(kuririn.Pos - pl.Pos).Length / T:0} tiles");
			checa("**LONGE, NADA NA HORA**: o host nao viu -- nem raiva, nem cena, nem fala (o caminho de sempre e so de quem VE)",
				  Nivel() == NivelDeRaiva.Nenhuma && CenasDoHost() == cenasAntes && DoHost().Count == 0, $"{Nivel()}");
			MorteComCorpo? registro = _mortesComCorpo.Find(m => m.Corpo == kuririn);
			checa("...e a morte deixou o REGISTRO no corpo: o nome dele, quem o matou, e ninguem que o tenha achado ainda",
				  registro is { NomeDaVitima: "Kuririn" } && registro.Algoz == freeza.Assinatura && !registro.JaAcharam.Contains(pl.Assinatura),
				  registro == null ? "sem registro" : $"{registro.NomeDaVitima}, {registro.JaAcharam.Count} ja acharam");
			Pulso(1.0);
			checa("...e de longe o pulso nao acha nada", _chamando.Count == 0 && DoHost().Count == 0, string.Join(" | ", DoHost()));

			// ---------------------------------------------------------------- b. o host chega: o nome, e so depois a furia
			pl.Pos = kuririn.Pos - new Vec2(3 * T, 0);
			Pulso(0.1);
			List<string> primeiro = DoHost();
			checa("**ACHOU O CORPO: CHAMA O NOME** -- o primeiro chamado sai na hora, e a furia AINDA nao veio",
				  primeiro.Count == 1 && primeiro[0] == chamados[0] && Nivel() == NivelDeRaiva.Nenhuma && CenasDoHost() == cenasAntes,
				  $"{string.Join(" | ", primeiro)}; {Nivel()}");
			for (int i = 1; i < chamados.Length; i++) Pulso(RaivaEmDegraus.SegundosEntreChamados);
			checa($"...os {chamados.Length} chamados, um a cada {RaivaEmDegraus.SegundosEntreChamados:0.0} s -- e ainda sem furia",
				  DoHost().SequenceEqual(chamados) && Nivel() == NivelDeRaiva.Nenhuma && CenasDoHost() == cenasAntes,
				  $"{string.Join(" | ", DoHost())}; {Nivel()}");
			Pulso(RaivaEmDegraus.SegundosEntreChamados);
			List<string> tudo = DoHost();
			checa("**...E SO DEPOIS A FURIA**: o degrau extremo acende e a CENA da furia toca -- depois do ultimo chamado",
				  Nivel() == NivelDeRaiva.Extrema && CenasDoHost() == cenasAntes + 1, $"{Nivel()}; {CenasDoHost() - cenasAntes} cena(s)");
			checa("...e o grito da furia, com o nome (\"KURIRIN NAO!\"), vem depois dos chamados",
				  tudo.Count == chamados.Length + 1 && RaivaEmDegraus.FormasDoNome(NivelDeRaiva.Extrema, "Kuririn").Contains(tudo[^1]),
				  string.Join(" | ", tudo));

			// contra-exemplo: so ver a morte enfurece (o jogo de antes)
			DoZero();
			ServerPlayer yamcha = MorteLonge(32, "Yamcha", freeza);
			SoVerAMorteEnfureceDeTeste = true;
			try { Achar(yamcha); }
			finally { SoVerAMorteEnfureceDeTeste = false; }
			checa("   DEFEITO INJETADO (so ver a morte enfurece, o jogo de antes): o host chega ao corpo e nada acontece -- a regua de cima reprovaria",
				  DoHost().Count == 0 && Nivel() == NivelDeRaiva.Nenhuma, $"{string.Join(" | ", DoHost())}; {Nivel()}");

			// contra-exemplo: a furia sem chamar o nome
			DoZero();
			ServerPlayer tenshin = MorteLonge(33, "Tenshinhan", freeza);
			FuriaSemChamarONomeDeTeste = true;
			try
			{
				pl.Pos = tenshin.Pos - new Vec2(3 * T, 0);
				Pulso(0.1);
			}
			finally { FuriaSemChamarONomeDeTeste = false; }
			string[] chamadosDele = RaivaEmDegraus.ChamadosAoAcharOCorpo("Tenshinhan");
			checa("   DEFEITO INJETADO (a furia sem chamar o nome): ao achar, a furia ja irrompe e nenhum chamado sai -- a regua da ordem reprovaria",
				  Nivel() == NivelDeRaiva.Extrema && !DoHost().Any(chamadosDele.Contains), $"{string.Join(" | ", DoHost())}; {Nivel()}");

			// ---------------------------------------------------------------- c. pela primeira vez
			DoZero();
			ServerPlayer chaozu = MorteLonge(34, "Chaozu", freeza);
			Achar(chaozu);
			bool achouAPrimeira = Nivel() == NivelDeRaiva.Extrema;
			EmPaz();
			falas.Clear();
			pl.Pos = casa;
			Pulso(1.0);
			Achar(chaozu);
			checa("**SO NA PRIMEIRA VEZ**: calmo de novo, o host se afasta, volta ao mesmo corpo -- e nada, nem chamado nem furia (uma vez por morte, por quem acha)",
				  achouAPrimeira && DoHost().Count == 0 && Nivel() == NivelDeRaiva.Nenhuma, $"a primeira {achouAPrimeira}; agora {string.Join(" | ", DoHost())}; {Nivel()}");

			DoZero();
			ServerPlayer launch = Pessoa(35, "Launch", 3);
			DeclararDeTeste(pl, launch, Relacao.Bom);
			Matar(launch, freeza);
			bool viuAMorte = Nivel() == NivelDeRaiva.Extrema;
			EmPaz();
			falas.Clear();
			Achar(launch);
			checa("**QUEM VIU A MORTE NAO 'ACHA' O CORPO**: o host viu a amiga morrer a 3 tiles (a furia de sempre, na hora); calmo, ao lado do corpo, ele nao chama nem se enfurece de novo",
				  viuAMorte && DoHost().Count == 0 && Nivel() == NivelDeRaiva.Nenhuma, $"viu {viuAMorte}; agora {string.Join(" | ", DoHost())}; {Nivel()}");
			AchaOMesmoCorpoDeNovoDeTeste = true;
			try { EmPaz(); falas.Clear(); Achar(launch); }
			finally { AchaOMesmoCorpoDeNovoDeTeste = false; }
			checa("   DEFEITO INJETADO (o mesmo corpo de novo): ao lado do corpo que ja viu, o host volta a chamar e a se enfurecer -- as duas reguas de cima reprovariam",
				  DoHost().Count > 0 && Nivel() == NivelDeRaiva.Extrema, $"{string.Join(" | ", DoHost())}; {Nivel()}");

			// ---------------------------------------------------------------- d. o algoz amigo do host
			DoZero();
			ServerPlayer goku = Pessoa(36, "bancada: o outro amigo", 41);
			DeclararDeTeste(pl, goku, Relacao.Bom);
			ServerPlayer piccolo = MorteLonge(37, "Piccolo", goku);
			Achar(piccolo);
			checa("**O AMIGO QUE MATA O AMIGO NAO ENFURECE** (*\"pra outra pessoa (ao qual n seja seu amigo)\"*): o corpo de quem o outro amigo do host matou -- nem chamado nem furia",
				  DoHost().Count == 0 && Nivel() == NivelDeRaiva.Nenhuma, $"{string.Join(" | ", DoHost())}; {Nivel()}");
			DoZero();
			ServerPlayer gohan = MorteLonge(38, "Gohan", goku);
			RaivaSoPelaVitimaDeTeste = true;
			try { Achar(gohan); }
			finally { RaivaSoPelaVitimaDeTeste = false; }
			checa("   DEFEITO INJETADO (so a lista da vitima, o jogo de antes): o host chama o nome e se enfurece -- a regua de cima reprovaria",
				  DoHost().Count > 0 && Nivel() == NivelDeRaiva.Extrema, $"{string.Join(" | ", DoHost())}; {Nivel()}");

			// ---------------------------------------------------------------- e. o cadaver
			// A VIAGEM, como a do jogador no fim dos 2 s: o `DeixarOCadaver` de producao ergue o corpo que fica, e o morto sai do mundo dos vivos.
			DoZero();
			ServerPlayer yajirobe = MorteLonge(39, "Yajirobe", freeza);
			ServerPlayer cadaver = Cadaver(yajirobe);
			RemoverNpc(yajirobe);
			checa("**O CADAVER LEVA O REGISTRO**: o morto viajou, e a morte agora esta presa ao corpo que ficou no chao",
				  _mortesComCorpo.Exists(m => m.Corpo == cadaver), $"{_mortesComCorpo.Count} registro(s)");
			Achar(cadaver);
			checa("...e quem acha o CADAVER chama o nome e se enfurece, como no corpo dos 2 s",
				  DoHost().Take(3).SequenceEqual(RaivaEmDegraus.ChamadosAoAcharOCorpo("Yajirobe")) && Nivel() == NivelDeRaiva.Extrema,
				  $"{string.Join(" | ", DoHost())}; {Nivel()}");

			DoZero();
			ServerPlayer oolong = MorteLonge(40, "Oolong", freeza);
			ServerPlayer cadaver2;
			CadaverSemOLutoDeTeste = true;
			try { cadaver2 = Cadaver(oolong); }
			finally { CadaverSemOLutoDeTeste = false; }
			RemoverNpc(oolong);
			Achar(cadaver2);
			checa("   DEFEITO INJETADO (o cadaver sem o registro): o corpo que fica nao lembra de nada -- nem chamado nem furia, e as duas reguas de cima reprovariam",
				  DoHost().Count == 0 && Nivel() == NivelDeRaiva.Nenhuma, $"{string.Join(" | ", DoHost())}; {Nivel()}");

			// ---------------------------------------------------------------- f. o amigo que voltou
			DoZero();
			ServerPlayer bulma = MorteLonge(41, "Bulma", freeza);
			ServerPlayer cadaver3 = Cadaver(bulma);
			bulma.Ficha.dead = false;   // as esferas o trouxeram de volta (o corpo velho continua no chao)
			Achar(cadaver3);
			checa("**O AMIGO QUE JA VOLTOU NAO E CHORADO**: com ele vivo no mundo, o corpo velho nao enfurece ninguem",
				  DoHost().Count == 0 && Nivel() == NivelDeRaiva.Nenhuma && !_mortesComCorpo.Exists(m => m.Corpo == cadaver3),
				  $"{string.Join(" | ", DoHost())}; {Nivel()}");
			DoZero();
			ServerPlayer mrPopo = MorteLonge(42, "Popo", freeza);
			ServerPlayer cadaver4 = Cadaver(mrPopo);
			mrPopo.Ficha.dead = false;
			AmigoVivoAindaEChoradoDeTeste = true;
			try { Achar(cadaver4); }
			finally { AmigoVivoAindaEChoradoDeTeste = false; }
			checa("   DEFEITO INJETADO (o registro nao cai com a volta do amigo): o corpo velho de quem esta vivo enfurece -- a regua de cima reprovaria",
				  DoHost().Count > 0 && Nivel() == NivelDeRaiva.Extrema, $"{string.Join(" | ", DoHost())}; {Nivel()}");

			// ---------------------------------------------------------------- h. (o pedido 192) so na TELA
			// O dono: *"esse corpo nao tenha entrado no seu campo de visao antes, ao entrar seu personagem vai gritar o nome"*. A tela do zoom de
			// fabrica tem 6,67 tiles pra cada lado e 3,75 pra cima e pra baixo; a fala (o raio de antes) tem 22.
			DoZero();
			ServerPlayer uub = MorteLonge(44, "Uub", freeza);
			MorteComCorpo? doUub = _mortesComCorpo.Find(m => m.Corpo == uub);
			bool NuncaVisto() => DoHost().Count == 0 && _chamando.Count == 0 && doUub != null && !doUub.JaAcharam.Contains(pl.Assinatura);
			pl.Pos = uub.Pos - new Vec2(10 * T, 0);
			Pulso(1.0);
			bool deLado = NuncaVisto();
			pl.Pos = uub.Pos - new Vec2(0, 5 * T);
			Pulso(1.0);
			bool deCima = NuncaVisto();
			checa("(o pedido 192) **SO NA TELA**: a 10 tiles do corpo (dentro dos 22 da fala, fora da tela) o host nao acha -- nem chamado, e o corpo continua NUNCA VISTO por ele",
				  deLado, $"{string.Join(" | ", DoHost())}; ja acharam: {doUub?.JaAcharam.Count}");
			checa("(o pedido 192) **...nem a 5 tiles acima dele** (a tela tem 3,75 tiles de meia altura no zoom de fabrica)",
				  deCima, $"{string.Join(" | ", DoHost())}; ja acharam: {doUub?.JaAcharam.Count}");
			Achar(uub);
			checa("(o pedido 192) ...e quando o corpo ENTRA na tela (a 3 tiles) o host chama o nome e se enfurece",
				  DoHost().Take(3).SequenceEqual(RaivaEmDegraus.ChamadosAoAcharOCorpo("Uub")) && Nivel() == NivelDeRaiva.Extrema,
				  $"{string.Join(" | ", DoHost())}; {Nivel()}");

			// ---------------------------------------------------------------- i. (o pedido 192) da mesma conexao nada rende (o pedido 110)
			// O endereco de mentira da bancada (`IpDeTeste`) nos corpos forjados, e a regra ligada como na `--mesmoipteste`.
			bool mesmoIpDeFora = MesmoIpNaBancadaDeTeste;
			string? ipReal = IpDe(pl);
			string ipDoHost = ipReal ?? "10.0.0.192";
			var comIp = new List<int>();
			void DarIp(ServerPlayer p) { IpDeTeste[p.Id] = ipDoHost; comIp.Add(p.Id); }
			if (ipReal == null) DarIp(pl);
			MesmoIpNaBancadaDeTeste = true;
			try
			{
				DoZero();
				ServerPlayer marron = Pessoa(45, "Marron", 40);
				DarIp(marron);
				DeclararDeTeste(pl, marron, Relacao.Bom);
				Matar(marron, freeza);
				Achar(marron);
				checa("(o pedido 192) **DA MESMA CONEXAO NADA RENDE** (o pedido 110): o corpo da amiga jogada do MESMO IP do host, morta por um estranho -- nem chamado nem furia",
					  DoHost().Count == 0 && Nivel() == NivelDeRaiva.Nenhuma, $"{string.Join(" | ", DoHost())}; {Nivel()}");

				DoZero();
				ServerPlayer capanga = Pessoa(46, "bancada: o capanga", 42);
				DarIp(capanga);
				ServerPlayer dende = MorteLonge(47, "Dende", capanga);
				Achar(dende);
				checa("(o pedido 192) **...nem o corpo que o personagem do mesmo IP matou**: o amigo do host morto por um corpo dessa conexao -- nem chamado nem furia",
					  DoHost().Count == 0 && Nivel() == NivelDeRaiva.Nenhuma, $"{string.Join(" | ", DoHost())}; {Nivel()}");
			}
			finally
			{
				MesmoIpNaBancadaDeTeste = mesmoIpDeFora;
				foreach (int id in comIp) IpDeTeste.Remove(id);
			}

			// ---------------------------------------------------------------- j. (o pedido 193) a MORTE so se ve na tela
			// O dono, na pergunta do 192 (ver a morte passa a contar so pela tela?): *"Sim so dentro do campo de visao"*. A amiga morre a 10
			// tiles do host -- dentro dos 22 da fala, fora da tela: ele nao viu, e o corpo fica pra ser achado por ele.
			DoZero();
			int cenasDaMorte = CenasDoHost();
			ServerPlayer videl = Pessoa(48, "Videl", 10);
			DeclararDeTeste(pl, videl, Relacao.Bom);
			Matar(videl, freeza);
			MorteComCorpo? daVidel = _mortesComCorpo.Find(m => m.Corpo == videl);
			checa("(montagem) a amiga morreu a 10 tiles do host -- dentro dos 22 da fala, fora da tela (6,67 tiles de meia largura)",
				  videl.Ficha.dead && (videl.Pos - pl.Pos).LengthSquared <= RaioDeTestemunha * RaioDeTestemunha && !NaVistaDe(pl, videl),
				  $"{(videl.Pos - pl.Pos).Length / T:0.#} tiles");
			checa("(o pedido 193) **A MORTE FORA DA TELA NAO E VISTA**: a amiga morre a 10 tiles e o host nao se enfurece na hora -- nem raiva, nem cena",
				  Nivel() == NivelDeRaiva.Nenhuma && CenasDoHost() == cenasDaMorte, $"{Nivel()}; {CenasDoHost() - cenasDaMorte} cena(s)");
			checa("(o pedido 193) **...E O CORPO DELA CONTINUA NUNCA VISTO POR ELE**: o registro da morte nao o anota como quem a viu",
				  daVidel != null && !daVidel.JaAcharam.Contains(pl.Assinatura), daVidel == null ? "sem registro" : $"{daVidel.JaAcharam.Count} ja acharam");
			falas.Clear();
			Achar(videl);
			checa("(o pedido 193) ...e quando o corpo ENTRA na tela o host o ACHA: chama o nome e se enfurece",
				  DoHost().Take(3).SequenceEqual(RaivaEmDegraus.ChamadosAoAcharOCorpo("Videl")) && Nivel() == NivelDeRaiva.Extrema,
				  $"{string.Join(" | ", DoHost())}; {Nivel()}");

			// 5 tiles ACIMA: dentro dos 22 da fala, fora da tela (3,75 tiles de meia altura no zoom de fabrica)
			DoZero();
			cenasDaMorte = CenasDoHost();
			ServerPlayer lime = Pessoa(49, "Lime", 0);
			lime.Pos = pl.Pos - new Vec2(0, 5 * T);
			DeclararDeTeste(pl, lime, Relacao.Bom);
			Matar(lime, freeza);
			checa("(o pedido 193) **...nem a amiga que morre 5 tiles ACIMA dele** (a tela tem 3,75 tiles de meia altura): nada na hora",
				  Nivel() == NivelDeRaiva.Nenhuma && CenasDoHost() == cenasDaMorte, $"{Nivel()}; {CenasDoHost() - cenasDaMorte} cena(s)");

			// contra-exemplo: a morte vista nos 22 tiles (o jogo de antes)
			DoZero();
			ServerPlayer erasa = Pessoa(50, "Erasa", 10);
			DeclararDeTeste(pl, erasa, Relacao.Bom);
			MorteVistaNosVinteEDoisDeTeste = true;
			try { Matar(erasa, freeza); }
			finally { MorteVistaNosVinteEDoisDeTeste = false; }
			MorteComCorpo? daErasa = _mortesComCorpo.Find(m => m.Corpo == erasa);
			checa("   DEFEITO INJETADO (a morte vista nos 22 tiles, o jogo de antes): a 10 tiles, fora da tela, o host se enfurece na hora e o corpo fica como visto -- as reguas de cima reprovariam",
				  Nivel() == NivelDeRaiva.Extrema && daErasa != null && daErasa.JaAcharam.Contains(pl.Assinatura), $"{Nivel()}");

			// ---------------------------------------------------------------- g. a furia que desperta a forma
			DoZero();
			EsquecerAsFormasDeTeste(pl.Id);
			DarPoderPraFormaDeTeste(pl.Id, "ssj1", "ssj2");
			ServerPlayer pan = MorteLonge(43, "Pan", freeza);
			var anuncios = new List<(int Quem, string De, string Para, DegrauDeCena Degrau)>();
			List<(int Quem, string De, string Para, DegrauDeCena Degrau)>? anunciosDeFora = EscutaDeAnuncios;
			int cenasDaFuria = CenasDoHost();
			EscutaDeAnuncios = anuncios;
			try
			{
				Achar(pan);
				TickDaForma(pl, 1 / 30.0);   // o despertar pela raiva e um estado: o tique da forma o executa (`DespertarPelaRaiva`)
			}
			finally { EscutaDeAnuncios = anunciosDeFora; }
			checa("**A FURIA QUE DESPERTA A FORMA**: depois dos chamados, o host vira Super Saiyajin SOZINHO pela estreia (com a cena dela) -- e a cena da furia nao toca",
				  DoHost().Take(3).SequenceEqual(RaivaEmDegraus.ChamadosAoAcharOCorpo("Pan"))
				  && anuncios.Any(a => a.Quem == pl.Id && a.Para == "ssj1" && a.Degrau == DegrauDeCena.Estreia) && CenasDoHost() == cenasDaFuria,
				  $"{string.Join(" | ", DoHost())}; anuncios: {string.Join(" ", anuncios.Where(a => a.Quem == pl.Id).Select(a => $"{a.De}->{a.Para}/{a.Degrau}"))}; "
				  + $"{CenasDoHost() - cenasDaFuria} cena(s) de furia");
			VoltarPraBaseDeTeste(pl.Id);
			EsquecerAsFormasDeTeste(pl.Id);
			pl.Ficha.BP = 1;
		}
		finally
		{
			SoVerAMorteEnfureceDeTeste = false;
			FuriaSemChamarONomeDeTeste = false;
			AchaOMesmoCorpoDeNovoDeTeste = false;
			CadaverSemOLutoDeTeste = false;
			AmigoVivoAindaEChoradoDeTeste = false;
			RaivaSoPelaVitimaDeTeste = false;
			MorteVistaNosVinteEDoisDeTeste = false;
			foreach (ServerPlayer c in cadaveres) DesfazerOCadaver(c);
			_mortesComCorpo.Clear();
			_chamando.Clear();
			_gritosQueFaltam.Remove(pl.Id);
			_ultimaFala.Remove(pl.Id);   // (a hora da ultima fala foi marcada com o relogio adiantado: deixada, calaria o host ate o relogio a alcancar)
			EscutaDeFalas = falasDeFora;
			AdiantoDoRelogioDeTeste = relogioDeFora;
			pl.Pos = casa;
			EmPaz();
		}
	}

	// =====================================================================
	// O PALCO DA `--diagraiva` PRO CORPO ACHADO (as fotos `raiva-10` a `raiva-13`)
	// =====================================================================

	/// <summary>
	/// SO PRA BANCADA DE JANELA: o host se afasta do palco (<paramref name="tiles"/> a oeste do amigo, fora da vista dele) e o amigo do palco
	/// MORRE nas maos do algoz -- de verdade (`Morrer`), pelo funil de producao da derrota. O corpo fica no chao: o amigo forjado nao e jogador,
	/// e nao viaja pro Outro Mundo. Devolve se ele morreu.
	/// </summary>
	internal bool MatarOAmigoLongeDeTeste(int hostId, float tiles)
	{
		if (_palcoDaRaiva.Count < 2 || !_players.TryGetValue(hostId, out ServerPlayer? host)) return false;
		ServerPlayer amigo = _palcoDaRaiva[0], algoz = _palcoDaRaiva[1];
		CravarPosicao(host, amigo.Pos - new Vec2(tiles * ZoneCollision.TileSize, 0));
		if (!amigo.Ficha.KO) amigo.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
		_ = amigo.Combate.Morrer();
		AoPerderALuta(amigo, algoz, morreu: true);
		return amigo.Ficha.dead;
	}

	/// <summary>SO PRA BANCADA DE JANELA: o host chega ao corpo do amigo do palco (tres tiles a oeste dele). Achar e chamar e o pulso de producao.</summary>
	internal bool LevarOHostAoCorpoDeTeste(int hostId)
	{
		if (_palcoDaRaiva.Count < 1 || !_players.TryGetValue(hostId, out ServerPlayer? host)) return false;
		CravarPosicao(host, _palcoDaRaiva[0].Pos - new Vec2(3 * ZoneCollision.TileSize, 0));
		return true;
	}
}
