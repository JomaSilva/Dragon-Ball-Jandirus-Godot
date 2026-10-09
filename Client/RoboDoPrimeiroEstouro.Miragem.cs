using Godot;

namespace Jandirus.Client;

/// <summary>
/// A PRIMEIRA MIRAGEM DO PROCESSO -- a peca do vulto do Zanzoken na rodada dos "outros primeiros usos" (`--diagestouro
/// --pecas`), a regua dela, a medicao em pecas (`--miragempartida`), o Zanzoken pedido ao servidor (`--zanzodeverdade`) e
/// o rotulo da miragem no gravador sozinho (`--passivo`). A historia e os numeros estao no cabecalho do
/// `RoboDoPrimeiroEstouro`, no bloco "E A PRIMEIRA MIRAGEM"; o desenho partido com o atraso certo, a linha da tabela e o
/// ensaio ato a ato sao os do borrao (o arquivo `RoboDoPrimeiroEstouro.Borrao.cs`). Aqui fica o que e so desta regua.
///
/// ============================ O DEFEITO ============================
/// O `Zanzoken` carregava o shader da miragem na hora do primeiro uso, e ninguem o trazia antes: a primeira miragem do
/// processo -- o primeiro Zanzoken, ou o primeiro arranque de quem tem a `Afterimage Technique` -- lia o
/// `Zanzoken.gdshader` do disco pela thread principal, criava os primeiros materiais dele (um por camada do corpo) e os
/// desenhava no MESMO quadro. E o feitio do primeiro borrao, num efeito que so quem tem a skill dispara.
///
/// ============================ O QUE SE MEDE ============================
/// O QUADRO EM QUE A MIRAGEM NASCE, partido em tres como o do borrao: o SCRIPT, o PREPARO do desenho (onde a thread
/// principal espera um shader que ainda esta compilando) e a TELA (onde a pipeline e montada).
///
/// ============================ A REGUA TEM TRES PERNAS ============================
///   1. O ARQUIVO JA ESTAVA NA MEMORIA antes do primeiro gesto -- um fato perguntado ao proprio Godot
///      (`ResourceLoader.HasCached`), e nao um tempo: ler este shader custa 0,8 ms, abaixo de qualquer teto de quadro;
///   2. NENHUMA PIPELINE 2D NASCE no quadro da primeira miragem, nem no seguinte -- contagem do proprio motor, que nao
///      depende do cache de ninguem. E a perna que guarda os DOIS lados do palco do ensaio: de dia a miragem usa a
///      pipeline do sprite sem luz; com uma luz em cima (`--raioscomluz`), a gemea iluminada -- a que mais custa ao
///      driver de video na primeira vez;
///   3. O QUADRO CUSTA UM QUADRO NORMAL -- o teto de trabalho de todo efeito desta bancada. E a perna que o jogador
///      sente, e a unica que depende de cache: so morde com o cache de shader do Godot vazio (a thread principal espera
///      a compilacao) ou com o driver de video frio (a pipeline e montada do zero). Com os dois quentes o defeito
///      inteiro sao poucos milissegundos, abaixo do teto, e o quadro sai como numero.
///
/// O DEFEITO QUE ELA GUARDA e o `Aquecimento.SemEnsaioDaMiragemDeTeste` (`--pecas --semensaiodamiragem`): o aquecimento
/// sem o shader na fila e sem o ato do ensaio, o jogo de antes. As tres pernas reprovam.
///
/// ============================ A PECA TEM DUAS PORTAS ============================
///   * A MIRAGEM SOZINHA (a rodada do `.bat`): a chamada que o `World.AoPiscar` faz pra miragem
///     (`LocalPlayer.DeixarVulto`), e mais nada no quadro -- e o custo DELA;
///   * O ZANZOKEN DE VERDADE (`--zanzodeverdade`, com `--skillteste /datum/skill/ki/Afterimage` na linha): o pedido do
///     duplo clique no chao vai ao servidor, e a miragem nasce quando o anuncio volta -- com o borrao do salto e o som
///     do teleporte no mesmo quadro, que e como o jogador a ve.
/// </summary>
public partial class RoboDoPrimeiroEstouro
{
	/// <summary>
	/// `--pecas --semensaiodamiragem`: A RODADA DE INJECAO da miragem -- o processo entra no mundo com o defeito
	/// `Aquecimento.SemEnsaioDaMiragemDeTeste` (ligado pelo `Boot` na linha em que o aquecimento nasce): o jogo de antes,
	/// em que o shader da miragem nao vinha do lobby nem era ensaiado. As tres pernas da regua tem que REPROVAR.
	/// </summary>
	private readonly bool _semEnsaioDaMiragem = Tem("--semensaiodamiragem");

	private const string DefeitoDaMiragem = "o aquecimento sem o shader e sem o ensaio da miragem";

	/// <summary>`--pecas --miragempartida`: A MEDICAO EM PECAS, sem regua. Ver <see cref="AndarNaMiragem"/>.</summary>
	private readonly bool _miragemPartida = Tem("--miragempartida");

	/// <summary>
	/// `--pecas --zanzodeverdade`: A MIRAGEM PELO JOGO INTEIRO. Em vez da chamada direta, a peca pede um Zanzoken ao
	/// SERVIDOR -- as duas linhas do `World` quando o duplo clique cai no chao: `LocalPlayer.MarcarSaida` e
	/// `GameClient.SendZanzoken` --, e a miragem nasce quando o anuncio volta (`S2C.Zanzo` -> `World.AoPiscar`), com o que
	/// o jogo poe no mesmo quadro: o borrao do salto e o som do teleporte. O corpo precisa saber a tecnica, e a linha da
	/// rodada leva `--skillteste /datum/skill/ki/Afterimage`. Ver <see cref="PedirUmZanzoken"/>.
	/// </summary>
	private readonly bool _zanzoDeVerdade = Tem("--zanzodeverdade");

	/// <summary>
	/// `--driverfrio Zanzoken`: o driver de video nunca viu o shader da miragem desta corrida. Quem salga, e quem diz no
	/// relatorio se o sal entrou, e o arquivo `RoboDoPrimeiroEstouro.Raios.cs` (<see cref="SalgarOsShaders"/>); aqui so
	/// se pergunta se a miragem esta na lista dele.
	/// </summary>
	private bool MiragemNoDriverFrio => Array.IndexOf(_shadersFrios, Zanzoken.CaminhoDoShader) >= 0;

	/// <summary>O que esta rodada tem de proprio da miragem, pra nota "rodada:" do relatorio.</summary>
	private string RotuloDaMiragem() =>
		(_semEnsaioDaMiragem ? $" | DEFEITO INJETADO: {DefeitoDaMiragem} (`--semensaiodamiragem`)" : "")
		+ (_miragemPartida ? " | a primeira miragem em pecas (`--miragempartida`)" : "")
		+ (_zanzoDeVerdade && _pecas ? " | a miragem pelo Zanzoken pedido ao servidor (`--zanzodeverdade`)" : "");

	// =====================================================================
	// O ROTEIRO DA PECA -- um gesto por segundo, depois do borrao
	// =====================================================================
	private List<Action<World, GameClient>>? _gestosDaMiragem;
	private int _gestoDaMiragem, _miragensPedidas;

	/// <summary>
	/// O FATO DE PARTIDA: o Godot tinha o shader da miragem no cache ANTES de a bancada encostar nele? Perguntado uma vez
	/// so (`ResourceLoader.HasCached`): no primeiro gesto da peca e, no gravador sozinho, quando o corpo entra no mundo.
	/// </summary>
	private bool _miragemNaMemoria, _olheiAMemoriaDaMiragem;

	/// <summary>A bancada carregou o shader ela mesma, pra o sal do driver frio ter o que salgar. Ver <see cref="PrepararAMiragem"/>.</summary>
	private bool _carregueiAMiragemPraOSal;

	/// <summary>Os eventos desta peca, na ordem: o indice em <see cref="_eventos"/> e um nome curto pra tabela.</summary>
	private readonly List<(int Evento, string Curto)> _eventosDaMiragem = [];
	private int _eventoDaPrimeiraMiragem = -1;

	/// <summary>O palco da medicao em pecas: nasce FORA da arvore com a miragem dentro, e entra nela um segundo depois.</summary>
	private Node2D? _palcoDaMiragemPartida;

	/// <summary>
	/// O Zanzoken pedido ao servidor ainda nao voltou: a peca espera o quadro em que a conta de miragens do jogo anda
	/// (<see cref="NoFimDoQuadroDaMiragem"/>). O numero e a conta no instante do pedido.
	/// </summary>
	private bool _esperandoOZanzoken;
	private int _miragensAntesDoZanzoken;

	/// <summary>
	/// A PRIMEIRA MIRAGEM DO PROCESSO, SOZINHA NUM QUADRO -- e a segunda, um segundo depois, que e o controle (nada nela e
	/// novo). Chamado pelo <see cref="AndarNasPecas"/> uma vez por segundo, depois do borrao; devolve falso quando o
	/// roteiro acabou.
	///
	/// PELA PORTA DE PRODUCAO: `LocalPlayer.DeixarVulto` no corpo local -- a chamada que o `World.AoPiscar` faz quando o
	/// anuncio de um salto chega com `vulto` (o Zanzoken, ou o arranque de quem tem a Afterimage) --, logo depois de um
	/// `MarcarSaida`, que e o que o cliente faz no instante do gesto: a miragem nasce onde o corpo esta, no meio da tela.
	/// NAO PASSA PELO SERVIDOR de proposito: o salto de verdade traz no mesmo quadro o borrao do arranque, o som do
	/// teleporte e, quase sempre, um golpe, e a peca quer o custo da miragem sozinho. Quem quer o jogo inteiro roda a
	/// peca com `--zanzodeverdade` (<see cref="PedirUmZanzoken"/>), ou poe o gravador ao lado de uma luta
	/// (`--trailer duelo --diagestouro --passivo 100`: a miragem so aparece la quando o sorteio da a tecnica a um dos dois).
	///
	/// COM `--miragempartida` VEM ANTES, CADA UMA NUM QUADRO SO DELA, AS TRES PECAS DO PRIMEIRO USO -- o arquivo, os
	/// primeiros materiais e o primeiro desenho --, e a miragem pela porta do jogo ja nao tem nada de novo. Nessa rodada
	/// nada e cobrado: os tres custos ja foram pagos em jogo, pela propria bancada.
	/// </summary>
	private bool AndarNaMiragem(World mundo, GameClient cli)
	{
		if (_gestosDaMiragem == null)
		{
			_gestosDaMiragem = [];
			if (_miragemPartida)
			{
				_gestosDaMiragem.Add(PecaDoArquivoDaMiragem);
				_gestosDaMiragem.Add(PecaDosMateriaisDaMiragem);
				_gestosDaMiragem.Add(PecaDoDesenhoDaMiragem);
			}
			_gestosDaMiragem.Add(PedirUmaMiragem);
			_gestosDaMiragem.Add(PedirUmaMiragem);
			_gestosDaMiragem.Add(static (_, _) => { });   // os ultimos quadros da segunda miragem
			PrepararAMiragem();
			return true;
		}

		if (_esperandoOZanzoken)
		{
			Conferir(false, $"o Zanzoken {_miragensPedidas} foi atendido em um segundo: o anuncio do servidor voltou e a miragem nasceu"
							+ " (o corpo sabe a tecnica? a linha da rodada precisa de `--skillteste /datum/skill/ki/Afterimage`)");
			return false;
		}
		if (_gestoDaMiragem >= _gestosDaMiragem.Count) return false;
		_gestosDaMiragem[_gestoDaMiragem++](mundo, cli);
		return true;
	}

	/// <summary>
	/// O PRIMEIRO GESTO E SO O PREPARO, um segundo antes de qualquer coisa da miragem: o fato de partida (o shader esta na
	/// memoria?) e, com `--verbose` na linha, o ouvido das cargas.
	///
	/// E O CASO QUE SO A BANCADA TEM: `--driverfrio Zanzoken` numa rodada em que o shader NAO veio do lobby. O sal entra
	/// num shader ja carregado, e ai nao ha nenhum -- sem o aquecimento ele so e carregado no quadro da primeira miragem,
	/// e o sal chegaria depois dos primeiros materiais. A bancada o carrega aqui, pela porta de producao: a leitura do
	/// arquivo sai do quadro medido (quem a cobra e a perna do fato, que ja foi anotada), e o que fica nele e o que a
	/// rodada veio medir -- o shader compilando e a pipeline montada do zero.
	/// </summary>
	private void PrepararAMiragem()
	{
		LigarAEscutaDeCarga();

		_miragemNaMemoria = ResourceLoader.HasCached(Zanzoken.CaminhoDoShader);
		_olheiAMemoriaDaMiragem = true;

		if (MiragemNoDriverFrio && !_miragemNaMemoria && !_miragemPartida)
		{
			_ = Zanzoken.Sh;
			_carregueiAMiragemPraOSal = true;
			Marcar("`--driverfrio Zanzoken` sem o shader na memoria: a bancada o carrega aqui, pra o sal entrar antes da primeira miragem");
		}
	}

	private void PedirUmaMiragem(World mundo, GameClient cli)
	{
		if (mundo.CorpoDeTeste(cli.LocalId) is not LocalPlayer local || local.GetParent() is not { } palco)
		{
			Conferir(false, "o corpo local esta no mundo pra a peca da miragem");
			_gestoDaMiragem = int.MaxValue;
			return;
		}

		_miragensPedidas++;
		if (_zanzoDeVerdade) { PedirUmZanzoken(local, cli); return; }

		int antes = Zanzoken.MiragensDeTeste;
		local.MarcarSaida();
		local.DeixarVulto(local.GlobalPosition);
		if (Zanzoken.MiragensDeTeste == antes)
		{
			Conferir(false, $"a miragem {_miragensPedidas} nasceu (`LocalPlayer.DeixarVulto` -> `Zanzoken.Deixar`)");
			_gestoDaMiragem = int.MaxValue;
			return;
		}
		AnotarAMiragemDaPeca(CamadasDaUltimaMiragem(palco));
	}

	/// <summary>
	/// Quantos tiles o Zanzoken da peca pede. O servidor encurta o salto pro alcance da tecnica (`zanzorange`) se o pedido
	/// passar dele, e recusa calado o que nao pode ser atendido (sem Ki, parede no caminho): ai a peca reprova por espera.
	/// </summary>
	private const int TilesDoZanzoken = 3;

	/// <summary>
	/// O ZANZOKEN DE VERDADE (`--zanzodeverdade`): o pedido do duplo clique no chao, como o `World` o manda -- o ponto de
	/// partida guardado no cliente (`MarcarSaida`) e o destino no pacote (`SendZanzoken`). Quem decide e o servidor
	/// (`GameServer.Zanzoken`: a skill, o Ki, o alcance, a parede), e a miragem nasce no quadro em que o anuncio dele
	/// chega. PRA OESTE NA PRIMEIRA E DE VOLTA NA SEGUNDA: o alvo das pecas de ki esta a leste do corpo.
	/// </summary>
	private void PedirUmZanzoken(LocalPlayer local, GameClient cli)
	{
		float lado = _miragensPedidas % 2 == 1 ? -1f : 1f;
		Vector2 destino = local.GlobalPosition + new Vector2(lado * TilesDoZanzoken * Jandirus.Core.World.ZoneCollision.TileSize, 0);

		_miragensAntesDoZanzoken = Zanzoken.MiragensDeTeste;
		_esperandoOZanzoken = true;
		local.MarcarSaida();
		cli.SendZanzoken(new Jandirus.Core.World.Vec2(destino.X, destino.Y));
		Marcar($"o Zanzoken {_miragensPedidas} PEDIDO ao servidor (`LocalPlayer.MarcarSaida` + `GameClient.SendZanzoken`, {TilesDoZanzoken} tiles a {(lado < 0 ? "oeste" : "leste")})");
	}

	/// <summary>As camadas da miragem que acaba de nascer neste palco: os sprites da foto dela. Zero se nao a achou.</summary>
	private static int CamadasDaUltimaMiragem(Node palco)
	{
		for (int i = palco.GetChildCount() - 1; i >= 0; i--)
			if (palco.GetChild(i) is Zanzoken vulto)
				return vulto.GetChildCount() > 0 ? vulto.GetChild(0).GetChildCount() : 0;
		return 0;
	}

	// =====================================================================
	// AS PECAS DO PRIMEIRO USO, UMA POR QUADRO (`--miragempartida`)
	// =====================================================================
	private void PecaDoArquivoDaMiragem(World mundo, GameClient cli)
	{
		ulong t0 = Time.GetTicksUsec();
		_ = Zanzoken.Sh;
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;
		Evento($"PECA DA MIRAGEM 1 de 3 -- ARQUIVO: o shader pedido pela porta de producao (`Zanzoken.Sh`; a chamada: {ms:0.00} ms;"
			   + $" {(_miragemNaMemoria ? "ja estava na memoria" : "LIDO DO DISCO aqui")})", Papel.Nota);
		_eventosDaMiragem.Add((_eventos.Count - 1, "PECA 1, o arquivo"));
	}

	/// <summary>
	/// A MIRAGEM INTEIRA, pela chamada de producao (`Zanzoken.Deixar`), num palco FORA da arvore: a foto do corpo e um
	/// material do shader por camada nascem aqui, e nada e desenhado. E o que separa a espera pelo shader (que cai no
	/// quadro em que o primeiro material nasce) da pipeline (que so nasce no primeiro desenho).
	/// </summary>
	private void PecaDosMateriaisDaMiragem(World mundo, GameClient cli)
	{
		if (mundo.CorpoDeTeste(cli.LocalId) is not LocalPlayer local)
		{
			Conferir(false, "o corpo local esta no mundo pra a medicao em pecas da miragem");
			_gestoDaMiragem = int.MaxValue;
			return;
		}

		_palcoDaMiragemPartida = new Node2D { Name = "PalcoDaMiragemPartida" };
		ulong t0 = Time.GetTicksUsec();
		Zanzoken.Deixar(_palcoDaMiragemPartida, local, local.GlobalPosition + new Vector2(-64, 0));
		double ms = (Time.GetTicksUsec() - t0) / 1000.0;
		Evento($"PECA DA MIRAGEM 2 de 3 -- MATERIAIS: a miragem inteira montada pela chamada de producao (`Zanzoken.Deixar`) num palco FORA da"
			   + $" arvore -- a foto do corpo, {CamadasDaUltimaMiragem(_palcoDaMiragemPartida)} camada(s), e um material do shader em cada uma; nada e desenhado; sem o ensaio"
			   + $" do lobby sao os PRIMEIROS do processo, e o Godot comeca a compilar; a chamada: {ms:0.00} ms)", Papel.Nota);
		_eventosDaMiragem.Add((_eventos.Count - 1, "PECA 2, os materiais"));
	}

	private void PecaDoDesenhoDaMiragem(World mundo, GameClient cli)
	{
		if (_palcoDaMiragemPartida == null || _palco == null) return;
		_palco.AddChild(_palcoDaMiragemPartida);
		Evento("PECA DA MIRAGEM 3 de 3 -- A MIRAGEM NA TELA: o mesmo palco entra na arvore um segundo depois de os materiais nascerem (o DESENHO)", Papel.Nota);
		_eventosDaMiragem.Add((_eventos.Count - 1, "PECA 3, o desenho"));
	}

	// =====================================================================
	// O FIM DE CADA QUADRO: a miragem que nasce por um pacote do servidor
	// =====================================================================
	/// <summary>Quantos nascimentos de miragem o gravador sozinho rotula e parte: o primeiro que ele ve e tres de controle.</summary>
	private const int MiragensDoPassivo = 4;

	private readonly List<(int Quadro, int Quantas)> _miragensDoPassivo = [];
	private int _miragensVistas, _miragensNascidas;

	/// <summary>
	/// No fim de cada quadro, depois de todos os scripts (quem chama e o <see cref="NoFimDoQuadro"/>): a conta de
	/// miragens do jogo andou neste quadro? Entao uma nasceu nele.
	///
	///   * NA PECA COM O ZANZOKEN DE VERDADE e o quadro do evento: o anuncio do servidor chega quando chega, e a peca
	///     nao escolhe o quadro;
	///   * NO GRAVADOR SOZINHO o quadro ganha rotulo -- os quatro primeiros da cena que ele assiste.
	/// (A miragem do ensaio do lobby nao entra na conta: ver `Zanzoken.MiragensDeTeste`.)
	/// </summary>
	private void NoFimDoQuadroDaMiragem()
	{
		if (_esperandoOZanzoken && Zanzoken.MiragensDeTeste != _miragensAntesDoZanzoken)
		{
			_esperandoOZanzoken = false;
			Node? palco = World.Instancia is { } mundo && C is { } cli ? mundo.CorpoDeTeste(cli.LocalId)?.GetParent() : null;
			AnotarAMiragemDaPeca(palco != null ? CamadasDaUltimaMiragem(palco) : 0);
		}

		if (_passivo <= 0) return;

		if (!_olheiAMemoriaDaMiragem && World.Instancia?.PosicaoLocal != null)
		{
			_olheiAMemoriaDaMiragem = true;
			_miragemNaMemoria = ResourceLoader.HasCached(Zanzoken.CaminhoDoShader);
		}

		int agora = Zanzoken.MiragensDeTeste;
		int novas = agora - _miragensVistas;
		_miragensVistas = agora;
		if (novas <= 0) return;

		_miragensNascidas += novas;
		if (_miragensDoPassivo.Count >= MiragensDoPassivo) return;
		_miragensDoPassivo.Add((_quadros.Count, novas));
		Marcar($"MIRAGEM {_miragensDoPassivo.Count}: {novas} vulto(s) do Zanzoken nasce(m)");
	}

	/// <summary>O defeito injetado sai -- e o campo e ESTATICO e de producao --, e o palco da medicao em pecas tambem, se nunca chegou a arvore.</summary>
	private void SoltarAMiragem()
	{
		Aquecimento.SemEnsaioDaMiragemDeTeste = false;
		if (_palcoDaMiragemPartida is { } palco && IsInstanceValid(palco) && !palco.IsInsideTree()) palco.Free();
		_palcoDaMiragemPartida = null;
	}

	/// <summary>
	/// UMA MIRAGEM PEDIDA PELA PECA NASCEU NESTE QUADRO: e ele o evento. (Pela chamada direta o vulto nasce dentro dela,
	/// ao contrario do rastro, que so resolve o salto no `_Process` dele; pelo Zanzoken de verdade, no quadro em que o
	/// anuncio do servidor chega.)
	///
	/// O PAPEL: a primeira e a regua, e a segunda o controle dela. Na rodada de injecao a primeira tem que REPROVAR -- mas
	/// a perna do TEMPO so reprova quando ha o que esperar: com o cache de shader do Godot vazio (o de toda bancada) a
	/// thread principal espera a compilacao, e com o driver frio a pipeline e montada do zero. Com os dois quentes o
	/// defeito inteiro sao poucos milissegundos, e o quadro sai como numero; quem reprova ai sao as duas pernas de fato
	/// (<see cref="ConferirAPrimeiraMiragem"/>). Nas rodadas sem ensaio nenhum (`--semensaio`, `--semaquecimento`) e na
	/// medicao em pecas, tudo sai como numero.
	/// </summary>
	private void AnotarAMiragemDaPeca(int camadas)
	{
		bool primeira = _miragensPedidas == 1;

		Papel papel;
		string defeito = "";
		if (_miragemPartida || _semEnsaio != null) papel = Papel.Nota;
		else if (!primeira || !_semEnsaioDaMiragem) papel = Papel.Regua;
		else if (_shadersEmDiscoNoComeco == 0 || MiragemNoDriverFrio) { papel = Papel.Injetado; defeito = DefeitoDaMiragem; }
		else papel = Papel.Nota;

		string porta = _zanzoDeVerdade
			? "`GameClient.SendZanzoken` -> o servidor -> `S2C.Zanzo` -> `World.AoPiscar`: o borrao do salto, o som do teleporte e a miragem"
			: "`LocalPlayer.DeixarVulto` -> `Zanzoken.Deixar`: a foto do corpo";
		string oque = !primeira ? $"a miragem {_miragensPedidas} (o controle: nada e novo; {camadas} camada(s))"
			: _miragemPartida ? $"a miragem pela porta do jogo, depois das pecas ({porta}, {camadas} camada(s); o primeiro uso ja foi pago, em pecas)"
			: $"a PRIMEIRA MIRAGEM do processo ({porta}, {camadas} camada(s), e cada camada com um material do `Zanzoken.gdshader`)";
		Evento(oque, papel, defeito);
		_eventosDaMiragem.Add((_eventos.Count - 1, primeira ? "a PRIMEIRA miragem" : $"a miragem {_miragensPedidas} (o controle)"));
		if (primeira) _eventoDaPrimeiraMiragem = _eventos.Count - 1;
	}

	// =====================================================================
	// AS CONTAS
	// =====================================================================
	/// <summary>
	/// O FECHO DA PECA, no relatorio da rodada `--pecas` (quem chama e o <see cref="Relatar"/>, depois do fecho do borrao,
	/// que ja imprimiu o ensaio ato a ato): as duas pernas de fato da regua -- a terceira, a do tempo, ja saiu com os
	/// outros eventos -- e a tabela dos quadros.
	/// </summary>
	private void ConferirAPrimeiraMiragem()
	{
		if (_eventoDaPrimeiraMiragem < 0)
		{
			Conferir(false, "a rodada chegou na PRIMEIRA MIRAGEM do processo (o vulto do Zanzoken nasceu)");
			return;
		}
		int q = _eventos[_eventoDaPrimeiraMiragem].Quadro;
		if (q + 3 >= _quadros.Count) { Conferir(false, "a primeira miragem: a corrida gravou os tres quadros seguintes"); return; }

		ulong nascidas = _quadros[q + 2].Canvas - _quadros[q].Canvas;
		const string naMemoria = "o shader da PRIMEIRA MIRAGEM ja estava na memoria antes dela -- veio do lobby, numa thread, e a thread principal nao le o arquivo no meio da luta";
		const string semPipeline = "nenhuma pipeline 2D nasce no quadro da PRIMEIRA MIRAGEM, nem no seguinte -- o ensaio do lobby ja a desenhou";
		string achadoNaMemoria = (_miragemNaMemoria
									 ? "`ResourceLoader.HasCached` disse SIM antes do primeiro gesto"
									 : "`ResourceLoader.HasCached` disse NAO antes do primeiro gesto: quem le o arquivo e a primeira miragem")
								 + (_carregueiAMiragemPraOSal ? "; depois disso a bancada o carregou, pra o sal do driver frio entrar -- a leitura nao esta no quadro medido" : "");
		string achadoDaPipeline = $"{nascidas} nascida(s); o preparo do desenho dele custou {PreparoNoBorrao(q):0.0} ms e a tela {TelaNoBorrao(q):0.0}";

		if (_miragemPartida || _semEnsaio != null)
		{
			string porque = _miragemPartida ? "nesta rodada a propria bancada paga antes, em pecas, o primeiro uso" : "nesta rodada o aquecimento nao e o do jogo";
			Nota($"{naMemoria}: {achadoNaMemoria} ({porque})");
			Nota($"{semPipeline}: {achadoDaPipeline} ({porque})");
		}
		else if (_semEnsaioDaMiragem)
		{
			Conferir(!_miragemNaMemoria, $"(defeito injetado: {DefeitoDaMiragem}) a mesma regua REPROVA: {naMemoria} ({achadoNaMemoria})");
			Conferir(nascidas > 0, $"(defeito injetado: {DefeitoDaMiragem}) a mesma regua REPROVA: {semPipeline} ({achadoDaPipeline})");
		}
		else
		{
			Conferir(_miragemNaMemoria, $"{naMemoria} ({achadoNaMemoria})");
			Conferir(nascidas == 0, $"{semPipeline} ({achadoDaPipeline})");
		}

		Nota("A MIRAGEM, QUADRO A QUADRO -- o SCRIPT, e o desenho partido em PREPARO (os recursos sujos do quadro: e onde a thread principal espera um shader"
			 + " que ainda esta compilando) e TELA (o desenho da janela: e onde a pipeline e montada):");
		foreach ((int evento, string curto) in _eventosDaMiragem)
		{
			int qe = _eventos[evento].Quadro;
			if (qe + 3 >= _quadros.Count) continue;
			_linhas.Add($"           {curto,-28} " + LinhaPartidaDoBorrao(qe));
		}
	}

	/// <summary>O fecho do gravador sozinho: as primeiras miragens da cena que ele assistiu, cada uma com o quadro partido.</summary>
	private void ImprimirAMiragemDoPassivo()
	{
		GD.Print($"[estouro] A MIRAGEM DO ZANZOKEN: {_miragensNascidas} vulto(s) nasceram em jogo (cada Zanzoken, e cada arranque de quem tem a Afterimage)"
				 + $" | o shader dela estava na memoria quando o corpo entrou no mundo? {(!_olheiAMemoriaDaMiragem ? "(o corpo nao entrou)" : _miragemNaMemoria ? "SIM" : "NAO")}");
		if (_semEnsaioDaMiragem) GD.Print($"[estouro] DEFEITO INJETADO a gravacao inteira: {DefeitoDaMiragem} (`--semensaiodamiragem`)");

		int de = Math.Max(1, _entrouNoMundo);
		for (int n = 0; n < _miragensDoPassivo.Count; n++)
		{
			(int q, int quantas) = _miragensDoPassivo[n];
			if (q + 3 >= _quadros.Count) continue;
			GD.Print($"[estouro]    {(n == 0 ? "a PRIMEIRA" : $"a {n + 1}a        ")}  t={SegundosDesde(de, q),7:0.000}s  {quantas} vulto(s)  " + LinhaPartidaDoBorrao(q));
		}
	}
}
