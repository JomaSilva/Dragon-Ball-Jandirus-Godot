using Godot;
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// ============================ A FOTO DE UM NPC VIRANDO MACACO (`--diagmacaco`) ============================
/// A bancada do servidor (`--luaferateste`, 71 checagens) prova que o gatilho decide certo: quem vira,
/// quem nao vira, quem perde as redeas e quem volta a ser gente. **Nenhuma delas olha a tela**, e a
/// distancia entre as duas coisas e o defeito mais caro deste port: o servidor transforma, o pacote
/// sai, e o boneco continua sendo o mesmo Saiyajin de 32 pixels. *"O gatilho ligou e o desenho nao"*.
///
/// Aqui o juiz e a FOTO, em tres tempos -- antes, durante e depois --, com a leitura do node ao lado
/// dela como contraprova (a foto responde "que forma e essa"; o node responde "de que folha").
///
/// COMO RODAR (precisa de JANELA: no headless o `GetImage` volta vazio e nao ha foto nenhuma):
///     Godot --path . --host --rede 7954 --macacovivo --diagmacaco --bpteste 3000000 \
///           --raca Saiyan --conta bancada_macaco --nome OlhaOMacaco
///
/// As fotos saem em `user://macaco-*.png`.
///
/// ============================ POR QUE ELA NAO ESCOLHE O CORPO, E ESPERA O PACOTE ============================
/// A Terra tem cidadaos de povoamento andando por perto, e "o corpo mais proximo do host" pode ser
/// qualquer um deles. Uma bancada que escolhesse o boneco pelo olho mediria o corpo errado no dia em
/// que um cidadao passasse na frente -- e a foto sairia certa, do sujeito errado.
///
/// Entao ela nao escolhe: **quem diz qual corpo e o proprio `S2C.Oozaru`**, que traz o id. A foto 1 e
/// tirada antes de qualquer transformacao e guarda a FOLHA DE CADA CORPO da zona; quando o pacote
/// chega, o id dele acha nessa tabela o que aquele mesmo corpo era um instante antes. E assim a
/// afirmacao "o sprite trocou" e sobre UM corpo, e nao sobre a paisagem.
/// ==========================================================================================================
/// </summary>
public partial class RoboDeMacaco : Node
{
	/// <summary>Quanto esperar o pacote da fera antes de desistir. O palco abre o corpo aos 10 s.</summary>
	private const double Paciencia = 45;

	/// <summary>
	/// ============================ O OBTURADOR TEM QUE CABER O ROTEIRO, E ISSO FOI PAGO ============================
	/// A primeira versao tirou oito quadros a 0,4 s (3,2 s) e a foto "depois" saiu aos ~3,6 s -- e a
	/// cena do Oozaru troca o corpo no beat **4,0 s** (`Cinematicas.Oozaru`, `Efeito.Assumir`). Resultado:
	/// oito checagens vermelhas dizendo *"o sprite nao trocou"* sobre um desenho que estava certo e
	/// ainda nao tinha chegado a vez dele. Uma bancada impaciente e um relatorio de defeito inventado.
	///
	/// Catorze quadros a 0,5 s cobrem 7 s -- a cena inteira com folga --, e o "depois" ainda espera
	/// <see cref="RespiroDepoisDaCena"/> em cima disso. O numero nao e chutado: ele e conferido contra o
	/// proprio catalogo em <see cref="ContarOInstanteDaTroca"/>, que reprova se alguem alongar a cena.
	/// ==========================================================================================================
	/// </summary>
	/// <summary>
	/// QUARENTA QUADROS: 4 s de cena mais DEZESSEIS segundos de macaco solto. A tira longa nao e
	/// capricho -- ela e a janela em que o obturador PROCURA um quadro com a fera na tela (ver
	/// `MacacoNaTela`), e a fera vai e volta.
	///
	/// **E ELA COMECA LONGE, DE PROPOSITO NENHUM**: o Saiyajin de povoamento luta com Onda de Ki, e o
	/// cerebro dele mantem distancia (`circular`/`recuar`). Medido: quando a lua o pega ele ja esta a
	/// ~19 tiles do host, ou seja FORA de uma tela de 1920 no zoom 2 (que cobre 15). Ele volta quando
	/// escolhe o host como presa -- e e esse quadro que a tira espera. Vinte segundos e menos de um
	/// decimo dos 300 s da forma.
	/// </summary>
	private const int QuadrosDaCena = 40;

	private const double EntreQuadros = 0.5;

	/// <summary>
	/// Quanto o "depois" espera DEPOIS da tira -- pra a foto 3 ser o mundo, e nao o fim da cena.
	///
	/// MEIO SEGUNDO E NAO TRES, e o corte tem motivo MEDIDO: o macaco solto CACA (`PresaDaFera`) e a
	/// camera segue o HOST. Com respiro de 3 s a fera estava em x=2132 numa tela de 1920 -- fora de
	/// quadro --, e a foto 4 saiu de um pedaco de grama. Ver a checagem "esta na tela" em
	/// <see cref="DePerto"/>, que e o que transforma esse acidente em linha vermelha.
	///
	/// A conta que sobra e a que importa: a cena troca o corpo aos 4,0 s, a tira cobre 5,0 s e a foto
	/// sai aos ~5,5 s -- um segundo e meio de macaco DEPOIS da troca, e antes de ele sair andando.
	/// </summary>
	private const double RespiroDepoisDaCena = 0.5;

	private readonly List<string> _falhas = [];
	private int _oks;

	private double _t;
	private int _passo;
	private bool _acabou;
	private double _espera = 4.0;

	/// <summary>O que cada corpo da zona era ANTES -- id -> folha do corpo de forma (vazio = nenhuma).</summary>
	private readonly Dictionary<int, string> _folhaAntes = [];

	/// <summary>O corpo que o servidor disse ter virado fera, e em que forma.</summary>
	private int _idDaFera;
	private FormaOozaru _formaDaFera = FormaOozaru.Nao;
	private bool _pacoteChegou;

	private static GameClient? C => GameClient.Instance;

	private void Conferir(bool ok, string oque)
	{
		if (ok) { _oks++; GD.Print("[macaco]   ok    " + oque); }
		else { _falhas.Add(oque); GD.PrintErr("[macaco]   FALHA " + oque); }
	}

	public override void _Ready()
	{
		// O PACOTE DA FERA E O RELOGIO DESTA BANCADA. Ele e o mesmo `S2C.Oozaru` que faz o boneco
		// trocar de folha -- ou seja, escutar aqui e escutar exatamente o que o desenho escuta.
		if (C is { } cli)
			cli.OozaruMudou += (id, forma, primeira, degrau) =>
			{
				if (_pacoteChegou || forma == FormaOozaru.Nao) return;
				_pacoteChegou = true;
				_idDaFera = id;
				_formaDaFera = forma;
				GD.Print($"[macaco] o servidor anunciou a fera: corpo {id} -> {forma} "
					   + $"(estreia={primeira}, cena={degrau})");
			};
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ESTE MUNDO E MEU? Mesma recusa do `RoboDeFera` e pelo mesmo motivo escrito la: com a porta
		// tomada o `--host` nao vira servidor nenhum e o cliente entra no mundo DA OUTRA SESSAO -- e
		// esta bancada faz um macaco de dez metros nascer no meio do planeta de quem estiver jogando.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[macaco] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo "
					  + "e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
			return;
		}

		if (C is not { Connected: true } || World.Instancia == null) return;

		_t += delta;
		if (_t < _espera) return;
		_t = 0;

		switch (_passo)
		{
			case 0: OQueOOlhoVaiJulgar(); break;
			case 1: Antes(); break;
			case 2: EsperarALua(); return;    // ele se adianta sozinho quando o pacote chegar
			default: Durante(); break;
		}
	}

	/// <summary>
	/// O ENUNCIADO ANTES DA PRIMEIRA FOTO. Mesma regra do `RoboDeFera`: escrever a expectativa DEPOIS
	/// do resultado deixa qualquer foto ser lida como confirmacao do que quer que ela mostre.
	/// </summary>
	private void OQueOOlhoVaiJulgar()
	{
		GD.Print("[macaco] --     1-antes  : um Saiyajin de tamanho de gente a uns seis tiles do host.");
		GD.Print("[macaco] --     2-durante: a cena -- e no fim dela um MACACO, varias vezes maior que");
		GD.Print("[macaco] --                o corpo do host, sem cabelo e sem roupa (`Oozaru.dm:123-139`");
		GD.Print("[macaco] --                remove todo overlay antes de trocar o icone).");
		GD.Print("[macaco] --     3-depois : o macaco em pe no mundo, DEPOIS de a cena acabar -- e e");
		GD.Print("[macaco] --                esta que reprova o desenho que so existe durante a cena.");
		GD.Print("[macaco] --     Se as tres saírem iguais, o gatilho ligou e o desenho nao.");
		_passo = 1;
		_espera = 1.0;
	}

	// =====================================================================
	// 1. ANTES -- e a tabela de "o que cada corpo era"
	// =====================================================================
	private void Antes()
	{
		foreach ((int id, RemotePlayer corpo) in CorposDaZona())
			_folhaAntes[id] = corpo.GetNodeOrNull<CharacterVisual>("Visual")?.FolhaDoCorpoDaFormaDeTeste
							  ?? "";

		Conferir(_folhaAntes.Count > 0,
				 $"ha corpo alheio na tela pra fotografar ({_folhaAntes.Count})");
		Conferir(!_pacoteChegou,
				 "a foto 1 sai ANTES de qualquer transformacao (senao ela seria o fim da historia)");

		Fotografar("1-antes");
		_passo = 2;
		_espera = 0.5;
	}

	// =====================================================================
	// 2. ESPERAR A LUA -- o servidor abre o corpo do Saiyajin aos 10 s
	// =====================================================================
	private void EsperarALua()
	{
		_espera = PassoDaEspera;

		if (_pacoteChegou)
		{
			Conferir(true, $"o servidor transformou o corpo {_idDaFera} em {_formaDaFera} "
						 + $"({_esperando:0.0} s depois do login) -- pelo `TickDoCeu` de verdade");
			_passo = 3;
			_espera = 0;
			return;
		}

		_esperando += PassoDaEspera;
		if (_esperando <= Paciencia) return;

		Conferir(false, $"o servidor transformou alguem em {Paciencia:0} s "
					  + "(sem pacote de fera nao ha o que fotografar)");
		Fechar();
	}

	private const double PassoDaEspera = 0.25;

	private double _esperando;

	// =====================================================================
	// 3. DURANTE E DEPOIS -- a tira, e o node ao lado dela
	// =====================================================================
	private void Durante()
	{
		int n = _passo - 3;
		_passo++;

		if (n == 0)
		{
			ContarOInstanteDaTroca();

			// NO COMECO DA CENA ELE AINDA E GENTE, e isto e uma checagem e nao uma desculpa: o corpo do
			// macaco entra no beat `Assumir` (4,0 s), e um desenho que trocasse ANTES estaria estragando
			// a propria cena -- o clarao e o tremor existem pra cobrir a troca, nao pra vir depois dela.
			LerONode("durante", esperaMacaco: false);
			Fotografar("2-durante-0");
			_espera = EntreQuadros;
			return;
		}

		if (n < QuadrosDaCena)
		{
			Fotografar($"2-durante-{n}");

			// ============================ O DEPOIS E O PRIMEIRO QUADRO EM QUE DA PRA VER ============================
			// A foto "depois" era tirada num instante FIXO, depois da tira -- e as duas primeiras rodadas
			// mostraram por que isso nao serve: o macaco solto sai andando atras de presa (`PresaDaFera`)
			// e a camera segue o HOST, entao aos 5,5 s ele ja estava em x=2238 numa tela de 1920. As
			// checagens do node passavam (o desenho existia!) e a foto saia de grama.
			//
			// Agora o obturador PROCURA o quadro: o primeiro em que o corpo ja e o macaco E ainda esta
			// dentro da tela. Isso nao afrouxa nada -- se nenhum quadro servir, a linha do fim reprova.
			// ==================================================================================================
			// TRES FOTOS BOAS E NAO UMA, e o motivo e o que se viu nas rodadas: o quadro em que a fera
			// entra na tela costuma ser o quadro em que ela ESTA BATENDO -- poeira, balao de fala e o
			// corpo do outro por cima. Uma foto so entrega o que a sorte der; tres separadas por 2,5 s
			// entregam ao menos uma silhueta limpa. As afirmacoes ficam so na primeira: elas sao sobre
			// o node, e repetir o mesmo teste tres vezes inflaria o placar sem medir nada a mais.
			if (_depois < FotosDoDepois && n >= _proximoDepois && MacacoNaTela())
			{
				_depois++;
				_proximoDepois = n + 5;
				ODepois(_depois);
			}
			else if (_depois == 0) NarrarAProcura(n);

			_espera = EntreQuadros;
			return;
		}

		if (n == QuadrosDaCena) { _espera = RespiroDepoisDaCena; return; }

		// E SE NENHUM QUADRO SERVIU, a rodada reprova em vez de entregar foto de grama.
		if (_depois == 0)
		{
			LerONode("depois", esperaMacaco: true);
			Fotografar("3-depois-fora-de-quadro");
			Conferir(false, "houve ao menos um quadro com o macaco DESENHADO e dentro da tela "
						  + "(nenhum serviu: ou o sprite nao trocou, ou ele saiu de quadro antes)");
		}
		Fechar();
	}

	/// <summary>
	/// A CONTRAPROVA QUE ANDA AO LADO DA FOTO: de que folha e o corpo daquele id, agora.
	///
	/// Ela nao substitui a foto (um `FolhaDoCorpoDaFormaDeTeste` certo com o sprite escondido atras do
	/// corpo base ja aconteceu neste projeto) e a foto nao substitui ela -- quem olha uma foto de um
	/// macaco nao sabe dizer se aquele e o corpo do id que o servidor transformou.
	/// </summary>
	/// <summary>Quantas fotos "depois" pegar, e o quadro minimo pra a proxima. Ver a tira em `Durante`.</summary>
	private const int FotosDoDepois = 3;

	private int _depois;
	private int _proximoDepois;

	/// <summary>
	/// JA DA PRA FOTOGRAFAR? -- as duas condicoes juntas: o corpo ja e o macaco E ele esta na tela.
	///
	/// Ela nao AFIRMA nada (quem afirma e o <see cref="ODepois"/>): ela so escolhe o quadro. Sem a
	/// segunda metade a bancada fotografa grama; sem a primeira, fotografa um homem.
	/// </summary>
	private bool MacacoNaTela()
	{
		if (World.Instancia?.CorpoDeTeste(_idDaFera) is not { } corpo
			|| corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { CorpoDaFormaVisivelDeTeste: true })
			return false;

		return NaTela() is { } p && GetViewport().GetVisibleRect().HasPoint(p);
	}

	/// <summary>
	/// ============================ ONDE, NA TELA, O MACACO ESTA DESENHADO ============================
	/// **`GetGlobalTransformWithCanvas()` NAO SERVE AQUI, E ISSO FOI MEDIDO**: com ele a fera ficou
	/// parada em `x = 2177` por vinte quadros seguidos enquanto a foto da tela a mostrava andando --
	/// aquilo era a posicao de MUNDO dela. Vinte quadros, nenhum "dentro da tela", e uma bancada
	/// concluindo que o macaco tinha fugido de quadro sem nunca ter saido.
	///
	/// A conta certa e a da CAMERA, e ela e trivial porque a camera deste jogo segue o corpo do dono:
	/// o local esta SEMPRE no centro da tela, entao a distancia em mundo vezes o zoom, somada ao
	/// centro, e a posicao de tela de qualquer outro corpo.
	/// ==========================================================================================
	/// </summary>
	private Vector2? NaTela()
	{
		if (World.Instancia is not { } mundo || C is not { } cli) return null;
		if (mundo.PosicaoDesenhadaDe(_idDaFera) is not { } fera) return null;
		if (mundo.PosicaoDesenhadaDe(cli.LocalId) is not { } eu) return null;

		int zoom = Math.Max(1, mundo.ZoomDeTeste);
		return (fera - eu) * zoom + GetViewport().GetVisibleRect().Size / 2f;
	}

	/// <summary>
	/// POR QUE ESTE QUADRO NAO SERVIU. Sem esta linha, uma rodada sem foto boa so diz "nao serviu" no
	/// fim -- e as duas causas possiveis (o sprite nao trocou / ele saiu de quadro) pedem consertos
	/// opostos: uma e defeito do jogo, a outra e do obturador.
	/// </summary>
	private void NarrarAProcura(int n)
	{
		if (World.Instancia?.CorpoDeTeste(_idDaFera) is not { } corpo) return;
		Vector2 p = NaTela() ?? Vector2.Zero;
		bool macaco = corpo.GetNodeOrNull<CharacterVisual>("Visual")
					  is { CorpoDaFormaVisivelDeTeste: true };
		Vector2 tela = GetViewport().GetVisibleRect().Size;
		GD.Print($"[macaco] --     quadro {n}: macaco={macaco} tela=({p.X:0},{p.Y:0}) de "
			   + $"({tela.X:0},{tela.Y:0})");
	}

	/// <summary>A foto 3 e a 4 do MESMO quadro, com as afirmacoes do node ao lado (so na primeira).</summary>
	private void ODepois(int qual)
	{
		if (qual == 1) LerONode("depois", esperaMacaco: true);
		Fotografar($"3-depois-{qual}");
		DePerto(qual);
	}

	/// <summary>
	/// O INSTANTE EM QUE O CORPO TROCA, LIDO DO CATALOGO -- e a bancada afirma que o obturador o cobre.
	///
	/// Existe porque a primeira rodada desta bancada reprovou o jogo por impaciencia (ver
	/// <see cref="QuadrosDaCena"/>). Cravar "espere 7 s" conserta a rodada de hoje e volta a mentir no
	/// dia em que alguem alongar a cena; conferir contra o roteiro faz a bancada RECLAMAR nesse dia.
	/// </summary>
	private void ContarOInstanteDaTroca()
	{
		FormaDef? def = _formaDaFera == FormaOozaru.Dourado
			? Catalogo.Def(Oozaru.IdDourado) : Catalogo.Def(Oozaru.IdRegular);
		if (def == null)
		{
			Conferir(false, $"o catalogo tem a entrada da fera `{_formaDaFera}`");
			return;
		}

		Cinematica? cena = Cinematicas.NoDegrau(def, DegrauDeCena.Estreia);

		double troca = -1;
		foreach (Beat b in cena?.Beats ?? [])
			if (b.Faz.HasFlag(Efeito.Assumir)) { troca = b.Em; break; }

		double obturador = QuadrosDaCena * EntreQuadros + RespiroDepoisDaCena;
		GD.Print($"[macaco] --     a cena troca o corpo no beat {troca:0.0} s; a tira cobre "
			   + $"{QuadrosDaCena * EntreQuadros:0.0} s e a foto 3 sai aos ~{obturador:0.0} s");

		Conferir(troca > 0 && troca < obturador,
				 $"o obturador cobre o instante da troca ({troca:0.0} s < {obturador:0.0} s) -- "
			   + "sem isto a bancada reprova o desenho por chegar cedo demais");
	}

	private void LerONode(string quando, bool esperaMacaco)
	{
		if (World.Instancia?.CorpoDeTeste(_idDaFera) is not { } corpo
			|| corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis)
		{
			Conferir(false, $"[{quando}] o corpo {_idDaFera} que virou fera esta na tela");
			return;
		}

		string folha = vis.FolhaDoCorpoDaFormaDeTeste;
		string antes = _folhaAntes.GetValueOrDefault(_idDaFera, "(nao estava na tela)");

		GD.Print($"[macaco] --     [{quando}] corpo {_idDaFera}: folha da forma \"{folha}\" "
			   + $"| corpo base visivel={vis.CorpoBaseVisivelDeTeste} "
			   + $"| corpo da forma visivel={vis.CorpoDaFormaVisivelDeTeste} "
			   + $"| cabelo visivel={vis.TemCabeloVisivelDeTeste} | pose {vis.PoseDoCorpoDaFormaDeTeste}");

		string esperada = _formaDaFera == FormaOozaru.Dourado
			? CorposDeForma.OozaruDourado : CorposDeForma.Oozaru;

		if (!esperaMacaco)
		{
			Conferir(folha != esperada && vis.CorpoBaseVisivelDeTeste,
					 $"[{quando}] no COMECO da cena ele ainda e gente (a troca e do beat `Assumir`, "
				   + $"nao da chegada do pacote) -- folha \"{folha}\", corpo base "
				   + $"visivel={vis.CorpoBaseVisivelDeTeste}");
			return;
		}

		Conferir(folha == esperada,
				 $"[{quando}] o corpo do NPC trocou de folha: \"{antes}\" -> \"{folha}\" "
			   + $"(o macaco e `{esperada.GetFile()}`)");
		Conferir(vis.CorpoDaFormaVisivelDeTeste,
				 $"[{quando}] e o macaco esta DESENHADO (nao so montado)");
		Conferir(!vis.CorpoBaseVisivelDeTeste,
				 $"[{quando}] e o corpo de gente sumiu por baixo dele "
			   + "(`Oozaru.dm:123-125`: o macaco nao veste nada, ele SUBSTITUI o mob)");
		Conferir(!vis.TemCabeloVisivelDeTeste,
				 $"[{quando}] e o cabelo saiu junto (`RemoveHair()` antes da troca de icone)");
	}

	// =====================================================================
	// AS FERRAMENTAS
	// =====================================================================
	/// <summary>Os corpos alheios da zona, por id -- o id sai do nome do node (`Remoto42`).</summary>
	private IEnumerable<(int Id, RemotePlayer Corpo)> CorposDaZona()
	{
		if (GetTree().Root.FindChild("World", true, false) is not Node mundo) yield break;

		var achados = new List<RemotePlayer>();
		Rastelar(mundo, achados);
		foreach (RemotePlayer r in achados)
		{
			string nome = r.Name.ToString();
			if (nome.StartsWith("Remoto", StringComparison.Ordinal)
				&& int.TryParse(nome[6..], out int id))
				yield return (id, r);
		}
	}

	private static void Rastelar(Node n, List<RemotePlayer> achados)
	{
		foreach (Node f in n.GetChildren())
		{
			if (f is RemotePlayer r) achados.Add(r);
			Rastelar(f, achados);
		}
	}

	/// <summary>
	/// A TELA INTEIRA, e nao um recorte em volta do corpo.
	///
	/// As bancadas de COR deste projeto recortam (a pergunta la e "de que cor e este pixel"); a
	/// pergunta daqui e de TAMANHO -- *"o macaco e maior que o homem?"* --, e ela so tem resposta com
	/// os dois corpos no mesmo quadro. Um recorte justo no macaco daria uma foto de um macaco sem
	/// escala nenhuma, que e exatamente o que nao se consegue julgar.
	/// </summary>
	private void Fotografar(string rotulo)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty())
			{
				GD.Print($"[macaco] SEM FOTO em {rotulo} (headless nao renderiza)");
				return;
			}
			string caminho = ProjectSettings.GlobalizePath($"user://macaco-{rotulo}.png");
			img.SavePng(caminho);
			GD.Print($"[macaco] foto {rotulo}: {caminho}");
		}
		catch (Exception e) { GD.Print($"[macaco] sem foto em {rotulo}: {e.Message}"); }
	}

	/// <summary>
	/// ============================ A QUARTA FOTO: O MACACO DE PERTO, E POR QUE ELA PRECISOU EXISTIR ============================
	/// A foto 3 e a tela inteira, e ela responde a pergunta de ESCALA -- mas ela e tirada **de noite**,
	/// que e a unica hora em que este sistema pode acontecer. Na primeira rodada o macaco estava la, do
	/// tamanho certo, e quase nao dava pra ver: pelagem escura sobre grama escura, sem sol nenhum.
	///
	/// Entao saem DUAS imagens do mesmo recorte, e as duas com o nome dizendo o que sao:
	///   * `4-de-perto`  -- os pixels como o jogador os ve, ampliados por vizinho-mais-proximo (nada
	///     inventado entre dois pixels);
	///   * `4-de-perto-clareada` -- a MESMA coisa com o brilho dobrado, so pra o olho achar a silhueta.
	///     Ela nao serve pra julgar COR, e o nome existe pra ninguem a usar pra isso.
	/// ==========================================================================================================
	/// </summary>
	private void DePerto(int qual)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return;

		// LADO GRANDE DE PROPOSITO: o Oozaru tem varias vezes a altura de um homem, e um recorte justo
		// nele deixaria de fora o vizinho de tamanho de gente -- que e a REGUA da foto.
		const int LadoEmTela = 460;

		// E O RECORTE SOBE UM POUCO: a ancora do corpo e o PE (e do pe do Oozaru ate a cabeca vao dez
		// metros de bicho). Centrar no node deixava metade do quadro em grama e cortava a cabeca --
		// que e a parte que responde "isso e um macaco".
		const int SobeEmTela = 120;

		if (NaTela() is not { } pos) return;

		// ============================ E ELE ESTA NA TELA? ============================
		// A pergunta parece boba e nao e: o macaco solto CACA, a camera segue o HOST, e um recorte
		// centrado num corpo que saiu de quadro e CLAMPADO pra dentro da imagem -- ou seja ele produz
		// uma foto perfeita de um pedaco de grama, com log verde e tudo. Aconteceu na primeira rodada.
		// ==========================================================================
		Rect2 tela = GetViewport().GetVisibleRect();
		if (qual == 1)
			Conferir(tela.HasPoint(pos),
					 $"[depois] o macaco esta DENTRO da tela na hora da foto (em {pos.X:0},{pos.Y:0} "
				   + $"de {tela.Size.X:0}x{tela.Size.Y:0}) -- fora dela a foto sai de grama");

		int lado = Mathf.Min(LadoEmTela, Mathf.Min(img.GetWidth(), img.GetHeight()));
		int x = Mathf.Clamp((int)pos.X - lado / 2, 0, img.GetWidth() - lado);
		int y = Mathf.Clamp((int)pos.Y - SobeEmTela - lado / 2, 0, img.GetHeight() - lado);

		Image corte = img.GetRegion(new Rect2I(x, y, lado, lado));
		corte.Convert(Image.Format.Rgba8);

		Image ampliada = Image.CreateEmpty(lado, lado, false, corte.GetFormat());
		ampliada.BlitRect(corte, new Rect2I(0, 0, lado, lado), Vector2I.Zero);
		ampliada.Resize(lado * 3, lado * 3, Image.Interpolation.Nearest);
		ampliada.SavePng(ProjectSettings.GlobalizePath($"user://macaco-4-de-perto-{qual}.png"));

		Image clara = Image.CreateEmpty(lado * 3, lado * 3, false, ampliada.GetFormat());
		clara.BlitRect(ampliada, new Rect2I(0, 0, lado * 3, lado * 3), Vector2I.Zero);
		clara.AdjustBcs(2.4f, 1.2f, 1.0f);
		clara.SavePng(ProjectSettings.GlobalizePath($"user://macaco-4-de-perto-{qual}-clareada.png"));

		GD.Print($"[macaco] foto 4-de-perto-{qual} (3x) e -clareada (brilho 2,4x -- so pra o olho "
			   + "achar a silhueta; nao vale pra julgar cor)");
	}

	private void Fechar()
	{
		_acabou = true;
		GD.Print($"[macaco] ===== {_oks} OK, {_falhas.Count} FALHA(S) -- agora o juiz e o OLHO =====");
		foreach (string f in _falhas) GD.PrintErr("[macaco]   FALHA " + f);
		GetTree().Quit();
	}
}
