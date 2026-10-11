using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Social;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// ============================ O RADIO DO SCOUTER, NO SERVIDOR: QUEM RECEBE ============================
/// *"faca com q o scouter tenha uma opcao de falar no voice chat via radio, ao falar no radio vc escolhe uma
/// frequencia e os jogadores q estao no mesmo planeta q vc ou em uma distancia de 500 tiles de vc no espaco
/// com scouter na mesma frequencia podem escutar sua voz no ouvido esquerdo deles com um efeito de radio"*.
///
///   | pedaco                          | onde mora                                   |
///   |---------------------------------|---------------------------------------------|
///   | a tecla do radio, a captura     | `Client/Microfone.cs`                       |
///   | as regras (puras)               | `Core/Social/RadioDoScouter.cs`             |
///   | **QUEM RECEBE**                 | **este arquivo**                            |
///   | o ouvido esquerdo e o efeito    | `Client/RadioOuvido.cs`, `EfeitoDeRadio.cs` |
///
/// ============================ ELE E IRMAO DA VOZ DE PERTO, E NAO UM RAMO DELA ============================
/// O quadro de radio chega por um opcode proprio (`C2S.VozDeRadio`) e sai por outro (`S2C.VozDeRadio`), com
/// torneira e sequencia PROPRIAS por falante. Tres razoes, e as tres sao do que o jogador ouve:
///
///   * a rota e POR QUADRO. Um "estou no radio" mandado a parte (pelo canal confiavel) chegaria fora de
///     compasso com os quadros (que nao sao confiaveis): o comeco de uma transmissao sairia pela voz de
///     perto, pra quem estivesse ao lado, e o fim dela pelo radio;
///   * com as DUAS teclas seguradas saem dois fluxos, cada um com 50 quadros por segundo. Numa torneira
///     so, metade seria recusada e os dois sairiam picotados;
///   * cada fluxo conta a propria sequencia, entao quem so ouve o radio nao ve buraco nenhum enquanto o
///     outro fala de perto com a tecla do radio solta (a fila de jitter remendaria cada buraco com 20 ms
///     de fala inventada).
///
/// O QUE E DA VOZ E CONTINUA VALENDO AQUI, pelo MESMO funil: o teto de tamanho e de ritmo
/// (<see cref="VozLocal.Torneira"/>), o mute de admin (`EstaCalado`, a mesma marca do texto), a sequencia
/// do falante saneada (<see cref="VozLocal.NumeradorDoRelay"/>) e a entrega nao confiavel. Nocauteado e
/// morto transmitem, como falam de perto: o `ServerPlayer` continua no corpo, e a voz nunca calou quem
/// esta no chao. A rede ruim de bancada (`--vozrede`) pega o radio tambem.
/// =========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>A torneira do RADIO de cada falante. Propria, e nao a da voz de perto -- ver o cabecalho.</summary>
	private readonly Dictionary<int, VozLocal.Torneira> _torneirasDoRadio = [];

	/// <summary>
	/// UMA TRANSMISSAO: a sequencia que o relay carimba e desde quando o canal esta aberto.
	///
	/// So nasce DEPOIS dos portoes do falante (torneira, mute, scouter) -- e por isso "a transmissao nao
	/// existe" e a evidencia, na bancada, de que o quadro morreu antes do leque.
	/// </summary>
	private sealed class TransmissaoDeRadio
	{
		public readonly VozLocal.NumeradorDoRelay Numerador = new();

		/// <summary>Quando esta transmissao ABRIU: e a fila do teto de vozes. Ver <see cref="CabeNoRadioDe"/>.</summary>
		public long Abriu;
		public long UltimoQuadro;

		/// <summary>O canal esta aberto AGORA? O mesmo prazo do "esta falando" da voz (<see cref="VozLocal.MsAteCalar"/>).</summary>
		public bool NoAr(long agora) => UltimoQuadro != 0 && agora - UltimoQuadro <= VozLocal.MsAteCalar;
	}

	private readonly Dictionary<int, TransmissaoDeRadio> _transmissoes = [];

	/// <summary>O escritor do relay, reusado: no radio o pacote e IGUAL pra todo ouvinte, entao e montado uma vez por quadro.</summary>
	private readonly LiteNetLib.Utils.NetDataWriter _relayDoRadio = new();

	private readonly List<ServerPlayer> _ouvintesDoRadio = [];

	/// <summary>A frequencia em que o scouter deste corpo esta. Mora na ficha (`Fighter.FrequenciaDoRadio`).</summary>
	internal static int FrequenciaDe(ServerPlayer p) => RadioDoScouter.Sanear(p.Ficha.FrequenciaDoRadio);

	/// <summary>
	/// O QUE O FUNIL DECIDIU ENTREGAR -- so bancada, nula em jogo: (quem falou, pra quem, bytes de audio), uma
	/// vez por ouvinte do leque de cada quadro aceito.
	///
	/// ANTES da pergunta "tem conexao?", ao contrario do <see cref="EspiaoDaVoz"/>: a bancada de servidor
	/// forja corpos, corpo forjado nao tem `Peer`, e depois dela "nao entregou" seria verdade sempre. O que
	/// fica entre este ponto e o fio e um `Peer.Send` guardado por `Peer != null`; esse a `--radiovivo` mede
	/// do outro lado, com um cliente de verdade.
	/// </summary>
	public static Action<int, int, int>? EspiaoDoRadio;

	// =====================================================================
	// O QUADRO CHEGOU
	// =====================================================================
	/// <summary>O despacho do `C2S.VozDeRadio`. Irmao do `case Protocol.C2S.Voz`, e com o mesmo mute vindo de fora.</summary>
	private void VozDeRadioChegou(NetPeer peer, LiteNetLib.Utils.NetDataReader reader)
	{
		if (!FioDoRadio.LerQuadroDeQuemFala(reader, _quadroDeVoz, out ushort seq, out int tam)) return;
		if (!_byPeer.TryGetValue(peer, out ServerPlayer? quem)) return;
		if (_vozRedeLigada) VozPelaRedeRuim(quem, seq, _quadroDeVoz, tam, peloRadio: true);
		else RecebiVozDeRadio(quem, seq, _quadroDeVoz, tam, EstaCalado(quem), RelogioDaVoz());
	}

	/// <summary>
	/// UM QUADRO DE RADIO CHEGOU. Os portoes de quem FALA, do mais barato pro mais caro: tamanho e ritmo (a
	/// torneira), o mute, o scouter. So entao o leque (<see cref="QuemOuveORadio"/>), que e onde moram a
	/// frequencia, o mundo e a distancia.
	///
	/// O TEMPO ENTRA COMO PARAMETRO, como na <see cref="VozLocal.Torneira"/>: a ordem em que as transmissoes
	/// abriram decide quem cabe no teto de vozes, e a bancada precisa dize-la sem esperar relogio nenhum.
	/// </summary>
	private void RecebiVozDeRadio(ServerPlayer a, ushort seqDoFalante, byte[] dados, int tam, bool calado, long agora)
	{
		if (!_torneirasDoRadio.TryGetValue(a.Id, out VozLocal.Torneira? torneira))
			_torneirasDoRadio[a.Id] = torneira = new VozLocal.Torneira();

		// A TORNEIRA CORRE ATE PRA QUEM NAO PODE TRANSMITIR (calado, sem scouter): ela e o teto de trafego, e
		// nao a regra do radio. E as recusas sao MUDAS pelo motivo da voz -- um aviso por quadro recusado sao
		// 50 linhas de chat por segundo. Quem aperta a tecla sem scouter e avisado uma vez, pelo cliente.
		if (!torneira.Cabe(agora, tam)) return;
		if (calado) return;
		if (!RadioDoScouter.TemAparelho(TemScouter(a))) return;

		if (!_transmissoes.TryGetValue(a.Id, out TransmissaoDeRadio? t))
			_transmissoes[a.Id] = t = new TransmissaoDeRadio();
		if (!t.NoAr(agora)) t.Abriu = agora;
		t.UltimoQuadro = agora;
		ushort seq = t.Numerador.Carimbar(seqDoFalante);

		_ouvintesDoRadio.Clear();
		QuemOuveORadio(a, agora, _ouvintesDoRadio);
		if (_ouvintesDoRadio.Count == 0) return;

		var w = _relayDoRadio;
		w.Reset();
		w.Put((byte)Protocol.S2C.VozDeRadio);
		FioDoRadio.EscreverQuadroPraQuemOuve(w, a.Id, seq, dados, tam);

		foreach (ServerPlayer o in _ouvintesDoRadio)
		{
			EspiaoDoRadio?.Invoke(a.Id, o.Id, tam);

			// TER DIREITO DE OUVIR NAO E TER CONEXAO: ver o mesmo bloco no `RecebiVoz`.
			if (o.Peer == null) continue;

			// NAO CONFIAVEL, como toda voz: quadro perdido se joga fora. O `Send` copia o buffer.
			if (_vozRedeLigada) MandarPelaRedeRuim(o.Peer, w);
			else o.Peer.Send(w, Protocol.ChannelVoz, DeliveryMethod.Unreliable);
		}
	}

	// =====================================================================
	// O CORTE
	// =====================================================================
	/// <summary>
	/// **O CORTE DO RADIO.** Quem tem direito de ouvir esta transmissao: scouter ligado, a mesma frequencia,
	/// o mesmo mundo (ou ate 500 tiles, no espaco) e uma vaga entre as vozes de radio de quem ouve.
	///
	/// E UMA FUNCAO PELO MOTIVO DO <see cref="QuemOuveAVoz"/>: ela e o sistema, e a `--radioteste` pergunta a
	/// ELA com corpos forjados. A regra em si e a <see cref="RadioDoScouter.Ouve"/>, pura.
	///
	/// A LISTA DA ZONA E O PRIMEIRO CORTE: quem esta em outro mundo nem e olhado. (Com o defeito de bancada
	/// que faz o radio atravessar mundos, o laco passa a olhar todo mundo -- e o "manda pra todos" que este
	/// arquivo recusa, posto no lugar do corte pra a checagem provar que sabe reprovar.)
	/// </summary>
	public void QuemOuveORadio(ServerPlayer a, long agora, List<ServerPlayer> saida)
	{
		IEnumerable<ServerPlayer> candidatos =
			RadioDoScouter.AtravessaMundosDeTeste ? _players.Values : ZoneList(a.Zone.Hash);

		foreach (ServerPlayer o in candidatos)
		{
			if (o.Id == a.Id) continue;
			if (!ORadioChega(a, o)) continue;
			if (!CabeNoRadioDe(o, a, agora)) continue;
			saida.Add(o);
		}
	}

	/// <summary>Os tres portoes de <see cref="RadioDoScouter.Ouve"/> com os dados destes dois corpos.</summary>
	private static bool ORadioChega(ServerPlayer falante, ServerPlayer ouvinte) =>
		RadioDoScouter.Ouve(TemScouter(falante), FrequenciaDe(falante), falante.Zone, falante.Pos,
							TemScouter(ouvinte), FrequenciaDe(ouvinte), ouvinte.Zone, ouvinte.Pos);

	/// <summary>
	/// <paramref name="falante"/> CABE ENTRE AS VOZES DE RADIO DE <paramref name="ouvinte"/>? O teto de banda
	/// (<see cref="RadioDoScouter.MaxVozesPorOuvinte"/>), e quem fica e quem ABRIU a transmissao primeiro --
	/// empate vai pro menor id, pra duas transmissoes abertas no mesmo instante nao trocarem de vaga a cada
	/// quadro. O laco e curto por construcao: `_transmissoes` so tem quem ja transmitiu, e so conta quem
	/// esta no ar agora.
	/// </summary>
	private bool CabeNoRadioDe(ServerPlayer ouvinte, ServerPlayer falante, long agora)
	{
		// QUEM NAO ESTA NO AR ENTRA NO FIM DA FILA: a hora em que ele abriu ONTEM nao guarda lugar hoje.
		long abriu = _transmissoes.TryGetValue(falante.Id, out TransmissaoDeRadio? minha) && minha.NoAr(agora)
			? minha.Abriu : agora;

		int naFrente = 0;
		foreach ((int id, TransmissaoDeRadio t) in _transmissoes)
		{
			if (id == falante.Id || id == ouvinte.Id || !t.NoAr(agora)) continue;
			if (t.Abriu > abriu || (t.Abriu == abriu && id > falante.Id)) continue;
			if (!_players.TryGetValue(id, out ServerPlayer? outro) || !ORadioChega(outro, ouvinte)) continue;
			if (++naFrente >= RadioDoScouter.MaxVozesPorOuvinte) return false;
		}
		return true;
	}

	/// <summary>Este corpo saiu: somem a torneira e a transmissao dele. Chamado pelo <see cref="EsquecerVoz"/> (id se reusa).</summary>
	private void EsquecerRadio(int id)
	{
		_torneirasDoRadio.Remove(id);
		_transmissoes.Remove(id);
	}

	// =====================================================================
	// A FREQUENCIA
	// =====================================================================
	/// <summary>O verbo do radio (`radio_frequencia &lt;numero&gt;`). Uma linha no despacho do `Verbo`.</summary>
	private bool ComandoDeRadio(ServerPlayer pl, string cmd, string arg)
	{
		if (cmd != RadioDoScouter.VerboDeSintonizar) return false;
		Sintonizar(pl, arg);
		return true;
	}

	/// <summary>
	/// TROCA A FREQUENCIA DO SCOUTER. Exige o APARELHO (ligado, ou na mochila: o botao gira com ele
	/// desligado), e um numero inteiro dentro da faixa -- o que vem do cliente e texto, e nada dele e
	/// aproveitado quando nao serve.
	///
	/// O ESTADO VOLTA SEMPRE, ate na recusa: a tela mostra o que o servidor guarda, e um pedido recusado
	/// tem que desfazer o numero que ela tivesse adiantado.
	/// </summary>
	private void Sintonizar(ServerPlayer pl, string pedido)
	{
		if (!TemScouter(pl) && pl.Mochila.Quantos(CatalogoDeItens.Scouter) <= 0)
			Avisar(pl, "você precisa de um scouter pra sintonizar o rádio.");
		else if (!int.TryParse(pedido, System.Globalization.NumberStyles.None,
							   System.Globalization.CultureInfo.InvariantCulture, out int f)
				 || !RadioDoScouter.FrequenciaValida(f))
			Avisar(pl, $"a frequência do rádio vai de {RadioDoScouter.FrequenciaMinima} a {RadioDoScouter.FrequenciaMaxima}.");
		else if (f != FrequenciaDe(pl))
		{
			pl.Ficha.FrequenciaDoRadio = f;
			Avisar(pl, $"rádio do scouter na frequência {f}. Só ouve quem estiver nela.");
		}
		MandarRadio(pl);
	}

	/// <summary>O estado do radio deste jogador (`S2C.Radio`): no login e a cada pedido de troca.</summary>
	private void MandarRadio(ServerPlayer pl)
	{
		if (pl.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Radio);
		FioDoRadio.EscreverEstado(w, FrequenciaDe(pl));
		pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	// =====================================================================
	// A BANCADA LE DAQUI
	// =====================================================================
	/// <summary>Quantos quadros de radio deste falante a torneira recusou. So a bancada le.</summary>
	public int QuadrosDeRadioRecusadosDeTeste(int id) =>
		_torneirasDoRadio.TryGetValue(id, out VozLocal.Torneira? t) ? t.Recusados : 0;

	/// <summary>Este corpo esta com o canal de radio aberto AGORA (um quadro aceito nos ultimos 250 ms)? So a bancada le.</summary>
	public bool TransmitindoNoRadioDeTeste(int id) =>
		_transmissoes.TryGetValue(id, out TransmissaoDeRadio? t) && t.NoAr(RelogioDaVoz());

	/// <summary>A ultima sequencia que o relay do radio carimbou pra este falante (-1 = nenhum quadro passou dos portoes). So a bancada le.</summary>
	public int UltimaSequenciaDoRadioDeTeste(int id) =>
		_transmissoes.TryGetValue(id, out TransmissaoDeRadio? t) ? t.Numerador.Saida : -1;
}
