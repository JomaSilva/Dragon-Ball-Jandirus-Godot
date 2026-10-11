using Jandirus.Core.Magic;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Client;

/// <summary>
/// O `S2C.MenuDoDragao` do lado do cliente: o ultimo menu que o servidor mandou, e os tres avisos que a janela
/// de desejos (`MenuDeDesejos`) escuta. Ver `Core/Magic/MenuDoDragao.cs` e `Server/GameServer.MenuDoDragao.cs`.
/// </summary>
public partial class GameClient
{
	/// <summary>
	/// O MENU DO DRAGAO como o servidor o mandou por ultimo -- nulo antes do primeiro. E do SERVIDOR: o cliente
	/// nao acrescenta linha, nao reordena e nao decide o que pode ser pedido.
	/// </summary>
	public MenuDoDragao? MenuDoDragao { get; private set; }

	/// <summary>Chegou um menu (novo, ou o mesmo com a contagem mudada).</summary>
	public event Action? MenuDoDragaoMudou;

	/// <summary>
	/// O SERVIDOR PEDIU A JANELA AGORA: o dragao acabou de subir, o jogador apertou "Ver os desejos", ou ha uma
	/// pergunta esperando a palavra dele. Separado do <see cref="MenuDoDragaoMudou"/> pelo motivo do
	/// `RefugioPediuAbrir`: atualizar o que esta na tela e abrir na cara de alguem nao sao a mesma coisa.
	/// </summary>
	public event Action? MenuDoDragaoPediuAbrir;

	/// <summary>O dragao que a janela mostrava foi embora.</summary>
	public event Action<QualDragao>? DragaoFoiEmbora;

	private void LerOMenuDoDragao(NetDataReader reader)
	{
		MenuDoDragao m = FioDoMenuDoDragao.Ler(reader);
		if (m.Fechar)
		{
			DragaoFoiEmbora?.Invoke(m.Dragao);
			return;
		}

		MenuDoDragao = m;
		MenuDoDragaoMudou?.Invoke();
		if (m.Abrir) MenuDoDragaoPediuAbrir?.Invoke();
	}
}
