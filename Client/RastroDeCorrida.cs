using Godot;

namespace Jandirus.Client;

/// <summary>
/// O RASTRO DE QUEM CORRE -- e ele e TEMPORAL, que e o que "motion blur" quer dizer.
///
/// ============================ POR QUE A VERSAO ANTERIOR PISCAVA ============================
/// A primeira tentativa era um smear no shader: quatro amostras extras da propria textura ao longo
/// do rumo do movimento. O dono reportou a piscada duas vezes, e na segunda veio com o diagnostico
/// junto -- "n teria como o efeito ser um motion blur?".
///
/// Ele apontou a natureza do defeito, nao so o sintoma. Aquilo era um borrao ESPACIAL, calculado
/// dentro do quadro ATUAL da animacao. E a animacao de corrida roda 2,2x mais rapido: a cada troca
/// de quadro do .dmi, TODO o conteudo amostrado trocava de uma vez. O borrao dava um salto por
/// quadro, e salto periodico e exatamente o que o olho le como piscada.
///
/// Eu tinha tentado consertar suavizando a DIRECAO. Nao alcancava: a descontinuidade nao estava na
/// direcao, estava na FONTE.
///
/// Motion blur de verdade mostra onde o corpo ESTEVE. Aqui, copias do corpo largadas nas posicoes
/// passadas, esmaecendo. Copia velha guarda o quadro velho -- trocar de quadro nao mexe no que ja
/// foi desenhado, entao a piscada nao tem por onde nascer.
/// ==========================================================================================
///
/// CUSTO: uma foto a cada 22 ms com vida de 0,085 s da ~4 copias vivas. Cada uma sao 3-6 Sprite2D
/// sem processamento proprio (quem conta o tempo e este node, um so). E menos trabalho por quadro
/// do que o shader que ele substitui, que rodava 5 amostras por pixel do personagem inteiro.
/// </summary>
public partial class RastroDeCorrida : Node2D
{
	// ============================ POR QUE ESTES TRES NUMEROS ============================
	// A primeira calibragem (45 ms / 0,17 s / alfa 0,34) deu o que o dono fotografou: TRES bonecos
	// nitidos e espacados. Ele nomeou os dois problemas -- "o atual tem um efeito mt comprido e n
	// da impressao de velocidade alta".
	//
	// Sao a mesma causa. Copia NITIDA e ESPACADA le como copia; o olho conta "um, dois, tres". Pra
	// ler como BORRAO as copias precisam se SOBREPOR e serem fracas o bastante pra nenhuma se
	// destacar -- ai o cerebro funde as tres num rastro so.
	//
	// Entao: intervalo pela METADE (as fotos se sobrepoem em vez de enfileirar), vida pela metade
	// (rastro curto -- rastro comprido le como lentidao, nao velocidade) e alfa quase pela metade
	// (nenhuma copia compete com o corpo). Sao MAIS fotos e um efeito MENOR.
	// ==================================================================================

	/// <summary>Intervalo entre duas fotos. Menor SOBREPOE as copias, que e o que vira borrao.</summary>
	private const double Intervalo = 0.018;

	/// <summary>Quanto cada foto dura. Vezes o intervalo, e o COMPRIMENTO do rastro.</summary>
	private const double Vida = 0.055;

	/// <summary>Opacidade da foto mais nova. Baixa de proposito: rastro nao compete com o corpo.</summary>
	private const float Alfa = 0.26f;

	private CharacterVisual? _visual;
	private Node2D? _corpo;
	private double _proxima;
	private bool _ligado;

	/// <summary>Pra onde o corpo estava indo na ultima foto. Alimenta o borrao de cada copia.</summary>
	private Vector2 _ultimoRumo = Vector2.Right;
	private Vector2 _posAnterior;

	public override void _Ready()
	{
		_corpo = GetParent<Node2D>();
		_visual = _corpo?.GetNodeOrNull<CharacterVisual>("Visual");
		SetProcess(false);
	}

	/// <summary>
	/// Liga/desliga. Chamado por quadro pelo corpo, entao a primeira linha e a comparacao.
	///
	/// DESLIGAR NAO APAGA AS FOTOS QUE JA SAIRAM -- elas terminam de esmaecer sozinhas, e e isso
	/// que faz o rastro se recolher em vez de sumir num quadro quando o jogador solta o SHIFT.
	/// </summary>
	public void Definir(bool correndo)
	{
		if (correndo == _ligado) return;
		_ligado = correndo;
		SetProcess(correndo);
		_proxima = 0;   // a primeira foto sai NO ATO de comecar a correr
		if (correndo && _corpo != null) _posAnterior = _corpo.GlobalPosition;
	}

	public override void _Process(double delta)
	{
		if (_visual == null || _corpo == null) { SetProcess(false); return; }

		// O RUMO SAI DO DESLOCAMENTO REAL entre duas fotos. Meio pixel de piso: perto de zero a
		// normalizacao vira ruido e o borrao apontaria pra qualquer lado.
		Vector2 desl = _corpo.GlobalPosition - _posAnterior;
		if (desl.LengthSquared() > 0.25f) _ultimoRumo = desl.Normalized();
		_posAnterior = _corpo.GlobalPosition;

		_proxima -= delta;
		if (_proxima > 0) return;
		_proxima = Intervalo;

		// A FOTO VAI NO PAI DO CORPO, nao neste node: ela precisa FICAR onde foi tirada enquanto o
		// corpo segue andando. Filha, ela viajaria junto -- e rastro que acompanha o dono e so um
		// borrao grudado, que e de novo o efeito errado.
		if (_corpo.GetParent() is not { } palco) return;

		// SEM TINTA: cada camada desta foto recebe o material de BORRAO tres linhas abaixo. Pedir a
		// tinta aqui seria criar um `ShaderMaterial` por camada pra descarta-lo no mesmo quadro --
		// 120 por segundo, por corpo correndo.
		Node2D foto = _visual.Fotografar(comTinta: false);
		foto.GlobalPosition = _corpo.GlobalPosition;

		// CADA COPIA VAI BORRADA no rumo do movimento. E o que faltava: copia NITIDA le como
		// copia, por mais transparente que seja. Borrada, ela nao tem borda pra o olho fixar --
		// e tres delas sobrepostas viram um rastro so, que e o que "motion blur" quer dizer.
		//
		// O borrao mora na COPIA e nao no corpo vivo de proposito: a copia congela o quadro em que
		// nasceu, entao trocar de quadro de animacao nao muda nada do que ja foi desenhado. Foi
		// exatamente isso que fez a primeira versao (borrao no corpo) estroboscopar.
		Vector2 rumo = _ultimoRumo;
		foreach (Node n in foto.GetChildren())
			if (n is Sprite2D sp) BorraoDirecional.Aplicar(sp, rumo, 1f);
		foto.ZIndex = _corpo.ZIndex - 1;   // atras do corpo: o rastro nao pode tapar o personagem
		foto.Modulate = new Color(1, 1, 1, Alfa);
		palco.AddChild(foto);

		// APAGA SOZINHA, sem node de controle. Um Tween por foto e mais barato que um _Process por
		// foto, e o `QueueFree` no fim evita o vazamento que um node orfao daria.
		Tween t = foto.CreateTween();
		t.TweenProperty(foto, "modulate:a", 0f, Vida).SetEase(Tween.EaseType.In);
		t.TweenCallback(Callable.From(foto.QueueFree));
	}
}
