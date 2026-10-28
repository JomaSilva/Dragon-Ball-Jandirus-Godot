using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// `--kaikaiteste` -- O KAI KAI COM A TELA DOS PLANETAS CONHECIDOS (2026-10-23). O dono: *"faça o kai kai abrir uma tela com todos
/// os planetas conhecidos por jogadores e ao clicar ele teleporta pra eles no mesmo estilo do IT"*. Ver `GameServer.KaiKai.cs`.
///
/// ============================ O QUE ELA AFIRMA, E COMO ============================
/// Pelo caminho de producao (`UsarHabilidade` -> o registro do lote G11 -> `KaiKaiG11`), com corpos forjados na Terra e TRES
/// MUNDOS GERADOS DE VERDADE (os menores perto da Terra: nascem rapido), anotados no livro de batismo como o `AnotarDescoberta`
/// anota. O livro e restaurado no fim e nada vai pro disco (`BatismosSoNaMemoriaDeTeste`); a morte de planeta mora num
/// `PalcoDeMortes`. Os atalhos sao de RELOGIO, e so de relogio (`VencerOKaiKaiDeTeste`, `EnvelhecerAEsperaDoKaiKaiDeTeste`); quem
/// decide continua sendo o tique de producao. A espera do mundo gerado e REAL: a bancada so colhe a geracao quando quer
/// (`TickDasGeracoes`), e o mundo fica "nascendo" pro servidor ate la.
///
/// Cada regra tem o DEFEITO INJETADO dela (`KaiKai*DeTeste`, lido pela propria linha da regra): a mesma regua, com ele ligado,
/// REPROVA -- e o "(defeito injetado: ...)" de cada familia.
///
/// (2026-10-24) A FAMILIA 9 e a chegada no BERCO do planeta (dono: "Usar o berco do planeta"): o Kai Kai, o Devil Bringer e o Rasgo
/// Dimensional pousam no `PontoDeNascimento`, em chao firme -- e nao no `SpawnPos` fixo, que em Namek e agua
/// (`ChegadaNoPontoFixoDeTeste`, `GameServer.Tecnicas.G4.cs`).
///
///     Godot --headless --server --port 10830 --kaikaiteste
/// ==================================================================================
/// </summary>
public partial class GameServer
{
	private int _kkOk, _kkFalhou;

	private void AfirmarKK(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _kkOk++; GD.Print($"[kaikai]   OK    {oque}"); return; }
		_kkFalhou++;
		GD.PrintErr($"[kaikai]   FALHA {oque}   {detalhe}");
	}

	private const string PathDoKaiKaiKK = "/datum/skill/kai/Teleport";
	private const string PathDoDevilBringerKK = "/datum/skill/demon/Devil_Bringer";
	private const string PathDoRasgoKK = "/datum/skill/RiftTeleport";

	/// <summary>Os tres mundos gerados da bancada: A (pisado e batizado), B (pisado, sem nome) e C (que ninguem pisou).</summary>
	private readonly record struct MundosDoKaiKaiKK(PlanetaNoEspaco A, PlanetaNoEspaco B, PlanetaNoEspaco C)
	{
		public string ChaveA => ChaveDePlaneta.De(A).Texto;
		public string ChaveB => ChaveDePlaneta.De(B).Texto;
		public string ChaveC => ChaveDePlaneta.De(C).Texto;
		public ZoneKey ZonaA => Espaco.ZonaDe(A);
		public ZoneKey ZonaB => Espaco.ZonaDe(B);
		public ZoneKey ZonaC => Espaco.ZonaDe(C);
	}

	/// <summary>O batismo do mundo A: o nome que a tela tem de mostrar no lugar do nome sorteado.</summary>
	private const string BatismoDoKK = "Kaiopolis";

	public void RodarBancadaDoKaiKai()
	{
		_kkOk = _kkFalhou = 0;
		_pjProximoCorredor = 8;
		_pjMapa = MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
		GD.Print("[kaikai] ================ O KAI KAI COM A TELA DOS PLANETAS CONHECIDOS ================");

		List<NomeDePlaneta> livroAntes = [.. _batismos.Todos];
		bool memoriaAntes = BatismosSoNaMemoriaDeTeste;
		List<(int Id, float X, float Y)>? claraoAntes = EscutaDaTransmissao;
		BatismosSoNaMemoriaDeTeste = true;
		try
		{
			AfirmarKK("(montagem) a zona da bancada tem colisao carregada", _pjMapa != null);
			if (AcharMundosDoKK() is not { } m)
			{
				AfirmarKK("(montagem) ha tres mundos gerados de nomes diferentes perto da Terra, com a volta da zona pro corpo", false);
				return;
			}
			AnotarNoLivroKK(m.A, BatismoDoKK);
			AnotarNoLivroKK(m.B, "");
			GD.Print($"[kaikai]    mundos: A {m.A.Nome} ({m.ChaveA}, batizado {BatismoDoKK}), B {m.B.Nome} ({m.ChaveB}), C {m.C.Nome} ({m.ChaveC}, fora do livro)");

			OFioKK();
			ATelaKK(m);
			OLivroKK(m);
			ODestruidoKK(m);
			OSaltoNaHoraKK();
			ONomeDigitadoKK(m);
			OMundoQueAindaNaoNasceuKK(m);
			AEsperaQueDesisteKK(m);
			AChegadaNoBercoKK();
		}
		catch (Exception e)
		{
			_kkFalhou++;
			GD.PrintErr($"[kaikai]   FALHA a bancada rodou inteira   {e}");
		}
		finally
		{
			KaiKaiListaNoChatDeTeste = KaiKaiSoOsDozeDeTeste = KaiKaiListaODestruidoDeTeste = KaiKaiNomeRepetidoEscolheDeTeste = false;
			KaiKaiSemClaraoDeTeste = KaiKaiSomeNaHoraDeTeste = KaiKaiClaraoNaHoraDeTeste = false;
			KaiKaiNaoEsperaOMundoDeTeste = KaiKaiCobraNaEsperaDeTeste = KaiKaiEsperaMudaDeTeste = false;
			ChegadaNoPontoFixoDeTeste = false;
			EscutaDoKaiKai = null;
			EscutaDaTransmissao = claraoAntes;
			_kaiKaiEmCurso.Clear();
			LimparTudoDaBancada();
			_batismos.Substituir(livroAntes);
			BatismosSoNaMemoriaDeTeste = memoriaAntes;
			// O PLACAR E A ULTIMA LINHA, e e ela que o lancador espera.
			GD.Print($"[kaikai] ================ {_kkOk} OK, {_kkFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// A INFRAESTRUTURA
	// =====================================================================
	/// <summary>
	/// OS TRES MENORES MUNDOS GERADOS PERTO DA TERRA (o mesmo garimpo da `--paradateste`), de nomes diferentes -- mundo gerado repete
	/// nome, e a familia do nome digitado nao pode tropecar num homonimo que ela nao plantou. Cada um tem de ter a volta da zona pro
	/// corpo (`CorpoGerado.DaZona`), que e por onde o Kai Kai acha o que gerar.
	/// </summary>
	private MundosDoKaiKaiKK? AcharMundosDoKK()
	{
		var achados = new List<(int Lado, PlanetaNoEspaco P)>();
		for (int sy = -6; sy <= 6; sy++)
			for (int sx = -6; sx <= 6; sx++)
			{
				if (Sistemas.Do(SeedDoUniverso, sx, sy) is not { } s) continue;
				for (int k = 0; k < s.Orbitas; k++)
				{
					if (k == s.OrbitaPreFeita) continue;
					PlanetaNoEspaco p = s.Planeta(k);
					ZoneKey z = Espaco.ZonaDe(p);
					if (CorpoGerado.DaZona(SeedDoUniverso, z) is null || ZonaMorta(z)) continue;
					achados.Add((MundoProcedural.DaSeed(p.Seed, p.Nome).Lado, p));
				}
			}
		achados.Sort((a, b) => a.Lado.CompareTo(b.Lado));
		var escolhidos = new List<PlanetaNoEspaco>();
		var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach ((int _, PlanetaNoEspaco p) in achados)
		{
			if (!nomes.Add(p.Nome)) continue;
			escolhidos.Add(p);
			if (escolhidos.Count == 3) return new MundosDoKaiKaiKK(escolhidos[0], escolhidos[1], escolhidos[2]);
		}
		return null;
	}

	/// <summary>O que o `AnotarDescoberta` escreve quando um jogador pisa num mundo gerado (e o batismo, se pedido).</summary>
	private void AnotarNoLivroKK(PlanetaNoEspaco p, string batismo)
	{
		NomeDePlaneta n = _batismos.Abrir(ChaveDePlaneta.De(p).Texto, p.Nome);
		n.Descobridor = "Pioneiro";
		n.AssinaturaDoDescobridor = "bancada_kaikai_pioneiro";
		n.Nome = batismo;
		n.PorQuem = batismo.Length > 0 ? "Pioneiro" : "";
	}

	/// <summary>Uma cena nova: quem salta (com a skill e o Ki cheio), um corpo colado (1 tile) e um a seis tiles.</summary>
	private (ServerPlayer Kai, ServerPlayer Colado, ServerPlayer Longe) CenaKK()
	{
		_kaiKaiEmCurso.Clear();
		LimparTudoDaBancada();
		Vec2 chao = CorredorLivre(12);
		ServerPlayer kai = ForjarComSkills("Kaioshin", chao, 5_000, [PathDoKaiKaiKK], efeitosDeSkill: true);
		ServerPlayer colado = Forjar("Colado", chao + new Vec2(ZoneCollision.TileSize, 0), bp: 1_000);
		ServerPlayer longe = Forjar("Longe", chao + new Vec2(6 * ZoneCollision.TileSize, 0), bp: 1_000);
		return (kai, colado, longe);
	}

	/// <summary>O mundo sai da memoria do servidor (e da fila de geracao): pra ele, volta a ser um mundo que ainda nao nasceu.</summary>
	private void DesligarOMundoKK(ZoneKey z)
	{
		if (_zonasGeradas.ContainsKey(z.Hash)) DescarregarOMundoGerado(z.Hash);
		_gerando.Remove(z.Hash);
	}

	/// <summary>A ultima lista que foi pra tela deste corpo (vazia = nenhuma).</summary>
	private static List<DestinoDoKaiKai> DaTelaKK(ServerPlayer pl)
	{
		if (EscutaDoKaiKai == null) return [];
		for (int i = EscutaDoKaiKai.Count - 1; i >= 0; i--)
			if (EscutaDoKaiKai[i].Quem == pl.Id) return EscutaDoKaiKai[i].Lista;
		return [];
	}

	private static string TelaEmTexto(List<DestinoDoKaiKai> tela) =>
		string.Join(", ", tela.Select(d => $"{d.Chave}={d.Nome}{(d.Gerado ? "[g]" : "")}{(d.Aqui ? "[aqui]" : "")}"));

	private static bool PiscouEm(List<(int Id, float X, float Y)> escuta, int indice, ServerPlayer quem, Vec2 onde) =>
		escuta.Count > indice && escuta[indice].Id == quem.Id
		&& Math.Abs(escuta[indice].X - onde.X) < 1f && Math.Abs(escuta[indice].Y - onde.Y) < 1f;

	private static string ClaroesEmTexto(List<(int Id, float X, float Y)>? escuta) =>
		escuta == null ? "nulo" : string.Join(" ", escuta.Select(e => $"#{e.Id}@({e.X:0},{e.Y:0})"));

	// =====================================================================
	// 1) O FIO
	// =====================================================================
	private void OFioKK()
	{
		GD.Print("[kaikai] -- 1) O FIO: a lista escrita e lida pelo FioDoKaiKai volta igual");
		List<DestinoDoKaiKai> ida =
		[
			new("Earth", "Terra", Gerado: false, Aqui: true),
			new("#12345678901234567890", "Kaiópolis", Gerado: true, Aqui: false),
			new("Heaven", "Paraíso", Gerado: false, Aqui: false),
		];
		var w = new NetDataWriter();
		FioDoKaiKai.Escrever(w, ida);
		List<DestinoDoKaiKai> volta = FioDoKaiKai.Ler(new NetDataReader(w.CopyData()));
		AfirmarKK("1a. a lista volta igual do fio: chave, nome com acento, 'mundo gerado' e 'voce esta aqui'",
				  volta.SequenceEqual(ida), TelaEmTexto(volta));

		string comprido = new string('x', FioDoKaiKai.MaxNome) + "🌍";
		var w2 = new NetDataWriter();
		FioDoKaiKai.Escrever(w2, [new DestinoDoKaiKai("#1", comprido, true, false)]);
		List<DestinoDoKaiKai> cortado = FioDoKaiKai.Ler(new NetDataReader(w2.CopyData()));
		AfirmarKK("1b. nome acima do teto chega CORTADO no teto, e nao vazio (o leitor do LiteNetLib devolve vazio acima do teto)",
				  cortado.Count == 1 && cortado[0].Nome.Length == FioDoKaiKai.MaxNome && cortado[0].Chave == "#1",
				  cortado.Count == 1 ? $"{cortado[0].Nome.Length} letras" : "nada");
	}

	// =====================================================================
	// 2) A TELA
	// =====================================================================
	private void ATelaKK(MundosDoKaiKaiKK m)
	{
		GD.Print("[kaikai] -- 2) A TELA: sem destino, a lista vai pra tela -- os doze de sempre e os mundos do livro; nao viaja nem cobra");
		(ServerPlayer kai, _, _) = CenaKK();

		// (2026-10-27, o pedido 135) O PEDAGIO E O PRECO DO SALTO (10% do Ki de um Kai recem-nascido), e nao mais o Ki cheio
		double preco = CustoDoKaiKai;
		double kiDeUmKai = KiDeUmKaiRecemNascido();
		AfirmarKK("2h. (o pedido 135) o PRECO do Kai Kai e 10% do Ki com que um Kai nasce -- um numero fixo e pequeno, igual pra todo mundo",
				  preco >= 1 && Math.Abs(preco - Math.Round(kiDeUmKai * 0.10)) < 1e-9 && preco < kai.Ficha.MaxKi / 2,
				  $"preco {preco:0}; Ki de um Kai recem-nascido {kiDeUmKai:0}; Ki maximo de quem salta {kai.Ficha.MaxKi:0}");

		kai.Ficha.Ki = preco / 2;
		EscutaDoKaiKai = [];
		List<string> falas = ApertarEOuvir(kai, "Kai_Kai");
		AfirmarKK("2a. com Ki ABAIXO do preco a tela NAO abre (a porta vem antes da lista, `kai.dm:114-115`): diz o preco e nada se cobra",
				  EscutaDoKaiKai.Count == 0 && Disse(falas, "o Kai Kai pede") && Math.Abs(kai.Ficha.Ki - preco / 2) < 1e-9,
				  Ultimos(falas));

		double kiDaTela = kai.Ficha.MaxKi / 2;
		kai.Ficha.Ki = kiDaTela;
		EscutaDoKaiKai = [];
		falas = ApertarEOuvir(kai, "Kai_Kai");
		List<DestinoDoKaiKai> tela = DaTelaKK(kai);
		AfirmarKK("2b. (o pedido 135) com Ki pela METADE (acima do preco -- o Ki cheio nao e mais pedido) a lista vai pra TELA de quem apertou, e o chat diz onde ela esta",
				  EscutaDoKaiKai.Count == 1 && tela.Count > 0 && Disse(falas, "na tela do Kai Kai"), $"{EscutaDoKaiKai.Count} lista(s) | {Ultimos(falas)}");

		string[] doze = [.. DestinosDoKaiKaiG11.Where(d => _catalogo?.Get(d) != null)];
		AfirmarKK("2c. ...os doze de sempre primeiro, na ordem do DM (`kai.dm:117`), sem nenhum faltando",
				  doze.Length == 12 && tela.Take(12).Select(d => d.Chave).SequenceEqual(doze) && tela.Take(12).All(d => !d.Gerado),
				  TelaEmTexto(tela));

		string Nome(string chave) => tela.Find(d => d.Chave == chave).Nome ?? "";
		AfirmarKK("2d. ...com o nome que o jogador le: Terra, Paraíso, Inferno, Outro Mundo, Large Space Station, Namek",
				  Nome("Earth") == "Terra" && Nome("Heaven") == "Paraíso" && Nome("Hell") == "Inferno" && Nome("Afterlife") == "Outro Mundo"
				  && Nome("Large_Space_Station") == "Large Space Station" && Nome("Namek") == "Namek",
				  TelaEmTexto(tela));
		AfirmarKK("2e. ...e a Terra, onde ele esta, marcada 'voce esta aqui' (e so ela)",
				  tela.Count(d => d.Aqui) == 1 && tela.Find(d => d.Chave == "Earth").Aqui, TelaEmTexto(tela));
		AfirmarKK("2f. ...e abrir a tela nao viaja nem cobra: o Ki de antes, na Terra, nenhum salto em curso",
				  kai.Zone.Name == "Earth" && Math.Abs(kai.Ficha.Ki - kiDaTela) < 1e-9 && FaseDoKaiKaiDeTeste(kai.Id) == null,
				  $"{kai.Zone.Name}, Ki {kai.Ficha.Ki:0}/{kai.Ficha.MaxKi:0}, fase {FaseDoKaiKaiDeTeste(kai.Id) ?? "-"}");

		KaiKaiListaNoChatDeTeste = true;
		try
		{
			EscutaDoKaiKai = [];
			falas = ApertarEOuvir(kai, "Kai_Kai");
			AfirmarKK("2g. (defeito injetado: a lista volta pro chat, como antes) a regua de 2b REPROVA -- nada vai pra tela",
					  DaTelaKK(kai).Count == 0 && Disse(falas, "para ir: Kai_Kai"), Ultimos(falas));
		}
		finally { KaiKaiListaNoChatDeTeste = false; }
	}

	// =====================================================================
	// 3) O LIVRO DE BATISMO
	// =====================================================================
	private void OLivroKK(MundosDoKaiKaiKK m)
	{
		GD.Print("[kaikai] -- 3) O LIVRO: os mundos gerados que algum jogador ja pisou entram, com o nome do batismo");
		(ServerPlayer kai, _, _) = CenaKK();
		EscutaDoKaiKai = [];
		ApertarEOuvir(kai, "Kai_Kai");
		List<DestinoDoKaiKai> tela = DaTelaKK(kai);
		DestinoDoKaiKai? a = tela.Select(d => (DestinoDoKaiKai?)d).FirstOrDefault(d => d!.Value.Chave == m.ChaveA);
		DestinoDoKaiKai? b = tela.Select(d => (DestinoDoKaiKai?)d).FirstOrDefault(d => d!.Value.Chave == m.ChaveB);
		AfirmarKK($"3a. o mundo pisado e batizado entra com o nome do BATISMO ({BatismoDoKK}, e nao {m.A.Nome}), marcado 'mundo gerado'",
				  a is { Gerado: true, Aqui: false } va && va.Nome == BatismoDoKK, TelaEmTexto(tela));
		AfirmarKK($"3b. ...o pisado sem batismo entra com o nome do mundo ({m.B.Nome})",
				  b is { Gerado: true } vb && vb.Nome == m.B.Nome, TelaEmTexto(tela));
		AfirmarKK("3c. ...o que ninguem pisou (fora do livro) nao entra",
				  !tela.Exists(d => d.Chave == m.ChaveC), TelaEmTexto(tela));
		int primeiroGerado = tela.FindIndex(d => d.Gerado);
		List<string> nomesGerados = [.. tela.Where(d => d.Gerado).Select(d => d.Nome)];
		AfirmarKK("3d. ...e os gerados vem DEPOIS dos doze, em ordem de nome",
				  primeiroGerado == 12 && nomesGerados.SequenceEqual(nomesGerados.Order(StringComparer.OrdinalIgnoreCase)),
				  TelaEmTexto(tela));

		KaiKaiSoOsDozeDeTeste = true;
		try
		{
			EscutaDoKaiKai = [];
			ApertarEOuvir(kai, "Kai_Kai");
			List<DestinoDoKaiKai> comDefeito = DaTelaKK(kai);
			AfirmarKK("3e. (defeito injetado: so os doze de sempre) a regua de 3a REPROVA -- o mundo do livro some da tela",
					  comDefeito.Count > 0 && !comDefeito.Exists(d => d.Chave == m.ChaveA), TelaEmTexto(comDefeito));
		}
		finally { KaiKaiSoOsDozeDeTeste = false; }
	}

	// =====================================================================
	// 4) O PLANETA DESTRUIDO
	// =====================================================================
	private void ODestruidoKK(MundosDoKaiKaiKK m)
	{
		GD.Print("[kaikai] -- 4) O DESTRUIDO: o planeta destruido sai da tela, e o pedido por ele diz por que");
		(ServerPlayer kai, _, _) = CenaKK();
		using (PalcoDeMortesDeBancada())
		{
			_mortos.Por(new EstadoDaMorte { Chave = "Namek", Nome = "Namek", Fase = FaseDaMorte.Destruido, Motivo = "bancada do Kai Kai" });
			_mortos.Por(new EstadoDaMorte { Chave = m.ChaveA, Nome = m.A.Nome, Fase = FaseDaMorte.Destruido, Motivo = "bancada do Kai Kai" });

			EscutaDoKaiKai = [];
			ApertarEOuvir(kai, "Kai_Kai");
			List<DestinoDoKaiKai> tela = DaTelaKK(kai);
			AfirmarKK("4a. Namek destruida e o mundo do livro destruido saem da tela; Vegeta e o outro mundo do livro ficam",
					  tela.Count > 0 && !tela.Exists(d => d.Chave == "Namek") && !tela.Exists(d => d.Chave == m.ChaveA)
					  && tela.Exists(d => d.Chave == "Vegeta") && tela.Exists(d => d.Chave == m.ChaveB),
					  TelaEmTexto(tela));

			List<string> falas = ApertarEOuvir(kai, "Kai_Kai:Namek");
			AfirmarKK("4b. ...e o pedido digitado por Namek ouve 'nao existe mais': nada se cobra, ninguem sai",
					  Disse(falas, "não existe mais") && Math.Abs(kai.Ficha.Ki - kai.Ficha.MaxKi) < 1e-9 && kai.Zone.Name == "Earth"
					  && FaseDoKaiKaiDeTeste(kai.Id) == null, Ultimos(falas));

			KaiKaiListaODestruidoDeTeste = true;
			try
			{
				EscutaDoKaiKai = [];
				ApertarEOuvir(kai, "Kai_Kai");
				List<DestinoDoKaiKai> comDefeito = DaTelaKK(kai);
				AfirmarKK("4c. (defeito injetado: o destruido fica na lista) a regua de 4a REPROVA -- Namek volta pra tela",
						  comDefeito.Exists(d => d.Chave == "Namek"), TelaEmTexto(comDefeito));
			}
			finally { KaiKaiListaODestruidoDeTeste = false; }
		}
	}

	// =====================================================================
	// 5) O SALTO NA HORA, COM O CLARAO NAS DUAS PONTAS
	// =====================================================================
	private void OSaltoNaHoraKK()
	{
		GD.Print("[kaikai] -- 5) O SALTO NA HORA: paga, pisca na saida, some, chega com o colado e pisca na chegada");
		(ServerPlayer kai, ServerPlayer colado, ServerPlayer longe) = CenaKK();
		Vec2 deKai = kai.Pos, deColado = colado.Pos;
		EscutaDaTransmissao = [];
		List<string> falas = ApertarEOuvir(kai, "Kai_Kai:Namek");
		AfirmarKK("5a. (o pedido 135) o clique PAGA na hora o PRECO do salto (e nao zera mais o Ki, o `usr.Ki = 0` do DM) e PISCA quem salta e quem esta colado, nas posicoes da Terra",
				  Math.Abs(kai.Ficha.Ki - (kai.Ficha.MaxKi - CustoDoKaiKai)) < 1e-6 && EscutaDaTransmissao.Count == 2 && PiscouEm(EscutaDaTransmissao, 0, kai, deKai)
				  && PiscouEm(EscutaDaTransmissao, 1, colado, deColado),
				  $"Ki {kai.Ficha.Ki:0}; claroes {ClaroesEmTexto(EscutaDaTransmissao)} | {Ultimos(falas)}");
		AfirmarKK("5b. ...e o corpo ainda esta na Terra no tique do clique: o clarao e desenhado no corpo, e quem fica tem de ve-lo",
				  kai.Zone.Name == "Earth" && colado.Zone.Name == "Earth" && FaseDoKaiKaiDeTeste(kai.Id) == "Saindo",
				  $"{kai.Zone.Name}, fase {FaseDoKaiKaiDeTeste(kai.Id) ?? "-"}");
		TickDoKaiKai();
		AfirmarKK("5c. ...e um tique sem o relogio vencer nao o tira de la",
				  kai.Zone.Name == "Earth" && FaseDoKaiKaiDeTeste(kai.Id) == "Saindo", kai.Zone.Name);

		VencerOKaiKaiDeTeste(kai.Id);
		Vec2 bercoDeNamek = PontoDeNascimento(ZoneKey.Premade("Namek"));
		AfirmarKK($"5d. vencidos os {KaiKai.AtrasoDaSaidaMs} ms ele CHEGA em Namek (no berco do planeta) com o colado; o de seis tiles fica",
				  kai.Zone.Name == "Namek" && colado.Zone.Name == "Namek" && longe.Zone.Name == "Earth" && Vec2.Distance(kai.Pos, bercoDeNamek) < 1f,
				  $"kai {kai.Zone.Name} {kai.Pos}, berco {bercoDeNamek}, colado {colado.Zone.Name}, longe {longe.Zone.Name}");
		AfirmarKK("5e. ...e o clarao da chegada NAO sai no tique do salto (o corpo ainda nao entrou no retrato de quem esta la)",
				  EscutaDaTransmissao.Count == 2 && FaseDoKaiKaiDeTeste(kai.Id) == "Chegando",
				  $"claroes {ClaroesEmTexto(EscutaDaTransmissao)}, fase {FaseDoKaiKaiDeTeste(kai.Id) ?? "-"}");

		VencerOKaiKaiDeTeste(kai.Id);
		AfirmarKK($"5f. vencidos os {KaiKai.AtrasoDoClaraoDaChegadaMs} ms o clarao da chegada sai, nas posicoes de Namek, pra quem saltou e pro colado",
				  EscutaDaTransmissao.Count == 4 && PiscouEm(EscutaDaTransmissao, 2, kai, kai.Pos) && PiscouEm(EscutaDaTransmissao, 3, colado, colado.Pos)
				  && FaseDoKaiKaiDeTeste(kai.Id) == null,
				  $"claroes {ClaroesEmTexto(EscutaDaTransmissao)}");

		// ---- os tres defeitos, cada um numa cena nova ----
		(kai, _, _) = CenaKK();
		EscutaDaTransmissao = [];
		KaiKaiSemClaraoDeTeste = true;
		try
		{
			UsarHabilidade(kai, "Kai_Kai:Namek");
			TerminarOKaiKaiDeTeste(kai.Id);
			AfirmarKK("5g. (defeito injetado: sem o clarao, o efeito de antes so ia ao dono) a regua de 5a REPROVA -- ninguem pisca",
					  kai.Zone.Name == "Namek" && EscutaDaTransmissao.Count == 0, $"{kai.Zone.Name}, claroes {ClaroesEmTexto(EscutaDaTransmissao)}");
		}
		finally { KaiKaiSemClaraoDeTeste = false; }

		(kai, _, _) = CenaKK();
		KaiKaiSomeNaHoraDeTeste = true;
		try
		{
			UsarHabilidade(kai, "Kai_Kai:Namek");
			AfirmarKK("5h. (defeito injetado: some no tique do clarao) a regua de 5b REPROVA -- o corpo ja nao esta na Terra",
					  kai.Zone.Name == "Namek", kai.Zone.Name);
		}
		finally { KaiKaiSomeNaHoraDeTeste = false; }

		(kai, _, _) = CenaKK();
		EscutaDaTransmissao = [];
		UsarHabilidade(kai, "Kai_Kai:Namek");
		KaiKaiClaraoNaHoraDeTeste = true;
		try
		{
			VencerOKaiKaiDeTeste(kai.Id);
			AfirmarKK("5i. (defeito injetado: o clarao da chegada no tique do salto) a regua de 5e REPROVA -- ele ja saiu",
					  kai.Zone.Name == "Namek" && EscutaDaTransmissao.Count == 4, $"claroes {ClaroesEmTexto(EscutaDaTransmissao)}");
		}
		finally { KaiKaiClaraoNaHoraDeTeste = false; }
	}

	// =====================================================================
	// 6) O DESTINO DIGITADO
	// =====================================================================
	private void ONomeDigitadoKK(MundosDoKaiKaiKK m)
	{
		GD.Print("[kaikai] -- 6) O DESTINO DIGITADO: chave ou nome, sem caixa; nome repetido recusa; fora do livro nao se alcanca");
		(ServerPlayer kai, _, _) = CenaKK();
		UsarHabilidade(kai, "Kai_Kai:large space station");
		TerminarOKaiKaiDeTeste(kai.Id);
		AfirmarKK("6a. o nome digitado sem caixa e sem sublinhado acha o destino (`Kai_Kai:large space station`)",
				  kai.Zone.Name == "Large_Space_Station", kai.Zone.Name);

		(kai, _, _) = CenaKK();
		List<string> falas = ApertarEOuvir(kai, "Kai_Kai:terra");
		AfirmarKK("6b. ...e quem ja esta la ouve 'voce ja esta em Terra', sem cobrar",
				  Disse(falas, "já está em Terra") && Math.Abs(kai.Ficha.Ki - kai.Ficha.MaxKi) < 1e-9 && FaseDoKaiKaiDeTeste(kai.Id) == null,
				  Ultimos(falas));

		falas = ApertarEOuvir(kai, $"Kai_Kai:{BatismoDoKK.ToLowerInvariant()}");
		AfirmarKK($"6c. o nome do BATISMO digitado acha o mundo do livro (`Kai_Kai:{BatismoDoKK.ToLowerInvariant()}` -> {m.ChaveA})",
				  ChaveDoKaiKaiDeTeste(kai.Id) == m.ChaveA, $"{ChaveDoKaiKaiDeTeste(kai.Id) ?? "nenhum"} | {Ultimos(falas)}");
		EsquecerOKaiKaiDeTeste(kai.Id);

		(kai, _, _) = CenaKK();
		bool encomendadoAntes = _gerando.ContainsKey(m.ZonaC.Hash) || _zonasGeradas.ContainsKey(m.ZonaC.Hash);
		falas = ApertarEOuvir(kai, $"Kai_Kai:{m.ChaveC}");
		AfirmarKK("6d. um mundo gerado FORA do livro (ninguem pisou) nao se alcanca nem pela chave: recusa, sem cobrar e sem encomendar o mundo",
				  Disse(falas, "não alcança") && Math.Abs(kai.Ficha.Ki - kai.Ficha.MaxKi) < 1e-9 && FaseDoKaiKaiDeTeste(kai.Id) == null
				  && (encomendadoAntes || (!_gerando.ContainsKey(m.ZonaC.Hash) && !_zonasGeradas.ContainsKey(m.ZonaC.Hash))),
				  Ultimos(falas));

		// DOIS MUNDOS DO LIVRO COM O MESMO NOME (mundo gerado repete nome: 22,6% dos de perto da Terra). Nao precisam existir no ceu:
		// a recusa vem antes de procurar o corpo.
		List<NomeDePlaneta> livro = [.. _batismos.Todos];
		try
		{
			_batismos.Abrir("#11", "Gemeo-111").Descobridor = "Pioneiro";
			_batismos.Abrir("#12", "Gemeo-111").Descobridor = "Pioneiro";
			falas = ApertarEOuvir(kai, "Kai_Kai:gemeo-111");
			AfirmarKK("6e. nome que aponta DOIS mundos recusa e manda escolher na tela, sem cobrar",
					  Disse(falas, "mais de um mundo") && Math.Abs(kai.Ficha.Ki - kai.Ficha.MaxKi) < 1e-9 && FaseDoKaiKaiDeTeste(kai.Id) == null,
					  Ultimos(falas));

			KaiKaiNomeRepetidoEscolheDeTeste = true;
			try
			{
				falas = ApertarEOuvir(kai, "Kai_Kai:gemeo-111");
				AfirmarKK("6f. (defeito injetado: o nome escolhe o primeiro) a regua de 6e REPROVA -- ele vai atras de um dos dois",
						  !Disse(falas, "mais de um mundo"), Ultimos(falas));
			}
			finally { KaiKaiNomeRepetidoEscolheDeTeste = false; }
		}
		finally { _batismos.Substituir(livro); }
	}

	// =====================================================================
	// 7) O MUNDO GERADO QUE AINDA NAO NASCEU
	// =====================================================================
	private void OMundoQueAindaNaoNasceuKK(MundosDoKaiKaiKK m)
	{
		GD.Print("[kaikai] -- 7) O MUNDO QUE AINDA NAO NASCEU: o salto espera a geracao, sem cobrar antes de ir, e avisa se demorar");
		(ServerPlayer kai, ServerPlayer colado, _) = CenaKK();
		DesligarOMundoKK(m.ZonaA);
		Vec2 deKai = kai.Pos;
		EscutaDaTransmissao = [];
		List<string> falas = ApertarEOuvir(kai, $"Kai_Kai:{m.ChaveA}");
		AfirmarKK("7a. pedido um mundo do livro que o servidor ainda nao tem, o salto ESPERA: nada pago (Ki cheio), ninguem pisca, ninguem sai -- e a geracao foi encomendada",
				  FaseDoKaiKaiDeTeste(kai.Id) == "EsperandoOMundo" && Math.Abs(kai.Ficha.Ki - kai.Ficha.MaxKi) < 1e-9
				  && EscutaDaTransmissao.Count == 0 && kai.Zone.Name == "Earth" && _gerando.ContainsKey(m.ZonaA.Hash),
				  $"fase {FaseDoKaiKaiDeTeste(kai.Id) ?? "-"}, Ki {kai.Ficha.Ki:0}, claroes {EscutaDaTransmissao.Count}, gerando {_gerando.ContainsKey(m.ZonaA.Hash)} | {Ultimos(falas)}");

		EnvelhecerAEsperaDoKaiKaiDeTeste(kai.Id, KaiKai.EsperaSemAvisoMs + 1);
		falas = Ouvir(TickDoKaiKai);
		AfirmarKK($"7b. ...se a geracao passa de {KaiKai.EsperaSemAvisoMs} ms, avisa ('ainda está se formando') e continua esperando",
				  Disse(falas, "ainda está se formando") && FaseDoKaiKaiDeTeste(kai.Id) == "EsperandoOMundo" && kai.Zone.Name == "Earth",
				  Ultimos(falas));
		falas = Ouvir(TickDoKaiKai);
		AfirmarKK("7c. ...e avisa UMA vez so (o tique seguinte fica calado)", !Disse(falas, "ainda está se formando"), Ultimos(falas));

		for (int i = 0; i < 4000 && FaseDoKaiKaiDeTeste(kai.Id) == "EsperandoOMundo"; i++)
		{
			TickDasGeracoes();
			TickDoKaiKai();
			if (FaseDoKaiKaiDeTeste(kai.Id) == "EsperandoOMundo") System.Threading.Thread.Sleep(5);
		}
		AfirmarKK("7d. quando o mundo nasce o salto sai: paga AGORA (o preco do salto, o pedido 135) e pisca quem salta e o colado na Terra",
				  FaseDoKaiKaiDeTeste(kai.Id) == "Saindo" && Math.Abs(kai.Ficha.Ki - (kai.Ficha.MaxKi - CustoDoKaiKai)) < 1e-6 && EscutaDaTransmissao.Count == 2
				  && PiscouEm(EscutaDaTransmissao, 0, kai, deKai),
				  $"fase {FaseDoKaiKaiDeTeste(kai.Id) ?? "-"}, Ki {kai.Ficha.Ki:0}, claroes {ClaroesEmTexto(EscutaDaTransmissao)}");

		TerminarOKaiKaiDeTeste(kai.Id);
		Vec2? chegada = ChegadaDaZonaGerada(m.ZonaA);
		AfirmarKK("7e. ...e chega no ponto que o GERADOR garantiu livre (a chegada do mundo), com o colado, piscando la",
				  kai.Zone.Equals(m.ZonaA) && colado.Zone.Equals(m.ZonaA) && chegada is { } ch && Vec2.Distance(kai.Pos, ch) < ZoneCollision.TileSize
				  && EscutaDaTransmissao.Count == 4 && PiscouEm(EscutaDaTransmissao, 2, kai, kai.Pos),
				  $"kai {kai.Zone} {kai.Pos}, chegada {chegada}, colado {colado.Zone}, claroes {ClaroesEmTexto(EscutaDaTransmissao)}");

		_saltoPronto.Remove(kai.Id);
		kai.Ficha.Ki = kai.Ficha.MaxKi;
		EscutaDoKaiKai = [];
		ApertarEOuvir(kai, "Kai_Kai");
		List<DestinoDoKaiKai> tela = DaTelaKK(kai);
		AfirmarKK("7f. ...e de la, na tela, o mundo aparece como 'voce esta aqui' (e a Terra nao)",
				  tela.Exists(d => d.Chave == m.ChaveA && d.Aqui) && !tela.Exists(d => d.Chave == "Earth" && d.Aqui), TelaEmTexto(tela));

		// ---- os dois defeitos, no mundo B (que a bancada desliga antes de cada pedido) ----
		(kai, _, _) = CenaKK();
		DesligarOMundoKK(m.ZonaB);
		KaiKaiCobraNaEsperaDeTeste = true;
		try
		{
			UsarHabilidade(kai, $"Kai_Kai:{m.ChaveB}");
			AfirmarKK("7g. (defeito injetado: o Ki cobrado no clique) a regua de 7a REPROVA -- o Ki some antes de o mundo nascer",
					  FaseDoKaiKaiDeTeste(kai.Id) == "EsperandoOMundo" && kai.Ficha.Ki == 0, $"Ki {kai.Ficha.Ki:0}, fase {FaseDoKaiKaiDeTeste(kai.Id) ?? "-"}");
		}
		finally { KaiKaiCobraNaEsperaDeTeste = false; EsquecerOKaiKaiDeTeste(kai.Id); }

		(kai, _, _) = CenaKK();
		DesligarOMundoKK(m.ZonaB);
		KaiKaiNaoEsperaOMundoDeTeste = true;
		try
		{
			UsarHabilidade(kai, $"Kai_Kai:{m.ChaveB}");
			AfirmarKK("7h. (defeito injetado: o salto nao espera o mundo) a regua de 7a REPROVA -- ele sai na hora, pagando (o preco do salto), pro mundo frio",
					  FaseDoKaiKaiDeTeste(kai.Id) == "Saindo" && Math.Abs(kai.Ficha.Ki - (kai.Ficha.MaxKi - CustoDoKaiKai)) < 1e-6,
					  $"Ki {kai.Ficha.Ki:0}, fase {FaseDoKaiKaiDeTeste(kai.Id) ?? "-"}");
		}
		finally { KaiKaiNaoEsperaOMundoDeTeste = false; EsquecerOKaiKaiDeTeste(kai.Id); }
	}

	// =====================================================================
	// 8) A ESPERA QUE DESISTE
	// =====================================================================
	private void AEsperaQueDesisteKK(MundosDoKaiKaiKK m)
	{
		GD.Print("[kaikai] -- 8) A ESPERA QUE DESISTE: o mundo que nao nasce nao prende o pedido pra sempre");
		(ServerPlayer kai, _, _) = CenaKK();
		DesligarOMundoKK(m.ZonaB);
		UsarHabilidade(kai, $"Kai_Kai:{m.ChaveB}");
		EnvelhecerAEsperaDoKaiKaiDeTeste(kai.Id, KaiKai.TetoDaEsperaMs + 1);
		List<string> falas = Ouvir(TickDoKaiKai);
		AfirmarKK($"8a. se o mundo nao nasce em {KaiKai.TetoDaEsperaMs / 1000} s o pedido cai, avisando, com o Ki inteiro e sem sair do lugar",
				  FaseDoKaiKaiDeTeste(kai.Id) == null && Disse(falas, "não terminou de se formar")
				  && Math.Abs(kai.Ficha.Ki - kai.Ficha.MaxKi) < 1e-9 && kai.Zone.Name == "Earth",
				  $"fase {FaseDoKaiKaiDeTeste(kai.Id) ?? "-"} | {Ultimos(falas)}");

		(kai, _, _) = CenaKK();
		DesligarOMundoKK(m.ZonaB);
		UsarHabilidade(kai, $"Kai_Kai:{m.ChaveB}");
		EnvelhecerAEsperaDoKaiKaiDeTeste(kai.Id, KaiKai.TetoDaEsperaMs + 1);
		KaiKaiEsperaMudaDeTeste = true;
		try
		{
			falas = Ouvir(TickDoKaiKai);
			AfirmarKK("8b. (defeito injetado: a espera muda) a regua de 8a REPROVA -- o pedido fica pendurado, calado",
					  FaseDoKaiKaiDeTeste(kai.Id) == "EsperandoOMundo" && !Disse(falas, "não terminou de se formar"), Ultimos(falas));
		}
		finally { KaiKaiEsperaMudaDeTeste = false; EsquecerOKaiKaiDeTeste(kai.Id); }
		DesligarOMundoKK(m.ZonaB);
	}

	// =====================================================================
	// 9) A CHEGADA NO BERCO DO PLANETA (2026-10-24)
	// =====================================================================
	/// <summary>
	/// O dono escolheu "Usar o berco do planeta": o Kai Kai, o Devil Bringer e o Rasgo Dimensional pousam no `PontoDeNascimento` (o marco
	/// do mapa, conferido contra a colisao), e nao no `SpawnPos` fixo -- que em Namek e agua. Pelos tres verbos de producao, com a MESMA
	/// regua (no berco, em chao firme); e a varredura dos doze da tela, sem saltar. O defeito (`ChegadaNoPontoFixoDeTeste`) devolve o
	/// ponto fixo pela propria linha da chegada.
	/// </summary>
	private void AChegadaNoBercoKK()
	{
		GD.Print("[kaikai] -- 9) A CHEGADA NO BERCO: o Kai Kai, o Devil Bringer e o Rasgo pousam no berco do planeta, em chao firme");
		ZoneKey namek = ZoneKey.Premade("Namek");
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(namek);
		Vec2 berco = PontoDeNascimento(namek);
		AfirmarKK("9a. (montagem) em Namek o ponto fixo de antes (o `SpawnPos`) e AGUA, e o berco e chao firme, longe dele",
				  mapa != null && mapa.EhAguaEm(SpawnPos) && NoChaoKK(mapa, berco) && Vec2.Distance(berco, SpawnPos) > 4 * ZoneCollision.TileSize,
				  $"fixo {CelulaEmTextoKK(mapa, SpawnPos)}, berco {CelulaEmTextoKK(mapa, berco)}");

		// A REGUA dos tres verbos: o corpo esta em Namek, no berco, em chao.
		bool NoBerco(ServerPlayer p) => p.Zone.Equals(namek) && Vec2.Distance(p.Pos, berco) < 1f && NoChaoKK(mapa, p.Pos);
		List<string> ForaDoChao() =>
			[.. DestinosDoKaiKaiG11.Where(d => !NoChaoKK(MapaDaZonaOuCatalogo(ZoneKey.Premade(d)), ChegadaDoSaltoDePlaneta(ZoneKey.Premade(d))))];
		string Varredura() => string.Join("  ", DestinosDoKaiKaiG11.Select(d =>
			$"{d} {CelulaEmTextoKK(MapaDaZonaOuCatalogo(ZoneKey.Premade(d)), ChegadaDoSaltoDePlaneta(ZoneKey.Premade(d)))}"));

		(ServerPlayer kai, ServerPlayer colado, _) = CenaKK();
		UsarHabilidade(kai, "Kai_Kai:Namek");
		TerminarOKaiKaiDeTeste(kai.Id);
		AfirmarKK("9b. o Kai Kai pousa no BERCO de Namek, em chao firme -- ele e o colado",
				  NoBerco(kai) && NoBerco(colado), $"kai {kai.Zone.Name} {CelulaEmTextoKK(mapa, kai.Pos)}, colado {colado.Zone.Name} {CelulaEmTextoKK(mapa, colado.Pos)}");

		ServerPlayer demo = ForjarComSkills("Demonio", CorredorLivre(12), 5_000, [PathDoDevilBringerKK], efeitosDeSkill: true);
		List<string> falas = ApertarEOuvir(demo, "Devil_Bringer:Namek");
		AfirmarKK("9c. ...o Devil Bringer tambem", NoBerco(demo), $"{demo.Zone.Name} {CelulaEmTextoKK(mapa, demo.Pos)} | {Ultimos(falas)}");

		ServerPlayer rasgo = ForjarComSkills("Rasgador", CorredorLivre(12), 5_000, [PathDoRasgoKK], efeitosDeSkill: true);
		falas = ApertarEOuvir(rasgo, "RiftTeleport:Namek");
		AfirmarKK("9d. ...e o Rasgo Dimensional", NoBerco(rasgo), $"{rasgo.Zone.Name} {CelulaEmTextoKK(mapa, rasgo.Pos)} | {Ultimos(falas)}");

		GD.Print($"[kaikai]    chegadas: {Varredura()}");
		List<string> fora = ForaDoChao();
		AfirmarKK("9e. os doze destinos da tela pousam todos em chao firme (o berco de cada um; o Devil Bringer e o Rasgo usam partes desta lista)",
				  fora.Count == 0, string.Join(", ", fora));

		ChegadaNoPontoFixoDeTeste = true;
		try
		{
			(kai, _, _) = CenaKK();
			UsarHabilidade(kai, "Kai_Kai:Namek");
			TerminarOKaiKaiDeTeste(kai.Id);
			AfirmarKK("9f. (defeito injetado: o salto pousa no ponto fixo de antes) a regua de 9b REPROVA -- o Kaioshin cai na agua de Namek",
					  kai.Zone.Equals(namek) && !NoBerco(kai) && mapa != null && mapa.EhAguaEm(kai.Pos), CelulaEmTextoKK(mapa, kai.Pos));

			demo = ForjarComSkills("Demonio", CorredorLivre(12), 5_000, [PathDoDevilBringerKK], efeitosDeSkill: true);
			UsarHabilidade(demo, "Devil_Bringer:Namek");
			rasgo = ForjarComSkills("Rasgador", CorredorLivre(12), 5_000, [PathDoRasgoKK], efeitosDeSkill: true);
			UsarHabilidade(rasgo, "RiftTeleport:Namek");
			AfirmarKK("9g. ...e as de 9c e 9d tambem: o Devil Bringer e o Rasgo caem na agua",
					  demo.Zone.Equals(namek) && !NoBerco(demo) && rasgo.Zone.Equals(namek) && !NoBerco(rasgo),
					  $"{CelulaEmTextoKK(mapa, demo.Pos)} / {CelulaEmTextoKK(mapa, rasgo.Pos)}");

			GD.Print($"[kaikai]    chegadas com o defeito: {Varredura()}");
			List<string> foraComDefeito = ForaDoChao();
			AfirmarKK("9h. ...e a varredura de 9e acusa os planetas em que o ponto fixo nao e chao (Namek entre eles)",
					  foraComDefeito.Contains("Namek"), string.Join(", ", foraComDefeito));
		}
		finally { ChegadaNoPontoFixoDeTeste = false; }
	}

	/// <summary>Esta posicao cai numa celula em que um corpo a pe fica (`ServeDeChao`: nem parede, nem beirada, nem agua, nem nuvem)?</summary>
	private static bool NoChaoKK(ZoneCollision? mapa, Vec2 p) =>
		mapa != null && mapa.ServeDeChao((int)MathF.Floor(p.X / ZoneCollision.TileSize), (int)MathF.Floor(p.Y / ZoneCollision.TileSize));

	private static string CelulaEmTextoKK(ZoneCollision? mapa, Vec2 p)
	{
		int cx = (int)MathF.Floor(p.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(p.Y / ZoneCollision.TileSize);
		string o = mapa == null ? "sem mapa"
			: mapa.EhAgua(cx, cy) ? "AGUA"
			: mapa.BlockedCell(cx, cy) ? "PAREDE"
			: mapa.ServeDeChao(cx, cy) ? "chao" : "BEIRADA/NUVEM";
		return $"({cx},{cy}) {o}";
	}
}
