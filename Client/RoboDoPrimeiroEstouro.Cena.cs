using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// AS CINEMATICAS DE TRANSFORMACAO, QUADRO A QUADRO (`--diagestouro --temas --cenas &lt;lista&gt;`) -- A RODADA DE MEDICAO.
/// Nao cobra nada: da o tamanho de cada pedaco, com rotulo. E dela que saem os numeros da regua da primeira
/// cinematica (`AquecerACena`, na rodada `--temas`), os do cabecalho do `Transformacao.Ensaiar` e os do beat que
/// assume (`AcompanharAPrimeiraCena`).
///
/// ============================ O QUE ELA FAZ ============================
///   * `--cenas ssj1,ssj2,blue`    toca a cena de cada forma da lista, na ordem, do NASCIMENTO AO FIM, com um evento
///                                 por beat -- o custo de primeiro uso de cada efeito da cena cai no quadro do beat
///                                 que o dispara. `id:E` toca a de ESTREIA (a cheia, com musica e, no `ssj1`, a
///                                 tempestade); sem sufixo, a ENCURTADA. Cada cena nasce so com o pacote da
///                                 transformacao, pelo `World.AoMudarForma`, que e por onde o do servidor entra;
///   * `... --cenapartida`         antes da primeira cena, cada PECA do quadro do nascimento nasce sozinha, num quadro
///                                 so dela (a folha lida do disco, o primeiro material do shader, a chama desenhada,
///                                 a pedra, a volta a base) -- e o que sobra pro nascimento de verdade e o resto;
///   * `... --cenacorte 12`        corta cada cena aos tantos segundos (a estreia do `ssj3` tem 140 s);
///   * `... --cenacoleta`          entre uma cena e a seguinte, com a fumaca da anterior ja sumida, uma coleta forcada
///                                 do .NET: o que so um involucro C# segurava sai do cache do Godot, e a cena seguinte
///                                 mostra o que volta do disco e quanto custa. No fim sai a lista do que ficou na
///                                 memoria e do que saiu, depois de cada coleta.
///
/// O `_Process` DE CADA CENA SAI PARTIDO EM PEDACOS (`Transformacao.EspiaoDePedaco`): debaixo de cada quadro caro, quanto
/// foi a cratera, a fumaca, cada passo do `Vestir`, o clarao, o som. E COM `--verbose` CADA ARQUIVO QUE A THREAD
/// PRINCIPAL LEU GANHA O QUADRO DELE (`EscutaDeCarga`: o Godot escreve `Completed load ... at thread -1` a cada leitura
/// sincrona) -- o nome do arquivo sai no relatorio, sem ninguem casar o log com as marcas na mao.
///
/// O DESENHO SAI PARTIDO EM DOIS: o PREPARO do quadro (`RenderingServer.GetFrameSetupTimeCpu`: os recursos sujos, e
/// e ali que a thread principal espera um shader que ainda esta compilando) e a TELA
/// (`ViewportGetMeasuredRenderTimeCpu`: o desenho da janela, onde a pipeline e montada). Com `--verbose` a rodada
/// imprime uma marca por gesto e por quadro, pra as linhas `Shader cache miss` e `Loading resource` do proprio Godot
/// cairem entre duas delas -- foi assim que o shader que se esperava ganhou nome.
///
/// ============================ O QUE ELA MEDIU (2026-10-08, cache de shader em disco vazio) ============================
/// Os numeros de "antes" sao de antes do conserto; no binario de hoje o jogo de antes se mede com `--semensaiodacena`
/// na linha (o defeito do `Aquecimento`).
///
///     AS PECAS DO QUADRO DO NASCIMENTO, cada uma sozinha (`--cenapartida`, duas corridas):
///       a folha `AuraSSjBig.tres` lida do disco ................... 2,9 a 3,8 ms de script
///       o primeiro material do `Aura.gdshader`, sem desenhar ..... 1,5 a 1,7 ms -- e e DENTRO dessa chamada que o
///                                                                  Godot acusa o `Shader cache miss` e comeca a
///                                                                  compilar, em segundo plano
///       a chama desenhada um segundo e meio depois ............... 1,6 a 1,9 ms de script e 1 de desenho (uma
///                                                                  pipeline), sem espera nenhuma
///       a volta a base (so a bancada a manda antes de uma cena) .. 4,7 a 6,7 ms na primeira vez, 0,3 depois
///       o nascimento, com tudo isso ja pago ...................... 8,3 a 9,5 ms na chamada, contra 1,6 a 1,9 de uma
///                                                                  cena qualquer: o codigo de primeira vez
///     A PRIMEIRA CENA INTEIRA, sem pecas: 43 a 45 ms no quadro do nascimento (com a volta a base, que a rodada
///     ainda mandava nele), 21 deles no PREPARO do desenho.
///
///     POR FORMA, o script do quadro em que a cena nasce: `ssj2`, `ssj3` e `ssj4` (a folha do `ssj1`) 3,6 a 5,1 ms;
///     `blue` 6,0 a 6,3 e `ssg` 11,4 -- a folha de chama de cada linha, lida ali. Depois: 3,2 a 3,6 todas.
///
///     E O RESTO DA CENA, medido com os pedacos e com a coleta entre as cenas (`--cenacoleta`): o beat que ASSUME 28 a
///     36 ms na primeira cena do processo, 31 a 38 na primeira de cada outra forma e 18 a 21 numa forma repetida -- a
///     fumaca da cratera, sozinha, 14 a 23 em TODA cena; cada beat com som 2,5 a 5,6; o primeiro piscar de cabelo 12.
///     Era tudo arquivo lido do disco pela thread principal (na estreia do `ssj1`, tambem os dois trovoes da
///     tempestade), e ganhou conserto e regua: o bloco "E O BEAT QUE ASSUME" do cabecalho do `RoboDoPrimeiroEstouro`.
///     DEPOIS: 4,4 a 6,5 / 3,7 a 7,6 / 3,2 a 3,7, e nenhum arquivo lido durante a cena alem do script dela (que
///     ganhou dono depois: o arquivo `RoboDoPrimeiroEstouro.Scripts.cs`). O que sobra e codigo de primeira vez -- o
///     primeiro `_Process` da primeira cena, 9 a 13 ms -- e, com rotulo nenhum, os tiques mais gordos do servidor que
///     este processo hospeda (8 a 16 ms, em instantes quaisquer).
/// </summary>
public partial class RoboDoPrimeiroEstouro
{
	/// <summary>`--cenas &lt;lista&gt;`: as formas cuja cena a rodada toca, na ordem, separadas por virgula. Vazia = a rodada nao e esta.</summary>
	private readonly string[] _listaDasCenas = ListaDasCenas();

	private bool RodadaDasCenas => _listaDasCenas.Length > 0;

	/// <summary>`--cenapartida`: as pecas do quadro do nascimento, uma por quadro, antes da primeira cena. Ver <see cref="FazerAPeca"/>.</summary>
	private readonly bool _cenaPartida = Tem("--cenapartida");

	/// <summary>`--cenacorte &lt;s&gt;`: a idade em que cada cena e cortada. Zero = ela roda ate o fim.</summary>
	private readonly double _corteDaCena = NumeroDaLinha("--cenacorte");

	/// <summary>O Godot foi aberto com `--verbose`? Ai cada gesto e cada quadro da rodada deixam uma marca no log.</summary>
	private static readonly bool SaidaVerbosa = OS.IsStdOutVerbose();

	private static string[] ListaDasCenas()
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, "--cenas");
		return i >= 0 && i + 1 < a.Length
			? a[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			: Array.Empty<string>();
	}

	private static double NumeroDaLinha(string flag)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, flag);
		return i >= 0 && i + 1 < a.Length
			   && double.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
			? v : 0;
	}

	private bool _ganchoDasCenas, _calarAntes;
	private int _pecaDaCena, _proximaCena;

	/// <summary>A cena em curso, o rotulo dela, quantos beats ela ja disparou e ha quanto tempo ela roda.</summary>
	private Transformacao? _cenaMedida;
	private string _rotuloDaCenaMedida = "";
	private int _beatsDaCenaMedida;
	private double _idadeDaCenaMedida;

	private Node2D? _palcoDaMedicao;

	/// <summary>O que as pecas carregaram e montaram fora da arvore: segurado, senao sairia do cache antes da cena.</summary>
	private readonly List<Resource> _presosDaMedicao = [];

	/// <summary>
	/// O DESENHO DE CADA QUADRO, PARTIDO: o preparo e a tela, lidos no fim do script do quadro SEGUINTE (o Godot
	/// entrega os do ultimo quadro desenhado). O indice 0 e o quadro <see cref="_primeiroQuadroPartido"/>.
	/// </summary>
	private readonly List<(double Preparo, double Tela)> _desenhoPartido = new(16384);
	private int _primeiroQuadroPartido = -1;

	private static void MarcaVerbosa(string oque)
	{
		if (SaidaVerbosa) GD.Print("[cena-marca] " + oque);
	}

	// =====================================================================
	// OS PEDACOS DE UM QUADRO DE CENA, E OS ARQUIVOS QUE ELE LEU DO DISCO
	// =====================================================================
	/// <summary>
	/// `--cenacoleta`: entre uma cena e a seguinte, com a fumaca da anterior ja sumida, uma coleta forcada do .NET.
	/// E o que tira do cache do Godot tudo o que so um involucro C# segurava -- a cena seguinte mostra o que volta
	/// do disco, e quanto custa. Ver <see cref="ColetarEOlharOCache"/>.
	/// </summary>
	private readonly bool _cenaColeta = Tem("--cenacoleta");

	private bool _coletarAntes;
	private int _raiosDaCenaMedida;

	/// <summary>Os pedacos que a espia juntou no quadro corrente, e os de cada quadro que valeu guardar.</summary>
	private readonly List<(string Qual, double Ms)> _pedacosDoQuadro = [];
	private readonly Dictionary<int, (string Qual, double Ms)[]> _pedacos = [];

	/// <summary>Os arquivos que a thread principal leu do disco em cada quadro -- so com `--verbose`. Ver <see cref="EscutaDeCarga"/>.</summary>
	private readonly Dictionary<int, string[]> _lidos = [];
	private EscutaDeCarga? _escutaDeCarga;

	private readonly List<string> _notasDeCache = [];

	/// <summary>O pedaco que entra na lista, e a soma a partir da qual o quadro e guardado, em ms.</summary>
	private const double PedacoQueConta = 0.05, QuadroComPedacos = 1.0;

	/// <summary>A espia do `Transformacao.EspiaoDePedaco`: um pedaco do `_Process` de uma cena acabou de correr.</summary>
	private void AoMedirPedaco(string qual, double ms)
	{
		if (ms >= PedacoQueConta) _pedacosDoQuadro.Add((qual, ms));
	}

	/// <summary>No fim do quadro: os pedacos e os arquivos lidos passam a ser DESTE quadro.</summary>
	private void GuardarOsPedacosDoQuadro()
	{
		if (_pedacosDoQuadro.Count > 0)
		{
			double soma = 0;
			foreach ((_, double ms) in _pedacosDoQuadro) soma += ms;
			if (soma >= QuadroComPedacos) _pedacos[_quadros.Count] = [.. _pedacosDoQuadro];
			_pedacosDoQuadro.Clear();
		}
		if (_escutaDeCarga?.Recolher() is { Length: > 0 } lidos) _lidos[_quadros.Count] = lidos;
	}

	private void LigarAEscutaDeCarga()
	{
		if (!SaidaVerbosa || _escutaDeCarga != null) return;
		_escutaDeCarga = new EscutaDeCarga();
		OS.AddLogger(_escutaDeCarga);
	}

	private void SoltarAEscutaDeCarga()
	{
		if (_escutaDeCarga == null) return;
		OS.RemoveLogger(_escutaDeCarga);
		_escutaDeCarga = null;
	}

	/// <summary>Os pedacos da cena e os arquivos lidos num quadro, uma linha de cada -- se ele tem.</summary>
	private void ImprimirOsPedacos(int quadro)
	{
		if (_pedacos.TryGetValue(quadro, out (string Qual, double Ms)[]? pedacos))
		{
			var texto = new System.Text.StringBuilder();
			double soma = 0;
			foreach ((string qual, double ms) in pedacos)
			{
				soma += ms;
				if (ms < 0.2) continue;
				if (texto.Length > 0) texto.Append(" | ");
				texto.Append($"{qual} {ms:0.0}");
			}
			_linhas.Add($"               a cena neste quadro, em pedacos ({soma:0.0} ms): {texto}");
		}
		if (_lidos.TryGetValue(quadro, out string[]? lidos))
			_linhas.Add($"               lidos do disco pela thread principal neste quadro ({lidos.Length}): {string.Join(", ", lidos.Select(l => l.GetFile()))}");
	}

	/// <summary>A cena viva em volta do corpo deste cliente, ou nulo.</summary>
	private static Transformacao? CenaDoCorpoLocal(World mundo, GameClient cli)
	{
		if (mundo.CorpoDeTeste(cli.LocalId) is not { } corpo) return null;
		foreach (Node n in corpo.GetParent().GetChildren())
			if (n is Transformacao c && c.Rodando && !c.IsQueuedForDeletion()) return c;
		return null;
	}

	/// <summary>
	/// O QUE UMA CENA USA DEPOIS DE NASCER E NAO DEPENDE DE QUEM SE TRANSFORMA: a fumaca, as duas crateras, os quatro
	/// estalos da faisca e o som de cada beat de cada cena. Os caminhos saem de quem os usa (`Decalques`,
	/// `RaiosDaForma`, `Transformacao.CaminhoDoSom`) e a lista dos sons e montada AQUI, dos roteiros, e nao pedida a
	/// quem os carrega: a pergunta e "o que os beats podem pedir esta na memoria?", e a resposta nao pode vir de
	/// quem escolheu o que por la.
	/// </summary>
	private static IEnumerable<string> ArquivosQueACenaNaoPodeReler()
	{
		yield return Decalques.CaminhoDaArte(Jandirus.Net.Protocol.Decal.Fumaca);
		yield return Decalques.CaminhoDaArte(Jandirus.Net.Protocol.Decal.CrateraGrande);
		yield return Decalques.CaminhoDaArte(Jandirus.Net.Protocol.Decal.Cratera);

		foreach (string estalo in RaiosDaForma.Estalos) yield return estalo;

		var vistos = new HashSet<string>();
		Cinematica[] cenas = [.. Cinematicas.Todas, Cinematicas.Furia, Cinematicas.Fusao,
							  Cinematicas.BioSemiPerfeito, Cinematicas.BioPerfeito, Cinematicas.BioSsj2];
		foreach (Cinematica cena in cenas)
			foreach (Beat beat in cena.Beats)
				if (beat.Som.Length > 0 && Transformacao.CaminhoDoSom(beat.Som) is { Length: > 0 } som && vistos.Add(som))
					yield return som;
	}

	/// <summary>
	/// `--cenacoleta`: A COLETA ENTRE DUAS CENAS. A cena anterior acabou ha tres segundos (a fumaca dela vive 2,6 s
	/// e a ultima nasce um segundo antes do fim): o coletor do .NET passa, os involucros C# do que ela usou morrem, e
	/// o que so eles seguravam sai do cache do Godot. A nota diz o que ficou e o que saiu.
	/// </summary>
	private void ColetarEOlharOCache()
	{
		ulong t0 = Time.GetTicksUsec();
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;

		var dentro = new List<string>();
		var fora = new List<string>();
		foreach (string caminho in ArquivosQueACenaNaoPodeReler())
			(ResourceLoader.HasCached(caminho) ? dentro : fora).Add(caminho.GetFile());

		Marcar($"coleta forcada do .NET ({ms:0.0} ms)");
		_notasDeCache.Add($"depois da cena {_proximaCena} de {_listaDasCenas.Length}, {_rotuloDaCenaMedida}, com a fumaca sumida e o coletor passado:"
						  + $" NA MEMORIA {dentro.Count} ({string.Join(", ", dentro)}) | FORA {fora.Count} ({string.Join(", ", fora)})");
	}

	/// <summary>
	/// A MAQUINA DA RODADA: um gesto a cada segundo e meio, e nenhum enquanto uma cena roda. Chamada todo quadro, no
	/// lugar do <see cref="AndarNosTemas"/>.
	/// </summary>
	private void AndarNasCenas(World mundo, GameClient cli)
	{
		if (!_ganchoDasCenas)
		{
			// O GANCHO DO FIM DO QUADRO, um degrau antes do da bancada (100_000): o que ele marca entra no quadro
			// que esta fechando. E la que se ve o beat -- a cena roda DEPOIS deste robo no mesmo quadro.
			_ganchoDasCenas = true;
			AddChild(new MarcaDeQuadro { Name = "FimDoQuadroDasCenas", ProcessPriority = 99_999, Agora = NoFimDoQuadroDasCenas });
			_t = 0;
			return;
		}

		if (_cenaMedida != null)
		{
			_idadeDaCenaMedida += GetProcessDeltaTime();
			if (_corteDaCena > 0 && _idadeDaCenaMedida >= _corteDaCena && AudioDirector.Instance is { } corta)
			{
				Marcar($"a bancada CORTA a cena de {_rotuloDaCenaMedida}, aos {_idadeDaCenaMedida:0.0} s");
				CortarACena(corta);
			}
			return;
		}
		if (_t < 1.5) return;
		_t = 0;

		// A CENA ANTERIOR E DESFEITA num gesto so dele: o tema sai do ar (a de estreia deixa a faixa tocando depois
		// de acabar) e o corpo volta a base, pra a seguinte nascer so com o pacote da transformacao.
		if (_calarAntes)
		{
			_calarAntes = false;
			if (AudioDirector.Instance is { } audio) CortarACena(audio);
			_coletarAntes = _cenaColeta;
			return;
		}

		// `--cenacoleta`: e num gesto so dela, um segundo e meio depois, a coleta forcada do .NET.
		if (_coletarAntes)
		{
			_coletarAntes = false;
			ColetarEOlharOCache();
			return;
		}

		if (_cenaPartida && _pecaDaCena < QuantasPecas) { FazerAPeca(mundo, cli); return; }

		if (_proximaCena >= _listaDasCenas.Length) { Fechar(); return; }
		NascerACena(mundo, cli, _listaDasCenas[_proximaCena++]);
	}

	/// <summary>
	/// NASCE A CENA DE UMA FORMA pelo caminho por onde o pacote do servidor entra, e SO o pacote da transformacao,
	/// como o <see cref="AquecerACena"/>: o corpo ja esta na base -- no comeco da rodada porque nasceu nela, e
	/// depois de cada cena porque o <see cref="CortarACena"/> do gesto anterior o devolveu.
	/// </summary>
	private void NascerACena(World mundo, GameClient cli, string pedido)
	{
		bool estreia = pedido.EndsWith(":E", StringComparison.Ordinal);
		string id = estreia ? pedido[..^2] : pedido;
		if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } def)
		{
			Conferir(false, $"a forma `{id}` da lista de `--cenas` existe no catalogo");
			Fechar();
			return;
		}

		DegrauDeCena degrau = estreia ? DegrauDeCena.Estreia : DegrauDeCena.Curta;
		string qual = $"`{id}` ({(estreia ? "a de ESTREIA" : "a encurtada")})";
		ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);

		MarcaVerbosa($">>> a cena de {qual}: o `AoMudarForma` com a cena");
		ulong t1 = Time.GetTicksUsec();
		mundo.AoMudarForma(cli.LocalId, b, def.IdRede, degrau);
		ulong t2 = Time.GetTicksUsec();
		MarcaVerbosa($"<<< a cena de {qual}: a chamada voltou");

		_cenaMedida = null;
		if (mundo.CorpoDeTeste(cli.LocalId) is { } corpo)
			foreach (Node n in corpo.GetParent().GetChildren())
				if (n is Transformacao c && c.Rodando && !c.IsQueuedForDeletion()) _cenaMedida = c;
		if (_cenaMedida == null)
		{
			Conferir(false, $"a cena de {qual} nasceu (`World.AoMudarForma` -> `Transformacao.Rodar`)");
			Fechar();
			return;
		}

		_rotuloDaCenaMedida = qual;
		_beatsDaCenaMedida = 0;
		_raiosDaCenaMedida = 0;
		_idadeDaCenaMedida = 0;
		_calarAntes = true;
		Cinematica cena = _cenaMedida.CenaDeTeste;
		Evento($"{(_proximaCena == 1 ? "a PRIMEIRA CENA da rodada" : "a cena")} de {qual} NASCE: {cena.Beats.Length} beats em {cena.Segundos:0.0} s"
			   + $" (o `AoMudarForma` com a cena: {(t2 - t1) / 1000.0:0.0} ms)", Papel.Nota);
	}

	// =====================================================================
	// AS PECAS DO QUADRO DO NASCIMENTO, UMA POR QUADRO (`--cenapartida`)
	// =====================================================================
	private const int QuantasPecas = 9;

	private static string NomeDaPeca(int n) => n switch
	{
		0 => "CORE: `Cinematicas.NoDegrau(ssj1, Curta)` (a carga da classe das cenas, se ninguem a pediu antes)",
		1 => "ARQUIVO: a folha `AuraSSjBig.tres`, pela porta das folhas",
		2 => "ARQUIVO: a folha `Rising Rocks.tres`, pela porta das folhas",
		3 => "MATERIAL: o primeiro `ShaderMaterial` do `Aura.gdshader`, fora da arvore (nada e desenhado)",
		4 => "A CHAMA NA TELA: uma `SpriteDeAura` com a folha do Super Saiyajin, montada e desenhada com forca ZERO, como a cena a poe",
		5 => "a mesma chama com forca UM (so muda o valor de um uniform)",
		6 => "A PEDRA NA TELA: um `AnimatedSprite2D` da `Rising Rocks`, no z das pedras da cena",
		7 => "A VOLTA A BASE: `World.AoMudarForma(base, base, Nenhuma)`, o pacote que a bancada manda ao cortar uma cena",
		_ => "o palco da medicao sai da tela",
	};

	private Node2D PalcoDaMedicao(World mundo)
	{
		if (_palcoDaMedicao == null)
		{
			_palcoDaMedicao = new Node2D { Name = "PalcoDaMedicaoDeCena", ZIndex = 5 };
			mundo.AddChild(_palcoDaMedicao);
		}
		return _palcoDaMedicao;
	}

	private void Prender(Resource? r)
	{
		if (r != null) _presosDaMedicao.Add(r);
	}

	/// <summary>
	/// CADA PECA NASCE SOZINHA, pela porta de producao quando ha uma, e o custo de primeiro uso dela cai num quadro
	/// so dela: o cronometro da chamada da o script, o quadro da o desenho. A ordem e a do `Transformacao._Ready`.
	/// </summary>
	private void FazerAPeca(World mundo, GameClient cli)
	{
		int n = _pecaDaCena++;
		string nome = NomeDaPeca(n);
		Vector2 onde = (mundo.PosicaoLocal ?? Vector2.Zero) + new Vector2(96, -64);
		var dourado = new Color(1f, 1f, 0.5f);

		MarcaVerbosa(">>> PECA: " + nome);
		ulong t0 = Time.GetTicksUsec();
		switch (n)
		{
			case 0:
				if (Jandirus.Core.Forms.Catalogo.Def("ssj1") is { } ssj1) _ = Cinematicas.NoDegrau(ssj1, DegrauDeCena.Curta);
				break;
			case 1: Prender(FolhasPresas.Carregar(SpriteDeAura.FolhaSsj)); break;
			case 2: Prender(FolhasPresas.Carregar(Transformacao.CaminhoDasPedras)); break;
			case 3:
			{
				var material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("res://Assets/Shaders/Aura.gdshader") };
				material.SetShaderParameter("cor", new Vector3(dourado.R, dourado.G, dourado.B));
				material.SetShaderParameter("forca", 0f);
				material.SetShaderParameter("tingir", false);
				material.SetShaderParameter("forma_no_alfa", true);
				Prender(material);
				break;
			}
			case 4:
			{
				var chama = new SpriteDeAura { Name = "ChamaDaMedicao", Position = onde };
				PalcoDaMedicao(mundo).AddChild(chama);
				chama.DefinirFolha(FolhaDeAura.Ssj);
				chama.Definir(true, dourado, 0f);
				break;
			}
			case 5:
				PalcoDaMedicao(mundo).GetNodeOrNull<SpriteDeAura>("ChamaDaMedicao")?.Definir(true, dourado, 1f);
				break;
			case 6:
				if (FolhasPresas.Carregar(Transformacao.CaminhoDasPedras) is { } folha && folha.GetAnimationNames() is { Length: > 0 } nomes)
				{
					var pedra = new AnimatedSprite2D
					{
						Name = "PedraDaMedicao",
						SpriteFrames = folha,
						Animation = nomes[0],
						Position = onde + new Vector2(64, 48),
						ZIndex = -1,
						ZAsRelative = false,
						TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
					};
					PalcoDaMedicao(mundo).AddChild(pedra);
					pedra.Play();
				}
				break;
			case 7:
			{
				ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);
				mundo.AoMudarForma(cli.LocalId, b, b, DegrauDeCena.Nenhuma);
				break;
			}
			default:
				_palcoDaMedicao?.QueueFree();
				_palcoDaMedicao = null;
				break;
		}
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;
		MarcaVerbosa("<<< PECA: " + nome);
		Evento($"PECA {n + 1} de {QuantasPecas} -- {nome} (a chamada: {ms:0.0} ms)", Papel.Nota);
	}

	// =====================================================================
	// O FIM DE CADA QUADRO: o desenho partido, e os beats da cena em curso
	// =====================================================================
	private void NoFimDoQuadroDasCenas()
	{
		if (_acabou) return;
		if (_primeiroQuadroPartido < 0) _primeiroQuadroPartido = _quadros.Count;
		_desenhoPartido.Add((RenderingServer.GetFrameSetupTimeCpu(), RenderingServer.ViewportGetMeasuredRenderTimeCpu(_tela)));
		if (SaidaVerbosa && _eventos.Count > 0) GD.Print($"[cena-marca] --- fim do script do quadro {_quadros.Count}");

		if (_cenaMedida is not { } cena) return;
		if (!IsInstanceValid(cena) || cena.IsQueuedForDeletion() || !cena.Rodando)
		{
			Marcar($"a cena de {_rotuloDaCenaMedida} ACABOU");
			_cenaMedida = null;
			_t = 0;
			return;
		}

		// OS RAIOS DA TEMPESTADE (a estreia do `ssj1`) nao sao beats: sem este rotulo o quadro de cada um sai "sem rotulo".
		int raios = cena.RaiosDaEstreiaDeTeste;
		if (raios != _raiosDaCenaMedida)
		{
			Marcar($"a tempestade: o raio {raios} cai");
			_raiosDaCenaMedida = raios;
		}

		int n = cena.BeatsDeTeste;
		if (n == _beatsDaCenaMedida) return;

		Beat[] beats = cena.CenaDeTeste.Beats;
		var rotulo = new System.Text.StringBuilder();
		for (int i = _beatsDaCenaMedida; i < n && i < beats.Length; i++)
		{
			if (rotulo.Length > 0) rotulo.Append(" + ");
			rotulo.Append($"beat {i} aos {beats[i].Em:0.00} s [{beats[i].Faz}{(beats[i].Som.Length > 0 ? ", som `" + beats[i].Som + "`" : "")}]");
		}
		_beatsDaCenaMedida = n;
		Evento($"a cena de {_rotuloDaCenaMedida}: {rotulo}", Papel.Nota);
	}

	private (double Preparo, double Tela) Partido(int quadro)
	{
		int k = quadro - _primeiroQuadroPartido;
		return _primeiroQuadroPartido >= 0 && k >= 0 && k < _desenhoPartido.Count ? _desenhoPartido[k] : (0, 0);
	}

	/// <summary>O preparo e a tela do quadro `i`: como a CPU do desenho, estao na leitura do quadro seguinte.</summary>
	private double PreparoMs(int i) => Partido(i + 1).Preparo;
	private double TelaMs(int i) => Partido(i + 1).Tela;

	/// <summary>
	/// O FECHO DA RODADA: de cada coisa medida, o quadro dela e o seguinte, com o desenho partido; e todo quadro
	/// acima de um piso baixo desde o primeiro gesto, com rotulo ou sem.
	/// </summary>
	private void RelatarAsCenas()
	{
		Nota("AS CENAS, QUADRO A QUADRO -- de cada coisa medida, o quadro dela e o seguinte: o SCRIPT, e o desenho partido em"
			 + " PREPARO (os recursos sujos do quadro) e TELA (o desenho da janela):");
		foreach ((string oque, int q, _, _, _) in _eventos)
		{
			_linhas.Add($"           {oque}   [quadro {q}]");
			for (int i = q; i <= q + 1 && i + 1 < _quadros.Count; i++)
				_linhas.Add($"               q{i - q,+2}  script {ScriptMs(i) - ColetorNoScript(i),6:0.0} | desenho {DesenhoMs(i),5:0.0} = preparo {PreparoMs(i),5:0.0} + tela {TelaMs(i),5:0.0}"
							+ $" | inteiro {TotalMs(i),6:0.0} | pipelines 2D +{_quadros[i + 1].Canvas - _quadros[i].Canvas}"
							+ (ColetorNoQuadro(i) > 0.05 ? $" | coletor {ColetorNoQuadro(i):0.0}" : ""));
		}

		const double Caro = 6.0;
		if (_eventos.Count == 0) return;
		int de = Math.Max(1, _eventos[0].Quadro);
		Nota($"todo quadro com trabalho acima de {Caro:0} ms desde o primeiro gesto da rodada (um piso baixo, pra nada passar sem rotulo):");
		for (int i = de; i + 1 < _quadros.Count; i++)
		{
			if (Trabalho(i) <= Caro) continue;
			_linhas.Add($"           t={(_quadros[i].Comeco - _quadros[de].Comeco) / 1e6,7:0.000}s  trabalho {Trabalho(i),6:0.0} ms"
						+ $" (script {ScriptMs(i) - ColetorNoScript(i):0.0} + preparo {PreparoMs(i):0.0} + tela {TelaMs(i):0.0})"
						+ $"  pipelines 2D +{_quadros[i + 1].Canvas - _quadros[i].Canvas}  [quadro {i}]"
						+ (_quadros[i].Marcas.Length > 0 ? "   <-- " + _quadros[i].Marcas : "   <-- (sem rotulo)"));
			ImprimirOsPedacos(i);
		}

		if (_notasDeCache.Count == 0) return;
		Nota("O CACHE DO GODOT DEPOIS DE CADA COLETA (`--cenacoleta`) -- o que ninguem segura sai dele, e a cena seguinte o le do disco de novo:");
		foreach (string nota in _notasDeCache) _linhas.Add("           " + nota);
	}
}

/// <summary>
/// O OUVIDO DAS CARGAS (`--verbose`): com a saida verbosa, o Godot escreve `Completed load for: '...' at thread N`
/// a cada recurso que acaba de carregar, e N e -1 quando quem carregou foi a thread principal -- uma leitura de
/// disco no meio de um quadro. Este `Logger` guarda o caminho de cada uma, e a bancada o recolhe no fim de cada
/// quadro: e o que da NOME ao arquivo que um quadro caro leu, sem ninguem ter que casar o log com as marcas na mao.
///
/// O MOTOR CHAMA ISTO DE QUALQUER THREAD (as de carga tambem escrevem), entao a fila e a concorrente e nada aqui
/// toca em node.
/// </summary>
public partial class EscutaDeCarga : Logger
{
	private const string Cabeca = "Completed load for: '";

	private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lidos = new();

	public override void _LogMessage(string message, bool error)
	{
		if (error || !message.StartsWith(Cabeca, StringComparison.Ordinal)
			|| !message.Contains(" at thread -1", StringComparison.Ordinal)) return;
		int fim = message.IndexOf('\'', Cabeca.Length);
		if (fim > Cabeca.Length) _lidos.Enqueue(message[Cabeca.Length..fim]);
	}

	/// <summary>O que a thread principal leu desde a ultima chamada, na ordem. Vazio quase sempre.</summary>
	public string[] Recolher()
	{
		if (_lidos.IsEmpty) return [];
		var lidos = new List<string>();
		while (_lidos.TryDequeue(out string? caminho)) lidos.Add(caminho);
		return [.. lidos];
	}
}
