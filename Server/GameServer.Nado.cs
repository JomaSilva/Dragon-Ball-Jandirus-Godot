using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O NADO, LADO DO SERVIDOR -- o verb `Swim` (`Code/Modules/Movement Improvement/Swim.dm:5-23`).
///
/// As formulas moram no <see cref="Nado"/> (Core), porque a velocidade que sai daqui e prevista
/// pelo cliente tambem. Aqui mora o que e DECISAO: quem pode comecar a nadar, quanto ja pagou,
/// quando o nado cai sozinho, e o que acontece quando a agua acaba embaixo do corpo.
///
/// ============================ O QUE O NADO **NAO** MUDA ============================
/// Uma coisa so muda na passagem: a agua deixa de parar este corpo (<see cref="ModoDeTravessia"/>
/// <c>.Nadando</c>). Sobre o chao seco nadar nao faz nada de diferente de andar -- a parede continua
/// parando, o soco continua saindo, a altura continua zero. E o original tambem nao muda nada
/// (`testWaters` so olha `Water`), com uma diferenca: la nem da pra LIGAR o nado fora d'agua.
///
/// E NAO E VOO RENTE AO CHAO. `pl.Altitude` fica em zero, entao:
///   * o corpo nao sobe na tela (o desenho sobe por `Altitude * Voo.EscalaNaTela`);
///   * a sombra nao existe -- ela e um node que so nasce quando a altura sai de zero;
///   * o andar continua sendo 0, entao quem esta no chao alcanca e enxerga quem nada;
///   * o byte de altitude nem viaja no snapshot (ele so vai com o bit `Voando`).
/// ==================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>
	/// COMO ESTE CORPO ESTA ATRAVESSANDO O MUNDO AGORA -- a pergunta tem UM dono, e e este.
	///
	/// A conta em si mora no Core (<see cref="ClasseDeAgua.ModoDe"/>), porque o cliente faz a MESMA
	/// pergunta com a mesma funcao. Aqui so se diz de onde vem cada resposta:
	///
	///   * ARREMESSADO -- `TiquesDeVoo > 0` e o `KB` do DM: quem esta sendo jogado atravessa o lago.
	///   * NO AR -- ALTURA, e nao a flag de voo. Quem esta subindo (ou caindo) nos primeiros 32 px
	///     ainda consulta o mapa, e sem isto a decolagem da beira do lago levaria parede de agua.
	///   * NADANDO -- <see cref="ServerPlayer.Nadando"/>, que so este arquivo escreve.
	/// </summary>
	private static ModoDeTravessia ModoDeTravessiaDe(ServerPlayer pl)
		=> ClasseDeAgua.ModoDe(pl.TiquesDeVoo > 0, pl.Altitude > 0f, pl.Nadando);

	/// <summary>
	/// Este corpo esta em cima de agua AGORA? Falso em zona sem plano de agua (espaco, Sala do
	/// Tempo, interior de nave) -- e e o certo: nao ha lago nenhum la.
	///
	/// PELA CAIXA DOS PES e nao pelo centro: ver <see cref="MoveRules.NaAgua"/>, que explica a faixa
	/// de 8 px na margem que a pergunta pelo centro abriria.
	/// </summary>
	private bool SobreAgua(ServerPlayer pl)
		=> MapaDaZonaOuCatalogo(pl.Zone) is { } mapa && MoveRules.NaAgua(mapa, pl.Pos);

	/// <summary>
	/// O TILE A FRENTE E AGUA? -- o `get_step(usr, dir)` do verb (`Swim.dm:9-10`).
	///
	/// ============================ O PASSO DO DM CONTINUA SEM SER PORTADO, E AGORA DA PRA DIZER POR QUE ============================
	/// O original so deixa ligar o nado olhando pra agua e, no MESMO gesto, da o passo pra dentro
	/// dela (`step(usr, usr.dir)`, `:15`). Aquele passo existe porque o BYOND anda por TILE: sem ele,
	/// ligar o nado de frente pro lago nao teria efeito nenhum ate o jogador apertar a direcao.
	///
	/// A primeira versao disto recusou o passo com o argumento de que ele seria "um empurrao do
	/// servidor a ser corrigido contra a previsao do cliente" -- e o argumento se contradizia, porque
	/// SEM ele ninguem entrava: o tique matava o nado antes de o corpo molhar, e o jogador a pe nao
	/// entra andando (a agua barra quem esta a pe).
	///
	/// O QUE FALTAVA NAO ERA O EMPURRAO, ERA DEIXAR O MODO VALER ANTES DE MOLHAR. Ligado o nado, o
	/// `ModoDeTravessiaDe` ja devolve `Nadando` -- a agua para de barrar este corpo no mesmo instante
	/// --, e o jogador entra pelo proprio movimento, sem o servidor mexer em posicao nenhuma. O unico
	/// que discordava era o desligamento por chao seco, e ele passou a distinguir "ainda entrando" de
	/// "saiu no seco" (ver <see cref="ServerPlayer.NadoJaMolhou"/>).
	///
	/// Entao o passo do DM segue de fora, agora por escolha e nao por acidente: ele e um deslocamento
	/// que o servidor impoe (sequencia carimbada, janela de correcao, pacote de `Correction`) pra
	/// resolver um problema que o pixel nao tem.
	/// ==========================================================================================================================
	/// </summary>
	private bool AguaAFrente(ServerPlayer pl)
	{
		if (MapaDaZonaOuCatalogo(pl.Zone) is not { } mapa) return false;
		Vec2 frente = pl.Facing switch
		{
			Facing.North => new Vec2(0, -ZoneCollision.TileSize),
			Facing.South => new Vec2(0, ZoneCollision.TileSize),
			Facing.East => new Vec2(ZoneCollision.TileSize, 0),
			_ => new Vec2(-ZoneCollision.TileSize, 0),
		};
		return mapa.EhAguaEm(pl.Pos + frente);
	}

	/// <summary>
	/// ELE PODE COMECAR A NADAR DAQUI?
	///
	/// O DM pede so o tile A FRENTE. Aqui vale o de frente **ou** o de baixo, e a diferenca cobre um
	/// caso que o original nao tem: no BYOND e impossivel estar em cima de agua sem estar nadando
	/// (o `Enter()` recusa), aqui da -- deslogar dentro do lago, ser jogado la por um arremesso,
	/// nascer perto demais da beira. Recusar quem JA esta na agua seria a unica forma de prender
	/// alguem nela, que e exatamente o que o dono nao quer.
	///
	/// A CLAUSULA "A FRENTE" VIROU A GUARDA DE TUDO, e vale dizer por que: como o modo `Nadando` vale
	/// desde o instante em que o verb aceita (e nao so depois de o corpo molhar), e ELA que impede
	/// "nadar" de virar um botao de andar por cima de qualquer agua do mapa a partir de qualquer
	/// lugar. Sem agua a um tile de distancia o verb recusa, e quem conseguiu ligar tem
	/// <see cref="Jandirus.Core.World.Nado.PrazoParaEntrar"/> segundos pra entrar de verdade.
	/// </summary>
	private bool PodeComecarANadar(ServerPlayer pl) => SobreAgua(pl) || AguaAFrente(pl);

	/// <summary>
	/// LIGAR E DESLIGAR O NADO -- o verb, e o mesmo verb faz os dois (`Swim.dm:5-23`).
	///
	/// ============================ DESLIGAR EM CIMA DA AGUA E RECUSADO ============================
	/// O original deixa: `else if(swim)` nao pergunta onde voce esta. O resultado la e um corpo a pe
	/// numa celula de agua, e como o `Enter()` de toda celula de agua vizinha o recusa, quem fizesse
	/// isso no meio do oceano ficava **preso pra sempre**. E uma armadilha, e o dono pediu o
	/// contrario ("prefira o que nao prende ninguem").
	///
	/// E AQUI SERIA PIOR QUE LA, e vale dizer por que: o `MoveRules.Advance` tem uma saida de
	/// emergencia ("ja preso dentro de parede: deixa sair") que devolve o passo CHEIO, sem consultar
	/// nada. Ela e o que impede a agua de prender -- e, ligada de proposito, seria andar por cima do
	/// lago inteiro (e atravessar parede) a pe.
	///
	/// Entao: em cima da agua o verb recusa e diz por que. Ele nao precisa desligar -- quem chega no
	/// seco tem o nado desligado SOZINHO, no tique, que e o que o `unitimer` do DM ja fazia
	/// (`movement handler.dm:283-287`).
	/// ==========================================================================================
	/// </summary>
	private void AlternarNado(ServerPlayer pl)
	{
		if (pl.Nadando)
		{
			if (SobreAgua(pl))
			{
				Avisar(pl, "voce esta em cima da agua -- nade ate o seco pra parar.");
				return;
			}
			PararDeNadar(pl, "voce para de nadar.");
			return;
		}

		// O `if(!usr.KO)` do original (`Swim.dm:11`). Morto entra junto: no DM um cadaver nao chega
		// a chamar verb nenhum.
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "nao da pra nadar assim."); return; }

		// ============================ SEM FOLEGO NAO SE COMECA -- E E UMA ARMADILHA FECHADA ============================
		// O tique da exaustao faz DUAS coisas: para o nado e **zera o Ki** (`f.Ki = 0`, literal do
		// `Stats.dm:405-410`). Sem esta guarda, quem esta boiando e aperta o nado com o tanque ainda no
		// piso perde no tique seguinte o pouco que a regeneracao tinha devolvido -- e, martelando a
		// tecla, nunca acumula o bastante pra nadar de volta. Ficaria preso de verdade, que e o oposto
		// do que o dono pediu (*"recarregar o ki pra voltar a nadar E CONTINUAR"*).
		//
		// O NUMERO NAO E O CORTE DA PARADA, E A BANCADA MOSTROU POR QUE: com o corte cru, quem religa
		// um fio acima dele exausta no MESMO decimo e perde o Ki de novo -- 0,3 tile por rodada, pra
		// sempre. `Nado.KiParaComecar` pede o corte MAIS tres segundos de custo, que e o menor tanque
		// que faz o ciclo espera->nada->espera ANDAR. Ver `Nado.SegundosMinimosDeNado`.
		if (pl.Ficha.Ki < Nado.KiParaComecar(pl.Ficha.MaxKi, pl.Ficha.KiMod, pl.Ficha.swimmastery))
		{
			Avisar(pl, SobreAgua(pl)
				? "sem fôlego pra nadar. Fique parado e espere o Ki subir -- então nade de novo."
				: "você não tem fôlego pra nadar agora.");
			return;
		}

		if (!PodeComecarANadar(pl))
		{
			// "You can't swim there!" (`Swim.dm:23`) -- a UNICA mensagem do sistema inteiro no DM.
			// Encostar na agua a pe nao diz nada la, e nao diz nada aqui: o `Enter()` devolve 0
			// calado, indistinguivel de um muro. Ver a resposta do `MoveRules`.
			Avisar(pl, "nao da pra nadar aqui.");
			return;
		}

		// LIGAR O NADO DESLIGA O VOO -- `usr.flight = 0` (`Swim.dm:12`). O caminho de volta esta em
		// `AlternarVoo`, e os dois juntos sao o que faz os dois estados se excluirem no original.
		//
		// A ALTURA NAO E ZERADA NA MARRA: quem estava no ar continua descendo pelo `TickDoVoo`, e so
		// nada de verdade quando encostar. Zerar aqui seria teletransportar o corpo pro chao.
		if (pl.Voando)
		{
			pl.Voando = false;
			MandarEfeito(pl, "voo", 0);
		}
		pl.VooRapido = false;

		pl.Nadando = true;
		pl.UltimaDirDoNado = pl.Facing;

		// A JANELA DE ENTRADA COMECA AQUI -- e ela ja nasce FECHADA pra quem ligou o nado dentro da
		// agua (o mergulhado, o arremessado, o que deslogou no lago). Perguntar agora, e nao esperar o
		// primeiro tique, e o que faz o prazo valer so pra quem tem mesmo um caminho a andar.
		pl.NadoJaMolhou = SobreAgua(pl);
		pl.NadoSecoS = 0f;

		Avisar(pl, "voce comeca a nadar.");
		AnunciarNado(pl);
	}

	/// <summary>
	/// PARA DE NADAR, por qualquer um dos quatro motivos (verb no seco, agua acabou, Ki no fim,
	/// nocaute). Uma porta so.
	///
	/// UMA PORTA E NAO QUATRO porque cada saida tem que fazer as MESMAS tres coisas: apagar o bit,
	/// devolver a velocidade e avisar o cliente na hora. Uma saida que esquecesse a terceira deixaria
	/// o dono do corpo ate 200 ms achando que ainda nada -- e nesses 200 ms ele estaria prevendo o
	/// passo por uma regra que o servidor ja nao aceita, o que vira correcao de posicao (o corpo
	/// tremendo) em jogo honesto.
	/// </summary>
	private void PararDeNadar(ServerPlayer pl, string? aviso)
	{
		if (!pl.Nadando) return;
		pl.Nadando = false;
		if (aviso != null) Avisar(pl, aviso);
		AnunciarNado(pl);
	}

	/// <summary>
	/// RECALCULA A VELOCIDADE E MANDA A FICHA NA HORA.
	///
	/// Os dois juntos, e nao so o segundo: `SpeedStat` **nao esta** na lista de campos que o
	/// `TickFichas` compara antes de reenviar (ver `RecalcularVelocidade`), entao mandar a ficha sem
	/// recalcular mandaria a velocidade velha. E recalcular sem mandar deixaria o cliente andando na
	/// velocidade errada ate outro numero da ficha mexer por acaso.
	/// </summary>
	private void AnunciarNado(ServerPlayer pl)
	{
		RecalcularVelocidade(pl);
		MandarFicha(pl);
	}

	/// <summary>
	/// O TIQUE DO NADO -- porte de `Stats.dm:397-410` mais o desligar do `unitimer`
	/// (`movement handler.dm:283-287`).
	///
	/// Roda no tique CHEIO junto do voo, da forma e da carga, e pelo mesmo motivo que o cabecalho do
	/// `TickDoVoo` ja escreve: os quatro mexem no MESMO Ki, e rodar em cadencias diferentes faria o
	/// saldo depender de qual relogio acordou primeiro.
	/// </summary>
	private void TickDoNado(ServerPlayer pl, float dt)
	{
		if (!pl.Nadando) return;

		// NOCAUTE E MORTE TIRAM O NADO. No DM isso acontece por tabela (`KO.dm` zera os estados de
		// movimento), e aqui pelo mesmo caminho de todos os outros: quem caiu nao esta mais fazendo
		// nada.
		//
		// O CORPO FICA BOIANDO ONDE ESTAVA -- e agora ele FICA MESMO. Esta linha dizia "e sai andando
		// pelo escape do `MoveRules`", e era verdade ate o escape virar dirigido (`MoveRules.Escapar`).
		// Nao ha o que consertar: quem esta nocauteado nao anda de qualquer jeito, e quem acorda liga o
		// nado de novo -- a agua e um lugar que exige outro modo, nao um lugar que prende.
		if (pl.Ficha.KO || pl.Ficha.dead) { PararDeNadar(pl, null); return; }

		// ============================ A AGUA ACABOU EMBAIXO DELE ============================
		// `if(!T.Water && swim)` -- o `unitimer` do DM, 10 vezes por segundo, com a mesma mensagem.
		// E ELE que responde a metade do item 4 do pedido: chegar no seco desliga o nado sozinho.
		// Nao ha nada pra o jogador fazer, nao ha nada pra ele lembrar, e nao ha como ficar nadando
		// em terra firme -- que era o defeito latente do save do original.
		//
		// VALE TAMBEM PRA ZONA SEM AGUA (`mapa` nulo ou sem plano): `EhAguaEm` devolve falso, e o
		// nado cai no primeiro tique depois de uma passagem pro espaco, pra Sala do Tempo ou pro
		// interior de uma nave. E o certo -- nao ha lago nenhum la.
		//
		// ============================ MAS "SECO" TEM DUAS LEITURAS, E SO UMA DESLIGA NA HORA ============================
		// Este `if` sozinho matava o nado no MESMO decimo em que ele nascia: o verb aceita quem tem
		// agua A FRENTE, e da beira do lago o corpo ainda esta no seco quando o primeiro tique chega.
		// O diario da bancada mostrava as duas linhas juntas -- "12,1s nado LIGOU | 12,1s nado
		// APAGOU" -- e o jogador atravessava o teste inteiro a pe.
		//
		// Entao a pergunta deixou de ser "ele esta na agua?" e passou a ser **"ele ja esteve?"**:
		//
		//   * JA MOLHOU -- vale o original, sem carencia nenhuma: chao seco desliga neste tique. E
		//     por isso que sair na outra margem para o nado, e por isso que nao da pra sair andando
		//     pelo mapa com a pose de voo.
		//   * AINDA NAO -- e a janela de ENTRADA. Ela dura `Nado.PrazoParaEntrar` segundos e nao
		//     rearma (o bit so vai de falso pra verdade), entao ela nao vira nado eterno em terra
		//     firme nem se o jogador entrar e sair da agua de proposito.
		//
		// E ELA CUSTA ZERO: o `return` abaixo sai ANTES do custo de Ki e da maestria. Nadar no seco
		// continua sendo o que o cabecalho deste arquivo promete -- "nada de diferente de andar".
		// ==========================================================================================================
		if (!SobreAgua(pl))
		{
			if (pl.NadoJaMolhou) { PararDeNadar(pl, "voce para de nadar."); return; }

			pl.NadoSecoS += dt;
			if (pl.NadoSecoS < Nado.PrazoParaEntrar) return;

			// O PRAZO VENCEU SEM ELE ENTRAR. A mensagem e OUTRA de proposito: "voce para de nadar"
			// e o aviso de quem estava nadando, e quem nunca molhou nao estava -- dizer a mesma
			// frase deixaria o jogador achando que atravessou alguma coisa.
			PararDeNadar(pl, "voce nao chegou a entrar na agua.");
			return;
		}

		// MOLHOU. Daqui pra frente vale a regra do original, e este bit nunca volta a ser falso
		// enquanto o nado durar (ele e zerado no `AlternarNado`, quando um nado NOVO comeca).
		pl.NadoJaMolhou = true;

		// ============================ EM CENA NAO SE COBRA NEM SE TREINA ============================
		// A mesma guarda do `TickDoVoo`, e pela mesma razao: durante os 140 s da cena do SSJ3 o corpo
		// esta preso, e cobrar o Ki de nadar esvaziaria o tanque por assistir a uma cinematica. A
		// maestria para junto, porque ela e paga POR este custo.
		// ========================================================================================
		if (EmCena(pl)) return;

		Jandirus.Core.Stats.Fighter f = pl.Ficha;

		// PARA POR EXAUSTAO: `if(Ki > MaxKi*0.01) ... else { to_chat("You stop swimming."); Ki=0 }`.
		// O `Ki = 0` e literal do original -- quem esvazia o tanque nadando sai da agua sem nada.
		if (f.Ki <= Nado.KiQuePara(f.MaxKi))
		{
			f.Ki = 0;
			// ============================ ELE FICA ONDE ESTA, E ISSO E O DM ============================
			// Aqui havia um `LevarProSeco(pl, "voce e levado pra margem.")` -- **invencao deste port**,
			// justificada com "um corpo livre pela colisao e parado pela regra nao pode existir". O
			// SINTOMA estava certo e o REMEDIO errado, e o dono provou os dois de uma vez:
			//
			//   *"ao acabar o ki nadando, eu sou JOGADO DE VOLTA PRA MARGEM mas da um bug q se eu
			//    estiver SEGURANDO O BOTAO DE ANDAR eu consigo VOLTAR PRA AGUA ANDANDO POR CIMA DELA.
			//    (...) entao TIRA ISSO DE TELEPORTAR PRA MARGEM, e faca o personagem FICAR PRESO LA
			//    tendo q RECARREGAR O KI pra voltar a nadar e continuar"*
			//
			// O teleporte nao fechava o buraco -- ele so escondia: quem chegasse na margem com a tecla
			// apertada voltava andando por cima do lago, porque a saida de emergencia do `MoveRules`
			// aprovava TODO passo de quem ja estava numa celula invalida. Quem fechou foi o
			// `MoveRules.Escapar`: hoje a agua e um lugar que exige OUTRO MODO, e o corpo a pe nela nao
			// anda -- nem pra dentro, nem pra fora, nem atraves de parede.
			//
			// E O DM SEMPRE FEZ ASSIM: `Stats.dm:405-410` so desliga o nado onde o corpo esta (com o
			// `Ki = 0` literal que a linha acima copia), e o `testWaters` (`Swim.dm:26-38`) recusa o
			// `Enter()` de quem nao nada, nao voa e nao esta sendo arremessado.
			//
			// COMO ELE SAI: o Ki regenera parado, sem porta de agua nem de imobilidade
			// (`Core/Stats/RegenDeKi.cs`) -- com jato de 3x abaixo de 10% do tanque. Medido com MaxKi
			// 100: ~10 s compram 3,2 tiles de nado, ~60 s compram 16 tiles, e o tanque cheio compra 58.
			// Cada ciclo GUARDA o terreno andado, entao a travessia sempre progride. Ver o aviso logo
			// abaixo, que e o que conta isso pro jogador.
			// ======================================================================================
			PararDeNadar(pl, "o fôlego acaba: você para de nadar e fica boiando. "
							 + "Espere o Ki voltar e nade de novo (tecla de nado) pra sair daqui.");
			return;
		}

		// ============================ O CUSTO, LITERAL ============================
		// `Stats.dm:401-403`. A maestria sobe ANTES de o custo ser calculado, dentro do mesmo `if` --
		// entao a ordem aqui e a de la. Admin nao paga pelo voo (`freeflight`), e o original nao tem
		// equivalente disso pro nado: aqui tambem nao tem.
		// ==========================================================================
		f.swimmastery = Nado.SubirMaestria(f.swimmastery, dt);
		f.Ki -= Nado.CustoPorSegundo(f.MaxKi, f.KiMod, f.swimmastery) * dt;
		if (f.Ki < 0) f.Ki = 0;   // `if(Ki<0) Ki=0` (`Stats.dm:404`)

		// O GANHO DE BP: `Swim_Gain()` (`Movement.dm:1-4`), e so quando a direcao muda.
		if (pl.Facing != pl.UltimaDirDoNado)
		{
			pl.UltimaDirDoNado = pl.Facing;
			f.SwimGain();
		}
	}

	// O `LevarProSeco` MORREU AQUI, e vale dizer o que ele era: ele punha o corpo exausto na margem
	// mais proxima (`ChaoLivrePerto`, 12 aneis) e mandava um pacote de correcao. Nao era do DM, nao
	// tinha outro chamador, e o dono pediu a remocao com todas as letras -- ver o comentario do ramo
	// da exaustao, no `TickDoNado`, que conta a historia inteira e o que ficou no lugar.
	//
	// (Regra da casa: codigo substituido se DELETA. As tres outras citacoes do nome no repositorio
	// eram comentarios de bancada e cairam junto.)
}
