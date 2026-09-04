using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// O FANTASMA DE ASSENTAR: a construcao que saiu da mochila e espera um lugar no chao.
///
/// ============================ A GRADE DA BANCADA MORREU AQUI (2026-09-03) ============================
/// Este arquivo tinha DUAS coisas: a grade de fabricar da bancada de pesquisa (aberta pela tecla E,
/// "Fabricar...") e o fantasma. A grade foi apagada a pedido do dono -- *"agora que da pra construir
/// pelo menu P nao precisa mais ter a opcao de construir na research table"* -- porque fabricar
/// ganhou um lugar so, a aba Tech do menu P (`MenuJogo.Tech.cs`): icone, custo, o motivo de cada
/// recusa e o mesmo pacote (`SendTech("construir", id)`). A bancada de pesquisa ficou sendo o que o
/// nome diz: onde se ESTUDA.
///
/// O que sobrou e a metade que nao tem como morar num menu: assentar pede um LUGAR, e lugar se escolhe
/// com o mouse no mundo. O fantasma segue o mouse, pergunta "cabe aqui?" antes do clique (as MESMAS
/// perguntas do servidor, ver <see cref="Assentamento.DoLugar"/>) e manda o clique pelo canal de tech.
/// ====================================================================================================
/// </summary>
public partial class TelaDeConstrucao : CanvasLayer
{
	public static TelaDeConstrucao? Instancia { get; private set; }

	public override void _Ready()
	{
		Instancia = this;
		Layer = 4;
		if (GameClient.Instance is { } cli)
			cli.TechMudou += AoMudarTech;
	}

	public override void _ExitTree()
	{
		// A ASSINATURA TEM QUE SAIR JUNTO -- ver a nota no `MenuJogo._Ready`. O `GameClient`
		// sobrevive ao logout, e um assinante morto vira erro no login seguinte.
		if (GameClient.Instance is { } cli) cli.TechMudou -= AoMudarTech;
		if (Instancia == this) Instancia = null;
	}

	/// <summary>
	/// O pacote de tecnologia chegou. E o SINO do fantasma: o que estava esperando descobre se foi
	/// aceito -- ver `ResolverEspera`. (Era tambem o sino da grade, que ja nao existe.)
	/// </summary>
	private void AoMudarTech() => ResolverEspera();

	// =====================================================================
	// O FANTASMA
	// =====================================================================
	/// <summary>
	/// A CONSTRUCAO QUE ESTA NA MAO, esperando um lugar.
	///
	/// ============================ POR QUE ELA SEGUE O MOUSE ============================
	/// Antes o jogo construia embaixo dos proprios pes: nao havia o que escolher, e nao havia o que
	/// mostrar. Agora o jogador aponta -- e apontar sem ver o que vai sair dali e adivinhar. O
	/// fantasma e a resposta a "cabe aqui?" ANTES do clique, que e quando a pergunta importa.
	///
	/// ELE E SO DESENHO, E ELE E SO SEU. Nenhum pacote sai enquanto a previa esta no mouse -- e a
	/// regra 3 do dono, na letra: *"isso claramente so aparece pro jogador local"*. O `Sprite2D`
	/// nasce, anda e morre dentro deste processo; o servidor so fica sabendo do CLIQUE.
	///
	/// E QUEM DECIDE CONTINUA SENDO O SERVIDOR. O fantasma vermelho e um aviso, nao um veto: ele
	/// existe pra o jogador nao clicar as cegas, e faz as MESMAS perguntas do outro lado
	/// (<see cref="Assentamento.DoLugar"/>) justamente pra os dois nao poderem discordar por escrito.
	/// ===================================================================================
	/// </summary>
	private Sprite2D? _fantasma;
	private string _naMao = "";

	/// <summary>
	/// MANDEI O CLIQUE E ESTOU ESPERANDO A RESPOSTA -- e quantos eu tinha quando mandei.
	///
	/// ============================ POR QUE O FANTASMA NAO SOME NO CLIQUE ============================
	/// Sumia. E quando o servidor recusava (agua, obra ja ali, um passo alem do alcance -- desacordos
	/// normais numa rede), sobrava uma frase no chat e nenhuma previa: pra tentar um tile ao lado o
	/// jogador tinha que reabrir a mochila e achar o item de novo.
	///
	/// Agora ele fica na mao, congelado, ate a resposta. **E a resposta sempre chega**: o
	/// `ComandoDeTech` reenvia o catalogo depois de TODO comando (`if (cmd != "lista")`), pelo mesmo
	/// canal confiavel e ordenado do pacote de mochila -- entao quando o `TechMudou` bate aqui, a
	/// mochila local ja e a de depois da decisao. Se o item saiu dela, o servidor aceitou e o
	/// fantasma se despede; se continua la, foi recusa e ele volta a seguir o mouse.
	///
	/// NAO HA CRONOMETRO E NAO PRECISA HAVER -- o gatilho e o pacote, nao o relogio.
	/// ==========================================================================================
	/// </summary>
	private bool _esperandoResposta;
	private int _tinhaAoMandar;

	/// <summary>
	/// O NODE DA PREVIA, pra a bancada `--diaginstalar` medir o que esta NA TELA e nao o que o
	/// codigo quis. Ela confere o pai (tem que ser o `World`, ou seja e desenho local), a
	/// transparencia e a posicao -- as tres coisas que o dono descreveu.
	/// </summary>
	internal Sprite2D? FantasmaNaTela => _fantasma;

	/// <summary>O id que esta na mao, ou "" quando nao ha previa nenhuma.</summary>
	internal string NaMao => _fantasma != null ? _naMao : "";

	/// <summary>A previa mandou o clique e espera a decisao do servidor.</summary>
	internal bool EsperandoOServidor => _esperandoResposta;

	/// <summary>
	/// O QUE ESTA TELA ACHA DESTA CELULA -- a MESMA funcao que pinta o fantasma de vermelho.
	///
	/// Existe pra a bancada `--diaginstalar` poder escolher um ponto que o CLIENTE aprova e entao
	/// cobrar que o SERVIDOR aceite ele. E a pergunta que fecha as duas metades: "onde a previa diz
	/// branco, o servidor tem que assentar" -- e o contrario, onde ela diz vermelho, nada sai.
	/// </summary>
	internal static RecusaDeAssento RecusaEm(int cx, int cy) => Recusa(cx, cy);

	public void Segurar(string id)
	{
		Largar();
		if (Jandirus.Core.Items.CatalogoDeItens.Get(id) is not { } def) return;

		// A MESMA PERGUNTA DO SERVIDOR (regra 2). O menu do inventario nem desenha o botao pra item
		// pessoal -- as duas coisas leem `AcoesDoItem` --, mas quem SEGURA tem que conferir: este
		// metodo e publico e um dia alguem vai chama-lo de outro lugar.
		if (!Jandirus.Core.Items.CatalogoDeItens.PodeAssentarNoChao(id))
		{
			Chat.Sistema(Assentamento.Motivo(RecusaDeAssento.NaoEDoChao, def.Nome));
			return;
		}

		_naMao = id;
		_fantasma = new Sprite2D
		{
			// SEM ARTE, UM VULTO -- e nao o nada de antes. `Miniatura` devolvia nulo e o metodo
			// desistia EM SILENCIO: no unico compravel sem `.dmi` convertido (a Crafting Bench),
			// clicar em "Assentar no chão" fechava a mochila e nao acontecia mais nada -- sem
			// fantasma, sem mensagem, sem erro. Um retangulo do tamanho do tile e feio e honesto:
			// da pra escolher o lugar e o ciclo termina.
			Texture = Miniaturas.De(def.Arte, def.Estado) ?? Vulto(),
			Centered = false,
			// MEIO TRANSPARENTE, como o dono pediu: da pra ver o chao por baixo e julgar o encaixe.
			Modulate = Livre,
			ZIndex = 50,
		};
		World.Instancia?.AddChild(_fantasma);
		Chat.Sistema($"{def.Nome}: clique onde quer instalar. Botão direito ou Esc cancela -- "
					 + "o item continua na mochila até o servidor aceitar o lugar.");
	}

	/// <summary>Larga a previa. O item NUNCA saiu da mochila -- quem tira e o servidor, no aceite.</summary>
	public void Largar()
	{
		_fantasma?.QueueFree();
		_fantasma = null;
		_naMao = "";
		_esperandoResposta = false;
	}

	private static readonly Color Livre = new(1, 1, 1, 0.55f);
	private static readonly Color NaoCabe = new(1f, 0.45f, 0.4f, 0.55f);
	private static readonly Color Esperando = new(0.75f, 0.8f, 1f, 0.35f);

	/// <summary>O retangulo de reserva pra construcao sem `.dmi` convertido. Um tile, azulado.</summary>
	private static Texture2D Vulto()
	{
		Image img = Image.CreateEmpty(ZoneCollision.TileSize, ZoneCollision.TileSize, false,
									  Image.Format.Rgba8);
		img.Fill(new Color(0.55f, 0.7f, 0.95f));
		return ImageTexture.CreateFromImage(img);
	}

	/// <summary>
	/// O QUE IMPEDE DE ASSENTAR NESTA CELULA, do que o cliente sabe.
	///
	/// AS MESMAS PERGUNTAS DO SERVIDOR, e nao "algumas delas": alcance, parede, agua, nuvem e beirada
	/// saem do <see cref="Assentamento.DoLugar"/>; "ja tem coisa ali" sai do
	/// <see cref="Assentamento.TemCoisaEm"/> com a lista de construcoes que este cliente recebeu (o
	/// mesmo pacote traz as naves paradas -- ver `MandarObras`).
	///
	/// A LISTA PODE ESTAR UM PACOTE ATRASADA e a posicao e a INTERPOLADA, entao "cabe" aqui e um
	/// palpite bem informado e nao uma promessa. E por isso o clique nao joga o item fora.
	/// </summary>
	private static RecusaDeAssento Recusa(int cx, int cy)
	{
		if (World.Instancia is not { } mundo) return RecusaDeAssento.Pode;

		// SEM SABER ONDE EU ESTOU, NAO SE AFIRMA "LONGE DEMAIS". O corpo local pode ainda nao ter
		// sido desenhado (primeiro quadro depois de trocar de zona), e um `Vector2.Zero` de reserva
		// poria o fantasma vermelho no mundo inteiro. Quem decide o alcance nesse instante e o
		// servidor -- que sabe onde o corpo esta.
		if (mundo.PosicaoDesenhadaDe(GameClient.Instance?.LocalId ?? 0) is not { } eu)
			return RecusaDeAssento.Pode;

		RecusaDeAssento r = Assentamento.DoLugar(mundo.Colisao, new Vec2(eu.X, eu.Y), cx, cy);
		if (r != RecusaDeAssento.Pode) return r;

		const int t = ZoneCollision.TileSize;
		var centro = new Vec2(cx * t + t / 2f, cy * t + t / 2f);
		if (GameClient.Instance is { } cli
			&& Assentamento.TemCoisaEm(cli.Obras.Select(o => new Vec2(o.Pos.X, o.Pos.Y)), centro))
			return RecusaDeAssento.LugarOcupado;

		return RecusaDeAssento.Pode;
	}

	public override void _Process(double delta)
	{
		if (_fantasma == null || World.Instancia is not { } mundo) return;

		// CONGELADO ENQUANTO ESPERA. Deixa-lo seguir o mouse enquanto o servidor decide mostraria a
		// previa num lugar que nao tem nada a ver com o clique que foi mandado.
		if (_esperandoResposta) { _fantasma.Modulate = Esperando; return; }

		// A ANCORA E A CELULA, e nao o pixel do mouse: a construcao ocupa um tile inteiro, e o
		// desenho tem que cair onde a PAREDE vai cair. Ver `CatalogoDeObras.Celula`.
		Vector2 alvo = mundo.GetGlobalMousePosition();
		(int cx, int cy) = CatalogoDeObras.Celula(alvo.X, alvo.Y);
		const int t = ZoneCollision.TileSize;

		_fantasma.Position = new Vector2(cx * t, (cy + 1) * t - _fantasma.Texture.GetHeight());
		_fantasma.Modulate = Recusa(cx, cy) == RecusaDeAssento.Pode ? Livre : NaoCabe;
	}

	public override void _Input(InputEvent evento)
	{
		if (_fantasma == null) return;

		if (evento is InputEventKey { Pressed: true, Keycode: Key.Escape })
		{
			Cancelar();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (evento is not InputEventMouseButton { Pressed: true } m) return;

		if (m.ButtonIndex == MouseButton.Right) { Cancelar(); GetViewport().SetInputAsHandled(); return; }
		if (m.ButtonIndex != MouseButton.Left) return;

		// O CLIQUE E ENGOLIDO DE QUALQUER JEITO enquanto a previa esta na mao -- inclusive o que nao
		// vira pacote --, senao ele viraria um golpe ou uma marcacao de alvo no meio da escolha.
		GetViewport().SetInputAsHandled();
		if (_esperandoResposta) return;

		// ============================ O PONTO E O DO EVENTO, E NAO O DO CURSOR AGORA ============================
		// `GetGlobalMousePosition()` responde "onde o mouse esta NESTE instante"; o evento carrega
		// "onde ele estava quando o botao desceu". A diferenca e de poucos pixels num jogo com mouse,
		// e e zero quando o jogador nao esta arrastando -- mas ela e a diferenca entre assentar onde
		// o fantasma estava e assentar onde ele ja nao esta. `GetCanvasTransform().AffineInverse()`
		// e literalmente a conta que o `GetGlobalMousePosition` faz por dentro; a unica coisa que
		// muda e de qual ponto ela parte.
		// =====================================================================================================
		Vector2 alvo = World.Instancia is { } m2
			? m2.GetCanvasTransform().AffineInverse() * m.Position
			: Vector2.Zero;
		(int cx, int cy) = CatalogoDeObras.Celula(alvo.X, alvo.Y);
		const int t = ZoneCollision.TileSize;

		// ============================ O QUE O CLIENTE JA SABE, ELE NEM MANDA ============================
		// A previa ja estava vermelha; clicar mesmo assim merece a FRASE, e nao um pacote que volta
		// com a mesma frase um ida-e-volta depois. E o fantasma continua na mao pra tentar ao lado.
		// A frase e a do Core -- a mesma que o servidor diria, pra nao parecerem dois problemas.
		// ============================================================================================
		RecusaDeAssento r = Recusa(cx, cy);
		if (r != RecusaDeAssento.Pode)
		{
			Chat.Sistema(Assentamento.Motivo(
				r, Jandirus.Core.Items.CatalogoDeItens.Get(_naMao)?.Nome ?? _naMao));
			return;
		}

		// O PONTO QUE VAI PRO SERVIDOR E O CENTRO DA CELULA, e nao o pixel do clique: e assim que o
		// servidor guarda a obra, e mandar o pixel cru faria o desenho pular meio tile ao chegar.
		// PELO CANAL `Tech` -- ver o comentario longo no `Abrir`: "tech_posicionar" nunca existiu do
		// outro lado, e o clique no chao nao fazia nada.
		_tinhaAoMandar = GameClient.Instance?.Mochila.Quantos(_naMao) ?? 0;
		_esperandoResposta = true;
		GameClient.Instance?.SendTech("posicionar",
			$"{_naMao}/{cx * t + t / 2f:0}/{cy * t + t / 2f:0}");
	}

	/// <summary>Esc ou botao direito. Diz em voz alta que nada se perdeu -- e a regra 3 do dono.</summary>
	private void Cancelar()
	{
		if (_naMao.Length == 0) return;
		string nome = Jandirus.Core.Items.CatalogoDeItens.Get(_naMao)?.Nome ?? _naMao;
		Largar();
		Chat.Sistema($"você guarda {nome} de volta.");
	}

	/// <summary>
	/// A RESPOSTA DO SERVIDOR CHEGOU (ver <see cref="_esperandoResposta"/>).
	///
	/// A pergunta e sobre RESULTADO e nao sobre intencao: nao "o servidor mandou alguma coisa?", mas
	/// "o item saiu da minha mochila?". E o unico sinal que significa exatamente o que aconteceu do
	/// outro lado, e ele nao precisou de um opcode novo no protocolo.
	/// </summary>
	private void ResolverEspera()
	{
		if (!_esperandoResposta || _fantasma == null) return;

		int agora = GameClient.Instance?.Mochila.Quantos(_naMao) ?? 0;
		if (agora < _tinhaAoMandar) { Largar(); return; }   // aceitou: a obra e do mundo agora

		// RECUSOU. O motivo o servidor ja disse no chat; aqui o fantasma volta pra mao, e o item
		// nunca chegou a sair da mochila.
		_esperandoResposta = false;
	}
}
