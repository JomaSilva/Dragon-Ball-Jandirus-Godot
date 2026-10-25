using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO DESPERTAR DO KI DIVINO (`--godkiteste`) -- o pedido do dono de 2026-10-10:
///
///   *"o god ki alguns planetas raros vao possuir uma atmosfera divina ao qual quando o jogador tiver um bp
///   base maior q o requisito minimo dele de despertar o god ki, ao ficar um tempo nessa atmosfera o ki
///   divino e liberado, outra forma seria lutando com pessoas q ja tem ki divino ou estao transformadas em
///   formas divinas, mas claro so desperta se tiver acima do bp base minimo do god ki pessoal dele."*
///
/// COM O HOST DE VERDADE, no primeiro tique do ki divino em que ha um jogador no mundo (o disparo mora no
/// `TickDoGodKi`, como o da `--verbsteste` mora no tique dos fatos). Nada aqui recalcula a regra: o corpo
/// troca de mundo pelo `MoveToZone`, o tempo passa pelo `TickDoGodKi` de producao (com o `dt` que ele ja
/// recebe), o golpe e o `Atacar` do `C2S.Action`, o save vai ao disco e volta, e os verbs de admin passam
/// pelo despacho.
///
/// O UNIVERSO E SORTEADO A CADA RODADA (pasta de saves nova = semente nova): os mundos NAO sao cravados --
/// a bancada varre a carta em volta da origem e escolhe o divino mais leve e um comum leve.
///
/// ============================ AS OITO FAMILIAS ============================
///   1. O REQUISITO -- pessoal, em torno de 1 bilhao (o dono, 2026-10-21; o DM pede 250 milhoes), e sobre o BP BASE.
///   2. O UNIVERSO -- quais mundos sao divinos: a raridade, a garantia (com o sorteio desligado), o que
///      nunca e (o espaco, a Terra) e o que e por natureza (o Reino dos Deuses).
///   3. A ATMOSFERA -- abaixo do requisito nada acumula e a frase e uma; acima, o tempo enche o medidor;
///      planeta comum nao; nao zera ao sair; o cadaver nao respira.
///   4. O DESPERTAR -- o que ele escreve (marco, excecoes) e o que NAO escreve mais (os 2,5x de poder pra sempre: o
///      dono os tirou em 2026-10-23), quem ouve, e o que o resto do jogo passa a enxergar: o perfil de formas, o
///      Super Saiyajin God e o fato do "Challenge God of Destruction".
///   5. A LUTA -- o golpe dado e o recebido, o revide, o teto por minuto, o caido, o golpe que nao
///      encosta, o reflexo de um deus, o NPC numa forma divina, e os dois caminhos somando.
///   6. A MAESTRIA DIVINA -- so sobe usando o ki divino, dezesseis horas de uso, o dobro em combate.
///   7. O SAVE -- a exposicao parcial e o despertar atravessam o disco e o login; e o save de antes de 2026-10-23,
///      com o bonus gravado, entra sem ele.
///   8. OS VERBS DE ADMIN -- `admin_godki` e `admin_mundo_divino`, com o pouso de verdade.
/// ==========================================================================
///
/// OITO DEFEITOS INJETADOS, um por regra: `GodKiSemRequisitoDeTeste`, `GodKiEmQualquerChaoDeTeste`,
/// `GodKiSemRevideDeTeste`, `GodKiSemTetoDeLutaDeTeste`, `GodKiNaoDespertaDeTeste`,
/// `MaestriaDivinaParadaDeTeste` e, no Core, `AtmosferaDivina.GarantiaDesligadaDeTeste` e
/// `Fighter.BonusDoGodKiPraSempreDeTeste` (os 2,5x pra sempre de antes de 2026-10-23).
///
///     Godot --headless --path . --host --rede 8250 --godkiteste --raca Saiyan
///                      --conta bancada_godki --nome Deus
/// </summary>
public partial class GameServer
{
	/// <summary>A flag da bancada, lida uma vez.</summary>
	private static readonly bool GodKiDeTeste = Array.IndexOf(OS.GetCmdlineArgs(), "--godkiteste") >= 0;

	private bool _godKiTestou;

	/// <summary>Faixa de ids propria -- longe do `_nextId` e das faixas das outras bancadas.</summary>
	private const int IdBaseDoGodKiDeTeste = 97_300;

	/// <summary>A conta do "relog" da familia 7. Gravada de verdade (na pasta desviada da bancada) e apagada no fim.</summary>
	private const string ContaDaBancadaDoGodKi = "bancada_godki_relog";

	private int _gkOk, _gkFalhou, _gkCorpos;

	/// <summary>Chamado do <see cref="TickDoGodKi"/>: roda UMA vez, no primeiro tique em que ha um jogador de verdade.</summary>
	private void DispararABancadaDoGodKi()
	{
		if (_godKiTestou || Jogadores.FirstOrDefault() is not { } host) return;
		_godKiTestou = true;
		RodarBancadaDoGodKi(host);
	}

	private void AfirmarGk(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _gkOk++; GD.Print($"[godki]   OK    {oque}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); return; }
		_gkFalhou++;
		GD.PrintErr($"[godki]   FALHA {oque}   [{detalhe}]");
	}

	private void RodarBancadaDoGodKi(ServerPlayer pl)
	{
		_gkOk = _gkFalhou = _gkCorpos = 0;
		GD.Print("[godki] ================ O DESPERTAR DO KI DIVINO: A ATMOSFERA E A LUTA (dono, 2026-10-10) ================");

		// A FOTO DO HOST. Ele vai trocar de mundo, de poder e de ki aqui dentro; tudo volta no `finally`.
		Fighter f = pl.Ficha;
		ZoneKey zonaGuardada = pl.Zone;
		Vec2 posGuardada = pl.Pos;
		ChunkId chunkGuardada = pl.ChunkAtual;
		string origemGuardada = pl.PlanetaDeOrigem;
		double bpGuardado = f.BP, kiGuardado = f.Ki, vigorGuardado = f.stamina, gravidadeGuardada = f.GravMastered;
		GodKiState? godkiGuardado = f.godki;
		double exposicaoGuardada = f.ExposicaoDivina;
		double marcoGuardado = f.bp_milestone_mult;
		var marcosGuardados = new HashSet<string>(f.bp_milestones_done, StringComparer.Ordinal);
		bool mortoGuardado = f.dead, koGuardado = f.KO;
		string formaGuardada = pl.Forma.Atual;
		var liberadasGuardadas = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasGuardadas = new HashSet<int>(pl.Forma.EstreiaVista);
		bool knockbackGuardado = pl.Knockback;
		int alvoGuardado = pl.AlvoId;
		Facing rostoGuardado = pl.Facing;
		double relogioGuardado = _relogioDoGodKi;

		List<(int Quem, byte Bits, byte Pct, ushort Maestria)>? escutaAntes = EscutaDoGodKi;
		List<(int Quem, int Telas)>? despertarAntes = EscutaDoDespertar;
		EscutaDoGodKi = [];
		EscutaDoDespertar = [];
		var forjados = new List<ServerPlayer>();

		try
		{
			AfirmarGk("PRECONDICAO: o host tem dono na tela e e admin (os dois verbs da familia 8 sao de admin)",
					  EhJogador(pl) && EhAdmin(pl), $"jogador={EhJogador(pl)} admin={EhAdmin(pl)}");
			if (f.dead || f.KO) pl.Combate.Reviver();
			pl.Knockback = false;   // os golpes da familia 5 nao podem arremessar: o arremessado nao revida

			GkORequisito(pl);
			(MundoDivino? divino, PlanetaNoEspaco? comum) = GkOUniverso();
			if (divino is not { } d || comum is not { } c)
			{
				AfirmarGk("(montagem) a carta desta rodada tem um mundo divino e um comum pra pousar", false,
						  $"divino={divino?.Corpo.Nome ?? "nenhum"} comum={comum?.Nome ?? "nenhum"}");
				return;
			}

			ZoneKey? zd = ZonaGeradaPronta(d.Corpo), zc = ZonaGeradaPronta(c);
			AfirmarGk($"(montagem) os dois mundos nasceram no servidor: {d.Corpo.Nome} (divino, "
					  + $"{MundoProcedural.GravidadeDaSeed(d.Corpo.Seed):0}x) e {c.Nome} (comum, {MundoProcedural.GravidadeDaSeed(c.Seed):0}x)",
					  zd != null && zc != null);
			if (zd is not { } zonaDivina || zc is not { } zonaComum) return;

			GkAAtmosferaEODespertar(pl, zonaDivina, zonaComum, forjados);
			GkALuta(pl, zonaDivina, zonaComum, forjados);
			GkAMaestria(pl, zonaComum);
			GkOSave(pl, zonaDivina, forjados);
			GkOsVerbsDeAdmin(pl);

			AfirmarGk("a bancada chegou ao fim (sem esta linha, abortar no meio reportaria '0 falhas')", true);
		}
		catch (Exception e)
		{
			AfirmarGk($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			GodKiSemRequisitoDeTeste = GodKiEmQualquerChaoDeTeste = GodKiSemRevideDeTeste = false;
			GodKiSemTetoDeLutaDeTeste = GodKiNaoDespertaDeTeste = MaestriaDivinaParadaDeTeste = false;
			AtmosferaDivina.GarantiaDesligadaDeTeste = false;
			Fighter.BonusDoGodKiPraSempreDeTeste = false;
			EscutaDoGodKi = escutaAntes;
			EscutaDoDespertar = despertarAntes;

			try
			{
				foreach (ServerPlayer corpo in forjados) GkRecolher(corpo);
				GkApagarAConta();

				// O HOST VOLTA COMO ESTAVA: a ficha, a forma, o lugar.
				pl.Combate.Curar();
				f.BP = bpGuardado;
				f.GravMastered = gravidadeGuardada;
				f.godki = godkiGuardado;
				f.ExposicaoDivina = exposicaoGuardada;
				f.bp_milestone_mult = marcoGuardado;
				f.bp_milestones_done = marcosGuardados;
				f.dead = mortoGuardado;
				f.KO = koGuardado;
				pl.Forma.Entrar(formaGuardada);
				pl.Forma.Liberadas = liberadasGuardadas;
				pl.Forma.EstreiaVista = estreiasGuardadas;
				pl.CenaSegundos = 0;
				AplicarForma(pl);
				pl.Knockback = knockbackGuardado;
				pl.AlvoId = alvoGuardado;

				if (!pl.Zone.Equals(zonaGuardada)) MoveToZone(pl.Id, zonaGuardada, posGuardada);
				else pl.Pos = posGuardada;
				pl.Facing = rostoGuardado;
				pl.ChunkAtual = chunkGuardada;
				pl.PlanetaDeOrigem = origemGuardada;
				f.Tick(agoraMs: NowMs());
				f.Ki = Math.Min(kiGuardado, f.MaxKi);
				f.stamina = Math.Min(vigorGuardado, f.maxstamina);
				pl.Combate.SincronizarVida();

				_godKiDe.Remove(pl.Id);
				_golpesComUmDeus.Clear();
				_relogioDoGodKi = relogioGuardado;
			}
			finally
			{
				GD.Print($"[godki] ================ {_gkOk} OK, {_gkFalhou} FALHA(S) ================");
			}
		}
	}

	// =====================================================================
	// 1) O REQUISITO
	// =====================================================================
	private void GkORequisito(ServerPlayer pl)
	{
		GD.Print("[godki] -- 1) o requisito: pessoal, em torno de 1 bilhao, e sobre o BP BASE --");

		// MIL PERSONAGENS: a faixa, os valores discretos, e a mesma semente dando sempre o mesmo numero.
		var valores = new SortedSet<double>();
		bool naFaixa = true, estavel = true;
		var rng = new Random(20261010);
		for (int i = 0; i < 1000; i++)
		{
			ulong semente = (ulong)rng.NextInt64();
			double req = DespertarDivino.RequisitoDeBpBase(semente);
			valores.Add(req);
			naFaixa &= req >= DespertarDivino.BpBaseDeFabrica * DespertarDivino.FaixaPessoalDe / 10.0 - 1
					   && req <= DespertarDivino.BpBaseDeFabrica * DespertarDivino.FaixaPessoalAte / 10.0 + 1;
			estavel &= req == DespertarDivino.RequisitoDeBpBase(semente);
		}
		int esperados = DespertarDivino.FaixaPessoalAte - DespertarDivino.FaixaPessoalDe + 1;
		AfirmarGk($"o requisito e PESSOAL: mil personagens caem em {esperados} valores, de {valores.Min:N0} a {valores.Max:N0} "
				  + $"(o centro, {DespertarDivino.BpBaseDeFabrica:N0}, vezes {DespertarDivino.FaixaPessoalDe / 10.0:0.0} a {DespertarDivino.FaixaPessoalAte / 10.0:0.0})",
				  naFaixa && valores.Count == esperados, $"{valores.Count} valores");
		AfirmarGk("...e a mesma semente da sempre o mesmo numero (nada e re-sorteado no login)", estavel);

		// O NUMERO DO DONO, CRAVADO: a regua de cima le a propria constante e passaria com qualquer centro (com os 250
		// milhoes do DM inclusive). Esta e a que reprova se o requisito voltar a ser o de antes.
		AfirmarGk("o centro e 1 BILHAO de BP base (dono, 2026-10-21: *\"suba o requisito pra god ki pra 1 bilhao\"*): "
				  + "mil personagens vao de 900 milhoes a 1,3 bilhao",
				  Math.Abs(valores.Min - 9e8) < 1 && Math.Abs(valores.Max - 1.3e9) < 1,
				  $"{valores.Min:N0} a {valores.Max:N0}");

		double meu = RequisitoDeGodKi(pl);
		AfirmarGk("o do HOST sai da semente dos limiares dele (a gravada no save), e o servidor responde o mesmo duas vezes",
				  meu == RequisitoDeGodKi(pl) && valores.Contains(meu)
				  && (pl.Forma.Limiares is not { Rolado: true, Semente: not 0UL } l
					  || meu == DespertarDivino.RequisitoDeBpBase(l.Semente)),
				  $"{meu:N0} (limiares rolados={pl.Forma.Limiares?.Rolado})");

		// O BP BASE, e so ele.
		var cobaia = new Fighter { Race = "Human", BP = meu - 1 };
		ulong s = SementeDoGodKi(pl);
		bool abaixo = DespertarDivino.AlcancaORequisito(cobaia, s);
		cobaia.BP = meu;
		bool noLimiar = DespertarDivino.AlcancaORequisito(cobaia, s);
		AfirmarGk("um ponto abaixo do requisito NAO alcanca; em cima dele, alcanca", !abaixo && noLimiar);

		cobaia.BP = meu * 0.5;
		cobaia.ssjBuff = 22;
		cobaia.Statify();
		cobaia.Ki = cobaia.MaxKi;
		cobaia.PowerLevel(agoraMs: NowMs());
		AfirmarGk("e o BP BASE que conta: metade do requisito numa forma de 22x (poder expresso muito acima dele) NAO alcanca",
				  cobaia.expressedBP > meu && !DespertarDivino.AlcancaORequisito(cobaia, s),
				  $"base {cobaia.BP:N0}, expresso {cobaia.expressedBP:N0}, requisito {meu:N0}");

		cobaia.BP = meu * 10;
		cobaia.godki_gt_mode = true;
		AfirmarGk("o `godki_gt_mode` fecha a porta, como no DM (`godkiattain.dm:22`)", !DespertarDivino.AlcancaORequisito(cobaia, s));
	}

	// =====================================================================
	// 2) O UNIVERSO
	// =====================================================================
	/// <summary>Quais mundos sao divinos. Devolve o divino mais LEVE da regiao garantida e um comum leve, pras familias seguintes.</summary>
	private (MundoDivino? Divino, PlanetaNoEspaco? Comum) GkOUniverso()
	{
		GD.Print("[godki] -- 2) o universo: a raridade, a garantia e o que nunca e divino --");
		int raio = AtmosferaDivina.RaioGarantidoEmCelulas;

		// ESTE UNIVERSO.
		List<MundoDivino> daqui = AtmosferaDivina.NaRegiao(SeedDoUniverso, 0, 0, raio);
		List<MundoDaGravidade> carta = GcMundosDaCarta(raio);
		int concordam = carta.Count(m => AtmosferaDivina.Tem(SeedDoUniverso, m.Zona) == AtmosferaDivina.Tem(SeedDoUniverso, m.P)
										 && AtmosferaDivina.Tem(SeedDoUniverso, m.P) == daqui.Exists(x => x.Corpo.Seed == m.P.Seed));
		AfirmarGk($"este universo (seed {SeedDoUniverso}) tem {daqui.Count} mundo(s) divino(s) entre os {carta.Count} sorteados da regiao garantida "
				  + $"(raio {raio}), pelo menos {AtmosferaDivina.MinimoGarantido}",
				  daqui.Count >= AtmosferaDivina.MinimoGarantido,
				  string.Join(", ", daqui.Take(6).Select(m => $"{m.Corpo.Nome}{(m.PorGarantia ? "*" : "")}")));
		AfirmarGk("...e as tres perguntas concordam mundo a mundo: pela zona (o servidor), pelo corpo da carta (o cliente) e pela varredura",
				  concordam == carta.Count && carta.Count > 0, $"{concordam} de {carta.Count}");

		// O NOME NAO E CHAVE: quem decide e a seed.
		if (daqui.Count > 0 && carta.FirstOrDefault(m => !AtmosferaDivina.Tem(SeedDoUniverso, m.P)) is { P.Nome.Length: > 0 } outro)
		{
			var comONomeDoDivino = ZoneKey.Procedural(daqui[0].Corpo.Nome, outro.P.Seed);
			AfirmarGk("dois mundos de mesmo NOME e seeds diferentes: so o da seed sorteada e divino",
					  AtmosferaDivina.Tem(SeedDoUniverso, Espaco.ZonaDe(daqui[0].Corpo)) && !AtmosferaDivina.Tem(SeedDoUniverso, comONomeDoDivino));
		}

		// O QUE NUNCA E, E O QUE E POR NATUREZA.
		AfirmarGk("o ESPACO nao e divino (ele e `KindProcedural`, e a seed dele e a do universo), nem um interior, nem a Terra",
				  !AtmosferaDivina.Tem(SeedDoUniverso, ZonaDoEspaco)
				  && !AtmosferaDivina.Tem(SeedDoUniverso, ZoneKey.Interior("Nave", 7))
				  && !AtmosferaDivina.Tem(SeedDoUniverso, ZoneKey.Premade("Earth"))
				  && !AtmosferaDivina.Tem(SeedDoUniverso, ZoneKey.Premade(Alem.ZonaDoOutroMundo)));
		AfirmarGk("o Reino dos Deuses (`God_Realm`) e divino POR NATUREZA -- e ele existe no manifesto dos mapas",
				  AtmosferaDivina.Tem(SeedDoUniverso, ZoneKey.Premade("God_Realm")) && _catalogo?.Get("God_Realm") != null);

		// QUARENTA UNIVERSOS: todos tem o minimo, e a raridade e a da constante.
		var rng = new Random(424242);
		int comOMinimo = 0;
		long mundos = 0, sorteados = 0;
		int menor = int.MaxValue, maior = 0;
		const int Universos = 40;
		for (int i = 0; i < Universos; i++)
		{
			ulong seed = (ulong)rng.NextInt64();
			List<MundoDivino> l = AtmosferaDivina.NaRegiao(seed, 0, 0, raio);
			if (l.Count >= AtmosferaDivina.MinimoGarantido) comOMinimo++;
			menor = Math.Min(menor, l.Count);
			maior = Math.Max(maior, l.Count);
			sorteados += l.Count(m => !m.PorGarantia);
			for (int sy = -raio; sy <= raio; sy++)
				for (int sx = -raio; sx <= raio; sx++)
					if (Sistemas.Do(seed, sx, sy) is { } sis) mundos += sis.Orbitas - (sis.OrbitaPreFeita >= 0 ? 1 : 0);
		}
		double fracao = sorteados / (double)mundos;
		AfirmarGk($"**TODO UNIVERSO TEM MUNDO DIVINO AO ALCANCE**: em {Universos} seeds, todas com pelo menos {AtmosferaDivina.MinimoGarantido} na regiao garantida",
				  comOMinimo == Universos, $"de {menor} a {maior} por universo");
		AfirmarGk($"a raridade e a da constante: 1 mundo divino em cada ~{AtmosferaDivina.UmEm} sorteados",
				  fracao > 0.6 / AtmosferaDivina.UmEm && fracao < 1.5 / AtmosferaDivina.UmEm,
				  $"{sorteados} em {mundos} = 1 em {1 / Math.Max(fracao, 1e-9):0}");

		// A GARANTIA, COM O SORTEIO DESLIGADO: a mesma funcao, e ela tem que trabalhar em todo universo.
		RegraDaAtmosfera semSorteio = RegraDaAtmosfera.Hoje.ComRaridade(0);
		int exatos = 0, coerentes = 0;
		const int Secos = 12;
		ulong seco = 0;
		for (int i = 0; i < Secos; i++)
		{
			ulong seed = (ulong)rng.NextInt64();
			List<MundoDivino> l = AtmosferaDivina.NaRegiao(seed, 0, 0, raio, semSorteio);
			if (l.Count == AtmosferaDivina.MinimoGarantido && l.TrueForAll(m => m.PorGarantia)) exatos++;
			if (l.TrueForAll(m => AtmosferaDivina.Tem(seed, Espaco.ZonaDe(m.Corpo), semSorteio))) coerentes++;
			seco = seed;
		}
		AfirmarGk($"**A GARANTIA**: com o sorteio sem dar NENHUM mundo, cada um de {Secos} universos ganha exatamente "
				  + $"{AtmosferaDivina.MinimoGarantido} pela garantia (os de menor peso da regiao)",
				  exatos == Secos && coerentes == Secos, $"{exatos} exatos, {coerentes} coerentes com a pergunta por zona");

		AtmosferaDivina.GarantiaDesligadaDeTeste = true;
		try
		{
			int semNada = AtmosferaDivina.NaRegiao(seco, 0, 0, raio, semSorteio).Count;
			AfirmarGk("(defeito injetado: sem a garantia) o mesmo universo REPROVA -- nenhum mundo divino, e ninguem nele despertaria",
					  semNada == 0, $"{semNada} mundo(s)");
		}
		finally { AtmosferaDivina.GarantiaDesligadaDeTeste = false; }

		// OS DOIS PALCOS DAS FAMILIAS SEGUINTES: o divino mais leve (o mundo e pequeno, e nasce em poucos ms) e um comum leve.
		MundoDivino? divino = daqui.Where(m => !ZonaMorta(Espaco.ZonaDe(m.Corpo)))
								   .OrderBy(m => MundoProcedural.GravidadeDaSeed(m.Corpo.Seed))
								   .Cast<MundoDivino?>().FirstOrDefault();
		PlanetaNoEspaco? comum = carta.Where(m => !AtmosferaDivina.Tem(SeedDoUniverso, m.P) && m.G <= 5)
									  .Select(m => (PlanetaNoEspaco?)m.P).FirstOrDefault();
		return (divino, comum);
	}

	// =====================================================================
	// 3) A ATMOSFERA  +  4) O DESPERTAR
	// =====================================================================
	private void GkAAtmosferaEODespertar(ServerPlayer pl, ZoneKey zonaDivina, ZoneKey zonaComum, List<ServerPlayer> forjados)
	{
		GD.Print($"[godki] -- 3) a atmosfera: {zonaDivina.Name} (divino) e {zonaComum.Name} (comum) --");
		Fighter f = pl.Ficha;
		double req = RequisitoDeGodKi(pl);
		Vec2 noDivino = ChegadaDaZonaGerada(zonaDivina) ?? default, noComum = ChegadaDaZonaGerada(zonaComum) ?? default;
		ZoneKey terra = ZoneKey.Premade("Earth");
		double porSegundo = DespertarDivino.PorSegundoNaAtmosfera;

		// ---------------------------------------------------------------- (a) ABAIXO DO REQUISITO
		GkVirarMortal(pl, req * 0.5);
		MoveToZone(pl.Id, zonaDivina, noDivino);
		List<string> falas = Ouvir(() => TickDoGodKi(1));
		AfirmarGk("AO CHEGAR num mundo divino o jogador e avisado: \"ATMOSFERA DIVINA\"", Disse(falas, "ATMOSFERA DIVINA"), Ultimos(falas));
		AfirmarGk("...e, abaixo do requisito, ouve que o corpo ainda nao alcanca", Disse(falas, "ainda não o alcança"), Ultimos(falas));

		falas = Ouvir(() => { for (int i = 0; i < 300; i++) TickDoGodKi(1); });
		AfirmarGk("**ABAIXO DO REQUISITO NADA ACUMULA**: cinco minutos na atmosfera, e o medidor continua em zero",
				  f.ExposicaoDivina == 0 && !DespertarDivino.JaDespertou(f), $"{f.ExposicaoDivina:0.###}%");
		AfirmarGk("...e a frase e UMA: nesses cinco minutos o servidor nao disse mais nada",
				  !Disse(falas, "alcança") && !Disse(falas, "ATMOSFERA"), Ultimos(falas));
		AfirmarGk("o retrato que foi pro cliente diz: na atmosfera, sem alcancar o requisito, 0%",
				  GkUltimoRetrato(pl) is { } r0 && r0.Bits == (byte)Protocol.BitsDoGodKi.NaAtmosfera && r0.Pct == 0,
				  GkRetratoEmTexto(pl));

		GodKiSemRequisitoDeTeste = true;
		try
		{
			TickDoGodKi(60);
			AfirmarGk("(defeito injetado: ninguem pergunta pelo requisito) o mesmo minuto REPROVA -- o corpo de BP baixo acumula",
					  f.ExposicaoDivina > 5, $"{f.ExposicaoDivina:0.##}%");
		}
		finally { GodKiSemRequisitoDeTeste = false; }
		f.ExposicaoDivina = 0;
		TickDoGodKi(1);   // de volta a regra: o corpo para de absorver (e a frase de "nao alcanca" ja foi dita)

		// ---------------------------------------------------------------- (b) CRUZA O REQUISITO SEM SAIR DO MUNDO
		f.BP = req;
		falas = Ouvir(() => TickDoGodKi(1));
		AfirmarGk("no segundo em que o BP BASE alcanca o requisito, o corpo comeca a absorver -- e o jogador ouve isso",
				  Disse(falas, "começa a absorvê-lo") && Math.Abs(f.ExposicaoDivina - porSegundo) < 1e-9,
				  $"{f.ExposicaoDivina:0.####}% | {Ultimos(falas)}");

		falas = Ouvir(() => { for (int i = 0; i < 59; i++) TickDoGodKi(1); });
		AfirmarGk($"**O TEMPO NA ATMOSFERA ENCHE O MEDIDOR**: um minuto = {porSegundo * 60:0.##} pontos ({DespertarDivino.SegundosNaAtmosfera / 60:0} minutos pra encher)",
				  Math.Abs(f.ExposicaoDivina - porSegundo * 60) < 1e-6 && !Disse(falas, "absorvê-lo"), $"{f.ExposicaoDivina:0.####}%");
		AfirmarGk("o retrato do cliente acompanha: alcanca o requisito, na atmosfera, 10%",
				  GkUltimoRetrato(pl) is { } r1 && r1.Pct == 10
				  && r1.Bits == (byte)(Protocol.BitsDoGodKi.NaAtmosfera | Protocol.BitsDoGodKi.AlcancaORequisito),
				  GkRetratoEmTexto(pl));

		falas = Ouvir(() => { for (int i = 0; i < 180; i++) TickDoGodKi(1); });   // 240 s = 40%
		AfirmarGk("as ETAPAS sao contadas ao jogador: os 25% passaram, e ele ouviu", Disse(falas, "(25%)") && !Disse(falas, "(50%)"), Ultimos(falas));
		double aos40 = f.ExposicaoDivina;
		AfirmarGk("quatro minutos = 40%", Math.Abs(aos40 - 40) < 1e-6, $"{aos40:0.####}%");

		// ---------------------------------------------------------------- (c) PLANETA COMUM, E NAO ZERA AO SAIR
		MoveToZone(pl.Id, zonaComum, noComum);
		falas = Ouvir(() => { for (int i = 0; i < 300; i++) TickDoGodKi(1); });
		AfirmarGk("**PLANETA COMUM NAO DESPERTA NINGUEM**: cinco minutos num mundo sorteado sem atmosfera, e o medidor nao anda",
				  f.ExposicaoDivina == aos40 && !Disse(falas, "ATMOSFERA"), $"{f.ExposicaoDivina:0.####}%");
		AfirmarGk("...e ele NAO ZERA ao sair do mundo divino: os 40% continuam la", f.ExposicaoDivina == aos40);

		GodKiEmQualquerChaoDeTeste = true;
		try
		{
			TickDoGodKi(60);
			AfirmarGk("(defeito injetado: todo chao e divino) o mesmo planeta comum REPROVA -- o medidor anda",
					  f.ExposicaoDivina > aos40 + 5, $"{f.ExposicaoDivina:0.##}%");
		}
		finally { GodKiEmQualquerChaoDeTeste = false; }
		f.ExposicaoDivina = aos40;
		_godKiDe.Remove(pl.Id);

		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		for (int i = 0; i < 120; i++) TickDoGodKi(1);
		AfirmarGk("na Terra tambem nao (mapa feito a mao, fora da lista dos divinos por natureza)", f.ExposicaoDivina == aos40);

		// ---------------------------------------------------------------- (d) DE VOLTA: O CADAVER E O NOCAUTEADO
		MoveToZone(pl.Id, zonaDivina, noDivino);
		falas = Ouvir(() => TickDoGodKi(1));
		AfirmarGk("de volta ao mundo divino o aviso sai de novo, com o que ja foi juntado (\"40% até agora\")",
				  Disse(falas, "ATMOSFERA DIVINA") && Disse(falas, "40% até agora"), Ultimos(falas));

		double antesDeMorrer = f.ExposicaoDivina;
		f.dead = true;
		for (int i = 0; i < 60; i++) TickDoGodKi(1);
		bool cadaverParado = f.ExposicaoDivina == antesDeMorrer && EhCadaver(pl);
		f.dead = false;
		AfirmarGk("um CADAVER nao respira: um minuto morto no chao do mundo divino nao enche nada", cadaverParado, $"{f.ExposicaoDivina:0.####}%");

		f.KO = true;
		for (int i = 0; i < 6; i++) TickDoGodKi(1);
		f.KO = false;
		AfirmarGk("o NOCAUTEADO respira: caido no mundo divino o medidor continua andando",
				  Math.Abs(f.ExposicaoDivina - (antesDeMorrer + porSegundo * 6)) < 1e-6, $"{f.ExposicaoDivina:0.####}%");

		// ---------------------------------------------------------------- 4) O DESPERTAR
		GD.Print("[godki] -- 4) o despertar: o que ele escreve, quem ouve e o que o jogo passa a enxergar --");

		ServerPlayer vizinho = GkForjar("gk: quem esta perto", "Human", zonaDivina, pl.Pos + new Vec2(3 * ZoneCollision.TileSize, 0), 1000, forjados);
		ServerPlayer distante = GkForjar("gk: quem esta longe", "Human", zonaDivina, pl.Pos + new Vec2(60 * ZoneCollision.TileSize, 0), 1000, forjados);

		// PRIMEIRO O DEFEITO, que e o jogo de ANTES: o medidor enche e ninguem liga `awakened`.
		double guardada = f.ExposicaoDivina;
		GodKiNaoDespertaDeTeste = true;
		try
		{
			f.ExposicaoDivina = DespertarDivino.ExposicaoCheia - porSegundo / 2;
			TickDoGodKi(1);
			AfirmarGk("(defeito injetado: ninguem liga `awakened`) o medidor cheio REPROVA -- 100% e nenhum deus: o perfil de formas "
					  + "continua em -1 e o fato do desafio apagado. E o jogo de antes desta bancada",
					  f.ExposicaoDivina >= DespertarDivino.ExposicaoCheia && !DespertarDivino.JaDespertou(f)
					  && Perfil(pl).GodKi < 0 && (FatosDe(pl) & Fatos.GodKiDesperto) == 0,
					  $"{f.ExposicaoDivina:0.##}%, perfil {Perfil(pl).GodKi:0}");
		}
		finally { GodKiNaoDespertaDeTeste = false; }
		f.ExposicaoDivina = guardada;

		// O ANTES, medido.
		f.PowerLevel(agoraMs: NowMs());
		double expressoAntes = f.expressedBP, marcoAntes = f.bp_milestone_mult;
		double perfilAntes = Perfil(pl).GodKi;
		bool fatoAntes = (FatosDe(pl) & Fatos.GodKiDesperto) != 0;
		bool botaoAntes = RequisitosDosVerbos.DoVerbo("cargo_desafiar").Atende(PosseDe(pl));
		int linhasAntes = Catalogo.LinhasAbertas(Perfil(pl)).Count;
		EscutaDoDespertar!.Clear();

		// O RESTO DO TEMPO, segundo a segundo: 247 s ja contaram (4 min, o segundo da volta e os 6 s caido).
		int segundos = 0;
		falas = Ouvir(() =>
		{
			while (!DespertarDivino.JaDespertou(f) && segundos < 2000) { TickDoGodKi(1); segundos++; }
		});
		int total = 247 + segundos;
		AfirmarGk($"**ACIMA DO REQUISITO, O TEMPO NA ATMOSFERA DESPERTA O KI DIVINO**: aos {total} s somados "
				  + $"(a constante: {DespertarDivino.SegundosNaAtmosfera:0} s)",
				  DespertarDivino.JaDespertou(f) && Math.Abs(total - DespertarDivino.SegundosNaAtmosfera) <= 1, $"{total} s");
		AfirmarGk("...passando pelas etapas de 50% e 75%, ditas ao jogador", Disse(falas, "(50%)") && Disse(falas, "(75%)"), "");
		AfirmarGk("o jogador ouve o despertar e o marco -- e nenhuma frase promete poder \"pra sempre\" (o bonus saiu: dono, 2026-10-23)",
				  Disse(falas, "O KI DIVINO DESPERTOU EM VOCÊ") && !Disse(falas, "pra sempre") && Disse(falas, "MARCO DE PODER"), Ultimos(falas));
		// QUEM OUVE: os corpos da zona dentro da vista (o vizinho, a 3 tiles), e nao os de fora dela (o distante, a 60).
		int naVista = ZoneList(pl.Zone.Hash).Count(o => o != pl && (o.Pos - pl.Pos).LengthSquared <= RaioDaVista * RaioDaVista);
		int ouviram = falas.Count(x => x.Contains("o KI DIVINO despertou nele", StringComparison.Ordinal));
		AfirmarGk("QUEM ESTA EM VOLTA fica sabendo (a frase e o `S2C.GodKiDespertou`); quem esta a 60 tiles, nao",
				  ouviram == naVista && naVista >= 1
				  && (vizinho.Pos - pl.Pos).Length <= RaioDaVista && (distante.Pos - pl.Pos).Length > RaioDaVista
				  && ZoneList(pl.Zone.Hash).Contains(distante)
				  && EscutaDoDespertar.Count == 1 && EscutaDoDespertar[0].Quem == pl.Id && EscutaDoDespertar[0].Telas >= 1,
				  $"{ouviram} frase(s) pra {naVista} corpo(s) na vista, {(EscutaDoDespertar.Count > 0 ? EscutaDoDespertar[0].Telas : 0)} tela(s)");

		// O QUE O DESPERTAR ESCREVEU.
		AfirmarGk("a ficha: `godki.awakened`, o medidor em 100, maestria 0 (o sangue comum comeca do zero)",
				  f.godki is { awakened: true } && f.ExposicaoDivina == DespertarDivino.ExposicaoCheia
				  && (DespertarDivino.SangueDivino(f) || f.godki.mastery == 0), $"maestria {f.godki?.mastery:0.##}");
		AfirmarGk($"o MARCO de ganho (`bp_milestone_reach(\"godki\")`): todo ganho agora e x{Milestones.Valor(DespertarDivino.MarcoDoDespertar):0}",
				  f.bp_milestones_done.Contains(DespertarDivino.MarcoDoDespertar)
				  && f.bp_milestone_mult == Math.Max(marcoAntes, Milestones.Valor(DespertarDivino.MarcoDoDespertar)),
				  $"x{marcoAntes:0.##} -> x{f.bp_milestone_mult:0.##}");
		// (2026-10-23) O PODER NAO MUDA MAIS NO DESPERTAR: os 2,5x pra sempre do DM (`godki_give_mult`) sairam. A regua com o contra-exemplo
		// vem mais abaixo, num Humano forjado -- o host pode ser Primal, e o Primal nunca teve o bonus.
		f.PowerLevel(agoraMs: NowMs());
		AfirmarGk("GKB1. **O DESPERTAR NAO MULTIPLICA MAIS O PODER** (dono, 2026-10-23: *\"o god ki dobrar o bp quando pega n achei mt legal nao, "
				  + "so as formas e as passivas de god ki ja estao bons\"*): o poder expresso do host, sem forma nenhuma, e o de antes",
				  Math.Abs(f.expressedBP / expressoAntes - 1) < 0.01,
				  $"{expressoAntes:N0} -> {f.expressedBP:N0} (x{f.expressedBP / expressoAntes:0.###}; "
				  + $"{(f.SaiyanLineage == Oozaru.LinhagemPrimal ? "Primal" : "nao e Primal")})");
		AfirmarGk("o retrato do cliente: DESPERTO (e a maestria passa a viajar)",
				  GkUltimoRetrato(pl) is { } r2 && (r2.Bits & (byte)Protocol.BitsDoGodKi.Desperto) != 0 && r2.Pct == 100, GkRetratoEmTexto(pl));

		// O QUE O RESTO DO JOGO PASSA A ENXERGAR.
		AfirmarGk("**O PERFIL DE FORMAS ENXERGA O KI DIVINO**: de -1 (nunca despertou) pra a maestria, e uma linha divina se abre",
				  perfilAntes < 0 && Perfil(pl).GodKi >= 0 && Catalogo.LinhasAbertas(Perfil(pl)).Count > linhasAntes,
				  $"perfil {perfilAntes:0} -> {Perfil(pl).GodKi:0}; linhas {linhasAntes} -> {Catalogo.LinhasAbertas(Perfil(pl)).Count}");
		AfirmarGk("**O FATO `GodKiDesperto` LIGA**, e com ele o botao \"Challenge God of Destruction\" passa a existir pra este personagem",
				  !fatoAntes && !botaoAntes && (FatosDe(pl) & Fatos.GodKiDesperto) != 0
				  && RequisitosDosVerbos.DoVerbo("cargo_desafiar").Atende(PosseDe(pl)));

		// IDEMPOTENTE: despertar de novo nao dobra nada.
		double marco = f.bp_milestone_mult;
		bool deNovo = DespertarOGodKi(pl, FonteDaExposicao.Admin);
		for (int i = 0; i < 30; i++) TickDoGodKi(1);
		AfirmarGk("despertar duas vezes nao existe: a segunda chamada devolve falso, e mais meio minuto de atmosfera nao muda nada",
				  !deNovo && f.bp_milestone_mult == marco && f.ExposicaoDivina == DespertarDivino.ExposicaoCheia);

		// AS EXCECOES DO ORIGINAL, em corpos forjados (pelo funil do servidor).
		ServerPlayer saiyajin = GkForjar("gk: Saiyajin Normal", "Saiyan", zonaDivina, noDivino, req, forjados);
		saiyajin.Ficha.Class = "Normal";
		saiyajin.Ficha.SaiyanLineage = "Saiyan";
		RecusaForma antes = saiyajin.Forma.Avaliar("ssg", saiyajin.Ficha.BP, 1, false, Perfil(saiyajin));
		DespertarOGodKi(saiyajin, FonteDaExposicao.Admin);
		RecusaForma depois = saiyajin.Forma.Avaliar("ssg", saiyajin.Ficha.BP, 1, false, Perfil(saiyajin));
		AfirmarGk("**O SUPER SAIYAJIN GOD FICA AO ALCANCE** de um Saiyajin de classe Normal no instante do despertar (antes era recusado)",
				  antes != RecusaForma.Pode && depois == RecusaForma.Pode, $"{antes} -> {depois}");
		AfirmarGk("...e o Blue continua fechado: ele pede 33% de maestria divina, e quem acabou de despertar tem 0",
				  saiyajin.Forma.Avaliar("blue", saiyajin.Ficha.BP * 100, 1, false, Perfil(saiyajin)) != RecusaForma.Pode);

		ServerPlayer primalSaiyan = GkForjar("gk: Primal", "Saiyan", zonaDivina, noDivino, req, forjados);
		primalSaiyan.Ficha.SaiyanLineage = Oozaru.LinhagemPrimal;
		DespertarOGodKi(primalSaiyan, FonteDaExposicao.Admin);
		AfirmarGk("o PRIMAL desperta e ganha o marco (o bonus de poder que o DM ja lhe negava, `godkiattain.dm:38`, desde 2026-10-23 nao e de ninguem)",
				  primalSaiyan.Ficha.godki is { awakened: true }
				  && primalSaiyan.Ficha.bp_milestones_done.Contains(DespertarDivino.MarcoDoDespertar));

		ServerPlayer kai = GkForjar("gk: Kai", "Kai", zonaDivina, noDivino, req, forjados);
		ServerPlayer filhoDeDemonio = GkForjar("gk: filho de Demonio", "Human", zonaDivina, noDivino, req, forjados);
		filhoDeDemonio.Ficha.ParentRace = "Demon";
		ServerPlayer humano = GkForjar("gk: Humano", "Human", zonaDivina, noDivino, req, forjados);
		humano.Ficha.PowerLevel(agoraMs: NowMs());
		double humanoAntes = humano.Ficha.expressedBP;
		DespertarOGodKi(kai, FonteDaExposicao.Admin);
		DespertarOGodKi(filhoDeDemonio, FonteDaExposicao.Admin);
		DespertarOGodKi(humano, FonteDaExposicao.Admin);
		AfirmarGk($"o SANGUE DIVINO (Kai, Demonio, ou filho de um) ja desperta com {DespertarDivino.MaestriaDoSangueDivino:0}% de maestria; o Humano, com 0",
				  kai.Ficha.godki?.mastery == DespertarDivino.MaestriaDoSangueDivino
				  && filhoDeDemonio.Ficha.godki?.mastery == DespertarDivino.MaestriaDoSangueDivino
				  && humano.Ficha.godki is { awakened: true, mastery: 0 },
				  $"Kai {kai.Ficha.godki?.mastery:0}, filho de Demonio {filhoDeDemonio.Ficha.godki?.mastery:0}, Humano {humano.Ficha.godki?.mastery:0}");

		// (2026-10-23) O PODER NAO MUDA NO DESPERTAR, num corpo que com certeza nao e Primal (o Humano): a regua e o contra-exemplo dela. O
		// defeito injetado e o jogo de antes (`Fighter.BonusDoGodKiPraSempreDeTeste`, lido pela propria linha do `GodKiFactor`).
		long agora = NowMs();
		humano.Ficha.PowerLevel(agoraMs: agora);
		double humanoDepois = humano.Ficha.expressedBP, humanoComBonus;
		Fighter.BonusDoGodKiPraSempreDeTeste = true;
		try
		{
			humano.Ficha.PowerLevel(agoraMs: agora);
			humanoComBonus = humano.Ficha.expressedBP;
		}
		finally
		{
			Fighter.BonusDoGodKiPraSempreDeTeste = false;
			humano.Ficha.PowerLevel(agoraMs: agora);
		}
		AfirmarGk("GKB2. **...E NO HUMANO DESPERTO TAMBEM**: o poder expresso e o de antes do despertar -- ficam so as formas divinas e as passivas",
				  DespertarDivino.JaDespertou(humano.Ficha) && Math.Abs(humanoDepois / humanoAntes - 1) < 0.01,
				  $"{humanoAntes:N0} -> {humanoDepois:N0} (x{humanoDepois / humanoAntes:0.###})");
		AfirmarGk($"   (defeito injetado: os {humano.Ficha.godki_boost:0.#}x pra sempre de antes de 2026-10-23) a mesma regua REPROVA: o Humano desperto vale "
				  + $"x{humano.Ficha.godki_boost:0.#} sem forma nenhuma",
				  Math.Abs(humanoComBonus / humanoAntes - humano.Ficha.godki_boost) < 0.02 * humano.Ficha.godki_boost,
				  $"{humanoAntes:N0} -> {humanoComBonus:N0} (x{humanoComBonus / humanoAntes:0.###})");
	}

	// =====================================================================
	// 5) A LUTA
	// =====================================================================
	private void GkALuta(ServerPlayer pl, ZoneKey zonaDivina, ZoneKey zonaComum, List<ServerPlayer> forjados)
	{
		GD.Print("[godki] -- 5) a luta: trocar golpes de verdade com quem carrega ki divino --");
		Fighter f = pl.Ficha;
		double req = RequisitoDeGodKi(pl);
		Vec2 ringue = ChegadaDaZonaGerada(zonaComum) ?? default;
		double dado = DespertarDivino.PorGolpeDado, recebido = DespertarDivino.PorGolpeRecebido;

		MoveToZone(pl.Id, zonaComum, ringue);
		GkVirarMortal(pl, req);

		// O DEUS: um corpo com dono na tela (a tela emprestada do host) e o ki divino desperto.
		ServerPlayer deus = GkForjar("gk: o deus", "Human", zonaComum, ringue, f.BP, forjados);
		deus.Peer = pl.Peer;
		deus.Ficha.godki = new GodKiState { awakened = true };
		GkIgualarOPoder(deus, pl);
		AfirmarGk("(montagem) o deus e um jogador com ki divino, e o host e um mortal que ja alcanca o requisito",
				  EhJogador(deus) && EhUmDeus(deus) && !EhUmDeus(pl) && AlcancaOKiDivino(pl) && f.ExposicaoDivina == 0);

		// ---------------------------------------------------------------- (a) SEM REVIDE, NOS DOIS SENTIDOS
		int certos = 0;
		for (int i = 0; i < 12; i++) if (GkSoco(pl, deus, ringue)) certos++;
		AfirmarGk("**BATER NUM DEUS QUE NAO REVIDA NAO ENSINA NADA**: doze golpes certos nele, parado, e o medidor em zero",
				  certos == 12 && f.ExposicaoDivina == 0, $"{certos} golpes, {f.ExposicaoDivina:0.###}%");

		GodKiSemRevideDeTeste = true;
		try
		{
			GkSoco(pl, deus, ringue);
			AfirmarGk("(defeito injetado: o golpe conta sem troca) o mesmo golpe REPROVA -- bater num deus parado enche o medidor",
					  f.ExposicaoDivina > 0, $"{f.ExposicaoDivina:0.###}%");
		}
		finally { GodKiSemRevideDeTeste = false; }
		GkZerarALuta(pl);

		for (int i = 0; i < 20; i++) TickDoGodKi(1);   // a janela do revide vence
		certos = 0;
		for (int i = 0; i < 12; i++) if (GkSoco(deus, pl, ringue)) certos++;
		AfirmarGk("**E FICAR PARADO APANHANDO DE UM DEUS TAMBEM NAO**: doze golpes dele em quem nao revida, e nada",
				  certos == 12 && f.ExposicaoDivina == 0, $"{certos} golpes, {f.ExposicaoDivina:0.###}%");

		// ---------------------------------------------------------------- (b) A TROCA, GOLPE A GOLPE
		GkZerarALuta(pl);
		for (int i = 0; i < 20; i++) TickDoGodKi(1);
		List<string> falas = Ouvir(() =>
		{
			GkSoco(pl, deus, ringue);   // o primeiro: ainda nao houve revide
			GkSoco(deus, pl, ringue);   // o revide: o host RECEBEU um golpe de um deus
		});
		AfirmarGk($"a TROCA: o primeiro golpe nao vale (ninguem revidou ainda); o revide do deus vale o golpe RECEBIDO ({recebido:0.##})",
				  Math.Abs(f.ExposicaoDivina - recebido) < 1e-9, $"{f.ExposicaoDivina:0.###}%");
		AfirmarGk("...e o jogador ouve, uma vez, que o ki do outro e divino e que trocar golpes acorda o dele",
				  Disse(falas, "é divino") && Disse(falas, "acorda o seu"), Ultimos(falas));
		falas = Ouvir(() => GkSoco(pl, deus, ringue));
		AfirmarGk($"...e o golpe seguinte do host, ja dentro da troca, vale o golpe DADO ({dado:0.##}) -- sem repetir a frase",
				  Math.Abs(f.ExposicaoDivina - (recebido + dado)) < 1e-9 && !Disse(falas, "acorda o seu"), $"{f.ExposicaoDivina:0.###}%");

		// ---------------------------------------------------------------- (c) O QUE NAO CONTA
		double antes = f.ExposicaoDivina;
		ResolverDesfecho(pl, deus, new GolpeResultado { Desfecho = Desfecho.Esquivou, Membro = "" });
		ResolverDesfecho(deus, pl, new GolpeResultado { Desfecho = Desfecho.Errou, Membro = "" });
		AfirmarGk("o GOLPE QUE NAO ENCOSTA nao conta: uma esquiva e um soco no ar, no meio da troca, nao rendem nada",
				  f.ExposicaoDivina == antes);

		deus.Ficha.KO = true;
		bool acertouOCaido = GkSoco(pl, deus, ringue, curar: false);
		AfirmarGk("**BATER EM NOCAUTEADO NAO CONTA**: o deus no chao leva o golpe, e o medidor nao anda",
				  acertouOCaido && f.ExposicaoDivina == antes, $"acertou={acertouOCaido}, {f.ExposicaoDivina:0.###}%");
		deus.Ficha.KO = false;

		f.KO = true;
		bool acertouOHostCaido = GkSoco(deus, pl, ringue, curar: false);
		AfirmarGk("...e apanhar caido tambem nao: o host no chao leva o golpe do deus, e nada",
				  acertouOHostCaido && f.ExposicaoDivina == antes, $"acertou={acertouOHostCaido}, {f.ExposicaoDivina:0.###}%");
		f.KO = false;

		for (int i = 0; i < (int)DespertarDivino.SegundosDeRevide + 2; i++) TickDoGodKi(1);
		GkSoco(pl, deus, ringue);
		AfirmarGk($"a JANELA do revide: {DespertarDivino.SegundosDeRevide:0} s depois do ultimo golpe do deus, o golpe do host volta a nao valer",
				  f.ExposicaoDivina == antes, $"{f.ExposicaoDivina:0.###}%");

		// ---------------------------------------------------------------- (d) O TETO POR MINUTO
		GkZerarALuta(pl);
		for (int i = 0; i < 20; i++) TickDoGodKi(1);
		int trocas = 0;
		for (int s = 0; s < 55; s++) { GkTroca(pl, deus, ringue); trocas++; TickDoGodKi(1); }
		AfirmarGk($"**O TETO**: {trocas} trocas em menos de um minuto ({trocas * (dado + recebido):0.#} pontos em golpes) rendem "
				  + $"{DespertarDivino.TetoDeLutaPorMinuto:0}, e nem um a mais",
				  Math.Abs(f.ExposicaoDivina - DespertarDivino.TetoDeLutaPorMinuto) < 1e-6, $"{f.ExposicaoDivina:0.###}%");
		for (int s = 0; s < 60; s++) { GkTroca(pl, deus, ringue); TickDoGodKi(1); }
		AfirmarGk("...e no minuto seguinte o teto abre de novo: dois minutos de luta = o dobro",
				  Math.Abs(f.ExposicaoDivina - 2 * DespertarDivino.TetoDeLutaPorMinuto) < 1e-6, $"{f.ExposicaoDivina:0.###}%");

		GodKiSemTetoDeLutaDeTeste = true;
		try
		{
			double semTeto = f.ExposicaoDivina;
			for (int i = 0; i < 30; i++) GkTroca(pl, deus, ringue);
			AfirmarGk("(defeito injetado: a luta sem teto) trinta trocas no mesmo segundo REPROVAM -- o medidor salta",
					  f.ExposicaoDivina - semTeto > DespertarDivino.TetoDeLutaPorMinuto, $"+{f.ExposicaoDivina - semTeto:0.#} num segundo");
		}
		finally { GodKiSemTetoDeLutaDeTeste = false; }

		// ---------------------------------------------------------------- (e) ABAIXO DO REQUISITO, PELA LUTA
		GkVirarMortal(pl, req * 0.5);
		GkIgualarOPoder(deus, pl);
		for (int i = 0; i < 20; i++) TickDoGodKi(1);
		falas = Ouvir(() => { for (int i = 0; i < 6; i++) GkTroca(pl, deus, ringue); });
		AfirmarGk("**ABAIXO DO REQUISITO, LUTAR COM UM DEUS TAMBEM NAO ACUMULA** -- e a frase de que o corpo nao alcanca sai uma vez so",
				  f.ExposicaoDivina == 0 && falas.Count(x => x.Contains("ainda não o alcança", StringComparison.Ordinal)) == 1,
				  $"{f.ExposicaoDivina:0.###}% | {falas.Count(x => x.Contains("ainda não o alcança", StringComparison.Ordinal))} frase(s)");

		GodKiSemRequisitoDeTeste = true;
		try
		{
			GkTroca(pl, deus, ringue);
			AfirmarGk("(defeito injetado: ninguem pergunta pelo requisito) a mesma troca REPROVA -- o corpo de BP baixo acumula lutando",
					  f.ExposicaoDivina > 0, $"{f.ExposicaoDivina:0.###}%");
		}
		finally { GodKiSemRequisitoDeTeste = false; }

		// ---------------------------------------------------------------- (f) QUEM NAO E UM DEUS
		GkVirarMortal(pl, req);
		ServerPlayer mortal = GkForjar("gk: outro mortal", "Human", zonaComum, ringue, f.BP, forjados);
		mortal.Peer = pl.Peer;
		GkIgualarOPoder(mortal, pl);
		for (int i = 0; i < 20; i++) TickDoGodKi(1);
		for (int i = 0; i < 6; i++) GkTroca(pl, mortal, ringue);
		AfirmarGk("dois mortais trocando golpes nao despertam nada: sem ki divino do outro lado nao ha o que absorver",
				  f.ExposicaoDivina == 0 && mortal.Ficha.ExposicaoDivina == 0);
		mortal.Peer = null;

		ServerPlayer reflexo = GkForjar("gk: o reflexo de um deus", "Human", zonaComum, ringue, f.BP, forjados);
		reflexo.Ficha.godki = new GodKiState { awakened = true };
		GkIgualarOPoder(reflexo, pl);
		for (int i = 0; i < 6; i++) GkTroca(pl, reflexo, ringue);
		AfirmarGk("o REFLEXO de um deus nao e um deus: corpo sem dono e sem molde (um clone, um boneco) com a ficha dele nao desperta ninguem",
				  !EhUmDeus(reflexo) && f.ExposicaoDivina == 0, $"{f.ExposicaoDivina:0.###}%");

		// ---------------------------------------------------------------- (g) O NPC NUMA FORMA DIVINA
		ServerPlayer? npc = NascerNpc("lutador_de_torneio", zonaComum, ringue, 9_771_001UL);
		AfirmarGk("(montagem) um NPC do mundo nasceu no ringue (o molde `lutador_de_torneio`)", npc != null && EhNpcDoMundo(npc));
		if (npc != null)
		{
			forjados.Add(npc);
			npc.Knockback = false;
			bool antesDaForma = EhUmDeus(npc);
			Verbo(pl, "admin_forma", $"{npc.Id}|ssg");
			npc.CenaSegundos = 0;   // a cena da estreia prende o corpo (intocavel); o assunto aqui e o golpe
			GkIgualarOPoder(npc, pl);
			AfirmarGk("**\"OU ESTAO TRANSFORMADAS EM FORMAS DIVINAS\"**: um NPC sem ki divino desperto, posto em Super Saiyajin God, conta como deus",
					  !antesDaForma && npc.Ficha.godki is not { awakened: true } && npc.Forma.Atual == "ssg" && EhUmDeus(npc),
					  $"forma {npc.Forma.Atual}");
			for (int i = 0; i < 20; i++) TickDoGodKi(1);
			for (int i = 0; i < 4; i++) GkTroca(pl, npc, ringue);
			AfirmarGk("...e trocar golpes com ele enche o medidor do host",
					  Math.Abs(f.ExposicaoDivina - (4 * (dado + recebido) - dado)) < 1e-6, $"{f.ExposicaoDivina:0.###}%");

			Verbo(pl, "admin_forma", $"{npc.Id}|base");
			double semForma = f.ExposicaoDivina;
			for (int i = 0; i < 4; i++) GkTroca(pl, npc, ringue);
			AfirmarGk("...e de volta a forma base ele deixa de contar", !EhUmDeus(npc) && f.ExposicaoDivina == semForma);
		}

		// O TIRO DE KI entra pela mesma porta: o passo 4 do `Acertar` e `AplicarDanoPronto` + `ResolverDesfecho`.
		GkZerarALuta(pl);
		for (int i = 0; i < 20; i++) TickDoGodKi(1);
		GkRefazer(pl);
		GkRefazer(deus);
		GkSoco(pl, deus, ringue);
		GkRefazer(pl);
		ResolverDesfecho(deus, pl, MeleeResolver.AplicarDanoPronto(pl.Combate, 5, false, _rng));
		AfirmarGk("o TIRO DE KI de um deus entra pelo mesmo gancho do soco (o desfecho do `Acertar`): levar um vale o golpe recebido",
				  Math.Abs(f.ExposicaoDivina - recebido) < 1e-9, $"{f.ExposicaoDivina:0.###}%");

		// ---------------------------------------------------------------- (h) OS DOIS CAMINHOS SOMAM
		GkZerarALuta(pl);
		ZoneKey ringueDivino = zonaDivina;
		Vec2 la = ChegadaDaZonaGerada(ringueDivino) ?? default;
		MoveToZone(pl.Id, ringueDivino, la);
		GkLevar(deus, ringueDivino, la);
		for (int i = 0; i < 20; i++) TickDoGodKi(1);
		f.ExposicaoDivina = 0;
		GkZerarALuta(pl, manterAExposicao: true);
		for (int s = 0; s < 60; s++) { GkTroca(pl, deus, la); TickDoGodKi(1); }
		double somado = DespertarDivino.PorSegundoNaAtmosfera * 60 + DespertarDivino.TetoDeLutaPorMinuto;
		AfirmarGk($"**OS DOIS CAMINHOS SOMAM**: um minuto lutando com um deus NUM MUNDO DIVINO rende {somado:0.#} (o ar + o teto da luta)",
				  Math.Abs(f.ExposicaoDivina - somado) < 0.5, $"{f.ExposicaoDivina:0.###}%");

		// ---------------------------------------------------------------- (i) LUTAR ATE DESPERTAR
		MoveToZone(pl.Id, zonaComum, ringue);
		GkLevar(deus, zonaComum, ringue);
		GkVirarMortal(pl, req);
		GkIgualarOPoder(deus, pl);
		for (int i = 0; i < 20; i++) TickDoGodKi(1);
		int segundos = 0;
		EscutaDoDespertar!.Clear();
		falas = Ouvir(() =>
		{
			while (!DespertarDivino.JaDespertou(f) && segundos < 3000) { GkTroca(pl, deus, ringue); TickDoGodKi(1); segundos++; }
		});
		double minimo = (DespertarDivino.ExposicaoCheia / DespertarDivino.TetoDeLutaPorMinuto - 1) * DespertarDivino.SegundosDoTeto;
		AfirmarGk($"**LUTAR COM UM DEUS DESPERTA O KI DIVINO** -- num planeta comum, so pela luta, em {segundos} s de troca "
				  + $"(o teto nao deixa ser em menos de {minimo:0} s)",
				  DespertarDivino.JaDespertou(f) && segundos >= minimo && segundos <= minimo + DespertarDivino.SegundosDoTeto,
				  $"{segundos} s");
		AfirmarGk("...com as etapas e o despertar ditos ao jogador, e o anel de luz anunciado",
				  Disse(falas, "(50%)") && Disse(falas, "O KI DIVINO DESPERTOU EM VOCÊ") && EscutaDoDespertar.Exists(x => x.Quem == pl.Id), "");
		double depoisDeDespertar = f.ExposicaoDivina;
		for (int i = 0; i < 5; i++) GkTroca(pl, deus, ringue);
		AfirmarGk("depois de desperto, a luta nao soma mais nada no medidor (ele e de quem ainda nao despertou)",
				  f.ExposicaoDivina == depoisDeDespertar);

		deus.Peer = null;
	}

	// =====================================================================
	// 6) A MAESTRIA DIVINA
	// =====================================================================
	private void GkAMaestria(ServerPlayer pl, ZoneKey zonaComum)
	{
		GD.Print("[godki] -- 6) a maestria divina: so sobe USANDO o ki divino (o `godki_mastery_tick` do DM) --");
		Fighter f = pl.Ficha;
		string classeGuardada = f.Class, linhagemGuardada = f.SaiyanLineage;
		double horas = DespertarDivino.HorasDeUsoPraMaestriaCheia;

		try
		{
			if (!pl.Zone.Equals(zonaComum)) MoveToZone(pl.Id, zonaComum, ChegadaDaZonaGerada(zonaComum) ?? default);
			if (!DespertarDivino.JaDespertou(f)) DespertarOGodKi(pl, FonteDaExposicao.Admin);
			f.Class = "Normal";
			f.godki!.mastery = 0;
			GkRefazer(pl);
			pl.Combate.EmCombate = 0;
			pl.Forma.Entrar(Catalogo.IdBase);

			for (int i = 0; i < 60; i++) TickDoGodKi(60);   // uma hora na forma base
			AfirmarGk("fora de uma forma divina a maestria NAO sobe: uma hora na forma base, e ela continua em 0", f.godki.mastery == 0, $"{f.godki.mastery:0.####}%");

			pl.Forma.Entrar("ssj1");
			for (int i = 0; i < 60; i++) TickDoGodKi(60);
			AfirmarGk("...nem numa forma que nao usa ki divino (uma hora de Super Saiyajin comum)", f.godki.mastery == 0, $"{f.godki.mastery:0.####}%");

			MaestriaDivinaParadaDeTeste = true;
			try
			{
				pl.Forma.Entrar("ssg");
				for (int i = 0; i < 60; i++) TickDoGodKi(60);
				AfirmarGk("(defeito injetado: a maestria divina sem motor) uma hora de Super Saiyajin God REPROVA -- 0% pra sempre, "
						  + "e o Blue (33%) inalcancavel: o jogo de antes", f.godki.mastery == 0, $"{f.godki.mastery:0.####}%");
			}
			finally { MaestriaDivinaParadaDeTeste = false; }

			for (int i = 0; i < 60; i++) TickDoGodKi(60);
			AfirmarGk($"**USANDO O KI DIVINO ELA SOBE**: uma hora de Super Saiyajin God = {100 / horas:0.###}% ({horas:0} horas de uso pra 100%, como no DM)",
					  Math.Abs(f.godki.mastery - 100 / horas) < 1e-6, $"{f.godki.mastery:0.####}%");

			double antes = f.godki.mastery;
			pl.Combate.EntrarEmCombate();
			for (int i = 0; i < 60; i++) { pl.Combate.EntrarEmCombate(); TickDoGodKi(60); }
			AfirmarGk($"em COMBATE ela rende o dobro (`GODKI_MASTERY_COMBAT_MULT`): mais uma hora, mais {100 / horas * DespertarDivino.MaestriaEmCombate:0.###}%",
					  Math.Abs(f.godki.mastery - antes - 100 / horas * DespertarDivino.MaestriaEmCombate) < 1e-6, $"{f.godki.mastery:0.####}%");
			pl.Combate.EmCombate = 0;

			antes = f.godki.mastery;
			f.KO = true;
			for (int i = 0; i < 10; i++) TickDoGodKi(60);
			f.KO = false;
			AfirmarGk("nocauteado ninguem treina: dez minutos caido na forma, e a maestria nao anda", f.godki.mastery == antes);

			// ATE O FIM, contando os marcos.
			var ditos = new List<string>();
			int minutos = 0;
			while (f.godki.mastery < 100 && minutos < 3000)
			{
				List<string> falas = Ouvir(() => TickDoGodKi(60));
				ditos.AddRange(falas.Where(x => x.Contains("maestria", StringComparison.OrdinalIgnoreCase) || x.Contains("amadurece", StringComparison.Ordinal)));
				minutos++;
			}
			AfirmarGk("os QUATRO marcos (33, 50, 70 e 100%) sao ditos ao jogador, cada um uma vez",
					  ditos.Count == 4 && ditos[0].Contains("33%") && ditos[1].Contains("50%") && ditos[2].Contains("70%") && ditos[3].Contains("TOTAL"),
					  string.Join(" | ", ditos));
			double minutosEsperados = horas * 60 * (100 - antes) / 100;
			AfirmarGk($"e ela para em 100: o resto do caminho levou {minutos} minutos de uso (a conta: {minutosEsperados:0})",
					  f.godki.mastery == 100 && Math.Abs(minutos - minutosEsperados) <= 1, $"{minutos} min");
			AfirmarGk("o retrato do cliente leva a maestria em decimos: 1000", GkUltimoRetrato(pl) is { Maestria: 1000 }, GkRetratoEmTexto(pl));

			AfirmarGk("o perfil de formas le a maestria nova: 100, e o Blue (33%) deixou de ser recusado por ki divino",
					  Perfil(pl).GodKi == 100
					  && pl.Forma.Avaliar("blue", f.BP, 1, false, Perfil(pl)) != RecusaForma.SemGodKi);

			// "EM USO" e a forma que PEDE ki divino, ou a que ESCALA com ele pra origem certa.
			FormaDef? mistico = Catalogo.Def(Catalogo.IdMistico), ssg = Catalogo.Def("ssg"), ssj1 = Catalogo.Def("ssj1");
			AfirmarGk("a regra de \"em uso\": o Super Saiyajin God usa; o Super Saiyajin comum nao; o Mistico usa SO no Prodigial "
					  + "(e o caminho divino daquela linhagem)",
					  DespertarDivino.UsaOKiDivino(ssg, "Normal", "Saiyan") && !DespertarDivino.UsaOKiDivino(ssj1, "Normal", "Saiyan")
					  && DespertarDivino.UsaOKiDivino(mistico, Catalogo.ClasseProdigial, "") && !DespertarDivino.UsaOKiDivino(mistico, "Normal", "Saiyan")
					  && !DespertarDivino.UsaOKiDivino(null, "Normal", "Saiyan"));
		}
		finally
		{
			MaestriaDivinaParadaDeTeste = false;
			f.Class = classeGuardada;
			f.SaiyanLineage = linhagemGuardada;
			pl.Forma.Entrar(Catalogo.IdBase);
			pl.Combate.EmCombate = 0;
			f.KO = false;
		}
	}

	// =====================================================================
	// 7) O SAVE
	// =====================================================================
	private void GkOSave(ServerPlayer pl, ZoneKey zonaDivina, List<ServerPlayer> forjados)
	{
		GD.Print("[godki] -- 7) o save: a exposicao parcial e o despertar atravessam o disco e o login --");
		Fighter f = pl.Ficha;
		if (_store == null) { AfirmarGk("(montagem) o servidor tem pasta de contas", false); return; }
		double req = RequisitoDeGodKi(pl);
		Vec2 la = ChegadaDaZonaGerada(zonaDivina) ?? default;

		// ---------------------------------------------------------------- (a) NO MEIO DO CAMINHO
		GkVirarMortal(pl, req);
		MoveToZone(pl.Id, zonaDivina, la);
		for (int i = 0; i < 222; i++) TickDoGodKi(1);   // 37%
		double parcial = f.ExposicaoDivina;

		var conta = new AccountSave { Conta = ContaDaBancadaDoGodKi, CriadaEm = NowMs() };
		conta.Slots[0] = AccountStore.DeJogador(pl, NowMs());
		_store.Gravar(conta);
		CharacterSave? salvo = _store.Carregar(ContaDaBancadaDoGodKi)?.Slots[0];
		AfirmarGk($"a EXPOSICAO PARCIAL vai pro disco com a ficha: {parcial:0.##}%, e nenhum datum de ki divino",
				  salvo != null && Math.Abs(salvo.Ficha.ExposicaoDivina - parcial) < 1e-9 && parcial > 30 && salvo.Ficha.godki is not { awakened: true },
				  salvo == null ? "nao voltou do disco" : $"{salvo.Ficha.ExposicaoDivina:0.####}%");
		if (salvo == null) return;

		ServerPlayer relogado = Logar(ContaDaBancadaDoGodKi, 0, 0);
		forjados.Add(relogado);
		relogado.Pos = la;
		relogado.Peer = pl.Peer;   // o tique so anda pra quem tem dono na tela
		relogado.Slot = -1;        // ...e este corpo nao pode gravar por cima do slot do host
		AfirmarGk("quem LOGA de novo encontra o medidor onde deixou, e o requisito dele e o mesmo de antes do logout",
				  Math.Abs(relogado.Ficha.ExposicaoDivina - parcial) < 1e-9 && !DespertarDivino.JaDespertou(relogado.Ficha)
				  && RequisitoDeGodKi(relogado) == req,
				  $"{relogado.Ficha.ExposicaoDivina:0.####}% | requisito {RequisitoDeGodKi(relogado):N0} (era {req:N0})");

		double antes = relogado.Ficha.ExposicaoDivina;
		for (int i = 0; i < 60; i++) TickDoGodKi(1);
		AfirmarGk("...e o medidor continua de onde parou (mais um minuto no mundo divino)",
				  Math.Abs(relogado.Ficha.ExposicaoDivina - antes - DespertarDivino.PorSegundoNaAtmosfera * 60) < 1e-6,
				  $"{antes:0.##}% -> {relogado.Ficha.ExposicaoDivina:0.##}%");
		relogado.Peer = null;
		GkRecolher(relogado);
		forjados.Remove(relogado);

		// ---------------------------------------------------------------- (b) DEPOIS DE DESPERTO
		for (int i = 0; i < 2000 && !DespertarDivino.JaDespertou(f); i++) TickDoGodKi(1);
		AfirmarGk("(montagem) o host terminou de despertar no mundo divino", DespertarDivino.JaDespertou(f));
		if (f.godki == null) return;
		f.godki.mastery = 12.5;
		conta.Slots[0] = AccountStore.DeJogador(pl, NowMs());
		_store.Gravar(conta);

		ServerPlayer deus = Logar(ContaDaBancadaDoGodKi, 0, 0);
		forjados.Add(deus);
		deus.Pos = la;
		Fighter g = deus.Ficha;
		AfirmarGk("**O DESPERTAR ATRAVESSA O LOGIN**: `awakened`, a maestria e o marco voltam do disco",
				  g.godki is { awakened: true, mastery: 12.5 }
				  && g.bp_milestone_mult == f.bp_milestone_mult && g.bp_milestones_done.Contains(DespertarDivino.MarcoDoDespertar)
				  && g.ExposicaoDivina == DespertarDivino.ExposicaoCheia,
				  $"awakened={g.godki?.awakened} maestria={g.godki?.mastery:0.##} marco=x{g.bp_milestone_mult:0.##}");
		AfirmarGk("...e o resto do jogo continua enxergando: o perfil de formas e o fato do desafio",
				  Perfil(deus).GodKi == 12.5 && (FatosDe(deus) & Fatos.GodKiDesperto) != 0);
		double marcoDoDeus = g.bp_milestone_mult;
		AfirmarGk("...e ele NAO desperta de novo (nem o marco sobe)", !DespertarOGodKi(deus, FonteDaExposicao.Admin) && g.bp_milestone_mult == marcoDoDeus);
		GkRecolher(deus);
		forjados.Remove(deus);

		// ---------------------------------------------------------------- (c) O SAVE DE ANTES DE 2026-10-23, COM O BONUS GRAVADO
		// Ate a v0.4.4 o despertar gravava na ficha os 2,5x de poder pra sempre (`godki_give_mult`, o campo do DM). O campo saiu da ficha, e a
		// decisao e que quem ja despertou perca o bonus no login. A bancada escreve no disco o save como a v0.4.4 o gravava -- o mesmo JSON,
		// com o campo em 2,5 numa ficha de Saiyajin comum (o Primal nunca teve o bonus) -- e loga por ele, pelo funil de sempre. Duas reguas
		// numa: o poder dele e o do MESMO save sem o campo (o numero gravado nao conta) e o da mesma ficha sem ki divino nenhum (o despertar nao
		// conta).
		string arquivo = System.IO.Path.Combine(_store.Pasta, AccountStore.NomeDeArquivo(ContaDaBancadaDoGodKi) + ".json");
		System.Text.Json.Nodes.JsonNode? raiz = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(arquivo));
		if (raiz?["Slots"]?[0]?["Ficha"] is not System.Text.Json.Nodes.JsonObject fichaNoDisco)
		{
			AfirmarGk("(montagem) o save do host no disco tem a ficha no slot 0", false);
			return;
		}
		long t = NowMs();
		fichaNoDisco["SaiyanLineage"] = "Saiyan";
		fichaNoDisco.Remove("godki_give_mult");
		System.IO.File.WriteAllText(arquivo, raiz!.ToJsonString());
		ServerPlayer controle = Logar(ContaDaBancadaDoGodKi, 0, 0);
		forjados.Add(controle);
		controle.Pos = la;
		controle.Ficha.PowerLevel(agoraMs: t);
		double doMesmoSaveSemOCampo = controle.Ficha.expressedBP;
		GkRecolher(controle);
		forjados.Remove(controle);

		fichaNoDisco["godki_give_mult"] = f.godki_boost;   // o save da v0.4.4
		System.IO.File.WriteAllText(arquivo, raiz!.ToJsonString());
		bool campoNoDisco = System.IO.File.ReadAllText(arquivo).Contains("\"godki_give_mult\"", StringComparison.Ordinal);
		ServerPlayer antigo = Logar(ContaDaBancadaDoGodKi, 0, 0);
		forjados.Add(antigo);
		antigo.Pos = la;
		Fighter v = antigo.Ficha;
		v.PowerLevel(agoraMs: t);
		double comKiDivino = v.expressedBP, semKiDivino, comODefeito;
		GodKiState? kiDivinoDoSave = v.godki;
		v.godki = null;   // a mesma ficha sem ki divino nenhum
		v.PowerLevel(agoraMs: t);
		semKiDivino = v.expressedBP;
		v.godki = kiDivinoDoSave;
		Fighter.BonusDoGodKiPraSempreDeTeste = true;
		try
		{
			v.PowerLevel(agoraMs: t);
			comODefeito = v.expressedBP;
		}
		finally
		{
			Fighter.BonusDoGodKiPraSempreDeTeste = false;
			v.PowerLevel(agoraMs: t);
		}
		AfirmarGk("GKB3. **QUEM JA TINHA O BONUS O PERDE NO LOGIN** (2026-10-23): o save de antes, com os 2,5x gravados, entra desperto e com o "
				  + "poder do mesmo save sem o campo -- que e o poder de quem nao tem ki divino nenhum",
				  campoNoDisco && DespertarDivino.JaDespertou(v) && v.SaiyanLineage == "Saiyan"
				  && Math.Abs(comKiDivino / doMesmoSaveSemOCampo - 1) < 0.01 && Math.Abs(comKiDivino / semKiDivino - 1) < 0.01,
				  $"campo no disco={campoNoDisco}; linhagem '{v.SaiyanLineage}'; com o campo {comKiDivino:N0}, sem o campo {doMesmoSaveSemOCampo:N0}, "
				  + $"sem ki divino {semKiDivino:N0}");
		AfirmarGk($"   (defeito injetado: o bonus pra sempre de antes) a mesma regua REPROVA: o save entraria valendo x{f.godki_boost:0.#}",
				  Math.Abs(comODefeito / semKiDivino - f.godki_boost) < 0.02 * f.godki_boost, $"x{comODefeito / semKiDivino:0.###}");
		GkRecolher(antigo);
		forjados.Remove(antigo);
	}

	// =====================================================================
	// 8) OS VERBS DE ADMIN
	// =====================================================================
	private void GkOsVerbsDeAdmin(ServerPlayer pl)
	{
		GD.Print("[godki] -- 8) os verbs de admin: `admin_godki` e `admin_mundo_divino` --");
		Fighter f = pl.Ficha;
		double req = RequisitoDeGodKi(pl);

		// `admin_godki`: forca o despertar, ignorando o requisito -- pelo despacho de producao.
		GkVirarMortal(pl, req * 0.01);
		List<string> falas = Ouvir(() => Verbo(pl, "admin_godki", "0"));
		AfirmarGk("`admin_godki` desperta o proprio admin mesmo com 1% do requisito (o \"Force God Ki\" do DM), pelo funil de sempre",
				  DespertarDivino.JaDespertou(f) && Disse(falas, "O KI DIVINO DESPERTOU EM VOCÊ") && Disse(falas, "pela sua mão"), Ultimos(falas));
		falas = Ouvir(() => Verbo(pl, "admin_godki", "0"));
		AfirmarGk("...e de novo ele so avisa que ja esta desperto", Disse(falas, "já tem o ki divino desperto"), Ultimos(falas));

		// `admin_mundo_divino`: a orbita de um mundo divino -- e dali o POUSO DE VERDADE.
		GkVirarMortal(pl, req);
		ZoneKey terra = ZoneKey.Premade("Earth");
		MoveToZone(pl.Id, terra, PontoDeNascimento(terra));
		TickDoGodKi(1);
		falas = Ouvir(() => Verbo(pl, "admin_mundo_divino", ""));
		AfirmarGk("NO CHAO o `admin_mundo_divino` recusa (\"decole primeiro\"): como o `admin_estrela`, ele nao muda de zona sozinho",
				  pl.Zone.Equals(terra) && Disse(falas, "decole primeiro"), $"{pl.Zone} | {Ultimos(falas)}");

		Decolar(pl);
		AfirmarGk("(montagem) o host decolou da Terra pelo caminho de producao", Espaco.EhEspaco(pl.Zone), $"{pl.Zone}");
		pl.Ficha.GravMastered = 1;   // o corpo mais fragil que existe: o verb tem de procurar um mundo que ele aguente
		SistemaId deOnde = SistemaId.De(pl.Pos);
		falas = Ouvir(() => Verbo(pl, "admin_mundo_divino", ""));
		PlanetaNoEspaco? alvo = null;
		foreach (PlanetaNoEspaco p in Espaco.PorPerto(SeedDoUniverso, ChunkId.De(pl.Pos)))
			if (Math.Abs((p.Pos - pl.Pos).Length - (p.Raio + 90)) < 1 && AtmosferaDivina.Tem(SeedDoUniverso, p)) alvo = p;
		AfirmarGk("DO ESPACO ele larga o admin em ORBITA de um mundo de atmosfera divina (fora do disco: pousar e encostar)",
				  Espaco.EhEspaco(pl.Zone) && alvo != null && Disse(falas, "atmosfera"), $"{pl.Zone} | {Ultimos(falas)}");
		if (alvo is not { } mundo) return;

		// O MAIS PERTO QUE O CORPO AGUENTA: com maestria 1 so serve mundo de 1x; se a regiao nao tem nenhum, vai o
		// mais LEVE dela, com o aviso. Nos dois casos nao ha mundo divino mais leve que o escolhido.
		double g = MundoProcedural.GravidadeDaSeed(mundo.Seed);
		double maisLeve = AtmosferaDivina.NaRegiao(SeedDoUniverso, deOnde.Sx, deOnde.Sy, AtmosferaDivina.RaioGarantidoEmCelulas * 2)
										 .Where(m => !ZonaMorta(Espaco.ZonaDe(m.Corpo)))
										 .Min(m => MundoProcedural.GravidadeDaSeed(m.Corpo.Seed));
		AfirmarGk("...e escolhe pelo CORPO: o mais perto que a maestria de gravidade dele aguenta -- ou, nao havendo, o mais leve (com o aviso)",
				  g <= pl.Ficha.GravMastered ? !Disse(falas, "ATENÇÃO") : g <= maisLeve + 1e-9 && Disse(falas, "o mais leve"),
				  $"escolhido {g:0}x, o mais leve da regiao {maisLeve:0}x, maestria {pl.Ficha.GravMastered:0}x");

		pl.Pos = mundo.Pos;
		pl.ChunkAtual = ChunkId.De(pl.Pos);
		bool pousou = PousarDeVerdade(pl) > 0 && pl.Zone.Equals(Espaco.ZonaDe(mundo));
		falas = Ouvir(() => TickDoGodKi(1));
		AfirmarGk("**O AVISO AO POUSAR**: encostando no disco o corpo desce pelo pouso de producao, e no primeiro segundo no chao "
				  + "ouve que o mundo tem atmosfera divina e que ja a absorve",
				  pousou && Disse(falas, "ATMOSFERA DIVINA") && Disse(falas, "começa a absorvê-lo") && f.ExposicaoDivina > 0,
				  $"pousou={pousou} em {pl.Zone} | {Ultimos(falas)}");
	}

	// =====================================================================
	// A SUPERFICIE DA BANCADA DE TELA (`--diaggodki`)
	// =====================================================================
	// O robo do cliente (`Client/RoboDoGodKi.cs`) roda no mesmo processo (`--host`). Ele viaja, pousa e aperta
	// os verbs pelo fio; o que ele pede daqui e so o que o jogo cobra CARO -- o BP base de um deus -- e a
	// resposta certa do servidor, pra comparar com o que a tela desenhou.

	/// <summary>
	/// A COBAIA DA BANCADA DE TELA: um mortal sem medidor, de pe, com o BP base em <paramref name="fatorDoRequisito"/>
	/// vezes o requisito pessoal dele -- e aguentando o chao de qualquer mundo (o divino mais perto pode puxar
	/// dezenas de vezes, e um novato esmagado mediria o Outro Mundo).
	/// </summary>
	internal bool CobaiaDoGodKiDeTeste(int id, double fatorDoRequisito)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.GravMastered = Math.Max(pl.Ficha.GravMastered, 200);
		GkVirarMortal(pl, RequisitoDeGodKi(pl) * fatorDoRequisito);
		pl.Combate.SincronizarVida();
		AplicarGravidade(pl);
		MandarFicha(pl);
		return true;
	}

	/// <summary>So o BP base da cobaia muda, em multiplos do requisito pessoal dela. O resto (medidor, sessao) fica.</summary>
	internal bool BpBaseDoGodKiDeTeste(int id, double fatorDoRequisito)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.BP = RequisitoDeGodKi(pl) * fatorDoRequisito;
		pl.Ficha.Tick(agoraMs: NowMs());
		return true;
	}

	/// <summary>O QUE O SERVIDOR SABE do ki divino deste corpo -- a resposta certa com que a tela e comparada.</summary>
	internal (double Exposicao, bool Desperto, double Maestria, bool NaAtmosfera, bool Alcanca)? KiDivinoDeTeste(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (pl.Ficha.ExposicaoDivina, DespertarDivino.JaDespertou(pl.Ficha), pl.Ficha.godki?.mastery ?? 0,
			   EmAtmosferaDivina(pl), AlcancaOKiDivino(pl))
			: null;

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>O host (ou um forjado) volta a ser um mortal sem medidor, com este BP base e a sessao zerada.</summary>
	private void GkVirarMortal(ServerPlayer c, double bp)
	{
		Fighter f = c.Ficha;
		f.godki = null;
		f.ExposicaoDivina = 0;
		f.bp_milestones_done.Remove(DespertarDivino.MarcoDoDespertar);
		f.bp_milestone_mult = 1;
		f.BP = bp;
		f.dead = false;
		f.KO = false;
		c.Forma.Entrar(Catalogo.IdBase);
		c.CenaSegundos = 0;
		AplicarForma(c);
		f.Tick(agoraMs: NowMs());
		GkRefazer(c);
		_godKiDe.Remove(c.Id);
	}

	/// <summary>Zera o que a familia 5 mede entre uma cena e outra: o medidor, a sessao (teto e frases) e a caderneta de golpes.</summary>
	private void GkZerarALuta(ServerPlayer c, bool manterAExposicao = false)
	{
		if (!manterAExposicao) c.Ficha.ExposicaoDivina = 0;
		_godKiDe.Remove(c.Id);
		_golpesComUmDeus.Clear();
	}

	/// <summary>Corpo inteiro, de pe, com Ki e folego -- antes de cada golpe, pra a familia 5 medir a regra e nao o cansaco.</summary>
	private static void GkRefazer(ServerPlayer c)
	{
		c.Combate.Curar();
		c.Ficha.KO = false;
		c.Ficha.Ki = c.Ficha.MaxKi;
		c.Ficha.stamina = c.Ficha.maxstamina;
		c.Combate.Recarga = 0;
		c.Combate.Stun = 0;
		c.TiquesDeVoo = 0;
	}

	/// <summary>
	/// O PODER EXPRESSO DE <paramref name="c"/> VIRA O DE <paramref name="igualA"/>, mexendo no BP base dele. A pontaria
	/// do soco sai da razao de poder: com um lado muito acima do outro, "o golpe encostou" viraria loteria.
	/// </summary>
	private static void GkIgualarOPoder(ServerPlayer c, ServerPlayer igualA)
	{
		for (int i = 0; i < 3; i++)
		{
			c.Ficha.Tick(agoraMs: NowMs());
			igualA.Ficha.Tick(agoraMs: NowMs());
			if (c.Ficha.expressedBP <= 0) return;
			c.Ficha.BP *= igualA.Ficha.expressedBP / c.Ficha.expressedBP;
		}
		c.Ficha.Tick(agoraMs: NowMs());
	}

	/// <summary>
	/// UM SOCO DE VERDADE, pela funcao do `C2S.Action`, repetido ate ENCOSTAR (o resolvedor sorteia pontaria e
	/// esquiva). Quem bate fica no ringue e o alvo um passo a leste: os dois trocam de lugar quando trocam de papel.
	/// </summary>
	private bool GkSoco(ServerPlayer quem, ServerPlayer alvo, Vec2 ringue, bool curar = true)
	{
		for (int tentativa = 0; tentativa < 80; tentativa++)
		{
			if (curar) { GkRefazer(quem); GkRefazer(alvo); }
			else { quem.Combate.Recarga = 0; quem.Combate.Stun = 0; quem.TiquesDeVoo = 0; alvo.TiquesDeVoo = 0; }
			quem.Pos = ringue;
			alvo.Pos = ringue + new Vec2(30, 0);
			quem.Facing = Facing.East;
			alvo.Facing = Facing.West;
			quem.AlvoId = alvo.Id;
			alvo.Combate.Bloqueando = false;

			int combo = quem.Combate.Combo;
			Atacar(quem, Protocol.Golpe.Leve);
			if (quem.Combate.Combo > combo) return true;
		}
		return false;
	}

	/// <summary>Uma TROCA: um golpe certo de cada lado.</summary>
	private void GkTroca(ServerPlayer a, ServerPlayer b, Vec2 ringue)
	{
		GkSoco(a, b, ringue);
		GkSoco(b, a, ringue);
	}

	/// <summary>O ultimo `S2C.GodKi` que saiu pra este jogador, ou nulo.</summary>
	private static (byte Bits, byte Pct, ushort Maestria)? GkUltimoRetrato(ServerPlayer pl)
	{
		if (EscutaDoGodKi == null) return null;
		for (int i = EscutaDoGodKi.Count - 1; i >= 0; i--)
			if (EscutaDoGodKi[i].Quem == pl.Id) return (EscutaDoGodKi[i].Bits, EscutaDoGodKi[i].Pct, EscutaDoGodKi[i].Maestria);
		return null;
	}

	private static string GkRetratoEmTexto(ServerPlayer pl) =>
		GkUltimoRetrato(pl) is { } r ? $"bits {(Protocol.BitsDoGodKi)r.Bits}, {r.Pct}%, maestria {r.Maestria / 10.0:0.#}%" : "nenhum pacote saiu";

	/// <summary>Um corpo sem dono, posto no mundo pela porta de sempre (`PorNoMundo`).</summary>
	private ServerPlayer GkForjar(string nome, string raca, ZoneKey zona, Vec2 onde, double bp, List<ServerPlayer> forjados)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDoGodKiDeTeste + _gkCorpos++,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = zona,
			Pos = onde,
			Conta = "",
			Slot = -1,
			LastInputMs = NowMs(),
			Ficha = new Fighter { Race = raca, BP = bp },
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Knockback = false,
		};
		novo.Ficha.Class = "Normal";
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		forjados.Add(novo);
		return novo;
	}

	/// <summary>
	/// Leva um corpo FORJADO pra outra zona. A tela emprestada sai antes: com ela, o `MoveToZone` mandaria a troca
	/// de zona DELE pro cliente do host.
	/// </summary>
	private void GkLevar(ServerPlayer corpo, ZoneKey zona, Vec2 onde)
	{
		LiteNetLib.NetPeer? tela = corpo.Peer;
		corpo.Peer = null;
		MoveToZone(corpo.Id, zona, onde);
		corpo.Peer = tela;
	}

	/// <summary>Tira um corpo da bancada do mundo. O NPC sai pela porta dele; a tela emprestada e devolvida antes.</summary>
	private void GkRecolher(ServerPlayer corpo)
	{
		corpo.Peer = null;
		_godKiDe.Remove(corpo.Id);
		if (corpo.Papel != null) { RemoverNpc(corpo); return; }
		_npcsPraTirar.Remove(corpo);
		EsquecerTecnicas(corpo.Id);
		_players.Remove(corpo.Id);
		ZoneList(corpo.Zone.Hash).Remove(corpo);
	}

	/// <summary>
	/// A conta do relog e gravada de verdade; ela sai no fim, mesmo se a bancada estourar. (A bancada dos marcos de
	/// forma, `--marcosdeformateste`, usa a mesma faxina com a conta dela.)
	/// </summary>
	private void GkApagarAConta(string conta = ContaDaBancadaDoGodKi)
	{
		if (_store == null) return;
		try
		{
			string caminho = System.IO.Path.Combine(_store.Pasta, AccountStore.NomeDeArquivo(conta) + ".json");
			if (System.IO.File.Exists(caminho)) System.IO.File.Delete(caminho);
		}
		catch (Exception e) { GD.PushWarning($"[godki] nao consegui apagar a conta de bancada: {e.Message}"); }
	}
}
