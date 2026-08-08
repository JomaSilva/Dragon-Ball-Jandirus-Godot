using Godot;

// A VARREDURA DO CATALOGO le `FormaDef` e `LinhaDeForma` dezenas de vezes; por extenso, o prefixo
// esconderia a propria regra dentro dele. O `Catalogo` continua escrito completo (como no
// `RoboDeForma`, e pelo mesmo motivo la): e o nome mais generico do lote e o unico que confunde.
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA NEBULOSA DO ULTRA INSTINTO (`--diagnebulosa`). Ela existe pra TIRAR A FOTO.
///
/// ============================ POR QUE ELA NAO E MAIS UM PASSO DO `--diagforma` ============================
/// O `RoboDeForma` mede o CATALOGO e o TEXTO: ele confere que o shader carregou, que os uniforms
/// existem e que o `Posar` pinta. Nada disso responde a unica pergunta que esta arte tem -- "parece
/// com a referencia?" --, e o `Posar` de la nem passa pelo servidor: e pintura direta no node.
///
/// Esta bancada faz o contrario. Ela nao pinta nada: manda `admin_forma ui_sign` pela rede, espera a
/// cinematica, e so entao pergunta ao node o que ele RECEBEU. E o unico jeito de provar as tres
/// coisas que a validacao fora do jogo (projeto descartavel, GPU real) nao provava -- que o node
/// nasce, que o `Definir` chega pelo caminho de producao, e que a ordem de irmao poe a nuvem atras.
/// ==========================================================================================================
///
/// ============================ E POR QUE ELA MEDE A IMAGEM, E NAO SO SALVA ============================
/// Duas correcoes desta arte sairam de MEDIR pixel, nao de olhar: "branco puro saindo cinza 148" e
/// "o ciano-claro estava atras do boneco" sao invisiveis a olho nu num sprite de 32 px. Entao cada
/// foto sai com um laudo ao lado (pixel mais claro, quantos brancos puros, a cor media do anel da
/// nuvem e a do fundo). O laudo nao substitui abrir o PNG -- ele diz ONDE olhar.
/// ====================================================================================================
///
/// ============================ O QUE ELA COBRE, E COM QUAL CONTROLE ============================
/// Sete perguntas, e cada uma com a sua rede contra o verde vazio (a regra da casa: *um teto que
/// nunca dispara e indistinguivel de nao ter teto*):
///
///   1. O overlay ACENDE nas duas formas de UI  -> `DepoisDeVestir`, pela REDE, nao por pintura.
///   2. E **so** nelas                          -> `Varrer` no catalogo + `ANebulosaSoEDoUltraInstinto`
///                                                 no corpo. Controle: tres predicados DEFEITUOSOS
///                                                 passam pela mesma varredura e tem que reprovar.
///   3. As duas usam o MESMO overlay            -> `NebulosaDaForma.AssinaturaDeTeste`, uniform a
///                                                 uniform. Controle: as duas `Ordem` diferem, entao
///                                                 havia por onde ramificar.
///   4. O shader esta LIGADO, nao so escrito    -> `Faltando`, perguntando ao COMPILADO. Controle:
///                                                 dois shaders quebrados de proposito reprovam.
///   5. O overlay nao depende do Ki             -> aceso a 118% e a ~50%.
///   6. A chama de carga CONTINUA dependendo    -> acesa a 118%, apagada com o C solto. E ela e o
///                                                 controle da linha 5: a mesma medicao mudou.
///   7. As particulas existem e afinam          -> quatro quadros contando branco puro, girando
///                                                 `pontos_brilho` e `pontos_densidade`. Controle: os
///                                                 dois jeitos de apaga-las tem que medir igual.
///   8. A FOLGA CABE NOS 5 px DO DONO           -> `AFolgaNaImagem`, por DUAS subtracoes de fotos
///                                                 consecutivas: uma da o efeito, a outra da o
///                                                 personagem INTEIRO (cabelo e rabo inclusos).
///                                                 Controle -- o chao que um teto nao tem: a mesma
///                                                 medida cobra que o efeito repinte >=80% do
///                                                 personagem. Uma nuvem encolhida a nada, ou
///                                                 desenhada ATRAS do corpo, passa em "menor que 5"
///                                                 e reprova ali.
///   9. E ela SOME atras do cenario             -> `AcharUmaArvore` + `OCenarioTapaANuvem`: o robo
///                                                 acha uma copa no tilemap, anda ate ela e mede
///                                                 quanta AREA a nuvem perdeu. Controle: o boneco
///                                                 tem que ter perdido area tambem -- senao nao
///                                                 havia nada na frente e o verde seria vazio.
///  10. A folga e DERIVADA do boneco           -> `AFolgaEDerivada`: a bancada remede a caixa do
///                                                 ALFA da folha e cobra que o shader tenha
///                                                 recebido o mesmo numero -- um lado cravado no
///                                                 `.cs` reprova. Mais a mesma regra rodada numa
///                                                 folha de 96 (a do Oozaru). Controle: tres
///                                                 defeitos injetados em `AFolgaCabe` -- quad que
///                                                 sobra, folga de 20 px e folga zero.
///  11. E a pilha tem DUAS pontas               -> `ANuvemEAIrmaMaisNova`: irma mais nova (por cima
///                                                 do corpo) E `ZIndex` zero (atras do cenario), na
///                                                 mesma conta. Controle: o robo empurra a nuvem
///                                                 pra posicao 0 -- a ordem ANTIGA -- e cobra que a
///                                                 mesma funcao reprove.
/// ==========================================================================================
///
/// TETO CONHECIDO: este node so nasce depois de ENTRAR NO MUNDO (`Boot.cs:455`). Uma rodada que nem
/// loga (nome em uso, conta recusada) nao entrega nem os dois blocos puros, que nao precisariam de
/// mundo nenhum. Ja aconteceu -- e a conta e o nome tem que ser ineditos.
///
/// COMO RODAR (precisa de janela: no headless o `GetImage` volta vazio e nao ha foto):
///     Godot --path . --host --kiteste --bpteste 3000000 --diagnebulosa \
///           --raca Saiyan --nome Zx --conta &lt;NOVA&gt; --horateste 0.5
///
/// O `--horateste` e o que da os DOIS FUNDOS pedidos, e ele muda de verdade o que esta atras do
/// efeito: 0.5 e meio-dia (o `CanvasModulate` perto do branco, chao claro) e 0.0 e meia-noite. Rodar
/// as duas vezes e comparar os dois laudos e o teste de "branco puro some em fundo claro".
/// </summary>
public partial class RoboDeNebulosa : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	/// <summary>Quanto o passo CORRENTE espera antes de rodar. Escrito pelo passo anterior.</summary>
	private double _espera = 1.5;

	/// <summary>Quantas vezes um passo que faz enquete ja se repetiu. Ver o passo do Ki a 50%.</summary>
	private int _voltas;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	/// <summary>Quantas checagens passaram. O placar precisa dos DOIS numeros, nao so das falhas.</summary>
	private int _oks;

	private static GameClient? C => GameClient.Instance;

	private Node2D? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as Node2D;

	private void Conferir(bool ok, string oque)
	{
		Nota((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (ok) _oks++; else _falhas.Add(oque);
	}

	/// <summary>
	/// Guarda a linha E A IMPRIME NA HORA. As duas coisas, e a segunda nasceu de uma rodada perdida:
	/// esta bancada leva uns tres minutos (duas cinematicas de 22 s so pra queimar a estreia) e ela
	/// nao sai sozinha -- quem a roda a mata por prazo. Guardando tudo pra imprimir no fim, uma
	/// rodada morta um segundo cedo demais nao dizia NADA sobre os vinte passos que ja tinham corrido.
	/// </summary>
	private void Nota(string linha)
	{
		_passos.Add(linha);
		GD.Print("[nebulosa] " + linha);
	}

	/// <summary>A razao de Ki que o CLIENTE conhece (a mesma que a barra do HUD desenha).</summary>
	private static double RazaoDeKi =>
		C?.Sheet is { MaxKi: > 0 } s ? s.Ki / s.MaxKi : double.NaN;

	public override void _Process(double delta)
	{
		if (_acabou) return;

		// ============================ O QUE NAO PRECISA DE MUNDO SAI PRIMEIRO ============================
		// O bloco do catalogo e o do shader sao PUROS: eles respondem lendo o `Catalogo` e o `.gdshader`,
		// e nao dependem de haver servidor, conta, corpo ou janela. Rodam antes de tudo de proposito --
		// inclusive antes da recusa de porta la embaixo -- porque uma rodada que morre por porta ocupada
		// ainda assim entrega metade do veredito, e essa metade e a que cobre "so as duas de UI".
		//
		// (E a ordem tambem e diagnostica: se o catalogo ja estiver errado, nao ha por que ler as fotos
		// dos vinte passos seguintes procurando a causa na tela.)
		// ==============================================================================================
		if (!_purosJaForam)
		{
			_purosJaForam = true;
			OCatalogo();
			OShaderEstaLigado();
		}

		// ============================ ESTE MUNDO E MEU? SE NAO FOR, NAO ENCOSTA ============================
		// Isto nao e paranoia, e uma rodada perdida: a porta 7777 e unica na maquina e ha outras
		// sessoes subindo o jogo. Quando o `_net.Start` falha (`Running == false`) o `--host` nao vira
		// servidor NENHUM -- e o cliente, que conecta em 127.0.0.1 logo depois, entra alegremente no
		// servidor DA OUTRA SESSAO. Foi o que aconteceu: o log trouxe "Bind exception ...
		// AddressAlreadyInUse" e, tres linhas abaixo, "entrou no meu campo de visao: id 1" -- havia
		// gente ali.
		//
		// E o estrago nao seria so a foto errada: esta bancada manda `admin_forma`, segura o C e anda
		// com o corpo. Tudo isso seria TRANSMITIDO pra tela de quem estava jogando naquele mundo.
		// Entao a regra e desistir, e desistir FALANDO -- uma bancada que morre calada seria lida como
		// "nao rodou" em vez de "recusou".
		// ==============================================================================================
		if (Array.IndexOf(OS.GetCmdlineArgs(), "--host") >= 0
			&& Jandirus.Server.GameServer.Instance is { Running: false })
		{
			GD.PrintErr("[nebulosa] RECUSADO: subi com `--host` mas a porta ja estava tomada -- este "
					  + "mundo e de outra sessao. Nada foi forcado. Espere a porta 7777 vagar.");
			// FECHA EM VEZ DE SO SAIR: os blocos puros la de cima ja rodaram e ja tem veredito. Sair
			// calado aqui jogaria fora o placar deles e a rodada seria lida como "nao rodou nada".
			Fechar();
			return;
		}

		// Sem corpo nao ha nada pra medir nem pra fotografar -- o relogio so comeca a correr quando o
		// mundo existe, senao os primeiros passos gastariam a espera durante o login.
		if (C is not { Connected: true } || Corpo is null) return;

		_t += delta;
		if (_t < _espera) return;
		_t = 0;

		switch (_passo++)
		{
			case 0: OsTresQueSoOJogoResponde(); break;

			// ============================ O CEU TEM QUE PARAR QUIETO PRA A FOTO VALER ============================
			// O clima natural sorteia sozinho, e ele arruinou a comparacao entre duas rodadas: uma saiu
			// com a grama em RGB(79,111,24) e a seguinte, com chuva, em RGB(38,58,17). Sao fundos
			// DIFERENTES, e comparar o efeito entre elas nao diz nada -- metade da diferenca era o tempo.
			//
			// Nao da pra pedir "limpo": o `admin_clima` recusa `Limpo` de proposito (e o valor que ele usa
			// pra dizer "nao conheco esse clima") e a forca tem piso de 0,05. Entao pede-se o clima mais
			// discreto da lista na forca MINIMA -- que e o mesmo que limpar, e usa a ferramenta que ja
			// existe em vez de abrir uma porta nova no servidor so pra bancada.
			// ================================================================================================
			// (o pedido de ceu sai junto do passo 0 -- ver o fim de `OsTresQueSoOJogoResponde`)

			// ============================ OS TRES PRIMEIROS PASSOS SO QUEIMAM CINEMATICA ============================
			// A PRIMEIRA RODADA DESTA BANCADA MENTIU, e vale guardar por que: eu esperava 9 s depois do
			// `admin_forma` e media. Mas `UiSign.SegundosPreso` e 22 -- o `Efeito.Assumir` (que e quem
			// chama o `Vestir`, que e quem acende a nuvem) so cai no beat de 22 s. Entao a bancada
			// media um corpo NO MEIO da transformacao e escrevia "a nuvem nao acendeu": um defeito que
			// nao existia, na foto de um jogo que estava certo.
			//
			// E TINHA PIOR NA FOTO. A cena inteira despeja `Poeira`, `Cascalho`, `Cratera` e
			// `AnelDeChoque` em volta do corpo. As quatro fotos daquela rodada sairam com uma nuvem
			// MARROM cobrindo o personagem, um anel branco de onda de choque e uma cratera no chao --
			// e nada daquilo era a nebulosa. Eu quase reportei "a rampa esta saindo marrom".
			//
			// O CONSERTO E QUEIMAR A ESTREIA. `Forma.Entrar` so devolve `primeira` uma vez por forma
			// (ver `AdminForcarForma`: *"forcar a mesma forma duas vezes da cena cheia na primeira e a
			// regra normal na segunda"*). Entao a bancada paga as duas cenas ADIANTADO, volta pra base,
			// e o passe que ela vai MEDIR entra sem cena nenhuma -- instantaneo e sem sujeira.
			// ==================================================================================================
			//
			// E A ESPERA E POR ENQUETE, NAO POR PRAZO. Cravar "30 s" pela duracao da cena foi a segunda
			// versao disto, e ela tambem mentiu: numa maquina ocupada a cena atrasa, o passo seguinte
			// media um corpo ainda em transformacao, e a bancada acusava defeito de novo. A cena e um
			// EVENTO -- entao espera-se o evento (o `forca` virar), com um teto generoso pra a bancada
			// nao ficar presa pra sempre se ele nunca vier.
			case 1: Forcar("ui_sign", "QUEIMANDO a estreia (a cena e de 22 s -- ninguem mede aqui)"); break;
			case 2: EsperarANuvem(true, "a cena do -Sign- terminar"); break;
			case 3: Forcar("ui_perfected", "QUEIMANDO a estreia do segundo estagio"); break;
			case 4: EsperarANuvem(true, "a cena do Perfected terminar"); break;
			case 5: Forcar(Jandirus.Core.Forms.Catalogo.IdBase, "de volta a base pra comecar a medir"); break;
			case 6: EsperarANuvem(false, "o corpo voltar mesmo pra base"); break;

			// E SAINDO DE CIMA DA CRATERA. As duas cenas deixaram o chao revirado e o decalque nao
			// evapora -- fotografar aqui seria fotografar terra marrom, que foi metade do engano da
			// rodada anterior. Andar alguns tiles poe o corpo em grama limpa, que e o fundo que se quer
			// julgar.
			case 7: Andar(); break;
			case 8: PararDeAndar(); break;

			case 9: Forcar("ui_sign", "agora SEM cena: o passe que a bancada mede"); break;
			case 10: EsperarANuvem(true, "a nuvem acender (sem cena, e quase imediato)"); break;
			// A FOLGA DERIVADA ANDA DE CARONA NESTE PASSO, e nao num proprio, por duas razoes: ela
			// precisa de um corpo que JA ACENDEU a nuvem (o `Definir(true)` e quem remede a silhueta --
			// no `_Ready` o boneco ainda nao tem folha e vale o fallback), e um caso novo aqui
			// renumeraria os trinta e tres abaixo por causa de uma linha.
			case 11: DepoisDeVestir("sign"); AFolgaEDerivada(); break;
			case 12: Carregar(true, "segurando C -- o Ki passa de 100% e a sobrecarga acende"); break;
			case 13: ASobrecargaNaoEmpilhaChama(); break;
			case 14: Carregar(false, "soltando C -- agora o Ki cai sozinho pelo dreno da forma"); break;
			case 15: OKiCaindo(); break;
			case 16: Forcar("ui_perfected", "o segundo estagio: tem que sair IGUAL ao primeiro"); break;
			case 17: EsperarANuvem(true, "o segundo estagio assentar"); break;
			case 18: DepoisDeVestir("perfected"); break;

			// ============================ O PAR LIGADO/DESLIGADO, NO MESMO LUGAR ============================
			// A foto sozinha nao diz de QUEM e cada pixel: no mesmo enquadramento ha grama, o boneco, a
			// chama, o clima e a nuvem. Eu ja me enganei duas vezes hoje lendo poeira de cinematica como
			// se fosse a rampa da nebulosa.
            //
			// Duas fotos do MESMO quadro-a-quadro, so com o `forca` mudando, e a subtracao delas isola
			// exatamente os pixels que este efeito pinta -- e ONDE ele os pinta. E o unico jeito honesto
			// de responder "a nuvem esta em volta do corpo?" sem confiar no olho.
			// ==========================================================================================
			//
			// UM QUADRO ENTRE AS DUAS, e este numero e o conserto de um engano meu. Com 0,35 s de folga
			// a subtracao trouxe junto a grama balancando, a nuvem do CLIMA andando e a neve caindo --
			// e eu li aquele borrao do canto como se fosse a nebulosa. Ele nao era: aparecia igual com
			// o efeito DESLIGADO. Entre dois quadros consecutivos nada mais tem tempo de se mexer, e o
			// que sobra na diferenca e so o que este shader pinta.
			//
			// TRES PASSOS PRA DUAS FOTOS, e o passo do meio nao e desperdicio: `GetTexture().GetImage()`
			// devolve o quadro JA DESENHADO, ou seja o anterior. Apagar a nuvem e fotografar no mesmo
			// passo salvaria o quadro em que ela ainda estava acesa -- as duas fotos sairiam trocadas e
			// a subtracao daria o negativo do efeito, que e o tipo de erro que se le como "funcionou".
			//
			// E SAO TRES FOTOS, NAO DUAS -- a terceira e O BONECO SUMINDO, e ela e o que faltava pra a
			// folga ser MEDIDA em vez de deduzida. A subtracao ligado/desligado isola onde o efeito
			// desenha; sozinha ela nao diz onde o CORPO acaba, e a folga e a distancia entre os dois.
			// Segmentar o boneco por cor na foto nao funciona: a cratera que a cinematica deixou no chao
			// nao e grama nem e corpo, e qualquer regra de "isto nao e fundo" a inclui -- foi assim que a
			// primeira medida deu a foto inteira como sendo o boneco.
			//
			// Escondendo o `Visual` por UM quadro, a subtracao `par-off - sem-corpo` devolve exatamente
			// os pixels do personagem (corpo, cabelo, roupa e rabo), com a cratera cancelando dos dois
			// lados. Duas subtracoes no mesmo enquadramento, e a folga sai em pixel medido.
			case 19: Alternar(false); EsconderOBoneco(true); break;
			case 20: Fotografar("sem-corpo", Corpo); EsconderOBoneco(false); break;
			case 21: Fotografar("par-off", Corpo); Alternar(true); break;
			case 22: Fotografar("par-on", Corpo); AFolgaNaImagem(); Chapado(true); break;

			// A ELIPSE CHAPADA, e ela responde a pergunta que a foto normal nao responde: ONDE este
			// quad desenha. Ver `NebulosaDaForma.DiagnosticoChapado`.
			// E A FOTO CHAPADA E CONFERIDA, nao so salva. Ver `AElipseCaiNoCorpo`: e a unica checagem
			// desta bancada que julga PIXEL DESENHADO em vez de estado de node, e e a que teria pego o
			// defeito da ancora no primeiro dia.
			case 23: AElipseCaiNoCorpo(); Moldura(true); break;
			case 24: Fotografar("moldura", Corpo); Moldura(false); break;

			// ============================ AS MICROPARTICULAS: QUATRO QUADROS, DOIS BOTOES ============================
			// O requisito e duplo -- "as particulas EXISTEM" e "a densidade e AFINAVEL" -- e nenhuma das
			// duas metades se prova lendo o arquivo. Um `uniform float pontos_densidade` declarado e
			// nunca lido passaria em qualquer checagem de texto, e a tela ficaria igual em 0 e em 1.
			//
			// Entao a bancada GIRA O BOTAO e CONTA OS PIXELS BRANCOS, quatro vezes:
			//   * `pontos_brilho = 0` e o padrao   -> quem desenha os pontinhos e ESTA camada;
			//   * `pontos_densidade = 0` e `= 1`   -> girar o botao muda o que se ve.
			//
			// UM QUADRO ENTRE ESCREVER E MEDIR, pelo motivo ja escrito no par de subtracao: o
			// `GetTexture().GetImage()` devolve o quadro JA DESENHADO. Medir no mesmo passo em que se
			// escreve o uniform leria a foto do valor ANTERIOR -- as quatro medidas sairiam deslocadas de
			// um passo e a conclusao (que e uma comparacao entre elas) sairia trocada.
			// ==================================================================================================
			//
			// E CADA MEDIDA E UMA SOMA DE DEZ QUADROS, nao um quadro. Uma rodada com um quadro so deu
			// 25 px de branco e a seguinte deu 6 -- as duas com o efeito intacto. Nao e ruido de
			// medicao: sao ~11 particulas de 1 px subindo em velocidades diferentes, e quantas delas
			// estao DENTRO do anel num instante qualquer varia com a fase do laco. Um limiar fixo em
			// cima de um quadro sorteado reprova o efeito certo em metade das rodadas -- que e pior do
			// que nao ter limiar, porque ensina quem roda a bancada a ignorar o vermelho.
			//
			// (O `Medir` so devolve `true` no ultimo quadro da amostra; por isso o `if` -- girar o botao
			// seguinte no meio da coleta contaminaria a medida com o valor do proximo estado.)
			case 25: Afinar("pontos_brilho", 0f); break;
			case 26: if (Medir(ref _semBrilho, "brilho 0")) Afinar("pontos_brilho", null); break;
			case 27: if (Medir(ref _comBrilho, "padrao")) Afinar("pontos_densidade", 0f); break;
			case 28: if (Medir(ref _densidade0, "densidade 0")) Afinar("pontos_densidade", 1f); break;
			case 29: if (Medir(ref _densidade1, "densidade 1")) Afinar("pontos_densidade", 0.25f); break;

			// ============================ A RAMPA: O BOTAO SOBE OU SO MEXE? ============================
			// Os quatro quadros de cima provam que girar o botao pros extremos muda a tela (0 contra
			// algo). Isso ainda deixa passar um botao que mexe pra QUALQUER LADO -- e o que o dono
			// precisa saber e o SENTIDO: no comentario do shader esta escrito que subir enche.
			//
			// Uma rodada anterior levantou justamente essa duvida: a medida em 1,00 saiu MENOR que a do
			// padrao 0,75 (79 contra 198 px somados). Pelo shader isso e impossivel -- o corte e
			// `if (h1 > pontos_densidade) return 0.0`, entao subir o numero so pode ADMITIR colunas,
			// nunca tirar. Ou seja, aquilo era a variancia da MEDIDA, nao o botao: as ~11 particulas de
			// 1 px sobem em velocidades diferentes e quantas estao dentro do anel oscila muito.
			//
			// A rampa poe essa variancia na mesa em vez de escondê-la: quatro pontos no log, e uma
			// checagem so entre os EXTREMOS (0,25 contra 1,00 -- quatro vezes o portao), que e a unica
			// distancia grande o bastante pra o sentido aparecer por cima do ruido.
			// ======================================================================================
			case 30: if (Medir(ref _rampa25, "densidade 0,25")) Afinar("pontos_densidade", 0.50f); break;
			case 31: if (Medir(ref _rampa50, "densidade 0,50")) Afinar("pontos_densidade", 0.75f); break;
			case 32: if (Medir(ref _rampa75, "densidade 0,75")) Afinar("pontos_densidade", 1.00f); break;
			case 33: if (Medir(ref _rampa100, "densidade 1,00")) Afinar("pontos_densidade", null); break;
			case 34: AsParticulasExistemEAfinam(); break;

			// ============================ E AGORA UMA FORMA QUE **NAO** E ULTRA INSTINTO ============================
			// O catalogo ja disse que so as duas de UI tem nebulosa. Isto e a outra metade: que o
			// CAMINHO DE PRODUCAO obedece a ela. Sao coisas diferentes -- o `Vestir` podia acender a
			// nuvem sem consultar o `TemNebulosa`, ou consultar e ignorar a resposta, e a varredura de
			// texto continuaria verde.
			//
			// O `ssj1` paga uma cinematica de estreia aqui (ninguem a queimou antes), e por isso a
			// espera e por EVENTO. Nao ha foto a proteger neste passo: o que se mede e estado de node.
			// ==================================================================================================
			case 35: Forcar("ssj1", "uma forma que NAO e Ultra Instinto (a cinematica dela e de estreia)"); break;
			case 36: EsperarANuvem(false, "o `ssj1` assentar"); break;
			case 37: ANebulosaSoEDoUltraInstinto(); break;

			// ============================ E A NUVEM SOME ATRAS DO CENARIO? ============================
			// Esta e a contrapartida de "o efeito por cima do corpo". Por cima do CORPO ele tem que
			// ficar; por cima da ARVORE, nunca -- e as duas coisas se decidem em lugares diferentes,
			// entao acertar uma nao diz nada sobre a outra. Dentro do personagem quem manda e a ORDEM
			// DE IRMAO (a nuvem e a filha mais nova); contra o cenario quem manda e o `ZIndex`, que e 0
			// justamente pra o Y-sort continuar valendo. Um `ZIndex` positivo poria a nuvem por cima de
			// TUDO que esta em z 0, arvore inclusive -- e e o tombo que o `_cabelo` ja levou
			// (`CharacterVisual.NovaCamada`: *"o personagem virava um tufo de cabelo flutuando na copa"*).
			//
			// A bancada ja afirmava `ZIndex == 0`. Isso e ler o campo, nao a tela: com a nuvem sendo
			// filha do corpo, o que importa e se o desenho dela cai no MESMO degrau de Y que o resto do
			// boneco -- e isso so a foto responde. Por isso o robo agora PROCURA uma arvore no tilemap
			// (o cenario e tile, nao ha node pra achar com `FindChild`), ANDA ate ela e fotografa de
			// baixo dela.
			// ======================================================================================
			case 38: Forcar("ui_sign", "de volta ao Ultra Instinto pro teste de cenario"); break;
			case 39: EsperarANuvem(true, "a nuvem acender de novo"); break;
			case 40: AcharUmaArvore(); break;
			case 41: AndarAteOAlvo(); break;
			case 42: OCenarioTapaANuvem(); break;

			case 43: Forcar(Jandirus.Core.Forms.Catalogo.IdBase, "voltando pra base"); break;
			case 44: EsperarANuvem(false, "a base apagar a nuvem"); break;
			default: ABaseApaga(); break;
		}
	}

	// =====================================================================
	// 0-A. O CATALOGO: QUEM TEM NEBULOSA
	// =====================================================================
	private bool _purosJaForam;

	/// <summary>
	/// A VARREDURA, e ela e uma FUNCAO DE UM PREDICADO de proposito.
	///
	/// ============================ E ISSO NAO E ENFEITE: E O CONTROLE NEGATIVO ============================
	/// Uma varredura que so roda com a resposta certa nao consegue provar que ENXERGA. Se
	/// `Catalogo.Todas` viesse vazia, se o `foreach` errasse o filtro, se a comparacao estivesse
	/// invertida -- em qualquer um desses casos o laco daria "0 erradas" e o relatorio sairia verde por
	/// vacuidade. Isso ja aconteceu neste projeto o bastante pra virar regra da casa: *"um teto que
	/// nunca dispara e indistinguivel de nao ter teto"*.
	///
	/// Recebendo o predicado de fora, a MESMA varredura roda quatro vezes: uma com a regra de verdade
	/// (`Catalogo.TemNebulosa`, tem que dar zero) e tres com defeitos INJETADOS -- "todo mundo tem",
	/// "ninguem tem" e "trocaram UI por UE". As tres tem que REPROVAR, e com o numero exato de formas
	/// fora do lugar. Se alguma delas passar, a varredura esta cega e nenhuma das outras linhas deste
	/// arquivo vale nada.
	/// ================================================================================================
	/// </summary>
	private static (int Erradas, int Com, int Sem) Varrer(Func<FormaDef, bool> pergunta)
	{
		int erradas = 0, com = 0, sem = 0;
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			// A REGRA, ESCRITA AQUI E NAO IMPORTADA. Chamar `Catalogo.TemNebulosa` dos dois lados faria
			// a linha comparar a funcao com ela mesma -- verde eterno, inclusive no dia em que ela
			// passasse a responder qualquer outra coisa. O que se cobra e a FRASE do dono: a nebulosa e
			// da linha do Ultra Instinto, e de mais ninguem.
			bool devia = d.Linha == LinhaDeForma.UltraInstinct;
			bool deu = pergunta(d);
			if (deu != devia) erradas++;
			if (deu) com++; else sem++;
		}
		return (erradas, com, sem);
	}

	/// <inheritdoc cref="Varrer"/>
	private void OCatalogo()
	{
		Nota("== 1. O CATALOGO (puro: nao precisa de mundo, de conta nem de janela) ==");

		FormaDef[] todas = Jandirus.Core.Forms.Catalogo.Todas;
		FormaDef? sign = todas.FirstOrDefault(d => d.Id == "ui_sign");
		FormaDef? perf = todas.FirstOrDefault(d => d.Id == "ui_perfected");

		Conferir(sign != null && perf != null,
				 "as DUAS formas de Ultra Instinto existem no catalogo (`ui_sign` e `ui_perfected`)");
		if (sign == null || perf == null) return;

		// --- (a) LIGADO NAS DUAS ---
		Conferir(Jandirus.Core.Forms.Catalogo.TemNebulosa(sign), "`ui_sign` tem nebulosa");
		Conferir(Jandirus.Core.Forms.Catalogo.TemNebulosa(perf), "`ui_perfected` tem nebulosa");

		// --- (a2) E AS DUAS DE ULTRA EGO **NAO** TEM ---
		// ============================ A MAESTRIA VIROU REGRA COMUM AS QUATRO; O DESENHO NAO ============================
		// As quatro formas divinas passaram a nao ter maestria propria (a proficiencia da SKILL substituiu),
		// e essa regra e do PAR: `ui_sign`, `ui_perfected`, `destroyer` e `ultra_ego`. A NEBULOSA nao segue
		// o par -- `Catalogo.TemNebulosa` responde pela LINHA do Ultra Instinto, e o Ultra Ego nao tem
		// nuvem nenhuma (o DM tambem nao lhe da: `UltraEgo.dm` nao veste as duas folhas de galaxia).
		//
		// A varredura logo abaixo ja cobre isso em NUMERO, e estas duas linhas cobrem em NOME -- porque a
		// confusao possivel aqui e humana e nao aritmetica: alguem que leia "a regra vale pras quatro" no
		// arquivo da maestria e venha ligar a nuvem nas quatro. Duas linhas nomeadas param essa leitura.
		// ==========================================================================================================
		FormaDef? destr = todas.FirstOrDefault(d => d.Id == "destroyer");
		FormaDef? ego = todas.FirstOrDefault(d => d.Id == "ultra_ego");
		Conferir(destr != null && !Jandirus.Core.Forms.Catalogo.TemNebulosa(destr),
				 "`destroyer` NAO tem nebulosa (a regra da maestria vale pras quatro; o DESENHO nao)");
		Conferir(ego != null && !Jandirus.Core.Forms.Catalogo.TemNebulosa(ego),
				 "`ultra_ego` NAO tem nebulosa (idem -- a nuvem e da LINHA do Ultra Instinto)");

		// --- (b) E **SO** NAS DUAS ---
		(int erradas, int com, int sem) = Varrer(Jandirus.Core.Forms.Catalogo.TemNebulosa);
		Conferir(erradas == 0,
				 $"a nebulosa e SO da linha do Ultra Instinto ({erradas} forma(s) fora do lugar em "
			   + $"{todas.Length} varridas)");
		Conferir(com == 2, $"exatamente DUAS formas acendem nebulosa (deu {com})");
		Conferir(sem == todas.Length - 2,
				 $"e as outras {todas.Length - 2} NAO ganharam de brinde (deu {sem})");

		// --- (c) A MESMA NOS DOIS ESTAGIOS ---
		// ============================ A LINHA DE BAIXO SO VALE POR CAUSA DESTA ============================
		// "as duas respondem igual" e uma frase vazia se as duas forem indistinguiveis pra quem
		// responde. `TemNebulosa` le a LINHA; se o unico dado que separasse os dois estagios fosse a
		// linha, nao haveria por onde ramificar e a checagem seria verde por construcao. A `Ordem` e o
		// campo por onde uma ramificacao por estagio entraria (e e por ele que a `Coladas` e a `Folha`
		// ramificam, do lado das divinas) -- entao ele e que precisa DIFERIR pra a pergunta ter sentido.
		// ============================================================================================
		Conferir(sign.Ordem != perf.Ordem,
				 $"os dois estagios DIFEREM em `Ordem` ({sign.Ordem} e {perf.Ordem}) -- e por aqui que "
			   + "uma ramificacao por estagio entraria");
		Conferir(Jandirus.Core.Forms.Catalogo.TemNebulosa(sign)
			  == Jandirus.Core.Forms.Catalogo.TemNebulosa(perf),
				 "e mesmo assim a resposta e a MESMA nos dois -- `TemNebulosa` nao ramifica por estagio "
			   + "(`UltraInstinct.dm:479` tambem nao)");

		bool[] daLinha = [.. Jandirus.Core.Forms.Catalogo.DaLinha(LinhaDeForma.UltraInstinct)
			.Select(d => Jandirus.Core.Forms.Catalogo.TemNebulosa(d)).Distinct()];
		Conferir(daLinha.Length == 1 && daLinha[0],
				 $"e vale pra LINHA INTEIRA, nao so pros dois de hoje ({daLinha.Length} resposta(s) "
			   + "distinta(s) entre os degraus do Ultra Instinto)");

		// --- (d) A VARREDURA ENXERGA? OS TRES DEFEITOS INJETADOS ---
		Nota("  --     agora os DEFEITOS INJETADOS: as tres linhas abaixo tem que dizer que reprovaram");
		Conferir(Varrer(_ => true).Erradas == todas.Length - 2,
				 $"[injetado] 'nebulosa pra TODO MUNDO' e pego, e nas {todas.Length - 2} formas certas "
			   + $"(deu {Varrer(_ => true).Erradas})");
		Conferir(Varrer(_ => false).Erradas == 2,
				 $"[injetado] 'NINGUEM tem nebulosa' e pego nas duas de UI (deu {Varrer(_ => false).Erradas})");
		Conferir(Varrer(d => d.Linha == LinhaDeForma.UltraEgo).Erradas == 4,
				 "[injetado] trocar Ultra Instinto por Ultra EGO e pego dos dois lados -- 2 que ganharam "
			   + $"e 2 que perderam (deu {Varrer(d => d.Linha == LinhaDeForma.UltraEgo).Erradas})");

		// --- (e) E ELA ENXERGA COISA DE VERDADE? ---
		// ============================ O CONTROLE POSITIVO ============================
		// Os tres de cima provam que a varredura sabe dizer NAO. Falta provar que a lista que ela varre
		// tem conteudo: um `Catalogo.Todas` vazio faria os quatro darem "0 erradas, 0 com, 0 sem" e a
		// unica linha que reclamaria seria a contagem -- que e facil de alguem "consertar" pro numero
		// novo. Estas duas medem OUTROS efeitos de forma no mesmo catalogo: se ha colada e ha raio, a
		// lista existe e esta povoada.
		// =========================================================================
		int comColada = todas.Count(d => Jandirus.Core.Forms.Catalogo.Coladas(d).Length > 0);
		int comRaio = todas.Count(d => d.Raios > 0);
		Conferir(todas.Length >= 30 && comColada > 0 && comRaio > 0,
				 $"a varredura enxerga um catalogo POVOADO: {todas.Length} formas, {comColada} com "
			   + $"colada, {comRaio} com raio -- nao e uma lista vazia dando verde");
	}

	// =====================================================================
	// 0-B. O SHADER: ESCRITO NAO E LIGADO
	// =====================================================================
	/// <summary>
	/// Os uniforms que o efeito PRECISA ter. Tres grupos, e cada um por um motivo diferente:
	///   * `forca`, `semente`, `lado_do_quad` sao escritos pelo C# -- escrever num uniform inexistente
	///     e SILENCIOSO no Godot, entao o sumico de qualquer um deles vira "a nuvem nunca acende" (ou
	///     "os pontinhos ficam do tamanho errado") sem uma mensagem em lugar nenhum;
	///   * a rampa (`cor_*`, `parada_meio`) e `pontos_densidade` e `pontos_brilho` sao os BOTOES que o
	///     dono afina editando o `.gdshader` e reabrindo o jogo. Virassem constante, o ajuste passaria
	///     a custar um build inteiro -- o efeito continuaria desenhando e a propriedade morreria calada.
	/// </summary>
	private static readonly string[] UniformsObrigatorios =
	[
		"forca", "semente", "lado_do_quad",
		"cor_borda", "cor_meio", "cor_perto", "parada_meio",
		"pontos_densidade", "pontos_brilho",
	];

	/// <summary>
	/// QUANTOS DOS PEDIDOS FALTAM -- perguntando ao SHADER COMPILADO e nao ao texto do arquivo.
	///
	/// ============================ ESTA E A DIFERENCA ENTRE ESCRITO E LIGADO ============================
	/// O `--diagforma` ja confere estes nomes com `code.Contains(" forca ")`, e essa pergunta e sobre o
	/// TEXTO: ela da verde num arquivo que o Godot recusou inteiro. Um `.gdshader` com erro de sintaxe
	/// nao derruba o jogo -- ele reclama no console e o material passa a desenhar com o padrao, ou seja
	/// um quadrado branco (ou nada). O `Code` continua la, com todas as palavras no lugar.
	///
	/// `GetShaderUniformList` vem do shader JA COMPILADO pelo servidor de renderizacao. Compilacao
	/// falhou, lista vazia. E a mesma familia do tombo dos 35 atlas escritos no disco e nunca importados
	/// pelo Godot: **escrever o arquivo nao e ligar o arquivo**.
	/// ==============================================================================================
	/// </summary>
	private static int Faltando(Shader sh)
	{
		var tem = new HashSet<string>();
		foreach (Variant item in sh.GetShaderUniformList())
			tem.Add(item.AsGodotDictionary()["name"].AsString());
		return UniformsObrigatorios.Count(u => !tem.Contains(u));
	}

	/// <inheritdoc cref="Faltando"/>
	private void OShaderEstaLigado()
	{
		Nota("== 2. O SHADER (puro): importado, COMPILADO, e com os botoes de fora ==");

		const string caminho = "res://Assets/Shaders/NebulosaDaForma.gdshader";
		Conferir(ResourceLoader.Exists(caminho),
				 $"{caminho.GetFile()} esta IMPORTADO -- o banco de recursos do Godot o conhece "
			   + "(estar na pasta nao basta)");

		var sh = GD.Load<Shader>(caminho);
		Conferir(sh != null, $"{caminho.GetFile()} carrega");
		if (sh == null) return;

		int faltam = Faltando(sh);
		Conferir(faltam == 0,
				 $"o shader COMPILOU e entrega os {UniformsObrigatorios.Length} uniforms que o efeito "
			   + $"precisa ({faltam} faltando) -- perguntado ao compilado, nao ao texto");

		// ============================ E O VERIFICADOR ENXERGA? DOIS DEFEITOS INJETADOS ============================
		// Sem estas duas linhas, `Faltando` podia estar devolvendo 0 por qualquer motivo -- inclusive
		// por sempre devolver 0 -- e a linha de cima seria um verde vazio.
		//
		// Os dois casos sao diferentes de proposito: o primeiro COMPILA (a lista vem cheia, so que com
		// o uniform errado) e o segundo NAO compila (a lista vem vazia). Um verificador que so soubesse
		// distinguir "carregou / nao carregou" passaria no segundo e falharia no primeiro.
		// ======================================================================================================
		Nota("  --     ATENCAO: as linhas VERMELHAS do Godot abaixo sao PROPOSITAIS -- e um shader "
		   + "quebrado de proposito, pra provar que o verificador reprova");

		var soUmUniform = new Shader
		{
			Code = "shader_type canvas_item;\nuniform float forca = 0.0;\n"
				 + "void fragment() { COLOR = vec4(forca); }\n",
		};
		Conferir(Faltando(soUmUniform) == UniformsObrigatorios.Length - 1,
				 $"[injetado] um shader que COMPILA e so tem `forca` reprova em "
			   + $"{UniformsObrigatorios.Length - 1} uniforms (deu {Faltando(soUmUniform)})");

		var quebrado = new Shader { Code = "shader_type canvas_item;\nvoid fragment() { isto nao e glsl }\n" };
		Conferir(Faltando(quebrado) == UniformsObrigatorios.Length,
				 $"[injetado] um shader que NAO COMPILA reprova nos {UniformsObrigatorios.Length} "
			   + $"(deu {Faltando(quebrado)}) -- e por isso que a pergunta e feita ao compilado");
	}

	/// <summary>
	/// ESPERA O EVENTO, e nao o relogio. Fica no mesmo passo ate o `forca` da nuvem virar o que se
	/// pediu (ou ate o teto de ~40 s, pra uma bancada travada morrer falando em vez de rodar pra
	/// sempre). E o unico jeito honesto de esperar uma cinematica: a duracao dela esta escrita no
	/// `Cinematicas.cs`, mas o que chega na tela depende da maquina, e as duas rodadas que eu perdi
	/// hoje foram exatamente por cravar o numero do papel.
	/// </summary>
	private void EsperarANuvem(bool acesa, string oque)
	{
		float alvo = acesa ? 1f : 0f;

		// ============================ E ESPERAR A CENA SOLTAR O CORPO, NAO SO A NUVEM ACENDER ============================
		// Esperar so pelo `forca` tinha um buraco cego, e ele custou duas rodadas: os DOIS estagios do
		// Ultra Instinto acendem a MESMA nuvem (e o ponto do efeito -- `TemNebulosa` nao ramifica por
		// `Ordem`). Entao, ao forcar `ui_perfected` logo depois do `ui_sign`, o `forca` ja valia 1 e a
		// espera voltava em "0,0s" sem esperar nada. A bancada seguia em frente NO MEIO da cinematica
		// de 22 s do Perfected e mandava `admin_forma base` por cima dela -- que a cena engolia. Dai os
		// "DESISTI de esperar o corpo voltar pra base (teto de 40,2s)" que apareciam duas vezes por
		// rodada e faziam a bancada estourar o proprio prazo antes de tirar as fotos.
		//
		// `PresosDeTeste` conta quantas cenas seguram o corpo agora. Zero e a unica definicao honesta
		// de "pode mexer nele de novo", e ela vale pros dois estagios sem precisar distinguir um do
		// outro.
		// ============================================================================================================
		bool chegou = Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.ForcaDeTeste == alvo
				   && Transformacao.PresosDeTeste == 0;

		if (!chegou && _voltasDaEspera++ < 200)
		{
			_passo--;            // fica neste mesmo passo
			_espera = 0.2;
			return;
		}

		Nota(chegou
			? $"  --     esperei {oque}: {_voltasDaEspera * 0.2:0.0}s"
			: $"  --     DESISTI de esperar {oque} (teto de {_voltasDaEspera * 0.2:0.0}s)");
		_voltasDaEspera = 0;
		// A POEIRA DA CENA AINDA ESTA NO AR quando o `Assumir` cai. O beat final do `UiSign` e
		// `Efeito.Poeira` DEPOIS do `Assumir` -- fotografar no instante em que a nuvem acende pegaria
		// aquela poeira por cima dela, que foi o engano da primeira rodada.
		_espera = acesa ? 3.0 : 1.5;
	}

	private int _voltasDaEspera;

	/// <summary>
	/// Anda pra longe da cratera que as duas cinematicas cavaram. Pelo `Input`, e nao empurrando a
	/// posicao do node: mexer no node direto brigaria com a reconciliacao do servidor (o corpo
	/// voltaria sozinho) -- e o caminho da tecla e o mesmo que o jogador usa.
	/// </summary>
	private void Andar()
	{
		Input.ActionPress("move_right");
		Nota("  --     andando pra sair de cima da cratera que as cenas cavaram");
		_espera = 4.0;
	}

	private void PararDeAndar()
	{
		Input.ActionRelease("move_right");
		_espera = 1.2;   // a poeira do proprio passo assenta
	}

	// =====================================================================
	// 1. O QUE SO O JOGO RODANDO RESPONDE
	// =====================================================================
	/// <summary>
	/// As tres perguntas que a validacao em projeto descartavel NAO respondia. Elas sao de
	/// integracao, nao de desenho: o shader podia estar perfeito e nenhuma delas dar certo.
	/// </summary>
	private void OsTresQueSoOJogoResponde()
	{
		if (Corpo is not { } corpo) return;

		// (a) O NODE NASCE. Ele e criado no `World.AoEntrar`, e nao numa cena -- se alguem mexer na
		// lista de filhos do corpo, some sem erro nenhum e o efeito simplesmente nunca aparece.
		var neb = corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa");
		Conferir(neb != null, "o node `Nebulosa` nasce no corpo local");
		if (neb == null) { Fechar(); return; }

		// (b) A ORDEM DE IRMAO POE A NUVEM NA FRENTE. Este e o ponto que NAO da pra provar fora do
		// jogo: o `ZIndex` e 0 em todos eles de proposito (z vence Y-sort, e a nuvem passaria por cima
		// das arvores do cenario -- o tombo que o `_cabelo` ja levou), entao quem decide a pilha e o
		// indice do filho. Uma linha trocada no `AoEntrar` devolve a nuvem pra tras do boneco.
		//
		// ESTA LINHA JA COBROU O CONTRARIO. Ela pedia `iNeb < iAura < iCarga < iVis` e estava certa
		// enquanto o efeito era pra ficar atras; o dono corrigiu ("o efeito deveria ficar sobre o corpo
		// e nao atras") e a assercao virou. Fica anotado pra que a proxima leitura nao a "conserte" de
		// volta achando que a ordem regrediu.
		(bool naFrente, string pilha) = ANuvemEAIrmaMaisNova(corpo);
		Conferir(naFrente, pilha);

		// ============================ E A ASSERCAO ENXERGA? O DEFEITO INJETADO ============================
		// A linha de cima le quatro indices e os compara. Ela daria verde tambem se a comparacao fosse
		// escrita ao contrario -- que E o que ela dizia antes de o dono pedir a nuvem na frente, e por isso
		// esta e a assercao mais facil de "consertar" de volta por engano. Entao a bancada FAZ o defeito:
		// devolve a nuvem pra irma mais velha (a ordem da versao anterior deste efeito) e cobra que a MESMA
		// funcao reprove.
		//
		// `MoveChild` e depois de volta pro indice guardado: o node fica exatamente onde estava, e o unico
		// estrago e um quadro com a nuvem atras -- ninguem fotografa neste passo.
		int voltarPara = neb.GetIndex();
		corpo.MoveChild(neb, 0);
		(bool aindaPassa, string invertida) = ANuvemEAIrmaMaisNova(corpo);
		Conferir(!aindaPassa, "[injetado] com a nuvem devolvida a irma mais VELHA (a ordem antiga) a mesma "
							+ $"assercao REPROVA -- {invertida}");
		corpo.MoveChild(neb, voltarPara);
		Conferir(ANuvemEAIrmaMaisNova(corpo).Ok,
				 "...e a pilha voltou ao lugar depois do defeito injetado");

		// (c) NA BASE ELA ESTA APAGADA. O `TemNebulosa` responde pela LINHA da forma, e a base mora
		// na linha Saiyajin -- se ela acendesse aqui, o efeito estaria ligado no jogo inteiro.
		Conferir(neb.ForcaDeTeste == 0f,
				 $"na base a nuvem esta APAGADA (forca={neb.ForcaDeTeste:0.##})");
		Nota($"  --     lado do quad no mundo: {neb.LadoDeTeste:0} px "
				  + $"(o corpo tem 32 -- a nuvem precisa COLAR, nao envolver)");

		// ============================ OS 5 PIXELS, MEDIDOS ============================
		// A frase do dono e um numero: "no MAXIMO 5 pixels de distancia do corpo". Antes desta linha a
		// bancada media o TAMANHO do quad (a `Nota` acima) e nunca a FOLGA -- e o quad de 96 px passava
		// por ela sem uma reclamacao, com 26 px sobrando de cada lado do boneco.
		//
		// A folga sai da diferenca entre as duas elipses lidas dos uniforms, ou seja do que o shader
		// recebeu. `<= 5` nos DOIS eixos: o pedido e um teto, e a nuvem pode colar mais que isso.
		Vector2 folga = neb.FolgaDeTeste;
		Conferir(folga.X > 0f && folga.X <= 5.01f && folga.Y > 0f && folga.Y <= 5.01f,
				 $"a folga em volta da silhueta cabe nos 5 px do pedido "
			   + $"(lateral {folga.X:0.0} px, vertical {folga.Y:0.0} px)");

		// ============================ ONDE O QUAD ESTA, EM NUMERO ============================
		// A foto mostrou a nuvem DESENCOSTADA do corpo, uns 32 px pra baixo e pra direita. Foto nao diz
		// de quem e a culpa: pode ser o node fora do lugar, o quad com ancora errada, ou a mascara do
		// shader mirando fora do centro. Estas tres linhas separam os tres casos -- se os globais
		// baterem, o desvio e do shader; se nao baterem, e do node.
		// ============================ O CENTRO DO QUAD E O DO CORPO ============================
		// Isto era uma NOTA e devia ter sido uma checagem desde o comeco -- a nuvem passou a bancada
		// inteira desenhada 51 px fora do corpo, e o log dizia "as tres tem que ser a MESMA" sem nunca
		// comparar nada. Pior: a linha lia o `GlobalPosition` do quad, que era igual ao do corpo mesmo
		// com o desenho fora do lugar (a ancora e que estava errada, nao a posicao do node). Agora a
		// propriedade devolve o CENTRO do retangulo desenhado, que e o numero que a foto cobra.
		float fora = corpo.GlobalPosition.DistanceTo(neb.PosicaoDoQuadDeTeste);
		Conferir(fora < 1.0f,
				 $"o CENTRO do quad cai no corpo (fora por {fora:0.0} px; corpo={corpo.GlobalPosition}, "
			   + $"centro do quad={neb.PosicaoDoQuadDeTeste})");

		// O PENTEADO DA BASE, guardado AQUI porque este e o unico passo em que o corpo esta
		// comprovadamente na base (a linha logo acima acabou de medir `forca == 0`). Ele e o controle
		// negativo do passo do `ssj1`, la no fim -- ver `ANebulosaSoEDoUltraInstinto`.
		_cabeloNaBase = corpo.GetNodeOrNull<CharacterVisual>("Visual")?.CabeloDeTeste ?? "";
		Nota($"  --     penteado da base guardado: `{_cabeloNaBase.GetFile()}`");

		// O ceu vai junto daqui -- ver o comentario no roteiro. Sai NESTE passo (e nao num proprio) so
		// pra nao renumerar os vinte e cinco casos abaixo por causa de uma linha.
		C?.SendVerbo("admin_clima", "Neblina|0.05");
		Nota("  --     ceu pedido no minimo (`Neblina|0.05`): sem chuva a foto de dois dias diferentes "
		   + "vira comparavel");

		// E O MESMO EM PIXEL DE TELA, que e a unidade da FOTO. Sem isto eu ficava convertendo mundo
		// pra tela na mao, multiplicando pelo zoom que eu supunha, e a conta nunca fechava com o
		// recorte -- a bancada tem que dizer o zoom em vez de me deixar adivinhar.
		Rect2 r = neb.RetanguloNaTelaDeTeste;
		Vector2 pc = corpo.GetGlobalTransformWithCanvas().Origin;
		Nota($"  --     na TELA: corpo em {pc}  |  quad {r.Size.X:0}x{r.Size.Y:0} px com centro em "
		   + $"{r.Position + r.Size / 2}  |  zoom={World.Instancia?.ZoomDeTeste}  "
		   + $"|  janela={GetViewport().GetVisibleRect().Size}");

		_espera = 0.5;
	}

	// =====================================================================
	// 2. VESTIR PELA REDE
	// =====================================================================
	/// <summary>
	/// Manda o verb de admin e espera. NAO pinta o node: se a forma nao chegar pelo caminho de
	/// producao (servidor -> pacote -> `Transformacao.Vestir`), esta bancada tem que REPROVAR --
	/// era exatamente isso que o `Posar` do `--diagforma` escondia.
	/// </summary>
	/// <summary>
	/// A ESPERA E DE QUEM CHAMA, e nao deduzida do id -- foi a deducao que quebrou a rodada anterior.
	/// A MESMA forma pede 30 s na estreia (cena de 22 s mais a poeira assentando) e 2,5 s depois dela,
	/// entao nao ha nada no `id` de onde tirar esse numero: quem sabe se a cena ja foi paga e o
	/// roteiro, la em cima.
	/// </summary>
	private void Forcar(string id, string porque, double espera = 2.5)
	{
		C?.SendVerbo("admin_forma", id);
		Nota($"  --     `admin_forma {id}`: {porque}");
		_espera = espera;
	}

	private void DepoisDeVestir(string nome)
	{
		if (Corpo is not { } corpo) return;
		var neb = corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa");

		// O `Definir` CHEGOU? Esta e a pergunta que a validacao fora do jogo nao podia fazer. Escrever
		// num uniform inexistente e silencioso no Godot: sem esta linha, um erro de digitacao em
		// "forca" daria uma nuvem que nunca acende e nenhuma mensagem em lugar nenhum.
		Conferir(neb?.ForcaDeTeste == 1f,
				 $"[{nome}] a nuvem ACENDEU pelo caminho de producao (forca={neb?.ForcaDeTeste:0.##})");

		// ============================ E O SEGUNDO ESTAGIO TEM QUE SAIR IGUAL AO PRIMEIRO ============================
		// Nao "tambem aceso" -- IGUAL. Ver `NebulosaDaForma.AssinaturaDeTeste`: a comparacao pega todo
		// uniform do shader, entao ela reprova qualquer ramificacao por estagio, inclusive uma que
		// alguem escreva amanha num uniform que ainda nao existe.
		//
		// A primeira passagem so GUARDA. Comparar aqui mesmo, com uma assinatura vazia, daria verde na
		// estreia -- o classico teto que nunca dispara.
		// ======================================================================================================
		string assinatura = neb?.AssinaturaDeTeste() ?? "";
		if (_assinaturaDoSign == null)
		{
			_assinaturaDoSign = assinatura;
			Nota($"  --     [{nome}] assinatura do material guardada ({assinatura.Length} caracteres, "
			   + "pra comparar com o outro estagio)");
		}
		else
		{
			Conferir(assinatura.Length > 0,
					 $"[{nome}] a assinatura do material nao e vazia ({assinatura.Length} caracteres) -- "
				   + "duas assinaturas vazias seriam 'iguais' e nao provariam nada");
			Conferir(assinatura == _assinaturaDoSign,
					 $"[{nome}] os DOIS estagios usam o MESMO overlay: todo uniform do shader bate com o "
				   + "do `ui_sign` (se alguem ramificar por estagio, esta linha cai)");
			if (assinatura != _assinaturaDoSign)
				Nota($"  --     sign: {_assinaturaDoSign}\n  --     {nome}: {assinatura}");
		}

		Nota($"  --     [{nome}] Ki em {RazaoDeKi * 100:0}% do tanque");
		Fotografar(nome, corpo);
		_espera = 0.5;
	}

	/// <summary>A assinatura do material no `ui_sign`, pra o `ui_perfected` ter com o que se comparar.</summary>
	private string? _assinaturaDoSign;

	// =====================================================================
	// 3. O KI NAO MANDA NA NUVEM
	// =====================================================================
	private void Carregar(bool ligado, string porque)
	{
		C?.SendCarregar(ligado);
		Nota($"  --     {porque}");
		_espera = ligado ? 7.0 : 0.6;
	}

	/// <summary>
	/// A CHAMA DA CARGA ACENDE POR CIMA -- E SO UMA. "Duas chamas empilhadas" ja foi defeito
	/// fotografado (ver <see cref="Aura.ChamaDaCarga"/>): a `CargaVisual` e a `Aura` desenham a MESMA
	/// arte, e quando as duas ficam visiveis o jogador ve uma clara atras e a colorida na frente.
	///
	/// A nuvem entra nisso porque ela e um TERCEIRO desenho no mesmo corpo, e o pedido foi que ela
	/// pertenca a FORMA e nao ao Ki: passar de 100% nao pode apaga-la nem substitui-la.
	/// </summary>
	private void ASobrecargaNaoEmpilhaChama()
	{
		if (Corpo is not { } corpo) return;
		var neb = corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa");
		var aura = corpo.GetNodeOrNull<Aura>("Aura");
		var carga = corpo.GetNodeOrNull<CargaVisual>("Carga");

		bool chamaDaCarga = carga?.DesenhoDeTeste.Visible == true;
		bool chamaDaForma = aura?.DesenhoDeTeste.Visible == true;

		Nota($"  --     [carga] Ki em {RazaoDeKi * 100:0}% do tanque "
				  + $"(chama da carga={chamaDaCarga}, chama da forma={chamaDaForma})");

		// GUARDADO PRA O OUTRO LADO DA MOEDA. O passo do Ki caindo vai cobrar que esta MESMA medicao
		// diga o contrario -- e e a mudanca dela, entre os dois momentos, que prova que ela enxerga.
		_chamaComKiAlto = chamaDaCarga;
		_kiAlto = RazaoDeKi;

		Conferir(RazaoDeKi > 1.0, $"segurar C passou dos 100% ({RazaoDeKi * 100:0}%)");
		Conferir(chamaDaCarga, "a chama da carga acendeu");
		Conferir(!(chamaDaCarga && chamaDaForma),
				 "NAO ha duas chamas empilhadas (so um dos dois desenhos esta visivel)");
		// O PONTO DESTA BANCADA. A nuvem e da forma; a chama e do Ki. Uma nao pode desligar a outra.
		Conferir(neb?.ForcaDeTeste == 1f,
				 $"com o Ki ACIMA de 100% a nuvem continua acesa (forca={neb?.ForcaDeTeste:0.##})");

		Fotografar("carga", corpo);
		_espera = 0.4;
	}

	/// <summary>
	/// O KI CAINDO SOZINHO. Depois de soltar o C, o dreno da forma nao-masterizada derruba o tanque;
	/// esta enquete espera ele cruzar a metade e fotografa NA HORA -- se esperasse um prazo fixo, o
	/// corpo ja poderia ter caido da forma (o ciclo de Ki e quem derruba) e a foto seria da base.
	/// </summary>
	private void OKiCaindo()
	{
		double r = RazaoDeKi;
		var neb = Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa");

		bool aindaVestido = neb?.ForcaDeTeste == 1f;

		// ============================ O QUE SE GUARDA E O MENOR KI COM A NUVEM ACESA ============================
		// A versao anterior lia o ULTIMO quadro e cobrava a nuvem acesa nele. Uma rodada mostrou o
		// estrago: `FALHA -- a nuvem continua acesa com o Ki em 100%`, num efeito intacto.
		//
		// O que tinha acontecido e que o dreno derrubou a forma aos 28 s -- que e o CICLO DE KI fazendo
		// o trabalho dele, nao um defeito da nuvem -- e, ao voltar pra base, o teto de Ki encolhe junto
		// com a forma, entao a RAZAO salta de volta pra 100%. O numero lido, "100%", nunca existiu
		// enquanto o corpo estava vestido: era Ki de base medido depois da queda.
		//
		// A pergunta certa nao e "como esta a nuvem no fim da espera" -- e **ate que Ki ela ficou
		// acesa**. Isso e um minimo ao longo da enquete, e ele so conta quando o corpo esta vestido.
		// ====================================================================================================
		if (aindaVestido && r < _kiMinimoComNuvem) _kiMinimoComNuvem = r;

		// O ALVO E 45%, e a saida por queda da forma NAO e mais um caminho de falha (e so o fim da
		// coleta): o quanto o dreno consegue descer antes de derrubar o corpo varia por rodada.
		if (r > 0.45 && aindaVestido && _voltas++ < 90)
		{
			_passo--;              // repete este mesmo passo
			_espera = 0.5;
			return;
		}

		Nota($"  --     [ki-baixo] o dreno rodou {_voltas * 0.5:0.0}s; agora o Ki esta em {r * 100:0}% "
		   + $"e o corpo {(aindaVestido ? "continua" : "NAO esta mais")} vestido");
		// O TETO E 90% e nao "a metade", e o motivo e o de cima: quem manda no piso e o dreno, que
		// derruba a forma numa hora que a bancada nao controla. Noventa por cento ja esta do outro lado
		// do portao que governa a CHAMA (que exige C ou passar de 100%) -- que e exatamente a fronteira
		// que esta linha existe pra provar que a nuvem nao respeita.
		Conferir(_kiMinimoComNuvem <= 0.90,
				 $"a nuvem ficou acesa com o Ki ABAIXO do portao da chama: vista acesa ate "
			   + $"{_kiMinimoComNuvem * 100:0}% do tanque -- ela e da FORMA, nao do Ki");

		// ============================ O OUTRO SENTIDO DA REGRA, E ELE E O CONTROLE ============================
		// A regra do dono tem DOIS lados e a bancada media so um deles: *"e pra ela vir desativada e so
		// ativar se o ki passar de 100% ou eu apertar C"*. A chama, ao contrario da nuvem, TEM que
		// obedecer ao Ki -- e aqui o C esta solto e o tanque abaixo de 100%, que e o unico estado em
		// que ela deve estar apagada.
		//
		// E ela e o CONTROLE NEGATIVO da linha de cima. "A nuvem nao mudou" so vale alguma coisa se o
		// instrumento soubesse ver uma mudanca; a mesma medicao (`DesenhoDeTeste.Visible`, no mesmo
		// corpo, nos mesmos dois momentos) viu a chama ACESA no passo do Ki alto e a ve APAGADA agora.
		// Se as duas dessem o mesmo valor, o par inteiro seria um instrumento cego dando verde.
		// ================================================================================================
		bool chamaDaCarga = Corpo?.GetNodeOrNull<CargaVisual>("Carga")?.DesenhoDeTeste.Visible == true;
		Conferir(!chamaDaCarga,
				 $"e a CHAMA DA CARGA esta APAGADA com o C solto e o Ki em {r * 100:0}% -- ela obedece "
			   + "ao Ki (a regra do dono), a nuvem nao");
		Conferir(_chamaComKiAlto && !chamaDaCarga,
				 $"O CONTROLE FECHA: a MESMA medicao viu a chama acesa a {_kiAlto * 100:0}% e apagada a "
			   + $"{r * 100:0}% -- entao 'a nuvem nao se mexeu' e um fato medido, e nao um sensor morto");

		// SE O DRENO DERRUBOU A FORMA nao ha nada a consertar aqui: o proximo passo do roteiro e
		// `admin_forma ui_perfected`, que veste de novo (e a estreia dele ja foi queimada la em cima).
		// Todos os passos de baixo -- o segundo estagio, o par de subtracao, a elipse e as quatro
		// medidas de particula -- pegam a nuvem acesa de qualquer jeito.
		if (!aindaVestido)
			Nota("  --     (o dreno derrubou a forma antes do alvo; o passo seguinte ja veste de novo)");

		if (aindaVestido) Fotografar("ki-baixo", Corpo);
		_espera = 0.4;
	}

	private bool _chamaComKiAlto;
	private double _kiAlto = double.NaN;

	/// <summary>O menor Ki em que a nuvem foi VISTA acesa. Ver <see cref="OKiCaindo"/>.</summary>
	private double _kiMinimoComNuvem = double.PositiveInfinity;

	/// <summary>O penteado da BASE. Ver <see cref="ANebulosaSoEDoUltraInstinto"/>.</summary>
	private string _cabeloNaBase = "";

	/// <summary>
	/// Apaga (ou reacende) SO a nuvem, sem mexer na forma, e fotografa. O par vira uma subtracao fora
	/// do jogo. Mexer no node direto aqui e legitimo e nao contradiz o resto da bancada: os passos de
	/// cima ja provaram que o caminho de PRODUCAO acende: este par existe pra localizar o desenho, nao
	/// pra provar o encanamento.
	/// </summary>
	/// <summary>
	/// A ELIPSE CHAPADA CAI EM CIMA DO CORPO? Mede o CENTROIDE dos pixels que o modo diagnostico
	/// pintou (indigo/violeta: azul bem acima do verde) e compara com o centro do recorte, que e o
	/// corpo.
	///
	/// ============================ POR QUE ESTA CHECAGEM EXISTE ============================
	/// Todas as outras linhas desta bancada perguntam a um NODE o que ele acha que esta fazendo, e
	/// foi por isso que elas deram verde durante duas rodadas inteiras com a nuvem desenhada fora do
	/// personagem. O node estava na posicao certa; o DESENHO e que saia deslocado, porque a ancora do
	/// quad se perdia num arredondamento de meio pixel. Nenhuma pergunta feita ao C# podia ver isso.
	///
	/// Esta ve, porque ela le a tela. O modo chapado existe justamente pra dar a ela um alvo com
	/// borda: a nuvem de verdade muda a cor media do fundo em 2 unidades de 255 e nao tem contorno
	/// pra localizar.
	/// ==================================================================================
	/// </summary>
	private void AElipseCaiNoCorpo()
	{
		Image? img = Recorte(Corpo);
		if (img == null) { Nota("  --     [chapado] sem foto (headless nao renderiza)"); return; }
		img.SavePng(ProjectSettings.GlobalizePath("user://neb-chapado.png"));

		double sx = 0, sy = 0; int n = 0;
		for (int y = 0; y < img.GetHeight(); y++)
		for (int x = 0; x < img.GetWidth(); x++)
		{
			Color c = img.GetPixel(x, y);
			if (c.B * 255 > c.G * 255 + 25 && c.B > 0.23f) { sx += x; sy += y; n++; }
		}

		if (n < 200) { Conferir(false, $"a elipse chapada aparece na foto (so {n} px indigo)"); return; }

		// O recorte esta em pixel de TELA e o centro dele e o corpo; o desvio volta pra pixel de MUNDO
		// pelo zoom, que e a unidade em que o defeito foi diagnosticado (meio quad = 48).
		int zoom = Mathf.Max(World.Instancia?.ZoomDeTeste ?? 1, 1);
		double dx = (sx / n - img.GetWidth() / 2.0) / zoom;
		double dy = (sy / n - img.GetHeight() / 2.0) / zoom;
		double d = Math.Sqrt(dx * dx + dy * dy);

		// O TETO E 12 PX DE MUNDO e ele nao e frouxo por acaso: o centroide de uma elipse cortada pela
		// borda da foto puxa alguns pixels, e o corpo desenhado POR BAIXO dela tira outros tantos do
		// azul (o veu do `veu_no_corpo` deixa o boneco aparecer no miolo). O vies pra cima que folgava
		// isto antes (`centro.y = 0.55`) morreu junto com o quad de 96: a elipse agora e centrada na
		// silhueta medida. O defeito que esta linha persegue media 70 px de distancia -- nao ha risco de
		// confundir os dois.
		Conferir(d < 12,
				 $"a elipse desenhada CAI NO CORPO (centro a {d:0.0} px de mundo dele: "
			   + $"{dx:+0.0;-0.0} em x, {dy:+0.0;-0.0} em y, de {n} px medidos)");
	}

	// =====================================================================
	// 3-B. AS MICROPARTICULAS EXISTEM, E A DENSIDADE AFINA
	// =====================================================================
	private int _semBrilho = -1, _comBrilho = -1, _densidade0 = -1, _densidade1 = -1;

	/// <summary>A rampa do botao de densidade, quatro pontos. Ver o roteiro no `_Process`.</summary>
	private int _rampa25 = -1, _rampa50 = -1, _rampa75 = -1, _rampa100 = -1;

	/// <summary>Gira um botao do shader neste corpo; `null` devolve o padrao do arquivo.</summary>
	private void Afinar(string uniform, float? valor)
	{
		Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.AfinarDeTeste(uniform, valor);
		// ZERO: o proximo `_Process` e o proximo QUADRO -- e e o quadro seguinte que sai desenhado com
		// este valor. Ver o bloco no roteiro.
		_espera = 0;
	}

	/// <summary>
	/// QUANTOS PIXELS BRANCOS HA NA NUVEM, no quadro que acabou de ser desenhado.
	///
	/// A janela e a MESMA ideia do <see cref="Laudo"/> -- o retangulo que o quad ocupa na tela,
	/// perguntado ao node --, e nao um anel cravado. O bloco no corpo do metodo explica por que os dois
	/// tiveram que parar de cravar, e por que o miolo agora entra na conta.
	/// </summary>
	private bool Medir(ref int onde, string rotulo)
	{
		if (Recorte(Corpo) is not { } img)
		{
			Nota($"  --     [pontos/{rotulo}] sem foto (headless nao renderiza)");
			onde = -1;
			_espera = 0;
			return true;
		}

		// ============================ A JANELA E O QUAD, PERGUNTADO -- NAO UM ANEL CRAVADO ============================
		// Aqui havia um anel fixo, "48 a 150 px de tela", copiado do <see cref="Laudo"/>. Ele durou uma
		// tarde: outra sessao trocou o tamanho do quad de 96 px de mundo pra ~42 (derivado da silhueta
		// mais 5 px de folga, a pedido do dono) e passou a desenhar POR CIMA do corpo. Com aquele anel,
		// as quatro medidas passariam a contar uma regiao inteiramente FORA do efeito -- quatro zeros, e
		// tres falhas de um efeito intacto.
		//
		// A janela agora e o retangulo que o proprio quad ocupa na tela, que e a mesma pergunta que a
		// bancada ja fazia pra localizar o desenho. Ele encolhe, cresce ou anda com o efeito sozinho.
		//
		// E O CORPO NAO PRECISA MAIS SER EXCLUIDO: com a nuvem desenhando na frente da silhueta, tirar o
		// miolo tiraria justamente onde as particulas passam. O branco do proprio boneco entra na conta,
		// sim -- e some nas tres comparacoes, porque todas elas sao DIFERENCAS entre dois estados do
		// mesmo enquadramento, e o boneco esta parado e identico nos dois.
		// ========================================================================================================
		Rect2 quad = Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.RetanguloNaTelaDeTeste
				  ?? new Rect2(Vector2.Zero, img.GetSize());
		int x0 = Mathf.Clamp((int)quad.Position.X - _cantoDoCorte.X, 0, img.GetWidth());
		int y0 = Mathf.Clamp((int)quad.Position.Y - _cantoDoCorte.Y, 0, img.GetHeight());
		int x1 = Mathf.Clamp((int)quad.End.X - _cantoDoCorte.X, 0, img.GetWidth());
		int y1 = Mathf.Clamp((int)quad.End.Y - _cantoDoCorte.Y, 0, img.GetHeight());

		int n = 0;
		for (int y = y0; y < y1; y++)
		for (int x = x0; x < x1; x++)
		{
			Color c = img.GetPixel(x, y);
			// O MENOR DOS TRES CANAIS >= 250 e "branco puro", e nao "claro": a rampa da nuvem chega a
			// ciano quase branco (`d8e8ff` -- menor canal 216) e passaria num teste de luminancia.
			// Exigir os TRES canais no teto separa a particula da ponta clara da propria nuvem.
			if (Mathf.Min(c.R, Mathf.Min(c.G, c.B)) * 255 >= 250) n++;
		}

		if (x1 <= x0 || y1 <= y0)
		{
			Nota($"  --     [pontos/{rotulo}] o quad nao cai dentro do recorte -- medida invalida");
			onde = -1;
			_espera = 0;
			return true;
		}

		// ============================ A AMOSTRA E DE DEZ QUADROS ESPALHADOS ============================
		// Dez quadros COLADOS nao adiantariam: a 60 Hz eles cobrem 0,16 s e as particulas mal se mexem
		// -- seria o mesmo quadro contado dez vezes, com o mesmo azar. Com 0,06 s entre eles a amostra
		// atravessa ~0,6 s e pega a subida, que e onde a variacao mora.
		// ==========================================================================================
		if (_amostras == 0) onde = 0;
		onde += n;

		if (++_amostras < QuadrosPorMedida)
		{
			_passo--;          // fica neste mesmo passo, coletando
			_espera = 0.06;
			return false;
		}

		Nota($"  --     [pontos/{rotulo}] {onde} px de branco puro somados em {_amostras} quadros "
		   + $"({onde / (double)_amostras:0.0} por quadro)");
		_amostras = 0;
		_espera = 0;
		return true;
	}

	/// <summary>Quantos quadros entram em cada uma das quatro medidas. Ver <see cref="Medir"/>.</summary>
	private const int QuadrosPorMedida = 10;

	private int _amostras;

	/// <summary>
	/// O VEREDITO DAS QUATRO MEDIDAS. Duas diferencas, e uma terceira linha que julga o INSTRUMENTO.
	///
	/// ============================ POR QUE DIFERENCA E NAO VALOR ABSOLUTO ============================
	/// "ha mais de N brancos no anel" seria uma checagem sobre o CENARIO tanto quanto sobre o efeito:
	/// neve, uma nuvem do clima ou uma pedra clara passando entram na mesma contagem. A diferenca entre
	/// dois quadros consecutivos, com uma unica coisa mudando entre eles, cancela tudo o que nao e este
	/// shader -- e a mesma logica do par de subtracao que localizou a nuvem.
	/// ==========================================================================================
	/// </summary>
	private void AsParticulasExistemEAfinam()
	{
		if (_semBrilho < 0 || _comBrilho < 0 || _densidade0 < 0 || _densidade1 < 0
		 || _rampa25 < 0 || _rampa100 < 0)
		{
			Nota("  --     [pontos] sem foto nesta rodada -- as quatro medidas nao existem "
			   + "(headless? entao rode com janela)");
			_espera = 0.3;
			return;
		}

		int existem = _comBrilho - _semBrilho;
		int afina = _densidade1 - _densidade0;

		// ============================ O PISO E 25 PX SOMADOS EM DEZ QUADROS ============================
		// Ou seja ~2,5 px de branco por quadro. A conta que sustenta o numero: uma particula tem 1 px de
		// MUNDO e o zoom medido em jogo e 2, entao cada uma pinta ~4 px de tela; o pior quadro ja
		// observado numa rodada sadia deu 6 px (a melhor deu 25), o que poe o piso de uma amostra de dez
		// quadros em ~60 no pior caso. Vinte e cinco fica com folga de mais de duas vezes pra baixo
		// disso e ainda assim MUITO acima de zero -- o defeito que esta linha persegue (a camada nao
		// desenhar nada, ou o botao nao ser lido) da exatamente 0.
		//
		// O NUMERO POR QUADRO VAI NO LOG de proposito, e nao so o veredito: se a densidade cair pela
		// metade um dia, esta linha ainda passa -- e quem ler o log vai ver 12 onde havia 25.
		// ==========================================================================================
		Conferir(existem >= 25,
				 $"as MICROPARTICULAS existem e quem as desenha e esta camada: apagar `pontos_brilho` "
			   + $"tira {existem} px de branco do anel ({_comBrilho} com, {_semBrilho} sem)");

		Conferir(afina >= 25,
				 $"e a DENSIDADE afina de verdade: `pontos_densidade` de 0 a 1 poe {afina} px de branco "
			   + $"({_densidade0} em 0, {_densidade1} em 1) -- o botao move a tela, nao so o arquivo");

		// ============================ O INSTRUMENTO SE JULGA ============================
		// `pontos_brilho = 0` e `pontos_densidade = 0` sao DOIS caminhos pro mesmo estado: nenhum ponto
		// desenhado. Eles tem que medir quase a mesma coisa. Se nao medirem, o que varia entre os
		// quadros nao sao os pontos -- e ai as duas diferencas acima estao medindo outra coisa e nao
		// valem nada, mesmo tendo dado verde.
		// ============================================================================
		int discordancia = Math.Abs(_semBrilho - _densidade0);
		Conferir(discordancia <= Math.Max(existem, afina) / 2,
				 $"e as DUAS maneiras de apagar os pontos medem a mesma coisa (diferem em "
			   + $"{discordancia} px) -- e isso que torna as duas linhas de cima confiaveis");

		// --- A RAMPA: o SENTIDO do botao, com a variancia a mostra ---
		Nota($"  --     [pontos] rampa da densidade (px somados em {QuadrosPorMedida} quadros): "
		   + $"0,25 -> {_rampa25}  |  0,50 -> {_rampa50}  |  0,75 -> {_rampa75}  |  1,00 -> {_rampa100}");
		Conferir(_rampa100 > _rampa25,
				 $"e o botao SOBE: quatro vezes o portao ({_rampa25} em 0,25 contra {_rampa100} em 1,00) "
			   + "-- 'subir enche' e o que o comentario do shader promete a quem for afinar");

		Fotografar("pontos", Corpo);
		_espera = 0.3;
	}

	// =====================================================================
	// 3-C. E **SO** O ULTRA INSTINTO, NO CAMINHO DE PRODUCAO
	// =====================================================================
	/// <summary>
	/// Uma forma que nao e Ultra Instinto NAO acende a nuvem -- medido no corpo, e nao no catalogo.
	///
	/// ============================ A PRIMEIRA LINHA E O CONTROLE NEGATIVO DA SEGUNDA ============================
	/// "a nuvem esta apagada" e verdade num corpo que nunca se transformou, num corpo que nao existe e
	/// num jogo que nao rodou. Sozinha, ela e a checagem mais facil de passar por acidente que ha nesta
	/// bancada -- e seria exatamente o verde vazio que o dono mandou nao entregar.
	///
	/// Entao antes dela se cobra que o corpo TENHA MESMO trocado de forma: o contorno da forma no
	/// material do boneco e um efeito que o `ssj1` liga e que a base nao tem. Com as duas juntas a
	/// frase vira a que interessa: **o corpo se transformou E a nuvem continuou apagada.**
	/// ======================================================================================================
	/// </summary>
	private void ANebulosaSoEDoUltraInstinto()
	{
		if (Corpo is not { } corpo) return;
		var neb = corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa");
		var vis = corpo.GetNodeOrNull<CharacterVisual>("Visual");

		string cabelo = vis?.CabeloDeTeste ?? "";

		// ============================ O SINAL E O CABELO, E ELE E COMPARADO COM A BASE ============================
		// A PRIMEIRA VERSAO DESTA LINHA ERA VACUA, e a propria rodada mostrou: ela pedia
		// `contorno > 0 || cabelo.Length > 0`, e passou pelo lado do cabelo -- que e um caminho de
		// arquivo NAO-VAZIO em qualquer forma, base inclusive. Ou seja, o "controle" dava verde num
		// corpo que nao tivesse trocado de nada, que e exatamente o que ele existia pra impedir.
		//
		// E o contorno nao servia mesmo: ele NAO acende abaixo dos 100% de Ki, por pedido do dono
		// (`CharacterVisual.AnimarContorno`, a guarda `_auraDaForma <= 0`) -- e aqui o tanque esta na
		// metade depois do dreno. A rodada mediu `contorno = 0` num `ssj1` legitimo.
		//
		// O que distingue de verdade e a MUDANCA: o penteado da base contra o da forma. Guardado no
		// passo 0, quando o corpo estava comprovadamente na base.
		// ======================================================================================================
		Conferir(_cabeloNaBase.Length > 0 && cabelo != _cabeloNaBase,
				 $"o corpo VESTIU MESMO o `ssj1`: o penteado MUDOU em relacao a base "
			   + $"(`{_cabeloNaBase.GetFile()}` -> `{cabelo.GetFile()}`) -- sem esta linha a de baixo "
			   + "passaria verde num corpo que nao trocou de nada");
		Nota($"  --     (o contorno da forma esta em {vis?.AuraDaFormaDeTeste ?? 0f:0.##}, e isso e "
		   + "ESPERADO: ele so acende acima dos 100% de Ki -- por isso ele nao serve de controle aqui)");

		Conferir(neb?.ForcaDeTeste == 0f,
				 $"e em `ssj1` a nuvem esta APAGADA (forca={neb?.ForcaDeTeste:0.##}) -- o caminho de "
			   + "producao obedece ao catalogo, e a nebulosa nao vem de brinde com toda forma");

		Fotografar("ssj1", corpo);
		_espera = 0.4;
	}

	private void Chapado(bool ligado)
	{
		Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.DiagnosticoChapado(ligado);
		Nota($"  --     modo diagnostico (elipse chapada) {(ligado ? "LIGADO" : "desligado")}");
		_espera = 0;
	}

	private void Moldura(bool ligado)
	{
		if (Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is not { } neb) return;
		neb.DiagnosticoMoldura(ligado);
		// O RETANGULO MEDIDO AGORA, e nao no passo 0. A primeira versao media no comeco da bancada e
		// eu comparei aquele numero com uma foto tirada dois minutos e duas transformacoes depois --
		// se alguma coisa mexe na escala do corpo no meio (e ha formas que incham o boneco), a
		// comparacao estava condenada desde o inicio.
		Rect2 r = neb.RetanguloNaTelaDeTeste;
		Vector2 pc = Corpo!.GetGlobalTransformWithCanvas().Origin;
		Nota($"  --     NA HORA DA FOTO: corpo na tela {pc}  |  quad {r.Size.X:0}x{r.Size.Y:0} px "
		   + $"centro {r.Position + r.Size / 2}  |  escala do corpo {Corpo.Scale}");
		_espera = 0;
	}

	// =====================================================================
	// A PILHA DO CORPO: A NUVEM NA FRENTE DO BONECO, ATRAS DO CENARIO
	// =====================================================================
	/// <summary>
	/// A NUVEM E A IRMA MAIS NOVA DOS QUATRO DESENHOS DO CORPO? Devolve a resposta e o laudo.
	///
	/// ============================ POR QUE ISTO E UMA FUNCAO, E NAO QUATRO LINHAS SOLTAS ============================
	/// Ela e chamada DUAS vezes com o mesmo corpo: uma com a pilha de producao (tem que passar) e uma com
	/// a nuvem empurrada pra posicao 0 (tem que reprovar). Uma comparacao escrita inline no passo nao pode
	/// ser exercitada ao contrario, e "a nuvem esta na frente" e exatamente o tipo de assercao que passa
	/// verde por estar invertida -- ela JA ESTEVE invertida neste arquivo, e com razao, enquanto o efeito
	/// era pra ficar atras do boneco.
	///
	/// ============================ E A OUTRA PONTA MORA EM OUTRO LUGAR ============================
	/// Esta funcao responde metade do pedido: por cima do CORPO. A outra metade -- **atras do CENARIO** --
	/// nao se decide por ordem de irmao nenhuma, e sim pelo `ZIndex` continuar ZERO (z vence Y-sort). Por
	/// isso o z entra NESTA conta em vez de virar uma linha propria: as duas pontas do pedido do dono sao
	/// duas propriedades do MESMO desenho, e separa-las deixaria a segunda sem quem a cobre. Quem mede a
	/// consequencia na tela e o <see cref="OCenarioTapaANuvem"/>, com foto embaixo de uma copa.
	/// ========================================================================================
	/// </summary>
	private static (bool Ok, string Laudo) ANuvemEAIrmaMaisNova(Node2D corpo)
	{
		int iNeb = corpo.GetNodeOrNull<Node>("Nebulosa")?.GetIndex() ?? -1;
		int iAura = corpo.GetNodeOrNull<Node>("Aura")?.GetIndex() ?? -1;
		int iCarga = corpo.GetNodeOrNull<Node>("Carga")?.GetIndex() ?? -1;
		int iVis = corpo.GetNodeOrNull<Node>("Visual")?.GetIndex() ?? -1;
		int z = corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.CamadaDeTeste ?? -99;

		// OS QUATRO ACHADOS E O Z EM ZERO fazem parte da MESMA pergunta. Um `-1` (node que nao existe)
		// nao pode virar "esta na frente" por acidente de comparacao de inteiros, e um z positivo poria a
		// nuvem na frente do corpo E das arvores -- o que ninguem pediu.
		bool ok = iAura >= 0 && iCarga >= 0 && iVis >= 0 && iNeb >= 0
			   && iAura < iCarga && iCarga < iVis && iVis < iNeb && z == 0;

		return (ok, $"a nuvem e a irma MAIS NOVA dos quatro (aura {iAura} < carga {iCarga} < boneco "
				  + $"{iVis} < nebulosa {iNeb}) e o `ZIndex` dela e {z} -- e por isso que ela desenha SOBRE "
				  + "o corpo e AINDA ASSIM atras da arvore");
	}

	// =====================================================================
	// A FOLGA E DERIVADA DO BONECO -- nao e um numero que alguem escreveu
	// =====================================================================
	/// <summary>
	/// OS 5 PIXELS DO DONO, LITERAIS: *"no MAXIMO 5 pixels de distancia do corpo"*. E o unico numero
	/// desta secao que nao sai de medida nenhuma -- ele E o pedido.
	/// </summary>
	private const float FolgaPedida = 5f;

	/// <summary>
	/// A REGRA DA FOLGA, ESCRITA AQUI E NAO IMPORTADA: a caixa do alfa dos quadros PARADOS da folha, mais
	/// os 5 px do dono, e um quad que e exatamente a elipse.
	///
	/// ============================ POR QUE A BANCADA REFAZ A CONTA EM VEZ DE LER O RESULTADO ============================
	/// A bancada ja lia a folga (`FolgaDeTeste`) e ja media a folga NA FOTO. Nenhuma das duas responde a
	/// pergunta que o pedido do dono realmente faz -- *"no MAXIMO 5 pixels de distancia do corpo"* e uma
	/// DISTANCIA, e distancia precisa de um corpo. Um `_lado = 36` cravado no `.cs` passaria pelas duas: a
	/// folga sai da diferenca entre duas elipses (e continuaria de 5 px), e a foto de HOJE mostraria o
	/// tamanho certo -- porque 36 e, hoje, o numero certo. Ele so estaria errado na PROXIMA folha, e o jogo
	/// tem folhas de 96 (o Oozaru) e trocas de corpo por forma (o SSJ4).
	///
	/// Chamar `Silhueta()` do node compararia a conta com ela mesma. Entao esta funcao le a FOLHA e remede
	/// o alfa: a fonte de verdade e a ARTE, e o que se cobra e que o node tenha chegado no mesmo numero que
	/// a arte manda.
	///
	/// A REGRA, LITERAL (ver `NebulosaDaForma.ModelarPeloCorpo`): `rx = floor(meiaLargura + 5)`,
	/// `ry = floor(meiaAltura + 5)`, `lado = 2 x max(rx, ry)`. O `floor` e o pedido e nao gosto -- "no
	/// MAXIMO 5" nao sobrevive a um arredondamento pra cima.
	/// ============================================================================================================
	/// </summary>
	private static (int Lado, Vector2 Semi, Vector2 Silhueta, float LadoDoQuadro)? RegraDaFolga(SpriteFrames? folha)
	{
		if (folha == null) return null;

		// OS QUADROS PARADOS, e nao o que esta tocando: as folhas base tem poses de alfa CHEIO (o `sp` e
		// as quatro do `take_off` sao 32x32 opacas) e medir uma delas devolveria a caixa do QUADRO com
		// outro nome. Mesmo motivo escrito no `NebulosaDaForma.QuadrosParados`.
		var quadros = new List<Texture2D>();
		foreach (StringName nome in folha.GetAnimationNames())
			if (nome.ToString().StartsWith("walk_", StringComparison.Ordinal)
			 && folha.GetFrameCount(nome) > 0 && folha.GetFrameTexture(nome, 0) is { } t)
				quadros.Add(t);

		if (quadros.Count == 0)
			foreach (StringName nome in folha.GetAnimationNames())
				if (folha.GetFrameCount(nome) > 0 && folha.GetFrameTexture(nome, 0) is { } t)
				{ quadros.Add(t); break; }

		if (quadros.Count == 0) return null;

		float largura = quadros[0].GetWidth();
		int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue;
		var imagens = new Dictionary<Texture2D, Image>();

		foreach (Texture2D quadro in quadros)
		{
			Texture2D dono = quadro is AtlasTexture at && at.Atlas != null ? at.Atlas : quadro;
			if (!imagens.TryGetValue(dono, out Image? img))
			{
				if (dono.GetImage() is not { } lida) continue;
				img = lida;
				imagens[dono] = img;
			}

			Rect2I regiao = quadro is AtlasTexture a2 && a2.Atlas != null
				? new Rect2I((Vector2I)a2.Region.Position, (Vector2I)a2.Region.Size)
				: new Rect2I(0, 0, img.GetWidth(), img.GetHeight());

			for (int y = 0; y < regiao.Size.Y; y++)
				for (int x = 0; x < regiao.Size.X; x++)
				{
					if (img.GetPixel(regiao.Position.X + x, regiao.Position.Y + y).A < 0.1f) continue;
					if (x < x0) x0 = x;
					if (x > x1) x1 = x;
					if (y < y0) y0 = y;
					if (y > y1) y1 = y;
				}
		}
		if (x1 < x0 || y1 < y0) return null;

		// HORIZONTALMENTE O QUE VALE E O ALCANCE (a arte e centrada no quadro), verticalmente a caixa.
		float meiaLargura = Mathf.Max(largura * 0.5f - x0, x1 + 1f - largura * 0.5f);
		float meiaAltura = (y1 + 1f - y0) * 0.5f;

		int rx = Mathf.Max(Mathf.FloorToInt(meiaLargura + FolgaPedida), 2);
		int ry = Mathf.Max(Mathf.FloorToInt(meiaAltura + FolgaPedida), 2);
		return (2 * Mathf.Max(rx, ry), new Vector2(rx, ry), new Vector2(meiaLargura, meiaAltura), largura);
	}

	/// <summary>
	/// A FOLGA CABE, TEM CHAO, E O QUAD NAO SOBRA. Recebe os tres numeros de fora de proposito: e a mesma
	/// funcao que julga o efeito de verdade e os tres defeitos injetados.
	///
	/// TRES PERGUNTAS, e cada uma fecha um jeito diferente de a regra morrer:
	///   * TETO   -- folga &lt;= 5 nos dois eixos. E o pedido, literal.
	///   * CHAO   -- folga &gt;= 4. Sem ele, encolher a nuvem pra DENTRO do boneco (ou pra nada) passaria
	///               verde, porque -20 tambem e menor que 5. O `floor` da regra garante [4, 5].
	///   * SOBRA  -- `2 x maior semi-eixo == lado do quad`. Esta e a que faltava: a folga e uma DIFERENCA
	///               entre duas elipses, e as duas cabem num quad de qualquer tamanho. O quad de 96 px que
	///               o dono reclamou passaria pelas outras duas sem uma queixa.
	/// </summary>
	private static (bool Ok, string Laudo) AFolgaCabe(float lado, Vector2 semi, Vector2 silhueta)
	{
		Vector2 folga = semi - silhueta;
		bool teto = folga.X <= FolgaPedida + 0.01f && folga.Y <= FolgaPedida + 0.01f;
		bool chao = folga.X >= FolgaPedida - 1.01f && folga.Y >= FolgaPedida - 1.01f;
		bool semSobra = Mathf.Abs(2 * Mathf.Max(semi.X, semi.Y) - lado) < 0.01f;

		return (teto && chao && semSobra,
				$"folga ({folga.X:0.##}, {folga.Y:0.##}) px de mundo [teto {teto}, chao {chao}]  |  "
			  + $"elipse {semi.X:0.#}x{semi.Y:0.#} num quad de {lado:0} px [sem sobra {semSobra}]  |  "
			  + $"silhueta medida {silhueta.X:0.##}x{silhueta.Y:0.##}");
	}

	/// <summary>
	/// ============================ A FOLGA E DERIVADA DO BONECO, E O DONO PEDIU 5 PX ============================
	/// Dois requisitos, e eles nao se provam juntos: **cabe em 5 px** (um teto) e **sai do corpo** (uma
	/// derivacao). A bancada media o primeiro de tres maneiras e o segundo de nenhuma -- e era o segundo que
	/// estava em jogo, porque o defeito original era exatamente um numero escrito a mao (`Lado = 96`, lido
	/// da referencia "em corpos") que ninguem tinha do que derivar.
	///
	/// AQUI A CONTA E REFEITA A PARTIR DA ARTE (ver <see cref="RegraDaFolga"/>) e comparada com o que o
	/// shader RECEBEU. As duas metades:
	///   1. na folha que o boneco esta vestindo AGORA, o node chegou no mesmo numero que a arte manda -- um
	///      lado cravado reprova aqui no dia em que a arte mudar, e reprova HOJE se o numero cravado nao
	///      for por acaso o certo;
	///   2. numa folha DE OUTRO TAMANHO (a do Oozaru, 96x96, carregada do disco), a MESMA regra continua
	///      dando 5 px de folga -- ou seja a regra e uma regra, e nao uma calibragem pra folha de 32.
	///
	/// E os tres defeitos injetados fecham os tres jeitos de o teto virar enfeite. Ver
	/// <see cref="AFolgaCabe"/>.
	/// ======================================================================================================
	/// </summary>
	private void AFolgaEDerivada()
	{
		Nota("== A FOLGA E DERIVADA DO BONECO (a regra refeita a partir da ARTE) ==");

		if (Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is not { } neb) return;
		SpriteFrames? folha = Corpo.GetNodeOrNull<CharacterVisual>("Visual")?.FolhaDoCorpo;

		// --- 1. O QUE O SHADER RECEBEU, JULGADO PELAS TRES PERGUNTAS ---
		(bool ok, string laudo) = AFolgaCabe(neb.LadoDeTeste, neb.SemiEixosDeTeste, neb.SilhuetaDeTeste);
		Conferir(ok, "a nuvem cola no boneco: " + laudo);

		// --- 2. E O NUMERO SAIU DA ARTE ---
		Conferir(folha != null, "a folha do CORPO esta acessivel pra a bancada remedir o alfa dela");
		if (RegraDaFolga(folha) is not { } r)
		{
			Conferir(false, "a bancada conseguiu remedir a silhueta da folha (sem isso a derivacao nao e "
						  + "verificavel -- so o teto seria)");
			return;
		}

		Nota($"  --     a ARTE manda: lado {r.Lado} px, elipse {r.Semi.X:0}x{r.Semi.Y:0}, silhueta "
		   + $"{r.Silhueta.X:0.##}x{r.Silhueta.Y:0.##}  |  o SHADER recebeu: lado {neb.LadoDeTeste:0}, "
		   + $"elipse {neb.SemiEixosDeTeste.X:0}x{neb.SemiEixosDeTeste.Y:0}, silhueta "
		   + $"{neb.SilhuetaDeTeste.X:0.##}x{neb.SilhuetaDeTeste.Y:0.##}");

		Conferir(Mathf.Abs(neb.SilhuetaDeTeste.X - r.Silhueta.X) < 0.01f
			  && Mathf.Abs(neb.SilhuetaDeTeste.Y - r.Silhueta.Y) < 0.01f,
				 "a silhueta que o shader recebeu e a caixa do ALFA da folha, remedida pela bancada");
		Conferir(Mathf.Abs(neb.LadoDeTeste - r.Lado) < 0.01f,
				 $"e o lado do quad e DERIVADO dela ({neb.LadoDeTeste:0} contra os {r.Lado} que a arte "
			   + "manda) -- nao ha tamanho escrito em lugar nenhum");

		// E A SILHUETA NAO E A CAIXA DO QUADRO. Esta linha separa "mediu o alfa" de "mediu a moldura", que
		// e o defeito menor da mesma familia: contar os 5 px da borda do QUADRO daria 13 px de folga do
		// BONECO nos lados. O quadro do corpo e 32; a silhueta em pe ocupa ~16 de largura util.
		Conferir(r.Silhueta.X < r.LadoDoQuadro * 0.5f - 0.5f,
				 $"a silhueta medida ({r.Silhueta.X:0.##}) e MENOR que a meia-largura do quadro "
			   + $"({r.LadoDoQuadro * 0.5f:0.#}) -- ou seja o que se mediu foi o desenho, e nao a moldura");

		// --- 3. A MESMA REGRA NUMA FOLHA DE OUTRO TAMANHO ---
		// ============================ POR QUE A DO OOZARU, E POR QUE DO DISCO ============================
		// A afirmacao do `NebulosaDaForma.Folga` e que "o corpo de 32 produz um quad de 42; o Oozaru, de
		// folha 96, produziria um de ~106 -- com os mesmos 5 px em volta, sem ninguem recalcular nada". Isso
		// e uma promessa sobre uma folha que este corpo nao esta vestindo, e ela nunca foi conferida.
		//
		// LER A FOLHA DO DISCO em vez de FORCAR O OOZARU e escolha de custo: a cinematica do macaco e sempre
		// de ESTREIA (`Cinematicas.Degrau`: a linha Oozaru nunca dispensa cena) e prenderia esta bancada por
		// mais de um minuto pra medir uma conta que nao depende de haver macaco nenhum. O que se cobra aqui e
		// a REGRA; que o node a aplique de novo quando a folha troca ja esta coberto pelo item 2, no corpo
		// que esta na tela.
		// ==========================================================================================
		var outra = GD.Load<SpriteFrames>("res://Assets/Sprites/Character Icons/Transformations/oozaruhayate.tres");
		if (RegraDaFolga(outra) is { } o)
		{
			(bool okOozaru, string laudoOozaru) = AFolgaCabe(o.Lado, o.Semi, o.Silhueta);
			Conferir(okOozaru, "a MESMA regra numa folha de 96 (a do Oozaru) tambem da 5 px: " + laudoOozaru);
			Conferir(o.Lado > r.Lado + 20,
					 $"e o quad dela e MUITO maior ({o.Lado} contra {r.Lado} px) -- a regra acompanha a "
				   + "folha, o que um numero cravado nao faria");
		}
		else Conferir(false, "a folha do Oozaru carregou e deu pra remedir (a segunda folha e o que prova "
						   + "que a regra e regra, e nao calibragem pra folha de 32)");

		// --- 4. E O JULGAMENTO ENXERGA? OS TRES DEFEITOS INJETADOS ---
		// Sem estas tres linhas, `AFolgaCabe` podia estar devolvendo `true` por qualquer motivo -- inclusive
		// por sempre devolver `true` -- e as duas metades acima seriam verde vazio.
		Nota("  --     agora os DEFEITOS INJETADOS: as tres linhas abaixo tem que dizer que reprovaram");
		Vector2 semi = neb.SemiEixosDeTeste, sil = neb.SilhuetaDeTeste;

		Conferir(!AFolgaCabe(96f, semi, sil).Ok,
				 "[injetado] o QUAD DE 96 px do defeito original reprova (a elipse cabe, o quad SOBRA) -- "
			   + AFolgaCabe(96f, semi, sil).Laudo);
		Conferir(!AFolgaCabe(2 * Mathf.Max(semi.X + 15, semi.Y + 15), semi + new Vector2(15, 15), sil).Ok,
				 "[injetado] 20 px de folga (a nuvem VOLTANDO a crescer) reprova pelo teto");
		Conferir(!AFolgaCabe(neb.LadoDeTeste, semi, semi).Ok,
				 "[injetado] folga ZERO (a nuvem encolhida pra dentro do boneco) reprova pelo chao -- e "
			   + "este e o defeito que um teto sozinho nunca pegaria");
		_espera = 0.5;
	}

	/// <summary>
	/// Some com o personagem inteiro por um quadro -- corpo, cabelo, olhos, roupa e rabo, que sao todos
	/// filhos do `Visual` (`CharacterVisual.NovaCamada`). A nebulosa, a aura e a barra de vida NAO sao,
	/// e e de proposito: elas tem que ficar em pe nas duas fotos pra cancelarem na subtracao.
	/// Ver <see cref="AFolgaNaImagem"/>.
	/// </summary>
	private void EsconderOBoneco(bool esconder)
	{
		if (Corpo?.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis) return;
		vis.Visible = !esconder;
		Nota($"  --     o boneco {(esconder ? "SUMIU" : "voltou")} (so o `Visual`; a nuvem e o cenario ficam)");
		_espera = 0;
	}

	/// <summary>
	/// ============================ A FOLGA, MEDIDA NA IMAGEM ============================
	/// O pedido do dono e um NUMERO -- *"no MAXIMO 5 pixels de distancia do corpo"* --, e ate aqui a
	/// bancada so sabia repetir o numero que o C# tinha ESCRITO (`FolgaDeTeste`). Isso prova que a
	/// conta bate com ela mesma. Nao prova nada sobre a tela: o quad podia estar no lugar errado, a
	/// mascara podia vazar pra fora dele, a elipse podia estar centrada no pe. Cada um desses ja
	/// aconteceu neste arquivo.
	///
	/// Aqui a folga sai de PIXEL DESENHADO, por duas subtracoes no mesmo enquadramento:
	///   * `par-on  - par-off`     -> onde o efeito pinta;
	///   * `par-off - sem-corpo`   -> onde o personagem esta (INCLUINDO cabelo, roupa e rabo, que a
	///                                `NebulosaDaForma.Silhueta` nao mede: ela le so a folha do corpo).
	/// A folga e a distancia entre as duas caixas, lado a lado, convertida de pixel de tela pra pixel
	/// de mundo pelo zoom da camera.
	///
	/// ============================ E A CAIXA DO CORPO E MAIOR QUE A SILHUETA MEDIDA ============================
	/// Esta e a razao de a medida valer a pena: o cabelo de Ultra Instinto e espetado e passa da folha
	/// do corpo pra cima e pros lados, e o rabo Saiyajin sai pro lado. A folga que o dono ve na captura
	/// dele e contada dali, nao da caixa do alfa do torso. Se a nuvem couber em 5 px contando O QUE
	/// APARECE, ela cabe em 5 px em qualquer leitura.
	/// ====================================================================================================
	///
	/// O LIMIAR E BAIXO (3/255) DE PROPOSITO. Ele decide o que conta como "o efeito chega ate aqui", e
	/// errar pra mais e o lado seguro: um limiar alto recortaria a franja fraca da nuvem e devolveria uma
	/// folga menor do que a que esta na tela -- ou seja, o instrumento aprovando o que o olho reprova.
	/// Como as tres fotos sao quadros CONSECUTIVOS de uma cena parada, o que nao e efeito nem corpo da
	/// diferenca ZERO, e nao ha ruido a filtrar.
	/// </summary>
	private void AFolgaNaImagem(string nOn = "par-on", string nOff = "par-off",
								string nSem = "sem-corpo", string onde = "em campo aberto",
								bool cobrarCobertura = true)
	{
		Nota($"== A FOLGA, MEDIDA NA IMAGEM ({onde}) ==");

		if (!_cortes.TryGetValue(nOn, out Image? on)
		 || !_cortes.TryGetValue(nOff, out Image? off)
		 || !_cortes.TryGetValue(nSem, out Image? sem))
		{
			Nota("  --     sem as tres fotos (headless?) -- nao da pra medir folga nenhuma");
			return;
		}

		// AS TRES TEM QUE SER DO MESMO ENQUADRAMENTO, senao a subtracao compara lugares diferentes do
		// mundo e devolve o cenario inteiro como se fosse efeito. O `Recorte` desliza quando o corpo
		// chega perto da beirada do mapa (ver o `Clamp` la), entao isto pode acontecer de verdade.
		Conferir(_cantos[nOn] == _cantos[nOff] && _cantos[nOff] == _cantos[nSem],
				 $"[{onde}] as tres fotos sao do MESMO enquadramento (cantos {_cantos[nOn]}, "
			   + $"{_cantos[nOff]}, {_cantos[nSem]}) -- sem isso a subtracao nao vale nada");

		bool[] mEfeito = OndeDiscordam(on, off), mCorpo = OndeDiscordam(off, sem);
		Rect2I? efeito = Caixa(mEfeito, on.GetWidth(), out int pxEfeito);
		Rect2I? corpo = Caixa(mCorpo, on.GetWidth(), out int pxCorpo);
		_pixelsDoEfeito[onde] = pxEfeito;
		_pixelsDoCorpo[onde] = pxCorpo;

		Conferir(efeito != null, $"[{onde}] a subtracao ligado/desligado achou PIXEL do efeito na tela");
		Conferir(corpo != null, $"[{onde}] a subtracao com/sem boneco achou PIXEL do personagem na tela");
		if (efeito is not { } e || corpo is not { } c) return;

		int zoom = Mathf.Max(World.Instancia?.ZoomDeTeste ?? 1, 1);
		Nota($"  --     [{onde}] caixa do EFEITO {e.Size.X}x{e.Size.Y} px de tela em {e.Position}  |  "
		   + $"caixa do PERSONAGEM {c.Size.X}x{c.Size.Y} px de tela em {c.Position}  |  zoom {zoom}x");

		(string Lado, int Tela)[] folgas =
		[
			("esquerda", c.Position.X - e.Position.X),
			("direita",  e.Position.X + e.Size.X - (c.Position.X + c.Size.X)),
			("acima",    c.Position.Y - e.Position.Y),
			("abaixo",   e.Position.Y + e.Size.Y - (c.Position.Y + c.Size.Y)),
		];

		foreach ((string lado, int tela) in folgas)
		{
			double mundo = tela / (double)zoom;
			Conferir(mundo <= 5.0,
					 $"[{onde}] folga {lado}: {tela} px de tela = {mundo:0.0} px de mundo (o teto do "
				   + "dono e 5)");
		}

		// ============================ E O CHAO DO TETO, QUE FALTAVA ============================
		// As quatro linhas de cima sao TETO. Sozinhas elas tem o defeito que esta bancada cobra de
		// todas as outras: nao ha como falharem por baixo. Uma `Folga` negativa encolheria a nuvem pra
		// dentro do boneco -- ou pra nada -- e as quatro sairiam verdes, porque -20 tambem e "menor
		// que 5". Um teto que so aperta nao mede nada.
		//
		// O CHAO E O SEGUNDO PEDIDO DO DONO, e ele e de AREA: *"o efeito deveria ficar sobre o corpo e
		// nao atras"*. Entao a pergunta e quanto do personagem a nuvem repinta -- e ela responde de uma
		// vez pelas duas coisas, porque uma nuvem que sumiu nao repinta nada e uma nuvem ATRAS do corpo
		// tambem nao (o sprite opaco ficaria por cima e o pixel do boneco sairia identico nas duas
		// fotos). Foi assim que a versao anterior deste efeito ficava: o `par-on` e o `par-off` eram
		// iguais em cima do boneco, e so diferiam na franja em volta.
		//
		// 80% e nao 100%: o rabo Saiyajin sai pro lado e passa da elipse, e o pico do cabelo tambem
		// arranha a quina. Ver a `NebulosaDaForma.Silhueta` -- ela mede a folha do CORPO, e cabelo e
		// rabo sao outras camadas.
		// ====================================================================================
		int juntos = 0;
		for (int i = 0; i < mEfeito.Length; i++)
			if (mEfeito[i] && mCorpo[i]) juntos++;

		double cobertura = pxCorpo > 0 ? juntos / (double)pxCorpo : 0;
		string laudo = $"[{onde}] o efeito e desenhado POR CIMA do personagem: {cobertura * 100:0}% dos "
					 + $"pixels dele foram repintados ({juntos} de {pxCorpo}) -- atras do corpo isso "
					 + "daria ~0%";

		// ============================ E SO ONDE NAO HA NADA NA FRENTE ============================
		// Esta linha ja reprovou uma rodada INTEIRA sendo o efeito o mesmo, e o motivo e instrutivo:
		// embaixo da copa sobram ~300 pixels de personagem em vez de ~1800, e quase todos sao franja
		// de folha (alfa parcial, onde tanto o boneco quanto a nuvem chegam fraquinhos). A razao entre
		// dois numeros pequenos e ruido -- deu 80% numa rodada e 98% na anterior, com a mesma arte,
		// so porque a caminhada parou 12 px adiante.
		//
		// E o que se quer saber ali nao e esse. "A nuvem esta por cima do corpo?" e uma pergunta sobre
		// a ORDEM DE IRMAO, e ela ja foi respondida em campo aberto com 1800 pixels de amostra. Embaixo
		// da arvore a pergunta e outra (a de AREA, logo abaixo). Cobrar a mesma linha nos dois lugares
		// e o jeito mais rapido de ensinar quem roda a bancada a ignorar o vermelho.
		// ====================================================================================
		if (cobrarCobertura) Conferir(cobertura >= 0.80, laudo);
		else Nota("  --     " + laudo + " (aqui e so NOTA: com a copa comendo 4/5 do boneco, "
				+ "esta razao vira ruido -- ver o bloco acima)");
	}

	/// <summary>
	/// Onde duas fotos do MESMO enquadramento discordam, por pixel. Ver o bloco do limiar em
	/// <see cref="AFolgaNaImagem"/>. Vazio (tudo `false`) quando as duas nao batem de tamanho.
	/// </summary>
	private static bool[] OndeDiscordam(Image a, Image b)
	{
		var m = new bool[a.GetWidth() * a.GetHeight()];
		if (a.GetWidth() != b.GetWidth() || a.GetHeight() != b.GetHeight()) return m;

		for (int y = 0; y < a.GetHeight(); y++)
			for (int x = 0; x < a.GetWidth(); x++)
			{
				Color p = a.GetPixel(x, y), q = b.GetPixel(x, y);
				float d = Mathf.Max(Mathf.Abs(p.R - q.R), Mathf.Max(Mathf.Abs(p.G - q.G), Mathf.Abs(p.B - q.B)));
				if (d >= 3f / 255f) m[y * a.GetWidth() + x] = true;
			}

		return m;
	}

	/// <summary>
	/// A caixa de uma mascara de <see cref="OndeDiscordam"/>, em pixels de TELA, e quantos pixels ela
	/// tem. Devolve `null` quando a mascara esta vazia -- que e a resposta certa pra "nao desenhou
	/// nada", e nao uma caixa de tamanho zero na quina.
	/// </summary>
	private static Rect2I? Caixa(bool[] mascara, int largura, out int pixels)
	{
		pixels = 0;
		int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;

		for (int i = 0; i < mascara.Length; i++)
		{
			if (!mascara[i]) continue;
			pixels++;
			int x = i % largura, y = i / largura;
			if (x < x0) x0 = x;
			if (x > x1) x1 = x;
			if (y < y0) y0 = y;
			if (y > y1) y1 = y;
		}

		return x1 < x0 ? null : new Rect2I(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
	}

	/// <inheritdoc cref="OCenarioTapaANuvem"/>
	private readonly Dictionary<string, int> _pixelsDoEfeito = [], _pixelsDoCorpo = [];

	/// <inheritdoc cref="AFolgaNaImagem"/>
	private readonly Dictionary<string, Image> _cortes = [];

	/// <inheritdoc cref="AFolgaNaImagem"/>
	private readonly Dictionary<string, Vector2I> _cantos = [];

	private void Alternar(bool ligado)
	{
		Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.Definir(ligado);
		Nota($"  --     par de subtracao: nuvem {(ligado ? "LIGADA" : "DESLIGADA")}");
		// ZERO: o proximo `_Process` e o proximo QUADRO. Ver o comentario no roteiro -- qualquer folga
		// aqui deixa o cenario se mexer e suja a subtracao.
		_espera = 0;
	}

	// =====================================================================
	// 9. O CENARIO NA FRENTE: A NUVEM TEM QUE SUMIR ATRAS DELE
	// =====================================================================
	/// <summary>Onde o robo quer ficar em pe pra a arvore tapa-lo. Ver <see cref="AcharUmaArvore"/>.</summary>
	private Vector2? _alvo;

	/// <summary>
	/// ACHA UMA ARVORE. O cenario deste jogo e TILE -- nao ha um node "Arvore" pra procurar --, entao
	/// a busca e por CELULA OCUPADA nas camadas do cenario (`World.CamadasDoCenarioDeTeste`).
	///
	/// E so nas camadas ORDENADAS POR Y: uma camada sem `YSortEnabled` desenha inteira antes ou depois
	/// dos atores, e nunca tapa ninguem -- o chao e uma dessas. Entre as que sobram vale a MAIS ESPARSA:
	/// a copa e decoracao (dezenas de celulas num mapa de milhares), e a densa seria o proprio chao
	/// caso ele passasse a ser ordenado. Pegar uma celula de chao daria um alvo a dois passos de
	/// distancia e uma foto sem nada na frente -- o verde vazio de sempre, com outra roupa.
	///
	/// O ALVO E UM POUCO ACIMA DA CELULA, e nao ela: quem tem que ficar na FRENTE e a arvore, ou seja
	/// ela precisa de um Y maior que o do personagem. Parado em cima dela o Y-sort poderia ir pros dois
	/// lados; alguns pixels acima, a resposta certa e uma so.
	/// </summary>
	private void AcharUmaArvore()
	{
		Nota("== 9. O CENARIO NA FRENTE (o `ZIndex` lido na tela, e nao no campo) ==");
		_espera = 0.2;

		if (Corpo is not { } corpo || World.Instancia is not { } mundo) return;

		TileMapLayer? escolhida = null;
		int menos = int.MaxValue;
		foreach (TileMapLayer c in mundo.CamadasDoCenarioDeTeste)
		{
			int n = c.GetUsedCells().Count;
			Nota($"  --     camada `{c.Name}`: {n} celula(s), ysort={c.YSortEnabled}");
			if (!c.YSortEnabled || n == 0 || n >= menos) continue;
			menos = n;
			escolhida = c;
		}

		if (escolhida == null)
		{
			Nota("  --     nenhuma camada de cenario ORDENADA POR Y nesta zona -- nao ha o que tapar a "
			   + "nuvem aqui, e nao da pra julgar a camada pela foto");
			return;
		}

		// A MAIS PERTO QUE AINDA DE UMA CAMINHADA. Uma celula colada no corpo pode estar embaixo dele
		// (o robo acabou de andar pra sair da cratera) e a foto sairia sem margem; uma longe demais
		// estoura o prazo da caminhada e a bancada desiste no meio do mapa.
		Vector2 aqui = corpo.GlobalPosition;
		Vector2? melhor = null;
		float perto = float.MaxValue;
		foreach (Vector2I celula in escolhida.GetUsedCells())
		{
			Vector2 p = escolhida.ToGlobal(escolhida.MapToLocal(celula));
			float d = p.DistanceTo(aqui);
			if (d < 40f || d > 700f || d >= perto) continue;
			perto = d;
			melhor = p;
		}

		if (melhor is not { } alvo)
		{
			Nota($"  --     a camada `{escolhida.Name}` nao tem celula entre 40 e 700 px daqui -- nada "
			   + "alcancavel pra ficar na frente");
			return;
		}

		// 20 px ACIMA: a copa fica com Y maior e desenha depois. Ver o cabecalho.
		_alvo = alvo - new Vector2(0, 20);
		Nota($"  --     achei cenario de `{escolhida.Name}` em {alvo} (a {perto:0} px daqui); vou ficar "
		   + $"em {_alvo} pra ele ficar na minha frente");
	}

	/// <summary>
	/// Anda ate <see cref="_alvo"/> apertando as MESMAS acoes que um jogador aperta -- o corpo e do
	/// servidor, entao nao ha como teleporta-lo daqui, e nem se quisesse: o que se quer fotografar e o
	/// personagem parado num lugar do mapa, exatamente como ele chegaria la jogando.
	///
	/// Uma tecla por vez, a do eixo que falta mais. Diagonal chegaria antes mas passa raspando na
	/// arvore (que e solida), e o robo ficaria empurrando a quina ate estourar o prazo.
	/// </summary>
	private void AndarAteOAlvo()
	{
		string[] teclas = ["move_left", "move_right", "move_up", "move_down"];

		if (_alvo is not { } alvo || Corpo is not { } corpo)
		{
			foreach (string t in teclas) Input.ActionRelease(t);
			return;
		}

		Vector2 falta = alvo - corpo.GlobalPosition;

		// ============================ 24 px, E O NUMERO E UM PASSO -- NAO E FROUXIDAO ============================
		// Com 10 px aqui o robo NUNCA chegava: ele decide a tecla a cada 0,2 s, e um corpo de 3 M de BP
		// anda ~108 px/s, ou seja ~22 px por decisao. Alvo dentro de meio passo = passa por cima dele,
		// corrige pro outro lado, passa de novo -- as tres rodadas anteriores gastaram o prazo INTEIRO
		// (24,2 s, 24,2 s, 40,2 s) oscilando a 20 px do destino e sairam pelo teto, com um log que se le
		// como falha e nao era.
		//
		// A tolerancia tem que ser maior que um passo, e menor que um tile (32 px) -- senao o robo para
		// fora da copa e a foto perde o assunto. 24 px cabe nos dois lados, e e onde ele ja estava
		// parando de qualquer jeito.
		// ==================================================================================================
		bool chegou = falta.Length() <= 24f;
		bool travou = _voltasDaCaminhada > 0 && corpo.GlobalPosition.DistanceTo(_ondeEuEstava) < 0.5f;

		// 200 VOLTAS (40 s) e prazo pra nao travar pra sempre, e nao aperto: com a tolerancia certa a
		// caminhada acaba em ~5 s. Estourar o prazo nao devolve erro -- devolve uma foto tirada no meio
		// do campo, com nada na frente --, e quem pega isso e o controle de area do
		// <see cref="OCenarioTapaANuvem"/> ("o boneco tem que ter encolhido"), nao esta linha.
		if (!chegou && !travou && _voltasDaCaminhada++ < 200)
		{
			_ondeEuEstava = corpo.GlobalPosition;
			foreach (string t in teclas) Input.ActionRelease(t);
			Input.ActionPress(Mathf.Abs(falta.X) > Mathf.Abs(falta.Y)
				? falta.X > 0 ? "move_right" : "move_left"
				: falta.Y > 0 ? "move_down" : "move_up");
			_passo--;
			_espera = 0.2;
			return;
		}

		foreach (string t in teclas) Input.ActionRelease(t);
		Nota($"  --     caminhada: parei a {falta.Length():0} px do alvo em {_voltasDaCaminhada * 0.2:0.0}s"
		   + (travou ? " (TRAVEI num obstaculo -- e o que se queria: estou encostado nele)" : ""));
		// A POEIRA DO PASSO e o proprio boneco tem que parar. Sem isso a subtracao de quadros
		// consecutivos traz o personagem se mexendo junto com o efeito.
		_espera = 1.5;
	}

	/// <inheritdoc cref="AndarAteOAlvo"/>
	private int _voltasDaCaminhada;

	/// <inheritdoc cref="AndarAteOAlvo"/>
	private Vector2 _ondeEuEstava;

	/// <summary>
	/// A FOTO DE BAIXO DA ARVORE, e o numero que ela devolve.
	///
	/// A pergunta e "o cenario que esta na FRENTE apaga a nuvem?", e ela se responde comparando o
	/// EFEITO com o PERSONAGEM no mesmo lugar: os dois estao no mesmo degrau de Y (a nuvem e filha do
	/// corpo, todos em `ZIndex` 0), entao o que tapa um tem que tapar o outro NA MESMA LINHA. Se a
	/// nuvem estivesse acima do cenario, ela apareceria por cima da copa -- ou seja, o efeito passaria
	/// a chegar mais alto do que o personagem chega, e a folga de cima explodiria justamente aqui.
	///
	/// As tres fotos e as quatro folgas saem da MESMA <see cref="AFolgaNaImagem"/>, so que tiradas
	/// aqui -- mas quem responde a pergunta e o bloco de AREA la embaixo, e nao elas: recortada pela
	/// copa a nuvem so pode encolher, e "menor que 5" fica facil demais. Uma foto sem nada na frente
	/// nao prova nada, e por isso a caminhada avisa quando travou e a area do BONECO entra como
	/// controle.
	/// </summary>
	private void OCenarioTapaANuvem()
	{
		if (_alvo == null)
		{
			Nota("  --     sem alvo de cenario: pulei a foto (e NAO estou dizendo que passou)");
			_espera = 0.3;
			return;
		}

		switch (_fasesDaArvore++)
		{
			case 0: Alternar(false); EsconderOBoneco(true); _passo--; return;
			case 1: Fotografar("arvore-sem-corpo", Corpo); EsconderOBoneco(false); _passo--; return;
			case 2: Fotografar("arvore-off", Corpo); Alternar(true); _passo--; return;
		}

		Fotografar("arvore-on", Corpo);
		AFolgaNaImagem("arvore-on", "arvore-off", "arvore-sem-corpo", "embaixo da arvore",
					   cobrarCobertura: false);

		// ============================ E AGORA A PERGUNTA DA COPA, QUE E DE AREA E NAO DE CAIXA ============================
		// As quatro folgas la de cima passam embaixo da arvore por um motivo bobo: com a nuvem
		// recortada pela copa, todas ENCOLHEM -- e "menor que 5" fica facil demais. A caixa nao
		// distingue "a copa cortou a nuvem" de "a nuvem passou por cima da copa e ficou do mesmo
		// tamanho", que sao justamente as duas respostas em disputa.
		//
		// A AREA distingue. Com o `ZIndex` em 0 e o Y-sort mandando, a copa arranca pixel da nuvem
		// EXATAMENTE como arranca do boneco; com um `ZIndex` positivo a nuvem desenharia por cima das
		// folhas e a contagem dela sairia a mesma do campo aberto, enquanto a do boneco despencaria.
		// Entao a checagem e dupla e uma segura a outra:
		//   * o BONECO tem que ter encolhido  -> prova que ha mesmo coisa na frente (sem isso, uma
		//                                        caminhada que parou no meio do nada daria verde);
		//   * a NUVEM tem que ter encolhido junto -> e a resposta do tombo.
		// ==============================================================================================================
		if (_pixelsDoCorpo.TryGetValue("em campo aberto", out int corpoAberto)
		 && _pixelsDoCorpo.TryGetValue("embaixo da arvore", out int corpoArvore)
		 && _pixelsDoEfeito.TryGetValue("em campo aberto", out int nuvemAberto)
		 && _pixelsDoEfeito.TryGetValue("embaixo da arvore", out int nuvemArvore)
		 && corpoAberto > 0 && nuvemAberto > 0)
		{
			double doCorpo = 1.0 - corpoArvore / (double)corpoAberto;
			double daNuvem = 1.0 - nuvemArvore / (double)nuvemAberto;
			Conferir(doCorpo > 0.15,
					 $"a copa REALMENTE esta na frente: o boneco perdeu {doCorpo * 100:0}% dos pixels "
				   + $"({corpoAberto} -> {corpoArvore}) -- sem isto a linha de baixo seria verde vazio");
			Conferir(daNuvem > 0.15,
					 $"e a nuvem foi recortada JUNTO: perdeu {daNuvem * 100:0}% ({nuvemAberto} -> "
				   + $"{nuvemArvore}) -- com `ZIndex` acima de 0 ela desenharia por cima da copa e este "
				   + "numero seria ~0%");
		}
		else Nota("  --     faltou uma das duas medidas (campo aberto / arvore): sem comparacao de area");

		_espera = 0.3;
	}

	/// <inheritdoc cref="OCenarioTapaANuvem"/>
	private int _fasesDaArvore;

	private void ABaseApaga()
	{
		var neb = Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa");
		Conferir(neb?.ForcaDeTeste == 0f,
				 $"voltando pra base a nuvem APAGA (forca={neb?.ForcaDeTeste:0.##})");
		Fotografar("base", Corpo);
		Fechar();
	}

	// =====================================================================
	// 4. A FOTO, E O LAUDO DELA
	// =====================================================================
	/// <summary>
	/// O lado do recorte em pixels de TELA. A nuvem tem 96 px de mundo e o zoom padrao e 3, entao ela
	/// ocupa 288 -- o recorte tem que ser maior que isso pra a franja e o fundo caberem na mesma foto
	/// (e a comparacao "efeito x fundo" e metade do que se olha aqui).
	/// </summary>
	private const int LadoDoCorte = 384;

	/// <summary>
	/// O quadro ja desenhado, recortado num quadrado em volta do corpo. Um lugar so, porque a foto e
	/// a checagem da elipse precisam do MESMO enquadramento -- se cada uma recortasse do seu jeito, o
	/// numero do log deixaria de descrever o PNG ao lado dele.
	///
	/// O CORPO NEM SEMPRE ESTA NO CENTRO DA TELA (a camera para nas beiradas do mapa), entao o
	/// recorte sai da posicao de tela do node e nao do meio da janela. Quando ele encosta na borda o
	/// `Clamp` desloca a janela -- e por isso o centro do recorte e devolvido junto, em vez de
	/// suposto.
	/// </summary>
	private Image? Recorte(Node2D? centro)
	{
		if (centro == null) return null;
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) return null;

		Vector2 p = centro.GetGlobalTransformWithCanvas().Origin;
		int lado = Mathf.Min(LadoDoCorte, Mathf.Min(img.GetWidth(), img.GetHeight()));
		int x = Mathf.Clamp((int)p.X - lado / 2, 0, img.GetWidth() - lado);
		int y = Mathf.Clamp((int)p.Y - lado / 2, 0, img.GetHeight() - lado);
		// O CANTO FICA GUARDADO porque o `Clamp` acima e imprevisivel: perto da beirada do mapa o
		// recorte desliza e deixa de ser centrado no corpo. Quem quiser converter um retangulo de TELA
		// (o quad, por exemplo) pra dentro desta imagem precisa do canto de verdade -- supo-lo centrado
		// e o erro que a checagem da elipse ja cometeu uma vez, com outro nome.
		_cantoDoCorte = new Vector2I(x, y);
		return img.GetRegion(new Rect2I(x, y, lado, lado));
	}

	/// <inheritdoc cref="Recorte"/>
	private Vector2I _cantoDoCorte;

	private void Fotografar(string nome, Node2D? centro)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty())
			{
				Nota($"  --     [{nome}] sem foto (headless nao renderiza)");
				return;
			}

			img.SavePng(ProjectSettings.GlobalizePath($"user://neb-{nome}-tela.png"));

			if (centro == null) return;
			if (Recorte(centro) is not { } corte) return;

			// O RECORTE FICA GUARDADO, e nao so salvo em disco. A <see cref="AFolgaNaImagem"/> subtrai
			// TRES destas fotos entre si -- e reabrir o PNG que acabou de ser escrito seria ler de volta,
			// no meio da bancada, um arquivo que ela mesma acabou de produzir. Guardar antes do `Resize`
			// tambem importa: a ampliacao 2x e pra o olho, e medir folga numa foto ampliada custaria uma
			// divisao a mais em todo numero do laudo.
			_cortes[nome] = (Image)corte.Duplicate();
			_cantos[nome] = _cantoDoCorte;
			// O RAIO DO EFEITO SAI DO QUAD, PERGUNTADO -- ver o cabecalho do `Laudo`. Sem nuvem no
			// corpo (uma foto da base), meio recorte: o laudo vira "tudo fundo", que e a verdade.
			float raio = centro.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.RetanguloNaTelaDeTeste.Size.X * 0.5f
					  ?? corte.GetWidth() / 2f;
			Nota($"  --     [{nome}] {Laudo(corte, corte.GetWidth() / 2, corte.GetHeight() / 2, raio)}");

			// NEAREST, e nao e detalhe: em arte de pixel qualquer interpolacao INVENTA tons
			// intermediarios -- e o laudo acima acabou de contar quantos pixels sao branco PURO. Uma
			// ampliacao suavizada faria a foto contradizer o proprio laudo.
			corte.Resize(corte.GetWidth() * 2, corte.GetHeight() * 2, Image.Interpolation.Nearest);
			corte.SavePng(ProjectSettings.GlobalizePath($"user://neb-{nome}.png"));
		}
		catch (Exception e) { _passos.Add($"  --     [{nome}] sem foto: {e.Message}"); }
	}

	/// <summary>
	/// O QUE A IMAGEM DIZ EM NUMERO. Duas regioes, medidas a partir do corpo e em RAIOS DO EFEITO:
	///   * o EFEITO (ate `raio`, a meia largura do quad na tela) -- onde a rampa indigo-violeta-ciano
	///     e as microparticulas tem que aparecer;
	///   * o FUNDO (alem de 1,6 x `raio`) e o cenario, e e contra ele que se julga contraste.
	/// O pixel mais claro e a contagem de brancos puros sao a medida das microparticulas: foi assim
	/// que "branco puro saindo 148" apareceu, e nenhum olho pegaria isso num sprite de 32 px.
	///
	/// ============================ OS RAIOS ERAM CRAVADOS, E ISSO ACABOU DE CUSTAR UM LAUDO ============
	/// Aqui estava "anel de 48 a 150 px de tela, fundo alem de 170", copiado da epoca do quad de 96 px
	/// de mundo -- e com o corpo EXCLUIDO, porque a nuvem desenhava atras dele e o miolo era sprite.
	///
	/// Quando o quad encolheu pra 42 px (silhueta + 5 px de folga, a pedido do dono) o efeito INTEIRO
	/// passou a caber dentro do miolo excluido: a rodada saiu com "anel RGB(23,28,12) fundo RGB(22,29,11),
	/// branco puro: 0 px" -- o instrumento medindo grama e dizendo que a nuvem sumiu, com a nuvem acesa.
	/// Um laudo assim nao e so inutil: ele manda consertar o que nao esta quebrado.
	///
	/// Agora o raio vem do QUAD, perguntado ao node (a mesma correcao que o `Medir` recebeu), e o miolo
	/// entra na conta -- a nuvem passou a desenhar POR CIMA da silhueta, entao ali tambem e efeito.
	/// ==============================================================================================
	/// </summary>
	private static string Laudo(Image img, int cx, int cy, float raio)
	{
		double aR = 0, aG = 0, aB = 0; int aN = 0;   // o efeito
		double fR = 0, fG = 0, fB = 0; int fN = 0;   // fundo
		int brancos = 0, maisClaro = 0;
		float longe = Mathf.Max(raio * 1.6f, raio + 8f);

		for (int y = 0; y < img.GetHeight(); y++)
		for (int x = 0; x < img.GetWidth(); x++)
		{
			Color c = img.GetPixel(x, y);
			int r = (int)(c.R * 255), g = (int)(c.G * 255), b = (int)(c.B * 255);
			double d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

			if (d <= raio)
			{
				aR += r; aG += g; aB += b; aN++;
				int menor = Mathf.Min(r, Mathf.Min(g, b));
				if (menor >= 250) brancos++;
				if (menor > maisClaro) maisClaro = menor;
			}
			else if (d > longe) { fR += r; fG += g; fB += b; fN++; }
		}

		if (aN == 0 || fN == 0) return "recorte pequeno demais pra laudo";
		return $"o efeito RGB({aR / aN:0},{aG / aN:0},{aB / aN:0})  "
			 + $"fundo RGB({fR / fN:0},{fG / fN:0},{fB / fN:0})  "
			 + $"branco puro: {brancos} px  mais claro: {maisClaro}";
	}

	private void Fechar()
	{
		_acabou = true;
		// As linhas ja sairam uma a uma (ver `Nota`) -- aqui so o VEREDITO, senao o log traria tudo
		// duas vezes e a segunda copia esconderia o resumo.
		GD.Print($"[nebulosa] ===== FIM: {_passos.Count} linha(s) de log =====");
		GD.Print($"[nebulosa] ===== PLACAR: {_oks + _falhas.Count} checagem(ns) -- "
			   + $"{_oks} ok, {_falhas.Count} falha(s) =====");
		GD.Print(_falhas.Count == 0
			? "[nebulosa] ===== TUDO OK ====="
			: $"[nebulosa] ===== {_falhas.Count} FALHA(S) =====\n[nebulosa]   "
			  + string.Join("\n[nebulosa]   ", _falhas));
	}
}
