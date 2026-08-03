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
	}
}
