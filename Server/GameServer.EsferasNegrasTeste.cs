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
		PedirDesejo(pl, "dinheiro");
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
