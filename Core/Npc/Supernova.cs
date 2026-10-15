namespace Jandirus.Core.Npc;

/// <summary>
/// A SUPERNOVA (v0.3.0, item 22) -- o ultimato do Freeza na tela. O dono (2026-10-15): *"quando freeza vai explodir o
/// planeta, ele deveria fazer uma animacao parecida com a da genkidama e carregar a super nova (terceira imagem, use a
/// imagem pra fazer o efeito desse ataque, parece um sol) nesse momento ele fica imune aos ataques e joga a esfera na
/// direcao do planeta, ao andar uns tiles pra frente e explodir, comeca a destruicao do planeta"*.
///
/// OS PRAZOS SAO DAQUI, e as duas pontas os leem: o servidor segura o chefe congelado e imune ate a explosao
/// (`GameServer.ComecarASupernova`) e so entao acende a destruicao; o cliente desenha o sol crescendo e caindo
/// (`Client/SupernovaDesenhada`) pelos mesmos numeros, sem pacote novo -- o servidor so diz que o corpo esta concentrando
/// (`Ocupacao.Supernova`).
///
/// ============================ O QUE MUDOU EM RELACAO AO DM, DECLARADO ============================
/// O `cast_planet_destroy` do DM (`BossEvents.dm:780-830`) carrega 30 s com a bola do `15.dmi` sobre o chefe -- e NESSA
/// janela nocautear o chefe interrompe (`BEV_PD_CHARGE`, *"janela p/ matar/nocautear e interromper"*). A regra do dono e
/// a outra: o Freeza fica IMUNE enquanto a Supernova cresce. Sem janela pra interromper, a carga encurtou pra
/// <see cref="SegundosDeCarga"/> -- e um gesto, e nao uma espera. A queda e a do DM: a bola vira a `16.dmi`, anda
/// `walk(A, SOUTH, 3)` -- tres tiles pro sul -- e vira um buraco gigante (`:812-821`).
/// ==============================================================================================
/// </summary>
public static class Supernova
{
	/// <summary>Quanto o sol leva crescendo sobre a cabeca do chefe -- o chefe imune e parado.</summary>
	public const double SegundosDeCarga = 10;

	/// <summary>Quanto ele leva caindo os <see cref="TilesDaQueda"/> ate explodir.</summary>
	public const double SegundosDaQueda = 3;

	/// <summary>Do primeiro brilho a explosao: e quando a destruicao do planeta comeca.</summary>
	public const double SegundosAteAExplosao = SegundosDeCarga + SegundosDaQueda;

	/// <summary>Quantos tiles o sol anda "na direcao do planeta" -- o `walk(A, SOUTH, 3)` do DM.</summary>
	public const float TilesDaQueda = 3;

	/// <summary>Do quanto o sol nasce (fracao do tamanho do fim da carga): uma brasa, e nao um ponto.</summary>
	public const float TamanhoDoPrimeiroBrilho = 0.08f;

	/// <summary>
	/// O TAMANHO DO SOL NA CARGA, de <see cref="TamanhoDoPrimeiroBrilho"/> a 1 (o tamanho cheio), por segundo de carga.
	/// Cresce em linha reta e para no cheio -- a Genkidama tambem cresce sem pressa.
	/// </summary>
	public static float Crescimento(double segundosDeCarga) =>
		(float)Math.Clamp(TamanhoDoPrimeiroBrilho + (1 - TamanhoDoPrimeiroBrilho) * segundosDeCarga / SegundosDeCarga,
						  TamanhoDoPrimeiroBrilho, 1);

	/// <summary>Quanto da queda ja andou (0 a 1), por segundo desde que o sol deixou a mao.</summary>
	public static float ProgressoDaQueda(double segundosDesdeOSolto) =>
		(float)Math.Clamp(segundosDesdeOSolto / SegundosDaQueda, 0, 1);
}
