using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ O CACHE DE ZONAS NAO ACUMULA PLANETA (`--diagcachezona`) ============================
/// O cliente guarda viva a zona de onde se sai (`World.GuardarZonaAtual`): ela fica NA ARVORE, escondida e
/// sem processar, pra a volta ser um reacender. So o ramo da cena PRE-FEITA reaproveita o guardado. Os
/// quatro ramos que montam um `PlanetaProcedural` -- nave, mente, interior do Majin e mundo sorteado --
/// criam um node NOVO a cada entrada, e o guardado da visita anterior continua onde estava.
///
/// A SUSPEITA, LIDA NO CODIGO: na segunda saida da MESMA zona gerada a chave do dicionario era
/// sobrescrita, e o node antigo perdia o unico dono que tinha. Ele seguia filho do `World`, invisivel,
/// ate o mundo inteiro morrer. O sintoma ja estava escrito no proprio `World.PedacosVivosDeTeste` (*"acaba
/// com mais de um node com esse nome"*) -- so que ninguem tinha CONTADO quantos, nem quanto pesam.
///
/// ============================ O QUE ELA CONTA ============================
/// A cada volta completa, o `World.CensoDeZonasDeTeste`: quantos nodes de zona sao filhos do `World`,
/// quantos sao `PlanetaProcedural`, quantos o cache guarda e quantos sao ORFAOS (na arvore, sem ser a
/// zona atual nem um guardado). Ao lado, a memoria: a NATIVA do Godot (os tilemaps pintados moram
/// nela), a GERENCIADA do .NET depois de uma coleta cheia (o `TerrenoGerado` de um mundo sorteado mora
/// nela) e os bytes privados do processo.
///
/// A CONTA DE NODE E O VEREDITO; A MEMORIA E NOTA. Node se conta exato e se repete igual em qualquer
/// maquina. Megabyte depende do alocador, do coletor e do que mais o processo fazia naquele quadro --
/// uma afirmacao em cima dele seria a proxima bancada instavel desta casa.
///
/// ============================ AS TRES FAMILIAS, E COMO CADA UMA REPROVA (medido, 2026-10-09) ============================
///   #  o que ela afirma                                        | ANTES do conserto (7 falhas em 11)       | DEPOIS
///   ---|-------------------------------------------------------|------------------------------------------|---------------
///   1  a MENTE, 5 idas e voltas pelos dois pacotes da telinha: | orfas 0, 1, 2, 3, 4                      | 0, 0, 0, 0, 0
///      nenhum orfao, geradas paradas, zonas <= teto + 1        | geradas 1, 2, 3, 4, 5; zonas 2..6        | 1 e 2, paradas
///      ...e a cena pre-feita volta como o MESMO node           | mesmo node (ela nunca vazou)             | mesmo node
///   2  um MUNDO SORTEADO, 5 pousos e decolagens no mesmo       | orfas 4, 5, 6, 7, 8 (os 4 da mente       | 0, 0, 0, 0, 0
///      planeta                                                 | continuam la); geradas 5..9              | 1, parada
///   3  [defeito injetado] `World.ZonaRepetidaSemSoltarDeTeste` | (a chave ainda nao tinha leitor: a       | +1, +2, +3 com
///      liga o estado de antes: +1 orfao por volta; desligada,  | metade "desfeito" reprovava -- 12, 13    | a chave; 3, 3
///      a conta para                                            | em vez de 11)                            | sem ela
///
/// A MEMORIA NATIVA, nas mesmas rodadas (sem janela): a mente vazava 6,0 MB por volta (24,1 MB em quatro)
/// e passou a +0,3 MB em quatro. O mundo sorteado, COM janela e com o defeito injetado (223x223, 17.197
/// celulas pintadas por pouso): 7,2 MB por volta (21,6 MB em tres), e parado sem ele. Sem janela o mesmo
/// vazamento vai de 0,5 a 6 MB por volta -- ver <see cref="CelulasPintadas"/> pro porque.
///
/// A NAVE E O INTERIOR DO MAJIN NAO TEM FAMILIA PROPRIA, e a razao e de construcao: os quatro ramos
/// gerados so se encontram numa linha (a que guarda a zona que sai), e e nela que mora o conserto. Erguer
/// uma nave-capital ou ser absorvido duas vezes custaria um roteiro inteiro pra exercitar a mesma linha.
/// (A `--diagmajin` entra no interior com a planta UMA vez so -- ela roda de vizinha, e rodou verde
/// depois do conserto, mas nao repete a zona gerada.)
///
/// ============================ POR QUE O POUSO E VOADO, E NAO ENCOMENDADO ============================
/// A bancada poe o corpo na orbita UMA vez (`GameServer.PorNaOrbitaDeUmGeradoNoTeste` -- ver la o porque).
/// Dali em diante o roteiro e o do jogador: o piloto voa contra o disco, o `TickDoEspaco` de producao
/// pousa, e a decolagem sai pelo canal de habilidade e devolve o corpo a 90 px do disco. Encomendar o
/// pouso direto ao servidor mediria a mesma linha do cliente, mas por um caminho que jogador nenhum tem.
/// ====================================================================================================
///
/// COMO RODAR (sem janela; com janela tambem roda, e a memoria nativa passa a incluir o desenho):
///
///     Godot --headless --path . --host --rede 7923 --campoteste 8 --diagcachezona \
///           --raca Human --conta bancada_cachezona --nome Cachezona
/// </summary>
public partial class RoboDoCacheDeZona : Node
{
	private static GameClient? C => GameClient.Instance;
	private static Jandirus.Server.GameServer? S => Jandirus.Server.GameServer.Instance;

	/// <summary>
	/// Quantas idas e voltas nas familias 1 e 2. CINCO porque a pergunta e "cresce com N?" e nao "sobra
	/// um?": a primeira volta nunca vaza (o node fica guardado, com dono), a segunda ja deixa um orfao, e
	/// so da terceira em diante se ve que a conta NAO PARA. 0, 1, 2, 3, 4 e uma reta; 0, 1 poderia ser um
	/// node a mais e pronto.
	/// </summary>
	private const int Voltas = 5;

	/// <summary>Voltas com o defeito ligado (familia 3): tres pontos bastam pra ver a reta 1, 2, 3.</summary>
	private const int VoltasComDefeito = 3;

	/// <summary>Voltas depois de desligar o defeito: a conta tem de PARAR onde estava.</summary>
	private const int VoltasDepoisDoDefeito = 2;

	/// <summary>
	/// Quanto se fica em cada lugar antes de sair de novo. Meio segundo deixa o pintor montar os pedacos em
	/// volta do corpo -- um node vazado sem pedaco nenhum pintado pesaria menos do que pesa em jogo.
	/// </summary>
	private const double Assento = 0.5;

	private const double Mb = 1024.0 * 1024.0;

	// =====================================================================
	// PLACAR
	// =====================================================================
	private readonly List<string> _linhas = [];
	private readonly List<Amostra> _amostras = [];
	private int _ok, _falhou;

	private void Afirmar(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _ok++; _linhas.Add($"  OK     {oque}"); GD.Print($"[cachezona]   OK    {oque}"); return; }
		_falhou++;
		_linhas.Add($"  FALHA  {oque}   {detalhe}");
		GD.PrintErr($"[cachezona]   FALHA {oque}   {detalhe}");
	}

	private void Nota(string t) { _linhas.Add($"   --    {t}"); GD.Print($"[cachezona]    --   {t}"); }

	// =====================================================================
	// RELOGIO E ESPERA
	// =====================================================================
	private double _relogio;
	private bool _rodando, _fechou;

	public override void _Process(double delta)
	{
		_relogio += delta;
		if (_rodando || _fechou) return;
		if (C is not { Connected: true }) return;
		if (World.Instancia == null) return;
		if (_relogio < 4.0) return;   // o mundo assenta: o primeiro quadro ainda monta pedaco

		_rodando = true;
		_ = Rodar();
	}

	private async Task Esperar(double s)
	{
		double ate = _relogio + s;
		while (_relogio < ate && !_fechou)
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
	}

	/// <summary>
	/// ESPERA O MUNDO MONTAR A ZONA, e nao a rede dizer que ela mudou.
	///
	/// O `ZoneChanged` chega e o `GameClient.Zone` troca na hora; o `World.CarregarZona` so roda dois
	/// quadros depois, com a tela de carregamento ja no ar. Contar node nesse vao e contar a zona
	/// ANTERIOR. As duas pontas sao cobradas juntas: a zona que o `World` montou e a que a rede diz.
	/// </summary>
	private async Task<bool> EsperarOMundoEm(Func<ZoneKey, bool> lugar, double prazo)
	{
		double ate = _relogio + prazo;
		while (_relogio < ate && !_fechou)
		{
			if (World.Instancia is { } mundo && C is { } cli
				&& lugar(mundo.CensoDeZonasDeTeste().Zona) && lugar(cli.Zone)) return true;
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
		return false;
	}

	private static string OndeEstou() =>
		$"rede: `{C?.Zone}` | mundo: `{World.Instancia?.CensoDeZonasDeTeste().Zona}` | corpo em {World.Instancia?.PosicaoLocal}";

	// =====================================================================
	// A AMOSTRA
	// =====================================================================
	private readonly record struct Amostra(
		string Rotulo, ulong Atual, int NaArvore, int Geradas, int Guardadas, int Orfas, int Teto,
		int CelulasAoSair, int CelulasNasGeradas,
		double NativaMb, double GerenciadaMb, double ProcessoMb, int Nodes)
	{
		public string Linha() =>
			$"{Rotulo,-34} | zonas na arvore {NaArvore} | geradas {Geradas} | guardadas {Guardadas} | ORFAS {Orfas}"
			+ $" | celulas ao sair {CelulasAoSair}, nas geradas da arvore {CelulasNasGeradas}"
			+ $" | nativa {NativaMb:0.0} MB | gerenciada {GerenciadaMb:0.0} MB | processo {ProcessoMb:0} MB | nodes {Nodes}";
	}

	/// <summary>
	/// QUANTAS CELULAS A ZONA ATUAL TEM PINTADAS, somando as camadas -- lido logo antes de sair dela.
	///
	/// ============================ E O QUE DA TAMANHO AO MEGABYTE ============================
	/// Um node de zona pesa pelo que o pintor montou nele, e nao pelo mapa -- MEDIDO (2026-10-09): 16.384
	/// celulas da mente = 6,0 MB, e 17.197 de um mundo de 223x223 = 7,2 MB. Perto de 400 bytes de memoria
	/// nativa por celula pintada.
	///
	/// E o peso com que ele FICA nao e sempre o peso com que ele saiu: no quadro da decolagem o pintor de
	/// um mundo sorteado as vezes solta os pedacos todos antes de o node ser escondido (`[pedacos] soltei
	/// 4, ficam 0` no log). Nas rodadas sem janela isso aconteceu na maioria dos pousos, e o mesmo planeta
	/// vazava 6 MB numa volta e 0,5 MB na outra; na rodada com janela, em nenhum dos dez. Sem os DOIS
	/// numeros ao lado da memoria, essa diferenca pareceria erro de medida.
	/// ======================================================================================
	/// </summary>
	private static int CelulasPintadas() =>
		World.Instancia?.CamadasDoCenarioDeTeste.Sum(c => c.GetUsedCells().Count) ?? 0;

	/// <summary>
	/// As celulas que continuam pintadas em TODOS os `PlanetaProcedural` filhos do World -- a zona atual,
	/// as guardadas e as orfas. No espaco e depois de sair da mente nao ha gerada atual, entao o numero e
	/// exatamente o que as geradas ESCONDIDAS ainda seguram.
	/// </summary>
	private static int CelulasNasGeradas()
	{
		if (World.Instancia is not { } mundo) return 0;

		int total = 0;
		foreach (Node zona in mundo.GetChildren())
		{
			if (zona is not PlanetaProcedural) continue;
			foreach (Node camada in zona.GetChildren())
				if (camada is TileMapLayer pintada) total += pintada.GetUsedCells().Count;
		}
		return total;
	}

	/// <summary>
	/// O CENSO E A MEMORIA, no mesmo instante.
	///
	/// A COLETA CHEIA VEM ANTES DA LEITURA, e ela e o que faz o numero gerenciado querer dizer alguma
	/// coisa: sem ela a leitura inclui o lixo que o coletor ainda nao levou, e um planeta LIBERADO pareceria
	/// vazado ate a proxima coleta. Um planeta vazado de verdade sobrevive a ela -- o node nativo segura o
	/// objeto C# dele, e o objeto segura o `TerrenoGerado`.
	/// </summary>
	private Amostra Amostrar(string rotulo, int celulas = 0)
	{
		var censo = World.Instancia!.CensoDeZonasDeTeste();
		double gerenciada = GC.GetTotalMemory(forceFullCollection: true) / Mb;
		double nativa = OS.GetStaticMemoryUsage() / Mb;
		double processo;
		using (var eu = System.Diagnostics.Process.GetCurrentProcess()) processo = eu.PrivateMemorySize64 / Mb;

		var a = new Amostra(rotulo, censo.Atual, censo.NaArvore, censo.Geradas, censo.Guardadas, censo.Orfas,
							censo.Teto, celulas, CelulasNasGeradas(), nativa, gerenciada, processo,
							(int)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount));
		_amostras.Add(a);
		GD.Print($"[cachezona]    ..   {a.Linha()}");
		return a;
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private async Task Rodar()
	{
		GD.Print("[cachezona] ============ O CACHE DE ZONAS NAO ACUMULA PLANETA ============");

		try
		{
			if (S is not { } servidor || C is not { } cli)
			{
				Afirmar("o servidor esta neste processo (rode com `--host`)", false,
						"sem `--host` nao ha quem ponha o corpo na orbita de um mundo sorteado");
				return;
			}

			int eu = cli.LocalId;
			ZoneKey casa = cli.Zone;

			Amostra chegada = Amostrar("chegada");
			Afirmar($"(montagem) o corpo nasce numa zona PRE-FEITA (`{casa.Name}`), e ela e a unica zona na "
					+ "arvore, sem nada guardado",
					casa.Kind == ZoneKey.KindPremade
					&& chegada is { NaArvore: 1, Geradas: 0, Guardadas: 0, Orfas: 0 },
					chegada.Linha());

			if (!await AMente(cli, casa)) return;

			if (servidor.PorNaOrbitaDeUmGeradoNoTeste(eu) is not { } mundo)
			{
				Afirmar("(montagem) ha um mundo SORTEADO vivo no universo pra pousar", false,
						"nenhum procedural nas chunks varridas");
				return;
			}

			if (!await OMundoSorteado(cli, servidor, mundo)) return;
			await ODefeitoInjetado(cli, servidor, mundo);
		}
		catch (Exception e)
		{
			Afirmar($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			World.ZonaRepetidaSemSoltarDeTeste = false;
			Fechar();
		}
	}

	// =====================================================================
	// 1. A MENTE
	// =====================================================================
	/// <summary>
	/// ENTRA E SAI DA PROPRIA MENTE, sempre a mesma zona (`Interdimension#id`).
	///
	/// OS DOIS PACOTES DA TELINHA (`LocalPlayer` manda a atividade e a habilidade ao escolher a profunda),
	/// na mesma ordem e no mesmo canal -- a porta que a `--diagreflexo` e a `--diagtrilha` ja usam. A
	/// saida e o verb `sairdamente`, a seca. A onda de 1,8 s da ida corre inteira: a bancada nao a pula.
	/// </summary>
	private async Task<bool> AMente(GameClient cli, ZoneKey casa)
	{
		GD.Print($"[cachezona] --- 1. a mente: {Voltas} idas e voltas ---");

		ulong nodeDaCasa = _amostras[^1].Atual;   // a amostra da chegada: a zona atual E a de casa
		var serie = new List<Amostra>();
		for (int k = 1; k <= Voltas; k++)
		{
			cli.SendActivity(Protocol.Activity.Meditando);
			await Esperar(0.3);
			cli.SendHabilidade("mente");
			if (!await EsperarOMundoEm(DimensaoMental.EhAMente, 8.0))
			{
				Afirmar($"(roteiro) volta {k}: o corpo chegou na mente (meditar + mergulhar)", false, OndeEstou());
				return false;
			}
			await Esperar(Assento);

			int celulas = CelulasPintadas();
			cli.SendHabilidade("sairdamente");
			bool voltou = await EsperarOMundoEm(z => z.Hash == casa.Hash, 6.0);
			cli.SendActivity(Protocol.Activity.Parado);
			if (!voltou)
			{
				Afirmar($"(roteiro) volta {k}: o corpo voltou da mente pra `{casa.Name}`", false, OndeEstou());
				return false;
			}
			await Esperar(Assento);

			serie.Add(Amostrar($"mente, volta {k}", celulas));
		}

		Julgar("1. a MENTE", "volta", serie);

		// A OUTRA METADE DO CACHE, QUE NAO PODE MUDAR: a cena pre-feita e a unica que o `CarregarZona`
		// REUSA, e o conserto mora na linha por onde ela tambem passa ao sair. Se ele soltasse o node
		// errado, `casa` voltaria relida do disco -- com outra identidade, e a contagem de cima nem notaria.
		Afirmar($"...e a cena PRE-FEITA de `{casa.Name}` voltou do cache nas {Voltas} voltas como o MESMO node "
				+ "(reacendida, e nao relida do disco)",
				nodeDaCasa != 0 && serie.All(a => a.Atual == nodeDaCasa),
				$"node da chegada {nodeDaCasa}, nas voltas: {string.Join(", ", serie.Select(a => a.Atual))}");
		return true;
	}

	// =====================================================================
	// 2. O MUNDO SORTEADO
	// =====================================================================
	/// <summary>
	/// POUSA E DECOLA DO MESMO MUNDO SORTEADO. E o caso pesado dos quatro: aqui o node vazado nao segura
	/// so tilemap -- ele segura o `TerrenoGerado` inteiro do planeta, que o cliente gera de novo a cada
	/// pouso (o da mente, da nave e do interior do Majin e uma planta compartilhada).
	///
	/// A AMOSTRA E NO ESPACO, depois de cada decolagem: e ao SAIR que a chave e escrita.
	/// </summary>
	private async Task<bool> OMundoSorteado(GameClient cli, Jandirus.Server.GameServer servidor, PlanetaNoEspaco mundo)
	{
		GD.Print($"[cachezona] --- 2. um mundo sorteado: {Voltas} pousos e decolagens ---");

		Nota($"o mundo sorteado desta rodada: {mundo.Nome} ({MundoProcedural.DaSeed(mundo.Seed, mundo.Nome).Descricao()})");
		if (!await EsperarOMundoEm(Espaco.EhEspaco, 8.0))
		{
			Afirmar($"(montagem) o corpo chegou a orbita de {mundo.Nome}", false, OndeEstou());
			return false;
		}
		await Esperar(Assento);

		var serie = new List<Amostra>();
		for (int k = 1; k <= Voltas; k++)
		{
			if (await PousarEDecolar(cli, servidor, mundo, $"pouso {k}") is not { } celulas) return false;
			serie.Add(Amostrar($"mundo sorteado, decolagem {k}", celulas));
		}

		Julgar("2. o MUNDO SORTEADO", "pouso", serie);
		return true;
	}

	/// <summary>
	/// Uma volta inteira: voar contra o disco, pousar, assentar, decolar. Devolve quantas celulas o mundo
	/// tinha pintadas na hora de decolar; nulo = o roteiro quebrou (e a falha ja foi anotada).
	/// </summary>
	private async Task<int?> PousarEDecolar(GameClient cli, Jandirus.Server.GameServer servidor,
											PlanetaNoEspaco mundo, string qual)
	{
		ZoneKey chao = Espaco.ZonaDe(mundo);

		World.Instancia?.IrAteDeTeste(mundo.Pos);
		bool pousou = await EsperarOMundoEm(z => z.Equals(chao), 20.0);
		World.Instancia?.PararDeTeste();
		if (!pousou)
		{
			Afirmar($"(roteiro) {qual}: o corpo POUSOU em {mundo.Nome} voando contra o disco", false, OndeEstou());
			return null;
		}

		// O FOLEGO DE VOLTA. Cada volta passa perto de um segundo no vacuo, e o vacuo cobra 5% da vida de
		// cada membro por segundo de quem nao respira la: sem esta linha a decima volta mataria a cobaia, e
		// a bancada passaria a medir o Outro Mundo.
		servidor.DePeNoTeste(cli.LocalId);
		await Esperar(Assento);

		int celulas = CelulasPintadas();
		cli.SendHabilidade("decolar");
		if (!await EsperarOMundoEm(Espaco.EhEspaco, 8.0))
		{
			Afirmar($"(roteiro) {qual}: o corpo DECOLOU de {mundo.Nome}", false, OndeEstou());
			return null;
		}
		await Esperar(Assento);
		return celulas;
	}

	// =====================================================================
	// 3. O DEFEITO INJETADO
	// =====================================================================
	/// <summary>
	/// A FAMILIA 2 DE NOVO, COM O ESTADO DE ANTES LIGADO -- no mesmo processo e no mesmo planeta.
	///
	/// E o unico "antes" que se compara com o "depois" sem asterisco: mesma DLL, mesmo mundo sorteado,
	/// mesma memoria de base. Com a chave ligada a linha do conserto nao roda, e cada decolagem tem de
	/// deixar UM planeta a mais sem dono. Com ela desligada a conta para -- os orfaos que o defeito fez
	/// continuam la, que e exatamente o que "orfao" quer dizer.
	/// </summary>
	private async Task ODefeitoInjetado(GameClient cli, Jandirus.Server.GameServer servidor, PlanetaNoEspaco mundo)
	{
		GD.Print("[cachezona] --- 3. o defeito injetado: a chave sobrescrita sem soltar ---");

		Amostra antes = _amostras[^1];
		var comDefeito = new List<Amostra>();

		World.ZonaRepetidaSemSoltarDeTeste = true;
		try
		{
			for (int k = 1; k <= VoltasComDefeito; k++)
			{
				if (await PousarEDecolar(cli, servidor, mundo, $"pouso {k} com o defeito") is not { } celulas) return;
				comDefeito.Add(Amostrar($"DEFEITO, decolagem {k}", celulas));
			}
		}
		finally
		{
			World.ZonaRepetidaSemSoltarDeTeste = false;
		}

		string subida = string.Join(", ", comDefeito.Select(a => a.Orfas - antes.Orfas));
		Afirmar("(defeito injetado: a zona que sai sobrescreve a chave sem soltar a que estava nela) cada pouso "
				+ $"repetido deixa UM planeta orfao a mais na arvore -- orfas a mais por volta: {subida}",
				comDefeito.Select(a => a.Orfas - antes.Orfas).SequenceEqual(Enumerable.Range(1, VoltasComDefeito)),
				$"esperado 1..{VoltasComDefeito}");
		Afirmar("(defeito injetado) ...e sao `PlanetaProcedural` de verdade, filhos do World: geradas a mais "
				+ $"por volta: {string.Join(", ", comDefeito.Select(a => a.Geradas - antes.Geradas))}",
				comDefeito.Select(a => a.Geradas - antes.Geradas).SequenceEqual(Enumerable.Range(1, VoltasComDefeito)),
				$"esperado 1..{VoltasComDefeito}");
		ContarAMemoria("COM o defeito (cada volta vaza um planeta)", antes, comDefeito[^1], VoltasComDefeito);

		var depois = new List<Amostra>();
		for (int k = 1; k <= VoltasDepoisDoDefeito; k++)
		{
			if (await PousarEDecolar(cli, servidor, mundo, $"pouso {k} depois do defeito") is not { } celulas) return;
			depois.Add(Amostrar($"defeito desfeito, decolagem {k}", celulas));
		}

		Afirmar("...e o defeito foi desfeito: com a chave desligada a conta PARA de crescer (orfas: "
				+ $"{string.Join(", ", depois.Select(a => a.Orfas))}; os {comDefeito[^1].Orfas - antes.Orfas} que o "
				+ "defeito fez continuam la ate o mundo morrer)",
				depois.All(a => a.Orfas == comDefeito[^1].Orfas && a.Geradas == comDefeito[^1].Geradas),
				$"esperado {comDefeito[^1].Orfas} em todas");
	}

	// =====================================================================
	// O VEREDITO DE UMA FAMILIA
	// =====================================================================
	/// <summary>
	/// AS TRES PERGUNTAS, e as tres sao a mesma por lados diferentes -- de proposito: "nenhum orfao" e a
	/// pergunta exata; "as geradas nao crescem" e a que o dono escreveu no pedido (contar os
	/// `PlanetaProcedural` filhos do `World`); e "cabe no teto" e a promessa do proprio cache.
	/// </summary>
	private void Julgar(string familia, string unidade, List<Amostra> serie)
	{
		Afirmar($"{familia}: NENHUM node de zona fica sem dono em {unidade} nenhuma "
				+ $"(orfas por {unidade}: {string.Join(", ", serie.Select(a => a.Orfas))})",
				serie.All(a => a.Orfas == 0), "esperado 0 em todas");
		Afirmar($"...e os `PlanetaProcedural` filhos do World NAO crescem com as idas e voltas "
				+ $"(por {unidade}: {string.Join(", ", serie.Select(a => a.Geradas))})",
				serie.All(a => a.Geradas == serie[0].Geradas), $"esperado {serie[0].Geradas} em todas");
		Afirmar($"...e as zonas na arvore cabem no teto do cache mais a atual, {serie[0].Teto}+1 "
				+ $"(por {unidade}: {string.Join(", ", serie.Select(a => a.NaArvore))})",
				serie.All(a => a.NaArvore <= a.Teto + 1), $"teto {serie[0].Teto}+1");

		// DA PRIMEIRA A ULTIMA AMOSTRA: a primeira ja traz o node velho que o cache guarda de proposito,
		// entao o que sobra entre as duas e so o que CRESCEU -- `Voltas - 1` voltas de crescimento.
		ContarAMemoria(familia, serie[0], serie[^1], serie.Count - 1);
	}

	private void ContarAMemoria(string qual, Amostra de, Amostra ate, int voltas)
	{
		double nativa = ate.NativaMb - de.NativaMb, gerenciada = ate.GerenciadaMb - de.GerenciadaMb;
		Nota($"memoria, {qual}: nativa {nativa:+0.00;-0.00} MB, gerenciada {gerenciada:+0.00;-0.00} MB, "
			 + $"processo {ate.ProcessoMb - de.ProcessoMb:+0.0;-0.0} MB em {voltas} volta(s) = "
			 + $"{(nativa + gerenciada) / Math.Max(voltas, 1):+0.00;-0.00} MB por volta (nativa + gerenciada); "
			 + $"celulas nas geradas da arvore: {de.CelulasNasGeradas} -> {ate.CelulasNasGeradas}");
	}

	// =====================================================================
	// FIM
	// =====================================================================
	private void Fechar()
	{
		_fechou = true;
		GD.Print("");
		GD.Print("========== BANCADA DO CACHE DE ZONAS ==========");
		foreach (Amostra a in _amostras) GD.Print($"   ..    {a.Linha()}");
		foreach (string l in _linhas) GD.Print(l);
		GD.Print(_falhou == 0
			? $"[cachezona] ===== TUDO OK: {_ok} afirmacoes ====="
			: $"[cachezona] ===== {_falhou} FALHA(S) em {_ok + _falhou} afirmacoes =====");
		GetTree().CreateTimer(1.0).Timeout += () => GetTree().Quit(_falhou == 0 ? 0 : 1);
	}
}
