using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O ZANZO CLASH -- o duelo de velocidade.
///
/// ============================ O QUE E, E O QUE MUDOU DO ORIGINAL ============================
/// No BYOND, dois lutadores que se acertam no MESMO instante e ambos com Imagem Remanescente
/// entram num embate: os corpos somem, se cruzam rapido demais pra vista, e um deles sai por cima.
/// La quem vencia era sorteio pesado por stat -- o jogador assistia.
///
/// Aqui o dono pediu outra coisa, e melhor: "ambos os jogadores vao ter q fazer um quick time
/// event que vai aparecer na tela pra clicar em teclas aleatorias do teclado... e o server vendo
/// qual cliente acertou mais o quick time event, ele determina o vencedor". O embate deixou de ser
/// uma animacao e virou uma DISPUTA -- e uma que o jogador mais fraco pode ganhar se for mais
/// rapido no teclado.
/// ============================================================================================
///
/// ============================ COMO SE JOGA ============================
///   1. Os dois SOMEM (a mesma invisibilidade da tecnica -- um caminho so pra sumir).
///   2. A cada fracao de segundo os dois TELEPORTAM pra um ponto novo em volta do meio do
///      caminho, e cada cruzamento solta um baque e RACHA O CHAO. Sem miragem: isto nao e
///      Zanzoken, e o dono foi explicito ("esses teleporte n sao zanzoken entao n tem after
///      image") -- o que se ve e o estrago, nao o corpo.
///   3. Cada um recebe uma LETRA na tela com prazo. Acertou, ponto; errou ou deixou vencer, nada.
///   4. No fim os dois reaparecem, o VENCEDOR nasce ATRAS do perdedor e desfere um golpe com
///      chance alta de arremesso.
/// =====================================================================
///
/// PONTO NAO E TUDO IGUAL: quem tem mais BP pontua multiplicado pela razao entre os dois poderes
/// (o pedido do dono, literal). Ver <see cref="TetoDaVantagem"/> -- ha um teto, e ele existe por
/// uma razao que esta escrita la.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// OS NUMEROS
	// =====================================================================
	// Os numeros abaixo sao os do DM, decimo por decimo. So o do quick time event e novo -- porque
	// o quick time event e novo.

	/// <summary>Quantos cruzamentos o embate tem. `var/blinks = rand(6, 9)`.</summary>
	private const int CruzamentosMin = 6, CruzamentosMax = 9;

	/// <summary>Quanto dura cada cruzamento. `sleep(rand(5,7))` -- decimos de segundo.</summary>
	private const long MsPorCruzamentoMin = 500, MsPorCruzamentoMax = 700;

	/// <summary>
	/// O RAIO DO SALTO: 6 tiles.
	///
	/// No DM e `max(5, min(haszanzo, M.haszanzo) * 2 + 4)`, e o comentario de la promete "quanto
	/// maior o nivel do Zanzoken, mais largos os saltos". A promessa e MORTA: `haszanzo` e 0 ou 1
	/// (a propria skill so liga e desliga), entao o `min()` e sempre 1 e a conta da 6, sempre.
	/// Portado como a constante que ele de fato e.
	/// </summary>
	private const float RaioDoEmbate = 6 * ZoneCollision.TileSize;

	/// <summary>
	/// O QUE CADA CRUZAMENTO COBRA dos dois: 1,5% do Ki maximo. `Ki = max(Ki - MaxKi*0.015, 0)`.
	///
	/// E o embate ACABA se qualquer um nao tiver mais isso pra pagar -- no DM e um `break` no laco.
	/// Ate 9 cruzamentos: o embate inteiro custa ate 13,5% do Ki de cada um.
	/// </summary>
	private const double CustoDeKiPorCruzamento = 0.015;

	/// <summary>
	/// A CHANCE, uma vez passadas todas as condicoes: `if(prob(50)) spawn ZanzoClash(M)`.
	///
	/// Ela existe pra que trocar socos com outro que tem Imagem Remanescente nao vire embate TODA
	/// vez -- o encontro tem que ser um acontecimento, nao o jeito normal de brigar.
	/// </summary>
	private const int ChanceDeEmbate = 50;

	/// <summary>
	/// UM SORTEIO POR TROCA, e nao um por tique. `zanzoClashRollCD = 10` decimos.
	///
	/// Sem isto uma sequencia sustentada de socos sortearia dezenas de vezes por segundo e o
	/// `prob(50)` viraria certeza.
	/// </summary>
	private const long MsEntreSorteios = 1000;

	/// <summary>
	/// A CHANCE DE O CENARIO CAIR em cada cruzamento, e o raio em celulas em volta do baque.
	///
	/// ============================ CHAO NAO E CENARIO ============================
	/// O `RacharChao` -- que roda em TODO cruzamento -- muda o DESENHO do chao com `prob(40)` por
	/// celula. Ele derruba um objeto que bloqueia so quando o sorteio calha justo na celula dele,
	/// que num cruzamento no meio de um campo aberto quase nunca acontece: o dono via a onda de
	/// choque estourar ao lado de uma arvore e a arvore continuar la.
	///
	/// Isto aqui e a outra metade, e e o pedido dele: "faça o cenario ter chance de quebrar a cada
	/// teleporte no local das ondas de choque (n e sempre mas tem uma boa chance)". Um sorteio por
	/// cruzamento; passando, o que BLOQUEIA em volta do baque cai pelo `DerrubarCenario` -- o mesmo
	/// caminho do muro em que um corpo arremessado se espatifa.
	/// ============================================================================
	/// </summary>
	private const double ChanceDeQuebrarCenario = 0.65;
	private const int RaioDoEstrago = 2;

	/// <summary>
	/// A CHANCE DE OS DOIS APARECEREM num cruzamento, e por quanto tempo.
	///
	/// ============================ O VISLUMBRE ============================
	/// Pedido do dono: "adicione uma chance q as vezes os personagens podem aparecer em alguns dos
	/// teleportes e outros eles continuam invisivel... e eles sempre estao fazendo animaçao de socar
	/// nessas vezes q eles aparecem".
	///
	/// E o que faz o embate ter CARA de briga em vez de fogos de artificio: por um quinto de segundo
	/// os dois piscam na tela ja se encarando e no meio de um golpe, e somem antes que a vista
	/// termine de reconhecer o que viu. Sempre aparecer viraria uma luta normal com teleporte; nunca
	/// aparecer nao mostra que ha DOIS lutadores ali.
	///
	/// O TEMPO tem piso: o snapshot sai a 30 Hz, entao menos de ~70 ms poderia cair inteiro entre
	/// dois pacotes e nao ser visto por ninguem de fora. 220 ms da tres ou quatro quadros de rede --
	/// um piscar, mas um piscar que chega.
	/// =====================================================================
	/// </summary>
	private const double ChanceDeAparecer = 0.40;
	private const long MsDeVislumbre = 220;

	/// <summary>
	/// A JANELA DE CADA LETRA -- quanto tempo voce tem pra responder. (Novo: nao ha quick time event
	/// no DM.)
	///
	/// ============================ ELA E PRAZO; A CADENCIA E O PISO ============================
	/// **Ela ja foi as duas coisas, e era esse o defeito.** A letra seguinte so nascia quando esta
	/// janela fechava, entao acertar em 100 ms e acertar em 890 ms davam o mesmo ponto no mesmo
	/// instante: com ~350 ms de reacao, o jogador passava 61% do embate olhando um anel vazio. O dono
	/// pediu o contrario com todas as letras -- *"faca com q caso vc ACERTE o quick time event, ele JA
	/// VAI PRO PROXIMO BOTAO pra apertar, entao realmente QUANTO MAIS RAPIDO VC FOR MELHOR VAI SER"*.
	///
	/// A RAZAO DE A JANELA SER CADENCIA CONTINUA VALENDO, e esta escrita: na primeira versao a letra
	/// certa pedia outra NA HORA, e a bancada mediu 55 letras em 5,4 segundos e 424 pontos num embate
	/// so -- *"um quick time event que premia velocidade de DIGITACAO mede a maquina do jogador, e a um
	/// cliente automatico entrega a vitoria de graca"*.
	///
	/// A RESPOSTA NAO E O METRONOMO: e um PISO (ver <see cref="MsMinimoEntreLetras"/>), e ela e do
	/// original. O `BeamClash.dm:24` resolve o mesmo abuso com um teto de cadencia
	/// (`#define BCL_PRESS_TICK_CAP 5 // apertos aceitos por tick (anti-macro: 5/0.2s = 25/s)`) e nao
	/// travando o jogador num compasso. Com piso, ser rapido paga de verdade ate o limite da mao
	/// humana, e alem dele nao paga mais nada -- que e exatamente onde a maquina deixaria de competir
	/// com a pessoa.
	///
	/// NAO E `const` PELO MESMO MOTIVO DO PISO -- ver <see cref="MsMinimoEntreLetras"/>: o defeito
	/// injetado e "piso = janela", e ele precisa que as duas sejam legiveis e devolviveis.
	/// ==========================================================================================
	/// </summary>
	private static long MsPorTecla = 900;

	/// <summary>
	/// O PISO DA CADENCIA: o minimo entre o NASCIMENTO de uma letra e o da proxima. **Vale nos dois
	/// embates** (o de velocidade e o de ki), como o alfabeto e a janela.
	///
	/// ============================ POR QUE MEDIDO DO NASCIMENTO, E NAO DO ACERTO ============================
	/// Do ACERTO, a cadencia seria "reacao + piso": um cliente automatico (reacao zero) receberia
	/// letra a cada 300 ms e uma pessoa de 250 ms a cada 550 -- quase o dobro de letras pra maquina, e
	/// o abuso voltava pela porta dos fundos. Do NASCIMENTO, a cadencia e `max(piso, a sua reacao)`:
	/// acima do piso cada milissegundo economizado vira letra, e abaixo dele **todo mundo empata**.
	/// E o `BCL_PRESS_TICK_CAP` na forma exata: um TETO de vazao, e nao um compasso.
	///
	/// 300 ms e o limite de quem esta LENDO uma letra e achando a tecla (o reflexo puro fica em ~250).
	/// A conta do embate de velocidade: 3,0 a 6,3 s de duracao viram 10 a 21 letras pra quem joga no
	/// limite, contra as 4 a 8 do metronomo -- e contra as 55 do abuso que a bancada mediu.
	/// ====================================================================================================
	///
	/// ============================ POR QUE ELE (E A JANELA) NAO SAO `const` ============================
	/// Porque o defeito que estas duas linhas existem pra impedir tem NOME e tem FORMA: e o metronomo
	/// de antes, e ele se escreve **piso = janela** -- com os dois iguais, o `-=` do
	/// <see cref="TeclaDoEmbate"/> subtrai zero e o adiantamento deixa de existir, exatamente como
	/// era. Sendo campo, a bancada (`--pressateste`) poe o jogo de antes na frente de cada afirmacao
	/// e exige que ela fique VERMELHA; sendo `const`, o pedido do dono ficaria com quatro checagens
	/// verdes que nao sabem ficar vermelhas.
	///
	/// **Ninguem alem da bancada escreve aqui**, e ela devolve no `finally` -- a mesma disciplina do
	/// `_clashSempre` e do `_comTecladoDeTeste`.
	/// ============================================================================================
	/// </summary>
	private static long MsMinimoEntreLetras = 300;

	/// <summary>
	/// A JANELA DO "AO MESMO TEMPO": `zanzoClashWindow = 7` decimos. Dois socos dentro dela contam
	/// como troca simultanea.
	///
	/// Larga o bastante pra caber a latencia de dois clientes (o pacote de um pode chegar 100 ms
	/// depois do outro sem que ninguem tenha sido mais lento) e curta o bastante pra nao pegar
	/// dois golpes que foram claramente um depois do outro.
	/// </summary>
	private const long MsDeTroca = 700;

	/// <summary>
	/// O RESPIRO depois de um embate: `zanzoRollCool = world.time + 30`, 3 segundos.
	///
	/// O comentario do DM diz pra que serve, e vale palavra por palavra aqui: pra dois usuarios de
	/// Imagem Remanescente nao encadearem embate atras de embate numa tempestade de teleporte.
	/// </summary>
	private const long MsDeDescanso = 3_000;

	/// <summary>
	/// O TETO DA VANTAGEM DE PODER.
	///
	/// O pedido foi literal: "o mais forte ganha os pontos multiplicados pela razao entre o bp dele
	/// e o do mais fraco, enquanto o mais fraco ganha o ponto normal". Implementado assim.
	///
	/// O TETO EXISTE porque a razao crua nao tem limite: neste jogo o BP vai de 9 a bilhoes, e um
	/// encontro de faixas diferentes daria multiplicador de milhoes -- o mais fraco perderia mesmo
	/// acertando TODAS as teclas contra um oponente que nao acertou nenhuma, e ai o quick time
	/// event vira enfeite pros dois. Com teto, poder continua decidindo a disputa parelha e ainda
	/// da pra roubar uma vitoria sendo muito mais rapido.
	///
	/// Pra ter a razao pura, e so por isto em `double.MaxValue`.
	/// </summary>
	private const double TetoDaVantagem = 8.0;

	// =====================================================================
	// O ESTADO
	// =====================================================================
	private sealed class Embate
	{
		public required ServerPlayer A, B;
		public long Acaba;
		public long ProximoBaque;

		/// <summary>
		/// OS CRUZAMENTOS, SORTEADOS DE UMA VEZ.
		///
		/// O DM sorteia o proximo `sleep` dentro do laco. Aqui a fila e rolada no comeco pelo mesmo
		/// motivo que a barra do embate precisa de um fim: o cliente recebe a duracao TOTAL junto
		/// com o `Comecou` e desenha a barra com ela. Sorteando a cada tique, a barra so poderia
		/// adivinhar quanto falta.
		/// </summary>
		public readonly Queue<long> Cruzamentos = new();
		public double PtsA, PtsB;
		public double MultA = 1, MultB = 1;

		/// <summary>A letra que cada um tem que apertar agora, e ate quando.</summary>
		public char LetraA, LetraB;
		public long PrazoA, PrazoB;

		/// <summary>
		/// QUANDO O CORPO SEM DONO VAI "APERTAR" A LETRA. Zero = nao ha ninguem pra responder por ele
		/// (o lado e de gente). Ver <see cref="GameServer.ResponderPelaMaquina"/>.
		/// </summary>
		public long RespondeA, RespondeB;

		public Vec2 Meio;

		/// <summary>Ate quando os dois estao a vista neste cruzamento. 0 = invisiveis.</summary>
		public long VislumbreAte;

		/// <summary>
		/// QUEM JA ESTAVA SUMIDO ANTES DO EMBATE.
		///
		/// Existe pra o fim do embate nao ROUBAR a tecnica de quem tinha ligado a invisibilidade
		/// por conta propria: o embate desfaz o que o embate fez, e nada alem.
		/// </summary>
		public bool SumidoAntesA, SumidoAntesB;
	}

	private readonly List<Embate> _embates = [];

	/// <summary>
	/// BANCADA (`--clashteste`): o embate acontece SEMPRE que as condicoes batem, sem o `prob(50)`.
	///
	/// Nao muda a constante: a regra do jogo continua sendo o sorteio, e o que a flag faz e pular
	/// o dado. Uma bancada que so mede metade das vezes nao mede nada.
	/// </summary>
	private bool _clashSempre;

	/// <summary>Ligado por `--voltateste`: nasce colado na beirada. Ver a bancada da volta.</summary>
	private bool _nascerNaBeirada;

	/// <summary>Quanto BP a bancada da ao host, pra a vantagem de poder ter o que multiplicar.</summary>
	private const double BpDoHostNoTeste = 1.25;

	/// <summary>Quem esta num embate agora: id -> o embate. Serve de trava e de roteador da tecla.</summary>
	private readonly Dictionary<int, Embate> _emEmbate = [];

	/// <summary>
	/// O `zanzoRollCool` do DM: quando cada um pode SORTEAR de novo.
	///
	/// Uma variavel so, dois usos -- e assim que e no original: +1 s na hora do sorteio (um sorteio
	/// por troca de socos) e +3 s quando um embate acaba (o respiro). Nao trava atacar nem andar
	/// nem skill nenhuma: trava so o sorteio.
	/// </summary>
	private readonly Dictionary<int, long> _sorteioLivreEm = [];

	/// <summary>
	/// AS LETRAS QUE PODEM SAIR.
	///
	/// So letras sem acento, como o dono pediu ("mantenha nas letras sem simbolo ou numeros"). WASD
	/// ficam DE FORA: sao as teclas de andar, e pedir a alguem que aperte "W" no meio de um embate
	/// e pedir que ele tente correr. O mesmo vale pras teclas que ja tem funcao em combate.
	/// </summary>
	private const string Letras = "BCEFGHIJKLMNOPQRTUVXYZ";

	// =====================================================================
	// COMECAR
	// =====================================================================
	/// <summary>
	/// OS DOIS SE ACERTARAM NO MESMO INSTANTE?
	///
	/// Chamado do alto do `Atacar`, antes de resolver o golpe. Devolve `true` quando o embate
	/// comecou -- e ai o soco que o disparou NAO acontece: ele virou o embate.
	/// </summary>
	private bool TentarEmbate(ServerPlayer a, ServerPlayer d)
	{
		long agora = NowMs();

		// TEM QUE SER TROCA, e nao um golpe seguido de outro: o alvo precisa ter batido em MIM,
		// e ha pouco. E o `M.lastMeleeFoe == src && (world.time - M.lastMeleeTick) <=
		// zanzoClashWindow` do DM -- `UltimoAlvo`/`UltimoSocoMs` sao os dois campos dele.
		if (d.UltimoAlvo != a.Id || agora - d.UltimoSocoMs > MsDeTroca) return false;

		// OS DOIS PRECISAM SABER SUMIR. E o que o original exige (`haszanzo && M.haszanzo`), e e o
		// que da sentido a cena: o embate e dois corpos rapidos demais pra vista.
		if (a.Livro?.Sabe(PathDoZanzoken) != true || d.Livro?.Sabe(PathDoZanzoken) != true) return false;

		if (_emEmbate.ContainsKey(a.Id) || _emEmbate.ContainsKey(d.Id)) return false;
		// NEM QUEM ESTA DANCANDO: o quick time event da fusao usa o MESMO canal de letra, e dois
		// donos pra mesma tecla fariam a letra da danca pontuar no embate (ou o contrario). Ver
		// `TeclaDeQualquerEmbate`.
		if (EstaDancando(a.Id) || EstaDancando(d.Id)) return false;
		if (a.Zone.Hash != d.Zone.Hash) return false;

		// `!KO && !M.KO && !dead && !M.dead && move && M.move` -- `Deitado` e o `!move` daqui:
		// nocauteado, morto ou sendo arremessado.
		if (a.Deitado || d.Deitado) return false;

		// `!barrage`: no meio de uma sequencia sustentada nao ha troca simultanea a reconhecer,
		// ha um lutador martelando o outro.
		if (_barragemG3.ContainsKey(a.Id) || _barragemG3.ContainsKey(d.Id)) return false;

		// ============================ ENERGIA PRA COMECAR ============================
		// O DM cobra o Ki DENTRO do laco e `break`a quando falta. So que la o embate nao vale nada
		// (nao ha vencedor, nao ha dano), entao quebrar no primeiro passo nao custava nada.
		//
		// Aqui vale: o encontro faz os dois sumirem, prende os corpos e decide uma disputa. Um
		// embate que comeca e morre no primeiro cruzamento entrega uma vitoria em 0 a 0 pra quem nao
		// fez nada -- foi o que a bancada mostrou, duas rodadas seguidas. Entao a mesma regra e
		// conferida ANTES, com folga pra tres cruzamentos: quem esta sem energia nao entra no embate,
		// em vez de entrar e ser cuspido fora.
		// =============================================================================
		if (!_clashSempre
			&& (a.Ficha.Ki < a.Ficha.MaxKi * CustoDeKiPorCruzamento * 3
				|| d.Ficha.Ki < d.Ficha.MaxKi * CustoDeKiPorCruzamento * 3)) return false;

		// O SORTEIO, e o gate dele. Os dois precisam estar livres, e os dois PAGAM o gate mesmo
		// quando o sorteio da nao -- assim uma troca sustentada sorteia uma vez por segundo, e nao
		// uma vez por soco.
		if (Travado(a.Id, agora) || Travado(d.Id, agora)) return false;
		_sorteioLivreEm[a.Id] = agora + MsEntreSorteios;
		_sorteioLivreEm[d.Id] = agora + MsEntreSorteios;
		if (!_clashSempre && _rng.Next(100) >= ChanceDeEmbate) return false;

		Comecar(a, d, agora);
		return true;
	}

	private bool Travado(int id, long agora) =>
		_sorteioLivreEm.TryGetValue(id, out long ate) && agora < ate;

	private void Comecar(ServerPlayer a, ServerPlayer b, long agora)
	{
		// A VANTAGEM DE PODER, calculada UMA vez: o BP muda durante a luta (carga de Ki, forma) e
		// um multiplicador que oscila no meio do embate seria injusto dos dois lados.
		double bpA = Math.Max(a.Ficha.expressedBP, 1), bpB = Math.Max(b.Ficha.expressedBP, 1);
		double razao = Math.Min(Math.Max(bpA, bpB) / Math.Min(bpA, bpB), TetoDaVantagem);

		var e = new Embate
		{
			A = a, B = b,
			ProximoBaque = agora,
			MultA = bpA >= bpB ? razao : 1,
			MultB = bpB > bpA ? razao : 1,
			Meio = (a.Pos + b.Pos) * 0.5f,
		};

		// A AGENDA INTEIRA, rolada agora: `blinks = rand(6,9)`, cada um `sleep(rand(5,7))`.
		int cruzamentos = _rng.Next(CruzamentosMin, CruzamentosMax + 1);
		long fim = agora;
		for (int i = 0; i < cruzamentos; i++)
		{
			long dt = MsPorCruzamentoMin + _rng.Next((int)(MsPorCruzamentoMax - MsPorCruzamentoMin) + 1);
			fim += dt;
			e.Cruzamentos.Enqueue(dt);
		}
		e.Acaba = fim;

		_embates.Add(e);
		_emEmbate[a.Id] = e;
		_emEmbate[b.Id] = e;

		// SOME PELO MESMO CAMINHO DA TECNICA. Um segundo jeito de ficar invisivel seria um segundo
		// jeito de ESQUECER de ficar visivel de novo.
		e.SumidoAntesA = _invisiveis.Contains(a.Id);
		e.SumidoAntesB = _invisiveis.Contains(b.Id);

		foreach (ServerPlayer p in new[] { a, b })
		{
			_invisiveis.Add(p.Id);
			p.Ficha.isconcealed = true;
			MandarEfeito(p, "invisivel", -1);
			// TRAVA O CORPO: durante o embate quem manda no lugar dele e o servidor.
			p.Combate.Stun = Math.Max(p.Combate.Stun, (e.Acaba - agora) / 1000.0);
			MandarFicha(p);
		}

		// O COMECO E PESSOAL, e nao um anuncio de zona.
		//
		// Cada um precisa saber QUANTO VALE a propria letra -- a vantagem de poder e o que decide um
		// encontro desigual, e esconde-la faria o jogador mais fraco acertar tudo e perder sem
		// entender por que. Quem so assiste nao recebe nada disto: o que a plateia ve sao os baques.
		Comeco(e, a, b, agora, e.MultA, e.MultB);
		Comeco(e, b, a, agora, e.MultB, e.MultA);

		NovaTecla(e, a);
		NovaTecla(e, b);

		string quanto(double m) => m > 1.001
			? $" Voce e mais forte: cada acerto seu vale {m:0.##}."
			: (razao > 1.001 ? " Ele e mais forte: cada acerto dele vale mais." : "");
		Avisar(a, $"ZANZO CLASH contra {b.Name}! Aperte as letras que aparecerem.{quanto(e.MultA)}");
		Avisar(b, $"ZANZO CLASH contra {a.Name}! Aperte as letras que aparecerem.{quanto(e.MultB)}");
		GD.Print($"[server] ZANZO CLASH: {a.Name} x {b.Name} (vantagem x{razao:0.##} pra "
				 + $"{(bpA >= bpB ? a.Name : b.Name)})");
	}

	private static void Comeco(Embate e, ServerPlayer p, ServerPlayer outro, long agora, double meu, double dele)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Comecou);
		// QUE DISPUTA E ESTA. Ver `Protocol.TipoDeEmbate` -- o mesmo opcode serve o embate de ki, e o
		// que separa os dois na tela e este byte.
		w.Put((byte)Protocol.TipoDeEmbate.Velocidade);
		w.Put(p.Id);
		w.Put(outro.Id);
		w.Put((int)(e.Acaba - agora));
		w.Put((float)meu);
		w.Put((float)dele);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// O QUICK TIME EVENT
	// =====================================================================
	private void NovaTecla(Embate e, ServerPlayer p)
	{
		char c = SortearLetraDeEmbate();
		long agora = NowMs();
		long prazo = agora + MsPorTecla;
		if (p == e.A) { e.LetraA = c; e.PrazoA = prazo; }
		else { e.LetraB = c; e.PrazoB = prazo; }

		// ============================ E SE DESTE LADO NAO HOUVER NINGUEM PRA APERTAR? ============================
		// O embate nasceu entre dois jogadores e nunca tinha encontrado um corpo sem dono -- porque
		// corpo sem dono nunca tinha Imagem Remanescente. Com o reflexo da mente herdando a skill (ver
		// `GameServer.Clone.cs`), o dono finalmente TEM com quem ter um Zanzo Clash, e sem esta agenda o
		// oponente dele seria uma estatua: nove cruzamentos, nove letras ignoradas, vitoria de graca e
		// um soco pesado com arremesso garantido de brinde. Um QTE contra ninguem nao e um QTE.
		//
		// A REACAO SAI DO CEREBRO (`TempoDeReacao`, 0,25 s), que e o mesmo relogio com que ele apara e
		// desvia -- e nao um numero novo. O sorteio de 0,7 a 1,6 do tempo dele e o que impede a maquina
		// de responder num metronomo, do mesmo jeito que a `EmRajada` impede o soco.
		//
		// **E QUEM DEMORAR DEMAIS PERDE A VEZ SOZINHO**: nao ha teto nenhum escrito aqui. Se o atraso
		// passar dos 900 ms da letra, o tique vence o prazo antes e pede outra -- exatamente o que
		// acontece com uma pessoa lenta. A regra e uma so pros dois lados.
		// =========================================================================================================
		// A PERGUNTA E A MESMA DOS DOIS EMBATES, e por isso ela e a MESMA FUNCAO: `TemTeclado` (ver
		// `GameServer.EmbateDeKi.cs`) e o `if(M.client)` do `side_presses`. Era `p.Peer == null` escrito
		// aqui, e a divergencia ja tinha consequencia: um corpo de bancada com teclado emprestado
		// recebia letra E ganhava um agendamento de maquina por cima -- dois lados respondendo pelo
		// mesmo lado. Uma pergunta, um lugar.
		if (!TemTeclado(p))
		{
			double reacao = p.Cerebro?.TempoDeReacao ?? 0.3;
			long quando = agora + (long)(reacao * 1000 * (0.7 + _rng.NextDouble() * 0.9));
			if (p == e.A) e.RespondeA = quando; else e.RespondeB = quando;
		}

		MandarTecla(p, c, MsPorTecla);
	}

	/// <summary>
	/// PISO DE ACERTO da maquina no quick time event: o quanto ela acerta com inteligencia ZERO.
	///
	/// A conta e `Piso + (1 - Piso) * Inteligencia`, o mesmo formato dos outros gates do cerebro. Com
	/// o tempero de fabrica (0,35) da ~58%, com o do reflexo da mente (0,70) da ~77% e com o de chefe
	/// (0,85) da ~90%. Ou seja: o reflexo e rapido, e ainda da pra vence-lo lendo as letras -- que e
	/// exatamente o que o embate existe pra premiar.
	/// </summary>
	private const double PisoDeAcertoDaMaquina = 0.35;

	/// <summary>
	/// O CORPO SEM DONO APERTA A LETRA -- **pelo mesmo funil do jogador**.
	///
	/// Ele chama o <see cref="TeclaDoEmbate"/>, e nao escreve ponto nenhum: quem julga a letra, cobra o
	/// ponto do erro, apaga a vez e manda o placar continua sendo uma funcao so. Uma segunda contagem
	/// aqui seria a segunda verdade do placar, e ela divergiria da primeira no dia em que o castigo do
	/// erro mudasse -- que ja mudou uma vez ("errando vc perde 1 ponto do clash").
	///
	/// ERRAR E MANDAR UMA LETRA QUALQUER, e nao um caminho de erro proprio: assim o erro da maquina
	/// paga o MESMO preco que o do jogador, e (uma em vinte e duas vezes) o chute ate acerta -- que e
	/// literalmente o que acontece com quem chuta.
	/// </summary>
	private void ResponderPelaMaquina(Embate e, ServerPlayer p, long agora)
	{
		// A MESMA PERGUNTA DO `NovaTecla`, e pela mesma razao: quem tem teclado responde por si.
		if (TemTeclado(p)) return;

		bool ehA = p == e.A;
		char esperada = ehA ? e.LetraA : e.LetraB;
		long quando = ehA ? e.RespondeA : e.RespondeB;
		if (esperada == '\0' || quando == 0 || agora < quando) return;

		if (ehA) e.RespondeA = 0; else e.RespondeB = 0;

		double tino = p.Cerebro?.Inteligencia ?? PisoDeAcertoDaMaquina;
		bool acerta = _rng.NextDouble() < PisoDeAcertoDaMaquina + (1 - PisoDeAcertoDaMaquina) * tino;
		TeclaDoEmbate(p, acerta ? esperada : SortearLetraDeEmbate());
	}

	// =====================================================================
	// AS PECAS QUE OS DOIS EMBATES DIVIDEM
	// =====================================================================
	/// <summary>
	/// UMA LETRA DO MESMO ALFABETO. Compartilhada com a colisao de ki
	/// (`GameServer.EmbateDeKi.cs`) -- ver <see cref="Letras"/> pra saber por que WASD esta de fora,
	/// que e uma regra que vale igual nos dois: pedir "W" a quem esta empurrando um feixe e pedir que
	/// ele tente andar.
	/// </summary>
	private char SortearLetraDeEmbate() => Letras[_rng.Next(Letras.Length)];

	/// <summary>
	/// "APERTE ESTA LETRA", com o prazo. O MESMO pacote dos dois embates -- ver
	/// <see cref="Protocol.TipoDeEmbate"/> sobre por que ha um opcode so.
	/// </summary>
	private static void MandarTecla(ServerPlayer p, char c, long ms)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Tecla);
		w.Put((byte)c);
		w.Put((int)ms);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O CABO DE GUERRA NA TELA DE UM DOS DOIS. No ZanzoClash sao pontos; no embate de ki e o
	/// medidor contra o que falta dele. A barra do cliente desenha `meus / (meus + dele)` nos dois.
	/// </summary>
	private static void MandarPlacar(ServerPlayer p, double meus, double dele)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Placar);
		w.Put((float)meus);
		w.Put((float)dele);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O jogador apertou uma letra. Quem julga e AQUI -- o cliente so relata a tecla crua.
	/// </summary>
	private void TeclaDoEmbate(ServerPlayer p, char c)
	{
		if (!_emEmbate.TryGetValue(p.Id, out Embate? e)) return;

		bool ehA = p == e.A;
		char esperada = ehA ? e.LetraA : e.LetraB;
		long prazo = ehA ? e.PrazoA : e.PrazoB;
		if (esperada == '\0' || NowMs() > prazo) return;

		// ERROU A LETRA: perde a vez E UM PONTO.
		//
		// Perder a vez e vencer o prazo na hora -- e por isso o castigo e escrito no PRAZO e nao na
		// letra. A primeira versao apagava a letra (`LetraA = '\0'`), e o tique so pede letra nova
		// pra quem TEM letra vencida: um unico erro deixava o jogador sem nada na tela ate o fim.
		// A bancada pegou exatamente isso -- um embate inteiro com UMA letra pedida.
		//
		// O PONTO A MENOS E PEDIDO DO DONO ("errando vc perde 1 ponto do clash"), e conserta um
		// desequilibrio real: sem custo, martelar o teclado era estrategia dominante -- errar so
		// gastava uma vez que ia acabar sozinha no prazo. Agora chutar CUSTA, e o embate passa a
		// premiar quem le a letra em vez de quem bate mais rapido nas teclas.
		//
		// O CASTIGO E PLANO (um ponto), e nao `Mult`: a vantagem de poder multiplica o que voce
		// CONQUISTA, e deixar que ela multiplicasse tambem o erro faria o lutador mais forte ser
		// punido mais duro por um chute -- o oposto do que a vantagem significa.
		if (char.ToUpperInvariant(c) != esperada)
		{
			if (ehA) { e.PtsA -= PontoPorErro; e.LetraA = '\0'; }
			else { e.PtsB -= PontoPorErro; e.LetraB = '\0'; }

			Julgou(p, false);
			Placar(e);
			return;
		}

		// ============================ ACERTOU: PONTUA E A PROXIMA JA VEM ============================
		// A letra some (nao da pra pontuar duas vezes na mesma) e o PRAZO ENCOLHE ate o piso da
		// cadencia -- o tique pede outra assim que ele vencer (`TickDosEmbates`). Antes o prazo ficava
		// de pe e a proxima so nascia com a janela inteira fechada, que era o metronomo que o dono
		// mandou tirar.
		//
		// A CONTA E O PISO MEDIDO DO NASCIMENTO DA LETRA, sem campo novo pra isso: o prazo NASCE em
		// `nasceu + MsPorTecla`, entao tirar `MsPorTecla - MsMinimoEntreLetras` dele da exatamente
		// `nasceu + MsMinimoEntreLetras`. Quem respondeu mais devagar que o piso ja tem o prazo no
		// passado e recebe a proxima no tique seguinte -- que e o "quanto mais rapido melhor" inteiro,
		// num `-=`. Ver <see cref="MsMinimoEntreLetras"/>.
		//
		// O ERRO NAO ADIANTA NADA, de proposito: quem chuta espera a janela CHEIA fechar. Isso nao e
		// castigo novo (o ponto a menos continua sendo o unico) -- e o mesmo castigo ficando mais caro
		// porque o acerto ficou mais barato, e e o que impede o adiantamento de virar premio pra quem
		// martela o teclado.
		// ==========================================================================================
		if (ehA) { e.PtsA += e.MultA; e.LetraA = '\0'; e.PrazoA -= MsPorTecla - MsMinimoEntreLetras; }
		else { e.PtsB += e.MultB; e.LetraB = '\0'; e.PrazoB -= MsPorTecla - MsMinimoEntreLetras; }

		Julgou(p, true);
		Placar(e);
	}

	/// <summary>Quanto custa uma letra errada. Ver <see cref="TeclaDoEmbate"/>.</summary>
	private const double PontoPorErro = 1;

	/// <summary>
	/// "ACERTEI OU ERREI" -- o pisca verde/vermelho do quick time event.
	///
	/// So pra quem apertou, e so isso: o cliente nao pode julgar sozinho porque o prazo tambem
	/// conta (letra certa fora da janela e erro) e porque o placar mexe quando o OUTRO pontua.
	/// Ver <see cref="Protocol.ClashSub.Julgou"/>.
	/// </summary>
	private static void Julgou(ServerPlayer p, bool acertou)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Julgou);
		w.Put(acertou);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// "O EMBATE ACABOU", pra UM dos dois. O MESMO pacote dos dois embates -- ver
	/// <see cref="Protocol.ClashSub.Acabou"/>; os dois zerados sao o empate da colisao de ki.
	/// </summary>
	private static void Acabou(ServerPlayer p, int venc, int perd)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Acabou);
		w.Put(venc);
		w.Put(perd);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private static void Placar(Embate e)
	{
		MandarPlacar(e.A, e.PtsA, e.PtsB);
		MandarPlacar(e.B, e.PtsB, e.PtsA);
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	private void TickDosEmbates()
	{
		long agora = NowMs();
		for (int i = _embates.Count - 1; i >= 0; i--)
		{
			Embate e = _embates[i];

			// alguem caiu, morreu ou saiu do mundo no meio: o embate acaba onde esta
			if (!_players.ContainsKey(e.A.Id) || !_players.ContainsKey(e.B.Id)
				|| e.A.Ficha.dead || e.B.Ficha.dead || agora >= e.Acaba)
			{
				Terminar(e);
				_embates.RemoveAt(i);
				continue;
			}

			// ---- o vislumbre fecha: os dois somem de novo ----
			if (e.VislumbreAte > 0 && agora >= e.VislumbreAte) Esconder(e);

			// ---- o lado que nao tem dono aperta a letra dele (ver `ResponderPelaMaquina`) ----
			// ANTES do prazo, de proposito: o corpo sem dono tem que poder responder DENTRO da janela,
			// como qualquer um. Depois seria "ele so responde a letra que ja venceu".
			ResponderPelaMaquina(e, e.A, agora);
			ResponderPelaMaquina(e, e.B, agora);

			// ---- a letra venceu o prazo (ou foi perdida por erro): manda outra, sem ponto ----
			if (agora > e.PrazoA) NovaTecla(e, e.A);
			if (agora > e.PrazoB) NovaTecla(e, e.B);

			// ---- o cruzamento ----
			if (agora < e.ProximoBaque) continue;

			// BANCADA: dois robos que socam sem parar chegam a troca simultanea sem energia, e a
			// regra do Ki (certa) encerraria o embate no primeiro cruzamento -- medindo o dreno em
			// vez do encontro. A flag devolve o gasto ANTES da cobranca; o que se mede aqui e o
			// embate.
			if (_clashSempre)
			{
				e.A.Ficha.Ki = e.A.Ficha.MaxKi;
				e.B.Ficha.Ki = e.B.Ficha.MaxKi;
			}

			// O ALUGUEL DO CRUZAMENTO, cobrado ANTES de ele acontecer -- e o `break` por Ki do DM:
			// quem nao tem os 1,5% nao cruza, e o embate acaba onde esta. Perder por energia e um
			// jeito legitimo de perder: quem chegou na troca ja gasto nao sustenta o encontro.
			if (e.A.Ficha.Ki < e.A.Ficha.MaxKi * CustoDeKiPorCruzamento
				|| e.B.Ficha.Ki < e.B.Ficha.MaxKi * CustoDeKiPorCruzamento)
			{
				Terminar(e);
				_embates.RemoveAt(i);
				continue;
			}
			e.A.Ficha.Ki = Math.Max(e.A.Ficha.Ki - e.A.Ficha.MaxKi * CustoDeKiPorCruzamento, 0);
			e.B.Ficha.Ki = Math.Max(e.B.Ficha.Ki - e.B.Ficha.MaxKi * CustoDeKiPorCruzamento, 0);


			e.ProximoBaque = agora + (e.Cruzamentos.Count > 0 ? e.Cruzamentos.Dequeue() : MsPorCruzamentoMin);
			Cruzar(e);
		}
	}

	/// <summary>
	/// OS DOIS SE CRUZAM NOUTRO LUGAR: teleporte sem miragem, baque e chao rachado.
	///
	/// Sem `AnunciarZanzo` de proposito -- o dono foi explicito: "esses teleporte n sao zanzoken
	/// entao n tem after image". O que a vista pega e o ESTRAGO, e nao o corpo: e isso que faz o
	/// embate parecer rapido demais pra ser visto em vez de dois bonecos piscando pela tela.
	/// </summary>
	private void Cruzar(Embate e)
	{
		// ============================ O ENCONTRO ANDA PELO CAMPO ============================
		// A primeira versao guardava o ponto de encontro do COMECO e so sorteava de que lado cada um
		// caia. Como os dois ficavam em `Meio + passo` e `Meio - passo`, o meio deles era sempre o
		// MESMO ponto -- e o dono viu o resultado na hora: "o chao so ta quebrando e as particulas
		// de destruicao so estao no local onde o clash começou". Nove baques no mesmo tile, e o
		// `RacharChao` nao tem o que rachar da segunda vez em diante: o chao ja esta liso ali.
		//
		// Agora o ponto de encontro SALTA a cada cruzamento (`tele_rand_turf_at_range` do DM, com o
		// raio de 6 tiles) e os dois caem colados nele, um de cada lado -- que e o "clung" do
		// original, os dois adjacentes e virados um pro outro. A briga atravessa o campo e deixa uma
		// TRILHA de estrago, em vez de uma cratera so.
		// ===================================================================================
		ZoneCollision? mapa = _catalogo?.Get(e.A.Zone)?.Mapa;

		// PAREDE MANDA MAIS QUE A TECNICA -- a mesma regra do Zanzoken e da investida do soco. Aqui
		// vale em dobro: sao ate nove saltos seguidos, e um unico ponto dentro de uma casa fechada
		// poria os dois atravessados no cenario ate o fim da cena.
		// O FALLBACK NAO PODE EMPILHAR OS DOIS NO MESMO PIXEL. Se as dez tentativas falharem (e a
		// guarda de borda deixou isso mais provavel perto do fim do mundo), o par fica com o que
		// estiver aqui -- e `Vec2.Zero` poria os dois corpos na MESMA coordenada. O `FacingFrom` de
		// um vetor nulo devolve o olhar anterior, entao eles nem se encarariam.
		Vec2 novo = e.Meio;
		var lado = new Vec2(ZoneCollision.TileSize * 0.5f, 0);
		for (int t = 0; t < 10; t++)
		{
			double ang = _rng.NextDouble() * Math.Tau;
			float r = RaioDoEmbate * (0.45f + (float)_rng.NextDouble() * 0.55f);
			Vec2 tenta = e.Meio + new Vec2((float)Math.Cos(ang) * r, (float)Math.Sin(ang) * r);

			// ============================ SEMPRE LADO A LADO, NUNCA UM EM CIMA DO OUTRO ============================
			// O deslocamento era um angulo sorteado no CIRCULO INTEIRO, com o argumento de que isso
			// variava as poses. Variava -- e metade das variacoes ficava ruim: com os dois no eixo
			// vertical um cobre o outro (o de baixo desenha por cima, por Y-sort), e nas diagonais o
			// `FacingFrom` tem que escolher entre dois eixos e os dois podem cair em direcoes que
			// nao se encaram.
			//
			// Sorteia-se o LADO, e nao o angulo: eles ficam sempre na horizontal, um de frente pro
			// outro, e o que varia e QUEM fica na esquerda. E o `clung` do DM (adjacentes e virados
			// um pro outro) sem o caso em que a camera de cima nao consegue mostrar a troca.
			//
			// A DISTANCIA E MEIO TILE PRA CADA LADO -- um tile entre os corpos. Ja foi 0,9, e o dono
			// viu: "a posiçao deles ainda esta estranha", porque 1,8 tile de folga vira dois sujeitos
			// se olhando de longe em vez de dois trocando golpes.
			// =======================================================================================================
			float sinal = _rng.Next(2) == 0 ? 1f : -1f;
			var meio = new Vec2(sinal * ZoneCollision.TileSize * 0.5f, 0);

			// A BEIRADA DO MAPA ESTA FORA. O `Occupied` recusaria um ponto FORA do mapa (celula de
			// fora conta como parede), mas nao recusa a ultima coluna DE DENTRO -- e um embate que
			// termina com os dois espremidos contra o fim do mundo foi o que o dono fotografou.
			if (mapa != null && (mapa.NaBordaEm(tenta + meio, ZoneCollision.MargemDaBorda + 1)
								 || mapa.NaBordaEm(tenta - meio, ZoneCollision.MargemDaBorda + 1))) continue;

			if (mapa == null || (!MoveRules.Occupied(mapa, tenta + meio)
								 && !MoveRules.Occupied(mapa, tenta - meio)))
			{
				novo = tenta;
				lado = meio;
				break;
			}
		}

		e.Meio = novo;   // o proximo salto parte DAQUI: a briga caminha

		Recolocar(e.A, novo + lado);
		Recolocar(e.B, novo - lado);

		// OS DOIS SE ENCARAM, como no `clung` do DM (`dir` e `turn(Dd,180)`). Importa pro fim: o
		// vencedor nasce ATRAS do perdedor, e "atras" depende de pra onde ele esta olhando.
		//
		// LESTE E OESTE, CRAVADOS. Com os corpos sempre na horizontal (ver `lado` acima) nao ha o
		// que deduzir, e cravar tira do caminho o `FacingFrom`, que precisa escolher um eixo e
		// pode devolver o olhar ANTERIOR quando o vetor e curto demais.
		bool aEstaNaDireita = lado.X > 0;
		e.A.Facing = aEstaNaDireita ? Facing.West : Facing.East;
		e.B.Facing = aEstaNaDireita ? Facing.East : Facing.West;

		AvisarZona(e, w => { w.Put((byte)Protocol.ClashSub.Baque); w.PutVec(novo); });

		// O CHAO PAGA O PATO, no ponto NOVO. Reusa o mesmo `RacharChao` do golpe pesado -- a forca e
		// a do MAIOR dos dois, porque quem racha o chao e o encontro e nao um deles.
		RacharChao(e.A.Zone, novo, Math.Max(e.A.Ficha.expressedBP, e.B.Ficha.expressedBP));

		// E O QUE ESTIVER DE PE EM VOLTA TAMBEM, com uma boa chance. Ver `ChanceDeQuebrarCenario`.
		if (_rng.NextDouble() < ChanceDeQuebrarCenario) Arrasar(e.A.Zone, novo);

		// E, AS VEZES, DA PRA VER. Ver `ChanceDeAparecer`.
		if (_rng.NextDouble() < ChanceDeAparecer) Vislumbrar(e);
	}

	/// <summary>
	/// OS DOIS APARECEM POR UM INSTANTE, ja se encarando e no meio de um golpe.
	///
	/// A VISIBILIDADE SAI DO MESMO LUGAR DE SEMPRE (`_invisiveis`, que o snapshot le pelo bit
	/// `Oculto`): quem assiste nao precisa de pacote novo nenhum, o corpo simplesmente esta la no
	/// proximo snapshot. `isconcealed` NAO e mexido -- ele e ficha, mexe em defesa e no BP expresso,
	/// e piscar isso nove vezes num embate seria mexer nas contas do jogador pra fazer efeito.
	///
	/// A POSE tambem sai do caminho normal: `AtaqueAte` no futuro e o que o `ServerPlayer.Pose` le
	/// pra dizer "atacando", e o `RemotePlayer` REINICIA a animacao de soco quando a pose aparece.
	/// Um campo que ja existia, usado pelo que ele ja significava.
	///
	/// AS POSICOES MUDAM SOZINHAS: o `lado` do cruzamento e um angulo sorteado, entao ora um esta a
	/// esquerda, ora a direita, ora acima. Nao ha tabela de poses -- ha um circulo.
	/// </summary>
	private void Vislumbrar(Embate e)
	{
		long agora = NowMs();
		e.VislumbreAte = agora + MsDeVislumbre;

		foreach (ServerPlayer p in new[] { e.A, e.B })
		{
			_invisiveis.Remove(p.Id);
			p.AtaqueAte = agora + Protocol.AttackPoseMs;
			MandarVislumbre(p, true);
		}
	}

	/// <summary>Fecha o vislumbre: os dois somem de novo.</summary>
	private void Esconder(Embate e)
	{
		e.VislumbreAte = 0;
		foreach (ServerPlayer p in new[] { e.A, e.B })
		{
			if (!_players.ContainsKey(p.Id)) continue;
			_invisiveis.Add(p.Id);
			MandarVislumbre(p, false);
		}
	}

	/// <summary>
	/// O VISLUMBRE VAI COM A DIRECAO, e e por isso que ele tem um byte a mais.
	///
	/// ============================ O CORPO LOCAL NAO SABE PRA ONDE ESTA VIRADO ============================
	/// Quem desenha o corpo local e o proprio cliente, a partir do TECLADO dele -- o servidor manda
	/// posicao (`Correction`) e ficha, mas o olhar de andar nasce no input. Num embate o corpo e do
	/// SERVIDOR: ele recoloca os dois e crava quem encara quem (ver `Cruzar`). Quando o vislumbre
	/// abria, o dono via o proprio boneco socando pro lado que ele estava segurando a tecla, e o
	/// adversario -- que vem por snapshot, com a direcao certa -- socando pro outro. Os dois no
	/// mesmo instante, virados pra lugares diferentes.
	///
	/// A direcao do embate so existe aqui, entao ela viaja aqui.
	/// ======================================================================================================
	/// </summary>
	private static void MandarVislumbre(ServerPlayer p, bool aparece)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Vislumbre);
		w.Put(aparece);
		w.Put((byte)p.Facing);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// DERRUBA O QUE BLOQUEIA em volta do baque -- arvore, pedra, muro, construcao.
	///
	/// Varre de dentro pra fora e para no PRIMEIRO que cai: uma onda de choque derruba o que estava
	/// no caminho dela, nao limpa um circulo de cinco tiles. Nove cruzamentos arrasando tudo em raio
	/// 2 deixariam a zona pelada depois de duas brigas.
	/// </summary>
	private void Arrasar(ZoneKey zona, Vec2 centro)
	{
		const int T = ZoneCollision.TileSize;
		for (int r = 0; r <= RaioDoEstrago; r++)
			for (int dy = -r; dy <= r; dy++)
				for (int dx = -r; dx <= r; dx++)
				{
					// so a casca do anel: o miolo ja foi visto na volta anterior
					if (r > 0 && Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					if (DerrubarCenario(zona, centro + new Vec2(dx * T, dy * T))) return;
				}
	}

	/// <summary>Poe o corpo noutro lugar e avisa o dono, sem miragem e sem validar passo.</summary>
	private void Recolocar(ServerPlayer p, Vec2 onde)
	{
		p.Pos = onde;
		// O MESMO CARIMBO DO TELEPORTE: sem ele os pacotes de input que o cliente ja mandou
		// (da posicao velha) seriam tratados como cliente errado e o puxariam de volta.
		p.SeqDoTeleporte = p.SeqInput;
		p.CorrecaoEsperadaAte = NowMs() + 500;
		p.OrcamentoPx = 0;

		var w = Protocol.Begin(Protocol.S2C.Correction);
		w.Put(p.SeqInput);
		w.PutVec(p.Pos);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// ACABAR
	// =====================================================================
	private void Terminar(Embate e)
	{
		_emEmbate.Remove(e.A.Id);
		_emEmbate.Remove(e.B.Id);

		// SE O EMBATE ACABOU NO MEIO DE UM VISLUMBRE, os dois ja estao FORA de `_invisiveis` -- e o
		// laco abaixo, que so tira, deixaria tudo certo por acaso. Fechar antes torna isso uma
		// certeza em vez de uma coincidencia.
		if (e.VislumbreAte > 0) Esconder(e);

		long agora = NowMs();
		_sorteioLivreEm[e.A.Id] = agora + MsDeDescanso;
		_sorteioLivreEm[e.B.Id] = agora + MsDeDescanso;

		// REAPARECEM PELO MESMO CAMINHO. Ver `Comecar`.
		foreach (ServerPlayer p in new[] { e.A, e.B })
		{
			if (!_players.ContainsKey(p.Id)) continue;
			p.Combate.Stun = 0;

			// SO DESFAZ O QUE O EMBATE FEZ: quem chegou ja invisivel continua invisivel.
			if (p == e.A ? e.SumidoAntesA : e.SumidoAntesB) continue;
			_invisiveis.Remove(p.Id);
			p.Ficha.isconcealed = false;
			MandarEfeito(p, "invisivel", 0);
		}

		bool vivo = _players.ContainsKey(e.A.Id) && _players.ContainsKey(e.B.Id);

		// EMPATE SE DECIDE NO PODER, nao na ordem em que os dois entraram na lista.
		//
		// Acontece de verdade: os dois acertam tudo, ou um embate curto acaba sem que ninguem
		// pontuasse. `PtsA >= PtsB` faria de A o vencedor por ser A -- e quem e A depende de quem
		// deu o soco que virou embate, que e ruido.
		bool ganhouA = e.PtsA > e.PtsB
					|| (Math.Abs(e.PtsA - e.PtsB) < 1e-9 && e.A.Ficha.expressedBP >= e.B.Ficha.expressedBP);
		ServerPlayer venc = ganhouA ? e.A : e.B;
		ServerPlayer perd = ganhouA ? e.B : e.A;

		// ============================ O FIM E PESSOAL, COMO O COMECO ============================
		// Isto era `AvisarZona`, e o defeito so aparecia com DOIS embates na mesma zona -- trivial numa
		// briga de quatro, e mais ainda porque a disputa de ki dura ate 22 s contra os 3 a 6,3 s daqui.
		// O cliente nao tinha como saber que aquele fim era de outros: ele derrubava o `EmClash` (e com
		// ele o silencio dos atalhos), fechava a tela do quick time event e escrevia na cara do jogador
		// o desfecho da briga alheia -- no meio da dele.
		//
		// Nao ha plateia a avisar: o `Acabou` so tem tres consumidores no cliente (a tela do embate, o
		// `EmClash` e o corpo local que volta a aparecer), e os tres sao dos DOIS. Quem assiste ve o
		// baque, que continua sendo de zona.
		//
		// E PESSOAL TAMBEM ALCANCA MAIS: `AvisarZona` varre a zona do embate, entao quem tivesse
		// trocado de planeta no meio nunca receberia o fim -- e ficaria com o teclado mudo ate o prazo
		// do cliente vencer. Peer a peer, chega enquanto houver conexao.
		// =======================================================================================
		Acabou(e.A, venc.Id, perd.Id);
		Acabou(e.B, venc.Id, perd.Id);

		if (!vivo) return;

		// O VENCEDOR NASCE ATRAS DO PERDEDOR. E a imagem que fecha a cena: o outro ainda olhando
		// pra frente enquanto o golpe vem das costas.
		Vec2 atras = perd.Pos - MeleeArea.Frente(perd.Facing) * (ZoneCollision.TileSize * 0.9f);
		ZoneCollision? mapa = _catalogo?.Get(perd.Zone)?.Mapa;
		// parede ou beirada do mundo: nasce em cima do perdedor mesmo, que e livre por construcao
		if (mapa != null && (MoveRules.Occupied(mapa, atras) || mapa.NaBordaEm(atras))) atras = perd.Pos;
		Recolocar(venc, atras);
		venc.Facing = perd.Facing;   // olhando pra mesma direcao: ele esta nas costas do outro
		MandarFicha(venc);
		MandarFicha(perd);

		Avisar(venc, $"voce foi mais rapido. {perd.Name} nao te viu.");
		Avisar(perd, $"{venc.Name} sumiu... e apareceu atras de voce.");
		GD.Print($"[server] ZANZO CLASH acabou: {venc.Name} {(venc == e.A ? e.PtsA : e.PtsB):0.#}"
				 + $"x{(perd == e.A ? e.PtsA : e.PtsB):0.#} {perd.Name}");

		GolpeDeSaida(venc, perd);
	}

	/// <summary>
	/// O GOLPE QUE FECHA: resolvido como um pesado normal, mas com o arremesso FORCADO.
	///
	/// Passa pelo `MeleeResolver` de verdade -- dano, membro, critico, tudo pelas regras -- porque
	/// um golpe que ignora o combate seria um numero inventado no meio de um sistema que tem
	/// formulas. O que muda e a chance de arremessar: o dono pediu "com maior chance de knock
	/// back", e um encontro que termina com o perdedor de pe no lugar nao fecha cena nenhuma.
	/// </summary>
	private void GolpeDeSaida(ServerPlayer venc, ServerPlayer perd)
	{
		CombatState cv = venc.Combate, cp = perd.Combate;
		double angulo = MeleeArea.AnguloDeChegada(perd.Pos, perd.Facing, venc.Pos);
		const double tipo = 3;   // peso do golpe PESADO

		GolpeResultado r = MeleeResolver.Resolver(cv, cp, angulo, _rng, tipo, DanoDeEstilo(venc, perd));
		MarcarAgressao(perd, venc);

		ResolverDesfecho(venc, perd, r);
		AnunciarGolpe(venc, perd, r, nivel: 3, zanzo: false);

		if (r.Encostou)
		{
			// ARREMESSO GARANTIDO, E AGORA ESTA ESCRITO: o `garantido` pula o sorteio.
			//
			// Esta linha SOBREVIVIA POR ACIDENTE. O pesado nao tinha sorteio nenhum -- arremessava
			// 100% das vezes --, entao "garantido" era o comportamento de fabrica e o comentario
			// descrevia uma regra que ninguem tinha escrito. Com o sorteio ligado (o pedido do dono:
			// o pesado jogava longe demais e cedo demais), esta cena terminaria com o perdedor de pe
			// em dois de cada tres embates. O golpe de saida e o unico caso do combate corpo a corpo
			// em que o arremesso E o desfecho, e nao uma consequencia dele.
			TentarEmpurrar(venc, perd, r.Dano * 2, Protocol.Golpe.Pesado, garantido: true);
			RacharChao(venc.Zone, perd.Pos, Math.Max(venc.Ficha.expressedBP, perd.Ficha.expressedBP));
		}
	}

	// =====================================================================
	// PECAS
	// =====================================================================
	private void AvisarZona(Embate e, Action<NetDataWriter> escrever)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		escrever(w);
		foreach (ServerPlayer o in ZoneList(e.A.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>Solta quem estava num embate -- chamado quando o jogador cai fora do mundo.</summary>
	private void SoltarDoEmbate(int id)
	{
		if (!_emEmbate.TryGetValue(id, out Embate? e)) return;
		Terminar(e);
		_embates.Remove(e);
	}
}
