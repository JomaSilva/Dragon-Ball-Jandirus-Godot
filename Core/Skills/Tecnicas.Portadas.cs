namespace Jandirus.Core.Skills;

/// <summary>
/// AS TECNICAS JA PORTADAS -- a tabela de descritores, e so ela.
///
/// POR QUE ISTO MORA NO CORE E NAO NO SERVIDOR: o EFEITO de cada tecnica e codigo de servidor
/// (mexe no mundo, em quem esta perto, no que vai pela rede) e vive nos quatro
/// `Server/GameServer.Tecnicas.G*.cs`. Mas o DESCRITOR -- id, nome, modo, texto -- e dado puro, e
/// quem precisa dele nao e so o servidor: a bancada `dotnet run -- efeitos` e um programa de
/// console que NAO carrega o Godot, e era ela que continuava respondendo "117 concedidas, 5
/// portadas" com as 28 ja prontas. O denominador estava certo e o numerador nao, porque o
/// numerador so existia do lado que a bancada nao enxerga -- e um relatorio que subconta o proprio
/// progresso e tao ruim quanto um que superconta.
///
/// ============================ ESTA E A UNICA BOCA ============================
/// Esta tabela NASCEU gerada das chamadas `Tecnicas.Registrar(...)` dos lotes -- e portanto era uma
/// COPIA. Uma copia envelhece: quando o corte foi feito, VINTE E DOIS descritores ja discordavam do
/// que o lote dizia (o Kikoho tinha ganhado "Tres seguidos podem derrubar voce" no servidor e nao
/// aqui; o `Planet_Destroy` tinha ate NOME diferente dos dois lados), e ninguem podia perceber,
/// porque o registro do lote rodava DEPOIS e cobria este -- o jogador lia um texto e o console do
/// extrator contava outro.
///
/// Agora o descritor mora SO aqui. O lote registra so o CORPO (`Vivo(id, handler)`). Ao portar uma
/// tecnica nova: escreva o `Vivo` no lote e a linha `Por` aqui -- e a `--catalogoteste` cobra as
/// duas direcoes (corpo sem descritor, descritor sem corpo) toda rodada.
/// =============================================================================
/// </summary>
public static partial class Tecnicas
{
	private static void RegistrarPortadas()
	{

		// ---- lote G1 ----
		Por("Brutal_Clarity", "Brutal Clarity", Modo.Sustentada, "Você traz o poder para dentro: a técnica sobe muito enquanto o buff estiver de pé. " + "Em troca, sua energia vai embora cinco vezes mais rápido. Só se sustenta uma dessas " + "por vez, e ela cai sozinha quando o Ki acaba.");
		Por("Extreme_Burst", "Extreme Burst", Modo.Sustentada, "Estoura a própria velocidade muito além do normal. O corpo paga o preço: a energia " + "drena cinco vezes mais rápido enquanto durar. Só se sustenta uma dessas por vez.");
		Por("Fighting_Power", "Fighting Power", Modo.Sustentada, "Empurra Ki para dentro dos músculos e a ofensiva física dispara. Custa cinco vezes o " + "dreno normal de energia enquanto estiver ligada. Só se sustenta uma dessas por vez.");
		Por("Ultradense_Body", "Ultradense Body", Modo.Sustentada, "O corpo vira algo parecido com aço e a defesa física sobe muito. O dreno de energia " + "fica cinco vezes maior enquanto você segurar essa densidade. Uma dessas por vez.");
		Por("Ki_Blade", "Ki Blade", Modo.Sustentada, "Molda uma lâmina de Ki na mão: soma o mesmo bônus na ofensiva física E na de Ki. " + "Cobra uma mordida de energia só para nascer, e depois não drena nada. Ocupa a mão -- " + "não dá para manter a lâmina e outro buff de corpo ao mesmo tempo.");
		Por("Ki_Sword", "Ki Sword", Modo.Sustentada, "A lâmina vira uma espada longa: o mesmo bônus do Ki Blade, só que mais generoso, e uma " + "mordida de energia maior para invocar. Ocupa a mão como a lâmina, e as duas não " + "convivem.");
		Por("Super_Majin", "Super Majin", Modo.Sustentada, "A forma de Super Majin: técnica, ofensiva de Ki e velocidade sobem juntas. Fora da " + "Ascensão isso custa um pedaço da sua defesa; com a Ascensão de pé não há penalidade " + "e o corpo ainda ganha poder e o dobro de capacidade de energia.");

		// ---- lote G2 ----
		Por("Kaioken", "Kaio-ken", Modo.Sustentada, "Sincroniza o Ki com o corpo e MULTIPLICA seu poder de verdade -- ao preco de queimar " + "energia, fôlego e a própria carne, continuamente. Apertar de novo desliga. Usar um " + "múltiplo acima do dobro da sua maestria e ficar sem Ki despedaça o corpo.");
		Por("Giant_Form", "Forma Gigante", Modo.Sustentada, "O corpo incha até mais de quatro vezes o tamanho normal: muito mais força e defesa " + "física, e menos velocidade. Consome fôlego enquanto durar. Apertar de novo desliga.");
		Por("Elemental", "Elemental", Modo.Sustentada, "Chama o elemento que dorme em você e o veste como armadura. O que ele soma depende de " + "QUAL elemento é. Drena Ki e fôlego, e cai sozinho quando um dos dois acaba.");
		Por("Time_Touch", "Toque do Tempo", Modo.Instantanea, "Toca alguém ao seu lado e empurra o tempo dele: um surto curto de velocidade, pago com " + "um pedaço da idade de quem recebe.");
		Por("Growth_Spurt", "Estirão", Modo.Instantanea, "Um Saibaman nunca para de crescer. Envelhece um mês de uma vez e, em troca, recebe uma " + "carga de energia de cerca de duas vezes o seu máximo -- e um empurrão de poder.");
		Por("HamonBreathing", "Respiração Hamon", Modo.Instantanea, "Respiração de emergência: gasta uma fatia do Ki máximo e devolve vida ao corpo inteiro. " + "Só serve ferido, e demora muito a voltar.");
		Por("Spirit_Fist", "Punho Espiritual", Modo.Instantanea, "Um soco carregado de espírito, que sai como golpe CRÍTICO garantido. É pago com FÔLEGO, " + "não com Ki -- e trava seus golpes normais por alguns segundos.");
		// OS SETE PRESETS DE KAIO-KEN. No lote eles saem de um laco; aqui viram linhas, porque
		// esta tabela e DADO -- e dado que se gera de laco nao da pra ler nem conferir.
		Por("Kaioken_2", "Kaio-ken x2", Modo.Sustentada,
			"Liga o Kaio-ken direto em x2. Quanto maior o múltiplo, mais poder e mais rápido o corpo "
			+ "se desfaz. Apertar de novo desliga.");
		Por("Kaioken_3", "Kaio-ken x3", Modo.Sustentada,
			"Liga o Kaio-ken direto em x3. Quanto maior o múltiplo, mais poder e mais rápido o corpo "
			+ "se desfaz. Apertar de novo desliga.");
		Por("Kaioken_5", "Kaio-ken x5", Modo.Sustentada,
			"Liga o Kaio-ken direto em x5. Quanto maior o múltiplo, mais poder e mais rápido o corpo "
			+ "se desfaz. Apertar de novo desliga.");
		Por("Kaioken_10", "Kaio-ken x10", Modo.Sustentada,
			"Liga o Kaio-ken direto em x10. Quanto maior o múltiplo, mais poder e mais rápido o corpo "
			+ "se desfaz. Apertar de novo desliga.");
		Por("Kaioken_20", "Kaio-ken x20", Modo.Sustentada,
			"Liga o Kaio-ken direto em x20. Quanto maior o múltiplo, mais poder e mais rápido o corpo "
			+ "se desfaz. Apertar de novo desliga.");
		Por("Kaioken_50", "Kaio-ken x50", Modo.Sustentada,
			"Liga o Kaio-ken direto em x50. Quanto maior o múltiplo, mais poder e mais rápido o corpo "
			+ "se desfaz. Apertar de novo desliga.");
		Por("Kaioken_100", "Kaio-ken x100", Modo.Sustentada,
			"Liga o Kaio-ken direto em x100. Quanto maior o múltiplo, mais poder e mais rápido o corpo "
			+ "se desfaz. Apertar de novo desliga.");

		// ---- lote G3 ----
		Por("Sword_Strike", "Sword Strike", Modo.Instantanea, "Um giro com a espada de Ki que corta TODOS que estiverem na sua frente de uma vez, " + "somando dano em cada um. So sai com a Ki Sword ligada, e deixa os golpes especiais " + "em espera por um segundo e meio.");
		Por("Shield_Bash", "Shield Bash", Modo.Instantanea, "Avanca com o Ki Shield na frente e empurra todo mundo que estiver no caminho. Exige o " + "escudo de pe -- sem ele não ha o que golpear com. Mesma espera do Sword Strike.");
		Por("Special_Multihit", "Special: Multihit", Modo.Instantanea, "Uma barragem de socos num alvo so: ate dez golpes seguidos, um a cada dois decimos de " + "segundo. Quantos saem depende da sua Técnica. Se o alvo se afastar ou erguer a " + "guarda, a barragem para no meio.");
		Por("Final_Explosion", "Final Explosion", Modo.Instantanea, "Duas fases: primeiro você escolhe o tamanho do estouro e comeca a juntar energia, " + "preso no lugar; depois aperta de novo pra detonar. Quanto mais tempo carregar, mais " + "forte -- e passando de vinte e cinco segundos de carga você provavelmente MORRE junto.");
		Por("Light_Buster", "Light Buster", Modo.Instantanea, "Você grita, some, e meio segundo depois está nas costas do alvo acertando quatro " + "golpes seguidos. Pra quem assiste parece teleporte. Precisa de alguém a ate quatro " + "tiles de distância.");
		Por("Bite", "Morder", Modo.Instantanea, "Crava os dentes em quem estiver colado em você. Alimenta -- mata a fome de uma vez -- " + "e de vez em quando o sangue rende poder que NAO vai embora. Esse ganho tem cinco " + "minutos de espera entre um e outro.");
		Por("Rock_Paper_Scissors", "Jokenpo", Modo.Instantanea, "Você escolhe pedra, papel ou tesoura EM SEGREDO. No aperto seguinte você avanca dois " + "passos e soca: pedra pune quem não está atacando, papel pune quem esta, tesoura pune " + "quem está bloqueando. Acertar a leitura vale dano extra.");

		// ---- lote G4 ----
		Por("Telepathy", "Telepatia", Modo.Instantanea, "Fala direto na mente de qualquer pessoa do mundo, sem limite de distância. Não alcanca " + "quem está escondido, quem e Android, nem quem tem energia fraca demais pra ser achado. " + "Mande sem alvo pra ver quem da pra alcancar.", aba: "Outros");
		Por("Revive", "Ressuscitar", Modo.Instantanea, "Traz de volta a vida um morto que esteja do seu lado, e o puxa pra onde você esta. O " + "corpo volta inteiro: membros decepados crescem de novo, Ki e fôlego cheios. Você " + "precisa estar vivo pra usar.");
		Por("RiftTeleport", "Rasgo Dimensional", Modo.Instantanea, "Rasga a realidade e atravessa ate outro mundo. Exige Ki CHEIO e leva TODO ele: você " + "chega do outro lado sem energia nenhuma. Mande sem destino pra ver aonde o rasgo " + "alcanca.", aba: "Outros");
		Por("Chrono_Trigger", "Gatilho Cronico", Modo.Instantanea, "Crava um ponto no tempo guardando seu corpo como ele está agora: vida, energia, fôlego, " + "idade, lugar -- e ate se você estava vivo. Usar de novo devolve você aquele instante e " + "apaga o ponto. Isso desfaz ate a própria morte.");
		Por("Life_Suck", "Sugar a Vida", Modo.Instantanea, "Se alimenta da alma de alguém NOCAUTEADO e vivo ao seu lado. A energia maxima da vitima " + "vira energia sua na hora, e uma fatia do poder dela vira poder seu. Não mata -- mas o " + "corpo leva cinco minutos pra conseguir de novo.");
		Por("DirectSSJ", "Transformacao Direta", Modo.Instantanea, "Pula direto pra qualquer forma que você JA despertou, sem subir a escada degrau por " + "degrau. Não afrouxa nenhum requisito: a forma continua pedindo o poder e a maestria " + "de sempre. Mande sem numero pra ver o que está aberto.", aba: "Formas");
		Por("Magic_Words", "Palavras Magicas", Modo.Instantanea, "Grava ate dez frases que viram atalhos de fala. Mande sem argumento pra ver as suas, e " + "com numero e texto pra gravar. Depois de gravada, a frase sai com um comando so.", aba: "Outros");

		// ---- lote dos PROJETEIS: uma tecnica por tipo de voo ----
		// Elas sao as tres primeiras do jogo que criam uma entidade que VIAJA (ver
		// `Core/Combat/Projetil.cs`), e estao aqui pelo mesmo motivo das outras: a bancada de console
		// (`dotnet run -- efeitos`) nao carrega o servidor e contaria como nao-portadas.
		Por("Ki_Wave", "Onda de Ki", Modo.Sustentada,
			"Concentra Ki na mao e solta um RAIO continuo. Aperte uma vez pra carregar, de novo pra "
			+ "soltar, e de novo pra parar. Enquanto carrega e enquanto atira voce fica PLANTADO no "
			+ "lugar -- e o preco de um raio, e e por isso que ele e a arma de quem ja ganhou espaco.");

		Por("Basic_Blast", "Bola de Ki", Modo.Instantanea,
			"Uma esfera de energia solta na direcao em que voce olha. Sai na hora, nao prende voce e "
			+ "custa pouco -- em troca voa devagar e perde forca contra quem sabe se defender de Ki.");

		Por("Guided_Ball", "Esfera Teleguiada", Modo.Instantanea,
			"Uma esfera pesada que PERSEGUE o alvo marcado ate acerta-lo ou se apagar. Custa caro e "
			+ "e lenta, mas nao adianta sair da frente. Sem alvo marcado, ela voa reto e voce e "
			+ "avisado disso.");

		// ---- lote G5: O ARSENAL NOMEADO ----
		// Catorze folhas que uma skill ja concedia e que o servidor nao atendia. Seis delas sao
		// `datum/skill/rank/*` -- fechar este lote fecha metade da arvore de cargo. Ver
		// `Server/GameServer.Tecnicas.G5.cs`.
		Por("Masenko", "Masenko", Modo.Sustentada, "Um raio que sai FORTE e vai MORRENDO no caminho: cada tile viajado tira um pouco da potencia. E a arma de quem luta colado -- de longe ela chega fraca.");
		Por("Makkankosappo", "Makankosappo", Modo.Sustentada, "O avesso do Masenko: um raio fino que GANHA forca a cada tile e alcanca o dobro da distancia. Em compensacao demora seis vezes mais pra carregar -- e essa espera, na cara do inimigo, e o preco dele.");
		Por("Massive_Beam", "Raio Colossal", Modo.Sustentada, "Um raio LARGO, com o dobro do poder do seu proprio corpo por tras e quatro vezes a potencia de um raio comum. Custa quinze vezes mais Ki por segundo que o raio comum e viaja devagar: quem carrega isso esta apostando tudo num tiro.");
		Por("Final_Flash", "Final Flash", Modo.Sustentada, "Uma muralha de energia com QUATRO vezes o seu poder por tras dela, capaz de passar por cima de gente mais forte que voce. Quanto melhor a sua pericia de raio, mais longe e mais forte ela sai -- e mais absurdamente cara fica.");
		Por("Charged_Shot", "Tiro Carregado", Modo.Instantanea, "A Bola de Ki carregada: o dobro da potencia e um poder vinte por cento maior que o seu por tras dela. Em troca custa cinco vezes mais e trava seu proximo tiro por dois segundos.");
		Por("KillDriver", "Kill Driver", Modo.Instantanea, "Um disco de energia rapido que NAO DA PRA DEFLETIR e PARALISA quem acerta. O dano e quase nada -- o que ele faz e tirar as pernas do inimigo por ate dez segundos.");
		Por("BusterShell", "Buster Shell", Modo.Instantanea, "Quatro esferas soltas de uma vez, abrindo em leque. Nenhuma delas machuca muito sozinha; juntas cobrem a frente inteira e e dificil sair de todas.");
		Por("Scattershot", "Tiro Disperso", Modo.Instantanea, "Uma nuvem de bolas que nascem ESPALHADAS em volta de voce e depois convergem pra frente. Quantas saem depende da sua pericia de bola e de volei. Cara, e deixa toda a familia de barragem em espera.");
		Por("Energy_Barrage", "Barragem de Energia", Modo.Instantanea, "Mais bolas que o Tiro Disperso e mais baratas, todas retas pra frente. E a barragem do dia a dia: menos dano por bola, muito mais volume.");
		Por("Ki_Bomb", "Campo Minado de Ki", Modo.Instantanea, "Semeia bolas PARADAS em volta do alvo marcado e as deixa la por quatro segundos. Elas nao perseguem ninguem -- quem se machuca e quem tentar sair do cerco. Precisa de alvo a ate vinte tiles.");
		Por("Hellzone_Grenade", "Hellzone Grenade", Modo.Instantanea, "O cerco que FECHA: as bolas nascem em volta do alvo, ficam paradas UM SEGUNDO e entao convergem todas nele ao mesmo tempo. O dobro do dano do campo minado e mais de tres vezes o preco.");
		Por("Kienzan", "Kienzan", Modo.Instantanea, "Um disco de corte com dano fixo e ALTO, que persegue o alvo marcado por dois minutos inteiros. Nao se apaga sozinho como os outros tiros: ou acerta, ou bate em alguma coisa.");
		Por("Paralysis", "Paralisia", Modo.Instantanea, "Um tiro que quase nao machuca e que NAO DA PRA DEFLETIR: ele tranca as pernas de quem acerta por cinco a dez segundos. O alvo continua batendo e se defendendo -- so nao consegue mais fugir. Custa uma fortuna em Ki.");
		Por("Stunlock", "Stunlock", Modo.Instantanea, "A paralisia dos Metamorianos: mais barata que a Paralysis e com um poder um pouco menor por tras, com a mesma promessa -- nao da pra defletir, e as pernas param.");

		// ---- a que faltava no espelho ----
		// O `Planet_Destroy` e registrado em `Server/GameServer.Destruicao.cs` e NUNCA foi copiado
		// pra ca. Consequencia: o console do extrator (`dotnet run -- efeitos`), que nao carrega o
		// servidor, contava a unica tecnica so-de-vilao do jogo como NAO-PORTADA -- exatamente o
		// defeito que este arquivo existe pra evitar, e que o cabecalho dele descreve. Achado pelo
		// censo do lote G6.
		Por("Planet_Destroy", "Planet Destroy", Modo.Instantanea,
		"Concentra toda a sua energia sobre o planeta em que você está e o parte ao meio. "
		+ "Custa 1000 de Ki, exige BP expresso de 10.000 vezes a gravidade daqui, e leva trinta "
		+ "segundos de carga -- se você for nocauteado nesse meio-tempo, não acontece nada. "
		+ "Depois disso o planeta tem cinco minutos, e some do mapa PARA SEMPRE. Só um vilão.");

		// ---- lote G6: O KIT DOS CARGOS, o sopro e os buffs de Ki ----
		// Dezenove folhas, ONZE delas verbos que um kit de cargo ja entregava e que nao faziam nada.
		// Ver `Server/GameServer.Tecnicas.G6.cs`.
		Por("Kamehameha", "Kamehameha", Modo.Sustentada,
		"A onda da Escola da Tartaruga: o raio que ANDA MAIS LONGE de todos e que menos cobra "
		+ "por segundo. Quanto melhor a sua pericia de raio, mais forte ele sai -- e mais caro "
		+ "fica de sustentar.");
		Por("GalicGun", "Galick Ho", Modo.Sustentada,
		"A onda da realeza saiyajin. Irma do Kamehameha, um pouco mais forte e um pouco mais "
		+ "lenta -- e no topo da pericia ela alcanca cinquenta tiles, mais que qualquer outro "
		+ "raio do jogo.");
		Por("Death_Beam", "Death Beam", Modo.Sustentada,
		"Um fio de energia com CINCO vezes a potencia de um raio comum e alcance de dez tiles. "
		+ "E a tecnica de matar de perto: nao ha raio no jogo que concentre tanto dano em tao "
		+ "pouca distancia.");
		Por("Dodompa", "Dodon Ray", Modo.Sustentada,
		"O raio da Escola do Grou: quatro vezes a potencia de um raio comum, alcance longo e o "
		+ "voo mais rapido da familia. Em troca, e o mais caro de sustentar depois do Death "
		+ "Beam.");
		Por("Enkumei", "Enkumei", Modo.Sustentada,
		"A onda de fogo negro dos Namekuseijin. Ela nao e a mais forte, mas carrega mais poder "
		+ "por tras do feixe que o seu proprio corpo -- e quem domina a pericia de raio dobra "
		+ "esse poder.");
		Por("Boom_Wave", "Boom Wave", Modo.Sustentada,
		"Um raio CURTO e grosso, de cinco tiles, que quase nao custa nada por segundo: o preco "
		+ "dele cai conforme a sua pericia de Ki sobe. E a arma de quem luta colado e nao pode "
		+ "parar pra carregar um raio caro.");
		Por("Kikoho", "Kikoho", Modo.Instantanea,
		"A Tri-Bomba: uma esfera pesada paga com a PROPRIA VIDA, e nao so com energia. Cada "
		+ "uso seguido grita uma silaba (KI, KO, HO) e sai mais forte que o anterior -- e cobra "
		+ "mais sangue. Tres seguidos podem derrubar voce.");
		Por("Focus", "Foco", Modo.Sustentada,
		"Voce concentra a circulacao do proprio Ki: a ofensiva de energia sobe, e o gasto sobe "
		+ "na mesma medida. Quem treinou circulacao e buff ganha mais dos dois lados. Apertar "
		+ "de novo desliga.");
		Por("Efficiency", "Eficiencia", Modo.Sustentada,
		"O contrario do Foco: voce racionaliza o gasto e a energia dura MUITO mais, ao preco de "
		+ "uma parte da sua ofensiva de Ki. Apertar de novo desliga.");
		Por("Energy_Shield", "Escudo de Energia", Modo.Sustentada,
		"Uma casca que soma defesa de Ki E armadura de energia -- a armadura e o que aguenta "
		+ "tiro sem descontar da sua vida. Consome energia todo segundo e cai sozinha quando a "
		+ "armadura acaba.");
		Por("Full_Power", "Full Power", Modo.Sustentada,
		"A concentracao total da aura: forca fisica, ofensiva de Ki e velocidade sobem juntas. "
		+ "Custa energia por segundo, e o custo CAI conforme voce pratica. Cai sozinha quando "
		+ "sua energia chega perto do fim.");
		Por("Kiai", "Kiai", Modo.Instantanea,
		"Um grito de energia que ARREMESSA quem estiver na sua frente, sem tiro nenhum pra "
		+ "desviar. Se nao houver ninguem no caminho, o sopro vira uma lamina de ar que segue "
		+ "em frente.");
		Por("Shockwave", "Onda de Choque", Modo.Instantanea,
		"O sopro em VOLTA de voce: joga longe todo mundo colado e APAGA os tiros de energia que "
		+ "estiverem chegando, desde que voce tenha poder pra isso. E a resposta de quem esta "
		+ "cercado.");
		Por("Deflection", "Deflexao", Modo.Instantanea,
		"Voce empurra de volta o que vem voando na sua direcao. Por tres investidas seguidas, "
		+ "todo tiro na sua frente que for mais fraco que voce e DEVOLVIDO a quem atirou -- e "
		+ "passa a ser seu.");
		Por("Explosive_Roar", "Rugido Explosivo", Modo.Instantanea,
		"Duas fases: aperte pra comecar a juntar o rugido e aperte de novo pra soltar. Quanto "
		+ "mais tempo carregar, maior o raio e mais longe todo mundo voa -- mas passando de "
		+ "cinco segundos so o preco continua subindo.");
		Por("Wolf_Fang_Fist", "Punho da Presa do Lobo", Modo.Instantanea,
		"Tres socos secos em quem estiver colado, e o terceiro ARREMESSA. Custa folego alem de "
		+ "energia, e precisa de pelo menos uma mao livre.");
		Por("Wolf_Fang_Hurricane", "Furacao da Presa do Lobo", Modo.Instantanea,
		"A versao encadeada do Punho da Presa: quatro golpes AVANCANDO, cada um empurrando voce "
		+ "pra cima do alvo. Nenhum deles arremessa -- e essa e a graca, o alvo fica na sua "
		+ "frente ate o ultimo.");
		Por("Heal", "Curar", Modo.Sustentada,
		"Poe a mao em quem esta do seu lado e fecha as feridas dele com o seu Ki, continuamente. "
		+ "Cura mais quem tem mais pericia de Ki. Se o alvo se afastar, a cura para sozinha. "
		+ "Apertar de novo tambem para.");
		Por("Assess_Ki_Skill", "Avaliar o Ki", Modo.Instantanea,
		"Le a pericia de Ki de quem voce marcou. Um novato so sente quem e mais treinado; com "
		+ "percepcao media voce compara pericia por pericia; passando de cinquenta, voce le os "
		+ "numeros dele como se fosse a sua propria ficha.", aba: "Outros");

		// ============================ lote G7 -- E ELE FICOU UMA SESSAO INTEIRA DE FORA ============================
		// Estas dezesseis linhas nao existiam. O lote G7 registrou os dezesseis verbos no servidor,
		// a bancada `--punhoteste` deu 45/0 e o jogo os atendia -- e o console do extrator continuou
		// respondendo "52 com efeito portado" sobre um jogo com 68, porque ele so enxerga ESTE
		// arquivo. Ninguem viu, porque um relatorio que subconta nao quebra nada: ele so faz a divida
		// parecer maior, que e o jeito mais silencioso de um numero mentir.
		//
		// O buraco esta fechado do lado de fora agora: a `--catalogoteste` compara, toda rodada, o
		// que o servidor registra com o que este arquivo declara, nas DUAS direcoes. Fechar um lote
		// sem passar por aqui deixa de ser possivel em silencio.
		// =========================================================================================================

		// ---- boxe (`Physical Skills.dm:66,89,113,120`) ----
		Por("One_Two", "Um-Dois", Modo.Instantanea, "O basico do boxe: um jab pra medir e um cruzado por cima, mais forte. Voce da um passo pra frente antes do primeiro, entao pega quem esta a dois tiles.");
		Por("One_Two_Five", "Um-Dois-Cinco", Modo.Instantanea, "Jab, cruzado e uppercut, nessa ordem e cada um mais forte que o anterior. Custa metade a mais que o Um-Dois e o ultimo golpe e o que derruba.");
		Por("Two_One_Four", "Dois-Um-Quatro", Modo.Instantanea, "Cruzado, jab e uppercut -- a combinacao que comeca forte. Nao avanca: e pra quem JA esta colado e quer despejar tres golpes sem dar um passo.");
		Por("KO_Punch", "Soco de Nocaute", Modo.Instantanea, "Um uppercut so, com dano enorme somado. E o golpe mais caro do boxe e o unico que aposta tudo numa unica leitura -- se o outro bloquear, acabou.");

		// ---- chutes (`Physical Skills.dm:155,203,224`) ----
		Por("Dropkick", "Voadora", Modo.Instantanea, "Voce se lanca contra o alvo e acerta com os dois pes. Quanto MENOS chao voce precisar correr, mais forte ela chega. Se o alvo nao estiver no fim da corrida, voce cai sozinho e fica atordoado.");
		Por("Falling_Kick", "Chute Descendente", Modo.Instantanea, "Um chute pra baixo. Se o alvo estiver VOANDO, voce emenda um segundo golpe que o traz junto pro chao. Se errar, voce e quem se desequilibra.");
		Por("Kickup", "Chute Ascendente", Modo.Instantanea, "Um chute de baixo pra cima, com dano extra. Barato, rapido, e o pao com manteiga de quem luta de perna.");

		// ---- artes marciais (`Martial Skill Attacks.dm:241,325,340,355`) ----
		Por("Dash_Attack", "Investida", Modo.Instantanea, "Uma corrida longa terminada num golpe pesado -- ela alcanca MUITO mais longe que a Voadora e custa quase metade. O preco e o mesmo: chegar e nao achar ninguem deixa voce atordoado no lugar.");
		Por("Spin_Attack", "Ataque Giratorio", Modo.Instantanea, "Voce gira e acerta ate TRES pessoas coladas em voce de uma vez. Nao precisa de alvo marcado e nao anda: e a resposta de quem esta cercado de perto.");
		Por("Stun_Attack", "Golpe Atordoante", Modo.Instantanea, "Voce avanca ate dez tiles e acerta um golpe que deixa o alvo dois segundos e meio sem reagir. O dano nao e o ponto -- o silencio depois dele e.");
		Por("Takedown", "Derrubada", Modo.Instantanea, "Voce agarra o alvo pelo tronco e o poe no chao. Contra alguem VOANDO ela e devastadora: tira o voo, bate duas vezes e deixa tres segundos e meio de atordoamento.");

		// ---- a investida de Ki (`speedy.dm:178`) ----
		Por("Lariat", "Lariat", Modo.Instantanea, "Voce acende o Ki e se lanca contra o alvo marcado, de ate trinta e cinco tiles, terminando com um ombro no peito dele. Custa quase nada -- e quanto mais forte e tecnico voce for, MENOS custa.");

		// ---- assassino (`Assassain Skills.dm:122,141`) ----
		Por("Cutthroat", "Degola", Modo.Instantanea, "Um corte curto que vale muito mais quando o alvo AINDA NAO ESTA EM COMBATE. Contra quem ja esta lutando e um golpe comum e caro. Deixa seus golpes especiais em espera por dois segundos e meio.");
		Por("Backstab", "Punhalada", Modo.Instantanea, "Vale pelo lugar de onde sai: se voce estiver olhando PRO MESMO LADO que o alvo -- ou seja, nas costas dele --, o golpe crita. Fora de combate ele soma ainda mais. Tres segundos de espera depois.");

		// ---- as duas bolas (`blasts.dm:530`, `Core Trees/Spirit.dm:344`) ----
		Por("Scattering_Bullet", "Bala Dispersa", Modo.Instantanea, "Uma nuvem de esferas que nasce ESPALHADA em volta de voce, fica um instante no ar e entao converge toda no alvo marcado, de qualquer angulo. Quantas saem depende da sua pericia de Ki e da sua forca. Precisa de alvo a ate trinta tiles.");
		Por("Spirit_Gun", "Spirit Gun", Modo.Instantanea, "Uma bala de espirito disparada do dedo. Ela NAO gasta energia: gasta FOLEGO -- e por isso sai quando o Ki ja acabou. Treinar a arvore do Espirito a deixa mais barata e mais forte ao mesmo tempo.");

		// ---- lote G8: OS VERBOS MUDOS DOS CARGOS ----
		// Seis verbos que um kit de cargo ja entregava e que nao faziam nada, e TRES deles estavam
		// catalogados como dependentes de sistema que eles nao usam (ver o cabecalho do
		// `Server/GameServer.Tecnicas.G8.cs`). Nenhum inventa entidade nem sistema novo.
		Por("Dead", "Ver os Mortos", Modo.Instantanea, "Lista quem, no mundo agora, esta morto. E o servico de quem cuida do Outro Mundo -- e, como o proprio jogo antigo brinca, tambem da pra so olhar a aureola.", aba: "Outros");
		Por("Go_To_Heaven_Or_Hell", "Ir ao Ceu ou ao Inferno", Modo.Instantanea, "O caminho do juiz do Outro Mundo: leva VOCE ao Ceu ou ao Inferno, na hora. Mande sem destino pra ver os dois.", aba: "Outros");
		Por("Holy_Shortcut", "Atalho Sagrado", Modo.Instantanea, "O atalho secreto entre o Reino Divino e Arconia. Cobra METADE da sua energia e leva junto quem estiver colado em voce. So sai com o corpo parado e inteiro.", aba: "Outros");
		Por("Detect_Shard", "Detectar a Esmeralda", Modo.Instantanea, "Tenta sentir a energia da Esmeralda Mestra. Ela nao existe mais -- e no jogo antigo o poder tambem nao fazia nada alem de dizer isso.", aba: "Outros");
		Por("Keep_Body", "Manter o Corpo", Modo.Instantanea, "Liga (e desliga) em quem voce marcou o direito de ficar com o proprio corpo depois de morto: em vez de sumir pro Outro Mundo, o corpo fica onde caiu enquanto houver energia -- com a aureola acesa, e ao alcance de quem quiser ressuscita-lo.", aba: "Outros");
		Por("Restore_Youth", "Restaurar a Juventude", Modo.Instantanea, "Oferece a quem voce marcou a idade que voce escolher, de 0 a 25 anos. E uma OFERTA: so vale se a pessoa aceitar. Use Restore_Youth:<idade>.", aba: "Outros");

		// ---- lote G9: O SELO, E O SIGILO PELO LADO DE QUEM ESCONDE ----
		// As duas primeiras SO PASSARAM A SER EXIGIVEIS quando o extrator parou de perder o
		// `after_learn` delas (ver `DmSkillScanner.CorposDeAprendizado`): ate entao saiam do
		// `skills.json` com `verbos: []` e o painel do cargo as anunciava como entregues.
		// As duas ultimas fecham o `SistSigilo`, que era o unico sistema do censo em que o port
		// tinha o lado de quem LE e nao o de quem ESCREVE.
		Por("Mafuba", "Mafuba", Modo.Instantanea, "A Onda Selante. Precisa de um Pote Selante assentado por perto: a fita sai atras do alvo e o prende no pote pra sempre. Custa 90 de dano em CADA membro seu -- pode te matar. Quem quiser o preso de volta tem que quebrar o pote.", aba: "Outros");
		Por("Open_Dead_Zone", "Abrir a Dead Zone", Modo.Instantanea,
		"Rasga a realidade cinco tiles ao norte e abre a Dead Zone por dez segundos. Ela puxa "
		+ "quem estiver por perto e sela quem cair dentro -- sem pote, sem quebrar: só sai "
		+ "quem ficar 25% mais forte do que você era na hora. Custa quase toda a sua energia.",
		aba: "Outros");
		Por("Conceal_Power", "Ocultar o Poder", Modo.Sustentada, "Esconde o seu poder de quem olha: o scouter dos outros passa a ler quase nada. Liga e desliga, com cinco segundos de espera entre um e outro.");
		Por("Power_Control", "Controle de Poder", Modo.Sustentada,
		"Segura o seu próprio poder numa porcentagem, de 1 a 100. Só serve pra BAIXAR -- pra "
		+ "voltar a subir é carregando (tecla C). Use Power_Control:40.", aba: "Skills");

		// ---- lote G10: OS GOLPES DO MOLDE DO G7 QUE O CENSO ACHOU MUDOS, e a Trindade ----
		// Dezenove verbos que a arvore ja concedia (dezesseis por DEGRAU) e o servidor nao atendia,
		// mais os tres da Trindade. Ver `Server/GameServer.Tecnicas.G10.cs`.
		Por("Shock", "Choque", Modo.Instantanea, "Um golpe curto que deixa energia presa no corpo do alvo: ele perde vida no membro mirado na hora e de novo um segundo e meio depois, sem rolagem nenhuma.");
		Por("Reverb", "Reverberacao", Modo.Instantanea, "Um punho carregado de energia que ECOA: tres ondas de dano espalhado pelo corpo inteiro do alvo, uma a cada dois segundos, depois do golpe.");
		Por("Precise_Explosion", "Explosao Precisa", Modo.Instantanea, "Voce crava um dedo no membro mirado e, dois segundos depois, ele estoura por dentro: setenta de dano mais a sua forca e a sua tecnica, direto no membro.");
		Por("Hokuto_Hyakuretsu_Ken", "Hokuto Hyakuretsu Ken", Modo.Instantanea, "ATATATATATA. Custa folego E energia, e deixa o alvo DEZ segundos sem reagir. Exige estar colado e com a mao livre; os golpes especiais de corpo ficam em espera depois.");
		Por("Trip", "Rasteira", Modo.Instantanea, "Uma rasteira suja: se o alvo estiver NO CHAO, ele fica tres segundos sem reagir e leva um pouco de dano em cada membro. Contra quem voa nao ha chao pra tropecar.");
		Por("Revenge_Demon", "Demonio da Vinganca", Modo.Instantanea, "Um soco e um jab na cara, e o alvo e ARREMESSADO pra frente. Se o primeiro golpe nao entrar, voce e quem se desequilibra.");
		Por("Gigantic_Spike", "Espigao Gigante", Modo.Instantanea, "Com alguem AGARRADO (se so estiver seguro, voce o levanta na hora), voce corre pra frente derrubando o que houver no caminho e esmaga quem carrega no fim -- quanto mais parede, mais forte. O chao em volta racha.");
		Por("Power_Drag", "Arrasto Brutal", Modo.Instantanea, "Com alguem CARREGADO, voce dispara pra frente arrastando o corpo dele pelo chao por varios tiles, e ele sai machucado do arrasto.");
		Por("Seismic_Press", "Prensa Sismica", Modo.Instantanea, "Um golpe pesado que deixa o alvo dois segundos sem reagir e RACHA o chao num raio igual a sua forca.");
		Por("Clench", "Aperto", Modo.Instantanea, "Aperta quem esta nos seus bracos (+4 de dano) e desfaz quatro pontos do que ele ja tinha lutado pra escapar. Se nao houver ninguem agarrado, agarra quem estiver na frente.");
		Por("Hold", "Chave", Modo.Instantanea, "Uma chave em quem esta agarrado: tira quinze pontos da luta dele pra escapar e o deixa CINCO segundos sem reagir.");
		Por("Power_Slam", "Power Slam", Modo.Instantanea, "Levanta e ESMAGA quem esta agarrado no chao: o golpe mais forte da luta livre (+10).");
		Por("Suplex", "Suplex", Modo.Instantanea, "O suplex: dano somado em quem esta agarrado e dois segundos sem reagir depois.");
		Por("Rapid_Movement", "Movimento Rapido", Modo.Instantanea, "Bombeia Ki nas pernas e avanca tres tiles contra o alvo MARCADO (ate vinte tiles). Nao bate: e o passo de quem quer fechar a distancia. Custa menos quanto mais rapido voce e.");
		Por("Zanzoken_Dash", "Investida Zanzoken", Modo.Instantanea, "A mesma corrida do Movimento Rapido. No jogo antigo ela prometia rodear o inimigo depois de chegar, e a promessa nunca foi cumprida: e o mesmo avanco de tres tiles.");
		Por("Zanzoken_Combo", "Zanzoken Combo", Modo.Instantanea, "Voce some e reaparece ATRAS do alvo, olhando pra ele -- e o proximo soco e seu. O alcance cresce com a sua pericia de Ki e a sua velocidade. Nao ha espaco atras dele? Voce fica.");
		Por("Zanzoken_Rush", "Zanzoken Rush", Modo.Instantanea, "Aparece ao lado do alvo e golpeia, varias vezes seguidas -- quantas depende da sua velocidade e de quanto voce treinou a tecnica. Depois vem a exaustao, que dura vinte vezes o intervalo entre os saltos.");
		Por("Taunt", "Provocacao", Modo.Instantanea, "Voce xinga alto e todo mundo por perto que ja estava lutando com alguem passa a lutar com VOCE -- quanto menos vontade a pessoa tem, mais facil ela cai.");
		Por("Counter_Taunt", "Contra-Provocacao", Modo.Instantanea, "Uma resposta atravessada que machuca: o seu alvo marcado leva dano MENTAL, um quarto de um golpe, sem poder desviar.");
		Por("Slap", "Tapa", Modo.Instantanea, "Voce bate na propria bunda e todo mundo por perto que estava lutando fica um segundo e meio sem reagir, de tao pasmo.");

		// ---- lote G12: OS PROJETEIS QUE FALTAVAM E OS SISTEMAS PEQUENOS ----
		// Onze verbos do censo de 2026-09-02 (tabela 1, familia F2). A Precognicao nao esta aqui porque
		// nao tem verbo (e um `effector()`). Ver `Server/GameServer.Tecnicas.G12.cs`.
		Por("Death_Ball", "Death Ball", Modo.Sustentada, "Uma esfera pesada que voce forma sobre a cabeca por ate quatro estagios de 1,5 s (cada um come um terco do custo de novo), e depois GUIA com o proprio olhar. Aperte de novo pra largar a guia; apertando uma terceira vez durante a carga ela sai na hora. Custa 150x o dreno-base e prende voce no lugar enquanto durar.");
		Por("BusterBarrage", "Buster Barrage", Modo.Sustentada, "Liga e voce passa a cuspir duas esferas por ciclo em direcoes ALEATORIAS, ate desligar, cair ou ficar sem energia. Cada ciclo custa um dreno-base. Nao prende voce no lugar.");
		Por("Continuous_Energy_Bullets", "Balas Continuas de Energia", Modo.Sustentada, "Uma rajada sem fim de esferas pra frente, dez por segundo, enquanto voce segurar -- plantado no lugar. A primeira custa pouco; cada esfera seguinte custa DEZ vezes mais, e a rajada para sozinha quando a energia nao paga a proxima. Cinco segundos de espera depois.");
		Por("Spin_Blast", "Rajada Giratoria", Modo.Sustentada, "A irma mais cara das Balas Continuas: vinte esferas por segundo nascendo em volta de voce e saindo em TODAS as oito direcoes, um pouco mais fortes. Oito segundos de espera depois.");
		Por("SpiritBomb", "Genkidama", Modo.Sustentada, "Custa 90% do seu Ki maximo. A esfera se forma sobre voce em 3 s e passa a CRESCER em pulsos de 1,5 s; quem estiver MEDITANDO no mesmo mundo pode doar um decimo do proprio Ki e engorda-la mais. Aperte de novo pra atirar: ela sai 2 s depois, na direcao do seu olhar, e voce fica preso ate 3 s depois do disparo.");
		Por("Soul_Absorb", "Absorver a Alma", Modo.Instantanea, "Arranca a alma de alguem NOCAUTEADO e vivo ao seu lado: a energia dele vira sua, uma parte do poder dele fica com voce, e ele morre um segundo depois. Cada alma so pode ser tomada uma vez, e o mesmo corpo nao pode ser absorvido de novo por cinco minutos.");
		Por("Absorb_Android", "Dreno de Energia", Modo.Sustentada, "Contra um Androide caido: absorve-o inteiro. Contra qualquer outro caido: comeca a DRENAR a energia dele, um decimo a cada 0,7 s, enquanto ele estiver colado em voce -- e ele morre se a energia cair abaixo de um decimo. Aperte de novo, leve um golpe ou caia e o dreno para.");
		Por("Imitation", "Imitacao", Modo.Sustentada, "Copia o nome e a aparencia inteira de quem voce marcou (ou do mais proximo, a ate cinco tiles) -- pra todo mundo que olhar. Aperte de novo pra voltar a ser voce.");
		Por("SplitForm", "Divisao do Corpo", Modo.Instantanea, "Cria uma copia sua com METADE do seu poder expresso, que obedece aos verbos Split Form da aba Other (seguir, parar, atacar o alvo, atacar o mais perto, desfazer). Custa metade do Ki maximo dividido pela sua pericia de divisao, e cada copia viva abaixa o seu proprio poder. Ela some sozinha em 100 s, ou quando cai.");
		Por("Grow_Senzu_Bean", "Cultivar Senzu", Modo.Instantanea, "Comeca a cultivar uma Semente Senzu: um minuto depois ela aparece na sua mochila. Uma de cada vez.", aba: "Outros");
		Por("Ki_Targets", "Alvos de Ki", Modo.Sustentada, "Voce entra em meditacao e, a cada 3,5 s, uma esfera de treino nasce ate quatro tiles de voce e vaga por cinco segundos. SOQUE-A (rende ganho de treino). Aperte de novo, ou pare de meditar, pra encerrar.");

		// ---- lote G11: "UMA FUNCAO SOBRE PECA EXISTENTE" ----
		// Os catorze verbs das skills que ja estavam na arvore e nao tinham efeito, cada um pendurado
		// numa peca que o port ja tinha (buffs com prazo, salto de zona, paralisia, agarrao, a Final
		// Explosion). Ver `Server/GameServer.Tecnicas.G11.cs`.
		Por("Sneak", "Sneak", Modo.Instantanea, "A furtividade do assassino: voce some da vista por alguns instantes (um segundo mais um decimo por ponto de Tecnica). Custa Ki e deixa os golpes especiais em espera por seis segundos. Nao funciona se voce ja estiver invisivel.");
		Por("Expand_Body", "Expansao do Corpo", Modo.Sustentada, "Infla os musculos com Ki: ofensiva e defesa fisica sobem por grau, e a velocidade cai. Use Expand_Body:1, :2 ou :3 (e :0 pra relaxar). Cada grau custa uma mordida de Ki e o corpo continua pagando aos poucos; com Ki quase zerado ele relaxa sozinho. Ocupa o mesmo lugar dos buffs de corpo e nao convive com as laminas de Ki.");
		Por("Majin", "Majin", Modo.Sustentada, "Canaliza os proprios demonios na forma Majin: um pedaco do seu poder vira soma fixa, a ofensiva fisica sobe 30%, o Ki regenera mais rapido e a raiva sobe mais devagar. Aperte de novo pra voltar ao normal.");
		Por("Shackle", "Grilhao", Modo.Instantanea, "Prende as pernas de quem voce marcou com uma aura de interferencia: a velocidade dele cai por alguns segundos (quanto melhor a sua pericia de debuff, menos ela cai, mas mais tempo dura). Compartilha a espera com a Paralysis e o Solar Flare.");
		Por("Devil_Bringer", "Devil Bringer", Modo.Instantanea, "Rasga um buraco na realidade e some -- levando junto quem estiver colado em voce. Exige Ki CHEIO e leva TODO ele. Um poder demoniaco: alcanca o Inferno, mas nunca o Ceu. Mande sem destino pra ver aonde ele chega.", aba: "Outros");
		Por("Kai_Kai", "Kai Kai", Modo.Instantanea, "O teleporte dos Kaioshin: voce grita 'Kai Kai!' e aparece em outro mundo, levando quem estiver colado em voce. Exige Ki CHEIO e leva TODO ele. Alcanca ate o Ceu. Mande sem destino pra ver a lista.", aba: "Outros");
		Por("Instant_Transmission", "Teletransporte", Modo.Instantanea, "Sente uma assinatura de Ki e se desmonta ate ela. Mande sem argumento pra ver quem da pra sentir (conhecidos pelo nome, desconhecidos pela assinatura); depois Instant_Transmission:<nome>. Voce precisa ficar PARADO enquanto se concentra; quem estiver colado vai junto. Custa quase toda a energia e vai ficando mais facil com a distancia percorrida.", aba: "Outros");
		Por("Flip", "Cambalhota", Modo.Instantanea, "A esquiva do gato: preso num agarrao, voce tenta se soltar com uma cambalhota. A chance cresce com a sua ofensiva e o seu poder contra a forca de quem segura, e com o quanto voce ja se debateu. Escapar machuca quem te segurava. Custa Ki mesmo se falhar.");
		Por("Self_Destruct", "Autodestruicao", Modo.Instantanea, "So com alguem AGARRADO. O primeiro aperto comeca a juntar energia (voce fica preso no lugar e o poder cresce a cada dois segundos e meio); o segundo detona: quem esta nos seus bracos leva a explosao inteira, quem esta a ate tres tiles leva uma parte -- e voce leva a mesma explosao. Carregando alem de vinte, 75% de chance de MORRER junto.");
		Por("Psycho_Thread", "Fio Psiquico", Modo.Sustentada, "Liga e desliga os fios dos Herans. Com eles ligados, o duplo clique no chao deixa de ser Zanzoken e passa a ARMAR um fio de paralisia embaixo dos seus pes por cinco segundos: quem pisar nele fica com as pernas trancadas. Custa cem vezes o seu dreno-base por fio.");
		Por("Freeze", "Congelar o Tempo", Modo.Instantanea, "Congela todo mundo a vista por alguns segundos (mais tempo quanto maior a sua pericia de Ki, menos quanto mais forte o alvo). Exige mais de um quarto do Ki -- e METADE do que voce tem vai embora, uma vez so, se alguem for congelado.");
		Por("Observe", "Observar", Modo.Instantanea, "Projeta a mente ate alguem e sente onde ele esta e o que o cerca: o mundo, a hora, a condicao dele e quem esta por perto. Nao alcanca quem esconde o poder, Androides nem energia fraca demais. Observe:<nome>; Observe sem nome solta.", aba: "Outros");
		Por("Unlock_Potential", "Despertar o Potencial", Modo.Instantanea, "O ritual do Anciao: oferece a quem esta marcado ao seu lado (ou a voce mesmo) despertar o potencial adormecido -- UMA vez na vida. O poder base sobe uma fracao por ponto de Potencial da raca, e o potencial acumulado por idade e treino vira poder na hora. Quem recebe precisa aceitar na aba Other.", aba: "Outros");
		Por("Give_Power", "Dar Poder", Modo.Sustentada, "Transfere a sua energia pra quem esta marcado ou perto: um por cento do seu Ki maximo a cada quinto de segundo, curando um pouco a cada dose. Quando a energia acaba (ou voce para), voce DESMAIA. Aperte de novo pra parar.");

		// ---- lote G13: O SISTEMA DE ESTUDO da arvore "Strength of Mind" ----
		// Os tres verbs que faltavam das dezessete skills da Mente. Os tres mexem no MESMO lugar --
		// o banco de exp adiantado de cada skill (`NiveisDeSkill.Progresso.Buffer`).
		// Ver `Server/GameServer.Tecnicas.G13.cs`.
		Por("Study_Other", "Estudar Outro", Modo.Sustentada,
			"Fica observando alguem a ate dez tiles e aprende com o que ve: a cada segundo, toda "
			+ "habilidade da Mente que a pessoa tem num nivel MAIS ALTO que o seu adianta o seu "
			+ "progresso nela. Study_Other:<nome> comeca; apertar de novo para. Perder o alvo de "
			+ "vista tambem para. Com uma habilidade em FOCO o ganho e dez vezes maior -- e so nela.",
			aba: "Outros");

		Por("Focus_Skill", "Focar Habilidade", Modo.Instantanea,
			"Escolhe em qual habilidade da Mente o seu estudo vai se concentrar. Focus_Skill sem nome "
			+ "lista as suas e diz qual esta escolhida; Focus_Skill:nenhuma solta o foco e volta a "
			+ "aprender um pouco de tudo.",
			aba: "Outros");

		Por("Write_Teachings", "Escrever Ensinamentos", Modo.Sustentada,
			"Escreve um livro sobre uma habilidade da Mente que voce ja subiu. So se escreve "
			+ "MEDITANDO, e leva um minuto por nivel que voce tem nela. O livro fica na sua mochila e "
			+ "ensina qualquer um que ja saiba aquela habilidade e ainda esteja na METADE do seu "
			+ "nivel ou abaixo -- some ao ser lido. Write_Teachings:parar desiste e perde o escrito.",
			aba: "Outros");
	}
}
