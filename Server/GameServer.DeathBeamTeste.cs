using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// 5c) O DEATH BEAM E MUITO RAPIDO E ESTOURA NO PRIMEIRO CORPO (`--censoteste`).
///
/// O pedido do dono (2026-10-08): *"death beam deveria ser mt rapido, e ao se chocar ele diferente dos outros
/// beams ele explode igual um blast causando 1 hit apenas"*.
///
/// ============================ O QUE SE MEDE, E POR ONDE ============================
/// O raio sai pelo VERBO de producao (`UsarHabilidade` -> `RaioNomeadoG6` -> `Canalizar` -> a carga inteira ->
/// `Disparar`): a receita que voa aqui e a que o jogador atira. O unico privilegio e a cadencia (os tiques na
/// mao) e o dado da deflexao desligado no raio ja nascido -- o `Acertar` sorteia em todo impacto acima do corte dos fracos contra quem
/// esta de pe, e o que se mede aqui e o impacto, nao o dado (o mesmo motivo do `RaioDaBancada`).
///
///   * RAPIDO: a velocidade do raio vivo contra a do raio comum, o que ele anda em dois tiques, e quanto leva
///     pra gastar os dez tiles de alcance.
///   * UM GOLPE: os relatos lidos do FIO (`EscutaDeGolpes`) -- um so, com o raio morto NO TIQUE do impacto
///     (`Acertou`, sem rastro sendo engolido), o canal de quem atirou fechado junto e as redeas do corpo com
///     o dono dele. O alvo fica a oito tiles: bem alem dos quatro em que um tiro arremessa, onde um raio comum
///     PEGA quem acerta pra levar junto.
///   * CONTRA A GUARDA: um golpe aparado, e nenhum embate de maos.
///
/// ============================ OS CONTRA-EXEMPLOS ============================
/// `Projetil.RaioQueEstouraMoiDeTeste` devolve o raio de antes (planta, moi a cada 0,2 s, carrega, vira embate
/// contra a guarda) e `Projetil.BeamspeedLidoComoSpeedDeTeste` devolve a velocidade de antes (6 tiles por
/// segundo). As mesmas cenas, medidas de novo, tem que reprovar.
/// ==========================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// A que distancia o alvo fica, em tiles: dentro do alcance (10) e com FOLGA alem do arremesso de perto. O
	/// arremesso vale ate quatro tiles VIAJADOS pela cabeca, e ela nasce um tile a frente da mao e encosta com a
	/// frente -- a seis tiles ela chega com 3,7 a 4,2 viajados (depende do sub-passo), e a cena caia ora num ramo,
	/// ora no outro. A oito sao seis viajados: o ramo e sempre o de quem um raio comum PEGA pra levar.
	/// </summary>
	private const int TilesDoAlvoDoDeathBeam = 8;

	/// <summary>
	/// O alvo DE PERTO: tres tiles, bem dentro do arremesso. E onde um raio comum fica plantado MOENDO (a bancada
	/// nao roda o tique do arremesso, entao o corpo nao sai da frente) -- a cena em que "um golpe so" tem o que negar.
	/// </summary>
	private const int TilesDoAlvoDePerto = 3;

	private void ODeathBeamERapidoEEstoura()
	{
		GD.Print("[censo] -- 5c) O DEATH BEAM: MUITO RAPIDO, E ESTOURA NO PRIMEIRO CORPO COM UM GOLPE SO");
		const int T = ZoneCollision.TileSize;
		double dt = Protocol.TickSeconds;

		// AS FAIXAS DO MAPA VOLTAM PRA ONDE ESTAVAM NO FIM: cada cena daqui pede um corredor, e o `CorredorLivre` so
		// anda pra frente -- as familias seguintes do censo nasceriam em outro pedaco do mapa (a primeira rodada
		// derrubou assim a dos Punhos do Lobo, que precisa de chao livre a frente pra avancar). Tudo que esta
		// familia poe no mundo ela recolhe, entao devolver a faixa e devolver o mapa como ele estava.
		int faixaDeAntes = _pjProximoCorredor;
		try
		{
			ODeathBeamMedido(T, dt);
			OsRaiosNomeadosEstaoNivelados();
		}
		finally
		{
			Projetil.RaioQueEstouraMoiDeTeste = Projetil.BeamspeedLidoComoSpeedDeTeste = false;
			LimparEmbatesDaBancada();
			_pjProximoCorredor = faixaDeAntes;
		}
	}

	/// <summary>
	/// 5d) OS OUTROS RAIOS NOMEADOS, NIVELADOS (dono, 2026-10-08: *"pode nivelar entre 24 e 30"*). O raio VIVO de cada
	/// verbo, pela velocidade com que nasceu: entre 24 e 30 tiles por segundo, acima do raio comum, na ordem do
	/// `beamspeed` do verb -- e o jogo de antes (`BeamspeedLidoComoSpeedDeTeste`) com o Kamehameha atras do raio comum.
	/// O Final Flash e o Massive Beam sao do lote G5 e passam pela MESMA conta (`Projetil.VelocidadeDeRaioNomeado`),
	/// conferida aqui nos dois extremos.
	/// </summary>
	private void OsRaiosNomeadosEstaoNivelados()
	{
		GD.Print("[censo] -- 5d) OS RAIOS NOMEADOS VOAM ENTRE 24 E 30 TILES POR SEGUNDO, NA ORDEM DO VERB");
		double comum = 1 / Projetil.AtrasoDeRaio(1);

		var medidos = new Dictionary<string, double>();
		foreach (string verbo in new[] { "Boom_Wave", "GalicGun", "Enkumei", "Kamehameha", "Dodompa" })
		{
			(_, Projetil? raio) = NasceORaioNomeado(verbo, CorredorLivre(16));
			medidos[verbo] = raio == null ? 0 : 1 / raio.SegundosPorTile;
			LimparEmbatesDaBancada();
		}
		string lista = string.Join(", ", medidos.Select(m => $"{m.Key} {m.Value:0.#}"));
		GD.Print($"[censo]      (medido) {lista}; raio comum {comum:0.#}");

		AfirmarCen("os cinco nascem pelo verbo de producao e voam entre 24 e 30 tiles por segundo -- todos ACIMA do raio comum",
				   medidos.Values.All(v => v >= 24 - 1e-6 && v <= 30 + 1e-6 && v > comum), lista);
		AfirmarCen("...na ORDEM do verb (menor `beamspeed`, mais rapido): Boom Wave > Galick Ho = Enkumei > Kamehameha > Dodon Ray",
				   medidos["Boom_Wave"] > medidos["GalicGun"] && Math.Abs(medidos["GalicGun"] - medidos["Enkumei"]) < 1e-9
				   && medidos["Enkumei"] > medidos["Kamehameha"] && medidos["Kamehameha"] > medidos["Dodompa"], lista);

		double doMaisLento = 1 / Projetil.AtrasoDeRaio(Projetil.VelocidadeDeRaioNomeado(Projetil.BeamspeedMaisLento));
		double doMaisRapido = 1 / Projetil.AtrasoDeRaio(Projetil.VelocidadeDeRaioNomeado(Projetil.BeamspeedMaisRapido));
		AfirmarCen("...e os extremos sao os do dono: o `beamspeed` 0,6 (Final Flash, Dodon Ray) da 24 e o 0,2 (Boom Wave) da 30",
				   Math.Abs(doMaisLento - 24) < 1e-9 && Math.Abs(doMaisRapido - 30) < 1e-9
				   && Math.Abs(medidos["Dodompa"] - doMaisLento) < 1e-9 && Math.Abs(medidos["Boom_Wave"] - doMaisRapido) < 1e-9,
				   $"{doMaisLento:0.###} e {doMaisRapido:0.###}");

		Projetil.BeamspeedLidoComoSpeedDeTeste = true;
		try
		{
			(_, Projetil? kame) = NasceORaioNomeado("Kamehameha", CorredorLivre(16));
			double tilesPorSegundo = kame == null ? 0 : 1 / kame.SegundosPorTile;
			AfirmarCen("(defeito injetado: o `beamspeed` lido como `speed`) o Kamehameha volta a voar ATRAS do raio comum",
					   kame != null && tilesPorSegundo < comum, $"{tilesPorSegundo:0.#} contra {comum:0.#} tiles/s");
		}
		finally
		{
			Projetil.BeamspeedLidoComoSpeedDeTeste = false;
			LimparEmbatesDaBancada();
		}
	}

	private void ODeathBeamMedido(int T, double dt)
	{

		// ---------------------------------------------------------------- (a) a velocidade, sem ninguem no caminho
		(_, Projetil? solto) = NasceODeathBeam(CorredorLivre(16));
		AfirmarCen("PREPARO: o Death Beam nasce pelo verbo de producao, depois da carga",
				   solto is { Vivo: true, Tipo: TipoDeProjetil.Beam });
		if (solto == null) { LimparEmbatesDaBancada(); return; }

		double doDeathBeam = 1 / solto.SegundosPorTile, doRaioComum = 1 / Projetil.AtrasoDeRaio(1);
		GD.Print($"[censo]      (medido) Death Beam a {doDeathBeam:0.#} tiles/s, raio comum a {doRaioComum:0.#}, pressa {solto.Pressa:0.##}, "
				 + $"alcance {solto.MaxDistancia:0} tiles");
		AfirmarCen("o Death Beam e MUITO rapido: mais de tres vezes o raio comum",
				   doDeathBeam > 3 * doRaioComum, $"{doDeathBeam:0.#} contra {doRaioComum:0.#} tiles/s");

		float x0 = solto.Pos.X;
		TickDosProjeteis(dt);
		TickDosProjeteis(dt);
		float andou = solto.Pos.X - x0, previsto = (float)(T / solto.SegundosPorTile * 2 * dt);
		AfirmarCen("...e ele ANDA isso: em dois tiques, o que a velocidade dele manda",
				   Math.Abs(andou - previsto) < 2f, $"{andou:0.#} px contra {previsto:0.#} px");
		AfirmarCen("...com a pressa que ele declara: o aviso do DM continua valendo o mesmo TEMPO (`PressaSobreORaioDoDm`)",
				   Math.Abs(solto.Pressa - doDeathBeam * Projetil.AtrasoDeRaioNoDm(1)) < 1e-6 && solto.Pressa > Projetil.PressaDeRaio,
				   $"pressa {solto.Pressa:0.###}");

		int tiques = 2;
		while (solto.Vivo && !solto.Esvaziando && tiques < 90) { TickDosProjeteis(dt); tiques++; }
		AfirmarCen("...e gasta os dez tiles do alcance em menos de um quinto de segundo",
				   solto.Esvaziando || !solto.Vivo ? tiques * dt < 0.2 : false, $"{tiques} tiques ({tiques * dt:0.00} s)");
		for (int i = 0; i < 90 && solto.Vivo; i++) TickDosProjeteis(dt);
		AfirmarCen("ERROU: sem ninguem no caminho ele se APAGA no fim do alcance, como qualquer raio (nada de estouro no ar)",
				   !solto.Vivo && solto.Fim == FimDeProjetil.Apagou, $"vivo {solto.Vivo}, fim {solto.Fim}");
		LimparEmbatesDaBancada();

		// ---------------------------------------------------------------- (b) num corpo: um golpe, e acabou
		var m = ODeathBeamNumCorpo(deGuarda: false);
		AfirmarCen("PREPARO: o raio nasceu e ALCANCOU o corpo (oito tiles: dentro do alcance, alem do arremesso de perto)",
				   m.Nasceu && m.Golpes >= 1, $"nasceu {m.Nasceu}, {m.Golpes} relato(s)");
		GD.Print($"[censo]      (medido) NUM CORPO: {m.Golpes} golpe(s) em 1,5 s, vida -{m.Dano:0.###}, fim {m.Fim}, "
				 + $"vivo no tique do impacto: {m.VivoNoImpacto}, canal aberto: {m.CanalNoImpacto}, corpo pego pelo raio: {m.PegoNoImpacto}");
		AfirmarCen("NUM CORPO: o Death Beam da UM golpe -- um relato so em um segundo e meio, com o dono sem soltar o botao",
				   m.Golpes == 1 && m.Dano > 0 && m.DanoDepois == m.Dano,
				   $"{m.Golpes} relato(s), vida -{m.Dano:0.###} no impacto e -{m.DanoDepois:0.###} no fim");
		AfirmarCen("...e ESTOURA ali: o raio acaba no tique do impacto, inteiro (`Acertou`, sem rastro sendo engolido)",
				   !m.VivoNoImpacto && m.Fim == FimDeProjetil.Acertou && !m.NaZonaNoImpacto,
				   $"vivo {m.VivoNoImpacto}, fim {m.Fim}, ainda na zona {m.NaZonaNoImpacto}");
		AfirmarCen("...a mao de quem atirou fica LIVRE no mesmo tique (o canal fecha junto: nao ha raio pra segurar)",
				   !m.CanalNoImpacto && m.SoltoNoImpacto);
		AfirmarCen("...e ele nao PEGA quem acerta: as redeas do corpo ficam com o dono dele (ali um raio comum comeca a leva-lo)",
				   !m.PegoNoImpacto);

		Projetil.RaioQueEstouraMoiDeTeste = true;
		try
		{
			var d = ODeathBeamNumCorpo(deGuarda: false);
			GD.Print($"[censo]      (defeito) NUM CORPO: {d.Golpes} golpe(s), fim {d.Fim}, vivo {d.VivoNoImpacto}, canal {d.CanalNoImpacto}, corpo pego {d.PegoNoImpacto}");
			AfirmarCen("(defeito injetado: o raio que estoura volta a ser um raio comum) o mesmo Death Beam NAO acaba no impacto: fica vivo, "
					   + "segura o dono e pega o corpo pra levar",
					   d.Nasceu && d.VivoNoImpacto && d.CanalNoImpacto && d.PegoNoImpacto && d.Fim != FimDeProjetil.Acertou,
					   $"vivo {d.VivoNoImpacto}, canal {d.CanalNoImpacto}, pego {d.PegoNoImpacto}, fim {d.Fim}");
		}
		finally { Projetil.RaioQueEstouraMoiDeTeste = false; }

		// ---------------------------------------------------------------- (b') de perto, onde um raio comum se planta e moi
		var perto = ODeathBeamNumCorpo(deGuarda: false, tiles: TilesDoAlvoDePerto);
		AfirmarCen("DE PERTO tambem: a tres tiles e UM golpe, e o raio estoura",
				   perto.Nasceu && perto.Golpes == 1 && perto.Fim == FimDeProjetil.Acertou && !perto.VivoNoImpacto,
				   $"{perto.Golpes} relato(s), fim {perto.Fim}, vivo {perto.VivoNoImpacto}");

		Projetil.RaioQueEstouraMoiDeTeste = true;
		try
		{
			var d = ODeathBeamNumCorpo(deGuarda: false, tiles: TilesDoAlvoDePerto);
			AfirmarCen("(defeito injetado: o raio que estoura volta a se plantar e moer) de perto o mesmo Death Beam bate VARIAS vezes "
					   + "(um golpe a cada 0,2 s enquanto o dono segura)",
					   d.Nasceu && d.Golpes >= 4, $"{d.Golpes} relato(s) em 1,5 s, fim {d.Fim}");
		}
		finally { Projetil.RaioQueEstouraMoiDeTeste = false; }

		// ---------------------------------------------------------------- (c) contra a guarda: aparado, e sem embate
		var g = ODeathBeamNumCorpo(deGuarda: true);
		GD.Print($"[censo]      (medido) NA GUARDA: {g.Golpes} golpe(s), desfecho {(Desfecho)g.Desfecho}, fim {g.Fim}, embate de maos: {g.Embate}");
		AfirmarCen("NA GUARDA: ele nao para nas maos de ninguem -- um golpe APARADO, o raio estoura, e nenhum embate comeca",
				   g.Nasceu && g.Golpes == 1 && g.Desfecho == (byte)Desfecho.Aparou && g.Fim == FimDeProjetil.Acertou && !g.Embate,
				   $"{g.Golpes} relato(s), desfecho {(Desfecho)g.Desfecho}, fim {g.Fim}, embate {g.Embate}");

		Projetil.RaioQueEstouraMoiDeTeste = true;
		try
		{
			var d = ODeathBeamNumCorpo(deGuarda: true);
			AfirmarCen("(defeito injetado: o raio que estoura volta a ser um raio comum) contra a guarda ele vira EMBATE de maos",
					   d.Nasceu && d.Embate, $"embate {d.Embate}, {d.Golpes} relato(s), fim {d.Fim}");
		}
		finally { Projetil.RaioQueEstouraMoiDeTeste = false; }

		// ---------------------------------------------------------------- (d) o jogo de antes: o raio mais lento que quem corre
		Projetil.BeamspeedLidoComoSpeedDeTeste = true;
		try
		{
			(_, Projetil? lento) = NasceODeathBeam(CorredorLivre(16));
			double tilesPorSegundo = lento == null ? 0 : 1 / lento.SegundosPorTile;
			AfirmarCen("(defeito injetado: o `beamspeed` lido como `speed`) o Death Beam volta a ser mais LENTO que o raio comum",
					   lento != null && tilesPorSegundo < doRaioComum, $"{tilesPorSegundo:0.#} contra {doRaioComum:0.#} tiles/s");
		}
		finally
		{
			Projetil.BeamspeedLidoComoSpeedDeTeste = false;
			LimparEmbatesDaBancada();
		}
	}

	/// <summary>
	/// O DEATH BEAM DE PRODUCAO NO AR: o verbo, a carga inteira, e o raio recem-nascido -- ainda na boca do cano,
	/// porque quem o faz nascer e o tique dos CANAIS e quem o anda e o dos projeteis.
	/// </summary>
	private (ServerPlayer Atirador, Projetil? Raio) NasceODeathBeam(Vec2 chao) => NasceORaioNomeado("Death_Beam", chao);

	/// <summary>O MESMO, PRA QUALQUER UM DOS SEIS RAIOS NOMEADOS: o verbo de producao, a carga inteira, o raio na boca do cano.</summary>
	private (ServerPlayer Atirador, Projetil? Raio) NasceORaioNomeado(string verbo, Vec2 chao)
	{
		ServerPlayer pl = ForjarArmadoG6("Freeza", chao, bp: 50_000);
		pl.Facing = Facing.East;
		// O PRIMEIRO DEGRAU DE PERICIA, cravado: quatro dos seis verbos trocam de numeros com o `beamskill`
		// (`DegrauDoRaioG6`), e o ultimo degrau do Galick Ho muda ate o `beamspeed`.
		pl.Ficha.beamskill = 0;
		UsarHabilidade(pl, verbo);
		for (int i = 0; i < 30 * 15 && _canais.TryGetValue(pl.Id, out CanalDeKi? c) && c.Raio == null; i++)
			TickDosCanaisDeKi(Protocol.TickSeconds);

		Projetil? raio = _canais.GetValueOrDefault(pl.Id)?.Raio;
		if (raio != null) raio.Deflectivel = false;   // o dado da deflexao fora: ver o cabecalho
		return (pl, raio);
	}

	/// <summary>
	/// A CENA DO IMPACTO, medida: o Death Beam contra um corpo parado a `tiles` tiles (o padrao e o
	/// <see cref="TilesDoAlvoDoDeathBeam"/>), por um segundo e meio, com o dono SEM soltar o botao (e o que separa "um golpe" de "soltei depois de um").
	/// O estado "no impacto" e o do fim do tique em que o primeiro relato saiu.
	/// </summary>
	private (bool Nasceu, int Golpes, byte Desfecho, double Dano, double DanoDepois, FimDeProjetil Fim, bool VivoNoImpacto,
			 bool NaZonaNoImpacto, bool CanalNoImpacto, bool SoltoNoImpacto, bool PegoNoImpacto, bool Embate)
		ODeathBeamNumCorpo(bool deGuarda, int tiles = TilesDoAlvoDoDeathBeam)
	{
		LimparEmbatesDaBancada();
		Vec2 chao = CorredorLivre(16);
		ServerPlayer alvo = Forjar("NaMira", new Vec2(chao.X + tiles * ZoneCollision.TileSize, chao.Y), bp: 50_000);
		if (deGuarda) SegurarAGuarda(alvo);

		(ServerPlayer atirador, Projetil? raio) = NasceODeathBeam(chao);
		if (raio == null) return default;

		double vida0 = alvo.Combate.Corpo.Vida(), danoNoImpacto = 0;
		bool visto = false, vivo = false, naZona = false, canal = false, solto = false, pego = false, embate = false;
		List<Protocol.HitEvent> Relatos() => [.. EscutaDeGolpes!.Where(x => x.Cheio).Select(x => LerGolpe(x.Fio)).Where(h => h.Alvo == alvo.Id)];

		EscutaDeGolpes = [];
		try
		{
			for (int i = 0; i < 45; i++)
			{
				TickDosCanaisDeKi(Protocol.TickSeconds);
				TickDosProjeteis(Protocol.TickSeconds);
				TickDosEmbatesDeKi(Protocol.TickSeconds);
				embate |= _emEmbateDeKi.ContainsKey(alvo.Id);
				if (visto || Relatos().Count == 0) continue;

				visto = true;
				vivo = raio.Vivo;
				naZona = ProjeteisDaZona(atirador.Zone.Hash).Contains(raio);
				canal = _canais.ContainsKey(atirador.Id);
				solto = PodeMexerOCorpo(atirador);
				// PEGO = o raio tomou as redeas do corpo (`ComecarArrasto`, que as tira no instante do impacto).
				pego = alvo.ArrastoRestante > 0;
				danoNoImpacto = vida0 - alvo.Combate.Corpo.Vida();
			}

			List<Protocol.HitEvent> relatos = Relatos();
			return (true, relatos.Count, relatos.Count > 0 ? relatos[0].Desfecho : (byte)0, danoNoImpacto,
					vida0 - alvo.Combate.Corpo.Vida(), raio.Vivo ? raio.FimPendente : raio.Fim, vivo, naZona, canal, solto,
					pego, embate);
		}
		finally { EscutaDeGolpes = null; }
	}
}
