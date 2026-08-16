using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// A FOTO DA CHAMA (`--diagchama`) -- a metade que nenhuma prova de folha alcanca.
///
/// ============================ O PEDIDO DO DONO, LITERAL E MARCADO COMO EXTREMA IMPORTANCIA ============================
/// *"mudar o sprite da CARGA/AURA DE CARREGAMENTO DE KI e de KI ACIMA DE 100% da FORMA BASE (e das
/// formas q usam o mesmo sprite da base, como o MISTICO etc) para o sprite `Aura, Big.png`"*.
/// =====================================================================================================================
///
/// ============================ POR QUE A `--diagforma` NAO BASTAVA ============================
/// A bancada das formas ja confere, com quatro mil checagens, que a folha base **e** a `Aura, Big`,
/// que ela carrega, que todo quadro dela tem pixel, que o RGB dela e chapado e que o alfa dela e
/// identico ao da `AuraSSjBig`. Tudo isso e verdade sobre o ARQUIVO e sobre o UNIFORM.
///
/// Nada disso e um pixel na tela. A memoria desta casa ja tem o nome do buraco ("a bancada mede
/// INTENCAO"), e ele custou quatro defeitos visuais depois de quatro mil provas verdes: *uniform
/// escrito nao e pixel desenhado*, `Modulate` nao e tela, e "as duas telas concordam" fica verde com
/// as duas erradas igual.
///
/// Esta bancada RENDERIZA e mede o pixel. E ela nao julga a foto no olho.
/// ==============================================================================================
///
/// ============================ DUAS MEDIDAS, E A SEGUNDA E QUE DECIDE ============================
///   1. **AS FOTOS EM JOGO** (`chama-jogo-*.png`) -- o corpo do jogador, segurando C, na zona de
///      verdade. Elas sao o registro visual pro dono e trazem o NUMERO da diferenca, mas nao decidem
///      nada sozinhas: medido, o piso de ruido em jogo e **26%** do recorte (os raios da carga, a luz
///      do planeta, o clima), da mesma ordem do sinal. Uma prova erguida em cima disso ficaria verde
///      com a folha errada.
///   2. **O LABORATORIO** (`chama-lab-*.png`) -- a MESMA classe de producao (<see cref="SpriteDeAura"/>,
///      com o `Aura.gdshader` de verdade) desenhando numa <see cref="SubViewport"/> de fundo
///      transparente. Sem mundo, sem luz, sem raio: o ruido cai a ZERO e a diferenca medida e a da
///      ARTE. E ai a comparacao com a folha de ontem vira prova, e nao impressao.
/// ================================================================================================
///
/// ============================ E A PROVA QUE IMPORTA MAIS QUE "MUDOU" ============================
/// **A chama da base nao pode sair PRETA.** `Aura, Big.png` e `rgb(0,0,0)` em 100% dos pixels opacos
/// dela; sem o ramo `forma_no_alfa` do shader, `i = max(r,g,b)` da zero e `cor * 0` e preto -- uma
/// silhueta preta em volta de 19 formas, com a bancada de folha VERDE.
///
/// Entao o laboratorio mede o BRILHO do que foi desenhado, e -- pra a medida valer -- desenha a mesma
/// folha com o uniform ERRADO de proposito (<see cref="SpriteDeAura.ForcarDesenhoNoAlfaDeTeste"/>) e
/// exige que ali o preto apareca. Uma medida que nao enxerga o desastre nao vale nada, e essa e a
/// unica maneira de saber que esta enxerga.
/// ================================================================================================
///
/// ============================ AS LINHAS DO PEDIDO, UMA A UMA ============================
///   1. a AURA/CARGA DA BASE      -- o corpo na base, com a chama acesa pela tecla C
///   2. o KI ACIMA DE 100%        -- o excesso, que so se distingue por `forca` e cadencia
///   3. uma forma que HERDA a base (o Mistico, que o dono citou pelo nome)
///   4. o CONTRA-EXEMPLO          -- quem NAO herda continua com a sua (SSJ e a divina quente)
/// ========================================================================================
///
///     Godot --path . --host --rede 7904 --kiteste --bpteste 3000000 --horateste 0.5 --diagchama \
///           --position 1920,0 --resolution 1280x720 --raca Human --conta bancada_chama --nome Chama
///
/// `--kiteste` porque o Ki acima de 100% exige controle de Ki liberado -- sem ele o servidor recusa a
/// carga e as fases 2 e 3 nao teriam o que medir. `--horateste 0.5` crava meio-dia (a hora do mundo e
/// sorteada, e o registro visual de uma chama as tres da manha nao responde nada pra ninguem).
///
/// **PRECISA DE JANELA.** No `--headless` o `GetImage` volta vazio: ali as linhas de pixel saem como
/// **SEM MEDIDA**, num contador proprio, e nunca como "ok".
///
/// As fotos saem em `user://chama-*.png`.
/// </summary>
public partial class RoboDaChama : Node
{
	private static GameClient? C => GameClient.Instance;

	private readonly List<string> _passos = [];
	private readonly List<string> _falhas = [];
	private readonly List<string> _semMedida = [];
	private int _verdes;

	private bool _acabou;
	private double _t, _vida;
	private int _passo;

	/// <summary>Depois disto ela desiste. O servidor pode ter recusado a carga.</summary>
	private const double Paciencia = 180;

	/// <summary>
	/// O QUADRO DA FITA EM QUE TODA FOTO E TIRADA. Qualquer um serviria -- o que nao pode e variar
	/// entre os disparos do mesmo par. Ver <see cref="SpriteDeAura.QuadroDeTeste"/>.
	/// </summary>
	private const int QuadroDaFoto = 2;

	/// <summary>
	/// A FOLHA DE ONTEM -- a `colorablebigaura`, que era a base ate o dono pedir a troca.
	///
	/// Ela entra pelo <see cref="SpriteDeAura.DefinirFolha(string)"/> (o caminho CRU), que existe pra
	/// bancada e diz isso no proprio cabecalho: quem entra por ali perde o simbolo e responde pelo
	/// arquivo. E o unico jeito de desenhar o "antes" sem fazer o jogo mentir sobre a forma.
	/// </summary>
	private const string FolhaDeOntem = "res://Assets/Sprites/Auras/colorablebigaura.tres";

	/// <summary>A cor que o laboratorio manda pra chama -- a do Ki cru, que e a da base em jogo.</summary>
	private static Color CorDaBase => Aura.CorDoKiCru;

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (ok) _verdes++; else _falhas.Add(oque);
	}

	private void NaoMediu(string oque)
	{
		_passos.Add("  --      SEM MEDIDA  " + oque);
		_semMedida.Add(oque);
	}

	private void Nota(string oque) => _passos.Add("  --     " + oque);

	public override void _Ready()
	{
		// ESTE MUNDO E MEU? Copiado do `RoboDeTrilha` e pelo mesmo motivo escrito la: com a porta
		// tomada o `--host` nao vira servidor nenhum e o cliente entra no mundo DE OUTRA SESSAO -- e
		// esta bancada segura a tecla C e troca a folha da aura do corpo. Ha outra sessao neste repo.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[chama] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo "
					  + "e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
		}
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (C is not { Connected: true } cli || World.Instancia is not { } mundo) return;

		_vida += delta;
		if (_vida > Paciencia) { Nota($"acabou a paciencia ({Paciencia:0} s)"); Fechar(); return; }

		_t += delta;

		switch (_passo)
		{
			case 0: Assentar(cli); break;
			case 1: OLaboratorio(); break;
			case 2: SegurarOC(mundo, cli); break;
			case 3: AFotoEmJogo(mundo, cli); break;
			case 4: OKiAcimaDeCem(mundo, cli); break;
			case 5: AsFolhasDasFormas(mundo, cli); break;
			default: Fechar(); break;
		}
	}

	private void Virar(int proximo) { _passo = proximo; _t = 0; }

	private void Assentar(GameClient cli)
	{
		if (_t < 3) return;
		Nota($"de pe em `{cli.Zone.Name}`, id {cli.LocalId}.");
		Virar(1);
	}

	// =====================================================================
	// 1) O LABORATORIO -- a mesma classe, o mesmo shader, sem mundo em volta
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ISTO NAO E "TESTAR O TESTE" ============================
	/// Nao ha nada de laboratorio no que e medido: e o <see cref="SpriteDeAura"/> de producao, com o
	/// `Aura.gdshader` de producao, montando o `.tres` de producao e pintado pelo `Pintar` de
	/// producao. O que fica de fora e o CENARIO -- e o cenario e exatamente o ruido que impede a
	/// medida em jogo de decidir qualquer coisa (26% do recorte, medido).
	///
	/// A `SubViewport` tem fundo TRANSPARENTE, entao todo pixel opaco da imagem e chama e nada mais.
	/// E o `_relogio` do sprite anda igual, entao o quadro da fita e esperado como em jogo.
	/// =============================================================================================
	/// </summary>
	private void OLaboratorio()
	{
		if (_lab == null)
		{
			_lab = new SubViewport
			{
				Size = new Vector2I(TamanhoDoLab, TamanhoDoLab),
				TransparentBg = true,
				RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
				RenderTargetClearMode = SubViewport.ClearMode.Always,
			};
			AddChild(_lab);

			// O `SpriteDeAura` ancora a base da chama nos PES do corpo (ver `AncoraPara`), entao ele
			// e posto abaixo do meio -- do contrario metade do desenho sairia por cima da borda de
			// cima e a comparacao mediria um recorte diferente em cada folha.
			_chama = new SpriteDeAura { Name = "ChamaDeLaboratorio", Position = new Vector2(TamanhoDoLab / 2f, TamanhoDoLab * 0.62f) };
			_lab.AddChild(_chama);
			_chama.Definir(true, CorDaBase);
			return;
		}

		if (_chama is not { } chama) { NaoMediu("o laboratorio nao montou"); Virar(2); return; }

		// ---------- as duas folhas do MESMO desenho: quantos quadros TOCAM ----------
		if (_etapaLab == 0)
		{
			chama.DefinirFolha(FolhaDeAura.Base);
			int qBase = chama.QuantosQuadrosDeTeste;
			chama.DefinirFolha(FolhaDeAura.Ssj);
			int qSsj = chama.QuantosQuadrosDeTeste;
			chama.DefinirFolha(FolhaDeAura.Base);

			// ============================ O NUMERO NAO E 8, E ISSO E UM ACHADO ============================
			// O comentario da folha diz "8 quadros em fita", e o `.tres` tem mesmo 8 recortes -- mas
			// divididos em DUAS animacoes de 4 (`default` e `2`), e o `Montar` toca so a `default`.
			// Ou seja METADE da fita nunca aparece.
			//
			// A prova nao crava 4 nem 8: ela cobra a RELACAO. A `AuraSSjBig` e o MESMO desenho (alfa
			// identico, medido) e sofre o mesmo corte, entao a chama da base anda exatamente como a do
			// Super Saiyajin que o dono ja aprovou -- que e a afirmacao que sustenta esta troca. Se um
			// dia alguem juntar as duas animacoes de uma das folhas e nao da outra, isto reprova.
			// ==============================================================================================
			Conferir(qBase > 0 && qBase == qSsj,
					 $"1. a fita da base toca os MESMOS quadros que a do SSJ ({qBase} contra {qSsj}) "
				   + "-- e o mesmo desenho, e anda igual");
			Nota($"achado: o `.tres` tem 8 recortes divididos em DUAS animacoes; so a `default` toca "
			   + $"({qBase} quadros). Vale pra `Aura, Big` E pra `AuraSSjBig`.");
			_etapaLab = 1;
			return;
		}

		// ---------- as tres imagens do par: hoje, hoje de novo (ruido), ontem ----------
		if (_etapaLab == 1)
		{
			if (chama.QuadroDeTeste != QuadroDaFoto) return;
			_labHoje = Foto("chama-lab-1-base-hoje-Aura-Big.png");
			if (_labHoje == null) { NaoMediu("o laboratorio nao devolveu imagem (janela?)"); Virar(2); return; }
			_etapaLab = 2;
			_t = 0;
			return;
		}

		if (_etapaLab == 2)
		{
			if (_t < 0.4 || chama.QuadroDeTeste != QuadroDaFoto) return;
			_labHoje2 = Foto("chama-lab-1b-base-hoje-de-novo.png");
			if (_labHoje2 == null) { NaoMediu("a segunda imagem do laboratorio"); Virar(2); return; }
			_ruidoLab = Diferenca(_labHoje!, _labHoje2);
			Nota($"piso de ruido do laboratorio: {_ruidoLab * 100:0.000}% "
			   + "(mesma folha, mesmo quadro -- sem mundo em volta ele e ZERO)");
			_etapaLab = 3;
			return;
		}

		if (_etapaLab == 3)
		{
			if (!ResourceLoader.Exists(FolhaDeOntem))
			{
				NaoMediu($"a folha de ontem sumiu do disco ({FolhaDeOntem.GetFile()}) -- sem par pra comparar");
				_etapaLab = 5;
				return;
			}
			chama.DefinirFolha(FolhaDeOntem);
			_etapaLab = 4;
			_t = 0;
			return;
		}

		if (_etapaLab == 4)
		{
			if (_t < 0.2 || chama.QuadroDeTeste != QuadroDaFoto) return;
			Image? ontem = Foto("chama-lab-2-base-ontem-colorablebigaura.png");
			chama.DefinirFolha(FolhaDeAura.Base);   // volta ANTES de qualquer `Conferir`

			if (ontem == null || _labHoje == null) { NaoMediu("a imagem da folha de ontem"); _etapaLab = 5; return; }

			double sinal = Diferenca(_labHoje, ontem);
			double razao = sinal / Math.Max(_ruidoLab, 1e-6);
			Nota($"sinal da troca de folha: {sinal * 100:0.000}% (ruido {_ruidoLab * 100:0.000}%)");
			Conferir(razao >= 10 && sinal > 0.05,
					 $"1. a chama da base MUDOU de desenho: {razao:0.#}x o ruido, {sinal * 100:0.0}% "
				   + "dos pixels do recorte");
			_etapaLab = 5;
			return;
		}

		// ---------- E A PROVA QUE VALE MAIS QUE "MUDOU": ela nao sai PRETA ----------
		if (_etapaLab == 5)
		{
			if (chama.QuadroDeTeste != QuadroDaFoto) return;
			if (Foto("chama-lab-3-base-como-o-jogo-pinta.png") is not { } certa)
			{ NaoMediu("a imagem do brilho"); _etapaLab = 7; return; }

			_brilhoCerto = Brilho(certa);
			Conferir(_brilhoCerto > 0.15,
					 $"1. a chama da base NAO sai preta: brilho medio {_brilhoCerto:0.000} nos pixels "
				   + "opacos (a `Aura, Big` e rgb(0,0,0) no arquivo -- quem a acende e o `forma_no_alfa`)");

			// O DEFEITO, INJETADO AQUI DENTRO: o mesmo desenho com o uniform errado.
			chama.ForcarDesenhoNoAlfaDeTeste(false);
			_etapaLab = 6;
			_t = 0;
			return;
		}

		if (_etapaLab == 6)
		{
			if (_t < 0.2 || chama.QuadroDeTeste != QuadroDaFoto) return;
			Image? preta = Foto("chama-lab-4-DEFEITO-sem-forma-no-alfa.png");
			chama.Definir(true, CorDaBase);   // o `Pintar` reescreve o uniform certo

			if (preta == null) { NaoMediu("a imagem do defeito injetado"); _etapaLab = 7; return; }

			double brilhoRuim = Brilho(preta);
			Nota($"defeito injetado (`forma_no_alfa = false`): brilho {brilhoRuim:0.000} "
			   + $"contra {_brilhoCerto:0.000} do certo");
			Conferir(brilhoRuim < 0.05 && _brilhoCerto > brilhoRuim * 5,
					 "1. e a MEDIDA ENXERGA o desastre: com o uniform errado a mesma folha sai PRETA "
				   + $"({brilhoRuim:0.000}) -- sem isto a linha de cima seria uma frase");
			_etapaLab = 7;
			return;
		}

		Virar(2);
	}

	private const int TamanhoDoLab = 192;
	private SubViewport? _lab;
	private SpriteDeAura? _chama;
	private int _etapaLab;
	private Image? _labHoje, _labHoje2;
	private double _ruidoLab, _brilhoCerto;

	// =====================================================================
	// 2) O GESTO DO JOGADOR: SEGURAR C
	// =====================================================================
	/// <summary>
	/// A CARGA E PEDIDA PELO CANAL DE VERDADE (<see cref="GameClient.SendCarregar"/>), e nao acesa na
	/// mao.
	///
	/// ============================ E ISSO NAO E CERIMONIA ============================
	/// Acender a `CargaVisual` daqui deixaria verde um jogo em que o servidor RECUSA a carga -- que e
	/// um estado que existe (sem controle de Ki, sem folego, andando, nocauteado). A tecla PEDE; quem
	/// decide e o servidor; quem acende e o `World` ao receber o efeito de volta. Esta fase espera o
	/// efeito CHEGAR, e desiste em voz alta se ele nao chegar.
	/// ================================================================================
	/// </summary>
	private void SegurarOC(World mundo, GameClient cli)
	{
		if (Carga(mundo, cli) is not { } carga)
		{
			if (_t > 8) { Conferir(false, "o meu corpo tem o node `Carga` (a chama da tecla C)"); Virar(5); }
			return;
		}

		if (!_pediuOC) { _pediuOC = true; cli.SendCarregar(true); Nota("pedi a carga pelo canal de verdade (tecla C)"); }

		if (!carga.CarregandoDeTeste)
		{
			if (_t > 12) { Conferir(false, "o servidor ACEITOU a carga (sem `--kiteste` ele recusa)"); Virar(5); }
			return;
		}

		Conferir(true, "2. o servidor aceitou a carga e a chama do C acendeu no MEU corpo");
		Conferir(carga.DesenhoDeTeste.FolhaDeTeste == SpriteDeAura.FolhaBase,
				 $"2. a CARGA da base desenha a `Aura, Big` ({carga.DesenhoDeTeste.FolhaDeTeste.GetFile()})");

		// ============================ A PERGUNTA CERTA NAO E O SIMBOLO ============================
		// A primeira versao cobrava `SimboloDeTeste == FolhaDeAura.Base` aqui e ficou VERMELHA num
		// jogo certo: num corpo que nunca trocou de forma ninguem chama `Folha(simbolo)` -- o
		// `SpriteDeAura` nasce com `_folha = FolhaBase` e o simbolo fica nulo. Cobrar o simbolo era
		// cobrar que a base fosse uma "transformacao", que ela nao e.
		//
		// O que precisa ser verdade e o `DesenhoNoAlfa`, porque e dele que sai o `forma_no_alfa` do
		// shader -- e sem ele esta folha sai preta. Ele responde certo pelos DOIS caminhos (simbolo ou
		// arquivo), que e exatamente o que o cabecalho dele promete.
		// ==========================================================================================
		Conferir(carga.DesenhoDeTeste.DesenhoNoAlfa,
				 "2. e o node sabe que o desenho dela mora no ALFA (sem isto ela sairia preta)");
		Virar(3);
	}

	private bool _pediuOC;

	// =====================================================================
	// 3) O REGISTRO VISUAL, EM JOGO
	// =====================================================================
	/// <summary>
	/// AS FOTOS EM JOGO SAO REGISTRO E NUMERO, E NAO VEREDITO -- ver o cabecalho da classe. O piso de
	/// ruido do mundo (raios da carga, luz, clima) e da MESMA ordem do sinal, entao aqui o par e
	/// gravado, medido e RELATADO; quem decide e o laboratorio.
	///
	/// Elas existem porque o dono pediu a foto, e porque uma foto responde uma coisa que numero
	/// nenhum responde: a chama esta no lugar certo do corpo, no tamanho certo, atras dele.
	/// </summary>
	private void AFotoEmJogo(World mundo, GameClient cli)
	{
		if (Carga(mundo, cli) is not { } carga) { Virar(5); return; }
		SpriteDeAura desenho = carga.DesenhoDeTeste;
		if (desenho.QuadroDeTeste != QuadroDaFoto) return;

		if (_jogoHoje == null)
		{
			_jogoHoje = Retrato("chama-jogo-1-base-hoje-Aura-Big.png");
			if (_jogoHoje == null) { NaoMediu("a foto em jogo (janela? `--headless` nao fotografa)"); Virar(4); }
			return;
		}

		if (!_trocouEmJogo)
		{
			if (_t < 0.5) return;
			Image? ruidoIm = Retrato("chama-jogo-1b-base-hoje-de-novo.png");
			_ruidoJogo = ruidoIm == null ? -1 : Diferenca(_jogoHoje, ruidoIm);
			if (!ResourceLoader.Exists(FolhaDeOntem)) { Virar(4); return; }
			_trocouEmJogo = true;
			desenho.DefinirFolha(FolhaDeOntem);
			_t = 0;
			return;
		}

		if (_t < 0.2) return;
		Image? ontem = Retrato("chama-jogo-2-base-ontem-colorablebigaura.png");
		desenho.DefinirFolha(FolhaDeAura.Base);
		Conferir(desenho.FolhaDeTeste == SpriteDeAura.FolhaBase,
				 "3. a folha de hoje voltou pro lugar depois da foto do `antes`");

		if (ontem != null && _ruidoJogo >= 0)
			Nota($"em jogo: a troca de folha moveu {Diferenca(_jogoHoje, ontem) * 100:0.0}% do recorte, "
			   + $"contra um piso de ruido de {_ruidoJogo * 100:0.0}% -- por isso quem DECIDE e o "
			   + "laboratorio, e nao esta foto");

		Virar(4);
	}

	private Image? _jogoHoje;
	private bool _trocouEmJogo;
	private double _ruidoJogo = -1;

	// =====================================================================
	// 4) O KI ACIMA DE 100%
	// =====================================================================
	/// <summary>
	/// O EXCESSO E A MESMA FOLHA, E E ISSO QUE SE AFIRMA.
	///
	/// A carga e o Ki acima de 100% nunca se distinguiram por FOLHA neste port -- eles se distinguem
	/// por `forca` (que no shader governa o alfa e o estouro da cor) e pela cadencia. Entao a prova
	/// aqui e dupla: o excesso ACONTECEU (`ExcessoDeTeste`, que vem do servidor pelo `aura_ki`) **e** a
	/// folha continua sendo a `Aura, Big`. Sem a primeira metade, a segunda ficaria verde num jogo em
	/// que o Ki nunca passa dos 100%.
	/// </summary>
	private void OKiAcimaDeCem(World mundo, GameClient cli)
	{
		if (Carga(mundo, cli) is not { } carga) { Virar(5); return; }

		if (!carga.ExcessoDeTeste)
		{
			if (_t > 30) { NaoMediu("o Ki nunca passou dos 100% (sem `--kiteste` o servidor recusa a carga)"); Virar(5); }
			return;
		}

		if (!_provouExcesso)
		{
			_provouExcesso = true;
			Conferir(true, "4. o Ki passou dos 100% -- o `aura_ki` chegou do servidor");
			Conferir(carga.DesenhoDeTeste.FolhaDeTeste == SpriteDeAura.FolhaBase,
					 "4. e o Ki acima de 100% na base desenha a MESMA `Aura, Big` "
				   + $"({carga.DesenhoDeTeste.FolhaDeTeste.GetFile()})");
			return;
		}

		if (carga.DesenhoDeTeste.QuadroDeTeste != QuadroDaFoto) return;
		if (Retrato("chama-jogo-3-ki-acima-de-100.png") == null) NaoMediu("a foto do Ki acima de 100%");
		Virar(5);
	}

	private bool _provouExcesso;

	// =====================================================================
	// 5) AS FORMAS: QUEM HERDA A BASE E QUEM NAO HERDA
	// =====================================================================
	/// <summary>
	/// ============================ ESTA FASE NAO TRANSFORMA O PERSONAGEM, E DIZ POR QUE ============================
	/// Ela alimenta o node com o MESMO simbolo que o jogo alimenta -- `Catalogo.Folha(def)` entregue a
	/// `CargaVisual.Folha`, que e literalmente a ultima linha do `World.PrepararAuraDaForma`. O que
	/// fica de fora e o encanamento da transformacao (escada, maestria, Ki, cinematica), e ele **nao e
	/// o assunto do pedido**: o dono falou de SPRITE.
	///
	/// Fazer a bancada subir a escada de verdade custaria minutos por degrau e faria uma prova de
	/// sprite reprovar quando quem quebrasse fosse a maestria. A afirmacao que fica e exata: *dado o
	/// simbolo que o Core deriva desta forma, a chama que este corpo monta e esta*.
	/// ============================================================================================================
	///
	/// ============================ E O CONTRA-EXEMPLO E METADE DA FAMILIA ============================
	/// Sem ele, trocar TODAS as folhas do jogo pela `Aura, Big` passaria com nota cheia -- e seria o
	/// oposto do pedido, que nomeia "a forma BASE (e as formas q usam o mesmo sprite da base)".
	/// ==============================================================================================
	/// </summary>
	private void AsFolhasDasFormas(World mundo, GameClient cli)
	{
		if (Carga(mundo, cli) is not { } carga || _chama is not { } lab) { Fechar(); return; }
		SpriteDeAura desenho = carga.DesenhoDeTeste;

		if (!_varreu)
		{
			_varreu = true;
			var herdam = new List<string>();
			var proprias = new List<string>();
			foreach (FormaDef d in Catalogo.Todas)
				(Catalogo.Folha(d) == FolhaDeAura.Base ? herdam : proprias).Add(d.Id);

			Conferir(herdam.Count > 0 && proprias.Count > 0,
					 $"5. as duas classes de forma existem ({herdam.Count} herdam a base, "
				   + $"{proprias.Count} tem folha propria)");
			Nota($"o \"etc\" do pedido, por extenso: {string.Join(", ", herdam)}");
			return;
		}

		// ---------- 5. UMA FORMA QUE HERDA: o Mistico, citado pelo nome ----------
		if (_fase == 0)
		{
			if (Forma(Catalogo.IdMistico) is not { } mistico)
			{
				NaoMediu($"nao achei a forma `{Catalogo.IdMistico}` no catalogo");
				_fase = 1;
				return;
			}

			// ============================ A PROVA SO UMA VEZ, E ISTO E CONSERTO DE RODADA ============================
			// Esta fase volta a cada quadro esperando o quadro certo da fita. Sem o `_provouFase`, cada
			// `Conferir` daqui saia UMA VEZ POR QUADRO -- a primeira rodada fechou com 120 linhas
			// verdes que eram 20 provas repetidas, e um placar inflado e um placar que nao se le.
			// ==========================================================================================================
			if (_provouFase != 1)
			{
				_provouFase = 1;
				Conferir(Catalogo.Folha(mistico) == FolhaDeAura.Base,
						 "5. o MISTICO herda o sprite da base (o Core diz `FolhaDeAura.Base`)");
				carga.Folha(Catalogo.Folha(mistico));
				lab.DefinirFolha(Catalogo.Folha(mistico));
				Conferir(desenho.FolhaDeTeste == SpriteDeAura.FolhaBase,
						 $"5. e o node monta a `Aura, Big` pra ele ({desenho.FolhaDeTeste.GetFile()})");
				return;
			}

			if (lab.QuadroDeTeste != QuadroDaFoto) return;
			_labMistico = Foto("chama-lab-5-mistico-herda-a-base.png");
			Retrato("chama-jogo-4-mistico-herda-a-base.png");
			if (_labMistico == null) NaoMediu("a imagem do Mistico");
			_fase = 1;
			_t = 0;
			return;
		}

		// ---------- 6. O CONTRA-EXEMPLO: quem NAO herda ----------
		if (_fase == 1)
		{
			if (Forma("ssj1") is not { } ssj) { NaoMediu("nao achei a forma `ssj1`"); _fase = 2; return; }

			if (_provouFase != 2)
			{
				_provouFase = 2;
				Conferir(Catalogo.Folha(ssj) == FolhaDeAura.Ssj,
						 "6. o SUPER SAIYAJIN nao herda: a folha dele continua sendo a `AuraSSjBig`");
				carga.Folha(Catalogo.Folha(ssj));
				lab.DefinirFolha(Catalogo.Folha(ssj));
				Conferir(desenho.FolhaDeTeste == SpriteDeAura.FolhaSsj,
						 $"6. e o node monta a folha DELE ({desenho.FolhaDeTeste.GetFile()})");
				_t = 0;
				return;
			}

			if (lab.QuadroDeTeste != QuadroDaFoto) return;
			if (_t < 0.3) return;
			Image? labSsj = Foto("chama-lab-6-ssj-tem-a-dele.png");
			Retrato("chama-jogo-5-ssj-tem-a-dele.png");

			// ============================ O PAR MAIS DURO DO CONTRA-EXEMPLO ============================
			// A `AuraSSjBig` **e o mesmo desenho** da `Aura, Big` no canal alfa -- zero pixel de
			// diferenca, medido. O que separa as duas na tela e so a COR (uma se tinge com a cor do
			// jogador, a outra ja vem `ffff80` no arquivo e sai pelo ramo `!tingir`).
			//
			// Entao esta linha exige que a diferenca APARECA mesmo quando o desenho e identico -- que e
			// a unica coisa que distingue "cada um com a sua folha" de uma frase sobre strings.
			// ==========================================================================================
			if (labSsj != null && _labMistico != null)
			{
				double sinal = Diferenca(_labMistico, labSsj);
				double razao = sinal / Math.Max(_ruidoLab, 1e-6);
				Conferir(razao >= 10 && sinal > 0.05,
						 $"6. e a chama do SSJ sai DIFERENTE da que herda a base: {razao:0.#}x o ruido "
					   + $"({sinal * 100:0.0}%) -- mesmo sendo o MESMO desenho no alfa");
			}
			else if (labSsj == null) NaoMediu("a imagem do SSJ");

			_fase = 2;
			_t = 0;
			return;
		}

		// ---------- 6b. E A DIVINA QUENTE, que e ARTE OUTRA e nao so outra cor ----------
		if (_fase == 2)
		{
			if (_provouFase != 3)
			{
				_provouFase = 3;
				carga.Folha(FolhaDeAura.DeusQuente);
				lab.DefinirFolha(FolhaDeAura.DeusQuente);
				Conferir(desenho.FolhaDeTeste == SpriteDeAura.FolhaDeusQuente,
						 $"6. a chama divina quente tambem tem a dela ({desenho.FolhaDeTeste.GetFile()})");
				_t = 0;
				return;
			}
			if (lab.QuadroDeTeste != QuadroDaFoto) return;
			if (_t < 0.3) return;
			Image? labDeus = Foto("chama-lab-7-deus-quente-arte-propria.png");
			Retrato("chama-jogo-6-deus-quente-arte-propria.png");
			if (labDeus != null && _labHoje != null)
			{
				double sinal = Diferenca(_labHoje, labDeus);
				Conferir(sinal > 0.05,
						 $"6. e ela e ARTE OUTRA, nao so outra cor: {sinal * 100:0.0}% de diferenca "
					   + "contra a chama da base");
			}
			_fase = 3;
			return;
		}

		// A FOLHA VOLTA PRA BASE ANTES DE SAIR. A bancada nao pode deixar o corpo do dono com a chama
		// de outra forma montada -- ela mediu, ela devolve.
		carga.Folha(FolhaDeAura.Base);
		Conferir(desenho.FolhaDeTeste == SpriteDeAura.FolhaBase,
				 "6. e a folha da base voltou pro lugar no fim de tudo");
		Fechar();
	}

	private bool _varreu;
	private int _fase;

	/// <summary>Em qual fase as provas ja sairam -- ver o bloco do `_provouFase` na fase 5.</summary>
	private int _provouFase;

	private Image? _labMistico;

	private static FormaDef? Forma(string id)
	{
		foreach (FormaDef d in Catalogo.Todas)
			if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d;
		return null;
	}

	// =====================================================================
	// A CAMERA E A REGUA
	// =====================================================================
	private CargaVisual? Carga(World mundo, GameClient cli) =>
		mundo.CorpoDeTeste(cli.LocalId)?.GetNodeOrNull<CargaVisual>("Carga");

	/// <summary>A imagem do LABORATORIO -- so a chama, em fundo transparente.</summary>
	private Image? Foto(string nome)
	{
		Image? img = _lab?.GetTexture()?.GetImage();
		return Gravar(img, nome);
	}

	/// <summary>A foto da TELA -- o corpo no mundo, com cenario e tudo.</summary>
	private Image? Retrato(string nome) => Gravar(GetViewport()?.GetTexture()?.GetImage(), nome);

	private Image? Gravar(Image? img, string nome)
	{
		if (img == null || img.GetWidth() == 0) return null;
		string caminho = $"user://{nome}";
		img.SavePng(caminho);
		Nota($"foto: {ProjectSettings.GlobalizePath(caminho)}");
		return img;
	}

	/// <summary>
	/// O BRILHO MEDIO DOS PIXELS OPACOS -- a medida que separa "a chama acendeu" de "saiu uma
	/// silhueta preta". Zero quando nao ha pixel opaco nenhum (chama apagada), e ai a prova reprova
	/// em vez de passar.
	/// </summary>
	private static double Brilho(Image im)
	{
		double soma = 0;
		long n = 0;
		for (int y = 0; y < im.GetHeight(); y++)
			for (int x = 0; x < im.GetWidth(); x++)
			{
				Color p = im.GetPixel(x, y);
				if (p.A <= 0.05f) continue;
				n++;
				soma += Mathf.Max(Mathf.Max(p.R, p.G), p.B);
			}
		return n == 0 ? 0 : soma / n;
	}

	/// <summary>
	/// A FRACAO DE PIXELS QUE MUDOU entre duas imagens do mesmo tamanho, no recorte do meio.
	///
	/// ============================ POR QUE SO O MEIO ============================
	/// No laboratorio o recorte e quase a imagem toda (a chama ocupa o quadro); em jogo o corpo esta
	/// no centro e a chama cabe num quadro pequeno em volta dele -- um diff da tela inteira diluiria
	/// o sinal em 1280x720 pixels de cenario parado. O recorte e uma FRACAO e nao um numero de
	/// pixels, pra a medida valer em qualquer `--resolution`.
	/// ============================================================================
	///
	/// ============================ E O QUE CONTA COMO "MUDOU" ============================
	/// Diferenca PERCEPTIVEL, nao bit exato: `1/255` em qualquer canal escaparia do dither e do
	/// arredondamento do shader e faria a imagem inteira "mudar" o tempo todo. O corte e 8/255 em
	/// algum canal -- acima do ruido de quantizacao e muito abaixo da diferenca entre duas artes.
	///
	/// O ALFA CONTA JUNTO, e no laboratorio ele e quem manda: duas folhas com silhuetas diferentes
	/// diferem primeiro em ONDE ha pixel, e so depois em cor.
	/// ====================================================================================
	/// </summary>
	private static double Diferenca(Image a, Image b)
	{
		if (a.GetSize() != b.GetSize()) return 1;

		int w = a.GetWidth(), h = a.GetHeight();
		int meiaL = Mathf.Max(1, w / 8), meiaA = Mathf.Max(1, h / 5);
		int x0 = Mathf.Max(0, w / 2 - meiaL), x1 = Mathf.Min(w, w / 2 + meiaL);
		int y0 = Mathf.Max(0, h / 2 - meiaA), y1 = Mathf.Min(h, h / 2 + meiaA);

		long total = 0, mudou = 0;
		const float corte = 8f / 255f;
		for (int y = y0; y < y1; y++)
			for (int x = x0; x < x1; x++)
			{
				total++;
				Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
				if (Mathf.Abs(p.R - q.R) > corte || Mathf.Abs(p.G - q.G) > corte
					|| Mathf.Abs(p.B - q.B) > corte || Mathf.Abs(p.A - q.A) > corte) mudou++;
			}
		return total == 0 ? 0 : (double)mudou / total;
	}

	private void Fechar()
	{
		if (_acabou) return;
		_acabou = true;

		// A TECLA E SOLTA. Sair da bancada com o C "em baixo" deixaria o corpo carregando pra sempre.
		C?.SendCarregar(false);

		GD.Print("[chama] ================ A FOTO DA CHAMA ================");
		foreach (string p in _passos) GD.Print("[chama] " + p);
		GD.Print($"[chama] ===== FIM: {_verdes} OK, {_falhas.Count} FALHA(S), "
			   + $"{_semMedida.Count} SEM MEDIDA =====");
		foreach (string f in _falhas) GD.PrintErr("[chama] FALHA: " + f);
		foreach (string s in _semMedida) GD.PrintErr("[chama] SEM MEDIDA: " + s);
		GD.Print("[chama] as fotos estao em " + ProjectSettings.GlobalizePath("user://"));
	}
}
