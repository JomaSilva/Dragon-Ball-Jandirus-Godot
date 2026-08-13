using Godot;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA FERA (`--diagfera`). Ela nao confere catalogo: ela TIRA FOTO da chama, do rabo, do
/// olho e da faisca do <b>Mistico</b> e do <b>Beast</b>.
///
/// ============================ POR QUE ELA EXISTE, SEPARADA DAS OUTRAS ============================
/// Os cinco pedidos do dono desta rodada sao TODOS "que cor esta na tela":
///
///   1. *"o mistico e beast tao usando a aura de carga do ssj god"* -- os dois, e esta errado;
///   2. *"o rabo do beast n ta branco"*;
///   3. *"o olho do beast era pra ser vermelho"*;
///   4. *"a aura do mistico tem q ser a mesma aura da BASE DO PERSONAGEM, porem com os efeitos de
///      raiozinhos q ja existem"*;
///   5. *"no beast os raiozinhos sao roxos"*.
///
/// O `--diagforma` responde os cinco em NUMERO -- e passa verde. Ele mede o catalogo e o uniform
/// ARMADO no material, e a familia de defeito deste projeto e exatamente a distancia entre esses
/// dois e o pixel: o `COLOR = c * COLOR` elevava cada canal ao quadrado com todo o `--diagforma`
/// verde, e a `FieryGod` **nao se tinge** -- ou seja o `7d5af0` do Beast podia estar armado no node
/// e nao chegar em pixel nenhum. Nenhuma medida de campo enxerga isso.
///
/// E o `--diagolhada` tambem nao serve: o recorte dele e a CABECA a 16 px de mundo. A chama nao cabe
/// nele (o `colorablebigaura` e maior que o corpo), o rabo fica de fora e a faisca sai da silhueta
/// pra os lados. Aqui o recorte e o CORPO INTEIRO com folga, e a cabeca sai numa segunda foto.
/// ==============================================================================================
///
/// ============================ POR QUE A TIRA TEM OITO QUADROS ============================
/// A faisca nao esta sempre na tela: o `RaiosDaForma` dispara rajadas a cada 1,3 s a 2,7 s
/// (`IntervaloMin`/`IntervaloMax`) e cada raio vive 0,9 s. Uma foto unica tem cerca de um terco de
/// chance de pegar uma -- e uma bancada que fotografasse o intervalo escreveria "nao ha faisca" com
/// a faisca funcionando. Oito quadros a 0,45 s cobrem 3,6 s, que e mais que o intervalo maximo:
/// **e impossivel a tira inteira sair sem raio nenhum se o node estiver disparando**.
/// ======================================================================================
///
/// ============================ O ROTEIRO, E POR QUE OS DOIS CONTROLES ============================
///   1. `base`    -- o CONTROLE do pedido 4. E aqui que se ve "a aura da base do personagem"; a foto
///                   2 tem que ser a MESMA cor, so que mais densa (o Mistico e 16x -- a forca
///                   continua perguntando `EhBase`, ver `Aura.ForcaDaChamaDe`).
///   2. `mistico` -- pedidos 1 e 4, mais a faisca BRANCA do pedido 5 pelo avesso (o dono nomeou a
///                   Fera, entao esta tem que continuar branca).
///   3. `beast`   -- pedidos 1, 2, 3 e 5 de uma vez.
///   4. `ssg`     -- o CONTROLE do pedido 1, e ele fecha de proposito. O SSG e o dono LEGITIMO da
///                   `FieryGod` (`AuraObject.dm:174-176`), e uma rodada que so fotografasse o que
///                   mudou nao distingue "tirei os dois da folha do God" de "apaguei a folha do God".
///                   Se a foto 4 deixar de ser pessego/laranja, quem mexeu quebrou o SSG.
///   5. `rose_ssg`-- o SEGUNDO controle, e ele nao e redundante: o `Catalogo.Folha` responde a linha
///                   `GodKiRose` por um RAMO PROPRIO (com corte de `Ordem`), e nao pelo ramo do SSG.
///                   Uma edicao que acertasse um e derrubasse o outro passaria inteira por uma
///                   bancada que so fotografasse o `ssg` -- e os dois sao a mesma queixa do dono.
/// ==========================================================================================
///
/// COMO RODAR (precisa de JANELA: no headless o `GetImage` volta vazio e nao ha foto nenhuma):
///     Godot --path . --host --rede 7945 --kiteste --bpteste 3000000 --diagfera \
///           --raca Saiyan --nome Zx --conta &lt;NOVA&gt;
///
/// As fotos saem em `user://fera-*.png`.
/// </summary>
public partial class RoboDeFera : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	/// <summary>Quanto o passo CORRENTE espera antes de rodar. Escrito pelo passo anterior.</summary>
	private double _espera = 2.0;

	private readonly List<string> _falhas = [];

	private static GameClient? C => GameClient.Instance;

	private Node2D? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as Node2D;

	private CharacterVisual? Visual => Corpo?.GetNodeOrNull<CharacterVisual>("Visual");

	private static void Nota(string linha) => GD.Print("[fera] " + linha);

	private void Conferir(bool ok, string oque)
	{
		Nota((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	private readonly record struct Pose(string Id, string Rotulo);

	private static readonly Pose[] Roteiro =
	[
		new("base",    "1-base-controle"),
		new("mistico", "2-mistico"),
		new("beast",   "3-beast"),
		new("ssg",      "4-ssg-controle"),
		new("rose_ssg", "5-rose-ssg-controle"),
	];

	/// <summary>Quadros por pose. Ver o cabecalho: oito a 0,45 s cobrem o intervalo maximo da rajada.</summary>
	private const int Quadros = 8;

	private const double EntreQuadros = 0.45;

	/// <summary>Os quadros da pose corrente, pra a montagem 4x2 do fim dela.</summary>
	private readonly List<Image> _tira = [];

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ============================ ESTE MUNDO E MEU? SE NAO FOR, NAO ENCOSTA ============================
		// Copiado do `RoboDeOlhada:104` e pelo mesmo motivo escrito la: com a porta tomada o `--host`
		// nao vira servidor nenhum e o cliente entra no mundo DA OUTRA SESSAO -- e esta bancada troca a
		// forma do corpo e segura a carga de Ki, o que apareceria na tela de quem estivesse jogando ali.
		// Ha outra sessao editando este repo agora; forcar seria estragar a rodada alheia.
		// ==============================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[fera] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este mundo "
					  + "e de outra sessao. Nada foi forcado. Suba com `--rede <outra porta>`.");
			return;
		}

		if (C is not { Connected: true } || Corpo is null) return;

		_t += delta;
		if (_t < _espera) return;
		_t = 0;

		const int Preparo = 2;
		const int PassosPorPose = 2 + Quadros;

		if (_passo < Preparo)
		{
			switch (_passo++)
			{
				case 0: Preparar(); break;
				default: OQueOOlhoVaiJulgar(); break;
			}
			return;
		}

		int i = (_passo - Preparo) / PassosPorPose;
		int dentro = (_passo - Preparo) % PassosPorPose;

		if (i >= Roteiro.Length) { Fechar(); return; }
		_passo++;

		Pose p = Roteiro[i];
		if (dentro == 0) { Vestir(p); return; }

		// UM PASSO INTEIRO ENTRE VESTIR E FOTOGRAFAR, e ele nao e desperdicio: o
		// `GetTexture().GetImage()` devolve o quadro JA DESENHADO, ou seja o anterior a este
		// `_Process`. Fotografar no mesmo passo do `Vestir` pegaria a forma de ANTES -- e a foto sairia
		// perfeita, so que da pose errada, que e o defeito mais dificil de desconfiar.
		// (Aqui ele tambem narra o que o node ficou, pra a contraprova ao lado da foto.)
		if (dentro == 1) { Narrar(p); return; }

		Quadro(p, dentro - 2);
	}

	// =====================================================================
	// 1. PREPARAR
	// =====================================================================
	private void Preparar()
	{
		// O CEU PARADO E O RELOGIO PARADO, os dois pelo motivo que o `RoboDeOlhada.Preparar` pagou pra
		// aprender: um dia dura 24 minutos (`Ceu.SegundosPorDia`), entao uma rodada em cada duas
		// fotografa a noite -- e la o pixel mais claro do recorte inteiro mediu `#1e274d`, com dourado
		// medindo quase preto. Uma bancada de COR que nao trava a hora mede a hora.
		C?.SendVerbo("admin_clima", "Neblina|0.05");
		C?.SendVerbo("admin_meio_dia");

		// ============================ O RABO PRECISA EXISTIR PRA A PERGUNTA 2 FAZER SENTIDO ============================
		// Metade desta rodada e *"o rabo do beast n ta branco"*, e um Saiyajin nasce com ou sem rabo
		// conforme a ficha (`LocalPlayer:280` -- `MostrarRabo(ficha.Rabo)`). Numa ficha sem rabo a foto
		// sairia certa e vazia, e o log diria `ok`. Forcar aqui e o mesmo que o `RoboDeForma:7715` faz,
		// e e seguro porque este canal NAO vem no snapshot (ao contrario da posse, que o servidor
		// reescreve 30x/s e ja desfez uma mentira de bancada -- ver `RoboDeOlhada.Depois`).
		// ==========================================================================================================
		Visual?.MostrarRabo(true);

		// ============================ E A CHAMA SO ACENDE COM O C, QUE E REGRA DO DONO ============================
		// *"a aura so nasce da tecla C ou do Ki acima de 100%"*. `Preparar` (o do node `Aura`) guarda a
		// cor e a folha SEM puxar o gatilho -- entao sem esta linha as quatro poses sairiam sem chama
		// nenhuma e as perguntas 1 e 4 ficariam sem foto.
		//
		// PELO PEDIDO DE VERDADE (`SendCarregar`) e nao acendendo o node na mao: quem pinta a chama
		// enquanto se carrega e a `CargaVisual` (o SEGUNDO desenho da mesma chama), e ela so entra em
		// cena pelo caminho servidor -> `aura_carga` -> `World.AplicarChamaDaCargaLocal`. Acender o node
		// `Aura` daqui fotografaria um desenho que o jogador nunca ve neste estado.
		// ====================================================================================================
		C?.SendCarregar(true);

		Nota($"  --     zoom={World.Instancia?.ZoomDeTeste}  janela={GetViewport().GetVisibleRect().Size}");
		_espera = 2.0;
	}

	/// <summary>
	/// O QUE O OLHO VAI JULGAR, dito ANTES de qualquer foto sair.
	///
	/// Nao e checagem: e o enunciado, e ele existe porque foto nao tem legenda. Escrever a expectativa
	/// depois do resultado deixa qualquer foto ser lida como confirmacao do que quer que ela mostre --
	/// foi assim que uma bancada leu poeira de cinematica como rampa de cor por duas rodadas, e outra
	/// leu grama verde como cabelo verde (`RoboDeOlhada.SoOCorpo`).
	/// </summary>
	private static void OQueOOlhoVaiJulgar()
	{
		Nota("  --     1. CHAMA: as fotos 2 (mistico) e 3 (beast) NAO podem ser pessego/laranja. Se");
		Nota("  --        forem, a `FieryGod` do SSG continua neles -- que e a queixa numero 1.");
		Nota("  --     2. A foto 2 tem que ter a MESMA COR da foto 1 (a chama do jogador, azul claro),");
		Nota("  --        so que mais densa. Outra cor ali = o `ChamaDoJogador` nao chegou no pixel.");
		Nota("  --     3. A foto 3 e ROXO-AZULADA (`7d5af0`). O dono nunca descreveu essa cor -- ela e o");
		Nota("  --        `rgb(125,90,240)` do `Mystic.dm:95`, que no DM e overlay de POSE e nao chama.");
		Nota("  --     4. RABO: na foto 3 ele e BRANCO. Na 2, a MESMA cor da 1 (o Mistico nao ganha");
		Nota("  --        tinta de brinde -- o enunciado dele e `tudo igual a base`).");
		Nota("  --     5. OLHO: na cabeca da foto 3, VERMELHO. Na 2, igual ao da 1.");
		Nota("  --     6. FAISCA: na tira da 3 ela e ROXA (lilas claro) e na da 2 e BRANCA. ARMADILHA");
		Nota("  --        conhecida e MEDIDA: a faisca da Fera e roxo-claro DENTRO de chama roxa (17");
		Nota("  --        graus de matiz entre as duas) -- ela le pelo VALOR, 1,76x mais clara. Se nao");
		Nota("  --        der pra ver, isso e achado e nao falha de bancada.");
		Nota("  --     7. CONTROLE: a foto 4 (ssg) TEM que continuar pessego/laranja. Ela e a unica que");
		Nota("  --        nao era pra mudar; se mudou, quem mexeu quebrou o dono legitimo da folha.");
		Nota("");
	}

	// =====================================================================
	// 2. VESTIR -- pelo caminho do jogo, e sem cinematica
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE NAO E O `admin_forma` DA REDE ============================
	/// Mesmo motivo do `RoboDeOlhada.Vestir` e do `RoboDeColada.Vestir`: `AdminForcarForma` nao suprime
	/// cinematica (e escrito la que nao suprime de proposito), entao cada pose custaria a cena de
	/// estreia inteira -- e a poeira, a cratera e o clarao dela ficariam POR CIMA da chama, que e o
	/// assunto de tres das quatro fotos. O pedido do dono desta rodada e sobre o corpo EM PE, depois de
	/// tudo assentar; fotografar o meio de uma cena ja produziu relatorio de cor errada neste projeto.
	///
	/// `DegrauDeCena.Nenhuma` cai no `World.VestirAFormaSemCena`, que e o caminho de producao de quem ja
	/// domina a forma -- e e ele quem chama o `PrepararAuraDaForma` (a folha e a cor da chama), o
	/// `VestirCabeloDaForma` (o rabo e o olho) e o `AcenderFormaNoCorpo` (a faisca). Os quatro canais
	/// desta rodada passam por ele.
	/// ==========================================================================================
	/// </summary>
	private void Vestir(Pose p)
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo) return;
		int meuId = C?.LocalId ?? 0;
		if (meuId == 0) { Conferir(false, "a bancada esta conectada"); return; }

		int b = Jandirus.Core.Forms.Catalogo.Rede(Jandirus.Core.Forms.Catalogo.IdBase);

		// PASSA PELA BASE ENTRE UMA POSE E OUTRA, pelo mesmo motivo do `RoboDeOlhada`: sem isto o
		// `AoMudarForma` pode receber `de == para` e tratar como "nada mudou". Aqui isso importa em
		// dobro -- `mistico` e `beast` sao da MESMA linha, e uma delas herdaria o estado da outra.
		mundo.AoMudarForma(meuId, b, b, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.AoMudarForma(meuId, b, Jandirus.Core.Forms.Catalogo.Rede(p.Id),
						   Jandirus.Core.Forms.DegrauDeCena.Nenhuma);

		// O C DE NOVO A CADA POSE. O Mistico e 16x e o Beast e 56x, e os dois drenam Ki
		// (`Dreno`): uma carga segurada desde o preparo pode ter acabado no meio do roteiro, e a foto
		// sairia sem chama por falta de combustivel -- que se leria como "a chama sumiu".
		C?.SendCarregar(true);

		_tira.Clear();
		_espera = 0.6;
	}

	// =====================================================================
	// 3. O QUE O NODE FICOU -- a contraprova que anda ao lado da foto
	// =====================================================================
	/// <summary>
	/// Os quatro canais desta rodada lidos no NODE, ao lado da foto e nao no lugar dela.
	///
	/// Eles ja sao medidos pelo `--diagforma` e la passam verdes: o valor deles AQUI e ser
	/// CONTRAPROVA. Quando o pixel na tela nao corresponde ao que esta escrito no node, o reu esta
	/// ENTRE os dois (material, mistura, ordem de desenho) e nao no catalogo -- foi assim que o
	/// `COLOR = c` apareceu, e nenhuma medicao de campo teria achado aquilo.
	/// </summary>
	private void Narrar(Pose p)
	{
		Jandirus.Core.Forms.FormaDef? d = Jandirus.Core.Forms.Catalogo.Def(p.Id);
		bool ehBase = p.Id == Jandirus.Core.Forms.Catalogo.IdBase;
		Jandirus.Core.Forms.FormaDef? df = ehBase ? null : d;

		// A COR PESSOAL SAI DO CORPO QUE ESTA POSANDO, e nao do fallback: esta narracao existe pra ser
		// comparada com o pixel, e a chama da base (e a do Mistico) e a cor sorteada DELE.
		Color pessoal = Corpo?.GetNodeOrNull<Aura>("Aura")?.CorPessoal ?? Aura.CorDoKiCru;

		Nota($"  --     [{p.Rotulo}] catalogo: chama={Aura.CorDaChamaDe(df, pessoal).ToHtml(false)} "
		   + $"folha={Jandirus.Core.Forms.Catalogo.Folha(df)} "
		   + $"doJogador={Jandirus.Core.Forms.Catalogo.ChamaDoJogador(df)} "
		   + $"rabo={Jandirus.Core.Forms.Catalogo.CorDoRabo(df) ?? "-"} "
		   + $"olho={Jandirus.Core.Forms.Catalogo.CorDoOlho(df) ?? "-"} "
		   + $"raio={Jandirus.Core.Forms.Catalogo.CorDosRaios(df)}");

		if (Corpo?.GetNodeOrNull<Aura>("Aura") is { } aura)
			Nota($"  --     [{p.Rotulo}] node Aura : folha={aura.DesenhoDeTeste.FolhaDeTeste.GetFile()} "
			   + $"semFolha={aura.DesenhoDeTeste.SemFolha} semTinta={aura.DesenhoDeTeste.SemTinta} "
			   + $"corNoMaterial={aura.DesenhoDeTeste.CorNoMaterialDeTeste?.ToHtml(false) ?? "-"} "
			   + $"acesa={aura.AcesaDeTeste}");

		if (Corpo?.GetNodeOrNull<CargaVisual>("Carga") is { } cg)
			Nota($"  --     [{p.Rotulo}] node Carga: folha={cg.DesenhoDeTeste.FolhaDeTeste.GetFile()} "
			   + $"semTinta={cg.DesenhoDeTeste.SemTinta} "
			   + $"corNoMaterial={cg.DesenhoDeTeste.CorNoMaterialDeTeste?.ToHtml(false) ?? "-"} "
			   + $"carregando={cg.CarregandoDeTeste} visivel={cg.DesenhoDeTeste.Visible}");

		if (Corpo?.GetNodeOrNull<RaiosDaForma>("Raios") is { } raios)
			Nota($"  --     [{p.Rotulo}] node Raios: cor={raios.CorDeTeste.ToHtml(false)} "
			   + $"vivos={raios.VivosDeTeste} rajadas={raios.RajadasDeTeste} "
			   + $"emitindo={raios.EmitindoDeTeste}");

		if (Visual is { } vis)
			Nota($"  --     [{p.Rotulo}] boneco   : rabo={Tinta(vis.TintaDoRaboDeTeste)} "
			   + $"raboVisivel={vis.RaboVisivelDeTeste} olho={Tinta(vis.TintaDoOlhoDeTeste)} "
			   + $"cabelo={vis.CabeloDeTeste.GetFile()}");

		// A CHAMA TEM QUE ESTAR ACESA, senao a foto desta pose nao responde nada. Isto e falha de
		// BANCADA e nao do jogo -- e e a diferenca entre "a chama esta apagada" e "nao consegui olhar".
		bool desenhando = Corpo?.GetNodeOrNull<CargaVisual>("Carga") is { DesenhoDeTeste.Visible: true }
					   || Corpo?.GetNodeOrNull<Aura>("Aura") is { AcesaDeTeste: true };
		Conferir(desenhando, $"[{p.Rotulo}] ha chama DESENHADA pra fotografar (o C pegou)");

		_espera = 0.3;
	}

	private static string Tinta(Vector3? v) =>
		v is { } t ? $"({t.X:0.###},{t.Y:0.###},{t.Z:0.###})" : "-";

	private static string Tinta((Vector3 Tinta, int Modo)? v) =>
		v is { } t ? $"({t.Tinta.X:0.###},{t.Tinta.Y:0.###},{t.Tinta.Z:0.###})m{t.Modo}" : "-";

	// =====================================================================
	// 4. A FOTO
	// =====================================================================
	/// <summary>
	/// O lado do recorte do CORPO, em pixels de mundo. Derivado do zoom e nao cravado em pixel de
	/// tela, pelo mesmo motivo do `RoboDeColada.LadoDoCorte`: cravar daria enquadramentos diferentes
	/// em `--zoom 2` e `--zoom 3`, e duas rodadas do dono deixariam de ser comparaveis.
	///
	/// SESSENTA E QUATRO porque a chama e MAIOR QUE O CORPO -- o `colorablebigaura` sobe bem acima da
	/// cabeca e a faisca sai da silhueta pra os lados. Um recorte justo no boneco cortaria fora
	/// justamente o assunto: a foto sairia de um corpo cercado de cor, sem forma nenhuma pra julgar.
	/// </summary>
	private const int LadoDoCorpo = 64;

	/// <summary>Lado do recorte da CABECA. Mesmos numeros do `RoboDeOlhada` -- ver la o porque do 16.</summary>
	private const int LadoDaCabeca = 16;

	private const int AlturaDaCabeca = 6;

	private int Zoom => Math.Max(1, World.Instancia?.ZoomDeTeste ?? 2);

	/// <summary>
	/// O quadro ja desenhado, recortado em volta do corpo. Irmao do `RoboDeOlhada.Recorte` e pelo
	/// mesmo motivo escrito la: o corpo NEM SEMPRE esta no centro da tela (a camera para nas beiradas
	/// do mapa), entao o recorte sai da posicao de TELA do node e nao do centro do viewport.
	/// </summary>
	private Image? Recorte(int ladoEmMundo, int sobeEmMundo)
	{
		if (Corpo is not { } centro) return null;
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;

		Vector2 pos = centro.GetGlobalTransformWithCanvas().Origin;
		int lado = Mathf.Min(ladoEmMundo * Zoom, Mathf.Min(img.GetWidth(), img.GetHeight()));
		int x = Mathf.Clamp((int)pos.X - lado / 2, 0, img.GetWidth() - lado);
		int y = Mathf.Clamp((int)pos.Y - sobeEmMundo * Zoom - lado / 2, 0, img.GetHeight() - lado);

		Image corte = img.GetRegion(new Rect2I(x, y, lado, lado));
		// FORMATO UNICO ANTES DE COLAR: o `BlitRect` da montagem nao converte, e o viewport pode
		// devolver um formato que nao e o da folha montada. Sem esta linha a tira sai preta em algumas
		// maquinas e perfeita em outras -- o pior tipo de defeito de bancada.
		corte.Convert(Image.Format.Rgba8);
		return corte;
	}

	/// <summary>
	/// VIZINHO-MAIS-PROXIMO, e nao bilinear. Interpolacao suave INVENTA cores entre dois pixels
	/// vizinhos -- e a pergunta desta rodada e literalmente "de que cor e este pixel". Uma faisca
	/// lilas clara ao lado de uma chama roxa ampliada com suavizacao produz uma franja que nao existe
	/// em folha nenhuma, e ela cairia bem no meio da unica duvida medida desta rodada.
	/// </summary>
	private static Image Ampliar(Image src, int vezes)
	{
		Image copia = Image.CreateEmpty(src.GetWidth(), src.GetHeight(), false, src.GetFormat());
		copia.BlitRect(src, new Rect2I(0, 0, src.GetWidth(), src.GetHeight()), Vector2I.Zero);
		copia.Resize(src.GetWidth() * vezes, src.GetHeight() * vezes, Image.Interpolation.Nearest);
		return copia;
	}

	private void Quadro(Pose p, int n)
	{
		if (Recorte(LadoDoCorpo, 0) is { } corpo) _tira.Add(corpo);

		// ============================ A RAJADA NA HORA DO OBTURADOR ============================
		// A tira de oito quadros GARANTE que ao menos uma rajada natural cai dentro dela (ver o
		// cabecalho), e isso basta pra responder *"a faisca existe?"*. Nao basta pra a pergunta que o
		// dono realmente fez -- *"da pra VER os raiozinhos contra a aura dele?"* --, porque com sorte
		// ruim a unica rajada da tira sai atras do corpo, ou de raspao na beirada, e o julgamento cai
		// sobre a foto errada.
		//
		// E `DispararDeTeste` NAO E UMA SEGUNDA VERDADE: ele chama o MESMO `Disparar()` que o relogio
		// do node chama a cada 1,3 s -- mesma cor, mesma quantidade, mesmo shader (`RaiosDaForma:158`).
		// O que muda e QUANDO, e quando e a unica coisa que esta bancada precisa controlar. Uma
		// bancada que dependesse do sorteio pra enquadrar mediria a sorte junto com a cor.
		//
		// EM QUADRO SIM, QUADRO NAO: o raio vive 0,9 s (`DuracaoDoRaio`) e os quadros saem a 0,45 s,
		// entao disparar nos pares deixa metade da tira com faisca RECEM-NASCIDA e a outra metade com
		// ela ja apagando. As duas metades importam -- a segunda e onde se ve se a faisca some dentro
		// da chama, que e o defeito que o `primal_legendary2` teve.
		// ==================================================================================
		if (n % 2 == 0 && Corpo?.GetNodeOrNull<RaiosDaForma>("Raios") is { VivosDeTeste: > 0 } raios)
			raios.DispararDeTeste();

		// A CABECA SAI DO QUADRO DO MEIO e nao do primeiro: o boneco entra na pose nova no quadro
		// seguinte ao `Vestir`, e a faisca do primeiro quadro costuma estar no meio de uma rajada
		// atravessando o rosto. O olho e DOIS pixels de mundo (`RoboDeOlhada.LadoDaCabeca`) -- um raio
		// por cima dele apaga a resposta inteira.
		if (n == Quadros / 2 && Recorte(LadoDaCabeca, AlturaDaCabeca) is { } cabeca)
		{
			string caminho = ProjectSettings.GlobalizePath($"user://fera-{p.Rotulo}-cabeca.png");
			Ampliar(cabeca, 12).SavePng(caminho);
			Nota($"  ok     [{p.Rotulo}] cabeca (12x) {caminho}");
		}

		if (n < Quadros - 1) { _espera = EntreQuadros; return; }

		// O ULTIMO QUADRO FECHA A MONTAGEM.
		Montar(p);
		_espera = 0.3;
	}

	/// <summary>
	/// OS OITO QUADROS NUM PNG SO, em quatro colunas por duas fileiras.
	///
	/// Oito arquivos soltos por pose seriam trinta e dois arquivos, e ninguem compara trinta e dois --
	/// abre dois e conclui. A montagem poe o TEMPO na mesma imagem: e nela que se ve a faisca aparecer
	/// e sumir, que e a unica pergunta desta rodada que nao se responde num instante congelado.
	/// </summary>
	private void Montar(Pose p)
	{
		if (_tira.Count == 0) { Nota($"  --     [{p.Rotulo}] sem foto (headless nao renderiza)"); return; }

		int lado = _tira[0].GetWidth();
		int colunas = 4;
		int linhas = (_tira.Count + colunas - 1) / colunas;
		Image folha = Image.CreateEmpty(lado * colunas, lado * linhas, false, Image.Format.Rgba8);
		for (int i = 0; i < _tira.Count; i++)
			folha.BlitRect(_tira[i], new Rect2I(0, 0, lado, lado),
						   new Vector2I(i % colunas * lado, i / colunas * lado));

		string caminho = ProjectSettings.GlobalizePath($"user://fera-{p.Rotulo}-tira.png");
		Ampliar(folha, 3).SavePng(caminho);
		Nota($"  ok     [{p.Rotulo}] tira de {_tira.Count} quadros (3x) {caminho}");

		// E O QUADRO DO MEIO SOZINHO, GRANDE. A montagem responde "a faisca aparece?"; esta responde
		// "de que cor e a chama", que a 3x com quatro quadros lado a lado ninguem julga -- foi
		// exatamente o erro que o `RoboDeOlhada.SoOCorpo` documenta (a tira lida como "verde, verde,
		// verde" sobre uma foto dourada).
		Image meio = _tira[Math.Min(_tira.Count - 1, Quadros / 2)];
		string solto = ProjectSettings.GlobalizePath($"user://fera-{p.Rotulo}-corpo.png");
		Ampliar(meio, 6).SavePng(solto);
		Nota($"  ok     [{p.Rotulo}] corpo (6x) {solto}");
	}

	private void Fechar()
	{
		_acabou = true;
		GD.Print("[fera] ===== BANCADA DA FERA =====");
		GD.Print(_falhas.Count == 0
			? "[fera] ===== sem falha de montagem -- agora o JUIZ E O OLHO ====="
			: $"[fera] ===== {_falhas.Count} FALHA(S) =====\n[fera]   " + string.Join("\n[fera]   ", _falhas));
		GetTree().Quit();
	}
}
