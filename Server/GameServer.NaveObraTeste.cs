using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A CAMADA 6 DA `--naveteste` (2026-10-18): A OBRA DENTRO DA NAVE GRANDE. O dono: *"permita construir dentro da capital ship (gravidade ,
/// research etc mas nao paredes etc) e se ela explodir esses itens saem da nave sem estarem parafusados"*. Pelas portas de producao:
///   a. DENTRO SE CONSTROI: a maquina de gravidade e a mesa de pesquisa se assentam no interior (`Posicionar`); contra-exemplo
///      (`LugarDeObra.NaveRecusaObraDeTeste`): a recusa de antes;
///   b. AS PASSAGENS FICAM LIVRES: a plataforma e a chegada recusam pela porta de producao, e a porta da ponte pela pergunta do Core que as
///      duas pontas fazem (`Assentamento.DoLugar` com a zona); PAREDE NAO (`PorQueNaoConstroi`, o bloco de base); nave nao cabe em nave;
///   c. A NAVE EXPLODE POUSADA, com o dono dentro: as duas saem pro chao do planeta, ao lado do casco, SOLTAS, o campo de gravidade
///      DESLIGADO, uma por celula, nenhuma em cima do dono ejetado, e a mesa densa barra a celula dela no mapa de fora; contra-exemplo
///      (`ObrasFicamNaNaveDeTeste`, o DM): a mesa fica presa no interior de uma nave que nao existe mais;
///   d. A NAVE E RECOLHIDA com uma maquina que um PASSAGEIRO montou dentro: a nave vai pra mochila, a maquina fica no chao, ao lado, solta.
/// A explosao no ESPACO e a secao 8 da camada 2 (a mesa da secao 3 de la). Esta camada empresta o corpo do dono como as camadas 1 a 3 e
/// devolve tudo no fim -- o zeni, a tech, a mochila, o lugar -- e tira as naves e as obras que fez.
/// </summary>
public partial class GameServer
{
	private void RodarBancadaDaObraNaNave(ServerPlayer pl, Action<string, bool, string> checa)
	{
		GD.Print("  -- 6) a obra dentro da nave grande (dono, 2026-10-18) --");
		const int t = ZoneCollision.TileSize;
		ZoneKey zonaAntes = pl.Zone;
		Vec2 posAntes = pl.Pos;
		var zeniAntes = pl.Ficha.Zeni;
		var techAntes = pl.Ficha.techskill;
		var naves = new List<Nave>();
		var obras = new List<Obra>();

		// UMA NAVE GRANDE DO DONO, pousada a dois tiles a leste de onde ele estava -- como a camada 5 arma o pod: o que esta sob teste e o que
		// acontece dentro e no fim dela, e nao a compra (a camada 2 cobre a fabrica).
		Nave NovaNave()
		{
			var n = new Nave
			{
				Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, X = posAntes.X + 2 * t, Y = posAntes.Y,
				DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs(),
				ArmaduraMax = NaveGrande.ArmaduraMaxima(pl.Ficha.expressedBP),
			};
			n.Armadura = n.ArmaduraMax;
			n.PorZona(zonaAntes);
			_naves.Add(n);
			naves.Add(n);
			return n;
		}

		// UMA OBRA PELA PORTA DE PRODUCAO: fabrica (`construir`) e assenta (`posicionar`) na celula pedida. Devolve a obra que subiu (ou nula) e
		// o que o servidor disse; a sobra de uma recusa sai da mochila.
		(Obra? Obra, List<string> Disse) Assentar(string tipo, (int X, int Y) cel)
		{
			ComandoDeTech(pl, "construir", tipo);
			Vec2 p = NaveGrande.PixelDe(cel);
			int antes = _noChao.Count;
			List<string> disse = Ouvir(() => ComandoDeTech(pl, "posicionar", $"{tipo}/{p.X:0}/{p.Y:0}"));
			if (pl.Mochila.Quantos(tipo) > 0) pl.Mochila.Tirar(tipo, pl.Mochila.Quantos(tipo));
			Obra? o = _noChao.Count > antes ? _noChao[^1] : null;
			if (o != null) obras.Add(o);
			return (o, disse);
		}

		try
		{
			pl.Ficha.techskill = 60;
			pl.Ficha.Zeni = 5_000_000;

			// ---------------------------------------------------------------- a. DENTRO SE CONSTROI
			Nave a = NovaNave();
			ZoneKey dentro = NaveGrande.ZonaDoInterior(a.Id);
			pl.Pos = new Vec2(a.X - t, a.Y);
			ComandoDeInteracao(pl, "nave_embarcar", "");
			checa("(montagem) o dono embarca na nave grande pousada, pela porta de producao", pl.Zone.Equals(dentro), $"zona {pl.Zone}");
			if (!pl.Zone.Equals(dentro)) return;

			(int X, int Y) ch = NaveGrande.CelDaChegada;
			(Obra? grav, List<string> disseGrav) = Assentar("Gravity", (ch.X - 2, ch.Y));
			(Obra? mesa, List<string> disseMesa) = Assentar("Research_Station", (ch.X + 2, ch.Y));
			checa("**DENTRO DA NAVE SE CONSTROI** (*\"permita construir dentro da capital ship (gravidade , research etc\"*): a maquina de gravidade e "
				  + "a mesa de pesquisa se assentam no interior DESTA nave, pela porta de producao",
				  grav != null && grav.Zona.Equals(dentro) && mesa != null && mesa.Zona.Equals(dentro),
				  $"gravidade: {(grav != null ? "no chao" : string.Join(" | ", disseGrav))}; mesa: {(mesa != null ? "no chao" : string.Join(" | ", disseMesa))}");
			if (grav == null || mesa == null) return;

			Obra? deAntes = null;
			List<string> disseAntes = [];
			LugarDeObra.NaveRecusaObraDeTeste = true;
			try { (deAntes, disseAntes) = Assentar("Research_Station", (ch.X + 2, ch.Y + 2)); }
			finally { LugarDeObra.NaveRecusaObraDeTeste = false; }
			checa("   DEFEITO INJETADO (a recusa de antes de 2026-10-18): a mesma mesa ouve que nao da pra construir dentro de uma nave, e nada sobe -- a "
				  + "regua de cima reprovaria",
				  deAntes == null && disseAntes.Any(f => f.Contains("não dá pra construir dentro de uma nave", StringComparison.Ordinal)),
				  string.Join(" | ", disseAntes));

			// ---------------------------------------------------------------- b. AS PASSAGENS, A PAREDE E A NAVE DENTRO DA NAVE
			(Obra? naPlataforma, List<string> disseP) = Assentar("Research_Station", NaveGrande.CelDaPlataforma);
			(Obra? naChegada, List<string> disseC) = Assentar("Research_Station", ch);
			ZoneCollision? planta = MapaDaZonaOuCatalogo(dentro);
			(int X, int Y) vao = (NaveGrande.ColunaDoVao, NaveGrande.AlturaDaSala);
			RecusaDeAssento Na((int X, int Y) cel) => Assentamento.DoLugar(planta, NaveGrande.PixelDe(cel), cel.X, cel.Y, dentro);
			RecusaDeAssento antesDoVao = Na((vao.X, vao.Y - 1)), noVao = Na(vao), depoisDoVao = Na((vao.X, vao.Y + 1));
			static bool Passagem(List<string> disse) => disse.Any(f => f.Contains("passagem da nave", StringComparison.Ordinal));
			checa("**AS PASSAGENS FICAM LIVRES**: a plataforma (a unica saida) e a chegada (onde quem embarca aparece) recusam pela porta de producao, "
				  + "e a porta da sala da ponte, com a celula de cada lado, pela pergunta do Core que o fantasma do cliente tambem faz",
				  naPlataforma == null && naChegada == null && Passagem(disseP) && Passagem(disseC)
				  && antesDoVao == RecusaDeAssento.PassagemDaNave && noVao == RecusaDeAssento.PassagemDaNave && depoisDoVao == RecusaDeAssento.PassagemDaNave,
				  $"plataforma: {string.Join(" | ", disseP)}; chegada: {string.Join(" | ", disseC)}; porta da ponte: {antesDoVao}/{noVao}/{depoisDoVao}");
			checa("...e so no interior: a mesma celula num planeta e chao como outro qualquer",
				  Assentamento.DoLugar(null, NaveGrande.PixelDe(vao), vao.X, vao.Y, zonaAntes) == RecusaDeAssento.Pode, $"em {zonaAntes.Name}");

			string parede = PorQueNaoConstroi(pl);
			int navesAntes = _naves.Count;
			ComandoDeTech(pl, "construir", "Spacepod");
			Vec2 pp = NaveGrande.PixelDe((ch.X, ch.Y + 2));
			List<string> disseNave = Ouvir(() => ComandoDeTech(pl, "posicionar", $"Spacepod/{pp.X:0}/{pp.Y:0}"));
			if (pl.Mochila.Quantos("Spacepod") > 0) pl.Mochila.Tirar("Spacepod", pl.Mochila.Quantos("Spacepod"));
			checa("**PAREDE NAO** (*\"mas nao paredes etc\"*): o bloco de base continua recusado no interior (`RegrasDeBloco.ZonaAceita`) -- e uma "
				  + "nave nao cabe dentro de outra",
				  parede == RegrasDeBloco.Motivo(RecusaDeBloco.Zona) && _naves.Count == navesAntes
				  && disseNave.Any(f => f.Contains("não cabe dentro de outra nave", StringComparison.Ordinal)),
				  $"bloco: \"{parede}\"; nave: {string.Join(" | ", disseNave)}");

			// ---------------------------------------------------------------- c. A NAVE EXPLODE POUSADA
			// A maquina de gravidade aparafusada e LIGADA em 10x -- o estado que o menu dela daria (o que esta sob teste e o fim da nave, e nao o
			// menu da maquina); a mesa ja nasce aparafusada (`Interacoes.NasceAparafusada`). O dono fica la dentro: e ejetado antes das obras.
			grav.Aparafusada = true;
			grav.Gravidade = new GravidadeDaObra { Grav = 10, Range = 2 };
			LerOsCampos();
			bool campoDentro = _camposLigados.Contains(grav);
			var casco = new Vec2(a.X, a.Y);
			EstragarNave(a, a.ArmaduraMax * 5, null);
			LerOsCampos();
			ZoneCollision? mapaDeFora = MapaDaZonaOuCatalogo(zonaAntes);
			(int mx, int my) = CatalogoDeObras.Celula(mesa.X, mesa.Y);
			(int gx, int gy) = CatalogoDeObras.Celula(grav.X, grav.Y);
			(int ex, int ey) = CatalogoDeObras.Celula(pl.Pos.X, pl.Pos.Y);
			checa("**A NAVE EXPLODE POUSADA, E O QUE ESTAVA DENTRO SAI SOLTO** (*\"se ela explodir esses itens saem da nave sem estarem parafusados\"*): "
				  + "a maquina e a mesa no chao do planeta, ao lado do casco, sem o parafuso -- e nenhuma obra ficou no interior",
				  _naves.All(x => x.Id != a.Id) && grav.Zona.Equals(zonaAntes) && mesa.Zona.Equals(zonaAntes) && !grav.Aparafusada && !mesa.Aparafusada
				  && _noChao.All(o => !o.Zona.Equals(dentro))
				  && (new Vec2(grav.X, grav.Y) - casco).Length < 6 * t && (new Vec2(mesa.X, mesa.Y) - casco).Length < 6 * t,
				  $"gravidade em {grav.Zona.Name} ({grav.X:0},{grav.Y:0}), aparafusada {grav.Aparafusada}; mesa em {mesa.Zona.Name} ({mesa.X:0},{mesa.Y:0}), "
				  + $"aparafusada {mesa.Aparafusada}; casco em ({casco.X:0},{casco.Y:0})");
			checa("...o CAMPO DE GRAVIDADE, ligado la dentro, DESLIGA (ele nao pede parafuso pra pulsar: uma maquina de 10x arremessada esmagaria quem "
				  + "estivesse em volta dos destrocos)",
				  campoDentro && grav.Gravidade is { Grav: 0, MudaEm: 0 } && !_camposLigados.Contains(grav),
				  $"ligado dentro {campoDentro}; gravidade agora {grav.Gravidade?.Grav}");
			checa("...uma por celula, nenhuma em cima do dono ejetado, e a mesa DENSA barra a celula dela no mapa de fora",
				  (mx, my) != (gx, gy) && (mx, my) != (ex, ey) && (gx, gy) != (ex, ey) && mapaDeFora?.BlockedCell(mx, my) == true,
				  $"mesa ({mx},{my}), gravidade ({gx},{gy}), o dono ({ex},{ey}) em {pl.Zone.Name}; a celula da mesa barra {mapaDeFora?.BlockedCell(mx, my)}");

			// ---------------------------------------------------------------- c'. CONTRA-EXEMPLO: a obra que fica
			Nave b = NovaNave();
			ZoneKey dentroB = NaveGrande.ZonaDoInterior(b.Id);
			Vec2 pm = NaveGrande.PixelDe((ch.X + 2, ch.Y));
			var presa = new Obra
			{
				Id = _proximaObraId++, Tipo = "Research_Station", X = pm.X, Y = pm.Y, DonoConta = pl.Conta, DonoNome = pl.Name,
				Aparafusada = true, ErguidaEm = NowMs(),
			};
			presa.PorZona(dentroB);
			_noChao.Add(presa);
			obras.Add(presa);
			ObrasFicamNaNaveDeTeste = true;
			try { EstragarNave(b, b.ArmaduraMax * 5, null); }
			finally { ObrasFicamNaNaveDeTeste = false; }
			checa("   DEFEITO INJETADO (o fim da nave sem tirar o que tem dentro -- o DM, cujo `testDestroy` so tira `mob`): a mesa fica presa no interior "
				  + "de uma nave que nao existe mais, aparafusada -- a regua de cima reprovaria",
				  _naves.All(x => x.Id != b.Id) && presa.Zona.Equals(dentroB) && presa.Aparafusada, $"mesa em {presa.Zona}");

			// ---------------------------------------------------------------- d. A NAVE RECOLHIDA
			// A maquina e de um PASSAGEIRO (outra conta): so a mao dele a tira do chao (`PegarObra`), e e por isso que o recolher nao pode esperar
			// a nave ficar vazia de obra.
			Nave c = NovaNave();
			ZoneKey dentroC = NaveGrande.ZonaDoInterior(c.Id);
			Vec2 pg = NaveGrande.PixelDe((ch.X - 2, ch.Y));
			var doPassageiro = new Obra
			{
				Id = _proximaObraId++, Tipo = "Gravity", X = pg.X, Y = pg.Y, DonoConta = "bancada_passageiro", DonoNome = "um passageiro",
				Aparafusada = true, ErguidaEm = NowMs(),
			};
			doPassageiro.PorZona(dentroC);
			_noChao.Add(doPassageiro);
			obras.Add(doPassageiro);
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, new Vec2(c.X - t, c.Y));
			else pl.Pos = new Vec2(c.X - t, c.Y);
			List<string> recolheu = Ouvir(() => PegarNave(pl));
			bool naMochila = pl.Mochila.Quantos(NaveGrande.Tipo) > 0;
			checa("**A NAVE RECOLHIDA DEIXA O QUE TINHA DENTRO NO CHAO, AO LADO, SOLTO**: ela vai pra mochila, a maquina que um passageiro montou la "
				  + "dentro nao trava o recolher -- e o dono ouve o que ficou",
				  _naves.All(x => x.Id != c.Id) && naMochila && doPassageiro.Zona.Equals(zonaAntes) && !doPassageiro.Aparafusada
				  && (new Vec2(doPassageiro.X, doPassageiro.Y) - new Vec2(c.X, c.Y)).Length < 6 * t
				  && recolheu.Any(f => f.Contains("fica no chão, ao lado", StringComparison.Ordinal)),
				  $"maquina em {doPassageiro.Zona.Name} ({doPassageiro.X:0},{doPassageiro.Y:0}), aparafusada {doPassageiro.Aparafusada}; "
				  + string.Join(" | ", recolheu));
		}
		finally
		{
			LugarDeObra.NaveRecusaObraDeTeste = false;
			ObrasFicamNaNaveDeTeste = false;
			List<ZoneKey> zonas = [.. obras.Select(o => o.Zona).Distinct()];
			foreach (Obra o in obras) _noChao.Remove(o);
			foreach (Nave n in naves) _naves.Remove(n);
			// a nave que tivesse subido DENTRO de uma destas (a recusa de cima falhando)
			_naves.RemoveAll(x => NaveGrande.EhInterior(x.Zona, out int id) && naves.Any(n => n.Id == id));
			GravarMundo();
			GravarNaves();
			pl.Ficha.Zeni = zeniAntes;
			pl.Ficha.techskill = techAntes;
			foreach (string item in new[] { "Gravity", "Research_Station", "Spacepod", NaveGrande.Tipo })
				if (pl.Mochila.Quantos(item) > 0) pl.Mochila.Tirar(item, pl.Mochila.Quantos(item));
			if (!pl.Zone.Equals(zonaAntes)) MoveToZone(pl.Id, zonaAntes, posAntes);
			else pl.Pos = posAntes;
			foreach (ZoneKey z in zonas) MandarObras(z);
			RecalcularVelocidade(pl);
			MandarFicha(pl);
			MandarMochila(pl);
		}
	}
}
