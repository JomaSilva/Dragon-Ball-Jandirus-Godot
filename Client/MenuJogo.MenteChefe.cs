using Godot;
using Jandirus.Core.Npc;

namespace Jandirus.Client;

/// <summary>
/// A LISTA DA ETAPA NA ABA LEARNING (2026-10-21). O botao "Enfrentar na mente" continua onde sempre
/// esteve. Se o servidor viu mais de uma forma, a resposta chega pelo mesmo canal das escolhas da
/// tecla E e os botoes nascem AQUI, nao na telinha do M.
/// </summary>
public partial class MenuJogo
{
	/// <summary>
	/// DEFEITO INJETADO (robo --diagetapa): a lista chega e a aba nao desenha os botoes. Falso em jogo, sempre.
	/// </summary>
	internal static bool EtapasSemCartaoDeTeste;

	/// <summary>O cartao das formas vistas, se o servidor acabou de perguntar. No fim da aba, junto dos verbs.</summary>
	private void DesenharEtapasDaMente()
	{
		if (GetNodeOrNull(OuvidoDasEtapasDaMente.Nome) == null)
			AddChild(new OuvidoDasEtapasDaMente { Name = OuvidoDasEtapasDaMente.Nome });
		if (EtapasSemCartaoDeTeste) return;
		if (GameClient.Instance?.Escolhas is not { } e || e.Verbo != EtapasDaMente.Verbo || e.Itens.Count == 0) return;

		Secao("Em qual forma?");
		foreach (string item in e.Itens)
		{
			string arg = item;
			var b = new Button
			{
				Text = EtapasDaMente.NomeDoItem(item),
				Alignment = HorizontalAlignment.Left,
			};
			b.SetMeta("etapa_da_mente", arg);
			b.Pressed += () => GameClient.Instance?.SendVerbo(EtapasDaMente.Verbo, arg);
			_conteudo.AddChild(b);
		}
	}
}

/// <summary>
/// Ouve a lista e remonta a Learning. Sem isto o pacote chega e a aba, que ja estava desenhada, fica muda.
/// </summary>
internal partial class OuvidoDasEtapasDaMente : Node
{
	internal const string Nome = "OuvidoDasEtapasDaMente";

	public override void _Ready()
	{
		if (GameClient.Instance is { } cli) cli.EscolhasChegaram += Ouvir;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.EscolhasChegaram -= Ouvir;
	}

	private void Ouvir(string verbo)
	{
		if (verbo != EtapasDaMente.Verbo) return;
		if (GetParent() is MenuJogo menu && menu.Visible && menu.AbaDeTeste == "Learning")
			menu.ForcarRedesenho();
	}
}
