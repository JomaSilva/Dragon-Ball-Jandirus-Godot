namespace Jandirus.Core.Tech;

/// <summary>
/// (2026-10-21) O DESENHO DO MAINFRAME DE ANDROIDES CONFORME O LABORATORIO. O pedido do dono, palavra por palavra: *"fazer com q caso o
/// player humano faça a maquina de bio androide ou tsufurujin de neotuffle, o sprite da maquina mude pra esse [...] Cell Pod.png enquanto se
/// ele for fazer androide a maquina é igual ao spirte padrao da maquina de fazer androides"*.
///
/// O mainframe (`Android_Creation_Mainframe`) e uma maquina so; o que muda e o laboratorio instalado nele (`Obra.Lab`, gravado pelo
/// `GameServer.InstalarLab`): 1 e o Android Lab (Humano), 2 o Bio-Android Lab (Humano) e 3 o Neo Tuffle Lab (Tsufurujin). Com o 2 e o 3
/// ele passa a ser desenhado no CELL POD do DU (`Assets/Sprites/DU/Items/Cell Pod.tres`: o Cell bebe borbulhando no tubo verde, 31
/// quadros de 48x87 a 10 por segundo, em loop -- animado sempre). Sem laboratorio (0) e com o Android Lab (1) continua o desenho do
/// catalogo (`64x64tech`, estado `android_pod`).
///
/// QUEM DECIDE E O SERVIDOR, no pacote das construcoes (`GameServer.ArteDaObra`): o cliente desenha a folha que chega, como desenha a de
/// qualquer obra. Recolher a maquina continua tirando o laboratorio (nao foi pedido mudar) -- instalada de novo, ela volta ao desenho do
/// catalogo.
/// </summary>
public static class ArteDoLaboratorio
{
	/// <summary>O tipo do mainframe no catalogo (`construcoes.json`).</summary>
	public const string TipoDoMainframe = "Android_Creation_Mainframe";

	/// <summary>Os laboratorios que trocam o desenho (`Obra.Lab`): o Bio-Android Lab e o Neo Tuffle Lab.</summary>
	public const int LabDoBio = 2, LabDoNeoTuffle = 3;

	/// <summary>A folha do Cell Pod e a animacao dela (a unica que ela tem: `default`, em loop).</summary>
	public const string FolhaDoCellPod = "res://Assets/Sprites/DU/Items/Cell Pod.tres", EstadoDoCellPod = "default";

	/// <summary>
	/// O DESLOCAMENTO DO POD, no `pixel_x`/`pixel_y` do BYOND: o cliente encosta o canto inferior esquerdo do quadro no da celula e soma
	/// isto (`ObraDesenhada.MontarSprite`). O pod tem 48 px de largura e e simetrico em volta da coluna 24; a celula tem 32. Com -8 o eixo do
	/// tubo cai no meio da celula -- a que o servidor bloqueia e em frente da qual se aperta a tecla E --, e o pe continua no chao da celula.
	/// Decisao minha (no DU o pod e so um icone de lista, sem `pixel_x`), conferida na foto do robo `--diagcellpod`.
	/// </summary>
	public const double DeslocamentoXDoCellPod = -8, DeslocamentoYDoCellPod = 0;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--tuffleteste` e `--bioteste`, robo `--diagcellpod`): nenhum laboratorio troca o desenho -- o jogo de antes
	/// de 2026-10-21. Lido pela propria linha da regra, em <see cref="NoCellPod"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool LabSemCellPodDeTeste;

	/// <summary>Esta obra e um mainframe com o Bio-Android Lab ou o Neo Tuffle Lab -- e e desenhada no Cell Pod?</summary>
	public static bool NoCellPod(string tipo, int lab) =>
		tipo == TipoDoMainframe && !LabSemCellPodDeTeste && lab is LabDoBio or LabDoNeoTuffle;
}
