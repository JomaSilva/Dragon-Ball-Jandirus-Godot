namespace Jandirus.Client;

/// <summary>
/// O ARRANJO DA INTERFACE -- onde o jogador pos e de que tamanho deixou cada peca da tela. Pedido do dono de
/// 2026-10-10: *"adicionar mais opcoes de customizacao da UI do jogo, podendo redimensionar ou mudar de lugar o
/// menu P como se fosse uma janela do windows, ou ate mesmo (...) abrir o menu P fora da janela do jogo (...).
/// poder redimensionar o chat pq uns players queriam aumentar e outros diminuir, o mesmo pros stats do canto
/// superior esquerdo e direito"*.
///
/// Mora dentro do <see cref="Settings"/> (o `config.json` da maquina), pelo motivo das teclas: o arranjo e da
/// PESSOA e do monitor dela, e nao do personagem nem do servidor.
///
/// ============================ O QUE FICA GRAVADO, E EM QUE UNIDADE ============================
///   | peca                     | o que se guarda                         | unidade                         |
///   |--------------------------|-----------------------------------------|---------------------------------|
///   | menu P embutido          | canto e tamanho                         | FRACAO da tela (0 a 1)          |
///   | menu P destacado         | monitor, canto e tamanho da janela      | pixels do monitor               |
///   | chat                     | largura e altura (o canto nao sai)      | FRACAO da tela                  |
///   | painel de status (esq.)  | escala                                  | fator (1 = o de fabrica)        |
///   | painel do corpo (dir.)   | escala                                  | fator                           |
///
/// FRACAO E NAO PIXEL no que mora dentro da tela do jogo: a "resolucao" deste jogo e a BASE DE DESENHO (ver o
/// bloco comprido do `Settings`), e ela muda quando o jogador troca a resolucao. Um chat de 700 px gravado numa
/// base de 1280 ocuparia um terco da tela numa base de 1920; a fracao ocupa a mesma parte dela nas duas.
///
/// CADA PECA TEM UM "MEXIDO". Falso = a peca esta no lugar DE FABRICA, montada pelas mesmas ancoras de sempre
/// (e nao por uma conta que "da no mesmo"): as dezenas de bancadas que medem posicao e foto continuam medindo o
/// que mediam. So quem arrastou alguma coisa sai desse caminho.
///
/// NADA AQUI E CONFIAVEL: o arquivo e do jogador e pode vir de outra maquina, de outro monitor, ou editado a
/// mao. Quem prende os numeros na tela e no minimo e o <see cref="Janelas"/>, toda vez que o arranjo e aplicado
/// (e nao so ao carregar); o <see cref="Saneado"/> daqui so tira o que nem numero e.
/// ================================================================================================
/// </summary>
public sealed class ArranjoDaInterface
{
	// ---- o menu do jogo (tecla P), dentro da janela do jogo ----
	/// <summary>O jogador ja arrastou ou redimensionou o menu? Falso = centrado, 760 x 580, como sempre foi.</summary>
	public bool MenuMexido;
	public float MenuX, MenuY, MenuLargura, MenuAltura;

	// ---- o menu do jogo numa janela do sistema ----
	/// <summary>O menu abre numa janela separada (pra por noutro monitor)? Falso = embutido, o padrao.</summary>
	public bool MenuDestacado;

	/// <summary>Ha um lugar gravado pra janela separada? Sem ele ela nasce em cima de onde o menu embutido estava.</summary>
	public bool JanelaLembrada;

	/// <summary>O monitor (indice do sistema) e o canto da area de desenho da janela, em pixels a partir do canto DELE.</summary>
	public int JanelaTela, JanelaX, JanelaY, JanelaLargura, JanelaAltura;

	// ---- o chat ----
	public bool ChatMexido;
	public float ChatLargura, ChatAltura;

	// ---- os dois paineis do topo ----
	/// <summary>A escala do painel de status (vida, ki, vigor, nutricao, BP), no canto superior esquerdo.</summary>
	public float EscalaDoStatus = 1f;

	/// <summary>A escala do painel do corpo (os bonecos e a mira), no canto superior direito.</summary>
	public float EscalaDoCorpo = 1f;

	/// <summary>
	/// AS BORDAS ESTAO TRAVADAS? Com isto ligado nenhuma alca responde ao mouse -- pra quem ja deixou a tela como
	/// quer e nao quer redimensionar o chat por acidente no meio de uma luta. O botao "Destacar" do menu continua.
	/// </summary>
	public bool Travado;

	/// <summary>Tudo no lugar de fabrica? (Metodo e nao propriedade: propriedade iria parar no `config.json`.)</summary>
	public bool EDeFabrica() =>
		!MenuMexido && !MenuDestacado && !JanelaLembrada && !ChatMexido && !Travado
		&& EscalaDoStatus == 1f && EscalaDoCorpo == 1f;

	/// <summary>
	/// O QUE VEIO DO ARQUIVO, SEM O QUE NAO E NUMERO: nulo vira o de fabrica, infinito e NaN viram o de fabrica do
	/// campo, e uma peca "mexida" sem tamanho volta a ser de fabrica. Nao prende nada na tela -- quem prende e o
	/// <see cref="Janelas"/>, que sabe o tamanho da tela de agora.
	/// </summary>
	public static ArranjoDaInterface Saneado(ArranjoDaInterface? a)
	{
		a ??= new ArranjoDaInterface();
		if (Janelas.SemTravaDeTeste) return a;

		a.MenuX = Finito(a.MenuX, 0f);
		a.MenuY = Finito(a.MenuY, 0f);
		a.MenuLargura = Finito(a.MenuLargura, 0f);
		a.MenuAltura = Finito(a.MenuAltura, 0f);
		if (a.MenuLargura <= 0f || a.MenuAltura <= 0f) a.MenuMexido = false;

		a.ChatLargura = Finito(a.ChatLargura, 0f);
		a.ChatAltura = Finito(a.ChatAltura, 0f);
		if (a.ChatLargura <= 0f || a.ChatAltura <= 0f) a.ChatMexido = false;

		a.EscalaDoStatus = Finito(a.EscalaDoStatus, 1f);
		a.EscalaDoCorpo = Finito(a.EscalaDoCorpo, 1f);

		if (a.JanelaLargura <= 0 || a.JanelaAltura <= 0) a.JanelaLembrada = false;
		return a;
	}

	private static float Finito(float v, float padrao) => float.IsFinite(v) ? v : padrao;

	/// <summary>
	/// O ARRANJO QUE UMA SESSAO NOVA ENCONTRARIA: le o `config.json` DO DISCO (e nao o que esta na memoria) pela
	/// mesma porta do boot. E o que as bancadas usam pra "fechar e abrir o jogo" sem fechar o jogo.
	/// </summary>
	public static ArranjoDaInterface DoDisco() => Saneado(Settings.Carregar().Arranjo);
}
