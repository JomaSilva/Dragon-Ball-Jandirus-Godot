using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--subzonasteste` -- AS SUB-ZONAS SAO O PLANETA DELAS (dono, 2026-10-21). O pedido e as escolhas estao em `Core/World/SubZonas.cs`
/// e `GameServer.SubZonas.cs`. Seis clientes crus entram pela PORTA (jogadores de verdade: `Peer`, o login de producao) e sao postos nos
/// lugares da medida: a Terra, o Lookout (a plataforma do Templo, ao ar livre), o castelo do Rei Vegeta (no canto do mesmo mapa), a caverna
/// da Terra, a caverna de Vegeta e a Sala do Tempo.
///   A. O CORE: de que planeta e cada sub-zona (o castelo pela posicao), o relogio e a lua, o clima natural, o perigo do chao, a recusa.
///   B. A HORA E A LUA DE CADA CORPO no servidor -- e a lua cheia da Terra nascendo pra quem esta no Lookout.
///   C. O CLIMA FORCADO: o temporal da Terra chega no Lookout e na caverna, quem chega o recebe, quem forca o do Lookout forca o da Terra,
///      quem cruza a fronteira do castelo troca de ceu, e o raio cai por ceu.
///   G. O ESPACO: do Lookout se decola pro da Terra (do castelo, pro de Vegeta); da caverna e da Sala do Tempo, nao -- e o voo nao cai.
///   H. OS CHEFES DE SAGA NO MUNDO: a lista sai no login e quando muda, pelo lugar do corpo.
///   D. O FIM DO MUNDO QUE COMECA: o tremor, o ceu, o chao que racha e os avisos chegam no Lookout e na caverna -- e nao no castelo nem na
///      Sala do Tempo.
///   E. O FIM: quem esta no Lookout morre junto, quem sobrevive na caverna sobe pro espaco, e a Sala do Tempo nao sente nada.
///   F. DEPOIS DO FIM: quem chega numa sub-zona da Terra morta e rebocado pro espaco, e quem acorda la vai pro berco.
/// Cada regra tem o defeito injetado dela (o jogo de antes) e a regua REPROVA com ele ligado. O fim do mundo roda dentro de um
/// `PalcoDeMortes`: o `planetas-mortos.json` nao e tocado.
///
/// O RELOGIO NAO ANDA: a bancada roda dentro de uma chamada, e cada tique de que ela precisa e chamado pela porta dele. Cada afirmacao
/// principal tem um rotulo proprio em negrito: e por ele que a contraprova por mutacao (o `ciclo.py` do agente `subzonas`) sabe onde cada
/// linha tirada reprova.
/// </summary>
public sealed partial class GameServer
{
	private static readonly string[] ContasDaBancadaDasSubZonas =
	[
		"bancada_sz_terra", "bancada_sz_monge", "bancada_sz_mineiro", "bancada_sz_guarda", "bancada_sz_treino", "bancada_sz_saiyajin",
		"bancada_sz_chegada",
	];

	/// <summary>Faixa de ids dos corpos forjados desta bancada -- longe das outras (a do espaco e 988_000, a do juiz 987_760).</summary>
	private const int IdBaseDasSubZonasDeTeste = 989_500;

	private bool _szRodando, _szJaRodou;
	private int _szOk, _szFalhou, _szProximoId;
	private readonly List<ServerPlayer> _szForjados = [];

	private void AfirmarSz(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _szOk++; GD.Print($"[subzonas]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _szFalhou++; GD.PrintErr($"[subzonas]   FALHA {nome}   [{detalhe}]"); }
	}

	// =====================================================================
	// OS LUGARES DA MEDIDA -- celulas conferidas nos planos do mapa (chao livre num bloco 3x3; teto so onde o mapa tem)
	// =====================================================================
	private static readonly ZoneKey TerraSz = ZoneKey.Premade("Earth");
	private static readonly ZoneKey VegetaSz = ZoneKey.Premade("Vegeta");
	private static readonly ZoneKey TemploSz = ZoneKey.Premade("Lookout");
	private static readonly ZoneKey CavernaDaTerraSz = ZoneKey.Premade("Earth_Cave");
	private static readonly ZoneKey CavernaDeVegetaSz = ZoneKey.Premade("Vegeta_Cave");
	private static readonly ZoneKey SalaSz = ZoneKey.Premade(SalaDoTempo.Zona);

	/// <summary>O centro de uma celula, em pixels.</summary>
	private static Vec2 CelulaSz(int cx, int cy) =>
		new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f, cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f);

	/// <summary>A plataforma do Templo, AO AR LIVRE (o oval verde do z12), longe da escada e das portas.</summary>
	private static readonly Vec2 PlataformaSz = CelulaSz(141, 320);

	/// <summary>O patio do castelo do Rei Vegeta, AO AR LIVRE (o chao de Vegeta no canto do z12).</summary>
	private static readonly Vec2 PatioDoCasteloSz = CelulaSz(354, 398);

	/// <summary>Um corredor das cavernas (z22 e z23 tem chao livre na mesma celula), SOB TETO como a caverna inteira.</summary>
	private static readonly Vec2 CorredorDaCavernaSz = CelulaSz(156, 197);

	/// <summary>O chao branco da Sala do Tempo.</summary>
	private static readonly Vec2 ChaoDaSalaSz = CelulaSz(145, 300);

	private void IrSz(ServerPlayer pl, ZoneKey zona, Vec2 onde)
	{
		if (!pl.Zone.Equals(zona)) MoveToZone(pl.Id, zona, onde);
		else CravarPosicao(pl, onde);
	}

	/// <summary>Um corpo forjado (sem `Peer`), pro que nao precisa ouvir nada: o chefe da lista, o ausente que acorda.</summary>
	private ServerPlayer ForjarSz(string nome, ZoneKey zona, Vec2 onde, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDasSubZonasDeTeste + _szProximoId++,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = onde,
			Conta = $"bancada_sz_{nome.ToLowerInvariant()}",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = bp },
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		_szForjados.Add(novo);
		return novo;
	}

	/// <summary>O voo e os tres portoes do `Space_Flight`: a maestria de Ki que abre o voo, Ki e poder de sobra, e o voo ligado.</summary>
	private void AsasSz(ServerPlayer pl)
	{
		if (!PodeVoar(pl))
		{
			pl.Livro.Dar(SkillDoKi);
			pl.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
		}
		pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, 10 * Voo.KiParaEspaco);
		pl.Ficha.expressedBP = Math.Max(pl.Ficha.expressedBP, 10 * Voo.BpParaEspaco);
		pl.Ficha.flightability = Math.Max(pl.Ficha.flightability, Voo.HabilidadeNivel1);
		if (!pl.Voando) AlternarVoo(pl);
	}

	/// <summary>Sobe voando ate o teto (o gesto de verdade: `QuerSubir` e o `TickDoVoo`), e devolve o que o servidor disse no caminho.</summary>
	private List<string> SubirAteOTetoSz(ServerPlayer pl)
	{
		AsasSz(pl);
		pl.AvisoDeTetoAte = 0;
		ZoneKey de = pl.Zone;
		List<string> falas = Ouvir(() =>
		{
			for (int i = 0; i < 300 && pl.Zone.Equals(de); i++)
			{
				pl.QuerSubir = true;
				TickDoVoo(pl, Protocol.TickSeconds);
			}
		});
		pl.QuerSubir = false;
		return falas;
	}

	/// <summary>O que cada corpo recebeu de chat, ao certo (a escuta da entrega, por destinatario).</summary>
	private static bool RecebeuSz(List<(int Para, string Autor, string Texto)> entregas, ServerPlayer pl, string trecho) =>
		entregas.Exists(e => e.Para == pl.Id && e.Texto.Contains(trecho, StringComparison.OrdinalIgnoreCase));

	private static PlanetaNoEspaco DiscoSz(string nome) => Espaco.PreFeitos().First(p => p.Nome == nome);

	private static bool PertoDoDiscoSz(ServerPlayer pl, string nome) =>
		Espaco.EhEspaco(pl.Zone) && (pl.Pos - DiscoSz(nome).Pos).Length <= DiscoSz(nome).Raio + 400;

	// =====================================================================
	// A BANCADA
	// =====================================================================
	public void RodarBancadaDasSubZonas(int porta)
	{
		if (_szRodando || _szJaRodou) return;
		_szRodando = _szJaRodou = true;
		_szOk = _szFalhou = 0;
		GD.Print("[subzonas] ================ AS SUB-ZONAS SAO O PLANETA DELAS (dono, 2026-10-21) ================");

		ApagarContasDasSubZonas();
		var c = new ClienteCruDeTeste[ContasDaBancadaDasSubZonas.Length];
		for (int i = 0; i < c.Length; i++) c[i] = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, c);

		// O QUE A BANCADA MEXE NO MUNDO E VOLTA: o clima forcado da Terra e de Vegeta, o adianto do relogio, o chao caido das cinco zonas, o
		// estado das sagas (o chefe de mentira) e o relogio do raio.
		double adiantoAntes = _adiantoDoCeu;
		var caidoAntes = new Dictionary<string, HashSet<(int X, int Y)>?>();
		foreach (ZoneKey z in new[] { TerraSz, VegetaSz, TemploSz, CavernaDaTerraSz, CavernaDeVegetaSz })
			caidoAntes[ChaveDeEstado(z)] = _cenarioCaido.TryGetValue(ChaveDeEstado(z), out HashSet<(int X, int Y)>? s) ? [.. s] : null;
		EstadoDoElo? eloDeMentira = null;
		EstadoDoChefe? chefeDeMentira = null;
		try
		{
			ServerPlayer?[] gente = new ServerPlayer?[6];
			string[] nomes = ["Terrestre", "Monge", "Mineiro", "Guarda", "Treino", "Saiyajin"];
			for (int i = 0; i < 6; i++) gente[i] = mesa.EntraNoMundo(c[i], ContasDaBancadaDasSubZonas[i], nomes[i]);
			AfirmarSz("(montagem) os seis clientes entram pela porta e sao jogadores",
				gente.All(p => p != null && EhJogador(p) && p.Combate != null), string.Join(", ", gente.Select(p => p?.Name ?? "-")));
			if (gente.Any(p => p == null || p.Combate == null)) return;
			ServerPlayer terrestre = gente[0]!, monge = gente[1]!, mineiro = gente[2]!, guarda = gente[3]!, treino = gente[4]!, saiyajin = gente[5]!;

			AoCoreSz();
			PorNosLugaresSz(terrestre, monge, mineiro, guarda, treino, saiyajin);
			AHoraEALuaSz(terrestre, monge, mineiro, guarda, saiyajin);
			OClimaForcadoSz(terrestre, monge, mineiro, guarda, treino, saiyajin);
			OEspacoSz(monge, mineiro, guarda, treino);
			PorNosLugaresSz(terrestre, monge, mineiro, guarda, treino, saiyajin);
			(eloDeMentira, chefeDeMentira) = OsChefesSz(mesa, c[6], terrestre);
			using (PalcoDeMortesDeBancada())
			{
				OFimQueComecaSz(terrestre, monge, mineiro, guarda, treino, saiyajin);
				OFimSz(terrestre, monge, mineiro, guarda, treino, saiyajin);
				DepoisDoFimSz(guarda, treino, saiyajin);
			}
		}
		catch (Exception e)
		{
			AfirmarSz("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			SubZonas.CeuProprioDeTeste = SubZonas.ClimaProprioDeTeste = SubZonas.CasteloDaTerraDeTeste = false;
			PlanetaEmPerigo.SubZonaForaDoPerigoDeTeste = false;
			PlateiaSoDaZonaDeTeste = LookoutSemEspacoDeTeste = CavernaRompeOCeuDeTeste = SubZonaMortaAbertaDeTeste = false;
			ChefesCaladosDeTeste = ClimaNaoSegueOPeDeTeste = false;
			EscutaDoClimaMandado = null;
			EscutaDosRaios = null;
			EscutaDosChefesNosPlanetas = null;
			EscutaDeEfeitos = null;
			EscutaDeEntregas = null;
			if (chefeDeMentira != null) eloDeMentira?.Chefes.Remove(chefeDeMentira);
			_chefesMandados = "";
			_proximoRaio.Clear();   // (o livro dos mortos e os tremores o `PalcoDeMortes` ja devolveu)
			ForcarClima(TerraSz, TipoDeClima.Limpo, 0);
			ForcarClima(VegetaSz, TipoDeClima.Limpo, 0);
			_adiantoDoCeu = adiantoAntes;
			foreach ((string chave, HashSet<(int X, int Y)>? s) in caidoAntes)
				if (s == null) _cenarioCaido.Remove(chave); else _cenarioCaido[chave] = s;
			foreach (ServerPlayer f in _szForjados)
			{
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
			}
			_szForjados.Clear();
			mesa.Fecha();
			ApagarContasDasSubZonas();
			_szRodando = false;
			GD.Print($"[subzonas] ================ {_szOk} OK, {_szFalhou} FALHA(S) ================");
		}
	}

	private void ApagarContasDasSubZonas()
	{
		if (_store == null) return;
		foreach (string conta in ContasDaBancadaDasSubZonas)
			try
			{
				string caminho = System.IO.Path.Combine(_store.Pasta, conta + ".json");
				if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
			}
			catch (Exception e) { GD.PushWarning($"[subzonas] nao apaguei '{conta}': {e.Message}"); }
	}

	/// <summary>Cada um no lugar dele -- pelas portas de producao (o `MoveToZone` da troca de zona, o `CravarPosicao` dentro dela).</summary>
	private void PorNosLugaresSz(ServerPlayer terrestre, ServerPlayer monge, ServerPlayer mineiro, ServerPlayer guarda, ServerPlayer treino,
								 ServerPlayer saiyajin)
	{
		IrSz(terrestre, TerraSz, PontoDeNascimento(TerraSz));
		IrSz(monge, TemploSz, PlataformaSz);
		IrSz(mineiro, CavernaDaTerraSz, CorredorDaCavernaSz);
		IrSz(guarda, TemploSz, PatioDoCasteloSz);
		IrSz(treino, SalaSz, ChaoDaSalaSz);
		IrSz(saiyajin, CavernaDeVegetaSz, CorredorDaCavernaSz);
		AfirmarSz("(montagem) cada um no seu lugar: a Terra, a plataforma do Templo (ao ar livre), a caverna da Terra (sob teto), o patio do "
				  + "castelo, a Sala do Tempo e a caverna de Vegeta",
			terrestre.Zone.Equals(TerraSz) && monge.Zone.Equals(TemploSz) && !SobTeto(monge) && mineiro.Zone.Equals(CavernaDaTerraSz)
			&& SobTeto(mineiro) && guarda.Zone.Equals(TemploSz) && !SobTeto(guarda) && treino.Zone.Equals(SalaSz)
			&& saiyajin.Zone.Equals(CavernaDeVegetaSz),
			$"{terrestre.Zone} {monge.Zone}(teto {SobTeto(monge)}) {mineiro.Zone}(teto {SobTeto(mineiro)}) {guarda.Zone} {treino.Zone} {saiyajin.Zone}");
	}

	// =====================================================================
	// A. O CORE
	// =====================================================================
	private void AoCoreSz()
	{
		GD.Print("[subzonas] -- A. o Core: de que planeta e cada sub-zona --");
		AfirmarSz("A1. **O LOOKOUT E A CAVERNA DA TERRA SAO DA TERRA; A CAVERNA DE VEGETA, DE VEGETA**",
			SubZonas.PlanetaMae(TemploSz) is { } ma && ma.Equals(TerraSz) && SubZonas.PlanetaMae(CavernaDaTerraSz) is { } mb && mb.Equals(TerraSz)
			&& SubZonas.PlanetaMae(CavernaDeVegetaSz) is { } mv && mv.Equals(VegetaSz),
			$"{SubZonas.PlanetaMae(TemploSz)} {SubZonas.PlanetaMae(CavernaDaTerraSz)} {SubZonas.PlanetaMae(CavernaDeVegetaSz)}");
		AfirmarSz("A2. ...e a Sala do Tempo (a da Terra e a de um mundo sorteado), a Terra, o espaco, uma nave e um mundo gerado com o nome do "
				  + "Lookout NAO sao sub-zona de ninguem",
			SubZonas.PlanetaMae(SalaSz) == null && SubZonas.PlanetaMae(ZoneKey.Interior(SalaDoTempo.Zona, 77)) == null
			&& SubZonas.PlanetaMae(TerraSz) == null && SubZonas.PlanetaMae(ZonaDoEspaco) == null
			&& SubZonas.PlanetaMae(ZoneKey.Interior("Nave", 3)) == null && SubZonas.PlanetaMae(ZoneKey.Procedural("Lookout", 9)) == null);

		// O CASTELO, PELOS PONTOS QUE O MAPA DE VERDADE USA: a chegada da escada de Vegeta (`VCastleEntrance1`) e a do Templo Sagrado da Terra.
		Vec2? daEscadaDeVegeta = _passagens.TryGetValue("Vegeta", out List<Passagem>? deVegeta)
			? deVegeta.Where(p => p.Zona == TemploSz.Name).Select(p => (Vec2?)new Vec2(p.Dx, p.Dy)).FirstOrDefault() : null;
		Vec2? daEscadaDaTerra = _passagens.TryGetValue("Earth", out List<Passagem>? daTerra)
			? daTerra.Where(p => p.Zona == TemploSz.Name).Select(p => (Vec2?)new Vec2(p.Dx, p.Dy)).FirstOrDefault() : null;
		AfirmarSz("(montagem) as duas escadas que chegam no Lookout estao no mapa: a do castelo (de Vegeta) e a do Templo (da Terra)",
			daEscadaDeVegeta != null && daEscadaDaTerra != null, $"{daEscadaDeVegeta} {daEscadaDaTerra}");
		bool castelo(Vec2 p) => SubZonas.PlanetaMae(TemploSz, p) is { } m && m.Equals(VegetaSz);
		bool templo(Vec2 p) => SubZonas.PlanetaMae(TemploSz, p) is { } m && m.Equals(TerraSz);
		bool casteloOk = daEscadaDeVegeta is { } ev && castelo(ev) && castelo(PatioDoCasteloSz)
						 && daEscadaDaTerra is { } et && templo(et) && templo(PlataformaSz);
		AfirmarSz("A3. **O CASTELO DO REI VEGETA, NO CANTO DO LOOKOUT, E DE VEGETA**: quem chega pela escada de Vegeta e quem esta no patio; "
				  + "a escada do Templo e a plataforma sao da Terra",
			casteloOk, $"escada de Vegeta {daEscadaDeVegeta}, patio {PatioDoCasteloSz}");
		SubZonas.CasteloDaTerraDeTeste = true;
		try
		{
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o castelo e Lookout como o resto) -- a regua A3 REPROVA: o patio do castelo vira Terra",
				!castelo(PatioDoCasteloSz));
		}
		finally { SubZonas.CasteloDaTerraDeTeste = false; }

		// O RELOGIO E A LUA
		RelogioDoPlaneta terra = RelogioDaZona(TerraSz), templo0 = RelogioDaZona(TemploSz), cavernaT = RelogioDaZona(CavernaDaTerraSz);
		RelogioDoPlaneta vegeta = RelogioDaZona(VegetaSz), cavernaV = RelogioDaZona(CavernaDeVegetaSz), sala = RelogioDaZona(SalaSz);
		int diferentes = 0;
		double t0 = TempoDoMundo;
		for (int i = 0; i < 360; i++)
		{
			double t = t0 + i * 600;   // de 10 em 10 minutos, dois dias e meio da Terra
			EstadoDoCeu a = Ceu.De(terra, t), b = Ceu.De(templo0, t);
			if (a.Hora != b.Hora || a.Fase != b.Fase || a.LuaNoCeu != b.LuaNoCeu) diferentes++;
		}
		AfirmarSz("A4. **O LOOKOUT TEM O RELOGIO E A LUA DA TERRA**: a mesma ficha, e a mesma hora e a mesma fase em 360 instantes de dois dias",
			MesmoRelogioSz(terra, templo0) && diferentes == 0,
			$"defasagem {templo0.Defasagem} x {terra.Defasagem}; lua {templo0.Lua.Defasagem} x {terra.Lua.Defasagem}; {diferentes} instante(s) diferentes");
		AfirmarSz("A4b. ...a caverna da Terra tem o da Terra, e a de Vegeta o de Vegeta",
			MesmoRelogioSz(terra, cavernaT) && MesmoRelogioSz(vegeta, cavernaV) && !MesmoRelogioSz(terra, vegeta));
		AfirmarSz("A4c. ...e a Sala do Tempo continua com o dela: sol eterno e sem lua (outra dimensao)",
			!sala.TemNoite && !sala.Lua.Existe && !MesmoRelogioSz(terra, sala));
		SubZonas.CeuProprioDeTeste = true;
		try
		{
			RelogioDoPlaneta antes = RelogioDaZona(TemploSz);
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: a sub-zona com o relogio dela, o jogo de antes) -- a regua A4 REPROVA: o Lookout tem "
					  + "outra defasagem e outra lua",
				!MesmoRelogioSz(terra, antes), $"defasagem {antes.Defasagem} x {terra.Defasagem}; lua {antes.Lua.Defasagem} x {terra.Lua.Defasagem}");
		}
		finally { SubZonas.CeuProprioDeTeste = false; }

		// O CLIMA NATURAL
		ClimaDoPlaneta climaT = ClimaDaZona(TerraSz), climaL = ClimaDaZona(TemploSz), climaV = ClimaDaZona(VegetaSz), climaCV = ClimaDaZona(CavernaDeVegetaSz);
		int blocosDiferentes = 0, blocosComClima = 0;
		for (int i = 0; i < 3000; i++)
		{
			double t = t0 + i * Clima.SegundosPorBloco * 0.5;
			EstadoDoClima a = Clima.Natural(climaT, t, Clima.SalDaZona(TerraSz)), b = Clima.Natural(climaL, t, Clima.SalDaZona(TemploSz));
			EstadoDoClima va = Clima.Natural(climaV, t, Clima.SalDaZona(VegetaSz)), vb = Clima.Natural(climaCV, t, Clima.SalDaZona(CavernaDeVegetaSz));
			if (a.Tipo != b.Tipo || a.Forca != b.Forca || va.Tipo != vb.Tipo || va.Forca != vb.Forca) blocosDiferentes++;
			if (b.Ativo) blocosComClima++;
		}
		AfirmarSz("A5. **O LOOKOUT TEM O CLIMA NATURAL DA TERRA**: a mesma lista, e o mesmo ceu em 3.000 meios-blocos (e a caverna de Vegeta o de "
				  + "Vegeta)",
			climaL.Existe && climaL.Permitidos.SequenceEqual(climaT.Permitidos) && blocosDiferentes == 0 && blocosComClima > 100,
			$"lista {string.Join(",", climaL.Permitidos)}; {blocosDiferentes} diferentes; {blocosComClima} com clima");
		ClimaDoPlaneta climaSala = ClimaDaZona(SalaSz);
		AfirmarSz("A5b. ...e a Sala do Tempo continua com o caotico dela (chuva de sangue e tempestade de areia, que a Terra nao tem)",
			climaSala.Existe && Array.IndexOf(climaSala.Permitidos, TipoDeClima.ChuvaDeSangue) >= 0
			&& Array.IndexOf(climaSala.Permitidos, TipoDeClima.Areia) >= 0, string.Join(",", climaSala.Permitidos));
		SubZonas.ClimaProprioDeTeste = true;
		try
		{
			ClimaDoPlaneta antes = ClimaDaZona(TemploSz);
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: a sub-zona com o clima dela, o jogo de antes) -- a regua A5 REPROVA: o Lookout fica sem "
					  + "clima nenhum, e o sal do sorteio e outro",
				!antes.Existe && Clima.SalDaZona(TemploSz) != Clima.SalDaZona(TerraSz));
		}
		finally { SubZonas.ClimaProprioDeTeste = false; }

		// O PERIGO DO CHAO
		var mortos = new RegistroDeMortos();
		var semChefe = new HashSet<string>();
		PerigoDoChao p(ZoneKey z, Vec2 onde, RegistroDeMortos r, HashSet<string> chefes) => PlanetaEmPerigo.De(z, onde, r, chefes);
		mortos.Por(new EstadoDaMorte { Chave = "Earth", Nome = "Earth", Fase = FaseDaMorte.Explodindo, Faltam = 100 });
		bool explodindo = p(TerraSz, PontoDeNascimento(TerraSz), mortos, semChefe) == PerigoDoChao.Explodindo
						  && p(TemploSz, PlataformaSz, mortos, semChefe) == PerigoDoChao.Explodindo
						  && p(CavernaDaTerraSz, CorredorDaCavernaSz, mortos, semChefe) == PerigoDoChao.Explodindo;
		bool foraDele = p(TemploSz, PatioDoCasteloSz, mortos, semChefe) == PerigoDoChao.Nenhum
						&& p(SalaSz, ChaoDaSalaSz, mortos, semChefe) == PerigoDoChao.Nenhum
						&& p(ZonaDoEspaco, DiscoSz("Earth").Pos, mortos, semChefe) == PerigoDoChao.Nenhum
						&& p(CavernaDeVegetaSz, CorredorDaCavernaSz, mortos, semChefe) == PerigoDoChao.Nenhum;
		mortos.Por(new EstadoDaMorte { Chave = "Earth", Nome = "Earth", Fase = FaseDaMorte.Morrendo, Faltam = 100 });
		bool morrendo = p(TemploSz, PlataformaSz, mortos, semChefe) == PerigoDoChao.Explodindo;
		mortos.Por(new EstadoDaMorte { Chave = "Earth", Nome = "Earth", Fase = FaseDaMorte.Destruido });
		bool destruido = p(TemploSz, PlataformaSz, mortos, semChefe) == PerigoDoChao.Nenhum;
		var chefeEmVegeta = new HashSet<string> { "Vegeta" };
		var vazio = new RegistroDeMortos();
		bool chefe = p(CavernaDeVegetaSz, CorredorDaCavernaSz, vazio, chefeEmVegeta) == PerigoDoChao.ChefeDeSaga
					 && p(TemploSz, PatioDoCasteloSz, vazio, chefeEmVegeta) == PerigoDoChao.ChefeDeSaga
					 && p(TemploSz, PlataformaSz, vazio, chefeEmVegeta) == PerigoDoChao.Nenhum;
		AfirmarSz("A6. **O PERIGO DO CHAO**: a Terra explodindo poe em perigo a Terra, o Lookout e a caverna dela -- e nao o castelo, a Sala do "
				  + "Tempo, o espaco ou a caverna de Vegeta; o pavio lento conta, o planeta ja destruido nao; o chefe de Vegeta poe em perigo a "
				  + "caverna e o castelo de Vegeta",
			explodindo && foraDele && morrendo && destruido && chefe,
			$"explodindo {explodindo}, fora {foraDele}, morrendo {morrendo}, destruido {destruido}, chefe {chefe}");
		mortos.Por(new EstadoDaMorte { Chave = "Earth", Nome = "Earth", Fase = FaseDaMorte.Explodindo, Faltam = 100 });
		PlanetaEmPerigo.SubZonaForaDoPerigoDeTeste = true;
		try
		{
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o perigo so da zona do planeta) -- a regua A6 REPROVA: o Lookout de uma Terra explodindo "
					  + "fica em paz",
				p(TemploSz, PlataformaSz, mortos, semChefe) == PerigoDoChao.Nenhum);
		}
		finally { PlanetaEmPerigo.SubZonaForaDoPerigoDeTeste = false; }

		AfirmarSz("A7. **DA CAVERNA E DA SALA DO TEMPO NAO SE SOBE**: as duas tem a recusa delas; o Lookout nao",
			SubZonas.PorQueNaoSobe(CavernaDaTerraSz).Contains("caverna") && SubZonas.PorQueNaoSobe(CavernaDeVegetaSz).Contains("caverna")
			&& SubZonas.PorQueNaoSobe(SalaSz).Contains("Sala do Tempo") && SubZonas.PorQueNaoSobe(TemploSz).Length == 0,
			$"'{SubZonas.PorQueNaoSobe(CavernaDaTerraSz)}' | '{SubZonas.PorQueNaoSobe(SalaSz)}'");
	}

	// =====================================================================
	// B. A HORA E A LUA DE CADA CORPO
	// =====================================================================
	/// <summary>A mesma ficha de ceu: rotacao, dia e noite, e a lua.</summary>
	private static bool MesmoRelogioSz(RelogioDoPlaneta x, RelogioDoPlaneta y) =>
		x.Defasagem == y.Defasagem && x.SegundosPorDia == y.SegundosPorDia && x.TemDia == y.TemDia && x.TemNoite == y.TemNoite
		&& x.Lua.Existe == y.Lua.Existe && x.Lua.Defasagem == y.Lua.Defasagem;

	private void AHoraEALuaSz(ServerPlayer terrestre, ServerPlayer monge, ServerPlayer mineiro, ServerPlayer guarda, ServerPlayer saiyajin)
	{
		GD.Print("[subzonas] -- B. a hora e a lua de cada corpo, no servidor --");
		// UM INSTANTE SO PRA SECAO INTEIRA: o `TempoDoMundo` e relogio de PAREDE, e um milissegundo entre duas leituras ja e 7e-7 do dia
		// da Terra -- mais que a folga destas reguas. Na r1 o contra-exemplo da B2 leu o castelo 2 ms depois da Terra e "passou" por isso,
		// com o defeito fazendo o que devia. Cada corpo e lido pelo relogio de producao dele (`RelogioDoLugar`, o do `CeuDe`, do anuncio
		// da lua e do Oozaru dos NPCs) no MESMO `t`, e o contra-exemplo rele tudo depois de ligar o defeito.
		double t = TempoDoMundo;
		EstadoDoCeu CeuEmT(ServerPlayer pl) => Ceu.De(RelogioDoLugar(pl), t);
		bool mesmoCeu(EstadoDoCeu a, EstadoDoCeu b) => a.Hora == b.Hora && a.Fase == b.Fase && a.LuaNoCeu == b.LuaNoCeu;
		EstadoDoCeu daTerra = CeuEmT(terrestre), deVegeta = Ceu.De(RelogioDaZona(VegetaSz), t);
		EstadoDoCeu doMonge = CeuEmT(monge), doMineiro = CeuEmT(mineiro), doSaiyajin = CeuEmT(saiyajin);
		AfirmarSz("B1. **QUEM ESTA NO LOOKOUT VE O CEU DA TERRA** -- e quem esta na caverna dela tambem; quem esta na de Vegeta, o de Vegeta",
			MesmoRelogioSz(RelogioDoLugar(monge), RelogioDaZona(TerraSz)) && MesmoRelogioSz(RelogioDoLugar(mineiro), RelogioDaZona(TerraSz))
			&& MesmoRelogioSz(RelogioDoLugar(saiyajin), RelogioDaZona(VegetaSz))
			&& mesmoCeu(doMonge, daTerra) && mesmoCeu(doMineiro, daTerra) && mesmoCeu(doSaiyajin, deVegeta),
			$"Terra {daTerra.Hora:0.0000}/{daTerra.Fase}, Lookout {doMonge.Hora:0.0000}/{doMonge.Fase}, caverna {doMineiro.Hora:0.0000}, "
			+ $"caverna de Vegeta {doSaiyajin.Hora:0.0000} x {deVegeta.Hora:0.0000}");
		EstadoDoCeu doGuarda = CeuEmT(guarda);
		AfirmarSz("B2. **QUEM ESTA NO CASTELO DO REI VEGETA VE O CEU DE VEGETA**, no mesmo mapa do Lookout",
			MesmoRelogioSz(RelogioDoLugar(guarda), RelogioDaZona(VegetaSz)) && mesmoCeu(doGuarda, deVegeta)
			&& !MesmoRelogioSz(RelogioDaZona(VegetaSz), RelogioDaZona(TerraSz)) && Math.Abs(deVegeta.Hora - daTerra.Hora) > 1e-3,
			$"castelo {doGuarda.Hora:0.0000}, Vegeta {deVegeta.Hora:0.0000}, Terra {daTerra.Hora:0.0000}");
		SubZonas.CasteloDaTerraDeTeste = true;
		try
		{
			EstadoDoCeu castelo = CeuEmT(guarda), terra = CeuEmT(terrestre);
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o castelo e Lookout como o resto) -- a regua B2 REPROVA: o castelo ve a hora da Terra",
				MesmoRelogioSz(RelogioDoLugar(guarda), RelogioDaZona(TerraSz)) && mesmoCeu(castelo, terra) && !mesmoCeu(castelo, deVegeta),
				$"castelo {castelo.Hora:0.0000}, Terra {terra.Hora:0.0000}, Vegeta {deVegeta.Hora:0.0000}");
		}
		finally { SubZonas.CasteloDaTerraDeTeste = false; }

		// A LUA CHEIA DA TERRA, NO ALTO, E O MONGE NA PLATAFORMA ABERTA: o tique do ceu de producao anuncia a quem a ve nascer.
		AjustarCeuDaTerra(hora: 0.90, fase: Ceu.Cheia);
		bool cheiaNaTerra = Ceu.De(RelogioDaZona(TerraSz), TempoDoMundo) is { Cheia: true, LuaNoCeu: true };
		monge.LuaEstavaNoCeu = false;
		monge.LuaVista = 0;
		var entregas = new List<(int Para, string Autor, string Texto)>();
		EscutaDeEntregas = entregas;
		try { TickDoCeu(); }
		finally { EscutaDeEntregas = null; }
		AfirmarSz("B3. **A LUA CHEIA DA TERRA NASCE PRA QUEM ESTA NO LOOKOUT**: o tique do ceu anuncia a cheia ao Monge, na plataforma",
			cheiaNaTerra && RecebeuSz(entregas, monge, "lua cheia"),
			$"cheia na Terra {cheiaNaTerra}; o Monge ouviu: {string.Join(" | ", entregas.Where(e => e.Para == monge.Id).Select(e => e.Texto))}");

		SubZonas.CeuProprioDeTeste = true;
		try
		{
			monge.LuaEstavaNoCeu = false;
			monge.LuaVista = 0;
			var comDefeito = new List<(int Para, string Autor, string Texto)>();
			EscutaDeEntregas = comDefeito;
			try { TickDoCeu(); }
			finally { EscutaDeEntregas = null; }
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o Lookout com a lua dele) -- a regua B3 REPROVA: a cheia da Terra nao nasce no Lookout",
				!RecebeuSz(comDefeito, monge, "lua cheia"),
				$"o Monge ouviu: {string.Join(" | ", comDefeito.Where(e => e.Para == monge.Id).Select(e => e.Texto))}");
		}
		finally { SubZonas.CeuProprioDeTeste = false; }
		monge.LuaEstavaNoCeu = false;
		monge.LuaVista = 0;
	}

	// =====================================================================
	// C. O CLIMA FORCADO
	// =====================================================================
	private void OClimaForcadoSz(ServerPlayer terrestre, ServerPlayer monge, ServerPlayer mineiro, ServerPlayer guarda, ServerPlayer treino,
								 ServerPlayer saiyajin)
	{
		GD.Print("[subzonas] -- C. o clima forcado --");
		ForcarClima(TerraSz, TipoDeClima.Limpo, 0);
		ForcarClima(VegetaSz, TipoDeClima.Limpo, 0);

		var mandados = new List<(int Quem, ulong Ceu, TipoDeClima Tipo)>();
		EscutaDoClimaMandado = mandados;
		ForcarClima(TerraSz, TipoDeClima.Tempestade, 600, 1, "bancada subzonas");
		bool recebeu(ServerPlayer pl, ZoneKey ceu, TipoDeClima tipo) => mandados.Exists(m => m.Quem == pl.Id && m.Ceu == ceu.Hash && m.Tipo == tipo);
		AfirmarSz("C1. **O TEMPORAL FORCADO DA TERRA CHEGA A QUEM ESTA NO LOOKOUT E NA CAVERNA DELA** -- e nao ao castelo, a Sala do Tempo ou "
				  + "a caverna de Vegeta",
			recebeu(terrestre, TerraSz, TipoDeClima.Tempestade) && recebeu(monge, TerraSz, TipoDeClima.Tempestade)
			&& recebeu(mineiro, TerraSz, TipoDeClima.Tempestade)
			&& !mandados.Exists(m => m.Quem == guarda.Id || m.Quem == treino.Id || m.Quem == saiyajin.Id),
			string.Join(" ", mandados.Select(m => $"{m.Quem}:{m.Tipo}")));
		AfirmarSz("C2. ...e o ceu de cada um diz o mesmo: temporal forcado no Lookout e na caverna; o castelo e a Sala do Tempo sem forcado nenhum",
			ClimaDe(monge) is { Tipo: TipoDeClima.Tempestade, Forcado: true } && ClimaDe(mineiro) is { Tipo: TipoDeClima.Tempestade, Forcado: true }
			&& ClimaAgora(TemploSz) is { Tipo: TipoDeClima.Tempestade, Forcado: true } && !ClimaDe(guarda).Forcado && !ClimaAgora(SalaSz).Forcado,
			$"Lookout {ClimaDe(monge).Tipo}, caverna {ClimaDe(mineiro).Tipo}, castelo {ClimaDe(guarda).Tipo}/{ClimaDe(guarda).Forcado}");

		PlateiaSoDaZonaDeTeste = true;
		try
		{
			mandados.Clear();
			ForcarClima(TerraSz, TipoDeClima.Nevasca, 600, 1, "bancada subzonas, com defeito");
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o ceu forcado so na zona, o jogo de antes) -- a regua C1 REPROVA: o Lookout e a caverna "
					  + "nao recebem nada, e o ceu deles nao tem o forcado da Terra",
				!mandados.Exists(m => m.Quem == monge.Id || m.Quem == mineiro.Id) && !ClimaDe(monge).Forcado,
				string.Join(" ", mandados.Select(m => $"{m.Quem}:{m.Tipo}")));
		}
		finally { PlateiaSoDaZonaDeTeste = false; }
		ForcarClima(TerraSz, TipoDeClima.Limpo, 0);
		ForcarClima(TerraSz, TipoDeClima.Tempestade, 600, 1, "bancada subzonas");

		// QUEM CHEGA
		mandados.Clear();
		IrSz(treino, TemploSz, PlataformaSz);
		AfirmarSz("C3. **QUEM CHEGA NO LOOKOUT NO MEIO DO TEMPORAL DA TERRA RECEBE O TEMPORAL** (a troca de zona manda o ceu forcado do lugar)",
			recebeu(treino, TerraSz, TipoDeClima.Tempestade), string.Join(" ", mandados.Select(m => $"{m.Quem}:{m.Tipo}")));
		IrSz(treino, SalaSz, ChaoDaSalaSz);

		// QUEM FORCA O DO LOOKOUT
		ForcarClima(TemploSz, TipoDeClima.Neve, 600, 1, "bancada subzonas, pelo Lookout");
		AfirmarSz("C4. **QUEM FORCA O CEU DO LOOKOUT FORCA O DA TERRA** (o Super Shenron, as fotos e o trailer forcam pelo `pl.Zone`)",
			ClimaAgora(TerraSz) is { Tipo: TipoDeClima.Neve, Forcado: true } && ClimaDe(monge).Tipo == TipoDeClima.Neve,
			$"Terra {ClimaAgora(TerraSz).Tipo}, Lookout {ClimaDe(monge).Tipo}");
		PlateiaSoDaZonaDeTeste = true;
		try
		{
			ForcarClima(TemploSz, TipoDeClima.Neblina, 600, 1, "bancada subzonas, pelo Lookout, com defeito");
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o forcado guardado na chave do Lookout) -- a regua C4 REPROVA: a Terra nao muda",
				ClimaAgora(TerraSz).Tipo == TipoDeClima.Neve, $"Terra {ClimaAgora(TerraSz).Tipo}");
			ForcarClima(TemploSz, TipoDeClima.Limpo, 0);   // a entrada orfa da chave do Lookout sai, com o mesmo defeito ligado
		}
		finally { PlateiaSoDaZonaDeTeste = false; }
		ForcarClima(TerraSz, TipoDeClima.Limpo, 0);
		ForcarClima(TerraSz, TipoDeClima.Tempestade, 600, 1, "bancada subzonas");

		// QUEM CRUZA A FRONTEIRA DO CASTELO
		ForcarClima(VegetaSz, TipoDeClima.ChuvaDeSangue, 600, 1, "bancada subzonas");
		MandarClima(monge);   // a plataforma: o da Terra, anotado
		mandados.Clear();
		CravarPosicao(monge, PatioDoCasteloSz);
		TickDasSubZonas();
		AfirmarSz("C5. **QUEM VOA DO LOOKOUT PRO CASTELO RECEBE O CEU FORCADO DE VEGETA** no tique seguinte",
			recebeu(monge, VegetaSz, TipoDeClima.ChuvaDeSangue), string.Join(" ", mandados.Select(m => $"{m.Quem}:{m.Tipo}")));
		mandados.Clear();
		CravarPosicao(monge, PlataformaSz);
		TickDasSubZonas();
		AfirmarSz("C5b. ...e quem volta pra plataforma, o da Terra", recebeu(monge, TerraSz, TipoDeClima.Tempestade),
			string.Join(" ", mandados.Select(m => $"{m.Quem}:{m.Tipo}")));
		ClimaNaoSegueOPeDeTeste = true;
		try
		{
			mandados.Clear();
			CravarPosicao(monge, PatioDoCasteloSz);
			TickDasSubZonas();
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o ceu forcado so na chegada da zona) -- a regua C5 REPROVA: no castelo ele fica com o da Terra",
				!mandados.Exists(m => m.Quem == monge.Id));
		}
		finally { ClimaNaoSegueOPeDeTeste = false; }
		CravarPosicao(monge, PlataformaSz);
		MandarClima(monge);

		// O RAIO, POR CEU. O forcado entra em 1,2 s de RELOGIO DO MUNDO (`Clima.De`), e a bancada roda num instante: o relogio anda 5 s pra o
		// temporal chegar na forca que puxa raio (>= 0,45). O `agora` do `TickDoRaio` e so a cadencia das descargas.
		ForcarClima(VegetaSz, TipoDeClima.Limpo, 0);
		_adiantoDoCeu += 5;
		var raios = new List<(int Perto, ulong Ceu)>();
		EscutaDosRaios = raios;
		_proximoRaio.Clear();
		double agora = TempoDoMundo;
		for (int i = 0; i < 400; i++) TickDoRaio(agora + i * 15);
		int pertoDoMonge = raios.Count(r => r.Perto == monge.Id), pertoDoGuarda = raios.Count(r => r.Perto == guarda.Id);
		AfirmarSz("C6. **O RAIO DO TEMPORAL DA TERRA NAO CAI PERTO DE QUEM ESTA NO CASTELO** -- cai perto de quem esta na plataforma",
			pertoDoMonge > 50 && pertoDoGuarda == 0, $"perto do Monge {pertoDoMonge}, do Guarda {pertoDoGuarda}, de todos {raios.Count}");
		SubZonas.CasteloDaTerraDeTeste = true;
		try
		{
			raios.Clear();
			_proximoRaio.Clear();
			for (int i = 0; i < 400; i++) TickDoRaio(agora + 6000 + i * 15);
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o castelo e Lookout como o resto) -- a regua C6 REPROVA: o temporal da Terra cai no patio "
					  + "do castelo",
				raios.Exists(r => r.Perto == guarda.Id), $"perto do Guarda {raios.Count(r => r.Perto == guarda.Id)}");
		}
		finally { SubZonas.CasteloDaTerraDeTeste = false; }
		EscutaDosRaios = null;
		_proximoRaio.Clear();
		EscutaDoClimaMandado = null;
		ForcarClima(TerraSz, TipoDeClima.Limpo, 0);
	}

	// =====================================================================
	// G. O ESPACO
	// =====================================================================
	private void OEspacoSz(ServerPlayer monge, ServerPlayer mineiro, ServerPlayer guarda, ServerPlayer treino)
	{
		GD.Print("[subzonas] -- G. o espaco --");
		Decolar(monge);
		AfirmarSz("G1. **DO LOOKOUT SE SOBE PRO ESPACO DA TERRA**: o `Decolar` larga o corpo no vazio em volta dela",
			PertoDoDiscoSz(monge, "Earth") && monge.PlanetaDeOrigem == "Earth", $"zona {monge.Zone.Name}, origem {monge.PlanetaDeOrigem}");
		IrSz(monge, TemploSz, PlataformaSz);
		LookoutSemEspacoDeTeste = true;
		try
		{
			List<string> recusa = Ouvir(() => Decolar(monge));
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o Lookout fora do mapa do universo, o jogo de antes) -- a regua G1 REPROVA: o corpo fica, "
					  + "e ouve que o lugar nao fica no mapa",
				monge.Zone.Equals(TemploSz) && Disse(recusa, "nao fica no mapa do universo"), Ultimos(recusa));
		}
		finally { LookoutSemEspacoDeTeste = false; }

		Decolar(guarda);
		AfirmarSz("G2. ...e do castelo do Rei Vegeta, pro espaco de Vegeta",
			PertoDoDiscoSz(guarda, "Vegeta") && guarda.PlanetaDeOrigem == "Vegeta", $"zona {guarda.Zone.Name}, origem {guarda.PlanetaDeOrigem}");
		IrSz(guarda, TemploSz, PatioDoCasteloSz);

		List<string> naCaverna = SubirAteOTetoSz(mineiro);
		AfirmarSz("G3. **O VOO QUE BATE NO TETO DA CAVERNA NAO ROMPE: AVISA E CONTINUA NO AR**",
			mineiro.Zone.Equals(CavernaDaTerraSz) && mineiro.Voando && mineiro.Altitude >= Voo.AlturaMaxima - 0.01f && Disse(naCaverna, "caverna"),
			$"zona {mineiro.Zone.Name}, voando {mineiro.Voando}, altura {mineiro.Altitude:0}; ouviu: {Ultimos(naCaverna)}");
		List<string> atalho = Ouvir(() => Decolar(mineiro));
		AfirmarSz("G4. ...e o atalho de decolar tambem recusa la dentro", mineiro.Zone.Equals(CavernaDaTerraSz) && Disse(atalho, "caverna"),
			Ultimos(atalho));
		CavernaRompeOCeuDeTeste = true;
		try
		{
			SubirAteOTetoSz(mineiro);
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: a caverna nao recusa) -- a regua G3 REPROVA: a caverna sobe pro espaco da Terra",
				Espaco.EhEspaco(mineiro.Zone), $"zona {mineiro.Zone.Name}");
		}
		finally { CavernaRompeOCeuDeTeste = false; }
		IrSz(mineiro, CavernaDaTerraSz, CorredorDaCavernaSz);

		List<string> naSala = SubirAteOTetoSz(treino);
		AfirmarSz("G5. **DA SALA DO TEMPO NAO SE SOBE PRO ESPACO**: avisa que e outra dimensao, e o corpo continua no ar",
			treino.Zone.Equals(SalaSz) && treino.Voando && Disse(naSala, "Sala do Tempo"),
			$"zona {treino.Zone.Name}, voando {treino.Voando}; ouviu: {Ultimos(naSala)}");

		SubirAteOTetoSz(monge);
		AfirmarSz("G6. **QUEM ROMPE A ATMOSFERA VOANDO NO LOOKOUT CHEGA VOANDO NO ESPACO DA TERRA**",
			PertoDoDiscoSz(monge, "Earth") && monge.Voando && monge.PlanetaDeOrigem == "Earth",
			$"zona {monge.Zone.Name}, voando {monge.Voando}, origem {monge.PlanetaDeOrigem}");

		foreach (ServerPlayer pl in new[] { monge, mineiro, treino })
		{
			pl.Voando = false;
			pl.Altitude = 0f;
		}
	}

	// =====================================================================
	// H. OS CHEFES DE SAGA NO MUNDO
	// =====================================================================
	private (EstadoDoElo? Elo, EstadoDoChefe? Chefe) OsChefesSz(MesaDeClientesCrus mesa, ClienteCruDeTeste chegada, ServerPlayer terrestre)
	{
		GD.Print("[subzonas] -- H. os chefes de saga no mundo --");
		EloDaSaga? elo = _cadeia.FirstOrDefault(e => e.Planeta == "Namek") ?? _cadeia.FirstOrDefault();
		EstadoDoElo? est = elo == null ? null : _sagas.Do(elo.Id);
		AfirmarSz("(montagem) a cadeia de sagas tem um elo pra plantar um chefe de mentira", elo != null && est != null, elo?.Id ?? "-");
		if (elo == null || est == null) return (null, null);

		_chefesMandados = "";
		AnunciarChefesSeMudou();   // o que a cadeia de verdade tem agora (nada, num mundo novo) -- e a assinatura parte dela
		List<string> antes = PlanetasComChefe();
		var chefe = new EstadoDoChefe { Molde = elo.Chefes.FirstOrDefault()?.Molde ?? "chefe", Fase = FaseDoChefe.NoMundo, Corpo = 0 };
		est.Chefes.Add(chefe);
		string chaveDoElo = ChaveDePlaneta.Da(ZoneKey.Premade(elo.Planeta))?.Texto ?? elo.Planeta;

		var listas = new List<(int Para, string[] Chaves)>();
		EscutaDosChefesNosPlanetas = listas;
		AnunciarChefesSeMudou();
		AfirmarSz("H1. **O CHEFE DE SAGA NO MUNDO POE O PLANETA DELE NA LISTA**, e ela sai pra todo jogador",
			PlanetasComChefe().Contains(chaveDoElo) && !antes.Contains(chaveDoElo) && listas.Exists(l => l.Para == terrestre.Id && l.Chaves.Contains(chaveDoElo)),
			$"{elo.Planeta}: {string.Join(",", PlanetasComChefe())}; mandadas {listas.Count}");
		ChefesCaladosDeTeste = true;
		try
		{
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o servidor nao conta) -- a regua H1 REPROVA: a lista sai vazia",
				!PlanetasComChefe().Contains(chaveDoElo));
		}
		finally { ChefesCaladosDeTeste = false; }

		ServerPlayer corpo = ForjarSz("Chefe", CavernaDaTerraSz, CorredorDaCavernaSz, 1e6);
		chefe.Corpo = corpo.Id;
		AfirmarSz("H2. ...pelo lugar do CORPO: o chefe que esta na caverna da Terra poe a TERRA na lista", PlanetasComChefe().Contains("Earth"),
			string.Join(",", PlanetasComChefe()));

		listas.Clear();
		ServerPlayer? novo = mesa.EntraNoMundo(chegada, ContasDaBancadaDasSubZonas[6], "Chegada");
		AfirmarSz("H3. **QUEM ENTRA NO MUNDO RECEBE A LISTA** (o login manda, como o dos planetas mortos)",
			novo != null && listas.Exists(l => l.Para == novo.Id && l.Chaves.Contains("Earth")),
			string.Join(" ", listas.Select(l => $"{l.Para}:{string.Join(",", l.Chaves)}")));

		listas.Clear();
		chefe.Fase = FaseDoChefe.Derrotado;
		AnunciarChefesSeMudou();
		AfirmarSz("H4. ...e quando o chefe cai, a lista sai de novo sem ele",
			listas.Exists(l => l.Para == terrestre.Id && !l.Chaves.Contains("Earth") && !l.Chaves.Contains(chaveDoElo)),
			string.Join(" ", listas.Select(l => $"{l.Para}:{string.Join(",", l.Chaves)}")));
		EscutaDosChefesNosPlanetas = null;

		// O CORPO DO CHEFE SAI DO MUNDO AQUI: ele esta na caverna da Terra, e o fim do mundo la embaixo o contaria como gente.
		_players.Remove(corpo.Id);
		ZoneList(corpo.Zone.Hash).Remove(corpo);
		_szForjados.Remove(corpo);
		return (est, chefe);
	}

	// =====================================================================
	// D. O FIM DO MUNDO QUE COMECA
	// =====================================================================
	private void OFimQueComecaSz(ServerPlayer terrestre, ServerPlayer monge, ServerPlayer mineiro, ServerPlayer guarda, ServerPlayer treino,
								 ServerPlayer saiyajin)
	{
		GD.Print("[subzonas] -- D. o fim do mundo que comeca --");
		_mortos.Limpar();
		var efeitos = new List<(int Quem, string Id, long Ms)>();
		var entregas = new List<(int Para, string Autor, string Texto)>();
		bool tremeu(ServerPlayer pl) => efeitos.Exists(e => e.Quem == pl.Id && e.Id == "terremoto");

		PlateiaSoDaZonaDeTeste = true;
		try
		{
			EscutaDeEfeitos = efeitos;
			ComecarDestruicao(TerraSz, 1, "bancada subzonas, com defeito");
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o fim do mundo so na zona) -- a regua D1 REPROVA: o Lookout e a caverna nao tremem",
				tremeu(terrestre) && !tremeu(monge) && !tremeu(mineiro));
		}
		finally
		{
			PlateiaSoDaZonaDeTeste = false;
			EscutaDeEfeitos = null;
		}
		AbortarMorte(TerraSz, "bancada subzonas");
		efeitos.Clear();

		EscutaDeEfeitos = efeitos;
		EscutaDeEntregas = entregas;
		try { ComecarDestruicao(TerraSz, 1, "bancada subzonas"); }
		finally { EscutaDeEfeitos = null; EscutaDeEntregas = null; }
		AfirmarSz("D1. **O PLANETA QUE COMECA A SE PARTIR TREME DEBAIXO DE QUEM ESTA NO LOOKOUT E NA CAVERNA** -- e nao no castelo, na Sala do "
				  + "Tempo nem na caverna de Vegeta",
			tremeu(terrestre) && tremeu(monge) && tremeu(mineiro) && !tremeu(guarda) && !tremeu(treino) && !tremeu(saiyajin)
			&& RecebeuSz(entregas, monge, "SE PARTIR") && RecebeuSz(entregas, mineiro, "SE PARTIR") && !RecebeuSz(entregas, guarda, "SE PARTIR"),
			string.Join(" ", efeitos.Where(e => e.Id == "terremoto").Select(e => e.Quem)));
		AfirmarSz("D2. **O CEU DA DESTRUICAO CHEGA NO LOOKOUT E NA CAVERNA** -- e nao no castelo nem na Sala do Tempo",
			ClimaDe(monge).Tipo == TipoDeClima.Destruicao && ClimaDe(mineiro).Tipo == TipoDeClima.Destruicao
			&& ClimaDe(guarda).Tipo != TipoDeClima.Destruicao && ClimaAgora(SalaSz).Tipo != TipoDeClima.Destruicao,
			$"Lookout {ClimaDe(monge).Tipo}, caverna {ClimaDe(mineiro).Tipo}, castelo {ClimaDe(guarda).Tipo}, Sala {ClimaAgora(SalaSz).Tipo}");

		efeitos.Clear();
		EscutaDeEfeitos = efeitos;
		try
		{
			_proximoTremor["Earth"] = 0;
			TickDaDestruicao(1);
		}
		finally { EscutaDeEfeitos = null; }
		AfirmarSz("D3. **O TREMOR DOS CINCO MINUTOS CHEGA NO LOOKOUT E NA CAVERNA** (a volta de producao, `TremorDaExplosao`)",
			tremeu(monge) && tremeu(mineiro) && !tremeu(guarda) && !tremeu(treino),
			string.Join(" ", efeitos.Where(e => e.Id == "terremoto").Select(e => e.Quem)));

		int caidasNaCaverna() => _cenarioCaido.TryGetValue(ChaveDeEstado(CavernaDaTerraSz), out HashSet<(int X, int Y)>? s) ? s.Count : 0;
		int antes = caidasNaCaverna();
		for (int i = 0; i < 40 && caidasNaCaverna() == antes; i++) QuebrarChaoPertoDosJogadores(TerraSz, TetoDeCelulasPorVolta);
		AfirmarSz("D4. **O CHAO DA CAVERNA RACHA PERTO DE QUEM ESTA NELA** -- o mapa dela, e nao o da Terra", caidasNaCaverna() > antes,
			$"{antes} -> {caidasNaCaverna()}");
		PlateiaSoDaZonaDeTeste = true;
		try
		{
			int antes2 = caidasNaCaverna();
			for (int i = 0; i < 40; i++) QuebrarChaoPertoDosJogadores(TerraSz, TetoDeCelulasPorVolta);
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o fim do mundo so na zona) -- a regua D4 REPROVA: a caverna fica inteira",
				caidasNaCaverna() == antes2, $"{antes2} -> {caidasNaCaverna()}");
		}
		finally { PlateiaSoDaZonaDeTeste = false; }

		entregas.Clear();
		EscutaDeEntregas = entregas;
		try { AnunciarNoPlaneta(TerraSz, "bancada subzonas: um aviso do planeta"); }
		finally { EscutaDeEntregas = null; }
		AfirmarSz("D5. **OS AVISOS DO PLANETA CHEGAM NO LOOKOUT E NA CAVERNA** (o pavio, o bombardeio, a carga do Planet Destroy)",
			RecebeuSz(entregas, monge, "um aviso do planeta") && RecebeuSz(entregas, mineiro, "um aviso do planeta")
			&& !RecebeuSz(entregas, guarda, "um aviso do planeta") && !RecebeuSz(entregas, treino, "um aviso do planeta"));

		AfirmarSz("D6. ...e o perigo do chao, pela regua do Core sobre o livro do servidor: o Lookout e a caverna em perigo, o castelo nao",
			PlanetaEmPerigo.De(monge.Zone, monge.Pos, _mortos, new HashSet<string>()) == PerigoDoChao.Explodindo
			&& PlanetaEmPerigo.De(mineiro.Zone, mineiro.Pos, _mortos, new HashSet<string>()) == PerigoDoChao.Explodindo
			&& PlanetaEmPerigo.De(guarda.Zone, guarda.Pos, _mortos, new HashSet<string>()) == PerigoDoChao.Nenhum);
	}

	// =====================================================================
	// E. O FIM
	// =====================================================================
	private void OFimSz(ServerPlayer terrestre, ServerPlayer monge, ServerPlayer mineiro, ServerPlayer guarda, ServerPlayer treino,
						ServerPlayer saiyajin)
	{
		GD.Print("[subzonas] -- E. o fim: quem esta no Lookout morre junto --");
		IrSz(terrestre, ZoneKey.Premade("Namek"), PontoDeNascimento(ZoneKey.Premade("Namek")));   // a testemunha sai da Terra
		void Poder(ServerPlayer pl, double bp)
		{
			pl.Ficha.BP = bp;
			pl.Ficha.Tick(agoraMs: NowMs());
		}
		Poder(monge, 1);
		Poder(mineiro, 1e12);
		Poder(guarda, 1);
		Poder(treino, 1);
		Poder(saiyajin, 1);
		double hpGuarda = guarda.Ficha.HP, hpTreino = treino.Ficha.HP, hpSaiyajin = saiyajin.Ficha.HP;

		void Consumar()
		{
			if (MorteDaZona(TerraSz) is { } e) e.Faltam = 0.5;
			TickDaDestruicao(1);
		}

		// PRIMEIRO O DEFEITO: com ele ninguem nas sub-zonas e tocado, entao a medida de verdade parte do mesmo mundo.
		PlateiaSoDaZonaDeTeste = true;
		try
		{
			Consumar();
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: o commit so na zona, o jogo de antes) -- a regua E1 REPROVA: o Monge sobrevive no Lookout "
					  + "de uma Terra que nao existe, e o Mineiro fica na caverna",
				ZonaMorta(TerraSz) && !monge.Ficha.dead && monge.Zone.Equals(TemploSz) && mineiro.Zone.Equals(CavernaDaTerraSz),
				$"Terra morta {ZonaMorta(TerraSz)}; Monge morto {monge.Ficha.dead} em {monge.Zone.Name}; Mineiro em {mineiro.Zone.Name}");
		}
		finally { PlateiaSoDaZonaDeTeste = false; }

		_mortos.Limpar();
		_proximoTremor.Clear();
		ComecarDestruicao(TerraSz, 1, "bancada subzonas, o fim");
		Consumar();
		AfirmarSz("E1. **QUEM ESTA NO LOOKOUT MORRE JUNTO COM A TERRA** (o fraco: a explosao o mata)",
			ZonaMorta(TerraSz) && monge.Ficha.dead, $"Terra morta {ZonaMorta(TerraSz)}; Monge morto {monge.Ficha.dead}, em {monge.Zone.Name}");
		AfirmarSz("E2. **QUEM SOBREVIVE NA CAVERNA SOBE PRO ESPACO EM VOLTA DA TERRA** (o forte: a mesma evacuacao do chao)",
			!mineiro.Ficha.dead && PertoDoDiscoSz(mineiro, "Earth"), $"Mineiro morto {mineiro.Ficha.dead}, em {mineiro.Zone.Name}");
		AfirmarSz("E3. ...e a Sala do Tempo, o castelo do Rei Vegeta e a caverna de Vegeta nao sentem nada",
			treino.Zone.Equals(SalaSz) && !treino.Ficha.dead && treino.Ficha.HP == hpTreino && guarda.Zone.Equals(TemploSz) && !guarda.Ficha.dead
			&& guarda.Ficha.HP == hpGuarda && saiyajin.Zone.Equals(CavernaDeVegetaSz) && !saiyajin.Ficha.dead && saiyajin.Ficha.HP == hpSaiyajin,
			$"Sala {treino.Zone.Name}/{treino.Ficha.HP:0}, castelo {guarda.Zone.Name}/{guarda.Ficha.HP:0}, caverna de Vegeta {saiyajin.Zone.Name}/{saiyajin.Ficha.HP:0}");
	}

	// =====================================================================
	// F. DEPOIS DO FIM
	// =====================================================================
	private void DepoisDoFimSz(ServerPlayer guarda, ServerPlayer treino, ServerPlayer saiyajin)
	{
		GD.Print("[subzonas] -- F. depois do fim --");
		AfirmarSz("(montagem) a Terra esta destruida no livro", ZonaMorta(TerraSz));

		SubZonaMortaAbertaDeTeste = true;
		try
		{
			IrSz(saiyajin, TemploSz, PlataformaSz);
			TickDasSubZonas();
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: a sub-zona de um mundo morto de pe) -- a regua F1 REPROVA: quem chega no Lookout de uma "
					  + "Terra morta fica la",
				saiyajin.Zone.Equals(TemploSz), $"{saiyajin.Zone.Name}");
		}
		finally { SubZonaMortaAbertaDeTeste = false; }

		IrSz(treino, TemploSz, PlataformaSz);   // a saida da Sala do Tempo da no Templo
		List<string> falas = Ouvir(TickDasSubZonas);
		AfirmarSz("F1. **QUEM CHEGA NO LOOKOUT DE UMA TERRA QUE JA EXPLODIU E LEVADO PRO ESPACO EM VOLTA DELA**, e ouve por que",
			PertoDoDiscoSz(treino, "Earth") && PertoDoDiscoSz(saiyajin, "Earth") && Disse(falas, "caiu junto"),
			$"Treino em {treino.Zone.Name}, Saiyajin em {saiyajin.Zone.Name}; ouviu: {Ultimos(falas)}");
		AfirmarSz("F2. ...mas o castelo de uma Vegeta viva continua de pe", guarda.Zone.Equals(TemploSz), guarda.Zone.Name);

		// O AUSENTE: quem deslogou na caverna e volta depois do fim. A REGUA E O PROPRIO PLANETA -- um segundo ausente, com o MESMO berco
		// de producao (o do Guarda, que e humano: a Terra) e a mesma semente, deslogado no chao da Terra morta. O `OndeEsteCorpoPodeAcordar`
		// manda os dois pro berco pelo mesmo funil, e o berco de quem nasceu na Terra morta e o REFUGIO (`DestinoDoBerco`): o mundo vivo
		// mais perto de casa -- pre-feito, ou a orbita de um gerado que o `TickDoEspaco` pousa sozinho. Na r1 a regua exigia "fora do
		// espaco" e reprovou um refugio gerado ("acordou em Espaco"), que e onde acorda tambem quem deslogou na propria Terra.
		ServerPlayer ausente = ForjarSz("Ausente", CavernaDaTerraSz, CorredorDaCavernaSz, 1e5);
		ServerPlayer daTerra = ForjarSz("AusenteDaTerra", TerraSz, PontoDeNascimento(TerraSz), 1e5);
		ausente.Berco = daTerra.Berco = guarda.Berco;
		ausente.SeedDoBerco = daTerra.SeedDoBerco = guarda.SeedDoBerco;
		SubZonaMortaAbertaDeTeste = true;
		try
		{
			OndeEsteCorpoPodeAcordar(ausente);
			AfirmarSz("CONTRA-EXEMPLO (defeito injetado: a sub-zona de um mundo morto de pe) -- a regua F3 REPROVA: quem deslogou na caverna "
					  + "acorda nela", ausente.Zone.Equals(CavernaDaTerraSz), ausente.Zone.Name);
		}
		finally { SubZonaMortaAbertaDeTeste = false; }
		ZoneKey zonaAntes = ausente.Zone;
		OndeEsteCorpoPodeAcordar(ausente);
		OndeEsteCorpoPodeAcordar(daTerra);
		AfirmarSz("F3. **QUEM DESLOGOU NA CAVERNA DE UMA TERRA QUE EXPLODIU ACORDA ONDE ACORDA QUEM DESLOGOU NA PROPRIA TERRA** -- o berco, "
				  + "pelo mesmo funil, e nao a caverna",
			zonaAntes.Equals(CavernaDaTerraSz) && !ausente.Zone.Equals(CavernaDaTerraSz) && !daTerra.Zone.Equals(TerraSz)
			&& ausente.Zone.Equals(daTerra.Zone) && (ausente.Pos - daTerra.Pos).Length < 1f,
			$"da caverna: {ausente.Zone.Name} {ausente.Pos}; da Terra: {daTerra.Zone.Name} {daTerra.Pos}");
		// LOGAR NAO MATA NINGUEM NEM LARGA NINGUEM NO VACUO SEM VOO: no espaco so anda quem voa, e o unico chao que vem sozinho e o
		// pouso do `TickDoEspaco` -- que so pousa num disco VIVO debaixo do corpo.
		string chao(ServerPlayer pl) => !Espaco.EhEspaco(pl.Zone) ? (ZonaMorta(pl.Zone) ? "" : pl.Zone.Name)
			: Espaco.PlanetaSob(SeedDoUniverso, pl.Pos) is { } sob && !PlanetaMorto(sob) ? "orbita de " + sob.Nome : "";
		AfirmarSz("F3b. ...e acorda VIVO e com chao: um mundo vivo, ou a orbita de um mundo vivo que o espaco pousa -- nunca o vacuo aberto, "
				  + "onde quem nao voa fica parado",
			!ausente.Ficha.dead && !ausente.Ficha.KO && chao(ausente).Length > 0, $"{ausente.Zone.Name}: '{chao(ausente)}'");
		ServerPlayer semRefugio = ForjarSz("AusenteSemRefugio", CavernaDaTerraSz, CorredorDaCavernaSz, 1e5);
		semRefugio.Berco = guarda.Berco;
		semRefugio.SeedDoBerco = guarda.SeedDoBerco;
		using (SemVizinhancaDeRefugio()) OndeEsteCorpoPodeAcordar(semRefugio);
		AfirmarSz("CONTRA-EXEMPLO (a busca do refugio desligada: o ultimo recurso do berco, o espaco aberto onde a Terra ficava) -- a regua "
				  + "F3b REPROVA: em cima do cadaver nao ha chao",
			Espaco.EhEspaco(semRefugio.Zone) && chao(semRefugio).Length == 0, $"{semRefugio.Zone.Name}: '{chao(semRefugio)}'");
		foreach ((ServerPlayer quem, ZoneKey onde) in new[] { (ausente, zonaAntes), (daTerra, TerraSz), (semRefugio, CavernaDaTerraSz) })
		{
			ZoneList(onde.Hash).Remove(quem);
			if (!ZoneList(quem.Zone.Hash).Contains(quem)) ZoneList(quem.Zone.Hash).Add(quem);
		}
	}
}
