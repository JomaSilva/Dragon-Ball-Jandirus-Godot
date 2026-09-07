using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA BOCA DA CAVERNA (`--passagemteste`).
///
/// A queixa do dono (2026-09-06): *"sair de cavernas etc nao ta colocando o personagem 1 tile na
/// frente da entrada, as vezes o personagem volta dentro de paredes"*. Tres coisas, tres familias:
///
///   1) A BOCA E LACRADA -- no BYOND ninguem pisa nela (`Enter()` teleporta e nega o passo). Aqui o
///      `.col` nao a marca (o arquivo nao mudou: contra-exemplo relendo o disco), quem lacra e o
///      runtime, nas duas pontas, pela mesma lista `.passagens`.
///   2) A CHEGADA E UM TILE NA FRENTE DA BOCA DE VOLTA, em todas as passagens de todos os mapas --
///      inclusive na unica que o DM cravou de lado (divergencia declarada em `Passagem.Chegada`).
///      E um contra-exemplo puro: ponto do DM dentro de parede cai na frente da boca, nunca na parede.
///   3) O GATILHO E A BORDA DO PASSO: parado ao lado nao viaja, passar na frente nao viaja,
///      empurrar viaja; segurar a tecla depois de chegar NAO devolve; soltar e empurrar de novo
///      devolve. Um corpo forjado em Vegeta, pelo `TickDasPassagens` de producao.
/// </summary>
public sealed partial class GameServer
{
	private int _pasOk, _pasFalhou;

	/// <summary>Ids dos corpos forjados aqui -- fora de toda faixa das outras bancadas.</summary>
	private const int IdBaseDePassagem = 93_400;

	private void AfirmarPassagem(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _pasOk++; GD.Print($"[passagem]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _pasFalhou++; GD.PrintErr($"[passagem]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDasPassagens()
	{
		_pasOk = _pasFalhou = 0;
		GD.Print("[passagem] ================ A BOCA DA CAVERNA (pedido do dono, 2026-09-06) ================");
		var forjados = new List<ServerPlayer>();
		try
		{
			ABocaELacradaNasDuasPontas();
			AChegadaEUmTileNaFrenteEmTodas();
			OContraExemploPuroDaChegada();
			OPassoQueEntraEPuro();

			ServerPlayer pl = ForjarNaFrenteDaBoca(forjados);
			PararAoLadoOuPassarNaFrenteNaoAtravessa(pl);
			EmpurrarABocaAtravessa(pl);
			SegurarATeclaDepoisDeChegarNaoDevolve(pl);
			SoltarEEmpurrarDeNovoDevolve(pl);
		}
		finally
		{
			foreach (ServerPlayer f in forjados)
			{
				EsquecerPassagem(f.Id);
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
			}
		}
		GD.Print($"[passagem] ================ {_pasOk} OK, {_pasFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// 1) A BOCA E LACRADA
	// =====================================================================
	private void ABocaELacradaNasDuasPontas()
	{
		GD.Print("[passagem] -- 1) a boca e lacrada nas duas pontas --");
		int bocas = 0, lacradas = 0;
		foreach ((string zona, List<Passagem> lista) in _passagens)
		{
			ZoneCollision? mapa = _catalogo?.Get(zona)?.Mapa;
			foreach (Passagem p in lista)
			{
				bocas++;
				if (mapa != null && mapa.BlockedCell(p.X, p.Y) && mapa.Selada(p.X, p.Y)) lacradas++;
			}
		}
		AfirmarPassagem($"toda boca de passagem esta LACRADA no mapa do servidor ({bocas} bocas em {_passagens.Count} zonas)",
						bocas > 0 && lacradas == bocas, $"{lacradas}/{bocas}");

		ZoneEntry? veg = _catalogo?.Get("Vegeta");
		Passagem? bocaV = _passagens.TryGetValue("Vegeta", out List<Passagem>? lv) ? lv.FirstOrDefault() : null;
		ZoneCollision? cru = veg != null && Godot.FileAccess.FileExists(veg.Colisao)
			? ZoneCollision.Load(Godot.FileAccess.GetFileAsBytes(veg.Colisao)) : null;
		AfirmarPassagem("...e o lacre e de RUNTIME: o `.col` de Vegeta relido do disco ainda diz que a boca e chao (contra-exemplo)",
						cru != null && bocaV != null && !cru.BlockedCell(bocaV.X, bocaV.Y),
						bocaV != null ? $"boca ({bocaV.X},{bocaV.Y})" : "sem boca");

		if (cru != null && veg != null && Godot.FileAccess.FileExists(veg.PassagensArq))
			foreach (Passagem p in Passagem.Parse(Godot.FileAccess.GetFileAsString(veg.PassagensArq)))
				cru.Selar(p.X, p.Y);
		AfirmarPassagem("...e lacrar o mapa cru pela MESMA lista `.passagens` que o cliente le bloqueia a boca (as duas pontas concordam)",
						cru != null && bocaV != null && cru.BlockedCell(bocaV.X, bocaV.Y));
	}

	// =====================================================================
	// 2) A CHEGADA E UM TILE NA FRENTE
	// =====================================================================
	private void AChegadaEUmTileNaFrenteEmTodas()
	{
		GD.Print("[passagem] -- 2) a chegada e um tile na frente da boca de volta, em todas --");
		int total = 0, comVolta = 0, semVolta = 0;
		foreach ((string zona, List<Passagem> lista) in _passagens)
			foreach (Passagem p in lista)
			{
				total++;
				var dm = new Vec2(p.Dx, p.Dy);
				Vec2 chegada = ChegadaDaPassagem(zona, p);
				ZoneCollision? mapa = _catalogo?.Get(p.Zona)?.Mapa;
				(int cx, int cy) = CelulaDoPonto(chegada);
				(int dx, int dy) = CelulaDoPonto(dm);
				// NAO E PAREDE NEM BOCA. Chao de verdade e o que se cobra quando ha uma frente livre; ceu e agua
				// valem quando e o proprio ponto do DM (os pads do Templo depositam no ceu, como no original).
				bool livre = mapa != null && !mapa.BlockedCell(cx, cy) && !mapa.Selada(cx, cy);

				// AS BOCAS DE VOLTA AO ALCANCE -- todas: a escada tem tres lado a lado, e "na frente"
				// e estar colado a QUALQUER uma delas, nao a primeira da lista.
				List<Passagem> voltas = (_passagens.TryGetValue(p.Zona, out List<Passagem>? l) ? l : [])
					.Where(v => string.Equals(v.Zona, zona, StringComparison.OrdinalIgnoreCase)
								&& v.Longe(dx, dy) <= Passagem.AlcanceDaVolta)
					.OrderBy(v => v.Longe(dx, dy)).ToList();
				Passagem? volta = voltas.FirstOrDefault(v => Math.Abs(v.X - cx) + Math.Abs(v.Y - cy) == 1) ?? voltas.FirstOrDefault();
				bool naFrente = volta != null && Math.Abs(volta.X - cx) + Math.Abs(volta.Y - cy) == 1;
				// SO SE HA FRENTE LIVRE: os pads do Templo tem parede ao norte e ao sul e ceu dos lados -- ali
				// nao existe "um tile na frente", e a regra cai no ponto do DM (ou na frente DELE).
				bool temFrente = mapa != null && voltas.Any(v =>
					new[] { (0, 1), (-1, 0), (1, 0), (0, -1) }.Any(r => mapa.ServeDeChao(v.X + r.Item1, v.Y + r.Item2, mesmoNaBorda: true)));
				if (volta != null) comVolta++; else semVolta++;

				GD.Print($"[passagem]      {zona} ({p.X},{p.Y}) -> {p.Zona}: DM ({dx},{dy}) -> chega ({cx},{cy})"
						 + (volta != null ? $" | boca de volta ({volta.X},{volta.Y})" : " | sem boca de volta por perto: vale o DM")
						 + (livre ? "" : "   !!! NAO E CHAO LIVRE"));
				// O DIAGNOSTICO DOS VIZINHOS DA BOCA, quando a chegada nao caiu na frente dela: diz POR QUE.
				if (volta != null && mapa != null && !naFrente)
					foreach ((string rumo, int vx, int vy) in new[] { ("S", volta.X, volta.Y + 1), ("O", volta.X - 1, volta.Y), ("L", volta.X + 1, volta.Y), ("N", volta.X, volta.Y - 1) })
						GD.Print($"[passagem]         vizinho {rumo} ({vx},{vy}): bloqueia={mapa.BlockedCell(vx, vy)} boca={mapa.Selada(vx, vy)} "
								 + $"agua={mapa.EhAgua(vx, vy)} nuvem={mapa.EhNuvem(vx, vy)} beirada={mapa.NaBorda(vx, vy)}");
				AfirmarPassagem($"{zona} ({p.X},{p.Y}) -> {p.Zona}: a chegada nao e parede nem boca", livre, $"({cx},{cy})");
				if (volta != null && temFrente)
					AfirmarPassagem($"...e fica UM TILE NA FRENTE da boca de volta ({volta.X},{volta.Y})", naFrente, $"chega ({cx},{cy})");
				else if (volta != null)
					GD.Print($"[passagem]         (boca de volta sem frente livre -- parede e ceu em volta: vale o ponto do DM ou a frente dele)");
			}
		GD.Print($"[passagem]      {total} passagens: {comVolta} com boca de volta por perto, {semVolta} sem");

		// AS DUAS NOMINAIS: a que o DM cravou de lado, e uma que ele acertou.
		Passagem? e1 = _passagens.TryGetValue("Earth_Cave", out List<Passagem>? lc)
			? lc.FirstOrDefault(q => q.X == 155 && q.Y == 200) : null;
		if (e1 != null)
		{
			(int cx, int cy) = CelulaDoPonto(ChegadaDaPassagem("Earth_Cave", e1));
			AfirmarPassagem("a saida 1 da caverna da Terra chega em (267,111), NA FRENTE da boca (267,110) -- e nao em (266,112), "
							+ "que o DM cravou de lado (divergencia declarada: Turfs.dm:1452-1455)",
							cx == 267 && cy == 111, $"({cx},{cy})");
		}
		else AfirmarPassagem("a saida 1 da caverna da Terra existe na lista (155,200)", false);

		// E A QUE CAI EM CIMA DE UMA MAQUINA: a saida da Sala do Tempo, cravada no teleporter denso do Templo.
		Passagem? h1 = _passagens.TryGetValue("Hyperbolic_Time_Chamber", out List<Passagem>? lh) ? lh.FirstOrDefault() : null;
		if (h1 != null)
		{
			(int cx, int cy) = CelulaDoPonto(ChegadaDaPassagem("Hyperbolic_Time_Chamber", h1));
			ZoneCollision? templo = _catalogo?.Get("Lookout")?.Mapa;
			AfirmarPassagem("a saida da Sala do Tempo chega em (124,81), NA FRENTE do teleporter denso (124,80) em que o DM cravava o corpo",
							cx == 124 && cy == 81 && templo != null && templo.BlockedCell(124, 80), $"({cx},{cy})");
		}
		else AfirmarPassagem("a saida da Sala do Tempo existe na lista", false);

		Passagem? v1 = _passagens.TryGetValue("Vegeta_Cave", out List<Passagem>? lvc) ? lvc.FirstOrDefault() : null;
		if (v1 != null)
		{
			(int cx, int cy) = CelulaDoPonto(ChegadaDaPassagem("Vegeta_Cave", v1));
			AfirmarPassagem("a saida da caverna de Vegeta chega em (141,215): o proprio ponto do DM, que e a frente da boca (141,214)",
							cx == 141 && cy == 215, $"({cx},{cy})");
		}
		else AfirmarPassagem("a saida da caverna de Vegeta existe na lista", false);
	}

	// =====================================================================
	// 2b) O CONTRA-EXEMPLO PURO: ponto do DM dentro de parede
	// =====================================================================
	private void OContraExemploPuroDaChegada()
	{
		GD.Print("[passagem] -- 2b) contra-exemplo puro: o DM cravou dentro da parede --");
		// 12x12 -- maior que a margem da beirada (2 tiles), senao o miolo nao tem chao que sirva.
		const int W = 12, H = 12;
		var bits = new byte[(W * H + 7) / 8];
		void Parede(int x, int y) { int i = y * W + x; bits[i >> 3] |= (byte)(1 << (i & 7)); }
		for (int x = 0; x < W; x++) Parede(x, 4);   // a fileira da parede, com a boca no meio dela
		ZoneCollision mapa = ZoneCollision.Montar(W, H, bits);
		mapa.Selar(5, 4);
		var voltas = new List<Passagem> { new() { X = 5, Y = 4, Zona = "Origem" } };

		Vec2 naParede = mapa.CentroDaCelula(4, 4);
		Vec2 chegou = Passagem.Chegada(mapa, voltas, "Origem", naParede);
		AfirmarPassagem("DM dentro da parede, boca de volta a um tile: o corpo cai NA FRENTE da boca (5,5), nao na parede",
						CelulaDoPonto(chegou) == (5, 5), $"({chegou.X:0},{chegou.Y:0})");

		Vec2 naFrente = mapa.CentroDaCelula(5, 5);
		AfirmarPassagem("DM ja na frente da boca: a chegada e o proprio ponto do DM, sem mexer um pixel",
						Passagem.Chegada(mapa, voltas, "Origem", naFrente).Equals(naFrente));

		Vec2 outra = Passagem.Chegada(mapa, voltas, "OutraZona", naParede);
		(int ox, int oy) = CelulaDoPonto(outra);
		AfirmarPassagem("boca de volta que leva pra OUTRA zona nao conta: DM na parede cai no chao livre mais perto (contra-exemplo)",
						mapa.ServeDeChao(ox, oy) && !mapa.Selada(ox, oy) && !outra.Equals(naParede), $"({ox},{oy})");

		Vec2 longe = mapa.CentroDaCelula(5, 8);   // a 4 tiles da boca: fora do alcance da volta
		AfirmarPassagem($"boca de volta a mais de {Passagem.AlcanceDaVolta} tiles do DM nao conta: vale o DM quando ele e chao",
						Passagem.Chegada(mapa, voltas, "Origem", longe).Equals(longe));

		// TRES BOCAS LADO A LADO (a escada do Templo): o par (boca, vizinho) e escolhido de uma vez.
		var escada = new List<Passagem>
		{
			new() { X = 4, Y = 4, Zona = "Origem" }, new() { X = 5, Y = 4, Zona = "Origem" }, new() { X = 6, Y = 4, Zona = "Origem" },
		};
		mapa.Selar(4, 4); mapa.Selar(6, 4);
		Vec2 frenteDoMeio = mapa.CentroDaCelula(5, 5);
		AfirmarPassagem("tres bocas lado a lado, DM na frente da do meio: a chegada e a frente da do MEIO (a primeira da lista nao rouba)",
						Passagem.Chegada(mapa, escada, "Origem", frenteDoMeio).Equals(frenteDoMeio));

		// A BEIRADA DO MAPA VALE COMO CHEGADA: a boca na penultima fileira, a frente na ultima.
		var beira = ZoneCollision.Montar(W, H, new byte[(W * H + 7) / 8]);
		beira.Selar(5, H - 2);
		var voltaNaBeira = new List<Passagem> { new() { X = 5, Y = H - 2, Zona = "Origem" } };
		Vec2 ultimaFileira = beira.CentroDaCelula(5, H - 1);
		AfirmarPassagem("boca na penultima fileira do mapa: a chegada e a ULTIMA fileira, a beirada (e onde o DM poe quem sobe ao Templo)",
						Passagem.Chegada(beira, voltaNaBeira, "Origem", ultimaFileira).Equals(ultimaFileira));

		AfirmarPassagem("a boca lacrada NAO serve de chao (a chegada nunca cai em cima dela)", !mapa.ServeDeChao(5, 4) && mapa.BlockedCell(5, 4));
	}

	// =====================================================================
	// 3a) O PASSO QUE ENTRA, puro
	// =====================================================================
	private void OPassoQueEntraEPuro()
	{
		GD.Print("[passagem] -- 3a) o passo que entra na boca (regra pura) --");
		const int T = ZoneCollision.TileSize;
		var boca = new Passagem { X = 4, Y = 2, Zona = "x" };
		Vec2 naFrente = new(4 * T + T / 2f, 3 * T + T / 2f);          // centro da celula da frente
		Vec2 doisAbaixo = new(4 * T + T / 2f, 4 * T + T / 2f);        // um tile mais longe
		Vec2 encostado = new(4 * T + T / 2f, 3 * T + 5 - 8);          // caixa dos pes colada na borda da boca
		Vec2 aoLado = new(5 * T + T / 2f, 3 * T + T / 2f);            // diagonal

		AfirmarPassagem("da celula da frente, olhando pro norte: ENTRA", boca.NoPasso(naFrente, Facing.North));
		AfirmarPassagem("da celula da frente, olhando pro leste: nao entra (passa na frente)", !boca.NoPasso(naFrente, Facing.East));
		AfirmarPassagem("da celula da frente, olhando pro sul (de costas): nao entra", !boca.NoPasso(naFrente, Facing.South));
		AfirmarPassagem("colado na borda da boca (onde o lacre para o corpo), olhando pro norte: ENTRA", boca.NoPasso(encostado, Facing.North));
		AfirmarPassagem("dois tiles abaixo, olhando pro norte: ainda nao (o passo projetado nao alcanca)", !boca.NoPasso(doisAbaixo, Facing.North));
		AfirmarPassagem("na diagonal, olhando pro norte: nao entra (a caixa projetada nao toca a coluna da boca)", !boca.NoPasso(aoLado, Facing.North));
	}

	// =====================================================================
	// 3b) UM CORPO FORJADO EM VEGETA, na frente da boca da caverna
	// =====================================================================
	private ServerPlayer ForjarNaFrenteDaBoca(List<ServerPlayer> forjados)
	{
		const int T = ZoneCollision.TileSize;
		var pl = new ServerPlayer
		{
			Id = IdBaseDePassagem, Peer = null, Name = "bancada: quem entra na caverna", Race = "Human", Genero = "Male", Idade = 25,
			Zone = ZoneKey.Premade("Vegeta"), Pos = new Vec2(141 * T + T / 2f, 215 * T + T / 2f),
			Conta = "bancada_passagem", Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = 1000 }, Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		pl.Ficha.Class = "Normal";
		PorNoMundo(pl);
		forjados.Add(pl);
		pl.Facing = Facing.North;
		pl.Moving = false;
		return pl;
	}

	private static (int X, int Y) CelulaDosPes(ServerPlayer pl) =>
		((int)MathF.Floor(pl.Pos.X / ZoneCollision.TileSize),
		 (int)MathF.Floor((pl.Pos.Y + MoveRules.FeetOffsetY) / ZoneCollision.TileSize));

	private void PararAoLadoOuPassarNaFrenteNaoAtravessa(ServerPlayer pl)
	{
		GD.Print("[passagem] -- 3b) parado ao lado, ou passando na frente: nao atravessa --");
		AfirmarPassagem("o corpo nasce na frente da boca de Vegeta (141,215), olhando pra ela",
						pl.Zone.Name == "Vegeta" && CelulaDosPes(pl) == (141, 215) && pl.Facing == Facing.North);

		pl.Moving = false;
		for (int i = 0; i < 3; i++) TickDasPassagens();
		AfirmarPassagem("PARADO olhando pra boca: tres tiques e continua em Vegeta", pl.Zone.Name == "Vegeta");

		pl.Facing = Facing.East; pl.Moving = true;
		for (int i = 0; i < 3; i++) TickDasPassagens();
		AfirmarPassagem("ANDANDO PRO LESTE na frente da boca (passando por ela): continua em Vegeta", pl.Zone.Name == "Vegeta");

		pl.Facing = Facing.South;
		for (int i = 0; i < 3; i++) TickDasPassagens();
		AfirmarPassagem("andando de COSTAS pra boca: continua em Vegeta", pl.Zone.Name == "Vegeta");
		pl.Moving = false;
		TickDasPassagens();
	}

	private void EmpurrarABocaAtravessa(ServerPlayer pl)
	{
		GD.Print("[passagem] -- 3c) empurrar a boca atravessa, e chega na frente da boca de volta --");
		pl.Facing = Facing.North; pl.Moving = true;
		TickDasPassagens();
		(int cx, int cy) = CelulaDosPes(pl);
		ZoneCollision? mapa = _catalogo?.Get("Vegeta_Cave")?.Mapa;
		AfirmarPassagem("um tique EMPURRANDO a boca: o corpo esta na caverna de Vegeta", pl.Zone.Name == "Vegeta_Cave", pl.Zone.Name);
		AfirmarPassagem("...na celula (156,201), um tile na frente da boca de volta (156,200)", (cx, cy) == (156, 201), $"({cx},{cy})");
		AfirmarPassagem("...em chao livre, que nao e boca (a boca de volta esta lacrada)",
						mapa != null && mapa.ServeDeChao(cx, cy) && mapa.Selada(156, 200));
	}

	private void SegurarATeclaDepoisDeChegarNaoDevolve(ServerPlayer pl)
	{
		GD.Print("[passagem] -- 3d) segurar a tecla depois de chegar nao devolve --");
		// A CARENCIA JA PASSOU (o relogio e adiantado na mao): o que segura agora e so a borda do gesto.
		_acabouDeAtravessar[pl.Id] = 0;
		pl.Facing = Facing.North; pl.Moving = true;   // continua empurrando a boca de volta
		for (int i = 0; i < 6; i++) TickDasPassagens();
		AfirmarPassagem("seis tiques com a tecla SEGURADA contra a boca de volta, carencia vencida: continua na caverna",
						pl.Zone.Name == "Vegeta_Cave", pl.Zone.Name);
	}

	private void SoltarEEmpurrarDeNovoDevolve(ServerPlayer pl)
	{
		GD.Print("[passagem] -- 3e) soltar e empurrar de novo devolve --");
		pl.Moving = false;
		TickDasPassagens();   // a borda cai: a boca esta armada de novo
		AfirmarPassagem("soltar a tecla por um tique nao viaja sozinho", pl.Zone.Name == "Vegeta_Cave");
		pl.Moving = true;
		TickDasPassagens();
		(int cx, int cy) = CelulaDosPes(pl);
		AfirmarPassagem("empurrar DE NOVO devolve pra Vegeta", pl.Zone.Name == "Vegeta", pl.Zone.Name);
		AfirmarPassagem("...na frente da boca de Vegeta, em (141,215) -- e nao dentro da parede", (cx, cy) == (141, 215), $"({cx},{cy})");

		// E A CARENCIA CONTINUA SENDO O PISO: soltar e empurrar dentro dos 1,5 s nao vale.
		pl.Moving = false; TickDasPassagens();
		pl.Moving = true; TickDasPassagens();
		AfirmarPassagem("soltar e empurrar DENTRO da carencia de 1,5 s nao viaja (o piso continua valendo)", pl.Zone.Name == "Vegeta");
		pl.Moving = false;
	}
}
