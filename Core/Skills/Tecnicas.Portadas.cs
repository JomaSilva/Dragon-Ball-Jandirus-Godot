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
/// GERADO das chamadas `Tecnicas.Registrar(...)` dos quatro lotes. Ao portar uma tecnica nova,
/// registre no lote como sempre e acrescente a linha aqui.
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
		Por("Planet_Destroy", "Destruir Planeta", Modo.Instantanea,
			"Trinta segundos de carga publica e o mundo inteiro deixa de existir. So vilao consegue.");

		// ---- lote G6: O KIT DOS CARGOS, o sopro e os buffs de Ki ----
		// Dezenove folhas, ONZE delas verbos que um kit de cargo ja entregava e que nao faziam nada.
		// Ver `Server/GameServer.Tecnicas.G6.cs`.
		Por("Kamehameha", "Kamehameha", Modo.Sustentada, "A onda da Escola da Tartaruga: o raio que anda mais longe de todos e que menos cobra por segundo. Quanto melhor a sua pericia de raio, mais forte ele sai -- e mais caro fica de sustentar.");
		Por("GalicGun", "Galick Ho", Modo.Sustentada, "A onda da realeza saiyajin. Irma do Kamehameha, um pouco mais forte e um pouco mais lenta -- e no topo da pericia ela alcanca cinquenta tiles.");
		Por("Death_Beam", "Death Beam", Modo.Sustentada, "Um fio de energia com cinco vezes a potencia de um raio comum e alcance de dez tiles. E a tecnica de matar de perto.");
		Por("Dodompa", "Dodon Ray", Modo.Sustentada, "O raio da Escola do Grou: quatro vezes a potencia de um raio comum, alcance longo e o voo mais rapido da familia.");
		Por("Enkumei", "Enkumei", Modo.Sustentada, "A onda de fogo negro dos Namekuseijin. Carrega mais poder por tras do feixe que o seu proprio corpo -- e quem domina a pericia de raio dobra esse poder.");
		Por("Boom_Wave", "Boom Wave", Modo.Sustentada, "Um raio curto e grosso, de cinco tiles, que quase nao custa nada por segundo: o preco cai conforme a sua pericia de Ki sobe.");
		Por("Kikoho", "Kikoho", Modo.Instantanea, "A Tri-Bomba: uma esfera pesada paga com a PROPRIA VIDA. Cada uso seguido grita uma silaba (KI, KO, HO) e sai mais forte -- e cobra mais sangue.");
		Por("Focus", "Foco", Modo.Sustentada, "Voce concentra a circulacao do proprio Ki: a ofensiva de energia sobe, e o gasto sobe na mesma medida.");
		Por("Efficiency", "Eficiencia", Modo.Sustentada, "O contrario do Foco: a energia dura muito mais, ao preco de uma parte da sua ofensiva de Ki.");
		Por("Energy_Shield", "Escudo de Energia", Modo.Sustentada, "Uma casca que soma defesa de Ki E armadura de energia -- a armadura e o que aguenta tiro sem descontar da vida.");
		Por("Full_Power", "Full Power", Modo.Sustentada, "A concentracao total da aura: forca, Ki e velocidade sobem juntas, e o custo por segundo CAI conforme voce pratica.");
		Por("Kiai", "Kiai", Modo.Instantanea, "Um grito de energia que arremessa quem estiver na sua frente, sem tiro nenhum pra desviar.");
		Por("Shockwave", "Onda de Choque", Modo.Instantanea, "O sopro em volta de voce: joga longe todo mundo colado e apaga os tiros de energia que estiverem chegando.");
		Por("Deflection", "Deflexao", Modo.Instantanea, "Todo tiro na sua frente que for mais fraco que voce e DEVOLVIDO a quem atirou -- e passa a ser seu.");
		Por("Explosive_Roar", "Rugido Explosivo", Modo.Instantanea, "Aperte pra juntar o rugido e aperte de novo pra soltar. Quanto mais tempo carregar, maior o raio e mais longe todo mundo voa.");
		Por("Wolf_Fang_Fist", "Punho da Presa do Lobo", Modo.Instantanea, "Tres socos secos em quem estiver colado, e o terceiro arremessa. Custa folego alem de energia.");
		Por("Wolf_Fang_Hurricane", "Furacao da Presa do Lobo", Modo.Instantanea, "Quatro golpes avancando, cada um empurrando voce pra cima do alvo. Nenhum deles arremessa -- e essa e a graca.");
		Por("Heal", "Curar", Modo.Sustentada, "Poe a mao em quem esta do seu lado e fecha as feridas dele com o seu Ki, continuamente.");
		Por("Assess_Ki_Skill", "Avaliar o Ki", Modo.Instantanea, "Le a pericia de Ki de quem voce marcou. Quanto maior a sua percepcao, mais detalhe voce enxerga.", aba: "Outros");

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
	}
}
