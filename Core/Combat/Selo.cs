using Jandirus.Core.World;

namespace Jandirus.Core.Combat;

/// <summary>O que o passo do selo decidiu neste instante. Ver <see cref="Selo.Passo"/>.</summary>
public enum FimDoSelo
{
	/// <summary>Continua preso, e nada mudou.</summary>
	Continua,

	/// <summary>O pote sumiu do mundo: a durabilidade caiu pro piso e o preso e AVISADO.</summary>
	Enfraqueceu,

	/// <summary>Saiu -- por poder proprio (o `BPModulus>=1.25`) ou porque o selo se gastou.</summary>
	Solta,
}

/// <summary>
/// O ESTADO DE ESTAR SELADO -- as seis `mob/var` de `Modules/Magic/Sealing.dm:22-28`, num objeto.
///
/// ============================ O QUE E UM SELO NESTE JOGO ============================
/// O cabecalho do arquivo do DM (`Sealing.dm:1-9`) explica o desenho inteiro, e vale copiar a
/// ideia porque ela nao e obvia: selar NAO e matar nem prender numa cela do mapa. O corpo sai do
/// mundo e vai pra uma area propria ("Sealed Grounds", `:10-20` -- sem dia, sem noite, sem clima
/// e sem lua), e a unica saida e **ficar mais forte que quem selou**.
///
/// A forca do selo e uma BALANCA de duas pontas, e o autor do original escreveu as duas:
///
///   * selo preso ao POTE   -- vale o TETO DURO (40 bilhões) e nao depende de quem selou, mas
///                             quebra facil por acao de fora: basta destruir o pote;
///   * selo preso ao PODER  -- vale o BP de quem selou, e nao ha como quebrar de fora; em
///                             compensacao o preso so precisa passar aquele numero pra sair.
///
/// O Mafuba usa a primeira ponta (e por um DEFEITO do original, ver <see cref="TetoDuro"/>); a
/// Dead Zone usa a segunda.
/// ====================================================================================
///
/// ============================ POR QUE NO CORE, E POR QUE UM OBJETO ============================
/// As contas sao tres linhas de aritmetica e uma comparacao -- mas sao as tres linhas que decidem
/// se um jogador sai ou nao da prisao, e por isso elas tem que ser exercitaveis sem subir o jogo.
/// O objeto existe (em vez de seis campos soltos no `ServerPlayer`) porque o estado inteiro
/// **vai pro disco**: no DM as seis variaveis nao sao `tmp`, entao selo sobrevive a logout, e o
/// `Login.dm:258` re-dispara o teste de fuga na entrada. Um selo que morresse no relog seria uma
/// prisao com porta giratoria.
/// ============================================================================================
/// </summary>
public sealed class Selo
{
	/// <summary>`isSealed` (`Sealing.dm:23`). Falso = este objeto e so lastro no save.</summary>
	public bool Preso;

	/// <summary>
	/// `SealerBP` (`:24`): o numero que o preso precisa superar. Ver <see cref="TetoDuro"/> pro
	/// caso em que quem selou passou zero.
	/// </summary>
	public double BpDoSelador;

	/// <summary>
	/// `SealedContainerDur` (`:25`): a durabilidade do pote, **copiada** dele a cada tique do
	/// proprio pote (`:121`). ZERO significa "nao ha pote" -- e nao "pote quebrado".
	/// </summary>
	public double DuracaoDoPote;

	/// <summary>`SealHP` (`:27`): 100 ao selar; corroi quando o preso ja passou do selador.</summary>
	public double VidaDoSelo = 100;

	/// <summary>
	/// `sealercontainersig` (`:28`): QUAL pote guarda este preso.
	///
	/// No DM e uma string sorteada (`"[rand(1,9999)]"`, `:138`) e a varredura compara string com
	/// string (`:56`). Aqui e o `Obra.Id`, que ja e unico por construcao -- a assinatura sorteada
	/// existia no original justamente porque objeto de BYOND nao tem id estavel. Zero = sem pote
	/// (o caso da Dead Zone).
	/// </summary>
	public int PoteId;

	// ---- `SealedLocation` (`:26`): pra onde o corpo volta. Guardado em PECAS pelo mesmo motivo
	// que a `Obra` guarda: nome de zona nao e endereco, e um planeta gerado homonimo de outro
	// devolveria a pessoa no mundo errado. Ver `Server/GameServer.Tech.Obra.ZonaTipo`.
	public byte VoltaTipo;
	public string VoltaNome = "";
	public ulong VoltaSeed;
	public float VoltaX, VoltaY;

	/// <summary>A zona de volta, montada das pecas. Ver o comentario dos campos.</summary>
	public ZoneKey ZonaDeVolta => new(VoltaTipo, VoltaNome, VoltaSeed);

	public void GuardarVolta(ZoneKey z, float x, float y)
	{
		VoltaTipo = z.Kind;
		VoltaNome = z.Name;
		VoltaSeed = z.Seed;
		VoltaX = x;
		VoltaY = y;
	}

	/// <summary>
	/// O TETO DURO: 40 bilhoes (`Sealing.dm:34`, escrito `4.0e010` porque nao cabe em 2^31).
	///
	/// ============================ ELE E O DEFEITO DO MAFUBA, E O PORTE MANTEM ============================
	/// A linha do DM e `if(SealingPersonBP==0) SealerBP = 4.0e10`, e ela existe pro selo "de pote"
	/// descrito no cabecalho: sem dono, vale o teto. Acontece que o `MafubaBlast` declara
	/// `var/SealStrength = 0` (`:194`) e **nunca atribui nada a ele** -- o `A.BP=expressedBP` de
	/// `:175` alimenta so o dano do feixe. Entao TODO Mafuba do jogo original sela em 40 bilhoes,
	/// independente de quem o lancou.
	///
	/// Isto fica como esta de proposito. Trocar por `A.BP` seria CONSERTAR o jogo, nao copia-lo --
	/// e o efeito de "consertar" seria um Mafuba de novato virar prisao de papel enquanto o de um
	/// veterano viraria uma pena maior que a do original. A escolha e do dono do jogo, nao do
	/// porte; o que o porte deve e deixar o numero visivel em vez de escondido.
	/// ===================================================================================================
	/// </summary>
	public const double TetoDuro = 4.0e10;

	/// <summary>
	/// O QUANTO O PRESO PRECISA SUPERAR PRA SAIR: `BPModulus(expressedBP, SealerBP) >= 1.25`
	/// (`Sealing.dm:46`). Nao e "ficar mais forte", e ficar **25% mais forte**.
	/// </summary>
	public const double RazaoDeFuga = 1.25;

	/// <summary>
	/// O PISO DE DURABILIDADE DE UM SELO SEM POTE NO MUNDO: `SealedContainerDur = 0.25` (`:59`).
	///
	/// E o castigo por o pote ter sumido (destruido, ou o servidor caiu com ele na mao): o selo
	/// nao arrebenta na hora, passa a corroer QUATRO vezes mais rapido -- `0.001/0.25`.
	/// </summary>
	public const double DuracaoSemPote = 0.25;

	/// <summary>
	/// QUANTO O SELO CORROI POR PASSO: `SealHP -= 0.001/SealedContainerDur` (`:49` e `:61`).
	///
	/// ============================ A DIVISAO POR ZERO DO ORIGINAL ============================
	/// Com `SealedContainerDur == 0` (que e o caso da Dead Zone, `:297`) a linha `:49` do DM e
	/// literalmente `0.001/0` -- runtime de divisao por zero, que no BYOND **aborta o proc**. O
	/// efeito observavel disso e que o resto do `TestEscape` daquele tique nao roda: nao ha
	/// corrosao, e a unica saida continua sendo a razao de fuga.
	///
	/// Aqui isso vira uma guarda explicita (`dur <= 0` -> corrosao zero) em vez de uma excecao.
	/// **O comportamento e o mesmo**; o que muda e que nao ha um erro por tique por preso no log
	/// do servidor. Copiar a excecao seria copiar o barulho, nao a regra.
	/// =======================================================================================
	/// </summary>
	public static double Corrosao(double duracaoDoPote) =>
		duracaoDoPote <= 0 ? 0 : 0.001 / duracaoDoPote;

	/// <summary>
	/// O PRESO JA PODE ARREBENTAR O SELO NA FORCA? `BPModulus(expressedBP, SealerBP) >= 1.25`.
	///
	/// Repare que quem entra e o BP **expresso** e nao o BP base: quem esta selado continua podendo
	/// transformar e carregar Ki la dentro, e e assim que a fuga acontece de verdade. Foi o que o
	/// cabecalho do DM chamou de *"the prisoner simply has to power up past it to escape"*.
	/// </summary>
	public static bool PodeArrebentar(double expressedBP, double bpDoSelador) =>
		CombatMath.BpModulus(expressedBP, bpDoSelador) >= RazaoDeFuga;

	/// <summary>
	/// O BP QUE O SELO VALE, dado o que quem selou informou: `SealMob`, `Sealing.dm:33-35`.
	/// Zero vira o <see cref="TetoDuro"/> -- e e por ai que o Mafuba entra.
	/// </summary>
	public static double BpDoSelo(double bpDeQuemSelou) =>
		bpDeQuemSelou == 0 ? TetoDuro : bpDeQuemSelou;

	/// <summary>
	/// SELA. O `SealMob(SealingPersonBP, ContainerDur)` do DM (`Sealing.dm:31-42`), sem a parte
	/// que mexe no mundo (mudar de zona e agendar o teste sao do servidor).
	/// </summary>
	public void Selar(double bpDeQuemSelou, double duracaoDoPote, int poteId, ZoneKey volta, float x, float y)
	{
		Preso = true;
		BpDoSelador = BpDoSelo(bpDeQuemSelou);
		// `if(ContainerDur>0) ... else SealedContainerDur = 0` (`:36-39`) -- negativo tambem vira 0.
		DuracaoDoPote = duracaoDoPote > 0 ? duracaoDoPote : 0;
		VidaDoSelo = 100;
		PoteId = poteId;
		GuardarVolta(volta, x, y);
	}

	/// <summary>SOLTA. O `UnSealMob()` (`:67-74`) sem a parte de mover o corpo.</summary>
	public void Soltar()
	{
		Preso = false;
		BpDoSelador = 0;
		DuracaoDoPote = 0;
		VidaDoSelo = 100;
		PoteId = 0;
		VoltaTipo = 0;
		VoltaNome = "";
		VoltaSeed = 0;
		VoltaX = VoltaY = 0;
	}

	/// <summary>
	/// UM PASSO DO `TestEscape()` (`Sealing.dm:44-66`) -- a funcao pura que decide o destino.
	///
	/// ============================ A ORDEM DOS TRES TESTES E A DO DM, E ELA IMPORTA ============================
	///   1. `:46-47`  razao de fuga -> sai, e sai independente de pote, de vida de selo e de tudo;
	///   2. `:48-50`  **so** se o preso ja passou do selador em numero cru: corroi, e quando a vida
	///                do selo cai abaixo de 1, sai. E o degrau intermediario -- passar o selador nao
	///                basta, mas comeca a gastar a prisao;
	///   3. `:51-62`  **so** com pote de durabilidade entre 0 e 1 (ou seja, um pote JA DANIFICADO):
	///                se aquele pote nao existe mais no mundo, o selo cai pro piso e o preso e
	///                avisado -- e corroi de novo, com a mesma conta.
	///
	/// O passo 3 corroer DE NOVO no mesmo tique nao e engano de leitura: as duas linhas `SealHP -=`
	/// existem no DM (`:49` e `:61`) e um preso forte num pote quebrado paga as duas.
	/// =========================================================================================================
	/// </summary>
	/// <param name="expressedBP">O BP expresso do preso AGORA (ele pode se transformar la dentro).</param>
	/// <param name="poteExisteNoMundo">
	/// A varredura `for(var/obj/items/SealingItem/O in world)` de `:54-57`, ja respondida por quem
	/// tem a lista do mundo na mao. So e consultada quando o passo 3 roda.
	/// </param>
	public FimDoSelo Passo(double expressedBP, bool poteExisteNoMundo)
	{
		if (!Preso) return FimDoSelo.Solta;

		// 1. `:46-47`
		if (PodeArrebentar(expressedBP, BpDoSelador)) return FimDoSelo.Solta;

		// 2. `:48-50`
		if (expressedBP > BpDoSelador)
		{
			if (VidaDoSelo >= 1) VidaDoSelo -= Corrosao(DuracaoDoPote);
			else return FimDoSelo.Solta;
		}

		// 3. `:51-62` -- pote DANIFICADO (entre 0 e 1). Pote intacto (1) e pote nenhum (0) nao entram.
		if (DuracaoDoPote is > 0 and < 1)
		{
			bool avisar = false;
			if (!poteExisteNoMundo)
			{
				DuracaoDoPote = DuracaoSemPote;
				// `if(SealHP==100)` (`:60`): o aviso sai UMA vez, no tique em que o selo comeca a
				// ceder. Comparacao exata como no DM -- e ela funciona porque a vida so sai de 100
				// pela subtracao logo abaixo.
				avisar = VidaDoSelo == 100;
			}

			if (VidaDoSelo >= 1) VidaDoSelo -= Corrosao(DuracaoDoPote);
			else return FimDoSelo.Solta;

			if (avisar) return FimDoSelo.Enfraqueceu;
		}

		return FimDoSelo.Continua;
	}
}
