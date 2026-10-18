using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// BANCADA `--iaraciaisteste` -- A IA COM AS PASSIVAS E AS ATIVAS DA RACA (2026-10-17). O dono: *"n esqueça de fazer as IAs saberem usar
/// as passivas e ativas da raça, e os boss de saga tb vao ter todas essas passivas e ativas a depender de sua raça"*.
///
/// TUDO PELA PORTA DE PRODUCAO: o chefe nasce pela fabrica (`NascerNpc`, com os moldes do `npcs.json`), o corpo do mundo e o que o
/// `EhNpcDoMundo` diz (papel de NPC e nenhuma conta), as capacidades e a percepcao sao as do `TicarUmCorpo` (`LerCapacidades`,
/// `LerPercepcao`), o cerebro decide (`Pensar`), o atuador aperta pelo canal do jogador (`AplicarComando` -> `UsarHabilidade` ->
/// `UsarAtivaRacial`) e o tique racial e o de producao (`TickDasRaciais`). A bancada so monta a cena, anda o relogio
/// (`AdiantoDoRelogioDeTeste`) e le o estado racial -- o que o verb ACEITOU, e nao so o que o cerebro pediu.
///
///   A. O POVO E OS CHEFES TEM O KIT -- o Freeza e o Guardiao Saiyajin pela fabrica, com as passivas valendo; a copia sem kit.
///      CONTRA-EXEMPLO: `Passivas.SoPessoaTemKitDeTeste` (o portao da v0.4.0).
///   B. A MAO DA IA -- as ativas de cada kit no repertorio de luta, a recarga que tira, o toggle ligado, nenhuma das treze de fora, e a
///      leitura de 1 Hz sem lixo. CONTRA-EXEMPLO: `AtivasRaciaisNaIa.IaSemAtivasDeTeste`.
///   C. AS TRES RECEITAS PROPRIAS numa tabela de decisao (o cerebro sozinho, com a cena escrita): o Demonic (longe sim, colado nao), o
///      corpo parado pela raca, a execucao do Ruthless Mind e o desabafo (perto, a folga, a luta acabada, a pausa).
///      CONTRA-EXEMPLO: `Cerebro.SemDesabafoDeTeste`.
///   D. NA LUTA, PELO TIQUE DE PRODUCAO -- cada kit liga os reforcos dele, os socorros acodem, o Max Power cai com o Ki baixo, o Overflowing
///      Power desabafa antes de o tronco cobrar (com o defeito, o corpo cai sozinho), o Demonic carrega parado e a execucao mata o caido
///      so com o golpe letal.
///   E. O CHEFE DE SAGA NA LUTA -- o Freeza da fabrica liga a Ira dele.
///
/// COMO RODAR: `Godot --headless --path . --server --port 8456 --iaraciaisteste`
/// </summary>
public partial class GameServer
{
	private const int IdBaseDaIaRacialDeTeste = 99_700;

	private int _irOk, _irFalhou, _irCorpos;
	private double _irRestoDoRelogio;
	private readonly List<ServerPlayer> _irNascidos = [];

	private void AfirmarIr(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _irOk++; GD.Print($"[iaraciais]   OK    {oque}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); return; }
		_irFalhou++;
		GD.PrintErr($"[iaraciais]   FALHA {oque}   [{detalhe}]");
	}

	public void RodarBancadaDaIaRacial()
	{
		_irOk = _irFalhou = 0;
		GD.Print("[iaraciais] ================ A IA COM AS PASSIVAS E AS ATIVAS DA RACA (2026-10-17) ================");
		_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		AfirmarIr("a zona da bancada tem colisao carregada", _pjMapa != null);
		long relogioDeAntes = AdiantoDoRelogioDeTeste;
		EscutaDeTecnicasDaIa = [];
		try
		{
			OKitDoPovoEDosChefesIr();
			AMaoDaIaIr();
			AsReceitasPropriasIr();
			NaLutaIr();
			OChefeDeSagaNaLutaIr();
		}
		catch (Exception e)
		{
			AfirmarIr($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			Passivas.SoPessoaTemKitDeTeste = false;
			AtivasRaciaisNaIa.IaSemAtivasDeTeste = false;
			Cerebro.SemDesabafoDeTeste = false;
			EscutaDeTecnicasDaIa = null;
			AdiantoDoRelogioDeTeste = relogioDeAntes;
			LimparABancadaDaIaRacial();
		}
		GD.Print($"[iaraciais] ================ {_irOk} OK, {_irFalhou} FALHA(S) ================");
	}

	// =====================================================================
	// OS CORPOS, O RELOGIO E A LIMPEZA
	// =====================================================================
	/// <summary>
	/// Um corpo de bancada. `doMundo`: papel de NPC e nenhuma conta -- o povo do mundo (`EhNpcDoMundo`); sem ele, a COPIA (sem papel e sem
	/// conta). Perfil so corpo (nem voa, nem atira): a cena e a da raca, e nao a do ki. Os atributos sao os da `--iateste`.
	/// </summary>
	private ServerPlayer ForjarIr(string nome, string raca, string classe, Vec2 onde, string linhagem = "", bool doMundo = true,
																												  bool comCerebro = true)
	{
		int n = _irCorpos++;
		var ficha = new Fighter
		{
			Name = nome, Race = raca, BP = 1_000_000, Idade = 25,
			physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5, kiskill = 5, speed = 5, magiskill = 5,
		};
		ficha.Class = classe;
		ficha.SaiyanLineage = linhagem;
		var novo = new ServerPlayer
		{
			Id = IdBaseDaIaRacialDeTeste + n,
			Peer = null,
			Name = "bancada: " + nome,
			Race = raca,
			Class = classe,
			Genero = "Male",
			Idade = 25,
			Zone = ZonaDaBancadaDeProjetil,
			Pos = onde,
			LastInputMs = NowMs(),
			Ficha = ficha,
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Papel = doMundo ? new PapelDeNpc(new MoldeDeNpc { Id = "bancada_iaraciais", Nome = nome }, (ulong)(n + 1)) : null,
			Cerebro = comCerebro ? new Cerebro() : null,
			Perfil = PerfilDeCombate.SoCorpo,
		};
		PorNoMundo(novo);
		ficha.PowerLevel();
		ficha.Ki = ficha.MaxKi;
		novo.PosDoTiqueRacial = novo.Pos;
		TickDasRaciais(novo, 0.05);   // a entrada racial nasce montada (o kit, a forma)
		return novo;
	}

	private int _irProximaFileira = 8;

	/// <summary>
	/// UM CORREDOR SECO de <paramref name="tiles"/> celulas: livre de parede E de AGUA, numa faixa de tres fileiras (os pes ficam abaixo da
	/// posicao). O `CorredorLivre` da bancada de projetil so pergunta por parede -- e os corredores dele eram agua: o NPC nadava, o nado
	/// comia o Ki, e o Demonic recusa pe molhado. Deterministico; cada chamada continua de onde a anterior parou.
	/// </summary>
	private Vec2 CorredorSecoIr(int tiles)
	{
		ZoneCollision? mapa = _pjMapa;
		if (mapa == null) return new Vec2(_irProximaFileira * 64 + 32, 32);
		for (int y = Math.Max(_irProximaFileira, 1); y < 249; y++)
			for (int x = 4; x + tiles < 250; x++)
			{
				bool seco = true;
				for (int d = 0; d <= tiles && seco; d++)
					for (int dy = -1; dy <= 1 && seco; dy++)
						seco = !mapa.BlockedCell(x + d, y + dy) && !mapa.EhAgua(x + d, y + dy);
				if (!seco) continue;
				_irProximaFileira = y + 4;
				return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
			}
		AfirmarIr($"achei um corredor SECO de {tiles} tiles no mapa da bancada", false, "varredura falhou");
		return new Vec2(64, 64);
	}

	/// <summary>O corpo dirigido e o alvo de pano dele (uma copia: sem kit), a `tiles` de distancia num corredor seco. O dirigido entra em combate.</summary>
	private (ServerPlayer Npc, ServerPlayer Alvo) DuploIr(string nome, string raca, string classe, string linhagem, float tiles)
	{
		Vec2 chao = CorredorSecoIr((int)Math.Ceiling(tiles) + 4);
		ServerPlayer npc = ForjarIr(nome, raca, classe, chao, linhagem);
		ServerPlayer alvo = ForjarIr("alvo de " + nome, "Human", "", chao + new Vec2(tiles * ZoneCollision.TileSize, 0), doMundo: false,
																												  comCerebro: false);
		npc.Combate.EmCombate = 90;
		return (npc, alvo);
	}

	/// <summary>Um tique do relogio do servidor (os milissegundos fracionados se acumulam) e a grade de corpos refeita.</summary>
	private void AndarOTiqueIr()
	{
		_irRestoDoRelogio += Protocol.TickSeconds * 1000;
		long ms = (long)_irRestoDoRelogio;
		_irRestoDoRelogio -= ms;
		AdiantoDoRelogioDeTeste += ms;
		MontarAsGrades();
	}

	/// <summary>
	/// UM TIQUE DE UM CORPO DIRIGIDO contra um alvo dado: as linhas do `TicarUmCorpo` depois da presa escolhida (as capacidades no relogio
	/// de 1 Hz, a percepcao, a decisao, o atuador), a carga e o tique racial. O alvo de pano nao luta: e erguido e curado a cada tique, a
	/// menos que a cena o queira no chao.
	/// </summary>
	private void TiqueIr(ServerPlayer corpo, ServerPlayer? alvo, Random sorte, bool alvoDePe)
	{
		const double dt = Protocol.TickSeconds;
		AndarOTiqueIr();
		if (alvo != null && alvoDePe)
		{
			if (alvo.Ficha.KO) alvo.Combate.Levantar();
			VidaDosMembrosPs(alvo, 1);
		}
		Cerebro cerebro = corpo.Cerebro!;
		if (cerebro.PrecisaLerCapacidades(dt)) cerebro.Poderes = corpo.Perfil.Filtrar(LerCapacidades(corpo));
		Percepcao p = LerPercepcao(corpo, alvo, alvo?.Pos ?? corpo.Pos + new Vec2(200, 0), cerebro.Poderes.PedeLinha);
		AplicarComando(corpo, cerebro.Pensar(p, dt, sorte), dt);
		TickDaCarga(corpo, dt);
		TickDasRaciais(corpo, dt);
	}

	/// <summary>Tiques ate a condicao valer, ou ate o prazo: os segundos que levou, ou -1.</summary>
	private double TiquesAteIr(ServerPlayer corpo, ServerPlayer? alvo, double prazo, Func<bool> ate, bool alvoDePe = true,
																									  Action? aCadaTique = null)
	{
		var sorte = new Random(2026);
		double t = 0;
		while (t < prazo)
		{
			TiqueIr(corpo, alvo, sorte, alvoDePe);
			t += Protocol.TickSeconds;
			aCadaTique?.Invoke();
			if (ate()) return t;
		}
		return -1;
	}

	/// <summary>A escuta de producao dos verbs que a IA apertou (`EscutaDeTecnicasDaIa`), ligada pela bancada inteira.</summary>
	private List<(int Quem, string Tecnica)> EscutaIr => EscutaDeTecnicasDaIa ?? new();

	/// <summary>Os verbs que a IA deste corpo apertou, na ordem.</summary>
	private string ApertosIr(ServerPlayer corpo) => string.Join(",", EscutaIr.Where(x => x.Quem == corpo.Id).Select(x => x.Tecnica));

	private int ApertosDeIr(ServerPlayer corpo, string verb) => EscutaIr.Count(x => x.Quem == corpo.Id && x.Tecnica == verb);

	private double RecargaIr(ServerPlayer pl, string id) => EstadoRacial.FaltaDaRecarga(pl.Ficha.RecargasRaciais, id, NowMs());

	private static double TroncoIr(ServerPlayer pl) => pl.Combate.Corpo.Achar(Body.NomeDoTronco) is { } t ? t.Fracao : 0;

	/// <summary>As linhas RACIAIS de um repertorio de luta (as da tabela da IA), na ordem do repertorio.</summary>
	private static List<string> RaciaisNaMaoIr(Arsenal luta)
	{
		var ids = new List<string>();
		for (int i = 0; i < luta.Quantas; i++)
			if (EhLinhaRacialIr(luta[i].Id)) ids.Add(luta[i].Id);
		return ids;
	}

	private static bool EhLinhaRacialIr(string id)
	{
		foreach (AtivasRaciaisNaIa.Linha l in AtivasRaciaisNaIa.Todas)
			if (l.Id == id) return true;
		return false;
	}

	private static Tiro? AcharIr(Arsenal luta, string id)
	{
		for (int i = 0; i < luta.Quantas; i++)
			if (luta[i].Id == id) return luta[i];
		return null;
	}

	/// <summary>Poe o corpo no LSSJ Primal dominado, em combate ha mais de 2 min -- o ponto do Demonic (o mesmo do robo `--diagesfera`).</summary>
	private void NaFormaDoDemonicoIr(ServerPlayer pl)
	{
		pl.Forma.Maestria.Por("primal_legendary", 100);
		pl.Forma.Entrar("primal_legendary");
		AplicarForma(pl);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		pl.Combate.EmCombate = 600;
		pl.Forma.CombateSegundos = Passivas.LutaPraDemonico + 10;
		pl.PosDoTiqueRacial = pl.Pos;
		TickDasRaciais(pl, 0.05);
	}

	private void LimparABancadaDaIaRacial()
	{
		bool DaBancada(ServerPlayer pl) =>
			(pl.Id >= IdBaseDaIaRacialDeTeste && pl.Id < IdBaseDaIaRacialDeTeste + 1000) || _irNascidos.Contains(pl);
		List<ServerPlayer> daBancada = [.. _players.Values.Where(DaBancada)];
		List<Projetil> tiros = ProjeteisDaZona(ZonaDaBancadaDeProjetil.Hash);
		int antes = tiros.Count;
		tiros.RemoveAll(p => daBancada.Exists(c => c.Id == p.Dono));
		_projeteisVivos -= antes - tiros.Count;
		foreach (ServerPlayer pl in daBancada)
		{
			EsquecerTecnicas(pl.Id);
			SoltarDoRaio(pl.Id);
			_players.Remove(pl.Id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		_irNascidos.Clear();
	}

	// =====================================================================
	// A. O POVO E OS CHEFES TEM O KIT
	// =====================================================================
	private void OKitDoPovoEDosChefesIr()
	{
		GD.Print("[iaraciais] -- A. O POVO E OS CHEFES DE SAGA TEM O KIT DA RACA --");
		const float T = ZoneCollision.TileSize;
		Vec2 chao = CorredorSecoIr(14);
		ServerPlayer? freeza = NascerNpc("freeza_vegeta", ZonaDaBancadaDeProjetil, chao, 991);
		ServerPlayer? guardiao = NascerNpc("guardiao_saiyajin", ZonaDaBancadaDeProjetil, chao + new Vec2(6 * T, 0), 992);
		if (freeza != null) _irNascidos.Add(freeza);
		if (guardiao != null) _irNascidos.Add(guardiao);
		AfirmarIr("(palco) o Freeza (`freeza_vegeta`) e o Guardiao Saiyajin (`guardiao_saiyajin`) nascem pela fabrica de producao: corpos do mundo, chefes",
				  freeza is { Papel.EhChefe: true } && guardiao is { Papel.EhChefe: true } && EhNpcDoMundo(freeza) && EhNpcDoMundo(guardiao));
		if (freeza == null || guardiao == null) return;
		TickDasRaciais(freeza, 0.05);
		TickDasRaciais(guardiao, 0.05);

		KitRacial doGuardiao = Passivas.KitDe(guardiao.Race, guardiao.Ficha.Class, guardiao.Ficha.SaiyanLineage);
		AfirmarIr("O CHEFE DE SAGA TEM O KIT DA RACA DELE (o dono: *\"os boss de saga tb vao ter todas essas passivas e ativas a depender de sua raça\"*): o Freeza (Icer, Frost Demon) tem o do Frost Demon; o Guardiao, o do sangue Saiyajin dele",
				  freeza.Racial.Entrada.Kit == KitRacial.FrostDemon && doGuardiao != KitRacial.Nenhum && guardiao.Racial.Entrada.Kit == doGuardiao,
				  $"{freeza.Race}/{freeza.Ficha.Class}: {freeza.Racial.Entrada.Kit}; {guardiao.Race}/{guardiao.Ficha.Class}/{guardiao.Ficha.SaiyanLineage}: {guardiao.Racial.Entrada.Kit}");
		EfeitoRacial ef = freeza.Racial.Efeito;
		AfirmarIr("...e as PASSIVAS valem pra ele pelo tique racial: o Perfect Control do Freeza (dano de ki 1,15x, carga de tecnica 2x) e a Extreme Tenacity",
				  Math.Abs(ef.DanoDeKi - Passivas.KiDoControle) < 1e-6 && Math.Abs(ef.CargaDeTecnica - Passivas.CargaDoControle) < 1e-6 && ef.Tenaz,
				  $"dano de ki {ef.DanoDeKi:0.###}, carga {ef.CargaDeTecnica:0.###}, tenaz {ef.Tenaz}");

		List<string> doFreeza = RaciaisNaMaoIr(LerCapacidades(freeza).DeLuta);
		AfirmarIr("...e a ATIVA do Freeza (a Unrestrained Wrath) esta no repertorio de luta da IA dele",
				  doFreeza.Count == 1 && doFreeza[0] == Passivas.Ira, string.Join(",", doFreeza));

		ServerPlayer copia = ForjarIr("copia", "Icer", "Frost Demon", chao + new Vec2(12 * T, 0), doMundo: false);
		AfirmarIr("a COPIA (sem papel de NPC e sem conta: o reflexo da mente, a Divisao do Corpo) continua SEM kit -- e sem ativa na mao",
				  copia.Racial.Entrada.Kit == KitRacial.Nenhum && !EhNpcDoMundo(copia) && !EhPessoa(copia)
				  && RaciaisNaMaoIr(LerCapacidades(copia).DeLuta).Count == 0, copia.Racial.Entrada.Kit.ToString());

		Passivas.SoPessoaTemKitDeTeste = true;
		try
		{
			TickDasRaciais(freeza, 0.05);
			TickDasRaciais(guardiao, 0.05);
			AfirmarIr("   DEFEITO INJETADO (`Passivas.SoPessoaTemKitDeTeste`, o portao da v0.4.0): o Freeza e o Guardiao ficam sem kit, sem o Perfect Control e sem a Ira na mao -- as provas de cima REPROVARIAM",
					  freeza.Racial.Entrada.Kit == KitRacial.Nenhum && guardiao.Racial.Entrada.Kit == KitRacial.Nenhum
					  && Math.Abs(freeza.Racial.Efeito.DanoDeKi - 1) < 1e-6 && RaciaisNaMaoIr(LerCapacidades(freeza).DeLuta).Count == 0,
					  $"{freeza.Racial.Entrada.Kit} / {guardiao.Racial.Entrada.Kit}");
		}
		finally { Passivas.SoPessoaTemKitDeTeste = false; }
		LimparABancadaDaIaRacial();
	}

	// =====================================================================
	// B. A MAO DA IA
	// =====================================================================
	private void AMaoDaIaIr()
	{
		GD.Print("[iaraciais] -- B. A MAO DA IA: as ativas de cada kit no repertorio de luta --");
		(string Nome, string Raca, string Classe, string Linhagem, string[] Ativas)[] casos =
		[
			("Humano", "Human", "", "", [Passivas.PoderMaximo, Passivas.SegundoFolego, Passivas.ForcaDeVontade]),
			("Namekuseijin", "Namekian", "Warrior clan", "", [Passivas.Disrupcao]),
			("Frost Demon", "Icer", "Frost Demon", "", [Passivas.Ira]),
			("Frost Demon mutante", "Icer", Passivas.ClasseMutante, "", [Passivas.Ira, Passivas.Sadismo]),
			("Saiyajin Lendario", "Saiyan", Passivas.ClasseLendaria, "Saiyan", [Passivas.Transbordar, Passivas.ForcaImparavel]),
			("Saiyajin Primal", "Saiyan", "Normal Primal Saiyan", "Primal Saiyan", [Passivas.Ferocidade]),
			("Lendario Primal", "Saiyan", Passivas.ClasseLendariaPrimal, "Primal Saiyan", [Passivas.Ferocidade, Passivas.TransbordarKi, Passivas.SedeDeSangue]),
			("Saiyajin comum", "Saiyan", "Normal", "Saiyan", []),
			("Meio-Saiyajin", "Halfbreed", "", "", []),
			("Androide", "Android", "", "", []),
		];
		ServerPlayer? humano = null;
		foreach ((string nome, string raca, string classe, string linhagem, string[] ativas) in casos)
		{
			ServerPlayer npc = ForjarIr(nome, raca, classe, CorredorSecoIr(3), linhagem, comCerebro: false);
			if (nome == "Humano") humano = npc;
			List<string> naMao = RaciaisNaMaoIr(LerCapacidades(npc).DeLuta);
			AfirmarIr($"{nome} do mundo: {(ativas.Length == 0 ? "nenhuma ativa na mao (o kit dele e so de passivas, ou nao ha kit)" : "na mao da IA, " + string.Join(", ", ativas))}",
					  naMao.Count == ativas.Length && ativas.All(naMao.Contains), $"kit {npc.Racial.Entrada.Kit}: [{string.Join(",", naMao)}]");
		}
		if (humano == null) return;

		// ---- A RECARGA TIRA DA MAO; O TOGGLE LIGADO VEM MARCADO ----
		EstadoRacial.Recarregar(humano.Ficha.RecargasRaciais, Passivas.SegundoFolego, NowMs(), Passivas.RecargaDoFolego);
		humano.Racial.PoderMaximo = true;
		Arsenal comRecarga = LerCapacidades(humano).DeLuta;
		Tiro? poder = AcharIr(comRecarga, Passivas.PoderMaximo);
		AfirmarIr("a ativa EM RECARGA sai da mao (o Second Wind recem-usado: o cerebro nao bate na porta que ja disse \"ainda em recarga\"), e o toggle LIGADO vem marcado (o Max Power de pe -- e ele que o cerebro desliga com o Ki baixo)",
				  !RaciaisNaMaoIr(comRecarga).Contains(Passivas.SegundoFolego) && poder is { Ligada: true, Uso: Uso.Reforco },
				  $"[{string.Join(",", RaciaisNaMaoIr(comRecarga))}], Max Power ligado {poder?.Ligada}");
		humano.Racial.PoderMaximo = false;
		humano.Ficha.RecargasRaciais.Remove(Passivas.SegundoFolego);

		Arsenal doHumano = LerCapacidades(humano).DeLuta;
		Tiro? folego = AcharIr(doHumano, Passivas.SegundoFolego), vontade = AcharIr(doHumano, Passivas.ForcaDeVontade);
		AfirmarIr("os SOCORROS do Humano: o Second Wind acode o TANQUE (`KiNoFim`), a Sheer Willpower a VIDA -- e ativa racial nenhuma custa Ki ou folego",
				  folego is { Uso: Uso.Socorro } f && (f.Exige & Exige.KiNoFim) != 0 && vontade is { Uso: Uso.Socorro } v
				  && (v.Exige & Exige.KiNoFim) == 0 && f.CustoDeKi == 0 && v.CustoDeFolego == 0);

		// ---- NENHUMA DAS TREZE FICA DE FORA ----
		string[] treze =
		[
			Passivas.Ferocidade, Passivas.Transbordar, Passivas.ForcaImparavel, Passivas.TransbordarKi, Passivas.SedeDeSangue, Passivas.Demonico,
			Passivas.SegundoFolego, Passivas.ForcaDeVontade, Passivas.PoderMaximo, Passivas.Disrupcao, Passivas.Ira, Passivas.MenteCruel,
			Passivas.Sadismo,
		];
		string[] deFora = [.. treze.Where(id => !EhLinhaRacialIr(id) && id != Passivas.Demonico && id != Passivas.MenteCruel)];
		int linhas = AtivasRaciaisNaIa.Todas.Length;
		bool todasDeKit = treze.All(id => Passivas.KitDaAtiva(id) != KitRacial.Nenhum);
		AfirmarIr("NENHUMA DAS TREZE ATIVAS FICA DE FORA DA IA: onze sao linhas do repertorio, e as duas que pedem o corpo parado (o Demonic e o Ruthless Mind) tem receita propria -- e cada uma e de um kit (`Passivas.KitDaAtiva`, a pergunta do proprio verb)",
				  deFora.Length == 0 && linhas == 11 && todasDeKit, $"de fora [{string.Join(",", deFora)}], {linhas} linhas");

		AtivasRaciaisNaIa.IaSemAtivasDeTeste = true;
		try
		{
			List<string> semNada = RaciaisNaMaoIr(LerCapacidades(humano).DeLuta);
			AfirmarIr("   DEFEITO INJETADO (`AtivasRaciaisNaIa.IaSemAtivasDeTeste`, a IA da v0.4.0): o Humano do mundo fica sem ativa nenhuma na mao -- a prova de cima REPROVARIA",
					  semNada.Count == 0, $"[{string.Join(",", semNada)}]");
		}
		finally { AtivasRaciaisNaIa.IaSemAtivasDeTeste = false; }

		// ---- A LEITURA DE 1 Hz CONTINUA SEM LIXO ----
		_ = LerCapacidades(humano);
		long b0 = GC.GetAllocatedBytesForCurrentThread();
		_ = LerCapacidades(humano);
		long lixo = GC.GetAllocatedBytesForCurrentThread() - b0;
		AfirmarIr("a leitura de capacidades de um Humano do mundo, com as tres ativas na mao, aloca ZERO bytes (o repertorio reescreve o array do proprio corpo -- a `--iateste` cobra o zero da leitura)",
				  lixo == 0, $"{lixo} B");
		LimparABancadaDaIaRacial();
	}

	// =====================================================================
	// C. AS TRES RECEITAS PROPRIAS -- a tabela de decisao
	// =====================================================================
	private void AsReceitasPropriasIr()
	{
		GD.Print("[iaraciais] -- C. AS TRES RECEITAS PROPRIAS, numa tabela de decisao (o cerebro sozinho, a cena escrita) --");
		const float T = ZoneCollision.TileSize;
		const double dt = Protocol.TickSeconds;
		Percepcao Ver(float tiles, bool temAlvo = true, bool caido = false, bool parado = false) => new()
		{
			DoAlvo = new Vec2(tiles * T, 0), TemAlvo = temAlvo, IdDoAlvo = temAlvo ? 77 : 0, AlvoCaido = caido,
			VidaFrac = 1, KiFrac = 1, Ki = 1e9, FolegoFrac = 1, Folego = 100, MeuPoder = 100, PoderDoAlvo = 100,
			VidaDoAlvo = caido ? 0 : 1, LinhaLivre = true, ParadoPelaRaca = parado,
		};
		List<(double Em, Comando C)> Ver30(Cerebro c, Percepcao p, double segundos)
		{
			var rng = new Random(17);
			var visto = new List<(double, Comando)>();
			for (double t = 0; t < segundos; t += dt) visto.Add((t, c.Pensar(p, dt, rng)));
			return visto;
		}
		static Cerebro Novo(Capacidades poderes) => new() { Poderes = poderes };
		static List<double> Quando(List<(double Em, Comando C)> visto, string verb) => [.. visto.Where(x => x.C.Habilidade == verb).Select(x => x.Em)];
		static string Lista(List<double> l) => string.Join(", ", l.Select(x => x.ToString("0.00")));

		// ---- O DEMONIC SAIYAN ----
		var pronto = new Capacidades { DemonicoPronto = true, TemComQueAparar = true };
		List<(double Em, Comando C)> longe = Ver30(Novo(pronto), Ver(8), 6);
		List<double> pediuLonge = Quando(longe, Passivas.Demonico);
		Comando primeiro = longe[0].C;
		AfirmarIr("O DEMONIC: pronto, e com o alvo a oito tiles (alem da distancia segura), o cerebro o pede no PRIMEIRO tique -- parado, de guarda, de frente pro alvo -- e nao de novo na pausa",
				  pediuLonge.Count == 1 && pediuLonge[0] == 0 && primeiro.Rumo.LengthSquared < 1e-6f && primeiro.Guardar && primeiro.Olhar.X > 0.9f,
				  $"pediu aos [{Lista(pediuLonge)}] s");
		List<double> pediuColado = Quando(Ver30(Novo(pronto), Ver(2), 4), Passivas.Demonico);
		AfirmarIr("...COLADO no alvo (dois tiles) ele NAO carrega: 15 s parado ao alcance de um soco, e o primeiro empurrao desfaria a esfera",
				  pediuColado.Count == 0, $"{pediuColado.Count} pedido(s)");
		List<double> semPronto = Quando(Ver30(Novo(new Capacidades { TemComQueAparar = true }), Ver(8), 4), Passivas.Demonico);
		AfirmarIr("...e sem o Demonic PRONTO (o kit, a forma, os 2 minutos, a recarga -- a pergunta do verb) ele nao bate na porta", semPronto.Count == 0);

		// ---- PARADO PELA RACA ----
		List<(double Em, Comando C)> preso = Ver30(Novo(new Capacidades { TemComQueAparar = true }), Ver(1, parado: true), 3);
		AfirmarIr("PARADO PELA RACA (a esfera carregando, a execucao correndo): tres segundos sem um passo, sem soco e sem tecnica -- so a guarda, de frente pro alvo",
				  preso.TrueForAll(x => x.C.Rumo.LengthSquared < 1e-6f && !x.C.Leve && !x.C.Pesado && x.C.Habilidade == null && x.C.Guardar && x.C.Olhar.X > 0.9f));

		// ---- A EXECUCAO DO RUTHLESS MIND ----
		var executa = new Capacidades { ExecucaoRacialPronta = true, TemComQueAparar = true };
		(double Em, Comando C) pedido = Ver30(Novo(executa), Ver(0.7f, caido: true), 1.5).Find(x => x.C.Habilidade == Passivas.MenteCruel);
		AfirmarIr("A EXECUCAO: o alvo CAIDO ao alcance da mao e a execucao na mao -- o cerebro pede o Ruthless Mind de frente pro caido (o verb executa quem esta na frente)",
				  pedido.C.Habilidade == Passivas.MenteCruel && pedido.C.Olhar.X > 0.9f, $"aos {pedido.Em:0.00} s");
		List<(double Em, Comando C)> longeDoCaido = Ver30(Novo(executa), Ver(4, caido: true), 1.5);
		AfirmarIr("...a quatro tiles do caido ele ANDA ate la, sem pedir a execucao de fora da mao",
				  !longeDoCaido.Exists(x => x.C.Habilidade == Passivas.MenteCruel) && longeDoCaido.Exists(x => x.C.Rumo.X > 0.9f));
		AfirmarIr("...e sem a execucao na mao (sem o golpe letal: corpo sem dono derruba, nao mata) o caido fica onde esta",
				  !Ver30(Novo(new Capacidades { TemComQueAparar = true }), Ver(0.7f, caido: true), 2).Exists(x => x.C.Habilidade == Passivas.MenteCruel));

		// ---- O DESABAFO ----
		static Capacidades Transbordando(double ha) => new() { Desabafo = Passivas.Transbordar, DesabafoHa = ha, TemComQueAparar = true };
		List<double> longeCedo = Quando(Ver30(Novo(Transbordando(0)), Ver(12), 3), Passivas.Transbordar);
		AfirmarIr("O DESABAFO: com o prazo recem-vencido e o alvo LONGE (doze tiles), o transbordar ainda espera -- o bonus sobe, e a rajada nao acertaria ninguem",
				  longeCedo.Count == 0, $"{longeCedo.Count} pedido(s)");
		// O GEMEO: o mesmo cerebro, a mesma sorte e a mesma cena, sem transbordar nenhum -- o que ele faz e o gesto do plano.
		List<(double Em, Comando C)> gemeo = Ver30(Novo(new Capacidades { TemComQueAparar = true }), Ver(12), 0.5);
		List<(double Em, Comando C)> comFolga = Ver30(Novo(Transbordando(Cerebro.FolgaDoDesabafo)), Ver(12), 0.5);
		int noTique = comFolga.FindIndex(x => x.C.Habilidade == Passivas.Transbordar);
		bool oMesmoGesto = noTique >= 0 && (comFolga[noTique].C with { Habilidade = null }).Equals(gemeo[noTique].C);
		AfirmarIr("...vencida a FOLGA (dez segundos alem do prazo: o tronco ja comeca a pagar caro) ele desabafa assim mesmo, logo -- e o desabafo e um pulso POR CIMA do gesto do plano: tirado o verb, o tique e o de um gemeo sem transbordar",
				  noTique >= 0 && comFolga[noTique].Em < 0.1 && oMesmoGesto,
				  noTique >= 0 ? $"aos {comFolga[noTique].Em:0.00} s; o mesmo gesto do gemeo: {oMesmoGesto}" : "nao desabafou");
		List<double> perto = Quando(Ver30(Novo(Transbordando(0)), Ver(2), 4), Passivas.Transbordar);
		AfirmarIr("...com o alvo PERTO (dois tiles) ele desabafa logo -- a rajada e um golpe -- e espaca os pedidos pela pausa (o desabafado ainda aparece pronto ate a leitura seguinte, e outro aperto o religaria)",
				  perto.Count == 2 && perto[0] < 0.1 && perto[1] - perto[0] >= Cerebro.PausaDoDesabafo - 0.05, $"aos [{Lista(perto)}] s");
		List<double> acabou = Quando(Ver30(Novo(Transbordando(0)), Ver(12, temAlvo: false), 8), Passivas.Transbordar);
		AfirmarIr("...e sem ninguem pra lutar (a luta acabou) ele desabafa passados os seis segundos sem alvo -- o transbordar nao fica ligado esperando a proxima briga",
				  acabou.Count >= 1 && acabou[0] >= Cerebro.SegundosSemAlvoAteDesligar - 0.05 && acabou[0] < Cerebro.SegundosSemAlvoAteDesligar + 0.5,
				  $"aos [{Lista(acabou)}] s");
		Cerebro.SemDesabafoDeTeste = true;
		try
		{
			List<double> nunca = Quando(Ver30(Novo(Transbordando(30)), Ver(2), 4), Passivas.Transbordar);
			AfirmarIr("   DEFEITO INJETADO (`Cerebro.SemDesabafoDeTeste`): alvo colado e trinta segundos alem do prazo, e o cerebro NAO desabafa -- as provas de cima REPROVARIAM",
					  nunca.Count == 0, $"{nunca.Count} pedido(s)");
		}
		finally { Cerebro.SemDesabafoDeTeste = false; }
	}

	// =====================================================================
	// D. NA LUTA, PELO TIQUE DE PRODUCAO
	// =====================================================================
	private void NaLutaIr()
	{
		GD.Print("[iaraciais] -- D. NA LUTA, PELO TIQUE DE PRODUCAO (o corpo do mundo contra um alvo de pano) --");

		// ---- D1. OS REFORCOS DE CADA KIT LIGAM QUANDO A LUTA COMECA ----
		(string Nome, string Raca, string Classe, string Linhagem, string Oque, Func<ServerPlayer, bool> Ligou)[] reforcos =
		[
			("Humano", "Human", "", "", "o Max Power", n => n.Racial.PoderMaximo),
			("Namekuseijin", "Namekian", "Warrior clan", "", "a Spirit Disruption", n => RecargaIr(n, Passivas.Disrupcao) > 0),
			("Frost Demon", "Icer", "Frost Demon", "", "a Unrestrained Wrath", n => n.Racial.IraDesde >= 0),
			("Frost Demon mutante", "Icer", Passivas.ClasseMutante, "", "a Ira e a Sadistic Nature",
				 n => n.Racial.IraDesde >= 0 && RecargaIr(n, Passivas.Sadismo) > 0),
			("Saiyajin Primal", "Saiyan", "Normal Primal Saiyan", "Primal Saiyan", "a Unmatched Ferocity", n => RecargaIr(n, Passivas.Ferocidade) > 0),
			("Saiyajin Lendario", "Saiyan", Passivas.ClasseLendaria, "Saiyan", "o Overflowing Power e a Unstoppable Force",
				 n => n.Racial.TransbordarDesde >= 0 && RecargaIr(n, Passivas.ForcaImparavel) > 0),
			("Lendario Primal", "Saiyan", Passivas.ClasseLendariaPrimal, "Primal Saiyan", "o Overflowing Ki, a Primal Bloodlust e a Unmatched Ferocity",
				 n => n.Racial.KiTransbordandoDesde >= 0 && RecargaIr(n, Passivas.SedeDeSangue) > 0 && RecargaIr(n, Passivas.Ferocidade) > 0),
		];
		foreach ((string nome, string raca, string classe, string linhagem, string oque, Func<ServerPlayer, bool> ligou) in reforcos)
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr(nome, raca, classe, linhagem, 1.2f);
			double em = TiquesAteIr(npc, alvo, 8, () => ligou(npc));
			AfirmarIr($"{nome} do mundo, na luta: a IA LIGA {oque} (o verb aceitou: o estado racial mudou)", em >= 0,
					  em >= 0 ? $"aos {em:0.0} s; apertou [{ApertosIr(npc)}]" : $"nada em 8 s; apertou [{ApertosIr(npc)}]");
			LimparABancadaDaIaRacial();
		}
		AtivasRaciaisNaIa.IaSemAtivasDeTeste = true;
		try
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Humano sem a tabela", "Human", "", "", 1.2f);
			double em = TiquesAteIr(npc, alvo, 6, () => npc.Racial.PoderMaximo);
			AfirmarIr("   DEFEITO INJETADO (`IaSemAtivasDeTeste`): o mesmo Humano luta seis segundos e NAO liga o Max Power, nem aperta ativa racial nenhuma -- a prova de cima REPROVARIA",
					  em < 0 && !ApertosIr(npc).Contains("rc_"), $"apertou [{ApertosIr(npc)}]");
		}
		finally { AtivasRaciaisNaIa.IaSemAtivasDeTeste = false; LimparABancadaDaIaRacial(); }

		// ---- D2. OS SOCORROS ----
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Humano sem Ki", "Human", "", "", 1.2f);
			npc.Ficha.Ki = npc.Ficha.MaxKi * 0.2;
			double em = TiquesAteIr(npc, alvo, 4, () => npc.Racial.FolegoAte > NowMs());
			double kiNoAperto = npc.Ficha.Ki;
			if (em >= 0) TiquesAteIr(npc, alvo, 1, () => false);
			AfirmarIr("SOCORRO DO TANQUE: o Humano do mundo com o Ki no fim (20%) aperta o Second Wind -- e o Ki volta a subir",
					  em >= 0 && npc.Ficha.Ki > kiNoAperto, $"aos {em:0.0} s; Ki {kiNoAperto / npc.Ficha.MaxKi:P0} -> {npc.Ficha.Ki / npc.Ficha.MaxKi:P0}");
			LimparABancadaDaIaRacial();
		}
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Humano ferido", "Human", "", "", 1.2f);
			VidaDosMembrosPs(npc, 0.5);
			double em = TiquesAteIr(npc, alvo, 4, () => npc.Racial.VontadeAte > NowMs());
			AfirmarIr("SOCORRO DA VIDA: o Humano do mundo com a vida na metade aperta a Sheer Willpower -- e a morte fica negada pelo tique racial (15 s em que nada o derruba)",
					  em >= 0 && npc.Racial.Efeito.NegaMorte, $"aos {em:0.0} s, vida {npc.Ficha.HP:0}");
			LimparABancadaDaIaRacial();
		}

		// ---- D3. O MAX POWER CAI COM O KI BAIXO ----
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Humano no Max Power", "Human", "", "", 1.2f);
			EstadoRacial.Recarregar(npc.Ficha.RecargasRaciais, Passivas.SegundoFolego, NowMs(), 600);   // (o socorro do tanque fica fora desta cena)
			double ligou = TiquesAteIr(npc, alvo, 6, () => npc.Racial.PoderMaximo);
			// A LUTA SEGUE COM ELE LIGADO: o reforco recem-apertado descansa uns segundos antes de poder ser apertado de novo
			// (`Cerebro.DescansoDoReforco`) -- o Ki que cai no segundo seguinte ao ligar e uma cena que o jogo nao tem.
			if (ligou >= 0) TiquesAteIr(npc, alvo, 4, () => false);
			npc.Ficha.Ki = npc.Ficha.MaxKi * 0.25;
			double desligou = ligou >= 0 && npc.Racial.PoderMaximo ? TiquesAteIr(npc, alvo, 3, () => !npc.Racial.PoderMaximo) : -1;
			AfirmarIr("O MAX POWER CAI COM O KI BAIXO: ligado na luta, o Ki a 25% e a IA o DESLIGA -- ele aluga o tanque, e e com o tanque que ela vai se afastar e carregar -- antes de o dreno zerar o Ki",
					  ligou >= 0 && desligou >= 0 && npc.Ficha.Ki > 0 && ApertosDeIr(npc, Passivas.PoderMaximo) >= 2,
					  $"ligou aos {ligou:0.0} s, desligou {desligou:0.0} s depois do Ki baixo, Ki {npc.Ficha.Ki / npc.Ficha.MaxKi:P0}, apertou [{ApertosIr(npc)}]");
			LimparABancadaDaIaRacial();
		}

		// ---- D4. O OVERFLOWING POWER DESABAFA ANTES DE O TRONCO COBRAR ----
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Lendario do desabafo", "Saiyan", Passivas.ClasseLendaria, "Saiyan", 1.2f);
			double ligou = TiquesAteIr(npc, alvo, 6, () => npc.Racial.TransbordarDesde >= 0);
			long ligadoEm = npc.Racial.TransbordarDesde;
			double desabafou = ligou >= 0 ? TiquesAteIr(npc, alvo, 50, () => npc.Racial.TransbordarDesde < 0 || npc.Ficha.KO) : -1;
			double ligadoPor = (NowMs() - ligadoEm) / 1000.0;
			// (2026-10-17) O DESABAFO E O GRITO: 3 s plantado na esfera verde, os tiros saindo espalhados -- a IA o atravessa parada (`ParadoPelaRaca`).
			Vec2 ondeGritou = npc.Pos;
			double calou = desabafou >= 0 ? TiquesAteIr(npc, alvo, 4, () => npc.Racial.GritoDesde < 0 || npc.Ficha.KO) : -1;
			int tiros = npc.Racial.GritoTiros;
			bool plantado = Vec2.Distance(npc.Pos, ondeGritou) < 1;
			double tronco = TroncoIr(npc);
			AfirmarIr("O OVERFLOWING POWER DESABAFA ANTES DE O TRONCO COBRAR: ligado na luta, a IA desabafa passado o prazo de 30 s (com o alvo colado, logo) -- o GRITO inteiro sai (os tiros todos, com o corpo parado), e o corpo segue de pe com o tronco quase inteiro",
					  ligou >= 0 && desabafou >= 0 && calou >= 0 && !npc.Ficha.KO && ligadoPor >= Passivas.SegundosPraDesabafar
					  && ligadoPor <= Passivas.SegundosPraDesabafar + Cerebro.FolgaDoDesabafo + 2 && tiros == Passivas.TirosDoGrito && plantado
					  && tronco > 0.9,
					  $"ligado por {ligadoPor:0.0} s, grito de {calou:0.0} s, {tiros} tiro(s), plantado {plantado}, tronco {tronco:P0}, KO {npc.Ficha.KO}");
			double religou = TiquesAteIr(npc, alvo, 10, () => npc.Racial.TransbordarDesde >= 0);
			AfirmarIr("...e nao religa no segundo seguinte: o transbordar desabafado descansa (dez segundos depois, ainda apagado)",
					  religou < 0, religou >= 0 ? $"religou {religou:0.0} s depois" : "");
			LimparABancadaDaIaRacial();
		}
		Cerebro.SemDesabafoDeTeste = true;
		try
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Lendario sem desabafo", "Saiyan", Passivas.ClasseLendaria, "Saiyan", 1.2f);
			double ligou = TiquesAteIr(npc, alvo, 6, () => npc.Racial.TransbordarDesde >= 0);
			double caiu = ligou >= 0 ? TiquesAteIr(npc, alvo, 100, () => npc.Ficha.KO) : -1;
			AfirmarIr("   DEFEITO INJETADO (`Cerebro.SemDesabafoDeTeste`): sem o desabafo o transbordar corre ate o TRONCO derrubar o corpo -- o \"quem nao desabafa cai\" -- e a prova de cima REPROVARIA",
					  caiu >= 0, caiu >= 0 ? $"caiu {caiu:0} s depois de ligar" : $"de pe depois de 100 s, tronco {TroncoIr(npc):P0}");
		}
		finally { Cerebro.SemDesabafoDeTeste = false; LimparABancadaDaIaRacial(); }

		// ---- D5. O DEMONIC SAIYAN CARREGA PARADO ----
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Lendario Primal do Demonic", "Saiyan", Passivas.ClasseLendariaPrimal, "Primal Saiyan", 8f);
			NaFormaDoDemonicoIr(npc);
			double carregou = TiquesAteIr(npc, alvo, 3, () => npc.Racial.DemonicoCargaDesde >= 0);
			Vec2 ondeCarregou = npc.Pos;
			float andou = 0;
			double acendeu = carregou >= 0
				? TiquesAteIr(npc, alvo, 17, () => npc.Racial.DemonicoDesde >= 0, aCadaTique: () => andou = Math.Max(andou, Vec2.Distance(npc.Pos, ondeCarregou)))
				: -1;
			AfirmarIr("O DEMONIC SAIYAN NA MAO DA IA: com o alvo a oito tiles, a IA aperta o Demonic, fica PARADA os 15 s da esfera (nem um passo) e ele acende",
					  carregou >= 0 && acendeu >= Passivas.SegundosDaCargaDemonica - 1.5 && andou < 1,
					  $"carregou aos {carregou:0.0} s, acendeu {acendeu:0.0} s depois, andou {andou:0.0} px; apertou [{ApertosIr(npc)}]");
			LimparABancadaDaIaRacial();
		}
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Lendario Primal colado", "Saiyan", Passivas.ClasseLendariaPrimal, "Primal Saiyan", 1.2f);
			NaFormaDoDemonicoIr(npc);
			double carregou = TiquesAteIr(npc, alvo, 3, () => npc.Racial.DemonicoCargaDesde >= 0);
			AfirmarIr("...e COLADO no alvo ela NAO carrega (o primeiro soco desfaria a esfera): tres segundos de luta sem o Demonic",
					  carregou < 0, $"apertou [{ApertosIr(npc)}]");
			LimparABancadaDaIaRacial();
		}
		AtivasRaciaisNaIa.IaSemAtivasDeTeste = true;
		try
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr("Lendario Primal sem a tabela", "Saiyan", Passivas.ClasseLendariaPrimal, "Primal Saiyan", 8f);
			NaFormaDoDemonicoIr(npc);
			double carregou = TiquesAteIr(npc, alvo, 3, () => npc.Racial.DemonicoCargaDesde >= 0);
			AfirmarIr("   DEFEITO INJETADO (`IaSemAtivasDeTeste`): o mesmo corpo, longe do alvo, nao carrega o Demonic -- a prova de cima REPROVARIA",
					  carregou < 0, $"apertou [{ApertosIr(npc)}]");
		}
		finally { AtivasRaciaisNaIa.IaSemAtivasDeTeste = false; LimparABancadaDaIaRacial(); }

		// ---- D6. A EXECUCAO DO RUTHLESS MIND, SO COM O GOLPE LETAL ----
		foreach (bool letal in new[] { true, false })
		{
			(ServerPlayer npc, ServerPlayer alvo) = DuploIr(letal ? "Mutante letal" : "Mutante sem letal", "Icer", Passivas.ClasseMutante, "", 1.0f);
			npc.Combate.Letal = letal;
			alvo.Combate.Nocautear(600);   // no chao, e assim fica
			Vec2 onde = npc.Pos;
			double morreu = TiquesAteIr(npc, alvo, 8, () => alvo.Ficha.dead, alvoDePe: false);
			if (letal)
				AfirmarIr("O RUTHLESS MIND NA MAO DA IA: com o golpe letal ligado e o alvo caido na frente, a IA o executa -- 3 s parada junto dele -- e ele morre",
						  morreu >= Passivas.SegundosDaExecucao - 0.2 && RecargaIr(npc, Passivas.MenteCruel) > 0,
						  morreu >= 0 ? $"morreu aos {morreu:0.0} s; apertou [{ApertosIr(npc)}]" : $"vivo depois de 8 s; apertou [{ApertosIr(npc)}]");
			else
				AfirmarIr("...e SEM o golpe letal (o padrao do corpo sem dono: derrubar, e nao matar) ninguem e executado -- o caido fica vivo",
						  morreu < 0 && ApertosDeIr(npc, Passivas.MenteCruel) == 0, $"apertou [{ApertosIr(npc)}]");
			LimparABancadaDaIaRacial();
		}
	}

	// =====================================================================
	// E. O CHEFE DE SAGA NA LUTA
	// =====================================================================
	private void OChefeDeSagaNaLutaIr()
	{
		GD.Print("[iaraciais] -- E. O CHEFE DE SAGA NA LUTA --");
		Vec2 chao = CorredorSecoIr(8);
		ServerPlayer? freeza = NascerNpc("freeza_vegeta", ZonaDaBancadaDeProjetil, chao, 993);
		if (freeza == null) { AfirmarIr("(palco) o Freeza nasce pela fabrica", false); return; }
		_irNascidos.Add(freeza);
		freeza.Perfil = PerfilDeCombate.SoCorpo;   // (a cena e a da raca: sem o raio dele, que seguraria as maos)
		freeza.Combate.EmCombate = 90;
		ServerPlayer alvo = ForjarIr("alvo do Freeza", "Human", "", chao + new Vec2(1.2f * ZoneCollision.TileSize, 0), doMundo: false, comCerebro: false);
		double em = TiquesAteIr(freeza, alvo, 8, () => freeza.Racial.IraDesde >= 0);
		AfirmarIr("O FREEZA DA FABRICA (`freeza_vegeta`), numa luta, LIGA a Unrestrained Wrath dele pela IA -- e o tique racial da o BP +20% dela",
				  em >= 0 && Math.Abs(freeza.Racial.Efeito.Bp - Passivas.BpDaIra) < 1e-6,
				  em >= 0 ? $"aos {em:0.0} s, BP racial {freeza.Racial.Efeito.Bp:0.###}" : $"nada em 8 s; apertou [{ApertosIr(freeza)}]");
		LimparABancadaDaIaRacial();
	}
}
