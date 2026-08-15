using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// AS FIXTURES DA BANCADA DA MUDEZ (`--mudezteste`) -- o lado do SERVIDOR do primeiro pedido do dono.
///
/// ============================ ELA NAO MEDE NADA ============================
/// Quem mede e o `Client/RoboDeMudez.cs` (`--diagmudez`), porque o que esta em jogo e do CLIENTE: as
/// teclas do jogador. O que este arquivo faz e a unica coisa que o cliente nao consegue fazer
/// sozinho -- **poe um embate de verdade de pe** -- e sai da frente.
///
/// Nada aqui simula estado no cliente. O `EmClash` do robo nasce do MESMO pacote
/// (`ClashSub.Comecou`) que nasce numa briga, porque aqui embate e embate: o gatilho e o
/// `TentarEmbate`, o encontro de feixes e o `TentarEmbateDeFeixes` de dentro do tique, o fim e o
/// `Terminar`. Se qualquer um deles quebrar, a bancada do cliente nao arma e diz isso.
/// ==========================================================================
///
/// ============================ OS DOIS PRIVILEGIOS, E POR QUE ============================
///   1. **A TROCA SIMULTANEA E ESCRITA, E NAO SOCADA.** `TentarEmbate` pede que o outro tenha me
///      acertado ha menos de 700 ms (`UltimoAlvo`/`UltimoSocoMs`), e esses dois campos sao o que o
///      soco escreve. Dar o soco de verdade ARREMESSARIA o corpo do jogador pelo mapa (o pesado
///      arremessa, e o leve as vezes) -- e o que esta bancada mede depois e se o **teclado** dele
///      responde, com o corpo onde ele o deixou. O gatilho continua sendo o de producao: a condicao
///      da skill, a da zona, a do nocaute e a do sorteio passam todas.
///   2. **O SORTEIO SAI DE CENA** (`_clashSempre`, a mesma flag da `--clashteste`): um embate que
///      acontece metade das vezes faria a bancada do cliente medir o dado.
/// =====================================================================================
///
/// COMO RODAR -- um processo so, hospedando (o robo do cliente sobe junto):
///     Godot --headless --path . --host --rede 7918 --mudezteste --kiteste --bpteste 200000
///           --conta bancada_mudez --nome Mudo --raca Saiyan --diagmudez
/// </summary>
public partial class GameServer
{
	/// <summary>Ligada por `--mudezteste`. Sem ela, TODA fixture daqui devolve falso e nao toca no mundo.</summary>
	private bool _mudezDeTeste;

	private const int IdBaseDaMudez = 92_700;
	private int _forjadosDaMudez;

	/// <summary>Os corpos que esta fixture pos no mundo, pra saírem todos no fim.</summary>
	private readonly List<ServerPlayer> _corposDaMudez = [];

	// =====================================================================
	// OS CORPOS
	// =====================================================================
	/// <summary>
	/// UM ADVERSARIO ao lado do jogador, na zona DELE -- e nao na zona de bancada das outras: o
	/// embate exige `a.Zone.Hash == d.Zone.Hash`, e o jogador esta onde o jogo o pos.
	/// </summary>
	private ServerPlayer ForjarRivalDaMudez(ServerPlayer perto, float dx, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDaMudez + _forjadosDaMudez++,
			Peer = null,
			Name = $"RivalDaMudez{_forjadosDaMudez}",
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = perto.Zone,
			Pos = perto.Pos + new Vec2(dx, 0),
			Conta = "bancada_mudez",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Human", BP = bp },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		// `PorNoMundo` faz os ATRIBUTOS; sem este tique o `expressedBP` nasce zero e a vantagem de
		// poder do embate dividiria por um numero que nao existe. Mesma pegadinha das outras bancadas.
		novo.Ficha.Tick(agoraMs: NowMs());
		_corposDaMudez.Add(novo);
		return novo;
	}

	/// <summary>Deixa o corpo pronto pra entrar num embate: de pe, inteiro, com energia e sem rastro.</summary>
	private void ProntoParaOEmbateDaMudez(ServerPlayer p)
	{
		if (p.Ficha.KO) p.Combate.Levantar();
		p.Combate.Corpo.Restaurar();
		p.Combate.SincronizarVida();
		p.Combate.Stun = 0;
		p.Combate.Recarga = 0;
		p.AtaqueAte = 0;
		p.Ficha.Ki = p.Ficha.MaxKi;
		p.UltimoAlvo = 0;
		p.UltimoSocoMs = 0;
		_sorteioLivreEm.Remove(p.Id);
		// O ARREMESSO DO EMBATE ANTERIOR: quem esta voando nao entra em embate nenhum (`!move`), e a
		// bancada arma varios seguidos. O laco e o de producao.
		int giros = 0;
		while (p.TiquesDeVoo > 0 && giros++ < 400) TickDoEmpurrao();
	}

	// =====================================================================
	// 1) O EMBATE DE VELOCIDADE
	// =====================================================================
	/// <summary>
	/// ARMA UM ZANZO CLASH DE VERDADE entre o jogador e um corpo forjado ao lado dele.
	///
	/// Devolve falso quando o gatilho de producao recusou -- e ai a bancada do cliente reprova
	/// dizendo isso, em vez de medir o silencio de um embate que nunca houve.
	/// </summary>
	public bool EmbateDeVelocidadeDeTeste(int idDoJogador)
	{
		if (!_mudezDeTeste || !_players.TryGetValue(idDoJogador, out ServerPlayer? eu)) return false;

		SoltarDoEmbate(eu.Id);
		ProntoParaOEmbateDaMudez(eu);

		ServerPlayer rival = ForjarRivalDaMudez(eu, dx: 28, bp: Math.Max(eu.Ficha.BP, 1));
		ProntoParaOEmbateDaMudez(rival);

		// A CONDICAO DA SKILL NAO E PULADA POR FLAG NENHUMA (`haszanzo && M.haszanzo`): os dois
		// aprendem a Imagem Remanescente pelo livro de verdade.
		eu.Livro?.Dar(PathDoZanzoken);
		rival.Livro?.Dar(PathDoZanzoken);

		// A TROCA SIMULTANEA, escrita nos dois campos que o soco escreveria -- ver o cabecalho.
		rival.UltimoAlvo = eu.Id;
		rival.UltimoSocoMs = NowMs();

		bool foi = TentarEmbate(eu, rival);
		GD.Print($"[mudez] embate de VELOCIDADE armado contra {rival.Name}: {(foi ? "comecou" : "RECUSADO")}");
		return foi;
	}

	// =====================================================================
	// 2) A COLISAO DE KI
	// =====================================================================
	/// <summary>
	/// ARMA UMA COLISAO DE KI DE VERDADE: dois raios canalizados, de frente, e o gatilho de dentro do
	/// tique do projetil (`TentarEmbateDeFeixes`) descobrindo o encontro sozinho.
	///
	/// A bancada do cliente ESPERA o `Comecou` chegar -- este metodo so acende os dois feixes. Se o
	/// gatilho nao disparar, ninguem finge que disparou.
	/// </summary>
	public bool EmbateDeKiDeTeste(int idDoJogador)
	{
		if (!_mudezDeTeste || !_players.TryGetValue(idDoJogador, out ServerPlayer? eu)) return false;

		SoltarDoEmbate(eu.Id);
		SoltarDoRaio(eu.Id);
		ProntoParaOEmbateDaMudez(eu);

		// DOZE TILES DE VAO: e o mesmo da bancada da colisao de ki, e ele existe pra os dois raios
		// terem chao pra andar antes de se encontrarem -- o encontro nasce no meio.
		ServerPlayer rival = ForjarRivalDaMudez(eu, dx: 12 * ZoneCollision.TileSize, bp: Math.Max(eu.Ficha.BP, 1));
		ProntoParaOEmbateDaMudez(rival);

		eu.Facing = Facing.East;
		rival.Facing = Facing.West;

		Canalizar(eu, "Ki_Wave", 10 * eu.Ficha.BaseDrain(), RaioDeTeste());
		Canalizar(rival, "Ki_Wave", 10 * rival.Ficha.BaseDrain(), RaioDeTeste());
		GD.Print($"[mudez] dois feixes acesos contra {rival.Name} -- o gatilho e do tique "
				 + $"(canal meu={_canais.ContainsKey(eu.Id)}, dele={_canais.ContainsKey(rival.Id)}, "
				 + $"train={eu.Ficha.train} med={eu.Ficha.med} ki={eu.Ficha.Ki:0}/{eu.Ficha.MaxKi:0})");
		return true;
	}

	/// <summary>
	/// UM CORPO NO CHAO AOS PES DO JOGADOR -- o que a tecla E precisa pra ter o que abrir.
	///
	/// ============================ POR QUE A BANCADA PRECISA DISTO ============================
	/// O menu da tecla E nao abre no vazio: ele quer uma obra, um veiculo ou um CADAVER por perto
	/// (`MenuDeInteracao.Abrir`). Sem nenhum dos tres, o E fica mudo com o jogo intacto -- e o
	/// contra-exemplo da bancada ("fora do embate ele ABRE") reprovaria por falta de alvo, acusando
	/// de defeito o unico caso em que o jogo esta certo.
	///
	/// O CORPO NASCE PELO CAMINHO DE PRODUCAO (`DeixarOCadaver`, o mesmo do `GameServer.Combat`
	/// quando alguem morre), e nao por um objeto forjado: e o cadaver de verdade que o cliente sabe
	/// desenhar e que o `S2C.Cadaver` anuncia.
	/// ====================================================================================
	/// </summary>
	public bool CadaverPertoDeTeste(int idDoJogador)
	{
		if (!_mudezDeTeste || !_players.TryGetValue(idDoJogador, out ServerPlayer? eu)) return false;

		// A ESQUERDA (-24): a bancada mede o movimento andando pra DIREITA, e um corpo posto no
		// caminho vira parede. Ver a nota no `F0` do robo.
		ServerPlayer vitima = ForjarRivalDaMudez(eu, dx: -24, bp: 100);
		vitima.Ficha.dead = true;
		ServerPlayer corpo = DeixarOCadaver(vitima);
		_corposDaMudez.Add(corpo);

		// A VITIMA SAI DE CENA: o cadaver e um corpo NOVO (ficha propria, fora do `_players`), e
		// deixar o original de pe poria dois bonecos no mesmo lugar.
		_players.Remove(vitima.Id);
		ZoneList(vitima.Zone.Hash).Remove(vitima);
		_corposDaMudez.Remove(vitima);

		MandarCadaverPerto(eu);
		GD.Print($"[mudez] um corpo no chao ao lado de {eu.Name} (pra a tecla E ter alvo)");
		return true;
	}

	// =====================================================================
	// 3) O EMBATE QUE O SERVIDOR ESQUECEU
	// =====================================================================
	/// <summary>
	/// SO O ANUNCIO, SEM EMBATE NENHUM. E o **pacote de fim que se perdeu**, visto do cliente.
	///
	/// ============================ POR QUE ISTO PRECISA EXISTIR ============================
	/// O silencio dos atalhos e DERIVADO de um prazo (`GameClient.EmClash`), e nao de um bit: o
	/// projeto ja pagou uma vez o preco de um estado que so um pacote apaga. O prazo existe pro caso
	/// em que o `Acabou` **nao chega** -- e esse caso, por construcao, nao se produz mandando o
	/// servidor terminar o embate (terminar MANDA o pacote).
	///
	/// Entao a fixture manda o `Comecou` e mais nada. Repare que ela usa o escritor de producao
	/// (`Comeco`), com um `Embate` que nao entra em lista nenhuma: nao ha uma segunda escrita do
	/// pacote, que envelheceria calada no dia em que ele ganhasse um campo.
	/// ====================================================================================
	/// </summary>
	public bool AnuncioSemEmbateDeTeste(int idDoJogador, int ms)
	{
		if (!_mudezDeTeste || !_players.TryGetValue(idDoJogador, out ServerPlayer? eu)) return false;

		long agora = NowMs();
		var fantasma = new Embate { A = eu, B = eu, Acaba = agora + ms };
		Comeco(fantasma, eu, eu, agora, 1, 1);
		GD.Print($"[mudez] anuncio SOLITARIO de {ms} ms (o fim nunca vai chegar)");
		return true;
	}

	// =====================================================================
	// 4) O EMBATE DOS OUTROS
	// =====================================================================
	/// <summary>
	/// UM EMBATE ENTRE DOIS TERCEIROS, na minha zona, que COMECA E ACABA enquanto o meu corre.
	///
	/// E o defeito que o fim pessoal consertou: enquanto o `Acabou` era anuncio de ZONA, o fim da
	/// briga alheia derrubava o `EmClash` de quem ainda estava no proprio embate -- teclado
	/// destravado no meio, tela do quick time event fechada. Numa briga de quatro isso e o normal, e
	/// nao o raro.
	///
	/// Os dois sao corpos sem `Peer`: o que chega ao jogador por causa deles e exatamente o que o
	/// jogo manda -- hoje, nada.
	/// </summary>
	public bool EmbateAlheioDeTeste(int idDoJogador)
	{
		if (!_mudezDeTeste || !_players.TryGetValue(idDoJogador, out ServerPlayer? eu)) return false;

		ServerPlayer a = ForjarRivalDaMudez(eu, dx: 6 * ZoneCollision.TileSize, bp: 5_000);
		ServerPlayer b = ForjarRivalDaMudez(eu, dx: 6 * ZoneCollision.TileSize + 28, bp: 5_000);
		ProntoParaOEmbateDaMudez(a);
		ProntoParaOEmbateDaMudez(b);
		a.Livro?.Dar(PathDoZanzoken);
		b.Livro?.Dar(PathDoZanzoken);

		b.UltimoAlvo = a.Id;
		b.UltimoSocoMs = NowMs();
		if (!TentarEmbate(a, b)) { GD.Print("[mudez] o embate ALHEIO nao comecou"); return false; }

		// E ACABA NA HORA: o que se mede do outro lado e o que o FIM dele faz com o meu teclado.
		SoltarDoEmbate(a.Id);
		GD.Print("[mudez] embate ALHEIO comecou e acabou (o meu continua)");
		return true;
	}

	/// <summary>
	/// O DEFEITO INJETADO do embate alheio: manda pro JOGADOR o `Acabou` de uma briga que nao e dele
	/// -- que e, byte por byte, o que o `AvisarZona` fazia chegar.
	///
	/// Ela existe pra a afirmacao de cima poder ficar VERMELHA. Sem ela, "o meu embate continuou"
	/// seria verde tambem num cliente que ignorasse o `Acabou` inteiro.
	/// </summary>
	public bool FimAlheioComoZonaDeTeste(int idDoJogador)
	{
		if (!_mudezDeTeste || !_players.TryGetValue(idDoJogador, out ServerPlayer? eu)) return false;
		Acabou(eu, IdBaseDaMudez, IdBaseDaMudez + 1);
		GD.Print("[mudez] DEFEITO INJETADO: o fim da briga alheia mandado pro jogador (o `AvisarZona` de antes)");
		return true;
	}

	// =====================================================================
	// 5) ACABAR
	// =====================================================================
	/// <summary>Encerra o meu embate agora, pelo caminho de producao (com `Acabou` e tudo).</summary>
	public bool SoltarEmbateDeTeste(int idDoJogador)
	{
		if (!_mudezDeTeste || !_players.ContainsKey(idDoJogador)) return false;
		SoltarDoEmbate(idDoJogador);
		SoltarDoRaio(idDoJogador);
		foreach (DisputaDeKi d in _disputas.ToList())
			if (d.A.Quem.Id == idDoJogador || d.B.Quem.Id == idDoJogador)
				Empatar(d);
		return true;
	}

	/// <summary>
	/// MATA O ADVERSARIO NO MEIO DO EMBATE -- o fim "anormal" que o jogo de verdade produz
	/// (`e.A.Ficha.dead` no tique), e nao um `Terminar` chamado na mao.
	/// </summary>
	public bool MatarAdversarioDeTeste(int idDoJogador)
	{
		if (!_mudezDeTeste || !_emEmbate.TryGetValue(idDoJogador, out Embate? e)) return false;
		ServerPlayer outro = e.A.Id == idDoJogador ? e.B : e.A;
		outro.Ficha.dead = true;
		GD.Print($"[mudez] {outro.Name} morreu no meio do embate -- o tique vai fechar o encontro");
		return true;
	}

	/// <summary>Tira do mundo tudo o que esta fixture pos nele.</summary>
	public void LimparAMudezDeTeste()
	{
		if (!_mudezDeTeste) return;
		foreach (ServerPlayer p in _corposDaMudez)
		{
			SoltarDoEmbate(p.Id);
			SoltarDoRaio(p.Id);
			LimparProjeteisDeUmDono(p.Id, p.Zone.Hash);
			_players.Remove(p.Id);
			ZoneList(p.Zone.Hash).Remove(p);
		}
		_corposDaMudez.Clear();
	}
}
