using Godot;
using Jandirus.Core.Skills;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A ABA KI -- a energia (tanque, razao, teto de carga) e as DEZENOVE PERICIAS de Ki: o dominio (a arvore
/// da Mente e o voo) e as tecnicas (as familias de tiro, o debuff, a defesa, a armadura).
///
/// E o `ui_tab_ki` do original (`HtmlUI.dm:507-541`): "KI ABILITY" e "KI TECHNIQUES", um contador por
/// linha. O DM listava tambem "MELEE" e "WEAPONS" (`tactics`, `swordskill`...) -- o port NAO tem esses
/// campos, e nao se desenha zero pra fingir que tem. Os contadores chegam pela ficha lenta
/// (`Protocol.AtributosState.Pericias`), na ordem de `Protocol.PericiasDeKi`, e cada rotulo passa pelo
/// `NomesLegiveis.Campo` -- nome cru do DM (`kiawarenessskill`) nao chega a tela.
///
/// AS LINHAS "Percentual" E "Teto de carga" SAO CONTRATO: a `--diagbancada` le as duas
/// (`RoboDeBancada.cs:212-213`) e compara com o HUD. Os textos nao mudam de forma.
/// </summary>
public partial class MenuJogo
{
	private void Ki(SheetState f)
	{
		// ACIMA DE 100% A COR E A DO EXCEDENTE, como na barra do HUD: o Ki carregado alem do tanque e
		// outra coisa (ele entra linear no poder) e merece outra cor.
		Color corDoKi = f.RazaoDeKi > 1 ? Tema.KiExcesso : Tema.Ki;
		Faixa("Ki", $"{f.RazaoDeKi * 100:0.#}%",
			  $"{f.Ki:N0} de {f.MaxKi:N0} · a carga cabe até {f.TrilhoDeKi * 100:0}% do tanque", corDoKi);

		VBoxContainer energia = Cartao("Energia");
		// O TRILHO DA BARRA E O TETO DE CARGA (`teto`), como o do HUD: a barra tem pra onde crescer alem
		// do tanque. Ver `SheetState.TetoKi` e `SheetState.TrilhoDeKi`.
		LinhaComBarra("Ki atual", $"{f.Ki:N0} / {f.MaxKi:N0}", f.RazaoDeKi, corDoKi, energia, teto: f.TrilhoDeKi);
		Linha("Ki máximo", $"{f.MaxKi:N0}", null, energia);

		// MESMA RAZAO DO HUD E DA ABA STATS -- ver `SheetState.RazaoDeKi`.
		Linha("Percentual", $"{f.RazaoDeKi * 100:0.#}%", f.RazaoDeKi > 1 ? Tema.Destaque : Tema.Texto, energia);

		// O TETO DE CARGA E O FIM DO TRILHO DA BARRA, e mostra-lo nao e invencao do port: o original
		// imprimia este mesmo numero com esta mesma cara (`Statistics.dm:349`, `Ki Capacity`). Ele so
		// sobe com as skills de power-up, e sem ele o jogador nao tem como saber quanto ainda cabe.
		Linha("Teto de carga", $"{f.TrilhoDeKi * 100:0}% do tanque", null, energia);
		Nota("Segure C pra reunir energia. Acima de 100% o Ki entra linear no poder (118% = 1,18x de BP) até o teto de carga.", energia);

		// AS DEZENOVE, em dois cartoes lado a lado: a ORDEM e a da tabela do fio, e o corte entre dominio e
		// tecnica e o `PericiasDeDominio` da mesma tabela. 100 e o teto pratico de cada contador (a
		// cadeia Basic -> Advanced -> Perfect da Mente soma ~95), e por isso a barra e sobre 100.
		GridContainer grade = Colunas();
		VBoxContainer dominio = Cartao("Domínio de Ki", grade);
		VBoxContainer tecnicas = Cartao("Técnicas de Ki", grade);
		float[] ps = _atributos.Pericias ?? [];
		for (int i = 0; i < Protocol.PericiasDeKi.Length; i++)
		{
			float v = i < ps.Length ? ps[i] : 0;
			LinhaComBarra(NomesLegiveis.Campo(Protocol.PericiasDeKi[i].Campo), $"{v:0.#}",
						  Math.Min(v / 100.0, 1), Tema.Ki, i < Protocol.PericiasDeDominio ? dominio : tecnicas);
		}
		Nota("100 é o teto prático: Basic, Advanced e Perfect da Mente somam ≈ 95.", dominio);
		Nota("cada família sobe usando a própria técnica.", tecnicas);
	}

	/// <summary>
	/// O PEDACO DESTA ABA NA ASSINATURA DE CACHE (ver `MenuJogo.Assinatura`): a basica cobre Ki, tanque,
	/// teto e a contagem de skills; as DEZENOVE entram aqui, com o mesmo `0.#` com que sao desenhadas --
	/// e o que faz a barra de uma pericia subir na tela quando um tiro rende, sem remontar a pagina a
	/// cada oscilacao invisivel.
	/// </summary>
	private string ExtraDaAssinaturaDeKi(SheetState f) =>
		string.Join(',', (_atributos.Pericias ?? []).Select(v => v.ToString("0.#")));
}
