using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Skills;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO SISTEMA DE KI, DE PONTA A PONTA (`--kideponta`) -- a metade de SERVIDOR.
///
///     Godot --headless --path . --host --rede 7961 --kideponta --diagki
///           --conta bancadaki7961 --nome Pontaponta
///
/// ============================ POR QUE UMA QUINTA BANCADA ============================
/// As quatro que ja existem (`--projetilteste`, `--tecnicateste`, `--embatekiteste`,
/// `--tiroiateste`, 216 checagens) medem cada camada ISOLADA, no boot, com corpos forjados e sem
/// ninguem no fio. Todas continuam valendo e nenhuma foi tocada.
///
/// O que nenhuma delas responde e o que esta aqui:
///   * a tabela de precos e a MESMA que o DM cobra, **linha a linha e por CONJUNTO** -- uma compra
///     nova que nasca sem linha na tabela reprova, e e isso que protege o amanha;
///   * o dano que sai do funil de producao bate com uma SEGUNDA implementacao da conta do DM,
///     escrita aqui a partir das linhas do original;
///   * **nao existe alavanca de acerto no fio** -- e isso e afirmacao sobre o CONJUNTO dos opcodes
///     e sobre o que o `Client/` conhece, nao sobre um pacote especifico;
///   * a colisao de ki empurra nos DOIS sentidos e empata quando as forcas sao iguais;
///   * a IA atira pelo funil do jogador -- **varredura de paridade do canal de tiro por CONJUNTO**,
///     nos moldes da que a `--iateste` ja faz pro corpo;
///   * os dois tetos (zona e MUNDO) disparam;
///   * um save escrito ANTES deste sistema (o arquivo do disco sem a chave) entra sem migracao.
///
/// A METADE VIVA -- conta nova pelo fio, tecnica montada por verbo de rede, o teto de dez ao vivo,
/// o CLIENTE MENTINDO e o RELOGIN -- e do `Client/RoboDeKi.cs` (`--diagki`), que roda no mesmo
/// processo e conversa com este servidor por socket de verdade. As duas metades imprimem o proprio
/// placar; o total e a soma.
/// ================================================================================
///
/// ============================ ELA CHAMA O CODIGO DE PRODUCAO, E SO ELE ============================
/// Os pontos passam por `Aplicar`, os verbos por `ComandoDeTecnicaCustomizada` (o MESMO ramo que o
/// `C2S.Verbo` do jogador aciona), os tiros por `Disparar`/`Canalizar`, o voo por
/// `TickDosProjeteis`, a disputa pelo gatilho dentro do tique, a IA por `TickDosCorposSemDono` e o
/// disco pelo `AccountStore`. O unico privilegio e a CADENCIA (os tiques sao chamados na mao),
/// exatamente como nas quatro anteriores.
/// =============================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>`--kideponta`: a bancada em si, e ela roda UMA vez, no primeiro login.</summary>
	private bool _pontaDeTeste;

	/// <summary>
	/// A MESMA FLAG, mas ela NAO SE APAGA -- e por isso e um campo separado.
	///
	/// A metade viva RELOGA de proposito (e a familia da persistencia), e quem volta encontra o
	/// proprio corpo do jeito que o deixou: com o Ki gasto nas rajadas anteriores. Sem re-armar a
	/// cada entrada, o primeiro tiro depois do relogin ouve *"isso pede pelo menos 27 de energia"* --
	/// que foi o que a primeira rodada mediu, e que teria passado por "a tecnica nao voltou".
	/// </summary>
	private bool _pontaLigada;

	private int _ppOk, _ppFalhou;

	/// <summary>
	/// O BONECO QUE O ROBO VAI ACERTAR. Fica de pe DEPOIS que esta bancada termina, e e a unica
	/// coisa que ela deixa no mundo -- a metade viva precisa de alguem pra levar o tiro, e um alvo
	/// que o proprio robo criasse seria o cliente inventando corpo.
	/// </summary>
	private ServerPlayer? _pontaBoneco;

	private void AfirmarPp(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ppOk++; GD.Print($"[kiponta]   OK    {oque}"); return; }
		_ppFalhou++;
		GD.PrintErr($"[kiponta]   FALHA {oque}   {detalhe}");
	}

	/// <summary>
	/// Roda no PRIMEIRO LOGIN, e nao no boot, por duas razoes que nenhuma das quatro anteriores
	/// tinha: (1) a metade viva precisa que o servidor ja esteja no ar e com o robo dentro, e (2) o
	/// alvo do robo (<see cref="_pontaBoneco"/>) tem que nascer na zona de quem entrou -- no boot
	/// nao ha zona de ninguem.
	/// </summary>
	public void RodarBancadaDePontaAPonta(ServerPlayer vivo)
	{
		_ppOk = _ppFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);

		GD.Print("[kiponta] ================ O SISTEMA DE KI, DE PONTA A PONTA ================");
		AfirmarPp("a zona da bancada tem colisao carregada", _pjMapa != null);

		List<string>? escutaAntes = EscutaDeAvisos;
		try
		{
			ATabelaDePontosDoDm();
			OsDoisTetosDaLoja();
			ODanoContraAFormulaDoOriginal();
			OsTresTiposVoamDiferente();
			NaoHaAlavancaDeAcertoNoFio();
			AColisaoNosDoisSentidos();
			AIaAtiraPeloFunilDoJogador();
			OTetoDeTirosVivosDispara();
			OSaveVelhoCarrega();
		}
		catch (Exception e)
		{
			AfirmarPp("a bancada rodou inteira sem estourar", false, e.ToString());
		}
		finally
		{
			EscutaDeAvisos = escutaAntes;
			LimparEmbatesDaBancada();
			ArmarAMetadeViva(vivo);
		}

		GD.Print($"[kiponta] ================ {_ppOk} passaram, {_ppFalhou} falharam ================");
	}

	// =====================================================================
	// 1) A TABELA DE PONTOS, LINHA A LINHA -- E POR CONJUNTO
	// =====================================================================
	/// <summary>
	/// UMA LINHA DA TABELA DO DM. Ela e DADO e nao codigo de proposito: a tabela do original tem
	/// dezoito botoes, cada um com a sua propria copia da guarda, e foi assim que tres deles
	/// ficaram diferentes dos outros quinze. Escrita como lista, ela se le junto com o
	/// `customattacks.dm` aberto ao lado.
	/// </summary>
	/// <param name="Compra">O botao.</param>
	/// <param name="Dm">A linha do `customattacks.dm` de onde o preco saiu.</param>
	/// <param name="Preparar">O estado que o botao exige (o DM tambem exige -- so nao diz).</param>
	/// <param name="Arg">O argumento, pros dois botoes que pedem numero (`input` do DM).</param>
	/// <param name="Campo">O que tem que ter mexido.</param>
	/// <param name="Delta">Quanto o campo anda.</param>
	/// <param name="Pontos">Quanto o `custompoints_spent` anda. NEGATIVO = a compra RENDE ponto.</param>
	private readonly record struct LinhaDaLoja(
		Compra Compra, string Dm, Action<TecnicaCustomizada>? Preparar, double Arg,
		Func<TecnicaCustomizada, double> Campo, double Delta, int Pontos);

	/// <summary>
	/// ENCHE O SALDO ANTES DE MEDIR UM ESTORNO. Desde o piso em zero do dono
	/// (`TecnicaCustomizada.Gasto`), uma desvantagem so RENDE se houver o que devolver -- entao medir
	/// o preco de um estorno num rascunho zerado mediria a recusa, e nao o preco.
	///
	/// Compra em POTENCIA de proposito: e o degrau de 1 ponto mais bobo da tabela, nao mexe em nenhum
	/// outro campo, e a unica linha cujo <c>Campo</c> e a potencia (`DanoMenos`) e justamente a que
	/// QUER esse ponto gasto na potencia.
	/// </summary>
	private static void Gastar(TecnicaCustomizada t, int pontos)
	{
		for (int i = 0; i < pontos; i++) t.Aplicar(Compra.DanoMais, 0, out _);
	}

	/// <summary>
	/// A TABELA INTEIRA. Dezoito linhas -- uma por valor de <see cref="Compra"/>, e a checagem de
	/// CONJUNTO logo abaixo exige que continue sendo uma por valor.
	///
	/// O <c>Preparar</c> de cada linha e o ESTADO QUE O BOTAO EXIGE, e desde o piso em zero ele passou
	/// a incluir o SALDO: toda linha de preco negativo (a compra que RENDE ponto) precisa ter gasto ao
	/// menos aquilo antes. O preco em si nao mudou em linha nenhuma -- o que mudou foi de onde se pode
	/// paga-lo.
	/// </summary>
	private static LinhaDaLoja[] TabelaDoDm() =>
	[
		new(Compra.DanoMais,   ":962",  null, 0, t => t.BaseDano, +0.1, +1),
		new(Compra.DanoMenos,  ":972",  t => Gastar(t, 1), 0, t => t.BaseDano, -0.1, -1),

		new(Compra.CargaMais,  ":940",  t => Gastar(t, 1), 0, t => t.CargaMinima, +0.4, -1),
		new(Compra.CargaMenos, ":947",  null, 0, t => t.CargaMinima, -0.4, +1),

		new(Compra.KiMais,     ":983",  t => Gastar(t, 1), 0, t => t.CustoKi, +40, -1),
		// O PISO DO KI E O PADRAO (20): pra baratear e preciso ter encarecido antes -- e la tambem.
		// E encarecer, agora, exige ter gasto o ponto que o encarecimento devolve.
		new(Compra.KiMenos,    ":990",  t => { Gastar(t, 1); t.Aplicar(Compra.KiMais, 0, out _); }, 0,
			t => t.CustoKi, -40, +1),

		// LIGAR A ESTAMINA **RENDE** 2 (`custompoints_spent += -2`). Ver a nota do briefing invertido.
		new(Compra.StaminaLigar,    ":1008", t => Gastar(t, 2), 0, t => t.CustoStamina, +1, -2),
		new(Compra.StaminaDesligar, ":1013",
			t => { Gastar(t, 2); t.Aplicar(Compra.StaminaLigar, 0, out _); }, 0,
			t => t.CustoStamina, -1, +2),
		// TRES gastos: dois financiam o `StaminaLigar` e o terceiro sobra pro degrau de folego.
		new(Compra.StaminaMais,     ":1025",
			t => { Gastar(t, 3); t.Aplicar(Compra.StaminaLigar, 0, out _); }, 0,
			t => t.CustoStamina, +1, -1),
		new(Compra.StaminaMenos,    ":1031",
			t => { Gastar(t, 3); t.Aplicar(Compra.StaminaLigar, 0, out _);
				   t.Aplicar(Compra.StaminaMais, 0, out _); }, 0,
			t => t.CustoStamina, -1, +1),

		// O DEGRAU MUDA NO 1: de 1 pra cima anda de 1 em 1; de 1 pra baixo, de 0,2 em 0,2.
		new(Compra.VelocidadeMais,  ":1046", null, 0, t => t.Velocidade, +1, +1),
		new(Compra.VelocidadeMenos, ":1064", t => Gastar(t, 1), 0, t => t.Velocidade, -0.2, -1),

		new(Compra.InstantaneoLigar,    ":1319", null, 0, t => t.Instantaneo ? 1 : 0, +1, +2),
		new(Compra.InstantaneoDesligar, ":1326", t => t.Aplicar(Compra.InstantaneoLigar, 0, out _), 0,
			t => t.Instantaneo ? 1 : 0, -1, -2),

		// A CAIXINHA DE CARGA nasce LIGADA no raio (`PickAttackType:874`), entao ligar exige desligar.
		new(Compra.CarregavelDesligar, ":922", null, 0, t => t.Carregavel ? 1 : 0, -1, +1),
		new(Compra.CarregavelLigar,    ":918", t => t.Aplicar(Compra.CarregavelDesligar, 0, out _), 0,
			t => t.Carregavel ? 1 : 0, +1, -1),

		// UM PONTO POR TILE, nos dois sentidos (`round(amount - range)`).
		new(Compra.Alcance, ":1346", null, TecnicaCustomizada.AlcancePadrao + 1, t => t.Alcance, +1, +1),

		// UM PONTO A CADA 0,1 (`round((amount - rangemodifier) * 10)`) -- e o ALERTA do DM diz 2.
		new(Compra.DistanciaMod, ":1377", null, TecnicaCustomizada.DistModPadrao + 0.1,
			t => t.DistanciaMod, +0.1, +1),
	];

	/// <summary>
	/// CADA LINHA, APLICADA NUM RASCUNHO NOVO -- e o campo e o preco conferidos juntos.
	///
	/// COMO ELA REPROVA: mexa em qualquer passo ou preco do `Core.Skills.TecnicaCustomizada` (troque
	/// `DanoPasso` pra 0,2, ou faca `StaminaLigar` cobrar em vez de render) e a linha
	/// correspondente fica vermelha, dizendo o que veio e o que o DM manda. Acrescente uma compra
	/// nova ao `enum Compra` sem escrever a linha dela aqui e a checagem de CONJUNTO reprova -- que
	/// e a unica que cobre o botao que ainda nao existe.
	/// </summary>
	private void ATabelaDePontosDoDm()
	{
		GD.Print("[kiponta] -- 1) A TABELA DE PONTOS, LINHA A LINHA CONTRA O `customattacks.dm`");

		LinhaDaLoja[] tabela = TabelaDoDm();

		foreach (LinhaDaLoja l in tabela)
		{
			var t = new TecnicaCustomizada { Id = 1 };
			t.PorTipo(TipoDeProjetil.Beam);   // o unico tipo que aceita a tabela inteira
			l.Preparar?.Invoke(t);

			double antes = l.Campo(t);
			int gastoAntes = t.Gasto;

			bool passou = t.Aplicar(l.Compra, l.Arg, out string porque);
			double andou = l.Campo(t) - antes;
			int custou = t.Gasto - gastoAntes;

			AfirmarPp($"{l.Compra} ({l.Dm}): move {l.Delta:+0.0#;-0.0#} e custa {l.Pontos:+0;-0} ponto(s)",
					  passou && Math.Abs(andou - l.Delta) < 1e-6 && custou == l.Pontos,
					  passou ? $"moveu {andou:0.###} e custou {custou:+0;-0}" : $"recusou: {porque}");
		}

		// ---- O CONJUNTO: nenhuma compra fica de fora ----
		// Esta e a linha que protege o amanha. As dezoito de cima provam o que existe hoje; o
		// proximo furo vai ser um botao NOVO com preco errado, e ele nasce fora de qualquer lista
		// escrita a mao.
		var naTabela = tabela.Select(l => l.Compra).ToHashSet();
		List<Compra> forasDaTabela = [.. Enum.GetValues<Compra>().Where(c => !naTabela.Contains(c))];
		AfirmarPp($"CONJUNTO: as {Enum.GetValues<Compra>().Length} compras do painel tem linha na tabela",
				  forasDaTabela.Count == 0, string.Join(", ", forasDaTabela));

		// ---- A IDA E VOLTA PELO FIO ----
		// A tabela so vale se o numero dela CHEGAR na tela: escrita e leitura do `CustomWire` moram
		// coladas justamente porque desalinhar o fio e o defeito mais barato de cometer.
		var cheia = new TecnicaCustomizada { Id = 7 };
		cheia.PorTipo(TipoDeProjetil.Guided);
		cheia.Nome = "Bola Caçadora";
		cheia.Grito = "SOME DAQUI!";
		cheia.DizGrito = true;
		// UMA TECNICA COM SALDO NO MEIO DO INTERVALO, e nao no zero: `Gasto` so vale como teste de fio
		// se for diferente do que um `new` traz de graca -- um campo que nao e escrito e um campo que
		// "atravessa" perfeitamente. Tres compras de potencia (gasto 3), o folego devolve 2 (gasto 1) e
		// a velocidade cobra 1 (gasto 2).
		for (int i = 0; i < 3; i++) cheia.Aplicar(Compra.DanoMais, 0, out _);
		cheia.Aplicar(Compra.StaminaLigar, 0, out _);
		cheia.Aplicar(Compra.VelocidadeMais, 0, out _);

		var w = new LiteNetLib.Utils.NetDataWriter();
		CustomWire.Escrever(w, cheia);
		var r = new LiteNetLib.Utils.NetDataReader(w.CopyData());
		TecnicaCustomizada volta = CustomWire.Ler(r);

		AfirmarPp("a tecnica atravessa o fio sem perder nada (tipo, texto, numeros e PONTOS)",
				  volta.Id == cheia.Id && volta.Tipo == cheia.Tipo && volta.Nome == cheia.Nome
				  && volta.Grito == cheia.Grito && volta.DizGrito
				  && Math.Abs(volta.BaseDano - cheia.BaseDano) < 1e-4
				  && Math.Abs(volta.Velocidade - cheia.Velocidade) < 1e-4
				  && volta.UsaStamina && volta.Gasto == cheia.Gasto && cheia.Gasto == 2,
				  $"gasto {volta.Gasto} contra {cheia.Gasto}, dano {volta.BaseDano:0.###}");
		AfirmarPp("...e o leitor consumiu o pacote inteiro (nada sobrou nem faltou)",
				  r.AvailableBytes == 0, $"sobraram {r.AvailableBytes} bytes");
	}

	// =====================================================================
	// 2) OS DOIS TETOS DA LOJA: 5 PONTOS E 10 TECNICAS
	// =====================================================================
	/// <summary>
	/// *"Um teto que nunca e atingido e indistinguivel de teto nenhum."* Entao os dois tetos sao
	/// ATINGIDOS aqui, pelos verbos de producao, e os dois tem que FALAR.
	///
	/// COMO ELA REPROVA: tire a guarda `Cabe` de qualquer compra (a divergencia 2 de 3 -- o ramo
	/// fino da velocidade -- era exatamente isso) e o sexto ponto passa; tire o `else` do teto de
	/// dez (que e o estado do DM) e a decima primeira tecnica nasce calada.
	/// </summary>
	private void OsDoisTetosDaLoja()
	{
		GD.Print("[kiponta] -- 2) OS DOIS TETOS DA LOJA DISPARAM (5 pontos, 10 tecnicas)");

		// ---- O TETO DE 5 PONTOS ----
		var t = new TecnicaCustomizada { Id = 1 };
		t.PorTipo(TipoDeProjetil.Beam);
		int compradas = 0;
		for (int i = 0; i < 12; i++)
			if (t.Aplicar(Compra.DanoMais, 0, out _)) compradas++;

		AfirmarPp($"cabem exatamente {TecnicaCustomizada.PontosTotais} compras de 1 ponto, e nem uma a mais",
				  compradas == TecnicaCustomizada.PontosTotais, $"comprou {compradas}");
		AfirmarPp("...e a recusa DIZ por que (silencio no lugar de erro e a armadilha 5 da casa)",
				  !t.Aplicar(Compra.DanoMais, 0, out string porque) && porque.Length > 0, porque);

		// ---- O PISO EM ZERO: O ESTORNO SO DEVOLVE O QUE FOI GASTO ----
		// ============================ ESTA AFIRMACAO FOI VIRADA DO AVESSO ============================
		// Ela dizia o contrario, e dizia certo pro codigo da epoca: o porte literal do DM deixava
		// `custompoints_spent` ficar NEGATIVO, e levar a potencia ao piso rendia 7 pontos e deixava DOZE
		// na mao. O dono cortou (ver `TecnicaCustomizada.Gasto`), entao a afirmacao mudou junto -- ela
		// nao foi AFROUXADA pra parar de reprovar, foi reescrita pro comportamento novo, e continua
		// exigindo numero exato dos dois lados.
		//
		// O QUE ELA PROVA AGORA: o estorno nao virou proibido, virou LIMITADO. Com 3 pontos gastos saem
		// exatamente 3 degraus de volta, o quarto e recusado, e -- a parte que importa -- a potencia
		// PAROU junto: o clique recusado nao enfraqueceu a tecnica de graca.
		// =========================================================================================
		var pobre = new TecnicaCustomizada { Id = 2 };
		pobre.PorTipo(TipoDeProjetil.Beam);
		string sobra = "";
		while (pobre.Aplicar(Compra.DanoMenos, 0, out sobra)) { }
		AfirmarPp("com o orcamento intacto, a desvantagem e RECUSADA e nao rende nada",
				  pobre.Gasto == 0 && pobre.Restantes == TecnicaCustomizada.PontosTotais
				  && Math.Abs(pobre.BaseDano - TecnicaCustomizada.DanoPadrao) < 1e-9 && sobra.Length > 0,
				  $"gasto {pobre.Gasto}, dano {pobre.BaseDano:0.###}, motivo '{sobra}'");

		for (int i = 0; i < 3; i++) pobre.Aplicar(Compra.DanoMais, 0, out _);
		int devolvidos = 0;
		while (pobre.Aplicar(Compra.DanoMenos, 0, out _)) devolvidos++;
		AfirmarPp("gastando 3, o estorno devolve exatamente 3 -- e para no zero, sem furar pra baixo",
				  devolvidos == 3 && pobre.Gasto == 0
				  && Math.Abs(pobre.BaseDano - TecnicaCustomizada.DanoPadrao) < 1e-9,
				  $"devolveu {devolvidos}, gasto {pobre.Gasto}, dano {pobre.BaseDano:0.###}");

		// ---- 4000 COMPRAS A ESMO: A INVARIANTE NAO CEDE ----
		var rng = new Random(20260813);
		Compra[] todas = Enum.GetValues<Compra>();
		var doida = new TecnicaCustomizada { Id = 3 };
		doida.PorTipo(TipoDeProjetil.Beam);
		bool estourou = false, forouDoPiso = false, afundou = false;
		for (int i = 0; i < 4000; i++)
		{
			Compra c = todas[rng.Next(todas.Length)];
			double arg = c switch
			{
				Compra.Alcance => rng.Next(1, 40),
				Compra.DistanciaMod => Math.Round(rng.NextDouble() * 2, 1),
				_ => 0,
			};
			doida.Aplicar(c, arg, out _);
			estourou |= doida.Gasto > TecnicaCustomizada.PontosTotais;
			// O PISO ANDA JUNTO DO TETO daqui em diante: as duas bordas moram no mesmo `Cobrar`, e
			// medir so uma delas deixaria a outra livre pra afundar sem ninguem ver.
			afundou |= doida.Gasto < 0;
			forouDoPiso |= doida.BaseDano < TecnicaCustomizada.DanoPiso - 1e-9
						|| doida.CargaMinima < TecnicaCustomizada.CargaPiso - 1e-9
						|| doida.CustoKi < TecnicaCustomizada.KiPiso - 1e-9
						|| doida.Velocidade < TecnicaCustomizada.VelocidadePiso - 1e-9
						|| doida.Velocidade > TecnicaCustomizada.VelocidadeTeto + 1e-9
						|| doida.Alcance < TecnicaCustomizada.AlcancePiso
						|| doida.DistanciaMod < TecnicaCustomizada.DistModPiso - 1e-9;
		}
		AfirmarPp("4000 compras a esmo nunca passam do teto de pontos", !estourou, $"gasto {doida.Gasto}");
		AfirmarPp("...nem afundam abaixo do piso de zero", !afundou, $"gasto {doida.Gasto}");
		AfirmarPp("...e nenhum campo furou o proprio piso ou teto", !forouDoPiso,
				  $"dano {doida.BaseDano:0.##} carga {doida.CargaMinima:0.##} vel {doida.Velocidade:0.##} "
				  + $"alc {doida.Alcance:0} mod {doida.DistanciaMod:0.##}");

		// ---- O TETO DE 10 TECNICAS, PELOS VERBOS DE VERDADE ----
		// Corpo forjado e nao o jogador vivo: `Persistir` sai fora quando nao ha `Peer`, entao esta
		// familia nao escreve nada no disco de ninguem -- e a decima primeira tecnica do ROBO, la na
		// metade viva, atravessa o mesmo teto com save de verdade.
		ServerPlayer pl = Forjar("Inventor", CorredorLivre(4), bp: 5_000);
		EscutaDeAvisos = [];
		for (int i = 0; i < TecnicaCustomizada.Maximo; i++)
		{
			ComandoDeTecnicaCustomizada(pl, "ca_criar", "");
			ComandoDeTecnicaCustomizada(pl, "ca_texto", $"nome/Tecnica {i + 1}");
			ComandoDeTecnicaCustomizada(pl, "ca_salvar", "");
		}
		AfirmarPp($"as {TecnicaCustomizada.Maximo} tecnicas cabem, criadas pelo verbo `ca_criar` de producao",
				  pl.Customizadas.Count == TecnicaCustomizada.Maximo, $"{pl.Customizadas.Count}");
		AfirmarPp("...e os ids sao 1..10, sem buraco e sem repetido",
				  pl.Customizadas.Select(x => x.Id).OrderBy(x => x).SequenceEqual(Enumerable.Range(1, 10)));

		EscutaDeAvisos.Clear();
		ComandoDeTecnicaCustomizada(pl, "ca_criar", "");
		AfirmarPp("a 11a e RECUSADA COM MOTIVO (no DM o verbo nao faz nada e nao diz nada)",
				  pl.Mesa == null && EscutaDeAvisos.Exists(a => a.Contains("cabem", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", EscutaDeAvisos));

		// A SEGUNDA PORTA: a mesa sobrevive entre comandos, entao confirmar tambem tem que recusar.
		pl.Mesa = new TecnicaCustomizada { Id = 11 };
		EscutaDeAvisos.Clear();
		ComandoDeTecnicaCustomizada(pl, "ca_salvar", "");
		AfirmarPp("...e CONFIRMAR uma 11a tambem recusa (um teto que so vale numa das portas nao e teto)",
				  pl.Customizadas.Count == TecnicaCustomizada.Maximo,
				  $"{pl.Customizadas.Count}: {string.Join(" | ", EscutaDeAvisos)}");

		// ESQUECER LIBERA A VAGA, e a vaga que volta e a do id ESQUECIDO -- nao `Count + 1`.
		ComandoDeTecnicaCustomizada(pl, "ca_esquecer", "4");
		ComandoDeTecnicaCustomizada(pl, "ca_criar", "");
		AfirmarPp("esquecer a 4 libera o id 4 (e nao o 11, que ja esta ocupado)",
				  pl.Mesa?.Id == 4, $"mesa no id {pl.Mesa?.Id}");

		EscutaDeAvisos = null;
		LimparTudoDaBancada();
	}

	// =====================================================================
	// 3) O DANO, CONTRA UMA SEGUNDA IMPLEMENTACAO DA CONTA DO DM
	// =====================================================================
	/// <summary>
	/// A CONTA DO `Bump` DE `/obj/attack/blast`, ESCRITA DE NOVO (`objects.dm:315-331`):
	/// <code>
	/// dmg = DamageCalc(mods*6*globalKiDamage, Ekidef**2 * max(Etechnique,Ekiskill), basedamage, maxdamage)
	/// se dmg == 0: dmg += basedamage*0.02
	/// dmg = ArmorCalc(dmg, Esuperkiarmor)
	/// dmg /= log_4(max(kidefenseskill,4));   se bloqueando: dmg /= 2*log_4(...)
	/// final = ResistCheck(dmg) * BPModulus(BP_do_tiro, expressedBP_do_alvo)
	/// </code>
	///
	/// ============================ O QUE E INDEPENDENTE AQUI, E O QUE NAO E ============================
	/// A ESTRUTURA da cadeia de ki -- o `Ekidef` AO QUADRADO, o piso de 2%, a ordem
	/// armadura -> pericia -> guarda -> gap de poder, e a pericia contando DE NOVO no bloqueio -- e
	/// escrita aqui do zero, e e ela que separa esta conta da do soco.
	///
	/// `CombatMath.Armadura`, `CombatMath.Resistencia` e `CombatMath.BpModulus` sao CHAMADAS e nao
	/// reescritas: sao o `ArmorCalc`, o `ResistCheck` e o `BPModulus` do DM, compartilhados com a
	/// cadeia do soco e com bancada propria. Copia-los aqui nao provaria nada a mais e daria uma
	/// terceira casa pra mesma formula -- que e a regra 4 da casa, valendo tambem pra teste.
	/// ===========================================================================================
	/// </summary>
	private static double DanoPelaContaDoDm(double mods, double baseDano, double maxDano,
											double bpDoTiro, CombatState alvo, bool bloqueando)
	{
		Fighter f = alvo.F;

		double cima = mods * 6 * DanoDeKi.DanoGlobalDeKi;
		double baixo = f.Ekidef * f.Ekidef * Math.Max(f.Etechnique, f.Ekiskill);
		if (baixo <= 0) baixo = 1;

		double dmg = cima / baixo * baseDano;
		if (maxDano > 0) dmg = Math.Min(dmg, maxDano);
		if (dmg == 0) dmg = baseDano * DanoDeKi.FracaoDoPiso;

		dmg = CombatMath.Armadura(dmg, f.Esuperkiarmor);

		double log4 = Math.Log(Math.Max(f.kidefenseskill, 4)) / Math.Log(4);
		dmg /= log4;
		if (bloqueando) dmg /= 2 * log4;

		dmg = CombatMath.Resistencia(dmg, DanoDeKi.TiposEnergia, alvo.Resistencias);
		return Math.Max(dmg * CombatMath.BpModulus(bpDoTiro, f.expressedBP), 0);
	}

	/// <summary>
	/// COMO ELA REPROVA: mexa em qualquer degrau da cadeia de ki -- tire o quadrado do `Ekidef`,
	/// troque a ordem da armadura com a pericia, esqueca de dobrar a pericia no bloqueio -- e as
	/// linhas da grade divergem, dizendo os dois numeros. Enfie o raio no funil do SOCO (o atalho
	/// barato) e a linha do quadrado reprova sozinha.
	/// </summary>
	private void ODanoContraAFormulaDoOriginal()
	{
		GD.Print("[kiponta] -- 3) O DANO, MEDIDO CONTRA A CONTA DO `objects.dm`");

		// A GRADE cobre os degraus que a cadeia tem: pericia de defesa, armadura, guarda, teto de
		// dano, desnivel de poder nos dois sentidos e o piso de 2%.
		(string oque, Action<Fighter> tempero, double mods, double baseDano, double maxDano,
		 double bp, bool guarda)[] casos =
		[
			("cru, sem tempero nenhum",            _ => { },                             1, 1, 0, 500, false),
			("com pericia de defesa de ki alta",   f => f.kidefenseskill = 64,           1, 1, 0, 500, false),
			("com armadura de ki",                 f => f.Esuperkiarmor = 30,            1, 1, 0, 500, false),
			("de guarda erguida (a pericia conta de novo)", f => f.kidefenseskill = 16,  1, 1, 0, 500, true),
			("com teto de dano (`maxdamage`)",     _ => { },                             50, 1, 3, 500, false),
			("contra alguem 10x mais fraco",       _ => { },                             1, 1, 0, 50_000, false),
			("contra alguem 10x mais forte",       _ => { },                             1, 1, 0, 50, false),
			("potencia no piso (0,1 da loja)",     _ => { },                             1, 0.1, 0, 500, false),
		];

		foreach ((string oque, Action<Fighter> tempero, double mods, double baseDano,
				  double maxDano, double bp, bool guarda) in casos)
		{
			ServerPlayer alvo = Forjar("Cobaia", CorredorLivre(3), bp: 500);
			tempero(alvo.Ficha);
			CombatState cd = alvo.Combate!;

			double producao = DanoDeKi.Final(mods, baseDano, maxDano, bp, cd, guarda);
			double replica = DanoPelaContaDoDm(mods, baseDano, maxDano, bp, cd, guarda);

			AfirmarPp($"o dano {oque} bate com a conta do DM",
					  Math.Abs(producao - replica) < 1e-9 * Math.Max(1, Math.Abs(replica)),
					  $"producao {producao:0.######} contra DM {replica:0.######}");
		}
		LimparTudoDaBancada();

		// ---- E A CADEIA E A DE KI, NAO A DO SOCO ----
		var molde = new Fighter { Race = "Human", BP = 500 };
		molde.Statify();
		double d1 = DanoDeKi.Bruto(1, 1, 0, molde);
		molde.Ekidef *= 2;
		double d2 = DanoDeKi.Bruto(1, 1, 0, molde);
		AfirmarPp("dobrar `Ekidef` divide o dano de ki por ~4 -- o QUADRADO, que e o que separa as duas cadeias",
				  Math.Abs(d1 / d2 - 4) < 0.01, $"razao {d1 / d2:0.###}");

		// ---- O MESMO QUADRADO, AGORA MEDIDO NO CORPO, AO VIVO ----
		// A grade acima compara duas contas; esta compara VIDA PERDIDA depois de o tiro nascer,
		// voar, colidir e passar pelo `AplicarDanoPronto`. Quarenta tiros em cada um porque o membro
		// atingido e SORTEADO -- com um tiro so, mediria-se o sorteio.
		double perdaFraca = VidaPerdidaEmTiros(fatorDeDefesa: 1, tiros: 40);
		double perdaForte = VidaPerdidaEmTiros(fatorDeDefesa: 2, tiros: 40);
		GD.Print($"[kiponta]      vida perdida em 40 tiros: defesa 1x -> {perdaFraca:0.##}, "
				 + $"defesa 2x -> {perdaForte:0.##} (razao {perdaFraca / Math.Max(perdaForte, 1e-9):0.##})");
		AfirmarPp("ao vivo: quem tem o DOBRO de defesa de ki perde ~4x menos vida nos mesmos 40 tiros",
				  perdaFraca > 0 && perdaForte > 0 && perdaFraca / perdaForte > 3
				  && perdaFraca / perdaForte < 5,
				  $"{perdaFraca:0.###} contra {perdaForte:0.###} (razao {perdaFraca / Math.Max(perdaForte, 1e-9):0.##})");

		// ---- A MIRA QUE VALE E A DE QUEM ATIRA ----
		// Varredura de fonte porque a resposta e sobre QUEM: o membro e sorteado, entao um teste de
		// comportamento mediria o dado. No `MeleeResolver` a zona vem do ATACANTE (`a.ZonaMirada`,
		// `MeleeResolver.cs:152`) e o tiro tem que perguntar a mesma coisa -- ele passava
		// `cd.ZonaMirada`, que e a mira da VITIMA: mirar na cabeca com um raio nao fazia nada, e
		// quem escolhia onde levar o tiro era quem apanhava.
		// A CHAMADA E LIDA COMO UMA LINHA SO -- o corpo vem quebrado em varias linhas de fonte, e a
		// primeira versao desta checagem procurava o argumento numa linha que so tinha a metade da
		// chamada. Juntar o corpo inteiro num texto e o que faz a busca ser sobre o CODIGO e nao
		// sobre a formatacao dele.
		string[] acertar = CorpoDoMetodo(Fonte("Server/GameServer.Projeteis.cs"), "private bool Acertar(Projetil");
		string tudo = string.Join(" ", acertar.Select(l => l.Trim()));
		var chamada = System.Text.RegularExpressions.Regex.Match(tudo, @"AplicarDanoPronto\s*\(([^)]*)\)");
		AfirmarPp("o corpo do `Acertar` foi lido do disco e ele chama o funil de dano do soco",
				  chamada.Success, $"{acertar.Length} linhas lidas");

		string argumentos = chamada.Success ? chamada.Groups[1].Value : "";
		AfirmarPp("a regiao mirada que vale e a de QUEM ATIRA (`dono`), como no soco -- e nao a da vitima",
				  argumentos.Contains("dono.") && !argumentos.Contains("cd.ZonaMirada"),
				  argumentos.Trim());
	}

	/// <summary>
	/// N TIROS IGUAIS NUM CORPO, e quanta vida eles somam. Tudo por producao: `Disparar` +
	/// `TickDosProjeteis` + `Acertar`. Sem deflexao (`Deflectivel = false`) porque o que se mede
	/// aqui e o DANO, e a deflexao e um sorteio com familia propria na `--projetilteste`.
	///
	/// ============================ TRES COISAS CONGELADAS, E CADA UMA JA TORCEU A MEDIDA ============================
	///   * a PERICIA de defesa de ki sobe a cada tiro levado (`kidefensecounter`) -- sem congelar,
	///     o ultimo tiro doeria menos que o primeiro por uma razao que nao e a que se mede;
	///   * o CORPO e restaurado entre tiros: membro no chao SATURA (nao da pra perder vida que ja
	///     foi), e a saturacao comprime justamente a razao que a familia quer ler -- foi o que a
	///     primeira rodada mediu, 1,69 no lugar de ~4;
	///   * o NOCAUTE, pelo mesmo motivo: quem cai deixa de ser o mesmo alvo.
	/// ==========================================================================================================
	/// </summary>
	private double VidaPerdidaEmTiros(double fatorDeDefesa, int tiros)
	{
		Vec2 raia = CorredorLivre(20);
		ServerPlayer atirador = Forjar("Metralhador", raia, bp: 5_000);
		atirador.Facing = Facing.East;
		ServerPlayer alvo = Forjar("Saco", new Vec2(raia.X + 160, raia.Y), bp: 5_000);
		alvo.Ficha.Ekidef *= fatorDeDefesa;

		double periciaFixa = alvo.Ficha.kidefenseskill;
		double defesaFixa = alvo.Ficha.Ekidef;

		double somaDasPerdas = 0;
		for (int i = 0; i < tiros; i++)
		{
			alvo.Combate!.Corpo.Restaurar();
			alvo.Combate.SincronizarVida();
			double antes = alvo.Combate.Corpo.Vida();

			Projetil p = Disparar(atirador, new ReceitaDeProjetil
			{
				Tipo = TipoDeProjetil.Blast, BaseDano = 1, Velocidade = 5,
				AlcanceTiles = 20, Deflectivel = false, Nome = "tiro de bancada",
			});
			for (int k = 0; k < 120 && p.Vivo; k++) TickDosProjeteis(Protocol.TickSeconds);

			somaDasPerdas += antes - alvo.Combate.Corpo.Vida();
			alvo.Ficha.kidefenseskill = periciaFixa;
			alvo.Ficha.Ekidef = defesaFixa;
			alvo.Ficha.KO = false;
		}
		LimparTudoDaBancada();
		return somaDasPerdas;
	}

	// =====================================================================
	// 4) OS TRES TIPOS VOAM DIFERENTE
	// =====================================================================
	/// <summary>
	/// COMO ELA REPROVA: iguale `AtrasoDeRaio` a `AtrasoDeBola` e o raio passa a rastejar -- a
	/// primeira linha fica vermelha com os dois numeros. Tire o `walk_towards` do teleguiado (o
	/// recalculo de rumo todo tique) e ele perde a presa que foge, enquanto a BOLA -- que e o
	/// contra-exemplo, e existe pra isso -- continua nao acertando.
	/// </summary>
	private void OsTresTiposVoamDiferente()
	{
		GD.Print("[kiponta] -- 4) OS TRES TIPOS VOAM DIFERENTE, E O TELEGUIADO PERSEGUE");

		// ---- AS DUAS ESCALAS DO DM, QUE NAO SAO A MESMA ----
		// bola/teleguiado: `lag = max(1, round(4-speed))` tiques de 0,1 s -> 0,3 s por tile no 1.
		// raio:            `beamspeed = 1/speed` tiques de 0,1 s          -> 0,1 s por tile no 1.
		AfirmarPp("com `speed = 1` a bola leva 3x o tempo do raio por tile (as duas escalas do DM)",
				  Math.Abs(Projetil.AtrasoDeBola(1) / Projetil.AtrasoDeRaio(1) - 3) < 1e-6,
				  $"bola {Projetil.AtrasoDeBola(1):0.###}s, raio {Projetil.AtrasoDeRaio(1):0.###}s");
		AfirmarPp("...e a velocidade 5 da loja acelera os dois, cada um na sua escala",
				  Projetil.AtrasoDeBola(5) < Projetil.AtrasoDeBola(1)
				  && Projetil.AtrasoDeRaio(5) < Projetil.AtrasoDeRaio(1));

		// ============================ MEDIDO NO AR: MEIO SEGUNDO DE CADA UM ============================
		// O RAIO PRECISA DE UM CANAL PRA EXISTIR, e isto e regra e nao detalhe de bancada: um beam
		// solto por `Disparar` nasce com a cauda EM CIMA da cabeca, e a primeira volta do tique le
		// isso como "o rastro acabou de ser engolido" e o apaga (`AndarProjetil`, ramo (c)). A
		// primeira versao desta familia mediu exatamente isso -- "o raio andou 0 px" -- e a resposta
		// certa nao era relaxar o numero: era passar pela porta pela qual raio nasce.
		// ==========================================================================================
		float noAr = Voo.AlturaQueAtravessa + 1;   // acima do cenario: o que se mede aqui e a VELOCIDADE
		double andouBola, andouRaio;
		{
			Vec2 raia = CorredorLivre(30);
			ServerPlayer pl = Forjar("Cronometro", raia, bp: 20_000);
			pl.Facing = Facing.East;
			pl.Altitude = noAr;
			pl.Ficha.Ki = pl.Ficha.MaxKi;

			Projetil bola = Disparar(pl, new ReceitaDeProjetil
			{ Tipo = TipoDeProjetil.Blast, Velocidade = 1, AlcanceTiles = 60 });
			float x0 = bola.Pos.X;
			for (int i = 0; i < 15; i++) TickDosProjeteis(Protocol.TickSeconds);
			andouBola = bola.Pos.X - x0;

			ServerPlayer raiador = Forjar("Cronometro2", CorredorLivre(30), bp: 20_000);
			raiador.Facing = Facing.East;
			raiador.Altitude = noAr;
			raiador.Ficha.Ki = raiador.Ficha.MaxKi;
			Canalizar(raiador, "Ki_Wave", 10 * raiador.Ficha.BaseDrain(), new ReceitaDeProjetil
			{ Tipo = TipoDeProjetil.Beam, Velocidade = 1, AlcanceTiles = 60, CargaMinima = 1 });

			for (int i = 0; i < 300 && _canais.GetValueOrDefault(raiador.Id)?.Raio == null; i++)
			{
				TickDosCanaisDeKi(Protocol.TickSeconds);
				TickDosProjeteis(Protocol.TickSeconds);
			}
			Projetil? raio = _canais.GetValueOrDefault(raiador.Id)?.Raio;
			float y0 = raio?.Pos.X ?? 0;
			for (int i = 0; i < 15; i++)
			{
				TickDosCanaisDeKi(Protocol.TickSeconds);
				TickDosProjeteis(Protocol.TickSeconds);
			}
			andouRaio = (raio?.Pos.X ?? 0) - y0;
			AfirmarPp("o raio existe (ele so nasce de um CANAL -- ver a nota acima)", raio is { Vivo: true });
		}
		GD.Print($"[kiponta]      meio segundo de voo: raio {andouRaio:0} px, bola {andouBola:0} px");
		AfirmarPp("em meio segundo de voo o RAIO anda ~3x o que a BOLA anda -- medido, nao tabelado",
				  andouRaio / Math.Max(andouBola, 1e-6) > 2.5,
				  $"raio {andouRaio:0} px, bola {andouBola:0} px");
		LimparTudoDaBancada();

		// ---- O TELEGUIADO PERSEGUE, E A BOLA NAO ----
		bool guiadoAcertou = TiroContraQuemFoge(TipoDeProjetil.Guided, out Vec2 fimGuiado);
		bool bolaAcertou = TiroContraQuemFoge(TipoDeProjetil.Blast, out Vec2 fimBola);
		AfirmarPp("o TELEGUIADO alcanca quem sai da linha", guiadoAcertou, $"parou em {fimGuiado}");
		AfirmarPp("...e a BOLA, na MESMA fuga, nao alcanca (sem este contra-exemplo a linha de cima nao vale)",
				  !bolaAcertou, $"parou em {fimBola}");

		// ---- E SO O RAIO CANALIZA (e so ele prende os pes) ----
		ServerPlayer canal = Forjar("Canalizador", CorredorLivre(10), bp: 50_000);
		canal.Ficha.Ki = canal.Ficha.MaxKi;
		Canalizar(canal, "Ki_Wave", 10 * canal.Ficha.BaseDrain(), new ReceitaDeProjetil
		{ Tipo = TipoDeProjetil.Beam, CargaMinima = 1, AlcanceTiles = 20 });
		AfirmarPp("o RAIO abre canal e planta os pes pelo funil de sempre (`PodeMexerOCorpo`)",
				  _canais.ContainsKey(canal.Id) && !PodeMexerOCorpo(canal));
		SoltarCanal(canal, "Ki_Wave");

		ServerPlayer solta = Forjar("Arremessador", CorredorLivre(10), bp: 50_000);
		solta.Ficha.Ki = solta.Ficha.MaxKi;
		Disparar(solta, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast });
		Disparar(solta, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Guided });
		AfirmarPp("...e nem a bola nem o teleguiado abrem canal ou prendem quem atirou",
				  !_canais.ContainsKey(solta.Id) && PodeMexerOCorpo(solta));

		LimparTudoDaBancada();
	}

	/// <summary>Um tiro contra um alvo que FOGE transversalmente. Devolve se acertou.</summary>
	private bool TiroContraQuemFoge(TipoDeProjetil tipo, out Vec2 ondeParou)
	{
		float noAr = Voo.AlturaQueAtravessa + 1;
		Vec2 pista = CorredorLivre(4);
		ServerPlayer atirador = Forjar("Cacador", pista, bp: 5_000);
		atirador.Facing = Facing.East;
		atirador.Altitude = noAr;
		ServerPlayer presa = Forjar("Presa", new Vec2(pista.X + 400, pista.Y), bp: 5_000);
		presa.Altitude = noAr;

		// `Deflectivel = false` PELA RAZAO DO `RaioDaBancada` (`GameServer.ProjeteisTeste.cs:2167`): o
		// que se mede aqui e a PERSEGUICAO, e o `Acertar` sorteia deflexao em todo impacto contra quem
		// esta de pe (`GameServer.Projeteis.cs:1190-1240`) -- com 5.000 contra 5.000 e `base_damage` 15
		// da 0,0044% por impacto, e um `Defletido` no fim da caca viraria "o teleguiado nao alcancou".
		// Vale pros DOIS tipos: o teleguiado e o contra-exemplo passam pela mesma receita de proposito.
		Projetil p = Disparar(atirador, new ReceitaDeProjetil
		{ Tipo = tipo, BaseDano = 15, Velocidade = 1, AlcanceTiles = 60, Deflectivel = false });
		if (tipo == TipoDeProjetil.Guided) p.Alvo = presa.Id;
		p.VidaRestante = 60;

		for (int i = 0; i < 900 && p.Vivo; i++)
		{
			presa.Pos = new Vec2(presa.Pos.X, presa.Pos.Y + 3f);
			TickDosProjeteis(Protocol.TickSeconds);
		}
		ondeParou = p.Pos;
		bool acertou = p.Fim == FimDeProjetil.Acertou;
		LimparTudoDaBancada();
		return acertou;
	}

	// =====================================================================
	// 5) NAO EXISTE ALAVANCA DE ACERTO NO FIO
	// =====================================================================
	/// <summary>
	/// O SERVIDOR DECIDE O ACERTO -- e a prova forte disso nao e um pacote testado, e o CONJUNTO.
	///
	/// ============================ POR QUE ISTO NAO PODE SER UMA LISTA DE MENTIRAS ============================
	/// O `RoboDeKi` mente ao vivo -- teleporta, pede tecnica que nao tem, aperta letra de embate
	/// fora de embate -- e cada mentira dela e verdadeira e nenhuma protege o amanha: a proxima
	/// alavanca vai ser um opcode que ainda nao existe. Um dia alguem acrescenta `C2S.AcerteiComOKi`
	/// pra "resolver o lag do projetil", as mentiras de hoje continuam sendo recusadas, e o jogo
	/// passa a aceitar dano do cliente.
	///
	/// Entao a afirmacao e sobre o conjunto, e sao tres:
	///   (a) OS OPCODES -- nenhum dos que existem fala de acerto, dano, vida ou projetil, e a
	///       LISTA deles esta escrita PELO NOME: um opcode novo obriga a passar por esta linha e
	///       argumentar, e a reprovacao diz qual foi;
	///   (b) O CLIENTE NAO CONHECE A CADEIA -- nada em `Client/` menciona `DanoDeKi`,
	///       `MeleeResolver`, `AplicarDanoPronto` ou o tique dos projeteis;
	///   (c) O TIRO NASCE DE UM VERBO, e o verbo nao carrega numero nenhum: o que o cliente manda e
	///       um NOME, e todos os numeros (dano, mods, BP, alcance) sao lidos do lado de ca.
	/// ==================================================================================================
	///
	/// COMO ELA REPROVA: acrescente qualquer valor ao `enum C2S` (mesmo inocente) e (a) reprova ate
	/// alguem escrever o motivo aqui; faca o cliente chamar `DanoDeKi` (pra "prever o dano na HUD")
	/// e (b) reprova; faca o `Disparar` ler um numero que veio do pacote e (c) reprova.
	/// </summary>
	private void NaoHaAlavancaDeAcertoNoFio()
	{
		GD.Print("[kiponta] -- 5) O SERVIDOR DECIDE O ACERTO: NAO HA ALAVANCA NO FIO");

		// ---- (a) O CONJUNTO DE OPCODES ----
		// ============================ PELO NOME, E NAO PELA CONTAGEM ============================
		// Esta trava era `const int OpcodesDeHoje = 23`, e ficou vermelha com o protocolo certo: o
		// `C2S.Voz` (a voz por proximidade) entrou no MESMO commit que escreveu esta familia
		// ("Grande Update parte 3", 2449619) e ninguem passou por aqui pra argumentar. A
		// `--formasteste` ficou vermelha pelo mesmo opcode e foi acertada em 03f18e6
		// (`GameServer.FormasTeste.cs:1941`). O vermelho era JUSTO -- e pra isso que a trava existe --,
		// mas o numero tinha dois defeitos:
		//   * ele nao dizia QUEM chegou: "achei 24" obriga a contar a lista na mao;
		//   * ele era CEGO A TROCA: apagar um opcode e criar outro no mesmo commit mantem a conta e
		//     passa. Um `C2S.Resultado` no lugar do `Tech` entraria calado -- nenhuma palavra proibida nele.
		//
		// Agora a trava e a LISTA, escrita a mao, com o motivo de cada grupo nao carregar acerto. Os
		// valores sao TIPADOS: apagar ou renomear um opcode quebra a COMPILACAO desta linha, e um
		// opcode NOVO aparece pelo nome no detalhe da reprovacao. E a conta le o enum COMPILADO, e nao
		// o texto do `Net/Protocol.cs` por regex: a regex so via membro com `= numero` escrito, e um
		// `AcerteiComOKi,` sem valor explicito passaria por baixo dela.
		// ======================================================================================
		Protocol.C2S[] argumentados =
		[
			// SESSAO: conta, slot, criar, apagar (o nome digitado e conferido pelo SERVIDOR), relogio
			Protocol.C2S.Login, Protocol.C2S.PickSlot, Protocol.C2S.CreateChar, Protocol.C2S.DeleteChar,
			Protocol.C2S.Ping,
			// O CORPO: posicao que o servidor CONFERE (`ValidateStep`), treino, guarda, zona mirada,
			// segurar C e o ponto do Zanzoken (que o servidor encurta ao alcance e cobra)
			Protocol.C2S.InputState, Protocol.C2S.Activity, Protocol.C2S.Guard, Protocol.C2S.Aim,
			Protocol.C2S.Carregar, Protocol.C2S.Zanzoken,
			// INTENCOES SEM NUMERO: "golpeie" (quem resolve e o `MeleeResolver`), "use pelo NOME" (a (c)
			// abaixo), "transforme", "trave neste alvo", "lute pra valer" -- o resultado e do servidor
			Protocol.C2S.Action, Protocol.C2S.Habilidade, Protocol.C2S.Transformar, Protocol.C2S.Alvo,
			Protocol.C2S.Lethal,
			// INTERFACE, por nome ou texto: comprar skill, cargo, tecnologia, estilo, verbos
			Protocol.C2S.Aprender, Protocol.C2S.Cargo, Protocol.C2S.Tech, Protocol.C2S.Estilo,
			Protocol.C2S.Verbo,
			// a LETRA do QTE vai crua: quem confere se bate e quem sorteou (ver o cabecalho dela)
			Protocol.C2S.ClashTecla,
			// A BOCA: texto e quadros de Opus. A voz nao tem nem campo de destino (ver o cabecalho dela
			// em `Net/Protocol.cs`) -- nao ha numero ali que chegue perto de um tiro
			Protocol.C2S.Chat, Protocol.C2S.Voz,
		];
		static List<string> SemArgumento(Protocol.C2S[] lista) =>
			[.. Enum.GetValues<Protocol.C2S>().Where(c => Array.IndexOf(lista, c) < 0).Select(c => c.ToString())];

		string[] opcodes = Enum.GetNames<Protocol.C2S>();
		List<string> novos = SemArgumento(argumentados);
		AfirmarPp($"os {opcodes.Length} opcodes que o cliente tem pra dizer estao TODOS argumentados aqui, pelo nome",
				  novos.Count == 0, $"sem argumento: {string.Join(", ", novos)}");

		// O CONTRA-EXEMPLO, rodado e nao so descrito: a MESMA conta sobre a lista de antes da voz tem
		// que reprovar DIZENDO o nome -- que e exatamente o que o numero nao fazia ("achei 24").
		List<string> antesDaVoz = SemArgumento([.. argumentados.Where(c => c != Protocol.C2S.Voz)]);
		AfirmarPp("CONTRA-EXEMPLO: com a lista de antes da voz, a mesma conta REPROVA e diz `Voz` pelo nome",
				  antesDaVoz.Count == 1 && antesDaVoz[0] == nameof(Protocol.C2S.Voz), string.Join(", ", antesDaVoz));

		string[] proibidas = ["acert", "dano", "dmg", "hit", "morte", "matar", "vida", "projetil",
							  "tiro", "kill", "damage"];
		List<string> suspeitos =
			[.. opcodes.Where(o => proibidas.Any(p => o.Contains(p, StringComparison.OrdinalIgnoreCase)))];
		AfirmarPp("...e nenhum deles fala de acerto, dano, vida, morte ou projetil",
				  suspeitos.Count == 0, string.Join(", ", suspeitos));

		// ---- (b) O CLIENTE NAO CONHECE A CADEIA DE DANO ----
		string[] cadeia = ["DanoDeKi", "MeleeResolver", "AplicarDanoPronto", "TickDosProjeteis",
						   "ChanceDeDeflexao", "PoderDeSegurar"];
		var contaminados = new List<string>();
		string pastaCliente = ProjectSettings.GlobalizePath("res://Client");
		if (System.IO.Directory.Exists(pastaCliente))
			foreach (string arq in System.IO.Directory.EnumerateFiles(pastaCliente, "*.cs"))
				foreach (string cru in System.IO.File.ReadAllLines(arq))
				{
					string l = SemTextoNemComentario(cru);
					if (cadeia.Any(c => l.Contains(c, StringComparison.Ordinal)))
						contaminados.Add($"{System.IO.Path.GetFileName(arq)}: {l.Trim()}");
				}
		AfirmarPp("nada em `Client/` conhece a cadeia de dano de ki nem o tique dos projeteis",
				  contaminados.Count == 0, string.Join(" | ", contaminados.Take(3)));

		// ---- (c) O TIRO NASCE DE UM NOME, NAO DE UM NUMERO ----
		// `C2S.Habilidade` carrega uma string (o id do verbo) e nada mais; `C2S.Verbo` carrega
		// comando + argumento de texto. Nenhum dos dois pode dizer quanto doi.
		// A ASSINATURA E CASADA POR PREFIXO, e nao pela linha inteira. Quando o arsenal nomeado
		// (lote G5) precisou disparar de outro lugar e pra outro lado, o `Disparar` ganhou dois
		// parametros opcionais e a assinatura quebrou em duas linhas -- e esta checagem passou a
		// devolver ZERO linhas, reprovando por nao achar o metodo em vez de por achar defeito nele.
		// O prefixo curto e o que os outros cinco chamadores do `CorpoDoMetodo` ja usavam
		// (`"private void Input(NetPeer"`), e ele sobrevive ao proximo parametro.
		string[] corpoDisparar = CorpoDoMetodo(Fonte("Server/GameServer.Projeteis.cs"),
											   "private Projetil Disparar(ServerPlayer pl");
		AfirmarPp("o corpo do `Disparar` foi lido do disco", corpoDisparar.Length > 10,
				  $"{corpoDisparar.Length} linhas");
		AfirmarPp("...e todo numero do tiro sai do ATIRADOR e da RECEITA -- nunca de um `reader`",
				  !corpoDisparar.Any(l => l.Contains("reader", StringComparison.OrdinalIgnoreCase)
										|| l.Contains("GetFloat") || l.Contains("GetInt")),
				  string.Join(" | ", corpoDisparar.Where(l => l.Contains("Get")).Take(2)));

		// ---- E A ULTIMA PORTA: MESMO CHAMANDO O DISPARO NA MAO, O TETO VALE ----
		// Quem manda o pacote na mao entra pelo `UsarHabilidade` -> `UsarTecnica`, e nao pelo
		// `Disparar`. Mas a bancada chama `Disparar` direto, entao ela e a prova de que a porta de
		// baixo tambem tem tranca -- ver a familia 8.
		ServerPlayer mentiroso = Forjar("Falsario", CorredorLivre(4), bp: 5_000);
		EscutaDeAvisos = [];
		UsarHabilidade(mentiroso, "Custom_Attack7");
		AfirmarPp("pedir uma tecnica customizada que nao existe nao cria projetil nenhum",
				  ProjeteisDaZona(mentiroso.Zone.Hash).Count == 0
				  && EscutaDeAvisos.Exists(a => a.Contains("nao tem uma tecnica", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", EscutaDeAvisos));

		EscutaDeAvisos.Clear();
		UsarHabilidade(mentiroso, "Ki_Wave");
		AfirmarPp("...e pedir uma tecnica de SKILL sem ter a skill ouve \"voce nao sabe\" e nao atira",
				  ProjeteisDaZona(mentiroso.Zone.Hash).Count == 0 && !_canais.ContainsKey(mentiroso.Id)
				  && EscutaDeAvisos.Exists(a => a.Contains("nao sabe", StringComparison.OrdinalIgnoreCase)),
				  string.Join(" | ", EscutaDeAvisos));
		EscutaDeAvisos = null;

		LimparTudoDaBancada();
	}

	// =====================================================================
	// 6) A COLISAO: NOS DOIS SENTIDOS, E O EMPATE
	// =====================================================================
	/// <summary>
	/// O pedido do dono foi *"ki EMPURRANDO o outro ate atingir o inimigo ou vc"* -- as duas
	/// pontas da frase. Entao a familia mede a MESMA disputa tres vezes: com o forte de um lado,
	/// com o forte do outro, e com os dois iguais.
	///
	/// COMO ELA REPROVA: faca `EmbateDeKi.Deslocamento` devolver 0 e o encontro para de andar (a
	/// cena que o dono pediu deixa de existir, e o dano continua saindo -- ou seja, so esta linha
	/// pega); tire o piso `1/CAP` da `Vantagem` (escrevendo `max(a/b, 1)`, que e a tentacao) e a
	/// deriva nunca sai de zero: os dois sentidos empatam e as tres linhas ficam vermelhas juntas.
	/// </summary>
	private void AColisaoNosDoisSentidos()
	{
		GD.Print("[kiponta] -- 6) A COLISAO DE KI: OS DOIS SENTIDOS, E O EMPATE");

		// ---- SENTIDO 1: O SEGUNDO E MUITO MAIS FORTE ----
		(ServerPlayer a1, ServerPlayer b1, DisputaDeKi? d1) =
			DoisRaiosDeFrente(tiles: 12, bpDoSegundo: 50_000 * 40, tecladoNoSegundo: true);
		AfirmarPp("dois raios de frente comecam uma disputa (o gatilho de producao, dentro do tique)",
				  d1 != null);
		if (d1 != null)
		{
			Vec2 partiu = d1.Ponto;
			double doForteAntes = MedidorDe(d1, b1);
			for (int i = 0; i < 30 * 4 && _disputas.Contains(d1); i++) TickDosEmbatesDeKi(Protocol.TickSeconds);

			AfirmarPp("o mais FORTE empurra sozinho, sem ninguem apertar tecla (a deriva do DM)",
					  MedidorDe(d1, b1) > doForteAntes,
					  $"o forte estava em {doForteAntes:0.#} e foi pra {MedidorDe(d1, b1):0.#}");
			AfirmarPp("...e o PONTO DE ENCONTRO ANDA -- em direcao a quem esta perdendo",
					  (d1.Ponto - partiu).Length > 4
					  && (d1.Ponto - a1.Pos).Length < (partiu - a1.Pos).Length,
					  $"partiu de {partiu}, esta em {d1.Ponto}, o fraco em {a1.Pos}");
			AfirmarPp("...e as cabecas dos DOIS feixes acompanham o encontro (o ki empurrando)",
					  d1.A.Feixe != null && (d1.A.Feixe.Pos - d1.Ponto).Length < ZoneCollision.TileSize * 2,
					  $"cabeca em {d1.A.Feixe?.Pos}, encontro em {d1.Ponto}");
		}
		LimparEmbatesDaBancada();

		// ============================ SENTIDO 2: AGORA O FORTE E O OUTRO ============================
		// Sem esta metade, um sinal trocado na deriva passaria verde: "o medidor sempre cai" e
		// indistinguivel de "o mais forte ganha" quando so se mede um lado.
		//
		// E O MEDIDOR E LIDO PELO LADO, e nao pelo campo cru: quem vira `d.A` e quem o TIQUE encontrar
		// primeiro na lista de projeteis, e nao quem a bancada criou primeiro. A primeira versao lia
		// `d.Medidor` direto e reprovou aqui -- medindo a ordem da lista, nao a fisica.
		// ========================================================================================
		(ServerPlayer a2, ServerPlayer _, DisputaDeKi? d2) =
			DoisRaiosDeFrente(tiles: 12, bpDoSegundo: 50_000 / 40.0, tecladoNoSegundo: true);
		if (d2 != null)
		{
			Vec2 partiu = d2.Ponto;
			for (int i = 0; i < 30 * 4 && _disputas.Contains(d2); i++) TickDosEmbatesDeKi(Protocol.TickSeconds);
			AfirmarPp("com o forte do OUTRO lado o medidor anda pro outro lado (a deriva tem sinal)",
					  MedidorDe(d2, a2) > EmbateDeKi.MedidorInicial,
					  $"o forte esta em {MedidorDe(d2, a2):0.#}");
			AfirmarPp("...e o encontro caminha pra LONGE de quem esta ganhando",
					  (d2.Ponto - a2.Pos).Length > (partiu - a2.Pos).Length,
					  $"partiu de {partiu}, esta em {d2.Ponto}, o forte em {a2.Pos}");
		}
		else AfirmarPp("a disputa do sentido inverso comecou", false, "gatilho nao disparou");
		LimparEmbatesDaBancada();

		// ---- FORCAS IGUAIS: NINGUEM EMPURRA, E O PRAZO DECIDE PELO EMPATE ----
		(ServerPlayer _, ServerPlayer _, DisputaDeKi? d3) = DoisRaiosDeFrente(tiles: 12);
		if (d3 != null)
		{
			AfirmarPp("com forcas IGUAIS as duas vantagens valem 1 (e por isso a deriva e zero)",
					  Math.Abs(d3.A.Vantagem - 1) < 1e-6 && Math.Abs(d3.B.Vantagem - 1) < 1e-6,
					  $"{d3.A.Vantagem:0.###} x {d3.B.Vantagem:0.###}");

			Vec2 partiu = d3.Ponto;
			// NINGUEM APERTA NADA: os dois tem "teclado" e nenhuma letra e respondida.
			for (int i = 0; i < 30 * 6 && _disputas.Contains(d3); i++) TickDosEmbatesDeKi(Protocol.TickSeconds);
			AfirmarPp("...e o encontro NAO SAI DO LUGAR enquanto ninguem faz nada",
					  (d3.Ponto - partiu).Length < 2, $"andou {(d3.Ponto - partiu).Length:0.##} px");
			AfirmarPp("...e o medidor continua no meio",
					  Math.Abs(d3.Medidor - EmbateDeKi.MedidorInicial) < 1,
					  $"medidor {d3.Medidor:0.##}");
		}
		else AfirmarPp("a disputa de forcas iguais comecou", false, "gatilho nao disparou");

		// O DESFECHO DE FORCAS IGUAIS E O `draw()`: dentro da margem de 5, os dois estouram.
		AfirmarPp("no prazo, medidor no meio = EMPATE (os dois feixes estouram juntos)",
				  EmbateDeKi.Decidir(EmbateDeKi.MedidorInicial, EmbateDeKi.SegundosMaximos)
					== FimDeEmbateDeKi.Empate
				  && EmbateDeKi.Decidir(EmbateDeKi.MedidorInicial + EmbateDeKi.MargemDoEmpate + 1,
										EmbateDeKi.SegundosMaximos) == FimDeEmbateDeKi.VenceuA
				  && EmbateDeKi.Decidir(EmbateDeKi.MedidorInicial - EmbateDeKi.MargemDoEmpate - 1,
										EmbateDeKi.SegundosMaximos) == FimDeEmbateDeKi.VenceuB);
		LimparEmbatesDaBancada();

		// ---- E O CORPO FICA PRESO NOS DOIS LADOS, PELO FUNIL DE SEMPRE ----
		(ServerPlayer p1, ServerPlayer p2, DisputaDeKi? d4) = DoisRaiosDeFrente(tiles: 12);
		AfirmarPp("os DOIS corpos ficam plantados durante a disputa (`PodeMexerOCorpo`)",
				  d4 != null && !PodeMexerOCorpo(p1) && !PodeMexerOCorpo(p2));
		LimparEmbatesDaBancada();
		AfirmarPp("...e voltam a andar quando ela acaba", PodeMexerOCorpo(p1) && PodeMexerOCorpo(p2));
	}

	/// <summary>
	/// O MEDIDOR VISTO DO LADO DE <paramref name="quem"/> -- 100 quer dizer "eu engoli a disputa".
	///
	/// Existe porque `d.A` e `d.B` sao decididos pela ORDEM DA LISTA de projeteis no tique, e nao
	/// por quem a bancada criou primeiro: ler `d.Medidor` cru afirma sobre o lado errado metade das
	/// vezes -- e foi o que aconteceu na primeira rodada desta familia.
	/// </summary>
	private static double MedidorDe(DisputaDeKi d, ServerPlayer quem)
		=> d.A.Quem == quem ? d.Medidor : 100 - d.Medidor;

	// =====================================================================
	// 7) A IA ATIRA PELO FUNIL DO JOGADOR -- PARIDADE POR CONJUNTO
	// =====================================================================
	/// <summary>
	/// A `--iateste` ja faz esta varredura pro CORPO da IA (voar, aparar, carregar, transformar).
	/// Esta faz a mesma pergunta pro CANAL DE TIRO, que nasceu depois dela e por isso ficou de fora:
	///
	///   (a) tudo que o caminho de tiro da IA chama, o funil do jogador tambem chama;
	///   (b) o disparo em si (`Disparar`/`Canalizar`) NAO e alcancado pela IA por fora --
	///       ela so sabe pedir por NOME, como o jogador.
	///
	/// COMO ELA REPROVA: troque, no atuador ou no reflexo do contra-feixe, o `UsarTecnica`/
	/// `UsarHabilidade` por um `Disparar(...)` direto -- que e o atalho natural pra "fazer o NPC
	/// atirar sem precisar dar a skill pra ele" -- e as duas linhas ficam vermelhas na hora, mesmo
	/// que o NPC continue atirando lindamente na tela.
	/// </summary>
	private void AIaAtiraPeloFunilDoJogador()
	{
		GD.Print("[kiponta] -- 7) A IA ATIRA PELO FUNIL DO JOGADOR (paridade por CONJUNTO)");

		string[] fonteIa = Fonte("Server/GameServer.Ia.cs");
		string[] fonteEmbate = Fonte("Server/GameServer.EmbateDeKi.cs");
		string[] fonteServidor = Fonte("Server/GameServer.cs");
		string[] fonteRaciais = Fonte("Server/GameServer.Raciais.cs");
		AfirmarPp("os quatro fontes da paridade foram lidos do disco",
				  fonteIa.Length > 0 && fonteEmbate.Length > 0 && fonteServidor.Length > 0
				  && fonteRaciais.Length > 0);

		// O CAMINHO DE TIRO DA IA: o atuador (que despacha a tecnica escolhida) e o REFLEXO do
		// contra-feixe, que e o unico gesto de ki que nasce fora do cerebro.
		string[] tiroDaIa =
			[.. CorpoDoMetodo(fonteIa, "private void AplicarComando"),
			 .. CorpoDoMetodo(fonteEmbate, "private void TickDoContraFeixe")];
		AfirmarPp("o caminho de tiro da IA foi extraido (`AplicarComando` + `TickDoContraFeixe`)",
				  tiroDaIa.Length > 40, $"{tiroDaIa.Length} linhas");

		// O `Input` so desempacota o fio desde 2026-09-05 (8e438fe) -- o passo do InputState mora no
		// `AplicarInput`, e o funil do jogador e os dois. Mesma deriva que deixou as varreduras (a)/(b) da
		// `--iateste` vermelhas pelo motivo errado; ver la (secao 15, `AncoraDoPasso`).
		//
		// E O PASSO TEM QUE TER VINDO MESMO. So o tamanho do funil nao diria: o `Handle` sozinho ja passa de
		// 200 linhas, e um `AplicarInput` que mudasse de nome ou de visibilidade sairia VAZIO daqui com a
		// linha verde -- exatamente a deriva que esta leitura acabou de consertar. Entao o corpo do passo e
		// conferido pelo CONTEUDO: e o metodo que chama o `ValidateStep`.
		string[] passoDoJogador = CorpoDoMetodo(fonteServidor, "internal void AplicarInput(ServerPlayer");
		bool passoVeio = passoDoJogador.Any(l => l.Contains("ValidateStep"));
		string[] funilDoJogador =
			[.. CorpoDoMetodo(fonteServidor, "private void Handle(NetPeer"),
			 .. CorpoDoMetodo(fonteServidor, "private void Input(NetPeer"),
			 .. passoDoJogador,
			 .. CorpoDoMetodo(fonteRaciais, "private void UsarHabilidade(ServerPlayer")];
		AfirmarPp("o funil do jogador foi extraido (`Handle` + `Input` + `AplicarInput` + `UsarHabilidade`), "
				  + "e o `AplicarInput` e o passo de verdade (chama o `ValidateStep`)",
				  funilDoJogador.Length > 200 && passoVeio,
				  $"{funilDoJogador.Length} linhas; passo {passoDoJogador.Length} linhas{(passoVeio ? "" : ", SEM o `ValidateStep`")}");

		// ============================ AS EXCECOES, E CADA UMA TEM ARGUMENTO ============================
		// As duas primeiras vem da `--iateste`: o atuador chamando a outra metade de si mesmo
		// (`PassoDaIa`) e o facing derivado do rumo (`FacingFrom`), porque a IA nao tem pacote.
		//
		// O `Advance` e o `GetValueOrDefault` SAIRAM desta lista, e nao por faxina: nenhum dos dois e
		// chamado pelo caminho de tiro, nem no dia em que a lista nasceu. O `Advance` mora no
		// `PassoDaIa`, cujo corpo esta bancada NAO le -- veio junto na copia da lista da `--iateste`, que
		// le. Excecao que ninguem usa e porta aberta: um `npc.Pos = MoveRules.Advance(...)` escrito no
		// reflexo (o NPC "recuando um passo antes de responder o raio", por fora do `PodeMexerOCorpo`)
		// passaria calado por (a). O DEFEITO INJETADO mais abaixo e exatamente esse.
		//
		// As quatro seguintes sao do REFLEXO do contra-feixe, e sao PERGUNTAS -- a mesma fronteira que
		// a `--iateste` ja tinha desenhado ao deixar `LerCapacidades`/`LerPercepcao`/`ArsenalDeLonge`
		// de fora: *"uma leitura pode chamar o que quiser; o que nao pode e AGIR por fora"*.
		// `VemFeixeContraMim` olha a zona, `SabeTecnica` e literalmente a porta do jogador (ela e quem
		// responde "voce nao sabe"), e `ContainsKey`/`TryGetValue` sao consulta a dicionario -- nao ha
		// estado de corpo do outro lado delas.
		//
		// `FraseDoContraFeixe` chegou DEPOIS desta lista (a55b464, a camada de fala do `Falatorio`), e
		// ela NAO e leitura pura: ela COBRA o relogio de fala de combate (`Falatorio.DeCombate`, o
		// `ai_next_chat = world.time + cooldown` do `npc_combat_chat`, `NPCAI.dm:227-230`). Fica aqui
		// mesmo assim, e por tres razoes:
		//   * o relogio mora DENTRO do cerebro, e o cerebro nao tem maos: nada em `Core/Ai` conhece
		//     `ServerPlayer` nem `GameServer` (a (c) da `--iateste`). Do outro lado dela nao ha corpo
		//     nem mundo -- ha a pergunta "tem frase agora?" e a conta de quando pode haver outra;
		//   * o relogio TEM que ser o do cerebro: e ele que impede o grito do raio de sair por cima do
		//     grito de soco do mesmo segundo, e o DM usa o MESMO `ai_next_chat` nos dois (`:378` e
		//     `:419`). Uma lista de frases escrita no reflexo seria o segundo relogio que o cabecalho
		//     do `Falatorio` proibe;
		//   * o GESTO -- a frase chegando no chat -- e o `Falar` da linha seguinte, o mesmo do
		//     `case Protocol.C2S.Chat` (`GameServer.cs:3647`) e do passo 10 do `AplicarComando`, com
		//     raio de vista, grito e teto de 400 ms. Por isso o `Falar` nao aparece na reprovacao: ele
		//     ESTA no funil do jogador.
		// Mandar a frase pelo `Comando.Falar` (o caminho das outras falas da IA) nao seria mais certo,
		// seria mais tarde: o reflexo roda fora da decisao de 4 Hz de proposito (ver o cabecalho do
		// `TickDoContraFeixe`), e no DM o grito sai colado no tiro (`NPCAI.dm:378-379`).
		//
		// `EnraizadoPorKi` chegou em 2026-10-07, com a trava do `AplicarComando` (*"parece q eles as vezes
		// se mexem durante a colisao"*): com um raio na mao ou numa disputa, a IA nao voa, nao troca de
		// guarda, nao usa habilidade e nao soca. E outra PERGUNTA, e a mais magra da lista -- o corpo dela
		// e `_canais.ContainsKey(id)`, o MESMO dicionario que o `PodeAtirar` do jogador consulta pra dizer
		// *"voce ja esta com um raio na mao"*. E o que ela serve e uma porta que so FECHA: a trava tira
		// gestos da IA, nao lhe da nenhum. A contraprova de que ela trabalha e a `--tiroiateste` (a rodada
		// com `IaComAsMaosLivresDeTeste`, em que a IA larga a disputa em 1,4 s).
		// ==========================================================================================
		string[] excecoes = ["PassoDaIa", "FacingFrom",
							 "VemFeixeContraMim", "SabeTecnica", "ContainsKey", "TryGetValue",
							 "FraseDoContraFeixe", "EnraizadoPorKi"];

		HashSet<string> chamaIa = ChamadasDe(tiroDaIa);
		HashSet<string> chamaJogador = ChamadasDe(funilDoJogador);
		List<string> foraDoFunil =
			[.. chamaIa.Where(n => !chamaJogador.Contains(n) && Array.IndexOf(excecoes, n) < 0).OrderBy(n => n)];
		AfirmarPp($"(a) as {chamaIa.Count} funcoes do caminho de tiro da IA, o funil do jogador tambem chama",
				  foraDoFunil.Count == 0, string.Join(", ", foraDoFunil));

		// E AS EXCECOES TEM QUE ESTAR MESMO LA -- a regra da `--iateste` (secao 15, "as excecoes escritas existem de verdade"),
		// que esta copia nao tinha trazido e por isso carregou duas excecoes mortas desde que nasceu.
		AfirmarPp("...e cada excecao escrita e chamada de verdade pelo caminho de tiro (nenhuma porta esquecida)",
				  excecoes.All(chamaIa.Contains), string.Join(", ", excecoes.Where(e => !chamaIa.Contains(e))));

		// (b) O DISPARO NAO E ALCANCADO POR FORA. Esta e a que pega o atalho classico -- ela nao
		// depende de a funcao nova estar ou nao no funil do jogador: nascer projetil por fora do
		// nome da tecnica ja e o defeito.
		string[] portasDoTiro = ["Disparar", "Canalizar"];
		List<string> portaAberta = [.. portasDoTiro.Where(chamaIa.Contains)];
		AfirmarPp("(b) a IA nunca chama `Disparar`/`Canalizar` direto -- ela pede a tecnica pelo NOME",
				  portaAberta.Count == 0, string.Join(", ", portaAberta));
		AfirmarPp("...e ela pede pelo MESMO canal do jogador (`UsarHabilidade`/`UsarTecnica`)",
				  chamaIa.Contains("UsarHabilidade") || chamaIa.Contains("UsarTecnica"),
				  string.Join(", ", chamaIa.OrderBy(x => x).Take(12)));

		// ============================ E A PROVA DE QUE (a) E (b) REPROVAM ============================
		// Varredura de fonte verde nao prova nada enquanto ninguem viu o vermelho -- e a lista de
		// excecoes acabou de ganhar um nome. Entao o atalho e ESCRITO numa COPIA do reflexo, na
		// memoria, como no DEFEITO INJETADO da `--iateste` (secao 15, "E AGORA A PROVA DE QUE ISTO REPROVA"): um passo por
		// fora do `PassoDaIa` e um disparo por fora do nome da tecnica. Nada vai pro disco, e sem a
		// adulteracao as MESMAS contas sao as de cima, que deram vazio.
		// ==========================================================================================
		{
			int ondeEnfiar = Array.FindIndex(fonteEmbate, l => l.Contains("private void TickDoContraFeixe"));
			// A `{` E PROCURADA, e nao contada: se a assinatura quebrar em duas linhas (a do `Disparar` ja
			// quebrou, ver a familia 5), `ondeEnfiar + 2` cairia ANTES da chave -- fora do corpo que o
			// `CorpoDoMetodo` le -- e as duas linhas abaixo ficariam vermelhas acusando a producao.
			int chave = ondeEnfiar < 0 ? -1 : Array.FindIndex(fonteEmbate, ondeEnfiar, l => l.Contains('{'));
			var adulterado = new List<string>(fonteEmbate);
			if (chave >= 0)
			{
				adulterado.Insert(chave + 1, "\t\tnpc.Pos = MoveRules.Advance(npc.Pos, dedonde, 1f);");
				adulterado.Insert(chave + 2, "\t\tDisparar(npc, receita);");
			}
			string[] reflexoFalso =
				[.. CorpoDoMetodo(fonteIa, "private void AplicarComando"),
				 .. CorpoDoMetodo([.. adulterado], "private void TickDoContraFeixe")];
			HashSet<string> chamaFalso = ChamadasDe(reflexoFalso);
			List<string> flagradas =
				[.. chamaFalso.Where(n => !chamaJogador.Contains(n) && Array.IndexOf(excecoes, n) < 0).OrderBy(n => n)];
			AfirmarPp("DEFEITO INJETADO: um `MoveRules.Advance` escrito no reflexo (o NPC andando por fora do "
					  + "`PassoDaIa`) faz (a) REPROVAR e dizer o nome -- nenhuma excecao o cobre",
					  chave >= 0 && flagradas.Contains("Advance"), string.Join(", ", flagradas));
			AfirmarPp("DEFEITO INJETADO: um `Disparar(...)` direto no reflexo faz (b) REPROVAR",
					  portasDoTiro.Any(chamaFalso.Contains), string.Join(", ", flagradas));
		}

		// ---- O CONTROLE COMPORTAMENTAL: ela atira, e ela NAO atira ----
		// Sem estas tres, as varreduras acima seriam compativeis com uma IA que nunca atira.
		(ServerPlayer atirador, ServerPlayer _) = DuelistasNoVazio(tiles: 9);
		AfirmarPp("na janela de alcance, com Ki e alvo de pe, o NPC atira",
				  AteAlguemAtirar(atirador, segundos: 12) != null);
		LimparDuelistasDoVazio();

		// ============================ COLADO: ELE NAO ATIRA DALI -- ELE RECUA PRIMEIRO ============================
		// A afirmacao ingenua ("colado, nao sai tiro nenhum em 6 s") reprovou, e o defeito era dela:
		// o NPC esperto (`Inteligencia >= 0,35`) RECUA pra abrir distancia e so entao atira -- foi
		// medido saindo de 1 tile e disparando a 4. A regra que o `EscolherTiro` promete nao e
		// "nunca atira quando esta perto", e sim **quando o tiro sai, o alvo esta dentro da janela**;
		// medir a ausencia do tiro mediria a velocidade de caminhada do NPC.
		//
		// E o piso vem da TABELA, nao digitado aqui: mexer na janela de uma tecnica reescreve a
		// afirmacao junto.
		// ====================================================================================================
		double minimoDaTabela = TecnicasDeLonge.Todas.Min(l => l.AlcanceMinTiles);
		(ServerPlayer colado, ServerPlayer alvoColado) = DuelistasNoVazio(tiles: 1);
		double tilesNoDisparo = DistanciaNoDisparo(colado, alvoColado, segundos: 8);
		GD.Print($"[kiponta]      colado: saiu de 1 tile e o tiro so saiu a {tilesNoDisparo:0.#} tiles "
				 + $"(o piso da tabela e {minimoDaTabela:0.#})");
		AfirmarPp($"...e colado ele NAO atira dali: quando o tiro sai, ja ha pelo menos "
				  + $"{minimoDaTabela:0.#} tiles de distancia",
				  tilesNoDisparo >= minimoDaTabela, $"disparou a {tilesNoDisparo:0.##} tiles");
		LimparDuelistasDoVazio();

		// ============================ O QUE A LINHA DE CIMA PEGOU, MEDIDO SEM SORTEIO ============================
		// A linha de cima ficou vermelha dizendo "disparou a 1 tiles" -- e 1 tile e EXATAMENTE a
		// `DistanciaDeParada` do arranque (32 px), e nao um lugar onde a IA escolheria atirar. O NPC recuava
		// ate a janela e soltava o `Ki_Wave`; durante os 0,67 s de carga (corpo ENRAIZADO pelo canal) a
		// pausa do tiro fazia o cerebro trocar `Atirar` por `Pressionar`, a investida pesada saia e o
		// `Aproximar` levava o corpo plantado colado no alvo. O defeito era de PRODUCAO: o arranque nao
		// perguntava ao `PodeMexerOCorpo` (a corrida do DM desiste com `canmove = 0`, `speedy.dm:225`).
		//
		// A linha de cima pega isso pelo COMPORTAMENTO e depende de a IA investir dentro da carga. Estas
		// duas pegam pela REGRA, sem tique de IA nenhum: o MESMO `Atacar` de producao (a porta do
		// `C2S.Action` e do `Comando.Pesado`), o mesmo alvo marcado a 4 tiles, uma vez com o canal aberto
		// e uma sem. O controle impede a primeira de passar por falta de alcance, de Ki ou de marca: se o
		// arranque nao sai nem com o corpo livre, a primeira nao esta medindo nada.
		//
		// NA ZONA VAZIA DE PROPOSITO: foi a falta de parede que expos o defeito, e nas duas irmas ela o
		// escondia por motivos DIFERENTES. Na `--tiroiateste` o arranque nem entra: o `CorredorLivre` devolve
		// o PRIMEIRO trecho livre da linha, entao a celula logo ATRAS do NPC e bloqueada, o `Disparo` nao
		// consegue recuar ate o `AlcanceMin`, o raio nunca sai e nao ha carga de onde arrancar. Na linha do
		// `turnlock` da `--projetilteste` o arranque e que foi engolido: o salto rumo ao alvo esbarrava na
		// parede do corredor e o `PathOccupied` o recusava.
		//
		// COMO ELA REPROVA: apague a recusa do topo do `Aproximar` e a primeira linha diz "andou 96 px"
		// enquanto o controle continua verde. E ela tambem cobra o SOCO: um conserto que recusasse o
		// `Atacar` inteiro de quem esta enraizado deixaria o corpo parado do mesmo jeito -- e no DM o golpe
		// com o raio carregando e legal (o `canfight = 0` esta comentado em `beams.dm:296`). A marca do soco
		// e a recarga que o `Atacar` arma DEPOIS do arranque, e um corpo recem-forjado nasce com ela zerada.
		// ======================================================================================================
		(ServerPlayer comRaio, ServerPlayer alvoDoRaio) = DuelistasNoVazio(tiles: 4);
		Mirar(comRaio, alvoDoRaio.Id);
		UsarHabilidade(comRaio, "Ki_Wave");   // o canal de verdade: carregando, corpo enraizado
		Vec2 plantadoEm = comRaio.Pos;
		Atacar(comRaio, Protocol.Golpe.Pesado);
		float andouComRaio = (comRaio.Pos - plantadoEm).Length;
		double recargaDoSoco = comRaio.Combate?.Recarga ?? 0;
		AfirmarPp("com o raio carregando, a INVESTIDA nao arranca o corpo (`speedy.dm:225`: a corrida "
				  + "desiste com `canmove = 0`) -- o soco sai, o salto nao",
				  _canais.ContainsKey(comRaio.Id) && andouComRaio < 0.5f && recargaDoSoco > 0,
				  $"canal {(_canais.ContainsKey(comRaio.Id) ? "aberto" : "FECHADO")}, andou {andouComRaio:0.#} px, "
				  + $"recarga do soco {recargaDoSoco:0.###} s");
		LimparDuelistasDoVazio();

		(ServerPlayer livre, ServerPlayer alvoLivre) = DuelistasNoVazio(tiles: 4);
		Mirar(livre, alvoLivre.Id);
		Vec2 alvoLivreEm = alvoLivre.Pos;
		Atacar(livre, Protocol.Golpe.Pesado);
		float vaoLivre = (alvoLivreEm - livre.Pos).Length;
		AfirmarPp("(controle) sem raio na mao, a MESMA investida leva o corpo a um tile do alvo",
				  Math.Abs(vaoLivre - ZoneCollision.TileSize) < 1f, $"ficou a {vaoLivre:0.#} px");
		LimparDuelistasDoVazio();

		// ============================ E A OUTRA PORTA: A PISCADA DO DUPLO CLIQUE (`click.dm:54`) ============================
		// A investida nao e o unico gesto que tira o corpo do lugar: o duplo clique no chao (`C2S.Zanzoken`) cai
		// no `Zanzoken`, que NAO passa pelo `Aproximar` -- e ele saltava com o raio carregando OU segurado, a cauda
		// do feixe indo junto com a mao e a cabeca ficando na linha velha. O DM fecha esta porta com `!usr.beaming`;
		// o port fecha com o CANAL inteiro (a divergencia da fase de carga esta declarada no `GameServer.Zanzoken.cs`).
		//
		// A PORTA E A DE PRODUCAO: `Zanzoken(pl, destino)` e exatamente o que o `case Protocol.C2S.Zanzoken`
		// chama, e o raio e aberto pelo verb (`UsarHabilidade`). A fase SEGURANDO e alcancada pelo tique de
		// producao dos canais (`TickDosCanaisDeKi`, o mesmo do `TiqueDeMundo`) e SO por ele: sem o tique dos
		// corpos, pra o cerebro do NPC nao apertar o verb de novo (soltando o raio) no meio da medicao.
		//
		// CADA RECUSA COBRA TRES COISAS: o corpo nao saiu, o Ki nao foi cobrado e nenhum salto foi anunciado (o
		// carimbo `_saltosAnunciados` do `AnunciarZanzo`) -- e o canal continua aberto, porque "soltar o raio e
		// piscar" nao e recusar.
		//
		// UM CORPO POR FASE, e o controle num terceiro: cada corpo esta num estado so, e o controle impede as
		// recusas de passarem por falta de skill, de Ki ou de alcance -- se a MESMA piscada nao sai do corpo
		// livre, elas nao estao medindo nada. (Ate 2026-09-24 havia um motivo a mais, a recarga de 900 ms que a
		// piscada que passasse armaria pra seguinte; a recarga nao existe no DM e saiu -- ver a linha dela abaixo.)
		// ================================================================================================================

		// ============================ O PISCADOR E O PRECO DO DM ============================
		// A Afterimage entra no livro E no nivel 1 (o `level = 1` do datum, `misc.dm:38`: e dele que o `zanzochange`
		// rende). E a PERICIA de Ki e a VELOCIDADE sobem pelo `Statify` de producao -- a bancada escreve o CRU (3 e 3)
		// e o E sai da curva (~2,6): com os atributos de fabrica (E ~1) o `zanzorange` e 1 tile, e a piscada de tres
		// tiles das linhas abaixo seria ENCURTADA pra um, medindo o encurtamento no lugar do que cada linha mede.
		//
		// O PRECO E ESCRITO AQUI DE NOVO, e de proposito: `kireq = (6*BaseDrain)/(Ekiskill*(Espeed/2))` por tile de
		// `get_dist` (`click.dm:51`, `:54`, `:64`). E a ESPECIFICACAO contra a qual a producao e medida; chamar a
		// conta de producao aqui seria a bancada concordando consigo mesma.
		// ======================================================================================
		void Piscador(ServerPlayer p, double treino = 3)
		{
			p.Livro.Dar(PathDoZanzoken);
			p.Niveis.Por(PathDoZanzoken, 1);
			p.Ficha.kiskill = treino;
			p.Ficha.speed = treino;
			p.Ficha.Statify();
			p.Ficha.Ki = p.Ficha.MaxKi;
		}

		double PrecoDoDm(ServerPlayer p, Vec2 destino) =>
			6 * p.Ficha.BaseDrain() / (p.Ficha.Ekiskill * (p.Ficha.Espeed / 2))
			* Jandirus.Core.Social.Fusao.DistanciaEmTilesDoDm(p.Pos, destino, ZoneCollision.TileSize);

		(float Andou, double KiCobrado, int Saltos) PiscarPelaPorta(ServerPlayer pisca, Vec2 ondeClicou)
		{
			Vec2 saiuDe = pisca.Pos;
			double kiAntesDaPiscada = pisca.Ficha.Ki;
			int saltosAntes = _saltosAnunciados;
			Zanzoken(pisca, ondeClicou);   // o MESMO metodo que o `case Protocol.C2S.Zanzoken` chama
			return ((pisca.Pos - saiuDe).Length, kiAntesDaPiscada - pisca.Ficha.Ki, _saltosAnunciados - saltosAntes);
		}

		// DE LADO PRO FEIXE (ele sai pro leste): e o salto que entortaria o raio. Tres tiles, dentro do `zanzorange`
		// do piscador (oito) -- a piscada livre pousa EXATAMENTE no ponto clicado, sem encurtar.
		Vec2 deLado = new(0, -3 * ZoneCollision.TileSize);

		(ServerPlayer naCarga, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(naCarga);
		UsarHabilidade(naCarga, "Ki_Wave");   // o canal de verdade, ainda na CARGA
		bool carregava = _canais.ContainsKey(naCarga.Id) && !CanalDeKiDe(naCarga.Id).atirando;
		bool kiPraPiscarNaCarga = naCarga.Ficha.Ki >= PrecoDoDm(naCarga, naCarga.Pos + deLado);
		var piscouCarregando = PiscarPelaPorta(naCarga, naCarga.Pos + deLado);
		AfirmarPp("com o raio CARREGANDO, a piscada do duplo clique nao tira o corpo do lugar -- nem cobra Ki, nem "
				  + "anuncia salto (o canal inteiro recusa; o DM, so o `beaming` de `click.dm:54`)",
				  carregava && kiPraPiscarNaCarga && _canais.ContainsKey(naCarga.Id) && piscouCarregando.Andou < 0.5f
				  && Math.Abs(piscouCarregando.KiCobrado) < 1e-9 && piscouCarregando.Saltos == 0,
				  $"{(carregava ? "carregando" : "NAO ESTAVA CARREGANDO")}{(kiPraPiscarNaCarga ? "" : ", SEM Ki pra piscar")}, "
				  + $"andou {piscouCarregando.Andou:0.#} px, Ki cobrado {piscouCarregando.KiCobrado:0.###}, "
				  + $"saltos {piscouCarregando.Saltos}, canal {(_canais.ContainsKey(naCarga.Id) ? "aberto" : "FECHADO")}");
		LimparDuelistasDoVazio();

		(ServerPlayer naMao, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(naMao);
		UsarHabilidade(naMao, "Ki_Wave");
		// A CARGA VIRA RAIO SOZINHA, no tique dos canais -- e esse tique que faz o `beaming` do DM existir aqui.
		for (int i = 0; i < (int)(5 / Protocol.TickSeconds)
						&& _canais.ContainsKey(naMao.Id) && !CanalDeKiDe(naMao.Id).atirando; i++)
			TickDosCanaisDeKi(Protocol.TickSeconds);
		bool segurava = _canais.ContainsKey(naMao.Id) && CanalDeKiDe(naMao.Id).atirando;
		bool kiPraPiscarNaMao = naMao.Ficha.Ki >= PrecoDoDm(naMao, naMao.Pos + deLado);
		var piscouSegurando = PiscarPelaPorta(naMao, naMao.Pos + deLado);
		AfirmarPp("...e SEGURANDO o raio (o `beaming` do DM) tambem nao: o corpo fica, e a cauda do feixe nao salta "
				  + "pra longe da cabeca",
				  segurava && kiPraPiscarNaMao && _canais.ContainsKey(naMao.Id) && piscouSegurando.Andou < 0.5f
				  && Math.Abs(piscouSegurando.KiCobrado) < 1e-9 && piscouSegurando.Saltos == 0,
				  $"{(segurava ? "segurando" : "O RAIO NAO CHEGOU A SAIR")}{(kiPraPiscarNaMao ? "" : ", SEM Ki pra piscar")}, "
				  + $"andou {piscouSegurando.Andou:0.#} px, Ki cobrado {piscouSegurando.KiCobrado:0.###}, "
				  + $"saltos {piscouSegurando.Saltos}, canal {(_canais.ContainsKey(naMao.Id) ? "aberto" : "FECHADO")}");
		LimparDuelistasDoVazio();

		// ============================ O CONTROLE, QUE AGORA TAMBEM COBRA O PRECO DO DM (`click.dm:51`, `:64`) ============================
		// A piscada livre cobra `kireq x 3` -- tres tiles de `get_dist`. Ate 2026-09-24 ela cobrava 2,5% do Ki maximo,
		// um numero sem linha no DM (o cabecalho do `Zanzoken` conta de onde ele veio). A PRECONDICAO de baixo garante
		// que os dois precos DIFEREM neste corpo -- sem ela, um piscador cujo `kireq x 3` caisse perto de 2,5% deixaria
		// a linha verde com a conta velha.
		// COMO ELA REPROVA: volte o `custo = MaxKi * 0.025` no `Zanzoken` e ela diz "Ki cobrado 2,5% de ...".
		// ================================================================================================================================
		(ServerPlayer semRaio, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(semRaio);
		Vec2 ondeCaiLivre = semRaio.Pos + deLado;
		double custoDaPiscada = PrecoDoDm(semRaio, ondeCaiLivre);
		double custoVelho = semRaio.Ficha.MaxKi * 0.025;
		AfirmarPp("(precondicao) neste piscador o preco do DM (`kireq x 3`) e o velho 2,5% do Ki maximo sao numeros DIFERENTES",
				  Math.Abs(custoDaPiscada - custoVelho) > 0.05 * Math.Max(custoDaPiscada, custoVelho),
				  $"DM {custoDaPiscada:0.###} vs velho {custoVelho:0.###}");
		Facing olhavaAntes = semRaio.Facing;   // o duelista nasce encarando o alvo (`DuelistasDeBancada`)
		var piscouLivre = PiscarPelaPorta(semRaio, ondeCaiLivre);
		float longeDoClique = Vec2.Distance(semRaio.Pos, ondeCaiLivre);
		AfirmarPp("(controle) sem raio na mao, a MESMA piscada pousa no ponto clicado e cobra o preco do DM: "
				  + "`(6*BaseDrain)/(Ekiskill*(Espeed/2))` por tile de `get_dist` (`click.dm:51`, `:64`)",
				  !_canais.ContainsKey(semRaio.Id) && longeDoClique < 0.5f
				  && Math.Abs(piscouLivre.KiCobrado - custoDaPiscada) < 1e-6 * Math.Max(1, custoDaPiscada)
				  && piscouLivre.Saltos == 1,
				  $"ficou a {longeDoClique:0.#} px do clique (andou {piscouLivre.Andou:0.#}), Ki cobrado "
				  + $"{piscouLivre.KiCobrado:0.###} de {custoDaPiscada:0.###} (o velho seria {custoVelho:0.###}), "
				  + $"saltos {piscouLivre.Saltos}");

		// ============================ E CHEGA OLHANDO PRA ONDE OLHAVA -- `formerdir` (`click.dm:61-63`) ============================
		// O DM guarda o `dir` antes do `Move()` e o devolve depois: piscar de lado nao tira os olhos do inimigo. O salto
		// aqui e pro NORTE (`deLado`) e o corpo encarava o alvo -- a primeira metade da conta prova que os dois rumos
		// DIFEREM (senao a linha passaria com qualquer regra), a segunda que a piscada saiu de fato.
		// COMO ELA REPROVA: volte o `pl.Facing = MoveRules.FacingFrom(d, pl.Facing)` no `Zanzoken` e ela diz
		// "chegou olhando North".
		// ======================================================================================================================
		Facing rumoDoSalto = MoveRules.FacingFrom(deLado, olhavaAntes);
		AfirmarPp("...e chega OLHANDO PRA ONDE OLHAVA, e nao pro destino (`formerdir`, `click.dm:61-63`)",
				  rumoDoSalto != olhavaAntes && piscouLivre.Saltos == 1 && semRaio.Facing == olhavaAntes,
				  $"olhava {olhavaAntes}, o salto ia pro {rumoDoSalto}, chegou olhando {semRaio.Facing}");

		// ============================ SEM RECARGA: A SEGUNDA PISCADA SAI NA HORA ============================
		// O `turf/DblClick` nao tem `sleep` nem prazo -- quem trava a piscada seguinte e o Ki. O MESMO corpo, no MESMO
		// instante, pisca de volta pro sul: sai, e paga o preco dela.
		// COMO ELA REPROVA: devolva ao `Zanzoken` a recarga de 900 ms que ele tinha (um prazo por corpo, conferido antes
		// de cobrar) e ela diz "saltos 0".
		// ====================================================================================
		Vec2 deVolta = semRaio.Pos - deLado;
		double custoDaVolta = PrecoDoDm(semRaio, deVolta);
		var piscouDeNovo = PiscarPelaPorta(semRaio, deVolta);
		AfirmarPp("SEM RECARGA: a segunda piscada, no mesmo instante, SAI e cobra o preco dela (o DM nao tem prazo "
				  + "entre duas -- quem trava e o Ki)",
				  piscouDeNovo.Saltos == 1 && Vec2.Distance(semRaio.Pos, deVolta) < 0.5f
				  && Math.Abs(piscouDeNovo.KiCobrado - custoDaVolta) < 1e-6 * Math.Max(1, custoDaVolta),
				  $"saltos {piscouDeNovo.Saltos}, ficou a {Vec2.Distance(semRaio.Pos, deVolta):0.#} px, "
				  + $"Ki cobrado {piscouDeNovo.KiCobrado:0.###} de {custoDaVolta:0.###}");

		// ============================ O PROPRIO TILE NAO E PISCADA -- `view(0,usr)` (`click.dm:53`) ============================
		// `for(var/turf/A in view(0,usr)) if(A==src) return`: o duplo clique no tile em que o corpo JA ESTA nao faz nada.
		// O clique e na QUINA mais longe da celula do centro do corpo -- mais de 8 px, que era o portao velho ("clicou
		// nos proprios pes"), entao sem a regra do tile a piscada sairia.
		// COMO ELA REPROVA: troque o `if (salto == 0) return;` do `Zanzoken` pelo velho `if (dist < 8f) return;` e ela
		// diz "saltos 1".
		// ========================================================================================================================
		int celX = (int)MathF.Floor(semRaio.Pos.X / ZoneCollision.TileSize);
		int celY = (int)MathF.Floor(semRaio.Pos.Y / ZoneCollision.TileSize);
		Vec2 quinaA = new(celX * ZoneCollision.TileSize + 1, celY * ZoneCollision.TileSize + 1);
		Vec2 quinaB = new((celX + 1) * ZoneCollision.TileSize - 1, (celY + 1) * ZoneCollision.TileSize - 1);
		Vec2 naPropriaCelula = Vec2.Distance(semRaio.Pos, quinaA) > Vec2.Distance(semRaio.Pos, quinaB) ? quinaA : quinaB;
		float aQuantoDoCentro = Vec2.Distance(semRaio.Pos, naPropriaCelula);   // medido ANTES: se a piscada sair, o corpo vai pra la
		var piscouNoLugar = PiscarPelaPorta(semRaio, naPropriaCelula);
		AfirmarPp("o duplo clique no PROPRIO tile nao pisca, nem cobra (`view(0,usr)`, `click.dm:53`) -- mesmo a "
				  + $"{aQuantoDoCentro:0.#} px do centro do corpo",
				  aQuantoDoCentro >= 8f && piscouNoLugar.Saltos == 0
				  && piscouNoLugar.Andou < 0.5f && Math.Abs(piscouNoLugar.KiCobrado) < 1e-9,
				  $"saltos {piscouNoLugar.Saltos}, andou {piscouNoLugar.Andou:0.#} px, Ki cobrado {piscouNoLugar.KiCobrado:0.###}");
		LimparDuelistasDoVazio();

		// ============================ O ALCANCE E O `zanzorange`, E O QUADRADO DO `get_dist` (`click.dm:52`) ============================
		// O clique vai longe na DIAGONAL (tres alcances pra nordeste). O DM ignora clique alem do `zanzorange`; o port
		// ENCURTA (declarado no cabecalho) -- e encurta pelo QUADRADO do `get_dist`, e nao pelo circulo: o corpo pousa a
		// `zanzorange` tiles nos DOIS eixos, e paga `kireq x zanzorange` (a diagonal de N tiles e N tiles no BYOND).
		// A PRECONDICAO garante que o alcance de producao nao e nenhum dos dois numeros velhos: 6 (os 192 px fixos) e
		// 4 (o que o circulo de 192 px da na diagonal).
		// COMO ELA REPROVA: volte o alcance fixo de 192 px com o `d.Normalized() * 192` (o circulo) e ela diz
		// "pousou a 4 tiles".
		// ===================================================================================================================================
		(ServerPlayer longe, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(longe);
		int alcanceDoDm = (int)Math.Round(1.2 * Math.Max(longe.Ficha.Ekiskill, longe.Ficha.Etechnique) * longe.Ficha.Espeed);
		AfirmarPp("(precondicao) o `zanzorange` deste piscador (`round(1.2*max(Ekiskill,Etechnique)*Espeed)`, `misc.dm:55`) "
				  + "nao e nenhum dos numeros velhos (6 reto, 4 na diagonal)",
				  alcanceDoDm >= 3 && alcanceDoDm != 6 && alcanceDoDm != 4, $"zanzorange {alcanceDoDm}");
		Vec2 origemLonge = longe.Pos;
		Vec2 nordesteLonge = origemLonge + new Vec2(3 * alcanceDoDm * ZoneCollision.TileSize, -3 * alcanceDoDm * ZoneCollision.TileSize);
		double custoDoAlcance = 6 * longe.Ficha.BaseDrain() / (longe.Ficha.Ekiskill * (longe.Ficha.Espeed / 2)) * alcanceDoDm;
		var piscouLonge = PiscarPelaPorta(longe, nordesteLonge);
		int pousouA = Jandirus.Core.Social.Fusao.DistanciaEmTilesDoDm(origemLonge, longe.Pos, ZoneCollision.TileSize);
		Vec2 passo = longe.Pos - origemLonge;
		AfirmarPp("LONGE DEMAIS ENCURTA ate o `zanzorange`, pelo QUADRADO do `get_dist`: pousa na diagonal a `zanzorange` "
				  + "tiles nos dois eixos, e paga `kireq x zanzorange`",
				  piscouLonge.Saltos == 1 && pousouA == alcanceDoDm
				  && Math.Abs(Math.Abs(passo.X) - Math.Abs(passo.Y)) < 0.5f
				  && Math.Abs(piscouLonge.KiCobrado - custoDoAlcance) < 1e-6 * Math.Max(1, custoDoAlcance),
				  $"zanzorange {alcanceDoDm}, pousou a {pousouA} tiles (passo {passo.X:0.#},{passo.Y:0.#} px), Ki cobrado "
				  + $"{piscouLonge.KiCobrado:0.###} de {custoDoAlcance:0.###}, saltos {piscouLonge.Saltos}");
		LimparDuelistasDoVazio();

		// ============================ O `zanzochange++` VIRA EXP DA AFTERIMAGE (`click.dm:65`, `misc.dm:56-59`) ============================
		// Tres corpos, a mesma piscada de um tile pro norte:
		//   * o piscador (nivel 1, `zanzorange` 8)            -> +5 de exp (a esperanca do `prob(20)`: 1/0,2);
		//   * o destreinado (nivel 1, cru 0,5 -- `zanzorange` abaixo de 3) -> nada: o efetor pede `zanzorange >= 3`;
		//   * o piscador no nivel 4                            -> nada: o efetor pede `level < 4`.
		// COMO ELA REPROVA: apague a chamada ao `ExpDaPiscada` no `Zanzoken` e a primeira diz "+0"; tire um dos dois
		// portoes dele e a linha do portao diz "+5".
		// ====================================================================================================================================
		Vec2 umAoNorte = new(0, -ZoneCollision.TileSize);
		(ServerPlayer treinado, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(treinado);
		double expAntes = treinado.Niveis.Exp(PathDoZanzoken);
		var piscouTreinado = PiscarPelaPorta(treinado, treinado.Pos + umAoNorte);
		double ganhou = treinado.Niveis.Exp(PathDoZanzoken) - expAntes;
		AfirmarPp("a piscada TREINA a Afterimage: +5 de exp com `zanzorange >= 3` e nivel < 4 (o `zanzochange` do DM "
				  + "rende inteiro a cada tique ate um `prob(20)` zera-lo -- 1/0,2 = 5 na media)",
				  piscouTreinado.Saltos == 1 && Math.Abs(ganhou - 5) < 1e-9,
				  $"saltos {piscouTreinado.Saltos}, exp +{ganhou:0.###} (zanzorange {ZanzorangeG10(treinado.Ficha)})");
		LimparDuelistasDoVazio();

		(ServerPlayer cru, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(cru, treino: 0.5);
		double expCruAntes = cru.Niveis.Exp(PathDoZanzoken);
		var piscouCru = PiscarPelaPorta(cru, cru.Pos + umAoNorte);
		AfirmarPp("...e NAO treina com `zanzorange < 3` (o portao `savant.zanzorange>=3` do efetor) -- a piscada sai igual",
				  ZanzorangeG10(cru.Ficha) < 3 && piscouCru.Saltos == 1
				  && Math.Abs(cru.Niveis.Exp(PathDoZanzoken) - expCruAntes) < 1e-9,
				  $"zanzorange {ZanzorangeG10(cru.Ficha)}, saltos {piscouCru.Saltos}, "
				  + $"exp +{cru.Niveis.Exp(PathDoZanzoken) - expCruAntes:0.###}");
		LimparDuelistasDoVazio();

		(ServerPlayer mestreDoZanzo, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(mestreDoZanzo);
		mestreDoZanzo.Niveis.Por(PathDoZanzoken, 4);
		double expNoTeto = mestreDoZanzo.Niveis.Exp(PathDoZanzoken);
		var piscouNoTeto = PiscarPelaPorta(mestreDoZanzo, mestreDoZanzo.Pos + umAoNorte);
		AfirmarPp("...nem no nivel 4 (o `if(level<4)` do efetor)",
				  mestreDoZanzo.Niveis.Nivel(PathDoZanzoken) == 4 && piscouNoTeto.Saltos == 1
				  && Math.Abs(mestreDoZanzo.Niveis.Exp(PathDoZanzoken) - expNoTeto) < 1e-9,
				  $"nivel {mestreDoZanzo.Niveis.Nivel(PathDoZanzoken)}, saltos {piscouNoTeto.Saltos}, "
				  + $"exp +{mestreDoZanzo.Niveis.Exp(PathDoZanzoken) - expNoTeto:0.###}");
		LimparDuelistasDoVazio();

		// ============================ O MORTO DE PE PISCA; O CADAVER NAO (`click.dm:54` le so `KO`) ============================
		// O corpo morre pelo funil de toda morte (`Morrer`) e fica CADAVER no chao dos vivos: `dead` e nao `MortoDePe`
		// -- ali o DM ja nao tem mob, so o `obj/mobCorpse`. Depois ele viaja pelo passo de producao da viagem
		// (`IrProAlem`, o que o `VenceuOPrazoDaMorte` chama pra quem tem tela; o corpo forjado nao tem `Peer`, e a
		// triagem o deixaria no chao -- ver `--alemteste`, secao 3) e fica de pe na mesa do Enma: o `Death()` do DM
		// escreve `move = 1` e faz o `Un_KO`, e a linha do `DblClick` nao le `dead`.
		// O CLIQUE e pra uma das oito direcoes com o caminho livre NO MAPA DO OUTRO MUNDO -- a mesa do Enma tem parede
		// em volta, e a parede no caminho recusa por outra regra.
		// COMO ELA REPROVA: volte o `if (pl.Ficha.KO || pl.Ficha.dead) return;` no `Zanzoken` e a segunda diz "saltos 0".
		// ========================================================================================================================
		(ServerPlayer finado, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(finado);
		bool morreu = finado.Combate.Morrer(ignorarSeguro: true);
		finado.Ficha.Ki = finado.Ficha.MaxKi;   // o preco da piscada, e nada mais: a regra medida e a do `dead`
		var piscouCadaver = PiscarPelaPorta(finado, finado.Pos + umAoNorte);
		AfirmarPp("o CADAVER (morto no chao dos vivos, `MortoDePe` falso) nao pisca",
				  morreu && finado.Ficha.dead && !finado.MortoDePe && piscouCadaver.Saltos == 0 && piscouCadaver.Andou < 0.5f,
				  $"morreu={morreu}, dead={finado.Ficha.dead}, de pe={finado.MortoDePe}, saltos {piscouCadaver.Saltos}");

		IrProAlem(finado);
		finado.Ficha.Ki = finado.Ficha.MaxKi;
		ZoneCollision? mapaDoAlem = MapaDaZonaOuCatalogo(finado.Zone);
		Vec2? livreNoAlem = null;
		foreach (Vec2 rumo in MoveRules.OitoRumos)
		{
			Vec2 alvoDoClique = finado.Pos + rumo * (2 * ZoneCollision.TileSize);
			if (mapaDoAlem == null || (!MoveRules.Occupied(mapaDoAlem, alvoDoClique)
									   && !MoveRules.PathOccupied(mapaDoAlem, finado.Pos, alvoDoClique)))
			{ livreNoAlem = alvoDoClique; break; }
		}
		AfirmarPp("(precondicao) o morto chegou de pe ao Outro Mundo, e ha um rumo livre de dois tiles na mesa do Enma",
				  finado.MortoDePe && livreNoAlem != null, $"zona {finado.Zone.Name}, de pe={finado.MortoDePe}");
		if (livreNoAlem is { } cliqueNoAlem)
		{
			var piscouFantasma = PiscarPelaPorta(finado, cliqueNoAlem);
			AfirmarPp("...e o MORTO DE PE do Outro Mundo PISCA (`click.dm:54` le `!usr.KO`, e nao `dead`)",
					  piscouFantasma.Saltos == 1 && Vec2.Distance(finado.Pos, cliqueNoAlem) < 0.5f,
					  $"saltos {piscouFantasma.Saltos}, ficou a {Vec2.Distance(finado.Pos, cliqueNoAlem):0.#} px do clique");
		}
		RemoverNpc(finado);   // ele saiu da zona vazia pro Outro Mundo: a limpeza da familia nao o alcancaria la
		LimparDuelistasDoVazio();

		// ============================ AS OUTRAS CINCO LETRAS DA LINHA -- `click.dm:54` ============================
		// `usr.move && !usr.Apeshit && !usr.KB && ... && !usr.med && !usr.train`. Uma linha por letra, com a MESMA cobranca
		// das duas recusas do raio (o corpo nao saiu, o Ki nao foi cobrado, nenhum salto anunciado) e o MESMO clique do
		// controle logo acima -- que e o que prova que cada recusa e da letra, e nao de alcance, Ki ou skill. UM CORPO
		// POR LETRA: cada um num estado so.
		//
		// CADA ESTADO ENTRA PELO CAMINHO DE PRODUCAO: o primeiro aperto da Final Explosion (o `move = 0` literal de
		// `misc.dm:355`; a Death Ball nao serve aqui porque o duelista nasce com 100 de Ki e ela pede 150x o dreno-base,
		// mais que o tanque inteiro), o funil do `admin_forma` (a cena CHEIA da estreia do SSJ -- o `move = 0` de
		// `SSJCinematic.dm:4`, e a parte do `move` que NAO ancora: sem a letra o corpo sairia da cena de verdade), o funil
		// da fera (`VirarFera`, o mesmo da lua e do `admin_forma`), a porta unica do arremesso (`Arremessar`) e os dois
		// campos que o `case Protocol.C2S.Activity` escreve (as teclas M e T).
		// O QUE FICA SEM LINHA: o torneio, a Death Ball, a Genkidama e o Instant Transmission do `MoveZerado` -- montar
		// cada um pede o evento inteiro ou mais Ki do que o duelista tem. Apagar um deles da funcao nao acende nada aqui.
		//
		// COMO ELAS REPROVAM: apague uma letra do `if` no `Zanzoken` (ou o `EmCena`/`_cargaG3` do `MoveZerado`) e a linha
		// dela diz "andou 96 px, Ki cobrado ..., saltos 1" -- e as outras continuam verdes, porque cada corpo esta num
		// estado so.
		// =======================================================================================================
		void AfirmarPiscadaRecusada(string oque, ServerPlayer quem, bool noEstado, string estado)
		{
			bool kiPraPiscar = quem.Ficha.Ki >= PrecoDoDm(quem, quem.Pos + deLado);
			var piscou = PiscarPelaPorta(quem, quem.Pos + deLado);
			AfirmarPp(oque,
					  noEstado && kiPraPiscar && piscou.Andou < 0.5f && Math.Abs(piscou.KiCobrado) < 1e-9
					  && piscou.Saltos == 0,
					  $"{(noEstado ? estado : "NAO ESTAVA " + estado)}{(kiPraPiscar ? "" : ", SEM Ki pra piscar")}, "
					  + $"andou {piscou.Andou:0.#} px, Ki cobrado {piscou.KiCobrado:0.###}, saltos {piscou.Saltos}");
		}

		(ServerPlayer bomba, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(bomba);
		ExplosaoFinalG3(bomba, RaioPertoG3);   // o primeiro aperto do verb: o corpo fica preso carregando
		AfirmarPiscadaRecusada("com a Final Explosion CARREGANDO (`usr.move = 0`, `misc.dm:355`) a piscada nao sai -- "
							   + "sem a letra, a ancora da carga puxaria o corpo de volta no pulso seguinte",
							   bomba, _cargaG3.ContainsKey(bomba.Id), "carregando a Final Explosion");
		_cargaG3.Remove(bomba.Id);   // o pulso de 10 Hz nao roda dentro da bancada: a carga sai daqui, sem detonar
		LimparDuelistasDoVazio();

		(ServerPlayer naCena, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(naCena);
		// O FUNIL DO `admin_forma` (`Entrar` -> `AplicarForma` -> `AnunciarForma` -> `MarcarCena`): um corpo recem-forjado
		// estreia o SSJ e ganha a cena cheia. Nada de Oozaru aqui, pra a linha medir SO o `move`.
		AdminForcarForma(naCena, "ssj1");
		AfirmarPiscadaRecusada("na CINEMATICA da estreia do SSJ (`move = 0`, `SSJCinematic.dm:4`) a piscada nao sai -- "
							   + "sem a letra, o corpo sairia de dentro da cena",
							   naCena, EmCena(naCena) && naCena.Oozaru == Jandirus.Core.Forms.FormaOozaru.Nao,
							   "preso na cena do SSJ");
		LimparDuelistasDoVazio();

		(ServerPlayer fera, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(fera);
		VirarFera(fera, Jandirus.Core.Forms.FormaOozaru.Regular);
		// A CENA DO MACACO TAMBEM PRENDE O CORPO (e ela e o `move` deste port -- ver o `MoveZerado`). Ela escorre pelo
		// relogio de producao, o topo do `TickDaForma` que roda pra todo corpo, pra esta linha medir SO a letra `Apeshit`.
		for (int i = 0; i < (int)(30 / Protocol.TickSeconds) && EmCena(fera); i++) TickDaForma(fera, Protocol.TickSeconds);
		AfirmarPiscadaRecusada("EM OOZARU a piscada nao sai (`!usr.Apeshit`), mesmo com a cena da fera ja acabada",
							   fera, fera.Oozaru != Jandirus.Core.Forms.FormaOozaru.Nao && !EmCena(fera),
							   "Oozaru fora da cena");
		LimparDuelistasDoVazio();

		(ServerPlayer voa, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(voa);
		Arremessar(voa, new Vec2(-1, 0), Empurrao.ResistenciaPadrao, Empurrao.TiquesMax);   // a porta unica do arremesso
		AfirmarPiscadaRecusada("ARREMESSADO a piscada nao sai (`!usr.KB`): quem esta no ar pelo golpe nao escolhe pra "
							   + "onde vai", voa, voa.TiquesDeVoo > 0, "no ar pelo arremesso");
		LimparDuelistasDoVazio();

		(ServerPlayer medita, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(medita);
		medita.Ficha.med = true;   // o que o `case Protocol.C2S.Activity` escreve com a tecla M
		AfirmarPiscadaRecusada("MEDITANDO a piscada nao sai (`!usr.med`)", medita, medita.Ficha.med, "meditando");
		LimparDuelistasDoVazio();

		(ServerPlayer treina, ServerPlayer _) = DuelistasNoVazio(tiles: 4);
		Piscador(treina);
		treina.Ficha.train = true;   // idem, com a tecla T
		AfirmarPiscadaRecusada("TREINANDO a piscada nao sai (`!usr.train`)", treina, treina.Ficha.train, "treinando");
		LimparDuelistasDoVazio();

		(ServerPlayer seco, ServerPlayer _) = DuelistasNoVazio(tiles: 9);
		seco.Ficha.Ki = 0;
		AfirmarPp("...e sem Ki ele NAO atira (a mesma recusa que o jogador ouve)",
				  AteAlguemAtirar(seco, segundos: 6) == null);
		LimparDuelistasDoVazio();
	}

	/// <summary>
	/// A QUANTOS TILES DO ALVO O NPC ESTAVA QUANDO O TIRO SAIU. Negativo = nao atirou no prazo.
	/// A distancia e lida NO MESMO tique do disparo -- lida depois, ela seria a de onde ele
	/// continuou andando.
	/// </summary>
	private double DistanciaNoDisparo(ServerPlayer npc, ServerPlayer alvo, double segundos)
	{
		int tiques = (int)(segundos / Protocol.TickSeconds);
		for (int i = 0; i < tiques; i++)
		{
			TiqueDeMundo();
			foreach (Projetil p in ProjeteisDaZona(npc.Zone.Hash))
				if (p.Dono == npc.Id && p.Vivo)
					return (alvo.Pos - npc.Pos).Length / ZoneCollision.TileSize;
		}
		return -1;
	}

	/// <summary>A zona so desta familia. Ver <see cref="DuelistasNoVazio"/>.</summary>
	private static readonly ZoneKey ZonaVaziaDaIa = new(ZoneKey.KindProcedural, "bancadaponta_ia");

	/// <summary>
	/// OS DUELISTAS DA `--tiroiateste`, MUDADOS PRA UMA ZONA VAZIA.
	///
	/// ============================ POR QUE ESTA FAMILIA NAO PODE RODAR NA TERRA ============================
	/// Esta bancada roda no PRIMEIRO LOGIN, com o mundo de pe: ha um jogador de verdade e ha
	/// cidadaos de molde na Terra. O cerebro do NPC escolhe o inimigo por PERCEPCAO, e nao pelo
	/// corpo que a bancada apontou -- entao o "colado a 1 tile" reprovava por um motivo verdadeiro e
	/// que nao era o dela: o NPC estava mirando outra pessoa, longe, e atirando com toda a razao.
	///
	/// A zona propria e a correcao honesta: dois corpos, ninguem mais, e a regra medida continua
	/// sendo a de producao. Nada e desligado -- so o mundo em volta e que sai de cena.
	/// ================================================================================================
	/// </summary>
	private (ServerPlayer, ServerPlayer) DuelistasNoVazio(int tiles)
	{
		(ServerPlayer npc, ServerPlayer alvo) = DuelistasDeBancada(tiles);
		foreach (ServerPlayer p in new[] { npc, alvo })
		{
			ZoneList(p.Zone.Hash).Remove(p);
			p.Zone = ZonaVaziaDaIa;
			ZoneList(p.Zone.Hash).Add(p);
		}
		return (npc, alvo);
	}

	/// <summary>Tira da zona vazia o que a familia pos nela -- corpos e tiros.</summary>
	private void LimparDuelistasDoVazio()
	{
		List<Projetil> lista = ProjeteisDaZona(ZonaVaziaDaIa.Hash);
		_projeteisVivos -= lista.Count;
		lista.Clear();

		foreach (ServerPlayer p in ZoneList(ZonaVaziaDaIa.Hash).ToList())
		{
			if (_canais.ContainsKey(p.Id)) SoltarDoRaio(p.Id);
			_players.Remove(p.Id);
			ZoneList(ZonaVaziaDaIa.Hash).Remove(p);
		}
		LimparEmbatesDaBancada();
	}

	// =====================================================================
	// 8) O TETO DE TIROS VIVOS DISPARA -- OS DOIS
	// =====================================================================
	/// <summary>
	/// Sao DOIS tetos e eles guardam coisas diferentes: o da ZONA impede um jogador de encher o
	/// pedaco de mapa em que os outros estao; o do MUNDO impede que trinta jogadores em trinta
	/// planetas façam a mesma coisa somados.
	///
	/// COMO ELA REPROVA: apague qualquer uma das duas guardas do `PodeAtirar` (ou a repeticao delas
	/// no `Disparar`, que e a porta por onde a tecnica customizada entra) e a linha correspondente
	/// fica vermelha dizendo quantos entraram.
	/// </summary>
	private void OTetoDeTirosVivosDispara()
	{
		GD.Print("[kiponta] -- 8) OS DOIS TETOS DE TIRO VIVO DISPARAM (zona e mundo)");

		LimparTudoDaBancada();
		ServerPlayer pl = Forjar("Metralha", CorredorLivre(4), bp: 5_000);
		pl.Ficha.Ki = 1e12;

		int aceitos = 0;
		for (int i = 0; i < MaxProjeteisPorZona + 32; i++)
		{
			Projetil p = Disparar(pl, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast });
			p.VidaRestante = 9999;
			if (p.Vivo) aceitos++;
		}
		AfirmarPp($"a zona aceita exatamente {MaxProjeteisPorZona} tiros vivos, e nem um a mais",
				  aceitos == MaxProjeteisPorZona, $"aceitou {aceitos}");
		AfirmarPp("...e a porta do jogador RECUSA COM MOTIVO (nao em silencio)",
				  !PodeAtirar(pl, 0, out string porZona) && porZona.Contains("saturad"), porZona);

		// O TETO DO MUNDO: enche varias zonas ate ele estourar. `_projeteisVivos` e a conta global.
		int vivosAntes = _projeteisVivos;
		bool bateuNoDoMundo = false;
		var enchedores = new List<ServerPlayer>();
		for (int z = 1; z <= 6 && !bateuNoDoMundo; z++)
		{
			ServerPlayer outro = Forjar($"Metralha{z}", CorredorLivre(4), bp: 5_000);
			outro.Zone = new ZoneKey(ZoneKey.KindPremade, "Earth");   // mesma faixa de ids da bancada
			outro.Ficha.Ki = 1e12;
			enchedores.Add(outro);

			// UMA ZONA DE MENTIRA POR ATIRADOR: o hash e o que separa as listas, e o que se mede
			// aqui e a soma delas. Sem varias zonas, o teto do mundo (1024) nunca seria alcancado --
			// o da zona (256) recusaria antes, e a linha abaixo mediria o teto errado.
			var falsa = new ZoneKey(ZoneKey.KindProcedural, $"bancadaponta{z}");
			outro.Zone = falsa;
			for (int i = 0; i < MaxProjeteisPorZona && !bateuNoDoMundo; i++)
			{
				Projetil p = Disparar(outro, new ReceitaDeProjetil { Tipo = TipoDeProjetil.Blast });
				p.VidaRestante = 9999;
				if (!p.Vivo) bateuNoDoMundo = _projeteisVivos >= MaxProjeteisNoMundo;
			}
			if (!PodeAtirar(outro, 0, out string motivo) && motivo.Contains("mundo")) bateuNoDoMundo = true;
		}
		AfirmarPp($"o teto do MUNDO ({MaxProjeteisNoMundo}) tambem dispara, somando as zonas",
				  bateuNoDoMundo && _projeteisVivos >= MaxProjeteisNoMundo,
				  $"vivos {_projeteisVivos} (antes {vivosAntes})");
		AfirmarPp("...e ele tambem fala",
				  !PodeAtirar(pl, 0, out string porMundo) && porMundo.Length > 0, porMundo);

		// LIMPEZA DAS ZONAS DE MENTIRA: a `LimparTudoDaBancada` so conhece a zona da Terra.
		foreach (ServerPlayer e in enchedores)
		{
			List<Projetil> lista = ProjeteisDaZona(e.Zone.Hash);
			_projeteisVivos -= lista.Count;
			lista.Clear();
			_players.Remove(e.Id);
			ZoneList(e.Zone.Hash).Remove(e);
		}
		LimparTudoDaBancada();

		AfirmarPp("depois da limpeza a conta do mundo volta a zero (o contador nao vaza)",
				  _projeteisVivos == 0, $"{_projeteisVivos}");
	}

	// =====================================================================
	// 9) O SAVE VELHO CARREGA
	// =====================================================================
	/// <summary>
	/// UM ARQUIVO ESCRITO ANTES DESTE SISTEMA -- e "antes" quer dizer SEM A CHAVE no JSON, que e
	/// diferente de "com a chave nula". O campo nulo a `--tecnicateste` ja mede; o que ninguem
	/// tinha medido e o arquivo de verdade, lido pelo `AccountStore` de producao.
	///
	/// COMO ELA REPROVA: apague a linha `Customizadas = ...` do `AccountStore.DeJogador` (que e
	/// exatamente como os `Limiares` sumiram do disco por meses) e a ida e volta reprova; faca o
	/// `PrepararCustomizadas` iterar sem guarda de nulo e o save velho derruba o login inteiro.
	/// </summary>
	private void OSaveVelhoCarrega()
	{
		GD.Print("[kiponta] -- 9) O SAVE VELHO CARREGA, E O NOVO ATRAVESSA O DISCO");

		ServerPlayer pl = Forjar("Arquivo", CorredorLivre(4), bp: 5_000);
		ComandoDeTecnicaCustomizada(pl, "ca_criar", "");
		ComandoDeTecnicaCustomizada(pl, "ca_tipo", "guided");
		ComandoDeTecnicaCustomizada(pl, "ca_texto", "nome/Perseguidora");
		ComandoDeTecnicaCustomizada(pl, "ca_grito", "grito");
		// TRES de potencia antes do folego: `StaminaLigar` DEVOLVE 2 pontos, e desde o piso em zero do
		// dono ele exige ter 2 gastos pra passar. Com um so, ele seria recusado e a tecnica de
		// referencia iria pro disco sem folego -- e a afirmacao abaixo exige `depois.UsaStamina`, entao
		// ela reprovaria por causa da MONTAGEM e nao do disco. Sobra gasto 1, que e proposital: `Gasto`
		// so testa o disco se for diferente do que um `new` traz de graca.
		for (int i = 0; i < 3; i++) ComandoDeTecnicaCustomizada(pl, "ca_comprar", nameof(Compra.DanoMais));
		ComandoDeTecnicaCustomizada(pl, "ca_comprar", nameof(Compra.StaminaLigar));
		ComandoDeTecnicaCustomizada(pl, "ca_salvar", "");
		AfirmarPp("a tecnica de referencia ficou pronta, COM folego e saldo 1 (senao esta familia mede nada)",
				  pl.Customizadas.Count == 1 && pl.Customizadas[0] is { Criada: true, UsaStamina: true, Gasto: 1 },
				  pl.Customizadas.Count == 0 ? "nenhuma"
											 : $"gasto {pl.Customizadas[0].Gasto} folego {pl.Customizadas[0].UsaStamina}");

		string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
											  "jandirus_kiponta_" + Guid.NewGuid().ToString("N"));
		try
		{
			var loja = new AccountStore(pasta);
			var conta = new AccountSave { Conta = "bancada_ponta" };
			conta.Slots[0] = AccountStore.DeJogador(pl, 0);
			loja.Gravar(conta);

			// ---- A IDA E VOLTA COMPLETA, PELO CAMINHO DE PRODUCAO ----
			CharacterSave? volta = loja.Carregar("bancada_ponta")?.Slots[0];
			var renascido = new ServerPlayer { Id = 4242, Ficha = pl.Ficha };
			PrepararCustomizadas(renascido, volta);
			TecnicaCustomizada antes = pl.Customizadas[0];
			TecnicaCustomizada? depois = renascido.Customizadas.FirstOrDefault();

			AfirmarPp("a tecnica volta do disco inteira (tipo, nome, grito, numeros e pontos)",
					  depois != null && depois.Tipo == antes.Tipo && depois.Nome == antes.Nome
					  && depois.DizGrito == antes.DizGrito && depois.Gasto == antes.Gasto
					  && Math.Abs(depois.BaseDano - antes.BaseDano) < 1e-9 && depois.UsaStamina,
					  depois == null ? "nao voltou nada"
									 : $"{depois.Tipo}/{depois.Nome}/gasto {depois.Gasto}");
			AfirmarPp("...e ela volta como PRONTA, com o verbo que o botao aciona",
					  depois is { Criada: true } && depois.Verbo == "Custom_Attack1",
					  depois?.Verbo ?? "-");

			// ---- AGORA O ARQUIVO VELHO: A CHAVE NAO EXISTE ----
			string caminho = System.IO.Directory.GetFiles(pasta, "*.json")[0];
			JsonNode? raiz = JsonNode.Parse(System.IO.File.ReadAllText(caminho));
			bool tinha = raiz?["Slots"]?[0]?.AsObject().Remove("Customizadas") ?? false;
			System.IO.File.WriteAllText(caminho, raiz!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

			AfirmarPp("consegui produzir um save de ANTES do sistema (a chave saiu do arquivo)", tinha);
			AfirmarPp("...e o arquivo realmente nao tem mais a palavra",
					  !System.IO.File.ReadAllText(caminho).Contains("Customizadas"));

			CharacterSave? antigo = loja.Carregar("bancada_ponta")?.Slots[0];
			AfirmarPp("o `AccountStore` de producao le o save velho sem reclamar", antigo != null);
			AfirmarPp("...e o campo entra NULO, que e o mesmo que \"nunca inventei nada\"",
					  antigo?.Customizadas == null);

			var velho = new ServerPlayer { Id = 4343, Ficha = pl.Ficha };
			PrepararCustomizadas(velho, antigo);
			AfirmarPp("...e o corpo entra sem tecnica nenhuma, sem migracao e sem estourar",
					  velho.Customizadas.Count == 0 && velho.Mesa == null);

			// E ELE AINDA CONSEGUE INVENTAR: um save velho nao pode ser um jogador aleijado.
			ComandoDeTecnicaCustomizada(velho, "ca_criar", "");
			ComandoDeTecnicaCustomizada(velho, "ca_texto", "nome/Primeira de todas");
			ComandoDeTecnicaCustomizada(velho, "ca_salvar", "");
			AfirmarPp("...e quem vem de um save velho consegue inventar a primeira tecnica dele",
					  velho.Customizadas.Count == 1 && velho.Customizadas[0].Id == 1);
		}
		catch (Exception e)
		{
			AfirmarPp("o save atravessa o disco nos dois formatos", false, e.Message);
		}
		finally
		{
			try { if (System.IO.Directory.Exists(pasta)) System.IO.Directory.Delete(pasta, true); }
			catch { /* pasta temporaria: nao ha o que consertar aqui */ }
			LimparTudoDaBancada();
		}
	}

	// =====================================================================
	// O QUE FICA DE PE PRA METADE VIVA
	// =====================================================================
	/// <summary>
	/// ARMA O JOGADOR DE VERDADE E POE UM BONECO NA FRENTE DELE.
	///
	/// ============================ POR QUE O ALVO NASCE AQUI, E NAO NO ROBO ============================
	/// O robo e um CLIENTE: ele nao pode criar corpo, nao pode escolher BP e nao pode decidir onde
	/// alguem esta -- e a metade viva existe justamente pra provar que ele nao pode. Entao quem poe
	/// o boneco no mundo e o servidor, e o robo so descobre que ha alguem ali olhando o snapshot,
	/// como qualquer jogador descobriria.
	///
	/// O BP e o Ki sao dados na mao pelo mesmo motivo do `--bpteste`: um personagem novo expressa
	/// entre 2 e 21 de poder, e um tiro desses nao arranca vida visivel de ninguem em tempo de teste.
	/// ============================================================================================
	/// </summary>
	public void ArmarAMetadeViva(ServerPlayer vivo)
	{
		vivo.Ficha.BP = Math.Max(vivo.Ficha.BP, 200_000);
		vivo.Ficha.Statify();
		vivo.Ficha.Tick(agoraMs: NowMs());
		vivo.Ficha.Ki = vivo.Ficha.MaxKi;

		// O BONECO NASCE UMA VEZ SO. No relogin quem volta e o jogador; o alvo continua de pe onde
		// estava, com a vida que ja perdeu -- que e o que faz a familia de depois do relogin medir o
		// MESMO mundo, e nao um mundo recem-montado.
		if (_pontaBoneco != null && _players.ContainsKey(_pontaBoneco.Id)) return;

		// O BONECO: tres tiles a leste e SEM cerebro -- ele nao reage, nao anda e nao atira. O que a
		// metade viva mede e o que o cliente consegue (ou nao) fazer com ele.
		var boneco = new ServerPlayer
		{
			Id = IdBaseDeProjetil + 900,
			Peer = null,
			Name = "Boneco de Bancada",
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = vivo.Zone,
			Pos = new Vec2(vivo.Pos.X + 3 * ZoneCollision.TileSize, vivo.Pos.Y),
			Conta = "bancada_ponta",
			Slot = 0,
			// ============================ O BONECO E TAO FORTE QUANTO QUEM ATIRA ============================
			// A primeira versao dava 2.000 de BP contra os 200.000 do jogador, e o `BPModulus` do DM
			// -- que multiplica a cadeia INTEIRA pela razao de poder -- transformava um tiro de
			// bancada em 1.014 de dano: o boneco morria no primeiro, sumia da lista de alvos, e a
			// familia que mede "a vida dele CAIU" media um cadaver.
			//
			// A DECIMA PARTE do poder de quem atira e o meio termo medido: o `BPModulus` ainda pesa
			// o bastante pra o byte de vida do snapshot mexer numa rajada, e pouco o bastante pra
			// ele continuar de pe pro tiro de DEPOIS do relogin -- que e a outra coisa que precisa
			// acertar alguem.
			// ==========================================================================================
			Ficha = new Fighter { Race = "Human", BP = Math.Max(vivo.Ficha.BP / 10, 1_000) },
			Livro = new SkillBook(),
		};
		boneco.Ficha.Class = "Normal";
		PorNoMundo(boneco);
		boneco.Ficha.Ki = boneco.Ficha.MaxKi;
		boneco.Ficha.Tick(agoraMs: NowMs());
		_pontaBoneco = boneco;

		GD.Print($"[kiponta] o boneco '{boneco.Name}' (id {boneco.Id}) esta de pe a 3 tiles de "
				 + $"'{vivo.Name}' -- a metade viva (`--diagki`) comeca agora.");
	}
}
