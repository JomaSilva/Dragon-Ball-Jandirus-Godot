namespace Jandirus.Core.Tech;

/// <summary>
/// O SUPER COMPUTADOR -- `obj/Super_Computer` (`Code/Modules/Tech/Tier3.dm:134-226`): *"A super computer to deal with
/// ALL of your robot armies!"* (tecnologia 100, cinco milhoes de zeni).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// O `Encrypt()` poe uma chave de controle; com ela posta, o `Use()` pede a senha antes de abrir o menu: *Make Body*
/// e *Mindswap* (um corpo de androide vazio, e a troca de corpo com ele), *Make Droid* (`makeCopy(4,"Android",
/// "None",/mob/npc/Clone,FALSE)`: um NPC "[nome] Android Body Model-#N", com a folha do androide e o BP de quem
/// pede), *Destroy All Androids*, *Decrypt*, *Bolt* e *Upgrade* -- *Super-Rename* (25.000), *Android Resurrection*
/// (75.000: *"You will be able to resurrect androids, or be resurrected by this computer. This is automatic, and it
/// only happens upon death."*), *Repair* (de graca) e *Upgrade Armor* (50.000: `maxarmor = usr.intBPcap*1.2`).
/// O droide e um `mob/npc/Clone` (`Geti Star.dm:9-53`): clicado pelo dono, obedece *Follow*, *Attack Target*,
/// *Attack Nearest* e *Destroy*.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * AS ORDENS SAEM DO COMPUTADOR, e valem pra TODOS os droides de quem as da. La se clica em cada droide; este
///     port nao tem clique em corpo. As tres funcoes sao as da copia do Split Form (`GuiarSplitformG12`), que no
///     original e da mesma familia: seguir, atacar o alvo MARCADO do dono, atacar quem estiver mais perto.
///   * HA TETO DE DROIDES (<see cref="MaxDeDroides"/> por dono). La nao ha teto nem custo: um exercito sem fim de
///     copias com o BP inteiro do dono.
///   * O DROIDE E DA SESSAO: sai do mundo quando o dono sai do jogo (como a copia do Split Form, a simulacao e os
///     companheiros), e fica parado enquanto o dono esta em outra zona. La ele fica no mundo pra sempre.
///   * "DESTRUIR" E SO OS SEUS. O *Destroy All Androids* de la apaga os droides de TODO MUNDO
///     (`for(var/mob/npc/Clone/A in NPC_list) if(!A.client) del(A)`).
///   * O DROIDE NAO MATA NEM DECEPA (o golpe nao-letal de todo corpo sem dono, como a simulacao).
///   * SO O DONO CIFRA. La qualquer um que chegue num computador sem chave poe a dele.
///   * A CHAVE ABRE O COMPUTADOR PRA SESSAO de quem a digita (ate ele sair do jogo, ou a chave mudar), em vez de ser
///     pedida a cada uso.
///   * A RESSURREICAO E DE QUEM CIFROU. La o teste e `S.controller == signature` (`DeathRegen.dm:15`) -- e
///     `controller` e a SENHA digitada no `Encrypt()`: so dispararia pra quem pusesse a propria assinatura como
///     senha. Aqui o computador guarda a CONTA de quem o cifrou, e e esse androide que ele refaz ao lado dele, na
///     vigia seguinte a morte (de dez em dez segundos; la e na hora). So ANDROIDE (`if(Race=="Android")`: a raca, ou
///     um corpo convertido no Android Lab), so com a melhoria comprada, e so com o computador de pe -- quem o
///     derruba acaba com ela.
///     E UMA SAIDA DA MORTE. A regra do dono (2026-09-04) e que morto fica morto ate as esferas ou o Enma; o proprio
///     DM, na revisao dele, escreve que *"immortal/regen-temporario/computador ainda revivem"* (`DeathRegen.dm:39`),
///     e por isso ela veio -- como a maquina de clonagem.
///   * AS MELHORIAS COBRAM DE QUEM PODE PAGAR. La as tres contas estao viradas (`if(usr.zenni<=75000)
///     usr.zenni-=75000`): quem tem MENOS que o preco compra e fica devendo, e quem tem mais ouve *"You don't have
///     enough Zenni."* E aceitam recursos, pelo dobro.
///   * A BLINDAGEM SO SOBE (como o reforco do telepad): la `maxarmor = usr.intBPcap*1.2` rebaixaria a armadura de um
///     computador blindado por alguem mais avancado.
///   * O *MAKE BODY* E O *MINDSWAP* nao vieram: este port nao tem troca de corpo -- o caminho pra um corpo de
///     androide e a conversao do Android Lab.
///   * O *SUPER-RENAME* nao veio: la ele liga uma variavel (`superrename`) que nenhuma outra linha do jogo le.
/// ==================================================================================
/// </summary>
public static class SuperComputador
{
	public const string Tipo = "Super_Computer";

	public const string VerboCifrar = "sc_cifrar", VerboChave = "sc_chave", VerboDecifrar = "sc_decifrar",
						VerboDroide = "sc_droide", VerboOrdem = "sc_ordem", VerboMelhorar = "sc_up", VerboInfo = "sc_info";

	/// <summary>As ordens (o argumento do <see cref="VerboOrdem"/>).</summary>
	public const string Seguir = "seguir", AtacarOAlvo = "alvo", AtacarOProximo = "proximo", Destruir = "destruir";

	/// <summary>As melhorias (o que o <see cref="VerboMelhorar"/> compra).</summary>
	public const string Ressurreicao = "ressurreicao", Blindagem = "blindagem", Conserto = "conserto";

	public const string ChaveDaChave = "chave", ChaveConta = "conta", ChaveNome = "nome", ChaveRessurreicao = "ressurreicao";

	/// <summary>"This takes 75000 zenni."</summary>
	public const double CustoDaRessurreicao = 75_000;

	/// <summary>"This takes 50000 zenni."</summary>
	public const double CustoDaBlindagem = 50_000;

	/// <summary>`maxarmor = usr.intBPcap*1.2`.</summary>
	public const double FatorDaBlindagem = 1.2;

	/// <summary>O teto de letras da chave de controle.</summary>
	public const int MaxDeChave = 16;

	/// <summary>Quantos droides um dono mantem de pe. Decisao deste port: la nao ha teto.</summary>
	public const int MaxDeDroides = 4;

	/// <summary>`Model-#[rand(1,1000)]`.</summary>
	public const int MaxDoModelo = 1000;

	/// <summary>`MAX_AGGRO_RANGE` (`1A Defines.dm:67`): o raio do "Attack Nearest", em tiles.</summary>
	public const float TilesDoMaisProximo = 20;

	/// <summary>`if(d >= 2)`: o droide so anda atras do dono quando ele esta a dois tiles ou mais.</summary>
	public const float TilesDeFolga = 2;

	/// <summary>De quanto em quanto o computador confere se o androide dele morreu.</summary>
	public const double SegundosEntreVigias = 10;

	/// <summary>A raca do droide (`makeCopy(4,"Android",...)`): os atributos dele saem do berco dela.</summary>
	public const string RacaDoDroide = "Android";

	/// <summary>A chave de CORPO do droide -- uma folha so, como o interceptador (`z.icon='GochekAndroid.dmi'`).</summary>
	public const string CorpoDoDroide = "AndroidBodyModel";

	public const string FolhaDoDroide = "res://Assets/Sprites/NPCs/GochekAndroid.tres";

	/// <summary>`z.name="[name] Android Body Model-#[rand(1,1000)]"`.</summary>
	public static string NomeDoDroide(string dono, int modelo) => $"{dono} Android Body Model-#{modelo}";

	/// <summary>A armadura que a blindagem da: o teto de tecnologia de quem paga, vezes 1,2.</summary>
	public static double ArmaduraBlindada(double tetoDeTecnologia) => tetoDeTecnologia * FatorDaBlindagem;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X9): o computador cifrado abre pra qualquer um, sem a chave.</summary>
	public static bool AbreSemAChaveDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X9): nao ha teto de droides -- o exercito sem fim do original.</summary>
	public static bool SemTetoDeDroidesDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X9): o computador refaz quem o cifrou mesmo sem ser androide.</summary>
	public static bool RefazQualquerUmDeTeste;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X9): o droide nao obedece a ordem de atacar -- so segue.</summary>
	public static bool DroideSurdoDeTeste;
}
