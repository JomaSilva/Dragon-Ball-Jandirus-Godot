using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DOS MARCOS DE GANHO DAS FORMAS (`--marcosdeformateste`) -- o pedido do dono de 2026-10-11:
///
///   *"sim pode ligar, mas lembre q USSJ n existe mais, oq tem e ssj grade 2 e ssj grade 3 no lugar, logo
///   esse 2,5x teria q distribuir entre eles"* -- e, sobre a divisao: *"pode ser essa distribuicao"*.
///
/// COM O HOST DE VERDADE (um Saiyajin, de classe sorteada) e corpos forjados -- que levam a TELA DO HOST
/// emprestada, porque o marco de forma e so de quem tem dono. Nada aqui recalcula a regra:
/// quem paga o marco e o `Treinar` de producao -- o laco de fichas, com a linha que chama a conferencia --,
/// as formas entram pelo `Transformar`/`TransformarPara` (a tecla C e a tecla de forma) ou pelo
/// `EstadoDeForma.Entrar` que todos os caminhos usam, e o login e o de verdade: a ficha vai ao disco e volta
/// pelo `RestaurarFormaEDisciplina`.
///
/// ============================ AS OITO FAMILIAS ============================
///   1. A TABELA -- o `ussj` saiu, os dois graus entraram (x2,25 e x2,5, escada crescente), o resto e o do
///      DM numero por numero, e TODA etiqueta tem dono: uma forma, outro sistema, ou "nao existe neste port".
///   2. O CATALOGO -- as 22 formas que concedem marco e a etiqueta de cada uma, contra a lista levantada no DM.
///   3. A ESTREIA -- forma por forma: entrar nela e passar um tique de fichas poe o ganho no valor da tabela;
///      as formas SEM marco nao mexem em nada.
///   4. O GESTO DE VERDADE -- o host aperta C, sobe o primeiro degrau da linha dele e ouve o marco; a
///      conferencia e idempotente; o ganho de treino multiplica de fato.
///   5. NAO EMPILHA, E OS DOIS GRAUS -- a escada inteira do Super Saiyajin pela tecla de forma (x2, x2,25,
///      x2,5, x3, x4: nunca o produto), a ordem trocada dos graus, e quem ja tem o ki divino.
///   6. QUEM NAO TEM A FORMA -- o Humano que aperta C, o Saiyajin comum que pede a forma do Lendario, e o
///      corpo SEM DONO (o NPC, que nasce com a escada na biografia).
///   7. O RETROATIVO NO LOGIN -- o save de antes dos marcos (o novo e o de antes dos dois bits), o segundo
///      login que nao repete, e o marco maior que nao desce.
///   8. OS CAMINHOS FORA DA TECLA -- a queda do Oozaru Dourado em SSJ4 (sem estreia) e o `admin_forma`.
/// ==========================================================================
///
/// DOIS DEFEITOS INJETADOS: `MarcosDeFormaDesligadosDeTeste` (ninguem confere: o jogo de antes) e, no Core,
/// `MarcosDeForma.SoAFormaDeAgoraDeTeste` (as estreias antigas nao contam: o retroativo morre). O "nao
/// empilha" e regra antiga do `Fighter.ReachMilestone`, que este trabalho nao tocou: a prova dele e o numero
/// exato contra o produto.
///
///     Godot --headless --path . --host --rede 8255 --marcosdeformateste --raca Saiyan
///                      --conta bancada_marcos --nome Marco
/// </summary>
public partial class GameServer
{
	/// <summary>A flag da bancada, lida uma vez.</summary>
	private static readonly bool MarcosDeFormaDeTeste = Array.IndexOf(OS.GetCmdlineArgs(), "--marcosdeformateste") >= 0;

	private bool _marcosDeFormaTestou;

	/// <summary>A conta do "relog" da familia 7. Gravada de verdade (na pasta desviada da bancada) e apagada no fim.</summary>
	private const string ContaDaBancadaDosMarcos = "bancada_marcos_relog";

	private int _mfOk, _mfFalhou;

	/// <summary>
	/// O `BP_MILESTONES` DO DM (`Stats/BP/LinearGain.dm:19-26`), menos o `ussj` -- copiado a mao DE PROPOSITO:
	/// e a regua contra a qual a tabela do port e conferida. O dono mandou nao mexer em nenhum outro numero.
	/// </summary>
	private static readonly Dictionary<string, double> TabelaDoDmSemOUssj = new(StringComparer.Ordinal)
	{
		["ssj"] = 2, ["ssj2"] = 3, ["ssj3"] = 4, ["ssj4"] = 5, ["ssj4fp"] = 6,
		["lssj1"] = 2, ["lssj2"] = 4, ["lssj3"] = 6, ["lssj4"] = 6,
		["bio1"] = 1.5, ["bio2"] = 2, ["bio3"] = 3, ["bio_ssj2"] = 4,
		["majin2"] = 2, ["majin3"] = 3,
		["frost_final"] = 2, ["frost_golden"] = 3,
		["asc5"] = 2, ["asc10"] = 3, ["asc20"] = 4,
		["potential"] = 1.5, ["godki"] = 5, ["blue"] = 6,
	};

	/// <summary>
	/// QUEM CONCEDE CADA FORMA, levantado no DM (o `arquivo.dm:linha` de cada uma esta na entrada do catalogo).
	/// Copiado a mao tambem: se o catalogo mudar uma etiqueta, tem de ser de proposito.
	/// </summary>
	private static readonly Dictionary<string, string> MarcoEsperadoDaForma = new(StringComparer.Ordinal)
	{
		["ssj1"] = "ssj", ["grade2"] = "ssjg2", ["grade3"] = "ssjg3", ["ssj2"] = "ssj2", ["ssj3"] = "ssj3",
		["ssj4"] = "ssj4", ["ssj4_full_power"] = "ssj4fp",
		["future_ssj"] = "ssj",
		[Catalogo.IdWrathful] = "lssj1", ["c_type"] = "lssj2", ["legendary"] = "lssj3",
		["primal_c_type"] = "ssj", ["primal_legendary"] = "ssj2", ["primal_legendary2"] = "ssj3",
		["primal_legendary3"] = "ssj3", ["primal_legendary4"] = "ssj4", ["primal_legendary4_full_power"] = "ssj4fp",
		["blue"] = "blue", ["blue_evolution"] = "blue", ["rose"] = "blue", ["rose2"] = "blue",
		["frost7"] = "frost_final",
	};

	/// <summary>As etiquetas que OUTRO sistema concede (e onde), e as que nada concede neste port (e por que).</summary>
	private static readonly string[] MarcosDeOutroSistema = ["bio1", "bio2", "bio3", "bio_ssj2", "asc5", "asc10", "asc20", "potential", "godki"];
	private static readonly string[] MarcosSemDonoNoPort = ["lssj4", "majin2", "majin3", "frost_golden"];

	/// <summary>Chamado do `TickDoGodKi` (fora do laco de fichas): roda UMA vez, no primeiro tique em que ha um jogador de verdade.</summary>
	private void DispararABancadaDosMarcosDeForma()
	{
		if (_marcosDeFormaTestou || Jogadores.FirstOrDefault() is not { } host) return;
		_marcosDeFormaTestou = true;
		RodarBancadaDosMarcosDeForma(host);
	}

	private void AfirmarMf(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mfOk++; GD.Print($"[marcos]   OK    {oque}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); return; }
		_mfFalhou++;
		GD.PrintErr($"[marcos]   FALHA {oque}   [{detalhe}]");
	}

	private void RodarBancadaDosMarcosDeForma(ServerPlayer pl)
	{
		_mfOk = _mfFalhou = 0;
		GD.Print("[marcos] ================ OS MARCOS DE GANHO DAS FORMAS (dono, 2026-10-11) ================");

		// A FOTO DO HOST. Ele sobe de forma, muda de poder e perde os marcos aqui dentro; tudo volta no `finally`.
		Fighter f = pl.Ficha;
		double bpGuardado = f.BP, kiGuardado = f.Ki, marcoGuardado = f.bp_milestone_mult;
		var marcosGuardados = new HashSet<string>(f.bp_milestones_done, StringComparer.Ordinal);
		string formaGuardada = pl.Forma.Atual;
		var liberadasGuardadas = new HashSet<int>(pl.Forma.Liberadas);
		var estreiasGuardadas = new HashSet<int>(pl.Forma.EstreiaVista);
		var forjados = new List<ServerPlayer>();

		try
		{
			AfirmarMf("PRECONDICAO: o host tem dono na tela, e Saiyajin e e admin (a familia 8 usa o `admin_forma`)",
					  EhJogador(pl) && EhAdmin(pl) && TemEscada(pl), $"jogador={EhJogador(pl)} admin={EhAdmin(pl)} classe={f.Class} linhagem={f.SaiyanLineage}");
			if (f.dead || f.KO) pl.Combate.Reviver();

			MfATabela();
			MfOCatalogo();
			MfAEstreia(pl, forjados);
			MfOGestoDeVerdade(pl);
			MfNaoEmpilha(pl, forjados);
			MfQuemNaoTemAForma(pl, forjados);
			MfORetroativo(pl, forjados);
			MfOsCaminhosForaDaTecla(pl, forjados);

			AfirmarMf("a bancada chegou ao fim (sem esta linha, abortar no meio reportaria '0 falhas')", true);
		}
		catch (Exception e)
		{
			AfirmarMf($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			MarcosDeFormaDesligadosDeTeste = false;
			MarcosDeForma.SoAFormaDeAgoraDeTeste = false;

			try
			{
				foreach (ServerPlayer corpo in forjados) GkRecolher(corpo);
				GkApagarAConta(ContaDaBancadaDosMarcos);

				// O HOST VOLTA COMO ESTAVA: a ficha, os marcos e a forma.
				f.BP = bpGuardado;
				f.bp_milestone_mult = marcoGuardado;
				f.bp_milestones_done = marcosGuardados;
				string deOnde = pl.Forma.Atual;
				pl.Forma.Atual = formaGuardada;
				pl.Forma.Liberadas = liberadasGuardadas;
				pl.Forma.EstreiaVista = estreiasGuardadas;
				AplicarForma(pl);
				if (deOnde != formaGuardada) AnunciarForma(pl, deOnde, formaGuardada, estreia: false, semCena: true);   // as telas voltam junto
				pl.CenaSegundos = 0;
				f.Tick(agoraMs: NowMs());
				f.Ki = Math.Min(kiGuardado, f.MaxKi);
			}
			finally
			{
				GD.Print($"[marcos] ================ {_mfOk} OK, {_mfFalhou} FALHA(S) ================");
			}
		}
	}

	// =====================================================================
	// 1) A TABELA
	// =====================================================================
	private void MfATabela()
	{
		GD.Print("[marcos] -- 1) a tabela: o `ussj` saiu, os dois graus entraram, e o resto e o do DM --");
		Dictionary<string, double> t = Milestones.Tabela;

		AfirmarMf("o `ussj` SAIU da tabela (o Ultra Super Saiyajin nao existe mais: ha os dois graus)", !t.ContainsKey("ussj"));
		AfirmarMf("os dois graus valem o que o dono confirmou: Grau 2 x2,25 e Grau 3 x2,5 (o topo dos graus e o que o USSJ valia)",
				  Milestones.Valor(Milestones.SsjGrau2) == 2.25 && Milestones.Valor(Milestones.SsjGrau3) == 2.5,
				  $"grau 2 x{Milestones.Valor(Milestones.SsjGrau2)}, grau 3 x{Milestones.Valor(Milestones.SsjGrau3)}");
		AfirmarMf("a escada continua CRESCENTE: Super Saiyajin x2 < Grau 2 < Grau 3 < Super Saiyajin 2 x3",
				  Milestones.Valor(Milestones.Ssj) == 2 && Milestones.Valor(Milestones.Ssj) < Milestones.Valor(Milestones.SsjGrau2)
				  && Milestones.Valor(Milestones.SsjGrau2) < Milestones.Valor(Milestones.SsjGrau3)
				  && Milestones.Valor(Milestones.SsjGrau3) < Milestones.Valor(Milestones.Ssj2) && Milestones.Valor(Milestones.Ssj2) == 3);

		var semGraus = t.Where(par => par.Key != Milestones.SsjGrau2 && par.Key != Milestones.SsjGrau3).ToDictionary(par => par.Key, par => par.Value);
		List<string> diferentes = [.. TabelaDoDmSemOUssj.Where(par => !semGraus.TryGetValue(par.Key, out double v) || v != par.Value).Select(par => par.Key),
								   .. semGraus.Keys.Where(k => !TabelaDoDmSemOUssj.ContainsKey(k))];
		AfirmarMf($"NENHUM OUTRO NUMERO MUDOU: as {TabelaDoDmSemOUssj.Count} etiquetas que sobram sao as do `BP_MILESTONES` do DM, valor por valor",
				  diferentes.Count == 0, diferentes.Count == 0 ? "" : "diferem: " + string.Join(", ", diferentes));

		// TODA ETIQUETA TEM DONO. E a classe de defeito que este trabalho veio consertar: regra na tabela, e ninguem pra liga-la.
		var deForma = new HashSet<string>(MarcosDeForma.ComMarco.Select(d => d.Marco), StringComparer.Ordinal);
		List<string> orfas = [.. t.Keys.Where(k => !deForma.Contains(k) && !MarcosDeOutroSistema.Contains(k) && !MarcosSemDonoNoPort.Contains(k))];
		List<string> emDois = [.. t.Keys.Where(k => (deForma.Contains(k) ? 1 : 0) + (MarcosDeOutroSistema.Contains(k) ? 1 : 0) + (MarcosSemDonoNoPort.Contains(k) ? 1 : 0) > 1)];
		AfirmarMf($"**TODA ETIQUETA DA TABELA TEM DONO**: {deForma.Count} saem de uma forma do catalogo, {MarcosDeOutroSistema.Length} de outro sistema "
				  + $"(bio, Ascensao, potencial, ki divino) e {MarcosSemDonoNoPort.Length} nao tem o que as conceda neste port ({string.Join(", ", MarcosSemDonoNoPort)})",
				  orfas.Count == 0 && emDois.Count == 0 && deForma.Count + MarcosDeOutroSistema.Length + MarcosSemDonoNoPort.Length == t.Count,
				  $"orfas: {string.Join(", ", orfas)} | em dois grupos: {string.Join(", ", emDois)} | tabela {t.Count}");
		AfirmarMf("...e as que ficaram sem dono continuam valendo zero na pratica: nenhuma forma do catalogo as cita",
				  !MarcosSemDonoNoPort.Any(deForma.Contains) && MarcosSemDonoNoPort.All(t.ContainsKey));
	}

	// =====================================================================
	// 2) O CATALOGO
	// =====================================================================
	private void MfOCatalogo()
	{
		GD.Print("[marcos] -- 2) o catalogo: quais formas concedem marco, e qual --");
		Dictionary<string, string> doCatalogo = MarcosDeForma.ComMarco.ToDictionary(d => d.Id, d => d.Marco, StringComparer.Ordinal);

		List<string> trocadas = [.. MarcoEsperadoDaForma.Where(par => doCatalogo.GetValueOrDefault(par.Key) != par.Value)
														.Select(par => $"{par.Key}: esperado {par.Value}, catalogo '{doCatalogo.GetValueOrDefault(par.Key)}'")];
		List<string> aMais = [.. doCatalogo.Keys.Where(id => !MarcoEsperadoDaForma.ContainsKey(id))];
		AfirmarMf($"as {MarcoEsperadoDaForma.Count} formas levantadas no DM carregam a etiqueta certa, e nenhuma outra forma do catalogo concede marco",
				  trocadas.Count == 0 && aMais.Count == 0,
				  $"trocadas: {string.Join("; ", trocadas)} | a mais: {string.Join(", ", aMais)}");
		AfirmarMf("toda etiqueta citada pelo catalogo EXISTE na tabela e vale mais que x1 (etiqueta desconhecida nao daria erro: daria zero, calada)",
				  MarcosDeForma.ComMarco.All(d => Milestones.Valor(d.Marco) > 1),
				  string.Join(", ", MarcosDeForma.ComMarco.Where(d => Milestones.Valor(d.Marco) <= 1).Select(d => $"{d.Id}:{d.Marco}")));
		AfirmarMf("os dois graus sao etiquetas DIFERENTES (no DM era uma so, a `ussj`), e o Super Saiyajin God nao leva a `blue` (ele e ki divino sem SSJ)",
				  doCatalogo.GetValueOrDefault("grade2") == Milestones.SsjGrau2 && doCatalogo.GetValueOrDefault("grade3") == Milestones.SsjGrau3
				  && !doCatalogo.ContainsKey("ssg") && !doCatalogo.ContainsKey("rose_ssg"));
	}

	// =====================================================================
	// 3) A ESTREIA, FORMA POR FORMA
	// =====================================================================
	private void MfAEstreia(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[marcos] -- 3) a estreia: entrar na forma e passar um tique de fichas poe o ganho no valor da tabela --");

		// UM CORPO NOVO POR FORMA, pelo `EstadoDeForma.Entrar` -- a porta que todo caminho de transformacao usa --,
		// e o tique e o `Treinar` de producao. (Sem `AplicarForma`: o marco nao le o multiplicador da forma, e um
		// Saiyajin forjado nao tem os corpos de um Frost Demon pra vestir.)
		foreach (FormaDef d in MarcosDeForma.ComMarco)
		{
			ServerPlayer c = MfForjarSaiyajin($"Estreia_{d.Id}", pl, forjados);
			double antes = c.Ficha.BpGainBase();
			c.Forma.Entrar(d.Id);
			List<string> falas = Ouvir(() => Treinar(c));
			double valor = Milestones.Valor(d.Marco);
			AfirmarMf($"{d.Nome} (`{d.Id}`): a estreia poe o ganho em x{valor:0.##} (`{d.Marco}`), a base de ganho multiplica junto e o corpo ouve o marco",
					  c.Ficha.bp_milestone_mult == valor && c.Ficha.bp_milestones_done.Contains(d.Marco)
					  && Math.Abs(c.Ficha.BpGainBase() / antes - valor) < 1e-9 && Disse(falas, "MARCO DE PODER") && Disse(falas, "rompeu um novo patamar"),
					  $"ganho x{c.Ficha.bp_milestone_mult:0.##} | {Ultimos(falas)}");
			GkRecolher(c);
			forjados.Remove(c);
		}

		// AS QUE NAO CONCEDEM NADA. O Oozaru fica de fora: ele nao e degrau da escada (`Catalogo.NaoSeSobePraEla`) --
		// entrar nele e o `VirarFera`, que nem passa pelo `Entrar`.
		var mexeram = new List<string>();
		int semMarco = 0;
		foreach (FormaDef d in Catalogo.Todas)
		{
			if (d.Marco.Length > 0 || d.Id == Catalogo.IdBase || d.Linha == LinhaDeForma.Oozaru) continue;
			semMarco++;
			ServerPlayer c = MfForjarSaiyajin($"SemMarco_{d.Id}", pl, forjados);
			c.Forma.Entrar(d.Id);
			List<string> falas = Ouvir(() => Treinar(c));
			if (c.Ficha.bp_milestone_mult != 1 || c.Ficha.bp_milestones_done.Count != 0 || Disse(falas, "MARCO DE PODER")) mexeram.Add(d.Id);
			GkRecolher(c);
			forjados.Remove(c);
		}
		AfirmarMf($"as {semMarco} formas SEM marco (God, os Limit Breaker, Mistico, Beast, disciplinas, Frost 1 a 6, Namek, Heran, Alien, Super Perfeita) nao mexem no ganho",
				  semMarco > 0 && mexeram.Count == 0, "mexeram: " + string.Join(", ", mexeram));
	}

	// =====================================================================
	// 4) O GESTO DE VERDADE -- o host aperta C
	// =====================================================================
	private void MfOGestoDeVerdade(ServerPlayer pl)
	{
		GD.Print("[marcos] -- 4) o gesto de verdade: o host aperta C e sobe o primeiro degrau da linha dele --");
		Fighter f = pl.Ficha;
		MfZerarOHost(pl);

		// O PRIMEIRO DEGRAU DA LINHA DESTE HOST (a classe e sorteada na criacao: comum, Lendario ou Primal). E o
		// primeiro que so esbarra na RAIVA -- e a raiva e paga uma vez: a forma ja LIBERADA (o estado de quem a
		// recebeu de um mestre) entra pelo C sem ela, e a estreia continua sendo esta.
		PerfilDeFormas perfil = Perfil(pl);
		FormaDef? alvo = null;
		foreach (FormaDef d in MarcosDeForma.ComMarco)
		{
			RecusaForma r = pl.Forma.Avaliar(d.Id, f.BP, kiFracao: 1, caido: false, perfil);
			if (r is RecusaForma.Pode or RecusaForma.SemFuria) { alvo = d; break; }
		}
		AfirmarMf("(montagem) a linha do host tem um primeiro degrau com marco ao alcance do C",
				  alvo != null, $"classe={f.Class} linhagem={f.SaiyanLineage}");
		if (alvo == null) return;
		pl.Forma.Liberar(alvo.Id);

		double base1 = f.BpGainBase();
		List<string> subida = Ouvir(() => Transformar(pl, subir: true));
		pl.CenaSegundos = 0;
		AfirmarMf($"o host aperta C e DESPERTA {alvo.Nome} pelo `Transformar` de producao (a estreia)",
				  pl.Forma.Atual == alvo.Id && pl.Forma.JaViuAEstreia(alvo.Id) && Disse(subida, "VOCE DESPERTA"), $"forma {pl.Forma.Atual} | {Ultimos(subida)}");
		if (pl.Forma.Atual != alvo.Id) return;

		double valor = Milestones.Valor(alvo.Marco);
		MarcosDeFormaDesligadosDeTeste = true;
		try
		{
			Treinar(pl);
			AfirmarMf("(defeito injetado: ninguem confere os marcos) transformado e com o tique passando, o ganho continua x1 -- o jogo de antes",
					  f.bp_milestone_mult == 1 && !f.bp_milestones_done.Contains(alvo.Marco), $"ganho x{f.bp_milestone_mult:0.##}");
		}
		finally { MarcosDeFormaDesligadosDeTeste = false; }

		List<string> falas = Ouvir(() => Treinar(pl));
		AfirmarMf($"**O MARCO VEM NO TIQUE DE FICHAS SEGUINTE**: o ganho do host vai a x{valor:0.##} (`{alvo.Marco}`) e ele le a frase da estreia",
				  f.bp_milestone_mult == valor && f.bp_milestones_done.Contains(alvo.Marco)
				  && Disse(falas, "MARCO DE PODER") && Disse(falas, "rompeu um novo patamar") && Disse(falas, $"x{valor:0.##}"),
				  $"ganho x{f.bp_milestone_mult:0.##} | {Ultimos(falas)}");
		AfirmarMf("...e o ganho de TREINO multiplica de verdade: a base de ganho (`BpGainBase`) cresceu na mesma razao",
				  Math.Abs(f.BpGainBase() / base1 - valor) < 1e-9, $"{base1:0.##} -> {f.BpGainBase():0.##}");

		falas = Ouvir(() => { Treinar(pl); Treinar(pl); Treinar(pl); });
		AfirmarMf("a conferencia e idempotente: mais tres tiques na mesma forma nao repetem a frase nem mexem no ganho",
				  f.bp_milestone_mult == valor && !Disse(falas, "MARCO DE PODER"), Ultimos(falas));

		List<string> descida = Ouvir(() => Transformar(pl, subir: false));
		pl.CenaSegundos = 0;
		falas = Ouvir(() => Treinar(pl));
		AfirmarMf("voltar ao normal NAO devolve o marco: ele e do personagem, nao da forma que esta vestida",
				  pl.Forma.Atual != alvo.Id && f.bp_milestone_mult == valor && !Disse(falas, "MARCO DE PODER"),
				  $"forma {pl.Forma.Atual} | ganho x{f.bp_milestone_mult:0.##} | {Ultimos(descida)}");
	}

	// =====================================================================
	// 5) NAO EMPILHA, E OS DOIS GRAUS
	// =====================================================================
	private void MfNaoEmpilha(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[marcos] -- 5) nao empilha, e os dois graus: a escada do Super Saiyajin pela tecla de forma --");

		// UM SAIYAJIN COMUM, com o que a escada cobra: poder de sobra, a maestria que abre os graus (50% e 70% do
		// SSJ1) e o SSJ3 (50% do SSJ2), e as duas formas que nascem da raiva ja liberadas. Cada degrau entra pelo
		// `TransformarPara` -- a tecla de forma, com o `Avaliar` inteiro na frente.
		ServerPlayer sj = MfForjarSaiyajin("EscadaDaBancada", pl, forjados);
		sj.Forma.GradesLigados = true;
		sj.Forma.Liberar("ssj1");
		sj.Forma.Liberar("ssj2");
		sj.Forma.Maestria.Por("ssj1", 80);
		sj.Forma.Maestria.Por("ssj2", 60);

		(string Id, double Ganho)[] escada =
			[("ssj1", 2), ("grade2", Milestones.ValorDoGrau2), ("grade3", Milestones.ValorDoGrau3), ("ssj2", 3), ("ssj3", 4)];
		double produto = 1;
		foreach ((string id, double ganho) in escada)
		{
			produto *= ganho;
			(bool entrou, List<string> gesto, List<string> tique) = MfPedirAForma(sj, id);
			AfirmarMf($"{Catalogo.Def(id)?.Nome}: a primeira vez NELE poe o ganho em x{ganho:0.##} -- o maior marco, e nao o produto (que ja seria x{produto:0.####})",
					  entrou && sj.Ficha.bp_milestone_mult == ganho && Disse(tique, $"x{ganho:0.##}"),
					  $"forma {sj.Forma.Atual} | ganho x{sj.Ficha.bp_milestone_mult:0.####} | gesto: {Ultimos(gesto)} | tique: {Ultimos(tique)}");
		}
		AfirmarMf("os cinco marcos estao na ficha, e o ganho e o do MAIOR (x4): SSJ3 depois de SSJ2 nao e x12",
				  sj.Ficha.bp_milestone_mult == 4 && new[] { "ssj", "ssjg2", "ssjg3", "ssj2", "ssj3" }.All(sj.Ficha.bp_milestones_done.Contains),
				  string.Join(",", sj.Ficha.bp_milestones_done));

		// DESCER E VOLTAR: nada muda.
		Ouvir(() => Transformar(sj, subir: false));
		sj.CenaSegundos = 0;
		(bool voltou, _, List<string> semFrase) = MfPedirAForma(sj, "ssj1");
		AfirmarMf("descer ao normal e voltar ao Super Saiyajin nao repete marco nenhum: continua x4, sem frase",
				  voltou && sj.Ficha.bp_milestone_mult == 4 && !Disse(semFrase, "MARCO DE PODER"), Ultimos(semFrase));

		// OS GRAUS FORA DE ORDEM: o Grau 3 primeiro (a tecla de forma deixa), depois o Grau 2.
		ServerPlayer troca = MfForjarSaiyajin("GrausTrocados", pl, forjados);
		troca.Forma.GradesLigados = true;
		troca.Forma.Liberar("ssj1");
		troca.Forma.Maestria.Por("ssj1", 80);
		MfPedirAForma(troca, "ssj1");
		(bool noTres, _, _) = MfPedirAForma(troca, "grade3");
		double depoisDoTres = troca.Ficha.bp_milestone_mult;
		(bool noDois, _, List<string> tiqueDoDois) = MfPedirAForma(troca, "grade2");
		AfirmarMf("o Grau 3 ANTES do Grau 2: x2,5 na estreia do 3, e a estreia do 2 nao DESCE o ganho (a etiqueta entra, sem frase)",
				  noTres && noDois && depoisDoTres == Milestones.ValorDoGrau3 && troca.Ficha.bp_milestone_mult == Milestones.ValorDoGrau3
				  && troca.Ficha.bp_milestones_done.Contains(Milestones.SsjGrau2) && !Disse(tiqueDoDois, "MARCO DE PODER"),
				  $"depois do 3: x{depoisDoTres:0.##}; depois do 2: x{troca.Ficha.bp_milestone_mult:0.##} | {Ultimos(tiqueDoDois)}");

		// QUEM JA TEM O KI DIVINO (x5): o Super Saiyajin (x2) entra calado; o Blue (x6) passa por cima.
		ServerPlayer deus = MfForjarSaiyajin("DeusDaBancada", pl, forjados);
		DespertarDivino.Despertar(deus.Ficha);
		double comOKiDivino = deus.Ficha.bp_milestone_mult;
		deus.Forma.Entrar("ssj1");
		List<string> calado = Ouvir(() => Treinar(deus));
		bool ssjCalado = deus.Ficha.bp_milestone_mult == comOKiDivino && deus.Ficha.bp_milestones_done.Contains(Milestones.Ssj) && !Disse(calado, "MARCO DE PODER");
		deus.Forma.Entrar("blue");
		List<string> azul = Ouvir(() => Treinar(deus));
		AfirmarMf("quem ja tem o `godki` (x5) vira Super Saiyajin SEM frase e sem descer; o Blue (x6) passa por cima e avisa",
				  comOKiDivino == 5 && ssjCalado && deus.Ficha.bp_milestone_mult == 6 && Disse(azul, "x6"),
				  $"ki divino x{comOKiDivino:0.##} -> ssj x? calado={ssjCalado} -> blue x{deus.Ficha.bp_milestone_mult:0.##} | {Ultimos(azul)}");
	}

	// =====================================================================
	// 6) QUEM NAO TEM A FORMA
	// =====================================================================
	private void MfQuemNaoTemAForma(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[marcos] -- 6) quem nao tem a forma nao ganha nada --");

		// O TSUJIN e a raca sem escada desde a v0.4.0 -- ate entao era o Humano, que ganhou a linha da Tensao (logo abaixo).
		ServerPlayer semEscada = GkForjar("TsujinDaBancada", "Tsujin", pl.Zone, pl.Pos + new Vec2(96, 64), 1e13, forjados);
		semEscada.Ficha.SaiyanLineage = "";
		semEscada.Peer = pl.Peer;   // um jogador de verdade, pra a recusa ser a da RACA e nao a de "corpo sem dono"
		List<string> gesto = Ouvir(() => Transformar(semEscada, subir: true));
		List<string> tique = Ouvir(() => { Treinar(semEscada); Treinar(semEscada); });
		AfirmarMf("o TSUJIN com poder de sobra aperta C: a raca nao tem a escada, ele fica na base, e o ganho continua x1 sem etiqueta nenhuma",
				  semEscada.Forma.NaBase && semEscada.Ficha.bp_milestone_mult == 1 && semEscada.Ficha.bp_milestones_done.Count == 0
				  && Disse(gesto, "nao tem essa escada") && !Disse(tique, "MARCO DE PODER"),
				  $"forma {semEscada.Forma.Atual} | {Ultimos(gesto)}");

		// (v0.4.0) E O HUMANO, que ganhou a linha da Tensao (a lista do amigo do dono): ele SOBE -- e a forma nao tem marco, porque nao
		// ha proc do DM pra citar (ver o bloco das entradas em `Formas.cs`). O ganho continua x1.
		ServerPlayer humano = GkForjar("HumanoDaBancada", "Human", pl.Zone, pl.Pos + new Vec2(128, 64), 1e13, forjados);
		humano.Ficha.SaiyanLineage = "";
		humano.Peer = pl.Peer;
		Ouvir(() => Transformar(humano, subir: true));
		List<string> tiqueH = Ouvir(() => { Treinar(humano); Treinar(humano); });
		AfirmarMf("o HUMANO aperta C e vira High Tension -- e a Tensao nao da marco: o ganho continua x1 sem etiqueta nenhuma",
				  humano.Forma.Atual == "alta_tensao" && humano.Ficha.bp_milestone_mult == 1 && humano.Ficha.bp_milestones_done.Count == 0
				  && !Disse(tiqueH, "MARCO DE PODER"),
				  $"forma {humano.Forma.Atual}, marco x{humano.Ficha.bp_milestone_mult:0.##} | {Ultimos(tiqueH)}");

		ServerPlayer comum = MfForjarSaiyajin("ComumDaBancada", pl, forjados);
		comum.Forma.Liberar(Catalogo.IdWrathful);
		(bool entrou, List<string> recusa, _) = MfPedirAForma(comum, Catalogo.IdWrathful);
		AfirmarMf("o Saiyajin COMUM pede o Wrathful (a escada do Lendario): recusado, e nenhum marco do Lendario entra na ficha dele",
				  !entrou && comum.Forma.NaBase && !comum.Ficha.bp_milestones_done.Contains(Milestones.Lssj1) && comum.Ficha.bp_milestone_mult == 1,
				  $"forma {comum.Forma.Atual} | {Ultimos(recusa)}");

		// O CORPO SEM DONO. O NPC sorteado nasce com a escada "estreada" pela biografia (`SorteioDeNpc.AbrirFormas`
		// sobe degrau por degrau pelo mesmo `Entrar`): pago por estado, todo chefe ganharia o marco do topo no
		// primeiro tique. O MESMO corpo, com dono, recebe -- a porta e ter dono, e mais nada.
		ServerPlayer semDono = MfForjarSaiyajin("SemDonoDaBancada", pl, forjados);
		semDono.Peer = null;
		semDono.Forma.Entrar("ssj1");
		semDono.Forma.Entrar("ssj2");
		semDono.Forma.Entrar(Catalogo.IdBase);
		List<string> mudo = Ouvir(() => { Treinar(semDono); Treinar(semDono); });
		bool nadaSemDono = semDono.Ficha.bp_milestone_mult == 1 && semDono.Ficha.bp_milestones_done.Count == 0 && !Disse(mudo, "MARCO DE PODER");
		semDono.Peer = pl.Peer;
		List<string> comDono = Ouvir(() => Treinar(semDono));
		AfirmarMf("o corpo SEM DONO (o NPC, com o SSJ1 e o SSJ2 na biografia) nao recebe marco nenhum; o mesmo corpo COM dono recebe o x3",
				  nadaSemDono && semDono.Ficha.bp_milestone_mult == 3 && Disse(comDono, "x3"),
				  $"sem dono: nada={nadaSemDono} | com dono: x{semDono.Ficha.bp_milestone_mult:0.##} | {Ultimos(comDono)}");
	}

	// =====================================================================
	// 7) O RETROATIVO NO LOGIN
	// =====================================================================
	private void MfORetroativo(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[marcos] -- 7) o retroativo: quem ja tinha despertado a forma recebe o marco ao entrar, uma vez --");
		if (_store == null) { AfirmarMf("(montagem) o servidor tem pasta de contas", false); return; }
		Fighter f = pl.Ficha;
		int ssj1 = Catalogo.Rede("ssj1"), ssj2 = Catalogo.Rede("ssj2"), ssj3 = Catalogo.Rede("ssj3");

		// ---------------------------------------------------------------- (a) O SAVE DE ANTES DOS MARCOS
		// O host vira o personagem de ontem: SSJ1 e SSJ2 ja estreados, de volta ao normal, e a ficha SEM marco nenhum.
		MfZerarOHost(pl);
		pl.Forma.Liberadas = [ssj1, ssj2];
		pl.Forma.EstreiaVista = [ssj1, ssj2];
		var conta = new AccountSave { Conta = ContaDaBancadaDosMarcos, CriadaEm = NowMs() };
		conta.Slots[0] = AccountStore.DeJogador(pl, NowMs());
		_store.Gravar(conta);
		CharacterSave? salvo = _store.Carregar(ContaDaBancadaDosMarcos)?.Slots[0];
		AfirmarMf("(montagem) o save de antes dos marcos foi ao disco: SSJ1 e SSJ2 estreados, ganho x1 e nenhuma etiqueta",
				  salvo is { FormasEstreadas.Count: 2 } && salvo.Ficha.bp_milestone_mult == 1 && salvo.Ficha.bp_milestones_done.Count == 0,
				  salvo == null ? "nao voltou do disco" : $"estreadas {salvo.FormasEstreadas?.Count} | ganho x{salvo.Ficha.bp_milestone_mult:0.##}");
		if (salvo == null) return;

		MarcosDeForma.SoAFormaDeAgoraDeTeste = true;
		try
		{
			ServerPlayer cego = MfLogar(pl, forjados);
			Treinar(cego);
			AfirmarMf("(defeito injetado: as estreias antigas nao contam) quem loga com o SSJ2 ja despertado fica em x1 -- o retroativo REPROVA",
					  cego.Ficha.bp_milestone_mult == 1 && cego.Forma.JaViuAEstreia("ssj2"), $"ganho x{cego.Ficha.bp_milestone_mult:0.##}");
			GkRecolher(cego);
			forjados.Remove(cego);
		}
		finally { MarcosDeForma.SoAFormaDeAgoraDeTeste = false; }

		ServerPlayer antigo = MfLogar(pl, forjados);
		AfirmarMf("o login de verdade devolve as estreias do disco e poe o corpo no repouso: ele NAO esta em SSJ2, ele ja ESTEVE",
				  antigo.Forma.JaViuAEstreia("ssj1") && antigo.Forma.JaViuAEstreia("ssj2") && antigo.Forma.Atual != "ssj2" && antigo.Ficha.bp_milestone_mult == 1,
				  $"forma {antigo.Forma.Atual} | ganho x{antigo.Ficha.bp_milestone_mult:0.##}");
		List<string> falas = Ouvir(() => Treinar(antigo));
		AfirmarMf("**O RETROATIVO**: no primeiro tique de fichas ele recebe o MAIOR marco das formas que ja estreou (SSJ2 = x3, e nao x2 nem x6) e le a frase da divida antiga",
				  antigo.Ficha.bp_milestone_mult == 3 && antigo.Ficha.bp_milestones_done.SetEquals(["ssj", "ssj2"])
				  && Disse(falas, "já tinha despertado") && Disse(falas, "x3") && !Disse(falas, "rompeu um novo patamar")
				  && falas.Count(x => x.Contains("MARCO DE PODER", StringComparison.Ordinal)) == 1,
				  $"ganho x{antigo.Ficha.bp_milestone_mult:0.##} | {Ultimos(falas)}");
		falas = Ouvir(() => { Treinar(antigo); Treinar(antigo); });
		AfirmarMf("...uma vez: os tiques seguintes nao repetem", antigo.Ficha.bp_milestone_mult == 3 && !Disse(falas, "MARCO DE PODER"), Ultimos(falas));

		// ---------------------------------------------------------------- (b) O SEGUNDO LOGIN
		conta.Slots[0] = AccountStore.DeJogador(antigo, NowMs());
		_store.Gravar(conta);
		GkRecolher(antigo);
		forjados.Remove(antigo);
		ServerPlayer deNovo = MfLogar(pl, forjados);
		falas = Ouvir(() => { Treinar(deNovo); Treinar(deNovo); });
		AfirmarMf("o SEGUNDO login nao concede de novo: o ganho x3 e as etiquetas voltaram do disco, e nao ha frase",
				  deNovo.Ficha.bp_milestone_mult == 3 && deNovo.Ficha.bp_milestones_done.SetEquals(["ssj", "ssj2"]) && !Disse(falas, "MARCO DE PODER"),
				  $"ganho x{deNovo.Ficha.bp_milestone_mult:0.##} | {Ultimos(falas)}");
		GkRecolher(deNovo);
		forjados.Remove(deNovo);

		// ---------------------------------------------------------------- (c) O SAVE DE ANTES DOS DOIS BITS
		// Num save mais velho ainda nao existe a lista de estreias: o login a copia das formas liberadas.
		CharacterSave velho = AccountStore.DeJogador(pl, NowMs());
		velho.FormasDespertadas = [ssj1, ssj2, ssj3];
		velho.FormasEstreadas = null;
		conta.Slots[0] = velho;
		_store.Gravar(conta);
		ServerPlayer maisVelho = MfLogar(pl, forjados);
		falas = Ouvir(() => Treinar(maisVelho));
		AfirmarMf("o save de antes de existir a lista de estreias (so as formas liberadas): SSJ3 liberado vira x4 no login",
				  maisVelho.Forma.JaViuAEstreia("ssj3") && maisVelho.Ficha.bp_milestone_mult == 4 && Disse(falas, "já tinha despertado"),
				  $"ganho x{maisVelho.Ficha.bp_milestone_mult:0.##} | {Ultimos(falas)}");
		GkRecolher(maisVelho);
		forjados.Remove(maisVelho);

		// ---------------------------------------------------------------- (d) O MARCO MAIOR NAO DESCE
		MfZerarOHost(pl);
		pl.Forma.Liberadas = [ssj1];
		pl.Forma.EstreiaVista = [ssj1];
		f.ReachMilestone("godki");
		conta.Slots[0] = AccountStore.DeJogador(pl, NowMs());
		_store.Gravar(conta);
		ServerPlayer divino = MfLogar(pl, forjados);
		falas = Ouvir(() => Treinar(divino));
		AfirmarMf("quem loga com um marco MAIOR ja pago (`godki`, x5) e so o SSJ1 estreado: a etiqueta entra, o ganho nao desce e nao ha frase",
				  divino.Ficha.bp_milestone_mult == 5 && divino.Ficha.bp_milestones_done.Contains(Milestones.Ssj) && !Disse(falas, "MARCO DE PODER"),
				  $"ganho x{divino.Ficha.bp_milestone_mult:0.##} | {string.Join(",", divino.Ficha.bp_milestones_done)} | {Ultimos(falas)}");
		GkRecolher(divino);
		forjados.Remove(divino);
	}

	// =====================================================================
	// 8) OS CAMINHOS FORA DA TECLA
	// =====================================================================
	private void MfOsCaminhosForaDaTecla(ServerPlayer pl, List<ServerPlayer> forjados)
	{
		GD.Print("[marcos] -- 8) os caminhos fora da tecla: a queda do Oozaru Dourado e o `admin_forma` --");

		// A QUEDA DO OOZARU DOURADO: o corpo cai em SSJ4 com a forma LIBERADA e a estreia devendo (a cena e do
		// primeiro C). Sao as duas linhas do `GameServer.Oozaru.cs` -- `Liberar` + `Atual =` --, e o marco e dele ali.
		ServerPlayer primal = MfForjarSaiyajin("PrimalDaBancada", pl, forjados);
		primal.Ficha.SaiyanLineage = Oozaru.LinhagemPrimal;
		primal.Forma.Liberar("ssj4");
		primal.Forma.Atual = "ssj4";
		List<string> falas = Ouvir(() => Treinar(primal));
		AfirmarMf("quem cai do Oozaru Dourado em SSJ4 (liberado, SEM estreia) recebe o `ssj4` (x5) ali mesmo, com a frase de quem acabou de entrar",
				  !primal.Forma.JaViuAEstreia("ssj4") && primal.Ficha.bp_milestone_mult == 5 && Disse(falas, "rompeu um novo patamar"),
				  $"ganho x{primal.Ficha.bp_milestone_mult:0.##} | {Ultimos(falas)}");

		// O `admin_forma`, pelo despacho de producao: a forma forcada tambem e uma forma em que o corpo esteve.
		MfZerarOHost(pl);
		Ouvir(() => Verbo(pl, "admin_forma", "ssj3"));
		pl.CenaSegundos = 0;
		falas = Ouvir(() => Treinar(pl));
		AfirmarMf("o `admin_forma ssj3` poe o host em Super Saiyajin 3, e o tique seguinte paga o `ssj3` (x4)",
				  pl.Forma.Atual == "ssj3" && pl.Ficha.bp_milestone_mult == 4 && Disse(falas, "x4"),
				  $"forma {pl.Forma.Atual} | ganho x{pl.Ficha.bp_milestone_mult:0.##} | {Ultimos(falas)}");

		// O `Assess Target` e onde o admin LE o numero (o jogador so tem a frase do chat).
		falas = Ouvir(() => Verbo(pl, "admin_ficha", "0"));
		AfirmarMf("o `Assess Target` mostra o ganho e os patamares do alvo: \"ganho de poder x4 · patamares rompidos: ssj3\"",
				  Disse(falas, "ganho de poder x4") && Disse(falas, "patamares rompidos: ssj3"),
				  string.Join(" | ", falas.Where(x => x.Contains("ganho de poder", StringComparison.Ordinal))));
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>
	/// Um Saiyajin comum ao lado do host, com poder acima de qualquer porta, o Ki cheio e a TELA DO HOST emprestada
	/// (o marco de forma e so de quem tem dono; o `GkRecolher` devolve a tela antes de tirar o corpo do mundo).
	/// </summary>
	private ServerPlayer MfForjarSaiyajin(string nome, ServerPlayer host, List<ServerPlayer> forjados)
	{
		ServerPlayer c = GkForjar(nome, "Saiyan", host.Zone, host.Pos + new Vec2(64, 64), 1e13, forjados);
		c.Ficha.SaiyanLineage = "Saiyan";
		c.Peer = host.Peer;
		return c;
	}

	/// <summary>
	/// O HOST VIRA UM SAIYAJIN QUE NUNCA SE TRANSFORMOU: no repouso, sem forma liberada nem estreada, sem marco
	/// nenhum, com poder acima de qualquer porta e o Ki cheio.
	/// </summary>
	private void MfZerarOHost(ServerPlayer pl)
	{
		Fighter f = pl.Ficha;
		pl.Forma.Atual = Catalogo.IdDoPiso(Perfil(pl));
		pl.Forma.Liberadas = [];
		pl.Forma.EstreiaVista = [];
		pl.CenaSegundos = 0;
		f.bp_milestone_mult = 1;
		f.bp_milestones_done = new HashSet<string>(StringComparer.Ordinal);
		f.BP = 1e13;
		f.KO = false;
		AplicarForma(pl);
		f.Tick(agoraMs: NowMs());
		f.Ki = f.MaxKi;
	}

	/// <summary>
	/// A TECLA DE FORMA (`TransformarPara`, com o `Avaliar` inteiro) e, em seguida, UM tique de fichas. Devolve se
	/// o corpo entrou na forma pedida, o que ele ouviu no gesto e o que ouviu no tique.
	/// </summary>
	private (bool Entrou, List<string> Gesto, List<string> Tique) MfPedirAForma(ServerPlayer c, string id)
	{
		c.CenaSegundos = 0;
		c.Ficha.Ki = c.Ficha.MaxKi;
		List<string> gesto = Ouvir(() => TransformarPara(c, id));
		c.CenaSegundos = 0;
		List<string> tique = Ouvir(() => Treinar(c));
		return (c.Forma.Atual == id, gesto, tique);
	}

	/// <summary>
	/// O login de verdade da conta da bancada (o `Logar`, que restaura a forma pelo metodo do login), num corpo que
	/// nao grava por cima de slot nenhum e que leva a tela do host emprestada.
	/// </summary>
	private ServerPlayer MfLogar(ServerPlayer host, List<ServerPlayer> forjados)
	{
		ServerPlayer c = Logar(ContaDaBancadaDosMarcos, 0, 0);
		c.Slot = -1;
		c.Peer = host.Peer;
		forjados.Add(c);
		return c;
	}
}
