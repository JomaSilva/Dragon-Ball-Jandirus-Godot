using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// KNOCKBACK E DESTRUICAO DE CENARIO -- as duas coisas vivem juntas porque no original tambem
/// vivem: e o corpo arremessado batendo na parede que derruba a parede.
///
/// ============================ DE ONDE VEM ============================
/// `Impact.dm` decide, `/effect/knockback` (Movement Effects.dm) executa, `turf/proc/Destroy`
/// (NewTurfs.dm) derruba. As formulas estao no <see cref="Empurrao"/>, no Core; aqui fica o
/// encanamento: quem voa, por quanto tempo, o que ele encontra e quem fica sabendo.
///
/// O TIQUE DO ORIGINAL E 1 DECIMO e cada tique da DOIS passos de tile -- ou seja ~2 tiles a cada
/// 0,1 s, ate 10 tiques. E rapido de proposito: o corpo tem que SUMIR de onde estava.
/// =====================================================================
///
/// ============================ POR QUE O SERVIDOR MOVE, E NAO O CLIENTE ============================
/// Todo o resto do movimento e do cliente ("cliente calcula, servidor valida"). O arremesso nao:
/// o jogador nao esta dirigindo, esta sendo jogado. Quem calcula e quem sabe -- e por isso o
/// `Estado` ganhou um bit dizendo "voce esta voando", pra o cliente parar de integrar input e so
/// seguir as correcoes. Sem esse bit, o cliente empurraria de volta e os dois brigariam pelo corpo,
/// que e exatamente a briga que fazia o personagem TREMER na parede.
/// ==================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O GOLPE MEXEU COM O CORPO? Chamado depois de o dano ser resolvido, como no original
	/// (`attack cmn.dm:110`, logo apos o `hitProc`).
	///
	/// ============================ HAVIA DOIS CAMINHOS AQUI, E UM DELES NAO SORTEAVA ============================
	/// Ate aqui este metodo tinha um `if` pro leve (que passava pelo <c>SorteioDoSocoLeve</c> e podia
	/// sair sem empurrar nada) e um `else` seco pro pesado, que caia direto no `ForcaDoPesado` -- ou
	/// seja **todo pesado que encostava arremessava, 100% das vezes**. Era copia fiel do DM
	/// (`attack cmn.dm:115`), e o dono reclamou do resultado em jogo: *"era UM JOGANDO O OUTRO PRA
	/// LONGE e tava estranha a luta"*.
	///
	/// Agora quem decide e UM SO lugar -- <see cref="Empurrao.DoSoco"/>, no Core, ao lado da formula
	/// --, e a diferenca entre os dois golpes virou o PESO (1 ou 3), que ja e um parametro que
	/// existia. Dois ramos divergentes foi exatamente o que deixou o pesado sem sorteio por meses.
	/// ==========================================================================================================
	/// </summary>
	/// <param name="garantido">
	/// Pula o sorteio: este golpe arremessa se encostou. So pra quem ja nasceu assim -- ver
	/// `GolpeDeSaida` do Zanzo Clash.
	/// </param>
	private void TentarEmpurrar(ServerPlayer a, ServerPlayer d, double dmg, Protocol.Golpe golpe,
								bool garantido = false)
	{
		if (!a.Knockback || d.TiquesDeVoo > 0 || d.Ficha.dead) return;

		// ============================ O INTERRUPTOR DO DEFEITO INJETADO ============================
		// FALSO EM JOGO, SEMPRE -- so a `--kbteste` o liga, e o que ele reproduz e EXATAMENTE a linha
		// que existia: `attack cmn.dm:115-116`, o `else` seco que mandava todo pesado pro `Impact` sem
		// `prob()` nenhum. Com ele ligado o sorteio do pesado devolve sempre "sim", que e a copia
		// verbatim daquele `else`; o LEVE nao e tocado, porque o leve nunca mudou.
		//
		// Ele mora AQUI e nao dentro do arquivo de bancada pela mesma razao escrita no
		// `_borraoSoComSkill` (`GameServer.Combat.cs`) e no `_dcGradeCega` (`GameServer.Corpos.cs`): a
		// medicao "antes x depois" exige que o MESMO objeto seja medido com e sem o defeito. Uma copia
		// do funil escrita na bancada mediria a copia concordando consigo mesma -- e este projeto ja
		// catalogou esse cego por escrito ("a bancada mede INTENCAO").
		//
		// E ele e o que torna a queixa do dono MENSURAVEL em vez de lembrada: as familias 8 e 9 brigam
		// 60 segundos com ele ligado, 60 com ele desligado, e a diferenca entre as duas e a resposta.
		// ==========================================================================================
		Func<double, bool> sorteio = _kbPesadoSemSorteio && golpe == Protocol.Golpe.Pesado
			? _ => true
			: p => _rng.NextDouble() * 100 < p;

		(EfeitoDeImpacto efeito, int tiques) = Empurrao.DoSoco(
			dmg, Protocol.PesoDoGolpe(golpe), a.Ficha, d.Ficha, sorteio, garantido);

		switch (efeito)
		{
			case EfeitoDeImpacto.Arremesso:
				Arremessar(d, MeleeArea.Frente(a.Facing), a.Ficha.expressedBP, tiques);
				break;

			// CAMBALEIA E LENTO ainda nao tem efeito proprio no port (nao ha `slowed`/`stagger` na
			// ficha), entao viram atordoamento curto -- que e o que o CombatState ja sabe fazer e o
			// que o jogador sente igual: perder o proximo golpe. Anotado como aproximacao.
			case EfeitoDeImpacto.Cambaleia:
				d.Combate.Stun = Math.Max(d.Combate.Stun, Empurrao.TiquesDeTropeco * Empurrao.SegundosPorTique);
				break;
		}
	}

	/// <summary>
	/// O DEFEITO QUE A `--kbteste` INJETA: "todo pesado que encosta arremessa", o `else` de
	/// `attack cmn.dm:115`. Ver o bloco no <see cref="TentarEmpurrar"/> -- e falso em jogo.
	/// </summary>
	private bool _kbPesadoSemSorteio;

	/// <summary>
	/// QUANTOS CORPOS ESTE SERVIDOR JA JOGOU PRO AR desde o boot -- um `++` por passagem pelo
	/// <see cref="Arremessar"/>, que e a UNICA porta do arremesso (o comentario dele explica por que
	/// ela e unica).
	///
	/// ============================ POR QUE UM CONTADOR, E NAO OLHAR O CORPO ============================
	/// A `--kbteste` contava arremesso pela subida de `TiquesDeVoo` de zero pra positivo, e **perdia
	/// silenciosamente uma classe inteira de arremesso**: o voo pode COMECAR E ACABAR no mesmo tique.
	/// O `TickDoEmpurrao` roda logo depois da IA no mesmo tique de mundo, e se a primeira amostra do
	/// caminho ja esbarra em parede ou noutro corpo ele escreve `TiquesDeVoo = 0` antes de qualquer
	/// bancada olhar. Numa briga em corredor isso e a MAIORIA dos arremessos -- a medicao deu zero com
	/// o defeito ligado, que e o oposto do que a queixa do dono descreve.
	///
	/// E o mesmo desenho (e o mesmo argumento) do <see cref="_decisoesDaMente"/>: quando a pergunta e
	/// "quantas vezes isto ACONTECEU", quem responde e a passagem pela funcao, e nao um efeito colateral
	/// dela que outra parte do tique ja pode ter desfeito. Custa um `long++` por arremesso.
	/// ============================================================================================
	/// </summary>
	private long _arremessosFeitos;

	/// <summary>
	/// PoE O CORPO NO AR -- o `AddEffect(/effect/knockback)` do original, e a UNICA porta pra isso.
	///
	/// ============================ POR QUE ISTO VIROU METODO ============================
	/// Ate o lote G6 este bloco morava dentro do `case Arremesso` do <see cref="TentarEmpurrar"/>,
	/// e era a unica coisa no jogo que arremessava alguem -- entao ele nao precisava de nome. O
	/// sopro (`Kiai`, `Shockwave`, `Explosive_Roar`) arremessa SEM golpe nenhum: o `KiKnockback`
	/// do DM (`KiStatsModule.dm:42`) escreve `kbpow`/`kbdur`/`kbdir` na mao e liga o mesmo efeito.
	///
	/// Copiar as sete linhas seria a segunda resposta pra "como um corpo comeca a voar", e as duas
	/// divergiriam no primeiro dia em que alguem mexesse numa -- comecando pelo `MarcarSulco` e
	/// pelo `MandarFicha`, que sao justamente os dois que ninguem lembra de repetir (e cuja falta
	/// ja produziu o "voa travado e rotacionado errado" que este arquivo conta em detalhe).
	/// ==================================================================================
	/// </summary>
	/// <param name="forca">
	/// O `kbpow`: o `expressedBP` de QUEM empurrou. Nao e o dano -- e ele que decide se a parede no
	/// caminho cai ou se o voo para nela.
	/// </param>
	/// <param name="tiques">O `kbdur`, em tiques de 0,1 s. O efeito ja aplica o teto de 10.</param>
	private void Arremessar(ServerPlayer d, Vec2 rumo, double forca, int tiques)
	{
		tiques = Math.Clamp(tiques, 1, Empurrao.TiquesMax);
		_arremessosFeitos++;

		// O ARREMESSO GANHA DO ARRASTO, e a precedencia e escrita e nao emergente.
		//
		// O caso: um feixe carrega alguem e um SEGUNDO golpe (outro raio, um soco, um sopro) o
		// arremessa no mesmo instante. Sem esta linha o corpo ficaria com os dois estados de pe, e o
		// laco abaixo -- que trata o arrasto primeiro e da `continue` -- seguraria o voo por ate um
		// decimo de segundo antes de o prazo escorrer. Nada quebraria, e por isso mesmo ninguem
		// descobriria: seria um arremesso que as vezes comeca tarde.
		//
		// O feixe se solta sozinho no tique seguinte -- `PodeSerLevadoPeloFeixe` recusa quem tem
		// `TiquesDeVoo > 0` e o `ArrastarComOFeixe` zera o `Arrastando` dele ali mesmo. Ou seja a
		// regra e dita UMA vez, aqui, e o outro lado so obedece.
		d.ArrastoRestante = 0;

		// E O ARREMESSO GANHA DO PUXAO DA FUSAO PELA MESMA RAZAO, e com a mesma precedencia ESCRITA:
		// quem levou um golpe forte no meio de uma Potara esta voando, e nao sendo atraido. O
		// `TickDoPuxaoDeFusao` nao o move enquanto ele estiver no ar (ver `AndarNoPuxao`) e o detector
		// de "pararam de se aproximar" desfaz a fusao sozinho se ele nao voltar -- que e o *"se um dos
		// dois nao chegar, a fusao NAO comeca"* do pedido, sem uma segunda regra sobre arremesso.
		d.PuxaoDeFusaoRestante = 0;

		// ============================ E O RAIO NA MAO DELE CAI JUNTO ============================
		// `while(beaming) { CHECK_TICK; if(KB) stopbeaming() }` -- o topo do laco do `ShootBeam`
		// (`beams.dm:72-74`). O `KB` do original e escrito em dois lugares (`Throw.dm:84` e o
		// `/effect/knockback` de `Movement Effects.dm`) e os dois viram ESTE metodo neste port, entao
		// esta linha e o porte inteiro daquela regra.
		//
		// FICA AQUI PELA MESMA RAZAO QUE O `ArrastoRestante` de cima: o arremesso e o instante em que
		// se decide o que este corpo deixa de estar fazendo, e a precedencia e escrita e nao
		// emergente. Escrever isso dentro do `TickDosCanaisDeKi` ("o dono esta voando? entao cai")
		// faria a pergunta 30 vezes por segundo pra responder nao, e deixaria o raio (e a pose) vivos
		// pelo tique em que o golpe acerta -- que e justamente o quadro que o dono quer ver mudando.
		//
		// E ELA FECHA METADE DO PEDIDO: *"ele so voltaria a posicao de IDLE quando ele PARASSE DE USAR
		// O BEAM (por vontade propria ou pq ALGUEM BATEU NELE e cancelou o beam)"*. Ate aqui o port
		// nao tinha a segunda metade -- nao havia caminho nenhum pra bater e derrubar o raio.
		// ====================================================================================
		DerrubarRaioPorGolpe(d.Id);

		d.TiquesDeVoo = tiques;
		d.TiquesIniciaisDoVoo = tiques;
		d.RumoDoVoo = rumo;
		d.ForcaDoVoo = forca;
		d.VooNoTique = 0;
		d.UltimoSulco = default;
		// A PONTA DE COMECO sai aqui, e nao no primeiro tique: no DU o "begin" e carimbado
		// quando `knock_dist == original_distance-1`, ou seja no primeiro passo, com o corpo
		// ainda na origem.
		MarcarSulco(d, Protocol.Decal.SulcoPonta);

		// A FICHA SAI AGORA, e nao no proximo tique de 5 Hz.
		//
		// O bit "estou voando" (e a direcao do corpo) so viaja no pacote de ficha, e ficha sai
		// no `TickFichas`, que roda 1 vez a cada 6 tiques. O voo comeca no tique CHEIO e a
		// primeira correcao sai ~33 ms depois -- ou seja, por ate 200 ms o cliente ainda nao
		// sabia que estava voando: nao girava o corpo E teleportava 64 px por correcao em vez
		// de deslizar. Como o voo mais curto tem 300 ms, ate dois tercos dele aconteciam em pe
		// e aos saltos. Era esse o resto de "voa travado" e "rotacionado errado".
		//
		// O canal e ReliableOrdered, entao mandar aqui poe a ficha NA FRENTE da primeira
		// correcao -- quando ela chega, o cliente ja esta no modo de voo.
		MandarFicha(d);
	}

	/// <summary>
	/// O VOO. Roda no tick cheio e so mexe em quem esta no ar.
	///
	/// ============================ O TIQUE DO DM E GROSSO DEMAIS PRA DESENHAR ============================
	/// O original anda em passos de 0,1 s valendo DOIS TILES cada, e era assim que isto estava: o
	/// corpo ficava 100 ms exatamente parado e entao pulava 64 px de uma vez. Quem assiste recebe
	/// snapshot a 30 Hz, entao via dois quadros de corpo imovel e um de corpo deslizando 64 px --
	/// dispara-para-dispara. Era o "quando toma knock back voa sem fluidez", e nao tinha conserto
	/// possivel no cliente: a interpolacao estava reproduzindo com fidelidade um movimento que de
	/// fato acontecia aos solavancos.
	///
	/// Agora o passo e FATIADO pelo tique do servidor -- um terco dele por vez, ~21 px a 30 Hz.
	/// Distancia e duracao totais nao mudam (tres fatias fecham os mesmos dois tiles no mesmo
	/// decimo de segundo), e `TiquesDeVoo` continua contando na moeda do DM, que e o que o dano do
	/// baque le. O que muda e so que o corpo atravessa o caminho em vez de aparecer no fim dele.
	///
	/// A COLISAO SO MELHOROU: o caminho continua sendo varrido de meio tile em meio tile pra nao
	/// pular parede, e agora isso acontece tres vezes por tique do original em vez de uma.
	/// ======================================================================================================
	/// </summary>
	private void TickDoEmpurrao()
	{
		long agora = NowMs();

		// Que fracao do tique do original cabe num tique do servidor. Com 30 Hz e 0,1 s, um terco.
		double fatia = Protocol.TickSeconds / Empurrao.SegundosPorTique;

		// ============================ A FONTE E TODO CORPO DO MUNDO, E NAO O `_players` ============================
		// **ISTO ERA UM DEFEITO ANTES DO CADAVER EXISTIR.** Este laco varria `_players.Values`, que quer
		// dizer "corpo que o servidor SIMULA" -- e nem todo corpo que existe num lugar e simulado. O
		// boneco do corpo largado (quem esta meditando ou ao leme) vive so na `ZoneList`, e o resultado
		// medido era o oposto do que o `GameServer.CorpoLargado.cs` afirma por escrito (*"o boneco e um
		// corpo comum, entao um soco o empurra"*): ele recebia `TiquesDeVoo` do `Arremessar` e ficava
		// com eles PRA SEMPRE -- nunca andava, e ficava desenhado deitado (por `Deitado`) ate o dono
		// voltar pro corpo.
		//
		// Com a fonte certa, o boneco voa, e o CADAVER voa junto -- sem uma linha que saiba o que um
		// cadaver e. Ver `TodosOsCorpos` em `GameServer.Corpos.cs`, que e a mesma fonte da grade de
		// colisao e pelo mesmo motivo.
		// ========================================================================================================
		foreach (ServerPlayer pl in TodosOsCorpos())
		{
			// ============================ O OUTRO JEITO DE O SERVIDOR DIRIGIR UM CORPO ============================
			// O feixe que CARREGA a vitima (ver `GameServer.Projeteis.ArrastarComOFeixe`) escreve a
			// posicao dela no tique dos projeteis, que roda DEPOIS deste. O que falta e o resto do
			// pacote -- escorrer o prazo, mandar a correcao e devolver as redeas -- e ele e o MESMO de
			// quem esta sendo arremessado, linha por linha. Por isso mora aqui e nao la: um segundo
			// lugar mandando correcao de posicao seria a segunda resposta pra "o servidor esta me
			// movendo", e este arquivo inteiro existe por causa da primeira vez que houve duas.
			//
			// O QUE NAO SE REUSOU e o deslocamento, e nao havia como: o arremesso anda `TilesPorTique`
			// (dois tiles a cada 0,1 s = 20 tiles/s, o numero do `/effect/knockback`), e o arrasto tem
			// que andar EXATAMENTE o que a cabeca do feixe andou -- que e 10 tiles/s num raio de
			// `speed` 1 e 5 tiles/s num de 0,5. Empurrar a vitima pelo funil do arremesso a faria sair
			// da frente do proprio feixe a duas a quatro vezes a velocidade dele, e o raio nunca mais a
			// alcancaria: o pedido do dono (*"conforme o beam vai indo"*) e literalmente irrealizavel
			// por esse caminho. Mexer no `TilesPorTique` pra encaixar mudaria TODO knockback do jogo.
			//
			// OS DOIS NUNCA VALEM JUNTOS: `PodeSerLevadoPeloFeixe` recusa quem esta com
			// `TiquesDeVoo > 0`, e este `continue` garante o outro lado. Nao ha tique em que os dois
			// escrevam `Pos`.
			// ======================================================================================================
			// ============================ E O PUXAO DA FUSAO ENTRA POR ESTA MESMA PORTA ============================
			// O `Potara_Fusion.dm:124-129` anda com os DOIS corpos um pro outro antes de fundir, e quem
			// escreve o `Pos` deles e o `GameServer.TickDoPuxaoDeFusao`. O que falta -- escorrer o prazo,
			// mandar a correcao e devolver as redeas -- e literalmente o mesmo pacote do feixe, linha por
			// linha, e por isso ele NAO foi copiado pra la: este arquivo inteiro existe por causa da
			// primeira vez que houve dois lugares mandando correcao de posicao.
			//
			// OS DOIS PRAZOS ESCORREM SEPARADOS e a soltura e a CONJUNCAO: os dois nunca valem juntos em
			// jogo (quem esta sendo puxado esta preso pela fusao, e o `Arremessar` zera o arrasto), mas
			// zerar um por causa do outro seria a precedencia emergente que o `Arremessar` documenta.
			// ==================================================================================================
			if (pl.ArrastoRestante > 0 || pl.PuxaoDeFusaoRestante > 0)
			{
				if (pl.ArrastoRestante > 0)
					pl.ArrastoRestante = Math.Max(0, pl.ArrastoRestante - Protocol.TickSeconds);
				if (pl.PuxaoDeFusaoRestante > 0)
					pl.PuxaoDeFusaoRestante = Math.Max(0, pl.PuxaoDeFusaoRestante - Protocol.TickSeconds);

				bool soltou = pl.ArrastoRestante <= 0 && pl.PuxaoDeFusaoRestante <= 0;

				// A MESMA COSTURA DA CORRECAO do arremesso, e pelas mesmas razoes -- o carimbo de
				// sequencia e o que impede os pacotes de input ja no ar (com a posicao antiga) de serem
				// lidos como cliente errado e puxarem o corpo de volta.
				pl.LastInputMs = agora;
				pl.CorrecaoEsperadaAte = agora + 500;
				pl.SeqDoTeleporte = pl.SeqInput;
				pl.OrcamentoPx = 0;

				var cw = Protocol.Begin(Protocol.S2C.Correction);
				cw.Put(pl.SeqInput);
				cw.PutVec(pl.Pos);
				pl.Peer?.Send(cw, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

				// SOLTOU: a ficha leva o bit `Empurrado` apagado -- sem ela o cliente continuaria
				// deslizando pra a ultima correcao em vez de voltar a obedecer a tecla.
				if (soltou) MandarFicha(pl);
				continue;
			}

			if (pl.TiquesDeVoo <= 0) continue;

			ZoneCollision? mapa = _catalogo?.Get(pl.Zone)?.Mapa;
			Vec2 passo = pl.RumoDoVoo * (float)(Empurrao.TilesPorTique * ZoneCollision.TileSize * fatia);
			Vec2 destino = pl.Pos + passo;

			// ---- o que tem no caminho ----
			bool parou = false;

			// ============================ O CORPO NO CAMINHO -- AGORA PELO MESMO FUNIL DA PAREDE ============================
			// **O QUE ESTAVA AQUI ERA UMA SEGUNDA NOCAO DE COLISAO, E ELA ERRAVA DE TRES JEITOS.** Havia
			// um `foreach` varrendo a `ZoneList` inteira -- O(n) por tique de arremesso -- que perguntava
			// `(o.Pos - destino).LengthSquared > 32*32`, ou seja um CIRCULO DE 32 px em volta do ponto de
			// CHEGADA. Os tres defeitos:
			//
			//   1. **CAIXA PROPRIA.** Um raio de 32 px nao e a caixa dos pes que o resto do jogo usa: havia
			//      pontos em que a parede deixava caber um corpo e o arremesso dizia que nao (e vice-versa).
			//   2. **SO O PONTO DE CHEGADA.** O passo do tique pode andar mais que a caixa, e quem estivesse
			//      no MEIO do salto nao era encontrado -- o mesmo defeito que a parede ja teve ("algumas
			//      paredes q ele passa n quebram") e que a varredura de meio tile consertou pra ela.
			//   3. **`o.Ficha.dead` PULAVA O CADAVER.** Ou seja: um corpo jogado atravessava um corpo morto.
			//      Isso contradiz literalmente o pedido 3 do dono (*"o corpo mesmo morto TEM TODAS AS
			//      INTERACOES DE UM CORPO VIVO"*) e era o unico `if (dead)` que ainda restava neste caminho.
			//
			// Agora a pergunta e a MESMA do passo a pe: `Vizinhanca.Barra`, com a caixa dos pes, o andar e o
			// `ModoDeTravessia.Arremessado` -- que **bloqueia**, porque o `mob/Cross` do DM so abre pra
			// `flying` (ver `ClasseDeCorpo.Bloqueia`). E ela e feita AMOSTRA POR AMOSTRA junto com a parede,
			// logo abaixo, e nao aqui em cima.
			//
			// O DANO CONTINUA SENDO DOS DOIS, e ele e literal (`Movement Effects.dm:77-81`):
			//     for(var/mob/M in get_step(target,dir))
			//         if(M&&M!=target)
			//             M.SpreadDamage(duration,0)      // quem levou a topada
			//             target.SpreadDamage(duration,0) // quem vinha voando
			//             duration=0                      // ...e o voo ACABA aqui
			// A dose e `duration` = **o que FALTAVA voar**, entao bater no comeco do arremesso doi mais que
			// bater no fim -- o corpo ainda tinha inercia. Nao inventei numero nenhum: o `Espalhar` e o
			// `SpreadDamage`, e o `duration` e o `TiquesDeVoo`.
			//
			// **E O ARREMESSO PARA** (o `duration=0`). Nao ricocheteia e nao continua: o corpo se arrebenta
			// em quem acertou e cai ali. E o mesmo desfecho da parede que resiste, tres linhas abaixo, e o
			// mesmo que faz nascer a cratera e a fumaca no fim do voo.
			// ==========================================================================================================

			// ============================ TODA PAREDE DO CAMINHO, NAO SO A DO FIM ============================
			// Quando o passo andava dois tiles de uma vez, testar so a celula de DESTINO deixava a
			// parede do MEIO do salto ser atravessada -- o dono viu exatamente isso, "algumas paredes
			// q ele passa n quebram". Por isso o passo e caminhado de meio tile em meio tile.
			//
			// A VARREDURA CONTINUA VALENDO com o passo fatiado, e so ficou mais fina: a fatia mede
			// 21 px contra os 16 do meio tile, entao sao duas amostras por tique do servidor e seis
			// por tique do original -- onde antes eram quatro.
			//
			// Cada parede encontrada e derrubada (se a forca vence) e o voo continua; a primeira que
			// RESISTE para o corpo ali, no ultimo ponto livre -- a mesma regra do `Ticked` do original.
			// ================================================================================================
			// A CONDICAO DEIXOU DE PEDIR MAPA. Este laco agora responde por DUAS coisas -- a parede e o
			// corpo alheio -- e so a primeira precisa de `.col`. Sem esta mudanca, arremessar alguem numa
			// zona sem colisao carregada (o espaco, um planeta ainda gerando) atravessaria gente.
			{
				const float Amostra = ZoneCollision.TileSize / 2f;
				int passos = Math.Max(1, (int)MathF.Ceiling(passo.Length / Amostra));
				Vec2 andado = pl.Pos;
				Vizinhanca vizinhos = VizinhancaDe(pl);

				for (int i = 1; i <= passos && !parou; i++)
				{
					Vec2 p = pl.Pos + passo * (i / (float)passos);

					// ============================ O CORPO NO CAMINHO -- os dois se machucam, e o voo ACABA ============================
					// A pergunta e a do passo a pe (ver o bloco grande la em cima): mesma caixa, mesmo
					// andar, mesmo `Vizinhanca`. O `pl.Pos` como "ja sobrepondo" e o que impede o corpo de
					// bater em quem ele JA estava tocando quando o arremesso comecou -- caso comum, porque o
					// arremesso do agarrao nasce colado e o soco que arremessa nasce a 32-40 px.
					// ==========================================================================================================
					if (vizinhos.Barra(pl.Pos, p, ModoDeTravessia.Arremessado) is int quem and > 0)
					{
						// `CorpoNaMinhaZona` e nao `_players[...]`: o boneco do corpo largado esta na
						// `ZoneList` (e por isso na grade) e NAO no `_players` -- ver o comentario da
						// fonte em `GameServer.Corpos.MontarAsGrades`. Buscar pelo dicionario faria o
						// corpo em transe parar o arremesso e nao levar dano nenhum.
						if (CorpoNaMinhaZona(pl, quem) is { } batido)
						{
							Espalhar(batido, pl.TiquesDeVoo);
							AvisarSePessoa(batido, $"{pl.Name} colide com você!");
						}
						Espalhar(pl, pl.TiquesDeVoo);
						parou = true;
						break;   // para no ultimo ponto livre, como na parede que resiste
					}

					// O QUE O CORPO ATRAVESSA TAMBEM SOFRE. E o `for(var/obj/O in get_step(...))
					// if(O.fragile) O.takeDamage(pow)` do original: a arvore e a bancada nao
					// precisam BLOQUEAR pra serem arrancadas por alguem passando voando por cima.
					EstragarObrasNoCaminho(pl, p);

					// ============================ QUEM E ARREMESSADO ATRAVESSA A AGUA ============================
					// E literal do original: o `testWaters()` deixa passar `M.KB`
					// (`Swim.dm:31`) junto com quem voa e quem nada. Faz sentido em jogo -- o corpo
					// esta no ar, nao esta pisando --, e sem o `modo` aqui um soco que jogasse
					// alguem pro lago o pararia na beirada como se a agua fosse muro, cortando o
					// arremesso no meio sem nada explicando por que.
					//
					// PAREDE CONTINUA PARANDO (e sendo derrubada logo abaixo): o modo muda a
					// resposta da AGUA e so dela. Ver `ClasseDeAgua.Bloqueia`.
					if (mapa == null || !MoveRules.Occupied(mapa, p, ModoDeTravessia.Arremessado))
					{ andado = p; continue; }

					if (pl.ForcaDoVoo >= Empurrao.ResistenciaPadrao && DerrubarCenario(pl.Zone, p))
					{
						andado = p;   // a parede caiu: o corpo passa por cima do escombro
						continue;
					}

					// RESISTIU: o corpo se arrebenta nela e o voo acaba no ultimo ponto livre.
					Espalhar(pl, pl.TiquesDeVoo);
					parou = true;
				}
				destino = andado;
			}

			pl.Pos = destino;

			// ============================ UM SULCO POR TILE, E NAO POR TIQUE ============================
			// A primeira versao carimbava no fim de cada tique do DM. So que o tique do DM anda DOIS
			// tiles (`Empurrao.TilesPorTique`), e o DU carimba dentro do laco que anda UM
			// (`step(src,knock_dir,32)` -- death.dm:216-224). Resultado: uma marca a cada 64 px, com
			// um buraco de um tile entre elas. O dono fotografou: "o rastro ta vindo picotado e n
			// continuo".
			//
			// Aqui a chamada e por FATIA (~21 px) e quem faz a conta certa e a guarda de celula que
			// ja existia dentro do `MarcarSulco`: ela deixa passar a primeira fatia de cada celula e
			// recusa as seguintes. Uma marca por tile, sem buraco e sem sobreposicao.
			// ==========================================================================================
			if (!parou && pl.TiquesDeVoo > 0) MarcarSulco(pl, Protocol.Decal.Sulco);

			// O RELOGIO DO DM SO VIRA QUANDO AS FATIAS FECHAM UM TIQUE INTEIRO. `TiquesDeVoo` e a
			// moeda do original -- e dela que sai o dano do baque (`Espalhar`) e a duracao do voo --
			// entao ela continua contando de 0,1 em 0,1 s, por mais que o corpo ande tres vezes
			// nesse intervalo. A folga no teste absorve o erro de ponto flutuante das tres somas.
			if (parou) { pl.TiquesDeVoo = 0; pl.VooNoTique = 0; }
			else
			{
				pl.VooNoTique += fatia;
				if (pl.VooNoTique >= 1 - 1e-6)
				{
					pl.VooNoTique -= 1;
					pl.TiquesDeVoo--;
				}
			}

			// A OUTRA BORDA: o pouso. Mesma razao da decolagem -- sem isto o corpo ficava ate 200 ms
			// torto e sem aceitar comando DEPOIS de ja ter parado.
			bool pousou = pl.TiquesDeVoo <= 0;

			// ============================ O FIM DO ARRASTO ============================
			// No DU sao duas coisas no mesmo instante: a ULTIMA marca do sulco vira "begin" de novo
			// (`knock_dist==0`), e onde o corpo para nasce uma CRATERA (`obj/Crater`, que cresce de
			// 1% ate o tamanho cheio). A fumaca nao vem do DU -- e o pedido do dono, e nasce junto
			// com a cratera porque e ela que a levanta.
			//
			// Vale pros DOIS fins: parar numa parede e chegar ao fim da distancia. E o que o dono
			// pediu -- "no final (ou se alguma parede parasse o jogador/npc voando)".
			// ==========================================================================
			if (pousou)
			{
				// ============================ UM CORPO SEM DONO NAO SABE NADAR ============================
				// O arremesso atravessa agua (`ModoDeTravessia.Arremessado`, o `M.KB` do `testWaters`) e
				// pode terminar EM CIMA dela. Um JOGADOR resolve isso sozinho -- liga o nado --, e desde
				// que o `MoveRules.Escapar` deixou de aprovar todo passo de quem esta numa celula
				// invalida, ficar ali ate recarregar o Ki e exatamente o que o dono pediu.
				//
				// UM CORPO DIRIGIDO (`Peer == null`: NPC, cidadao, chefe, clone) NAO TEM VERB NENHUM PRA
				// CHAMAR. Sem esta guarda ele congelaria no meio do lago pra sempre -- e calado, que e o
				// jeito que este projeto ja perdeu uma IA inteira antes.
				//
				// `PontoLivrePerto` e o funil que o resto do jogo usa pra "poe um corpo num lugar
				// valido": ele recusa parede, beirada E agua (`ZoneCollision.ServeDeChao`). Fica ANTES
				// da cratera pra a marca nascer onde o corpo de fato parou.
				if (pl.Peer == null && MapaDaZonaOuCatalogo(pl.Zone) is { } chaoFinal
					&& MoveRules.Occupied(chaoFinal, pl.Pos, ModoDeTravessiaDe(pl)))
					pl.Pos = chaoFinal.PontoLivrePerto(pl.Pos);

				MarcarSulco(pl, Protocol.Decal.SulcoPonta);
				if (RastroVale(pl))
				{
					MandarDecalque(pl.Zone, Protocol.Decal.Cratera, pl.Pos, pl.Facing);
					MandarDecalque(pl.Zone, Protocol.Decal.Fumaca, pl.Pos, pl.Facing);
				}
			}

			// O CLIENTE PRECISA SABER ONDE ELE ESTA, e com a sequencia carimbada -- senao os pacotes
			// que ele ja mandou (da posicao antiga) seriam tratados como cliente errado e puxariam o
			// corpo de volta. Mesma armadilha do dash.
			pl.LastInputMs = agora;
			pl.CorrecaoEsperadaAte = agora + 500;
			pl.SeqDoTeleporte = pl.SeqInput;
			pl.OrcamentoPx = 0;

			var w = Protocol.Begin(Protocol.S2C.Correction);
			w.Put(pl.SeqInput);
			w.PutVec(pl.Pos);
			pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
			if (pousou) MandarFicha(pl);   // a posicao final vai ANTES do "acabou"
		}
	}

	/// <summary>
	/// ESTE ARREMESSO DEIXA RASTRO NO CHAO?
	///
	/// ============================ AS REGRAS SAO DO DU, MENOS UMA ============================
	/// `death.dm:218` pede tres coisas: a flag `dirt_trail`, distancia total >= 8 tiles, e direcao
	/// CARDEAL.
	///
	/// A terceira NAO VIRA CODIGO AQUI, e isso e de proposito: `RumoDoVoo` sai de
	/// `MeleeArea.Frente(a.Facing)`, e `Facing` so tem quatro valores (as quatro do BYOND). Um
	/// `if` de direcao cardeal seria uma guarda que nunca falha -- codigo morto vestido de regra.
	///
	/// A `dirt_trail` do DU e um parametro por chamada (o `Stat Loop` passa 0 num caso); aqui ela
	/// virou "esta no CHAO", que e o que o dono pediu -- quem esta voando nao ara a terra.
	/// ======================================================================================
	///
	/// ============================ E NO ESPACO NAO HA CHAO PRA ARAR ============================
	/// A guarda de altura NAO cobria o vacuo: `pl.Altitude` e zero no espaco (a altitude e um
	/// sistema de planeta -- `TentarRomperAAtmosfera` devolve na porta quando a zona e o espaco), e
	/// `TiquesIniciaisDoVoo` passa de 4 em qualquer arremesso decente. Resultado: arremessar alguem
	/// no vacuo carimbava SULCO DE TERRA e CRATERA no nada, e o pacote ia pro `ZoneList` do espaco,
	/// que e o universo INTEIRO -- todo mundo online via a cratera, a qualquer distancia.
	///
	/// Ficou invisivel enquanto ninguem brigava no espaco. Jogar alguem no sol e exatamente brigar
	/// no espaco, entao a Fase 4 e quem paga esta conta.
	///
	/// A pergunta certa nao e "estou no chao": e "existe chao". `Espaco.EhPlaneta` e quem responde
	/// isso, e ja e o mesmo criterio que a volta da borda e a musica de transformacao usam.
	/// ======================================================================================
	/// </summary>
	private static bool RastroVale(ServerPlayer pl)
		=> pl.Altitude <= 0f
		   && Espaco.EhPlaneta(pl.Zone)
		   && pl.TiquesIniciaisDoVoo * Empurrao.TilesPorTique >= TilesParaDeixarRastro;

	/// <summary>Distancia minima do arremesso pra ele arar o chao. `death.dm:218` -- oito tiles.</summary>
	private const int TilesParaDeixarRastro = 8;

	/// <summary>
	/// CARIMBA UM SULCO onde o corpo esta, se o arremesso merecer rastro.
	///
	/// A guarda de celula repetida existe porque as tres fatias de um tique podem cair na mesma
	/// celula quando o corpo bate em algo e anda pouco -- e duas marcas sobrepostas leem como uma
	/// mancha, nao como pegada.
	/// </summary>
	private void MarcarSulco(ServerPlayer pl, Protocol.Decal tipo)
	{
		if (!RastroVale(pl)) return;

		// ============================ A CELULA E A DOS PES, NAO A DO CENTRO ============================
		// (a conta em si mora no <see cref="CarimbarSulco"/>, que o raio tambem usa -- ver la.)
		// `pl.Pos` e o CENTRO do corpo; quem encosta no chao sao os pes, 8 px abaixo
		// (`MoveRules.FeetOffsetY` -- a mesma ancora que a colisao e a sombra de voo ja usam).
		//
		// Escolher a celula pelo centro punha o sulco uma fileira acima do arrasto de verdade, e como
		// a marca e alinhada a grade esse erro nao se dissolve: ele aparece como um rastro deslocado
		// do personagem. O dono viu: "as vezes fica um pouco torto com a posiçao do personagem".
		//
		// So a ESCOLHA DA CELULA muda -- o alinhamento a grade (que foi o que fechou os buracos)
		// continua igual.
		// ==============================================================================================
		Vec2 pes = pl.Pos + new Vec2(0, MoveRules.FeetOffsetY);

		// A DIRECAO E A DO ARREMESSO, e nao pra onde o corpo olha: o sulco e a marca do arrasto.
		CarimbarSulco(pl.Zone, tipo, pes, MoveRules.FacingFrom(pl.RumoDoVoo, pl.Facing),
					  ref pl.UltimoSulco);
	}

	/// <summary>
	/// UMA MARCA DE ARRASTO NUMA CELULA -- a guarda de celula repetida, o alinhamento a grade e o
	/// envio, num lugar so.
	///
	/// ============================ POR QUE ISTO VIROU METODO ============================
	/// Ate aqui o unico que arava o chao era o CORPO arremessado. O raio passou a arar tambem (pedido
	/// do dono: *"ataques de ki como BEAM deveriam criar um RASTRO NO CHAO igual o knock back por onde
	/// passam"*), e ele NAO e um <see cref="ServerPlayer"/>: nao tem pes, nao tem `Facing`, nao tem
	/// `RumoDoVoo`. Copiar as dez linhas pro lado dos projeteis criaria o SEGUNDO mecanismo de rastro,
	/// e as duas copias divergiriam na primeira vez que alguem mexesse no alinhamento -- que e
	/// justamente a linha que o dono ja mandou consertar duas vezes ("o rastro ta vindo picotado").
	///
	/// O que e de cada um fica com cada um: o corpo entra com a celula dos PES e a direcao do voo; o
	/// raio entra com a cabeca dele e o proprio rumo. O que e comum -- uma marca por celula, no
	/// CENTRO dela, pra zona inteira -- e isto aqui.
	/// ==================================================================================
	/// </summary>
	/// <param name="ultima">
	/// A ultima celula marcada por ESTE dono de rastro (`ServerPlayer.UltimoSulco` ou
	/// `Projetil.UltimoSulco`). Passada por referencia porque a guarda so vale se ela for ATUALIZADA
	/// -- um contador local aqui deixaria a marca sair a cada sub-passo.
	/// </param>
	private void CarimbarSulco(ZoneKey zona, Protocol.Decal tipo, Vec2 onde, Facing dir,
							   ref Vec2 ultima)
	{
		var celula = new Vec2(MathF.Floor(onde.X / ZoneCollision.TileSize),
							  MathF.Floor(onde.Y / ZoneCollision.TileSize));
		if (celula.X == ultima.X && celula.Y == ultima.Y) return;
		ultima = celula;

		// ============================ O SULCO VAI NO CENTRO DA CELULA ============================
		// Nao na posicao do corpo. O DU carimba no TURF (`var/turf/t=loc; TimedOverlay(t,600,i)` --
		// death.dm:223), ou seja alinhado a grade; carimbar no pixel exato onde o corpo estava faz a
		// distancia entre duas marcas VARIAR.
		//
		// A conta: o passo do servidor e ~21 px e a celula tem 32. O corpo entra em cada celula com
		// uma sobra diferente (0, 10, 20, 30...), entao duas marcas consecutivas ficam de 22 a 42 px
		// uma da outra -- e o sprite tem 32. Onde a sobra e grande, aparece o buraco. Era o "ta
		// melhor mas n ta continuo" do dono.
		//
		// Alinhado a celula sao 32 px exatos entre marcas de 32 px: encostam, sem vao e sem
		// sobreposicao.
		// ========================================================================================
		float t = ZoneCollision.TileSize;
		var noCentro = new Vec2((celula.X + 0.5f) * t, (celula.Y + 0.5f) * t);

		MandarDecalque(zona, tipo, noCentro, dir);
	}

	/// <summary>
	/// DANO ESPALHADO PELO CORPO INTEIRO, em vez de num membro so.
	///
	/// No arremesso ele e o baque, e a dose e o que faltava voar. Mas o mecanismo nao e do
	/// arremesso: passar fome usa o mesmo caminho (`TickDoEstomago`), e por isso a dose e um
	/// `double` -- o dano de fome e fracionario por passada, e um `int` o truncaria pra zero.
	/// </summary>
	private static void Espalhar(ServerPlayer pl, double dano)
	{
		foreach (Jandirus.Core.Combat.BodyPart parte in pl.Combate.Corpo.Partes)
			parte.Vida = Math.Max(0, parte.Vida - dano / pl.Combate.Corpo.Partes.Count);
		pl.Combate.SincronizarVida();
	}

	// =====================================================================
	// DESTRUICAO DE CENARIO
	// =====================================================================
	/// <summary>
	/// AS CELULAS QUE JA CAIRAM, por zona. Vive em memoria e nao no `.col`: o arquivo e imutavel e
	/// compartilhado (ver `ZoneCollision`), e o mapa tem que voltar ao normal quando o servidor
	/// reinicia -- o original tambem nao salvava turf destruido fora do `MapSave`.
	/// </summary>
	private readonly Dictionary<string, HashSet<(int X, int Y)>> _cenarioCaido =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// DERRUBA A CELULA que <paramref name="onde"/> toca. Devolve se caiu alguma coisa.
	///
	/// ============================ O QUE O ORIGINAL FAZ ============================
    /// `turf/proc/Destroy()`: se `destroyable`, 25% de chance de poeira e o turf e SUBSTITUIDO por
    /// `/turf/Ground/Ground8` -- chao liso. Ou seja destruir nao abre buraco, aplaina.
    ///
    /// A resistencia padrao de todo turf e todo obj e VINTE (`buildable.dm:353-360`), e por isso
    /// praticamente tudo e destrutivel desde o primeiro soco: quem protege um pedaco do mapa e o
    /// `destroyable = 0` (borda do mundo, arena, espaco), nao a resistencia.
    ///
    /// AQUI O `destroyable` E APROXIMADO pela BORDA DO MUNDO: o conversor ja marca essas celulas
    /// com um grupo proprio (`ZoneCollision.BordaDoMundo`), que e exatamente o
    /// `/turf/Other/Blank` -- o unico `destroyable = 0` que aparece em quantidade nos mapas. As
    /// outras excecoes do DM (arena, Void_Wall) ficam anotadas como divida.
	/// =============================================================================
	/// </summary>
	private bool DerrubarCenario(ZoneKey zona, Vec2 onde)
	{
		ZoneCollision? mapa = _catalogo?.Get(zona)?.Mapa;
		if (mapa == null) return false;

		(int cx, int cy) = CelulaDoPonto(onde);
		if (!mapa.BlockedCell(cx, cy)) return false;

		// ============================ BORDA NAO CAI: E GEOMETRIA ============================
		// Duas perguntas, e as duas precisam ser feitas:
		//
		// A BEIRADA DO MAPA, por aritmetica. Nao depende de plano nenhum, e e a que faltava: um mapa
		// que termina em agua nao tem `BordaDoMundo` marcado em lugar nenhum, e a ultima coluna caia
		// como qualquer cenario.
		//
		// O QUE ESTAVA AQUI ERA CODIGO MORTO -- em 100% dos casos, nao "quase".
		//
		// A guarda antiga perguntava `Grupo(cx,cy) == BordaDoMundo`. O plano de grupo e escrito no
		// arquivo `.vis`, e o servidor carrega o `.col`, que sai SEM ele: `Grupo` devolvia
		// `SemGrupo` (255) em toda celula de toda zona do jogo, e 255 nunca e 0. A condicao
		// `Visao.Length > 0` que a acompanhava tambem nao protegia nada -- as 40 entradas do
		// manifest tem `visao`.
		//
		// E nem daria pra consertar mandando o servidor ler o `.vis`: o plano nasce ZERADO e so as
		// celulas cegas sao escritas, entao 244.198 das 250.000 celulas da Terra ficam com grupo 0,
		// que e o MESMO codigo de `BordaDoMundo`. O sentinela colide com o preenchimento padrao, e
		// ler o plano faria 97% do mapa responder "sou borda do mundo".
		//
		// Sobra a aritmetica, que nao depende de plano nenhum e vale ate em mundo sorteado.
		// ===================================================================================
		if (mapa.NaBorda(cx, cy)) return false;

		return DerrubarCelula(zona, cx, cy);
	}

	/// <summary>
	/// DERRUBA UMA CELULA JA ESCOLHIDA, sem perguntar de novo se ela podia cair.
	///
	/// Separado do <see cref="DerrubarCenario"/> porque ha quatro caminhos ate aqui -- o corpo que
	/// voa (que descobre a celula andando), o SOCO (que ja sabe qual e a celula da frente), o
	/// <see cref="RacharChao"/> e a morte do planeta. O que eles nao podem e ter cada um a sua copia
	/// da parte que muda o mundo.
	///
	/// ============================ E POR ISSO O `destroyable` MORA AQUI ============================
	/// No original a mesma coisa acontece, e literalmente: os quatro caminhos do DM (`attack_proc`,
	/// `Movement Effects`, `attack cmn` e `Area_Death`) terminam todos em `T.Destroy()`, e e o
	/// `Destroy()` -- e nao os chamadores -- que abre com `if(src.destroyable)`
	/// (`Modules/Turfs/NewTurfs.dm:2-4`). Espalhar a pergunta pelos quatro seria pedir pra um deles
	/// ficar de fora, e um so ja basta pra o dono voltar a socar o vazio e ver o vazio cair.
	/// =============================================================================================
	/// </summary>
	private bool DerrubarCelula(ZoneKey zona, int cx, int cy)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return false;

		// ============================ O QUE NAO SE QUEBRA, E ERA A QUEIXA ============================
		// `destroyable = 0` (`Turfs.dm:72,81,89,102`, `NewTurfs.dm:24,29,36,193,202,254,261,268`,
		// `ProceduralSpace.dm:543,567`, `MajinSaga.dm:54,62`, `MindMeditate.dm:29,37`). O grosso e o
		// `/turf/Other/Blank`: denso, SEM `icon` nenhum -- invisivel no BYOND tambem, entao nao ha
		// arte perdida -- e indestrutivel la.
		//
		// Aqui ele caia: 1.690 celulas alcancaveis a pe, quase todas costuras no MIOLO do Templo e de
		// Arconia (x=274, x=434, y=326, y=54), longe demais pro anel de 2 celulas do `NaBorda` que
		// aproximava esta regra. O jogador socava o nada, o nada levantava poeira, virava terra batida
		// e ABRIA -- dava pra andar pra dentro do vazio. Ver `ZoneCollision.Indestrutivel`.
		//
		// A guarda vale pros teleportes tambem, e la ela impede uma perda de verdade: a entrada de
		// caverna e um `turf/Teleporters` (`destroyable = 0` + `isSpecial = 1`), e o `RacharChao`
		// transformava a entrada em terra batida -- a passagem sumia ate o proximo boot.
		// ============================================================================================
		if (mapa.Indestrutivel(cx, cy)) return false;

		if (!_cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? caidas))
		{
			caidas = [];
			_cenarioCaido[zona.Name] = caidas;
		}
		if (!caidas.Add((cx, cy))) return false;

		// O MAPA MUDA PROS DOIS LADOS: aqui, e no cliente pelo pacote abaixo. A celula deixa de
		// bloquear e deixa de cegar -- ela virou chao.
		mapa.Abrir(cx, cy);

		MandarCelulaCaida(zona, cx, cy);
		return true;
	}

	/// <summary>
	/// O CHAO RACHA NO IMPACTO. E o `attack cmn.dm:49` do original:
	///
	///     for(var/turf/T in view(1,src))
	///         if(prob(40) && !T.isSpecial && !T.proprietor && T.Resistance <= max(expressedBP, M.expressedBP))
	///             if(prob(60)) createDust(T,1)
	///             T.Destroy()
	///
	/// La isso acontece no ZanzoClash; aqui vale pra todo golpe PESADO ou CRITICO, que e onde o
	/// dono esperava ver estrago: "mesmo com golpes fortes e criticos que causam knock back e onda
	/// de choque o chao em si tb n esta quebrando".
	///
	/// ============================ CHAO NAO E PAREDE ============================
	/// O <see cref="DerrubarCenario"/> so mexe em celula que BLOQUEIA -- ele existe pra derrubar o
	/// muro em que o corpo bateu. Rachar o chao e outra coisa: a celula continua passavel, o que
	/// muda e o DESENHO (vira terra batida, o Ground8). Por isso o caminho aqui e proprio e nao
	/// consulta colisao nenhuma.
	/// ==========================================================================
	/// </summary>
	private void RacharChao(ZoneKey zona, Vec2 centro, double bp)
	{
		if (bp < Empurrao.ResistenciaPadrao) return;
		if (_catalogo?.Get(zona)?.Mapa is not { } mapa) return;

		const int T = ZoneCollision.TileSize;
		int cx0 = (int)Math.Floor(centro.X / T), cy0 = (int)Math.Floor(centro.Y / T);

		// `view(1)` sao as nove celulas em volta -- a do impacto e as oito vizinhas.
		for (int dy = -1; dy <= 1; dy++)
			for (int dx = -1; dx <= 1; dx++)
			{
				int cx = cx0 + dx, cy = cy0 + dy;
				if (cx < 0 || cy < 0 || cx >= mapa.Width || cy >= mapa.Height) continue;
				if (_rng.NextDouble() >= 0.40) continue;            // o `prob(40)` do original
				if (mapa.NaBorda(cx, cy)) continue;   // beirada do mapa nao racha (ver `DerrubarCenario`)

				// ============================ A MUDANCA E DO `DerrubarCelula`, E SO DELE ============================
				// Isto aqui era uma SEGUNDA COPIA da parte que muda o mundo (o conjunto `_cenarioCaido`,
				// o `Abrir` e o pacote), e o preco apareceu na hora de trazer o `destroyable`: a guarda
				// nova entrava no `DerrubarCelula` e este caminho passava por baixo dela. O chao rachava
				// em cima de entrada de caverna (`turf/Teleporters`, `destroyable = 0`) e a passagem
				// sumia ate o proximo boot.
				//
				// No original os dois sao o MESMO proc: `attack cmn.dm:49-51` chama `T.Destroy()`,
				// exatamente como o soco e o arremesso. A unica coisa que e do rachar e o sorteio e o
				// `view(1)` -- e essas duas ficaram aqui.
				// ==================================================================================================
				DerrubarCelula(zona, cx, cy);
			}
	}

	/// <summary>Quantas celulas o `--quebrarteste N` derruba no nascimento. 0 = desligado.</summary>
	private int _quebrarDeTeste;

	/// <summary>
	/// BANCADA: derruba cenario em volta de quem nasceu, sem precisar de briga.
	///
	/// Usa o MESMO <see cref="RacharChao"/> do combate -- e nao um caminho proprio -- porque um
	/// atalho de teste que nao passa pelo codigo de verdade testa o atalho. A forca vai alta de
	/// proposito (a resistencia do cenario e 20) e o raio cresce ate juntar o tanto pedido.
	/// </summary>
	private void QuebrarCenarioDeTeste(ServerPlayer pl)
	{
		const int T = ZoneCollision.TileSize;
		_cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? antes);
		int alvo = (antes?.Count ?? 0) + _quebrarDeTeste;

		for (int anel = 0; anel < 12; anel++)
		{
			for (int dy = -anel; dy <= anel; dy++)
				for (int dx = -anel; dx <= anel; dx++)
				{
					if (Math.Abs(dx) != anel && Math.Abs(dy) != anel) continue;   // so a casca
					RacharChao(pl.Zone, pl.Pos + new Vec2(dx * T, dy * T), 1_000_000);
				}
			if (_cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? agora)
				&& agora.Count >= alvo) break;
		}

		int quantas = _cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? fim) ? fim.Count : 0;
		Godot.GD.Print($"[server] BANCADA: {quantas} celula(s) de cenario quebradas em {pl.Zone.Name}");

		MartelarABorda(pl.Zone);
	}

	/// <summary>
	/// BANCADA: bate com forca de sobra NOS QUATRO CANTOS do mapa e confere que NADA caiu.
	///
	/// ============================ POR QUE ISTO PRECISA DE TESTE ============================
	/// O dono fotografou o personagem no limite do mundo com uma mancha de chao quebrado ao lado.
	/// A guarda que existia olhava so o GRUPO da celula (`BordaDoMundo`), que o conversor marca
	/// apenas no `/turf/Other/Blank` -- um mapa que termina em AGUA nao tem nada marcado na ultima
	/// coluna, e a beirada do oceano racha como qualquer outro chao.
	///
	/// A regra nova (`ZoneCollision.NaBorda`) e aritmetica e nao depende de plano nenhum. Mas uma
	/// regra escrita nao e uma regra ligada -- e a coisa que este projeto mais erra. Aqui ela e
	/// EXERCIDA: quatro cantos, forca de um milhao, e a conta antes e depois.
	/// ======================================================================================
	/// </summary>
	private void MartelarABorda(ZoneKey zona)
	{
		if (_catalogo?.Get(zona)?.Mapa is not { } mapa) return;
		const int T = ZoneCollision.TileSize;

		_cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? antes);
		int comecou = antes?.Count ?? 0;

		foreach ((int cx, int cy) in new[]
		{
			(0, 0), (mapa.Width - 1, 0), (0, mapa.Height - 1), (mapa.Width - 1, mapa.Height - 1),
			(mapa.Width / 2, 0), (mapa.Width / 2, mapa.Height - 1),
			(0, mapa.Height / 2), (mapa.Width - 1, mapa.Height / 2),
		})
		{
			var onde = new Vec2(cx * T + T / 2f, cy * T + T / 2f);
			RacharChao(zona, onde, 1_000_000);
			DerrubarCenario(zona, onde);
		}

		int agora = _cenarioCaido.TryGetValue(zona.Name, out HashSet<(int X, int Y)>? fim2) ? fim2.Count : 0;
		// O RELATO NAO FALA EM "AGUENTAR". A borda nao tem resistencia nem contador: a destruicao
		// RECUSA antes de olhar forca (ver `NaBorda` em `RacharChao` e `DerrubarCenario`). As oito
		// marteladas sao do TESTE, e nao um limite -- oitocentas dariam o mesmo zero.
		Godot.GD.Print(agora == comecou
			? $"[server] BANCADA: a borda de {zona.Name} ({mapa.Width}x{mapa.Height}) e INQUEBRAVEL -- "
			  + "a destruicao recusa a beirada antes de olhar forca (8 tentativas, 0 celulas)"
			: $"[server] BANCADA: FALHA -- a borda de {zona.Name} cedeu em {agora - comecou} celula(s)");
	}

	/// <summary>Manda pra quem chega a lista do que ja caiu na zona.</summary>
	private void MandarCenario(ServerPlayer pl)
	{
		if (!_cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? caidas)) return;
		foreach ((int cx, int cy) in caidas) pl.Peer?.Send(PacoteDeCenario(false, cx, cy),
			Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O PACOTE DE CENARIO, num lugar so.
	///
	/// Ele tem tres escritores (a parede que cai, o chao que racha, e a lista que vai pra quem
	/// chega) e ganhou um campo novo -- o `limpar`. Tres copias da mesma escrita sao tres chances
	/// de uma delas ficar pra tras quando o formato muda, e foi por isso que virou funcao.
	/// </summary>
	private static NetDataWriter PacoteDeCenario(bool limpar, int cx, int cy, ulong zona = 0)
	{
		var w = Protocol.Begin(Protocol.S2C.Cenario);
		w.Put(limpar);
		w.Put((ushort)cx);
		w.Put((ushort)cy);
		// A ZONA SO VIAJA NA LIMPEZA. Uma celula que cai e sempre da zona em que quem recebe esta
		// -- o pacote so sai pra `ZoneList` dela. A limpeza, nao: ela vai pra TODO MUNDO, porque
		// quem esta noutro planeta guardou a cena suja no cache e precisa jogar fora aquela copia.
		if (limpar) w.Put(zona);
		return w;
	}

	private void MandarCelulaCaida(ZoneKey zona, int cx, int cy)
	{
		NetDataWriter w = PacoteDeCenario(false, cx, cy);
		foreach (ServerPlayer o in ZoneList(zona.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// "ESQUECA TODO O ESTRAGO DESTA ZONA" -- o verb de admin que refaz o cenario.
	///
	/// O cliente nao tem como DESFAZER celula por celula: ele apagou as camadas do tilemap e
	/// escreveu terra batida por cima, e nao guardou o que havia antes. Por isso a ordem e
	/// grossa: zera a lista, devolve a colisao ao que o arquivo diz, e recarrega a cena do disco.
	/// </summary>
	private void MandarLimpezaDeCenario(ZoneKey zona)
	{
		// TODO MUNDO, e nao so quem esta na zona.
		//
		// O cliente guarda ate duas cenas de zona vivas no cache (`_zonasVivas`). Quem viu paredes
		// cairem na Terra e viajou pra Namek levou a cena da Terra SUJA junto -- com as celulas
		// apagadas no tilemap. Se a limpeza so fosse pra quem esta na zona, essa pessoa voltaria
		// pra Terra, o cache acertaria, e ela veria o buraco que ja nao existe mais pra ninguem.
		//
		// O pacote e minusculo e isto acontece uma vez, quando um admin manda refazer.
		NetDataWriter w = PacoteDeCenario(true, 0, 0, zona.Hash);
		foreach (ServerPlayer o in _players.Values)
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}
}
