namespace Jandirus.Core.Stats;

/// <summary>
/// O QUE O CORPO ESTA FAZENDO COM O PROPRIO PODER -- o `relaxedstate` do original
/// (`Modules/Stats/BP/Power Control.dm:98-108`), que la sao quatro frases soltas numa string.
/// </summary>
public enum EstadoDoPoder
{
	/// <summary>"Using Full Power" (`:107-108`): `powerMod` em 1.</summary>
	PoderTotal,

	/// <summary>"Using some power." (`:103-104`): `powerMod` abaixo de 1 e acima do piso.</summary>
	Contendo,

	/// <summary>"Relaxed" (`:98-99`): `powerMod` no piso de 0,01.</summary>
	Relaxado,

	/// <summary>"Concealing Power" (`:100-101`): o poder expresso esta travado pelo sigilo.</summary>
	Escondendo,
}

/// <summary>
/// **POWER UP E POWER DOWN** -- as regras de `Modules/Stats/BP/Power Control.dm` que mexem em QUANTO do
/// proprio poder o corpo poe pra fora: relaxar e ficar pronto (`ReadyUp`, `:39-52`), baixar pra uma
/// porcentagem (`Power_Control`, `:176-185`), a raiva que nao deixa relaxar (`RelaxCheck`, `:87-111`) e a
/// leitura do estado (`relaxedstate`). Subir de volta segurando C e do `CargaDeKi` (o estagio `Retomando`,
/// `:153-157`); esconder o poder e o `Fighter.isconcealed`, lido pelo `PowerLevel` (`base.dm:114-115`).
///
/// ============================ O QUE O `powerMod` E, E O QUE ELE MUDA ============================
/// Um fator de 0,01 a 1 que multiplica o poder EXPRESSO no fim da conta (`base.dm:135`,
/// `Fighter.Power.cs`). Tudo o que le o poder expresso muda junto: o dano dado e o recebido, o que o Sense e
/// o scouter dos outros leem, o que a IA de um NPC pesa, e o bonus de lutar com alguem mais forte
/// (`fight_gain_mult`, `combatgains.dm:93-95`: quem se contem diante de um igual treina como quem luta com
/// alguem mais forte, ate o teto de 2x). Ele NAO entra na regeneracao nem no dreno de Ki, nem no ganho de
/// treino por tique (`Egains`, `base.dm:160`), nem nas portas de transformacao (que leem o BP BASE).
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
	/// O PISO: `powerMod = 0.01` de quem relaxa (`:48`) e o `max(1,numb)/100` do `Power_Control`
	/// (`:180-181`). Um por cento do proprio poder.
	/// </summary>
	public const double Piso = 0.01;

	/// <summary>
	/// O DIVISOR DA RAIVA QUE NAO DEIXA RELAXAR: `Anger > ((MaxAnger-100)/1.66)+100` (`:91`). E o mesmo
	/// limiar em que o humor do DM vira "Angry" (`Stats.dm:448`).
	/// </summary>
	public const double DivisorDaRaivaQueAcorda = 1.66;

	/// <summary>
	/// EM QUANTO O SIGILO DIVIDE O DRENO PASSIVO DE VIGOR: `concealedBuff = 1.5` (`master.dm:138`), lido
	/// pelo `CheckNutrition` (`StaminaDrain.dm:30`). So pra documentar: quem escreve o campo e o `Statify`.
	/// </summary>
	public const double FolgaDeVigorDoSigilo = 1.5;

	// =====================================================================
	// OS DEFEITOS INJETADOS (bancada `--poderteste`)
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): a raiva nao acorda quem relaxa -- furioso, o corpo fica
	/// em 1% do poder. Lido pela propria regra, em <see cref="IrritadoDemaisPraRelaxar"/>.
	/// </summary>
	public static bool RaivaDeixaRelaxarDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--poderteste`): o `concealedBuff` volta a ser calculado e jogado fora --
	/// esconder o poder nao poupa vigor nenhum (o estado de antes de 2026-10-11). Lido pela propria linha do
	/// conserto, em `Nutricao.QueimarVigor`.
	/// </summary>
	public static bool SigiloNaoPoupaVigorDeTeste;

	// =====================================================================
	// LER
	// =====================================================================

	/// <summary>O `powerMod` esta no piso (com a folga de um erro de ponto flutuante).</summary>
	public static bool NoPiso(Fighter f) => f.powerMod <= Piso + 1e-9;

	/// <summary>
	/// RELAXADO: o `isrelaxed` do DM, DERIVADO do piso e nao guardado.
	///
	/// DIVERGENCIA DECLARADA de `Power Control.dm:96`: la o `isrelaxed` e um campo, e a linha que o
	/// apagaria quando o poder sai do piso esta escrita `if(!powerMod==0.01)` -- a negacao vale primeiro,
	/// `!powerMod` e 0 e a comparacao nunca e verdadeira. Quem relaxa e depois carrega ate 100% segue
	/// "Relaxed" na ficha, e a raiva ainda o "acorda" com o poder inteiro. A outra metade (`:110-111`,
	/// `if(powerMod==0.01) isrelaxed=1`) funciona. Aqui valem as duas, como estao escritas pra valer:
	/// relaxado e quem esta no piso, por onde quer que tenha chegado la.
	/// </summary>
	public static bool Relaxado(Fighter f) => NoPiso(f);

	/// <summary>A raiva a partir da qual nao se relaxa: `((MaxAnger-100)/1.66)+100` (`:91`).</summary>
	public static double RaivaQueAcorda(Fighter f) => (f.MaxAnger - 100) / DivisorDaRaivaQueAcorda + 100;

	/// <summary>`Anger > ((MaxAnger-100)/1.66)+100` (`:91`).</summary>
	public static bool IrritadoDemaisPraRelaxar(Fighter f) =>
		!RaivaDeixaRelaxarDeTeste && f.Anger > RaivaQueAcorda(f);

	/// <summary>
	/// O ESTADO, num caso so -- o `relaxedstate` (`:98-108`).
	///
	/// DIVERGENCIA DECLARADA de `:100-101`: no DM "Concealing Power" so se le com o corpo TAMBEM relaxado
	/// (o teste mora dentro do `if(isrelaxed)`); quem esconde o poder com o `powerMod` em 1 le "Using Full
	/// Power" com o poder expresso travado em 5. Aqui o sigilo vence sempre que esta ligado: e ele que
	/// decide o que o corpo expressa, seja qual for o `powerMod`.
	/// </summary>
	public static EstadoDoPoder Estado(Fighter f) =>
		f.isconcealed ? EstadoDoPoder.Escondendo
		: NoPiso(f) ? EstadoDoPoder.Relaxado
		: f.powerMod < Cheio ? EstadoDoPoder.Contendo
		: EstadoDoPoder.PoderTotal;

	/// <summary>As quatro frases do `relaxedstate`, como o jogador as le.</summary>
	public static string Frase(EstadoDoPoder e) => e switch
	{
		EstadoDoPoder.Escondendo => "escondendo o poder",
		EstadoDoPoder.Relaxado => "relaxado",
		EstadoDoPoder.Contendo => "usando parte do poder",
		_ => "usando todo o poder",
	};

	/// <summary>
	/// QUANTO DO PROPRIO PODER ESTA EM USO, em porcentagem inteira de 1 a 100. So e 100 com o poder
	/// inteiro: no meio de uma subida, 99,9% ainda le 99. E porcentagem do PROPRIO poder -- nao e poder de
	/// luta, e pode ir pra tela de quem nao tem scouter.
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
	/// RELAXAR / FICAR PRONTO -- o `ReadyUp` ("Toggle Readiness", `:39-52`), verbo de TODO corpo
	/// (`mob/default/verb`, dado no login: `Login.dm:301`):
	///
	///     if(powerMod >= 1)  "You are relaxed."       powerMod=0.01
	///     else               "You are now ready."     powerMod=1
	///
	/// Com o poder inteiro, relaxa (vai ao piso); em qualquer ponto abaixo, fica pronto (volta a 100% na
	/// hora -- inclusive de 40%). Devolve verdadeiro quando RELAXOU.
	/// </summary>
	public static bool AlternarProntidao(Fighter f)
	{
		if (f.powerMod >= Cheio)
		{
			f.powerMod = Piso;
			return true;
		}
		f.powerMod = Cheio;
		return false;
	}

	/// <summary>
	/// A RAIVA ACORDA QUEM RELAXA -- o `RelaxCheck` (`:90-95`): relaxado e com a raiva acima do limiar, o
	/// poder volta inteiro. Devolve verdadeiro quando acordou (quem chama diz as duas frases de `:92-93`).
	/// </summary>
	public static bool ARaivaAcorda(Fighter f)
	{
		if (!Relaxado(f) || !IrritadoDemaisPraRelaxar(f)) return false;
		f.powerMod = Cheio;
		return true;
	}
}
