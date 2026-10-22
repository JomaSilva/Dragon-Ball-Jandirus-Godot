using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Items;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// (v0.2.1, lote T5 da tech) OS COMPANHEIROS -- as capsulas, a criatura que elas soltam, as ordens, a experiencia, o
/// interceptador, a captura e a racao. A regra mora em `Core/Tech/Companheiros.cs`, com o que o original faz e o que
/// muda aqui. A criatura e um corpo sem dono de tela que OBEDECE -- o mesmo desenho da copia do Split Form
/// (`GuiarSplitformG12`): quem ela persegue e pra onde vai saem da ordem do dono.
/// </summary>
public sealed partial class GameServer
{
	private enum OrdemDeCompanheiro { Seguir, Ficar }

	/// <summary>Uma criatura SOLTA: de quem e, de qual gaveta do cofre veio, e o que ela esta fazendo.</summary>
	private sealed class CompanheiroSolto
	{
		public int Dono;
		public int Gaveta;
		public OrdemDeCompanheiro Ordem = OrdemDeCompanheiro.Seguir;   // `setting = 1`: nasce seguindo

		/// <summary>Quem ela ataca (0 = ninguem), e de onde a perseguicao comecou (`aggro_loc`).</summary>
		public int Presa;
		public Vec2 OndeComecou;

		public double RelogioDeExp, RelogioDeRelacao, RelogioDoInterceptador;

		/// <summary>
		/// (2026-10-21) O ALIADO que este corpo e (o habitante recrutado pela amizade), ou nulo pra criatura de capsula. O registro dele
		/// mora na FICHA do dono (`Fighter.Aliados`), e nao numa gaveta do cofre (`Gaveta` fica zero). Ver `GameServer.Aliados.cs`.
		/// </summary>
		public Jandirus.Core.Npc.Aliado? Aliado;
	}

	/// <summary>O registro do motor deste corpo solto: o do aliado (na ficha do dono) ou o da gaveta da capsula (no cofre).</summary>
	private Companheiro? CriaturaDoSolto(CompanheiroSolto s) => s.Aliado?.Criatura ?? _cofreDeTech.Companheiros.GetValueOrDefault(s.Gaveta);

	/// <summary>id do corpo da criatura -> o estado dela. So memoria: o que dura (nome, poder, nivel) mora no cofre.</summary>
	private readonly Dictionary<int, CompanheiroSolto> _companheiros = [];

	/// <summary>`aggro_dist*2`, com o `aggro_dist = 30` de todo `/mob/npc` (`NPCAI.dm:52`): alem disso ela desiste.</summary>
	private const float TilesDePerseguicaoDoCompanheiro = 60;

	/// <summary>`as null|mob in view(5)`: a ordem de atacar vale pra quem esta a ate cinco tiles do dono.</summary>
	private const float TilesDaOrdemDeAtaque = 5;

	/// <summary>As acoes de mochila deste lote. Falso quando a acao nao e daqui.</summary>
	private bool AcaoDeCompanheiro(ServerPlayer pl, ItemDef def, string acao, string arg2)
	{
		switch (acao)
		{
			case Companheiros.AcaoSoltar: SoltarOuRecolher(pl, def); return true;
			case Companheiros.AcaoCapturar: CapturarComABola(pl, def); return true;
			case Companheiros.AcaoInfo: InfoDoCompanheiro(pl, def); return true;
			case Companheiros.AcaoSeguir: OrdenarSeguirOuFicar(pl, def); return true;
			case Companheiros.AcaoAtacar: OrdenarOAtaque(pl, def, matar: false); return true;
			case Companheiros.AcaoMatar: OrdenarOAtaque(pl, def, matar: true); return true;
			case Companheiros.AcaoParar: MandarParar(pl, def); return true;
			case Companheiros.AcaoQuandoAtaco: AlternarReflexo(pl, def, quandoAtaco: true); return true;
			case Companheiros.AcaoQuandoApanho: AlternarReflexo(pl, def, quandoAtaco: false); return true;
			case Companheiros.AcaoFalar: FalarPeloCompanheiro(pl, def, arg2); return true;
			case Companheiros.AcaoNome: RebatizarOCompanheiro(pl, def, arg2); return true;
			case Companheiros.AcaoMelhorar: GastarAMelhoria(pl, def, arg2); return true;
			case Companheiros.AcaoPoder: AtualizarOPoder(pl, def, arg2); return true;
			case Companheiros.AcaoRacao: DarARacao(pl, def); return true;
			// (lote T5) o que o sintetizador de virus fabrica -- `GameServer.Sintetizador.cs`.
			default: return AcaoDeSintetizado(pl, def, acao, arg2);
		}
	}

	// =====================================================================
	// A CAPSULA E A CRIATURA DELA
	// =====================================================================
	/// <summary>
	/// A criatura desta capsula. A de fabrica ganha a gaveta dela no primeiro uso (o `New()` do `companion_obj`, que
	/// preenche o nome e a cara pelo tipo); a bola vazia devolve nulo. O id devolvido e o da capsula DEPOIS disso.
	/// </summary>
	private (Companheiro? Criatura, string Id) CriaturaDaCapsula(ServerPlayer pl, string id)
	{
		int gaveta = Companheiros.Gaveta(id);
		if (gaveta > 0) return (_cofreDeTech.Companheiros.GetValueOrDefault(gaveta), id);
		string @base = TechDeBolso.Base(id);
		if (Companheiros.DaCapsula(@base) is not { } e) return (null, id);

		gaveta = _cofreDeTech.Proximo;
		var c = new Companheiro { Especie = e.Nome, Nome = e.Nome, Raca = e.Raca, Bp = e.Bp, BpMod = e.BpMod, Inteligencia = e.Inteligencia };
		string novo = Companheiros.Escrever(@base, gaveta, c.Inteligencia);
		if (!TrocarItem(pl, id, novo)) return (null, id);
		_cofreDeTech.Proximo++;
		_cofreDeTech.Companheiros[gaveta] = c;
		_cofreSujo = true;
		return (c, novo);
	}

	/// <summary>O corpo solto da criatura desta gaveta, se ela esta solta.</summary>
	private (ServerPlayer Corpo, CompanheiroSolto Estado)? SoltoDaGaveta(int gaveta)
	{
		if (gaveta <= 0) return null;
		foreach ((int id, CompanheiroSolto s) in _companheiros)
			if (s.Gaveta == gaveta && _players.TryGetValue(id, out ServerPlayer? corpo)) return (corpo, s);
		return null;
	}

	/// <summary>A criatura SOLTA desta capsula e o registro dela -- ou a recusa dita, e nulo.</summary>
	private (ServerPlayer Corpo, CompanheiroSolto Estado, Companheiro Criatura)? SoltoOuRecusa(ServerPlayer pl, ItemDef def)
	{
		int gaveta = Companheiros.Gaveta(def.Id);
		if (_cofreDeTech.Companheiros.GetValueOrDefault(gaveta) is not { } c) { Avisar(pl, "esta cápsula não guarda criatura nenhuma."); return null; }
		// `"No creature out!"`.
		if (SoltoDaGaveta(gaveta) is not { } solto || solto.Estado.Dono != pl.Id) { Avisar(pl, $"{c.Nome} não está solta: solte-a antes."); return null; }
		return (solto.Corpo, solto.Estado, c);
	}

	/// <summary>A criatura entende esta ordem? (`if(techmod &gt;= N) cmd_list += ...`.)</summary>
	private bool EntendeAOrdem(ServerPlayer pl, Companheiro c, int inteligencia)
	{
		if (c.Inteligencia >= inteligencia) return true;
		Avisar(pl, $"{c.Nome} não é esperta o bastante pra isso (inteligência {c.Inteligencia} de {inteligencia}). A ração a deixa mais esperta.");
		return false;
	}

	/// <summary>O `Use()` da capsula: solta a criatura (*"appears!!"*) ou a recolhe (*"disappears!"*).</summary>
	private void SoltarOuRecolher(ServerPlayer pl, ItemDef def)
	{
		(Companheiro? c, string id) = CriaturaDaCapsula(pl, def.Id);
		// `"No creature type linked [...] For blank companion shards, link them using a knocked out creature!"`.
		if (c == null) { Avisar(pl, "a bola está vazia: capture com ela uma criatura nocauteada."); return; }
		int gaveta = Companheiros.Gaveta(id);

		if (SoltoDaGaveta(gaveta) is { } solto)
		{
			RecolherCompanheiro(solto.Corpo.Id, null);
			Falar(pl, Protocol.Fala.Emote, $"recolhe {c.Nome} na cápsula.");
			Avisar(pl, $"{c.Nome} volta pra cápsula.");
			return;
		}

		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não solta ninguém."); return; }
		if (ZonaFechadaAoTeleporte(pl.Zone) || PresoNoTorneio(pl.Id)) { Avisar(pl, "aqui a cápsula não abre."); return; }
		// `usr.out_mobs.len <= 1`.
		if (!Companheiros.SemTetoDeSoltosDeTeste && _companheiros.Values.Count(s => s.Dono == pl.Id) >= Companheiros.MaxSoltos)
		{ Avisar(pl, $"você já tem {Companheiros.MaxSoltos} criaturas soltas: recolha uma antes."); return; }

		CriarCompanheiro(pl, c, gaveta);
		Falar(pl, Protocol.Fala.Emote, $"abre uma cápsula: {c.Nome} aparece!");
		Avisar(pl, $"{c.Nome} aparece ao seu lado e passa a seguir você. As ordens saem da cápsula.");
	}

	/// <summary>
	/// O CORPO DA CRIATURA (`add_linked_mob`): nasce a um passo do dono, com o nome, a cara e os numeros que a capsula
	/// guarda (`update_linked_mob`), e com o golpe letal do dono (`mob_reference.murderToggle = usr.murderToggle`).
	/// (2026-10-21) O ALIADO nasce pela mesma porta, com o registro da ficha (`aliado`) -- e, recrutado, onde o habitante estava (`onde`).
	/// </summary>
	private ServerPlayer CriarCompanheiro(ServerPlayer dono, Companheiro c, int gaveta, Jandirus.Core.Npc.Aliado? aliado = null, Vec2? onde = null)
	{
		Vec2 aFrente = dono.Pos + MeleeArea.Frente(dono.Facing) * ZoneCollision.TileSize;
		var corpo = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = c.Nome,
			Zone = dono.Zone,
			Pos = PontoLivre(dono.Zone, onde ?? aFrente) ?? dono.Pos,
			Facing = dono.Facing,
			Race = c.Raca,
			Genero = c.Genero,
			Planeta = dono.Planeta,
			Idade = (int)Jandirus.Core.Races.Envelhecimento.IdadeAdulta,
			Visual = c.Visual?.Copiar() ?? new Jandirus.Core.Appearance.Appearance(),
			LastInputMs = NowMs(),
			// O CEREBRO DE FABRICA, E AQUI ELE E O LITERAL (o argumento da copia do Split Form, `CriarSplitformG12`): o
			// `mob/npc/pet` nao declara `ai_intelligence` nem `ai_aggression` e herda os do `/mob/npc`
			// (`NPCAI.dm:62-76`) -- os defaults do `Cerebro`. Ela NAO e posse: nenhum corpo de jogador recebe este
			// cerebro, e e um lutador comum que obedece a ordem do dono. A `--iateste` (d) tem este arquivo na lista
			// dela com este argumento. (Os multiplicadores de temperamento de cada capsula -- `linked_mob_behavior` --
			// nao entram: a criatura nao tem molde nem semente pro `Temperamento` sortear.)
			Cerebro = new Cerebro(),
			Ficha = new Fighter { Name = c.Nome, Race = c.Raca, Idade = Jandirus.Core.Races.Envelhecimento.IdadeAdulta },
			Livro = new SkillBook(),
		};
		Fighter f = corpo.Ficha;
		Companheiros.Aplicar(f, c);
		f.HP = 100;
		f.KO = false; f.dead = false;
		f.Statify();
		f.Ki = f.MaxKi;
		f.PowerLevel();

		PorNoMundo(corpo);
		corpo.Combate.Letal = dono.Combate?.Letal ?? false;
		TrocarAparencias(corpo);
		_companheiros[corpo.Id] = new CompanheiroSolto { Dono = dono.Id, Gaveta = gaveta, Aliado = aliado };
		return corpo;
	}

	/// <summary>A criatura volta pra capsula (`remove_linked_mob`): o corpo sai do mundo, e o que ela e fica no cofre.</summary>
	private void RecolherCompanheiro(int idDoCorpo, string? avisoAoDono)
	{
		if (!_companheiros.Remove(idDoCorpo, out CompanheiroSolto? s)) return;
		if (_players.TryGetValue(idDoCorpo, out ServerPlayer? corpo)) RemoverNpc(corpo);
		if (avisoAoDono != null && _players.TryGetValue(s.Dono, out ServerPlayer? dono)) Avisar(dono, avisoAoDono);
	}

	/// <summary>Os numeros do cofre voltam pro corpo solto (`update_linked_mob`), depois de uma melhoria, da racao ou do poder novo.</summary>
	private void ReaplicarNoCorpo(ServerPlayer corpo, Companheiro c)
	{
		Companheiros.Aplicar(corpo.Ficha, c);
		corpo.Ficha.Statify();
		corpo.Ficha.PowerLevel();
		corpo.SigAtributos = "";
	}

	// =====================================================================
	// A PEE-KAY BALL -- `verb/Capture()` (`companion_list.dm:68-84`)
	// =====================================================================
	/// <summary>Este corpo pode virar a criatura de uma bola? Ver o cabecalho do `Companheiros`.</summary>
	private bool PodeSerCapturado(ServerPlayer o) =>
		o.Peer == null && o.CerebroDaPosse == null && o.DonoDoClone == 0 && o.PresoGuardado == 0
		&& !_companheiros.ContainsKey(o.Id) && !_simulacoes.ContainsKey(o.Id) && !_splitformsG12.ContainsKey(o.Id)
		&& o.Papel is not { Tipo: Jandirus.Core.Npc.TipoDeNpc.Chefe or Jandirus.Core.Npc.TipoDeNpc.Lutador }
		&& !Jandirus.Core.Npc.BillsEWhis.EhRacaDivina(o.Race);

	private void CapturarComABola(ServerPlayer pl, ItemDef def)
	{
		// `"Already have a mob type."`.
		if (Companheiros.Gaveta(def.Id) > 0) { Avisar(pl, "esta bola já guarda uma criatura."); return; }
		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não captura ninguém."); return; }
		// `for(var/mob/npc/M in get_step(src,dir)) if(!istype(M,/mob/npc/pet) && !M.sim && M.quality && M.KO)`.
		ServerPlayer? alvo = QuemEstaNaFrente(pl, o => o.Ficha.KO && !o.Ficha.dead && PodeSerCapturado(o));
		if (alvo == null) { Avisar(pl, "não há criatura nocauteada à sua frente que a bola possa capturar."); return; }   // `"No target!"`

		int gaveta = _cofreDeTech.Proximo;
		var c = new Companheiro
		{
			Especie = Companheiros.EspecieCapturada, Nome = alvo.Name, Raca = alvo.Race, Genero = alvo.Genero,
			Visual = alvo.Visual.Copiar(), Bp = Math.Max(alvo.Ficha.BP, 1),
		};
		if (!TrocarItem(pl, def.Id, Companheiros.Escrever(Companheiros.Bola, gaveta, c.Inteligencia))) { Avisar(pl, "sua mochila está cheia."); return; }
		_cofreDeTech.Proximo++;
		_cofreDeTech.Companheiros[gaveta] = c;
		GravarOCofreDeTech();
		Falar(pl, Protocol.Fala.Emote, $"aponta uma bola pra {alvo.Name}: ela guarda a forma dele!");
		Avisar(pl, $"a bola captura {alvo.Name}. Solte a criatura pela bola quando quiser.");
	}

	// =====================================================================
	// AS ORDENS -- o `Click()` do `mob/npc/pet` (`companion_list.dm:112-220`) e o `Remote_Update`
	// =====================================================================
	/// <summary>"Follow/Stay": `"You tell [src] to stay."` / `"You tell [src] to follow."`.</summary>
	private void OrdenarSeguirOuFicar(ServerPlayer pl, ItemDef def)
	{
		if (SoltoOuRecusa(pl, def) is not { } x || !EntendeAOrdem(pl, x.Criatura, Companheiros.IntDeObedecer)) return;
		bool fica = x.Estado.Ordem == OrdemDeCompanheiro.Seguir;
		x.Estado.Ordem = fica ? OrdemDeCompanheiro.Ficar : OrdemDeCompanheiro.Seguir;
		Avisar(pl, fica ? $"você manda {x.Criatura.Nome} ficar." : $"você manda {x.Criatura.Nome} seguir você.");
	}

	/// <summary>"Attack" e "Kill" (`murderToggle = 1`): a presa e o alvo MARCADO do dono, a ate cinco tiles dele.</summary>
	private void OrdenarOAtaque(ServerPlayer pl, ItemDef def, bool matar)
	{
		if (SoltoOuRecusa(pl, def) is not { } x) return;
		MandarAtacar(pl, x.Corpo, x.Estado, x.Criatura, matar);
	}

	/// <summary>A ordem de atacar (ou matar) o alvo marcado, pra criatura de capsula e pro aliado (a aba Allies) -- a mesma regra.</summary>
	private void MandarAtacar(ServerPlayer pl, ServerPlayer corpo, CompanheiroSolto s, Companheiro c, bool matar)
	{
		if (!EntendeAOrdem(pl, c, matar ? Companheiros.IntDeMatar : Companheiros.IntDeObedecer)) return;
		if (pl.AlvoId == 0 || !_players.TryGetValue(pl.AlvoId, out ServerPlayer? alvo)) { Avisar(pl, "marque um alvo primeiro (duplo clique)."); return; }
		if (alvo == corpo || alvo == pl || alvo.Zone.Hash != pl.Zone.Hash || alvo.Ficha.dead
			|| Vec2.Distance(alvo.Pos, pl.Pos) > TilesDaOrdemDeAtaque * ZoneCollision.TileSize)
		{ Avisar(pl, "o alvo tem de estar a até cinco tiles de você."); return; }

		s.Presa = alvo.Id;
		s.OndeComecou = corpo.Pos;
		if (matar) corpo.Combate.Letal = true;
		Avisar(pl, matar ? $"{c.Nome} parte pra matar {NomeVisivel(alvo)}." : $"{c.Nome} parte pra cima de {NomeVisivel(alvo)}.");
	}

	/// <summary>"Stop": `mob_reference.target = null; resetState()` -- larga a presa, volta, e o corpo se refaz.</summary>
	private void MandarParar(ServerPlayer pl, ItemDef def)
	{
		if (SoltoOuRecusa(pl, def) is not { } x) return;
		LargarAPresa(x.Corpo, x.Estado);
		Avisar(pl, $"{x.Criatura.Nome} larga o que fazia e volta pra você.");
	}

	/// <summary>O `resetState()`: sem presa, e com o corpo refeito (`B.RegrowLimb(); B.health = B.maxhealth`).</summary>
	private static void LargarAPresa(ServerPlayer corpo, CompanheiroSolto s)
	{
		s.Presa = 0;
		if (corpo.Ficha.KO || corpo.Ficha.dead) return;
		corpo.Combate.Corpo.Restaurar();
		corpo.Combate.SincronizarVida();
	}

	/// <summary>"Attack When I Do" e "Attack when I am attacked": pedem inteligencia E confianca (`relation &gt; 20`).</summary>
	private void AlternarReflexo(ServerPlayer pl, ItemDef def, bool quandoAtaco)
	{
		int gaveta = Companheiros.Gaveta(def.Id);
		if (_cofreDeTech.Companheiros.GetValueOrDefault(gaveta) is not { } c) { Avisar(pl, "esta cápsula não guarda criatura nenhuma."); return; }
		AlternarReflexoDe(pl, c, quandoAtaco);
	}

	/// <summary>Os dois reflexos, pra criatura de capsula e pro aliado (a aba Allies): inteligencia E confianca, e a frase de cada lado.</summary>
	private void AlternarReflexoDe(ServerPlayer pl, Companheiro c, bool quandoAtaco)
	{
		if (!EntendeAOrdem(pl, c, quandoAtaco ? Companheiros.IntDeQuandoAtaco : Companheiros.IntDeQuandoApanho)) return;
		if (c.Relacao <= Companheiros.RelacaoDeConfianca)
		{ Avisar(pl, $"{c.Nome} ainda não confia em você o bastante pra isso (relação {c.Relacao:0} de mais de {Companheiros.RelacaoDeConfianca:0}): ela cresce com o tempo solta ao seu lado."); return; }
		if (quandoAtaco) c.QuandoAtaco = !c.QuandoAtaco; else c.QuandoApanho = !c.QuandoApanho;
		_cofreSujo = true;
		bool ligou = quandoAtaco ? c.QuandoAtaco : c.QuandoApanho;
		Avisar(pl, quandoAtaco
			? (ligou ? $"{c.Nome} passa a atacar quem você ataca." : $"{c.Nome} deixa de atacar por conta de quem você ataca.")
			: (ligou ? $"{c.Nome} passa a atacar quem bate em você." : $"{c.Nome} deixa de atacar quem bate em você."));
	}

	/// <summary>"Speak": `sayType(msg,3)` -- a criatura diz a frase.</summary>
	private void FalarPeloCompanheiro(ServerPlayer pl, ItemDef def, string cru)
	{
		if (SoltoOuRecusa(pl, def) is not { } x || !EntendeAOrdem(pl, x.Criatura, Companheiros.IntDeFalar)) return;
		string frase = TextoLivre(cru, Vigilancia.MaxDeFrase);
		if (frase.Length == 0) return;
		if (EstaCalado(pl)) { Avisar(pl, "você está silenciado."); return; }
		Falar(x.Corpo, Protocol.Fala.Diz, frase);
	}

	/// <summary>"Change Name": `name=input() as text`.</summary>
	private void RebatizarOCompanheiro(ServerPlayer pl, ItemDef def, string cru)
	{
		int gaveta = Companheiros.Gaveta(def.Id);
		if (_cofreDeTech.Companheiros.GetValueOrDefault(gaveta) is not { } c) { Avisar(pl, "esta cápsula não guarda criatura nenhuma."); return; }
		string nome = TechDeBolso.Limpo(cru, Companheiros.MaxDeNome);
		if (nome.Length == 0) { Avisar(pl, "o nome não pode ser vazio."); return; }
		c.Nome = nome;
		GravarOCofreDeTech();
		if (SoltoDaGaveta(gaveta) is { } solto)
		{
			solto.Corpo.Name = nome;
			solto.Corpo.Ficha.Name = nome;
			TrocarAparencias(solto.Corpo);
		}
		Avisar(pl, $"a criatura passa a se chamar {nome}.");
	}

	// =====================================================================
	// AS MELHORIAS, O PODER E A RACAO
	// =====================================================================
	/// <summary>O `Upgrade()`: uma melhoria ganha num nivel vai pros fisicos (1), pro ki (2), pros variados (3) ou pro poder (4).</summary>
	private void GastarAMelhoria(ServerPlayer pl, ItemDef def, string arg2)
	{
		int gaveta = Companheiros.Gaveta(def.Id);
		if (_cofreDeTech.Companheiros.GetValueOrDefault(gaveta) is not { } c) { Avisar(pl, "esta cápsula não guarda criatura nenhuma."); return; }
		if (GastarAMelhoriaDe(pl, c, arg2, SoltoDaGaveta(gaveta)?.Corpo, "ela ganha uma a cada nível (lutando)")) GravarOCofreDeTech();
	}

	/// <summary>
	/// A MELHORIA GASTA, pra criatura de capsula e pro aliado (a aba Allies): o grupo escolhido sobe, e o corpo solto recebe os numeros
	/// novos. Falso = recusada (dita). `comoGanha` e o fim da frase de quem nao tem melhoria.
	/// </summary>
	private bool GastarAMelhoriaDe(ServerPlayer pl, Companheiro c, string arg2, ServerPlayer? corpoSolto, string comoGanha)
	{
		if (c.Sobras <= 0) { Avisar(pl, $"{c.Nome} não tem melhoria pra gastar: {comoGanha}."); return false; }
		if (!int.TryParse(arg2, out int qual) || qual is < 1 or > 4) { Avisar(pl, "escolha: 1 físicos, 2 Ki, 3 variados, 4 poder."); return false; }

		c.Sobras--;
		string onde;
		switch (qual)
		{
			case 1: c.Fisico++; onde = "os físicos (ataque e defesa)"; break;
			case 2: c.Ki++; onde = "o Ki (ataque, defesa e perícia)"; break;
			case 3: c.Misc++; onde = "os variados (técnica, velocidade e magia)"; break;
			default:
				// `linked_bp_mod += 0.1; upgrade_linked_mob_bp()`.
				c.BpMod += 0.1;
				c.Bp = Companheiros.Poder(MediaDoServidor(), pl.Ficha.BP, c.Nivel, c.BpMod, c.Bp);
				onde = "o poder";
				break;
		}
		if (corpoSolto != null) ReaplicarNoCorpo(corpoSolto, c);
		Avisar(pl, $"{c.Nome}: a melhoria vai pra {onde}. Restam {c.Sobras}.");
		return true;
	}

	/// <summary>
	/// O "Update BP" do `Remote_Update`: `"Update BP will require 10000 and one food item."` Em zeni ou (o dobro) em
	/// recursos; a comida e a primeira coisa de comer da mochila.
	/// </summary>
	private void AtualizarOPoder(ServerPlayer pl, ItemDef def, string arg2)
	{
		if (SoltoOuRecusa(pl, def) is not { } x) return;
		(_, Moeda moeda) = Recurso.LerPedidoPago(arg2);
		string? comida = null;
		foreach (Pilha p in pl.Mochila.Pilhas)
			if (p.Quantidade > 0 && CatalogoDeItens.Get(p.Id) is { } item && Array.IndexOf(item.AcoesDoItem, "comer") >= 0) { comida = p.Id; break; }
		if (comida == null) { Avisar(pl, "atualizar o poder da criatura pede uma comida na mochila."); return; }
		if (!CobrarOuExplicar(pl, Companheiros.CustoDeAtualizar, moeda, "atualizar o poder da criatura")) return;
		pl.Mochila.Tirar(comida);
		MandarMochila(pl);

		Companheiro c = x.Criatura;
		double antes = c.Bp;
		c.Bp = Companheiros.Poder(MediaDoServidor(), pl.Ficha.BP, c.Nivel, c.BpMod, c.Bp);
		GravarOCofreDeTech();
		ReaplicarNoCorpo(x.Corpo, c);
		Avisar(pl, c.Bp > antes
			? $"{c.Nome} come e fica mais forte."
			: $"{c.Nome} come -- e o poder dela já estava no que a média do mundo dá.");
	}

	/// <summary>A RACAO (`Pet_Food`): tres de inteligencia pra criatura do dono que estiver ao lado, e a racao se gasta.</summary>
	private void DarARacao(ServerPlayer pl, ItemDef def)
	{
		// `for(var/mob/npc/pet/M in view(1,usr))`.
		float alcance = 1.5f * ZoneCollision.TileSize;
		(ServerPlayer Corpo, CompanheiroSolto Estado)? perto = null;
		// (2026-10-21) O ALIADO NAO COME RACAO: ele e gente, e o registro dele nao mora numa gaveta do cofre (`s.Aliado`).
		foreach ((int id, CompanheiroSolto s) in _companheiros)
			if (s.Dono == pl.Id && s.Aliado == null && _players.TryGetValue(id, out ServerPlayer? corpo) && corpo.Zone.Hash == pl.Zone.Hash
				&& Vec2.Distance(corpo.Pos, pl.Pos) <= alcance)
			{ perto = (corpo, s); break; }
		if (perto is not { } x || _cofreDeTech.Companheiros.GetValueOrDefault(x.Estado.Gaveta) is not { } c)
		{ Avisar(pl, "nenhuma criatura sua está ao seu lado pra comer a ração."); return; }
		// (o `M.owner_ref == src` do original compara o dono com a propria racao -- ver o cabecalho do `Companheiros`)
		if (Companheiros.RacaoDoOriginalDeTeste) return;

		pl.Mochila.Tirar(def.Id);
		c.Inteligencia += Companheiros.InteligenciaDaRacao;
		GravarOCofreDeTech();
		// A INTELIGENCIA VIAJA NO ID DA CAPSULA (e ela que decide que ordens a mochila mostra): a capsula desta criatura
		// troca de id junto.
		string? capsula = pl.Mochila.Pilhas.Find(p => p.Quantidade > 0 && Companheiros.EhCapsula(TechDeBolso.Base(p.Id))
														&& Companheiros.Gaveta(p.Id) == x.Estado.Gaveta).Id;
		if (capsula != null) TrocarItem(pl, capsula, Companheiros.Escrever(TechDeBolso.Base(capsula), x.Estado.Gaveta, c.Inteligencia));
		else MandarMochila(pl);
		ReaplicarNoCorpo(x.Corpo, c);
		Falar(pl, Protocol.Fala.Emote, $"dá uma ração a {c.Nome}.");
		Avisar(pl, $"{c.Nome} fica mais esperta (inteligência {c.Inteligencia}).");
	}

	/// <summary>O `Info()` da capsula. O poder so sai em numero pra quem tem medidor (`PoderEscrito`).</summary>
	private void InfoDoCompanheiro(ServerPlayer pl, ItemDef def)
	{
		(Companheiro? c, _) = CriaturaDaCapsula(pl, def.Id);
		// `"[name]: Get a creature type in this!"`.
		if (c == null) { Avisar(pl, "a bola está vazia: capture com ela uma criatura nocauteada."); return; }
		Avisar(pl, $"{c.Nome}: uma criatura do tipo {c.Especie}. Poder: {PoderEscrito(pl, c.Bp)}, com modificador de {c.BpMod:0.#}x.");
		Avisar(pl, $"relação {c.Relacao:0} · inteligência {c.Inteligencia} (quanto maior, melhor).");
		Avisar(pl, $"melhorias: físicos {c.Fisico}, Ki {c.Ki}, variados {c.Misc} -- {c.Sobras} por gastar.");
		Avisar(pl, c.Nivel >= Companheiros.NivelMaximo
			? $"nível {c.Nivel} de {Companheiros.NivelMaximo} (o máximo)."
			: $"nível {c.Nivel} de {Companheiros.NivelMaximo}, com {c.Exp:0} de {Companheiros.ExpDoNivel(c.Nivel):0} de experiência.");
	}

	// =====================================================================
	// O CORPO SOLTO: quem ele persegue, e o relogio dele
	// =====================================================================
	/// <summary>Este corpo serve de presa pra criatura? Vivo, na zona dela, e de pe -- ou caido, se ela mata (`if(src.target.KO &amp;&amp; !src.murderToggle) break`).</summary>
	private bool ServeDePresa(ServerPlayer corpo, CompanheiroSolto s, int idDaPresa, out ServerPlayer presa)
	{
		presa = corpo;
		if (idDaPresa == 0 || idDaPresa == corpo.Id || idDaPresa == s.Dono || !_players.TryGetValue(idDaPresa, out ServerPlayer? m)) return false;
		presa = m;
		if (_companheiros.TryGetValue(m.Id, out CompanheiroSolto? irma) && irma.Dono == s.Dono) return false;
		return m.Zone.Hash == corpo.Zone.Hash && !m.Ficha.dead && (!m.Ficha.KO || corpo.Combate.Letal);
	}

	/// <summary>
	/// O RAMO DA CRIATURA NO `TicarUmCorpo` (`GameServer.Clone.cs`): quem ela persegue e pra onde vai saem da ordem do
	/// dono (`follow_state`, `foundTarget`, `chaseState`, `resetState` -- `companions.dm:307-509`). Devolve falso
	/// quando o corpo nao pensa neste tique.
	/// </summary>
	private bool GuiarOCompanheiro(ServerPlayer corpo, CompanheiroSolto s, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = corpo.Pos;
		// QUEM SAI DA ZONA (OU DO JOGO) LEVA A CRIATURA PRA CAPSULA -- ver o cabecalho do `Companheiros`.
		if (!_players.TryGetValue(s.Dono, out ServerPlayer? dono) || dono.Zone.Hash != corpo.Zone.Hash)
		{
			RecolherCompanheiro(corpo.Id, s.Aliado is { } aliado ? $"{aliado.Criatura.Nome} volta pra casa: chame-o de novo pela aba {Jandirus.Core.Npc.Aliados.NomeDaAba}."
																   : "a sua criatura volta pra cápsula.");
			return false;
		}
		if (corpo.Ficha.dead || corpo.Ficha.KO) { corpo.Moving = false; return false; }
		const float T = ZoneCollision.TileSize;

		// OS DOIS REFLEXOS, pra quem tem a ordem ligada: o alvo de quem o dono esta batendo, e quem bate no dono.
		if (s.Presa == 0 && CriaturaDoSolto(s) is { } c)
		{
			int novo = 0;
			if (c.QuandoApanho && dono.UltimoAgressor != 0 && NowMs() < dono.RancorAte) novo = dono.UltimoAgressor;
			else if (c.QuandoAtaco && dono.Combate is { EmCombate: > 0 } && dono.AlvoId != 0) novo = dono.AlvoId;
			// (2026-10-21) O PARCEIRO DE UM TREINO DE AMIZADE NAO E INIMIGO: o golpe dele no dono, e o do dono nele, sao o treino -- ver
			// `GameServer.Amizade.cs`. Sem isto, o companheiro com reflexo entraria no treino e o encerraria como intrometido.
			if (novo != 0 && TreinoDeAmizadeDe(dono) is { } treino && treino.Npc.Id == novo) novo = 0;
			if (novo != 0 && ServeDePresa(corpo, s, novo, out _)) { s.Presa = novo; s.OndeComecou = corpo.Pos; }
		}

		if (s.Presa != 0)
		{
			// `if(get_dist(aggro_loc,src)>aggro_dist*2||(src.target.KO&&!src.murderToggle)) src.lostTarget()`.
			if (!Companheiros.NaoObedeceDeTeste && ServeDePresa(corpo, s, s.Presa, out ServerPlayer m)
				&& Vec2.Distance(corpo.Pos, s.OndeComecou) <= TilesDePerseguicaoDoCompanheiro * T)
			{
				presa = m;
				destino = m.Pos;
				return true;
			}
			if (!Companheiros.NaoObedeceDeTeste) LargarAPresa(corpo, s);
		}

		if (s.Ordem == OrdemDeCompanheiro.Ficar) return true;

		// `follow_state()`: so anda quando o dono esta a mais de tres tiles; longe demais, reaparece ao lado dele.
		float d = Vec2.Distance(corpo.Pos, dono.Pos);
		if (d > Companheiros.TilesDeSumico * T) corpo.Pos = PontoAoLadoG11(dono);
		else if (d > Companheiros.TilesDeFolga * T) destino = dono.Pos;
		return true;
	}

	/// <summary>A experiencia de luta (`gain_exp`): cada nivel pede `10 * nivel`, e da uma melhoria pra gastar.</summary>
	private void DarExperiencia(CompanheiroSolto s, Companheiro c, double quanto)
	{
		if (c.Nivel >= Companheiros.NivelMaximo) return;
		c.Exp += quanto;
		while (c.Nivel < Companheiros.NivelMaximo && c.Exp >= Companheiros.ExpDoNivel(c.Nivel))
		{
			c.Exp -= Companheiros.ExpDoNivel(c.Nivel);
			c.Nivel++;
			c.Sobras++;
			if (_players.TryGetValue(s.Dono, out ServerPlayer? dono))
				Avisar(dono, $"{c.Nome} sobe pro nível {c.Nivel}: há uma melhoria pra gastar "
							 + (s.Aliado != null ? $"(pela aba {Jandirus.Core.Npc.Aliados.NomeDaAba})." : "(pela cápsula)."));
		}
		_cofreSujo = true;
	}

	/// <summary>
	/// O `InterceptorAI`: `for(var/obj/attack/blast/A in view(9)) if(A.proprietor!=owner_ref) ... del(A)` -- todo
	/// disparo solto que nao e do dono (nem dela), a ate nove tiles, cai. O raio canalizado fica de fora: ele esta
	/// preso a mao de quem o segura.
	/// </summary>
	private int DerrubarOsDisparos(ServerPlayer corpo, CompanheiroSolto s)
	{
		if (Companheiros.InterceptadorParadoDeTeste) return 0;
		float raio = Companheiros.RaioDoInterceptador * ZoneCollision.TileSize;
		int derrubados = 0;
		foreach (Projetil p in ProjeteisDaZona(corpo.Zone.Hash).ToList())
		{
			if (!p.Vivo || p.Tipo == TipoDeProjetil.Beam || p.Dono == s.Dono || p.Dono == corpo.Id) continue;
			if (Math.Abs(p.Pos.X - corpo.Pos.X) > raio || Math.Abs(p.Pos.Y - corpo.Pos.Y) > raio) continue;
			MandarDecalque(corpo.Zone, Protocol.Decal.Fumaca, p.Pos, Facing.South);   // o `missile(B,src,A)` que o alcanca
			Matar(p, FimDeProjetil.Defletido);
			derrubados++;
		}
		return derrubados;
	}

	/// <summary>O relogio das criaturas soltas: a morte (`mobDeath`), a relacao, a experiencia de luta e o interceptador.</summary>
	private void TickDosCompanheiros(double dt)
	{
		if (_companheiros.Count == 0) return;
		foreach ((int id, CompanheiroSolto s) in _companheiros.ToList())
		{
			if (!_players.TryGetValue(id, out ServerPlayer? corpo)) { _companheiros.Remove(id); continue; }
			if (CriaturaDoSolto(s) is not { } c) { RecolherCompanheiro(id, null); continue; }

			// `mobDeath()`: deixa um punhado de zeni no chao, e o corpo some. A capsula continua guardando a criatura.
			if (corpo.Ficha.dead)
			{
				// (2026-10-21) O ALIADO NAO VOLTA: ele morre como o habitante que era -- ver `MorreuOAliado` (`GameServer.Aliados.cs`).
				if (s.Aliado != null) { MorreuOAliado(id, corpo, s); continue; }
				PorNoChao(corpo.Zone, corpo.Pos, TipoDeLargado.Zeni, "", _rng.Next(Companheiros.ZeniMinDaMorte, Companheiros.ZeniMaxDaMorte + 1));
				RecolherCompanheiro(id, $"{c.Nome} foi morta. A cápsula ainda guarda a forma dela: é só soltá-la de novo.");
				continue;
			}

			s.RelogioDeRelacao += dt;
			if (s.RelogioDeRelacao >= Companheiros.SegundosPorRelacao)
			{
				s.RelogioDeRelacao -= Companheiros.SegundosPorRelacao;
				c.Relacao++;
				_cofreSujo = true;
			}

			// `if(mob_reference.attacking) if(prob(50)) gain_exp(1)`, de meio em meio segundo: um ponto por segundo de luta.
			if (s.Presa != 0 && corpo.Combate is { EmCombate: > 0 })
			{
				s.RelogioDeExp += dt;
				while (s.RelogioDeExp >= 1) { s.RelogioDeExp -= 1; DarExperiencia(s, c, 1); }
			}

			if (c.Especie == Companheiros.DoInterceptador.Nome && !corpo.Ficha.KO)
			{
				s.RelogioDoInterceptador += dt;
				if (s.RelogioDoInterceptador >= Companheiros.SegundosDoInterceptador)
				{
					s.RelogioDoInterceptador -= Companheiros.SegundosDoInterceptador;
					DerrubarOsDisparos(corpo, s);
				}
			}
		}
	}

	/// <summary>Quem saiu do mundo leva as criaturas dele pra capsula -- e a criatura que saiu deixa de estar solta.</summary>
	private void EsquecerCompanheiros(int id)
	{
		_companheiros.Remove(id);
		foreach (int solto in _companheiros.Where(x => x.Value.Dono == id).Select(x => x.Key).ToList()) RecolherCompanheiro(solto, null);
	}

	// =====================================================================
	// A FABRICACAO: a capsula de Saibaman pede uma semente
	// =====================================================================
	/// <summary>
	/// Por que este item nao se fabrica agora, ou nulo. `Saibaman_Capsule.Click()`: `var/obj/items/food/Seed/s =
	/// locate(/obj/items/food/Seed) in usr.contents; if(s) { if(..()) usr.contents -= s } else return` -- la, calado.
	/// </summary>
	private static string? PorQueNaoFabrica(ServerPlayer pl, string tipo) =>
		tipo == Companheiros.CapsulaDeSaibaman && pl.Mochila.Quantos(CatalogoDeItens.SementeDeMaca) <= 0
			? "a cápsula de Saibaman pede uma semente na mochila: é dela que ele brota."
			: null;
}
