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
/// (v0.2.1, lote T6 da tech) O SUPER COMPUTADOR -- a chave de controle, os droides e as ordens deles, a ressurreicao
/// de androide, a blindagem e o conserto. A regra mora em `Core/Tech/SuperComputador.cs`, com o que o original faz e
/// o que muda aqui. O droide e um corpo sem dono de tela que OBEDECE -- o mesmo desenho da copia do Split Form
/// (`GuiarSplitformG12`), com as mesmas tres funcoes.
/// </summary>
public sealed partial class GameServer
{
	private enum FuncaoDeDroide { Seguir, AtacarOAlvo, AtacarOProximo }

	/// <summary>Um droide DE PE: de quem e, e o que ele esta fazendo.</summary>
	private sealed class Droide
	{
		public int Dono;
		public FuncaoDeDroide Funcao = FuncaoDeDroide.Seguir;
	}

	/// <summary>id do corpo do droide -> o estado dele. So memoria: o droide e da sessao do dono.</summary>
	private readonly Dictionary<int, Droide> _droides = [];

	/// <summary>Quem digitou a chave de qual computador nesta sessao: id do jogador -> ids das obras abertas pra ele.</summary>
	private readonly Dictionary<int, HashSet<int>> _computadoresAbertos = [];

	/// <summary>A chave do estado que guarda o SLOT de quem cifrou (a conta sozinha serviria a outro personagem dela).</summary>
	private const string ChaveDoSlotDoComputador = "slot";

	/// <summary>SO PRA BANCADA: a vigia aceita o corpo sem conexao (os corpos da bancada nao tem `Peer`). Falso em jogo, sempre.</summary>
	internal bool VigiaDoComputadorSemPeerDeTeste;

	/// <summary>Os verbos do super computador (o menu da tecla E). Falso quando o verbo nao e daqui.</summary>
	private bool ComandoDeSuperComputador(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case SuperComputador.VerboCifrar: CifrarOComputador(pl, arg); return true;
			case SuperComputador.VerboChave: DigitarAChave(pl, arg); return true;
			case SuperComputador.VerboDecifrar: DecifrarOComputador(pl); return true;
			case SuperComputador.VerboDroide: FazerUmDroide(pl); return true;
			case SuperComputador.VerboOrdem: OrdenarOsDroides(pl, arg); return true;
			case SuperComputador.VerboMelhorar: MelhorarOComputador(pl, arg); return true;
			case SuperComputador.VerboInfo: InfoDoComputador(pl); return true;
			// (lote T7) a bancada de criacao -- `GameServer.BancadaDeCriacao.cs`.
			default: return ComandoDeBancadaDeCriacao(pl, cmd, arg);
		}
	}

	/// <summary>Este corpo e de androide? A raca, ou um corpo convertido no Android Lab (a mesma pergunta do `GameServer.Tech`).</summary>
	private static bool CorpoDeAndroide(ServerPlayer pl) =>
		pl.Race == "Android" || pl.Ficha.AndroideAbsorcao || pl.Ficha.AndroideInfinito;

	// =====================================================================
	// A CHAVE DE CONTROLE -- `Encrypt()`, o `input("Put in the password.")` do `Use()`, e o `Decrypt`
	// =====================================================================
	/// <summary>
	/// O computador abre pra este jogador? Sem chave, pra qualquer um (`if(!controller || ...)`); com chave, pra quem
	/// a digitou nesta sessao. A recusa e dita aqui (*"You do not know the encryption key."*).
	/// </summary>
	private bool AbreOComputador(ServerPlayer pl, Obra o)
	{
		if ((o.Tech?.Txt(SuperComputador.ChaveDaChave) ?? "").Length == 0 || SuperComputador.AbreSemAChaveDeTeste) return true;
		if (_computadoresAbertos.TryGetValue(pl.Id, out HashSet<int>? abertos) && abertos.Contains(o.Id)) return true;
		Avisar(pl, $"{NomeDaObra(o)} está cifrado, e você não digitou a chave dele.");
		return false;
	}

	/// <summary>A chave mudou (ou saiu): ninguem que a tinha digitado continua dentro.</summary>
	private void FecharAsSessoesDoComputador(Obra o)
	{
		foreach (HashSet<int> abertos in _computadoresAbertos.Values) abertos.Remove(o.Id);
	}

	private void AbrirOComputadorPra(ServerPlayer pl, Obra o)
	{
		if (!_computadoresAbertos.TryGetValue(pl.Id, out HashSet<int>? abertos)) _computadoresAbertos[pl.Id] = abertos = [];
		abertos.Add(o.Id);
	}

	/// <summary>`verb/Encrypt()`: *"Put in the control key. Don't forget this!"* -- e o computador passa a responder por quem a pos.</summary>
	private void CifrarOComputador(ServerPlayer pl, string crua)
	{
		Obra? o = ObraQueAceita(pl, SuperComputador.VerboCifrar);
		if (o == null) { Avisar(pl, "não há super computador por perto."); return; }
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		// `else to_chat(usr, "The Super Computer has already been encrypted.")`.
		if (e.Txt(SuperComputador.ChaveDaChave).Length > 0) { Avisar(pl, "o super computador já está cifrado."); return; }
		if (!EDonoDaObra(o, pl)) { Avisar(pl, $"só o dono cifra {NomeDaObra(o)}."); return; }
		string chave = TechDeBolso.Limpo(crua, SuperComputador.MaxDeChave);
		if (chave.Length == 0) { Avisar(pl, "a chave não pode ser vazia."); return; }

		e.S[SuperComputador.ChaveDaChave] = chave;
		e.S[SuperComputador.ChaveConta] = pl.Conta;
		e.S[SuperComputador.ChaveNome] = pl.Name;
		e.N[ChaveDoSlotDoComputador] = pl.Slot;
		FecharAsSessoesDoComputador(o);
		AbrirOComputadorPra(pl, o);
		GravarMundo();
		Avisar(pl, "chave de controle posta -- não a esqueça: daqui em diante só quem a digitar usa o computador, e ele responde por você.");
	}

	/// <summary>O `input(usr,"Put in the password.")` do `Use()`: a chave certa abre o computador pra sessao de quem a digita.</summary>
	private void DigitarAChave(ServerPlayer pl, string crua)
	{
		Obra? o = ObraQueAceita(pl, SuperComputador.VerboChave);
		if (o == null) { Avisar(pl, "não há super computador por perto."); return; }
		string posta = o.Tech?.Txt(SuperComputador.ChaveDaChave) ?? "";
		if (posta.Length == 0) { Avisar(pl, "este computador não está cifrado: qualquer um o usa."); return; }
		// `else to_chat(usr, "You do not know the encryption key.")`.
		if (posta != TechDeBolso.Limpo(crua, SuperComputador.MaxDeChave)) { Avisar(pl, "você não sabe a chave de controle."); return; }
		AbrirOComputadorPra(pl, o);
		Avisar(pl, "chave aceita: o computador está aberto pra você.");
	}

	/// <summary>"Decrypt": `controller=null` -- a chave sai, e com ela o androide por quem o computador respondia.</summary>
	private void DecifrarOComputador(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, SuperComputador.VerboDecifrar);
		if (o == null) { Avisar(pl, "não há super computador por perto."); return; }
		if (!AbreOComputador(pl, o)) return;
		if (o.Tech is not { } e || e.Txt(SuperComputador.ChaveDaChave).Length == 0) { Avisar(pl, "ele não está cifrado."); return; }
		e.S.Remove(SuperComputador.ChaveDaChave);
		e.S.Remove(SuperComputador.ChaveConta);
		e.S.Remove(SuperComputador.ChaveNome);
		e.N.Remove(ChaveDoSlotDoComputador);
		FecharAsSessoesDoComputador(o);
		GravarMundo();
		Avisar(pl, "computador decifrado: a chave foi apagada, e ele deixa de responder por um androide até ser cifrado de novo.");
	}

	// =====================================================================
	// O DROIDE -- "Make Droid" (`makeCopy(4,"Android","None",/mob/npc/Clone,FALSE)`, `CopyMaker.dm`)
	// =====================================================================
	private List<int> DroidesDe(int dono) => [.. _droides.Where(x => x.Value.Dono == dono).Select(x => x.Key)];

	private void FazerUmDroide(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, SuperComputador.VerboDroide);
		if (o == null) { Avisar(pl, "não há super computador por perto."); return; }
		if (!AbreOComputador(pl, o)) return;
		if (pl.Ficha.KO || pl.Ficha.dead || EhCadaver(pl)) { Avisar(pl, "caído, você não monta nada."); return; }
		if (!SuperComputador.SemTetoDeDroidesDeTeste && DroidesDe(pl.Id).Count >= SuperComputador.MaxDeDroides)
		{ Avisar(pl, $"você já tem {SuperComputador.MaxDeDroides} droides de pé: destrua algum antes (pelo computador)."); return; }

		ServerPlayer droide = CriarDroide(pl);
		AnunciarAVista(o, $"{NomeDaObra(o)} monta um droide pra {NomeVisivel(pl)}.");
		Avisar(pl, $"{droide.Name} sai da linha de montagem e passa a seguir você. As ordens saem do computador.");
		GD.Print($"[server] {pl.Name} montou um droide ({droide.Name})");
	}

	/// <summary>
	/// O CORPO DO DROIDE: um androide de berco (`z.genome = new/datum/genetics/Android`), com a folha do androide
	/// (`z.icon='GochekAndroid.dmi'`), o BP de quem pede (`A.BP=BP`, do `AssignDupeVars` -- o corte de 25% e do ramo
	/// `sentient`, que o droide nao roda) e a gravidade que ele ja domina (`A.GravMastered=GravMastered`). Nasce a
	/// um passo do dono, na direcao em que ele olha (`z.loc=locate(x,y,src.z); step(z,dir)`).
	/// </summary>
	private ServerPlayer CriarDroide(ServerPlayer dono)
	{
		string nome = SuperComputador.NomeDoDroide(dono.Name, _rng.Next(1, SuperComputador.MaxDoModelo + 1));
		Vec2 aFrente = dono.Pos + MeleeArea.Frente(dono.Facing) * ZoneCollision.TileSize;
		Fighter f = _racas != null
			? Jandirus.Core.Races.Birth.Nascer(_racas, SuperComputador.RacaDoDroide, "", _rng, nome)
			: new Fighter { Name = nome, Race = SuperComputador.RacaDoDroide };
		// (o `z.Age=1` de la nao veio: aqui a idade corta o poder de crianca, e o droide nao e uma)
		f.Idade = Jandirus.Core.Races.Envelhecimento.IdadeAdulta;
		f.BP = Math.Max(dono.Ficha.BP, 1);
		f.GravMastered = dono.Ficha.GravMastered;
		f.HP = 100;
		f.KO = false; f.dead = false;
		f.Statify();
		f.Ki = f.MaxKi;
		f.PowerLevel();

		var corpo = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = nome,
			Zone = dono.Zone,
			Pos = PontoLivre(dono.Zone, aFrente) ?? dono.Pos,
			Facing = dono.Facing,
			Race = SuperComputador.CorpoDoDroide,
			Genero = dono.Genero,
			Planeta = dono.Planeta,
			Idade = (int)Jandirus.Core.Races.Envelhecimento.IdadeAdulta,
			Visual = new Jandirus.Core.Appearance.Appearance(),
			LastInputMs = NowMs(),
			// O CEREBRO DE FABRICA, E AQUI ELE E O LITERAL (o argumento da copia do Split Form, `CriarSplitformG12`): o
			// droide e um `mob/npc/Clone` (`Geti Star.dm:9`), que so declara `hasAI=1` e herda o tempero de todo
			// `/mob/npc` (`NPCAI.dm:62-76`) -- os defaults do `Cerebro`. Ele NAO e posse: nenhum corpo de jogador recebe
			// este cerebro, e e um lutador comum que obedece a ordem do dono. A `--iateste` (d) tem este arquivo na
			// lista dela com este argumento.
			Cerebro = new Cerebro(),
			Ficha = f,
			Livro = new SkillBook(),
		};

		PorNoMundo(corpo);
		corpo.Combate.Letal = false;
		TrocarAparencias(corpo);
		_droides[corpo.Id] = new Droide { Dono = dono.Id };
		return corpo;
	}

	/// <summary>O droide sai do mundo (o "Destroy" do clique nele, a morte, ou a saida do dono).</summary>
	private void DesfazerDroide(int idDoCorpo, string? avisoAoDono)
	{
		if (!_droides.Remove(idDoCorpo, out Droide? d)) return;
		if (_players.TryGetValue(idDoCorpo, out ServerPlayer? corpo)) RemoverNpc(corpo);
		if (avisoAoDono != null && _players.TryGetValue(d.Dono, out ServerPlayer? dono)) Avisar(dono, avisoAoDono);
	}

	/// <summary>
	/// AS ORDENS -- o `Click()` do `mob/npc/Clone` (`Geti Star.dm:16-53`): "Follow", "Attack Target", "Attack Nearest"
	/// e "Destroy". Valem pra todos os droides de quem as da.
	/// </summary>
	private void OrdenarOsDroides(ServerPlayer pl, string ordem)
	{
		Obra? o = ObraQueAceita(pl, SuperComputador.VerboOrdem);
		if (o == null) { Avisar(pl, "não há super computador por perto."); return; }
		List<int> meus = DroidesDe(pl.Id);
		if (meus.Count == 0) { Avisar(pl, "você não tem droide de pé: monte um pelo computador."); return; }

		switch (ordem)
		{
			case SuperComputador.Destruir:
				foreach (int id in meus) DesfazerDroide(id, null);
				Avisar(pl, meus.Count == 1 ? "o seu droide foi destruído." : $"os seus {meus.Count} droides foram destruídos.");
				return;
			case SuperComputador.Seguir:
				foreach (int id in meus) _droides[id].Funcao = FuncaoDeDroide.Seguir;
				Avisar(pl, "os seus droides largam o que faziam e voltam a seguir você.");
				return;
			case SuperComputador.AtacarOAlvo:
				if (pl.AlvoId == 0 || !_players.TryGetValue(pl.AlvoId, out ServerPlayer? alvo) || alvo == pl
					|| (_droides.TryGetValue(alvo.Id, out Droide? marcado) && marcado.Dono == pl.Id))
				{ Avisar(pl, "marque um alvo primeiro (duplo clique)."); return; }
				foreach (int id in meus) _droides[id].Funcao = FuncaoDeDroide.AtacarOAlvo;
				Avisar(pl, $"os seus droides partem pra cima de {NomeVisivel(alvo)} -- e de quem você marcar depois.");
				return;
			case SuperComputador.AtacarOProximo:
				foreach (int id in meus) _droides[id].Funcao = FuncaoDeDroide.AtacarOProximo;
				Avisar(pl, $"os seus droides passam a atacar quem estiver mais perto deles, a até {SuperComputador.TilesDoMaisProximo:0} tiles.");
				return;
			default:
				Avisar(pl, "o computador não conhece essa ordem.");
				return;
		}
	}

	/// <summary>
	/// O RAMO DO DROIDE NO `TicarUmCorpo` (`GameServer.Clone.cs`): quem ele persegue e pra onde vai saem da ordem do
	/// dono -- as tres funcoes da copia do Split Form (`GuiarSplitformG12`). Devolve falso quando o corpo nao pensa
	/// neste tique.
	/// </summary>
	private bool GuiarODroide(ServerPlayer corpo, Droide d, out ServerPlayer? presa, out Vec2 destino)
	{
		presa = null;
		destino = corpo.Pos;
		// O DROIDE E DA SESSAO DO DONO -- ver o cabecalho do `SuperComputador`.
		if (!_players.TryGetValue(d.Dono, out ServerPlayer? dono))
		{
			DesfazerDroide(corpo.Id, null);
			return false;
		}
		if (corpo.Ficha.dead || corpo.Ficha.KO) { corpo.Moving = false; return false; }
		// O dono em outra zona: ele espera onde esta.
		if (dono.Zone.Hash != corpo.Zone.Hash) return true;

		switch (SuperComputador.DroideSurdoDeTeste ? FuncaoDeDroide.Seguir : d.Funcao)
		{
			case FuncaoDeDroide.Seguir:
				// `if(d >= 2) step_towards(src,usr)`.
				if (Vec2.Distance(corpo.Pos, dono.Pos) >= SuperComputador.TilesDeFolga * ZoneCollision.TileSize) destino = dono.Pos;
				break;
			case FuncaoDeDroide.AtacarOAlvo:
				// `var/mob/choice = input(usr,"Choose mob") as null|mob in view(usr)`: o alvo MARCADO do dono.
				if (dono.AlvoId != 0 && dono.AlvoId != corpo.Id && _players.TryGetValue(dono.AlvoId, out ServerPlayer? m)
					&& m.Zone.Hash == corpo.Zone.Hash && !m.Ficha.dead && !m.Ficha.KO
					&& !(_droides.TryGetValue(m.Id, out Droide? irmaoMarcado) && irmaoMarcado.Dono == d.Dono))
				{ presa = m; destino = m.Pos; }
				else if (Vec2.Distance(corpo.Pos, dono.Pos) >= SuperComputador.TilesDeFolga * ZoneCollision.TileSize) destino = dono.Pos;
				break;
			case FuncaoDeDroide.AtacarOProximo:
			{
				// `for(var/mob/nE in range(MAX_AGGRO_RANGE,usr)) if(nE!=usr && nE!=src) foundTarget(nE)`.
				float perto = SuperComputador.TilesDoMaisProximo * ZoneCollision.TileSize;
				perto *= perto;
				foreach (ServerPlayer outro in ZoneList(corpo.Zone.Hash))
				{
					if (outro.Id == corpo.Id || outro.Id == dono.Id || outro.Ficha.dead || outro.Ficha.KO || EhCadaver(outro)) continue;
					if (_droides.TryGetValue(outro.Id, out Droide? irmao) && irmao.Dono == d.Dono) continue;
					float d2 = (outro.Pos - corpo.Pos).LengthSquared;
					if (d2 >= perto) continue;
					perto = d2; presa = outro;
				}
				if (presa != null) destino = presa.Pos;
				else if (Vec2.Distance(corpo.Pos, dono.Pos) >= SuperComputador.TilesDeFolga * ZoneCollision.TileSize) destino = dono.Pos;
				break;
			}
		}
		return true;
	}

	// =====================================================================
	// AS MELHORIAS -- "Upgrade": a ressurreicao de androide, a blindagem e o conserto
	// =====================================================================
	private void MelhorarOComputador(ServerPlayer pl, string arg)
	{
		Obra? o = ObraQueAceita(pl, SuperComputador.VerboMelhorar);
		if (o == null) { Avisar(pl, "não há super computador por perto."); return; }
		if (!AbreOComputador(pl, o)) return;
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "caído, você não melhora nada."); return; }
		(string oQue, Moeda moeda) = Recurso.LerPedidoPago(arg);
		EstadoDeTech e = o.Tech ??= new EstadoDeTech();
		switch (oQue)
		{
			case SuperComputador.Ressurreicao:
				if (e.Ligado(SuperComputador.ChaveRessurreicao)) { Avisar(pl, "este computador já tem a ressurreição de androide."); return; }
				if (!CobrarOuExplicar(pl, SuperComputador.CustoDaRessurreicao, moeda, "a ressurreição de androide")) return;
				e.N[SuperComputador.ChaveRessurreicao] = 1;
				Avisar(pl, "ressurreição de androide instalada: quando o androide que cifrou este computador morrer, ele o refaz aqui."
						   + (e.Txt(SuperComputador.ChaveConta).Length == 0 ? " (Ele ainda não está cifrado: cifre-o pra ele saber por quem responder.)" : ""));
				break;
			case SuperComputador.Blindagem:
			{
				// `maxarmor = usr.intBPcap*1.2`. So SOBE -- ver o cabecalho do `SuperComputador`.
				double blindada = SuperComputador.ArmaduraBlindada(TetoDeTecnologia.De(pl.Ficha.relBPmax, pl.Ficha.techskill, pl.Ficha.techmod));
				if (o.ArmaduraMax >= blindada) { Avisar(pl, "a blindagem deste computador já está acima do que a sua tecnologia alcança."); return; }
				if (!CobrarOuExplicar(pl, SuperComputador.CustoDaBlindagem, moeda, "a blindagem do computador")) return;
				o.ArmaduraMax = blindada;
				o.Armadura = blindada;
				Avisar(pl, "blindagem reforçada: o computador aguenta mais do que uma construção sua aguentaria.");
				break;
			}
			case SuperComputador.Conserto:
				// "This takes no zenni": `healDamage(maxarmor)`.
				if (o.Armadura >= o.ArmaduraMax) { Avisar(pl, "o computador está inteiro."); return; }
				o.Armadura = o.ArmaduraMax;
				Avisar(pl, "computador consertado.");
				break;
			default:
				Avisar(pl, "o computador não tem essa melhoria.");
				return;
		}
		GravarMundo();
		// O MENU DE QUEM ESTA NA ZONA FICA SABENDO: a melhoria de uma vez so sai da lista (`MelhoriasFeitas`).
		MandarObras(o.Zona);
	}

	private void InfoDoComputador(ServerPlayer pl)
	{
		Obra? o = ObraQueAceita(pl, SuperComputador.VerboInfo);
		if (o == null) { Avisar(pl, "não há super computador por perto."); return; }
		EstadoDeTech? e = o.Tech;
		bool cifrado = (e?.Txt(SuperComputador.ChaveDaChave) ?? "").Length > 0;
		string responde = e?.Txt(SuperComputador.ChaveNome) ?? "";
		bool ressuscita = e?.Ligado(SuperComputador.ChaveRessurreicao) == true;
		Avisar(pl, $"{NomeDaObra(o)} -- " + (cifrado ? $"cifrado (responde por {responde})" : "sem chave: qualquer um o usa")
				   + $"; armadura {o.Armadura / Math.Max(o.ArmaduraMax, 1e-9) * 100:0}% inteira; "
				   + (ressuscita ? "com a ressurreição de androide" + (cifrado ? "." : " (parada: ele não está cifrado).") : "sem a ressurreição de androide."));
		Avisar(pl, $"droides seus de pé: {DroidesDe(pl.Id).Count} de {SuperComputador.MaxDeDroides}. Melhorias: "
				   + (ressuscita ? "" : $"ressurreição de androide por {Recurso.DoisPrecos(SuperComputador.CustoDaRessurreicao)}; ")
				   + $"blindagem por {Recurso.DoisPrecos(SuperComputador.CustoDaBlindagem)}; conserto de graça.");
	}

	// =====================================================================
	// A RESSURREICAO DE ANDROIDE -- `TestDeathRegen()` (`DeathRegen.dm:12-24`)
	// =====================================================================
	/// <summary>
	/// A VIGIA, de dez em dez segundos: o computador de pe, com a melhoria e cifrado, refaz ao lado dele o androide que
	/// o cifrou, se ele esta morto e conectado. *"Your memories have been saved and implanted into a new body. Your old
	/// body was terminated or reached expiration."*
	/// </summary>
	private void VigiarOsAndroides()
	{
		foreach (Obra o in _noChao.ToList())
		{
			if (o.Tipo != SuperComputador.Tipo || o.Tech is not { } e || !e.Ligado(SuperComputador.ChaveRessurreicao)) continue;
			string conta = e.Txt(SuperComputador.ChaveConta);
			if (conta.Length == 0) continue;
			int slot = (int)e.Num(ChaveDoSlotDoComputador);
			ServerPlayer? alvo = _players.Values.FirstOrDefault(p => (p.Peer != null || VigiaDoComputadorSemPeerDeTeste) && p.Slot == slot
																	   && string.Equals(p.Conta, conta, StringComparison.OrdinalIgnoreCase));
			if (alvo is not { Ficha.dead: true }) continue;
			// `if(Race=="Android")`.
			if (!CorpoDeAndroide(alvo) && !SuperComputador.RefazQualquerUmDeTeste) continue;

			Ressuscitar(alvo, o.Zona, new Vec2(o.X, o.Y + ZoneCollision.TileSize));
			alvo.Facing = Facing.South;
			AnunciarAVista(o, $"{NomeDaObra(o)}: memórias implantadas num corpo novo.");
			Avisar(alvo, "as suas memórias foram salvas e implantadas num corpo novo: o corpo antigo foi destruído.");
			GD.Print($"[server] o super computador refez {alvo.Name} em {o.Zona.Name}");
		}
	}

	// =====================================================================
	// O RELOGIO DO LOTE, E A SAIDA
	// =====================================================================
	private double _relogioDosComputadores;

	private void TickDosSuperComputadores(double dt)
	{
		// O droide que morreu sai do mundo (o `del` do clone caido).
		if (_droides.Count > 0)
			foreach (int id in _droides.Keys.ToList())
			{
				if (!_players.TryGetValue(id, out ServerPlayer? corpo)) { _droides.Remove(id); continue; }
				if (corpo.Ficha.dead) DesfazerDroide(id, $"{corpo.Name} foi destruído.");
			}

		_relogioDosComputadores += dt;
		if (_relogioDosComputadores < SuperComputador.SegundosEntreVigias) return;
		_relogioDosComputadores = 0;
		VigiarOsAndroides();
	}

	/// <summary>Quem saiu do mundo leva os droides dele e as chaves que digitou -- e o droide que saiu deixa de estar de pe.</summary>
	private void EsquecerSuperComputador(int id)
	{
		_computadoresAbertos.Remove(id);
		_droides.Remove(id);
		foreach (int droide in DroidesDe(id)) DesfazerDroide(droide, null);
	}
}
