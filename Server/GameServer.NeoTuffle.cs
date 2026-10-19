using System.Text.Json;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// UMA POSSE DO NEO TUFFLE EM CURSO: quem entrou (o Tuffle), em quem (o hospede), o corpo do Tuffle guardado enquanto ele esta dentro do
/// outro, e o que o mundo ve no lugar dele (o nome, a raca, o genero e a cara do hospede). Mora em `GameServer._posses`, pelos dois ids.
/// </summary>
public sealed class PosseDoTuffle
{
	public ServerPlayer Tuffle = null!, Hospede = null!;
	public Fighter FichaDoTuffle = null!;
	public CombatState CombateDoTuffle = null!;
	public DisfarceG12 Corpo = null!;
}

/// <summary>
/// ============================ O NEO TUFFLE (2026-10-18) ============================
/// O pedido do dono: a maquina dos Tsufurujins cria um NEO TUFFLE (em vez de androide/bio) que POSSUI jogadores nocauteados, e o cabelo do
/// possuido fica branco-acinzentado. E as tres respostas dele: quem vira e o PROPRIO Tsufurujin (como no Android Lab); a posse e "entra no
/// corpo" (como o Baby no Vegeta: o Tuffle dirige o corpo do nocauteado com o poder dele, e o dono so assiste); e ela acaba quando o corpo
/// possuido cai (o Tuffle e expulso e volta pro proprio corpo). O DU nao tem nada disto alem de uma roupa (`TuffleTux.dmi`): e sistema novo.
///
/// ============================ A MAQUINA ============================
/// O mainframe de androides ganha o terceiro laboratorio, o Neo Tuffle Lab (`Obra.Lab = 3`), que so um Tsufurujin instala, com a mesma
/// tecnologia dos outros dois (`TechDoLaboratorio`). Dentro dele o Tsufurujin vira Neo Tuffle (`Fighter.NeoTuffle`, no disco), pagando como
/// a conversao em androide de absorcao: um milhao de zeni ou o dobro em recursos. O poder nao muda -- o poder dele passa a ser o dos outros.
///
/// ============================ A POSSE: O TUFFLE ANDA NO CORPO DO OUTRO ============================
/// O verb "Possuir um nocauteado" (aba Other) pega o jogador nocauteado mais perto, ao alcance de uso. O cliente so dirige o proprio id
/// (`GameClient.LocalId` nasce no login e nao muda), entao quem anda no mundo e o corpo do TUFFLE, vestindo o do hospede:
///   * a ficha e o corpo de luta sao uma COPIA dos do hospede (`CorpoEmprestado`, o mesmo jeito do corpo do transe: a ficha por JSON, os
///     membros um a um) -- o poder, os stats, a raca da ficha e os membros que faltam. O Tuffle LEVANTA o corpo: a vida volta e o Ki
///     enche (o membro perdido continua perdido);
///   * o mundo ve o nome, a raca, o genero e a cara do hospede (`CorpoDaPosse`, pelo mesmo funil do disfarce -- `DisfarceDe`), com o cabelo
///     branco-acinzentado (o bit 1 do segundo byte de marcas do `PacoteDeAparencia`);
///   * o corpo de verdade do hospede some (`EstaOculto`), sai da grade, ninguem o alcanca (`CombatState.Possuido`) nem o sente
///     (`HospedeEscondido`) e anda com o Tuffle
///     (<see cref="TickDasPosses"/>); o dono dele nao mexe em nada (o portao do `Handle`) e o cliente dele segue o Tuffle (`S2C.Possuido`):
///     ele so assiste.
///   * A MENTE E DO TUFFLE: as tecnicas, a mochila, a raca do personagem e as formas continuam as dele. Ele usa o poder do outro, nao o
///     livro do outro.
///
/// ============================ O FIM: O CORPO POSSUIDO CAI ============================
/// Nocauteado ou morto o corpo emprestado, o Tuffle e expulso (<see cref="Expulsar"/>): ele volta pro proprio corpo -- como estava quando
/// entrou -- um tile ao lado, e o hospede volta a ser corpo onde o emprestado caiu, com o que ele sofreu la (os membros, o nocaute, a
/// morte). Sair do jogo (qualquer um dos dois) tambem expulsa, ANTES do save (o `Drop`). E o Tuffle nao vai pro disco enquanto esta dentro
/// do outro (`Persistir`): a ficha que ele carrega e a copia do corpo alheio -- o mesmo argumento da fusao.
/// ==================================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O laboratorio do Neo Tuffle no mainframe (`Obra.Lab`): 1 e o Android Lab, 2 o Bio-Android Lab.</summary>
	private const int LabDoNeoTuffle = 3;

	/// <summary>A raca que instala o laboratorio e vira Neo Tuffle (o Tsufurujin -- `NomeDaRaca`).</summary>
	private const string RacaDoTuffle = "Tsujin";

	/// <summary>A conversao, em zeni: a do androide de absorcao. Em recursos, o dobro (`CobrarOuExplicar`).</summary>
	private const double CustoNeoTuffle = 1_000_000;

	/// <summary>As posses em curso, pelos DOIS ids (o do Tuffle e o do hospede).</summary>
	private readonly Dictionary<int, PosseDoTuffle> _posses = [];

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): o corpo possuido cai e o Tuffle continua dentro. Lido pelo <see cref="TickDasPosses"/>. Falso em jogo, sempre.</summary>
	internal static bool PosseNaoAcabaNoNocauteDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): o corpo de verdade do hospede fica a vista -- o mundo ve dois dele. Lido pelo `EstaOculto`. Falso em jogo, sempre.</summary>
	internal static bool HospedeAVistaDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): o dono do corpo possuido continua mexendo nele. Lido pelo portao do `Handle`. Falso em jogo, sempre.</summary>
	internal static bool HospedeComAsRedeasDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste` e `--diagtuffle`): o pacote de aparencia sem o cabelo branco-acinzentado. Falso em jogo, sempre.</summary>
	internal static bool TuffleSemCabeloBrancoDeTeste;

	/// <summary>DEFEITO INJETADO (`--tuffleteste`): o Tsufurujin comum possui como o Neo Tuffle -- a maquina nao serve pra nada. Falso em jogo, sempre.</summary>
	internal static bool PosseSemAMaquinaDeTeste;

	// =====================================================================
	// A MAQUINA
	// =====================================================================
	/// <summary>VIRAR NEO TUFFLE, dentro do Neo Tuffle Lab: o Tsufurujin com a tecnologia e o dinheiro. O poder e a cara nao mudam.</summary>
	private void VirarNeoTuffle(ServerPlayer pl, Moeda moeda)
	{
		if (ObraPerto(pl) is not { Aparafusada: true, Lab: LabDoNeoTuffle })
		{ Avisar(pl, "você precisa estar dentro de um Neo Tuffle Lab."); return; }
		if (pl.Race != RacaDoTuffle) { Avisar(pl, "essa máquina foi feita pro corpo de um Tsufurujin."); return; }
		if (pl.Ficha.NeoTuffle) { Avisar(pl, "você já é um Neo Tuffle."); return; }
		if (pl.Ficha.techskill < TechDoLaboratorio)
		{
			Avisar(pl, $"isso pede {TechDoLaboratorio:0} de tecnologia -- você tem {pl.Ficha.techskill:0}.");
			return;
		}
		if (!CobrarOuExplicar(pl, CustoNeoTuffle, moeda, "a conversão")) return;

		pl.Ficha.NeoTuffle = true;
		pl.SigAtributos = "";
		GD.Print($"[server] {pl.Name} virou Neo Tuffle");
		Avisar(pl, "a máquina se fecha sobre você. Quando abre, você sente que caberia dentro de outro corpo -- e o dirigiria por dentro. "
				   + "(aba Other: \"Possuir um nocauteado\")");
		Persistir(pl);
	}

	// =====================================================================
	// A POSSE
	// =====================================================================
	/// <summary>O verb da aba Other. Mora na cadeia do `UsarHabilidade`, ao lado dos filhos.</summary>
	private bool UsarVerboDoTuffle(ServerPlayer pl, string id)
	{
		if (id != "tuffle_possuir") return false;
		Possuir(pl);
		return true;
	}

	/// <summary>A posse em que este corpo esta (como Tuffle ou como hospede), ou nula.</summary>
	private PosseDoTuffle? PosseDoTuffleDe(int id) => _posses.TryGetValue(id, out PosseDoTuffle? p) ? p : null;

	/// <summary>Este id e o corpo de verdade de alguem possuido agora? E o que esconde o corpo (`EstaOculto`).</summary>
	private bool EhHospedeDeTuffle(int id) => !HospedeAVistaDeTeste && _posses.TryGetValue(id, out PosseDoTuffle? p) && p.Hospede.Id == id;

	/// <summary>
	/// O MESMO ESCONDERIJO PRA QUEM PROCURA ENERGIA: o Sense, a telepatia e o Observe (`AchoAEnergia`) e o scouter (o scan dos `Sentidos`) nao
	/// acham o corpo de verdade do hospede -- a energia dele anda dentro do Tuffle, e ele apareceria duas vezes.
	/// </summary>
	private static bool HospedeEscondido(ServerPlayer o) => o.PossuidoPor != 0 && !HospedeAVistaDeTeste;

	/// <summary>
	/// O QUE O MUNDO VE NO LUGAR DO CORPO DE QUEM JOGA: o corpo possuido (o Tuffle andando como o hospede) ou o disfarce da imitacao. Um funil
	/// so pros dois -- o nome, a cara, a raca e o genero do `PacoteDeAparencia` perguntam aqui.
	/// </summary>
	private static DisfarceG12? DisfarceDe(ServerPlayer p) => p.CorpoDaPosse ?? p.Disfarce;

	/// <summary>
	/// POSSUIR: o Neo Tuffle, de pe no proprio corpo, entra no jogador nocauteado mais perto. Ver o cabecalho deste arquivo pro que muda.
	/// </summary>
	private void Possuir(ServerPlayer a)
	{
		if (PosseDoTuffleDe(a.Id) != null) { Avisar(a, "você já está dentro de outro corpo."); return; }
		if (!a.Ficha.NeoTuffle && !(PosseSemAMaquinaDeTeste && a.Race == RacaDoTuffle)) { Avisar(a, "você não é um Neo Tuffle."); return; }
		if (a.Ficha.KO || a.Ficha.dead) { Avisar(a, "caído, você não entra em corpo nenhum."); return; }
		if (EstaFundido(a.Id) || a.BonecoLargado != null || EhOlhoDeFora(a))
		{ Avisar(a, "agora não: você não está inteiro no próprio corpo."); return; }
		if (HospedeAoAlcance(a) is not { } b) { Avisar(a, "não há nenhum jogador nocauteado ao seu alcance."); return; }

		(Fighter emprestada, CombatState corpo) = CorpoEmprestado(a, b);
		var posse = new PosseDoTuffle
		{
			Tuffle = a, Hospede = b, FichaDoTuffle = a.Ficha, CombateDoTuffle = a.Combate,
			Corpo = new DisfarceG12 { Nome = NomeVisivel(b), Raca = b.Race, Genero = b.Genero, Visual = VisualVisivel(b).Copiar() },
		};
		a.Ficha = emprestada;
		a.Combate = corpo;
		a.CorpoDaPosse = posse.Corpo;
		a.Facing = b.Facing;
		CravarPosicao(a, b.Pos);
		b.PossuidoPor = a.Id;
		b.Combate.Possuido = true;
		_posses[a.Id] = posse;
		_posses[b.Id] = posse;

		a.Ficha.Statify();
		a.SigAtributos = "";
		AplicarPoderes(a);
		AplicarEfeitos(a);
		MandarSkills(a, forcar: true);
		MandarFicha(a);
		MandarAtributos(a);
		TrocarAparencias(a);
		MandarPossuido(b, a.Id);

		Avisar(a, $"você entra no corpo de {posse.Corpo.Nome}! Ele é seu até cair.");
		Avisar(b, "um Neo Tuffle entrou no seu corpo! Você só assiste -- ele sai quando o seu corpo cair.");
		// (O emote sai com o nome DO TUFFLE -- o `Falar` escreve o de quem joga --, e e a unica pista de quem esta la dentro: quem estava perto viu.)
		Falar(a, Protocol.Fala.Emote, $"entra no corpo de {posse.Corpo.Nome}, que se levanta com o cabelo branco-acinzentado.");
		GD.Print($"[server] NEO TUFFLE: {a.Name} entrou no corpo de {b.Name}");
	}

	/// <summary>
	/// O NOCAUTEADO QUE PODE SER POSSUIDO, o mais perto ao alcance de uso: um jogador (o NPC nao -- o pedido e de jogadores), vivo, no proprio
	/// corpo (sem fusao, sem transe, sem olho de nave) e que ninguem possui.
	/// </summary>
	private ServerPlayer? HospedeAoAlcance(ServerPlayer a) =>
		_players.Values
			.Where(o => o != a && EhJogador(o) && o.Zone.Equals(a.Zone) && o.Ficha.KO && !o.Ficha.dead && !EhCadaver(o)
						&& PosseDoTuffleDe(o.Id) == null && !EstaFundido(o.Id) && o.BonecoLargado == null && !EhOlhoDeFora(o)
						&& Vec2.Distance(o.Pos, a.Pos) <= AlcanceDeUso)
			.OrderBy(o => Vec2.Distance(o.Pos, a.Pos))
			.FirstOrDefault();

	/// <summary>
	/// O CORPO EMPRESTADO: uma copia da ficha do hospede (por JSON, o caminho do savefile -- como o corpo do transe, `DarCorpoProprio`) e um
	/// corpo de luta novo com os membros dele um a um. O Tuffle o levanta: o nocaute sai, a vida volta e o Ki enche; o membro perdido continua
	/// perdido. Os ganchos sao os do Tuffle (as pecas caem dele, o arremesso e o dele, a cena e a dele) -- e NAO ha `AoMorrer` nem
	/// `NegarMorte`: a morte deste corpo e a do hospede, cobrada pelas regras dele na expulsao.
	/// </summary>
	private (Fighter, CombatState) CorpoEmprestado(ServerPlayer tuffle, ServerPlayer hospede)
	{
		Fighter copia = JsonSerializer.Deserialize<Fighter>(JsonSerializer.Serialize(hospede.Ficha, CopiaDeFicha), CopiaDeFicha)!;
		copia.KO = false;
		copia.train = false;
		copia.med = false;

		var corpo = new CombatState(copia, TemRabo(hospede.Race), EixoDeRegen(hospede.Race, copia));
		foreach (BodyPart p in corpo.Corpo.Partes)
		{
			if (hospede.Combate.Corpo.Achar(p.Nome) is not { } real) continue;
			p.Decepado = real.Decepado;
			p.Vida = real.Decepado ? 0 : p.VidaMax;
		}
		corpo.AoDecepar = (_, caiu) => SoltarPecas(tuffle, caiu);
		corpo.SendoArremessado = () => tuffle.TiquesDeVoo > 0;
		corpo.EmCinematica = () => EmCena(tuffle);
		corpo.RitmoDaRecarga = () => RitmoDaRecargaDe(tuffle);
		corpo.SincronizarVida();
		copia.Statify();
		copia.Ki = copia.MaxKi;
		return (copia, corpo);
	}

	/// <summary>
	/// O TIQUE DAS POSSES, logo depois das naves: o corpo emprestado que caiu expulsa o Tuffle, e o corpo de verdade do hospede anda com ele
	/// -- e de la que o dono assiste (a zona do Tuffle e a que chega no cliente dele).
	/// </summary>
	private void TickDasPosses()
	{
		if (_posses.Count == 0) return;
		List<PosseDoTuffle> posses = [];
		foreach ((int id, PosseDoTuffle p) in _posses)
			if (id == p.Tuffle.Id) posses.Add(p);

		foreach (PosseDoTuffle p in posses)
		{
			ServerPlayer a = p.Tuffle, b = p.Hospede;
			if (!PosseNaoAcabaNoNocauteDeTeste && (a.Ficha.KO || a.Ficha.dead))
			{
				Expulsar(p, $"o corpo de {p.Corpo.Nome} caiu -- e você é expulso dele, de volta ao seu.");
				continue;
			}
			if (!b.Zone.Equals(a.Zone)) MoveToZone(b.Id, a.Zone, a.Pos);
			else b.Pos = a.Pos;
		}
	}

	/// <summary>
	/// A EXPULSAO: o Tuffle volta pro proprio corpo, como estava quando entrou, um tile ao lado; o hospede volta a ser corpo onde o emprestado
	/// estava, com o que ele sofreu la -- os membros e, pelas regras DELE, a morte (o `Morrer` do corpo dele, com o seguro e o Outro Mundo) ou o
	/// nocaute. Quem sai de um corpo o deixa caido: o hospede termina nocauteado mesmo quando a saida e por logout.
	/// </summary>
	private void Expulsar(PosseDoTuffle posse, string praOTuffle)
	{
		ServerPlayer a = posse.Tuffle, b = posse.Hospede;
		_posses.Remove(a.Id);
		_posses.Remove(b.Id);

		Fighter emprestada = a.Ficha;
		CombatState corpo = a.Combate;
		bool morreu = emprestada.dead || (corpo.Corpo.DeveMorrer() && !corpo.Corpo.RegeneraDecepado);
		ZoneKey zona = a.Zone;
		Vec2 onde = a.Pos;
		Facing caiuPara = a.DirecaoDeitado;

		// ---- o Tuffle volta pro proprio corpo ----
		a.Ficha = posse.FichaDoTuffle;
		a.Combate = posse.CombateDoTuffle;
		a.CorpoDaPosse = null;
		a.Ficha.Statify();
		a.SigAtributos = "";

		// ---- o hospede volta a ser corpo, onde o emprestado estava ----
		b.PossuidoPor = 0;
		b.Combate.Possuido = false;
		if (b.Zone.Equals(zona)) b.Pos = onde;
		else MoveToZone(b.Id, zona, onde);
		b.Facing = a.Facing;
		// DEITADO PRO MESMO RUMO em que o corpo emprestado caiu -- e o mesmo corpo, e quem viu a queda nao pode ve-lo virar no chao.
		b.CongelarDirecaoDeitada(caiuPara);
		foreach (BodyPart p in corpo.Corpo.Partes)
		{
			if (b.Combate.Corpo.Achar(p.Nome) is not { } real) continue;
			real.Decepado = p.Decepado;
			real.Vida = Math.Clamp(p.Vida, 0, real.VidaMax);
		}
		b.Combate.SincronizarVida();
		if (!morreu || !b.Combate.Morrer())
			b.Combate.Nocautear(corpo.NocauteRestante > 0 ? corpo.NocauteRestante : MeleeResolver.TetoDoNocaute, porVital: corpo.NocautePorVital);

		// ---- o Tuffle sai do lado ----
		Vec2 ao = onde + new Vec2(ZoneCollision.TileSize, 0);
		CravarPosicao(a, MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(ao) ?? ao);

		AplicarPoderes(a);
		AplicarEfeitos(a);
		MandarSkills(a, forcar: true);
		MandarFicha(a);
		MandarAtributos(a);
		TrocarAparencias(a);
		TrocarAparencias(b);
		MandarPossuido(b, 0);

		Avisar(a, praOTuffle);
		Avisar(b, "o Neo Tuffle saiu do seu corpo.");
		GD.Print($"[server] NEO TUFFLE: {a.Name} saiu do corpo de {b.Name}{(morreu ? " (o corpo morreu)" : "")}");
	}

	/// <summary>
	/// QUEM SAI DO JOGO NO MEIO DE UMA POSSE a desfaz ANTES do save -- seja o Tuffle (que carrega a copia do corpo alheio) ou o hospede (que
	/// esta escondido andando com ele). E o `SoltarDaFusao` desta mecanica: ver o `Drop`.
	/// </summary>
	private void SoltarDaPosse(ServerPlayer pl)
	{
		if (PosseDoTuffleDe(pl.Id) is not { } p) return;
		Expulsar(p, p.Tuffle == pl ? "você sai do corpo de " + p.Corpo.Nome + "." : $"o dono do corpo de {p.Corpo.Nome} saiu do jogo -- e você é expulso.");
	}

	/// <summary>O cliente do hospede passa a seguir o corpo do possessor (ou volta ao proprio, com 0). Ver `Protocol.S2C.Possuido`.</summary>
	private static void MandarPossuido(ServerPlayer hospede, int possessor)
	{
		if (hospede.Peer == null) return;
		var w = Protocol.Begin(Protocol.S2C.Possuido);
		w.Put(possessor);
		hospede.Peer.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
