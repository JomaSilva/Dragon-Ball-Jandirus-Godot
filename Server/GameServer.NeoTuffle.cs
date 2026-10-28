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

	/// <summary>
	/// (2026-10-28, o pedido 144) O RESTO DO CORPO DO TUFFLE, guardado enquanto ele usa o do hospede: a raca e a classe (as passivas de raca,
	/// a Ascensao e a escada de formas perguntam por elas), o livro de tecnicas e o estado das formas. Voltam na expulsao.
	/// </summary>
	public string RacaDoTuffle = "", ClasseDoTuffle = "";
	public Jandirus.Core.Skills.SkillBook LivroDoTuffle = null!;
	public Jandirus.Core.Forms.EstadoDeForma FormaDoTuffle = null!;
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
/// (2026-10-20) O CORPO MUDA: no proprio corpo o Neo Tuffle anda na folha do Baby (`Bebi 1 Alt`), careca e sem os olhos de gente, com a roupa
/// da criacao por cima -- o pedido do dono, *"ao inves de usar o sprite normal do personagem q se transformou em neotuffle"*, e a escolha dele
/// sobre a roupa. Ver `Appearance.CorpoDoTuffle`.
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
///   * (2026-10-28, o pedido 144) O CORPO E O DO HOSPEDE POR INTEIRO: *"poder usar todas as skills e formas desse corpo"* e *"ele n vai ter
///     ascencion bonus nem nada, ele vai ter literal tudo do corpo do hospedeiro, formas etc, e se a raça do hospedeiro n tiver ascencion
///     ele tb n vai ter"*. Alem da ficha, o Tuffle passa a ter a RACA e a CLASSE do hospede (as passivas de raca, a Ascensao -- que so
///     sobe raca sem escada de formas -- e a escada que a tecla C sobe), e uma COPIA do livro de tecnicas e do estado das formas dele (o
///     que o Tuffle treinar nelas fica na copia, como o poder). Os dele voltam na expulsao (`PosseDoTuffle`). A MOCHILA continua dele.
///     Ate aqui a mente era do Tuffle: as tecnicas, a raca e as formas eram as dele, e a Ascensao do Tsufurujin subia no poder do outro.
///
/// ============================ O FIM: O CORPO POSSUIDO CAI -- OU O TUFFLE SAI ============================
/// (2026-10-28, o pedido 144) O Tuffle sai quando quiser: o verb "Sair do corpo" (aba Other, so pra quem esta dentro de outro corpo --
/// `Fatos.DentroDeOutroCorpo`) e a mesma expulsao, e o hospede fica caido onde estava.
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

	/// <summary>
	/// DEFEITO INJETADO (`--tuffleteste` e `--diagtuffle`): o pacote de aparencia sem a marca do corpo do Neo Tuffle -- ele anda com a cara da
	/// criacao, cabelo e roupa, como antes de 2026-10-20. Lido pelo `PacoteDeAparencia`. Falso em jogo, sempre.
	/// </summary>
	internal static bool TuffleSemCorpoDoBabyDeTeste;

	// =====================================================================
	// A MAQUINA
	// =====================================================================
	/// <summary>
	/// VIRAR NEO TUFFLE, dentro do Neo Tuffle Lab: o Tsufurujin com a tecnologia e o dinheiro. O poder nao muda; o CORPO sim (2026-10-20, o dono):
	/// ele passa a andar na folha do Baby, careca, com a roupa da criacao por cima (`Appearance.CorpoDoTuffle`) -- e a zona ve na hora.
	/// </summary>
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
		// O CORPO NOVO APARECE AGORA, pra ele e pra quem esta perto -- o pacote de aparencia leva a marca (`PacoteDeAparencia`, o bit 2 do
		// segundo byte de marcas). Sem esta linha a folha do Baby so chegaria na proxima troca de zona.
		ReapresentarAparencia(pl);
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
		switch (id)
		{
			case "tuffle_possuir": Possuir(pl); return true;
			case "tuffle_sair": SairDoCorpo(pl); return true;   // (2026-10-28) o pedido 144
			default: return false;
		}
	}

	/// <summary>
	/// (2026-10-28, o pedido 144) SAIR DO CORPO QUANDO QUISER: *"permita o neotuffle ter uma opçao de sair do corpo de quem ele invadiu quando
	/// quiser"*. E a mesma expulsao do corpo que cai (<see cref="Expulsar"/>): o Tuffle volta pro proprio corpo um tile ao lado, e o hospede
	/// fica onde estava, caido -- quem sai de um corpo o deixa no chao, como no logout.
	/// </summary>
	private void SairDoCorpo(ServerPlayer a)
	{
		if (PosseDoTuffleDe(a.Id) is not { } p || p.Tuffle != a) { Avisar(a, "você não está dentro de outro corpo."); return; }
		Expulsar(p, $"você sai do corpo de {p.Corpo.Nome}.");
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

		// (2026-10-28, o pedido 144) O RESTO DO CORPO DO HOSPEDE: a raca e a classe dele, e uma copia do livro e das formas -- ver o cabecalho.
		// Os do Tuffle ficam guardados na posse e voltam na expulsao. Os `AplicarPoderes`/`AplicarEfeitos` logo abaixo ja leem o livro novo:
		// os efeitos das tecnicas DO HOSPEDE sobre a ficha dele, e as arvores pela raca dele.
		posse.RacaDoTuffle = a.Race;
		posse.ClasseDoTuffle = a.Class;
		posse.LivroDoTuffle = a.Livro;
		posse.FormaDoTuffle = a.Forma;
		if (!TuffleComAMenteDeTeste)
		{
			a.Race = b.Race;
			a.Class = b.Class;
			a.Livro = CopiaDoLivro(b.Livro);
			a.Forma = CopiaDasFormas(b.Forma);
		}

		// (A ASCENSAO e a do corpo sem linha nenhuma aqui: a ficha copiada traz o bonus do hospede, e o tique que a reafirma -- `AfirmarAAscensao`
		// -- pergunta pela raca, que agora e a dele. Com a raca do Tuffle, o tique punha a Ascensao do Tsufurujin no poder do outro.)
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
	/// (2026-10-28, o pedido 144) AS TECNICAS DO HOSPEDE, COPIADAS -- pelo mesmo caminho do login (`PrepararSkills`, que monta o livro do que o
	/// save guarda): as aprendidas, as ensinadas, as compradas, as escolhas, o que a compra somou e os marcos. O que o Tuffle fizer com elas
	/// (comprar, esquecer, escolher) fica na copia, como o poder na ficha copiada. As arvores destravadas se recontam no `AplicarEfeitos`.
	/// </summary>
	private static Jandirus.Core.Skills.SkillBook CopiaDoLivro(Jandirus.Core.Skills.SkillBook o)
	{
		var l = new Jandirus.Core.Skills.SkillBook();
		l.Carregar([.. o.Aprendidas]);
		l.CarregarEnsinadas([.. o.Ensinadas]);
		l.CarregarCompradas([.. o.Compradas]);
		l.CarregarEscolhas(new Dictionary<string, int>(o.Escolhas));
		l.CarregarGanhos(o.GanhosNaCompra.ToDictionary(kv => kv.Key, kv => new Dictionary<string, double>(kv.Value)));
		l.MarcosTotais = o.MarcosTotais;
		l.MarcosLivres = o.MarcosLivres;
		l.MarcosPorBp = o.MarcosPorBp;
		return l;
	}

	/// <summary>
	/// (2026-10-28, o pedido 144) AS FORMAS DO HOSPEDE, COPIADAS -- o que o save guardaria delas (`RestaurarFormaEDisciplina`): as despertadas,
	/// as ja estreadas, as portas cortadas, a maestria de cada uma, os limiares sorteados no nascimento dele e as duas preferencias do C. Na
	/// base: o corpo caido nao esta em forma nenhuma. A maestria que o Tuffle treinar nelas fica na copia.
	/// </summary>
	private static Jandirus.Core.Forms.EstadoDeForma CopiaDasFormas(Jandirus.Core.Forms.EstadoDeForma o)
	{
		var c = new Jandirus.Core.Forms.EstadoDeForma
		{
			Limiares = o.Limiares,
			Liberadas = [.. o.Liberadas],
			EstreiaVista = [.. o.EstreiaVista],
			PortasCortadas = [.. o.PortasCortadas],
			GradesLigados = o.GradesLigados,
			CPelasDivinas = o.CPelasDivinas,
		};
		c.Maestria.DoSave(o.Maestria.ParaSave());
		return c;
	}

	/// <summary>
	/// DEFEITO INJETADO (`--tuffleteste`, o pedido 144): a mente do Tuffle dentro do corpo -- a raca, as tecnicas e as formas continuam as dele,
	/// e a Ascensao do Tsufurujin sobe no poder do outro (o jogo de antes). Lido pelo `Possuir`. Falso em jogo, sempre.
	/// </summary>
	internal static bool TuffleComAMenteDeTeste;

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
		// (2026-10-28, o pedido 144) e a raca, a classe, o livro e as formas dele -- as copias do hospede ficam pra tras, com a ficha copiada
		a.Race = posse.RacaDoTuffle;
		a.Class = posse.ClasseDoTuffle;
		a.Livro = posse.LivroDoTuffle;
		a.Forma = posse.FormaDoTuffle;
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
