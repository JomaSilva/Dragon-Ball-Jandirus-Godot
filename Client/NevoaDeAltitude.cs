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

	public override void _Ready()
	{
		Layer = Camada;

		var sh = GD.Load<Shader>("res://Assets/Shaders/Altitude.gdshader");
		if (sh == null) { GD.PushWarning("[nevoa] Altitude.gdshader nao carregou"); return; }

		_mat = new ShaderMaterial { Shader = sh };
		_pano = new ColorRect
		{
			Name = "Nevoa",
			Material = _mat,
			// O RETANGULO NAO PODE ROUBAR O CLIQUE. Ele cobre a tela inteira; sem isto, mirar num
			// inimigo com o mouse pararia de funcionar assim que se saisse do chao.
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_pano.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		AddChild(_pano);
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
