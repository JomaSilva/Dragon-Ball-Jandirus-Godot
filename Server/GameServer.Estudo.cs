using Jandirus.Core.Tech;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ OS MINIJOGOS DE ESTUDO NO SERVIDOR (dono, 2026-10-27, o pedido 120) ============================
/// A cada minuto de estudo NA BANCADA (a Research Station aparafusada ao alcance; o estudo meditando sem mesa nao abre nada) o servidor
/// abre um minijogo -- uma conta, o simbolo de um elemento ou uma molecula pra montar -- e da 30 s pra responder. Acerto: +25% no ganho de
/// tech/s por 10 minutos, ate +150% (seis valendo; o setimo renova o mais antigo). Erro: sai o bonus mais antigo que estiver valendo.
/// Tempo esgotado: o minijogo fecha e nada muda. O bonus vale pra TODO estudo (na mesa e meditando), no `TickDoEstudo`.
///
/// QUASE NUNCA REPETE: elementos e moleculas saem de um baralho por corpo (todos antes de repetir); a conta e sorteada na hora, e as
/// ultimas cinquenta do corpo nao voltam. O tipo varia, e nunca o mesmo duas vezes seguidas.
///
/// No fio: `S2C.Estudo` leva o tipo, o indice, a expressao, a forma da frase, os segundos e os bonus valendo; o tipo zero fecha a janela
/// com o resultado. A resposta volta pelo canal de tecnologia (`estudo_resposta`, ver `ComandoDeTech`). Tudo por id, e o id se reusa: o
/// estado some no logout (`EsquecerOEstudo`, inscrito no `_aoEsquecer`). Os bonus nao vao pro save -- duram dez minutos.
/// ==================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O minijogo aberto de cada corpo.</summary>
	private readonly Dictionary<int, Minijogo> _minijogoDoEstudo = [];

	/// <summary>Os segundos de estudo NA BANCADA desde o ultimo minijogo.</summary>
	private readonly Dictionary<int, int> _estudoDesdeODesafio = [];

	/// <summary>Os bonus de estudo de cada corpo: os prazos (ms), do mais antigo pro mais novo.</summary>
	private readonly Dictionary<int, List<long>> _bonusDoEstudo = [];

	/// <summary>Os baralhos de cada corpo: os elementos (numero atomico) e as moleculas (posicao) que ainda nao sairam.</summary>
	private readonly Dictionary<int, List<int>> _baralhoDeElementos = [], _baralhoDeMoleculas = [];

	/// <summary>As ultimas contas de cada corpo, que nao voltam.</summary>
	private readonly Dictionary<int, Queue<string>> _contasRecentes = [];

	/// <summary>O tipo do ultimo minijogo de cada corpo (o proximo e outro).</summary>
	private readonly Dictionary<int, TipoDeMinijogo> _ultimoMinijogo = [];

	/// <summary>Quantas contas recentes o corpo nao ve de novo.</summary>
	private const int ContasQueNaoVoltam = 50;

	/// <summary>O resultado no pacote de fechar.</summary>
	internal const byte MinijogoCerto = 1, MinijogoErrado = 2, MinijogoTempo = 3;

	/// <summary>Este id saiu do mundo: o minijogo, o relogio, os bonus e os baralhos somem com ele (id se reusa -- ver `_aoEsquecer`).</summary>
	private void EsquecerOEstudo(int id)
	{
		_minijogoDoEstudo.Remove(id);
		_estudoDesdeODesafio.Remove(id);
		_bonusDoEstudo.Remove(id);
		_baralhoDeElementos.Remove(id);
		_baralhoDeMoleculas.Remove(id);
		_contasRecentes.Remove(id);
		_ultimoMinijogo.Remove(id);
	}

	/// <summary>Quantos bonus de estudo valem agora pra este corpo (os vencidos saem aqui).</summary>
	internal int BonusDoEstudoAtivos(int id)
	{
		if (!_bonusDoEstudo.TryGetValue(id, out List<long>? prazos)) return 0;
		long agora = NowMs();
		prazos.RemoveAll(p => p <= agora);
		if (prazos.Count == 0) _bonusDoEstudo.Remove(id);
		return prazos.Count;
	}

	/// <summary>
	/// O SEGUNDO DE ESTUDO, chamado pelo `TickDoEstudo` a 1 Hz pra quem esta estudando: o minijogo aberto que venceu fecha; sem minijogo
	/// aberto, o segundo NA BANCADA conta, e no minuto abre o proximo.
	/// </summary>
	private void MinijogoNoSegundo(ServerPlayer pl, bool naBancada)
	{
		if (_minijogoDoEstudo.TryGetValue(pl.Id, out Minijogo? aberto))
		{
			if (NowMs() >= aberto.Prazo) FecharMinijogo(pl, MinijogoTempo);
			return;   // com um aberto, o relogio do proximo nao anda
		}
		if (!naBancada) return;
		int s = _estudoDesdeODesafio.GetValueOrDefault(pl.Id) + 1;
		if (s < Minijogos.SegundosEntreDesafios) { _estudoDesdeODesafio[pl.Id] = s; return; }
		_estudoDesdeODesafio[pl.Id] = 0;
		AbrirMinijogo(pl);
	}

	/// <summary>
	/// ABRE UM MINIJOGO pra este corpo -- do tipo pedido (bancada e robo), ou sorteado entre os outros dois que nao o ultimo. Devolve o
	/// desafio aberto.
	/// </summary>
	internal Minijogo AbrirMinijogo(ServerPlayer pl, TipoDeMinijogo tipo = TipoDeMinijogo.Nenhum)
	{
		if (tipo == TipoDeMinijogo.Nenhum)
		{
			TipoDeMinijogo ultimo = _ultimoMinijogo.GetValueOrDefault(pl.Id);
			TipoDeMinijogo[] outros = [.. new[] { TipoDeMinijogo.Conta, TipoDeMinijogo.Elemento, TipoDeMinijogo.Molecula }.Where(t => t != ultimo)];
			tipo = outros[_rng.Next(outros.Length)];
		}
		_ultimoMinijogo[pl.Id] = tipo;

		var m = new Minijogo { Tipo = tipo, Forma = (byte)_rng.Next(Minijogos.FormasPorTipo), Prazo = NowMs() + Minijogos.SegundosPraResponder * 1000L };
		switch (tipo)
		{
			case TipoDeMinijogo.Conta:
			{
				if (!_contasRecentes.TryGetValue(pl.Id, out Queue<string>? recentes)) _contasRecentes[pl.Id] = recentes = new Queue<string>();
				(string expr, long resposta) = Minijogos.Conta(_rng);
				for (int tentativa = 0; tentativa < 20 && recentes.Contains(expr); tentativa++) (expr, resposta) = Minijogos.Conta(_rng);
				recentes.Enqueue(expr);
				while (recentes.Count > ContasQueNaoVoltam) recentes.Dequeue();
				m.Expressao = expr;
				m.Resposta = resposta.ToString();
				break;
			}
			case TipoDeMinijogo.Elemento:
				m.Indice = Tirar(_baralhoDeElementos, pl.Id, Enumerable.Range(1, Minijogos.Elementos.Length));
				m.Resposta = Minijogos.Elemento(m.Indice).Simbolo;
				break;
			default:
				m.Indice = Tirar(_baralhoDeMoleculas, pl.Id, Enumerable.Range(0, Minijogos.Moleculas.Length));
				break;
		}
		_minijogoDoEstudo[pl.Id] = m;
		MandarMinijogo(pl, m, 0);
		return m;
	}

	/// <summary>A PROXIMA CARTA do baralho deste corpo (embaralhado de novo quando acaba): todos saem antes de qualquer um repetir.</summary>
	private int Tirar(Dictionary<int, List<int>> baralhos, int id, IEnumerable<int> todos)
	{
		if (!baralhos.TryGetValue(id, out List<int>? b) || b.Count == 0)
		{
			b = [.. todos.OrderBy(_ => _rng.Next())];
			baralhos[id] = b;
		}
		int carta = b[^1];
		b.RemoveAt(b.Count - 1);
		return carta;
	}

	/// <summary>
	/// A RESPOSTA CHEGOU (o canal de tecnologia, `estudo_resposta`): certo soma um bonus (no teto, renova o mais antigo); errado tira o mais
	/// antigo. Sem minijogo aberto, ou fora do prazo, nada.
	/// </summary>
	private void ResponderAoEstudo(ServerPlayer pl, string resposta)
	{
		if (!_minijogoDoEstudo.TryGetValue(pl.Id, out Minijogo? m)) return;
		if (NowMs() >= m.Prazo) { FecharMinijogo(pl, MinijogoTempo); return; }

		bool certo = Minijogos.RespostaCerta(m, resposta);
		int antes = BonusDoEstudoAtivos(pl.Id);
		if (!_bonusDoEstudo.TryGetValue(pl.Id, out List<long>? prazos)) _bonusDoEstudo[pl.Id] = prazos = [];
		if (certo)
		{
			if (prazos.Count >= Minijogos.TetoDeBonus) prazos.RemoveAt(0);
			prazos.Add(NowMs() + Minijogos.MsDoBonus);
		}
		else if (prazos.Count > 0) prazos.RemoveAt(0);
		int depois = BonusDoEstudoAtivos(pl.Id);

		int pct = (int)Math.Round(Minijogos.BonusPorAcerto * depois * 100);
		if (certo)
			Avisar(pl, $"certo! seu estudo rende +{pct}% de tecnologia (cada acerto vale por 10 minutos, até +150%).");
		else if (antes > depois)
			Avisar(pl, $"errado: você perdeu o bônus de estudo mais antigo (agora +{pct}%).");
		else
			Avisar(pl, "errado -- e não havia bônus de estudo pra perder.");
		FecharMinijogo(pl, certo ? MinijogoCerto : MinijogoErrado);
	}

	/// <summary>Fecha o minijogo aberto deste corpo, com o resultado. O tempo esgotado avisa tambem.</summary>
	private void FecharMinijogo(ServerPlayer pl, byte resultado)
	{
		if (!_minijogoDoEstudo.Remove(pl.Id)) return;
		if (resultado == MinijogoTempo) Avisar(pl, "o tempo do minijogo acabou -- o bônus de estudo continua como estava.");
		MandarMinijogo(pl, null, resultado);
	}

	/// <summary>
	/// O PACOTE: o minijogo aberto (tipo, indice, expressao, forma, segundos), ou o fechamento (tipo zero e o resultado) -- e os bonus
	/// valendo, sempre. A resposta nunca vai.
	/// </summary>
	private void MandarMinijogo(ServerPlayer pl, Minijogo? m, byte resultado)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Estudo);
		w.Put((byte)(m?.Tipo ?? TipoDeMinijogo.Nenhum));
		w.Put(m?.Indice ?? 0);
		w.Put(m?.Expressao ?? "");
		w.Put(m?.Forma ?? (byte)0);
		w.Put((byte)(m == null ? 0 : Math.Max(0, (m.Prazo - NowMs() + 999) / 1000)));
		w.Put(resultado);
		w.Put((byte)BonusDoEstudoAtivos(pl.Id));
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
