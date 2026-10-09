using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// OS RAIOS DA FORMA NA RODADA DOS TEMAS (`--diagestouro --temas`): a regua dos quadros em que um efeito ENSAIADO NO
/// LOBBY e desenhado pela primeira vez em jogo, e os instrumentos que fazem os defeitos dela aparecer (`--driverfrio`,
/// `--raioscomluz`, e a propria rodada feita de NOITE). A historia e os numeros estao no cabecalho do
/// `RoboDoPrimeiroEstouro`, no bloco "E OS RAIOS DA FORMA"; aqui fica o que e so desta regua.
///
/// A MESMA REGUA SERVE AOS QUADROS QUE VIERAM DEPOIS -- o primeiro relampago, a primeira chuva e a primeira luz em cima
/// de um corpo --, que tem arquivo proprio (`RoboDoPrimeiroEstouro.Nascidas.cs`): o que e deles esta la, e o que e de
/// todos (a lista dos quadros, a conferencia, o sal, o inventario) ficou aqui.
///
/// ============================ OS TRES QUADROS ============================
///   * os FEIXES DO CHAO -- o primeiro beat `Efeito.FeixesNoChao` da primeira cinematica do processo (na encurtada
///     do `ssj1`, aos 6 s): oito `Sprite2D` com o `RaioDaForma.gdshader` (`Transformacao.MontarOsFeixes`);
///   * os PRIMEIROS RAIOS DE CORPO -- o primeiro beat `Efeito.Raios` de uma estreia (na do `ssj2`, o beat zero): a
///     faisca do `RaiosDaForma`, que e particula e usa o mesmo shader;
///   * o PRIMEIRO ANEL DE CHOQUE -- o primeiro beat `Efeito.AnelDeChoque` da primeira cinematica (aos 3 s): o
///     `CombatFx.Onda`. Nao e raio de forma; entra porque divide com os dois o defeito do lado escuro (abaixo), e e
///     o unico ato antigo do ensaio que esta rodada desenha depois dele.
///
/// OS DOIS PRIMEIROS SAO DUAS PIPELINES DO MESMO SHADER porque sao dois JEITOS DE DESENHAR: o motor monta uma pipeline
/// por variante (retangulo de sprite x malha de particula) e outra por haver ou nao luz em cima do item. Um mundo de
/// dia paga duas; de noite, debaixo de uma luz, as outras duas.
///
/// ============================ A REGUA TEM DUAS PERNAS ============================
/// NENHUMA PIPELINE 2D NASCE no quadro (nem no seguinte), E o desenho dele custa um desenho normal.
///
/// A primeira e a que reprova SEMPRE, e e contagem do proprio motor (`RenderingInfo.PipelineCompilationsCanvas`):
/// sem o ensaio do lobby nasce uma em cada quadro. A segunda e a que o jogador sente, e so morde quando o DRIVER
/// de video nao conhece o shader -- ver <see cref="SalgarOsShaders"/>: com o cache do driver cheio a pipeline
/// que falta custa 1 a 2 ms, e nenhum teto de tempo a separa de um quadro qualquer.
///
/// ============================ OS DOIS DEFEITOS QUE ELA GUARDA ============================
///   * O ENSAIO SEM OS RAIOS (`--semensaiodosraios`): o jogo de antes dos dois atos do `Aquecimento.AtosDosRaios`.
///     Reprova os feixes e os raios de corpo; o anel fica como esta.
///   * A ESTRELA COM LUZ NUM MUNDO ESCURO (`--estrelacomluz`, na rodada de NOITE: `--horateste 0.9 --estouronoite`):
///     a estrela do embate do ensaio pendurava a luz de ki dela, e a luz de ki so nasce quando o MUNDO esta escuro.
///     O ensaio que cai nos primeiros quadros de um mundo escuro (o login mais rapido que ele, numa noite ou numa
///     tempestade) ficava com o lado ESCURO do palco iluminado por ela, e os atos seguintes nao montavam ali a
///     pipeline do item sem luz -- a que o jogo usa de dia, e de noite longe de qualquer luz. Reprova os tres
///     quadros. Foi achado em 2026-10-09 por esta regua reprovar sozinha durante 24 minutos: o clima natural do
///     mundo e funcao do relogio (`Clima.TipoDoBloco`), e o bloco do meio-dia tinha virado tempestade.
/// (Os quadros do `RoboDoPrimeiroEstouro.Nascidas.cs` tem cada um o seu, e nenhum deles e destes dois: ver la.)
/// ========================================================================================
/// </summary>
public partial class RoboDoPrimeiroEstouro
{
	/// <summary>A pasta dos shaders do jogo: e nela que o `--driverfrio` acha, pelo nome, o que vai salgar.</summary>
	private const string PastaDosShaders = "res://Assets/Shaders";

	/// <summary>O shader que o `--driverfrio` salga quando a linha nao diz qual: o dos raios da forma, que foi por quem ele nasceu.</summary>
	private const string ShaderFrioPadrao = "RaioDaForma";

	/// <summary>
	/// `--temas --semensaiodosraios`: A RODADA DE INJECAO dos raios da forma -- o processo entra no mundo com o defeito
	/// `Aquecimento.SemEnsaioDosRaiosDeTeste` (ligado pelo `Boot` na linha em que o aquecimento nasce): o jogo de
	/// antes, em que ninguem desenhava um raio de forma antes da hora. A mesma regua tem que REPROVAR os dois quadros
	/// deles.
	/// </summary>
	private readonly bool _semEnsaioDosRaios = Tem("--semensaiodosraios");

	private const string DefeitoDosRaios = "o aquecimento sem o ensaio dos raios da forma";

	/// <summary>
	/// `--temas --horateste 0.9 --estouronoite --estrelacomluz`: A RODADA DE INJECAO do lado escuro do palco -- o
	/// defeito `Aquecimento.EstrelaComLuzDeTeste` (ligado pelo `Boot`): a estrela do embate do ensaio com a luz propria
	/// dela, o jogo de antes. So morde com o MUNDO escuro na hora do ensaio -- por isso a rodada e a de noite, com o
	/// login de robo --; a mesma regua tem que REPROVAR os tres quadros.
	/// </summary>
	private readonly bool _estrelaComLuz = Tem("--estrelacomluz");

	private const string DefeitoDaEstrela = "a estrela do embate do ensaio com a luz propria dela, num mundo escuro";

	/// <summary>
	/// `--driverfrio [lista]`: O DRIVER DE VIDEO NUNCA VIU OS SHADERS DA LISTA NESTA CORRIDA. Ver <see cref="SalgarOsShaders"/>.
	/// A lista sao nomes de arquivo da pasta dos shaders, sem a extensao, separados por virgula ou por `+` (`--driverfrio
	/// Raio,Personagem`, `--driverfrio Raio+Queda`: num `.bat` a virgula separa argumento, e o `+` nao); sem lista, e o
	/// dos raios da forma.
	/// </summary>
	private readonly bool _driverFrio = Tem("--driverfrio");

	/// <summary>Os caminhos dos shaders que o `--driverfrio` desta corrida salga. Vazio sem ele.</summary>
	private readonly string[] _shadersFrios = ShadersFrios();

	/// <summary>
	/// A lista do `--driverfrio`, em caminhos. O nome casa com o arquivo SEM diferenciar maiuscula, e sai escrito como o
	/// arquivo esta: o cache do Godot e por caminho exato, e `raio` nunca acharia o `Raio.gdshader` la. Nome que nao e
	/// de arquivo nenhum fica como veio -- nunca entra na memoria, e o relatorio reprova por ele.
	/// </summary>
	private static string[] ShadersFrios()
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, "--driverfrio");
		if (i < 0) return [];

		string lista = i + 1 < a.Length && !a[i + 1].StartsWith("--", StringComparison.Ordinal) ? a[i + 1] : ShaderFrioPadrao;
		var arquivos = new List<string>();
		foreach (string bruto in DirAccess.GetFilesAt(PastaDosShaders))
		{
			string nome = bruto.EndsWith(".remap", StringComparison.Ordinal) ? bruto[..^6] : bruto;
			if (nome.EndsWith(".gdshader", StringComparison.Ordinal)) arquivos.Add(nome[..^9]);
		}

		var caminhos = new List<string>();
		foreach (string pedido in lista.Split([',', '+'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string nome = arquivos.Find(n => n.Equals(pedido, StringComparison.OrdinalIgnoreCase)) ?? pedido;
			string caminho = $"{PastaDosShaders}/{nome}.gdshader";
			if (!caminhos.Contains(caminho)) caminhos.Add(caminho);
		}
		return [.. caminhos];
	}

	/// <summary>
	/// `--raioscomluz`: UMA LUZ EM CIMA DO CORPO. O motor monta OUTRA pipeline pro item que tem luz em cima, e de dia, em
	/// campo aberto, a rodada so passa pelas do escuro: com a luz, os quadros da regua (e tudo o mais que a cena
	/// desenha em volta do corpo) passam pelas gemeas iluminadas -- as que o jogo usa de noite ao lado de uma fogueira,
	/// ou debaixo de um teto. E como se confere o lado iluminado do palco do ensaio.
	///
	/// ELA ACENDE MEIO SEGUNDO DEPOIS DE A BASE COMECAR, e nao na entrada no mundo, e o quadro em que acende e um quadro
	/// da regua: o do PRIMEIRO CORPO ILUMINADO do processo (ver o `RoboDoPrimeiroEstouro.Nascidas.cs`). Ate 2026-10-09
	/// nascia ali a pipeline iluminada do boneco, que ensaio nenhum desenhava, e esta rodada cobrava o CONTRARIO -- que
	/// nascesse pelo menos uma, como prova de que a luz tinha pegado. Com o corpo no ensaio a prova mudou de lugar: e a
	/// rodada de injecao dele (`--raioscomluz --semensaiodocorpo`), em que a mesma luz, no mesmo lugar, volta a fazer
	/// nascer a do boneco.
	/// </summary>
	private readonly bool _raiosComLuz = Tem("--raioscomluz");
	private int _quadroDaLuz = -1;

	/// <summary>O sal de cada shader desta corrida e o quadro em que entrou; e, de quem nao tem a forma esperada, o porque.</summary>
	private readonly Dictionary<string, (int Sal, int Quadro)> _salgados = [];
	private readonly Dictionary<string, string> _falhasDoSal = [];

	/// <summary>
	/// QUEM NO ENSAIO DO LOBBY DESENHA O QUE UM QUADRO DA REGUA USA -- e, por tabela, que defeito injetado o reprova
	/// (<see cref="DefeitoQueReprova"/>). `Antigo` e um ato dos efeitos de ki (so o anel de choque esta nesta regua); os
	/// seis ultimos sao os da rodada dos avulsos (o arquivo `RoboDoPrimeiroEstouro.Avulsos.cs`).
	/// </summary>
	private enum AtoDoEnsaio { Antigo, Raios, Relampago, Corpo, Chuva, Gota, EstouroDePlaneta, Nebulosa, Nevoa, Embate, Planeta }

	/// <summary>
	/// Quantos quadros A MAIS a regua olha depois dos dois de sempre (o do gesto e o seguinte). A chuva pede: o quadro
	/// dela e aquele em que o emissor LIGA, e particula que acaba de ligar e processada no desenho desse quadro e
	/// desenhada a partir do seguinte -- a pipeline dela nasce um ou dois quadros depois do gesto. E o planeta visto do
	/// espaco tambem: o disco nasce no quadro em que a zona monta, e a camera pode chegar nele um ou dois quadros depois.
	/// </summary>
	private static int QuadrosAMais(AtoDoEnsaio ato) => ato is AtoDoEnsaio.Chuva or AtoDoEnsaio.Planeta ? 2 : 0;

	/// <summary>Os quadros da regua, na ordem em que a rodada chegou neles, cada um com o ato do ensaio que o cobre.</summary>
	private readonly List<(int Quadro, string Oque, AtoDoEnsaio Ato)> _primeirosDesenhos = [];

	private bool _feixesVistos, _raiosDeCorpoVistos, _anelVisto;

	/// <summary>A estreia em curso, enquanto nenhuma soltou raio de corpo ainda, e quantos beats dela ja foram vistos.</summary>
	private Transformacao? _cenaDosRaios;
	private string _rotuloDaCenaDosRaios = "";
	private int _beatsDaCenaDosRaios;

	/// <summary>
	/// ============================ O SAL: O DRIVER DE VIDEO TEM UM CACHE QUE A BANCADA NAO ESVAZIA ============================
	/// Toda bancada roda com a pasta de usuario desviada, e por isso com o cache de shader do GODOT vazio
	/// (`user://shader_cache`): mede o jogo de quem acabou de instalar. Mas ha um segundo cache, do DRIVER (o da
	/// NVIDIA mora em `%LOCALAPPDATA%\NVIDIA\DXCache`, fora da pasta desviada), e e ele que decide quanto custa MONTAR
	/// uma pipeline: com o shader ja visto pelo driver, 1 a 2 ms; nunca visto, 16 a 36 (medido em 2026-10-09, e e o
	/// engasgo das tres corridas em quinze que este arquivo veio explicar).
	///
	/// O cache do driver so esta vazio pra um shader em tres casos: a primeira vez que o jogo roda na maquina, depois de
	/// uma troca de driver, e quando alguem o apaga. E ha um quarto, que e de bancada: o driver da um arquivo de cache
	/// a cada processo SIMULTANEO do mesmo executavel, e o terceiro Godot com janela aberto ao mesmo tempo estreia um
	/// arquivo vazio. Nenhum dos quatro se repete de proposito sem mexer na maquina.
	///
	/// ENTAO A BANCADA MUDA O SHADER, E NAO A MAQUINA: soma a cada shader da lista do `--driverfrio`, ja carregado, nos
	/// dois estagios, uma constante sorteada vezes `step(2.0, UV.x)` -- que vale ZERO em todo pixel (o UV de um quad vai
	/// de 0 a 1) e que o compilador nao pode cortar, porque nao sabe disso. O desenho e identico; o codigo que chega ao
	/// driver, nao: e um shader que ele nunca viu, e toda pipeline dele e montada do zero como na maquina de quem acabou
	/// de instalar -- a do item sem luz e a gemea iluminada, a do sprite e a da particula.
	///
	/// SEM ANCORA DE TEXTO POR SHADER. A primeira versao procurava duas linhas do `RaioDaForma.gdshader`, e so servia pra
	/// ele; esta acha a chave que FECHA o `void fragment()` e o `void vertex()` de qualquer shader de item 2D e poe a
	/// constante antes dela (<see cref="Salgar"/>). Quem nao tem `vertex()` ganha um, so com a linha do sal.
	///
	/// O QUE O SAL NAO ALCANCA: o shader PADRAO do motor -- o de todo item sem material (o texto, o chao, o retangulo e
	/// o poligono de um `_Draw`). O codigo dele nao e do projeto, e nao ha `Shader.Code` pra mudar. Pra ele o driver frio
	/// so existe com um arquivo de cache novo do driver: ver o cabecalho do `RoboDoPrimeiroEstouro.Nascidas.cs`.
	///
	/// ENTRA ASSIM QUE O SHADER ESTA NA MEMORIA -- pros da fila do `Aquecimento` (que os pede no primeiro quadro), no
	/// lobby: antes de o ensaio comecar, antes de haver corpo. Um sal que entrasse depois do ensaio jogaria fora as
	/// pipelines que ele montou (`Shader.Code` novo limpa as pipelines do shader), e a regua reprovaria o conserto --
	/// alto, e nao calado. O shader que so e lido na hora do primeiro uso e salgado no fim do quadro em que chega, antes
	/// do desenho dele.
	///
	/// NAO SAI: o sal vale zero e morre com o processo, que a bancada sempre fecha.
	/// ====================================================================================================================
	/// </summary>
	private void SalgarOsShaders()
	{
		foreach (string caminho in _shadersFrios)
		{
			if (_salgados.ContainsKey(caminho) || _falhasDoSal.ContainsKey(caminho)) continue;
			// (o `GetCachedRef` so OLHA o cache: pedir um arquivo que ainda esta na thread de carga e a corrida que o
			// cabecalho do `Aquecimento` conta)
			if (ResourceLoader.GetCachedRef(caminho) is not Shader shader) continue;

			// UM INTEIRO SORTEADO SOBRE 1024: toda fracao dessas cabe exata num `float`, entao dois sais diferentes sao
			// sempre duas constantes diferentes no codigo compilado.
			int sal = System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, 1 << 23);
			string k = (sal / 1024.0).ToString("0.0#########", System.Globalization.CultureInfo.InvariantCulture);
			if (Salgar(shader.Code, k, out string falha) is not { } salgado) { _falhasDoSal[caminho] = falha; continue; }

			shader.Code = salgado;
			_salgados[caminho] = (sal, _quadros.Count);
			Marcar($"`--driverfrio`: o sal {sal} entrou no `{caminho.GetFile()}`");
		}
	}

	/// <summary>
	/// O CODIGO DE UM SHADER DE ITEM 2D COM O SAL `k` NOS DOIS ESTAGIOS, ou nulo -- e o porque -- quando o shader nao tem a
	/// forma esperada: nenhum `void fragment()`, ou mais de um, ou chave que nao fecha. No dia em que um shader mudar de
	/// feitio isto reprova, em vez de salgar a metade.
	///
	/// A LINHA DO SAL ENTRA ANTES DA CHAVE QUE FECHA A FUNCAO: e a ultima coisa que ela faz no caminho que chega ao fim,
	/// e nada do que o shader escreveu antes a desfaz.
	/// </summary>
	private static string? Salgar(string codigo, string k, out string falha)
	{
		falha = "";
		string limpo = SemComentarios(codigo);
		int fragmento = FimDoCorpo(limpo, "fragment"), vertice = FimDoCorpo(limpo, "vertex");
		if (fragmento < 0)
		{
			falha = fragmento == FuncaoQueNaoHa ? "o shader nao tem `void fragment()`" : "o `void fragment()` aparece mais de uma vez, ou a chave dele nao fecha";
			return null;
		}
		if (vertice == FuncaoTorta)
		{
			falha = "o `void vertex()` aparece mais de uma vez, ou a chave dele nao fecha";
			return null;
		}

		string noFragmento = $"\n\tCOLOR.rgb += vec3({k}) * step(2.0, UV.x);\n";
		string noVertice = $"\n\tVERTEX.x += {k} * step(2.0, UV.x);\n";
		if (vertice == FuncaoQueNaoHa) return codigo.Insert(fragmento, noFragmento) + $"\nvoid vertex() {{{noVertice}}}\n";
		// (de tras pra frente: o corte de cima nao pode deslocar o de baixo)
		return fragmento > vertice
			? codigo.Insert(fragmento, noFragmento).Insert(vertice, noVertice)
			: codigo.Insert(vertice, noVertice).Insert(fragmento, noFragmento);
	}

	private const int FuncaoQueNaoHa = -1, FuncaoTorta = -2;

	/// <summary>O mesmo texto com os comentarios trocados por espaco, caractere a caractere: os indices continuam valendo no original.</summary>
	private static string SemComentarios(string codigo)
	{
		var limpo = new System.Text.StringBuilder(codigo);
		int i = 0;
		while (i + 1 < codigo.Length)
		{
			if (codigo[i] == '/' && codigo[i + 1] == '/')
			{
				while (i < codigo.Length && codigo[i] != '\n') limpo[i++] = ' ';
			}
			else if (codigo[i] == '/' && codigo[i + 1] == '*')
			{
				int fim = codigo.IndexOf("*/", i + 2, StringComparison.Ordinal);
				fim = fim < 0 ? codigo.Length : fim + 2;
				for (; i < fim; i++) if (codigo[i] != '\n') limpo[i] = ' ';
			}
			else i++;
		}
		return limpo.ToString();
	}

	/// <summary>
	/// Onde esta a chave que FECHA o corpo de `void nome()`, num texto ja sem comentarios. <see cref="FuncaoQueNaoHa"/> se
	/// a funcao nao existe; <see cref="FuncaoTorta"/> se aparece mais de uma vez ou a chave nao fecha.
	/// </summary>
	private static int FimDoCorpo(string limpo, string nome)
	{
		System.Text.RegularExpressions.MatchCollection achados =
			System.Text.RegularExpressions.Regex.Matches(limpo, @"\bvoid\s+" + nome + @"\s*\(\s*\)\s*\{");
		if (achados.Count == 0) return FuncaoQueNaoHa;
		if (achados.Count > 1) return FuncaoTorta;

		int nivel = 0;
		for (int i = achados[0].Index + achados[0].Length - 1; i < limpo.Length; i++)
		{
			if (limpo[i] == '{') nivel++;
			else if (limpo[i] == '}' && --nivel == 0) return i;
		}
		return FuncaoTorta;
	}

	/// <summary>O que o `--driverfrio` fez nesta corrida, no relatorio, shader a shader. Vale pra toda rodada.</summary>
	private void RelatarOSal()
	{
		if (!_driverFrio) return;
		foreach (string caminho in _shadersFrios)
		{
			string arquivo = caminho.GetFile();
			if (!_salgados.TryGetValue(caminho, out (int Sal, int Quadro) s))
			{
				Conferir(false, $"`--driverfrio`: o sal entrou no `{arquivo}` ("
								+ (_falhasDoSal.TryGetValue(caminho, out string? falha) ? falha : "o shader nunca chegou a memoria nesta corrida") + ")");
				continue;
			}
			Nota($"`--driverfrio`: o DRIVER DE VIDEO NUNCA VIU o `{arquivo}` desta corrida -- ele ganhou o sal {s.Sal}"
				 + $" no quadro {s.Quadro}, {(_entrouNoMundo < 0 || s.Quadro <= _entrouNoMundo ? "antes de o corpo entrar no mundo" : $"{s.Quadro - _entrouNoMundo} quadro(s) DEPOIS de o corpo entrar no mundo")}"
				 + " (o sal vale zero em todo pixel: muda o codigo que o driver recebe, e nao o desenho)");
		}
	}

	/// <summary>
	/// No fim de cada quadro, depois de a cena rodar: o sal, enquanto algum shader da lista nao o recebeu; a luz do
	/// `--raioscomluz`; os beats da estreia que pode soltar os primeiros raios de corpo; e os primeiros desenhos do
	/// `RoboDoPrimeiroEstouro.Nascidas.cs`.
	/// </summary>
	private void NoFimDoQuadroDosRaios()
	{
		if (_salgados.Count + _falhasDoSal.Count < _shadersFrios.Length) SalgarOsShaders();
		if (_raiosComLuz && _quadroDaLuz < 0) PorALuzNoCorpo();
		if (_cenaDosRaios is { } cena) AcompanharOsRaiosDeCorpo(cena);
		OlharOsPrimeirosDesenhos();
	}

	/// <summary>
	/// A luz do `--raioscomluz`: a mesma radial do palco do ensaio, filha do corpo local, com alcance pra cobrir os
	/// feixes ate o fim da viagem deles (88 px do corpo). Meio segundo de base antes dela (60 quadros a 120 Hz, 30 a 60).
	/// O quadro em que ela acende e o do primeiro corpo iluminado do processo, e entra na regua.
	/// </summary>
	private void PorALuzNoCorpo()
	{
		if (_baseDe < 0 || _quadros.Count < _baseDe + 60) return;
		if (World.Instancia is not { } mundo || C is not { } cli || mundo.CorpoDeTeste(cli.LocalId) is not { } corpo) return;
		_quadroDaLuz = _quadros.Count;
		corpo.AddChild(new PointLight2D
		{
			Name = "LuzDaBancadaDosRaios",
			Texture = Fogo.Radial(LuzDeKi.RaioDaTextura),
			TextureScale = 1.6f,
			Energy = 0.5f,
		});
		Marcar("`--raioscomluz`: uma luz em cima do corpo");
		AnotarOPrimeiroCorpoIluminado("a luz do `--raioscomluz`, acesa em cima do corpo");
	}

	/// <summary>
	/// UM BEAT DA PRIMEIRA CINEMATICA DO PROCESSO DISPAROU NESTE QUADRO (quem avisa e o <see cref="AcompanharAPrimeiraCena"/>,
	/// que ja le os beats dela): o primeiro de anel de choque e o primeiro de feixes sao quadros da regua.
	/// </summary>
	private void AnotarOBeatDaRegua(Efeito faz, string rotulo)
	{
		if (faz.HasFlag(Efeito.AnelDeChoque) && !_anelVisto)
		{
			_anelVisto = true;
			_primeirosDesenhos.Add((_quadros.Count, $"o PRIMEIRO ANEL DE CHOQUE do processo ({rotulo}: o `CombatFx.Onda`, um ato antigo do ensaio)", AtoDoEnsaio.Antigo));
		}
		if (faz.HasFlag(Efeito.FeixesNoChao) && !_feixesVistos)
		{
			_feixesVistos = true;
			_primeirosDesenhos.Add((_quadros.Count, $"os FEIXES DO CHAO da primeira cinematica do processo ({rotulo}: oito sprites com o `RaioDaForma.gdshader`)", AtoDoEnsaio.Raios));
		}
	}

	/// <summary>
	/// UMA ESTREIA ACABA DE NASCER: enquanto nenhuma cena soltou raio de corpo neste processo, esta passa a ser
	/// seguida beat a beat. (A do `ssj1` nao solta nenhum no tempo que a rodada lhe da; a do `ssj2` solta no beat
	/// zero.) E a que tem tempestade segura a rodada ate o primeiro relampago: ver <see cref="SeguirATempestadeDaEstreia"/>.
	/// </summary>
	private void SeguirOsRaiosDaEstreia(World mundo, GameClient cli, string id)
	{
		Transformacao? cena = CenaDoCorpoLocal(mundo, cli);
		SeguirATempestadeDaEstreia(cena);
		if (_raiosDeCorpoVistos) return;
		_cenaDosRaios = cena;
		_rotuloDaCenaDosRaios = $"a estreia de `{id}`";
		_beatsDaCenaDosRaios = 0;
	}

	/// <summary>
	/// NAS RODADAS DE INJECAO DESTA REGUA, o quadro em que o tema de uma estreia entra no ar pode ser o dos primeiros
	/// raios de corpo (o beat zero da cena solta a faisca), e la esse quadro monta uma pipeline: a regua da musica
	/// sairia reprovada por uma conta que nao e dela. So vale pra estreia que solta raio logo de saida, e so enquanto a
	/// faisca dela nao saiu -- o tema das outras estreias continua regua.
	/// </summary>
	private bool OTemaCaiNoQuadroDosRaios() =>
		(_semEnsaioDosRaios || _estrelaComLuz) && _cenaDosRaios is { } cena && IsInstanceValid(cena)
		&& Array.Exists(cena.CenaDeTeste.Beats, b => b.Faz.HasFlag(Efeito.Raios) && b.Em < 0.5);

	private void AcompanharOsRaiosDeCorpo(Transformacao cena)
	{
		if (!IsInstanceValid(cena) || cena.IsQueuedForDeletion() || !cena.Rodando) { _cenaDosRaios = null; return; }

		int n = cena.BeatsDeTeste;
		if (n == _beatsDaCenaDosRaios) return;

		Beat[] beats = cena.CenaDeTeste.Beats;
		for (int i = _beatsDaCenaDosRaios; i < n && i < beats.Length; i++)
		{
			if (!beats[i].Faz.HasFlag(Efeito.Raios)) continue;
			_raiosDeCorpoVistos = true;
			_cenaDosRaios = null;
			string rotulo = $"{_rotuloDaCenaDosRaios}, beat {i} aos {beats[i].Em:0.00} s [{beats[i].Faz}]";
			Marcar("os primeiros raios de corpo do processo: " + rotulo);
			_primeirosDesenhos.Add((_quadros.Count, $"os PRIMEIROS RAIOS DE CORPO do processo ({rotulo}: a faisca do `RaiosDaForma`, que e particula)", AtoDoEnsaio.Raios));
			return;
		}
		_beatsDaCenaDosRaios = n;
	}

	/// <summary>
	/// O DEFEITO INJETADO DESTA CORRIDA QUE TIRA DO ENSAIO O QUE O QUADRO USA, ou nulo: e com ele ligado que a regua tem
	/// que REPROVAR o quadro.
	///
	/// A ESTRELA COM LUZ so estraga quem vem DEPOIS dela no ensaio -- os efeitos de ki e os raios da forma. O relampago,
	/// a chuva e o corpo sao ensaiados ANTES dos efeitos de ki (ver `Aquecimento.AtosDoRelampago`), com o lado escuro do
	/// palco ainda escuro: na rodada de noite com `--estrelacomluz` eles continuam regua.
	/// </summary>
	private string? DefeitoQueReprova(AtoDoEnsaio ato) => ato switch
	{
		AtoDoEnsaio.Antigo => _estrelaComLuz ? DefeitoDaEstrela : null,
		AtoDoEnsaio.Raios => _estrelaComLuz ? DefeitoDaEstrela : _semEnsaioDosRaios ? DefeitoDosRaios : null,
		AtoDoEnsaio.Relampago => _semEnsaioDoRelampago ? DefeitoDoRelampago : null,
		AtoDoEnsaio.Corpo => _semEnsaioDoCorpo ? DefeitoDoCorpo : null,
		AtoDoEnsaio.Chuva => _semEnsaioDaChuva ? DefeitoDaChuva : null,
		// (os da rodada dos avulsos: o arquivo `RoboDoPrimeiroEstouro.Avulsos.cs`)
		AtoDoEnsaio.Gota => _semEnsaioDaGota ? DefeitoDaGota : null,
		AtoDoEnsaio.EstouroDePlaneta or AtoDoEnsaio.Planeta => _semEnsaioDoPlaneta ? DefeitoDoPlaneta : null,
		AtoDoEnsaio.Nebulosa => _semEnsaioDaNebulosa ? DefeitoDaNebulosa : null,
		AtoDoEnsaio.Nevoa => _semEnsaioDaNevoa ? DefeitoDaNevoa : null,
		AtoDoEnsaio.Embate => _semEnsaioDoEmbate ? DefeitoDoEmbate : null,
		_ => null,
	};

	/// <summary>
	/// A REGUA, no fecho da rodada: em cada quadro da lista, NENHUMA PIPELINE 2D NASCE (nele nem no seguinte: a
	/// particula pode ser desenhada um quadro depois de solta) E o desenho custa um desenho normal. Ver o cabecalho
	/// deste arquivo pra o porque de serem duas pernas, e pra os defeitos que ela guarda.
	///
	/// O DESENHO DO QUADRO `q` ESTA NA LEITURA DE DOIS QUADROS DEPOIS: o Godot entrega o tempo medido com dois quadros
	/// de atraso (e a fila de quadros do driver), e o <see cref="DesenhoMs"/> ja desconta um deles. A janela impressa
	/// embaixo mostra os vizinhos: a pipeline aparece na linha `q 0` e o custo dela na coluna de desenho da `q 1`.
	///
	/// NAS RODADAS SEM ENSAIO NENHUM (`--semensaio`, `--semaquecimento`) sai como numero: la o processo esta frio por
	/// outra conta.
	/// </summary>
	private void ConferirOsPrimeirosDesenhos()
	{
		bool semEnsaioNenhum = _semEnsaio != null;
		// (a rodada do ceu, `--temas --ceu`, nao toca cinematica nenhuma: os tres quadros de cena nao sao dela)
		if (!semEnsaioNenhum && _temas && !_rodadaDoCeu)
		{
			if (!_anelVisto) Conferir(false, "a rodada chegou no quadro do PRIMEIRO ANEL DE CHOQUE (o primeiro beat `AnelDeChoque` da primeira cinematica)");
			if (!_feixesVistos) Conferir(false, "a rodada chegou no quadro dos FEIXES DO CHAO da primeira cinematica (o primeiro beat `FeixesNoChao` dela)");
			if (!_raiosDeCorpoVistos) Conferir(false, "a rodada chegou no quadro dos PRIMEIROS RAIOS DE CORPO (o primeiro beat `Raios` de uma estreia)");
		}
		if (!semEnsaioNenhum) ConferirQueARodadaChegouNosPrimeirosDesenhos();

		if (_primeirosDesenhos.Count == 0) return;

		// O TETO DO DESENHO: o de um quadro parado mais a folga de um efeito -- a mesma dos outros primeiros usos.
		var desenhos = new List<double>();
		for (int i = _baseDe; i >= 0 && i < _baseAte && i + 1 < _quadros.Count; i++) desenhos.Add(DesenhoMs(i));
		double tetoDoDesenho = Mediana(desenhos) + FolgaDoEfeito;

		foreach ((int q, string oque, AtoDoEnsaio ato) in _primeirosDesenhos)
		{
			int aMais = QuadrosAMais(ato);
			if (q + 3 + aMais >= _quadros.Count) { Conferir(false, $"{oque}: a corrida gravou os {3 + aMais} quadros seguintes"); continue; }

			ulong nascidas = _quadros[q + 2 + aMais].Canvas - _quadros[q].Canvas;
			double desenho = 0;
			for (int k = 0; k <= aMais; k++) desenho = Math.Max(desenho, DesenhoMs(q + 1 + k));
			bool normal = nascidas == 0 && desenho <= tetoDoDesenho;
			string medida = aMais == 0
				? $"{nascidas} pipeline(s) 2D nascida(s) nele e no seguinte; o desenho dele custou {desenho:0.0} ms contra o teto de {tetoDoDesenho:0.0};"
				  + $" quadro inteiro {TotalLiquido(q):0.0} ms"
				: $"{nascidas} pipeline(s) 2D nascida(s) nele e nos {1 + aMais} seguintes; o desenho mais caro deles custou {desenho:0.0} ms contra o teto de {tetoDoDesenho:0.0}";

			string? defeito = DefeitoQueReprova(ato);
			if (defeito != null) Conferir(!normal, $"(defeito injetado: {defeito}) a mesma regua REPROVA {oque} ({medida})");
			else if (semEnsaioNenhum) Nota($"{oque}: {medida} (nesta rodada o aquecimento nao e o do jogo)");
			else Conferir(normal, $"{oque}: nenhuma pipeline 2D nasce, e o desenho custa um desenho normal ({medida})");
			ImprimirJanela(q, 1, 3 + aMais);
		}
	}

	/// <summary>
	/// O INVENTARIO, no fim do relatorio de TODA rodada desta bancada (quem chama e o `Relatar`): todo quadro em que
	/// nasceu pipeline 2D depois de a base comecar, com o desenho dele e o rotulo (o do proprio quadro, ou o do ultimo
	/// quadro rotulado antes dele). Nao cobra nada.
	///
	/// CADA LINHA E UMA DE QUATRO COISAS, e so a primeira e defeito:
	///   * UM PRIMEIRO DESENHO QUE O ENSAIO DO LOBBY NAO FEZ, de um shader DO PROJETO -- com o driver de video quente
	///     custa 1 ms e ninguem ve; na maquina de quem acabou de instalar e um engasgo naquele quadro, do tamanho do
	///     shader (medido com o driver frio: 19 a 23 ms no raio da forma, 25 a 41 no relampago, 28 a 38 na chuva, 101 a
	///     106 no boneco com luz). E a lista do que ainda falta ensaiar, tirada da contagem do proprio motor em vez de
	///     procurada no cronometro;
	///   * UMA MALHA NOVA DO SHADER PADRAO DO MOTOR -- o contorno e o rabicho do primeiro balao de fala, o anel da
	///     primeira mira, o circulo da primeira sombra de voo: desenho de `_Draw` por polilinha ou poligono, que so
	///     difere do que a interface ja desenha no feitio do vertice e na primitiva. O driver ja compilou o shader, e a
	///     pipeline a mais sai quase de graca: com o cache dele NOVO, 0,7 ms de desenho e 2 ms a mais no quadro inteiro
	///     (2026-10-09; a conta esta no cabecalho do `RoboDoPrimeiroEstouro.Nascidas.cs`). Nao e defeito, e nao se ensaia;
	///   * A GEMEA SEM LUZ DE UM EFEITO QUE O ENSAIO SO DESENHOU COM LUZ -- hoje, so o quad da estrela do embate. O motor
	///     decide que um item "tem luz em cima" pelo RETANGULO dele contra o da luz, e a estrela do lado escuro do
	///     palco do ensaio (199 px de meio lado) encosta, por nove decimos de pixel, no retangulo da luz do lado
	///     iluminado: as duas estrelas do ensaio montam a pipeline COM luz, e a sem luz nasce no primeiro embate de dia.
	///     NAO CUSTA: montada depois da gemea iluminada, ela saiu por 0,5 e 0,6 ms de desenho com o `ChoqueDeKi.gdshader`
	///     salgado (`--driverfrio ChoqueDeKi`, duas corridas) e por 1,0 ms -- as duas pipelines do quadro da estrela --
	///     num arquivo de cache do driver estreado no mesmo dia por rodadas de temas, que nao desenham estrela em jogo
	///     (2026-10-09). Pelo jeito, porque a gemea sem luz e o mesmo codigo com o laco das luzes desligado. A ORDEM
	///     CONTRARIA E A CARA: a gemea COM luz do boneco, montada depois da sem luz, custa mais de 100 ms. Ver
	///     `Aquecimento.TamanhoDoPalco`;
	///   * UM MATERIAL GERADO QUE VOLTOU: o `CanvasItemMaterial` de um efeito (a faisca do golpe, o escudo, as faiscas
	///     do estouro e as da estrela) nao tem shader proprio -- o motor gera um por combinacao de modos e o solta
	///     quando morre o ultimo material que a usa (Godot 4.7, `canvas_item_material.cpp`: `shader_map` com contador
	///     de donos, e `free_rid` quando ele zera). O ensaio o monta, o palco sai, e o uso seguinte em jogo o monta de
	///     novo, com o driver ja conhecendo o codigo. Na rodada de producao e +1 no golpe e +1 no estouro das bolas 1,
	///     4 e 7 -- as que vem depois de o efeito anterior sumir e de o coletor do .NET passar --, com 0,4 a 0,9 ms de
	///     desenho no quadro (tres corridas em 2026-10-09, driver quente). QUE ESSAS LINHAS SEJAM DELE E DEDUCAO: elas
	///     VOLTAM a cada leva, e um primeiro desenho de verdade so acontece uma vez por processo.
	/// O que separa a primeira da ultima e a repeticao, e nao o rotulo. (A LINHA QUE NINGUEM TINHA DESTRINCHADO -- a
	/// primeira estrela do embate da rodada `--pecas`, com duas pipelines com o ensaio e duas sem ele -- era uma de cada
	/// uma das duas ultimas: a das faiscas, que volta, e a do proprio quad sem luz. Quem as separou foi a medicao
	/// `--pecas --estrelapartida`: ver o `RoboDoPrimeiroEstouro.Nascidas.cs`.)
	/// </summary>
	private void ListarAsPipelinesNascidasEmJogo()
	{
		if (_baseDe < 0) return;

		var linhas = new List<string>();
		for (int i = _baseDe; i + 2 < _quadros.Count; i++)
		{
			ulong nascidas = _quadros[i + 1].Canvas - _quadros[i].Canvas;
			if (nascidas == 0) continue;

			string rotulo = "(sem rotulo)";
			for (int k = i; k >= _baseDe && k > i - 600; k--)
			{
				string marcas = _quadros[k].Marcas;
				if (marcas.Length == 0) continue;
				rotulo = (k == i ? "" : $"{i - k} quadro(s) depois de: ") + (marcas.Length > 200 ? marcas[..200] + "..." : marcas);
				break;
			}
			linhas.Add($"           t={(_quadros[i].Comeco - _quadros[_baseDe].Comeco) / 1e6,7:0.000}s  pipelines 2D +{nascidas}"
					   + $"  o desenho dele custou {DesenhoMs(i + 1),5:0.0} ms, quadro inteiro {TotalMs(i),5:0.0} ms   <-- {rotulo}");
		}

		Nota($"toda pipeline 2D que nasceu depois de a base comecar ({linhas.Count} quadro(s)) -- ou e um primeiro desenho que o ensaio do lobby"
			 + " nao fez, ou uma malha nova do shader padrao do motor (quase de graca), ou o material gerado de um efeito ensaiado"
			 + " (`CanvasItemMaterial`), que morre com o ultimo dono e volta, ou a gemea sem luz do quad da estrela do embate"
			 + " (de graca tambem):");
		_linhas.AddRange(linhas);
	}
}
