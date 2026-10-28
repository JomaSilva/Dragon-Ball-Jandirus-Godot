using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (2026-10-21) A AMIZADE COM OS HABITANTES -- o presente, o treino e o recrutamento, pela tecla E. A regra pura (o medidor, o valor de
/// cada presente, o retorno decrescente, quem entra) mora em `Core/Npc/Amizade.cs`; o aliado que sai daqui, em `GameServer.Aliados.cs`.
///
/// ============================ O MENU E, PELO MESMO CANAL DO BILLS E DO GURU ============================
/// O habitante e um CORPO, e nao uma obra: o apontador de 5 Hz (`ApontarODivinoPerto`) o aponta pelo `S2C.DivinoPerto` depois do Whis,
/// do Bills e do Guru, com um dos tres menus (`Amizade.TipoDoHabitante`, `TipoDoAmigo` com o "Recrutar", `TipoEmTreino` com a saida).
/// Quem diz qual e o servidor -- o laco e dele --, e cada verbo confere de novo, na hora, quem esta ao alcance.
///
/// ============================ O TREINO E O MOLDE DO TREINO COM O BILLS E O WHIS ============================
/// No lugar, com o corpo do proprio habitante (o ramo `GuiarOTreinoDeAmizade` do `TicarUmCorpo` lhe da a presa e cala os especiais,
/// como o spar entre habitantes); o golpe letal de quem treina fica desligado; quem se mete encerra; quem cai e posto de pe; os dois ganham
/// uns segundos sem poder ser atingidos no fim; e a queda nao e derrota (`AoPerderALuta` sai pelo `TreinoDeAmizadeEntre`, como sai pelo
/// `TreinoDivinoEntre`). O que e daqui: acaba na METADE da vida de um dos dois (ou em um minuto), e o habitante recusa o poder desigual.
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 1. O LACO -- por habitante e por personagem, so na memoria
	// =====================================================================
	/// <summary>
	/// Os lacos: id do corpo do habitante -> assinatura do personagem -> o laco. SO MEMORIA, e de proposito: ver `LacoDeAmizade`. O que
	/// morre ou sai do mundo e podado a 1 Hz (`PodarAsAmizades`); quem bate de verdade perde o seu na hora (`AmizadeAoApanhar`).
	/// </summary>
	private readonly Dictionary<int, Dictionary<string, LacoDeAmizade>> _amizades = [];

	private LacoDeAmizade? LacoDe(ServerPlayer npc, ServerPlayer pl) =>
		pl.Assinatura.Length > 0 && _amizades.TryGetValue(npc.Id, out Dictionary<string, LacoDeAmizade>? lacos)
		&& lacos.TryGetValue(pl.Assinatura, out LacoDeAmizade? l) ? l : null;

	/// <summary>O laco deste par, criado se nao havia. Nulo pra quem nao tem assinatura (corpo sem conta nao e lembrado por ninguem).</summary>
	private LacoDeAmizade? LacoNovo(ServerPlayer npc, ServerPlayer pl)
	{
		if (pl.Assinatura.Length == 0) return null;
		if (!_amizades.TryGetValue(npc.Id, out Dictionary<string, LacoDeAmizade>? lacos))
			_amizades[npc.Id] = lacos = new Dictionary<string, LacoDeAmizade>(StringComparer.Ordinal);
		if (!lacos.TryGetValue(pl.Assinatura, out LacoDeAmizade? l)) lacos[pl.Assinatura] = l = new LacoDeAmizade();
		return l;
	}

	/// <summary>
	/// ESTE CORPO E UM HABITANTE COM QUEM SE FAZ AMIZADE? Do mundo (molde, sem dono), pacifico, de um molde que entra
	/// (`Amizade.MoldeRecrutavel`: so o comum), vivo, sem posse, sem ser copia de ninguem -- e ainda nao e aliado de alguem.
	/// </summary>
	private bool HabitanteDeAmizade(ServerPlayer o) =>
		EhNpcDoMundo(o) && o.Papel is { Pacifico: true } papel && Amizade.MoldeRecrutavel(papel.Molde.Id)
		&& o.CerebroDaPosse == null && o.DonoDoClone == 0 && !o.Ficha.dead && !_companheiros.ContainsKey(o.Id);

	/// <summary>O habitante que a tecla E deste jogador alcanca agora: o mais perto, de pe, na caixa de dois tiles por eixo.</summary>
	private ServerPlayer? HabitanteDaConversa(ServerPlayer pl)
	{
		ServerPlayer? melhor = null;
		float perto = float.MaxValue;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
		{
			if (o == pl || o.Ficha.KO || !AoAlcanceDaTeclaE(pl, o) || !HabitanteDeAmizade(o)) continue;
			float d = (o.Pos - pl.Pos).LengthSquared;
			if (d >= perto) continue;
			perto = d;
			melhor = o;
		}
		return melhor;
	}

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): o apontador nao enxerga o habitante -- a tecla E perto dele nao abre nada.</summary>
	internal static bool HabitanteSemMenuDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--aliadosteste`): o clique no menu aberto nao da a folga de um passo. O habitante que
	/// andou entre o menu abrir e o botao chega "nao ha ninguem ao lado".
	/// </summary>
	internal static bool MenuAbertoSemFolgaDeTeste;

	/// <summary>
	/// Quem o clique da tecla E acha. Primeiro o que esta no alcance de sempre. Se ninguem, o corpo que o apontador
	/// mostrou, ainda dentro da <see cref="Amizade.FolgaDoMenuAberto"/>: o menu ja estava aberto e ele deu um passo.
	/// </summary>
	private ServerPlayer? HabitanteDoClique(ServerPlayer pl)
	{
		if (HabitanteDaConversa(pl) is { } perto) return perto;
		if (MenuAbertoSemFolgaDeTeste) return null;
		if (!_divinoApontado.TryGetValue(pl.Id, out (ServerPlayer Quem, int Corpo, string Tipo) visto)
			|| !ReferenceEquals(visto.Quem, pl)) return null;
		if (visto.Tipo is not (Amizade.TipoDoHabitante or Amizade.TipoDoAmigo)) return null;
		if (!_players.TryGetValue(visto.Corpo, out ServerPlayer? npc) || npc.Ficha.KO || !HabitanteDeAmizade(npc)) return null;
		float folga = Interacoes.Alcance + Amizade.FolgaDoMenuAberto;
		return npc.Zone.Hash == pl.Zone.Hash
			&& Math.Abs(npc.Pos.X - pl.Pos.X) <= folga
			&& Math.Abs(npc.Pos.Y - pl.Pos.Y) <= folga ? npc : null;
	}

	/// <summary>
	/// O QUE O APONTADOR DA TECLA E OFERECE DESTE LADO (`ApontarODivinoPerto`): no meio de um treino, a SAIDA, a qualquer distancia (o
	/// parceiro tambem luta de longe -- o argumento da saida do treino com o Bills); fora dele, o habitante ao alcance, com o menu do laco.
	/// </summary>
	private (int Corpo, string Tipo, string Nome) HabitanteAoAlcance(ServerPlayer pl)
	{
		if (TreinoDeAmizadeDe(pl) is { } t) return (t.Npc.Id, Amizade.TipoEmTreino, t.Npc.Name);
		if (HabitanteSemMenuDeTeste || HabitanteDaConversa(pl) is not { } npc) return (0, "", "");
		return (npc.Id, Amizade.Cheio(LacoDe(npc, pl)) ? Amizade.TipoDoAmigo : Amizade.TipoDoHabitante, npc.Name);
	}

	/// <summary>Por que este personagem nao conversa com ninguem agora (nulo = conversa).</summary>
	private string? PorQueNaoConversa(ServerPlayer pl)
	{
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) return "caído, você não conversa com ninguém.";
		if (SemAsRedeas(pl)) return "este corpo não está sob o seu comando.";
		if (NaMente(pl)) return "aqui dentro nada é real: não há com quem fazer amizade.";
		if (pl.CorpoDaPosse != null) return "você está no corpo de outra pessoa: ninguém aqui conhece você.";
		if (pl.Assinatura.Length == 0) return "este corpo não tem identidade própria.";
		return null;
	}

	// =====================================================================
	// 2. OS VERBOS -- os da tecla E (amizade_*) e os da aba Allies (aliado_*)
	// =====================================================================
	/// <summary>
	/// O canal da amizade e dos aliados. Devolve `true` quando o comando era deste canal -- inclusive quando foi RECUSADO (senao o funil
	/// segue e o jogador le "comando desconhecido" depois de ja ter lido o porque).
	/// </summary>
	private bool ComandoDeAliados(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case Amizade.VerboPresente: DarPresente(pl, arg); return true;
			case Amizade.VerboTreinar: PedirOTreinoDeAmizade(pl); return true;
			case Amizade.VerboParar:
				if (TreinoDeAmizadeDe(pl) is { } t) EncerrarOTreinoDeAmizade(t, FimDoTreinoDeAmizade.Parou);
				else Avisar(pl, "você não está treinando com ninguém.");
				return true;
			case Amizade.VerboRecrutar: Recrutar(pl); return true;
			default: return OrdemAoAliado(pl, cmd, arg);
		}
	}

	// =====================================================================
	// 3. O PRESENTE
	// =====================================================================
	/// <summary>Um item da mochila que da pra dar, o que ele vale e o rotulo do botao ("Maçã (+3)").</summary>
	private readonly record struct PresenteNaMochila(string Id, ItemDef Def, double Valor, string Rotulo);

	/// <summary>
	/// O QUE ESTE PERSONAGEM PODE DAR, uma linha por item (pilhas iguais nao repetem), com o que cada um renderia AGORA (o retorno
	/// decrescente do dia ja contado) no rotulo -- e o rotulo que a pagina de escolhas mostra e devolve como argumento.
	/// </summary>
	private List<PresenteNaMochila> PresentesDe(ServerPlayer pl, LacoDeAmizade? l)
	{
		int hoje = l == null || l.Dia != DiaDoMundo ? 0 : l.PresentesHoje;
		var lista = new List<PresenteNaMochila>();
		var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (Pilha p in pl.Mochila.Pilhas)
		{
			if (p.Quantidade <= 0 || !vistos.Add(p.Id)) continue;
			ItemDef? def = CatalogoDeItens.Get(p.Id);
			double custo = CatalogoDeItens.Obras?.Get(p.Id)?.Custo ?? 0;
			if (def == null || !Amizade.DaPraDar(p.Id, def, custo)) continue;
			double valor = Amizade.ValorDoPresente(p.Id, def, custo);
			lista.Add(new PresenteNaMochila(p.Id, def, valor, $"{def.Nome} (+{Amizade.Ganho(valor, hoje):0.#})"));
		}
		return lista;
	}

	/// <summary>
	/// "DAR PRESENTE" (`Forma.Lista`): sem argumento, a pergunta -- o servidor responde a lista do que da pra dar (`S2C.Escolhas`); com
	/// ela, o item escolhido sai da mochila e o laco sobe pelo valor dele, com o retorno decrescente do dia.
	/// </summary>
	private void DarPresente(ServerPlayer pl, string arg)
	{
		string? recusa = PorQueNaoConversa(pl);
		ServerPlayer? npc = recusa == null ? HabitanteDoClique(pl) : null;
		if (npc == null)
		{
			Avisar(pl, recusa ?? "não há nenhum habitante ao seu lado pra receber um presente.");
			if (arg.Length == 0) MandarEscolhas(pl, Amizade.VerboPresente, []);   // a pagina nao fica "consultando..." pra sempre
			return;
		}
		LacoDeAmizade? laco = LacoDe(npc, pl);
		List<PresenteNaMochila> presentes = PresentesDe(pl, laco);
		if (arg.Length == 0)
		{
			MandarEscolhas(pl, Amizade.VerboPresente, presentes.Select(x => x.Rotulo));
			if (presentes.Count == 0) Avisar(pl, "nada na sua mochila serve de presente: comida, Senzu e o que tem preço de fábrica servem.");
			return;
		}

		// PELO ROTULO, e se o retorno do dia mudou entre a lista e o clique (outro presente no meio), pelo nome antes do "(+".
		PresenteNaMochila? escolhido = null;
		foreach (PresenteNaMochila x in presentes) if (x.Rotulo == arg) { escolhido = x; break; }
		if (escolhido == null)
			foreach (PresenteNaMochila x in presentes) if (arg.StartsWith(x.Def.Nome + " (+", StringComparison.Ordinal)) { escolhido = x; break; }
		if (escolhido is not { } dado) { Avisar(pl, "esse item não está na sua mochila (ou não é coisa que se dê de presente)."); return; }
		if (TreinoDeAmizadeDe(npc) != null) { Avisar(pl, $"{npc.Name} está no meio de um treino: espere acabar."); return; }

		if (pl.Mochila.Tirar(dado.Id) <= 0) { Avisar(pl, "esse item não está na sua mochila."); return; }
		MandarMochila(pl);

		LacoDeAmizade l = LacoNovo(npc, pl)!;
		Amizade.VirarODia(l, DiaDoMundo);
		bool estavaCheio = Amizade.Cheio(l);
		double ganho = Amizade.Somar(l, Amizade.Ganho(dado.Valor, l.PresentesHoje));
		l.PresentesHoje++;

		Falar(npc, Protocol.Fala.Diz, Sortear(dado.Valor >= Amizade.PresenteGrande ? Amizade.FalasDoPresenteGrande : Amizade.FalasDoPresente));
		Avisar(pl, $"{npc.Name} aceita: {dado.Def.Nome}. Amizade {l.Pontos:0}/{Amizade.Cheia:0} (+{ganho:0.#}).");
		if (!estavaCheio && Amizade.Cheio(l)) AvisarQueConfia(pl, npc);
		GD.Print($"[server] amizade: {pl.Name} deu {dado.Id} a {npc.Name} -> {l.Pontos:0.#} (+{ganho:0.##})");
		MandarAliados(pl);
	}

	/// <summary>O laco acabou de encher: a frase que ensina o resto.</summary>
	private void AvisarQueConfia(ServerPlayer pl, ServerPlayer npc)
	{
		Avisar(pl, $"{npc.Name} confia em você agora: dá pra recrutá-lo (tecla E perto dele: \"{Amizade.RotuloRecrutar}\").");
		// O apontador da tecla E so sai de 5 em 5 Hz, e o menu aberto congela o tipo antigo. Sem isto, com o laco
		// acabando de encher, o E ainda mostra "Dar presente / Treinar" e o Recrutar nao aparece.
		ApontarODivinoPerto();
	}

	// =====================================================================
	// 4. O TREINO -- o molde do treino com o Bills e o Whis
	// =====================================================================
	/// <summary>Como um treino de amizade acabou. Os tres primeiros sao o fim que vale amizade.</summary>
	internal enum FimDoTreinoDeAmizade : byte { Tempo, JogadorCedeu, HabitanteCedeu, Parou, Saiu, Sumiu, Intrometido }

	/// <summary>Um treino em curso: quem, ate quando (relogio dos aliados), e o golpe letal de quem pediu, devolvido no fim.</summary>
	private sealed class TreinoDeAmizade
	{
		public required ServerPlayer Jogador, Npc;
		public double Ate;
		public bool LetalAntes;
		/// <summary>(2026-10-27, o pedido 124b) Desde quando (relogio dos aliados) os dois estao longe demais; -1 = perto.</summary>
		public double LongeDesde = -1;
	}

	/// <summary>Os treinos em curso, pelo id do HABITANTE (o ramo dele no `TicarUmCorpo` pergunta por aqui).</summary>
	private readonly Dictionary<int, TreinoDeAmizade> _treinosDeAmizade = [];

	/// <summary>O relogio dos treinos e da volta de 1 Hz, em segundos de servidor (anda no `TickDosAliados`).</summary>
	private double _relogioDosAliados;

	/// <summary>Ate quando (relogio dos aliados) o par que acabou de treinar ainda esta no folego do fim: o golpe atrasado nao e agressao.</summary>
	private readonly Dictionary<(int Jogador, int Npc), double> _folegoDoTreino = [];

	/// <summary>O treino em que este corpo (jogador ou habitante) esta, se algum.</summary>
	private TreinoDeAmizade? TreinoDeAmizadeDe(ServerPlayer quem)
	{
		if (_treinosDeAmizade.TryGetValue(quem.Id, out TreinoDeAmizade? comoHabitante) && ReferenceEquals(comoHabitante.Npc, quem)) return comoHabitante;
		foreach (TreinoDeAmizade t in _treinosDeAmizade.Values) if (ReferenceEquals(t.Jogador, quem)) return t;
		return null;
	}

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): a queda no treino volta a ser derrota -- Zenkai, luto e o resto do funil.</summary>
	internal static bool TreinoDeAmizadeEhDerrotaDeTeste;

	/// <summary>Os dois estao num treino de amizade (um com o outro)? Quem cai nele nao perdeu luta nenhuma -- ver `AoPerderALuta`.</summary>
	private bool TreinoDeAmizadeEntre(ServerPlayer a, ServerPlayer b)
	{
		if (TreinoDeAmizadeEhDerrotaDeTeste) return false;
		return Par(a, b) || Par(b, a);

		bool Par(ServerPlayer npc, ServerPlayer jogador) =>
			_treinosDeAmizade.TryGetValue(npc.Id, out TreinoDeAmizade? t) && ReferenceEquals(t.Npc, npc) && ReferenceEquals(t.Jogador, jogador);
	}

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): o habitante aceita treinar com qualquer poder -- o esmagado e o que esmaga.</summary>
	internal static bool TreinoSemMedoDeTeste;

	/// <summary>"TREINAR": o pedido. Confere quem pede, o habitante ao alcance, se ele esta livre e se o poder dos dois e parecido.</summary>
	private void PedirOTreinoDeAmizade(ServerPlayer pl)
	{
		if (PorQueNaoConversa(pl) is { } recusa) { Avisar(pl, recusa); return; }
		if (TreinoDeAmizadeDe(pl) != null) { Avisar(pl, "você já está treinando com alguém."); return; }
		if (MestreDe(pl) != null) { Avisar(pl, "você já está no meio de uma luta com eles."); return; }
		if (EstaFundido(pl.Id) || pl.NomeDeFusao.Length > 0) { Avisar(pl, "desfaça a fusão antes: o treino é de um contra um."); return; }
		if (PrensadoPelaGravidade(pl)) { Avisar(pl, "você mal se aguenta de pé nesta gravidade: não há como treinar assim."); return; }
		if (HabitanteDoClique(pl) is not { } npc) { Avisar(pl, "não há nenhum habitante ao seu lado pra treinar."); return; }

		if (TreinoDeAmizadeDe(npc) != null || npc.Rotina is { Afazer: Jandirus.Core.Ai.Afazer.Sparring } || PresaDoNpc(npc) != null)
		{
			Falar(npc, Protocol.Fala.Diz, "Agora não, estou ocupado.");
			Avisar(pl, $"{npc.Name} está ocupado agora.");
			return;
		}

		// QUEM JA ESTA NA METADE DA VIDA NAO COMECA: o treino acabaria no primeiro tique, e pagaria amizade por um treino que nao houve.
		if (pl.Ficha.HP / 100.0 <= Amizade.VidaQueEncerraOTreino) { Avisar(pl, "você está ferido demais pra treinar: descanse antes."); return; }
		if (npc.Ficha.HP / 100.0 <= Amizade.VidaQueEncerraOTreino)
		{
			Falar(npc, Protocol.Fala.Diz, "Hoje não... ainda estou me recuperando.");
			Avisar(pl, $"{npc.Name} está ferido demais pra treinar agora.");
			return;
		}

		// A "INTELIGENCIA MINIMA" DO SPAR ENTRE HABITANTES (`Rotina.Convite`), com o limiar do `rotina.json`: ninguem treina com quem o
		// esmagaria, nem com quem ele esmagaria. Pelo poder EXPRESSO -- quem baixa o proprio poder (Power Down) consegue o treino.
		double razao = RazaoDePoder(pl, npc);
		if (!TreinoSemMedoDeTeste && Amizade.PoderDesigual(razao, _rotina.RazaoDePoderQueAssusta))
		{
			Falar(npc, Protocol.Fala.Diz, Sortear(razao > 1 ? Amizade.FalasDeForteDemais : Amizade.FalasDeFracoDemais));
			Avisar(pl, $"{npc.Name} recusa: o poder de vocês é desigual demais pra um treino (ninguém treina com quem o esmagaria).");
			return;
		}

		var t = new TreinoDeAmizade
		{
			Jogador = pl, Npc = npc,
			Ate = _relogioDosAliados + Amizade.SegundosDoTreino,
			LetalAntes = pl.Combate.Letal,
		};
		_treinosDeAmizade[npc.Id] = t;
		pl.Combate.Letal = false;   // o treino nao mata nem aleija: o golpe letal volta no fim
		npc.Combate.Letal = false;
		npc.Rotina?.Interromper("treino com um jogador");
		npc.Ficha.train = false;
		// A MARCA DE QUEM E O PARCEIRO, como no treino com o Bills: quem mais bater nele vira o `UltimoAgressor` e o treino acaba
		// ("Intrometido"). O rancor zera -- e o ramo do treino, e nao o rancor, que lhe da a presa.
		npc.UltimoAgressor = pl.Id;
		npc.RancorAte = 0;

		Falar(npc, Protocol.Fala.Diz, Sortear(Amizade.FalasDoTreinoAceito));
		Avisar(pl, $"o treino com {npc.Name} começou: ninguém se machuca de verdade, e acaba quando um dos dois chegar à metade da vida "
				 + $"(ou em {Amizade.SegundosDoTreino:0} s). Pra parar antes: [E] e \"{Amizade.RotuloParar}\".");
		GD.Print($"[server] amizade: {pl.Name} comecou a treinar com {npc.Name} (poder {razao:0.00}x)");
		MandarAliados(pl);
	}

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): o treino nao cala os especiais do habitante -- ele luta como numa briga de verdade.</summary>
	internal static bool TreinoComEspeciaisDeTeste;

	/// <summary>
	/// O RAMO DO HABITANTE EM TREINO no `TicarUmCorpo` (`GameServer.Clone.cs`): a presa e o parceiro, e o cerebro SABE que e treino
	/// (`Cerebro.LutaDeTreino`: sem golpe especial, como no spar entre habitantes). Caido, ele nao age. Falso = nao pensa neste tique.
	/// </summary>
	private bool GuiarOTreinoDeAmizade(ServerPlayer npc, TreinoDeAmizade t, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = npc.Pos;
		if (npc.Ficha.dead || npc.Ficha.KO) { npc.Moving = false; return false; }
		presa = t.Jogador;
		destino = t.Jogador.Pos;
		if (npc.Ficha.train) npc.Ficha.train = false;
		if (!TreinoComEspeciaisDeTeste && npc.Cerebro is { } cerebro) cerebro.LutaDeTreino = true;
		return true;
	}

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): o juiz nao olha a vida -- o treino so acaba no tempo (ou caido).</summary>
	internal static bool TreinoSemMetadeDeTeste;

	/// <summary>
	/// O JUIZ, a cada tique: o parceiro ainda esta aqui (e vivo)? Alguem de fora bateu? Algum dos dois chegou a metade da vida (ou caiu)?
	/// O minuto passou? Alguem se afastou? E o golpe letal de quem treina continua desligado.
	/// </summary>
	private void JulgarOTreinoDeAmizade(TreinoDeAmizade t)
	{
		ServerPlayer p = t.Jogador, n = t.Npc;
		bool npcAqui = _players.TryGetValue(n.Id, out ServerPlayer? vn) && ReferenceEquals(vn, n) && !n.Ficha.dead;
		bool jogadorAqui = _players.TryGetValue(p.Id, out ServerPlayer? vp) && ReferenceEquals(vp, p) && !p.Ficha.dead && p.Zone.Hash == n.Zone.Hash;
		if (!npcAqui) { EncerrarOTreinoDeAmizade(t, FimDoTreinoDeAmizade.Sumiu); return; }
		if (!jogadorAqui) { EncerrarOTreinoDeAmizade(t, FimDoTreinoDeAmizade.Saiu); return; }
		if (n.UltimoAgressor != 0 && n.UltimoAgressor != p.Id) { EncerrarOTreinoDeAmizade(t, FimDoTreinoDeAmizade.Intrometido); return; }

		bool metade = !TreinoSemMetadeDeTeste;
		if (p.Ficha.KO || (metade && p.Ficha.HP / 100.0 <= Amizade.VidaQueEncerraOTreino))
		{ EncerrarOTreinoDeAmizade(t, FimDoTreinoDeAmizade.JogadorCedeu); return; }
		if (n.Ficha.KO || (metade && n.Ficha.HP / 100.0 <= Amizade.VidaQueEncerraOTreino))
		{ EncerrarOTreinoDeAmizade(t, FimDoTreinoDeAmizade.HabitanteCedeu); return; }
		if (_relogioDosAliados >= t.Ate) { EncerrarOTreinoDeAmizade(t, FimDoTreinoDeAmizade.Tempo); return; }
		// (2026-10-27, o pedido 124b) LONGE DEMAIS SO ENCERRA DEPOIS DE `SegundosLongeQueEncerram` SEGUIDOS: o documento de um jogador, *"The
		// range limit of the spars is also too low, causing them to forcibly be ended as soon as either of you is hit by a knockback punch"*.
		if ((p.Pos - n.Pos).Length > Amizade.TilesDoTreino * ZoneCollision.TileSize)
		{
			if (t.LongeDesde < 0) t.LongeDesde = _relogioDosAliados;
			else if (_relogioDosAliados - t.LongeDesde >= Amizade.SegundosLongeQueEncerram) { EncerrarOTreinoDeAmizade(t, FimDoTreinoDeAmizade.Saiu); return; }
		}
		else t.LongeDesde = -1;

		if (p.Combate.Letal) p.Combate.Letal = false;
	}

	/// <summary>
	/// O TREINO ACABOU: o golpe letal volta pra quem pediu; o habitante larga a presa, o rancor e a marca do parceiro (ninguem sai do treino
	/// cacando ninguem); quem caiu e posto de pe; os dois ganham o folego do fim -- e o fim que vale (o tempo, ou a metade da vida de um
	/// dos dois) paga a amizade, com o retorno decrescente do dia.
	/// </summary>
	/// <summary>
	/// (2026-10-27, o pedido 124b) O TREINO QUE CHEGA AO FIM PAGA -- o documento de um jogador: *"this option should have a decent bp reward
	/// for completing a sparring session successfully, and put you into a state called inspired, where you gain a 1.5x training boost for a
	/// duration"*. O BP de `Amizade.SegundosDeTreinoDaRecompensa` de treino AQUI (os mesmos passes do `Treinar`, com a gravidade, o teto e o
	/// banco de cada um -- quem nao pode treinar no lugar nao ganha), e o estado INSPIRADO, que o `Treinar` le (`Inspiracao`).
	/// </summary>
	private void PagarOTreinoDeAmizade(ServerPlayer p)
	{
		double antes = p.Ficha.BP;
		int passes = (int)Math.Round(Amizade.SegundosDeTreinoDaRecompensa / SegundosPorFicha);
		for (int i = 0; i < passes; i++) p.Ficha.TrainGain(_rng, 6.0 / (1 + Math.Log(2)));   // o fator do passe do T, o mesmo do `Treinar`
		p.Ficha.inspirado_ate = NowMs() + (long)(Amizade.MinutosDoInspirado * 60_000);
		MandarFicha(p);
		Avisar(p, $"o treino rendeu {p.Ficha.BP - antes:N0} de BP -- e você está INSPIRADO: treino {Amizade.MultiplicadorDoInspirado:0.#}x por "
				 + $"{Amizade.MinutosDoInspirado:0} min.");
	}

	/// <summary>(2026-10-27, o pedido 124b) O fator do estado INSPIRADO no passe do treino (`Treinar`): o multiplicador enquanto dura, 1 fora dele.</summary>
	private double Inspiracao(ServerPlayer pl) => pl.Ficha.inspirado_ate > NowMs() ? Amizade.MultiplicadorDoInspirado : 1;

	private void EncerrarOTreinoDeAmizade(TreinoDeAmizade t, FimDoTreinoDeAmizade fim)
	{
		if (!_treinosDeAmizade.TryGetValue(t.Npc.Id, out TreinoDeAmizade? deFato) || !ReferenceEquals(deFato, t)) return;
		_treinosDeAmizade.Remove(t.Npc.Id);
		ServerPlayer p = t.Jogador, n = t.Npc;
		bool jogadorNoMundo = _players.TryGetValue(p.Id, out ServerPlayer? vp) && ReferenceEquals(vp, p);
		bool npcNoMundo = _players.TryGetValue(n.Id, out ServerPlayer? vn) && ReferenceEquals(vn, n) && !n.Ficha.dead;

		if (npcNoMundo)
		{
			if (n.Cerebro is { } cerebro) cerebro.LutaDeTreino = false;
			n.RancorAte = 0;
			n.UltimoAgressor = 0;
			if (n.Ficha.KO) CurarPraLuta(n);
			n.Combate.Carencia = Math.Max(n.Combate.Carencia, Amizade.SegundosDeFolegoAoFim);
		}
		if (jogadorNoMundo)
		{
			p.Combate.Letal = t.LetalAntes;
			// O GOLPE DO HABITANTE TAMBEM MARCOU O JOGADOR (`MarcarAgressao`): sem isto, o aliado com o reflexo "quando me atacam" sairia
			// do treino direto pra cima do parceiro.
			if (p.UltimoAgressor == n.Id) p.RancorAte = 0;
			if (!p.Ficha.dead && p.Ficha.KO) CurarPraLuta(p);
			p.Combate.Carencia = Math.Max(p.Combate.Carencia, Amizade.SegundosDeFolegoAoFim);
		}
		_folegoDoTreino[(p.Id, n.Id)] = _relogioDosAliados + Amizade.SegundosDeFolegoAoFim;

		bool vale = fim is FimDoTreinoDeAmizade.Tempo or FimDoTreinoDeAmizade.JogadorCedeu or FimDoTreinoDeAmizade.HabitanteCedeu;
		GD.Print($"[server] amizade: o treino de {p.Name} com {n.Name} acabou: {fim}");
		if (vale && jogadorNoMundo && npcNoMundo && LacoNovo(n, p) is { } l)
		{
			Amizade.VirarODia(l, DiaDoMundo);
			bool estavaCheio = Amizade.Cheio(l);
			double ganho = Amizade.Somar(l, Amizade.Ganho(Amizade.PontosDoTreino, l.TreinosHoje));
			l.TreinosHoje++;
			Falar(n, Protocol.Fala.Diz, Sortear(Amizade.FalasDoFimDoTreino));
			Avisar(p, $"o treino com {n.Name} acabou. Amizade {l.Pontos:0}/{Amizade.Cheia:0} (+{ganho:0.#}).");
			if (!estavaCheio && Amizade.Cheio(l)) AvisarQueConfia(p, n);
		}
		if (vale && jogadorNoMundo && npcNoMundo) PagarOTreinoDeAmizade(p);   // (2026-10-27) o pedido 124b
		else if (jogadorNoMundo)
			Avisar(p, fim switch
			{
				FimDoTreinoDeAmizade.Parou => $"você encerra o treino com {n.Name} -- treino pela metade não aproxima ninguém.",
				FimDoTreinoDeAmizade.Intrometido => $"o treino com {n.Name} acabou: alguém de fora o acertou.",
				FimDoTreinoDeAmizade.Sumiu => "o treino acabou: o seu parceiro não está mais aqui.",
				_ => $"o treino com {n.Name} acabou: vocês se afastaram.",
			});
		if (jogadorNoMundo)
		{
			MandarAliados(p);
			// O menu da tecla E ainda era o "Parar o treino" ate o proximo tique do apontador.
			ApontarODivinoPerto();
		}
	}

	/// <summary>O par acabou de treinar e ainda esta no folego do fim? O golpe que chega agora era do treino.</summary>
	private bool NoFolegoDoTreino(ServerPlayer jogador, ServerPlayer npc) =>
		_folegoDoTreino.TryGetValue((jogador.Id, npc.Id), out double ate) && _relogioDosAliados < ate;

	// =====================================================================
	// 5. BATER DE VERDADE ZERA
	// =====================================================================
	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): bater no habitante nao mexe no laco -- a amizade sobrevive a uma surra.</summary>
	internal static bool ApanharNaoZeraDeTeste;

	/// <summary>
	/// FULANO BATEU NUM HABITANTE (`MarcarAgressao`, o funil unico de agressao): se nao e o treino dos dois (nem o folego do fim dele), o
	/// laco DESTE personagem com ele zera -- e o rancor de 90 s e o de sempre, que o proprio funil escreve. Matar comeca batendo.
	/// </summary>
	private void AmizadeAoApanhar(ServerPlayer vitima, ServerPlayer agressor)
	{
		if (ApanharNaoZeraDeTeste || agressor.Assinatura.Length == 0) return;
		if (!_amizades.TryGetValue(vitima.Id, out Dictionary<string, LacoDeAmizade>? lacos)) return;
		if (TreinoDeAmizadeEntre(vitima, agressor) || NoFolegoDoTreino(agressor, vitima)) return;
		if (!lacos.Remove(agressor.Assinatura)) return;
		if (lacos.Count == 0) _amizades.Remove(vitima.Id);
		Avisar(agressor, $"{vitima.Name} não confia mais em você: a amizade de vocês voltou a zero.");
		MandarAliados(agressor);
	}

	// =====================================================================
	// 6. RECRUTAR
	// =====================================================================
	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): o "Recrutar" nao confere o laco -- qualquer habitante vem com voce.</summary>
	internal static bool RecrutaSemAmizadeDeTeste;

	/// <summary>
	/// "RECRUTAR": com o laco cheio, o habitante VIVO sai do povoamento (a manutencao repoe outro, de 5 em 5 min) e vira um aliado na ficha
	/// deste personagem -- solto na hora, ao lado, se houver vaga (dois soltos por dono, junto das criaturas de capsula); senao, guardado.
	/// </summary>
	private void Recrutar(ServerPlayer pl)
	{
		if (PorQueNaoConversa(pl) is { } recusa) { Avisar(pl, recusa); return; }
		if (HabitanteDoClique(pl) is not { } npc) { Avisar(pl, "não há nenhum habitante ao seu lado."); return; }
		LacoDeAmizade? l = LacoDe(npc, pl);
		if (!RecrutaSemAmizadeDeTeste && !Amizade.Cheio(l))
		{
			Avisar(pl, $"{npc.Name} ainda não confia em você o bastante (amizade {l?.Pontos ?? 0:0}/{Amizade.Cheia:0}): presentes e treinos aproximam vocês.");
			return;
		}
		List<Aliado> meus = AliadosOuNovos(pl);
		if (meus.Count >= Aliados.Maximo)
		{
			Avisar(pl, $"você já tem {Aliados.Maximo} aliados: dispense um na aba {Aliados.NomeDaAba} antes de recrutar outro.");
			return;
		}
		if (TreinoDeAmizadeDe(npc) != null || PresaDoNpc(npc) != null) { Avisar(pl, $"{npc.Name} está ocupado agora."); return; }

		Fighter ficha = FichaDeVerdade(pl);
		string planeta = npc.Berco.Natal.Length > 0 ? npc.Berco.Natal : npc.Zone.Name;
		Aliado a = Aliados.DoHabitante(ficha.ProximoAliado++, npc.Name, npc.Race, npc.Genero, npc.Visual.Copiar(), npc.Ficha.BP,
									   NomeDoPlaneta(planeta), NowMs(), l?.Pontos ?? Amizade.Cheia);
		meus.Add(a);
		Vec2 onde = npc.Pos;
		Falar(npc, Protocol.Fala.Diz, Sortear(Amizade.FalasDoRecrutamento));
		_amizades.Remove(npc.Id);
		RemoverNpc(npc);
		GD.Print($"[server] aliados: {pl.Name} recrutou {a.Criatura.Nome} ({a.Criatura.Raca}, BP {a.Criatura.Bp:0}) -- {meus.Count} aliado(s)");

		if (SoltosDe(pl) < Companheiros.MaxSoltos && PorQueNaoSolta(pl) == null)
		{
			SoltarOAliado(pl, a, onde);
			Avisar(pl, $"{a.Criatura.Nome} agora é seu aliado e segue você. As ordens ficam na aba {Aliados.NomeDaAba} (tecla P).");
		}
		else
			Avisar(pl, $"{a.Criatura.Nome} agora é seu aliado -- e vai esperar por você: solte-o pela aba {Aliados.NomeDaAba} (tecla P) quando houver vaga.");
		Persistir(pl);   // o aliado mora na ficha: o recrutamento nao pode depender do proximo salvamento
		MandarAliados(pl);
	}

	// =====================================================================
	// 7. A PODA -- o laco morre com o habitante
	// =====================================================================
	/// <summary>
	/// 1 Hz: o laco de quem nao esta mais no mundo (removido, reciclado), morreu ou deixou de ser habitante sai -- e o folego dos treinos que
	/// ja venceu tambem. (A aba dos donos se refaz sozinha: a volta de 1 Hz compara o que ela desenha.)
	/// </summary>
	private void PodarAsAmizades()
	{
		foreach (int id in _amizades.Keys.ToList())
			if (!_players.TryGetValue(id, out ServerPlayer? npc) || !HabitanteDeAmizade(npc)) _amizades.Remove(id);
		foreach ((int, int) par in _folegoDoTreino.Where(kv => _relogioDosAliados >= kv.Value).Select(kv => kv.Key).ToList())
			_folegoDoTreino.Remove(par);
	}

	/// <summary>
	/// (2026-10-21) A VAGA RECICLADA (`PurgarAssinatura`, via `EsquecerAAssinaturaNoMundo`): os lacos do personagem que saiu da vaga somem --
	/// senao o proximo da vaga herdaria a amizade dele com cada habitante. Os aliados moram na ficha e ja vao embora com ela.
	/// </summary>
	private void EsquecerAsAmizades(string sig)
	{
		if (AmizadeHerdaAVagaDeTeste || sig.Length == 0) return;
		foreach (int id in _amizades.Keys.ToList())
			if (_amizades[id].Remove(sig) && _amizades[id].Count == 0) _amizades.Remove(id);
	}

	/// <summary>DEFEITO INJETADO (bancada `--aliadosteste`): a vaga reciclada herda os lacos de amizade do personagem que saiu.</summary>
	internal static bool AmizadeHerdaAVagaDeTeste;
}
