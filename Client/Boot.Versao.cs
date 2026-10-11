using Godot;
using Jandirus.Core;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A VERSAO NA PRIMEIRA TELA, e o aviso de quando ela nao bate com a do servidor.
///
/// O dono (2026-10-11): *"o jogo poderia ter no menu mostrando qual versao ele ta, e ... se n bater o jogo manda
/// pro menu avisando q o cliente ta em alguma versao na frente ou atras do server"*.
///
/// O selo e a peca compartilhada <see cref="SeloDeVersao"/> (a pausa pendura o mesmo). O AVISO nao e uma tela
/// nova: e o painel da queda de conexao (`Boot.Queda.cs`, <see cref="PainelDeAviso"/>), com outro titulo e a
/// frase de versao -- quem foi barrado pela versao "caiu" antes de entrar, e volta ao mesmo lugar.
/// </summary>
public partial class Boot
{
	/// <summary>
	/// Pendura o selo da versao na camada da primeira tela (some com ela ao entrar no mundo, volta com ela),
	/// escreve a versao no titulo da janela e, com `--diagversao`, poe a bancada de pe.
	///
	/// O ROBO NASCE AQUI, e nao numa linha a mais do `_Ready`: ele mede esta tela, e esta e a primeira linha
	/// em que ela existe. A guarda `Vivo` cobre as voltas ao menu, que passam por aqui de novo.
	/// </summary>
	private void MostrarAVersao(CanvasLayer camada)
	{
		camada.AddChild(SeloDeVersao.Novo());
		SeloDeVersao.NoTituloDaJanela(GetWindow());

		// A VERSAO NO LOG DO JOGADOR, uma vez por processo: e a primeira coisa que se pede a quem manda o .log
		// dizendo "nao consigo entrar no servidor".
		if (!_versaoNoLog)
		{
			_versaoNoLog = true;
			IdentidadeDeBuild id = FioDaVersao.DoProcesso;
			GD.Print($"[versao] este jogo esta na {id.Texto} | protocolo {id.DigitalCurta}" + (FioDaVersao.Forjada ? "   (FORJADA pela linha de comando -- bancada)" : ""));
		}

		string[] args = OS.GetCmdlineArgs();
		if ((Array.IndexOf(args, "--diagversao") >= 0 || Array.IndexOf(args, "--versaodupla") >= 0) && !RoboDeVersao.Vivo)
			AddChild(new RoboDeVersao { Name = "RoboDeVersao" });
	}

	/// <summary>
	/// A VERSAO NAO BATEU: o mesmo painel da queda (o veu que segura o clique, o OK), com o titulo e a frase de
	/// versao -- as duas versoes e a DIRECAO. O resumo fica na linha de estado do formulario, pra continuar la
	/// depois do OK.
	/// </summary>
	private void AvisarDaVersao(Versao.Aviso aviso)
	{
		if (IsInstanceValid(_status))
		{
			// O RESUMO E MAIS COMPRIDO QUE A COLUNA DO FORMULARIO. Quebrado em linhas ele cresce pra BAIXO; inteiro
			// numa linha ele alargava o painel e empurrava a coluna dos perfis (a primeira foto da bancada mostrou).
			_status.AutowrapMode = ResumoSemQuebraDeTeste ? TextServer.AutowrapMode.Off : TextServer.AutowrapMode.WordSmart;
			_status.Text = aviso.Resumo + ".";
		}
		PainelDeAviso(aviso.Titulo, aviso.Frase);
	}

	/// <summary>A versao deste processo ja foi escrita no log? (Uma vez so: o menu e remontado a cada volta.)</summary>
	private static bool _versaoNoLog;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--diagversao`): o resumo do aviso de versao vai pra linha de estado SEM quebra
	/// de linha -- e o formulario do menu principal alarga pra caber. Lido pela propria regra, em
	/// <see cref="AvisarDaVersao"/>.
	/// </summary>
	internal static bool ResumoSemQuebraDeTeste;

	// =====================================================================
	// O QUE A BANCADA `--diagversao` PERGUNTA
	// =====================================================================
	/// <summary>O selo da versao da primeira tela, ou nulo (sem menu principal na tela).</summary>
	internal Label? SeloDaPrimeiraTela() =>
		IsInstanceValid(_painel) && _painel.GetParent()?.GetNodeOrNull<Label>(SeloDeVersao.Nome) is { } selo
		&& !selo.IsQueuedForDeletion() ? selo : null;

	/// <summary>A camada da primeira tela (o `LoginUI`), ou nulo sem menu principal de pe. E nela que moram o formulario e o selo.</summary>
	internal Node? CamadaDaPrimeiraTela() =>
		IsInstanceValid(_painel) && _painel.GetParent() is { } camada && !camada.IsQueuedForDeletion() ? camada : null;

	/// <summary>O retangulo, na tela, do bloco do formulario de login (as duas colunas), ou nulo.</summary>
	internal Rect2? RetanguloDoFormulario() =>
		IsInstanceValid(_painel) && _painel.GetChildCount() > 0 && _painel.GetChild(0) is Control colunas ? colunas.GetGlobalRect() : null;

	/// <summary>
	/// A largura da coluna em que a linha de estado mora (a do formulario), ou nulo. E ela que alargava quando a
	/// frase de estado nao quebrava -- a outra coluna (os perfis) muda de largura por conta propria.
	/// </summary>
	internal float? LarguraDaColunaDoEstado() =>
		IsInstanceValid(_status) && _status.GetParent() is Control coluna ? coluna.Size.X : null;

	/// <summary>A tela de selecao de personagem que esta de pe (a conta entrou), ou nulo.</summary>
	internal CharacterSelect? SelecaoNaTela =>
		_selecao is { } s && IsInstanceValid(s) && !s.IsQueuedForDeletion() && s.Visible ? s : null;

	/// <summary>
	/// PALCO DA `--versaodupla`: liga o modo automatico (o do `--connect`) SEM discar -- quem disca e o robo,
	/// pelo botao "Entrar", quando o outro processo ja subiu. Com ele ligado, a lista de slots que volta entra
	/// sozinha no mundo, como nas bancadas de `--host`.
	/// </summary>
	internal void LigarOModoAutomaticoDeTeste()
	{
		string[] args = OS.GetCmdlineArgs();
		_autoNome = Arg(args, "--nome") ?? "Guerreiro";
		_autoRaca = Arg(args, "--raca") ?? "Human";
		_auto = true;
	}
}
