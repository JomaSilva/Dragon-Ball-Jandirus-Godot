using Godot;

namespace Jandirus.Client;

/// <summary>
/// (2026-10-23) O CARTAO DO SUPER MAJIN NA ABA FORMS -- o estagio de agora e a regra da trava pelo poder (o pedido do dono: a maestria
/// *"trava com base no bp atual do majin"*, `Core/Forms/SuperMajin.cs`). Sem isto o jogador veria a barra parar em 30% e nao saberia por
/// que. A nota diz a REGRA e nao o BP dele (o BP so vira numero com scouter); o estagio sai da maestria que a ficha ja traz, pela conta do
/// Core (`Catalogo.MultBruto`), entao nao ha campo novo no fio nem na assinatura da aba -- a maestria ja esta nela.
/// </summary>
public partial class MenuJogo
{
	/// <summary>As duas linhas do Super Majin no cartao da forma; qualquer outra forma passa batido.</summary>
	private void TravaDoSuperMajin(Jandirus.Core.Forms.FormaDef d, VBoxContainer corpo)
	{
		if (d.Id != Jandirus.Core.Forms.SuperMajin.Id) return;
		Linha("estágio", MultTexto(Jandirus.Core.Forms.Catalogo.MultBruto(d, Maestria(d.Id), diluido: false)), Tema.Destaque, corpo);
		Nota(Jandirus.Core.Forms.SuperMajin.NotaDaTrava, corpo);
	}
}
