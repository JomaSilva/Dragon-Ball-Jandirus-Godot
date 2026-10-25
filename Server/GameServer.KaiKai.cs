using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-23) O KAI KAI COM A TELA DOS PLANETAS CONHECIDOS. O dono: *"faça o kai kai abrir uma tela com todos os planetas
/// conhecidos por jogadores e ao clicar ele teleporta pra eles no mesmo estilo do IT"*. As decisoes dele e os numeros estao em
/// `Core/Skills/KaiKai.cs`; o registro do verbo continua no lote G11 (`GameServer.Tecnicas.G11.cs`), e a tela e o
/// `Client/TelaDoKaiKai.cs`.
///
/// ============================ O CAMINHO ============================
///   1. O verbo SEM destino (o botao, a tecla, o `Kai_Kai` digitado) passa pela porta do salto de planeta -- quem pode, a recarga
///      e o Ki CHEIO, nessa ordem, como no DM (`kai.dm:114-115`: o `input()` so abre com Ki cheio) -- e manda a LISTA pelo
///      `S2C.DestinosDoKaiKai`: os doze de sempre (`DestinosDoKaiKaiG11`) e os mundos gerados do livro de batismo (o primeiro
///      jogador que pisa num mundo gerado o anota, `AnotarDescoberta`), cada um com o nome de batismo; o destruido fica de fora.
///      O cliente abre a tela ao receber.
///   2. Cada linha da tela manda `Kai_Kai:chave` (o nome da zona, ou `#seed` do mundo gerado). O verbo digitado com destino
///      (`Kai_Kai:Namek`, `Kai_Kai:Terra`, o nome de batismo) continua valendo: chave primeiro, nome depois, e nome repetido
///      recusa (mundo gerado repete nome). O servidor confere tudo de novo: a porta, e que o destino ESTA na lista de agora.
///   3. O salto e NA HORA, sem concentracao: paga (Ki -> 0, a recarga, o emote e a carona de 1,5 tile de sempre), pisca na saida
///      (`S2C.TransmissaoPiscar`, o pacote e o desenho do Instant Transmission, mandado a zona), some, chega e pisca na chegada.
///      Os dois atrasos do clarao (`KaiKai.AtrasoDaSaidaMs`, `KaiKai.AtrasoDoClaraoDaChegadaMs`) existem pra quem esta perto
///      VER o efeito -- ele e desenhado no corpo, e o corpo tem de estar na tela de quem olha.
///   4. Mundo gerado que o servidor ainda nao tem (ninguem esta nele agora): o salto ESPERA a geracao, como o pouso
///      (`PousarEmProcedural`), sem cobrar nada antes de ir, e so avisa se ela demorar.
/// ======================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada --kaikaiteste) -- falsos em jogo, sempre
	// =====================================================================
	/// <summary>DEFEITO INJETADO: a tela nao abre -- sem destino, a lista volta pro chat, como antes de 2026-10-23.</summary>
	internal static bool KaiKaiListaNoChatDeTeste;

	/// <summary>DEFEITO INJETADO: os mundos gerados do livro de batismo ficam fora (so os doze de sempre).</summary>
	internal static bool KaiKaiSoOsDozeDeTeste;

	/// <summary>DEFEITO INJETADO: o planeta destruido continua na lista, e o salto vai pra ele.</summary>
	internal static bool KaiKaiListaODestruidoDeTeste;

	/// <summary>DEFEITO INJETADO: o nome digitado que aponta dois mundos escolhe o primeiro, em vez de recusar.</summary>
	internal static bool KaiKaiNomeRepetidoEscolheDeTeste;

	/// <summary>DEFEITO INJETADO: sem o clarao do Instant Transmission nas duas pontas (o efeito "kaikai" de antes ia so ao dono, e o
	/// cliente nem o desenhava).</summary>
	internal static bool KaiKaiSemClaraoDeTeste;

	/// <summary>DEFEITO INJETADO: o corpo some no mesmo tique do clarao da saida -- quem fica so ouve o som.</summary>
	internal static bool KaiKaiSomeNaHoraDeTeste;

	/// <summary>DEFEITO INJETADO: o clarao da chegada sai no tique do salto, antes de o corpo entrar no retrato de quem ja esta la.</summary>
	internal static bool KaiKaiClaraoNaHoraDeTeste;

	/// <summary>DEFEITO INJETADO: o mundo gerado que ainda nao nasceu nao e esperado -- o salto vai na hora pro mundo frio, no ponto
	/// de chegada dos mapas feitos a mao (que num mundo gerado pode ser pedra ou agua).</summary>
	internal static bool KaiKaiNaoEsperaOMundoDeTeste;

	/// <summary>DEFEITO INJETADO: o Ki e cobrado no clique, antes de o mundo gerado nascer.</summary>
	internal static bool KaiKaiCobraNaEsperaDeTeste;

	/// <summary>DEFEITO INJETADO: a espera de um mundo que nao nasce nao avisa nem desiste.</summary>
	internal static bool KaiKaiEsperaMudaDeTeste;

	/// <summary>A BANCADA ESCUTA A LISTA QUE VAI PRA TELA (corpo forjado nao tem Peer). Nula em jogo.</summary>
	internal static List<(int Quem, List<DestinoDoKaiKai> Lista)>? EscutaDoKaiKai;

	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>Uma linha da lista no servidor: o que viaja no fio, a ZONA (o mundo gerado precisa do nome de dentro, que nao
	/// viaja) e se o planeta morreu (o morto sai da tela, e o pedido digitado por ele ouve por que).</summary>
	private readonly record struct DestinoDoKaiKaiNoServidor(DestinoDoKaiKai Fio, ZoneKey Zona, bool Morto);

	/// <summary>Em que ponto um Kai Kai pedido esta: esperando o mundo gerado nascer, piscando antes de sumir, ou ja chegou e falta o
	/// clarao da chegada.</summary>
	private enum FaseDoKaiKai : byte { EsperandoOMundo, Saindo, Chegando }

	private sealed class KaiKaiEmCurso
	{
		public required ServerPlayer Quem;
		public required ZoneKey Zona;
		public required string Chave;
		public required string Nome;
		public FaseDoKaiKai Fase;
		/// <summary>O corpo celeste do mundo gerado que se espera (o `MundoDaZona` encomenda a geracao com ele).</summary>
		public PlanetaNoEspaco Corpo;
		/// <summary>Onde se chega.</summary>
		public Vec2 Ponto;
		public List<ServerPlayer> Caronas = [];
		/// <summary>Quando entrou na fase de agora (ms).</summary>
		public long Desde;
		/// <summary>A espera ja disse que esta demorando.</summary>
		public bool Avisado;
	}

	/// <summary>Um Kai Kai por corpo, do pedido ao clarao da chegada. Quem sai do jogo leva o seu junto (ver <see cref="TickDoKaiKai"/>).</summary>
	private readonly Dictionary<int, KaiKaiEmCurso> _kaiKaiEmCurso = [];

	private const string EmoteDoKaiKai = "parece estar se concentrando... grita 'Kai Kai!' e some!";

	// =====================================================================
	// O VERBO
	// =====================================================================
	private void KaiKaiG11(ServerPlayer pl, string arg)
	{
		if (RecusarPelaMenteFragmentada(pl)) return;
		// A PORTA ANTES DA LISTA (`kai.dm:114-115`). No clique ela e perguntada de novo: entre a tela aberta e o botao da pra ter
		// gastado Ki, levado um agarrao ou caido.
		if (!PortaDoSaltoDePlaneta(pl, kiCheio: true)) return;

		List<DestinoDoKaiKaiNoServidor> todos = TodosOsDestinosDoKaiKai(pl);
		if (arg.Length == 0)
		{
			MandarATelaDoKaiKai(pl, [.. todos.Where(DaTela)]);
			return;
		}

		if (DestinoPedidoAoKaiKai(pl, arg, todos) is not { } d) return;
		if (d.Morto && !KaiKaiListaODestruidoDeTeste)
		{
			Avisar(pl, $"{d.Fio.Nome} não existe mais: o Kai Kai não chega lá.");
			return;
		}
		if (d.Fio.Aqui)
		{
			Avisar(pl, $"você já está em {d.Fio.Nome}.");
			return;
		}

		// O PEDIDO NOVO TROCA O ANTIGO: quem esperava um mundo nascer e escolheu outro destino nao salta duas vezes.
		_kaiKaiEmCurso.Remove(pl.Id);

		// OS DOZE DE SEMPRE pousam no berco do planeta, chao firme (2026-10-24, dono: "Usar o berco do planeta" -- o `SpawnPos` fixo de
		// antes caia na agua de Namek). O mesmo ponto do Devil Bringer e do Rasgo: ver `ChegadaDoSaltoDePlaneta`.
		if (!d.Fio.Gerado)
		{
			ComecarOSaltoDoKaiKai(pl, d.Zona, d.Fio.Chave, d.Fio.Nome, ChegadaDoSaltoDePlaneta(d.Zona));
			return;
		}

		// O MUNDO GERADO: o corpo dele no ceu (a geracao precisa dele -- e e pra la que a decolagem devolve) e a chegada que o
		// proprio gerador garantiu livre (`TerrenoGerado.Spawn`), e nao o ponto dos mapas feitos a mao.
		if (CorpoGerado.DaZona(SeedDoUniverso, d.Zona) is not { } achado)
		{
			Avisar(pl, "esse mundo não fica neste universo.");
			return;
		}
		if (KaiKaiNaoEsperaOMundoDeTeste)
		{
			ComecarOSaltoDoKaiKai(pl, d.Zona, d.Fio.Chave, d.Fio.Nome, SpawnPos);
			return;
		}
		if (MundoDaZona(d.Zona, achado.Corpo) is { } viva)
		{
			ComecarOSaltoDoKaiKai(pl, d.Zona, d.Fio.Chave, d.Fio.Nome, viva.Chegada);
			return;
		}

		// O MUNDO ESTA NASCENDO numa thread propria (o `MundoDaZona` acabou de encomenda-lo): o salto espera, e NADA se paga antes de
		// ir -- o Ki, a recarga e a carona saem no salto de verdade, pela mesma porta (`EsperarOMundoDoKaiKai`).
		if (KaiKaiCobraNaEsperaDeTeste) pl.Ficha.Ki = 0;
		_kaiKaiEmCurso[pl.Id] = new KaiKaiEmCurso
		{
			Quem = pl, Zona = d.Zona, Chave = d.Fio.Chave, Nome = d.Fio.Nome,
			Fase = FaseDoKaiKai.EsperandoOMundo, Corpo = achado.Corpo, Desde = NowMs(),
		};
		GD.Print($"[kaikai] {pl.Name} espera {d.Zona} nascer pra saltar");
	}

	// =====================================================================
	// A LISTA
	// =====================================================================
	/// <summary>
	/// TODOS OS DESTINOS, os mortos inclusive (marcados): os doze de sempre que tem mapa neste servidor, na ordem do DM, e os mundos
	/// gerados do livro de batismo -- o que algum jogador pisou --, por nome. O nome e o que o jogador le: o batismo, se houver; senao
	/// o de sempre (<see cref="KaiKai.NomePadrao"/>) ou o do mundo gerado.
	/// </summary>
	private List<DestinoDoKaiKaiNoServidor> TodosOsDestinosDoKaiKai(ServerPlayer pl)
	{
		var lista = new List<DestinoDoKaiKaiNoServidor>();
		foreach (string d in DestinosDoKaiKaiG11)
		{
			if (_catalogo?.Get(d) == null) continue;   // sem mapa neste servidor nao ha onde chegar
			ZoneKey z = ZoneKey.Premade(d);
			lista.Add(new DestinoDoKaiKaiNoServidor(
				new DestinoDoKaiKai(d, _batismos.NomeVisto(d, KaiKai.NomePadrao(d)), Gerado: false, Aqui: pl.Zone.Equals(z)),
				z, ZonaMorta(z)));
		}
		if (KaiKaiSoOsDozeDeTeste) return lista;

		var gerados = new List<DestinoDoKaiKaiNoServidor>();
		foreach (NomeDePlaneta n in _batismos.Todos)
		{
			// SO O MUNDO GERADO: o pre-feito batizado ja e um dos doze (e o batismo dele ja entrou no nome la em cima).
			if (n.Original.Length == 0 || !ChaveDePlaneta.Ler(n.Chave, n.Original, out ChaveDePlaneta c) || c.PreFeito) continue;
			ZoneKey z = ZoneKey.Procedural(n.Original, c.Seed);
			string nome = n.Nome.Length > 0 ? n.Nome : n.Original;
			gerados.Add(new DestinoDoKaiKaiNoServidor(new DestinoDoKaiKai(n.Chave, nome, Gerado: true, Aqui: pl.Zone.Equals(z)), z, ZonaMorta(z)));
		}
		gerados.Sort((a, b) =>
		{
			int porNome = string.Compare(a.Fio.Nome, b.Fio.Nome, StringComparison.OrdinalIgnoreCase);
			return porNome != 0 ? porNome : string.CompareOrdinal(a.Fio.Chave, b.Fio.Chave);
		});
		lista.AddRange(gerados);
		return lista;
	}

	/// <summary>O que aparece na tela: o planeta destruido nao (decisao do dono).</summary>
	private static bool DaTela(DestinoDoKaiKaiNoServidor d) => !d.Morto || KaiKaiListaODestruidoDeTeste;

	/// <summary>A lista vai pra TELA de quem pediu, e uma linha no chat diz onde ela esta.</summary>
	private void MandarATelaDoKaiKai(ServerPlayer pl, List<DestinoDoKaiKaiNoServidor> daTela)
	{
		if (KaiKaiListaNoChatDeTeste)
		{
			Avisar(pl, $"o Kai Kai alcanca: {string.Join(", ", daTela.Select(d => d.Fio.Chave))}");
			Avisar(pl, "para ir: Kai_Kai:<destino>");
			return;
		}
		List<DestinoDoKaiKai> fio = [.. daTela.Select(d => d.Fio)];
		EscutaDoKaiKai?.Add((pl.Id, fio));
		if (pl.Peer != null)
		{
			NetDataWriter w = Protocol.Begin(Protocol.S2C.DestinosDoKaiKai);
			FioDoKaiKai.Escrever(w, fio);
			pl.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
		}
		// A LINHA DO CHAT e tambem o que o verbo "responde" a quem nao tem tela (a bancada do catalogo aperta o verbo nu num corpo
		// sem cliente e exige uma resposta).
		Avisar(pl, "para onde? Escolha o destino na tela do Kai Kai.");
	}

	/// <summary>
	/// O DESTINO PEDIDO: a CHAVE exata primeiro (e o que o botao manda), depois o NOME -- o que se le na tela ou o nome do mapa,
	/// sem caixa e sem sublinhado. Nome que aponta mais de um mundo recusa: mundo gerado repete nome, e o batismo de um pode ser o
	/// nome de outro. Nulo = ja falou por que.
	/// </summary>
	private DestinoDoKaiKaiNoServidor? DestinoPedidoAoKaiKai(ServerPlayer pl, string arg, List<DestinoDoKaiKaiNoServidor> todos)
	{
		foreach (DestinoDoKaiKaiNoServidor d in todos)
			if (string.Equals(d.Fio.Chave, arg, StringComparison.OrdinalIgnoreCase)) return d;

		List<DestinoDoKaiKaiNoServidor> iguais =
			[.. todos.Where(d => KaiKai.MesmoNome(d.Fio.Nome, arg) || KaiKai.MesmoNome(d.Zona.Name, arg))];
		if (iguais.Count == 0)
		{
			Avisar(pl, $"o Kai Kai não alcança '{arg}'.");
			return null;
		}
		if (iguais.Count > 1 && !KaiKaiNomeRepetidoEscolheDeTeste)
		{
			Avisar(pl, $"há mais de um mundo chamado '{arg}': escolha na tela do Kai Kai.");
			return null;
		}
		return iguais[0];
	}

	// =====================================================================
	// O SALTO
	// =====================================================================
	/// <summary>
	/// O SALTO DECIDIDO: paga (o Ki inteiro, a recarga, o emote e a carona -- `PagarOSaltoDePlaneta`, o mesmo do RiftTeleport e do
	/// Devil Bringer), pisca na saida e espera <see cref="KaiKai.AtrasoDaSaidaMs"/> pra sumir: o clarao e desenhado no corpo, e quem
	/// fica so o ve se o corpo ainda estiver na zona dele.
	/// </summary>
	private void ComecarOSaltoDoKaiKai(ServerPlayer pl, ZoneKey zona, string chave, string nome, Vec2 ponto)
	{
		List<ServerPlayer> caronas = PagarOSaltoDePlaneta(pl, kiCheio: true, comCarona: true, EmoteDoKaiKai);
		ClaraoDoKaiKai(pl, caronas);
		var e = new KaiKaiEmCurso
		{
			Quem = pl, Zona = zona, Chave = chave, Nome = nome,
			Fase = FaseDoKaiKai.Saindo, Ponto = ponto, Caronas = caronas, Desde = NowMs(),
		};
		_kaiKaiEmCurso[pl.Id] = e;
		if (KaiKaiSomeNaHoraDeTeste) SumirPeloKaiKai(e);
	}

	/// <summary>
	/// O CORPO SOME E CHEGA, com as caronas que o pagamento colheu. Quem ficou preso nos 0,2 s (a Sala do Tempo, um selo, o bolso de
	/// uma fusao) nao sai -- e a carona que ficou presa fica. O clarao da chegada espera o corpo entrar no retrato da zona de quem ja
	/// esta la (<see cref="KaiKai.AtrasoDoClaraoDaChegadaMs"/>).
	/// </summary>
	private void SumirPeloKaiKai(KaiKaiEmCurso e)
	{
		ServerPlayer pl = e.Quem;
		if (PorQueNaoSaiDaqui(pl) is { Length: > 0 } preso)
		{
			_kaiKaiEmCurso.Remove(pl.Id);
			Avisar(pl, preso);
			return;
		}
		List<ServerPlayer> caronas = [.. e.Caronas.Where(c => _players.ContainsKey(c.Id) && !c.Ficha.dead && PorQueNaoSaiDaqui(c).Length == 0)];
		Saltar(pl, e.Zona, e.Ponto, caronas, "te leva junto no teleporte.");
		Avisar(pl, caronas.Count == 0
			? $"voce reaparece em {e.Nome}, sem uma gota de energia."
			: $"voce reaparece em {e.Nome} com {caronas.Count} pessoa{(caronas.Count == 1 ? "" : "s")} a tiracolo, sem uma gota de energia.");
		GD.Print($"[kaikai] {pl.Name} saltou ate {e.Nome} ({e.Zona}) com {caronas.Count} de carona");

		e.Caronas = caronas;
		e.Fase = FaseDoKaiKai.Chegando;
		e.Desde = NowMs();
		if (KaiKaiClaraoNaHoraDeTeste)
		{
			_kaiKaiEmCurso.Remove(pl.Id);
			ClaraoDoKaiKai(pl, caronas);
		}
	}

	/// <summary>
	/// O CLARAO DO INSTANT TRANSMISSION em quem salta e em cada carona, onde cada um esta agora: o mesmo pacote e o mesmo desenho
	/// (`PiscarNaZona` -> `S2C.TransmissaoPiscar` -> `World.PiscarDaTransmissao`: o efeito de desviar um golpe e o
	/// `instanttransmission.wav` posicionado), mandado a zona inteira -- quem esta longe ve e nao ouve.
	/// </summary>
	private void ClaraoDoKaiKai(ServerPlayer pl, List<ServerPlayer> caronas)
	{
		if (KaiKaiSemClaraoDeTeste) return;
		PiscarNaZona(pl.Zone, pl.Id, pl.Pos);
		foreach (ServerPlayer c in caronas)
			if (_players.ContainsKey(c.Id)) PiscarNaZona(c.Zone, c.Id, c.Pos);
	}

	// =====================================================================
	// O TIQUE -- chamado logo depois da colheita dos mundos gerados (`GameServer.cs`)
	// =====================================================================
	/// <summary>
	/// OS KAI KAI EM CURSO: o mundo que nasceu leva quem o esperava, o corpo que piscou some, e quem chegou pisca. Quem saiu do jogo
	/// (ou o corpo forjado que uma bancada tirou) leva o pedido junto -- pela referencia do corpo, e nao so pelo id.
	/// </summary>
	private void TickDoKaiKai()
	{
		if (_kaiKaiEmCurso.Count == 0) return;
		long agora = NowMs();
		foreach (KaiKaiEmCurso e in _kaiKaiEmCurso.Values.ToList())
		{
			if (!_players.TryGetValue(e.Quem.Id, out ServerPlayer? pl) || !ReferenceEquals(pl, e.Quem))
			{
				_kaiKaiEmCurso.Remove(e.Quem.Id);
				continue;
			}
			switch (e.Fase)
			{
				case FaseDoKaiKai.EsperandoOMundo:
					EsperarOMundoDoKaiKai(e, agora);
					break;
				case FaseDoKaiKai.Saindo:
					if (agora - e.Desde >= KaiKai.AtrasoDaSaidaMs) SumirPeloKaiKai(e);
					break;
				case FaseDoKaiKai.Chegando:
					if (agora - e.Desde >= KaiKai.AtrasoDoClaraoDaChegadaMs)
					{
						_kaiKaiEmCurso.Remove(pl.Id);
						ClaraoDoKaiKai(pl, e.Caronas);
					}
					break;
			}
		}
	}

	/// <summary>
	/// A ESPERA DE UM MUNDO GERADO. Nasceu: a porta e a lista de novo (o tempo passou -- o Ki gasto, um agarrao, o planeta que morreu)
	/// e o salto, com o preco cobrado SO agora. Nao nasceu: depois de <see cref="KaiKai.EsperaSemAvisoMs"/> avisa uma vez, e depois de
	/// <see cref="KaiKai.TetoDaEsperaMs"/> desiste (uma geracao que caiu na thread nao prende o pedido pra sempre).
	/// </summary>
	private void EsperarOMundoDoKaiKai(KaiKaiEmCurso e, long agora)
	{
		ServerPlayer pl = e.Quem;
		if (MundoDaZona(e.Zona, e.Corpo) is { } viva)
		{
			_kaiKaiEmCurso.Remove(pl.Id);
			if (!PortaDoSaltoDePlaneta(pl, kiCheio: true)) return;
			if (!TodosOsDestinosDoKaiKai(pl).Exists(d => d.Fio.Chave == e.Chave && DaTela(d)))
			{
				Avisar(pl, $"{e.Nome} não existe mais: o Kai Kai não chega lá.");
				return;
			}
			ComecarOSaltoDoKaiKai(pl, e.Zona, e.Chave, e.Nome, viva.Chegada);
			return;
		}
		if (KaiKaiEsperaMudaDeTeste) return;

		long esperou = agora - e.Desde;
		if (esperou >= KaiKai.TetoDaEsperaMs)
		{
			_kaiKaiEmCurso.Remove(pl.Id);
			Avisar(pl, $"{e.Nome} não terminou de se formar: o Kai Kai foi desfeito, e o seu Ki ficou com você.");
			return;
		}
		if (!e.Avisado && esperou >= KaiKai.EsperaSemAvisoMs)
		{
			e.Avisado = true;
			Avisar(pl, $"{e.Nome} ainda está se formando: o Kai Kai sai assim que ele existir.");
		}
	}

	// =====================================================================
	// AS PORTAS DE BANCADA -- o relogio, e so ele (quem decide continua sendo o tique de producao)
	// =====================================================================
	/// <summary>Em que fase esta o Kai Kai deste corpo (nulo = nenhum).</summary>
	internal string? FaseDoKaiKaiDeTeste(int id) => _kaiKaiEmCurso.TryGetValue(id, out KaiKaiEmCurso? e) ? e.Fase.ToString() : null;

	/// <summary>Pra onde vai o Kai Kai deste corpo: a chave do destino (nulo = nenhum).</summary>
	internal string? ChaveDoKaiKaiDeTeste(int id) => _kaiKaiEmCurso.TryGetValue(id, out KaiKaiEmCurso? e) ? e.Chave : null;

	/// <summary>
	/// O relogio do salto em curso deste corpo vence agora, e o tique de producao anda uma vez: na saida o corpo some, na chegada ele
	/// pisca. Falso se nao havia salto andando -- a espera de um mundo gerado nao se vence aqui, ela depende da geracao.
	/// </summary>
	internal bool VencerOKaiKaiDeTeste(int id)
	{
		if (!_kaiKaiEmCurso.TryGetValue(id, out KaiKaiEmCurso? e) || e.Fase == FaseDoKaiKai.EsperandoOMundo) return false;
		e.Desde = NowMs() - Math.Max(KaiKai.AtrasoDaSaidaMs, KaiKai.AtrasoDoClaraoDaChegadaMs) - 1;
		TickDoKaiKai();
		return true;
	}

	/// <summary>O salto inteiro de uma vez: some, chega e pisca.</summary>
	internal void TerminarOKaiKaiDeTeste(int id)
	{
		for (int i = 0; i < 4 && VencerOKaiKaiDeTeste(id); i++) { }
	}

	/// <summary>A espera de um mundo gerado ficou "antiga" em tantos ms (o aviso e a desistencia sao medidos assim).</summary>
	internal void EnvelhecerAEsperaDoKaiKaiDeTeste(int id, long ms)
	{
		if (_kaiKaiEmCurso.TryGetValue(id, out KaiKaiEmCurso? e)) e.Desde -= ms;
	}

	/// <summary>Esquece o Kai Kai deste corpo, em qualquer fase.</summary>
	internal void EsquecerOKaiKaiDeTeste(int id) => _kaiKaiEmCurso.Remove(id);
}
