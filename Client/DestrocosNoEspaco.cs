using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// ============================ O CAMPO DE DESTROCOS DE UM MUNDO MORTO ============================
/// *"onde ficava o planeta vao ter uns asteroides/rochas q vao girar lentamente e se afastar de onde
/// era o planeta pra representar os pedacos do planeta"* -- pedido literal do dono.
///
/// **ESTE NODE NAO GUARDA POSICAO DE NADA.** Ele guarda a RECEITA de cada pedaco (rumo, distancia de
/// partida, distancia de chegada, escala, quadro inicial e velocidade de cambaleio), resolvida uma
/// unica vez no nascimento, e por quadro ele so pergunta ao relogio onde a receita esta agora. A conta
/// mora no Core, em <see cref="DestrocosDeMundo"/>, e o cabecalho de la explica por que ela e funcao
/// pura -- inclusive os dois numeros medidos que descartaram sincronizar pedaco a pedaco.
///
/// ============================ O RELOGIO E O QUE JA VIAJAVA ============================
/// `t` = quantos segundos se passaram desde o estouro = **menos** o `SegundosAteOEstouro` do
/// `GameClient`, que ja e derivado do `EstadoDaMorte.Faltam` do `S2C.Mortos`. Nao entrou um byte novo
/// no protocolo por causa deste efeito, e o servidor nao ganhou um campo por asteroide.
///
/// O que MUDOU do outro lado foi uma linha de sinal: o `Faltam` de um planeta ja destruido continua
/// **descendo pra baixo de zero** ate o fim da janela, em vez de congelar em 0 (ver
/// `GameServer.TickDaDestruicao`). Sem isso, quem chegasse na orbita 30 s depois da explosao nao teria
/// como saber HA QUANTO TEMPO o mundo morreu -- e nao veria destroco nenhum, enquanto quem estava
/// online veria. Duas telas discordando e exatamente o que o *"server sync"* do dono proibe.
/// ==================================================================================
///
/// ============================ POR QUE ELE NAO E FILHO DO PLANETA ============================
/// O `PlanetaDesenhado` **se mata** `SegundosDoEstouro` depois do prazo -- e tem que se matar, porque
/// o pedido do dono e que *"ele vai sumir do espaco"*. Um campo de destrocos pendurado nele morreria
/// junto, 2,2 s depois de nascer. Entao ele nasce IRMAO: `Estourar()` o entrega ao pai
/// (`World._orbes`), e a partir dai as duas vidas sao independentes.
/// ========================================================================================
/// </summary>
public partial class DestrocosNoEspaco : Node2D
{
	/// <summary>A folha do original -- `Icons/Misc/Asteroid5112013.dmi`, 16 quadros de 128x128.</summary>
	public const string Folha = "res://Assets/Sprites/Misc/Asteroid5112013.tres";

	/// <summary>A semente do planeta que morreu. E ela que faz duas telas verem os MESMOS cacos.</summary>
	public ulong Seed;

	/// <summary>O raio que o planeta tinha. Decide quantos pedacos, o tamanho deles e o alcance.</summary>
	public float Raio = 120;

	/// <summary>
	/// A CHAVE do planeta morto -- e por ela que este node pergunta as horas ao <see cref="GameClient"/>.
	/// Ver <see cref="ChaveDePlaneta"/>: identidade honesta, e nao o nome.
	/// </summary>
	public ChaveDePlaneta Chave;

	/// <summary>O nome do node, na convencao do `P_&lt;nome&gt;` que o `PlanetaDesenhado` ja usa.</summary>
	public static string NomeDoNode(string nomeDoPlaneta) => "D_" + nomeDoPlaneta;

	/// <summary>
	/// ============================ O FUNIL UNICO DE CRIACAO -- E POR QUE ELE PRECISA EXISTIR ============================
	/// Dois lugares montam campo de destroco, e os dois sao necessarios:
	///   * `PlanetaDesenhado.Estourar()` -- pra quem estava OLHANDO no instante da explosao. O
	///     `DesenharPlanetas` nao roda de novo quando um planeta morre (ele e assinado no
	///     `VizinhancaMudou`, e a lista de mortos chega por outro canal), entao sem esta porta o
	///     rescaldo so apareceria no proximo passo de chunk;
	///   * `World.DesenharPlanetas()` -- pra quem CHEGOU DEPOIS, pra quem cruzou uma fronteira de chunk
	///     (que destroi e recria todos os orbes) e pra quem relogou. Sem esta porta, atravessar uma
	///     borda no meio do rescaldo o apagaria pra sempre.
	///
	/// As duas montam com os MESMOS parametros e a receita e funcao pura, entao o campo sai identico
	/// pelas duas -- mas montar DOIS no mesmo lugar dobraria a densidade, e o olho pega isso na hora.
	///
	/// A BUSCA E PELA CHAVE E NAO PELO NOME: `DesenharPlanetas` comeca com um `QueueFree` em todos os
	/// filhos, e um node ja marcado pra morrer continua respondendo por `GetNodeOrNull` -- procurar por
	/// nome acharia o cadaver e o campo nunca mais seria remontado. `IsQueuedForDeletion` e a unica
	/// pergunta que separa "ja existe" de "esta indo embora".
	/// ==============================================================================================================
	/// </summary>
	public static DestrocosNoEspaco Garantir(
		Node pai, string nomeDoPlaneta, Vector2 onde, float raio, ulong seed, ChaveDePlaneta chave)
	{
		foreach (Node n in pai.GetChildren())
			if (n is DestrocosNoEspaco d && !d.IsQueuedForDeletion() && d.Chave.Equals(chave))
				return d;

		var novo = new DestrocosNoEspaco
		{
			Name = NomeDoNode(nomeDoPlaneta),
			Position = onde,
			Raio = raio,
			Seed = seed,
			Chave = chave,
		};
		pai.AddChild(novo);
		return novo;
	}

	private DestrocosDeMundo.Pedaco[] _receita = [];
	private AnimatedSprite2D[] _cacos = [];
	private SpriteFrames? _folha;
	private bool _montado;

	public override void _Ready()
	{
		// O MESMO DEGRAU DO PLANETA: atras dos corpos e na frente do ceu de estrelas. O caco e a
		// mesma materia que o disco era, e um destroco desenhado por cima de um jogador leria como
		// nave passando na frente da camera.
		ZIndex = -60;

		// EXISTIR NA PASTA NAO E ESTAR IMPORTADO -- a mesma guarda da `PedrasDaAgonia` e do chao
		// solto da cinematica: um `.tres` cujo `.png` nao foi importado carrega NULO e o efeito some
		// calado. Esta arte em particular passou de 1 de agosto ate agora **importada e sem um unico
		// consumidor**, entao o caso "ela nao resolve" e tudo menos hipotetico.
		if (ResourceLoader.Exists(Folha))
			_folha = ResourceLoader.Load<SpriteFrames>(Folha);

		if (_folha == null)
			GD.PushWarning($"[destrocos] `{Folha}` nao resolve -- o planeta vai sumir sem deixar caco.");
	}

	/// <summary>
	/// Por quadro: **uma leitura de dicionario, uma subtracao e o <see cref="AplicarTempo"/>**.
	///
	/// Nada aqui varre lista, sorteia, aloca ou consulta o servidor. *"Nada pesado dentro do tique"* e
	/// requisito explicito desta tarefa, e a conta que sobra por quadro esta medida no Core: um
	/// `Math.Pow` pro campo inteiro e uma multiplicacao de vetor por caco.
	/// </summary>
	public override void _Process(double delta)
	{
		if (GameClient.Instance is not { } cli) return;

		// AUSENCIA E A RESPOSTA: sem prazo, este planeta nao esta em contagem nenhuma -- ou nunca
		// morreu, ou foi RESSUSCITADO por admin (o `_agoniaAte` perde a chave no `AplicarMortos`). Nos
		// dois casos nao ha rescaldo pra desenhar, e o campo se recolhe.
		if (cli.SegundosAteOEstouro(Chave) is not { } falta) { QueueFree(); return; }

		AplicarTempo(-falta);
	}

	/// <summary>
	/// ESTADO -> RECEITA -> PIXEL, num metodo so.
	///
	/// Separado do <see cref="_Process"/> pelo mesmo motivo do `PlanetaDesenhado.AplicarAgonia` e do
	/// `GameClient.AplicarMortos`: e o unico jeito de uma bancada percorrer os 60 s da janela em
	/// dezenas de quadros, sem servidor, sem rede e sem esperar um minuto de relogio -- exercitando o
	/// MESMO caminho que o jogo percorre.
	/// </summary>
	/// <param name="segundosDesdeOEstouro">
	/// Negativo = o planeta ainda nao estourou (o campo espera, invisivel). Acima da janela = acabou,
	/// e o node se recolhe.
	/// </param>
	internal void AplicarTempo(double segundosDesdeOEstouro)
	{
		// ============================ O DESPAWN E O FIM DA JANELA, E NAO UMA LIMPEZA ============================
		// O dono pediu o despawn com a razao dele: *"pro servidor n ter q ficar gastando tempo de tick
		// pra ver a posicao de asteroides"*. Como nao ha posicao no servidor, nao ha o que limpar la --
		// e aqui o que acontece e o node parar de existir junto com o motivo dele. Nao ha o modo de
		// falha classico do despawn (o objeto que ficou pendurado porque quem devia limpa-lo morreu
		// antes).
		// ==================================================================================================
		if (segundosDesdeOEstouro >= DestrocosDeMundo.SegundosDaJanela) { QueueFree(); return; }

		if (segundosDesdeOEstouro < 0)
		{
			// AINDA NAO ESTOUROU. Acontece no quadro em que o `Estourar()` monta o campo: o prazo ainda
			// e positivo por uma fracao de segundo. Ele fica montado e invisivel em vez de nascer
			// depois -- montar custa hashes, e o instante do estouro e o pior quadro do jogo pra pagar.
			Visible = false;
			return;
		}

		if (!_montado) Montar();
		Visible = true;

		// UM `Math.Pow` PRO CAMPO INTEIRO. Ver `DestrocosDeMundo.Avanco`.
		double avanco = DestrocosDeMundo.Avanco(segundosDesdeOEstouro);

		for (int i = 0; i < _cacos.Length; i++)
		{
			Vec2 p = DestrocosDeMundo.Onde(_receita[i], avanco);
			_cacos[i].Position = new Vector2(p.X, p.Y);
		}

		// A OPACIDADE VAI NO PAI, e nao nos filhos: uma escrita por quadro em vez de vinte e quatro.
		Modulate = new Color(1, 1, 1, (float)DestrocosDeMundo.Opacidade(segundosDesdeOEstouro));
	}

	/// <summary>
	/// MONTA O CAMPO -- **uma vez na vida deste node**, e todo o custo do efeito esta aqui.
	///
	/// ============================ O GIRO E A FOLHA, E NAO O `Rotation` ============================
	/// `Asteroid5112013` ja e uma rotacao desenhada: 16 quadros de UMA pedra cambaleando (medido, ver
	/// `DestrocosDeMundo.QuadrosDaFolha`). Entao *"girar lentamente"* aqui e `SpeedScale` baixo, e o
	/// `Rotation` do node fica intocado -- girar por codigo por cima do cambaleio giraria duas vezes.
	///
	/// E cada caco comeca num QUADRO diferente (`Frame`), senao os dezesseis tombam em unissono e o
	/// campo denuncia na hora que e um efeito e nao escombro. Mesma disciplina da fase por
	/// `INSTANCE_CUSTOM` nos raios de forma.
	/// ==========================================================================================
	/// </summary>
	private void Montar()
	{
		_montado = true;
		if (_folha == null) return;

		string anim = _folha.GetAnimationNames()[0];
		int n = DestrocosDeMundo.Quantos(Raio);

		_receita = new DestrocosDeMundo.Pedaco[n];
		_cacos = new AnimatedSprite2D[n];

		for (int i = 0; i < n; i++)
		{
			DestrocosDeMundo.Pedaco r = DestrocosDeMundo.De(Seed, i, Raio);
			_receita[i] = r;

			var s = new AnimatedSprite2D
			{
				Name = "Caco" + i,
				SpriteFrames = _folha,
				Animation = anim,
				Scale = Vector2.One * (float)r.Escala,
				SpeedScale = (float)r.Giro,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			};
			AddChild(s);
			s.Play();
			s.Frame = r.Quadro;   // DEPOIS do `Play()`: ele reinicia a animacao no quadro 0.
			_cacos[i] = s;
		}
	}

	// =====================================================================
	// AS SONDAS DA BANCADA -- resultado, e nao pedido
	// =====================================================================
	/// <summary>Quantos cacos existem AGORA na arvore. Ver o robo da agonia.</summary>
	public int CacosDeTeste => _cacos.Length;

	/// <summary>Onde eles estao, no mundo. Pra a bancada medir afastamento e determinismo.</summary>
	public Vector2[] OndeDeTeste
	{
		get
		{
			var l = new Vector2[_cacos.Length];
			for (int i = 0; i < _cacos.Length; i++)
				l[i] = IsInstanceValid(_cacos[i]) ? _cacos[i].GlobalPosition : Vector2.Zero;
			return l;
		}
	}

	/// <summary>Em que quadro da folha cada caco esta. Pra provar que eles nao cambaleiam juntos.</summary>
	public int[] QuadrosDeTeste
	{
		get
		{
			var l = new int[_cacos.Length];
			for (int i = 0; i < _cacos.Length; i++)
				l[i] = IsInstanceValid(_cacos[i]) ? _cacos[i].Frame : -1;
			return l;
		}
	}

	/// <summary>A velocidade da folha de cada caco -- o "girar lentamente", medido no node.</summary>
	public float[] GirosDeTeste
	{
		get
		{
			var l = new float[_cacos.Length];
			for (int i = 0; i < _cacos.Length; i++)
				l[i] = IsInstanceValid(_cacos[i]) ? _cacos[i].SpeedScale : -1f;
			return l;
		}
	}
}
