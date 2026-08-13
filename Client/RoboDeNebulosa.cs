using Godot;

// A VARREDURA DO CATALOGO le `FormaDef` e `LinhaDeForma` dezenas de vezes; por extenso, o prefixo
// esconderia a propria regra dentro dele. O `Catalogo` continua escrito completo (como no
// `RoboDeForma`, e pelo mesmo motivo la): e o nome mais generico do lote e o unico que confunde.
using Jandirus.Core.Forms;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DA NEBULOSA (`--diagnebulosa`). Ela existe pra TIRAR A FOTO.
///
/// ERA "A NEBULOSA DO ULTRA INSTINTO", e o titulo mudou junto com o efeito: o dono pediu
/// *"a aura/carga do ultra ego e a mesma do instinto superior so q ROXA ao inves de branca/prateada,
/// mas tem os mesmos efeitos"*, e a nuvem passou a ter DOIS donos. O que a bancada afirmava ("so as
/// duas de UI tem nebulosa") foi REESCRITO com o motivo novo, e nao afrouxado -- ver o `Varrer` e o
/// bloco (a2) do <see cref="OCatalogo"/>, onde a `destroyer` continua nomeada do lado de fora.
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
///   2. E **so** nelas e no `ultra_ego`         -> `Varrer` no catalogo + `ANebulosaSoEDoUltraInstinto`
///                                                 no corpo. Controle: QUATRO predicados DEFEITUOSOS
///                                                 passam pela mesma varredura e tem que reprovar --
///                                                 e o quarto ("a LINHA do Ultra Ego inteira") erra
///                                                 em uma forma so, a `destroyer`.
///   2b. E a nuvem do Ultra Ego e ROXA          -> `APaletaDasDuas` (puro: matiz contra o `8c32be` da
///                                                 forma, e as DUAS pontas da rampa em luminancia) e
///                                                 `ANuvemRoxaChegaNoPixel` (no material, com o corpo
///                                                 vestido pela rede). A segunda cobra que a UNICA
///                                                 diferenca pro `ui_sign` sejam os quatro uniforms de
///                                                 cor -- que e *"tem os mesmos efeitos"* medido.
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
///                                                 ALFA da folha e cobra que o campo do shader a
///                                                 CONTENHA, e que o lado do quad saia da moldura --
///                                                 um lado cravado no `.cs` reprova. Mais a mesma
///                                                 regra rodada numa folha de 96 (a do Oozaru).
///                                                 Controle: tres defeitos injetados em
///                                                 `AFolgaCabe` -- quad que sobra, folga de 20 px e
///                                                 folga zero.
///  10b. E a nuvem CONTORNA, nao enquadra      -> ainda em `AFolgaEDerivada`, pelo
///                                                 `PreenchimentoDeTeste`: a mascara enche menos de
///                                                 70% da propria caixa, e uma ELIPSE encheria 78,5%
///                                                 por definicao. E a unica linha que enxerga a
///                                                 queixa nova do dono -- todas as outras davam
///                                                 verde com a elipse, porque o TAMANHO estava certo.
///  10c. E ela ACOMPANHA A ANIMACAO            -> `AMascaraSegueAAnimacao`: 40 quadros andando, e a
///                                                 chave da pose tem que mudar. Controle no mesmo
///                                                 lugar: o LADO do quad tem que ficar parado -- se
///                                                 os dois mudam, a mascara acompanha REMEDINDO e a
///                                                 nuvem pulsa de tamanho a cada passo.
///  10d. E ela tem CINTURA onde o boneco tem   -> `ANuvemTemCinturaComoOBoneco`: o perfil linha a
///                                                 linha da nuvem contra o do personagem, os dois da
///                                                 MESMA foto. O preenchimento (10b) reprova a elipse
///                                                 mas passaria em qualquer mascara esburacada; esta
///                                                 compara as duas FORMAS. Controle: a elipse da mesma
///                                                 caixa tem proeminencia de vale ZERO por definicao,
///                                                 e reprova na mesma funcao.
///  12. A CARGA MUDA A NUVEM **EM PIXEL**       -> `OParDaCarga`: tres quadros (carga off, off, on) e
///                                                 duas subtracoes. A terceira foto e o controle de
///                                                 DERIVA -- a nuvem e animada, e dois quadros dela
///                                                 parada ja discordam. Controle: o par off/off vira o
///                                                 defeito injetado, com dado de verdade.
///  13. E A CHAMA **CONTINUA** NAS OUTRAS       -> `AChamaContinuaNasOutrasFormas`: em `ssj1`, com o C
///                                                 na mao, a folha de chama tem que acender. Todas as
///                                                 linhas de carga desta bancada cobram AUSENCIA, e
///                                                 ausencia se entrega por engano: apagar a chama do
///                                                 jogo INTEIRO passava aqui com o placar limpo.
///  14. E O CENARIO FICOU PARADO?               -> dentro da `AFolgaNaImagem`: longe do boneco as duas
///                                                 fotos nao podem discordar. Sem esta linha a bancada
///                                                 media a deriva do VEU DO CLIMA e chamava de
///                                                 personagem -- 116 mil pixels, caixa do tamanho da
///                                                 tela, quatro folgas de 0,0 px, e quatro verdes.
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
			// E TINHA PIOR NA FOTO. A cena inteira despejava `Poeira`, `Cascalho`, `Cratera` e
			// `AnelDeChoque` em volta do corpo. As quatro fotos daquela rodada sairam com uma nuvem
			// MARROM cobrindo o personagem, um anel branco de onda de choque e uma cratera no chao --
			// e nada daquilo era a nebulosa. Eu quase reportei "a rampa esta saindo marrom".
			//
			// (O `Cascalho` nao existe mais -- o dono o cortou, e a poeira e a cratera passaram a cair
			// so no instante da troca. A queima da estreia continua necessaria pelo resto.)
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

			// A MASCARA CHAPADA, e ela responde a pergunta que a foto normal nao responde: ONDE este
			// quad desenha. Ver `NebulosaDaForma.DiagnosticoChapado`.
			// E A FOTO CHAPADA E CONFERIDA, nao so salva. Ver `AMascaraCaiNoCorpo`: e a unica checagem
			// desta bancada que julga PIXEL DESENHADO em vez de estado de node, e e a que teria pego o
			// defeito da ancora no primeiro dia.
			case 23: AMascaraCaiNoCorpo(); Moldura(true); break;
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
			case 26: if (Medir("brilho 0")) Afinar("pontos_brilho", null); break;
			case 27: if (Medir("padrao")) Afinar("pontos_densidade", 0f); break;
			case 28: if (Medir("densidade 0")) Afinar("pontos_densidade", 1f); break;
			case 29: if (Medir("densidade 1")) Afinar("pontos_densidade", 0.25f); break;

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
			case 30: if (Medir("densidade 0,25")) Afinar("pontos_densidade", 0.50f); break;
			case 31: if (Medir("densidade 0,50")) Afinar("pontos_densidade", 0.75f); break;
			case 32: if (Medir("densidade 0,75")) Afinar("pontos_densidade", 1.00f); break;
			case 33: if (Medir("densidade 1,00")) Afinar("pontos_densidade", null); break;
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

			// ============================ AS TRES POSES: ANDAR, SOCAR, VOAR ============================
			// Este bloco fecha o buraco que o relatorio anterior nomeou com todas as letras: *"Voo e
			// nocaute nao foram fotografados. A copia da `Transform` resolve rotacao e escala POR
			// CONSTRUCAO, e nao ha passo de bancada que vista Ultra Instinto e voe. E o buraco mais
			// provavel de sobrar."* "Por construcao" e exatamente o tipo de garantia que esta casa ja
			// aprendeu a nao aceitar sozinha.
			//
			// E as tres poses sao as tres que MEXEM na `Transform` do `CharacterVisual` de um jeito que
			// a nuvem, sendo IRMA e nao filha dele, nao herdaria:
			//   * andar  -> a silhueta muda de quadro (e a mascara e por pose);
			//   * socar  -> o punho sai da caixa da pose parada -- e o caso que a `AMascaraSegueAAnimacao`
			//               mede em numero e ninguem nunca olhou;
			//   * voar   -> o `CharacterVisual` GIRA 90 graus por direcao. Uma nuvem que nao copiasse a
			//               `Transform` sairia de pe ao lado de um corpo deitado, que e o defeito mais
			//               visivel possivel desta tarefa.
			//
			// O QUE SE MEDE EM CADA UMA e a distancia entre o centro do quad e o corpo -- o mesmo numero
			// do passo 0, agora com o boneco em movimento. A foto e o laudo; o numero e o que reprova.
			// ==================================================================================
			// ============================ SEIS PASSOS PRA TRES FOTOS, E O DOBRO E O CONSERTO ============
			// A primeira versao apertava a tecla e fotografava na MESMA chamada, e o log a desmascarou:
			// `[andando] pose do visual default_south` e `[socando]` com a mesma chave de mascara -- as
			// duas fotos eram do boneco PARADO, e as duas passaram. `Input.ActionPress` so marca a acao;
			// quem a le e o `LocalPlayer` no `_Process` DELE, que pode rodar depois desta bancada no
			// mesmo quadro. Apertar e medir junto le sempre o quadro anterior.
			//
			// (O voo escapou por acidente: ele ja tinha passo proprio por causa da rampa de subida, e por
			// isso foi a unica das tres que saiu com a pose certa -- `flight_south`.)
			// ==========================================================================================
			case 40: Tecla("move_right", true); break;
			case 41: ALinhaDaPose("andando", "walk"); Tecla("move_right", false); break;
			case 42: Soco(true); break;
			case 43: ALinhaDaPose("socando", "attack"); Soco(false); break;
			case 44: Voar(true); break;
			case 45: PoseVoando(); break;
			case 46: Voar(false); break;

			case 47: AcharUmaArvore(); break;
			case 48: AndarAteOAlvo(); break;

			// ============================ A MASCARA ACOMPANHA A ANIMACAO? ============================
			// Este passo entrou com a mascara colada na silhueta, e ele nao existia antes porque nao
			// PRECISAVA: a elipse era medida uma vez por transformacao e ficava certa o dia inteiro, ja
			// que ela nao tinha forma pra descolar. Uma silhueta descola -- na pose de soco o punho sai
			// da caixa da pose parada --, e "a nuvem esta a 5 px do corpo" continua VERDE com a mascara
			// congelada no quadro errado. Nenhuma outra linha desta bancada ve isso.
			//
			// Ele vem depois da caminhada de proposito: o robo ja esta em pe, em terreno limpo e com a
			// nuvem acesa, e so precisa dar alguns passos. Ver `AMascaraSegueAAnimacao`.
			// ====================================================================================
			case 49: AMascaraSegueAAnimacao(); break;
			case 50: OCenarioTapaANuvem(); break;

			// ============================ OS DOIS ESTADOS DA NUVEM, EM PIXEL -- E O CONTRA-EXEMPLO ============
			// Este bloco fecha o buraco que o relatorio anterior nomeou: *"a checagem da carga e do
			// UNIFORM, nao do pixel -- e o buraco mais provavel de sobrar, e e exatamente o cego que esta
			// casa ja nomeou"*. `CargaDeTeste == 1,19` prova que o numero chegou ao material; nao prova
			// que UM PIXEL mudou na tela. Uniform escrito nao e pixel desenhado.
			//
			// E ele fecha junto a outra metade do pedido, que nao tinha NENHUMA linha: a chama saiu de
			// cena em Ultra Instinto, e so nele. Sem um contra-exemplo, apagar a folha de chama do jogo
			// INTEIRO passaria nesta bancada com o placar limpo -- todas as linhas de carga que existiam
			// aqui cobram AUSENCIA.
			//
			// A ORDEM E DE CUSTO: sair de baixo da arvore (a foto tem que ver a nuvem), revestir (o dreno
			// pode ter derrubado a forma durante as tres poses), medir os dois estados, e so entao trocar
			// pro `ssj1` -- que e ida sem volta pro par de fotos.
			// ==============================================================================================
			case 51: Andar(); break;
			case 52: PararDeAndar(); break;
			// O SEGUNDO ESTAGIO, e nao o `ui_sign` de novo: forcar a forma que o corpo JA esta vestindo
			// nao tem efeito garantido (o verb reentra na mesma), e a espera abaixo voltaria na hora sem
			// esperar nada -- que e o buraco cego que `EsperarANuvem` documenta. O `ui_perfected` acende a
			// MESMA nuvem (e o ponto do efeito) e a estreia dele foi queimada la no passo 3.
			case 53: Forcar("ui_perfected", "revestindo pro par de fotos da carga (o dreno pode ter "
										  + "derrubado a forma durante as tres poses)"); break;
			case 54: EsperarANuvem(true, "a nuvem acender pro par da carga"); break;
			case 55: OParDaCarga(); break;

			case 56: Forcar("ssj1", "o CONTRA-EXEMPLO: uma forma que TEM folha de chama"); break;
			case 57: EsperarANuvem(false, "o `ssj1` assentar de novo"); break;
			case 58: Carregar(true, "segurando C em `ssj1` -- aqui a chama TEM que acender"); break;
			case 59: AChamaContinuaNasOutrasFormas(); break;
			case 60: Carregar(false, "soltando o C"); break;

			// ============================ E AGORA A OUTRA NUVEM: A MESMA, EM ROXO ============================
			// Tudo acima mede o Ultra Instinto. Desde que o dono pediu *"a aura/carga do ultra ego e a
			// mesma do instinto superior so q ROXA"*, a nuvem tem DOIS donos -- e o unico jeito de saber
			// que a paleta roxa chega no pixel e vestindo o `ultra_ego` no jogo de verdade, pelo mesmo
			// caminho de producao (servidor -> pacote -> `Transformacao.Vestir`).
			//
			// A CENA DELE NAO FOI QUEIMADA la em cima de proposito: sao 22 s (`UltraEgo.dm:415`) e o
			// `EsperarANuvem` ja espera EVENTO, nao relogio -- ele so segue quando `PresosDeTeste`
			// zerar. Queimar a estreia custaria uma segunda passada de 22 s pra economizar nada.
			//
			// E ELE VEM ANTES DA BASE, e nao depois: o passo da base FECHA o robo (`ABaseApaga` chama
			// `Fechar`). Qualquer coisa depois dele nunca rodaria.
			// ==============================================================================================
			case 61: Forcar("ultra_ego", "o Ultra EGO: a MESMA nuvem, em roxo"); break;
			case 62: EsperarANuvem(true, "a cena do Ultra Ego terminar e a nuvem roxa acender"); break;
			case 63: ANuvemRoxaChegaNoPixel(); break;

			case 64: Forcar(Jandirus.Core.Forms.Catalogo.IdBase, "voltando pra base"); break;
			case 65: EsperarANuvem(false, "a base apagar a nuvem"); break;
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
	/// Recebendo o predicado de fora, a MESMA varredura roda cinco vezes: uma com a regra de verdade
	/// (`Catalogo.TemNebulosa`, tem que dar zero) e QUATRO com defeitos INJETADOS -- "todo mundo tem",
	/// "ninguem tem", "trocaram UI por UE" e "a LINHA do Ultra Ego inteira". As quatro tem que
	/// REPROVAR, e com o numero exato de formas fora do lugar. Se alguma delas passar, a varredura esta
	/// cega e nenhuma das outras linhas deste arquivo vale nada.
	///
	/// O QUARTO NASCEU COM O ULTRA EGO e e o mais fino dos quatro: ele erra em UMA forma so (a
	/// `destroyer`), que e exatamente o atalho que qualquer um escreveria ao ler a ordem do dono.
	/// ================================================================================================
	/// </summary>
	private static (int Erradas, int Com, int Sem) Varrer(Func<FormaDef, bool> pergunta)
	{
		int erradas = 0, com = 0, sem = 0;
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			// A REGRA, ESCRITA AQUI E NAO IMPORTADA. Chamar `Catalogo.TemNebulosa` dos dois lados faria
			// a linha comparar a funcao com ela mesma -- verde eterno, inclusive no dia em que ela
			// passasse a responder qualquer outra coisa. O que se cobra e a FRASE do dono.
			//
			// E A FRASE MUDOU: era "a nebulosa e da linha do Ultra Instinto, e de mais ninguem", e o
			// dono pediu depois *"a aura/carga do ultra ego e a mesma do instinto superior so q ROXA ao
			// inves de branca/prateada, mas tem os mesmos efeitos"*. Entao sao TRES formas: as duas da
			// linha do Ultra Instinto mais o `ultra_ego`.
			//
			// PELO ID E NAO PELA LINHA, e e o ponto todo desta linha de codigo: a `destroyer` e da MESMA
			// linha e NAO tem nuvem. O dono nomeou o Ultra Ego, e o DM diz que o cabelo e a diferenca
			// visual entre as duas formas da disciplina (`UltraEgo.dm:395-396`) -- dar a nuvem as duas
			// apagaria justamente essa diferenca. Escrever `d.Linha == UltraEgo` aqui seria afrouxar a
			// afirmacao pra caber no codigo, que e o contrario do que esta bancada existe pra fazer.
			bool devia = d.Linha == LinhaDeForma.UltraInstinct || d.Id == "ultra_ego";
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

		// --- (a2) O `ultra_ego` TEM; a `destroyer` NAO ---
		// ============================ ESTA AFIRMACAO VIROU DE LADO, E DE PROPOSITO ============================
		// Ate aqui esta bancada dizia que as DUAS de Ultra Ego ficavam de fora, e a razao escrita era o
		// DM (`UltraEgo.dm` nao veste as duas folhas de galaxia). Ela reprovou quando a nuvem chegou no
		// Ultra Ego, e reprovou CERTO -- o que mudou nao foi o codigo, foi a ordem do dono:
		//
		//   *"a aura/carga do ultra ego e a mesma do instinto superior so q ROXA ao inves de
		//    branca/prateada, mas tem os mesmos efeitos"*
		//
		// E DIVERGENCIA DECLARADA do original, como o rabo branco do Perfected foi. A afirmacao foi
		// REESCRITA com o motivo novo, e nao afrouxada: continua sendo uma lista fechada de formas com
		// nome, e a `destroyer` continua nomeada do lado de fora.
		//
		// ============================ A `destroyer` FICA DE FORA, E ISSO NAO E DESCUIDO ============================
		// **O dono nao a citou.** No DM a diferenca visual entre as duas formas da disciplina e justamente
		// o cabelo (`UltraEgo.dm:395-396`, e a cena, que ela tambem nao tem) -- dar a nuvem as duas
		// apagaria a unica coisa que as separa aos olhos. Fica na folha colorivel com o `9b4dff` dela.
		// A pergunta esta na mesa do dono; ate ele responder, o silencio dele e o que esta escrito aqui.
		// ==========================================================================================================
		FormaDef? destr = todas.FirstOrDefault(d => d.Id == "destroyer");
		FormaDef? ego = todas.FirstOrDefault(d => d.Id == "ultra_ego");
		Conferir(ego != null && Jandirus.Core.Forms.Catalogo.TemNebulosa(ego),
				 "`ultra_ego` TEM nebulosa (*\"a mesma do instinto superior so q ROXA\"*)");
		Conferir(destr != null && !Jandirus.Core.Forms.Catalogo.TemNebulosa(destr),
				 "e a `destroyer` NAO -- o dono nomeou o Ultra Ego, e o cabelo base e a diferenca visual "
			   + "entre as duas (`UltraEgo.dm:395-396`)");

		// E AS DUAS SAO DA MESMA LINHA, dito por medida e nao por memoria: e o que da sentido a linha de
		// cima. Se a `Linha` delas divergisse, "uma tem e a outra nao" seria uma consequencia boba de
		// ramo por linha, e nao a escolha por `Ordem` que o Core faz.
		Conferir(destr != null && ego != null && destr.Linha == ego.Linha && destr.Ordem != ego.Ordem,
				 $"-- e as duas sao da MESMA linha ({ego?.Linha}) separadas por `Ordem` "
			   + $"({destr?.Ordem} e {ego?.Ordem}): o corte e por degrau, nao por linha");

		// --- (b) E **SO** NAS TRES ---
		(int erradas, int com, int sem) = Varrer(Jandirus.Core.Forms.Catalogo.TemNebulosa);
		Conferir(erradas == 0,
				 $"a nebulosa e SO da linha do Ultra Instinto e do `ultra_ego` ({erradas} forma(s) fora "
			   + $"do lugar em {todas.Length} varridas)");
		Conferir(com == 3, $"exatamente TRES formas acendem nebulosa (deu {com})");
		Conferir(sem == todas.Length - 3,
				 $"e as outras {todas.Length - 3} NAO ganharam de brinde (deu {sem})");

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
		Nota("  --     agora os DEFEITOS INJETADOS: as quatro linhas abaixo tem que dizer que reprovaram");
		Conferir(Varrer(_ => true).Erradas == todas.Length - 3,
				 $"[injetado] 'nebulosa pra TODO MUNDO' e pego, e nas {todas.Length - 3} formas certas "
			   + $"(deu {Varrer(_ => true).Erradas})");
		Conferir(Varrer(_ => false).Erradas == 3,
				 $"[injetado] 'NINGUEM tem nebulosa' e pego nas tres (deu {Varrer(_ => false).Erradas})");
		Conferir(Varrer(d => d.Linha == LinhaDeForma.UltraEgo).Erradas == 3,
				 "[injetado] trocar Ultra Instinto por Ultra EGO e pego dos dois lados -- a `destroyer` "
			   + "que ganharia e as 2 de UI que perderiam "
			   + $"(deu {Varrer(d => d.Linha == LinhaDeForma.UltraEgo).Erradas})");

		// O QUARTO DEFEITO E O DESTE PEDIDO, e ele nao existia antes porque nao havia como erra-lo: "a
		// linha do Ultra Ego INTEIRA tem nuvem" e o atalho que qualquer um escreveria ao ler a ordem do
		// dono, e ele erra em UMA forma so -- a `destroyer`. Um defeito de uma forma so e exatamente o
		// tipo que uma contagem de "3" nao pega se a regra tambem for por linha.
		Conferir(Varrer(d => d.Linha == LinhaDeForma.UltraInstinct
						  || d.Linha == LinhaDeForma.UltraEgo).Erradas == 1,
				 "[injetado] 'a LINHA do Ultra Ego inteira' e pego na `destroyer`, e so nela (deu "
			   + $"{Varrer(d => d.Linha == LinhaDeForma.UltraInstinct || d.Linha == LinhaDeForma.UltraEgo).Erradas})");

		APaletaDasDuas();

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

	/// <summary>
	/// ============================ AS DUAS PALETAS: O UNICO PONTO EM QUE AS NUVENS DIFEREM ============================
	/// *"a aura/carga do ultra ego e a mesma do instinto superior so q ROXA ao inves de branca/prateada,
	/// mas tem os mesmos efeitos"*. A frase tem duas metades e as duas sao mediveis aqui:
	///
	///   * **os mesmos efeitos** -- e a AUSENCIA de qualquer outra diferenca. Nao ha uniform de forma no
	///     `Definir` alem das quatro cores, e quem cobra isso e a <see cref="AAssinaturaBate"/> la
	///     embaixo, que compara o material inteiro. Aqui se cobra o outro lado: que as cores REALMENTE
	///     mudem, senao "a mesma" viraria "a identica".
	///   * **roxa** -- e uma medida de MATIZ, nao de "e diferente". Duas paletas distintas passariam
	///     numa comparacao de igualdade mesmo se a segunda fosse verde.
	///
	/// ============================ E A LUMINANCIA E A METADE QUE NINGUEM MEDE ============================
	/// Este projeto ja calibrou uma rampa so pela ponta clara e entregou Blue marinho e Rose vinho -- o
	/// dono reclamou duas vezes. As duas pontas entram aqui com criterios DIFERENTES de proposito, que
	/// e o que o Core escreve em prosa:
	///
	///   * a ponta ESCURA tem que ficar praticamente na mesma luminancia (ela e quem decide o quanto a
	///     nuvem suja o cenario -- ver `atenua_escuro` no shader, que e correcao de foto);
	///   * a ponta CLARA tem que CAIR um pouco, e nao pode cair muito: sem a queda ela seria um lilas
	///     lavado (branco de novo, o que o dono mandou tirar) e com queda demais o anel colado no corpo
	///     deixaria de ser a coisa mais clara da nuvem.
	/// ==============================================================================================================
	/// </summary>
	private void APaletaDasDuas()
	{
		Nota("  -- 1-b. AS DUAS PALETAS DA MESMA NUVEM --");

		var ui = Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(Jandirus.Core.Forms.Catalogo.Def("ui_sign"));
		var perf = Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(Jandirus.Core.Forms.Catalogo.Def("ui_perfected"));
		var ue = Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(Jandirus.Core.Forms.Catalogo.Def("ultra_ego"));

		Conferir(ui is not null && ue is not null,
				 "as duas paletas EXISTEM (`ui_sign` e `ultra_ego`)");
		if (ui is not { } pu || ue is not { } pe) return;

		// NULO PRA QUEM NAO TEM NUVEM, e isto e o que amarra a paleta na `TemNebulosa`: se a cor pudesse
		// existir sem a nuvem, haveria dois jeitos de perguntar "esta forma tem nebulosa?" e eles
		// divergiriam no primeiro degrau novo. O dono foi explicito -- `Folha(d) == Nebulosa` e a unica
		// verdade sobre quem tem nuvem.
		int comPaleta = Jandirus.Core.Forms.Catalogo.Todas.Count(
			d => Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(d) is not null);
		int divergem = Jandirus.Core.Forms.Catalogo.Todas.Count(
			d => (Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(d) is not null)
				 != Jandirus.Core.Forms.Catalogo.TemNebulosa(d));
		Conferir(comPaleta == 3 && divergem == 0,
				 $"e 'tem paleta' e literalmente 'tem nebulosa' nas {Jandirus.Core.Forms.Catalogo.Todas.Length} "
			   + $"formas ({comPaleta} com paleta, {divergem} discordam) -- a paleta nao e um segundo predicado");

		// OS DOIS ESTAGIOS DE UI DIVIDEM A PALETA. Sem esta linha, "a nuvem e a mesma nos dois" (que e o
		// `UltraInstinct.dm:479`) passaria a valer so pros uniforms de geometria.
		Conferir(perf is { } pp && pp == pu,
				 "os dois estagios do Ultra Instinto usam a MESMA paleta (`UltraInstinct.dm:479` veste "
			   + "os mesmos overlays sem olhar o estagio)");

		// --- E AS DUAS SAO DIFERENTES, NAS QUATRO CORES ---
		Conferir(pu != pe && pu.Borda != pe.Borda && pu.Meio != pe.Meio
			  && pu.Perto != pe.Perto && pu.Pontos != pe.Pontos,
				 $"a do Ultra Ego difere nas QUATRO cores (UI {pu.Borda}/{pu.Meio}/{pu.Perto}/{pu.Pontos} "
			   + $"| UE {pe.Borda}/{pe.Meio}/{pe.Perto}/{pe.Pontos})");

		// --- ROXA, MEDIDA EM MATIZ ---
		// A MATIZ DO PROPRIO EGO e o alvo, e nao "roxo" em abstrato: `8c32be` e o `rgb(140,50,190)` do
		// `UltraEgo.dm:387-392`, que ja veste cabelo, olho e rabo da forma. 12 graus e a folga -- ela
		// aceita as tres pontas da rampa (278,4 a 282,6) e rejeita o azul do UI, que esta a 26 graus.
		float hEgo = new Color("8c32be").H * 360f;
		foreach ((string nome, string hexa) in new[]
				 { ("borda", pe.Borda), ("meio", pe.Meio), ("perto", pe.Perto), ("pontos", pe.Pontos) })
		{
			float h = new Color(hexa).H * 360f;
			float dist = Mathf.Abs(Mathf.Wrap(h - hEgo, -180f, 180f));
			Conferir(dist <= 12f,
					 $"a `{nome}` do Ultra Ego (#{hexa}, matiz {h:0.0} graus) esta a {dist:0.0} graus do "
				   + $"roxo do Ego (`8c32be`, {hEgo:0.0}) -- e o roxo da FORMA, nao um roxo qualquer");
		}

		// E A DO ULTRA INSTINTO NAO ESTA LA, dito por medida: sem esta linha, "as quatro estao perto do
		// roxo do Ego" seria verde num dia em que alguem tingisse as DUAS de roxo e apagasse a diferenca
		// entre as formas -- que e o oposto do pedido.
		float hUiPerto = new Color(pu.Perto).H * 360f;
		Conferir(Mathf.Abs(Mathf.Wrap(hUiPerto - hEgo, -180f, 180f)) > 30f,
				 $"e a ponta clara do Ultra Instinto continua LONGE dele (#{pu.Perto}, {hUiPerto:0.0} "
			   + "graus) -- as duas nuvens nao viraram a mesma cor");

		// --- AS DUAS PONTAS, EM LUMINANCIA ---
		static float Luz(string hexa)
		{
			var c = new Color(hexa);
			return (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) * 255f;
		}

		float escuroUi = Luz(pu.Borda), escuroUe = Luz(pe.Borda);
		float claroUi = Luz(pu.Perto), claroUe = Luz(pe.Perto);

		Nota($"  --     rampa UI  escuro {escuroUi:0.0} -> meio {Luz(pu.Meio):0.0} -> claro {claroUi:0.0} "
		   + $"-> particula {Luz(pu.Pontos):0.0}");
		Nota($"  --     rampa UE  escuro {escuroUe:0.0} -> meio {Luz(pe.Meio):0.0} -> claro {claroUe:0.0} "
		   + $"-> particula {Luz(pe.Pontos):0.0}");

		Conferir(Mathf.Abs(escuroUe - escuroUi) <= 3f,
				 $"a ponta ESCURA sai na mesma luminancia ({escuroUe:0.0} contra {escuroUi:0.0}) -- e ela "
			   + "que decide o quanto a nuvem suja o cenario");

		float razao = claroUe / claroUi;
		Conferir(razao is >= 0.80f and <= 0.95f,
				 $"e a ponta CLARA cai pra {razao * 100:0}% ({claroUe:0.0} contra {claroUi:0.0}): abaixo "
			   + "de 95% ela deixou de ser branca, acima de 80% ainda e o anel de luz");

		// E A ESCADA CONTINUA SUBINDO, nas duas. Uma rampa e uma rampa: se a ponta clara caisse abaixo do
		// meio, o anel colado no corpo deixaria de ser a coisa mais clara da nuvem e o efeito viraria
		// outro desenho -- que e o jeito de "so a cor mudou" deixar de ser verdade sem ninguem ver.
		foreach ((string nome, Jandirus.Core.Forms.PaletaDeNebulosa p) in new[] { ("UI", pu), ("UE", pe) })
			Conferir(Luz(p.Borda) < Luz(p.Meio) && Luz(p.Meio) < Luz(p.Perto)
				  && Luz(p.Perto) < Luz(p.Pontos),
					 $"a rampa {nome} SOBE do escuro pra particula, nesta ordem "
				   + $"({Luz(p.Borda):0.0} < {Luz(p.Meio):0.0} < {Luz(p.Perto):0.0} < {Luz(p.Pontos):0.0})");
	}

	// =====================================================================
	// 0-B. O SHADER: ESCRITO NAO E LIGADO
	// =====================================================================
	/// <summary>
	/// Os uniforms que o efeito PRECISA ter. Tres grupos, e cada um por um motivo diferente:
	///   * `forca`, `semente`, `lado_do_quad` e `campo_do_corpo` sao escritos pelo C# -- escrever num
	///     uniform inexistente e SILENCIOSO no Godot, entao o sumico de qualquer um deles vira "a nuvem
	///     nunca acende" (ou "os pontinhos ficam do tamanho errado") sem uma mensagem em lugar nenhum.
	///     O `campo_do_corpo` e o mais caro de perder dos quatro: sem ele o shader cai no
	///     `hint_default_black`, que quer dizer "este pixel esta no fundo do boneco" -- ou seja a nuvem
	///     sairia CHEIA no quad inteiro, um quadrado violeta por cima do personagem, e nenhuma outra
	///     checagem desta bancada distingue isso de uma mascara correta;
	///   * a rampa (`cor_*`) MUDOU DE GRUPO e hoje esta no primeiro: desde que o Ultra Ego ganhou a
	///     mesma nuvem em roxo, as quatro cores sao ESCRITAS pelo C# (`NebulosaDaForma.Definir`, com a
	///     `Catalogo.PaletaDaNebulosa`). Perder qualquer uma delas nao "muda um botao": deixa a forma
	///     desenhando a paleta da OUTRA, calada -- o Ultra Ego sairia indigo. O `cor_dos_pontos` e o
	///     mais novo dos quatro e o mais facil de perder, porque ele NASCEU desta tarefa: era
	///     `vec3(1.0)` cravado no `fragment`;
	///   * `parada_meio`, `pontos_densidade` e `pontos_brilho` continuam sendo os BOTOES que o dono
	///     afina editando o `.gdshader` e reabrindo o jogo. Virassem constante, o ajuste passaria a
	///     custar um build inteiro -- o efeito continuaria desenhando e a propriedade morreria calada.
	///
	/// O `carga` entrou no PRIMEIRO grupo e nao no segundo: ele e escrito pelo C# (`NebulosaDaForma.Carga`,
	/// alimentado pela `CargaVisual`), e perde-lo e o defeito mais silencioso que ha nesta lista -- a nuvem
	/// continuaria desenhando o estado de REPOUSO pra sempre, e o jogador em Ultra Instinto perderia a
	/// unica leitura de "estou carregando" que lhe sobrou depois que a folha de chama saiu de cena.
	///
	/// Os tres GANHOS (`ganho_da_carga`, `ganho_dos_pontos`, `veu_na_carga`) sao do segundo grupo: sao a
	/// distancia entre os dois estados, e e um numero de arte. Sumidos, os dois estados ficam IGUAIS --
	/// que e exatamente a queixa que este canal existe pra fechar, so que sem nenhum erro no console.
	/// </summary>
	private static readonly string[] UniformsObrigatorios =
	[
		"forca", "semente", "lado_do_quad", "campo_do_corpo",
		"cor_borda", "cor_meio", "cor_perto", "cor_dos_pontos", "parada_meio",
		"pontos_densidade", "pontos_brilho",
		"carga", "ganho_da_carga", "ganho_dos_pontos", "veu_na_carga",
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
	// AS TRES POSES: ANDAR, SOCAR, VOAR
	// =====================================================================
	/// <summary>
	/// O laudo comum das tres poses: FOTOGRAFA e cobra que o quad continue centrado no corpo.
	///
	/// O NUMERO E O MESMO DO PASSO 0 (`PosicaoDoQuadDeTeste`, que devolve o centro do retangulo
	/// DESENHADO e nao a posicao do node -- a distincao ja salvou esta bancada uma vez, com a nuvem
	/// 51 px fora do corpo e o `GlobalPosition` batendo). O que muda e o estado: la o boneco estava
	/// parado e aqui ele esta no meio de uma animacao.
	///
	/// A TOLERANCIA E MAIOR QUE A DO PASSO 0 (1 px) e o motivo e fisico, nao frouxidao: com o corpo
	/// andando, o quadro que a foto pega e o quadro que a medida le podem ser vizinhos, e um passo do
	/// personagem anda ~2 px por quadro. Cobrar 1 px aqui seria cobrar da bancada que ela pare o
	/// tempo. Meio tile continua reprovando qualquer descolamento que o olho veja.
	/// </summary>
	private void ALinhaDaPose(string pose, string prefixoEsperado)
	{
		if (Corpo is not { } corpo) return;
		var neb = corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa");
		var vis = corpo.GetNodeOrNull<CharacterVisual>("Visual");

		// ============================ O CORPO ESTA MESMO NESTA POSE? ============================
		// Esta linha e a que teria pego o defeito de timing sozinha. Sem ela, apertar a tecla e
		// fotografar no mesmo quadro dava duas fotos do boneco PARADO rotuladas "andando" e "socando",
		// com todas as outras checagens VERDES -- porque uma nuvem colada num corpo em pe esta, de
		// fato, colada. O que faltava nao era medir a nuvem: era provar que havia movimento pra ela
		// acompanhar.
		// ====================================================================================
		string agora = vis?.PoseDeTeste ?? "";
		Conferir(agora.StartsWith(prefixoEsperado, StringComparison.Ordinal),
				 $"[{pose}] o corpo esta MESMO nesta pose (`{agora}`, esperado `{prefixoEsperado}*`) -- "
			   + "senao a foto seria de alguem parado com outro nome");

		// ============================ O ALVO E O SPRITE, NAO O NO DO CHAO ============================
		// Escrevi isto comparando com `corpo.GlobalPosition` e a rodada me corrigiu: no voo deu 11,9 px
		// de desvio e passou, porque a tolerancia era maior que o erro. Aqueles 11,9 px sao a ALTITUDE
		// -- voando, o `CharacterVisual` e desenhado ACIMA do no (`vis.Position` e exatamente o
		// deslocamento, como o `Zanzoken` ja anotava). Ou seja: a nuvem estava certa e a regua e que
		// mirava o chao. Com o alvo certo o voo cobra 1 px como qualquer outra pose -- e um dia em que
		// a nuvem parar de acompanhar a altitude, esta linha cai em vez de dar 11,9 e verde.
		// ==========================================================================================
		Vector2 ondeODesenhoEsta = vis?.GlobalPosition ?? corpo.GlobalPosition;
		float fora = neb == null ? 999f : ondeODesenhoEsta.DistanceTo(neb.PosicaoDoQuadDeTeste);
		Conferir(fora < 2f,
				 $"[{pose}] a nuvem NAO descola do SPRITE ({fora:0.0} px entre o centro do quad e o "
			   + $"desenho; o no do chao esta {corpo.GlobalPosition.DistanceTo(ondeODesenhoEsta):0.0} px "
			   + "abaixo) -- ela e IRMA do visual, nao filha, entao nada nisto e automatico");

		// ============================ E A ROTACAO -- COM UMA RESSALVA ============================
		// A nuvem copia a `Transform` do `CharacterVisual`, e o unico jeito de isso importar e ele
		// GIRAR. Mas o voo por habilidade NAO gira: quem chama `VoarPara`/`GirarPara` e o ARREMESSO
		// (knockback), e o voo comum so troca a pose pra `flight_*` e sobe. Entao nesta bancada os dois
		// lados sao zero e a linha e um empate 0 == 0.
		//
		// Fica ASSIM MESMO, e declarada: ela nao prova que a copia funciona -- prova que a nuvem nao
		// inventa rotacao propria, que e o outro jeito de isto quebrar. Quem quiser a prova de verdade
		// precisa de um passo que tome knockback em Ultra Instinto, e nenhuma bancada faz isso hoje.
		// =====================================================================================
		if (neb != null && vis != null)
			Conferir(Mathf.Abs(Mathf.AngleDifference(neb.Rotation, vis.Rotation)) < 0.01f,
					 $"[{pose}] e ela nao inventa rotacao (nuvem {Mathf.RadToDeg(neb.Rotation):0}graus, "
				   + $"corpo {Mathf.RadToDeg(vis.Rotation):0}graus -- o voo comum nao gira ninguem, "
				   + "entao aqui os dois sao zero: e empate, nao prova)");

		Nota($"  --     [{pose}] pose do visual `{vis?.PoseDeTeste}`, chave da mascara "
		   + $"{neb?.PoseDeTeste}, quad {neb?.LadoDeTeste:0} px");
		Fotografar(pose, corpo);
	}

	/// <summary>Segura (ou solta) uma tecla de andar. A foto sai no passo SEGUINTE -- ver o roteiro.</summary>
	private void Tecla(string acao, bool aperta)
	{
		if (aperta)
		{
			Nota("== AS TRES POSES: a nuvem acompanha o boneco em movimento? ==");
			Input.ActionPress(acao);
		}
		else Input.ActionRelease(acao);
		// MEIO SEGUNDO PRA A POSE VIRAR. O ciclo de caminhada tem quadros de ~0,1 s, entao isto e
		// folgado de proposito -- o que nao pode e ser ZERO, que era o caso e deu a foto do boneco
		// parado com o rotulo "andando".
		_espera = aperta ? 0.5 : 0.3;
	}

	/// <summary>
	/// SOCA. O gatilho e `IsActionJustPressed` no `LocalPlayer`, e `Input.ActionPress` NAO o dispara
	/// quando a bancada roda antes dele no quadro -- ver a mesma armadilha, com o mesmo conserto, em
	/// `RoboDeColada.ManterOSoco`: injetar um evento de verdade, que e processado antes de todos os
	/// `_Process`. Duas rodadas daquela bancada morreram nisso, com a tecla "apertada" e o corpo em pe.
	///
	/// A ESPERA E CURTA porque a pose de ataque tambem e: ~0,33 s (`LocalPlayer:794`). Meio segundo
	/// aqui fotografaria o corpo ja de volta em pe.
	/// </summary>
	private void Soco(bool bater)
	{
		Input.ParseInputEvent(new InputEventAction { Action = "attack", Pressed = false });
		if (bater) Input.ParseInputEvent(new InputEventAction { Action = "attack", Pressed = true });
		_espera = bater ? 0.15 : 0.3;
	}

	/// <summary>
	/// DECOLA (ou pousa). Vai por `SendHabilidade` e nao por tecla pelo motivo escrito no
	/// `RoboDeColada.Tecla`: o voo e lido com `IsActionJustPressed` e um press+release no mesmo passo
	/// nao e visto.
	///
	/// E PRECISA DE `--vooteste`: o servidor recusa voo com maestria de Ki abaixo de 50 e nenhum verb
	/// de admin concede NIVEL de skill. Sem a flag o corpo fica no chao e a foto de "voando" sairia de
	/// alguem em pe -- que seria lida como "a nuvem acompanha o voo". Melhor recusar em voz alta.
	/// </summary>
	private void Voar(bool subir)
	{
		if (subir && Array.IndexOf(OS.GetCmdlineArgs(), "--vooteste") < 0)
		{
			_semVoo = true;
			Nota("  --     SEM `--vooteste`: o servidor recusa o voo e NAO ha foto de voo nesta rodada. "
			   + "Rode com a flag pra esta parte existir.");
			_espera = 0.1;
			return;
		}
		if (_semVoo) { _espera = 0.1; return; }

		C?.SendHabilidade("voar");
		// A SUBIDA TEM RAMPA e o sprite so vira `flight_*` acima de uma altura. Medir cedo demais pega
		// o corpo ainda de pe -- o engano que a `RoboDeColada` ja cometeu e anotou.
		_espera = subir ? 2.0 : 0.4;
	}

	/// <summary>VOANDO. E a unica das tres que gira o visual -- ver `ALinhaDaPose`.</summary>
	private void PoseVoando()
	{
		if (_semVoo) { _espera = 0.1; return; }
		ALinhaDaPose("voando", "flight");
		_espera = 0.4;
	}

	/// <summary>O servidor recusou o voo (sem `--vooteste`): os passos de voo nao acontecem.</summary>
	private bool _semVoo;

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
		// O PREENCHIMENTO AQUI E O DO FALLBACK, e por isso ele e NOTA e nao checagem: neste passo o corpo
		// esta na base e a nuvem nunca acendeu, entao o campo ainda e o retangulo de emergencia do
		// `_Ready` (`FracaoDaSilhuetaSemMedida`) -- retangulo enche 1,000 da propria caixa, por definicao.
		// Quem julga a FORMA e a `AFolgaEDerivada`, depois de a forma acender e a silhueta de verdade ser
		// composta. Sem esta frase, o "1" ao lado do "uma elipse encheria 0,785" se le como reprovacao.
		Nota($"  --     lado do quad no mundo: {neb.LadoDeTeste:0} px  |  preenchimento da mascara "
			   + $"{neb.PreenchimentoDeTeste:0.###} (ainda e o retangulo de fallback -- o corpo nao "
			   + "acendeu nenhuma forma; a forma de verdade e julgada depois de vestir)");

		// ============================ OS 5 PIXELS, MEDIDOS ============================
		// A frase do dono e um numero: "no MAXIMO 5 pixels de distancia do corpo". Antes desta linha a
		// bancada media o TAMANHO do quad (a `Nota` acima) e nunca a FOLGA -- e o quad de 96 px passava
		// por ela sem uma reclamacao, com 26 px sobrando de cada lado do boneco.
		//
		// A folga sai MEDIDA no campo de distancia que o shader recebeu, e nao da constante que o
		// construiu -- e ela e UM numero, a maior distancia entre nuvem e corpo (ver `FolgaDeTeste`: por
		// eixo ela mentia). O pedido e um teto, e a nuvem pode colar mais que isso.
		float folga = neb.FolgaDeTeste;
		Conferir(folga > 0f && folga <= FolgaPedida + 0.26f,
				 $"a folga em volta da silhueta cabe nos 5 px do pedido ({folga:0.00} px do pixel de nuvem "
			   + "mais longe do boneco)");

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

		// ============================ E O VEU DO CLIMA E APAGADO NA TELA -- ELE ERA O RUIDO ============================
		// Pedir o clima mais discreto NAO deixa a cena parada, e uma rodada inteira foi perdida antes de
		// eu entender isso. `Neblina` nao e particula: e uma MASSA de shader que cobre a tela toda e
		// DERIVA sozinha (ver `ClimaNaTela`, o par de deslocamento). Ou seja, entre dois quadros
		// consecutivos ~5% dos pixels da tela inteira mudam de tom -- e as tres fotos desta bancada sao
		// quadros consecutivos.
		//
		// O ESTRAGO NAO ERA SUTIL. A subtracao `par-off - sem-corpo` devolveu 116 mil pixels espalhados
		// pela tela inteira como se fossem o personagem; a caixa dele saiu 378x384 (o recorte inteiro),
		// as quatro folgas mediram 0,0 px de mundo -- e as quatro PASSARAM, porque zero tambem e menor
		// que cinco. Um instrumento medindo a propria bruma e dizendo "ok".
		//
		// Filtrar nao resolvia: a bruma nao sai em pontos soltos, sai em ~500 manchas de ~200 px cada, e
		// os tons dela pisam exatamente na faixa em que o boneco difere da grama. O que resolve e nao
		// desenhar o veu: ele nao e o assunto de nenhuma linha desta bancada, e some do jeito mais
		// direto -- o node dele fica invisivel. O clima FORCADO no servidor continua de pe (e ele que
		// impede a chuva de cair no meio da rodada); o que se apaga e o desenho.
		// ==========================================================================================================
		if (World.Instancia?.GetNodeOrNull<Node2D>("Iluminacao/Clima") is { } veu)
		{
			veu.Visible = false;
			Nota("  --     e o VEU do clima foi apagado na tela: ele deriva sozinho e fazia a subtracao "
			   + "de dois quadros consecutivos devolver a tela inteira (ver o bloco no codigo)");
		}
		else Conferir(false, "achei o node do veu do clima pra apaga-lo (sem isso a bruma deriva entre "
							+ "as fotos e a subtracao mede o ceu)");

		// ============================ E A HORA PELO VERB, NAO PELA BANDEIRA ============================
		// O `--horateste 0.5` do cabecalho poe o mundo no meio-dia e depois SOLTA o relogio: um dia
		// dura 24 minutos e esta bancada leva tres, entao ela atravessa tres horas de mundo. Quem a
		// roda sem a bandeira comeca numa hora sorteada -- e uma rodada em cada duas caiu na noite,
		// que ja produziu relatorio errado nesta casa (a `RoboDeOlhada` carrega a mesma anotacao).
		//
		// PIOR QUE ISSO: a rodada anterior ANOITECEU ENTRE AS FOTOS DE REPOUSO E DE CARGA. A tela
		// inteira mudou de cor entre as duas, e a subtracao que deveria isolar a nuvem trouxe o
		// crepusculo junto -- a medida de carga daquele relatorio teve que ser jogada fora.
		// ==========================================================================================
		C?.SendVerbo("admin_meio_dia");
		Nota("  --     e o ceu adiantado pro MEIO-DIA (`admin_meio_dia`) -- a foto de um efeito claro "
		   + "contra fundo escuro nao se compara com a mesma foto de dia");

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
		_ultimaFormaForcada = id;
		_espera = espera;
	}

	/// <summary>
	/// A ULTIMA FORMA que o roteiro mandou vestir. Existe por causa do <see cref="Alternar"/>: desde
	/// que a nuvem tem duas paletas, reacende-la exige a COR, e um id cravado ali ("ui_sign") seria
	/// uma segunda descricao do roteiro -- um passo novo que trocasse a forma antes da subtracao faria
	/// o par ligado/desligado voltar com a paleta de outra forma, e a foto sairia com a cor errada
	/// sem ninguem reclamar.
	/// </summary>
	private string _ultimaFormaForcada = "";

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

	/// <summary>Os quatro uniforms que a paleta escreve. Ver <see cref="ANuvemRoxaChegaNoPixel"/>.</summary>
	private static readonly string[] UniformsDaPaleta =
		["cor_borda", "cor_meio", "cor_perto", "cor_dos_pontos"];

	/// <summary>
	/// ============================ A NUVEM ROXA, NO CORPO DE VERDADE ============================
	/// Tudo o que a secao 1 mede e INTENCAO: funcoes puras conferidas contra hexa. Este projeto ja
	/// pagou caro por parar ai -- quatro defeitos visuais atravessaram milhares de checagens verdes
	/// porque *"uniform escrito nao e pixel desenhado"*. Aqui o corpo vestiu `ultra_ego` pelo caminho
	/// de producao (servidor -> pacote -> `Transformacao.Vestir`) e o que se le e o MATERIAL.
	///
	/// ============================ E A PERGUNTA CENTRAL E A SEGUNDA ============================
	/// *"a mesma do instinto superior so q ROXA, mas tem os mesmos efeitos"*. "Os mesmos efeitos" nao e
	/// medivel dizendo o que MUDA -- e medivel dizendo o que NAO muda. Entao a assinatura inteira do
	/// material e comparada com a do `ui_sign`, e o que se cobra e que a diferenca sejam EXATAMENTE os
	/// quatro uniforms de cor. Um `opacidade` diferente, um `pontos_densidade` diferente, um
	/// `escala` diferente: qualquer um deles cai aqui, porque nenhum deles e cor. (O `carga` fica de
	/// fora, medido e impresso -- ver o bloco dele la embaixo.)
	///
	/// Ela e a irma da comparacao `sign` x `perfected` (ver <see cref="DepoisDeVestir"/>), com o sinal
	/// trocado: la se cobra IGUALDADE TOTAL, aqui se cobra igualdade EM TUDO MENOS a paleta. As duas
	/// leem a mesma assinatura, entao um uniform novo entra nas duas sozinho.
	/// ==========================================================================================
	/// </summary>
	private void ANuvemRoxaChegaNoPixel()
	{
		Nota("== 6. O ULTRA EGO: A MESMA NUVEM, EM ROXO ==");

		if (Corpo is not { } corpo
			|| corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is not { } neb)
		{
			Conferir(false, "o corpo tem o node `Nebulosa` pra medir a paleta roxa");
			return;
		}

		Conferir(neb.ForcaDeTeste == 1f,
				 $"a nuvem do `ultra_ego` ACENDEU pelo caminho de producao (forca={neb.ForcaDeTeste:0.##})");

		var pe = Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(
			Jandirus.Core.Forms.Catalogo.Def("ultra_ego"));
		var pu = Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(
			Jandirus.Core.Forms.Catalogo.Def("ui_sign"));
		if (pe is not { } roxa || pu is not { } indigo) { Conferir(false, "as duas paletas existem"); return; }

		// --- (a) AS QUATRO CORES CHEGARAM, E SAO AS DO EGO ---
		// DUAS COMPARACOES E NENHUMA E REDUNDANTE, o mesmo par do `RoboDeForma`: contra o Core e o
		// ENCANAMENTO (a cor escolhida chegou inteira ate o `ShaderMaterial`); contra a paleta do Ultra
		// Instinto e a PROVA DE QUE ELA FOI REESCRITA -- o node e o mesmo desde o nascimento do corpo, e
		// uma paleta escrita so no `_Ready` deixaria o Ultra Ego com a cor indigo do primeiro dono.
		const float UmPasso = 1f / 255f;
		foreach ((string uniform, string doEgo, string doInstinto) in new[]
				 {
					 ("cor_borda", roxa.Borda, indigo.Borda),
					 ("cor_meio", roxa.Meio, indigo.Meio),
					 ("cor_perto", roxa.Perto, indigo.Perto),
					 ("cor_dos_pontos", roxa.Pontos, indigo.Pontos),
				 })
		{
			Color? noMaterial = neb.CorDeTeste(uniform);
			var alvo = new Color(doEgo);
			Conferir(noMaterial is { } c
				  && Mathf.Abs(c.R - alvo.R) <= UmPasso
				  && Mathf.Abs(c.G - alvo.G) <= UmPasso
				  && Mathf.Abs(c.B - alvo.B) <= UmPasso,
					 $"`{uniform}` CHEGA no shader em #{doEgo} (deu "
				   + $"{(noMaterial is { } c2 ? "#" + c2.ToHtml(false) : "uniform inexistente")})");
			Conferir(noMaterial is { } c3 && !c3.IsEqualApprox(new Color(doInstinto)),
					 $"-- e nao e mais o #{doInstinto} do Ultra Instinto (a paleta foi REESCRITA na troca "
				   + "de forma, e nao herdada do `_Ready`)");
		}

		// --- (b) E SO A COR MUDOU ---
		string assinatura = neb.AssinaturaDeTeste();
		if (_assinaturaDoSign is not { } doSign)
		{
			Conferir(false, "a assinatura do `ui_sign` foi guardada la atras (sem ela nao ha com o que "
						  + "comparar)");
			return;
		}

		static Dictionary<string, string> Partir(string a)
		{
			var m = new Dictionary<string, string>();
			foreach (string parte in a.Split(';', StringSplitOptions.RemoveEmptyEntries))
			{
				int i = parte.IndexOf('=');
				if (i > 0) m[parte[..i]] = parte[(i + 1)..];
			}
			return m;
		}

		Dictionary<string, string> ego = Partir(assinatura), sign = Partir(doSign);
		Conferir(ego.Count > 0 && ego.Count == sign.Count,
				 $"as duas assinaturas descrevem os MESMOS uniforms ({ego.Count} e {sign.Count}) -- "
			   + "conjuntos diferentes nao se comparam");

		// ============================ O `carga` SAI DA COMPARACAO, E MEDIDO ============================
		// Ele e o unico uniform da assinatura que NAO descreve o efeito: e o estado do INSTANTE (quanto o
		// corpo esta reunindo energia agora). A comparacao `sign` x `perfected` pode inclui-lo porque as
		// duas fotos sao tiradas a segundos uma da outra, com o C solto nas duas (o roteiro cobra isso no
		// passo 15). Esta aqui e do OUTRO LADO do roteiro -- entre as duas capturas passaram o par da
		// carga, tres poses, uma arvore e o `ssj1` inteiro --, e cobrar que o Ki esteja no mesmo ponto
		// quatro minutos depois seria cobrar o relogio, nao o desenho.
		//
		// ELE SAI NOMEADO E COM O VALOR IMPRESSO, e nao apagado: a `Nota` abaixo mostra os dois lados,
		// entao um `carga` que ficasse preso em 1,2 numa forma e 0 na outra continua visivel pra quem le
		// o log. O que nao se faz e chamar isso de "efeito diferente".
		const string DoInstante = "carga";
		Nota($"  --     `{DoInstante}` fora da comparacao (estado do instante): sign="
		   + $"{sign.GetValueOrDefault(DoInstante, "?")}  ultra_ego={ego.GetValueOrDefault(DoInstante, "?")}");

		List<string> diferentes = [.. ego.Where(p => p.Key != DoInstante)
									   .Where(p => !sign.TryGetValue(p.Key, out string? v) || v != p.Value)
									   .Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal)];
		string[] esperadas = [.. UniformsDaPaleta.OrderBy(k => k, StringComparer.Ordinal)];

		Nota($"  --     uniforms que diferem do `ui_sign`: {string.Join(", ", diferentes)}");
		Conferir(diferentes.SequenceEqual(esperadas),
				 "e a UNICA diferenca pro `ui_sign` sao os quatro uniforms de COR -- *\"tem os mesmos "
			   + $"efeitos\"* medido no material inteiro (esperado: {string.Join(", ", esperadas)})");

		// --- (c) E NENHUMA CHAMA POR CIMA ---
		// A queixa original do dono sobre o Ultra Instinto foi a nuvem E a `colorablebigaura` acesas
		// juntas. O Ultra Ego chegou aqui pelo mesmo caminho e podia repetir o defeito: a entrada dele
		// declara `Aura = "b96bff"`, e essa cor so nao vira chama porque a `Folha` dele deixou de ser
		// colorivel. Ler o simbolo prova que o caminho da chama esta mudo pra ele.
		Conferir(SpriteDeAura.CaminhoDa(Jandirus.Core.Forms.Catalogo.Folha(
					 Jandirus.Core.Forms.Catalogo.Def("ultra_ego"))) == null,
				 "e o `ultra_ego` nao tem ARQUIVO de chama nenhum -- a nuvem nao divide a tela com a "
			   + "`colorablebigaura`, que foi a queixa original do dono");

		Fotografar("ultra-ego", corpo);
		_espera = 0.5;
	}

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
	///
	/// ============================ E EM ULTRA INSTINTO O NUMERO CERTO DE CHAMAS E ZERO ============================
	/// Este passo cobrava `chamaDaCarga == true` -- "a chama da carga acendeu" --, e isso deixou de ser
	/// o certo por ordem do dono: *"a aura/carga do ultra instinto deveria ser essa aura em shaders, e
	/// nao o icone de carga atual"*. A folha de chama saiu de cena nesta linha de forma inteira, e quem
	/// desenha a carga passou a ser a NUVEM.
	///
	/// Entao a pergunta "ha duas chamas empilhadas?" ficou mais forte, nao mais fraca: nenhum dos dois
	/// desenhistas pode acender, e o que tem que mudar e o `carga` da nuvem. Sao TRES fatos no mesmo
	/// instante -- e sao os tres pedidos do dono medidos juntos, que e o unico jeito de nenhum deles
	/// passar as custas do outro.
	/// ========================================================================================================
	/// </summary>
	private void ASobrecargaNaoEmpilhaChama()
	{
		if (Corpo is not { } corpo) return;
		var neb = corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa");
		var aura = corpo.GetNodeOrNull<Aura>("Aura");
		var carga = corpo.GetNodeOrNull<CargaVisual>("Carga");

		bool chamaDaCarga = carga?.DesenhoDeTeste.Visible == true;
		bool chamaDaForma = aura?.DesenhoDeTeste.Visible == true;
		float adensou = neb?.CargaDeTeste ?? 0f;

		Nota($"  --     [carga] Ki em {RazaoDeKi * 100:0}% do tanque "
				  + $"(chama da carga={chamaDaCarga}, chama da forma={chamaDaForma}, "
				  + $"carga da nuvem={adensou:0.##})");

		// GUARDADO PRA O OUTRO LADO DA MOEDA. O passo do Ki caindo vai cobrar que esta MESMA medicao
		// diga o contrario -- e e a mudanca dela, entre os dois momentos, que prova que ela enxerga.
		//
		// O QUE SE GUARDA MUDOU DE SENSOR, e tinha que mudar: era `chamaDaCarga`, que em Ultra Instinto
		// e falso nos DOIS momentos -- um controle negativo que da o mesmo valor sempre e um sensor
		// morto dando verde. Quem se mexe agora e o `carga` da nuvem, que e o desenho que substituiu a
		// chama. O par continua sendo a MESMA medicao no mesmo corpo em dois instantes.
		_cargaComKiAlto = adensou;
		_kiAlto = RazaoDeKi;

		Conferir(RazaoDeKi > 1.0, $"segurar C passou dos 100% ({RazaoDeKi * 100:0}%)");

		// ============================ 1. NENHUMA CHAMA, E NAO "SO UMA" ============================
		// Os DOIS desenhistas ficam mudos porque os dois recebem o mesmo simbolo `FolhaDeAura.Nebulosa`
		// do Core (ver `SpriteDeAura.DefinirFolha`). `SemFolha` diz que eles nao PODEM acender, o que e
		// mais forte que `Visible == false` -- este ultimo tambem seria verdade num corpo em repouso.
		// ====================================================================================
		Conferir(!chamaDaCarga && !chamaDaForma,
				 "em Ultra Instinto NENHUMA das duas chamas acende ao carregar -- o desenho e a nuvem");
		Conferir(carga?.DesenhoDeTeste.SemFolha == true && aura?.DesenhoDeTeste.SemFolha == true,
				 "e e por NAO TEREM FOLHA, nao por estarem apagados (a carga e a aura, os dois)");

		// ============================ 2. A NUVEM CONTINUA ACESA (e da FORMA) ============================
		Conferir(neb?.ForcaDeTeste == 1f,
				 $"com o Ki ACIMA de 100% a nuvem continua acesa (forca={neb?.ForcaDeTeste:0.##})");

		// ============================ 3. E ELA SE DISTINGUE DO REPOUSO ============================
		// A nuvem tem DOIS papeis no mesmo desenho -- overlay da forma (sempre) e aura de carga (com o C
		// ou acima dos 100%). Se ela nao mudasse nada, o jogador em Ultra Instinto teria perdido a
		// leitura de "estou carregando" que a chama dava, e a tarefa teria trocado um efeito por nada.
		//
		// O NUMERO E O MESMO `forca` DA CHAMA (0,70..0,95 carregando; 0,95..1,50 na sobrecarga -- ver
		// `CargaVisual.Pintar`), entao o piso de 0,5 e folgado de proposito: ele afirma "esta bem acima
		// de zero e dentro da faixa que a chama usaria", e nao um instante do pulso.
		// ==================================================================================
		Conferir(adensou > 0.5f && adensou <= 2.0f,
				 $"e a nuvem ADENSA enquanto o corpo carrega (carga={adensou:0.##}) -- e o segundo papel "
			   + "dela, e e o que substituiu a chama na leitura");

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
		//
		// ============================ E O SENSOR TROCOU DE DESENHO JUNTO COM A CARGA ============================
		// A medicao era `CargaVisual.DesenhoDeTeste.Visible` -- a chama. Em Ultra Instinto ela e falsa nos
		// DOIS momentos (a folha saiu de cena por ordem do dono), entao ela virou um sensor morto: um
		// controle negativo que da o mesmo valor sempre "fecha" em cima de qualquer coisa.
		//
		// Quem carrega a leitura de carga nesta forma agora e o `carga` da NUVEM, e e ele que muda entre
		// os dois instantes. O par continua sendo a MESMA medicao no mesmo corpo -- e a comparacao
		// continua sendo a razao de existir do passo: sem ela, "a nuvem nao se apagou com o Ki baixo"
		// seria compativel com um jogo em que nada nunca acontece.
		// ====================================================================================================
		float cargaAgora = neb?.CargaDeTeste ?? 0f;
		Conferir(cargaAgora == 0f,
				 $"e a CARGA DA NUVEM voltou a ZERO com o C solto e o Ki em {r * 100:0}% (deu "
			   + $"{cargaAgora:0.##}) -- o SEGUNDO papel dela obedece ao Ki (a regra do dono); o "
			   + "primeiro, o overlay da forma, nao");
		Conferir(_cargaComKiAlto > 0.5f && cargaAgora == 0f,
				 $"O CONTROLE FECHA: a MESMA medicao viu a nuvem adensada ({_cargaComKiAlto:0.##}) a "
			   + $"{_kiAlto * 100:0}% e em repouso a {r * 100:0}% -- entao 'a nuvem continua ACESA' e um "
			   + "fato medido, e nao um sensor morto");

		// SE O DRENO DERRUBOU A FORMA nao ha nada a consertar aqui: o proximo passo do roteiro e
		// `admin_forma ui_perfected`, que veste de novo (e a estreia dele ja foi queimada la em cima).
		// Todos os passos de baixo -- o segundo estagio, o par de subtracao, a elipse e as quatro
		// medidas de particula -- pegam a nuvem acesa de qualquer jeito.
		if (!aindaVestido)
			Nota("  --     (o dreno derrubou a forma antes do alvo; o passo seguinte ja veste de novo)");

		if (aindaVestido) Fotografar("ki-baixo", Corpo);
		_espera = 0.4;
	}

	/// <summary>
	/// O `carga` da nuvem no instante em que o Ki estava acima de 100%. Ele e o par do
	/// <see cref="_kiAlto"/>, e existe pra o passo do Ki caindo ter com o que comparar -- ver
	/// <see cref="ASobrecargaNaoEmpilhaChama"/>. (Era um `bool _chamaComKiAlto` que lia a chama, e a
	/// chama nao acende mais nesta forma.)
	/// </summary>
	private float _cargaComKiAlto;
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
	/// A MASCARA CHAPADA CAI EM CIMA DO CORPO? Mede o CENTROIDE dos pixels que o modo diagnostico
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
	private void AMascaraCaiNoCorpo()
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

		if (n < 200) { Conferir(false, $"a mascara chapada aparece na foto (so {n} px indigo)"); return; }

		// O recorte esta em pixel de TELA e o centro dele e o corpo; o desvio volta pra pixel de MUNDO
		// pelo zoom, que e a unidade em que o defeito foi diagnosticado (meio quad = 48).
		int zoom = Mathf.Max(World.Instancia?.ZoomDeTeste ?? 1, 1);
		double dx = (sx / n - img.GetWidth() / 2.0) / zoom;
		double dy = (sy / n - img.GetHeight() / 2.0) / zoom;
		double d = Math.Sqrt(dx * dx + dy * dy);

		// O TETO E 12 PX DE MUNDO e ele nao e frouxo por acaso: o centroide de uma mascara cortada pela
		// borda da foto puxa alguns pixels, e a silhueta de um lutador em pe nao e centrada na vertical
		// (a cabeca e estreita, as pernas ocupam a metade de baixo). Colada no boneco, esse vies e MAIOR
		// do que era com a elipse -- e legitimo: o centroide do desenho nao e o centro da caixa dele. O
		// defeito que esta linha persegue media 70 px de distancia, entao nao ha risco de confundir os
		// dois.
		Conferir(d < 12,
				 $"a mascara desenhada CAI NO CORPO (centro a {d:0.0} px de mundo dele: "
			   + $"{dx:+0.0;-0.0} em x, {dy:+0.0;-0.0} em y, de {n} px medidos)");
	}

	// =====================================================================
	// 3-B. AS MICROPARTICULAS EXISTEM, E A DENSIDADE AFINA
	// =====================================================================
	/// <summary>
	/// O HISTOGRAMA DE BRILHO de cada estado medido -- 256 caixas, indexadas pelo MENOR canal do pixel.
	///
	/// ============================ POR QUE HISTOGRAMA, E NAO UMA CONTAGEM ============================
	/// Aqui havia um numero por estado: "quantos pixels de BRANCO PURO (menor canal >= 250)". Ele
	/// dependia de um limiar cravado, e uma rodada o derrubou: com o ceu daquele dia o pixel mais claro
	/// da nuvem inteira foi 239, entao as OITO medidas deram ZERO e tres linhas reprovaram um efeito
	/// intacto (as microparticulas estavam la, na foto; o que faltou foi o 250).
	///
	/// O limiar nao podia mesmo ser cravado: ele separa "particula" de "ponta clara da nuvem", e as duas
	/// sobem e descem juntas com a luz do dia, com o clima e com o `CanvasModulate` da hora. Guardando o
	/// histograma inteiro, o limiar vira DERIVADO -- ver `AsParticulasExistemEAfinam`: ele sai do estado
	/// em que os pontinhos estao COMPROVADAMENTE apagados, e por construcao nao ha nada acima dele ali.
	/// Custa 1 KB por estado.
	/// ==========================================================================================
	/// </summary>
	private readonly Dictionary<string, int[]> _histos = [];

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
	private bool Medir(string rotulo)
	{
		if (Recorte(Corpo) is not { } img)
		{
			Nota($"  --     [pontos/{rotulo}] sem foto (headless nao renderiza)");
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

		if (x1 <= x0 || y1 <= y0)
		{
			Nota($"  --     [pontos/{rotulo}] o quad nao cai dentro do recorte -- medida invalida");
			_espera = 0;
			return true;
		}

		if (!_histos.TryGetValue(rotulo, out int[]? histo)) _histos[rotulo] = histo = new int[256];

		for (int y = y0; y < y1; y++)
		for (int x = x0; x < x1; x++)
		{
			Color c = img.GetPixel(x, y);
			// O MENOR DOS TRES CANAIS e a medida, e nao a luminancia: a rampa da nuvem chega a ciano
			// quase branco (`d8e8ff` -- menor canal 216) e um teste de luminancia a leria como
			// particula. Exigir os TRES canais altos e o que separa o pontinho da ponta clara da nuvem.
			histo[Mathf.Clamp((int)(Mathf.Min(c.R, Mathf.Min(c.G, c.B)) * 255f), 0, 255)]++;
		}

		// ============================ A AMOSTRA E DE DEZ QUADROS ESPALHADOS ============================
		// Dez quadros COLADOS nao adiantariam: a 60 Hz eles cobrem 0,16 s e as particulas mal se mexem
		// -- seria o mesmo quadro contado dez vezes, com o mesmo azar. Com 0,06 s entre eles a amostra
		// atravessa ~0,6 s e pega a subida, que e onde a variacao mora.
		// ==========================================================================================
		if (++_amostras < QuadrosPorMedida)
		{
			_passo--;          // fica neste mesmo passo, coletando
			_espera = 0.06;
			return false;
		}

		int pico = 255;
		while (pico > 0 && histo[pico] == 0) pico--;
		Nota($"  --     [pontos/{rotulo}] histograma de {_amostras} quadros somados; pixel mais claro "
		   + $"do quad: {pico} (o limiar dos pontinhos e DERIVADO, ver o veredito)");
		_amostras = 0;
		_espera = 0;
		return true;
	}

	/// <summary>Quantos pixels deste estado tem o menor canal em <paramref name="limiar"/> ou acima.</summary>
	private int Acima(string rotulo, int limiar)
	{
		if (!_histos.TryGetValue(rotulo, out int[]? h)) return -1;
		int n = 0;
		for (int i = limiar; i < 256; i++) n += h[i];
		return n;
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
		string[] estados = ["brilho 0", "padrao", "densidade 0", "densidade 1",
							"densidade 0,25", "densidade 0,50", "densidade 0,75", "densidade 1,00"];
		if (estados.Any(e => !_histos.ContainsKey(e)))
		{
			Nota("  --     [pontos] sem foto nesta rodada -- as oito medidas nao existem "
			   + "(headless? entao rode com janela)");
			_espera = 0.3;
			return;
		}

		// ============================ O LIMIAR SAI DO ESTADO SEM PONTINHOS ============================
		// `pontos_brilho = 0` e o estado em que as particulas estao COMPROVADAMENTE apagadas -- e o
		// unico botao do shader que zera o desenho delas sem mexer em mais nada. Entao o pixel mais
		// claro que aparece ali e, por definicao, "o mais claro que esta nuvem consegue ser sem
		// pontinho": tudo acima disso, nos outros estados, e pontinho.
		//
		// E O `+1` E O LIMIAR INTEIRO: com ele, a contagem do proprio "brilho 0" e ZERO por construcao.
		// Isso nao e circular -- e o que torna a subtracao `padrao - brilho 0` uma contagem de
		// particulas em vez de uma diferenca entre dois numeros grandes e parecidos. E o controle de
		// que ele nao virou um limiar impossivel esta logo abaixo, na linha das DUAS maneiras de apagar:
		// `densidade 0` e outro caminho, e ele tem que medir zero TAMBEM -- e nao mede por construcao.
		// ======================================================================================
		int[] semPontos = _histos["brilho 0"];
		int limiar = 255;
		while (limiar > 0 && semPontos[limiar] == 0) limiar--;
		limiar++;

		int semBrilho = Acima("brilho 0", limiar), comBrilho = Acima("padrao", limiar);
		int densidade0 = Acima("densidade 0", limiar), densidade1 = Acima("densidade 1", limiar);
		int rampa25 = Acima("densidade 0,25", limiar), rampa50 = Acima("densidade 0,50", limiar);
		int rampa75 = Acima("densidade 0,75", limiar), rampa100 = Acima("densidade 1,00", limiar);

		Nota($"  --     [pontos] limiar DERIVADO desta rodada: menor canal >= {limiar} (o mais claro "
		   + $"sem pontinhos foi {limiar - 1}) -- com o ceu de outra hora este numero e outro, e e por "
		   + "isso que ele nao e cravado");
		Nota($"  --     [pontos] px acima do limiar, somados em {QuadrosPorMedida} quadros: brilho 0 -> "
		   + $"{semBrilho}  |  padrao -> {comBrilho}  |  densidade 0 -> {densidade0}  |  densidade 1 -> "
		   + $"{densidade1}");

		int existem = comBrilho - semBrilho;
		int afina = densidade1 - densidade0;

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
			   + $"tira {existem} px do topo do brilho ({comBrilho} com, {semBrilho} sem)");

		Conferir(afina >= 25,
				 $"e a DENSIDADE afina de verdade: `pontos_densidade` de 0 a 1 poe {afina} px "
			   + $"({densidade0} em 0, {densidade1} em 1) -- o botao move a tela, nao so o arquivo");

		// ============================ O INSTRUMENTO SE JULGA ============================
		// `pontos_brilho = 0` e `pontos_densidade = 0` sao DOIS caminhos pro mesmo estado: nenhum ponto
		// desenhado. Eles tem que medir quase a mesma coisa. Se nao medirem, o que varia entre os
		// quadros nao sao os pontos -- e ai as duas diferencas acima estao medindo outra coisa e nao
		// valem nada, mesmo tendo dado verde.
		// ============================================================================
		int discordancia = Math.Abs(semBrilho - densidade0);
		Conferir(discordancia <= Math.Max(existem, afina) / 2,
				 $"e as DUAS maneiras de apagar os pontos medem a mesma coisa (diferem em "
			   + $"{discordancia} px) -- e isso que torna as duas linhas de cima confiaveis");

		// --- A RAMPA: o SENTIDO do botao, com a variancia a mostra ---
		Nota($"  --     [pontos] rampa da densidade (px somados em {QuadrosPorMedida} quadros): "
		   + $"0,25 -> {rampa25}  |  0,50 -> {rampa50}  |  0,75 -> {rampa75}  |  1,00 -> {rampa100}");
		Conferir(rampa100 > rampa25,
				 $"e o botao SOBE: quatro vezes o portao ({rampa25} em 0,25 contra {rampa100} em 1,00) "
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

	// =====================================================================
	// 3-D. OS DOIS ESTADOS DA NUVEM, MEDIDOS EM PIXEL
	// =====================================================================
	/// <inheritdoc cref="OParDaCarga"/>
	private int _fasesDaCarga;

	/// <summary>
	/// ============================ POR QUE ESTE PAR EXISTE ============================
	/// A bancada ja cobrava que o `carga` do material subisse ao segurar o C. Isso e o UNIFORM, e o
	/// relatorio anterior escreveu com todas as letras que era o buraco mais provavel de sobrar:
	/// *"a checagem da carga e do uniform, nao do pixel"*. Esta casa ja pagou por essa distincao --
	/// "uniform escrito nao e pixel desenhado" e uma das tres cegueiras que deixaram quatro defeitos
	/// visuais passarem por milhares de checagens verdes.
	///
	/// ============================ E POR QUE SAO TRES FOTOS, E NAO DUAS ============================
	/// A nuvem e ANIMADA: `t = TIME * velocidade`, e a rampa rola sozinha. Dois quadros consecutivos
	/// discordam em milhares de pixels COM A CARGA PARADA -- e uma subtracao de duas fotos leria essa
	/// rolagem como se fosse o efeito da carga. Foi assim que a medida do relatorio anterior teve que
	/// ser jogada fora (la o confundidor foi o anoitecer; aqui seria o proprio shader).
	///
	/// Entao a terceira foto e o CONTROLE DE DERIVA: dois quadros consecutivos com a carga DESLIGADA
	/// nos dois. O que sobra ali e tudo que se mexe sozinho. O sinal da carga tem que ser muito maior
	/// que ele -- e o mesmo par vira o defeito injetado logo abaixo, sem inventar dado nenhum.
	///
	/// A CARGA E ESCRITA NO NODE, e nao pelo C: com o C na mao a `CargaVisual` repinta TODO QUADRO
	/// (ela pulsa), entao o valor mudaria entre as duas fotos e nao daria pra desligar por um quadro.
	/// Mexer no node aqui e legitimo pelo mesmo motivo do par `Alternar`: os passos de cima ja
	/// provaram que o caminho de producao (snapshot -> `CargaVisual.Pintar` -> `Carga`) chega -- este
	/// par existe pra LOCALIZAR o desenho, nao pra provar o encanamento.
	/// ==========================================================================================
	/// </summary>
	private void OParDaCarga()
	{
		if (Corpo is not { } corpo || corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is not { } neb)
		{
			Conferir(false, "o corpo e a nuvem existem pro par de fotos da carga");
			return;
		}

		// UM QUADRO ENTRE ESCREVER E FOTOGRAFAR, sempre: `GetTexture().GetImage()` devolve o quadro JA
		// desenhado, ou seja o anterior. Ver o bloco do par de subtracao la no roteiro.
		switch (_fasesDaCarga++)
		{
			case 0: neb.Carga(false, 0f); _passo--; _espera = 0; return;
			case 1: Fotografar("carga-off0", corpo); _passo--; _espera = 0; return;
			case 2: Fotografar("carga-off1", corpo);
					// 1,2 E A SOBRECARGA, e nao um numero de bancada: a `CargaVisual.Pintar` manda
					// 0,95..1,50 acima dos 100% de Ki. Medir com um valor que a producao nunca envia
					// mediria um estado que nao existe no jogo.
					neb.Carga(true, 1.2f); _passo--; _espera = 0; return;
			default: Fotografar("carga-on", corpo); break;
		}

		ACargaMudaAOlho(neb);
		neb.Carga(false, 0f);   // devolve o node ao estado de repouso -- o C esta solto
		_espera = 0.4;
	}

	/// <summary>
	/// O JULGAMENTO, e ele e uma funcao pra ser exercitado AO CONTRARIO com dado de verdade.
	///
	/// Duas perguntas, e a segunda e o que separa "mudou" de "o shader estava rolando":
	///   * O SINAL EXISTE  -- a carga repinta uma area comparavel a da propria nuvem, e nao dez pixels.
	///   * E ELE E MAIOR QUE A DERIVA -- tres vezes o que dois quadros parados ja discordam sozinhos.
	/// </summary>
	private static (bool Ok, string Laudo) ACargaSeVe(int sinal, int deriva, int daNuvem, string quem)
	{
		bool grande = sinal >= Mathf.Max(daNuvem / 5, 200);
		bool acimaDaDeriva = sinal >= deriva * 3;

		// O PISO SAI DA AREA DO QUAD, e o quad e so a REFERENCIA -- nao o teto do que se conta. A medida
		// deu 73 mil pixels num quad de 16 mil, e isso nao e erro: a nuvem mais densa alimenta o brilho
		// da cena, que espalha o clarao muito alem do retangulo dela (delta baixo numa area larga). O
		// que se cobra e "mudou bem mais do que um quinto do proprio quad", que continua valendo.
		return (grande && acimaDaDeriva,
				$"[{quem}] a carga repintou {sinal} px do recorte (o quad da nuvem tem {daNuvem}; o "
			  + $"clarao dela espalha alem disso); a DERIVA de dois quadros parados e {deriva} px "
			  + $"[grande {grande}, acima da deriva {acimaDaDeriva}]");
	}

	/// <inheritdoc cref="OParDaCarga"/>
	private void ACargaMudaAOlho(NebulosaDaForma neb)
	{
		Nota("== OS DOIS ESTADOS DA NUVEM, EM PIXEL DESENHADO ==");

		if (!_cortes.TryGetValue("carga-off0", out Image? a)
		 || !_cortes.TryGetValue("carga-off1", out Image? b)
		 || !_cortes.TryGetValue("carga-on", out Image? c))
		{
			Nota("  --     sem as tres fotos (headless?) -- a carga fica medida so no uniform");
			return;
		}

		Conferir(_cantos["carga-off0"] == _cantos["carga-off1"]
			  && _cantos["carga-off1"] == _cantos["carga-on"],
				 "as tres fotos da carga sao do MESMO enquadramento -- sem isso a subtracao compara "
			   + "lugares diferentes do mundo");

		Caixa(OndeDiscordam(a, b), a.GetWidth(), out int deriva);
		Caixa(OndeDiscordam(b, c), b.GetWidth(), out int sinal);

		// QUANTO A NUVEM OCUPA, pra o "grande" ter uma referencia que nao seja um numero cravado: e a
		// area do quad na tela, perguntada ao node. Um teto em pixel absoluto envelheceria no dia em que
		// o zoom da camera mudasse.
		Rect2 quad = neb.RetanguloNaTelaDeTeste;
		int daNuvem = Mathf.RoundToInt(quad.Size.X * quad.Size.Y);

		(bool ok, string laudo) = ACargaSeVe(sinal, deriva, daNuvem, "carga");
		Conferir(ok, "a nuvem MUDA DE ESTADO na tela quando o corpo carrega: " + laudo);

		// ============================ O DEFEITO INJETADO, COM DADO DE VERDADE ============================
		// A mesma funcao, alimentada com o par que tem a carga DESLIGADA dos dois lados. Se ela passasse
		// ali, "a nuvem muda de estado" seria a rolagem do shader com outro nome -- e e exatamente a
		// confusao que esta bancada ja cometeu uma vez, lendo a grama balancando como se fosse o efeito.
		// ==========================================================================================
		Nota("  --     agora o DEFEITO INJETADO: a linha abaixo tem que dizer que reprovou");
		Conferir(!ACargaSeVe(deriva, deriva, daNuvem, "carga parada").Ok,
				 "[injetado] o par com a carga DESLIGADA nos dois lados reprova na mesma linha -- "
			   + ACargaSeVe(deriva, deriva, daNuvem, "carga parada").Laudo);
	}

	// =====================================================================
	// 3-E. O CONTRA-EXEMPLO: A CHAMA NAO SAIU DO JOGO, SAIU DE UMA LINHA
	// =====================================================================
	/// <summary>
	/// ============================ SEM ESTA LINHA, "SUMIU A CHAMA" PASSAVA VERDE ============================
	/// Todas as linhas de carga desta bancada cobram AUSENCIA: nenhuma chama acende, os dois desenhistas
	/// estao sem folha, a nuvem e quem adensa. Ausencia e a coisa mais facil de entregar por engano --
	/// apagar a folha de chama do jogo INTEIRO (um `_ => null` no `CaminhoDa`, um `SemFolha` sempre
	/// verdadeiro, o `SpriteDeAura` nascendo quebrado) satisfaz todas elas com o placar limpo.
	///
	/// O contra-exemplo e uma forma que NAO e Ultra Instinto, carregando, e la a chama TEM que estar la.
	/// E ele mede o mesmo par de coisas nos dois lados -- `SemFolha` e `Visible` no mesmo node do mesmo
	/// corpo --, entao a diferenca entre os dois passos e um fato medido e nao duas leituras diferentes.
	/// ==================================================================================================
	/// </summary>
	private void AChamaContinuaNasOutrasFormas()
	{
		if (Corpo is not { } corpo) return;

		var carga = corpo.GetNodeOrNull<CargaVisual>("Carga");
		var aura = corpo.GetNodeOrNull<Aura>("Aura");
		var neb = corpo.GetNodeOrNull<NebulosaDaForma>("Nebulosa");

		bool temFolha = carga?.DesenhoDeTeste.SemFolha == false;
		bool desenha = carga?.DesenhoDeTeste.Visible == true;

		Nota($"  --     [ssj1/carga] Ki em {RazaoDeKi * 100:0}% do tanque (chama da carga: folha="
		   + $"{temFolha}, visivel={desenha}  |  chama da forma visivel="
		   + $"{aura?.DesenhoDeTeste.Visible}  |  nuvem forca={neb?.ForcaDeTeste:0.##} "
		   + $"carga={neb?.CargaDeTeste:0.##})");

		Conferir(temFolha,
				 "em `ssj1` a chama da carga TEM folha -- a folha nao saiu do jogo, saiu da LINHA do "
			   + "Ultra Instinto (e quem responde isso e o simbolo do Core, nao um `if` por forma no "
			   + "cliente)");
		Conferir(desenha,
				 "e ela ACENDE ao segurar o C -- este e o contra-exemplo das linhas que cobram a chama "
			   + "APAGADA em Ultra Instinto: sem ele, apagar a chama do jogo inteiro passaria verde");
		Conferir(neb?.ForcaDeTeste == 0f && neb?.CargaDeTeste == 0f,
				 $"e a nuvem continua apagada nos DOIS papeis (forca={neb?.ForcaDeTeste:0.##}, "
			   + $"carga={neb?.CargaDeTeste:0.##}) -- carregar nao acende nebulosa em forma que nao a tem");

		Fotografar("ssj1-carga", corpo);
		_espera = 0.4;
	}

	private void Chapado(bool ligado)
	{
		Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.DiagnosticoChapado(ligado);
		Nota($"  --     modo diagnostico (mascara chapada) {(ligado ? "LIGADO" : "desligado")}");
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
	/// A REGRA, ESCRITA AQUI E NAO IMPORTADA: a caixa do alfa dos quadros PARADOS da folha, e o lado que
	/// aquele QUADRO manda pro quad (a moldura mais os 5 px do dono, em cada lado).
	///
	/// ============================ POR QUE A BANCADA REFAZ A CONTA EM VEZ DE LER O RESULTADO ============================
	/// A bancada ja lia a folga (`FolgaDeTeste`) e ja media a folga NA FOTO. Nenhuma das duas responde a
	/// pergunta que o pedido do dono realmente faz -- *"no MAXIMO 5 pixels de distancia do corpo"* e uma
	/// DISTANCIA, e distancia precisa de um corpo. Um `_lado = 42` cravado no `.cs` passaria pelas duas: a
	/// folga sai do campo de distancia (e continuaria de 5 px), e a foto de HOJE mostraria o tamanho certo
	/// -- porque 42 e, hoje, o numero certo. Ele so estaria errado na PROXIMA folha, e o jogo tem folhas de
	/// 96 (o Oozaru) e trocas de corpo por forma (o SSJ4).
	///
	/// ============================ E O QUE ELA MEDE MUDOU JUNTO COM A GEOMETRIA ============================
	/// A elipse morreu (o dono: *"deveria n ser um circulo e sim contornar o corpo"*) e com ela morreu a
	/// conta antiga -- `rx = floor(meiaLargura + 5)`, `lado = 2 x max(rx, ry)`, que fazia o TAMANHO DO QUAD
	/// ser a fonte da folga. Agora sao duas coisas separadas, e por isso esta funcao devolve duas:
	///
	///   * `Silhueta` -- a caixa do alfa da folha do CORPO. Ela deixou de ser a mascara e virou um PISO: a
	///     mascara de verdade e a uniao das camadas VIVAS, entao ela tem que CONTER esta caixa (o cabelo e
	///     o rabo so aumentam). Cobrar igualdade daria vermelho num efeito certo.
	///   * `Lado` -- o quad, que agora nao mede silhueta nenhuma: e a moldura do quadro (32, ou 96 no
	///     macaco) mais 5 px de cada lado. Estavel na animacao inteira, que e o que impede a nuvem de
	///     pulsar de tamanho quando o boneco estica o braco.
	/// ============================================================================================================
	/// </summary>
	private static (int Lado, Vector2 Silhueta, float LadoDoQuadro)? RegraDaFolga(SpriteFrames? folha)
	{
		if (folha == null) return null;

		// OS QUADROS PARADOS, e nao o que esta tocando: as folhas base tem poses de alfa CHEIO (o `sp` e
		// as quatro do `take_off` sao 32x32 opacas) e medir uma delas devolveria a caixa do QUADRO com
		// outro nome, e o que se quer aqui e o DESENHO. (A mascara de verdade nao passa mais por esta
		// escolha: ela compoe o quadro VIVO, e a caixa do alfa que esta funcao devolve virou o PISO que
		// aquela composicao tem que conter.)
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

		return (LadoPelaRegra(largura), new Vector2(meiaLargura, meiaAltura), largura);
	}

	/// <summary>
	/// O LADO DO QUAD QUE UM QUADRO DESTE TAMANHO MANDA: a moldura mais <see cref="FolgaPedida"/> de cada
	/// lado, arredondada pra cima e mantida PAR (o C# posiciona o quad em `-lado/2`, e meio pixel nao
	/// sobrevive ao `snap_2d_vertices_to_pixel` do projeto).
	///
	/// Escrita AQUI, e nao lida do node: e a unica linha desta secao que diz o que o tamanho DEVIA ser, e
	/// e ela que separa "derivado da folha" de "42 digitado num `.cs`". Ela vale pra os dois lados da
	/// checagem -- a folha que o boneco esta vestindo e a do Oozaru, lida do disco.
	/// </summary>
	private static int LadoPelaRegra(float ladoDoQuadro) =>
		2 * Mathf.Max(Mathf.CeilToInt(ladoDoQuadro * 0.5f + FolgaPedida), 2);

	/// <summary>
	/// A FOLGA CABE, TEM CHAO, E O QUAD NAO SOBRA. Recebe os quatro numeros de fora de proposito: e a mesma
	/// funcao que julga o efeito de verdade e os tres defeitos injetados.
	///
	/// TRES PERGUNTAS, e cada uma fecha um jeito diferente de a regra morrer:
	///   * TETO   -- folga &lt;= 5. E o pedido, literal.
	///   * CHAO   -- folga &gt;= 4. Sem ele, encolher a nuvem pra DENTRO do boneco (ou pra nada) passaria
	///               verde, porque 0 tambem e menor que 5. E como a folga virou UM numero (a maior
	///               distancia entre nuvem e corpo), o mesmo numero responde os dois: o teto diz que ela
	///               nao passa de 5 em lugar nenhum, o chao diz que ela CHEGA a 5 em algum lugar.
	///   * SOBRA  -- o lado do quad e exatamente o que a moldura do quadro manda (`LadoPelaRegra`). Esta e
	///               a que faltava: a folga e uma DISTANCIA, e ela mede 5 px num quad de qualquer tamanho
	///               -- o quad de 96 px que o dono reclamou passaria pelas outras duas sem uma queixa.
	///
	/// ============================ A FOLGA MUDOU DE FONTE E DE FORMA, E ISSO E O PONTO DA TAREFA ============================
	/// Ela era a diferenca entre DUAS ELIPSES lidas dos uniforms, e vinha por EIXO. Elipse tem folga exata
	/// em quatro pontos e sobra nas diagonais, entao aquele par era o melhor caso e nao o pior -- e ele so
	/// existia porque elipse tem dois raios.
	///
	/// Distancia perpendicular nao tem eixo: agora ela e UM numero, a maior distancia entre um pixel de
	/// nuvem e o boneco, recalculada por forca bruta em cima do campo que o shader recebeu (ver
	/// `NebulosaDaForma.FolgaDeTeste`, que conta por que a medida por eixo mentia -- ela dava 8 px de lado
	/// num campo correto).
	///
	/// A TOLERANCIA E DE UM QUARTO DE PIXEL e nao dos 0,01 de antes: o campo mora numa grade de pixels e a
	/// distancia e medida de centro a centro, entao o ultimo texel aceso da banda pode cair uma fracao de
	/// pixel alem dos 5 -- e "5,0" e "5,2" sao o MESMO pixel na tela. Apertar isso ate 0,01 seria cobrar do
	/// instrumento uma precisao que a grade nao tem.
	/// ==========================================================================================================
	/// </summary>
	private static (bool Ok, string Laudo) AFolgaCabe(float lado, float folga, Vector2 silhueta,
													  float ladoDoQuadro)
	{
		bool teto = folga <= FolgaPedida + 0.26f;
		bool chao = folga >= FolgaPedida - 1.01f;
		bool semSobra = Mathf.Abs(lado - LadoPelaRegra(ladoDoQuadro)) < 0.01f;

		return (teto && chao && semSobra,
				$"folga de {folga:0.##} px de mundo, medida do pixel de nuvem mais LONGE do boneco "
			  + $"[teto {teto}, chao {chao}]  |  quad de {lado:0} px contra os "
			  + $"{LadoPelaRegra(ladoDoQuadro)} que a moldura de {ladoDoQuadro:0} manda "
			  + $"[sem sobra {semSobra}]  |  silhueta viva {silhueta.X:0.##}x{silhueta.Y:0.##}");
	}

	/// <summary>
	/// ============================ A FOLGA E DERIVADA DO BONECO, E A NUVEM VESTE ELE ============================
	/// Tres requisitos, e eles nao se provam juntos: **cabe em 5 px** (um teto), **sai do corpo** (uma
	/// derivacao) e **nao e um circulo** (uma forma). O terceiro entrou agora, e ele e o pedido novo do
	/// dono -- *"a aura em si ta estranha, ela deveria n ser um circulo e sim CONTORNAR O CORPO"*.
	///
	/// E ELE PRECISA DE LINHA PROPRIA JUSTAMENTE PORQUE OS OUTROS DOIS JA ESTAVAM VERDES. A elipse tinha o
	/// tamanho certo e a folga certa; ela errava so a forma, e nenhuma medida de tamanho ve isso. Quem ve e
	/// o <see cref="NebulosaDaForma.PreenchimentoDeTeste"/>: elipse enche `pi/4` = 78,5% da propria caixa
	/// por definicao, e um lutador em pe enche bem menos (o vao entre as pernas, o ar entre o braco e o
	/// tronco, as quinas vazias).
	///
	/// AS QUATRO METADES:
	///   1. na folha que o boneco esta vestindo AGORA, o node chegou no mesmo lado que a moldura manda -- um
	///      lado cravado reprova aqui no dia em que a arte mudar, e reprova HOJE se o numero cravado nao for
	///      por acaso o certo;
	///   2. a silhueta que o campo guarda CONTEM a caixa do alfa da folha do corpo (ela e a uniao das
	///      camadas vivas, entao o cabelo e o rabo so aumentam) e nao passa da moldura;
	///   3. a mascara nao e um circulo;
	///   4. numa folha DE OUTRO TAMANHO (a do Oozaru, 96x96, carregada do disco), a MESMA regra da um quad
	///      muito maior -- ou seja a regra e uma regra, e nao uma calibragem pra folha de 32.
	///
	/// E os tres defeitos injetados fecham os tres jeitos de o teto virar enfeite. Ver
	/// <see cref="AFolgaCabe"/>.
	/// ======================================================================================================
	/// </summary>
	private void AFolgaEDerivada()
	{
		Nota("== A FOLGA E DERIVADA DO BONECO, E A NUVEM VESTE A SILHUETA ==");

		if (Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is not { } neb) return;
		SpriteFrames? folha = Corpo.GetNodeOrNull<CharacterVisual>("Visual")?.FolhaDoCorpo;

		Conferir(folha != null, "a folha do CORPO esta acessivel pra a bancada remedir o alfa dela");
		if (RegraDaFolga(folha) is not { } r)
		{
			Conferir(false, "a bancada conseguiu remedir a silhueta da folha (sem isso a derivacao nao e "
						  + "verificavel -- so o teto seria)");
			return;
		}

		// --- 1. O QUE O SHADER RECEBEU, JULGADO PELAS TRES PERGUNTAS ---
		(bool ok, string laudo) = AFolgaCabe(neb.LadoDeTeste, neb.FolgaDeTeste, neb.SilhuetaDeTeste,
											 r.LadoDoQuadro);
		Conferir(ok, "a nuvem cola no boneco: " + laudo);

		// --- 2. E A SILHUETA SAIU DA ARTE ---
		Nota($"  --     a ARTE manda: lado {r.Lado} px, caixa do alfa do CORPO {r.Silhueta.X:0.##}x"
		   + $"{r.Silhueta.Y:0.##} num quadro de {r.LadoDoQuadro:0}  |  o CAMPO guarda: lado "
		   + $"{neb.LadoDeTeste:0}, silhueta viva {neb.SilhuetaDeTeste.X:0.##}x"
		   + $"{neb.SilhuetaDeTeste.Y:0.##}, preenchimento {neb.PreenchimentoDeTeste:0.###}");

		Conferir(neb.SilhuetaDeTeste.X >= r.Silhueta.X - 0.01f
			  && neb.SilhuetaDeTeste.Y >= r.Silhueta.Y - 0.01f,
				 "a silhueta que o campo guarda CONTEM a caixa do alfa da folha do corpo -- ela e a uniao "
			   + "das camadas vivas, e cabelo e rabo so podem aumentar");
		Conferir(neb.SilhuetaDeTeste.X <= r.LadoDoQuadro * 0.5f + 0.01f
			  && neb.SilhuetaDeTeste.Y <= r.LadoDoQuadro * 0.5f + 0.01f,
				 "...e nao passa da MOLDURA do quadro -- se passasse, o que foi composto nao era o boneco "
			   + "(a colada de aura enche o quadro inteiro, e e por isso que ela fica de fora)");
		Conferir(Mathf.Abs(neb.LadoDeTeste - r.Lado) < 0.01f,
				 $"e o lado do quad e DERIVADO da moldura ({neb.LadoDeTeste:0} contra os {r.Lado} que a "
			   + "arte manda) -- nao ha tamanho escrito em lugar nenhum");

		// E A SILHUETA NAO E A CAIXA DO QUADRO. Esta linha separa "mediu o alfa" de "mediu a moldura", que
		// e o defeito menor da mesma familia: contar os 5 px da borda do QUADRO daria 13 px de folga do
		// BONECO nos lados. O quadro do corpo e 32; a silhueta em pe ocupa ~16 de largura util.
		Conferir(r.Silhueta.X < r.LadoDoQuadro * 0.5f - 0.5f,
				 $"a caixa do alfa medida ({r.Silhueta.X:0.##}) e MENOR que a meia-largura do quadro "
			   + $"({r.LadoDoQuadro * 0.5f:0.#}) -- ou seja o que se mediu foi o desenho, e nao a moldura");

		// --- 3. E NAO E UM CIRCULO ---
		// ============================ O PEDIDO NOVO DO DONO, EM NUMERO ============================
		// O teto de 0,70 fica ENTRE as duas geometrias e nao encostado em nenhuma: a elipse enche 0,785
		// por definicao (e um numero exato, nao uma medida), e o boneco em pe enche ~0,5. A margem existe
		// porque a mascara nao e so o boneco -- ela tem a folga de 5 px em volta, que ARREDONDA os cantos e
		// tapa parte do vao entre as pernas. O que ela nao faz e chegar a 0,785: pra isso a silhueta
		// precisaria ser convexa, e lutador nenhum e.
		// ======================================================================================
		float cheio = neb.PreenchimentoDeTeste;
		Conferir(cheio > 0.05f && cheio < 0.70f,
				 $"a mascara CONTORNA o boneco em vez de o enquadrar: ela enche {cheio:0.###} da propria "
			   + "caixa, e uma elipse encheria 0,785 por definicao -- foi esta a queixa do dono, e nenhuma "
			   + "medida de tamanho a enxerga");

		// --- 4. A MESMA REGRA NUMA FOLHA DE OUTRO TAMANHO ---
		// ============================ POR QUE A DO OOZARU, E POR QUE DO DISCO ============================
		// A afirmacao do `NebulosaDaForma.Folga` e que "o corpo de 32 produz um quad de 42; o Oozaru, de
		// folha 96, produziria um de ~106 -- com os mesmos 5 px em volta, sem ninguem recalcular nada". Isso
		// e uma promessa sobre uma folha que este corpo nao esta vestindo, e ela nunca foi conferida.
		//
		// LER A FOLHA DO DISCO em vez de FORCAR O OOZARU e escolha de custo: a cinematica do macaco e sempre
		// de ESTREIA (`Cinematicas.Degrau`: a linha Oozaru nunca dispensa cena) e prenderia esta bancada por
		// mais de um minuto pra medir uma conta que nao depende de haver macaco nenhum. O que se cobra aqui e
		// a REGRA; que o node a aplique de novo quando a folha troca ja esta coberto pelo item 1, no corpo
		// que esta na tela.
		//
		// E SO O LADO E COBRADO AQUI, e nao a folga: a folga agora e MEDIDA no campo, e nao ha campo de uma
		// folha que ninguem esta vestindo. Cobrar a folga de um macaco exigiria construir um -- o que e o
		// minuto de cinematica que este bloco existe pra nao pagar.
		// ==========================================================================================
		var outra = GD.Load<SpriteFrames>("res://Assets/Sprites/Character Icons/Transformations/oozaruhayate.tres");
		if (RegraDaFolga(outra) is { } o)
		{
			Conferir(o.Lado == LadoPelaRegra(o.LadoDoQuadro) && o.LadoDoQuadro > r.LadoDoQuadro,
					 $"a MESMA regra numa folha de {o.LadoDoQuadro:0} (a do Oozaru) da um quad de {o.Lado} px");
			Conferir(o.Lado > r.Lado + 20,
					 $"e ele e MUITO maior que o do corpo normal ({o.Lado} contra {r.Lado} px) -- a regra "
				   + "acompanha a folha, o que um numero cravado nao faria");
		}
		else Conferir(false, "a folha do Oozaru carregou e deu pra remedir (a segunda folha e o que prova "
						   + "que a regra e regra, e nao calibragem pra folha de 32)");

		// --- 5. E O JULGAMENTO ENXERGA? OS TRES DEFEITOS INJETADOS ---
		// Sem estas tres linhas, `AFolgaCabe` podia estar devolvendo `true` por qualquer motivo -- inclusive
		// por sempre devolver `true` -- e as metades acima seriam verde vazio.
		Nota("  --     agora os DEFEITOS INJETADOS: as tres linhas abaixo tem que dizer que reprovaram");
		float folga = neb.FolgaDeTeste;
		Vector2 sil = neb.SilhuetaDeTeste;

		Conferir(!AFolgaCabe(96f, folga, sil, r.LadoDoQuadro).Ok,
				 "[injetado] o QUAD DE 96 px do defeito original reprova (a folga cabe, o quad SOBRA) -- "
			   + AFolgaCabe(96f, folga, sil, r.LadoDoQuadro).Laudo);
		Conferir(!AFolgaCabe(neb.LadoDeTeste, folga + 15f, sil, r.LadoDoQuadro).Ok,
				 "[injetado] 20 px de folga (a nuvem VOLTANDO a crescer) reprova pelo teto");
		Conferir(!AFolgaCabe(neb.LadoDeTeste, 0f, sil, r.LadoDoQuadro).Ok,
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

		// ONDE O QUAD CAI DENTRO DO RECORTE. Ele e a referencia de "perto do boneco" pro controle logo
		// abaixo, e sai do node em vez de ser suposto centrado -- o `Recorte` desliza perto da beirada
		// do mapa (ver o `Clamp` dele), e supor o centro ja custou um laudo errado nesta bancada.
		Rect2 quadNaTela = Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.RetanguloNaTelaDeTeste
						?? new Rect2(_cantos[nOn], on.GetSize());
		var quadNaFoto = new Rect2(quadNaTela.Position - _cantos[nOn], quadNaTela.Size);

		// A POEIRA SAI ANTES DE QUALQUER CONTA. Ver `SemPoeira`: um unico ponto solto na quina estoura a
		// caixa, que e um extremo. O corte de 24 px de TELA e ~2,7 px de mundo no zoom 3 -- menor que o
		// menor pedaco de personagem que a copa possa deixar inteiro, e maior que qualquer respingo.
		bool[] mEfeito = SemPoeira(OndeDiscordam(on, off), on.GetWidth(), 24, out int poeiraEfeito);
		bool[] mCorpo = SemPoeira(OndeDiscordam(off, sem), on.GetWidth(), 24, out int poeiraCorpo);
		Rect2I? efeito = Caixa(mEfeito, on.GetWidth(), out int pxEfeito);
		Rect2I? corpo = Caixa(mCorpo, on.GetWidth(), out int pxCorpo);

		// ============================ O CENARIO FICOU PARADO? ESTE E O CONTROLE QUE FALTAVA ============================
		// As tres fotos sao quadros CONSECUTIVOS de uma cena que se supoe parada, e essa suposicao nunca
		// tinha sido conferida. Ela e falsa quando ha qualquer coisa animada na tela -- e ela FOI falsa
		// por uma rodada inteira, com o veu do clima derivando (ver o bloco que o apaga, no passo 0).
		//
		// A medida e direta e nao custa foto nenhuma: LONGE do boneco, esconder o boneco nao pode mudar
		// pixel nenhum. Tudo que discordar ali e coisa que se mexeu sozinha entre um quadro e o outro --
		// e se for muito, nada abaixo desta linha esta medindo o efeito.
		//
		// E ELA VEM ANTES DAS OUTRAS de proposito: quem ler o log com esta linha vermelha sabe que as
		// folgas de baixo sao ruido, em vez de sair caçando um defeito de geometria que nao existe.
		// ==========================================================================================================
		int fora = 0, longe = Mathf.RoundToInt(Mathf.Max(quadNaFoto.Size.X, quadNaFoto.Size.Y) * 0.9f);
		Vector2 meio = quadNaFoto.Position + quadNaFoto.Size / 2f;
		bool[] churn = OndeDiscordam(off, sem);
		for (int i = 0; i < churn.Length; i++)
		{
			if (!churn[i]) continue;
			int x = i % on.GetWidth(), y = i / on.GetWidth();
			if (Mathf.Abs(x - meio.X) > longe || Mathf.Abs(y - meio.Y) > longe) fora++;
		}

		Conferir(fora < 500,
				 $"[{onde}] o CENARIO ficou parado entre as fotos: longe do boneco (alem de {longe} px de "
			   + $"tela) as duas so discordam em {fora} px. Muito acima disso e coisa animada na tela, e a "
			   + "subtracao passa a medir ela em vez do efeito");

		// ============================ E O FILTRO NAO PODE VIRAR A MEDIDA ============================
		// Um corte por tamanho que apagasse metade do personagem estaria ESCOLHENDO o que medir, e as
		// folgas abaixo passariam a descrever o que sobrou do filtro em vez do que esta na tela. Entao
		// ele diz quanto tirou, e o quanto tem que ser pouco.
		// ======================================================================================
		Nota($"  --     [{onde}] a poeira tirou {poeiraEfeito} px soltos do efeito e {poeiraCorpo} do "
		   + $"personagem (sobraram {pxEfeito} e {pxCorpo})");
		Conferir(poeiraEfeito * 4 < pxEfeito && poeiraCorpo * 4 < pxCorpo,
				 $"[{onde}] o filtro de poeira tirou POUCO ({poeiraEfeito} e {poeiraCorpo} px soltos) -- "
			   + "ele limpa respingo, e nao escolhe o que a bancada vai medir");
		_pixelsDoEfeito[onde] = pxEfeito;
		_pixelsDoCorpo[onde] = pxCorpo;

		Conferir(efeito != null, $"[{onde}] a subtracao ligado/desligado achou PIXEL do efeito na tela");
		Conferir(corpo != null, $"[{onde}] a subtracao com/sem boneco achou PIXEL do personagem na tela");
		if (efeito is not { } e || corpo is not { } c) return;

		// EMBAIXO DA COPA O CHAO DA FOLGA NAO VALE, e nao e frouxidao: a folhagem RECORTA a nuvem, entao
		// a caixa dela encolhe pra dentro da do boneco por um motivo legitimo -- e e justamente isso que
		// o bloco da copa cobra logo depois. O mesmo campo que diz "isto e campo aberto" governa os dois.
		bool cobrarChao = cobrarCobertura;

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
			(bool okDoLado, string laudoDoLado) = AFolgaDeUmLado(lado, tela, zoom, cobrarChao);
			Conferir(okDoLado, $"[{onde}] " + laudoDoLado);
		}

		// ============================ E O JULGAMENTO DE LADO ENXERGA? O DEFEITO INJETADO ============================
		// As quatro linhas de cima sao a MESMA funcao rodada quatro vezes. Ela tem que reprovar pelos
		// dois lados -- e o defeito de baixo e o que um teto sozinho jamais pegaria: a nuvem encolhida
		// PRA DENTRO do boneco da folga negativa, e negativo tambem e menor que cinco.
		// ========================================================================================================
		if (cobrarChao)
		{
			Nota("  --     agora os DEFEITOS INJETADOS: as duas linhas abaixo tem que dizer que reprovaram");
			Conferir(!AFolgaDeUmLado("encolhida", -2 * zoom, zoom, true).Ok,
					 "[injetado] a nuvem encolhida 2 px PRA DENTRO do boneco reprova pelo CHAO -- "
				   + AFolgaDeUmLado("encolhida", -2 * zoom, zoom, true).Laudo);
			Conferir(!AFolgaDeUmLado("inchada", 20 * zoom, zoom, true).Ok,
					 "[injetado] 20 px de folga (a nuvem voltando a crescer) reprova pelo TETO -- "
				   + AFolgaDeUmLado("inchada", 20 * zoom, zoom, true).Laudo);
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

		// ============================ E A FORMA, QUE E O PEDIDO NOVO ============================
		// As duas mascaras que a subtracao acabou de produzir sao a unica chance desta bancada de
		// comparar a FORMA da nuvem com a do boneco que estava embaixo dela -- as duas do mesmo quadro,
		// no mesmo enquadramento, em pixel desenhado. Ver `ANuvemTemCinturaComoOBoneco`.
		//
		// SO EM CAMPO ABERTO (a mesma condicao da cobertura, e pelo mesmo motivo): embaixo da copa
		// sobram fiapos do personagem, e o perfil linha a linha de uns poucos pixels de franja e ruido
		// -- ele acusaria "a nuvem perdeu a cintura" numa arte intacta.
		// ==================================================================================
		if (cobrarCobertura)
			ANuvemTemCinturaComoOBoneco(mEfeito, mCorpo, e, c, on.GetWidth(), zoom, onde);
	}

	/// <summary>
	/// UM LADO DA FOLGA, COM TETO E COM CHAO -- e ela e uma funcao pra poder ser exercitada ao contrario.
	///
	/// ============================ O CHAO E O QUE FALTAVA AQUI ============================
	/// As quatro folgas de tela so tinham TETO (`&lt;= 5 px`), e a rodada que trouxe a poeira da bruma
	/// mostrou o preco disso: com a caixa estourada as quatro mediram 0,0 px de mundo e as quatro
	/// passaram. Zero tambem e menor que cinco -- e um teto que aceita zero nao distingue uma nuvem
	/// colada de uma nuvem que sumiu, nem de uma desenhada ATRAS do corpo.
	///
	/// O CHAO E 2 px E NAO 5, e o motivo e o instrumento: a caixa do personagem inclui a ponta do cabelo
	/// e do rabo (o que APARECE), e a nuvem sai da silhueta, entao do lado do espeto ela chega mais
	/// perto. Cobrar 5 nos quatro lados seria cobrar que o cabelo nao existisse. Dois continua reprovando
	/// qualquer encolhimento que o olho veja -- e reprova, com folga, o negativo.
	///
	/// O TETO GANHOU UM PIXEL DE TELA de tolerancia (`1/zoom`): a caixa e contada em pixel inteiro nos
	/// dois lados, e a franja mais fraca da nuvem pode acender meio pixel adiante. "5,0" e "5,3" sao o
	/// mesmo pixel na tela.
	/// ==================================================================================
	/// </summary>
	private static (bool Ok, string Laudo) AFolgaDeUmLado(string lado, int tela, int zoom, bool cobrarChao)
	{
		double mundo = tela / (double)zoom;
		bool teto = mundo <= FolgaPedida + 1.0 / zoom;
		bool chao = !cobrarChao || mundo >= 2.0;

		return (teto && chao,
				$"folga {lado}: {tela} px de tela = {mundo:0.0} px de mundo (o teto do dono e "
			  + $"{FolgaPedida:0}) [teto {teto}, chao {(cobrarChao ? chao.ToString() : "nao cobrado")}]");
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
	/// TIRA A POEIRA DA MASCARA: apaga toda mancha ligada com menos de <paramref name="minimo"/> pixels.
	///
	/// ============================ E ISTO NAO E COSMETICA: SEM ELE A FOLGA MEDIA ZERO ============================
	/// A subtracao de dois quadros consecutivos foi desenhada supondo que, numa cena parada, o que nao e
	/// efeito nem corpo da diferenca ZERO. Uma rodada mostrou que nao da: com o clima em `Neblina|0.05` a
	/// bruma anda um fio de pixel entre um quadro e o outro, e sobram algumas dezenas de pontos SOLTOS
	/// espalhados pela foto inteira.
	///
	/// Eles nao mudam contagem nenhuma (sao poucos), mas a CAIXA e um extremo -- basta um ponto na quina
	/// pra ela virar a foto toda. Foi o que aconteceu: a caixa do personagem saiu 378x384 (o recorte
	/// inteiro), as quatro folgas sairam 0,0 px de mundo e as quatro passaram, porque zero tambem e
	/// menor que cinco. Um instrumento que responde "0,0 px" pra qualquer coisa nao esta medindo nada.
	///
	/// UM CORTE POR TAMANHO DE MANCHA, e nao pela maior: embaixo da copa o personagem PODE sair partido
	/// em dois (a folhagem corta o meio dele), e ficar so com a maior metade trocaria uma medida errada
	/// por outra. O que se quer apagar e o que nao tem tamanho pra ser nem corpo nem nuvem.
	/// ========================================================================================================
	/// </summary>
	private static bool[] SemPoeira(bool[] m, int largura, int minimo, out int soltos)
	{
		int altura = m.Length / largura;
		var limpa = new bool[m.Length];
		var visto = new bool[m.Length];
		var pilha = new Stack<int>();
		var mancha = new List<int>();
		soltos = 0;

		for (int i = 0; i < m.Length; i++)
		{
			if (!m[i] || visto[i]) continue;

			mancha.Clear();
			pilha.Push(i);
			visto[i] = true;

			while (pilha.Count > 0)
			{
				int p = pilha.Pop();
				mancha.Add(p);
				int x = p % largura, y = p / largura;

				// QUATRO VIZINHOS e nao oito: com oito, duas manchas que so se tocam na DIAGONAL viram
				// uma -- e uma fileira de pontos de bruma na diagonal somaria o bastante pra sobreviver
				// ao corte. Quatro e o vizinho que compartilha lado, que e o que "estar ligado" quer
				// dizer numa imagem.
				if (x > 0 && m[p - 1] && !visto[p - 1]) { visto[p - 1] = true; pilha.Push(p - 1); }
				if (x < largura - 1 && m[p + 1] && !visto[p + 1]) { visto[p + 1] = true; pilha.Push(p + 1); }
				if (y > 0 && m[p - largura] && !visto[p - largura]) { visto[p - largura] = true; pilha.Push(p - largura); }
				if (y < altura - 1 && m[p + largura] && !visto[p + largura]) { visto[p + largura] = true; pilha.Push(p + largura); }
			}

			if (mancha.Count < minimo) { soltos += mancha.Count; continue; }
			foreach (int p in mancha) limpa[p] = true;
		}

		return limpa;
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

	// =====================================================================
	// A NUVEM TEM CINTURA ONDE O BONECO TEM -- e uma elipse nao tem nenhuma
	// =====================================================================
	/// <summary>
	/// QUANTOS PIXELS DE MASCARA HA EM CADA LINHA, dentro da faixa de linhas pedida.
	///
	/// ============================ CONTAGEM, E NAO LARGURA ============================
	/// A tentacao e medir a largura (o maior x menos o menor) e ela nao serve: o vao entre as pernas e o
	/// ar entre o braco e o tronco nao mudam a largura da linha, so o quanto dela esta PREENCHIDO. Um
	/// boneco em pe e uma elipse do mesmo tamanho tem quase a mesma largura linha a linha -- e e por isso
	/// que todas as outras medidas desta bancada davam verde com a elipse.
	/// ==========================================================================
	/// </summary>
	private static int[] PerfilPorLinha(bool[] mascara, int largura, int y0, int altura)
	{
		var perfil = new int[altura];
		for (int i = 0; i < altura; i++)
		{
			int y = y0 + i, n = 0;
			for (int x = 0; x < largura; x++)
				if (mascara[y * largura + x]) n++;
			perfil[i] = n;
		}
		return perfil;
	}

	/// <summary>
	/// A CINTURA DE UM PERFIL: a linha mais AFUNDADA entre duas mais cheias, e o quanto ela afunda.
	///
	/// A conta e a proeminencia de um vale -- pra cada linha, o menor dos dois picos (o de cima e o de
	/// baixo) menos ela mesma. Ela vale ZERO, por construcao, em qualquer perfil que so sobe e depois so
	/// desce: e exatamente o caso da ELIPSE, e e por isso que esta funcao separa as duas geometrias sem
	/// precisar de limiar calibrado. Um boneco em pe tem pelo menos uma (a cintura, o vao entre as
	/// pernas, ou o ar entre o braco e o tronco).
	///
	/// AS PONTAS FICAM DE FORA (10% de cada lado): a primeira e a ultima linha de qualquer mascara valem
	/// quase zero, e sem a margem toda silhueta teria "cintura" no pescoco por causa do topo da cabeca.
	/// </summary>
	private static (int Linha, int Fundo) Cintura(int[] perfil)
	{
		int n = perfil.Length, margem = Mathf.Max(n / 10, 1);
		int melhorLinha = -1, melhorFundo = 0;

		for (int i = margem; i < n - margem; i++)
		{
			int acima = 0, abaixo = 0;
			for (int j = 0; j < i; j++) acima = Mathf.Max(acima, perfil[j]);
			for (int j = i + 1; j < n; j++) abaixo = Mathf.Max(abaixo, perfil[j]);

			int fundo = Mathf.Min(acima, abaixo) - perfil[i];
			if (fundo > melhorFundo) { melhorFundo = fundo; melhorLinha = i; }
		}

		return (melhorLinha, melhorFundo);
	}

	/// <summary>Correlacao de Pearson entre dois perfis do mesmo tamanho. Quanto duas formas concordam.</summary>
	private static double Correlacao(int[] a, int[] b)
	{
		int n = Mathf.Min(a.Length, b.Length);
		if (n < 4) return double.NaN;

		double ma = 0, mb = 0;
		for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
		ma /= n; mb /= n;

		double sab = 0, sa = 0, sb = 0;
		for (int i = 0; i < n; i++)
		{
			double da = a[i] - ma, db = b[i] - mb;
			sab += da * db; sa += da * da; sb += db * db;
		}
		return sa <= 0 || sb <= 0 ? double.NaN : sab / Math.Sqrt(sa * sb);
	}

	/// <summary>
	/// O PERFIL DE UMA ELIPSE inscrita numa caixa de `altura` linhas e `largura` de cheio no meio.
	///
	/// Ela e o DEFEITO INJETADO desta familia, e ela nao e uma invencao: e a geometria que este efeito
	/// TINHA, e que o dono reprovou com todas as letras (*"ela deveria n ser um circulo e sim contornar o
	/// corpo"*). Rodar a mesma funcao de julgamento nela e a unica forma de mostrar que a linha de cima
	/// enxerga a diferenca -- todas as outras medidas desta bancada davam verde nos dois casos.
	/// </summary>
	private static int[] PerfilDeElipse(int altura, int largura)
	{
		var p = new int[altura];
		for (int i = 0; i < altura; i++)
		{
			double t = (i + 0.5) / altura * 2.0 - 1.0;          // -1 no topo, +1 na base
			p[i] = (int)Math.Round(largura * Math.Sqrt(Math.Max(1.0 - t * t, 0.0)));
		}
		return p;
	}

	/// <summary>
	/// A NUVEM SEGUE A SILHUETA? Recebe os dois perfis de fora de proposito: e a MESMA funcao que julga o
	/// efeito de verdade e a elipse injetada.
	///
	/// DUAS PERGUNTAS, e nenhuma delas e sobre tamanho:
	///   * A NUVEM TEM CINTURA -- um vale de verdade no perfil dela. Elipse tem zero, por definicao.
	///   * E ELA CAI ONDE A DO BONECO CAI -- senao "tem um vale em algum lugar" seria compativel com uma
	///     mascara de qualquer forma, inclusive uma ampulheta de cabeca pra baixo.
	///
	/// A TOLERANCIA E DE 5 px DE MUNDO e ela nao e frouxidao: a nuvem tem 5 px de folga em volta, e a
	/// folga ARREDONDA o vale -- o fundo dela escorrega uma ou duas linhas em relacao ao do boneco. Meio
	/// corpo continua reprovando qualquer mascara que nao seja aquele boneco.
	/// </summary>
	private static (bool Ok, string Laudo) ACinturaBate(int[] nuvem, int[] corpo, int zoom, string quem)
	{
		(int linhaN, int fundoN) = Cintura(nuvem);
		(int linhaC, int fundoC) = Cintura(corpo);

		bool temCintura = fundoN >= zoom;                                     // >= 1 px de mundo de fundo
		bool noLugar = linhaN >= 0 && linhaC >= 0
					&& Mathf.Abs(linhaN - linhaC) <= 5 * zoom;
		double r = Correlacao(nuvem, corpo);

		return (temCintura && noLugar,
				$"[{quem}] a cintura da mascara afunda {fundoN} px de tela na linha {linhaN}, e a do "
			  + $"personagem afunda {fundoC} na linha {linhaC} [tem cintura {temCintura}, no lugar "
			  + $"{noLugar}]  |  correlacao dos dois perfis r={r:0.000}");
	}

	/// <summary>
	/// ============================ A AFIRMACAO HONESTA DA FORMA ============================
	/// O `PreenchimentoDeTeste` ja diz que a mascara nao enche a propria caixa como uma elipse encheria.
	/// Isso e um numero AGREGADO: ele reprova a elipse, mas passaria numa mascara de qualquer forma
	/// esburacada -- um losango, um X, a silhueta de OUTRO boneco.
	///
	/// Esta compara as duas FORMAS linha a linha, e as duas saem da MESMA foto (as subtracoes que a
	/// `AFolgaNaImagem` ja fez): o perfil da nuvem contra o perfil do personagem que estava embaixo dela
	/// naquele quadro. A frase que ela permite dizer e a que o dono pediu -- **a nuvem tem cintura onde o
	/// boneco tem** --, e nao "a nuvem nao e redonda".
	/// ==================================================================================
	/// </summary>
	private void ANuvemTemCinturaComoOBoneco(bool[] mEfeito, bool[] mCorpo, Rect2I efeito, Rect2I corpo,
											 int largura, int zoom, string onde)
	{
		// AS LINHAS SAO AS DO PERSONAGEM, e nao as da nuvem: a nuvem e o boneco mais 5 px de folga em
		// cima e embaixo, e comparar dois perfis de alturas diferentes exigiria reamostrar um deles --
		// que e inventar dado. Nas linhas do boneco os dois existem, e a comparacao e direta.
		int[] pNuvem = PerfilPorLinha(mEfeito, largura, corpo.Position.Y, corpo.Size.Y);
		int[] pCorpo = PerfilPorLinha(mCorpo, largura, corpo.Position.Y, corpo.Size.Y);

		// ============================ O CONTROLE VEM PRIMEIRO: O BONECO TEM CINTURA? ============================
		// Se o personagem daquele quadro nao tivesse vale nenhum (um boneco de bracos colados e pernas
		// juntas, ou uma pose de voo), a pergunta de baixo seria vazia e passaria verde por nao ter o que
		// comparar. Entao ela e cobrada ANTES, e no proprio personagem.
		// ==================================================================================================
		(int linhaC, int fundoC) = Cintura(pCorpo);
		Conferir(fundoC >= zoom,
				 $"[{onde}] o PERSONAGEM daquele quadro tem mesmo uma cintura pra comparar (afunda "
			   + $"{fundoC} px de tela na linha {linhaC} de {pCorpo.Length}) -- sem isso a linha abaixo "
			   + "nao teria o que casar e passaria vazia");

		(bool ok, string laudo) = ACinturaBate(pNuvem, pCorpo, zoom, "nuvem");
		Conferir(ok, "a nuvem SEGUE A SILHUETA e nao a enquadra: " + laudo);

		// --- O DEFEITO INJETADO: a elipse que este efeito ERA ---
		// Ela sai da MESMA caixa do efeito de verdade (mesma altura, mesmo cheio no meio), entao a unica
		// coisa que muda entre as duas linhas e a FORMA. Se a de cima passasse por tamanho, esta passaria
		// junto.
		int cheio = 0;
		foreach (int v in pNuvem) cheio = Mathf.Max(cheio, v);
		int[] pElipse = PerfilDeElipse(pCorpo.Length, cheio);

		Nota($"  --     [{onde}] caixa do efeito {efeito.Size.X}x{efeito.Size.Y} px de tela; perfil "
		   + $"comparado nas {pCorpo.Length} linhas do personagem, zoom {zoom}x");
		Nota("  --     agora o DEFEITO INJETADO: a linha abaixo tem que dizer que reprovou");
		Conferir(!ACinturaBate(pElipse, pCorpo, zoom, "elipse").Ok,
				 "[injetado] a ELIPSE que este efeito era -- mesma caixa, mesma largura, so a forma "
			   + "diferente -- REPROVA nesta mesma linha: "
			   + ACinturaBate(pElipse, pCorpo, zoom, "elipse").Laudo);
	}

	/// <inheritdoc cref="OCenarioTapaANuvem"/>
	private readonly Dictionary<string, int> _pixelsDoEfeito = [], _pixelsDoCorpo = [];

	/// <inheritdoc cref="AFolgaNaImagem"/>
	private readonly Dictionary<string, Image> _cortes = [];

	/// <inheritdoc cref="AFolgaNaImagem"/>
	private readonly Dictionary<string, Vector2I> _cantos = [];

	private void Alternar(bool ligado)
	{
		// A PALETA E A DA FORMA QUE O CORPO ESTA VESTINDO -- ver `_ultimaFormaForcada`. Reacender com a
		// paleta errada nao reprovaria nada aqui (a subtracao mede ONDE o efeito desenha, nao de que
		// cor), e por isso mesmo passaria despercebida ate alguem olhar a foto.
		Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa")?.Definir(
			ligado ? Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(
						 Jandirus.Core.Forms.Catalogo.Def(_ultimaFormaForcada))
				   : null);
		Nota($"  --     par de subtracao: nuvem {(ligado ? "LIGADA" : "DESLIGADA")}");
		// ZERO: o proximo `_Process` e o proximo QUADRO. Ver o comentario no roteiro -- qualquer folga
		// aqui deixa o cenario se mexer e suja a subtracao.
		_espera = 0;
	}

	// =====================================================================
	// A MASCARA ACOMPANHA A ANIMACAO -- e o quad NAO pulsa
	// =====================================================================
	/// <summary>As chaves de pose vistas enquanto o robo anda. Ver <see cref="AMascaraSegueAAnimacao"/>.</summary>
	private readonly HashSet<long> _posesVistas = [];

	/// <inheritdoc cref="AMascaraSegueAAnimacao"/>
	private int _voltasDaPose;

	/// <inheritdoc cref="AMascaraSegueAAnimacao"/>
	private float _ladoMinimo = float.MaxValue, _ladoMaximo = float.MinValue;

	/// <summary>
	/// ============================ AS DUAS METADES DE "SEGUIR A ANIMACAO" ============================
	/// O robo anda por ~40 quadros e amostra DUAS coisas em cada um deles. As duas juntas sao o
	/// requisito -- separadas, cada uma passa verde num efeito quebrado:
	///
	///   * A MASCARA MUDA. A chave da pose (`PoseDeTeste`) tem que assumir mais de um valor. Uma mascara
	///     construida uma vez e nunca mais devolveria UMA chave so, e todas as outras medidas desta
	///     bancada continuariam verdes -- a folga seria 5 px do quadro errado.
	///   * O QUAD NAO MUDA. O lado tem que ficar CONSTANTE enquanto isso. Este e o controle da linha de
	///     cima e nao um enfeite: o jeito preguicoso de fazer a mascara acompanhar a animacao e remedir a
	///     caixa do alfa por quadro, e ai o quad muda de tamanho a cada passo -- a nuvem PULSA, e pulsar
	///     e justamente o defeito que a versao anterior evitava medindo a pose parada. As duas afirmacoes
	///     tem que valer ao mesmo tempo, e e por isso que elas moram na mesma funcao.
	///
	/// AMOSTRA POR QUADRO, e nao por prazo: o `_passo--` com `_espera = 0` e o mesmo idioma do
	/// <see cref="EsperarANuvem"/>. Quarenta quadros sao ~0,7 s, e o ciclo de caminhada do corpo tem 4
	/// quadros -- ou seja o boneco passa por todos eles mais de uma vez, com folga pra qualquer taxa de
	/// quadros.
	/// ============================================================================================
	/// </summary>
	private void AMascaraSegueAAnimacao()
	{
		if (Corpo?.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is not { } neb) return;

		Input.ActionPress("move_right");
		_posesVistas.Add(neb.PoseDeTeste);
		_ladoMinimo = Mathf.Min(_ladoMinimo, neb.LadoDeTeste);
		_ladoMaximo = Mathf.Max(_ladoMaximo, neb.LadoDeTeste);

		if (_voltasDaPose++ < 40)
		{
			_passo--;
			_espera = 0;
			return;
		}

		Input.ActionRelease("move_right");
		Nota($"== A MASCARA ACOMPANHA A ANIMACAO ({_voltasDaPose} quadros andando) ==");

		Conferir(_posesVistas.Count >= 2,
				 $"a mascara foi RECONSTRUIDA enquanto o boneco andava ({_posesVistas.Count} pose(s) "
			   + "distinta(s) em 40 quadros) -- uma so quer dizer mascara congelada no quadro em que a "
			   + "forma acendeu, e a folga de 5 px seria do desenho errado");

		Conferir(Mathf.Abs(_ladoMaximo - _ladoMinimo) < 0.01f,
				 $"e o QUAD ficou parado no mesmo tamanho ({_ladoMinimo:0} px do comeco ao fim) -- ele sai "
			   + "da moldura do quadro e nao da caixa do alfa, entao a nuvem nao PULSA de tamanho a cada "
			   + "passo (que e o preco do jeito preguicoso de fazer a linha de cima passar)");

		_espera = 0.8;
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
			int comidosDoCorpo = corpoAberto - corpoArvore;
			int comidosDaNuvem = nuvemAberto - nuvemArvore;
			Conferir(doCorpo > 0.15,
					 $"a copa REALMENTE esta na frente: o boneco perdeu {doCorpo * 100:0}% dos pixels "
				   + $"({corpoAberto} -> {corpoArvore}) -- sem isto a linha de baixo seria verde vazio");

			// ============================ EM PIXEL COMIDO, E NAO EM PORCENTAGEM ============================
			// Isto cobrava `daNuvem > 0,15` -- a mesma FRACAO exigida do boneco -- e uma rodada mostrou
			// que a forma da conta estava errada: a copa comeu 440 px do boneco e 562 px da nuvem (a
			// nuvem perdeu MAIS pixel), e mesmo assim a linha reprovou, porque 562 sobre uma nuvem de
			// 4015 da 14% e 440 sobre um boneco de 1784 da 25%.
			//
			// A fracao nao podia mesmo funcionar: a nuvem e o boneco MAIS 5 px de folga em toda a volta,
			// entao ela sempre tem area que a copa nao alcanca, e dividir pelo total dela dilui o corte.
			// Exigir a mesma fracao dos dois era pedir que a folga nao existisse.
			//
			// EM PIXEL ABSOLUTO A GEOMETRIA FECHA: a nuvem cobre a area do boneco inteira, entao
			// qualquer coisa que apague N pixels do boneco tem que apagar pelo menos N da nuvem -- ou a
			// nuvem esta desenhando por cima daquela coisa. E a checagem fica MAIS forte, nao menos:
			// com `ZIndex` positivo a nuvem perde ~0 px enquanto o boneco perde centenas, e isto cai por
			// uma distancia enorme em vez de por um ponto percentual.
			// ==========================================================================================
			Conferir(comidosDaNuvem >= comidosDoCorpo,
					 $"e a nuvem foi recortada JUNTO: a copa comeu {comidosDaNuvem} px dela contra "
				   + $"{comidosDoCorpo} px do boneco ({nuvemAberto} -> {nuvemArvore}) -- ela cobre a area "
				   + "do boneco inteira, entao tem que perder pelo menos o mesmo tanto; com `ZIndex` "
				   + "acima de 0 ela desenharia por cima da copa e perderia ~0");
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
