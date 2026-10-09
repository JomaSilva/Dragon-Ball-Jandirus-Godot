using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// OS PRIMEIROS USOS AVULSOS (`--diagestouro --avulsos`): os seis shaders do projeto que ato nenhum do ensaio do lobby
/// desenhava e que nenhuma rodada desta bancada punha na tela em jogo -- a GOTA do transe, o ESTOURO de planeta, a
/// NEBULOSA do Ultra Instinto, a NEVOA de altitude, a tela do EMBATE e o PLANETA visto do espaco. (O setimo, o da
/// miragem do Zanzoken, e de outra frente: o arquivo `RoboDoPrimeiroEstouro.Miragem.cs`.) A historia e os numeros estao
/// no cabecalho do `RoboDoPrimeiroEstouro`, no bloco "E OS SEIS QUE NENHUMA RODADA PUNHA NA TELA"; aqui fica o que e so
/// desta rodada.
///
/// ============================ O QUE SE MEDE ============================
/// O QUADRO DO PRIMEIRO USO DE CADA UM, sozinho: um gesto a cada segundo e meio ou dois, pela porta por onde o jogo
/// chega no efeito, com o corpo no mundo e a base ja medida. O quadro sai partido em tres -- o SCRIPT, o PREPARO do
/// desenho (e onde a thread principal espera um shader que ainda esta compilando) e a TELA (e onde a pipeline e
/// montada) --, com as pipelines 2D que o motor montou nele e no seguinte.
///
/// DUAS PASSADAS, e a segunda com uma luz em cima do corpo: o motor desenha o item que tem luz em cima com OUTRA
/// pipeline do mesmo shader (a gemea iluminada), e a ordem cara e esta -- a COM luz montada depois da SEM luz. A luz e a
/// do `--raioscomluz`; aqui e a propria rodada que a acende, entre uma passada e a outra.
///
/// ============================ OS GESTOS, NA ORDEM ============================
///   1. a GOTA -- `World.AoCairEfeito("ondulacao")`, a chamada do pacote de efeito: a `GotaNaTela` nasce ali e ondula
///      no mesmo quadro;
///   2. o ESTOURO -- `World.AoCairEfeito("explosao_final")`: um quad com o `EstouroDePlaneta.gdshader` em volta do
///      corpo (a Final Explosion, a Aura da Destruicao, o tremor de um planeta que morre);
///   3. a NEBULOSA -- `NebulosaDaForma.Definir` no node do corpo local, a chamada dos dois caminhos que vestem uma
///      forma: o quad dela nasce invisivel com o corpo, e so e desenhado quando alguem entra em Ultra Instinto;
///   4. a NEVOA -- o corpo decola pela tecla do voo (`SendHabilidade("voar")`): o `World` cria a `NevoaDeAltitude`
///      no primeiro quadro em que o corpo tem altura;
///   (a luz acende, e os quatro de cima se repetem)
///   5. o EMBATE -- um ZanzoClash de verdade, armado no servidor pela fixture da bancada da mudez
///      (`GameServer.EmbateDeVelocidadeDeTeste`): a tela do `ClashQte` nasce invisivel com o mundo, e o fundo dela (o
///      `Embate.gdshader`) so e desenhado quando o pacote do comeco chega. Fica numa camada de interface: luz nenhuma
///      do mundo a alcanca, e por isso ela so tem a primeira passada;
///   6. o PLANETA -- o servidor leva o corpo pro espaco (`GameServer.MoveToZone`), em cima da Terra: cada disco e um
///      sprite com o `PlanetaMorrendo.gdshader`. Depois a luz acende de novo, e por fim o planeta estoura
///      (`PlanetaDesenhado.AplicarAgonia`), com o mesmo shader do gesto 2 -- o CONTROLE: as duas casas do estouro usam
///      as mesmas pipelines, e nenhuma nasce nele.
///
/// ============================ A REGUA ============================
/// A DOS PRIMEIROS DESENHOS (o arquivo `RoboDoPrimeiroEstouro.Raios.cs`), em cada um dos onze quadros: NENHUMA
/// PIPELINE 2D NASCE nele -- contagem do proprio motor, que reprova o defeito sempre -- e o DESENHO custa um desenho
/// normal, que e a perna que o jogador sente e so morde com o driver de video frio. E mais duas, daqui:
///   * O ARQUIVO JA ESTAVA NA MEMORIA antes do primeiro gesto, pros quatro shaders que o jogo lia do disco na hora (a
///     gota, o estouro, a nevoa, o planeta) -- um fato perguntado ao Godot, e nao um tempo;
///   * O QUADRO CUSTA UM QUADRO NORMAL, na gota, no estouro, na nevoa e no embate. Os outros saem como numero, porque o
///     quadro deles tem uma conta que nao e de shader: o da nebulosa constroi o campo de distancia da silhueta (13 a
///     14 ms de script na primeira vez), e os do espaco sao o quadro em que a zona monta.
/// E o item de cada uso tem que estar NA TELA: item fora dela, ou invisivel, nao e desenhado, e sem desenho nao ha
/// pipeline -- o quadro passaria pela regua sem provar nada.
///
/// OS CINCO DEFEITOS QUE ELA GUARDA sao os do `Aquecimento` sem o ato (e sem o arquivo na fila) de cada um:
/// `--semensaiodagota`, `--semensaiodoplaneta` (o planeta e o estouro dele), `--semensaiodanebulosa`,
/// `--semensaiodanevoa` e `--semensaiodoembate`. Com os cinco na linha, a mesma regua tem que reprovar os onze quadros.
///
/// A RODADA PRECISA DE `--mudezteste` (as fixtures do embate) E DE `--vooteste` (a skill de voo) na linha.
/// </summary>
public partial class RoboDoPrimeiroEstouro
{
	/// <summary>`--avulsos`: A RODADA DOS PRIMEIROS USOS AVULSOS. Sozinha: nao divide processo com as outras rodadas.</summary>
	private readonly bool _avulsos = Tem("--avulsos") && !Tem("--temas") && !Tem("--pecas") && !Tem("--sons") && !Tem("--scripts") && Passivo() <= 0;

	/// <summary>
	/// AS CINCO RODADAS DE INJECAO (os defeitos do `Aquecimento` de mesmo nome, ligados pelo `Boot` na linha em que o
	/// aquecimento nasce): o jogo de antes, em que ninguem desenhava o efeito antes da hora. A mesma regua tem que REPROVAR
	/// os quadros de cada um.
	/// </summary>
	private readonly bool _semEnsaioDaGota = Tem("--semensaiodagota"), _semEnsaioDaNevoa = Tem("--semensaiodanevoa"),
						  _semEnsaioDaNebulosa = Tem("--semensaiodanebulosa"), _semEnsaioDoEmbate = Tem("--semensaiodoembate"),
						  _semEnsaioDoPlaneta = Tem("--semensaiodoplaneta");

	private const string DefeitoDaGota = "o aquecimento sem o shader e sem o ensaio da gota",
						 DefeitoDaNevoa = "o aquecimento sem o shader e sem o ensaio da nevoa de altitude",
						 DefeitoDaNebulosa = "o aquecimento sem o ensaio da nebulosa",
						 DefeitoDoEmbate = "o aquecimento sem o ensaio da tela do embate",
						 DefeitoDoPlaneta = "o aquecimento sem os shaders e sem o ensaio do planeta e do estouro dele";

	private enum Avulso { Gota, Estouro, Nebulosa, Nevoa, Embate, Planeta, EstouroNoEspaco }

	/// <summary>Quanto dura a gota do gesto, e o `ms` do estouro (600 e o da Final Explosion: um quad de 660 px de lado), em ms.</summary>
	private const long MsDaGota = 1200, MsDoEstouro = 600;

	/// <summary>A forma cuja paleta acende a nebulosa do gesto: o primeiro estagio do Ultra Instinto.</summary>
	private const string FormaDaNebulosa = "ui_sign";

	private static readonly NodePath CaminhoDaNevoa = "NevoaDeAltitude/Nevoa", CaminhoDosPlanetas = "Planetas";

	/// <summary>O arquivo do shader que cada gesto usa pela primeira vez.</summary>
	private static string ShaderDoAvulso(Avulso a)
	{
		string nome = a switch
		{
			Avulso.Gota => "Gota",
			Avulso.Estouro or Avulso.EstouroNoEspaco => "EstouroDePlaneta",
			Avulso.Nebulosa => "NebulosaDaForma",
			Avulso.Nevoa => "Altitude",
			Avulso.Embate => "Embate",
			_ => "PlanetaMorrendo",
		};
		return $"{PastaDosShaders}/{nome}.gdshader";
	}

	/// <summary>Quem no ensaio do lobby desenha o que cada gesto usa -- e, por tabela, que defeito injetado o reprova.</summary>
	private static AtoDoEnsaio AtoDoAvulso(Avulso a) => a switch
	{
		Avulso.Gota => AtoDoEnsaio.Gota,
		Avulso.Estouro or Avulso.EstouroNoEspaco => AtoDoEnsaio.EstouroDePlaneta,
		Avulso.Nebulosa => AtoDoEnsaio.Nebulosa,
		Avulso.Nevoa => AtoDoEnsaio.Nevoa,
		Avulso.Embate => AtoDoEnsaio.Embate,
		_ => AtoDoEnsaio.Planeta,
	};

	private static string NomeDoAvulso(Avulso a) => a switch
	{
		Avulso.Gota => "a GOTA do transe (`World.AoCairEfeito(\"ondulacao\")` -> `GotaNaTela.Cair`: um retangulo de tela cheia com o `Gota.gdshader`)",
		Avulso.Estouro => "o ESTOURO no mundo (`World.AoCairEfeito(\"explosao_final\")` -> `EstouroNoMundo`: um quad com o `EstouroDePlaneta.gdshader`)",
		Avulso.Nebulosa => "a NEBULOSA do Ultra Instinto (`NebulosaDaForma.Definir` no corpo local: um sprite com o `NebulosaDaForma.gdshader`)",
		Avulso.Nevoa => "a NEVOA de altitude (o corpo decola -> `World.EfeitosDaAltura` -> `NevoaDeAltitude`: um retangulo de tela cheia com o `Altitude.gdshader`)",
		Avulso.Embate => "a tela do EMBATE (o pacote do comeco de um ZanzoClash -> `ClashQte.Comecou`: o fundo de tela cheia com o `Embate.gdshader`)",
		Avulso.Planeta => "o PLANETA visto do espaco (`World.DesenharPlanetas` -> `PlanetaDesenhado`: um sprite com o `PlanetaMorrendo.gdshader`)",
		_ => "o planeta ESTOURA no espaco (`PlanetaDesenhado.AplicarAgonia` -> `Estourar`: um quad com o `EstouroDePlaneta.gdshader`)",
	};

	/// <summary>O que esta rodada tem de proprio dos avulsos, pra nota "rodada:" do relatorio.</summary>
	private string RotuloDosAvulsos() =>
		(_semEnsaioDaGota ? $" | DEFEITO INJETADO: {DefeitoDaGota} (`--semensaiodagota`)" : "")
		+ (_semEnsaioDoPlaneta ? $" | DEFEITO INJETADO: {DefeitoDoPlaneta} (`--semensaiodoplaneta`)" : "")
		+ (_semEnsaioDaNebulosa ? $" | DEFEITO INJETADO: {DefeitoDaNebulosa} (`--semensaiodanebulosa`)" : "")
		+ (_semEnsaioDaNevoa ? $" | DEFEITO INJETADO: {DefeitoDaNevoa} (`--semensaiodanevoa`)" : "")
		+ (_semEnsaioDoEmbate ? $" | DEFEITO INJETADO: {DefeitoDoEmbate} (`--semensaiodoembate`)" : "");

	/// <summary>Cada primeiro uso que a rodada alcancou: qual, se a luz estava acesa, o quadro, e o que o Godot respondeu antes e depois dele.</summary>
	private readonly List<(Avulso Qual, bool ComLuz, int Quadro, bool NaMemoriaAntes, bool NaTela, string Nota)> _usosAvulsos = [];

	/// <summary>
	/// Os usos a espera de conferencia no fim do quadro, e quantos quadros ainda se espera pelo item de cada um: e no fim
	/// do quadro que se sabe se o item esta na tela, e o do planeta pode entrar nela um ou dois quadros depois de nascer
	/// (a camera ainda esta chegando na zona).
	/// </summary>
	private readonly List<(int Uso, int Quadros)> _usosAConferir = [];

	/// <summary>O FATO DE PARTIDA de cada shader: o Godot o tinha no cache antes do primeiro gesto da rodada (`ResourceLoader.HasCached`)?</summary>
	private readonly Dictionary<string, bool> _avulsoNaMemoria = new(StringComparer.Ordinal);

	/// <summary>Os shaders que a bancada carregou ela mesma, pra o sal do `--driverfrio` entrar antes do primeiro material. Ver <see cref="PrepararOsAvulsos"/>.</summary>
	private readonly List<Shader> _carregadosPraOSal = [];

	private int _passoAvulso;
	private bool _avulsosComLuz, _ouvindoOEmbate, _embatePedido, _embateVisto, _esperandoANevoa, _nevoaVista, _esperandoOPlaneta, _planetaVisto;
	private double _embateAos, _nevoaAos, _planetaAos;
	private PointLight2D? _luzAvulsa;
	private bool _zonaTrocou, _coberturaNoPlaneta;

	/// <summary>
	/// Quao escuro o espaco esta pra as luzes de efeito, lido no ultimo gesto da rodada (segundos depois de a zona montar:
	/// no quadro em que o planeta aparece o ambiente ainda e o do planeta de onde se veio): e o que diz se uma luz pode
	/// alcancar um planeta em jogo.
	/// </summary>
	private float _escuridaoNoEspaco = -1, _noiteNoEspaco = -1;

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	/// <summary>
	/// UM GESTO POR VEZ, cada primeiro uso num quadro so dele. Chamado todo quadro depois da base (quem chama e o
	/// `_Process`); cada passo comeca pela espera dele.
	/// </summary>
	private void AndarNosAvulsos(World mundo, Jandirus.Server.GameServer srv, GameClient cli)
	{
		switch (_passoAvulso)
		{
			case 0:
				if (_t < 1.0) return;
				PrepararOsAvulsos(cli);
				break;

			// ---- OS QUATRO GESTOS DO CHAO: a primeira passada (1 a 6) sem luz, a segunda (8 a 13) com a luz em cima do corpo ----
			case 1 or 8:
			{
				if (_t < 1.5) return;
				bool antes = NaMemoria(Avulso.Gota);
				mundo.AoCairEfeito("ondulacao", MsDaGota);
				AnotarOAvulso(Avulso.Gota, antes);
				break;
			}
			case 2 or 9:
			{
				if (mundo.OndulandoDeTeste || _t < 2.0) { if (_t > 8) Desistir("a gota do gesto acabou de ondular em 8 s"); return; }
				bool antes = NaMemoria(Avulso.Estouro);
				mundo.AoCairEfeito("explosao_final", MsDoEstouro);
				AnotarOAvulso(Avulso.Estouro, antes);
				break;
			}
			case 3 or 10:
			{
				if (_t < 2.0) return;
				if (NebulosaDoCorpo(mundo, cli) is not { } nuvem) { Desistir("o corpo local tem o node da nebulosa (`Nebulosa`)"); return; }
				bool antes = NaMemoria(Avulso.Nebulosa);
				nuvem.Definir(Catalogo.PaletaDaNebulosa(Catalogo.Def(FormaDaNebulosa)));
				AnotarOAvulso(Avulso.Nebulosa, antes);
				break;
			}
			case 4 or 11:
				if (_t < 1.5) return;
				NebulosaDoCorpo(mundo, cli)?.Definir(null);
				Marcar("a nebulosa apaga");
				break;
			case 5 or 12:
				if (_t < 0.8) return;
				_nevoaVista = false;
				_esperandoANevoa = true;
				cli.SendHabilidade("voar");
				Marcar("o corpo DECOLA (`SendHabilidade(\"voar\")`, a tecla do voo)");
				break;
			case 6 or 13:
				if (!_nevoaVista) { if (_t > 8) Desistir("a nevoa de altitude apareceu em 8 s depois de o corpo decolar (a rodada precisa de `--vooteste`)"); return; }
				if (_vida - _nevoaAos < 2.0) return;
				cli.SendHabilidade("voar");
				Marcar("o corpo POUSA (a mesma tecla)");
				break;

			// ---- pousou: A LUZ ACENDE, e os quatro se repetem ----
			case 7:
				if (mundo.AlturaDeTeste > 0.01f || _t < 1.5) { if (_t > 12) Desistir("o corpo pousou em 12 s"); return; }
				if (!AcenderALuzAvulsa(mundo, cli)) { Desistir("o corpo local existe pra receber a luz da segunda passada"); return; }
				_avulsosComLuz = true;
				Marcar("a LUZ da segunda passada acende em cima do corpo");
				AnotarOPrimeiroCorpoIluminado("a luz da rodada dos avulsos, acesa em cima do corpo");
				break;

			// ---- pousou de novo: a luz apaga, e vem O EMBATE ----
			case 14:
				if (mundo.AlturaDeTeste > 0.01f || _t < 1.5) { if (_t > 12) Desistir("o corpo pousou em 12 s (segunda passada)"); return; }
				ApagarALuzAvulsa();
				_avulsosComLuz = false;
				Marcar("a luz da segunda passada apaga");
				break;
			case 15:
				if (_t < 1.0) return;
				_embatePedido = true;
				if (!srv.EmbateDeVelocidadeDeTeste(cli.LocalId))
				{
					Conferir(false, "o servidor armou o ZanzoClash do gesto do embate (a rodada precisa de `--mudezteste`, e o gatilho de producao tem que aceitar)");
					_passoAvulso = 18;
					_t = 0;
					return;
				}
				Marcar("o servidor ARMA um ZanzoClash contra um corpo forjado ao lado (`GameServer.EmbateDeVelocidadeDeTeste`)");
				break;
			case 16:
				if (!_embateVisto)
				{
					if (_t <= 4) return;
					Conferir(false, "o pacote do comeco do embate chegou ao cliente em 4 s");
					_passoAvulso = 17;
					_t = 0;
					return;
				}
				if (_vida - _embateAos < 1.5) return;
				srv.SoltarEmbateDeTeste(cli.LocalId);
				Marcar("o embate e ENCERRADO no servidor (`GameServer.SoltarEmbateDeTeste`)");
				break;
			case 17:
				// (o desfecho fica 2,2 s na tela e o fundo sai em fade: so depois disso a tela do embate apaga)
				if (_t < 3.6) return;
				srv.LimparAMudezDeTeste();
				break;

			// ---- O ESPACO ----
			case 18:
				if (_t < 0.5) return;
				if (!IrProEspaco(srv, cli)) return;
				break;
			case 19:
				if (!_planetaVisto) { if (_t > 12) Desistir("um planeta foi desenhado em 12 s depois de o corpo ir pro espaco"); return; }
				if (_vida - _planetaAos < 2.0) return;
				if (!AcenderALuzAvulsa(mundo, cli)) { Desistir("o corpo local existe pra receber a luz no espaco"); return; }
				_avulsosComLuz = true;
				Marcar("a LUZ acende em cima do corpo, no espaco");
				AnotarOAvulso(Avulso.Planeta, NaMemoria(Avulso.Planeta));
				break;
			case 20:
			{
				if (_t < 1.5) return;
				// (o planeta e procurado de novo, e nao guardado: o `World.DesenharPlanetas` refaz os discos a cada pacote de vizinhanca)
				if (PlanetaMaisPerto(mundo) is not { } planeta) { Desistir("ha um planeta desenhado pra estourar no gesto do espaco"); return; }
				bool antes = NaMemoria(Avulso.EstouroNoEspaco);
				_escuridaoNoEspaco = Iluminacao.Escuridao;
				_noiteNoEspaco = Iluminacao.ForcaDaNoite();
				planeta.AplicarAgonia(1.0, 0.0);
				AnotarOAvulso(Avulso.EstouroNoEspaco, antes);
				break;
			}

			default:
				if (_t >= 2.6) Fechar();
				return;
		}
		_passoAvulso++;
		_t = 0;
	}

	private void Desistir(string oque)
	{
		Conferir(false, oque);
		Fechar();
	}

	private static bool NaMemoria(Avulso a) => ResourceLoader.HasCached(ShaderDoAvulso(a));

	private static NebulosaDaForma? NebulosaDoCorpo(World mundo, GameClient cli) =>
		mundo.CorpoDeTeste(cli.LocalId)?.GetNodeOrNull<NebulosaDaForma>("Nebulosa");

	/// <summary>
	/// O PRIMEIRO PASSO E SO O PREPARO, um segundo e meio antes de qualquer gesto: o fato de partida de cada shader (esta
	/// na memoria?), a escuta do pacote do embate e, com `--verbose` na linha, o ouvido das cargas.
	///
	/// E O CASO QUE SO A BANCADA TEM: `--driverfrio` de um shader que NAO veio do lobby (as rodadas de injecao: sem o
	/// conserto, a gota, a nevoa, o estouro e o planeta so sao lidos na hora do primeiro uso). O sal entra num shader ja
	/// carregado, e desses nao ha nenhum na memoria -- lido no quadro do gesto, o sal chegaria depois de o primeiro
	/// material ter comecado a compilar o codigo sem sal, e o quadro pagaria duas compilacoes. A bancada o carrega aqui e
	/// o segura: a leitura do arquivo sai do quadro medido (quem a cobra e a perna do fato, que ja foi anotada), e o que
	/// fica nele e o shader compilando uma vez e a pipeline montada do zero, como na maquina de quem acabou de instalar.
	/// </summary>
	private void PrepararOsAvulsos(GameClient cli)
	{
		LigarAEscutaDeCarga();

		// (a regua da musica de combate nao e desta rodada: o embate do gesto 5 abre uma luta, e a faixa dela so ganha rotulo)
		_lutaAberta = true;

		foreach (Avulso a in Enum.GetValues<Avulso>())
		{
			string caminho = ShaderDoAvulso(a);
			_avulsoNaMemoria.TryAdd(caminho, ResourceLoader.HasCached(caminho));
		}

		foreach (string caminho in _shadersFrios)
		{
			if (_salgados.ContainsKey(caminho) || ResourceLoader.HasCached(caminho) || !ResourceLoader.Exists(caminho)) continue;
			if (ResourceLoader.Load<Shader>(caminho) is not { } shader) continue;
			_carregadosPraOSal.Add(shader);
			Marcar($"`--driverfrio` sem o `{caminho.GetFile()}` na memoria: a bancada o carrega aqui, pra o sal entrar antes do primeiro uso");
		}

		if (!_ouvindoOEmbate)
		{
			_ouvindoOEmbate = true;
			cli.ClashComecou += AoComecarOEmbateAvulso;
		}
	}

	/// <summary>O pacote do comeco de um embate chegou neste quadro: se e o do gesto, e o quadro do primeiro uso da tela dele.</summary>
	private void AoComecarOEmbateAvulso(Jandirus.Net.Protocol.TipoDeEmbate tipo, int a, int b, int ms, float meu, float dele)
	{
		if (!_embatePedido || _embateVisto) return;
		_embateVisto = true;
		_embateAos = _vida;
		AnotarOAvulso(Avulso.Embate, NaMemoria(Avulso.Embate));
	}

	/// <summary>
	/// A LUZ DA SEGUNDA PASSADA: a mesma do `--raioscomluz` (a radial das luzes de ki, filha do corpo local, com um
	/// retangulo de 307 px de lado), acesa pela propria rodada.
	/// </summary>
	private bool AcenderALuzAvulsa(World mundo, GameClient cli)
	{
		if (mundo.CorpoDeTeste(cli.LocalId) is not { } corpo) return false;
		ApagarALuzAvulsa();
		_luzAvulsa = new PointLight2D
		{
			Name = "LuzDaBancadaDosAvulsos",
			Texture = Fogo.Radial(LuzDeKi.RaioDaTextura),
			TextureScale = 1.6f,
			Energy = 0.5f,
		};
		corpo.AddChild(_luzAvulsa);
		return true;
	}

	private void ApagarALuzAvulsa()
	{
		if (_luzAvulsa != null && IsInstanceValid(_luzAvulsa)) _luzAvulsa.QueueFree();
		_luzAvulsa = null;
	}

	/// <summary>
	/// O SERVIDOR LEVA O CORPO PRO ESPACO, logo acima da Terra (`Espaco.PontoDeDecolagem`: 90 px acima da superficie
	/// dela, que e onde quem decola aparece). Devolve falso -- e fecha a rodada -- se nao ha planeta pra onde ir.
	/// </summary>
	private bool IrProEspaco(Jandirus.Server.GameServer srv, GameClient cli)
	{
		PlanetaNoEspaco alvo = default!;
		bool achei = false;
		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
		{
			if (achei && !string.Equals(p.Nome, "Earth", StringComparison.OrdinalIgnoreCase)) continue;
			alvo = p;
			achei = true;
		}
		if (!achei) { Desistir("ha um planeta pre-feito na carta estelar pra o gesto do espaco"); return false; }

		_zonaTrocou = false;
		_planetaVisto = false;
		_esperandoOPlaneta = true;
		ApagarALuzAvulsa();
		_avulsosComLuz = false;
		srv.MoveToZone(cli.LocalId, Espaco.Zona(cli.SeedDoUniverso), Espaco.PontoDeDecolagem(alvo));
		Marcar($"o servidor leva o corpo pro ESPACO, em cima de `{alvo.Nome}` (`GameServer.MoveToZone`)");
		return true;
	}

	// =====================================================================
	// O FIM DE CADA QUADRO
	// =====================================================================
	/// <summary>
	/// No fim de cada quadro, depois de todos os scripts (quem chama e o <see cref="NoFimDoQuadro"/>) -- so nesta rodada:
	///
	///   * guarda o PREPARO e a TELA que o Godot acabou de entregar (a mesma gaveta das rodadas das cenas e das pecas,
	///     que nesta ninguem mais enche);
	///   * ve os dois primeiros usos que nao acontecem no quadro do gesto -- a nevoa, que o `World` cria quando a altura
	///     do corpo chega pelo servidor, e o planeta, que nasce quando a zona do espaco monta;
	///   * confere se o item de cada uso anotado esta NA TELA: item fora dela (ou invisivel) nao e desenhado, e sem
	///     desenho nao ha pipeline -- o quadro passaria pela regua sem provar nada.
	/// </summary>
	private void NoFimDoQuadroDosAvulsos()
	{
		if (!_avulsos) return;

		if (_primeiroQuadroPartido < 0) _primeiroQuadroPartido = _quadros.Count;
		_desenhoPartido.Add((RenderingServer.GetFrameSetupTimeCpu(), RenderingServer.ViewportGetMeasuredRenderTimeCpu(_tela)));

		if (World.Instancia is not { } mundo || C is not { } cli) return;

		if (_esperandoANevoa && mundo.GetNodeOrNull<ColorRect>(CaminhoDaNevoa) is { Visible: true })
		{
			_esperandoANevoa = false;
			_nevoaVista = true;
			_nevoaAos = _vida;
			// (sem o conserto o shader da nevoa e lido no `_Ready` dela, neste mesmo quadro: "na memoria antes" e o fato
			// de partida -- e, na segunda passada, verdade)
			AnotarOAvulso(Avulso.Nevoa, _avulsosComLuz || _avulsoNaMemoria.GetValueOrDefault(ShaderDoAvulso(Avulso.Nevoa)));
		}

		if (_esperandoOPlaneta)
		{
			if (!_zonaTrocou && Espaco.EhEspaco(cli.Zone))
			{
				_zonaTrocou = true;
				Marcar("a zona do cliente passou a ser o ESPACO");
			}
			if (PlanetaMaisPerto(mundo) != null)
			{
				_esperandoOPlaneta = false;
				_planetaVisto = true;
				_planetaAos = _vida;
				_coberturaNoPlaneta = TelaDeCarregamento.Instancia is { NoAr: true };
				AnotarOAvulso(Avulso.Planeta, _avulsoNaMemoria.GetValueOrDefault(ShaderDoAvulso(Avulso.Planeta)));
			}
		}

		for (int k = _usosAConferir.Count - 1; k >= 0; k--)
		{
			(int n, int restam) = _usosAConferir[k];
			(Avulso qual, bool comLuz, int quadro, bool antes, _, _) = _usosAvulsos[n];
			CanvasItem? item = ItemDoAvulso(qual, mundo, cli);
			bool naTela = item != null && item.IsVisibleInTree() && ItemNaTela(item);
			if (!naTela && restam > 0) { _usosAConferir[k] = (n, restam - 1); continue; }

			int depois = _quadros.Count - quadro;
			string nota = naTela ? (depois > 0 ? $"entrou na tela {depois} quadro(s) depois" : "")
				: item == null ? "o item nao foi achado" : !item.IsVisibleInTree() ? "o item esta INVISIVEL" : "o item esta FORA da tela";
			_usosAvulsos[n] = (qual, comLuz, quadro, antes, naTela, nota);
			_usosAConferir.RemoveAt(k);
		}
	}

	/// <summary>O planeta desenhado mais perto do corpo local, ou nulo enquanto a zona do espaco nao desenhou nenhum.</summary>
	private static PlanetaDesenhado? PlanetaMaisPerto(World mundo)
	{
		if (mundo.GetNodeOrNull<Node2D>(CaminhoDosPlanetas) is not { } orbes || mundo.PosicaoLocal is not { } eu) return null;
		PlanetaDesenhado? perto = null;
		float d2 = float.MaxValue;
		foreach (Node n in orbes.GetChildren())
		{
			if (n is not PlanetaDesenhado p || p.IsQueuedForDeletion()) continue;
			float d = p.GlobalPosition.DistanceSquaredTo(eu);
			if (d < d2) { d2 = d; perto = p; }
		}
		return perto;
	}

	/// <summary>
	/// O PAPEL DO QUADRO DE UM USO na perna do TEMPO (a das pipelines e a mesma pra todos: ver <see cref="AnotarOAvulso"/>).
	///
	/// REGUA na gota, no estouro, na nevoa e no embate. NUMERO nos outros, porque o quadro deles tem uma conta que nao e de
	/// shader e fica na beira do teto com o conserto ou sem ele: o da nebulosa constroi o campo de distancia da silhueta
	/// na primeira vez (13 a 14 ms de script), e os do espaco sao o quadro em que a zona monta e o do campo de destrocos.
	///
	/// NAS RODADAS DE INJECAO o quadro so tem que REPROVAR quando ha o que esperar: com o cache de shader do Godot vazio (o
	/// de toda bancada) a thread principal espera a compilacao no primeiro uso sem luz, e com o driver frio a pipeline e
	/// montada do zero -- a da gemea iluminada so custa assim. Fora disso o defeito inteiro sao poucos milissegundos, e o
	/// quadro sai como numero: quem reprova ai e a contagem. O do embate sai sempre como numero: o custo dele e so de
	/// tela (14 a 20 ms com o driver frio), e o quadro inteiro fica na beira do teto.
	/// </summary>
	private Papel PapelDoAvulso(Avulso qual, bool comLuz)
	{
		if (_semEnsaio != null || qual is Avulso.Nebulosa or Avulso.Planeta or Avulso.EstouroNoEspaco) return Papel.Nota;
		if (DefeitoQueReprova(AtoDoAvulso(qual)) == null) return Papel.Regua;
		if (qual == Avulso.Embate) return Papel.Nota;
		bool frio = Array.IndexOf(_shadersFrios, ShaderDoAvulso(qual)) >= 0;
		return (comLuz ? frio : frio || _shadersEmDiscoNoComeco == 0) ? Papel.Injetado : Papel.Nota;
	}

	/// <summary>
	/// UM PRIMEIRO USO CAIU NESTE QUADRO: vira evento (a perna do tempo), entra na lista dos primeiros desenhos (a das
	/// pipelines e do desenho) e o item dele e conferido no fim do quadro.
	///
	/// O ESTOURO NO ESPACO NAO ENTRA NA LISTA: e o controle -- o quad dele e o do estouro no mundo (os gestos 2), e as
	/// pipelines ja nasceram la, com o conserto e sem ele. Quem o cobra e o <see cref="RelatarOsAvulsos"/>.
	/// </summary>
	private void AnotarOAvulso(Avulso qual, bool naMemoriaAntes)
	{
		bool comLuz = _avulsosComLuz;
		string oque = NomeDoAvulso(qual) + (comLuz ? ", COM LUZ em cima do corpo (a gemea iluminada)" : ", sem luz");
		AtoDoEnsaio ato = AtoDoAvulso(qual);
		Evento("AVULSO: " + oque, PapelDoAvulso(qual, comLuz), DefeitoQueReprova(ato) ?? "");
		if (qual != Avulso.EstouroNoEspaco) _primeirosDesenhos.Add((_quadros.Count, "AVULSO -- " + oque, ato));
		// (o planeta pode entrar na tela ate tres quadros depois de nascer; os outros estao nela no quadro do gesto)
		_usosAConferir.Add((_usosAvulsos.Count, qual == Avulso.Planeta ? 3 : 0));
		_usosAvulsos.Add((qual, comLuz, _quadros.Count, naMemoriaAntes, false, ""));
	}

	/// <summary>O item de tela que carrega o shader de cada uso -- procurado no quadro do uso.</summary>
	private CanvasItem? ItemDoAvulso(Avulso qual, World mundo, GameClient cli) => qual switch
	{
		Avulso.Gota => mundo.GetNodeOrNull<CanvasItem>("GotaNaTela/Gota"),
		Avulso.Estouro => mundo.FindChild("Estouro", true, false) as CanvasItem,
		Avulso.Nebulosa => mundo.CorpoDeTeste(cli.LocalId)?.GetNodeOrNull<CanvasItem>("Nebulosa/Quad"),
		Avulso.Nevoa => mundo.GetNodeOrNull<CanvasItem>(CaminhoDaNevoa),
		Avulso.Embate => GetTree().Root.FindChild("ClashQte", true, false)?.FindChildren("*", "ColorRect", true, false) is [CanvasItem fundo, ..] ? fundo : null,
		Avulso.Planeta => PlanetaMaisPerto(mundo)?.GetNodeOrNull<CanvasItem>("Icone"),
		_ => PlanetaMaisPerto(mundo)?.GetNodeOrNull<CanvasItem>("Estouro"),
	};

	/// <summary>
	/// O ITEM TEM PEDACO DENTRO DA TELA? O de uma camada de interface (`CanvasLayer`) mora no espaco da tela; o do mundo,
	/// no da camera -- a mesma conta do `RiscoNaTela`.
	/// </summary>
	private static bool ItemNaTela(CanvasItem item)
	{
		if (item.GetViewport() is not { } tela) return false;
		Rect2 r = item switch
		{
			Control c => c.GetGlobalRect(),
			Sprite2D s => s.GetGlobalTransform() * s.GetRect(),
			_ => new Rect2(),
		};
		if (r.Size.X <= 0 || r.Size.Y <= 0) return false;

		for (Node? pai = item.GetParent(); pai != null; pai = pai.GetParent())
			if (pai is CanvasLayer) return tela.GetVisibleRect().Intersects(r);

		Camera2D? camera = tela.GetCamera2D();
		Vector2 mundo = camera != null ? tela.GetVisibleRect().Size / camera.Zoom : tela.GetVisibleRect().Size;
		Vector2 centro = camera?.GetScreenCenterPosition() ?? mundo * 0.5f;
		return new Rect2(centro - mundo * 0.5f, mundo).Intersects(r);
	}

	/// <summary>
	/// A escuta do embate, a luz e os cinco defeitos saem: o evento e do cliente e os defeitos sao campos ESTATICOS de
	/// producao -- os dois sobrevivem a este node.
	/// </summary>
	private void SoltarOsAvulsos()
	{
		Aquecimento.SemEnsaioDaGotaDeTeste = false;
		Aquecimento.SemEnsaioDaNevoaDeTeste = false;
		Aquecimento.SemEnsaioDaNebulosaDeTeste = false;
		Aquecimento.SemEnsaioDoEmbateDeTeste = false;
		Aquecimento.SemEnsaioDoPlanetaDeTeste = false;
		ApagarALuzAvulsa();
		if (!_ouvindoOEmbate) return;
		_ouvindoOEmbate = false;
		if (C is { } cli) cli.ClashComecou -= AoComecarOEmbateAvulso;
	}

	// =====================================================================
	// AS CONTAS
	// =====================================================================
	private string LinhaDoAvulso(int q) =>
		$"script {ScriptMs(q) - ColetorNoScript(q),5:0.0} | preparo {Partido(q + 1).Preparo,5:0.0} | tela {Partido(q + 2).Tela,5:0.0} | quadro inteiro {TotalLiquido(q),5:0.0} ms"
		+ $" | pipelines 2D +{_quadros[q + 1].Canvas - _quadros[q].Canvas}"
		+ (ColetorNoQuadro(q) > 0.05 ? $" | coletor {ColetorNoQuadro(q):0.0}" : "");

	/// <summary>
	/// O FECHO DA RODADA (quem chama e o <see cref="Relatar"/>): as pernas que sao so dela -- o arquivo ja na memoria, o
	/// item na tela, o controle do estouro no espaco e a rodada ter chegado em cada quadro -- e a tabela, com o quadro
	/// de cada uso e o seguinte partidos. (A perna das pipelines e do desenho ja saiu com os primeiros desenhos, e a do
	/// tempo com os eventos.)
	/// </summary>
	private void RelatarOsAvulsos()
	{
		if (_raiosComLuz) Nota("`--raioscomluz` junto: a luz dela acende antes da primeira passada, e os usos \"sem luz\" desta corrida sairam iluminados");

		// ---- O ARQUIVO JA NA MEMORIA: os quatro shaders que o jogo lia do disco na hora do primeiro uso ----
		foreach (Avulso qual in new[] { Avulso.Gota, Avulso.Estouro, Avulso.Nevoa, Avulso.Planeta })
		{
			string caminho = ShaderDoAvulso(qual);
			bool tinha = _avulsoNaMemoria.GetValueOrDefault(caminho);
			string oque = $"o `{caminho.GetFile()}` ja estava na memoria antes do primeiro gesto da rodada -- veio do lobby, numa thread, e a thread principal nao le o arquivo no meio do jogo";
			string achado = tinha ? "`ResourceLoader.HasCached` disse SIM" : "`ResourceLoader.HasCached` disse NAO: quem le o arquivo e o primeiro uso";
			string? defeito = DefeitoQueReprova(AtoDoAvulso(qual));
			if (_semEnsaio != null) Nota($"{oque}: {achado} (nesta rodada o aquecimento nao e o do jogo)");
			else if (defeito != null) Conferir(!tinha, $"(defeito injetado: {defeito}) a mesma regua REPROVA: {oque} ({achado})");
			else Conferir(tinha, $"{oque} ({achado})");
		}
		if (_carregadosPraOSal.Count > 0)
			Nota($"depois do fato de partida a bancada carregou {_carregadosPraOSal.Count} shader(s) pra o sal do driver frio entrar ({string.Join(", ", _carregadosPraOSal.Select(s => s.ResourcePath.GetFile()))}): a leitura deles nao esta no quadro medido");

		// ---- A RODADA CHEGOU EM CADA QUADRO: quem nao chegou num gesto fecharia sem ter cobrado nada dele ----
		foreach ((Avulso qual, bool comLuz) in new[]
				 {
					 (Avulso.Gota, false), (Avulso.Estouro, false), (Avulso.Nebulosa, false), (Avulso.Nevoa, false),
					 (Avulso.Gota, true), (Avulso.Estouro, true), (Avulso.Nebulosa, true), (Avulso.Nevoa, true),
					 (Avulso.Embate, false), (Avulso.Planeta, false), (Avulso.Planeta, true), (Avulso.EstouroNoEspaco, true),
				 })
			if (!_usosAvulsos.Exists(u => u.Qual == qual && u.ComLuz == comLuz))
				Conferir(false, $"a rodada chegou no quadro de: {NomeDoAvulso(qual)}{(comLuz ? ", com luz" : ", sem luz")}");

		// ---- O ITEM NA TELA ----
		var fora = _usosAvulsos.Where(u => !u.NaTela).Select(u => $"{u.Qual}{(u.ComLuz ? " com luz" : "")} ({u.Nota})").ToList();
		Conferir(fora.Count == 0, $"o item de cada um dos {_usosAvulsos.Count} usos estava NA TELA no quadro dele -- fora dela, ou invisivel, o motor nao o desenha, e a regua passaria sem provar nada"
								  + (fora.Count > 0 ? $" ({fora.Count} fora: {string.Join("; ", fora)})" : ""));

		if (_planetaVisto)
			Nota((_noiteNoEspaco < 0 ? "a rodada nao chegou no ultimo gesto do espaco, e a escuridao de la nao foi lida"
					 : $"no espaco, com a zona assentada, a escuridao vale {_escuridaoNoEspaco:0.00} e a forca da noite {_noiteNoEspaco:0.00} (`Iluminacao.ForcaDaNoite`): "
					   + (_noiteNoEspaco > 0.01f ? "HA luz de efeito la (a aura de uma forma, um tiro de ki), e ela alcanca o disco de um planeta que esteja perto" : "nao ha luz de efeito la"))
				 + $" | quando o planeta apareceu a cobertura de carregamento {(_coberturaNoPlaneta ? "ESTAVA no ar" : "nao estava no ar")}");

		Nota("OS AVULSOS, QUADRO A QUADRO -- o SCRIPT, e o desenho partido em PREPARO (os recursos sujos do quadro: e onde a thread principal espera um shader"
			 + " que ainda esta compilando) e TELA (o desenho da janela: e onde a pipeline e montada); `q0` e o quadro do uso, `q+1` o seguinte:");
		foreach ((Avulso qual, bool comLuz, int q, bool antes, bool naTela, string nota) in _usosAvulsos)
		{
			if (q + 4 >= _quadros.Count) { Conferir(false, $"{NomeDoAvulso(qual)}: a corrida gravou os 4 quadros seguintes"); continue; }
			ulong nascidas = _quadros[q + 2].Canvas - _quadros[q].Canvas;
			double desenho = Math.Max(DesenhoMs(q + 1), DesenhoMs(q + 2));

			// O CONTROLE: o estouro do planeta no espaco e o mesmo quad do estouro no mundo. Vale com o conserto e sem ele.
			if (qual == Avulso.EstouroNoEspaco)
				Conferir(nascidas == 0, "o estouro do planeta no ESPACO usa as mesmas pipelines do estouro no MUNDO (os gestos 2 ja as montaram, ou o ensaio): nenhuma nasce no quadro dele"
										+ $" ({nascidas} nascida(s) nele e no seguinte; o desenho custou {desenho:0.0} ms)");

			_linhas.Add($"           AVULSO {qual}{(comLuz ? " COM LUZ" : " sem luz")}: {nascidas} pipeline(s) 2D nele e no seguinte; desenho {desenho:0.0} ms; quadro inteiro {Math.Max(TotalLiquido(q), TotalLiquido(q + 1)):0.0} ms"
						+ $" | shader na memoria antes? {(antes ? "sim" : "NAO")} | item na tela? {(naTela ? "sim" : "NAO")}{(nota.Length > 0 ? $" ({nota})" : "")}");
			_linhas.Add("               q0   " + LinhaDoAvulso(q));
			_linhas.Add("               q+1  " + LinhaDoAvulso(q + 1));
		}
	}
}
