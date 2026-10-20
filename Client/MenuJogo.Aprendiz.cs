using Godot;
using Jandirus.Core.Ranks;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-19) O CARTAO DAS ESCOLHAS DO APRENDIZ DE KAIOSHIN, na aba Other: o `input()` do DM (`KaioshinApprentice.dm:330`, `:361`) --
/// "Tomar quem como Aprendiz de Kaioshin?" e "Dispensar qual aprendiz?". O servidor manda a lista pelo `S2C.Escolhas` (como as sagas
/// do admin, `MenuJogo.Admin.cs`) e cada botao devolve o verbo com o escolhido. Quem decide e o servidor
/// (`GameServer.AprendizDeKaioshin.cs`); a lista vazia some com o cartao.
/// </summary>
public partial class MenuJogo
{
	/// <summary>O titulo do cartao -- o robo `--diagaprendiz` o acha por aqui.</summary>
	internal const string TituloDasEscolhasDoAprendiz = "Aprendiz de Kaioshin: escolha";

	private void CartaoDasEscolhasDoAprendiz()
	{
		// O OUVIDO NASCE NA PRIMEIRA VISITA A ABA: ver `OuvidoDasEscolhasDoAprendiz`.
		if (GetNodeOrNull(OuvidoDasEscolhasDoAprendiz.Nome) == null) AddChild(new OuvidoDasEscolhasDoAprendiz { Name = OuvidoDasEscolhasDoAprendiz.Nome });

		if (GameClient.Instance is not { } cli || cli.Escolhas is not { } lista || lista.Itens.Count == 0) return;
		bool nomear = lista.Verbo == AprendizDeKaioshin.VerboNomear;
		if (!nomear && lista.Verbo != AprendizDeKaioshin.VerboDispensar) return;

		VBoxContainer corpo = Cartao(TituloDasEscolhasDoAprendiz);
		Nota(nomear ? "Tomar quem como Aprendiz de Kaioshin? (ele ainda aceita ou recusa)"
					: "Dispensar qual aprendiz? (ele perde o kit e recebe os Marcos de volta)", corpo);
		foreach (string item in lista.Itens)
		{
			string[] partes = item.Split('|', 2);
			string quem = partes[0];
			var b = new Button
			{
				Text = partes.Length > 1 ? partes[1] : item,
				FocusMode = Control.FocusModeEnum.None,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			};
			b.SetMeta("aprendiz_escolha", quem);
			string verbo = lista.Verbo;
			b.Pressed += () => cli.SendVerbo(verbo, quem);
			corpo.AddChild(b);
		}
	}

	/// <summary>A lista chegou: a aba Other remonta se e ela que esta aberta (a assinatura decide se mudou).</summary>
	internal void AoEscolhasDoAprendiz()
	{
		if (Visible && _aba == Verbos.Outros) Redesenhar();
	}

	/// <summary>O pedaco do cartao na assinatura da aba Other: a lista nova remonta a aba (o `ChegouEm`, como o das sagas).</summary>
	private static string AssinaturaDasEscolhasDoAprendiz() =>
		GameClient.Instance?.Escolhas is { Verbo: AprendizDeKaioshin.VerboNomear or AprendizDeKaioshin.VerboDispensar } e
			? $"|aprendiz:{e.ChegouEm}:{e.Itens.Count}" : "";
}

/// <summary>
/// O OUVIDO DAS ESCOLHAS DO APRENDIZ: um no filho do menu que assina o `EscolhasChegaram` e remonta a aba Other quando a lista chega.
/// Sem ele o cartao so viraria tela no proximo pacote de ficha -- e o Kaioshin parado com a aba aberta nao recebe nenhum (o mesmo defeito
/// do cartao dos servicos do Outro Mundo, `OuvidoDoServicoDoAlem`). Um no proprio, pela mesma razao: a assinatura sai quando o menu sai.
/// </summary>
internal partial class OuvidoDasEscolhasDoAprendiz : Node
{
	internal const string Nome = "OuvidoDasEscolhasDoAprendiz";

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
		if (verbo is AprendizDeKaioshin.VerboNomear or AprendizDeKaioshin.VerboDispensar) (GetParent() as MenuJogo)?.AoEscolhasDoAprendiz();
	}
}
