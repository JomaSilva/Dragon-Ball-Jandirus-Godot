using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DAS MELHORIAS (`--melhoriasteste`) -- os tres pedidos do dono de 2026-10-19:
///
///   1. *"ao pegar um item q vc construiu e deu upgrade, tipo a maquina de gravidade etc (a drill isso n acontece) ao vc
///      re-instala-la em outro local, todos os upgrades sao removidos"* -- a maquina recolhida leva o estado numa gaveta do
///      cofre, e o item dela leva o numero (`MaquinaGuardada`, `GameServer.MaquinaGuardada.cs`);
///   2. *"coloque limites do quanto vc pode dar de upgrade nas maquinas com base na tech level da pessoa"* -- o teto de cada
///      melhoria pela tecnologia de quem paga (`TetoPorTecnologia`), com 100 = o jogo de antes;
///   3. *"o certo seria capar o upgrade em 500x ou qualquer valor q o server tenha como teto"* -- a forca do campo para no
///      teto do mundo (`TetoDaForca`).
///
/// No boot, como a `--maquinasteste`: corpos forjados, e TUDO pelo funil de producao -- a maquina e assentada pelo
/// `posicionar`, melhorada pelos verbos da tecla E e recolhida pelo `pegar` (a nave, pelo `nave_pegar`). O palco e a praca da
/// Terra, e o que a bancada mexe volta no `finally`: as obras, as naves, as gavetas do cofre e os corpos.
///
/// TRES DEFEITOS INJETADOS, um por regra: a maquina recolhida que perde o estado (`MaquinaRecolhidaPerdeAsMelhoriasDeTeste`),
/// a melhoria sem teto de tecnologia (`TetoPorTecnologia.SemTetoDeTeste`, medido em cada uma das dezesseis melhorias) e a
/// forca que passa do teto do mundo (`ForcaPassaDoTetoDoMundoDeTeste`).
///
/// A familia 0 IMPRIME A TABELA dos tetos (tecnologia 0, 25, 50, 75 e 100) a partir das proprias contas do Core: a tabela que
/// vai pro dono sai daqui, e nao de uma conta de papel.
///
///     Godot --headless -- --server --port 8620 --melhoriasteste
/// </summary>
public sealed partial class GameServer
{
	private int _meOk, _meFalhou;

	private void AfirmarMe(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _meOk++; GD.Print($"[melhorias]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _meFalhou++; GD.PrintErr($"[melhorias]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDasMelhorias()
	{
		_meOk = _meFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[melhorias] ================ A MAQUINA RECOLHIDA, O TETO DA TECNOLOGIA E O DO MUNDO (dono, 2026-10-19) ================");

		ZoneKey terra = ZonaDaBancadaDeProjetil;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
		int obrasAntes = _noChao.Count, navesAntes = _naves.Count, gavetaAntes = _cofreDeTech.Proximo;
		try
		{
			AfirmarMe("(montagem) a Terra tem mapa, e o catalogo de obras carregou", mapa != null && _obras != null);
			if (mapa == null || _obras == null) return;
			(int px, int py) = PracaDaBancada(mapa);

			MeDaTabela();
			MeDaMaquinaRecolhida(px, py, obrasAntes, navesAntes);
			LimparPalcoMe(obrasAntes, navesAntes);
			MeDoTetoDaTecnologia(px, py, obrasAntes, navesAntes);
			LimparPalcoMe(obrasAntes, navesAntes);
			MeDoTetoDoMundo(px, py);
		}
		finally
		{
			MaquinaRecolhidaPerdeAsMelhoriasDeTeste = ForcaPassaDoTetoDoMundoDeTeste = false;
			TetoPorTecnologia.SemTetoDeTeste = false;

			// O MUNDO VOLTA: as obras, as naves e as gavetas de antes da bancada -- e nada dela no disco.
			LimparPalcoMe(obrasAntes, navesAntes);
			foreach (int g in _cofreDeTech.Maquinas.Keys.Where(k => k >= gavetaAntes).ToList()) _cofreDeTech.Maquinas.Remove(g);
			GravarMundo();
			GravarNaves();
			GravarOCofreDeTech();
			AplicarColisaoDasObras(terra);
			LerOsCampos();
			GD.Print($"[melhorias] ================ {_meOk} OK, {_meFalhou} FALHA(S) ================");
		}
	}

	/// <summary>
	/// Tira do mundo o que a bancada assentou depois do instante dado, e os corpos forjados. A COLISAO E REFEITA: a obra
	/// tirada da lista deixaria a parede dela no mapa (`AplicarColisaoDasObras` le a lista inteira), e a proxima maquina
	/// da bancada, na mesma celula, ouviria "dentro de uma parede".
	/// </summary>
	private void LimparPalcoMe(int obrasAntes, int navesAntes)
	{
		if (_noChao.Count > obrasAntes) _noChao.RemoveRange(obrasAntes, _noChao.Count - obrasAntes);
		if (_naves.Count > navesAntes) _naves.RemoveRange(navesAntes, _naves.Count - navesAntes);
		AplicarColisaoDasObras(ZonaDaBancadaDeProjetil);
		LimparTudoDaBancada();
	}

	/// <summary>O meio de uma celula, como posicao de CORPO -- a conta da `--maquinasteste` (o `posicionar` mede pelos pes).</summary>
	private static Vec2 NoMe(int cx, int cy) =>
		new(cx * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f, cy * ZoneCollision.TileSize + ZoneCollision.TileSize / 2f - MoveRules.FeetOffsetY);

	/// <summary>
	/// ASSENTA O ITEM -- o id inteiro, com a gaveta -- pelo funil de producao (`posicionar`), com quem o tem parado logo ao sul.
	/// Devolve a obra nova, ou nulo se o servidor recusou. (O `AssentarDeTeste` da vizinha guarda o id cru antes: aqui o item ja
	/// esta na mochila, e e ELE que se assenta.)
	/// </summary>
	private Obra? AssentarItemMe(ServerPlayer dono, string idDoItem, int cx, int cy)
	{
		const int T = ZoneCollision.TileSize;
		dono.Pos = NoMe(cx, cy + 1);
		int antes = _noChao.Count;
		ComandoDeTech(dono, "posicionar", $"{idDoItem}/{cx * T + T / 2}/{cy * T + T / 2 - (int)MoveRules.FeetOffsetY}");
		return _noChao.Count > antes ? _noChao[^1] : null;
	}

	/// <summary>A mesma porta, pra nave (ela mora noutra lista): devolve a nave nova, ou nula.</summary>
	private Nave? AssentarNaveMe(ServerPlayer dono, string idDoItem, int cx, int cy)
	{
		const int T = ZoneCollision.TileSize;
		dono.Pos = NoMe(cx, cy + 1);
		int antes = _naves.Count;
		ComandoDeTech(dono, "posicionar", $"{idDoItem}/{cx * T + T / 2}/{cy * T + T / 2 - (int)MoveRules.FeetOffsetY}");
		return _naves.Count > antes ? _naves[^1] : null;
	}

	/// <summary>O item deste tipo na mochila (o id inteiro: cru ou com gaveta), ou nulo.</summary>
	private static string? ItemDoTipoMe(ServerPlayer pl, string tipo) =>
		pl.Mochila.Pilhas.Select(p => p.Id).FirstOrDefault(id => string.Equals(MaquinaGuardada.Tipo(id), tipo, StringComparison.OrdinalIgnoreCase));

	private ServerPlayer ForjarMe(string nome, string conta, Vec2 onde, double tech)
	{
		ServerPlayer p = Forjar(nome, onde, 1_000);
		p.Conta = conta;
		p.Ficha.Zeni = 1e12;
		p.Ficha.techskill = tech;
		return p;
	}

	// =====================================================================
	// 0) A TABELA
	// =====================================================================
	private void MeDaTabela()
	{
		GD.Print("[melhorias] -- 0) a tabela: o teto de cada melhoria pela tecnologia de quem paga (`TetoPorTecnologia`) --");
		double mundo = Interacoes.TetoDeGravidade;
		static string Sem(int n, Func<int, string> escrito) => n == TetoPorTecnologia.SemTeto ? "sem teto" : escrito(n);
		double[] niveis = [0, 25, 50, 75, 100];
		(string Nome, Func<double, string> Teto)[] linhas =
		[
			("gravidade: forca do campo", t => $"{TetoPorTecnologia.ForcaDaGravidade(t, mundo):0}x"),
			("gravidade: bateria (cargas)", t => Sem(TetoPorTecnologia.DobrasDaBateriaDaGravidade(t), d => $"{Math.Pow(2, d):N0}")),
			("gravidade: alcance", t => $"{TetoPorTecnologia.AlcanceDaGravidade(t, 10)}"),
			("gravidade: nanites", t => Sem(TetoPorTecnologia.NanitesDaGravidade(t), n => $"{n}")),
			("regenerador: bateria (cargas)", t => Sem(TetoPorTecnologia.BateriaDoRegenerador(t), n => $"{n}")),
			("regenerador: velocidade de cura", t => Sem(TetoPorTecnologia.VelocidadeDoRegenerador(t), n => $"{n}")),
			("regenerador: nanites", t => Sem(TetoPorTecnologia.NanitesDoRegenerador(t), n => $"{n}")),
			("drill: nivel", t => $"{TetoPorTecnologia.NivelDaDrill(t)}"),
			("estacao de recarga: eficiencia", t => $"{TetoPorTecnologia.EficienciaDaEstacao(t):0.#}"),
			("estacao de recarga: nivel", t => $"{TetoPorTecnologia.NivelDaEstacao(t)}"),
			("simulador: teto de poder (dobras)", t => Sem(TetoPorTecnologia.DobrasDoSimulador(t), n => $"{n}")),
			("simulador: energia", t => Sem(TetoPorTecnologia.EnergiaDoSimulador(t), n => $"{n}")),
			("clonagem: sessao mais curta", t => Sem(TetoPorTecnologia.VelocidadeDaClonagem(t), n => $"{n}")),
			("clonagem: menos falha", t => Sem(TetoPorTecnologia.FalhaDaClonagem(t), n => $"{n}")),
			("clonagem: bateria", t => Sem(TetoPorTecnologia.BateriaDaClonagem(t), n => $"{n}")),
			("naves: velocidade", t => $"{TetoPorTecnologia.VelocidadeDaNave(t)}x"),
		];
		GD.Print($"[melhorias]    {"melhoria",-36}" + string.Concat(niveis.Select(t => $"{$"tech {t:0}",11}")));
		foreach ((string nome, Func<double, string> teto) in linhas)
			GD.Print($"[melhorias]    {nome,-36}" + string.Concat(niveis.Select(t => $"{teto(t),11}")));

		AfirmarMe("a FORCA DO CAMPO: o teto do mundo vezes tecnologia/100 -- 125x aos 25, 250x aos 50 --, nunca abaixo dos 10x de fábrica, e o do mundo inteiro aos 100 (e além)",
				  TetoPorTecnologia.ForcaDaGravidade(0, mundo) == 10 && TetoPorTecnologia.ForcaDaGravidade(25, mundo) == 125
				  && TetoPorTecnologia.ForcaDaGravidade(50, mundo) == 250 && TetoPorTecnologia.ForcaDaGravidade(100, mundo) == mundo
				  && TetoPorTecnologia.ForcaDaGravidade(250, mundo) == mundo);
		AfirmarMe("o que o JOGO JA LIMITA sobe na proporção, arredondado pra cima: a Drill vai ao nível 3 aos 30 (a tecnologia que a fabrica) e ao 10 aos 100, a nave a 16x aos 40 e 39x aos 100, o alcance a 3 aos 25 e a 1 com um ponto",
				  TetoPorTecnologia.NivelDaDrill(30) == 3 && TetoPorTecnologia.NivelDaDrill(50) == 5 && TetoPorTecnologia.NivelDaDrill(100) == Drill.NivelMaximo
				  && TetoPorTecnologia.VelocidadeDaNave(40) == 16 && TetoPorTecnologia.VelocidadeDaNave(100) == Naves.VelocidadeMaxima
				  && TetoPorTecnologia.AlcanceDaGravidade(25, 10) == 3 && TetoPorTecnologia.AlcanceDaGravidade(1, 10) == 1
				  && TetoPorTecnologia.AlcanceDaGravidade(50, 10) == 5 && TetoPorTecnologia.EficienciaDaEstacao(25) == 1.0
				  && TetoPorTecnologia.EficienciaDaEstacao(100) == EstacaoDeRecarga.EficienciaMaxima && TetoPorTecnologia.NivelDaEstacao(100) == EstacaoDeRecarga.NivelMaximo);
		AfirmarMe("o que o jogo NAO limita: uma compra a cada 5 de tecnologia, desde a fábrica -- 5 aos 25, 10 aos 50, 19 aos 99 --, e sem teto aos 100",
				  TetoPorTecnologia.Compras(25) == 5 && TetoPorTecnologia.Compras(50) == 10 && TetoPorTecnologia.Compras(99) == 19
				  && TetoPorTecnologia.Compras(100) == TetoPorTecnologia.SemTeto && TetoPorTecnologia.BateriaDoRegenerador(25) == 6
				  && TetoPorTecnologia.BateriaDaClonagem(25) == 7 && TetoPorTecnologia.BateriaDoRegenerador(100) == TetoPorTecnologia.SemTeto);
		AfirmarMe("a tecnologia torta (NaN, negativa) vale zero -- e não tira o teto de ninguém",
				  !TetoPorTecnologia.Livre(double.NaN) && TetoPorTecnologia.Compras(double.NaN) == 0 && TetoPorTecnologia.Compras(-5) == 0
				  && TetoPorTecnologia.ForcaDaGravidade(double.NaN, mundo) == 10);
		TetoPorTecnologia.SemTetoDeTeste = true;
		try
		{
			AfirmarMe("(defeito injetado: sem teto de tecnologia) a mesma régua REPROVA -- a força aos 25 vai ao teto do mundo, e a compra não tem fim",
					  TetoPorTecnologia.ForcaDaGravidade(25, mundo) == mundo && TetoPorTecnologia.Compras(25) == TetoPorTecnologia.SemTeto);
		}
		finally { TetoPorTecnologia.SemTetoDeTeste = false; }
	}

	// =====================================================================
	// 1) A MAQUINA RECOLHIDA LEVA AS MELHORIAS
	// =====================================================================
	private static string EstadoMe(GravidadeDaObra? g) =>
		g == null ? "sem estado" : $"{g.Max:0}x alcance {g.Range} bateria {g.Energia:0.##}/{g.EnergiaMax:0} estavel {g.Estavel} nanites {g.Nanites}";

	private void MeDaMaquinaRecolhida(int px, int py, int obrasAntes, int navesAntes)
	{
		GD.Print("[melhorias] -- 1) a maquina recolhida leva as melhorias, no item (`MaquinaGuardada`) --");
		ServerPlayer dono = ForjarMe("me: a dona das maquinas", "bancada_melhorias", NoMe(px + 1, py + 2), 100);

		// ------------------------------------------------------------ A MAQUINA DE GRAVIDADE
		dono.Pos = NoMe(px + 1, py + 2);
		Obra? g1 = AssentarDeTeste(dono, "Gravity", px + 1, py + 1);
		AfirmarMe("(montagem) a máquina de gravidade assentou pelo funil (`posicionar`)", g1 != null);
		if (g1 == null) return;
		foreach (string m in new[] { "forca", "forca", "forca", "alcance", "alcance", "bateria", "estabilidade", "nanites" })
			ComandoDeInteracao(dono, "grav_up", m);
		GravidadeDaObra? melhorada = g1.Gravidade;
		melhorada!.Energia = 1.25;   // a carga gasta pela metade: tambem e estado, e tambem tem de voltar
		bool ComoEstava(GravidadeDaObra? g) =>
			g is { Max: 17, Range: 2, Estavel: true, EnergiaMax: 2, Energia: 1.25, Nanites: 1 };
		AfirmarMe("(montagem) ela foi melhorada pelos verbos da tecla E: 17x, alcance 2, a bateria dobrada (e meio gasta), estabilizada, uma nanite",
				  ComoEstava(melhorada), EstadoMe(melhorada));
		// O REFORCO (o `Wall_Upgrader`, a blindagem do super computador) tambem e melhoria: a armadura desta e maior que a que ela
		// nasceria nas maos de quem a assenta.
		g1.ArmaduraMax = g1.Armadura = 1e9;

		List<string> aoPegar = Ouvir(() => ComandoDeInteracao(dono, "pegar", ""));
		string? item = ItemDoTipoMe(dono, "Gravity");
		int gaveta = item == null ? 0 : MaquinaGuardada.Gaveta(item);
		AfirmarMe("**RECOLHIDA, ELA VAI PRA MOCHILA COM ESTADO**: o item leva uma gaveta do cofre (`Gravity|c<n>|17x`), a gaveta guarda a máquina inteira, e a dona ouve que as melhorias vão junto",
				  !_noChao.Contains(g1) && item != null && gaveta > 0 && dono.Mochila.Quantos("Gravity") == 0
				  && _cofreDeTech.Maquinas.TryGetValue(gaveta, out MaquinaNaGaveta? naGaveta) && naGaveta.Tipo == "Gravity" && ComoEstava(naGaveta.Gravidade)
				  && Disse(aoPegar, "vão junto"),
				  $"item {item}; {Ultimos(aoPegar)}");
		AfirmarMe("...e a mochila a mostra com o rótulo -- 'Gravity (17x)' --, como uma máquina que se INSTALA (e não se planta)",
				  item != null && CatalogoDeItens.Get(item) is { Empilhavel: false } def && def.Nome.EndsWith("(17x)", StringComparison.Ordinal)
				  && CatalogoDeItens.PodeAssentarNoChao(item) && CatalogoDeItens.OQueAssenta(item) == "Gravity" && !CatalogoDeItens.EhPlantio(item)
				  && CatalogoDeItens.EhPlantio(CatalogoDeItens.SementeDeMaca) && !CatalogoDeItens.EhPlantio("Gravity"),
				  item == null ? "sem item" : $"{CatalogoDeItens.Get(item)?.Nome}");

		Obra? g2 = null;
		List<string> aoAssentar = item == null ? [] : Ouvir(() => g2 = AssentarItemMe(dono, item, px + 5, py + 1));
		AfirmarMe("**ASSENTADA DE NOVO, EM OUTRO LUGAR, ELA VOLTA COMO ESTAVA**: 17x, alcance 2, a bateria dobrada com a carga que tinha, estabilizada, a nanite -- nada a pagar de novo",
				  g2 != null && g2 != g1 && ComoEstava(g2.Gravidade) && Disse(aoAssentar, "com as melhorias") && !Disse(aoAssentar, "planta"),
				  $"{EstadoMe(g2?.Gravidade)}; {Ultimos(aoAssentar)}");
		AfirmarMe("...a gaveta se esvazia (o mesmo item não devolve duas vezes) e a mochila fica sem a máquina",
				  gaveta > 0 && !_cofreDeTech.Maquinas.ContainsKey(gaveta) && ItemDoTipoMe(dono, "Gravity") == null);
		AfirmarMe("...e a armadura reforçada que ela tinha não desce pra que ela nasceria nas mãos de quem a assenta",
				  g2 is { ArmaduraMax: 1e9, Armadura: 1e9 }, $"armadura {g2?.Armadura:N0} de {g2?.ArmaduraMax:N0}");

		// O CONTRA-EXEMPLO: a mesma volta, com a gaveta desligada -- o jogo de antes.
		if (g2 != null)
		{
			MaquinaRecolhidaPerdeAsMelhoriasDeTeste = true;
			try
			{
				dono.Pos = NoMe(px + 5, py + 2);
				ComandoDeInteracao(dono, "pegar", "");
				string? cru = ItemDoTipoMe(dono, "Gravity");
				Obra? g3 = cru == null ? null : AssentarItemMe(dono, cru, px + 1, py + 1);
				AfirmarMe("(defeito injetado: a máquina recolhida perde o estado) a mesma régua REPROVA -- volta pra mochila o id cru, e ela é assentada de fábrica",
						  cru == "Gravity" && g3 != null && !ComoEstava(g3.Gravidade), $"item {cru}; {EstadoMe(g3?.Gravidade)}");
			}
			finally { MaquinaRecolhidaPerdeAsMelhoriasDeTeste = false; }
		}
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: a dona das maquinas", "bancada_melhorias", NoMe(px + 1, py + 2), 100);

		// ------------------------------------------------------------ A DE FABRICA VOLTA COM O ID CRU
		dono.Pos = NoMe(px + 1, py + 2);
		int proximaAntes = _cofreDeTech.Proximo;
		if (AssentarDeTeste(dono, "Gravity", px + 1, py + 1) is { } nova)
		{
			ComandoDeInteracao(dono, "grav_info", "");   // o estado nasce (de fabrica) no primeiro toque
			ComandoDeInteracao(dono, "pegar", "");
			AfirmarMe("a máquina DE FÁBRICA (mexida, mas sem melhoria nenhuma) volta com o id cru, e nenhuma gaveta é aberta",
					  !_noChao.Contains(nova) && dono.Mochila.Quantos("Gravity") == 1 && _cofreDeTech.Proximo == proximaAntes,
					  $"mochila [{string.Join(", ", dono.Mochila.Pilhas.Select(p => p.Id))}]");
			dono.Mochila.Tirar("Gravity");
		}
		else AfirmarMe("(montagem) a segunda máquina de gravidade assentou", false);

		// ------------------------------------------------------------ O REGENERADOR, E O ITEM QUE MUDA DE MAO
		dono.Pos = NoMe(px + 1, py + 2);
		Obra? t1 = AssentarDeTeste(dono, Regenerador.Tipo, px + 1, py + 1);
		ServerPlayer amigo = ForjarMe("me: o amigo sem tecnologia", "bancada_melhorias_amigo", NoMe(px + 5, py + 2), 0);
		if (t1 != null)
		{
			ComandoDeInteracao(dono, "aparafusar", "");
			foreach (string m in new[] { "velocidade", "velocidade", "bateria", "ferimentos", "nanites" })
				ComandoDeInteracao(dono, "regen_up", m);
			ComandoDeInteracao(dono, "regen_ligar", "");
			bool ligado = t1.Regenerador is { Ligado: true };
			ComandoDeInteracao(dono, "pegar", "");
			string? doTanque = ItemDoTipoMe(dono, Regenerador.Tipo);
			// O ITEM MUDA DE MAO: sai da mochila da dona e entra na do amigo -- o caminho de uma troca, de um bau, do chao.
			if (doTanque != null) { dono.Mochila.Tirar(doTanque); amigo.Mochila.Guardar(doTanque); }
			Obra? t2 = doTanque == null ? null : AssentarItemMe(amigo, doTanque, px + 5, py + 1);
			AfirmarMe("**O REGENERADOR TAMBÉM**: recolhido ligado e melhorado, o item leva o estado -- e quem o assenta é o AMIGO (sem tecnologia nenhuma), que ganha o tanque como estava: velocidade 3, duas cargas, ferimentos, a nanite, desligado e solto",
					  ligado && doTanque != null && MaquinaGuardada.Gaveta(doTanque) > 0
					  && t2 is { DonoConta: "bancada_melhorias_amigo", Aparafusada: false, Regenerador: { Eficiencia: 3, EnergiaMax: 2, CuraFerimentos: true, Nanites: 1, Ligado: false } },
					  $"item {doTanque}; {(t2?.Regenerador is { } r ? $"vel {r.Eficiencia} bat {r.EnergiaMax} fer {r.CuraFerimentos} nan {r.Nanites} ligado {r.Ligado}" : "sem estado")}");

			// UM ID COPIADO (se um dia houver um) NAO DEVOLVE O ESTADO DE NOVO: a gaveta ja se esvaziou.
			if (doTanque != null && t2 != null)
			{
				amigo.Mochila.Guardar(doTanque);
				Obra? t3 = AssentarItemMe(amigo, doTanque, px + 1, py + 3);
				AfirmarMe("...e o mesmo id assentado de novo (uma cópia) nasce de fábrica: a gaveta já foi esvaziada pela primeira",
						  t3 != null && t3.Regenerador == null, t3 == null ? "não assentou" : $"estado {(t3.Regenerador == null ? "nenhum" : "devolvido")}");
			}
		}
		else AfirmarMe("(montagem) o regenerador assentou", false);
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: a dona das maquinas", "bancada_melhorias", NoMe(px + 1, py + 2), 100);

		// ------------------------------------------------------------ UMA MAQUINA DA v0.2.1 (o saco `Obra.Tech`)
		dono.Pos = NoMe(px + 1, py + 2);
		if (AssentarDeTeste(dono, EstacaoDeRecarga.Tipo, px + 1, py + 1) is { } e1)
		{
			foreach (string m in new[] { EstacaoDeRecarga.Nivel, EstacaoDeRecarga.Nivel, EstacaoDeRecarga.Eficiencia, EstacaoDeRecarga.Solar })
				ComandoDeInteracao(dono, EstacaoDeRecarga.VerboMelhorar, m);
			bool melhorou = e1.Tech is { } antes && antes.Num(EstacaoDeRecarga.ChaveNivel) == 3 && antes.Num(EstacaoDeRecarga.ChaveEficiencia) == 1.0
							&& antes.Ligado(EstacaoDeRecarga.ChaveSolar);
			ComandoDeInteracao(dono, "pegar", "");
			string? daEstacao = ItemDoTipoMe(dono, EstacaoDeRecarga.Tipo);
			Obra? e2 = daEstacao == null ? null : AssentarItemMe(dono, daEstacao, px + 5, py + 1);
			AfirmarMe("**AS MÁQUINAS DA v0.2.1 TAMBÉM** (o estado no saco `Obra.Tech`): a estação de recarga no nível 3, eficiência 1 e com painéis volta assim",
					  melhorou && e2?.Tech is { } depois && depois.Num(EstacaoDeRecarga.ChaveNivel) == 3 && depois.Num(EstacaoDeRecarga.ChaveEficiencia) == 1.0
					  && depois.Ligado(EstacaoDeRecarga.ChaveSolar) && CatalogoDeItens.Get(daEstacao!)?.Nome.Contains("nível 3", StringComparison.Ordinal) == true,
					  $"item {daEstacao}; nivel {e2?.Tech?.Num(EstacaoDeRecarga.ChaveNivel)}");
		}
		else AfirmarMe("(montagem) a estação de recarga assentou", false);
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: a dona das maquinas", "bancada_melhorias", NoMe(px + 1, py + 2), 100);

		// ------------------------------------------------------------ A NAVE
		Guardar(dono, "Spacepod");
		Nave? n1 = AssentarNaveMe(dono, "Spacepod", px + 5, py + 1);
		if (n1 != null)
		{
			for (int i = 0; i < 3; i++) ComandoDeInteracao(dono, "nave_melhorar", "");
			bool subiu = n1.Velocidade == 4;
			List<string> aoRecolher = Ouvir(() => ComandoDeInteracao(dono, "nave_pegar", ""));
			string? daNave = ItemDoTipoMe(dono, "Spacepod");
			Nave? n2 = daNave == null ? null : AssentarNaveMe(dono, daNave, px + 1, py + 1);
			AfirmarMe("**A NAVE TAMBÉM**: a velocidade comprada (4x) não se perde mais ao desmontar -- vai no item, e volta com ela",
					  subiu && daNave != null && MaquinaGuardada.Gaveta(daNave) > 0 && Disse(aoRecolher, "velocidade vai junto")
					  && n2 is { Velocidade: 4 } && n2 != n1,
					  $"item {daNave}; velocidade {n2?.Velocidade}; {Ultimos(aoRecolher)}");

			if (n2 != null)
			{
				MaquinaRecolhidaPerdeAsMelhoriasDeTeste = true;
				try
				{
					dono.Pos = NoMe(px + 1, py + 2);
					ComandoDeInteracao(dono, "nave_pegar", "");
					string? cru = ItemDoTipoMe(dono, "Spacepod");
					Nave? n3 = cru == null ? null : AssentarNaveMe(dono, cru, px + 5, py + 1);
					AfirmarMe("(defeito injetado: a máquina recolhida perde o estado) a mesma régua REPROVA -- a nave volta no degrau 1",
							  cru == "Spacepod" && n3 is { Velocidade: Naves.VelocidadeInicial }, $"item {cru}; velocidade {n3?.Velocidade}");
				}
				finally { MaquinaRecolhidaPerdeAsMelhoriasDeTeste = false; }
			}
		}
		else AfirmarMe("(montagem) a nave assentou pelo funil", false);
	}

	// =====================================================================
	// 2) O TETO PELA TECNOLOGIA, MELHORIA POR MELHORIA
	// =====================================================================
	/// <summary>
	/// UMA MELHORIA CONTRA O TETO DA TECNOLOGIA, pelo verbo de producao, em quatro passos: (1) no teto de quem tem 25 de
	/// tecnologia ela e recusada, sem cobrar, e diz por que; (2) com o defeito injetado (sem teto) a MESMA compra passa -- a
	/// regua sabe reprovar; (3) um degrau abaixo ela compra; (4) com 100 de tecnologia, do mesmo ponto do teto de 25, ela compra
	/// -- o jogo de antes, como o dono pediu.
	/// </summary>
	private void MeNoTeto(string nome, ServerPlayer quem, Action porNoTeto, Action porAbaixo, Action melhorar, Func<double> medir)
	{
		quem.Ficha.techskill = 25;
		porNoTeto();
		double noTeto = medir(), zeni = quem.Ficha.Zeni;
		List<string> falas = Ouvir(melhorar);
		AfirmarMe($"{nome}: NO TETO DA TECNOLOGIA (25) a melhoria é recusada, sem cobrar, e diz o porquê",
				  medir() == noTeto && quem.Ficha.Zeni == zeni && Disse(falas, "teto da sua tecnologia"), $"{noTeto} -> {medir()}; {Ultimos(falas)}");

		TetoPorTecnologia.SemTetoDeTeste = true;
		try
		{
			porNoTeto();
			Ouvir(melhorar);
			AfirmarMe("   (defeito injetado: sem teto de tecnologia) a mesma régua REPROVA -- a compra passa do teto", medir() > noTeto, $"{noTeto} -> {medir()}");
		}
		finally { TetoPorTecnologia.SemTetoDeTeste = false; }

		porAbaixo();
		double abaixo = medir();
		zeni = quem.Ficha.Zeni;
		Ouvir(melhorar);
		AfirmarMe("   ...um degrau abaixo do teto ela compra, e cobra", medir() > abaixo && quem.Ficha.Zeni < zeni, $"{abaixo} -> {medir()}");

		quem.Ficha.techskill = 100;
		porNoTeto();
		Ouvir(melhorar);
		AfirmarMe("   ...e com 100 de tecnologia, do mesmo ponto, ela compra: o jogo de antes", medir() > noTeto, $"{noTeto} -> {medir()}");
	}

	private void MeDoTetoDaTecnologia(int px, int py, int obrasAntes, int navesAntes)
	{
		GD.Print("[melhorias] -- 2) o teto pela tecnologia de quem paga, em cada melhoria que sobe (`TetoPorTecnologia`) --");
		ServerPlayer dono = ForjarMe("me: quem melhora", "bancada_melhorias", NoMe(px + 1, py + 2), 25);

		// ------------------------------------------------------------ A MAQUINA DE GRAVIDADE
		dono.Pos = NoMe(px + 1, py + 2);
		if (AssentarDeTeste(dono, "Gravity", px + 1, py + 1) is { } gm)
		{
			ComandoDeInteracao(dono, "grav_info", "");
			GravidadeDaObra g = gm.Gravidade!;
			MeNoTeto("gravidade, força do campo (teto 125x aos 25)", dono, () => g.Max = 125, () => g.Max = 124,
					 () => ComandoDeInteracao(dono, "grav_up", "forca"), () => g.Max);
			MeNoTeto("gravidade, bateria (5 dobras aos 25)", dono, () => g.EnergiaMax = 32, () => g.EnergiaMax = 16,
					 () => ComandoDeInteracao(dono, "grav_up", "bateria"), () => g.EnergiaMax);
			MeNoTeto("gravidade, alcance (3 aos 25)", dono, () => g.Range = 3, () => g.Range = 2,
					 () => ComandoDeInteracao(dono, "grav_up", "alcance"), () => g.Range);
			MeNoTeto("gravidade, nanites (5 aos 25)", dono, () => g.Nanites = 5, () => g.Nanites = 4,
					 () => ComandoDeInteracao(dono, "grav_up", "nanites"), () => g.Nanites);
		}
		else AfirmarMe("(montagem) a máquina de gravidade assentou", false);
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: quem melhora", "bancada_melhorias", NoMe(px + 1, py + 2), 25);

		// ------------------------------------------------------------ O REGENERADOR
		dono.Pos = NoMe(px + 1, py + 2);
		if (AssentarDeTeste(dono, Regenerador.Tipo, px + 1, py + 1) is { } tm)
		{
			RegeneradorDaObra r = EstadoDoTanque(tm);
			MeNoTeto("regenerador, bateria maior (6 cargas aos 25)", dono, () => r.EnergiaMax = 6, () => r.EnergiaMax = 5,
					 () => ComandoDeInteracao(dono, "regen_up", "bateria"), () => r.EnergiaMax);
			MeNoTeto("regenerador, velocidade de cura (6 aos 25)", dono, () => r.Eficiencia = 6, () => r.Eficiencia = 5,
					 () => ComandoDeInteracao(dono, "regen_up", "velocidade"), () => r.Eficiencia);
			MeNoTeto("regenerador, nanites (5 aos 25)", dono, () => r.Nanites = 5, () => r.Nanites = 4,
					 () => ComandoDeInteracao(dono, "regen_up", "nanites"), () => r.Nanites);
		}
		else AfirmarMe("(montagem) o regenerador assentou", false);
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: quem melhora", "bancada_melhorias", NoMe(px + 1, py + 2), 25);

		// ------------------------------------------------------------ A DRILL
		dono.Pos = NoMe(px + 1, py + 2);
		if (AssentarDeTeste(dono, Drill.Tipo, px + 1, py + 1) is { } dm)
		{
			DrillDaObra d = dm.Drill ??= new DrillDaObra { ContadoAteMs = NowMs() };
			MeNoTeto("drill, nível (3 aos 25)", dono, () => d.Nivel = 3, () => d.Nivel = 2,
					 () => ComandoDeInteracao(dono, Drill.VerboMelhorar, Drill.PagarComZeni), () => d.Nivel);
		}
		else AfirmarMe("(montagem) a Drill assentou", false);
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: quem melhora", "bancada_melhorias", NoMe(px + 1, py + 2), 25);

		// ------------------------------------------------------------ A ESTACAO DE RECARGA
		dono.Pos = NoMe(px + 1, py + 2);
		if (AssentarDeTeste(dono, EstacaoDeRecarga.Tipo, px + 1, py + 1) is { } em)
		{
			EstadoDeTech e = EstadoDaEstacao(em);
			MeNoTeto("estação de recarga, eficiência (1 aos 25)", dono,
					 () => e.N[EstacaoDeRecarga.ChaveEficiencia] = 1.0, () => e.N[EstacaoDeRecarga.ChaveEficiencia] = 0.5,
					 () => ComandoDeInteracao(dono, EstacaoDeRecarga.VerboMelhorar, EstacaoDeRecarga.Eficiencia), () => e.Num(EstacaoDeRecarga.ChaveEficiencia));
			MeNoTeto("estação de recarga, nível (2 aos 25)", dono,
					 () => e.N[EstacaoDeRecarga.ChaveNivel] = 2, () => e.N[EstacaoDeRecarga.ChaveNivel] = 1,
					 () => ComandoDeInteracao(dono, EstacaoDeRecarga.VerboMelhorar, EstacaoDeRecarga.Nivel), () => e.Num(EstacaoDeRecarga.ChaveNivel));
		}
		else AfirmarMe("(montagem) a estação de recarga assentou", false);
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: quem melhora", "bancada_melhorias", NoMe(px + 1, py + 2), 25);

		// ------------------------------------------------------------ O SIMULADOR
		dono.Pos = NoMe(px + 1, py + 2);
		if (AssentarDeTeste(dono, Simulador.Tipo, px + 1, py + 1) is { } sm)
		{
			EstadoDeTech e = EstadoDoSimulador(sm);
			MeNoTeto("simulador, teto de poder (5 dobras aos 25)", dono,
					 () => e.N[Simulador.ChaveTeto] = Simulador.TetoDeFabrica * 32, () => e.N[Simulador.ChaveTeto] = Simulador.TetoDeFabrica * 16,
					 () => ComandoDeInteracao(dono, Simulador.VerboMelhorar, Simulador.Poder), () => e.Num(Simulador.ChaveTeto));
			MeNoTeto("simulador, energia (6 aos 25)", dono,
					 () => e.N[Simulador.ChaveEnergiaMax] = 6, () => e.N[Simulador.ChaveEnergiaMax] = 5,
					 () => ComandoDeInteracao(dono, Simulador.VerboMelhorar, Simulador.Energia), () => e.Num(Simulador.ChaveEnergiaMax));
		}
		else AfirmarMe("(montagem) o simulador assentou", false);
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: quem melhora", "bancada_melhorias", NoMe(px + 1, py + 2), 25);

		// ------------------------------------------------------------ A MAQUINA DE CLONAGEM
		dono.Pos = NoMe(px + 1, py + 2);
		if (AssentarDeTeste(dono, MaquinaDeClonagem.Tipo, px + 1, py + 1) is { } cm)
		{
			EstadoDeTech e = EstadoDaClonagem(cm);
			MeNoTeto("clonagem, sessão mais curta (6 aos 25)", dono,
					 () => e.N[MaquinaDeClonagem.ChaveVelocidade] = 6, () => e.N[MaquinaDeClonagem.ChaveVelocidade] = 5,
					 () => ComandoDeInteracao(dono, MaquinaDeClonagem.VerboMelhorar, MaquinaDeClonagem.Tempo), () => e.Num(MaquinaDeClonagem.ChaveVelocidade));
			MeNoTeto("clonagem, menos falha (6 aos 25)", dono,
					 () => e.N[MaquinaDeClonagem.ChaveFalha] = 6, () => e.N[MaquinaDeClonagem.ChaveFalha] = 5,
					 () => ComandoDeInteracao(dono, MaquinaDeClonagem.VerboMelhorar, MaquinaDeClonagem.Falha), () => e.Num(MaquinaDeClonagem.ChaveFalha));
			MeNoTeto("clonagem, bateria (7 aos 25)", dono,
					 () => e.N[MaquinaDeClonagem.ChaveEnergiaMax] = 7, () => e.N[MaquinaDeClonagem.ChaveEnergiaMax] = 6,
					 () => ComandoDeInteracao(dono, MaquinaDeClonagem.VerboMelhorar, MaquinaDeClonagem.Bateria), () => e.Num(MaquinaDeClonagem.ChaveEnergiaMax));
		}
		else AfirmarMe("(montagem) a máquina de clonagem assentou", false);
		LimparPalcoMe(obrasAntes, navesAntes);
		dono = ForjarMe("me: quem melhora", "bancada_melhorias", NoMe(px + 1, py + 2), 25);

		// ------------------------------------------------------------ A NAVE
		Guardar(dono, "Spacepod");
		if (AssentarNaveMe(dono, "Spacepod", px + 1, py + 1) is { } n)
			MeNoTeto("nave, velocidade (10x aos 25)", dono, () => n.Velocidade = 10, () => n.Velocidade = 9,
					 () => ComandoDeInteracao(dono, "nave_melhorar", ""), () => n.Velocidade);
		else AfirmarMe("(montagem) a nave assentou", false);
	}

	// =====================================================================
	// 3) A FORCA DO CAMPO PARA NO TETO DO MUNDO
	// =====================================================================
	private void MeDoTetoDoMundo(int px, int py)
	{
		GD.Print($"[melhorias] -- 3) a força do campo para no teto do mundo ({Interacoes.TetoDeGravidade:0}x), e não passa dele (`TetoDaForca`) --");
		ServerPlayer dono = ForjarMe("me: quem melhora no teto", "bancada_melhorias", NoMe(px + 1, py + 2), 100);
		dono.Pos = NoMe(px + 1, py + 2);
		if (AssentarDeTeste(dono, "Gravity", px + 1, py + 1) is not { } gm) { AfirmarMe("(montagem) a máquina de gravidade assentou", false); return; }
		ComandoDeInteracao(dono, "grav_info", "");
		GravidadeDaObra g = gm.Gravidade!;
		double teto = Interacoes.TetoDeGravidade;

		g.Max = 446;   // o degrau seguinte da escada de 1,2 seria 535x
		bool noMenuAntes = MelhoriasNoMenu(gm).Contains("upgrades/forca");
		List<string> falas = Ouvir(() => ComandoDeInteracao(dono, "grav_up", "forca"));
		AfirmarMe($"**O DEGRAU QUE PASSARIA DO TETO PARA NELE**: com 100 de tecnologia, 446x vira {teto:0}x -- e não 535x",
				  g.Max == teto && Disse(falas, "o teto do mundo"), $"{g.Max:0}x; {Ultimos(falas)}");
		// O GATILHO DESTA E DELA: a maquina vai pro teto do mundo aqui, e nao herda o degrau de cima -- que pode ter falhado
		// sozinho (sem o degrau aparado, ela ficaria em 535x e esta afirmacao reprovaria junto, por outro culpado).
		g.Max = teto;
		double zeni = dono.Ficha.Zeni;
		falas = Ouvir(() => ComandoDeInteracao(dono, "grav_up", "forca"));
		AfirmarMe("...e no teto do mundo a melhoria é recusada sem cobrar -- e some do menu dela, como o alcance no 10",
				  g.Max == teto && dono.Ficha.Zeni == zeni && Disse(falas, "teto do mundo") && noMenuAntes
				  && !MelhoriasNoMenu(gm).Contains("upgrades/forca") && MelhoriasNoMenu(gm).Contains("upgrades/bateria"),
				  $"{g.Max:0}x; menu [{string.Join(",", MelhoriasNoMenu(gm))}]; {Ultimos(falas)}");

		g.Max = 535;   // uma maquina de antes do conserto, gravada no `mundo.json` acima do teto
		ComandoDeInteracao(dono, "grav_info", "");
		AfirmarMe($"a máquina de ANTES do conserto, melhorada além do teto (535x), desce pra ele ({teto:0}x) no primeiro toque",
				  g.Max == teto, $"{g.Max:0}x");

		ForcaPassaDoTetoDoMundoDeTeste = true;
		try
		{
			g.Max = 446;
			ComandoDeInteracao(dono, "grav_up", "forca");
			AfirmarMe("(defeito injetado: a força passa do teto do mundo) a mesma régua REPROVA -- a máquina chega a 535x, que ela nunca liga",
					  g.Max > teto, $"{g.Max:0}x");
		}
		finally { ForcaPassaDoTetoDoMundoDeTeste = false; }
		g.Max = teto;
	}
}
