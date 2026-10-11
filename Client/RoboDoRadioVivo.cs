using Concentus;
using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Social;
using Jandirus.Net;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// ============================ BANCADA DO RADIO DO SCOUTER PELO FIO (`--radiovivo`) ============================
/// A `--radioteste` prova o corte com corpos forjados e a `--diagradio` prova o efeito sem rede. Entre as duas
/// fica o que nenhuma alcanca: os tres pacotes novos atravessando o fio de verdade (UDP em 127.0.0.1, o
/// despacho de producao das duas pontas), o menu que troca a frequencia e as teclas.
///
/// Roda com `--host`: servidor e cliente no mesmo processo, ligados pelo fio. O robo joga como o anfitriao
/// e conversa com um SEGUNDO corpo forjado no servidor (com scouter), colado nele:
///
///   A. O ESTADO   -- o `S2C.Radio` chega no login; o menu P (aba Equip) mostra a frequencia e a troca pelo
///                    teclado numerico; o servidor recusa numero fora da faixa.
///   B. A DESCIDA  -- o outro transmite: os quadros chegam (`S2C.VozDeRadio`), tocam SO no ouvido esquerdo,
///                    a faixa diz o nome dele; NADA chega pela voz de perto, com ele a um tile de distancia;
///                    noutra frequencia nao chega byte nenhum; e a voz de perto dele nao vaza pro radio.
///   C. A SUBIDA   -- a tecla do radio de verdade (`Input.ActionPress`), com o microfone trocado por um tom:
///                    o servidor recebe pelo `C2S.VozDeRadio`; so a tecla do radio NAO fala de perto; as duas
///                    juntas falam pelos dois; sem scouter a tecla nao manda nada -- e o servidor recusa
///                    quem mande assim mesmo.
///
/// Cada afirmacao sobre o que NAO acontece tem o defeito injetado ao lado (`...DeTeste`).
///
///     Godot --headless --path . --host --rede 8101 --radiovivo --raca Human --conta bancada_radio_vivo --nome Operador
///
/// COM JANELA e `--pasta <dir>` ela tambem FOTOGRAFA quatro momentos (o cartao do scouter, o teclado da
/// frequencia, a faixa de quem ouve e a de quem transmite): a faixa e o sinal verde sao desenho, e desenho
/// se confere olhando.
/// ================================================================================================================
/// </summary>
public partial class RoboDoRadioVivo : Node
{
	private const int Frequencia = 42;
	private const string NomeDoOutro = "Radialista";

	private double _t, _relogioDoOutro;
	private int _passo, _esperas, _eu, _outro, _seqAntes;
	private bool _acabou;

	private readonly List<string> _falhas = [];
	private readonly List<string> _linhas = [];

	private static GameClient? C => GameClient.Instance;
	private static GameServer? S => GameServer.Instance;

	// ---- o que o outro corpo esta fazendo (o servidor recebe os quadros dele direto no funil) ----
	private enum Boca { Calada, NoRadio, DePerto }
	private Boca _bocaDoOutro = Boca.Calada;
	private ushort _seqDoOutro;
	private readonly List<byte[]> _tom = [];

	// ---- o que chegou em mim ----
	private int _pacotesDeRadio, _pacotesDePerto, _quadrosTocados, _quadrosComDireito, _estalos;
	private double _maiorEsquerdo;

	// ---- o que subiu de mim (contado NO SERVIDOR) ----
	private int _subiram;

	// ---- a onda no lugar do meu microfone ----
	private double _devidos;
	private int _faseDoTom;

	public override void _Ready()
	{
		// PENSA ANTES DO MICROFONE: a tecla desce neste quadro e o `Microfone._Process` ja a encontra apertada.
		ProcessPriority = -100;

		// 50 quadros de um tom de 1 kHz, codificados pelo codificador de producao: a "voz" do outro corpo.
		IOpusEncoder enc = Microfone.CriarCodificador();
		var pcm = new short[VozLocal.AmostrasPorQuadro];
		var buf = new byte[VozLocal.MaxBytesDeQuadro];
		for (int k = 0; k < 50; k++)
		{
			for (int i = 0; i < pcm.Length; i++)
				pcm[i] = (short)(0.4 * short.MaxValue * Math.Sin(2 * Math.PI * 1000 * (i + k * pcm.Length) / VozLocal.TaxaDeAmostragem));
			int n = enc.Encode(pcm, pcm.Length, buf, buf.Length);
			_tom.Add(buf[..n]);
		}

		RadioOuvido.Espiao = AoTocar;
		GameServer.EspiaoDoRadio = AoSubir;
		Microfone.FonteDeTeste = TomDoMicrofone;
		Boot.Config.VozLigada = true;
		Boot.Config.VozApertarParaFalar = true;
		if (C is { } cli)
		{
			cli.VozDeRadioRecebida += ContarRadio;
			cli.VozRecebida += ContarPerto;
		}
	}

	public override void _ExitTree() => Desmontar();

	private void Desmontar()
	{
		SoltarTudo();
		RadioOuvido.Espiao = null;
		GameServer.EspiaoDoRadio = null;
		Microfone.FonteDeTeste = null;
		EfeitoDeRadio.NosDoisOuvidosDeTeste = false;
		RadioDoScouter.RadioVazaPorPertoDeTeste = false;
		if (C is { } cli)
		{
			cli.VozDeRadioRecebida -= ContarRadio;
			cli.VozRecebida -= ContarPerto;
		}
		if (_outro != 0) S?.RemoverCorpoDeRadioDeTeste(_outro);
		_outro = 0;
	}

	private void ContarRadio(int quem, ushort seq, byte[] dados, int n) { if (quem == _outro) _pacotesDeRadio++; }
	private void ContarPerto(int quem, ushort seq, byte dist, bool parede, byte[] dados, int n) { if (quem == _outro) _pacotesDePerto++; }
	private void AoSubir(int de, int pra, int bytes) { if (de == _eu && pra == _outro) _subiram++; }

	private void AoTocar(int quem, Vector2[] estereo, int n, bool ehEstalo)
	{
		if (quem != _outro) return;
		double dir = 0, esq = 0;
		for (int i = 0; i < n; i++) { dir = Math.Max(dir, Math.Abs(estereo[i].Y)); esq = Math.Max(esq, Math.Abs(estereo[i].X)); }
		if (dir > 0) _quadrosComDireito++;
		if (ehEstalo) { _estalos++; return; }
		_quadrosTocados++;
		_maiorEsquerdo = Math.Max(_maiorEsquerdo, esq);
	}

	/// <summary>A onda no lugar do microfone: um quadro de 440 Hz a cada 20 ms de relogio. Quem decide se ela SAI e o `Microfone` de producao.</summary>
	private bool TomDoMicrofone(short[] quadro)
	{
		if (_devidos < 1) return false;
		_devidos -= 1;
		for (int i = 0; i < quadro.Length; i++)
			quadro[i] = (short)(0.5 * short.MaxValue * Math.Sin(2 * Math.PI * 440 * (_faseDoTom + i) / VozLocal.TaxaDeAmostragem));
		_faseDoTom += quadro.Length;
		return true;
	}

	/// <summary>Uma foto da tela -- so com janela e com `--pasta`. Ver o cabecalho.</summary>
	private void Foto(string nome)
	{
		if (DisplayServer.GetName() == "headless") return;
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, "--pasta");
		if (i < 0 || i + 1 >= a.Length) return;
		GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(a[i + 1], nome));
		_linhas.Add($"  foto     {nome}");
	}

	/// <summary>
	/// O SINAL DE UM CORPO QUE NUNCA VOOU FICA ACIMA DA CABECA. O outro corpo nasceu no chao e nao saiu dele:
	/// o sinal dele so esta no lugar se nascer la (a varredura do voo nunca rodou pra ele). A contraprova
	/// nasce um sinal com o defeito ligado e mostra onde ele ficava: na origem do corpo.
	/// </summary>
	private void ConferirOLugarDoSinal()
	{
		SinalDeVoz? dele = GetTree().Root.FindChildren("SinalDeVoz", "", true, false).OfType<SinalDeVoz>().FirstOrDefault(s => s.Id == _outro);
		Conferir(dele is { Position.Y: < -20 }, "**o sinal de quem nunca saiu do chao fica ACIMA da cabeca dele** (e nao em cima do rosto)",
			dele == null ? "sem sinal no corpo" : $"posicao {dele.Position}");

		SinalDeVoz.SemAlturaDeBaseDeTeste = true;
		try
		{
			var cobaia = new SinalDeVoz { Name = "SinalDeTeste" };
			AddChild(cobaia);
			Conferir(cobaia.Position == Vector2.Zero, "(defeito injetado: o sinal de antes) ele nasce na origem do corpo -- a regua sabe reprovar", $"{cobaia.Position}");
			cobaia.QueueFree();
		}
		finally { SinalDeVoz.SemAlturaDeBaseDeTeste = false; }
	}

	private static void Apertar(string acao) => Godot.Input.ActionPress(Teclas.NomeNoMotor(acao));
	private static void Soltar(string acao) => Godot.Input.ActionRelease(Teclas.NomeNoMotor(acao));
	private static void SoltarTudo() { Soltar("falar_radio"); Soltar("falar_voz"); }

	private void Conferir(bool ok, string oque, string detalhe = "")
	{
		_linhas.Add((ok ? "  ok   " : "  FALHA") + "  " + oque + (ok || detalhe.Length == 0 ? "" : $"   [{detalhe}]"));
		if (!ok) _falhas.Add(oque);
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private static Button? Botao(Node? raiz, string texto) =>
		raiz == null ? null : Todos(raiz).OfType<Button>().FirstOrDefault(b => b.Text == texto && b.IsVisibleInTree());

	/// <summary>A frequencia que a aba Equip ESCREVEU (o metadado `radio` da linha), ou -1 sem a linha na tela.</summary>
	private static int FrequenciaNaTela()
	{
		if (MenuJogo.Instancia?.PaginaDeTeste("Equip") is not { } pg) return -1;
		HBoxContainer? linha = Todos(pg).OfType<HBoxContainer>().FirstOrDefault(h => h.HasMeta("radio") && h.IsVisibleInTree());
		return linha == null ? -1 : linha.GetMeta("radio").AsInt32();
	}

	public override void _Process(double delta)
	{
		if (_acabou || C is not { Connected: true } cli || S is not { } srv) return;

		// O RELOGIO DAS DUAS BOCAS anda todo quadro, e nao so nos passos: 50 quadros de audio por segundo.
		_devidos = Math.Min(_devidos + delta * VozLocal.QuadrosPorSegundo, 5);
		if (_bocaDoOutro != Boca.Calada && _outro != 0)
		{
			_relogioDoOutro = Math.Min(_relogioDoOutro + delta * VozLocal.QuadrosPorSegundo, 5);
			while (_relogioDoOutro >= 1)
			{
				_relogioDoOutro -= 1;
				byte[] q = _tom[_seqDoOutro % _tom.Count];
				srv.TransmitirDeTeste(_outro, _seqDoOutro++, q, q.Length, _bocaDoOutro == Boca.NoRadio);
			}
		}

		_t += delta;
		if (_t < 0.4) return;
		_t = 0;

		try { Passo(cli, srv); }
		catch (Exception e)
		{
			Conferir(false, "o roteiro correu ate o fim sem lancar excecao", $"{e.GetType().Name}: {e.Message}");
			_passo = 1000;
		}
	}

	private void Passo(GameClient cli, GameServer srv)
	{
		MenuJogo? menu = MenuJogo.Instancia;
		RadioOuvido? radio = RadioOuvido.Instancia;

		switch (_passo++)
		{
			// ================================================================ A. O ESTADO
			case 0:
				if (cli.LocalId == 0 || cli.Atributos.Raca is not { Length: > 0 } || radio == null || menu == null)
				{ _passo = 0; if (++_esperas > 150) { Conferir(false, "o mundo chegou (conexao, ficha lenta, o no do radio e o menu)"); _passo = 1000; } return; }
				_eu = cli.LocalId;
				Conferir(cli.RadioChegouDeTeste && cli.FrequenciaDoRadio == RadioDoScouter.FrequenciaPadrao,
					$"**o `S2C.Radio` chega no login**, com a frequencia de fabrica ({RadioDoScouter.FrequenciaPadrao})",
					$"chegou={cli.RadioChegouDeTeste} freq={cli.FrequenciaDoRadio}");
				Conferir(!cli.Atributos.Tem(Protocol.Poder.Scouter), "(montagem) o personagem nasce SEM scouter");
				Apertar("falar_radio");   // sem scouter: nao pode sair nada
				break;

			case 1:
				Conferir(!Microfone.NoRadio && srv.UltimaSequenciaDoRadioDeTeste(_eu) == -1 && radio!.FaixaDeTeste == ""
						 && radio.EstalosPropriosDeTeste == 0,
					"**SEM SCOUTER a tecla do radio nao transmite**: nenhum quadro chega ao servidor, a faixa nao acende e o aparelho nao estala",
					$"noRadio={Microfone.NoRadio} seq={srv.UltimaSequenciaDoRadioDeTeste(_eu)} faixa='{radio!.FaixaDeTeste}' estalos={radio.EstalosPropriosDeTeste}");
				Soltar("falar_radio");
				Conferir(srv.ScouterNaMochilaDeTeste(_eu), "(montagem) um scouter na mochila");
				cli.SendVerbo("item_equipar", CatalogoDeItens.Scouter);
				_esperas = 0;
				break;

			case 2:
				if (!cli.Atributos.Tem(Protocol.Poder.Scouter) && ++_esperas < 20) { _passo = 2; return; }
				Conferir(cli.Atributos.Tem(Protocol.Poder.Scouter), "(montagem) o scouter liga pelo verbo de producao");
				menu!.Abrir();
				menu.IrPara("Equip");
				break;

			case 3:
			{
				Foto("radio-1-equip.png");
				Conferir(FrequenciaNaTela() == RadioDoScouter.FrequenciaPadrao,
					"a aba Equip mostra o RADIO no cartao do scouter, com a frequencia que o servidor guarda", $"na tela: {FrequenciaNaTela()}");
				Button? trocar = Botao(menu!.PaginaDeTeste("Equip"), "Trocar a frequência");
				Conferir(trocar is { Disabled: false }, "o botao 'Trocar a frequência' existe e esta aceso com o scouter");
				trocar?.EmitSignal(BaseButton.SignalName.Pressed);
				break;
			}

			case 4:
			{
				Foto("radio-2-teclado.png");
				Node? teclado = menu!.FindChild("TecladoDoRadio", true, false);
				Conferir(menu.TecladoDoRadioDeTeste && teclado != null, "o botao abre o teclado numerico da frequencia");
				foreach (string tecla in new[] { "4", "2", "Confirmar" })
					Botao(teclado, tecla)?.EmitSignal(BaseButton.SignalName.Pressed);
				_esperas = 0;
				break;
			}

			case 5:
				if (cli.FrequenciaDoRadio != Frequencia && ++_esperas < 20) { _passo = 5; return; }
				Conferir(cli.FrequenciaDoRadio == Frequencia && srv.FrequenciaDeTeste(_eu) == Frequencia,
					$"**digitar {Frequencia} e confirmar TROCA A FREQUENCIA**: o servidor guarda na ficha e devolve pelo `S2C.Radio`",
					$"cliente={cli.FrequenciaDoRadio} servidor={srv.FrequenciaDeTeste(_eu)}");
				Conferir(!menu!.TecladoDoRadioDeTeste, "...o teclado fecha");
				break;

			case 6:
				Conferir(FrequenciaNaTela() == Frequencia,
					"...e a aba se redesenha sozinha com a frequencia nova (sem esperar um pacote de ficha)", $"na tela: {FrequenciaNaTela()}");
				cli.SintonizarORadio(5000);   // um cliente mexido pedindo o que a tela nao deixa digitar
				break;

			case 7:
				Conferir(cli.FrequenciaDoRadio == Frequencia && srv.FrequenciaDeTeste(_eu) == Frequencia,
					"frequencia fora da faixa pedida pelo fio e RECUSADA pelo servidor: continua a mesma",
					$"cliente={cli.FrequenciaDoRadio} servidor={srv.FrequenciaDeTeste(_eu)}");
				Conferir(cli.PacotesMalLidos(Protocol.S2C.Radio) == 0, "o `S2C.Radio` foi lido certo todas as vezes (nem excecao, nem leitura alem do fim)");
				menu!.Fechar();

				// ============================================================ B. A DESCIDA
				_outro = srv.ForjarCorpoDeRadioDeTeste(_eu, NomeDoOutro, Frequencia);
				Conferir(_outro != 0, "(montagem) um segundo corpo com scouter, na mesma frequencia, a um tile de mim");
				_bocaDoOutro = Boca.NoRadio;
				break;

			case 8: case 9: break;   // 1,2 s de transmissao

			case 10:
				Foto("radio-3-ouvindo.png");
				Conferir(_pacotesDeRadio > 30, "**o outro transmite e os quadros CHEGAM pelo fio** (`S2C.VozDeRadio`)", $"{_pacotesDeRadio} pacotes");
				Conferir(cli.PacotesMalLidos(Protocol.S2C.VozDeRadio) == 0, "...lidos certo, todos (nem excecao, nem leitura alem do fim)");
				Conferir(_pacotesDePerto == 0,
					"**e NADA chega pela voz de perto** -- com ele a um tile de mim: o radio nao e a proximidade", $"{_pacotesDePerto} pacotes de voz de perto");
				Conferir(_quadrosTocados > 15 && _maiorEsquerdo > 0.05, "os quadros viram som no ouvido ESQUERDO",
					$"{_quadrosTocados} quadros, pico {_maiorEsquerdo:0.000}");
				Conferir(_quadrosComDireito == 0, "**e nenhum poe uma amostra no ouvido DIREITO**", $"{_quadrosComDireito} quadro(s)");
				Conferir(radio!.Falando(_outro), "'esta no radio' responde SIM pra ele (o sinal sobre a cabeca acende)");
				ConferirOLugarDoSinal();
				Conferir(radio.FaixaDeTeste.Contains($"{Frequencia:000}") && radio.FaixaDeTeste.Contains($"ouvindo {NomeDoOutro}"),
					"**a faixa do alto da tela diz QUEM esta falando no radio**, e a frequencia", radio.FaixaDeTeste);
				_bocaDoOutro = Boca.Calada;
				break;

			case 11: case 12: break;   // o silencio: 0,8 s

			case 13:
				Conferir(!radio!.Falando(_outro) && radio.FaixaDeTeste == "", "calado ele, o sinal apaga e a faixa some", radio.FaixaDeTeste);
				Conferir(_estalos == 1, "a transmissao fechou com UM estalo (e tambem so no esquerdo)", $"{_estalos} estalo(s), {_quadrosComDireito} com direito");
				srv.SintonizarCorpoDeTeste(_outro, Frequencia + 1);
				_pacotesDeRadio = 0;
				_bocaDoOutro = Boca.NoRadio;
				break;

			case 14: break;

			case 15:
				Conferir(_pacotesDeRadio == 0, $"**ele na frequencia {Frequencia + 1} e eu na {Frequencia}: NENHUM BYTE chega**", $"{_pacotesDeRadio} pacotes");
				_bocaDoOutro = Boca.Calada;
				srv.SintonizarCorpoDeTeste(_outro, Frequencia);
				break;

			case 16:
				_pacotesDeRadio = _pacotesDePerto = 0;
				_bocaDoOutro = Boca.DePerto;
				break;

			case 17: break;

			case 18:
				Conferir(_pacotesDePerto > 15 && _pacotesDeRadio == 0,
					"a voz de PERTO dele chega pela proximidade (ele esta colado) e NAO vaza pro radio", $"perto={_pacotesDePerto} radio={_pacotesDeRadio}");
				_bocaDoOutro = Boca.Calada;
				break;

			case 19:
				_quadrosComDireito = 0;
				EfeitoDeRadio.NosDoisOuvidosDeTeste = true;
				_bocaDoOutro = Boca.NoRadio;
				break;

			case 20: break;

			case 21:
				Conferir(_quadrosComDireito > 0, "(defeito injetado: a voz de radio nos dois ouvidos) aparecem quadros com o DIREITO -- a regua sabe reprovar", $"{_quadrosComDireito}");
				EfeitoDeRadio.NosDoisOuvidosDeTeste = false;
				_bocaDoOutro = Boca.Calada;
				break;

			// ================================================================ C. A SUBIDA
			case 22:
				_subiram = 0;
				Apertar("falar_radio");
				break;

			case 23: case 24: break;   // ~1,2 s com a tecla do radio

			case 25:
				Foto("radio-4-transmitindo.png");
				Conferir(_subiram > 30, "**a tecla do radio manda a minha voz pelo fio** (`C2S.VozDeRadio`) e o servidor a entrega a quem esta na frequencia",
					$"{_subiram} quadros entregues ao outro");
				Conferir(Microfone.NoRadio && srv.TransmitindoNoRadioDeTeste(_eu), "o cliente e o servidor concordam: estou transmitindo");
				Conferir(radio!.FaixaDeTeste.Contains("transmitindo") && radio.FaixaDeTeste.Contains($"{Frequencia:000}"),
					"**quem transmite ve que esta no radio, e a frequencia**", radio.FaixaDeTeste);
				Conferir(!srv.FalandoPorVozDeTeste(_eu),
					"**segurando SO a tecla do radio, a voz de perto NAO sai** (nenhum quadro de `C2S.Voz` chegou ao servidor)");
				Soltar("falar_radio");
				break;

			case 26:
				Conferir(!Microfone.NoRadio && radio!.FaixaDeTeste == "", "soltei: paro de transmitir e a faixa some", radio!.FaixaDeTeste);
				Conferir(radio.EstalosPropriosDeTeste == 2, "o MEU aparelho estalou duas vezes: ao abrir o canal e ao fechar",
					$"{radio.EstalosPropriosDeTeste} estalo(s)");
				break;

			case 27:
				_subiram = 0;
				Apertar("falar_radio");
				Apertar("falar_voz");
				break;

			case 28: break;

			case 29:
				Conferir(_subiram > 15 && srv.FalandoPorVozDeTeste(_eu), "com as DUAS teclas seguradas, a voz vai pelos dois caminhos",
					$"radio={_subiram} perto={srv.FalandoPorVozDeTeste(_eu)}");
				Conferir(srv.QuadrosDeRadioRecusadosDeTeste(_eu) == 0 && srv.QuadrosDeVozRecusadosDeTeste(_eu) == 0,
					"...e nenhum dos dois fluxos e recusado pela torneira (cada um tem a sua)",
					$"radio recusou {srv.QuadrosDeRadioRecusadosDeTeste(_eu)}, perto recusou {srv.QuadrosDeVozRecusadosDeTeste(_eu)}");
				SoltarTudo();
				break;

			case 30: case 31: break;   // a voz de perto cala (250 ms) antes da injecao

			case 32:
				Conferir(!srv.FalandoPorVozDeTeste(_eu), "(montagem) soltas as teclas, a voz de perto calou");
				RadioDoScouter.RadioVazaPorPertoDeTeste = true;
				Apertar("falar_radio");
				break;

			case 33: break;

			case 34:
				Conferir(srv.FalandoPorVozDeTeste(_eu), "(defeito injetado: o radio vaza pela proximidade) so a tecla do radio passa a falar de perto -- a regua sabe reprovar");
				RadioDoScouter.RadioVazaPorPertoDeTeste = false;
				Soltar("falar_radio");
				cli.SendVerbo("item_equipar", CatalogoDeItens.Scouter);   // tira o scouter
				_esperas = 0;
				break;

			case 35:
				if (cli.Atributos.Tem(Protocol.Poder.Scouter) && ++_esperas < 20) { _passo = 35; return; }
				Conferir(!cli.Atributos.Tem(Protocol.Poder.Scouter), "(montagem) scouter desligado de novo");
				_subiram = 0;
				Apertar("falar_radio");
				break;

			case 36: break;

			case 37:
			{
				Conferir(_subiram == 0 && !Microfone.NoRadio, "tirado o scouter, a tecla do radio volta a nao transmitir", $"{_subiram} quadros");
				Soltar("falar_radio");
				// O CLIENTE MEXIDO: manda o pacote assim mesmo, sem passar pelo portao da tecla.
				int antes = srv.UltimaSequenciaDoRadioDeTeste(_eu);
				for (ushort i = 0; i < 10; i++) cli.MandarVozDeRadio((ushort)(9000 + i), _tom[i], _tom[i].Length);
				_seqAntes = antes;
				break;
			}

			case 38:
				Conferir(_subiram == 0 && srv.UltimaSequenciaDoRadioDeTeste(_eu) == _seqAntes,
					"**e o SERVIDOR recusa quem manda sem scouter** (dez quadros forjados pelo fio: nenhum passou dos portoes)",
					$"entregues={_subiram} seq {_seqAntes} -> {srv.UltimaSequenciaDoRadioDeTeste(_eu)}");
				Conferir(!srv.FalandoPorVozDeTeste(_eu), "(montagem) a voz de perto esta calada antes do ultimo passo");
				_seqAntes = srv.UltimaSequenciaDoRadioDeTeste(_eu);
				Apertar("falar_voz");   // a tecla de sempre, sozinha e sem scouter
				break;

			case 39: break;

			case 40:
				Conferir(srv.FalandoPorVozDeTeste(_eu) && Microfone.PorPerto && !Microfone.NoRadio
						 && srv.UltimaSequenciaDoRadioDeTeste(_eu) == _seqAntes && radio!.FaixaDeTeste == "",
					"**a tecla de VOZ sozinha continua falando de perto pelo fio** (`C2S.Voz`), com scouter ou sem -- e nada vai pro radio",
					$"perto={srv.FalandoPorVozDeTeste(_eu)} noRadio={Microfone.NoRadio} seq do radio {_seqAntes} -> {srv.UltimaSequenciaDoRadioDeTeste(_eu)}");
				Soltar("falar_voz");
				break;

			// ================================================================ relatorio
			default:
				_acabou = true;
				Desmontar();
				GD.Print("\n[radiovivo] ===== BANCADA DO RADIO DO SCOUTER PELO FIO =====");
				foreach (string l in _linhas) GD.Print("[radiovivo] " + l);
				GD.Print(_falhas.Count == 0
					? "[radiovivo] ===== TUDO OK ====="
					: $"[radiovivo] ===== {_falhas.Count} FALHA(S) =====\n[radiovivo]   " + string.Join("\n[radiovivo]   ", _falhas));
				break;
		}
	}
}
