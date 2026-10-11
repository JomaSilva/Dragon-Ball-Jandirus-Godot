using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Forms;

namespace Jandirus.Server;

/// <summary>
/// ============================ A RAIVA POR AMIGO, EM DOIS DEGRAUS -- O QUE E DO SERVIDOR ============================
/// A regra do dono (2026-10-11) e os numeros moram em <see cref="RaivaEmDegraus"/> (Core). O gancho continua sendo
/// UM (`AmigoAbatido`, `GameServer.Formas.cs`), e os chamadores dele continuam sendo o funil da derrota
/// (`LutoNaVizinhanca`: nocaute = comum, morte = extrema) e a absorcao do Majin (`LutoPelaAbsorcao`; a do
/// bio-androide mata a vitima e chega pelo funil). Aqui mora o que este pedido acrescentou e que precisa de
/// servidor:
///
///   * o SALTO DE KI da furia extrema (<see cref="SaltoDeKiDaFuria"/>);
///   * QUEM RECEBE a cinematica e o tema (<see cref="QuemVeAFuria"/>): o enfurecido e quem esta perto dele;
///   * QUANDO a cinematica toca de novo (<see cref="JaEstavaNaFuriaDaCena"/>): a morte que vem depois do nocaute
///     e degrau NOVO, e a cena e dele;
///   * o DESPERTAR AUTOMATICO: a raiva que abre uma forma NOVA transforma o personagem sozinho -- subindo antes,
///     sem cena, pelos degraus que ja sao dele -- e a cinematica e a da transformacao, nunca as duas
///     (<see cref="DespertarPelaRaiva"/>). Sem forma nova a estrear, a cena e a da raiva e ele fica como esta.
///
/// O QUE ACALMA mora ao lado de quem acende (`AcalmarPelaMeditacao`, `GameServer.Formas.cs`): sentar pra meditar
/// fecha os dois degraus na hora.
///
/// ============================ A FUSAO NAMEKUSEIJIN FICOU DE FORA, E FOI O DONO QUE TIROU ============================
/// Uma rodada deste arquivo ligou o luto a ela (o absorvido tem o personagem apagado, e o pedido falava de "ser
/// absorvido" sem excecao). O dono, no mesmo dia: *"fusao namek n deveria dar o trigger na raiva"*. E o
/// comportamento do DM -- `Fusion.dm:301-310` torna a fusao definitiva e nao chama o `Do_Anger_Stuff` --, e faz
/// sentido por ser CONSENTIDA: ninguem tirou o amigo de quem assiste, ele escolheu ir. A `--raivateste` (secao 4)
/// roda a fusao de verdade na frente de quem declarou o absorvido amigo e mede que nada acende.
/// ======================================================================================================================
///
/// Bancadas: `--raivateste` (`GameServer.RaivaPorAmigoTeste.cs`), `--diagraiva` (`Client/RoboDaRaiva.cs`) e a pura
/// `raiva` (`Tools/AssetPipeline/RaivaBench.cs`).
/// ====================================================================================================================
/// </summary>
public partial class GameServer
{
	// =====================================================================
	// 1. QUANDO A CINEMATICA TOCA DE NOVO
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada `--raivateste`): a cena da furia volta a exigir CALMA -- o `wasRaging` literal do
	/// DM (`Murder.dm:111`: qualquer raiva aberta cala a cena), que era o port ate 2026-10-11. Com ele, quem viu o
	/// amigo CAIR e depois MORRER nao assiste a cinematica nem ouve o tema. Lido pelo <see cref="JaEstavaNaFuriaDaCena"/>.
	/// </summary>
	internal bool CenaSoSaiDaCalmaDeTeste;

	/// <summary>
	/// ESTE CORPO JA ESTAVA NA FURIA QUE A CENA MOSTRA? -- a pergunta que cala a cinematica quando o evento e so um
	/// prolongamento (um segundo amigo morrendo no meio do luto).
	///
	/// ============================ DIVERGENCIA DECLARADA DO DM ============================
	/// La a pergunta e `wasRaging = (rageExpire > world.time)` (`Murder.dm:111`), lida em `:119`: QUALQUER raiva
	/// aberta. Fazia sentido la porque os dois gatilhos escreviam o MESMO numero -- quem ja estava enfurecido pelo
	/// nocaute estava, literalmente, no mesmo estado que a morte daria.
	///
	/// So que ninguem morre de pe neste jogo: o amigo cai, e SO DEPOIS e finalizado ou absorvido (a absorcao do
	/// Majin EXIGE a vitima nocauteada, `MajinSaga.dm:131`). Com o `wasRaging` literal, quem assistiu a luta
	/// inteira entrava na raiva do nocaute e, segundos depois, via o amigo morrer SEM cena e SEM tema -- a
	/// cinematica so tocava pra quem chegou depois da queda. E o oposto da regra do dono, em que morte e absorcao
	/// SAO o degrau da cinematica.
	///
	/// Com os dois degraus, "ja estava" passou a ser "ja estava no degrau EXTREMO": subir da raiva comum pra
	/// extrema e uma erupcao nova, e a cena e dela. A recarga de 60 s (`rageCinematicCD`, `Murder.dm:139-140`)
	/// continua sendo o freio de repeticao.
	/// ======================================================================================
	/// </summary>
	/// <param name="nivelDeAntes">A raiva em que o corpo estava ANTES deste evento.</param>
	private bool JaEstavaNaFuriaDaCena(NivelDeRaiva nivelDeAntes) =>
		CenaSoSaiDaCalmaDeTeste ? nivelDeAntes != NivelDeRaiva.Nenhuma : nivelDeAntes == NivelDeRaiva.Extrema;

	// =====================================================================
	// 2. QUEM RECEBE A CENA E O TEMA
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada `--raivateste`): a cinematica e o tema da raiva voltam a ir pra ZONA INTEIRA, que
	/// era o port ate 2026-10-11 -- num planeta, o tema de quatro minutos tocava pra quem estava a quinhentos
	/// tiles do enfurecido. Lido pelo <see cref="QuemVeAFuria"/>.
	/// </summary>
	internal bool FuriaProPlanetaInteiroDeTeste;

	/// <summary>
	/// PRA QUEM VAI O PACOTE DA FURIA: o enfurecido e quem esta na MESMA zona e ao alcance da vista dele.
	///
	/// E o `view(src)` do original, nos tres lugares em que a cena fala com alguem: a narracao
	/// (`to_chat(view(src), ...)`, `Murder.dm:145`), o tema (`emit_RageMusic`: `for(var/mob/M in view(src))`,
	/// `BattleMusic.dm:147-153`) e as ondas de choque, que sao objetos no chao em volta dele. O raio e o mesmo com
	/// que este arquivo ja traduz `view()` em todo o resto do luto (<see cref="RaioDeTestemunha"/>).
	///
	/// A TRANSFORMACAO E OUTRA REGRA, e ela nao mudou: o tema de uma forma toca pro planeta inteiro por decisao do
	/// dono (ver `Transformacao._Ready`). A raiva de alguem nao e acontecimento do planeta.
	/// </summary>
	private List<ServerPlayer> QuemVeAFuria(ServerPlayer enlutado)
	{
		List<ServerPlayer> zona = ZoneList(enlutado.Zone.Hash);
		if (FuriaProPlanetaInteiroDeTeste) return [.. zona];

		float raio2 = RaioDeTestemunha * RaioDeTestemunha;
		var perto = new List<ServerPlayer>();
		foreach (ServerPlayer o in zona)
			if (o == enlutado || (o.Pos - enlutado.Pos).LengthSquared <= raio2) perto.Add(o);
		return perto;
	}

	/// <summary>
	/// QUEM RECEBEU CADA CENA DE FURIA (o enfurecido, e os ids pra quem o pacote foi). So a bancada liga; nulo em
	/// jogo. Anotada dentro do `TalvezACenaDaFuria`, ao lado da `EscutaDeFurias`.
	/// </summary>
	internal static List<(int Enfurecido, int[] Para)>? EscutaDeQuemViuAFuria;

	// =====================================================================
	// 3. O SALTO DE KI
	// =====================================================================

	/// <summary>
	/// NA FURIA EXTREMA, O KI DE QUEM TEM CONTROLE VAI NA HORA PRO TETO DE CARGA DELE. A regra (quem, quanto, e por
	/// que o `overcharge`) e do Core: <see cref="RaivaEmDegraus.SaltarOKi"/>.
	///
	/// CHAMADO DO GANCHO (`AmigoAbatido`), em TODO evento do degrau extremo -- a erupcao e o prolongamento. E a
	/// mesma leitura que o DM faz do numero da raiva: `Do_Anger_Stuff` reescreve `Anger = max(Anger, MaxAnger)` a
	/// cada amigo que morre (`Murder.dm:112`), enfurecido ou nao; so a cinematica e que nao repete. O salto e
	/// estado, nao espetaculo -- e como ele nunca DESCE o Ki, repetir nao custa nada a ninguem.
	///
	/// O QUE OS OUTROS VEEM sai do estado que ja se replica: Ki acima do tanque acende a aura de excesso no tique
	/// seguinte (`TickDaCarga` -> `EntityState.Sobrecarregado`). Daqui so saem o poder e a ficha do dono, na hora
	/// -- sem isto a barra de Ki dele so pularia no proximo tique de ficha (ate 200 ms).
	/// </summary>
	private void SaltoDeKiDaFuria(ServerPlayer pl, NivelDeRaiva grau)
	{
		double antes = pl.Ficha.Ki;
		if (!RaivaEmDegraus.SaltarOKi(grau, pl.Ficha)) return;

		RepercutirPoder(pl);
		MandarFicha(pl);
		Avisar(pl, "a fúria faz a sua energia EXPLODIR -- o Ki salta de uma vez pro máximo que o seu corpo aguenta.");
		GD.Print($"[server] {pl.Name}: a furia extrema levou o Ki de {antes:0} a {pl.Ficha.Ki:0} "
				 + $"({pl.Ficha.Ki / Math.Max(pl.Ficha.MaxKi, 1e-9) * 100:0}% do tanque)");
	}

	// =====================================================================
	// 4. O QUE QUEM ESTA PERTO LE
	// =====================================================================

	/// <summary>
	/// A FRASE PRA QUEM VE ALGUEM SE ENFURECER, uma por degrau. O DM tem as duas (`Death.dm:83`, *"has become
	/// EXTREMELY enraged"*, e `KO.dm:38`, *"has become very angry"*); a do nocaute desceu um tom junto com o degrau
	/// dele -- pela regra do dono o nocaute e "angry", e "muito furioso" passou a ser o que so a morte da.
	/// </summary>
	private static string FraseDeQuemVeARaiva(string nome, bool extrema) => extrema
		? $"você percebe que {nome} ficou EXTREMAMENTE furioso!!!"
		: $"você percebe que {nome} ficou com raiva!";

	// =====================================================================
	// 5. A RAIVA QUE ABRE UMA FORMA NOVA TRANSFORMA SOZINHA
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada `--raivateste`): a raiva so LIBERA a forma e fica esperando a tecla C -- o port
	/// ate 2026-10-11. Lido pelo <see cref="DespertarPelaRaiva"/>.
	/// </summary>
	internal bool RaivaNaoTransformaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--raivateste`): a cinematica da furia NAO cede o momento a transformacao -- as
	/// duas tocam juntas, que e o que o dono proibiu. Lido pelo `AFuriaVaiVirarForma` (`GameServer.Formas.cs`).
	/// </summary>
	internal bool FuriaNaoCedeACenaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancadas `--raivateste` e `--formasteste`): a cinematica da furia volta a ceder o momento
	/// ao PROXIMO C -- o `hasssj ||` do `anger_will_transform()` (`Murder.dm:129-130`), que era o port ate
	/// 2026-10-11: quem ja tinha a forma despertada via o amigo morrer SEM cena de raiva e sem transformacao
	/// nenhuma. Lido pelo `AFuriaVaiVirarForma` (`GameServer.Formas.cs`).
	/// </summary>
	internal bool FuriaCedeAoGestoDeTeste;

	/// <summary>
	/// O PLANO DA RAIVA DESTE CORPO (nulo = ela nao desperta forma nenhuma): a forma NOVA que estreia e os degraus
	/// ja despertados que ele sobe antes. A regra inteira -- e os dois defeitos injetaveis dela -- e do Core:
	/// <see cref="SubidaPelaRaiva.Planejar"/>.
	/// </summary>
	private PlanoDeSubida? PlanoDaRaiva(ServerPlayer pl)
	{
		// QUEM ESTA CALMO SAI AQUI, antes de montar o perfil: isto roda no tique da forma de todo corpo.
		if (NivelDaRaivaDe(pl) == NivelDeRaiva.Nenhuma) return null;
		return SubidaPelaRaiva.Planejar(pl.Forma, pl.Ficha.BP, Perfil(pl));
	}

	/// <summary>
	/// ============================ O DESPERTAR PELA RAIVA E AUTOMATICO -- DONO, 2026-10-11 ============================
	/// *"ao bater reqs de transformacao q precisam de raiva, o personagem se transforma AUTOMATICAMENTE, o jogador n
	/// tem q apertar nada, exemplo: ficou com raiva com o req de ssj1, o personagem vai virar ssj1 pela primeira vez
	/// de forma forcada com cinematica e tudo"* -- e, na mesma mensagem: *"a preferencia de cinematica e de
	/// transformacao (...) a cinematica vai ser a da transformacao e nao a da raiva"*.
	///
	/// E O QUE O DM FAZ, e o port nao fazia: aqui a raiva so abria a porta e esperava a tecla C. La o despertar
	/// mora no laco da arvore de skills, que CHAMA a forma sozinho na primeira vez e so entao marca que ela e do
	/// personagem -- `savant.SSj(); savant.hasssj=1` (`supersaiyan.dm:105-118`), o SSJ2 (`:122-148`), a linha
	/// Legendary (`:60-104`, com o comentario *"rage AUTO-transforma so na PRIMEIRA vez de cada forma (pra
	/// DESBLOQUEAR)... Forma ja desbloqueada => transformacao MANUAL (C)"*), o Beast (`:164-170`) e o Heran
	/// (`heran.dm:22-52`).
	///
	/// ============================ VALE PRAS DUAS RAIVAS, E CADA UMA PAGA O SEU PRECO ============================
	/// O mesmo `switch(savant.Emotion)` tem o ramo `"Angry"`, e o dono o pediu em 2026-10-11: *"o rage normal (ao
	/// ver um amigo ser nocauteado mas n morto) tb faz vc se transformar da mesma forma q o extreme, porem o
	/// requisito de bp pro rage normal liberar sua proxima transformacao e maior"*. Nao ha `if` aqui pra isso: o
	/// plano pergunta ao `Avaliar` com a raiva que o corpo TEM, e e o passo 9 de la que cobra o agio da comum
	/// (`FormaDef.AgioNaRaivaComum`). A raiva comum continua sem cinematica propria, sem tema e sem salto de Ki --
	/// quando ela transforma, o que toca e a estreia da forma.
	///
	/// ============================ PELO CAMINHO DA ESTREIA DE SEMPRE ============================
	/// Quem entra na forma e o `EntrarNaForma` -- o mesmo da tecla C e do despertar assistido pelo mestre. E dele
	/// que saem o Ki cheio, o multiplicador, o marco, o anuncio pra zona e a cinematica de estreia com o tema;
	/// um segundo caminho que escrevesse a forma na mao daria um Super Saiyajin sem cena.
	///
	/// ============================ SO A PRIMEIRA VEZ -- E O QUE JA E DELE SERVE DE DEGRAU ============================
	/// O que a raiva ESTREIA e forma que ele nunca despertou (o `!savant.hasssj` de la). Forma que ja e do
	/// personagem nao vem sozinha: se e so ela que a raiva "abriria", ninguem se transforma, e a cinematica que
	/// toca e a da raiva (`AFuriaVaiVirarForma`).
	///
	/// Mas ela serve de DEGRAU (dono, 2026-10-11: *"se ele tiver os reqs por exemplo do ssj2 e ele estiver na base
	/// e ficar com raiva, ele vai pro ssj1 e comeca a cinematica do ssj2"*): quando a forma nova fica um degrau
	/// acima de onde o corpo esta, ele sobe pelos que ja despertou -- SEM cena, no mesmo instante -- e estreia a
	/// nova de la, com a cinematica DELA. A corrente e a divergencia do DM estao em
	/// <see cref="SubidaPelaRaiva"/>.
	///
	/// ============================ E ELE E UM ESTADO, NAO UM INSTANTE ============================
	/// Chamado do tique da forma (`TickDaForma`), e nao so do gancho da raiva: no DM tambem e um laco, que
	/// pergunta de novo enquanto o humor durar. E o que faz valer os casos em que o corpo NAO PODE no instante da
	/// furia -- caido, preso noutra cinematica, em Oozaru, sem as redeas -- e passa a poder ainda enfurecido (ele
	/// se levanta, e vira), e o caso em que o requisito que faltava chega depois (o poder cruza a porta com a
	/// raiva acesa). Tambem e o que encadeia: quem desperta o Super Saiyajin com poder pro 2 desperta o 2 quando a
	/// primeira cena solta o corpo, como la.
	///
	/// AS GUARDAS SAO AS DO GESTO (`Transformar`: caido, chefe roteirizado) E AS DO CORPO OCUPADO
	/// (`mst_form_blocked`, `MasterStudent.dm:317-318`: `if(transing || Apeshit) return 1`), mais a de quem esta
	/// sem as redeas -- o corpo que a fera ou a furia lendaria esta dirigindo espera voltar a ser do dono.
	///
	/// A ORDEM COM O SALTO DE KI: o salto vem ANTES (no gancho, no instante da furia) e atravessa a transformacao
	/// inteiro -- o `AplicarForma` leva a RAZAO do Ki pro tanque da forma, e o "Ki cheio na estreia" do
	/// `EntrarNaForma` e um `Math.Max`, que nao desce ninguem. Quem saltou pra 140% estreia a forma com 140%.
	/// ==============================================================================================================
	/// </summary>
	/// <returns>TRUE se o corpo acabou de entrar numa forma por causa da raiva.</returns>
	private bool DespertarPelaRaiva(ServerPlayer pl)
	{
		if (RaivaNaoTransformaDeTeste) return false;
		if (PlanoDaRaiva(pl) is not { } plano) return false;

		if (CaidoPraForma(pl) || !AscendePorDecisao(pl, avisar: false)) return false;
		if (EmCena(pl) || pl.Oozaru != FormaOozaru.Nao || SemAsRedeas(pl)) return false;

		GD.Print($"[server] {pl.Name}: a RAIVA desperta {plano.Alvo.Id} sozinha (raiva {NivelDaRaivaDe(pl)}"
				 + (plano.Degraus.Length > 0 ? $", subindo por {string.Join(" -> ", plano.Degraus.Select(d => d.Id))}" : "") + ")");
		Avisar(pl, "a raiva rompe o que te segurava -- o seu corpo se transforma SOZINHO.");

		// OS DEGRAUS QUE JA SAO DELE, SEM CENA: nao e gesto do jogador, e a cinematica deste instante e a da forma
		// nova. Cada um entra pelo mesmo `EntrarNaForma` (o buff, o Ki levado na razao, o anuncio que veste o corpo
		// na tela de todo mundo).
		foreach (FormaDef degrau in plano.Degraus) EntrarNaForma(pl, degrau, semCena: true);
		EntrarNaForma(pl, plano.Alvo);
		return true;
	}
}
