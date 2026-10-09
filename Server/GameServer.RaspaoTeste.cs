using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// 15) UM ENCONTRO, UM SORTEIO (`--projetilteste`) -- o raspao do `Bump` (`objects.dm:358-363`).
///
/// ============================ O QUE SE MEDE ============================
/// Quantas vezes o `Acertar` roda pra UM tiro contra UM corpo quando o sorteio da raspao, e como o tiro acaba.
/// Tudo lido do FIO (`EscutaDeGolpes`: cada raspao e um relato `Esquivou`, cada acerto um relato de dano) e do
/// estado de producao (o `Fim` do tiro, a vida e a pericia de defesa de ki de quem apanha -- cada `Acertar` soma
/// 0,1 nela, e cada raspao mais 0,4).
///
/// AS OUTRAS BANCADAS DE KI DESLIGAM O DADO (`Deflectivel = false`) porque medem outra coisa; aqui o dado e o
/// assunto. Entao ele entra de dois jeitos que nao sao moeda:
///   * CERTO: a chance de deflexao passa de 200% e o `prob(deflectchance/2)` sai SEMPRE. A cena conta relatos, e a
///     conta e exata. Ate 2026-10-08 bastava um fraco atirar num forte; desde o corte dos fracos (`objects.dm:355-357`,
///     `DanoDeKi.CorteDoFraco`) esse tiro nem chega ao `prob` -- o dano final dele e centesimos, e o que nao chega a
///     10 nao e sorteado (a familia 16 mede exatamente esse par). O raspao certo passou a ser CRAVADO NO TIRO, com o
///     dano acima do corte: ver `CravarORaspaoCerto`, que e o controle que o BYOND mediu (200 raspoes em 200 bolas).
///   * CRAVADO EM 20%: oito mil tiros com o dano acima do corte e a chance acertada no proprio tiro, e a proporcao
///     dos tres desfechos comparada com a do DM (10% raspam, 18% defletem, 72% apanham), com folga de seis desvios.
///
/// ============================ O QUE ELA ACHOU (2026-10-08) ============================
/// O raspao devolvia "o tiro continua" e mais nada, com o corpo parado no lugar: a bola era sorteada de novo a
/// cada sub-passo dentro do raio. ANTES do conserto esta familia mediu 4 `Acertar` por bola (9 na velocidade do
/// DM), 60 numa teleguiada, 20 em 20 tiques de mina, 15 em 18 tiques de raio (com o feixe picotado em quatro), e
/// na amostra: 815 tiros rasparam no primeiro sorteio, 658 apanharam depois, 156 defletiram depois, UM saiu
/// ileso -- 0,01% / 19,76% / 80,23% onde o DM da 10 / 18 / 72. A regra e o conserto estao no `Projetil` (bloco
/// "O RASPAO ENCERRA O ENCONTRO") e no `EncerrarOEncontroNoRaspao`; o contra-exemplo e o
/// `Projetil.RaspaoSorteiaDeNovoDeTeste`, no fim desta familia.
/// =====================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// Os dois corpos das cenas do raspao certo. A razao dos poderes ja NAO basta pra crava-lo (o tiro de um nos
	/// centesimos de dano do outro cai no corte dos fracos): quem o crava e o `CravarORaspaoCerto`, no tiro.
	/// </summary>
	private const double BpDoFracoDoRaspao = 500, BpDoForteDoRaspao = 500_000;

	/// <summary>A chance cravada da amostra, em porcento: 10% de raspao, e dos outros 90%, 20% de deflexao.</summary>
	private const double ChanceCravadaDoRaspao = 20;

	/// <summary>Quantos tiros a amostra da. Oito mil: o desvio-padrao das tres faixas fica entre 0,34 e 0,50 ponto.</summary>
	private const int TirosDaAmostraDeRaspao = 8000;

	private void UmEncontroUmSorteio()
	{
		GD.Print("[projetil] -- 15) UM ENCONTRO, UM SORTEIO: o raspao (`objects.dm:358-363`) encerra o encontro do tiro com o corpo");
		const int T = ZoneCollision.TileSize;

		// ---------------------------------------------------------------- (a) a bola reta
		var m = BolaContraQuemSempreRaspa(teleguiada: false);
		AfirmarPj("PREPARO: contra o forte o raspao e CERTO -- o tiro passa do corte dos fracos (dano final >= 10) e o `prob(deflectchance/2)` sai com a chance acima de 200%",
				  m.Chance / 2 >= 100 && m.Dano >= DanoDeKi.CorteDoFraco, $"chance {m.Chance:0.#}%, dano final {m.Dano:0.##}");
		GD.Print($"[projetil]      (medido) BOLA a {1 / m.Tiro.SegundosPorTile:0.#} tiles/s: {m.Relatos} `Acertar`, {m.Raspoes} raspao(oes), fim {m.Tiro.Fim} "
				 + $"a {(m.Tiro.Pos - m.Forte.Pos).Length / T:0.0} tiles do corpo, pericia +{m.Pericia:0.0}");
		AfirmarPj("BOLA: o tiro que leva o raspao e sorteado UMA vez (um `Bump`, um `prob`)",
				  m.Relatos == 1 && m.Raspoes == 1, $"{m.Relatos} relato(s), {m.Raspoes} raspao(oes)");
		AfirmarPj("BOLA: ...e ACABA ali, sem estouro e sem dano -- o `Move()` do DM tira do mundo a bola cujo passo falhou (`objects.dm:92-99`)",
				  !m.Tiro.Vivo && m.Tiro.Fim == FimDeProjetil.Apagou
				  && (m.Tiro.Pos - m.Forte.Pos).Length <= Projetil.RaioDeImpacto + 0.5f && Math.Abs(m.Vida) < 1e-9,
				  $"fim {m.Tiro.Fim} a {(m.Tiro.Pos - m.Forte.Pos).Length:0.0} px do corpo, vida {m.Vida:+0.###;-0.###;0}");
		AfirmarPj("BOLA: ...e o treino e o de UM encontro: 0,1 de apanhar mais 0,4 do raspao (`objects.dm:313` e `:359`)",
				  Math.Abs(m.Pericia - 0.5) < 1e-9, $"pericia +{m.Pericia:0.0}");

		// ---------------------------------------------------------------- (a') na velocidade do DM
		// O NUMERO DE SUB-PASSOS DENTRO DO RAIO DEPENDE DA VELOCIDADE; a regra nao pode depender.
		Projetil.VelocidadeDoDmDeTeste = true;
		try
		{
			m = BolaContraQuemSempreRaspa(teleguiada: false);
			GD.Print($"[projetil]      (medido) BOLA na velocidade do DM ({1 / m.Tiro.SegundosPorTile:0.#} tiles/s): {m.Relatos} `Acertar`, "
					 + $"{m.Raspoes} raspao(oes), fim {m.Tiro.Fim}, pericia +{m.Pericia:0.0}");
			AfirmarPj("BOLA na velocidade do DM (3,3 tiles/s): um sorteio tambem -- a regra nao depende de quantos sub-passos cabem no raio",
					  m.Relatos == 1 && m.Raspoes == 1 && !m.Tiro.Vivo && Math.Abs(m.Pericia - 0.5) < 1e-9,
					  $"{m.Relatos} relato(s), {m.Raspoes} raspao(oes), pericia +{m.Pericia:0.0}");
		}
		finally { Projetil.VelocidadeDoDmDeTeste = false; }

		// ---------------------------------------------------------------- (b) a teleguiada
		m = BolaContraQuemSempreRaspa(teleguiada: true);
		GD.Print($"[projetil]      (medido) TELEGUIADA: {m.Relatos} `Acertar`, {m.Raspoes} raspao(oes), fim {m.Tiro.Fim} "
				 + $"a {(m.Tiro.Pos - m.Forte.Pos).Length / T:0.0} tiles do corpo, pericia +{m.Pericia:0.0}");
		AfirmarPj("TELEGUIADA: um sorteio, e ela acaba no raspao como a bola (e o mesmo `Move()`: `walk_towards` nao a salva)",
				  m.Relatos == 1 && m.Raspoes == 1 && !m.Tiro.Vivo && m.Tiro.Fim == FimDeProjetil.Apagou
				  && (m.Tiro.Pos - m.Forte.Pos).Length <= Projetil.RaioDeImpacto + 0.5f && Math.Abs(m.Pericia - 0.5) < 1e-9,
				  $"{m.Relatos} relato(s), {m.Raspoes} raspao(oes), fim {m.Tiro.Fim} a {(m.Tiro.Pos - m.Forte.Pos).Length:0.0} px, pericia +{m.Pericia:0.0}");

		// ---------------------------------------------------------------- (c) a bola parada (a mina)
		var mina = MinaContraQuemSempreRaspa();
		GD.Print($"[projetil]      (medido) MINA: {mina.NaEntrada} `Acertar` em 20 tiques com o corpo em cima dela, "
				 + $"{mina.NaVolta} depois de ele sair e voltar; viva {mina.Mina.Vivo}");
		AfirmarPj("MINA (bola parada): com o corpo em cima dela o raspao e sorteado UMA vez, e ela FICA -- quem esbarrou foi o corpo (`mobBump.dm:57-58`), nenhum `Move()` dela falhou",
				  mina.NaEntrada == 1 && mina.Mina.Vivo, $"{mina.NaEntrada} relato(s) em 20 tiques, viva {mina.Mina.Vivo}");
		AfirmarPj("MINA: ...e quem SAI de cima e VOLTA esbarra de novo: outro encontro, outro sorteio (um so)",
				  mina.NaVolta == 1 && mina.Mina.Vivo, $"{mina.NaVolta} relato(s) na volta, viva {mina.Mina.Vivo}");

		// ---------------------------------------------------------------- (d) o raio
		var r = RaioContraQuemSempreRaspa();
		GD.Print($"[projetil]      (medido) RAIO: {r.NaPassagem} `Acertar` na passagem ({r.FeixesNaPassagem} feixe(s) na zona depois dela), "
				 + $"cabeca a {r.CabecaAlem / T:0.0} tiles alem do corpo, pericia +{r.Pericia:0.0}; {r.NaVolta} na volta ({r.NascidosNaVolta} feixe(s) nascido(s))");
		AfirmarPj("RAIO: a cabeca que leva o raspao e sorteada UMA vez (o `Bump` da cabeca, `objects.dm:358-363`)",
				  r.NaPassagem == 1, $"{r.NaPassagem} relato(s)");
		AfirmarPj("RAIO: ...e SEGUE VIAGEM: passa do corpo sem plantar, sem levar ninguem e sem dano (o ramo `WaveAttack` do `Move()` nao mata a cabeca, `objects.dm:100-113`)",
				  r.Raio.Vivo && !r.Raio.Encostado && r.Raio.Arrastando == 0 && r.CabecaAlem > 3 * T && Math.Abs(r.Vida) < 1e-9,
				  $"cabeca {r.CabecaAlem / T:0.0} tiles alem, encostado {r.Raio.Encostado}, levando {r.Raio.Arrastando}, vida {r.Vida:+0.###;-0.###;0}");
		AfirmarPj("RAIO: ...e o corpo que saiu da linha NAO corta o tronco que passa por ele (no DM ele esta um tile ao lado)",
				  r.FeixesNaPassagem == 1, $"{r.FeixesNaPassagem} feixe(s) na zona");
		AfirmarPj("RAIO: ...com o treino de UM encontro", Math.Abs(r.Pericia - 0.5) < 1e-9, $"pericia +{r.Pericia:0.0}");
		AfirmarPj("RAIO: quem SAI da linha e VOLTA a pisar no tronco abre outro encontro -- o corte do `Crossed` (`objects.dm:156-171`) e UM sorteio",
				  r.NaVolta == 1 && r.NascidosNaVolta == 1, $"{r.NaVolta} relato(s) na volta, {r.NascidosNaVolta} feixe(s) nascido(s) do corte");

		// ---------------------------------------------------------------- (d') o raio que ja vinha LEVANDO o corpo
		var l = RaioQueLevavaQuemRaspa();
		GD.Print($"[projetil]      (medido) RAIO QUE LEVAVA: {l.Raspoes} raspao(oes); o corpo andou {l.NoTiqueDoRaspao:0.00} px no tique do raspao e "
				 + $"{l.Depois:0.00} px nos seis seguintes; cabeca {l.CabecaAlem / T:0.0} tiles alem, {l.Feixes} feixe(s), levando {l.Levando}");
		AfirmarPj("PREPARO: o raio pegou o corpo e vinha LEVANDO-o, e dali em diante o raspao e certo",
				  l.Pegou && l.Chance / 2 >= 100, $"pegou {l.Pegou}, chance {l.Chance:0.#}%");
		AfirmarPj("RAIO QUE LEVAVA: o raspao do ciclo seguinte e UM sorteio e SOLTA o corpo -- quem saiu da linha nao e carregado",
				  l.Raspoes == 1 && l.Levando == 0 && l.Depois < 0.01f,
				  $"{l.Raspoes} raspao(oes), levando {l.Levando}, o corpo andou {l.Depois:0.00} px depois");
		AfirmarPj("RAIO QUE LEVAVA: ...solto no proprio sub-passo do raspao: no tique dele o corpo anda so o sub-passo em que a cabeca ainda o empurrava",
				  l.NoTiqueDoRaspao <= Projetil.RaioDeImpacto + 0.01f, $"{l.NoTiqueDoRaspao:0.00} px no tique do raspao");
		AfirmarPj("RAIO QUE LEVAVA: ...e a cabeca segue viagem, sem o corpo cortar o tronco que passa por ele",
				  l.CabecaAlem > 2 * T && l.Feixes == 1, $"cabeca {l.CabecaAlem / T:0.0} tiles alem, {l.Feixes} feixe(s)");

		// ---------------------------------------------------------------- (e) a proporcao, com a chance cravada
		AmostraDeRaspao a = ColherAmostraDeRaspao();
		GD.Print($"[projetil]      (medido) {a.Relato()}");
		AfirmarPj("PREPARO da amostra: todo tiro saiu com a chance cravada, contra um corpo de pe e com Ki pra aparar",
				  a.ForaDoPreparo == 0 && a.Outros == 0, $"{a.ForaDoPreparo} fora do preparo, {a.Outros} com fim inesperado");
		AfirmarPj($"AMOSTRA: nenhum dos {a.Tiros} tiros foi sorteado mais de uma vez",
				  a.MaisDeUm == 0 && a.Sorteios == a.Tiros, $"{a.MaisDeUm} tiro(s) com mais de um sorteio, {a.Sorteios} sorteios no total");
		AfirmarPj("AMOSTRA: quem raspa SAI ILESO -- nenhum tiro que deu raspao acertou ou foi defletido depois",
				  a.RasparamEApanharam == 0 && a.RasparamEDefletiram == 0 && a.Ilesos == a.Rasparam,
				  $"de {a.Rasparam} que rasparam: {a.RasparamEApanharam} apanharam, {a.RasparamEDefletiram} defletiram, {a.Ilesos} ilesos");
		AfirmarPj("AMOSTRA: a proporcao e a do DM -- 10% raspam, 18% defletem, 72% apanham (folga de seis desvios)",
				  a.NaProporcaoDoDm, a.Proporcao());

		// ---------------------------------------------------------------- (f) o defeito injetado
		// O JOGO DE ANTES: o raspao nao encerra nada. As quatro reguas de cima tem que ficar VERMELHAS com ele.
		Projetil.RaspaoSorteiaDeNovoDeTeste = true;
		try
		{
			m = BolaContraQuemSempreRaspa(teleguiada: false);
			AfirmarPj("(defeito injetado: o raspao sorteia de novo) BOLA: mais de um `Acertar` no mesmo corpo, e ela passa por ele viva",
					  m.Relatos > 1 && (m.Tiro.Pos - m.Forte.Pos).Length > T && m.Pericia > 0.5 + 1e-9,
					  $"{m.Relatos} relato(s), fim {m.Tiro.Fim} a {(m.Tiro.Pos - m.Forte.Pos).Length:0.0} px, pericia +{m.Pericia:0.0}");

			// A TELEGUIADA PASSA POR ELE COMO A BOLA desde a curva limitada (2026-10-09, `Core.Combat.Teleguiado`):
			// quem ela atravessou fica nas costas dela, fora do leque. Com o `walk_towards` de antes ela dava a
			// volta e ficava EM CIMA do alvo sorteando a cada sub-passo (60 relatos) -- a segunda linha repoe aquele
			// mundo, com os dois defeitos juntos, pra o numero continuar medido e nao so contado.
			m = BolaContraQuemSempreRaspa(teleguiada: true);
			AfirmarPj("(defeito injetado) TELEGUIADA: mais de um `Acertar` no mesmo corpo, e ela passa por ele viva, como a bola",
					  m.Relatos > 1 && (m.Tiro.Pos - m.Forte.Pos).Length > T,
					  $"{m.Relatos} relato(s), fim {m.Tiro.Fim} a {(m.Tiro.Pos - m.Forte.Pos).Length:0.0} px");

			Teleguiado.SemLimiteDeTeste = true;
			try
			{
				m = BolaContraQuemSempreRaspa(teleguiada: true);
				AfirmarPj("(os dois defeitos: o raspao sorteia de novo E o teleguiado vira sem limite) ela fica em cima do alvo sorteando a cada sub-passo",
						  m.Relatos > 10, $"{m.Relatos} relato(s)");
			}
			finally { Teleguiado.SemLimiteDeTeste = false; }

			mina = MinaContraQuemSempreRaspa();
			AfirmarPj("(defeito injetado) MINA: um sorteio por tique enquanto o corpo esta em cima dela",
					  mina.NaEntrada > 1, $"{mina.NaEntrada} relato(s) em 20 tiques");

			r = RaioContraQuemSempreRaspa();
			AfirmarPj("(defeito injetado) RAIO: a cabeca sorteia a cada sub-passo e o corpo 'esquivado' PICOTA o tronco",
					  r.NaPassagem > 1 && r.FeixesNaPassagem > 1, $"{r.NaPassagem} relato(s), {r.FeixesNaPassagem} feixe(s) na zona");

			l = RaioQueLevavaQuemRaspa();
			AfirmarPj("(defeito injetado) RAIO QUE LEVAVA: o corpo 'esquivado' continua sendo levado, e sorteado a cada sub-passo",
					  l.Raspoes > 1 && l.Depois > T, $"{l.Raspoes} raspao(oes), o corpo andou {l.Depois:0.0} px depois");

			a = ColherAmostraDeRaspao();
			AfirmarPj("(defeito injetado) AMOSTRA: quem raspa APANHA depois, e a proporcao sai do DM",
					  a.MaisDeUm > 0 && a.RasparamEApanharam > 0 && !a.NaProporcaoDoDm,
					  $"{a.MaisDeUm} tiro(s) com mais de um sorteio; {a.Proporcao()}");
		}
		finally { Projetil.RaspaoSorteiaDeNovoDeTeste = false; }

		LimparTudoDaBancada();
	}

	/// <summary>Os relatos de golpe que sairam pro fio enquanto o gesto rodava -- so os deste alvo, lidos como o cliente le.</summary>
	private static List<Protocol.HitEvent> RelatosNoAlvo(ServerPlayer alvo, Action gesto)
	{
		EscutaDeGolpes = [];
		try
		{
			gesto();
			return EscutaDeGolpes.Where(g => g.Cheio).Select(g => LerGolpe(g.Fio)).Where(h => h.Alvo == alvo.Id).ToList();
		}
		finally { EscutaDeGolpes = null; }
	}

	private static int RaspoesEm(List<Protocol.HitEvent> relatos) => relatos.Count(h => (Desfecho)h.Desfecho == Desfecho.Esquivou);

	/// <summary>A chance de deflexao que o `Acertar` sorteia pra este tiro neste corpo, em porcento. O raspao e a METADE dela.</summary>
	private static double ChanceDeDeflexaoDe(Projetil p, ServerPlayer alvo)
		=> DanoDeKi.ChanceDeDeflexao(alvo.Ficha, p.Bp, p.ModsAgora(), p.BaseDano, alvo.Combate!.Bloqueando, p.Fisico);

	/// <summary>
	/// CRAVA O RASPAO CERTO neste tiro contra este corpo: dano final ACIMA do corte (12) e chance de deflexao acima de
	/// 200%, as duas pela conta de producao.
	///
	/// ============================ POR QUE NO TIRO, E NAO SO NO PAR DE CORPOS ============================
	/// Ate o corte dos fracos (`DanoDeKi.CorteDoFraco`, `objects.dm:355-357`) bastava um fraco atirar num forte: a
	/// chance e a razao dos poderes. So que a mesma razao derruba o dano -- `chance% x dano final` e uma constante do
	/// DEFENSOR (`12 x max(kdef/10,1) / (Ekidef x log_4(max(kdef,4)))`) --, e um tiro com dano >= 10 tem chance de
	/// ~1%: o tiro do fraco no forte nem chega ao sorteio. O unico regime em que as duas sobem juntas e o PISO do
	/// `BpModulus` (0,01): abaixo de 1/40 do poder do alvo o multiplicador para de cair, e o dano passa a depender so
	/// do `mods`. Entao o tiro desta prova leva um `Bp` minusculo (um milionesimo do poder do alvo: a chance vai a
	/// milhares por cento) e um `ModsBase` escalado ate o dano final dar 12 (`CravarDanoFinal`).
	///
	/// E O CONTROLE QUE O BYOND MEDIU em 2026-10-08: BP 1 contra 1.000.000, `mods` 100, `basedamage` 10 -> dano final
	/// 120, deflexao "de 1000%", 200 raspoes em 200 bolas (e nenhum com o dano abaixo de 10).
	/// ====================================================================================================
	/// </summary>
	private static void CravarORaspaoCerto(Projetil p, ServerPlayer forte)
	{
		p.Bp = forte.Ficha.expressedBP / 1_000_000;
		CravarDanoFinal(p, forte, DanoQueFereDoCorte);
	}

	/// <summary>
	/// UMA BOLA (reta ou teleguiada) de um fraco contra um forte parado a seis tiles, ate ela acabar. Devolve quantos
	/// relatos sairam (cada um e um `Acertar`: com o raspao certo, nenhum outro desfecho acontece), o tiro, e o que
	/// mudou na pericia e na vida de quem apanhou.
	/// </summary>
	private (int Relatos, int Raspoes, Projetil Tiro, ServerPlayer Forte, double Pericia, double Vida, double Chance, double Dano)
		BolaContraQuemSempreRaspa(bool teleguiada)
	{
		LimparTudoDaBancada();
		Vec2 chao = CorredorLivre(24);
		ServerPlayer fraco = Forjar("Fraco", chao, bp: BpDoFracoDoRaspao);
		fraco.Facing = Facing.East;
		ServerPlayer forte = Forjar("Forte", chao + new Vec2(6 * ZoneCollision.TileSize, 0), bp: BpDoForteDoRaspao);
		double periciaAntes = forte.Ficha.kidefenseskill, vidaAntes = forte.Combate!.Corpo.Vida();

		Projetil p = Disparar(fraco, new ReceitaDeProjetil
		{
			Tipo = teleguiada ? TipoDeProjetil.Guided : TipoDeProjetil.Blast,
			BaseDano = 1, Velocidade = 1, AlcanceTiles = 20, Nome = "bola do raspao",
		});
		if (teleguiada) p.Alvo = forte.Id;
		CravarORaspaoCerto(p, forte);
		double chance = ChanceDeDeflexaoDe(p, forte), dano = DanoFinalDe(p, forte);

		List<Protocol.HitEvent> relatos = RelatosNoAlvo(forte, () =>
		{
			for (int i = 0; i < 30 * 8 && p.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		});
		return (relatos.Count, RaspoesEm(relatos), p, forte, forte.Ficha.kidefenseskill - periciaAntes,
				forte.Combate.Corpo.Vida() - vidaAntes, chance, dano);
	}

	/// <summary>
	/// UMA BOLA PARADA (rumo nulo, como as do `Ki_Bomb`) a oito pixels de um forte: vinte tiques com ele em cima
	/// dela, depois ele sai tres tiles por dois tiques, e volta por mais vinte. Devolve os relatos de cada metade.
	/// </summary>
	private (int NaEntrada, int NaVolta, Projetil Mina) MinaContraQuemSempreRaspa()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		Vec2 chao = CorredorLivre(24);
		ServerPlayer fraco = Forjar("FracoDaMina", chao, bp: BpDoFracoDoRaspao);
		ServerPlayer forte = Forjar("ForteNaMina", chao + new Vec2(6 * T, 0), bp: BpDoForteDoRaspao);
		Vec2 lugar = forte.Pos;

		Projetil mina = Disparar(fraco, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, AlcanceTiles = 20, Nome = "mina do raspao",
		}, rumoDado: Vec2.Zero, deOnde: lugar + new Vec2(8, 0));
		CravarORaspaoCerto(mina, forte);

		int naEntrada = RelatosNoAlvo(forte, () =>
		{
			for (int i = 0; i < 20 && mina.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		}).Count;

		forte.Pos = lugar + new Vec2(0, 3 * T);
		for (int i = 0; i < 2 && mina.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		forte.Pos = lugar;

		int naVolta = RelatosNoAlvo(forte, () =>
		{
			for (int i = 0; i < 20 && mina.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		}).Count;
		return (naEntrada, naVolta, mina);
	}

	/// <summary>
	/// UM RAIO CANALIZADO de um fraco contra um forte parado a seis tiles: dezoito tiques (a cabeca chega, raspa e
	/// tem tempo de andar doze tiles), depois o forte sai quatro tiles de lado por dois tiques e volta a pisar no
	/// mesmo lugar -- agora em cima do TRONCO -- por mais seis.
	/// </summary>
	private (int NaPassagem, int FeixesNaPassagem, float CabecaAlem, double Pericia, double Vida, int NaVolta, int NascidosNaVolta, Projetil Raio)
		RaioContraQuemSempreRaspa()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		Vec2 raia = CorredorSeco(30);
		ServerPlayer fraco = Forjar("FracoDoRaio", raia, bp: BpDoFracoDoRaspao);
		fraco.Facing = Facing.East;
		ServerPlayer forte = Forjar("ForteNoRaio", raia + new Vec2(6 * T, 0), bp: BpDoForteDoRaspao);
		Vec2 lugar = forte.Pos;
		double periciaAntes = forte.Ficha.kidefenseskill, vidaAntes = forte.Combate!.Corpo.Vida();

		// `Canalizando` na unha, como o `RaioDaBancada`: sem o bit um beam nascido pelo `Disparar` morre de `Cessou`
		// no primeiro tique. A receita e a dele, MENOS o `Deflectivel = false`.
		Projetil raio = Disparar(fraco, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 40, Nome = "Onda de Ki",
		});
		raio.Canalizando = true;
		CravarORaspaoCerto(raio, forte);
		List<Projetil> lista = ProjeteisDaZona(fraco.Zone.Hash);

		int naPassagem = RelatosNoAlvo(forte, () =>
		{
			for (int i = 0; i < 18; i++) UmTiqueDeArrasto();
		}).Count;
		int feixesNaPassagem = lista.Count;
		float cabecaAlem = raio.Pos.X - lugar.X;
		double pericia = forte.Ficha.kidefenseskill - periciaAntes, vida = forte.Combate.Corpo.Vida() - vidaAntes;

		forte.Pos = lugar + new Vec2(0, 4 * T);
		UmTiqueDeArrasto();
		UmTiqueDeArrasto();
		forte.Pos = lugar;
		int feixesAntesDaVolta = lista.Count;
		int naVolta = RelatosNoAlvo(forte, () =>
		{
			for (int i = 0; i < 6; i++) UmTiqueDeArrasto();
		}).Count;
		return (naPassagem, feixesNaPassagem, cabecaAlem, pericia, vida, naVolta, lista.Count - feixesAntesDaVolta, raio);
	}

	/// <summary>
	/// UM RAIO QUE JA VINHA LEVANDO O CORPO (o primeiro impacto acerta, com o dado desligado, e o arrasto comeca) e
	/// RASPA no ciclo de moer seguinte: o dado e religado no proprio tiro, que ja saiu com o raspao certo cravado
	/// (`CravarORaspaoCerto`: 12 por batida, e a chance acima de 200%). Devolve os raspoes, quanto o corpo andou NO
	/// TIQUE do raspao e DEPOIS dele (seis tiques), onde a cabeca foi parar e quantos feixes ha na zona.
	/// </summary>
	private (bool Pegou, double Chance, int Raspoes, float NoTiqueDoRaspao, float Depois, float CabecaAlem, int Feixes, int Levando)
		RaioQueLevavaQuemRaspa()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		Vec2 raia = CorredorSeco(30);
		ServerPlayer atira = Forjar("FeixeQueLeva", raia, bp: 200_000);
		atira.Facing = Facing.East;
		ServerPlayer levado = Forjar("LevadoQueRaspa", raia + new Vec2(6 * T, 0), bp: 200_000);

		Projetil raio = RaioDaBancada(atira, baseDano: 1);
		CravarORaspaoCerto(raio, levado);
		for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();
		bool pegou = raio.Arrastando == levado.Id;

		raio.Deflectivel = true;
		double chance = ChanceDeDeflexaoDe(raio, levado);

		int Contar() => EscutaDeGolpes!.Where(g => g.Cheio).Select(g => LerGolpe(g.Fio))
										.Count(h => h.Alvo == levado.Id && (Desfecho)h.Desfecho == Desfecho.Esquivou);
		EscutaDeGolpes = [];
		try
		{
			float noTique = 0;
			Vec2 ondeRaspou = levado.Pos;
			for (int i = 0; i < 12 && Contar() == 0; i++)
			{
				Vec2 antes = levado.Pos;
				UmTiqueDeArrasto();
				noTique = (levado.Pos - antes).Length;
				ondeRaspou = levado.Pos;
			}
			for (int i = 0; i < 6; i++) UmTiqueDeArrasto();
			return (pegou, chance, Contar(), noTique, (levado.Pos - ondeRaspou).Length, raio.Pos.X - levado.Pos.X,
					ProjeteisDaZona(atira.Zone.Hash).Count, raio.Arrastando);
		}
		finally { EscutaDeGolpes = null; }
	}

	/// <summary>O que a amostra contou. `Rasparam` = tiros cujo PRIMEIRO sorteio deu raspao; `Ilesos` = os que acabaram sem acertar nem ser defletidos.</summary>
	private readonly record struct AmostraDeRaspao(
		int Tiros, int Ilesos, int Defletidos, int Acertaram, int Outros, int ForaDoPreparo, int Sorteios, int MaisDeUm,
		int Rasparam, int RasparamEApanharam, int RasparamEDefletiram, int SorteiosDosQueRasparam, long Ms)
	{
		private static string Pc(int n, int de) => de == 0 ? "-" : $"{100.0 * n / de:0.00}%";

		/// <summary>
		/// 10% / 18% / 72%, com folga de seis desvios-padrao (2,0 / 2,6 / 3,0 pontos em oito mil tiros): o dado e o de
		/// producao, sem semente, e uma regua justa viraria moeda.
		/// </summary>
		public bool NaProporcaoDoDm
			=> Math.Abs(100.0 * Ilesos / Tiros - 10) <= 2.0 && Math.Abs(100.0 * Defletidos / Tiros - 18) <= 2.6
			   && Math.Abs(100.0 * Acertaram / Tiros - 72) <= 3.0;

		public string Proporcao()
			=> $"saem ilesos de raspao {Ilesos} ({Pc(Ilesos, Tiros)}), defletem {Defletidos} ({Pc(Defletidos, Tiros)}), apanham {Acertaram} ({Pc(Acertaram, Tiros)})";

		public string Relato()
			=> $"{Tiros} bolas com a chance de deflexao cravada em {ChanceCravadaDoRaspao:0}% ({Ms} ms): {Proporcao()}. "
			   + $"Sorteios por tiro: {(double)Sorteios / Tiros:0.000} em media, {MaisDeUm} tiro(s) com mais de um. "
			   + $"O 1o sorteio deu RASPAO em {Rasparam} ({Pc(Rasparam, Tiros)}): {Rasparam - RasparamEApanharam - RasparamEDefletiram} sairam ilesos, "
			   + $"{RasparamEApanharam} APANHARAM depois, {RasparamEDefletiram} defletiram depois "
			   + $"({(Rasparam == 0 ? 0 : (double)SorteiosDosQueRasparam / Rasparam):0.00} sorteios cada)";
	}

	/// <summary>
	/// A PROPORCAO DOS TRES DESFECHOS com a chance de deflexao CRAVADA em 20% -- no DM, 10% dos tiros raspam (e
	/// acabam ali, sem dano), 18% sao defletidos (`prob(deflectchance)` nos 90% que sobram) e 72% acertam.
	///
	/// A CHANCE E CRAVADA NO TIRO (o `Bp` dele e acertado ate a conta de producao dar 20,000%), pela mesma razao
	/// por que as outras bancadas viram o `Deflectivel` no projetil: a receita e a conta sao as de producao, e o
	/// unico lugar que a bancada alcanca sem mexer nelas e o tiro desta prova. O corpo que apanha e curado e volta
	/// a pericia de antes a cada tiro -- senao a chance (e a vida) do tiro mil nao seria a do tiro um.
	/// </summary>
	private AmostraDeRaspao ColherAmostraDeRaspao()
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		Vec2 chao = CorredorLivre(24);
		ServerPlayer atira = Forjar("Amostrador", chao, bp: 5_000);
		atira.Facing = Facing.East;
		// A SETE TILES: a bola chega com seis de viagem, alem dos quatro em que ela arremessa (`FatorDeEmpurrao`) --
		// um corpo arremessado sairia do lugar entre um tiro e outro.
		ServerPlayer alvo = Forjar("Amostra", chao + new Vec2(7 * T, 0), bp: 100_000);
		double periciaDeBase = alvo.Ficha.kidefenseskill;

		int ilesos = 0, defletidos = 0, acertaram = 0, outros = 0, foraDoPreparo = 0, sorteios = 0, maisDeUm = 0;
		int rasparam = 0, rasparamEApanharam = 0, rasparamEDefletiram = 0, sorteiosDosQueRasparam = 0;
		var relogio = System.Diagnostics.Stopwatch.StartNew();

		EscutaDeGolpes = [];
		try
		{
			for (int n = 0; n < TirosDaAmostraDeRaspao; n++)
			{
				alvo.Combate!.Curar();
				alvo.Combate.Stun = 0;
				alvo.Ficha.Ki = alvo.Ficha.MaxKi;
				alvo.Ficha.kidefenseskill = periciaDeBase;

				Projetil p = Disparar(atira, new ReceitaDeProjetil
				{
					Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 20, Nome = "bola da amostra",
				});
				// O DANO ACIMA DO CORTE E A CHANCE EM 20%, os dois no tiro (ver `CravarORaspaoCerto`): no piso do
				// `BpModulus` o dano nao depende do `Bp`, entao crava-se o dano primeiro e a chance depois, so no `Bp`.
				CravarORaspaoCerto(p, alvo);
				p.Bp *= ChanceDeDeflexaoDe(p, alvo) / ChanceCravadaDoRaspao;
				if (Math.Abs(ChanceDeDeflexaoDe(p, alvo) - ChanceCravadaDoRaspao) > 1e-6 || DanoFinalDe(p, alvo) < DanoDeKi.CorteDoFraco
					|| alvo.Ficha.KO || alvo.Ficha.Ki < 5) foraDoPreparo++;

				EscutaDeGolpes.Clear();
				for (int i = 0; i < 30 * 8 && p.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);

				int raspoes = 0;
				foreach ((bool cheio, byte[] fio) in EscutaDeGolpes)
				{
					if (!cheio) continue;
					Protocol.HitEvent h = LerGolpe(fio);
					if (h.Alvo == alvo.Id && (Desfecho)h.Desfecho == Desfecho.Esquivou) raspoes++;
				}

				// CADA `Acertar` TERMINA EM UM DE TRES: raspao (um relato `Esquivou`), deflexao (o tiro morre `Defletido`)
				// ou dano (o tiro morre `Acertou`). Entao os sorteios deste tiro sao os raspoes mais o desfecho que o matou.
				int deste = raspoes + (p.Fim is FimDeProjetil.Acertou or FimDeProjetil.Defletido ? 1 : 0);
				sorteios += deste;
				if (deste > 1) maisDeUm++;

				switch (p.Fim)
				{
					case FimDeProjetil.Acertou: acertaram++; break;
					case FimDeProjetil.Defletido: defletidos++; break;
					case FimDeProjetil.Apagou when raspoes > 0: ilesos++; break;
					default: outros++; break;
				}
				if (raspoes == 0) continue;

				rasparam++;
				sorteiosDosQueRasparam += deste;
				if (p.Fim == FimDeProjetil.Acertou) rasparamEApanharam++;
				else if (p.Fim == FimDeProjetil.Defletido) rasparamEDefletiram++;
			}
		}
		finally { EscutaDeGolpes = null; }

		return new AmostraDeRaspao(TirosDaAmostraDeRaspao, ilesos, defletidos, acertaram, outros, foraDoPreparo, sorteios, maisDeUm,
								   rasparam, rasparamEApanharam, rasparamEDefletiram, sorteiosDosQueRasparam, relogio.ElapsedMilliseconds);
	}
}
