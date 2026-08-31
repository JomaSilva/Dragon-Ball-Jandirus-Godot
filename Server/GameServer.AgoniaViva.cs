using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O PALCO DA AGONIA (`--agoniaviva`) -- O LADO DO SERVIDOR DA FOTO ============================
/// A `--planetateste` mede a morte de um planeta em 116 provas e **nao olha a tela uma vez**. Ela
/// ficaria verde num servidor que apertasse o ceu, encurtasse o tremor, derrubasse chao e abrisse
/// cratera sem que um pixel mudasse pra quem esta la embaixo -- porque o caminho do desenho e outro
/// (`ForcarClima` -> `S2C.Ceu`; `MandarEfeito` -> `AoCairEfeito`; `MandarDecalque` -> `Decalques`;
/// `MandarCelulaCaida` -> `PintorDePedacos`), e esse caminho so tem um juiz, que e a foto.
///
/// Este palco existe pra a `--diagchao` (o robo do outro lado) poder fotografar **cinco instantes
/// dos cinco minutos** sem esperar cinco minutos, e pra medir o CUSTO no pico com o jogador dentro do
/// planeta -- que e a pergunta que o dono precisa ver respondida antes do beta.
///
/// ============================ ELE NAO ENCURTA O CAMINHO, SO O RELOGIO ============================
/// A destruicao comeca pela porta unica de producao (`ComecarDestruicao`, a mesma do verb Planet
/// Destroy e a mesma que a vida zerada por tiro de ki usa). Dali pra frente **nada aqui e especial**:
/// quem aperta o ceu, sorteia o tremor, derruba a celula e abre a cratera e o `TremorDaExplosao` de
/// producao, lendo a `MortePlanetaria.Intensidade` de producao.
///
/// O UNICO atalho e o `Faltam`: em vez de descer 1 por segundo por 310 segundos, ele e **fixado** em
/// cinco patamares, um por instante da tira. Fixar (e nao acelerar) e deliberado, por duas razoes
/// medidas:
///   * **o efeito precisa de tempo pra ACUMULAR na intensidade dele.** Cratera dura 18 s, cratera
///     grande 40 s, o chao cai de uma celula por vez. Um relogio acelerado passaria por "agonia 0,7"
///     em tres segundos e a foto sairia com o acumulo da agonia 0,4 -- ou seja, a tira mediria a
///     VELOCIDADE do meu relogio, e nao a rampa;
///   * **a medida de custo pede um estado ESTAVEL.** Quadros por segundo no pico so quer dizer alguma
///     coisa se o pico durar mais que a janela de medida.
///
/// O patamar do pico nao e zero e nem 0,5: e <see cref="FaltamNoPico"/>, e o motivo e aritmetico --
/// o `TickDaDestruicao` faz `e.Faltam -= dt` ANTES de comparar com zero, entao qualquer patamar menor
/// que um tique consumaria a destruicao no proximo segundo em vez de segurar o pico.
///
/// ============================ E A PASTA DO DONO NAO PAGA POR ISTO ============================
/// O palco abre um <see cref="PalcoDeMortes"/> e o segura pela rodada inteira. Enquanto ele esta
/// aberto, morte de planeta **acontece so na memoria** -- `SalvarPlanetasMortos` nao escreve. Ele e
/// fechado no fim, e ai imprime <see cref="PalcoDeMortes.MatouAqui"/>: um palco que se protege sem
/// provar que a arma disparou fica verde para sempre no dia em que a destruicao parar de matar.
/// ======================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>`--agoniaviva`. Ver o cabecalho.</summary>
	private bool _agoniaViva;

	/// <summary>
	/// OS CINCO PATAMARES, em "segundos que faltam". Sao os MESMOS instantes que a tira do espaco
	/// (`RoboDaAgonia.DegrausDaTira`) fotografa, de proposito: as duas tiras contam a mesma historia
	/// vista de dois lugares, e um dono que abra as duas lado a lado tem que ver a mesma escada.
	///
	/// A agonia que cada um produz (`Intensidade`, `t^1,5` sobre o piso 0,12): 0,12 / 0,24 / 0,43 /
	/// 0,70 / 0,98.
	/// </summary>
	private static readonly double[] PatamaresDaAgonia =
	[
		MortePlanetaria.SegundosDeExplosao,             // 310 -- o piso, o segundo zero
		MortePlanetaria.SegundosDeExplosao * 0.75,      // 232,5
		MortePlanetaria.SegundosDeExplosao * 0.50,      // 155
		MortePlanetaria.SegundosDeExplosao * 0.25,      // 77,5
		FaltamNoPico,                                   // 4 -- o auge. Ver o cabecalho.
	];

	/// <summary>O patamar do pico. Maior que um tique de propósito -- ver o cabecalho.</summary>
	private const double FaltamNoPico = 4;

	/// <summary>
	/// QUANTO CADA PATAMAR SEGURA. Dezoito segundos porque o tremor mais lento da rampa vem a cada
	/// ~10 s (`TremorMax` apertado pela agonia do piso): um patamar mais curto poderia nao conter
	/// tremor nenhum, e a foto do primeiro instante mostraria um mundo calmo por acidente de sorteio.
	/// </summary>
	private const double SegundosPorPatamar = 26;

	/// <summary>
	/// O PICO SEGURA MAIS, e a conta e do consumidor: o robo mede o custo numa janela de 8 s **depois**
	/// de gastar ~5 s medindo tremor e chao, e antes disso ele ainda espera o mundo assentar. Quarenta
	/// segundos cobrem os tres com folga -- e cobrem tambem a vida da cratera grande (40 s), que e o
	/// decalque mais longo: medir custo antes dela encher seria medir o pior caso pela metade.
	/// </summary>
	private const double SegundosNoPico = 40;

	/// <summary>
	/// A CALMA ANTES. O robo precisa de um mundo NORMAL pra fotografar o controle e pra medir os
	/// quadros por segundo de base e pro PISO DE RUIDO (o mundo parado contra ele mesmo) -- sem isso,
	/// "58 quadros por segundo no pico" e "93% do chao virou destroco" nao querem dizer nada,
	/// porque ninguem sabe quanto era antes.
	/// </summary>
	private const double SegundosDeCalma = 34;

	/// <summary>O que o palco esta fazendo. Ver <see cref="TickDoPalcoDaAgonia"/>.</summary>
	private int _agoniaVivaPasso = -1;

	private double _agoniaVivaRelogio;
	private ZoneKey _agoniaVivaZona;
	private PalcoDeMortes? _agoniaVivaPalco;
	private bool _agoniaVivaConsumada;

	/// <summary>
	/// A MARCA QUE O ROBO PODE LER NO LOG. Ela nao e o relogio dele -- ele decide quando fotografar
	/// pela AGONIA que o `S2C.Mortos` entrega, que e o caminho de producao. Isto aqui e pra o humano
	/// que le a saida do processo entender o que esta vendo na janela.
	/// </summary>
	private const string MarcaDoPalcoDaAgonia = "[PALCO-DA-AGONIA] ";

	/// <summary>
	/// UM SEGUNDO DO PALCO. Chamado do bloco de 1 Hz do <see cref="TickDaDestruicao"/>, **antes** dele:
	/// assim o patamar que este metodo fixa e o que a destruicao consome no mesmo segundo, em vez de
	/// ser fixado sobre um valor que ela ja gastou.
	/// </summary>
	private void TickDoPalcoDaAgonia(double dt)
	{
		if (!_agoniaViva) return;


		// ---- PASSO -1: ESPERANDO ALGUEM NUM PLANETA ----
		// O palco nao escolhe o planeta: quem escolhe e onde a pessoa entrou. Cravar "Earth" aqui
		// faria a bancada mentir no dia em que o berco mudasse de lugar.
		if (_agoniaVivaPasso < 0)
		{
			ServerPlayer? quem = _players.Values.FirstOrDefault(
				p => p.Peer != null && !p.Ficha.dead && ChaveDePlaneta.Da(p.Zone) != null);
			if (quem == null) return;

			_agoniaVivaZona = quem.Zone;
			_agoniaVivaPasso = 0;
			_agoniaVivaRelogio = 0;

			// ============================ MEIO-DIA, UMA VEZ SO, E A EXCECAO TEM MOTIVO ============================
			// **Uma rodada caiu no meio da NOITE e a tira do chao saiu imprestavel**: seis quadros quase
			// pretos, o mundo calmo indistinguivel do mundo acabando, com o placar todo verde -- porque a
			// rampa continuava subindo em NUMERO enquanto a foto nao mostrava nada. A mesma medida do ar
			// deu `1,55 -> 2,95` de dia e `0,88 -> 0,99` de noite, porque a `Iluminacao` encolhe o efeito
			// do clima conforme a base escurece (esta escrito la, e e proposital).
			//
			// ============================ E POR QUE NAO TIQUE A TIQUE, COMO O PALCO DO BIO ============================
			// Porque aqui isso QUEBRA O ASSUNTO DA BANCADA, e a medida mostrou: `AjustarCeuDaTerra`
			// adianta o relogio do mundo ate o PROXIMO meio-dia -- ou seja, chamada a 1 Hz ela empurra o
			// mundo quase um dia inteiro por segundo. E `ClimaForcado.Ate` e um instante ABSOLUTO do
			// mundo: com o relogio pulando um dia por tique, o ceu de destruicao vence no primeiro salto
			// e o `ApertarClima` para de valer. Medido: a coluna do ceu ficou **0,52 do comeco ao fim**,
			// travada no valor de largada, e a rampa do clima -- que e metade do pedido do dono --
			// simplesmente nao aconteceu.
			//
			// O palco do bio pode chamar tique a tique porque ele nao usa clima forcado. Este nao pode.
			// Uma vez basta: o dia da Terra tem 1440 s (`Ceu.SegundosPorDia`) e a rodada inteira leva
			// ~3,5 min, ou seja o sol anda umas tres horas e meia -- de meio-dia ate o meio da tarde.
			// ====================================================================================================
			AjustarCeuDaTerra(hora: 0.5);

			// ============================ E O CEU FICA LIMPO DURANTE A CALMA ============================
			// **A FOTO MOSTROU A CAUSA DE UM NUMERO QUE EU NAO ENTENDIA.** O piso de ruido do mundo
			// parado (o quanto ele difere de si mesmo) media 15% e pulava de rodada pra rodada -- perto
			// demais do primeiro instante da agonia (22%), o que estreitava justamente a ponta baixa da
			// rampa. Olhando o quadro 0 da tira: estava CHOVENDO. Cada gota e um pixel que muda entre
			// dois quadros, e a chuva cobre a tela inteira.
			//
			// A regra da casa e literal sobre isto (uma bancada anterior mediu a cor errada por causa de
			// uma chuva de sangue no palco): *"force clima limpo quando precisar"*.
			//
			// ============================ E A FORCA E BAIXA, COM PRAZO CURTO, DE PROPOSITO ============================
			// `ForcarClima` tem a guarda do "o mais forte vence, e nao o mais recente":
			// `if (atual.Forca > forca && atual.Ate >= agora + segundos) return;`. Um ceu limpo forcado
			// com forca 1 e prazo longo **RECUSARIA o ceu de destruicao** que comeca em 0,52 -- e a
			// bancada mediria um fim de mundo com sol. Meia forca, e um prazo que vence antes de a
			// destruicao comecar: quando ela chega, as duas metades da guarda ja falharam.
			// ==================================================================================================
			ForcarClima(_agoniaVivaZona, TipoDeClima.Limpo, SegundosDeCalma - 4, 0.5,
						"bancada da agonia: o controle precisa de um ceu limpo");
			GD.Print($"{MarcaDoPalcoDaAgonia}calma: {SegundosDeCalma:0} s de mundo NORMAL em '{_agoniaVivaZona.Name}' "
				   + "(o controle e a linha de base do custo)");
			return;
		}

		_agoniaVivaRelogio += dt;

		// ---- PASSO 0: A CALMA ----
		if (_agoniaVivaPasso == 0)
		{
			if (_agoniaVivaRelogio < SegundosDeCalma) return;

			// O PALCO ABRE **ANTES** DA ARMA DISPARAR, e nao depois: o `PalcoDeMortes` tira uma foto
			// do registro no construtor, e abrir depois guardaria o mundo ja estragado como se fosse
			// o estado bom.
			_agoniaVivaPalco = PalcoDeMortesDeBancada();

			// A PORTA UNICA DE PRODUCAO. O BP do algoz e o de quem esta la -- o mesmo numero que o
			// verb fixaria (`var/mexpressedBP = usr.expressedBP`), e nao um numero de bancada.
			double bp = _players.Values
				.Where(p => p.Peer != null && p.Zone.Hash == _agoniaVivaZona.Hash)
				.Select(p => p.Ficha.expressedBP)
				.DefaultIfEmpty(MortePlanetaria.BpExigido(1))
				.Max();

			bool foi = ComecarDestruicao(_agoniaVivaZona, bp,
										 "bancada da agonia (--agoniaviva)", 0);
			_agoniaVivaPasso = 1;
			_agoniaVivaRelogio = 0;
			GD.Print($"{MarcaDoPalcoDaAgonia}a destruicao COMECOU pela porta de producao (aceita={foi}); "
				   + $"{PatamaresDaAgonia.Length} patamares de {SegundosPorPatamar:0} s");
			return;
		}

		// ---- PASSOS 1..N: OS PATAMARES ----
		int i = _agoniaVivaPasso - 1;
		if (i < PatamaresDaAgonia.Length)
		{
			if (MorteDaZona(_agoniaVivaZona) is not { } e) return;

			// A FIXACAO, E O REENVIO JUNTO. O cliente converte `Faltam` em prazo ABSOLUTO no instante
			// em que recebe, e dali em diante ele integra sozinho -- entao fixar sem reenviar deixaria
			// as duas pontas com rampas diferentes, e a foto sairia de um instante que o servidor nao
			// estava vivendo. Um pacote pequeno por segundo por pessoa: e bancada, e esta escrito.
			e.Faltam = PatamaresDaAgonia[i];
			MandarMortosPraTodos();

			double quanto = i == PatamaresDaAgonia.Length - 1 ? SegundosNoPico : SegundosPorPatamar;
			if (_agoniaVivaRelogio < quanto) return;

			_agoniaVivaPasso++;
			_agoniaVivaRelogio = 0;
			GD.Print($"{MarcaDoPalcoDaAgonia}patamar {i + 1}/{PatamaresDaAgonia.Length} cumprido "
				   + $"(faltam={PatamaresDaAgonia[i]:0.0}s, agonia={MortePlanetaria.Intensidade(e):0.000})");
			return;
		}

		// ---- O DESFECHO: SOLTA O RELOGIO E DEIXA O COMMIT ACONTECER ----
		// Daqui pra frente nada e fixado: o `TickDaDestruicao` desce o `Faltam` sozinho, chega em zero
		// e chama o `ConsumarDestruicao` de producao -- dano pela furia do mundo, evacuacao pro ponto
		// onde o planeta ficava. E o X3/X4 do pedido, e o robo fotografa o outro lado dele.
		if (!_agoniaVivaConsumada && ZonaMorta(_agoniaVivaZona))
		{
			_agoniaVivaConsumada = true;
			_agoniaVivaRelogio = 0;
			GD.Print($"{MarcaDoPalcoDaAgonia}o mundo acabou. '{_agoniaVivaZona.Name}' esta destruido e quem estava la "
				   + "foi jogado no espaco.");
			return;
		}

		// ---- E O PALCO FECHA, devolvendo o mundo ----
		if (_agoniaVivaConsumada && _agoniaVivaRelogio >= 20 && _agoniaVivaPalco != null)
		{
			PalcoDeMortes palco = _agoniaVivaPalco;
			_agoniaVivaPalco = null;
			palco.Dispose();
			GD.Print($"{MarcaDoPalcoDaAgonia}palco fechado: {palco.MatouAqui} planeta(s) morreram DENTRO dele "
				   + $"({palco.NomesQueMorreram}). O `planetas-mortos.json` nunca foi tocado.");
		}
	}
}
