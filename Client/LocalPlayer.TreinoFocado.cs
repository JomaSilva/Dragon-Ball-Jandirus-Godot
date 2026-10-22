using Godot;
using Jandirus.Core.Stats;
using Jandirus.Net;

namespace Jandirus.Client;

public partial class LocalPlayer
{
	/// <summary>
	/// A tecla T. Quem ja treina, para -- sair nao e escolha, como no M. Quem nao treina ve a telinha.
	/// Sem a telinha montada (bancada sem interface), o T treina direto, como o M sem a tela da meditacao.
	/// Devolve falso quando a telinha comeu a tecla e a atividade nao deve mudar neste quadro.
	/// </summary>
	private bool LerOTreino(bool andando, ref Protocol.Activity nova)
	{
		if (_atividade == Protocol.Activity.Treinando)
		{
			nova = Protocol.Activity.Parado;
			GameClient.Instance?.SoltarFocado();
			return true;
		}
		if (!andando && TelaDeTreino.Instancia is { } pergunta)
		{
			if (pergunta.NaTela) pergunta.Fechar();
			else pergunta.Abrir(EscolheuTreinar);
			return false;
		}
		nova = Protocol.Activity.Treinando;
		return true;
	}

	private void EscolheuTreinar(EscolhaDoTreino escolha)
	{
		_atividade = Protocol.Activity.Treinando;
		GameClient.Instance?.SendActivity(Protocol.Activity.Treinando);
		if (escolha == EscolhaDoTreino.Focado) GameClient.Instance?.PedirFocado(meditar: false);
	}

	private void EscolheuMeditarFocada()
	{
		_atividade = Protocol.Activity.Meditando;
		GameClient.Instance?.SendActivity(Protocol.Activity.Meditando);
		GameClient.Instance?.PedirFocado(meditar: true);
	}

	/// <summary>O WASD e a letra do anel, nao um passo. A seta continua andando e derruba o treino.</summary>
	private bool WasdDoTreinoFocado()
	{
		GameClient? cli = GameClient.Instance;
		return cli != null && TreinoFocado.WasdNaoAnda(
			cli.FocoModo == TreinoFocado.ModoTreino && !cli.FocoPausado && cli.Atividade == Protocol.Activity.Treinando);
	}

	/// <summary>O espaco da barra nao soca. O soco le a tecla direto, fora do <c>SetInputAsHandled</c>.</summary>
	private bool EspacoDaRespiracaoFocada()
	{
		GameClient? cli = GameClient.Instance;
		return cli != null && TreinoFocado.EspacoEhDaRespiracao(
			cli.FocoModo == TreinoFocado.ModoMeditacao && !cli.FocoPausado && cli.Atividade == Protocol.Activity.Meditando);
	}
}
