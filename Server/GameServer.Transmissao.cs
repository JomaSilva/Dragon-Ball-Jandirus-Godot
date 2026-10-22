using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O INSTANT TRANSMISSION DO DONO (2026-10-21). O verbo continua o <c>Instant_Transmission</c> do lote G11
/// (<c>GameServer.Tecnicas.G11.cs</c> registra; o tique da concentracao tambem ficou la). O que mudou mora aqui.
///
/// O ALVO e quem a SUA leitura de Sense alcancaria no SEU nivel de Instant Transmission (<see cref="TransmissaoEmNiveis"/>):
/// jogador, NPC, chefe, e o morto de pe no Outro Mundo. Os filtros velhos da lista (a conta de BP, a familiaridade,
/// o "weak or concealing", o veto do Alem) sairam: o que o Sense nao mostra, isto tambem nao alcanca. O androide
/// continua fora pela regra do dono de 2026-10-19 (<see cref="TravaNaEnergia"/>). Ficam as recusas de destino
/// fechado (Sala do Tempo, mente, interior de Majin, selo -- <see cref="ZonaFechadaAoTeleporte"/>) e as de quem usa
/// (preso, esferas que prendem no planeta, via <see cref="PorQueNaoSaiDaqui"/>).
///
/// O BOTAO da aba Sense mira pelo id do corpo (<see cref="Protocol.PresencaState.Corpo"/>). Nome repetido nao escolhe
/// ninguem: homonimo e o normal neste jogo.
///
/// A CARONA e outra. Kai Kai e Saltar continuam com o <c>LevarCaronas</c> de 1,5 tile. Aqui so vai quem medita a
/// 1 tile se o usuario TAMBEM medita, e quem ele esta segurando ou carregando. Pro espaco o agarrado nao vai
/// (o dono: forcar alguem pro espaco e "op") -- solta, com aviso. O grupo anda por <c>MoveToZone</c>/<c>CravarPosicao</c>
/// um a um, nesta funcao. Nao ha carona pendurada dentro do <c>MoveToZone</c> (isso e do agente das portas e naves).
///
/// O MORTO que sai do Outro Mundo rumo aos vivos continua na passagem de 5 s (<c>GameServer.PassagemDoMorto.cs</c>),
/// ele e cada carona, cada um. Quem chega no espaco chega voando, na altura de pairar.
/// </summary>
public partial class GameServer
{
	/// <summary>DEFEITO INJETADO (bancada --teleporteteste): a concentracao volta a durar 60 s. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoSemTetoDeEsperaDeTeste;

	/// <summary>DEFEITO INJETADO: o nivel nao limita. O nivel 1 alcanca o universo. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoSemNivelDeTeste;

	/// <summary>DEFEITO INJETADO: quem esta de pe a 1 tile vai junto, como a carona velha. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoLevaQuemEstaDePeDeTeste;

	/// <summary>DEFEITO INJETADO: o nome escolhe o primeiro homonimo, e nao recusa. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoAceitaNomeAmbiguoDeTeste;

	/// <summary>DEFEITO INJETADO: o agarrado e levado ao espaco. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoLevaAgarraAoEspacoDeTeste;

	/// <summary>DEFEITO INJETADO: nao sai o piscar pras duas pontas. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoSemPiscarDeTeste;

	/// <summary>DEFEITO INJETADO: meditar volta a recusar o verbo, como antes de 2026-10-21. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoRecusaMeditandoDeTeste;

	/// <summary>DEFEITO INJETADO: quem chega no espaco chega a pe (o <c>MoveToZone</c> apaga o voo e ninguem liga de novo). Falso em jogo, sempre.</summary>
	internal static bool TransmissaoChegaAPeNoEspacoDeTeste;

	/// <summary>DEFEITO INJETADO: o morto que sai do Alem nao arma a passagem. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoSemPassagemDeTeste;

	/// <summary>DEFEITO INJETADO: o cartao do Sense viaja sem o id do corpo. Falso em jogo, sempre.</summary>
	internal static bool TransmissaoSemIdNoFioDeTeste;

	/// <summary>A BANCADA ESCUTA O PISCAR (corpo forjado nao tem Peer). Nula em jogo.</summary>
	internal static List<(int Id, float X, float Y)>? EscutaDaTransmissao;

	/// <summary>O id que o cartao do Sense carrega. O defeito zera, e o botao nao tem em quem mirar.</summary>
	private static int IdNoCartaoDoSense(ServerPlayer o) => TransmissaoSemIdNoFioDeTeste ? 0 : o.Id;

	/// <summary>O nivel de Instant Transmission deste corpo. Ver <see cref="TransmissaoEmNiveis.NivelDe"/>.</summary>
	internal static int NivelDaTransmissao(ServerPlayer pl) =>
		TransmissaoEmNiveis.NivelDe(pl.Niveis.Nivel(TransmissaoEmNiveis.Path), pl.Ficha.teleskill);

	/// <summary>0 no fio quando a skill nao esta no livro: a aba nao desenha botao nenhum.</summary>
	private static byte NivelItNoFio(ServerPlayer pl) =>
		pl.Livro?.Sabe(TransmissaoEmNiveis.Path) == true
			? (byte)NivelDaTransmissao(pl)
			: (byte)0;

	/// <summary>
	/// O pedido. Sem argumento, lista. Com argumento, concentra. O argumento <c>#id</c> e o botao da aba;
	/// nome ou assinatura continuam valendo quando apontam UMA pessoa so.
	/// </summary>
	private void TeletransporteG11(ServerPlayer pl, string arg)
	{
		if (RecusarPelaMenteFragmentada(pl)) return;
		Fighter f = pl.Ficha;
		if (f.KO || EhCadaver(pl)) { Avisar(pl, "nao da, caido."); return; }
		// (v0.3.2) O MORTO DE PE TRANSMITE. Quem sai do Outro Mundo chega de passagem. O defeito da v030 desliga isto.
		if (f.dead && MortoNaoTransmiteDeTeste) { Avisar(pl, "um morto nao sai do Outro Mundo por conta propria."); return; }
		if (pl.Combate is { Stun: > 0 }) { Avisar(pl, "voce ainda esta atordoado."); return; }
		if (Agarrado(pl)) { Avisar(pl, "preso num agarrao voce nao se concentra."); return; }
		// MEDITAR NAO RECUSA MAIS (dono, 2026-10-21): de pe ou meditando, o verbo sai. Treinar, mente, selo e canal continuam.
		if ((TransmissaoRecusaMeditandoDeTeste && f.med) || f.train || NaMente(pl) || pl.Selo.Preso || _canais.ContainsKey(pl.Id))
		{
			Avisar(pl, "voce nao consegue fazer isso agora.");
			return;
		}
		if (PorQueNaoSaiDaqui(pl) is { Length: > 0 } preso) { Avisar(pl, preso); return; }
		if (_transmissaoG11.ContainsKey(pl.Id)) { Avisar(pl, "voce ja esta se concentrando -- nao se mexa."); return; }

		List<(ServerPlayer Quem, byte Alcance, string Rotulo, bool Conhecido)> lista = AlvosDaTransmissao(pl);
		if (lista.Count == 0)
		{
			Avisar(pl, "voce nao sente nenhuma assinatura valida. Chegue mais perto, ou conheca melhor as pessoas.");
			return;
		}

		if (arg.Length == 0)
		{
			Avisar(pl, "assinaturas de Ki ao alcance:");
			foreach ((ServerPlayer q, byte _, string rotulo, bool conhecido) in lista)
			{
				double razao = q.Ficha.expressedBP / Math.Max(PoderQueSeCompara(pl), 1e-9);
				if (!conhecido) razao *= _rng.Next(100, 1001) / 500.0;
				Avisar(pl, $"  {rotulo}: parece ter {razao:0.##}x o seu poder");
			}
			Avisar(pl, "para ir: Instant_Transmission:<nome ou assinatura>");
			return;
		}

		if (AlvoDoPedido(pl, arg, lista) is not { } alvo) return;
		if (alvo.Zone.Hash != pl.Zone.Hash && ZonaFechadaAoTeleporte(alvo.Zone))
		{
			Avisar(pl, "a assinatura que voce seguia entrou num lugar onde nao se chega assim.");
			return;
		}

		// O CUSTO continua o que o verbo ja cobrava (`kireq`, yardrat.dm:101), sem o BaseDrain por cima.
		double teleskill = Math.Max(f.teleskill, 1e-9);
		double kireq = Math.Min(f.MaxKi, f.MaxKi / (teleskill / 100));
		int nivel = NivelDaTransmissao(pl);
		bool yardrat = string.Equals(pl.Race, "Yardrat", StringComparison.OrdinalIgnoreCase);
		long espera = TransmissaoEmNiveis.MsDaConcentracao(nivel, yardrat, TransmissaoSemTetoDeEsperaDeTeste);

		_transmissaoG11[pl.Id] = new TransmissaoG11
		{
			Alvo = alvo.Id, QuandoMs = NowMs() + espera, Ancora = pl.Pos, Kireq = kireq,
		};
		string frase = $"{pl.Name} toca a própria testa e se concentra.";
		Avisar(pl, frase);
		AvisarPertoG3(pl, RaioDaVista, frase, excetoCentro: true);
		GD.Print($"[G11] {pl.Name} se concentra em {alvo.Name} ({espera} ms, nivel {nivel}, teleskill {f.teleskill:0.#})");
	}

	/// <summary>A lista no formato que as bancadas vizinhas ja leem (o <c>Quem</c>). O alcance novo nao sai por aqui.</summary>
	private List<(ServerPlayer Quem, string Rotulo, bool Conhecido)> ListaDeAssinaturasG11(ServerPlayer pl)
	{
		var lista = new List<(ServerPlayer, string, bool)>();
		foreach ((ServerPlayer quem, byte _, string rotulo, bool conhecido) in AlvosDaTransmissao(pl))
			lista.Add((quem, rotulo, conhecido));
		return lista;
	}

	/// <summary>
	/// Quem da pra alcancar AGORA, no nivel deste corpo. A mesma peneira do Sense (<c>SentidosDe</c>): a energia
	/// (<see cref="TravaNaEnergia"/>), NPC so ate a regiao, e de longe so jogador e chefe. O nivel aqui e o do teleporte,
	/// nao o do Sense -- da pra ter um sem o outro. Cadaver fica de fora; morto de pe, nao.
	/// </summary>
	private List<(ServerPlayer Quem, byte Alcance, string Rotulo, bool Conhecido)> AlvosDaTransmissao(ServerPlayer pl)
	{
		int nivelDoAlcance = TransmissaoSemNivelDeTeste ? TransmissaoEmNiveis.Universo : NivelDaTransmissao(pl);
		var lista = new List<(ServerPlayer, byte, string, bool)>();
		var vistos = new HashSet<int>();
		(int mx, int my) = TileDe(pl.Pos);
		bool noEspaco = Espaco.EhEspaco(pl.Zone);
		SistemaId? meuSistema = nivelDoAlcance >= TransmissaoEmNiveis.Sistema ? SistemaDe(pl) : null;
		byte Longe(ServerPlayer o) => meuSistema is { } ms && SistemaDe(o) is { } os && os == ms
			? (byte)TransmissaoEmNiveis.Sistema : (byte)TransmissaoEmNiveis.Universo;

		List<ServerPlayer> zona = _zones.TryGetValue(pl.Zone.Hash, out List<ServerPlayer>? z) ? z : [];
		foreach (ServerPlayer o in zona)
		{
			if (o == pl || o.ECadaver || EhCadaver(o) || !TravaNaEnergia(pl, o)) continue;
			ushort dist = DistanciaEmTiles(pl, o);
			byte alcance = dist <= SenseEmNiveis.TilesDoCampoDeVisao ? (byte)TransmissaoEmNiveis.CampoDeVisao
				: dist <= SenseEmNiveis.TilesDaRegiao ? (byte)TransmissaoEmNiveis.Regiao
				: noEspaco ? Longe(o) : (byte)TransmissaoEmNiveis.Planeta;
			if (alcance > nivelDoAlcance || (alcance > TransmissaoEmNiveis.Regiao && !GenteOuChefe(o))) continue;
			vistos.Add(o.Id);
			(string rotulo, bool conhecido) = RotuloDaTransmissao(pl, o);
			lista.Add((o, alcance, rotulo, conhecido));
		}

		if (nivelDoAlcance >= TransmissaoEmNiveis.Sistema)
			foreach (ServerPlayer o in _players.Values)
			{
				if (o == pl || o.ECadaver || EhCadaver(o) || vistos.Contains(o.Id) || o.Zone.Hash == pl.Zone.Hash) continue;
				if (!GenteOuChefe(o) || !TravaNaEnergia(pl, o)) continue;
				byte alcance = Longe(o);
				if (alcance > nivelDoAlcance) continue;
				vistos.Add(o.Id);
				(string rotulo, bool conhecido) = RotuloDaTransmissao(pl, o);
				lista.Add((o, alcance, rotulo, conhecido));
			}
		return lista;
	}

	/// <summary>
	/// A ENERGIA EM QUE O TELEPORTE TRAVA: a do Sense (<see cref="AchoAEnergia"/>: escondido, o piso dos 5, o ki divino, o hospede
	/// do Neo Tuffle) sem a letra da raca "Android", que e do Sense do DM -- o <c>yardrat.dm</c> nao testa raca. O ANDROIDE NAO
	/// TEM KI E O TELEPORTE NAO TRAVA NELE pela regra do dono (2026-10-19, <see cref="AndroideSemKi"/>), a mesma linha da lista
	/// de antes: o botao de defeito dela o poe de volta aqui, e a familia 6 da <c>--sentidosteste</c> ve a regua reprovar.
	/// </summary>
	private bool TravaNaEnergia(ServerPlayer eu, ServerPlayer o) =>
		!AndroideSemKi(o) && !o.Ficha.isconcealed && TemEnergiaQueSeSente(o)
		&& (!KiDivinoEscondido(o) || SenteKiDivino(eu)) && !HospedeEscondido(o);

	/// <summary>NPC pelo nome (nao tem assinatura). Pessoa conhecida pelo nome; o resto, pela assinatura.</summary>
	private static (string Rotulo, bool Conhecido) RotuloDaTransmissao(ServerPlayer eu, ServerPlayer o)
	{
		if (Gente.EhNpcDoMundo(o.Peer != null, o.Papel)) return (o.Name, true);
		bool conhecido = eu.Social.Conhece(o.Assinatura) || eu.Social.Familiaridade(o.Assinatura) > 0;
		return conhecido ? (o.Name, true) : (o.Assinatura, false);
	}

	/// <summary>
	/// O <c>#id</c> do botao acha o corpo. Nome ou assinatura so servem quando sobra UMA pessoa: dois "Lobo"
	/// no alcance recusam, em vez de levar ao primeiro da lista.
	/// </summary>
	private ServerPlayer? AlvoDoPedido(ServerPlayer pl, string arg,
									   List<(ServerPlayer Quem, byte Alcance, string Rotulo, bool Conhecido)> lista)
	{
		if (arg.Length > 1 && arg[0] == '#' && int.TryParse(arg.AsSpan(1), out int id))
		{
			foreach ((ServerPlayer quem, byte _, string _, bool _) in lista)
				if (quem.Id == id) return quem;
			Avisar(pl, "essa presenca saiu do alcance.");
			return null;
		}

		var iguais = new List<ServerPlayer>();
		foreach ((ServerPlayer quem, byte _, string rotulo, bool _) in lista)
			if (string.Equals(rotulo, arg, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(quem.Name, arg, StringComparison.OrdinalIgnoreCase)
				|| (quem.Assinatura.Length > 0 && string.Equals(quem.Assinatura, arg, StringComparison.OrdinalIgnoreCase)))
				iguais.Add(quem);
		if (iguais.Count == 0) { Avisar(pl, $"nao ha assinatura '{arg}' ao seu alcance."); return null; }
		if (iguais.Count > 1 && !TransmissaoAceitaNomeAmbiguoDeTeste)
		{
			Avisar(pl, "há mais de uma pessoa com esse nome no alcance. Use o botão da aba Sense.");
			return null;
		}
		return iguais[0];
	}

	/// <summary>A chegada, no fim da concentracao. O grupo e desta funcao: ninguem e puxado pelo <c>MoveToZone</c>.</summary>
	private void ChegarPelaTransmissaoG11(ServerPlayer pl, TransmissaoG11 t)
	{
		Fighter f = pl.Ficha;
		if (!_players.TryGetValue(t.Alvo, out ServerPlayer? alvo) || alvo.ECadaver || EhCadaver(alvo))
		{
			Avisar(pl, "a assinatura que voce seguia se apagou.");
			return;
		}
		// As duas perguntas se refazem ANTES de cobrar: da pra ter ficado preso no meio, e o alvo da pra ter entrado
		// onde nao se chega. O morto de pe continua sendo alvo -- o cadaver, nao.
		if (PorQueNaoSaiDaqui(pl) is { Length: > 0 } preso) { Avisar(pl, preso); return; }
		if (alvo.Zone.Hash != pl.Zone.Hash && ZonaFechadaAoTeleporte(alvo.Zone))
		{
			Avisar(pl, "a assinatura que voce seguia entrou num lugar onde nao se chega assim.");
			return;
		}

		bool mesmaZona = alvo.Zone.Hash == pl.Zone.Hash;
		double distTiles = mesmaZona ? Vec2.Distance(alvo.Pos, pl.Pos) / ZoneCollision.TileSize : 30;
		bool yardrat = string.Equals(pl.Race, "Yardrat", StringComparison.OrdinalIgnoreCase);
		if (yardrat && f.teleskill < 500) f.teleskill = Math.Min(500, f.teleskill + distTiles * 0.2);
		else if (!yardrat && f.teleskill < 300) f.teleskill = Math.Min(300, f.teleskill + distTiles * 0.1);
		f.Ki -= t.Kireq;
		f.Ki = Math.Max(f.Ki, 0);

		List<ServerPlayer> caronas = GrupoDaTransmissao(pl, alvo.Zone);
		List<ServerPlayer> dePassagem = [.. caronas.Prepend(pl).Where(q => q.Ficha.dead && Alem.EhOAlem(q.Zone) && !Alem.EhOAlem(alvo.Zone))];
		if (!TransmissaoSemPassagemDeTeste)
			foreach (ServerPlayer quem in dePassagem) ArmarAPassagem(quem);

		PiscarNaZona(pl.Zone, pl.Id, pl.Pos);
		foreach (ServerPlayer c in caronas) PiscarNaZona(c.Zone, c.Id, c.Pos);
		AvisarPertoG3(pl, RaioDaVista, $"{pl.Name} desaparece num clarao!", excetoCentro: true);

		Vec2 ponto = PontoAoLadoG11(alvo);
		Saltar(pl, alvo.Zone, ponto, caronas, "te leva junto no Teletransporte.");
		foreach (ServerPlayer quem in dePassagem) AvisarSePessoa(quem, FraseDaPassagem);

		if (Espaco.EhEspaco(alvo.Zone) && !TransmissaoChegaAPeNoEspacoDeTeste)
		{
			LigarOVooDaChegada(pl);
			foreach (ServerPlayer c in caronas) LigarOVooDaChegada(c);
		}

		PiscarNaZona(pl.Zone, pl.Id, pl.Pos);
		foreach (ServerPlayer c in caronas)
			if (_players.ContainsKey(c.Id)) PiscarNaZona(c.Zone, c.Id, c.Pos);

		Avisar(pl, "voce localiza a assinatura e aparece num instante!");
		Avisar(alvo, $"{pl.Name} aparece num instante ao seu lado!");
		GD.Print($"[G11] {pl.Name} se teletransportou ate {alvo.Name} (teleskill {f.teleskill:0.#}, +{caronas.Count} carona)");
	}

	/// <summary>
	/// Quem vai junto, colhido ANTES do salto. Meditando a 1 tile, os dois; ou no agarrao (segurar ou carregar).
	/// O agarrado a caminho do espaco e solto aqui, ainda na zona de saida, e nao entra na lista.
	/// </summary>
	private List<ServerPlayer> GrupoDaTransmissao(ServerPlayer pl, ZoneKey destino)
	{
		var grupo = new List<ServerPlayer>();
		var vistos = new HashSet<int>();
		ServerPlayer? agarrado = QuemEuSeguro(pl);
		bool segura = agarrado != null
			&& pl.ModoDoAgarrao is ModoDeAgarrao.Segurando or ModoDeAgarrao.Carregando;
		// Soltar no espaco nao pode deixar a mesma pessoa entrar de novo pela regra de quem medita.
		int ficouPorSerEspaco = 0;

		if (segura && Espaco.EhEspaco(destino) && !TransmissaoLevaAgarraAoEspacoDeTeste)
		{
			ficouPorSerEspaco = agarrado!.Id;
			// O `Soltar` ja diz "voce solta X" / "X solta voce": aqui vai so o PORQUE, uma linha a cada um, depois dele.
			Soltar(pl, MotivoDaSoltura.Tecla);
			Avisar(pl, "o Instant Transmission não leva ninguém para o espaço à força.");
			AvisarSePessoa(agarrado, "o Instant Transmission não leva ninguém para o espaço à força.");
			segura = false;
			agarrado = null;
		}

		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			if (o == pl || o.Id == ficouPorSerEspaco || !_players.ContainsKey(o.Id) || o.ECadaver || EhCadaver(o)) continue;
			if (PorQueNaoSaiDaqui(o).Length > 0) continue;
			int tiles = DistanciaEmTiles(pl, o);
			bool meditandoJunto = pl.Ficha.med && o.Ficha.med && tiles <= TransmissaoEmNiveis.TilesDaCarona;
			if (TransmissaoLevaQuemEstaDePeDeTeste && tiles <= TransmissaoEmNiveis.TilesDaCarona) meditandoJunto = true;
			if (!meditandoJunto) continue;
			vistos.Add(o.Id);
			grupo.Add(o);
		}

		if (segura && agarrado != null && !vistos.Contains(agarrado.Id) && _players.ContainsKey(agarrado.Id)
			&& PorQueNaoSaiDaqui(agarrado).Length == 0 && !agarrado.ECadaver && !EhCadaver(agarrado))
			grupo.Add(agarrado);
		return grupo;
	}

	/// <summary>O <c>MoveToZone</c> apaga o voo de toda troca de zona. No espaco, sem isto, o corpo chega e nao anda.</summary>
	private static void LigarOVooDaChegada(ServerPlayer pl)
	{
		pl.Voando = true;
		pl.Altitude = Voo.AlturaDePairar;
		pl.QuerSubir = pl.QuerDescer = false;
	}

	/// <summary>
	/// O piscar, pra zona inteira: o mesmo efeito de desviar um golpe, nas duas pontas. O som e do cliente,
	/// posicionado, entao quem esta longe ve o corpo sumir e nao ouve.
	/// </summary>
	private void PiscarNaZona(ZoneKey zona, int id, Vec2 pos)
	{
		if (TransmissaoSemPiscarDeTeste) return;
		EscutaDaTransmissao?.Add((id, pos.X, pos.Y));
		NetDataWriter w = Protocol.Begin(Protocol.S2C.TransmissaoPiscar);
		w.Put(id);
		w.Put(pos.X);
		w.Put(pos.Y);
		foreach (ServerPlayer o in ZoneList(zona.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
