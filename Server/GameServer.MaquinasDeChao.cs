using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Forms;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (v0.2.1, lote T3 da tech) AS MAQUINAS DE CHAO E AS FERRAMENTAS DE PAREDE -- o reparador e o reforcador de
/// paredes, o reparador portatil e o destruidor, a estacao de recarga dos androides, a lua artificial e o emissor
/// de ondas Blutz. As regras de cada uma moram em `Core/Tech/MaquinasDeChao.cs`, com o que o original faz e o que
/// muda aqui; este arquivo e a costura com as construcoes (`Obra`), os blocos erguidos e a fera.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os verbos de maquina deste lote (o menu da tecla E). Falso quando o verbo nao e daqui.</summary>
	private bool ComandoDeMaquinaDeChao(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case MurosDeTech.VerboReforcar: ReforcarEmVolta(pl); return true;
			case MurosDeTech.VerboInfo: InfoDoReparador(pl); return true;
			case EstacaoDeRecarga.VerboInfo: InfoDaEstacao(pl); return true;
			case EstacaoDeRecarga.VerboMelhorar: MelhorarAEstacao(pl, arg); return true;
			case LuaArtificial.VerboAcender: AcenderALua(pl); return true;
			case LuaArtificial.VerboMelhorar: EnegrecerALua(pl, arg); return true;
			// (a segunda metade do lote) o simulador e a maquina de clonagem -- `GameServer.MaquinasGrandes.cs`.
			default: return ComandoDeMaquinaGrande(pl, cmd, arg);
		}
	}

	/// <summary>As acoes de mochila deste lote. Falso quando a acao nao e daqui.</summary>
	private bool AcaoDeFerramentaDeParede(ServerPlayer pl, string acao, string arg2)
	{
		switch (acao)
		{
			case MurosDeTech.AcaoReparar: RepararAoRedor(pl); return true;
			case MurosDeTech.AcaoDestruir: DestruirAoRedor(pl, arg2); return true;
			default: return false;
		}
	}

	// =====================================================================
	// AS FERRAMENTAS DE PAREDE -- `buildable.dm:41-176`. A regra esta em `MurosDeTech`.
	// =====================================================================
	/// <summary>As construcoes desta zona a ate <paramref name="tiles"/> tiles (por eixo, como o `range()`) de um ponto.</summary>
	private IEnumerable<Obra> ObrasNoRaio(ZoneKey zona, Vec2 centro, int tiles)
	{
		float raio = tiles * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f;
		foreach (Obra o in _noChao)
			if (o.Zona.Equals(zona) && Math.Abs(o.X - centro.X) <= raio && Math.Abs(o.Y - centro.Y) <= raio)
				yield return o;
	}

	/// <summary>Cura a armadura de tudo o que ha no raio -- o `healDamage(maxarmor)` que o original pretende. Devolve quantas curou.</summary>
	private int CurarObrasNoRaio(ZoneKey zona, Vec2 centro, int tiles)
	{
		int curadas = 0;
		foreach (Obra o in ObrasNoRaio(zona, centro, tiles))
		{
			if (o.Armadura >= o.ArmaduraMax) continue;
			o.Armadura = o.ArmaduraMax;
			curadas++;
		}
		return curadas;
	}

	private double _relogioDosReparos;

	/// <summary>De minuto em minuto, todo reparador e todo reforcador de pe cura o que ha em volta (`sleep(600)`).</summary>
	private void TickDosReparadores(double dt)
	{
		_relogioDosReparos += dt;
		if (_relogioDosReparos < MurosDeTech.SegundosEntreReparos) return;
		_relogioDosReparos = 0;
		RepararPelasAntenas();
	}

	/// <summary>A volta de reparo em si. Devolve quantas construcoes foram curadas (pra bancada).</summary>
	private int RepararPelasAntenas()
	{
		if (MurosDeTech.NaoReparaDeTeste) return 0;
		int curadas = 0;
		foreach (Obra antena in _noChao.Where(o => MurosDeTech.CuraSozinha(o.Tipo)).ToList())
			curadas += CurarObrasNoRaio(antena.Zona, new Vec2(antena.X, antena.Y), MurosDeTech.RaioEmTiles);
		if (curadas > 0) GravarMundo();
		return curadas;
	}

	private void InfoDoReparador(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, MurosDeTech.VerboInfo);
		if (o == null) { Avisar(pl, "não há reparador por perto."); return; }
		int noRaio = ObrasNoRaio(o.Zona, new Vec2(o.X, o.Y), MurosDeTech.RaioEmTiles).Count() - 1;
		Avisar(pl, $"{NomeDaObra(o)} -- cura a armadura de toda construção a até {MurosDeTech.RaioEmTiles} tiles, de minuto em "
				   + $"minuto ({noRaio} no alcance agora; a próxima volta em {Math.Max(MurosDeTech.SegundosEntreReparos - _relogioDosReparos, 0):0} s). "
				   + "O que já caiu não volta.");
	}

	/// <summary>
	/// O `Click()` do reforcador: a armadura maxima das construcoes e a resistencia das paredes erguidas, a 15 tiles,
	/// sobem pro teto de tecnologia de quem clica -- e so SOBEM (ver o cabecalho do `MurosDeTech`).
	/// </summary>
	private void ReforcarEmVolta(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, MurosDeTech.VerboReforcar);
		if (o == null) { Avisar(pl, "não há reforçador por perto."); return; }
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não opera nada."); return; }

		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		double teto = TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod);
		var centro = new Vec2(o.X, o.Y);

		int obras = 0;
		foreach (Obra perto in ObrasNoRaio(o.Zona, centro, MurosDeTech.RaioEmTiles))
		{
			if (perto.ArmaduraMax < teto) { perto.ArmaduraMax = teto; obras++; }
			perto.Armadura = perto.ArmaduraMax;
		}

		int paredes = 0;
		if (_bases.TryGetValue(o.Zona.Hash, out BaseDaZona? z))
		{
			(int cx, int cy) = CatalogoDeObras.Celula(o.X, o.Y);
			foreach (BlocoDePe b in z.Celulas.Values)
			{
				if (Math.Abs(b.X - cx) > MurosDeTech.RaioEmTiles || Math.Abs(b.Y - cy) > MurosDeTech.RaioEmTiles) continue;
				if (b.Resistencia >= teto) continue;
				b.Resistencia = teto;
				paredes++;
			}
			if (paredes > 0) _basesSujas = true;
		}

		GravarMundo();
		AnunciarAVista(o, $"{NomeVisivel(pl)} aciona {NomeDaObra(o)}: tudo em volta é reforçado.");
		Avisar(pl, obras + paredes == 0
			? "tudo o que está no alcance já é tão resistente quanto a sua tecnologia consegue fazer -- as armaduras foram curadas."
			: $"{obras} construção(ões) e {paredes} parede(s) erguida(s) sobem pro teto da sua tecnologia ({PoderEscrito(pl, teto)}); as armaduras foram curadas.");
	}

	/// <summary>O `Click()` do reparador portatil: cura as construcoes a um tile de quem o usa.</summary>
	private void RepararAoRedor(ServerPlayer pl)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não repara nada."); return; }
		int curadas = MurosDeTech.NaoReparaDeTeste ? 0 : CurarObrasNoRaio(pl.Zone, pl.Pos, MurosDeTech.RaioDeMaoEmTiles);
		if (curadas > 0) GravarMundo();
		Avisar(pl, curadas == 0 ? "não há nada danificado ao seu alcance." : $"você repara {curadas} construção(ões) ao seu redor.");
	}

	/// <summary>
	/// O `Click()` do destruidor (`buildable.dm:149-176`): por 5.000 zeni (ou o dobro em recursos), cai tudo o que ha
	/// a um tile e que a tecnologia de quem paga alcanca (ou que e dele): as construcoes, os blocos erguidos e o
	/// cenario do mapa. Cobra antes, como la -- e nao devolve se nao havia o que derrubar.
	/// </summary>
	private void DestruirAoRedor(ServerPlayer pl, string arg2)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não destrói nada."); return; }
		if (Voo.AtravessaCenario(pl.Altitude)) { Avisar(pl, "daqui de cima você não alcança o chão: desça."); return; }
		(_, Moeda moeda) = Recurso.LerPedidoPago(arg2);
		if (!CobrarOuExplicar(pl, MurosDeTech.CustoDeDestruir, moeda, "acionar o destruidor")) return;

		Jandirus.Core.Stats.Fighter f = pl.Ficha;
		double teto = TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod);
		bool Alcanca(double resistencia, string donoConta) =>
			MurosDeTech.DestroiTudoDeTeste || resistencia <= teto || (pl.Conta.Length > 0 && donoConta == pl.Conta);

		// AS CONSTRUCOES: `if(M.fragile&&(M.armor<=usr.intBPcap||M.proprietor==usr.key)) M.takeDamage(...)`.
		int obras = 0;
		foreach (Obra o in ObrasNoRaio(pl.Zone, pl.Pos, MurosDeTech.RaioDeMaoEmTiles).ToList())
		{
			if (!Alcanca(o.Armadura, o.DonoConta)) continue;
			// (uma pancada que nenhuma armadura segura: o piso de 75% do `Armadura.Bater` fica pra tras)
			Estragar(o, o.ArmaduraMax * 5 + o.Armadura, null);
			if (!_noChao.Contains(o)) obras++;
		}

		// OS BLOCOS ERGUIDOS E O CENARIO: `if(T.Resistance<=usr.intBPcap||T.proprietor==usr.key) T.Destroy()`.
		int celulas = 0;
		(int cx0, int cy0) = CelulaDoPonto(pl.Pos);
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		for (int dy = -MurosDeTech.RaioDeMaoEmTiles; dy <= MurosDeTech.RaioDeMaoEmTiles; dy++)
			for (int dx = -MurosDeTech.RaioDeMaoEmTiles; dx <= MurosDeTech.RaioDeMaoEmTiles; dx++)
			{
				int cx = cx0 + dx, cy = cy0 + dy;
				if (BlocoEm(pl.Zone, cx, cy) is { } bloco)
				{
					if (!Alcanca(bloco.Resistencia, bloco.DonoConta)) continue;
					DerrubarBloco(bloco, pl);
					celulas++;
					continue;
				}
				if (mapa == null || cx < 0 || cy < 0 || cx >= mapa.Width || cy >= mapa.Height) continue;
				if (mapa.NaBorda(cx, cy) || mapa.EhAgua(cx, cy) || mapa.EhNuvem(cx, cy)) continue;
				if (!Alcanca(Empurrao.ResistenciaPadrao, "")) continue;
				if (DerrubarCelula(pl.Zone, cx, cy)) celulas++;
			}

		Falar(pl, Protocol.Fala.Emote, "aciona um destruidor!");
		Avisar(pl, obras + celulas == 0
			? "o destruidor dispara -- e nada ao seu redor cede (ou é resistente demais pra sua tecnologia, ou não há o que derrubar)."
			: $"o destruidor leva {obras} construção(ões) e {celulas} tile(s) ao seu redor.");
	}

	// =====================================================================
	// A ESTACAO DE RECARGA -- `Androids.dm:568-675`. A regra esta em `EstacaoDeRecarga`.
	// =====================================================================
	/// <summary>O estado desta estacao; a recem-assentada nasce com a energia e a eficiencia de fabrica.</summary>
	private static EstadoDeTech EstadoDaEstacao(Obra o)
	{
		if (o.Tech is { } ja && ja.N.ContainsKey(EstacaoDeRecarga.ChaveNivel)) return ja;
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		e.N[EstacaoDeRecarga.ChaveEnergia] = EstacaoDeRecarga.EnergiaDeFabrica;
		e.N[EstacaoDeRecarga.ChaveNivel] = 1;
		e.N[EstacaoDeRecarga.ChaveEficiencia] = EstacaoDeRecarga.EficienciaDeFabrica;
		return e;
	}

	/// <summary>Uma volta do relogio de todas as estacoes (`proc/Ticker()`): recarrega quem esta em cima, e rende.</summary>
	private void TickDasEstacoes()
	{
		foreach (Obra o in _noChao)
		{
			if (o.Tipo != EstacaoDeRecarga.Tipo || !o.Aparafusada) continue;   // `if(Bolted)`
			EstadoDeTech e = EstadoDaEstacao(o);
			int nivel = (int)e.Num(EstacaoDeRecarga.ChaveNivel, 1);
			double eficiencia = e.Num(EstacaoDeRecarga.ChaveEficiencia, EstacaoDeRecarga.EficienciaDeFabrica);
			double energia = e.Num(EstacaoDeRecarga.ChaveEnergia);

			// `for(var/mob/M in view(0)) ... break`: quem esta EM CIMA dela, e so o primeiro.
			if (energia > 0 && _zones.TryGetValue(o.Zona.Hash, out List<ServerPlayer>? naZona))
			{
				(int ox, int oy) = CatalogoDeObras.Celula(o.X, o.Y);
				foreach (ServerPlayer pl in naZona)
				{
					if (pl.Ficha.dead || CelulaDoPonto(pl.Pos) != (ox, oy)) continue;
					if (pl.Race != "Android" && !EstacaoDeRecarga.RecarregaQualquerUmDeTeste) break;
					Jandirus.Core.Stats.Fighter f = pl.Ficha;
					double custo = EstacaoDeRecarga.CustoPorRecarga(eficiencia);
					if (f.stamina < f.maxstamina)
					{
						f.stamina = Math.Min(f.stamina + eficiencia, f.maxstamina);
						energia -= custo;
					}
					if (f.Ki < f.MaxKi)
					{
						f.Ki = Math.Min(f.Ki + EstacaoDeRecarga.KiPorVolta(eficiencia, f.MaxKi), f.MaxKi);
						energia -= custo;
					}
					break;
				}
			}

			energia += EstacaoDeRecarga.RendaPorVolta(nivel, e.Ligado(EstacaoDeRecarga.ChaveSolar));
			e.N[EstacaoDeRecarga.ChaveEnergia] = Math.Clamp(energia, 0, EstacaoDeRecarga.Capacidade(nivel));
		}
	}

	private void InfoDaEstacao(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, EstacaoDeRecarga.VerboInfo);
		if (o == null) { Avisar(pl, "não há estação de recarga por perto."); return; }
		EstadoDeTech e = EstadoDaEstacao(o);
		int nivel = (int)e.Num(EstacaoDeRecarga.ChaveNivel, 1);
		double eficiencia = e.Num(EstacaoDeRecarga.ChaveEficiencia);
		// `"This station has [elec_energy] energy of a maximum of [elec_energy_max]."`
		Avisar(pl, $"{NomeDaObra(o)} -- energia {e.Num(EstacaoDeRecarga.ChaveEnergia):0} de {EstacaoDeRecarga.Capacidade(nivel):0}; "
				   + $"nível {nivel} de {EstacaoDeRecarga.NivelMaximo}; eficiência {eficiencia:0.#} de {EstacaoDeRecarga.EficienciaMaxima:0}; "
				   + (e.Ligado(EstacaoDeRecarga.ChaveSolar) ? "com painéis solares." : "sem painéis solares.")
				   + (o.Aparafusada ? "" : " Solta, ela não funciona: aparafuse.")
				   + " Recarrega o fôlego e o Ki do androide que estiver em cima dela.");
		Avisar(pl, $"melhorias: eficiência +{EstacaoDeRecarga.PassoDeEficiencia:0.#} por {Recurso.DoisPrecos(EstacaoDeRecarga.CustoDaEficiencia(eficiencia))}; "
				   + $"nível +1 por {Recurso.DoisPrecos(EstacaoDeRecarga.CustoDoNivel(nivel))}; painéis solares por {Recurso.DoisPrecos(EstacaoDeRecarga.CustoDosPaineis)}.");
	}

	/// <summary>`verb/Upgrade()`: eficiencia, nivel ou paineis -- em zeni ou (o dobro) em recursos.</summary>
	private void MelhorarAEstacao(ServerPlayer pl, string arg)
	{
		Obra? o = ObraQueAceita(pl, EstacaoDeRecarga.VerboMelhorar);
		if (o == null) { Avisar(pl, "não há estação de recarga por perto."); return; }
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não melhora nada."); return; }
		(string oQue, Moeda moeda) = Recurso.LerPedidoPago(arg);
		EstadoDeTech e = EstadoDaEstacao(o);
		int nivel = (int)e.Num(EstacaoDeRecarga.ChaveNivel, 1);
		double eficiencia = e.Num(EstacaoDeRecarga.ChaveEficiencia);

		switch (oQue)
		{
			case EstacaoDeRecarga.Eficiencia:
				// `if(efficiency>=4) "You can't upgrade this any further!"`.
				if (eficiencia >= EstacaoDeRecarga.EficienciaMaxima) { Avisar(pl, "a eficiência não sobe mais."); return; }
				if (!CobrarOuExplicar(pl, EstacaoDeRecarga.CustoDaEficiencia(eficiencia), moeda, "a eficiência da estação")) return;
				e.N[EstacaoDeRecarga.ChaveEficiencia] = eficiencia + EstacaoDeRecarga.PassoDeEficiencia;
				break;
			case EstacaoDeRecarga.Nivel:
				if (nivel >= EstacaoDeRecarga.NivelMaximo) { Avisar(pl, "o nível não sobe mais."); return; }
				if (!CobrarOuExplicar(pl, EstacaoDeRecarga.CustoDoNivel(nivel), moeda, "o nível da estação")) return;
				e.N[EstacaoDeRecarga.ChaveNivel] = nivel + 1;
				break;
			case EstacaoDeRecarga.Solar:
				// `"You already have this upgrade!"`.
				if (e.Ligado(EstacaoDeRecarga.ChaveSolar)) { Avisar(pl, "ela já tem os painéis solares."); return; }
				if (!CobrarOuExplicar(pl, EstacaoDeRecarga.CustoDosPaineis, moeda, "os painéis solares")) return;
				e.N[EstacaoDeRecarga.ChaveSolar] = 1;
				break;
			default:
				Avisar(pl, "a estação não tem essa melhoria.");
				return;
		}
		GravarMundo();
		// `to_chat(view(usr), "Recharge Station upgraded.")`.
		AnunciarAVista(o, $"{NomeDaObra(o)}: melhorada.");
	}

	// =====================================================================
	// A LUA ARTIFICIAL E O EMISSOR -- `Tier 1.dm:364-417`, `Tier3.dm:69-97`. A regra esta em `LuaArtificial`.
	// =====================================================================
	/// <summary>Quem cada lua acesa ja olhou (id da obra -> corpos): cada um e olhado uma vez por acendimento.</summary>
	private readonly Dictionary<int, HashSet<int>> _olhadosPelaLua = [];

	private static bool LuaAcesa(Obra o, long agora) =>
		LuaArtificial.EhFonte(o.Tipo) && o.Tech is { } e && e.Num(LuaArtificial.ChaveAcesaAte) > agora;

	/// <summary>Este corpo esta ao alcance desta lua? (O `view(src)` de la: a tela.)</summary>
	private static bool AoAlcanceDaLua(Obra lua, ServerPlayer pl)
	{
		float raio = LuaArtificial.RaioEmTiles * ZoneCollision.TileSize;
		return lua.Zona.Hash == pl.Zone.Hash && Math.Abs(pl.Pos.X - lua.X) <= raio && Math.Abs(pl.Pos.Y - lua.Y) <= raio;
	}

	/// <summary>
	/// HA UMA LUA ARTIFICIAL (OU UM EMISSOR) ACESA A VISTA DESTE CORPO? -- a SEGUNDA fonte da fera, ao lado da lua
	/// cheia do ceu (`AFonteDaFera`): enquanto ela arde a fera fica, e quando ela se apaga -- ou e destruida -- a
	/// fera se desfaz.
	/// </summary>
	private bool LuaArtificialAVista(ServerPlayer pl)
	{
		if (LuaArtificial.NaoEFonteDeTeste) return false;
		long agora = NowMs();
		foreach (Obra o in _noChao)
			if (LuaAcesa(o, agora) && AoAlcanceDaLua(o, pl)) return true;
		return false;
	}

	/// <summary>`verb/Moon()` e `verb/Activate()`: acende. Uma vez so -- ela se apaga e some no fim.</summary>
	private void AcenderALua(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, LuaArtificial.VerboAcender);
		if (o == null) { Avisar(pl, "não há lua artificial por perto."); return; }
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		// `"It has already been activated..."`.
		if (e.Num(LuaArtificial.ChaveAcesaAte) > 0) { Avisar(pl, "ela já foi ativada..."); return; }

		bool negra = e.Ligado(LuaArtificial.ChaveNegra);
		double segundos = LuaArtificial.SegundosAcesa(o.Tipo, negra);
		e.N[LuaArtificial.ChaveAcesaAte] = NowMs() + (long)(segundos * 1000);
		_olhadosPelaLua.Remove(o.Id);
		GravarMundo();
		MandarObras(o.Zona);   // o desenho passa a ser o da lua acesa (`EstadoDaArte`)
		// `to_chat(view(usr), "[usr] activates an artificial moon!")` / `"[usr] activates the emitter!"`.
		AnunciarAVista(o, o.Tipo == LuaArtificial.Emissor
			? $"{NomeVisivel(pl)} ativa o emissor de ondas Blutz!"
			: $"{NomeVisivel(pl)} acende uma lua artificial{(negra ? " NEGRA" : "")}!");
		GD.Print($"[server] {pl.Name} acendeu {o.Tipo} em {o.Zona.Name} ({segundos:0} s)");
	}

	/// <summary>"Upgrade_Moon": a lua comum vira a NEGRA, que manda pro Dourado quem ja pode ser Dourado.</summary>
	private void EnegrecerALua(ServerPlayer pl, string arg)
	{
		Obra? o = ObraQueAceita(pl, LuaArtificial.VerboMelhorar);
		if (o == null) { Avisar(pl, "não há lua artificial por perto."); return; }
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		if (e.Ligado(LuaArtificial.ChaveNegra)) { Avisar(pl, "esta já é uma lua negra."); return; }
		if (e.Num(LuaArtificial.ChaveAcesaAte) > 0) { Avisar(pl, "ela já foi ativada: não dá mais pra mexer nela."); return; }
		if (pl.Ficha.techskill < LuaArtificial.TecnologiaDaLuaNegra)
		{ Avisar(pl, $"a lua negra pede {LuaArtificial.TecnologiaDaLuaNegra:0} de tecnologia."); return; }
		(_, Moeda moeda) = Recurso.LerPedidoPago(arg);
		if (!CobrarOuExplicar(pl, LuaArtificial.CustoDaLuaNegra, moeda, "a lua negra")) return;
		e.N[LuaArtificial.ChaveNegra] = 1;
		GravarMundo();
		Avisar(pl, "a lua artificial agora é uma LUA NEGRA: acesa, manda pro Oozaru Dourado quem já pode ser Dourado.");
	}

	/// <summary>
	/// A cada meio segundo (`sleep(5)`): toda lua acesa olha quem a ve, e a que venceu o prazo se apaga e some
	/// (`del(src)`) -- as feras que ela fez se desfazem no tique delas (`AFonteDaFera`).
	/// </summary>
	private void TickDasLuas()
	{
		long agora = NowMs();
		List<Obra>? apagadas = null;
		foreach (Obra o in _noChao)
		{
			if (!LuaArtificial.EhFonte(o.Tipo) || o.Tech is not { } e) continue;
			double ate = e.Num(LuaArtificial.ChaveAcesaAte);
			if (ate <= 0) continue;
			if (ate <= agora) { (apagadas ??= []).Add(o); continue; }
			if (LuaArtificial.NaoEFonteDeTeste || !_zones.TryGetValue(o.Zona.Hash, out List<ServerPlayer>? naZona)) continue;

			HashSet<int> olhados = _olhadosPelaLua.TryGetValue(o.Id, out HashSet<int>? ja) ? ja : _olhadosPelaLua[o.Id] = [];
			foreach (ServerPlayer pl in naZona.ToList())
			{
				if (!AoAlcanceDaLua(o, pl) || !olhados.Add(pl.Id)) continue;
				OlharALuaArtificial(pl, o, e.Ligado(LuaArtificial.ChaveNegra));
			}
		}

		if (apagadas == null) return;
		foreach (Obra o in apagadas)
		{
			_olhadosPelaLua.Remove(o.Id);
			_noChao.Remove(o);
			AplicarColisaoDasObras(o.Zona);
			MandarObras(o.Zona);
			AnunciarAVista(o, $"{NomeDaObra(o)} se apaga e se desfaz.");
		}
		GravarMundo();
	}

	/// <summary>
	/// UM CORPO VE A LUA ACESA. O emissor devolve o rabo do Saiyajin antes (`if(!M.has_Tail()) M.Tail_Grow()`) --
	/// menos o que foi perdido de vez (regra do dono, `RaboDoSaiyajin`): esse nem o emissor refaz. Depois, a fera.
	/// </summary>
	private void OlharALuaArtificial(ServerPlayer pl, Obra lua, bool negra)
	{
		if (pl.Combate is not { } cd || pl.Ficha.dead || pl.Oozaru != FormaOozaru.Nao) return;

		if (LuaArtificial.DevolveORabo(lua.Tipo) && string.Equals(pl.Race, "Saiyan", StringComparison.OrdinalIgnoreCase)
			&& cd.Corpo.Achar("Rabo") is { Decepado: true } rabo && cd.Corpo.Regenerar(rabo))
		{
			AjustarGanhoDoRabo(pl);
			pl.CorpoEnviado = "";
			Avisar(pl, "as ondas do emissor fazem o seu rabo crescer de volta.");
		}
		if (!TemRaboInteiro(pl) || pl.Ficha.KO) return;

		// A LUA COMUM e a lua cheia posta no chao: quem decide e o `Apeshit` de sempre.
		if (!LuaArtificial.PuxaODourado(lua.Tipo, negra))
		{
			// `to_chat(M, "You catch a glimpse of the moon.")`.
			Avisar(pl, "você vê a lua artificial de relance.");
			Apeshit(pl);
			return;
		}

		// A LUA NEGRA E O EMISSOR (`GoldenApeshit()`): pro Dourado quem ja pode ser Dourado; pra fera comum, o resto.
		double genoma = pl.Ficha.Genoma?.RacePercent("Saiyan")
			?? (string.Equals(pl.Race, "Saiyan", StringComparison.OrdinalIgnoreCase) ? 100 : 0);
		if (Oozaru.PodeVirar(genoma, true, false, pl.Oozaru) != RecusaOozaru.Pode) return;
		bool dourado = Oozaru.PodeDourado(pl.Ficha.SaiyanLineage, pl.Forma.Maestria, pl.Ficha.BP, pl.Forma.Limiares) == RecusaOozaru.Pode;
		Avisar(pl, lua.Tipo == LuaArtificial.Emissor ? "as ondas Blutz atravessam você." : "você vê a lua negra de relance!");
		VirarFera(pl, dourado ? FormaOozaru.Dourado : FormaOozaru.Regular);
	}

	// =====================================================================
	// O RELOGIO DO LOTE
	// =====================================================================
	private double _relogioDasLuas, _relogioDasEstacoes;

	private void TickDasMaquinasDeChao(double dt)
	{
		TickDosReparadores(dt);

		_relogioDasLuas += dt;
		if (_relogioDasLuas >= LuaArtificial.SegundosEntreOlhares)
		{
			_relogioDasLuas = 0;
			TickDasLuas();
		}

		_relogioDasEstacoes += dt;
		if (_relogioDasEstacoes >= 1)
		{
			_relogioDasEstacoes -= 1;
			TickDasEstacoes();
		}
	}
}
