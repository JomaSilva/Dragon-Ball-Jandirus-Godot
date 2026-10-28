using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Npc;
using Jandirus.Core.Ranks;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// AS LUTAS DA CASA DO BILLS -- o treino com o Whis, o treino com o Bills e o desafio pelo titulo (dono, 2026-10-11).
///
/// ============================ O FORMATO, QUE O DONO NAO DISSE E AQUI ESTA DECIDIDO ============================
///   * NO LUGAR, com o proprio corpo que estava parado: quem pede acorda o mestre, e os dois lutam ali, na frente
///     de quem estiver vendo. Nao ha arena nem copia -- o que o jogador ve parado e o que luta com ele.
///   * UM CONTRA UM, um aluno por vez por mestre. Quem pede com o mestre ocupado ouve "um de cada vez" (os dois
///     mestres sao independentes: da pra um treinar com o Whis enquanto outro treina com o Bills).
///   * TERCEIRO NAO ENTRA. Quem acerta um mestre no meio da luta de outro ENCERRA a luta, sem resultado: e o que
///     impede derrubar o adversario "de tres vezes o seu poder" com a ajuda de um amigo mais forte -- e, no
///     desafio, o que impede um grupo de coroar alguem.
///   * O TREINO NAO MATA nem aleija: o mestre so derruba (e o padrao de todo corpo sem dono), e o golpe letal de
///     quem treina fica desligado enquanto durar. Quem cai e posto de pe no fim. E a queda no treino nao e
///     derrota: nao paga Zenkai, luto nem karma (ver `AoPerderALuta`).
///   * A SAIDA ESTA SEMPRE A MAO: no meio de uma luta a tecla E de quem luta oferece "parar" (ou "desistir"), a
///     qualquer distancia e na frente de tudo -- o mestre tambem luta de longe (ver <see cref="DivinoAoAlcance"/>).
///     E quem sai ganha uns segundos sem poder ser atingido: o que o mestre ja tinha lancado continua no ar
///     (<see cref="BillsEWhis.SegundosDeFolegoAoFim"/>).
///   * O DESAFIO SEGUE O DUELO PELO TITULO que ja existia entre jogadores (`GameServer.CargoDuelo.cs`): os dois
///     entram inteiros, contagem de cinco segundos, dez minutos de teto, nocaute decide (`Duelo.Passo`, a mesma
///     regra pura), e quem desafia precisa do ki divino desperto (e quem treina tambem, desde 2026-10-25). O que nao vale contra um NPC e a carencia de
///     sete dias e o intervalo de dois: no lugar deles, quem PERDE espera um dia in-game.
///   * NO DESAFIO O BILLS CARREGA O KIT DO DEUS DA DESTRUICAO INTEIRO, com a Fury por cima (dono, 2026-10-11) -- e
///     o Hakai conforme a chave do desafio (<see cref="BillsEWhis.HakaiNoDesafio"/>). Com ela ligada o duelo PODE
///     MATAR: o desafiante que cai no ponto do Hakai e apagado em vez de so perder (o juiz espera a execucao por
///     <see cref="BillsEWhis.SegundosPraExecutar"/> segundos). O TREINO nao tem kit nenhum.
/// =============================================================================================================
///
/// O adversario de poder calculado nao e um terceiro jeito de fazer isso: e o corpo de molde de todo chefe
/// (`NascerNpc`), com o poder cravado pelo `Papel` como o de chefe de saga e a presa imposta como a do lutador
/// de torneio (`PapelDeNpc.PresaDoRoteiro`). O que ha de novo e so o que o dono pediu de novo: o pino ANDA com o
/// poder de quem treina.
/// </summary>
public partial class GameServer
{
	/// <summary>Como uma luta da casa acabou.</summary>
	private enum FimDaSessao : byte
	{
		ParceiroCaiu, MestreCaiu, Tempo, ParceiroParou, ParceiroSaiu, Intrometido, MestreSumiu,

		/// <summary>O desafiante MORREU no duelo (o Hakai): perdeu o desafio e a vida.</summary>
		ParceiroMorreu,
	}

	/// <summary>Uma luta da casa em curso: quem pediu, se e o duelo pelo titulo, e os prazos (relogio do sistema).</summary>
	private sealed class SessaoDivina
	{
		public required ServerPlayer Parceiro;
		public bool Desafio;

		/// <summary>Falso so na contagem do desafio: ninguem bate em ninguem antes do "comecem".</summary>
		public bool Lutando;

		public double LutaEm, Acaba;

		/// <summary>O golpe letal do parceiro como estava antes do treino -- devolvido no fim.</summary>
		public bool LetalAntes;

		public int ContagemDita = int.MaxValue;
		public double ProximaFrase;

		/// <summary>
		/// No desafio: ate quando o juiz espera o Bills executar o desafiante que caiu no ponto do Hakai (relogio do
		/// sistema; zero = ninguem caiu assim). Quem caiu e chegou a esperar ja perdeu o duelo, mesmo que se levante.
		/// </summary>
		public double ExecutaAte;

		/// <summary>(2026-10-24) No treino: o MAIOR poder expresso que o parceiro mostrou nesta aula -- o Whis tem o triplo dele de base, e nao desce.</summary>
		public double PicoDoParceiro;

		/// <summary>(2026-10-24) Quando a luta comecou (relogio do sistema): o tempo de aula que o fim credita ao ensino (`CreditarAAula`).</summary>
		public double AulaComecouEm;

		/// <summary>(2026-10-25) No desafio: o Bills ja virou (se levantou inteiro na Fury) -- a virada e uma por duelo. Ver `VirarNaFury`.</summary>
		public bool Virou;
	}

	/// <summary>Quem perdeu um desafio, e o dia in-game em que pode tentar de novo (pela assinatura do personagem). So memoria.</summary>
	private readonly Dictionary<string, long> _desafiosPerdidos = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>O mestre com quem este corpo esta lutando agora, se algum.</summary>
	private CorpoDivino? MestreDe(ServerPlayer pl) =>
		ReferenceEquals(_whis.Sessao?.Parceiro, pl) ? _whis
		: ReferenceEquals(_bills.Sessao?.Parceiro, pl) ? _bills
		: null;

	private static string TipoDoDivino(CorpoDivino c) =>
		c.Quem == QuemFala.Whis ? BillsEWhis.TipoWhis : BillsEWhis.TipoBills;

	private static TipoDeDisciplina DisciplinaDoDivino(CorpoDivino c) =>
		c.Quem == QuemFala.Whis ? TipoDeDisciplina.UltraInstinct : TipoDeDisciplina.PoderDaDestruicao;

	// =====================================================================
	// 1. O MENU: o servidor aponta, o cliente pede, o servidor confere
	// =====================================================================

	/// <summary>Este corpo esta na caixa da tecla E deste jogador? A MESMA conta por eixo do menu e das obras (`Interacoes.Alcance`).</summary>
	private static bool AoAlcanceDaTeclaE(ServerPlayer pl, ServerPlayer npc) =>
		pl.Zone.Hash == npc.Zone.Hash
		&& Math.Abs(npc.Pos.X - pl.Pos.X) <= Interacoes.Alcance
		&& Math.Abs(npc.Pos.Y - pl.Pos.Y) <= Interacoes.Alcance;

	/// <summary>DEFEITO INJETADO (bancada `--billsteste`): a saida da luta so e apontada a quem esta ENCOSTADO no mestre, como o convite. Falso em jogo, sempre.</summary>
	internal bool SaidaSoDePertoDeTeste;

	/// <summary>
	/// QUAL DOS DOIS A TECLA E DESTE JOGADOR OFERECE, e com que menu. Parado em casa: o convite
	/// (<see cref="BillsEWhis.TipoWhis"/> / <see cref="BillsEWhis.TipoBills"/>), pra quem esta ao alcance -- o mais
	/// perto ganha. Na Terra ninguem tem menu: la a conversa e no soco.
	///
	/// NO MEIO DE UMA LUTA o menu e a SAIDA, so pro parceiro dela -- e A QUALQUER DISTANCIA, na frente de tudo.
	/// Isto foi medido (`--diagbills`, 2026-10-11): o mestre tambem luta de longe (ele atira), ficou a mais de dois
	/// tiles do aluno, e a tecla E de quem queria parar a aula abria o menu do OUTRO, parado ali ao lado. Quem
	/// esta apanhando de alguem com o triplo do poder dele nao pode depender de encostar no adversario pra dizer
	/// "chega".
	/// </summary>
	private (int Corpo, string Tipo, string Nome) DivinoAoAlcance(ServerPlayer pl)
	{
		if (!SaidaSoDePertoDeTeste && MestreDe(pl) is { Corpo: { } mestre, Sessao: { } luta } && mestre.Zone.Hash == pl.Zone.Hash)
			return (mestre.Id, luta.Desafio ? BillsEWhis.TipoEmDesafio : BillsEWhis.TipoEmTreino, mestre.Name);

		(int, string, string) melhor = (0, "", "");
		float perto = float.MaxValue;
		Disputar(_whis);
		Disputar(_bills);
		return melhor;

		void Disputar(CorpoDivino c)
		{
			if (c.Corpo is not { } npc || !AoAlcanceDaTeclaE(pl, npc)) return;

			string tipo =
				c.Sessao is { } s ? (ReferenceEquals(s.Parceiro, pl) ? (s.Desafio ? BillsEWhis.TipoEmDesafio : BillsEWhis.TipoEmTreino) : "")
				: c.Modo == ModoDivino.Parado && _visita.Fase == FaseDaVisita.EmCasa && !_cenaDivina ? TipoDoDivino(c)
				: "";
			if (tipo.Length == 0) return;

			float d = (npc.Pos - pl.Pos).LengthSquared;
			if (d >= perto) return;
			perto = d;
			melhor = (npc.Id, tipo, npc.Name);
		}
	}

	/// <summary>
	/// APONTA A CADA JOGADOR o corpo que a tecla E dele alcanca (5 Hz; so sai pacote quando a resposta muda, e zero
	/// apaga). E o molde do cadaver ao alcance (`S2C.Cadaver`): o snapshot diz onde os corpos estao, e nao qual
	/// deles tem menu.
	///
	/// A PRIMEIRA RESPOSTA DE CADA SESSAO SAI SEMPRE, mesmo sendo "nenhum": o cliente atravessa o relog (e a troca
	/// de servidor) com o ultimo apontado na memoria, e um id velho pode ser o de outro corpo no servidor novo.
	/// </summary>
	private void ApontarODivinoPerto()
	{
		bool haCorpo = _bills.Corpo != null || _whis.Corpo != null;
		int ligados = 0;

		foreach (ServerPlayer pl in _players.Values)
		{
			if (pl.Peer == null) continue;
			ligados++;

			(int corpo, string tipo, string nome) = haCorpo ? DivinoAoAlcance(pl) : (0, "", "");
			// (2026-10-19) O GRANDE PATRIARCA DE NAMEK e o terceiro corpo com menu, pelo mesmo pacote -- ver `GameServer.Patriarca.cs`.
			if (corpo == 0) (corpo, tipo, nome) = PatriarcaAoAlcance(pl);
			// (2026-10-27) E O MESTRE DO YARDRAT, antes do habitante comum -- ver `GameServer.Yardrat.cs`.
			if (corpo == 0) (corpo, tipo, nome) = MestreYardratAoAlcance(pl);
			// (2026-10-21) E O HABITANTE COMUM: o presente, o treino e o recrutamento da amizade -- ver `GameServer.Amizade.cs`.
			if (corpo == 0) (corpo, tipo, nome) = HabitanteAoAlcance(pl);
			if (_divinoApontado.TryGetValue(pl.Id, out (ServerPlayer Quem, int Corpo, string Tipo) visto)
				&& ReferenceEquals(visto.Quem, pl) && visto.Corpo == corpo && visto.Tipo == tipo) continue;

			_divinoApontado[pl.Id] = (pl, corpo, tipo);
			MandarODivinoPerto(pl, corpo, tipo, nome,
				PoderNoFioDeBillsDeTeste && _players.TryGetValue(corpo, out ServerPlayer? vazado) ? vazado.Ficha.BP : 0);
		}

		// QUEM SAIU: a ficha de quem nao esta mais ligado sai da tabela (so ha o que varrer quando sobra alguem).
		if (_divinoApontado.Count > ligados)
			foreach (int id in _divinoApontado.Keys.ToList())
				if (!_players.TryGetValue(id, out ServerPlayer? p) || p.Peer == null || !ReferenceEquals(p, _divinoApontado[id].Quem))
					_divinoApontado.Remove(id);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--billsteste`): o pacote do menu passa a levar o PODER do corpo apontado -- o
	/// vazamento que a regra principal do BP proibe, e que o canario da bancada tem de pegar. Falso em jogo, sempre.
	/// </summary>
	internal static bool PoderNoFioDeBillsDeTeste;

	/// <summary>`S2C.DivinoPerto`: `int corpo` (0 = nenhum) + `string tipo` (a chave do menu) + `string nome`. Nada de poder.</summary>
	private static void MandarODivinoPerto(ServerPlayer pl, int corpo, string tipo, string nome, double poderVazadoDeTeste = 0)
	{
		var w = Protocol.Begin(Protocol.S2C.DivinoPerto);
		w.Put(corpo);
		w.Put(tipo);
		w.Put(nome);
		if (poderVazadoDeTeste > 0) w.Put(poderVazadoDeTeste);
		EscutaDeBills?.Add((pl.Id, w.CopyData()));
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// OS VERBOS DO MENU. Devolve `true` quando o comando era deste canal -- inclusive quando foi RECUSADO.
	/// O cliente so PEDE: lugar, distancia, estado de quem pede e estado do mestre sao conferidos aqui, de novo, na
	/// hora do pedido (o menu aberto nao fecha sozinho quando o jogador se afasta).
	/// </summary>
	private bool ComandoDeBills(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case BillsEWhis.VerboTreinarComWhis: PedirALuta(pl, _whis, desafio: false, cmd); return true;
			case BillsEWhis.VerboTreinarComBills: PedirALuta(pl, _bills, desafio: false, cmd); return true;
			case BillsEWhis.VerboDesafiar: PedirALuta(pl, _bills, desafio: true, cmd); return true;
			case BillsEWhis.VerboParar:
				if (MestreDe(pl) is { } mestre) EncerrarASessao(mestre, FimDaSessao.ParceiroParou);
				else Avisar(pl, "você não está em luta nenhuma com eles.");
				return true;
			default: return false;
		}
	}

	/// <summary>
	/// POR QUE ESTE PEDIDO NAO PASSA (vazio = passa). A ordem e a de quem le: primeiro o estado de quem pede,
	/// depois o lugar, depois a distancia, e so entao o mestre.
	/// </summary>
	private string PorQueNaoPede(ServerPlayer pl, CorpoDivino c, string verbo)
	{
		string nome = c.Quem == QuemFala.Whis ? "o Whis" : "o Bills";

		// ---- quem pede ----
		if (pl.Ficha.dead || pl.Ficha.KO) return "caído, você não chama ninguém pra lutar.";
		if (SemAsRedeas(pl)) return "este corpo não está sob o seu comando.";
		if (NaMente(pl)) return "aqui dentro nada é real: não há com quem lutar.";
		if (MestreDe(pl) != null) return "você já está no meio de uma luta com eles.";
		if (PrensadoPelaGravidade(pl)) return "você mal se aguenta de pé nesta gravidade: não há como lutar assim.";

		// ---- o lugar ----
		if (_visita.Fase != FaseDaVisita.EmCasa || c.Corpo is not { } npc) return $"{nome} não está recebendo ninguém agora.";
		if (pl.Zone.Hash != npc.Zone.Hash || npc.Zone.Hash != c.Zona.Hash) return $"{nome} não está neste mundo.";

		// ---- a distancia ----
		if (!AoAlcanceDaTeclaE(pl, npc)) return $"{npc.Name} está longe demais.";

		// ---- o pedido serve pra ele? (a mesma tabela do menu) ----
		if (!Interacoes.Aceita(TipoDoDivino(c), verbo)) return $"isso não se pede a {npc.Name}.";

		return "";
	}

	/// <summary>
	/// O PEDIDO DE LUTA (treino ou desafio). Passou pela conferencia acima, falta o mestre estar livre -- e, pro
	/// desafio, o que o duelo pelo titulo sempre cobrou de quem desafia.
	/// </summary>
	private void PedirALuta(ServerPlayer pl, CorpoDivino c, bool desafio, string verbo)
	{
		if (!BillsEWhis.MenuSemValidacaoDeTeste && PorQueNaoPede(pl, c, verbo) is { Length: > 0 } recusa)
		{
			Avisar(pl, recusa);
			return;
		}
		if (c.Corpo is not { } npc) return;

		if (c.Modo != ModoDivino.Parado || c.Sessao != null || _cenaDivina)
		{
			DizerDivino(c.Quem, c.Quem == QuemFala.Whis ? BillsEWhis.WhisOcupado : BillsEWhis.BillsOcupado);
			Avisar(pl, $"{npc.Name} está ocupado com outra pessoa. Espere a vez.");
			return;
		}

		// UM DE CADA VEZ, E INTEIRO: a fusao e dois num corpo so (e o duelo pelo titulo sempre a proibiu).
		if (EstaFundido(pl.Id) || pl.NomeDeFusao.Length > 0)
		{
			DizerDivino(c.Quem, BillsEWhis.BillsFundido);
			Avisar(pl, "desfaça a fusão antes: a luta é de um contra um.");
			return;
		}

		// (2026-10-25) O TREINO TAMBEM PEDE O KI DIVINO DESPERTO (o dono: "so de pra treinar com bills e whis caso vc tenha god ki
		// despertado"). O desafio ja pedia, logo abaixo, com a fala dele.
		if (!desafio && pl.Ficha.godki?.awakened != true && !BillsEWhis.TreinoSemKiDivinoDeTeste)
		{
			DizerDivino(c.Quem, c.Quem == QuemFala.Whis ? BillsEWhis.WhisSemKiDivino : BillsEWhis.BillsSemKiDivinoNoTreino);
			Avisar(pl, BillsEWhis.AvisoDoTreinoSemKiDivino);
			return;
		}
		if (!desafio) { ComecarOTreino(pl, c); return; }

		// ---- o desafio pelo titulo ----
		if (pl.Ficha.godki?.awakened != true)
		{
			DizerDivino(QuemFala.Bills, BillsEWhis.BillsSemKiDivino);
			Avisar(pl, "só quem despertou o ki divino pode desafiar um Deus.");
			return;
		}
		if (_desafiosPerdidos.TryGetValue(pl.Assinatura, out long dia) && DiaDoMundo < dia)
		{
			DizerDivino(QuemFala.Bills, BillsEWhis.BillsDesafioCedo);
			Avisar(pl, $"você já o desafiou e perdeu. Ele só aceita de novo daqui a {dia - DiaDoMundo} dia(s).");
			return;
		}
		if (!FuryNoCatalogo)
		{
			Avisar(pl, "o desafio ainda não está aberto neste servidor: a forma Fury do Deus da Destruição não está no catálogo de formas.");
			return;
		}

		ComecarODesafio(pl, c);
	}

	// =====================================================================
	// 2. O TREINO
	// =====================================================================

	private void ComecarOTreino(ServerPlayer pl, CorpoDivino c)
	{
		if (c.Corpo is not { } npc) return;

		var s = new SessaoDivina
		{
			Parceiro = pl,
			Lutando = true,
			Acaba = _relogioDivino + BillsEWhis.SegundosDeTreino,
			LetalAntes = pl.Combate.Letal,
			ProximaFrase = _relogioDivino + 10,
			PicoDoParceiro = pl.Ficha.expressedBP,
			AulaComecouEm = _relogioDivino,
		};
		c.Sessao = s;
		pl.Combate.Letal = false;   // treino nao mata nem aleija: o golpe letal volta no fim

		RestaurarODivino(npc);
		ParOuDespertar(c, ModoDivino.NaSessao);
		if (BillsEWhis.KitNoTreinoDeTeste && c == _bills) ConsagrarPraLuta(npc, comHakai: true);
		npc.Papel!.PresaDoRoteiro = pl.Id;
		npc.UltimoAgressor = pl.Id;
		VestirADisciplina(c, ligada: true);
		if (c == _bills) VestirAFuryDoBillsNoTreino(npc);   // (2026-10-25) no treino o Bills luta na Fury, com o visual -- o dono
		ManterOMestre(c, s);

		bool temADele = (c.Quem == QuemFala.Whis ? pl.UltraInstinct : pl.PoderDaDestruicao).Aprendida;
		DizerDivino(c.Quem, c.Quem == QuemFala.Whis
			? (temADele ? BillsEWhis.WhisComecaComInstinto : BillsEWhis.WhisComecaOTreino)
			: (temADele ? BillsEWhis.BillsComecaComDestruicao : BillsEWhis.BillsComecaOTreino));
		Avisar(pl, $"o treino com {npc.Name} começou: ninguém morre aqui, e quem cair, para. "
				 + "Pra sair antes, aperte [E] (de onde estiver) e escolha \"Parar o treino\".");
		GD.Print($"[server] {pl.Name} comecou a treinar com {npc.Name}");
	}

	/// <summary>
	/// A DISCIPLINA DO MESTRE -- o Ultra Instinto do Whis, o Poder da Destruicao do Bills -- no proprio estado de
	/// disciplina que todo corpo ja tem: aprendida, maestria 100, precisao 100 e a passiva LIGADA. E so isso: as
	/// passivas saem do motor de sempre (`GameServer.Disciplinas.cs`), e o que ele ganhar amanha o mestre ganha
	/// junto.
	///
	/// SEM EFEITO VISUAL no Whis: o dono -- "por ele ser um ser da raca anjo o instinto superior nele n tem efeito visual
	/// nenhum, so as passivas". (2026-10-24) Ele esta SEMPRE no Ultra Instinto Perfeito, e a forma vale -- so que calada
	/// (`VestirAFormaDoWhis`, `ServerPlayer.FormaSemVisual`); o Bills treina na Fury (2026-10-25), com o visual (`VestirAFuryDoBillsNoTreino`).
	/// </summary>
	private void VestirADisciplina(CorpoDivino c, bool ligada)
	{
		if (c.Corpo is not { } npc) return;
		TipoDeDisciplina tipo = DisciplinaDoDivino(c);
		EstadoDeDisciplina est = EstadoDe(npc, tipo);

		est.Aprendida = true;
		est.Real = 100;
		est.Atual = 100;
		est.Ligada = ligada;
		npc.Disciplina = tipo;
		AplicarDisciplina(npc);
	}

	/// <summary>
	/// A forma do ultimo degrau desta disciplina que concede forma ("" se nenhum concede), tirada das faixas dela
	/// (`Disciplinas.Def`): quem sabe que forma e de que escola e o dado, e nao este arquivo.
	/// </summary>
	internal static string FormaMaisAltaDaDisciplina(TipoDeDisciplina tipo) =>
		Disciplinas.Def(tipo)?.Degraus.LastOrDefault(g => g.Forma is { Length: > 0 }).Forma ?? "";

	/// <summary>
	/// O QUE SE REESCREVE NO MESTRE A CADA TIQUE DE UMA LUTA:
	///
	///   * a PRESA: so o parceiro (o canal do torneio);
	///   * no treino, o PODER: o triplo do poder EXPRESSO do parceiro, medido agora (ver
	///     <see cref="BillsEWhis.VezesOPoderDoParceiro"/>) -- e cravado de modo que o expresso DELE seja esse;
	///   * no treino, a DISCIPLINA: "nunca cai a proficiencia dele como e com os players". A precisao drena no
	///     tique das disciplinas (como a de todo mundo) e volta ao cheio aqui, logo depois;
	///   * no treino, o FOLEGO: a esquiva do instinto custa Ki, e um mestre sem Ki pararia de desviar no meio da
	///     aula. Ele nao cansa.
	///
	/// No DESAFIO so a presa e o poder (o trilhao) sao mantidos: ali o Bills luta a serio, com o que tem.
	/// </summary>
	private void ManterOMestre(CorpoDivino c, SessaoDivina s)
	{
		if (c.Corpo is not { } npc) return;
		npc.Papel!.PresaDoRoteiro = s.Parceiro.Id;
		npc.Ficha.KiDeChefe = !BillsEWhis.KiDivinoFinitoDeTeste;   // (2026-10-24) o Ki dos dois nao acaba -- ver `NascerODivino`

		if (s.Desafio)
		{
			CravarOPoder(npc, BillsEWhis.PoderNoDesafio(MultiplicadorDaForma(npc)));
			return;
		}

		if (s.Parceiro.Combate.Letal) s.Parceiro.Combate.Letal = false;

		// (2026-10-25) O PODER DO TREINO, DE NOVO (o dono, depois de o Bills cair pra um Blue com furia): pelo EXPRESSO de AGORA de quem treina
		// -- o Whis sempre 3x, sem teto, no Instinto Perfeito que NAO aumenta o poder dele ("e considerado q esta usando UI mas sem aumentar o
		// bp"); o Bills, na FURY, sempre 2x, ate os 100 tri. O base e o expresso pedido dividido pelo multiplicador da forma em que ele esta: a
		// forma vale (a esquiva do Instinto, a aura da Fury), e o numero que vale na luta e o pedido.
		double expresso = Math.Max(s.Parceiro.Ficha.expressedBP, 1);
		s.PicoDoParceiro = Math.Max(s.PicoDoParceiro, expresso);
		if (c == _whis)
		{
			VestirAFormaDoWhis(npc);
			CravarOPoder(npc, BillsEWhis.PoderDoTreino(expresso) / Math.Max(MultiplicadorDaForma(npc), 1));
		}
		else
		{
			VestirAFuryDoBillsNoTreino(npc);
			CravarOPoder(npc, BillsEWhis.PoderDoBillsNoTreino(expresso) / Math.Max(MultiplicadorDaForma(npc), 1));
		}

		if (!BillsEWhis.InstintoQueCansaDeTeste)
		{
			EstadoDeDisciplina est = EstadoDe(npc, DisciplinaDoDivino(c));
			est.Real = 100;
			est.Atual = 100;
			est.Ligada = true;
			AplicarDisciplina(npc);
		}

		if (npc.Ficha.Ki < npc.Ficha.MaxKi) npc.Ficha.Ki = npc.Ficha.MaxKi;
		if (npc.Ficha.stamina < npc.Ficha.maxstamina) npc.Ficha.stamina = npc.Ficha.maxstamina;
	}

	/// <summary>
	/// "caso o jogador ja tenha a skill do ultra instinto liberada, lutar com o whis acelera a maestria dela" (e o
	/// Bills, a do Poder da Destruicao). E o fator que o `SubirReal` das disciplinas multiplica -- UMA porta: o
	/// segundo de luta, a esquiva e o recuo da aura passam todos por ela. Vale no treino (nao no desafio), e so
	/// pra disciplina do mestre com quem se luta.
	/// </summary>
	private double AceleracaoDoTreinoDivino(ServerPlayer pl, DisciplinaDef def) =>
		!BillsEWhis.TreinoNaoAceleraDeTeste
		&& MestreDe(pl) is { Sessao: { Desafio: false, Lutando: true } } mestre
		&& DisciplinaDoDivino(mestre) == def.Tipo
			? BillsEWhis.AceleracaoDaMaestria : 1;

	/// <summary>
	/// (2026-10-24) O ENSINO DOS PRINCIPIOS -- o dono: *"apos alguns treinos ambos whis ou bills começam a ensinar os principios do UE (bills)
	/// ou do UI (whis) ao qual dps de um tempo vc libera a técnica de ambos podendo comecar a evoluir elas ate liberar as formas delas"*. O
	/// tempo de LUTA de cada aula se acumula na ficha de quem treina (`Fighter.aulaComWhis`/`aulaComBills`, vai pro disco). Passado
	/// <see cref="BillsEWhis.SegundosPraComecarAEnsinar"/>, o mestre comeca a ensinar (uma fala, uma vez); passado
	/// <see cref="BillsEWhis.SegundosPraLiberar"/>, a disciplina dele e APRENDIDA -- pela mesma porta de quem aprende com um mestre jogador
	/// (`PodeAprender`: os dois caminhos se excluem, e o ki divino desperto e pedido; a recusa e dita a cada aula, pra se saber o que falta).
	/// Dali em diante o treino acelera a maestria dela, como sempre (`AceleracaoDoTreinoDivino`), ate as formas.
	/// </summary>
	private void CreditarAAula(ServerPlayer p, CorpoDivino c, double segundos)
	{
		if (BillsEWhis.TreinoNaoEnsinaDeTeste || segundos <= 0) return;
		bool whis = c.Quem == QuemFala.Whis;
		double antes = whis ? p.Ficha.aulaComWhis : p.Ficha.aulaComBills;
		double depois = antes + segundos;
		if (whis) p.Ficha.aulaComWhis = depois;
		else p.Ficha.aulaComBills = depois;

		TipoDeDisciplina tipo = DisciplinaDoDivino(c);
		if (EstadoDe(p, tipo).Aprendida) return;   // ja sabe: daqui em diante o treino so acelera a maestria

		if (antes < BillsEWhis.SegundosPraComecarAEnsinar && depois >= BillsEWhis.SegundosPraComecarAEnsinar)
		{
			DizerDivino(c.Quem, whis ? BillsEWhis.WhisComecaAEnsinar : BillsEWhis.BillsComecaAEnsinar);
			Avisar(p, whis ? BillsEWhis.AvisoDoWhisEnsinando : BillsEWhis.AvisoDoBillsEnsinando);
		}
		if (depois < BillsEWhis.SegundosPraLiberar) return;

		RecusaDeDisciplina r = PodeAprender(p, tipo);
		if (r != RecusaDeDisciplina.Pode)
		{
			Avisar(p, r switch
			{
				RecusaDeDisciplina.OutraDisciplina => BillsEWhis.NaoLiberaPelaOutraDisciplina(whis),
				RecusaDeDisciplina.SemGodKi => BillsEWhis.NaoLiberaSemGodKi(whis, Disciplinas.GodKiParaAprender),
				RecusaDeDisciplina.ClasseRara => BillsEWhis.NaoLiberaClasseRara,
				_ => BillsEWhis.NaoLiberaAinda,
			});
			return;
		}
		EstadoDeDisciplina est = EstadoDe(p, tipo);
		est.Aprendida = true;
		p.Disciplina = tipo;
		AplicarDisciplina(p);
		p.SigAtributos = "";
		DizerDivino(c.Quem, whis ? BillsEWhis.WhisLiberou : BillsEWhis.BillsLiberou);
		Avisar(p, whis ? BillsEWhis.AvisoDoInstintoAprendido : BillsEWhis.AvisoDoEgoAprendido);
		GD.Print($"[server] {p.Name} aprendeu {Disciplinas.Def(tipo)!.Nome} no treino com {(whis ? "o Whis" : "o Bills")}");
	}

	/// <summary>Os dois estao lutando um TREINO da casa agora? (Quem cai nele nao perdeu luta nenhuma -- ver `AoPerderALuta`.)</summary>
	private bool TreinoDivinoEntre(ServerPlayer a, ServerPlayer b)
	{
		if (BillsEWhis.TreinoEhDerrotaDeTeste) return false;
		return Par(a, b) || Par(b, a);

		bool Par(ServerPlayer jogador, ServerPlayer npc) =>
			DivinoDe(npc) is { Sessao: { Desafio: false } s } && ReferenceEquals(s.Parceiro, jogador);
	}

	// =====================================================================
	// 3. O DESAFIO PELO TITULO
	// =====================================================================

	/// <summary>A forma Fury esta no catalogo de formas deste servidor? Sem ela o desafio nao abre.</summary>
	private static bool FuryNoCatalogo => Catalogo.Def(BillsEWhis.IdDaFury) != null;

	/// <summary>
	/// (2026-10-25) O BILLS NA FURY, no treino -- o dono: *"o bills vai usar fury e vai sempre ter 2x o bp do jogador expresso"*. Pela FORMA, e
	/// nao pela porta do titulo (`EntrarNaFury`, que pede o kit do Deus): no treino ele nao tem o kit, e nada de Hakai nem esfera numa aula. A
	/// forma nao tem portao no catalogo (`Catalogo.SoPeloTitulo` so marca a linha), e o 77x dele sai do funil de sempre (`FuryDoBills`). Com o
	/// visual (o anuncio de sempre) e sem cena; quem a tira e o fim da aula (`EncerrarASessao`), como tirava o Ultra Ego.
	/// </summary>
	private void VestirAFuryDoBillsNoTreino(ServerPlayer b)
	{
		if (b.Forma.Atual == BillsEWhis.IdDaFury || !FuryNoCatalogo) return;
		string antes = b.Forma.Atual;
		bool estreia = b.Forma.Entrar(BillsEWhis.IdDaFury);
		AplicarForma(b);
		AnunciarForma(b, antes, BillsEWhis.IdDaFury, estreia, semCena: true);
	}

	/// <summary>
	/// ============================ A FURY E DO DESAFIO, E ENTRA POR AQUI ============================
	/// A forma e do kit do Deus da Destruicao (`GameServer.KitDoDeus.cs`, porte de `GodOfDestruction.dm:388-419`: 72x,
	/// aura roxa colada -- e no corpo DELE 77x, ver <see cref="FuryDoBills"/>), e a porta dela e uma so, pra jogador e pra
	/// corpo sem dono: `EntrarNaFury`. Ela cobra o
	/// TITULO -- e o do Bills nao mora em conta nenhuma (o trono esta vago, e por isso que ele existe). O kit ja
	/// previu o caso: `ConsagrarDeusSemDono` faz deste corpo o Deus da Destruicao sem escrever no trono.
	///
	/// A CONSAGRACAO DURA A LUTA, e nao a vida dele. No desafio o dono o quer inteiro ("100% do poder + forma
	/// fury"): o kit todo, com o Hakai conforme a chave do desafio (<see cref="BillsEWhis.HakaiNoDesafio"/>), e a
	/// Fury por cima -- desde 2026-10-25 na VIRADA do duelo, e nao na largada: ele comeca com o kit e o trilhao, e a
	/// Fury entra quando ele esta perdendo (<see cref="VirarNaFury"/>). Na Terra ele carrega o mesmo kit SEM a Fury
	/// (`ArmarOKitNaTerra`) -- a nao ser que quem o enfrenta passe ate do Ultra Ego dele (`AcompanharNaTerra`); no
	/// treino, nada -- ali ninguem morre. Quem desfaz e a <see cref="EncerrarASessao"/>.
	///
	/// SEM A CENA DA ESTREIA (2,5 s com o corpo preso): a contagem do duelo ja e a pausa, e quem ve recebe a
	/// forma, a aura e a frase do proprio kit. Devolve se ele esta na Fury ao sair daqui.
	/// =========================================================================================
	/// </summary>
	private bool BillsVesteAFury(ServerPlayer bills) => BillsEWhis.DesafioSemFuryDeTeste || EntrarNaFury(bills, semCena: true);

	/// <summary>
	/// ============================ A FURY DO BILLS MULTIPLICA POR 77x (dono, 2026-10-23) ============================
	/// *"bills teria 1 tri mas ao ativar o fury multiplica por 77x"*: o MULTIPLICADOR que a Fury da a este corpo, ou nulo
	/// (o corpo nao e o Bills, ou nao esta na Fury) -- e ai vale a conta de todo mundo. Quem pergunta e o funil unico do
	/// multiplicador de forma (`MultiplicadorDaForma`, o que escreve o `ssjBuff` em todo `AplicarForma`), e nao a entrada
	/// na forma: o `TickDaForma` reaplica a forma a cada tique, e um numero escrito so na entrada voltaria a 72 no seguinte.
	///
	/// POR QUE O MULTIPLICADOR E NAO O BASE: o dono deu as duas metades -- o base e o trilhao (o que o desafio ja cravava)
	/// e a Fury multiplica por 77. Com o numero no funil, o base continua o trilhao e o `ssjBuff` dele e 77; cravar um
	/// base de 1,07 trilhao pra o expresso dar 77 com a forma em 72 daria o mesmo poder com os dois numeros mentindo (o log
	/// do desafio, `x72`, e a ficha dele). O que o jogador le e o mesmo nos dois jeitos: o scouter nao le o Bills (ki
	/// divino), e o Sense de quem tem ki divino mostra a razao do EXPRESSO.
	///
	/// SO O DELE: o corpo e o que este sistema guarda (`_bills`). O jogador com o titulo veste a MESMA forma pela MESMA
	/// porta e fica com os 72x do catalogo (decisao do orquestrador: o dono falou do Bills). O Bills que outra coisa ponha
	/// no mundo pelo molde (uma bancada) nao e este corpo, e tambem nao veste a Fury.
	/// ================================================================================================================
	/// </summary>
	private double? FuryDoBills(ServerPlayer pl) =>
		(ReferenceEquals(_bills.Corpo, pl) || BillsEWhis.FuryDoBillsPraTodosDeTeste)
		&& !BillsEWhis.FuryDoCatalogoDeTeste && pl.Forma.Atual == BillsEWhis.IdDaFury
			? BillsEWhis.MultDaFuryDoBills : null;

	private void ComecarODesafio(ServerPlayer pl, CorpoDivino c)
	{
		if (c.Corpo is not { } npc) return;

		var s = new SessaoDivina
		{
			Parceiro = pl,
			Desafio = true,
			Lutando = false,
			LutaEm = _relogioDivino + Duelo.Contagem / 1000.0,
			LetalAntes = pl.Combate.Letal,
		};
		s.Acaba = s.LutaEm + Duelo.Prazo / 1000.0;
		c.Sessao = s;

		// OS DOIS ENTRAM INTEIROS, como no duelo entre jogadores (`ComecarDuelo`).
		Restaurar(pl);
		RestaurarODivino(npc);
		c.Modo = ModoDivino.NaSessao;        // ainda sem mente: ele so parte pra cima no "comecem"

		// O PODER INTEIRO E O KIT, com a aura da Destruicao ligada, como em toda luta dele em casa -- e a FURY FICA PRA VIRADA (2026-10-25,
		// o dono: *"o bills quando estiver perdendo o desafio pra virar god ele deve recuperar toda a vida e vigor ... e virar a forma
		// fury"*): ele comeca na forma de sempre, com o trilhao, e quem o poe na Fury e o juiz, quando ele estiver perdendo (`VirarNaFury`).
		VestirADisciplina(c, ligada: true);
		ConsagrarPraLuta(npc, comHakai: BillsEWhis.OBillsExecuta(noDesafio: true));
		CravarOPoder(npc, BillsEWhis.PoderTotal);
		SorteioDeNpc.Normalizar(npc.Ficha);

		AnunciarNoMundo(BillsEWhis.Preencher(BillsEWhis.AvisoDoDesafio, nome: NomeVisivel(pl)));
		Avisar(pl, "o duelo começa ao fim da contagem. Pra desistir, aperte [E] (de onde estiver) e escolha "
				 + "\"Desistir do desafio\" -- conta como derrota.");
		double em = 0;
		foreach (Fala f in BillsEWhis.FalasDoDesafio)
		{
			Fala linha = f;
			Agendar(em, () => DizerDivino(linha.Quem, linha.Texto));
			em += 1.6;
		}
		GD.Print($"[server] {pl.Name} DESAFIOU o Bills pelo titulo | Bills BP {npc.Ficha.BP:N0}, expresso {npc.Ficha.expressedBP:N0}, forma {npc.Forma.Atual}");
	}

	// =====================================================================
	// 4. O JUIZ -- a cada tique, pras duas lutas
	// =====================================================================

	private void GuiarAsSessoesDivinas(double dt)
	{
		if (_whis.Sessao != null) Julgar(_whis);
		if (_bills.Sessao != null) Julgar(_bills);
	}

	private void Julgar(CorpoDivino c)
	{
		if (c.Sessao is not { } s || c.Corpo is not { } npc) return;
		ServerPlayer p = s.Parceiro;

		// O PARCEIRO AINDA E O MESMO CORPO DO MUNDO, e esta aqui? (Deslogar ou trocar de mundo larga a luta. MORRER no
		// desafio e perde-lo: ali quem mata e o Hakai, e ele tem a fala dele.)
		bool aqui = _players.TryGetValue(p.Id, out ServerPlayer? vivo) && ReferenceEquals(vivo, p) && p.Zone.Hash == npc.Zone.Hash;
		if (!aqui || p.Ficha.dead)
		{ EncerrarASessao(c, aqui && s.Desafio ? FimDaSessao.ParceiroMorreu : FimDaSessao.ParceiroSaiu); return; }

		// ---- a contagem do desafio ----
		if (!s.Lutando)
		{
			int faltam = (int)Math.Ceiling(s.LutaEm - _relogioDivino);
			if (faltam > 0)
			{
				if (faltam < s.ContagemDita) { s.ContagemDita = faltam; Avisar(p, $"{faltam}..."); }
				return;
			}
			s.Lutando = true;
			ParOuDespertar(c, ModoDivino.NaSessao);
			npc.UltimoAgressor = p.Id;
			Avisar(p, "COMECEM!");
		}

		// ---- o terceiro que se meteu ----
		if (npc.UltimoAgressor != 0 && npc.UltimoAgressor != p.Id)
		{ EncerrarASessao(c, FimDaSessao.Intrometido); return; }

		// ---- quem caiu, quem saiu, o prazo ----
		if (s.Desafio)
		{
			// (2026-10-25) A VIRADA: perdendo (a vida abaixo de 30%, ou no chao), o Bills se levanta inteiro e entra na Fury -- uma vez por duelo
			if (s.Lutando && !s.Virou && !BillsEWhis.DesafioSemViradaDeTeste && (npc.Ficha.KO || npc.Ficha.HP < BillsEWhis.VidaDaVirada))
				VirarNaFury(npc, s);

			// (quem caiu e chegou a esperar a execucao ja perdeu, mesmo que se levante antes de o Bills chegar)
			bool caiu = p.Ficha.KO || s.ExecutaAte > 0;
			FimDeDuelo fim = Duelo.Passo(donoSumiu: false, desafianteSumiu: false,
				donoCaido: npc.Ficha.KO, desafianteCaido: caiu,
				vidaDoDono: npc.Ficha.HP, vidaDoDesafiante: p.Ficha.HP,
				prazoEstourou: _relogioDivino >= s.Acaba);

			if (fim == FimDeDuelo.Desafiante) { EncerrarASessao(c, FimDaSessao.MestreCaiu); return; }
			if (fim == FimDeDuelo.Dono)
			{
				// O KIT: o desafiante esta no chao, NO PONTO DO HAKAI, e o Hakai vale neste duelo -- o juiz espera uns
				// segundos o Bills chegar nele (`Cerebro.Execucao`). Apagado, o duelo acaba pela morte (la em cima);
				// vencido o prazo, ou se o criterio deixar de valer, acaba do jeito de sempre: os dois inteiros.
				if (p.Ficha.KO && EsperaAExecucao(npc, s)) { ManterOMestre(c, s); return; }
				EncerrarASessao(c, caiu ? FimDaSessao.ParceiroCaiu : FimDaSessao.Tempo);
				return;
			}
		}
		else
		{
			if (npc.Ficha.KO) { EncerrarASessao(c, FimDaSessao.MestreCaiu); return; }
			if (p.Ficha.KO) { EncerrarASessao(c, FimDaSessao.ParceiroCaiu); return; }
			if (_relogioDivino >= s.Acaba) { EncerrarASessao(c, FimDaSessao.Tempo); return; }
		}

		// (2026-10-24) O PLANETA INTEIRO E A AREA DA LUTA (o dono: *"deixa o planeta todo ser a area da luta sem terminar ela em 40 tiles"*).
		// Os quarenta tiles do POSTO encerravam a aula no primeiro arremesso longo -- o log do dono: "acabou: ParceiroSaiu". Quem SAI do
		// planeta (outra zona) continua largando a luta, la em cima.
		if (BillsEWhis.AulaComCercaDeTeste && (p.Pos - c.Posto).Length > 40 * ZoneCollision.TileSize)
		{ EncerrarASessao(c, FimDaSessao.ParceiroSaiu); return; }

		ManterOMestre(c, s);
		FalarNoTreino(c, s);
	}

	/// <summary>
	/// O JUIZ ESPERA A EXECUCAO? So enquanto o Hakai PEGA o desafiante caido (a pergunta do proprio verb, que ja
	/// responde nao com o Hakai desligado pra este duelo) e o prazo de <see cref="BillsEWhis.SegundosPraExecutar"/>
	/// nao venceu. O prazo comeca na primeira vez que a resposta e sim.
	/// </summary>
	private bool EsperaAExecucao(ServerPlayer bills, SessaoDivina s)
	{
		if (!OHakaiPega(bills, s.Parceiro)) return false;
		if (s.ExecutaAte <= 0) s.ExecutaAte = _relogioDivino + BillsEWhis.SegundosPraExecutar;
		return _relogioDivino < s.ExecutaAte;
	}

	/// <summary>
	/// (2026-10-25) A VIRADA DO DESAFIO -- ver `BillsEWhis.VidaDaVirada`. Ele se levanta (se caiu), a vida e o vigor voltam cheios (o Ki ja nao
	/// acaba), e a Fury entra por cima do trilhao -- a dele, 77x (`FuryDoBills`). O mundo ouve, e os dois falam.
	/// </summary>
	private void VirarNaFury(ServerPlayer npc, SessaoDivina s)
	{
		s.Virou = true;
		if (npc.Ficha.KO) npc.Combate.Levantar();
		npc.Combate.Curar();
		npc.Combate.SincronizarVida();
		npc.Ficha.stamina = npc.Ficha.maxstamina;
		if (!BillsVesteAFury(npc))
			GD.PushError($"[server] o Bills nao entrou na Fury na virada (forma de agora: '{npc.Forma.Atual}') -- o duelo segue sem ela");
		CravarOPoder(npc, BillsEWhis.PoderNoDesafio(MultiplicadorDaForma(npc)));
		npc.Ficha.PowerLevel();
		AnunciarNoMundo(BillsEWhis.Preencher(BillsEWhis.AvisoDaVirada, nome: NomeVisivel(s.Parceiro)));
		double em = 0;
		foreach (Fala f in BillsEWhis.FalasDaVirada)
		{
			Fala linha = f;
			Agendar(em, () => DizerDivino(linha.Quem, linha.Texto));
			em += 0.8;
		}
		GD.Print($"[server] BILLS VIROU o desafio de {s.Parceiro.Name}: inteiro e na Fury | expresso {npc.Ficha.expressedBP:N0}");
	}

	/// <summary>De vez em quando o mestre comenta a luta de treino -- o Whis, quando desvia; o Bills, por implicancia.</summary>
	private void FalarNoTreino(CorpoDivino c, SessaoDivina s)
	{
		if (s.Desafio || _relogioDivino < s.ProximaFrase) return;
		s.ProximaFrase = _relogioDivino + 14 + _rng.Next(8);
		DizerDivino(c.Quem, Sortear(c.Quem == QuemFala.Whis ? BillsEWhis.WhisDesvia : BillsEWhis.BillsProvoca));
	}

	// =====================================================================
	// 5. O FIM
	// =====================================================================

	/// <summary>Devolve ao parceiro o que a luta mexeu nele (o golpe letal), sem mais nada. Pra quando o mestre some por fora.</summary>
	private void SoltarOParceiro(CorpoDivino c)
	{
		if (c.Sessao is not { } s) return;
		c.Sessao = null;
		if (!s.Desafio && _players.TryGetValue(s.Parceiro.Id, out ServerPlayer? p) && ReferenceEquals(p, s.Parceiro))
			p.Combate.Letal = s.LetalAntes;
	}

	/// <summary>
	/// A LUTA ACABOU. O mestre volta ao posto inteiro, sem forma e sem mente; o parceiro recebe de volta o que a
	/// luta mexeu nele; e o desfecho e dito -- ou, no desafio, COBRADO: o titulo.
	/// </summary>
	private void EncerrarASessao(CorpoDivino c, FimDaSessao fim)
	{
		if (c.Sessao is not { } s) return;
		c.Sessao = null;

		ServerPlayer p = s.Parceiro;
		bool noMundo = _players.TryGetValue(p.Id, out ServerPlayer? vivo) && ReferenceEquals(vivo, p);

		if (!s.Desafio && noMundo) p.Combate.Letal = s.LetalAntes;

		if (c.Corpo is { } npc)
		{
			// O KIT ERA DA LUTA: sem ele a Fury cai na hora, pela regra do proprio kit (ver `BillsVesteAFury`).
			if (EhDeusDaDestruicao(npc)) DesconsagrarDaLuta(npc);
			// o Bills sai da Fury (a do treino, desde 2026-10-25, e a do desafio); o Whis FICA no Instinto Perfeito -- e sempre
			if (c == _bills && npc.Forma.Atual != Catalogo.IdDoPiso(Perfil(npc))) Reverter(npc, "");
			VestirADisciplina(c, ligada: false);
			VoltarAoPosto(c);
			RestaurarODivino(npc);
			// (o poder de REPOUSO de cada um: o trilhao do Bills, os cem trilhoes do Whis -- dono, 2026-10-23)
			CravarOPoder(npc, BillsEWhis.PoderDeRepouso(c.Quem));
			ParOuDespertar(c, ModoDivino.Parado);
			if (c == _whis) VestirAFormaDoWhis(npc);
		}

		// QUEM CAIU NA LUTA E POSTO DE PE (o `trn_heal` do torneio): e treino, e o duelo tambem devolve os dois inteiros.
		if (noMundo && !p.Ficha.dead && (p.Ficha.KO || s.Desafio)) CurarPraLuta(p);

		// E GANHA O FOLEGO DE QUEM ACABOU DE SE LEVANTAR: o mestre parou, mas o que ele ja tinha lancado ainda esta no
		// ar -- ver `BillsEWhis.SegundosDeFolegoAoFim`.
		if (noMundo && !p.Ficha.dead && !BillsEWhis.FimSemFolegoDeTeste)
			p.Combate.Carencia = Math.Max(p.Combate.Carencia, BillsEWhis.SegundosDeFolegoAoFim);

		string nome = noMundo ? NomeVisivel(p) : p.Name;
		GD.Print($"[server] a luta de {p.Name} com {(c.Quem == QuemFala.Whis ? "Whis" : "Bills")} acabou: {fim}"
			   + (s.Desafio ? " (DESAFIO)" : ""));

		if (!s.Desafio)
		{
			bool whis = c.Quem == QuemFala.Whis;
			string fala = fim switch
			{
				FimDaSessao.ParceiroCaiu => whis ? BillsEWhis.WhisDerrubouOAluno : BillsEWhis.BillsDerrubouOAluno,
				FimDaSessao.MestreCaiu => whis ? BillsEWhis.WhisFoiDerrubado : BillsEWhis.BillsFoiDerrubadoNoTreino,
				FimDaSessao.Tempo => whis ? BillsEWhis.WhisAcabouOTempo : BillsEWhis.BillsAcabouOTempo,
				FimDaSessao.ParceiroParou => whis ? BillsEWhis.WhisAlunoParou : BillsEWhis.BillsAlunoParou,
				FimDaSessao.ParceiroSaiu => whis ? BillsEWhis.WhisAlunoFugiu : BillsEWhis.BillsAlunoFugiu,
				FimDaSessao.Intrometido => whis ? BillsEWhis.WhisIntrometido : BillsEWhis.BillsIntrometido,
				_ => "",
			};
			if (fala.Length > 0) DizerDivino(c.Quem, fala);
			if (noMundo) Avisar(p, fim == FimDaSessao.Intrometido
				? "o treino acabou: alguém de fora acertou o seu mestre, e a aula é de um aluno só."
				: "o treino acabou.");
			// (2026-10-24) O TEMPO DE LUTA DA AULA VIRA ENSINO. Ver `CreditarAAula`. (2026-10-25) So o tempo: derrubar o mestre valia uma aula
			// inteira a mais, e o dono pediu o ensino pelo tempo total de treino ("vc aprende UE vencendo o bills").
			if (noMundo && !p.Ficha.dead)
				CreditarAAula(p, c, s.Lutando ? _relogioDivino - s.AulaComecouEm : 0);
			return;
		}

		// ---- o desafio ----
		switch (fim)
		{
			case FimDaSessao.MestreCaiu:
				CoroarQuemVenceuOBills(p);
				break;

			case FimDaSessao.Intrometido:
				DizerDivino(QuemFala.Bills, BillsEWhis.BillsDesafioAnulado);
				AnunciarNoMundo(BillsEWhis.Preencher(BillsEWhis.AvisoDoDesafioAnulado, nome: nome));
				break;

			case FimDaSessao.MestreSumiu:
				if (noMundo) Avisar(p, "o desafio se desfez, sem vencedor.");
				break;

			default:
				// QUEM PERDE ESPERA: um dia in-game ate poder desafiar de novo (so ele).
				if (p.Assinatura.Length > 0)
					_desafiosPerdidos[p.Assinatura] = DiaDoMundo + BillsEWhis.DiasEntreDesafiosDoMesmo;
				DizerDivino(QuemFala.Bills, fim switch
				{
					FimDaSessao.Tempo => BillsEWhis.BillsAcabouOPrazo,
					FimDaSessao.ParceiroCaiu => BillsEWhis.BillsVenceuODesafio,
					FimDaSessao.ParceiroMorreu => BillsEWhis.BillsApagouODesafiante,
					_ => BillsEWhis.BillsDesafianteSumiu,
				});
				AnunciarNoMundo(BillsEWhis.Preencher(BillsEWhis.AvisoDoTituloDefendido, nome: nome));
				break;
		}
	}

	/// <summary>
	/// O BILLS CAIU NO DUELO: o titulo muda de mao AGORA, pela mesma porta do duelo entre jogadores
	/// (<see cref="Entronizar"/> -- que grava o trono, entrega o kit e o traje pela `ReconciliarDadiva`, e
	/// anuncia). Com o trono ocupado o Bills deixa de existir (<see cref="BillsEWhis.Existe"/>): ele se despede e
	/// some quando a ultima linha acabar. O relogio do titulo novo (carencia, tarefas) quem arma e o
	/// `TickDoDuelo`, como arma pra todo titulo que chega por fora do duelo de jogadores.
	/// </summary>
	private void CoroarQuemVenceuOBills(ServerPlayer novoDeus)
	{
		if (Cargos.Get(BillsEWhis.CargoDoBills) is not { } cargo) return;

		_cenaDivina = true;
		ParOuDespertar(_bills, ModoDivino.EmCena);

		Entronizar(cargo, novoDeus, BillsEWhis.Preencher(BillsEWhis.ViaDoTitulo, nome: NomeVisivel(novoDeus)));

		double em = 1.5;
		foreach (Fala f in BillsEWhis.FalasDaCoroacao)
		{
			Fala linha = f;
			Agendar(em, () => DizerDivino(linha.Quem, linha.Texto));
			em += BillsEWhis.SegundosEntreFalas;
		}
		Agendar(em, () =>
		{
			_cenaDivina = false;
			GarantirOsDivinos();   // o trono tem dono: o Bills sai agora
		});
	}
}
