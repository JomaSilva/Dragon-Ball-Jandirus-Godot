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
/// (v0.2.1, lote T3 da tech, segunda metade) O SIMULADOR, O RECRIADOR, A MAQUINA DE CLONAGEM E O BARCO. As regras
/// de cada um moram em `Core/Tech/MaquinasGrandes.cs`, com o que o original faz e o que muda aqui.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os verbos de maquina desta metade do lote (o menu da tecla E). Falso quando o verbo nao e daqui.</summary>
	private bool ComandoDeMaquinaGrande(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case Simulador.VerboIniciar: IniciarSimulacao(pl); return true;
			case Simulador.VerboInfo: InfoDoSimulador(pl); return true;
			case Simulador.VerboMelhorar: MelhorarOSimulador(pl, arg); return true;
			case MaquinaDeClonagem.VerboProgramar: ProgramarAClonagem(pl, arg); return true;
			case MaquinaDeClonagem.VerboInfo: InfoDaClonagem(pl); return true;
			case MaquinaDeClonagem.VerboMelhorar: MelhorarAClonagem(pl, arg); return true;
			case MaquinaDeClonagem.VerboSenha: PorSenhaNaClonagem(pl, arg); return true;
			// (lote T4) o teleporte e a vigilancia -- `GameServer.Comunicacoes.cs`.
			default: return ComandoDeComunicacao(pl, cmd, arg);
		}
	}

	/// <summary>As acoes de mochila desta metade do lote. Falso quando a acao nao e daqui.</summary>
	private bool AcaoDeMaquinaGrande(ServerPlayer pl, ItemDef def, string acao, string arg2)
	{
		switch (acao)
		{
			case Recriador.AcaoGravar: GravarAPlanta(pl, def); return true;
			case Recriador.AcaoRecriar: RecriarAPlanta(pl, def, arg2); return true;
			case Recriador.AcaoInfo: InfoDaPlanta(pl, def); return true;
			case Barco.AcaoVelejar: AlternarVela(pl); return true;
			// (lote T4) o teleporte e a vigilancia -- `GameServer.Comunicacoes.cs`.
			default: return AcaoDeComunicacao(pl, def, acao, arg2);
		}
	}

	// =====================================================================
	// O SIMULADOR -- `Tier2.dm:61-182`. A regra esta em `Simulador`.
	// =====================================================================
	/// <summary>As simulacoes de pe: o corpo -> quem a pediu.</summary>
	private readonly Dictionary<int, int> _simulacoes = [];

	/// <summary>O estado deste simulador; o recem-assentado nasce com o teto e a energia de fabrica.</summary>
	private static EstadoDeTech EstadoDoSimulador(Obra o)
	{
		if (o.Tech is { } ja && ja.N.ContainsKey(Simulador.ChaveTeto)) return ja;
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		e.N[Simulador.ChaveTeto] = Simulador.TetoDeFabrica;
		e.N[Simulador.ChaveEnergia] = e.N[Simulador.ChaveEnergiaMax] = Simulador.EnergiaDeFabrica;
		return e;
	}

	/// <summary>`verb/Start()`: ergue a simulacao -- ou cancela a que este jogador ja tem de pe.</summary>
	private void IniciarSimulacao(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, Simulador.VerboIniciar);
		if (o == null) { Avisar(pl, "não há simulador por perto."); return; }

		// `for(var/mob/M in NPC_list) if(M.name=="[usr] Simulation") { M.deleteMe(); "Simulations cancelled." }`.
		List<int> minhas = [.. _simulacoes.Where(s => s.Value == pl.Id).Select(s => s.Key)];
		if (minhas.Count > 0)
		{
			foreach (int id in minhas) DesfazerSimulacao(id, null);
			Avisar(pl, "simulação cancelada.");
			return;
		}

		if (pl.Ficha.KO || CaidoPelaMorte(pl)) { Avisar(pl, "caído, você não treina com ninguém."); return; }
		EstadoDeTech e = EstadoDoSimulador(o);
		// `if(Energy>=0.1)`. (com folga de arredondamento: dez simulacoes de 0,1 somam 0,9999...)
		if (e.Num(Simulador.ChaveEnergia) < Simulador.EnergiaPorSimulacao - 1e-9)
		{ Avisar(pl, $"{NomeDaObra(o)}: sem energia. A expansão de energia a enche de novo."); return; }

		e.N[Simulador.ChaveEnergia] = Math.Max(e.Num(Simulador.ChaveEnergia) - Simulador.EnergiaPorSimulacao, 0);
		ServerPlayer sim = CriarSimulacao(pl, o, e.Num(Simulador.ChaveTeto));
		_simulacoes[sim.Id] = pl.Id;
		GravarMundo();
		AnunciarAVista(o, $"{NomeDaObra(o)} ergue uma simulação de {NomeVisivel(pl)}.");
		GD.Print($"[server] {pl.Name} iniciou uma simulacao (BP {sim.Ficha.BP:0})");
	}

	/// <summary>
	/// O CORPO DA SIMULACAO -- o `mob/npc/Enemy/Simulation` do `Start()` (`Tier2.dm:147-168`): a cara de quem pediu,
	/// o poder dele ate o teto da maquina, e os atributos EFETIVOS dele como base. O molde do nascimento e o da copia
	/// do Split Form (`CriarSplitformG12`): um corpo sem dono, com o cerebro de fabrica e o golpe nao-letal.
	/// </summary>
	private ServerPlayer CriarSimulacao(ServerPlayer dono, Obra maquina, double teto)
	{
		Fighter d = dono.Ficha;
		// `x=new(loc)` e `spawn step(x,SOUTH)`: um tile ao sul da maquina -- ou ao lado do dono, se ali nao cabe.
		var onde = new Vec2(maquina.X, maquina.Y + ZoneCollision.TileSize);
		if (MapaDaZonaOuCatalogo(dono.Zone) is { } mapa && mapa.BlockedAt(onde + new Vec2(0, MoveRules.FeetOffsetY)))
			onde = dono.Pos + MeleeArea.Frente(dono.Facing) * ZoneCollision.TileSize;

		var sim = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = dono.Name + " Simulation",      // `x.name="[usr.name] Simulation"`
			Zone = dono.Zone,
			Pos = onde,
			Facing = Facing.South,
			Race = dono.Race,
			Class = dono.Class,
			Genero = dono.Genero,
			Planeta = dono.Planeta,
			Idade = dono.Idade,
			Visual = dono.Visual.Copiar(),         // `x.icon=usr.icon`, `x.overlays=usr.overlays`
			LastInputMs = NowMs(),
			// O CEREBRO DE FABRICA, E AQUI ELE E O LITERAL (o argumento da copia do Split Form, `CriarSplitformG12`): a
			// simulacao e um `mob/npc/Enemy/Simulation`, que nao declara tempero nenhum e herda os do `/mob/npc`
			// (`NPCAI.dm:62-76`) -- os defaults do `Cerebro`. Ela NAO e posse: nenhum corpo de jogador recebe este
			// cerebro. A `--iateste` (d) tem este arquivo na lista dela com este argumento.
			Cerebro = new Cerebro(),
			Ficha = new Fighter(),
			Livro = new SkillBook(),
		};
		Fighter c = sim.Ficha;
		c.Race = d.Race; c.Class = d.Class; c.Idade = d.Idade;
		// `x.BP=usr.BP` e `if(x.BP>maxbp) x.BP=maxbp`.
		c.BP = Simulador.SemTetoDeTeste ? Math.Max(d.BP, 1) : Simulador.PoderDaSimulacao(d.BP, teto);
		c.BPMod = d.BPMod;
		// `x.kidef = usr.Ekidef`, `x.physdef=usr.Ephysdef + 1`, `x.speed=usr.Espeed`, `x.physoff=usr.Ephysoff + 1`,
		// `x.technique =usr.Etechnique`: os EFETIVOS de quem pediu viram a base dela (os `Mod` ficam em 1).
		c.kidef = d.Ekidef; c.physdef = d.Ephysdef + 1; c.speed = d.Espeed; c.physoff = d.Ephysoff + 1; c.technique = d.Etechnique;
		c.HP = 100;   // `x.HP=100`
		c.KO = false; c.dead = false;
		c.maxstamina = d.maxstamina; c.stamina = d.maxstamina;
		// (2026-10-21) A MAESTRIA DE GRAVIDADE VAI JUNTO, antes do Statify e do PorNoMundo. O clone nascia com 1
		// (`Fighter.GravMastered`) e o berco so SOBE (`AclimatarAoBerco`): num campo que o dono aguenta, a simulacao
		// era esmagada, presa, e o `GuiarASimulacao` dizia que voce a derrotou. O droide ja copiava (`CriarDroide`).
		if (!SimulacaoSemAMaestriaDeTeste)
		{
			c.GravMastered = d.GravMastered;
		}
		c.Statify();
		c.Ki = c.MaxKi;
		c.PowerLevel();

		PorNoMundo(sim);
		sim.Combate.Letal = false;
		TrocarAparencias(sim);
		return sim;
	}

	/// <summary>
	/// QUEM A SIMULACAO CACA (o ramo dela no `TicarUmCorpo`): quem a pediu, e so ele (`x.foundTarget(usr)`). Ela se
	/// desfaz quando cai, e quando o dono cai, sai da zona ou sai do jogo. Devolve falso quando nao ha o que dirigir.
	/// </summary>
	private bool GuiarASimulacao(ServerPlayer sim, int idDoDono, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = sim.Pos;
		_players.TryGetValue(idDoDono, out ServerPlayer? dono);
		if (dono == null || dono.Zone.Hash != sim.Zone.Hash)
		{
			DesfazerSimulacao(sim.Id, null);
			return false;
		}
		if (sim.Ficha.dead || sim.Ficha.KO)
		{
			DesfazerSimulacao(sim.Id, "você derrota a simulação: ela se desfaz.");
			return false;
		}
		if (dono.Ficha.dead || dono.Ficha.KO)
		{
			DesfazerSimulacao(sim.Id, "a simulação se encerra.");
			return false;
		}
		presa = dono;
		destino = dono.Pos;
		return true;
	}

	private void DesfazerSimulacao(int id, string? avisoAoDono)
	{
		if (!_simulacoes.Remove(id, out int idDoDono)) return;
		if (_players.TryGetValue(id, out ServerPlayer? sim)) RemoverNpc(sim);
		if (avisoAoDono != null && _players.TryGetValue(idDoDono, out ServerPlayer? dono)) Avisar(dono, avisoAoDono);
	}

	private void InfoDoSimulador(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, Simulador.VerboInfo);
		if (o == null) { Avisar(pl, "não há simulador por perto."); return; }
		EstadoDeTech e = EstadoDoSimulador(o);
		// O TETO E UM NUMERO DE PODER: em numero so com medidor (a regra principal do BP -- `PoderEscrito`).
		string teto = $"poder de até {PoderEscrito(pl, e.Num(Simulador.ChaveTeto))}";
		Avisar(pl, $"{NomeDaObra(o)} -- ergue uma cópia sua pra você derrotar, com {teto}; energia "
				   + $"{e.Num(Simulador.ChaveEnergia) * 100:0} de {e.Num(Simulador.ChaveEnergiaMax) * 100:0} (cada simulação gasta {Simulador.EnergiaPorSimulacao * 100:0}).");
		Avisar(pl, $"melhorias: dobrar o teto de poder por {Recurso.DoisPrecos(Simulador.CustoDoPoder(e.Num(Simulador.ChaveTeto)))}; "
				   + $"expandir (e encher) a energia por {Recurso.DoisPrecos(Simulador.CustoDaEnergia(e.Num(Simulador.ChaveEnergiaMax)))}.");
		// (2026-10-19) O TETO DE QUEM PERGUNTA (`TetoPorTecnologia`), em DOBRAS e nao em poder -- ver `DobrasDoSimulador`.
		if (!TetoPorTecnologia.Livre(pl.Ficha.techskill))
			Avisar(pl, $"a sua tecnologia dobra o teto de poder até {TetoPorTecnologia.DobrasDoSimulador(pl.Ficha.techskill)} vez(es) "
					   + $"(esta já dobrou {DobrasDoTetoDoSimulador(e)}) e leva a energia até {TetoPorTecnologia.EnergiaDoSimulador(pl.Ficha.techskill) * 100:0}.");
	}

	/// <summary>Quantas vezes o teto de poder deste simulador ja dobrou desde os 500 de fabrica.</summary>
	private static int DobrasDoTetoDoSimulador(EstadoDeTech e) =>
		(int)Math.Round(Math.Log2(Math.Max(e.Num(Simulador.ChaveTeto), Simulador.TetoDeFabrica) / Simulador.TetoDeFabrica));

	/// <summary>`verb/Upgrade()`: o teto de poder e a energia -- em zeni ou (o dobro) em recursos.</summary>
	private void MelhorarOSimulador(ServerPlayer pl, string arg)
	{
		Obra? o = ObraQueAceita(pl, Simulador.VerboMelhorar);
		if (o == null) { Avisar(pl, "não há simulador por perto."); return; }
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não melhora nada."); return; }
		(string oQue, Moeda moeda) = Recurso.LerPedidoPago(arg);
		EstadoDeTech e = EstadoDoSimulador(o);
		// (2026-10-19) O TETO DE QUEM PAGA (`TetoPorTecnologia`): a recusa vem antes da cobranca.
		double tech = pl.Ficha.techskill;
		switch (oQue)
		{
			case Simulador.Poder:
				int dobras = TetoPorTecnologia.DobrasDoSimulador(tech);
				if (DobrasDoTetoDoSimulador(e) >= dobras)
				{ Avisar(pl, TetoPorTecnologia.Recusa("o teto de poder do simulador", tech, $"{dobras} dobra(s)", proporcional: false)); return; }
				if (!CobrarOuExplicar(pl, Simulador.CustoDoPoder(e.Num(Simulador.ChaveTeto)), moeda, "o teto de poder do simulador")) return;
				e.N[Simulador.ChaveTeto] = e.Num(Simulador.ChaveTeto) * 2;   // `maxbp*=2`
				Avisar(pl, "o poder da simulação aumentou.");
				break;
			case Simulador.Energia:
				int energia = TetoPorTecnologia.EnergiaDoSimulador(tech);
				if (e.Num(Simulador.ChaveEnergiaMax) >= energia)
				{ Avisar(pl, TetoPorTecnologia.Recusa("a energia do simulador", tech, $"{(double)energia * 100:0}", proporcional: false)); return; }
				if (!CobrarOuExplicar(pl, Simulador.CustoDaEnergia(e.Num(Simulador.ChaveEnergiaMax)), moeda, "a energia do simulador")) return;
				e.N[Simulador.ChaveEnergiaMax] = e.Num(Simulador.ChaveEnergiaMax) + 1;   // `MaxEnergy+=1; Energy=MaxEnergy`
				e.N[Simulador.ChaveEnergia] = e.Num(Simulador.ChaveEnergiaMax);
				Avisar(pl, "núcleo expandido: a energia do simulador está cheia.");
				break;
			default:
				Avisar(pl, "o simulador não tem essa melhoria.");
				return;
		}
		GravarMundo();
	}

	// =====================================================================
	// O RECRIADOR -- `buildable.dm:225-460`. A regra esta em `Recriador`.
	// =====================================================================
	private Planta? PlantaDo(string id) => _cofreDeTech.Plantas.GetValueOrDefault(NumeroDoCofre(id));

	/// <summary>Grava a planta dos blocos erguidos de quem pede, num quadrado de 30 por 30 em volta dele.</summary>
	private void GravarAPlanta(ServerPlayer pl, ItemDef def)
	{
		if (pl.Conta.Length == 0) return;
		(int cx, int cy) = CelulaDoPonto(pl.Pos);
		var pecas = new List<PecaDaPlanta>();
		if (_bases.TryGetValue(pl.Zone.Hash, out BaseDaZona? z))
			foreach (BlocoDePe b in z.Celulas.Values)
				if (EDonoDoBloco(b, pl) && Math.Abs(b.X - cx) <= Recriador.MeioLado && Math.Abs(b.Y - cy) <= Recriador.MeioLado)
					pecas.Add(new PecaDaPlanta { X = b.X, Y = b.Y, Tipo = b.Tipo, Senha = b.Senha });
		if (pecas.Count == 0) { Avisar(pl, "não há nenhum bloco SEU erguido por perto pra gravar."); return; }

		int numero = NumeroDoCofre(def.Id);
		if (numero == 0) numero = _cofreDeTech.Proximo++;
		if (!TrocarItem(pl, def.Id, TechDeBolso.Com(Recriador.Id, numero.ToString()))) { Avisar(pl, "sua mochila está cheia."); return; }
		_cofreDeTech.Plantas[numero] = new Planta
		{
			ZonaTipo = pl.Zone.Kind, ZonaNome = pl.Zone.Name, ZonaSeed = pl.Zone.Seed, Conta = pl.Conta, Pecas = pecas,
		};
		GravarOCofreDeTech();
		Avisar(pl, $"planta gravada: {pecas.Count} bloco(s) seus, num quadrado de {Recriador.MeioLado * 2} por {Recriador.MeioLado * 2} em volta daqui. "
				   + $"Recriar ergue de novo o que cair, por {Recurso.DoisPrecos(Recriador.CustoPorBloco)} cada.");
	}

	private void InfoDaPlanta(ServerPlayer pl, ItemDef def)
	{
		if (PlantaDo(def.Id) is not { } planta) { Avisar(pl, "este recriador ainda não tem planta gravada."); return; }
		int faltam = planta.Pecas.Count(p => BlocoEm(new ZoneKey(planta.ZonaTipo, planta.ZonaNome, planta.ZonaSeed), p.X, p.Y) == null);
		Avisar(pl, $"planta de {planta.Pecas.Count} bloco(s) em {planta.ZonaNome}; faltam {faltam} de pé "
				   + $"(recriar tudo custaria {Recurso.DoisPrecos(faltam * Recriador.CustoPorBloco)}).");
	}

	/// <summary>
	/// Ergue de novo o que falta da planta, onde o terreno ainda aceita -- as mesmas perguntas do `RecusaDeErguer`,
	/// menos a do alcance (a planta foi gravada ali) -- e cobra por bloco que SOBE.
	/// </summary>
	private void RecriarAPlanta(ServerPlayer pl, ItemDef def, string arg2)
	{
		if (PlantaDo(def.Id) is not { } planta) { Avisar(pl, "este recriador ainda não tem planta gravada."); return; }
		if (PorQueNaoConstroi(pl) is { Length: > 0 } nao) { Avisar(pl, nao); return; }
		var zona = new ZoneKey(planta.ZonaTipo, planta.ZonaNome, planta.ZonaSeed);
		if (!zona.Equals(pl.Zone)) { Avisar(pl, $"esta planta é de {planta.ZonaNome}: recrie lá."); return; }
		if (!string.Equals(planta.Conta, pl.Conta, StringComparison.OrdinalIgnoreCase)) { Avisar(pl, "esta planta não é sua."); return; }
		(_, Moeda moeda) = Recurso.LerPedidoPago(arg2);

		BaseDaZona z = BaseDe(zona);
		ZoneCollision? mapa = AssentarBase(z);
		_cenarioCaido.TryGetValue(ChaveDeEstado(zona), out HashSet<(int X, int Y)>? caidas);
		Fighter f = pl.Ficha;
		double resistencia = Math.Max(Empurrao.ResistenciaPadrao, TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod));
		const int t = ZoneCollision.TileSize;

		int subiram = 0, recusados = 0;
		bool semDinheiro = false;
		foreach (PecaDaPlanta peca in planta.Pecas)
		{
			if (BlocoEm(zona, peca.X, peca.Y) != null) continue;                     // ja esta de pe
			if (_blocos?.Get(peca.Tipo) is not { } bloco) { recusados++; continue; }
			// (o corpo de mentira no centro da celula: a pergunta do alcance nao e desta ferramenta)
			var centro = new Vec2(peca.X * t + t / 2f, peca.Y * t + t / 2f - MoveRules.FeetOffsetY);
			if (RegrasDeBloco.DoTerreno(mapa, centro, peca.X, peca.Y, caidas?.Contains((peca.X, peca.Y)) == true) != RecusaDeBloco.Pode)
			{ recusados++; continue; }
			if (bloco.Classe != ClasseDeBloco.Piso
				&& (ObraNaCelula(zona, peca.X, peca.Y) != null || NaveNaCelula(zona, peca.X, peca.Y) != null
					|| ZoneList(zona.Hash).Any(o => RegrasDeBloco.CorpoNaCelula(o.Pos, peca.X, peca.Y))))
			{ recusados++; continue; }
			if (_blocosPorConta.GetValueOrDefault(pl.Conta) >= RegrasDeBloco.TetoPorConta) { recusados++; continue; }

			if (!Recriador.DeGracaDeTeste && !Cobrar(pl, Recriador.CustoPorBloco, moeda)) { semDinheiro = true; break; }

			var novo = new BlocoDePe
			{
				Tipo = bloco.Id, Def = bloco, ZonaTipo = zona.Kind, ZonaNome = zona.Name, ZonaSeed = zona.Seed,
				X = peca.X, Y = peca.Y, DonoConta = pl.Conta, DonoNome = pl.Name, Resistencia = resistencia,
				Pago = Recriador.CustoPorBloco, PagoEm = (byte)moeda,
				Senha = bloco.Classe == ClasseDeBloco.Porta ? peca.Senha : "",
			};
			GuardarBloco(novo);
			RefazerNoMapa(z, novo);
			AnunciarBloco(z, novo);
			subiram++;
		}
		if (subiram > 0) _basesSujas = true;

		Avisar(pl, subiram == 0 && !semDinheiro && recusados == 0
			? "a planta está inteira: não há nada pra recriar."
			: $"{subiram} bloco(s) recriado(s)"
			  + (recusados > 0 ? $"; {recusados} não couberam (o terreno, uma máquina ou alguém no lugar)" : "")
			  + (semDinheiro ? $"; o dinheiro acabou no meio ({Recurso.DoisPrecos(Recriador.CustoPorBloco)} por bloco)" : "") + ".");
	}

	// =====================================================================
	// A MAQUINA DE CLONAGEM -- `Cloning Machine.dm`. A regra esta em `MaquinaDeClonagem`.
	// =====================================================================
	private static EstadoDeTech EstadoDaClonagem(Obra o)
	{
		if (o.Tech is { } ja && ja.N.ContainsKey(MaquinaDeClonagem.ChaveVelocidade)) return ja;
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		e.N[MaquinaDeClonagem.ChaveEnergia] = e.N[MaquinaDeClonagem.ChaveEnergiaMax] = MaquinaDeClonagem.EnergiaDeFabrica;
		e.N[MaquinaDeClonagem.ChaveVelocidade] = e.N[MaquinaDeClonagem.ChaveFalha] = 1;
		return e;
	}

	/// <summary>`verb/Program()`: a maquina passa a vigiar quem a programa. Com senha posta, so quem a sabe.</summary>
	private void ProgramarAClonagem(ServerPlayer pl, string senha)
	{
		Obra? o = ObraQueAceita(pl, MaquinaDeClonagem.VerboProgramar);
		if (o == null) { Avisar(pl, "não há máquina de clonagem por perto."); return; }
		if (pl.Ficha.dead || EhCadaver(pl)) { Avisar(pl, "os mortos não programam nada."); return; }
		EstadoDeTech e = EstadoDaClonagem(o);
		// `if(Guess!=Password) "Access denied."`.
		if (e.Txt(MaquinaDeClonagem.ChaveSenha) is { Length: > 0 } posta && posta != TechDeBolso.Limpo(senha, MaquinaDeClonagem.MaxDeSenha))
		{ Avisar(pl, "acesso negado."); return; }

		e.S[MaquinaDeClonagem.ChaveConta] = pl.Conta;
		e.S[MaquinaDeClonagem.ChaveNome] = pl.Name;
		e.N[MaquinaDeClonagem.ChaveSessaoAte] = 0;
		GravarMundo();
		// `"Reset to clone [usr] if fatality occurs."`.
		Avisar(pl, $"{NomeDaObra(o)} programada: se você morrer, ela tenta trazer você de volta ("
				   + $"{MaquinaDeClonagem.SegundosDaSessao(e.Num(MaquinaDeClonagem.ChaveVelocidade)) / 60:0.#} min por sessão, "
				   + $"{MaquinaDeClonagem.ChanceDeFalhar(e.Num(MaquinaDeClonagem.ChaveFalha)):0}% de chance de falhar, um ponto de energia por tentativa).");
	}

	private void InfoDaClonagem(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, MaquinaDeClonagem.VerboInfo);
		if (o == null) { Avisar(pl, "não há máquina de clonagem por perto."); return; }
		EstadoDeTech e = EstadoDaClonagem(o);
		double velocidade = e.Num(MaquinaDeClonagem.ChaveVelocidade), falha = e.Num(MaquinaDeClonagem.ChaveFalha);
		string dono = e.Txt(MaquinaDeClonagem.ChaveNome);
		Avisar(pl, $"{NomeDaObra(o)} -- energia {e.Num(MaquinaDeClonagem.ChaveEnergia):0} de {e.Num(MaquinaDeClonagem.ChaveEnergiaMax):0}; "
				   + $"sessão de {MaquinaDeClonagem.SegundosDaSessao(velocidade) / 60:0.#} min; {MaquinaDeClonagem.ChanceDeFalhar(falha):0}% de chance de falhar; "
				   + (dono.Length > 0 ? $"vigia {dono}" : "sem ninguém programado")
				   + (e.Txt(MaquinaDeClonagem.ChaveSenha).Length > 0 ? "; com senha." : "; sem senha."));
		Avisar(pl, $"melhorias: sessão mais curta por {Recurso.DoisPrecos(MaquinaDeClonagem.CustoDoTempo(velocidade))}; "
				   + $"menos falha por {Recurso.DoisPrecos(MaquinaDeClonagem.CustoDaFalha(falha))}; "
				   + $"bateria (+1, e enche) por {Recurso.DoisPrecos(MaquinaDeClonagem.CustoDaBateria(e.Num(MaquinaDeClonagem.ChaveEnergiaMax)))}; "
				   + $"senha por {MaquinaDeClonagem.CustoDaSenha:N0} zeni.");
		// (2026-10-19) O TETO DE QUEM PERGUNTA (`TetoPorTecnologia`).
		double tech = pl.Ficha.techskill;
		if (!TetoPorTecnologia.Livre(tech))
			Avisar(pl, $"a sua tecnologia leva a sessão mais curta até o nível {TetoPorTecnologia.VelocidadeDaClonagem(tech)}, "
					   + $"a menos falha até o nível {TetoPorTecnologia.FalhaDaClonagem(tech)} e a bateria até {TetoPorTecnologia.BateriaDaClonagem(tech)}.");
	}

	/// <summary>`verb/Upgrade()`: o tempo, a falha e a bateria -- em zeni ou (o dobro) em recursos.</summary>
	private void MelhorarAClonagem(ServerPlayer pl, string arg)
	{
		Obra? o = ObraQueAceita(pl, MaquinaDeClonagem.VerboMelhorar);
		if (o == null) { Avisar(pl, "não há máquina de clonagem por perto."); return; }
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não melhora nada."); return; }
		(string oQue, Moeda moeda) = Recurso.LerPedidoPago(arg);
		EstadoDeTech e = EstadoDaClonagem(o);
		// (2026-10-19) O TETO DE QUEM PAGA (`TetoPorTecnologia`): a recusa vem antes da cobranca.
		double tech = pl.Ficha.techskill;
		switch (oQue)
		{
			case MaquinaDeClonagem.Tempo:
				int tempo = TetoPorTecnologia.VelocidadeDaClonagem(tech);
				if (e.Num(MaquinaDeClonagem.ChaveVelocidade) >= tempo)
				{ Avisar(pl, TetoPorTecnologia.Recusa("a sessão mais curta", tech, $"nível {tempo}", proporcional: false)); return; }
				if (!CobrarOuExplicar(pl, MaquinaDeClonagem.CustoDoTempo(e.Num(MaquinaDeClonagem.ChaveVelocidade)), moeda, "a sessão mais curta")) return;
				e.N[MaquinaDeClonagem.ChaveVelocidade] = e.Num(MaquinaDeClonagem.ChaveVelocidade) + 1;
				Avisar(pl, "o tempo de clonagem diminuiu.");
				break;
			case MaquinaDeClonagem.Falha:
				int falha = TetoPorTecnologia.FalhaDaClonagem(tech);
				if (e.Num(MaquinaDeClonagem.ChaveFalha) >= falha)
				{ Avisar(pl, TetoPorTecnologia.Recusa("a menos falha", tech, $"nível {falha}", proporcional: false)); return; }
				if (!CobrarOuExplicar(pl, MaquinaDeClonagem.CustoDaFalha(e.Num(MaquinaDeClonagem.ChaveFalha)), moeda, "a chance de falha menor")) return;
				e.N[MaquinaDeClonagem.ChaveFalha] = e.Num(MaquinaDeClonagem.ChaveFalha) + 1;
				Avisar(pl, "a chance de falha diminuiu.");
				break;
			case MaquinaDeClonagem.Bateria:
				int bateria = TetoPorTecnologia.BateriaDaClonagem(tech);
				if (e.Num(MaquinaDeClonagem.ChaveEnergiaMax) >= bateria)
				{ Avisar(pl, TetoPorTecnologia.Recusa("a bateria da máquina", tech, $"{bateria} de energia", proporcional: false)); return; }
				if (!CobrarOuExplicar(pl, MaquinaDeClonagem.CustoDaBateria(e.Num(MaquinaDeClonagem.ChaveEnergiaMax)), moeda, "a bateria da máquina")) return;
				e.N[MaquinaDeClonagem.ChaveEnergiaMax] = e.Num(MaquinaDeClonagem.ChaveEnergiaMax) + 1;
				e.N[MaquinaDeClonagem.ChaveEnergia] = e.Num(MaquinaDeClonagem.ChaveEnergiaMax);
				Avisar(pl, "bateria expandida: a energia da máquina está cheia.");
				break;
			default:
				Avisar(pl, "a máquina não tem essa melhoria.");
				return;
		}
		GravarMundo();
	}

	/// <summary>"Security Password (1000z)": uma vez posta, so quem a sabe reprograma a maquina. So em zeni: nao e melhoria de potencia.</summary>
	private void PorSenhaNaClonagem(ServerPlayer pl, string crua)
	{
		Obra? o = ObraQueAceita(pl, MaquinaDeClonagem.VerboSenha);
		if (o == null) { Avisar(pl, "não há máquina de clonagem por perto."); return; }
		EstadoDeTech e = EstadoDaClonagem(o);
		if (e.Txt(MaquinaDeClonagem.ChaveSenha).Length > 0) { Avisar(pl, "ela já tem senha, e a senha é permanente."); return; }
		if (!EDonoDaObra(o, pl)) { Avisar(pl, $"só o dono põe senha em {NomeDaObra(o)}."); return; }
		string senha = TechDeBolso.Limpo(crua, MaquinaDeClonagem.MaxDeSenha);
		if (senha.Length == 0) { Avisar(pl, "a senha não pode ser vazia."); return; }
		if (!CobrarOuExplicar(pl, MaquinaDeClonagem.CustoDaSenha, Moeda.Zeni, "a senha da máquina")) return;
		e.S[MaquinaDeClonagem.ChaveSenha] = senha;
		GravarMundo();
		Avisar(pl, "senha posta: só quem a souber reprograma a máquina.");
	}

	/// <summary>
	/// A VIGIA (`New()`: `while(src) ... sleep(100)`), de dez em dez segundos: a maquina com alguem programado, morto
	/// e conectado, abre uma sessao; a sessao que venceu e sorteada -- falha, ou o morto volta ao lado dela.
	/// </summary>
	private void TickDasClonagens()
	{
		long agora = NowMs();
		bool mexeu = false;
		foreach (Obra o in _noChao)
		{
			if (o.Tipo != MaquinaDeClonagem.Tipo || o.Tech is not { } e) continue;
			string conta = e.Txt(MaquinaDeClonagem.ChaveConta);
			if (conta.Length == 0) continue;
			ServerPlayer? alvo = _players.Values.FirstOrDefault(p => p.Peer != null && string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase))
								 ?? (ClonagemSemPeerDeTeste ? _players.Values.FirstOrDefault(p => string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase)) : null);
			double sessaoAte = e.Num(MaquinaDeClonagem.ChaveSessaoAte);

			if (sessaoAte <= 0)
			{
				// `if(A==Initiator&&Energy>=1&&A.dead)`.
				if (alvo is not { Ficha.dead: true } || e.Num(MaquinaDeClonagem.ChaveEnergia) < 1 || MaquinaDeClonagem.NuncaReviveDeTeste) continue;
				double segundos = MaquinaDeClonagem.SegundosDaSessao(e.Num(MaquinaDeClonagem.ChaveVelocidade));
				e.N[MaquinaDeClonagem.ChaveSessaoAte] = agora + (long)(segundos * 1000);
				mexeu = true;
				AnunciarAVista(o, $"{NomeDaObra(o)}: sessão de clonagem ativada. Retorno em {segundos / 60:0.#} minuto(s).");
				Avisar(alvo, $"a sua máquina de clonagem detectou a sua morte e está tentando trazer você de volta: {segundos / 60:0.#} minuto(s).");
				continue;
			}
			if (agora < sessaoAte) continue;

			// A SESSAO VENCEU.
			e.N[MaquinaDeClonagem.ChaveSessaoAte] = 0;
			mexeu = true;
			if (alvo is not { Ficha.dead: true }) continue;   // ele ja voltou por outro caminho, ou saiu do jogo: nada se gasta
			e.N[MaquinaDeClonagem.ChaveEnergia] = Math.Max(e.Num(MaquinaDeClonagem.ChaveEnergia) - 1, 0);
			string resto = $"{e.Num(MaquinaDeClonagem.ChaveEnergia) / Math.Max(e.Num(MaquinaDeClonagem.ChaveEnergiaMax), 1) * 100:0}% de energia restante.";
			if (Sorteio(MaquinaDeClonagem.ChanceDeFalhar(e.Num(MaquinaDeClonagem.ChaveFalha))))
			{
				// `"The cloning process failed."`.
				Avisar(alvo, "o processo de clonagem falhou.");
				AnunciarAVista(o, $"{NomeDaObra(o)}: a clonagem falhou. {resto}");
				continue;
			}

			// `A.loc=locate(x,y,z)`, `A.dir=SOUTH`, `A.ReviveMe()` -- a porta e a do desejo de reviver.
			Ressuscitar(alvo, o.Zona, new Vec2(o.X, o.Y + ZoneCollision.TileSize));
			alvo.Facing = Facing.South;
			AnunciarAVista(o, $"{NomeDaObra(o)}: clonagem concluída. {resto}");
			Avisar(alvo, "você abre os olhos dentro do tanque: a máquina de clonagem trouxe você de volta.");
			GD.Print($"[server] a maquina de clonagem reviveu {alvo.Name} em {o.Zona.Name}");
		}
		if (mexeu) GravarMundo();
	}

	/// <summary>SO PRA BANCADA: a vigia aceita o corpo sem conexao (os corpos da bancada nao tem `Peer`). Falso em jogo, sempre.</summary>
	internal bool ClonagemSemPeerDeTeste;

	// =====================================================================
	// O BARCO -- `Boat.dm`. A regra esta em `Barco`.
	// =====================================================================
	/// <summary>Quem esta velejando agora. So memoria: quem sai do jogo na agua volta nadando.</summary>
	private readonly HashSet<int> _velejando = [];

	private bool Velejando(ServerPlayer pl) => _velejando.Contains(pl.Id);

	/// <summary>Velejar tira o custo do nado? (Tudo, menos com o defeito da bancada ligado.)</summary>
	private bool VelaPoupaONado(ServerPlayer pl) => Velejando(pl) && !Barco.CobraComoNadoDeTeste;

	/// <summary>
	/// `verb/Sail()`: com agua a frente (ou embaixo), zarpa; velejando e no seco, atraca. Em cima da agua nao se
	/// atraca -- a mesma recusa do nado, pelo mesmo motivo (`AlternarNado`).
	/// </summary>
	private void AlternarVela(ServerPlayer pl)
	{
		if (Velejando(pl))
		{
			if (SobreAgua(pl)) { Avisar(pl, "você está em cima da água -- veleje até o seco pra atracar."); return; }
			PararDeNadar(pl, "você atraca.");   // `"You stop sailing."`
			return;
		}
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "não dá pra velejar assim."); return; }
		if (PresoNoTorneio(pl.Id)) { Avisar(pl, "você está na área de espera do torneio: sem velejar até a sua vez."); return; }
		// `"You can only sail in water!"`.
		if (!PodeComecarANadar(pl)) { Avisar(pl, "só dá pra velejar na água: chegue na margem, de frente pra ela."); return; }

		if (pl.Voando)
		{
			pl.Voando = false;
			MandarEfeito(pl, "voo", 0);
		}
		pl.VooRapido = false;
		pl.Nadando = true;
		pl.UltimaDirDoNado = pl.Facing;
		pl.NadoJaMolhou = SobreAgua(pl);
		pl.NadoSecoS = 0f;
		_velejando.Add(pl.Id);
		// `"Anchors away! You set sail!"`.
		Avisar(pl, "âncoras içadas: você zarpa!");
		AnunciarNado(pl);
		ReapresentarOCorpo(pl);
	}

	/// <summary>O nado acabou (o seco, o nocaute, a zona sem agua): quem velejava atracou. Chamado pelo `PararDeNadar`.</summary>
	private void AoPararDeNadar(ServerPlayer pl)
	{
		if (_velejando.Remove(pl.Id)) ReapresentarOCorpo(pl);
	}

	// =====================================================================
	// O RELOGIO DO LOTE, E A SAIDA
	// =====================================================================
	private double _relogioDasClonagens;

	private void TickDasMaquinasGrandes(double dt)
	{
		_relogioDasClonagens += dt;
		if (_relogioDasClonagens < MaquinaDeClonagem.SegundosEntreVigias) return;
		_relogioDasClonagens = 0;
		TickDasClonagens();
	}

	/// <summary>Quem saiu do mundo leva a simulacao dele, e deixa de velejar.</summary>
	private void EsquecerMaquinasGrandes(int id)
	{
		_velejando.Remove(id);
		foreach (int sim in _simulacoes.Where(s => s.Value == id).Select(s => s.Key).ToList()) DesfazerSimulacao(sim, null);
	}
}
