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
/// assunto. Entao ele entra de dois jeitos que nao sao moeda, os dois CRAVADOS NO TIRO (<see cref="CravarAChance"/>:
/// a conta da chance e a de producao, e o `ModsBase` do tiro desta prova e escalado ate ela dar o numero pedido):
///   * CERTO: a chance de deflexao passa de 200% e o `prob(deflectchance/2)` sai SEMPRE. A cena conta relatos, e a
///     conta e exata.
///   * CRAVADO EM 20%: oito mil tiros por degrau, e a proporcao dos tres desfechos comparada com a que a escada
///     manda, com folga de seis desvios.
///
/// ============================ O SORTEIO, NA ESCADA DE 2026-10-12 (`KiContraCorpo`) ============================
/// QUEM E SORTEADO mudou de dono. No DM (e aqui ate aquele dia) o corte era por DANO -- o impacto de dano final
/// abaixo de 10 nem chegava ao `prob` (`objects.dm:355-357`) --, e o raspao certo so cabia no piso do `BpModulus`
/// (um tiro pesado de alguem mais de quarenta vezes mais fraco). Hoje quem nao e sorteado e quem e PAREDE pro tiro
/// (quatro vezes o poder dele): contra todo o resto o raspao e sorteado, por mais fraco que seja o golpe. E a
/// DEFLEXAO cheia (o segundo `prob`, `:364`) pede o DOBRO do poder do tiro (`KiContraCorpo.PodeDefletir`). Entao:
///   * as cenas do raspao certo rodam entre IGUAIS -- o primeiro degrau que apanha e fica --, com a chance cravada;
///   * a amostra roda dos dois lados da porta do deflect: logo abaixo do dobro sai 10% de raspao e NENHUMA
///     deflexao; logo acima, os 10% / 18% / 72% do DM;
///   * quem e LEVADO sem defender esta atordoado, e atordoado nao raspa (`if(M.KO||M.stagger) deflectchance=0`,
///     `objects.dm:345`): o raio o leva ate o fim. Quem e levado de GUARDA raspa, e e solto ali.
/// O jogo de antes (o corte por dano) fica no fim, como o defeito injetado.
/// ================================================================================================================
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
	/// A chance de deflexao do RASPAO CERTO, em porcento: a metade dela (o raspao) passa de 100 com folga, e o
	/// `prob` acima de 100 sai sempre. (No BYOND, medido em 2026-10-08 com a deflexao "de 1000%": 200 raspoes em 200 bolas.)
	/// </summary>
	private const double ChanceDoRaspaoCerto = 1000;

	/// <summary>A chance cravada da amostra, em porcento: 10% de raspao, e dos outros 90%, 20% de deflexao -- pra quem pode defletir.</summary>
	private const double ChanceCravadaDoRaspao = 20;

	/// <summary>Quantos tiros cada amostra da. Oito mil: o desvio-padrao das tres faixas fica entre 0,34 e 0,50 ponto.</summary>
	private const int TirosDaAmostraDeRaspao = 8000;

	/// <summary>
	/// Os dois degraus da amostra, em vezes o poder do tiro: logo ABAIXO e logo ACIMA da porta do deflect
	/// (`KiContraCorpo.VezesPraDefletir`, o dobro). Os dois apanham e ficam (nenhum e parede, nenhum e levado).
	/// </summary>
	private const double VezesLogoAbaixoDoDeflect = 1.9, VezesLogoAcimaDoDeflect = 2.1;

	private void UmEncontroUmSorteio()
	{
		GD.Print("[projetil] -- 15) UM ENCONTRO, UM SORTEIO: o raspao (`objects.dm:358-363`) encerra o encontro do tiro com o corpo");
		const int T = ZoneCollision.TileSize;
		// DUAS FAIXAS DE CHAO PRA FAMILIA INTEIRA (cada cena limpa a bancada e nasce de novo nelas): as bolas no
		// corredor livre, os raios no seco.
		Vec2 chao = CorredorLivre(24);
		Vec2 raia = CorredorSeco(30);

		// ---------------------------------------------------------------- (a) a bola reta
		var m = BolaContraQuemSempreRaspa(chao, teleguiada: false);
		AfirmarPj("PREPARO: entre IGUAIS o raspao e CERTO -- o corpo nao e parede pro tiro (menos de quatro vezes o poder dele) e o `prob(deflectchance/2)` sai com a chance acima de 200%",
				  m.Chance / 2 >= 100 && !KiContraCorpo.EhParede(m.Razao) && Math.Abs(m.Razao - 1) < 0.01,
				  $"chance {m.Chance:0.#}%, corpo com {m.Razao:0.###}x o poder do tiro, dano final {m.Dano:0.####}");
		GD.Print($"[projetil]      (medido) BOLA a {1 / m.Tiro.SegundosPorTile:0.#} tiles/s: {m.Relatos} `Acertar`, {m.Raspoes} raspao(oes), fim {m.Tiro.Fim} "
				 + $"a {(m.Tiro.Pos - m.Corpo.Pos).Length / T:0.0} tiles do corpo, pericia +{m.Pericia:0.0}");
		AfirmarPj("BOLA: o tiro que leva o raspao e sorteado UMA vez (um `Bump`, um `prob`)",
				  m.Relatos == 1 && m.Raspoes == 1, $"{m.Relatos} relato(s), {m.Raspoes} raspao(oes)");
		AfirmarPj("BOLA: ...e ACABA ali, sem estouro e sem dano -- o `Move()` do DM tira do mundo a bola cujo passo falhou (`objects.dm:92-99`)",
				  !m.Tiro.Vivo && m.Tiro.Fim == FimDeProjetil.Apagou
				  && (m.Tiro.Pos - m.Corpo.Pos).Length <= Projetil.RaioDeImpacto + 0.5f && Math.Abs(m.Vida) < 1e-9,
				  $"fim {m.Tiro.Fim} a {(m.Tiro.Pos - m.Corpo.Pos).Length:0.0} px do corpo, vida {m.Vida:+0.###;-0.###;0}");
		AfirmarPj("BOLA: ...e o treino e o de UM encontro: 0,1 de apanhar mais 0,4 do raspao (`objects.dm:313` e `:359`)",
				  Math.Abs(m.Pericia - 0.5) < 1e-9, $"pericia +{m.Pericia:0.0}");

		// ---------------------------------------------------------------- (a') na velocidade do DM
		// O NUMERO DE SUB-PASSOS DENTRO DO RAIO DEPENDE DA VELOCIDADE; a regra nao pode depender.
		Projetil.VelocidadeDoDmDeTeste = true;
		try
		{
			m = BolaContraQuemSempreRaspa(chao, teleguiada: false);
			GD.Print($"[projetil]      (medido) BOLA na velocidade do DM ({1 / m.Tiro.SegundosPorTile:0.#} tiles/s): {m.Relatos} `Acertar`, "
					 + $"{m.Raspoes} raspao(oes), fim {m.Tiro.Fim}, pericia +{m.Pericia:0.0}");
			AfirmarPj("BOLA na velocidade do DM (3,3 tiles/s): um sorteio tambem -- a regra nao depende de quantos sub-passos cabem no raio",
					  m.Relatos == 1 && m.Raspoes == 1 && !m.Tiro.Vivo && Math.Abs(m.Pericia - 0.5) < 1e-9,
					  $"{m.Relatos} relato(s), {m.Raspoes} raspao(oes), pericia +{m.Pericia:0.0}");
		}
		finally { Projetil.VelocidadeDoDmDeTeste = false; }

		// ---------------------------------------------------------------- (b) a teleguiada
		m = BolaContraQuemSempreRaspa(chao, teleguiada: true);
		GD.Print($"[projetil]      (medido) TELEGUIADA: {m.Relatos} `Acertar`, {m.Raspoes} raspao(oes), fim {m.Tiro.Fim} "
				 + $"a {(m.Tiro.Pos - m.Corpo.Pos).Length / T:0.0} tiles do corpo, pericia +{m.Pericia:0.0}");
		AfirmarPj("TELEGUIADA: um sorteio, e ela acaba no raspao como a bola (e o mesmo `Move()`: `walk_towards` nao a salva)",
				  m.Relatos == 1 && m.Raspoes == 1 && !m.Tiro.Vivo && m.Tiro.Fim == FimDeProjetil.Apagou
				  && (m.Tiro.Pos - m.Corpo.Pos).Length <= Projetil.RaioDeImpacto + 0.5f && Math.Abs(m.Pericia - 0.5) < 1e-9,
				  $"{m.Relatos} relato(s), {m.Raspoes} raspao(oes), fim {m.Tiro.Fim} a {(m.Tiro.Pos - m.Corpo.Pos).Length:0.0} px, pericia +{m.Pericia:0.0}");

		// ---------------------------------------------------------------- (c) a bola parada (a mina)
		var mina = MinaContraQuemSempreRaspa(chao);
		GD.Print($"[projetil]      (medido) MINA: {mina.NaEntrada} `Acertar` em 20 tiques com o corpo em cima dela, "
				 + $"{mina.NaVolta} depois de ele sair e voltar; viva {mina.Mina.Vivo}");
		AfirmarPj("MINA (bola parada): com o corpo em cima dela o raspao e sorteado UMA vez, e ela FICA -- quem esbarrou foi o corpo (`mobBump.dm:57-58`), nenhum `Move()` dela falhou",
				  mina.NaEntrada == 1 && mina.Mina.Vivo, $"{mina.NaEntrada} relato(s) em 20 tiques, viva {mina.Mina.Vivo}");
		AfirmarPj("MINA: ...e quem SAI de cima e VOLTA esbarra de novo: outro encontro, outro sorteio (um so)",
				  mina.NaVolta == 1 && mina.Mina.Vivo, $"{mina.NaVolta} relato(s) na volta, viva {mina.Mina.Vivo}");

		// ---------------------------------------------------------------- (d) o raio
		var r = RaioContraQuemSempreRaspa(raia);
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
		var l = RaioQueLevavaQuemRaspa(raia, deGuarda: true);
		GD.Print($"[projetil]      (medido) RAIO QUE LEVAVA (o mais fraco, de guarda): {l.Raspoes} raspao(oes); o corpo andou {l.NoTiqueDoRaspao:0.00} px no tique do raspao e "
				 + $"{l.Depois:0.00} px nos doze seguintes; cabeca {l.CabecaAlem / T:0.0} tiles alem, {l.Feixes} feixe(s), levando {l.Levando}");
		AfirmarPj("PREPARO: o raio pegou o mais fraco DE GUARDA e vinha LEVANDO-o, sem atordoar (quem defende nao e atordoado), e dali em diante o raspao e certo",
				  l.Pegou && l.Stun <= 0 && l.Chance / 2 >= 100, $"pegou {l.Pegou}, stun {l.Stun:0.##}, chance {l.Chance:0.#}%");
		AfirmarPj("RAIO QUE LEVAVA: o raspao do ciclo seguinte e UM sorteio e SOLTA o corpo -- quem saiu da linha nao e carregado",
				  l.Raspoes == 1 && l.Levando == 0 && l.Depois < 0.01f,
				  $"{l.Raspoes} raspao(oes), levando {l.Levando}, o corpo andou {l.Depois:0.00} px depois");
		AfirmarPj("RAIO QUE LEVAVA: ...solto no proprio sub-passo do raspao: no tique dele o corpo anda so o sub-passo em que a cabeca ainda o empurrava",
				  l.NoTiqueDoRaspao <= Projetil.RaioDeImpacto + 0.01f, $"{l.NoTiqueDoRaspao:0.00} px no tique do raspao");
		AfirmarPj("RAIO QUE LEVAVA: ...e a cabeca segue viagem, sem o corpo cortar o tronco que passa por ele",
				  l.CabecaAlem > 2 * T && l.Feixes == 1, $"cabeca {l.CabecaAlem / T:0.0} tiles alem, {l.Feixes} feixe(s)");

		// ---------------------------------------------------------------- (d'') quem e levado SEM defender nao raspa
		// O MESMO RAIO, O MESMO DADO CERTO, SEM A GUARDA: o mais fraco esta atordoado enquanto apanha (`KiContraCorpo.Atordoa`),
		// e o `Acertar` zera a chance de quem esta atordoado -- `if(M.KO||M.stagger) deflectchance=0` (`objects.dm:345`).
		var a = RaioQueLevavaQuemRaspa(raia, deGuarda: false);
		GD.Print($"[projetil]      (medido) RAIO QUE LEVAVA (o mais fraco, SEM guarda): {a.Raspoes} raspao(oes) com a chance da conta em {a.Chance:0.#}%; "
				 + $"stun {a.Stun:0.##} s, levando {a.Levando}, o corpo andou {a.Depois:0.0} px nos doze tiques de depois");
		AfirmarPj("QUEM E LEVADO SEM DEFENDER NAO RASPA: atordoado, a chance dele e zero -- o raio continua levando, por mais certo que o dado estivesse",
				  a.Pegou && a.Stun > 0 && a.Chance / 2 >= 100 && a.Raspoes == 0 && a.Levando != 0 && a.Depois > T,
				  $"pegou {a.Pegou}, stun {a.Stun:0.##}, chance da conta {a.Chance:0.#}%, {a.Raspoes} raspao(oes), levando {a.Levando}, andou {a.Depois:0.0} px");

		// ---------------------------------------------------------------- (e) a proporcao, dos dois lados da porta do deflect
		AmostraDeRaspao abaixo = ColherAmostraDeRaspao(chao, VezesLogoAbaixoDoDeflect);
		GD.Print($"[projetil]      (medido) {abaixo.Relato()}");
		AfirmarPj("PREPARO da amostra de baixo: todo tiro saiu com a chance cravada, contra um corpo de pe, com Ki pra aparar e logo ABAIXO do dobro do poder do tiro",
				  abaixo.ForaDoPreparo == 0 && abaixo.Outros == 0 && abaixo.Vezes < KiContraCorpo.VezesPraDefletir && abaixo.Vezes >= 1,
				  $"{abaixo.ForaDoPreparo} fora do preparo, {abaixo.Outros} com fim inesperado, {abaixo.Vezes:0.###}x o poder do tiro");
		AfirmarPj($"AMOSTRA: nenhum dos {abaixo.Tiros} tiros foi sorteado mais de uma vez",
				  abaixo.MaisDeUm == 0 && abaixo.Sorteios == abaixo.Tiros, $"{abaixo.MaisDeUm} tiro(s) com mais de um sorteio, {abaixo.Sorteios} sorteios no total");
		AfirmarPj("AMOSTRA: quem raspa SAI ILESO -- nenhum tiro que deu raspao acertou ou foi defletido depois",
				  abaixo.RasparamEApanharam == 0 && abaixo.RasparamEDefletiram == 0 && abaixo.Ilesos == abaixo.Rasparam,
				  $"de {abaixo.Rasparam} que rasparam: {abaixo.RasparamEApanharam} apanharam, {abaixo.RasparamEDefletiram} defletiram, {abaixo.Ilesos} ilesos");
		AfirmarPj("AMOSTRA ABAIXO DO DOBRO: 10% raspam, NENHUM deflete e 90% apanham -- a deflexao pede o dobro do poder do tiro (folga de seis desvios)",
				  abaixo.NaProporcao(10, 0, 90), abaixo.Proporcao());

		AmostraDeRaspao acima = ColherAmostraDeRaspao(chao, VezesLogoAcimaDoDeflect);
		GD.Print($"[projetil]      (medido) {acima.Relato()}");
		AfirmarPj("PREPARO da amostra de cima: a mesma cena, com o corpo logo ACIMA do dobro -- e ainda longe de ser parede",
				  acima.ForaDoPreparo == 0 && acima.Outros == 0 && acima.Vezes >= KiContraCorpo.VezesPraDefletir && !KiContraCorpo.EhParede(acima.Vezes),
				  $"{acima.ForaDoPreparo} fora do preparo, {acima.Outros} com fim inesperado, {acima.Vezes:0.###}x o poder do tiro");
		AfirmarPj("AMOSTRA ACIMA DO DOBRO: a proporcao e a do DM -- 10% raspam, 18% defletem, 72% apanham (folga de seis desvios), um sorteio por tiro",
				  acima.NaProporcao(10, 18, 72) && acima.MaisDeUm == 0 && acima.RasparamEApanharam == 0 && acima.RasparamEDefletiram == 0,
				  $"{acima.Proporcao()}; {acima.MaisDeUm} tiro(s) com mais de um sorteio");

		// ---------------------------------------------------------------- (f) o defeito injetado
		// O JOGO DE ANTES DE 2026-10-08: o raspao nao encerra nada. As reguas de cima tem que ficar VERMELHAS com ele.
		Projetil.RaspaoSorteiaDeNovoDeTeste = true;
		try
		{
			m = BolaContraQuemSempreRaspa(chao, teleguiada: false);
			AfirmarPj("(defeito injetado: o raspao sorteia de novo) BOLA: mais de um `Acertar` no mesmo corpo, e ela passa por ele viva",
					  m.Relatos > 1 && (m.Tiro.Pos - m.Corpo.Pos).Length > T && m.Pericia > 0.5 + 1e-9,
					  $"{m.Relatos} relato(s), fim {m.Tiro.Fim} a {(m.Tiro.Pos - m.Corpo.Pos).Length:0.0} px, pericia +{m.Pericia:0.0}");

			// A TELEGUIADA PASSA POR ELE COMO A BOLA desde a curva limitada (2026-10-09, `Core.Combat.Teleguiado`):
			// quem ela atravessou fica nas costas dela, fora do leque. Com o `walk_towards` de antes ela dava a
			// volta e ficava EM CIMA do alvo sorteando a cada sub-passo (60 relatos) -- a segunda linha repoe aquele
			// mundo, com os dois defeitos juntos, pra o numero continuar medido e nao so contado.
			m = BolaContraQuemSempreRaspa(chao, teleguiada: true);
			AfirmarPj("(defeito injetado) TELEGUIADA: mais de um `Acertar` no mesmo corpo, e ela passa por ele viva, como a bola",
					  m.Relatos > 1 && (m.Tiro.Pos - m.Corpo.Pos).Length > T,
					  $"{m.Relatos} relato(s), fim {m.Tiro.Fim} a {(m.Tiro.Pos - m.Corpo.Pos).Length:0.0} px");

			Teleguiado.SemLimiteDeTeste = true;
			try
			{
				m = BolaContraQuemSempreRaspa(chao, teleguiada: true);
				AfirmarPj("(os dois defeitos: o raspao sorteia de novo E o teleguiado vira sem limite) ela fica em cima do alvo sorteando a cada sub-passo",
						  m.Relatos > 10, $"{m.Relatos} relato(s)");
			}
			finally { Teleguiado.SemLimiteDeTeste = false; }

			mina = MinaContraQuemSempreRaspa(chao);
			AfirmarPj("(defeito injetado) MINA: um sorteio por tique enquanto o corpo esta em cima dela",
					  mina.NaEntrada > 1, $"{mina.NaEntrada} relato(s) em 20 tiques");

			r = RaioContraQuemSempreRaspa(raia);
			AfirmarPj("(defeito injetado) RAIO: a cabeca sorteia a cada sub-passo e o corpo 'esquivado' PICOTA o tronco",
					  r.NaPassagem > 1 && r.FeixesNaPassagem > 1, $"{r.NaPassagem} relato(s), {r.FeixesNaPassagem} feixe(s) na zona");

			l = RaioQueLevavaQuemRaspa(raia, deGuarda: true);
			AfirmarPj("(defeito injetado) RAIO QUE LEVAVA: o corpo 'esquivado' continua sendo levado, e sorteado a cada sub-passo",
					  l.Raspoes > 1 && l.Depois > T, $"{l.Raspoes} raspao(oes), o corpo andou {l.Depois:0.0} px depois");

			acima = ColherAmostraDeRaspao(chao, VezesLogoAcimaDoDeflect);
			AfirmarPj("(defeito injetado) AMOSTRA: quem raspa APANHA depois, e a proporcao sai do DM",
					  acima.MaisDeUm > 0 && acima.RasparamEApanharam > 0 && !acima.NaProporcao(10, 18, 72),
					  $"{acima.MaisDeUm} tiro(s) com mais de um sorteio; {acima.Proporcao()}");
		}
		finally { Projetil.RaspaoSorteiaDeNovoDeTeste = false; }

		// ---------------------------------------------------------------- (g) o jogo de antes de 2026-10-12
		// O CORTE POR DANO, de volta: o tiro do raspao certo -- um golpe de centesimos, com a chance la em cima -- nem
		// chega ao sorteio. Era por isso que o raspao certo so cabia no piso do `BpModulus`; hoje quem nao e sorteado
		// e quem e parede, e a regua "um raspao, e a bola acaba ali" tem de ficar vermelha com o botao ligado.
		KiContraCorpo.RegraDeAntesDeTeste = true;
		try
		{
			m = BolaContraQuemSempreRaspa(chao, teleguiada: false);
			AfirmarPj("(defeito injetado: o jogo de antes, o corte por dano) o MESMO tiro do raspao certo nem e sorteado: estoura no corpo sem relato nenhum, so com o treino de encostar (0,1)",
					  m.Chance / 2 >= 100 && DanoDeKi.FracoDemais(m.Dano) && m.Relatos == 0 && m.Tiro.Fim == FimDeProjetil.Acertou
					  && Math.Abs(m.Pericia - 0.1) < 1e-9 && Math.Abs(m.Vida) < 1e-9,
					  $"chance {m.Chance:0.#}%, dano final {m.Dano:0.####}, {m.Relatos} relato(s), fim {m.Tiro.Fim}, pericia +{m.Pericia:0.0}");
		}
		finally { KiContraCorpo.RegraDeAntesDeTeste = false; }

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

	/// <summary>Quantas vezes o poder deste tiro o corpo tem -- a razao que a escada le (`KiContraCorpo.Razao`).</summary>
	private static double VezesOPoderDoTiro(Projetil p, ServerPlayer corpo) => KiContraCorpo.Razao(corpo.Ficha.expressedBP, p.Bp);

	/// <summary>
	/// CRAVA A CHANCE DE DEFLEXAO deste tiro contra este corpo, em porcento, pela conta de producao
	/// (`DanoDeKi.ChanceDeDeflexao`): ela e inversa no `mods`, e o `ModsBase` do tiro desta prova -- o unico lugar que
	/// a bancada alcanca sem mexer na receita nem na conta -- e escalado ate ela devolver o numero pedido.
	///
	/// ============================ POR QUE NO `mods`, E NAO NO PODER DO TIRO ============================
	/// O PODER DO TIRO (`Projetil.Bp`) E O DEGRAU DA CENA: e a razao dele com o poder do corpo que diz se o corpo e
	/// levado, se pode defletir, se e parede (`KiContraCorpo`). Ate 2026-10-12 esta familia cravava a chance mexendo
	/// nele (um tiro com um milionesimo do poder do alvo) -- e hoje esse tiro encontra uma PAREDE, que nem e sorteada.
	/// O `mods` nao entra na escada: mexer nele muda a chance (e o dano, que cai junto) sem tirar a cena do degrau.
	///
	/// O DANO QUE SOBRA E PEQUENO (`chance% x dano final` e uma constante do DEFENSOR), e isso deixou de importar: o
	/// corte por dano saiu, e um golpe de centesimos e sorteado como qualquer outro.
	/// ====================================================================================================
	/// </summary>
	private static void CravarAChance(Projetil p, ServerPlayer corpo, double chance)
		=> p.ModsBase *= ChanceDeDeflexaoDe(p, corpo) / chance;

	/// <summary>
	/// UMA BOLA (reta ou teleguiada) contra um IGUAL parado a seis tiles, com o raspao certo cravado, ate ela acabar.
	/// Devolve quantos relatos sairam (cada um e um `Acertar`: com o raspao certo, nenhum outro desfecho acontece), o
	/// tiro, o corpo, e o que mudou na pericia e na vida de quem apanhou.
	/// </summary>
	private (int Relatos, int Raspoes, Projetil Tiro, ServerPlayer Corpo, double Pericia, double Vida, double Chance, double Dano, double Razao)
		BolaContraQuemSempreRaspa(Vec2 chao, bool teleguiada)
	{
		LimparTudoDaBancada();
		ServerPlayer atira = Forjar("AtiraNoRaspao", chao, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		ServerPlayer corpo = Forjar("Raspa", chao + new Vec2(6 * ZoneCollision.TileSize, 0), bp: BpDoIgualDaEscada);
		double periciaAntes = corpo.Ficha.kidefenseskill, vidaAntes = corpo.Combate!.Corpo.Vida();

		Projetil p = Disparar(atira, new ReceitaDeProjetil
		{
			Tipo = teleguiada ? TipoDeProjetil.Guided : TipoDeProjetil.Blast,
			BaseDano = 1, Velocidade = 1, AlcanceTiles = 20, Nome = "bola do raspao",
		});
		if (teleguiada) p.Alvo = corpo.Id;
		CravarAChance(p, corpo, ChanceDoRaspaoCerto);
		double chance = ChanceDeDeflexaoDe(p, corpo), dano = DanoFinalDe(p, corpo), razao = VezesOPoderDoTiro(p, corpo);

		List<Protocol.HitEvent> relatos = RelatosNoAlvo(corpo, () =>
		{
			for (int i = 0; i < 30 * 8 && p.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		});
		return (relatos.Count, RaspoesEm(relatos), p, corpo, corpo.Ficha.kidefenseskill - periciaAntes,
				corpo.Combate.Corpo.Vida() - vidaAntes, chance, dano, razao);
	}

	/// <summary>
	/// UMA BOLA PARADA (rumo nulo, como as do `Ki_Bomb`) a oito pixels de um igual: vinte tiques com ele em cima
	/// dela, depois ele sai tres tiles por dois tiques, e volta por mais vinte. Devolve os relatos de cada metade.
	/// </summary>
	private (int NaEntrada, int NaVolta, Projetil Mina) MinaContraQuemSempreRaspa(Vec2 chao)
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		ServerPlayer dono = Forjar("DonoDaMina", chao, bp: BpDoFeixeDaEscada);
		ServerPlayer corpo = Forjar("RaspaNaMina", chao + new Vec2(6 * T, 0), bp: BpDoIgualDaEscada);
		Vec2 lugar = corpo.Pos;

		Projetil mina = Disparar(dono, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Blast, BaseDano = 1, AlcanceTiles = 20, Nome = "mina do raspao",
		}, rumoDado: Vec2.Zero, deOnde: lugar + new Vec2(8, 0));
		CravarAChance(mina, corpo, ChanceDoRaspaoCerto);

		int naEntrada = RelatosNoAlvo(corpo, () =>
		{
			for (int i = 0; i < 20 && mina.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		}).Count;

		corpo.Pos = lugar + new Vec2(0, 3 * T);
		for (int i = 0; i < 2 && mina.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		corpo.Pos = lugar;

		int naVolta = RelatosNoAlvo(corpo, () =>
		{
			for (int i = 0; i < 20 && mina.Vivo; i++) TickDosProjeteis(Protocol.TickSeconds);
		}).Count;
		return (naEntrada, naVolta, mina);
	}

	/// <summary>
	/// UM RAIO CANALIZADO contra um igual parado a seis tiles: dezoito tiques (a cabeca chega, raspa e tem tempo de
	/// andar doze tiles), depois o corpo sai quatro tiles de lado por dois tiques e volta a pisar no mesmo lugar
	/// -- agora em cima do TRONCO -- por mais seis.
	/// </summary>
	private (int NaPassagem, int FeixesNaPassagem, float CabecaAlem, double Pericia, double Vida, int NaVolta, int NascidosNaVolta, Projetil Raio)
		RaioContraQuemSempreRaspa(Vec2 raia)
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		ServerPlayer atira = Forjar("RaioNoRaspao", raia, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		ServerPlayer corpo = Forjar("RaspaNoRaio", raia + new Vec2(6 * T, 0), bp: BpDoIgualDaEscada);
		Vec2 lugar = corpo.Pos;
		double periciaAntes = corpo.Ficha.kidefenseskill, vidaAntes = corpo.Combate!.Corpo.Vida();

		// `Canalizando` na unha, como o `RaioDaBancada`: sem o bit um beam nascido pelo `Disparar` morre de `Cessou`
		// no primeiro tique. A receita e a dele, MENOS o `Deflectivel = false`.
		Projetil raio = Disparar(atira, new ReceitaDeProjetil
		{
			Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 40, Nome = "Onda de Ki",
		});
		raio.Canalizando = true;
		CravarAChance(raio, corpo, ChanceDoRaspaoCerto);
		List<Projetil> lista = ProjeteisDaZona(atira.Zone.Hash);

		int naPassagem = RelatosNoAlvo(corpo, () =>
		{
			for (int i = 0; i < 18; i++) UmTiqueDeArrasto();
		}).Count;
		int feixesNaPassagem = lista.Count;
		float cabecaAlem = raio.Pos.X - lugar.X;
		double pericia = corpo.Ficha.kidefenseskill - periciaAntes, vida = corpo.Combate.Corpo.Vida() - vidaAntes;

		corpo.Pos = lugar + new Vec2(0, 4 * T);
		UmTiqueDeArrasto();
		UmTiqueDeArrasto();
		corpo.Pos = lugar;
		int feixesAntesDaVolta = lista.Count;
		int naVolta = RelatosNoAlvo(corpo, () =>
		{
			for (int i = 0; i < 6; i++) UmTiqueDeArrasto();
		}).Count;
		return (naPassagem, feixesNaPassagem, cabecaAlem, pericia, vida, naVolta, lista.Count - feixesAntesDaVolta, raio);
	}

	/// <summary>
	/// UM RAIO QUE JA VINHA LEVANDO O CORPO -- o mais fraco (`FeixeNoMaisFraco`), de guarda SEGURADA ou sem ela -- e
	/// que depois passa a ter o raspao CERTO: o primeiro impacto acerta com o dado desligado e o arrasto comeca; ai o
	/// dado e religado no proprio tiro e a chance e cravada. Devolve os raspoes, o stun de quem vinha sendo levado,
	/// quanto o corpo andou NO TIQUE do primeiro raspao e nos doze DEPOIS dele (sem raspao: nos doze ultimos da cena),
	/// onde a cabeca foi parar e quantos feixes ha na zona.
	/// </summary>
	private (bool Pegou, double Stun, double Chance, int Raspoes, float NoTiqueDoRaspao, float Depois, float CabecaAlem, int Feixes, int Levando)
		RaioQueLevavaQuemRaspa(Vec2 raia, bool deGuarda)
	{
		LimparTudoDaBancada();
		(_, ServerPlayer levado, Projetil raio) = FeixeNoMaisFraco("LevadoQueRaspa", raia, tiles: 6);
		levado.Ficha.Ki = levado.Ficha.MaxKi;
		// A GUARDA SEGURADA (fora da janela do deflect): quem defende e levado sem atordoar, e por isso pode raspar.
		if (deGuarda) SegurarAGuarda(levado);
		for (int i = 0; i < 300 && raio.Vivo && raio.Arrastando == 0; i++) UmTiqueDeArrasto();
		bool pegou = raio.Arrastando == levado.Id;
		double stun = levado.Combate!.Stun;

		raio.Deflectivel = true;
		CravarAChance(raio, levado, ChanceDoRaspaoCerto);
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
			for (int i = 0; i < 12; i++) UmTiqueDeArrasto();
			return (pegou, stun, chance, Contar(), noTique, (levado.Pos - ondeRaspou).Length, raio.Pos.X - levado.Pos.X,
					ProjeteisDaZona(levado.Zone.Hash).Count, raio.Arrastando);
		}
		finally { EscutaDeGolpes = null; }
	}

	/// <summary>
	/// O que uma amostra contou. `Rasparam` = tiros cujo PRIMEIRO sorteio deu raspao; `Ilesos` = os que acabaram sem
	/// acertar nem ser defletidos; `Vezes` = quantas vezes o poder do tiro o corpo tinha.
	/// </summary>
	private readonly record struct AmostraDeRaspao(
		double Vezes, int Tiros, int Ilesos, int Defletidos, int Acertaram, int Outros, int ForaDoPreparo, int Sorteios, int MaisDeUm,
		int Rasparam, int RasparamEApanharam, int RasparamEDefletiram, int SorteiosDosQueRasparam, long Ms)
	{
		private static string Pc(int n, int de) => de == 0 ? "-" : $"{100.0 * n / de:0.00}%";

		/// <summary>A folga de SEIS desvios-padrao de uma faixa esperada em `pc` porcento, em pontos: o dado e o de producao, sem semente, e uma regua justa viraria moeda.</summary>
		private double Folga(double pc) => 6 * Math.Sqrt(pc / 100 * (1 - pc / 100) / Tiros) * 100;

		/// <summary>
		/// OS TRES DESFECHOS NA PROPORCAO ESPERADA (em porcento), cada faixa com a folga dela -- dois pontos pra 10%,
		/// 2,6 pra 18%, tres pra 72% em oito mil tiros. A faixa que se espera VAZIA nao tem folga: um so ja reprova.
		/// </summary>
		public bool NaProporcao(double ilesos, double defletidos, double acertaram)
			=> Math.Abs(100.0 * Ilesos / Tiros - ilesos) <= Folga(ilesos) && Math.Abs(100.0 * Defletidos / Tiros - defletidos) <= Folga(defletidos)
			   && Math.Abs(100.0 * Acertaram / Tiros - acertaram) <= Folga(acertaram);

		public string Proporcao()
			=> $"saem ilesos de raspao {Ilesos} ({Pc(Ilesos, Tiros)}), defletem {Defletidos} ({Pc(Defletidos, Tiros)}), apanham {Acertaram} ({Pc(Acertaram, Tiros)})";

		public string Relato()
			=> $"{Tiros} bolas com a chance de deflexao cravada em {ChanceCravadaDoRaspao:0}%, contra quem tem {Vezes:0.##}x o poder do tiro ({Ms} ms): {Proporcao()}. "
			   + $"Sorteios por tiro: {(double)Sorteios / Tiros:0.000} em media, {MaisDeUm} tiro(s) com mais de um. "
			   + $"O 1o sorteio deu RASPAO em {Rasparam} ({Pc(Rasparam, Tiros)}): {Rasparam - RasparamEApanharam - RasparamEDefletiram} sairam ilesos, "
			   + $"{RasparamEApanharam} APANHARAM depois, {RasparamEDefletiram} defletiram depois "
			   + $"({(Rasparam == 0 ? 0 : (double)SorteiosDosQueRasparam / Rasparam):0.00} sorteios cada)";
	}

	/// <summary>
	/// A PROPORCAO DOS TRES DESFECHOS com a chance de deflexao CRAVADA em 20%, contra um corpo com
	/// <paramref name="vezesOPoderDoTiro"/> vezes o poder do tiro -- 10% dos tiros raspam (e acabam ali, sem dano); dos
	/// 90% que sobram, quem tem o dobro do poder do tiro deflete 20% (`prob(deflectchance)`: 18% do total) e apanha
	/// do resto (72%), e quem nao tem apanha de todos (90%).
	///
	/// A CHANCE E CRAVADA NO TIRO (<see cref="CravarAChance"/>), pela mesma razao por que as outras bancadas viram o
	/// `Deflectivel` no projetil: a receita e a conta sao as de producao, e o unico lugar que a bancada alcanca sem
	/// mexer nelas e o tiro desta prova. Os dois corpos sao curados a cada tiro, e quem apanha volta a pericia de
	/// antes -- senao a chance (e a vida) do tiro mil nao seria a do tiro um. (O ATIRADOR TAMBEM, porque a esfera
	/// defletida VOLTA pra ele -- `RebaterBola`, dono 2026-10-12 -- com o poder de quem a defletiu.)
	/// </summary>
	private AmostraDeRaspao ColherAmostraDeRaspao(Vec2 chao, double vezesOPoderDoTiro)
	{
		LimparTudoDaBancada();
		const int T = ZoneCollision.TileSize;
		ServerPlayer atira = Forjar("Amostrador", chao, bp: BpDoFeixeDaEscada);
		atira.Facing = Facing.East;
		// A SETE TILES: a bola chega com seis de viagem, alem dos quatro em que ela arremessa (`FatorDeEmpurrao`) --
		// um corpo arremessado sairia do lugar entre um tiro e outro.
		ServerPlayer alvo = Forjar("Amostra", chao + new Vec2(7 * T, 0), bp: BpDoFeixeDaEscada * vezesOPoderDoTiro);
		double periciaDeBase = alvo.Ficha.kidefenseskill, vezes = 0;

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
				atira.Combate!.Curar();
				atira.Combate.Stun = 0;

				Projetil p = Disparar(atira, new ReceitaDeProjetil
				{
					Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 1, AlcanceTiles = 20, Nome = "bola da amostra",
				});
				CravarAChance(p, alvo, ChanceCravadaDoRaspao);
				vezes = VezesOPoderDoTiro(p, alvo);
				if (Math.Abs(ChanceDeDeflexaoDe(p, alvo) - ChanceCravadaDoRaspao) > 1e-6 || KiContraCorpo.EhParede(vezes)
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

				// CADA `Acertar` TERMINA EM UM DE TRES: raspao (um relato `Esquivou`), deflexao ou dano (o tiro morre
				// `Acertou`). Entao os sorteios deste tiro sao os raspoes mais o desfecho que encerrou o encontro.
				//
				// O TIRO DEFLETIDO NAO MORRE NA HORA: a esfera volta pra quem a atirou (`DefletirOAtaque`, regra do dono
				// de 2026-10-12). Quem diz que ela foi defletida e o `Desviador` que a deflexao escreve nela -- o fim
				// dela (acertar quem atirou, raspar nele, apagar no caminho) ja nao diz nada sobre este encontro.
				bool defletido = p.Desviador != 0;
				int deste = raspoes + (p.Fim == FimDeProjetil.Acertou || defletido ? 1 : 0);
				sorteios += deste;
				if (deste > 1) maisDeUm++;

				if (defletido) defletidos++;
				else
					switch (p.Fim)
					{
						case FimDeProjetil.Acertou: acertaram++; break;
						case FimDeProjetil.Apagou when raspoes > 0: ilesos++; break;
						default: outros++; break;
					}
				if (raspoes == 0) continue;

				rasparam++;
				sorteiosDosQueRasparam += deste;
				if (p.Fim == FimDeProjetil.Acertou && !defletido) rasparamEApanharam++;
				else if (defletido) rasparamEDefletiram++;
			}
		}
		finally { EscutaDeGolpes = null; }

		return new AmostraDeRaspao(vezes, TirosDaAmostraDeRaspao, ilesos, defletidos, acertaram, outros, foraDoPreparo, sorteios, maisDeUm,
								   rasparam, rasparamEApanharam, rasparamEDefletiram, sorteiosDosQueRasparam, relogio.ElapsedMilliseconds);
	}
}
