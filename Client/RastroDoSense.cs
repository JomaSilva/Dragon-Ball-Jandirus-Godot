using Godot;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// ============================ O RASTRO E A BUSCA DO SENSE (pedido de jogador repassado pelo dono, 2026-10-19) ============================
/// *"o sistema de Sense ou ao menos colocar a opcao de pesquisar o nome da pessoa ou dar track clicando no nome da pessoa"*. Duas
/// pecas, e as duas so LEEM a lista que o servidor ja manda (`S2C.Sentidos`, 1 Hz, passada pelo sigilo -- `GameServer.Sentidos.cs`):
///   * a BUSCA da aba Sense/Scan filtra os cartoes pelo nome (ou pela assinatura de quem voce nao conhece), sem acento e sem caixa:
///     "joao" acha o "Joao" com til;
///   * o RASTRO: clicar no nome de uma presenca passa a segui-la. O HUD ganha o selo (nome, distancia, rumo -- ou o lugar, se ela
///     estiver em outra zona) e uma seta em volta do seu corpo apontando pra ela (`RastroNaTela`). E o `Sense HUD` do DU
///     (`2021+ New/HUD Stuff/Sense HUD.dm:7-37`, a seta do `PointArrow` de `Misc/Utilities.dm:50-88`), onde clicar no nome da aba
///     Sense marca o alvo e a seta some com ele ja na tela. No Finale nao ha rastro nem busca (as linhas do `ui_tab_sense` nao tem
///     link, `HtmlUI.dm:85-86`); o mais perto e o `StatSense` antigo, que prendia o alvo no topo do painel (`Sense3.0.dm:332-339`).
///
/// O RASTRO NAO SABE MAIS QUE O SENSE. Ele vale enquanto a presenca estiver na lista: saiu do alcance, relaxou o poder, entrou no
/// ki divino, morreu -- o selo diz que perdeu o rastro e a seta some; voltou pra lista, o rastro volta sozinho. Nada novo viaja.
///
/// A CHAVE E A IDENTIDADE QUE A PROPRIA LISTA MOSTRA: o nome (de quem voce conhece, ou de NPC) ou a assinatura (de quem nao). A
/// linha nao tem id de corpo -- de proposito: ele ligaria o "???" ao nome que o snapshot ja carrega --, e a POSICAO dela muda a
/// cada pacote, porque a lista vem ordenada por alcance e distancia: seguir "a terceira linha" seria seguir quem estiver mais perto.
/// ==========================================================================================================================
/// </summary>
public static class RastroDoSense
{
	/// <summary>O nome seguido ("" = segue-se pela assinatura, ou ninguem).</summary>
	public static string Nome { get; private set; } = "";

	/// <summary>A assinatura seguida -- so de quem voce nao conhece ("" = segue-se pelo nome, ou ninguem).</summary>
	public static string Assinatura { get; private set; } = "";

	public static bool Ativo => Nome.Length > 0 || Assinatura.Length > 0;

	/// <summary>Como a pessoa seguida aparece na tela: o nome, ou o "??? (assinatura)" do cartao.</summary>
	public static string Rotulo => Nome.Length > 0 ? Nome : Assinatura.Length > 0 ? $"??? ({Assinatura})" : "";

	/// <summary>O rastro comecou, trocou de alvo ou parou. O selo do HUD e a aba remontam por aqui.</summary>
	public static event Action? Mudou;

	/// <summary>A linha da lista em que o alvo estava quando foi escolhido -- so o defeito injetado abaixo a le.</summary>
	private static int _linhaDoClique = -1;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagsenserastro`): o rastro segue a LINHA da lista em que o nome foi clicado, e nao a pessoa. A lista
	/// vem ordenada pela distancia, entao basta outro corpo chegar mais perto pra seta passar a apontar pra ele. Falso em jogo.
	/// </summary>
	internal static bool RastroPelaLinhaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (robo `--diagsenserastro`): a busca ignora o que foi digitado e todo cartao continua na aba. Falso em jogo.
	/// </summary>
	internal static bool BuscaCegaDeTeste;

	/// <summary>Esta presenca e a seguida? (O cartao dela ganha a borda acesa e o clique no nome vira "parar".)</summary>
	public static bool EhOAlvo(Protocol.PresencaState p) =>
		Ativo && (Nome.Length > 0 ? p.Nome == Nome : p.Nome.Length == 0 && p.Assinatura == Assinatura);

	/// <summary>Passa a seguir esta presenca. <paramref name="linha"/> e onde ela estava na lista (so o defeito injetado le).</summary>
	public static void Seguir(Protocol.PresencaState p, int linha)
	{
		// O NOME VENCE A ASSINATURA: a lista manda um OU o outro (`IdentidadePara`), e o NPC vai so com o nome.
		Nome = p.Nome;
		Assinatura = p.Nome.Length > 0 ? "" : p.Assinatura;
		_linhaDoClique = linha;
		Mudou?.Invoke();
	}

	public static void Parar()
	{
		if (!Ativo) return;
		Nome = Assinatura = "";
		_linhaDoClique = -1;
		Mudou?.Invoke();
	}

	/// <summary>
	/// A PRESENCA SEGUIDA NA LISTA DE AGORA, ou nula -- perdeu o rastro. Entre dois NPCs com o mesmo nome fica o primeiro, que e o
	/// mais perto (a lista vem por alcance e distancia): NPC nao tem assinatura que os separe, e o da mesma praca e o que se procura.
	/// </summary>
	public static Protocol.PresencaState? Leitura(IReadOnlyList<Protocol.PresencaState> lista)
	{
		if (!Ativo) return null;
		if (RastroPelaLinhaDeTeste)
			return _linhaDoClique >= 0 && _linhaDoClique < lista.Count ? lista[_linhaDoClique] : null;
		foreach (Protocol.PresencaState p in lista)
			if (EhOAlvo(p)) return p;
		return null;
	}

	// =====================================================================
	// A BUSCA
	// =====================================================================

	/// <summary>O texto como a busca o compara: sem acento, sem caixa e sem espaco nas pontas.</summary>
	public static string Normalizar(string s)
	{
		var sb = new System.Text.StringBuilder(s.Length);
		foreach (char c in s.Trim().Normalize(System.Text.NormalizationForm.FormD))
			if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
				sb.Append(char.ToLowerInvariant(c));
		return sb.ToString();
	}

	/// <summary>
	/// O CARTAO FICA NA ABA? Termo vazio deixa todos. Casa no NOME e na ASSINATURA -- quem voce nao conhece so tem a assinatura, e
	/// ela e o que o cartao mostra ("??? (assinatura)"): dava pra ler e nao dava pra procurar.
	/// </summary>
	public static bool CasaComABusca(Protocol.PresencaState p, string termo)
	{
		string t = Normalizar(termo);
		if (t.Length == 0 || BuscaCegaDeTeste) return true;
		return Normalizar(p.Nome).Contains(t, StringComparison.Ordinal)
			|| Normalizar(p.Assinatura).Contains(t, StringComparison.Ordinal);
	}

	// =====================================================================
	// O RUMO
	// =====================================================================

	/// <summary>
	/// A DIRECAO DOS OITO PONTOS da lista na tela (x pra direita, y pra baixo -- o norte e y negativo, como o `RumoDoDm` do
	/// servidor), ou nula sem rumo (0 = "em cima de voce", ou a presenca de outra zona).
	/// </summary>
	public static Vector2? DirecaoDoRumo(byte rumo) => rumo switch
	{
		1 => new Vector2(0, -1),
		2 => new Vector2(1, -1).Normalized(),
		3 => new Vector2(1, 0),
		4 => new Vector2(1, 1).Normalized(),
		5 => new Vector2(0, 1),
		6 => new Vector2(-1, 1).Normalized(),
		7 => new Vector2(-1, 0),
		8 => new Vector2(-1, -1).Normalized(),
		_ => null,
	};
}
