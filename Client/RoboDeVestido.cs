using Godot;
using Jandirus.Core.Appearance;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DE OLHAR OS VIZINHOS VESTIDOS (`--diagvestido`). Ela nao confere campo nenhum: ela
/// TIRA FOTO do povo que nasceu no planeta e escreve, ao lado de cada foto, o que aquele corpo
/// diz vestir.
///
/// ============================ POR QUE ELA EXISTE, SEPARADA DA `--npcteste` ============================
/// A `--npcteste` ja tranca a REGRA inteira: a tabela do DM esta portada linha por linha, o funil e
/// o mesmo do jogador, a roupa e funcao pura da semente, o chefe nunca entra no sorteio racial.
/// Noventa checagens verdes. E mesmo assim ela nao responde a queixa que o dono fez, porque a
/// queixa dele e uma FOTO: *"todo npc ta nascendo SEM ROUPAS"*. Ele nao mediu `ap.Roupa` -- ele
/// olhou a tela.
///
/// A casa ja pagou por essa diferenca mais de uma vez, e esta escrito nos vizinhos: *"uniform
/// escrito nao e pixel desenhado"* (`RoboDeForma.Fotografar`) e a tira da colada, que saiu verde
/// enquanto o dono via brilho cinza. Entre `ap.Roupa` ter tres itens e o boneco aparecer vestido ha
/// um catalogo (`VisualCatalog.Sanear`, que DESCARTA peca fora da lista), um fio (`Protocol`, que
/// devolve string VAZIA acima de 120 caracteres) e um `Vestir` que remonta camadas. Cada um deles
/// ja quebrou uma vez, e nenhum deles aparece numa checagem de Core.
/// ==================================================================================================
///
/// ============================ O QUE ELA FOTOGRAFA, E POR QUE ASSIM ============================
///   1. `grupo`  -- a TELA INTEIRA, janela maximizada. E o unico enquadramento em que se ve
///                  "um grupo", que e literalmente o que foi pedido ("fotografe um grupo em
///                  Vegeta"). Recorte de corpo nao responde "o povo daqui se parece".
///   2. `EU`     -- o corpo do JOGADOR, no mesmo recorte e na mesma ampliacao dos NPCs. Pedido
///                  literal do dono ("compare com o corpo do jogador no mesmo quadro"), e serve de
///                  regua: se o boneco do jogador tambem sair pelado, o defeito nao e do povoamento.
///   3. um por vizinho -- recorte de 48 px de mundo, ampliado 6x em vizinho-mais-proximo. O NOME DO
///                  ARQUIVO CARREGA A ETIQUETA (raca e pecas), porque desenhar texto na imagem
///                  exigiria fonte e o que se julga aqui e cor e silhueta.
///   4. `tira`   -- todos lado a lado num PNG so. Ninguem compara doze arquivos: abre dois e
///                  conclui. A tira e onde se ve que o povo de Vegeta se parece ENTRE SI e nao se
///                  parece com o da Terra -- que e a pergunta do dono ("da pra ver a raca pela
///                  roupa?"), e ela e comparativa por natureza.
///
/// E ela escreve um `vestido-&lt;rotulo&gt;.txt` com a lista do que cada corpo veste. A foto responde
/// "aparece"; o texto responde "aparece O QUE" -- e sem os dois lados nao da pra separar "vestiu a
/// armadura" de "vestiu alguma coisa".
/// ==========================================================================================
///
/// COMO RODAR (precisa de JANELA -- no headless o `GetImage` volta vazio e nao ha foto nenhuma):
///     Godot --path . --host --rede 7958 --diagvestido --vestidorotulo vegeta \
///           --raca Saiyan --nome Olheiro --conta &lt;NOVA&gt;
///
/// A raca do jogador decide o PLANETA (o berco), e o planeta decide o povo: Saiyan -> Vegeta,
/// Human -> Earth, Namekian -> Namek. Nao ha argumento de planeta nesta bancada de proposito --
/// quem escolhe onde o corpo nasce e a mesma regra que escolhe onde o jogador nasce.
/// </summary>
public partial class RoboDeVestido : Node
{
	private static GameClient? C => GameClient.Instance;

	private static World? Mundo => World.Instancia;

	private Node2D? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as Node2D;

	private static void Nota(string linha) => GD.Print("[vestido] " + linha);

	private double _t;
	private int _passo;
	private bool _acabou;

	private readonly List<string> _diario = [];
	private readonly List<Image> _tira = [];

	/// <summary>
	/// QUANTO ESPERAR PELO POVO, em segundos (`--vestidoespera N`).
	///
	/// O povoamento nao nasce de uma vez: a manutencao enfileira e o nascimento e drenado a
	/// <see cref="Jandirus.Core.Npc.Povoamento.NascimentosPorTique"/> por tique
	/// (`PlanetPopulation.dm:431` -- *"nasce espalhado"*). Fotografar aos 5 s pegaria meia dúzia de
	/// corpos e diria "o planeta tem pouca gente", que e uma conclusao sobre o obturador e nao sobre
	/// o jogo. Quarenta segundos cobrem a fila inteira de um planeta com folga.
	/// </summary>
	private double EsperaPeloPovo =>
		double.TryParse(Arg("--vestidoespera"), out double s) && s > 0 ? s : 40;

	private string Rotulo => Arg("--vestidorotulo") ?? "planeta";

	private static string? Arg(string nome)
	{
		string[] a = OS.GetCmdlineArgs();
		int i = Array.IndexOf(a, nome);
		return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
	}

	// =====================================================================
	// O RECORTE
	// =====================================================================
	/// <summary>
	/// O lado do recorte de um corpo, em pixels de MUNDO. Derivado do zoom na hora de recortar (e
	/// nao cravado em pixel de tela) pelo mesmo motivo do `RoboDeFera.LadoDoCorpo`: cravar daria
	/// enquadramentos diferentes em `--zoom 2` e `--zoom 3`, e duas rodadas deixariam de ser
	/// comparaveis.
	///
	/// 48 e nao 32: a armadura Saiyajin tem ombreira que sai da silhueta do corpo, e a capa do kit de
	/// elite desce abaixo do pe. Um recorte justo no boneco cortaria fora justamente a peca.
	/// </summary>
	private const int LadoDoCorpo = 48;

	private static int Zoom => Math.Max(1, Mundo?.ZoomDeTeste ?? 2);

	/// <summary>
	/// O quadro JA DESENHADO, recortado em volta de um node. Irmao do `RoboDeFera.Recorte` e pelo
	/// mesmo motivo escrito la: o corpo nem sempre esta no centro da tela (a camera para nas beiradas
	/// do mapa), entao o recorte sai da posicao de TELA do node e nao do centro do viewport.
	/// </summary>
	private Image? Recorte(Node2D quem)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;

		Vector2 pos = quem.GetGlobalTransformWithCanvas().Origin;

		// ============================ QUEM ESTA FORA DA TELA NAO E FOTOGRAFADO ============================
		// O `Clamp` abaixo NAO devolve vazio pra quem esta fora do quadro: ele gruda o recorte na borda
		// mais proxima e entrega um pedaco de CENARIO. A primeira rodada saiu com duas celulas de mato
		// na tira, e mato ao lado de um boneco vestido le-se como "este aqui esta pelado" -- a bancada
		// inventaria um defeito. Quem nao esta na tela sai do julgamento e continua no diario.
		// =============================================================================================
		if (pos.X < 0 || pos.Y < 0 || pos.X >= img.GetWidth() || pos.Y >= img.GetHeight()) return null;

		int lado = Mathf.Min(LadoDoCorpo * Zoom, Mathf.Min(img.GetWidth(), img.GetHeight()));
		int x = Mathf.Clamp((int)pos.X - lado / 2, 0, img.GetWidth() - lado);
		int y = Mathf.Clamp((int)pos.Y - lado / 2, 0, img.GetHeight() - lado);

		Image corte = img.GetRegion(new Rect2I(x, y, lado, lado));
		// FORMATO UNICO ANTES DE COLAR: o `BlitRect` da montagem nao converte, e o viewport pode
		// devolver um formato que nao e o da folha montada. Sem esta linha a tira sai preta em
		// algumas maquinas e perfeita em outras -- o pior tipo de defeito de bancada.
		corte.Convert(Image.Format.Rgba8);
		return corte;
	}

	/// <summary>
	/// VIZINHO-MAIS-PROXIMO, e nao bilinear -- ver `RoboDeFera.Ampliar`. A pergunta aqui e "de que
	/// cor e esta roupa", e suavizacao inventa cor entre dois pixels vizinhos.
	/// </summary>
	private static Image Ampliar(Image src, int vezes)
	{
		Image copia = Image.CreateEmpty(src.GetWidth(), src.GetHeight(), false, src.GetFormat());
		copia.BlitRect(src, new Rect2I(0, 0, src.GetWidth(), src.GetHeight()), Vector2I.Zero);
		copia.Resize(src.GetWidth() * vezes, src.GetHeight() * vezes, Image.Interpolation.Nearest);
		return copia;
	}

	private void Salvar(Image img, string sufixo)
	{
		string caminho = ProjectSettings.GlobalizePath($"user://vestido-{Rotulo}-{sufixo}.png");
		img.SavePng(caminho);
		Nota($"  ok     foto {sufixo} -> {caminho}");
	}

	// =====================================================================
	// O PALCO
	// =====================================================================
	/// <summary>
	/// A JANELA GRANDE, e ela nao e enfeite. O povo nasce espalhado em +-10 tiles do ponto de berco
	/// (`PlanetPopulation.dm:283`), ou seja num quadrado de 640 px de mundo. Numa janela de 1280x720
	/// com `--zoom 2` cabem 640x360 px de mundo: metade do povo ficaria fora do quadro por
	/// aritmetica, e a foto diria "ha tres pessoas aqui".
	/// </summary>
	private static void JanelaGrande() => DisplayServer.WindowSetMode(DisplayServer.WindowMode.Maximized);

	// =====================================================================
	// A NOITE
	// =====================================================================
	/// <summary>
	/// O `CanvasModulate` do ceu -- ver <see cref="ApagarANoite"/>.
	/// </summary>
	private CanvasModulate? Ambiente =>
		Mundo?.GetNodeOrNull<Iluminacao>("Iluminacao")?.GetNodeOrNull<CanvasModulate>("Ambiente");

	/// <summary>
	/// APAGA O FILTRO DE NOITE PRA A FOTO DE JULGAR COR -- e ela nao substitui a foto do jogo, ela
	/// vem ALEM dela.
	///
	/// ============================ POR QUE PRECISA, E POR QUE NAO E TRAPACA ============================
	/// A primeira rodada desta bancada caiu em Vegeta as 22h locais e saiu com nove bonecos pretos
	/// sobre chao preto. Nao havia nada de errado com a roupa: o `CanvasModulate` do ceu multiplica a
	/// cena INTEIRA depois do fragment (medido no `RoboDeTintaNoMundo`), e a 13% de brilho nenhuma cor
	/// e julgavel. Uma bancada que entregasse aquela foto ao dono estaria medindo a HORA e chamando o
	/// resultado de "roupa".
	///
	/// A hora nao da pra escolher: `--horateste` acerta a TERRA, e Vegeta e Namek tem defasagem e
	/// rotacao proprias (`GameServer.Ceu.AjustarCeuDaTerra` diz isso na propria documentacao). Entao
	/// em vez de sortear a hora certa, apaga-se o veu -- que nao toca em sprite, camada, material nem
	/// tinta: e literalmente o mesmo quadro sem a multiplicacao do ceu.
	///
	/// SE APAGA O NODE, e nao a cor dele: a <see cref="Iluminacao"/> reescreve a cor a cada quadro
	/// pelo ceu do planeta, entao um branco escrito aqui nunca chegaria a ser desenhado -- foi assim
	/// que a primeira versao do `--diagtintamundo` mediu razao 1,000 e quase derrubou a suspeita
	/// certa. O `Visible` ninguem reescreve.
	///
	/// E A FOTO DO JOGO SAI ANTES, com a noite no lugar: as duas juntas separam "esta escuro" de
	/// "esta pelado", que e a unica confusao que esta bancada pode produzir.
	/// ==============================================================================================
	/// </summary>
	private void ApagarANoite(bool apagar)
	{
		if (Ambiente is { } a) a.Visible = !apagar;
	}

	// =====================================================================
	// A INTERFACE
	// =====================================================================
	/// <summary>Quem foi apagado pra o recorte, pra devolver exatamente esses e mais ninguem.</summary>
	private readonly List<CanvasLayer> _apagados = [];

	/// <summary>
	/// TODA CAMADA DE INTERFACE SAI DA FRENTE, e o alvo e a CLASSE e nao tres nomes conhecidos.
	///
	/// ============================ POR QUE NAO E `Hud` MAIS `Chat` ============================
	/// Foi assim na primeira versao, e a rodada de Namek voltou com a tira inteira coberta pelo menu
	/// P (a aba `Body`/`Forms` e o texto do tutorial), que e um terceiro `CanvasLayer` que ninguem
	/// tinha lembrado de listar. Uma lista de nomes so cobre a interface que ja existia quando ela foi
	/// escrita -- e o proximo painel que alguem criar volta a tapar a foto, em silencio.
	///
	/// O mundo NAO e um `CanvasLayer` (ele mora na arvore do `World`, no canvas padrao), entao apagar
	/// a classe inteira apaga interface e nada mais.
	///
	/// SO OS QUE ESTAVAM VISIVEIS entram na lista: devolver `Visible = true` a quem ja estava apagado
	/// acenderia painel que o jogo tinha fechado.
	/// ====================================================================================
	/// </summary>
	private void EsconderAInterface()
	{
		_apagados.Clear();
		Varrer(GetTree().Root);

		void Varrer(Node no)
		{
			foreach (Node f in no.GetChildren())
			{
				if (f is CanvasLayer cl && cl.Visible) { cl.Visible = false; _apagados.Add(cl); }
				Varrer(f);
			}
		}
	}

	private void DevolverAInterface()
	{
		foreach (CanvasLayer cl in _apagados)
			if (IsInstanceValid(cl)) cl.Visible = true;
		_apagados.Clear();
	}

	/// <summary>
	/// OS VIZINHOS QUE ESTAO NA ARVORE, do mais perto pro mais longe.
	///
	/// A busca e pelos nodes `Remoto&lt;id&gt;` (ver `World.AoReceberSnapshot`) e nao por uma lista nova:
	/// o que se quer fotografar e o que foi DESENHADO, e o desenho e a arvore. Perguntar ao `_remotos`
	/// por um caminho de bancada seria a segunda fonte da mesma resposta.
	/// </summary>
	private List<(int Id, Node2D No)> Vizinhos()
	{
		var fora = new List<(int, Node2D)>();
		Node? raiz = Mundo;
		if (raiz == null) return fora;

		Vector2 eu = Corpo?.GlobalPosition ?? Vector2.Zero;
		Empilhar(raiz, fora);
		fora.Sort((a, b) => a.Item2.GlobalPosition.DistanceSquaredTo(eu)
							 .CompareTo(b.Item2.GlobalPosition.DistanceSquaredTo(eu)));
		return fora;
	}

	private static void Empilhar(Node no, List<(int, Node2D)> fora)
	{
		foreach (Node f in no.GetChildren())
		{
			if (f is Node2D n2 && f.Name.ToString().StartsWith("Remoto", StringComparison.Ordinal)
				&& int.TryParse(f.Name.ToString()[6..], out int id))
				fora.Add((id, n2));
			Empilhar(f, fora);
		}
	}

	/// <summary>
	/// O QUE ESTE CORPO DIZ VESTIR -- lido da ficha que o CLIENTE recebeu pelo fio, e nao do Core.
	///
	/// A diferenca e a bancada inteira: `RoupaDeNpc.Vestir` responde o que o servidor DECIDIU, e essa
	/// resposta ja e medida pela `--npcteste`. O que nunca foi medido e o que ATRAVESSOU -- e entre
	/// uma coisa e outra ha o `Sanear` (que descarta peca fora do catalogo) e o `GetString(120)` do
	/// `Protocol` (que devolve VAZIO, e nao truncado, acima do teto).
	/// </summary>
	private static string Pecas(Appearance ap)
	{
		if (ap.Roupa.Count == 0) return "NADA";
		var nomes = new List<string>();
		foreach (PecaDeRoupa p in ap.Roupa)
		{
			string n = p.Caminho;
			int barra = n.LastIndexOf('/');
			if (barra >= 0) n = n[(barra + 1)..];
			if (n.EndsWith(".tres", StringComparison.OrdinalIgnoreCase)) n = n[..^5];
			nomes.Add(p.Cor is { } c ? $"{n}#{c.R:X2}{c.G:X2}{c.B:X2}" : n);
		}
		return string.Join("+", nomes);
	}

	/// <summary>So o que cabe num nome de arquivo -- e curto, porque Windows corta em 260.</summary>
	private static string Limpo(string s, int max)
	{
		var sb = new System.Text.StringBuilder();
		foreach (char c in s)
			sb.Append(char.IsLetterOrDigit(c) || c is '-' or '+' or '_' ? c : '_');
		string t = sb.ToString();
		return t.Length <= max ? t : t[..max];
	}

	// =====================================================================
	// O ROTEIRO
	// =====================================================================
	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ESTE MUNDO E MEU? Copiado do `RoboDeFera:_Process` e pelo mesmo motivo escrito la: com a
		// porta tomada o `--host` nao vira servidor nenhum e o cliente entra no mundo DA OUTRA SESSAO.
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			_acabou = true;
			GD.PrintErr("[vestido] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este "
					  + "mundo e de outra sessao. Suba com `--rede <outra porta>`.");
			return;
		}

		if (C is not { Connected: true } || Corpo is null) return;

		_t += delta;

		switch (_passo)
		{
			case 0:
				JanelaGrande();
				Nota($"planeta pelo berco da raca '{Arg("--raca") ?? "?"}' -- esperando "
				   + $"{EsperaPeloPovo:0} s pelo povo nascer (a fila drena por tique)");
				_t = 0; _passo = 1;
				return;

			case 1:
				if (_t < EsperaPeloPovo) return;
				_t = 0; _passo = 2;
				return;

			// UM PASSO INTEIRO ENTRE MAXIMIZAR E FOTOGRAFAR. O `GetTexture().GetImage()` devolve o
			// quadro JA DESENHADO -- fotografar no passo que mexeu na janela fotografa o
			// enquadramento ANTERIOR (a mesma armadilha documentada no `RoboDeNav`).
			//
			// A FOTO DO JOGO VEM PRIMEIRO, com a noite no lugar: e o que o dono ve quando entra.
			case 2:
				Grupo("grupo");
				ApagarANoite(true);
				// ============================ A INTERFACE SAI DA FRENTE PRO RECORTE ============================
				// A HUD e o chat sao CanvasLayer POR CIMA do mundo, e um vizinho que esteja atras deles
				// e recortado com painel e tudo -- a primeira rodada da Terra entregou uma celula com a
				// barra de vida no lugar do boneco. E "esta atras da HUD" nao e motivo pra sumir da
				// tira: sao os corpos MAIS PERTO de mim, ou seja justamente os que valem a foto.
				//
				// A foto do JOGO ja saiu acima COM a interface, que e como o dono ve a tela. Aqui o
				// assunto e a roupa.
				// =========================================================================================
				EsconderAInterface();
				_passo = 3;
				return;

			// E SO AGORA, um quadro depois de apagar o veu, o que se pode julgar por cor.
			case 3:
				Fotografar();
				ApagarANoite(false);
				DevolverAInterface();
				_passo = 4;
				return;

			default:
				Fechar();
				return;
		}
	}

	/// <summary>A tela inteira. Ver o cabecalho: e o unico enquadramento em que se ve "um grupo".</summary>
	private void Grupo(string sufixo)
	{
		Image? tela = GetViewport()?.GetTexture()?.GetImage();
		if (tela == null || tela.IsEmpty())
		{
			Nota("  --     sem foto (headless nao renderiza) -- rode COM janela");
			return;
		}
		tela.Convert(Image.Format.Rgba8);
		Salvar(tela, sufixo);
	}

	private void Fotografar()
	{
		List<(int Id, Node2D No)> vizinhos = Vizinhos();
		Nota($"corpos alheios na arvore: {vizinhos.Count}");

		// A HORA LOCAL VAI PRO DIARIO, e ela e a leitura que explica a foto do jogo: um planeta as
		// 22h sai preto, e sem este numero alguem leria a foto escura como "a roupa nao aparece".
		if (Mundo?.GetNodeOrNull<Iluminacao>("Iluminacao") is { } luz)
			_diario.Add($"hora local do planeta: {luz.Fase:0.00} (0 = meia-noite) | "
					  + $"ambiente {Ambiente?.Color.ToHtml(false) ?? "?"}");

		// -------------------------------------------------- 1. o grupo, sem o veu da noite
		Grupo("grupo-sem-noite");

		// -------------------------------------------------- 2. eu, na mesma regua
		string minhaRaca = C?.LocalId is { } meu && Mundo?.LookDeTeste(meu) is { } meuLook
			? meuLook.Raca : (Arg("--raca") ?? "?");
		string minhasPecas = C?.LocalId is { } meu2 && Mundo?.LookDeTeste(meu2) is { } meuLook2
			? Pecas(meuLook2.Ap) : "?";
		_diario.Add($"EU (jogador)  raca={minhaRaca,-14} veste={minhasPecas}");

		if (Corpo is { } eu3 && Recorte(eu3) is { } meuCorte)
		{
			_tira.Add(meuCorte);
			Salvar(Ampliar(meuCorte, 6), $"00-EU-{Limpo(minhaRaca, 14)}");
		}

		// -------------------------------------------------- 3. um por vizinho
		int n = 0;
		foreach ((int id, Node2D no) in vizinhos)
		{
			var look = Mundo?.LookDeTeste(id);
			string raca = look?.Raca ?? "?";
			string pecas = look is { } l ? Pecas(l.Ap) : "?";
			_diario.Add($"#{id,-5} raca={raca,-14} veste={pecas}");

			// SO OS OITO MAIS PERTO GANHAM ARQUIVO PROPRIO -- o resto entra no diario. Quarenta
			// PNGs por planeta seriam cento e vinte arquivos, e ninguem olha cento e vinte.
			if (n >= 8) { n++; continue; }
			if (Recorte(no) is not { } corte) { n++; continue; }

			_tira.Add(corte);
			Salvar(Ampliar(corte, 6), $"{n + 1:00}-{Limpo(raca, 14)}-{Limpo(pecas, 60)}");
			n++;
		}

		// -------------------------------------------------- 4. a tira comparativa
		if (_tira.Count > 0)
		{
			int lado = _tira[0].GetWidth();
			int colunas = Math.Min(_tira.Count, 5);
			int linhas = (_tira.Count + colunas - 1) / colunas;
			Image folha = Image.CreateEmpty(lado * colunas, lado * linhas, false, Image.Format.Rgba8);
			// O FUNDO PINTADO, e nao o que o `CreateEmpty` deixar. A primeira rodada saiu com a
			// ultima celula BRANCA (a sobra da ultima fileira), e branco ao lado de bonecos escuros
			// puxa o olho pra o unico lugar da imagem que nao e assunto nenhum.
			folha.Fill(new Color(0.08f, 0.08f, 0.10f));
			for (int i = 0; i < _tira.Count; i++)
				folha.BlitRect(_tira[i], new Rect2I(0, 0, lado, lado),
							   new Vector2I(i % colunas * lado, i / colunas * lado));
			Salvar(Ampliar(folha, 3), "tira");
		}
	}

	private void Fechar()
	{
		_acabou = true;

		string txt = ProjectSettings.GlobalizePath($"user://vestido-{Rotulo}.txt");
		var linhas = new List<string>
		{
			$"O POVO DE '{Rotulo}' -- o que cada corpo DIZ vestir (ficha recebida pelo fio)",
			$"semente do universo: 0x{C?.SeedDoUniverso:X16}",
			"",
		};
		linhas.AddRange(_diario);
		System.IO.File.WriteAllText(txt, string.Join("\n", linhas));
		Nota($"diario -> {txt}");

		foreach (string l in _diario) Nota("  " + l);
		Nota("FIM");
		GetTree().Quit();
	}
}
