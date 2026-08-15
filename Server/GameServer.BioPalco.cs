using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O PALCO DA ESCADA DO BIO (`--biovivo`) -- O LADO DO SERVIDOR DA FOTO ============================
/// As 159 provas do `--bioteste` medem o gatilho, o numero e a recusa. **Todas elas passariam verdes
/// num servidor que subisse a escada inteira sem que um pixel mudasse na tela de quem esta olhando** --
/// porque o caminho do desenho e outro (`Appearance.Corpo` -> `TrocarAparencias` -> `S2C.PeerLook` ->
/// `CharacterVisual.Vestir` -> `VisualCatalog.CorpoSprite`), e ele so tem um juiz: a foto.
///
/// Este projeto ja pagou tres vezes por confiar em numero verde onde a pergunta era de pixel (a cor do
/// desvio, a barra de vida, o rastro da agua). O caso do bio-androide e o mais provavel de todos: a
/// arte dos quatro degraus estava importada e **sem um unico consumidor em C#** ate esta sessao. Uma
/// lista de caminhos que ninguem carrega e uma escada em que todos os degraus saem iguais na tela.
///
/// ============================ ELE NAO ENCURTA CAMINHO ============================
/// O corpo nasce pelo `NascerNpc` de producao, vira bio-androide pelo `NascerBioAndroide` de producao
/// (com uma fornada de verdade num laboratorio de verdade), sobe cada degrau pelo `SubirDegrauDoBio` de
/// producao e entra nas duas formas pelo `TransformarPara` de producao. O unico atalho e o RELOGIO: os
/// degraus vem de oito em oito segundos em vez de doze horas e dez jogadores.
///
/// ============================ OITO POSES, E A PRIMEIRA E O CONTROLE ============================
///   0  HUMANO         -- o corpo antes de tudo. Sem ele, "os quatro degraus sao diferentes" nao diz
///                        que a criatura deixou de parecer gente;
///   1  LARVA          -- `Cell Larva.tres`
///   2  IMPERFEITO     -- `Bio Android 1.tres`
///   3  SEMI-PERFEITO  -- `Bio Android 2.tres`
///   4  PERFEITO       -- `Bio Android 3.tres`
///   5  SUPER PERFEITA -- `Bio Android 4.tres`, e esta e CAMADA DE FORMA e nao corpo de repouso
///   6  SUPER SAIYAJIN -- o mesmo corpo perfeito, PARADO. E a unica pose em que o sprite **nao** muda:
///                        o bio nao ganha cabelo em forma nenhuma (`HairObject.dm:168`), o SSJ1 nao
///                        tem raios, e a aura deste port so acende acima de 100% de Ki. Ela sai IGUAL
///                        a pose 4, e isso e a regra e nao um buraco;
///   7  SUPER SAIYAJIN CARREGANDO -- o tanque acima de 100%, a aura dourada acesa. **Esta** e a unica
///                        imagem desta escada em que o DNA Saiyajin do doador aparece na tela.
/// ==========================================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>`--biovivo`. Ver o cabecalho.</summary>
	private bool _bioVivo;

	/// <summary>O corpo em cena. Zero = o palco ainda nao foi montado.</summary>
	private int _bioVivoId;

	/// <summary>Em que pose ele esta. Ver o cabecalho.</summary>
	private int _bioVivoPasso;

	/// <summary>Quando dar o proximo passo (ms do relogio do servidor). Zero = acabou.</summary>
	private long _bioVivoProximo;

	/// <summary>A conta de quem esta OLHANDO -- o palco anda atras dela. Ver o `TickDoPalcoDoBio`.</summary>
	private string _bioVivoConta = "";

	/// <summary>
	/// O NOME DO CORPO, e ele e a unica coisa que o robo da foto precisa saber de antemao.
	///
	/// O `TrocarAparencias` manda um `S2C.PeerLook` com id + nome pra zona inteira a cada degrau, e e
	/// por ele que o fotografo acha o corpo certo num planeta com cento e quarenta e oito cidadaos.
	/// Procurar "o corpo mais perto" daria a resposta certa quase sempre, e "quase sempre" numa bancada
	/// visual e uma foto de um transeunte com nome de prova.
	/// </summary>
	public const string NomeDoPalcoDoBio = "Palco do Bio";

	/// <summary>
	/// O PREFIXO DO ANUNCIO DE POSE. Ver o bloco no <see cref="TickDoPalcoDoBio"/>: e por ele que o
	/// fotografo sabe QUANDO disparar, em vez de adivinhar olhando o corpo. Constante compartilhada
	/// entre o palco e o robo -- duas strings digitadas seriam duas verdades.
	/// </summary>
	public const string MarcaDoPalcoDoBio = "[PALCO-DO-BIO] pose ";

	/// <summary>
	/// OITO SEGUNDOS POR DEGRAU. O robo espera o corpo ASSENTAR (a cena de forma leva quadros pra
	/// vestir) antes de disparar, e uma janela curta fotografaria a troca em vez do estado -- que e o
	/// erro classico deste tipo de bancada, ja pago no `RoboDeDoisCorpos`.
	/// </summary>
	private const double SegundosPorDegrauDoPalco = 8;

	/// <summary>
	/// ============================ MENOS OS DOIS DEGRAUS QUE RODAM CINEMATICA DE 28 s ============================
	/// **ESTA TABELA E O CONSERTO DE QUATRO LINHAS VERMELHAS**, e a causa nao estava neste arquivo: quando
	/// as cenas do bio-androide entraram (`Cinematicas.BioEvoluindo`, 28,0 s), o semi-perfeito e o perfeito
	/// passaram a rodar filme -- e o cliente **segura a aparencia nova ate a virada da cena**, de proposito
	/// (`World.AoReceberAparencia`: vestir no meio penduraria a silhueta de luz sobre um corpo que ja e o
	/// novo, que e um defeito que o dono ja fotografou uma vez).
	///
	/// Com o palco andando de 8 em 8 segundos, as poses 3, 4, 5 e 6 eram fotografadas **dentro** de uma cena
	/// que ainda nao virou, e as quatro saiam vestindo a `Bio Android 1` -- a folha do degrau ANTERIOR. A
	/// bancada estava certa e o jogo estava certo: quem estava errado era o relogio do palco.
	///
	/// E ISSO E UMA CORRECAO DE MEDIDA, nao de escada: nada do que o `--bioteste` cobra muda: o degrau, o BP
	/// e a folha continuam sendo escritos no mesmo instante. O que muda e o palco esperar o filme acabar
	/// antes de pedir a proxima pose -- que e o que um jogador tambem faz.
	///
	/// 36 e nao 28: os 28 sao ate a VIRADA, e depois dela o robo ainda espera o corpo ASSENTAR (2,0 s) e
	/// ainda tira o SEGUNDO clique da mesma pose (3,5 s), que e o controle de ruido dele. Uma janela justa
	/// cortaria o segundo clique e a comparacao de pixel ficaria comparando mato com mato.
	/// ========================================================================================================
	/// </summary>
	private static double SegundosDoPassoDoPalco(int passo) => passo switch
	{
		3 or 4 => 36,
		_ => SegundosPorDegrauDoPalco,
	};

	/// <summary>
	/// O BP DO DOADOR DE MENTIRA. A Super Perfeita pede `PortaSuperPerfeito` (750 milhoes) de BP
	/// BASE, e o bio nasce com METADE do doador mais forte -- entao o doador precisa de quatro
	/// bilhoes pra a criatura nascer com dois e a forma abrir sem ninguem empurrar nada.
	/// </summary>
	private const double BpDoDoadorDoPalco = 4e9;

	/// <summary>
	/// MONTA O PALCO no primeiro login: um corpo comum nasce a seis tiles de quem entrou, e ele ainda
	/// e gente. O primeiro passo (o nascimento) so vem no proximo tique -- e essa folga E a foto de
	/// controle: sem ela o robo chegaria ao primeiro quadro com a larva ja na tela.
	/// </summary>
	private void MontarOPalcoDoBio(ServerPlayer pl)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(pl.Zone);
		if (mapa == null) { GD.PrintErr("[biovivo] a zona do host nao tem mapa"); return; }

		// SEIS TILES: perto o bastante pra caber no mesmo quadro que o host (a camera segue o host) e
		// longe o bastante pra os dois corpos nao se empilharem na imagem -- corpos empilhados nao se
		// leem na foto, e isso ja custou uma rodada inteira ao `RoboDeDoisCorpos`.
		Vec2 onde = mapa.PontoLivrePerto(pl.Pos + new Vec2(6 * ZoneCollision.TileSize, 0));
		ServerPlayer? s = NascerNpc("cidadao", pl.Zone, onde, ++_lugarDaBancadaDeBio);
		if (s == null) { GD.PrintErr("[biovivo] o corpo do palco nao nasceu"); return; }

		// ============================ ELE FICA PARADO, E ISSO E UMA MEDIDA E NAO CONFORTO ============================
		// Um cidadao passeia (`Cerebro`), e a bancada compara os RETRATOS pixel a pixel: um corpo que
		// anda entre uma pose e outra muda o FUNDO do recorte, e a diferenca sai grande por causa do
		// mato atras dele. Ou seja, um palco andarilho daria VERDE numa escada em que os quatro degraus
		// desenhassem o mesmo boneco -- que e exatamente o defeito que esta foto existe pra pegar.
		//
		// `Cerebro = null` e a porta que o proprio jogo ja usa pra corpo que nao se move (o boneco do
		// transe nasce assim, `GameServer.CorpoLargado.cs:86`), e nao um `if` novo no laco da IA.
		// =========================================================================================================
		s.Cerebro = null;
		s.Name = NomeDoPalcoDoBio;
		s.Conta = "palco-do-bio";
		s.Race = "Human";
		s.Ficha.Race = "Human";
		s.Ficha.ParentRace = "Human";
		s.Ficha.Statify();
		s.SigAtributos = "";
		TrocarAparencias(s);

		_bioVivoId = s.Id;
		_bioVivoConta = pl.Conta;

		// ============================ O PALCO COMECA EM -1, E ISSO E O CONSERTO DE UM ERRO DE UM ============================
		// O anuncio saia ANTES da acao ("pose 1" e so entao o nascimento), e o fotografo -- que dispara
		// no anuncio -- retratava o estado ANTERIOR com o rotulo do SEGUINTE. A rodada saiu com as oito
		// poses e **todos os rotulos deslocados em um**: a larva rotulada "humano", a Super Perfeita
		// fotografada antes de a camada existir.
		//
		// Com -1, o primeiro tique cai no `case 0`, que nao faz nada e serve so pra anunciar o corpo
		// HUMANO -- a foto de controle. Dai em diante a acao vem primeiro e o anuncio depois, entao o
		// numero anunciado e sempre o do estado que ja esta na tela.
		// ================================================================================================================
		_bioVivoPasso = -1;
		_bioVivoProximo = NowMs() + (long)(SegundosPorDegrauDoPalco * 1000);

		GD.Print($"[biovivo] '{s.Name}' (id {s.Id}) nasceu HUMANO a 6 tiles do host. "
			   + $"A escada comeca em {SegundosPorDegrauDoPalco:0} s, um degrau a cada "
			   + $"{SegundosPorDegrauDoPalco:0} s.");
	}

	/// <summary>
	/// UM DEGRAU POR VOLTA. Roda no tique de 1 Hz, ao lado do <see cref="TickDaGestacao"/> -- que e
	/// exatamente o relogio que faria isso em jogo, so que com outros numeros.
	/// </summary>
	private void TickDoPalcoDoBio()
	{
		if (_bioVivoProximo == 0) return;
		if (!_players.TryGetValue(_bioVivoId, out ServerPlayer? s))
		{ _bioVivoProximo = 0; GD.PrintErr("[biovivo] o corpo do palco sumiu"); return; }

		// ============================ O PALCO SEGUE O ELENCO, E ISSO CUSTOU UMA RODADA INTEIRA ============================
		// A camera do jogo segue o DONO da tela, e o dono da tela leva porrada: nesta zona ha um chefe de
		// saga de pe, e ele nocauteia e ARRASTA o fotografo pelo mapa. Uma rodada voltou com quatro das
		// oito poses marcadas "o palco esta FORA DA TELA -- corpo em (3907, 351) numa tela de 1920x1080".
		//
		// O conserto e o palco andar com quem olha, e nao o contrario: reposicionar o BONECO custa uma
		// atribuicao (ele e do servidor), enquanto arrastar o jogador de volta seria teleporte a cada
		// oito segundos, com correcao de posicao no cliente e briga com o controle dele.
		//
		// ============================ E ELE SEGUE A CADA TIQUE, NAO A CADA PASSO ============================
		// Isto ficava DEPOIS da guarda do prazo, ou seja so acontecia na virada do passo -- e enquanto os
		// passos duravam oito segundos os dois eram quase a mesma coisa. Deixaram de ser no instante em
		// que os dois degraus com cinematica passaram a durar 36 s (ver `SegundosDoPassoDoPalco`): meio
		// minuto e tempo de sobra pra o chefe da saga arrastar o fotografo pra longe, e a rodada voltou
		// com quatro retratos de MATO -- que a bancada leu como "os degraus desenham igual" (0,1% de
		// diferenca entre a pose 3 e a 4), porque duas fotos de grama sao mesmo iguais.
		// ================================================================================================
		ServerPlayer? olheiro = _players.Values.FirstOrDefault(
			p => p.Conta == _bioVivoConta && p.Peer != null && p.Zone.Equals(s.Zone));
		if (olheiro != null) s.Pos = olheiro.Pos + new Vec2(6 * ZoneCollision.TileSize, 0);

		// ============================ E O SOL FICA A PINO A RODADA INTEIRA, TIQUE A TIQUE ============================
		// `--horateste 0.5` crava meio-dia **na largada** e o relogio do mundo continua andando. Com a
		// escada em 8 s por degrau a rodada cabia num pedaco do dia; com os 36 s das duas cinematicas ela
		// passou a durar quase o triplo, e os ultimos retratos sairam ao ANOITECER -- quase pretos. Numa
		// bancada que julga por diferenca de pixel isso nao aparece como "esta escuro": aparece como
		// "todos os degraus desenham igual", porque no escuro eles desenham mesmo (9 linhas vermelhas,
		// com a pose 3 e a 4 diferindo em 0,1%).
		//
		// A CADA TIQUE, E NAO A CADA PASSO -- e essa diferenca tambem foi medida. Recravando so na virada
		// do passo, a foto da pose 4 sai trinta segundos depois do ajuste (ela espera a cinematica) e a da
		// pose 6 sai dois segundos depois: meia hora de mundo entre uma e outra, e o par delas voltou
		// "94,3% de pixels diferentes" sendo o mesmo boneco parado. Tique a tique o sol nao anda mais que
		// um segundo, e a luz para de ser uma variavel.
		//
		// `AjustarCeuDaTerra` e aritmetica pura sem `fase` (tres linhas e uma soma), entao 1 Hz nao custa
		// nada. Ela empurra `_adiantoDoCeu` pra o PROXIMO meio-dia, ou seja o ceu volta sempre ao mesmo
		// ponto -- e o que cresce e um `double` de adianto, que nesta bancada morre com o processo.
		// ========================================================================================================
		AjustarCeuDaTerra(hora: 0.5);

		if (NowMs() < _bioVivoProximo) return;
		if (olheiro == null)
			GD.PrintErr("[biovivo] o olheiro nao esta na zona do palco -- as fotos vao sair vazias");

		// ============================ OITO SEGUNDOS BASTAM PORQUE A ESTREIA FOI PULADA ============================
		// Numa rodada anterior as tres ultimas poses sumiram, e a causa nao era o palco: uma forma NOVA
		// roda a cena de estreia (~29 s) e enquanto ela corre o corpo troca de folha varias vezes por
		// segundo. O fotografo espera o corpo ASSENTAR antes de disparar -- e tem que esperar --, entao
		// ele nunca chegava a um estado parado dentro da janela.
		//
		// O conserto NAO foi esticar a janela (chegou a quarenta segundos e ainda era frageil): foi
		// marcar as duas formas como ja estreadas no nascimento (ver `NascerNoPalco`). Sem cena, oito
		// segundos sobram pra qualquer pose.
		// ======================================================================================================
		// O PRAZO E O DO PASSO QUE VAI RODAR AGORA (`_bioVivoPasso` so incrementa no `switch` abaixo), e
		// ele nao e mais unico -- ver `SegundosDoPassoDoPalco`: os dois degraus que rodam cinematica de 28 s
		// precisam de mais que oito segundos, senao a foto sai com a folha do degrau anterior.
		_bioVivoProximo = NowMs() + (long)(SegundosDoPassoDoPalco(_bioVivoPasso + 1) * 1000);

		// ============================ O PALCO **ANUNCIA** A POSE, E ISSO NAO E ENFEITE ============================
		// O fotografo disparava sozinho, quando via o corpo trocar de folha -- e com isso ele perdia
		// TRES das oito poses, sempre as mesmas. Duas razoes, e as duas sao do desenho e nao do bug:
		//
		//   * a pose 6 (Super Saiyajin parado) veste EXATAMENTE as mesmas folhas da pose 4, porque o bio
		//     nao ganha cabelo em forma nenhuma. Pra quem olha so a folha, ela nunca aconteceu;
		//   * a pose 5 (Super Perfeita) tem RAIOS, e o bit de emissao pisca -- o carimbo nunca ficava
		//     parado o bastante pra o disparo acontecer.
		//
		// O anuncio inverte a responsabilidade: quem sabe que a pose mudou e quem a EXECUTOU. Ele sai
		// pelo `Avisar` (`Fala.Sistema`), que e o mesmo canal de chat que o jogo ja usa -- nao ha
		// pacote novo, e o robo escuta o evento `Falou` que o cliente ja publica.
		// ======================================================================================================
		switch (++_bioVivoPasso)
		{
			// O CONTROLE: o corpo ainda e gente, e a unica coisa que este passo faz e AVISAR isso.
			case 0: break;

			case 1: NascerNoPalco(s); break;
			case 2: SubirDegrauDoBio(s, BioAndroids.Imperfeito); break;
			case 3: SubirDegrauDoBio(s, BioAndroids.SemiPerfeito); break;
			case 4: SubirDegrauDoBio(s, BioAndroids.Perfeito); break;

			case 5:
				s.Ficha.Ki = s.Ficha.MaxKi;
				TransformarPara(s, "super_perfect");
				GD.Print($"[biovivo] passo 5: SUPER PERFEITA -- forma '{s.Forma.Atual}', "
					   + $"buff {s.Ficha.ssjBuff:0.##}x");
				break;

			case 6:
				// DE VOLTA A BASE PRIMEIRO: a Super Perfeita proibe a escada Saiyajin inteira nos DOIS
				// sentidos (`ProibidoComFormaAtual`), que e o conserto do bug em que o SSJ2 do DM
				// atropelava a forma de 8x calado.
				TransformarPara(s, Jandirus.Core.Forms.Catalogo.IdBase);
				s.Ficha.Ki = s.Ficha.MaxKi;
				TransformarPara(s, "ssj1");
				GD.Print($"[biovivo] passo 6: SUPER SAIYAJIN pelo DNA -- forma '{s.Forma.Atual}', "
					   + $"buff {s.Ficha.ssjBuff:0.##}x (careca: o bio nao ganha cabelo em forma "
					   + "nenhuma -- so aura e raios)");
				break;

			// ============================ A OITAVA POSE EXISTE POR CAUSA DE UMA FOTO ============================
			// A pose 6 saiu **identica** a pose 4 na primeira rodada boa, e nao e defeito: o bio nao
			// ganha cabelo em forma nenhuma (`HairObject.dm:168`), o SSJ1 nao tem raios, e a regra de
			// aura deste port e do dono -- *"so aparece quando o ki passa de 100%; um Super Saiyajin
			// parado deixa de brilhar"* (`World.PrepararAuraDaForma`). Ou seja: um bio-androide em
			// Super Saiyajin PARADO e visualmente o mesmo bicho.
			//
			// Entao a prova de que o DNA Saiyajin chega ate a TELA nao esta na pose 6 -- esta aqui: com
			// o tanque acima de 100%, o `TickDaCarga` acende o `aura_ki` e a aura dourada aparece. Sem
			// esta pose a bancada teria que reprovar a 6 (que esta certa) ou aprova-la sem prova.
			// ==================================================================================================
			case 7:
				s.Ficha.Ki = s.Ficha.MaxKi * 1.6;
				TickDaCarga(s, 0.1);
				GD.Print($"[biovivo] passo 7: SUPER SAIYAJIN **CARREGANDO** -- Ki em "
					   + $"{s.Ficha.Ki / Math.Max(s.Ficha.MaxKi, 1) * 100:0}% do tanque, "
					   + $"aura de carga={s.AuraDeCarga}");
				break;

			default:
				_bioVivoProximo = 0;
				GD.Print("[biovivo] a escada terminou. As oito poses passaram pela tela.");
				return;
		}

		// ============================ O ANUNCIO VEM DEPOIS DA ACAO ============================
		// Ver o bloco do `_bioVivoPasso = -1`: anunciando antes, o fotografo retrata o estado velho com
		// o rotulo novo. Ele sai pelo `Avisar` (`Fala.Sistema`), o mesmo canal de chat do jogo -- nao ha
		// pacote novo, e o robo escuta o evento `Falou` que o cliente ja publica.
		// ==================================================================================
		AnunciarNoMundo($"{MarcaDoPalcoDoBio}{_bioVivoPasso}");
	}

	/// <summary>
	/// O NASCIMENTO, PELA PORTA DE PRODUCAO: um laboratorio de verdade com uma fornada de verdade, e
	/// o <see cref="NascerBioAndroide"/> faz o resto. O laboratorio se rompe la dentro, como sempre.
	///
	/// A AMOSTRA E SAIYAJIN de proposito: sem ela nao ha `canSSJ`, e o passo 6 (a prova de que o DNA
	/// chega ate a tela) nao existiria.
	/// </summary>
	private void NascerNoPalco(ServerPlayer s)
	{
		var lab = new Obra
		{
			Id = 984_900, Tipo = "Android_Creation_Mainframe", Aparafusada = true, Lab = 2,
			X = s.Pos.X, Y = s.Pos.Y, DonoConta = s.Conta, DonoNome = s.Name,
		};
		lab.PorZona(s.Zone);
		_noChao.Add(lab);

		var g = new Gestacao
		{
			DonoConta = s.Conta,
			MaiorBp = BpDoDoadorDoPalco,
			TemSaiyajin = true,
			Amostras = { new Amostra { Raca = "Saiyan", Doador = "doador do palco", Bp = BpDoDoadorDoPalco } },
		};

		NascerBioAndroide(s, lab, g);

		// ============================ A MAESTRIA VAI A 100% AQUI, E E POR CAUSA DA CINEMATICA ============================
		// Forma nova roda cena de estreia (`Cinematicas.NoDegrau`), e cena de estreia troca a folha do
		// corpo varias vezes por segundo por quase meio minuto. O fotografo espera o corpo assentar --
		// e tem que esperar --, entao um palco que estreia cada forma nao produz retrato nenhum.
		//
		// Maestria cheia e o terceiro degrau de cena ("quem domina a forma"), que e a transformacao
		// DIRETA. Isso nao encurta caminho nenhum do que esta sendo medido: a folha, a camada de forma
		// e a aura sao as mesmas: o que muda e so o tempo que o corpo passa piscando antes de chegar
		// nelas. E e a mesma manivela que o `--dois` usa pra fotografar corpo transformado.
		// ==============================================================================================================
		s.Forma.Maestria.Por("ssj1", 100);
		s.Forma.Maestria.Por("super_perfect", 100);

		// ============================ E A ESTREIA JA CONTA COMO VISTA, PELO MESMO MOTIVO ============================
		// Maestria cheia encurta a cena das vezes SEGUINTES; a ESTREIA roda a cena inteira de qualquer
		// jeito (o log do servidor marca `<- PRIMEIRA VEZ`), e sao quase trinta segundos de corpo
		// piscando. Marcar as duas formas como ja estreadas e a mesma manivela que o jogo usa pra quem
		// ja se transformou uma vez -- nao inventa estado, so pula um filme que ninguem esta medindo.
		// ========================================================================================================
		s.Forma.EstreiaVista.Add(Jandirus.Core.Forms.Catalogo.Rede("super_perfect"));
		s.Forma.EstreiaVista.Add(Jandirus.Core.Forms.Catalogo.Rede("ssj1"));
		GD.Print($"[biovivo] passo 1: nasceu -- raca '{s.Race}', degrau {s.Ficha.bio_stage} "
			   + $"(larva), corpo {s.Visual.Corpo}, BP {s.Ficha.BP:N0}, canSSJ={s.Ficha.canSSJ}");
	}
}
