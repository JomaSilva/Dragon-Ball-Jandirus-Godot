using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// O SELO DA VERSAO: o "v0.1.0" pequeno, num canto de baixo da PRIMEIRA TELA e da PAUSA.
///
/// O dono (2026-10-11): *"o jogo poderia ter no menu mostrando qual versao ele ta"*.
///
/// ============================ UMA PECA SO, PRAS DUAS TELAS ============================
/// A primeira tela (`Boot.MontarLogin`) e a pausa/opcoes (`PauseMenu`) penduram o MESMO selo, feito aqui. O
/// texto sai de <see cref="FioDaVersao.DoProcesso"/> -- a mesma identidade que viaja no pedido de conexao --,
/// entao o que o jogador le no canto e, por construcao, o que o servidor vai julgar.
///
/// ============================ ANCORADO NA QUINA, E NAO NUMA POSICAO ============================
/// A interface e redimensionavel (resolucao, tela cheia, `Janelas`), e o topo da primeira tela e do titulo do
/// jogo. O selo nao tem coordenada: ele e ancorado na quina de baixo, a direita, do que o segura, cresce pra
/// dentro (pra cima e pra esquerda) e deixa so a margem. Mudou o tamanho da tela, ele continua la.
/// Nao pega clique (`MouseFilter.Ignore`): e letreiro, e nao botao.
/// ================================================================================================
/// </summary>
public static class SeloDeVersao
{
	/// <summary>O nome do node. As bancadas (`--diagversao`) o procuram por ele.</summary>
	public const string Nome = "SeloDeVersao";

	/// <summary>A folga entre o selo e as duas bordas da quina, em pixels de interface.</summary>
	public const int MargemDaQuina = 8;

	/// <summary>O tamanho da letra: menor que o texto comum da interface (14), pra ser lido so por quem procura.</summary>
	public const int TamanhoDaLetra = 12;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagversao`): o selo nasce numa posicao CRAVADA (a quina de uma tela de
	/// 1280 x 720) em vez de ancorado -- noutra resolucao ele fica fora do canto, ou fora da tela. Lido pela
	/// propria montagem, em <see cref="Novo"/>.
	/// </summary>
	internal static bool PosicaoCravadaDeTeste;

	/// <summary>O que o selo diz: "v0.1.0" (e avisa quando a versao deste processo foi forjada por bancada).</summary>
	public static string Texto => "v" + FioDaVersao.DoProcesso.Texto + (FioDaVersao.Forjada ? " (forjada)" : "");

	/// <summary>Um selo novo, pronto pra ser pendurado numa camada ou num controle que cubra a tela.</summary>
	public static Label Novo()
	{
		// A LEGENDA DA CASA (com contorno): na pausa o selo fica por cima do mundo escurecido.
		Label selo = Tema.Legenda(Texto, Tema.TextoFraco, TamanhoDaLetra);
		selo.Name = Nome;
		selo.MouseFilter = Control.MouseFilterEnum.Ignore;
		selo.HorizontalAlignment = HorizontalAlignment.Right;
		selo.VerticalAlignment = VerticalAlignment.Bottom;

		if (PosicaoCravadaDeTeste)
		{
			selo.Position = new Vector2(1280 - 60, 720 - 26);
			return selo;
		}

		// AS QUATRO ANCORAS NA QUINA (1, 1) e um retangulo de tamanho zero recuado pela margem: o tamanho de
		// verdade vem do texto, e o `Grow...Begin` o faz crescer PRA DENTRO da tela.
		selo.AnchorLeft = selo.AnchorRight = selo.AnchorTop = selo.AnchorBottom = 1;
		selo.OffsetLeft = selo.OffsetRight = selo.OffsetTop = selo.OffsetBottom = -MargemDaQuina;
		selo.GrowHorizontal = Control.GrowDirection.Begin;
		selo.GrowVertical = Control.GrowDirection.Begin;
		return selo;
	}

	/// <summary>
	/// A versao no TITULO DA JANELA ("Dragon Ball Jandirus -- v0.1.0"): quem esta com dois builds abertos sabe
	/// qual e qual sem entrar em nenhum. Sem janela (o `--headless`) nao ha titulo, e nada acontece.
	/// </summary>
	public static void NoTituloDaJanela(Window? janela)
	{
		if (janela == null || DisplayServer.GetName() == "headless") return;
		string nome = ProjectSettings.GetSetting("application/config/name", "Dragon ball Jandirus").AsString();
		janela.Title = $"{nome} — {Texto}";
	}
}
