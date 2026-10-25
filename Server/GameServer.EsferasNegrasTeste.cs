using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DAS ESFERAS QUE SE ESPALHAM DE VERDADE E DAS BLACK STAR (`--esferanegrateste`).
///
/// O dono (2026-10-21): cada uso espalha as esferas em lugar novo no planeta delas, e as Black
/// Star sao um set global, um desejo, 5x o Shenron, e saem do planeta.
///
/// (2026-10-23, dono) O desejo das Black Star PARTE O PLANETA onde elas foram usadas (familia X) e a lista delas ganha "Aprender a
/// Danca da Fusao" (familia D). Os desejos de verdade daqui em diante rodam DENTRO do palco das mortes (`PalcoDeMortesDeBancada`):
/// sem ele, os 310 s da Terra iriam pro `planetas-mortos.json` -- o acidente da `--escudoteste` (`GameServer.Destruicao.cs`).
///
///     Godot --headless --path . --host --rede 10130 --esferanegrateste --raca Human --conta bancada_negra --nome Negra
/// </summary>
public sealed partial class GameServer
{
	private bool _negraDeTeste;
	private int _okNegra, _falhaNegra;

	private void ChecaNegra(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _okNegra++; GD.Print($"[esferanegra] ok  {nome}"); }
		else
		{
			_falhaNegra++;
			GD.Print($"[esferanegra] FALHA {nome}" + (detalhe.Length > 0 ? "   " + detalhe : ""));
		}
	}

	private void RodarBancadaDasNegras(ServerPlayer pl)
	{
		SalFixoDeTeste = false;
		EsferaForaDoHabitatDeTeste = false;
		NegraPrendeNoPlanetaDeTeste = false;
		BlackStarSemMultiplicadorDeTeste = false;
		BlackStarComSupremoDeTeste = false;
		ReassentarTudoNoBootDeTeste = DeslogarEspalhaDeTeste = false;
		NegraNaoExplodeOPlanetaDeTeste = NegraCaiNoPlanetaCondenadoDeTeste = false;
		DancaEmTodoDragaoDeTeste = DancaSemSerEnsinadaDeTeste = DancaRepetidaDeTeste = false;
		try { CorpoDaBancadaNegra(pl); }
		catch (Exception e) { ChecaNegra("a bancada nao estourou", false, e.ToString()); }
		finally
		{
			SalFixoDeTeste = false;
			EsferaForaDoHabitatDeTeste = false;
			NegraPrendeNoPlanetaDeTeste = false;
			BlackStarSemMultiplicadorDeTeste = false;
			BlackStarComSupremoDeTeste = false;
			ReassentarTudoNoBootDeTeste = DeslogarEspalhaDeTeste = false;
			NegraNaoExplodeOPlanetaDeTeste = NegraCaiNoPlanetaCondenadoDeTeste = false;
			DancaEmTodoDragaoDeTeste = DancaSemSerEnsinadaDeTeste = DancaRepetidaDeTeste = false;
			GD.Print($"[esferanegra] ================ {_okNegra} OK, {_falhaNegra} FALHA(S) ================");
		}
	}

	private void CorpoDaBancadaNegra(ServerPlayer pl)
	{
		SetDeEsferas? eterno = _sets.Find(s => s.Eterno);
		ChecaNegra("o Porunga de Namek esta de pe pra medir o espalhamento", eterno != null);
		if (eterno == null) return;
		var habitat = HabitatDaZona(eterno.Zona);
		ChecaNegra("Namek tem habitat (terra navegavel, sem ilhota)", habitat is { Vagas: > 0 });
		if (habitat == null) return;

		bool Diferem()
		{
			EspalharOSet(eterno);
			var a = OndeEsta(eterno);
			EspalharOSet(eterno);
			var b = OndeEsta(eterno);
			int mudou = 0;
			for (int i = 0; i < a.Count; i++)
				if ((a[i] - b[i]).Length > 1f) mudou++;
			return mudou >= Esferas.Total - 1;
		}

		bool Navegaveis()
		{
			EspalharOSet(eterno);
			foreach (Esfera e in _esferas.Where(x => x.Set == eterno.Id))
			{
				int cx = (int)MathF.Floor(e.X / ZoneCollision.TileSize);
				int cy = (int)MathF.Floor(e.Y / ZoneCollision.TileSize);
				if (!habitat.Navegavel(cx, cy)) return false;
			}
			return _esferas.Count(x => x.Set == eterno.Id) == Esferas.Total;
		}

		ChecaNegra("duas espalhadas seguidas do mesmo set caem em lugares diferentes", Diferem());
		ChecaNegra("nenhuma esfera cai em agua ou ilhota", Navegaveis());

		EspalharOSet(eterno);
		ulong sal = eterno.LerSal();
		ulong[] lugares = EsferasNegras.LugaresDoSal(sal, habitat.Vagas);
		int bateram = 0;
		foreach (Esfera e in _esferas.Where(x => x.Set == eterno.Id))
		{
			(int x, int y) = habitat.Vaga(lugares[e.Numero - 1]);
			float px = x * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f;
			float py = y * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f;
			if (e.X.Equals(px) && e.Y.Equals(py)) bateram++;
		}
		ChecaNegra("o mesmo sal poem cada esfera na vaga do habitat", bateram == Esferas.Total, $"{bateram}");

		MutacaoNegra("o sal fixo poem as duas espalhadas no mesmo lugar",
			"sal cravado",
			Diferem,
			() => SalFixoDeTeste = true,
			() => SalFixoDeTeste = false);

		MutacaoNegra("fora do habitat a regua da agua reprova",
			"assentar na celula 0,0",
			Navegaveis,
			() => EsferaForaDoHabitatDeTeste = true,
			() => EsferaForaDoHabitatDeTeste = false);

		// ---------------------------------------------------------------- Black Star
		ChecaNegra("ha um set de 7 Black Star", _negras.Count == Esferas.Total);
		ChecaNegra("cada uma esta num planeta", _negras.All(e => Espaco.EhPlaneta(e.Zona)));
		ChecaNegra("nenhuma esta num planeta morto", _negras.All(e => !ZonaMorta(e.Zona)));
		int planetas = _negras.Select(e => e.Zona.Hash).Distinct().Count();
		ChecaNegra("de preferencia 7 planetas diferentes", planetas == Esferas.Total, $"{planetas}");
		ChecaNegra("as assentadas nao cairam em agua nem ilhota", _negras.Where(e => !e.PrecisaAssentar).All(e =>
		{
			var h = HabitatDaZona(e.Zona);
			return h != null && h.Navegavel(e.Cx, e.Cy);
		}));

		foreach (Esfera comum in _esferas) comum.Portador = 0;
		foreach (EsferaNegra n in _negras) n.Portador = pl.Id;
		ChecaNegra("as comuns continuam prendendo e a Black Star nao entra nessa frase",
			PorQueAsEsferasPrendem(pl).Length == 0, PorQueAsEsferasPrendem(pl));

		MoveToZone(pl.Id, ZonaDoEspaco, new Vec2(400, 400));
		TickDasNegras();
		ChecaNegra("da pra sair do planeta carregando a Black Star",
			_negras.All(e => e.Portador == pl.Id) && _negras.All(e => Espaco.EhEspaco(e.Zona)));

		MutacaoNegra("com o defeito, sair do planeta espalha a Black Star",
			"ela prende como as comuns",
			() =>
			{
				foreach (EsferaNegra n in _negras) { n.Portador = pl.Id; n.PorZona(pl.Zone); }
				MoveToZone(pl.Id, ZoneKey.Premade("Earth"), PontoDeNascimento(ZoneKey.Premade("Earth")));
				TickDasNegras();
				MoveToZone(pl.Id, ZonaDoEspaco, new Vec2(800, 800));
				TickDasNegras();
				return _negras.All(e => e.Portador == pl.Id);
			},
			() => NegraPrendeNoPlanetaDeTeste = true,
			() => NegraPrendeNoPlanetaDeTeste = false);

		// Morreu no espaco: espalha. Desmaiou num planeta: cai no chao de la.
		foreach (EsferaNegra n in _negras) { n.Portador = pl.Id; n.PorZona(ZonaDoEspaco); }
		pl.Ficha.dead = true;
		TickDasNegras();
		ChecaNegra("morrer fora de um planeta espalha a Black Star de novo",
			_negras.All(e => e.Portador == 0 && Espaco.EhPlaneta(e.Zona)));
		pl.Ficha.dead = false;
		pl.Ficha.KO = false;

		var terra = ZoneKey.Premade("Earth");
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		EsferaNegra uma = _negras[0];
		uma.Portador = pl.Id;
		uma.PorZona(terra);
		pl.Ficha.KO = true;
		TickDasNegras();
		ChecaNegra("desmaiar na superficie larga a esfera no planeta, sem espalhar",
			uma.Portador == 0 && uma.Zona.Hash == terra.Hash);
		pl.Ficha.KO = false;

		// Marcos: uma vez, e o numero vem vezes 5.
		pl.Ficha.wishedpoints = 0;
		bool primeiro = DesejoDeMarcos(pl, _setNegro);
		int ganhou = pl.Ficha.wishedpoints;
		bool segundo = DesejoDeMarcos(pl, _setNegro);
		ChecaNegra("marcos da Black Star da 5 vezes os 2 do Shenron",
			primeiro && ganhou == (int)(Desejos.MarcosDoDesejo * EsferasNegras.MultiplicadorDaBlackStar),
			$"wishedpoints {ganhou}");
		ChecaNegra("marcos continua uma vez por personagem", !segundo);
		pl.Ficha.wishedpoints = 0;

		double zeniAntes = pl.Ficha.Zeni;
		DesejoDeDinheiro(pl, _setNegro);
		double delta = pl.Ficha.Zeni - zeniAntes;
		ChecaNegra("a riqueza da Black Star vale 5 vezes a do Shenron",
			Math.Abs(delta - Desejos.ZeniDoDesejo * EsferasNegras.MultiplicadorDaBlackStar) < 1,
			$"delta {delta}");
		MutacaoNegra("sem o multiplicador a riqueza volta a ser a do Shenron",
			"fator 1",
			() =>
			{
				double antes = pl.Ficha.Zeni;
				DesejoDeDinheiro(pl, _setNegro);
				return Math.Abs(pl.Ficha.Zeni - antes - Desejos.ZeniDoDesejo * EsferasNegras.MultiplicadorDaBlackStar) < 1;
			},
			() => BlackStarSemMultiplicadorDeTeste = true,
			() => BlackStarSemMultiplicadorDeTeste = false);

		// Invocar na superficie, sem estatua. No espaco, nao.
		_setNegro.AtivoEm = 0;
		_setNegro.Pedidos = 0;
		_setNegro.Inerte = false;
		foreach (EsferaNegra n in _negras) { n.Portador = pl.Id; n.PorZona(terra); }
		MoveToZone(pl.Id, ZonaDoEspaco, new Vec2(100, 100));
		_invocacoes.Remove(EsferasNegras.IdDoSet);
		InvocarODragao(pl);
		ChecaNegra("no espaco as Black Star nao invocam", !_invocacoes.ContainsKey(EsferasNegras.IdDoSet));

		// (2026-10-23) DAQUI ATE O FIM DAS FAMILIAS X E D, O PALCO DAS MORTES: o desejo de verdade parte o planeta onde foi feito -- a
		// Terra, aqui. Declarado com `using` (uma excecao no meio ainda devolve o mundo) e fechado a mao antes do X10, que le o que ele
		// barrou: o `EscritasBarradas` so e preenchido no `Dispose`.
		using PalcoDeMortes palco = PalcoDeMortesDeBancada();
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		foreach (EsferaNegra n in _negras) { n.Portador = pl.Id; n.PorZona(terra); n.PrecisaAssentar = false; }
		InvocarODragao(pl);
		ChecaNegra("na superficie, com as 7, o Shenlong sobe sem estatua",
			_invocacoes.ContainsKey(EsferasNegras.IdDoSet));
		bool temSupremo = MenuDaqui(pl, _setNegro).Any(d => d.Id == "supremo");
		ChecaNegra("o Supremo nao entra na lista das Black Star", !temSupremo);
		MutacaoNegra("com o defeito o Supremo aparece na lista",
			"lista com supremo",
			() => !MenuDaqui(pl, _setNegro).Any(d => d.Id == "supremo"),
			() => BlackStarComSupremoDeTeste = true,
			() => BlackStarComSupremoDeTeste = false);

		double zeniDoDesejo = pl.Ficha.Zeni;
		List<string> ouviuNoDesejo = Ouvir(() => PedirDesejo(pl, "dinheiro"));
		ChecaNegra("o desejo unico consome e multiplica o zeni",
			Math.Abs(pl.Ficha.Zeni - zeniDoDesejo - Desejos.ZeniDoDesejo * EsferasNegras.MultiplicadorDaBlackStar) < 1
			&& !_invocacoes.ContainsKey(EsferasNegras.IdDoSet));
		double espera = _setNegro.AtivoEm - TempoDoMundo;
		ChecaNegra("depois do desejo elas ficam pedra por um ano do jogo",
			espera > Esferas.SegundosDe(EsferasNegras.OffTimeEmAnos) * 0.9
			&& espera < Esferas.SegundosDe(EsferasNegras.OffTimeEmAnos) + 5,
			$"espera {espera:0} s");
		ChecaNegra("e se espalham de novo por planetas",
			_negras.All(e => e.Portador == 0 && Espaco.EhPlaneta(e.Zona)));

		FamiliaDaExplosaoNegra(pl, terra, ouviuNoDesejo);
		FamiliaDaDancaNegra(pl, terra, eterno);
		palco.Dispose();
		ChecaNegra("X10. (palco) as contagens e a explosao desta bancada ficaram na memoria: as gravacoes do planetas-mortos.json foram "
				   + "barradas, e a Terra e Arlia voltam vivas",
			palco.EscritasBarradas > 0 && !ZonaCondenada(terra) && !ZonaCondenada(Cobaia),
			$"{palco.EscritasBarradas} gravacao(oes) barrada(s); morreram aqui: {palco.NomesQueMorreram}");

		// Disco de largura fixa, ida e volta.
		SalvarNegras();
		long tamanho = new System.IO.FileInfo(CaminhoDasNegras).Length;
		string texto = System.IO.File.ReadAllText(CaminhoDasNegras);
		var memoria = _negras.Select(e => (e.Numero, e.Zona.Hash, e.Cx, e.Cy)).ToList();
		string salGuardado = _setNegro.SalHex;
		foreach (EsferaNegra n in _negras) n.ZonaNome = "SUMIU";
		CarregarNegras();
		bool voltou = _negras.Count == Esferas.Total
			&& _setNegro.SalHex == salGuardado
			&& memoria.All(m => _negras.Any(e => e.Numero == m.Numero && e.Zona.Hash == m.Hash && e.Cx == m.Cx && e.Cy == m.Cy));
		SalvarNegras();
		long tamanho2 = new System.IO.FileInfo(CaminhoDasNegras).Length;
		ChecaNegra("o esferas-negras.txt volta com sal, planeta e celula", voltou, texto.Length > 0 ? "" : "vazio");
		ChecaNegra("dois salvamentos tem o mesmo tamanho (o retrato da limpeza)", tamanho == tamanho2 && tamanho > 0,
			$"{tamanho} vs {tamanho2}");

		// (2026-10-21, revisao) A ESFERA LARGADA NAO PULA NO REINICIO. A ida e volta de cima so tinha esfera na vaga do sal -- e o
		// boot reassentava TODAS pelo sal, entao passava com o defeito. Aqui uma esfera no chao fora da vaga (largada por alguem)
		// tem de voltar na MESMA celula.
		bool LargadaFica()
		{
			EsferaNegra? e = _negras.Find(x => x.Portador == 0 && !x.PrecisaAssentar && Espaco.EhPlaneta(x.Zona)
				&& HabitatDaZona(x.Zona) is { Vagas: > 1 });
			if (e == null) return false;
			int numero = e.Numero;
			ulong zona = e.Zona.Hash;
			int cx = e.Cx + 3, cy = e.Cy;
			e.Cx = cx;
			SalvarNegras();
			CarregarNegras();
			EsferaNegra? volta = _negras.Find(x => x.Numero == numero);
			return volta != null && volta.Zona.Hash == zona && volta.Cx == cx && volta.Cy == cy && !volta.PrecisaAssentar;
		}
		MutacaoNegra("a esfera largada no chao volta na mesma celula depois do reinicio",
			"o boot reassenta todas pelo sal",
			LargadaFica,
			() => ReassentarTudoNoBootDeTeste = true,
			() => ReassentarTudoNoBootDeTeste = false);

		// (2026-10-21, revisao) QUEM SAI DO JOGO CARREGANDO, NUM PLANETA, DEIXA A ESFERA CAIR ALI -- como quem desmaia. O id do portador e
		// de ninguem no `_players`: e o tique depois de um logout.
		bool DeslogadoLarga()
		{
			var terra = ZoneKey.Premade("Earth");
			EsferaNegra e = _negras.First(x => x.Numero == 2);
			e.Portador = 987_654;
			e.PorZona(terra);
			e.Cx = 77;
			e.Cy = 88;
			e.PrecisaAssentar = false;
			TickDasNegras();
			return e.Portador == 0 && e.Zona.Hash == terra.Hash && e.Cx == 77 && e.Cy == 88 && !e.PrecisaAssentar;
		}
		MutacaoNegra("quem desloga num planeta deixa a esfera cair ali, sem espalhar",
			"o logout espalha",
			DeslogadoLarga,
			() => DeslogarEspalhaDeTeste = true,
			() => DeslogarEspalhaDeTeste = false);

		// Planeta morto. (2026-10-21, r4) A esfera da prova fica num PRE-FEITO de proposito: as Black Star se espalham tambem pelos mundos
		// gerados (pedido do dono), e a primeira da lista caia num gerado em parte das rodadas -- a montagem de baixo reprovava pela sorte
		// (a r3 sorteou Gelido-001 e Vulcanico-000). Sem nenhuma num pre-feito, a 5 vai pra um (nem Namek, o eterno, nem a Terra, onde
		// esta o corpo da bancada). A regra medida e a mesma: planeta destruido com Black Star em cima a espalha de novo.
		EsferaNegra noMundo = _negras.FirstOrDefault(e => e.ZonaNome != Esferas.PlanetaEterno && Espaco.EhPlaneta(e.Zona)
														  && ChaveDePlaneta.Da(e.Zona) is { PreFeito: true })
							  ?? NumPreFeito(_negras.First(x => x.Numero == 5));
		EsferaNegra NumPreFeito(EsferaNegra e)
		{
			PlanetaNoEspaco p = Espaco.PreFeitos().First(x => x.Nome != Esferas.PlanetaEterno && x.Nome != "Earth");
			e.Portador = 0;
			e.PorZona(ZoneKey.Premade(p.Nome));
			e.Cx = 40;
			e.Cy = 40;
			e.PrecisaAssentar = false;
			return e;
		}
		ChaveDePlaneta? chave = ChaveDePlaneta.Da(noMundo.Zona);
		ChecaNegra("achei um planeta pre-feito pra destruir na prova", chave is { } c && c.PreFeito, noMundo.ZonaNome);
		if (chave is { } ck)
		{
			ulong era = noMundo.Zona.Hash;
			_mortos.Por(new EstadoDaMorte { Chave = ck.Texto, Nome = noMundo.ZonaNome, Fase = FaseDaMorte.Destruido });
			RecolherNegrasDePlanetasMortos();
			ChecaNegra("planeta destruido com Black Star em cima espalha ela de novo", noMundo.Zona.Hash != era, noMundo.ZonaNome);
			if (_mortos.De(ck) is { } morte) morte.Fase = FaseDaMorte.Vivo;
		}
	}

	/// <summary>
	/// (2026-10-23) UM DESEJO DE VERDADE DAS BLACK STAR em <paramref name="onde"/>, do zero: o planeta vivo (o desejo anterior o condenou),
	/// o set aceso, as sete na mao, o Shenlong pelo verbo de producao (`InvocarODragao`) e o pedido. Devolve o que o servidor disse.
	/// </summary>
	private List<string> DesejoNegroDeVerdade(ServerPlayer pl, ZoneKey onde, string pedido)
	{
		AbortarMorte(onde, "bancada: o planeta volta vivo pro proximo desejo");
		_setNegro.AtivoEm = 0;
		_setNegro.Pedidos = 0;
		_setNegro.Inerte = false;
		_invocacoes.Remove(EsferasNegras.IdDoSet);
		MoveToZone(pl.Id, onde, PontoDeNascimento(onde));
		foreach (EsferaNegra n in _negras) { n.Portador = pl.Id; n.PorZona(onde); n.PrecisaAssentar = false; }
		InvocarODragao(pl);
		return Ouvir(() => PedirDesejo(pl, pedido));
	}

	// =====================================================================
	// (2026-10-23) O PLANETA DO DESEJO SE PARTE -- familia X
	// =====================================================================
	/// <summary>
	/// O dono (2026-10-23): *"black star dragon ball ao ser usada explodir o planeta q esta sendo usada alguns minutos apos o uso, e elas
	/// se espalharem pelo universo"*; decidido "5 min, pelos planetas de hoje". Roda DENTRO do palco das mortes, logo depois do desejo de
	/// verdade feito na Terra (<paramref name="ouviuNoDesejo"/> e o que se ouviu nele):
	///   X1-X4. a Terra entra nos 310 s da porta unica, com os dois avisos, sem autor, e nenhuma das sete cai num planeta condenado;
	///   X5.    o sorteio nao conhece planeta condenado -- pedindo o universo inteiro dele (no desejo de verdade e sorte: sete de ~12);
	///   X6-X8. o desejo em Arlia e a consumacao pelo tique de producao (o laco da `--porungateste`): o planeta explode e a Black Star que
	///          alguem largou la depois do desejo se espalha de novo, num mundo vivo;
	///   X9.    o contra-exemplo da contagem: com o defeito ligado, a mesma regua do X1/X2 (num desejo de verdade) reprova.
	/// </summary>
	private void FamiliaDaExplosaoNegra(ServerPlayer pl, ZoneKey terra, List<string> ouviuNoDesejo)
	{
		bool PartiuEAvisou(ZoneKey onde, List<string> falas) =>
			MorteDaZona(onde) is { Fase: FaseDaMorte.Explodindo } m
			&& Math.Abs(m.Faltam - MortePlanetaria.SegundosDeExplosao) < 1e-6
			&& _proximoTremor.ContainsKey(m.Chave)
			&& Disse(falas, "COMEÇA A SE PARTIR") && Disse(falas, "se despedaçando");

		// ---- X1-X4: o desejo de cima, na Terra (o berco: "a Terra tambem", decisao do dono)
		EstadoDaMorte? daTerra = MorteDaZona(terra);
		ChecaNegra("X1. O DESEJO PARTE O PLANETA ONDE AS BLACK STAR FORAM USADAS: a Terra entra nos 310 s da porta unica (fase, prazo e "
				   + "o relogio do tremor)",
			daTerra is { Fase: FaseDaMorte.Explodindo } x1 && Math.Abs(x1.Faltam - MortePlanetaria.SegundosDeExplosao) < 1e-6
			&& _proximoTremor.ContainsKey(x1.Chave),
			daTerra == null ? "a Terra continua viva" : $"{daTerra.Fase}, faltam {daTerra.Faltam:0} s");
		ChecaNegra("X2. ...e o aviso sai: quem esta nela ouve que o planeta comeca a se partir, e o mundo, que ele se despedaca",
			Disse(ouviuNoDesejo, "COMEÇA A SE PARTIR") && Disse(ouviuNoDesejo, "se despedaçando"), Ultimos(ouviuNoDesejo));
		ChecaNegra("X3. ...sem autor (e o preco das esferas, nao um golpe de quem pediu), com o motivo no registro",
			daTerra is { IdDoAlgoz: 0 } x3 && x3.Motivo.Contains("Black Star", StringComparison.Ordinal),
			daTerra == null ? "sem registro" : $"algoz {daTerra.IdDoAlgoz}, motivo '{daTerra.Motivo}'");
		ChecaNegra("X4. ...e nenhuma das sete caiu num planeta condenado (viraram pedra e foram pros outros)",
			_negras.All(e => !ZonaCondenada(e.Zona)),
			string.Join(", ", _negras.Where(e => ZonaCondenada(e.Zona)).Select(e => $"#{e.Numero} em {e.ZonaNome}")));
		AbortarMorte(terra, "bancada: a Terra volta viva pro resto da familia");

		// ---- X5: O SORTEIO NAO CONHECE PLANETA CONDENADO. A regua pede o universo INTEIRO do sorteio (os pre-feitos + os gerados em
		// volta da Terra) com Arlia nos 310 s: com o filtro ela nunca vem, e sem ele vem sempre.
		ZoneKey arlia = Cobaia;
		PlanetaNoEspaco casa = Espaco.PreFeitos().First();
		int universo = Espaco.PreFeitos().Count()
			+ Espaco.PorPerto(SeedDoUniverso, casa.Chunk, EsferasNegras.RaioDeBuscaEmChunks).Count;
		bool SorteioPulaArlia() => !SortearPlanetas(_setNegro.LerSal(), universo).Any(z => z.Hash == arlia.Hash);
		bool condenou = ComecarDestruicao(arlia, 1, "bancada: o sorteio das Black Star pula o planeta condenado");
		ChecaNegra("X5. (montagem) Arlia nos 310 s pra medir o sorteio -- e ela e um planeta que o sorteio conhece",
			condenou && MorteDaZona(arlia) is { Fase: FaseDaMorte.Explodindo } && Espaco.EhPlaneta(arlia));
		ChecaNegra($"X5a. O SORTEIO DAS BLACK STAR NAO CONHECE PLANETA CONDENADO: pedindo o universo inteiro dele ({universo} mundos), "
				   + "Arlia nunca vem", SorteioPulaArlia());
		bool pulaComDefeito;
		NegraCaiNoPlanetaCondenadoDeTeste = true;
		try { pulaComDefeito = SorteioPulaArlia(); }
		finally { NegraCaiNoPlanetaCondenadoDeTeste = false; }
		ChecaNegra("X5b. (defeito injetado: o sorteio so pula planeta MORTO, como antes) a MESMA regua do X5a REPROVA", !pulaComDefeito);
		AbortarMorte(arlia, "bancada: Arlia volta viva pro desejo dela");

		// ---- X6-X8: O DESEJO EM ARLIA E A CONSUMACAO, PELO TIQUE DE PRODUCAO
		List<string> ouviuEmArlia = DesejoNegroDeVerdade(pl, arlia, "dinheiro");
		ChecaNegra("X6. o desejo em Arlia tambem a parte (a regra e de qualquer planeta, nao so do berco)", PartiuEAvisou(arlia, ouviuEmArlia),
			MorteDaZona(arlia) is { } ma ? $"{ma.Fase}, faltam {ma.Faltam:0} s" : "Arlia continua viva");
		// A BLACK STAR QUE ALGUEM TROUXE DE VOLTA nos 310 s: no chao de Arlia, aos pes de quem pediu.
		EsferaNegra largada = _negras.First(x => x.Numero == 1);
		largada.Portador = 0;
		largada.PorZona(arlia);
		largada.Cx = (int)MathF.Floor(pl.Pos.X / ZoneCollision.TileSize);
		largada.Cy = (int)MathF.Floor(pl.Pos.Y / ZoneCollision.TileSize);
		largada.PrecisaAssentar = false;
		// QUEM PEDIU SAI ANTES DO FIM: o commit feriria o corpo da bancada (dano letal, pelo tamanho do mundo) e o mandaria pro espaco.
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		int voltas = 0;
		List<string> fim = Ouvir(() => { while (!ZonaMorta(arlia) && voltas < 4000) { TickDaDestruicao(1); voltas++; } });
		ChecaNegra("X7. NO FIM DA CONTAGEM O PLANETA DO DESEJO EXPLODE (o tique de producao), e o mundo ouve que ele nao existe mais",
			ZonaMorta(arlia) && Disse(fim, "NÃO EXISTE MAIS"), $"{voltas} voltas; {Ultimos(fim)}");
		ChecaNegra("X8. ...e a Black Star largada la depois do desejo se espalha de novo, num mundo vivo",
			largada.Portador == 0 && largada.Zona.Hash != arlia.Hash && Espaco.EhPlaneta(largada.Zona) && !ZonaCondenada(largada.Zona),
			largada.ZonaNome);

		// ---- X9: O CONTRA-EXEMPLO DA CONTAGEM -- a mesma regua do X1/X2, num desejo de verdade na Terra, com o defeito ligado
		bool partiuComDefeito;
		NegraNaoExplodeOPlanetaDeTeste = true;
		try { partiuComDefeito = PartiuEAvisou(terra, DesejoNegroDeVerdade(pl, terra, "dinheiro")); }
		finally { NegraNaoExplodeOPlanetaDeTeste = false; }
		ChecaNegra("X9a. (defeito injetado: o desejo nao parte o planeta) a MESMA regua do X1/X2 REPROVA", !partiuComDefeito);
		ChecaNegra("X9b. ...e desfeito o defeito ela volta a passar", PartiuEAvisou(terra, DesejoNegroDeVerdade(pl, terra, "dinheiro")));
	}

	// =====================================================================
	// (2026-10-23) A DANCA DA FUSAO, SO NAS BLACK STAR -- familia D
	// =====================================================================
	/// <summary>
	/// O dono (2026-10-23): *"faca ter a opcao de aprender a danca da fusao como um pedido da black star dragon ball"*. Roda DENTRO do palco
	/// das mortes (o desejo de verdade parte a Terra):
	///   D1. a lista das Black Star tem "Aprender a Danca da Fusao", e a linha vem acesa pra quem nao a sabe;
	///   D2. e so ela: o Porunga, o menu da estatua e o Super Shenlong nao a oferecem (contra-exemplo: a danca em todo dragao);
	///   D3. o desejo de verdade ensina a Danca, e ela FICA depois do tique dos cargos -- e skill do kit do Geti Star, e o tique tira a copia
	///       de cargo que ninguem ensinou (contra-exemplo: sem a marca de ensinada);
	///   D4. quem ja sabe nao gasta o pedido: a linha vem apagada com o motivo, e o pedido por verbo recusa com o dragao de pe
	///       (contra-exemplo: com o defeito a linha acende e o pedido e gasto).
	/// </summary>
	private void FamiliaDaDancaNegra(ServerPlayer pl, ZoneKey terra, SetDeEsferas porunga)
	{
		DesejoDef danca = Desejos.DaBlackStar.First(d => d.Id == Desejos.IdDaDancaDaFusao);
		void SemADanca() { if (SabeDancar(pl)) pl.Livro!.Esquecer(PathDaDanca); }

		// ---- D1
		SemADanca();
		ChecaNegra("D1. A LISTA DAS BLACK STAR TEM \"Aprender a Dança da Fusão\" -- e a linha da janela vem acesa pra quem nao sabe a danca",
			MenuDaqui(pl, _setNegro).Any(d => d.Id == danca.Id)
			&& EntradaDoDesejo(pl, _setNegro, danca, posso: true, juntas: false) is { Pode: true },
			string.Join(", ", MenuDaqui(pl, _setNegro).Select(d => d.Id)));

		// ---- D2
		bool SoNelas() =>
			!MenuDaqui(pl, porunga).Any(d => d.Id == danca.Id)
			&& !ConfigDaEstatua.Ajustaveis.Any(d => d.Id == danca.Id)
			&& !Desejos.MenuDoSuper(ehKaioshin: true, haProcuracao: false, sangueSaiyajin: 100).Any(d => d.Id == danca.Id);
		ChecaNegra("D2. ...e SO nelas: nem o Porunga (o dragao comum), nem o menu da estatua, nem o Super Shenlong a oferecem", SoNelas());
		bool soComDefeito;
		DancaEmTodoDragaoDeTeste = true;
		try { soComDefeito = SoNelas(); }
		finally { DancaEmTodoDragaoDeTeste = false; }
		ChecaNegra("D2b. (defeito injetado: a danca entra em todo dragao) a MESMA regua do D2 REPROVA", !soComDefeito);

		// ---- D3: o pedido de verdade, e o tique dos cargos (o corpo da bancada nao tem cargo) passando por cima
		bool AprendeEFica(out string detalhe)
		{
			SemADanca();
			List<string> falas = DesejoNegroDeVerdade(pl, terra, danca.Id);
			bool gastou = _setNegro.Pedidos == EsferasNegras.Desejos && !_invocacoes.ContainsKey(EsferasNegras.IdDoSet);
			bool aprendeu = SabeDancar(pl);
			ReconciliarDadiva(pl);
			bool ficou = SabeDancar(pl);
			detalhe = $"gastou {gastou}, aprendeu {aprendeu}, ficou depois do tique {ficou}; {Ultimos(falas)}";
			return gastou && aprendeu && ficou && Disse(falas, "você aprende a Dança da Fusão");
		}
		ChecaNegra("D3. O DESEJO ENSINA A DANCA DA FUSAO, e ela FICA depois do tique dos cargos (a Danca e do kit do Geti Star)",
			AprendeEFica(out string d3), d3);
		bool ficaComDefeito;
		DancaSemSerEnsinadaDeTeste = true;
		try { ficaComDefeito = AprendeEFica(out _); }
		finally { DancaSemSerEnsinadaDeTeste = false; }
		ChecaNegra("D3b. (defeito injetado: o desejo poe a danca no livro sem a marca de ensinada) a MESMA regua do D3 REPROVA -- o tique "
				   + "dos cargos a tira", !ficaComDefeito);

		// ---- D4: quem ja sabe. A montagem poe a danca no livro ela mesma (como um mestre poria), e nao pelo D3: cada medida parte do zero.
		(bool Janela, bool Verbo, string Detalhe) JaSabe()
		{
			if (!SabeDancar(pl)) (pl.Livro ??= new Jandirus.Core.Skills.SkillBook()).DarComoEnsinada(PathDaDanca);
			AbortarMorte(terra, "bancada");
			_setNegro.AtivoEm = 0;
			_setNegro.Pedidos = 0;
			_setNegro.Inerte = false;
			_invocacoes.Remove(EsferasNegras.IdDoSet);
			MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
			foreach (EsferaNegra n in _negras) { n.Portador = pl.Id; n.PorZona(terra); n.PrecisaAssentar = false; }
			InvocarODragao(pl);
			EntradaDoDragao linha = EntradaDoDesejo(pl, _setNegro, danca, posso: true, juntas: false);
			List<string> falas = Ouvir(() => PedirDesejo(pl, danca.Id));
			bool janela = !linha.Pode && linha.Motivo.Contains("já sabe a Dança da Fusão", StringComparison.Ordinal);
			bool verbo = _setNegro.Pedidos == 0 && _invocacoes.ContainsKey(EsferasNegras.IdDoSet)
						 && Disse(falas, "você já sabe a Dança da Fusão") && Disse(falas, "Nenhum pedido foi gasto");
			return (janela, verbo, $"linha Pode={linha.Pode} '{linha.Motivo}'; pedidos {_setNegro.Pedidos}; {Ultimos(falas)}");
		}
		(bool janela, bool verbo, string d4) = JaSabe();
		ChecaNegra("D4a. QUEM JA SABE A DANCA ve a linha APAGADA, com o motivo escrito", janela, d4);
		ChecaNegra("D4b. ...e o pedido por verbo recusa SEM GASTAR: o Shenlong continua de pe e o pedido unico continua la", verbo, d4);
		(bool janelaComDefeito, bool verboComDefeito, string d4c) = (false, false, "");
		DancaRepetidaDeTeste = true;
		try { (janelaComDefeito, verboComDefeito, d4c) = JaSabe(); }
		finally { DancaRepetidaDeTeste = false; }
		ChecaNegra("D4c. (defeito injetado: quem ja sabe pede de novo) as MESMAS reguas do D4a e do D4b REPROVAM",
			!janelaComDefeito && !verboComDefeito, d4c);
		_invocacoes.Remove(EsferasNegras.IdDoSet);
	}

	private List<Vec2> OndeEsta(SetDeEsferas s) =>
		[.. _esferas.Where(e => e.Set == s.Id).OrderBy(e => e.Numero).Select(e => new Vec2(e.X, e.Y))];

	private void MutacaoNegra(string oQue, string oDefeito, Func<bool> criterio, Action estragar, Action consertar)
	{
		ChecaNegra(oQue, criterio(), "o criterio ja reprova ANTES do defeito");
		bool caiu = false;
		try
		{
			estragar();
			caiu = !criterio();
		}
		finally { consertar(); }
		ChecaNegra($"   DEFEITO INJETADO ({oDefeito}): o MESMO criterio REPROVA", caiu);
		ChecaNegra("   ...e desfeito o defeito ele volta a passar", criterio());
	}
}
