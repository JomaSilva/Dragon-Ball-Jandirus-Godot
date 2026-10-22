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
///     regra pura), e quem desafia precisa do ki divino desperto. O que nao vale contra um NPC e a carencia de
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
		};
		c.Sessao = s;
		pl.Combate.Letal = false;   // treino nao mata nem aleija: o golpe letal volta no fim

		RestaurarODivino(npc);
		ParOuDespertar(c, ModoDivino.NaSessao);
		if (BillsEWhis.KitNoTreinoDeTeste && c == _bills) ConsagrarPraLuta(npc, comHakai: true);
		npc.Papel!.PresaDoRoteiro = pl.Id;
		npc.UltimoAgressor = pl.Id;
		VestirADisciplina(c, ligada: true);
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
	/// SEM FORMA, e por isso sem efeito visual: o dono -- "por ele ser um ser da raca anjo o instinto superior
	/// nele n tem efeito visual nenhum, so as passivas". O cabelo prateado e a aura sao das FORMAS do Instinto
	/// (Sign e Perfected), e nenhum dos dois entra nelas: o poder deles e cravado, e nao multiplicado.
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

		// (O DEFEITO INJETADO veste a forma mais alta que a disciplina DELE concede -- perguntada ao dado das
		// disciplinas, e nao a um id escrito aqui: a varredura da bancada pura `disciplinas` reprova o segundo ramo.)
		if (ligada && BillsEWhis.InstintoComFormaDeTeste && c.Quem == QuemFala.Whis
			&& FormaMaisAltaDaDisciplina(tipo) is { Length: > 0 } formaDoMestre && Catalogo.Def(formaDoMestre) != null)
		{
			string antes = npc.Forma.Atual;
			bool estreia = npc.Forma.Entrar(formaDoMestre);
			AplicarForma(npc);
			AnunciarForma(npc, antes, formaDoMestre, estreia);
		}
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

		if (s.Desafio)
		{
			CravarOPoder(npc, BillsEWhis.PoderNoDesafio(MultiplicadorDaForma(npc)));
			return;
		}

		if (s.Parceiro.Combate.Letal) s.Parceiro.Combate.Letal = false;

		CravarOPoder(npc, BillsEWhis.BaseParaOExpresso(
			BillsEWhis.PoderDoTreino(s.Parceiro.Ficha.expressedBP), npc.Ficha.BP, npc.Ficha.expressedBP));

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
	/// ============================ A FURY E DO DESAFIO, E ENTRA POR AQUI ============================
	/// A forma e do kit do Deus da Destruicao (`GameServer.KitDoDeus.cs`, porte de `GodOfDestruction.dm:388-419`: 72x,
	/// aura roxa colada), e a porta dela e uma so, pra jogador e pra corpo sem dono: `EntrarNaFury`. Ela cobra o
	/// TITULO -- e o do Bills nao mora em conta nenhuma (o trono esta vago, e por isso que ele existe). O kit ja
	/// previu o caso: `ConsagrarDeusSemDono` faz deste corpo o Deus da Destruicao sem escrever no trono.
	///
	/// A CONSAGRACAO DURA A LUTA, e nao a vida dele. No desafio o dono o quer inteiro ("100% do poder + forma
	/// fury"): o kit todo, com o Hakai conforme a chave do desafio (<see cref="BillsEWhis.HakaiNoDesafio"/>), e a
	/// Fury por cima. Na Terra ele carrega o mesmo kit SEM a Fury (`ArmarOKitNaTerra`); no treino, nada -- ali
	/// ninguem morre, e o poder dele e o triplo de quem treina, sem surto por cima. Quem desfaz e a
	/// <see cref="EncerrarASessao"/>.
	///
	/// SEM A CENA DA ESTREIA (2,5 s com o corpo preso): a contagem do duelo ja e a pausa, e quem ve recebe a
	/// forma, a aura e a frase do proprio kit. Devolve se ele esta na Fury ao sair daqui.
	/// =========================================================================================
	/// </summary>
	private bool BillsVesteAFury(ServerPlayer bills)
	{
		ConsagrarPraLuta(bills, comHakai: BillsEWhis.OBillsExecuta(noDesafio: true));
		return BillsEWhis.DesafioSemFuryDeTeste || EntrarNaFury(bills, semCena: true);
	}

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

		// TUDO, E COM RAIVA: o poder inteiro e a Fury por cima (ver `BillsEWhis.AFuryMultiplicaOTotal`), e a aura
		// da Destruicao ligada, como em toda luta dele em casa. SEM A FURY NAO HA DESAFIO: ninguem ganha o titulo
		// de um Bills sem forma (a porta do kit recusou -- o motivo sai no log dela).
		VestirADisciplina(c, ligada: true);
		if (!BillsVesteAFury(npc))
		{
			GD.PushError($"[server] o Bills nao entrou na Fury (forma de agora: '{npc.Forma.Atual}') -- o desafio de {pl.Name} nao abriu");
			Avisar(pl, "o desafio não começou: a Fúria do Bills não despertou. Tente de novo daqui a pouco.");
			EncerrarASessao(c, FimDaSessao.MestreSumiu);
			return;
		}
		CravarOPoder(npc, BillsEWhis.PoderNoDesafio(MultiplicadorDaForma(npc)));
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

		if ((p.Pos - c.Posto).Length > BillsEWhis.TilesDoTreino * ZoneCollision.TileSize)
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
			if (npc.Forma.Atual != Catalogo.IdDoPiso(Perfil(npc))) Reverter(npc, "");
			VestirADisciplina(c, ligada: false);
			VoltarAoPosto(c);
			RestaurarODivino(npc);
			CravarOPoder(npc, BillsEWhis.PoderTotal);
			ParOuDespertar(c, ModoDivino.Parado);
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
