using Godot;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DE OLHAR (`--diagolhada`). Ela nao confere campo nenhum: ela TIRA FOTO.
///
/// ============================ POR QUE ELA EXISTE, SEPARADA DAS OUTRAS ============================
/// Tres dos cinco pedidos do dono desta rodada sao COR e NOME NA TELA:
///
///   * o cabelo `SSjFP` tem que trocar aos 100% de maestria (o Grade 4);
///   * o Blue tem que ser ciano vivo e o Blue Evolution o azul escuro que era do Blue;
///   * o Legendary tem que ser verde AMARELADO, e o C-Type tem que continuar dourado.
///
/// Numero verde nao prova nenhum dos tres. O `--diagforma` mede a tinta ARMADA no material e passa
/// verde -- ele passou verde durante toda a vida do `COLOR = c * COLOR`, que elevava cada canal ao
/// QUADRADO e transformava o SSG em preto puro em 40% dos pixels. A tinta estava certa no node o
/// tempo todo; o pixel e que era outro. Por isso esta bancada le o VIEWPORT e mais nada.
///
/// E o `--diagcolada` tambem nao serve: ele enquadra o CORPO INTEIRO pra julgar cadencia de
/// animacao, e a 40 px de mundo o cabelo tem 10 px de altura na foto -- cabe julgar "tem brilho",
/// nao cabe julgar "este azul e marinho ou e ciano". Aqui o recorte e a CABECA, ampliada em
/// vizinho-mais-proximo, porque as duas perguntas do dono sao sobre o cabelo e sobre a PUPILA.
/// ==============================================================================================
///
/// ============================ O QUE ELA FOTOGRAFA, E POR QUE NESTA ORDEM ============================
/// A ordem do roteiro nao e decorativa -- ela poe LADO A LADO na tira de contato exatamente os
/// pares que o dono disse estarem trocados:
///
///   1. `ssj1` cru       ) o par do Grade 4: mesma forma, mesma maestria em tudo menos no bit.
///   2. `ssj1` dominado  ) Se a folha nao mudar de `Hair_GokuSSj` pra `Hair_GokuSSjFP`, e aqui.
///   3. `blue`           ) o par dos azuis: *"o cabelo atual do blue e pra ser do evolved/royale"*.
///   4. `blue_evolution` ) O 4 TEM que sair mais escuro que o 3 -- eles trocaram de dono.
///   5. `legendary`      ) o par das linhagens: mesma cor de cabelo (verde amarelado), NOMES
///   6. `primal_legendary`) diferentes. A foto julga a cor; o log julga o nome.
///   7. `c_type`         ) o controle: *"o type C ta certo"*. Se ele mudou, eu quebrei algo.
///
/// O `c_type` fecha de proposito. Ele e a unica entrada desta lista que NAO era pra mudar, e uma
/// bancada que so fotografa o que mudou nao consegue distinguir "consertei o Blue" de "repintei a
/// escada inteira de ciano".
/// ================================================================================================
///
/// COMO RODAR (precisa de JANELA: no headless o `GetImage` volta vazio e nao ha foto nenhuma):
///     Godot --path . --host --rede 7801 --kiteste --bpteste 3000000 --diagolhada \
///           --raca Saiyan --nome Zx --conta &lt;NOVA&gt;
///
/// As fotos saem em `user://olhada-*.png`, e a tira de contato em `user://olhada-CONTATO.png`.
/// </summary>
public partial class RoboDeOlhada : Node
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

	private static void Nota(string linha) => GD.Print("[olhada] " + linha);

	private void Conferir(bool ok, string oque)
	{
		Nota((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	/// <summary>
	/// Uma pose = uma forma + o bit de maestria + o rotulo que vira nome de arquivo.
	///
	/// O BIT E UM CAMPO DA POSE e nao um passo separado porque ele muda a FOLHA do cabelo, e nao
	/// so a tinta: `Catalogo.SufixoDoCabeloDe` devolve `SSjFP` no lugar de `SSj` quando ele e
	/// verdadeiro (`Formas.cs:2247`). Uma pose que nao carregasse o bit fotografaria sempre o mesmo
	/// penteado e o par 1/2 nao provaria nada.
	/// </summary>
	private readonly record struct Pose(string Id, bool Dominada, string Rotulo);

	private static readonly Pose[] Roteiro =
	[
		new("ssj1",             false, "1-ssj1-cru"),
		new("ssj1",             true,  "2-ssj1-grade4"),
		new("blue",             false, "3-blue"),
		new("blue_evolution",   false, "4-blue-evolution"),
		new("legendary",        false, "5-legendary-comum"),
		new("primal_legendary", false, "6-legendary-primal"),
		new("c_type",           false, "7-c-type-controle"),
	];

	/// <summary>Os recortes de cabeca na ordem do roteiro, pra a tira de contato do fim.</summary>
	private readonly List<Image> _contato = [];

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ============================ ESTE MUNDO E MEU? SE NAO FOR, NAO ENCOSTA ============================
		// Copiado do `RoboDeColada:84` e pelo mesmo motivo escrito la: com a porta tomada o `--host`
		// nao vira servidor nenhum e o cliente entra no mundo DA OUTRA SESSAO -- e esta bancada troca
		// a forma do corpo, o que apareceria na tela de quem estivesse jogando ali. Ha outras sessoes
		// editando este repo agora; forcar seria estragar a rodada alheia pra salvar a minha.
		// ==============================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[olhada] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo "
					  + "e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
			return;
		}

		if (C is not { Connected: true } || Corpo is null) return;

		_t += delta;
		if (_t < _espera) return;
		_t = 0;

		const int Preparo = 3;
		const int PassosPorPose = 3;

		if (_passo < Preparo)
		{
			switch (_passo++)
			{
				case 0: Preparar(); break;
				case 1: OQueOOlhoVaiJulgar(); break;
				// O FUNDO, TIRADO NA FORMA BASE E ANTES DE TUDO. Ver `SoOCorpo`. Depois desta linha o
				// corpo nao anda mais: o recorte sai da posicao de TELA dele, entao um passo dado entre
				// o fundo e as poses deslocaria a mascara e ela apagaria o cabelo em vez da grama.
				default:
					_fundo = Recorte();
					Nota("  --     fundo (forma base) guardado pra a mascara");
					// A LUZ SE CONFERE NO FUNDO, e nao numa foto de forma: aqui o corpo esta na base,
					// sem aura nenhuma acendendo -- ou seja e a luz do MUNDO que esta sendo medida, que
					// e a que polui todas as sete fotos seguintes.
					if (_fundo is { } f) ConferirALuz(f);
					break;
			}
			return;
		}

		int i = (_passo - Preparo) / PassosPorPose;
		int dentro = (_passo - Preparo) % PassosPorPose;

		if (i >= Roteiro.Length) { Depois(); return; }
		_passo++;

		Pose p = Roteiro[i];
		switch (dentro)
		{
			case 0: Vestir(p); break;
			// UM PASSO INTEIRO ENTRE VESTIR E FOTOGRAFAR, e ele nao e desperdicio: o
			// `GetTexture().GetImage()` devolve o quadro JA DESENHADO, ou seja o anterior a este
			// `_Process`. Fotografar no mesmo passo do `Vestir` pegaria a forma de ANTES -- e a foto
			// sairia perfeita, so que da pose errada, que e o defeito mais dificil de desconfiar.
			case 1: break;
			default: Fotografar(p); break;
		}
	}

	// =====================================================================
	// 1. PREPARAR
	// =====================================================================
	private void Preparar()
	{
		// O CEU PARADO, mesmo motivo do `RoboDeColada.Preparar`: o clima natural sorteia sozinho e
		// muda a luz da cena. Metade da diferenca entre duas fotos seria o TEMPO, e nao a tinta --
		// e a pergunta desta rodada e "este azul mudou?", que so se responde entre fotos comparaveis.
		C?.SendVerbo("admin_clima", "Neblina|0.05");

		// ============================ E O RELOGIO PARADO TAMBEM -- ESTA LINHA E O CONSERTO ============================
		// Sem ela esta bancada media a hora do dia e nao a cor do cabelo. Ela rodou duas vezes com o
		// mesmo codigo: na primeira o C-Type saiu dourado, na segunda o pixel MAIS CLARO do recorte
		// inteiro era `#1e274d` e o mesmo dourado media quase preto -- tinha anoitecido entre uma
		// rodada e outra. Um dia dura 24 minutos (`Ceu.SegundosPorDia`), entao isso nao e azar raro:
		// e uma rodada em cada duas. Ver `GameServer.AdminMeioDia`.
		// ==========================================================================================================
		C?.SendVerbo("admin_meio_dia");

		Conferir(Visual?.TemCabeloDeTeste ?? false,
				 "este corpo TEM cabelo -- num careca nenhuma destas fotos prova coisa alguma");

		Nota($"  --     zoom={World.Instancia?.ZoomDeTeste}  janela={GetViewport().GetVisibleRect().Size}");
		_espera = 1.5;
	}

	/// <summary>
	/// ESTA CLARO O BASTANTE PRA JULGAR COR?
	///
	/// Nao e zelo: e a checagem que faltava. As duas rodadas de noite salvaram sete fotos cada uma e
	/// escreveram `ok foto ...` em todas as quatorze -- o log inteiro verde sobre imagens onde nao
	/// dava pra distinguir dourado de verde. Uma bancada que nao sabe dizer "esta escuro demais" nao
	/// protege quem le do erro que ela mesma acabou de cometer.
	///
	/// O LIMIAR SAI DA MEDIDA E NAO DO GOSTO: de noite o pixel mais claro do recorte foi `#1e274d`
	/// (o canal mais forte em 77); de dia a pele e o cabelo passam de 200. Cento e vinte fica no meio
	/// do vao, longe das duas.
	/// </summary>
	private void ConferirALuz(Image foto)
	{
		int maior = 0;
		for (int y = 0; y < foto.GetHeight(); y++)
			for (int x = 0; x < foto.GetWidth(); x++)
			{
				Color c = foto.GetPixel(x, y);
				maior = Math.Max(maior, (int)(Math.Max(c.R, Math.Max(c.G, c.B)) * 255));
			}

		Conferir(maior >= 120,
				 $"ha LUZ pra julgar cor (canal mais forte do recorte = {maior}/255; de noite deu 77) "
			   + "-- abaixo disto as fotos nao valem e o `admin_meio_dia` nao pegou");
	}

	/// <summary>
	/// O QUE O OLHO VAI JULGAR, dito ANTES de qualquer foto sair.
	///
	/// Nao e checagem: e o enunciado, e ele existe porque foto nao tem legenda. Escrever a
	/// expectativa depois do resultado deixa qualquer foto ser lida como confirmacao do que quer
	/// que ela mostre -- e foi assim que a nebulosa teve poeira de cinematica lida como rampa de cor
	/// por duas rodadas (`RoboDeColada.OQueOOlhoVaiJulgar`).
	/// </summary>
	private static void OQueOOlhoVaiJulgar()
	{
		Nota("  --     1. GRADE 4: a foto 2 tem que ter OUTRO PENTEADO que a 1 (folha `SSjFP`), e nao");
		Nota("  --        so outra cor. Mesma folha nas duas = o bit de maestria nao chegou no cabelo.");
		Nota("  --     2. AZUIS: a foto 3 (Blue) e CIANO CLARO; a 4 (Evolution) e o azul ESCURO que");
		Nota("  --        ela tinha antes. Se a 3 continuar marinho, o `AzulDoCabeloDivino` nao pegou.");
		Nota("  --     3. LENDARIOS: 5 e 6 sao o MESMO verde amarelado (limao), nao verde-bandeira.");
		// ============================ ESTE AVISO E O QUE ME FALTOU NA PRIMEIRA LEITURA ============================
		// Eu li a tira e conclui "o C-Type ficou verde, eu repintei a escada inteira". Estava errado, e
		// o erro e reincidente por construcao: a linha Legendary cola uma camada VERDE por cima do
		// corpo todo (`Catalogo.Coladas` -> `Ameacadora`), entao o C-Type SEMPRE parece esverdeado na
		// tela mesmo com o cabelo dourado intacto embaixo. Quem olhar esta tira amanha vai tropecar no
		// mesmo lugar se ninguem avisar.
		// =======================================================================================================
		Nota("  --     4. CONTROLE: a foto 7 e o C-Type, que o dono confirmou que JA ESTAVA CERTO.");
		Nota("  --        ARMADILHA: ela sai VERDE-AMARELADA e isso NAO quer dizer que o cabelo mudou --");
		Nota("  --        a linha Legendary cola uma camada verde (`Ameacadora`) sobre o corpo inteiro.");
		Nota("  --        Quem responde de verdade e a checagem `C-Type e SSJ1 pedem a MESMA folha/tinta`.");
		Nota("  --     5. PUPILA: nas fotos `posse-*`, branca com a IA dirigindo e verde de volta.");
		Nota("");
	}

	// =====================================================================
	// 2. VESTIR -- pelo caminho do jogo, e sem cinematica
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE NAO E O `admin_forma` DA REDE ============================
	/// Mesmo motivo do `RoboDeColada.Vestir`: `AdminForcarForma` nao suprime cinematica (e escrito la
	/// que nao suprime de proposito), entao cada uma das sete poses custaria a cena de estreia inteira
	/// -- e a poeira, a cratera e o clarao dela ficariam POR CIMA do cabelo, que e o unico assunto
	/// destas fotos.
	///
	/// `DegrauDeCena.Nenhuma` cai no `World.VestirAFormaSemCena`, que e o caminho de producao de quem
	/// ja domina a forma, e e ele quem chama o `vis.MarcarFormaDominada` e o `vis.VestirCabeloDaForma`
	/// (`World.cs:2481`) -- os dois metodos que esta rodada veio olhar.
	///
	/// A PROVA REAL DO GRADE 4 (com o servidor, a maestria de verdade e a aba) esta no <see
	/// cref="AProvaDoGrade4"/>. Esta pintura aqui responde so "que pixel sai".
	/// ==========================================================================================
	/// </summary>
	private void Vestir(Pose p)
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo) return;
		int meuId = C?.LocalId ?? 0;
		if (meuId == 0) { Conferir(false, "a bancada esta conectada"); return; }

		ushort b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);

		// PASSA PELA BASE ENTRE UMA POSE E OUTRA. Sem isto o `AoMudarForma` recebe `de == para` em
		// duas poses seguidas da mesma forma (o par `ssj1` cru/dominado) e pode tratar como "nada
		// mudou" -- e a foto 2 sairia identica a 1 por um motivo que nao e o defeito procurado.
		mundo.AoMudarForma(meuId, b, b, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.AoMudarForma(meuId, b, Jandirus.Core.Forms.Catalogo.Rede(p.Id),
						   Jandirus.Core.Forms.DegrauDeCena.Nenhuma, p.Dominada);

		_espera = 0.35;
	}

	// =====================================================================
	// 3. A FOTO
	// =====================================================================
	/// <summary>
	/// O lado do recorte da CABECA, em pixels de mundo. Derivado do zoom e nao cravado em pixel de
	/// tela pelo mesmo motivo do `RoboDeColada.LadoDoCorte`: cravar daria enquadramentos diferentes
	/// em `--zoom 2` e `--zoom 3` e duas rodadas do dono deixariam de ser comparaveis.
	///
	/// ============================ DEZESSEIS, E A PRIMEIRA RODADA E QUEM DIZ POR QUE ============================
	/// Comecou em 12 com <see cref="AlturaDaCabeca"/> 8 -- ou seja o recorte ia de `centro-14` a
	/// `centro-2`. O cabelo cabia inteiro e a foto parecia otima, mas os OLHOS ficavam colados na
	/// borda de baixo: nas duas fotos de posse deu pra ver que havia marcas escuras ali e nada mais.
	/// Metade do pedido 5 do dono ("a pupila verde volta, deixa de ser branca") ficou sem resposta com
	/// duas fotos salvas com sucesso -- que e o pior jeito de falhar, porque o log diz `ok`.
	///
	/// Com 16 e altura 6 o recorte vai de `centro-14` a `centro+2` e a pupila (~`centro-3`) cai a
	/// tres quartos da altura, longe de qualquer borda.
	/// ========================================================================================================
	/// </summary>
	private const int LadoDaCabeca = 16;

	/// <summary>Quanto a cabeca sobe do centro do corpo, em px de mundo. Ver <see cref="LadoDaCabeca"/>.</summary>
	private const int AlturaDaCabeca = 6;

	/// <summary>Ampliacao da foto. VIZINHO-MAIS-PROXIMO -- ver <see cref="Ampliar"/>.</summary>
	private const int Ampliacao = 12;

	private int Zoom => Math.Max(1, World.Instancia?.ZoomDeTeste ?? 2);

	/// <summary>
	/// O quadro ja desenhado, recortado em volta da CABECA. Irmao do `RoboDeColada.Recorte` e pelo
	/// mesmo motivo escrito la: o corpo NEM SEMPRE esta no centro da tela (a camera para nas beiradas
	/// do mapa), entao o recorte sai da posicao de TELA do node e nao do centro do viewport.
	/// </summary>
	private Image? Recorte()
	{
		if (Corpo is not { } centro) return null;
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;

		Vector2 pos = centro.GetGlobalTransformWithCanvas().Origin;
		int lado = Mathf.Min(LadoDaCabeca * Zoom, Mathf.Min(img.GetWidth(), img.GetHeight()));
		int x = Mathf.Clamp((int)pos.X - lado / 2, 0, img.GetWidth() - lado);
		int y = Mathf.Clamp((int)pos.Y - AlturaDaCabeca * Zoom - lado / 2, 0, img.GetHeight() - lado);

		Image corte = img.GetRegion(new Rect2I(x, y, lado, lado));
		// FORMATO UNICO ANTES DE COLAR: o `BlitRect` da tira de contato nao converte, e o viewport
		// pode devolver um formato que nao e o da folha montada. Sem esta linha a tira sai preta em
		// algumas maquinas e perfeita em outras -- o pior tipo de defeito de bancada.
		corte.Convert(Image.Format.Rgba8);
		return corte;
	}

	/// <summary>
	/// VIZINHO-MAIS-PROXIMO, e nao bilinear. Interpolacao suave INVENTA cores entre dois pixels
	/// vizinhos -- e a pergunta desta rodada e literalmente "de que cor e este pixel". Um cabelo
	/// ciano ao lado de um fundo escuro ampliado com suavizacao produz uma franja de azul-marinho
	/// que nao existe na folha, e ela e exatamente a cor que o dono reclamou.
	/// </summary>
	private static Image Ampliar(Image src, int vezes)
	{
		Image copia = Image.CreateEmpty(src.GetWidth(), src.GetHeight(), false, src.GetFormat());
		copia.BlitRect(src, new Rect2I(0, 0, src.GetWidth(), src.GetHeight()), Vector2I.Zero);
		copia.Resize(src.GetWidth() * vezes, src.GetHeight() * vezes, Image.Interpolation.Nearest);
		return copia;
	}

	/// <summary>
	/// O MESMO RECORTE COM O CORPO NA FORMA BASE. Escrito no preparo, lido pelo <see cref="SoOCorpo"/>.
	/// </summary>
	private Image? _fundo;

	/// <summary>
	/// PINTA DE CINZA NEUTRO TUDO QUE NAO MUDOU DESDE A FORMA BASE -- ou seja, apaga o CENARIO.
	///
	/// ============================ ISTO NAO E ENFEITE: E O ERRO QUE EU QUASE COMETI ============================
	/// O chao desta zona e GRAMA, e grama e verde. Na primeira rodada eu abri a tira de contato e li
	/// os paineis 5, 6 e 7 como "verde, verde, verde" -- e o 7 e o C-Type, que e DOURADO e que o dono
	/// mandou explicitamente nao mexer (*"o type C ta certo"*). Eu estava a um passo de escrever que
	/// tinha repintado a escada inteira de verde. Abrindo a foto 7 sozinha, em tamanho grande, o
	/// dourado e obvio.
	///
	/// O erro nao foi meu olho: foi o enquadramento. Um cabelo verde-limao no meio de grama verde nao
	/// e julgavel, e o pedido do dono nesta rodada e exatamente sobre um cabelo verde-limao. Entao a
	/// bancada tira o cenario da frente em vez de pedir pra quem le "ignorar o fundo".
	///
	/// O CINZA MEDIO (`808080`) e escolhido, e nao o preto nem a magenta: preto escurece a percepcao
	/// de qualquer cor vizinha e magenta puxa a leitura do verde pro amarelo -- os dois falsificariam
	/// justamente a pergunta. Cinza medio e o fundo neutro de quem julga cor.
	///
	/// ============================ POR QUE DIFERENCA CONTRA A BASE, E NAO UM RECORTE MENOR ============================
	/// Porque o cabelo nao e um retangulo. Qualquer caixa que pegue a franja inteira pega grama nos
	/// quatro cantos, e qualquer caixa sem grama corta o cabelo. A diferenca contra a base separa
	/// pelo que a FORMA mudou, que e a pergunta de verdade.
	///
	/// E ela e honesta consigo mesma: se a aura da forma acendesse a grama em volta, o fundo tambem
	/// mudaria e a mascara falharia -- so que falharia VISIVELMENTE (a foto sairia sem cinza nenhum),
	/// e nao em silencio. A rodada anterior mediu que nao acende: os cantos das fotos 3, 4 e 7 saem no
	/// mesmo verde-oliva nas tres.
	/// ============================================================================================================
	/// </summary>
	private Image SoOCorpo(Image atual)
	{
		if (_fundo is not { } b || b.GetWidth() != atual.GetWidth() || b.GetHeight() != atual.GetHeight())
			return atual;

		// TOLERANCIA, e nao igualdade exata: o chao tem dithering e a compressao do viewport mexe no
		// ultimo bit. Igualdade exata deixaria pontinhos de grama passando pela mascara, e pontinho de
		// grama numa foto de cabelo verde e exatamente o rui­do que se quer tirar.
		const float Tol = 0.06f;
		var cinza = new Color(0.5f, 0.5f, 0.5f);

		Image saida = Image.CreateEmpty(atual.GetWidth(), atual.GetHeight(), false, Image.Format.Rgba8);
		int iguais = 0;
		for (int y = 0; y < atual.GetHeight(); y++)
			for (int x = 0; x < atual.GetWidth(); x++)
			{
				Color c = atual.GetPixel(x, y), f = b.GetPixel(x, y);
				bool igual = Mathf.Abs(c.R - f.R) < Tol && Mathf.Abs(c.G - f.G) < Tol
						  && Mathf.Abs(c.B - f.B) < Tol;
				if (igual) iguais++;
				saida.SetPixel(x, y, igual ? cinza : c);
			}

		// QUANTO SOBROU, DITO EM VOZ ALTA. Se a mascara apagar quase tudo (o corpo nao mudou) ou quase
		// nada (a aura mexeu no cenario), a foto seguinte nao presta -- e quem le tem que saber disso
		// pelo log, e nao descobrir olhando uma imagem que parece normal.
		int total = atual.GetWidth() * atual.GetHeight();
		Nota($"  --     mascara: {total - iguais}/{total} px do CORPO ({100.0 * (total - iguais) / total:0}%)");
		return saida;
	}

	private void Fotografar(Pose p)
	{
		string folha = Visual?.CabeloDeTeste ?? "";
		(Vector3 Tinta, int Modo)? t = Visual?.TintaDoCabeloDeTeste;

		// A FOLHA E A TINTA COMO O NODE FICOU, AO LADO DA FOTO -- e nao no lugar dela. Elas ja sao
		// medidas pelo `--diagforma` e la passam verdes: o valor delas AQUI e ser CONTRAPROVA. Quando
		// o pixel na tela nao corresponde ao que esta escrito no node, o reu esta ENTRE os dois
		// (material, mistura, ordem de desenho) e nao no catalogo -- foi assim que o `COLOR = c`
		// apareceu, e nenhuma medicao de campo teria achado aquilo.
		Nota($"  --     [{p.Rotulo}] folha `{folha.GetFile()}`"
		   + (t is { } tt ? $"  tinta=({tt.Tinta.X:0.###},{tt.Tinta.Y:0.###},{tt.Tinta.Z:0.###}) modo={tt.Modo}" : "  (sem cabelo)"));

		if (Recorte() is not { } corte)
		{
			Nota($"  --     [{p.Rotulo}] sem foto (headless nao renderiza)");
			_espera = 0.2;
			return;
		}

		// AS DUAS FOTOS, e as duas servem. A crua e a prova de que aquilo e o jogo de verdade (com
		// chao, sombra e o resto); a mascarada e a unica em que a COR do cabelo e julgavel. Guardar so
		// a mascarada esconderia um defeito de composicao (cabelo por cima da orelha, contorno errado)
		// atras do proprio cinza.
		string caminho = ProjectSettings.GlobalizePath($"user://olhada-{p.Rotulo}.png");
		Ampliar(corte, Ampliacao).SavePng(caminho);

		Image so = SoOCorpo(corte);
		_contato.Add(so);
		Ampliar(so, Ampliacao).SavePng(ProjectSettings.GlobalizePath($"user://olhada-{p.Rotulo}-SOCORPO.png"));

		Nota($"  ok     foto {caminho} (+ a mascarada)");
		_espera = 0.2;
	}

	// =====================================================================
	// 4. DEPOIS DAS SETE POSES
	// =====================================================================
	private int _depois;

	private void Depois()
	{
		switch (_depois++)
		{
			// ============================ A PUPILA VEM DO SERVIDOR, E NAO DE UM `AoMudarPosse` DAQUI ============================
			// A primeira versao chamava `World.AoMudarPosse(meuId, true)` no cliente e fotografava. As
			// duas fotos sairam com a pupila VERDE e a subtracao acusou ZERO pixel de diferenca -- com
			// o uniform do olho lendo branco `(0,99 0,99 0,99)` no mesmo instante. Nao era bug do jogo:
			// o servidor manda `SemRedeas` em todo snapshot (30 Hz) e o `AoReceberSnapshot` chama
			// `AoMudarPosse(id, false)` de volta. A autoridade desfazia a mentira do cliente antes do
			// obturador, que e exatamente o que ela tem que fazer.
			//
			// Entao a posse e pedida DE VERDADE. A furia se arma sozinha: o primeiro tique dentro de
			// uma forma lendaria nao dominada arma o prazo de controle (`TickDaFuriaLendaria`, passo 2)
			// e a maestria ZERO da o piso de 6 s (`Controle(0) = max(6, D*0²)`). Basta entrar no
			// `legendary` e esperar. Pra devolver as redeas usa-se o gatilho que o proprio motor
			// documenta -- dominar a forma DENTRO da posse solta o corpo na hora.
			// ================================================================================================================
			case 0: C?.SendVerbo("admin_forma", "0|legendary"); _espera = 2.0; break;
			case 1: EsperarAFuria(); break;
			case 2: SemAura(); break;
			case 3: SemColadas(); break;
			case 4: FotoDaPupila("posse-1-sem-redeas", branca: true);
					C?.SendVerbo("admin_dominar", ""); _espera = 2.0; break;
			case 5: SemAura(); break;
			case 6: SemColadas(); break;
			case 7: FotoDaPupila("posse-2-com-redeas", branca: false); break;

			// ============================ A PROVA DO C-TYPE VEM ANTES DO SERVIDOR, E ISSO E OBRIGATORIO ============================
			// Ela ficava no fim e reprovava com 96% de diferenca sobre duas fotos de um corpo de CABELO
			// PRETO -- ou seja da forma base. O motivo nao e o C-Type: e que o bloco do Grade 4 logo
			// abaixo poe o personagem em `ssj1` PELO SERVIDOR, e dali em diante a ficha do servidor diz
			// "ssj1" enquanto o `Vestir` daqui pinta por conta propria. A primeira reconciliacao de
			// snapshot desfaz a pintura, e as duas fotos da prova saem do estado que o SERVIDOR quer,
			// nao do que a bancada pediu.
			//
			// Aqui em cima o servidor ainda nao foi mexido, entao a pintura e a unica verdade na tela.
			// A regra geral: tudo que pinta pelo cliente tem que rodar ANTES de qualquer verb que mude
			// a forma de verdade.
			// ================================================================================================================
			case 8: AProvaDoCType(0); break;
			case 9: SemAura(); break;
			case 10: FotoSolta("ctype-A-ssj1-sem-aura"); break;
			case 11: AProvaDoCType(1); break;
			case 12: SemAura(); break;
			case 13: AProvaDoCType(2); break;

			// A PROVA REAL DO GRADE 4: servidor, maestria de verdade, aba aberta.
			case 14: AProvaDoGrade4(); break;
			case 15: C?.SendVerbo("admin_dominar", ""); _espera = 1.5; break;
			case 16: AbrirAba("Forms"); break;
			case 17: TelaCheia("aba-formas-grade4"); AbrirAba(Verbos.Admin); break;
			// ROLAR ATE A LINHA LENDARIA. Na primeira rodada esta foto saiu com a aba de admin aberta e
			// CERTA -- e inutil: o painel monta as escadas de cima pra baixo e a Saiyajin sozinha (dez
			// degraus, tres fileiras) empurra a Legendary pra fora da janela. A foto provava que o
			// painel abre, e a pergunta do dono e quantos botoes tem a quarta faixa.
			case 18: Rolar(395); break;
			case 19: TelaCheia("painel-admin-linha-legendary"); break;
			case 20: OsNomes(); break;
			default: TiraDeContato(); Fechar(); break;
		}
	}

	/// <summary>
	/// ESCONDE AS COLADAS DA FORMA -- as camadas de brilho que a linha Legendary instala por cima do
	/// corpo (`Catalogo.Coladas`: `PoderLendario` + `Ameacadora` tingida de verde).
	///
	/// ============================ SEM ISTO NAO HA FOTO DA PUPILA, E EU TENTEI ============================
	/// As duas coladas sao ANIMADAS e cobrem a cabeca inteira. Entre os dois quadros do par de posse
	/// elas trocam de desenho sozinhas, entao a subtracao que devia isolar o olho devolveu 176 pixels
	/// espalhados por todo o recorte -- dezenas de verdes andando, e a pupila (UM pixel de mundo)
	/// enterrada no meio. Nao ha limiar que separe: o ruido e maior que o sinal e da MESMA cor.
	///
	/// Na rodada da noite a mesma subtracao deu 16 px limpos, e isso nao era sorte nem acerto: era o
	/// escuro apagando as coladas. A medida certa nao pode depender de estar escuro demais pra ver.
	///
	/// ACHADAS PELA ARVORE e nao por API nova: `_coladas` e privado e o `CharacterVisual` esta com
	/// outra sessao dentro dele. O `ColadasNoCorpoDeTeste` ja publica a FOLHA de cada colada, e isso
	/// basta pra reconhece-las entre os filhos do Visual -- nenhuma camada do boneco (corpo, cabelo,
	/// roupa, rabo) usa essas folhas.
	/// ================================================================================================
	/// </summary>
	private void SemColadas()
	{
		if (Visual is not { } vis) return;
		var folhas = new HashSet<string>();
		foreach ((string folha, Color _) in vis.ColadasNoCorpoDeTeste) folhas.Add(folha);

		int quantas = 0;
		foreach (Node n in vis.GetChildren())
			if (n is AnimatedSprite2D s && s.SpriteFrames is { } f && folhas.Contains(f.ResourcePath))
			{
				s.Visible = false;
				quantas++;
			}
		Nota($"  --     {quantas} colada(s) escondida(s) pra a pupila aparecer");
		_espera = 0.3;
	}

	/// <summary>Quantas voltas o <see cref="EsperarAFuria"/> ja deu. Ver ali.</summary>
	private int _voltasDaFuria;

	/// <summary>
	/// ESPERA A FURIA TOMAR O CORPO -- pela CONDICAO, e nao pelo relogio.
	///
	/// ============================ O PRAZO FIXO NAO SERVE, E A RODADA ANTERIOR MOSTROU ============================
	/// Eu tinha calculado 14 s com o piso de 6 s do `Controle(0)` e sobra, e mesmo assim a foto saiu
	/// com a pupila verde. O motivo esta na PRIMEIRA guarda do `TickDaFuriaLendaria`: `EmCena(pl)`.
	/// Entrar no `legendary` pela primeira vez neste corpo toca a cinematica de ESTREIA, e o proprio
	/// comentario do motor diz que ali o relogio nem comeca ("o relogio comeca quando o corpo e
	/// dele"). Ou seja o prazo que eu esperava nao tinha sequer sido armado.
	///
	/// Cravar um numero maior so trocaria o erro de lugar: a cinematica de estreia depende da forma,
	/// da maestria e da maquina. Perguntar "a pupila ja apagou?" a cada meio segundo responde certo em
	/// qualquer um dos casos, e -- o que importa mais -- FALHA EM VOZ ALTA quando nunca apaga, em vez
	/// de fotografar o estado errado e chamar de resultado.
	/// ==========================================================================================================
	/// </summary>
	private void EsperarAFuria()
	{
		const int Voltas = 80;   // 80 x 0,5 s = 40 s, folga larga sobre a cinematica de estreia
		bool branca = Visual?.TintaDoOlhoDeTeste is { } v && v.X > 0.8f && v.Y > 0.8f && v.Z > 0.8f;

		if (branca)
		{
			Nota($"  --     a furia tomou o corpo apos ~{_voltasDaFuria * 0.5:0.#} s (cinematica + prazo de controle)");
			_espera = 0.2;
			return;
		}

		if (++_voltasDaFuria >= Voltas)
		{
			Conferir(false, $"a furia lendaria tomou o corpo em ate {Voltas * 0.5:0} s dentro do `legendary` "
						  + "com maestria 0 -- a pupila nunca apagou, e a foto seguinte NAO mostra posse");
			_espera = 0.2;
			return;
		}

		_depois--;             // repete este passo, como o `EsperarANuvem` do `RoboDeNebulosa`
		_espera = 0.5;
	}

	/// <summary>
	/// A FOTO DA PUPILA, com a cor ARMADA conferida junto -- e as duas coisas no mesmo metodo de
	/// proposito.
	///
	/// O uniform sozinho ja me enganou uma vez: ele lia branco enquanto a tela desenhava verde
	/// (o servidor desfazia a posse fingida entre a leitura e o obturador). A foto sozinha tambem
	/// nao basta -- um pixel de 1x1 no meio de um cabelo verde e discutivel a olho. Medir o uniform
	/// NO MESMO PASSO em que o quadro e capturado amarra os dois: se divergirem de novo, divergem
	/// dentro de uma linha de log so.
	/// </summary>
	private void FotoDaPupila(string rotulo, bool branca)
	{
		Vector3? t = Visual?.TintaDoOlhoDeTeste;
		// BRANCO E `fcfdfd` (os tres canais juntos e altos); o verde da escada e `40a060` (o verde
		// manda com folga). A pergunta e a MESMA das duas pontas, entao um so predicado com o sinal
		// trocado -- duas condicoes separadas envelheceriam em direcoes diferentes.
		bool ehBranca = t is { } v && v.X > 0.8f && v.Y > 0.8f && v.Z > 0.8f;
		Conferir(t != null && ehBranca == branca,
				 $"[{rotulo}] a pupila armada e {(branca ? "BRANCA (a IA dirige)" : "VERDE (o jogador retomou)")}"
			   + $" -- uniform {(t is { } u ? $"({u.X:0.##},{u.Y:0.##},{u.Z:0.##})" : "nenhum")}");
		FotoSolta(rotulo);
	}

	/// <summary>A foto de posse ANTERIOR, pra o <see cref="ODoOlho"/> subtrair. Ver ali o porque.</summary>
	private Image? _posseAnterior;

	private void FotoSolta(string rotulo)
	{
		if (Recorte() is not { } corte) { Nota($"  --     [{rotulo}] sem foto"); _espera = 0.2; return; }
		string caminho = ProjectSettings.GlobalizePath($"user://olhada-{rotulo}.png");
		Ampliar(corte, Ampliacao).SavePng(caminho);
		Ampliar(SoOCorpo(corte), Ampliacao)
			.SavePng(ProjectSettings.GlobalizePath($"user://olhada-{rotulo}-SOCORPO.png"));

		// ============================ O PAR DE POSSE SE SUBTRAI, E ISSO ACHA A PUPILA SOZINHO ============================
		// As duas fotos de posse sao o MESMO corpo, na MESMA forma, no MESMO lugar -- a unica coisa
		// que o bit `semRedeas` muda no pixel e o olho (`World.AoMudarPosse` repinta so ele, e o
		// comentario de la explica por que nao reveste o resto). Entao a diferenca entre as duas E a
		// pupila, achada sem eu precisar adivinhar em que altura ela mora.
		//
		// Isto tambem e uma CHECAGEM disfarcada de foto: se o par diferir em muitos pixels, alguma
		// outra coisa mudou junto (a forma caiu, a aura piscou) e a foto nao esta provando o que diz.
		//
		// SO PRO PAR DE POSSE, e o `StartsWith` nao e preguica: o `FotoSolta` e compartilhado com a
		// prova do C-Type, e sem esta guarda a subtracao rodava entre uma foto de posse e uma foto de
		// C-Type -- duas coisas que nao tem nada a ver uma com a outra -- e sobrescrevia o PNG do olho
		// com o resultado. A foto que respondia o pedido 5 do dono era apagada pelo passo seguinte.
		if (rotulo.StartsWith("posse"))
		{
			if (_posseAnterior is { } antes) ODoOlho(antes, corte);
			_posseAnterior = corte;
		}
		Nota($"  ok     foto {caminho}");
		_espera = 0.2;
	}

	private void ODoOlho(Image a, Image b)
	{
		if (a.GetWidth() != b.GetWidth() || a.GetHeight() != b.GetHeight()) return;
		int mudou = 0;
		Image saida = Image.CreateEmpty(a.GetWidth(), a.GetHeight(), false, Image.Format.Rgba8);
		for (int y = 0; y < a.GetHeight(); y++)
			for (int x = 0; x < a.GetWidth(); x++)
			{
				Color ca = a.GetPixel(x, y), cb = b.GetPixel(x, y);
				bool igual = Mathf.Abs(ca.R - cb.R) < 0.06f && Mathf.Abs(ca.G - cb.G) < 0.06f
						  && Mathf.Abs(ca.B - cb.B) < 0.06f;
				if (!igual) mudou++;
				saida.SetPixel(x, y, igual ? new Color(0.5f, 0.5f, 0.5f) : cb);
			}
		Ampliar(saida, Ampliacao).SavePng(ProjectSettings.GlobalizePath("user://olhada-posse-3-SO-O-QUE-MUDOU.png"));
		Nota($"  --     a posse mudou {mudou} px do recorte -- se for muito mais que a pupila, "
		   + "alguma outra coisa mudou junto e a foto nao prova o que diz");
	}

	/// <summary>
	/// O SSJ1 PELO SERVIDOR, pra a aba Formas ter o que mostrar.
	///
	/// A aba so lista o que JA DESPERTOU (`MenuJogo.AbaFormas`: `Maestria(d.Id) > 0`), e a pintura
	/// do <see cref="Vestir"/> nao escreve maestria nenhuma -- ela nao passa pelo servidor. Sem este
	/// bloco a aba sairia vazia na foto e a pergunta do dono ("o painel diz Grade 4?") ficaria sem
	/// resposta, com sete fotos bonitas ao lado.
	/// </summary>
	private void AProvaDoGrade4()
	{
		C?.SendVerbo("admin_forma", $"0|ssj1");
		// A CINEMATICA TOCA AQUI, e de proposito: este e o caminho de producao. Espera-se ela sair.
		_espera = 6.0;
	}

	private void AbrirAba(string aba)
	{
		if (MenuJogo.Instancia is not { } m) { Conferir(false, "o menu existe pra a foto da aba"); return; }
		m.Abrir();
		m.IrPara(aba);
		Nota($"  --     menu aberto na aba `{aba}` (abas vivas: {string.Join(",", m.AbasDeTeste)})");
		_espera = 0.6;
	}

	/// <summary>
	/// APAGA A AURA E O CONTORNO, deixando na tela so o SPRITE do cabelo com a tinta dele.
	///
	/// Nao e trapaca de bancada: e a unica maneira de perguntar "esta forma mexeu no CABELO?" sem a
	/// resposta vir contaminada pela luz que a forma joga por cima de tudo.
	/// </summary>
	private void SemAura()
	{
		if (Corpo is not { } corpo) return;
		corpo.GetNodeOrNull<Aura>("Aura")?.Apagar();
		corpo.GetNodeOrNull<RaiosDaForma>("Raios")?.Definir(false, Colors.White, 0);
		Visual?.AuraDaForma(Colors.White, 0f, null);

		// ============================ E ELA VOLTA -- POR ISSO A LUZ E MEDIDA E NAO SUPOSTA ============================
		// `Apagar` desliga, mas o `World` reacende: `PrepararAuraDaForma`/`AcenderFormaNoCorpo`
		// (`World.cs:2542,2729`) rodam a cada pacote de forma, entao qualquer snapshot que chegue nos
		// 0,4 s seguintes devolve a aura. Isso e certo em jogo -- o servidor e a autoridade -- e e o
		// motivo de esta bancada NAO poder provar cor de cabelo por subtracao de pixel.
		//
		// A medida abaixo existe pra a conclusao nao virar chute: quando eu vi a foto do C-Type verde
		// mesmo depois do `SemAura`, "deve ser a aura que voltou" era uma hipotese. Com a energia e a
		// cor da luz no log, e um fato.
		// =========================================================================================================
		if (corpo.GetNodeOrNull<Aura>("Aura") is { } au)
			Nota($"  --     apos SemAura: acesa={au.AcesaDeTeste} energia={au.EnergiaDeTeste:0.##} "
			   + $"luz=#{au.CorDaLuzDeTeste.ToHtml(false)}");
		_espera = 0.4;
	}

	/// <summary>
	/// ============================ O C-TYPE E O SSJ1 TEM QUE SAIR IDENTICOS ============================
	/// *"o type C ta certo (e pra ter a mesma cor de cabelo de um ssj normal mesmo)"* -- e o catalogo
	/// concorda: as duas entradas pedem a MESMA folha (`SSj`) e NENHUMA tinta. Entao a prova nao e
	/// "o C-Type parece dourado", e sim "o C-Type e o SSJ1 sao o mesmo pixel".
	///
	/// ESTA CHECAGEM EXISTE PORQUE EU LI A FOTO ERRADO. Na tira de contato o painel do C-Type me
	/// pareceu verde-limao e eu quase escrevi que tinha repintado a forma que o dono mandou nao
	/// tocar. O que eu estava vendo era a AURA dele -- `Aura = "4dff5a"`, verde, porque ele e da linha
	/// Legendary --, luz que cai por igual no cabelo, na pele e na grama. O cabelo embaixo dela e
	/// dourado.
	///
	/// Nenhuma quantidade de "olhar com mais cuidado" resolve isso: uma luz verde forte sobre dourado
	/// PRODUZ amarelo-esverdeado de verdade, e o olho nao tem como desfazer a multiplicacao.
	///
	/// ============================ POR QUE A COMPARACAO NAO E DE PIXEL ============================
	/// Foi, e nao funcionou -- e a razao vale mais que a checagem. Apagar a aura pra comparar os dois
	/// recortes falha porque o `World` a REACENDE no proximo pacote de forma (ver `SemAura`, que agora
	/// mede isso). Tres rodadas reprovaram com 39%, 96% e 70% de diferenca: numeros que nao mediam
	/// cabelo nenhum, so quanto de aura tinha voltado antes do obturador.
	///
	/// A claim do dono e sobre o SPRITE, e sprite tem identidade exata: a FOLHA, a TINTA e o MODO.
	/// Se os tres baterem, os dois cabelos sao literalmente o mesmo desenho com a mesma pintura --
	/// mais forte que qualquer semelhanca de pixel, e imune a luz, a hora do dia e ao clima.
	///
	/// As duas fotos continuam saindo, como ILUSTRACAO e nao como prova: elas mostram por que o
	/// C-Type PARECE esverdeado em jogo (a aura verde `4dff5a` dele, que e da linha Legendary e que o
	/// dono nao pediu pra mexer) sem que isso queira dizer que o cabelo mudou.
	/// ==========================================================================================
	/// </summary>
	/// <param name="fase">0 veste o SSJ1, 1 veste o C-Type, 2 compara. Ver o roteiro em `Depois`.</param>
	private void AProvaDoCType(int fase)
	{
		if (fase == 0) { Vestir(new Pose("ssj1", false, "prova")); return; }
		if (fase == 1)
		{
			_folhaA = Visual?.CabeloDeTeste ?? "";
			_tintaA = Visual?.TintaDoCabeloDeTeste;
			Vestir(new Pose("c_type", false, "prova"));
			return;
		}

		FotoSolta("ctype-B-ctype-sem-aura");

		string folhaB = Visual?.CabeloDeTeste ?? "";
		(Vector3 Tinta, int Modo)? tintaB = Visual?.TintaDoCabeloDeTeste;

		Conferir(_folhaA.Length > 0 && _folhaA == folhaB,
				 $"C-Type e SSJ1 pedem a MESMA folha de cabelo (`{_folhaA.GetFile()}` vs `{folhaB.GetFile()}`)");

		bool mesmaTinta = _tintaA is { } ta && tintaB is { } tb
					   && ta.Modo == tb.Modo && (ta.Tinta - tb.Tinta).Length() < 0.01f;
		Conferir(mesmaTinta,
				 $"C-Type e SSJ1 pedem a MESMA tinta ({Descrever(_tintaA)} vs {Descrever(tintaB)}) "
			   + "-- e as duas sao ZERO, ou seja nenhum dos dois pinta o cabelo");
		_espera = 0.3;
	}

	private string _folhaA = "";
	private (Vector3 Tinta, int Modo)? _tintaA;

	private static string Descrever((Vector3 Tinta, int Modo)? t) =>
		t is { } v ? $"({v.Tinta.X:0.##},{v.Tinta.Y:0.##},{v.Tinta.Z:0.##}) modo {v.Modo}" : "sem cabelo";

	/// <summary>
	/// ROLA A PAGINA DA ABA. O `ScrollContainer` do menu nao tem nome (`MenuJogo.Montar`) e nao ha
	/// API pra ele -- entao acha-se pelo TIPO, que e estavel: e o unico do menu com rolagem vertical
	/// (o das abas nasce com `VerticalScrollMode.Disabled`, `MenuJogo.cs:319`).
	///
	/// Sem API nova no `MenuJogo` de proposito: aquele arquivo esta com outra sessao dentro dele, e a
	/// pergunta e de bancada -- nao vale abrir um campo publico no menu do jogo pra responde-la.
	/// </summary>
	private void Rolar(int quanto)
	{
		if (MenuJogo.Instancia is not { } m) { Conferir(false, "o menu existe pra rolar"); return; }
		foreach (Node n in m.FindChildren("*", "ScrollContainer", true, false))
			if (n is ScrollContainer sc && sc.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled)
			{
				sc.ScrollVertical = quanto;
				Nota($"  --     rolei a aba {quanto} px (de {sc.GetVScrollBar().MaxValue:0} possiveis)");
			}
		_espera = 0.4;
	}

	/// <summary>A TELA INTEIRA, sem recorte: a pergunta aqui e texto de interface, e nao pixel de cabelo.</summary>
	private void TelaCheia(string rotulo)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { Nota($"  --     [{rotulo}] sem foto"); _espera = 0.3; return; }
		string caminho = ProjectSettings.GlobalizePath($"user://olhada-{rotulo}.png");
		img.SavePng(caminho);
		Nota($"  ok     foto {caminho}");
		_espera = 0.3;
	}

	/// <summary>
	/// OS NOMES, escritos no log ao lado das fotos.
	///
	/// Nao substitui a foto da aba: o log prova o que a FUNCAO responde, e a foto prova o que a TELA
	/// mostra. Os dois defeitos que o dono relatou nesta area foram de tela e nao de funcao (o
	/// `d.Nome` cru num chamador, o botao a mais no painel), entao o log sozinho aprovaria os dois.
	/// </summary>
	private void OsNomes()
	{
		var livro = new Jandirus.Core.Forms.Maestrias();
		Jandirus.Core.Forms.FormaDef? ssj1 = Jandirus.Core.Forms.Catalogo.Def("ssj1");

		Nota($"  --     `ssj1` a   0%: \"{Jandirus.Core.Forms.Catalogo.NomeDe(ssj1, livro)}\"");
		livro.Por("ssj1", 100);
		Nota($"  --     `ssj1` a 100%: \"{Jandirus.Core.Forms.Catalogo.NomeDe(ssj1, livro)}\"");
		Nota($"  --     `legendary`        (comum) : \"{Jandirus.Core.Forms.Catalogo.Def("legendary")?.Nome}\"");
		Nota($"  --     `primal_legendary` (primal): \"{Jandirus.Core.Forms.Catalogo.Def("primal_legendary")?.Nome}\"");

		// A LINHA LENDARIA INTEIRA, contada. O pedido do dono e "um botao a menos", e o painel de
		// admin monta um botao por entrada da linha (`MenuJogo.PainelDeFormas`) -- entao contar as
		// entradas E contar os botoes, sem depender de ler a foto.
		string[] daLinha = [.. Jandirus.Core.Forms.Catalogo.Todas
			.Where(d => d.Linha == Jandirus.Core.Forms.LinhaDeForma.Legendary)
			.OrderBy(d => d.Ordem).Select(d => d.Nome)];
		Nota($"  --     linha Legendary: {daLinha.Length} botao(oes) -- {string.Join(" | ", daLinha)}");
		Conferir(daLinha.Length == 3,
				 $"a linha Legendary tem TRES entradas (o `legendary_full_power` sumiu) -- tem {daLinha.Length}");
		_espera = 0.3;
	}

	// =====================================================================
	// 5. A TIRA DE CONTATO
	// =====================================================================
	/// <summary>
	/// AS SETE CABECAS NUM PNG SO, na ordem do <see cref="Roteiro"/>.
	///
	/// As fotos soltas respondem "que cor e esta"; a tira responde "esta e mais clara que aquela",
	/// que e a forma real das tres reclamacoes do dono -- todas elas sao COMPARACOES (o Blue contra
	/// o Evolution, o Legendary contra o verde-bandeira antigo, o C-Type contra ele mesmo). Julgar
	/// tom absoluto em duas imagens abertas uma depois da outra e justamente o que o olho nao faz.
	/// </summary>
	private void TiraDeContato()
	{
		if (_contato.Count == 0) { Nota("  --     sem tira (nenhuma foto saiu)"); return; }

		int lado = _contato[0].GetWidth();
		Image folha = Image.CreateEmpty(lado * _contato.Count, lado, false, Image.Format.Rgba8);
		for (int i = 0; i < _contato.Count; i++)
			folha.BlitRect(_contato[i], new Rect2I(0, 0, lado, lado), new Vector2I(i * lado, 0));

		string caminho = ProjectSettings.GlobalizePath("user://olhada-CONTATO.png");
		Ampliar(folha, Ampliacao).SavePng(caminho);
		Nota($"  ok     tira de contato ({_contato.Count} poses, na ordem do roteiro) {caminho}");
	}

	private void Fechar()
	{
		_acabou = true;
		GD.Print("[olhada] ===== BANCADA DE OLHAR =====");
		GD.Print(_falhas.Count == 0
			? "[olhada] ===== sem falha de montagem -- agora o JUIZ E O OLHO ====="
			: $"[olhada] ===== {_falhas.Count} FALHA(S) =====\n[olhada]   " + string.Join("\n[olhada]   ", _falhas));
		GetTree().Quit();
	}
}
