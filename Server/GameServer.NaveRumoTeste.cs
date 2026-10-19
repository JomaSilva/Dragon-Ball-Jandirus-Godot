using System.Text.Json;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A CAMADA 7 DA `--naveteste` (2026-10-18): A NAVE GRANDE SEGUE SOZINHA. O dono: *"permita q na capital ship ao colocar pra ela ir pra um
/// planeta com nav system e o jogador sair do piloto, ela ainda continua, e avisa no chat quando chegar"* -- e *"o personagem fica voando
/// quando eu piloto a nave , ele deveria so ficar parado na capital ship"*. Pelas portas de producao (o embarque, o leme, o `TickDasNaves`):
///   a. ao leme da nave grande a pose que vai pros outros e a de PARADO, e nao a de voo; contra-exemplo `PilotoNaPoseDeVooDeTeste`;
///   b. largar o leme com o destino (o argumento do `nave_pilotar` -- o que o cliente manda com o nav system ligado): a nave, pousada, arma a
///      sequencia de lancamento e a tripulacao ouve pra onde ela vai; contra-exemplo `RumoSoNoClienteDeTeste` (ela fica parada, o jogo de antes);
///   c. a sequencia vence: ela sobe ao espaco SEM piloto e segue -- um tique a aproxima do destino exatamente um passo da velocidade dela;
///   d. sobre o planeta do destino ela POUSA sozinha, fora do berco, e quem esta dentro ouve que ela chegou;
///   e. quem assume o leme no meio da viagem desliga o rumo, e ouve isso;
///   f. o rumo pra um ponto vazio (o porto de um sistema): ela para no ponto e avisa;
///   g. o rumo vai pro disco (`naves.json`), e a nave pousada com rumo cuja contagem o reinicio comeu volta a contar.
/// Ela empresta o corpo do dono como as camadas 1 a 3, e devolve tudo no fim.
/// </summary>
public partial class GameServer
{
	private void RodarBancadaDoRumoSozinho(ServerPlayer pl, Action<string, bool, string> checa)
	{
		GD.Print("  -- 7) a nave grande segue sozinha (dono, 2026-10-18) --");
		const int t = ZoneCollision.TileSize;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		ZoneKey terra = ZoneKey.Premade("Earth");
		PlanetaNoEspaco namek = Espaco.PreFeitos().First(p => string.Equals(p.Nome, "Namek", StringComparison.OrdinalIgnoreCase));
		Nave? n = null;

		// O LEME, PELAS PORTAS DE PRODUCAO: do console (o corpo um tile ao sul dele), o verbo que assume; do leme, o mesmo verbo larga -- com o
		// destino no argumento, como o cliente o manda com o nav system ligado.
		void AssumirOLeme()
		{
			pl.Pos = NaveGrande.PixelDe((NaveGrande.CelDoConsole.X, NaveGrande.CelDoConsole.Y + 1));
			ComandoDeInteracao(pl, "nave_pilotar", "");
		}
		string Rumo(Vec2 p) => $"{p.X:0}/{p.Y:0}";

		try
		{
			// ---------------------------------------------------------------- montagem: a nave grande pousada na Terra, o dono dentro
			Vec2 berco = PontoDeNascimento(terra);
			n = new Nave
			{
				Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, X = berco.X + 6 * t, Y = berco.Y, DonoConta = pl.Conta, DonoNome = pl.Name,
				ErguidaEm = NowMs(), ArmaduraMax = NaveGrande.ArmaduraMaxima(pl.Ficha.expressedBP),
			};
			n.Armadura = n.ArmaduraMax;
			n.PorZona(terra);
			_naves.Add(n);
			ZoneKey dentro = NaveGrande.ZonaDoInterior(n.Id);
			MoveToZone(pl.Id, terra, new Vec2(n.X - t, n.Y));
			ComandoDeInteracao(pl, "nave_embarcar", "");
			AssumirOLeme();
			checa("(montagem) o dono embarca na nave grande pousada na Terra e assume o leme, pelas portas de producao",
				  n.PilotoId == pl.Id, $"piloto {n.PilotoId}; zona do corpo {pl.Zone}");
			if (n.PilotoId != pl.Id) return;

			// ---------------------------------------------------------------- a. a pose ao leme
			Protocol.Pose pose = EstadoDe(pl, NowMs()).Pose;
			PilotoNaPoseDeVooDeTeste = true;
			Protocol.Pose poseDeAntes;
			try { poseDeAntes = EstadoDe(pl, NowMs()).Pose; }
			finally { PilotoNaPoseDeVooDeTeste = false; }
			checa("**AO LEME DA NAVE GRANDE O CORPO FICA PARADO** (*\"ele deveria so ficar parado na capital ship\"*): pilotar liga o voo, e a pose que vai "
				  + "pros outros e a de parado, e nao a de voo", pl.Voando && pose == Protocol.Pose.Normal, $"voando {pl.Voando}; pose {pose}");
			checa("   DEFEITO INJETADO (a pose de antes): ao leme, a de voo -- a regua de cima reprovaria", poseDeAntes == Protocol.Pose.Voando, $"pose {poseDeAntes}");

			// ---------------------------------------------------------------- a2. ao leme nao se constroi
			// *"n era pra construir fora da nave com vc pilotando ela, seria vc construir dentro da nave"*: o corpo de quem pilota esta do lado de fora
			// do casco, e o assento cairia no chao de fora.
			pl.Ficha.techskill = Math.Max(pl.Ficha.techskill, 60);
			pl.Ficha.Zeni = Math.Max(pl.Ficha.Zeni, 1_000_000);
			int obrasAntes = _noChao.Count;
			ComandoDeTech(pl, "construir", "Research_Station");
			string aoLado = $"Research_Station/{pl.Pos.X + 2 * t:0}/{pl.Pos.Y:0}";
			List<string> recusou = Ouvir(() => ComandoDeTech(pl, "posicionar", aoLado));
			bool nadaSubiu = _noChao.Count == obrasAntes;
			ConstruirAoLemeDeTeste = true;
			try { ComandoDeTech(pl, "posicionar", aoLado); }
			finally { ConstruirAoLemeDeTeste = false; }
			Obra? deFora = _noChao.Count > obrasAntes ? _noChao[^1] : null;
			if (deFora != null) { _noChao.Remove(deFora); GravarMundo(); MandarObras(deFora.Zona); }
			if (pl.Mochila.Quantos("Research_Station") > 0) pl.Mochila.Tirar("Research_Station", pl.Mochila.Quantos("Research_Station"));
			checa("**AO LEME NAO SE CONSTROI** (*\"n era pra construir fora da nave com vc pilotando ela, seria vc construir dentro da nave\"*): o assento "
				  + "e recusado com a frase que manda voltar a ponte, e nada sobe no chao de fora",
				  nadaSubiu && recusou.Any(f => f.Contains(Naves.AoLemeNaoSeConstroi(NaveGrande.Tipo), StringComparison.Ordinal)), string.Join(" | ", recusou));
			checa("   DEFEITO INJETADO (o assento ao leme, o jogo de antes): a mesa sobe no chao de FORA da nave -- a regua de cima reprovaria",
				  deFora != null && !NaveGrande.EhInterior(deFora.Zona, out _), deFora == null ? "nada subiu" : $"em {deFora.Zona}");

			// ---------------------------------------------------------------- b. largar o leme com o destino
			RumoSoNoClienteDeTeste = true;
			try { ComandoDeInteracao(pl, "nave_pilotar", Rumo(namek.Pos)); }
			finally { RumoSoNoClienteDeTeste = false; }
			bool parouAntes = !n.TemRumo && n.LancaEm == 0 && n.PilotoId == 0;
			AssumirOLeme();
			List<string> disse = Ouvir(() => ComandoDeInteracao(pl, "nave_pilotar", Rumo(namek.Pos)));
			checa("**LARGAR O LEME COM O NAV SYSTEM LIGADO, ELA SEGUE** (*\"o jogador sair do piloto, ela ainda continua\"*): o corpo volta pra ponte, a "
				  + "nave guarda o rumo e, pousada, arma a sequencia de lancamento -- e a tripulacao ouve pra onde ela vai",
				  n.PilotoId == 0 && pl.Zone.Equals(dentro) && n.TemRumo && n.LancaEm > 0
				  && Math.Abs(n.RumoX - namek.Pos.X) < 1 && Math.Abs(n.RumoY - namek.Pos.Y) < 1
				  && disse.Any(f => f.Contains("ela sobe sozinha e segue rumo a", StringComparison.Ordinal)),
				  $"rumo {n.TemRumo} ({n.RumoX:0},{n.RumoY:0}); lanca {n.LancaEm > 0}; {string.Join(" | ", disse)}");
			checa("   DEFEITO INJETADO (o rumo so no cliente, o jogo de antes): o mesmo leme largado deixa a nave parada onde estava -- a regua de cima "
				  + "reprovaria", parouAntes, "");

			// ---------------------------------------------------------------- c. sobe sem piloto e anda um passo por tique
			n.LancaEm = NowMs() - 1;
			List<string> subiu = Ouvir(TickDasNaves);
			bool noEspaco = Espaco.EhEspaco(n.Zona);
			checa("...a sequencia vence: ela SOBE AO ESPACO sem ninguem ao leme, com o rumo de pe, e a tripulacao ouve que ela segue sozinha",
				  noEspaco && n.TemRumo && pl.Zone.Equals(dentro) && subiu.Any(f => f.Contains("seguindo sozinha rumo a", StringComparison.Ordinal)),
				  $"nave em {n.Zona}; {string.Join(" | ", subiu)}");
			if (!noEspaco) return;
			double passo = Jandirus.Core.World.MoveRules.BaseSpeedPx * Naves.Fator(n.Velocidade) * Protocol.TickSeconds;
			double antes = (namek.Pos - new Vec2(n.X, n.Y)).Length;
			TickDasNaves();
			double depois = (namek.Pos - new Vec2(n.X, n.Y)).Length;
			checa("...e cada tique a aproxima do destino EXATAMENTE um passo da velocidade dela (a mesma conta da estimativa da carta estelar)",
				  Math.Abs((antes - depois) - passo) < 0.5, $"andou {antes - depois:0.00} px; passo {passo:0.00} px");

			// ---------------------------------------------------------------- d. pousa sozinha no destino
			Vec2 rumo = (namek.Pos - new Vec2(n.X, n.Y)).Normalized();
			Vec2 naBorda = namek.Pos - rumo * (float)(namek.Raio + 3 * passo);
			n.X = naBorda.X;
			n.Y = naBorda.Y;
			List<string> chegou = Ouvir(() => { for (int i = 0; i < 30 && n.TemRumo; i++) TickDasNaves(); });
			ZoneKey zonaDeNamek = ZoneKey.Premade(namek.Nome);
			double doBerco = (new Vec2(n.X, n.Y) - PontoDeNascimento(zonaDeNamek)).Length;
			checa("**ELA POUSA SOZINHA E AVISA** (*\"e avisa no chat quando chegar\"*): sobre o planeta do destino ela desce nele, parada, FORA do berco "
				  + "(a nave e densa: em cima dele prenderia quem nasce) -- e quem esta dentro ouve que ela chegou",
				  !n.TemRumo && n.Zona.Equals(zonaDeNamek) && n.PilotoId == 0 && doBerco >= 2 * t
				  && chegou.Any(f => f.Contains("chegou: pousou em", StringComparison.Ordinal)),
				  $"nave em {n.Zona} a {doBerco:0} px do berco; {string.Join(" | ", chegou)}");

			// ---------------------------------------------------------------- e. quem assume o leme desliga o rumo
			AssumirOLeme();
			ComandoDeInteracao(pl, "nave_pilotar", Rumo(new Vec2(namek.Pos.X + 50_000, namek.Pos.Y)));
			n.LancaEm = NowMs() - 1;
			TickDasNaves();
			bool viajava = Espaco.EhEspaco(n.Zona) && n.TemRumo;
			List<string> assumiu = Ouvir(AssumirOLeme);
			checa("...e QUEM ASSUME O LEME no meio da viagem desliga o rumo, e ouve isso (piloto que ignora o piloto e um sequestro)",
				  viajava && !n.TemRumo && n.PilotoId == pl.Id && assumiu.Any(f => f.Contains("piloto automático da nave desliga", StringComparison.Ordinal)),
				  $"viajava {viajava}; rumo agora {n.TemRumo}; {string.Join(" | ", assumiu)}");

			// ---------------------------------------------------------------- f. o ponto vazio
			Vec2 vazio = new(n.X + 3_000, n.Y);
			for (int i = 1; i < 20 && Espaco.PlanetaSob(SeedDoUniverso, vazio) != null; i++) vazio = new Vec2(n.X + 3_000 + i * 1_000, n.Y);
			ComandoDeInteracao(pl, "nave_pilotar", Rumo(vazio));
			n.X = vazio.X - (float)(passo / 2);
			n.Y = vazio.Y;
			List<string> noPonto = Ouvir(TickDasNaves);
			checa("...o rumo pra um ponto VAZIO (o porto de um sistema): ela para no ponto, no espaco, e avisa",
				  !n.TemRumo && Espaco.EhEspaco(n.Zona) && Math.Abs(n.X - vazio.X) < 1 && noPonto.Any(f => f.Contains("chegou ao ponto marcado", StringComparison.Ordinal)),
				  $"rumo {n.TemRumo}; nave em ({n.X:0},{n.Y:0}), ponto ({vazio.X:0},{vazio.Y:0}); {string.Join(" | ", noPonto)}");

			// ---------------------------------------------------------------- g. o disco
			n.TemRumo = true;
			n.RumoX = namek.Pos.X;
			n.RumoY = namek.Pos.Y;
			var opcoes = new JsonSerializerOptions { IncludeFields = true };
			Nave? lida = JsonSerializer.Deserialize<Nave>(JsonSerializer.Serialize(n, opcoes), opcoes);
			n.PorZona(terra);
			n.X = berco.X + 6 * t;
			n.Y = berco.Y;
			n.LancaEm = 0;   // o reinicio come a contagem (ela nao vai pro disco)
			TickDasNaves();
			checa("**A VIAGEM ATRAVESSA O REINICIO**: o rumo vai pro `naves.json` (o mesmo serializador do `GravarNaves`) -- e a nave pousada com rumo, sem a "
				  + "contagem que o reinicio comeu, volta a contar a sequencia de lancamento",
				  lida is { TemRumo: true } && Math.Abs(lida.RumoX - namek.Pos.X) < 1 && n.TemRumo && n.LancaEm > 0,
				  $"lida: rumo {lida?.TemRumo} ({lida?.RumoX:0},{lida?.RumoY:0}); contagem {n.LancaEm > 0}");
		}
		finally
		{
			RumoSoNoClienteDeTeste = false;
			PilotoNaPoseDeVooDeTeste = false;
			if (NaveDoPiloto(pl) is { } aoLeme) PararDePilotar(pl, aoLeme);
			if (n != null) _naves.Remove(n);
			GravarNaves();
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			pl.Voando = false;
			pl.Altitude = 0f;
			RecalcularVelocidade(pl);
			MandarFicha(pl);
			MandarObras(terra);
		}
	}
}
