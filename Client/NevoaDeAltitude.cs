using Godot;

namespace Jandirus.Client;

/// <summary>
/// A NEVOA DE ALTITUDE: o "efeito turvo" que o dono pediu.
///
/// Um retangulo do tamanho da tela com o <c>Altitude.gdshader</c>. Ele nao desenha nada por conta
/// propria -- le o que ja foi desenhado e devolve borrado, lavado e com peso maior na beirada.
///
/// ============================ POR QUE SUAVIZA O NUMERO ============================
/// A altura chega por SNAPSHOT, a 30 Hz, e o quadro roda a 60+. Escrever o valor cru no shader
/// faria o desfoque andar aos degraus -- e desfoque em degrau e mais visivel que desfoque forte,
/// porque o olho pega a MUDANCA. A suavizacao aqui e so isso: o mesmo destino, sem escada.
///
/// Ela tambem cobre o buraco do pouso: quando o corpo encosta no chao, o servidor manda 0 de uma
/// vez, e sem a suavizacao a tela sairia de turva pra nitida num quadro so.
/// =================================================================================
/// </summary>
public partial class NevoaDeAltitude : CanvasLayer
{
	/// <summary>
	/// ATRAS DE TUDO QUE E INTERFACE, na frente do mundo.
	///
	/// ============================ ERA 1, E 1 E A CAMADA DO HUD ============================
	/// Camadas iguais desempatam pela ordem na arvore, e a nevoa nasce depois -- entao ela caia por
	/// CIMA do HUD, do boneco de vida e do chat. O dono viu na primeira foto: "a gui ta ficando toda
	/// com efeito, deveria ser tudo menos a GUI".
	///
	/// Zero e a camada do MUNDO. Uma `CanvasLayer` com o mesmo numero da canvas padrao continua
	/// sendo composta depois dela (e uma camada separada), entao a nevoa cobre o cenario e os
	/// corpos -- e qualquer interface, que comeca no 1, fica acima e intacta:
	///   0  mundo + NEVOA        2  chat            4  interacao/inventario   20 pause
	///   1  HUD                  3  menu do jogo    5  quick time event       90 carregamento
	/// =====================================================================================
	/// </summary>
	private const int Camada = 0;

	/// <summary>Quanto o valor exibido persegue o valor real por segundo.</summary>
	private const float Perseguicao = 6f;

	private ColorRect? _pano;
	private ShaderMaterial? _mat;
	private float _alvo;
	private float _agora;

	/// <summary>De 0 (chao) a 1 (teto). Quem escreve e o <see cref="World"/>, por altitude.</summary>
	public float Fracao { get => _alvo; set => _alvo = Mathf.Clamp(value, 0f, 1f); }

	/// <summary>O que o shader esta mostrando AGORA -- a bancada le isto pra provar que subiu.</summary>
	public float FracaoNaTela => _agora;

	/// <summary>O arquivo do shader. Publico pra o `Aquecimento` po-lo na fila de carga do lobby -- ver <see cref="Ensaiar"/>.</summary>
	public const string CaminhoDoShader = "res://Assets/Shaders/Altitude.gdshader";

	public override void _Ready()
	{
		Layer = Camada;

		if (NovoPano() is not { } novo) return;
		(_pano, _mat) = novo;
		_pano.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_pano);
	}

	/// <summary>
	/// O PANO DA NEVOA: o retangulo e o material dele, como a tela os usa. UMA receita pra duas casas, a da tela
	/// (<see cref="_Ready"/>) e a do palco do ensaio (<see cref="Ensaiar"/>): e isso que faz a pipeline montada no ensaio
	/// ser a que o primeiro voo acha pronta. Nulo se o shader nao carregou.
	/// </summary>
	private static (ColorRect Pano, ShaderMaterial Tinta)? NovoPano()
	{
		var sh = GD.Load<Shader>(CaminhoDoShader);
		if (sh == null) { GD.PushWarning("[nevoa] Altitude.gdshader nao carregou"); return null; }

		var tinta = new ShaderMaterial { Shader = sh };
		var pano = new ColorRect
		{
			Name = "Nevoa",
			Material = tinta,
			// O RETANGULO NAO PODE ROUBAR O CLIQUE. Ele cobre a tela inteira; sem isto, mirar num
			// inimigo com o mouse pararia de funcionar assim que se saisse do chao.
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		return (pano, tinta);
	}

	/// <summary>O lado do pano do ensaio, em pixels do palco: a pipeline e do shader e do jeito de desenhar, e nao do tamanho.</summary>
	private const float PanoDoEnsaio = 32f;

	/// <summary>
	/// O ENSAIO DO LOBBY (ver `Aquecimento.AtosDaNevoa`): o pano da nevoa, pela receita de producao (<see cref="NovoPano"/>),
	/// num palco fora da tela -- pra o shader ser compilado e a pipeline dele montada ALI, e nao no quadro em que o corpo
	/// sai do chao pela primeira vez no processo.
	///
	/// ============================ O QUE ISTO TIRA DO PRIMEIRO VOO ============================
	/// Esta classe so nasce no primeiro quadro em que o corpo tem altura (`World.EfeitosDaAltura`), e o shader dela nem na
	/// fila de carga do aquecimento estava: era lido do disco, compilado e desenhado no mesmo quadro -- o da primeira
	/// decolagem. MEDIDO em 2026-10-09 pela `--diagestouro --avulsos`, o quadro em que a nevoa aparece, em relogio:
	///
	///     com o driver de video FRIO .......... 50 a 55 ms   script 6 a 7, a espera pelo shader 24 a 26, a pipeline 19 a 21
	///     com o driver quente ................. 33 ms        so a espera: e o cache de shader do Godot vazio, o de quem abre
	///                                                        o jogo pela primeira vez
	///     a gemea ILUMINADA, driver frio ...... 62 a 65 ms   60 a 62 deles a pipeline do item COM luz, montada depois da sem luz
	///
	/// A GEMEA EXISTE PORQUE O PANO E ALCANCADO PELAS LUZES DO MUNDO: ele mora na camada 0 (ver <see cref="Camada"/>), que e
	/// a faixa de camadas das luzes -- basta a aura de uma forma, um tiro de ki ou uma fogueira na tela, de noite. Quem
	/// voava de dia pagava o primeiro quadro; no primeiro voo com uma luz na tela, pagava o outro, maior.
	///
	/// DEPOIS: nenhuma pipeline nasce no quadro em que a nevoa aparece, com luz ou sem ela, e ele custa 8 a 9 ms -- uma
	/// volta do monitor --, com o driver quente ou frio.
	/// ========================================================================================
	///
	/// A MEIA ALTURA (`altura` 0,5) e SEM RELOGIO: quem persegue a altura e o <see cref="_Process"/> de uma
	/// `NevoaDeAltitude` viva, e aqui nao ha uma. Morre com o palco do aquecimento.
	/// </summary>
	public static void Ensaiar(Node2D pai, Vector2 onde)
	{
		if (NovoPano() is not { } novo) return;
		(ColorRect pano, ShaderMaterial tinta) = novo;
		pano.Name = "NevoaDoEnsaio";
		pano.Size = new Vector2(PanoDoEnsaio, PanoDoEnsaio);
		pano.Position = onde - pano.Size * 0.5f;
		tinta.SetShaderParameter("altura", 0.5f);
		pai.AddChild(pano);
	}

	public override void _Process(double delta)
	{
		if (_mat == null) return;

		float k = Mathf.Min(1f, (float)delta * Perseguicao);
		_agora = Mathf.Lerp(_agora, _alvo, k);
		if (Mathf.Abs(_agora - _alvo) < 0.002f) _agora = _alvo;

		// O NODE INTEIRO SOME NO CHAO. O shader ja sai cedo com altura 0, mas um ColorRect visivel
		// cobrindo a tela continua sendo um passe de composicao por quadro -- e voar e a excecao,
		// nao a regra: quem esta no chao nao deve pagar nada por um efeito que nao esta vendo.
		if (_pano != null) _pano.Visible = _agora > 0.001f;
		_mat.SetShaderParameter("altura", _agora);
	}
}
