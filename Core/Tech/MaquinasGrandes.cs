namespace Jandirus.Core.Tech;

/// <summary>
/// O SIMULADOR -- `obj/items/Simulator` (`Code/Modules/Tech/Tier2.dm:61-182`): *"Simulators create mobs for you to defeat."*
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// O `Start()` ergue ao lado da maquina um `mob/npc/Enemy/Simulation` com a cara de quem pediu e o poder dele
/// limitado ao teto da maquina (`x.BP = usr.BP; if(x.BP > maxbp) x.BP = maxbp`, 500 de fabrica), com os atributos
/// EFETIVOS de quem pediu como base (`x.physoff = usr.Ephysoff + 1`, `x.physdef = usr.Ephysdef + 1`,
/// `x.technique = usr.Etechnique`...), e o manda atras dele (`x.foundTarget(usr)`). Cada simulacao gasta 0,1 de
/// energia, e pedir de novo com uma de pe a CANCELA. As melhorias: o teto de poder dobra por `maxbp` zeni
/// (`500*(maxbp/500)`), a energia ganha um ponto e enche por `1000*MaxEnergy`.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * A SIMULACAO SE DESFAZ quando cai (nocaute ou morte), quando o dono cai, sai da zona ou sai do jogo. La o
///     boneco e um inimigo comum que fica no mundo ate alguem o apagar.
///   * ELA NAO MATA NEM DECEPA (o golpe nao-letal de todo corpo sem dono): e treino.
///   * A "DURABILIDADE" E A "REGENERACAO DE NANITES" nao vieram: la sao dois numeros internos da maquina
///     (`Health`, `NanoCore`) que nenhuma outra linha do jogo le -- a armadura de verdade dela e a de toda
///     construcao (`maxarmor`), e a mensagem da nanite diz "energia" mas mexe na vida.
///   * A CARNE DE SIMULACAO (`food/Simulation_Meat`) nao existe neste port.
///   * AS MELHORIAS ACEITAM RECURSOS, pelo dobro.
/// ==================================================================================
/// </summary>
public static class Simulador
{
	public const string Tipo = "Simulator";

	public const string VerboIniciar = "sim_iniciar", VerboInfo = "sim_info", VerboMelhorar = "sim_up";
	public const string Poder = "poder", Energia = "energia";

	public const string ChaveTeto = "teto", ChaveEnergia = "energia", ChaveEnergiaMax = "energiaMax";

	/// <summary>`var/maxbp=500`.</summary>
	public const double TetoDeFabrica = 500;

	/// <summary>`Energy=1`, `MaxEnergy=1`, e `Energy-=0.1` por simulacao.</summary>
	public const double EnergiaDeFabrica = 1, EnergiaPorSimulacao = 0.1;

	/// <summary>"Simulation Power ([500*(maxbp/500)]z)": o teto dobra pelo proprio teto, em zeni.</summary>
	public static double CustoDoPoder(double teto) => 500 * (teto / 500);

	/// <summary>"Energy Expansion ([1000*MaxEnergy]z)": mais um ponto de energia, e ela enche.</summary>
	public static double CustoDaEnergia(double energiaMax) => 1000 * energiaMax;

	/// <summary>O poder de base da simulacao: o de quem pediu, ate o teto da maquina (e nunca menos de 1).</summary>
	public static double PoderDaSimulacao(double bpDoDono, double teto) => Math.Max(Math.Min(bpDoDono, teto), 1);

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X4): a simulacao nasce com o poder inteiro de quem pediu -- o teto da maquina nao vale.</summary>
	public static bool SemTetoDeTeste;
}

/// <summary>
/// O RECRIADOR -- `obj/items/Recreator` (`Code/Modules/Turfs/Building/buildable.dm:225-460`): *"Recreators use
/// resources to recreate sections of tiles. [...] It'll cost 10 zenni per turf, 100 zenni per build object. Maximum
/// 30 by 30 size."*
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// Escolhidos dois pontos (ate 30 por 30), ele grava num arquivo os tiles e os objetos construidos do retangulo, e
/// depois os recria no mesmo lugar, cobrando 10 zeni por tile e 100 por objeto.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * A PLANTA E A DOS SEUS BLOCOS ERGUIDOS (o modo de construir, tecla B): paredes, pisos e portas -- as portas
///     com a senha delas. As maquinas assentadas nao entram: cada uma tem estado proprio, e recria-las por 100 zeni
///     seria fabrica-las de graca.
///   * O QUADRADO E CENTRADO EM QUEM GRAVA (30 por 30: quinze celulas pra cada lado), e nao dois cliques.
///   * RECRIAR SO ERGUE O QUE FALTA, onde o terreno ainda aceita (o que caiu na porrada, o que foi destruido), e
///     so pra QUEM GRAVOU -- a planta guarda a conta. Os blocos recriados saem com a resistencia de AGORA (o teto
///     de tecnologia de quem recria), como todo bloco erguido.
///   * O ARQUIVO PRA BAIXAR E SUBIR nao existe: a planta mora no servidor (`tech-itens.json`).
///   * ACEITA RECURSOS, pelo dobro.
/// ==================================================================================
/// </summary>
public static class Recriador
{
	public const string Id = "Recreator";

	public const string AcaoGravar = "planta_gravar", AcaoRecriar = "planta_recriar", AcaoInfo = "planta_info";

	/// <summary>"Maximum 30 by 30 size": quantas celulas pra cada lado de quem grava.</summary>
	public const int MeioLado = 15;

	/// <summary>"It'll cost 10 zenni per turf".</summary>
	public const double CustoPorBloco = 10;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X4): o recriador ergue a planta de graca.</summary>
	public static bool DeGracaDeTeste;
}

/// <summary>Uma peca de uma planta do recriador: a celula, o bloco e a senha (so porta tem).</summary>
public sealed class PecaDaPlanta
{
	public int X, Y;
	public string Tipo = "";
	public string Senha = "";
}

/// <summary>Uma planta gravada: a zona, quem gravou e as pecas.</summary>
public sealed class Planta
{
	public byte ZonaTipo;
	public string ZonaNome = "";
	public ulong ZonaSeed;
	public string Conta = "";
	public List<PecaDaPlanta> Pecas = [];
}

/// <summary>
/// A MAQUINA DE CLONAGEM -- `obj/items/Clone_Machine` (`Code/Cloning Machine.dm`).
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// PROGRAMADA (`Program()`: *"Reset to clone [usr] if fatality occurs."*), ela vigia quem a programou: de dez em
/// dez segundos, se ele esta MORTO e a maquina tem energia, comeca uma sessao -- *"Revival in [10/Speed]
/// minutes"* -- e no fim dela ha `50/Failure` porcento de chance de falhar; se nao falha, o morto volta a vida ao
/// lado da maquina (`A.ReviveMe()`) e ela perde um ponto de energia (sao dois de fabrica). As melhorias: o tempo
/// (`1000*Speed`), a chance de falha (`1000*Failure`), a bateria (`1000*MaxEnergy`: mais um ponto, e enche) e uma
/// senha (1.000) sem a qual ninguem mais a reprograma.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * E UMA QUARTA SAIDA DA MORTE. A regra do dono (2026-09-04) e que a morte nao tem volta automatica -- as
///     saidas eram as esferas, a tecnica de reviver e o Enma. A maquina e do original (tech 100, dez milhoes de
///     zeni) e veio com o lote da tecnologia; ela nao e de graca nem certa: custa energia, demora e pode falhar.
///   * A SESSAO QUE FALHA DEIXA A MAQUINA LIVRE. La o `Reviving` nunca volta a zero depois de uma falha (nem a
///     energia e gasta): a maquina trava pra sempre. Aqui a falha GASTA a energia da sessao, e a seguinte comeca
///     na proxima vigia -- senao a chance de falha seria so atraso.
///   * O `Clone()` (um corpo vazio a partir de um recipiente de DNA) e o `Mindswap()` nao vieram: este port nao
///     tem troca de corpo. O recipiente de DNA deste port e o do Bio-Android Lab.
///   * A "DURABILIDADE" e a "ENCRIPTACAO" nao vieram: sao numeros que nada le (a armadura da maquina e a de toda
///     construcao, e nao ha quem quebre senha).
///   * AS MELHORIAS ACEITAM RECURSOS, pelo dobro.
/// ==================================================================================
/// </summary>
public static class MaquinaDeClonagem
{
	public const string Tipo = "Clone_Machine";

	public const string VerboProgramar = "clone_programar", VerboInfo = "clone_info", VerboMelhorar = "clone_up",
						VerboSenha = "clone_senha";
	public const string Tempo = "tempo", Falha = "falha", Bateria = "bateria";

	public const string ChaveEnergia = "energia", ChaveEnergiaMax = "energiaMax", ChaveVelocidade = "velocidade",
						ChaveFalha = "falha", ChaveSessaoAte = "sessaoAte", ChaveConta = "conta", ChaveNome = "nome",
						ChaveSenha = "senha";

	/// <summary>`Energy=2`, `MaxEnergy=2`.</summary>
	public const double EnergiaDeFabrica = 2;

	/// <summary>`sleep(100)`: de quanto em quanto ela vigia quem a programou.</summary>
	public const double SegundosEntreVigias = 10;

	/// <summary>`sleep(6000/Speed)`: quanto dura uma sessao de clonagem, em segundos.</summary>
	public static double SegundosDaSessao(double velocidade) => 600 / Math.Max(velocidade, 1);

	/// <summary>`prob(50/Failure)`: a chance (em porcento) de a sessao falhar.</summary>
	public static double ChanceDeFalhar(double falha) => 50 / Math.Max(falha, 1);

	public static double CustoDoTempo(double velocidade) => 1000 * velocidade;
	public static double CustoDaFalha(double falha) => 1000 * falha;
	public static double CustoDaBateria(double energiaMax) => 1000 * energiaMax;
	public const double CustoDaSenha = 1000;

	/// <summary>O teto de letras da senha.</summary>
	public const int MaxDeSenha = 16;

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X4): a maquina nunca revive ninguem -- o tanque de enfeite da v0.2.0.</summary>
	public static bool NuncaReviveDeTeste;
}

/// <summary>
/// O BARCO -- `obj/Boat` (`Code/Modules/Tech/Boat.dm`): *"Boats allow you to sail across the open waters freely."*
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// O `Sail()`, com agua no tile a frente de quem pede, o poe pra velejar: ele entra na agua com a pose de voo, o
/// barco anda embaixo dele, e pedir de novo (ou a agua acabar) o faz atracar. Velejar nao custa nada.
///
/// ============================ O QUE MUDA AQUI, DECLARADO ============================
///   * O BARCO VAI NA MOCHILA. La ele e um objeto no chao que anda junto do piloto e fica onde ele atraca; aqui as
///     construcoes nao andam, e o barco e uma ferramenta de bolso: com ele na mochila se veleja de qualquer margem,
///     e ele aparece embaixo do corpo enquanto se veleja. (Instalado no chao -- ele continua sendo uma construcao
///     do catalogo -- e um barco parado no cais: pra velejar, recolha.)
///   * VELEJAR E NADAR SEM PAGAR: a agua deixa de parar o corpo pelo mesmo modo do nado (`ModoDeTravessia.Nadando`),
///     sem o custo de Ki, sem a exaustao e sem a lentidao dele -- e, como no nado, atraca sozinho no seco e nao
///     se desliga em cima da agua (o que prenderia o corpo num lugar em que ele nao anda).
/// ==================================================================================
/// </summary>
public static class Barco
{
	public const string Id = "Boat";

	public const string AcaoVelejar = "barco_velejar";

	/// <summary>A folha do barco embaixo de quem veleja: a `Boat.dmi` do objeto.</summary>
	public const string Folha = "res://Assets/Sprites/Misc/Boat.tres";

	/// <summary>DEFEITO INJETADO (bancada `--v021teste`, familia X4): velejar cobra o Ki do nado -- o barco nao serve pra nada.</summary>
	public static bool CobraComoNadoDeTeste;
}
