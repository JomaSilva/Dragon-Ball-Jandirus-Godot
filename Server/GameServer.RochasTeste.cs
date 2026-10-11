using Godot;
using Jandirus.Core.Economia;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DAS ROCHAS (`--rochasteste`) -- o pedido do dono de 2026-10-10: as rochas grandes dos planetas
/// gerados ganham colisao, e as rochas de minerio nascem pelos planetas na proporcao da reserva, rendem
/// recursos a quem as colhe e tambem barram. As regras estao em `Core/World/RochaGrande.cs` e
/// `Core/Economia/RochaDeMinerio.cs`; o servidor delas, em `GameServer.Rochas.cs`.
///
/// No boot, como as outras de servidor. Os corpos sao forjados (sem `Peer`) e contam como TELAS pela escuta
/// (`EscutaDeRochas`): e ela quem guarda cada pacote que sairia no fio. Tudo entra pelo caminho de producao
/// -- o `TickDasRochas`, o `Verbo(...)` da tecla E, o `SocarCenario`, o `DerrubarCelula`, o `MoveToZone` e o
/// pouso de verdade num mundo sorteado.
///
///   1. A REGRA: o teto pelo tamanho, o alvo pela fracao da reserva, a faixa do rendimento.
///   2. A ROCHA GRANDE NO GERADOR: a pegada barra e nao se quebra, o topo fica livre, ninguem pousa preso.
///   3. A ROCHA GRANDE NO SERVIDOR: o mundo sorteado de verdade barra o passo; quem chega dentro e solto.
///   4. ONDE O MINERIO NASCE: so em planeta, em terra aberta, longe de tudo.
///   5. QUANTAS: cheia, meia e zerada.
///   6. COLHER: tira da reserva, paga, some.
///   7. A ROCHA DE MINERIO BARRA, e o soco e o cenario que cai nao a levam.
///   8. A REPOSICAO: o relogio, o teto dele e o planeta sem ninguem.
///   9. O FIO: o retrato de quem chega e o que muda depois.
///
/// Cinco defeitos injetados, cada um lido pela linha do conserto: `RochaGrande.SemPegadaDeTeste`,
/// `RochaDeMinerio.IgnoraAReservaDeTeste`, `RochaDeMinerio.SemColisaoDeTeste`, `PresoNaRochaDeTeste` e a
/// reserva que nao e consultada na coleta (medida pela propria reserva).
///
///     Godot --headless -- --server --port 8130 --rochasteste
/// </summary>
public partial class GameServer
{
	private int _roOk, _roFalhou;

	private void AfirmarRo(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _roOk++; GD.Print($"[rochas]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _roFalhou++; GD.PrintErr($"[rochas]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDasRochas()
	{
		_roOk = _roFalhou = 0;
		GD.Print("[rochas] ================ AS ROCHAS GRANDES E AS ROCHAS DE MINERIO (dono, 2026-10-10) ================");

		ZoneKey terra = ZoneKey.Premade("Earth"), namek = ZoneKey.Premade("Namek"), vegeta = ZoneKey.Premade("Vegeta");
		ZoneKey mirante = ZoneKey.Premade("Lookout");
		var forjados = new List<ServerPlayer>();
		var reservasMexidas = new List<ZoneKey>();

		ServerPlayer Forjar(string nome, ZoneKey zona, Vec2 onde)
		{
			var c = new ServerPlayer
			{
				Id = _nextId++, Peer = null, Name = nome, Race = "Human", Genero = "Male", Idade = 25,
				Zone = zona, Pos = onde, LastInputMs = NowMs(), Conta = ContaDaBancadaDeRochas, Slot = forjados.Count,
				Ficha = new Fighter
				{
					Name = nome, Race = "Human", BP = 1000, Idade = 25,
					physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
					kiskill = 5, speed = 5, magiskill = 5, maxstamina = 100, stamina = 100,
				},
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			c.Ficha.Class = "Normal";
			PorNoMundo(c);
			c.Ficha.PowerLevel();
			forjados.Add(c);
			return c;
		}

		// Os pacotes de rocha que sairam pra um corpo desde a ultima limpeza da escuta.
		List<byte[]> Fios(ServerPlayer p) => [.. EscutaDeRochas!.Where(e => e.Para == p.Id).Select(e => e.Fio)];

		static FioDeRocha Ler(byte[] fio)
		{
			var r = new NetDataReader(fio);
			r.GetByte();   // o opcode
			byte modo = r.GetByte();
			ulong zona = r.GetULong();
			var celulas = new List<(int X, int Y)>();
			int n = modo == Protocol.RochasRetrato ? r.GetUShort() : 1;
			for (int i = 0; i < n; i++) celulas.Add((r.GetUShort(), r.GetUShort()));
			return new FioDeRocha(modo, zona, celulas);
		}

		// Um corpo com os pes no meio da celula.
		static Vec2 NaCelula(int cx, int cy) => RochaDeMinerio.Ponto(cx, cy);

		EscutaDeRochas = [];
		try
		{
			// ---------------------------------------------------------------- 1. a regra
			GD.Print("[rochas] -- 1) a regra: teto pelo tamanho, alvo pela reserva, faixa do rendimento --");
			AfirmarRo("o TETO sai do tamanho: uma rocha a cada 400 celulas de terra (150 mil celulas = 375 rochas)",
					  RochaDeMinerio.Teto(150_000) == 375 && RochaDeMinerio.Teto(399) == 0 && RochaDeMinerio.Teto(-5) == 0);
			AfirmarRo("**O ALVO SEGUE A RESERVA**: cheia = o teto, meia = a metade, zerada = NENHUMA",
					  RochaDeMinerio.Alvo(100, 1) == 100 && RochaDeMinerio.Alvo(100, 0.5) == 50 && RochaDeMinerio.Alvo(100, 0) == 0,
					  $"{RochaDeMinerio.Alvo(100, 1)} / {RochaDeMinerio.Alvo(100, 0.5)} / {RochaDeMinerio.Alvo(100, 0)}");
			AfirmarRo("...quase seca nao segura meia rocha (0,4% de 100 = zero), e a fracao nunca passa do teto",
					  RochaDeMinerio.Alvo(100, 0.004) == 0 && RochaDeMinerio.Alvo(100, 7) == 100 && RochaDeMinerio.Alvo(100, double.NaN) == 0);
			bool faixa = true;
			for (int i = 0; i < 1000; i++)
			{
				double r = RochaDeMinerio.Rendimento(i / 1000.0);
				faixa &= r >= RochaDeMinerio.RendimentoMinimo && r <= RochaDeMinerio.RendimentoMaximo && r == Math.Floor(r);
			}
			AfirmarRo("o RENDIMENTO e inteiro e fica na faixa (40 a 160), com as duas pontas alcancadas",
					  faixa && RochaDeMinerio.Rendimento(0) == RochaDeMinerio.RendimentoMinimo
					  && RochaDeMinerio.Rendimento(0.999999) == RochaDeMinerio.RendimentoMaximo && RochaDeMinerio.Rendimento(1) == RochaDeMinerio.RendimentoMaximo);
			RochaDeMinerio.IgnoraAReservaDeTeste = true;
			try { AfirmarRo("(defeito injetado: a reserva nao entra na conta) o planeta ZERADO continua com alvo cheio -- a regua sabe reprovar", RochaDeMinerio.Alvo(100, 0) == 100); }
			finally { RochaDeMinerio.IgnoraAReservaDeTeste = false; }

			// ---------------------------------------------------------------- 2. a rocha grande no gerador
			GD.Print("[rochas] -- 2) a rocha grande no gerador (Core) --");
			ARochaGrandeNoGerador();

			// ---------------------------------------------------------------- 3. a rocha grande no servidor
			GD.Print("[rochas] -- 3) a rocha grande num mundo sorteado, pelo servidor --");
			ServerPlayer viajante = Forjar("Viajante", ZonaDoEspaco, new Vec2(0, 0));
			ARochaGrandeNoServidor(viajante, Fios);

			// ---------------------------------------------------------------- 4. onde o minerio nasce
			GD.Print("[rochas] -- 4) onde o minerio nasce --");
			if (MapaDaZonaOuCatalogo(terra) is not { } mapaT) { AfirmarRo("a Terra tem mapa de colisao", false); return; }
			AfirmarRo("antes de alguem pisar na Terra nao ha rocha nenhuma (planeta sem ninguem nao e simulado)", RochasDeTeste(terra).Count == 0);

			Vec2 chegadaT = PontoDeNascimento(terra);
			ServerPlayer mineiro = Forjar("Mineiro", mirante, PontoDeNascimento(mirante));
			EscutaDeRochas.Clear();
			TickDasRochas();
			List<byte[]> noMirante = Fios(mineiro);
			AfirmarRo("**NO MIRANTE NAO NASCE ROCHA**: sem reserva (`ReservaDe` nula), sem campo -- e o retrato sai VAZIO",
					  ReservaDe(mirante) == null && RochasDeTeste(mirante).Count == 0
					  && noMirante.Count == 1 && Ler(noMirante[0]) is { Modo: Protocol.RochasRetrato, Celulas.Count: 0 } rm && rm.Zona == mirante.Hash,
					  $"{noMirante.Count} pacote(s)");
			EscutaDeRochas.Clear();
			TickDasRochas();
			AfirmarRo("...e ele sai UMA vez: parado no mesmo lugar, o tique nao manda de novo", Fios(mineiro).Count == 0);

			MoveToZone(mineiro.Id, terra, chegadaT);
			EscutaDeRochas.Clear();
			TickDasRochas();
			HashSet<(int X, int Y)> naTerra = [.. RochasDeTeste(terra)];
			int tetoT = TetoDeRochasDeTeste(terra);
			Habitat? habitatT = HabitatDaZona(terra);
			AfirmarRo("**CHEGOU ALGUEM NA TERRA: AS ROCHAS NASCEM**, e o teto sai da terra em que se anda",
					  naTerra.Count > 0 && tetoT == RochaDeMinerio.Teto(habitatT?.Celulas ?? 0),
					  $"{naTerra.Count} rochas, teto {tetoT}, {habitatT?.Celulas ?? 0:N0} celulas navegaveis");

			int naAgua = 0, sobTeto = 0, semBarrar = 0, coladas = 0, foraDaTerra = 0, emCimaDeCoisa = 0, pertoDemais = 0, pertoDeGente = 0;
			(int mx, int my) = CelulaInterna.CelulaDoCorpo(mineiro.Pos);
			foreach ((int x, int y) in naTerra)
			{
				if (mapaT.EhAgua(x, y)) naAgua++;
				if (CelulaInterna.SobTeto(mapaT, x, y, caiu: false)) sobTeto++;
				if (!mapaT.TemRocha(x, y) || !mapaT.BlockedCell(x, y)) semBarrar++;
				if (habitatT != null && !habitatT.Navegavel(x, y)) foraDaTerra++;
				if (ObraNaCelula(terra, x, y) != null || BlocoEm(terra, x, y) != null) emCimaDeCoisa++;
				if (Math.Max(Math.Abs(x - mx), Math.Abs(y - my)) < RochaDeMinerio.DistanciaDeJogador) pertoDeGente++;
				bool anelLivre = true;
				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
					{
						if (dx == 0 && dy == 0) continue;
						anelLivre &= mapaT.ServeDeChao(x + dx, y + dy) && !CelulaInterna.SobTeto(mapaT, x + dx, y + dy, caiu: false);
					}
				if (!anelLivre) coladas++;
				int d = RochaDeMinerio.DistanciaEntreRochas - 1;
				for (int dy = -d; dy <= d; dy++)
					for (int dx = -d; dx <= d; dx++)
						if ((dx != 0 || dy != 0) && naTerra.Contains((x + dx, y + dy))) pertoDemais++;
			}
			AfirmarRo("**SO EM TERRA FIRME E A CEU ABERTO**: nenhuma na agua, nenhuma sob teto, nenhuma fora da terra em que se anda",
					  naAgua == 0 && sobTeto == 0 && foraDaTerra == 0, $"{naAgua} na agua, {sobTeto} sob teto, {foraDaTerra} fora do habitat");
			AfirmarRo("**AS OITO CELULAS EM VOLTA DE CADA UMA SAO CHAO LIVRE** (a rocha nunca tapa corredor, porta nem ponte)", coladas == 0, $"{coladas} coladas em alguma coisa");
			AfirmarRo($"nenhuma a menos de {RochaDeMinerio.DistanciaEntreRochas} tiles de outra", pertoDemais == 0, $"{pertoDemais / 2} pares perto demais");
			AfirmarRo("nenhuma em cima de obra ou de bloco de base", emCimaDeCoisa == 0, $"{emCimaDeCoisa}");
			AfirmarRo($"nenhuma a menos de {RochaDeMinerio.DistanciaDeJogador} tiles de quem acabou de chegar", pertoDeGente == 0, $"{pertoDeGente}");
			AfirmarRo("**TODA ROCHA BARRA** enquanto existe: esta na camada de rochas do mapa de colisao", semBarrar == 0, $"{semBarrar} sem barrar");

			// ---------------------------------------------------------------- 5. quantas
			GD.Print("[rochas] -- 5) quantas: cheia, meia e zerada --");
			ReservaDePlaneta reservaT = ReservaDe(terra)!;
			AfirmarRo("**RESERVA CHEIA: O PLANETA NASCE COM O TETO** (a primeira leva enche de uma vez)",
					  reservaT.Fracao >= 0.999 && naTerra.Count == tetoT, $"{naTerra.Count} de {tetoT}, reserva {reservaT.Fracao * 100:0}%");

			ReservaDePlaneta reservaN = ReservaDe(namek)!;
			reservasMexidas.Add(namek);
			reservaN.Tirar(reservaN.Atual - reservaN.Maximo / 2);
			ServerPlayer emNamek = Forjar("EmNamek", namek, PontoDeNascimento(namek));
			TickDasRochas();
			int tetoN = TetoDeRochasDeTeste(namek), deNamek = RochasDeTeste(namek).Count;
			AfirmarRo("**RESERVA PELA METADE: METADE DAS ROCHAS**",
					  tetoN > 4 && deNamek == RochaDeMinerio.Alvo(tetoN, 0.5) && deNamek <= tetoN / 2 + 1 && deNamek >= tetoN / 2 - 1,
					  $"{deNamek} de teto {tetoN}, reserva {reservaN.Fracao * 100:0}%");

			ReservaDePlaneta reservaV = ReservaDe(vegeta)!;
			reservasMexidas.Add(vegeta);
			reservaV.Tirar(reservaV.Atual);
			ServerPlayer emVegeta = Forjar("EmVegeta", vegeta, PontoDeNascimento(vegeta));
			TickDasRochas();
			AfirmarRo("**RESERVA ZERADA (a Drill secou o planeta): NENHUMA ROCHA NASCE**",
					  reservaV.Atual == 0 && TetoDeRochasDeTeste(vegeta) > 4 && RochasDeTeste(vegeta).Count == 0,
					  $"{RochasDeTeste(vegeta).Count} de teto {TetoDeRochasDeTeste(vegeta)}");
			AdiantarRochasDeTeste(vegeta, 3 * 3600 * 1000L);
			TickDasRochas();
			AfirmarRo("...e tres horas de relogio depois continua sem nenhuma: zerado, o planeta PARA de parir", RochasDeTeste(vegeta).Count == 0);

			RochaDeMinerio.IgnoraAReservaDeTeste = true;
			try
			{
				AdiantarRochasDeTeste(vegeta, 3 * 3600 * 1000L);
				TickDasRochas();
				AfirmarRo("(defeito injetado: a reserva nao entra na conta) o planeta zerado volta a parir rocha -- a regua sabe reprovar",
						  RochasDeTeste(vegeta).Count > 0, $"{RochasDeTeste(vegeta).Count} rochas");
			}
			finally { RochaDeMinerio.IgnoraAReservaDeTeste = false; }

			// ---------------------------------------------------------------- 6. colher
			GD.Print("[rochas] -- 6) colher: tira da reserva, paga, some --");
			(int X, int Y) alvo = naTerra.OrderBy(c => Math.Abs(c.X - mx) + Math.Abs(c.Y - my)).First();
			mineiro.Pos = NaCelula(alvo.X - 10, alvo.Y);
			double antesReserva = reservaT.Atual, antesSaldo = mineiro.Ficha.Recursos;
			List<string> falas = Ouvir(() => Verbo(mineiro, RochaDeMinerio.Verbo, ""));
			AfirmarRo("LONGE (10 tiles): o servidor recusa -- nem reserva, nem saldo, nem rocha mudam",
					  reservaT.Atual == antesReserva && mineiro.Ficha.Recursos == antesSaldo && RochasDeTeste(terra).Contains(alvo) && Disse(falas, "por perto"),
					  Ultimos(falas));

			mineiro.Pos = NaCelula(alvo.X - 1, alvo.Y);
			AfirmarRo("a um tile da rocha o corpo ESTA ao alcance (a conta do Core, a mesma do menu)", RochaDeMinerio.AoAlcance(mineiro.Pos, alvo.X, alvo.Y));
			mineiro.Ficha.KO = true;
			falas = Ouvir(() => Verbo(mineiro, RochaDeMinerio.Verbo, ""));
			mineiro.Ficha.KO = false;
			AfirmarRo("NOCAUTEADO nao colhe", RochasDeTeste(terra).Contains(alvo) && reservaT.Atual == antesReserva && Disse(falas, "condição"), Ultimos(falas));

			RendimentoDaRochaDeTeste = 0.5;   // 40 + piso(0,5 x 121) = 100
			EscutaDeRochas.Clear();
			falas = Ouvir(() => Verbo(mineiro, RochaDeMinerio.Verbo, ""));
			AfirmarRo("**A COLETA TIRA DA RESERVA DO PLANETA E PAGA AO JOGADOR** a mesma quantia (100, com o sorteio da bancada)",
					  Math.Abs(antesReserva - reservaT.Atual - 100) < 1e-6 && Math.Abs(mineiro.Ficha.Recursos - antesSaldo - 100) < 1e-6,
					  $"reserva {antesReserva:0} -> {reservaT.Atual:0}, saldo {antesSaldo:0} -> {mineiro.Ficha.Recursos:0}");
			AfirmarRo("...o aviso diz quanto veio", Disse(falas, "100 de recursos"), Ultimos(falas));
			AfirmarRo("**E A ROCHA SOME**: do planeta e do mapa de colisao (a celula volta a ser chao)",
					  !RochasDeTeste(terra).Contains(alvo) && !mapaT.TemRocha(alvo.X, alvo.Y) && !mapaT.BlockedCell(alvo.X, alvo.Y));
			List<byte[]> aoColher = Fios(mineiro);
			AfirmarRo("...e quem esta no planeta fica sabendo: UM pacote de 14 bytes, `Sumiu`, com a zona e a celula",
					  aoColher.Count == 1 && aoColher[0].Length == 14 && Ler(aoColher[0]) is { Modo: Protocol.RochasSumiu } s1 && s1.Zona == terra.Hash && s1.Celulas[0] == alvo,
					  $"{aoColher.Count} pacote(s), {(aoColher.Count > 0 ? aoColher[0].Length : 0)} bytes");
			falas = Ouvir(() => Verbo(mineiro, RochaDeMinerio.Verbo, ""));
			AfirmarRo("uma coleta por rocha: de novo no mesmo lugar, nao ha mais o que colher", Disse(falas, "por perto") && Math.Abs(mineiro.Ficha.Recursos - antesSaldo - 100) < 1e-6, Ultimos(falas));

			// A reserva quase seca: o que resta e o que sai.
			(int X, int Y) segunda = RochasDeTeste(terra).OrderBy(c => Math.Abs(c.X - alvo.X) + Math.Abs(c.Y - alvo.Y)).First();
			reservasMexidas.Add(terra);
			reservaT.Tirar(reservaT.Atual - 30);
			mineiro.Pos = NaCelula(segunda.X + 1, segunda.Y);
			double saldo2 = mineiro.Ficha.Recursos;
			falas = Ouvir(() => Verbo(mineiro, RochaDeMinerio.Verbo, ""));
			AfirmarRo("**PLANETA QUASE SECO: A ROCHA RENDE SO O QUE RESTAVA** (pediu 100, a reserva tinha 30) -- `Tirar` e a porta unica",
					  reservaT.Atual == 0 && Math.Abs(mineiro.Ficha.Recursos - saldo2 - 30) < 1e-6 && Disse(falas, "restava") && !RochasDeTeste(terra).Contains(segunda),
					  Ultimos(falas));
			(int X, int Y) terceira = RochasDeTeste(terra).OrderBy(c => Math.Abs(c.X - segunda.X) + Math.Abs(c.Y - segunda.Y)).First();
			mineiro.Pos = NaCelula(terceira.X, terceira.Y + 1);
			double saldo3 = mineiro.Ficha.Recursos;
			falas = Ouvir(() => Verbo(mineiro, RochaDeMinerio.Verbo, ""));
			AfirmarRo("PLANETA ZERADO: a rocha que sobrou era so casca -- some, e nao rende nada",
					  mineiro.Ficha.Recursos == saldo3 && Disse(falas, "sem recursos") && !RochasDeTeste(terra).Contains(terceira), Ultimos(falas));
			int dePeNaSeca = RochasDeTeste(terra).Count;
			AdiantarRochasDeTeste(terra, 3600 * 1000L);
			TickDasRochas();
			AfirmarRo("...e as outras que ja estavam de pe FICAM (a reserva caiu, nada some sozinho) -- mas nenhuma nova nasce",
					  RochasDeTeste(terra).Count == dePeNaSeca && dePeNaSeca > 0, $"{dePeNaSeca} de pe, alvo {RochaDeMinerio.Alvo(tetoT, reservaT.Fracao)}");

			// ---------------------------------------------------------------- 7. a rocha de minerio barra
			GD.Print("[rochas] -- 7) a rocha de minerio barra; o soco e o cenario que cai nao a levam --");
			(int X, int Y) quarta = RochasDeTeste(terra).OrderBy(c => Math.Abs(c.X - terceira.X) + Math.Abs(c.Y - terceira.Y)).First();
			Vec2 aOeste = NaCelula(quarta.X - 1, quarta.Y);   // a celula ao lado dela e chao livre, por regra
			Vec2 Andar(Vec2 de)
			{
				Vec2 p = de;
				for (int i = 0; i < 90; i++) p = MoveRules.Advance(p, new Vec2(1, 0), Protocol.TickSeconds, 1f, mapaT, out _);
				return p;
			}
			Vec2 parou = Andar(aOeste);
			AfirmarRo("**ANDANDO CONTRA A ROCHA O CORPO PARA NELA**: tres segundos de passo e ele encosta e nao entra na celula",
					  parou.X + MoveRules.BodyHalfW <= quarta.X * ZoneCollision.TileSize + 0.01f && parou.X > aOeste.X + 1 && !MoveRules.Occupied(mapaT, parou),
					  $"saiu de x={aOeste.X:0}, parou em x={parou.X:0}; a rocha comeca em x={quarta.X * ZoneCollision.TileSize}");
			AfirmarRo("...quem VOA por cima do cenario passa (como passa por arvore e montanha), e a pe a celula barra",
					  !mapaT.Bloqueia(quarta.X, quarta.Y, ModoDeTravessia.PorCima) && mapaT.Bloqueia(quarta.X, quarta.Y, ModoDeTravessia.APe));

			mineiro.Pos = NaCelula(quarta.X - 1, quarta.Y);
			mineiro.Facing = Facing.East;
			mineiro.Ficha.BP = 5_000_000;
			mineiro.Ficha.PowerLevel();
			bool cedeu = false;
			falas = Ouvir(() => { for (int i = 0; i < 40; i++) SocarCenario(mineiro); });
			cedeu = Disse(falas, "cede");
			AfirmarRo("**O SOCO NAO A QUEBRA** (40 socos de quem derruba parede): ela fica, o chao debaixo nao racha, e o aviso ensina a tecla E",
					  RochasDeTeste(terra).Contains(quarta) && mapaT.BlockedCell(quarta.X, quarta.Y) && !cedeu && Disse(falas, "aperte E"),
					  falas.Count > 0 ? falas[0] : "sem fala");
			AfirmarRo("**O CENARIO QUE CAI NAO A LEVA**: o funil de toda destruicao (`DerrubarCelula`: raio, arremesso, chao que racha) recusa a celula dela",
					  !DerrubarCelula(terra, quarta.X, quarta.Y) && RochasDeTeste(terra).Contains(quarta) && mapaT.BlockedCell(quarta.X, quarta.Y));

			AfirmarRo("**NAO SE CONSTROI EM CIMA DELA**: a regra de base (a do fantasma e a do servidor) recusa a celula como cenario de pe -- colha primeiro",
					  RegrasDeBloco.DoTerreno(mapaT, mineiro.Pos, quarta.X, quarta.Y, caiu: false) == RecusaDeBloco.Cenario
					  && RegrasDeBloco.DoTerreno(mapaT, mineiro.Pos, quarta.X - 1, quarta.Y, caiu: false) == RecusaDeBloco.Pode,
					  $"{RegrasDeBloco.DoTerreno(mapaT, mineiro.Pos, quarta.X, quarta.Y, caiu: false)}");

			RochaDeMinerio.SemColisaoDeTeste = true;
			try
			{
				(int X, int Y) solta = (quarta.X - 40, quarta.Y);
				for (int t = 0; t < 400 && !(RochaDeMinerio.ChaoServe(mapaT, solta.X, solta.Y) && mapaT.ServeDeChao(solta.X - 2, solta.Y)); t++)
					solta = (solta.X - 1, solta.Y);
				PorRochaDeTeste(terra, solta.X, solta.Y);
				Vec2 passou = Andar(NaCelula(solta.X - 2, solta.Y));
				AfirmarRo("(defeito injetado: a rocha e so desenho) o corpo ATRAVESSA a celula dela -- a regua sabe reprovar",
						  RochasDeTeste(terra).Contains(solta) && passou.X > (solta.X + 1) * ZoneCollision.TileSize,
						  $"rocha em x={solta.X * ZoneCollision.TileSize}, o corpo foi ate x={passou.X:0}");
				TirarRochaDeTeste(terra, solta.X, solta.Y);
			}
			finally { RochaDeMinerio.SemColisaoDeTeste = false; }

			// ---------------------------------------------------------------- 8. a reposicao
			GD.Print("[rochas] -- 8) a reposicao: o relogio, o teto dele e o planeta sem ninguem --");
			reservaT.Regenerar(Recurso.HorasPraEncher * 3600);   // a reserva volta a cheia: o alvo volta ao teto
			int antes = RochasDeTeste(terra).Count;
			double porRocha = RochaDeMinerio.SegundosPorRocha(tetoT);
			AfirmarRo("ha rochas faltando (as colhidas) e a reserva esta cheia de novo", antes < tetoT && reservaT.Fracao >= 0.999, $"{antes} de {tetoT}");
			TickDasRochas();
			EscutaDeRochas.Clear();
			AdiantarRochasDeTeste(terra, (long)(porRocha * 1000 * 0.5));
			TickDasRochas();
			AfirmarRo("meio intervalo de relogio: NENHUMA rocha nova (a reposicao e lenta)", RochasDeTeste(terra).Count == antes, $"intervalo {porRocha:0.0} s");
			AdiantarRochasDeTeste(terra, (long)(porRocha * 1000 * 0.6));
			TickDasRochas();
			List<byte[]> aoNascer = Fios(mineiro);
			AfirmarRo("**PASSOU UM INTERVALO: NASCE UMA** -- e quem esta no planeta recebe o `Nasceu` dela",
					  RochasDeTeste(terra).Count == antes + 1 && aoNascer.Count == 1 && Ler(aoNascer[0]) is { Modo: Protocol.RochasNasceu } nasc
					  && nasc.Zona == terra.Hash && RochasDeTeste(terra).Contains(nasc.Celulas[0]),
					  $"{antes} -> {RochasDeTeste(terra).Count}, {aoNascer.Count} pacote(s)");
			AdiantarRochasDeTeste(terra, 10 * 3600 * 1000L);
			TickDasRochas();
			AfirmarRo("**DEZ HORAS DEPOIS O PLANETA ESTA NO TETO, E NAO ACIMA**",
					  RochasDeTeste(terra).Count == tetoT && RochaDeMinerio.Alvo(tetoT, reservaT.Fracao) == tetoT, $"{RochasDeTeste(terra).Count} de {tetoT}");

			// Tres coletas, e todo mundo sai da Terra.
			RendimentoDaRochaDeTeste = 0;
			for (int i = 0; i < 3; i++)
			{
				(int X, int Y) c = RochasDeTeste(terra).First();
				mineiro.Pos = NaCelula(c.X - 1, c.Y);
				Verbo(mineiro, RochaDeMinerio.Verbo, "");
			}
			int aoSair = RochasDeTeste(terra).Count;
			MoveToZone(mineiro.Id, mirante, PontoDeNascimento(mirante));
			TickDasRochas();
			AdiantarRochasDeTeste(terra, 10 * 3600 * 1000L);
			TickDasRochas();
			AfirmarRo("**PLANETA SEM NINGUEM NAO E SIMULADO**: dez horas de relogio e as tres colhidas continuam faltando",
					  aoSair == tetoT - 3 && RochasDeTeste(terra).Count == aoSair, $"{RochasDeTeste(terra).Count} de {tetoT}");

			// ---------------------------------------------------------------- 9. o fio
			GD.Print("[rochas] -- 9) o fio: o retrato de quem chega, e o que muda depois --");
			MoveToZone(mineiro.Id, terra, chegadaT);
			EscutaDeRochas.Clear();
			TickDasRochas();
			List<byte[]> naVolta = Fios(mineiro);
			HashSet<(int X, int Y)> agora = [.. RochasDeTeste(terra)];
			AfirmarRo("**A CONTA SE ACERTA QUANDO ALGUEM CHEGA**: o planeta volta ao que a reserva sustenta (o teto: tres coletas nao custam uma rocha)",
					  agora.Count == RochaDeMinerio.Alvo(tetoT, reservaT.Fracao) && agora.Count == tetoT, $"{agora.Count} de {tetoT}, reserva {reservaT.Fracao * 100:0.00}%");
			AfirmarRo("**QUEM CHEGA RECEBE O RETRATO**: um pacote, a zona dentro, e exatamente as rochas de pe (4 bytes cada)",
					  naVolta.Count == 1 && Ler(naVolta[0]) is { Modo: Protocol.RochasRetrato } ret && ret.Zona == terra.Hash
					  && ret.Celulas.Count == agora.Count && ret.Celulas.All(agora.Contains) && naVolta[0].Length == 12 + 4 * agora.Count,
					  $"{naVolta.Count} pacote(s), {(naVolta.Count > 0 ? naVolta[0].Length : 0)} bytes pra {agora.Count} rochas");

			ServerPlayer vizinho = Forjar("Vizinho", terra, chegadaT + new Vec2(ZoneCollision.TileSize, 0));
			TickDasRochas();
			EscutaDeRochas.Clear();
			(int X, int Y) ultima = RochasDeTeste(terra).First();
			mineiro.Pos = NaCelula(ultima.X, ultima.Y - 1);
			Verbo(mineiro, RochaDeMinerio.Verbo, "");
			AfirmarRo("a coleta de um chega aos DOIS que estao na Terra, e a NINGUEM de outro planeta",
					  Fios(mineiro).Count == 1 && Fios(vizinho).Count == 1 && Fios(emNamek).Count == 0 && Fios(emVegeta).Count == 0,
					  $"mineiro {Fios(mineiro).Count}, vizinho {Fios(vizinho).Count}, Namek {Fios(emNamek).Count}, Vegeta {Fios(emVegeta).Count}");
			ServerPlayer tardio = Forjar("Tardio", terra, chegadaT + new Vec2(0, ZoneCollision.TileSize));
			EscutaDeRochas.Clear();
			TickDasRochas();
			List<byte[]> doTardio = Fios(tardio);
			AfirmarRo("quem chega DEPOIS recebe as que existem agora: a colhida ja nao esta no retrato dele",
					  doTardio.Count == 1 && Ler(doTardio[0]) is { Modo: Protocol.RochasRetrato } rt && !rt.Celulas.Contains(ultima)
					  && rt.Celulas.Count == RochasDeTeste(terra).Count,
					  $"{(doTardio.Count > 0 ? Ler(doTardio[0]).Celulas.Count : -1)} rochas no retrato");
		}
		catch (Exception e)
		{
			AfirmarRo("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			RochaGrande.SemPegadaDeTeste = false;
			RochaDeMinerio.IgnoraAReservaDeTeste = false;
			RochaDeMinerio.SemColisaoDeTeste = false;
			PresoNaRochaDeTeste = false;
			RendimentoDaRochaDeTeste = null;
			foreach (ServerPlayer c in forjados) RemoverNpc(c);
			foreach (ZoneKey z in new[] { terra, namek, vegeta }) EsquecerRochasDeTeste(z);
			foreach (ZoneKey z in reservasMexidas) ReservaDe(z)?.Regenerar(Recurso.HorasPraEncher * 3600);
			EscutaDeRochas = null;

			// O PLACAR SAI DAQUI: a bancada tem saidas no meio, e sem placar o lancador espera o prazo inteiro.
			GD.Print($"[rochas] ================ {_roOk} OK, {_roFalhou} FALHA(S) ================");
		}
	}

	/// <summary>Um pacote de rocha, lido do fio pela bancada.</summary>
	private readonly record struct FioDeRocha(byte Modo, ulong Zona, List<(int X, int Y)> Celulas);

	// =====================================================================
	// 2. A ROCHA GRANDE NO GERADOR
	// =====================================================================
	/// <summary>
	/// A PEGADA, MEDIDA NO PRODUTO DO GERADOR -- doze mundos (os seis biomas, duas sementes), a funcao que o
	/// cliente e o servidor chamam. O contra-exemplo regenera o MESMO mundo com a pegada desligada e manda o
	/// mesmo corpo andar pela mesma reta.
	/// </summary>
	private void ARochaGrandeNoGerador()
	{
		int mundos = 0, rochas = 0, furos = 0, durasFora = 0, topoTomado = 0, pousosPresos = 0, pousosCercados = 0, instaveis = 0;
		TerrenoGerado? exemplo = null;
		(int X, int Y) ancora = default;
		ParametrosDeTerreno? doExemplo = null;

		foreach (BiomaDeTerreno bioma in Enum.GetValues<BiomaDeTerreno>())
			for (ulong s = 1; s <= 2; s++)
			{
				var p = new ParametrosDeTerreno { Seed = 7919UL * s + (ulong)bioma * 104729UL, Largura = 192, Altura = 192, Bioma = bioma, Gravidade = 1 };
				TerrenoGerado t = GeradorDeTerreno.Gerar(p);
				mundos++;
				if (t.Assinatura() != GeradorDeTerreno.Gerar(p).Assinatura()) instaveis++;

				for (int y = 0; y < t.Altura; y++)
					for (int x = 0; x < t.Largura; x++)
					{
						bool coberta = RochaGrande.Cobre(t.Largura, t.Altura, t.Cobertura, x, y);
						// "DURO" SO EXISTE DEBAIXO DE PEDRA: o plano do que nao se quebra de um mundo gerado e a
						// uniao das pegadas, e mais nada.
						if (t.Colisao.Indestrutivel(x, y) != coberta) durasFora++;
						if (coberta && !t.Colisao.BlockedCell(x, y)) furos++;

						if (t.CoberturaEm(x, y) != CoberturaDeTerreno.RochaGrande) continue;
						rochas++;
						// O TOPO (as duas fileiras acima da pegada) so barra se OUTRA coisa o tomar.
						for (int dx = -3; dx <= 3; dx++)
							for (int dy = -3; dy <= -2; dy++)
								if (t.Dentro(x + dx, y + dy) && t.Colisao.Indestrutivel(x + dx, y + dy)
									&& !RochaGrande.Cobre(t.Largura, t.Altura, t.Cobertura, x + dx, y + dy)) topoTomado++;

					}

				// Um exemplo pra andar: uma rocha com a reta da coluna dela livre (ver `RochaGrandeComRetaLivre`).
				if (exemplo == null && RochaGrandeComRetaLivre(t) is { } comReta) { exemplo = t; ancora = comReta; doExemplo = p; }

				if (MoveRules.Occupied(t.Colisao, t.Spawn)) pousosPresos++;
				for (int dy = -1; dy <= 1; dy++)
					for (int dx = -1; dx <= 1; dx++)
						if (t.Colisao.BlockedCell(t.SpawnCelX + dx, t.SpawnCelY + dy) && t.ClasseEm(t.SpawnCelX + dx, t.SpawnCelY + dy) != ClasseDeTerreno.Montanha)
							pousosCercados++;
			}

		AfirmarRo("ha rochas grandes nos mundos gerados (doze mundos de 192, os seis biomas)", rochas > 0, $"{rochas} rochas em {mundos} mundos");
		AfirmarRo("**TODA CELULA DA PEGADA BARRA** (onze por rocha: a fileira de cima da ancora inteira e o miolo da fileira dela)", furos == 0, $"{furos} celulas de pegada abertas");
		AfirmarRo("**E NAO SE QUEBRA** -- e fora das pegadas nada e duro: o plano e a uniao delas, celula por celula", durasFora == 0, $"{durasFora} celulas divergentes");
		AfirmarRo("**O TOPO FICA LIVRE**: as duas fileiras do alto do desenho nao entram na pegada (anda-se por tras da pedra)", topoTomado == 0, $"{topoTomado}");
		AfirmarRo("**NINGUEM POUSA PRESO**: o ponto de chegada dos doze mundos esta livre, e as oito celulas em volta tambem (menos montanha, que a clareira nao derruba)",
				  pousosPresos == 0 && pousosCercados == 0, $"{pousosPresos} presos, {pousosCercados} vizinhas tomadas");
		AfirmarRo("a geracao continua pura: a mesma semente da a mesma assinatura", instaveis == 0, $"{instaveis} divergiram");

		if (exemplo == null || doExemplo == null) { AfirmarRo("achei uma rocha grande com chao livre na frente e atras pra andar contra ela", false); return; }

		// O corpo sobe de tres celulas abaixo da ancora, em linha reta, por quatro segundos.
		Vec2 partida = RochaDeMinerio.Ponto(ancora.X, ancora.Y + 3);
		float Subir(ZoneCollision mapa, out bool entrou)
		{
			Vec2 pos = partida;
			entrou = false;
			for (int i = 0; i < 120; i++)
			{
				pos = MoveRules.Advance(pos, new Vec2(0, -1), Protocol.TickSeconds, 1f, mapa, out _);
				entrou |= MoveRules.Occupied(mapa, pos);
			}
			return pos.Y + MoveRules.FeetOffsetY;
		}

		float pes = Subir(exemplo.Colisao, out bool dentro);
		float baseDaPedra = (ancora.Y + 1) * ZoneCollision.TileSize;
		AfirmarRo("**ANDANDO CONTRA A ROCHA GRANDE O CORPO PARA NA BASE DELA**: quatro segundos de passo e os pes nao passam do pe da ancora",
				  !dentro && pes - MoveRules.BodyHalfH >= baseDaPedra - 0.01f && pes < baseDaPedra + ZoneCollision.TileSize,
				  $"pes em y={pes:0}, a base da pedra em y={baseDaPedra:0}");
		AfirmarRo("...quem voa por cima do cenario passa, e a pe a ancora barra",
				  !exemplo.Colisao.Bloqueia(ancora.X, ancora.Y, ModoDeTravessia.PorCima) && exemplo.Colisao.Bloqueia(ancora.X, ancora.Y, ModoDeTravessia.APe));

		RochaGrande.SemPegadaDeTeste = true;
		try
		{
			TerrenoGerado antigo = GeradorDeTerreno.Gerar(doExemplo);
			float pesDeAntes = Subir(antigo.Colisao, out _);
			AfirmarRo("(defeito injetado: a rocha grande e so desenho) o mesmo corpo ATRAVESSA a pedra inteira -- a regua sabe reprovar",
					  pesDeAntes < (ancora.Y - 1) * ZoneCollision.TileSize && !antigo.Colisao.BlockedCell(ancora.X, ancora.Y),
					  $"pes em y={pesDeAntes:0}, do outro lado da pedra");
		}
		finally { RochaGrande.SemPegadaDeTeste = false; }
	}

	// =====================================================================
	// 3. A ROCHA GRANDE NO SERVIDOR
	// =====================================================================
	/// <summary>
	/// O MUNDO SORTEADO DE VERDADE: o corpo pousa pelo funil de producao (`PousarDeVerdade`, da bancada das
	/// obras), e o mapa que o servidor guardou e comparado com o que o cliente geraria da mesma seed.
	/// </summary>
	private void ARochaGrandeNoServidor(ServerPlayer pl, Func<ServerPlayer, List<byte[]>> fios)
	{
		// Um mundo sorteado COM rocha grande -- os primeiros do universo podem nao ter nenhuma.
		PlanetaNoEspaco? achado = null;
		TerrenoGerado? doCliente = null;
		for (int i = 1; i <= 600 && achado == null; i++)
			foreach (PlanetaNoEspaco p in Espaco.PorPerto(SeedDoUniverso, new ChunkId(i * 37, i * 53)))
			{
				if (p.Premade || PlanetaMorto(p)) continue;
				MundoProcedural ficha = MundoProcedural.DaSeed(p.Seed, p.Nome);
				if (ficha.Lado > 400) continue;   // a bancada nao precisa de um mundo de um milhao de celulas
				TerrenoGerado t = GeradorDeTerreno.Gerar(ficha.Parametros());
				if (!t.Cobertura.Contains((byte)CoberturaDeTerreno.RochaGrande)) continue;
				achado = p;
				doCliente = t;
				break;
			}
		if (achado is not { } mundo || doCliente == null) { AfirmarRo("ha um mundo sorteado com rocha grande no universo de producao", false); return; }

		bool pousou = PousarDeVerdade(pl, mundo);
		ZoneKey zona = Espaco.ZonaDe(mundo);
		ZonaGerada? viva = MundoDaZona(zona);
		AfirmarRo($"o corpo POUSOU em {mundo.Nome} pelo funil de producao, e o servidor guardou o mundo", pousou && viva != null && pl.Zone.Equals(zona));
		if (viva == null) return;
		ZoneCollision mapa = viva.Colisao;
		AfirmarRo("**AS DUAS PONTAS GERAM A MESMA PEDRA**: a assinatura do servidor e a do cliente (mesma seed) batem",
				  viva.Assinatura == doCliente.Assinatura(), $"{viva.Assinatura:X16} x {doCliente.Assinatura():X16}");

		(int X, int Y) ancora = default;
		int rochas = 0, furos = 0;
		for (int y = 0; y < doCliente.Altura; y++)
			for (int x = 0; x < doCliente.Largura; x++)
			{
				if (doCliente.CoberturaEm(x, y) != CoberturaDeTerreno.RochaGrande) continue;
				rochas++;
				foreach ((int dx, int dy) in RochaGrande.Pegada)
					if (doCliente.Dentro(x + dx, y + dy) && !(mapa.BlockedCell(x + dx, y + dy) && mapa.Indestrutivel(x + dx, y + dy))) furos++;
				if (ancora == default && x > 8 && y > 8 && x < doCliente.Largura - 8 && y < doCliente.Altura - 8) ancora = (x, y);
			}
		AfirmarRo("**NO MAPA DO SERVIDOR A PEGADA DE TODA ROCHA GRANDE BARRA E NAO SE QUEBRA** (e ele quem valida o passo)", rochas > 0 && furos == 0, $"{rochas} rochas, {furos} celulas abertas");
		AfirmarRo("o funil de toda destruicao recusa a celula da pedra (raio, arremesso, chao que racha): ela continua de pe",
				  !DerrubarCelula(zona, ancora.X, ancora.Y) && mapa.BlockedCell(ancora.X, ancora.Y));

		// QUEM CHEGA COM OS PES DENTRO DA PEDRA -- o personagem gravado ali antes de ela ter pegada.
		Vec2 dentro = RochaDeMinerio.Ponto(ancora.X, ancora.Y - 1);
		PresoNaRochaDeTeste = true;
		try
		{
			pl.Pos = dentro;
			_rochasVistas.Remove(pl);
			TickDasRochas();
			AfirmarRo("(defeito injetado: quem chega na pedra fica nela) o corpo continua com os pes dentro da rocha -- a regua sabe reprovar",
					  MoveRules.Occupied(mapa, pl.Pos) && (pl.Pos - dentro).Length < 0.01f);
		}
		finally { PresoNaRochaDeTeste = false; }

		pl.Pos = dentro;
		_rochasVistas.Remove(pl);
		List<string> falas = Ouvir(TickDasRochas);
		float andou = (pl.Pos - dentro).Length / ZoneCollision.TileSize;
		AfirmarRo("**QUEM CHEGA COM OS PES DENTRO DA PEDRA E POSTO AO LADO DELA**, pelo tique de producao, e avisado",
				  !MoveRules.Occupied(mapa, pl.Pos) && andou > 0 && andou <= MoveRules.RaioDoEscape + 1 && Disse(falas, "rocha"),
				  $"movido {andou:0.0} tiles; {Ultimos(falas)}");

		// E AS ROCHAS DE MINERIO NASCEM NESTE MUNDO TAMBEM: ele e planeta, e tem reserva.
		IReadOnlyCollection<(int X, int Y)> minerio = RochasDeTeste(zona);
		int teto = TetoDeRochasDeTeste(zona);
		int emPedra = minerio.Count(c => RochaGrande.Cobre(doCliente.Largura, doCliente.Altura, doCliente.Cobertura, c.X, c.Y));
		AfirmarRo("**O MUNDO SORTEADO TAMBEM TEM ROCHA DE MINERIO** (tem reserva): nasce o teto dele, nenhuma debaixo de rocha grande",
				  ReservaDe(zona) != null && teto > 0 && minerio.Count == teto && emPedra == 0, $"{minerio.Count} de {teto}; {emPedra} em pedra");
		List<byte[]> retratos = fios(pl);
		AfirmarRo("...e quem pousou recebeu o retrato delas", retratos.Count > 0 && retratos[^1].Length == 12 + 4 * minerio.Count, $"{retratos.Count} pacote(s)");

		EsquecerRochasDeTeste(zona);
		MoveToZone(pl.Id, ZoneKey.Premade("Lookout"), PontoDeNascimento(ZoneKey.Premade("Lookout")));
	}
}
