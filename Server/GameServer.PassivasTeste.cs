using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Races;
using Jandirus.Core.Social;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--passivasteste` -- AS PASSIVAS E AS ATIVAS RACIAIS (v0.4.0, pedido 16). O dono: *"meu amigo gringo deu sugestoes de
/// passivas pras seguintes raças"* -- e, perguntado, elas vem com a raca e o Demonic Saiyan e uma ativa do Legendary Primal.
///
/// TUDO PELA PORTA DE PRODUCAO: o tique racial de verdade (`TickDasRaciais`, o mesmo do `TickDosRelogiosDoCorpo`), o canal de
/// habilidade (`UsarHabilidade` -> `UsarAtivaRacial`), os ganchos do soco e do tiro (`AoGolpeRacial`, `DanoDeKiRacial`,
/// `AoTiroRacial`, `FatorRacialDoDisparo`), a morte (`CombatState.Morrer` -> `NegarMorte`), o dano no membro
/// (`MeleeResolver.AplicarDanoPronto`, que passa pelo `AplicarNoMembro`), o nocaute (`CombatState.Tick`) e a tecla C
/// (`Transformar`). A bancada so monta a cena (corpos forjados sem `Peer`, cada um com conta propria -- a conta faz dele uma PESSOA),
/// anda o relogio (`AdiantoDoRelogioDeTeste`) e le os canais.
///
///   A. O KIT -- quem tem qual; a pessoa e (2026-10-17) o corpo do mundo tem, a copia nao; e a recusa da ativa de outro sangue.
///      CONTRA-EXEMPLOS: `Passivas.SemKitDeTeste` e `Passivas.SoPessoaTemKitDeTeste` (o portao da v0.4.0).
///   B. SAIYAJIN -- Seasoned Warrior, Warrior Race (2026-10-19: pelo tronco ou pela cabeca, e nao pela vida; CONTRA-EXEMPLO
///      `Passivas.GuerreiroPelaVidaDeTeste`), Saiyan's Resolve, Prideful, False Advantage.
///   C. PRIMAL -- Primal Evolution, Unmatched Ferocity, Limitless Power, Shattering the Limit (2026-10-19: so no degrau SSJ4, a carga e o
///      raio vermelhos, e o Kamehameha x10 sem os 10%; CONTRA-EXEMPLOS `Passivas.LimiteEmQualquerFormaDeTeste`,
///      `Passivas.LimiteSemVermelhoDeTeste` e `Passivas.KamehamehaPedeOPisoDeTeste`), Roaring Evolution.
///   D. LENDARIO -- Overflowing Power (e o desabafo), Unstoppable Force, Escalating Power.
///   E. LENDARIO PRIMAL -- Overflowing Ki, Primal Bloodlust, Demonic Saiyan (as recusas, a carga parada, o tanque 3x), Legendary
///      Resurrection.
///   F. MEIO-SAIYAJIN -- Prodigy, Immeasurable Potential, Hidden Talent, Blazing Spirit.
///   G. HUMANO -- Persistence, Second Wind, Sheer Willpower, Max Power, e a linha da Tensao pela tecla C.
///   H. NAMEKUSEIJIN -- Resilience, Warrior's Vitality, Spirit Disruption.
///   I. FROST DEMON -- Perfect Control, Extreme Tenacity, Unrestrained Wrath, Revitalizing Metamorphosis.
///   J. MUTANTE -- Long-Awaited 100%, Sadistic Nature, Ruthless Mind.
///   K. O SAVE -- a recarga atravessa o relog; o buff nao.
///
/// COMO RODAR: `Godot --headless --path . --server --port 8451 --passivasteste`
/// </summary>
public partial class GameServer
{
	private const int IdBaseDasPassivasDeTeste = 99_400;

	private int _psOk, _psFalhou, _psCorpos;

	private void AfirmarPs(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _psOk++; GD.Print($"[passivas]   OK    {oque}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); return; }
		_psFalhou++;
		GD.PrintErr($"[passivas]   FALHA {oque}   [{detalhe}]");
	}

	public void RodarBancadaDasPassivas()
	{
		_psOk = _psFalhou = 0;
		GD.Print("[passivas] ================ AS PASSIVAS E AS ATIVAS RACIAIS (v0.4.0) ================");
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		AfirmarPs("a zona da bancada tem colisao carregada", _pjMapa != null);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		try
		{
			OKitRacial();
			OSaiyajinRacial();
			OPrimalRacial();
			OLendarioRacial();
			OLendarioPrimalRacial();
			OMeioSaiyajinRacial();
			OHumanoRacial();
			ONamekuseijinRacial();
			OFrostDemonRacial();
			OMutanteRacial();
			ARecargaVaiProSave();
		}
		catch (Exception e)
		{
			AfirmarPs($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			Passivas.SemKitDeTeste = false;
			Passivas.SoPessoaTemKitDeTeste = false;
			Passivas.LimiteEmQualquerFormaDeTeste = false;
			Passivas.LimiteSemVermelhoDeTeste = false;
			Passivas.KamehamehaPedeOPisoDeTeste = false;
			Passivas.GuerreiroPelaVidaDeTeste = false;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			LimparABancadaDasPassivas();
		}
		GD.Print($"[passivas] ================ {_psOk} passaram, {_psFalhou} falharam ================");
	}

	// =====================================================================
	// OS CORPOS, O RELOGIO E A LIMPEZA
	// =====================================================================
	/// <summary>
	/// Um corpo de bancada num ponto livre da zona. `pessoa` da conta e slot (a assinatura: e ela que faz o corpo ser uma PESSOA) -- sem
	/// ela e sem papel de NPC, e a copia de alguem, que nao tem kit. A conta e so de letras e sublinhado.
	/// </summary>
	private ServerPlayer ForjarPs(string nome, string raca, string classe = "", double bp = 1_000_000, string linhagem = "",
								  bool pessoa = true, Vec2? onde = null)
	{
		int n = _psCorpos++;
		var novo = new ServerPlayer
		{
			Id = IdBaseDasPassivasDeTeste + n,
			Peer = null,
			Name = "bancada: " + nome,
			Race = raca,
			Class = classe,
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaBancadaDeProjetil,
			Pos = onde ?? CorredorLivre(8),
			Conta = pessoa ? $"bancada_passivas_{(char)('a' + n / 26)}{(char)('a' + n % 26)}" : "",
			Slot = pessoa ? 0 : -1,
			Ficha = new Fighter { Race = raca, BP = bp },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = classe;
		novo.Ficha.SaiyanLineage = linhagem;
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		novo.PosDoTiqueRacial = novo.Pos;
		return novo;
	}

	/// <summary>O relogio anda `segundos` e o corpo passa pelo tique racial de producao, em passos de `dt`.</summary>
	private void AndarPs(ServerPlayer pl, double segundos, double dt = 0.5)
	{
		double feito = 0;
		while (feito < segundos - 1e-9)
		{
			double passo = Math.Min(dt, segundos - feito);
			AdiantoDoRelogioDeTeste += (long)Math.Round(passo * 1000);
			TickDasRaciais(pl, passo);
			feito += passo;
		}
	}

	/// <summary>Poe o corpo numa forma como a tecla a poria (a entrada e o `AplicarForma`), sem a cena.</summary>
	private void EntrarPs(ServerPlayer pl, string forma)
	{
		pl.Forma.Entrar(forma);
		AplicarForma(pl);
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel();
	}

	/// <summary>O dano que CHEGA num membro (`r.Dano`, depois das defesas raciais) de um golpe pronto de `dano`.</summary>
	private double ChegaPs(ServerPlayer pl, double dano = 1) =>
		MeleeResolver.AplicarDanoPronto(pl.Combate, dano, letal: false, new Random(17)).Dano;

	private static GolpeResultado AcertoPs => new() { Desfecho = Desfecho.Acertou, Dano = 1, Membro = "" };
	private static GolpeResultado AparadoPs => new() { Desfecho = Desfecho.Aparou, Dano = 0, Membro = "" };

	private static bool PertoPs(double a, double b, double tol = 1e-6) => Math.Abs(a - b) <= tol;

	/// <summary>O membro que as cenas ferem e decepam: um braco (um MEMBRO, e nao um nucleo -- o nucleo derruba e mata).</summary>
	private const string BracoDaBancadaPs = "Braco esquerdo";

	/// <summary>Todo membro de fora (e o tronco) numa fracao da vida.</summary>
	private static void VidaDosMembrosPs(ServerPlayer pl, double fracao)
	{
		foreach (BodyPart p in pl.Combate.Corpo.Partes)
			if (!p.Decepado) p.Vida = p.VidaMax * fracao;
		pl.Combate.SincronizarVida();
	}

	private List<Projetil> TirosDePs(ServerPlayer pl) =>
		ProjeteisDaZona(pl.Zone.Hash).Where(p => p.Dono == pl.Id).ToList();

	// =====================================================================
	// O ROBO `--diagesfera` (cliente, com o servidor neste processo)
	// =====================================================================
	/// <summary>
	/// O ROBO `--diagesfera` poe um corpo no ponto de carregar o Demonic Saiyan -- Lendario Primal, o LSSJ desperto e DOMINADO (a furia
	/// lendaria nao tira as redeas de quem domina, e o corpo tem de ficar parado), na forma, em combate ha mais de 2 min -- e aperta a
	/// ativa pelo canal de habilidade de producao. Devolve o que o servidor disse. Corpo sem conta ganha uma: a copia (sem conta e sem
	/// papel de NPC) nao tem kit.
	/// </summary>
	internal string CarregarODemonicoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return "sem corpo";
		if (pl.Assinatura.Length == 0)
		{
			pl.Conta = $"bancada_desfera_{(char)('a' + id % 26)}{(char)('a' + id / 26 % 26)}";
			pl.Slot = 0;
		}
		pl.Race = pl.Ficha.Race = "Saiyan";
		pl.Class = pl.Ficha.Class = Passivas.ClasseLendariaPrimal;
		pl.Ficha.SaiyanLineage = "Primal Saiyan";
		pl.Forma.Maestria.Por("primal_legendary", 100);
		pl.Forma.Entrar("primal_legendary");
		AplicarForma(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Combate.EmCombate = 600;
		pl.Forma.CombateSegundos = Passivas.LutaPraDemonico + 10;
		pl.PosDoTiqueRacial = pl.Pos;
		TickDasRaciais(pl, 0.05);
		return string.Join(" | ", Ouvir(() => UsarHabilidade(pl, Passivas.Demonico)));
	}

	/// <summary>
	/// (2026-10-17) A LINHA DO GRITO NA `--doiscorposteste`: poe um corpo no ponto de desabafar -- Lendario, com o Overflowing Power ligado ha
	/// mais que o prazo do segundo aperto -- e aperta a ativa pelo canal de producao. Devolve o que o servidor disse. Corpo sem conta ganha uma
	/// (a copia sem conta e sem papel de NPC nao tem kit).
	/// </summary>
	internal string GritarODesabafoDeTeste(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Combate == null) return "sem corpo";
		if (pl.Assinatura.Length == 0)
		{
			pl.Conta = $"bancada_grito_{(char)('a' + id % 26)}{(char)('a' + id / 26 % 26)}";
			pl.Slot = 0;
		}
		pl.Race = pl.Ficha.Race = "Saiyan";
		pl.Class = pl.Ficha.Class = Passivas.ClasseLendaria;
		pl.Ficha.SaiyanLineage = "Saiyan";
		pl.Ficha.Statify();
		TickDasRaciais(pl, 0.05);
		pl.Racial.TransbordarDesde = NowMs() - (long)((Passivas.SegundosPraDesabafar + 1) * 1000);
		return string.Join(" | ", Ouvir(() => UsarHabilidade(pl, Passivas.Transbordar)));
	}

	/// <summary>A Unstoppable Force recusada no meio do grito nao ligou (o prazo dela continua no passado).</summary>
	private bool SemImparavelPs(ServerPlayer pl) => pl.Racial.ImparavelAte <= NowMs();

	/// <summary>O que a bancada viu de um grito do desabafo: os tiros, quando sairam, quando ele calou, as folgas entre os rumos e as velocidades.</summary>
	private readonly record struct GritoMedido(int Tiros, double PrimeiroEm, double UltimoEm, double CalouEm, double FolgaMaior, double FolgaMenor,
		int Velocidades)
	{
		public string Resumo =>
			$"{Tiros} tiro(s), do {PrimeiroEm:0.00} s ao {UltimoEm:0.00} s, calou aos {CalouEm:0.00} s; folgas entre rumos vizinhos de "
			+ $"{FolgaMenor * 180 / Math.PI:0.0} a {FolgaMaior * 180 / Math.PI:0.0} graus; {Velocidades} velocidade(s)";
	}

	/// <summary>
	/// ANDA O GRITO de 0,05 em 0,05 s ate ele calar (ou 4 s), anotando cada tiro NOVO do corpo (fora dos <paramref name="jaHavia"/>): quando
	/// apareceu, o rumo da saida e a velocidade. Os tiros nao andam nesta bancada (o tique dos projeteis nao roda), entao nenhum some antes de
	/// ser visto.
	/// </summary>
	private GritoMedido MedirOGritoPs(ServerPlayer pl, HashSet<int> jaHavia)
	{
		long desde = pl.Racial.GritoDesde;
		var angulos = new List<double>();
		var velocidades = new HashSet<double>();
		double primeiro = -1, ultimo = -1, calou = -1;
		for (int i = 0; i <= 80; i++)
		{
			double agora = (NowMs() - desde) / 1000.0;
			foreach (Projetil p in TirosDePs(pl))
			{
				if (!jaHavia.Add(p.Id)) continue;
				angulos.Add(Math.Atan2(p.RumoDaSaida.Y, p.RumoDaSaida.X));
				velocidades.Add(Math.Round(p.SegundosPorTile, 5));
				if (primeiro < 0) primeiro = agora;
				ultimo = agora;
			}
			if (pl.Racial.GritoDesde < 0) { calou = agora; break; }
			AndarPs(pl, 0.05, dt: 0.05);
		}
		angulos.Sort();
		double maior = 0, menor = double.MaxValue;
		for (int i = 0; i < angulos.Count; i++)
		{
			double folga = i + 1 < angulos.Count ? angulos[i + 1] - angulos[i] : angulos[0] + Math.Tau - angulos[i];
			maior = Math.Max(maior, folga);
			menor = Math.Min(menor, folga);
		}
		return new GritoMedido(angulos.Count, primeiro, ultimo, calou, maior, angulos.Count > 0 ? menor : 0, velocidades.Count);
	}

	/// <summary>O robo `--diagesfera`: a carga do Demonic esta de pe neste corpo -- e o snapshot a leva (`Ocupacao.CargaDemoniaca`)?</summary>
	internal bool CarregandoODemonicoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) && pl.Racial.DemonicoCargaDesde >= 0 && OcupacaoDe(pl) == Ocupacao.CargaDemoniaca;

	/// <summary>O robo `--diagesfera`: o corpo forjado da um passo (o servidor o tira do lugar) -- e a carga dele se desfaz no tique.</summary>
	internal void DarUmPassoDeTeste(int id, Vec2 passo)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.Pos += passo;
	}

	private void LimparABancadaDasPassivas()
	{
		bool DaBancada(int id) => id >= IdBaseDasPassivasDeTeste && id < IdBaseDasPassivasDeTeste + 1000;
		List<Projetil> tiros = ProjeteisDaZona(ZonaDaBancadaDeProjetil.Hash);
		int antes = tiros.Count;
		tiros.RemoveAll(p => DaBancada(p.Dono));
		_projeteisVivos -= antes - tiros.Count;
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (!DaBancada(pl.Id)) continue;
			EsquecerTecnicas(pl.Id);
			SoltarDoRaio(pl.Id);
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		foreach (ServerPlayer pl in ZoneList(ZonaDaBancadaDeProjetil.Hash).ToList())
			if (pl.Name.StartsWith("bancada: ", StringComparison.Ordinal) && DaBancada(pl.Id))
				ZoneList(ZonaDaBancadaDeProjetil.Hash).Remove(pl);
	}

	// =====================================================================
	// A. O KIT
	// =====================================================================
	private void OKitRacial()
	{
		GD.Print("[passivas] -- A. O KIT: vem com a raca (e a classe), e so pra pessoa --");
		(string Raca, string Classe, string Linhagem, KitRacial Kit)[] casos =
		[
			("Saiyan", "Normal", "Saiyan", KitRacial.Saiyajin),
			("Saiyan", "Elite", "Saiyan", KitRacial.Saiyajin),
			("Saiyan", "Legendary", "Saiyan", KitRacial.Saiyajin | KitRacial.Lendario),
			("Saiyan", "Normal Primal Saiyan", "Primal Saiyan", KitRacial.Primal),
			("Saiyan", "Legendary Primal Saiyan", "Primal Saiyan", KitRacial.Primal | KitRacial.LendarioPrimal),
			("Halfbreed", "New Generation", "", KitRacial.MeioSaiyajin),
			("Human", "", "", KitRacial.Humano),
			("Namekian", "Warrior clan", "", KitRacial.Namekuseijin),
			("Frost Demon", "Frost Demon", "", KitRacial.FrostDemon),
			("Frost Demon", "Mutant Frost Demon", "", KitRacial.FrostDemon | KitRacial.Mutante),
			("Android", "", "", KitRacial.Nenhum),
			("Tsujin", "", "", KitRacial.Nenhum),
		];
		foreach ((string raca, string classe, string linhagem, KitRacial kit) in casos)
		{
			KitRacial deu = Passivas.KitDe(raca, classe, linhagem);
			AfirmarPs($"{raca}{(classe.Length > 0 ? "/" + classe : "")}: {kit}", deu == kit, deu.ToString());
		}

		Vec2 chao = CorredorLivre(8);
		ServerPlayer pessoa = ForjarPs("Saiyajin com conta", "Saiyan", "Normal", linhagem: "Saiyan", onde: chao);
		ServerPlayer povo = ForjarPs("Saiyajin do mundo", "Saiyan", "Normal", linhagem: "Saiyan", pessoa: false,
									 onde: chao + new Vec2(3 * ZoneCollision.TileSize, 0));
		povo.Papel = new Jandirus.Core.Npc.PapelDeNpc(new Jandirus.Core.Npc.MoldeDeNpc { Id = "bancada_passivas", Nome = "soldado" }, 1);
		ServerPlayer copia = ForjarPs("Saiyajin copia", "Saiyan", "Normal", linhagem: "Saiyan", pessoa: false,
									 onde: chao + new Vec2(6 * ZoneCollision.TileSize, 0));
		ServerPlayer humanoDoMundo = ForjarPs("Humano do mundo", "Human", pessoa: false, onde: chao + new Vec2(9 * ZoneCollision.TileSize, 0));
		humanoDoMundo.Papel = new Jandirus.Core.Npc.PapelDeNpc(new Jandirus.Core.Npc.MoldeDeNpc { Id = "bancada_passivas", Nome = "aldeao" }, 2);
		AndarPs(pessoa, 0.1);
		AndarPs(povo, 0.1);
		AndarPs(copia, 0.1);
		AfirmarPs("pelo tique de producao, a PESSOA tem o kit", pessoa.Racial.Entrada.Kit == KitRacial.Saiyajin,
				  pessoa.Racial.Entrada.Kit.ToString());
		AfirmarPs("...e o CORPO DO MUNDO tambem (o dono, 2026-10-17: *\"os boss de saga tb vao ter todas essas passivas e ativas a depender de sua raça\"*): o povo, com papel de NPC e sem conta",
				  povo.Racial.Entrada.Kit == KitRacial.Saiyajin && EhNpcDoMundo(povo), povo.Racial.Entrada.Kit.ToString());
		AfirmarPs("...e a COPIA (sem papel de NPC e sem conta: o reflexo da mente, a Divisao do Corpo) continua sem",
				  copia.Racial.Entrada.Kit == KitRacial.Nenhum, copia.Racial.Entrada.Kit.ToString());
		List<string> doPovo = Ouvir(() => UsarHabilidade(humanoDoMundo, Passivas.SegundoFolego));
		AfirmarPs("...e a ativa do corpo do mundo sai pelo canal de habilidade (o Second Wind do Humano do mundo)",
				  humanoDoMundo.Racial.FolegoAte > NowMs(), Ultimos(doPovo));

		// O CONTRA-EXEMPLO DA REGRA NOVA: o portao da v0.4.0 (so pessoa) tira o kit do povo -- e so dele.
		Passivas.SoPessoaTemKitDeTeste = true;
		try
		{
			AndarPs(povo, 0.1);
			AndarPs(pessoa, 0.1);
			humanoDoMundo.Racial.FolegoAte = 0;
			humanoDoMundo.Ficha.RecargasRaciais.Remove(Passivas.SegundoFolego);
			List<string> recusa = Ouvir(() => UsarHabilidade(humanoDoMundo, Passivas.SegundoFolego));
			AfirmarPs("   DEFEITO INJETADO (`Passivas.SoPessoaTemKitDeTeste`, o portao da v0.4.0): o povo perde o kit e a ativa dele e recusada; a pessoa fica com o dela -- as provas de cima REPROVARIAM",
					  povo.Racial.Entrada.Kit == KitRacial.Nenhum && pessoa.Racial.Entrada.Kit == KitRacial.Saiyajin
					  && humanoDoMundo.Racial.FolegoAte == 0 && Disse(recusa, "não é do seu sangue"), Ultimos(recusa));
		}
		finally { Passivas.SoPessoaTemKitDeTeste = false; }

		List<string> falas = Ouvir(() => UsarHabilidade(pessoa, Passivas.SegundoFolego));
		AfirmarPs("a ativa de OUTRO sangue (o Second Wind do Humano) e recusada pelo canal de habilidade",
				  Disse(falas, "não é do seu sangue") && pessoa.Racial.FolegoAte == 0, Ultimos(falas));

		// O CONTRA-EXEMPLO: sem kit (o jogo de antes da v0.4.0), a mesma pessoa no mesmo tique nao tem nada.
		Passivas.SemKitDeTeste = true;
		try
		{
			AndarPs(pessoa, 0.1);
			AfirmarPs("   DEFEITO INJETADO (`Passivas.SemKitDeTeste`): o kit some do tique -- o criterio de cima REPROVARIA",
					  pessoa.Racial.Entrada.Kit == KitRacial.Nenhum, pessoa.Racial.Entrada.Kit.ToString());
		}
		finally { Passivas.SemKitDeTeste = false; }
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// B. SAIYAJIN
	// =====================================================================
	private void OSaiyajinRacial()
	{
		GD.Print("[passivas] -- B. SAIYAJIN --");
		ServerPlayer s = ForjarPs("Saiyajin", "Saiyan", "Normal", bp: 5e8, linhagem: "Saiyan");
		s.Combate.EmCombate = 90;

		// ---- Seasoned Warrior ----
		EntrarPs(s, "ssj1");
		AndarPs(s, 9.5);
		AfirmarPs("Seasoned Warrior: 9,5 s de luta no SSJ1 ainda nao pagam", s.Racial.FormasVeteranas == 0 && PertoPs(s.Ficha.racialBp, 1),
				  $"veteranas {s.Racial.FormasVeteranas}, racialBp {s.Ficha.racialBp:0.###}");
		AndarPs(s, 0.5);
		AfirmarPs("...aos 10 s a forma paga: BP +10%", s.Racial.FormasVeteranas == 1 && PertoPs(s.Ficha.racialBp, 1.10),
				  $"veteranas {s.Racial.FormasVeteranas}, racialBp {s.Ficha.racialBp:0.###}");
		EntrarPs(s, "ssj2");
		AndarPs(s, 19.5);
		AfirmarPs("...o 2o degrau pede 20 s numa forma MAIS FORTE (19,5 s ainda nao)", s.Racial.FormasVeteranas == 1,
				  $"veteranas {s.Racial.FormasVeteranas}");
		AndarPs(s, 0.5);
		AfirmarPs("...20 s no SSJ2 pagam: BP +20%", s.Racial.FormasVeteranas == 2 && PertoPs(s.Ficha.racialBp, 1.20),
				  $"veteranas {s.Racial.FormasVeteranas}, racialBp {s.Ficha.racialBp:0.###}");
		EntrarPs(s, Catalogo.IdBase);
		AndarPs(s, 0.1);
		AfirmarPs("...e descer pra base ZERA as veteranas", s.Racial.FormasVeteranas == 0 && PertoPs(s.Ficha.racialBp, 1),
				  $"veteranas {s.Racial.FormasVeteranas}, racialBp {s.Ficha.racialBp:0.###}");

		// ---- Warrior Race (o tronco ou a cabeca abaixo da metade; 20% ate 2026-10-24) + False Advantage (a vida baixa) ----
		// (2026-10-19) O dono: *"mudar de 20% de vida pra 20% do tronco ou cabeça"*. A vida e o `Fighter.HP` (a do corpo inteiro); o tronco e a
		// cabeca sao as partes do corpo de verdade, lidas pela entrada racial de producao (`MontarEntradaRacial`).
		BodyPart tronco = s.Combate.Corpo.Achar(Body.NomeDoTronco)!, cabeca = s.Combate.Corpo.Achar("Cabeca")!;
		bool Guerreiro() => PertoPs(s.Ficha.racialPhysoff, 1.2) && PertoPs(s.Ficha.racialPhysdef, 1.3);
		string Fisico() => $"physoff {s.Ficha.racialPhysoff:0.###}, physdef {s.Ficha.racialPhysdef:0.###}";
		void Partes(double doTronco, double daCabeca)
		{
			tronco.Vida = tronco.VidaMax * doTronco;
			cabeca.Vida = cabeca.VidaMax * daCabeca;
		}
		Partes(0.15, 1);
		AndarPs(s, 0.1);
		bool peloTronco = Guerreiro();
		string doTronco = Fisico();
		Partes(1, 0.15);
		AndarPs(s, 0.1);
		bool pelaCabeca = Guerreiro();
		string daCabeca = Fisico();
		AfirmarPs("WARRIOR RACE PELO TRONCO OU PELA CABECA (dono, 2026-10-19): o tronco a 15% acende (ataque fisico +20%, defesa fisica +30%), e a "
				  + "cabeca a 15% com o tronco inteiro tambem", peloTronco && pelaCabeca, $"tronco: {doTronco}; cabeca: {daCabeca}");
		// (2026-10-24) O LIMIAR SUBIU PRA METADE -- o dono: *"aumentar o warrior race de 20% de hp pra 50% de hp na cabeça ou torso"*. A parte a
		// 35% (acima dos 20% de antes, abaixo da metade) acende; o defeito devolve os 20% e a mesma parte a 35% nao acende.
		Partes(0.35, 1);
		AndarPs(s, 0.1);
		bool nos35 = Guerreiro();
		string dos35 = Fisico();
		Partes(1, 0.35);
		AndarPs(s, 0.1);
		AfirmarPs("WARRIOR RACE NA METADE (dono, 2026-10-24): o tronco a 35% acende, e a cabeca a 35% com o tronco inteiro tambem (era 20%)",
				  nos35 && Guerreiro(), $"tronco: {dos35}; cabeca: {Fisico()}");
		Passivas.GuerreiroNos20DeTeste = true;
		try
		{
			Partes(0.35, 0.35);
			AndarPs(s, 0.1);
			AfirmarPs("   (defeito injetado: `GuerreiroNos20DeTeste`, o limiar de antes) o tronco e a cabeca a 35% NAO acendem -- a regua de cima REPROVA",
					  PertoPs(s.Ficha.racialPhysoff, 1) && PertoPs(s.Ficha.racialPhysdef, 1), Fisico());
		}
		finally { Passivas.GuerreiroNos20DeTeste = false; }
		Partes(0.55, 0.55);
		AndarPs(s, 0.1);
		AfirmarPs("...e o tronco e a cabeca a 55% nao acendem", PertoPs(s.Ficha.racialPhysoff, 1) && PertoPs(s.Ficha.racialPhysdef, 1), Fisico());
		Partes(1, 1);
		s.Ficha.HP = 15;
		AndarPs(s, 0.1);
		AfirmarPs("...nem a VIDA a 15% com o tronco e a cabeca inteiros (era a regra de antes)",
				  PertoPs(s.Ficha.racialPhysoff, 1) && PertoPs(s.Ficha.racialPhysdef, 1), Fisico());
		Passivas.GuerreiroPelaVidaDeTeste = true;
		try
		{
			AndarPs(s, 0.1);
			AfirmarPs("   (defeito injetado: `GuerreiroPelaVidaDeTeste`, a regra de antes) a vida a 15% acende com o tronco e a cabeca inteiros -- a regua "
					  + "de cima REPROVA", Guerreiro(), Fisico());
		}
		finally { Passivas.GuerreiroPelaVidaDeTeste = false; }
		AndarPs(s, 0.1);
		AfirmarPs("False Advantage: abaixo de 50% de vida e nada dominado, +0,5x na forma (a base vale 1,5x) e o dreno pela metade",
				  PertoPs(s.Ficha.racialBp, 1.5) && PertoPs(s.Racial.Efeito.Dreno, 0.5),
				  $"racialBp {s.Ficha.racialBp:0.###}, dreno {s.Racial.Efeito.Dreno:0.###}");
		s.Ficha.HP = 100;
		s.Forma.Maestria.Por("ssj1", 100);
		EntrarPs(s, "ssj1");
		s.Ficha.Ki = s.Ficha.MaxKi * 0.4;
		AndarPs(s, 0.1);
		double m = Math.Max(s.Ficha.ssjBuff, 1);
		AfirmarPs("...com o SSJ1 DOMINADO e o Ki em 40%, a soma e +1x no multiplicador da forma",
				  PertoPs(s.Ficha.racialBp, (m + 1) / m, 1e-4) && PertoPs(s.Racial.Efeito.FalsaVantagem, 1),
				  $"forma {m:0.##}x, racialBp {s.Ficha.racialBp:0.####}");
		s.Ficha.Ki = s.Ficha.MaxKi;
		AndarPs(s, 0.1);
		AfirmarPs("...e com tudo acima de 50% ela some", PertoPs(s.Racial.Efeito.FalsaVantagem, 0), $"{s.Racial.Efeito.FalsaVantagem}");

		// ---- Saiyan's Resolve ----
		ServerPlayer agressor = ForjarPs("agressor do Saiyajin", "Human");
		AoGolpeRacial(agressor, s, AcertoPs);
		AndarPs(s, 0.1);
		AfirmarPs("Saiyan's Resolve: o PRIMEIRO soco nao reduz nada", PertoPs(ChegaPs(s), 1), $"{ChegaPs(s):0.####}");
		AoGolpeRacial(agressor, s, AcertoPs);
		AndarPs(s, 0.1);
		AfirmarPs("...o segundo do MESMO tipo ja reduz: 2 golpes = -4%", PertoPs(ChegaPs(s), 0.96), $"{ChegaPs(s):0.####}");
		for (int i = 0; i < 12; i++) AoGolpeRacial(agressor, s, AcertoPs);
		AndarPs(s, 0.1);
		AfirmarPs("...e para nos 20%", PertoPs(ChegaPs(s), 0.80), $"{ChegaPs(s):0.####}");
		AoTiroRacial(new Projetil { Nome = "tiro da bancada" }, s, AcertoPs, 1, naGuarda: false);
		AndarPs(s, 0.1);
		AfirmarPs("...um golpe de OUTRO tipo (o tiro de ki) recomeca a fila: nada de reducao", PertoPs(ChegaPs(s), 1), $"{ChegaPs(s):0.####}");
		AoTiroRacial(new Projetil { Nome = "tiro da bancada" }, s, AcertoPs, 1, naGuarda: false);
		AndarPs(s, 10.5);
		AfirmarPs("...e 10 s sem golpe apagam a fila", PertoPs(ChegaPs(s), 1), $"{ChegaPs(s):0.####}");

		// ---- Prideful ----
		s.Ficha.powerMod = 0.4;
		AndarPs(s, 0.1);
		AfirmarPs("Prideful: em luta e segurando o poder (barra em 40%), o 2o degrau -- regen 1,5x, fisico 1,25x, Ki Skill 0,85x",
				  PertoPs(s.Ficha.racialRegenKiPassiva, 1.5) && PertoPs(s.Ficha.racialPhysoff, 1.25) && PertoPs(s.Ficha.racialKiskill, 0.85),
				  $"regen {s.Ficha.racialRegenKiPassiva:0.##}, physoff {s.Ficha.racialPhysoff:0.##}, kiskill {s.Ficha.racialKiskill:0.##}");
		s.Ficha.powerMod = 1;
		s.Combate.EmCombate = 0;
		s.Ficha.powerMod = 0.4;
		AndarPs(s, 0.1);
		AfirmarPs("...e fora da luta o orgulho nao fala", PertoPs(s.Ficha.racialRegenKiPassiva, 1) && s.Racial.Efeito.Orgulho == 0,
				  $"regen {s.Ficha.racialRegenKiPassiva:0.##}");
		s.Ficha.powerMod = 1;

		// O CONTRA-EXEMPLO da secao: sem kit, o guerreiro ferido e um ferido qualquer (o tronco quebrado, que acende o Warrior Race).
		Partes(0.15, 1);
		Passivas.SemKitDeTeste = true;
		try
		{
			AndarPs(s, 0.1);
			AfirmarPs("   DEFEITO INJETADO (`SemKitDeTeste`): o Warrior Race some -- a prova de cima reprovaria",
					  PertoPs(s.Ficha.racialPhysoff, 1) && PertoPs(s.Ficha.racialPhysdef, 1), $"physoff {s.Ficha.racialPhysoff:0.##}");
		}
		finally { Passivas.SemKitDeTeste = false; Partes(1, 1); }
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// C. PRIMAL
	// =====================================================================
	private void OPrimalRacial()
	{
		GD.Print("[passivas] -- C. PRIMAL --");
		ServerPlayer p = ForjarPs("Primal", "Saiyan", "Normal Primal Saiyan", bp: 5e8, linhagem: "Primal Saiyan");
		AndarPs(p, 0.1);

		// ---- Primal Evolution ----
		AfirmarPs("Primal Evolution: com o rabo, na base, +5% de dano e -5% de consumo de Ki",
				  p.Racial.Entrada.TemRabo && PertoPs(p.Combate.DanoDadoRacial, 1.05) && PertoPs(p.Ficha.racialCustoKi, 0.95),
				  $"rabo {p.Racial.Entrada.TemRabo}, dano {p.Combate.DanoDadoRacial:0.###}, custo {p.Ficha.racialCustoKi:0.###}");
		EntrarPs(p, "ssj4");
		AndarPs(p, 0.1);
		AfirmarPs("...no SSJ4, +10% e -10%", PertoPs(p.Combate.DanoDadoRacial, 1.10) && PertoPs(p.Ficha.racialCustoKi, 0.90),
				  $"dano {p.Combate.DanoDadoRacial:0.###}, custo {p.Ficha.racialCustoKi:0.###}");
		EntrarPs(p, Catalogo.IdBase);
		BodyPart? rabo = p.Combate.Corpo.Achar("Rabo");
		if (rabo != null) rabo.Decepado = true;
		AndarPs(p, 0.1);
		AfirmarPs("...e SEM o rabo, nada", PertoPs(p.Combate.DanoDadoRacial, 1), $"dano {p.Combate.DanoDadoRacial:0.###}");
		if (rabo != null) { rabo.Decepado = false; rabo.Vida = rabo.VidaMax; }
		AndarPs(p, 0.1);

		// ---- Unmatched Ferocity ----
		p.Ficha.powerMod = 0.3;
		List<string> falas = Ouvir(() => UsarHabilidade(p, Passivas.Ferocidade));
		AndarPs(p, 0.1);
		AfirmarPs("Unmatched Ferocity: a barra vai a 100%, sem dreno e sem efeito negativo",
				  PertoPs(p.Ficha.powerMod, 1) && PertoPs(p.Racial.Efeito.Dreno, 0) && p.Combate.SemEfeitosNegativos,
				  $"barra {p.Ficha.powerMod:0.##}, dreno {p.Racial.Efeito.Dreno:0.##}, semNegativos {p.Combate.SemEfeitosNegativos} | {Ultimos(falas)}");
		falas = Ouvir(() => UsarHabilidade(p, Passivas.Ferocidade));
		AfirmarPs("...apertar de novo: em recarga (6 min)", Disse(falas, "recarga"), Ultimos(falas));
		AndarPs(p, 60, dt: 2);
		AfirmarPs("...e na base ela dura 1 min (o dreno volta -- com os -5% do rabo)",
				  !p.Combate.SemEfeitosNegativos && PertoPs(p.Racial.Efeito.Dreno, 1 - Passivas.EvolucaoPrimal(0)),
				  $"semNegativos {p.Combate.SemEfeitosNegativos}, dreno {p.Racial.Efeito.Dreno:0.##}");

		// ---- Limitless Power ----
		ServerPlayer humano = ForjarPs("atirador do Primal", "Human");
		var bola = new Projetil { Nome = "Kamehameha", Dono = humano.Id };
		falas = Ouvir(() => AoTiroRacial(bola, p, AcertoPs, 200, naGuarda: true));
		AfirmarPs("Limitless Power: o tiro de ki na guarda guarda 5% (na base) pro proximo ataque",
				  PertoPs(p.Racial.LimitlessGuardado, 10) && Disse(falas, "LIMITLESS POWER"), $"{p.Racial.LimitlessGuardado:0.##} | {Ultimos(falas)}");
		var meu = new Projetil { Nome = "Kamehameha", Dono = p.Id };
		double comGuardado = DanoDeKiRacial(meu, p, humano, 100);
		double semGuardado = DanoDeKiRacial(meu, p, humano, 100);
		AfirmarPs("...e ele SAI no proximo tiro de ki: 100 x 1,05 (o rabo) + 10, e o seguinte ja nao leva",
				  PertoPs(comGuardado, 115) && PertoPs(semGuardado, 105), $"{comGuardado:0.##} e {semGuardado:0.##}");
		AoTiroRacial(bola, p, AcertoPs, 200, naGuarda: true);
		AfirmarPs("...com recarga de 2 min: o bloqueio seguinte nao guarda", PertoPs(p.Racial.LimitlessGuardado, 0),
				  $"{p.Racial.LimitlessGuardado:0.##}");

		// ---- Shattering the Limit ----
		AfirmarPs("Shattering the Limit: na base, nao", PertoPs(p.Racial.FatorDoLimite(50, 100), 1));
		EntrarPs(p, "ssj4");
		AndarPs(p, 0.1);
		AfirmarPs("...no SSJ4, o tiro que custa 10% ou mais do Ki de agora sai 2x, e o de 9% nao",
				  PertoPs(p.Racial.FatorDoLimite(10, 100), 2) && PertoPs(p.Racial.FatorDoLimite(9, 100), 1));
		var carregado = new Projetil { Nome = "Kamehameha", Dono = p.Id, Bp = 1000 };
		FatorRacialDoDisparo(p, carregado, "", p.Racial.FatorDoLimite(20, 100));
		AfirmarPs("...e o disparo de producao (`FatorRacialDoDisparo`) dobra o BP do tiro E o fator da onda",
				  PertoPs(carregado.Bp, 2000) && PertoPs(carregado.MultDeOnda, 2), $"bp {carregado.Bp:0}, onda {carregado.MultDeOnda:0.##}");
		AfirmarPs("...e o grito ganha o prefixo", Passivas.PrefixoDoLimite == "10x ");
		OLimiteSoNoSsj4(p);
		EntrarPs(p, "ssj1");
		AndarPs(p, 0.1);

		// ---- Roaring Evolution ----
		p.Combate.EmCombate = 90;
		AndarPs(p, 0.1);
		double r1 = DanoDeKiRacial(new Projetil { Nome = "Kamehameha", Dono = humano.Id }, humano, p, 100);
		double r2 = DanoDeKiRacial(new Projetil { Nome = "Kamehameha", Dono = humano.Id }, humano, p, 100);
		AndarPs(p, 10, dt: 1);
		double r3 = DanoDeKiRacial(new Projetil { Nome = "Kamehameha", Dono = humano.Id }, humano, p, 100);
		AndarPs(p, 10, dt: 1);
		double r4 = DanoDeKiRacial(new Projetil { Nome = "Kamehameha", Dono = humano.Id }, humano, p, 100);
		double outra = DanoDeKiRacial(new Projetil { Nome = "Galick Gun", Dono = humano.Id }, humano, p, 100);
		AfirmarPs("Roaring Evolution: a mesma tecnica perde 10% por degrau, no maximo um degrau a cada 10 s (100, 90, 90, 80)",
				  PertoPs(r1, 100) && PertoPs(r2, 90) && PertoPs(r3, 90) && PertoPs(r4, 80), $"{r1:0} {r2:0} {r3:0} {r4:0}");
		AfirmarPs("...e uma tecnica DIFERENTE chega inteira", PertoPs(outra, 100), $"{outra:0}");
		p.Combate.EmCombate = 0;
		AndarPs(p, 0.1);
		double depois = DanoDeKiRacial(new Projetil { Nome = "Kamehameha", Dono = humano.Id }, humano, p, 100);
		AfirmarPs("...e a tag de combate acabando zera os rugidos", PertoPs(depois, 100), $"{depois:0}");
		LimparABancadaDasPassivas();
	}

	/// <summary>
	/// (2026-10-19) O SHATTERING THE LIMIT SO NO DEGRAU SSJ4 -- e o raio dele sai VERMELHO. O dono: *"faça q esse "transformado" seja
	/// apenas no ssj4, ssj4 fp e ssj4 lb (pra lendario primal é a mesma coisa mas a diferença q seria no lssj4 dele etc)"*, *"lendario tb
	/// tem as lssj4 fp e lssj4 lb n esqueça delas"* e *"faça com q a técnica de ki em questao saia vermelha"*.
	///
	///   1. a escada INTEIRA das duas linhas, forma por forma (o tiro de 20% do Ki): 2x so nas seis do degrau SSJ4;
	///   2. o tiro de verdade (`Canalizar` -> o tique dos canais -> `NascerORaio`): no SSJ4 o raio nasce marcado (`Projetil.DoLimite`), com
	///      a onda 2x e o grito "10x"; o mesmo tiro no SSJ1 sai comum;
	///   3. o feixe cortado: a parte de la do raio vermelho continua vermelha;
	///   4. o Kamehameha x10: no SSJ4 o Kamehameha pronto dobra mesmo custando 1% do Ki, e o Galick Ho de 1% nao;
	///   5. CONTRA-EXEMPLOS: `Passivas.LimiteEmQualquerFormaDeTeste` (o SSJ1 volta a dobrar), `Passivas.LimiteSemVermelhoDeTeste` (o raio do
	///      SSJ4 sai 2x sem a marca) e `Passivas.KamehamehaPedeOPisoDeTeste` (o Kamehameha de 1% volta a sair 1x).
	/// O vermelho na TELA (o bit no fio e a cor do desenho) e da `--diaglimite`, com fotos.
	/// </summary>
	private void OLimiteSoNoSsj4(ServerPlayer p)
	{
		ServerPlayer lp = ForjarPs("Lendario Primal", "Saiyan", Passivas.ClasseLendariaPrimal, bp: 5e8, linhagem: "Primal Saiyan");
		(ServerPlayer Corpo, string Forma, bool Dobra)[] escada =
		[
			(p, "ssj1", false), (p, "ssj2", false), (p, "ssj3", false),
			(p, "ssj4", true), (p, "ssj4_full_power", true), (p, "ssj4_limit_breaker", true),
			(lp, "primal_c_type", false), (lp, "primal_legendary", false), (lp, "primal_legendary2", false), (lp, "primal_legendary3", false),
			(lp, "primal_legendary4", true), (lp, "primal_legendary4_full_power", true), (lp, "primal_legendary4_limit_breaker", true),
		];
		List<string> Erradas()
		{
			var erradas = new List<string>();
			foreach ((ServerPlayer corpo, string forma, bool dobra) in escada)
			{
				EntrarPs(corpo, forma);
				AndarPs(corpo, 0.1);
				double f = corpo.Racial.FatorDoLimite(20, 100);
				if (!PertoPs(f, dobra ? 2 : 1)) erradas.Add($"{forma} {f:0.##}x");
			}
			return erradas;
		}
		List<string> certo = Erradas();
		AfirmarPs("LIMITE SO NO SSJ4 (dono, 2026-10-19): forma por forma, o tiro de 20% do Ki sai 2x SO no SSJ4, SSJ4 FP e SSJ4 LB -- e, no "
				  + "Lendario Primal, so no LSSJ4, LSSJ4 FP e LSSJ4 LB; o SSJ1-3, o C-Type e o LSSJ1-3 ficam 1x",
				  certo.Count == 0, certo.Count == 0 ? $"{escada.Length} formas certas" : string.Join(", ", certo));
		Passivas.LimiteEmQualquerFormaDeTeste = true;
		try
		{
			List<string> antiga = Erradas();
			AfirmarPs("   (defeito injetado: `LimiteEmQualquerFormaDeTeste`, a regra de antes) o SSJ1-3 e o LSSJ1-3 voltam a dobrar o tiro -- a "
					  + "regua de cima REPROVA", antiga.Exists(e => e.StartsWith("ssj1 ")) && antiga.Exists(e => e.StartsWith("primal_legendary3 ")),
					  string.Join(", ", antiga));
		}
		finally { Passivas.LimiteEmQualquerFormaDeTeste = false; }

		// O TIRO DE VERDADE: o Kamehameha que custa 20% do Ki, carregado pelo tique dos canais de producao. `carga` = a marca vermelha da
		// CARGA na mao (`CargaDoLimite`, o bit do snapshot), lida com o canal aberto e o raio por nascer.
		bool carga = false;
		Projetil? Atirar(ServerPlayer corpo, string forma, List<string> falas, string verbo = Passivas.VerboDoKamehameha, string nome = "Kamehameha",
						 double fracao = 0.2)
		{
			EntrarPs(corpo, forma);
			AndarPs(corpo, 0.1);
			double custo = 10 * corpo.Ficha.BaseDrain();
			corpo.Ficha.Ki = custo / fracao;
			_ultimaFala.Remove(corpo.Id);   // (a fala de uma cena de cima nao engole o grito -- `MsEntreFalas`)
			var ouvidas = new List<(int Quem, Jandirus.Net.Protocol.Fala Canal, string Texto)>();
			var antes = EscutaDeFalas;
			EscutaDeFalas = ouvidas;
			try
			{
				Canalizar(corpo, verbo, custo, new ReceitaDeProjetil
				{
					Tipo = TipoDeProjetil.Beam, BaseDano = 1, Velocidade = 1, AlcanceTiles = 30, CargaMinima = 1,
					Deflectivel = false, Nome = nome,
				});
				carga = CargaDoLimite(corpo.Id);
				for (int i = 0; i < 200 && _canais.TryGetValue(corpo.Id, out CanalDeKi? c) && c.Raio == null; i++)
				{
					AdiantoDoRelogioDeTeste += 50;
					TickDosCanaisDeKi(0.05);
				}
			}
			finally { EscutaDeFalas = antes; }
			falas.AddRange(ouvidas.Where(o => o.Quem == corpo.Id).Select(o => o.Texto));
			return _canais.TryGetValue(corpo.Id, out CanalDeKi? canal) ? canal.Raio : null;
		}
		static string Raio(Projetil? r) => r == null ? "o raio nao nasceu" : $"raio #{r.Id}, marca {r.DoLimite}, onda {r.MultDeOnda:0.##}";

		var falas4 = new List<string>();
		Projetil? r4 = Atirar(p, "ssj4", falas4);
		bool carga4 = carga;
		AfirmarPs("O TIRO DE VERDADE NO SSJ4 (`Canalizar` -> tique dos canais -> `NascerORaio`): a carga e o raio nascem MARCADOS pro vermelho "
				  + "(`CargaDoLimite`, `Projetil.DoLimite`), com a onda 2x e o grito \"10x Kamehameha!!\"",
				  carga4 && r4 is { DoLimite: true } && PertoPs(r4.MultDeOnda, 2) && Disse(falas4, "10x Kamehameha!!"),
				  $"carga marcada {carga4}; {Raio(r4)} | {Ultimos(falas4)}");

		// O FEIXE CORTADO: um corpo pisa no tronco do raio vermelho e a parte de la nasce pelo `Cortar` de producao
		if (r4 != null)
		{
			float t = ZoneCollision.TileSize;
			List<Projetil> lista = ProjeteisDaZona(p.Zone.Hash);
			r4.Cauda = p.Pos + r4.Rumo * t;
			r4.Pos = p.Pos + r4.Rumo * (12 * t);
			ServerPlayer intruso = ForjarPs("intruso no feixe", "Human", onde: p.Pos + r4.Rumo * (6 * t));
			Cortar(r4, intruso, intruso.Pos, lista, p.Zone.Hash);
			Projetil? deLa = lista.Find(x => x.NascidoDoCorte == r4.Id);
			AfirmarPs("...e CORTADO (alguem pisou no tronco), a parte de la do raio continua vermelha",
					  deLa is { DoLimite: true }, deLa == null ? "a parte de la nao nasceu" : $"parte de la #{deLa.Id}, marca {deLa.DoLimite}");
		}
		SoltarDoRaio(p.Id);

		var falas1 = new List<string>();
		Projetil? r1 = Atirar(p, "ssj1", falas1);
		bool carga1 = carga;
		AfirmarPs("...e o MESMO tiro no SSJ1 sai comum: a carga e o raio sem a marca, a onda 1x e o grito sem o \"10x\"",
				  !carga1 && r1 is { DoLimite: false } && PertoPs(r1.MultDeOnda, 1) && Disse(falas1, "Kamehameha!!") && !Disse(falas1, "10x"),
				  $"carga marcada {carga1}; {Raio(r1)} | {Ultimos(falas1)}");
		SoltarDoRaio(p.Id);

		Passivas.LimiteSemVermelhoDeTeste = true;
		try
		{
			var falasD = new List<string>();
			Projetil? rd = Atirar(p, "ssj4", falasD);
			AfirmarPs("   (defeito injetado: `LimiteSemVermelhoDeTeste`) o tiro do SSJ4 sai 2x com a carga e o raio SEM a marca -- a regua do vermelho "
					  + "REPROVA", !carga && rd is { DoLimite: false } && PertoPs(rd.MultDeOnda, 2), $"carga marcada {carga}; {Raio(rd)} | {Ultimos(falasD)}");
		}
		finally { Passivas.LimiteSemVermelhoDeTeste = false; SoltarDoRaio(p.Id); }

		// O KAMEHAMEHA X10 (dono, 2026-10-19): no degrau SSJ4 o Kamehameha PRONTO dobra seja qual for o custo -- o tiro de 1% do Ki. As outras
		// tecnicas continuam pedindo os 10% (o Galick Ho de 1%). So o fator e o grito: a marca vermelha vem do mesmo `CanalDeKi.Limite`, e quem
		// a mede e o tiro de 20% acima.
		var falasK = new List<string>();
		Projetil? rk = Atirar(p, "ssj4", falasK, fracao: 0.01);
		SoltarDoRaio(p.Id);
		var falasG = new List<string>();
		Projetil? rg = Atirar(p, "ssj4", falasG, "GalicGun", "Galick Ho", 0.01);
		SoltarDoRaio(p.Id);
		AfirmarPs("O KAMEHAMEHA X10 (dono, 2026-10-19): no SSJ4 o Kamehameha pronto que custa so 1% do Ki sai 2x e com o \"10x\" -- e o Galick Ho "
				  + "de 1% continua 1x (so o Kamehameha dispensa os 10%)",
				  rk != null && PertoPs(rk.MultDeOnda, 2) && Disse(falasK, "10x Kamehameha!!")
				  && rg != null && PertoPs(rg.MultDeOnda, 1) && Disse(falasG, "Galick Ho!!") && !Disse(falasG, "10x"),
				  $"Kamehameha: {Raio(rk)} | {Ultimos(falasK)} || Galick Ho: {Raio(rg)} | {Ultimos(falasG)}");
		Passivas.KamehamehaPedeOPisoDeTeste = true;
		try
		{
			var falasKd = new List<string>();
			Projetil? rkd = Atirar(p, "ssj4", falasKd, fracao: 0.01);
			AfirmarPs("   (defeito injetado: `KamehamehaPedeOPisoDeTeste`, a regra de antes) o Kamehameha de 1% no SSJ4 sai 1x e sem o \"10x\" -- a regua do "
					  + "Kamehameha x10 REPROVA", rkd != null && PertoPs(rkd.MultDeOnda, 1) && !Disse(falasKd, "10x"), $"{Raio(rkd)} | {Ultimos(falasKd)}");
		}
		finally { Passivas.KamehamehaPedeOPisoDeTeste = false; SoltarDoRaio(p.Id); }
	}

	// =====================================================================
	// D. LENDARIO
	// =====================================================================
	private void OLendarioRacial()
	{
		GD.Print("[passivas] -- D. LENDARIO (o kit do Saiyajin e o dele) --");
		ServerPlayer l = ForjarPs("Lendario", "Saiyan", "Legendary", bp: 5e8, linhagem: "Saiyan");
		AndarPs(l, 0.1);
		AfirmarPs("o Lendario tem o kit do Saiyajin junto (o Warrior Race na vida baixa)",
				  (l.Racial.Entrada.Kit & KitRacial.Saiyajin) != 0, l.Racial.Entrada.Kit.ToString());

		// ---- Overflowing Power ----
		double troncoAntes = l.Combate.Corpo.Achar(Body.NomeDoTronco)!.Fracao;
		List<string> falas = Ouvir(() => UsarHabilidade(l, Passivas.Transbordar));
		AndarPs(l, 20.1, dt: 0.5);
		double troncoDepois = l.Combate.Corpo.Achar(Body.NomeDoTronco)!.Fracao;
		AfirmarPs("Overflowing Power: 20 s depois, BP +10% (5% a cada 10 s)", PertoPs(l.Ficha.racialBp, 1.10, 1e-4),
				  $"racialBp {l.Ficha.racialBp:0.####} | {Ultimos(falas)}");
		AfirmarPs("...e o tronco paga", troncoDepois < troncoAntes, $"tronco {troncoAntes:P1} -> {troncoDepois:P1}");
		falas = Ouvir(() => UsarHabilidade(l, Passivas.Transbordar));
		AfirmarPs("...desabafar antes dos 30 s e recusado", Disse(falas, "ainda nao da pra desabafar") && l.Racial.TransbordarDesde >= 0,
				  Ultimos(falas));
		AndarPs(l, 10.5, dt: 0.5);
		var jaHavia = new HashSet<int>(TirosDePs(l).Select(p => p.Id));
		falas = Ouvir(() => UsarHabilidade(l, Passivas.Transbordar));
		// (2026-10-17) O GRITO -- o dono: *"seriam 3 segundos do personagem gritando e uma esfera de energia verde parecendo com a do demonic em
		// volta do personagem e nesses 3 segundos kiblasts de forma nao ordenada /aleatoria saindo pra todo lado"*. Era a rajada de doze de uma vez.
		AfirmarPs("...depois dos 30 s o segundo toque DESABAFA: o buff sai e o Lendario comeca a GRITAR -- plantado (a trava de tecnica), o "
				  + "snapshot o manda na esfera verde (`Ocupacao.GritoDoDesabafo`), e nem verb de ki nem outra ativa saem no meio dele",
				  l.Racial.TransbordarDesde < 0 && l.Racial.GritoDesde >= 0 && OcupacaoDe(l) == Ocupacao.GritoDoDesabafo && !PodeMexerOCorpo(l)
				  && !PodeAtirar(l, 0, out string naoAtira) && naoAtira.Contains("desabafando") && Disse(Ouvir(() => UsarHabilidade(l, Passivas.ForcaImparavel)), "desabafando")
				  && SemImparavelPs(l),
				  $"ocupacao '{CorpoOcupado.Nome(OcupacaoDe(l))}' | {Ultimos(falas)}");
		GritoMedido grito = MedirOGritoPs(l, jaHavia);
		AfirmarPs($"...e o grito DURA {Passivas.SegundosDoGrito:0} s: os {Passivas.TirosDoGrito} tiros saem ESPALHADOS por ele (e nao de uma vez), ele "
				  + "cala no prazo, o corpo volta a andar e o BP volta ao de antes",
				  grito.Tiros == Passivas.TirosDoGrito && l.Racial.GritoTiros == Passivas.TirosDoGrito && grito.PrimeiroEm < 0.75
				  && grito.UltimoEm > Passivas.SegundosDoGrito - 0.75 && grito.CalouEm >= Passivas.SegundosDoGrito - 0.05
				  && grito.CalouEm <= Passivas.SegundosDoGrito + 0.15 && PodeMexerOCorpo(l) && OcupacaoDe(l) != Ocupacao.GritoDoDesabafo
				  && PertoPs(l.Ficha.racialBp, 1),
				  grito.Resumo);
		AfirmarPs("...em rumos SORTEADOS -- nao uma roda (as folgas entre rumos vizinhos variam muito) -- e em velocidades diferentes: dificil de "
				  + "desviar",
				  grito.FolgaMaior > 2.5 * grito.FolgaMenor && grito.Velocidades >= 3, grito.Resumo);
		GritoOrdenadoDeTeste = true;
		try
		{
			l.Racial.TransbordarDesde = NowMs() - (long)((Passivas.SegundosPraDesabafar + 1) * 1000);
			jaHavia = new HashSet<int>(TirosDePs(l).Select(p => p.Id));
			UsarHabilidade(l, Passivas.Transbordar);
			GritoMedido ordenado = MedirOGritoPs(l, jaHavia);
			AfirmarPs("   DEFEITO INJETADO (`GritoOrdenadoDeTeste`, o desabafo de antes: todos de uma vez, uma roda, uma velocidade) -- as duas "
					  + "provas de cima REPROVARIAM",
					  !(ordenado.UltimoEm > Passivas.SegundosDoGrito - 0.75) && !(ordenado.FolgaMaior > 2.5 * ordenado.FolgaMenor && ordenado.Velocidades >= 3),
					  ordenado.Resumo);
		}
		finally { GritoOrdenadoDeTeste = false; }

		// ---- Unstoppable Force ----
		falas = Ouvir(() => UsarHabilidade(l, Passivas.ForcaImparavel));
		AndarPs(l, 0.1);
		l.TiquesDeVoo = 0;
		Arremessar(l, new Vec2(1, 0), 1e9, 30);
		AfirmarPs("Unstoppable Force: sem recuo (o arremesso nao pega) e -10% no dano que chega",
				  l.Combate.Inabalavel && l.TiquesDeVoo == 0 && PertoPs(ChegaPs(l), 0.90),
				  $"inabalavel {l.Combate.Inabalavel}, voo {l.TiquesDeVoo}, chega {ChegaPs(l):0.###} | {Ultimos(falas)}");
		l.Combate.Stun = 2;
		l.Combate.Tick(0.05);
		AfirmarPs("...e nem atordoamento (o tique do combate zera o stun de quem e inabalavel)", l.Combate.Stun == 0, $"stun {l.Combate.Stun}");
		AndarPs(l, 30.5, dt: 1);
		l.TiquesDeVoo = 0;
		Arremessar(l, new Vec2(1, 0), 1e9, 30);
		AfirmarPs("...30 s depois, o arremesso volta a pegar", !l.Combate.Inabalavel && l.TiquesDeVoo > 0, $"voo {l.TiquesDeVoo}");
		l.TiquesDeVoo = 0;

		// ---- Escalating Power ----
		l.Combate.EmCombate = 90;
		EntrarPs(l, Catalogo.IdWrathful);
		VidaDosMembrosPs(l, 1);
		l.Ficha.stamina = l.Ficha.maxstamina;
		l.Ficha.Ki = l.Ficha.MaxKi;
		AndarPs(l, 0.1);
		AfirmarPs("Escalating Power: com os recursos cheios, nada", PertoPs(l.Racial.Efeito.Escalada, 0), $"{l.Racial.Efeito.Escalada:0.####}");
		l.Combate.Corpo.Achar(Body.NomeDoTronco)!.Vida = l.Combate.Corpo.Achar(Body.NomeDoTronco)!.VidaMax * 0.4;
		l.Ficha.stamina = l.Ficha.maxstamina * 0.4;
		l.Ficha.Ki = l.Ficha.MaxKi * 0.4;
		AndarPs(l, 0.1);
		double esperado = Passivas.BonusDaEscalada(0.4, 0.4, l.Ficha.Ki / l.Ficha.MaxKi);
		AfirmarPs("...com tronco, vigor e Ki em 40%, a forma sobe (30% x 0,5^1,5 = +10,6%)",
				  esperado > 0.10 && PertoPs(l.Racial.Efeito.Escalada, esperado, 1e-3), $"{l.Racial.Efeito.Escalada:0.####} (esperado {esperado:0.####})");
		l.Ficha.stamina = l.Ficha.maxstamina;
		AndarPs(l, 0.1);
		AfirmarPs("...e basta UM recurso acima de 80% pra parar", PertoPs(l.Racial.Efeito.Escalada, 0), $"{l.Racial.Efeito.Escalada:0.####}");
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// E. LENDARIO PRIMAL
	// =====================================================================
	private void OLendarioPrimalRacial()
	{
		GD.Print("[passivas] -- E. LENDARIO PRIMAL (o kit do Primal e o dele) --");
		ServerPlayer lp = ForjarPs("Lendario Primal", "Saiyan", "Legendary Primal Saiyan", bp: 5e8, linhagem: "Primal Saiyan");
		AndarPs(lp, 0.1);

		// ---- Overflowing Ki ----
		double maxAntes = lp.Ficha.MaxKi;
		List<string> falas = Ouvir(() => UsarHabilidade(lp, Passivas.TransbordarKi));
		AndarPs(lp, 30.1, dt: 0.5);
		AfirmarPs("Overflowing Ki: 30 s depois, o Ki maximo +30% (10% a cada 10 s)",
				  PertoPs(lp.Ficha.racialMaxKi, 1.30, 1e-6) && PertoPs(lp.Ficha.MaxKi / maxAntes, 1.30, 1e-3),
				  $"racialMaxKi {lp.Ficha.racialMaxKi:0.###}, MaxKi {maxAntes:0} -> {lp.Ficha.MaxKi:0} | {Ultimos(falas)}");
		falas = Ouvir(() => UsarHabilidade(lp, Passivas.TransbordarKi));
		AfirmarPs("...o desabafo dele so depois de 60 s", Disse(falas, "ainda nao da pra desabafar"), Ultimos(falas));
		AndarPs(lp, 30.5, dt: 0.5);
		var jaHavia = new HashSet<int>(TirosDePs(lp).Select(p => p.Id));
		falas = Ouvir(() => UsarHabilidade(lp, Passivas.TransbordarKi));
		bool gritando = lp.Racial.KiTransbordandoDesde < 0 && lp.Racial.GritoDesde >= 0 && OcupacaoDe(lp) == Ocupacao.GritoDoDesabafo;
		GritoMedido grito = MedirOGritoPs(lp, jaHavia);
		AfirmarPs("...aos 60 s desabafa no MESMO grito (os 3 s na esfera verde, os tiros espalhados), e o tanque volta",
				  gritando && grito.Tiros == Passivas.TirosDoGrito && grito.UltimoEm > Passivas.SegundosDoGrito - 0.75 && grito.CalouEm > 0
				  && PertoPs(lp.Ficha.racialMaxKi, 1),
				  $"{grito.Resumo}, racialMaxKi {lp.Ficha.racialMaxKi:0.###} | {Ultimos(falas)}");
		VidaDosMembrosPs(lp, 1);

		// ---- Primal Bloodlust ----
		ServerPlayer agressor = ForjarPs("agressor do Lendario Primal", "Human");
		falas = Ouvir(() => UsarHabilidade(lp, Passivas.SedeDeSangue));
		AndarPs(lp, 0.1);
		AfirmarPs("Primal Bloodlust: 15 golpes de armadura (na base), sem recuo, apanhando 5% MAIS",
				  lp.Racial.SedeGolpes == 15 && lp.Combate.Inabalavel && PertoPs(ChegaPs(lp), 1.05),
				  $"golpes {lp.Racial.SedeGolpes}, inabalavel {lp.Combate.Inabalavel}, chega {ChegaPs(lp):0.###} | {Ultimos(falas)}");
		for (int i = 0; i < 15; i++) AoGolpeRacial(agressor, lp, AcertoPs);
		AndarPs(lp, 0.1);
		AfirmarPs("...cada golpe levado vira +2% de dano: 15 golpes = +30% (vezes o +5% do rabo), e a armadura acaba",
				  lp.Racial.SedeGolpes == 0 && !lp.Combate.Inabalavel && PertoPs(lp.Combate.DanoDadoRacial, 1.05 * 1.30, 1e-6),
				  $"golpes {lp.Racial.SedeGolpes}, dano {lp.Combate.DanoDadoRacial:0.####}");
		ServerPlayer lp4 = ForjarPs("Lendario Primal no SSJ4", "Saiyan", "Legendary Primal Saiyan", bp: 5e8, linhagem: "Primal Saiyan");
		EntrarPs(lp4, "primal_legendary4");
		AndarPs(lp4, 0.1);
		UsarHabilidade(lp4, Passivas.SedeDeSangue);
		AfirmarPs("...no SSJ4 sao 20 golpes", lp4.Racial.SedeGolpes == 20, $"{lp4.Racial.SedeGolpes}");

		// ---- Demonic Saiyan ----
		ServerPlayer d = ForjarPs("Saiyajin Demoniaco", "Saiyan", "Legendary Primal Saiyan", bp: 5e8, linhagem: "Primal Saiyan");
		AndarPs(d, 0.1);
		string Recusa() { List<string> f = Ouvir(() => UsarHabilidade(d, Passivas.Demonico)); return Ultimos(f); }
		string r1 = Recusa();
		AfirmarPs("Demonic Saiyan, recusa 1: sem o Legendary Super Saiyan desperto", r1.Contains("desperto") && d.Racial.DemonicoCargaDesde < 0, r1);
		d.Forma.Liberar("primal_legendary");
		AndarPs(d, 0.1);
		string r2 = Recusa();
		AfirmarPs("...recusa 2: fora de uma forma Lendaria", r2.Contains("forma Lendaria"), r2);
		EntrarPs(d, "primal_legendary");
		AndarPs(d, 0.1);
		string r3 = Recusa();
		AfirmarPs("...recusa 3: fora de combate", r3.Contains("em combate"), r3);
		d.Combate.EmCombate = 90;
		d.Forma.CombateSegundos = 100;
		AndarPs(d, 0.1);
		string r4 = Recusa();
		AfirmarPs("...recusa 4: antes dos 2 min de luta", r4.Contains("ainda nao ferveu"), r4);
		d.Forma.CombateSegundos = 130;
		string carga = Recusa();
		AfirmarPs("...com tudo pago, a esfera verde comeca a carga -- e o corpo fica `CargaDemoniaca` (o que a tela le pra desenha-la)",
				  d.Racial.DemonicoCargaDesde >= 0 && carga.Contains("PARADO") && OcupacaoDe(d) == Ocupacao.CargaDemoniaca,
				  $"{carga} | ocupacao {OcupacaoDe(d)}");
		AndarPs(d, 5, dt: 0.5);
		d.Pos += new Vec2(2 * ZoneCollision.TileSize, 0);
		AndarPs(d, 0.5);
		AfirmarPs("...ANDAR desfaz a carga (e a recarga nem comecou), e a esfera sai da ocupacao",
				  d.Racial.DemonicoCargaDesde < 0 && EstadoRacial.FaltaDaRecarga(d.Ficha.RecargasRaciais, Passivas.Demonico, NowMs()) <= 0
				  && OcupacaoDe(d) != Ocupacao.CargaDemoniaca,
				  $"carga {d.Racial.DemonicoCargaDesde}, ocupacao {OcupacaoDe(d)}");
		Recusa();
		double maxKiBase = d.Ficha.MaxKi;
		d.Ficha.Ki = d.Ficha.MaxKi * 0.5;
		List<string> acendeu = Ouvir(() => AndarPs(d, 15.5, dt: 0.5));
		AfirmarPs("...15 s PARADO: acende, a recarga de 20 min comeca, e o Ki e o folego vao a 300% (o tanque triplica e enche)",
				  d.Racial.DemonicoDesde >= 0 && PertoPs(d.Ficha.racialMaxKi, 3) && PertoPs(d.Ficha.Ki, d.Ficha.MaxKi, 1)
				  && PertoPs(d.Ficha.MaxKi / maxKiBase, 3, 1e-3)
				  && EstadoRacial.FaltaDaRecarga(d.Ficha.RecargasRaciais, Passivas.Demonico, NowMs()) > 19 * 60,
				  $"desde {d.Racial.DemonicoDesde}, racialMaxKi {d.Ficha.racialMaxKi:0.##}, Ki {d.Ficha.Ki:0}/{d.Ficha.MaxKi:0} | {Ultimos(acendeu)}");
		AfirmarPs("...Mindless Beast (+10% de consumo, vezes o -5% do rabo) e Primitive Rage (+10% nos efeitos de membro)",
				  PertoPs(d.Ficha.racialCustoKi, 0.95 * 1.10, 1e-6) && PertoPs(d.Combate.FatorDosMembros, 1.10),
				  $"custo {d.Ficha.racialCustoKi:0.####}, membros {d.Combate.FatorDosMembros:0.##}");
		AndarPs(d, 10.5, dt: 0.5);
		AfirmarPs("...a janela de 10 s fecha (o tanque volta a 1x) ate o proximo ciclo", PertoPs(d.Ficha.racialMaxKi, 1),
				  $"racialMaxKi {d.Ficha.racialMaxKi:0.##}");
		d.Ficha.Ki = d.Ficha.MaxKi * 0.2;
		AndarPs(d, 5, dt: 0.5);
		AfirmarPs("...e aos 15 s ele ENCHE de novo a 300% -- nao soma 300 + 300, refaz os 300",
				  PertoPs(d.Ficha.racialMaxKi, 3) && PertoPs(d.Ficha.Ki, d.Ficha.MaxKi, 1), $"Ki {d.Ficha.Ki:0}/{d.Ficha.MaxKi:0}");
		d.Combate.EmCombate = 0;
		AndarPs(d, 0.1);
		AfirmarPs("...e o fim do combate o apaga", d.Racial.DemonicoDesde < 0 && PertoPs(d.Ficha.racialMaxKi, 1),
				  $"desde {d.Racial.DemonicoDesde}");

		// ---- Legendary Resurrection ----
		ServerPlayer res = ForjarPs("Lendario Primal que volta", "Saiyan", "Legendary Primal Saiyan", bp: 5e8, linhagem: "Primal Saiyan");
		AndarPs(res, 0.1);
		BodyPart tronco = res.Combate.Corpo.Achar(Body.NomeDoTronco)!;
		tronco.Vida = 0;
		res.Combate.SincronizarVida();
		res.Combate.Nocautear(60, porVital: true);
		List<string> volta = Ouvir(() => AndarPs(res, 0.1));
		AfirmarPs("Legendary Resurrection: nocauteado com o tronco quebrado, ele LEVANTA com 30% de volta e fica invulneravel 3 s",
				  !res.Ficha.KO && tronco.Fracao >= Passivas.RefilDaRessurreicao - 1e-6 && res.Combate.Invulneravel,
				  $"KO {res.Ficha.KO}, tronco {tronco.Fracao:P0}, invulneravel {res.Combate.Invulneravel} | {Ultimos(volta)}");
		AndarPs(res, 3.5, dt: 0.5);
		AfirmarPs("...depois da animacao, +10% em todos os stats", PertoPs(res.Ficha.racialPhysoff, Passivas.StatsDaRessurreicao)
				  && !res.Combate.Invulneravel, $"physoff {res.Ficha.racialPhysoff:0.##}");
		res.Combate.Nocautear(60);
		List<string> morte = Ouvir(() => AndarPs(res, 0.1));
		AfirmarPs("...e cair de novo nos 3 min e MORRER", res.Ficha.dead, $"dead {res.Ficha.dead} | {Ultimos(morte)}");

		ServerPlayer res2 = ForjarPs("Lendario Primal contra o golpe fatal", "Saiyan", "Legendary Primal Saiyan", bp: 5e8, linhagem: "Primal Saiyan");
		AndarPs(res2, 0.1);
		res2.Combate.Corpo.Achar(Body.NomeDoTronco)!.Vida = 0;
		bool morreu = res2.Combate.Morrer();
		AfirmarPs("...o golpe FATAL com o tronco quebrado tambem e negado pela ressurreicao (o gancho `NegarMorte`)",
				  !morreu && !res2.Ficha.dead && res2.Racial.RessurreicaoBuffAte > NowMs(), $"morreu {morreu}");
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// F. MEIO-SAIYAJIN
	// =====================================================================
	private void OMeioSaiyajinRacial()
	{
		GD.Print("[passivas] -- F. MEIO-SAIYAJIN --");
		ServerPlayer h = ForjarPs("meio-Saiyajin", Catalogo.RacaMeioSaiyajin, "New Generation", bp: 5e8);
		AndarPs(h, 0.1);

		// ---- Prodigy ----
		EntrarPs(h, "ssj1");
		AndarPs(h, 0.1);
		AfirmarPs("Prodigy: a forma mortal nova ja comeca com 50% de maestria", PertoPs(h.Forma.Maestria.De("ssj1"), 50),
				  $"{h.Forma.Maestria.De("ssj1"):0.#}%");
		h.Forma.Maestria.Por("ssj1", 100);
		EntrarPs(h, "ssj1");
		AndarPs(h, 0.1);
		double m = Math.Max(h.Ficha.ssjBuff, 1);
		AfirmarPs("...dominada, +0,5x no multiplicador da forma e a regen ativa de Ki +25%",
				  PertoPs(h.Ficha.racialBp, (m + 0.5) / m, 1e-4) && PertoPs(h.Ficha.racialRegenKiAtiva, 1.25),
				  $"forma {m:0.##}x, racialBp {h.Ficha.racialBp:0.####}, regen {h.Ficha.racialRegenKiAtiva:0.##}");

		// ---- Immeasurable Potential ----
		AfirmarPs("Immeasurable Potential: o teto da carga de Ki vai de 200% a 225%", PertoPs(h.Ficha.racialTetoDaCarga, 2.25 / 2.0),
				  $"{h.Ficha.racialTetoDaCarga:0.####}");

		// ---- Hidden Talent ----
		// A TECNICA DE KI SAI DE UMA SKILL DE MENTE: o Masenko e o degrau 30 da Basic Beam Mastery (`niveis.json`), e "dominada" e essa
		// familia no nivel maximo (100). (Uma skill sem regra de nivel -- a do cargo, por exemplo -- nunca sobe, e nunca domina.)
		const string Familia = "/datum/skill/mind/Basic_Beam_Mastery";
		h.Niveis.Por(Familia, 50);
		var tiro = new Projetil { Nome = "Masenko", Dono = h.Id, Bp = 1000 };
		FatorRacialDoDisparo(h, tiro, "Masenko");
		AfirmarPs("Hidden Talent: com a familia no nivel 50 de 100, a tecnica NAO esta dominada e sai como e",
				  PertoPs(tiro.Bp, 1000) && !TecnicaDominada(h, "Masenko"), $"{tiro.Bp:0}");
		h.Niveis.Por(Familia, 100);
		// A FILA E DE QUEM USA, dominada ou nao: o tiro de cima ja a abriu com o Masenko. Uma tecnica OUTRA a recomeca.
		FatorRacialDoDisparo(h, new Projetil { Nome = "Kienzan", Dono = h.Id, Bp = 1000 }, "Kienzan");
		var t1 = new Projetil { Nome = "Masenko", Dono = h.Id, Bp = 1000 };
		var t2 = new Projetil { Nome = "Masenko", Dono = h.Id, Bp = 1000 };
		var t3 = new Projetil { Nome = "Masenko", Dono = h.Id, Bp = 1000 };
		FatorRacialDoDisparo(h, t1, "Masenko");
		FatorRacialDoDisparo(h, t2, "Masenko");
		FatorRacialDoDisparo(h, t3, "Masenko");
		AfirmarPs("...no nivel 100 ela esta dominada: +10%; a MESMA de novo sai 2x na segunda; e a fila recomeca",
				  TecnicaDominada(h, "Masenko") && PertoPs(t1.Bp, 1100) && PertoPs(t2.Bp, 2200) && PertoPs(t3.Bp, 1100),
				  $"{t1.Bp:0}, {t2.Bp:0}, {t3.Bp:0}");

		// ---- Blazing Spirit ----
		BodyPart? braco = h.Combate.Corpo.Achar(BracoDaBancadaPs);
		if (braco != null) braco.Vida = braco.VidaMax * 0.4;
		h.Combate.SincronizarVida();
		AndarPs(h, 0.1);
		AfirmarPs("Blazing Spirit: um membro abaixo de 50% -> -20% de dano e sem efeitos negativos",
				  PertoPs(ChegaPs(h), 0.80) && h.Combate.SemEfeitosNegativos && h.Combate.EscalaDosMembros == 0,
				  $"chega {ChegaPs(h):0.###}, semNegativos {h.Combate.SemEfeitosNegativos}");
		AndarPs(h, 10.5, dt: 0.5);
		AfirmarPs("...o 'sem negativos' dura 10 s, e a reducao continua", !h.Combate.SemEfeitosNegativos && PertoPs(ChegaPs(h), 0.80),
				  $"semNegativos {h.Combate.SemEfeitosNegativos}, chega {ChegaPs(h):0.###}");
		AndarPs(h, 50, dt: 2);
		AfirmarPs("...ate 1 min", PertoPs(ChegaPs(h), 1), $"chega {ChegaPs(h):0.###}");
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// G. HUMANO
	// =====================================================================
	private void OHumanoRacial()
	{
		GD.Print("[passivas] -- G. HUMANO --");
		ServerPlayer h = ForjarPs("Humano", Catalogo.RacaHumano, "", bp: 1e9);
		AndarPs(h, 0.1);

		// ---- Persistence ----
		h.Ficha.train = true;
		AndarPs(h, 600, dt: 10);
		AfirmarPs("Persistence: 10 min de treino sem parar = ganho de treino 2x", PertoPs(h.Ficha.racialGanhoDeTreino, 2, 1e-6),
				  $"{h.Ficha.racialGanhoDeTreino:0.###}");
		AndarPs(h, 1200, dt: 10);
		AfirmarPs("...e para em 3x", PertoPs(h.Ficha.racialGanhoDeTreino, 3), $"{h.Ficha.racialGanhoDeTreino:0.###}");
		h.Ficha.train = false;
		AndarPs(h, 0.1);
		AfirmarPs("...parar de treinar zera", PertoPs(h.Ficha.racialGanhoDeTreino, 1), $"{h.Ficha.racialGanhoDeTreino:0.###}");

		// ---- Second Wind ----
		h.Ficha.Ki = h.Ficha.MaxKi * 0.2;
		h.Ficha.stamina = h.Ficha.maxstamina * 0.2;
		List<string> falas = Ouvir(() => UsarHabilidade(h, Passivas.SegundoFolego));
		AndarPs(h, 20, dt: 0.5);
		AfirmarPs("Second Wind: em 20 s devolve 50% do Ki e do vigor maximos (de 20% a 70%)",
				  PertoPs(h.Ficha.Ki / h.Ficha.MaxKi, 0.70, 0.02) && PertoPs(h.Ficha.stamina / h.Ficha.maxstamina, 0.70, 0.02),
				  $"Ki {h.Ficha.Ki / h.Ficha.MaxKi:P0}, vigor {h.Ficha.stamina / h.Ficha.maxstamina:P0} | {Ultimos(falas)}");
		falas = Ouvir(() => UsarHabilidade(h, Passivas.SegundoFolego));
		AfirmarPs("...recarga de 2 min", Disse(falas, "recarga"), Ultimos(falas));

		// ---- Sheer Willpower ----
		falas = Ouvir(() => UsarHabilidade(h, Passivas.ForcaDeVontade));
		AndarPs(h, 0.1);
		bool morreu = h.Combate.Morrer();
		AfirmarPs("Sheer Willpower: o golpe fatal e negado -- de pe, por um fio -- e nada de efeito negativo",
				  !morreu && !h.Ficha.dead && h.Combate.SemEfeitosNegativos, $"morreu {morreu} | {Ultimos(falas)}");
		AndarPs(h, 15.5, dt: 0.5);
		AfirmarPs("...por 15 s", !h.Combate.SemEfeitosNegativos && !h.Racial.Efeito.NegaMorte, $"semNegativos {h.Combate.SemEfeitosNegativos}");

		// ---- Max Power ----
		VidaDosMembrosPs(h, 1);
		h.Ficha.Ki = h.Ficha.MaxKi;
		falas = Ouvir(() => UsarHabilidade(h, Passivas.PoderMaximo));
		AndarPs(h, 0.1);
		AfirmarPs("Max Power: ataque fisico, defesa fisica e velocidade 1,5x",
				  PertoPs(h.Ficha.racialPhysoff, 1.5) && PertoPs(h.Ficha.racialPhysdef, 1.5) && PertoPs(h.Ficha.racialSpeed, 1.5),
				  $"{h.Ficha.racialPhysoff:0.##} {h.Ficha.racialPhysdef:0.##} {h.Ficha.racialSpeed:0.##} | {Ultimos(falas)}");
		double kiAntes = h.Ficha.Ki;
		AndarPs(h, 10, dt: 1);
		AfirmarPs("...drena 1,5% do Ki maximo por segundo ligado", PertoPs((kiAntes - h.Ficha.Ki) / h.Ficha.MaxKi, 0.015 * 10, 0.005),
				  $"{(kiAntes - h.Ficha.Ki) / h.Ficha.MaxKi:P1} em 10 s");
		ServerPlayer saco = ForjarPs("saco de pancada do Humano", "Human");
		kiAntes = h.Ficha.Ki;
		AoGolpeRacial(h, saco, AcertoPs);
		AfirmarPs("...e 1% a cada soco", PertoPs((kiAntes - h.Ficha.Ki) / h.Ficha.MaxKi, 0.01, 1e-6), $"{(kiAntes - h.Ficha.Ki) / h.Ficha.MaxKi:P2}");
		h.Ficha.Ki = h.Ficha.MaxKi * 0.001;
		List<string> acabou = Ouvir(() => AndarPs(h, 1));
		AfirmarPs("...quando o Ki acaba, ele se desfaz (e avisa)", !h.Racial.PoderMaximo && Disse(acabou, "MAX POWER se desfaz"),
				  Ultimos(acabou));
		AndarPs(h, 0.1);
		AfirmarPs("...e o fisico volta a 1x", PertoPs(h.Ficha.racialPhysoff, 1), $"{h.Ficha.racialPhysoff:0.##}");

		// ---- a linha da Tensao, pela tecla C ----
		ServerPlayer t = ForjarPs("Humano tenso", Catalogo.RacaHumano, "", bp: 1e10);
		t.Forma.Limiares = LimiaresPessoais.Rolar(Catalogo.RacaHumano, "", LimiaresPessoais.SementeDe("humano_tenso", 1_700_000_000_000));
		t.Ficha.Ki = t.Ficha.MaxKi;
		Transformar(t, subir: true);
		AfirmarPs("a TENSAO pela tecla C: da base, o Humano vira High Tension (1,5x)",
				  t.Forma.Atual == "alta_tensao" && PertoPs(t.Ficha.ssjBuff, 1.5), $"{t.Forma.Atual}, {t.Ficha.ssjBuff:0.##}x");
		t.Ficha.Ki = t.Ficha.MaxKi;
		Transformar(t, subir: true);
		AfirmarPs("...e de la, Extreme Tension (6x sem maestria)", t.Forma.Atual == "tensao_extrema" && PertoPs(t.Ficha.ssjBuff, 6),
				  $"{t.Forma.Atual}, {t.Ficha.ssjBuff:0.##}x");
		t.Ficha.Ki = t.Ficha.MaxKi;
		List<string> semDeus = Ouvir(() => Transformar(t, subir: true));
		AfirmarPs("...sem o ki divino, o Godly Tension nao vem", t.Forma.Atual == "tensao_extrema", $"{t.Forma.Atual} | {Ultimos(semDeus)}");
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// H. NAMEKUSEIJIN
	// =====================================================================
	private void ONamekuseijinRacial()
	{
		GD.Print("[passivas] -- H. NAMEKUSEIJIN --");
		Vec2 chao = CorredorLivre(10);
		ServerPlayer n = ForjarPs("Namekuseijin", "Namekian", "Warrior clan", bp: 1e7, onde: chao);
		ServerPlayer h = ForjarPs("Humano de controle", Catalogo.RacaHumano, "", bp: 1e7, onde: chao + new Vec2(5 * ZoneCollision.TileSize, 0));
		AndarPs(n, 0.1);
		AndarPs(h, 0.1);

		// ---- Resilience ----
		n.Combate.Nocautear(10);
		h.Combate.Nocautear(10);
		for (int i = 0; i < 5; i++) { n.Combate.Tick(1); h.Combate.Tick(1); }
		AfirmarPs("Namekian Resilience: o nocaute de 10 s levanta em 5 (o Humano ainda esta no chao)",
				  !n.Ficha.KO && h.Ficha.KO, $"namek KO {n.Ficha.KO}, humano KO {h.Ficha.KO} ({h.Combate.NocauteRestante:0.#} s)");
		h.Combate.Levantar();

		// ---- Warrior's Vitality ----
		AndarPs(n, 0.1);
		AfirmarPs("Warrior's Vitality: os efeitos de membro pela metade, e com os membros inteiros a regen passiva de Ki e vigor +25%",
				  PertoPs(n.Combate.EscalaDosMembros, 0.5) && PertoPs(n.Ficha.racialRegenKiPassiva, 1.25) && PertoPs(n.Ficha.racialRegenVigor, 1.25),
				  $"membros {n.Combate.EscalaDosMembros:0.##}, regen {n.Ficha.racialRegenKiPassiva:0.##}");
		BodyPart? braco = n.Combate.Corpo.Achar(BracoDaBancadaPs);
		if (braco != null) braco.Vida = braco.VidaMax * 0.1;
		AndarPs(n, 0.1);
		AfirmarPs("...com um membro QUEBRADO, a regen volta a 1x (os membros continuam pela metade)",
				  PertoPs(n.Ficha.racialRegenKiPassiva, 1) && PertoPs(n.Combate.EscalaDosMembros, 0.5), $"regen {n.Ficha.racialRegenKiPassiva:0.##}");
		if (braco != null) braco.Vida = braco.VidaMax;

		// ---- Spirit Disruption ----
		ServerPlayer alvo = ForjarPs("alvo da Disrupcao", Catalogo.RacaHumano, "", bp: 1e7);
		AndarPs(alvo, 0.1);
		double maxAlvo = alvo.Ficha.MaxKi;
		List<string> falas = Ouvir(() => UsarHabilidade(n, Passivas.Disrupcao));
		AfirmarPs("Spirit Disruption: a aura arma nos punhos", n.Racial.DisrupcaoArmada, Ultimos(falas));
		for (int i = 0; i < 4; i++) AoGolpeRacial(n, alvo, AcertoPs);
		AndarPs(alvo, 0.1);
		AfirmarPs("...quatro acertos limpos tiram 40% do Ki MAXIMO do alvo (10% cada), e o 4o desarma",
				  PertoPs(alvo.Ficha.racialMaxKi, 0.60, 1e-6) && PertoPs(alvo.Ficha.MaxKi / maxAlvo, 0.60, 1e-3) && !n.Racial.DisrupcaoArmada,
				  $"racialMaxKi {alvo.Ficha.racialMaxKi:0.##}, armada {n.Racial.DisrupcaoArmada}");
		AoGolpeRacial(n, alvo, AcertoPs);
		AndarPs(alvo, 0.1);
		AfirmarPs("...desarmada, o 5o golpe nao tira mais nada", PertoPs(alvo.Ficha.racialMaxKi, 0.60, 1e-6), $"{alvo.Ficha.racialMaxKi:0.##}");
		AndarPs(alvo, 30.5, dt: 1);
		AfirmarPs("...e o fluxo de ki do alvo se acerta em 30 s", PertoPs(alvo.Ficha.racialMaxKi, 1), $"{alvo.Ficha.racialMaxKi:0.##}");
		AdiantoDoRelogioDeTeste += 3 * 60 * 1000;
		UsarHabilidade(n, Passivas.Disrupcao);
		for (int i = 0; i < 4; i++) AoGolpeRacial(n, alvo, AparadoPs);
		AndarPs(alvo, 0.1);
		AfirmarPs("...quatro golpes APARADOS apagam a aura sem tirar nada", !n.Racial.DisrupcaoArmada && PertoPs(alvo.Ficha.racialMaxKi, 1),
				  $"armada {n.Racial.DisrupcaoArmada}, racialMaxKi {alvo.Ficha.racialMaxKi:0.##}");
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// I. FROST DEMON
	// =====================================================================
	private void OFrostDemonRacial()
	{
		GD.Print("[passivas] -- I. FROST DEMON --");
		Vec2 chao = CorredorLivre(14);
		const float T = ZoneCollision.TileSize;
		ServerPlayer fd = ForjarPs("Frost Demon", "Frost Demon", "Frost Demon", bp: 1e8, onde: chao);
		ServerPlayer humano = ForjarPs("Humano perto do Frost", Catalogo.RacaHumano, "", bp: 1e3, onde: chao + new Vec2(2 * T, 0));
		ServerPlayer longe = ForjarPs("Humano longe do Frost", Catalogo.RacaHumano, "", bp: 1e3, onde: chao + new Vec2(9 * T, 0));
		AndarPs(fd, 0.1);
		AndarPs(humano, 0.1);

		// ---- Perfect Control ----
		double cargaFrost = Projetil.SegundosDeCarga(30, fd.Ficha), cargaHumano = Projetil.SegundosDeCarga(30, humano.Ficha);
		AfirmarPs("Perfect Control: a carga de tecnica de ki leva a METADE do tempo",
				  PertoPs(fd.Ficha.racialCargaDeTecnica, 2) && cargaFrost < cargaHumano, $"frost {cargaFrost:0.##} s, humano {cargaHumano:0.##} s");
		double tiro = DanoDeKiRacial(new Projetil { Nome = "Death Beam", Dono = fd.Id }, fd, humano, 100);
		AfirmarPs("...e o dano de ki +15%", PertoPs(tiro, 115), $"{tiro:0.##}");

		// ---- Extreme Tenacity ----
		VidaDosMembrosPs(fd, 0.35);
		AndarPs(fd, 0.1);
		double a35 = ChegaPs(fd);
		VidaDosMembrosPs(fd, 0.08);
		AndarPs(fd, 0.1);
		double a08 = ChegaPs(fd);
		AfirmarPs("Extreme Tenacity: o membro ferido apanha menos -- 10% a menos com 35% de vida, 25% com 8%",
				  PertoPs(a35, 0.90) && PertoPs(a08, 0.75) && PertoPs(fd.Combate.FatorDoNocaute, 0.5),
				  $"35%: {a35:0.###}, 8%: {a08:0.###}, nocaute {fd.Combate.FatorDoNocaute:0.##}");
		VidaDosMembrosPs(fd, 1);
		// O NUCLEO A 8% DERRUBOU O CORPO (abaixo de 20% o nucleo nocauteia -- e o golpe de producao fazendo o que faz). A ira pede ele de pe.
		if (fd.Ficha.KO) fd.Combate.Levantar();

		// ---- Unrestrained Wrath ----
		humano.TiquesDeVoo = longe.TiquesDeVoo = 0;
		List<string> falas = Ouvir(() => UsarHabilidade(fd, Passivas.Ira));
		AndarPs(fd, 0.1);
		AfirmarPs("Unrestrained Wrath: o KIAI joga longe quem esta a 3 tiles -- e so ele",
				  humano.TiquesDeVoo > 0 && longe.TiquesDeVoo == 0, $"perto {humano.TiquesDeVoo}, longe {longe.TiquesDeVoo} | {Ultimos(falas)}");
		AfirmarPs("...e a ira toma conta: BP +20%, Ki maximo -20%", PertoPs(fd.Ficha.racialBp, 1.2) && PertoPs(fd.Ficha.racialMaxKi, 0.8),
				  $"racialBp {fd.Ficha.racialBp:0.##}, racialMaxKi {fd.Ficha.racialMaxKi:0.##}");
		falas = Ouvir(() => UsarHabilidade(fd, Passivas.Ira));
		AfirmarPs("...engolir a ira antes de 30 s e recusado", fd.Racial.IraDesde >= 0 && Disse(falas, "ainda nao assentou"), Ultimos(falas));
		AndarPs(fd, 30.5, dt: 1);
		falas = Ouvir(() => UsarHabilidade(fd, Passivas.Ira));
		AndarPs(fd, 0.1);
		AfirmarPs("...depois de 30 s ela desliga, e a recarga de 3 min comeca", fd.Racial.IraDesde < 0 && PertoPs(fd.Ficha.racialBp, 1)
				  && EstadoRacial.FaltaDaRecarga(fd.Ficha.RecargasRaciais, Passivas.Ira, NowMs()) > 170, Ultimos(falas));

		// ---- Revitalizing Metamorphosis ----
		EntrarPs(fd, "frost5");
		AndarPs(fd, 0.1);
		BodyPart tronco = fd.Combate.Corpo.Achar(Body.NomeDoTronco)!;
		tronco.Vida = tronco.VidaMax * 0.5;
		BodyPart? braco = fd.Combate.Corpo.Achar(BracoDaBancadaPs);
		if (braco != null) { braco.Vida = 0; braco.Decepado = true; }
		fd.Combate.SincronizarVida();
		EntrarPs(fd, "frost6");
		falas = Ouvir(() => AndarPs(fd, 0.1));
		AfirmarPs("Revitalizing Metamorphosis: transformar com o tronco ferido cura +20% e devolve o membro decepado com 10%",
				  PertoPs(tronco.Fracao, 0.70, 1e-6) && braco is { Decepado: false } && PertoPs(braco.Fracao, 0.10, 1e-6),
				  $"tronco {tronco.Fracao:P0}, membro {(braco == null ? "?" : braco.Decepado ? "decepado" : braco.Fracao.ToString("P0"))} | {Ultimos(falas)}");
		AndarPs(fd, 0.1);   // a metamorfose e do servidor e anda DEPOIS dos canais: o +40% chega no tique seguinte
		AfirmarPs("...e a regen ativa de Ki +40% por 1 min", PertoPs(fd.Ficha.racialRegenKiAtiva, 1.4), $"{fd.Ficha.racialRegenKiAtiva:0.##}");
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// J. MUTANTE
	// =====================================================================
	private void OMutanteRacial()
	{
		GD.Print("[passivas] -- J. FROST DEMON MUTANTE (o kit do Frost Demon e o dele) --");
		Vec2 chao = CorredorLivre(14);
		const float T = ZoneCollision.TileSize;
		ServerPlayer mu = ForjarPs("Mutante", "Frost Demon", "Mutant Frost Demon", bp: 1e8, onde: chao);
		mu.Facing = Facing.East;
		AndarPs(mu, 0.1);

		// ---- Long-Awaited 100% ----
		mu.Ficha.powerMod = 0.5;
		AndarPs(mu, 0.1);
		mu.Ficha.powerMod = 1;
		AndarPs(mu, 0.1);
		AfirmarPs("Long-Awaited 100%: pular de 50% pra 100% de uma vez NAO conta", !mu.Racial.EsperaCumprida, "");
		mu.Ficha.powerMod = 0.5;
		AndarPs(mu, 0.1);
		foreach (double b in new[] { 0.6, 0.7, 0.8, 0.9, 1.0 }) { mu.Ficha.powerMod = b; AndarPs(mu, 0.5); }
		AfirmarPs("...subir DEVAGAR de 50% a 100% cumpre a espera: dano, defesa e velocidade +10%",
				  mu.Racial.EsperaCumprida && PertoPs(mu.Combate.DanoDadoRacial, 1.10) && PertoPs(mu.Ficha.racialSpeed, 1.10),
				  $"espera {mu.Racial.EsperaCumprida}, dano {mu.Combate.DanoDadoRacial:0.##}");
		mu.Ficha.powerMod = 0.9;
		AndarPs(mu, 0.1);
		AfirmarPs("...e sair dos 100% desfaz", !mu.Racial.EsperaCumprida && PertoPs(mu.Combate.DanoDadoRacial, 1), "");
		mu.Ficha.powerMod = 1;

		// ---- Sadistic Nature ----
		ServerPlayer saco = ForjarPs("saco do Mutante", Catalogo.RacaHumano, "", bp: 1e3);
		List<string> falas = Ouvir(() => UsarHabilidade(mu, Passivas.Sadismo));
		for (int i = 0; i < 3; i++) AoGolpeRacial(mu, saco, AcertoPs);
		AoGolpeRacial(mu, saco, AparadoPs);
		AndarPs(mu, 0.1);
		AfirmarPs("Sadistic Nature: tres golpes sem guarda = +15% de dano (o aparado nao conta)",
				  PertoPs(mu.Combate.DanoDadoRacial, 1.15), $"dano {mu.Combate.DanoDadoRacial:0.###} | {Ultimos(falas)}");
		AndarPs(mu, 60.5, dt: 2);
		AfirmarPs("...por 1 min", PertoPs(mu.Combate.DanoDadoRacial, 1), $"{mu.Combate.DanoDadoRacial:0.###}");

		// ---- Ruthless Mind ----
		ServerPlayer caido = ForjarPs("o caido", Catalogo.RacaHumano, "", bp: 1e3, onde: mu.Pos + new Vec2(0.9f * T, 0));
		ServerPlayer inimigo = ForjarPs("o inimigo que assiste", Catalogo.RacaHumano, "", bp: 1e3, onde: mu.Pos + new Vec2(6 * T, 0));
		ServerPlayer amigo = ForjarPs("o amigo que assiste", Catalogo.RacaHumano, "", bp: 1e3, onde: mu.Pos + new Vec2(7 * T, 0));
		amigo.Social.Fotografar(mu.Assinatura, mu.Name, mu.Race, mu.Genero, mu.Idade, NowMs());
		amigo.Social.Ficha(mu.Assinatura)!.Familiaridade = Convivio.FamiliaridadeExigida(Relacao.Bom);
		amigo.Social.Declarar(mu.Assinatura, Relacao.Bom);
		mu.Combate.Letal = false;
		falas = Ouvir(() => UsarHabilidade(mu, Passivas.MenteCruel));
		AfirmarPs("Ruthless Mind, recusa 1: sem o golpe letal", Disse(falas, "golpe letal") && mu.Racial.ExecucaoAlvo == 0, Ultimos(falas));
		mu.Combate.Letal = true;
		falas = Ouvir(() => UsarHabilidade(mu, Passivas.MenteCruel));
		AfirmarPs("...recusa 2: ninguem caido na frente", Disse(falas, "ninguem caido") && mu.Racial.ExecucaoAlvo == 0, Ultimos(falas));
		caido.Combate.Nocautear(600);
		falas = Ouvir(() => UsarHabilidade(mu, Passivas.MenteCruel));
		AfirmarPs("...com alguem caido na frente, a execucao comeca", mu.Racial.ExecucaoAlvo == caido.Id, Ultimos(falas));
		mu.Pos += new Vec2(0, 2 * T);
		falas = Ouvir(() => AndarPs(mu, 0.5));
		AfirmarPs("...ANDAR desfaz a execucao", mu.Racial.ExecucaoAlvo == 0 && !caido.Ficha.dead && Disse(falas, "se desfaz"), Ultimos(falas));
		mu.Pos -= new Vec2(0, 2 * T);
		mu.PosDoTiqueRacial = mu.Pos;
		UsarHabilidade(mu, Passivas.MenteCruel);
		falas = Ouvir(() => AndarPs(mu, 3.5, dt: 0.5));
		AndarPs(inimigo, 0.1);
		AndarPs(amigo, 0.1);
		AfirmarPs("...3 s parado: o caido MORRE, e a recarga de 5 min comeca",
				  caido.Ficha.dead && EstadoRacial.FaltaDaRecarga(mu.Ficha.RecargasRaciais, Passivas.MenteCruel, NowMs()) > 290,
				  $"morto {caido.Ficha.dead} | {Ultimos(falas)}");
		AfirmarPs("...quem assiste e NAO e amigo leva -10% em todos os stats por 2 min",
				  PertoPs(inimigo.Ficha.racialPhysoff, 0.9) && PertoPs(inimigo.Ficha.racialKidef, 0.9),
				  $"physoff {inimigo.Ficha.racialPhysoff:0.##}");
		AfirmarPs("...e o AMIGO (Bom pra cima, na aba People) nao", PertoPs(amigo.Ficha.racialPhysoff, 1), $"physoff {amigo.Ficha.racialPhysoff:0.##}");
		LimparABancadaDasPassivas();
	}

	// =====================================================================
	// K. O SAVE
	// =====================================================================
	private void ARecargaVaiProSave()
	{
		GD.Print("[passivas] -- K. O SAVE: a recarga atravessa o relog, o buff nao --");
		ServerPlayer h = ForjarPs("Humano que desloga", Catalogo.RacaHumano, "");
		AndarPs(h, 0.1);
		UsarHabilidade(h, Passivas.ForcaDeVontade);
		UsarHabilidade(h, Passivas.PoderMaximo);
		AndarPs(h, 0.1);
		var opcoes = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
		string json = System.Text.Json.JsonSerializer.Serialize(h.Ficha, opcoes);
		Fighter? volta = System.Text.Json.JsonSerializer.Deserialize<Fighter>(json, opcoes);
		AfirmarPs("a RECARGA vai pro save (`Fighter.RecargasRaciais`): deslogar nao zera os 10 min do Sheer Willpower",
				  volta != null && volta.RecargasRaciais.TryGetValue(Passivas.ForcaDeVontade, out long ate) && ate > NowMs() + 9 * 60 * 1000,
				  json.Contains("RecargasRaciais") ? "" : "sem o campo no json");
		AfirmarPs("...e os CANAIS raciais nao vao (o Max Power ligado nao volta ligado)",
				  !json.Contains("racialPhysoff") && !json.Contains("racialBp") && volta is { racialPhysoff: 1 },
				  $"racialPhysoff {volta?.racialPhysoff}");
		LimparABancadaDasPassivas();
	}
}
