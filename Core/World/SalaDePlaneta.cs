namespace Jandirus.Core.World;

/// <summary>Uma faixa da tabela de dilatacao: quantos anos passam la dentro por dia in-game, e o peso dela em cem.</summary>
public readonly record struct FaixaDeDilatacao(double AnosPorDia, int Peso);

/// <summary>Uma Sala do Tempo de planeta na carta: o mundo que a guarda, o endereco dele e a dilatacao dela.</summary>
public readonly record struct SalaNoUniverso(PlanetaNoEspaco Corpo, int Sx, int Sy, int K, double Dilatacao);

/// <summary>Onde a porta ficou num terreno, e como: a celula dela (rocha), a soleira (o chao em frente) e o caminho ate la.</summary>
/// <param name="Passos">Quantos passos a pe do pouso ate a soleira.</param>
/// <param name="Erguida">Verdadeiro quando nao havia parede de montanha alcancavel e o gerador ergueu um rochedo pra ela.</param>
public readonly record struct PortaNoTerreno(int X, int Y, int Passos, bool Erguida)
{
	/// <summary>A SOLEIRA: a celula de chao logo ao SUL da porta. E dali que se entra, e e pra la que a sala devolve.</summary>
	public (int X, int Y) Soleira => (X, Y + 1);
}

/// <summary>
/// AS SALAS DO TEMPO DOS MUNDOS SORTEADOS -- quais mundos tem uma, quanto o tempo dilata nela e onde fica a porta.
///
/// ============================ O PEDIDO DO DONO (2026-10-11) ============================
/// *"planetas procedurais possam ter de forma BEM RARA uma sala do tempo (vai ser uma porta na parede de uma
/// montanha ou coisa do tipo) essas salas do tempo procedurais vao ter uma dilataçã temporal aleatorio, tipo, na
/// terra 1 dia fora é 1 ano la dentro (multiplicador de ganhos do jogo considera isso), mas na procedural pode ter
/// uma q 1 dia fora sao 3 meses la dentro (ganhos menores mas ainda bem maiores q o normal), e com um limitr
/// altissimo q seria a melhor sala do tempo possivel 1 dia fora sao 5 anos la dentro, ganho de bp absurdo mas
/// envelhece 5 anos mt rapidamente"*.
///
/// NAO HA NADA DISTO NO DM: la a Sala e uma, a da Terra (`Modules/Globals/TimeChamber.dm`). E regra nova, do dono,
/// em cima da Sala que o port ja tem (<see cref="SalaDoTempo"/>).
/// ======================================================================================
///
/// ============================ TUDO AQUI E FUNCAO PURA DA SEMENTE ============================
/// "Este mundo tem uma sala?", "quanto ela dilata?" e "em que celula fica a porta?" saem da seed do mundo, e de
/// mais nada: o mesmo mundo tem a mesma sala, a mesma porta e a mesma dilatacao em qualquer maquina e depois de
/// qualquer reinicio, sem um byte em disco. E o desenho da <see cref="AtmosferaDivina"/> -- o servidor pergunta
/// aqui pra por a porta de pe e cobrar a sessao, e o cliente pergunta aqui pra escrever a linha da aba World.
///
/// TRES SORTEIOS, TRES SAIS: ter a sala, a dilatacao dela e o desempate da porta nao podem sair correlacionados
/// (nem com o bioma, a gravidade ou a atmosfera, que tem os sais deles).
/// ==========================================================================================
///
/// ============================ O INTERIOR E O QUARTO QUE JA EXISTE ============================
/// Nenhum mapa novo: cada sala de planeta e uma INSTANCIA do z13 (o quarto e o vazio branco sem borda), como o
/// interior de um Majin e uma instancia da planta dele. A zona e `ZoneKey.Interior(SalaDoTempo.Zona, seed do
/// mundo)` (<see cref="Interior"/>): o NOME e o da Sala da Terra de proposito -- o catalogo de mapas resolve zona
/// pelo nome (`ZoneCatalog.Get(ZoneKey)`), entao as duas pontas carregam a cena, a colisao sem borda, as
/// passagens e a ficha de planeta (gravidade 10x, os climas) pelo caminho que ja existia --, e o que separa a
/// sala de um mundo da de outro, e todas elas da da Terra, e o TIPO e a SEED da chave.
///
/// QUEM RESPONDE "que sala e esta?" e o <see cref="SalaDoTempo"/> (`EhASala` = a da Terra, `EhDePlaneta` = uma
/// destas, `EhUmaSala` = qualquer uma): a separacao mora la, num lugar so.
/// ===========================================================================================
/// </summary>
public static class SalaDePlaneta
{
	// =====================================================================
	// A RARIDADE
	// =====================================================================
	/// <summary>
	/// UM MUNDO SORTEADO EM CADA QUATROCENTOS TEM UMA SALA DO TEMPO. *"De forma BEM RARA"*, nas palavras do dono --
	/// quatro vezes mais rara que a atmosfera divina (<see cref="AtmosferaDivina.UmEm"/> = 100), e sem garantia
	/// perto da Terra: a Terra ja tem a dela.
	///
	/// O QUE O NUMERO DA NA CARTA, MEDIDO pela `--salasteste` (2026-10-11; 62% das celulas tem sistema, 5,5 mundos
	/// por sistema): uma sala a cada ~111 celulas de sistema -- 365 nas 40.401 celulas (201x201) em volta da Terra,
	/// das quais 6 de 5 anos. Em 300 universos a mais perto da Terra fica em media a 5,3 celulas (36 minutos de voo
	/// a pe, um minuto numa nave no teto de velocidade), em 9 de cada 10 universos a menos de 9,4, e no pior a 15,7;
	/// a de 5 ANOS mais perto fica em media a 37 celulas (mais de 4 horas de voo a pe), no pior a 92. Como a
	/// distancia anda com a raiz da raridade, dobrar este numero poe a mais perto a ~7,5 celulas; cortar pela
	/// metade, a ~3,8.
	/// </summary>
	public const int UmEm = 400;

	/// <summary>
	/// DEFEITO INJETADO (bancada --salasteste, e o "antes" dela, `--salasantes`): nenhum mundo tem sala -- o jogo
	/// de antes de 2026-10-11. Lido pela propria regra, em <see cref="Tem(ulong,int)"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool NenhumaSalaDeTeste;

	// SAIS -- um por sorteio. Nenhum e `0x9E3779B9...` (o `Espaco.Misturar` ja faz `seed ^ 0x9E37...` na primeira
	// linha, e repetir a constante devolveria a seed crua), nem o da atmosfera, o da gravidade ou os do relevo.
	private const ulong SalDaSala = 0xBB67AE8584CAA73BUL;
	private const ulong SalDaDilatacao = 0x3C6EF372FE94F82BUL;
	private const ulong SalDaPorta = 0xA54FF53A5F1D36F1UL;

	/// <summary>O peso de um mundo no sorteio da sala: o hash da seed dele com o sal daqui.</summary>
	public static ulong Peso(ulong seedDoMundo) => Espaco.Misturar(seedDoMundo ^ SalDaSala, 0, 0);

	/// <summary>
	/// ESTE MUNDO TEM UMA SALA DO TEMPO? So a seed dele entra. O segundo argumento e o botao da bancada (um mundo
	/// em cada tantos), pelo motivo da `RegraDaAtmosfera`: com 1 ela mede a regra da porta em qualquer terreno, em
	/// vez de procurar entre quatrocentos o unico que a teria. O jogo nunca o passa.
	/// </summary>
	public static bool Tem(ulong seedDoMundo, int umEm = UmEm) =>
		!NenhumaSalaDeTeste && umEm > 0 && Peso(seedDoMundo) % (ulong)umEm == 0;

	/// <summary>A mesma pergunta pela zona: so mundo SORTEADO pode ter (o espaco tambem e `KindProcedural`, e fica de fora).</summary>
	public static bool Tem(ZoneKey zona) =>
		zona.Kind == ZoneKey.KindProcedural && !Espaco.EhEspaco(zona) && Tem(zona.Seed);

	/// <summary>A mesma pergunta pelo corpo da carta estelar.</summary>
	public static bool Tem(PlanetaNoEspaco p) => !p.Premade && Tem(p.Seed);

	// =====================================================================
	// A DILATACAO
	// =====================================================================
	/// <summary>A dilatacao da Sala da TERRA: um ano la dentro por dia in-game. E a regua de todas as outras.</summary>
	public const double DilatacaoDaTerra = 1;

	/// <summary>A pior e a melhor sala que existe: 3 meses e 5 anos por dia (as duas pontas que o dono citou).</summary>
	public const double DilatacaoMinima = 0.25, DilatacaoMaxima = 5;

	/// <summary>
	/// A TABELA "DILATACAO -> CHANCE", em cem. *"As melhores tem de ser as mais raras"*: os pesos so descem.
	///
	///     anos por dia   la dentro, num dia daqui   chance   uma em cada N salas
	///        0,25              3 meses               34%          2,9
	///        0,5               6 meses               26%          3,8
	///        1                 1 ano (a da Terra)    18%          5,6
	///        2                 2 anos                11%          9,1
	///        3                 3 anos                 6%         16,7
	///        4                 4 anos                 3%         33,3
	///        5                 5 anos (o teto)        2%         50
	///
	/// SEIS EM CADA DEZ SAO PIORES QUE A DA TERRA (3 e 6 meses), e isso e o pedido: *"ganhos menores mas ainda bem
	/// maiores q o normal"* -- o que elas tem a mais que a da Terra e nao pedir a chave do Guardiao. Com a raridade
	/// de hoje, a de 5 anos e um mundo em cada VINTE MIL.
	///
	/// VALORES DE TABELA, e nao um numero continuo, por dois motivos: a frase que o jogador le e inteira ("3
	/// meses", "2 anos"), e o envelhecimento da sessao fecha em anos inteiros em todas menos a de 3 meses (ver
	/// <see cref="SalaDoTempo.AnosFechados"/>).
	/// </summary>
	public static readonly FaixaDeDilatacao[] Tabela =
	[
		new(0.25, 34),
		new(0.5, 26),
		new(1, 18),
		new(2, 11),
		new(3, 6),
		new(4, 3),
		new(5, 2),
	];

	/// <summary>A soma dos pesos da <see cref="Tabela"/>. A bancada confere.</summary>
	public const int PesoTotal = 100;

	/// <summary>
	/// DEFEITO INJETADO (bancada --salasteste): toda sala de planeta dilata como a da Terra -- o sorteio da
	/// dilatacao nao existe. Lido pela propria regra, em <see cref="Dilatacao"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool DilatacaoFixaDeTeste;

	/// <summary>
	/// QUANTOS ANOS PASSAM NESTA SALA POR DIA IN-GAME. So a seed do mundo entra, com sal proprio.
	///
	/// Vale pra qualquer seed (a pergunta "ha sala?" e do <see cref="Tem(ulong,int)"/>): e o que deixa a bancada
	/// medir a tabela em cem mil seeds sem depender da raridade.
	/// </summary>
	public static double Dilatacao(ulong seedDoMundo)
	{
		if (DilatacaoFixaDeTeste) return DilatacaoDaTerra;

		int r = (int)(Espaco.Misturar(seedDoMundo ^ SalDaDilatacao, 0, 0) % PesoTotal), soma = 0;
		foreach (FaixaDeDilatacao f in Tabela)
		{
			soma += f.Peso;
			if (r < soma) return f.AnosPorDia;
		}
		return Tabela[0].AnosPorDia;
	}

	/// <summary>
	/// A DILATACAO NA BOCA DO JOGO: "3 meses", "1 ano", "5 anos" -- o que passa la dentro num dia in-game daqui.
	/// E do LUGAR, e nao de corpo nenhum: pode aparecer na tela (a regra do BP nao a alcanca).
	/// </summary>
	public static string TextoDaDilatacao(double anosPorDia)
	{
		if (anosPorDia < 1) return $"{anosPorDia * 12:0.#} meses";
		return anosPorDia == 1 ? "1 ano" : $"{anosPorDia:0.##} anos";
	}

	// =====================================================================
	// A ZONA
	// =====================================================================
	/// <summary>
	/// A ZONA DO INTERIOR da sala deste mundo. UMA por mundo, e a mesma em toda entrada: quem entra pela mesma
	/// porta se encontra la dentro.
	///
	/// A SEED DA CHAVE E A DO MUNDO, e e isso que dispensa registro: de dentro da sala o servidor sabe a
	/// dilatacao (<see cref="Dilatacao"/> da seed da zona) e, com o nome do mundo que o personagem guarda desde a
	/// porta, reconstroi a zona de fora (<see cref="MundoDe"/>).
	/// </summary>
	public static ZoneKey Interior(ulong seedDoMundo) => ZoneKey.Interior(SalaDoTempo.Zona, seedDoMundo);

	/// <summary>O interior da sala do mundo em que esta zona fica. So faz sentido pra mundo sorteado.</summary>
	public static ZoneKey Interior(ZoneKey mundo) => Interior(mundo.Seed);

	/// <summary>
	/// O MUNDO EM QUE A PORTA DESTA SALA FICA. O nome vem de fora porque a chave de um mundo gerado e nome + seed,
	/// e a seed sozinha identifica mas nao enderece (ver <see cref="ChaveDePlaneta"/>).
	/// </summary>
	public static ZoneKey MundoDe(ZoneKey sala, string nomeDoMundo) => ZoneKey.Procedural(nomeDoMundo, sala.Seed);

	// =====================================================================
	// A PORTA
	// =====================================================================
	/// <summary>
	/// A que distancia do pouso, NO MINIMO, a porta prefere ficar -- em passos a pe. Dez passos: fora da clareira
	/// do pouso e da sombra da macieira da chegada. Se nenhuma parede fica alem disso, vale a mais perto.
	/// </summary>
	public const int PassosMinimosDoPouso = 10;

	/// <summary>Quantas celulas a porta e a soleira guardam da beirada do mapa (a mesma folga do pomar).</summary>
	public const int MargemDaBeirada = Pomar.MargemDaBeirada;

	/// <summary>
	/// O TERREIRO: o retangulo de chao em frente a porta que fica SEM arvore nem planta -- duas colunas pra cada
	/// lado da porta, cinco fileiras ao sul dela.
	///
	/// A PORTA TEM DE SE VER. A arvore deste jogo desenha por cima do que esta atras dela: a copa sobe mais de tres
	/// tiles a partir da base e sobra pros lados. Na primeira foto da `--diagsalas` a porta estava la, na rocha, e
	/// nao aparecia -- um pinheiro com a base tres fileiras ao sul a cobria inteira, e a copa de outra arvore cobria
	/// quem estava de pe na soleira. Cinco fileiras e o que tira da frente toda copa que alcanca a porta ou a soleira.
	/// </summary>
	public const int LadoDoTerreiro = 2, FundoDoTerreiro = 5;

	/// <summary>
	/// DEFEITO INJETADO (bancada --salasteste): o terreiro nao e limpo -- a porta nasce no meio do mato, atras das
	/// copas. Lido pela propria regra, em <see cref="LimparOTerreiro"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool PortaNoMatoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada --salasteste): a porta nasce na primeira celula de montanha da varredura, sem
	/// perguntar se da pra chegar nela a pe -- dentro de uma cordilheira, do outro lado de um lago. Lido pela
	/// propria regra, em <see cref="AbrirAPorta"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool PortaSemCaminhoDeTeste;

	/// <summary>
	/// ESCOLHE A CELULA DA PORTA num mundo que acabou de ser gerado. Nulo quando o mundo nao tem sala.
	///
	/// Roda DENTRO do <see cref="GeradorDeTerreno.Gerar"/>, depois do pomar e antes do blob de colisao -- pelo
	/// motivo do <see cref="Pomar.Plantar"/>: ela MEXE no terreno, e o que ela mexe tem de sair igual nas duas
	/// pontas. Sao duas coisas: tira o mato da frente da porta (<see cref="LimparOTerreiro"/>) e, no ultimo recurso,
	/// ERGUE um rochedo pra ela (<see cref="ErguerORochedo"/>). So no mundo que TEM sala: nos outros 399 em cada 400
	/// ela sai na primeira linha, e a assinatura do mundo e a que a seed sempre deu.
	///
	/// ============================ "UMA PORTA NA PAREDE DE UMA MONTANHA" ============================
	/// A porta e uma celula de MONTANHA (que ja barra) cuja vizinha do SUL -- a SOLEIRA -- e chao firme a que se
	/// chega A PE desde o pouso: a arte da porta olha pro sul, e quem chega fica de frente pra ela. A conta de
	/// "chegar a pe" e uma inundacao do mapa a partir do pouso, em cruz (o corpo nao passa entre duas paredes que
	/// so se tocam pelo canto), por tudo o que nao e parede, agua nem macieira do pomar.
	///
	/// TRES PREFERENCIAS, da melhor pra pior:
	///   2. PAREDE DE VERDADE: rocha dos dois lados da porta e atras dela -- a porta fica encravada na cordilheira;
	///   1. rocha atras dela (um contraforte);
	///   0. qualquer celula de montanha com soleira alcancavel (uma pedra solta).
	/// Dentro da preferencia: a mais perto do pouso que fique alem de <see cref="PassosMinimosDoPouso"/> (a pe);
	/// se nenhuma fica, a mais perto. O desempate e o hash da seed, e nao a ordem da varredura.
	///
	/// PERTO DO POUSO DE PROPOSITO: o raro e o MUNDO (um em <see cref="UmEm"/>). Quem ja esta nele acha a porta
	/// andando, sem varrer um mapa de ate mil tiles de lado.
	/// ==============================================================================================
	///
	/// PUBLICA POR CAUSA DA BANCADA: ela entrega aqui o terreno de qualquer seed com `umEm: 1`, e mede a regra em
	/// centenas de mundos em vez de so nos raros.
	/// </summary>
	/// <param name="macieiras">As celulas do pomar: viram obra densa, e nao estao no `bits`.</param>
	public static PortaNoTerreno? AbrirAPorta(int w, int h, byte[] chao, byte[] cobertura, byte[] bits, byte[] agua,
											  int sx, int sy, ulong seed, (int X, int Y)[] macieiras, int umEm = UmEm)
	{
		if (!Tem(seed, umEm)) return null;

		var pomar = new HashSet<int>();
		foreach ((int mx, int my) in macieiras) pomar.Add(my * w + mx);

		bool Parede(int i) => (bits[i >> 3] & (1 << (i & 7))) != 0;
		bool Montanha(int x, int y) =>
			x >= 0 && y >= 0 && x < w && y < h && chao[y * w + x] == (byte)ClasseDeTerreno.Montanha && Parede(y * w + x);

		// ---- 1. AONDE SE CHEGA A PE, e em quantos passos ----
		int[] passos = new int[w * h];
		Array.Fill(passos, -1);
		int[] fila = new int[w * h];
		int cabeca = 0, cauda = 0;
		passos[sy * w + sx] = 0;
		fila[cauda++] = sy * w + sx;
		while (cabeca < cauda)
		{
			int k = fila[cabeca++];
			int x = k % w, y = k / w;
			foreach ((int dx, int dy) in PassosEmCruz)
			{
				int nx = x + dx, ny = y + dy;
				if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
				int n = ny * w + nx;
				if (passos[n] >= 0 || Parede(n) || chao[n] == (byte)ClasseDeTerreno.Agua || pomar.Contains(n)) continue;
				passos[n] = passos[k] + 1;
				fila[cauda++] = n;
			}
		}

		// ---- 2. A MELHOR SOLEIRA ----
		const int m = MargemDaBeirada;
		PortaNoTerreno? melhor = null;
		(int Preferencia, int Perto, int Passos, ulong Hash) notaDoMelhor = default;

		for (int y = m + 1; y < h - m; y++)
		{
			for (int x = m; x < w - m; x++)
			{
				int soleira = y * w + x;
				int caminho = passos[soleira];
				if (!PortaSemCaminhoDeTeste && caminho < 0) continue;
				if (!Montanha(x, y - 1)) continue;

				// A SOLEIRA E CHAO FIRME E LIMPO: nem agua, nem planta de colher, nem o desenho de uma rocha
				// grande por cima dela ou da porta (a pedra de 250 px cobriria as duas).
				if (!PortaSemCaminhoDeTeste)
				{
					if (cobertura[soleira] != (byte)CoberturaDeTerreno.Nada) continue;
					if ((ClasseDeTerreno)chao[soleira] is not (ClasseDeTerreno.Praia or ClasseDeTerreno.Planicie or ClasseDeTerreno.Colina))
						continue;
					if (RochaGrande.Desenha(w, h, cobertura, x, y) || RochaGrande.Desenha(w, h, cobertura, x, y - 1)) continue;
					// ...nem com uma macieira do pomar no terreiro: ela e obra do servidor (nao sai daqui), e a copa cobriria a porta.
					if (MacieiraNoTerreiro(w, pomar, x, y - 1)) continue;
				}

				bool atras = Montanha(x, y - 2);
				int preferencia = atras && Montanha(x - 1, y - 1) && Montanha(x + 1, y - 1) ? 2 : atras ? 1 : 0;

				// MAIOR E MELHOR nos dois primeiros campos; MENOR e melhor nos dois ultimos.
				var nota = (preferencia, caminho >= PassosMinimosDoPouso ? 1 : 0, caminho,
							Espaco.Misturar(seed ^ SalDaPorta, (ulong)(uint)x, (ulong)(uint)y));
				if (melhor != null && !Vence(nota, notaDoMelhor)) continue;

				notaDoMelhor = nota;
				melhor = new PortaNoTerreno(x, y - 1, Math.Max(caminho, 0), Erguida: false);

				// O DEFEITO PARA NA PRIMEIRA: e a "primeira celula de montanha da varredura".
				if (PortaSemCaminhoDeTeste) return melhor;
			}
		}

		PortaNoTerreno porta = melhor ?? ErguerORochedo(w, h, chao, cobertura, bits, agua, sx, sy, passos, pomar);
		LimparOTerreiro(w, h, chao, cobertura, bits, porta);
		return porta;
	}

	/// <summary>Ha uma macieira do pomar no terreiro da porta que ficaria nesta celula?</summary>
	private static bool MacieiraNoTerreiro(int w, HashSet<int> pomar, int portaX, int portaY)
	{
		if (pomar.Count == 0) return false;
		for (int y = portaY + 1; y <= portaY + FundoDoTerreiro; y++)
			for (int x = portaX - LadoDoTerreiro; x <= portaX + LadoDoTerreiro; x++)
				if (x >= 0 && y >= 0 && x < w && pomar.Contains(y * w + x)) return true;
		return false;
	}

	/// <summary>
	/// TIRA O MATO DA FRENTE DA PORTA: as arvores e as plantas do <see cref="LadoDoTerreiro"/> x <see cref="FundoDoTerreiro"/>
	/// ao sul dela saem do mapa (a cobertura e a parede de cada uma, juntas -- o cliente nao pode desenhar tronco
	/// onde o servidor ja deixa passar). E a operacao da clareira do pouso (`GeradorDeTerreno.LimparEmVolta`), em
	/// frente a porta.
	///
	/// A ROCHA GRANDE FICA: a porta e a soleira ja foram escolhidas fora do desenho de qualquer uma, e a celula de
	/// arvore que cai debaixo da pegada de uma pedra continua barrando (o bit e da pedra).
	/// </summary>
	private static void LimparOTerreiro(int w, int h, byte[] chao, byte[] cobertura, byte[] bits, PortaNoTerreno porta)
	{
		if (PortaNoMatoDeTeste || PortaSemCaminhoDeTeste) return;

		for (int y = porta.Y + 1; y <= porta.Y + FundoDoTerreiro; y++)
			for (int x = porta.X - LadoDoTerreiro; x <= porta.X + LadoDoTerreiro; x++)
			{
				if (x < 0 || y < 0 || x >= w || y >= h) continue;
				int i = y * w + x;
				if ((CoberturaDeTerreno)cobertura[i] is not (CoberturaDeTerreno.Arvore or CoberturaDeTerreno.ArvoreAcento or CoberturaDeTerreno.Planta))
					continue;

				cobertura[i] = (byte)CoberturaDeTerreno.Nada;
				if (chao[i] != (byte)ClasseDeTerreno.Montanha && !RochaGrande.Cobre(w, h, cobertura, x, y))
					bits[i >> 3] &= (byte)~(1 << (i & 7));
			}
	}

	private static bool Vence((int Preferencia, int Perto, int Passos, ulong Hash) a,
							  (int Preferencia, int Perto, int Passos, ulong Hash) b)
	{
		if (a.Preferencia != b.Preferencia) return a.Preferencia > b.Preferencia;
		if (a.Perto != b.Perto) return a.Perto > b.Perto;
		if (a.Passos != b.Passos) return a.Passos < b.Passos;
		return a.Hash < b.Hash;
	}

	private static readonly (int Dx, int Dy)[] PassosEmCruz = [(1, 0), (-1, 0), (0, 1), (0, -1)];

	/// <summary>Ate quantos aneis em volta do pouso o rochedo procura lugar.</summary>
	private const int RaioDoRochedo = 48;

	/// <summary>
	/// O ULTIMO RECURSO: o mundo tem sala e nao tem parede de montanha a que se chegue a pe (um mundo sem
	/// cordilheira ao alcance, um pouso ilhado). ERGUE um rochedo de tres celulas de largura por duas de altura no
	/// chao alcancavel mais perto do pouso, e a porta e a celula do meio da fileira de baixo.
	///
	/// E A OPERACAO INVERSA DA CLAREIRA ESCAVADA (`GeradorDeTerreno.LimparEmVolta` com `cavar`), e mexe nas quatro
	/// grades pelo mesmo motivo: o cliente nao pode pintar campo onde o servidor ja pos rocha. Sem ela a promessa
	/// teria um buraco -- a aba World diz "este mundo guarda uma Sala do Tempo" olhando so a seed, e num universo
	/// que nao acaba o raro acontece.
	///
	/// SO EM CHAO LIMPO: as seis celulas e a soleira sem cobertura (arvore, planta, rocha grande) e fora do pomar;
	/// a tres tiles ou mais do pouso, pra nao fechar a clareira de quem chega. Se nem assim couber (um mundo todo
	/// mar), o rochedo sobe logo ao norte do pouso, por cima do que houver.
	/// </summary>
	private static PortaNoTerreno ErguerORochedo(int w, int h, byte[] chao, byte[] cobertura, byte[] bits, byte[] agua,
												 int sx, int sy, int[] passos, HashSet<int> pomar)
	{
		const int m = MargemDaBeirada;

		bool Cabe(int x, int y)
		{
			// (x, y) e a SOLEIRA; o rochedo ocupa (x-1..x+1, y-2..y-1).
			if (x - 1 < m || x + 1 >= w - m || y - 2 < m || y >= h - m) return false;
			if (passos[y * w + x] < 0 || cobertura[y * w + x] != (byte)CoberturaDeTerreno.Nada || pomar.Contains(y * w + x)) return false;
			if (RochaGrande.Desenha(w, h, cobertura, x, y)) return false;
			for (int ry = y - 2; ry <= y - 1; ry++)
				for (int rx = x - 1; rx <= x + 1; rx++)
				{
					int i = ry * w + rx;
					if (Math.Max(Math.Abs(rx - sx), Math.Abs(ry - sy)) < 3) return false;
					if (cobertura[i] != (byte)CoberturaDeTerreno.Nada || pomar.Contains(i)) return false;
					if (RochaGrande.Desenha(w, h, cobertura, rx, ry)) return false;
				}
			return true;
		}

		(int X, int Y) soleira = (Math.Clamp(sx, m + 1, w - m - 2), Math.Clamp(sy - 2, m + 2, h - m - 1));
		bool achou = false;
		for (int r = 3; r <= RaioDoRochedo && !achou; r++)
			for (int d = -r; d <= r && !achou; d++)
				foreach ((int x, int y) in new[] { (sx + d, sy - r), (sx + d, sy + r), (sx - r, sy + d), (sx + r, sy + d) })
				{
					if (x < 0 || y < 0 || x >= w || y >= h || !Cabe(x, y)) continue;
					soleira = (x, y);
					achou = true;
					break;
				}

		for (int ry = soleira.Y - 2; ry <= soleira.Y - 1; ry++)
			for (int rx = soleira.X - 1; rx <= soleira.X + 1; rx++)
			{
				int i = ry * w + rx;
				chao[i] = (byte)ClasseDeTerreno.Montanha;
				cobertura[i] = (byte)CoberturaDeTerreno.Nada;
				bits[i >> 3] |= (byte)(1 << (i & 7));
				agua[i >> 3] &= (byte)~(1 << (i & 7));
			}

		// A SOLEIRA TEM DE SER CHAO, mesmo quando o rochedo subiu por cima de um mar (o caso sem lugar).
		int s = soleira.Y * w + soleira.X;
		if (!achou)
		{
			chao[s] = (byte)ClasseDeTerreno.Planicie;
			cobertura[s] = (byte)CoberturaDeTerreno.Nada;
			bits[s >> 3] &= (byte)~(1 << (s & 7));
			agua[s >> 3] &= (byte)~(1 << (s & 7));
		}

		return new PortaNoTerreno(soleira.X, soleira.Y - 1, Math.Max(passos[s], 0), Erguida: true);
	}

	// =====================================================================
	// A VARREDURA -- pro admin e pra bancada
	// =====================================================================
	/// <summary>
	/// AS SALAS das celulas de sistema a ate <paramref name="raio"/> de (<paramref name="sx0"/>, <paramref name="sy0"/>).
	/// Monta um planeta por mundo varrido (uma string de nome cada): e consulta de admin e de bancada, nao de tique.
	/// </summary>
	public static List<SalaNoUniverso> NaRegiao(ulong seedDoUniverso, int sx0, int sy0, int raio)
	{
		var l = new List<SalaNoUniverso>();
		for (int sy = sy0 - raio; sy <= sy0 + raio; sy++)
			for (int sx = sx0 - raio; sx <= sx0 + raio; sx++)
			{
				if (Sistemas.Do(seedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					if (k == s.OrbitaPreFeita) continue;
					PlanetaNoEspaco p = s.Planeta(k);
					if (Tem(p.Seed)) l.Add(new SalaNoUniverso(p, sx, sy, k, Dilatacao(p.Seed)));
				}
			}
		return l;
	}

	/// <summary>Quantos mundos SORTEADOS ha na mesma regiao -- o denominador da raridade medida.</summary>
	public static int MundosNaRegiao(ulong seedDoUniverso, int sx0, int sy0, int raio)
	{
		int n = 0;
		for (int sy = sy0 - raio; sy <= sy0 + raio; sy++)
			for (int sx = sx0 - raio; sx <= sx0 + raio; sx++)
				if (Sistemas.Do(seedDoUniverso, sx, sy) is { } s) n += s.Orbitas - (s.OrbitaPreFeita >= 0 ? 1 : 0);
		return n;
	}

	/// <summary>
	/// A QUANTAS CELULAS DA TERRA fica o SISTEMA com sala mais perto, num universo qualquer -- e quantos anos por dia
	/// ela dilata. Negativo = nenhuma ate o raio. SEM montar planeta nenhum (so a seed de cada orbita, pela mesma
	/// conta do <see cref="SistemaSolar.Planeta"/>): e o que deixa a bancada medir centenas de universos em vez de
	/// um so. A distancia e ate a ESTRELA do sistema, em celulas de <see cref="Sistemas.CelulaPx"/>.
	/// </summary>
	public static (double Celulas, double Dilatacao) MaisPertoDaTerra(ulong seedDoUniverso, int raio, double dilatacaoMinima = 0)
	{
		double menor = double.MaxValue, dilatacao = 0;
		for (int sy = -raio; sy <= raio; sy++)
			for (int sx = -raio; sx <= raio; sx++)
			{
				if (Sistemas.Do(seedDoUniverso, sx, sy) is not { } s) continue;
				double d2 = (double)s.Estrela.Pos.X * s.Estrela.Pos.X + (double)s.Estrela.Pos.Y * s.Estrela.Pos.Y;
				if (d2 >= menor) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					if (k == s.OrbitaPreFeita) continue;
					ulong seed = Espaco.Misturar(s.H ^ Sistemas.SalDoPlaneta, (ulong)(uint)k, 0);
					if (!Tem(seed) || Dilatacao(seed) < dilatacaoMinima) continue;
					menor = d2;
					dilatacao = Dilatacao(seed);
					break;
				}
			}
		return menor == double.MaxValue ? (-1, 0) : (Math.Sqrt(menor) / Sistemas.CelulaPx, dilatacao);
	}

	/// <summary>A sala mais perto de um ponto da galaxia entre as celulas em volta, ou nulo se nao ha nenhuma nelas.</summary>
	public static SalaNoUniverso? MaisProxima(ulong seedDoUniverso, Vec2 de, int raioEmCelulas)
	{
		SistemaId c = SistemaId.De(de);
		SalaNoUniverso? melhor = null;
		double menor = double.MaxValue;
		foreach (SalaNoUniverso s in NaRegiao(seedDoUniverso, c.Sx, c.Sy, raioEmCelulas))
		{
			double dx = s.Corpo.Pos.X - (double)de.X, dy = s.Corpo.Pos.Y - (double)de.Y;
			double d2 = dx * dx + dy * dy;
			if (d2 >= menor) continue;
			menor = d2;
			melhor = s;
		}
		return melhor;
	}
}
