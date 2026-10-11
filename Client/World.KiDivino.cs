using Godot;

namespace Jandirus.Client;

/// <summary>
/// O DESPERTAR DO KI DIVINO, NA TELA -- o instante em que ele acontece num corpo desta zona
/// (`S2C.GodKiDespertou`, repassado pelo `GameClient.GodKiDespertou`).
///
/// E o `GodkiIncreaseScene()` do DM (`godki.dm:156-163`): o `ssg.wav` e duas ondas de choque em volta do
/// corpo. O terremoto de la (`Quake()`) virou um solavanco de camera, e so pra QUEM despertou -- pros
/// outros e uma luz que se abre ali adiante.
///
/// NAO E CINEMATICA: nao prende o corpo, nao troca a musica e nao tem estreia. A cena de verdade e a da
/// primeira forma divina, que o `Transformacao` ja toca quando ele subir nela.
///
/// DE DIA O ANEL, DE NOITE O CLARAO -- ver <see cref="ClaraoDivino"/>.
/// </summary>
public partial class World
{
	/// <summary>A cor do anel: o dourado claro do ki divino antes de ele ganhar a cor de uma forma.</summary>
	private static readonly Color LuzDivina = new(1.0f, 0.93f, 0.72f);

	/// <summary>Quanto dura o anel mais largo -- e o clarao da noite, que apaga junto com ele.</summary>
	private const double SegundosDaLuz = 0.95;

	/// <summary>O tamanho do clarao na regua da <see cref="LuzDeKi"/>: 1 e um Ki Wave, 4 um Final Flash, 6 o teto dela.</summary>
	private const float TamanhoDoClarao = 6f;

	/// <summary>
	/// QUANTOS DESPERTARES FORAM DESENHADOS neste processo. So pra bancada (`--diaggodki`): e o que separa "o
	/// pacote chegou" de "o efeito nasceu no mundo" -- um corpo que o cliente nao acha nao ganha anel nenhum, e
	/// o pacote teria chegado do mesmo jeito.
	/// </summary>
	public static int DespertaresDesenhadosDeTeste;

	private void AoDespertarDivino(int quem)
	{
		Node2D? corpo = Corpo(quem);
		if (corpo == null) return;

		Vector2 p = Desenhado(corpo);
		AudioDirector.EfeitoNoLugar(corpo, Trilha.KiDivino, 1f, alcance: 900f);

		// TRES ANEIS, do mais curto pro mais largo: o primeiro e o estalo, o ultimo e a luz que varre a cena.
		CombatFx.Onda(_atores, p, 90, Dourado, 0.30);
		CombatFx.Onda(_atores, p, 210, LuzDivina, 0.60);
		CombatFx.Onda(_atores, p, 360, LuzDivina, SegundosDaLuz);
		ClaraoDivino(p);
		DespertaresDesenhadosDeTeste++;

		if (GameClient.Instance is { } cli && quem == cli.LocalId) Sacudir(6f);
	}

	/// <summary>
	/// O CLARAO DA NOITE. O anel de choque nao e `unshaded` (nenhum anel da casa e): ele pega a cor do ambiente,
	/// e na primeira foto -- de madrugada, sob tempestade -- nao sobrou anel nenhum na tela. Justo o despertar,
	/// que e uma luz se abrindo. Entao no escuro ele ganha a luz que os tiros de ki ja lancam
	/// (<see cref="LuzDeKi"/>): a mesma receita, a mesma vaga no teto de luzes da qualidade grafica e a mesma
	/// regra do dono -- de dia ela nem nasce (o `Pendurar` nao cria node), e quem se ve e o anel.
	///
	/// A LUZ E FILHA DE UM NODE SO DELA, que a apaga e se libera sozinho. O anel do `CombatFx.Onda` nao devolve
	/// o node dele; e pendurada no CORPO ela andaria com ele e teria de ser lembrada por alguem.
	/// </summary>
	private void ClaraoDivino(Vector2 onde)
	{
		var clarao = new Node2D { Name = "ClaraoDivino", Position = onde };
		_atores.AddChild(clarao);
		LuzDeKi.Pendurar(clarao, LuzDivina, TamanhoDoClarao);
		if (clarao.GetNodeOrNull<LuzDeKi>(LuzDeKi.NomeDoNode) is not { } luz)
		{
			clarao.QueueFree();   // de dia: luz nenhuma nasceu
			return;
		}

		// FORTE NO COMECO E CAINDO DEPRESSA NO FIM: um clarao, e nao uma lampada que se apaga devagar.
		Tween t = clarao.CreateTween();
		t.TweenProperty(luz, "energy", 0f, SegundosDaLuz).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
		t.TweenCallback(Callable.From(clarao.QueueFree));
	}
}
