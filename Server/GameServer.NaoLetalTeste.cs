using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--naoletalteste` -- O GOLPE NAO-LETAL NAO MATA NINGUEM. O dono (2026-10-19): *"modo nao letal ainda ta matando as vezes"*.
///
/// A CAUSA TEM DUAS METADES, e o "as vezes" e o encontro delas:
///   * QUEM ZERAVA SEM PISO. O `Espalhar` (`GameServer.Empurrao.cs`) descia todo membro ate zero, nucleo incluso, sem perguntar o golpe
///     de ninguem -- e ele e o baque do arremesso (na parede e no corpo do caminho), o arremesso do agarrao, a cambalhota de quem se solta
///     e a fome. No DM os quatro sao nao-letais ou levam o `murderToggle` de quem bate (ver o `Espalhar`).
///   * QUEM MATAVA PELO ESTADO. As duas portas da morte por golpe (`MeleeResolver.AplicarNoMembro` e `ConsequenciaDoDano`) perguntam
///     `DeveMorrer` -- o corpo INTEIRO --, e o piso do nao-letal so segura o membro que o golpe pegou. O corpo que o `Espalhar` deixava de
///     pe com a cabeca zerada morria no PROXIMO golpe de qualquer um: o soco nao-letal no braco matava, e o soco na cabeca nao (o piso a
///     subia de volta) -- dai o sorteio.
///
/// AS FAMILIAS, pelos funis de producao, com corpos forjados na Terra (sem cliente -- e corpo sem cliente e o que um NPC e):
///   1. O BAQUE NAO MATA: um corpo A UM FIO (os tres nucleos em <see cref="FioNl"/>, o que um golpe LETAL de outro deixaria) jogado contra
///      outro corpo a um fio e contra uma parede: os dois continuam a um fio, e nao em zero.
///   2. O ARREMESSO DO AGARRAO E DE QUEM JOGA: o caido (nucleos no piso) que um forte nao-letal joga continua no piso; o mesmo arremesso de
///      quem luta pra matar o zera (o controle: a dose e de verdade).
///   3. A CAMBALHOTA E DE QUEM SE SOLTA: quem se solta sem matar deixa quem o segurava no piso; matando, zera.
///   4. A FOME NAO ZERA: uma passada de fome no corpo a um fio o deixa a um fio (e doi no resto).
///   5. O MORTO-VIVO NAO MORRE DE GOLPE NAO-LETAL: um corpo de pe com os tres nucleos zerados (o que o `Espalhar` deixava) leva o golpe
///      nao-letal por cada porta -- o dano pronto na guarda, o dano direto, o estrangulamento, a explosao de ki em quem defende, a estocada
///      da cauda do bio e o dano em area (num corpo sem cabeca: ver la) -- e CAI, sem morrer. O mesmo golpe letal o mata (o controle).
///   6. O CASO DO DONO, DE PONTA A PONTA: o fraco derrubado no soco por um forte que luta sem matar, jogado pelo agarrao e socado de novo,
///      continua vivo.
///
/// CONTRA-EXEMPLOS: <see cref="EspalharSemPisoDeTeste"/> (familias 1 a 4) e <see cref="NaoLetal.MataPeloEstadoDeTeste"/>
/// (familia 5) -- e os dois juntos, o jogo de antes inteiro, na 6. Com eles as mesmas reguas reprovam.
///
/// Os habitantes que estiverem na cena saem do mundo antes dela (`RemoverNpc`; o povoamento os repoe), e a bancada diz quem saiu. Roda no
/// boot e o servidor continua de pe.
///
///     Godot --headless --path . --server --port 8940 --naoletalteste
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--naoletalteste`): o `Espalhar` volta a descer todo membro ate zero, letal ou nao -- o jogo de antes de
	/// 2026-10-19. Lido pela propria linha do piso (`GameServer.Empurrao.cs`). Mora aqui, e nao ao lado dela, pra este arquivo compilar
	/// sozinho contra o repo de antes do conserto. Falso em jogo, sempre.
	/// </summary>
	internal static bool EspalharSemPisoDeTeste;

	private int _nlOk, _nlFalhou, _nlFileira;

	private void AfirmarNl(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _nlOk++; GD.Print($"[naoletal]   OK    {oque}"); return; }
		_nlFalhou++;
		GD.PrintErr($"[naoletal]   FALHA {oque}   {detalhe}");
	}

	/// <summary>Os tres nucleos -- os que matam (`Body.DeveMorrer`).</summary>
	private static readonly string[] NucleosNl = ["Cabeca", "Torso", "Abdomen"];

	/// <summary>
	/// A UM FIO: abaixo da dose de um baque (o voo mais longo, 10 tiques, espalhados pelos 15 membros: 0,67) e acima de zero -- o que um
	/// golpe LETAL de outro deixa. E abaixo do piso do nao-letal, entao a regra do `DamageMe` (nao sobe quem ja estava abaixo) e medida junto.
	/// </summary>
	private const double FioNl = 0.3;

	/// <summary>A luta pra escapar la no alto: a cambalhota sai certa (`prob(chance * grabCounter)`), e o dano dela -- `DanoBase + grabCounter` -- passa de longe a vida de quem segura.</summary>
	private const double ContadorDaCambalhotaNl = 1e6;

	public void RodarBancadaDoNaoLetal()
	{
		_nlOk = _nlFalhou = 0;
		_nlFileira = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[naoletal] ================ O GOLPE NAO-LETAL NAO MATA NINGUEM ================");
		try
		{
			AfirmarNl("(premissa) a zona da bancada tem colisao carregada", _pjMapa != null);
			if (_pjMapa == null) return;
			NlOBaque();
			NlOArremessoDoAgarrao();
			NlACambalhota();
			NlAFome();
			NlOMortoVivo();
			NlOCasoDoDono();
		}
		catch (Exception e)
		{
			_nlFalhou++;
			GD.PrintErr($"[naoletal]   FALHA a bancada rodou inteira   {e}");
		}
		finally
		{
			// OS BOTOES DE DEFEITO VOLTAM AO LUGAR, e o placar sai daqui haja o que houver.
			EspalharSemPisoDeTeste = false;
			NaoLetal.MataPeloEstadoDeTeste = false;
			try { LimparTudoDaBancada(); } catch (Exception e) { GD.PrintErr($"[naoletal]   (limpeza) {e.Message}"); }
			GD.Print($"[naoletal] ================ {_nlOk} OK, {_nlFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// A INFRAESTRUTURA
	// =====================================================================

	/// <summary>
	/// Um corpo da bancada (o `Forjar` das bancadas de projetil: Humano, na Terra, limpo pelo <see cref="LimparTudoDaBancada"/>) com os
	/// atributos em 5. Sem eles o `DanoDoArremesso` zera sozinho (`meu = 0` quando forca e tecnica nao passam de 1, `Throw.dm:21-25`).
	/// </summary>
	private ServerPlayer ForjarNl(string nome, Vec2 onde, double bp)
	{
		ServerPlayer c = Forjar(nome, onde, bp);
		Fighter f = c.Ficha;
		f.physoff = f.physdef = f.technique = f.kioff = f.kidef = f.kiskill = f.speed = f.magiskill = 5;
		f.maxstamina = f.stamina = 100;
		f.Statify();
		f.Ki = f.MaxKi;
		f.Tick(agoraMs: NowMs());
		return c;
	}

	private static void NucleosEmNl(ServerPlayer c, double vida)
	{
		foreach (string n in NucleosNl) c.Combate.Corpo.Achar(n)!.Vida = vida;
		c.Combate.SincronizarVida();
	}

	private static double MenorNucleoNl(ServerPlayer c) => NucleosNl.Min(n => c.Combate.Corpo.Achar(n)!.Vida);

	/// <summary>O braco esquerdo: inteiro no comeco de toda cena, entao "perdeu vida" diz que o dano ACONTECEU (a precondicao das reguas de piso).</summary>
	private static double BracoNl(ServerPlayer c) => c.Combate.Corpo.Achar("Braco esquerdo")!.Vida;

	/// <summary>O piso do nao-letal num membro inteiro (`NaoLetal.Piso`, 19,8 de 100).</summary>
	private static double PisoNl => NaoLetal.Piso(100);

	/// <summary>Os tiques que importam pro arremesso -- a grade de corpos e o voo --, e nada que cure ou mexa na vida por fora.</summary>
	private void TiquesNl(int n)
	{
		for (int i = 0; i < n; i++)
		{
			MontarAsGrades();
			TickDoEmpurrao();
		}
	}

	/// <summary>
	/// UM CORREDOR DE TERRA FIRME de `tiles` pra leste, nas tres fileiras que a caixa do corpo toca: sem parede, sem agua e longe da beirada
	/// (o criterio do `TerraFirmeG12`, com a fileira desta bancada). Devolve o meio da primeira celula.
	/// </summary>
	private Vec2 ChaoNl(int tiles)
	{
		ZoneCollision mapa = _pjMapa!;
		for (int y = _nlFileira; y < mapa.Height - 2; y++)
			for (int x = 4; x < mapa.Width - tiles - 2; x++)
			{
				if (!LivreNl(mapa, x - 1, x + tiles, y)) continue;
				_nlFileira = y + 3;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}
		AfirmarNl("(premissa) achei um corredor de terra firme no mapa da bancada", false, $"a varredura passou da fileira {_nlFileira}");
		return CorredorLivre(tiles);
	}

	/// <summary>
	/// UMA PAREDE QUE RESISTE: a coluna `x+1` barrada nas tres fileiras da caixa, e `tiles` de terra firme a oeste dela. Devolve a largada
	/// (o meio da celula mais a oeste) e o X da face da parede.
	/// </summary>
	private (Vec2 Largada, float Face)? ParedeNl(int tiles)
	{
		ZoneCollision mapa = _pjMapa!;
		for (int y = _nlFileira; y < mapa.Height - 2; y++)
			for (int x = tiles + 4; x < mapa.Width - 2; x++)
			{
				if (!mapa.BlockedCell(x + 1, y - 1) || !mapa.BlockedCell(x + 1, y) || !mapa.BlockedCell(x + 1, y + 1)) continue;
				if (!LivreNl(mapa, x - tiles, x, y)) continue;
				_nlFileira = y + 3;
				return (new Vec2((x - tiles) * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16), (x + 1) * ZoneCollision.TileSize);
			}
		return null;
	}

	private static bool LivreNl(ZoneCollision mapa, int x0, int x1, int y)
	{
		for (int x = x0; x <= x1; x++)
			for (int dy = -1; dy <= 1; dy++)
				if (mapa.BlockedCell(x, y + dy) || mapa.EhAgua(x, y + dy) || mapa.NaBorda(x, y + dy)) return false;
		return true;
	}

	/// <summary>
	/// QUEM NAO E DA CENA SAI DELA: o habitante que passeia pelo corredor pararia o voo (ou levaria a topada no lugar do corpo da bancada).
	/// Sai pelo `RemoverNpc` -- o povoamento o repoe --, e a bancada diz quem foi.
	/// </summary>
	private void TirarQuemNaoEDaCenaNl(Vec2 centro, int tiles)
	{
		float raio = tiles * ZoneCollision.TileSize;
		foreach (ServerPlayer o in ZoneList(ZonaDaBancadaDeProjetil.Hash).ToList())
		{
			if (o.Id >= IdBaseDeProjetil || o.Peer != null || Vec2.Distance(o.Pos, centro) > raio) continue;
			GD.Print($"[naoletal]   (nota) {o.Name} estava na cena e saiu do mundo");
			RemoverNpc(o);
		}
	}

	/// <summary>Uma cena: monta, mede e limpa -- e por ser funcao o `Mutacao` a roda de novo, do zero, com e sem o defeito.</summary>
	private bool CenaNl(Func<bool> cena)
	{
		try { return cena(); }
		finally { LimparTudoDaBancada(); }
	}

	// =====================================================================
	// 1) O BAQUE NAO MATA
	// =====================================================================
	private void NlOBaque()
	{
		GD.Print("[naoletal] -- 1) o baque nao mata: um corpo a um fio contra outro corpo a um fio, e contra uma parede --");
		const float T = ZoneCollision.TileSize;
		string conta = "";

		// CONTRA OUTRO CORPO -- `Movement Effects.dm:79-80`: os dois apanham, quem voava e quem levou a topada.
		bool NoCorpo() => CenaNl(() =>
		{
			Vec2 o = ChaoNl(10);
			TirarQuemNaoEDaCenaNl(o + new Vec2(4 * T, 0), 10);
			ServerPlayer voa = ForjarNl("Arremessado", o, 1000), topa = ForjarNl("Topado", o + new Vec2(3 * T, 0), 1000);
			NucleosEmNl(voa, FioNl);
			NucleosEmNl(topa, FioNl);
			Arremessar(voa, new Vec2(1, 0), 1, Empurrao.TiquesMax);
			TiquesNl(20);
			bool bateu = BracoNl(voa) < 100 && BracoNl(topa) < 100;
			conta = $"bateu={bateu}; menor nucleo de quem voou {MenorNucleoNl(voa):0.###}, de quem levou a topada {MenorNucleoNl(topa):0.###}";
			return bateu && MenorNucleoNl(voa) >= FioNl - 1e-9 && MenorNucleoNl(topa) >= FioNl - 1e-9;
		});
		Mutacao(AfirmarNl,
				"**O BAQUE NO CORPO NAO ZERA NINGUEM**: os dois a um fio se arrebentam um no outro (os bracos perdem vida) e continuam a um fio",
				"o `Espalhar` sem piso, o jogo de antes",
				NoCorpo, () => EspalharSemPisoDeTeste = true, () => EspalharSemPisoDeTeste = false);
		GD.Print($"[naoletal]   (o corpo) {conta}");

		// CONTRA A PAREDE -- `Movement Effects.dm:72`: a parede que resiste para o voo, e quem voava apanha.
		bool NaParede() => CenaNl(() =>
		{
			if (ParedeNl(6) is not { } p) { conta = "nao achei parede que resiste com chao a oeste"; return false; }
			TirarQuemNaoEDaCenaNl(p.Largada, 10);
			ServerPlayer voa = ForjarNl("ContraOMuro", p.Largada, 1000);
			NucleosEmNl(voa, FioNl);
			// FORCA 1: abaixo da `ResistenciaPadrao`, a parede nao cai -- o voo PARA nela, que e o ramo do baque.
			Arremessar(voa, new Vec2(1, 0), 1, Empurrao.TiquesMax);
			TiquesNl(20);
			bool bateu = BracoNl(voa) < 100 && voa.Pos.X < p.Face && voa.TiquesDeVoo == 0;
			conta = $"bateu={bateu} (parou em x {voa.Pos.X:0}, a parede em {p.Face:0}); menor nucleo {MenorNucleoNl(voa):0.###}";
			return bateu && MenorNucleoNl(voa) >= FioNl - 1e-9;
		});
		Mutacao(AfirmarNl,
				"**O BAQUE NA PAREDE NAO ZERA**: o corpo a um fio para no muro que resiste, o braco perde vida, e os nucleos continuam a um fio",
				"o `Espalhar` sem piso, o jogo de antes",
				NaParede, () => EspalharSemPisoDeTeste = true, () => EspalharSemPisoDeTeste = false);
		GD.Print($"[naoletal]   (a parede) {conta}");
	}

	// =====================================================================
	// 2) O ARREMESSO DO AGARRAO E DE QUEM JOGA
	// =====================================================================
	private void NlOArremessoDoAgarrao()
	{
		GD.Print("[naoletal] -- 2) o arremesso do agarrao e de quem joga --");
		const float T = ZoneCollision.TileSize;
		double dose = 0;

		// O CAIDO (nucleos no piso: o nao-letal o derrubou) nos bracos de um forte, que o joga. Devolve o menor nucleo logo depois do
		// arremesso -- antes do voo, entao so a linha do arremesso esta sendo medida.
		double Jogar(bool letal) => Medir(() =>
		{
			Vec2 o = ChaoNl(10);
			TirarQuemNaoEDaCenaNl(o, 12);
			ServerPlayer forte = ForjarNl("Arremessador", o, 1_000_000), caido = ForjarNl("Caido", o + new Vec2(0.75f * T, 0), 1000);
			forte.Facing = Facing.East;
			forte.Combate.Letal = letal;
			NucleosEmNl(caido, PisoNl);
			caido.Combate.Nocautear(MeleeResolver.TetoDoNocaute, porVital: true);
			dose = Agarrao.DanoDoArremesso(forte.Ficha, caido.Ficha) / caido.Combate.Corpo.Partes.Count;
			Prender(forte, caido);
			ArremessarComOAgarrao(forte, caido);
			return MenorNucleoNl(caido);
		});

		double controle = Jogar(letal: true);
		AfirmarNl($"PRECONDICAO: o arremesso tira mais que o caido tem -- {dose:0.#} por membro contra os {PisoNl:0.#} do piso", dose > PisoNl);
		AfirmarNl("(o controle) o MESMO arremesso de quem luta pra MATAR zera os nucleos do caido -- a regua enxerga o dano letal",
				  controle <= 0, $"menor nucleo {controle:0.###}");
		Mutacao(AfirmarNl,
				"**O ARREMESSO DE QUEM LUTA SEM MATAR PARA NO PISO**: o caido que o forte nao-letal joga continua com os nucleos no piso, e nao em zero",
				"o `Espalhar` sem piso, o jogo de antes",
				() => Jogar(letal: false) >= PisoNl - 1e-9, () => EspalharSemPisoDeTeste = true, () => EspalharSemPisoDeTeste = false);
	}

	/// <summary>Como o <see cref="CenaNl"/>, pra uma cena que devolve um numero.</summary>
	private double Medir(Func<double> cena)
	{
		try { return cena(); }
		finally { LimparTudoDaBancada(); }
	}

	// =====================================================================
	// 3) A CAMBALHOTA E DE QUEM SE SOLTA
	// =====================================================================
	private void NlACambalhota()
	{
		GD.Print("[naoletal] -- 3) a cambalhota e de quem se solta --");
		const float T = ZoneCollision.TileSize;
		bool soltou = false;

		// QUEM SEGURA, inteiro; quem se solta, com a luta pra escapar la no alto. Devolve o menor nucleo de quem segurava.
		double Escapar(bool letal) => Medir(() =>
		{
			Vec2 o = ChaoNl(6);
			TirarQuemNaoEDaCenaNl(o, 10);
			ServerPlayer segura = ForjarNl("QuemSegura", o, 1000), preso = ForjarNl("QuemSeSolta", o + new Vec2(0.75f * T, 0), 1000);
			preso.Combate.Letal = letal;
			Prender(segura, preso);
			preso.ContadorDaLuta = ContadorDaCambalhotaNl;
			CambalhotaG11(preso);
			soltou = preso.AgarradoPorId == 0 && segura.AgarrandoId == 0;
			return MenorNucleoNl(segura);
		});

		double controle = Escapar(letal: true);
		AfirmarNl("(o controle) a cambalhota de quem luta pra MATAR solta e ZERA os nucleos de quem segurava -- a dose e de verdade",
				  soltou && controle <= 0, $"soltou={soltou}, menor nucleo {controle:0.###}");
		Mutacao(AfirmarNl,
				"**A CAMBALHOTA DE QUEM LUTA SEM MATAR PARA NO PISO**: solta, e quem segurava fica com os nucleos no piso",
				"o `Espalhar` sem piso, o jogo de antes",
				() => Escapar(letal: false) >= PisoNl - 1e-9 && soltou, () => EspalharSemPisoDeTeste = true, () => EspalharSemPisoDeTeste = false);
	}

	// =====================================================================
	// 4) A FOME NAO ZERA
	// =====================================================================
	private void NlAFome()
	{
		GD.Print("[naoletal] -- 4) a fome nao deixa a cabeca zerada --");
		string conta = "";

		// UMA PASSADA DO ESTOMAGO num corpo a um fio, sem comida e com o vigor no fundo: `DanoDaFome` cobra |vigor| x o acumulado.
		bool Passada() => CenaNl(() =>
		{
			ServerPlayer faminto = ForjarNl("Faminto", ChaoNl(3), 1000);
			try
			{
				Fighter f = faminto.Ficha;
				NucleosEmNl(faminto, FioNl);
				f.CurrentNutrition = 0;
				f.stamina = 0.9;
				_fomeAcumulada[faminto.Id] = 10;   // o teto do acumulado (`StaminaDrain.dm:71`)
				_relogioDoEstomago.Remove(faminto.Id);
				TickDoEstomago(faminto, Nutricao.SegundosPorPassada / Math.Max(f.Metabolism, 0.1) + 1e-6);
				bool doeu = BracoNl(faminto) < 100;
				conta = $"doeu={doeu} (braco {BracoNl(faminto):0.###}); menor nucleo {MenorNucleoNl(faminto):0.###}";
				return doeu && MenorNucleoNl(faminto) >= FioNl - 1e-9;
			}
			finally
			{
				_fomeAcumulada.Remove(faminto.Id);
				_relogioDoEstomago.Remove(faminto.Id);
			}
		});
		Mutacao(AfirmarNl,
				"**A FOME DOI E NAO ZERA**: uma passada de fome tira vida do braco e deixa os nucleos a um fio, e nao em zero (`SpreadDamage(x)` nao-letal)",
				"o `Espalhar` sem piso, o jogo de antes",
				Passada, () => EspalharSemPisoDeTeste = true, () => EspalharSemPisoDeTeste = false);
		GD.Print($"[naoletal]   (a fome) {conta}");
	}

	// =====================================================================
	// 5) O MORTO-VIVO NAO MORRE DE GOLPE NAO-LETAL
	// =====================================================================
	private void NlOMortoVivo()
	{
		GD.Print("[naoletal] -- 5) o morto-vivo nao morre de golpe nao-letal: as portas da morte so atendem o letal --");
		const float T = ZoneCollision.TileSize;

		// O CORPO DE PE COM OS TRES NUCLEOS ZERADOS -- o que o `Espalhar` sem piso deixava pro proximo golpe. Os tres e nao um: o golpe que
		// cai NUM nucleo o sobe pro piso, e com um so a regua dependeria do membro que o dado sorteasse.
		(bool Morreu, bool Caiu) Golpe(Action<ServerPlayer, ServerPlayer> bater)
		{
			(bool, bool) r = (false, false);
			CenaNl(() =>
			{
				Vec2 o = ChaoNl(4);
				ServerPlayer algoz = ForjarNl("Algoz", o, 1000), v = ForjarNl("MortoVivo", o + new Vec2(0.75f * T, 0), 1000);
				algoz.Facing = Facing.East;
				NucleosEmNl(v, 0);
				bater(algoz, v);
				r = (v.Ficha.dead, v.Ficha.KO);
				return true;
			});
			return r;
		}

		var portas = new (string Nome, Action<ServerPlayer, ServerPlayer> Bater)[]
		{
			("O DANO PRONTO NA GUARDA (o soco aparado e o ki na guarda, `MeleeResolver.AplicarNoMembro`)",
			 (a, v) => MeleeResolver.AplicarDanoProntoNaGuarda(v.Combate, 5, a.Combate.Letal, _rng)),
			("O DANO DIRETO (`FerirUmMembroG10` -> `ConsequenciaDoDano`)",
			 (a, v) => FerirUmMembroG10(v, a, 5, a.Combate.Letal, zona: "bracos")),
			("O ESTRANGULAMENTO (o aperto sobe a cabeca pro piso; o tronco e o abdomen continuam zerados)",
			 (a, v) => ApertarOPescoco(a, v)),
			("A EXPLOSAO DE KI EM QUEM DEFENDE (so os membros da guarda levam)",
			 (a, v) => ExplodirNl(a, v)),
			("A ESTOCADA DA CAUDA DO BIO (`EstocadaDeAbsorcao`: o braco esquerdo, o primeiro da ordem)",
			 (a, v) => EstocadaDeAbsorcao(a, v)),
			// O DANO EM AREA nao-letal sobe os nucleos que pega pro piso -- entao o corpo dele e o de cabeca JA ARRANCADA (o laco pula o
			// membro perdido), o unico jeito de a porta da morte do dano em area encontrar um nucleo cedido depois de um golpe nao-letal.
			("O DANO EM AREA NUM CORPO SEM CABECA (`EspalharDanoG3`: o tronco e o abdomen sobem pro piso, e a cabeca nao volta)",
			 (a, v) => { v.Combate.Corpo.Achar("Cabeca")!.Decepado = true; EspalharDanoG3(v, a, 5, a.Combate.Letal); }),
		};

		foreach ((string nome, Action<ServerPlayer, ServerPlayer> bater) in portas)
		{
			(bool morreuLetal, _) = Golpe((a, v) => { a.Combate.Letal = true; bater(a, v); });
			AfirmarNl($"(o controle) {nome}: o golpe LETAL mata o morto-vivo -- a regua enxerga a morte quando ela e devida", morreuLetal);
			Mutacao(AfirmarNl,
					$"**{nome}: O NAO-LETAL SO DERRUBA** -- o morto-vivo cai, e nao morre",
					"a porta da morte pelo estado do corpo, o jogo de antes",
					() => Golpe((a, v) => { a.Combate.Letal = false; bater(a, v); }) is { Morreu: false, Caiu: true },
					() => NaoLetal.MataPeloEstadoDeTeste = true, () => NaoLetal.MataPeloEstadoDeTeste = false);
		}
	}

	/// <summary>
	/// A EXPLOSAO DE UM TIRO DE KI de quem atira (o letal do tiro e o dele, como no `Disparar`) em quem esta DEFENDENDO: a explosao vai nos
	/// membros da guarda (`FerirComAExplosao`), e os nucleos ficam como estavam.
	/// </summary>
	private void ExplodirNl(ServerPlayer atirador, ServerPlayer v)
	{
		v.Combate.Bloqueando = true;
		var p = new Projetil { Dono = atirador.Id, Bp = atirador.Ficha.expressedBP, BaseDano = 5, MaxDano = 5, Letal = atirador.Combate.Letal };
		FerirComAExplosao(p, atirador, v, 1);
	}

	// =====================================================================
	// 6) O CASO DO DONO, DE PONTA A PONTA
	// =====================================================================
	private void NlOCasoDoDono()
	{
		GD.Print("[naoletal] -- 6) o caso do dono: derrubado no soco, jogado pelo agarrao e socado de novo, por quem luta sem matar --");
		const float T = ZoneCollision.TileSize;
		string conta = "";

		// DEZ MIL VEZES MAIS FORTE: o arremesso dele tira mais de 100 de cada membro, entao no jogo de antes os TRES nucleos zeravam -- e o
		// soco seguinte matava sem depender do membro sorteado (com um nucleo so, o soco que caisse nele o salvaria).
		bool Vive() => CenaNl(() =>
		{
			Vec2 o = ChaoNl(10);
			TirarQuemNaoEDaCenaNl(o, 12);
			ServerPlayer forte = ForjarNl("Forte", o, 10_000_000), fraco = ForjarNl("Fraco", o + new Vec2(0.75f * T, 0), 1000);
			forte.Facing = Facing.East;
			// 1. O SOCO NAO-LETAL DERRUBA -- pelo dano pronto, que e o mesmo `AplicarNoMembro` do soco, sem o sorteio da pontaria.
			for (int i = 0; i < 20 && !fraco.Ficha.KO; i++)
				MeleeResolver.AplicarDanoPronto(fraco.Combate, 1e6, forte.Combate.Letal, _rng, "torso", porKi: false);
			bool caiu = fraco.Ficha.KO;
			// 2. AGARRA E JOGA.
			Prender(forte, fraco);
			ArremessarComOAgarrao(forte, fraco);
			// 3. E SOCA DE NOVO: o caido nao esquiva nem defende (o `indefeso` do `Resolver`), entao todo soco encosta.
			for (int i = 0; i < 6; i++) MeleeResolver.Resolver(forte.Combate, fraco.Combate, 0, _rng);
			conta = $"caiu={caiu}, morto={fraco.Ficha.dead}, menor nucleo {MenorNucleoNl(fraco):0.###}";
			return caiu && !fraco.Ficha.dead && !fraco.Combate.Corpo.DeveMorrer();
		});

		Mutacao(AfirmarNl,
				"**O CASO DO DONO**: o fraco que um forte nao-letal derruba, joga pelo agarrao e soca de novo continua VIVO (e no chao)",
				"o jogo de antes inteiro: o `Espalhar` sem piso E a morte pelo estado do corpo",
				Vive,
				() => { EspalharSemPisoDeTeste = true; NaoLetal.MataPeloEstadoDeTeste = true; },
				() => { EspalharSemPisoDeTeste = false; NaoLetal.MataPeloEstadoDeTeste = false; });
		GD.Print($"[naoletal]   (o caso) {conta}");
	}

	// =====================================================================
	// O ROBO DO HUD (`--diaggolpe`, `Client/RoboDoGolpe.cs`)
	// =====================================================================

	/// <summary>(robo `--diaggolpe`) O golpe que o SERVIDOR aplica neste corpo agora -- o `Combate.Letal`, e nao o que a tela diz.</summary>
	internal bool LetalNoServidorDeTeste(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate?.Letal == true;

	/// <summary>
	/// (robo `--diaggolpe`) O SERVIDOR TROCA O GOLPE POR CONTA PROPRIA, escrevendo o campo como o fim do treino com o Whis e do torneio, a
	/// saida da meditacao profunda e a posse do Neo Tuffle escrevem -- sem passar pelo pacote da tecla.
	/// </summary>
	internal void TrocarOLetalNoServidorDeTeste(int id, bool letal)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null) pl.Combate.Letal = letal;
	}
}
