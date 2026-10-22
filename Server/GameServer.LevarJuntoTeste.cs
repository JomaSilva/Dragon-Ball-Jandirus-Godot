using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// QUEM E CARREGADO VAI JUNTO (`--levarjuntoteste`). A passagem, a Capital Ship, o pouso da nuvem
/// e a Sala levam os dois; o espaco, o pod e a esfera comum na porta da nave nao. Cada regra tem
/// o defeito injetado na propria linha, e a mesma regua reprova com ele ligado.
/// </summary>
public sealed partial class GameServer
{
	private bool _levarJuntoDeTeste;
	private int _ljOk, _ljFalhou;

	/// <summary>Fora das faixas das outras bancadas (a saga do Majin usa 94_700).</summary>
	private const int IdBaseDoLevarJunto = 94_160;

	private void AfirmarLj(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ljOk++; GD.Print($"[levarjunto]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _ljFalhou++; GD.PrintErr($"[levarjunto]   FALHA {nome}   [{detalhe}]"); }
	}

	private void RodarBancadaDoLevarJunto(ServerPlayer host)
	{
		_ljOk = _ljFalhou = 0;
		GD.Print("[levarjunto] ================ QUEM E CARREGADO VAI JUNTO (menos o espaco) ================");
		var forjados = new List<ServerPlayer>();
		var naves = new List<Nave>();
		var esferas = new List<Esfera>();
		try
		{
			ZoneKey terra = ZoneKey.Premade("Earth");
			Vec2 chao = PontoDeNascimento(terra);
			ServerPlayer leva = ForjarLj(forjados, "Leva", terra, chao);
			ServerPlayer carga = ForjarLj(forjados, "Carga", terra, chao);
			Passagem? praCaverna = _passagens.GetValueOrDefault("Earth")?.Find(p => p.Zona == "Earth_Cave");
			AfirmarLj("(montagem) a Terra tem boca pra caverna e os dois corpos agarram",
				praCaverna != null && CarregarLj(leva, carga),
				$"boca {(praCaverna == null ? "nenhuma" : praCaverna.Zona)}; modo {leva.ModoDoAgarrao}; agarra {leva.AgarrandoId}");
			if (praCaverna == null || leva.AgarrandoId != carga.Id) return;

			ACavernaLevaOsDois(leva, carga, praCaverna, terra, chao);
			ANaveLevaESoltaNoEspaco(host, leva, carga, terra, chao, naves, esferas);
			OEspacoESoPodSoltam(leva, carga, terra, chao, naves);
			OPresoFicaEASalaNaoFura(leva, carga, terra, chao);
			ANuvemDoLookoutLeva(leva, carga, terra, chao);
			AZonaCruAindaSolta(leva, carga, terra, chao);
			OsDefeitosInjetados(host, leva, carga, praCaverna, terra, chao, naves, esferas);
		}
		finally
		{
			LevarJuntoNaoLevaDeTeste = false;
			LevarJuntoLevaProEspacoDeTeste = false;
			LevarJuntoLevaOPresoDeTeste = false;
			LevarJuntoIgnoraEsferaDeTeste = false;
			LevarJuntoFuraASalaDeTeste = false;
			LevarJuntoPodNaoAvisaDeTeste = false;
			foreach (Esfera e in esferas) _esferas.Remove(e);
			foreach (Nave n in naves) _naves.Remove(n);
			foreach (ServerPlayer f in forjados)
			{
				f.Peer = null;
				if (f.AgarrandoId != 0) Soltar(f, MotivoDaSoltura.Tecla);
				if (NaveDoPiloto(f) is { } n) Desembarcar(f, n);
				EsquecerPassagem(f.Id);
				if (f.Assinatura.Length > 0) _naSala.Remove(f.Assinatura);
				f.SalaPlaneta = "";
				f.SalaPreso = false;
				f.SalaAutorizada = false;
				f.SalaUltimaEntrada = 0;
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
			}
			GD.Print($"[levarjunto] ================ {_ljOk} OK, {_ljFalhou} FALHA(S) ================");
		}
	}

	private ServerPlayer ForjarLj(List<ServerPlayer> forjados, string nome, ZoneKey zona, Vec2 onde)
	{
		var f = new Jandirus.Core.Stats.Fighter
		{
			Name = nome, Race = "Human", BP = 5_000,
			physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
			kiskill = 5, speed = 5, magiskill = 5, Idade = 25,
			maxstamina = 100, stamina = 100,
			// A SALA DO TEMPO PESA 10x: com a maestria de fabrica (1) a razao la dentro e 10, acima do 4 que PRENDE o corpo
			// (`Esmagamento.RazaoQuePrende`), e o `PodeMexerOCorpo` recusa ate o agarrar ("não dá pra agarrar ninguém assim").
			// A c1 mediu isso na familia D. Quem entra na Sala de verdade aguenta ficar de pe la.
			GravMastered = 20,
		};
		f.Statify();
		f.PowerLevel();
		f.Ki = f.MaxKi;
		var c = new ServerPlayer
		{
			Id = IdBaseDoLevarJunto + forjados.Count,
			Peer = null,
			Cerebro = null,
			Name = nome,
			Race = "Human", Genero = "Male", Idade = 25,
			Zone = zona, Pos = onde,
			Conta = "bancada_levarjunto",
			Slot = forjados.Count,
			LastInputMs = NowMs(),
			Ficha = f,
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		PorNoMundo(c);
		c.Combate.Reviver();
		c.Moving = false;
		c.Voando = false;
		forjados.Add(c);
		return c;
	}

	/// <summary>Dois toques na tecla de producao: segurou e levantou. Falso se o segundo corpo nao estava na frente.</summary>
	private bool CarregarLj(ServerPlayer leva, ServerPlayer carga)
	{
		if (leva.AgarrandoId != 0) Soltar(leva, MotivoDaSoltura.Tecla);
		if (NaveDoPiloto(leva) is { } n) Desembarcar(leva, n);
		leva.Moving = carga.Moving = false;
		leva.Voando = carga.Voando = false;
		leva.Altitude = carga.Altitude = 0;
		leva.Ficha.KO = carga.Ficha.KO = false;
		leva.Ficha.dead = carga.Ficha.dead = false;
		Encostar(leva, carga, new Vec2(0, 1));
		AlternarAgarrao(leva);
		AlternarAgarrao(leva);
		return leva.AgarrandoId == carga.Id && leva.ModoDoAgarrao == ModoDeAgarrao.Carregando && carga.AgarradoPorId == leva.Id;
	}

	private void SegurarLj(ServerPlayer leva, ServerPlayer carga)
	{
		if (leva.AgarrandoId != 0) Soltar(leva, MotivoDaSoltura.Tecla);
		leva.Moving = carga.Moving = false;
		leva.Voando = carga.Voando = false;
		leva.Altitude = carga.Altitude = 0;
		Encostar(leva, carga, new Vec2(0, 1));
		AlternarAgarrao(leva);
	}

	private void VoltarLj(ServerPlayer leva, ServerPlayer carga, ZoneKey terra, Vec2 chao)
	{
		leva.Peer = null;
		leva.Moving = carga.Moving = false;
		if (!leva.Zone.Equals(terra)) MoveToZone(leva.Id, terra, chao);
		if (!carga.Zone.Equals(terra)) MoveToZone(carga.Id, terra, chao);
		leva.Voando = carga.Voando = false;
		leva.Altitude = carga.Altitude = 0;
	}

	/// <summary>
	/// O CHAO DE DENTRO DA SALA, o mesmo ponto onde a porta poe quem entra (`ComandoDaSalaDoTempo`). Um
	/// ponto inventado (8,8 tiles ou o nascer da Terra) cai em parede no z13, o corpo da frente nao e
	/// achado e o agarrao nunca fecha: a familia D e o contra-exemplo do preso reprovavam pela montagem.
	/// </summary>
	private Vec2 ChaoDaSalaLj()
	{
		const int t = ZoneCollision.TileSize;
		int h = _catalogo?.Get(ZoneKey.Premade(ZonaDaSala))?.H ?? 0;
		return new Vec2((EntradaByondX - 1) * t + t / 2f, (h - EntradaByondY) * t + t / 2f);
	}

	private static bool Juntos(ServerPlayer leva, ServerPlayer carga, ZoneKey zona) =>
		leva.Zone.Hash == zona.Hash && carga.Zone.Hash == zona.Hash
		&& leva.AgarrandoId == carga.Id && carga.AgarradoPorId == leva.Id;

	private void ACavernaLevaOsDois(ServerPlayer leva, ServerPlayer carga, Passagem praCaverna, ZoneKey terra, Vec2 chao)
	{
		GD.Print("[levarjunto] -- A) a caverna leva segurando e carregando, e o tique nao desfaz --");
		Atravessar(leva, praCaverna);
		ZoneKey caverna = ZoneKey.Premade("Earth_Cave");
		AfirmarLj("A1. carregando, a passagem leva os dois e o agarrao fica",
			Juntos(leva, carga, caverna), $"{leva.Zone.Name}/{carga.Zone.Name} agarra {leva.AgarrandoId}");
		leva.Moving = false;
		TickDoAgarrao(Protocol.TickSeconds);
		AfirmarLj("A3. o tique do agarrao, ja na caverna, nao solta",
			Juntos(leva, carga, caverna), $"modo {leva.ModoDoAgarrao}");

		VoltarLj(leva, carga, terra, chao);
		SegurarLj(leva, carga);
		AfirmarLj("(montagem) segurou sem levantar", leva.ModoDoAgarrao == ModoDeAgarrao.Segurando && leva.AgarrandoId == carga.Id);
		Atravessar(leva, praCaverna);
		AfirmarLj("A2. segurando, a passagem tambem leva os dois",
			Juntos(leva, carga, caverna) && leva.ModoDoAgarrao == ModoDeAgarrao.Segurando,
			$"{leva.Zone.Name}/{carga.Zone.Name}");
		VoltarLj(leva, carga, terra, chao);
	}

	private void ANaveLevaESoltaNoEspaco(ServerPlayer host, ServerPlayer leva, ServerPlayer carga, ZoneKey terra, Vec2 chao,
		List<Nave> naves, List<Esfera> esferas)
	{
		GD.Print("[levarjunto] -- B) a Capital Ship leva; a esfera do agarrado recusa; sair no espaco solta --");
		if (!CarregarLj(leva, carga)) { AfirmarLj("B0. reagarrou pra nave", false); return; }

		var grande = new Nave
		{
			Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, DonoConta = leva.Conta, DonoNome = leva.Name, ErguidaEm = NowMs(),
			ArmaduraMax = 1000, Armadura = 1000,
		};
		grande.PorZona(terra);
		grande.X = leva.Pos.X;
		grande.Y = leva.Pos.Y;
		_naves.Add(grande);
		naves.Add(grande);

		leva.Peer = host.Peer;
		try
		{
			EmbarcarNaNaveGrande(leva, grande);
			ZoneKey dentro = NaveGrande.ZonaDoInterior(grande.Id);
			AfirmarLj("B1. embarcar na Capital Ship leva os dois pro interior",
				Juntos(leva, carga, dentro), $"{leva.Zone.Name}/{carga.Zone.Name}");
			// O TIQUE DO AGARRAO ENTRE O EMBARQUE E A SAIDA, como no jogo (a viagem dura). Sem ele a B2 passava no mutante que nao leva
			// ninguem: o aperto separado so cai no tique, e a saida devolvia os dois a Terra com os campos de pe (a r2, x1).
			leva.Moving = carga.Moving = false;
			TickDoAgarrao(Protocol.TickSeconds);

			// A saida so atende quem esta na plataforma. O agarrao nao depende desse passo: os dois
			// continuam na mesma zona, colados, e o SairDaNave e que troca a zona.
			Vec2 plataforma = NaveGrande.PixelDe(NaveGrande.CelDaPlataforma);
			leva.Pos = carga.Pos = plataforma;
			SairDaNave(leva);
			AfirmarLj("B2. sair no planeta leva os dois pra fora",
				Juntos(leva, carga, terra), $"{leva.Zone.Name}/{carga.Zone.Name}");

			if (!CarregarLj(leva, carga)) { AfirmarLj("B3. reagarrou com a esfera", false); return; }
			var esfera = new Esfera { Set = -1, Numero = 1, Portador = carga.Id };
			esfera.PorZona(terra);
			_esferas.Add(esfera);
			esferas.Add(esfera);
			List<string> falas = Ouvir(() => EmbarcarNaNaveGrande(leva, grande));
			AfirmarLj("B3. a esfera do agarrado recusa o embarque e o agarrao fica",
				leva.Zone.Equals(terra) && carga.Zone.Equals(terra) && leva.AgarrandoId == carga.Id
				&& esfera.Portador == carga.Id && Disse(falas, FraseDaEsferaDoAgarrado),
				$"{leva.Zone.Name}; {Ultimos(falas)}");
			_esferas.Remove(esfera);
			esferas.Remove(esfera);

			if (!CarregarLj(leva, carga)) { AfirmarLj("B4. reagarrou pra sair no espaco", false); return; }
			EmbarcarNaNaveGrande(leva, grande);
			leva.Pos = carga.Pos = plataforma;
			grande.PorZona(ZonaDoEspaco);
			List<string> noVazio = Ouvir(() => SairDaNave(leva));
			AfirmarLj("B4. sair com a nave no espaco solta, e o agarrado fica no casco",
				Espaco.EhEspaco(leva.Zone) && carga.Zone.Equals(dentro) && leva.AgarrandoId == 0 && carga.AgarradoPorId == 0
				&& Disse(noVazio, FraseDeNaoLevarAoEspaco),
				$"leva {leva.Zone.Name}; carga {carga.Zone.Name}; {Ultimos(noVazio)}");
		}
		finally { leva.Peer = null; }

		VoltarLj(leva, carga, terra, chao);
	}

	private void OEspacoESoPodSoltam(ServerPlayer leva, ServerPlayer carga, ZoneKey terra, Vec2 chao, List<Nave> naves)
	{
		GD.Print("[levarjunto] -- C) decolar e o pod soltam, com a frase do espaco --");
		VoltarLj(leva, carga, terra, chao);
		if (!CarregarLj(leva, carga)) { AfirmarLj("C0. reagarrou pra decolar", false); return; }
		List<string> subiu = Ouvir(() => Decolar(leva));
		AfirmarLj("C1. decolar solta e so quem subiu vai pro espaco",
			Espaco.EhEspaco(leva.Zone) && carga.Zone.Equals(terra) && leva.AgarrandoId == 0 && carga.AgarradoPorId == 0
			&& Disse(subiu, FraseDeNaoLevarAoEspaco),
			$"leva {leva.Zone.Name}; carga {carga.Zone.Name}; {Ultimos(subiu)}");
		VoltarLj(leva, carga, terra, chao);

		if (!CarregarLj(leva, carga)) { AfirmarLj("C2. reagarrou pro pod", false); return; }
		var pod = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", DonoConta = leva.Conta, DonoNome = leva.Name, ErguidaEm = NowMs() };
		pod.PorZona(terra);
		pod.X = leva.Pos.X;
		pod.Y = leva.Pos.Y;
		_naves.Add(pod);
		naves.Add(pod);
		List<string> entrou = Ouvir(() => UsarNave(leva));
		AfirmarLj("C2. o pod solta com a frase do espaco e nao leva ninguem pra dentro",
			NaveDoPiloto(leva) == pod && leva.AgarrandoId == 0 && carga.AgarradoPorId == 0 && carga.Zone.Equals(terra)
			&& Disse(entrou, FraseDeNaoLevarAoEspaco),
			$"piloto {NaveDoPiloto(leva)?.Id}; {Ultimos(entrou)}");
		if (NaveDoPiloto(leva) is { } ocupado) Desembarcar(leva, ocupado);
		_naves.Remove(pod);
		naves.Remove(pod);
	}

	private void OPresoFicaEASalaNaoFura(ServerPlayer leva, ServerPlayer carga, ZoneKey terra, Vec2 chao)
	{
		GD.Print("[levarjunto] -- D/E) o preso fica; a Sala nao fura chave nem lotacao, e leva quem passaria --");
		VoltarLj(leva, carga, terra, chao);
		ZoneKey sala = ZoneKey.Premade(ZonaDaSala);
		Vec2 dentro = ChaoDaSalaLj();
		MoveToZone(leva.Id, sala, dentro);
		MoveToZone(carga.Id, sala, dentro);
		bool pegou = false;
		List<string> naSala = Ouvir(() => pegou = CarregarLj(leva, carga));
		if (!pegou)
		{
			AfirmarLj("D0. reagarrou na sala", false,
				$"zona {leva.Zone.Name}/{carga.Zone.Name}; pos {leva.Pos}/{carga.Pos}; modo {leva.ModoDoAgarrao}; agarra {leva.AgarrandoId}; "
				+ $"mexe {PodeMexerOCorpo(leva)}; {Ultimos(naSala)}");
			VoltarLj(leva, carga, terra, chao);
			return;
		}
		carga.SalaPreso = true;
		List<string> saiu = Ouvir(() => ViajarVoluntario(leva, terra, chao));
		AfirmarLj("D1. preso na Sala, quem carrega sai e o preso fica",
			leva.Zone.Equals(terra) && carga.Zone.Equals(sala) && leva.AgarrandoId == 0 && carga.AgarradoPorId == 0
			&& Disse(saiu, FraseDeQuemFicaPreso),
			$"leva {leva.Zone.Name}; carga {carga.Zone.Name}; {Ultimos(saiu)}");
		carga.SalaPreso = false;
		VoltarLj(leva, carga, terra, chao);

		Obra? porta = _noChao.Find(o => o.Tipo == TipoDaPorta && o.Zona.Equals(ZoneKey.Premade("Lookout")));
		AfirmarLj("(montagem) a porta da Sala esta no Templo", porta != null, porta == null ? "nenhuma" : porta.Zona.Name);
		if (porta == null) return;

		void NaPorta()
		{
			VoltarLj(leva, carga, terra, chao);
			leva.SalaAutorizada = true;
			leva.SalaUltimaEntrada = 0;
			leva.SalaPreso = false;
			carga.SalaAutorizada = false;
			carga.SalaUltimaEntrada = 0;
			carga.SalaPreso = false;
			carga.SalaPlaneta = "";
			leva.SalaPlaneta = "";
			_naSala.Remove(leva.Assinatura);
			_naSala.Remove(carga.Assinatura);
			MoveToZone(leva.Id, porta.Zona, new Vec2(porta.X, porta.Y));
			MoveToZone(carga.Id, porta.Zona, new Vec2(porta.X, porta.Y));
			CarregarLj(leva, carga);
		}

		NaPorta();
		List<string> semChave = Ouvir(() => ComandoDaSalaDoTempo(leva, "sala_entrar", ""));
		AfirmarLj("E1. sem a chave do agarrado, ninguem entra e a chave de quem carrega fica",
			leva.Zone.Equals(porta.Zona) && carga.Zone.Equals(porta.Zona) && leva.AgarrandoId == carga.Id
			&& leva.SalaAutorizada && Disse(semChave, "Você também não entra"),
			$"{leva.Zone.Name}; autorizada {leva.SalaAutorizada}; {Ultimos(semChave)}");

		NaPorta();
		carga.SalaAutorizada = true;
		_naSala["lj_ocupante"] = NowMs();
		List<string> cheia = Ouvir(() => ComandoDaSalaDoTempo(leva, "sala_entrar", ""));
		bool recusouCheia = leva.Zone.Equals(porta.Zona) && leva.SalaAutorizada && Disse(cheia, "não cabe");
		_naSala.Remove("lj_ocupante");
		AfirmarLj("E3. com uma vaga so, os dois nao entram", recusouCheia, Ultimos(cheia));

		NaPorta();
		carga.SalaAutorizada = true;
		ComandoDaSalaDoTempo(leva, "sala_entrar", "");
		AfirmarLj("E2. os dois autorizados, sala vazia: entram e os dois ocupam vaga",
			leva.Zone.Name == ZonaDaSala && carga.Zone.Name == ZonaDaSala && leva.AgarrandoId == carga.Id
			&& _naSala.ContainsKey(leva.Assinatura) && _naSala.ContainsKey(carga.Assinatura)
			&& !leva.SalaAutorizada && !carga.SalaAutorizada,
			$"zona {leva.Zone.Name}/{carga.Zone.Name}; vagas {QuantosNaSala()}; chaves {leva.SalaAutorizada}/{carga.SalaAutorizada}");
		_naSala.Remove(leva.Assinatura);
		_naSala.Remove(carga.Assinatura);
		leva.SalaPlaneta = carga.SalaPlaneta = "";
		leva.SalaUltimaEntrada = carga.SalaUltimaEntrada = 0;
		VoltarLj(leva, carga, terra, chao);
	}

	private void ANuvemDoLookoutLeva(ServerPlayer leva, ServerPlayer carga, ZoneKey terra, Vec2 chao)
	{
		GD.Print("[levarjunto] -- F) cair do Lookout leva os dois pra Terra --");
		VoltarLj(leva, carga, terra, chao);
		ZoneKey templo = ZoneKey.Premade(ClasseDeNuvem.ZonaDoTemplo);
		MoveToZone(leva.Id, templo, chao);
		MoveToZone(carga.Id, templo, chao);
		if (!CarregarLj(leva, carga)) { AfirmarLj("F0. reagarrou no templo", false); VoltarLj(leva, carga, terra, chao); return; }
		Cair(leva);
		AfirmarLj("F1. a queda da nuvem do Lookout leva os dois pra Terra",
			Juntos(leva, carga, terra), $"{leva.Zone.Name}/{carga.Zone.Name} agarra {leva.AgarrandoId}");
		VoltarLj(leva, carga, terra, chao);
	}

	private void AZonaCruAindaSolta(ServerPlayer leva, ServerPlayer carga, ZoneKey terra, Vec2 chao)
	{
		GD.Print("[levarjunto] -- G) morte, admin, evacuacao: o MoveToZone cru continua soltando no tique --");
		VoltarLj(leva, carga, terra, chao);
		if (!CarregarLj(leva, carga)) { AfirmarLj("G0. reagarrou pra zona crua", false); return; }
		MoveToZone(leva.Id, ZoneKey.Premade("Earth_Cave"), chao);
		TickDoAgarrao(Protocol.TickSeconds);
		AfirmarLj("G1. so quem viaja, sem o funil, o tique solta e o outro fica",
			leva.Zone.Name == "Earth_Cave" && carga.Zone.Equals(terra) && leva.AgarrandoId == 0 && carga.AgarradoPorId == 0,
			$"leva {leva.Zone.Name} agarra {leva.AgarrandoId}; carga {carga.Zone.Name} preso {carga.AgarradoPorId}");
		VoltarLj(leva, carga, terra, chao);
	}

	private void OsDefeitosInjetados(ServerPlayer host, ServerPlayer leva, ServerPlayer carga, Passagem praCaverna,
		ZoneKey terra, Vec2 chao, List<Nave> naves, List<Esfera> esferas)
	{
		GD.Print("[levarjunto] -- os defeitos injetados: a mesma regua reprova --");
		VoltarLj(leva, carga, terra, chao);
		LevarJuntoNaoLevaDeTeste = true;
		try
		{
			CarregarLj(leva, carga);
			Atravessar(leva, praCaverna);
			TickDoAgarrao(Protocol.TickSeconds);
			AfirmarLj("CONTRA-EXEMPLO (defeito injetado: a viagem leva so quem andou): a mesma regua REPROVA",
				leva.Zone.Name == "Earth_Cave" && carga.Zone.Equals(terra) && leva.AgarrandoId == 0,
				$"leva {leva.Zone.Name}; carga {carga.Zone.Name}; agarra {leva.AgarrandoId}");
		}
		finally { LevarJuntoNaoLevaDeTeste = false; }

		VoltarLj(leva, carga, terra, chao);
		LevarJuntoLevaProEspacoDeTeste = true;
		try
		{
			CarregarLj(leva, carga);
			Decolar(leva);
			AfirmarLj("CONTRA-EXEMPLO (defeito injetado: o espaco leva a forca): a mesma regua REPROVA",
				Espaco.EhEspaco(leva.Zone) && Espaco.EhEspaco(carga.Zone) && leva.AgarrandoId == carga.Id,
				$"leva {leva.Zone.Name}; carga {carga.Zone.Name}; agarra {leva.AgarrandoId}");
		}
		finally { LevarJuntoLevaProEspacoDeTeste = false; }

		VoltarLj(leva, carga, terra, chao);
		ZoneKey sala = ZoneKey.Premade(ZonaDaSala);
		MoveToZone(leva.Id, sala, ChaoDaSalaLj());
		MoveToZone(carga.Id, sala, ChaoDaSalaLj());
		carga.SalaPreso = true;
		LevarJuntoLevaOPresoDeTeste = true;
		try
		{
			bool pegou = CarregarLj(leva, carga);
			ViajarVoluntario(leva, terra, chao);
			AfirmarLj("CONTRA-EXEMPLO (defeito injetado: o preso e arrastado): a mesma regua REPROVA",
				pegou && leva.Zone.Equals(terra) && carga.Zone.Equals(terra) && leva.AgarrandoId == carga.Id,
				$"pegou {pegou}; carga {carga.Zone.Name}; agarra {leva.AgarrandoId}");
		}
		finally { LevarJuntoLevaOPresoDeTeste = false; carga.SalaPreso = false; }

		VoltarLj(leva, carga, terra, chao);
		var grande = new Nave
		{
			Id = _proximaNaveId++, Tipo = NaveGrande.Tipo, DonoConta = leva.Conta, DonoNome = leva.Name, ErguidaEm = NowMs(),
			ArmaduraMax = 1000, Armadura = 1000,
		};
		grande.PorZona(terra);
		grande.X = chao.X;
		grande.Y = chao.Y;
		_naves.Add(grande);
		naves.Add(grande);
		var esfera = new Esfera { Set = -1, Numero = 1, Portador = carga.Id };
		esfera.PorZona(terra);
		_esferas.Add(esfera);
		esferas.Add(esfera);
		LevarJuntoIgnoraEsferaDeTeste = true;
		leva.Peer = host.Peer;
		try
		{
			CarregarLj(leva, carga);
			grande.X = leva.Pos.X;
			grande.Y = leva.Pos.Y;
			EmbarcarNaNaveGrande(leva, grande);
			AfirmarLj("CONTRA-EXEMPLO (defeito injetado: a esfera do agarrado nao prende): a mesma regua REPROVA",
				leva.Zone.Equals(NaveGrande.ZonaDoInterior(grande.Id)) && carga.Zone.Equals(leva.Zone) && leva.AgarrandoId == carga.Id,
				$"{leva.Zone.Name}/{carga.Zone.Name}");
		}
		finally
		{
			LevarJuntoIgnoraEsferaDeTeste = false;
			leva.Peer = null;
			_esferas.Remove(esfera);
			esferas.Remove(esfera);
			// A CAPITAL SHIP SAI DO CHAO JA: parada no nascer da Terra, ela seria a nave MAIS PERTO do pod do contra-exemplo de
			// baixo, e o `UsarNave` escolheria ela (a c1 viu "piloto vazio, agarrao de pe").
			if (NaveDoPiloto(leva) is { } dentro) Desembarcar(leva, dentro);
			_naves.Remove(grande);
			naves.Remove(grande);
		}

		VoltarLj(leva, carga, terra, chao);
		Obra? porta = _noChao.Find(o => o.Tipo == TipoDaPorta && o.Zona.Equals(ZoneKey.Premade("Lookout")));
		if (porta != null)
		{
			LevarJuntoFuraASalaDeTeste = true;
			try
			{
				leva.SalaAutorizada = true;
				leva.SalaUltimaEntrada = 0;
				carga.SalaAutorizada = false;
				carga.SalaUltimaEntrada = 0;
				_naSala.Remove(leva.Assinatura);
				_naSala.Remove(carga.Assinatura);
				MoveToZone(leva.Id, porta.Zona, new Vec2(porta.X, porta.Y));
				MoveToZone(carga.Id, porta.Zona, new Vec2(porta.X, porta.Y));
				CarregarLj(leva, carga);
				ComandoDaSalaDoTempo(leva, "sala_entrar", "");
				AfirmarLj("CONTRA-EXEMPLO (defeito injetado: a Sala deixa o agarrado furar a chave): a mesma regua REPROVA",
					leva.Zone.Name == ZonaDaSala && carga.Zone.Name == ZonaDaSala,
					$"{leva.Zone.Name}/{carga.Zone.Name}");
			}
			finally
			{
				LevarJuntoFuraASalaDeTeste = false;
				_naSala.Remove(leva.Assinatura);
				_naSala.Remove(carga.Assinatura);
				leva.SalaPlaneta = carga.SalaPlaneta = "";
				leva.SalaUltimaEntrada = carga.SalaUltimaEntrada = 0;
			}
		}

		VoltarLj(leva, carga, terra, chao);
		var pod = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", DonoConta = leva.Conta, DonoNome = leva.Name, ErguidaEm = NowMs() };
		pod.PorZona(terra);
		pod.X = chao.X;
		pod.Y = chao.Y;
		_naves.Add(pod);
		naves.Add(pod);
		LevarJuntoPodNaoAvisaDeTeste = true;
		try
		{
			CarregarLj(leva, carga);
			pod.X = leva.Pos.X;
			pod.Y = leva.Pos.Y;
			List<string> falas = Ouvir(() => UsarNave(leva));
			// A MESMA REGUA DA C2, e nao outra: com o defeito o aperto ainda acaba (pela tecla, calado), entao
			// o que tem de cair e so a frase do espaco. Pedir "voce solta" aqui media a frase da tecla, que o
			// `Soltar(Tecla)` nem diz -- era por isso que o contra-exemplo reprovava sem nada no chat.
			bool c2 = NaveDoPiloto(leva) == pod && leva.AgarrandoId == 0 && carga.AgarradoPorId == 0 && carga.Zone.Equals(terra)
				&& Disse(falas, FraseDeNaoLevarAoEspaco);
			AfirmarLj("CONTRA-EXEMPLO (defeito injetado: o pod solta sem a frase do espaco): a mesma regua REPROVA",
				!c2 && NaveDoPiloto(leva) == pod && leva.AgarrandoId == 0,
				$"c2 {c2}; piloto {NaveDoPiloto(leva)?.Id}; agarra {leva.AgarrandoId}; {Ultimos(falas)}");
		}
		finally
		{
			LevarJuntoPodNaoAvisaDeTeste = false;
			if (NaveDoPiloto(leva) is { } ocupado) Desembarcar(leva, ocupado);
			_naves.Remove(pod);
			naves.Remove(pod);
		}
		VoltarLj(leva, carga, terra, chao);
	}
}
