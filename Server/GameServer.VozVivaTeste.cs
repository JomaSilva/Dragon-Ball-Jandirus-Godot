using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ A VOZ COM CLIENTES DE VERDADE -- `--vozviva` ============================
/// A IRMA VIVA da <see cref="RodarBancadaDaVoz"/> (`--vozteste`), na mesma divisao de trabalho que a
/// `--menteviva` tem com a `--menteteste`. E ela existe por uma razao que ja estava ESCRITA, com todas
/// as letras, no cabecalho da bancada de regra:
///
///     *"O QUE ESTA BANCADA NAO PROVA: que o pacote chegou no fio. Corpo forjado nao tem `Peer`, e
///     pacote que saiu nao volta pra ser conferido."*
///
/// Ou seja: o CORTE estava medido e a ENTREGA nunca. E "entrega" e onde mora a unica afirmacao que
/// separa este sistema de um vazamento -- *"quem esta longe nao recebe byte nenhum"*. Um leque vazio
/// prova que o servidor NAO IA mandar; so um segundo processo contando bytes prova que ele NAO MANDOU.
///
/// ============================ POR QUE PRECISA DE PROCESSOS SEPARADOS ============================
/// Duas razoes, e as duas sao intransponiveis num processo so:
///
///   1. **`Peer == null` esta no caminho da entrega** (`GameServer.Voz.cs`: *"ter conexao nao e ter
///      direito de ouvir"*). Corpo forjado nao tem conexao, entao o `Peer.Send` -- a linha que este
///      trabalho inteiro existe pra vigiar -- nunca foi executada por bancada nenhuma.
///   2. **O ouvinte tem que ser outra memoria.** Com um processo so, o PCM que "chegou" seria o mesmo
///      array que saiu: mediria o codec (que a `--diagvoz` ja mede) e nao mediria fio nenhum.
///
/// ============================ O SERVIDOR DIRIGE, OS DOIS CLIENTES MEDEM ============================
/// Aqui o PLACAR nao mora todo no servidor, e isso e uma diferenca proposital pra `--menteviva`. O que
/// esta bancada pergunta e *"o que chegou no OUVIDO do outro?"*, e essa pergunta so tem resposta do
/// lado de la: amplitude, espectro e abafada sao amostras que so existem depois do decodificador do
/// cliente B. O que o servidor faz aqui e o que so ele pode fazer:
///
///   * **por os corpos onde a cena precisa** (autoridade: `pl.Pos` e dele, e o cliente e corrigido);
///   * **anunciar a fase** pelo canal de texto que ja existe, pra os dois lados baldearem no mesmo
///     instante sem um pacote novo so pra bancada;
///   * **contar o que ELE entregou** (<see cref="EspiaoDaVoz"/>) -- que e a versao da autoridade do
///     mesmo numero que o cliente B conta do lado dele. As duas contagens tem que bater; quando nao
///     baterem, a diferenca e perda de rede e nao regra.
///
/// ============================ AS OITO FASES ============================
///   0. `aquecimento` -- os corpos assentam e o Opus sai do primeiro quadro frio. Ninguem mede.
///   1. `perto`   --  2 tiles.  Item 1 (a onda atravessa) e o 1o falante do item 6 (banda).
///   2. `medio`   -- 11 tiles.  Item 2 (a amplitude cai).
///   3. `longe`   -- 21 tiles.  Item 2, ainda DENTRO dos 22.
///   4. `fora`    -- 30 tiles.  **Item 3: tem que ser ZERO byte, e nao "baixinho".**
///   5. `parede`  -- o par de celulas com parede de verdade no meio (o `.vis` do mapa).
///   6. `aberto`  -- **a MESMA distancia da fase 5, sem parede.** Sem esta fase, a 5 estaria medindo
///                   distancia e chamando de parede.
///   7. `tres`    -- tres falantes ao mesmo tempo. O 2o numero do item 6.
/// ================================================================
///
/// ============================ O QUE ELA NAO FAZ: JULGAR ============================
/// Ela IMPRIME TABELAS -- amplitude, espectro, banda -- e quem le decide se o numero esta bom. Nenhuma
/// linha dela e OK ou FALHA. A irma que da veredito por familia, e que poe o DEFEITO na frente de cada
/// regra pra exigir a linha vermelha, e a `--vozdupla` (`GameServer.VozDuplaTeste.cs`), com dois corpos.
/// As duas convivem porque respondem perguntas diferentes: *"quanto?"* e *"a checagem sabe olhar?"*.
/// ==============================================================================
///
/// COMO RODAR: `testar-voz.bat` (quatro processos). Ou na mao:
///     Godot --headless --path . --host --rede 7982 --vozviva --conta bancada_vozviva_a --vozviva-papel a
///     Godot --headless --path . --rede 7982 --connect 127.0.0.1 --conta bancada_vozviva_b --vozviva-papel b
///     (+ os papeis c e d, que so falam na fase 7)
/// </summary>
public partial class GameServer
{
	/// <summary>Ligada por `--vozviva`. Ver o cabecalho.</summary>
	private bool _vozVivaLigada;

	/// <summary>Quantos clientes de verdade a cena precisa antes de comecar (`--vozvivagente N`).</summary>
	private int _vozVivaGente = 4;

	private bool _vozVivaJaRodou;

	/// <summary>
	/// SEGUNDOS DE CADA FASE. Tres.
	///
	/// Nao e um numero redondo por acaso: a 50 quadros por segundo sao **150 quadros de audio** por
	/// fase, e a media de amplitude e de espectro de 150 quadros nao se move por um engasgo de rede.
	/// Com um segundo (50 quadros) um unico lote perdido mexeria no terceiro decimal de tudo; com dez,
	/// a bancada inteira passaria de um minuto e ninguem a rodaria.
	/// </summary>
	private const double SegundosPorFaseDaVoz = 3.0;

	private int _vvFase = -1;
	private long _vvViraEm;

	private ServerPlayer? _vvFalante, _vvOuvinte;
	private readonly List<ServerPlayer> _vvExtras = [];

	private ZoneKey _vvZona;
	/// <summary>Onde o OUVINTE fica em todas as fases de distancia -- a ponta do corredor livre.</summary>
	private Vec2 _vvOuve;
	/// <summary>O par com PAREDE de verdade entre os dois, achado no `.vis` do mapa.</summary>
	private Vec2 _vvParedeA, _vvParedeB;
	/// <summary>O par SEM parede, na MESMA distancia do de cima -- o controle da fase 5.</summary>
	private Vec2 _vvAbertoA, _vvAbertoB;
	/// <summary>Onde os falantes extras esperam: longe o bastante pra nao entrarem no leque.</summary>
	private Vec2 _vvExilio;

	/// <summary>O que o SERVIDOR entregou nesta fase: quadros e bytes de audio, por ouvinte.</summary>
	private readonly Dictionary<int, (int Quadros, long Bytes)> _vvEntregue = [];

	// =====================================================================
	// A PORTA
	// =====================================================================
	/// <summary>
	/// Chamada no fim do <c>Entrar</c>, como a `--menteviva` -- e pelo mesmo motivo que ela: o corpo que
	/// acabou de chegar so esta na `ZoneList` depois de tudo o que vem antes.
	/// </summary>
	private void VozVivaNoLogin()
	{
		if (!_vozVivaLigada || _vozVivaJaRodou) return;

		int vivos = _players.Values.Count(p => p.Peer != null);
		if (vivos < _vozVivaGente)
		{
			GD.Print($"[vozviva] {vivos} de {_vozVivaGente} clientes de verdade no ar -- esperando.");
			return;
		}

		// DOIS SEGUNDOS, o mesmo atraso (e o mesmo motivo) da `--menteviva`: os quatro clientes ainda
		// estao mandando posicao, e mexer no corpo de alguem no meio disso poria a bancada brigando
		// com o proprio anti-cheat de movimento.
		_vozVivaJaRodou = true;
		GetTree().CreateTimer(2.0).Timeout += ComecarAVozViva;
	}

	// =====================================================================
	// A CENA
	// =====================================================================
	private void ComecarAVozViva()
	{
		GD.Print("[vozviva] ============ A VOZ, COM CLIENTES DE VERDADE ============");

		List<ServerPlayer> gente = _players.Values.Where(p => p.Peer != null).ToList();

		// OS PAPEIS SAEM DA CONTA, e nao da ordem de chegada. A ordem de login e uma corrida entre
		// quatro processos -- ela ja inverteu falante e ouvinte em bancadas deste projeto antes, e o
		// sintoma seria a bancada medindo o silencio de quem nunca foi mandado falar.
		_vvFalante = gente.FirstOrDefault(p => p.Conta.EndsWith("_a", StringComparison.Ordinal));
		_vvOuvinte = gente.FirstOrDefault(p => p.Conta.EndsWith("_b", StringComparison.Ordinal));
		_vvExtras.Clear();
		_vvExtras.AddRange(gente.Where(p => p.Conta.EndsWith("_c", StringComparison.Ordinal)
										 || p.Conta.EndsWith("_d", StringComparison.Ordinal)));

		if (_vvFalante == null || _vvOuvinte == null)
		{
			GD.PrintErr("[vozviva] FALHA: nao achei as contas *_a (falante) e *_b (ouvinte). "
					  + $"Contas no ar: {string.Join(", ", gente.Select(p => p.Conta))}");
			GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(1);
			return;
		}

		if (!AcharOPalcoDaVoz())
		{
			GD.PrintErr("[vozviva] FALHA: nao achei uma zona com parede de verdade E corredor livre.");
			GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(1);
			return;
		}

		// TODO MUNDO PRA MESMA ZONA, pelo funil de verdade. `MoveToZone` e o que uma passagem de mapa
		// chama: ele reemite cenario, portas e correcao de posicao. Fazer na mao deixaria os quatro
		// clientes desenhando a zona antiga com corpos na zona nova.
		foreach (ServerPlayer p in gente) MoveToZone(p.Id, _vvZona, _vvOuve);

		EspiaoDaVoz = ContarEntrega;

		_vvFase = -1;
		_vvViraEm = NowMs();   // a proxima batida do tique ja vira a fase 0
	}

	/// <summary>
	/// ACHA A ZONA DA CENA: ela precisa das DUAS coisas, e no MESMO mapa.
	///
	/// ============================ POR QUE AS DUAS TEM QUE SER O MESMO MAPA ============================
	/// A fase da parede so quer dizer alguma coisa contra um controle -- a MESMA distancia, sem parede
	/// (item 4 do pedido: *"senao voce esta medindo distancia e chamando de parede"*). Se o controle
	/// rodasse noutro mapa, sobrariam duas diferencas entre as duas medidas em vez de uma, e a
	/// comparacao deixaria de dizer qualquer coisa sobre paredes.
	/// ==============================================================================================
	/// </summary>
	private bool AcharOPalcoDaVoz()
	{
		if (_catalogo == null) return false;

		const float t = ZoneCollision.TileSize;

		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.Vista is not { } vis || e.Mapa is not { } col) continue;

			if (!AcharParedeEntreDoisLivres(vis, out (int X, int Y) pa, out (int X, int Y) pb)) continue;

			// O CORREDOR PRECISA DE 31 CELULAS LIVRES: a fase mais longa poe o falante a 30 tiles do
			// ouvinte (fora do alcance, item 3), e as duas pontas tem que ser chao de verdade.
			if (!AcharCorredorLivre(vis, col, 32, out (int X, int Y) ini)) continue;

			int dParede = Math.Abs(pb.X - pa.X) + Math.Abs(pb.Y - pa.Y);
			if (dParede >= 30) continue;   // o controle precisa caber no mesmo corredor

			_vvZona = ZoneKey.Premade(e.Zona);
			_vvOuve = new Vec2((ini.X + 0.5f) * t, (ini.Y + 0.5f) * t);
			_vvParedeA = new Vec2((pa.X + 0.5f) * t, (pa.Y + 0.5f) * t);
			_vvParedeB = new Vec2((pb.X + 0.5f) * t, (pb.Y + 0.5f) * t);
			_vvAbertoA = _vvOuve;
			_vvAbertoB = new Vec2((ini.X + dParede + 0.5f) * t, (ini.Y + 0.5f) * t);

			// O EXILIO DOS EXTRAS: 60 tiles pra baixo. Bem alem dos 22, e por isso eles somem do leque
			// sem que a bancada precise mandar ninguem parar de falar -- o corte do servidor faz o
			// silencio deles, que e o mesmo corte que a bancada esta medindo.
			_vvExilio = _vvOuve + new Vec2(0, 60 * t);

			GD.Print($"[vozviva] palco: {e.Zona} | corredor em ({ini.X},{ini.Y}) | "
				   + $"parede ({pa.X},{pa.Y})->({pb.X},{pb.Y}) = {dParede} tiles");
			return true;
		}
		return false;
	}

	/// <summary>
	/// Um trecho HORIZONTAL de <paramref name="quantas"/> celulas livres no `.vis` **e** no `.col`.
	///
	/// As duas condicoes, e nao uma: livre so no `.vis` deixaria os corpos empilhados numa pedra (o
	/// `.col` os empurraria de volta no primeiro pacote de movimento, e a distancia medida nao seria a
	/// que a bancada mandou); livre so no `.col` deixaria uma parede invisivel no meio do corredor, e
	/// a fase "aberto" -- que e o CONTROLE da parede -- sairia marcada com parede.
	/// </summary>
	private static bool AcharCorredorLivre(ZoneCollision vis, ZoneCollision col, int quantas,
										   out (int X, int Y) inicio)
	{
		for (int y = 8; y < vis.Height - 8; y += 2)
		{
			int corrido = 0;
			for (int x = 8; x < vis.Width - 8; x++)
			{
				bool livre = !vis.BlockedCell(x, y) && !col.BlockedCell(x, y)
						  && !vis.BlockedCell(x, y - 1) && !col.BlockedCell(x, y - 1)
						  && !vis.BlockedCell(x, y + 1) && !col.BlockedCell(x, y + 1);
				corrido = livre ? corrido + 1 : 0;
				if (corrido < quantas) continue;
				inicio = (x - quantas + 1, y);
				return true;
			}
		}
		inicio = default;
		return false;
	}

	// =====================================================================
	// O TIQUE
	// =====================================================================
	/// <summary>
	/// Vira a fase quando der a hora. Chamada do <c>Tick</c>, e ela sai na primeira linha em jogo.
	/// </summary>
	private void TickDaVozViva()
	{
		if (!_vozVivaLigada || _vvFase < -1 || _vvOuvinte == null || _vvFalante == null) return;

		long agora = NowMs();
		if (agora < _vvViraEm) return;

		// O PLACAR DA FASE QUE ACABOU sai ANTES de mexer nos corpos: mexer primeiro faria o ultimo
		// quadro de audio da fase velha ser contado com a posicao da fase nova.
		if (_vvFase >= 0) RelatarFaseDaVoz();

		_vvFase++;
		_vvEntregue.Clear();

		const float t = ZoneCollision.TileSize;
		string nome;
		float distEsperada;
		bool paredeEsperada = false;

		switch (_vvFase)
		{
			case 0:
				nome = "aquecimento"; distEsperada = 2 * t;
				PorNoLugar(_vvOuvinte, _vvOuve);
				PorNoLugar(_vvFalante, _vvOuve + new Vec2(distEsperada, 0));
				// CADA EXTRA NUM EXILIO PROPRIO, **40 tiles** um do outro -- e o numero e 40 e nao 20
				// porque o alcance e 22: empilhados (ou a 20) eles ouviriam UM AO OUTRO, e o log do
				// servidor passaria a dizer "2 outros corpos tambem ouviram" numa fase em que o falante
				// esta sozinho. E uma linha que parece vazamento e nao e -- ou seja, o pior tipo de
				// ruido num placar que existe pra provar que nao ha vazamento.
				for (int i = 0; i < _vvExtras.Count; i++)
					PorNoLugar(_vvExtras[i], _vvExilio + new Vec2(0, i * 40 * t));
				break;

			case 1: nome = "perto"; distEsperada = 2 * t; PorEmLinha(distEsperada); break;
			case 2: nome = "medio"; distEsperada = 11 * t; PorEmLinha(distEsperada); break;
			case 3: nome = "longe"; distEsperada = 21 * t; PorEmLinha(distEsperada); break;
			case 4: nome = "fora"; distEsperada = 30 * t; PorEmLinha(distEsperada); break;

			case 5:
				nome = "parede"; paredeEsperada = true;
				PorNoLugar(_vvOuvinte, _vvParedeA);
				PorNoLugar(_vvFalante, _vvParedeB);
				distEsperada = (_vvParedeB - _vvParedeA).Length;
				break;

			case 6:
				nome = "aberto";
				PorNoLugar(_vvOuvinte, _vvAbertoA);
				PorNoLugar(_vvFalante, _vvAbertoB);
				distEsperada = (_vvAbertoB - _vvAbertoA).Length;
				break;

			case 7:
				nome = "tres"; distEsperada = 2 * t;
				PorEmLinha(distEsperada);
				// OS EXTRAS ENTRAM NO ALCANCE. Eles nunca pararam de falar: quem os calava ate agora
				// era o corte de 22 tiles, que e exatamente o mecanismo sob teste.
				for (int i = 0; i < _vvExtras.Count; i++)
					PorNoLugar(_vvExtras[i], _vvOuve + new Vec2((3 + i) * t, 0));
				break;

			default:
				// O FIM E ANUNCIADO E DEPOIS ESPERADO: o cliente B precisa de um instante pra imprimir
				// o placar dele antes de o servidor derrubar a conexao de todo mundo.
				AnunciarFaseDaVoz("fim", 0, false);
				GD.Print("[vozviva] ============ o servidor terminou; o placar do OUVIDO sai no cliente b ============");
				EspiaoDaVoz = null;
				_vvFase = -99;
				GetTree().CreateTimer(4.0).Timeout += () => GetTree().Quit(0);
				return;
		}

		// O CACHE DE PAREDE E DE 100 ms E A FASE ACABOU DE MUDAR DE LUGAR: sem limpar, os primeiros
		// quadros da fase nova sairiam com a resposta de parede da posicao ANTIGA. Em jogo isso e o
		// que se quer (ninguem se teleporta); numa bancada que teleporta, e um erro de medida.
		_paredeDaVoz.Clear();

		AnunciarFaseDaVoz(nome, distEsperada, paredeEsperada);
		_vvViraEm = agora + (long)((_vvFase == 0 ? 2.0 : SegundosPorFaseDaVoz) * 1000);
	}

	/// <summary>Ouvinte na ponta do corredor, falante a <paramref name="px"/> dele, na mesma linha.</summary>
	private void PorEmLinha(float px)
	{
		PorNoLugar(_vvOuvinte!, _vvOuve);
		PorNoLugar(_vvFalante!, _vvOuve + new Vec2(px, 0));
	}

	/// <summary>
	/// TELEPORTE CURTO, DENTRO DA MESMA ZONA -- o mesmo par de linhas do Zanzoken.
	///
	/// Nao e `MoveToZone`: aquele reemite cenario, portas e construcoes, e usa-lo oito vezes seguidas
	/// faria cada cliente RECARREGAR o mapa entre as fases -- engasgo de carga bem no meio da medida
	/// de amplitude. Aqui o que precisa acontecer e so o que o Zanzoken ja faz: mover, carimbar a
	/// sequencia (senao os pacotes em voo do cliente puxam o corpo de volta) e mandar a correcao.
	/// </summary>
	private void PorNoLugar(ServerPlayer pl, Vec2 onde)
	{
		pl.Pos = onde;
		pl.LastInputMs = NowMs();
		pl.CorrecaoEsperadaAte = NowMs() + 500;
		pl.SeqDoTeleporte = pl.SeqInput;
		pl.OrcamentoPx = 0;

		var corr = Protocol.Begin(Protocol.S2C.Correction);
		corr.Put(pl.SeqInput);
		corr.PutVec(pl.Pos);
		pl.Peer?.Send(corr, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// A FASE VAI PELO CANAL DE TEXTO QUE JA EXISTE, e nao por um pacote novo.
	///
	/// Um `S2C.FaseDaBancada` seria codigo de producao que so a bancada usa -- exatamente o que este
	/// projeto chama de API orfa. O `Avisar` e confiavel e ordenado, chega antes do proximo quadro de
	/// voz por construcao, e ja atravessa o mesmo funil que o jogador ve.
	/// </summary>
	private void AnunciarFaseDaVoz(string nome, float dist, bool parede)
	{
		string linha = $"[vozviva] fase={_vvFase} nome={nome} d={dist:0} parede={(parede ? 1 : 0)} "
					 + $"falante={_vvFalante!.Id} ouvinte={_vvOuvinte!.Id}";
		foreach (ServerPlayer p in _players.Values.Where(p => p.Peer != null)) Avisar(p, linha);
		GD.Print($"[vozviva] --> {linha}");
	}

	private void ContarEntrega(int falante, int ouvinte, int bytes)
	{
		_vvEntregue.TryGetValue(ouvinte, out (int Quadros, long Bytes) v);
		_vvEntregue[ouvinte] = (v.Quadros + 1, v.Bytes + bytes);
	}

	/// <summary>
	/// O QUE A AUTORIDADE ENTREGOU NESTA FASE -- a metade da conta que so o servidor tem.
	///
	/// Ela existe pra ser comparada com a contagem do cliente B: se as duas baterem, o que o cliente
	/// mediu e o que o servidor mandou; se nao baterem, a diferenca e perda de rede -- e ai a bancada
	/// sabe que esta olhando o cano, e nao a regra.
	/// </summary>
	private void RelatarFaseDaVoz()
	{
		int idB = _vvOuvinte!.Id;
		_vvEntregue.TryGetValue(idB, out (int Quadros, long Bytes) b);

		double seg = _vvFase == 0 ? 2.0 : SegundosPorFaseDaVoz;
		// OS 10 BYTES DE CABECALHO DO `S2C.Voz`: opcode(1) + falante(4) + seq(2) + dist(1) + parede(1)
		// + tam(1). Somados porque a pergunta do dono e em bytes por segundo NO FIO, e nao em audio.
		double bs = (b.Bytes + b.Quadros * 10) / seg;

		GD.Print($"[vozviva]     fase {_vvFase}: o servidor entregou ao ouvinte {b.Quadros} quadros, "
			   + $"{b.Bytes} B de audio ({bs / 1024:0.00} KB/s com cabecalho)"
			   + (_vvEntregue.Count > 1
					? $" | e {_vvEntregue.Count - 1} outro(s) corpo(s) tambem ouviram" : ""));
	}
}
