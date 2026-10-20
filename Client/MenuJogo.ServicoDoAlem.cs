using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) O CARTAO "SERVIÇOS DO OUTRO MUNDO" DA ABA WORLD -- so pro morto (o servidor manda `Mostrar` falso pro vivo): o servico em
/// curso (o pagamento, os minutos que faltam, o andamento e o botao de desistir) e a rede inteira, com o estado de cada servico. O cliente
/// so desenha o que veio (`GameClient.CartaoDoAlem`); a regra mora em `Core/World/ServicosDoAlem.cs`.
/// </summary>
public partial class MenuJogo
{
	/// <summary>O titulo do cartao -- o robo `--diagservicos` o acha por aqui.</summary>
	internal const string TituloDoCartaoDoAlem = "Serviços do Outro Mundo";

	/// <summary>O rotulo do botao de desistir.</summary>
	internal const string RotuloDesistirDoServico = "Desistir do serviço";

	private void CartaoDoServicoDoAlem()
	{
		// O OUVIDO NASCE NA PRIMEIRA VISITA A ABA: ver `OuvidoDoServicoDoAlem`.
		if (GetNodeOrNull(OuvidoDoServicoDoAlem.Nome) == null) AddChild(new OuvidoDoServicoDoAlem { Name = OuvidoDoServicoDoAlem.Nome });

		if (GameClient.Instance is not { } cli || !cli.CartaoDoAlem.Mostrar) return;
		ServicoDoAlemState s = cli.CartaoDoAlem;
		VBoxContainer c = Cartao(TituloDoCartaoDoAlem, destaque: s.Titulo.Length > 0);
		if (s.Titulo.Length > 0)
		{
			c.AddChild(Cabecalho(s.Titulo, Tema.Texto, ($"{s.Recompensa:N0} zeni", Tema.Bom),
								 ($"{s.Minutos} min", s.Minutos <= 3 ? Tema.Perigo : Tema.Ki)));
			Linha("andamento", s.Andamento, null, c);
			var desistir = new Button { Text = RotuloDesistirDoServico, FocusMode = Control.FocusModeEnum.None };
			desistir.Pressed += () => cli.SendVerbo(Jandirus.Core.World.ServicosDoAlem.VerboDesistir);
			c.AddChild(BotaoComDescricao(desistir, "larga o serviço sem pagar -- dá pra pedir de novo na hora"));
		}
		else Nota("Nenhum serviço agora. O Oni da recepção, ao lado da mesa do Enma, sempre tem um (menu E: Pedir serviço).", c);
		foreach (LinhaDeServico l in s.Rede) Linha(l.Nome, l.Estado, l.Estado == "em curso" ? Tema.Destaque : null, c);
	}

	/// <summary>O pedaco do cartao na assinatura da aba World (`ExtraDaAssinaturaDeMundo`): o que ele desenha.</summary>
	private static string AssinaturaDoServicoDoAlem(GameClient c) =>
		$"{c.CartaoDoAlem.Mostrar}/{c.CartaoDoAlem.Titulo}/{c.CartaoDoAlem.Andamento}/{c.CartaoDoAlem.Minutos}/"
		+ string.Join(",", c.CartaoDoAlem.Rede.Select(l => l.Estado));

	/// <summary>O cartao novo chegou: a aba World remonta se e ela que esta aberta (a assinatura decide se mudou).</summary>
	internal void AoCartaoDoAlem()
	{
		if (Visible && _aba == "World") Redesenhar();
	}
}

/// <summary>
/// O OUVIDO DO CARTAO DOS SERVICOS: um no filho do menu que assina o `CartaoDoAlemMudou` e remonta a aba World quando o pacote chega.
///
/// POR QUE UM NO E NAO O `_Ready` DO MENU: o menu se redesenha a cada pacote de FICHA, e a ficha so sai quando muda -- um morto parado
/// com a aba World aberta nao receberia nenhuma, e o cartao novo ficaria na memoria sem virar tela (o mesmo defeito que a aba Sense
/// teve, `MenuJogo.Sentidos.cs`). O `_Notification` do menu ja tem dono, e a assinatura tem de sair quando o menu sai -- o
/// `GameClient` sobrevive ao logout, e um assinante morto acordaria a cada pacote. Um no proprio resolve as duas coisas.
/// </summary>
internal partial class OuvidoDoServicoDoAlem : Node
{
	internal const string Nome = "OuvidoDoServicoDoAlem";

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagservicos`): o ouvido nao remonta a aba -- o cartao novo so viraria tela no proximo pacote de ficha, e
	/// o morto parado nao recebe nenhum. Falso em jogo.
	/// </summary>
	internal static bool SurdoDeTeste;

	public override void _Ready()
	{
		if (GameClient.Instance is { } cli) cli.CartaoDoAlemMudou += Ouvir;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.CartaoDoAlemMudou -= Ouvir;
	}

	private void Ouvir()
	{
		if (!SurdoDeTeste) (GetParent() as MenuJogo)?.AoCartaoDoAlem();
	}
}
