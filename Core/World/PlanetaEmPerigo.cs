namespace Jandirus.Core.World;

/// <summary>Por que o chao em que se pisa esta em perigo -- e, portanto, por que a trilha de combate toca nele.</summary>
public enum PerigoDoChao : byte
{
	/// <summary>Nada: a musica do lugar segue.</summary>
	Nenhum = 0,

	/// <summary>O planeta esta morrendo -- o pavio lento (`Morrendo`) ou os cinco minutos da explosao (`Explodindo`).</summary>
	Explodindo = 1,

	/// <summary>Um chefe de saga esta no mundo, neste planeta (a fase `NoMundo` da cadeia, mesmo antes de alguem lutar).</summary>
	ChefeDeSaga = 2,
}

/// <summary>
/// O PLANETA DESTE CHAO ESTA EM PERIGO? A regra pura da trilha de perigo (dono, 2026-10-21): *"enquanto vc estiver em um planeta q esta
/// explodindo, a trilha sonora do jogo vai ser a mesma q toca ao entrar em combate, o mesmo vale quando vc ta em um planeta q ta com boss
/// de saga ativo"*. Quem toca a musica e o cliente (`World.SubZonas.cs`); a pergunta mora aqui pra a bancada do servidor medir a mesma
/// regra que a tela usa.
///
/// ============================ O QUE CONTA COMO "ESTAR NO PLANETA" ============================
/// A superficie dele e as sub-zonas que dividem o ceu dele (<see cref="SubZonas"/>): o Lookout e a caverna da Terra sao a Terra, o
/// castelo do Rei Vegeta e Vegeta. O ESPACO NAO: quem esta em orbita de um planeta explodindo ve a explosao, mas nao esta nele. A SALA
/// DO TEMPO TAMBEM NAO: e outra dimensao.
///
/// "EXPLODINDO" E A AGONIA INTEIRA: os cinco minutos da explosao e os vinte do pavio lento (`FaseDaMorte.Morrendo`) -- o ceu ja fica da
/// cor de ferrugem e o chao se abre no pavio. O planeta ja `Destruido` nao conta: nao ha mais chao pra se estar nele.
/// "CHEFE" E A FASE `NoMundo` DA CADEIA, que o servidor manda pelo `S2C.ChefesNosPlanetas`: Bills e Whis nao sao da cadeia, e o vilao do
/// Inferno nao esta num planeta.
/// ============================================================================================
/// </summary>
public static class PlanetaEmPerigo
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--subzonasteste` e robo `--diagsubzonas`): o perigo e so da ZONA do planeta -- quem esta na caverna ou
	/// no Lookout de uma Terra explodindo nao ouve a trilha de combate. Lido pela propria linha da regra, em <see cref="De"/>. Falso em
	/// jogo, sempre.
	/// </summary>
	public static bool SubZonaForaDoPerigoDeTeste;

	/// <summary>
	/// O PERIGO DO CHAO EM QUE ESTE CORPO PISA.
	/// </summary>
	/// <param name="zona">Onde o corpo esta.</param>
	/// <param name="pos">Onde, dentro da zona (o castelo do Rei Vegeta no Lookout e de Vegeta).</param>
	/// <param name="mortos">O registro de planetas morrendo e mortos (o `S2C.Mortos`, no cliente; o livro, no servidor).</param>
	/// <param name="planetasComChefe">As chaves (`ChaveDePlaneta.Texto`) dos planetas com chefe de saga no mundo.</param>
	public static PerigoDoChao De(ZoneKey zona, Vec2 pos, RegistroDeMortos mortos, IReadOnlySet<string> planetasComChefe)
	{
		ZoneKey chao = SubZonaForaDoPerigoDeTeste ? zona : SubZonas.ZonaDoCeu(zona, pos);
		if (ChaveDePlaneta.Da(chao) is not { } chave) return PerigoDoChao.Nenhum;
		if (mortos.De(chave) is { Fase: FaseDaMorte.Morrendo or FaseDaMorte.Explodindo }) return PerigoDoChao.Explodindo;
		if (planetasComChefe.Contains(chave.Texto)) return PerigoDoChao.ChefeDeSaga;
		return PerigoDoChao.Nenhum;
	}
}
