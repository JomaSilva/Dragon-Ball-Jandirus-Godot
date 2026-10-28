namespace Jandirus.Core.Stats;

/// <summary>
/// O QUE O CORPO ESTA FAZENDO COM O PROPRIO PODER -- o `relaxedstate` do original
/// (`Modules/Stats/BP/Power Control.dm:98-108`), que la sao quatro frases soltas numa string.
/// </summary>
public enum EstadoDoPoder
{
	/// <summary>"Using Full Power" (`:107-108`): `powerMod` em 1.</summary>
	PoderTotal,

	/// <summary>"Using some power." (`:103-104`): `powerMod` abaixo de 1 -- o corpo se segura.</summary>
	Contendo,

	/// <summary>"Relaxed" (`:98-99`): o corpo soltou o poder (`Fighter.relaxado`) -- 5 de poder, e ninguem o sente.</summary>
	Relaxado,

	/// <summary>"Concealing Power" (`:100-101`): o poder expresso esta travado por uma tecnica de sumir (`Fighter.isconcealed`).</summary>
	Escondendo,
}

/// <summary>
/// **POWER UP E POWER DOWN** -- as regras de `Modules/Stats/BP/Power Control.dm` que mexem em QUANTO do
/// proprio poder o corpo poe pra fora: relaxar e ficar pronto (`ReadyUp`, `:39-52`), baixar pra uma
/// porcentagem (`Power_Control`, `:176-185`), a raiva que nao deixa relaxar (`RelaxCheck`, `:87-111`) e a
/// leitura do estado (`relaxedstate`). Subir de volta segurando C e do `CargaDeKi` (o estagio `Retomando`,
/// `:153-157`).
///
/// ============================ DOIS GESTOS, E O QUE CADA UM FAZ (regra do dono, 2026-10-12) ============================
/// O DM tem tres: a barra (`Power_Control`), relaxar (`ReadyUp`, `powerMod = 0.01`) e esconder (`Conceal_Power`,
/// `isconcealed`). O dono jogou e cortou um: *"esconder o ki e relaxar é redundante ambos abaixam o poder de forma
/// praticamente igual (esconder o ki é ate menos), relaxar e o poder cair deveria ser somente pra quem sabe
/// controlar o proprio poder, tire o botao de esconder o ki e faça com q o de relaxar faca o poder do personagem
/// cair pra 5 de poder de luta sempre, ficando invisivel pra scouters etc, mas extremamente fraco fisicamente, vc
/// mudar teu poder na barra de % n esconde o ki do scouter, ou sense, so serve pra se segurar ou usar todo o
/// poder, esconder o ki totalmente so com relaxar"*. Ficaram dois:
///
///   * **A BARRA** (`Fighter.powerMod`, de 0,01 a 1): multiplica o poder EXPRESSO no fim da conta (`base.dm:135`,
///     `Fighter.Power.cs`). Serve pra se segurar ou usar o poder todo, e so: o dano dado e o recebido, o que o
///     Sense e o scouter dos outros LEEM e o que a IA pesa descem na mesma proporcao -- mas ela nunca ESCONDE
///     ninguem (<see cref="SemABarra"/>: o piso dos 5 de quem nao se sente e medido sem ela);
///   * **RELAXAR** (`Fighter.relaxado`): o poder expresso trava em 5 (a trava do `isconcealed` do DM,
///     `base.dm:114-115`, que aqui vale pros dois -- <see cref="Escondido"/>), o Sense, o scouter e a telepatia
///     nao acham o corpo, e ele bate e apanha como quem nao tem poder. E o unico jeito de esconder o ki.
///
/// Os dois pedem controle de ki (o degrau 5 de Basic Ki Control, que concede o `Power_Control`): quem cobra e o
/// servidor (`GameServer.PoderEmUso.cs`). DIVERGENCIA DECLARADA de `Power Control.dm:39` e `Login.dm:301`, onde
/// relaxar e `mob/default/verb` de todo corpo: a regra do dono vale mais.
///
/// O QUE NENHUM DOS DOIS MUDA: a regeneracao e o dreno de Ki, o ganho de treino por tique (`Egains`,
/// `base.dm:160`) e as portas de transformacao (que leem o BP BASE). O que os dois mudam alem do poder e o VIGOR:
/// <see cref="FatorDeDescanso"/>. (2026-10-28) E o ganho da LUTA ENTRE JOGADORES: <see cref="FatorDaTroca"/> e
/// <see cref="ReservaPorSegundo"/> (o pedido 148).
///
/// ============================ DUAS FRASES DO ORIGINAL QUE O CODIGO DELE NAO CUMPRE ============================
/// A caixa do `Power_Control` (`:178`) promete duas coisas: *"You can only use this to set your power lower
/// than it is"* e *"This will slowly bring you down to that level"*. O codigo (`:182-185`) faz outra:
///
///     while(!is_drawing &amp;&amp; usr.powerMod &gt; numb &amp;&amp; usr.powerMod &gt; 0)
///         usr.powerMod -= 0.01 * (Ekiskill+(kicontrolskill+kigatheringskill)/25)
///         usr.powerMod = max(numb,usr.powerMod)
///     usr.powerMod = numb
///
///   * o laco NAO TEM `sleep`: ele roda inteiro no mesmo tique, e a descida e NA HORA (a taxa pela pericia
///     de ki nunca chega a ser vista). **Portado o codigo: baixar e instantaneo** (<see cref="Baixar"/>);
///   * a ultima linha escreve `numb` de qualquer jeito, entao pedir MAIS do que se expressa SOBE na hora.
///     **Aqui vale a frase, e nao a linha** -- DIVERGENCIA DECLARADA de `:185`, que ja era a deste port
///     (`GameServer.Tecnicas.G9.cs`, bancada `--seloteste`): o verbo so baixa, e quem quer subir segura C
///     (aos poucos) ou fica pronto (<see cref="AlternarProntidao"/>, que no original tambem devolve 100% na
///     hora).
/// ================================================================================================================
/// </summary>
public static class PoderEmUso
{
	/// <summary>`powerMod = 1` (`Power Control.dm:52`): o poder inteiro.</summary>
	public const double Cheio = 1;

	/// <summary>
	/// O PISO DA BARRA: o `max(1,numb)/100` do `Power_Control` (`:180-181`). Um por cento do proprio poder.
	/// </summary>
	public const double Piso = 0.01;

	/// <summary>
	/// O VERBO QUE DIZ "ESTE CORPO SABE CONTROLAR O PROPRIO PODER": o `Power_Control`, que nasce no degrau 5 de
	/// Basic Ki Control (`Mind.dm:279-283`). E a porta da barra e, desde 2026-10-12, a de relaxar.
	/// </summary>
	public const string VerboDoControle = "Power_Control";

	/// <summary>
	/// O DIVISOR DA RAIVA QUE NAO DEIXA RELAXAR: `Anger > ((MaxAnger-100)/1.66)+100` (`:91`). E o mesmo
	/// limiar em que o humor do DM vira "Angry" (`Stats.dm:448`).
	/// </summary>
	public const double DivisorDaRaivaQueAcorda = 1.66;

	/// <summary>
	/// EM QUANTO UMA TECNICA DE SUMIR DIVIDE O DRENO PASSIVO DE VIGOR: `concealedBuff = 1.5` (`master.dm:138`),
	/// lido pelo `CheckNutrition` (`StaminaDrain.dm:30`). So pra documentar: quem escreve o campo e o `Statify`,
	/// pelo `isconcealed` -- relaxar nao passa por ele (o premio de quem relaxa e o <see cref="DescansoRelaxado"/>).
	/// </summary>
	public const double FolgaDeVigorDoSigilo = 1.5;

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancadas `--poderteste` e `--v020teste`)
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): a raiva nao acorda quem relaxa -- furioso, o corpo continua
	/// relaxado. Lido pela propria regra, em <see cref="IrritadoDemaisPraRelaxar"/>.
	/// </summary>
	public static bool RaivaDeixaRelaxarDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o `concealedBuff` volta a ser calculado e jogado fora --
	/// sumir por tecnica nao poupa vigor nenhum (o estado de antes de 2026-10-11). Lido pela propria linha do
	/// conserto, em `Nutricao.QueimarVigor`.
	/// </summary>
	public static bool SigiloNaoPoupaVigorDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): relaxar nao trava o poder nem esconde ninguem -- o corpo
	/// "relaxado" segue com o poder inteiro a vista de todo Sense e de todo scouter. Lido pela propria regra, em
	/// <see cref="Escondido"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool RelaxadoNaoEscondeDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o piso de quem nao se sente volta a ser medido COM a barra --
	/// um corpo fraco que se segura a 1% expressa 5 ou menos e some do Sense e do scouter, que e o jogo de antes
	/// de 2026-10-12. Lido pela propria regra, em <see cref="SemABarra"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool BarraEscondeDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): reunir energia nao acorda quem relaxa -- o corpo enche o tanque
	/// e passa dos 100% de Ki escondido, com 5 de poder. Lido pela propria regra, em <see cref="ACargaAcorda"/>.
	/// Falso em jogo, sempre.
	/// </summary>
	public static bool CargaNaoAcordaDeTeste;

	// =====================================================================
	// LER
	// =====================================================================

	/// <summary>
	/// RELAXADO: o corpo soltou o proprio poder (`Fighter.relaxado`) -- ou (2026-10-17) e uma LARVA de bio-androide, que fica sempre assim (o
	/// dono: *"ela fica sempre no modo 'relaxado' com 5 de bp e escondido do sense e rastreadores"*). A larva nao precisa do botao e nem a raiva
	/// nem a carga a acordam: quem a solta e amadurecer (`Races.BioAndroids.EmLarva`).
	/// </summary>
	public static bool Relaxado(Fighter f) => f.relaxado || Races.BioAndroids.EmLarva(f);

	/// <summary>
	/// **O PODER DESTE CORPO ESTA ESCONDIDO?** -- travado em 5 (`Fighter.Power.cs`), fora do Sense, do scouter e
	/// da telepatia. Dois caminhos chegam aqui: uma tecnica de sumir (a invisibilidade, o Sneak, o embate de
	/// Zanzoken: `isconcealed`) e relaxar. E a unica pergunta que quem LE o sigilo faz -- a trava do poder, o corte
	/// do multiplicador (`GameServer.Sigilo.cs`) e o denominador do Sense (`GameServer.PoderEmUso.cs`).
	/// </summary>
	public static bool Escondido(Fighter f) => f.isconcealed || (Relaxado(f) && !RelaxadoNaoEscondeDeTeste);

	/// <summary>
	/// **O PODER QUE O CORPO EXPRESSARIA SEM A BARRA** -- o dono: *"vc mudar teu poder na barra de % n esconde o ki
	/// do scouter, ou sense, so serve pra se segurar ou usar todo o poder"*.
	///
	/// O DM corta do Sense, do scouter e da telepatia quem expressa 5 de BP ou menos (`HtmlUI.dm:370` e `:411`,
	/// `Communication.dm:53`), e la a barra entra nessa conta: um corpo de 400 de poder a 1% expressa 4 e some.
	/// Aqui quem pergunta "este corpo tem energia que se sinta?" pergunta por ESTE numero, e nao pelo expresso:
	/// a barra continua baixando o que os outros leem (10% do poder = 10% da leitura), mas nao tira ninguem da
	/// lista. DIVERGENCIA DECLARADA das tres linhas: a regra do dono vale mais.
	///
	/// A barra e o ULTIMO fator do poder expresso (`... * powerMod`, `Fighter.Power.cs`), entao dividir por ela a
	/// desfaz. Escondido (<see cref="Escondido"/>) o expresso e a constante 5, que nao passou pela barra: vai como
	/// esta -- e o que mantem escondido quem relaxa.
	/// </summary>
	public static double SemABarra(Fighter f)
	{
		if (BarraEscondeDeTeste || Escondido(f) || !double.IsFinite(f.powerMod)) return f.expressedBP;
		return f.expressedBP / Math.Clamp(f.powerMod, Piso, Cheio);
	}

	// =====================================================================
	// O DESCANSO -- regra do dono (2026-10-12)
	// =====================================================================

	/// <summary>Quantas vezes o vigor volta mais depressa com a barra no piso (1% do poder).</summary>
	public const double DescansoNoPiso = 2.5;

	/// <summary>Quantas vezes o vigor volta mais depressa RELAXADO -- o maximo que ha.</summary>
	public const double DescansoRelaxado = 3.5;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--v020teste`): baixar o poder nao rende vigor nenhum -- o jogo de antes de
	/// 2026-10-12. Lido pela propria regra, em <see cref="FatorDeDescanso"/>. Falso em jogo, sempre.
	/// </summary>
	public static bool DescansoNaoRendeDeTeste;

	/// <summary>
	/// QUANTO O VIGOR VOLTA MAIS DEPRESSA PRA QUEM NAO ESTA USANDO O PODER INTEIRO. O dono: *"a regeneração de vigor
	/// é a maxima relaxado, e um pouco menor se vc tiver em 1% de poder, assim a regeneração de vigor sendo uma
	/// função linear em relação a % do poder liberado, com o teto em 2,5x quando chega em 1% do poder total, e
	/// relaxado seria 3,5x"*.
	///
	/// Uma reta pela barra: 1x com o poder inteiro, <see cref="DescansoNoPiso"/> a 1%. Relaxado e um degrau a
	/// parte, acima da reta: <see cref="DescansoRelaxado"/>. Sumir por tecnica (`isconcealed`) nao entra aqui --
	/// o que o sigilo da e a folga no DRENO (<see cref="FolgaDeVigorDoSigilo"/>), como no DM.
	///
	/// No DM nao ha isto: la o `powerMod` so corta o poder. Multiplica o vigor que a digestao devolve
	/// (`Nutricao.Digerir`), sem cobrar mais nutricao por ele.
	/// </summary>
	public static double FatorDeDescanso(Fighter f)
	{
		if (DescansoNaoRendeDeTeste) return 1;
		if (Relaxado(f)) return DescansoRelaxado;
		double contido = (Cheio - Math.Clamp(f.powerMod, Piso, Cheio)) / (Cheio - Piso);   // 0 = poder inteiro, 1 = no piso
		return 1 + (DescansoNoPiso - 1) * contido;
	}

	// =====================================================================
	// O TREINO DE LUTA -- regra do dono (2026-10-28, o pedido 148)
	// =====================================================================

	/// <summary>
	/// QUANTO UM GOLPE NA TROCA ENTRE JOGADORES RENDE PELA BARRA DE QUEM GANHA. O dono: *"quanto menor o % de poder liberado menor os ganhos e
	/// menos bp no tick, mas isso n conta o efetivo, apenas a % de bp liberado pelo power control"*. Uma reta pela barra, como a do descanso:
	/// 1x com o poder inteiro, <see cref="GainKnobs.TrocaNoPiso"/> a 1%. Relaxado e o piso -- o corpo soltou o poder inteiro.
	/// </summary>
	public static double FatorDaTroca(Fighter f)
	{
		if (Relaxado(f)) return GainKnobs.TrocaNoPiso;
		double solto = (Math.Clamp(f.powerMod, Piso, Cheio) - Piso) / (Cheio - Piso);   // 1 = poder inteiro, 0 = no piso
		return GainKnobs.TrocaNoPiso + (1 - GainKnobs.TrocaNoPiso) * solto;
	}

	/// <summary>
	/// QUANTO DA RESERVA DA LUTA UM SEGUNDO DE TROCA QUENTE GUARDA (o *"bp invisivel"* do dono): <see cref="GainKnobs.ReservaPorSegundo"/> vezes
	/// a barra -- e NADA relaxado ou com os proprios golpes sem arremessar (*"enquanto o relaxed sem KB o bp por tick n aconteceria"*). A chave
	/// do knockback e do servidor (`ServerPlayer.Knockback`), por isso vem de fora.
	/// </summary>
	public static double ReservaPorSegundo(Fighter f, bool knockback)
	{
		if (Relaxado(f) || !knockback) return 0;
		return GainKnobs.ReservaPorSegundo * Math.Clamp(f.powerMod, Piso, Cheio);
	}

	/// <summary>A raiva a partir da qual nao se relaxa: `((MaxAnger-100)/1.66)+100` (`:91`).</summary>
	public static double RaivaQueAcorda(Fighter f) => (f.MaxAnger - 100) / DivisorDaRaivaQueAcorda + 100;

	/// <summary>`Anger > ((MaxAnger-100)/1.66)+100` (`:91`).</summary>
	public static bool IrritadoDemaisPraRelaxar(Fighter f) =>
		!RaivaDeixaRelaxarDeTeste && f.Anger > RaivaQueAcorda(f);

	/// <summary>
	/// O ESTADO, num caso so -- o `relaxedstate` (`:98-108`). Relaxado vence (e ele que decide o que o corpo
	/// expressa); depois a tecnica de sumir; depois a barra.
	/// </summary>
	public static EstadoDoPoder Estado(Fighter f) =>
		Relaxado(f) ? EstadoDoPoder.Relaxado
		: f.isconcealed ? EstadoDoPoder.Escondendo
		: f.powerMod < Cheio ? EstadoDoPoder.Contendo
		: EstadoDoPoder.PoderTotal;

	/// <summary>
	/// QUANTO DO PROPRIO PODER A BARRA DEIXA PASSAR, em porcentagem inteira de 1 a 100. So e 100 com o poder
	/// inteiro: no meio de uma subida, 99,9% ainda le 99. E porcentagem do PROPRIO poder -- nao e poder de
	/// luta, e pode ir pra tela de quem nao tem scouter. (Relaxado a barra nao diz nada: o corpo expressa 5.)
	/// </summary>
	public static int Porcento(Fighter f)
	{
		if (!double.IsFinite(f.powerMod) || f.powerMod >= Cheio) return 100;
		return (int)Math.Clamp(Math.Floor(f.powerMod * 100 + 1e-9), 1, 99);
	}

	// =====================================================================
	// MEXER
	// =====================================================================

	/// <summary>
	/// O PEDIDO DE 1 A 100 VIRA FRACAO: `numb = min(numb,100)`, `numb = max(1,numb)`, `numb /= 100`
	/// (`:179-181`). Quem chama ja recusou o que nao e numero finito.
	/// </summary>
	public static double FracaoDoPedido(double pedido) => Math.Clamp(pedido, 1, 100) / 100;

	/// <summary>
	/// BAIXAR ATE UMA FRACAO, na hora (`:182-185`, ver o cabecalho da classe). Devolve falso -- e nao mexe
	/// em nada -- quando a fracao pedida nao esta ABAIXO do que o corpo ja expressa.
	/// </summary>
	public static bool Baixar(Fighter f, double fracao)
	{
		if (fracao >= f.powerMod) return false;
		f.powerMod = fracao;
		return true;
	}

	/// <summary>
	/// O PROXIMO APERTO DO BOTAO RELAXA? So com o poder inteiro e de pe: de qualquer ponto abaixo de 100% (e de
	/// relaxado) o mesmo botao e "ficar pronto". E a pergunta que o servidor faz antes de cobrar o controle de ki
	/// -- voltar ao poder inteiro nao pede porta nenhuma.
	/// </summary>
	public static bool VaiRelaxar(Fighter f) => !f.relaxado && f.powerMod >= Cheio;

	/// <summary>
	/// RELAXAR / FICAR PRONTO -- o `ReadyUp` ("Toggle Readiness", `:39-52`):
	///
	///     if(powerMod >= 1)  "You are relaxed."       powerMod=0.01
	///     else               "You are now ready."     powerMod=1
	///
	/// Com o poder inteiro, relaxa; relaxado, ou em qualquer ponto abaixo de 100% da barra, fica pronto (o poder
	/// volta inteiro na hora -- inclusive de 40%). Devolve verdadeiro quando RELAXOU.
	///
	/// DIVERGENCIA DECLARADA de `:48` (regra do dono, 2026-10-12): relaxar nao e mais o piso de 1% da barra. E um
	/// estado proprio (`Fighter.relaxado`) em que o poder expresso trava em 5 e o corpo some do Sense e do
	/// scouter -- ver o cabecalho da classe. A barra fica onde estava (em 100%, que e de onde se relaxa).
	/// </summary>
	public static bool AlternarProntidao(Fighter f)
	{
		if (VaiRelaxar(f))
		{
			f.relaxado = true;
			return true;
		}
		f.relaxado = false;
		f.powerMod = Cheio;
		return false;
	}

	/// <summary>
	/// A RAIVA ACORDA QUEM RELAXA -- o `RelaxCheck` (`:90-95`): relaxado e com a raiva acima do limiar, o
	/// poder volta inteiro. Devolve verdadeiro quando acordou (quem chama diz as duas frases de `:92-93`).
	///
	/// (No DM quem leva a barra a 1% tambem conta como relaxado -- `if(powerMod==0.01) isrelaxed=1`, `:110-111` --
	/// e a raiva o acorda. Aqui se segurar a 1% e uma escolha de luta, e nao relaxar: a raiva nao mexe na barra.)
	/// </summary>
	public static bool ARaivaAcorda(Fighter f)
	{
		// (2026-10-17) a LARVA nao acorda: ela e relaxada pela carapaca, e nao pelo botao.
		if (!f.relaxado || Races.BioAndroids.EmLarva(f) || !IrritadoDemaisPraRelaxar(f)) return false;
		f.relaxado = false;
		f.powerMod = Cheio;
		return true;
	}

	/// <summary>
	/// REUNIR ENERGIA ACORDA QUEM RELAXA: segurar a tecla de carregar e o contrario de soltar o corpo. O relaxado
	/// deixa de se-lo e a barra comeca do piso -- e dali que o estagio `Retomando` da carga (`CargaDeKi.Passo`,
	/// `Power Control.dm:153-157`) sobe o poder aos poucos, que e o power-up de quem estava escondido. Devolve
	/// verdadeiro quando acordou.
	///
	/// Sem isto o relaxado encheria o tanque e passaria dos 100% de Ki com 5 de poder e fora de todo scouter, pra
	/// aparecer ja carregado ao ficar pronto.
	/// </summary>
	public static bool ACargaAcorda(Fighter f)
	{
		if (!f.relaxado || Races.BioAndroids.EmLarva(f) || CargaNaoAcordaDeTeste) return false;   // (2026-10-17) a larva nao acorda
		f.relaxado = false;
		f.powerMod = Piso;
		return true;
	}
}
