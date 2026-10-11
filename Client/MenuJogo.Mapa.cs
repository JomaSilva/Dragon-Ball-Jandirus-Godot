using Godot;

namespace Jandirus.Client;

/// <summary>
/// O CARTAO DO MAPA, na aba World -- o pedido do dono (2026-10-10): *"adicione na aba world o mapa do mundo
/// atual q vc ta pra vc saber onde ta no mundo e suas coordenadas"*.
///
/// O mundo inteiro em miniatura a esquerda (`MapaDoLugarNaTela`), e ao lado o nome do mundo, o tamanho dele
/// e as COORDENADAS do corpo, em tiles. O quadro e as tres linhas vivas sao mantidos em dia pelo proprio
/// node do mapa, a cada quadro -- nada disto entra na assinatura da pagina, porque andar com o menu aberto
/// nao pode remontar a aba.
///
/// ELE NAO E UM MAPA DO TESOURO: so o chao e voce. Esferas, gente, NPC, obra e base dos outros nao
/// aparecem (quem acha esfera e o Dragon Radar; quem acha gente e o Sense/Scan).
/// </summary>
public partial class MenuJogo
{
	private void BlocoDoMapa(GameClient cli)
	{
		VBoxContainer corpo = Cartao("Mapa");

		var lado = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		lado.AddThemeConstantOverride("separation", 12);
		corpo.AddChild(lado);

		var mapa = new MapaDoLugarNaTela { Name = "MapaDoLugar" };
		lado.AddChild(mapa);

		var info = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		info.AddThemeConstantOverride("separation", 4);
		lado.AddChild(info);

		Linha("Mundo", cli.NomeDoLugar, null, info);
		var tamanho = (Label)Linha("Tamanho", "—", null, info).GetChild(1);
		var coordenadas = (Label)Linha("Coordenadas", "—", Tema.Destaque, info).GetChild(1);

		// A FRASE DE ESTADO (desenhando, sem mapa, alem da borda, o mundo da a volta): quem a escreve e o node.
		Label estado = Nota("", info);
		estado.Visible = false;

		Nota("O norte é em cima. X cresce para o leste e Y para o sul, em tiles a partir do canto de cima à esquerda.", info);
		Label dica = Nota("Clique no mapa para alternar entre o mundo inteiro e a vista de perto.", info);

		mapa.Ligar(tamanho, coordenadas, estado, dica);
	}
}
