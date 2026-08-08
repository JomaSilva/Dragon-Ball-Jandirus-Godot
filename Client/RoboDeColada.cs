using Godot;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA COLADA DE FORMA (`--diagcolada`). Ela existe pra OLHAR, e so pra isso.
///
/// ============================ POR QUE ELA NAO E UM PASSO DO `--diagforma` ============================
/// O `RoboDeForma` ja mede a colada em quatro lugares -- folha, tinta, pose e (desde o slideshow) o
/// quadro andando no tempo. As quatro passaram VERDES enquanto o dono via, na tela, um brilho cinza
/// avancando em camera lenta. Foram 4000+ checagens verdes contra dois defeitos visiveis, e a licao e
/// que numero nao prova aparencia nem cadencia: `s.Modulate` estava certo no node e o material
/// descartava (`Personagem.gdshader` escrevia `COLOR` sem le-lo), e o `Frame` estava "certo" tambem --
/// so que reescalado pro ciclo do corpo.
///
/// Entao esta bancada nao confere campo nenhum. Ela veste a forma pelo caminho do jogo, filma o
/// personagem e monta uma TIRA de quadros consecutivos num PNG so. Quem julga e o olho.
/// ==================================================================================================
///
/// ============================ POR QUE UMA TIRA, E NAO UMA FOTO ============================
/// Os dois defeitos sao de naturezas diferentes e so um deles cabe numa foto parada:
///
///   * A COR (o `god - grey` cinza em vez de verde/rosa) aparece em qualquer quadro solto.
///   * A CADENCIA ("parece um slide show") NAO APARECE em foto nenhuma. Um quadro nao tem
///     velocidade. Precisa de quadros CONSECUTIVOS lado a lado -- se a fagulha muda de tile pra tile,
///     ela anda; se dezesseis tiles saem identicos, e o slideshow, e da pra CONTAR o quanto.
///
/// O intervalo (<see cref="PassoDaTira"/>) e escolhido contra o numero medido nas folhas: a colada
/// `default_south` tem 4 quadros de 0,100 s (ciclo 0,400 s). Amostrando de 0,05 em 0,05 s, um brilho
/// SADIO troca de desenho a cada dois tiles e a tira mostra dois ciclos inteiros; o brilho DOENTE
/// (0,825 s por quadro, o ciclo do corpo parado esticado 8,25x) sai com os dezesseis tiles iguais.
/// A diferenca entre "certo" e "quebrado" e visivel sem ler numero nenhum.
/// ======================================================================================
///
/// COMO RODAR (precisa de janela: no headless o `GetImage` volta vazio e nao ha foto):
///     Godot --path . --host --kiteste --bpteste 3000000 --diagcolada \
///           --raca Saiyan --nome Zx --conta &lt;NOVA&gt;
///
/// As tiras saem em `user://colada-&lt;forma&gt;-&lt;estado&gt;.png`.
/// </summary>
public partial class RoboDeColada : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	/// <summary>Quanto o passo CORRENTE espera antes de rodar. Escrito pelo passo anterior.</summary>
	private double _espera = 1.5;

	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;

	private Node2D? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as Node2D;

	private CharacterVisual? Visual => Corpo?.GetNodeOrNull<CharacterVisual>("Visual");

	private void Nota(string linha) => GD.Print("[colada] " + linha);

	private void Conferir(bool ok, string oque)
	{
		Nota((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	/// <summary>
	/// AS QUATRO LINHAS QUE COLAM ALGUMA COISA, uma forma de cada -- ver `Catalogo.Coladas`.
	///
	/// `legendary` vem primeiro porque e a UNICA que instala duas camadas (`LSSJpowerz` mais a cinza
	/// tingida), ou seja e a unica em que o par "fagulha rapida + brilho lento" pode sair fora de
	/// compasso um do outro. `rose` fecha porque e o segundo consumidor da folha cinza -- se a tinta
	/// so funcionasse pro verde, ele e quem denuncia.
	/// </summary>
	private static readonly string[] Roteiro = ["legendary", "ssg", "blue", "rose"];

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ============================ ESTE MUNDO E MEU? SE NAO FOR, NAO ENCOSTA ============================
		// Copiado do `RoboDeNebulosa`, e pelo mesmo motivo escrito la: a porta 7777 e unica na maquina.
		// Com o `_net.Start` falhando, o `--host` nao vira servidor nenhum e o cliente entra no mundo DA
		// OUTRA SESSAO -- e esta bancada anda com o corpo e troca a forma dele, o que apareceria na tela
		// de quem estivesse jogando ali.
		// ==============================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[colada] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo "
					  + "e de outra sessao. Nada foi forcado. Espere a porta 7777 vagar.");
			return;
		}

		if (C is not { Connected: true } || Corpo is null) return;

		_t += delta;
		if (_t < _espera) return;
		_t = 0;

		// ============================ O ROTEIRO E UMA CONTA, E NAO UM `switch` DE 20 CASOS ============================
		// Sao quatro formas x tres tiras iguais. Escrever doze `case` copiados seria doze lugares pra
		// esquecer de mudar um. Os tres primeiros passos preparam; o resto e o bloco de cinco passos
		// repetido por forma.
		// ==========================================================================================================
		const int Preparo = 2;
		const int PassosPorForma = 18;

		if (_passo < Preparo)
		{
			switch (_passo++)
			{
				case 0: Preparar(); break;
				default: OQueOOlhoVaiJulgar(); break;
			}
			return;
		}

		int i = (_passo - Preparo) / PassosPorForma;
		int dentro = (_passo - Preparo) % PassosPorForma;
		if (i >= Roteiro.Length) { AProvaDaTinta(); return; }
		_passo++;

		string forma = Roteiro[i];
		switch (dentro)
		{
			// PARADO E O PIOR CASO, e e por isso que ele vem primeiro. `default_south` do corpo e
			// `1,1,1,30` (ciclo 3,300 s) e o `30` do ultimo quadro sozinho e 91% do ciclo: era dele que
			// vinha o esticamento de 8,25x. Andando os dois ciclos sao 0,800 s e a razao e 1:1 -- o
			// "frame rate bom" do relato do dono, ou seja o caso que ja funcionava.
			case 0: Vestir(forma); break;
			case 1: Filmar(forma, "parado"); break;
			// O PAR LIGADO/DESLIGADO, e ele e a unica coisa nesta bancada que responde "de que COR e o
			// brilho". A tira nao responde: no enquadramento cabem grama, pele, cabelo e roupa, e o
			// brilho e translucido por cima de tudo isso. Eu li a tira do `rose` como "nao ha rosa
			// nenhum" e a do `blue` como "ha um borrao claro" -- duas leituras de olho sobre a mesma
			// duvida. A subtracao das duas fotos isola exatamente os pixels que esta camada pinta.
			case 2: Alternar(forma, false); break;
			case 3: Fotografar(forma, "off"); Alternar(forma, true); break;
			case 4: Fotografar(forma, "on"); Isolar(forma, ColadasDeForma.FolhaAmeacadora); break;
			// O SEGUNDO PAR SO ACONTECE ONDE HA MAIS DE UMA CAMADA -- hoje, so no Legendary. Ele isola a
			// `god - grey` da fagulha. Onde nao ha o que isolar, o `Isolar` deixa `_isolada` nulo e estes
			// tres passos nao fazem nada (o par seria identico ao de cima e a checagem reprovaria por um
			// motivo que nao existe).
			case 5: if (_isolada != null) Alternar(forma, false); break;
			case 6: if (_isolada != null) { Fotografar(forma, "off"); Alternar(forma, true); } break;
			case 7: if (_isolada != null) { Fotografar(forma, "on"); _isolada = null; } break;
			case 8: Andar(true); break;
			case 9: Filmar(forma, "andando"); break;
			case 10: Andar(false); break;

			// ============================ AS DUAS OUTRAS POSES, E SO NA PRIMEIRA FORMA ============================
			// Elas sao o avesso do caso parado e o irmao gemeo dele: `attack_south` do corpo tem UM quadro
			// (ciclo 0,100 s -- ali a colada corria 4x RAPIDO demais) e `flight_south` tem o mesmo
			// `1,1,1,30` do `default` (3,300 s -- o mesmo esticamento de 8,25x). Nenhuma das duas foi
			// citada por ninguem, e as duas caem no mesmo defeito.
			//
			// E sao as poses em que cabelo, roupa e rabo mais tem chance de sair de compasso com o corpo,
			// porque nem toda peca as tem (13 roupas nao tem ataque) e a que nao tem cai numa pose
			// EMPRESTADA, que congela no quadro 0 -- ver o `sync` no `_Process`.
			//
			// So na primeira forma do roteiro (`legendary`), que e a unica com DUAS coladas: se houver
			// desencontro entre camadas, e nela que aparece. Repetir nas quatro so custaria rodada.
			// ==================================================================================================
			case 11: if (i == 0) Tecla("attack", true); break;
			case 12: if (i == 0) { Filmar(forma, "atacando"); Tecla("attack", false); } break;
			case 13: if (i == 0) Tecla("voar", true); break;
			case 14: if (i == 0 && !_semVoo) Filmar(forma, "voando"); break;
			case 15: if (i == 0 && !_semVoo) Andar(true); break;
			case 16: if (i == 0 && !_semVoo) { Filmar(forma, "voando-andando"); Andar(false); } break;
			default: if (i == 0 && !_semVoo) Tecla("voar", false); break;
		}
	}

	/// <summary>
	/// Um toque de tecla pelo `Input`, como o <see cref="Andar"/> -- e pelo mesmo motivo: e o caminho
	/// que o jogador usa. O `voar` e ALTERNANCIA (um toque liga, outro desliga), entao ele precisa do
	/// par press/release pra o toque acontecer; o `attack` fica segurado enquanto se filma.
	/// </summary>
	private void Tecla(string acao, bool aperta)
	{
		// ============================ O VOO NAO SAI POR TECLA AQUI, E ELE ME ENSINOU ISSO ============================
		// A primeira versao fazia `ActionPress("voar")` e o log saiu com `corpo: default_east` -- o
		// personagem nunca decolou, e eu quase escrevi "a pose de voo esta certa" sobre uma foto de
		// alguem de pe. O motivo: o `LocalPlayer` le o voo com `IsActionJustPressed` (`:591`), que so e
		// verdadeiro no quadro em que a tecla DESCE; um press+release no mesmo passo nao e visto.
		//
		// Entao vai pelo mesmo caminho que a tecla usa uma linha abaixo -- `SendHabilidade("voar")`,
		// igual ao `RoboDeDoisCorpos:182`. E o pedido de verdade, sem depender do relogio do teclado.
		// =========================================================================================================
		if (acao == "voar")
		{
			// E PRECISA DE `--vooteste` NA LINHA DE COMANDO. O `AlternarVoo` do servidor recusa quem tem
			// maestria de Ki abaixo de 50 (`GameServer.Voo.cs:148`), e nenhum verb de admin concede
			// NIVEL de skill -- so marco. Sem a flag, o pedido volta com "voce ainda nao sente o proprio
			// Ki o bastante pra voar" e o corpo fica no chao: a tira de voo sairia com o boneco de pe e
			// seria lida como "a pose de voo esta certa". Melhor recusar em voz alta.
			if (aperta && Array.IndexOf(OS.GetCmdlineArgs(), "--vooteste") < 0)
			{
				Nota("  --     SEM `--vooteste`: o servidor recusa o voo (maestria de Ki < 50) e nao ha "
				   + "tira de voo. Rode com a flag pra esta parte existir.");
				_semVoo = true;
				_espera = 0.1;
				return;
			}
			if (aperta) C?.SendHabilidade("voar");
			// A SUBIDA TEM RAMPA e o sprite so vira `flight_*` acima de uma altura (ver `EfeitosDaAltura`).
			// Filmar cedo demais pega o corpo ainda de pe -- que foi exatamente o engano de cima.
			_espera = aperta ? 2.0 : 0.3;
			return;
		}
		if (aperta) Input.ActionPress(acao); else Input.ActionRelease(acao);
		_espera = aperta ? 0.1 : 0.2;
	}

	/// <summary>
	/// RE-DISPARA O SOCO A CADA QUADRO DA TIRA. A pose de ataque dura `_cadencia * PesoDoGolpe`
	/// (~0,33 s -- `LocalPlayer:794`) e a tira leva 0,800 s: sem re-disparar, metade dos tiles sairia
	/// com o corpo ja de pe, e a metade em pe e justamente a que eu confundiria com "o ataque nao
	/// pegou". E precisa de SOLTAR entre um e outro porque o gatilho e `IsActionJustPressed` (`:773`).
	/// </summary>
	private void ManterOSoco()
	{
		// ============================ E POR EVENTO, NAO POR `ActionPress` ============================
		// `Input.ActionPress` marca a acao como apertada NO QUADRO EM QUE ESTE `_Process` roda, e o
		// `IsActionJustPressed` do `LocalPlayer` (`:773`) so e verdadeiro no quadro da descida. Esta
		// bancada e filha do `Boot` e o `LocalPlayer` mora dentro do `World`: quem roda primeiro nao e
		// escolha minha. Quando o `LocalPlayer` roda antes, ele nunca ve a descida -- e foi o que
		// aconteceu: duas rodadas inteiras com a tecla "apertada" e o corpo em `default_east`, e eu quase
		// escrevi "a pose de ataque esta certa" olhando uma foto de alguem parado.
		//
		// `ParseInputEvent` injeta um evento de verdade, processado ANTES dos `_Process` do quadro --
		// o mesmo caminho do teclado. E precisa SOLTAR antes, senao nao ha descida pra ver.
		// ==========================================================================================
		Input.ParseInputEvent(new InputEventAction { Action = "attack", Pressed = false });
		Input.ParseInputEvent(new InputEventAction { Action = "attack", Pressed = true });
	}

	// =====================================================================
	// 1b. A PROVA DA TINTA -- o controle que separa "a tinta" de "a folha"
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ESTE BLOCO EXISTE ============================
	/// Os quatro pares de cima deixaram um empate: as DUAS camadas que quase nao pintam nada
	/// (`legendary` isolada e `rose`) sao as duas que carregam `Modulate`, e sao tambem as duas que
	/// usam a folha `god - grey`. As duas que pintam forte (`god`, `god blue`) tem `Modulate` BRANCO e
	/// outra folha. Os dois suspeitos estao perfeitamente confundidos, e nenhuma foto de forma real
	/// separa um do outro -- o catalogo nunca poe tinta na `god` nem deixa a `god - grey` sem tinta.
	///
	/// Entao a bancada fabrica os dois casos que faltam, na MESMA camada, com o corpo parado no mesmo
	/// lugar: a `god - grey` do Rose com a tinta BRANCA (o neutro da multiplicacao) e com VERMELHO
	/// PURO. Tres pares da mesma camada, so a tinta mudando:
	///
	///   * se o BRANCO pintar um brilho cinza forte e o rosa nao pintar nada, o reu e a TINTA -- o
	///     `Modulate` esta sendo perdido ou aplicado duas vezes entre o node e o pixel;
	///   * se o BRANCO tambem nao pintar nada, o reu e a FOLHA (import, alfa, caminho com espaco).
	///
	/// O VERMELHO PURO existe pra distinguir "perdido" de "aplicado duas vezes": perdido da um brilho
	/// cinza; duas vezes da o mesmo vermelho, so que mais escuro (o quadrado de um canal em 1,0
	/// continua 1,0 e o dos outros dois vai a zero).
	/// ==================================================================================
	/// </summary>
	private void AProvaDaTinta()
	{
		switch (_prova++)
		{
			case 0: Vestir("rose"); break;
			case 1: Pintar("rose", Colors.White, "branco (o neutro da multiplicacao)"); break;
			case 2: Alternar("rose", false); break;
			case 3: Fotografar("rose", "off"); Alternar("rose", true); break;
			case 4: Fotografar("rose", "on"); break;
			case 5: Pintar("rose", new Color(1, 0, 0), "VERMELHO puro"); break;
			case 6: Alternar("rose", false); break;
			case 7: Fotografar("rose", "off"); Alternar("rose", true); break;
			case 8: Fotografar("rose", "on"); break;
			default: Base(); Fechar(); break;
		}
	}

	private int _prova;

	/// <summary>O servidor recusou o voo (sem `--vooteste`): os passos de voo nao acontecem.</summary>
	private bool _semVoo;

	/// <summary>O rotulo que entra no nome do arquivo e no log -- escrito pelo <see cref="Pintar"/>.</summary>
	private string _rotuloDaProva = "";

	private void Pintar(string forma, Color tinta, string porque)
	{
		foreach (AnimatedSprite2D s in CamadasColadas(forma)) s.Modulate = tinta;
		_rotuloDaProva = $"tinta-{tinta.ToHtml(false)}";
		Nota($"  --     PROVA: a colada do `{forma}` repintada de #{tinta.ToHtml(false)} -- {porque}");
		_espera = 0.2;
	}

	// =====================================================================
	// 1. PREPARAR A CENA PRA A FOTO SER COMPARAVEL
	// =====================================================================
	private void Preparar()
	{
		// O CEU PARADO. Mesmo motivo do `RoboDeNebulosa`: o clima natural sorteia sozinho e ja fez duas
		// rodadas sairem com a grama em cores diferentes. Metade da diferenca entre duas fotos era o
		// tempo, e nao o efeito. `admin_clima` recusa "Limpo" de proposito, entao pede-se o clima mais
		// discreto na forca minima -- que da no mesmo e usa a porta que ja existe.
		C?.SendVerbo("admin_clima", "Neblina|0.05");

		Nota($"  --     zoom={World.Instancia?.ZoomDeTeste}  janela={GetViewport().GetVisibleRect().Size}  "
		   + $"lado do recorte={LadoDoCorte()} px de tela");
		_espera = 1.0;
	}

	/// <summary>
	/// O QUE O OLHO VAI JULGAR, dito ANTES de qualquer foto sair.
	///
	/// Nao e checagem: e o enunciado. Um relatorio que so mostrasse PNGs deixaria quem lesse sem saber
	/// o que era pra ver -- e foi assim que a nebulosa teve poeira de cinematica lida como rampa de cor
	/// por duas rodadas. Escrever a expectativa antes do resultado e o que impede a foto de ser lida
	/// como confirmacao do que quer que ela mostre.
	/// </summary>
	private void OQueOOlhoVaiJulgar()
	{
		Nota("  --     1. CADENCIA: na tira `parado`, a colada tem que TROCAR DE DESENHO a cada ~2 tiles.");
		Nota("  --        Dezesseis tiles identicos e o slideshow (1,21 quadro/s) -- o defeito relatado.");
		Nota("  --     2. COR: `legendary` VERDE e `rose` ROSA. `ssg` laranja e `blue` azul (sem tinta).");
		Nota("  --     3. SINCRONIA: cabelo, roupa e rabo tem que continuar no quadro do CORPO -- eles nao");
		Nota("  --        sao efeito, sao o boneco recortado. Na tira `andando` o passo tem que ser um so.");
		_espera = 0.5;
	}

	// =====================================================================
	// 2. VESTIR PELO CAMINHO DO JOGO
	// =====================================================================
	/// <summary>
	/// Veste a forma pelo <see cref="World.AoMudarForma"/> no degrau `Nenhuma` -- o mesmo funil que o
	/// `AIdaEAVolta` do `--diagforma` usa.
	///
	/// ============================ POR QUE NAO E O `admin_forma` DA REDE ============================
	/// O `RoboDeNebulosa` manda pela rede de proposito, porque a pergunta DELE e "o `Definir` chega pelo
	/// caminho de producao?". Aqui a pergunta ja e outra -- "que pixel sai?" --, e o caminho da rede
	/// cobraria caro por nada: `AdminForcarForma` nao suprime cinematica (e escrito la que nao suprime
	/// de proposito), entao cada uma das quatro formas custaria a cena de estreia inteira, e a poeira,
	/// a cratera e o anel de choque dela ficariam POR CIMA do que se quer olhar. Foi literalmente o
	/// engano da primeira rodada da nebulosa.
	///
	/// `DegrauDeCena.Nenhuma` cai no `VestirAFormaSemCena`, que e o caminho de producao de quem ja
	/// domina a forma (maestria acima de 50%) -- e ele chama o `vis.ColadasDaForma(def)` que esta
	/// bancada existe pra fotografar. Nao e pintura direta como o `Posar` do `--diagforma`.
	/// ==========================================================================================
	/// </summary>
	private void Vestir(string id)
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo) return;
		int meuId = C?.LocalId ?? 0;
		if (meuId == 0) { Conferir(false, "a bancada esta conectada"); return; }

		mundo.AoMudarForma(meuId, Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase),
						   Jandirus.Core.Forms.Catalogo.Rede(id),
						   Jandirus.Core.Forms.DegrauDeCena.Nenhuma);

		// AS CAMADAS NASCERAM? Nao e a checagem desta bancada (o `--diagforma` ja cobre isso), e sim a
		// garantia de que a tira seguinte nao vai fotografar um corpo SEM colada nenhuma e ser lida como
		// "o brilho sumiu". Uma tira em branco tem que ser reprovada aqui, e nao interpretada la.
		int quantas = Visual?.ColadasDeTeste ?? 0;
		Conferir(quantas > 0, $"[{id}] a forma instalou {quantas} camada(s) colada(s) -- ha o que fotografar");

		// A FOLHA E A TINTA COMO O NODE FICOU, ao lado da foto. Elas ja sao medidas pelo `--diagforma`
		// e la passam verdes -- o valor delas AQUI e servir de contraprova a foto: quando o pixel na
		// tela nao corresponde ao que esta escrito no node, o defeito esta ENTRE os dois (material,
		// mistura, ordem de desenho) e nao no catalogo. Foi assim que o `COLOR = c` apareceu.
		foreach ((string folha, Color tinta) in Visual?.ColadasNoCorpoDeTeste ?? [])
			Nota($"  --     [{id}] folha `{folha.GetFile()}` com tinta #{tinta.ToHtml(false)}");
		_espera = 0.4;
	}

	private void Base()
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo) return;
		int meuId = C?.LocalId ?? 0;
		if (meuId == 0) return;
		ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);
		mundo.AoMudarForma(meuId, b, b, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
	}

	/// <summary>
	/// Anda pelo `Input`, e nao empurrando a posicao do node -- mexer no node brigaria com a
	/// reconciliacao do servidor. Mesma razao escrita no `RoboDeNebulosa.Andar`.
	/// </summary>
	private void Andar(bool anda)
	{
		if (anda) Input.ActionPress("move_right"); else Input.ActionRelease("move_right");
		// MEIO SEGUNDO PRA O PASSO ASSENTAR. A pose so vira `walk_*` depois que o corpo se move de
		// verdade; filmar no instante da tecla pegaria metade da tira ainda em `default_*`, e as duas
		// metades tem cadencias diferentes -- que e justamente o que se esta medindo.
		_espera = anda ? 0.5 : 0.3;
	}

	// =====================================================================
	// 3. A TIRA
	// =====================================================================
	/// <summary>Quantos quadros a tira guarda. 4x4, e o 16 sai do <see cref="PassoDaTira"/>.</summary>
	private const int QuadrosDaTira = 16;

	/// <summary>
	/// O INTERVALO ENTRE DOIS QUADROS DA TIRA. 0,05 s, e o numero e escolhido contra a folha:
	///
	///   * a colada tem quadros de 0,100 s -- amostrar na metade disso da DOIS tiles por desenho, que
	///     e o que faz a troca aparecer sem depender de fase (amostrar exatamente em 0,100 s poderia
	///     travar em fase e repetir o mesmo desenho a tira inteira, mentindo do lado contrario);
	///   * 16 x 0,05 = 0,800 s, que sao DOIS ciclos inteiros da colada parada (0,400 s). A tira mostra
	///     o desenho voltando ao comeco, que e a prova de que ela esta rodando o ciclo DELA;
	///   * e 0,800 s e menos do que UM quadro do defeito (0,825 s). No mundo doente a tira inteira
	///     nao consegue conter uma troca sequer -- por construcao.
	/// </summary>
	private const double PassoDaTira = 0.05;

	private readonly List<Image> _tira = [];

	/// <summary>A sequencia de quadros de CADA camada da pilha, um por tile da tira.</summary>
	private readonly Dictionary<string, List<string>> _porCamada = [];

	/// <summary>
	/// O lado do recorte em pixels de TELA, derivado do zoom em vez de cravado.
	///
	/// 40 px de MUNDO em volta do corpo: o boneco tem 32, entao ele ocupa ~80% da largura e ainda
	/// sobra franja de chao pra a cor do brilho ser julgada contra alguma coisa. Cravar pixel de tela
	/// daria enquadramentos diferentes em `--zoom 2` e `--zoom 3`, e duas rodadas do dono deixariam de
	/// ser comparaveis entre si por um motivo que nao tem nada a ver com o efeito.
	/// </summary>
	private static int LadoDoCorte() => 40 * Math.Max(1, World.Instancia?.ZoomDeTeste ?? 2);

	/// <summary>
	/// O quadro ja desenhado, recortado em volta do corpo. Irmao do `RoboDeNebulosa.Recorte` e pelo
	/// mesmo motivo: o corpo NEM SEMPRE esta no centro da tela (a camera para nas beiradas do mapa),
	/// entao o recorte sai da posicao de tela do node.
	/// </summary>
	private Image? Recorte()
	{
		if (Corpo is not { } centro) return null;
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;

		Vector2 p = centro.GetGlobalTransformWithCanvas().Origin;
		int lado = Mathf.Min(LadoDoCorte(), Mathf.Min(img.GetWidth(), img.GetHeight()));
		int x = Mathf.Clamp((int)p.X - lado / 2, 0, img.GetWidth() - lado);
		int y = Mathf.Clamp((int)p.Y - lado / 2, 0, img.GetHeight() - lado);
		Image corte = img.GetRegion(new Rect2I(x, y, lado, lado));
		// FORMATO UNICO ANTES DE COLAR: o `BlitRect` nao converte, e o viewport pode devolver um
		// formato que nao e o da folha montada. Sem esta linha a tira sai preta em algumas maquinas e
		// perfeita em outras, que e o pior tipo de defeito de bancada.
		corte.Convert(Image.Format.Rgba8);
		return corte;
	}

	/// <summary>
	/// Junta <see cref="QuadrosDaTira"/> recortes CONSECUTIVOS num PNG so. O passo se repete a si
	/// mesmo (`_passo--`) ate encher, como o `EsperarANuvem` do `RoboDeNebulosa`.
	/// </summary>
	private void Filmar(string forma, string estado)
	{
		if (estado == "atacando") ManterOSoco();
		if (Recorte() is { } c) _tira.Add(c);

		// ============================ A LEGENDA DA TIRA: TODA CAMADA, TILE A TILE ============================
		// Nao e a prova (a prova e a tira), e sim a legenda dela -- e ela responde as DUAS perguntas de
		// uma vez, que e o motivo de anotar a pilha INTEIRA e nao so a colada:
		//
		//   * um tile parecido com o vizinho deixa a duvida entre "a colada nao andou" e "ela andou pra
		//     um desenho parecido". A sequencia de quadros separa as duas leituras;
		//   * e a sincronia (`corpo`, `cabelo`, `roupa`, `rabo`) e um DESENCONTRO entre sequencias, nao
		//     um valor. Ela so aparece pondo as linhas uma debaixo da outra: a do corpo e as das partes
		//     dele tem que subir juntas, e so a da colada tem direito de andar noutro passo.
		//
		// LIDO DA ARVORE, sem API nova no `CharacterVisual` -- aquele arquivo esta com outra sessao
		// dentro dele agora, e a pergunta nao precisa de campo nenhum: os nodes sao filhos do Visual e
		// cada um ja carrega a folha, a animacao e o quadro que esta desenhando.
		// ================================================================================================
		foreach (Node n in Visual?.GetChildren() ?? [])
		{
			if (n is not AnimatedSprite2D s || s.SpriteFrames is not { } f) continue;
			string nome = f.ResourcePath.GetFile().GetBaseName();
			if (!_porCamada.TryGetValue(nome, out List<string>? lista))
				_porCamada[nome] = lista = [];
			lista.Add(s.Visible ? $"{s.Frame}" : "-");
		}

		if (_tira.Count < QuadrosDaTira) { _passo--; _espera = PassoDaTira; return; }

		// ============================ EFEITO NENHUM PODE SUMIR NUMA POSE ============================
		// A tira do `atacando` saiu com a fagulha do Legendary aparecendo em um tile sim, outro nao, e a
		// do `voando` saiu SEM fagulha nenhuma -- o Legendary decolava e perdia o efeito que o define.
		// A causa estava no `CharacterVisual.Escolher`: a escada de saidas so tentava nome COM sufixo de
		// direcao, e a `LSSJpowerz` tem UMA animacao chamada `default`. Sem casar nada, o `Aplicar`
		// esconde a camada.
		//
		// A checagem e sobre a TIRA inteira e nao sobre um instante, que e o que a torna capaz de pegar o
		// pisca-pisca: sumir metade dos quadros e tao defeito quanto sumir todos, e um `Conferir` tirado
		// num quadro so passaria verde na metade das rodadas.
		// ========================================================================================
		foreach ((string nome, List<string> seq) in _porCamada)
		{
			if (nome is not ("LSSJpowerz" or "god" or "god blue" or "god - grey")) continue;
			int sumiu = seq.Count(q => q == "-");
			Conferir(sumiu == 0,
				$"[{forma}/{estado}] a colada `{nome}` NAO some em nenhum dos {seq.Count} quadros "
			  + $"(sumiu em {sumiu})");
		}

		Montar($"colada-{forma}-{estado}");
		Nota($"  --     [{forma}/{estado}] pose da colada: "
		   + string.Join(",", Visual?.PosesDasColadasDeTeste ?? []) + $" | corpo: {Visual?.PoseDeTeste}");
		foreach ((string nome, List<string> seq) in _porCamada)
			Nota($"  --     [{forma}/{estado}] {nome,-22} {string.Join(" ", seq)}");
		_tira.Clear();
		_porCamada.Clear();
		_espera = 0.3;
	}

	// =====================================================================
	// 4. O PAR LIGADO/DESLIGADO, E A SUBTRACAO
	// =====================================================================
	/// <summary>
	/// AS CAMADAS COLADAS DESTE CORPO, achadas pela FOLHA que elas carregam.
	///
	/// ============================ POR QUE ASSIM, E NAO UM CAMPO NOVO NO `CharacterVisual` ============================
	/// Aquele arquivo esta sendo editado por outra sessao agora, e a pergunta nao precisa de campo
	/// nenhum: o `Catalogo.Coladas` diz QUAIS folhas a forma pediu e o `ColadasDeForma.CaminhoDa`
	/// traduz cada uma em `res://`. Uma camada cuja folha esta nessa lista E uma colada -- e o mesmo
	/// raciocinio derivado do `CharacterVisual.EhColada`, so que de fora.
	///
	/// NAO SERVE `vis.ColadasDaForma(null)` pra apagar, e vale guardar por que: ele chama
	/// `Aplicar(force: true)`, que zera o `_relogio`. O corpo saltaria pro quadro 0 da pose -- e a
	/// subtracao das duas fotos traria o CORPO inteiro mudando junto, que e exatamente o sinal que se
	/// quer isolar. Mexer so no `Visible` deixa todo o resto imovel.
	/// ==========================================================================================================
	/// </summary>
	/// <param name="soFolha">
	/// Quando dado, so a camada DESTA folha entra -- e o que separa as duas do Legendary. Sem isso o
	/// par do Legendary media a soma da fagulha `LSSJpowerz` (opaca, `#d3ff26`) com o brilho `god - grey`
	/// (translucido, 21% de alfa medio): a fagulha domina a subtracao e um `god - grey` que nao pintasse
	/// nada passaria despercebido debaixo dela. Foi exatamente o que quase aconteceu.
	/// </param>
	private List<AnimatedSprite2D> CamadasColadas(string forma, string? soFolha = null)
	{
		var r = new List<AnimatedSprite2D>();
		if (Visual is not { } vis) return r;

		var folhas = new HashSet<string>();
		foreach (Jandirus.Core.Forms.Colada c in
				 Jandirus.Core.Forms.Catalogo.Coladas(Jandirus.Core.Forms.Catalogo.Def(forma)))
		{
			string caminho = ColadasDeForma.CaminhoDa(c.Folha);
			if (soFolha == null || caminho == soFolha) folhas.Add(caminho);
		}

		foreach (Node n in vis.GetChildren())
			if (n is AnimatedSprite2D s && s.SpriteFrames is { } f && folhas.Contains(f.ResourcePath))
				r.Add(s);
		return r;
	}

	/// <summary>Qual folha o par corrente esta isolando, ou nulo pra "todas".</summary>
	private string? _isolada;

	/// <summary>
	/// Pede o par ISOLADO numa folha, quando ha o que isolar. Nulo quando a forma so tem uma camada --
	/// isolar a unica camada daria o mesmo par de cima com outro nome.
	/// </summary>
	private void Isolar(string forma, string folha)
	{
		_isolada = CamadasColadas(forma).Count > 1 && CamadasColadas(forma, folha).Count > 0
			? folha : null;
		if (_isolada != null)
			Nota($"  --     [{forma}] segundo par, isolando SO a folha `{folha.GetFile()}`");
		_espera = 0.2;
	}

	private void Alternar(string forma, bool aceso)
	{
		foreach (AnimatedSprite2D s in CamadasColadas(forma, _isolada)) s.Visible = aceso;
		// UM QUADRO ENTRE AS DUAS FOTOS, e nao mais: `GetTexture().GetImage()` devolve o quadro JA
		// desenhado, ou seja o ANTERIOR. Com folga maior a grama balanca, o clima anda e a subtracao
		// traz aquilo junto -- o engano que o `RoboDeNebulosa` documenta ter cometido. E parado o corpo
		// fica 3,0 s no ultimo quadro do `default_*`, entao um quadro de gap nao o move.
		_espera = 0;
	}

	private Image? _off;

	private void Fotografar(string forma, string lado)
	{
		if (Recorte() is not { } c) { Nota($"  --     [{forma}] sem foto (headless nao renderiza)"); return; }
		if (lado == "off") { _off = c; return; }

		if (_off == null) return;
		Image on = c;

		// ============================ A SUBTRACAO, E O QUE ELA MEDE ============================
		// A colada e TRANSLUCIDA (medido na folha: o miolo do `god - grey` cobre bem, a franja quase
		// nada), entao ela nao substitui o pixel -- ela o desloca. `on - off` e exatamente esse
		// deslocamento, e o SINAL dele e a resposta que se procura: deslocar pro verde e um brilho
		// verde; deslocar por igual nos tres canais e um brilho CINZA, que era a queixa do dono.
		// ======================================================================================
		int n = 0; double dr = 0, dg = 0, db = 0; int mudou = 0;
		var dif = Image.CreateEmpty(on.GetWidth(), on.GetHeight(), false, Image.Format.Rgba8);
		for (int y = 0; y < on.GetHeight(); y++)
		for (int x = 0; x < on.GetWidth(); x++)
		{
			Color a = _off.GetPixel(x, y), b = on.GetPixel(x, y);
			float rr = b.R - a.R, gg = b.G - a.G, bb = b.B - a.B;
			if (Mathf.Abs(rr) + Mathf.Abs(gg) + Mathf.Abs(bb) < 0.02f) { dif.SetPixel(x, y, Colors.Black); continue; }
			mudou++;
			dr += rr; dg += gg; db += bb; n++;
			// AMPLIADA 4x: a diferenca de um brilho translucido e de poucos niveis por canal, e uma
			// imagem de diferenca crua sai preta -- ninguem consegue julgar cor nela. O ganho e o
			// mesmo nos tres canais, entao ele nao INVENTA matiz: so torna visivel o que ja estava la.
			dif.SetPixel(x, y, new Color(Mathf.Clamp(Mathf.Abs(rr) * 4, 0, 1),
										 Mathf.Clamp(Mathf.Abs(gg) * 4, 0, 1),
										 Mathf.Clamp(Mathf.Abs(bb) * 4, 0, 1)));
		}

		// O TRIO NUMA IMAGEM SO: desligado | ligado | diferenca x4. Lado a lado porque a diferenca
		// sozinha nao diz ONDE ela cai no boneco, e o par sozinho nao diz o QUANTO.
		int lado3 = on.GetWidth();
		var folha = Image.CreateEmpty(lado3 * 3 * 2, lado3 * 2, false, Image.Format.Rgba8);
		int k = 0;
		foreach (Image img in new[] { _off, on, dif })
		{
			Image t = (Image)img.Duplicate();
			t.Convert(Image.Format.Rgba8);
			t.Resize(lado3 * 2, lado3 * 2, Image.Interpolation.Nearest);
			folha.BlitRect(t, new Rect2I(0, 0, lado3 * 2, lado3 * 2), new Vector2I(k++ * lado3 * 2, 0));
		}
		string quem = _rotuloDaProva.Length > 0 ? _rotuloDaProva
					: _isolada == null ? "par" : "par-grey";
		folha.SavePng(ProjectSettings.GlobalizePath($"user://colada-{forma}-{quem}.png"));

		Nota($"  --     [{forma}/{quem}] a colada mexeu em {mudou} px de {on.GetWidth() * on.GetHeight()}; "
		   + $"deslocamento medio RGB({dr / Math.Max(n, 1) * 255:+0;-0},{dg / Math.Max(n, 1) * 255:+0;-0},"
		   + $"{db / Math.Max(n, 1) * 255:+0;-0})");
		Nota($"  ok     par salvo em user://colada-{forma}-{quem}.png (off | on | diferenca x4)");

		// A COLADA TEM QUE MUDAR ALGUMA COISA. Zero aqui significa camada instalada, com folha, com
		// tinta, com pose e com o quadro andando -- e sem UM pixel na tela. E o modo de falhar desta
		// familia inteira (o `Modulate` escrito no vazio, a API do sigilo de BP orfa), e nenhuma
		// checagem que olhe estado de node consegue enxerga-lo.
		Conferir(mudou > 0, $"[{forma}/{quem}] a colada DESENHA na tela (mexeu em {mudou} px)");
		_off = null;
		_espera = 0.2;
	}

	/// <summary>O lado de cada tile no PNG final. 4x4 x 240 = 960, que cabe numa tela pra olhar.</summary>
	private const int LadoDoTile = 240;

	private void Montar(string nome)
	{
		try
		{
			if (_tira.Count == 0) { Nota($"  --     [{nome}] sem foto (headless nao renderiza)"); return; }

			const int Colunas = 4;
			int linhas = Mathf.CeilToInt(_tira.Count / (float)Colunas);
			Image folha = Image.CreateEmpty(Colunas * LadoDoTile, linhas * LadoDoTile, false,
											Image.Format.Rgba8);

			for (int k = 0; k < _tira.Count; k++)
			{
				Image t = _tira[k];
				// NEAREST, e nao e detalhe: em arte de pixel qualquer interpolacao INVENTA tons
				// intermediarios. Esta tira existe pra alguem responder "este brilho e verde ou cinza?"
				// -- uma ampliacao suavizada misturaria o brilho com o chao e daria a resposta errada.
				t.Resize(LadoDoTile, LadoDoTile, Image.Interpolation.Nearest);
				folha.BlitRect(t, new Rect2I(0, 0, LadoDoTile, LadoDoTile),
							   new Vector2I(k % Colunas * LadoDoTile, k / Colunas * LadoDoTile));
			}

			string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
			folha.SavePng(caminho);
			Nota($"  ok     tira salva em {caminho}  ({Colunas}x{linhas} tiles, "
			   + $"{PassoDaTira:0.00}s entre tiles, esquerda->direita e de cima pra baixo)");
		}
		catch (Exception e) { Nota($"  --     [{nome}] sem tira: {e.Message}"); }
	}

	private void Fechar()
	{
		_acabou = true;
		Input.ActionRelease("move_right");
		GD.Print(_falhas.Count == 0
			? "[colada] ===== TUDO OK -- agora OLHE as tiras ====="
			: $"[colada] ===== {_falhas.Count} FALHA(S) =====\n[colada]   "
			  + string.Join("\n[colada]   ", _falhas));
	}
}
