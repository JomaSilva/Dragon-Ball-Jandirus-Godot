using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// A COLISAO DE KI -- dois feixes se empurrando no ar, ou um feixe contra as maos de quem aguenta.
///
/// ============================ ISTO E PORTE: `BeamClash.dm` ============================
/// O DM tem a disputa inteira (`Code/Modules/CombatMechanics/BeamClash.dm`, 463 linhas), com
/// gatilho no `Bump` de feixe contra feixe (`objects.dm:509`) e um segundo por PROXIMIDADE
/// (`objects.dm:246-254`, com um comentario do proprio autor contando por que o frontal nao bastava:
/// dois feixes retos em fileiras vizinhas se atravessavam sem nunca colidir). Os numeros moram em
/// <see cref="EmbateDeKi"/>, um por um, com a linha de origem.
///
/// TRES COISAS SAO NOVAS, e cada uma esta marcada no lugar:
///   1. FEIXE CONTRA QUEM BLOQUEIA -- no DM a disputa exige `WaveAttack` dos DOIS lados; quem esta
///      de guarda so ganha meia conta de dano e o dobro da chance de defletir. Ver
///      <see cref="TentarEmbateDeGuarda"/> e `EmbateDeKi.PoderDeSegurar`.
///   2. O PONTO DE ENCONTRO ANDA DURANTE a disputa -- no DM so o orbe se deslocava em pixel e o
///      avanco de verdade era depois de decidido. Ver <see cref="MoverOEncontro"/>.
///   3. A ENTRADA E O QUICK TIME EVENT do ZanzoClash, e nao martelar ESPACO. Ver
///      `EmbateDeKi.ApertosPorLetra`.
/// ======================================================================================
///
/// ============================ O QUE SE REUSOU DO ZANZO CLASH ============================
/// O molde e ele, como a instrucao desta camada pediu, e o reuso e literal:
///   * o ALFABETO, a JANELA e o PISO DE CADENCIA da letra (`Letras`, `MsPorTecla`,
///     `MsMinimoEntreLetras`) -- e a licao que mora la: o acerto ADIANTA a proxima letra, e quem
///     impede que isso vire concurso de digitacao e o piso, que e o `BCL_PRESS_TICK_CAP` do DM;
///   * o CANAL da tecla (`C2S.ClashTecla`), o OPCODE (`S2C.Clash`) e a TELA (`Client/ClashQte.cs`),
///     separados por um byte de <see cref="Protocol.TipoDeEmbate"/>;
///   * o FUNIL do corpo preso (`PodeMexerOCorpo`), o mesmo do nocaute, da tecla C e do raio;
///   * o CHAO que racha (`RacharChao` / `Arrasar`) e o tremor de tela, que o cliente ja liga sozinho
///     quando `EmClash` esta de pe.
/// O que NAO se reusou e o estado: o ZanzoClash conta PONTOS e este conta um MEDIDOR de cabo de
/// guerra que decide sozinho ao chegar na ponta -- sao duas fisicas, e enfiar as duas na mesma
/// classe seria um `if` por linha de tique.
/// ========================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>
	/// UM LADO DA DISPUTA. Ou ele tem um FEIXE, ou ele tem as MAOS -- e e so isso que os separa: a
	/// fisica, a letra, o Ki e o desfecho sao os mesmos. Escrever duas classes (uma pro embate de
	/// feixes, outra pro de guarda) faria duas copias do laco.
	/// </summary>
	private sealed class LadoDeKi
	{
		public required ServerPlayer Quem;

		/// <summary>O feixe deste lado. NULO = ele esta segurando com as maos.</summary>
		public Projetil? Feixe;

		/// <summary>A letra que ele tem que apertar agora.</summary>
		public char Letra;

		/// <summary>
		/// QUANTOS SEGUNDOS AINDA VALEM nesta letra -- um CONTADOR, e nao um instante do relogio.
		///
		/// ============================ POR QUE NAO E `NowMs() + 900` ============================
		/// O ZanzoClash guarda o prazo como carimbo de relogio, e la funciona: o embate dele e curto e
		/// a bancada que o mede e um robo de CLIENTE, rodando em tempo real.
		///
		/// Aqui todo o resto do encontro anda por `dt` -- os segundos corridos, o ciclo de Ki, a
		/// deriva, o ponto de encontro. Misturar as duas fontes de tempo tem dois precos, e a bancada
		/// cobrou os dois: (1) num servidor sobrecarregado a janela da letra encolheria em tempo de
		/// jogo enquanto a fisica continuaria no ritmo dela; (2) qualquer medicao que rode o tique mais
		/// rapido que o relogio -- que e o que uma bancada de boot FAZ -- ve o prazo nunca vencer, o
		/// jogador recebe UMA letra pro encontro inteiro, e o teste mede um jogador amordacado achando
		/// que mede um jogador perfeito.
		/// ===================================================================================
		/// </summary>
		public double Restante;

		/// <summary>Quanto vale cada acerto dele. `advantage()` do DM, calculada UMA vez.</summary>
		public double Vantagem = 1;

		/// <summary>Apertos que ainda nao entraram na conta (letras acertadas + a taxa da IA).</summary>
		public double ApertosPendentes;

		/// <summary>Segurando com as maos e nao com um feixe.</summary>
		public bool DeMaos => Feixe == null;
	}

	/// <summary>Uma disputa viva.</summary>
	private sealed class DisputaDeKi
	{
		public required LadoDeKi A, B;

		/// <summary>0..100. 100 = A engoliu; 0 = B engoliu. Comeca em 50.</summary>
		public double Medidor = EmbateDeKi.MedidorInicial;

		/// <summary>Segundos corridos -- o `t` do `clash_loop`.</summary>
		public double Corridos;

		/// <summary>Onde os dois se encontraram, e onde eles se encontram AGORA.</summary>
		public Vec2 PontoInicial, Ponto;

		/// <summary>Unitario, do lado A pro lado B. E o trilho por onde o encontro caminha.</summary>
		public Vec2 Eixo;

		/// <summary>Quantos pixels o encontro pode caminhar pra cada lado sem chegar num corpo.</summary>
		public float ParaB, ParaA;

		/// <summary>Relogio do ciclo de 0,2 s -- e nele que o Ki e cobrado e o estrago acontece.</summary>
		public double AteOCiclo = EmbateDeKi.SegundosPorCiclo;

		/// <summary>Quantos ciclos ja passaram -- o `fxtick` do DM.</summary>
		public int Ciclos;

		public ulong Zona;
		public Protocol.TipoDeEmbate Tipo;
	}

	private readonly List<DisputaDeKi> _disputas = [];

	/// <summary>
	/// Quem esta numa colisao de ki: id -> a disputa. Trava, roteador da tecla, e -- porque entra no
	/// <see cref="PodeMexerOCorpo"/> -- o que planta os pes dos dois enquanto a disputa corre.
	/// </summary>
	private readonly Dictionary<int, DisputaDeKi> _emEmbateDeKi = [];

	/// <summary>Quando cada NPC pode tentar outro contra-feixe. `bcl_npc_beam_cd` do DM.</summary>
	private readonly Dictionary<int, long> _contraFeixeLivreEm = [];

	/// <summary>
	/// OS CORPOS QUE CONTAM COMO "TEM TECLADO" MESMO SEM `Peer` -- so a bancada `--embatekiteste`
	/// escreve aqui.
	///
	/// Existe pelo mesmo motivo do `_clashSempre` do ZanzoClash, e com a mesma disciplina: **nao muda
	/// regra nenhuma**, so tira do caminho a unica coisa que impediria a regra de rodar no boot. Os
	/// corpos forjados nao tem conexao, e sem isto todo lado cairia na taxa automatica da IA -- a
	/// bancada mediria duas IAs se empurrando e o quick time event, que e o que ela existe pra medir,
	/// nunca seria exercitado.
	/// </summary>
	private readonly HashSet<int> _comTecladoDeTeste = [];

	/// <summary>
	/// ESTE CORPO E DIRIGIDO POR ALGUEM COM TECLADO? E a pergunta que separa "recebe letra" de
	/// "empurra na taxa automatica" -- o `if(M.client) ... else ...` do `side_presses`
	/// (`BeamClash.dm:138`).
	///
	/// Ela e feita UMA vez e por uma funcao so. Perguntada em dois lugares, um dia um deles esqueceria
	/// a bancada e o outro nao -- e o defeito seria um lado do embate que recebe letra e nunca pontua.
	/// </summary>
	private bool TemTeclado(ServerPlayer p) => p.Peer != null || _comTecladoDeTeste.Contains(p.Id);

	// =====================================================================
	// OS GATILHOS
	// =====================================================================
	/// <summary>
	/// DOIS FEIXES SE ENCONTRARAM? Chamado do tique do projetil, so pra cabeca de raio.
	///
	/// ============================ AS CONDICOES SAO AS DO `bcl_try_start` ============================
	/// `BeamClash.dm:68-83`, uma a uma: os dois tem que ser `WaveAttack` (*"disputa e coisa de BEAM
	/// canalizado"*), donos diferentes e vivos, nenhum dos dois ja numa disputa, e -- a que mais
	/// importa -- **os dois donos ainda canalizando** (`A.beaming && B.beaming`). Um raio que o dono
	/// ja soltou e um raio abandonado: ele nao tem quem o empurre, e disputar contra ele seria
	/// disputar contra ninguem.
	///
	/// A GEOMETRIA e a do gatilho por proximidade (`objects.dm:250-253`): cabeca inimiga a ate um
	/// tile, vindo CONTRA nos (o rumo dela dentro de 45 graus do oposto do nosso). Feixes paralelos no
	/// mesmo sentido sao dois aliados atirando juntos, e o DM diz isso com todas as letras.
	/// ==========================================================================================
	/// </summary>
	private bool TentarEmbateDeFeixes(Projetil p, List<Projetil> lista)
	{
		if (!PodeDisputar(p, out ServerPlayer? dono) || dono == null) return false;

		foreach (Projetil r in lista)
		{
			if (ReferenceEquals(r, p)) continue;
			if (!PodeDisputar(r, out ServerPlayer? outro) || outro == null) continue;
			if (outro.Id == dono.Id) continue;

			// A UM TILE, como o `range(1, src)` do DM.
			if ((r.Pos - p.Pos).LengthSquared > ZoneCollision.TileSize * (float)ZoneCollision.TileSize) continue;

			// VINDO CONTRA: o rumo dele dentro de 45 graus do oposto do meu. `cos(135) = -0,707`.
			if (p.Rumo.X * r.Rumo.X + p.Rumo.Y * r.Rumo.Y > -0.7f) continue;

			Comecar(new LadoDeKi { Quem = dono, Feixe = p },
					new LadoDeKi { Quem = outro, Feixe = r },
					Protocol.TipoDeEmbate.FeixeContraFeixe);
			return true;
		}
		return false;
	}

	/// <summary>
	/// ESTE FEIXE PODE ENTRAR NUMA DISPUTA? A parte que os dois lados tem em comum.
	///
	/// A conferencia de canalizacao e feita contra o `_canais` (e contra o raio QUE ESTA NELE): e o
	/// `A.beaming` do DM sem a chance de um raio velho, de um canal ja fechado, se passar por
	/// canalizado.
	/// </summary>
	private bool PodeDisputar(Projetil p, out ServerPlayer? dono)
	{
		dono = null;
		if (!p.Vivo || p.Tipo != TipoDeProjetil.Beam || p.EmEmbate || p.JaDisputou) return false;
		if (!p.Canalizando) return false;
		if (!_players.TryGetValue(p.Dono, out ServerPlayer? d)) return false;
		if (d.Ficha.dead || d.Ficha.KO) return false;
		if (_emEmbateDeKi.ContainsKey(d.Id) || _emEmbate.ContainsKey(d.Id)) return false;
		if (_canais.GetValueOrDefault(d.Id)?.Raio != p) return false;

		dono = d;
		return true;
	}

	/// <summary>
	/// UM FEIXE BATEU EM QUEM ESTA DE GUARDA. **NOVO -- o DM nao tem isto.**
	///
	/// ============================ O QUE A GUARDA FAZIA CONTRA KI, E O QUE FALTAVA ============================
	/// Ela ja fazia duas coisas, as duas portadas da camada 1 e as duas do DM: o dano e dividido de
	/// novo pela pericia de defesa de ki (`dmg /= 2 * log_4(...)`, `objects.dm:341`) e a chance de
	/// defletir DOBRA. Ou seja: erguer a guarda contra um Kamehameha era um sorteio melhor e um numero
	/// menor -- nunca uma CENA.
	///
	/// O pedido do dono foi *"feixe contra feixe, mas tambem feixe contra quem bloqueia"*, e a imagem
	/// e conhecida: as duas maos seguram o raio, os pes cavam o chao, e ou ele engole voce ou voce
	/// joga aquilo de volta. Entao o mesmo cabo de guerra roda com um lado sem feixe -- e o poder das
	/// maos NAO e inventado: e o numerador da chance de deflexao que o jogo ja calculava
	/// (`EmbateDeKi.PoderDeSegurar`), que e a unica conta do DM que ja comparava um corpo com um tiro.
	///
	/// O SORTEIO DE DEFLEXAO CONTINUA VINDO ANTES (ver `Acertar`): quem tem sorte defende de graca,
	/// como sempre teve. Isto aqui e o que acontece com quem NAO defletiu e mesmo assim nao vai
	/// abaixar a guarda.
	/// ====================================================================================================
	/// </summary>
	private bool TentarEmbateDeGuarda(Projetil p, ServerPlayer alvo)
	{
		if (!PodeDisputar(p, out ServerPlayer? dono) || dono == null) return false;
		if (alvo.Combate is not { Bloqueando: true }) return false;
		if (alvo.Ficha.dead || alvo.Ficha.KO || alvo.Combate.Stun > 0) return false;
		if (_emEmbateDeKi.ContainsKey(alvo.Id) || _emEmbate.ContainsKey(alvo.Id)) return false;

		Comecar(new LadoDeKi { Quem = dono, Feixe = p },
				new LadoDeKi { Quem = alvo, Feixe = null },
				Protocol.TipoDeEmbate.FeixeContraGuarda);
		return true;
	}

	// =====================================================================
	// COMECAR
	// =====================================================================
	private void Comecar(LadoDeKi a, LadoDeKi b, Protocol.TipoDeEmbate tipo)
	{
		// A VANTAGEM, CALCULADA UMA VEZ -- a mesma disciplina do ZanzoClash: o BP muda no meio da
		// briga (carga de Ki, forma, raiva) e um multiplicador que oscila seria injusto dos dois lados.
		double poderA = PoderDoLado(a), poderB = PoderDoLado(b);
		a.Vantagem = EmbateDeKi.Vantagem(poderA, poderB);
		b.Vantagem = EmbateDeKi.Vantagem(poderB, poderA);

		// O PONTO DE ENCONTRO: onde as cabecas se tocaram. No embate de guarda ha uma cabeca so, e
		// ela ja esta em cima do corpo de quem segura.
		Vec2 ponto = b.Feixe != null ? (a.Feixe!.Pos + b.Feixe.Pos) * 0.5f : a.Feixe!.Pos;

		Vec2 eixo = b.Quem.Pos - a.Quem.Pos;
		eixo = eixo.LengthSquared > 1e-4f ? eixo.Normalized() : a.Feixe!.Rumo;

		// QUANTO O ENCONTRO PODE CAMINHAR pra cada lado. Um tile de folga: o feixe so ENCOSTA em
		// alguem quando a disputa acabou -- enquanto ela corre, chegar perto e a ameaca, nao o dano.
		const float folga = ZoneCollision.TileSize;
		var d = new DisputaDeKi
		{
			A = a, B = b,
			PontoInicial = ponto, Ponto = ponto,
			Eixo = eixo,
			ParaB = Math.Max((b.Quem.Pos - ponto).Length - folga, 0),
			ParaA = Math.Max((a.Quem.Pos - ponto).Length - folga, 0),
			Zona = a.Quem.Zone.Hash,
			Tipo = tipo,
		};

		_disputas.Add(d);
		_emEmbateDeKi[a.Quem.Id] = d;
		_emEmbateDeKi[b.Quem.Id] = d;

		// AS CABECAS PARAM. `headA.in_beamclash = 1; walk(headA, 0)` -- dali em diante quem manda na
		// posicao delas e o tique da disputa.
		if (a.Feixe != null) a.Feixe.EmEmbate = true;
		if (b.Feixe != null) b.Feixe.EmEmbate = true;

		Comeco(d, a, b);
		Comeco(d, b, a);
		NovaLetra(d, a);
		NovaLetra(d, b);

		string oque = tipo == Protocol.TipoDeEmbate.FeixeContraGuarda
			? $"{b.Quem.Name} SEGUROU seu ataque com as maos!"
			: $"os ataques de {a.Quem.Name} e {b.Quem.Name} colidem!";
		Avisar(a.Quem, $"DISPUTA DE KI: {oque} Acerte as letras pra empurrar.");
		Avisar(b.Quem, tipo == Protocol.TipoDeEmbate.FeixeContraGuarda
			? $"voce agarra o ataque de {a.Quem.Name}! Acerte as letras pra devolve-lo."
			: $"DISPUTA DE KI contra {a.Quem.Name}! Acerte as letras pra empurrar.");

		// O ESTOURO INICIAL, no mundo: o mesmo `createShockwavemisc(center, 3)` do `start()`.
		Estourar(d, forte: true);

		GD.Print($"[server] EMBATE DE KI ({tipo}): {a.Quem.Name} x {b.Quem.Name} "
				 + $"| poder {poderA:0.###e0} x {poderB:0.###e0} | vantagem {a.Vantagem:0.##} x {b.Vantagem:0.##}");
	}

	/// <summary>
	/// O PODER DE UM LADO: o do FEIXE se ele tem um, o das MAOS se nao tem. Ver os dois metodos em
	/// <see cref="EmbateDeKi"/> -- o primeiro e do DM, o segundo e o numerador da deflexao do DM.
	/// </summary>
	private static double PoderDoLado(LadoDeKi l)
		=> l.Feixe?.PoderDeEmbate()
		   ?? EmbateDeKi.PoderDeSegurar(l.Quem.Ficha, l.Quem.Combate?.Bloqueando ?? false);

	private static void Comeco(DisputaDeKi d, LadoDeKi meu, LadoDeKi dele)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Comecou);
		w.Put((byte)d.Tipo);
		w.Put(meu.Quem.Id);
		w.Put(dele.Quem.Id);
		w.Put((int)(EmbateDeKi.SegundosMaximos * 1000));
		w.Put((float)meu.Vantagem);
		w.Put((float)dele.Vantagem);
		meu.Quem.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// A LETRA
	// =====================================================================
	/// <summary>
	/// Pede uma letra nova a este lado. NPC nao recebe letra: ele empurra pela taxa do
	/// `side_presses` (ver <see cref="TickDosEmbatesDeKi"/>), que e o que o DM faz.
	/// </summary>
	private void NovaLetra(DisputaDeKi d, LadoDeKi l)
	{
		// ============================ O RESTO DO TIQUE NAO SE PERDE ============================
		// Era `l.Restante = janela`, e o `=` jogava fora o quanto o contador passou de zero dentro do
		// tique -- ate 1/30 s por letra. Com a janela em 0,9 s isso era 3%; com o PISO de 0,3 s (o
		// adiantamento do acerto) vira 11%, e 11% e exatamente a fracao que separa o jogador perfeito
		// do NPC mediano na calibragem do `ApertosPorLetra`. Ou seja: sem esta linha, adiantar a letra
		// deixaria quem acerta TUDO perdendo pra taxa automatica de um NPC qualquer -- o oposto do
		// pedido.
		//
		// O CARREGO E DE UM TIQUE SO. Uma travada do servidor nao pode virar credito acumulado de
		// letras; o que se recupera aqui e o arredondamento, e nao o tempo perdido.
		l.Restante = Math.Max(l.Restante, -Protocol.TickSeconds) + MsPorTecla / 1000.0;
		if (!TemTeclado(l.Quem)) { l.Letra = '\0'; return; }

		l.Letra = SortearLetraDeEmbate();
		MandarTecla(l.Quem, l.Letra, MsPorTecla);
	}

	/// <summary>
	/// O JOGADOR APERTOU UMA LETRA NUMA COLISAO DE KI. Devolve falso quando ele nao esta em nenhuma
	/// -- e ai quem trata e o ZanzoClash (ver <see cref="TeclaDeQualquerEmbate"/>).
	///
	/// O CASTIGO DO ERRO E PLANO, e pelo mesmo motivo escrito no `TeclaDoEmbate` do ZanzoClash: a
	/// vantagem multiplica o que voce CONQUISTA; deixa-la multiplicar o erro puniria o mais forte
	/// mais duro por um chute. Um aperto a menos, sem vantagem.
	/// </summary>
	private bool TeclaDoEmbateDeKi(ServerPlayer p, char c)
	{
		if (!_emEmbateDeKi.TryGetValue(p.Id, out DisputaDeKi? d)) return false;

		LadoDeKi meu = d.A.Quem == p ? d.A : d.B;
		if (meu.Letra == '\0' || meu.Restante <= 0) return true;

		bool acertou = char.ToUpperInvariant(c) == meu.Letra;

		// O PRAZO SEGURA A VEZ, e nao a letra -- a mesma correcao que o ZanzoClash ja pagou: apagar a
		// letra fazia o tique achar que ela ainda valia, e um unico erro deixava o jogador sem nada na
		// tela ate o fim.
		meu.Letra = '\0';

		// ============================ ACERTOU: A PROXIMA JA VEM (O PEDIDO DO DONO) ============================
		// O mesmo adiantamento do ZanzoClash, na moeda deste laco: `Restante` e um CONTADOR de segundos
		// (ver o campo), entao encurtar o prazo ate o piso e tirar dele o que a janela tem alem do piso.
		// O errado espera a janela CHEIA, como la.
		//
		// **E AQUI ELE PAGA DOBRADO**, porque este placar nao e "acertos sobre um numero fixo de
		// letras": e um cabo de guerra continuo que acaba quando o medidor chega na ponta. Mais letras
		// = mais empurrao = vitoria mais cedo, e o encontro pode nem chegar aos 22 s.
		//
		// E POR ISSO A LETRA VALE MENOS: o `ApertosPorLetra` era calibrado pela JANELA (0,9 s), e a
		// promessa dele -- *"jogador perfeito e NPC mediano empatam"* -- e sobre VAZAO, nao sobre
		// letras. Triplicando a vazao sem tocar no valor, quem acerta tudo passaria a ganhar ate de um
		// rival 6x mais forte e a escada de poder inteira (a tabela 1x / 1,75x / 6x que a bancada
		// imprime) iria pro lixo. Calibrado pelo PISO, a conta fecha igual: 1/0,3 letras por segundo
		// vezes o que o NPC mediano rende em 0,3 s da os mesmos 3,2 apertos/s de antes -- so que agora
		// quem responde em 600 ms recebe metade das letras, e ai ser rapido VALE.
		// ====================================================================================================
		double vale = EmbateDeKi.ApertosPorLetra(MsMinimoEntreLetras / 1000.0);
		if (acertou) meu.Restante -= (MsPorTecla - MsMinimoEntreLetras) / 1000.0;
		meu.ApertosPendentes += acertou ? vale : -vale / meu.Vantagem;

		Julgou(p, acertou);
		return true;
	}

	/// <summary>
	/// O ROTEADOR DA TECLA CRUA. Um canal (`C2S.ClashTecla`), dois embates -- e quem sabe em qual
	/// deles o jogador esta e o servidor, que e quem sorteou a letra.
	/// </summary>
	private void TeclaDeQualquerEmbate(ServerPlayer p, char c)
	{
		if (!TeclaDoEmbateDeKi(p, c)) TeclaDoEmbate(p, c);
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// O `clash_loop` do DM, a 30 Hz em vez de 5: validade dos dois lados, aluguel de Ki, fisica do
	/// medidor, o encontro que caminha, e a resolucao.
	///
	/// NADA PESADO AQUI: o custo e O(disputas vivas), e uma disputa e um par de pessoas. A varredura
	/// que poderia doer e a do GATILHO, e ela mora no tique do projetil atras de dois portoes (so
	/// cabeca de raio, so quem esta canalizando) -- ver a familia de custo da bancada.
	/// </summary>
	private void TickDosEmbatesDeKi(double dt)
	{
		if (_disputas.Count == 0) return;

		for (int i = _disputas.Count - 1; i >= 0; i--)
		{
			DisputaDeKi d = _disputas[i];
			d.Corridos += dt;

			// ---- OS DOIS LADOS AINDA VALEM? `side_ok` do DM ----
			if (!LadoOk(d.A)) { Resolver(d, d.B, d.A, $"{d.A.Quem.Name} perde a firmeza"); continue; }
			if (!LadoOk(d.B)) { Resolver(d, d.A, d.B, $"{d.B.Quem.Name} perde a firmeza"); continue; }

			// ---- A LETRA VENCEU: outra, sem ponto. O relogio dela e o mesmo `dt` de todo o resto ----
			d.A.Restante -= dt;
			d.B.Restante -= dt;
			if (d.A.Restante <= 0) NovaLetra(d, d.A);
			if (d.B.Restante <= 0) NovaLetra(d, d.B);

			// ---- QUEM NAO TEM TECLADO EMPURRA SOZINHO. `side_presses` pra quem nao tem client ----
			ApertosDeQuemNaoTemTeclado(d.A, dt);
			ApertosDeQuemNaoTemTeclado(d.B, dt);

			// ---- A FISICA ----
			d.Medidor = EmbateDeKi.Empurrar(d.Medidor, d.A.ApertosPendentes, d.B.ApertosPendentes,
											d.A.Vantagem, d.B.Vantagem, dt);
			d.A.ApertosPendentes = d.B.ApertosPendentes = 0;

			MoverOEncontro(d);

			// ---- O CICLO DE 0,2 s: Ki, placar e estrago ----
			d.AteOCiclo -= dt;
			if (d.AteOCiclo <= 0)
			{
				d.AteOCiclo += EmbateDeKi.SegundosPorCiclo;
				d.Ciclos++;

				// `A.Ki = max(A.Ki - A.MaxKi * BCL_KI_PER_TICK, 0)` -- os DOIS pagam, sempre.
				foreach (LadoDeKi l in new[] { d.A, d.B })
					l.Quem.Ficha.Ki = Math.Max(l.Quem.Ficha.Ki - l.Quem.Ficha.MaxKi * EmbateDeKi.KiPorCiclo, 0);

				MandarPlacar(d.A.Quem, d.Medidor, 100 - d.Medidor);
				MandarPlacar(d.B.Quem, 100 - d.Medidor, d.Medidor);

				// A DISPUTA E COMBATE: `refresh_combat_tag()` dos dois, pra a tag e a musica de
				// batalha nao cairem no meio de um encontro que pode durar 22 s.
				d.A.Quem.Combate?.EntrarEmCombate();
				d.B.Quem.Combate?.EntrarEmCombate();

				// ~1x por segundo: onda de choque, poeira e o chao pagando o pato -- `fxtick % 5`.
				if (d.Ciclos % 5 == 0) Estourar(d, forte: false);
			}

			// ---- O DESFECHO ----
			switch (EmbateDeKi.Decidir(d.Medidor, d.Corridos))
			{
				case FimDeEmbateDeKi.VenceuA:
					Resolver(d, d.A, d.B, $"o poder de {d.A.Quem.Name} engole a disputa"); break;
				case FimDeEmbateDeKi.VenceuB:
					Resolver(d, d.B, d.A, $"o poder de {d.B.Quem.Name} engole a disputa"); break;
				case FimDeEmbateDeKi.Empate:
					Empatar(d); break;
			}
		}
	}

	/// <summary>
	/// `side_ok(M, H)` (`BeamClash.dm:150`): vivo, de pe, com o feixe na mao e com Ki.
	///
	/// ============================ DA PRA SAIR DO EMBATE, E SAIR E PERDER ============================
	/// A linha `if(!M.beaming) return 0` do DM e a saida: soltar o raio ENCERRA o seu lado, e o
	/// enunciado de la ("o beam de X perde a firmeza") ja diz o que acontece em seguida -- o do outro
	/// avanca. Aqui vale igual, e pro lado das MAOS a saida equivalente e ABAIXAR A GUARDA.
	///
	/// Ou seja: sair e sempre possivel e nunca e de graca. Quem larga leva o feixe do outro, com o
	/// dano cheio da cadeia de ki -- que e o mesmo desfecho de quem disputou e perdeu.
	/// ==========================================================================================
	/// </summary>
	private bool LadoOk(LadoDeKi l)
	{
		ServerPlayer p = l.Quem;
		if (!_players.ContainsKey(p.Id) || p.Ficha.dead || p.Ficha.KO) return false;
		if (p.Ficha.Ki <= 1) return false;

		if (l.DeMaos) return p.Combate is { Bloqueando: true };

		// O FEIXE precisa continuar vivo E continuar sendo alimentado pelo dono.
		return l.Feixe is { Vivo: true } && _canais.GetValueOrDefault(p.Id)?.Raio == l.Feixe;
	}

	/// <summary>
	/// `side_presses` pra quem nao tem `client`: a taxa automatica, escalada pela inteligencia
	/// (`BCL_NPC_CPS * (0.5 + intel/100)`).
	///
	/// O JOGADOR NAO PASSA POR AQUI -- ele empurra por letra. E e por construcao que os dois se
	/// medem: uma letra vale o que a taxa de um NPC MEDIANO renderia na CADENCIA MINIMA dela
	/// (`EmbateDeKi.ApertosPorLetra`), que e o ritmo de quem esta jogando no limite. Quem responde
	/// mais devagar recebe menos letras e empurra menos -- e essa e a diferenca inteira.
	/// </summary>
	private void ApertosDeQuemNaoTemTeclado(LadoDeKi l, double dt)
	{
		if (TemTeclado(l.Quem)) return;
		l.ApertosPendentes += EmbateDeKi.ApertosPorSegundo(InteligenciaDoCorpo(l.Quem)) * dt;
	}

	/// <summary>
	/// A INTELIGENCIA DESTE CORPO, 0..100 -- o `N.ai_intelligence` do DM. Quem nao e NPC (um jogador
	/// sem conexao, um clone) fica com a `intel = 50` que o proprio `BeamClash.dm:145` usa de padrao.
	/// </summary>
	private static double InteligenciaDoCorpo(ServerPlayer p)
		=> p.Cerebro is { } c ? Math.Clamp(c.Inteligencia * 100, 0, 100) : EmbateDeKi.InteligenciaPadrao;

	/// <summary>
	/// O ENCONTRO CAMINHA. **NOVO** -- ver `EmbateDeKi.Deslocamento` sobre o que o DM fazia.
	///
	/// A fracao do medidor vira PIXELS contra a folga que sobra ate o corpo de cada um, entao um
	/// encontro que nasceu colado no defensor nao tem pra onde recuar e um que nasceu no meio do campo
	/// tem os dois lados inteiros. As cabecas vao junto: elas nao andam mais sozinhas (`EmEmbate`), e
	/// o rastro de cada uma continua saindo da mao do dono -- entao o raio de quem esta ganhando
	/// ESTICA e o de quem esta perdendo ENCOLHE, sem uma linha de codigo de desenho.
	/// </summary>
	private static void MoverOEncontro(DisputaDeKi d)
	{
		double f = EmbateDeKi.Deslocamento(d.Medidor);
		float px = (float)(f >= 0 ? f * d.ParaB : f * d.ParaA);
		d.Ponto = d.PontoInicial + d.Eixo * px;

		if (d.A.Feixe != null) d.A.Feixe.Pos = d.Ponto;
		if (d.B.Feixe != null) d.B.Feixe.Pos = d.Ponto;
	}

	/// <summary>
	/// O ESTOURO NO PONTO DE ENCONTRO: baque na tela dos dois, chao rachado e, de vez em quando, o
	/// que estiver de pe em volta caindo.
	///
	/// Tudo reusado: `RacharChao` e `Arrasar` sao os mesmos do ZanzoClash e do golpe pesado, e o
	/// `ClashSub.Baque` ja faz o cliente soltar faisca, poeira e tremor (`World.Baque`). Um efeito
	/// novo aqui seria um segundo jeito de dizer "estourou".
	/// </summary>
	private void Estourar(DisputaDeKi d, bool forte)
	{
		double bp = Math.Max(d.A.Quem.Ficha.expressedBP, d.B.Quem.Ficha.expressedBP);
		RacharChao(d.A.Quem.Zone, d.Ponto, bp);
		if (forte || _rng.NextDouble() < ChanceDeQuebrarCenario) Arrasar(d.A.Quem.Zone, d.Ponto);

		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Baque);
		w.PutVec(d.Ponto);
		foreach (ServerPlayer o in ZoneList(d.Zona))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// O DESFECHO
	// =====================================================================
	/// <summary>
	/// UM LADO ENGOLIU O OUTRO. O `resolve()` + `push_phase()` do DM, e aqui os dois sao a MESMA
	/// coisa: o feixe vencedor e simplesmente SOLTO com o rumo do perdedor e uma velocidade de
	/// empurrao, e quem o leva ate o corpo e o tique de projetil de sempre.
	///
	/// ============================ POR QUE NAO HA UM LACO DE EMPURRAO ============================
	/// No DM o `push_phase` e um `while` proprio que anda tile a tile, engole os segmentos do
	/// perdedor, move o orbe e no fim devolve a cabeca ao `walk` normal -- 60 linhas que existem
	/// porque la o beam e um TREM de objetos e alguem precisa apagar os vagoes.
	///
	/// Aqui o feixe e um objeto so (camada 1), o rastro do perdedor morre com ele, e "avancar ate
	/// acertar" ja e exatamente o que <c>AndarProjetil</c> faz. Entao o empurrao e: destrava a cabeca,
	/// aponta pro perdedor, poe a velocidade do `BCL_PUSH_STEP` (0,2 s por tile -- mais lenta que o
	/// voo normal do raio, e e isso que faz a cena durar) e renova o alcance com o `BCL_PUSH_MAX`
	/// (*"alcance renovado: o empurrao nao morre por distancia no meio"*). O impacto sai pelo
	/// `Acertar`, com a cadeia de dano de ki e o membro sorteado, como qualquer outro tiro.
	/// ======================================================================================
	/// </summary>
	private void Resolver(DisputaDeKi d, LadoDeKi venc, LadoDeKi perd, string motivo)
	{
		Fechar(d);

		Projetil? hV = venc.Feixe;
		Projetil? hP = perd.Feixe;

		// O CANAL DO PERDEDOR CAI NOS DOIS CASOS -- `L.beaming = 0; L.stopbeaming()`.
		if (_canais.TryGetValue(perd.Quem.Id, out CanalDeKi? cp) && (hP == null || cp.Raio == hP))
			FecharCanal(perd.Quem.Id, cp, "seu ataque foi ENGOLIDO!");

		if (hV != null)
		{
			// ============================ FEIXE CONTRA FEIXE: O DO PERDEDOR MORRE ============================
			// E o `push_phase` do DM engolindo os segmentos do perdedor, resumido a uma linha porque
			// aqui o feixe e um objeto so.
			//
			// ESTA MORTE MORA DENTRO DESTE `if`, e ja morou fora. Fora, ela acontecia TAMBEM quando
			// quem vencia eram as MAOS -- e o ramo de baixo entao "devolvia" um feixe que ja estava
			// morto: o ataque mudava de dono, virava, e sumia no tique seguinte sem tocar em ninguem.
			// A bancada pegou isso com a unica afirmacao que podia pegar ("o ataque DEVOLVIDO acertou
			// quem o disparou"), porque todo o RESTO da cena estava certo.
			// ============================================================================================
			if (hP != null)
			{
				hP.EmEmbate = false;
				hP.JaDisputou = true;
				hP.Vivo = false;
				hP.Fim = FimDeProjetil.Defletido;
			}

			hV.EmEmbate = false;
			hV.JaDisputou = true;

			// `hW.BP = W.expressedBP * max(hW.wavemultipl,1)` -- a cabeca vencedora carrega o poder de
			// AGORA do dono, e nao o do instante em que o tiro saiu. Quem se transformou no meio da
			// disputa acerta com a forma nova.
			hV.Bp = venc.Quem.Ficha.expressedBP;

			// O RUMO E PRO CORPO DO PERDEDOR, e nao o rumo original.
			Vec2 ate = perd.Quem.Pos - hV.Pos;
			if (ate.LengthSquared > 1e-4f) hV.Rumo = ate.Normalized();

			RenovarParaOEmpurrao(hV);
		}
		// O DEFENSOR VENCEU DE MAOS LIMPAS: o feixe do agressor nao morre -- ele VIRA e volta.
		else if (hP != null) Devolver(hP, venc.Quem, perd.Quem);

		Anunciar(d, venc.Quem.Id, perd.Quem.Id);
		Avisar(venc.Quem, $"voce VENCE a disputa ({motivo}) -- seu ataque avanca!");
		Avisar(perd.Quem, "seu ataque esta sendo EMPURRADO DE VOLTA!!");
		foreach (ServerPlayer o in ZoneList(d.Zona))
			if (o != venc.Quem && o != perd.Quem)
				Avisar(o, $"{venc.Quem.Name} vence a disputa de ki contra {perd.Quem.Name}.");

		GD.Print($"[server] EMBATE DE KI acabou: {venc.Quem.Name} vence ({motivo}) "
				 + $"| medidor {d.Medidor:0.#} | {d.Corridos:0.#}s");
	}

	/// <summary>
	/// O FEIXE MUDA DE DONO -- e ele so acontece quando quem venceu estava de MAOS LIMPAS.
	///
	/// ============================ UM DEFEITO DO DM, CONSERTADO E ANOTADO ============================
	/// La a reflexao de um raio (`objects.dm:373-390`) tira o canal do atirador e manda o raio andar
	/// na direcao do defensor -- mas NAO troca o `proprietor`. Como `avoidusr = 1` faz o `Bump`
	/// ignorar o proprio dono (`if(M != proprietor || !avoidusr)`), o raio refletido atravessa quem
	/// atirou e vai bater na paisagem. A "reflexao" do original nao acerta ninguem.
	///
	/// Aqui o feixe passa a ser do defensor: e o que faz o `avoidusr` proteger a pessoa certa, e e a
	/// unica forma de o funil de derrota (`ResolverDesfecho` -> Zenkai, luto, raiva) creditar quem de
	/// fato venceu o encontro. Sem isso, "voce devolveu o Kamehameha" seria uma frase sem dano.
	/// ==========================================================================================
	/// </summary>
	private void Devolver(Projetil p, ServerPlayer novoDono, ServerPlayer alvo)
	{
		p.EmEmbate = false;
		p.JaDisputou = true;
		p.Canalizando = false;   // ninguem o alimenta mais: o rastro passa a voar solto
		p.Dono = novoDono.Id;
		p.Bp = novoDono.Ficha.expressedBP;
		p.Letal = novoDono.Combate?.Letal ?? p.Letal;
		p.Nome = $"{p.Nome} (devolvido)";

		Vec2 ate = alvo.Pos - p.Pos;
		if (ate.LengthSquared > 1e-4f) p.Rumo = ate.Normalized();

		RenovarParaOEmpurrao(p);

		// QUEM DEVOLVE E O AGRESSOR: sem esta linha, matar alguem com o proprio ataque dele nao
		// contaria como briga entre os dois.
		MarcarAgressao(alvo, novoDono);
	}

	/// <summary>
	/// O FEIXE VENCEDOR VAI CUMPRIR O EMPURRAO -- velocidade, alcance e PRAZO renovados.
	///
	/// ============================ AS TRES SAO A MESMA IDEIA ============================
	/// A velocidade e o `BCL_PUSH_STEP` (0,2 s por tile: mais lento que o voo normal do raio, e e o
	/// que faz a cena durar). O alcance e o `BCL_PUSH_MAX`, com a linha do DM junto -- *"alcance
	/// renovado: o empurrao nao morre por distancia no meio"* (`BeamClash.dm:308`).
	///
	/// E O PRAZO PELO MESMO ARGUMENTO. O `Burnout` sao 5 s desde o disparo; um feixe que passou 18 s
	/// numa disputa e venceu chegaria ao perdedor com o cronometro estourado e se apagaria a um tile
	/// do rosto dele. O DM nao tem este problema porque la a cabeca do raio e RECRIADA a cada 0,2 s
	/// pelo dono; aqui ela e um objeto so (camada 1), e renovar o prazo e o equivalente exato.
	/// ==============================================================================
	/// </summary>
	private static void RenovarParaOEmpurrao(Projetil p)
	{
		p.SegundosPorTile = EmbateDeKi.SegundosPorTileDoEmpurrao;
		p.Distancia = Math.Max(p.Distancia, EmbateDeKi.TilesDoEmpurrao);
		p.MaxDistancia = Math.Max(p.MaxDistancia, p.Distancia);
		p.VidaRestante = Math.Max(p.VidaRestante, Projetil.SegundosDeBurnout);
	}

	/// <summary>
	/// EMPATE NO PRAZO: `draw()` -- os dois feixes explodem no meio e os dois perdem o canal.
	///
	/// Ele existe pra que 22 segundos de disputa parelha nao terminem com um vencedor sorteado. A
	/// faixa e estreita de proposito (5 pontos de 100): fora dela, quem estava na frente leva.
	/// </summary>
	private void Empatar(DisputaDeKi d)
	{
		Fechar(d);

		foreach (LadoDeKi l in new[] { d.A, d.B })
		{
			if (l.Feixe != null)
			{
				l.Feixe.EmEmbate = false;
				l.Feixe.JaDisputou = true;
				l.Feixe.Vivo = false;
				l.Feixe.Fim = FimDeProjetil.Acertou;   // estouro, e nao "apagou": houve impacto
			}
			if (_canais.TryGetValue(l.Quem.Id, out CanalDeKi? c)) FecharCanal(l.Quem.Id, c, null);
			Avisar(l.Quem, "os dois ataques explodem em pe de igualdade!");
		}

		Estourar(d, forte: true);
		Anunciar(d, 0, 0);   // os dois zerados = empate. Ver `Protocol.ClashSub.Acabou`.
		GD.Print($"[server] EMBATE DE KI: EMPATE em {d.Corridos:0.#}s (medidor {d.Medidor:0.#})");
	}

	/// <summary>Tira a disputa das duas listas. So isso -- o que acontece com os feixes e de quem chamou.</summary>
	private void Fechar(DisputaDeKi d)
	{
		_emEmbateDeKi.Remove(d.A.Quem.Id);
		_emEmbateDeKi.Remove(d.B.Quem.Id);
		_disputas.Remove(d);
	}

	/// <summary>
	/// O FIM, PRA OS DOIS -- e so pra eles. Era um anuncio de ZONA, e o motivo de ter deixado de ser
	/// esta escrito no `Terminar` do ZanzoClash: com dois embates correndo na mesma zona, o fim de um
	/// derrubava o `EmClash` (e o silencio dos atalhos) e fechava a tela do OUTRO.
	///
	/// Aqui doia mais que la: esta disputa dura ate 22 s, entao ela e justamente a que costuma estar
	/// de pe quando um ZanzoClash de 3 s comeca e acaba ao lado.
	/// </summary>
	private void Anunciar(DisputaDeKi d, int venc, int perd)
	{
		Acabou(d.A.Quem, venc, perd);
		Acabou(d.B.Quem, venc, perd);
	}

	/// <summary>
	/// SOLTA QUEM ESTAVA NUMA DISPUTA -- pra quem caiu do jogo, morreu ou trocou de planeta.
	/// Chamado do mesmo lugar que o <c>SoltarDoEmbate</c> do ZanzoClash.
	/// </summary>
	private void SoltarDoEmbateDeKi(int id)
	{
		if (!_emEmbateDeKi.TryGetValue(id, out DisputaDeKi? d)) return;

		LadoDeKi quemSaiu = d.A.Quem.Id == id ? d.A : d.B;
		LadoDeKi oOutro = quemSaiu == d.A ? d.B : d.A;
		Resolver(d, oOutro, quemSaiu, $"{quemSaiu.Quem.Name} sai do encontro");
	}

	// =====================================================================
	// A IA: O CONTRA-FEIXE
	// =====================================================================
	/// <summary>
	/// HA UM FEIXE INIMIGO VINDO NA MINHA DIRECAO? `bcl_incoming_beam` (`BeamClash.dm:399`).
	///
	/// A geometria e a de la: cabeca de raio dentro de <c>TilesDeFaro</c>, de outro dono, e vindo
	/// PRA CIMA de mim (o rumo dela dentro de 45 graus da direcao que aponta pra mim). Um raio que
	/// passa de lado nao e ameaca, e responder a ele seria o NPC atirando pro nada.
	/// </summary>
	private bool VemFeixeContraMim(ServerPlayer npc, out Vec2 deOndeVem)
	{
		deOndeVem = default;
		float faro = (float)(EmbateDeKi.TilesDeFaro * ZoneCollision.TileSize);

		// SEM CRIAR LISTA: `ProjeteisDaZona` NASCE uma lista vazia pra zona que nao tem tiro nenhum, e
		// este metodo roda a 30 Hz por corpo dirigido -- ele encheria o dicionario de listas vazias, uma
		// por planeta habitado. Aqui a pergunta e so "ha tiro nesta zona?".
		if (!_projeteis.TryGetValue(npc.Zone.Hash, out List<Projetil>? daZona) || daZona.Count == 0)
			return false;

		foreach (Projetil k in daZona)
		{
			if (!k.Vivo || k.Tipo != TipoDeProjetil.Beam || k.EmEmbate || k.Dono == npc.Id) continue;
			if (!_players.ContainsKey(k.Dono)) continue;

			Vec2 ateMim = npc.Pos - k.Pos;
			float dist = ateMim.Length;
			if (dist > faro || dist < 1e-3f) continue;

			Vec2 u = ateMim.Normalized();
			if (k.Rumo.X * u.X + k.Rumo.Y * u.Y < 0.7f) continue;   // nao vem pra ca

			deOndeVem = k.Pos;
			return true;
		}
		return false;
	}

	/// <summary>
	/// O REFLEXO DO NPC: responder feixe com feixe. `NPCAI.dm:374-377`.
	///
	/// ============================ POR QUE ISTO NAO PASSA PELO CEREBRO ============================
	/// O `Cerebro` decide o que FAZER com o inimigo -- alcancar, pressionar, recuar, atirar --, e
	/// decide a 4 Hz com um compromisso minimo de tempo por plano. Um raio chegando nao e uma opcao
	/// tatica: e uma coisa que vai acertar em menos de um segundo, e o DM tratou isso como reflexo,
	/// fora da arvore de decisao, com recarga propria (`bcl_npc_beam_cd`).
	///
	/// Portei igual, e a razao vale a pena escrever: se isto virasse um `Plano`, o NPC teria que
	/// LARGAR o plano atual pra reagir, e a proxima decisao poderia trocar de plano no meio do
	/// contra-feixe -- justamente o que o `npc_beam_loop` do DM evita ao nao expirar durante a
	/// disputa. Reflexo e reflexo; plano e plano.
	/// =======================================================================================
	/// </summary>
	private void TickDoContraFeixe(ServerPlayer npc, long agora)
	{
		if (npc.Peer != null) return;                                  // reflexo e de quem nao tem dono
		if (_canais.ContainsKey(npc.Id)) return;                       // ja esta com um raio na mao
		if (npc.Ficha.KO || npc.Ficha.dead) return;
		if (_emEmbateDeKi.ContainsKey(npc.Id) || _emEmbate.ContainsKey(npc.Id)) return;
		if (_contraFeixeLivreEm.TryGetValue(npc.Id, out long livre) && agora < livre) return;

		// `Ki >= MaxKi * BCL_NPC_KI_MIN` -- o NPC nao entra num encontro que ele nao pode sustentar.
		if (npc.Ficha.Ki < npc.Ficha.MaxKi * EmbateDeKi.FracaoDeKiDoContraFeixe) return;

		// ============================ A ORDEM AQUI E ORCAMENTO, E FOI MEDIDA ============================
		// A pergunta GEOMETRICA vem antes da pergunta de SKILL, e nao por gosto: este metodo roda a
		// 30 Hz por corpo dirigido, `VemFeixeContraMim` nao aloca nada (sai na primeira linha quando a
		// zona nao tem tiro) e `SabeTecnica` varre o livro e o mapa de niveis -- com um enumerador
		// boxeado por chamada.
		//
		// Na ordem inversa, a bancada de alocacao da IA reprovou na hora: 200 bytes por tique com 20
		// corpos, num teste cujo alvo declarado e ZERO. Agora o livro so e aberto quando ha de fato um
		// feixe inimigo vindo -- que e uma vez a cada muitos minutos de briga.
		// ============================================================================================
		if (!VemFeixeContraMim(npc, out Vec2 dedonde)) return;

		// SO QUEM SABE O RAIO. A mesma porta do jogador: `SabeTecnica` -- um NPC que responde com uma
		// tecnica que ele nao tem seria a IA trapaceando, que e o defeito classico de "a IA obedece as
		// mesmas regras menos uma".
		if (!SabeTecnica(npc, "Ki_Wave")) return;

		_contraFeixeLivreEm[npc.Id] = agora + (long)(EmbateDeKi.SegundosEntreContraFeixes * 1000);

		// ENCARA O QUE VEM e responde pelo MESMO caminho do jogador -- `UsarTecnica`, nao um disparo
		// paralelo. A carga, o custo, a trava do corpo e o teto de tiros valem igual.
		npc.Facing = MoveRules.FacingFrom(dedonde - npc.Pos, npc.Facing);

		// E ELE GRITA. `npc_combat_chat(pick("HAAAAAA!!","Tome ISTO!","Nao vai me vencer nisso!"))`
		// LOGO ANTES do `npc_fire_beam` (`NPCAI.dm:378-379`) -- e no original ele e o UNICO ponto de
		// fala sem `prob()` na frente, porque responder um raio com o proprio raio ja e raro por
		// conta propria.
		//
		// A frase vem do `Falatorio` do cerebro (e nao de uma lista escrita aqui) porque e ELE que
		// tem o relogio de combate: sem isso o contra-feixe poderia falar por cima de um grito de
		// soco do mesmo segundo. Ver `Cerebro.FraseDoContraFeixe`.
		if (npc.Cerebro?.FraseDoContraFeixe(_rng) is { Length: > 0 } grito)
			Falar(npc, Protocol.Fala.Diz, grito);

		UsarTecnica(npc, "Ki_Wave");
	}

	/// <summary>
	/// O PRAZO DO RAIO DE QUEM NAO TEM TECLADO: `BCL_NPC_BEAM_TIME`, e ele **nao corre durante uma
	/// disputa** (`beamclash || world.time - started < BCL_NPC_BEAM_TIME`, `BeamClash.dm:430`).
	///
	/// E o que fecha o gancho da IA sem mexer no `Comando`: o cerebro solta UM pulso pra abrir o
	/// canal (que e tudo que ele sabe fazer) e este relogio o fecha. Sem ele, um NPC que largasse um
	/// Ki Wave ficaria plantado ate o Ki acabar -- e plantado, pro `PodeMexerOCorpo`, quer dizer sem
	/// andar, sem perseguir e sem fugir.
	/// </summary>
	private void TickDoPrazoDeRaioDaIa(double dt)
	{
		if (_canais.Count == 0) return;

		foreach (int id in _canais.Keys.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Peer != null) continue;
			if (_emEmbateDeKi.ContainsKey(id)) continue;   // numa disputa o prazo NAO corre

			double t = _raioDaIaAte.GetValueOrDefault(id) + dt;
			if (t < EmbateDeKi.SegundosDeFeixeDeNpc) { _raioDaIaAte[id] = t; continue; }

			_raioDaIaAte.Remove(id);
			FecharCanal(id, _canais[id], null);
		}

		// quem soltou o raio por conta propria some da conta
		foreach (int id in _raioDaIaAte.Keys.ToList())
			if (!_canais.ContainsKey(id)) _raioDaIaAte.Remove(id);
	}

	/// <summary>Ha quanto tempo cada corpo sem teclado esta segurando um raio. Ver o metodo acima.</summary>
	private readonly Dictionary<int, double> _raioDaIaAte = [];
}
