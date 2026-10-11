using Godot;
using Jandirus.Core.Social;

namespace Jandirus.Client;

/// <summary>
/// O RADIO DO SCOUTER NO MENU P -- o bloco "Rádio" dentro do cartao do Scouter da aba Equip (o lugar onde o
/// aparelho ja e ligado e desligado). Mostra a frequencia que o SERVIDOR guarda (`GameClient.FrequenciaDoRadio`),
/// troca por um teclado numerico e ensina a tecla. As regras estao em <see cref="RadioDoScouter"/>.
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// O BLOCO DO RADIO. A pilula diz a frequencia (verde com o scouter ligado: e quando ela vale); o botao
	/// abre o teclado e fica APAGADO sem o aparelho, pelo motivo do botao de ligar -- um botao que some nao
	/// ensina o que falta.
	///
	/// A FREQUENCIA SE DIGITA NUM TECLADO DE BOTOES (<see cref="TecladoNumerico"/>), e nao numa caixa de texto,
	/// pelas duas razoes escritas la: e o que o dono pediu pros numeros do jogo, e nao rouba o teclado de
	/// quem esta andando. E ele abre POR CIMA do menu, fora da pagina: a pagina e refeita quando a ficha muda,
	/// e um teclado dentro dela morreria embaixo do dedo.
	/// </summary>
	private void BlocoDoRadio(bool temScouter, bool ligado, VBoxContainer c)
	{
		int frequencia = GameClient.Instance?.FrequenciaDoRadio ?? RadioDoScouter.FrequenciaPadrao;

		c.AddChild(new HSeparator());
		HBoxContainer cab = Cabecalho("Rádio", ligado ? Tema.Texto : Tema.TextoFraco,
									  ($"freq. {frequencia}", ligado ? RadioOuvido.CorDoRadio : Tema.TextoFraco));
		// A IDENTIDADE VAI EM METADADO: e por ele que a bancada acha a linha do radio e le a frequencia escrita.
		cab.SetMeta("radio", frequencia);
		c.AddChild(cab);

		var b = new Button { Text = "Trocar a frequência", Disabled = !temScouter };
		b.Pressed += AbrirTecladoDoRadio;
		c.AddChild(BotaoComDescricao(b, !temScouter
			? "precisa de um scouter na mochila (tecla I)"
			: $"de {RadioDoScouter.FrequenciaMinima} a {RadioDoScouter.FrequenciaMaxima}: só ouve quem estiver na mesma"));

		Nota($"Com o scouter ligado, segure {Teclas.NomeDaAcao("falar_radio")} pra falar no rádio "
		   + $"({Teclas.NomeDaAcao("falar_voz")} continua sendo a voz de perto; as duas juntas mandam pelos dois). "
		   + $"Recebe quem estiver com scouter ligado na mesma frequência: no mesmo planeta, ou a até "
		   + $"{RadioDoScouter.AlcanceNoEspacoEmTiles} tiles no espaço. Chega só no ouvido esquerdo.", c);
	}

	private CenterContainer? _tecladoDoRadio;

	private void AbrirTecladoDoRadio()
	{
		FecharTecladoDoRadio();

		var t = new TecladoNumerico("Frequência do rádio", RadioDoScouter.FrequenciaMinima, RadioDoScouter.FrequenciaMaxima,
			v =>
			{
				GameClient.Instance?.SintonizarORadio((int)v);
				FecharTecladoDoRadio();
			},
			FecharTecladoDoRadio);

		_tecladoDoRadio = new CenterContainer { AnchorRight = 1, AnchorBottom = 1, Name = "TecladoDoRadio" };
		_tecladoDoRadio.AddChild(t);
		_raiz.AddChild(_tecladoDoRadio);

		// FECHAR O MENU FECHA O TECLADO: ele mora fora da pagina, entao ninguem o desmontaria por ele.
		VisibilityChanged += FecharTecladoDoRadio;
	}

	private void FecharTecladoDoRadio()
	{
		if (_tecladoDoRadio == null) return;
		VisibilityChanged -= FecharTecladoDoRadio;
		_tecladoDoRadio.QueueFree();
		_tecladoDoRadio = null;
	}

	/// <summary>A frequencia chegou do servidor: se a aba aberta e a que a desenha, redesenha. Mesma regra do `AoSentidos`.</summary>
	private void AoRadio() { if (Visible && _aba == "Equip") Redesenhar(); }

	/// <summary>O teclado do radio esta na tela? So a bancada le.</summary>
	public bool TecladoDoRadioDeTeste => _tecladoDoRadio != null && IsInstanceValid(_tecladoDoRadio);
}
