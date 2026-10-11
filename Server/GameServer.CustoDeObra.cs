using Jandirus.Core.Economia;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O CUSTO DA OBRA DE BASE -- cada parede, piso e porta passa a custar, em zeni ou em recursos.
///
/// ============================ O PEDIDO ============================
/// O dono (2026-10-10): *"construcao de base: construir paredes etc tem custo em zenni/ recursos o usuario
/// escolhe na hora de construir e essa opcao fica selecionada ate ele quiser trocar (e uma opcao no canto
/// inferior direito)"*.
/// ==================================================================
///
/// ============================ COMO FICOU ============================
///   * O PRECO e por classe e mora no Core (<see cref="PrecoDeBloco"/>): em zeni o da tabela, em recursos o
///     dobro. No DM construir e de graca (`buildcost` nunca e lido): e divergencia pedida.
///   * A MOEDA E UMA ESCOLHA QUE FICA. O jogador a troca pelo verbo `obra_moeda` (o seletor "Pagar com" do
///     modo de construir) e o servidor a guarda por jogador ate ele trocar de novo. Ela NAO viaja em cada
///     bloco: o `C2S.Bloco` continua com o formato de sempre -- um byte a mais num pacote que as duas pontas
///     leem na mao estoura o leitor ou le lixo (o `NetDataReader` nao confere o fim). Quem nunca escolheu
///     paga em zeni, e o cliente repete a escolha gravada toda vez que liga o modo de construir.
///   * A COBRANCA E A ULTIMA PERGUNTA do `ErguerBloco`, depois de todas as de lugar: so se paga pelo que
///     sobe. Passa pelo `Cobrar` do contrato da economia (`GameServer.Recursos.cs`): sem saldo NAQUELA moeda
///     a peca nao sobe, nada e cobrado, e a outra moeda nao completa.
///   * EM LOTE (arrastando) cada peca e um pedido e uma cobranca: a primeira que o bolso nao paga nao sobe,
///     e as seguintes tambem nao.
///   * TROCAR uma peca sua por outra (construir por cima) paga a nova inteira. Pedir de novo a MESMA peca na
///     mesma celula nao troca nem cobra nada: e o que o arrasto manda quando o mouse volta por cima de uma
///     parede cujo anuncio ainda nao chegou ao cliente.
///   * DESMANCHAR NAO DEVOLVE (decisao deste port). O reembolso existe, desligado: ver
///     <see cref="PrecoDeBloco.DevolveAoDesmanchar"/>.
///   * O QUE JA ESTAVA DE PE NAO PAGA NADA: a leitura do `bases.json` nao cobra de ninguem, e o bloco de antes
///     de haver preco volta com `Pago = 0`.
///   * O ADMIN PAGA COMO TODO MUNDO. Nao ha (nem havia) modo de admin que constroi livre -- o que ele tem e
///     desmanchar o bloco de qualquer um (`DesmancharBloco`), e isso continua igual.
/// ====================================================================
///
/// ============================ O SALDO NA TELA ============================
/// O cliente mostra o preco e o saldo no seletor e pinta o fantasma de vermelho quando o bolso nao paga. Pra
/// isso ele recebe o <see cref="Protocol.S2C.SaldoDeObra"/>: os dois saldos e a moeda que ESTE servidor esta
/// cobrando dele. Sai em resposta ao `obra_moeda` e UMA VEZ POR PEDIDO DE ERGUER, tenha a peca subido ou nao
/// -- com essa garantia o cliente desconta as pecas que ainda estao a caminho (arrastar manda uma duzia por
/// segundo) e para de pedir na que nao da pra pagar, sem esperar a recusa chegar.
///
/// NAO E A CARTEIRA DA ECONOMIA (`S2C.Carteira`, que o `Cobrar` manda a cada cobranca e o cliente guarda no
/// mesmo lugar): ela diz quanto ha, e nao diz "esta e a resposta DAQUELE pedido" nem a moeda em uso. Numa
/// peca cobrada saem os dois, a carteira na frente; a conta que o cliente faz com eles e a `ContaDeObra`.
/// =========================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// DEFEITOS INJETADOS (bancada `--blocoteste`, familia 11)
	// =====================================================================
	/// <summary>DEFEITO INJETADO (bancada `--blocoteste`): a peca sobe e nada e cobrado -- a obra de graca de antes do pedido. Falso em jogo, sempre.</summary>
	internal bool ObraDeGracaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--blocoteste`): em recursos a peca sai pelo MESMO numero do zeni, sem o dobro. Falso em jogo, sempre.</summary>
	internal bool RecursoSemDobroDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--blocoteste`): sem saldo a peca sobe do mesmo jeito -- obra fiada. Falso em jogo, sempre.</summary>
	internal bool ObraFiadaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--blocoteste`): faltando saldo na moeda escolhida, a peca e paga com a outra. Falso em jogo, sempre.</summary>
	internal bool ObraCompletaComAOutraDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--blocoteste`): a escolha da moeda vale so pra proxima peca, e volta ao zeni. Falso em jogo, sempre.</summary>
	internal bool MoedaDeObraNaoFicaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--blocoteste`): o saldo so e respondido quando a peca sobe -- o pedido recusado fica sem resposta. Falso em jogo, sempre.</summary>
	internal bool SaldoSoQuandoSobeDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--blocoteste`): pedir de novo a MESMA peca na mesma celula (a janela do arrasto) cobra outra vez. Falso em jogo, sempre.</summary>
	internal bool MesmaPecaCobraDeNovoDeTeste;

	// =====================================================================
	// A ESCOLHA
	// =====================================================================
	/// <summary>
	/// COM O QUE CADA JOGADOR ESTA PAGANDO, pelo id do corpo. Quem nao esta aqui paga em zeni. Vive so na
	/// memoria: a escolha que atravessa as sessoes e a do `config.json` do cliente, que a repete ao ligar o
	/// modo de construir. (Id de corpo nao se reusa numa vida do servidor: a entrada de quem saiu so sobra.)
	/// </summary>
	private readonly Dictionary<int, Moeda> _moedaDeObra = [];

	/// <summary>A moeda em que as pecas deste jogador estao sendo cobradas agora.</summary>
	internal Moeda MoedaDeObra(ServerPlayer pl) => _moedaDeObra.GetValueOrDefault(pl.Id, Moeda.Zeni);

	/// <summary>
	/// O verbo `obra_moeda` (<see cref="PrecoDeBloco.Verbo"/>), com `zeni` ou `recursos` no argumento. Responde
	/// SEMPRE com o <see cref="Protocol.S2C.SaldoDeObra"/>: e ele que conta ao cliente com o que este servidor
	/// esta cobrando, e quanto ha em cada bolso.
	/// </summary>
	private bool ComandoDeMoedaDeObra(ServerPlayer pl, string cmd, string arg)
	{
		if (cmd != PrecoDeBloco.Verbo) return false;

		// TEXTO QUE NAO E MOEDA NAO TROCA NADA: a escolha fica a que estava.
		if (PrecoDeBloco.DaChave(arg) is { } moeda) _moedaDeObra[pl.Id] = moeda;
		MandarSaldoDeObra(pl, deUmaPeca: false);
		return true;
	}

	// =====================================================================
	// A COBRANCA
	// =====================================================================
	private static double SaldoNa(ServerPlayer pl, Moeda moeda) => moeda == Moeda.Recursos ? pl.Ficha.Recursos : pl.Ficha.Zeni;

	/// <summary>
	/// A LINHA QUE COBRA: o `Cobrar` do contrato da economia -- em zeni o preco da tabela, em recursos o dobro
	/// (`Recurso.CustoEmRecursos`). Falso sem saldo NAQUELA moeda, e ai nada saiu de bolso nenhum.
	/// </summary>
	private bool CobrarNa(ServerPlayer pl, double emZeni, Moeda moeda) =>
		RecursoSemDobroDeTeste && moeda == Moeda.Recursos ? CobrarRecursos(pl, emZeni) : Cobrar(pl, emZeni, moeda);

	/// <summary>
	/// COBRA ESTA PECA na moeda que o jogador escolheu. Falso = o bolso nao paga: nada foi cobrado, o jogador
	/// ja foi avisado e a peca NAO sobe. Devolve tambem quanto saiu e de qual bolso -- e o que o bloco anota
	/// (<see cref="BlocoDePe.Pago"/>).
	/// </summary>
	private bool PagarAPeca(ServerPlayer pl, BlocoDef def, out Moeda moeda, out double pago)
	{
		Moeda escolhida = MoedaDeObra(pl);
		moeda = escolhida;
		pago = 0;
		if (ObraDeGracaDeTeste) return true;

		double emZeni = PrecoDeBloco.EmZeni(def.Classe);
		double antes = SaldoNa(pl, moeda);
		bool pagou = CobrarNa(pl, emZeni, moeda);
		if (!pagou && ObraCompletaComAOutraDeTeste)
		{
			moeda = escolhida == Moeda.Zeni ? Moeda.Recursos : Moeda.Zeni;
			antes = SaldoNa(pl, moeda);
			pagou = CobrarNa(pl, emZeni, moeda);
		}
		if (MoedaDeObraNaoFicaDeTeste) _moedaDeObra.Remove(pl.Id);

		// O QUE SAIU E LIDO DO BOLSO, e nao recalculado: e o numero que o `Cobrar` tirou de fato.
		if (pagou) { pago = antes - SaldoNa(pl, moeda); return true; }
		if (ObraFiadaDeTeste) return true;

		Avisar(pl, PrecoDeBloco.FaltaSaldo(def.Classe, escolhida, SaldoNa(pl, escolhida)));
		return false;
	}

	/// <summary>
	/// O QUE VOLTA PRO BOLSO DE QUEM DESMANCHOU A PROPRIA PECA. Hoje, nada: <see cref="PrecoDeBloco.DevolveAoDesmanchar"/>
	/// e zero. Com a constante ligada volta essa fracao do que a peca CUSTOU, na moeda em que foi paga -- o bloco
	/// de antes de haver preco (`Pago = 0`) nao devolve nada, e o admin que desmancha o de outro nao recebe.
	/// Devolve se o saldo mudou.
	/// </summary>
	private bool DevolverAoDesmanchar(ServerPlayer pl, BlocoDePe b)
	{
		double volta = b.Pago * PrecoDeBloco.DevolveAoDesmanchar;
		if (!(volta > 0) || !EDonoDoBloco(b, pl)) return false;

		if ((Moeda)b.PagoEm == Moeda.Recursos) DarRecursos(pl, volta);
		else pl.Ficha.Zeni += volta;
		return true;
	}

	// =====================================================================
	// O FIO
	// =====================================================================
	/// <summary>
	/// Tudo que sai pelo `S2C.SaldoDeObra` enquanto uma bancada escuta: pra quem (o id) e o fio. Nulo fora da
	/// bancada -- o mesmo molde da <see cref="EscutaDeBlocos"/>.
	/// </summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDeSaldoDeObra;

	/// <summary>
	/// OS DOIS SALDOS E A MOEDA EM USO, so pra ele. `deUmaPeca` = esta e a resposta de UM pedido de erguer (o
	/// cliente da baixa numa peca a caminho); falso = a resposta do `obra_moeda`, ou um saldo que mudou por
	/// fora de um pedido. O formato casa byte a byte com o `GameClient.LerSaldoDeObra`.
	/// </summary>
	private void MandarSaldoDeObra(ServerPlayer pl, bool deUmaPeca)
	{
		if (pl.Peer == null && EscutaDeSaldoDeObra == null) return;

		NetDataWriter w = Protocol.Begin(Protocol.S2C.SaldoDeObra);
		w.Put(pl.Ficha.Zeni);
		w.Put(pl.Ficha.Recursos);
		w.Put((byte)MoedaDeObra(pl));
		w.Put(deUmaPeca);
		EscutaDeSaldoDeObra?.Add((pl.Id, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagconstruir`: o robo do cliente, no modo host)
	// =====================================================================
	/// <summary>Poe os dois saldos deste jogador nos numeros pedidos, e conta ao cliente dele.</summary>
	internal void SaldoDeObraDeTeste(int id, double zeni, double recursos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Zeni = zeni;
		pl.Ficha.Recursos = recursos;
		MandarSaldoDeObra(pl, deUmaPeca: false);
	}

	/// <summary>Os dois saldos e a moeda em uso deste jogador, na conta do SERVIDOR.</summary>
	internal (double Zeni, double Recursos, Moeda Moeda)? ObraPagaDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Ficha.Zeni, pl.Ficha.Recursos, MoedaDeObra(pl)) : null;

	/// <summary>O servidor esquece a escolha deste jogador -- o que uma sessao nova faz: volta ao zeni de quem nunca escolheu.</summary>
	internal void EsquecerMoedaDeObraDeTeste(int id) => _moedaDeObra.Remove(id);
}
