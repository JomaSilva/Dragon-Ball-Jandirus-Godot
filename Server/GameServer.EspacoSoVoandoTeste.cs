using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--espacovooteste` -- NO ESPACO SO SE ANDA VOANDO (dono, 2026-10-20). O pedido e as quatro escolhas dele estao no
/// `ParadoNoEspaco` (`GameServer.Espaco.cs`). Um cliente cru entra pela PORTA (o jogador de verdade: `Peer`, o login, o `AplicarInput` de
/// producao pelo `PacoteDePasso`), e corpos forjados fazem o resto (a IA pelo `PassoDaIa`, quem carrega no colo, quem leva o golpe):
///   P. NO PLANETA, A PE, ANDA -- o contra-exemplo da zona: a regra nao vaza pro chao.
///   E. NO ESPACO, A PE, NAO ANDA -- o funil recusa e o pacote de input volta sem passo.
///   V. VOANDO ANDA -- o `AlternarVoo` de producao liga o voo, e o mesmo pacote passa.
///   S. QUEM PARA DE VOAR FICA PARADO -- desligado o voo, o corpo ainda anda enquanto desce (o cliente ja parou: nada a corrigir), e
///      na altura zero o passo volta a ser recusado.
///   I. A IA, PELA MESMA REGRA (dono): o corpo a pe no espaco nao sai do lugar pelo `PassoDaIa`; voando sai.
///   K. QUEM ACORDA DO NOCAUTE FICA PARADO (dono): o nocaute derruba o voo, e de pe de novo o corpo nao anda ate voar.
///   L. AO LEME, A NAVE ANDA (dono): o piloto do pod no espaco tem o voo apagado pela troca de zona (o `MoveToZone` da subida) e anda.
///   C. O COLO DE QUEM VOA LEVA (dono): a pe no espaco ninguem agarra ninguem, e quem voa leva no colo quem ficou parado -- o resgate.
///   G. O GOLPE QUE ARREMESSA LEVA (dono): o arremesso nao e passo do corpo, e atravessa o vazio com ele parado.
///   A. QUEM ROMPE A ATMOSFERA VOANDO CHEGA VOANDO (dono), na altura de pairar, e anda.
/// Os dois defeitos injetados -- `APeNoEspacoAndaDeTeste` (o corpo a pe anda no espaco) e `ChegaSemVooNoEspacoDeTeste` (chega parado) --
/// reprovam as reguas que dependem deles.
///
/// O RELOGIO NAO ANDA: a bancada roda dentro de uma chamada, antes de a arvore processar o servidor, e cada tique de que ela precisa e
/// chamado pela porta dele (o vacuo nao sufoca ninguem aqui, porque o tique de 1 Hz nao roda). Cada afirmacao principal tem um rotulo
/// proprio em negrito: e por ele que a contraprova por mutacao (o `ciclo.py` do agente `espacovoo`) sabe onde cada linha tirada reprova.
/// </summary>
public sealed partial class GameServer
{
	private const string ContaDaBancadaDoEspacoVoo = "bancada_espacovoo";

	/// <summary>Faixa de ids dos corpos forjados desta bancada -- longe das outras (a do juiz e 987_760, a da cura viva 991_000).</summary>
	private const int IdBaseDoEspacoVooDeTeste = 988_000;

	private bool _evRodando, _evJaRodou;
	private int _evOk, _evFalhou, _evProximoId;
	private readonly List<ServerPlayer> _evForjados = [];

	private void AfirmarEv(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _evOk++; GD.Print($"[espacovoo]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _evFalhou++; GD.PrintErr($"[espacovoo]   FALHA {nome}   [{detalhe}]"); }
	}

	public void RodarBancadaDoEspacoSoVoando(int porta)
	{
		if (_evRodando || _evJaRodou) return;
		_evRodando = _evJaRodou = true;
		_evOk = _evFalhou = 0;
		GD.Print("[espacovoo] ================ NO ESPACO SO SE ANDA VOANDO (dono, 2026-10-20) ================");

		var cli = new ClienteCruDeTeste();
		var mesa = new MesaDeClientesCrus(this, porta, cli);
		ServerPlayer? pl = null;
		Nave? pod = null;
		try
		{
			pl = mesa.EntraNoMundo(cli, ContaDaBancadaDoEspacoVoo, "Astronauta");
			AfirmarEv("(montagem) o cliente entra pela porta e e jogador", pl != null && EhJogador(pl) && pl.Combate != null && pl.Livro != null);
			if (pl == null || pl.Combate == null || pl.Livro == null) return;

			// A SKILL QUE ABRE O VOO, concedida como o `--vooteste` concede: o que se mede e o voo, e nao a compra dele.
			pl.Livro.Dar(SkillDoKi);
			pl.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);

			NoPlanetaAPeAndaEv(pl);
			NoEspacoAPeNaoAndaEv(pl);
			VoandoAndaEParandoFicaEv(pl);
			AIaPelaMesmaRegraEv();
			QuemAcordaDoNocauteFicaParadoEv();
			pod = AoLemeANaveAndaEv(pl);
			OColoDeQuemVoaLevaEv();
			OGolpeQueArremessaLevaEv();
			QuemRompeAAtmosferaChegaVoandoEv(pl);
		}
		catch (Exception e)
		{
			AfirmarEv("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			APeNoEspacoAndaDeTeste = false;
			ChegaSemVooNoEspacoDeTeste = false;
			if (pl != null && NaveDoPiloto(pl) is { } aoLeme) Desembarcar(pl, aoLeme);
			if (pod != null) _naves.Remove(pod);
			GravarNaves();
			foreach (ServerPlayer f in _evForjados)
			{
				EsquecerTecnicas(f.Id);
				_players.Remove(f.Id);
				ZoneList(f.Zone.Hash).Remove(f);
			}
			_evForjados.Clear();
			mesa.Fecha();
			if (_store != null)
				try
				{
					string caminho = System.IO.Path.Combine(_store.Pasta, ContaDaBancadaDoEspacoVoo + ".json");
					if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
				}
				catch (Exception e) { GD.PushWarning($"[espacovoo] nao apaguei '{ContaDaBancadaDoEspacoVoo}': {e.Message}"); }
			_evRodando = false;
			GD.Print($"[espacovoo] ================ {_evOk} OK, {_evFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// AS PECAS DA BANCADA
	// =====================================================================
	private static readonly ZoneKey TerraDaBancadaDoEspacoVoo = ZoneKey.Premade("Earth");

	/// <summary>O ponto do vazio logo acima da Terra -- onde o `Decolar` de producao larga quem sobe dela.</summary>
	private static Vec2 AcimaDaTerraEv() =>
		Espaco.PontoDeDecolagem(Espaco.PreFeitos().First(p => p.Nome == TerraDaBancadaDoEspacoVoo.Name));

	/// <summary>Um passo honesto pelo `AplicarInput` de producao (o `PacoteDePasso` da `--saidaembateteste`). Devolve quanto o corpo andou.</summary>
	private float UmPassoPeloFioEv(ServerPlayer pl)
	{
		Vec2 antes = pl.Pos;
		PacoteDePasso(pl, new Vec2(1, 0));
		return (pl.Pos - antes).Length;
	}

	/// <summary>Um passo pelo atuador da IA (`PassoDaIa`, o mesmo do cerebro). Devolve quanto o corpo andou.</summary>
	private float UmPassoDaIaEv(ServerPlayer npc)
	{
		Vec2 antes = npc.Pos;
		PassoDaIa(npc, new Comando { Rumo = new Vec2(1, 0) }, Protocol.TickSeconds);
		return (npc.Pos - antes).Length;
	}

	/// <summary>O relogio do voo deste corpo, N tiques -- a subida, a descida e a queda sao todas dele.</summary>
	private void TiquesDoVooEv(ServerPlayer pl, int n)
	{
		for (int i = 0; i < n; i++) TickDoVoo(pl, Protocol.TickSeconds);
	}

	/// <summary>
	/// UM CORPO DE VERDADE NO VAZIO, pela sequencia de verdade (`PorNoMundo`), sem cerebro (quem o move e a bancada, pelo atuador da IA) e
	/// ja sabendo voar (a mesma concessao do jogador). Nasce a pe: `Voando` falso e altura zero, como chega quem troca de zona.
	/// </summary>
	private ServerPlayer ForjarNoEspacoEv(string nome, Vec2 onde)
	{
		var f = new Jandirus.Core.Stats.Fighter
		{
			Name = nome, Race = "Human", BP = 50_000,
			physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
			kiskill = 5, speed = 5, magiskill = 5, Idade = 25,
			maxstamina = 100, stamina = 100,
		};
		f.Statify();
		f.PowerLevel();
		f.Ki = f.MaxKi;
		var c = new ServerPlayer
		{
			Id = IdBaseDoEspacoVooDeTeste + _evProximoId++,
			Peer = null,
			Cerebro = null,
			Name = nome,
			Race = "Human", Genero = "Male", Idade = 25,
			Zone = ZonaDoEspaco, Pos = onde,
			Conta = ContaDaBancadaDoEspacoVoo,
			LastInputMs = NowMs(),
			Ficha = f,
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		PorNoMundo(c);
		c.Combate.Reviver();
		c.Ficha.PowerLevel();
		c.Livro.Dar(SkillDoKi);
		c.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
		_evForjados.Add(c);
		return c;
	}

	// =====================================================================
	// P. NO PLANETA, A PE, ANDA
	// =====================================================================
	private void NoPlanetaAPeAndaEv(ServerPlayer pl)
	{
		GD.Print("[espacovoo] -- P. no planeta, a pe, anda (o contra-exemplo da zona) --");
		if (!pl.Zone.Equals(TerraDaBancadaDoEspacoVoo)) MoveToZone(pl.Id, TerraDaBancadaDoEspacoVoo, PontoDeNascimento(TerraDaBancadaDoEspacoVoo));
		PorEmCampoAberto(pl, 4);
		float andou = UmPassoPeloFioEv(pl);
		AfirmarEv("P. **NO PLANETA, A PE, ANDA**: na Terra, sem voar, o pacote de input passa pelo `AplicarInput` de producao -- a regra nao vaza pro chao",
			pl.Zone.Equals(TerraDaBancadaDoEspacoVoo) && !pl.Voando && PodeMexerOCorpo(pl) && andou > 1f,
			$"zona {pl.Zone.Name}; voando {pl.Voando}; andou {andou:0.0} px");
	}

	// =====================================================================
	// E. NO ESPACO, A PE, NAO ANDA
	// =====================================================================
	private void NoEspacoAPeNaoAndaEv(ServerPlayer pl)
	{
		GD.Print("[espacovoo] -- E. no espaco, a pe, nao anda --");
		MoveToZone(pl.Id, ZonaDoEspaco, AcimaDaTerraEv());
		AfirmarEv("(montagem) o `MoveToZone` poe o jogador no vazio sem voo (o voo nao atravessa a porta)",
			Espaco.EhEspaco(pl.Zone) && !pl.Voando && pl.Altitude <= 0f, $"zona {pl.Zone.Name}; voando {pl.Voando}; altura {pl.Altitude:0}");

		float andou = UmPassoPeloFioEv(pl);
		AfirmarEv("E. **NO ESPACO, A PE, NAO ANDA**: o funil (`PodeMexerOCorpo`) recusa e o pacote de input nao move o corpo",
			!PodeMexerOCorpo(pl) && andou < 0.01f, $"pode mexer {PodeMexerOCorpo(pl)}; andou {andou:0.00} px");

		APeNoEspacoAndaDeTeste = true;
		try
		{
			float comDefeito = UmPassoPeloFioEv(pl);
			AfirmarEv("CONTRA-EXEMPLO (defeito injetado: o corpo a pe anda no espaco) -- a regua de cima REPROVA: o mesmo pacote passa",
				PodeMexerOCorpo(pl) && comDefeito > 1f, $"andou {comDefeito:0.0} px");
		}
		finally { APeNoEspacoAndaDeTeste = false; }
	}

	// =====================================================================
	// V + S. VOANDO ANDA; QUEM PARA DE VOAR FICA PARADO
	// =====================================================================
	private void VoandoAndaEParandoFicaEv(ServerPlayer pl)
	{
		GD.Print("[espacovoo] -- V. voando anda / S. quem para de voar fica parado --");
		pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, pl.Ficha.MaxKi);
		AlternarVoo(pl);
		TiquesDoVooEv(pl, 15);
		float voando = UmPassoPeloFioEv(pl);
		AfirmarEv("V. **VOANDO ANDA**: o `AlternarVoo` de producao liga o voo no vazio, e o mesmo pacote de input passa",
			Espaco.EhEspaco(pl.Zone) && pl.Voando && PodeMexerOCorpo(pl) && voando > 1f,
			$"voando {pl.Voando}; altura {pl.Altitude:0}; andou {voando:0.0} px");

		AlternarVoo(pl);
		float descendo = UmPassoPeloFioEv(pl);
		AfirmarEv("S1. **DURANTE A DESCIDA AINDA ANDA**: com o voo desligado e o corpo ainda no ar, o passo passa -- o cliente, que para de prever no instante em que o snapshot diz \"sem voo\", nao leva correcao nenhuma nesse meio-tempo",
			!pl.Voando && pl.Altitude > 0f && descendo > 1f, $"voando {pl.Voando}; altura {pl.Altitude:0.0}; andou {descendo:0.0} px");

		TiquesDoVooEv(pl, 30);
		float parado = UmPassoPeloFioEv(pl);
		AfirmarEv("S2. **QUEM PARA DE VOAR FICA PARADO**: na altura zero, sem voo, o passo volta a ser recusado",
			!pl.Voando && pl.Altitude <= 0f && !PodeMexerOCorpo(pl) && parado < 0.01f,
			$"voando {pl.Voando}; altura {pl.Altitude:0.0}; andou {parado:0.00} px");
	}

	// =====================================================================
	// I. A IA, PELA MESMA REGRA
	// =====================================================================
	private void AIaPelaMesmaRegraEv()
	{
		GD.Print("[espacovoo] -- I. a IA, pela mesma regra (dono) --");
		ServerPlayer npc = ForjarNoEspacoEv("NaufragoDaIa", AcimaDaTerraEv() + new Vec2(0, 6 * ZoneCollision.TileSize));
		float aPe = UmPassoDaIaEv(npc);
		AfirmarEv("I1. **A IA A PE NO ESPACO NAO ANDA**: o atuador da IA (`PassoDaIa`) pergunta ao mesmo funil e o corpo fica onde esta",
			!PodeMexerOCorpo(npc) && aPe < 0.01f && !npc.Moving, $"andou {aPe:0.00} px; moving {npc.Moving}");

		AlternarVoo(npc);
		float voando = UmPassoDaIaEv(npc);
		AfirmarEv("I2. ...e VOANDO ELA ANDA: o mesmo verbo `Fly` do jogador, e o passo sai",
			npc.Voando && voando > 1f, $"voando {npc.Voando}; andou {voando:0.0} px");

		AlternarVoo(npc);
		TiquesDoVooEv(npc, 30);
		APeNoEspacoAndaDeTeste = true;
		try
		{
			float comDefeito = UmPassoDaIaEv(npc);
			AfirmarEv("CONTRA-EXEMPLO (defeito injetado: o corpo a pe anda no espaco) -- a regua I1 REPROVA: o NPC a pe sai do lugar",
				!npc.Voando && comDefeito > 1f, $"andou {comDefeito:0.0} px");
		}
		finally { APeNoEspacoAndaDeTeste = false; }
	}

	// =====================================================================
	// K. QUEM ACORDA DO NOCAUTE FICA PARADO
	// =====================================================================
	private void QuemAcordaDoNocauteFicaParadoEv()
	{
		GD.Print("[espacovoo] -- K. quem acorda do nocaute fica parado (dono) --");
		ServerPlayer c = ForjarNoEspacoEv("NocauteadoNoVazio", AcimaDaTerraEv() + new Vec2(0, 12 * ZoneCollision.TileSize));
		AlternarVoo(c);
		TiquesDoVooEv(c, 15);
		bool voava = c.Voando && c.Altitude > 0f;

		c.Combate.Nocautear(30, porVital: false);
		TiquesDoVooEv(c, 30);
		bool caiu = c.Ficha.KO && !c.Voando && c.Altitude <= 0f;
		c.Combate.Levantar();

		float andou = UmPassoDaIaEv(c);
		AfirmarEv("K. **QUEM ACORDA DO NOCAUTE FICA PARADO**: o nocaute derruba o voo no vazio, e de pe de novo o corpo nao sai do lugar ate voar",
			voava && caiu && !c.Ficha.KO && !c.Voando && !PodeMexerOCorpo(c) && andou < 0.01f,
			$"voava {voava}; caiu sem voo {caiu}; de pe {!c.Ficha.KO}; andou {andou:0.00} px");

		APeNoEspacoAndaDeTeste = true;
		bool soARegra;
		try { soARegra = PodeMexerOCorpo(c); }
		finally { APeNoEspacoAndaDeTeste = false; }
		AfirmarEv("...e quem o segura e a regra do espaco, e nao o nocaute: com ela desligada (defeito injetado) o mesmo corpo de pe anda",
			soARegra, $"pode mexer sem a regra {soARegra}");
	}

	// =====================================================================
	// L. AO LEME, A NAVE ANDA
	// =====================================================================
	/// <summary>
	/// O JOGADOR EMBARCA NUM POD NA TERRA (a tecla E, `nave_usar`) e sobe pelo MESMO `MoveToZone` que a subida da nave chama: chega ao
	/// vazio com o voo apagado, como chega todo piloto. Devolve o pod (a limpeza o tira do mundo).
	/// </summary>
	private Nave? AoLemeANaveAndaEv(ServerPlayer pl)
	{
		GD.Print("[espacovoo] -- L. ao leme, a nave anda (dono) --");
		MoveToZone(pl.Id, TerraDaBancadaDoEspacoVoo, PontoDeNascimento(TerraDaBancadaDoEspacoVoo));
		PorEmCampoAberto(pl, 4);
		var pod = new Nave { Id = _proximaNaveId++, Tipo = "Spacepod", DonoConta = pl.Conta, DonoNome = pl.Name, ErguidaEm = NowMs() };
		pod.PorZona(pl.Zone);
		pod.X = pl.Pos.X;
		pod.Y = pl.Pos.Y;
		_naves.Add(pod);
		ComandoDeInteracao(pl, "nave_usar", "");
		bool aoLeme = NaveDoPiloto(pl) == pod;
		AfirmarEv("(montagem) a tecla E poe o jogador ao leme do pod parado aos pes dele", aoLeme, $"piloto {pod.PilotoId}");
		if (!aoLeme) return pod;

		MoveToZone(pl.Id, ZonaDoEspaco, AcimaDaTerraEv());
		float andou = UmPassoPeloFioEv(pl);
		AfirmarEv("L. **AO LEME, A NAVE ANDA**: no vazio, com o voo do piloto apagado pela troca de zona, o passo passa -- quem anda e a nave",
			Espaco.EhEspaco(pl.Zone) && EstaPilotando(pl.Id) && !pl.Voando && PodeMexerOCorpo(pl) && andou > 1f,
			$"zona {pl.Zone.Name}; ao leme {EstaPilotando(pl.Id)}; voando {pl.Voando}; andou {andou:0.0} px");

		Desembarcar(pl, pod);
		float fora = UmPassoPeloFioEv(pl);
		AfirmarEv("...e fora do pod, no mesmo vazio, quem nao voa volta a ficar parado",
			!EstaPilotando(pl.Id) && !pl.Voando && !PodeMexerOCorpo(pl) && fora < 0.01f, $"voando {pl.Voando}; andou {fora:0.00} px");
		return pod;
	}

	// =====================================================================
	// C. O COLO DE QUEM VOA LEVA
	// =====================================================================
	private void OColoDeQuemVoaLevaEv()
	{
		GD.Print("[espacovoo] -- C. o colo de quem voa leva (dono) -- o resgate --");
		const float T = ZoneCollision.TileSize;
		Vec2 aqui = AcimaDaTerraEv() + new Vec2(0, 18 * T);
		ServerPlayer naufrago = ForjarNoEspacoEv("NaufragoDoColo", aqui);
		ServerPlayer resgate = ForjarNoEspacoEv("ResgateDoColo", aqui - new Vec2(0.75f * T, 0));
		resgate.Facing = Facing.East;

		AlternarAgarrao(resgate);
		AfirmarEv("C1. **A PE NO ESPACO NINGUEM AGARRA**: o agarrao pergunta ao mesmo funil, e quem nao voa nao pega nem quem esta colado",
			resgate.AgarrandoId == 0 && !PodeMexerOCorpo(resgate), $"agarrando {resgate.AgarrandoId}");
		if (resgate.AgarrandoId != 0) Soltar(resgate, MotivoDaSoltura.Tecla);   // (so sem a regra: o agarrao de cima pegou -- C2 parte do zero)

		AlternarVoo(resgate);
		TiquesDoVooEv(resgate, 15);
		resgate.Pos = naufrago.Pos - new Vec2(0.75f * T, 0);
		resgate.Facing = Facing.East;
		AlternarAgarrao(resgate);   // segurando
		AlternarAgarrao(resgate);   // no colo
		bool noColo = resgate.AgarrandoId == naufrago.Id && resgate.ModoDoAgarrao == ModoDeAgarrao.Carregando;

		Vec2 antes = naufrago.Pos;
		for (int i = 0; i < 20; i++)
		{
			PassoDaIa(resgate, new Comando { Rumo = new Vec2(1, 0) }, Protocol.TickSeconds);
			TickDoAgarrao(Protocol.TickSeconds);
		}
		float levou = (naufrago.Pos - antes).Length;
		AfirmarEv("C2. **O COLO DE QUEM VOA LEVA**: quem voa pega no colo o corpo parado no vazio e anda com ele -- o resgate de quem nao sabe voar",
			noColo && !naufrago.Voando && levou > 8f && (naufrago.Pos - resgate.Pos).LengthSquared < 1f,
			$"no colo {noColo}; levou {levou:0.0} px; distancia dos dois {(naufrago.Pos - resgate.Pos).Length:0.0} px");
		if (resgate.AgarrandoId != 0) AlternarAgarrao(resgate);   // solta
	}

	// =====================================================================
	// G. O GOLPE QUE ARREMESSA LEVA
	// =====================================================================
	private void OGolpeQueArremessaLevaEv()
	{
		GD.Print("[espacovoo] -- G. o golpe que arremessa leva (dono) --");
		ServerPlayer c = ForjarNoEspacoEv("ArremessadoNoVazio", AcimaDaTerraEv() + new Vec2(0, 24 * ZoneCollision.TileSize));
		Vec2 antes = c.Pos;
		c.Facing = Facing.East;
		Arremessar(c, new Vec2(1, 0), 1_000_000, 5);

		// O PACOTE DE QUEM ESTA NO AR PELO GOLPE, com o olhar pro oeste: a porta do arremesso (`TiquesDeVoo`, no `AplicarInput`) o guarda.
		AplicarInput(c, c.SeqInput + 1, (uint)RelogioDeQuadrosMs(), c.Pos, (byte)Facing.West);
		bool olharDoPacote = c.Facing == Facing.West;

		for (int i = 0; i < 60 && c.TiquesDeVoo > 0; i++) TickDoEmpurrao();
		float voou = (c.Pos - antes).Length;
		AfirmarEv("G1. **O GOLPE QUE ARREMESSA LEVA**: o arremesso nao e passo do corpo, e atravessa o vazio com ele a pe -- parado e preso ao lugar sao coisas diferentes",
			!c.Voando && voou > ZoneCollision.TileSize, $"voou {voou:0.0} px; tiques que sobraram {c.TiquesDeVoo}");
		AfirmarEv("G2. **O ARREMESSADO NAO ESTA PARADO**: no meio do golpe o pacote dele segue pela porta do arremesso (o olhar que o cliente manda vale), e nao pela recusa de quem nao sai do lugar",
			olharDoPacote, $"olhar {c.Facing}");
	}

	// =====================================================================
	// A. QUEM ROMPE A ATMOSFERA VOANDO CHEGA VOANDO
	// =====================================================================
	/// <summary>
	/// PELO CAMINHO DE QUEM VOA: o voo ligado na Terra (`AlternarVoo`) e a tecla de subir no relogio do voo (`TickDoVoo`), que no teto
	/// chama o `TentarRomperAAtmosfera` de producao. Os tres portoes (Ki, poder, voo) sao dados como a `--esferamochilateste` os da.
	/// Devolve se chegou ao espaco.
	/// </summary>
	private bool SubirAteOEspacoEv(ServerPlayer pl)
	{
		MoveToZone(pl.Id, TerraDaBancadaDoEspacoVoo, PontoDeNascimento(TerraDaBancadaDoEspacoVoo));
		pl.Ficha.Ki = Math.Max(pl.Ficha.Ki, 10 * Voo.KiParaEspaco);
		pl.Ficha.expressedBP = Math.Max(pl.Ficha.expressedBP, 10 * Voo.BpParaEspaco);
		pl.Ficha.flightability = Math.Max(pl.Ficha.flightability, Voo.HabilidadeNivel1);
		AlternarVoo(pl);
		for (int i = 0; i < 300 && !Espaco.EhEspaco(pl.Zone); i++)
		{
			pl.QuerSubir = true;
			TickDoVoo(pl, Protocol.TickSeconds);
		}
		pl.QuerSubir = false;
		return Espaco.EhEspaco(pl.Zone);
	}

	private void QuemRompeAAtmosferaChegaVoandoEv(ServerPlayer pl)
	{
		GD.Print("[espacovoo] -- A. quem rompe a atmosfera voando chega voando (dono) --");
		bool subiu = SubirAteOEspacoEv(pl);
		float andou = subiu ? UmPassoPeloFioEv(pl) : 0f;
		AfirmarEv("A. **QUEM ROMPE A ATMOSFERA VOANDO CHEGA VOANDO**: do teto da Terra o corpo chega ao vazio ainda no ar, na altura de pairar, e anda",
			subiu && pl.Voando && Math.Abs(pl.Altitude - Voo.AlturaDePairar) < 0.01f && PodeMexerOCorpo(pl) && andou > 1f,
			$"subiu {subiu}; zona {pl.Zone.Name}; voando {pl.Voando}; altura {pl.Altitude:0.0} (pairar {Voo.AlturaDePairar:0}); andou {andou:0.0} px");

		ChegaSemVooNoEspacoDeTeste = true;
		try
		{
			bool subiuDeNovo = SubirAteOEspacoEv(pl);
			float comDefeito = subiuDeNovo ? UmPassoPeloFioEv(pl) : -1f;
			AfirmarEv("CONTRA-EXEMPLO (defeito injetado: a chegada sem voo, o jogo de antes) -- a regua A REPROVA: o corpo chega a pe e fica parado no vazio",
				subiuDeNovo && !pl.Voando && !PodeMexerOCorpo(pl) && comDefeito < 0.01f,
				$"subiu {subiuDeNovo}; voando {pl.Voando}; andou {comDefeito:0.00} px");
		}
		finally { ChegaSemVooNoEspacoDeTeste = false; }
	}
}
