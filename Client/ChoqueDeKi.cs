using Godot;
using Jandirus.Core.World;

namespace Jandirus.Client;

/// <summary>
/// A ESTRELA DO EMBATE -- o que se ve no ponto onde dois feixes se prensam (ou onde um feixe se prensa
/// contra as maos de quem o segura na guarda).
///
/// ============================ O QUE HAVIA ALI ERA QUASE NADA ============================
/// O `objects.dm:287` troca o quadro da cabeca pro `struggle` e e so. O port desenhava menos: duas
/// cabecas encostadas e, uma vez por segundo, o anel do `Baque` -- e quem assistia de fora nem tinha
/// como saber que aquilo era uma disputa.
///
/// ============================ E A PRIMEIRA TENTATIVA O DONO REPROVOU ============================
/// A primeira versao deste node era uma bola de ruido com agulhas, aneis e descargas. O dono viu no
/// jogo: *"To achando a colisao mt feia sendo sincero, queria q vc refizesse e veja essas imagens de
/// referencia"* -- e mandou o choque do Kamehameha contra o Galick Ho, no anime e num desenho de fa.
/// O que esta aqui e a leitura daquelas duas imagens, peca por peca:
///
///   * a ESTRELA de pontas duras, com o miolo branco e a metade de cada lutador na cor dele -- e o
///     `ChoqueDeKi.gdshader`, e la esta escrito por que ela e desenhada em degraus;
///   * as PEDRAS SUBINDO do chao em volta, que estao nas duas imagens -- ver <see cref="LevantarUmaPedra"/>.
///
/// (As imagens tambem mostram os FEIXES ENGROSSANDO ate o encontro, e isso chegou a ser feito -- um cone
/// no `FeixeDeKi.gdshader`. O dono viu e dispensou: *"N precisa fazer a ponta do beam ficar maior na
/// colisao, so a estrela no centro do choque basta, ja fica legal"*. O feixe numa disputa e o feixe de
/// sempre; o choque e so este node.)
///
/// ============================ ELE NAO SABE DE REGRA NENHUMA ============================
/// Quem ganha, quanto falta, que letra apertar: nada disso passa por aqui. O `World` o cria quando ve
/// uma ponta prensada (`TickDosChoquesDeKi`), diz a ele onde ela esta a cada quadro e o solta quando
/// ela deixa de estar. A posicao e a do FEIXE DESENHADO -- e por isso que a estrela anda junto quando
/// um dos dois empurra o outro, sem pacote nenhum dizendo "o ponto mudou".
///
/// O TRANCO e a unica coisa que vem do servidor: o `S2C.Clash.Baque` (zona inteira, no comeco, uma
/// vez por segundo e no empate) cai aqui como um soco na estrela -- ver <see cref="Tranco"/>.
/// ======================================================================================
/// </summary>
public partial class ChoqueDeKi : Node2D
{
	/// <summary>
	/// A UNIDADE DAS PONTAS pelo tamanho do corpo: `ponta = isto x raiz(raio)`. Raiz, e nao proporcao:
	/// o corpo de um Final Flash e cinco vezes o de um Kamehameha, e pontas cinco vezes maiores sairiam
	/// da tela -- com a raiz elas pouco mais que dobram. Num Kamehameha da uns 20 px de unidade, ou
	/// seja, a agulha mais comprida passa uns 110 px do corpo: a estrela e a maior coisa da tela, como
	/// nas imagens do dono.
	/// </summary>
	private const float PontaPorRaizDoRaio = 3.6f;

	/// <summary>
	/// O TETO DO CORPO, em px. Um Final Flash de escala 4 tem 116 px de cabeca: pela conta do
	/// <see cref="CorpoPara"/> a lente branca sairia com 272 de altura numa tela que mostra 240 (a foto da
	/// `--diagembateki` saiu inteira branca). Com o teto, a estrela de dois feixes-parede e uma estrela no
	/// meio de duas paredes, e nao um clarao que apaga a cena. Nenhum feixe de escala 1 chega perto dele (o
	/// maior, o Big Fire, da 51).
	/// </summary>
	private const float CorpoMaximo = 56f;

	/// <summary>
	/// O SEMI-EIXO DE TRAVES DO CORPO que o choque deste feixe merece, em px: a cabeca dele e mais outro
	/// tanto (120%, com teto de 20 px), mais 2 de folga. E a lente que tapa as duas pontas com sobra -- num
	/// Kamehameha da 33 px, pouco mais que o dobro da cabeca.
	///
	/// O teto de 20 px no ganho existe porque a escala nao tem teto: sem ele o corpo cresceria na proporcao
	/// de um feixe-parede, e quem segura o resto e o <see cref="CorpoMaximo"/>.
	/// </summary>
	public static float CorpoPara(PintorDeKi.MedidasDoFeixe m)
	{
		float cabeca = Mathf.Max(m.Cabeca, m.Raio);
		return cabeca + Mathf.Min(cabeca * 1.2f, 20f) + 2f;
	}

	/// <summary>Em quanto tempo a estrela acende e apaga. Curto: um embate comeca num tapa e acaba num estouro.</summary>
	private const float SegundosDeEntrada = 0.12f, SegundosDeSaida = 0.20f;

	/// <summary>
	/// AS PEDRAS: de quanto em quanto tempo uma sobe, ate que distancia do encontro (em tiles), quanto
	/// tempo ela fica no ar e quanto sobe nesse tempo (px). Com estes numeros ha umas dez no ar por vez,
	/// espalhadas -- nas duas imagens do dono elas pontuam a cena, nao a cobrem.
	/// </summary>
	private const float SegundosEntrePedras = 0.15f, TilesDasPedras = 4.5f;
	private const float VidaMinimaDaPedra = 1.0f, VidaMaximaDaPedra = 2.1f;
	private const float SubidaMinimaDaPedra = 14f, SubidaMaximaDaPedra = 46f;

	/// <summary>
	/// Pra onde a cor do chao e puxada pra virar cor de pedra: capim solta torrao, e nao folha. CLARO de
	/// proposito -- este e o tom da face iluminada; a sombra e o miolo saem dele pelos tons da lasca.
	/// </summary>
	private static readonly Color TomDePedra = new(0.74f, 0.63f, 0.50f);

	/// <summary>
	/// O CHAO SOLTO DESTE PONTO, pela cor dele -- ou nulo, se dali nao sai pedra. Quem responde e o `World`
	/// (celula livre, sem agua, e a zona nao e o espaco). Nulo o campo inteiro = sem mundo (uma bancada sem
	/// rede): nenhuma pedra sobe.
	///
	/// Pedra nao sai de dentro de parede -- `if(T &amp;&amp; !T.density)` (`lssjbuff.dm:471`), a mesma guarda das
	/// pedras da transformacao e da agonia do planeta -- e nao sobe do vacuo: o dono ja reprovou por escrito
	/// cascalho caindo onde nao ha chao (ver a nota do `FimDeProjetil.Mundo` no `World.AoMorrerTiro`).
	/// </summary>
	public static Func<Vector2, Color?>? ChaoSoltoEm;

	private ShaderMaterial? _mat;
	private CpuParticles2D? _faiscas;
	private double _idade, _ateAPedra;
	private float _forca, _tranco;
	private float _raio = 30f, _raioNoEixo = 18f;
	private Vector2 _eixo = Vector2.Right;
	private Color _corA = Colors.White, _corB = Colors.White;
	private bool _saindo;

	/// <summary>
	/// O EMBATE E RENTE AO CHAO? Dois feixes que se encontram la em cima nao levantam pedra nenhuma.
	/// Escrito pelo `World` junto com o resto, a partir da altura em que os feixes voam.
	/// </summary>
	public bool NoChao = true;

	/// <summary>
	/// SEM LUZ PROPRIA: a estrela nao pendura a <see cref="LuzDeKi"/> dela. So o ensaio do `Aquecimento` a pede assim,
	/// como pede ao tiro (`ProjetilDesenhado.SemLuz`): no palco dele quem ilumina e a luz do palco, e de um lado so.
	/// A luz de ki nasce quando o MUNDO esta escuro, e o ensaio pode cair nos primeiros quadros de um mundo escuro --
	/// a da estrela acendia o lado que tinha de ficar no escuro. Ver `Aquecimento.PorAEstrela`.
	/// </summary>
	public bool SemLuz;

	/// <summary>O semi-eixo de traves do corpo agora, em px. Pra bancada.</summary>
	public float RaioDeTeste => _raio;

	/// <summary>A unidade das pontas, em px -- ver <see cref="PontaPorRaizDoRaio"/>.</summary>
	private float Ponta => PontaPorRaizDoRaio * Mathf.Sqrt(_raio);

	/// <summary>
	/// MEIO LADO DO QUAD: o teto do que o `ChoqueDeKi.gdshader` pinta, e os numeros estao escritos la -- o
	/// risco mais comprido passa 6 `ponta` do corpo (a agulha mais comprida, 5,55), o halo chega a 1,5
	/// corpo + 3 `ponta`, e tudo incha ate 1,33x com o pulso e o tranco. Menos que isso e a borda reta do
	/// quad corta as pontas.
	/// </summary>
	private float MeioQuadro
	{
		get
		{
			float corpo = Mathf.Max(_raio, _raioNoEixo), ponta = Ponta;
			return 1.33f * Mathf.Max(corpo + 6f * ponta, 1.5f * corpo + 3f * ponta) + 2f;
		}
	}

	/// <summary>A estrela ja esta apagando (o embate acabou)? Pra bancada.</summary>
	public bool SaindoDeTeste => _saindo;

	public override void _Ready()
	{
		// POR CIMA DOS FEIXES: a estrela TAPA o lugar onde as duas pontas se encontram, e e ela que
		// esconde a emenda entre dois desenhos que nao sabem um do outro.
		ZIndex = PintorDeKi.CamadaDoChoque;

		if (ResourceLoader.Load<Shader>(PintorDeKi.ShaderDoChoque) is { } sh)
		{
			_mat = new ShaderMaterial { Shader = sh };
			_mat.SetShaderParameter("semente", PintorDeKi.SementeDeTeste ?? GD.Randf() * 10f);
			_mat.SetShaderParameter("forca", 0f);
			Material = _mat;
			EscreverNoShader();
		}

		// AS FAISCAS: os pontinhos claros das duas imagens, voando pra fora. Sao particula e nao shader
		// de proposito -- um ponto que VOA e cai fora da estrela nao cabe num quad, e pixel quadrado
		// conversa com o resto do jogo, que e todo de pixel.
		_faiscas = new CpuParticles2D
		{
			Name = "Faiscas",
			Amount = 22,
			Lifetime = 0.55,
			Direction = Vector2.Right,
			Spread = 180f,
			Gravity = Vector2.Zero,
			InitialVelocityMin = 60f,
			InitialVelocityMax = 230f,
			DampingMin = 110f,
			DampingMax = 220f,
			ScaleAmountMin = 1f,
			ScaleAmountMax = 2f,
			EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere,
			// luz propria e soma, como o resto do efeito: de noite a faisca nao pode escurecer
			Material = new CanvasItemMaterial
			{
				LightMode = CanvasItemMaterial.LightModeEnum.Unshaded,
				BlendMode = CanvasItemMaterial.BlendModeEnum.Add,
			},
			// some no fim da vida, em vez de apagar de estalo
			ColorRamp = new Gradient { Colors = [Colors.White, new Color(1, 1, 1, 0)], Offsets = [0.35f, 1f] },
		};
		AddChild(_faiscas);
		AjustarFaiscas();

		// A ESTRELA TAMBEM ACENDE O CHAO, pela mesma regra do tiro: energia que nao ilumina nada em volta
		// le como adesivo (ver `ProjetilDesenhado._Ready`). De dia a `LuzDeKi` nao cria nada.
		if (!SemLuz) LuzDeKi.Pendurar(this, _corA.Lerp(_corB, 0.5f), 3f);
	}

	/// <summary>
	/// ONDE, PRA ONDE, DE QUE TAMANHO E DE QUE CORES -- escrito pelo `World` a cada quadro.
	/// </summary>
	/// <param name="ponto">O encontro, ja na altura em que os feixes sao desenhados.</param>
	/// <param name="eixo">A direcao do feixe A (o de `corA`): as pontas compridas saem de lado dela.</param>
	/// <param name="raio">O semi-eixo do corpo de TRAVES no eixo, em px -- ver <see cref="CorpoPara"/>.</param>
	/// <param name="raioNoEixo">O semi-eixo do corpo AO LONGO do eixo, em px: cobre as cabecas.</param>
	public void Definir(Vector2 ponto, Vector2 eixo, float raio, float raioNoEixo, Color corA, Color corB)
	{
		Position = ponto;
		raio = Mathf.Min(raio, CorpoMaximo);
		raioNoEixo = Mathf.Min(raioNoEixo, raio);   // a lente nunca e mais comprida que larga
		bool mudou = !Mathf.IsEqualApprox(raio, _raio) || !Mathf.IsEqualApprox(raioNoEixo, _raioNoEixo)
				  || !eixo.IsEqualApprox(_eixo) || corA != _corA || corB != _corB;
		_eixo = eixo;
		_raio = raio;
		_raioNoEixo = raioNoEixo;
		_corA = corA;
		_corB = corB;
		if (!mudou) return;

		EscreverNoShader();
		AjustarFaiscas();
		QueueRedraw();
	}

	/// <summary>
	/// O `Baque` do servidor caiu aqui: a estrela incha num soco. E o que marca o compasso da disputa
	/// pra quem esta de fora -- um por segundo, e um mais forte no empate.
	/// </summary>
	public void Tranco() => _tranco = 1f;

	/// <summary>O embate acabou: a estrela apaga em <see cref="SegundosDeSaida"/> e o node se recolhe sozinho.</summary>
	public void Soltar()
	{
		_saindo = true;
		if (_faiscas != null) _faiscas.Emitting = false;
	}

	public override void _Process(double delta)
	{
		_idade += delta;
		_tranco = Mathf.MoveToward(_tranco, 0f, (float)delta * 3.2f);
		_forca = _saindo
			? Mathf.MoveToward(_forca, 0f, (float)delta / SegundosDeSaida)
			: Mathf.MoveToward(_forca, 1f, (float)delta / SegundosDeEntrada);

		if (_saindo && _forca <= 0f) { QueueFree(); return; }

		if (!_saindo && NoChao && (_ateAPedra -= delta) <= 0)
		{
			_ateAPedra = SegundosEntrePedras;
			LevantarUmaPedra();
		}

		if (_mat == null) return;
		_mat.SetShaderParameter("tempo", (float)(ProjetilDesenhado.TempoDeTeste ?? _idade));
		_mat.SetShaderParameter("forca", _forca);
		_mat.SetShaderParameter("tranco", _tranco);
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagembateki`): o node continua na arvore -- anda com o encontro, solta
	/// faisca, levanta pedra -- e NAO pinta a estrela. E o defeito que contar nodes nao ve ("ha UMA estrela
	/// no encontro" continua verde) e que a foto tem que reprovar. Sempre falso em jogo. Quem liga ou desliga
	/// pede um `QueueRedraw` no node: o `_Draw` so roda quando alguem pede.
	/// </summary>
	public static bool SemPinturaDeTeste;

	public override void _Draw()
	{
		if (_mat != null && !SemPinturaDeTeste) PintorDeKi.Quadro(this, Vector2.Zero, _eixo, MeioQuadro);
	}

	private void EscreverNoShader()
	{
		if (_mat == null) return;
		_mat.SetShaderParameter("raio", _raio);
		_mat.SetShaderParameter("raio_no_eixo", _raioNoEixo);
		_mat.SetShaderParameter("ponta", Ponta);
		_mat.SetShaderParameter("cor_a", new Vector3(_corA.R, _corA.G, _corA.B));
		_mat.SetShaderParameter("cor_b", new Vector3(_corB.R, _corB.G, _corB.B));
	}

	private void AjustarFaiscas()
	{
		if (_faiscas == null) return;
		_faiscas.EmissionSphereRadius = _raioNoEixo * 0.9f;
		_faiscas.Color = _corA.Lerp(_corB, 0.5f).Lerp(Colors.White, 0.6f);
	}

	// =====================================================================
	// AS PEDRAS
	// =====================================================================
	/// <summary>
	/// UMA PEDRA SOBE, de um ponto sorteado em volta do encontro -- a pressao do choque arranca lascas do
	/// chao e elas ficam flutuando, que e o que as duas imagens do dono mostram.
	///
	/// ============================ POR QUE NAO E A FOLHA `Rising Rocks` ============================
	/// O jogo ja tem pedra subindo (as da transformacao, `Transformacao.CaminhoDasPedras`), e a primeira
	/// ideia foi reusar. Aquela folha sao PONTINHOS de 2 px -- servem em volta de um corpo, e ao lado de
	/// uma estrela deste tamanho somem. Nas referencias as pedras sao pedacos de rocha com volume. Entao
	/// estas sao lascas desenhadas em pixel (<see cref="Lascas"/>), tingidas com a cor do chao de onde
	/// sairam (e a mesma regra da `PoeiraDeEstrago`: a cor amarra o efeito ao lugar).
	///
	/// ELA NAO E FILHA DA ESTRELA, e irma: a estrela escorrega pelo eixo conforme um lado empurra o outro,
	/// e uma pedra que escorregasse junto deixaria de ser chao. E assim ela termina a subida dela mesmo
	/// que o embate acabe antes -- some sozinha, como a poeira.
	///
	/// NAO E `unshaded`, de proposito: pedra e coisa do mundo. De noite ela escurece com o cenario e e
	/// acesa pela luz do proprio choque.
	/// ==============================================================================================
	/// </summary>
	private void LevantarUmaPedra()
	{
		if (ChaoSoltoEm == null || GetParent() is not { } pai) return;

		const int T = ZoneCollision.TileSize;
		float longe = (0.8f + GD.Randf() * (TilesDasPedras - 0.8f)) * T;
		Vector2 onde = Position + Vector2.FromAngle(GD.Randf() * Mathf.Tau) * longe;
		if (ChaoSoltoEm(onde) is not { } chao) return;

		ImageTexture[] lascas = Lascas;
		Color cor = chao.Lerp(TomDePedra, 0.7f);
		var pedra = new Sprite2D
		{
			Texture = lascas[(int)(GD.Randi() % (uint)lascas.Length)],
			Position = onde,
			FlipH = GD.Randf() < 0.5f,
			TextureFilter = TextureFilterEnum.Nearest,
			Modulate = new Color(cor.R, cor.G, cor.B, 0f),
			// NO AR, NA CAMADA DA ESTRELA: entre iguais quem decide e o Y, entao a pedra que sobe do lado de
			// ca passa NA FRENTE do clarao (o vulto escuro contra o branco, das imagens do dono) e a que sobe
			// do lado de la passa por tras
			ZIndex = PintorDeKi.CamadaDoChoque,
		};
		pai.AddChild(pedra);

		float vida = VidaMinimaDaPedra + GD.Randf() * (VidaMaximaDaPedra - VidaMinimaDaPedra);
		float sobe = SubidaMinimaDaPedra + GD.Randf() * (SubidaMaximaDaPedra - SubidaMinimaDaPedra);

		// sobe desacelerando (ela e EMPURRADA, nao voa), aparece num piscar e some no fim da subida
		Tween tw = pedra.CreateTween().SetParallel();
		tw.TweenProperty(pedra, "position:y", onde.Y - sobe, vida)
			.SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
		tw.TweenProperty(pedra, "modulate:a", 1f, 0.12);
		tw.TweenProperty(pedra, "modulate:a", 0f, 0.30).SetDelay(vida - 0.30);
		tw.Chain().TweenCallback(Callable.From(pedra.QueueFree));
	}

	private static ImageTexture[]? _lascas;

	/// <summary>
	/// AS LASCAS: seis pedras de 5 a 13 px, em tons de cinza (a cor entra pelo `Modulate`, como na
	/// baforada da `PoeiraDeEstrago`). Feitas uma vez por sessao e compartilhadas por todo embate.
	///
	/// O DESENHO E SEMPRE O MESMO (o sorteio daqui tem semente fixa, e nao o `GD.Randf`): o que varia de
	/// pedra pra pedra e qual das seis, espelhada ou nao, e onde. Assim duas fotos de bancada do mesmo
	/// embate nao diferem pelo FORMATO de uma pedra.
	///
	/// SAO FACETADAS: um poligono de cinco a sete quinas, partido por uma aresta inclinada em duas faces
	/// -- a de cima no tom cheio (olha pra luz), a de baixo a 58%, e a beirada da face de sombra quase
	/// preta. A primeira versao arredondava o contorno e sombreava so a beirada: a 3x de zoom saia uma
	/// batata escura. E a FACE que faz um borrao de 9 px ler como um solido.
	/// </summary>
	private static ImageTexture[] Lascas => _lascas ??= MontarLascas();

	private static ImageTexture[] MontarLascas()
	{
		int[] lados = [5, 6, 7, 9, 11, 13];
		var prontas = new ImageTexture[lados.Length];

		uint s = 0x9E3779B9u;
		float Sorte()
		{
			s ^= s << 13; s ^= s >> 17; s ^= s << 5;
			return (s & 0xFFFFFF) / 16777216f;
		}

		for (int i = 0; i < lados.Length; i++)
		{
			int lado = lados[i];
			float meio = lado / 2f;

			// O CONTORNO: de cinco a sete quinas em volta do centro, em angulo crescente, ligadas em RETA.
			var quinas = new Vector2[5 + (int)(Sorte() * 2.99f)];
			for (int k = 0; k < quinas.Length; k++)
			{
				float ang = (k + 0.15f + 0.7f * Sorte()) / quinas.Length * Mathf.Tau;
				quinas[k] = Vector2.FromAngle(ang) * (meio * (0.62f + 0.38f * Sorte()));
			}

			// A ARESTA entre a face de luz e a de sombra: passa perto do centro, descendo pra direita.
			float inclina = -0.9f + 0.5f * Sorte();
			float desvio = (Sorte() - 0.5f) * meio * 0.35f;

			bool Dentro(int x, int y)
			{
				if (x < 0 || y < 0 || x >= lado || y >= lado) return false;
				var p = new Vector2(x + 0.5f - meio, y + 0.5f - meio);
				// as quinas giram num sentido so: de dentro, toda aresta fica do mesmo lado
				for (int k = 0; k < quinas.Length; k++)
				{
					Vector2 a = quinas[k], b = quinas[(k + 1) % quinas.Length];
					if ((b - a).Cross(p - a) < 0f) return false;
				}
				return true;
			}

			Image img = Image.CreateEmpty(lado, lado, false, Image.Format.Rgba8);
			for (int y = 0; y < lado; y++)
				for (int x = 0; x < lado; x++)
				{
					if (!Dentro(x, y)) continue;
					bool beira = !(Dentro(x + 1, y) && Dentro(x - 1, y) && Dentro(x, y + 1) && Dentro(x, y - 1));
					bool luz = y + 0.5f - meio < inclina * (x + 0.5f - meio) + desvio;
					// (as menores nao tem pixel pra gastar com contorno: duas faces e so)
					float tom = luz ? 1f : beira && lado >= 7 ? 0.30f : 0.58f;
					img.SetPixel(x, y, new Color(tom, tom, tom));
				}

			prontas[i] = ImageTexture.CreateFromImage(img);
		}
		return prontas;
	}
}
