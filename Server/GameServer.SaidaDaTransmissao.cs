using Jandirus.Core.Skills;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-24) A SAIDA PISCANDO E O CLARAO DA CHEGADA DO INSTANT TRANSMISSION. O dono, sobre o IT entre zonas: "Fazer igual no IT" --
/// o mesmo par de atrasos do Kai Kai (`GameServer.KaiKai.cs`, `Core/Skills/KaiKai.cs`). O clarao e o efeito de desviar um golpe,
/// desenhado NO CORPO; quem esta perto so o via se o corpo estivesse na tela dele. Antes, o pacote do clarao saia no mesmo tique em que
/// o corpo sumia da zona (o `PeerLeft` o solta no mesmo quadro) ou chegava (o pacote vinha antes de o corpo entrar no retrato da zona).
///
/// ============================ O CAMINHO, EM TRES TEMPOS ============================
///   1. A CONCENTRACAO VENCE (`ChegarPelaTransmissaoG11`): as perguntas de sempre, o grupo colhido, o clarao em quem sai e nas caronas,
///      e a SAIDA comeca (<see cref="ComecarASaidaDaTransmissao"/>). O corpo continua na zona, piscando.
///   2. <see cref="TransmissaoEmNiveis.AtrasoDaSaidaMs"/> depois, o SALTO (`SaltarPelaTransmissaoG11`): as perguntas de novo, a cobranca,
///      o grupo, a maestria -- tudo o que a chegada sempre fez, menos o clarao.
///   3. <see cref="TransmissaoEmNiveis.AtrasoDoClaraoDaChegadaMs"/> depois do salto, o CLARAO DA CHEGADA, com o corpo ja no retrato da
///      zona de quem esta la.
/// Tudo anda no relogio fino (30 Hz, `TickDasTransmissoesProntas`).
///
/// ============================ E SE ELE LEVAR UM GOLPE NESSES 0,2 s? ============================
/// Nao pega. A viagem ja esta decidida, e o corpo que pisca esta se desfazendo -- e o efeito de DESVIAR um golpe, e o servidor o trata
/// assim: quem sai e cada carona ficam intocaveis pela saida inteira (<see cref="TransmissaoEmNiveis.EsquivaDaSaidaMs"/>, a mesma
/// carencia de quem acabou de renascer: golpe, tiro, raio, agarrao e tecnica pulam o corpo). Sem isso, os 0,2 s seriam uma janela NOVA
/// pra apanhar com a viagem ja paga. A concentracao continua a de antes: mexer (o empurrao de um golpe inclusive) ou cair a cancela.
/// Na saida o corpo NAO fica preso: anda, e mexer nao cancela mais (a concentracao acabou). So cair segura a viagem (`SaltarPelaTransmissaoG11`),
/// sem cobrar nada.
/// </summary>
public partial class GameServer
{
	/// <summary>DEFEITO INJETADO (bancada --teleporteteste, familia S): o corpo some no mesmo tique do clarao da saida -- quem fica so
	/// ouve o som. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoSomeNaHoraDeTeste;

	/// <summary>DEFEITO INJETADO: o clarao da chegada sai no tique do salto, antes de o corpo entrar no retrato de quem ja esta la.
	/// Falso em jogo, sempre.</summary>
	internal static bool TransmissaoClaraoNaHoraDeTeste;

	/// <summary>DEFEITO INJETADO: sem o desvio da saida -- o corpo que pisca continua apanhando nos 0,2 s. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoSemEsquivaNaSaidaDeTeste;

	/// <summary>DEFEITO INJETADO: quem caiu nos 0,2 s viaja caido. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoCaidoViajaDeTeste;

	private enum FaseDaTransmissao : byte { Saindo, Chegando }

	/// <summary>Uma viagem decidida: o pedido da concentracao (o alvo e o Ki que ela marcou), quem vai junto e em que tempo esta.</summary>
	private sealed class TransmissaoEmCurso
	{
		public required ServerPlayer Quem;
		public required TransmissaoG11 Pedido;
		public List<ServerPlayer> Caronas = [];
		public FaseDaTransmissao Fase;
		/// <summary>Quando entrou na fase de agora (ms).</summary>
		public long Desde;
	}

	/// <summary>Uma viagem por corpo, do fim da concentracao ao clarao da chegada. Quem sai do jogo leva a sua junto (o tique confere).</summary>
	private readonly Dictionary<int, TransmissaoEmCurso> _transmissoesEmCurso = [];

	/// <summary>Este corpo esta piscando pra sair, ou acabou de chegar e falta o clarao? O verbo recusa um segundo pedido nesses 0,3 s.</summary>
	private bool EmTransmissao(int id) => _transmissoesEmCurso.ContainsKey(id);

	/// <summary>
	/// A SAIDA COMECA, com o clarao ja mandado: quem sai e as caronas ficam intocaveis pela saida inteira (o desvio), e o salto espera
	/// <see cref="TransmissaoEmNiveis.AtrasoDaSaidaMs"/>.
	/// </summary>
	private void ComecarASaidaDaTransmissao(ServerPlayer pl, TransmissaoG11 t, List<ServerPlayer> caronas)
	{
		if (!TransmissaoSemEsquivaNaSaidaDeTeste)
			foreach (ServerPlayer q in caronas.Prepend(pl))
				if (q.Combate is { } c) c.Carencia = Math.Max(c.Carencia, TransmissaoEmNiveis.EsquivaDaSaidaMs / 1000.0);
		var e = new TransmissaoEmCurso { Quem = pl, Pedido = t, Caronas = caronas, Fase = FaseDaTransmissao.Saindo, Desde = NowMs() };
		_transmissoesEmCurso[pl.Id] = e;
		if (TransmissaoSomeNaHoraDeTeste) SaltarDaSaida(e);
	}

	/// <summary>O salto: se ele nao sai (caiu, preso, o alvo sumiu), a viagem acaba aqui; se sai, o clarao da chegada fica pra depois.</summary>
	private void SaltarDaSaida(TransmissaoEmCurso e)
	{
		List<ServerPlayer>? foram = SaltarPelaTransmissaoG11(e.Quem, e.Pedido, e.Caronas);
		if (foram == null)
		{
			_transmissoesEmCurso.Remove(e.Quem.Id);
			return;
		}
		e.Caronas = foram;
		e.Fase = FaseDaTransmissao.Chegando;
		e.Desde = NowMs();
		if (TransmissaoClaraoNaHoraDeTeste)
		{
			_transmissoesEmCurso.Remove(e.Quem.Id);
			ClaraoDaChegadaDaTransmissao(e.Quem, foram);
		}
	}

	/// <summary>O clarao da chegada, em quem chegou e em cada carona que ainda esta no mundo, onde cada um esta agora.</summary>
	private void ClaraoDaChegadaDaTransmissao(ServerPlayer pl, List<ServerPlayer> caronas)
	{
		PiscarNaZona(pl.Zone, pl.Id, pl.Pos);
		foreach (ServerPlayer c in caronas)
			if (_players.ContainsKey(c.Id)) PiscarNaZona(c.Zone, c.Id, c.Pos);
	}

	/// <summary>
	/// AS VIAGENS EM CURSO, no tique cheio: o corpo que piscou o bastante salta, e quem chegou ha 0,1 s pisca. Quem saiu do jogo (ou o
	/// corpo forjado que uma bancada tirou) leva a viagem junto -- pela referencia do corpo, e nao so pelo id (id se reusa).
	/// </summary>
	private void TickDasSaidasDaTransmissao()
	{
		if (_transmissoesEmCurso.Count == 0) return;
		long agora = NowMs();
		foreach (TransmissaoEmCurso e in _transmissoesEmCurso.Values.ToList())
		{
			if (!_players.TryGetValue(e.Quem.Id, out ServerPlayer? pl) || !ReferenceEquals(pl, e.Quem))
			{
				_transmissoesEmCurso.Remove(e.Quem.Id);
				continue;
			}
			if (e.Fase == FaseDaTransmissao.Saindo && agora - e.Desde >= TransmissaoEmNiveis.AtrasoDaSaidaMs)
				SaltarDaSaida(e);
			else if (e.Fase == FaseDaTransmissao.Chegando && agora - e.Desde >= TransmissaoEmNiveis.AtrasoDoClaraoDaChegadaMs)
			{
				_transmissoesEmCurso.Remove(pl.Id);
				ClaraoDaChegadaDaTransmissao(pl, e.Caronas);
			}
		}
	}

	// =====================================================================
	// AS PORTAS DE BANCADA -- o relogio, e so ele (quem decide continua sendo o tique de producao)
	// =====================================================================
	/// <summary>Em que tempo esta a viagem deste corpo ("Saindo", "Chegando"; nulo = nenhuma).</summary>
	internal string? FaseDaTransmissaoDeTeste(int id) =>
		_transmissoesEmCurso.TryGetValue(id, out TransmissaoEmCurso? e) ? e.Fase.ToString() : null;

	/// <summary>A viagem deste corpo ficou "antiga" em tantos ms, e o tique de producao anda uma vez.</summary>
	internal void EnvelhecerATransmissaoDeTeste(int id, long ms)
	{
		if (_transmissoesEmCurso.TryGetValue(id, out TransmissaoEmCurso? e)) e.Desde -= ms;
		TickDasSaidasDaTransmissao();
	}

	/// <summary>A viagem inteira de uma vez: salta e pisca na chegada. E o que as bancadas que medem a CHEGADA usam (a saida piscando
	/// tem a familia dela, a S da `--teleporteteste`, e o robo `--diagitmaestria`).</summary>
	internal void TerminarATransmissaoDeTeste(int id)
	{
		for (int i = 0; i < 3 && _transmissoesEmCurso.TryGetValue(id, out TransmissaoEmCurso? e); i++)
		{
			e.Desde = NowMs() - Math.Max(TransmissaoEmNiveis.AtrasoDaSaidaMs, TransmissaoEmNiveis.AtrasoDoClaraoDaChegadaMs) - 1;
			TickDasSaidasDaTransmissao();
		}
	}

	/// <summary>Esquece as viagens em curso (a limpeza das bancadas).</summary>
	internal void EsquecerAsTransmissoesDeTeste() => _transmissoesEmCurso.Clear();
}
