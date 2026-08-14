using Jandirus.Core.World;

namespace Jandirus.Core.Tech;

/// <summary>
/// A NAVE -- OS NUMEROS, e so eles. Quem executa e o `GameServer.Nave.cs`.
///
/// ============================ O ACHADO QUE MUDOU O DESENHO ============================
/// No original o `Speed` da Spacepod (`PlanetTech.dm:51`) **nao acelera viagem nenhuma**. Ele faz
/// duas coisas, e as duas sao pequenas: divide a espera do `Launch` (`sleep(400/Speed)`, :68) e
/// multiplica o preco do proprio upgrade (`1000*Speed`, :192). Quem se move e o PILOTO, na
/// velocidade do corpo dele; o pod e um sprite que copia a posicao a cada `sleep(0.2)` (:140-144).
///
/// Ou seja: **"velocidade de nave" nao existe em lugar nenhum do DM** -- e por isso a spec pediu
/// que ela fosse construida aqui ("a nave precisa de velocidade propria -- hoje o espaco usa a
/// velocidade do corpo, e e isso que faz a viagem a Namek levar 7 dias").
///
/// O que se GANHOU: o upgrade de 39 degraus, que la so encurtava uma espera de 40 s pra 1 s, passa
/// a ser a coisa mais cara e mais util que um piloto compra. O que se PERDEU: nada mensuravel --
/// nenhuma regra do DM dependia daquele numero.
/// =====================================================================================
///
/// ============================ POR QUE ELA SUBSTITUI E NAO MULTIPLICA O CORPO ============================
/// A tentacao e `SpeedStat * Speed`. Ela **fura o mundo**: o corpo ja chega a ~5,5x de
/// <see cref="MoveRules.SpeedStatFrom"/> (Espeed satura perto de 11), e 5,5 x 39 daria 214x --
/// 34 mil px por segundo, 1.140 px por tique, quatro vezes o diametro do menor planeta. O pouso e
/// o sol sao AMOSTRAGEM POR POSICAO a 30 Hz (`Espaco.PlanetaSob`, `Sistemas.EstrelaPerto`), entao
/// acima de um certo passo por tique o corpo atravessa um planeta sem pousar nele.
///
/// Com a nave SUBSTITUINDO (o maior entre os dois, ver <see cref="FatorDoPiloto"/>), o teto e o do
/// DM (39) e ele cabe com folga no limite medido -- ver <see cref="TetoSemTunelamento"/>.
/// ======================================================================================================
/// </summary>
public static class Naves
{
	/// <summary>`var/Speed=1` (`PlanetTech.dm:51`). Toda nave nasce no degrau 1.</summary>
	public const int VelocidadeInicial = 1;

	/// <summary>
	/// O TETO DO UPGRADE -- o `Speed&lt;=39` do `verb/Upgrade` (`PlanetTech.dm:196`).
	///
	/// A guarda do DM e no MENU: com `Speed` 39 a opcao ainda aparece e ainda soma 1, entao a nave
	/// de la para em 40. Aqui o teto e o numero escrito, e a diferenca de um degrau nao vale uma
	/// divergencia -- o que importa e que ele existe e que a bancada o alcanca (regra 0.7).
	/// </summary>
	public const int VelocidadeMaxima = 39;

	/// <summary>
	/// ============================ O TETO QUE A AMOSTRAGEM IMPOE -- MEDIDO, NAO ESTIMADO ============================
	/// O pouso (<see cref="Espaco.PlanetaSob"/>) e a queimadura (<see cref="Sistemas.EstrelaPerto"/>)
	/// perguntam "o que ha SOB este ponto?" trinta vezes por segundo. Um corpo que anda mais que o
	/// DIAMETRO do alvo entre dois tiques passa por dentro dele sem que a pergunta seja feita la.
	///
	/// Medido contra os corpos deste universo (`MoveRules.BaseSpeedPx` = 160 px/s, 30 Hz):
	///
	///     fator   px/s      passo/tique   menor planeta (R=150, 300 px de diametro)
	///        11   1.760       59 px       passa
	///        39   6.240      208 px       passa (o teto do DM)
	///        56   8.960      299 px       NA BORDA
	///       100  16.000      533 px       ATRAVESSA sem pousar
	///
	/// Acima de ~56x o `PlanetaSob` teria que virar teste de SEGMENTO (o
	/// <see cref="MoveRules.PathOccupied"/> ja e exatamente esse padrao) em vez de teste de ponto.
	/// Enquanto o teto for o do DM, nada quebra -- e este numero existe pra que o dia em que alguem
	/// quiser subir o teto ele encontre a conta feita em vez de descobrir pelo bug.
	/// ==========================================================================================================
	/// </summary>
	public const int TetoSemTunelamento = 56;

	/// <summary>
	/// O PRECO DO PROXIMO DEGRAU -- `1000*Speed` (`PlanetTech.dm:192`).
	///
	/// Subir do 1 ao 39 custa 1000*(1+2+...+38) = **741.000 zeni**, sete vezes o preco do pod. E o
	/// preco certo pra uma coisa que corta a viagem a Namek de 167 minutos pra 4.
	/// </summary>
	public static double CustoDoUpgrade(int velocidade) => 1000.0 * Math.Max(velocidade, 1);

	/// <summary>
	/// A ESPERA DO LANCAMENTO -- `sleep(400/Speed)` (`PlanetTech.dm:68`), e o proprio DM escreve a
	/// unidade no chat: `"ETA [((400/Speed)/10)] second(s)"` (:66).
	///
	/// **Decimos**, e a armadilha ja registrada na memoria deste projeto: `sleep(N)` do BYOND e
	/// N/10 segundos, e nao N/`world.fps`. Sao 40 s no degrau 1 e ~1,0 s no 39.
	/// </summary>
	public static double SegundosDeLancamento(int velocidade) => 40.0 / Math.Max(velocidade, 1);

	/// <summary>
	/// O FATOR DE MOVIMENTO DESTA NAVE. E o proprio degrau, aparado pelos dois tetos.
	///
	/// Ele entra no jogo como <c>SpeedStat</c>, o MESMO campo que o corpo usa -- entao ele passa
	/// pelo `MoveRules.SpeedPx` do cliente, pelo `MoveRules.ValidateStep` do servidor e pelo
	/// `SheetState` que liga os dois. Um segundo caminho de movimento ("velocidade de veiculo")
	/// obrigaria os quatro chamadores do `SpeedPx` a lembrar de passa-lo, e esquecer nao aparece em
	/// teste -- e o mesmo argumento que o `CalorDaEstrela.Fator` ja escreveu.
	/// </summary>
	public static float Fator(int velocidade) =>
		Math.Clamp(velocidade, VelocidadeInicial, Math.Min(VelocidadeMaxima, TetoSemTunelamento));

	/// <summary>
	/// A VELOCIDADE DE QUEM ESTA PILOTANDO: a MAIOR entre o corpo e a nave.
	///
	/// Nao e "a da nave, ponto": no degrau 1 o pod do DM nao acelera nada (quem anda e o piloto), e
	/// um lutador que ja corre a 5x ficaria mais LENTO ao embarcar -- o que faria a nave de 100 mil
	/// zeni ser uma punicao ate o primeiro upgrade. Com o maior dos dois, embarcar nunca piora.
	/// </summary>
	public static float FatorDoPiloto(float doCorpo, int velocidade) => Math.Max(doCorpo, Fator(velocidade));

	/// <summary>
	/// ESTE ID DO CATALOGO E UMA NAVE? (qualquer uma das tres)
	///
	/// E o crivo do ASSENTAMENTO: quem responde `true` aqui vira uma <c>Nave</c> em vez de uma
	/// <c>Obra</c>, e ganha com isso a chave de zona inteira (tipo+nome+seed). Quem responde o QUE
	/// cada uma faz sao os dois crivos abaixo.
	///
	/// As duas primeiras entradas apontam pro mesmo `/obj/Spacepod` do `construcoes.json`: a
	/// `Spacepod` (100k, tech 40) e a `Personal_Spacepod` (150k, tech 35). Sao dois precos pro mesmo
	/// objeto no original -- o degrau de tech mais barato custa mais zeni --, e as duas viram a
	/// mesma nave.
	/// </summary>
	public static bool EhNave(string tipo) =>
		EhPod(tipo) || EhFoguete(tipo) || NaveGrande.EhNaveGrande(tipo);

	/// <summary>A Spacepod: a que se veste, se melhora e se pilota do lado de fora.</summary>
	public static bool EhPod(string tipo) => tipo is "Spacepod" or "Personal_Spacepod";

	/// <summary>
	/// O FOGUETE -- `obj/Rocketship` (`PlanetTech.dm:237`). USO UNICO, e e so isso que o separa do pod.
	/// </summary>
	public static bool EhFoguete(string tipo) => tipo == "Rocket_Ship";

	// =====================================================================
	// O FOGUETE -- os tres numeros que ele tem a mais
	// =====================================================================
	/// <summary>
	/// QUANTO TEMPO O FOGUETE FICA LA EM CIMA antes de voltar sozinho.
	///
	/// `sleep(150)` + o aviso de reentrada + `sleep(100)` (`PlanetTech.dm:276-279`): 15 s de orbita
	/// e mais 10 de descida, 25 no total. E o que faz o foguete ser o que o proprio DM diz que ele
	/// e -- *"basically just one-use pods"* --: ele nao te LEVA a lugar nenhum, ele te da uma janela
	/// de 25 segundos no espaco e te traz de volta.
	///
	/// A JANELA E O PRODUTO. Quem souber voar (ou tiver outra nave esperando) usa esses 25 segundos
	/// pra sair de orbita por conta propria; quem nao souber volta com o foguete. E por isso que ele
	/// e mais barato em TECNOLOGIA que a Spacepod (30 contra 40) e mais caro em zeni: e o degrau de
	/// quem quer chegar ao espaco antes de saber ficar nele.
	/// </summary>
	public const double SegundosEmOrbita = 25.0;

	/// <summary>Quando o aviso de reentrada sai -- `to_chat(... "Re-entry imminent")` (:277).</summary>
	public const double SegundosAteAvisarAReentrada = 15.0;

	/// <summary>
	/// RECONDICIONAR: `usr.zenni -= 100000` no `verb/Fit` (`PlanetTech.dm:325-334`) -- *"Fit the pod
	/// onto another rocket? Costs 100k Zenni"*.
	///
	/// Metade do preco do foguete inteiro (200k), e a conta so fecha assim: com o recondicionamento
	/// custando o mesmo que a construcao, ninguem recondicionaria nada.
	/// </summary>
	public const double CustoDeRecondicionar = 100_000;

	/// <summary>
	/// QUANTO TEMPO REAL leva pra cruzar <paramref name="px"/> pixels nesta nave.
	///
	/// Irma de <see cref="Espaco.SegundosDeViagem"/> e derivada dos MESMOS dois numeros: sem isto
	/// a carta estelar e a nave dariam estimativas diferentes pra mesma viagem.
	/// </summary>
	public static double SegundosDeViagem(double px, int velocidade) =>
		px / (MoveRules.BaseSpeedPx * Fator(velocidade));

	/// <summary>Quantos dias in-game a mesma viagem custa. Ver <see cref="Espaco.DiasInGame"/>.</summary>
	public static double DiasInGame(double px, int velocidade) =>
		SegundosDeViagem(px, velocidade) / Espaco.SegundosPorDiaInGame;

	/// <summary>
	/// O PASSO POR TIQUE desta nave, em pixels. E a conta que decide o tunelamento -- ver
	/// <see cref="TetoSemTunelamento"/>. Publica porque a bancada mede com ELA, e nao com uma copia.
	/// </summary>
	public static double PassoPorTique(int velocidade, double hz = 30.0) =>
		MoveRules.BaseSpeedPx * Fator(velocidade) / hz;
}
