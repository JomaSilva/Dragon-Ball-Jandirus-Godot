using Godot;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// OS SERVICOS DO OUTRO MUNDO (2026-10-19) -- a rede de quests que paga zeni aos mortos. A regra mora em `Core/World/ServicosDoAlem.cs`;
/// aqui fica o que so o servidor sabe:
///   * os TRES DOADORES NOVOS, semeados com o Sr. Kaioh e o Korin (`SemearOsNpcsDeConversa`): obra fixa do mapa, densa, com menu E;
///   * o MENU E de todo doador (pedir servico, entregar) e a desistencia, pela aba World;
///   * o NOCAUTE que conta (pelo funil unico da derrota, `AoPerderALuta`) e o SEGUNDO de meditacao ou treino no lugar certo (1 Hz);
///   * o PRAZO que vence, o servico que cai com o revive, o ZENI no bolso e o cartao da aba World (`S2C.ServicoDoAlem`).
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// SEMEIA os tres doadores novos -- chamado do fim do <see cref="SemearOsNpcsDeConversa"/>, no boot, pelo mesmo molde do Sr. Kaioh:
	/// a arte e um `SpriteFrames` de um quadro so (`Assets/Sprites/NPCs`), recortado das folhas que o projeto ja tinha (o Oni azul do DU, a
	/// Deusa das Serpentes e o Demon1).
	/// </summary>
	private void SemearOsDoadoresDoAlem()
	{
		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo);
		SemearUmNpcDeConversa(ServicosDoAlem.TipoDaRecepcao, "Oni da recepção",
			"Confere as fichas das almas que chegam -- e sempre tem um serviço pra quem quer juntar o preço da volta.",
			"res://Assets/Sprites/NPCs/OniDaRecepcao.tres", alem, _ => (ServicosDoAlem.RecepcaoX, ServicosDoAlem.RecepcaoY));
		SemearUmNpcDeConversa(ServicosDoAlem.TipoDaPrincesa, "Princesa Serpente",
			"A dona do palácio no meio do Caminho da Serpente. Adora visita -- e detesta que ela vá embora.",
			"res://Assets/Sprites/NPCs/PrincesaSerpente.tres", alem, _ => (ServicosDoAlem.PrincesaX, ServicosDoAlem.PrincesaY));
		SemearUmNpcDeConversa(ServicosDoAlem.TipoDoOniDoInferno, "Oni do Inferno",
			"Toma conta da fila dos condenados. Ela vive fugindo.",
			"res://Assets/Sprites/NPCs/OniDoInferno.tres", ZoneKey.Premade(Alem.ZonaDoInferno),
			_ => (ServicosDoAlem.OniDoInfernoX, ServicosDoAlem.OniDoInfernoY));
	}

	// =====================================================================
	// O MENU E E A DESISTENCIA
	// =====================================================================

	/// <summary>Os verbos dos servicos. Devolve `true` quando o comando era daqui -- inclusive recusado (ver `ComandoDeInteracao`).</summary>
	private bool ComandoDosServicosDoAlem(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case ServicosDoAlem.VerboPedir: PedirServicoDoAlem(pl, arg); return true;
			case ServicosDoAlem.VerboEntregar: EntregarServicoDoAlem(pl); return true;
			case ServicosDoAlem.VerboDesistir: DesistirDoServicoDoAlem(pl); return true;
			default: return false;
		}
	}

	private static FichaDoAlem FichaDoAlemDe(ServerPlayer pl) => pl.Ficha.ServicosNoAlem ??= new FichaDoAlem();

	private static string Maiuscula(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

	/// <summary>
	/// PEDIR SERVICO: sem escolha, o doador diz o que tem (uma linha por servico, com o pagamento e o porque de nao, se for o caso) e a
	/// lista sobe pro menu; com a escolha (o nome que a lista mostrou), ele entrega o servico ou diz o que falta.
	/// </summary>
	private void PedirServicoDoAlem(ServerPlayer pl, string escolha)
	{
		if (ObraQueAceita(pl, ServicosDoAlem.VerboPedir) is not { } obra)
		{
			Avisar(pl, "chegue perto de quem dá serviço -- o Oni da recepção fica ao lado da mesa do Enma.");
			return;
		}
		DoadorDoAlem doador = ServicosDoAlem.DoTipo(obra.Tipo);
		string quem = Maiuscula(ServicosDoAlem.NomeDo(doador));
		if (ServicosDoAlem.PorQueNaoAtende(pl.Ficha.dead) is { } vivo) { Avisar(pl, $"{quem}: \"{vivo}\""); return; }

		FichaDoAlem f = FichaDoAlemDe(pl);
		long agora = NowMs();
		if (escolha.Trim().Length == 0)
		{
			List<ServicoDoAlem> oferta = [.. ServicosDoAlem.De(doador)];
			foreach (ServicoDoAlem s in oferta)
				Avisar(pl, $"{quem}: \"{s.Pedido}\" -- {s.Recompensa:N0} zeni, {s.MinutosDePrazo} min"
						 + (ServicosDoAlem.PorQueNaoPega(f, s, pl.Ficha.dead, agora) is { } nao ? $" ({nao})" : ""));
			MandarEscolhas(pl, ServicosDoAlem.VerboPedir, oferta.Select(ServicosDoAlem.RotuloNaLista));
			return;
		}
		if (ServicosDoAlem.PeloNome(escolha) is not { } pedido || pedido.Doador != doador)
		{
			Avisar(pl, $"{quem} não tem esse serviço.");
			return;
		}
		if (ServicosDoAlem.PorQueNaoPega(f, pedido, pl.Ficha.dead, agora) is { } motivo) { Avisar(pl, motivo); return; }
		ServicosDoAlem.Pegar(f, pedido, agora);
		Avisar(pl, $"{quem}: \"{pedido.Pedido}\" Você tem {pedido.MinutosDePrazo} min, e o serviço paga {pedido.Recompensa:N0} zeni.");
		GD.Print($"[server] {pl.Name} pegou o servico do Outro Mundo '{pedido.Id}'");
		MandarServicoDoAlem(pl, forcar: true);
	}

	/// <summary>ENTREGAR: cumpre a entrega em curso se ela e pra ESTE doador -- e o destino passa a conhecer quem entregou (a rede).</summary>
	private void EntregarServicoDoAlem(ServerPlayer pl)
	{
		if (ObraQueAceita(pl, ServicosDoAlem.VerboEntregar) is not { } obra) { Avisar(pl, "chegue perto de quem vai receber a entrega."); return; }
		DoadorDoAlem destino = ServicosDoAlem.DoTipo(obra.Tipo);
		string quem = Maiuscula(ServicosDoAlem.NomeDo(destino));
		FichaDoAlem f = FichaDoAlemDe(pl);
		// O PRAZO E CONFERIDO AQUI TAMBEM: o tique de 1 Hz pode nao ter passado desde que ele venceu
		if (VencerOServicoDoAlem(pl, f, NowMs())) return;
		if (!ServicosDoAlem.EntregaPara(f, destino))
		{
			Avisar(pl, f.Atual.Length == 0
				? $"{quem} não espera nada de você."
				: $"{quem} não espera nada de você -- o seu serviço agora é: {ServicosDoAlem.Andamento(f)}.");
			return;
		}
		CumprirServicoDoAlem(pl, f, $"{quem} recebe a entrega");
	}

	private void DesistirDoServicoDoAlem(ServerPlayer pl)
	{
		FichaDoAlem f = FichaDoAlemDe(pl);
		if (ServicosDoAlem.Get(f.Atual) is not { } s) { Avisar(pl, "você não tem serviço nenhum do Outro Mundo."); return; }
		ServicosDoAlem.Desistir(f);
		Avisar(pl, $"você largou o serviço \"{s.Nome}\". Pode pegar outro (ou o mesmo) quando quiser.");
		MandarServicoDoAlem(pl, forcar: true);
	}

	/// <summary>Cumpre o servico em curso e paga. O zeni vai pro BOLSO, como toda recompensa do jogo -- o banco do Outro Mundo e logo ali.</summary>
	private void CumprirServicoDoAlem(ServerPlayer pl, FichaDoAlem f, string fala)
	{
		ServicoDoAlem? s = ServicosDoAlem.Get(f.Atual);
		double pago = ServicosDoAlem.Cumprir(f, NowMs());
		pl.Ficha.Zeni += pago;
		MandarCarteira(pl);
		Avisar(pl, $"{fala}: serviço \"{s?.Nome}\" cumprido. +{pago:N0} zeni.");
		GD.Print($"[server] {pl.Name} cumpriu o servico do Outro Mundo '{s?.Id}' (+{pago:N0} zeni)");
		MandarServicoDoAlem(pl, forcar: true);
	}

	/// <summary>O servico em curso venceu? Entao ele cai, avisado. Verdadeiro se caiu.</summary>
	private bool VencerOServicoDoAlem(ServerPlayer pl, FichaDoAlem f, long agora)
	{
		if (!ServicosDoAlem.Venceu(f, agora)) return false;
		string nome = ServicosDoAlem.Get(f.Atual)?.Nome ?? f.Atual;
		ServicosDoAlem.Desistir(f);
		Avisar(pl, $"o prazo do serviço \"{nome}\" venceu -- ele ficou sem fazer. Pode pedir de novo.");
		MandarServicoDoAlem(pl, forcar: true);
		return true;
	}

	// =====================================================================
	// O QUE ANDA SOZINHO: o nocaute, o segundo no lugar certo, o prazo e o revive
	// =====================================================================

	/// <summary>
	/// O NOCAUTE QUE CONTA -- chamado do funil unico da derrota (`AoPerderALuta`), entao vale o golpe, o raio, o agarrao e o que vier.
	/// So o molde do servico em curso e so corpo que ainda nao foi contado (`ServicosDoAlem.Nocaute`).
	/// </summary>
	private void ServicoDoAlemAoDerrotar(ServerPlayer vitima, ServerPlayer algoz)
	{
		if (vitima.Papel is not { } papel || algoz.Ficha.ServicosNoAlem is not { Atual.Length: > 0 } f || !algoz.Ficha.dead) return;
		if (!ServicosDoAlem.Nocaute(f, papel.Molde.Id, vitima.Id)) return;
		if (ServicosDoAlem.Pronto(f)) CumprirServicoDoAlem(algoz, f, "o serviço está feito");
		else
		{
			Avisar(algoz, $"serviço do Outro Mundo: {ServicosDoAlem.Andamento(f)}.");
			MandarServicoDoAlem(algoz, forcar: true);
		}
	}

	/// <summary>
	/// O TIQUE DE 1 Hz (pendurado ao lado do dos sentidos, `GameServer.cs`): o revive que derruba o servico, o prazo que vence, o segundo
	/// de meditacao ou de treino no lugar certo, e o cartao da aba World -- que so sai quando muda.
	/// </summary>
	private void TickDosServicosDoAlem()
	{
		if (_sigServicosDoAlem.Count > 0)
			foreach (int id in _sigServicosDoAlem.Keys.ToList())
				if (!_players.ContainsKey(id)) _sigServicosDoAlem.Remove(id);

		long agora = NowMs();
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (pl.Peer == null && EscutaDeServicosDoAlem == null) continue;
			if (pl.Ficha.ServicosNoAlem is { Atual.Length: > 0 } f)
			{
				if (ServicosDoAlem.PorQueNaoAtende(pl.Ficha.dead) != null)
				{
					string nome = ServicosDoAlem.Get(f.Atual)?.Nome ?? f.Atual;
					ServicosDoAlem.Desistir(f);
					Avisar(pl, $"você voltou à vida: o serviço \"{nome}\" do Outro Mundo fica pra trás.");
				}
				else if (!VencerOServicoDoAlem(pl, f, agora) && (pl.Ficha.med || pl.Ficha.train))
				{
					var cel = new Vec2(pl.Pos.X / Jandirus.Core.World.ZoneCollision.TileSize, pl.Pos.Y / Jandirus.Core.World.ZoneCollision.TileSize);
					LugarDoServico lugar = ServicosDoAlem.LugarEm(pl.Zone.Kind == ZoneKey.KindPremade ? pl.Zone.Name : "",
																   (int)Math.Floor(cel.X), (int)Math.Floor(cel.Y));
					if (ServicosDoAlem.Segundo(f, pl.Ficha.med ? TipoDeServico.Meditar : TipoDeServico.Treinar, lugar) && ServicosDoAlem.Pronto(f))
						CumprirServicoDoAlem(pl, f, "o serviço está feito");
				}
			}
			MandarServicoDoAlem(pl);
		}
	}

	// =====================================================================
	// O CARTAO DA ABA WORLD -- `S2C.ServicoDoAlem`
	// =====================================================================

	/// <summary>A ultima assinatura do cartao de cada um: o pacote so sai quando ela muda (o minuto do prazo, o progresso, a rede).</summary>
	private readonly Dictionary<int, string> _sigServicosDoAlem = [];

	/// <summary>A ESCUTA DA BANCADA: cada cartao que sairia, com quem. Nula em jogo.</summary>
	internal static List<(int Quem, NetDataWriter Pacote)>? EscutaDeServicosDoAlem;

	/// <summary>O cartao deste personagem: so o morto o ve; o servico em curso (se houver) e a rede inteira, com o estado de cada um.</summary>
	private static ServicoDoAlemState EstadoDosServicosDoAlem(ServerPlayer pl, long agora)
	{
		FichaDoAlem f = pl.Ficha.ServicosNoAlem ?? new FichaDoAlem();
		List<LinhaDeServico> rede = [.. ServicosDoAlem.Todos.Select(s => new LinhaDeServico(s.Nome, ServicosDoAlem.EstadoNaRede(f, s, agora)))];
		bool mostrar = ServicosDoAlem.PorQueNaoAtende(pl.Ficha.dead) == null;
		return ServicosDoAlem.Get(f.Atual) is { } atual
			? new ServicoDoAlemState(mostrar, atual.Nome, ServicosDoAlem.Andamento(f), (int)ServicosDoAlem.MinutosAte(f.PrazoMs, agora),
									 atual.Recompensa, rede)
			: new ServicoDoAlemState(mostrar, "", "", 0, 0, rede);
	}

	private void MandarServicoDoAlem(ServerPlayer pl, bool forcar = false)
	{
		ServicoDoAlemState s = EstadoDosServicosDoAlem(pl, NowMs());
		string sig = $"{s.Mostrar}|{s.Titulo}|{s.Andamento}|{s.Minutos}|" + string.Join(",", s.Rede.Select(l => l.Estado));
		if (!forcar && _sigServicosDoAlem.TryGetValue(pl.Id, out string? antiga) && antiga == sig) return;
		_sigServicosDoAlem[pl.Id] = sig;
		NetDataWriter w = Protocol.Begin(Protocol.S2C.ServicoDoAlem);
		FioDoServicoDoAlem.Escrever(w, s);
		EscutaDeServicosDoAlem?.Add((pl.Id, w));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
