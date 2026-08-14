using Godot;
using Jandirus.Core.Forms;

// O APELIDO EXISTE POR CAUSA DE UM CHOQUE DE NOMES DESTE ARQUIVO: a bancada tem um metodo
// `Catalogo()`, e por isso todo acesso ao `Jandirus.Core.Forms.Catalogo` aqui e escrito por
// extenso. O `ModoDoCabelo` nao choca com nada, mas ele aparece 40 vezes na tabela do cabelo
// abaixo -- e 40 linhas de `Jandirus.Core.Forms.ModoDoCabelo.TrocarETingir` esconderiam a tabela
// dentro do proprio prefixo.
using ModoCabelo = Jandirus.Core.Forms.ModoDoCabelo;

namespace Jandirus.Client;

/// <summary>
/// BANCADA DOS EFEITOS DE FORMA (`--diagforma`): raiozinhos, contorno brilhoso e luz da aura.
///
/// ============================ O QUE SO ESTA BANCADA CONSEGUE VER ============================
/// Efeito visual nao mente pouco: ele mente MUITO. Um shader que nao compila deixa o Godot cair
/// pro material padrao e a particula vira um retangulo branco -- que a 32 px de distancia, num
/// corpo dourado, passa por "brilho". Um `INSTANCE_CUSTOM` que nao chega deixa o raio ESTATICO, e
/// raio estatico a 30 quadros por segundo parece raio piscando.
///
/// Entao esta bancada nao pergunta "esta bonito". Ela pergunta:
///   1. os dois shaders COMPILARAM? (o Godot avisa no console e segue em frente -- ninguem morre)
///   2. o node de raios existe no corpo, e ele acende quando a forma acende?
///   3. o volume MUDA por forma? (senao o campo `FormaDef.Raios` esta escrito e nao ligado -- a
///      falha assinatura deste projeto)
///   4. o contorno da forma chegou no material do sprite, e ele NAO e o mesmo canal do impacto?
///   5. a luz da aura esta na cor da forma?
///   6. tudo APAGA ao voltar pra base?
///
/// E tira uma foto, que e a unica parte que responde "esta bonito".
/// ========================================================================================
///
/// COMO RODAR (com janela, pra a foto sair):
///     Godot --path . --host --diagforma --raca Saiyan --nome Brilhante --conta brilhante
/// </summary>
public partial class RoboDeForma : Node
{
	private double _t;
	private int _passo;
	private bool _acabou;

	private readonly List<string> _falhas = [];
	private readonly List<string> _passos = [];

	/// <summary>
	/// ============================ A COR PESSOAL DO SUJEITO DESTA BANCADA ============================
	/// A chama da base (e a do Mistico) deixou de ser uma constante compartilhada: cada personagem
	/// sorteia a propria no nascimento (`Appearance.CorAura`). Entao a pergunta "de que cor e a chama"
	/// passou a ter um segundo argumento, e esta e a resposta que este robo da.
	///
	/// **NAO E o <see cref="Aura.CorDoKiCru"/>, E ESSE E O PONTO.** Enquanto o sujeito de teste usasse
	/// o fallback, todas as afirmacoes desta bancada ficariam verdes com a cor pessoal NUNCA saindo do
	/// lugar -- que e exatamente o defeito que ela precisa pegar. Com um tom que so pode ter vindo
	/// daqui, "a chama do Mistico e a do jogador" vira uma medida em vez de uma coincidencia.
	///
	/// E ELE E UM SORTEIO PLAUSIVEL, e nao um hexa bonito: os tres canais estao em 200..255, que e a
	/// faixa inteira que `CorDeAura.Sortear` pode produzir (`min(255, 200 + rand(0,255))`). Um tom
	/// escuro aqui testaria uma cor que o jogo nao sabe sortear.
	/// ==========================================================================================
	/// </summary>
	private static readonly Color CorPessoalDeTeste = new("ffd2c8");

	/// <summary>
	/// Formas percorridas, da mais fraca a mais forte.
	///
	/// AS CINCO QUE ACENDEM RAIO ESTAO TODAS AQUI, e e o ponto: depois do enunciado do dono elas sao
	/// o catalogo INTEIRO da faisca, entao um roteiro que pulasse uma delas deixaria um quinto do
	/// efeito sem bancada. As duas primeiras (`ssj1` e `grade2`) ficam pelo contrario -- sao as que
	/// precisam sair com raio ZERO, e o `grade2` e o caso citado pelo dono ("pode zerar").
	/// </summary>
	private static readonly string[] Roteiro =
	[
		"ssj1", "grade2", "ssj2", "ssj3",
		"primal_legendary2", "primal_legendary3", "ssj4_limit_breaker",
	];

	/// <summary>
	/// DE QUE COR A FAISCA DESTA FORMA TEM QUE SAIR, ou NULO quando a resposta e "a aura dela".
	/// Escrito a mao a partir do enunciado do dono e NAO lido da `CorDosRaios` -- perguntar a funcao
	/// o que ela devia responder e conferir a funcao com ela mesma, e qualquer regra passaria.
	///
	/// Uma verdade so, lida por dois blocos: o <see cref="AsCoresNoCatalogoInteiro"/> cobra que a cor
	/// saia exatamente assim nas 36 entradas, e o <see cref="AsCoresNaoSaoAAura"/> cobra que so estas
	/// fiquem paradas quando a aura e envenenada.
	///
	/// ============================ POR QUE POR LINHA, E NAO A LISTA DOS CINCO ============================
	/// A pergunta "de que cor" tem resposta ate pra quem nao acende raio nenhum -- e e por isso que a
	/// `CorDosRaios` nao olha o `Raios`. Escrever aqui so os cinco que desenham deixaria as outras 31
	/// sem cobranca nenhuma, e o defeito que isso libera e o mais silencioso possivel: uma forma que
	/// GANHE faisca amanha nascendo da cor errada, sem nada reprovar.
	///
	/// A FORMA DISTO E A MESMA DO <see cref="GrupoDoEnunciado"/> (por linha, com a excecao do Limit
	/// Breaker por id) de proposito: sao o mesmo enunciado do dono lido duas vezes, uma pro contorno
	/// e outra pra a faisca, e as duas tem que se parecer pra a diferenca entre elas saltar aos olhos.
	/// ============================================================================================
	/// </summary>
	private static string? FaiscaDoEnunciado(FormaDef d) => d.Linha switch
	{
		Jandirus.Core.Forms.LinhaDeForma.Saiyajin or Jandirus.Core.Forms.LinhaDeForma.Futuro
			when d.Id == Jandirus.Core.Forms.Catalogo.IdBase => null,
		// O VERMELHO E UM SO, e e por id AQUI (na bancada) de proposito: no Core ele e derivado do
		// `PedeGodKi`, e repetir a derivacao aqui faria os dois lados errarem juntos.
		Jandirus.Core.Forms.LinhaDeForma.Saiyajin or Jandirus.Core.Forms.LinhaDeForma.Futuro =>
			d.Id == "ssj4_limit_breaker" ? "ff2d2f" : "8fe3ff",
		// AS ESCADAS DE SANGUE INTEIRAS, e isso inclui o `primal_legendary4_limit_breaker`: ele pede
		// o mesmo ki divino do irmao Saiyajin e mesmo assim fica no AZUL da linha dele, que e o que
		// o contorno tambem faz (verde da linha, nao vermelho). O dono nomeou um Limit Breaker.
		Jandirus.Core.Forms.LinhaDeForma.Legendary
			or Jandirus.Core.Forms.LinhaDeForma.LegendaryPrimal => "8fe3ff",
		// A LINHA DO MISTICO E A UNICA FORA DAS ESCADAS DE SANGUE COM COR PROPRIA, e ela e DUAS desde
		// que o dono pediu *"no beast os raiozinhos sao roxos"*. Branca no Mistico porque a folha do
		// DM (`Electric_Mystic.dmi`) e neutra; roxo-clara na Fera porque foi pedida assim -- o
		// enunciado inteiro (e a colisao com a chama roxa dela) esta no `Catalogo.CorDosRaios`.
		//
		// POR ID AQUI e derivado la, exatamente como o vermelho do Limit Breaker acima: repetir a
		// derivacao `PedeGodKi >= GodkiRoyalePct` na bancada faria os dois lados errarem juntos no dia
		// em que alguem mexesse no `PedeGodKi` da Fera.
		Jandirus.Core.Forms.LinhaDeForma.Mistico => d.Id == "beast" ? "d9b0ff" : "ffffff",
		_ => null,   // divinas, Oozaru e a base: seguem a aura
	};

	private void Conferir(bool ok, string oque)
	{
		_passos.Add((ok ? "  ok   " : "  FALHA") + "  " + oque);
		if (!ok) _falhas.Add(oque);
	}

	/// <summary>
	/// O CORPO LOCAL. Estava escrito `... is { } id && id != 0 ? (Node2D?)null : null` -- os DOIS
	/// ramos devolviam nulo, entao a propriedade era um `null` com dez palavras em volta. Ninguem a
	/// consumia e por isso ela nunca reprovou nada; quem precisava do boneco o procurava na mao, com
	/// a linha repetida em quatro lugares deste arquivo.
	///
	/// Ela voltou a existir porque o recorte da foto (ver <see cref="Fotografar"/>) precisa saber
	/// ONDE o boneco esta na tela -- e a foto da rajada e o unico ponto desta bancada que responde
	/// "esta bonito".
	/// </summary>
	private Node2D? Corpo => GetTree().Root.FindChild("LocalPlayer", true, false) as Node2D;

	public override void _Process(double delta)
	{
		if (_acabou) return;
		_t += delta;
		// ============================ CUIDADO COM O OFF-BY-ONE AQUI ============================
		// O `switch (_passo++)` incrementa ANTES do corpo rodar. Entao esta guarda ve `_passo`
		// **um a menos** do que o `case` que vai executar: a espera da cena e pedida em 8 e o
		// `CenaDepois` roda no 9.
		//
		// Isso ja me pegou: eu pedi 6,5 s de espera em 9, a guarda leu 8, a cena teve 0,14 s -- e a
		// bancada acusou "a cena nao soltou o corpo" com o codigo perfeitamente certo.
		//
		// E A ESPERA SAI DA PROPRIA CENA (`_esperaDaCena`, escrito por quem a cria) e nao de um 6,5
		// cravado: a cena escolhida e a mais curta de `Cinematicas.Todas`, e o dia em que ela mudar de
		// duracao -- foi o que a restauracao dos prazos do DM fez -- a espera acompanha sozinha.
		// ==================================================================================
		// A CAMINHADA DECIDE A CADA 0,2 s, e ela tem que vir ANTES dos outros dois ramos: o passo dela
		// e o 0, que cairia no 1,2 s dos passos de abertura -- 200 decisoes a 1,2 s sao quatro minutos
		// de bancada pra atravessar quatro tiles. Ver `SairDaCopa`.
		if (_t < (_caminhando ? 0.2
				: _passo == PassoDaEspera ? _esperaDaCena
				: _passo >= 3 ? 0.14 : 1.2)) return;
		_t = 0;

		// ============================ A CAMARA ESCURA SEGURA O PASSO ============================
		// As fotos do bloco do PIXEL (`OParEOPixelDaTinta`) sao agendadas dentro do passo 2 e so podem
		// ser lidas no quadro SEGUINTE -- um `SubViewport` recem-nascido nao desenha no mesmo quadro em
		// que nasce, e chamar `RenderingServer.ForceDraw()` de dentro do `_Process` nao resolve (a foto
		// volta com alfa zero, calada). Entao a fila fica ANTES do `switch` e nao deixa o `_passo`
		// andar enquanto houver o que revelar: quem vem depois -- o `Posar`, as fotos da rajada -- e
		// exatamente quem nao pode ver os viewports de bancada ainda de pe.
		//
		// UM QUADRO SO: todas as fotos sao agendadas no mesmo passo, entao todas ficam prontas juntas.
		// ==================================================================================
		if (_fila.Count > 0) { RevelarAFila(); return; }

		switch (_passo++)
		{
			// O CEU E O CLIMA SAEM AQUI porque sao pedidos ao SERVIDOR e precisam da viagem de ida e
			// volta; a caminhada, nao -- ela so pode acontecer depois que a sujeira existir. Ver
			// `OCeuEOClimaDaFoto` e `SairDaSujeira`.
			case 0: Shaders(); OCeuEOClimaDaFoto(); break;
			case 1:
				Catalogo(); AsCoresNoCatalogoInteiro(); AsCoresNaoSaoAAura(); Cinematicas_();
					// LOGO DEPOIS DO BLOCO DO VENENO, e a ordem conta pra quem le o log: os dois usam a
					// MESMA tecnica (trocar a cor declarada e ver quem se mexe) em canais diferentes --
					// la contorno e faisca, aqui a CHAMA, que era o canal que faltava. Ele tambem mexe no
					// catalogo vivo e devolve tudo, entao ficar colado no irmao deixa as duas janelas de
					// veneno juntas em vez de espalhadas pela varredura.
					AChamaDeQuemEDeQueFolha();
					// E LOGO DEPOIS A FAMILIA QUE OLHA O **DISCO**, pela mesma logica de leitura do log:
					// as duas falam da cor pessoal, e a de cima so tem assunto porque a de baixo garante
					// que a cor existe em todo personagem -- inclusive nos que nasceram antes do campo.
					// Ela nao precisa de mundo, de rede nem de corpo; roda cedo de proposito, pra uma
					// rodada que morra la na frente ainda entregar o veredito que protege a CONTA.
					OSaveVelhoCarrega();
				AEscadaDoSsj3NoRoteiro();
				// OS TRES DEPOIS DO `Cinematicas_`, e a ordem importa pra quem LE o log: a duracao do DM
				// e o teto da encurtada sao afirmacoes sobre as mesmas cenas que o bloco de cima acabou
				// de percorrer, e a sonda de vao so faz sentido depois das 68 linhas que ela protege.
				ADuracaoDoDm(); OTetoDaEncurtada(); ASondaDeVaoEnxerga();
				// AS QUATRO ANCORAS DO RELOGIO, e a ordem delas conta uma historia pra quem le o log:
				// primeiro a UNIDADE contra a prosa do autor do DM (a unica fonte que ninguem aqui
				// pode reescrever), depois os NUMEROS que ela produziu cravados a mao, e por fim a
				// TRANCA medida contra a cena que a bancada mesma varreu. As tres so existem porque o
				// bloco de cima -- as 25 cenas -- deu verde por meses medindo o jogo contra o 12 que
				// esta propria bancada carregava.
				AUnidadeContraAProsaDoDm(); OsNumerosDeDestino(); ATrancaCobreACena();
				// AS QUATRO DIVINAS. Roda AQUI, e nao dentro do `NoCorpo`, porque ele nao precisa de
				// corpo nenhum: le catalogo, resolvedor de cabelo, disco e roteiro. Ficar fora do bloco
				// vivo tambem o deixa imune a ordem delicada de la (cenas vivas segurando o boneco).
				AsQuatroDivinas();
				break;
			// A ORDEM AQUI E REGRA. `AIdaEAVolta` dirige o corpo pelo `World.AoMudarForma` e precisa
			// achar o boneco como o `NoCorpo` o deixou (de gente, sem macaco, sem cena viva alem da
			// do teto). E o `Posar` vem DEPOIS dela porque a ida e volta termina na base, apagada --
			// quem repoe o corpo aceso pra a foto e ele.
			case 2:
				NoCorpo(); AIdaEAVolta();
				// DEPOIS DA IDA E VOLTA E PELO MESMO MOTIVO QUE ELA VEM DEPOIS DO `NoCorpo`: as duas
				// dirigem o corpo local pelo `World.AoMudarForma`, e esta precisa achar o boneco na BASE
				// (a `AIdaEAVolta` termina nela, apagada). Rodar antes mediria a volta de um corpo que
				// ainda estava vestido pelo bloco anterior.
				AVoltaDesfazTudo();
				// LOGO DEPOIS DELA E PELA MESMA REGRA DE ORDEM: as duas dirigem o corpo LOCAL e as duas
				// precisam acha-lo na base. Esta e a irma da de cima com uma FICHA passando no meio --
				// e ela devolve a ficha original no fim, justamente pra o `Posar` e as fotos la de baixo
				// continuarem vendo o boneco de sempre.
				ORaboEOOlhoSobrevivemAFicha();
				// ULTIMA DAS TRES QUE DIRIGEM O CORPO LOCAL, e pela mesma regra de ordem: ela precisa
				// achar o boneco na BASE e com a ficha ORIGINAL de volta (a irma de cima devolve as
				// duas coisas no fim). Ela tambem termina na base -- o `Posar` la embaixo continua
				// vendo o que sempre viu.
				OParEOPixelDaTinta();
				AProporcaoDeKi(); OCorteAntigoDoKi(); OKiCongeladoNaCena();
				// LE O FONTE E NAO O CORPO -- por isso pode rodar aqui, fora do `NoCorpo`. Ele e o
				// avesso do bloco vivo (`AAparenciaInteiraDoDegrau`, dentro do `NoCorpo`): la se
				// compara a FOTO, aqui o TEXTO. Ver o cabecalho dele.
				CadaCanalDaFormaTemUmDono();
				// A IRMA LARGA DELE: o mesmo tipo de pergunta (sobre o TEXTO), so que sobre o
				// repositorio inteiro e sobre uma arte que nao pode ter dono nenhum.
				AArteVelhaNaoTemDono();
				Posar("primal_legendary2");
				break;
			// DUAS FOTOS, e a segunda e a que prova alguma coisa. O SSJ3 tem aura quase branca
			// (`fff08a`): uma foto so dele nao distingue "o halo esta na cor da forma" de "o halo e
			// branco". A pose precisa de aura SATURADA -- se ela sair na cor, a cor manda mesmo.
			//
			// ERA O `blue`, E ELE NAO SERVE MAIS. A foto tem que responder DUAS perguntas ao mesmo
			// tempo -- "o halo esta na cor da forma?" e "o raio desenha?" -- e depois do corte do
			// dono o Blue esta em `Raios = 0`. A rajada forcada continuava saindo (o
			// `DispararDeTeste` nao pergunta se a forma tem raio), entao a foto mostrava faisca numa
			// forma que nao tem nenhuma: quem olhasse raj05..raj12 leria uma REGRESSAO onde estava
			// tudo certo. O `primal_legendary2` responde as duas: aura `4dff5a`, tao saturada quanto
			// o azul, e `Raios = 2`.
			// UMA RAJADA QUE COBRE UM CICLO INTEIRO, e nao tres fotos soltas.
			//
			// O raio agora e RARO de proposito (um a cada 2 s, visivel por ~0,3 s), e a primeira
			// rajada de tres quadros nao pegou nenhum -- o que NAO distingue "esta raro" de
			// "quebrou". Doze quadros a 0,2 s cobrem os 2 s do ciclo: se houver raio, ele aparece
			// em pelo menos um. Se nao aparecer em nenhum, ai sim ha defeito.
			// A RAJADA E DISPARADA DE PROPOSITO ANTES DE CADA FOTO.
			//
			// A rajada anterior mirava "esperar o relogio e torcer": doze quadros passaram e nenhum
			// pegou raio, o que nao distingue "esta raro" de "quebrou" -- e os pontos brancos que
			// apareceram na foto eram a NEVE do clima, nao eletricidade. Forcar o disparo tira a
			// sorte da conta: se ainda assim nao houver raio na foto, o defeito e real.
			// UMA rajada, e depois a VIDA dela quadro a quadro.
			//
			// Antes eu forcava uma rajada nova antes de cada foto -- o que provava que o raio
			// existe, mas nao mostrava o que ele FAZ. Como agora ele se contorce, estroba e se
			// parte em cacos ao longo de ~0,9 s, o que interessa e a SEQUENCIA: um disparo e as
			// fotos seguidas atravessando a vida inteira.
			// A CAMINHADA MORA DENTRO DO PASSO 3, repetindo-o, e nao num caso proprio: `PassoDaEspera` e
			// 8, os nomes das fotos saem do `_passo` (`raj05..raj12`) e o `CenaDepois` mora no 9 --
			// inserir um caso aqui deslocaria os tres e renomearia as fotos que o dono compara entre
			// rodadas.
			case 3:
				if (SairDaSujeira()) { _passo--; break; }
				break;
			default:
				// ============================ AS QUATRO PRIMEIRAS FOTOS FORAM DELETADAS ============================
				// Elas saiam nos passos 5 a 8 -- ou seja, DENTRO da cena do teto, que o `OTetoDaEncurtada`
				// monta no passo 1 justamente pra ela nao conseguir terminar. Fotografado, isso da o
				// personagem gritando "HAAAAH!!!" no meio de uma coluna de poeira que o cobre inteiro. Eu
				// abri as quatro achando que ia julgar a grossura do raio e o que estava na tela era terra.
				//
				// Nao ha o que salvar ali: o corpo esta no meio de uma cinematica proposital, e nenhuma
				// espera resolve porque a cena SO acaba no passo 9. Entao a rajada inteira mudou de lado --
				// e disparada DEPOIS de a cena se liberar, e as dez fotos limpas atravessam a vida dela.
				// ==============================================================================================
				if (_passo <= PassoDaEspera) break;
				if (_passo == PassoDaEspera + 1) { CenaDepois(); break; }
				// ============================ E O CORPO E REPOSADO ANTES DA RAJADA ============================
				// O `Posar` do passo 2 pinta o node direto, mas entre ele e aqui passaram a cena do teto e
				// o `CenaDepois` -- e cena veste e DESPE forma, o que chama `RaiosDaForma.Definir(false)` e
				// esconde o node. O `DispararDeTeste` nao pergunta nada: ele restarta o emissor de um node
				// invisivel e devolve `emitindo=True`, alegre.
				//
				// Foi exatamente o que aconteceu: dez fotos limpas, em campo aberto, ao meio-dia, com o log
				// dizendo "rajada forcada: 2 raio(s), emitindo=True" -- e ZERO pixel azul em qualquer uma
				// delas (medido: nenhum pixel com B>140 e B>R+25 nas dez). Um relatorio de "os raios
				// sumiram" sairia dali, sobre um efeito intacto.
				// ==========================================================================================
				if (_passo == PassoDaEspera + 2) { Posar("primal_legendary2"); ForcarRajada(); break; }
				if (_passo > 20) { Fechar(); break; }

				// ============================ DUAS TIRAS, E ELAS RESPONDEM PERGUNTAS DIFERENTES ============
				// `raj11..raj15` sao UMA rajada vivendo: nascer, estrobar, partir-se em cacos, apagar. E a
				// tira que mostra o que o raio FAZ.
				//
				// `raj16..raj20` sao CINCO RAJADAS NOVAS, uma por quadro. Ela existe porque a primeira nao
				// responde a pergunta do dono: numa rodada o sorteio deu UM raio, e o estrobo o apagou em
				// oito dos dez quadros -- sobraram 22 e 32 pixels de faisca no strip inteiro. Julgar
				// "a grossura variada esta preservada?" com um raio a cada duas fotos e adivinhacao.
				//
				// `Restart()` num OneShot MATA a leva anterior, entao as duas coisas nao cabem na mesma
				// tira: ou se ve uma rajada envelhecer, ou se veem muitas rajadas distintas. Sao cinco
				// amostras independentes de ate quatro raios cada -- ate 20 raios de sorteios diferentes,
				// que e amostra pra o olho comparar grossura de um pro outro.
				// ==========================================================================================
				if (_passo > 15) ForcarRajada(cobrar: false);
				Fotografar($"user://raj{_passo:00}.png");
				break;
		}
	}

	// =====================================================================
	// 1. OS SHADERS COMPILARAM?
	// =====================================================================
	/// <summary>
	/// Um shader quebrado NAO derruba o Godot: ele reclama no console e desenha com o material
	/// padrao. Quer dizer que o raio vira um retangulo branco e a aura some -- e o jogo continua
	/// rodando, o que e exatamente o que torna o defeito dificil de achar olhando.
	/// </summary>
	private void Shaders()
	{
		foreach (string caminho in new[]
		{
			"res://Assets/Shaders/RaioDaForma.gdshader",
			"res://Assets/Shaders/Personagem.gdshader",
			"res://Assets/Shaders/NebulosaDaForma.gdshader",
		})
		{
			var sh = GD.Load<Shader>(caminho);
			Conferir(sh != null, $"{caminho.GetFile()} carregou");
			if (sh == null) continue;

			// O `Code` volta vazio quando o arquivo nao foi lido; erro de sintaxe aparece no
			// console do Godot. Aqui da pra conferir que os uniforms que o C# escreve EXISTEM --
			// escrever num uniform inexistente e silencioso, e e o jeito classico de um efeito
			// "nao funcionar" sem nenhuma mensagem.
			string code = sh.Code ?? "";
			if (caminho.Contains("Raio"))
				// O `afinar` e o `variacao_grossura` entram na lista pela razao da rampa da nebulosa,
				// logo abaixo: sao os dois botoes com que o dono afina a grossura dos raiozinhos de
				// olho, sem recompilar. Quem os "limpar" pra constante mata a propriedade em
				// silencio -- o raio continua desenhando, e so o ajuste e que passa a custar um build.
				// E eles PRECISAM continuar uniform tambem porque o feixe de chao da cinematica
				// (`Transformacao.Feixes`) escreve os dois pra NAO ser afinado junto.
				foreach (string u in new[]
						 { "cor", "zigue", "grossura", "halo", "afinar", "variacao_grossura" })
					Conferir(code.Contains($"uniform") && code.Contains($" {u} "),
							 $"RaioDaForma tem o uniform '{u}'");
			else if (caminho.Contains("Nebulosa"))
				// A RAMPA PRECISA CONTINUAR SENDO UNIFORM, e e isto que a linha mede. Ela mora no
				// shader (e nao no Core, como as cores irmas) EXATAMENTE pra o dono afinar o indigo e
				// o ciano sem recompilar o jogo -- ver `Catalogo.TemNebulosa`. Alguem que "limpe" a
				// rampa pra constantes mata essa propriedade em silencio: o efeito continua
				// desenhando, so que so muda com um build inteiro pelo meio.
				//
				// O `forca` entra na lista porque e o UNICO uniform que o C# escreve (junto do
				// `semente`), e escrever num uniform inexistente e silencioso no Godot -- a nebulosa
				// simplesmente nunca acenderia, sem uma mensagem.
				//
				// O `pontos_densidade` entra pela MESMA razao da rampa: ele e o botao de "mais ou menos
				// pontinho branco", e o pedido foi explicito em poder afina-lo sem recompilar. Virasse
				// constante, o ajuste passaria a custar um build inteiro.
				//
				// E o `lado_do_quad` entra pelo motivo do `forca`: e escrito pelo C#
				// (`NebulosaDaForma._Ready`) pra converter o tamanho da particula de pixel pra UV. Se ele
				// sumir do shader a escrita vira silencio, e os pontos ficam no tamanho do padrao --
				// certos por acidente hoje, errados no dia em que o quad mudar de tamanho.
				foreach (string u in new[]
						 { "cor_borda", "cor_meio", "cor_perto", "parada_meio", "forca", "semente",
						   "pontos_densidade", "lado_do_quad" })
					Conferir(code.Contains($" {u} "), $"NebulosaDaForma tem o uniform '{u}'");
			else
			{
				// O `aura_pulso` CONTINUA FORA DA LISTA mesmo depois de o contorno voltar a pulsar:
				// o pulso e o proprio `aura`, escrito quadro a quadro pelo C# (ver
				// `CharacterVisual.ForcaNaFaseDoPulso`). Nao ha uniform novo pra conferir.
				foreach (string u in new[] { "aura", "aura_cor" })
					Conferir(code.Contains($" {u} "), $"Personagem tem o uniform '{u}'");

				// E O HALO NAO PODE VOLTAR. Ele era o unico ponto deste shader que escrevia alfa
				// onde nao havia desenho, e era o que o dono via "amarronzado por cima do verde do
				// chao". Se alguem o reintroduzir sera por `c.a = max(...)` dentro do bloco da aura
				// -- entao e o alfa que se mede, e nao a palavra "halo".
				//
				// A CHAVE E NOVA E ELA CONSERTA UM ESCOPO: sem ela esta linha ficava FORA do `else`
				// (so a indentacao fingia o contrario) e era medida em TODO shader da lista. Passava
				// verde por acidente, e com a nebulosa entrando na lista viraria uma terceira copia da
				// mesma frase no relatorio -- dizendo do `NebulosaDaForma` uma coisa que so faz
				// sentido pro `Personagem`.
				Conferir(!code.Contains("c.a = max("),
						 "o contorno da forma nao cria pixel fora da silhueta (quem brilha e a aura)");
			}
		}

		// O SHADER DO RAIO PRECISA DO `INSTANCE_CUSTOM`: e dele que sai a fase de vida da
		// particula. Sem ela o raio nasce e morre com o mesmo brilho -- estatico.
		var raio = GD.Load<Shader>("res://Assets/Shaders/RaioDaForma.gdshader");
		Conferir(raio?.Code?.Contains("INSTANCE_CUSTOM") == true,
				 "o raio le INSTANCE_CUSTOM (senao ele nao pisca nem apaga)");
		Conferir(raio?.Code?.Contains("MODEL_MATRIX") == true,
				 "o raio usa a posicao como semente (senao todos saem iguais)");
	}

	// =====================================================================
	// 2. O CATALOGO
	// =====================================================================
	private void Catalogo()
	{
		var comRaio = new List<string>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Raios > 0) comRaio.Add(d.Id);
			if (d.Raios is < 0 or > 2) Conferir(false, $"{d.Id}: Raios fora de 0..2 ({d.Raios})");
			// TODA FORMA COM RAIO PRECISA DE COR. Era a `Aura` medida aqui; hoje quem pinta o raio e a
			// `Catalogo.CorDosRaios`, e e ELA que precisa devolver hexa valido -- medir a aura passaria
			// verde mesmo se a derivacao devolvesse lixo.
			if (d.Raios > 0 && Jandirus.Core.Forms.Catalogo.CorDosRaios(d).Length != 6)
				Conferir(false, $"{d.Id}: raio sem cor");
		}
		// ============================ A LISTA INTEIRA, E NAO UM PISO ============================
		// Isto era `comRaio >= 15`, e o piso era o defeito: ele passava com QUALQUER conjunto de 15
		// formas. Quando o dono cortou a faisca de quase tudo, um piso desses nao teria dito nada
		// sobre QUEM ficou -- e "quem" e a regra inteira. Agora e o conjunto EXATO, escrito a mao a
		// partir do enunciado ("ssj n tem efeitos de raio" + "raiozinhos somente o lssj 2 do primal
		// legendary o resto n tem raio" + "grade2 e grade3 nao tem raio mesmo, pode zerar" + o
		// `primal_legendary3` e o `ssj4_limit_breaker` que ele acrescentou depois), e nao lido do
		// catalogo: ler seria conferir o catalogo com ele mesmo.
		//
		// COMO REPROVA SE A REGRA SUMIR: devolva `Raios` a qualquer forma e ela aparece aqui pelo id;
		// tire de qualquer uma destas sete e ela some da lista impressa.
		//
		// ERAM CINCO E VIRARAM SETE. Os dois novos sao a linha do Mistico -- *"Mistico: tudo igual a
		// base, MAS ele TEM os raiozinhos, que estao faltando"* --, e eles nao vem da mesma folha dos
		// outros: `Electric_Mystic.dmi` contra o `electrictyeffects` da escada Saiyajin. O `beast` nao
		// foi nomeado pelo dono e entrou por `Mystic.dm:112` (o mesmo objeto de overlay, mantido no
		// buff dele) -- ver `FormaDef.Raios`.
		// ==================================================================================
		//
		// ============================ E ONZE DEPOIS DAS LINHAS RACIAIS -- A LISTA FICOU PRA TRAS ============================
		// As quatro que entraram sao do porte das outras racas, e nenhuma delas e escolha de desenho:
		// as quatro vestem folha de eletricidade no PROPRIO buff do DM, e a lista aqui e que nao tinha
		// sido atualizada -- ou seja, a bancada ficou VERMELHA por um motivo velho enquanto o catalogo
		// estava certo, que e o pior estado dos dois lados (a proxima falha de verdade entra no meio
		// das ja aceitas). Cada uma com a folha que a acende:
		//
		//   * `snamek`  -- `snamek Elec.dmi` (`Super_Namek.dm`), UMA folha, volume 1;
		//   * `heran2`  -- `Electric_Red.dmi` (`HeranBuff.dm:212`), UMA folha, volume 1. O `heran1`
		//     fica de fora e a ausencia dele e a prova de que a linha nao entrou "por simetria": o
		//     `Max_Power` nao veste folha nenhuma -- o raio dele e da transformacao, nao da aura;
		//   * `alien1` e `alien2` -- a MESMA `spc` nos dois galhos (`Alien_Transformations.dm:60`,
		//     `:66`), UMA folha cada.
		// ============================================================================================================
		string[] esperado =
			["ssj2", "ssj3", "primal_legendary2", "primal_legendary3", "ssj4_limit_breaker",
			 "mistico", "beast", "snamek", "heran2", "alien1", "alien2"];
		Conferir(comRaio.Count == esperado.Length && esperado.All(comRaio.Contains),
				 $"SO onze formas acendem raio, e sao estas ({string.Join(", ", comRaio)})");

		// O VOLUME. O SSJ2 e o UNICO leve porque no DM o `if(2)` acende UMA folha; o SSJ3 e o
		// `primal_legendary2` sao o CHEIO porque os dois caem no `if(3)`, que soma tres; o Limit
		// Breaker e cheio pelas DUAS folhas proprias dele (`ssj4lb_sparks` + `ssj4lb_lightning`); e o
		// `primal_legendary3` e o unico volume que NAO e o do DM -- ver `FormaDef.Raios`.
		Conferir(Jandirus.Core.Forms.Catalogo.Def("ssj2")!.Raios == 1, "SSJ2 tem raio LEVE (uma folha no DM)");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("ssj3")!.Raios == 2, "SSJ3 tem raio cheio (tres folhas)");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("primal_legendary2")!.Raios == 2,
				 "o LSSJ2 do Primal Legendary tem o MESMO cheio do SSJ3 (e o mesmo `if(3)` do DM)");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("primal_legendary3")!.Raios == 2,
				 "o LSSJ3 do Primal Legendary tambem e cheio (nao PERDE faisca subindo a escada)");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("ssj4_limit_breaker")!.Raios == 2,
				 "o Limit Breaker e cheio (as duas folhas proprias dele no DM)");

		// E OS QUE NAO TEM. Os quatro estao aqui porque cada um representa um motivo diferente de
		// alguem querer devolver a faisca: o `ssj1` foi o primeiro corte do dono, os grades eram o
		// `reg_elec` do DM (e o dono nomeou os dois pra zerar), o `ssj4` troca o CORPO em vez da
		// eletricidade, e o `primal_legendary4_limit_breaker` e o que mais parece com quem TEM --
		// mesmo nome, mesmo ki divino, outra linha. Ele e a checagem que pega o erro provavel deste
		// lote: dar faisca aos "dois Limit Breaker" quando o dono nomeou um.
		Conferir(Jandirus.Core.Forms.Catalogo.Def("ssj1")!.Raios == 0, "SSJ1 NAO tem raio");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("grade2")!.Raios == 0
			  && Jandirus.Core.Forms.Catalogo.Def("grade3")!.Raios == 0,
				 "os Grades NAO tem raio (o `reg_elec` do DM saiu no corte: \"pode zerar\")");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("ssj4")!.Raios == 0,
				 "SSJ4 NAO tem raio (ele troca o CORPO, nao a eletricidade)");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("primal_legendary4_limit_breaker")!.Raios == 0,
				 "o Limit Breaker PRIMAL nao acompanhou o irmao Saiyajin (o dono nomeou um so)");

		// A LINHA DO MISTICO E LEVE NOS DOIS, e o motivo e a mesma conta de folhas do resto do bloco:
		// `Mystic.dm:37` acende UMA (`MysticEffect`) e `:112` mantem a MESMA no Beast -- nao ha
		// segunda folha em lugar nenhum dos dois buffs. Marcar o Beast como cheio seria dar volume
		// por hierarquia ("ele e o degrau de cima, entao tem mais") e nao por arte.
		Conferir(Jandirus.Core.Forms.Catalogo.Def("mistico")!.Raios == 1,
				 "o Mistico tem raio LEVE (uma folha: `Electric_Mystic.dmi`)");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("beast")!.Raios == 1,
				 "e o Beast herda a MESMA (`Mystic.dm:112`), entao tambem leve");

		// ============================ QUATRO AZUIS E UM VERMELHO, CONTADOS ENTRE OS QUE DESENHAM ============================
		// O enunciado do dono tem DUAS metades -- "cinco formas com raio" e "quatro azuis, um
		// vermelho" -- e as checagens de cima so cobrem a primeira. A cor e cobrada mais abaixo
		// (`AsCoresNoCatalogoInteiro`), so que la ela e cobrada nas 36 ENTRADAS, pelo grupo do
		// enunciado: as 20 formas da regra saem em dois tons e as 16 restantes seguem a aura.
		//
		// E o que aquilo NAO diz e justamente a segunda metade: das 20 que a regra pinta, so CINCO
		// desenham, e a divisao entre elas e 4/1. Um catalogo em que o `ssj2` virasse vermelho e o
		// `ssj4_limit_breaker` azul passaria naquele bloco inteiro (os dois tons continuam sendo dois,
		// cada grupo continua com um tom) e cairia aqui.
		//
		// A CONTA E SOBRE `Raios > 0` DE PROPOSITO, e este e o unico lugar da bancada em que isso e
		// certo: aqui a pergunta e "de que cor sai a eletricidade que o jogador VE", e quem nao
		// desenha nao aparece na tela pra ter cor. Nos blocos de cor la de baixo a mesma guarda seria
		// defeito -- ela deixaria as 31 sem cobranca nenhuma, e um degrau novo nasceria da cor errada.
		//
		// COMO REPROVA SE A REGRA SUMIR: troque a guarda `d.PedeGodKi >= 0` de `CorDosRaios` por um
		// `d.Id == "ssj2"` e a conta vira 1 azul / 4 vermelhos; apague a guarda inteira e vira 5 azuis
		// e ZERO vermelhos -- os dois casos saem com a lista de ids ao lado do tom.
		// ================================================================================================
		var tomDaFaisca = new Dictionary<string, List<string>>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Raios <= 0) continue;
			string tom = Jandirus.Core.Forms.Catalogo.CorDosRaios(d);
			if (!tomDaFaisca.TryGetValue(tom, out List<string>? l)) tomDaFaisca[tom] = l = [];
			l.Add(d.Id);
		}
		string Quem(string tom) => tomDaFaisca.TryGetValue(tom, out List<string>? l)
			? $"{l.Count}: {string.Join(", ", l)}" : "0";

		// LITERAIS, como no resto das cores: escrever `== AzulDaFaisca` conferiria a constante com
		// ela mesma e passaria com qualquer valor trocado por engano.
		//
		// ============================ E VIRARAM OITO TONS COM AS QUATRO RACIAIS ============================
		// As quatro que entraram sao cada uma de um tom, e nenhuma delas sai desta lista escrita a mao:
		// as linhas raciais caem no `_ => d.Aura` do `CorDosRaios` (o AZUL e das escadas de SANGUE
		// Saiyajin), entao a faisca de cada uma **e a cor da chama dela**. Isso e deliberado e esta
		// medido na bancada `formas` [19] ("a faisca e da cor da propria chama"); o que se conta aqui
		// e o efeito na TELA: quatro tons novos, um por linha, e nenhum deles repetindo os quatro
		// antigos -- se o `snamek` virasse azul, esta conta cairia pra sete sem que a cor dele
		// parecesse errada em lugar nenhum.
		// ============================================================================================
		Conferir(tomDaFaisca.Count == 8,
				 $"as onze faiscas saem em OITO tons ({tomDaFaisca.Count}: "
			   + $"{string.Join(" / ", tomDaFaisca.Select(p => $"{p.Key}={p.Value.Count}"))})");
		Conferir(tomDaFaisca.TryGetValue("8fe3ff", out List<string>? azuis) && azuis.Count == 4,
				 $"QUATRO delas sao azuis #8fe3ff ({Quem("8fe3ff")})");
		// E AS QUATRO RACIAIS SAO **UMA CADA**, e nao "quatro em algum lugar da conta": a verde do
		// Super Namekuseijin (`snamek Elec.dmi`), a vermelha do True Max Power (`Electric_Red.dmi`) e
		// as duas Alien, que sao a MESMA folha em tons diferentes porque a chama de cada degrau e que
		// muda. Sem esta linha, duas racas trocando de cor entre si passariam pela conta de cima.
		foreach ((string id, string tom) in new[]
			{ ("snamek", "6fe36f"), ("heran2", "ff4a4a") })
			Conferir(tomDaFaisca.TryGetValue(tom, out List<string>? so) && so.Count == 1 && so[0] == id,
					 $"e a faisca de `{id}` e #{tom}, so dela ({Quem(tom)})");
		Conferir(tomDaFaisca.TryGetValue("ff2d2f", out List<string>? vermelhas)
			  && vermelhas.Count == 1 && vermelhas[0] == "ssj4_limit_breaker",
				 $"e UMA e vermelha #ff2d2f, o Limit Breaker ({Quem("ff2d2f")})");

		// ============================ E A LINHA DO MISTICO VIROU DOIS TONS ============================
		// Esta conta era TRES tons com DUAS brancas -- a linha inteira dividia o `ffffff`. O dono
		// separou: *"no beast os raiozinhos sao roxos"*, e so a Fera.
		//
		// UMA branca e UMA roxa, cada uma na sua linha: uma conta unica de "duas cores na linha"
		// passaria com as duas trocadas de lugar, que e justamente o erro provavel aqui (o Mistico e o
		// degrau de BAIXO e e ele que fica com o branco do arquivo do DM).
		Conferir(tomDaFaisca.TryGetValue("ffffff", out List<string>? brancas)
			  && brancas.Count == 1 && brancas[0] == "mistico",
				 $"e UMA e branca #ffffff, o Mistico -- a folha `Electric_Mystic.dmi` nao tem matiz "
			   + $"nenhuma ({Quem("ffffff")})");
		Conferir(tomDaFaisca.TryGetValue("d9b0ff", out List<string>? roxas)
			  && roxas.Count == 1 && roxas[0] == "beast",
				 $"e UMA e roxa #d9b0ff, a Fera -- pedido do dono ({Quem("d9b0ff")})");
	}

	// =====================================================================
	// 2b. AS CINEMATICAS
	// =====================================================================
	/// <summary>
	/// AS CENAS DE PRIMEIRA TRANSFORMACAO.
	///
	/// ============================ O QUE PODE QUEBRAR CALADO AQUI ============================
	/// Cinematica e o pior tipo de codigo pra confiar no olho: ela roda UMA VEZ na vida do
	/// personagem, e quem a viu quebrada nao a ve de novo pra conferir. Os defeitos possiveis:
	///   * uma musica cujo arquivo nao existe (o Godot avisa e segue -- cena muda);
	///   * beats fora de ordem (o tocador dispara por `Em <= _t` e pularia os atrasados);
	///   * uma cena sem o beat `Assumir` -- a pessoa faria a cena inteira e NAO transformaria;
	///   * uma forma sem cena, ou pegando a cena de outra forma.
	/// ==================================================================================
	/// </summary>
	private void Cinematicas_()
	{
		// ============================ AS REGRAS VALEM PRAS DUAS VERSOES DA CENA ============================
		// Isto era o corpo de um `foreach` sobre `Cinematicas.Todas`. Virou funcao local porque a cena
		// ENCURTADA (`Cinematicas.Encurtada`) e uma cena de verdade -- ela e tocada pelo mesmo
		// `Transformacao`, prende o mesmo corpo e pode ter os mesmos defeitos. Conferir so a cheia
		// deixaria a versao que o jogador vai ver NA MAIORIA DAS VEZES sem bancada nenhuma: a estreia
		// roda uma vez na vida do personagem, a encurtada roda ate ele dominar a forma.
		//
		// COPIAR O BLOCO seria a outra saida, e o modo de falha dela e o de sempre aqui: consertar uma
		// regra numa copia e esquecer da outra.
		// ==========================================================================================
		// ============================ E ELAS VALEM PRA CENA QUE NAO E DE FORMA NENHUMA ============================
		// O `prendeOCorpo` nasceu com a cena da FURIA (`Cinematicas.Furia`), a unica que legitimamente
		// nao trava o jogador (`set waitfor = 0`, `Murder.dm:137`). Ele isola a UNICA regra desta funcao
		// que fala de forma -- "o corpo fica preso ate o beat que assume" -- e deixa todo o resto valendo
		// pra ela: a cratera no instante da troca, a poeira que so vem com a cratera, a cauda que so
		// assenta, o vao, o beat vazio, o clarao, o piscar, os sons.
		//
		// A ALTERNATIVA ERA COPIAR AS REGRAS, e ela ja estava em uso: a bancada `raiva` [10] confere as
		// quatro principais na cena da furia por uma copia escrita a mao. Copia envelhece de um jeito so
		// -- a regra numero cinco entra AQUI, a copia nao sabe, e a cena nova fica de fora sem ninguem
		// notar. Com o parametro, a furia entra na varredura das 34 e sai junto com elas.
		// ======================================================================================================
		void ConferirRoteiro(Jandirus.Core.Forms.Cinematica c, string rotulo, bool prendeOCorpo = true)
		{
			Conferir(c.Beats.Length >= 3, $"cena de '{rotulo}': tem beats ({c.Beats.Length})");

			// EM ORDEM CRESCENTE: o tocador anda pra frente e nao volta.
			bool ordenada = true;
			for (int i = 1; i < c.Beats.Length; i++)
				if (c.Beats[i].Em < c.Beats[i - 1].Em) ordenada = false;
			Conferir(ordenada, $"cena de '{rotulo}': os beats estao em ordem de tempo");

			// ============================ O BEAT QUE FAZ A FORMA FICAR, E SO UM ============================
			// Sem ele a cena roda e o personagem volta ao normal.
			//
			// E `== 1`, e nao `>= 1`, porque DOIS `Assumir` nao dao erro nenhum em jogo: o segundo
			// reaplica cabelo, aura e contorno por cima do primeiro e a cena continua bonita. O que ele
			// quebra e a CONTA -- o `Cinematicas.Encurtar` tira o `k` do PRIMEIRO `Assumir` e o
			// `SegundosPreso` do primeiro tambem, entao a curta comprimiria o relogio por um instante e
			// soltaria o corpo por outro. O proprio comentario de la ja avisava do risco; aqui ele deixa
			// de ser aviso e vira reprova.
			//
			// COMO REPROVA SE A REGRA SUMIR: acrescente `| Efeito.Assumir` a qualquer beat de qualquer
			// cena -- esta linha cai nas DUAS versoes dela (cheia e curta), porque quem confere e a
			// funcao compartilhada.
			// =========================================================================================
			int assumires = c.Beats.Count(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir));
			bool assume = assumires > 0;
			Conferir(assumires == 1, $"cena de '{rotulo}': tem UM beat que ASSUME a forma ({assumires})");

			// E ELE NAO PODE SER O PRIMEIRO -- seria transformar antes da cena.
			if (assume)
			{
				int ondeNaLista = Array.FindIndex(
					c.Beats, b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir));
				double quando = c.Beats[ondeNaLista].Em;
				Conferir(quando > 1.0, $"cena de '{rotulo}': a forma so fica aos {quando:0.#}s, nao no inicio");

				// ============================ O PRAZO E O BEAT, SEM FOLGA NENHUMA ============================
				// Isto eram DUAS checagens com folga (`>= preso - 0,01` de um lado, `< preso + 0,51` do
				// outro), e a folga do segundo lado era justificada por "uma das cenas escritas a mao ja
				// diverge de proposito". Nenhuma diverge: as 34 casam exatamente, nas duas versoes -- o
				// `Encurtar` inclusive tira o prazo da curta DO BEAT JA ARREDONDADO so pra que este `==`
				// seja possivel (o comentario dele diz isso, e a bancada nao estava cobrando).
				//
				// Meio segundo de folga nao e pouco aqui: sao seis tiques do DM, e o defeito que ela deixava
				// passar e o pior dos dois lados -- o corpo solto ANTES da forma ficar, ou seja o jogador
				// andando enquanto a cena ainda conta que ele vai virar. A regra do dono e uma so ("no dm e
				// o tempo inteiro da transformaçao parado"), entao a checagem tambem e uma so.
				//
				// COMO REPROVA SE A REGRA SUMIR: escreva `SegundosPreso = 8.5` em qualquer cena (que e
				// literalmente o estado anterior a restauracao dos prazos do DM) e ela cai; troque o
				// `preso = nb.Em` do `Encurtar` pelo `alvo` cru e ela cai so na versao CURTA, que e o lado
				// que ninguem olha.
				// =======================================================================================
				//
				// E A CENA QUE NAO PRENDE TEM QUE NAO PRENDER **NADA** -- o outro lado da mesma regra,
				// e nao uma dispensa. Meio segundo de prisao numa cena que se anuncia livre seria pior
				// que a prisao inteira: o jogador que acabou de ver um amigo morrer nao entenderia por
				// que travou, e nao ha tela nenhuma que diga isso.
				if (prendeOCorpo)
					Conferir(Math.Abs(quando - c.SegundosPreso) < 0.001,
							 $"cena de '{rotulo}': o corpo fica preso ATE o beat que assume, sem folga "
						   + $"({c.SegundosPreso:0.###}s preso, forma aos {quando:0.###}s)");
				else
					Conferir(c.SegundosPreso == 0,
							 $"cena de '{rotulo}': nao prende o corpo NEM UM INSTANTE "
						   + $"({c.SegundosPreso:0.###}s preso)");

				// ============================ E DEPOIS DELE SO CABE O ASSENTAMENTO ============================
				// O pedido era "o beat que ASSUME e o ultimo". Ele NAO e: nas 34 cenas ha exatamente um beat
				// depois dele, e nas duas versoes -- e ele nao e sobra, e a poeira baixando (num dos moldes e
				// o `spawn(20) createCrater` do DM, que cai 2,0 s DEPOIS do `move = 1`).
				//
				// Entao o que se tranca aqui e a regra que aquele pedido protege, e ela e mais estreita: da
				// para o beat que assume, o corpo ESTA SOLTO. Tudo o que for agendado nessa janela acontece a
				// alguem que ja pode sair andando, e os efeitos perseguem o corpo (`_alvo.GlobalPosition`) --
				// uma segunda aura grande ali seguiria o jogador pelo mapa, um segundo `Assumir` reaplicaria
				// cabelo em quem ja esta longe, e uma fala sairia da boca de quem ja esta em outra briga.
				//
				// SAO DUAS COISAS SEPARADAS porque quebram separadas: o TAMANHO da cauda (um segundo ato
				// escrito depois do climax) e o CONTEUDO dela (um efeito de transformacao que vazou pra
				// depois da transformacao).
				//
				// COMO REPROVA SE A REGRA SUMIR: acrescente um segundo beat depois do ultimo de qualquer
				// cena e cai a primeira; ponha `Efeito.AuraGrande` (ou uma `Fala`) na cauda e cai a segunda.
				// As duas caem nas DUAS versoes, porque quem confere e esta funcao compartilhada.
				// ========================================================================================
				Conferir(ondeNaLista >= c.Beats.Length - 2,
						 $"cena de '{rotulo}': depois do beat que assume so cabe UM de assentamento "
					   + $"(o assumir e o {ondeNaLista + 1}o de {c.Beats.Length})");

				// O CASCALHO SAIU DESTA MASCARA junto com o efeito (bit 8192 aposentado, cortado pelo
				// dono). O que sobra e o que assenta de verdade: poeira, cratera e a faisca do corpo.
				const Jandirus.Core.Forms.Efeito assentar =
					Jandirus.Core.Forms.Efeito.Poeira
					| Jandirus.Core.Forms.Efeito.Cratera | Jandirus.Core.Forms.Efeito.Raios;
				string vazou = "";
				foreach (Jandirus.Core.Forms.Beat b in c.Beats.Skip(ondeNaLista + 1))
					if ((b.Faz & ~assentar) != Jandirus.Core.Forms.Efeito.Nada
						|| b.Fala.Length > 0 || b.Narra.Length > 0 || b.Som.Length > 0)
						vazou = $"{b.Em:0.##}s: {b.Faz} '{b.Fala}{b.Narra}' {b.Som}";
				Conferir(vazou.Length == 0,
						 $"cena de '{rotulo}': e essa cauda so ASSENTA (poeira/cratera/faisca) -- {vazou}");
			}

			// O CORPO NAO PODE FICAR PRESO A CENA INTEIRA -- ver `Cinematica.SegundosPreso`.
			Conferir(c.SegundosPreso < c.Segundos,
					 $"cena de '{rotulo}': solta o corpo antes do fim ({c.SegundosPreso:0.#} de {c.Segundos:0.#}s)");

			// ============================ E NENHUM BURACO NO MEIO ============================
			// A checagem que impede a cena longa e VAZIA -- ver <see cref="MaiorVaoSemBeat"/> pro N e a
			// justificativa dele. Aqui, na funcao compartilhada, porque um buraco e propriedade da CENA e a
			// reprova precisa dizer QUAL: uma unica linha "alguma cena tem buraco" mandaria quem for
			// consertar procurar em 34.
			//
			// NA CURTA TAMBEM, e ela nao e de graca: o `Encurtar` multiplica todos os beats pelo mesmo `k`,
			// entao um buraco encolhe junto -- mas o `k` sai do beat que ASSUME, e beats DEPOIS dele nao
			// estao no calculo. Uma cauda que crescesse abriria um vao que a cheia nao tem.
			// ============================================================================
			(double vao, double onde) = MaiorVaoSemBeat(c);
			Conferir(vao <= VaoMaximoSemBeat + 0.001,
					 $"cena de '{rotulo}': nenhum vao acima de {VaoMaximoSemBeat:0.#}s sem beat "
				   + $"(o maior tem {vao:0.##}s, a partir de {onde:0.##}s)");

			// BEAT QUE NAO FAZ NADA E NAO FALA NADA nao pode existir: e um instante em que a cena para
			// pra nada acontecer. Na cheia isso e desleixo; na ENCURTADA seria defeito do `Encurtar`,
			// que apaga fala e narracao e por isso pode esvaziar um beat -- e ele descarta esses.
			int vazios = c.Beats.Count(b => b.Faz == Jandirus.Core.Forms.Efeito.Nada
										 && b.Som.Length == 0 && b.Fala.Length == 0 && b.Narra.Length == 0);
			Conferir(vazios == 0, $"cena de '{rotulo}': nenhum beat vazio ({vazios})");

			// ============================ A CENA NAO VESTE DEGRAU QUE NAO EXISTE ============================
			// O `Efeito.VesteDegrau` nao diz QUAL degrau (ver o comentario dele): o n-esimo beat veste o
			// n-esimo degrau da `EscadaDaCena`. Um beat a mais que a escada nao derruba nada em jogo --
			// o tocador simplesmente nao veste nada --, e e por isso que ele precisa reprovar aqui: em
			// jogo ele se pareceria com um instante em que a cena para pra nada acontecer, que e o mesmo
			// defeito do beat vazio, so que invisivel pra checagem de cima.
			//
			// COMO REPROVA SE A REGRA SUMIR: acrescente `| Efeito.VesteDegrau` a um quarto beat da cena
			// do SSJ3 (a escada dela tem tres: base, SSJ1, SSJ2).
			// ==========================================================================================
			int veste = c.Beats.Count(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.VesteDegrau));
			if (veste > 0 && Jandirus.Core.Forms.Catalogo.Def(c.Forma) is { } defDaCena)
			{
				int degraus = Jandirus.Core.Forms.Cinematicas.EscadaDaCena(defDaCena).Length;
				Conferir(veste <= degraus,
						 $"cena de '{rotulo}': veste {veste} degrau(s) e a escada ate '{c.Forma}' tem {degraus}");
			}

			// ============================ O CLARAO E UM SO, E MORA NO BEAT QUE ASSUME ============================
			// A regra e do `Efeito.ClaraoDeTela` e ela existe por SEGURANCA, nao por gosto: um `ColorRect`
			// de tela cheia acendendo duas vezes numa cena e um estrobo, e a versao ENCURTADA comprime a
			// MESMA cena em ate 10 s -- o que numa cheia de 116 s seria "de vez em quando" viraria piscar.
			//
			// Ela precisa de bancada porque nada em jogo a acusa: um clarao a mais parece efeito. E as
			// DUAS metades importam -- a contagem impede o estrobo, e a amarracao ao `Assumir` e o que
			// impede o clarao de virar mais um enfeite de meio de cena (o instante em que a forma FICA e
			// a unica coisa que ele existe pra distinguir).
			//
			// COMO REPROVA SE A REGRA SUMIR: acrescente `| Efeito.ClaraoDeTela` a qualquer beat que nao
			// seja o do `Assumir` -- cai nas duas versoes da cena, porque quem confere e esta funcao.
			// ==================================================================================================
			// ============================ O PISCAR E UM INTERRUPTOR, LOGO E UM SO ============================
			// O `Efeito.PiscaCabelo` deixou de valer uma troca e passou a valer "liga daqui ate o
			// `Assumir`" (ver o comentario dele no Core). Um segundo beat com a bandeira nao faz nada em
			// jogo -- o tocador ignora se ja estiver ligado --, e e por isso que ele precisa reprovar
			// aqui: em jogo ele se pareceria com um beat que existe pra nada acontecer, que e o mesmo
			// defeito do beat vazio, invisivel pra checagem de cima porque ele nao esta vazio.
			//
			// E O PERIGO REAL E O CONTRARIO: alguem lendo a cena antiga (quatro beats, um por troca) e
			// "consertando" o piscar de volta pra pulso enchendo a cena de bandeiras. Com esta linha, a
			// primeira tentativa reprova e manda ler o Core.
			//
			// COMO REPROVA SE A REGRA SUMIR: acrescente `| Efeito.PiscaCabelo` a qualquer outro beat da
			// cena do SSJ1 -- cai nas duas versoes dela, porque quem confere e esta funcao.
			// ============================================================================================
			int piscares = c.Beats.Count(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.PiscaCabelo));
			Conferir(piscares <= 1, $"cena de '{rotulo}': no maximo UM beat que ARMA o piscar ({piscares})");

			// ============================ E ELE NAO CONVIVE COM A ESCADA ============================
			// Os dois escrevem cabelo por relogios diferentes: o degrau e o ESTADO que a cena narra, o
			// piscar troca duas vezes por segundo. O tocador tem a rede (`VestirODegrauSeguinte` desliga
			// o piscar), mas rede nao e projeto: uma cena com as duas bandeiras teria o piscar morrendo
			// no primeiro degrau, ou seja um efeito escrito no roteiro e apagado pelo tocador -- que e
			// exatamente a "funcionalidade escondida" que este projeto nao deixa ficar.
			//
			// Hoje o conjunto e vazio dos dois lados (piscam `ssj1`/`future_ssj`/`primal_c_type`, veste
			// degrau so o `ssj3`), e essa e a afirmacao que se tranca.
			//
			// COMO REPROVA SE A REGRA SUMIR: ponha `| Efeito.PiscaCabelo` no primeiro beat da cena do SSJ3.
			// =====================================================================================
			Conferir(piscares == 0 || veste == 0,
					 $"cena de '{rotulo}': o piscar de cabelo e a escada de degraus nao convivem "
				   + $"({piscares} piscar, {veste} degrau)");

			// ============================ O BANHO DE COR TAMBEM E UM SO ============================
			// Mesma razao do clarao logo abaixo, e o mesmo perigo na encurtada: o `Encurtar` multiplica
			// os instantes pelo mesmo `k`, entao dois banhos numa cheia de 23 s viram dois banhos em 9,3
			// -- e o banho dura 1,0 s (`Cinematicas.SegundosDoBanho`). Nas quatro procs de surto o DM da
			// exatamente um, na largada; nas outras tres fabricas, exatamente um, no fim.
			//
			// ELE NAO PRECISA CAIR NO `Assumir` (a diferenca pro clarao): nos surtos ele cai no beat 0,
			// junto do grito, e a forma so vem `sleep(8)` depois. Amarra-lo ao `Assumir` obrigaria a
			// fabrica a mentir sobre o instante do `animate` do DM.
			//
			// COMO REPROVA SE A REGRA SUMIR: acrescente `| Efeito.BanhoDeCor` a um segundo beat de
			// qualquer cena.
			// ==================================================================================
			int banhos = c.Beats.Count(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.BanhoDeCor));
			Conferir(banhos <= 1, $"cena de '{rotulo}': no maximo UM banho de cor ({banhos})");

			int claroes = c.Beats.Count(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.ClaraoDeTela));
			Conferir(claroes <= 1, $"cena de '{rotulo}': no maximo UM clarao de tela ({claroes})");
			Conferir(c.Beats.All(b => !b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.ClaraoDeTela)
								   || b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir)),
					 $"cena de '{rotulo}': o clarao so acende no beat que ASSUME a forma");

			// ============================ A CRATERA E O INSTANTE DA TROCA -- SEMPRE ============================
			// O pedido do dono foi textual: *"tem transformacao q estao criando a cratera no meio da
			// cinematica (deveria ser sempre no final, assim q se transformar cria a cratera)"*. Doze das
			// trinta e duas cenas erravam, e as doze saiam de QUATRO linhas -- ou seja o defeito era a
			// cratera ser um campo livre do beat, e nao as cenas.
			//
			// Quem garante hoje e o funil da `Cinematica.Beats` (ver o Core), e ele garante por
			// construcao: isto aqui NAO conserta nada, so testemunha que o funil continua no caminho. E
			// nao e checagem de decoracao -- o jeito de o funil sumir e alguem trocar o `init` do `Beats`
			// por uma atribuicao direta "porque estava sobrando", e nesse instante as trinta e duas cenas
			// voltam a ter cratera onde o roteiro escrever. Sao TRES afirmacoes e cada uma cai sozinha:
			//
			//   1. EXATAMENTE UMA cratera por cena. Nao zero (uma transformacao abre o chao) e nao duas.
			//   2. Ela mora NO beat que assume.
			//   3. NENHUMA poeira antes dele -- ver o pedido irmao, *"a dust cloud ... deveria apenas vir
			//      quando a animacao cria uma cratera"*. Depois do beat que assume ela pode ficar: e a
			//      poeira DA cratera baixando, na cauda de assentamento que a checagem de cima ja limita
			//      a um beat.
			//
			// NAS DUAS VERSOES, porque quem confere e esta funcao compartilhada -- e a encurtada e
			// reconstruida pelo `Encurtar`, que passa pelo mesmo funil.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire o funil do `Cinematica.Beats` e as doze cenas antigas
			// caem na hora; escreva `| Efeito.Cratera` num beat de meio de cena e ele cai na (2).
			// =============================================================================================
			int crateras = c.Beats.Count(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Cratera));
			Conferir(crateras == 1, $"cena de '{rotulo}': UMA cratera, e uma so ({crateras})");
			Conferir(c.Beats.All(b => !b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Cratera)
								   || b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir)),
					 $"cena de '{rotulo}': a cratera cai NO beat que assume a forma, e em nenhum outro");

			int poeiraSolta = c.Beats
				.TakeWhile(b => !b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir))
				.Count(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Poeira));
			Conferir(poeiraSolta == 0,
					 $"cena de '{rotulo}': nenhuma poeira ANTES da cratera ({poeiraSolta} beat(s))");

			// ============================ E O AVESSO: ONDE HA CRATERA, HA POEIRA ============================
			// A linha de cima diz onde a poeira NAO pode estar. Faltava a que diz onde ela TEM que estar,
			// e as duas sao a MESMA frase do dono lida pelos dois lados -- *"a dust cloud ... deveria
			// apenas vir quando a animacao cria uma cratera"* nao e so "poeira nunca sem cratera", e
			// tambem "cratera nunca sem poeira", porque o que ele descreve e um acontecimento unico.
			//
			// O DEFEITO QUE ESTA PEGA E O SILENCIOSO DOS DOIS. O funil escreve as duas juntas
			// (`Chao = Cratera | Poeira`); basta o `| Efeito.Poeira` cair de la -- uma "simplificacao"
			// de uma linha -- pro chao passar a abrir SEM levantar terra. Nenhuma checagem anterior
			// via isso: `crateras == 1` continua verde, "a cratera cai no beat que assume" continua
			// verde, "nenhuma poeira antes" fica MAIS verde ainda. E em jogo o buraco aparecendo seco
			// le como cratera, nao como efeito faltando.
			//
			// A TERCEIRA E DA CENA INTEIRA, e ela pega o que as duas de beat nao alcancam: uma cena SEM
			// beat que assume (que o funil deixa de proposito sem cratera nenhuma, ver o comentario de
			// la) cujos beats de cauda carreguem poeira escrita a mao. Ali a poeira baixaria de um
			// buraco que nunca se abriu.
			//
			// COMO REPROVAM SE A REGRA SUMIR: tire o `| Efeito.Poeira` do `Chao` do funil e a primeira
			// cai nas 64; troque o `faz |= Chao` por `faz |= Efeito.Poeira` (cratera fora) e caem a
			// primeira e a terceira juntas.
			// =============================================================================================
			Conferir(c.Beats.All(b => !b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Cratera)
								   || b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Poeira)),
					 $"cena de '{rotulo}': o beat da cratera LEVANTA a poeira dela");

			bool algumaPoeira = c.Beats.Any(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Poeira));
			bool algumaCratera = c.Beats.Any(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Cratera));
			Conferir(!algumaPoeira || algumaCratera,
					 $"cena de '{rotulo}': nao ha poeira nenhuma sem cratera nenhuma");

			// ============================ TODO `Som` DE BEAT TEM QUE RESOLVER ============================
			// Um som e a coisa mais facil de "ligar" sem ligar, e esta bancada ja tinha a checagem pra
			// MUSICA e nao pra EFEITO. A diferenca importava: o `Transformacao.Som` tinha um
			// `_ => Trilha.Dash` no fim, entao um nome sem caso nao dava silencio (que se percebe) --
			// dava o swoosh do dash, que passa por som certo. Era o caso do `rockmoving` das cenas do
			// SSJ1 e do SSJ2: arquivo convertido, importado, pedido pelo beat, e nunca tocado.
			//
			// COMO ELA REPROVA SE A REGRA SUMIR: a pergunta e feita pelo MESMO resolvedor que o jogo
			// usa (`Transformacao.CaminhoDoSom`). Devolver o fallback de novo faria todo nome passar
			// e esta checagem viraria decoracao -- por isso o resolvedor devolve NULO pro desconhecido,
			// e um beat com nome novo sem caso reprova aqui antes de chegar no ouvido de alguem.
			// ========================================================================================
			foreach (Jandirus.Core.Forms.Beat b in c.Beats)
			{
				if (b.Som.Length == 0) continue;
				string? somCaminho = Transformacao.CaminhoDoSom(b.Som);
				Conferir(somCaminho != null, $"cena de '{rotulo}': o som '{b.Som}' tem caso no resolvedor");
				if (somCaminho != null)
					Conferir(ResourceLoader.Exists(somCaminho),
							 $"cena de '{rotulo}': o arquivo de '{b.Som}' existe ({somCaminho.GetFile()})");
			}
		}

		var vistas = new HashSet<string>();
		foreach (Jandirus.Core.Forms.Cinematica c in Jandirus.Core.Forms.Cinematicas.Todas)
		{
			vistas.Add(c.Forma);
			Conferir(Jandirus.Core.Forms.Catalogo.Def(c.Forma) != null,
					 $"cena de '{c.Forma}': a forma existe no catalogo");

			// A MUSICA TEM QUE EXISTIR. Um caminho errado nao derruba nada -- a cena so fica muda.
			if (c.Musica.Length > 0)
			{
				string caminho = $"res://Assets/Sounds/Music/{c.Musica}";
				Conferir(ResourceLoader.Exists(caminho), $"cena de '{c.Forma}': a musica existe ({c.Musica})");
			}

			// ============================ FAISCA NA CENA X FAISCA NO CATALOGO ============================
			// A regra e do dono e ele a disse do SSJ1: *"ssj n tem efeitos de raio"*. O catalogo ja
			// concordava (`ssj1` tem `Raios = 0`) -- o que soltava faisca era a CENA, ou seja a mesma
			// regra escrita em dois lugares. Escrita duas vezes, ela envelheceu: `ssj4` e `ui_sign`
			// tambem tem `Raios = 0` e as cenas dos dois soltavam faisca.
			//
			// Hoje quem decide e o `Cinematicas.Faisca`, que le o catalogo. Esta linha e o que
			// impede a decisao de voltar pra mao de quem escreve a proxima cena -- e o defeito que
			// ela pega e invisivel em jogo: faisca a mais parece efeito, nao erro.
			// =========================================================================================
			// NOS DOIS SENTIDOS, e o segundo nao e simetria de enfeite: uma forma com faisca cuja
			// cena nao solta nenhuma tambem e desencontro -- so que desse lado ninguem estranha,
			// porque faltar efeito na estreia le como cena discreta.
			int raiosDaForma = Jandirus.Core.Forms.Catalogo.Def(c.Forma)?.Raios ?? 0;
			int beatsComFaisca = c.Beats.Count(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Raios));
			Conferir(raiosDaForma > 0 ? beatsComFaisca > 0 : beatsComFaisca == 0,
					 $"cena de '{c.Forma}': a faisca casa com o catalogo "
				   + $"(Raios={raiosDaForma}, {beatsComFaisca} beat(s) com raio)");

			// ============================ CENA DE FORMA PRENDE O CORPO. SEM EXCECAO ============================
			// O `ConferirRoteiro` cobra "o corpo fica preso ATE o beat que assume, sem folga" -- e essa
			// regra tem uma saida que ninguem tinha fechado: `SegundosPreso = 0` num roteiro que assume
			// aos 0,0 s tambem casa com um `==`. Ela nao era alcancavel enquanto TODA cena era de forma;
			// passou a ser no dia em que nasceu uma cena que legitimamente nao prende (a furia).
			//
			// A furia nao passa por aqui (ela nao esta na `Todas`, ver `Cinematicas.Furia`), e esta linha
			// e o que garante que nenhuma das 34 aprenda o truque dela: transformar para o corpo, e o
			// dono decidiu isso com todas as letras (*"no dm e o tempo inteiro da transformaçao parado"*).
			// =============================================================================================
			Conferir(c.SegundosPreso > 0, $"cena de '{c.Forma}': prende o corpo ({c.SegundosPreso:0.###}s)");

			ConferirRoteiro(c, c.Forma);
			ConferirRoteiro(Jandirus.Core.Forms.Cinematicas.Encurtada(c), $"{c.Forma} curta");
		}

		// ============================ A CENA DA FURIA, PELA MESMA REGUA DAS 34 ============================
		// Ela nao esta na `Todas` de proposito (nao e de forma nenhuma -- nao veste cabelo, nao entra no
		// `Encurtar`, nao responde ao `Cinematicas.Para`), e por isso ela ficava FORA desta varredura: as
		// quatro regras de cena valiam pra ela por uma copia escrita a mao na bancada `raiva` [10].
		//
		// Aqui ela entra pelo caminho certo -- a MESMA funcao, com a unica regra de forma desligada (ver
		// o `prendeOCorpo`). O que isso compra e uma coisa so, e ela e o motivo: **a regra que alguem
		// escrever amanha ali dentro passa a valer pra ela sozinha**. Sem isto, a proxima cena avulsa
		// nasce com a mesma divida que esta tinha.
		//
		// SEM A ENCURTADA: a furia nao tem versao curta. O `Encurtar` existe pra cena de forma que se
		// repete ate a maestria dispensa-la (`Cinematicas.Encurtada`), e a furia nao se repete nem se
		// domina -- ela tem a propria recarga (`SegundosEntreFurias`). Encurta-la aqui testaria uma cena
		// que o jogo nunca toca.
		// ==============================================================================================
		ConferirRoteiro(Jandirus.Core.Forms.Cinematicas.Furia, "furia", prendeOCorpo: false);

		AsTresDuracoes();

		// ============================ TODA FORMA TEM CENA, E CENA PROPRIA ============================
		// Esta checagem era quase decorativa: `Cinematicas.Para` tinha um fallback por `Ordem` que
		// NUNCA devolvia nulo, entao ela passava mesmo com 26 das 35 formas estreando com a cena de
		// outra. O fallback foi deletado e o nulo virou possivel -- e a linha abaixo passou a valer.
		//
		// A DE BAIXO E A QUE IMPORTA AGORA: nao basta ter cena, tem que ser a DELA. Duas formas
		// apontando pro mesmo objeto e o fallback voltando com outro nome -- e o defeito nao aparece
		// em jogo, porque uma cena emprestada roda, prende o corpo e faz barulho igual.
		//
		// A UNICA excecao e a linha do Oozaru, que compartilha de proposito (o dono: *"o resto da
		// cinematica do oozaru pode deixar"*), e por isso ela e contada a parte.
		// =========================================================================================
		int semCena = 0, emprestadas = 0;
		var donoDaCena = new Dictionary<Jandirus.Core.Forms.Cinematica, string>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Id == Jandirus.Core.Forms.Catalogo.IdBase) continue;
			Jandirus.Core.Forms.Cinematica? c = Jandirus.Core.Forms.Cinematicas.Para(d);
			if (c == null) { semCena++; continue; }
			if (d.Linha == Jandirus.Core.Forms.LinhaDeForma.Oozaru) continue;
			if (donoDaCena.TryGetValue(c, out string? jaEDe))
			{
				emprestadas++;
				Conferir(false, $"`{d.Id}` estrearia com a cena de `{jaEDe}` (cena emprestada)");
			}
			else donoDaCena[c] = d.Id;
		}
		Conferir(semCena == 0, $"toda forma tem cena -- {semCena} sem");
		Conferir(emprestadas == 0, $"nenhuma forma pega a cena de outra ({emprestadas} emprestada(s))");

		NenhumaFormaCaiEmFallback();

		// ============================ E O MACACO USA A CENA DO MACACO ============================
		// A linha do Oozaru e a UNICA que compartilha cena de proposito -- o `oozaru_dourado` nao
		// tem roteiro proprio e usa o do `oozaru`, a pedido do dono. Quando havia fallback por
		// `Ordem`, esquecer esta regra dava um resultado especifico e ridiculo: `oozaru` e ordem 10
		// e `oozaru_dourado` e ordem 20, que caiam em `Ssj1` e `Ssj2`.
		//
		// COMO ELA REPROVA SE A REGRA SUMIR: apague o `if (d.Linha == LinhaDeForma.Oozaru)` de
		// `Cinematicas.Para` e o `oozaru_dourado` fica SEM CENA -- um macaco de dez metros nascendo
		// num quadro, que le como sprite errado e nao como transformacao. Nada em jogo daria erro.
		//
		// PELA LINHA e nao por dois ids: o `Oozaru LSSJ.dmi` do DU esta convertido e sem entrada no
		// catalogo, e o dia em que ele virar uma terceira entrada esta checagem ja o cobre.
		// ======================================================================================
		int daFera = 0;
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			bool ehFera = d.Linha == Jandirus.Core.Forms.LinhaDeForma.Oozaru;
			Jandirus.Core.Forms.Cinematica? qual = Jandirus.Core.Forms.Cinematicas.Para(d);
			if (ehFera)
			{
				daFera++;
				Conferir(qual == Jandirus.Core.Forms.Cinematicas.Oozaru,
						 $"`{d.Id}` (ordem {d.Ordem}) usa a cena do Oozaru -- pegou a de '{qual?.Forma}'");
			}
			// E O AVESSO, que e o que impede a checagem acima de passar num `Para` que devolvesse a
			// cena do macaco pra TODO MUNDO: quem nao e da linha nao pode cair nela.
			else
				Conferir(qual != Jandirus.Core.Forms.Cinematicas.Oozaru,
						 $"`{d.Id}` NAO pega a cena do macaco (ele nao e da linha Oozaru)");
		}
		Conferir(daFera >= 2, $"a linha do Oozaru tem entradas pra conferir ({daFera})");

		// E ELA E MUDA DE PROPOSITO. A faixa de transformacao ABAFA a de batalha pelo tempo dela
		// (ver `Cinematica.Musica`), e virar Oozaru nao acaba a briga -- comeca a pior parte dela.
		// Escrito como checagem e nao como comentario porque e uma decisao que so se percebe jogando:
		// quem puser musica aqui vai achar que melhorou.
		Conferir(Jandirus.Core.Forms.Cinematicas.Oozaru.Musica.Length == 0,
				 "a cena do Oozaru continua sem musica (senao ela abafa a trilha da luta)");

		// ============================ E ELA CONTINUA SEM LEVANTAR PEDRA ============================
		// *"oozaru n tem esse efeito de rocks nem de particulas, o resto da cinematica do oozaru pode
		// deixar"*. A pergunta mudou de lugar neste passe e a checagem foi atras dela: a pedra deixou
		// de ser um bit de beat e virou o estado `Cinematica.OChaoSeSolta`, derivado do CATALOGO
		// (`Catalogo.NaoSeSobePraEla` -- a linha do Oozaru inteira).
		//
		// PERGUNTAR AO CAMPO DERIVADO E MELHOR QUE CONTAR BEATS, e por um motivo concreto: o
		// `oozaru_dourado` divide esta cena hoje, mas no dia em que ele ganhar a dele a resposta certa
		// ja vem junto -- e a antiga (contar beats de uma cena so) ficaria verde com o macaco dourado
		// levantando pedra.
		//
		// A metade AO VIVO (a cena inteira rodada sem uma pedra nascer) esta mais abaixo, e ela e que
		// prova que o `false` daqui chega no tocador.
		// ========================================================================================
		Conferir(!Jandirus.Core.Forms.Cinematicas.Oozaru.OChaoSeSolta,
				 "a cena do Oozaru nao solta o chao (o dono cortou as pedras dela)");
		foreach (string idDaFera in new[] { "oozaru", "oozaru_dourado" })
			if (Jandirus.Core.Forms.Cinematicas.De(idDaFera) is { } daFeraCena)
				Conferir(!daFeraCena.OChaoSeSolta,
						 $"...e `{idDaFera}` cai na mesma resposta pelo catalogo, sem lista de isentos");

		// ============================ E TODAS AS OUTRAS SOLTAM ============================
		// *"deveria ter mais `rising rocks.png` q ficariam do INICIO AO FIM em TODAS as
		// transformacoes"*. O "todas" e literal: antes deste passe onze cenas nao levantavam pedra
		// nenhuma, e uma delas (`ui_sign`) por DECISAO ESCRITA minha -- "o -Sign- e a contencao, ele
		// nao levanta pedra". A decisao era minha e o dono disse todas; ela caiu.
		//
		// A checagem e o contrario da de cima e vale a mesma coisa: sem ela, `OChaoSeSolta` podendo
		// devolver falso pra qualquer forma deixaria o efeito sumir de uma cena sem ninguem ver.
		foreach (Jandirus.Core.Forms.Cinematica cSolta in Jandirus.Core.Forms.Cinematicas.Todas)
			if (!Jandirus.Core.Forms.Catalogo.NaoSeSobePraEla(
					Jandirus.Core.Forms.Catalogo.Def(cSolta.Forma)))
				Conferir(cSolta.OChaoSeSolta, $"cena de '{cSolta.Forma}': o chao se solta nela");

		// ============================ E NENHUMA DELAS QUEBRA CENARIO ============================
		// *"vc colocou uns efeitos de particula nas cinematicas q parecem q tem uns quadrados marrons
		// caindo e criando uma fumaca parecendo q quebrou uma parede ou objeto, TIRE esse efeito"*.
		//
		// O `Efeito.Cascalho` foi aposentado, entao o COMPILADOR ja e a primeira barreira -- mas ele
		// so cobre o bit, e nao a chamada: nada impede alguem de por `PoeiraDeEstrago.Soltar` direto
		// no `Disparar`, que e exatamente como o efeito chegou ali da primeira vez. Quem prova que
		// ninguem chamou e o contador da PROPRIA `PoeiraDeEstrago`, medido em volta de cada cena --
		// ver o laco das 64 cenas, mais abaixo.
		//
		// AQUI SO FICA A AFIRMACAO SOBRE O SISTEMA: ele continua vivo e inteiro. Cortar o efeito da
		// cinematica nao podia virar mutilar o estrago de cenario, que tem dono proprio e roda em
		// combate.
		Conferir(Jandirus.Client.PoeiraDeEstrago.MaxVivos > 0,
				 $"a `PoeiraDeEstrago` continua inteira ({Jandirus.Client.PoeiraDeEstrago.MaxVivos} vivos) "
			   + "-- o corte foi na CHAMADA da cinematica, nao no sistema de estrago");

		// O SSJ3 E A CENA FALADA. Se ela perder as falas, perde o que a torna memoravel.
		int falas = Jandirus.Core.Forms.Cinematicas.Ssj3.Beats.Count(b => b.Fala.Length > 0);
		Conferir(falas >= 8, $"a cena do SSJ3 tem as falas do 'ainda mais alem' ({falas})");
		Conferir(Jandirus.Core.Forms.Cinematicas.Ssj3.Beats.Any(b => b.Fala.Contains("ALÉM")),
				 "a fala 'AINDA MAIS ALÉM' esta la");

		_passos.Add($"  --     {vistas.Count} cenas proprias; a mais longa tem "
				  + $"{Jandirus.Core.Forms.Cinematicas.Todas.Max(c => c.Segundos):0.#}s");

		OPrazoCasaComOBeatNoFunil();
		OFunilDaCrateraReprovaODefeito();
	}

	// =====================================================================
	// 2b-bis. O FUNIL DA CRATERA, COM O DEFEITO INJETADO
	// =====================================================================
	/// <summary>
	/// A REGRA DA CRATERA MEDIDA NUM ROTEIRO ESCRITO ERRADO DE PROPOSITO.
	///
	/// ============================ POR QUE ESTA PRECISA EXISTIR ============================
	/// As checagens do <c>ConferirRoteiro</c> varrem as 64 cenas de producao -- e as 64 ja sairam
	/// LIMPAS deste passe: nenhum roteiro do arquivo escreve `Efeito.Cratera`, e a poeira so aparece
	/// em beat de cauda. Ou seja, hoje elas confirmam que o resultado esta certo e nao chegam a
	/// exercitar o funil: se o `ACrateraECoisaDoInstanteDaTroca` virasse a funcao identidade, o unico
	/// sinal seria a cratera SUMIR (`crateras == 1` cai) -- e o dia em que alguem "consertar" isso
	/// devolvendo `| Efeito.Cratera` aos roteiros, as 64 voltam a passar com a cratera onde o autor
	/// escrever, que e exatamente o estado que o dono reclamou.
	///
	/// Entao aqui a bancada ESCREVE o defeito que ele descreveu -- *"tem transformacao q estao criando
	/// a cratera no meio da cinematica"* -- e cobra que o funil o desfaca. Nenhuma cena de producao e
	/// tocada: os roteiros abaixo nascem e morrem dentro deste metodo.
	///
	/// ============================ OS QUATRO ROTEIROS, E O QUE CADA UM PROVA ============================
	///   1. CRATERA NO MEIO (o defeito literal do dono, e o dos 12 antigos): ela tem que sair do beat
	///      de meio e aparecer no que assume.
	///   2. POEIRA SOLTA ANTES (o *"colocou ela de mais durante as cinematicas"*): apagada.
	///   3. DUAS CRATERAS, uma antes e uma depois: sobra UMA, e no lugar certo. Este e o unico que pega
	///      um funil que so soubesse ACRESCENTAR sem apagar.
	///   4. ROTEIRO SEM `Assumir`: cratera nenhuma e poeira nenhuma -- o funil nao inventa um instante
	///      de troca que a cena nao tem. Sem esta, um funil que puzesse a cratera "no ultimo beat, se
	///      nao houver quem assuma" esconderia a cena defeituosa em vez de deixa-la reprovar.
	///
	/// ============================ E A IDEMPOTENCIA, QUE E O QUE O `Encurtar` USA ============================
	/// O `Cinematicas.Encurtar` reconstroi uma cena JA funilada (`new Cinematica { Beats = ... }` sobre
	/// beats que ja passaram por aqui). Um funil que acumulasse -- que somasse poeira a cada passada,
	/// por exemplo -- daria uma curta diferente da cheia sem nada reprovar, porque as duas continuariam
	/// obedecendo a todas as regras de cima. Por isso a segunda passada e conferida, e por igualdade de
	/// mascara e nao por "continua valendo a regra".
	/// ==============================================================================================
	/// </summary>
	private void OFunilDaCrateraReprovaODefeito()
	{
		const Jandirus.Core.Forms.Efeito Cr = Jandirus.Core.Forms.Efeito.Cratera;
		const Jandirus.Core.Forms.Efeito Po = Jandirus.Core.Forms.Efeito.Poeira;
		const Jandirus.Core.Forms.Efeito As = Jandirus.Core.Forms.Efeito.Assumir;

		// O `Beats` de uma `Cinematica` E o funil (ele mora no `init`), entao montar a cena JA e
		// aplica-lo. Nao ha como pedir "o roteiro cru" de volta -- e e essa a garantia que o Core da.
		static Jandirus.Core.Forms.Cinematica Cena(params Jandirus.Core.Forms.Beat[] bs) =>
			new() { Forma = "teste_do_funil", SegundosPreso = 20.0, Beats = bs };

		// --- 1. A CRATERA ESCRITA NO MEIO ------------------------------------
		Jandirus.Core.Forms.Cinematica m = Cena(
			new(2.0, Jandirus.Core.Forms.Efeito.AuraGrande),
			new(10.0, Cr | Po | Jandirus.Core.Forms.Efeito.Tremor),   // o defeito do dono, literal
			new(20.0, As),
			new(22.0, Jandirus.Core.Forms.Efeito.Tremor));
		Conferir(!m.Beats[1].Faz.HasFlag(Cr) && !m.Beats[1].Faz.HasFlag(Po),
				 $"funil: a cratera escrita no MEIO sai de la ({m.Beats[1].Faz})");
		Conferir(m.Beats[1].Faz.HasFlag(Jandirus.Core.Forms.Efeito.Tremor),
				 $"funil: ...e o resto daquele beat fica intacto ({m.Beats[1].Faz})");
		Conferir(m.Beats[2].Faz.HasFlag(Cr) && m.Beats[2].Faz.HasFlag(Po),
				 $"funil: ela reaparece no beat que ASSUME, com a poeira ({m.Beats[2].Faz})");
		Conferir(m.Beats.Count(b => b.Faz.HasFlag(Cr)) == 1,
				 "funil: e sobra uma cratera so no roteiro inteiro");

		// --- 2. A POEIRA SOLTA, LONGE DE QUALQUER CRATERA --------------------
		Jandirus.Core.Forms.Cinematica p = Cena(
			new(1.0, Po), new(5.0, Po), new(12.0, Po | Jandirus.Core.Forms.Efeito.Raios),
			new(20.0, As), new(22.0, Po));
		Conferir(p.Beats.Take(3).All(b => !b.Faz.HasFlag(Po)),
				 "funil: as tres poeiras soltas ANTES da troca foram apagadas");
		Conferir(p.Beats[2].Faz.HasFlag(Jandirus.Core.Forms.Efeito.Raios),
				 $"funil: ...e a faisca que dividia o beat com ela ficou ({p.Beats[2].Faz})");
		Conferir(p.Beats[4].Faz.HasFlag(Po) && !p.Beats[4].Faz.HasFlag(Cr),
				 $"funil: a da CAUDA fica (e a poeira baixando), sem cratera junto ({p.Beats[4].Faz})");

		// --- 3. DUAS CRATERAS, DOS DOIS LADOS DA TROCA -----------------------
		// O caso que separa "o funil poe" de "o funil MANDA": um que so soubesse acrescentar deixaria
		// as tres em pe, e a cena teria buraco antes, no meio e depois da transformacao.
		Jandirus.Core.Forms.Cinematica d = Cena(
			new(3.0, Cr | Po), new(20.0, As), new(24.0, Cr | Po));
		Conferir(d.Beats.Count(b => b.Faz.HasFlag(Cr)) == 1,
				 $"funil: das duas crateras escritas a mao sobra UMA "
			   + $"({d.Beats.Count(b => b.Faz.HasFlag(Cr))})");
		Conferir(d.Beats[1].Faz.HasFlag(Cr), "funil: e a que sobrou e a do beat que assume");
		Conferir(!d.Beats[0].Faz.HasFlag(Po), "funil: a poeira de antes da troca saiu com a cratera dela");

		// --- 4. SEM QUEM ASSUMA, NAO SE INVENTA INSTANTE ---------------------
		Jandirus.Core.Forms.Cinematica s = Cena(new(2.0, Cr | Po), new(10.0, Po));
		Conferir(s.Beats.All(b => !b.Faz.HasFlag(Cr) && !b.Faz.HasFlag(Po)),
				 "funil: roteiro SEM beat que assume fica sem cratera e sem poeira "
			   + "-- a cena defeituosa reprova em vez de se disfarcar");

		// --- 5. A SEGUNDA PASSADA NAO ACUMULA (o que o `Encurtar` faz) -------
		Jandirus.Core.Forms.Cinematica outraVez = Cena([.. m.Beats]);
		Conferir(outraVez.Beats.Length == m.Beats.Length
				 && !outraVez.Beats.Where((b, i) => b.Faz != m.Beats[i].Faz).Any(),
				 "funil: reconstruir uma cena JA funilada da exatamente a mesma cena (idempotente)");
	}

	// =====================================================================
	// 2c. NENHUMA FORMA CAI EM FALLBACK
	// =====================================================================
	/// <summary>
	/// O CATALOGO INTEIRO, PROCURANDO CENA EMPRESTADA -- por quatro perguntas diferentes.
	///
	/// ============================ POR QUE QUATRO E NAO UMA ============================
	/// O fallback antigo era um `switch (d.Ordem)` que devolvia a cena de OUTRA forma, e ele
	/// sobreviveu a bancada por muito tempo porque a unica pergunta que se fazia era "devolveu
	/// alguma coisa?" -- e ele SEMPRE devolvia. Vinte e seis formas estreavam com o tema e as falas
	/// de outra, e passava verde.
	///
	/// Ele foi deletado. Estas linhas existem pra ele nao voltar com outra cara, e cada uma pega uma
	/// cara diferente:
	///
	///   1. **A cena diz o nome de quem ela e** (`c.Forma == d.Id`). Um fallback que devolva a cena
	///      pronta de outra forma cai aqui na hora. A checagem "cena emprestada" logo acima so o
	///      pegaria se DUAS formas caissem na MESMA cena -- um fallback que atendesse uma unica forma
	///      passaria por ela inteirinho.
	///   2. **A cena e uma das de `Todas`**. Um fallback que MONTE uma cena na hora (`new Cinematica`,
	///      ou uma das fabricas chamada dentro do `Para`) escapa da 1 e da 2 anterior -- ele traz o id
	///      certo e nao repete objeto. So que `Cinematicas.Curtas` e uma tabela FECHADA, montada na
	///      carga da classe: uma cena de fora dela nunca tem encurtada pronta, e `Encurtada` passa a
	///      devolver um objeto NOVO a cada chamada -- o que quebra a comparacao por referencia que o
	///      resto deste sistema faz.
	///   3. **`De` e `Para` so divergem na linha do macaco**. Esta e a mais direta das quatro: se
	///      `De(id)` (que so olha a tabela) devolve nulo e `Para(def)` devolve cena, alguma regra
	///      inventou uma resposta -- e isso E um fallback, chame-se como se chamar.
	///   4. **Nenhuma cena de `Todas` esta orfa**: toda cena escrita e a que `Para` entrega pra a
	///      forma dela. Uma cena que ninguem alcanca e trabalho perdido que ninguem percebe.
	/// ==============================================================================
	///
	/// A CONTAGEM NO FIM NAO E ENFEITE: as quatro varreduras sao `foreach` sobre o catalogo, e um
	/// catalogo vazio (ou um `Todas` vazio) faria as quatro passarem sem olhar nada. Ja aconteceu
	/// neste projeto -- ver o "o boneco precisa ter rabo antes" la embaixo.
	/// </summary>
	private void NenhumaFormaCaiEmFallback()
	{
		int percorridas = 0, idErrado = 0, foraDaTabela = 0, inventadas = 0;
		string exId = "", exFora = "", exInv = "";

		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Id == Jandirus.Core.Forms.Catalogo.IdBase) continue;
			percorridas++;

			Jandirus.Core.Forms.Cinematica? c = Jandirus.Core.Forms.Cinematicas.Para(d);
			if (c == null) continue;   // ja contado por "toda forma tem cena"

			// A LINHA DO MACACO E A UNICA EXCECAO, e ela e do dono. Ela entra nas perguntas 2 e 4, e
			// fica de fora da 1 e da 3 -- que sao justamente as que ela violaria de proposito.
			bool fera = d.Linha == Jandirus.Core.Forms.LinhaDeForma.Oozaru;

			if (!fera && c.Forma != d.Id)
			{
				idErrado++;
				if (exId.Length == 0) exId = $"`{d.Id}` recebeu a cena de `{c.Forma}`";
			}

			if (Array.IndexOf(Jandirus.Core.Forms.Cinematicas.Todas, c) < 0)
			{
				foraDaTabela++;
				if (exFora.Length == 0) exFora = $"`{d.Id}` recebeu uma cena que nao esta em `Todas`";
			}

			if (Jandirus.Core.Forms.Cinematicas.De(d.Id) is { } propria)
			{
				if (!ReferenceEquals(propria, c))
				{
					inventadas++;
					if (exInv.Length == 0) exInv = $"`{d.Id}`: `De` e `Para` devolvem cenas diferentes";
				}
			}
			else if (!fera)
			{
				// `De` NAO ACHOU e `Para` ACHOU: alguem inventou a resposta. Isto e o fallback,
				// qualquer que seja a forma que ele tome.
				inventadas++;
				if (exInv.Length == 0) exInv = $"`{d.Id}` nao tem cena na tabela e mesmo assim `Para` devolveu uma";
			}
		}

		Conferir(percorridas >= 30,
				 $"a varredura de fallback olhou o catalogo inteiro ({percorridas} formas)");
		Conferir(idErrado == 0, $"nenhuma cena chega numa forma com o nome de outra ({idErrado}: {exId})");
		Conferir(foraDaTabela == 0,
				 $"toda cena entregue e uma das de `Todas` -- nenhuma montada na hora ({foraDaTabela}: {exFora})");
		Conferir(inventadas == 0,
				 $"`De` e `Para` so divergem na linha do macaco ({inventadas}: {exInv})");

		// ============================ E NADA ESCRITO FICA INALCANCAVEL ============================
		// O avesso das tres de cima. Uma cena em `Todas` cuja forma nao existe (ou que o `Para` nao
		// entrega) e uma cena escrita, revisada, com musica e falas, que nunca vai rodar -- e nada em
		// jogo diria isso. Duas entradas com o MESMO `Forma` produzem exatamente esse estado: `De` faz
		// `Array.Find`, pega a primeira, e a segunda vira letra morta.
		// ====================================================================================
		int orfas = 0;
		string exOrfa = "";
		foreach (Jandirus.Core.Forms.Cinematica c in Jandirus.Core.Forms.Cinematicas.Todas)
		{
			FormaDef? dono = Jandirus.Core.Forms.Catalogo.Def(c.Forma);
			if (dono != null && ReferenceEquals(Jandirus.Core.Forms.Cinematicas.Para(dono), c)) continue;
			orfas++;
			if (exOrfa.Length == 0) exOrfa = $"a cena de `{c.Forma}` nao e entregue a ninguem";
		}
		Conferir(orfas == 0, $"nenhuma cena escrita fica inalcancavel ({orfas}: {exOrfa})");

		int nomes = Jandirus.Core.Forms.Cinematicas.Todas.Select(c => c.Forma).Distinct().Count();
		Conferir(nomes == Jandirus.Core.Forms.Cinematicas.Todas.Length,
				 $"nenhuma forma tem duas cenas em `Todas` ({nomes} nomes pra "
			   + $"{Jandirus.Core.Forms.Cinematicas.Todas.Length} entradas)");

		// ============================ A TEMPESTADE E DE UMA CENA SO ============================
		// *"o ssj1 na cinematica da PRIMEIRA VEZ"*. A varredura das 64 cenas (`NoCorpo`) mede cada uma
		// contra o proprio `OCeuDescarrega` dela, e por isso ela e verdadeira pra qualquer resposta que
		// a propriedade der -- se um dia ela devolvesse `true` pro catalogo inteiro, trinta e quatro
		// cinematicas ganhariam tempestade e o placar la continuaria verde.
		//
		// Esta linha pergunta a outra metade, e ela e uma AFIRMACAO sobre o recorte: uma cena, esta
		// cena, e a versao cheia dela. As tres partes estao aqui porque as tres foram pedidas, e cada
		// uma tem um jeito proprio de se perder:
		//   * `== 1` pega a propriedade generalizando (um `Musica.Length > 0` sozinho daria 34);
		//   * `ssj1` pega a propriedade mirando na forma errada;
		//   * a ENCURTADA falsa pega o "so na primeira vez" vazando -- que e o unico dos tres que o
		//     jogador veria como defeito de jogo (tempestade em toda transformacao) e nao como efeito.
		// ==================================================================================
		Jandirus.Core.Forms.Cinematica[] comCeu =
			[.. Jandirus.Core.Forms.Cinematicas.Todas.Where(c => c.OCeuDescarrega)];
		Conferir(comCeu.Length == 1 && comCeu[0].Forma == Jandirus.Core.Forms.Catalogo.IdSsj1,
				 "so a estreia do SSJ1 parte o ceu de ponta a ponta "
			   + $"({comCeu.Length}: {string.Join(", ", comCeu.Select(c => c.Forma))})");
		Conferir(comCeu.Length != 1
				 || !Jandirus.Core.Forms.Cinematicas.Encurtada(comCeu[0]).OCeuDescarrega,
				 "e a versao ENCURTADA dela nao parte -- a tempestade e da PRIMEIRA vez");
	}

	// =====================================================================
	// 2d. O PRAZO CASA COM O BEAT -- NO FUNIL QUE O JOGO USA
	// =====================================================================
	/// <summary>
	/// A REGRA "SOLTA O CORPO NO INSTANTE EM QUE A FORMA FICA", PERGUNTADA AO `NoDegrau`.
	///
	/// ============================ POR QUE DE NOVO, SE O `ConferirRoteiro` JA MEDE ============================
	/// Porque ele mede as cenas que estao em `Cinematicas.Todas`, e o que o jogo toca nao e uma cena de
	/// `Todas` -- e o que sai de `Cinematicas.NoDegrau(forma, degrau)`. Sao a mesma coisa hoje **por
	/// construcao**, e "por construcao" e exatamente o tipo de coisa que muda sem ninguem avisar: basta
	/// o `NoDegrau` ganhar um caso (uma cena mais curta pra quem esta em combate, um degrau novo) pra a
	/// cena tocada deixar de ser a cena conferida.
	///
	/// Entao aqui a pergunta e feita ao FUNIL, forma por forma, degrau por degrau -- as duas linhas que
	/// prendem o corpo de verdade.
	///
	/// COMO REPROVA SE A REGRA SUMIR: troque `Encurtar` pra tirar o `preso = nb.Em` (deixando o `alvo`
	/// cru) e o prazo da curta descola do beat por 5 milesimos -- nao pega. Troque pra `preso = alvo * 2`
	/// (ou esqueca de reescalar o prazo junto com os beats, que e o erro provavel) e cai aqui.
	/// ====================================================================================================
	/// </summary>
	private void OPrazoCasaComOBeatNoFunil()
	{
		int medidos = 0, semAssumir = 0, descasados = 0;
		string exDes = "";

		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Id == Jandirus.Core.Forms.Catalogo.IdBase) continue;
			foreach (Jandirus.Core.Forms.DegrauDeCena g in
					 new[] { Jandirus.Core.Forms.DegrauDeCena.Curta, Jandirus.Core.Forms.DegrauDeCena.Estreia })
			{
				if (Jandirus.Core.Forms.Cinematicas.NoDegrau(d, g) is not { } cena) continue;
				medidos++;

				double assume = -1;
				foreach (Jandirus.Core.Forms.Beat b in cena.Beats)
					if (b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir)) { assume = b.Em; break; }

				if (assume < 0) { semAssumir++; continue; }
				if (Math.Abs(cena.SegundosPreso - assume) < 0.51) continue;

				descasados++;
				if (exDes.Length == 0)
					exDes = $"`{d.Id}`/{g}: preso {cena.SegundosPreso:0.##}s, assume {assume:0.##}s";
			}
		}

		Conferir(medidos >= 60, $"o funil `NoDegrau` entregou cena nos dois degraus de {medidos / 2} formas");
		Conferir(semAssumir == 0, $"toda cena que o funil entrega tem o beat que ASSUME ({semAssumir} sem)");
		Conferir(descasados == 0, $"e o prazo casa com esse beat nas {medidos} ({descasados}: {exDes})");
	}

	/// <summary>
	/// OS TRES DEGRAUS DE DURACAO -- estreia / encurtada / instantanea.
	///
	/// ============================ POR QUE ISTO PRECISA DE BANCADA ============================
	/// A regra e do dono e ela e invisivel em jogo ate o momento exato em que esta errada: ninguem
	/// percebe que a segunda transformacao deveria ter sido mais curta -- percebe que "ficou preso de
	/// novo". E o degrau instantaneo, que e o mais importante dos tres, NAO TEM NADA NA TELA: ele e a
	/// ausencia de cena. Um bug que o transformasse em "cena curta" passaria por ritmo do jogo.
	///
	/// O que se confere aqui e a DERIVACAO, e nao os numeros: que a curta e mais curta que a cheia,
	/// que ela nao repete a musica da estreia, que o prazo dela continua casando com o beat que assume
	/// a forma, e que a excecao do Oozaru sai da LINHA (e nao de uma lista de dois ids que envelhece).
	/// =====================================================================================
	/// </summary>
	private void AsTresDuracoes()
	{
		var cheia = Jandirus.Core.Forms.Cinematicas.Ssj3;
		var curta = Jandirus.Core.Forms.Cinematicas.Encurtada(cheia);

		// --- 1. A CURTA E DERIVADA, e nao um segundo roteiro escrito a mao ---
		foreach (Jandirus.Core.Forms.Cinematica c in Jandirus.Core.Forms.Cinematicas.Todas)
		{
			var k = Jandirus.Core.Forms.Cinematicas.Encurtada(c);

			Conferir(k.SegundosPreso < c.SegundosPreso,
					 $"`{c.Forma}`: a curta prende MENOS que a estreia "
				   + $"({k.SegundosPreso:0.##}s contra {c.SegundosPreso:0.##}s)");

			// A MUSICA E DA ESTREIA. `ssj1_music_played` persiste no save do DM: o tema toca uma vez
			// na vida do personagem. Toca-lo a cada transformacao seria transformar o acontecimento em
			// toque de celular -- e nada em jogo acusaria, porque um tema tocando parece certo.
			Conferir(k.Musica.Length == 0, $"`{c.Forma}`: a curta NAO repete a musica da estreia");

			// AS FALAS TAMBEM. Ninguem explica pela terceira vez o que e um Super Saiyajin.
			Conferir(k.Beats.All(b => b.Fala.Length == 0 && b.Narra.Length == 0),
					 $"`{c.Forma}`: a curta nao repete as falas nem a narracao");

			// E A COREOGRAFIA FICA. Este e o "ainda assim vai ser lenda" do dono: a curta COMPRIME o
			// relogio, nao corta os efeitos. Se alguem trocar a compressao por um corte de beats, os
			// efeitos comecam a sumir e esta linha e a unica que percebe.
			int efeitosCheia = c.Beats.Count(b => b.Faz != Jandirus.Core.Forms.Efeito.Nada);
			int efeitosCurta = k.Beats.Count(b => b.Faz != Jandirus.Core.Forms.Efeito.Nada);
			Conferir(efeitosCurta == efeitosCheia,
					 $"`{c.Forma}`: a curta guarda TODOS os beats de efeito ({efeitosCurta} de {efeitosCheia})");

			// E ELA CABE NA FAIXA. Piso e teto existem porque o fator sozinho daria 1,6 s no SSJ4
			// (abaixo do 1,0 s que a bancada exige do beat que assume) e 12,8 s no SSJ3 (que e a mesma
			// espera de novo, so que sem musica).
			Conferir(k.SegundosPreso >= Jandirus.Core.Forms.Cinematicas.MinimoDaCurta - 0.01
				  && k.SegundosPreso <= Jandirus.Core.Forms.Cinematicas.MaximoDaCurta + 0.01,
					 $"`{c.Forma}`: a curta prende entre {Jandirus.Core.Forms.Cinematicas.MinimoDaCurta:0.#} e "
				   + $"{Jandirus.Core.Forms.Cinematicas.MaximoDaCurta:0.#}s (prende {k.SegundosPreso:0.##})");
		}

		// A MESMA CENA DUAS VEZES E O MESMO OBJETO. A tabela e pre-calculada de proposito (ver
		// `Cinematicas.Curtas`): um cache preenchido sob demanda seria escrita concorrente entre a
		// linha do servidor e a do cliente no modo `--host`, que e onde este jogo passa a vida.
		Conferir(ReferenceEquals(curta, Jandirus.Core.Forms.Cinematicas.Encurtada(cheia)),
				 "encurtar a mesma cena duas vezes devolve o MESMO objeto (a tabela e fechada)");

		// --- 2. QUEM CAI EM QUAL DEGRAU ---
		var ssj1 = Jandirus.Core.Forms.Catalogo.Def("ssj1")!;
		Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(ssj1, estreia: true, 0)
				 == Jandirus.Core.Forms.DegrauDeCena.Estreia, "estreia = cena CHEIA");
		Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(ssj1, estreia: false, 0)
				 == Jandirus.Core.Forms.DegrauDeCena.Curta, "sem maestria nenhuma = cena ENCURTADA");

		// AS DUAS BEIRADAS DO LIMIAR, e nao uma. Um `>` no lugar do `>=` (ou o contrario) passaria em
		// qualquer teste que so olhasse 0% e 100%.
		double corte = Jandirus.Core.Forms.Cinematicas.MaestriaQueDispensaCena;
		Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(ssj1, false, corte - 0.1)
				 == Jandirus.Core.Forms.DegrauDeCena.Curta, $"{corte - 0.1:0.#}% de maestria = ainda ENCURTADA");
		Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(ssj1, false, corte)
				 == Jandirus.Core.Forms.DegrauDeCena.Nenhuma, $"{corte:0}% cravados = INSTANTANEA");
		Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(ssj1, false, 100)
				 == Jandirus.Core.Forms.DegrauDeCena.Nenhuma, "forma dominada = INSTANTANEA");

		// E A ESTREIA GANHA DA MAESTRIA. Parece impossivel (nao se domina o que nunca se assumiu) e
		// nao e: o Oozaru Dourado dominado LIBERA o SSJ4 sem consumir a estreia, e o `DirectSSJ` do
		// Grade 4 leva a formas ja liberadas. Se a ordem dos `if` invertesse, o jogador perderia a
		// cena mais cara da linha Saiyajin sem nada na tela dizendo que perdeu.
		Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(ssj1, estreia: true, 100)
				 == Jandirus.Core.Forms.DegrauDeCena.Estreia, "a ESTREIA vence a maestria (o SSJ4 pelo Oozaru)");

		// A BASE NAO E CENA. Descer manda `para = base` pelo mesmo pacote, e um degrau que nao fosse
		// `Nenhuma` aqui prenderia o corpo do jogador toda vez que ele voltasse ao normal.
		Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(
					 Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdBase), true, 0)
				 == Jandirus.Core.Forms.DegrauDeCena.Nenhuma, "voltar pra BASE nunca tem cena");
		Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(null, true, 0)
				 == Jandirus.Core.Forms.DegrauDeCena.Nenhuma, "forma desconhecida nao prende ninguem");

		// --- 3. A EXCECAO DO OOZARU, PELA LINHA ---
		// O dono: "isso serve pra TODAS as formas do jogo, MENOS as oozaru e golden oozaru". Derivada
		// da `LinhaDeForma.Oozaru` e nao de dois ids: o `Oozaru LSSJ.dmi` do DU esta convertido e sem
		// entrada no catalogo, e no dia em que ele virar a terceira entrada esta checagem ja o cobre.
		int feras = 0, escada = 0;
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Id == Jandirus.Core.Forms.Catalogo.IdBase) continue;
			var degrauDominado = Jandirus.Core.Forms.Cinematicas.Degrau(d, estreia: false, 100);
			if (d.Linha == Jandirus.Core.Forms.LinhaDeForma.Oozaru)
			{
				feras++;
				Conferir(degrauDominado == Jandirus.Core.Forms.DegrauDeCena.Estreia,
						 $"`{d.Id}` MANTEM a cena cheia mesmo dominado (a cena E a transformacao)");
			}
			else
			{
				escada++;
				if (degrauDominado != Jandirus.Core.Forms.DegrauDeCena.Nenhuma) escada = -9999;
			}
		}
		Conferir(feras >= 2, $"a linha do Oozaru tem entradas pra conferir ({feras})");
		Conferir(escada > 0, "toda forma FORA da linha do Oozaru vira instantanea quando dominada");

		// --- 4. `NoDegrau` DEVOLVE A CENA CERTA PRA CADA DEGRAU ---
		int erros = 0;
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Id == Jandirus.Core.Forms.Catalogo.IdBase) continue;
			var cheiaDela = Jandirus.Core.Forms.Cinematicas.Para(d)!;
			if (Jandirus.Core.Forms.Cinematicas.NoDegrau(d, Jandirus.Core.Forms.DegrauDeCena.Nenhuma) != null) erros++;
			if (!ReferenceEquals(Jandirus.Core.Forms.Cinematicas.NoDegrau(
					d, Jandirus.Core.Forms.DegrauDeCena.Estreia), cheiaDela)) erros++;
			var kd = Jandirus.Core.Forms.Cinematicas.NoDegrau(d, Jandirus.Core.Forms.DegrauDeCena.Curta);
			if (kd == null || ReferenceEquals(kd, cheiaDela) || kd.SegundosPreso >= cheiaDela.SegundosPreso) erros++;
		}
		Conferir(erros == 0, $"`NoDegrau` casa com o degrau nas {Jandirus.Core.Forms.Catalogo.Todas.Length} formas ({erros} erros)");

		_passos.Add($"  --     tres degraus: SSJ3 prende {cheia.SegundosPreso:0.#}s na estreia, "
				  + $"{curta.SegundosPreso:0.#}s encurtada, 0s a partir de {corte:0}% de maestria");
	}

	// =====================================================================
	// 2e. A DURACAO DE CADA CENA E A DO DM
	// =====================================================================
	/// <summary>
	/// QUANTOS TIQUES O `proc` DO BYOND DORME, contado a mao, arquivo por arquivo.
	///
	/// ============================ POR QUE A TABELA E ESCRITA AQUI E NAO LIDA DO CORE ============================
	/// Este e o unico numero desta bancada que NAO pode ser derivado: perguntar ao `Cinematicas` quanto a
	/// cena dura e conferir a resposta com ela mesma aprovaria qualquer duracao. A fonte e o DM, e o DM
	/// nao esta em memoria -- entao ele e transcrito, com o arquivo e o `proc` de cada linha ao lado, e
	/// quem duvidar abre o arquivo e recontar leva um minuto.
	///
	/// E ela existe porque a compressao ja aconteceu UMA VEZ, calada, com argumento escrito: a cena do
	/// SSJ3 estava com 32 s e o `SSJ3Cinematic.dm` tem VINTE `sleep` somando 1400 tiques. Ninguem
	/// percebeu por meses -- uma cinematica curta demais nao da erro, da a impressao de ritmo. Sem uma
	/// tabela externa nao ha como uma bancada notar isso: ela veria 32 s e concordaria com 32 s.
	///
	/// ============================ EM TIQUE, E NAO EM SEGUNDO ============================
	/// `sleep(50)` sao 50 DECISSEGUNDOS = 5,0 s (ver `Jandirus.Core.TempoDoDm`). Escrever 5,0 aqui
	/// seria transcrever a MINHA divisao em vez do numero do arquivo -- e um erro de conta viraria a
	/// nova verdade. O tique e o que esta escrito no `.dm`; o segundo e derivado.
	///
	/// E ESSE ERRO DE CONTA ACONTECEU: esta bancada dividia por 12 (o `world.fps`) e por isso APROVOU
	/// as 25 cenas com todas elas 20% curtas. A tabela de tiques abaixo estava certa o tempo todo --
	/// o defeito morava no divisor, uma linha adiante. E por isso que ela e transcrita em TIQUE: com
	/// o segundo escrito aqui, nao haveria como o conserto do divisor ser um numero so.
	/// ======================================================================================================
	///
	/// SO O QUE DORME NA LINHA PRINCIPAL DA `proc` CONTA. Os `sleep(rand(3,10))` do piscar de cabelo e o
	/// `sleep(1)` do laco de feixes moram dentro de um `spawn` -- rodam EM PARALELO e nao adiam nada. Foi
	/// a primeira coisa que a conta errou (dava 251 no SSJ1 em vez de 250).
	/// </summary>
	private static readonly (string Cena, int Tiques, string Origem)[] OsSleepsDoDm =
	[
		// `SSJCinematic.dm`: sleep(50) + sleep(100) + sleep(100). O `move = 1` cai aos 150 tiques (:84) e
		// a proc ainda dorme 100 depois dele -- por isso o SSJ1 e a irma dele (a divina) sao mais longos
		// que o SSJ2, que ACABA no proprio `move = 1`. Nao e ritmo escolhido: e o arquivo.
		("ssj1",                        250, "cinematics/SSJCinematic.dm"),
		("future_ssj",                  250, "cinematics/SSJCinematic.dm (mesmo `SSj()`)"),
		("primal_c_type",               250, "cinematics/SSJCinematic.dm (mesmo `SSj()`)"),
		// O Blue nao tem proc propria: `SSj()` com `godki.usage` (supersaiyanbuff.dm:307) chama o MESMO
		// `SSJCinematic()`. Este era o erro mais caro da tabela antiga -- eu tinha racionalizado que "o
		// Blue e curto de proposito" e escrito 6 s onde o original tem 25,0.
		("blue",                        250, "supersaiyanbuff.dm:320 -> SSJCinematic.dm"),
		("rose",                        250, "supersaiyanbuff.dm:320 -> SSJCinematic.dm"),

		// `SSJ2Cinematic.dm`: sleep(50) + sleep(100), e a proc termina no `move = 1` (:46).
		("ssj2",                        150, "cinematics/SSJ2Cinematic.dm"),

		// `UltraSSJCinematic.dm`: sleep(50) + sleep(100). Mesma espinha, sem o `sleep(100)` final.
		("grade2",                      150, "cinematics/UltraSSJCinematic.dm"),
		("grade3",                      150, "cinematics/UltraSSJCinematic.dm"),
		("primal_legendary",            150, "cinematics/UltraSSJCinematic.dm"),
		("blue_evolution",              150, "cinematics/UltraSSJCinematic.dm"),
		("rose2",                       150, "cinematics/UltraSSJCinematic.dm"),

		// `SSJ3Cinematic.dm`: VINTE sleeps -- 10+60+50+20+50+40+50+20+30+50+50+100+100+100+130+100+140+
		// 100+100+100. Oito deles passam de 100 tiques e sao os silencios entre os gritos.
		("ssj3",                       1400, "cinematics/SSJ3Cinematic.dm (20 sleeps)"),

		// O SSJ4 nao ganhou arquivo: a cena mora no `if(firsttime<=3)` do proprio `SSj4()` --
		// sleep(50) + sleep(100) la dentro, e o `sleep(10)` de fora do bloco, antes do `ssj = 4`.
		// O `sleep(10)` conta porque a FORMA so fica depois dele.
		("ssj4",                        160, "supersaiyanbuff.dm:736 (`SSj4()`, firsttime)"),
		("primal_legendary4",           160, "supersaiyanbuff.dm:736 (`SSj4()`, firsttime)"),

		// `ui_grand_cinematic()` / `ue_grand_cinematic()`: 12 ciclos de sleep(10) + 10 ciclos de
		// sleep(10). Sao a mesma receita escrita duas vezes no DM, e por isso o mesmo numero.
		("ui_sign",                     220, "UltraInstinct.dm:326 (`ui_grand_cinematic`)"),
		("ui_perfected",                220, "UltraInstinct.dm:326 (`ui_grand_cinematic`)"),
		("ultra_ego",                   220, "UltraEgo.dm:415 (`ue_grand_cinematic`)"),

		// `lssj_grand_cinematic()`: 16 + 12 ciclos de sleep(10). A unica que este port NUNCA comprimiu
		// -- e por isso ela serve de afericao do metodo: se a tabela estivesse errada, esta erraria junto.
		("wrathful",                    280, "lssjbuff.dm:495 (`lssj_grand_cinematic`)"),
		("c_type",                      280, "lssjbuff.dm:495 (`lssj_grand_cinematic`)"),
		("legendary",                   280, "lssjbuff.dm:495 (`lssj_grand_cinematic`)"),
		("primal_legendary2",           280, "lssjbuff.dm:495 (`lssj_grand_cinematic`)"),

		// `INITIALIZEGODPROTOCOL()`: 20+30+40+10+10+10+10+15. A outra que nunca foi comprimida.
		("ssg",                         145, "GodRitual.dm:41 (`INITIALIZEGODPROTOCOL`)"),
		("rose_ssg",                    145, "GodRitual.dm:41 (`INITIALIZEGODPROTOCOL`)"),
		("mistico",                     145, "GodRitual.dm:41 (`INITIALIZEGODPROTOCOL`)"),

		// ============================ O FROST DEMON TEM AS DUAS CENAS DO ORIGINAL ============================
		// `Frost_Demon_Forms()` escolhe entre elas pelo ESTADO (Mutante entrando numa forma que nao
		// segura, primeira vez naquela forma), e nao pelo degrau. Aqui a cena e da FORMA -- mas os
		// PRAZOS sao os de la, os dois, e e isso que esta tabela cobra:
		//
		//   * `fd_burst_fx()` (`IcerTransform.dm:186`) -- `spawn(25)`, 2,5 s. E o surto das repeticoes,
		//     e e o que as quatro supressoes e a forma base usam: recolher a casca (ou abri-la) nao e
		//     virar nada, e no DM isso e literalmente mudo pro Frost Demon normal;
		//   * `fd_grand_cinematic()` (`IcerTransform.dm:196`) -- 16 ciclos de `sleep(10)` mais 12, ou
		//     seja 280 tiques. **E o mesmo numero do bloco Legendary logo acima, e nao por coincidencia**:
		//     o proprio DM diz na linha que a proc e a *"receita da lssj_grand_cinematica, recolorida"*.
		//     Por isso as duas evolucoes saem da MESMA fabrica (`LendaGrande`) e nao de uma copia.
		// ================================================================================================
		("frost1",                       25, "IcerTransform.dm:186 (`fd_burst_fx`, spawn(25))"),
		("frost2",                       25, "IcerTransform.dm:186 (`fd_burst_fx`, spawn(25))"),
		("frost3",                       25, "IcerTransform.dm:186 (`fd_burst_fx`, spawn(25))"),
		("frost4",                       25, "IcerTransform.dm:186 (`fd_burst_fx`, spawn(25))"),
		("frost5",                       25, "IcerTransform.dm:186 (`fd_burst_fx`, spawn(25))"),
		("frost6",                      280, "IcerTransform.dm:196 (`fd_grand_cinematic`)"),
		("frost7",                      280, "IcerTransform.dm:196 (`fd_grand_cinematic`)"),
	];

	/// <summary>
	/// AS CENAS QUE O DM NAO TEM -- invencao deste port, e cada uma sabe por que.
	///
	/// Elas ficam FORA da tabela de tiques e DENTRO desta, e nao simplesmente de fora das duas: uma cena
	/// sem entrada em lugar nenhum e indistinguivel de uma cena esquecida. Escrever a ausencia e o que
	/// permite a checagem "a tabela cobre o catalogo inteiro" existir.
	/// </summary>
	private static readonly (string Cena, string PorQue)[] AsCenasSemProcNoDm =
	[
		// Os quatro procs de surto do DM (`SSj4FP`, `SSj4FPLB`, `LSSj_Controlled`, `LSSj3_Primal`) tem
		// SETE linhas e um `sleep(8)` -- 0,8 s. Curto demais pra ler como cena; o port o alonga.
		("ssj4_full_power",                 "`SSj4FP` tem sleep(8) = 0,8 s -- curto demais pra cena"),
		("ssj4_limit_breaker",              "`SSj4FPLB` tem sleep(8) = 0,8 s"),
		// O `LSSj_Controlled` (a quarta proc de surto) NAO esta aqui: o `legendary_full_power` deixou
		// de ser forma -- ele e o `legendary` com a maestria em 100% -- e forma que nao existe nao
		// pode ter cena inventada nem ausencia declarada. Ver o bloco dele em `Formas.cs`.
		("primal_legendary3",               "`LSSj3_Primal` tem sleep(8) = 0,8 s"),
		("primal_legendary4_full_power",    "irmao do `SSj4FP`"),
		("primal_legendary4_limit_breaker", "irmao do `SSj4FPLB`"),
		("beast",                           "o Beast nasce da raiva e nao tem proc de cena no DM"),
		// `UltraEgo.dm:530` diz, textual, que ela nao tem cinematica. A cena e desenho novo.
		("destroyer",                       "o DM diz textualmente que a Destroyer nao tem cinematica"),
		("oozaru",                          "o Oozaru nunca foi cinematica no DM -- desenho novo"),

		// ============================ AS TRES QUE O ORIGINAL TROCA **NO MESMO TIQUE** ============================
		// `snamek()` (`Super_Namek.dm:8-15`) e `Alien_Trans()` (`Alien_Transformations.dm:10-22`) nao tem
		// UM `sleep`: som, `createDustshock`/`createShockwavemisc`, cratera, `startbuff`, e a forma esta
		// posta. O unico relogio dos dois arquivos e o `animate(src, time=7)` do Namekuseijin -- 0,7 s de
		// flash, que nao e cena.
		//
		// Elas ficam aqui e nao fora das duas tabelas porque a ausencia PRECISA estar escrita: as cinco
		// formas raciais nasceram na camada 2 e ficaram sem classificacao nenhuma, e a bancada acusou
		// isso por execucoes seguidas ("sem classificacao: snamek, heran1, heran2, alien1, alien2") sem
		// que ninguem tivesse decidido nada. E a checagem [2] fazendo o trabalho dela.
		// ====================================================================================================
		("snamek",                          "`snamek()` nao tem um `sleep` -- a forma fica no mesmo tique"),
		("alien1",                          "`Alien_Trans()` nao tem um `sleep`"),
		("alien2",                          "`Alien_Trans()` nao tem um `sleep` (o mesmo proc)"),
	];

	/// <summary>
	/// AS CENAS QUE O DM CRONOMETRA E QUE ESTE PORT ENCURTA **DE PROPOSITO**.
	///
	/// ============================ POR QUE UMA TERCEIRA TABELA, E NAO UMA DAS DUAS ============================
	/// As duas de cima dividem o catalogo em "o DM cronometra, e nos copiamos o numero" e "o DM nao
	/// cronometra, e nos inventamos". As duas formas do Heran nao sao nem uma nem outra: o
	/// `Max_Power()` e o `True_Max_Power()` (`HeranBuff.dm:97` e `:193`) TEM prazo -- e a linha
	/// principal deles soma 200 e 230 tiques -- e mesmo assim o port usa a espinha Saiyajin de 15,0 s.
	///
	/// Por-las na tabela do DM daria uma falha VERDADEIRA e ja decidida (a camada que as portou
	/// escreveu a divergencia com o motivo, em `Cinematicas.cs`); por-las na tabela de invencoes seria
	/// mentira -- ha proc. O que faltava era o terceiro nome.
	///
	/// A CAUDA E A DIVERGENCIA INTEIRA. Os dois procs terminam em `sleep(2000 * ssjdrain)`, e o
	/// `ssjdrain` do Heran CAI com a maestria ate zero (`HeranBuff.dm:278`): no original a espera pela
	/// forma encolhe sozinha conforme ele a domina. Este port modela isso pelos tres degraus de cena
	/// (estreia / encurtada / instantanea), que e a MESMA ideia medida por outra variavel -- e por isso
	/// o prazo fixo daqui e o do meio da faixa do DM, e nao o topo dela.
	///
	/// O QUE A BANCADA COBRA DELAS: que a divergencia seja **pra baixo**. Uma cena mais longa que a do
	/// original prenderia o corpo mais tempo do que o jogo de onde ela veio -- o mesmo perigo que o teto
	/// das invencoes existe pra impedir.
	/// </summary>
	private static readonly (string Cena, int TiquesDoDm, string PorQue)[] AsCenasQueEncurtamDeProposito =
	[
		("heran1", 200, "`Max_Power`: sleep(50) + sleep(100) + sleep(2000*ssjdrain=0,025)"),
		("heran2", 230, "`True_Max_Power`: sleep(50) + sleep(100) + sleep(2000*ssj2drain=0,040)"),
	];

	/// <summary>
	/// A TOLERANCIA, DECLARADA: <b>0,05 s</b>.
	///
	/// ============================ O ARGUMENTO MUDOU JUNTO COM O DIVISOR ============================
	/// Ela existia porque o divisor era 12: `250/12 = 20,8333` nao cabe numa casa decimal, entao o
	/// arredondamento sozinho ja produzia ate 0,05 s de diferenca e exigir `==` reprovaria as 25 cenas
	/// certas. Com o divisor certo (10) isso ACABOU: tique/10 tem exatamente uma casa decimal --
	/// 250 -> 25,0, 1400 -> 140,0, 145 -> 14,5 --, e hoje as 25 batem CRAVADAS. A folga nao esta mais
	/// absorvendo arredondamento nenhum.
	///
	/// E ela fica assim mesmo, pelo argumento que sobreviveu e que sempre foi o mais forte: 0,05 s e
	/// menor que UM TIQUE do BYOND (0,1 s), entao nao cabe um `sleep` dentro dela. Nenhuma diferenca
	/// que a tolerancia deixa passar corresponde a uma linha do arquivo original -- que e a unica
	/// coisa que uma tolerancia precisa garantir. Zerar seria trocar uma folga de meio tique por uma
	/// comparacao de `double` com `==`, e essa e pior.
	/// ==========================================================================================
	/// </summary>
	private const double ToleranciaDoDm = 0.05;

	/// <summary>
	/// QUANTOS TIQUES DE `sleep` CABEM NUM SEGUNDO NO BYOND: <b>10</b>. Ver <see cref="OsSleepsDoDm"/>.
	///
	/// ============================ ESTAVA 12, E ERA O `world.fps` ============================
	/// `Globals/World.dm:5` diz `fps = 12`, e eu li isso como a unidade do `sleep`. Nao e: `sleep()`,
	/// `spawn()` e `world.time` sao DECISSEGUNDOS, e o `world.fps` so governa o `tick_lag`. As 25
	/// cenas saiam 20% curtas e esta bancada dizia verde nas 25, porque o erro estava justamente no
	/// numero com que ela conferia.
	///
	/// ELE CONTINUA ESCRITO A MAO e nao lido do `Core.TempoDoDm` DE PROPOSITO, pela mesma razao da
	/// tabela de tiques logo acima: uma bancada que pergunta ao codigo qual e a unidade e depois
	/// confere o codigo com a resposta dele aprova qualquer unidade. A prova mora no `.dm` -- e ela
	/// esta transcrita no sumario do `Core.TempoDoDm`, com oito comentarios do proprio original
	/// (`sleep(3000) // every ~5 min`, `sleep(600) //1 min`, `sleep(100) // 10 seconds`...).
	/// ====================================================================================
	/// </summary>
	private const double TiquesPorSegundoNoDm = 10.0;

	/// <summary>
	/// CADA CENA DURA O QUE A `proc` DO BYOND DURA.
	///
	/// ============================ PELO FUNIL, E NAO PELA LISTA ============================
	/// A pergunta e feita percorrendo o CATALOGO e resolvendo a cena por `Cinematicas.NoDegrau(d,
	/// Estreia)` -- o mesmo caminho que o `World.AoMudarForma` usa quando o pacote chega. Percorrer
	/// `Cinematicas.Todas` mediria uma lista que o jogo nao consulta: no dia em que o `NoDegrau` ganhar
	/// um caso (uma cena mais curta em combate, um degrau novo), a cena TOCADA deixa de ser a cena
	/// conferida e a tabela continuaria dando verde.
	///
	/// E O QUE SE MEDE E O `SegundosPreso`, que e o instante em que a forma FICA -- o retorno da proc no
	/// DM. Nao e o `Segundos` (que inclui a cauda de assentamento, e essa e invencao deste port).
	/// ==================================================================================
	/// </summary>
	private void ADuracaoDoDm()
	{
		var porCena = OsSleepsDoDm.ToDictionary(e => e.Cena, e => e);
		var inventadas = AsCenasSemProcNoDm.Select(e => e.Cena).ToHashSet();

		var medidas = new Dictionary<string, Jandirus.Core.Forms.Cinematica>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Id == Jandirus.Core.Forms.Catalogo.IdBase) continue;
			if (Jandirus.Core.Forms.Cinematicas.NoDegrau(
					d, Jandirus.Core.Forms.DegrauDeCena.Estreia) is { } cena)
				medidas[cena.Forma] = cena;
		}

		// --- 1. AS 25 QUE O DM CRONOMETRA -------------------------------------
		foreach ((string cena, int tiques, string origem) in OsSleepsDoDm)
		{
			if (!medidas.TryGetValue(cena, out Jandirus.Core.Forms.Cinematica? c))
			{
				Conferir(false, $"a cena de '{cena}' ({origem}) chega pelo funil `NoDegrau`");
				continue;
			}
			double doDm = tiques / TiquesPorSegundoNoDm;
			Conferir(Math.Abs(c.SegundosPreso - doDm) <= ToleranciaDoDm,
					 $"'{cena}' dura os {tiques} tiques do DM = {doDm:0.##}s "
				   + $"(esta com {c.SegundosPreso:0.##}s) -- {origem}");
		}

		// ============================ 2. A TABELA COBRE O CATALOGO INTEIRO ============================
		// A checagem que impede a tabela de envelhecer. Sem ela, uma cena nova nasce sem prazo conferido
		// e a bancada continua verde -- que e o modo de falha de toda tabela escrita a mao neste projeto.
		//
		// COMO REPROVA SE A REGRA SUMIR: escreva uma cena nova em `Cinematicas.Todas` e ela cai no mesmo
		// instante, dizendo o nome da forma que ficou sem classificacao.
		// ========================================================================================
		var encurtadas = AsCenasQueEncurtamDeProposito.ToDictionary(e => e.Cena, e => e);
		string semClassificacao = string.Join(", ",
			medidas.Keys.Where(f => !porCena.ContainsKey(f) && !inventadas.Contains(f)
									&& !encurtadas.ContainsKey(f)));
		Conferir(semClassificacao.Length == 0,
				 $"toda cena esta na tabela do DM, declarada invencao ou declarada encurtada "
			   + $"({medidas.Count} cenas) "
			   + $"-- sem classificacao: {(semClassificacao.Length == 0 ? "nenhuma" : semClassificacao)}");

		// ============================ 2b. E A DIVERGENCIA DECLARADA E **PRA BAIXO** ============================
		// Ver `AsCenasQueEncurtamDeProposito`. Declarar uma divergencia nao e ficar livre dela: o que a
		// declaracao compra e o direito de o numero ser outro, nao o de ser MAIOR -- uma cena mais longa
		// que a do original prende o corpo mais tempo do que o jogo de onde ela veio, que e o unico
		// defeito de cinematica que custa partida.
		// ==================================================================================================
		foreach ((string cena, int tiquesDoDm, string porQue) in AsCenasQueEncurtamDeProposito)
		{
			if (!medidas.TryGetValue(cena, out Jandirus.Core.Forms.Cinematica? c))
			{
				Conferir(false, $"a cena encurtada de '{cena}' chega pelo funil `NoDegrau`");
				continue;
			}
			double doDm = tiquesDoDm / TiquesPorSegundoNoDm;
			Conferir(c.SegundosPreso < doDm,
					 $"'{cena}' encurta de proposito e encurta PRA BAIXO "
				   + $"({c.SegundosPreso:0.##}s contra os {doDm:0.##}s do DM) -- {porQue}");
		}

		// E O AVESSO: uma entrada da tabela que nao existe mais no catalogo e uma cena renomeada ou
		// apagada, e a linha dela viraria uma cobranca sem reu (o `foreach` de cima ja reprova, mas
		// dizendo "nao chega pelo funil", que manda procurar no lugar errado).
		string fantasmas = string.Join(", ",
			porCena.Keys.Concat(inventadas).Concat(encurtadas.Keys).Where(f => !medidas.ContainsKey(f)));
		Conferir(fantasmas.Length == 0,
				 $"e nenhuma entrada da tabela ficou sem cena ({(fantasmas.Length == 0 ? "nenhuma" : fantasmas)})");

		// ============================ 3. ONDE EU INVENTEI, INVENTEI PRA BAIXO ============================
		// As oito sem `proc` sao o lugar onde este port escolheu o numero sozinho, e nao ha original pra
		// conferi-las. O que da pra cobrar e a FORMA da invencao: nenhuma delas pode ser mais longa que a
		// menor cena que o DM realmente tem (o ritual divino, 145 tiques = 14,5 s).
		//
		// A regra existe porque a invencao perigosa e a longa: um surto de 15 s prenderia o corpo de quem
		// so subiu um degrau JA transformado, e a diferenca entre "cena curta demais" (chato) e "cena longa
		// demais" (o jogador apanhando parado) e a unica que custa partida.
		// ==========================================================================================
		double menorDoDm = OsSleepsDoDm.Min(e => e.Tiques) / TiquesPorSegundoNoDm;
		foreach ((string cena, string porQue) in AsCenasSemProcNoDm)
		{
			if (!medidas.TryGetValue(cena, out Jandirus.Core.Forms.Cinematica? c)) continue;
			// ============================ `&lt;=` E NAO `&lt;`, E O MOTIVO E QUE O PISO MUDOU DE DONO ============================
			// Este teto era 14,5 s -- "o ritual divino, a menor cena que o DM realmente tem", como diz o
			// comentario acima. A linha do Frost Demon trouxe o `fd_burst_fx` (`spawn(25)`) e o piso caiu
			// pra 2,5 s **sem que ninguem tivesse decidido isso**: a menor cena do DM passou a ser um
			// SURTO e nao uma cinematica.
			//
			// Com `&lt;` estrito a regra ficou contraditoria consigo mesma: as tres cenas raciais que este
			// port inventou usam EXATAMENTE aquele numero do DM (2,5 s, o `SurtoInstantaneo` do
			// `Cinematicas.cs`, cuja origem esta escrita la e e o proprio `fd_burst_fx`) -- ou seja, uma
			// invencao cujo prazo E o da menor cena do original era acusada de passar dela.
			//
			// O `&lt;=` diz o que a regra sempre quis dizer: **nenhuma invencao dura MAIS que a menor cena
			// que o DM realmente tem**. As que ainda estao acima continuam vermelhas, e devem continuar:
			// elas foram escritas contra um piso de 14,5 s que nao existe mais, e reescolher o prazo
			// delas (ou separar surto de cinematica na tabela) e decisao de quem desenhou as cenas, nao
			// desta bancada.
			// ==========================================================================================================
			Conferir(c.SegundosPreso <= menorDoDm + ToleranciaDoDm,
					 $"'{cena}' e invencao ({porQue}) e nao passa da menor cena do DM "
				   + $"({c.SegundosPreso:0.##}s contra {menorDoDm:0.##}s)");
		}

		_passos.Add($"  --     {OsSleepsDoDm.Length} cenas cronometradas no DM + {AsCenasSemProcNoDm.Length} "
				  + $"invencoes declaradas; tolerancia {ToleranciaDoDm:0.##}s "
				  + $"({ToleranciaDoDm * TiquesPorSegundoNoDm:0.#} tique)");
	}

	// =====================================================================
	// 2f. O TETO DA ENCURTADA
	// =====================================================================
	/// <summary>
	/// O TETO ESCRITO AQUI, E NAO LIDO DO CORE: <b>10 s</b>.
	///
	/// A checagem que ja existia (`a curta cabe na faixa`) le `Cinematicas.MaximoDaCurta` dos dois lados
	/// da comparacao -- ela prova que o `Clamp` funciona e nao prova NADA sobre o numero. Trocar o 10 por
	/// 40 passaria verde, e o degrau do meio voltaria a ser a espera inteira.
	///
	/// Dez e decisao do dono, tomada junto com a restauracao dos prazos: com as cheias de volta ao DM, o
	/// teto antigo de 5 s fazia ONZE das 34 encurtadas baterem no limite e sairem TODAS com o mesmo
	/// comprimento -- cenas de 12, 18 e 116 segundos empatadas em 5.
	/// </summary>
	private const double TetoDaCurtaEscritoAMao = 10.0;

	/// <inheritdoc cref="TetoDaCurtaEscritoAMao"/>
	private void OTetoDaEncurtada()
	{
		// --- 1. O NUMERO NAO ESCORREGOU --------------------------------------
		Conferir(Math.Abs(Jandirus.Core.Forms.Cinematicas.MaximoDaCurta - TetoDaCurtaEscritoAMao) < 0.001,
				 $"o teto da encurtada continua sendo {TetoDaCurtaEscritoAMao:0.#}s "
			   + $"(o Core diz {Jandirus.Core.Forms.Cinematicas.MaximoDaCurta:0.#})");

		// --- 2. NINGUEM PASSA DELE, PELO FUNIL QUE O JOGO USA -----------------
		double pior = 0; string dePior = "";
		var curtas = new List<double>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (d.Id == Jandirus.Core.Forms.Catalogo.IdBase) continue;
			if (Jandirus.Core.Forms.Cinematicas.NoDegrau(
					d, Jandirus.Core.Forms.DegrauDeCena.Curta) is not { } k) continue;
			curtas.Add(k.SegundosPreso);
			if (k.SegundosPreso > pior) { pior = k.SegundosPreso; dePior = d.Id; }
		}
		Conferir(pior <= TetoDaCurtaEscritoAMao + 0.001,
				 $"nenhuma encurtada prende mais que {TetoDaCurtaEscritoAMao:0.#}s "
			   + $"(a maior e `{dePior}` com {pior:0.##}s)");

		// ============================ 3. E O TETO TEM QUE MORDER ============================
		// Um limite que nunca e alcancado e um literal morto: 10, 100 ou 1000 dariam o mesmo jogo, e
		// ninguem descobriria a diferenca ate alguem "limpar" a linha. A cena do SSJ3 assume aos 140 s
		// e 0,4 disso sao 56 -- ela existe pra encostar aqui.
		//
		// COMO REPROVA SE A REGRA SUMIR: tire o `MaximoDaCurta` do `Math.Clamp` do `Encurtar` e esta linha
		// cai junto com a de cima (a de cima diz "passou do teto", esta diz "o teto nao existe mais").
		// ================================================================================
		Conferir(curtas.Any(s => Math.Abs(s - TetoDaCurtaEscritoAMao) < 0.001),
				 $"e alguma encosta nele -- senao o {TetoDaCurtaEscritoAMao:0.#} seria literal morto "
			   + $"({curtas.Count(s => Math.Abs(s - TetoDaCurtaEscritoAMao) < 0.001)} encostam)");

		Conferir(curtas.Any(s => Math.Abs(s - Jandirus.Core.Forms.Cinematicas.MinimoDaCurta) < 0.001),
				 $"e o PISO tambem morde ({curtas.Count(s => Math.Abs(s - Jandirus.Core.Forms.Cinematicas.MinimoDaCurta) < 0.001)} "
			   + $"cenas em {Jandirus.Core.Forms.Cinematicas.MinimoDaCurta:0.#}s)");

		// ============================ 4. MAS ELE NAO PODE ENGOLIR A ESCADA ============================
		// Esta e a checagem que teria pegado o teto ANTIGO. Com 5 s, onze das 34 encurtadas empatavam no
		// limite -- e nada reprovava, porque "ninguem passou do teto" continuava verdade. O defeito nao e
		// passar do teto: e o teto virar o UNICO valor, e a proporcao da cheia (que e a razao de o fator
		// existir) desaparecer.
		//
		// Cinco valores distintos e um piso conservador: hoje sao sete (2,0 / 2,4 / 5,8 / 6,0 / 6,4 /
		// 8,8 / 10,0). Baixar o teto pra 5 derrubaria isto pra tres.
		//
		// ERAM NOVE ANTES DE O RELOGIO SER CORRIGIDO, e isso e uma consequencia real do conserto que
		// vale a pena ter escrita: com as cheias 1,2x mais longas, QUATRO grupos de cena (SSJ1, as
		// divinas de meio de escada, o SSJ3 e as quatro Legendary) passaram a estourar o teto de 10 s
		// e empataram nele. A variacao que o `FatorDaCurta` existe pra dar diminuiu -- nao sumiu --,
		// e mexer no teto por causa disso e decisao do dono, nao conserto de unidade.
		// ======================================================================================
		int distintos = curtas.Select(s => Math.Round(s, 2)).Distinct().Count();
		Conferir(distintos >= 5,
				 $"e a encurtada volta a ter a PROPORCAO da cheia -- {distintos} duracoes distintas "
			   + $"(com teto de 5 s eram 3)");

		// ============================ 5. A CAUDA NAO CRESCE NA COMPRESSAO ============================
		// O teto e sobre o `SegundosPreso`, e a cena continua depois dele (a cauda de assentamento). Se o
		// `Encurtar` comprimisse o prazo e nao os beats -- que e o erro de quem "arruma" o clamp sem olhar
		// o laco --, a curta prenderia 10 s e continuaria rodando por 40. A janela solta nunca pode ficar
		// maior que a da cheia.
		// ======================================================================================
		int cresceu = 0; string exemplo = "";
		foreach (Jandirus.Core.Forms.Cinematica c in Jandirus.Core.Forms.Cinematicas.Todas)
		{
			var k = Jandirus.Core.Forms.Cinematicas.Encurtada(c);
			double janelaCheia = c.Segundos - c.SegundosPreso;
			double janelaCurta = k.Segundos - k.SegundosPreso;
			if (janelaCurta > janelaCheia + 0.001)
			{
				cresceu++;
				if (exemplo.Length == 0)
					exemplo = $"`{c.Forma}`: {janelaCurta:0.##}s solta contra {janelaCheia:0.##}s";
			}
		}
		Conferir(cresceu == 0,
				 $"e a cauda solta nunca cresce ao comprimir ({cresceu}: {exemplo})");

		_passos.Add($"  --     encurtadas de {curtas.Min():0.##} a {curtas.Max():0.##}s "
				  + $"({distintos} duracoes distintas em {curtas.Count} formas)");
	}

	// =====================================================================
	// 2f-bis. A UNIDADE DO DM, MEDIDA CONTRA A PROSA DO PROPRIO AUTOR
	// =====================================================================
	/// <summary>
	/// OS PRAZOS QUE O AUTOR DO ORIGINAL ESCREVEU POR EXTENSO, AO LADO DO `sleep` QUE ELES DESCREVEM.
	///
	/// ============================ POR QUE ESTA TABELA PRECISOU EXISTIR ============================
	/// O defeito que ela ancora nao foi um numero errado: foi uma BANCADA que media o jogo contra a
	/// constante errada dela mesma. Este arquivo carregava `TiquesPorSegundoNoDm = 12`, as 25 cenas
	/// eram conferidas com esse 12, e as 25 davam verde -- estando todas 20% curtas. Uma bancada que
	/// pergunta ao codigo qual e a unidade e depois confere o codigo com a resposta dele aprova
	/// QUALQUER unidade; e foi exatamente isso que aconteceu por meses.
	///
	/// Entao a pergunta aqui nao passa por constante nenhuma nossa. Ela e feita contra a unica fonte
	/// que nao pode ser reescrita por quem esta mexendo neste repo: o COMENTARIO DO AUTOR DO DM, que
	/// em quinze lugares escreveu o `sleep()` e a duracao dele em prosa, lado a lado. `sleep(3000) //
	/// every ~5 min` e uma equacao com os dois lados preenchidos, e nenhum dos dois e nosso.
	///
	/// ============================ O QUE ELA REPROVA, E COMO ============================
	/// Ela le <see cref="Jandirus.Core.TempoDoDm.TiquesPorSegundo"/> -- a constante do JOGO, a que
	/// `Oozaru`, `Voo` e as duas rampas derivam -- e cobra que ela reproduza os quinze. Repor o 12
	/// derruba os quinze de uma vez, no mesmo instante, com o `.dm` e a linha citados em cada um.
	///
	/// E o `ADuracaoDoDm` logo acima NAO faz esse servico, apesar de parecer: os `SegundosPreso` das
	/// cinematicas sao literais (`25.0`, `140.0`), nao derivados do `TempoDoDm`. Um 12 reposto no
	/// Core deixaria as 25 cenas intactas e verdes e quebraria o Oozaru, o dreno de voo e as rampas
	/// em silencio -- que e o buraco que esta tabela fecha.
	///
	/// ============================ A FOLGA, E POR QUE ELA E 5% ============================
	/// A prosa do autor e aproximada ("~5 min", "100 or so seconds"), entao a comparacao e RELATIVA.
	/// Cinco por cento e escolha com conta por tras: o unico verso que usa a folga e o
	/// `sleep(3100)//five minutes lol` (310 s contra 300 = 3,3%); todos os outros catorze batem
	/// CRAVADOS com o decimo. E o erro que o divisor 12 produz e 16,7% em toda linha -- mais de tres
	/// vezes a folga. Nao ha aqui um numero que o 12 alcance por sorte, e isso nao fica so escrito:
	/// a ultima checagem do metodo MEDE a tabela contra o 12 e cobra que os quinze o rejeitem.
	///
	/// ============================ UMA LINHA DO DM QUE FICOU DE FORA ============================
	/// `Players/MindSwap.dm:34` diz `sleep(5)//...it takes five seconds for the SLogoffOverride var
	/// to be ticked back`. Cinco tiques nao sao cinco segundos em divisor nenhum (0,5 s pelo decimo,
	/// 0,42 s pelo doze): o autor errou a propria prosa ali. Ela fica FORA da tabela e escrita aqui,
	/// porque uma linha ausente sem explicacao e indistinguivel de uma linha esquecida -- e porque a
	/// proxima pessoa que varrer o DM atras de prosa vai reencontra-la.
	/// ==========================================================================================
	/// </summary>
	private static readonly (string Onde, int Tiques, double SegundosNaProsa, string Prosa)[] AProsaDoAutorDoDm =
	[
		("Globals/PlanetPopulation.dm:471", 3000, 300,  "sleep(3000)  // every ~5 min"),
		("Tech/ProceduralSpace.dm:980",     3000, 300,  "sleep(3000) //5 min"),
		("Login/Login.dm:65",               3000, 300,  "spawn(3000) goto save_char //autosave every 5 mins"),
		("Turfs/Area_Death.dm:129",         3100, 300,  "sleep(3100)//five minutes lol"),
		("Styles/stylemobhandler.dm:51",    1000, 100,  "spawn(1000) StyleUpdate() //every 100 or so seconds"),
		("Ranks/RankQuests.dm:632",          600,  60,  "sleep(600) //1 min"),
		("Ranks/GodOfDestruction.dm:550",    600,  60,  "sleep(600) //1 min real"),
		("Magic/Dragonballs.dm:404",         600,  60,  "sleep(600) //a cada ~1 min"),
		("Skills/UltraInstinct.dm:61",       600,  60,  "#define UI_GD_CD 600 //cooldown (1 min)"),
		("Tech/ShipVessel.dm:187",           100,  10,  "sleep(100) //10 seconds"),
		("Magic/Fusion.dm:342",               10,   1,  "sleep(10) //~1s tick"),
		("Tech/ProceduralSpace.dm:775",       10,   1,  "spawn(10) //... 1s pro OnLogin assentar"),
		("UI/HtmlUI.dm:669",                   8, 0.8,  "sleep(8) //~0.8s"),
		("UI/HtmlUI.dm:735",                   4, 0.4,  "sleep(4) //~0.4s"),
		("Skills/UltraInstinct.dm:62",         3, 0.3,  "#define UI_TICKSECS 0.3 //1 ciclo do GlobalStats (sleep(3))"),
	];

	/// <inheritdoc cref="AProsaDoAutorDoDm"/>
	private const double FolgaDaProsa = 0.05;

	/// <summary>
	/// O DIVISOR QUE ESTAVA AQUI E QUE NAO PODE VOLTAR: <b>12</b>.
	///
	/// Ele nao e um numero qualquer -- e o `world.fps` de `Globals/World.dm:5`, e a confusao dele com
	/// a unidade do `sleep` e o defeito inteiro. Escrito com nome pra a checagem que o rejeita poder
	/// dizer de onde ele veio, e nao so "12".
	/// </summary>
	private const double OFpsQueNaoEAUnidade = 12.0;

	/// <inheritdoc cref="AProsaDoAutorDoDm"/>
	private void AUnidadeContraAProsaDoDm()
	{
		// A CONSTANTE DO JOGO, e nao a desta bancada. Ver o sumario: perguntar a nos mesmos e o que
		// deixou o 12 passar 25 vezes.
		double nossa = Jandirus.Core.TempoDoDm.TiquesPorSegundo;

		// --- 1. OS QUINZE COMENTARIOS DO AUTOR -------------------------------
		foreach ((string onde, int tiques, double naProsa, string prosa) in AProsaDoAutorDoDm)
		{
			double nossoDiz = tiques / nossa;
			double erro = Math.Abs(nossoDiz - naProsa) / naProsa;
			Conferir(erro <= FolgaDaProsa,
					 $"{onde} `{prosa}`: o autor escreveu {naProsa:0.##}s e a nossa unidade da "
				   + $"{nossoDiz:0.##}s ({erro * 100:0.#}% de erro)");
		}

		// ============================ 2. A TABELA SABE DIZER 10 DE 12 ============================
		// Uma tabela de prosa com folga generosa demais e um enfeite: se o 12 tambem coubesse na
		// folga, os quinze `Conferir` acima continuariam verdes com o defeito de volta e eu teria
		// escrito quinze linhas que nao medem nada. Entao a tabela se mede a si mesma -- cada verso
		// e reprocessado com o `world.fps` no lugar do decimo, e cobra-se que TODOS reprovem.
		//
		// Esta e a linha que substitui "confie em mim, o 12 quebraria": ela roda o 12 de verdade.
		// ====================================================================================
		int rejeitam = 0; double piorFolga = 0; string maisFrouxa = "";
		foreach ((string onde, int tiques, double naProsa, _) in AProsaDoAutorDoDm)
		{
			double erroDoDoze = Math.Abs(tiques / OFpsQueNaoEAUnidade - naProsa) / naProsa;
			if (erroDoDoze > FolgaDaProsa) rejeitam++;
			else if (erroDoDoze > piorFolga) { piorFolga = erroDoDoze; maisFrouxa = onde; }
		}
		Conferir(rejeitam == AProsaDoAutorDoDm.Length,
				 $"e os {AProsaDoAutorDoDm.Length} versos REPROVAM o divisor {OFpsQueNaoEAUnidade:0} "
			   + $"(o `world.fps`) -- {rejeitam} rejeitam"
			   + (maisFrouxa.Length == 0 ? "" : $", o mais frouxo seria {maisFrouxa} com {piorFolga * 100:0.#}%"));

		// ============================ 3. AS DUAS ANCORAS NAO PODEM SE SEPARAR ============================
		// Esta bancada tem DOIS numeros: o `TiquesPorSegundoNoDm` (escrito a mao aqui, que confere as 25
		// cenas) e o `TempoDoDm.TiquesPorSegundo` (do Core, que o Oozaru e o voo derivam). Eles medem a
		// mesma coisa e nada os obrigava a concordar -- e um par de ancoras que pode divergir e como nao
		// ter nenhuma: a metade errada continua verde contra si mesma, que e o retrato exato do dia em
		// que as cinematicas estavam certas e o resto do jogo, 20% curto.
		// ==========================================================================================
		Conferir(Math.Abs(nossa - TiquesPorSegundoNoDm) < 1e-9,
				 $"e a unidade do Core ({nossa:0.##}) e a que esta bancada usa nas cenas "
			   + $"({TiquesPorSegundoNoDm:0.##}) sao a MESMA");

		// ============================ 4. AS DUAS CADENCIAS, QUE NAO SAO `sleep` ============================
		// O erro irmao -- e o que a multiplicacao por 1,2 nao consertava -- e converter um CONTADOR de
		// ciclos como se fosse um prazo. `angertick--` e `combatTime++` nao dizem quanto tempo passa: quem
		// diz e o laco que os anda, e o DM tem DOIS lacos parecidos com cadencias diferentes
		// (`Stats()` dorme 2, `GlobalStats()` dorme 3). Trocar um pelo outro erra 50%, e foi o que
		// aconteceu com as rampas Legendary (100 s onde o DM diz 3 min).
		//
		// E O AUTOR CRAVOU AS DUAS POR ESCRITO, o que faz delas prova e nao deducao:
		//   `UltraInstinct.dm:62`  #define UI_TICKSECS 0.3  //1 ciclo do GlobalStats (sleep(3))
		//   `1A Defines.dm:44`     #define LSSJ_RAMP_TICKS 600 //ciclos de GlobalStats (~0.3s) (600 = ~3min)
		// A segunda e melhor que a primeira: ela multiplica a cadencia por 600 e diz o resultado em
		// minutos, entao um erro de 0,1 s na cadencia vira um minuto inteiro de diferenca -- ela detecta
		// o que a folga de 5% de um unico ciclo deixaria passar.
		// ==========================================================================================
		Conferir(Math.Abs(Jandirus.Core.TempoDoDm.SegundosDoLacoGlobalStats - 0.3) < 1e-9,
				 "a volta do `GlobalStats()` dura os 0,3 s que o `UI_TICKSECS` do DM crava "
			   + $"(deu {Jandirus.Core.TempoDoDm.SegundosDoLacoGlobalStats:0.###}s)");

		double rampa = 600 * Jandirus.Core.TempoDoDm.SegundosDoLacoGlobalStats;
		Conferir(Math.Abs(rampa - 180) / 180 <= FolgaDaProsa,
				 $"e os 600 ciclos do `LSSJ_RAMP_TICKS` dao os ~3 min que o `1A Defines.dm:44` escreve "
			   + $"({rampa:0.#}s)");

		Conferir(Math.Abs(Jandirus.Core.TempoDoDm.SegundosDoLacoStats - 2 / nossa) < 1e-9
			  && Math.Abs(Jandirus.Core.TempoDoDm.SegundosDoLacoStats - 0.2) < 1e-9,
				 $"a volta do `Stats()` e o `sleep(2)` de `Stats.dm:126` na mesma unidade "
			   + $"({Jandirus.Core.TempoDoDm.SegundosDoLacoStats:0.###}s)");

		// E AS DUAS SAO DUAS. A cilada nao e errar o valor de uma delas: e alguem "simplificar" as duas
		// numa constante so, e nesse dia metade dos prazos do jogo anda 50% errada sem nenhum numero ter
		// mudado de aparencia. Esta linha e a que cai.
		Conferir(Jandirus.Core.TempoDoDm.SegundosDoLacoStats
			  != Jandirus.Core.TempoDoDm.SegundosDoLacoGlobalStats,
				 "e os DOIS lacos do DM continuam sendo dois (0,2 s do corpo, 0,3 s do estado)");

		_passos.Add($"  --     {AProsaDoAutorDoDm.Length} comentarios do autor do DM conferidos contra "
				  + $"{nossa:0.##} tiques/s; folga {FolgaDaProsa * 100:0.#}% (o divisor {OFpsQueNaoEAUnidade:0} erra 16,7%)");
	}

	// =====================================================================
	// 2f-ter. OS NUMEROS DE DESTINO, ESCRITOS A MAO
	// =====================================================================
	/// <summary>
	/// O SEGUNDO CADEADO: os prazos que o conserto do relogio produziu, cravados aqui um por um.
	///
	/// ============================ POR QUE ISTO NAO E REPETICAO DO BLOCO DE CIMA ============================
	/// O <see cref="AUnidadeContraAProsaDoDm"/> prova a UNIDADE e o <see cref="ADuracaoDoDm"/> prova as
	/// cenas contra os tiques -- e mesmo assim os dois juntos deixam uma porta aberta: quem mexer nos
	/// DOIS lados ao mesmo tempo (a tabela de tiques e o valor da cena) continua verde nos dois. Foi
	/// assim que as cenas ficaram 20% curtas com a bancada aprovando.
	///
	/// Estes numeros nao derivam de nada. Sao o resultado escrito, e eles so mudam se alguem vier aqui
	/// mudar. E cada um deles e um prazo que o jogador SENTE -- o macaco que cai, a cena que prende, a
	/// forma que sobe --, entao um erro aqui e um erro de jogo, nao de planilha.
	///
	/// ============================ TRES DELES NAO SAO CONVERSAO ============================
	/// O teto de 10 s, o piso de 2 s e o fator 0,4 da encurtada sao numeros do DONO. Eles estao aqui
	/// junto com os convertidos DE PROPOSITO, e com a diferenca dita em voz alta: a varredura que
	/// corrigiu o relogio passou multiplicando prazo por 1,2, e uma varredura assim nao distingue "o
	/// numero veio do DM" de "o numero veio do dono". Um 10 que virasse 12 seria invisivel -- e as tres
	/// linhas abaixo cobram exatamente o valor que o dono deu, ao lado do valor que a varredura teria
	/// deixado.
	/// ==================================================================================================
	/// </summary>
	private void OsNumerosDeDestino()
	{
		static double Preso(string id) =>
			Jandirus.Core.Forms.Catalogo.Def(id) is { } d
			&& Jandirus.Core.Forms.Cinematicas.NoDegrau(d, Jandirus.Core.Forms.DegrauDeCena.Estreia) is { } c
				? c.SegundosPreso : double.NaN;

		void Crava(double deu, double alvo, string oque) =>
			Conferir(Math.Abs(deu - alvo) < 0.001, $"{oque} = {alvo:0.##}s (deu {deu:0.##})");

		// --- 1. A CENA MAIS LONGA DO JOGO ------------------------------------
		// 1400 tiques em `SSJ3Cinematic.dm`. E o prazo mais visivel de todos: e ele que dita o tamanho
		// das duas redes de seguranca la embaixo, e foi ele que passou de 116,7 pra 140.
		Crava(Preso("ssj3"), 140.0, "a estreia do SSJ3 prende os 1400 tiques do DM");

		// --- 2. AS TRES CENAS DIVINAS ----------------------------------------
		// 220 tiques, e as tres dividem o mesmo numero por dividirem o mesmo tipo de proc no DM
		// (`ui_grand_cinematic` / `ue_grand_cinematic`). Cravadas as TRES, e nao uma: um `NoDegrau`
		// que passasse a resolver a linha do Ultra Ego por outro caminho sairia so numa delas.
		foreach (string id in new[] { "ui_sign", "ui_perfected", "ultra_ego" })
			Crava(Preso(id), 22.0, $"a estreia de `{id}` prende os 220 tiques do DM");

		// --- 3. O OOZARU -----------------------------------------------------
		// Os dois `spawn` de `Oozaru.dm` (3000 e 1000). O regular e o que o `PlanetPopulation.dm:471`
		// confirma por prosa com o MESMO 3000: cinco minutos.
		Crava(Jandirus.Core.Forms.Oozaru.SegundosRegular, 300.0, "o Oozaru comum dura o `spawn(3000)`");
		Crava(Jandirus.Core.Forms.Oozaru.SegundosDourado, 100.0, "e o Dourado, o `spawn(1000)`");

		// ============================ 3-bis. E MEDITAR NAO ENCURTA O COMUM ============================
		// Isto NAO e um erro corrigido pela metade -- e o DM. O `angertick` do `BuffLoop()` e contador de
		// ciclos de `GlobalStats` (0,3 s), entao 1000 ciclos sao 300 s, que e exatamente a duracao do
		// Oozaru comum: meditar so antecipa a queda do DOURADO (100 s). Portado como esta escrito.
		//
		// A linha existe pra a coincidencia ficar AFIRMADA. Sem ela, a proxima pessoa que notar os dois
		// 300 vai concluir "um deles esta errado" e mexer -- e a igualdade e o porte fiel.
		// ==========================================================================================
		Crava(Jandirus.Core.Forms.Oozaru.SegundosMeditandoAteCair,
			  Jandirus.Core.Forms.Oozaru.SegundosRegular,
			  "e meditar leva os mesmos 300 s do Oozaru comum (o DM: so o Dourado encurta)");

		// --- 4. OS DOIS QUE ERAM CADENCIA, E NAO UNIDADE ---------------------
		// Estes tres nao teriam sido consertados por uma varredura de x1,2: o fator deles e 1,5 (o voo,
		// que lia 6 Hz onde o `Stats()` roda a 5) e 1,8 (as rampas, que liam a cadencia do laco errado).
		// Cravados aqui porque sao os que nenhuma conta geral alcanca.
		Crava(Jandirus.Core.World.Voo.TiquesDoDmPorSegundo, 5.0,
			  "o dreno de voo anda na cadencia do `Stats()` -- 5 tiques/s, e nao 6");
		Crava(Jandirus.Core.Forms.Catalogo.RampaLssjSegundos, 180.0,
			  "a rampa Legendary demora os ~3 min do `1A Defines.dm:44`");
		Crava(Jandirus.Core.Forms.Catalogo.RampaPrimalSegundos, 216.0,
			  "e a Primal, os 720 ciclos dela");

		// ============================ 5. OS TRES NUMEROS DO DONO NAO LEVARAM O x1,2 ============================
		// A varredura que corrigiu o relogio multiplicou prazo por 1,2. Estes tres nao sao prazo do DM --
		// sao decisao do dono sobre a encurtada -- e a unica coisa que os protegeu foi alguem lembrar.
		// Aqui a lembranca vira checagem, e ela diz ao lado o valor que a varredura teria deixado.
		// ================================================================================================
		Crava(Jandirus.Core.Forms.Cinematicas.MaximoDaCurta, 10.0,
			  "o TETO da encurtada e o 10 do dono e nao levou o x1,2 (que teria deixado 12,0)");
		Crava(Jandirus.Core.Forms.Cinematicas.MinimoDaCurta, 2.0,
			  "o PISO dela e o 2 do dono (o x1,2 teria deixado 2,4)");
		Crava(Jandirus.Core.Forms.Cinematicas.FatorDaCurta, 0.4,
			  "e o FATOR e 0,4 -- razao, nao prazo: um x1,2 aqui seria 0,48");

		// E O TETO DO DONO NAO PODE SER CONFUNDIDO COM CENA. Se ele algum dia coincidir com a duracao de
		// uma cena do DM, a proxima pessoa vai deriva-lo dela e ele deixa de ser decisao. Dez segundos
		// ficam com folga abaixo da menor cena que o original tem (o ritual divino, 145 tiques).
		double menorCena = OsSleepsDoDm.Min(e => e.Tiques) / TiquesPorSegundoNoDm;
		Conferir(Jandirus.Core.Forms.Cinematicas.MaximoDaCurta < menorCena,
				 $"e o teto do dono ({Jandirus.Core.Forms.Cinematicas.MaximoDaCurta:0.#}s) nao coincide com "
			   + $"cena nenhuma do DM -- a menor delas prende {menorCena:0.##}s");
	}

	// =====================================================================
	// 2f-quater. A TRANCA COBRE A CENA INTEIRA
	// =====================================================================
	/// <summary>
	/// AS DUAS REDES DE SEGURANCA CONTRA A CENA -- e a cena e medida AQUI, nao perguntada ao Core.
	///
	/// ============================ O DEFEITO QUE ISTO IMPEDE E O AVESSO DO ESPERADO ============================
	/// As duas redes (`Transformacao.PrazoMaximoPreso` e `LocalPlayer.SegundosAteDestravar`) existem pra
	/// "preso pra sempre" nao ser alcancavel. Mas uma rede mais CURTA que a cena nao salva ninguem: ela
	/// SOLTA o jogador no meio da propria estreia, andando por baixo da cinematica. Foi o que aconteceu
	/// -- o prazo do corpo era 40 s cravado e a cena do SSJ3 passou a prender 140.
	///
	/// ============================ POR QUE A BANCADA RECALCULA A CENA MAIS LONGA ============================
	/// Ja havia uma checagem de relacao aqui, e ela lia `Cinematicas.CenaMaisLonga` -- o mesmo campo de
	/// onde as redes saem. Perguntar ao Core qual e a cena mais longa e depois conferir as redes com a
	/// resposta dele aprova qualquer numero: trocar o campo por um literal `50.0` faria as redes virarem
	/// 100 e as tres linhas continuariam verdes, com o SSJ3 prendendo 140.
	///
	/// Entao o maximo e varrido AQUI, de `Cinematicas.Todas`, e o campo do Core e cobrado contra ele. E
	/// a folga e uma RAZAO (a rede vale ao menos uma cena e meia), nunca um literal em segundos: uma
	/// folga escrita "50 s" e generosa hoje e curta no dia da proxima cena longa, e ninguem descobre.
	/// ==================================================================================================
	/// </summary>
	private void ATrancaCobreACena()
	{
		double maisLonga = 0, maiorPreso = 0; string deQuem = "";
		foreach (Jandirus.Core.Forms.Cinematica c in Jandirus.Core.Forms.Cinematicas.Todas)
		{
			if (c.Segundos > maisLonga) { maisLonga = c.Segundos; deQuem = c.Forma; }
			if (c.SegundosPreso > maiorPreso) maiorPreso = c.SegundosPreso;
		}
		if (maisLonga <= 0) { Conferir(false, "ha cena pra medir a tranca"); return; }

		// --- 1. O CAMPO DO CORE E MESMO O MAXIMO ------------------------------
		Conferir(Math.Abs(Jandirus.Core.Forms.Cinematicas.CenaMaisLonga - maisLonga) < 0.001,
				 $"a `CenaMaisLonga` do Core e a cena mais longa que existe -- `{deQuem}` com "
			   + $"{maisLonga:0.##}s (o Core diz {Jandirus.Core.Forms.Cinematicas.CenaMaisLonga:0.##})");

		// ============================ 2. A CENA INTEIRA, E NAO SO O TRECHO PRESO ============================
		// A cena nao acaba quando a forma assenta: ha a cauda (beats de fumaca, luz baixando). Medir a rede
		// contra o `SegundosPreso` e o erro confortavel -- ele e sempre menor, e uma rede dimensionada por
		// ele solta o corpo durante a cauda, que e justamente a parte em que ninguem esta olhando o log.
		// ==============================================================================================
		double folgaTranca = Transformacao.PrazoMaximoPreso / maisLonga;
		double folgaCorpo = LocalPlayer.SegundosAteDestravarDeTeste / maisLonga;
		Conferir(folgaTranca >= 1.5,
				 $"a rede da TRANCA cobre a cena INTEIRA com folga ({Transformacao.PrazoMaximoPreso:0.#}s "
			   + $"= {folgaTranca:0.##}x os {maisLonga:0.##}s de `{deQuem}`)");
		Conferir(folgaCorpo >= 1.5,
				 $"a rede do CORPO cobre a cena INTEIRA com folga ({LocalPlayer.SegundosAteDestravarDeTeste:0.#}s "
			   + $"= {folgaCorpo:0.##}x)");

		// ============================ 3. AS DUAS SAO DERIVADAS, E ISSO SE MEDE ============================
		// Uma rede que fosse literal passaria nas duas linhas de cima HOJE e apodreceria na proxima cena
		// longa. O que distingue derivada de literal e a RELACAO exata com o maximo varrido acima: se
		// alguem cravar `286.4`, a igualdade sobrevive ate a primeira cena mudar de tamanho -- e ai cai,
		// que e exatamente quando ela precisa cair.
		// ==========================================================================================
		Conferir(Math.Abs(Transformacao.PrazoMaximoPreso - maisLonga * 2.0) < 0.001,
				 $"e ela e DERIVADA da cena (2x {maisLonga:0.##} = {maisLonga * 2:0.##}s), nao um prazo escrito");
		Conferir(Math.Abs(LocalPlayer.SegundosAteDestravarDeTeste - maisLonga * 2.0) < 0.001,
				 "e a do corpo tambem -- o piso de 40 s dela nao morde ha muito tempo");

		// E A TRANCA NAO PODE SER MAIS CURTA QUE O TRECHO PRESO, que e a leitura mais direta do defeito
		// original. Redundante com o item 2 hoje; deixa de ser no dia em que uma cena tiver cauda longa.
		Conferir(Transformacao.PrazoMaximoPreso > maiorPreso
			  && LocalPlayer.SegundosAteDestravarDeTeste > maiorPreso,
				 $"e as duas passam do trecho que mais PRENDE ({maiorPreso:0.#}s)");

		_passos.Add($"  --     cena mais longa `{deQuem}` {maisLonga:0.##}s (prende {maiorPreso:0.#}s); "
				  + $"redes em {Transformacao.PrazoMaximoPreso:0.#}s ({folgaTranca:0.##}x) e "
				  + $"{LocalPlayer.SegundosAteDestravarDeTeste:0.#}s ({folgaCorpo:0.##}x)");
	}

	// =====================================================================
	// 2g. NENHUM BURACO NO MEIO DE UMA CENA
	// =====================================================================
	/// <summary>
	/// O MAIOR VAO SEM BEAT: <b>7,2 s</b>, e a cabeca da cena conta como vao.
	///
	/// ============================ POR QUE ESTE N, E NAO OUTRO ============================
	/// Sao duas razoes e nenhuma delas e gosto:
	///
	///   1. <b>E CATRACA.</b> O maior vao que existe hoje sao os 7,0 s da cena do SSJ3 (entre 103 e
	///      110 s, um dos nove silencios do original). 7,2 aceita o estado atual e recusa qualquer
	///      passo pra tras -- e o passo pra tras e o provavel, porque ele nao custa nada a quem escreve:
	///      apagar um beat de meio de cena nao quebra nada, nao muda o prazo e nao aparece em lugar nenhum.
	///
	///   2. <b>SETE SEGUNDOS JA FOI JULGADO LONGO DEMAIS, com o dono na sala.</b> A passada que encheu as
	///      cenas achou o buraco de 7,0 s da cena do SSJ1 -- entre a aura grande sair (18,0 s) e a forma
	///      ficar (25,0) -- e o chamou de "o buraco real". A razao esta escrita la e continua valendo: o
	///      corpo esta PRESO nesse tempo, entao uma tela parada com um boneco que nao responde nao le
	///      como tensao, le como o jogo ter travado. Nao ha nada na interface dizendo o contrario.
	///
	/// ============================ ELE ERA 6,0, E SUBIU COM O RELOGIO ============================
	/// Este numero nao e uma opiniao independente sobre quanto tempo de tela parada se aguenta: ele e
	/// uma medida do RITMO DAS CENAS, e o ritmo inteiro acabou de crescer 20% quando o divisor do
	/// tique passou de 12 pra 10 (ver <see cref="TiquesPorSegundoNoDm"/> e `Core.TempoDoDm`). Deixar
	/// 6,0 aqui reprovaria as cenas CERTAS -- e o conserto tentador seria apagar a checagem.
	/// =====================================================================================
	///
	/// O DM tem vaos MAIORES que isto (o `sleep(100)` do `SSJCinematic.dm` sao 10,0 s), e mesmo assim o
	/// port os enche: la os `spawn(rand(10,150))` da abertura continuam cuspindo raio e poeira pelo
	/// cenario durante o silencio da linha principal, e aqui nao ha nada equivalente. O silencio do
	/// original e o das FALAS, nao o da tela.
	/// ==================================================================================
	///
	/// A CABECA CONTA COMO VAO (0 -> primeiro beat) e a CAUDA nao: a cena acaba em `ultimo beat + 1,0`
	/// por construcao (`Cinematica.Segundos`), entao nao existe cauda vazia pra medir. A cabeca existe --
	/// uma cena que comecasse aos 4 s trancaria o corpo antes de qualquer coisa acontecer na tela.
	/// </summary>
	private const double VaoMaximoSemBeat = 7.2;

	/// <inheritdoc cref="VaoMaximoSemBeat"/>
	private static (double Vao, double Onde) MaiorVaoSemBeat(Jandirus.Core.Forms.Cinematica c)
	{
		if (c.Beats.Length == 0) return (double.MaxValue, 0);
		double pior = c.Beats[0].Em, onde = 0;      // a CABECA
		for (int i = 1; i < c.Beats.Length; i++)
		{
			double d = c.Beats[i].Em - c.Beats[i - 1].Em;
			if (d > pior) { pior = d; onde = c.Beats[i - 1].Em; }
		}
		return (pior, onde);
	}

	/// <summary>
	/// A SONDA DO VAO, TESTADA CONTRA ELA MESMA -- e o resumo do que ela achou.
	///
	/// ============================ POR QUE ISTO NAO E PARANOIA ============================
	/// A checagem do vao roda 68 vezes (34 cenas x 2 versoes) e da verde nas 68. Verde repetido e o
	/// disfarce perfeito pra uma sonda cega: se o `MaiorVaoSemBeat` devolvesse 0 por um `for` que comeca
	/// no indice errado, o placar ficaria EXATAMENTE igual ao de hoje. Foi assim que a checagem do
	/// fallback de cena sobreviveu meses aqui -- ela so perguntava "devolveu alguma coisa?".
	///
	/// Entao a sonda recebe cenas construidas pra falhar, de duas formas diferentes (um buraco no MEIO e
	/// um na CABECA, que sao ramos diferentes da funcao), e tem que enxergar as duas.
	/// ================================================================================
	/// </summary>
	private void ASondaDeVaoEnxerga()
	{
		var comBuracoNoMeio = new Jandirus.Core.Forms.Cinematica
		{
			Forma = "bancada: buraco no meio",
			SegundosPreso = 22.0,
			Beats =
			[
				new(0.0, Jandirus.Core.Forms.Efeito.Tremor),
				new(2.0, Jandirus.Core.Forms.Efeito.Poeira),
				new(22.0, Jandirus.Core.Forms.Efeito.Assumir),   // 20 s de nada
				new(23.0, Jandirus.Core.Forms.Efeito.Poeira),
			],
		};
		var comBuracoNaCabeca = new Jandirus.Core.Forms.Cinematica
		{
			Forma = "bancada: buraco na cabeca",
			SegundosPreso = 9.0,
			Beats =
			[
				new(8.0, Jandirus.Core.Forms.Efeito.Tremor),     // 8 s de tela parada antes de comecar
				new(9.0, Jandirus.Core.Forms.Efeito.Assumir),
				new(10.0, Jandirus.Core.Forms.Efeito.Poeira),
			],
		};

		Conferir(MaiorVaoSemBeat(comBuracoNoMeio).Vao > VaoMaximoSemBeat,
				 $"a sonda enxerga um buraco de 20 s no MEIO ({MaiorVaoSemBeat(comBuracoNoMeio).Vao:0.#}s)");
		Conferir(MaiorVaoSemBeat(comBuracoNaCabeca).Vao > VaoMaximoSemBeat,
				 $"e um de 8 s na CABECA ({MaiorVaoSemBeat(comBuracoNaCabeca).Vao:0.#}s)");

		// E O CONTRA-CONTRA-TESTE: a mesma sonda tem que APROVAR uma cena bem escrita. Sem esta linha,
		// uma funcao que devolvesse `double.MaxValue` sempre passaria nas duas de cima e reprovaria as 68
		// de verdade -- o que se leria como "as cenas quebraram", e nao como "a sonda quebrou".
		var bemEscrita = new Jandirus.Core.Forms.Cinematica
		{
			Forma = "bancada: sem buraco",
			SegundosPreso = 6.0,
			Beats =
			[
				new(0.0, Jandirus.Core.Forms.Efeito.Tremor),
				new(3.0, Jandirus.Core.Forms.Efeito.Poeira),
				new(6.0, Jandirus.Core.Forms.Efeito.Assumir),
				new(7.0, Jandirus.Core.Forms.Efeito.Poeira),
			],
		};
		Conferir(MaiorVaoSemBeat(bemEscrita).Vao <= VaoMaximoSemBeat,
				 $"e aprova uma cena sem buraco ({MaiorVaoSemBeat(bemEscrita).Vao:0.#}s)");

		double piorDeTodas = 0; string dePior = "";
		foreach (Jandirus.Core.Forms.Cinematica c in Jandirus.Core.Forms.Cinematicas.Todas)
		{
			double v = MaiorVaoSemBeat(c).Vao;
			if (v > piorDeTodas) { piorDeTodas = v; dePior = c.Forma; }
		}
		_passos.Add($"  --     maior vao sem beat em todo o arquivo: {piorDeTodas:0.##}s (`{dePior}`), "
				  + $"limite {VaoMaximoSemBeat:0.#}s");
	}

	// =====================================================================
	// 3. NO CORPO, AO VIVO
	// =====================================================================
	private void NoCorpo()
	{
		Node? mundo = GetTree().Root.FindChild("LocalPlayer", true, false);
		if (mundo is not Node2D corpo) { Conferir(false, "achei o corpo local"); return; }

		var raios = corpo.GetNodeOrNull<RaiosDaForma>("Raios");
		var vis = corpo.GetNodeOrNull<CharacterVisual>("Visual");
		var aura = corpo.GetNodeOrNull<Aura>("Aura");
		Conferir(raios != null, "o corpo tem o node de raios");
		Conferir(vis != null, "o corpo tem o visual");
		Conferir(aura != null, "o corpo tem a aura");
		if (raios == null || vis == null || aura == null) return;

		// --- percorre o roteiro e mede o que MUDA ---
		var vivosPorForma = new List<(string Id, int Vivos, float Contorno)>();
		foreach (string id in Roteiro)
		{
			FormaDef? d = Jandirus.Core.Forms.Catalogo.Def(id);
			if (d == null) { Conferir(false, $"a forma '{id}' existe"); continue; }

			// AS TRES CORES, pelas mesmas funcoes que o jogo usa. Era um `cor` unico da `Aura` nos
			// tres desenhos -- a bancada estaria exercitando um caminho que o `World` nao tem mais.
			raios.Definir(true, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(d)), d.Raios);
			vis.AuraDaForma(new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(d)),
							0.35f + d.Intensidade * 0.13f, ContornoAlterna(d));
			aura.Acender(new Color(d.Aura), 0.8f + d.Intensidade * 0.5f);
			if (primeiraAura)
			{
				primeiraAura = false;
				SpriteDeAura des = aura.DesenhoDeTeste;
				Conferir(Mathf.Abs(des.BaseDeTeste - SpriteDeAura.LinhaDosPes) <= 2f,
						 $"a aura persistente nasce no pe (base {des.BaseDeTeste:0.#}, pe {SpriteDeAura.LinhaDosPes})");
				// ============================ O RECORTE, COM O NUMERO DE CADA FOLHA ============================
				// Esta checagem cravava 32 -- o recorte da `Aurabigcombined`, que era a folha da aura
				// quando eu a escrevi. Ao trocar a base pra `colorablebigaura` ela reprovou com o
				// codigo CERTO, porque o numero estava colado numa folha e nao na folha em uso.
				//
				// Medido nas tres: `colorablebigaura` e `AuraLSSjBig` sao 96x96 (arte ocupando
				// x 0..90, y 0..95 -- ela chega na ultima linha, por isso a ancora -32 poe a base no
				// pe); a `Aurabigcombined` e 32x64 (a arte so ocupa os 32 px da esquerda de cada
				// celula de 64, e foi por isso que o recorte dela teve que ser refeito).
				//
				// O que isto protege continua sendo o mesmo: um `.tres` que volte a carregar vazio
				// grudado na arte tira a chama de cima do corpo.
				// ================================================================================================
				Conferir(Mathf.IsEqualApprox(des.LarguraDeTeste, 96f),
						 $"a folha base tem quadro de 96 px (tem {des.LarguraDeTeste:0.#})");

				// A TROCA DE FOLHA E O QUE PODE QUEBRAR, porque a ancora depende da ALTURA do quadro:
				// trocar `SpriteFrames` sem remontar deixaria o offset da folha velha. Se as duas
				// tivessem alturas diferentes, isto pegaria; como hoje as duas sao 96, a checagem e
				// barata e continua valendo quando alguem trocar uma das artes.
				des.DefinirFolha(SpriteDeAura.FolhaLssj);
				Conferir(Mathf.Abs(des.BaseDeTeste - SpriteDeAura.LinhaDosPes) <= 2f,
						 $"a folha do LSSJ tambem nasce no pe (base {des.BaseDeTeste:0.#})");
				_passos.Add($"  --     folha LSSJ: base {des.BaseDeTeste:0.#} px, quadro {des.LarguraDeTeste:0.#} px");
				des.DefinirFolha(SpriteDeAura.FolhaBase);
				Conferir(Mathf.Abs(des.BaseDeTeste - SpriteDeAura.LinhaDosPes) <= 2f,
						 "volta pra folha base sem perder a ancora");
				_passos.Add($"  --     aura: base {des.BaseDeTeste:0.#} px, largura {des.LarguraDeTeste:0.#} px");
			}

			vivosPorForma.Add((id, raios.VivosDeTeste, vis.AuraDaFormaDeTeste));
		}

		foreach ((string id, int vivos, float contorno) in vivosPorForma)
			_passos.Add($"  --     {id}: {vivos} raio(s), contorno {contorno:0.##}");

		// O VOLUME TEM QUE MUDAR ENTRE FORMAS. Se todos derem o mesmo numero, o campo `Raios` foi
		// escrito no catalogo e nao chegou no node -- a falha assinatura deste projeto.
		//
		// SAO TRES VALORES E NAO MAIS: o zero de quem nao tem faisca, o teto do LEVE e o teto do
		// CHEIO. Era `>= 3`, e o `>=` era o frouxo: o catalogo tem exatamente dois volumes acesos,
		// entao um quarto valor so pode ter nascido de alguem reintroduzir uma intensidade sem passar
		// pelo catalogo (o `Definir` prende em 0..2, entao ela sairia TRUNCADA e nao errada -- o tipo
		// de defeito que nenhuma tela mostra).
		int[] volumes = [.. vivosPorForma.Select(v => v.Vivos).Distinct().Order()];
		Conferir(volumes.Length == 3 && volumes[0] == 0,
				 $"o volume de raios sai em tres degraus, e o primeiro e zero ({string.Join("/", volumes)})");
		Conferir(volumes.Length == 3 && volumes[1] < volumes[2],
				 $"o cheio acende mais que o leve ({string.Join(" < ", volumes[1..])})");

		Conferir(vivosPorForma.First(v => v.Id == "ssj1").Vivos == 0, "o SSJ1 nao acende raio nenhum");
		Conferir(vivosPorForma.First(v => v.Id == "grade2").Vivos == 0,
				 "e o Grade 2 tambem nao (\"grade2 e grade3 nao tem raio mesmo, pode zerar\")");
		Conferir(vivosPorForma.First(v => v.Id == "ssj3").Vivos
				 > vivosPorForma.First(v => v.Id == "ssj2").Vivos,
				 "o SSJ3 acende MAIS que o SSJ2 (tres folhas contra uma, no DM)");
		Conferir(vivosPorForma.First(v => v.Id == "ssj3").Vivos
				 == vivosPorForma.First(v => v.Id == "primal_legendary2").Vivos,
				 "e o LSSJ2 do Primal Legendary acende IGUAL ao SSJ3 (e o mesmo `if(3)` do DM)");
		// OS DOIS QUE VOLTARAM. Medidos pelo NODE e nao pelo catalogo: o campo pode estar certo e o
		// valor nao chegar no emissor, que e a falha assinatura deste projeto.
		Conferir(vivosPorForma.First(v => v.Id == "primal_legendary3").Vivos
				 == vivosPorForma.First(v => v.Id == "primal_legendary2").Vivos,
				 "o LSSJ3 acende o MESMO que o LSSJ2 -- subir a escada nao pode APAGAR faisca");
		Conferir(vivosPorForma.First(v => v.Id == "ssj4_limit_breaker").Vivos
				 == vivosPorForma.First(v => v.Id == "ssj3").Vivos,
				 "e o Limit Breaker voltou a acender, no cheio (era zero ate o dono corrigir)");

		// ============================ A TAXA, QUE E O QUE O DONO PEDIU ============================
		// "Poucos e nao toda hora -- um raio a cada 2 segundos." O numero que responde isso nao e
		// quantas particulas existem, e `Amount / Lifetime`. Antes desta checagem a bancada passava
		// com SETENTA raios por segundo.
		//
		// AS DUAS PONTAS TROCARAM DE DONO: o leve era o `grade2`, que hoje esta em `Raios = 0` --
		// medir a taxa nele seria medir o efeito DESLIGADO, que passa em qualquer teto. Hoje as
		// pontas sao o `ssj2` (o unico leve que restou) e o `ssj3`. O forte era o
		// `ssj4_limit_breaker`, que voltou a acender mas empatado com o SSJ3 no cheio: as duas pontas
		// de VOLUME hoje sao 1 e 2, e nao ha mais uma terceira pra medir.
		// =====================================================================================
		FormaDef leve = Jandirus.Core.Forms.Catalogo.Def("ssj2")!;
		raios.Definir(true, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(leve)), leve.Raios);
		Conferir(raios.PorSegundoDeTeste <= 0.8f,
				 $"crepitar leve fica perto de um raio a cada 2s ({raios.PorSegundoDeTeste:0.##}/s)");

		FormaDef forte = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		raios.Definir(true, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(forte)), forte.Raios);
		Conferir(raios.PorSegundoDeTeste <= 1.6f,
				 $"nem a forma mais forte passa de 1,5 raio/s ({raios.PorSegundoDeTeste:0.##}/s)");
		Conferir(raios.VivosDeTeste == 4, $"a rajada mais forte chega a 4 raios ({raios.VivosDeTeste})");

		// ============================ O RAIO NAO PODE IR LONGE ============================
		// Um corpo tem 32 px. O raio corre POR FORA dele, entao pode passar da silhueta -- mas se
		// passar de um tile e meio deixa de parecer eletricidade do personagem e vira raio do
		// cenario, que foi a queixa do dono.
		//
		// A checagem soma os TRES termos (caixa, deriva da subida, meio quad escalado). Conferir so
		// a caixa foi o meu erro: eu a apertei e os outros dois continuaram jogando o raio longe.
		// =============================================================================
		float alcance = raios.AlcanceMaximoDeTeste;
		_passos.Add($"  --     alcance maximo de um raio: {alcance:0} px ({alcance / 32f:0.0} tiles)");
		Conferir(alcance <= 48, $"o raio nao passa de 1,5 tile do centro ({alcance:0} px)");
		Conferir(alcance >= 20, $"e nao ficou preso dentro do corpo ({alcance:0} px)");

		// E OS ARCOS: sem eles, "alguns raios contornam o personagem" seria so comentario.
		var shRaio = GD.Load<Shader>("res://Assets/Shaders/RaioDaForma.gdshader");
		Conferir(shRaio?.Code?.Contains("arco") == true, "o shader desenha raios em arco (o 'C')");

		// ============================ O SORTEIO PRECISA SORTEAR ============================
		// "Aleatorio" e a palavra mais facil de escrever e a mais facil de nao entregar: um sorteio
		// que devolve sempre o mesmo numero passa em toda checagem de faixa. Entao aqui a rajada e
		// disparada muitas vezes e se conta QUANTOS VALORES DISTINTOS sairam.
		// ==============================================================================
		var tamanhos = new HashSet<int>();
		int fora = 0, antes = raios.RajadasDeTeste;
		for (int i = 0; i < 400; i++)
		{
			raios.DispararDeTeste();
			int n = raios.UltimaRajadaDeTeste;
			tamanhos.Add(n);
			if (n is < 1 or > 4) fora++;
		}
		_passos.Add($"  --     400 rajadas: tamanhos {string.Join(",", tamanhos.Order())}");
		// ERA "1 A 5": o pool desceu pra 4 junto com a intensidade 3 (ver `RaiosDaForma.Maximo`).
		// Este numero e o do POOL e nao o de uma forma -- se ele voltar a 5, sobra um lugar do buffer
		// que o catalogo nao pode mais pedir.
		Conferir(fora == 0, $"toda rajada fica entre 1 e 4 ({fora} fora da faixa)");
		Conferir(tamanhos.Count == 4, $"os quatro tamanhos aparecem ({tamanhos.Count} distintos)");
		Conferir(tamanhos.Contains(1), "sai rajada de UM raio");
		Conferir(tamanhos.Contains(4), "sai rajada de QUATRO raios (o pool inteiro)");
		Conferir(raios.RajadasDeTeste - antes == 400, "o contador de rajadas anda");

		// E NA INTENSIDADE LEVE O TETO E MENOR -- senao o campo do catalogo nao muda nada.
		raios.Definir(true, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(leve)), leve.Raios);
		var leves = new HashSet<int>();
		for (int i = 0; i < 200; i++) { raios.DispararDeTeste(); leves.Add(raios.UltimaRajadaDeTeste); }
		Conferir(leves.Max() <= 2, $"o crepitar do SSJ2 nunca passa de 2 raios (maior: {leves.Max()})");
		Conferir(leves.Max() < 4, "o teto do SSJ2 e MENOR que o do SSJ3");

		// ============================ A GROSSURA AFINOU SEM PERDER A VARIACAO ============================
		// O pedido do dono tem DUAS metades e uma delas e facil de perder: baixar a media e trivial,
		// manter a variacao e o que um "conserto" futuro apaga sem perceber (basta escrever a
		// grossura pronta no C# e pronto -- todos os raios voltam a sair iguais, e nenhuma foto
		// mostra isso porque a foto tem UM raio).
		//
		// Entao a bancada mede as tres propriedades separadas: (1) a base ainda desce com a
		// intensidade da forma, (2) a queda e MULTIPLICADOR e nao teto -- teto cortaria so o degrau
		// de cima e apagaria a distancia entre SSJ2 e SSJ3 -- e (3) cada raio ainda sorteia a
		// grossura dele.
		// ==========================================================================================
		float baseLeve = raios.GrossuraBaseDeTeste;
		raios.Definir(true, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(forte)), forte.Raios);
		float baseForte = raios.GrossuraBaseDeTeste;
		_passos.Add($"  --     grossura base: leve {baseLeve:0.###} · cheio {baseForte:0.###}"
					+ " (x afinar x sorteio por particula, no shader)");
		Conferir(baseForte < baseLeve,
				 $"a grossura base ainda desce com a forma ({baseForte:0.###} < {baseLeve:0.###})");
		Conferir(raios.BotoesDaGrossuraIntactosDeTeste,
				 "o C# NAO escreve os botoes da grossura (senao o dono perde a afinacao sem recompilar)");
		Conferir(shRaio?.Code?.Contains("grossura * afinar") == true,
				 "a queda da grossura e proporcional (multiplicador, nao teto)");
		Conferir(shRaio?.Code?.Contains("variacao_grossura * (sgrossura") == true,
				 "e cada raio continua sorteando a grossura dele pela propria semente");

		AGrossuraEmPixel(raios, shRaio);

		// O CONTORNO SOBE COM A FORMA.
		Conferir(vivosPorForma.First(v => v.Id == "ssj3").Contorno
				 > vivosPorForma.First(v => v.Id == "ssj1").Contorno,
				 "o contorno do SSJ3 e mais forte que o do SSJ1");

		// OS DOIS CANAIS DE CONTORNO SAO INDEPENDENTES. Levar um soco nao pode apagar a aura.
		vis.Impacto(Colors.White, Colors.Red, Vector2.Right);
		Conferir(vis.AuraDaFormaDeTeste > 0,
				 "o contorno da forma sobrevive a um impacto (canais separados)");

		// --- e a base APAGA tudo ---
		// SAI DA FORMA MAIS CHEIA QUE EXISTE, e por isso e o `ssj3` e nao mais o Limit Breaker:
		// apagar uma forma que ja estava sem raio nao prova apagamento nenhum.
		FormaDef ultima = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		raios.Definir(false, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(ultima)), 0);
		vis.AuraDaForma(Colors.White, 0, null);
		aura.Apagar();
		Conferir(raios.VivosDeTeste == 0, "voltar pra base apaga os raios");
		Conferir(Mathf.IsZeroApprox(vis.AuraDaFormaDeTeste), "voltar pra base apaga o contorno");

		// ============================ A LUZ DA AURA E DA NOITE ============================
		// Pedido do dono. E o teste tem que exercitar os DOIS extremos: um teste que so conferisse
		// "de dia a luz esta fraca" passaria com a luz desligada pra sempre, que e outro defeito.
		// ==============================================================================
		FormaDef alta = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		var corAlta = new Color(alta.Aura);

		Iluminacao.EscuridaoDeTeste(0f);            // meio-dia cheio
		aura.Acender(corAlta, 2.0f);
		float aoMeioDia = aura.EnergiaDeTeste;

		Iluminacao.EscuridaoDeTeste(1f);            // breu
		aura.Acender(corAlta, 2.0f);
		float aMeiaNoite = aura.EnergiaDeTeste;

		// A COR DE DIA DA `Iluminacao` (`dcdcd2`) da escuridao ~0,15 -- e ela vale ANTES de o
		// servidor mandar a hora. Era exatamente esse valor que acendia a luz por alguns quadros
		// ao transformar de dia. Tem que dar ZERO.
		Iluminacao.EscuridaoDeTeste(1 - Iluminacao.AmbienteDia.Luminance);
		aura.Acender(corAlta, 2.0f);
		float antesDaHora = aura.EnergiaDeTeste;

		Iluminacao.EscuridaoDeTeste(0.6f);          // entardecer
		aura.Acender(corAlta, 2.0f);
		float noEntardecer = aura.EnergiaDeTeste;

		_passos.Add($"  --     luz da aura: meio-dia {aoMeioDia:0.##} · entardecer {noEntardecer:0.##} · noite {aMeiaNoite:0.##}");
		Conferir(Mathf.IsZeroApprox(aoMeioDia), $"ao MEIO-DIA a luz nao acende ({aoMeioDia:0.###})");
		Conferir(Mathf.IsZeroApprox(antesDaHora),
				 $"e nem ANTES de o servidor mandar a hora ({antesDaHora:0.###}) -- era o clarao ao transformar");
		Conferir(aMeiaNoite > 0.5f, $"a NOITE a luz acende de verdade ({aMeiaNoite:0.##})");
		Conferir(noEntardecer > 0 && noEntardecer < aMeiaNoite,
				 $"o entardecer fica no meio ({noEntardecer:0.##})");

		// E O DESENHO DA AURA NAO SOME DE DIA -- e ele que diz que a pessoa esta transformada.
		Iluminacao.EscuridaoDeTeste(0f);
		aura.Acender(corAlta, 2.0f);
		Conferir(vis.AuraDaFormaDeTeste >= 0, "o contorno no sprite nao depende da hora");

		Iluminacao.EscuridaoDeTeste(1f);   // deixa a noite pra a foto

		// ============================ O CORPO PROPRIO DO SSJ4 ============================
		// `supersaiyanbuff.dm:245` -- o SSJ4 troca o CORPO (`saiyan4body`), nao so o cabelo. Eu
		// tinha portado a escada inteira como "cabelo + aura" e o SSJ4 saia com o corpo base.
		// =============================================================================
		FormaDef s4 = Jandirus.Core.Forms.Catalogo.Def("ssj4")!;
		Conferir(s4.Corpo == CorpoDeForma.Ssj4, "o SSJ4 tem o corpo dele no catalogo");
		Conferir(CorposDeForma.Existe(CorposDeForma.Ssj4),
				 $"o sprite do corpo esta IMPORTADO ({CorposDeForma.Ssj4.GetFile()})");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("ssj3")!.Corpo == CorpoDeForma.Nenhum,
				 "o SSJ3 NAO troca de corpo (so o SSJ4 em diante)");

		vis.CorpoDaForma(s4.Corpo);
		Conferir(vis.CorpoDaFormaDeTeste, "vestir o corpo do SSJ4 cria a camada");
		Conferir(!vis.EhCriatura, "o SSJ4 e PELAGEM, nao criatura -- a roupa e o cabelo continuam");
		vis.CorpoDaForma(CorpoDeForma.Nenhum);
		Conferir(!vis.CorpoDaFormaDeTeste, "voltar pra base TIRA a camada (senao fica peludo pra sempre)");

		// ============================ O CORPO DO OOZARU E OUTRO BICHO ============================
		// `Oozaru.dm:137-139` troca o ICONE do mob (96x96) e apaga os overlays. Aqui a distincao e
		// DERIVADA do tamanho do quadro -- e essa derivacao e o que este bloco existe pra provar:
		// se um dia o `.tres` do macaco for regerado em 32x32 (ou o caminho apontar pro arquivo
		// errado), o jogo NAO reclamaria -- sairia um macaco do tamanho de uma pessoa com cabelo e
		// camisa por cima, e ninguem saberia por que.
		// O BONECO PRECISA TER RABO ANTES. `RaboVisivelDeTeste` devolve `true` quando NAO HA rabo
		// (nada escondido esta, de fato, escondido), entao conferir "o macaco esconde o rabo" num
		// boneco sem rabo passaria sem provar nada -- o mesmo buraco que o `TemCabeloDeTeste`
		// fecha pro cabelo, tres blocos acima.
		vis.MostrarRabo(true);
		Conferir(vis.RaboVisivelDeTeste, "o boneco de bancada tem rabo (senao o teste abaixo e vazio)");

		foreach (string idMacaco in new[] { "oozaru", "oozaru_dourado" })
		{
			FormaDef mk = Jandirus.Core.Forms.Catalogo.Def(idMacaco)!;
			Conferir(mk.Corpo is CorpoDeForma.Oozaru or CorpoDeForma.OozaruDourado,
					 $"`{idMacaco}` tem corpo proprio no catalogo");
			string folhaMk = CorposDeForma.Caminho(mk.Corpo, "")!;
			Conferir(CorposDeForma.Existe(folhaMk), $"o sprite existe ({folhaMk.GetFile()})");

			vis.CorpoDaForma(mk.Corpo);
			Conferir(vis.EhCriatura, $"`{idMacaco}` e CRIATURA (quadro maior que o do corpo)");
			Conferir(!vis.TemCabeloVisivelDeTeste, $"`{idMacaco}`: o cabelo some (o `RemoveHair()` do DM)");
			Conferir(!vis.RaboVisivelDeTeste, $"`{idMacaco}`: o rabo some -- o macaco tem o proprio");

			// A ANCORA. Se ela estiver errada o macaco fica enterrado no chao (ou pairando), e a
			// unica forma de perceber e olhando uma foto -- que e justamente o que uma bancada
			// existe pra dispensar. -32 e a conta `(32 - 96) / 2` pra a folha do BYOND.
			Conferir(Mathf.IsEqualApprox(vis.AncoraDoCorpoDaFormaDeTeste, -32f),
					 $"`{idMacaco}`: a base do quadro cai na linha dos pes (offset {vis.AncoraDoCorpoDaFormaDeTeste:0.#})");
		}

		vis.CorpoDaForma(CorpoDeForma.Nenhum);
		Conferir(!vis.EhCriatura && vis.TemCabeloVisivelDeTeste,
				 "sair do macaco devolve o cabelo e a roupa (senao o jogador fica pelado pra sempre)");

		// ============================ O CABELO TROCA DE SPRITE, NAO SO DE COR ============================
		// O BYOND troca o overlay INTEIRO (`removeOverlay(hair)` + `updateOverlay(ssj/ssj1)`). Eu
		// tinha portado como TINTA, e o Super Saiyajin saia com o penteado normal, amarelo.
		// A bancada nasce com o cabelo do Goku justamente pra isto ter o que provar.
		// ==========================================================================================
		Conferir(vis.TemCabeloDeTeste, "o personagem de bancada tem cabelo (nao e careca)");
		string normal = vis.CabeloDeTeste;
		_passos.Add($"  --     cabelo base: {normal.GetFile()}");

		vis.CabeloDaForma("SSj");
		string ssj = vis.CabeloDeTeste;
		Conferir(ssj != normal, $"o SSJ1 TROCA o sprite do cabelo ({ssj.GetFile()})");

		vis.CabeloDaForma("SSj3");
		Conferir(vis.CabeloDeTeste != ssj, $"o SSJ3 tem sprite proprio ({vis.CabeloDeTeste.GetFile()})");

		// O GOKU NAO TEM VARIANTE DE SSJ2 na pasta -- ele tem que HERDAR a do SSJ1, e nao voltar
		// ao penteado normal. Voltar seria um passo ATRAS no meio da escada.
		vis.CabeloDaForma("SSj2");
		Conferir(vis.CabeloDeTeste != normal,
				 $"sem variante de SSJ2, HERDA a de baixo em vez de voltar ao normal ({vis.CabeloDeTeste.GetFile()})");

		// ============================ O SSJ4 TEM CABELO PROPRIO E UNICO ============================
		// Este e o defeito que o dono viu na foto: o SSJ4 saiu com o cabelo do SSJ1. A arte tem
		// `Hair_SSj4` -- um cabelo so, igual pra todos -- e nao uma variante por penteado; meu
		// resolvedor procurava `Hair_GokuSSJ4`, nao achava e herdava o de baixo.
		// ======================================================================================
		vis.CabeloDaForma("SSJ4");
		string s4cabelo = vis.CabeloDeTeste;
		Conferir(s4cabelo.Contains("SSj4") || s4cabelo.Contains("SSJ4"),
				 $"o SSJ4 usa o cabelo PROPRIO dele, nao o do SSJ1 ({s4cabelo.GetFile()})");
		Conferir(s4cabelo != ssj, "o cabelo do SSJ4 e diferente do cabelo do SSJ1");

		// AS TRES REGRAS DO SSJ4, direto do dono. Conferidas no resolvedor, sem depender do
		// personagem de bancada ser homem ou mulher.
		Conferir(CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Goku.tres", "SSJ4", false)
				 ?.Contains("Hair_SSj4") == true, "homem comum -> Hair_SSj4");
		Conferir(CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Vegeta.tres", "SSJ4", false)
				 ?.Contains("VegetaSSJ4") == true, "cabelo de Vegeta -> Hair VegetaSSJ4");
		Conferir(CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_GTVegeta.tres", "SSJ4", false)
				 ?.Contains("VegetaSSJ4") == true, "GT Vegeta tambem -> Hair VegetaSSJ4");
		Conferir(CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_FemaleLong.tres", "SSJ4", true)
				 ?.Contains("SSJ4Female") == true, "mulher, qualquer penteado -> Hair_SSJ4Female");
		Conferir(CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Vegeta.tres", "SSJ4", true)
				 ?.Contains("SSJ4Female") == true, "mulher com cabelo de Vegeta -> Female vence");

		vis.CabeloDaForma("");
		Conferir(vis.CabeloDeTeste == normal, "voltar pra base devolve o penteado normal");

		// ============================ E AGORA PELA FORMA, QUE E COMO O JOGO CHAMA ============================
		// As linhas de cima passam SUFIXO na mao, e por isso elas nunca viram o defeito que o dono
		// apontou: o `wrathful` e o `ssg` estavam com `SufixoDoCabelo = "SSj"` no catalogo, e
		// `CabeloDaForma("SSj")` faz a coisa certa com o dado errado. A pergunta que faltava era a que
		// o jogo faz -- `VestirCabeloDaForma(def)` --, e ela mede as TRES saidas de uma vez: sprite,
		// tinta do cabelo e tinta do rabo.
		//
		// A TINTA E LIDA DO MATERIAL (`TintaDoCabeloDeTeste`) e nao do catalogo: o catalogo ja estava
		// certo em versoes anteriores deste passe enquanto NADA aplicava a cor -- era o `PintarCabelo`
		// que tinha o corpo vazio. Conferir o dado teria aprovado o jogo mudo.
		// ================================================================================================
		{
			Vector3 tintaNatural = vis.TintaDoCabeloDeTeste?.Tinta ?? Vector3.Zero;
			bool Pintado() => vis.TintaDoCabeloDeTeste is { } t && !t.Tinta.IsEqualApprox(tintaNatural);
			bool Perto(Vector3 v, string hexa) => v.IsEqualApprox(
				new Vector3(new Color(hexa).R, new Color(hexa).G, new Color(hexa).B));

			void VesteAConfere(string id, bool trocaSprite, string? hexaEsperado, string porque)
			{
				if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } dv) { Conferir(false, $"`{id}` existe"); return; }
				vis.VestirCabeloDaForma(dv);
				bool trocou = vis.CabeloDeTeste != normal;
				Conferir(trocou == trocaSprite,
						 $"`{id}`: {(trocaSprite ? "TROCA" : "MANTEM")} o penteado -- {porque} "
					   + $"({vis.CabeloDeTeste.GetFile()})");
				if (hexaEsperado == null)
					Conferir(!Pintado(), $"`{id}`: e NAO tinge o cabelo");
				else
					Conferir(vis.TintaDoCabeloDeTeste is { } tt && Perto(tt.Tinta, hexaEsperado),
							 $"`{id}`: e tinge de #{hexaEsperado} "
						   + $"({vis.TintaDoCabeloDeTeste?.Tinta.ToString() ?? "sem cabelo"})");
			}

			// O WRATHFUL E O SSG SAO AS DUAS QUEIXAS DO DONO, e sao o mesmo defeito por dois lados:
			// um nao devia trocar NEM pintar, o outro devia pintar SEM trocar.
			VesteAConfere("wrathful", trocaSprite: false, hexaEsperado: null,
						  "cabelo BASE, sem tinta (HairObject.dm:209)");
			VesteAConfere("ssg", trocaSprite: false, hexaEsperado: "e2331c",
						  "cabelo BASE tingido de vermelho (HairObject.dm:73)");
			VesteAConfere("ssj1", trocaSprite: true, hexaEsperado: null,
						  "arte propria e NENHUMA tinta (o veto do dono)");
			VesteAConfere("legendary", trocaSprite: true, hexaEsperado: "7ba81f",
						  "USSj com verde AMARELADO por cima (SaiyanObjects.dm:83)");
			VesteAConfere("primal_legendary", trocaSprite: true, hexaEsperado: "7ba81f",
						  "a intencao verde que o DM tenta e nao entrega");
			// O AZUL NAO E MAIS O `0d49ee` DO DM, E ISSO E UMA CONTA E NAO UM GOSTO: o cabelo de SSJ
			// e DOURADO e a soma nao abaixa canal nenhum, entao aquele valor entregava BRANCO em dois
			// dos quatro tons. Ver `Catalogo.AzulDoCabeloDivino`, onde a medicao esta escrita.
			VesteAConfere("blue", trocaSprite: true, hexaEsperado: "3392c7",
						  "SSj com azul por cima, na escala do MATIZ (SaiyanObjects.dm:18)");
			VesteAConfere("blue_evolution", trocaSprite: true, hexaEsperado: "082b8d",
						  "e o Royale herdou o azul ESCURO do DM (pedido do dono)");
			VesteAConfere("rose", trocaSprite: true, hexaEsperado: "d15694",
						  "SSj com rosa CHICLETE por cima (SaiyanObjects.dm:14)");
			VesteAConfere("ultra_ego", trocaSprite: false, hexaEsperado: "8c32be",
						  "cabelo BASE roxo, sem trocar (UltraEgo.dm:390)");
			VesteAConfere("mistico", trocaSprite: false, hexaEsperado: null,
						  "o Mistico fica com o cabelo NATURAL (Mystic.dm:36)");

			// O BEAST E O UNICO EM MATIZ. Medir so a cor aprovaria o Beast dourado-lavado -- ver
			// `ModoDoCabelo.TrocarERecolorir`.
			VesteAConfere("beast", trocaSprite: true, hexaEsperado: "b6bac4",
						  "o SSJ2 do jogador embranquecido (Mystic.dm:81)");
			Conferir(vis.TintaDoCabeloDeTeste?.Modo == 1,
					 $"e o Beast pinta em MATIZ e nao em soma (modo {vis.TintaDoCabeloDeTeste?.Modo})");

			// O ULTRA INSTINCT: o personagem de bancada USA o cabelo do Goku, entao ele e justamente
			// quem ganha a arte propria -- e quem NAO pode receber prata por cima dela.
			VesteAConfere("ui_perfected", trocaSprite: true, hexaEsperado: null,
						  "o Goku ganha a arte do UI e NAO a prata (UltraInstinct.dm:299)");
			Conferir(vis.CabeloDeTeste.Contains("UltraInstinct"),
					 $"e a arte e mesmo a do Ultra Instinct ({vis.CabeloDeTeste.GetFile()})");
			// E QUEM NAO E GOKU CAI NO OUTRO RAMO -- no resolvedor, que e onde a regra do DM mora.
			Conferir(CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Vegeta.tres",
									   Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstintoPerfeito) == null,
					 "quem nao e Goku nao ganha arte de UI (e recebe a prata por tinta)");

			// E A VOLTA DESFAZ TUDO: sprite, tinta e o registro do natural.
			vis.VestirCabeloDaForma(null);
			Conferir(vis.CabeloDeTeste == normal && !Pintado(),
					 "e sair da forma devolve o penteado E a cor da ficha");
		}

		// =====================================================================
		// O OVERLAY COLADO NO CORPO
		// =====================================================================
		// ============================ O QUE ESTAS LINHAS PROTEGEM ============================
		// A camada colada e a UNICA parte da aparencia da forma que nao aparece em foto de longe: ela
		// tem o tamanho do boneco e mora por cima dele. Um erro de tabela aqui -- o Blue com a folha
		// laranja do SSG, o Rose com a azul -- passa como "a aura ta meio estranha" e sobrevive meses.
		//
		// Sao TRES perguntas, e cada uma pega um defeito diferente:
		//   1. a arte esta IMPORTADA (nao "esta na pasta" -- ver `ColadasDeForma.Existe`);
		//   2. a forma recebe as folhas CERTAS, na quantidade certa;
		//   3. a camada nao herda o que nao e dela (contorno) nem sobra ao trocar de forma.
		// ==================================================================================
		{
			// (1) A ARTE. Quatro folhas, e as quatro tem que carregar de verdade -- este projeto ja
			// achou arte convertida e nunca importada tres vezes.
			int semArte = 0;
			foreach (Jandirus.Core.Forms.FolhaColada fc
					 in System.Enum.GetValues<Jandirus.Core.Forms.FolhaColada>())
			{
				bool ok = ColadasDeForma.Existe(fc);
				if (!ok) semArte++;
				Conferir(ok, $"a folha colada `{fc}` esta IMPORTADA ({ColadasDeForma.CaminhoDa(fc).GetFile()})");
			}
			Conferir(semArte == 0, $"e nenhuma folha colada ficou sem arte ({semArte})");

			// (2) A TABELA DO DONO, forma por forma. Os ids saem do catalogo e nao de uma lista escrita
			// aqui: um degrau divino novo cai na mesma derivacao e aparece nesta contagem sozinho.
			void ColadaConfere(string id, string[] arquivos, string?[] tintas, string porque)
			{
				if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } dv) { Conferir(false, $"`{id}` existe"); return; }
				vis.ColadasDaForma(dv);
				(string Folha, Color Tinta)[] tem = vis.ColadasNoCorpoDeTeste;

				Conferir(tem.Length == arquivos.Length,
						 $"`{id}`: {arquivos.Length} camada(s) colada(s) -- {porque} (deu {tem.Length})");
				if (tem.Length != arquivos.Length) return;

				for (int i = 0; i < arquivos.Length; i++)
				{
					Conferir(tem[i].Folha.GetFile() == arquivos[i],
							 $"`{id}`: a colada {i} e `{arquivos[i]}` (deu `{tem[i].Folha.GetFile()}`)");

					// A TINTA LIDA DO NODE, e nao do catalogo: e a mesma razao das linhas de cabelo la
					// em cima -- o dado ja esteve certo enquanto NADA o aplicava.
					Color esperada = tintas[i] is { } h ? new Color(h) : Colors.White;
					Conferir(tem[i].Tinta.IsEqualApprox(esperada),
							 $"`{id}`: e ela {(tintas[i] == null ? "NAO se pinta (a arte ja vem colorida)" : $"e pintada de #{tintas[i]}")}"
						   + $" -- deu {tem[i].Tinta.ToHtml(false)}");
				}
			}

			const string Powerz = "LSSJpowerz.tres", Cinza = "god - grey.tres";
			const string Deus = "god.tres", DeusAzul = "god blue.tres";
			const string Verde = "6eff8c", Rosa = "ff7ac6";

			// O LEGENDARY E O UNICO COM DUAS, e foi ele que impediu isto de caber em `Catalogo.Folha`.
			ColadaConfere("legendary", [Powerz, Cinza], [null, Verde],
						  "fagulha + a cinza VERDE (EffectLayer.dm:30-33)");
			ColadaConfere("wrathful", [Powerz, Cinza], [null, Verde],
						  "a linha Legendary INTEIRA, do primeiro degrau (lssjbuff.dm:85)");
			ColadaConfere("primal_legendary2", [Powerz, Cinza], [null, Verde],
						  "e a linha Primal vai junto, como ja vai na aura e no contorno");

			// O CORTE ENTRE `god` E `god blue` E O `ssj==0 && lssj==0` DO DM (godki.dm:265).
			ColadaConfere("ssg", [Deus], [null], "ki divino SEM Super Saiyajin por baixo");
			ColadaConfere("rose_ssg", [Deus], [null], "o SSG do Kaio e um SSG comum (godki_mod so vale com ssj)");
			ColadaConfere("blue", [DeusAzul], [null], "ki divino COM Super Saiyajin (godki.dm:295)");
			ColadaConfere("blue_evolution", [DeusAzul], [null], "e o degrau de cima nao muda de folha");

			// O ROSE E A DIVERGENCIA DECLARADA: no DM ele usaria a AZUL. Ver `Catalogo.Coladas`.
			ColadaConfere("rose", [Cinza], [Rosa], "a cinza ROSA, e nao a azul do original");
			ColadaConfere("rose2", [Cinza], [Rosa], "e o Rose 2 acompanha o Rose");

			// (3) O QUE NAO PODE SOBRAR. Trocar de duas camadas pra uma tem que DESCARTAR a segunda --
			// senao a fagulha verde do Legendary fica grudada no Blue pra sempre, que e o tombo do
			// `ussj_saved_icon` do DM um andar acima.
			vis.ColadasDaForma(Jandirus.Core.Forms.Catalogo.Def("legendary"));
			Conferir(vis.ColadasDeTeste == 2, "duas coladas no Legendary");
			vis.ColadasDaForma(Jandirus.Core.Forms.Catalogo.Def("blue"));
			Conferir(vis.ColadasDeTeste == 1,
					 $"e ir do Legendary pro Blue DESCARTA a segunda ({vis.ColadasDeTeste})");

			// ============================ ELA HERDA A POSE DO CORPO, MAS NAO O RELOGIO ============================
			// A razao de isto ser CAMADA e nao folha de aura e que as tres folhas grandes trazem as 24
			// animacoes do corpo: o brilho tem que estar na MESMA pose e na MESMA direcao, senao o boneco
			// fica de perfil com a aura de frente. Isso a linha de baixo confere.
			//
			// O QUADRO e outra pergunta, e a bancada era cega pra ela -- ver o bloco seguinte.
			// ======================================================================================================
			vis.ColadasDaForma(Jandirus.Core.Forms.Catalogo.Def("blue"));
			string[] poses = vis.PosesDasColadasDeTeste;
			Conferir(poses.Length == 1 && poses[0] == vis.PoseDeTeste,
					 $"a colada do Blue anda na MESMA pose do corpo (`{vis.PoseDeTeste}` vs "
				   + $"`{(poses.Length > 0 ? poses[0] : "nenhuma")}`)");

			vis.ColadasDaForma(Jandirus.Core.Forms.Catalogo.Def("legendary"));
			poses = vis.PosesDasColadasDeTeste;
			Conferir(poses.Length == 2 && poses[0] == "default" && poses[1] == vis.PoseDeTeste,
					 "e no Legendary a fagulha (uma animacao so) e a cinza (as 24 poses) convivem "
				   + $"(`{string.Join("` / `", poses)}`)");

			// ============================ O QUADRO ANDA PARADO -- O DEFEITO DO SLIDESHOW ============================
			// Palavras do dono: "os overlays das formas god e lssj estao com baixo fps quando to PARADO,
			// cada frame demora pra trocar, parece um slide show, mas quando ANDO elas voltam a andar em
			// um frame rate bom".
			//
			// PARADO e a unica pose que discrimina, e e por isso que a checagem forca o corpo a parar: o
			// `default_south` do corpo dura 3,300 s (`1,1,1,30`) contra 0,400 s da folha `god - grey` e
			// 0,600 s da `LSSJpowerz`. ANDANDO os dois ciclos sao 0,800 s e QUALQUER implementacao passa.
			//
			// Meio segundo de relogio cobre o ciclo inteiro das duas folhas, entao as duas tem que visitar
			// varios quadros. Na versao antiga o quadro saia da fase do CORPO: meio segundo era 15% do
			// ciclo dele, as duas ficavam no quadro 0 o tempo todo, e esta checagem acusaria 1 quadro.
			//
			// Nao ha tique do motor entre as chamadas (a bancada roda sincrona), entao o tempo aqui e
			// exatamente o que este laco entrega.
			// ======================================================================================================
			vis.SetMotion(Jandirus.Core.World.Facing.South, moving: false);
			Conferir(vis.PoseDeTeste == "default_south",
					 $"o corpo esta PARADO pra medir o pior caso (pose `{vis.PoseDeTeste}`)");

			var quadrosFagulha = new System.Collections.Generic.HashSet<int>();
			var quadrosCinza = new System.Collections.Generic.HashSet<int>();
			for (int passo = 0; passo < 10; passo++)
			{
				vis._Process(0.05);
				int[] q = vis.QuadrosDasColadasDeTeste;
				if (q.Length != 2) continue;
				quadrosFagulha.Add(q[0]);
				quadrosCinza.Add(q[1]);
			}

			Conferir(quadrosCinza.Count >= 3,
					 "com o corpo PARADO, a colada `god - grey` percorre o ciclo DELA em meio segundo "
				   + $"(visitou {quadrosCinza.Count} quadro(s) de 4 -- 1 seria o slideshow)");
			Conferir(quadrosFagulha.Count >= 3,
					 "e a fagulha `LSSJpowerz` tambem, que parada caia no ramo sincronizado "
				   + $"(visitou {quadrosFagulha.Count} quadro(s) de 6)");

			// E A ESCADA SAIYAJIN NAO TEM NENHUMA -- a tabela e do dono e ela nao lista o SSJ.
			vis.ColadasDaForma(Jandirus.Core.Forms.Catalogo.Def("ssj3"));
			Conferir(vis.ColadasDeTeste == 0, $"e o SSJ3 nao cola nada ({vis.ColadasDeTeste})");

			vis.ColadasDaForma(null);
			Conferir(vis.ColadasDeTeste == 0, "e reverter pra base tira todas");
		}

		// A COBERTURA: quantos dos penteados do jogo tem variante. Nao precisa ser 100% -- o
		// resolvedor devolve nulo e o cabelo normal fica -- mas se fosse ZERO, o sistema inteiro
		// estaria escrito e desligado, que e o defeito que esta bancada existe pra pegar.
		string[] bases = [.. System.IO.Directory.GetFiles(
			ProjectSettings.GlobalizePath("res://Assets/Sprites/Hair/"), "*.tres")];
		(int com, int sem) = CabelosDeForma.Cobertura(bases.Select(b => $"res://Assets/Sprites/Hair/{System.IO.Path.GetFileName(b)}"), "SSj");
		_passos.Add($"  --     variantes de SSJ1: {com} penteados COM, {sem} sem (de {com + sem})");
		Conferir(com >= 15, $"boa parte dos penteados tem variante de SSJ ({com})");

		// AS 36 ENTRADAS, UMA A UMA. As linhas acima escolhem doze formas a dedo; esta varre o
		// catalogo inteiro em dois bonecos. Ela nao usa o corpo da bancada -- monta os proprios --,
		// entao pode rodar aqui sem perturbar as medidas de cima.
		OCabeloDeCadaFormaNoCatalogo();

		// O DESENHO DO OLHO, MEDIDO. Roda logo depois porque e ela que sustenta a coluna `Olho` da
		// tabela acima -- sem ela, "sem iris = `fcfdfd`" e afirmacao minha e nao fato do arquivo.
		OOlhoNoDesenho();

		// O CORPO INCHADO. Monta os proprios bonecos (como as duas de cima), entao pode rodar aqui
		// sem perturbar as medidas do corpo vivo.
		OCorpoInchado();

		// ============================ A VARREDURA DO ENUNCIADO DO DONO ============================
		// Os seis blocos abaixo respondem, na ordem, aos seis pedidos desta rodada: o overlay de cada
		// forma e a arte que manda na tinta; o azul do Blue medido no RESULTADO; a chama propria do
		// Rose; o olho da linha lendaria e a excecao do Wrathful; o rabo medido no desenho; e o Mistico,
		// que so ganhou a faisca.
		//
		// TODOS SAO DE DADO E DE PIXEL (os dois que precisam de boneco montam o proprio), entao eles
		// podem rodar aqui sem perturbar o corpo vivo -- a unica excecao e a `AAuraDoRose`, que troca a
		// folha do desenho persistente e a devolve na ultima linha dela.
		// ======================================================================================
		AsDuasContasSaoAsDoShader();
		AsColadasNoCatalogoInteiro();
		// A MEDICAO NA CAMADA vem logo depois da do catalogo de proposito: as duas fazem a mesma pergunta
		// em dois lugares, e foi a distancia entre esses dois lugares que deixou os dois defeitos passarem
		// (o catalogo certo, o pixel cinza; o quadro "certo", o ritmo do corpo). Boneco proprio, entao ela
		// nao perturba o corpo vivo nem as medidas de pose de cima.
		AColadaMedidaNaCamada();
		OAzulDoBlueNoCabeloLoiro();
		AAuraDoRose(aura.DesenhoDeTeste);
		OOlhoDaLinhaLendaria();
		ORaboMedidoNoDesenho();
		OMisticoSoGanhouAFaisca();

		// O CORPO TOMADO. Antes do bloco da fera, pelo mesmo motivo que ele: daqui pra baixo a
		// bancada deixa cenas vivas segurando o corpo, e `SetMotion` travado nao mexe em nada.
		OCorpoTomado(corpo, vis);

		// A FERA. Roda AQUI, e nao depois, porque daqui pra baixo esta bancada deixa cenas vivas
		// segurando o corpo -- e as checagens de pose do macaco passam pelo `SetPose`/`SetMotion`,
		// que o `_travado` faz virar no-op. Um teste que nao chega a mexer em nada passa sempre.
		AFera(corpo, vis, aura);

		// ============================ A CENA RODA MESMO? ============================
		// O resto desta bancada confere os DADOS da cinematica. Isto exercita o TOCADOR: os beats
		// disparam, o corpo e preso e -- o que mais importa -- ele e SOLTO. Uma cena que prende e
		// nao solta paralisa o jogador pra sempre, e e um defeito que so aparece jogando.
		// ========================================================================
		// ============================ A MAIS CURTA, PERGUNTADA E NAO ESCOLHIDA ============================
		// Este teste espera a cena INTEIRA correr, entao ele quer a mais curta que existir -- cada
		// segundo aqui e um segundo a mais de bancada. Isto era `Cinematicas.Ssj4` cravado, com o
		// comentario "porque ela e a mais CURTA (solta aos 4 s, acaba em 5)".
		//
		// Deixou de ser: com os prazos do DM restaurados a cena do SSJ4 passou pra 17,8 s, e o teste
		// continuaria conferindo aos 6,5 -- ou seja, acusaria "a cena nao soltou o corpo" com o codigo
		// perfeitamente certo. E o MESMO erro que o comentario original ja contava ter cometido uma
		// vez (com a do SSJ1), e ele se repetiu porque a escolha era um literal em vez de uma pergunta.
		// ============================================================================================
		Jandirus.Core.Forms.Cinematica cena = Jandirus.Core.Forms.Cinematicas.Todas.MinBy(c => c.Segundos)!;
		_esperaDaCena = cena.Segundos + 1.5;   // a folga que o tocador precisa pra se liberar sozinho
		FormaDef daCena = Jandirus.Core.Forms.Catalogo.Def(cena.Forma)!;
		Transformacao t = Transformacao.Rodar(corpo.GetParent(), corpo, daCena, cena, souEu: true);
		_cena = t;
		Conferir(Transformacao.PrendendoOCorpo, "a cena PRENDE o corpo ao comecar");

		// ============================ A TRANCA E O ALVO DO TESTE, NAO A GUARDA ============================
		// A versao anterior guardava o CHAMADOR (uma linha por quadro no `LocalPlayer`) e esta
		// bancada passava 150/150 enquanto o jogo real mostrava o personagem andando -- porque a
		// bancada monta um corpo avulso e NUNCA passa pelo `LocalPlayer`. Ela media a guarda errada.
		//
		// Agora ela mede o que o jogo mede: pede pro corpo andar e confere que ele nao anda. Sem a
		// tranca em `CharacterVisual` estes tres pedidos passam e a pose vira `walk_east`.
		// ================================================================================================
		// ============================ A ANCORA DA AURA, MEDIDA E NAO OLHADA ============================
		// Tres tentativas de offset erradas (-8, -22, -16) sairam de eu julgar FOTO. A foto da
		// bancada ainda por cima vinha tomada da fumaca da propria cena. Numero e melhor que olho:
		// a base da chama tem que cair na linha dos pes, e a mesma arte tem que ter a mesma base
		// nos DOIS desenhos (o persistente e o da cinematica).
		// ================================================================================================
		Conferir(Mathf.Abs(t.BaseDaAuraGrandeDeTeste - SpriteDeAura.LinhaDosPes) <= 2f,
				 $"a aura GRANDE nasce no pe (base {t.BaseDaAuraGrandeDeTeste:0.#}, pe {SpriteDeAura.LinhaDosPes})");

		// ============================ MEDIR CRESCIDA, NAO SO NO INSTANTE ZERO ============================
		// A cena escala a aura ate ~1,36 enquanto ela cresce, e no Godot o `Scale` multiplica o
		// `Offset` junto -- com o sprite escalando em torno do proprio centro (altura do peito), a
		// base descia quase 6 px por baixo do chao. Uma checagem que so mede em t=0 mede o instante
		// em que ainda nao aconteceu nada, e foi por isso que a primeira versao passou verde.
		foreach (float esc in new[] { 1.12f, 1.24f, 1.36f, 2.0f })
		{
			t.EscalarAuraDeTeste(esc);
			Conferir(Mathf.Abs(t.BaseDaAuraGrandeDeTeste - SpriteDeAura.LinhaDosPes) <= 2f,
					 $"a aura grande NAO afunda ao crescer {esc:0.00}x "
				   + $"(base {t.BaseDaAuraGrandeDeTeste:0.#}, pe {SpriteDeAura.LinhaDosPes})");
		}
		t.EscalarAuraDeTeste(1f);

		// A ANCORA E REGRA, e a regra tem que valer pra qualquer folha -- as proximas (`Aura, Big`,
		// `AuraLSSJBig`) vao ter outras alturas. Se alguem cravar um numero no lugar da conta, estas
		// tres linhas caem.
		Conferir(Mathf.IsEqualApprox(SpriteDeAura.AncoraPara(64).Y, -16f), "ancora de quadro 64 = -16");
		Conferir(Mathf.IsEqualApprox(SpriteDeAura.AncoraPara(96).Y, -32f), "ancora de quadro 96 = -32");
		Conferir(Mathf.IsEqualApprox(SpriteDeAura.AncoraPara(32).Y, 0f), "ancora de quadro 32 = 0");

		// ============================ A CHAMA DA CENA E A AURA DA PROPRIA FORMA ============================
		// O dono: *"vamos trocar das cinematicas o Aurabigcombined pela propria aura da transformaçao q
		// vc ta virando"*. A cena nasce na folha da forma DELA -- e e por isso que as tres linhas de
		// ancora aqui em cima passaram a medir alguma coisa nova: elas agora medem a `colorablebigaura`
		// de 96 px no lugar do recorte de 32x64 que a arte antiga tinha.
		//
		// A PERGUNTA E FEITA PELA MESMA TRADUCAO QUE O JOGO USA (`SpriteDeAura.CaminhoDa` sobre
		// `Catalogo.Folha`), e nao pela string do arquivo: escrever `AuraSSjBig.tres` aqui faria esta
		// linha reprovar no dia em que alguem trocasse a arte de lugar, com o codigo certo.
		// ==============================================================================================
		Conferir(t.ChamaDaCenaDeTeste.FolhaDeTeste
					 == SpriteDeAura.CaminhoDa(Jandirus.Core.Forms.Catalogo.Folha(daCena)),
				 $"a chama da cena nasce na folha da forma dela, `{daCena.Id}` "
			   + $"({t.ChamaDaCenaDeTeste.FolhaDeTeste.GetFile()})");

		// ============================ SEM ISTO A MEDIDA DO DEGRAU E CEGA ============================
		// A cena do SSJ3 veste base -> SSJ1 -> SSJ2 -> SSJ3, e o `AAparenciaInteiraDoDegrau` cobra que a
		// chama acompanhe cada um. Se a escada inteira caisse numa folha so, aquela cobranca passaria
		// verde com um `Vestir` que NUNCA trocasse a folha -- e o defeito ("a cena mostra a aura do SSJ3
		// enquanto o corpo ainda e SSJ1") voltaria por baixo de um placar limpo.
		//
		// E DADO PURO, sem corpo nenhum: nao ha o que restaurar depois.
		// ==========================================================================================
		FormaDef daEscada = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Cinematicas.Ssj3.Forma)!;
		int folhasNaEscada = Jandirus.Core.Forms.Cinematicas.EscadaDaCena(daEscada).Append(daEscada)
			.Select(Jandirus.Core.Forms.Catalogo.Folha).Distinct().Count();
		Conferir(folhasNaEscada >= 2,
				 $"e a escada da cena de `{daEscada.Id}` passa por mais de uma folha ({folhasNaEscada})");

		// ============================ A FOLHA DE CADA FORMA, NO CATALOGO INTEIRO ============================
		// "toda forma usa colorablebigaura.png MENOS o LSSJ". Percorrer as 33 entradas e o que
		// transforma "esqueci de marcar o degrau novo" em REPROVA, em vez de sair com a aura errada
		// e ninguem notar ate alguem virar Legendary.
		var porFolha = new Dictionary<Jandirus.Core.Forms.FolhaDeAura, int>();
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			// SEIS FOLHAS AGORA. As tres velhas (base colorivel, LSSJ e a `AuraSSjBig` ja dourada) mais
			// as tres divinas que a varredura desenterrou: a `FieryGod` quente (SSG e a linha Prodigial
			// inteira, que roda com `ssj == 0`), a `FieryGodBlue` fria (Blue) e a MESMA fria tingida de
			// rosa (Rose). Ver `FolhaDeAura` no Core, entrada por entrada.
			//
			// A ESPERADA E RECALCULADA AQUI, e nao lida do `Catalogo.Folha`: o ponto e comparar duas
			// contas independentes. Uma bancada que chamasse a funcao e conferisse contra ela mesma
			// aprovaria qualquer derivacao, inclusive "devolve Base sempre".
			bool ehBaseDoCatalogo = d.Id == Jandirus.Core.Forms.Catalogo.IdBase;
			var esperada = d.Linha switch
			{
				Jandirus.Core.Forms.LinhaDeForma.Legendary
					or Jandirus.Core.Forms.LinhaDeForma.LegendaryPrimal => Jandirus.Core.Forms.FolhaDeAura.Lssj,
				Jandirus.Core.Forms.LinhaDeForma.Saiyajin
					or Jandirus.Core.Forms.LinhaDeForma.Futuro when !ehBaseDoCatalogo
						=> Jandirus.Core.Forms.FolhaDeAura.Ssj,
				Jandirus.Core.Forms.LinhaDeForma.GodKi => d.Ordem >= 20
					? Jandirus.Core.Forms.FolhaDeAura.DeusFrio : Jandirus.Core.Forms.FolhaDeAura.DeusQuente,
				Jandirus.Core.Forms.LinhaDeForma.GodKiRose => d.Ordem >= 20
					? Jandirus.Core.Forms.FolhaDeAura.DeusRosa : Jandirus.Core.Forms.FolhaDeAura.DeusQuente,
				// A LINHA DO MISTICO NAO TEM RAMO, e a ausencia e a regra: havia aqui um
				// `Mistico => DeusQuente` (o ramo do DM) e o dono derrubou os dois degraus --
				// *"o mistico e beast tao usando a aura de carga do ssj god"*. Eles caem na COLORIVEL,
				// que e a unica folha que aceita a cor que cada um deles ja declara. Ver `Catalogo.Folha`.
				// A LINHA DO ULTRA INSTINTO NAO TEM FOLHA: ela tem a NUVEM. Ela caia neste `_ => Base`
				// -- e era o defeito, nao a regra: o `colorablebigaura` acendia POR CIMA da nebulosa.
				Jandirus.Core.Forms.LinhaDeForma.UltraInstinct => Jandirus.Core.Forms.FolhaDeAura.Nebulosa,
				// E O `ultra_ego` TAMBEM, mas SO ele: *"a aura/carga do ultra ego e a mesma do instinto
				// superior so q ROXA"*. A `destroyer` (Ordem 10) fica na colorivel com o `9b4dff` dela --
				// o cabelo e a cena sao a diferenca visual entre as duas (`UltraEgo.dm:395-396`), e a
				// nuvem nas duas apagaria isso. O corte por `Ordem` e o mesmo das divinas acima.
				Jandirus.Core.Forms.LinhaDeForma.UltraEgo when d.Ordem >= 20
					=> Jandirus.Core.Forms.FolhaDeAura.Nebulosa,
				_ => Jandirus.Core.Forms.FolhaDeAura.Base,
			};
			var f = Jandirus.Core.Forms.Catalogo.Folha(d);
			Conferir(f == esperada, $"folha de `{d.Id}` ({d.Linha}) = {esperada}");
			porFolha[f] = porFolha.GetValueOrDefault(f) + 1;
		}
		// UMA CONTAGEM POR FOLHA, porque "todas Base" tambem passaria em todas as linhas acima se o
		// resolvedor devolvesse Base sempre. As SETE tem que aparecer -- uma folha com zero entradas e
		// arte importada e morta, que e exatamente o que as duas divinas eram ate esta varredura.
		//
		// A SETIMA NAO E ARTE: e a `Nebulosa`, o simbolo que diz "esta forma nao usa folha" (ver o Core).
		// Ela conta aqui pelo mesmo motivo das outras -- se a linha do Ultra Instinto voltasse a cair na
		// `Base`, este numero cairia pra seis e a chama voltaria a acender por cima da nuvem.
		Conferir(porFolha.Count == 7,
				 "o catalogo usa as SETE folhas ("
			   + string.Join(", ", porFolha.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")) + ")");

		// ============================ E TODAS EXISTEM MESMO NO DISCO ============================
		// Percorrer o ENUM e nao uma lista escrita a mao: foi assim que as duas divinas passaram anos
		// convertidas, importadas e nao citadas por nenhum `.cs`. Uma folha nova que nao tenha `.tres`
		// (ou que o Godot nunca tenha importado) reprova aqui, e nao vira uma aura invisivel em jogo.
		//
		// ============================ E O SIMBOLO SEM ARQUIVO E COBRADO PELO AVESSO ============================
		// A `FolhaDeAura.Nebulosa` NAO tem `.tres`, de proposito -- ela e o jeito de o Core dizer "esta
		// forma nao usa folha" (o Ultra Instinto desenha a nuvem). Pular a entrada em silencio seria
		// abrir um buraco do tamanho do enum: qualquer folha nova que alguem esquecesse de traduzir
		// devolveria nulo e passaria por aqui sem uma palavra.
		//
		// Entao o nulo e uma AFIRMACAO e nao uma excecao: exatamente UM simbolo pode nao ter arquivo, e
		// tem que ser a `Nebulosa`. Um `CaminhoDa` que devolvesse nulo pra qualquer outro cai aqui.
		// ==================================================================================================
		foreach (Jandirus.Core.Forms.FolhaDeAura fl in Enum.GetValues<Jandirus.Core.Forms.FolhaDeAura>())
		{
			if (SpriteDeAura.CaminhoDa(fl) is not { } cm)
			{
				Conferir(fl == Jandirus.Core.Forms.FolhaDeAura.Nebulosa,
						 $"a folha {fl} nao tem arquivo, e a UNICA que pode nao ter e a Nebulosa "
					   + "(o simbolo de 'esta forma nao usa folha')");
				continue;
			}
			Conferir(ResourceLoader.Exists(cm), $"a folha {fl} existe e esta IMPORTADA ({cm.GetFile()})");
		}

		// ============================ E "EXISTE" NAO E "IMPORTADA": ELAS TEM QUE CARREGAR ============================
		// O `ResourceLoader.Exists` responde sobre o ARQUIVO `.tres`, e o `.tres` e so uma lista de
		// quadros apontando pra um `.png`. Este projeto ja pagou exatamente esse buraco: **35 atlas de
		// animacao escritos e NUNCA importados**, 178 animacoes mortas -- os `.tres` estavam todos la,
		// e o Godot nunca tinha gerado o `.ctex` do `.png` que eles citam.
		//
		// Um `.tres` que carrega e devolve zero quadros (ou quadros com textura NULA) desenha
		// exatamente nada em jogo, sem erro nenhum no console: a aura simplesmente nao aparece, que e o
		// sintoma mais facil de confundir com "a forma nao acende aura". Por isso a pergunta aqui e
		// pelo QUADRO e nao pelo caminho -- e ela e feita no enum inteiro, e nao nas que alguem lembrou.
		//
		// COMO REPROVA SE A REGRA SUMIR: apague o `.import` de qualquer `Assets/Sprites/Auras/*.png` --
		// a linha do arquivo continua verde e esta aqui cai, dizendo qual folha e quantos quadros ela deu.
		// ========================================================================================================
		foreach (Jandirus.Core.Forms.FolhaDeAura fl in Enum.GetValues<Jandirus.Core.Forms.FolhaDeAura>())
		{
			// A `Nebulosa` nao tem arquivo pra carregar -- a linha de cima ja cobrou que ela e a unica.
			if (SpriteDeAura.CaminhoDa(fl) is not { } caminho) continue;
			var folha = ResourceLoader.Load<SpriteFrames>(caminho);
			int quadros = 0, semTextura = 0;
			if (folha != null)
				foreach (string anim in folha.GetAnimationNames())
					for (int q = 0; q < folha.GetFrameCount(anim); q++)
					{
						quadros++;
						Texture2D? tex = folha.GetFrameTexture(anim, q);
						if (tex == null || tex.GetWidth() == 0 || tex.GetHeight() == 0) semTextura++;
					}

			Conferir(folha != null && quadros > 0 && semTextura == 0,
					 $"a folha {fl} CARREGA e todo quadro dela tem pixel "
				   + $"({quadros} quadro(s), {semTextura} sem textura -- {caminho.GetFile()})");
		}

		// ============================ NENHUMA REDE PODE SER MENOR QUE A CENA ============================
		// As redes de seguranca existem pra "preso pra sempre" nao ser alcancavel -- mas uma rede
		// mais CURTA que a cena solta o jogador no meio da cinematica, que e o defeito que ela
		// deveria impedir, ao contrario. Foi o que aconteceu: o prazo do `LocalPlayer` era 40 s
		// cravado e a cena do SSJ3 passou a prender 140 s.
		//
		// Esta checagem afirma a RELACAO, nao os numeros: qualquer cena nova mais longa que as redes
		// reprova aqui, em vez de ser descoberta por alguem andando no meio da propria estreia.
		// ================================================================================================
		{
			double maisLonga = Jandirus.Core.Forms.Cinematicas.CenaMaisLonga;
			double maiorPreso = 0;
			foreach (Jandirus.Core.Forms.Cinematica cn in Jandirus.Core.Forms.Cinematicas.Todas)
				if (cn.SegundosPreso > maiorPreso) maiorPreso = cn.SegundosPreso;

			Conferir(Transformacao.PrazoMaximoPreso > maiorPreso,
					 $"a rede da TRANCA cobre a cena que mais prende "
				   + $"({Transformacao.PrazoMaximoPreso:0.#}s contra {maiorPreso:0.#}s)");
			Conferir(LocalPlayer.SegundosAteDestravarDeTeste > maiorPreso,
					 $"a rede do CORPO cobre a cena que mais prende "
				   + $"({LocalPlayer.SegundosAteDestravarDeTeste:0.#}s contra {maiorPreso:0.#}s)");
			_passos.Add($"  --     cena mais longa {maisLonga:0.#}s, prende no maximo {maiorPreso:0.#}s; "
					  + $"redes em {Transformacao.PrazoMaximoPreso:0.#}s e "
					  + $"{LocalPlayer.SegundosAteDestravarDeTeste:0.#}s");
		}

		// ============================ TRANSFORMAR NAO ACENDE AURA ============================
		// Regra do dono: a aura so nasce da tecla C ou do Ki acima de 100%. Antes ela vinha junto
		// com a forma, e por isso o personagem aparecia aceso sem ter feito nada. `Preparar` guarda
		// a cor e a folha sem puxar o gatilho.
		// ================================================================================
		// ============================ OS DOIS DESENHOS TEM QUE CONCORDAR ============================
		// A `CargaVisual` monta o proprio `SpriteDeAura`. Testar so o node `Aura` foi o que me deixou
		// declarar "resolvido" duas vezes com o jogo errado: em Super Saiyajin, o C acendia a folha
		// da BASE tingida. Aqui se confere que os DOIS respondem a mesma regra.
		// ================================================================================================
		if (corpo.GetNodeOrNull<CargaVisual>("Carga") is { } cgt)
		{
			// O ENUM INTEIRO, e nao tres escolhidos: e a mesma regra do bloco de disco acima -- uma
			// folha nova que so um dos dois desenhos conhecesse sairia certa na aura e errada na carga.
			foreach (Jandirus.Core.Forms.FolhaDeAura fl in Enum.GetValues<Jandirus.Core.Forms.FolhaDeAura>())
			{
				aura.Folha(fl);
				cgt.Folha(fl);

				// ============================ E O SIMBOLO SEM ARQUIVO CALA OS DOIS, NO MESMO INSTANTE ============================
				// Este e o requisito "nao pode empilhar duas chamas" medido na sua forma mais forte. A
				// queixa original do dono era uma chama por cima da outra (foto); em Ultra Instinto o
				// numero certo de chamas nao e uma, e ZERO -- o desenho e a nuvem.
				//
				// Os DOIS sao perguntados porque sao dois `SpriteDeAura` distintos, e este arquivo ja
				// registrou o que custa ensinar so um deles ("em Super Saiyajin o C acendia a folha da
				// BASE tingida"). `SemFolha` e a medida certa e nao `Visible`: um sprite apagado por estar
				// em repouso tambem daria `Visible == false`, e o que se quer saber e se ele PODE acender.
				// ============================================================================================================
				if (SpriteDeAura.CaminhoDa(fl) is not { } esperado)
				{
					Conferir(aura.DesenhoDeTeste.SemFolha && cgt.DesenhoDeTeste.SemFolha,
							 $"pra {fl} os DOIS desenhos ficam sem folha -- nenhuma chama pode acender "
						   + $"(aura {aura.DesenhoDeTeste.SemFolha}, carga {cgt.DesenhoDeTeste.SemFolha})");
					Conferir(!aura.DesenhoDeTeste.Visible && !cgt.DesenhoDeTeste.Visible,
							 $"e nenhum dos dois esta desenhando pra {fl}");
					continue;
				}

				Conferir(aura.DesenhoDeTeste.FolhaDeTeste == esperado && !aura.DesenhoDeTeste.SemFolha,
						 $"a AURA usa {esperado.GetFile()} pra {fl}");
				Conferir(cgt.DesenhoDeTeste.FolhaDeTeste == esperado && !cgt.DesenhoDeTeste.SemFolha,
						 $"a CARGA usa {esperado.GetFile()} pra {fl}");
			}

			// ============================ E A VOLTA REMONTA, QUE E O QUE UM `return` CEDO ESCONDE ============================
			// Sair do Ultra Instinto tem que devolver a chama. O laco acima termina na ULTIMA folha do
			// enum -- que hoje e justamente a `Nebulosa` --, entao a linha abaixo faz o caminho de volta
			// de proposito: sem ela, um `SpriteDeAura` que apagasse a folha e nunca mais remontasse
			// passaria a rodada inteira verde. (Foi por isso que o `_folha` vai a vazio junto com o
			// `_semFolha`: com o caminho velho guardado, `DefinirFolha` sairia no `if (_folha == caminho)`
			// e o sprite ficaria morto pra sempre.)
			// ==========================================================================================================
			aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Nebulosa);
			cgt.Folha(Jandirus.Core.Forms.FolhaDeAura.Nebulosa);
			aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Ssj);
			cgt.Folha(Jandirus.Core.Forms.FolhaDeAura.Ssj);
			Conferir(aura.DesenhoDeTeste.FolhaDeTeste == SpriteDeAura.FolhaSsj
				  && !aura.DesenhoDeTeste.SemFolha
				  && cgt.DesenhoDeTeste.FolhaDeTeste == SpriteDeAura.FolhaSsj
				  && !cgt.DesenhoDeTeste.SemFolha,
					 "e SAIR do Ultra Instinto devolve a folha aos dois desenhos "
				   + $"(aura {aura.DesenhoDeTeste.FolhaDeTeste.GetFile()}, "
				   + $"carga {cgt.DesenhoDeTeste.FolhaDeTeste.GetFile()})");

			// ============================ O ROSE DEIXOU DE DIVIDIR O ARQUIVO ============================
			// Esta medicao ja disse o contrario: `DeusFrio` e `DeusRosa` eram a MESMA `FieryGodBlue` e
			// so a TINTA as separava, porque "nao havia folha Rose no repo". Havia -- o dono achou a
			// `Supa Saiyan Rose Aura-1`, importada e nunca ligada. Com arte propria, ela entrou nas
			// PRE-COLORIDAS e o tingimento saiu junto.
			//
			// O QUE ESTA LINHA MEDE AGORA e o que a antiga media pelo avesso: que as quatro folhas
			// dedicadas apontam pra QUATRO arquivos e nenhuma delas se pinta. Um `CaminhoDa` que
			// voltasse a mandar duas pro mesmo `.tres` cai aqui.
			var dedicadas = new[]
			{
				Jandirus.Core.Forms.FolhaDeAura.Ssj, Jandirus.Core.Forms.FolhaDeAura.DeusQuente,
				Jandirus.Core.Forms.FolhaDeAura.DeusFrio, Jandirus.Core.Forms.FolhaDeAura.DeusRosa,
			};
			var vistos = new HashSet<string>();
			int naoSePintam = 0;
			foreach (Jandirus.Core.Forms.FolhaDeAura fl in dedicadas)
			{
				// AS QUATRO TEM ARQUIVO POR DEFINICAO (sao as pre-coloridas), entao um nulo aqui derruba a
				// contagem abaixo em vez de virar um `!` que engoliria o caso.
				if (SpriteDeAura.CaminhoDa(fl) is { } c) vistos.Add(c);
				aura.Folha(fl);
				if (aura.DesenhoDeTeste.SemTinta) naoSePintam++;
			}
			Conferir(vistos.Count == dedicadas.Length,
					 $"as {dedicadas.Length} folhas ja pintadas tem arquivo PROPRIO ({vistos.Count} distintos)");
			Conferir(naoSePintam == dedicadas.Length,
					 $"e nenhuma delas se tinge -- o `icolor = null` do DM ({naoSePintam})");
			aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Base);
			Conferir(!aura.DesenhoDeTeste.SemTinta, "e a folha de todo mundo continua sendo COLORIDA por fora");
			Conferir(SpriteDeAura.PreColorida(Jandirus.Core.Forms.FolhaDeAura.DeusQuente),
					 "e a chama quente do SSG e arte ja colorida (o `icolor = null` do DM)");
			aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Base);
			cgt.Folha(Jandirus.Core.Forms.FolhaDeAura.Base);
		}
		else Conferir(false, "o corpo de bancada tem node de Carga");

		aura.Apagar();
		// A FORMA, e nao mais o par (cor, forca): quem resolve os dois hoje e o proprio node, que e
		// quem tem a cor pessoal deste corpo (ver `Aura.Preparar`).
		aura.Preparar(Jandirus.Core.Forms.Catalogo.Def("ssj1"), true);
		Conferir(!aura.AcesaDeTeste, "PREPARAR nao acende a aura");
		aura.Acender(new Color("ffd24a"), 1.2f);
		Conferir(aura.AcesaDeTeste, "ACENDER acende (e o caminho da carga)");
		aura.Apagar();
		Conferir(!aura.AcesaDeTeste, "e apagar apaga");

		// ============================ A FOLHA TEM QUE CHEGAR NO DESENHO ============================
		// A regra existir no `Catalogo` nao bastou: `Aura.Folha` tinha DOIS chamadores e eu so
		// tinha ligado um -- o caminho normal do `World`. A estreia desviava pra cinematica e a
		// aura saia com a folha velha, que foi o que o dono viu. Aqui se confere o RESULTADO no
		// node, nao a intencao.
		// ================================================================================
		aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Ssj);
		aura.Acender(new Color("ffd24a"), 1f);
		Conferir(aura.DesenhoDeTeste.SemTinta, "a folha do SSJ nao se tinge (arte ja dourada)");
		Conferir(Mathf.Abs(aura.DesenhoDeTeste.BaseDeTeste - SpriteDeAura.LinhaDosPes) <= 2f,
				 $"a folha do SSJ nasce no pe (base {aura.DesenhoDeTeste.BaseDeTeste:0.#})");
		aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Base);
		Conferir(!aura.DesenhoDeTeste.SemTinta, "a folha base VOLTA a se tingir");
		// AS DIVINAS SAIRAM DA BASE COLORIVEL -- se o Blue cair na folha dourada ele fica amarelo, e se
		// cair na colorivel ele volta a ser "a aura de todo mundo tingida", que era o estado anterior.
		Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def("blue"))
				 == Jandirus.Core.Forms.FolhaDeAura.DeusFrio, "o Blue acende a chama FRIA (FieryGodBlue)");
		Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def("ssg"))
				 == Jandirus.Core.Forms.FolhaDeAura.DeusQuente, "o SSG acende a chama QUENTE (FieryGod)");
		// ============================ E A LINHA DO MISTICO SAIU DA QUENTE ============================
		// Esta linha dizia o CONTRARIO ate agora ("o Mistico cai na MESMA chama do SSG"), e era leitura
		// certa do DM. O dono derrubou: *"o mistico e beast tao usando a aura de carga do ssj god"* --
		// os DOIS. Os dois voltaram pra a folha COLORIVEL, que e a unica que aceita cor de fora.
		//
		// OS DOIS JUNTOS NUMA LINHA SO de proposito: a queixa foi sobre a LINHA, e um conserto que
		// pegasse so um dos degraus e o jeito mais provavel de isto reaparecer.
		//
		// COMO REPROVA SE A REGRA SUMIR: devolva o ramo `LinhaDeForma.Mistico => FolhaDeAura.DeusQuente`
		// ao `Catalogo.Folha` -- a chama do SSG volta aos dois e esta linha cai, junto com a de baixo.
		foreach (string prodigial in new[] { "mistico", "beast" })
			Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def(prodigial))
					 == Jandirus.Core.Forms.FolhaDeAura.Base,
					 $"`{prodigial}` NAO usa mais a chama do SSG -- ele acende a folha colorivel "
				   + $"(deu {Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def(prodigial))})");

		// E ELA SE TINGE MESMO. A folha certa com `PreColorida` verdadeira seria pior que a folha
		// errada: os dois sairiam CINZAS e nenhuma checagem de cor acusaria -- a cor continuaria certa
		// no catalogo, certa no node, e jogada fora no shader.
		Conferir(!SpriteDeAura.PreColorida(Jandirus.Core.Forms.FolhaDeAura.Base),
				 "-- e essa folha ACEITA cor de fora (senao as duas chamas saem cinzas e iguais)");

		// ============================ E A COR DE CADA UM DOS DOIS VEM DE UM LUGAR DIFERENTE ============================
		// A folha e a mesma; a COR nao. E esta e a distincao que o par `SemTinta`/`PreColorida` nao
		// sabe fazer -- "a folha aceita cor" nao diz de QUEM e a cor. Ver `Catalogo.ChamaDoJogador`.
		//
		//   `mistico` -> a chama do JOGADOR (*"a mesma aura da BASE DO PERSONAGEM"*), que hoje e a cor
		//                SORTEADA de cada personagem. E ela e IGUAL a da base, medida, e nao "parecida";
		//   `beast`   -> o `7d5af0` que a propria entrada dele declara (`Mystic.dm:95`), que ate esta
		//                passada NAO CHEGAVA EM PIXEL NENHUM: a `FieryGod` nao se tinge.
		//
		// COMO REPROVA SE A REGRA SUMIR: apague o ramo `Mistico` do `ChamaDoJogador` e a primeira vira
		// o `d8c8ff` do catalogo; devolva o Beast pra ele e a segunda vira a cor do jogador.
		//
		// E A COR PESSOAL AQUI NAO E O FALLBACK (ver `CorPessoalDeTeste`): com o `Aura.CorDoKiCru` no
		// lugar dela, estas duas linhas ficariam verdes num jogo em que a cor sorteada nunca saisse do
		// save.
		Conferir(Aura.CorDaChamaDe(Jandirus.Core.Forms.Catalogo.Def("mistico"), CorPessoalDeTeste)
					 .IsEqualApprox(CorPessoalDeTeste)
			  && Aura.CorDaChamaDe(Jandirus.Core.Forms.Catalogo.Def("mistico"), CorPessoalDeTeste)
					 .IsEqualApprox(Aura.CorDaChamaDe(Jandirus.Core.Forms.Catalogo.Def("base"),
													  CorPessoalDeTeste)),
				 "a chama do Mistico e a MESMA da base do personagem (#"
			   + $"{Aura.CorDaChamaDe(Jandirus.Core.Forms.Catalogo.Def("mistico"), CorPessoalDeTeste).ToHtml(false)})");
		Conferir(Aura.CorDaChamaDe(Jandirus.Core.Forms.Catalogo.Def("beast"), CorPessoalDeTeste)
					 .IsEqualApprox(new Color("7d5af0")),
				 "e a da Fera e o roxo que a entrada dela declara (#"
			   + $"{Aura.CorDaChamaDe(Jandirus.Core.Forms.Catalogo.Def("beast"), CorPessoalDeTeste).ToHtml(false)})");

		// ============================ O SORTEIO EM SI, QUE E O QUE ALIMENTA TUDO ISSO ============================
		// Duas propriedades, e as duas sao do DM e nao de gosto:
		//
		//   FAIXA -- `min(255, 200 + rand(0,255))` nunca desce de 200. O `200` e o tom dominante da
		//            `colorablebigaura` (`c8c8c8`), e o shader normaliza por ele (`PicoDaFolha`); um
		//            canal abaixo disso seria uma chama que o DM nao sabe produzir. Portar o `rand`
		//            CRU -- o erro obvio -- daria media 127 e reprovaria aqui.
		//   MAIORIA BRANCA -- o dono lembrou que "a mais comum e a branca", e isso nao e um peso: e a
		//            saturacao. ~79% de chance por canal de estourar em 255, ~49% nos tres.
		//
		// ESTAVEL, por ultimo: a cor de um personagem e funcao PURA de nome + `CriadoEm`, e e essa
		// pureza que faz a migracao do save antigo existir sem ramo nenhum (ver `CorDeAura.De`).
		// ==================================================================================================
		{
			int brancas = 0, forasDeFaixa = 0;
			for (int i = 0; i < 2000; i++)
			{
				Jandirus.Core.Appearance.Rgb c = Jandirus.Core.Appearance.CorDeAura.Sortear((ulong)i);
				if (c.R < Jandirus.Core.Appearance.CorDeAura.PicoDaFolha
					|| c.G < Jandirus.Core.Appearance.CorDeAura.PicoDaFolha
					|| c.B < Jandirus.Core.Appearance.CorDeAura.PicoDaFolha) forasDeFaixa++;
				if (c.R == 255 && c.G == 255 && c.B == 255) brancas++;
			}
			Conferir(forasDeFaixa == 0,
					 $"o sorteio da aura nunca desce de {Jandirus.Core.Appearance.CorDeAura.PicoDaFolha} "
				   + $"em canal nenhum ({forasDeFaixa} fora da faixa em 2000)");
			Conferir(brancas > 800 && brancas < 1200,
					 $"e a aura mais comum e a BRANCA, por saturacao e nao por peso ({brancas}/2000, "
				   + "esperado ~980)");

			Jandirus.Core.Appearance.Rgb a1 = Jandirus.Core.Appearance.CorDeAura.De("Zx", 1_700_000_000_000L);
			Jandirus.Core.Appearance.Rgb a2 = Jandirus.Core.Appearance.CorDeAura.De("Zx", 1_700_000_000_000L);
			Conferir(a1.R == a2.R && a1.G == a2.G && a1.B == a2.B,
					 $"a cor derivada do mesmo personagem e SEMPRE a mesma ({a1}) -- e por isso que o "
				   + "save antigo nao precisa de campo nem de migracao");

			// E NAO E UMA CONSTANTE. Comparar DOIS personagens nao serviria: metade dos sorteios da
			// branco puro, entao dois quaisquer batem em ~24% das vezes e a bancada piscaria vermelha
			// sozinha uma vez a cada quatro. O que se mede e a POPULACAO -- com ~51% de nao-brancas,
			// cem personagens tem que produzir dezenas de tons distintos.
			var tons = new HashSet<string>();
			for (int i = 0; i < 100; i++)
				tons.Add(Jandirus.Core.Appearance.CorDeAura.De($"Lutador{i}", 1_700_000_000_000L).ToString());
			Conferir(tons.Count >= 20,
					 $"e cem personagens produzem {tons.Count} tons distintos -- a derivacao varia com "
				   + "o personagem, e nao com o processo");
		}

		// E A FORCA CONTINUA SEPARANDO OS DOIS DA BASE. Cor igual nao pode virar chama igual: o
		// Mistico e uma transformacao de 16x, e o que o distingue da base e a DENSIDADE.
		Conferir(Aura.ForcaDaChamaDe(Jandirus.Core.Forms.Catalogo.Def("mistico"))
				 > Aura.ForcaDaChamaDe(Jandirus.Core.Forms.Catalogo.Def("base")),
				 "-- mas a chama dele e mais DENSA que a da base (a cor e do jogador, a forca e da forma)");

		// ============================ E AGORA NO **MATERIAL**, QUE E OUTRA PERGUNTA ============================
		// Tudo o que esta acima e INTENCAO: funcoes puras conferidas contra hexa. Este projeto ja pagou
		// caro por parar ai -- quatro defeitos visuais atravessaram milhares de checagens verdes porque
		// "uniform escrito" nao e "pixel desenhado". As tres linhas acima ficariam verdes com a folha
		// pre-colorida (a cor certa jogada fora pelo shader) e com o desenho nunca montado.
		//
		// ENTAO AQUI SE PERCORRE O CAMINHO DE VERDADE, o mesmo do `World.PrepararAuraDaForma`: escolhe
		// a folha pelo simbolo do Core, acende com a cor e a forca derivadas, e le a cor DE VOLTA do
		// `ShaderMaterial` -- que so existe depois do `Montar()`. `CorNoMaterialDeTeste` nulo quer
		// dizer "nao ha o que medir", e isso reprova em vez de passar calado.
		// ====================================================================================================
		//
		// E O TOM DO MISTICO E A COR PESSOAL DESTE SUJEITO (`ffd2c8`), e nao mais um hexa fixo: e a
		// unica maneira de esta linha reprovar se a cor sorteada parar de sair do node. A do Beast
		// continua sendo a declarada na entrada dele -- ele NAO usa a do jogador.
		foreach ((string id, string tom) in new[] { ("mistico", "ffd2c8"), ("beast", "7d5af0") })
			if (Jandirus.Core.Forms.Catalogo.Def(id) is { } df)
			{
				aura.Apagar();
				aura.Folha(Jandirus.Core.Forms.Catalogo.Folha(df));
				aura.Acender(Aura.CorDaChamaDe(df, CorPessoalDeTeste), Aura.ForcaDaChamaDe(df));
				// DUAS COMPARACOES E NENHUMA E REDUNDANTE. Contra a funcao: e o ENCANAMENTO -- a cor
				// escolhida chegou inteira ate o `ShaderMaterial`, sem ninguem no meio trocar por
				// branco. Contra o HEXA escrito: e a cor CERTA -- sem ele, um `CorDaChamaDe` que
				// devolvesse tudo preto passaria feliz na primeira.
				//
				// A FOLGA E DE UM PASSO DE 8 BITS (e nao o `IsEqualApprox`, que exige 1e-5): a
				// `CorDoKiCru` e escrita como `0.62f` e o hexa mais proximo dela e `9e` = 0,619607 --
				// meio milesimo de diferenca que nao existe em pixel nenhum, e que reprovava esta
				// linha imprimindo os dois lados IGUAIS. Erro de bancada, nao de jogo.
				Color? noMaterial = aura.DesenhoDeTeste.CorNoMaterialDeTeste;
				var esperada = new Color(tom);
				const float UmPasso = 1f / 255f;
				Conferir(noMaterial is { } cm && cm.IsEqualApprox(Aura.CorDaChamaDe(df, CorPessoalDeTeste))
					  && Mathf.Abs(cm.R - esperada.R) <= UmPasso
					  && Mathf.Abs(cm.G - esperada.G) <= UmPasso
					  && Mathf.Abs(cm.B - esperada.B) <= UmPasso,
						 $"`{id}`: a chama CHEGA no shader em #{tom} "
					   + $"(deu {(noMaterial is { } c2 ? "#" + c2.ToHtml(false) : "material nao montado")})");
				Conferir(!aura.DesenhoDeTeste.SemTinta,
						 $"-- e o desenho dele ACEITA a tinta (senao a cor e descartada e sai a arte crua)");
			}
		aura.Apagar();
		aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Base);

		Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def("ssj4"))
				 == Jandirus.Core.Forms.FolhaDeAura.Ssj, "o SSJ4 usa a folha dourada do SSJ");

		// ============================ O ULTRA INSTINTO NAO TEM CHAMA, E O `TemNebulosa` SAI DAI ============================
		// As duas metades do pedido do dono, medidas juntas porque hoje elas sao UMA: o simbolo
		// `Nebulosa` e ao mesmo tempo "nao ha folha" e "ha nuvem" (ver o Core). Enquanto eram dois
		// predicados, o Ultra Instinto tinha nuvem E caia na `colorablebigaura` -- as duas verdades
		// discordando, que e a queixa.
		//
		// A DERIVACAO E COBRADA NO CATALOGO INTEIRO e nao nos dois ids: um `TemNebulosa` que voltasse a
		// ser o predicado da linha continuaria certo nas duas formas de UI e poderia divergir da folha em
		// qualquer degrau novo. O que se afirma e a EQUIVALENCIA, forma por forma.
		// ==============================================================================================================
		foreach (string id in new[] { "ui_sign", "ui_perfected", "ultra_ego" })
			Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def(id))
					 == Jandirus.Core.Forms.FolhaDeAura.Nebulosa,
					 $"`{id}` nao usa folha de chama nenhuma -- o desenho dela e a NUVEM");
		// E A `destroyer` CONTINUA COM CHAMA, nomeada: ela e a irma de linha do `ultra_ego`, e a linha de
		// cima passaria igual se alguem trocasse o corte por `Ordem` por um ramo de LINHA inteira. Ver
		// `Catalogo.OrdemDoEgoSobreADestruicao`.
		Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def("destroyer"))
				 == Jandirus.Core.Forms.FolhaDeAura.Base,
				 "e a `destroyer` continua na folha COLORIVEL -- o dono nomeou o Ultra Ego, e o cabelo "
			   + "base e a diferenca visual entre as duas (`UltraEgo.dm:395-396`)");
		int divergem = Jandirus.Core.Forms.Catalogo.Todas.Count(
			d => Jandirus.Core.Forms.Catalogo.TemNebulosa(d)
				 != (Jandirus.Core.Forms.Catalogo.Folha(d) == Jandirus.Core.Forms.FolhaDeAura.Nebulosa));
		Conferir(divergem == 0,
				 $"e 'tem nebulosa' e literalmente 'nao tem folha' nas {Jandirus.Core.Forms.Catalogo.Todas.Length} "
			   + $"formas ({divergem} discordam) -- uma pergunta so, nao duas fontes de verdade");

		// ============================ AS FORMAS QUE NASCEM DA RAIVA ============================
		// A regra e DERIVADA (ver `Catalogo.NasceDaRaiva`), e derivacao sem teste e palpite: uma
		// mexida em `ForaDoTronco` ou `PedeMaestria` de qualquer degrau muda esta lista sem que
		// ninguem esteja pensando em raiva. A lista abaixo foi conferida contra o DM entrada por
		// entrada -- se a derivacao mudar, isto reprova em vez de a cratera errada aparecer calada.
		// ================================================================================
		// O `ssj3` SAIU DAQUI de proposito: ele pede 50% de maestria do SSJ2 e o poder minimo
		// pessoal, nao raiva (`Transformation Controls.dm:46`, regra confirmada pelo dono). Se
		// alguem devolver ele pra ca, a linha `PedeMaestria` da entrada `ssj3` foi apagada junto.
		string[] esperadas =
		[
			"ssj1", "ssj2", "future_ssj",
			"wrathful", "c_type", "legendary",
			"primal_c_type", "primal_legendary", "primal_legendary2",
			"beast",

			// O HERAN. As duas formas dele nascem da furia do LUTO pelo mesmo motivo do tronco
			// Saiyajin -- `heran.dm:20-52` roda o mesmo `switch(savant.Emotion)` nos dois degraus --,
			// e por isso ganham a CRATERA GRANDE junto com o resto do tronco de sangue.
			//
			// **ELAS FORAM O CONSERTO DE UMA DERIVACAO, E NAO UM ACRESCIMO**: o `RaivaExigida` listava
			// so `Saiyajin` e `Futuro`, com um comentario prometendo que uma linha de sangue nova
			// entraria sozinha. Nao entraria -- caia no `_ => Nenhuma`, calada.
			//
			// O SUPER NAMEKUSEIJIN E AS DUAS FORMAS ALIEN NAO ESTAO AQUI, e a ausencia e medida pelo
			// laco de baixo: elas se COMPRAM (`FormaDef.PedeFlag`), e o que se compra nao se desperta.
			"heran1", "heran2",
		];
		var deuRaiva = new List<string>();
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
			if (Jandirus.Core.Forms.Catalogo.NasceDaRaiva(d)) deuRaiva.Add(d.Id);

		foreach (string id in esperadas)
			Conferir(deuRaiva.Contains(id), $"`{id}` nasce da raiva");
		foreach (string id in deuRaiva)
			Conferir(Array.IndexOf(esperadas, id) >= 0, $"`{id}` NAO deveria contar como raiva");
		Conferir(deuRaiva.Count == esperadas.Length,
				 $"sao {esperadas.Length} formas por raiva (deu {deuRaiva.Count})");

		// ============================ SAO DOIS PRECOS, E A CRATERA E UMA SO ============================
		// O gate cobra DOIS degraus de raiva (`Catalogo.RaivaExigida`): a furia do luto pro tronco
		// Saiyajin e pro Beast, e o desconto da `legendary anger` pra linha Legendary. O DESENHO nao
		// se divide -- toda forma que nasce de raiva abre a cratera grande, porque quem esta olhando
		// nao sabe (nem tem como saber) de que dor ela veio.
		//
		// A CHECAGEM E DAQUI E NAO DO CORE porque a consequencia e daqui: o unico consumidor de
		// producao do `NasceDaRaiva` e o `Transformacao.cs`, escolhendo `CrateraGrande` contra
		// `Cratera`. Trocar aquela pergunta por "pede furia extrema?" -- a "correcao" plausivel,
		// agora que existem dois niveis -- faria as SEIS formas Legendary estrearem com a cratera
		// pequena. Nada quebraria, nada apareceria no log: so um chao que racha menos.
		// ================================================================================
		int extremas = 0, lendarias = 0;
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			Jandirus.Core.Forms.NivelDeRaiva n = Jandirus.Core.Forms.Catalogo.RaivaExigida(d);
			if (n == Jandirus.Core.Forms.NivelDeRaiva.Extrema) extremas++;
			else if (n == Jandirus.Core.Forms.NivelDeRaiva.Lendaria) lendarias++;

			// A CRATERA SEGUE A PERGUNTA "TEM RAIVA?", e nao "de qual?".
			Conferir(Jandirus.Core.Forms.Catalogo.NasceDaRaiva(d)
					 == (n != Jandirus.Core.Forms.NivelDeRaiva.Nenhuma),
					 $"`{d.Id}`: a cratera segue o `NasceDaRaiva`, que segue o nivel ({n})");
		}
		Conferir(extremas > 0 && lendarias > 0,
				 $"os DOIS degraus existem no catalogo ({extremas} por luto, {lendarias} por queda)");
		Conferir(extremas + lendarias == deuRaiva.Count,
				 $"e os dois somam a lista da cratera ({extremas}+{lendarias} contra {deuRaiva.Count})");
		_passos.Add($"  --     por LUTO: {extremas} formas; pelo desconto Legendary: {lendarias}");

		// AS QUE MAIS PODEM ESCORREGAR, ditas com o motivo -- sao os tres jeitos de sair do tronco.
		Conferir(!Jandirus.Core.Forms.Catalogo.NasceDaRaiva(Jandirus.Core.Forms.Catalogo.Def("grade2")),
				 "`grade2` sai por ForaDoTronco (e treino, nao furia)");
		Conferir(!Jandirus.Core.Forms.Catalogo.NasceDaRaiva(Jandirus.Core.Forms.Catalogo.Def("ssj4")),
				 "`ssj4` sai por PedeFormaDespertada (vem do Oozaru)");
		// O EXEMPLO DO `PedeMaestria` ERA O `legendary_full_power`, e ele deixou de ser forma (virou o
		// `legendary` a 100% de maestria). Trocado pelo `primal_legendary3`, que sai do tronco pelo
		// MESMO campo -- e a troca importa: com um id inexistente o `Def` devolveria nulo, o
		// `NasceDaRaiva(null)` devolveria falso e a checagem passaria sem medir nada.
		Conferir(!Jandirus.Core.Forms.Catalogo.NasceDaRaiva(Jandirus.Core.Forms.Catalogo.Def("primal_legendary3")),
				 "`primal_legendary3` sai por PedeMaestria");
		Conferir(!Jandirus.Core.Forms.Catalogo.NasceDaRaiva(Jandirus.Core.Forms.Catalogo.Def("base")),
				 "`base` nao e transformacao nenhuma");

		// O SSJ3 DITO PELO NOME, e nao so pela ausencia na lista: a contagem acima reprovaria se ele
		// voltasse, mas a mensagem falaria de "10 formas" e nao do degrau que o dono nomeou.
		Conferir(!Jandirus.Core.Forms.Catalogo.NasceDaRaiva(Jandirus.Core.Forms.Catalogo.Def("ssj3")),
				 "`ssj3` sai por PedeMaestria (50% de SSJ2, nao furia)");

		// ============================ NENHUMA DIVINA POR RAIVA, MENOS O BEAST ============================
		// Ordem do dono: forma divina e tecnica e de mente calma; raiva e mortal e bruta. As quatro
		// linhas caem no `_ => false` do `NasceDaRaiva`, e o Beast e a excecao dele -- mas "cai no
		// default" e uma verdade da IMPLEMENTACAO, e implementacao muda. Isto prova a REGRA: varre as
		// linhas divinas e exige que so o Beast passe, entao uma linha divina nova que alguem pendure
		// no braco errado reprova aqui em vez de estrear com a cratera da furia.
		// ================================================================================================
		Jandirus.Core.Forms.LinhaDeForma[] divinas =
		[
			Jandirus.Core.Forms.LinhaDeForma.GodKi, Jandirus.Core.Forms.LinhaDeForma.GodKiRose,
			Jandirus.Core.Forms.LinhaDeForma.Mistico, Jandirus.Core.Forms.LinhaDeForma.UltraInstinct,
			Jandirus.Core.Forms.LinhaDeForma.UltraEgo,
		];
		foreach (Jandirus.Core.Forms.FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (Array.IndexOf(divinas, d.Linha) < 0) continue;
			if (d.Id == "beast") continue;   // a unica excecao, e ela e pedido do dono
			Conferir(!Jandirus.Core.Forms.Catalogo.NasceDaRaiva(d),
					 $"`{d.Id}` e divina: NAO nasce da raiva (mente calma, nao furia)");
		}
		Conferir(Jandirus.Core.Forms.Catalogo.NasceDaRaiva(Jandirus.Core.Forms.Catalogo.Def("beast")),
				 "`beast` e a UNICA divina por raiva (o Prodigial nao vira deus, vira fera)");

		_passos.Add($"  --     por raiva: {string.Join(", ", deuRaiva)}");

		// A CRATERA GRANDE E OUTRO TIPO, OUTRA FOLHA. As duas coisas tem que valer: se a folha nao
		// existir o decalque some calado, e se o tipo cair de volta na `Cratera` a raiva volta a
		// abrir um buraquinho. Nenhuma das duas da erro em jogo -- so um efeito errado.
		// ============================ A COR DO RABO NAO E A DO CABELO ============================
		// Foi exatamente essa confusao que deixou o rabo do SSJ marrom: eu passava
		// `FormaDef.Cabelo` pro rabo, e no Oozaru esse campo e `5a3a1b` porque descreve a PELAGEM.
		// A tabela do rabo e outra (`SaiyanObjects.dm:100-118`) e a maioria das formas nao pinta.
		// ================================================================================
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(Jandirus.Core.Forms.Catalogo.Def("ssj1")) == "dada26",
				 "o rabo do SSJ e DOURADO");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(Jandirus.Core.Forms.Catalogo.Def("legendary")) == "7ba81f",
				 "o rabo do Legendary e VERDE");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(Jandirus.Core.Forms.Catalogo.Def("c_type")) == "dada26",
				 "o C-Type e DOURADO (o `lssj == 2` de SaiyanObjects.dm:116)");
		// ============================ AS TRES CORRECOES QUE O DONO APONTOU ============================
		// Elas nao sao ajuste de tom: cada uma era um ramo do DM lido errado, e as tres viviam na mesma
		// funcao. Ficam com o hexa LITERAL e o motivo escrito, porque uma delas ja voltou uma vez.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(Jandirus.Core.Forms.Catalogo.Def("wrathful")) == null,
				 "o Wrathful NAO pinta o rabo (o `lssj == 1` nem aparece na tabela do DM)");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(Jandirus.Core.Forms.Catalogo.Def("ssg")) == "e2331c",
				 "o rabo do SSG e VERMELHO, a mesma tinta do cabelo (era AZUL)");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(Jandirus.Core.Forms.Catalogo.Def("rose_ssg")) == "e2331c",
				 "e o SSG da linha Rose e IGUAL ao SSG comum (era ROSA)");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(Jandirus.Core.Forms.Catalogo.Def("base")) == null,
				 "na base o rabo NAO se pinta");
		// O `beast` SAIU DESTA LISTA, e ele foi o TERCEIRO a sair pelo mesmo caminho. Ele estava aqui
		// com o motivo certo -- no DM a cauda dele nao pinta, porque o buff chama `Revert()` --, e saiu
		// por pedido do dono: *"o rabo do beast n ta branco"*. Ver `Catalogo.CorDoRabo`; quem o cobra
		// agora e o `ORaboMedidoNoDesenho`, que mede o PIXEL e nao o hexa.
		//
		// O `ui_perfected` E O `ultra_ego` TINHAM SAIDO ANTES, pelo mesmo motivo (*"Perfected Ultra
		// Instinct: o RABO fica BRANCO"*, *"Ultra Ego: o RABO fica ROXO tambem"*), e as tres cores caem
		// sozinhas da derivacao que ja existia.
		//
		// OS IRMAOS DE CADA UM DOS TRES FICARAM, e sao eles que provam que a mudanca foi por DEGRAU e
		// nao por linha: `ui_sign` e `destroyer` nao tem tinta de cabelo, e o **`mistico` tambem nao**
		// -- ele saiu da lista de exclusao do `CorDoRabo` JUNTO com o Beast (e uma linha so la) e
		// continua sem rabo pintado por DERIVACAO. Esta linha e o unico lugar que cobra isso aqui.
		foreach (string semTinta in new[] { "oozaru", "ui_sign", "destroyer",
										   "mistico", "ssj4", "primal_legendary4" })
			if (Jandirus.Core.Forms.Catalogo.Def(semTinta) is { } dz)
				Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(dz) == null,
						 $"`{semTinta}` nao pinta rabo (era daqui que vinha o marrom)");

		// ============================ TROCAR, TINGIR, OU OS DOIS ============================
		// O modo e DERIVADO de (sufixo, tinta) -- ver `ModoDoCabelo` no Core. Derivacao sem teste e
		// palpite: preencher a `Cabelo` de um degrau Saiyajin por reflexo o tiraria de `Trocar` e o
		// poria a pintar por cima da arte dourada, que e exatamente o que o dono vetou, e nada
		// reclamaria. Os quatro casos abaixo sao um por MODO, e os tres primeiros sao as queixas dele.
		// ================================================================================
		foreach ((string id, Jandirus.Core.Forms.ModoDoCabelo esperado, string porque) in new[]
		{
			("wrathful",     Jandirus.Core.Forms.ModoDoCabelo.Base,
			 "mantem o penteado do jogador (HairObject.dm:209)"),
			("ssg",          Jandirus.Core.Forms.ModoDoCabelo.Tingir,
			 "pinta o penteado BASE de vermelho (HairObject.dm:73)"),
			("ssj1",         Jandirus.Core.Forms.ModoDoCabelo.Trocar,
			 "troca a arte e NAO pinta (o veto do dono)"),
			("legendary",    Jandirus.Core.Forms.ModoDoCabelo.TrocarETingir,
			 "USSj + verde por cima (SaiyanObjects.dm:83)"),
			("blue",         Jandirus.Core.Forms.ModoDoCabelo.TrocarETingir,
			 "SSj + azul por cima (SaiyanObjects.dm:18)"),
			("ui_perfected", Jandirus.Core.Forms.ModoDoCabelo.TrocarOuTingir,
			 "arte do Goku OU prata no base (UltraInstinct.dm:298)"),
			("beast",        Jandirus.Core.Forms.ModoDoCabelo.TrocarERecolorir,
			 "SSj2 em matiz branco-gelo (Mystic.dm:81)"),
			("mistico",      Jandirus.Core.Forms.ModoDoCabelo.Base,
			 "o Mistico nao toca no cabelo -- penteado base, sem tinta (HairObject.dm:29)"),
		})
			if (Jandirus.Core.Forms.Catalogo.Def(id) is { } dm)
				Conferir(Jandirus.Core.Forms.Catalogo.ModoDoCabelo(dm) == esperado,
						 $"`{id}` veste o cabelo em {esperado} -- {porque}");

		// E NENHUM DEGRAU DA ESCADA SAIYAJIN PINTA. Por LINHA e nao por id: um degrau novo ja nasce
		// coberto, que e o ponto inteiro de derivar.
		int saiyajinQuePinta = Jandirus.Core.Forms.Catalogo.Todas.Count(
			d => d.Linha is Jandirus.Core.Forms.LinhaDeForma.Saiyajin
						 or Jandirus.Core.Forms.LinhaDeForma.Futuro
			  && Jandirus.Core.Forms.Catalogo.CorDoCabelo(d) != null);
		Conferir(saiyajinQuePinta == 0,
				 $"nenhum Super Saiyajin tinge o cabelo ({saiyajinQuePinta} tingem)");

		// ============================ CADA COISA COM A SUA COR ============================
		// A `FormaDef.Aura` mandava em tres desenhos (aura, contorno e raios) e por isso todo ajuste
		// de cor puxava um defeito atras. Isto aqui confere as DERIVACOES -- que o contorno e o raio
		// se soltaram da aura E que continuam colados nela onde devem.
		//
		// Os hexas estao LITERAIS de proposito, como no bloco do rabo acima: mudar a cor tem que
		// obrigar a passar por aqui. Uma checagem escrita como `== AmareloSaiyajin` passaria com
		// qualquer valor, inclusive um trocado por engano.
		// ================================================================================
		foreach (string amarelo in new[] { "ssj1", "grade2", "grade3", "ssj2", "ssj3",
										   "ssj4", "ssj4_full_power", "future_ssj" })
			if (Jandirus.Core.Forms.Catalogo.Def(amarelo) is { } da)
				Conferir(Jandirus.Core.Forms.Catalogo.CorDoContorno(da) == "ffd24a",
						 $"o contorno de `{amarelo}` e AMARELO (a escada inteira, um tom so)");

		// A EXCECAO, e ela vale duplo: e o unico degrau Saiyajin que exige ki divino, e e por AI
		// que ela e derivada. Se alguem tirar o `PedeGodKi` dele, o contorno volta a dourar calado.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoContorno(
					 Jandirus.Core.Forms.Catalogo.Def("ssj4_limit_breaker")) == "ff2d2f",
				 "o contorno do Limit Breaker e VERMELHO (e a aura dele tambem voltou a ser)");
		Conferir(Jandirus.Core.Forms.Catalogo.Def("ssj4_limit_breaker")!.Aura == "ff2d2f",
				 "a AURA do Limit Breaker e vermelha, e nao dourada");

		// AS QUATRO AZUIS, uma a uma e com o hexa literal. As duas do Primal ESTAVAM VERDES (a faisca
		// herdava a aura da linha) ate o dono corrigir -- entao estas duas linhas nao sao simetria de
		// enfeite: elas sao o enunciado novo, e sao o que reprova se alguem devolver o `_ => d.Aura`
		// pra a linha Legendary Primal.
		foreach (string azul in new[] { "ssj2", "ssj3", "primal_legendary2", "primal_legendary3" })
			Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(
						 Jandirus.Core.Forms.Catalogo.Def(azul)) == "8fe3ff",
					 $"a faisca de `{azul}` e AZUL");

		// E A QUINTA E VERMELHA -- "limit breaker tem raios vermelhos". Ela vale duplo, como a do
		// contorno logo acima: a cor sai da MESMA derivacao (`PedeGodKi >= 0`), entao esta linha
		// tambem e a que acusa se alguem tirar o ki divino do catalogo dele.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(
					 Jandirus.Core.Forms.Catalogo.Def("ssj4_limit_breaker")) == "ff2d2f",
				 "a faisca do Limit Breaker e VERMELHA (aura, contorno e raio, os tres)");

		// E O IRMAO DA OUTRA LINHA CONTINUA AZUL. Ele pede o mesmo ki divino e tem quase o mesmo
		// nome: e o unico jeito de o vermelho escapar pra duas formas sem ninguem notar -- ele nem
		// desenha raio, entao a cor errada nele seria INVISIVEL.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(
					 Jandirus.Core.Forms.Catalogo.Def("primal_legendary4_limit_breaker")) == "8fe3ff",
				 "o Limit Breaker PRIMAL nao pegou o vermelho junto (ele e da escada verde)");

		// E FORA DAS ESCADAS DE SANGUE A FAISCA CONTINUA SENDO A AURA. Estas quatro sao o CONTROLE do
		// azul: sem elas, uma `CorDosRaios` que devolvesse `8fe3ff` pra tudo passaria em todas as
		// checagens acima. Nenhuma das quatro desenha raio, e e justamente por isso que a cor errada
		// nelas seria INVISIVEL -- ate o dia em que alguem desse faisca a uma.
		//
		// O `beast` SAIU DESTA LISTA nesta passada: ele acende faisca agora, e a cor dele nao e mais a
		// aura. Ver o bloco do Mistico logo abaixo.
		foreach (string comAura in new[] { "blue", "rose", "ssg", "oozaru" })
			if (Jandirus.Core.Forms.Catalogo.Def(comAura) is { } dr)
				Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(dr) == dr.Aura,
						 $"a faisca de `{comAura}` segue a AURA dela");

		// ============================ A FAISCA DO MISTICO, QUE ESTAVA FALTANDO ============================
		// Queixa do dono: *"Mistico: tudo igual a base, MAS ele TEM os raiozinhos, que estao faltando"*.
		// E o comentario do proprio catalogo ja dizia que o Mistico "se le pela aura E PELA FAISCA" --
		// com o campo `Raios` em ZERO ao lado. Um comentario descrevendo o que o dado nao faz.
		//
		// SAO DUAS COISAS SEPARADAS e as duas tem que valer, porque falham em lugares diferentes: o
		// VOLUME e dado do catalogo (`FormaDef.Raios`) e a COR e derivacao (`CorDosRaios`).
		//
		// E A COR E O UNICO CASO DO JOGO QUE NAO E NEM ESCADA DE SANGUE NEM AURA -- e sao DUAS cores,
		// nao uma. O VOLUME e da linha inteira (`Mystic.dm:37` e `:112` vestem o MESMO overlay); a COR
		// se separou quando o dono pediu roxo na Fera.
		//
		//   `mistico` -> `ffffff`, porque `Electric_Mystic.dmi` (a folha do `MysticEffect`,
		//                `Mystic.dm:20-23`) e neutra, cinco tons sem matiz nenhuma;
		//   `beast`   -> `d9b0ff`, *"no beast os raiozinhos sao roxos"*.
		//
		// AS DUAS SAO COBRADAS CONTRA A AURA DA PROPRIA FORMA, e essa e a parte que nao pode cair: a
		// faisca nao pode virar a chama de novo (`_ => d.Aura`), que e o defeito verde-sobre-verde do
		// `primal_legendary2`. Na Fera isso vale DUPLO -- a chama dela agora e `7d5af0`, roxa, e o roxo
		// da faisca e um roxo DIFERENTE, escolhido pela luminancia (ver `RoxoDaFaiscaDaFera`).
		//
		// COMO REPROVA SE A REGRA SUMIR: tire os ramos `LinhaDeForma.Mistico` do `CorDosRaios` e as
		// duas cores caem no `_ => d.Aura`. Zere o `Raios` de novo e caem as de volume.
		// ==============================================================================================
		foreach ((string id, string tom) in new[] { ("mistico", "ffffff"), ("beast", "d9b0ff") })
			if (Jandirus.Core.Forms.Catalogo.Def(id) is { } dmi)
			{
				Conferir(dmi.Raios > 0,
						 $"`{id}` ACENDE faisca (`Mystic.dm:37` e `:112`) -- volume {dmi.Raios}");
				Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(dmi) == tom,
						 $"...e ela e #{tom} e nao a cor da aura dele (#{dmi.Aura}) -- deu "
					   + $"#{Jandirus.Core.Forms.Catalogo.CorDosRaios(dmi)}");
			}

		// A BASE E O NULO NAO PINTAM NADA -- eles saem com forca zero, mas a cor tem que existir:
		// `new Color(null)` seria erro em jogo, na volta pra forma normal.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoContorno(null).Length == 6
				 && Jandirus.Core.Forms.Catalogo.CorDosRaios(null).Length == 6,
				 "sem forma, as duas cores ainda devolvem hexa valido");
		// A BASE FICA FORA DAS DUAS REGRAS pela MESMA guarda (`d.Id != IdBase`): ela mora na linha
		// Saiyajin do catalogo e nao e transformacao nenhuma. Tirar o `when` da faisca deixaria a
		// forma normal com raio azul -- que ninguem veria, porque ela nao acende raio.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoContorno(
					 Jandirus.Core.Forms.Catalogo.Def("base")) == "ffffff"
			  && Jandirus.Core.Forms.Catalogo.CorDosRaios(
					 Jandirus.Core.Forms.Catalogo.Def("base")) == "ffffff",
				 "a `base` nao entra nem no amarelo da escada nem no azul da faisca");

		// ============================ O ESTALO TOCA MESMO ============================
		// Som e o efeito mais facil de "ligar" sem ligar: o caminho errado nao da erro, so silencio.
		// Ja aconteceu neste projeto (o som da carga com caminho errado e o laco quebrado). Aqui se
		// confere que os quatro arquivos existem E que a rajada incrementa o contador.
		// ========================================================================
		foreach (string sp in new[] { "sparks1", "sparks2", "sparks3", "sparks4" })
			Conferir(ResourceLoader.Exists($"res://Assets/Sounds/Effects/sparks/{sp}.mp3"),
					 $"o estalo `{sp}.mp3` existe no disco");
		if (corpo.GetNodeOrNull<RaiosDaForma>("Raios") is { } rf)
		{
			int estAntes = rf.EstalosDeTeste;
			rf.DispararDeTeste();
			Conferir(rf.EstalosDeTeste == estAntes + 1, "cada rajada de raio toca um estalo");
		}

		Conferir(ResourceLoader.Exists("res://Assets/Sprites/DU/Map/big crater.tres"),
				 "a folha da cratera grande existe no disco");
		Conferir(Jandirus.Net.Protocol.Decal.CrateraGrande != Jandirus.Net.Protocol.Decal.Cratera,
				 "a cratera grande e um tipo PROPRIO de decalque");
		var frCr = ResourceLoader.Load<SpriteFrames>("res://Assets/Sprites/DU/Map/big crater.tres");
		Conferir(frCr != null && frCr.HasAnimation("default"),
				 "a folha da cratera grande tem a animacao `default`");
		if (frCr?.GetFrameTexture("default", 0) is { } qc)
		{
			// 96x96: e o que permite ela ser grande sem virar borrao. O recorte `small_crater` da
			// folha `Craters` e bem menor -- esticar aquele era o defeito.
			Conferir(qc.GetWidth() >= 64,
					 $"o quadro da cratera grande e grande mesmo ({qc.GetWidth()}x{qc.GetHeight()})");
		}

		AsPedrasSubindo(t, corpo);


		Conferir(vis.PoseTravadaDeTeste, "a cena TRANCA a pose do corpo");
		string poseNaCena = vis.PoseDeTeste;
		Conferir(!poseNaCena.StartsWith("walk"), $"a pose trancada e a PARADA (e {poseNaCena})");

		vis.SetMotion(Jandirus.Core.World.Facing.East, moving: true);
		Conferir(vis.PoseDeTeste == poseNaCena, $"pedido de ANDAR ignorado na cena (virou {vis.PoseDeTeste})");
		vis.SetState("train");
		Conferir(vis.PoseDeTeste == poseNaCena, $"pedido de ESTADO ignorado na cena (virou {vis.PoseDeTeste})");
		vis.RestartState("attack");
		Conferir(vis.PoseDeTeste == poseNaCena, $"pedido de GOLPE ignorado na cena (virou {vis.PoseDeTeste})");
		_passos.Add($"  --     pose trancada em `{poseNaCena}`; 3 pedidos recusados");
		_visDaCena = vis;

		// ============================ TODAS AS CENAS, E NAO SO A DO SSJ4 ============================
		// O dono: "apos se transformar o personagem fica preso sem conseguir andar". A bancada
		// testava UMA cena (a do SSJ4, escolhida por ser a mais curta) e dava verde -- ela nao tinha
		// como ver um prazo errado nas outras oito.
		//
		// Aqui cada cena roda INTEIRA. Nao em tempo real (seriam ~110 s): o relogio e bombeado a
		// mao no `_Process` de producao, com o `SetProcess(false)` pra a engine nao adiantar junto.
		// E o mesmo codigo que roda em jogo -- so o relogio e nosso.
		// ================================================================================================
		// ============================ E AS ENCURTADAS RODAM JUNTO ============================
		// A lista era `Cinematicas.Todas`. Agora ela e cada cena DUAS vezes: cheia e encurtada.
		//
		// O motivo e o mesmo do `ConferirRoteiro` la em cima, so que ao vivo: a encurtada e tocada
		// pelo MESMO `Transformacao`, prende o MESMO contador estatico e pode travar o jogador do
		// mesmo jeito -- e ela e a que o jogador ve na maioria das vezes, porque a estreia roda uma vez
		// na vida do personagem. Testar so a cheia deixaria o caminho comum sem bancada.
		//
		// O `rotulo` existe porque as duas versoes tem o MESMO `Forma`: sem ele, uma reprova diria
		// "`ssj3`: nao soltou o corpo" sem dizer qual das duas.
		// ================================================================================
		var aRodar = new List<(Jandirus.Core.Forms.Cinematica C, string Rotulo)>();
		foreach (Jandirus.Core.Forms.Cinematica c0 in Jandirus.Core.Forms.Cinematicas.Todas)
		{
			aRodar.Add((c0, c0.Forma));
			var k0 = Jandirus.Core.Forms.Cinematicas.Encurtada(c0);
			if (!ReferenceEquals(k0, c0)) aRodar.Add((k0, $"{c0.Forma} curta"));
		}

		// A LINHA DE BASE DO TETO, pra a soma do catalogo inteiro la embaixo. Tirada aqui e nao zerada:
		// o contador do `Transformacao` e `static` e vive a execucao toda, e um `Zerar()` publico seria
		// uma segunda maneira de mexer nele -- ver o comentario do `TetosDeTeste`.
		int tetosNoCatalogo = Transformacao.TetosDeTeste;

		// LIDO UMA VEZ, e nao por cena: e a mesma folha nas 64. Ver o bloco da vida da pedra, mais
		// abaixo, pro que ele mede.
		double cicloDaPedra = CicloDaFolhaDasPedras();

		foreach ((Jandirus.Core.Forms.Cinematica cn, string rotulo) in aRodar)
		{
			Jandirus.Core.Forms.FormaDef? df = Jandirus.Core.Forms.Catalogo.Def(cn.Forma);
			if (df == null) { Conferir(false, $"a cena de `{rotulo}` aponta pra uma forma que existe"); continue; }

			// O PRAZO TEM QUE CASAR COM O MOMENTO DE VIRAR. Em 8 das 9 cenas o corpo e solto no
			// exato beat que assume a forma. Onde os dois divergem, o jogador ou fica preso depois
			// de ja ter virado, ou anda solto antes de virar -- as duas coisas leem como defeito.
			double assume = -1;
			foreach (Jandirus.Core.Forms.Beat bt in cn.Beats)
				if (bt.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir) && assume < 0) assume = bt.Em;
			Conferir(assume >= 0, $"`{rotulo}`: a cena tem um beat que ASSUME a forma");
			Conferir(assume < 0 || Mathf.Abs(cn.SegundosPreso - assume) < 0.51,
					 $"`{rotulo}`: solta o corpo quando assume a forma "
				   + $"(solta {cn.SegundosPreso:0.#}s, assume {assume:0.#}s)");

			// MEDIR POR LINHA DE BASE, e nao pelo sinal global. A cena da foto ainda esta viva
			// segurando o corpo; perguntar "o corpo esta livre?" mediria as duas juntas. A pergunta
			// certa e se ESTA cena devolveu a vez dela -- que e justamente a distincao que a tranca
			// booleana nao sabia fazer, e por isso o defeito existia.
			int presosAntes = Transformacao.PresosDeTeste;
			int poseAntes = vis.DonosDaPoseDeTeste;
			int tetosAntes = Transformacao.TetosDeTeste;

			var tc = Transformacao.Rodar(corpo.GetParent(), corpo, df, cn, souEu: true);
			tc.SetProcess(false);                       // o relogio e nosso, nao o da engine
			Conferir(Transformacao.PresosDeTeste == presosAntes + 1,
					 $"`{rotulo}`: prende o corpo ao comecar");
			Conferir(vis.DonosDaPoseDeTeste == poseAntes + 1, $"`{rotulo}`: tranca a pose ao comecar");

			// ============================ ATE DEPOIS DO PRAZO DO TETO, E ESSE E O PONTO ============================
			// Isto ia ate `Segundos + 3`. Tres e MENOR que a `FolgaDoTeto` (5), entao nenhuma destas cenas
			// jamais chegava perto do teto -- e "o teto nao disparou aqui" era uma afirmacao vazia, verdadeira
			// por o laco ter parado antes e nao por a cena ter se resolvido. Foi essa mesma folga que escondeu
			// o defeito das 67 cenas irmas quando ele so aparecia na `ssj4_full_power`, cujo bloco proprio
			// bombeava `Segundos + 12`.
			//
			// Agora todas passam do prazo com um segundo de sobra: quem nao tiver terminado sozinha ATE LA
			// dispara o teto de verdade, e as duas checagens la embaixo (o fim e a sonda) o pegam.
			//
			// O `FolgaDoTeto` e PERGUNTADO ao tocador e nao recopiado -- um `+ 6` cravado aqui voltaria a
			// parar antes do prazo no dia em que a folga crescesse, e a bancada emudeceria sem ninguem notar.
			// ====================================================================================================
			double soltouEm = -1;
			bool acendeuBase = false;
			int pedrasMax = 0;
			// ============================ "DO INICIO AO FIM" SE MEDE POR AMOSTRA, NAO POR PICO ============================
			// O pedido do dono (*"que ficariam do INICIO AO FIM"*) e sobre COBERTURA, e cobertura e a
			// unica coisa que um pico nao ve: uma cena que solta trinta pedras num quadro e fica muda
			// pelos outros vinte e seis segundos tem o mesmo pico de uma que as mantem o tempo todo. Era
			// exatamente esse o estado anterior -- a melhor cena do arquivo tinha pedra em 46,5% do tempo.
			//
			// Entao conta-se em quantos dos instantes amostrados havia pedra viva. So DENTRO da cena: o
			// laco corre alem do prazo pra provocar o teto, e ali a resposta certa e zero.
			// =========================================================================================================
			int amostras = 0, amostrasComPedra = 0;
			int estragoAntes = PoeiraDeEstrago.PedidosDeTeste;
			double ateQuando = cn.Segundos + Transformacao.FolgaDoTeto + 1.0;

			// ============================ E A VIDA DE CADA PEDRA SE MEDE NO NODE ============================
			// Ver <see cref="AVidaDaPedraEADoDm"/> pro enunciado inteiro. O que interessa aqui e por que a
			// medida e por NODE e nao por campo: a `PedraViva` guarda `Nasce`/`Morre`, e ler aqueles dois
			// numeros seria perguntar ao tocador o que ele PRETENDIA fazer. Quem some da tela e o node --
			// e ele tem os dois estados que o olho ve, `Visible` (o `TocarPedras` a acende na hora marcada)
			// e o `QueueFree`, que marca a morte no MESMO quadro mesmo com a liberacao adiada.
			//
			// SO DENTRO DA CENA, e nao ate o fim do bombeamento: depois que a cena se encerra o `_Process`
			// volta cedo, ninguem mais recolhe pedra, e uma pedra que nunca mais for marcada pareceria
			// viver ate o fim do laco. Cortar em `cn.Segundos` e o mesmo corte que a cobertura usa, e ele
			// tambem e a verdade da tela: o `QueueFree` da cena leva as pedras junto.
			//
			// ============================ E ELA NAO PODE CUSTAR MAIS QUE O QUE MEDE ============================
			// O `QueueFree` e ADIADO ate o fim do QUADRO, e este laco bombeia as 64 cenas dentro de UM
			// quadro so -- entao pedra morta nao sai da arvore: ela se acumula ali ate o fim da rodada.
			// Numa arvore sa isso e barato (o SSJ3 inteiro produz ~250 pedras), mas o custo cresce com o
			// numero de NASCIMENTOS, e nascimento e exatamente o que um defeito de vida multiplica.
			//
			// Isto ja pagou: com a vida encurtada de proposito pra provar esta checagem, a arvore chegou
			// a milhares de nodes mortos e a bancada PAROU -- perguntando `IsQueuedForDeletion` a cada um
			// deles, dez vezes por segundo de cena. Uma bancada que trava quando o defeito aparece nao
			// reprova o defeito: ela some.
			//
			// Duas travas, e as duas sao sobre custo e nao sobre a medida:
			//   * `mortas` -- a pedra ja recolhida nao e perguntada de novo. Uma vez morta, morta fica;
			//   * a amostra do CHAO corre a cada 0,5 s e nao a cada 0,1 s. O que se mede aqui vai de 2 a
			//     40 s; meio segundo de resolucao sobra, e a tolerancia la embaixo ja o absorve.
			// ==================================================================================================
			var deQuando = new Dictionary<ulong, double>();
			var ateQuandoViva = new Dictionary<ulong, double>();
			var mortas = new HashSet<ulong>();
			const double PassoDaAmostraDoChao = 0.5;
			double proximaAmostraDoChao = 0;
			Node? chao = tc.GetNodeOrNull("Pedras");

			for (double k = 0; k < ateQuando && IsInstanceValid(tc); k += 0.1)
			{
				tc._Process(0.1);
				if (soltouEm < 0 && Transformacao.PresosDeTeste == presosAntes) soltouEm = k;
				if (tc.AuraBaseDeTeste) acendeuBase = true;
				// O PICO, e nao o valor do fim: a pedra morre sozinha quando a animacao dela fecha.
				// Perguntar "quantas ha agora" depois de bombear a cena inteira mediria o momento em que
				// a resposta certa e zero nos dois casos.
				pedrasMax = Math.Max(pedrasMax, tc.PedrasVivasDeTeste);
				if (k < cn.Segundos)
				{
					amostras++;
					if (tc.PedrasVivasDeTeste > 0) amostrasComPedra++;

					if (k >= proximaAmostraDoChao)
					{
						proximaAmostraDoChao = k + PassoDaAmostraDoChao;
						foreach (Node n in chao?.GetChildren() ?? [])
						{
							if (n is not AnimatedSprite2D pedra) continue;
							ulong quem = pedra.GetInstanceId();
							if (mortas.Contains(quem)) continue;
							if (pedra.IsQueuedForDeletion()) { mortas.Add(quem); continue; }
							// AINDA NAO ACENDEU: o `TocarPedras` a torna visivel na hora sorteada. Contar
							// daqui seria contar a espera dela junto com a vida.
							if (!pedra.Visible) continue;
							if (!deQuando.ContainsKey(quem)) deQuando[quem] = k;
							ateQuandoViva[quem] = k;
						}
					}
				}
			}
			Conferir(soltouEm >= 0, $"`{rotulo}`: SOLTOU o corpo (senao o jogador trava pra sempre)");

			// ============================ E SOLTOU NA HORA ESCRITA ============================
			// Isto era a metade que faltava. A bancada conferia que o prazo (`SegundosPreso`) casa com o
			// beat que assume -- nos DADOS -- e que o corpo era solto em algum momento. Ninguem perguntava
			// se o TOCADOR honra o prazo. Sao coisas diferentes: o dado pode estar perfeito e a linha que
			// o le estar errada.
			//
			// COMO REPROVA SE A REGRA SUMIR: troque o `_t >= _cena.SegundosPreso` do `Transformacao._Process`
			// por `_t >= _cena.Segundos` (que e a leitura ingenua de "solta no fim da cena", e ja foi o
			// comportamento deste arquivo). Todas as checagens de dado continuam verdes e esta cai nas 68 --
			// no SSJ3 o jogador ficaria 33 s preso em vez de 32.
			//
			// A TOLERANCIA E UM PASSO DO RELOGIO BOMBEADO (0,1 s) mais folga de arredondamento.
			// ==========================================================================
			Conferir(soltouEm < 0 || Math.Abs(soltouEm - cn.SegundosPreso) <= 0.2,
					 $"`{rotulo}`: soltou no prazo escrito (soltou {soltouEm:0.##}s, prazo {cn.SegundosPreso:0.##}s)");

			// ============================ E SE ENCERROU SOZINHA, QUE NAO E A MESMA COISA ============================
			// As duas linhas acima perguntam pelo CORPO: ele foi solto, e na hora. Nenhuma delas pergunta se
			// a CENA acabou -- e as duas continuam verdes quando quem soltou o corpo foi o teto, porque o teto
			// solta o corpo tambem (e essa e a razao de ele existir).
			//
			// Uma checagem que aceita "a rede me salvou" como sucesso e cega exatamente no caso que a rede
			// existe pra sinalizar: o `FolgaDoTeto` diz, com todas as letras, que disparar E DEFEITO. Entao a
			// pergunta certa e pelo FIM, e ela distingue os tres (`Sozinha`, `Teto`, `AlvoSumiu`).
			//
			// COMO REPROVA SE A REGRA SUMIR: ponha um beat com `Em` maior que todos DEPOIS do ultimo beat de
			// qualquer cena -- o laco de disparo (`Beats[_proximo].Em <= _t`) trava nele, `_proximo` nunca
			// alcanca o fim, a cena nunca se encerra e o teto a solta. Tudo o mais continua verde; esta cai.
			// ====================================================================================================
			Conferir(tc.FimDeTeste == Transformacao.FimDaCena.Sozinha,
					 $"`{rotulo}`: a cena se ENCERROU SOZINHA (fim `{tc.FimDeTeste}`) -- "
				   + $"parou no beat {tc.BeatsDeTeste} de {cn.Beats.Length}, aos {tc.TempoDeTeste:0.##}s "
				   + $"de {cn.Segundos:0.##}s");

			// ============================ E O TETO NAO DISPAROU -- CONTADO, NAO SUPOSTO ============================
			// A linha de cima le o ESTADO da cena; esta conta os DISPAROS. Parecem a mesma pergunta e nao sao,
			// e a diferenca e o defeito que acabou de ser pago: uma cena que termina sozinha e continua sendo
			// bombeada volta a somar tempo, cruza o prazo e faz o teto gritar -- uma vez por quadro, com o
			// `FimDeTeste` marcando `Teto` uma vez so. Um estado, muitos avisos.
			//
			// E o aviso e a unica coisa que o dono ve. `GD.PushWarning` vai pro log da engine; setenta e um
			// deles sairam por rodada com o placar inteiro VERDE, porque nao havia ninguem no jogo capaz de
			// contá-los. Este e o `Transformacao.TetosDeTeste`, e ele fecha esse ponto cego: a cena e obrigada
			// a atravessar o prazo (ver o laco acima) e o contador tem que ficar PARADO.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire o `if (_fim != FimDaCena.Rodando) return;` do topo do
			// `Transformacao._Process` -- o guarda que ignora cena encerrada. Toda cena desta lista volta a
			// contar depois de morta, cruza o prazo, e esta linha cai nas 68 com o numero de avisos em cada
			// uma. O `FimDeTeste` acima cai junto, e e de proposito: sao as duas metades da mesma regra
			// (nao precisar do teto, e nao acordar depois de morta) vistas por dois angulos.
			// ====================================================================================================
			Conferir(Transformacao.TetosDeTeste == tetosAntes,
					 $"`{rotulo}`: e o TETO NAO disparou nela -- rodou {ateQuando:0.#}s, "
				   + $"{Transformacao.FolgaDoTeto:0.#}s alem do prazo, e soltou {Transformacao.TetosDeTeste - tetosAntes} aviso(s)");

			// ============================ AS PEDRAS SAO DA CENA, NOS DOIS SENTIDOS ============================
			// A checagem dedicada das pedras (`AsPedrasSubindo`) enche o chao NA MARRA, pra medir grade,
			// camada e alcance. Ela nao prova que o estado da cena CHEGA la -- e o `OChaoSeSolta` e um
			// `if` que qualquer refatoracao do `TocarPedras` pode perder calado.
			//
			// Aqui a pergunta e feita a CENA, e nos DOIS sentidos, porque cada lado pega um defeito
			// diferente:
			//   * cena que solta o chao e sem pedra = o estado nao chegou no `NascerPedra` (efeito
			//     escrito e nunca lido, a falha assinatura deste projeto);
			//   * cena que NAO solta e com pedra = alguem levantou pedra por fora do gate. E e este lado
			//     que cobre o pedido do dono sobre o macaco -- *"oozaru n tem esse efeito de rocks nem de
			//     particulas"* --, porque a cena do Oozaru esta nesta mesma lista.
			// ================================================================================================
			bool devePedra = cn.OChaoSeSolta;
			Conferir(devePedra ? pedrasMax > 0 : pedrasMax == 0,
					 $"`{rotulo}`: as pedras casam com a cena "
				   + $"({(devePedra ? "solta" : "nao solta")} o chao, nasceram {pedrasMax})");

			// ============================ E ELAS FICAM, DO INICIO AO FIM ============================
			// *"deveria ter mais `rising rocks.png` q ficariam do INICIO AO FIM em todas as
			// transformacoes"*. Esta e a linha que cobra o pedido, e ela nao e a de cima: `pedrasMax > 0`
			// ficava VERDE com o estado anterior, em que a cena do SSJ1 tinha pedra em 13,1% do tempo.
			//
			// O CORTE E 90% e nao 100% porque as duas pontas sao legitimas: a primeira pedra nasce com
			// ate 0,2 s de atraso (o `if(prob(20)) sleep(1)` da varredura do DM) e a ultima morre ate um
			// ciclo da folha antes do fim (pedra que nao cabe inteira na cena nao nasce -- ver
			// `NascerPedra`). Numa cena de 4,6 s isso ja e ~7% dos dois lados somados.
			//
			// COMO REPROVA SE A REGRA SUMIR: devolva a pedra ao beat (uma leva por `Efeito`) e a
			// cobertura despenca pra faixa de 13% a 46% nas 64 cenas -- que e o numero medido antes
			// deste passe.
			// ==================================================================================
			if (devePedra && amostras > 0)
			{
				double cobertura = amostrasComPedra / (double)amostras;
				Conferir(cobertura >= 0.90,
						 $"`{rotulo}`: e ha pedra viva do inicio ao fim ({cobertura * 100:0.#}% da cena, "
					   + $"{amostrasComPedra} de {amostras} amostras)");
			}

			// ============================ E CADA UMA DURA O QUE O DM MANDA ============================
			// *"aumente a area ... e dura mt pouco"*. A AREA tinha bancada (o alcance e refeito da camera
			// no `AsPedrasSubindo`); a VIDA nao tinha nenhuma, e ela e a metade da frase que o dono
			// escreveu. A `VidaMinima`/`VidaMaxima` (10 a 40 s, o `spawn(rand(100,400))` de `dusts.dm:207`)
			// era um par de constantes que ninguem media -- escritas, e so.
			//
			// E A COBERTURA ACIMA NAO A COBRE, que e o ponto: com a vida de volta pros 2,4 s antigos, a
			// cadencia de reposicao (`_intervaloDePedra`) continua repondo e continua havendo pedra viva
			// em quase todo instante. A cobertura fica VERDE. O que muda e o que o olho ve -- pedra
			// piscando em vez de chao solto --, e era exatamente disso que o dono reclamou.
			//
			// O QUE SE COBRA E A VIDA OBSERVADA de cada node, contra a faixa DERIVADA:
			//   * o PISO e `VidaMinima` cortada em ciclos inteiros da folha (a pedra some no fim da
			//     animacao, nao no meio dela -- ver `NascerPedra`), e ele cede pro que resta de cena
			//     quando a cena e mais curta que isso. Sem essa segunda metade, as cenas de 4,6 s
			//     reprovariam por obedecer;
			//   * o TETO e `VidaMaxima`, cru.
			// O ciclo e RELIDO da folha (ver `CicloDaFolhaDasPedras`) e nao copiado do tocador.
			//
			// A ANCORA DOS DOIS NUMEROS ESTA NO `AVidaDaPedraEADoDm`, e ela e obrigatoria: o piso daqui e
			// DERIVADO da propria `Transformacao.VidaMinima`, entao baixa-la baixa a regua junto. Este
			// bloco prova que a constante CHEGA na tela; quem prova que ela e a do original e la.
			//
			// COMO REPROVA SE A REGRA SUMIR: troque a vida por `ciclo * (2 + GD.Randi() % 2)`, que e
			// literalmente a linha anterior a este passe, e as 60 cenas que soltam chao caem aqui.
			// =====================================================================================
			if (devePedra && cicloDaPedra > 0 && deQuando.Count > 0)
			{
				double piso = Math.Max(1, (int)(Transformacao.VidaMinima / cicloDaPedra)) * cicloDaPedra;
				int curtas = 0, longas = 0;
				double pior = double.MaxValue, maisLonga = 0;
				foreach ((ulong quem, double nasceu) in deQuando)
				{
					double vista = ateQuandoViva[quem] - nasceu;
					maisLonga = Math.Max(maisLonga, vista);
					// A TOLERANCIA E O PASSO DA AMOSTRA DUAS VEZES, e o "duas" custou uma rodada: a
					// amostra corta os DOIS extremos da vida, nao um. Ela ve a pedra acesa so na
					// primeira passada DEPOIS de ela acender (perde ate um passo no comeco) e viva pela
					// ultima vez na ultima passada ANTES de ela morrer (perde ate um passo no fim).
					// Com um passo so de folga, o SSJ1 reprovou com uma pedra de 8,8 s contra um piso de
					// 9,6 -- uma pedra perfeita, medida curta pela regua.
					//
					// Mais 0,3 pelo pingo de atraso do nascimento (o `if(prob(20)) sleep(1)` do DM). O
					// total (1,3 s) continua cabendo de sobra no defeito que este bloco existe pra pegar:
					// a vida antiga era 2,4 s contra um piso de 9,6.
					if (vista < Math.Min(piso, cn.Segundos - nasceu) - (PassoDaAmostraDoChao * 2 + 0.3))
					{
						curtas++;
						pior = Math.Min(pior, vista);
					}
					// O TETO NAO PRECISA DA MESMA FOLGA -- a amostra so sabe medir MENOS que a vida real,
					// nunca mais --, mas ele a leva pelo mesmo motivo: a regua e uma so.
					if (vista > Transformacao.VidaMaxima + (PassoDaAmostraDoChao * 2 + 0.3)) longas++;
				}
				Conferir(curtas == 0,
						 $"`{rotulo}`: nenhuma pedra vive menos que os {piso:0.#}s do DM "
					   + $"({curtas} de {deQuando.Count}"
					   + $"{(curtas > 0 ? $", a pior com {pior:0.##}s" : "")})");
				Conferir(longas == 0,
						 $"`{rotulo}`: e nenhuma passa dos {Transformacao.VidaMaxima:0.#}s "
					   + $"({longas} de {deQuando.Count}, a mais longa com {maisLonga:0.##}s)");
			}

			// ============================ E A POPULACAO TEM TETO, QUE E O CUSTO ============================
			// A resposta a *"mais pedras"* foi multiplicar a populacao por quatro (de 10 por leva pra 41
			// vivas o tempo todo) e a vida por dez. As duas juntas sao o unico jeito de este passe virar
			// custo: o `while` do `TocarPedras` para no `_alvoDePedras`, e se ele parar de parar a cena
			// do SSJ3 (143 s, reposicao continua) enche a arvore de node.
			//
			// A checagem do `AsPedrasSubindo` cobra o teto ABSOLUTO (uma por tile) num enchimento na
			// marra. Esta cobra o teto DA CENA, ao vivo, com o relogio correndo -- que e o unico lugar
			// onde a reposicao existe pra ultrapassa-lo.
			// ==========================================================================================
			Conferir(pedrasMax <= tc.AlvoDePedrasDeTeste,
					 $"`{rotulo}`: a populacao de pedra nao passa do alvo derivado "
				   + $"({pedrasMax} vivas no pico, alvo {tc.AlvoDePedrasDeTeste})");

			// ============================ E O CEU DESCARREGA, NA CENA CERTA E SO NELA ============================
			// *"o ssj1 na cinematica da primeira vez, deveria fazer raios cairem durante TODA a
			// cinematica na regiao q o personagem esta se transformando"*.
			//
			// A pergunta e feita a CENA (`OCeuDescarrega`) e nao a um id escrito aqui, entao os DOIS
			// recortes do pedido caem nesta linha de graca -- esta lista roda as 64 cenas, e as 63 que
			// nao sao a estreia do SSJ1 (inclusive a ENCURTADA dele, que e uma delas) tem que dar ZERO.
			// Um "so na primeira vez" que vazasse pra encurtada nao teria como se esconder aqui.
			//
			// A FAIXA E LARGA PORQUE O INTERVALO E SORTEADO, e ela e mesmo assim a linha que cobra o
			// *"durante TODA a cinematica"*: o piso `duracao / DescargaMaxima` so e alcancavel se as
			// descargas atravessarem a cena inteira. Uma tempestade que parasse no `Assumir`, ou que
			// virasse um pulso, cai aqui -- "aconteceu pelo menos uma vez" nao cairia.
			//
			// A DESCARGA SO SAI EM PLANETA (ver `Transformacao.TocarTempestade`), pelo mesmo motivo e
			// com a mesma forma da checagem do `DescargaNoCeu` mais abaixo: pergunta-se pela zona em
			// vez de cravar um dos dois lados.
			// ====================================================================================================
			bool ceuAberto = cn.OCeuDescarrega
						  && Jandirus.Core.World.Espaco.EhPlaneta(GameClient.Instance?.Zone ?? default);
			int minRaios = ceuAberto ? (int)(cn.Segundos / Cinematicas.DescargaMaxima) : 0;
			int maxRaios = ceuAberto ? (int)((cn.Segundos + 0.2) / Cinematicas.DescargaMinima) : 0;
			Conferir(tc.RaiosDaEstreiaDeTeste >= minRaios && tc.RaiosDaEstreiaDeTeste <= maxRaios,
					 $"`{rotulo}`: o ceu descarrega o quanto a cena manda "
				   + $"({tc.RaiosDaEstreiaDeTeste} raios, esperado {minRaios}..{maxRaios})");

			// ============================ E ELES CAEM EM VOLTA, NAO EM CIMA ============================
			// *"na regiao q o personagem esta se transformando"* -- uma AREA, e nao a coordenada dele.
			// O `Efeito.DescargaNoCeu` do beat atinge o proprio corpo (`_alvo.GlobalPosition`), entao
			// "raio na cinematica" ja existia com o defeito de sempre cair no mesmo pixel: dezessete
			// deles ali seriam um estrobo em cima do boneco, e a REGIAO nunca apareceria.
			//
			// Duas cobrancas, e as duas sao geometricas porque nenhuma foto as pega (o risco fica
			// visivel 0,333 s e a bancada nao ve quadro):
			//   * TODO ponto esta no anel [miolo, `RaioDoTremorCheio`] medido do centro CONGELADO --
			//     o de dentro prova que nao caiu em cima do sprite, o de fora prova que a tempestade
			//     nao virou uma tela inteira de raio;
			//   * ha mais de UM ponto distinto -- um sorteio quebrado que devolvesse sempre o mesmo
			//     angulo passaria no anel e continuaria sendo o defeito que este bloco existe pra pegar.
			// ==========================================================================================
			if (ceuAberto && tc.RaiosDaEstreiaDeTeste > 0)
			{
				Vector2[] pontos = tc.PontosDeRaioDeTeste;
				int foraDoAnel = pontos.Count(p =>
				{
					float d = p.DistanceTo(tc.CentroDaTempestadeDeTeste);
					return d < tc.MioloDaTempestadeDeTeste - 0.01f
						|| d > Cinematicas.RaioDoTremorCheio + 0.01f;
				});
				Conferir(foraDoAnel == 0,
						 $"`{rotulo}`: todo raio caiu no anel de {tc.MioloDaTempestadeDeTeste:0.#} a "
					   + $"{Cinematicas.RaioDoTremorCheio:0.#}px do centro ({foraDoAnel} fora)");
				Conferir(pontos.Distinct().Count() > 1,
						 $"`{rotulo}`: e eles se espalham pela regiao ({pontos.Distinct().Count()} pontos distintos)");
			}

			// ============================ E A CENA NAO QUEBRA CENARIO ============================
			// *"...uns quadrados marrons caindo e criando uma fumaca parecendo q quebrou uma parede ou
			// objeto, TIRE esse efeito"*. Era o `Efeito.Cascalho` -> `PoeiraDeEstrago.Soltar`.
			//
			// O bit foi aposentado, entao o compilador ja impede o caminho ANTIGO. Esta linha impede o
			// caminho NOVO: uma chamada direta a `PoeiraDeEstrago` de dentro do tocador, que e
            // literalmente como o efeito entrou na primeira vez. O contador e o da propria classe, medido
			// em volta da cena inteira -- se qualquer beat pedir um pedaco, ele anda.
			//
			// A BANCADA E QUEM TEM QUE PERGUNTAR ISSO, e nao o leitor: cascalho a mais numa cinematica
			// LE COMO EFEITO, e foi assim que ele sobreviveu ali desde que foi escrito.
			// =================================================================================
			Conferir(PoeiraDeEstrago.PedidosDeTeste == estragoAntes,
					 $"`{rotulo}`: nao pediu UM pedaco de cascalho a `PoeiraDeEstrago` "
				   + $"({PoeiraDeEstrago.PedidosDeTeste - estragoAntes})");

			// ============================ E OS QUATRO EFEITOS NOVOS, PELA MESMA REGRA ============================
			// Os tres do enchimento (anel, clarao, descarga) sao exatamente o que a checagem das
			// pedras acima existe pra pegar: um bit num `if` do `Disparar`, que qualquer refatoracao perde
			// calado -- e a perda e INVISIVEL em jogo, porque uma cena com um efeito a menos continua sendo
			// uma cena.
			//
			// ERAM QUATRO. O cascalho saiu com o efeito (bit 8192 aposentado, cortado pelo dono), e a
			// checagem dele nao virou nada aqui: ela virou o `PoeiraDeEstrago.PedidosDeTeste` la em cima,
			// que e mais forte -- em vez de contar disparos do bit, cobra que o SISTEMA nao seja tocado.
			//
			// A CONTAGEM E POR BEAT e nao por "aconteceu": o contador do tocador sobe uma vez por disparo,
			// entao ele tem que bater com quantos beats do roteiro pedem aquilo. "Aconteceu pelo menos uma
			// vez" passaria numa cena que perdesse oito dos nove beats de anel do SSJ3.
			//
			// A DESCARGA E A EXCECAO, e ela e honesta: ela so sai em PLANETA (ver `Transformacao.Descarga`).
			// A bancada roda na Terra, entao o esperado e o numero cheio; fora de um planeta o esperado
			// seria zero, e nesse caso o que se mede aqui e o gate e nao o efeito. Por isso a comparacao
			// pergunta pela zona em vez de cravar um dos dois lados.
			// ================================================================================================
			int quantos(Jandirus.Core.Forms.Efeito e) => cn.Beats.Count(b => b.Faz.HasFlag(e));

			Conferir(tc.AneisDeTeste == quantos(Jandirus.Core.Forms.Efeito.AnelDeChoque),
					 $"`{rotulo}`: os aneis de choque casam com o roteiro "
				   + $"({tc.AneisDeTeste} de {quantos(Jandirus.Core.Forms.Efeito.AnelDeChoque)})");

			Conferir(tc.ClaroesDeTeste == quantos(Jandirus.Core.Forms.Efeito.ClaraoDeTela),
					 $"`{rotulo}`: o clarao casa com o roteiro "
				   + $"({tc.ClaroesDeTeste} de {quantos(Jandirus.Core.Forms.Efeito.ClaraoDeTela)})");

			int descargasEsperadas =
				Jandirus.Core.World.Espaco.EhPlaneta(GameClient.Instance?.Zone ?? default)
					? quantos(Jandirus.Core.Forms.Efeito.DescargaNoCeu) : 0;
			Conferir(tc.DescargasDeTeste == descargasEsperadas,
					 $"`{rotulo}`: as descargas no ceu casam com o roteiro e com o lugar "
				   + $"({tc.DescargasDeTeste} de {descargasEsperadas})");
			Conferir(Transformacao.PresosDeTeste == presosAntes, $"`{rotulo}`: devolveu a vez no fim");
			Conferir(vis.DonosDaPoseDeTeste == poseAntes, $"`{rotulo}`: devolveu a pose no fim");

			// ============================ A AURA EMPRESTADA TEM QUE VOLTAR ============================
			// So a cena do Oozaru acende a aura BASE do corpo (`Efeito.AuraBase`), e o dono pediu as
			// duas metades: *"a aura do personagem vai ativar ... e nesse momento a aura desativa"*.
			//
			// A segunda metade e a que da defeito silencioso. Acender e visivel na hora; NAO apagar
			// so aparece depois -- um macaco (ou, se a cena morrer antes, um lutador em forma base)
			// brilhando pra sempre, sem nada no jogo que explique por que nem como desligar. Por
			// isso o teste mede o ESTADO DO NODE `Aura` no fim, e nao o do tocador: o tocador ja
			// morreu, e quem ficou aceso na tela e o node.
			if (cn.Beats.Any(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.AuraBase)))
			{
				Conferir(acendeuBase, $"`{rotulo}`: a aura BASE acendeu durante a cena");
				Conferir(corpo.GetNodeOrNull<Aura>("Aura") is not { AcesaDeTeste: true },
						 $"`{rotulo}`: e APAGOU quando a forma ficou (senao brilha pra sempre)");
			}
			_passos.Add($"  --     `{rotulo}`: preso {soltouEm:0.#}s, cena {cn.Segundos:0.#}s");
			if (IsInstanceValid(tc)) tc.QueueFree();
		}

		// A CENA DO OOZARU DEIXA UM MACACO NO BONECO. Ela e a unica da lista cujo `Assumir` instala
		// uma CRIATURA -- as outras instalam pelagem (ou nada), e `CorpoDaForma("")` limpa sozinho.
		// Sem esta linha o resto da bancada mediria um corpo de 96x96 sem cabelo nem roupa, e a foto
		// do fim sairia com um macaco no lugar do lutador. Tira tambem o tween de crescimento, que
		// e do relogio da engine e nao do relogio bombeado aqui.
		vis.CorpoDaForma(CorpoDeForma.Nenhum);
		Conferir(!vis.EhCriatura, "as cenas nao deixaram o boneco virado macaco");

		// ============================ NENHUMA CENA DO JOGO PRECISA DO TETO ============================
		// As 68 linhas de cima dizem, uma por uma, "esta aqui nao precisou". Esta diz a frase inteira, e
		// ela e a que o dono pediu: o CATALOGO nao precisa. As duas nao sao a mesma coisa -- as individuais
		// medem cada cena isolada; esta soma tambem o que acontece ENTRE elas, que e onde mora o defeito
		// que motivou o contador (uma cena morta que o quadro seguinte ressuscita).
		//
		// E ela e a que sobrevive a lista: no dia em que a 35a cinematica nascer, ela entra na conta sem
		// ninguem editar numero nenhum -- `Cinematicas.Todas` e a fonte, o `aRodar` deriva as encurtadas,
		// e o esperado aqui e ZERO e nao um total.
		//
		// COMO REPROVA SE A REGRA SUMIR: e a mesma do guarda do `_Process` (tire o
		// `if (_fim != FimDaCena.Rodando) return;`) -- so que aqui a mensagem sai com o numero cru de
		// avisos da rodada, que e a forma em que o defeito foi visto pela primeira vez: "71".
		// ==========================================================================================
		Conferir(Transformacao.TetosDeTeste == tetosNoCatalogo,
				 $"NENHUMA das {aRodar.Count} cenas do catalogo precisou do teto pra se encerrar "
			   + $"({Transformacao.TetosDeTeste - tetosNoCatalogo} aviso(s) em "
			   + $"{Jandirus.Core.Forms.Cinematicas.Todas.Length} cheias + as encurtadas)");

		// AQUI, e nao junto do resto das pedras: este bloco roda DUAS cenas inteiras, e o lugar da
		// bancada onde rodar cena e rotina e este -- logo depois das 64. Chamado la em cima, no meio da
		// cena da foto, ele mexeria na pose e no corpo que os blocos seguintes medem.
		AAreaDaPedraEMedidaENaoEscrita(corpo);

		// ============================ O TETO TEM QUE DISPARAR ============================
		// "Um teto que nunca dispara e indistinguivel de nao ter teto." Se ele nao existisse, o
		// contador ficaria pendurado e o jogador travaria PARA SEMPRE -- que e exatamente o que o
		// dono relatou.
		//
		// ============================ E ELE PRECISA DE UMA CENA QUE NAO CONSIGA TERMINAR ============================
		// Isto rodava uma cena NORMAL (a mais curta do jogo) e bombeava o relogio 12 s alem dela, contando
		// que "passar do prazo" bastasse. Nao basta: uma cena normal se encerra sozinha na hora certa, e o
		// que fazia o teto disparar era um artefato da bancada -- `QueueFree()` so libera no fim do QUADRO, e
		// como o laco bombeia tudo dentro de um quadro so, ele continuava empurrando uma cena ja encerrada
		// ate ela cruzar o teto. Setenta e um avisos por rodada, e todos falsos.
		//
		// Com o `Transformacao._Process` ignorando cena encerrada (que e o conserto), aquele caminho morreu --
		// e este bloco viraria uma linha verde medindo nada, porque o corpo teria sido solto pelo fim NORMAL.
		//
		// Entao a cena travada e montada aqui, e ela e o defeito de verdade que o teto existe pra cobrir: um
		// beat cujo instante nunca vence, posto ANTES do ultimo. O laco de disparo para no primeiro beat que
		// ainda nao venceu, entao `_proximo` nunca alcanca o fim do roteiro; e como `Cinematica.Segundos` sai
		// do ULTIMO beat (e nao do maior), a cena "dura" 2 s e fica presa pra sempre. `SegundosPreso` alem do
		// teto fecha a ultima porta: ninguem alem da rede tem como devolver o corpo.
		// ========================================================================================================
		{
			int b1 = Transformacao.PresosDeTeste;
			int tetosB1 = Transformacao.TetosDeTeste;

			var travada = new Jandirus.Core.Forms.Cinematica
			{
				Forma = daCena.Id,
				SegundosPreso = 999,       // so a rede solta este corpo
				Beats =
				[
					new(0.0, Jandirus.Core.Forms.Efeito.Tremor),
					new(600.0, Jandirus.Core.Forms.Efeito.Poeira),   // o beat que nunca vence
					new(1.0, Jandirus.Core.Forms.Efeito.Poeira),     // o ultimo, e e ele que da o `Segundos`
				],
			};

			var estourada = Transformacao.Rodar(corpo.GetParent(), corpo, daCena, travada, souEu: true);
			estourada.SetProcess(false);
			Conferir(Transformacao.PresosDeTeste == b1 + 1, "a cena do teto prendeu o corpo");

			// O INSTANTE EM QUE O TETO DISPAROU, guardado no laco: e ele que prova, la embaixo, que a cena
			// PAROU de contar depois. Sem essa metade, o teto disparando 71 vezes passaria aqui igualzinho.
			double tNoTeto = -1;
			for (double k = 0; k < travada.Segundos + 12 && IsInstanceValid(estourada); k += 0.1)
			{
				estourada._Process(0.1);
				if (tNoTeto < 0 && IsInstanceValid(estourada)
				 && estourada.FimDeTeste == Transformacao.FimDaCena.Teto)
					tNoTeto = estourada.TempoDeTeste;
			}

			bool viva = IsInstanceValid(estourada);
			Transformacao.FimDaCena fim = viva ? estourada.FimDeTeste : Transformacao.FimDaCena.Rodando;
			double tDepois = viva ? estourada.TempoDeTeste : -1;

			Conferir(viva && fim == Transformacao.FimDaCena.Teto,
					 $"o TETO disparou numa cena que NAO consegue terminar (fim `{fim}`)");
			Conferir(Transformacao.PresosDeTeste == b1,
					 $"e o contador voltou pra base (tem {Transformacao.PresosDeTeste}, base {b1})");
			Conferir(tNoTeto > 0 && Mathf.IsEqualApprox((float)tDepois, (float)tNoTeto),
					 $"e a cena parou de correr no instante do teto ({tNoTeto:0.##}s -> {tDepois:0.##}s) -- "
				   + "uma cena morta que volta a contar reclama a cada quadro");

			// ============================ E RECLAMOU UMA VEZ SO ============================
			// A linha de cima le o relogio; esta le o MEGAFONE, e e ela que mede a coisa que o dono
			// enxergava. O laco continua bombeando ~7 s DEPOIS do teto ter disparado (vai ate
			// `Segundos + 12`): cada um desses passos e uma chance de o aviso sair de novo, e era
			// exatamente assim que um unico defeito virava setenta e um.
			//
			// Um teto que reclama por quadro nao e um teto -- e um alarme quebrado, e um alarme que
			// grita sempre e igual a nao ter alarme: ninguem le o log depois do decimo.
			//
			// COMO REPROVA SE A REGRA SUMIR: tire o `if (_fim != FimDaCena.Rodando) return;` do
			// `Transformacao._Process` e esta linha volta a acusar setenta e tantos, sozinha e com o
			// numero na cara -- e ela reprova mesmo se alguem "consertar" o relogio da linha de cima
			// sem consertar o aviso.
			// =========================================================================
			Conferir(Transformacao.TetosDeTeste == tetosB1 + 1,
					 $"e avisou UMA vez so, nao uma por quadro ({Transformacao.TetosDeTeste - tetosB1} aviso(s) "
				   + $"em {(travada.Segundos + 12) / 0.1:0} passos)");
			if (IsInstanceValid(estourada)) estourada.QueueFree();
		}

		// NENHUMA CENA PODE TER DEIXADO DONO PENDURADO. Um contador que sobe e nao desce e a
		// mesma coisa que a tranca velha: o jogador nunca mais anda.
		// O QUE SOBRA TEM QUE SER SO A CENA DA FOTO (1), e nao um dono por cena.
		// (Dizia "as nove" quando eram nove cenas; o Oozaru virou a decima e o numero cravado no
		// texto ja tinha envelhecido -- por isso agora ele nao esta mais escrito em lugar nenhum.)
		Conferir(Transformacao.PresosDeTeste <= 1,
				 $"nenhuma cena deixou dono pendurado (contador {Transformacao.PresosDeTeste})");
		Conferir(vis.DonosDaPoseDeTeste <= 1,
				 $"nenhuma cena deixou a pose presa (donos {vis.DonosDaPoseDeTeste})");

		// ============================ DUAS CENAS AO MESMO TEMPO ============================
		// O defeito que o dono relatou: "quando executa todas as transformaçoes de uma vez, o jogo
		// buga e ele tenta me deixar andar mas fico preso".
		//
		// Com a tranca booleana, a cena CURTA terminava e escrevia `false` enquanto a LONGA ainda
		// segurava -- e como as duas trancas (input e pose) sao apagadas em pontos diferentes, uma
		// soltava e a outra nao. Este teste solta fora de ordem de proposito, que e o caso que a
		// versao booleana nao aguentava.
		// ==========================================================================
		{
			int b0 = Transformacao.PresosDeTeste, p0 = vis.DonosDaPoseDeTeste;
			var curta = Transformacao.Rodar(corpo.GetParent(), corpo, daCena, cena, souEu: true);
			Jandirus.Core.Forms.Cinematica cLonga = Jandirus.Core.Forms.Cinematicas.Ssj1;
			var longa = Transformacao.Rodar(corpo.GetParent(), corpo,
											Jandirus.Core.Forms.Catalogo.Def(cLonga.Forma)!, cLonga, souEu: true);
			curta.SetProcess(false); longa.SetProcess(false);

			Conferir(Transformacao.PresosDeTeste == b0 + 2,
					 $"duas cenas = dois donos a mais (tem {Transformacao.PresosDeTeste}, base {b0})");
			Conferir(vis.DonosDaPoseDeTeste == p0 + 2,
					 $"duas cenas = dois donos da pose a mais (tem {vis.DonosDaPoseDeTeste})");

			for (double k = 0; k < cena.SegundosPreso + 1 && IsInstanceValid(curta); k += 0.1) curta._Process(0.1);
			Conferir(Transformacao.PresosDeTeste == b0 + 1 && Transformacao.PrendendoOCorpo,
					 $"a cena CURTA acabou e o corpo continua preso pela LONGA (donos {Transformacao.PresosDeTeste})");
			Conferir(vis.PoseTravadaDeTeste && vis.DonosDaPoseDeTeste == p0 + 1,
					 "a pose continua trancada pela cena LONGA");

			for (double k = 0; k < cLonga.SegundosPreso + 1 && IsInstanceValid(longa); k += 0.1) longa._Process(0.1);
			Conferir(Transformacao.PresosDeTeste == b0, "a ULTIMA cena a acabar e que solta o corpo");
			Conferir(vis.DonosDaPoseDeTeste == p0, "e a pose destranca junto");

			if (IsInstanceValid(curta)) curta.QueueFree();
			if (IsInstanceValid(longa)) longa.QueueFree();
		}

		// ============================ A REDE TEM QUE DISPARAR ============================
		// O dono ficou "preso pra sempre" depois de um duplo C. A rede solta o corpo na marra
		// passado o prazo -- e uma rede que nunca fira nao se distingue de nao ter rede nenhuma.
		// Aqui ela e OBRIGADA a disparar: prende o corpo e adianta o relogio dela.
		// ========================================================================
		{
			var presa = Transformacao.Rodar(corpo.GetParent(), corpo, daCena, cena, souEu: true);
			presa.SetProcess(false);              // ninguem vai solta-la pelo caminho normal
            int b1 = Transformacao.PresosDeTeste;
			Conferir(b1 >= 1, $"a cena de teste prendeu o corpo (donos {b1})");

			Transformacao.VigiarTranca(1.0);
			Conferir(Transformacao.PresosDeTeste == b1,
					 "a rede NAO solta antes do prazo (senao ela cortaria cenas legitimas)");

			// MUITO ALEM DO PRAZO, e o prazo e PERGUNTADO: ele agora sai da cena mais longa do jogo
			// (`Transformacao.PrazoMaximoPreso`), entao um "60" cravado aqui deixaria de fazer a rede
			// disparar no dia em que uma cena passasse dos 30 s -- que e exatamente o que aconteceu
			// quando o SSJ3 voltou aos 140 s do DM.
			Transformacao.VigiarTranca(Transformacao.PrazoMaximoPreso + 1.0);
			Conferir(!Transformacao.PrendendoOCorpo,
					 $"a rede SOLTOU o corpo na marra (donos {Transformacao.PresosDeTeste})");

			if (IsInstanceValid(presa)) presa.QueueFree();
		}
		_passos.Add($"  --     cena de estreia rodando ({cena.Segundos:0.#}s, solta aos {cena.SegundosPreso:0.#}s)");

		// OS TRES DEGRAUS E O MACACO, pelo caminho que o PACOTE percorre -- e nao pelo `Transformacao.Rodar`
		// que o resto deste bloco chama a mao. Rodam aqui, no fim, porque os dois mexem no cabelo e no
		// corpo do boneco e o comeco da bancada mede exatamente isso.
		OsTresDegrausAoVivo(corpo, vis);
		AEscadaDoSsj3AoVivo(corpo, vis);
		OPiscarDuraACenaInteira(corpo, vis);
		// DEPOIS DELE, e nao antes: este monta uma cena que NAO existe no roteiro (as duas bandeiras
		// juntas) e a roda no mesmo corpo. Rodando primeiro, as seis passadas do bloco de cima
		// mediriam um boneco que acabou de atravessar a cena do SSJ3 inteira.
		OPiscarCedeAEscadaAoVivo(corpo, vis);
		OBanhoDeCorChegaNoCorpo(corpo, vis);
		AFeraForaDosDegraus(corpo, vis);
		OContornoDaFeraTrocandoDeCor(vis);
		OContornoMaisFracoEPulsando(vis);
		AChegadaNaZona(corpo, vis);
		// DEPOIS DELA E ANTES DO VARREDOR DE APARENCIA: este bloco veste o SSJ3 no boneco de proposito
		// (a cena da furia tem que deixar o penteado em paz) e o `AAparenciaInteiraDoDegrau` logo abaixo
		// devolve o corpo a base de todo jeito, entao ele nao herda nada daqui.
		ACenaDaFuriaAoVivo(corpo, vis);
		// POR ULTIMO DOS QUE MEXEM NO BONECO: ele veste as 33 formas uma a uma pra provar que a base
		// as desfaz todas, e termina devolvendo o corpo a base. Rodar antes deixaria os blocos de
		// cima medindo um boneco que ele acabou de trocar 66 vezes.
		AAparenciaInteiraDoDegrau(corpo, vis);

		// --- deixa o corpo ACESO pra a foto ---
		FormaDef pose = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		var corPose = new Color(pose.Aura);
		raios.Definir(true, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(pose)), pose.Raios);
		vis.AuraDaForma(new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(pose)),
						0.35f + pose.Intensidade * 0.13f, ContornoAlterna(pose));
		aura.Acender(corPose, 0.8f + pose.Intensidade * 0.5f);
		// SO A AURA PRA A FOTO: sem raios nem contorno. Judgar alinhamento com tres efeitos ligados
		// e o que me fez errar o offset duas vezes -- nao dava pra ver onde acabava um e comecava o
		// outro.
		raios.Definir(false, corPose, 0);
		vis.AuraDaForma(corPose, 0, null);
		_passos.Add("  --     SO a aura acesa, pra medir o alinhamento");

		// QUEM MAIS ESTA DESENHANDO? A foto mostrou DOIS blocos de aura, nao um torto -- entao a
		// pergunta certa nao e "qual o offset" e sim "quantos desenhos existem".
		foreach (Node n in corpo.GetChildren())
		{
			string extra = n is Node2D n2 ? $" pos={n2.Position} z={n2.ZIndex} vis={n2.Visible}" : "";
			_passos.Add($"  --     filho: {n.Name} ({n.GetType().Name}){extra}");
			foreach (Node c in n.GetChildren())
				if (c is Node2D c2 && c2.Visible)
					_passos.Add($"  --         > {c.Name} ({c.GetType().Name}) pos={c2.Position} "
							  + $"z={c2.ZIndex} escala={c2.Scale} mod={c2.Modulate}");
		}
	}

	// =====================================================================
	// 3a-bis. A GROSSURA DOS RAIOS, EM PIXEL DE MUNDO -- E A VARIACAO DELA
	// =====================================================================
	/// <summary>
	/// O `ruido()` DO `RaioDaForma.gdshader`, LETRA POR LETRA -- e escrito aqui de proposito.
	///
	/// ============================ POR QUE REESCREVER EM VEZ DE PERGUNTAR ============================
	/// Nao ha a quem perguntar: este hash roda no FRAGMENT, uma vez por pixel, dentro da GPU. Nao existe
	/// propriedade em node nenhum que devolva "a grossura que o raio numero 3 saiu". Medir isso na tela
	/// exigiria fotografar a faisca e recortar a luz dela -- que e como o relatorio anterior mediu, na
	/// mao, com dez fotos e duas rodadas de bandeira trocada, e que nenhuma bancada refaz sozinha.
	///
	/// Entao a bancada REAVALIA a conta do fragment. Isso tem um teto que fica dito: ela mede a FORMULA
	/// com os valores que o shader compilado entrega, e nao o pixel rasterizado -- se a placa desenhar
	/// outra coisa, esta linha nao ve. O que ela ve, e nenhuma outra linha desta casa vê, e a
	/// DISTRIBUICAO: media, faixa e desvio de milhares de raios, que e a unica forma do pedido do dono
	/// ("afinar a media SEM achatar a variacao") que uma foto de um raio nao responde.
	/// ============================================================================================
	/// </summary>
	private static double RuidoDoShader(double x, double y)
	{
		// `fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453)` -- em float64. A GPU faz em float32 e o
		// resultado numerico difere; o que se mede aqui e a DISTRIBUICAO do sorteio, e ela e a mesma nas
		// duas precisoes (um hash de seno espalha uniforme em [0,1) em qualquer largura de mantissa).
		double v = Math.Sin(x * 127.1 + y * 311.7) * 43758.5453;
		return v - Math.Floor(v);
	}

	/// <summary>
	/// A GROSSURA DE CADA RAIO EM PIXEL DE MUNDO, sobre uma nuvem de posicoes de emissao.
	///
	/// A conta e a do shader mais a do emissor, e as DUAS entram porque as duas variam por particula:
	///   * `g = grossura x afinar x (1 + variacao x (sorteio x 2 - 1))` -- fracao de UV, o sorteio novo;
	///   * a ESCALA da particula multiplica o quad inteiro -- a variacao que ja existia antes.
	/// A largura do nucleo na tela e `2 x g x largura do quad x escala`.
	///
	/// A SEMENTE E A POSICAO NO MUNDO, `floor`ada (`MODEL_MATRIX[3].xy` no vertex do shader), entao a
	/// amostra e uma grade de posicoes inteiras do tamanho da caixa de emissao -- e nao um
	/// `Random.NextDouble()`, que mediria o gerador do C# em vez do hash que a GPU roda.
	/// </summary>
	private static double[] GrossurasEmPixel(float baseG, float afinar, float variacao,
											 Vector2 escala, float larguraDoQuad)
	{
		var saida = new List<double>(64 * 64);
		for (int sx = -32; sx < 32; sx++)
			for (int sy = -32; sy < 32; sy++)
			{
				double s = RuidoDoShader(sx + 59.0, sy + 5.0);
				double g = baseG * afinar * (1.0 + variacao * (s * 2.0 - 1.0));

				// A ESCALA E SORTEADA A PARTE pelo `ParticleProcessMaterial` (uniforme entre min e max),
				// e ela e independente do hash de posicao -- entao aqui ela e varrida em degraus em vez
				// de sorteada: o produto de duas variaveis independentes se mede pelo produto cartesiano,
				// e assim a amostra nao depende de semente nenhuma do C#.
				for (int e = 0; e <= 8; e++)
				{
					double esc = escala.X + (escala.Y - escala.X) * (e / 8.0);
					saida.Add(2.0 * g * larguraDoQuad * esc);
				}
			}

		saida.Sort();
		return [.. saida];
	}

	/// <summary>Media, decil de baixo, decil de cima, maior e desvio-padrao de uma amostra ORDENADA.</summary>
	private static (double Media, double P10, double P90, double Maior, double Desvio) Resumo(double[] v)
	{
		double soma = 0;
		foreach (double x in v) soma += x;
		double media = soma / v.Length;

		double s2 = 0;
		foreach (double x in v) s2 += (x - media) * (x - media);

		return (media, v[v.Length / 10], v[v.Length * 9 / 10], v[^1], Math.Sqrt(s2 / v.Length));
	}

	/// <summary>
	/// AS TRES PERGUNTAS DO PEDIDO, e elas sao uma funcao pra poderem ser exercitadas AO CONTRARIO.
	///
	/// O dono pediu duas coisas numa frase -- *"uns sao mt coisa, poderia afinar a MEDIA dos
	/// raiozinhos"* --, e a segunda metade e a que um conserto futuro apaga sem perceber: achatar todo
	/// raio num valor so DERRUBA A MEDIA e passaria em qualquer linha que so olhasse a media. Por isso
	/// sao tres:
	///
	///   * A MEDIA CAIU  -- contra a MESMA conta com os botoes neutros (`afinar 1`, `variacao 0`), que e
	///                      literalmente este shader antes da mudanca. Nao contra um numero cravado: um
	///                      numero cravado envelhece na primeira vez que a grossura base mudar.
	///   * A FAIXA SOBREVIVEU -- e ela e cobrada de DOIS jeitos, porque um so tem buraco:
	///                      (a) o desvio relativo do SORTEIO POR RAIO nao pode ser zero -- e o unico
	///                          numero que enxerga "achataram tudo", ja que a escala da particula
	///                          continuaria espalhando sozinha e mascarando o achatamento;
	///                      (b) a faixa p90/p10 do conjunto tem que ser MAIOR que a de antes -- se o
	///                          eixo novo desaparecer, ela volta a ser exatamente a de antes.
	///   * O TOPO NAO VOLTA -- `afinar x (1 + variacao) &lt; 1`: o raio mais gordo que este sorteio
	///                      consegue produzir tem que ser mais fino que o gordo de antes. Sem esta,
	///                      subir a variacao devolveria pela cauda o exagero que o multiplicador tirou
	///                      da media, e as duas de cima continuariam verdes.
	/// </summary>
	private static (bool Ok, string Laudo) AGrossuraAfinouSemAchatar(
		(double Media, double P10, double P90, double Maior, double Desvio) hoje,
		(double Media, double P10, double P90, double Maior, double Desvio) antes,
		float afinar, float variacao)
	{
		bool caiu = hoje.Media <= antes.Media * 0.80;
		// O DESVIO DO SORTEIO POR RAIO, isolado da escala: `variacao` uniforme em [-1,1] da desvio
		// relativo `variacao / raiz(3)`. Cobrar 0,08 e cobrar que o eixo EXISTA (hoje ele da 0,26) sem
		// cravar o 0,45 de hoje -- o dono pode afinar o botao pra baixo sem a bancada reclamar.
		bool espalha = variacao / Math.Sqrt(3.0) >= 0.08;
		bool faixa = hoje.P10 > 0 && antes.P10 > 0
				  && hoje.P90 / hoje.P10 >= antes.P90 / antes.P10 * 1.15;
		bool topo = afinar * (1f + variacao) < 1f;

		return (caiu && espalha && faixa && topo,
				$"media {hoje.Media:0.00} px contra {antes.Media:0.00} de antes [caiu {caiu}]  |  "
			  + $"faixa p10-p90 {hoje.P10:0.00}-{hoje.P90:0.00} px, razao {hoje.P90 / hoje.P10:0.00} "
			  + $"contra {antes.P90 / antes.P10:0.00} [sobreviveu {faixa}]  |  desvio do sorteio por raio "
			  + $"{variacao / Math.Sqrt(3.0):0.000} [espalha {espalha}]  |  maior {hoje.Maior:0.00} px "
			  + $"contra {antes.Maior:0.00} [topo nao volta {topo}]");
	}

	/// <inheritdoc cref="AGrossuraAfinouSemAchatar"/>
	private void AGrossuraEmPixel(RaiosDaForma raios, Shader? shRaio)
	{
		if (shRaio == null) { Conferir(false, "o shader do raio carregou (sem ele nao ha o que medir)"); return; }

		// ============================ OS DOIS BOTOES SAIEM DO COMPILADO, E NAO DO TEXTO ============================
		// `RenderingServer.ShaderGetParameterDefault` devolve o padrao que o servidor de renderizacao
		// guardou depois de COMPILAR -- e a mesma distincao do bloco `Shaders()`: um `.gdshader` que o
		// Godot recusou continua com as palavras certas no `Code` e nao entrega padrao nenhum.
		//
		// E o `RaiosDaForma` NAO escreve estes dois (a linha `BotoesDaGrossuraIntactosDeTeste`, logo
		// acima, e quem cobra isso), entao o padrao do arquivo E o valor que desenha.
		// ======================================================================================================
		Rid rid = shRaio.GetRid();
		float afinar = RenderingServer.ShaderGetParameterDefault(rid, "afinar").AsSingle();
		float variacao = RenderingServer.ShaderGetParameterDefault(rid, "variacao_grossura").AsSingle();
		Conferir(afinar > 0f,
				 $"os botoes da grossura vem do shader COMPILADO (afinar={afinar:0.##}, "
			   + $"variacao={variacao:0.##}) -- zero aqui quer dizer uniform sumido, e nao botao no minimo");

		// A INTENSIDADE 2 e a que o dono reclamou (SSJ3, Limit Breaker, primal_legendary), e e a que o
		// relatorio anterior fotografou. O node ja esta nela quando este bloco roda.
		float baseG = raios.GrossuraBaseDeTeste;
		Vector2 escala = raios.EscalaDaParticulaDeTeste;
		float larg = RaiosDaForma.LarguraDoQuadDeTeste;

		var hoje = Resumo(GrossurasEmPixel(baseG, afinar, variacao, escala, larg));
		// O "ANTES" NAO E UM NUMERO GUARDADO: e a mesma funcao com os botoes NEUTROS, que e este shader
		// no dia anterior a mudanca. Guardar "1,70 px" aqui faria a comparacao envelhecer sozinha na
		// primeira vez que a grossura base ou o tamanho do quad mudassem -- e a bancada acusaria
		// regressao numa arte que ninguem mexeu.
		var antes = Resumo(GrossurasEmPixel(baseG, 1f, 0f, escala, larg));

		_passos.Add($"  --     grossura do NUCLEO em px de mundo (corpo = 32 px), intensidade "
					+ $"{raios.IntensidadeDeTeste}: hoje media {hoje.Media:0.00} · p10 {hoje.P10:0.00} · "
					+ $"p90 {hoje.P90:0.00} · maior {hoje.Maior:0.00} · desvio {hoje.Desvio:0.00}");
		_passos.Add($"  --     com os botoes NEUTROS (o shader de antes): media {antes.Media:0.00} · "
					+ $"p10 {antes.P10:0.00} · p90 {antes.P90:0.00} · maior {antes.Maior:0.00}");

		(bool ok, string laudo) = AGrossuraAfinouSemAchatar(hoje, antes, afinar, variacao);
		Conferir(ok, "a media da grossura CAIU e a variacao SOBREVIVEU: " + laudo);

		// ============================ E O JULGAMENTO ENXERGA? OS TRES DEFEITOS INJETADOS ============================
		// Sem estas tres linhas a de cima seria verde vazio -- e os tres casos sao os tres jeitos
		// REAIS de esta arte regredir, nao invencoes:
		//   * alguem devolve `afinar` pra 1 achando que "afinou demais"  -> a media volta;
		//   * alguem escreve a grossura pronta no C# / zera a variacao    -> todo raio sai igual;
		//   * alguem sobe a variacao pra "ficar mais variado"             -> a cauda gorda volta.
		// O terceiro e o mais insidioso: ele mantem a media e passa nas duas primeiras perguntas.
		// ======================================================================================================
		_passos.Add("  --     agora os DEFEITOS INJETADOS: as tres linhas abaixo tem que dizer que reprovaram");

		var semAfinar = Resumo(GrossurasEmPixel(baseG, 1f, variacao, escala, larg));
		Conferir(!AGrossuraAfinouSemAchatar(semAfinar, antes, 1f, variacao).Ok,
				 "[injetado] `afinar = 1` (a grossura de antes, com sorteio) reprova pela MEDIA -- "
			   + AGrossuraAfinouSemAchatar(semAfinar, antes, 1f, variacao).Laudo);

		var achatado = Resumo(GrossurasEmPixel(baseG, afinar, 0f, escala, larg));
		Conferir(!AGrossuraAfinouSemAchatar(achatado, antes, afinar, 0f).Ok,
				 "[injetado] `variacao = 0` (todo raio com a MESMA grossura) reprova mesmo com a media "
			   + "la embaixo -- e este e o defeito que uma linha so de media nunca pegaria: "
			   + AGrossuraAfinouSemAchatar(achatado, antes, afinar, 0f).Laudo);

		var caudaGorda = Resumo(GrossurasEmPixel(baseG, afinar, 0.9f, escala, larg));
		Conferir(!AGrossuraAfinouSemAchatar(caudaGorda, antes, afinar, 0.9f).Ok,
				 "[injetado] `variacao = 0,9` devolve o raio 'mt coisa' pela CAUDA (a media continua "
			   + $"caida, o maior sobe pra {caudaGorda.Maior:0.00} px) e reprova pelo topo");

		// ============================ E A MEDIDA ENXERGA A FORMA, E NAO SO OS BOTOES? ============================
		// As linhas de cima giram os botoes e comparam. Todas passariam se `GrossurasEmPixel` devolvesse
		// uma constante vezes os argumentos -- ou seja, se o hash nao sorteasse nada. Esta cobra que o
		// sorteio POR POSICAO produza valores distintos de verdade: 4096 posicoes de emissao, e a caixa
		// tem 14x22 px, entao ha posicao repetida no jogo -- mas nao 4096 vezes o mesmo numero.
		// ====================================================================================================
		var distintos = new HashSet<long>();
		for (int sx = -32; sx < 32; sx++)
			for (int sy = -32; sy < 32; sy++)
				distintos.Add((long)(RuidoDoShader(sx + 59.0, sy + 5.0) * 1e6));
		Conferir(distintos.Count > 3500,
				 $"o sorteio por particula sorteia MESMO ({distintos.Count} valores distintos em 4096 "
			   + "posicoes de emissao) -- um hash preso devolveria um punhado e a variacao seria enfeite");
	}

	// =====================================================================
	// 3a-ter. OS TRES DEGRAUS, PELO CAMINHO DO PACOTE
	// =====================================================================
	/// <summary>
	/// ESTREIA / ENCURTADA / INSTANTANEA -- medidos em `World.AoMudarForma`, e nao em volta dele.
	///
	/// ============================ POR QUE ISTO NAO PODIA SER SO DADO ============================
	/// O bloco `AsTresDuracoes` mede a DERIVACAO: que `Degrau` devolve o degrau certo e que `NoDegrau`
	/// devolve a cena certa pra cada degrau. Tudo verdade, e tudo inutil se o `World` nao perguntar. E
	/// ele ja nao perguntou uma vez: aquele ponto era um `if (primeira)` e o degrau do meio -- que e o
	/// que o jogador ve na maior parte da vida do personagem -- simplesmente nao existia.
	///
	/// Um `if (primeira)` reposto ali passa em TODAS as checagens de dado desta bancada. Este bloco e o
	/// unico lugar em que ele cai.
	///
	/// ============================ E O DEGRAU VEM DE `Degrau`, NAO DE UM LITERAL ============================
	/// A bancada nao escreve `DegrauDeCena.Curta` e manda: ela pergunta a `Cinematicas.Degrau` -- a
	/// MESMA funcao que o servidor consulta no `Entrar()` -- e manda o que ela responder. Assim a
	/// checagem cobre a corrente inteira (quem decide -> o que viaja -> o que roda) em vez de so o
	/// ultimo elo. Se o limiar de 50% mudar de lugar, e daqui que sai o aviso.
	/// ====================================================================================================
	///
	/// O QUE FICA DE FORA, DITO: o decodificador do byte (`byte` -> `DegrauDeCena`) mora no `switch` do
	/// `GameClient` e so roda com pacote de verdade no fio. Um valor desconhecido cair em `Nenhuma` em
	/// vez de `Estreia` continua sem bancada.
	/// </summary>
	private void OsTresDegrausAoVivo(Node2D corpo, CharacterVisual vis)
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo)
		{
			Conferir(false, "achei o `World` (sem ele nao da pra medir o caminho do pacote)");
			return;
		}
		int eu = GameClient.Instance?.LocalId ?? 0;
		Conferir(eu != 0, $"a bancada tem id de jogador ({eu}) -- `Corpo(id)` devolve nulo sem ele");
		if (eu == 0) return;

		// O SSJ3 porque ele e o extremo: 32 s de estreia contra 5 s de encurtada. Num degrau em que os
		// dois numeros fossem parecidos, uma troca de cena passaria despercebida na medicao.
		FormaDef alvo = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		FormaDef raiz = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdBase)!;
		Node atores = corpo.GetParent();
		var cheia = Jandirus.Core.Forms.Cinematicas.Para(alvo)!;
		var encurtada = Jandirus.Core.Forms.Cinematicas.Encurtada(cheia);

		List<Transformacao> Cenas()
		{
			var l = new List<Transformacao>();
			foreach (Node n in atores.GetChildren()) if (n is Transformacao tr) l.Add(tr);
			return l;
		}

		// LINHA DE BASE: o corpo volta pra base pelo MESMO metodo, e o penteado normal e o que se
		// compara depois. Sem isto o `cabeloBase` seria o que a checagem anterior deixou no boneco.
		mundo.AoMudarForma(eu, alvo.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		string cabeloBase = vis.CabeloDeTeste;
		Conferir(cabeloBase.Length > 0, $"o boneco tem penteado pra a medicao valer ({cabeloBase.GetFile()})");

		void UmDegrau(bool estreia, double maestria, Jandirus.Core.Forms.DegrauDeCena esperado,
					  Jandirus.Core.Forms.Cinematica? roteiro, string rotulo)
		{
			var degrau = Jandirus.Core.Forms.Cinematicas.Degrau(alvo, estreia, maestria);
			Conferir(degrau == esperado, $"{rotulo}: quem decide devolve `{esperado}` (devolveu `{degrau}`)");

			List<Transformacao> antes = Cenas();
			int presos = Transformacao.PresosDeTeste;
			vis.CabeloDaForma("");                       // zera o penteado pra a medicao abaixo valer

			mundo.AoMudarForma(eu, raiz.IdRede, alvo.IdRede, degrau);

			Transformacao? nova = null;
			foreach (Transformacao tr in Cenas()) if (!antes.Contains(tr)) nova = tr;

			if (roteiro == null)
			{
				// ============================ INSTANTANEA E A AUSENCIA DE CENA ============================
				// O degrau mais importante dos tres e o unico que nao tem NADA na tela -- e por isso o
				// unico cujo defeito le como ritmo do jogo. As tres linhas dizem o que "instantanea"
				// quer dizer: nao nasce cena, o corpo nao e preso, e a forma ja esta no personagem.
				//
				// A TERCEIRA E A QUE IMPORTA. "Nao nasceu cena" tambem seria verdade se o `AoMudarForma`
				// saisse mais cedo sem fazer nada -- e ai o jogador com 50% de maestria apertaria C e
				// nao aconteceria coisa nenhuma. Perguntar pelo cabelo NO MESMO QUADRO e o que separa
				// "transformou na hora" de "nao transformou".
				// ====================================================================================
				Conferir(nova == null, $"{rotulo}: nao nasce cena nenhuma");
				Conferir(Transformacao.PresosDeTeste == presos,
						 $"{rotulo}: NAO prende o corpo (donos {Transformacao.PresosDeTeste}, base {presos})");
				Conferir(vis.CabeloDeTeste != cabeloBase && vis.CabeloDeTeste.Length > 0,
						 $"{rotulo}: a forma ja esta no corpo no MESMO quadro ({vis.CabeloDeTeste.GetFile()})");
			}
			else
			{
				Conferir(nova != null, $"{rotulo}: nasce a cena");
				if (nova == null) return;
				nova.SetProcess(false);              // o relogio e nosso, como no resto desta bancada

				Conferir(ReferenceEquals(nova.CenaDeTeste, roteiro),
						 $"{rotulo}: e o roteiro e o certo (prende {nova.CenaDeTeste.SegundosPreso:0.##}s, "
					   + $"esperado {roteiro.SegundosPreso:0.##}s)");
				Conferir(Transformacao.PresosDeTeste == presos + 1,
						 $"{rotulo}: prende o corpo (donos {Transformacao.PresosDeTeste}, base {presos})");

				// E O CABELO ESPERA O BEAT. "Quando ha cinematica, ela manda" -- aplicar a forma aqui
				// deixaria o personagem ja transformado assistindo a propria transformacao, e o piscar
				// de cabelo do SSJ1 nao teria pra onde piscar.
				Conferir(vis.CabeloDeTeste == cabeloBase,
						 $"{rotulo}: e o cabelo NAO troca ainda -- quem troca e o beat `Assumir`");

				atores.RemoveChild(nova);            // `_ExitTree` solta a tranca na hora
				nova.QueueFree();
				Conferir(Transformacao.PresosDeTeste == presos,
						 $"{rotulo}: e matar a cena devolve a vez do corpo");
			}
		}

		UmDegrau(true, 0, Jandirus.Core.Forms.DegrauDeCena.Estreia, cheia, "estreia");
		UmDegrau(false, 0, Jandirus.Core.Forms.DegrauDeCena.Curta, encurtada, "sem maestria");
		UmDegrau(false, Jandirus.Core.Forms.Cinematicas.MaestriaQueDispensaCena - 0.1,
				 Jandirus.Core.Forms.DegrauDeCena.Curta, encurtada, "quase dominada (49,9%)");
		UmDegrau(false, 100, Jandirus.Core.Forms.DegrauDeCena.Nenhuma, null, "dominada (100%)");

		// A ENCURTADA E MENOR, medida nos objetos que O JOGO instanciou -- e nao numa conta refeita
		// aqui. O `<` e a regra inteira dos tres degraus: uma curta que prendesse o mesmo tanto seria
		// a estreia de novo, sem musica.
		Conferir(encurtada.SegundosPreso < cheia.SegundosPreso,
				 $"a cena do degrau do meio prende MENOS ({encurtada.SegundosPreso:0.##}s contra "
			   + $"{cheia.SegundosPreso:0.##}s)");

		// --- devolve o corpo pra a base pelo mesmo caminho ---
		mundo.AoMudarForma(eu, alvo.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(vis.CabeloDeTeste == cabeloBase,
				 $"voltar pra base pelo mesmo caminho devolve o penteado ({vis.CabeloDeTeste.GetFile()})");
		_passos.Add($"  --     tres degraus ao vivo: estreia prende {cheia.SegundosPreso:0.#}s, "
				  + $"curta {encurtada.SegundosPreso:0.#}s, dominada 0s");
	}

	// =====================================================================
	// 3a-ter. A ESCADA DO SSJ3 AO VIVO -- VESTE, E NAO CONCEDE
	// =====================================================================
	/// <summary>
	/// A CENA DO SSJ3 PONDO O SSJ1 E O SSJ2 NO CORPO -- e NAO no personagem.
	///
	/// ============================ AS DUAS METADES, E POR QUE ELAS PRECISAM UMA DA OUTRA ============================
	/// **Vestir** e **conceder** produzem a MESMA tela: um boneco com cabelo de Super Saiyajin. A
	/// diferenca inteira mora em dois lugares que ninguem olha -- o `_formaDaZona` do cliente e o
	/// `EstadoDeForma` do servidor. Entao uma bancada que so confirmasse "o cabelo dourou aos 7,0 s"
	/// daria verde pra a implementacao errada (mandar o degrau pelo `AoMudarForma`, que e a
	/// simplificacao tentadora: uma linha em vez de duas funcoes), e essa implementacao errada
	/// DESPERTARIA o SSJ1 em quem esta assistindo a propria estreia de SSJ3.
	///
	/// Por isso as duas metades correm no MESMO passeio, na mesma cena, no mesmo corpo:
	///   1. o boneco veste base -> SSJ1 -> SSJ2 nos tres beats, e SSJ3 so no `Assumir`;
	///   2. e nada disso chega ao servidor: `Atual`, `Liberadas`, `EstreiaVista` e as maestrias dos
	///      tres degraus terminam byte a byte como comecaram.
	///
	/// ============================ E A SONDA E TESTADA CONTRA ELA MESMA ============================
	/// "Nada mudou no servidor" e a afirmacao mais facil de fazer por acidente: uma sonda que lesse o
	/// jogador errado, ou um `FormaDeTeste` que devolvesse nulo, diriam exatamente a mesma coisa. Por
	/// isso, no fim, a bancada CONCEDE o SSJ1 na marra e exige que a comparacao acuse -- e so entao
	/// desfaz. Sem esse contra-teste o bloco inteiro seria decorativo.
	///
	/// ============================ A REFERENCIA DE "COMO O SSJ1 SE PARECE" ============================
	/// Nao ha lista de arquivos escrita aqui: antes da cena o proprio boneco e vestido com o sufixo de
	/// cada degrau e o penteado resultante e guardado. O que se afirma depois e que a CENA chega no
	/// mesmo penteado -- ou seja, a regra sob teste e o pareamento beat->degrau, e nao o resolvedor de
	/// sprite (que tem bancada propria). E a linha dos "penteados distintos" e o que impede isto de
	/// virar enfeite: num corpo cujo cabelo nao tenha variante, os tres alvos seriam o mesmo arquivo e
	/// as tres comparacoes passariam sem ver nada.
	///
	/// COMO REPROVA SE A REGRA SUMIR:
	///   * troque o `VestirODegrauSeguinte()` do `Transformacao` por um `return` -- os tres penteados
	///     medidos ficam todos no base, e as linhas do `ssj1` e do `ssj2` caem com o arquivo ao lado;
	///   * troque-o por `mundo.AoMudarForma(eu, ..., escada[n].IdRede, Nenhuma)` (a simplificacao) --
	///     o cabelo continua certo e caem as linhas do SERVIDOR... nao: caem as do CLIENTE, porque o
	///     `AoMudarForma` reescreve o `_formaDaZona` e nasce uma cena por degrau. O servidor so cai se
	///     alguem plugar o degrau num pacote C2S, que e o outro jeito de escrever a mesma simplificacao;
	///   * faca o beat `Assumir` parar de vestir e o SSJ3 nunca chega no corpo: a ultima linha cai com
	///     o penteado do SSJ2 ainda vestido -- que e o tombo do `ussj_saved_icon` que o DM documenta.
	/// ==========================================================================================================
	/// </summary>
	private void AEscadaDoSsj3AoVivo(Node2D corpo, CharacterVisual vis)
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo) return;   // ja acusado acima
		int eu = GameClient.Instance?.LocalId ?? 0;
		if (eu == 0) return;

		FormaDef alvo = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		FormaDef raiz = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdBase)!;
		FormaDef[] escada = Jandirus.Core.Forms.Cinematicas.EscadaDaCena(alvo);
		if (Jandirus.Core.Forms.Cinematicas.NoDegrau(alvo, Jandirus.Core.Forms.DegrauDeCena.Estreia)
			is not { } cheia) return;

		double[] instantes = cheia.Beats
			.Where(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.VesteDegrau))
			.Select(b => b.Em).ToArray();
		if (instantes.Length != escada.Length) return;   // ja acusado no `AEscadaDoSsj3NoRoteiro`

		// A QUARTA TROCA E A DA PROPRIA FORMA, e ela vem do beat `Assumir` -- lida do roteiro e nao
		// escrita aqui, porque o instante dela ja mudou quatro vezes neste projeto (20 -> 32 -> 116,7 -> 140 s).
		double instantesAssumir = cheia.Beats
			.First(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir)).Em;

		Node atores = corpo.GetParent();
		List<Transformacao> Cenas()
		{
			var l = new List<Transformacao>();
			foreach (Node n in atores.GetChildren()) if (n is Transformacao tr) l.Add(tr);
			return l;
		}

		// --- 0. COMO CADA DEGRAU SE PARECE NESTE CORPO, medido antes de a cena existir ---
		mundo.AoMudarForma(eu, alvo.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		var referencia = new string[escada.Length];
		for (int i = 0; i < escada.Length; i++)
		{
			vis.CabeloDaForma(escada[i].SufixoDoCabelo);
			referencia[i] = vis.CabeloDeTeste;
		}
		vis.CabeloDaForma(alvo.SufixoDoCabelo);
		string cabeloDoSsj3 = vis.CabeloDeTeste;
		vis.CabeloDaForma("");
		string cabeloBase = vis.CabeloDeTeste;

		// ============================ E A FOLHA DA CHAMA DE CADA DEGRAU, PELA MESMA RECEITA ============================
		// A faisca nao foi o unico canal que o `Assumir` escrevia sozinho: a folha da AURA e a da CARGA
		// tambem. O sintoma delas e mais calado que o dos raios -- so aparece pra quem segurar o C no
		// meio da cena --, e por isso ele merece medicao mais teimosa e nao menos.
		//
		// A REFERENCIA E TIRADA PELO PROPRIO `Aura.Folha`, alimentado pelo `Catalogo.Folha`: recopiar
		// aqui o `switch` que traduz `FolhaDeAura` em caminho de arquivo criaria a segunda tabela, e a
		// bancada passaria a conferir a tabela dela contra ela mesma.
		// ==========================================================================================================
		var auraDoCorpo = corpo.GetNodeOrNull<Aura>("Aura");
		var cargaDoCorpo = corpo.GetNodeOrNull<CargaVisual>("Carga");
		var folhaDeCada = new string[escada.Length + 1];
		if (auraDoCorpo != null)
			for (int i = 0; i <= escada.Length; i++)
			{
				auraDoCorpo.Folha(Jandirus.Core.Forms.Catalogo.Folha(i < escada.Length ? escada[i] : alvo));
				folhaDeCada[i] = auraDoCorpo.DesenhoDeTeste.FolhaDeTeste;
			}

		Conferir(auraDoCorpo != null && cargaDoCorpo != null
				 && folhaDeCada.Distinct().Count() >= 2,
				 "a base e os degraus desenham a chama em FOLHAS diferentes -- senao a medicao dela e cega ("
			   + string.Join(", ", folhaDeCada.Select(f => f?.GetFile() ?? "?").Distinct()) + ")");

		Conferir(vis.TemCabeloDeTeste && cabeloBase.Length > 0,
				 $"o boneco tem penteado pra a escada ser vista ({cabeloBase.GetFile()})");
		Conferir(referencia[0] == cabeloBase,
				 "o degrau base E o penteado normal (o `RemoveHair()` de SSJ3Cinematic.dm:12)");
		Conferir(referencia.Append(cabeloDoSsj3).Distinct().Count() == escada.Length + 1,
				 "os quatro penteados da escada sao ARQUIVOS DIFERENTES -- senao a medicao e cega ("
			   + string.Join(", ", referencia.Append(cabeloDoSsj3).Select(s => s.GetFile())) + ")");

		// --- 1. O QUE O SERVIDOR SABE, ANTES ---
		Jandirus.Core.Forms.EstadoDeForma? est = Jandirus.Server.GameServer.Instance?.FormaDeTeste(eu);
		Conferir(est != null, "a bancada alcanca o estado de forma NO SERVIDOR (sem isso so da pra ver a tela)");

		string atualAntes = est?.Atual ?? "";
		int[] liberadasAntes = est?.Liberadas.ToArray() ?? [];
		int[] estreiaAntes = est?.EstreiaVista.ToArray() ?? [];
		double[] maestriaAntes = escada.Select(d => est?.Maestria.De(d.Id) ?? 0).ToArray();
		bool Intacto()
		{
			if (est == null) return false;
			if (est.Atual != atualAntes) return false;
			if (est.Liberadas.Count != liberadasAntes.Length || !liberadasAntes.All(est.Liberadas.Contains))
				return false;
			if (est.EstreiaVista.Count != estreiaAntes.Length || !estreiaAntes.All(est.EstreiaVista.Contains))
				return false;
			// A MAESTRIA TAMBEM: conceder nao e a unica forma de "dar" uma forma -- subir a maestria do
			// SSJ1 durante a cena do SSJ3 encurtaria a proxima estreia dele, que e a mesma perda por
			// outra porta (ver `Cinematicas.MaestriaQueDispensaCena`).
			for (int i = 0; i < escada.Length; i++)
				if (Math.Abs(est.Maestria.De(escada[i].Id) - maestriaAntes[i]) > 1e-9) return false;
			return true;
		}

		// ============================ 2. E A CENA COMECA EM SSJ2, QUE E O CASO DE VERDADE ============================
		// Subir de SSJ2 pra SSJ3 e o caminho normal -- ninguem estreia o SSJ3 vindo da base. E e
		// exatamente ai que o primeiro degrau da escada (o `RemoveHair()` de `SSJ3Cinematic.dm:12`)
		// deixa de ser enfeite: com cinematica o `World.AoMudarForma` NAO veste nada, entao o corpo
		// entra na cena de CABELO DOURADO e a fala "o que voce esta vendo agora e o meu estado normal"
		// sairia por cima de um Super Saiyajin 2.
		//
		// Comecar da base esconderia isso: o degrau base seria uma troca de nada por nada, o penteado
		// nao mudaria, e a checagem passaria verde com o primeiro beat inteiramente morto. Este e o
		// mesmo erro do "zero que nao pode ser vazio" que esta bancada ja cobra em outros pontos.
		//
		// O `Nenhuma` e o degrau de quem ja domina a forma: veste na hora, sem cena. E o jeito de
		// montar o estado inicial sem depender de mais uma cinematica.
		// ========================================================================================================
		mundo.AoMudarForma(eu, raiz.IdRede, Jandirus.Core.Forms.Catalogo.Def("ssj2")!.IdRede,
						   Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(vis.CabeloDeTeste == referencia[2],
				 $"o corpo entra na cena JA em SSJ2 ({vis.CabeloDeTeste.GetFile()}) -- "
			   + "e o degrau base da escada tem o que desfazer");

		// ============================ E ELE ENTRA COM A FAISCA LIGADA ============================
		// Esta linha e a metade que faltava do "o degrau base tem o que desfazer" logo acima: o cabelo
		// nao era a unica coisa vestida. O SSJ2 tem `Raios = 1`, entao quem sobe pra SSJ3 entra na cena
		// CREPITANDO -- e era exatamente isso que sobrevivia ao degrau base ("os efeitos dos raiozinhos
		// continuam", com o personagem de cabelo preto dizendo "este e o meu estado normal").
		//
		// Medir isto ANTES da cena e o que impede a checagem de baixo de virar enfeite: raio que nunca
		// acendeu tambem termina apagado no degrau base.
		var raiosDoCorpo = corpo.GetNodeOrNull<RaiosDaForma>("Raios");
		Conferir(raiosDoCorpo != null && raiosDoCorpo.VivosDeTeste > 0,
				 "e ele entra na cena com a FAISCA do SSJ2 ligada -- o degrau base tem o que apagar");

		// --- A CENA, pelo caminho do PACOTE (e nao pelo `Transformacao.Rodar` a mao) ---
		List<Transformacao> antesDaCena = Cenas();
		mundo.AoMudarForma(eu, Jandirus.Core.Forms.Catalogo.Def("ssj2")!.IdRede, alvo.IdRede,
						   Jandirus.Core.Forms.DegrauDeCena.Estreia);

		Transformacao? cena = null;
		foreach (Transformacao tr in Cenas()) if (!antesDaCena.Contains(tr)) cena = tr;
		Conferir(cena != null, "a estreia do SSJ3 nasce pra a escada rodar");
		if (cena == null) return;
		cena.SetProcess(false);   // o relogio e nosso, como no resto desta bancada

		// ============================ O PASSO E MENOR QUE A DISTANCIA ENTRE OS BEATS ============================
		// O tocador dispara por `Em <= _t`, entao dois beats dentro do mesmo passo disparariam JUNTOS e
		// o penteado lido seria o do segundo -- a medicao daria "o SSJ1 nunca foi vestido" com o codigo
		// perfeito. Os tres atos do DM caem em 0,8 / 5,8 / 10,0 s; 0,05 s tem folga de sobra, e o passo
		// e conferido contra a distancia REAL abaixo em vez de ser confiado.
		// ====================================================================================================
		const double Passo = 0.05;
		double menorVao = double.MaxValue;
		for (int i = 1; i < instantes.Length; i++) menorVao = Math.Min(menorVao, instantes[i] - instantes[i - 1]);
		Conferir(menorVao > Passo * 2,
				 $"os beats da escada estao mais longe que o passo da medicao ({menorVao:0.##}s > {Passo * 2:0.##}s)");

		// ============================ A TRAJETORIA INTEIRA, E NAO TRES AMOSTRAS ============================
		// A primeira versao disto lia o penteado NOS tres instantes e comparava. Passava -- e passaria
		// tambem com um tocador que vestisse os degraus em qualquer outro momento, ou que os vestisse
		// e desvestisse entre as amostras, ou que trocasse o cabelo a cada quadro. Tres pontos nao
		// descrevem uma curva.
		//
		// Aqui a cena e percorrida inteira e TODA troca de penteado e anotada com a hora. O que se
		// afirma depois e a lista: quatro trocas, nesta ordem, nestes instantes. Isso torna
		// inalcancavel toda a familia de defeitos acima -- uma troca a mais ou a menos aparece, e uma
		// troca na hora errada aparece com a hora.
		//
		// COMO REPROVA SE A REGRA SUMIR (o teste que eu NAO pude rodar como experimento, porque o
		// `Transformacao.cs` estava sendo editado por outro trabalho): esvazie o
		// `VestirODegrauSeguinte` -- a lista cai de quatro trocas pra UMA (so o `Assumir`, de SSJ2
		// direto pra SSJ3), e a primeira linha abaixo cai imprimindo a lista curta. Nao ha como um
		// `VesteDegrau` morto passar por aqui, porque os quatro penteados sao arquivos comprovadamente
		// diferentes (a linha "ARQUIVOS DIFERENTES" acima) e uma troca que nao acontece nao se anota.
		// ================================================================================================
		var trocas = new List<(double Em, string Cabelo)>();
		string ultimo = vis.CabeloDeTeste;
		bool servidorMexeuNoMeio = false, ssj3CedoDemais = false;

		// ============================ A FAISCA ANDA JUNTO COM O PENTEADO ============================
		// Mesma trajetoria, mesma varredura, e por isso as duas se comparam: em todo quadro em que o
		// corpo vestir um degrau SEM faisca (a base e o SSJ1, que tem `Raios = 0`) a faisca tem que
		// estar apagada, e nos degraus COM faisca (o SSJ2 e o SSJ3) acesa.
		//
		// Isto nao pode ser tres amostras nos beats: o defeito do dono era justamente o efeito
		// SOBREVIVENDO entre dois instantes -- ele nao acende na hora errada, ele nunca apaga. Um
		// quadro em que a faisca esta ligada com o degrau errado ja e o defeito inteiro.
		// ==========================================================================================
		string[] idDoPenteado = [.. escada.Select(x => x.Id), alvo.Id];
		string[] penteadoDeCada = [.. referencia, cabeloDoSsj3];
		int quadrosComFaiscaSobrando = 0, quadrosSemFaiscaDevida = 0;
		string ondeSobrou = "";

		// A FOLHA DA CHAMA NO MESMO PASSEIO -- ver o bloco da referencia la em cima.
		int quadrosComFolhaErrada = 0, quadrosComFolhaDaCargaErrada = 0;
		string ondeAFolhaErrou = "";

		// ============================ E A TERCEIRA CHAMA, A DA PROPRIA CENA ============================
		// O dono: *"vamos trocar das cinematicas o Aurabigcombined pela propria aura da transformaçao q vc
		// ta virando"*. O `AAparenciaInteiraDoDegrau` ja cobra isso nas 33 formas, mas por `VestirDeTeste`
		// -- ou seja com o roteiro parado. Aqui e AO VIVO, com os beats disparando: e a unica medida que
		// pega a folha certa vestida na HORA errada, que e a familia inteira de defeito deste bloco.
		//
		// A JANELA COMECA NO PRIMEIRO `VesteDegrau`, e pelo mesmo motivo da janela do corpo: ate ali a
		// chama da cena esta na folha da forma ALVO (escrita no `Montar`, ver `Transformacao.ChamaDoDegrau`)
		// e nao na de degrau nenhum -- o corpo ainda esta em SSJ2, que so por acaso cai na mesma folha do
		// SSJ3. Cobrar esses quadros seria cobrar um acerto por coincidencia.
		// ==========================================================================================
		int quadrosComFolhaDaCenaErrada = 0;
		string ondeACenaErrou = "";

		// ============================ E A CAMERA, NO MESMO PASSEIO ============================
		// "tremendo e parando" so se mede VARRENDO: a queixa e a camera PARADA entre dois beats, e
		// amostrar nos beats e justamente amostrar nos instantes em que ela nunca parou. O maior vao
		// da cena tem 9,2 s (de 10,0 a 19,2), e um beat de tremor dura `Forca/Queda` = 0,75 s.
		//
		// O relogio do tremor tambem e nosso: o `TickDoTremor` mora no `World._Process`, que NAO roda
		// dentro deste laco (a bancada inteira acontece dentro de um quadro so). Sem a linha abaixo o
		// `_tremor` nunca DESCERIA, e "a camera nao para de tremer" passaria verde com o rumor
		// deletado -- a medicao mais cega possivel, pelo caminho mais facil de nao perceber.
		// ======================================================================================
		mundo.TickDoTremorDeTeste(10.0);          // zera o que blocos anteriores tenham deixado no ar
		int quadrosComCameraParada = 0, quadrosDeCamera = 0, quadrosEmPico = 0;
		float menorTremor = float.MaxValue, maiorTremor = 0f;
		double primeiraParada = -1;

		// ============================ O PIOR SILENCIO DA CENA, MEDIDO E NAO ESTIMADO ============================
		// "o tremor nunca zera ENTRE o primeiro e o ultimo beat" e uma afirmacao sobre o pior VAO, e o
		// menor tremor da cena inteira nao a responde: o minimo global pode cair logo depois de um beat
		// e dizer nada sobre o buraco de nove segundos la no meio.
		//
		// Entao aqui o vao e reconstruido do jeito que o jogador o vive: conta-se quanto tempo passa
		// SEM NENHUM beat disparar (o `BeatsDeTeste` e quem avisa), guarda-se o maior desses trechos, e
		// o que se cobra depois e o menor tremor DENTRO dele. Um vao so entra na conta quando outro
		// beat o fecha -- e assim ele e mesmo "entre dois beats", e nao a cauda do fim da cena.
		// ====================================================================================================
		int beatsAntes = cena.BeatsDeTeste;
		bool jaTeveBeat = false;
		double silencioAtual = 0, maiorSilencio = 0;
		float menorNoSilencio = float.MaxValue, menorNoMaiorSilencio = float.MaxValue;

		for (double t = 0; t < cheia.Segundos + 1.0 && IsInstanceValid(cena); t += Passo)
		{
			cena._Process(Passo);
			if (!IsInstanceValid(cena)) break;

			bool disparouBeat = cena.BeatsDeTeste > beatsAntes;
			beatsAntes = cena.BeatsDeTeste;

			// SO ENQUANTO A CENA ESTA RODANDO: depois do ultimo beat o `_Process` retorna na guarda do
			// `_fim` e o tremor cai pra zero de proposito (e o fim macio). Contar esses quadros seria
			// cobrar rumor de uma cena que ja acabou.
			if (cena.FimDeTeste == Transformacao.FimDaCena.Rodando)
			{
				mundo.TickDoTremorDeTeste(Passo);
				float agora = mundo.TremorDeTeste;
				quadrosDeCamera++;
				menorTremor = Math.Min(menorTremor, agora);
				maiorTremor = Math.Max(maiorTremor, agora);
				if (agora > Jandirus.Core.Forms.Cinematicas.RumorDaCena + 0.01f) quadrosEmPico++;
				if (agora <= 0f)
				{
					quadrosComCameraParada++;
					if (primeiraParada < 0) primeiraParada = cena.TempoDeTeste;
				}

				// O beat FECHA o vao anterior e abre um novo. O primeiro vao da cena (antes de haver
				// qualquer beat) nao conta: ali nao ha "entre dois beats" nenhum ainda.
				if (disparouBeat)
				{
					if (jaTeveBeat && silencioAtual > maiorSilencio)
					{
						maiorSilencio = silencioAtual;
						menorNoMaiorSilencio = menorNoSilencio;
					}
					jaTeveBeat = true;
					silencioAtual = 0;
					menorNoSilencio = float.MaxValue;
				}
				else if (jaTeveBeat)
				{
					silencioAtual += Passo;
					menorNoSilencio = Math.Min(menorNoSilencio, agora);
				}
			}

			if (vis.CabeloDeTeste != ultimo)
			{
				ultimo = vis.CabeloDeTeste;
				trocas.Add((cena.TempoDeTeste, ultimo));
				// O SSJ3 NAO PODE APARECER ANTES DA HORA: quem assiste a propria estreia ja
				// transformado e o defeito que o `return` da cinematica no `AoMudarForma` evita.
				if (ultimo == cabeloDoSsj3 && cena.TempoDeTeste < instantesAssumir - Passo * 2)
					ssj3CedoDemais = true;
			}

			if (!Intacto()) servidorMexeuNoMeio = true;

			// QUEM ESTA VESTIDO AGORA, lido pelo PENTEADO -- o mesmo sinal que a trajetoria acima usa.
			// Perguntar ao `Transformacao` qual degrau ele acha que vestiu seria a bancada confirmando
			// a intencao do codigo em vez do resultado dele.
			int qual = Array.IndexOf(penteadoDeCada, vis.CabeloDeTeste);
			if (qual >= 0 && raiosDoCorpo != null)
			{
				bool deveTer = Jandirus.Core.Forms.Catalogo.Def(idDoPenteado[qual])!.Raios > 0;
				bool tem = raiosDoCorpo.VivosDeTeste > 0;

				if (tem && !deveTer)
				{
					quadrosComFaiscaSobrando++;
					if (ondeSobrou.Length == 0)
						ondeSobrou = $"`{idDoPenteado[qual]}` aos {cena.TempoDeTeste:0.##}s";
				}
				if (!tem && deveTer) quadrosSemFaiscaDevida++;
			}

			// E A CHAMA, PELO MESMO SINAL (o penteado diz que degrau esta vestido). Os dois desenhos
			// sao cobrados separado de proposito: sao dois nodes e duas linhas no `Vestir`, e esquecer
			// UMA delas foi exatamente o que aconteceu antes.
			if (qual >= 0 && auraDoCorpo != null && cargaDoCorpo != null)
			{
				if (auraDoCorpo.DesenhoDeTeste.FolhaDeTeste != folhaDeCada[qual])
				{
					quadrosComFolhaErrada++;
					if (ondeAFolhaErrou.Length == 0)
						ondeAFolhaErrou = $"`{idDoPenteado[qual]}` aos {cena.TempoDeTeste:0.##}s desenha "
										+ $"{auraDoCorpo.DesenhoDeTeste.FolhaDeTeste.GetFile()}, "
										+ $"esperado {folhaDeCada[qual].GetFile()}";
				}
				if (cargaDoCorpo.DesenhoDeTeste.FolhaDeTeste != folhaDeCada[qual])
					quadrosComFolhaDaCargaErrada++;

				if (cena.TempoDeTeste >= instantes[0]
					&& cena.ChamaDaCenaDeTeste.FolhaDeTeste != folhaDeCada[qual])
				{
					quadrosComFolhaDaCenaErrada++;
					if (ondeACenaErrou.Length == 0)
						ondeACenaErrou = $"`{idDoPenteado[qual]}` aos {cena.TempoDeTeste:0.##}s desenha "
									   + $"{cena.ChamaDaCenaDeTeste.FolhaDeTeste.GetFile()}, "
									   + $"esperado {folhaDeCada[qual].GetFile()}";
				}
			}
		}

		// --- 3. A TRAJETORIA: quatro trocas, nesta ordem, nestes instantes ---
		string[] esperados = [.. referencia, cabeloDoSsj3];
		double[] horas = [.. instantes, instantesAssumir];
		string desenho = trocas.Count == 0 ? "(nenhuma)"
					   : string.Join(" | ", trocas.Select(p => $"{p.Em:0.#}s {p.Cabelo.GetFile()}"));

		Conferir(trocas.Count == esperados.Length,
				 $"a cena troca o penteado {esperados.Length}x -- base, SSJ1, SSJ2 e a forma "
			   + $"({trocas.Count}: {desenho})");

		for (int i = 0; i < esperados.Length && i < trocas.Count; i++)
		{
			// O INSTANTE COM TOLERANCIA DE UM PASSO, e nao exato: o tocador dispara no primeiro quadro
			// em que `Em <= _t`, entao a troca cai sempre dentro de um passo DEPOIS do beat. Tolerar
			// mais que isso deixaria a fala e o degrau se descolarem sem ninguem ver.
			string nome = i < escada.Length ? escada[i].Id : alvo.Id;
			bool naHora = trocas[i].Em >= horas[i] && trocas[i].Em < horas[i] + Passo * 2;
			Conferir(trocas[i].Cabelo == esperados[i] && naHora,
					 $"aos {horas[i]:0.#}s o corpo passa a vestir `{nome}` "
				   + $"({trocas[i].Cabelo.GetFile()} aos {trocas[i].Em:0.##}s, "
				   + $"esperado {esperados[i].GetFile()})");
		}

		Conferir(!ssj3CedoDemais, "e o SSJ3 NAO aparece antes do beat que assume a forma");
		Conferir(vis.CabeloDeTeste == cabeloDoSsj3,
				 $"no fim da cena quem fica e o SSJ3 ({vis.CabeloDeTeste.GetFile()}) -- "
			   + "o degrau vestido no meio nao sobra (o tombo do `ussj_saved_icon`)");

		// ============================ E A FAISCA SEGUE O DEGRAU, QUADRO A QUADRO ============================
		// A QUEIXA DO DONO, medida: "se eu estiver no ssj2 e iniciar a cinematica do ssj3, ele faz tudo
		// certinho voltando pra base etc, porem os efeitos dos raiozinhos continuam". Com o `Vestir`
		// descrevendo so cabelo/corpo/tinta, esta linha caia com ~180 quadros de sobra -- toda a
		// travessia base -> SSJ1, um personagem de cabelo preto crepitando azul.
		//
		// A SEGUNDA LINHA E O CONTRA-TESTE DA PRIMEIRA: um `Vestir` que simplesmente apagasse a faisca
		// sempre zeraria a de cima e cairia aqui, porque o SSJ2 do meio da cena tem que voltar a
		// crepitar. Sozinhas, cada uma passa com a metade errada do conserto.
		//
		// MEDIDO: com a linha da faisca do `Vestir` desligada, esta checagem cai com
		// "184 quadro(s), o primeiro em `base` aos 0,8s" -- que e a queixa do dono em numeros.
		// ================================================================================================
		Conferir(quadrosComFaiscaSobrando == 0,
				 $"a faisca NAO sobrevive ao degrau sem faisca ({quadrosComFaiscaSobrando} quadro(s)"
			   + (ondeSobrou.Length > 0 ? $", o primeiro em {ondeSobrou}" : "") + ")");
		Conferir(quadrosSemFaiscaDevida == 0,
				 $"e ela VOLTA no degrau que tem faisca ({quadrosSemFaiscaDevida} quadro(s) sem ela)");

		// ============================ E A CHAMA DO DEGRAU, O CANAL CALADO ============================
		// Este e o irmao mudo da faisca: sem a folha no `Vestir`, segurar o C durante a fala "este e o
		// meu estado normal" desenhava a chama do Super Saiyajin num corpo em forma base. Nada pisca,
		// nada erra, nao ha aviso -- o jogador so ve o desenho errado se por acaso estiver carregando.
		//
		// SAO DUAS LINHAS PORQUE SAO DOIS NODES. O `Aura` e a `CargaVisual` desenham a MESMA chama e
		// cada um tem a propria folha; uma so das duas cobriria metade do defeito e daria verde na
		// outra metade -- que foi como esta familia inteira sobreviveu ate agora.
		//
		// MEDIDO (contraprova RODADA, nao prometida): com a linha `aura.Folha(...)` do `Vestir`
		// desligada, esta checagem cai com `101 quadro(s) errado(s) -- base aos 0,8s desenha
		// AuraSSjBig.tres, esperado colorablebigaura.tres`. Sao os cinco segundos em que o
		// personagem esta de cabelo preto dizendo "este e o meu estado normal" com a chama de Super
		// Saiyajin armada. A da carga cai com os mesmos 101.
		// ==========================================================================================
		Conferir(quadrosComFolhaErrada == 0,
				 $"a folha da AURA acompanha o degrau vestido ({quadrosComFolhaErrada} quadro(s) errado(s)"
			   + (ondeAFolhaErrou.Length > 0 ? $" -- {ondeAFolhaErrou}" : "") + ")");
		Conferir(quadrosComFolhaDaCargaErrada == 0,
				 $"e a da CARGA (a tecla C no meio da cena) tambem "
			   + $"({quadrosComFolhaDaCargaErrada} quadro(s) errado(s))");

		// E A TERCEIRA, a da propria cinematica -- ver o bloco do contador la em cima. Sao TRES desenhos
		// da mesma arte e cada um tem a propria folha; medir dois deixaria o terceiro livre pra mostrar a
		// aura do SSJ3 durante os degraus em que o corpo ainda e base ou SSJ1, que e a queixa literal.
		//
		// COMO REPROVA SE A REGRA SUMIR: tire o `ChamaDoDegrau(d)` do `Transformacao.Vestir` (ou devolva-o
		// pro `Assumir`, que e onde ele quase nasceu) e ela cai com toda a travessia base -> SSJ2.
		Conferir(quadrosComFolhaDaCenaErrada == 0,
				 $"e a chama DA CENA acompanha o degrau vestido, quadro a quadro "
			   + $"({quadrosComFolhaDaCenaErrada} quadro(s) errado(s)"
			   + (ondeACenaErrou.Length > 0 ? $" -- {ondeACenaErrou}" : "") + ")");

		// ============================ E A CAMERA NAO PARA -- "tremendo e parando, tremendo e parando" ============================
		// Quatro linhas porque sao quatro jeitos diferentes de "consertar" isto errado, e cada uma
		// derruba um deles:
		//
		//   1. a camera nunca para. E a queixa literal, e ela sozinha passaria com um rumor FORTE;
		//   2. o beat continua sendo PICO. Sozinha, passaria com o rumor deletado (era o estado velho);
		//   3. o fundo nunca cai abaixo do rumor -- prova que o piso e PISO, e nao um beat sobrando;
		//   4. e o fundo e FRACO: quase toda a cena roda no rumor e nao no pico. E o "116 s de camera
		//      sacudindo forte enjoa" virado numero -- sem ela, um `RumorDaCena = ForcaDoTremor` (a
		//      camera aos berros por dois minutos) passaria nas tres primeiras.
		//
		// MEDIDO (contra-teste rodado, nao prometido): com a linha do rumor do `_Process` desligada, a
		// primeira cai com "2082 parado(s), o primeiro aos 0,05s" de 2390 quadros -- 87% da cena com a
		// camera imovel, e a terceira cai junto ("o menor foi 0"). E a queixa do dono em numeros.
		// ====================================================================================================================
		float rumor = Jandirus.Core.Forms.Cinematicas.RumorDaCena;
		float pico = Jandirus.Core.Forms.Cinematicas.ForcaDoTremor;

		// ============================ A LEITURA E SEMPRE UM PASSO DEPOIS DA ACESA ============================
		// A ordem do laco e `Sacudir` (no `_Process` da cena) -> `TickDoTremorDeTeste` -> leitura, entao o
		// que a bancada le ja perdeu `Passo * Queda` = 0,4 px de amplitude. Vale pros DOIS extremos: o
		// rumor de 1,6 le 1,2 e o pico de 6,0 le 5,6.
		//
		// ISTO REPROVOU DE VERDADE na primeira rodada ("PICOS ... 5,6 px contra 1,6 de fundo"), com o
		// codigo de producao certo -- era a bancada que estava comparando o valor ANTES da queda com o
		// valor DEPOIS dela. Descontar o passo em vez de afrouxar a tolerancia mantem as duas linhas
		// capazes de acusar um rumor pela metade ou um pico pela metade.
		// ================================================================================================
		float umPasso = (float)Passo * Jandirus.Core.Forms.Cinematicas.QuedaDoTremor;
		float pisoEsperado = rumor - umPasso, picoEsperado = pico - umPasso;

		Conferir(quadrosComCameraParada == 0,
				 $"a camera NAO para de tremer em nenhum dos {quadrosDeCamera} quadros da cena "
			   + $"({quadrosComCameraParada} parado(s)"
			   + (primeiraParada >= 0 ? $", o primeiro aos {primeiraParada:0.##}s" : "") + ")");
		Conferir(maiorTremor >= picoEsperado - 0.01f,
				 $"e os beats de tremor continuam sendo PICOS por cima dele "
			   + $"({maiorTremor:0.##} px contra {rumor:0.##} de fundo, esperado >= {picoEsperado:0.##})");
		Conferir(menorTremor >= pisoEsperado - 0.01f,
				 $"o fundo nunca cai abaixo do rumor de {rumor:0.##} px "
			   + $"(o menor foi {menorTremor:0.##}, esperado >= {pisoEsperado:0.##})");
		Conferir(quadrosEmPico * 4 < quadrosDeCamera,
				 $"e o rumor e FRACO: so {quadrosEmPico} dos {quadrosDeCamera} quadros passam dele "
			   + $"-- o resto da cena treme a {rumor:0.##} px, nao a {pico:0.##}");

		// ============================ E O PIOR VAO, QUE E A QUEIXA LITERAL ============================
		// A primeira linha existe pra a segunda nao ser vazia: se o maior silencio da cena coubesse
		// dentro da vida de um beat (`Forca/Queda` = 0,75 s), o piso seria decorativo -- o proprio
		// impulso cobriria o buraco e "a camera nao para" passaria com o rumor deletado.
		//
		// A SEGUNDA E A AFIRMACAO DO DONO em numeros: no MAIOR buraco da cena mais longa do jogo, a
		// camera continua viva. Sem o rumor ela mediria zero, e este e o unico ponto da bancada que
		// olha pro fundo do buraco em vez de pra media da cena.
		//
		// MEDIDO (contraprova rodada): o maior vao da cena do SSJ3 tem 7,0 s e o rumor o atravessa a
		// 1,2 px; com o `Sacudir` do rumor desligado no `_Process`, esta linha cai com `o menor
		// tremor la dentro foi 0 px` -- sete segundos de camera morta no meio da estreia.
		// ==========================================================================================
		double vidaDeUmBeat = pico / Jandirus.Core.Forms.Cinematicas.QuedaDoTremor;
		Conferir(maiorSilencio > vidaDeUmBeat,
				 $"o maior vao SEM beat nenhum entre dois beats tem {maiorSilencio:0.#}s -- mais que a "
			   + $"vida de um solavanco ({vidaDeUmBeat:0.##}s), entao ha buraco de verdade pra cobrir");
		Conferir(menorNoMaiorSilencio >= pisoEsperado - 0.01f,
				 $"e a camera atravessa esse vao inteiro sem morrer (o menor tremor la dentro foi "
			   + $"{(menorNoMaiorSilencio == float.MaxValue ? 0 : menorNoMaiorSilencio):0.##} px, "
			   + $"esperado >= {pisoEsperado:0.##})");

		mundo.TickDoTremorDeTeste(10.0);   // nao deixa a camera tremendo pra os blocos seguintes

		_passos.Add("  --     escada ao vivo (entrando de SSJ2): " + desenho);

		// --- 4. E O SERVIDOR NAO SOUBE DE NADA ---
		// A CADA QUADRO, e nao so nos beats: uma concessao feita e desfeita entre duas amostras nao
		// existe como defeito plausivel, mas uma feita num beat qualquer da cena existe -- e a cena do
		// SSJ3 tem 21 beats, dos quais so 4 mexem no corpo.
		Conferir(!servidorMexeuNoMeio,
				 $"durante a cena inteira o servidor nao mexeu em nada (medido nos {(int)(cheia.Segundos / Passo)} quadros)");
		Conferir(Intacto(),
				 $"a cena inteira NAO desperta nem concede: `{atualAntes}` continua a forma do servidor, "
			   + $"{liberadasAntes.Length} despertada(s) continuam {est?.Liberadas.Count ?? -1}");
		Conferir(est != null && !est.Despertou("ssj1") && !est.Despertou("ssj2"),
				 "e nem SSJ1 nem SSJ2 constam como despertados depois de terem sido VESTIDOS");

		// ============================ O CONTRA-TESTE: A SONDA ENXERGA? ============================
		// Concede o SSJ1 na marra e exige que o `Intacto()` acuse. Sem isto, uma sonda cega (jogador
		// errado, `FormaDeTeste` nulo, comparacao invertida) daria as tres linhas de cima em verde com
		// o servidor sendo saqueado. Desfaz em seguida restaurando o conjunto guardado -- e nao
		// removendo o que foi posto, que sairia certo por coincidencia se `Liberar` fosse mudado.
		// ======================================================================================
		if (est != null)
		{
			est.Liberar("ssj1");
			Conferir(!Intacto(), "a sonda do servidor ENXERGA (conceder SSJ1 na marra faz a comparacao cair)");
			est.Liberadas.Clear();
			foreach (int r in liberadasAntes) est.Liberadas.Add(r);
			Conferir(Intacto(), "e a bancada devolveu o estado que achou");
		}

		// --- devolve o corpo pra a base pelo mesmo caminho ---
		mundo.AoMudarForma(eu, alvo.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(vis.CabeloDeTeste == cabeloBase,
				 $"e voltar pra base devolve o penteado normal ({vis.CabeloDeTeste.GetFile()})");
	}

	// =====================================================================
	// 3a-ter-ante. O OLHO NO DESENHO -- os pixels, e nao o catalogo
	// =====================================================================
	/// <summary>
	/// A ESCLEROTICA DO CORPO, `#fcfdfd`. Escrita aqui e MEDIDA logo abaixo: a linha do catalogo
	/// (`Catalogo.BrancoSemIris`) tem que bater com o pixel do arquivo, e as duas escritas existem
	/// justamente pra o dia em que alguem redesenhar o rosto.
	/// </summary>
	private const string EscleroticaDoCorpo = "fcfdfd";

	/// <summary>
	/// ============================ "SEM IRIS" E UMA DECISAO SOBRE O DESENHO, ENTAO SE MEDE O DESENHO ============================
	/// O dono pediu *"LSSJ: olhos BRANCOS -- sem iris"* e mandou olhar o sprite antes de escolher como
	/// representar. A escolha foi **pintar a iris da cor da esclerotica**, e nao esconder a camada.
	/// Este bloco e a prova dela, e ele nao pergunta nada ao catalogo -- le PNG.
	///
	/// SAO TRES AFIRMACOES, e cada uma sustenta um pedaco da decisao:
	///
	///   1. **a camada de olhos e so a IRIS.** A folha inteira tem uma mao-cheia de pixels opacos --
	///      dois por quadro nos perfis, quatro de frente, zero de costas. Se ela fosse o olho inteiro,
	///      pintar de branco apagaria tambem o cilio e o contorno;
	///   2. **eles sao todos PRETOS.** E o que autoriza o modo SOMA: `clamp(0 + tinta)` devolve
	///      exatamente a tinta, entao o hexa escrito no catalogo e o hexa que chega na tela. Um sprite
	///      cinza (como o `god - grey` das coladas) exigiria matiz;
	///   3. **o CORPO desenha uma iris COLORIDA embaixo, ladeada pela esclerotica branca.** Esta e a
	///      que derruba a alternativa obvia: esconder a camada NAO apaga iris nenhuma -- revela a iris
	///      azul de fabrica do corpo. E e daqui que sai o hexa do branco.
	///
	/// COMO REPROVA SE A REGRA SUMIR: troque o `BrancoSemIris` do Core por `ffffff` e a terceira cai
	/// (o pixel do corpo e `fcfdfd`). Substitua a folha de olhos por uma com o olho inteiro desenhado e
	/// a primeira cai. Mande a camada de olhos SUMIR em vez de pintar e nada aqui reprova -- mas a
	/// tabela do `OQueCadaFormaFazNoCabelo` reprova, e este bloco e o que explica por que.
	/// ================================================================================================
	/// </summary>
	/// <summary>
	/// O CORPO MUSCULOSO -- a folha escolhida pela PELE, e as tres coisas que so uma bancada ve.
	///
	/// ============================ POR QUE ISTO PRECISA DE BANCADA ============================
	/// As tres falhas possiveis aqui sao todas SILENCIOSAS em jogo:
	///
	///   1. **a folha nao esta importada.** O `.tres` esta na pasta, o catalogo aponta pra ele, e o
	///      `ResourceLoader.Load` devolve nulo -- o `CorpoDaForma` avisa por `PushWarning` e segue.
	///      O jogador se transforma e nao incha, e ninguem descobre por que. Ja aconteceu tres vezes
	///      neste projeto (os 35 atlas, os sons, a aura do Rose);
	///   2. **a folha nao tem a animacao.** As musculosas trazem 24 estados contra os 48 do
	///      `NewPaleMale`, e o `Escolher` cobre o buraco emprestando pose. Emprestar e o certo, mas
	///      "emprestou" e "sumiu" sao a mesma coisa vistas de fora se ninguem contar quantas foram;
	///   3. **o rabo.** Esta e a que me pegou: a regra do rabo era "some pra QUALQUER corpo de
	///      forma", e um Saiyajin em Grade 2 sairia sem cauda -- e sem cauda nao ha Oozaru, que e a
	///      porta do SSJ4. Uma linha de desenho apagando um degrau da escada.
	/// ======================================================================================
	/// </summary>
	private void OCorpoInchado()
	{
		// --- 1. QUEM INCHA, no catalogo ---
		//
		// A ESPERADA E ESCRITA A MAO de proposito, e nao derivada de `Linha`: se ela fosse "toda
		// Legendary sem corpo de SSJ4", ela concordaria com o catalogo por construcao e nao mediria
		// nada. Escrita, ela e o enunciado do dono virado lista -- e o Wrathful de fora e a UNICA
		// divergencia dele (ver a entrada dele em `Formas.cs`).
		string[] devemInchar =
		[
			"grade2", "grade3",
			"c_type", "legendary",
			"primal_c_type", "primal_legendary", "primal_legendary2", "primal_legendary3",
		];

		var incham = Jandirus.Core.Forms.Catalogo.Todas
			.Where(d => d.Corpo == CorpoDeForma.Musculoso).Select(d => d.Id).OrderBy(x => x).ToArray();
		Conferir(incham.SequenceEqual(devemInchar.OrderBy(x => x)),
				 $"exatamente os {devemInchar.Length} degraus inchados tem `Musculoso` "
			   + $"(deu {incham.Length}: {string.Join(", ", incham)})");

		// E OS TRES SSJ4 CONTINUAM COM A PELAGEM, nao com o musculo. E a mesma pergunta que a
		// `CorDoOlho` faz pra dar o amarelado: se o campo virasse booleano de novo, os nove de cima
		// entrariam nela calados.
		foreach (string id in new[] { "ssj4", "ssj4_full_power", "ssj4_limit_breaker" })
			Conferir(Jandirus.Core.Forms.Catalogo.Def(id)!.Corpo == CorpoDeForma.Ssj4,
					 $"`{id}` continua sendo PELAGEM de SSJ4 e nao musculo");
		Conferir(Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdWrathful)!.Corpo
					 == CorpoDeForma.Nenhum,
				 "o `wrathful` NAO incha -- `lssjbuff.dm:84-89`, o unico `if` sem linha de icone");

		// ============================ E A `LSSJ4` FICA DE FORA, DITA PELO NOME ============================
		// *"o corpo musculoso entra no LSSJ (menos a lssj4)"*. Os tres degraus da lssj4 ja caem na
		// igualdade la em cima -- eles nao estao na lista --, mas caem como "a lista tem 9 e deu 12", que
		// nao diz QUEM sobrou. Estas tres linhas nomeiam o corte do dono pra quem for ler o log atras
		// dele, e reprovam sozinhas se alguem "completar" a linha primal por simetria.
		//
		// ============================ "NAO INCHA" NAO E "NAO TROCA DE CORPO" ============================
		// Escrevi isto como `Corpo == Nenhum` e a bancada me corrigiu: os tres tem `Corpo = Ssj4` -- eles
		// vestem a PELAGEM, como os SSJ4 da linha Saiyajin (`SufixoDoCabelo = "SSJ4"` nas tres entradas).
		// O corte do dono e sobre o MUSCULO, e nao sobre trocar de corpo; e as duas coisas dividem o
		// mesmo campo desde que ele virou enum. A pergunta certa e pelo VALOR.
		// ============================================================================================
		foreach (string id in new[] { "primal_legendary4", "primal_legendary4_full_power",
									  "primal_legendary4_limit_breaker" })
		{
			CorpoDeForma corpoDela = Jandirus.Core.Forms.Catalogo.Def(id)!.Corpo;
			Conferir(corpoDela != CorpoDeForma.Musculoso,
					 $"`{id}` (a lssj4) NAO incha -- o corte do dono dentro da linha Legendary "
				   + $"(corpo {corpoDela})");
			Conferir(corpoDela == CorpoDeForma.Ssj4,
					 $"-- ela veste a PELAGEM de SSJ4, que e outra coisa e nao musculo ({corpoDela})");
		}

		// E OS DOIS GRADES INCHAM, ditos pelo nome pelo mesmo motivo -- eles sao a outra metade do
		// enunciado (*"e nos Grades"*), e o `if(1.5)` do `supersaiyanbuff.dm:222` e de onde saem.
		foreach (string id in new[] { "grade2", "grade3" })
			Conferir(Jandirus.Core.Forms.Catalogo.Def(id)!.Corpo == CorpoDeForma.Musculoso,
					 $"`{id}` INCHA -- o `apply_ussj_body()` do `if(1.5)` (supersaiyanbuff.dm:222)");

		// --- 2. A PELE ESCOLHE A FOLHA (`apply_ussj_body`, supersaiyanbuff.dm:357-360) ---
		(string corpo, string? esperada)[] pele =
		[
			("res://Assets/Sprites/Character Icons/NewPaleMale.tres", CorposDeForma.MusculosoClaro),
			("res://Assets/Sprites/Character Icons/NewTanMale.tres", CorposDeForma.MusculosoMoreno),
			("res://Assets/Sprites/Character Icons/NewBlackMale.tres", CorposDeForma.MusculosoNegro),
			("res://Assets/Sprites/Character Icons/BaseWhiteMale.tres", CorposDeForma.MusculosoClaro),
			("res://Assets/Sprites/Character Icons/White Male.tres", CorposDeForma.MusculosoClaro),
			// AS QUATRO QUE NAO INCHAM, e a mulher e a primeira delas: nao existe folha musculosa
			// feminina no projeto (nem no DM). A resposta certa e `null`, e o corpo fica o que era --
			// por um corpo masculino nela seria pior que nao inchar.
			("res://Assets/Sprites/Character Icons/NewPaleFemale.tres", null),
			("res://Assets/Sprites/Character Icons/NewBlackFemale.tres", null),
			("res://Assets/Sprites/Character Icons/Namekians/Namek Young.tres", null),
			("", null),   // boneco que ainda nao se vestiu: o `if(musc)` do DM sai sem trocar nada
		];
		foreach ((string c, string? e) in pele)
			Conferir(CorposDeForma.Caminho(CorpoDeForma.Musculoso, c) == e,
					 $"`{(c.Length == 0 ? "(sem corpo)" : c.GetFile())}` -> "
				   + $"{(e == null ? "NAO incha" : e.GetFile())}");

		// --- 3. AS TRES FOLHAS ESTAO IMPORTADAS, E O QUE FALTA NELAS ESTA MEDIDO ---
		//
		// A REFERENCIA E O CORPO DA MESMA PELE, e nao o `NewPaleMale`: so o palido masculino tem as
		// 48 animacoes (dodge, spinkick, spinpunch e as duas de carga so existem nele). Medir os
		// tres contra ele acusaria "faltam 28" em folhas que estao em dia com o corpo que elas
		// substituem -- e a bancada estaria reprovando o corpo BASE do moreno e do negro.
		(string baseDoTom, string musc)[] pares =
		[
			("res://Assets/Sprites/Character Icons/NewPaleMale.tres", CorposDeForma.MusculosoClaro),
			("res://Assets/Sprites/Character Icons/NewTanMale.tres", CorposDeForma.MusculosoMoreno),
			("res://Assets/Sprites/Character Icons/NewBlackMale.tres", CorposDeForma.MusculosoNegro),
		];
		foreach ((string b, string m) in pares)
		{
			Conferir(CorposDeForma.Existe(m), $"`{m.GetFile()}` esta IMPORTADA (nao so na pasta)");
			var fm = ResourceLoader.Load<SpriteFrames>(m);
			var fb = ResourceLoader.Load<SpriteFrames>(b);
			Conferir(fm != null && fb != null, $"`{m.GetFile()}` e `{b.GetFile()}` carregam");
			if (fm == null || fb == null) continue;

			// O QUADRO TEM QUE SER O MESMO. Maior que o do corpo faria o `CharacterVisual` chamar
			// isto de CRIATURA (a derivacao por tamanho) e apagar roupa, cabelo e olhos -- o
			// jogador viraria um macaco musculoso pelado ao entrar em Grade 2.
			Vector2 qm = fm.GetFrameTexture(fm.GetAnimationNames()[0], 0)?.GetSize() ?? Vector2.Zero;
			Vector2 qb = fb.GetFrameTexture(fb.GetAnimationNames()[0], 0)?.GetSize() ?? Vector2.Zero;
			Conferir(qm == qb && qm.X > 0,
					 $"`{m.GetFile()}`: o quadro e o do corpo ({qm.X}x{qm.Y} contra {qb.X}x{qb.Y})");

			// O QUE FALTA, NOMEADO. O `Escolher` do `CharacterVisual` cobre os buracos (o apelido
			// `_mov` acha `flight_mov_*`, e quem nao tem `default_*` cai no primeiro quadro do
			// `walk_*`), entao isto nao reprova -- ele CONTA e escreve no log, que e a diferenca
			// entre "sabido" e "descoberto em jogo".
			string[] faltam = [.. fb.GetAnimationNames()
				.Where(a => !fm.HasAnimation(a))
				.Where(a => !fm.HasAnimation(a.Replace("flight_", "flight_mov_")))
				.OrderBy(a => a)];
			_passos.Add($"  --     `{m.GetFile()}`: {fm.GetAnimationNames().Length} animacoes, "
					  + $"faltam {faltam.Length} do corpo base"
					  + (faltam.Length > 0 ? $" ({string.Join(", ", faltam)})" : ""));

			// MAS O IDLE NAO PODE FALTAR SEM SUBSTITUTA. Sem `default_*` E sem `walk_*` a camada
			// sairia invisivel -- o jogador some ao ficar parado, que e o estado mais comum do jogo.
			foreach (string dir in new[] { "south", "north", "east", "west" })
				Conferir(fm.HasAnimation($"default_{dir}") || fm.HasAnimation($"walk_{dir}"),
						 $"`{m.GetFile()}`: ha pose parada (ou caminhada) pra `{dir}`");
		}

		// --- 4. NO BONECO: incha, mantem o rabo, e desincha ---
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados)) { Conferir(false, "o catalogo visual pra o boneco inchado"); return; }
		var cat = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

		var boneco = new CharacterVisual { Name = "BonecoInchado" };
		AddChild(boneco);
		try
		{
			// `Corpo = 1` E O MORENO -- de proposito, e nao o padrao: o indice 0 daria `NewPaleMale`
			// e a checagem passaria com a folha que ja e a primeira do `switch`. Com o moreno, um
			// resolvedor que devolvesse "a primeira que achar" reprova.
			boneco.Vestir(cat, new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku", Corpo = 1 },
						  "Saiyan", "Male");
			boneco.MostrarRabo(true);
			Conferir(boneco.RaboVisivelDeTeste, "o boneco tem rabo antes de inchar (senao nada abaixo mede)");

			boneco.CorpoDaForma(CorpoDeForma.Musculoso);
			Conferir(boneco.CorpoDaFormaDeTeste, "o Grade 2 cria a camada de corpo");
			Conferir(boneco.FolhaDoCorpoDaFormaDeTeste == CorposDeForma.MusculosoMoreno,
					 $"e ela e a folha MORENA ({boneco.FolhaDoCorpoDaFormaDeTeste.GetFile()})");
			Conferir(!boneco.EhCriatura,
					 "o corpo inchado NAO e criatura -- a roupa, o cabelo e os olhos continuam");
			Conferir(boneco.TemCabeloVisivelDeTeste, "o cabelo continua visivel por cima do musculo");

			// A REGRESSAO. Antes desta tarefa a `Escondida` apagava o rabo pra qualquer camada de
			// forma, e este `Conferir` seria vermelho.
			Conferir(boneco.RaboVisivelDeTeste,
					 "O RABO CONTINUA -- a folha musculosa nao desenha rabo nenhum (e sem rabo nao ha Oozaru)");
			Conferir(boneco.TintaDoRaboDeTeste != null, "e ele continua pintavel (o `PintarRabo` nao desiste)");

			// A POSE. A mesma varredura do macaco, e pelo mesmo motivo: um `AnimatedSprite2D` novo
			// nasce com `Animation = "default"`, que NAO existe nestas folhas -- ele ficaria visivel,
			// com nome, desenhando nada.
			var folha = ResourceLoader.Load<SpriteFrames>(CorposDeForma.MusculosoMoreno);
			int apagados = 0; string pior = "";
			foreach (Jandirus.Net.Protocol.Pose p in Enum.GetValues<Jandirus.Net.Protocol.Pose>())
				foreach (Jandirus.Core.World.Facing f in Enum.GetValues<Jandirus.Core.World.Facing>())
					foreach (bool andando in new[] { false, true })
					{
						boneco.SetPose(p);
						boneco.SetMotion(f, andando);
						if (boneco.CorpoDaFormaVisivelDeTeste && folha != null
							&& folha.HasAnimation(boneco.PoseDoCorpoDaFormaDeTeste)) continue;
						apagados++;
						if (pior.Length == 0)
							pior = $"{p}/{f}{(andando ? " andando" : "")}: corpo em `{boneco.PoseDeTeste}`, "
								 + $"musculo em `{boneco.PoseDoCorpoDaFormaDeTeste}`";
					}
			Conferir(apagados == 0,
					 $"o corpo inchado desenha em TODA pose ({apagados} apagados, ex.: {pior})");

			boneco.SetPose(Jandirus.Net.Protocol.Pose.Normal);
			boneco.SetMotion(Jandirus.Core.World.Facing.South, moving: false);
			boneco.CorpoDaForma(CorpoDeForma.Nenhum);
			Conferir(!boneco.CorpoDaFormaDeTeste, "reverter DESINCHA (senao o lutador fica inchado pra sempre)");
			Conferir(boneco.RaboVisivelDeTeste, "e o rabo continua la depois de desinchar");

			// ============================ AS TRES PELES, NO BONECO E PELA FORMA ============================
			// O bloco 2 mede o RESOLVEDOR (`CorposDeForma.Caminho`), que e uma funcao pura; as linhas de
			// cima medem UMA pele no boneco. O que falta e a costura: o `CorpoDaForma` le a pele do meta
			// `src` da camada de corpo -- se essa leitura quebrar, o resolvedor continua certo e o
			// jogador incha sempre na MESMA folha, com dois dos tres tons de pele saindo errados.
			//
			// E A FORMA E QUEM PEDE (`d.Corpo`, do `legendary`) em vez do simbolo na mao: e assim que o
			// `World.VestirAFormaSemCena` chama, e e o que liga esta medida ao pedido do dono
			// ("o corpo musculoso entra no LSSJ e nos Grades, e a folha escolhida casa com a pele base").
			// ==========================================================================================
			CorpoDeForma doLegendary = Jandirus.Core.Forms.Catalogo.Def("legendary")!.Corpo;
			(int Indice, string Base, string Musculosa, string Tom)[] peles =
			[
				(0, "NewPaleMale.tres", CorposDeForma.MusculosoClaro, "clara"),
				(1, "NewTanMale.tres", CorposDeForma.MusculosoMoreno, "morena"),
				(2, "NewBlackMale.tres", CorposDeForma.MusculosoNegro, "negra"),
			];
			foreach ((int i, string baseEsperada, string musc, string tom) in peles)
			{
				boneco.Vestir(cat, new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku", Corpo = i },
							  "Saiyan", "Male");
				boneco.CorpoDaForma(doLegendary);
				Conferir(boneco.CorpoDaFormaDeTeste && boneco.FolhaDoCorpoDaFormaDeTeste == musc,
						 $"pele {tom} (`{baseEsperada}`) -> incha na `{musc.GetFile()}` "
					   + $"(deu `{boneco.FolhaDoCorpoDaFormaDeTeste.GetFile()}`)");
				boneco.CorpoDaForma(CorpoDeForma.Nenhum);
			}

			// E A MULHER NAO INCHA -- no boneco, e nao so na tabela. `CorpoDaForma` recebe o MESMO
			// simbolo e nao cria camada nenhuma, porque a folha dela nao tem musculosa.
			boneco.Vestir(cat, new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku" },
						  "Saiyan", "Female");
			boneco.CorpoDaForma(CorpoDeForma.Musculoso);
			Conferir(!boneco.CorpoDaFormaDeTeste,
					 "a personagem FEMININA nao troca de corpo (nao ha arte, e nao se poe corpo de homem nela)");
		}
		finally
		{
			RemoveChild(boneco);
			boneco.QueueFree();
		}
	}

	private void OOlhoNoDesenho()
	{
		const string folhaDoOlho = "res://Assets/Sprites/Clothes/Eyes_Black.png";
		const string corpoDeProva = "res://Assets/Sprites/Character Icons/NewPaleMale.png";

		// ARTE IMPORTADA E NAO SO PRESENTE NA PASTA -- a regra da casa. Um `Load` nulo aqui viraria
		// `continue` silencioso e o bloco inteiro nao mediria nada.
		var texOlho = ResourceLoader.Load<Texture2D>(folhaDoOlho);
		var texCorpo = ResourceLoader.Load<Texture2D>(corpoDeProva);
		Conferir(texOlho != null && texCorpo != null,
				 "a folha de olhos e o corpo de prova estao IMPORTADOS (senao nada abaixo mede)");
		if (texOlho == null || texCorpo == null) return;

		Image imgOlho = texOlho.GetImage(), imgCorpo = texCorpo.GetImage();

		// --- 1 e 2: quantos pixels a camada tem, e de que cor ---
		int opacos = 0, pretos = 0;
		for (int y = 0; y < imgOlho.GetHeight(); y++)
			for (int x = 0; x < imgOlho.GetWidth(); x++)
			{
				Color c = imgOlho.GetPixel(x, y);
				if (c.A <= 0f) continue;
				opacos++;
				if (c.R8 == 0 && c.G8 == 0 && c.B8 == 0) pretos++;
			}

		// O TETO E FROUXO DE PROPOSITO (4 pixels x 42 quadros = 168): a pergunta nao e "sao exatamente
		// 90", e sim "isto e uma IRIS e nao um olho desenhado". Um olho inteiro nesta folha passaria
		// dos milhares -- a `Eyes_Black` tem 43 mil pixels de area.
		Conferir(opacos > 0 && opacos < 200,
				 $"a camada de olhos e so a IRIS: {opacos} pixels opacos em {imgOlho.GetWidth()}x{imgOlho.GetHeight()}");
		Conferir(pretos == opacos,
				 $"e os {opacos} sao TODOS pretos -- e o que autoriza a soma ({pretos} pretos)");

		// --- 3: o que o CORPO desenha debaixo deles ---
		// O PRIMEIRO QUADRO, e a linha do olho sai da propria folha em vez de estar escrita: procura-se
		// a linha `y` com pixels opacos dentro da celula 0 e mede-se o corpo NELA. Escrever `y = 13`
		// aqui amarraria a bancada a um desenho especifico do rosto.
		const int celula = 32;
		int achouIris = 0, achouEsclerotica = 0;
		string corDaEsclerotica = "";
		for (int y = 0; y < celula; y++)
			for (int x = 0; x < celula; x++)
			{
				if (imgOlho.GetPixel(x, y).A <= 0f) continue;

				// O CORPO NO MESMO PIXEL: e a iris de fabrica, e ela NAO pode ser o branco -- se fosse,
				// esconder a camada ja daria "sem iris" e a decisao seria outra.
				Color sob = imgCorpo.GetPixel(x, y);
				if (sob.A > 0f && !(sob.R8 > 240 && sob.G8 > 240 && sob.B8 > 240)) achouIris++;

				// E A ESCLEROTICA E O VIZINHO HORIZONTAL DE FORA (o olho esquerdo tem branco a
				// esquerda, o direito a direita). Basta um dos dois: os quadros de perfil so tem um.
				foreach (int viz in new[] { x - 1, x + 1 })
				{
					if (viz < 0 || viz >= celula) continue;
					Color b = imgCorpo.GetPixel(viz, y);
					if (b.A <= 0f || b.R8 < 240 || b.G8 < 240 || b.B8 < 240) continue;
					achouEsclerotica++;
					corDaEsclerotica = $"{b.R8:x2}{b.G8:x2}{b.B8:x2}";
				}
			}

		Conferir(achouIris > 0,
				 $"o CORPO ja desenha uma iris colorida debaixo da camada ({achouIris} pixels) -- "
			   + "por isso ESCONDER a camada nao apagaria iris nenhuma");
		Conferir(achouEsclerotica > 0 && corDaEsclerotica == EscleroticaDoCorpo,
				 $"e a esclerotica encostada nela e #{EscleroticaDoCorpo} (achou #{corDaEsclerotica} "
			   + $"em {achouEsclerotica} vizinhos)");

		// E O CATALOGO DIZ ESSE MESMO HEXA. Esta e a costura: o pixel medido acima e o valor que o
		// Core devolve pra as formas lendarias **enquanto a furia dirige o corpo** tem que ser a MESMA
		// string.
		//
		// O `semRedeas: true` NAO E DETALHE DE CHAMADA, e o enunciado inteiro: o branco sem iris deixou
		// de ser a cor da linha Legendary e virou a cor de um corpo POSSUIDO (`Catalogo.CorDoOlho(d,
		// semRedeas)`). Com as redeas na mao a mesma forma devolve o verde da escada -- e e por isso
		// que a linha de baixo existe: sem ela, um `CorDoOlho` que ignorasse o bit e devolvesse branco
		// sempre passaria aqui em cima com tudo errado.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoOlho(
					 Jandirus.Core.Forms.Catalogo.Def("primal_legendary"), semRedeas: true) == corDaEsclerotica,
				 "e o 'sem iris' do catalogo e exatamente o branco que o desenho tem");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoOlho(
					 Jandirus.Core.Forms.Catalogo.Def("primal_legendary"), semRedeas: false) != corDaEsclerotica,
				 "-- e com as REDEAS NA MAO ele nao e branco nenhum (a pupila verde volta)");
	}

	// =====================================================================
	// 3a-ter-A. AS DUAS CONTAS DO SHADER, REFEITAS EM C#
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE REFAZER A CONTA AQUI E LEGITIMO ============================
	/// O pedido do dono e sobre o RESULTADO -- *"o azul do Blue sobre cabelo LOIRO nao resulta em branco
	/// (meça o resultado, nao a entrada)"* --, e o resultado so existe depois que o `Personagem.gdshader`
	/// roda. No headless nao ha quadro pra ler: o `GetImage` do viewport volta vazio (e a foto desta
	/// bancada ja diz isso no log). Entao a conta e refeita aqui, sobre os pixels REAIS da folha.
	///
	/// Isso e uma SEGUNDA VERDADE, e ela e perigosa do jeito conhecido: alguem troca a luminancia do
	/// shader e a bancada continua aprovando o azul que ela mesma calculou. A trava esta em
	/// <see cref="AsDuasContasSaoAsDoShader"/>, que exige as tres expressoes literais dentro do `.gdshader`
	/// -- se a operacao mudar la, ela reprova aqui e o replicador vai junto pra a mesa.
	/// ============================================================================================
	/// </summary>
	private static Color EmMatiz(Color desenho, Color tinta)
	{
		float luz = desenho.R * 0.299f + desenho.G * 0.587f + desenho.B * 0.114f;
		return new Color(Mathf.Clamp(tinta.R * (luz * 2f), 0f, 1f),
						 Mathf.Clamp(tinta.G * (luz * 2f), 0f, 1f),
						 Mathf.Clamp(tinta.B * (luz * 2f), 0f, 1f));
	}

	/// <inheritdoc cref="EmMatiz"/>
	private static Color EmSoma(Color desenho, Color tinta) =>
		new(Mathf.Clamp(desenho.R + tinta.R, 0f, 1f),
			Mathf.Clamp(desenho.G + tinta.G, 0f, 1f),
			Mathf.Clamp(desenho.B + tinta.B, 0f, 1f));

	/// <summary>
	/// OS TONS OPACOS DE UMA FOLHA, do mais comum ao menos raro. Vazio quando a arte nao carrega -- e
	/// quem chama TEM que reprovar nesse caso, senao o bloco inteiro vira um `foreach` sobre nada.
	///
	/// Le a folha inteira e nao um quadro: as artes deste jogo tem poucos tons (o cabelo de SSJ tem
	/// QUATRO em 8640 pixels), e e essa paleta curta que faz a medicao valer -- "o resultado" nao e uma
	/// media, e o que acontece com cada degrau do sombreado.
	/// </summary>
	private static (Color Cor, int Pixels)[] TonsOpacosDe(string png)
	{
		if (!ResourceLoader.Exists(png)) return [];
		if (ResourceLoader.Load<Texture2D>(png)?.GetImage() is not { } img) return [];

		var conta = new Dictionary<int, int>();
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				Color c = img.GetPixel(x, y);
				if (c.A <= 0f) continue;
				int chave = (c.R8 << 16) | (c.G8 << 8) | c.B8;
				conta[chave] = conta.GetValueOrDefault(chave) + 1;
			}

		return [.. conta.OrderByDescending(p => p.Value)
						.Select(p => (new Color(((p.Key >> 16) & 255) / 255f,
												((p.Key >> 8) & 255) / 255f,
												(p.Key & 255) / 255f), p.Value))];
	}

	/// <summary>O PNG que este `.tres` embrulha. As duas artes moram lado a lado, com o mesmo nome.</summary>
	private static string PngDe(string tres) => tres.GetBaseName() + ".png";

	/// <summary>
	/// A TRAVA DO REPLICADOR: as duas operacoes que <see cref="EmMatiz"/> e <see cref="EmSoma"/> copiam
	/// tem que estar ESCRITAS no shader, palavra por palavra.
	///
	/// Sem estas tres linhas o replicador seria uma opiniao: trocar a luminancia por `(r+g+b)/3` no
	/// `.gdshader` mudaria todo azul, rosa e verde da tela e esta bancada continuaria verde, medindo uma
	/// conta que o jogo nao faz mais.
	/// </summary>
	private void AsDuasContasSaoAsDoShader()
	{
		var sh = GD.Load<Shader>("res://Assets/Shaders/Personagem.gdshader");
		string code = sh?.Code ?? "";
		Conferir(code.Length > 0, "o `Personagem.gdshader` tem codigo pra a bancada conferir a conta");
		Conferir(code.Contains("vec3(0.299, 0.587, 0.114)"),
				 "a LUMINANCIA do matiz continua sendo a do replicador (0.299/0.587/0.114)");
		Conferir(code.Contains("tinta * (luz * 2.0)"),
				 "o MATIZ continua sendo `tinta * luz * 2` -- e desse `2` que sai o teto de 141 das tintas");
		Conferir(code.Contains("c.rgb + tinta"),
				 "e a SOMA continua sendo `desenho + tinta` (o `ICON_ADD` do BYOND)");

		// ============================ A TERCEIRA OPERACAO E A QUE NAO TINHA TRAVA ============================
		// A tinta da colada NAO passa pelo uniform `tinta`: ela e o `modulate` do node (o
		// `color = rgb(110,255,140)` do `EffectLayer.dm:32`), e quem entrega isso ao shader e o varying
		// `COLOR` do Godot. Nao havendo trava, ela ficou ORFA: o `Personagem.gdshader` terminava em
		// `COLOR = c`, sem NUNCA ler o `COLOR` que chegou, e todo `Modulate`/`SelfModulate` desta pilha
		// era descartado CALADO. Foi o defeito que o dono relatou (*"o `god - grey.png` ta CINZA ainda e
		// n verde"*) -- e junto com ele morriam o tom do Namekuseijin (`VisualCatalog.TintaDoCorpo`, o
		// `Brilho` 1,18/0,82) e o vulto simples do Zanzoken, que some pelo proprio `Modulate`.
		//
		// A pergunta e sobre o TEXTO do shader pelo mesmo motivo das tres de cima: no headless nao ha
		// quadro pra ler (ver <see cref="EmMatiz"/>). Escrever `COLOR = c` de novo reprova aqui, e a
		// bancada volta a ser a unica que ve o canal desligado antes do dono.
		//
		// ============================ E ESTA TRAVA JA REPROVOU UM SHADER CONSERTADO ============================
		// Ela era `code.Contains("COLOR = c * COLOR")` -- o literal exato do primeiro conserto. So que
		// `c * COLOR` estava ERRADO: no `fragment` do canvas_item o `COLOR` que CHEGA ja vem multiplicado
		// pela textura, entao aquilo desenhava a folha AO QUADRADO (o cabelo preto que o dono viu). O
		// conserto certo leva o modulate PURO do `vertex` num varying -- e no dia em que ele entrou, esta
		// linha virou FALHA vermelha em cima de um shader que estava certo. Trava por literal protege ate o
		// proximo conserto e nao um dia a mais.
		//
		// Agora ela pergunta o que interessa, e nao a forma: **o modulate do node chega inteiro na cor
		// final?** Sao DOIS elos, e os dois sao lidos do texto -- o `vertex` guarda o `COLOR` (que la ainda
		// e so o modulate) num varying, e o `fragment` multiplica a cor final por esse mesmo varying.
		// `COLOR = c;` quebra o segundo; apagar a linha do `vertex` quebra o primeiro; RENOMEAR o varying
		// nao quebra nada, que e exatamente o certo.
		//
		// (E O `CanvasModulate` DO MUNDO NAO VEM JUNTO por este canal, o que ja se chegou a suspeitar: o
		// Godot aplica o ambiente DEPOIS do fragment. A prova, com numeros, esta no fim do
		// `Personagem.gdshader`; quem quiser refazer, e a `--diagtintamundo`.)
		//
		// SEM OS COMENTARIOS, e isto nao e detalhe: este shader tem 60 linhas de prosa que citam
		// `COLOR = c` e `c * COLOR` ao explicar os dois defeitos antigos. Casar no arquivo cru aprovaria o
		// shader pela DESCRICAO do erro que ele conta ter cometido.
		// ====================================================================================================
		string sem = SemComentarios(code);
		var varying = System.Text.RegularExpressions.Regex.Match(sem, @"varying\s+vec4\s+(\w+)\s*;");
		Conferir(varying.Success,
				 "o shader declara um varying pra levar o modulate do `vertex` ao `fragment`");
		string via = varying.Success ? varying.Groups[1].Value : "cor_do_node";

		Conferir(System.Text.RegularExpressions.Regex.IsMatch(sem, $@"\b{via}\s*=\s*COLOR\s*;"),
				 $"o `vertex` GUARDA o modulate puro em `{via}` -- la o `COLOR` ainda nao passou pela textura");
		Conferir(System.Text.RegularExpressions.Regex.IsMatch(sem, $@"\bCOLOR\s*=\s*[^;]*\b{via}\b[^;]*;"),
				 $"e a cor final MULTIPLICA por `{via}` -- e por este canal que a tinta da colada "
			   + "(`modulate`) e o tom do Namekuseijin (`self_modulate`) chegam ao pixel");
		Conferir(!System.Text.RegularExpressions.Regex.IsMatch(sem, @"\bCOLOR\s*=\s*c\s*\*\s*COLOR\s*;"),
				 "e ela NAO multiplica pelo `COLOR` do fragment, que ja vem com a textura dentro "
			   + "(era a folha ao quadrado: cabelo preto e olho preto)");
	}

	/// <summary>
	/// O CODIGO DO SHADER SEM A PROSA. Existe por um motivo especifico e vivido: os arquivos deste
	/// projeto EXPLICAM os defeitos antigos citando o codigo errado por extenso, entao qualquer trava
	/// que case texto no arquivo cru pode aprovar um shader pela descricao do erro que ele nao comete
	/// mais. Tira `/* */` e `//` -- o `///` cai junto, ele comeca por `//`.
	/// </summary>
	private static string SemComentarios(string codigo) =>
		System.Text.RegularExpressions.Regex.Replace(
			System.Text.RegularExpressions.Regex.Replace(
				codigo, @"/\*.*?\*/", "", System.Text.RegularExpressions.RegexOptions.Singleline),
			@"//[^\n]*", "");

	/// <inheritdoc cref="EmMatiz"/>
	/// <remarks>
	/// A TERCEIRA CONTA. Nao e um `tinta_modo`: e o que o `COLOR` do Godot faz sozinho quando o shader o
	/// consome, e e o que o `color` de um atom do BYOND sempre fez. Ver <see cref="AsDuasContasSaoAsDoShader"/>.
	/// </remarks>
	private static Color EmMultiplicacao(Color desenho, Color tinta) =>
		new(desenho.R * tinta.R, desenho.G * tinta.G, desenho.B * tinta.B);

	/// <summary>
	/// QUANTA COR SOBROU: canal maior menos canal menor, sobre o maior. Zero e cinza (ou branco, ou
	/// preto) -- e cinza e exatamente o defeito que o dono viu.
	/// </summary>
	private static float Saturacao(Color c)
	{
		float maior = Mathf.Max(c.R, Mathf.Max(c.G, c.B));
		float menor = Mathf.Min(c.R, Mathf.Min(c.G, c.B));
		return maior <= 0.001f ? 0f : (maior - menor) / maior;
	}

	// =====================================================================
	// 3a-ter-B. O OVERLAY COLADO DE **CADA** FORMA, E A ARTE QUE MANDA NA TINTA
	// =====================================================================
	/// <summary>
	/// AS 36 ENTRADAS, UMA A UMA. O bloco do <see cref="NoCorpo"/> escolhe NOVE formas a dedo e as veste
	/// num boneco de verdade -- e aquilo responde *"o node recebeu o que o catalogo disse"*. Falta a
	/// outra metade, e ela e a que o dono pediu: **o catalogo diz a coisa certa nas outras vinte e
	/// sete?** Um degrau divino novo nasce com colada sozinho (a derivacao e por Linha+Ordem); um degrau
	/// de linha nenhuma tem que nascer SEM. As duas coisas so aparecem varrendo o catalogo inteiro.
	///
	/// ============================ E A TINTA E PROPRIEDADE DA ARTE, NAO DA FORMA ============================
	/// *"`god.png` e `god blue.png` NAO recebem tinta; `god - grey` recebe (verde no LSSJ, rosa no
	/// Rose)"* nao e gosto: e o que o DESENHO permite. Por isso este bloco mede os quatro PNGs em vez de
	/// so reler o catalogo -- enquanto a `god - grey` for cinza puro ela e a unica que pode multiplicar,
	/// e no dia em que alguem trocar uma das artes a bancada reprova ANTES de a cor sair errada em jogo.
	///
	/// A tinta MULTIPLICA aqui (`Modulate`, o `color = rgb()` do `EffectLayer.dm:32`): somar num cinza de
	/// 192 estoura tudo pro branco, e multiplicar uma arte JA colorida sujaria o desenho.
	/// ====================================================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR: ponha uma tinta na entrada `Deus` de `Catalogo.Coladas` -- caem a
	/// linha do `ssg` e a invariante do fim. Troque o corte `Ordem >= 20` por `>= 30` -- o `blue` sai com
	/// a folha laranja e cai. Apague o ramo `GodKiRose` -- caem as tres do Rose e a contagem por folha.
	/// </summary>
	private void AsColadasNoCatalogoInteiro()
	{
		const string Verde = "6eff8c", Rosa = "ff7ac6";

		// ============================ A ESPERADA E RECALCULADA, E DE PROPOSITO POR EXTENSO ============================
		// Mesma disciplina da varredura da folha de aura: perguntar ao `Catalogo.Coladas` e conferir a
		// resposta contra ela mesma aprovaria "devolve vazio sempre". O corte divino esta escrito
		// `d.Ordem >= 20` e nao `OrdemDoKiSobreOSuperSaiyajin` pelo mesmo motivo -- se a constante do Core
		// mudar de valor, as duas contas tem que DISCORDAR e nao andar juntas.
		// ========================================================================================================
		var porFolha = new Dictionary<FolhaColada, int>();
		int comColada = 0, semColada = 0;
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			Colada[] esperada = d.Id == Jandirus.Core.Forms.Catalogo.IdBase ? [] : d.Linha switch
			{
				LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal =>
					[new(FolhaColada.PoderLendario), new(FolhaColada.Ameacadora, Verde)],
				LinhaDeForma.GodKi => d.Ordem >= 20
					? [new(FolhaColada.DeusAzul)] : [new(FolhaColada.Deus)],
				LinhaDeForma.GodKiRose => d.Ordem >= 20
					? [new(FolhaColada.Ameacadora, Rosa)] : [new(FolhaColada.Deus)],
				_ => [],
			};

			Colada[] deu = Jandirus.Core.Forms.Catalogo.Coladas(d);
			Conferir(deu.Length == esperada.Length,
					 $"`{d.Id}` ({d.Linha}): {esperada.Length} colada(s) (deu {deu.Length})");
			if (deu.Length != esperada.Length) continue;

			for (int i = 0; i < esperada.Length; i++)
			{
				Conferir(deu[i].Folha == esperada[i].Folha,
						 $"`{d.Id}`: a colada {i} e `{esperada[i].Folha}` (deu `{deu[i].Folha}`)");
				Conferir(deu[i].Tinta == esperada[i].Tinta,
						 $"`{d.Id}`: e ela "
					   + (esperada[i].Tinta == null ? "NAO se pinta" : $"e pintada de #{esperada[i].Tinta}")
					   + $" (deu {deu[i].Tinta ?? "sem tinta"})");
				porFolha[deu[i].Folha] = porFolha.GetValueOrDefault(deu[i].Folha) + 1;
			}
			if (esperada.Length > 0) comColada++; else semColada++;
		}

		_passos.Add($"  --     coladas: {comColada} formas colam alguma coisa, {semColada} nao colam nada");
		_passos.Add("  --     por folha: "
				  + string.Join(", ", porFolha.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")));

		// AS QUATRO FOLHAS TEM QUE APARECER. Uma folha com zero formas e arte importada e MORTA -- foi
		// exatamente o que a `LSSJpowerz`, a `FieryGod` e a aura do Rose foram por anos neste projeto. E
		// a contagem tambem e a rede contra o oposto do defeito de cima: um `Coladas` que devolvesse
		// sempre a mesma folha passaria em muitas linhas acima se a esperada tambem colapsasse.
		Conferir(porFolha.Count == Enum.GetValues<FolhaColada>().Length,
				 $"o catalogo usa as {Enum.GetValues<FolhaColada>().Length} folhas coladas ({porFolha.Count})");
		Conferir(comColada > 0 && semColada > 0,
				 $"e colar e uma decisao dos dois lados ({comColada} colam, {semColada} nao)");

		// ============================ E QUEM **NAO** COLA ESTA NOMEADO ============================
		// "semColada > 0" sozinho e fraco: uma linha inteira que passasse a colar por engano ainda
		// deixaria alguem de fora e a contagem continuaria verde. As linhas de baixo dizem QUEM tem que
		// ficar de fora, e elas sao a tabela do dono lida pelo avesso -- ele listou quatro grupos, e
		// tudo o que nao esta neles nao cola nada.
		//
		// A escada Saiyajin e a mais importante das quatro: ela e a maior linha do jogo e a mais provavel
		// de ganhar uma colada "por simetria" com o Legendary.
		// ======================================================================================
		foreach (LinhaDeForma linha in new[]
				 { LinhaDeForma.Saiyajin, LinhaDeForma.Futuro, LinhaDeForma.Mistico,
				   LinhaDeForma.UltraInstinct, LinhaDeForma.UltraEgo, LinhaDeForma.Oozaru })
		{
			int coladas = Jandirus.Core.Forms.Catalogo.Todas.Where(d => d.Linha == linha)
				.Sum(d => Jandirus.Core.Forms.Catalogo.Coladas(d).Length);
			Conferir(coladas == 0,
					 $"a linha {linha} NAO cola nada -- a tabela do dono nao a lista ({coladas})");
		}

		// ============================ A INVARIANTE DA TINTA, NO CATALOGO INTEIRO ============================
		// Uma frase so, cobrada em todas as coladas de todas as formas: **so a cinza se pinta**. Ela pega
		// os dois lados do enunciado do dono de uma vez -- tinta na `god`/`god blue` (que sujaria arte ja
		// colorida) e cinza SEM tinta (que sairia um borrao cinza no meio do corpo).
		// ================================================================================================
		int erradas = 0;
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
			foreach (Colada c in Jandirus.Core.Forms.Catalogo.Coladas(d))
				if ((c.Folha == FolhaColada.Ameacadora) != (c.Tinta != null)) erradas++;
		Conferir(erradas == 0,
				 $"em TODO o catalogo, so a `god - grey` leva tinta e ela NUNCA fica sem ({erradas} fora da regra)");

		// E AS DUAS CORES SAO AS DUAS, e nao uma. O verde e literal do DM (`EffectLayer.dm:31-32`); o
		// rosa nao existe la e e divergencia declarada (ver `Catalogo.Coladas`).
		string[] tintas = [.. Jandirus.Core.Forms.Catalogo.Todas
			.SelectMany(d => Jandirus.Core.Forms.Catalogo.Coladas(d))
			.Where(c => c.Tinta != null).Select(c => c.Tinta!).Distinct().Order()];
		Conferir(tintas.Length == 2 && tintas.Contains(Verde) && tintas.Contains(Rosa),
				 $"e as tintas coladas do jogo sao DUAS -- #{Verde} (Legendary) e #{Rosa} (Rose) "
			   + $"({string.Join(", ", tintas.Select(t => "#" + t))})");

		// --- A ARTE: importada, carregando, e do TIPO que a regra da tinta pressupoe ---
		//
		// `cinza` aqui e a pergunta "esta folha PODE ser pintada?", medida pixel a pixel. Um desenho
		// cinza nao tem cor propria pra perder; um desenho colorido tem, e multiplicar cor nele mistura
		// as duas -- que e o defeito que o dono quer impedido.
		(FolhaColada Folha, bool Cinza, string OQue)[] arte =
		[
			(FolhaColada.Ameacadora,    true,  "e CINZA -- e e por isso que ela e a unica que se pinta"),
			(FolhaColada.Deus,          false, "ja vem LARANJA (`gkoverlay`, AuraObject.dm:14)"),
			(FolhaColada.DeusAzul,      false, "ja vem AZUL (`sgkoverlay`, AuraObject.dm:16)"),
			(FolhaColada.PoderLendario, false, "ja vem no verde-limao da fagulha"),
		];

		foreach ((FolhaColada f, bool deveSerCinza, string oque) in arte)
		{
			string tres = ColadasDeForma.CaminhoDa(f);
			Conferir(ColadasDeForma.Existe(f), $"`{f}` esta IMPORTADA ({tres.GetFile()})");

			var frames = ResourceLoader.Load<SpriteFrames>(tres);
			Conferir(frames != null && frames.GetAnimationNames().Length > 0,
					 $"`{f}` CARREGA e tem animacao ({frames?.GetAnimationNames().Length ?? 0})");

			(Color Cor, int Pixels)[] tons = TonsOpacosDe(PngDe(tres));
			Conferir(tons.Length > 0, $"`{f}`: o PNG dela abre pra a medicao ({PngDe(tres).GetFile()})");
			if (tons.Length == 0) continue;

			int opacos = tons.Sum(t => t.Pixels);
			// "CINZA" E UMA MEDIDA E NAO UM OLHAR: canal maximo menos canal minimo, com folga de 6 pra
			// o serrilhado da conversao do `.dmi`. O preto do contorno conta como cinza e nao atrapalha
			// -- ele tambem nao tem cor pra perder.
			int cinzas = tons.Where(t => Mathf.Max(t.Cor.R8, Mathf.Max(t.Cor.G8, t.Cor.B8))
									   - Mathf.Min(t.Cor.R8, Mathf.Min(t.Cor.G8, t.Cor.B8)) <= 6)
							 .Sum(t => t.Pixels);
			float pct = 100f * cinzas / opacos;
			Color dom = tons[0].Cor;
			_passos.Add($"  --     `{tres.GetFile()}`: {opacos} px opacos, {tons.Length} tons, "
					  + $"dominante #{dom.ToHtml(false)}, cinza {pct:0.#}%");

			Conferir(deveSerCinza ? pct >= 99f : pct <= 20f,
					 $"`{f}` {oque} (cinza {pct:0.#}%)");
		}

		// E AS DUAS JA COLORIDAS SAO CORES OPOSTAS, senao "ja vem colorida" nao distinguiria uma da
		// outra: o corte `ssj==0 && lssj==0` do `godki.dm:265` existe justamente pra escolher entre elas.
		Color quente = TonsOpacosDe(PngDe(ColadasDeForma.FolhaDeus)) is { Length: > 0 } tq
					   ? tq[0].Cor : Colors.Black;
		Color fria = TonsOpacosDe(PngDe(ColadasDeForma.FolhaDeusAzul)) is { Length: > 0 } tf
					 ? tf[0].Cor : Colors.Black;
		Conferir(quente.R > quente.B + 0.3f,
				 $"a `god` e QUENTE (#{quente.ToHtml(false)}: vermelho acima do azul)");
		Conferir(fria.B > fria.R + 0.3f,
				 $"e a `god blue` e FRIA (#{fria.ToHtml(false)}: azul acima do vermelho)");

		// ============================ E A MULTIPLICACAO TEM QUE SOBRAR COR ============================
		// A tinta que multiplica falha de dois jeitos silenciosos: escura demais devolve quase preto
		// (um borrao no corpo) e clara demais devolve quase branco (o mesmo brilho pras duas linhas).
		// Aqui o resultado e CALCULADO sobre o cinza medido, e o que se cobra e que ele tenha corpo E
		// matiz -- e nao que o hexa escrito seja bonito.
		// ==========================================================================================
		Color cinzaDom = TonsOpacosDe(PngDe(ColadasDeForma.FolhaAmeacadora)) is { Length: > 0 } tc
						 ? tc[0].Cor : Colors.White;
		foreach ((string hexa, string quem, bool verde) in
				 new[] { (Verde, "Legendary", true), (Rosa, "Rose", false) })
		{
			var t = new Color(hexa);
			Color deu = EmMultiplicacao(cinzaDom, t);
			float maior = Mathf.Max(deu.R, Mathf.Max(deu.G, deu.B));
			float menor = Mathf.Min(deu.R, Mathf.Min(deu.G, deu.B));
			_passos.Add($"  --     #{hexa} x #{cinzaDom.ToHtml(false)} ({quem}) = #{deu.ToHtml(false)}");

			Conferir(maior >= 0.25f && menor <= 0.85f,
					 $"a colada do {quem} nao vira nem borrao preto nem clarao branco (#{deu.ToHtml(false)})");
			Conferir(verde ? deu.G > deu.R + 0.15f && deu.G > deu.B + 0.15f
						   : deu.R > deu.G + 0.15f && deu.B > deu.G + 0.1f,
					 $"e ela sai {(verde ? "VERDE" : "ROSA")} de verdade (#{deu.ToHtml(false)})");

			// ============================ E OS DOIS MODOS DO UNIFORM NAO SERVEM AQUI ============================
			// A pergunta apareceu no dia em que a tinta foi consertada: se ela nao chegava, por que nao
			// manda-la pelo `tinta` do shader como faz o resto da pilha? Porque os DOIS modos que existem la
			// erram esta folha -- e o numero diz isso melhor que o argumento.
			//
			// A folha e cinza CLARO (o tom dominante e 192, 54,9% dos pixels opacos), e e essa altura que
			// decide tudo: somar 110 em 192 ja estoura, e a luminancia de 0,75 poe o ganho do matiz em 1,5x,
			// que satura o canal forte da tinta. As duas constantes (`6eff8c`, `ff7ac6`) foram
			// DIMENSIONADAS pra multiplicar -- o cabecalho de `Catalogo.VerdeDaAmeaca` ja fazia a conta
			// `192 x 110/255 = 83` --, e nenhum dos dois modos entrega isso.
			//
			// AS DUAS CHECAGENS SAO SOBRE O QUE SOBROU DE COR, e nao sobre um hexa esperado: cinza tem
			// saturacao 0, e cinza e literalmente o defeito que o dono viu.
			// ====================================================================================================
			Color naSoma = EmSoma(cinzaDom, t), noMatiz = EmMatiz(cinzaDom, t);
			_passos.Add($"  --       ... a mesma tinta em SOMA = #{naSoma.ToHtml(false)} "
					  + $"(sat {Saturacao(naSoma):0.00}), em MATIZ = #{noMatiz.ToHtml(false)} "
					  + $"(sat {Saturacao(noMatiz):0.00}), MULTIPLICANDO (sat {Saturacao(deu):0.00})");

			Conferir(Saturacao(naSoma) <= 0.02f,
					 $"a SOMA estouraria esta folha pro BRANCO (#{naSoma.ToHtml(false)}, sat "
				   + $"{Saturacao(naSoma):0.00}) -- e por isso que a colada nao usa `tinta_modo` 0");
			Conferir(Saturacao(noMatiz) <= Saturacao(deu) - 0.15f,
					 $"e o MATIZ lavaria a cor (#{noMatiz.ToHtml(false)}, sat {Saturacao(noMatiz):0.00} "
				   + $"contra {Saturacao(deu):0.00} multiplicando) -- nem o `tinta_modo` 1");
		}
	}

	// =====================================================================
	// 3a-ter-B3. A COLADA MEDIDA NA CAMADA -- a cor que chega e o dono do relogio
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ESTE BLOCO EXISTE ============================
	/// Dois defeitos VISIVEIS atravessaram esta bancada com ela verde -- o brilho cinza em vez de verde
	/// e o slideshow parado --, e o motivo dos dois e o mesmo: as checagens de colada perguntavam ao
	/// CATALOGO (que folha, que hexa, quantas camadas) e a ARTE (existe? importou?). Nenhuma delas
	/// perguntou **que cor chega na camada** nem **em que ritmo o quadro dela anda**, que sao as duas
	/// coisas que o dono ve.
	///
	/// Este bloco monta um boneco de verdade -- corpo, roupa, cabelo, olho e rabo -- e mede as duas no
	/// node, do lado de fora. Ele nao le hexa nenhum do catalogo pra decidir o que esperar: le a ARTE e
	/// a LINHA da forma.
	///
	/// ============================ E MEDE A OUTRA METADE, QUE E A QUE SE ESQUECE ============================
	/// O conserto do slideshow deu relogio proprio a colada. A metade que ninguem escreve o teste e a
	/// oposta: cabelo, roupa e rabo tem que CONTINUAR no relogio do corpo. Sem isso, o conserto de hoje
	/// vira o bug de amanha -- uma camisa uma fase adiantada, um passo que a perna nao esta dando.
	///
	/// As duas metades sao a MESMA medicao aqui (ver <see cref="ODonoDoRelogioDeCadaCamada"/>), e tem que
	/// ser: elas so significam alguma coisa uma contra a outra.
	/// ==================================================================================
	///
	/// O BONECO E PROPRIO (o mesmo idioma do `OCorpoInchado`), entao este bloco roda no meio da bancada
	/// sem perturbar o corpo vivo nem as medidas de pose dele.
	/// </summary>
	private void AColadaMedidaNaCamada()
	{
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados)) { Conferir(false, "o catalogo visual pra o boneco da colada"); return; }
		var cat = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

		var boneco = new CharacterVisual { Name = "BonecoDaColada" };
		AddChild(boneco);
		try
		{
			// COM ROUPA DE PROPOSITO, e e a peca que o dono citou sem saber: o `ClothesSaiyanSuit` tem 8
			// quadros de idle contra os 4 do corpo, e foi ela que DISFARCOU o defeito ("a roupa n muda
			// nada"). Uma camada com contagem diferente da do corpo e a unica que prova que a sincronia e
			// por FASE e nao por indice copiado.
			var ficha = new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku" };
			ficha.Roupa.Add(new Jandirus.Core.Appearance.PecaDeRoupa(RoupaDoBoneco));
			boneco.Vestir(cat, ficha, "Saiyan", "Male");
			boneco.MostrarRabo(true);
			Conferir(boneco.TemCabeloDeTeste && boneco.RaboVisivelDeTeste,
					 "o boneco da colada tem corpo, roupa, cabelo e rabo -- senao a metade de baixo mede o vazio");

			// O CAMINHO DO CORPO, PERGUNTADO AO CATALOGO e nao cravado: e por ele que a medicao acha o
			// node do corpo na arvore pra ler o ciclo DELE.
			_folhaDoCorpo = cat.CorpoSprite(ficha, "Saiyan", "Male");

			ATintaQueChegaNaCamada(boneco);
			APoseEADirecaoDaColada(boneco);
			ODonoDoRelogioDeCadaCamada(boneco);
			AsTresChecagensReprovamODefeito(boneco);
		}
		finally { boneco.QueueFree(); }
	}

	/// <summary>A peca de roupa do boneco desta bancada -- ver o bloco em <see cref="AColadaMedidaNaCamada"/>.</summary>
	private const string RoupaDoBoneco = "res://Assets/Sprites/Clothes/ClothesSaiyanSuit.tres";

	/// <summary>A folha do CORPO do boneco, resolvida pelo catalogo em <see cref="AColadaMedidaNaCamada"/>.</summary>
	private string _folhaDoCorpo = "";

	// ---------------------------------------------------------------------
	// FAMILIA 1 -- A TINTA QUE CHEGA NA CAMADA (e a cobertura, no mesmo laco)
	// ---------------------------------------------------------------------
	/// <summary>
	/// ============================ A PERGUNTA E "QUE COR SAI DESTA CAMADA" ============================
	/// O <see cref="AsColadasNoCatalogoInteiro"/> ja cobra o catalogo, e ele passou VERDE enquanto o dono
	/// via cinza: o hexa estava certo, o `Modulate` estava certo no node, e o material descartava tudo.
	/// Entao aqui nao se le catalogo. Le-se o `Modulate` que ficou NA CAMADA (o canal por onde a tinta
	/// chega ao shader -- ver <see cref="AsDuasContasSaoAsDoShader"/>), multiplica-se pelo tom medido na
	/// ARTE, e cobra-se a cor que sai.
	///
	/// ============================ E POR FORMA DE COR, NAO POR HEXA ============================
	/// O pedido do dono foi *"no wrathfull e nas formas de lssj o `god - grey.png` ta CINZA ainda e n
	/// verde"* -- ele pediu VERDE, e nao `#53c069`. Uma trava por igualdade de hexa e mais apertada e
	/// vale MENOS: ela reprova o dia em que alguem escurecer o verde de proposito, e continua aprovando
	/// um `#808080` se ele for o hexa escrito. "G acima de R e de B com folga" e a afirmacao que
	/// corresponde ao que se ve: aceita qualquer verde e recusa qualquer cinza.
	///
	/// E A SATURACAO E COBRADA NAS QUATRO FOLHAS: cinza e literalmente o defeito relatado, e e o unico
	/// modo de as quatro falharem pelo MESMO motivo (foi o que aconteceu -- o material descartava o
	/// `modulate` de todas).
	/// ================================================================================================
	///
	/// COMO REPROVA: apague a linha `s.Modulate = ...` do `ColadasDaForma` -- caem as linhas verdes do
	/// Legendary inteiro e as rosas do Rose (a folha e cinza puro, e cinza vezes branco continua cinza).
	/// Ponha tinta na entrada `Deus` do `Catalogo.Coladas` -- caem o "NAO se pinta" e o "sai identico a
	/// arte". Troque o verde por um cinza claro -- a forma de cor cai mesmo com a saturacao raspando.
	/// </summary>
	private void ATintaQueChegaNaCamada(CharacterVisual boneco)
	{
		// O CAMINHO -> SIMBOLO, pelo MESMO tradutor que o `CharacterVisual` usou pra montar a camada:
		// assim esta bancada tambem cobra a traducao -- uma folha trocada no `ColadasDeForma.CaminhoDa`
		// aparece aqui como "folha desconhecida" em vez de passar batida.
		var simboloDoCaminho = new Dictionary<string, FolhaColada>();
		foreach (FolhaColada f in Enum.GetValues<FolhaColada>())
			simboloDoCaminho[ColadasDeForma.CaminhoDa(f)] = f;

		// O TOM DE CADA ARTE, MEDIDO UMA VEZ. E a arte que manda: a `god - grey` nao tem cor propria pra
		// perder (por isso ela e a unica que se pinta) e as outras tres tem (por isso nao se pintam).
		var tomDaArte = new Dictionary<FolhaColada, Color>();
		foreach (FolhaColada f in Enum.GetValues<FolhaColada>())
		{
			(Color Cor, int Pixels)[] tons = TonsOpacosDe(PngDe(ColadasDeForma.CaminhoDa(f)));
			Conferir(tons.Length > 0, $"a arte da folha `{f}` abre pra medir a cor que sai da camada");
			tomDaArte[f] = tons.Length > 0 ? tons[0].Cor : Colors.White;
		}

		int formas = 0, camadas = 0, pintadas = 0, cruas = 0;
		var vistas = new HashSet<FolhaColada>();
		var porCor = new Dictionary<string, int>();

		// POR CONJUNTO, e nao por lista escrita a mao: quem manda no laco e o proprio catalogo. Um degrau
		// divino novo nasce com colada sozinho (a derivacao e por Linha+Ordem) e cai aqui na mesma rodada
		// em que nascer, sem ninguem lembrar de acrescentar uma linha nesta bancada.
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			Colada[] quer = Jandirus.Core.Forms.Catalogo.Coladas(d);
			if (quer.Length == 0) continue;
			formas++;

			boneco.ColadasDaForma(d);
			(string Folha, Color Tinta)[] tem = boneco.ColadasNoCorpoDeTeste;

			// AS CAMADAS NASCERAM -- e esta e a cobertura de ARTE medida do jeito que importa. O
			// `ColadasDaForma` SALTA a camada cuja folha nao carrega (`frames == null`, com um
			// `PushWarning` que ninguem le): arte faltando, nao importada ou com o caminho torto some
			// aqui como camada a menos, em vez de virar uma folha sem pixel na tela.
			Conferir(tem.Length == quer.Length,
					 $"`{d.Id}`: as {quer.Length} camada(s) coladas NASCERAM no boneco (deu {tem.Length})");
			if (tem.Length != quer.Length) continue;

			for (int i = 0; i < tem.Length; i++)
			{
				if (!simboloDoCaminho.TryGetValue(tem[i].Folha, out FolhaColada f))
				{
					Conferir(false, $"`{d.Id}`: a camada {i} carrega uma folha conhecida "
								  + $"(deu `{tem[i].Folha.GetFile()}`)");
					continue;
				}
				camadas++;
				vistas.Add(f);

				Color arte = tomDaArte[f];
				Color saiu = EmMultiplicacao(arte, tem[i].Tinta);

				// ============================ CINZA E O DEFEITO, E ELE E COBRADO EM TODAS ============================
				// Uma frase so pras quatro folhas, e ela e a queixa do dono escrita como medida. Uma colada
				// que saia sem cor esta errada mesmo com o hexa do catalogo certo -- foi exatamente o estado
				// em que o jogo ficou meses.
				// ====================================================================================================
				Conferir(Saturacao(saiu) >= 0.20f,
						 $"`{d.Id}`/`{f}`: a camada sai COM COR (#{saiu.ToHtml(false)}, sat "
					   + $"{Saturacao(saiu):0.00}) -- cinza e o defeito relatado");

				if (f == FolhaColada.Ameacadora)
				{
					// A UNICA QUE SE PINTA, e a cor dela vem da LINHA da forma -- nao de uma lista de ids.
					pintadas++;
					bool lendaria = d.Linha is LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal;
					bool rose = d.Linha == LinhaDeForma.GodKiRose;
					Conferir(lendaria || rose,
							 $"`{d.Id}`: quem usa a folha cinza e linha Legendary ou Rose (deu {d.Linha})");

					Conferir(!tem[i].Tinta.IsEqualApprox(Colors.White),
							 $"`{d.Id}`: a folha cinza CHEGOU TINGIDA na camada "
						   + $"(#{tem[i].Tinta.ToHtml(false)}; branco seria a tinta perdida no caminho)");

					if (lendaria)
					{
						porCor["VERDE"] = porCor.GetValueOrDefault("VERDE") + 1;
						Conferir(EhVerde(saiu),
								 $"`{d.Id}`: e ela sai VERDE -- G acima de R e de B com folga "
							   + $"(#{saiu.ToHtml(false)}: R{saiu.R8} G{saiu.G8} B{saiu.B8})");
					}
					else if (rose)
					{
						porCor["ROSA"] = porCor.GetValueOrDefault("ROSA") + 1;
						Conferir(EhRosa(saiu),
								 $"`{d.Id}`: e ela sai ROSA -- R e B acima do G com folga "
							   + $"(#{saiu.ToHtml(false)}: R{saiu.R8} G{saiu.G8} B{saiu.B8})");
					}
					continue;
				}

				// ============================ E AS OUTRAS TRES NAO PODEM LEVAR TINTA NENHUMA ============================
				// "Sem tinta" e "com tinta branca" dao o mesmo pixel hoje e sao afirmacoes diferentes. A
				// segunda linha e a que importa -- o pixel tem que sair IDENTICO a arte --, e e a unica que
				// reprova uma tinta quase branca (`#f0f0f0`), que passaria na primeira por aproximacao e
				// sujaria a arte de leve pra sempre.
				// ====================================================================================================
				cruas++;
				Conferir(tem[i].Tinta.IsEqualApprox(Colors.White),
						 $"`{d.Id}`/`{f}`: a arte JA vem colorida, entao a camada NAO se pinta "
					   + $"(deu #{tem[i].Tinta.ToHtml(false)})");
				Conferir(saiu.IsEqualApprox(arte),
						 $"`{d.Id}`/`{f}`: e o pixel sai identico a arte (#{saiu.ToHtml(false)} contra "
					   + $"#{arte.ToHtml(false)})");

				string cor = f switch
				{
					FolhaColada.Deus => "QUENTE",
					FolhaColada.DeusAzul => "FRIA",
					_ => "LIMAO",
				};
				porCor[cor] = porCor.GetValueOrDefault(cor) + 1;
				Conferir(f switch
				{
					FolhaColada.Deus => EhQuente(saiu),
					FolhaColada.DeusAzul => EhFria(saiu),
					// A FAGULHA: `#d3ff26`, verde-limao. A forma dela e o G bem acima do B, e NAO o
					// `EhVerde` -- ela passa nele raspando (G-R = 0,17), o que a tornaria indistinguivel da
					// cinza tingida de verde, que e a camada ao lado dela no MESMO Legendary.
					_ => saiu.G > saiu.B + 0.30f,
				}, $"`{d.Id}`/`{f}`: e a cor propria dela e {cor} (#{saiu.ToHtml(false)})");
			}
		}

		_passos.Add($"  --     na camada: {formas} formas coladas, {camadas} camadas -- "
				  + $"{pintadas} pintadas, {cruas} cruas");
		_passos.Add("  --     por forma de cor: "
				  + string.Join(", ", porCor.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")));

		// ============================ A COBERTURA, FECHADA POR CONTAGEM ============================
		// As linhas de cima so existem pras formas que o laco visitou. Estas quatro dizem que o laco
		// visitou TODAS -- e sao elas que impedem o modo de falhar classico da bancada por conjunto: um
		// `Coladas` que devolvesse vazio pra tudo nao deixaria UMA linha vermelha acima.
		// ======================================================================================
		int declaradas = Jandirus.Core.Forms.Catalogo.Todas
			.Count(x => Jandirus.Core.Forms.Catalogo.Coladas(x).Length > 0);
		Conferir(formas == declaradas && formas > 0,
				 $"toda forma que DECLARA colada foi medida na camada ({formas} de {declaradas})");
		Conferir(vistas.Count == Enum.GetValues<FolhaColada>().Length,
				 $"e as {Enum.GetValues<FolhaColada>().Length} folhas apareceram em alguma camada ({vistas.Count})");
		Conferir(pintadas > 0 && cruas > 0,
				 $"e os DOIS estados de tinta existem no jogo ({pintadas} pintadas, {cruas} cruas)");
		// CINCO FORMAS DE COR, e nao quatro: as duas tintas (verde e rosa) mais as tres artes que ja vem
		// pintadas (quente, fria, limao) -- a cinza aparece nas duas primeiras porque quem a pinta e a
		// LINHA. Um jogo em que todas as coladas sairem da mesma cor passaria em cada linha de forma de
		// cor acima e cairia aqui, que e o unico lugar que olha o conjunto.
		Conferir(porCor.Count == 5,
				 "e as CINCO formas de cor sairam todas -- verde e rosa (as duas tintas), quente, fria e "
			   + $"limao (as tres artes ja coloridas) ({porCor.Count}: {string.Join("/", porCor.Keys.Order())})");

		// ============================ E A CAMADA TEM QUE TER O MATERIAL QUE LE A TINTA ============================
		// O elo que faltava entre as duas metades: o `Modulate` so vira pixel porque o material da camada e
		// o `Personagem.gdshader`, que consome o `COLOR` (ver `AsDuasContasSaoAsDoShader`). Uma camada sem
		// material -- ou com outro shader -- deixaria as duas travas verdes e o pixel errado, que e o modo
		// de falhar desta familia inteira. Lido da arvore, sem campo novo no `CharacterVisual`.
		// ====================================================================================================
		boneco.ColadasDaForma(Jandirus.Core.Forms.Catalogo.Def("legendary"));
		int comShader = 0, semShader = 0;
		foreach (AnimatedSprite2D s in CamadasDaArvore(boneco))
		{
			if (s.SpriteFrames is not { } f || !simboloDoCaminho.ContainsKey(f.ResourcePath)) continue;
			if (s.Material is ShaderMaterial m && m.Shader?.ResourcePath == ShaderDoPersonagem) comShader++;
			else semShader++;
		}
		Conferir(comShader == 2 && semShader == 0,
				 "as duas camadas do Legendary usam o `Personagem.gdshader` -- o material que LE a tinta "
			   + $"({comShader} usam, {semShader} nao)");
	}

	/// <summary>O material de toda camada desta pilha. Cravado aqui de proposito: e o que a trava afirma.</summary>
	private const string ShaderDoPersonagem = "res://Assets/Shaders/Personagem.gdshader";

	/// <summary>
	/// A FOLGA DAS FORMAS DE COR. 0,15 em 0..1 (~38 de 255) e larga o bastante pra nenhum serrilhado de
	/// conversao do `.dmi` passar por matiz, e estreita o bastante pra aceitar qualquer verde que um
	/// artista chame de verde. Ver o bloco de <see cref="ATintaQueChegaNaCamada"/>.
	/// </summary>
	private const float FolgaDaCor = 0.15f;

	/// <summary>VERDE: o canal G acima dos outros dois com folga. O pedido do dono, escrito como medida.</summary>
	private static bool EhVerde(Color c) => c.G > c.R + FolgaDaCor && c.G > c.B + FolgaDaCor;

	/// <summary>
	/// ROSA: R e B acima do G. A folga do azul e menor (0,10) porque rosa e vermelho COM azul, e nao
	/// magenta puro -- exigir 0,15 nos dois recusaria o `#ff7ac6` do catalogo, que e a cor certa.
	/// </summary>
	private static bool EhRosa(Color c) => c.R > c.G + FolgaDaCor && c.B > c.G + 0.10f;

	/// <summary>QUENTE: vermelho bem acima do azul. A `god` laranja (`gkoverlay`, AuraObject.dm:14).</summary>
	private static bool EhQuente(Color c) => c.R > c.B + 0.30f;

	/// <summary>FRIA: azul bem acima do vermelho. A `god blue` (`sgkoverlay`, AuraObject.dm:16).</summary>
	private static bool EhFria(Color c) => c.B > c.R + 0.30f;

	/// <summary>
	/// AS CAMADAS DESTE BONECO, LIDAS DA ARVORE. Sem API nova no `CharacterVisual` -- o precedente e o
	/// `RoboDeColada.Filmar`, e a razao e a mesma: os nodes sao filhos do Visual e cada um ja carrega a
	/// folha, a animacao, o quadro e a meta `sync`. Nao ha o que perguntar a um campo.
	/// </summary>
	private static List<AnimatedSprite2D> CamadasDaArvore(CharacterVisual v)
	{
		var r = new List<AnimatedSprite2D>();
		foreach (Node n in v.GetChildren())
			if (n is AnimatedSprite2D s && IsInstanceValid(s) && s.SpriteFrames != null) r.Add(s);
		return r;
	}

	// ---------------------------------------------------------------------
	// FAMILIA 2 -- A POSE E A DIRECAO
	// ---------------------------------------------------------------------
	/// <summary>
	/// ============================ O QUE A COLADA HERDA DO CORPO ============================
	/// Herda POSE e DIRECAO, e so isso -- e o `VIS_INHERIT_ICON_STATE` do BYOND (`Overlays.dm:19`), que
	/// herda o NOME DO ESTADO. Tem que herdar: as tres folhas `god*` trazem as 24 animacoes do corpo, e
	/// um boneco de perfil com a aura de frente e o defeito que fez isto ser CAMADA e nao folha de aura.
	///
	/// SAO TRES AFIRMACOES, e a terceira e a que nenhuma checagem de instante pega:
	///   1. a camada nao SOME em pose nenhuma -- foi assim que a fagulha do Legendary apagava a cada soco
	///      e sumia inteira no voo (a escada do `Escolher` so tentava nome COM sufixo de direcao);
	///   2. quando a folha TEM a pose do corpo, ela toca exatamente aquela, e nao uma parecida;
	///   3. e quem tem as quatro direcoes TROCA de estado nas quatro. Uma camada travada numa direcao
	///      passa nas duas de cima em cada instante e esta errada o tempo todo.
	///
	/// A terceira e DERIVADA e nao listada: quem e cobrado por ela e quem tem as quatro animacoes na
	/// folha. A `LSSJpowerz` tem uma so (`default`) e nao e cobrada -- brilho nao tem lado.
	/// ======================================================================================
	///
	/// COMO REPROVA: tire os dois ultimos degraus do `CharacterVisual.Escolher` (as saidas sem direcao) --
	/// a fagulha volta a sumir em ataque e em voo e cai a afirmacao 1. Faca o `Aplicar` pular a colada --
	/// ela congela na pose antiga e caem a 2 e a 3.
	/// </summary>
	private void APoseEADirecaoDaColada(CharacterVisual boneco)
	{
		Jandirus.Core.World.Facing[] lados =
		[
			Jandirus.Core.World.Facing.South, Jandirus.Core.World.Facing.North,
			Jandirus.Core.World.Facing.East, Jandirus.Core.World.Facing.West,
		];

		// AS DUAS FORMAS QUE COBREM OS DOIS FEITIOS DE FOLHA: o Legendary instala as duas ao mesmo tempo
		// (a de 24 poses e a de uma so), e o Blue e o controle de folha unica direcional.
		foreach (string id in new[] { "legendary", "blue" })
		{
			if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } d) { Conferir(false, $"`{id}` existe"); continue; }
			boneco.ColadasDaForma(d);
			(string Folha, Color Tinta)[] tem = boneco.ColadasNoCorpoDeTeste;
			SpriteFrames?[] folhas = [.. tem.Select(t => ResourceLoader.Load<SpriteFrames>(t.Folha))];

			foreach ((Jandirus.Net.Protocol.Pose pose, bool andando, string oque) in new[]
			{
				(Jandirus.Net.Protocol.Pose.Normal, false, "parado"),
				(Jandirus.Net.Protocol.Pose.Normal, true, "andando"),
				(Jandirus.Net.Protocol.Pose.Atacando, false, "atacando"),
				(Jandirus.Net.Protocol.Pose.Voando, false, "voando"),
			})
			{
				var porCamada = new List<HashSet<string>>();
				for (int i = 0; i < tem.Length; i++) porCamada.Add([]);

				foreach (Jandirus.Core.World.Facing lado in lados)
				{
					boneco.SetPose(pose);
					boneco.SetMotion(lado, andando);
					string doCorpo = boneco.PoseDeTeste;
					string[] poses = boneco.PosesDasColadasDeTeste;
					if (poses.Length != tem.Length)
					{
						Conferir(false, $"[{id}/{oque}] as {tem.Length} camadas responderam a pose ({poses.Length})");
						continue;
					}

					for (int i = 0; i < poses.Length; i++)
					{
						// "" E O ESCONDIDO -- ver `PosesDasColadasDeTeste`. E o pisca-pisca da fagulha.
						Conferir(poses[i].Length > 0,
								 $"[{id}/{oque}/{lado}] a colada {i} (`{tem[i].Folha.GetFile()}`) NAO some "
							   + $"com o corpo em `{doCorpo}`");
						if (poses[i].Length == 0) continue;
						porCamada[i].Add(poses[i]);

						if (folhas[i] is not { } f) continue;
						if (f.HasAnimation(doCorpo))
							Conferir(poses[i] == doCorpo,
									 $"[{id}/{oque}/{lado}] a colada {i} anda na pose E na direcao do corpo "
								   + $"(`{doCorpo}` vs `{poses[i]}`)");
						else
							Conferir(f.HasAnimation(poses[i]),
									 $"[{id}/{oque}/{lado}] a colada {i} nao tem `{doCorpo}` e caiu numa pose "
								   + $"que a folha DELA tem (`{poses[i]}`)");
					}
				}

				// A TROCA POR DIRECAO, cobrada de quem tem as quatro -- derivado da folha, nao listado.
				string doCorpoAgora = boneco.PoseDeTeste;
				string fam = doCorpoAgora.Contains('_')
					? doCorpoAgora[..doCorpoAgora.LastIndexOf('_')] : doCorpoAgora;
				for (int i = 0; i < tem.Length; i++)
				{
					if (folhas[i] is not { } f) continue;
					if (!new[] { "south", "north", "east", "west" }.All(dir => f.HasAnimation($"{fam}_{dir}")))
						continue;
					Conferir(porCamada[i].Count == 4,
							 $"[{id}/{oque}] a colada {i} TROCA de estado nas quatro direcoes "
						   + $"(deu {porCamada[i].Count}: {string.Join(", ", porCamada[i].Order())})");
				}
			}
		}

		boneco.SetPose(Jandirus.Net.Protocol.Pose.Normal);
		boneco.SetMotion(Jandirus.Core.World.Facing.South, moving: false);
	}

	// ---------------------------------------------------------------------
	// FAMILIA 3 -- QUEM E O DONO DO RELOGIO DE CADA CAMADA
	// ---------------------------------------------------------------------
	/// <summary>
	/// ============================ AS DUAS METADES, NA MESMA MEDICAO ============================
	/// A pilha tem duas naturezas, e elas so significam alguma coisa uma contra a outra:
	///
	///   EFEITO (a colada) -- avanca pelos delays da folha DELA. Foi o defeito relatado: *"os overlays
	///   das formas god, e lssj, estao com baixo fps quando to PARADO ... parece um slide show"*.
	///   PARTE DO CORPO (roupa, cabelo, rabo, o proprio corpo) -- avanca pela fase do CORPO. Esta e a
	///   metade que ninguem escreve o teste, e sem ela o conserto de hoje vira o bug de amanha.
	///
	/// ============================ E A MEDIDA E RITMO, NAO INDICE ============================
	/// Conferir o indice do quadro seria reescrever o `_Process` dentro da bancada: o teste passaria a
	/// aprovar a implementacao por ela mesma. O que se mede aqui e quantas VEZES a camada troca de
	/// desenho numa janela de tempo -- a pergunta "de quem e o relogio", respondida por fora:
	///
	///     trocas = janela / ciclo x quadros        (o ciclo do CORPO pra parte, o DA FOLHA pro efeito)
	///
	/// A janela e um multiplo INTEIRO do ciclo do corpo de proposito: e o que faz a conta da PARTE fechar
	/// exata (N ciclos do corpo sao N ciclos da parte, seja qual for a contagem de quadros dela) enquanto
	/// o efeito ja deu muitas voltas. Medido no `default_south`: o corpo leva 3,300 s (`1,1,1,30`) e a
	/// `god - grey` 0,400 s -- na janela de 6,600 s a parte troca 2xN vezes e o efeito 66.
	///
	/// ============================ E O QUE DA VALOR A ISSO E A LINHA DA DISCORDANCIA ============================
	/// Onde as duas previsoes sao parecidas, acertar nao prova nada -- e o caso ANDANDO, em que os dois
	/// ciclos sao 0,800 s e QUALQUER implementacao passa (o *"quando ANDO elas voltam a andar em um frame
	/// rate bom"* do relato). Entao, quando as duas previsoes DISCORDAM, esta medicao cobra as duas
	/// coisas: que a camada bata com a previsao da familia DELA e que ela NAO bata com a da outra. E o que
	/// a torna capaz de reprovar a TROCA das duas naturezas, e nao so o congelamento.
	/// ======================================================================================================
	///
	/// COMO REPROVA CADA FAMILIA: sabote o `EhEfeito` pra `false` -- as coladas caem no `default_south`
	/// (medem ~4 trocas onde a previsao delas e 66) e passam raspando no `andando`, que e o que a linha da
	/// discordancia denuncia. Faca o `EhEfeito` devolver `true` pra todo mundo -- cai a roupa (16 trocas
	/// onde a previsao e 8) e cai o rabo. Zere o `_relogioSolto` no `Aplicar` -- caem as coladas em todas.
	/// </summary>
	private void ODonoDoRelogioDeCadaCamada(CharacterVisual boneco)
	{
		// O LEGENDARY POR SER O UNICO COM DUAS CAMADAS de feitios diferentes: a `god - grey` acompanha as
		// poses do corpo e a `LSSJpowerz` tem uma animacao so. As duas tem que andar no relogio DELAS, e
		// elas chegavam ao defeito por caminhos opostos (ver o bloco do `_Process`).
		boneco.ColadasDaForma(Jandirus.Core.Forms.Catalogo.Def("legendary"));
		var coladas = new HashSet<string>(boneco.ColadasNoCorpoDeTeste.Select(c => c.Folha));
		Conferir(coladas.Count == 2, $"o boneco tem as duas coladas do Legendary pra medir ({coladas.Count})");

		// O ROTULO CURTO E O QUE ENTRA EM CADA LINHA, e a explicacao fica no cabecalho da medicao: a
		// primeira versao levava a frase inteira pra dentro de cada `Conferir` e uma FALHA saia com 90
		// caracteres de contexto antes do que falhou.
		foreach ((Jandirus.Net.Protocol.Pose pose, bool andando, string tag, string porque) in new[]
		{
			// PARADO E O PIOR CASO e vem primeiro: o corpo segura o ultimo quadro 3,0 s dos 3,3 s do ciclo.
			(Jandirus.Net.Protocol.Pose.Normal, false, "PARADO",
			 "o caso do relato -- 3,300 s de ciclo no corpo contra 0,400 s na folha"),
			// ANDANDO E O CONTROLE: os dois ciclos sao 0,800 s, as previsoes empatam e nada discrimina.
			(Jandirus.Net.Protocol.Pose.Normal, true, "ANDANDO",
			 "o caso que ja funcionava, e por isso o que menos prova"),
			// ATACAR E O DEFEITO PELO AVESSO: o corpo tem UM quadro de 0,100 s e a colada tem quatro.
			(Jandirus.Net.Protocol.Pose.Atacando, false, "ATACANDO",
			 "o avesso -- aqui a colada corria RAPIDO demais, e ninguem tinha citado"),
			// VOAR PARADO tem o mesmo `1,1,1,30` do parado, e tambem nao tinha sido citado.
			(Jandirus.Net.Protocol.Pose.Voando, false, "VOANDO",
			 "parado no ar: o mesmo `1,1,1,30` do parado, e ninguem tinha citado"),
		})
		{
			boneco.SetPose(pose);
			boneco.SetMotion(Jandirus.Core.World.Facing.South, andando);
			ODonoDoRelogio(boneco, coladas, tag, porque);
		}

		boneco.SetPose(Jandirus.Net.Protocol.Pose.Normal);
		boneco.SetMotion(Jandirus.Core.World.Facing.South, moving: false);
	}

	/// <inheritdoc cref="ODonoDoRelogioDeCadaCamada"/>
	private void ODonoDoRelogio(CharacterVisual boneco, HashSet<string> coladas, string oque, string porque)
	{
		List<AnimatedSprite2D> camadas = CamadasDaArvore(boneco);

		// O CICLO DO CORPO, lido do node do CORPO -- achado pela folha que o catalogo resolveu.
		AnimatedSprite2D? corpo = camadas.FirstOrDefault(s => s.SpriteFrames!.ResourcePath == _folhaDoCorpo);
		if (corpo?.SpriteFrames is not { } folhaCorpo)
		{
			Conferir(false, $"[{oque}] o node do corpo esta na arvore pra dar o ciclo ({_folhaDoCorpo.GetFile()})");
			return;
		}

		double cicloCorpo = CicloDaFolha(folhaCorpo, corpo.Animation);
		if (cicloCorpo <= 0) { Conferir(false, $"[{oque}] o corpo tem ciclo pra medir (`{corpo.Animation}`)"); return; }

		// ============================ A JANELA E UM MULTIPLO INTEIRO DO CICLO DO CORPO ============================
		// Ser multiplo INTEIRO e o que faz a previsao da parte fechar exata: N ciclos do corpo sao N ciclos
		// da parte, seja qual for a contagem de quadros dela. Dois ciclos bastam parado (6,600 s), mas
		// ATACANDO o ciclo do corpo e 0,100 s -- dois dariam uma janela de 0,200 s, em que o efeito troca
		// duas vezes e nenhuma conta convence ninguem. Entao a janela cresce em ciclos INTEIROS ate passar
		// de dois segundos, e a exatidao da parte fica de pe do mesmo jeito.
		//
		// O PASSO e bem menor que o quadro mais curto de qualquer folha (0,100 s): com 0,005 s nao ha troca
		// que caiba entre duas amostras, entao a contagem e exata e nao amostral.
		// ======================================================================================================
		const double Passo = 0.005;
		int ciclos = Math.Max(2, (int)Math.Ceiling(2.0 / cicloCorpo));
		double janela = cicloCorpo * ciclos;
		int passos = (int)Math.Round(janela / Passo);

		var antes = new Dictionary<AnimatedSprite2D, int>();
		var trocas = new Dictionary<AnimatedSprite2D, int>();
		foreach (AnimatedSprite2D s in camadas) { antes[s] = s.Frame; trocas[s] = 0; }

		for (int k = 0; k < passos; k++)
		{
			boneco._Process(Passo);
			foreach (AnimatedSprite2D s in camadas)
			{
				if (!IsInstanceValid(s)) continue;
				if (s.Frame != antes[s]) trocas[s]++;
				antes[s] = s.Frame;
			}
		}

		_passos.Add($"  --     {oque} -- {porque}");
		_passos.Add($"  --       corpo em `{corpo.Animation}`, ciclo {cicloCorpo:0.000}s, "
				  + $"janela {janela:0.000}s ({ciclos} ciclos do corpo)");

		int partes = 0, efeitos = 0, discriminaram = 0;
		foreach (AnimatedSprite2D s in camadas)
		{
			if (s.SpriteFrames is not { } f || !s.Visible || !f.HasAnimation(s.Animation)) continue;
			bool efeito = coladas.Contains(f.ResourcePath);
			bool sync = s.GetMeta("sync", true).AsBool();
			int n = f.GetFrameCount(s.Animation);
			double proprio = CicloDaFolha(f, s.Animation);

			// AS DUAS PREVISOES, sempre as duas -- e a segunda que da sentido a primeira. PARTE em pose
			// EMPRESTADA congela no quadro 0 (`sync` falso): zero trocas, e e o certo.
			double comoParte = !sync || n <= 1 ? 0 : janela / cicloCorpo * n;
			double comoEfeito = n <= 1 || proprio <= 0 ? 0 : janela / proprio * n;
			double previsto = efeito ? comoEfeito : comoParte;
			int deu = trocas[s];
			string quem = f.ResourcePath.GetFile().GetBaseName();

			_passos.Add($"  --       {quem,-24} {(efeito ? "EFEITO" : sync ? "parte " : "parte*")} "
					  + $"`{s.Animation}` {n}q  trocou {deu}x  (previsto {previsto:0.#} como "
					  + $"{(efeito ? "efeito" : "parte")}, {(efeito ? comoParte : comoEfeito):0.#} como o outro)");

			Conferir(Math.Abs(deu - previsto) <= FolgaDeTrocas,
					 $"[{oque}] `{quem}` anda no relogio "
				   + (efeito ? "DELA (efeito de ki -- brilho por cima do boneco)"
							 : "do CORPO (e um pedaco do boneco -- quadro fora de fase e membro fora de lugar)")
				   + $": {deu} trocas contra {previsto:0.#} previstas");

			if (efeito) efeitos++; else partes++;

			// ============================ A LINHA DA DISCORDANCIA ============================
			// So vale quando as duas previsoes sao mesmo diferentes. Onde elas empatam (o caso ANDANDO), a
			// medicao nao tem o que dizer -- e dizer isso em voz alta e melhor que fingir que provou.
			// ================================================================================
			double outro = efeito ? comoParte : comoEfeito;
			if (Math.Abs(outro - previsto) <= Math.Max(2, previsto * 0.3)) continue;
			discriminaram++;
			Conferir(Math.Abs(deu - outro) > FolgaDeTrocas,
					 $"[{oque}] e `{quem}` NAO anda no relogio da outra natureza ({deu} trocas; o relogio "
				   + $"{(efeito ? "do corpo" : "proprio")} daria {outro:0.#})");
		}

		// NAO PODE MEDIR O VAZIO. Duas partes e duas coladas e o minimo do boneco montado la em cima --
		// uma camada que sumisse (ou uma folha que nao carregasse) tornaria o bloco inteiro inofensivo.
		Conferir(partes >= 2 && efeitos == 2,
				 $"[{oque}] havia o que medir dos DOIS lados ({partes} partes, {efeitos} coladas)");
		_passos.Add($"  --       {discriminaram} camada(s) nesta pose separam as duas naturezas"
				  + (discriminaram == 0 ? " -- aqui os dois relogios empatam e a pose nao prova nada" : ""));
	}

	// ---------------------------------------------------------------------
	// A FALSIFICACAO -- as tres checagens em cima do defeito, e nao so em cima do conserto
	// ---------------------------------------------------------------------
	/// <summary>
	/// ============================ UMA CHECAGEM VERDE NAO DIZ QUE ELA SABE FICAR VERMELHA ============================
	/// As tres familias acima passaram na primeira rodada. Isso significa duas coisas ao mesmo tempo -- que
	/// o jogo esta certo, ou que as checagens nao sabem reprovar --, e o historico deste arquivo pesa pro
	/// lado ruim: as checagens antigas de colada estavam TODAS verdes enquanto o dono via cinza e
	/// slideshow. Uma bancada que nunca foi vista vermelha e uma opiniao.
	///
	/// Entao aqui os DEFEITOS RELATADOS sao reproduzidos, e o que se cobra e que as regras de cima os
	/// recusem. Nao e o conserto sendo medido: e o medidor.
	///
	/// ============================ POR QUE ISTO FICA NO REPOSITORIO E NAO NUMA RODADA A MAO ============================
	/// O jeito classico e sabotar o codigo de producao, rodar, ver vermelho e desfazer -- e foi assim que
	/// as sessoes anteriores deste trabalho falsificaram as delas. So que a prova morre na hora em que o
	/// terminal fecha: o `EhEfeito` sabotado nao esta em lugar nenhum seis meses depois, quando alguem
	/// afrouxar a tolerancia de 1 troca pra 20 e a bancada continuar verde pra sempre.
	///
	/// Aqui o defeito e reproduzido POR DENTRO, sobre o mesmo boneco e com as mesmas funcoes de medida, e
	/// a exigencia e permanente: afrouxar a regra a ponto de ela aceitar o defeito reprova NESTE bloco.
	/// (Foi tambem o unico caminho honesto nesta rodada -- o `CharacterVisual.cs` e o `Core/Forms/Formas.cs`
	/// tinham sido escritos por outra sessao 3 e 5 minutos antes, e sabotar arquivo quente e o jeito de
	/// clobberar o trabalho alheio no `git` do desfazer.)
	/// ==========================================================================================================
	/// </summary>
	private void AsTresChecagensReprovamODefeito(CharacterVisual boneco)
	{
		// ============================ 1. O CINZA -- "o `god - grey.png` ta CINZA ainda e n verde" ============================
		// O defeito historico nao era um hexa errado: era a tinta NAO CHEGANDO (o material descartava o
		// `modulate`). Na camada, isso e exatamente o mesmo que uma tinta branca -- e branco e o neutro da
		// multiplicacao, entao o pixel sai o cinza cru da arte. Se a regra de cor aprovar isto, ela nao
		// serve pra nada.
		// ==============================================================================================================
		Color arte = TonsOpacosDe(PngDe(ColadasDeForma.FolhaAmeacadora)) is { Length: > 0 } tons
					 ? tons[0].Cor : Colors.White;
		Color perdida = EmMultiplicacao(arte, Colors.White);
		Conferir(!EhVerde(perdida) && !EhRosa(perdida) && Saturacao(perdida) < 0.20f,
				 $"a regra de cor RECUSA a tinta perdida (#{perdida.ToHtml(false)}, sat "
			   + $"{Saturacao(perdida):0.00}) -- e este e o pixel que o dono viu por meses");

		// E APROVA AS DUAS DE VERDADE, tiradas do catalogo e nao escritas aqui: sem esta linha a de cima
		// passaria com uma regra que recusa tudo, inclusive o verde certo.
		Colada[] doLegendary = Jandirus.Core.Forms.Catalogo.Coladas(Jandirus.Core.Forms.Catalogo.Def("legendary"));
		Colada[] doRose = Jandirus.Core.Forms.Catalogo.Coladas(Jandirus.Core.Forms.Catalogo.Def("rose"));
		Color verde = EmMultiplicacao(arte, new Color(doLegendary.First(c => c.Tinta != null).Tinta!));
		Color rosa = EmMultiplicacao(arte, new Color(doRose.First(c => c.Tinta != null).Tinta!));
		Conferir(EhVerde(verde) && EhRosa(rosa) && !EhVerde(rosa) && !EhRosa(verde),
				 $"e a MESMA regra separa o verde do rosa (#{verde.ToHtml(false)} e #{rosa.ToHtml(false)}) "
			   + "-- ela nao recusa tudo, e nao confunde as duas linhas uma com a outra");

		// ============================ 2. O SUMICO -- a fagulha apagando a cada soco ============================
		// A camada escondida e o estado em que a `LSSJpowerz` ficava em `attack_*` e `flight_*` antes dos
		// dois ultimos degraus do `Escolher`. A afirmacao "nao some" tem que enxergar isso.
		// ==================================================================================================
		boneco.ColadasDaForma(Jandirus.Core.Forms.Catalogo.Def("legendary"));
		var escondida = CamadasDaArvore(boneco)
			.FirstOrDefault(s => s.SpriteFrames!.ResourcePath == ColadasDeForma.FolhaPoderLendario);
		if (escondida == null) { Conferir(false, "a fagulha esta na arvore pra a falsificacao do sumico"); return; }

		escondida.Visible = false;
		string[] comSumico = boneco.PosesDasColadasDeTeste;
		escondida.Visible = true;
		string[] semSumico = boneco.PosesDasColadasDeTeste;
		Conferir(comSumico.Any(p => p.Length == 0) && semSumico.All(p => p.Length > 0),
				 "a leitura de pose ACUSA a camada escondida (o pisca-pisca da fagulha) e nao acusa a "
			   + $"visivel ({comSumico.Count(p => p.Length == 0)} sumida(s) contra "
			   + $"{semSumico.Count(p => p.Length == 0)})");

		// ============================ 3. O SLIDESHOW -- a linha deletada, reproduzida ============================
		// Aqui o defeito nao e simulado por analogia: e a LINHA que foi apagada do `_Process`, escrita de
		// novo. Ela reescalava o ciclo da folha pra caber no ciclo do corpo --
		//
		//     alvo = QuadroEm(f, s.Animation, fase * Ciclo(f, s.Animation))
		//
		// -- e o efeito, com o corpo parado, era 1,21 quadro por segundo em vez de 10. O laco abaixo roda o
		// `_Process` normal e, a cada passo, SOBRESCREVE o quadro da colada com o que a linha antiga daria.
		// Depois conta as trocas com o mesmo criterio da familia 3.
		//
		// SAO DUAS EXIGENCIAS, e a segunda vale tanto quanto a primeira: o medidor tem que reprovar (a
		// contagem longe da previsao do efeito) E o diagnostico tem que bater (a contagem EM CIMA da
		// previsao da parte). A segunda e a que impede "reprovou por qualquer motivo" de passar por prova.
		// ====================================================================================================
		boneco.SetPose(Jandirus.Net.Protocol.Pose.Normal);
		boneco.SetMotion(Jandirus.Core.World.Facing.South, moving: false);

		List<AnimatedSprite2D> camadas = CamadasDaArvore(boneco);
		AnimatedSprite2D? corpo = camadas.FirstOrDefault(s => s.SpriteFrames!.ResourcePath == _folhaDoCorpo);
		AnimatedSprite2D? doente = camadas
			.FirstOrDefault(s => s.SpriteFrames!.ResourcePath == ColadasDeForma.FolhaAmeacadora);
		if (corpo?.SpriteFrames is not { } folhaCorpo || doente?.SpriteFrames is not { } folhaDoente)
		{ Conferir(false, "o corpo e a colada cinza estao na arvore pra a falsificacao do slideshow"); return; }

		double cicloCorpo = CicloDaFolha(folhaCorpo, corpo.Animation);
		double proprio = CicloDaFolha(folhaDoente, doente.Animation);
		int n = folhaDoente.GetFrameCount(doente.Animation);
		if (cicloCorpo <= 0 || proprio <= 0 || n <= 1)
		{ Conferir(false, "ha ciclo dos dois lados pra a falsificacao do slideshow"); return; }

		const double Passo = 0.005;
		int ciclos = Math.Max(2, (int)Math.Ceiling(2.0 / cicloCorpo));
		double janela = cicloCorpo * ciclos;
		double t = 0;
		int trocas = 0, antes = doente.Frame;
		for (int k = 0; k < (int)Math.Round(janela / Passo); k++)
		{
			boneco._Process(Passo);
			t += Passo;
			// A LINHA ANTIGA. A fase e a do CORPO (o `_relogio`, zerado pelo `Aplicar` na troca de pose
			// logo acima), e o ciclo da folha e esticado pra caber nela.
			double fase = t % cicloCorpo / cicloCorpo;
			doente.Frame = QuadroDaFolhaEm(folhaDoente, doente.Animation, fase * proprio);
			if (doente.Frame != antes) trocas++;
			antes = doente.Frame;
		}

		double comoEfeito = janela / proprio * n;
		double comoParte = janela / cicloCorpo * n;
		_passos.Add($"  --     FALSIFICACAO: com a linha antiga de volta, a `god - grey` parada troca "
				  + $"{trocas}x na janela de {janela:0.000}s (o relogio DELA daria {comoEfeito:0.#})");
		Conferir(Math.Abs(trocas - comoEfeito) > FolgaDeTrocas,
				 $"a medicao de cadencia REPROVA o slideshow ({trocas} trocas contra {comoEfeito:0.#} "
			   + "previstas) -- e ela nao passaria verde no jogo que o dono relatou");
		Conferir(Math.Abs(trocas - comoParte) <= FolgaDeTrocas,
				 $"e o diagnostico BATE: o defeito e a colada andando no relogio do CORPO ({trocas} trocas "
			   + $"contra {comoParte:0.#}) -- reprovar por outro motivo nao seria prova de nada");

		// E O BONECO VOLTA AO NORMAL. Ele morre no `finally` de qualquer jeito, mas deixar um node com o
		// quadro escrito a mao e o tipo de rastro que a proxima medicao herda sem saber.
		boneco.ColadasDaForma(null);
	}

	/// <summary>
	/// EM QUE QUADRO A ANIMACAO ESTA no instante dado, respeitando a duracao de CADA quadro. Irma da
	/// <see cref="CicloDaFolha"/> e re-derivada pelo mesmo motivo -- ela existe pra a falsificacao poder
	/// reescrever a linha deletada do `_Process` sem depender do metodo privado que a substituiu.
	/// </summary>
	private static int QuadroDaFolhaEm(SpriteFrames f, string anim, double t)
	{
		int n = f.GetFrameCount(anim);
		if (n <= 1) return 0;
		double vel = Math.Max(f.GetAnimationSpeed(anim), 0.01);
		double acc = 0;
		for (int i = 0; i < n; i++)
		{
			acc += f.GetFrameDuration(anim, i) / vel;
			if (t < acc) return i;
		}
		return n - 1;
	}

	/// <summary>
	/// ============================ UMA TROCA DE FOLGA, E O `,05` NAO E FRESCURA ============================
	/// UMA troca porque a janela e exata pro relogio do CORPO (N ciclos) e nao pro da folha: a ultima troca
	/// do efeito cai de um lado ou do outro da borda conforme a fase do `_relogioSolto`, que ninguem zera.
	///
	/// E o `,05` porque a previsao e ponto flutuante: `6,6 / 0,4 x 4` da **65,99999999999999**, e o
	/// `god - grey` voando mediu 67. A distancia real e 1, a distancia calculada e 1,0000000000000142, e a
	/// primeira rodada desta bancada saiu com uma FALHA que nao existia. Reprovar por um bit de mantissa e
	/// pior que nao reprovar: manda alguem cacar um defeito no lugar errado.
	///
	/// NAO AFROUXA NADA: as distancias que estas linhas separam sao 8 contra 66, e 2 contra 8.
	/// ================================================================================================
	/// </summary>
	private const double FolgaDeTrocas = 1.05;

	/// <summary>
	/// A DURACAO DE UMA ANIMACAO, em segundos. Re-derivada aqui de proposito (o `CharacterVisual.Ciclo` e
	/// privado, e importa-lo seria conferir a implementacao contra ela mesma): soma a duracao de cada
	/// quadro dividida pela velocidade da animacao, que e o que o `.tres` guarda.
	/// </summary>
	private static double CicloDaFolha(SpriteFrames f, string anim)
	{
		if (!f.HasAnimation(anim)) return 0;
		double vel = Math.Max(f.GetAnimationSpeed(anim), 0.01);
		double total = 0;
		for (int i = 0; i < f.GetFrameCount(anim); i++) total += f.GetFrameDuration(anim, i) / vel;
		return total;
	}

	// =====================================================================
	// 3a-ter-C. O AZUL DO BLUE MEDIDO NO RESULTADO -- e nao na entrada
	// =====================================================================
	/// <summary>
	/// ============================ A PERGUNTA E "QUE COR SAI", E ELA TEM UMA RESPOSTA SO ============================
	/// O dono relatou o defeito duas vezes e dos dois lados -- *"aplicar azul por cima esta dando
	/// branco"* e *"o cabelo do Rose esta loiro"* --, e o catalogo estava CERTO nos dois casos: o hexa
	/// era o do DM. O que estava errado era a OPERACAO, e nenhuma checagem sobre o hexa escrito ve isso.
	///
	/// Entao aqui nao se confere tinta: confere-se o PIXEL QUE SAI. Os quatro tons dourados vem do
	/// arquivo (`Hair_GokuSSj.png`, e sao os mesmos quatro nas 57 variantes), a conta vem do shader (ver
	/// <see cref="EmMatiz"/>) e o que se cobra e a cor final de cada degrau do sombreado.
	///
	/// ============================ E O CONTROLE E A SOMA ============================
	/// Sem ele, "o matiz da azul" seria uma afirmacao sobre uma conta que ninguem questionou. Com ele, a
	/// bancada mostra POR QUE a regra existe: o `rgb(13,73,238)` do `SaiyanObjects.dm:18`, SOMADO nestes
	/// mesmos quatro tons, entrega branco chapado em dois deles. O defeito do dono, reproduzido a cada
	/// rodada.
	/// ============================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR: troque o `matiz: trocou` do `VestirCabeloDaForma` por `matiz:
	/// false` -- o boneco do `VarrerOCabeloNumBoneco` cai, e ESTE bloco explica o que o jogador veria.
	/// Devolva o `AzulDoCabeloDivino` ao `0d49ee` do DM e caem as linhas do azul, uma por tom.
	/// </summary>
	private void OAzulDoBlueNoCabeloLoiro()
	{
		// A FOLHA SAI DO RESOLVEDOR e nao de um caminho escrito: e a MESMA arte que o jogo poe na
		// cabeca do jogador (`CabelosDeForma.De`), e no dia em que a pasta mudar de nome a medida segue.
		string? tres = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Goku.tres", "SSj");
		Conferir(tres != null, "o resolvedor acha o cabelo de Super Saiyajin pra a medicao");
		if (tres == null) return;

		(Color Cor, int Pixels)[] tons = TonsOpacosDe(PngDe(tres));
		Conferir(tons.Length > 0, $"a folha `{tres.GetFile()}` abre ({PngDe(tres).GetFile()})");
		if (tons.Length == 0) return;

		_passos.Add($"  --     `{tres.GetFile()}`: {tons.Length} tons -- "
				  + string.Join(", ", tons.Select(t => $"#{t.Cor.ToHtml(false)}x{t.Pixels}")));

		// --- 1. A PREMISSA: a arte que a forma traz e DOURADA ---
		// Sem esta linha o bloco inteiro nao prova nada: se a folha fosse preta (como o penteado base),
		// a soma seria a operacao certa e o matiz e que estaria errado. E o dourado que muda a resposta.
		int dourados = tons.Count(t => t.Cor.R > 0.5f && t.Cor.G > 0.4f && t.Cor.B < t.Cor.G - 0.2f);
		Conferir(dourados == tons.Length,
				 $"os {tons.Length} tons da arte de SSJ sao DOURADOS -- e e isso que quebra a soma ({dourados})");
		Conferir(tons.Length >= 3,
				 $"e ela tem sombreado de verdade ({tons.Length} tons) -- um tom so nao mediria achatamento");

		// ============================ E A MESMA PALETA VALE PRA O DEGRAU DE CIMA ============================
		// O `blue_evolution` e o `rose2` nao vestem esta folha: eles usam o `USSj`, o penteado
		// intermediario. Medir os quatro aqui e falar deles seria trocar a arte no meio da frase -- entao
		// a bancada CONFERE que a paleta e a mesma (o USSj tem os mesmos quatro dourados mais uma faixa
		// verde de bandana). Se um dia a arte do USSj for repintada, esta linha cai antes das outras.
		// ================================================================================================
		string? doUssj = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Goku.tres", "USSj");
		(Color Cor, int Pixels)[] tonsUssj = doUssj != null ? TonsOpacosDe(PngDe(doUssj)) : [];
		int comuns = tons.Count(t => tonsUssj.Any(u => u.Cor.ToHtml(false) == t.Cor.ToHtml(false)));
		Conferir(doUssj != null && comuns == tons.Length,
				 $"e a folha do degrau de cima (`{doUssj?.GetFile() ?? "nenhuma"}`) tem os MESMOS "
			   + $"{tons.Length} tons ({comuns}) -- e o que autoriza medir os dois aqui");

		// --- 2. O RESULTADO, TOM A TOM ---
		void MedirMatiz(string id, string comoTemQueLer, Func<Color, bool> leCerto)
		{
			if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } d) { Conferir(false, $"`{id}` existe"); return; }
			if (Jandirus.Core.Forms.Catalogo.CorDoCabelo(d) is not { } hexa)
			{ Conferir(false, $"`{id}` tem tinta de cabelo"); return; }

			var tinta = new Color(hexa);
			Color[] saiu = [.. tons.Select(t => EmMatiz(t.Cor, tinta))];
			_passos.Add($"  --     `{id}` #{hexa} sobre o loiro -> "
					  + string.Join(" ", saiu.Select(c => "#" + c.ToHtml(false))));

			for (int i = 0; i < saiu.Length; i++)
				Conferir(leCerto(saiu[i]),
						 $"`{id}`: o tom #{tons[i].Cor.ToHtml(false)} sai {comoTemQueLer} "
					   + $"(#{saiu[i].ToHtml(false)})");

			// BRANCO E O DEFEITO NOMEADO PELO DONO. Um pixel so basta pra a queixa voltar, entao a
			// pergunta e feita em todos: "os tres canais altos" e o que ele viu.
			int brancos = saiu.Count(c => c.R > 0.85f && c.G > 0.85f && c.B > 0.85f);
			Conferir(brancos == 0, $"`{id}`: e NENHUM tom sai branco ({brancos} de {saiu.Length})");

			// E O SOMBREADO SOBREVIVE. Um matiz calibrado alto demais satura os quatro tons no mesmo
			// valor: a cor fica certa e o cabelo vira uma mancha chapada, sem os fios desenhados.
			int distintos = saiu.Select(c => c.ToHtml(false)).Distinct().Count();
			Conferir(distintos >= 3,
					 $"`{id}`: e os degraus do sombreado nao se fundem ({distintos} tons distintos "
				   + $"de {saiu.Length})");
		}

		// ============================ A PERGUNTA E POR RAZAO E NAO POR DIFERENCA ============================
		// A primeira versao destas linhas cobrava `c.B > c.G + 0.3` -- e ela REPROVAVA o Blue Evolution
		// com o codigo certo: o matiz MULTIPLICA a tinta pela luz do desenho, entao o tom mais escuro do
		// cabelo devolve o azul inteiro em escala menor (0,39 contra 0,12), e a diferenca cai junto com
		// o brilho enquanto a COR nao muda em nada. Margem fixa mede brilho; razao mede matiz.
		//
		// O piso absoluto (`> 0,15`) fica porque razao sozinha aprovaria preto: `0,003 > 0,001` e
		// verdade e nao e azul nenhum.
		// ================================================================================================
		//
		// ============================ E OS DOIS AZUIS SAO DOIS AZUIS DIFERENTES ============================
		// Ate esta passada as duas linhas cobravam a MESMA coisa ("o canal azul manda com folga"), porque
		// o Royale era o Blue escalado. O dono separou os dois: o Blue virou o CIANO do Goku SSGSS
		// (*"e um azul mais claro"*) e o azul fundo desceu pro Evolution (*"o cabelo atual do blue e pra
		// ser do evolved/royale"*). Uma medida so nao ve mais a diferenca -- e pior: a medida antiga
		// REPROVA o ciano, porque num ciano o verde anda junto com o azul de proposito.
		// ==============================================================================================
		//
		// CIANO: azul e verde altos e JUNTOS, vermelho no chao. O `G > 0,5` e o que reprova a volta pro
		// azul-marinho -- e exatamente o tom que o dono reclamou duas vezes.
		MedirMatiz("blue", "CIANO", c => c.B >= c.G && c.G > c.R * 1.5f && c.G > 0.5f);
		// AZUL FUNDO: o canal azul manda com folga sobre os outros dois.
		MedirMatiz("blue_evolution", "AZUL", c => c.B > c.G * 1.5f && c.B > c.R * 2f && c.B > 0.15f);

		// ROSA: vermelho na frente, azul no meio, verde no fim. Loiro e o contrario disso -- vermelho e
		// verde juntos no alto e azul no chao --, entao o `B > G` sozinho ja separa os dois casos.
		MedirMatiz("rose", "ROSA", c => c.R > c.G * 1.5f && c.B > c.G * 1.2f && c.R > 0.2f);
		MedirMatiz("rose2", "ROSA", c => c.R > c.G * 1.5f && c.B > c.G * 1.2f && c.R > 0.2f);

		// VERDE AMARELADO DO LEGENDARY. A medida antiga era `G > R*2`, ou seja VERDE PURO -- e ela
		// reprovava justamente o que o dono pediu: *"o lssj e um verde AMARELADO em ambos os casos
		// (primal e normal)"*. O que descreve amarelado e o vermelho ALTO junto do verde e o azul no
		// chao; o `R > B*3` e quem separa isso de um verde de bandeira (onde R e B caem juntos), e o
		// `G > R` e quem impede que ele vire dourado de SSJ comum.
		MedirMatiz("legendary", "VERDE AMARELADO", c => c.G > c.R && c.R > c.B * 3f && c.G > 0.5f);

		// --- 3. O EVOLUTION E MAIS ESCURO QUE O BLUE ---
		// Pedido do dono, e a medida e a LUMINANCIA do resultado (nao do hexa): duas tintas podem ter
		// hexas parecidos e cair diferente sobre a mesma arte.
		float Brilho(string id)
		{
			FormaDef d = Jandirus.Core.Forms.Catalogo.Def(id)!;
			var t = new Color(Jandirus.Core.Forms.Catalogo.CorDoCabelo(d)!);
			return tons.Select(x => EmMatiz(x.Cor, t))
					   .Average(c => c.R * 0.299f + c.G * 0.587f + c.B * 0.114f);
		}
		float doBlue = Brilho("blue"), doRoyale = Brilho("blue_evolution");
		_passos.Add($"  --     brilho medio: blue {doBlue:0.###} · blue_evolution {doRoyale:0.###}");
		Conferir(doRoyale < doBlue,
				 $"o Blue Evolution desenha MAIS ESCURO que o Blue ({doRoyale:0.###} contra {doBlue:0.###})");
		Conferir(doRoyale > 0.05f,
				 $"-- e nao tao escuro que o cabelo suma no preto ({doRoyale:0.###})");

		// --- 4. O CONTROLE: a soma, com o hexa do DM, entrega o defeito ---
		// ISTO NAO E UMA REGRA DO JOGO, e uma MEDIDA: o `rgb(13,73,238)` de `SaiyanObjects.dm:18` somado
		// nesta arte. Ela existe pra o dia em que alguem quiser "voltar ao original" -- o log mostra o
		// que isso devolve antes de a queixa voltar.
		var doDm = new Color("0d49ee");
		Color[] somados = [.. tons.Select(t => EmSoma(t.Cor, doDm))];
		_passos.Add("  --     CONTROLE -- o azul do DM (#0d49ee) SOMADO: "
				  + string.Join(" ", somados.Select(c => "#" + c.ToHtml(false))));
		int estourados = somados.Count(c => c.R > 0.95f && c.G > 0.95f && c.B > 0.95f);
		Conferir(estourados >= 2,
				 $"o azul do DM SOMADO estoura pro branco em {estourados} dos {somados.Length} tons "
			   + "-- e o defeito que o dono viu, medido");

		// --- 5. E AGORA A REGRA, NO CATALOGO INTEIRO ---
		OMatizNoCatalogoInteiro();
	}

	/// <summary>
	/// ============================ CINCO IDS A MAO NAO GUARDAM UMA REGRA ============================
	/// As linhas de cima medem `blue`, `blue_evolution`, `rose`, `rose2` e `legendary` -- e elas tem que
	/// existir, porque so quem sabe o que a forma DEVE parecer pode cobrar "isto le como ciano". Mas
	/// cobrar cinco ids nao e cobrar a regra, e o proprio `AsCoresNoCatalogoInteiro` ja escreveu o porque
	/// em cima da irma desta: *"um degrau novo na escada nasce fora da lista, sai com a cor errada e a
	/// bancada continua verde"*.
	///
	/// E NAO ERA HIPOTESE. A lista tinha `legendary` -- a linha Legendary COMUM -- e nao tinha um degrau
	/// sequer da linha Legendary PRIMAL, que e a outra metade da mesma escada e passa pelo mesmo matiz:
	/// `primal_legendary` e `primal_legendary2` tingem verde sobre a arte dourada e o
	/// `primal_legendary4_limit_breaker` tinge vermelho, e nenhum dos tres era medido por ninguem. Meia
	/// escada guardada e meia solta e exatamente o defeito que mais se repete neste port.
	///
	/// ============================ O QUE SE AFIRMA AQUI E O QUE MOTIVOU O MATIZ ============================
	/// Nao a cor -- a REGRA, e ela tem duas metades, que sao as duas queixas que criaram o modo:
	///
	///   1. NENHUM TOM SAI BRANCO. E o defeito nomeado pelo dono (*"aplicar azul por cima esta dando
	///      branco"*), e e o que a SOMA fazia na arte dourada. Um matiz que estoure os tres canais
	///      devolveu o defeito por outro caminho;
	///   2. O SOMBREADO SOBREVIVE. Uma tinta calibrada alto satura todos os tons no mesmo valor: a cor
	///      fica certa e o cabelo vira mancha chapada. E a MESMA assinatura do borrao sem relevo que o
	///      dono relatou no penteado base -- ali por causa do `COLOR = c * COLOR`, aqui por calibragem --,
	///      e ela reprova sem que a bancada precise saber qual cor e a certa.
	///
	/// ============================ E O CONJUNTO E DERIVADO, NAO ESCRITO ============================
	/// Quem entra na varredura sai das MESMAS tres perguntas que o `CharacterVisual.VestirCabeloDaForma`
	/// faz, na mesma ordem: o `ModoDoCabelo` troca a folha? o `CabelosDeForma` acha a variante (o `trocou`,
	/// que E o argumento `matiz:`)? e o `CorDoCabelo` devolve tinta? So quem responde sim as tres alcanca
	/// o matiz -- e ai a folha medida e a que ESSE degrau veste, e nao a dourada do bloco de cima.
	///
	/// Isso pega sozinho o degrau que alguem acrescentar amanha, e exclui sozinho quem soma no penteado
	/// preto (o SSG, o Ultra Ego sem variante, o `ui_perfected` sem sufixo) -- pra esses a soma e a
	/// operacao certa e medi-los aqui seria cobrar a conta errada.
	///
	/// COMO REPROVA SE A REGRA SUMIR: troque o `matiz: trocou` do `VestirCabeloDaForma` por `matiz: true`
	/// sem mais nada e nada aqui cai (a conta e a mesma); troque a tinta de um degrau por um hexa claro
	/// -- `ffffff` em qualquer um dos verdes -- e a metade 1 acusa aquele degrau pelo nome. E a metade 2
	/// e quem acusa o achatamento, que e o defeito que nao tem cor.
	/// ==============================================================================================
	/// </summary>
	private void OMatizNoCatalogoInteiro()
	{
		// A BASE E A DO JOGADOR PADRAO, e e a mesma que o bloco de cima mede: o que se afirma nao e "o
		// Goku fica azul", e sim "a conta do matiz nao estoura nem achata" -- e ela nao muda com o
		// penteado. Um penteado SEM variante cai no `De(...) == null` e sai da varredura sozinho,
		// exatamente como sai em jogo (`trocou` falso = soma).
		const string BaseDoCabelo = "res://Assets/Sprites/Hair/Hair_Goku.tres";

		var medidos = new List<string>();
		int semVariante = 0, semTinta = 0;

		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			// 1. A FORMA TROCA A FOLHA? As mesmas quatro entradas do `VestirCabeloDaForma`.
			Jandirus.Core.Forms.ModoDoCabelo modo = Jandirus.Core.Forms.Catalogo.ModoDoCabelo(d);
			bool troca = modo is Jandirus.Core.Forms.ModoDoCabelo.Trocar
							  or Jandirus.Core.Forms.ModoDoCabelo.TrocarETingir
							  or Jandirus.Core.Forms.ModoDoCabelo.TrocarOuTingir
							  or Jandirus.Core.Forms.ModoDoCabelo.TrocarERecolorir;
			if (!troca) continue;

			// 2. A VARIANTE EXISTE? Este e o `trocou`, e ele E o argumento `matiz:`. Sem folha propria
			// a tinta cai no penteado preto do jogador e a operacao passa a ser SOMA.
			string? folha = CabelosDeForma.De(
				BaseDoCabelo, Jandirus.Core.Forms.Catalogo.SufixoDoCabeloDe(d, false));
			if (folha == null) { semVariante++; continue; }

			// 3. AINDA CABE TINTA? No `TrocarOuTingir` a tinta e ALTERNATIVA a troca -- quem trocou nao
			// pinta --, entao esse modo nunca alcanca o matiz. A conta e a do jogo, nao um atalho.
			bool pinta = modo switch
			{
				Jandirus.Core.Forms.ModoDoCabelo.Tingir           => true,
				Jandirus.Core.Forms.ModoDoCabelo.TrocarETingir    => true,
				Jandirus.Core.Forms.ModoDoCabelo.TrocarERecolorir => true,
				_ => false,   // TrocarOuTingir: trocou, logo nao pinta
			};
			if (!pinta || Jandirus.Core.Forms.Catalogo.CorDoCabelo(d) is not { } hexa)
			{ semTinta++; continue; }

			(Color Cor, int Pixels)[] tons = TonsOpacosDe(PngDe(folha));
			if (tons.Length == 0) { Conferir(false, $"`{d.Id}`: a folha `{folha.GetFile()}` abre"); continue; }

			var tinta = new Color(hexa);
			Color[] saiu = [.. tons.Select(t => EmMatiz(t.Cor, tinta))];
			medidos.Add(d.Id);
			_passos.Add($"  --     `{d.Id}` #{hexa} sobre `{folha.GetFile()}` -> "
					  + string.Join(" ", saiu.Select(c => "#" + c.ToHtml(false))));

			// ============================ "NAO SAI BRANCO" SO E PERGUNTA PRA TINTA QUE TEM COR ============================
			// A queixa do dono era *"aplicar azul por cima esta dando branco"*: uma tinta COLORIDA que
			// perde a cor no estouro. Cobrar isso de uma tinta que ja e quase branca e cobrar que ela
			// deixe de ser o que e -- e o `beast` e exatamente esse caso: o cabelo da Fera e
			// BRANCO-GELO de proposito (`b6bac4`, o `ssj2hair` do jogador passado por grayscale e
			// clareado, `Mystic.dm:76-85`). Ele mede `#b6bac4 #edf3ff #ffffff #ffffff #9699a2` -- os dois
			// tons de cima estouram, e num cabelo branco isso e o desenho, nao o defeito.
			//
			// O CORTE SAI DA TINTA E NAO DE UMA LISTA DE IDS, que e o que impede isto de virar a excecao
			// calada de sempre: mede-se a saturacao do hexa. Os oito coloridos ficam entre 0,48 e 0,58; a
			// Fera da 0,08. Uma tinta nova colorida entra na cobranca sozinha, e uma que alguem lavar ate
			// o branco SAI dela -- e ai quem segura o relevo e a linha de baixo, que vale pra todo mundo.
			// ==========================================================================================================
			float satDaTinta = Mathf.Max(tinta.R, Mathf.Max(tinta.G, tinta.B))
							 - Mathf.Min(tinta.R, Mathf.Min(tinta.G, tinta.B));
			int brancos = saiu.Count(c => c.R > 0.85f && c.G > 0.85f && c.B > 0.85f);
			if (satDaTinta >= 0.25f)
				Conferir(brancos == 0,
						 $"`{d.Id}`: nenhum tom do matiz sai branco ({brancos} de {saiu.Length})");
			else
				_passos.Add($"  --     `{d.Id}`: tinta quase sem cor (sat {satDaTinta:0.00}) -- o teste de "
						  + $"branco nao se aplica; {brancos} tom(ns) claro(s), e o relevo abaixo e quem manda");

			// O MESMO PISO DE TRES do bloco de cima, e pelo mesmo motivo: com dois tons ainda ha
			// relevo; com um, o cabelo virou silhueta.
			//
			// E ELE E QUEM CARREGA A TINTA QUASE-BRANCA SOZINHO, ja que a linha de cima se cala nela: o
			// `beast` fecha em 4 de 5 (os dois tons mais claros da arte caem no mesmo `#ffffff`), o que e
			// perda de relevo NO REALCE e nao no desenho todo -- ainda restam tres degraus lendo. Se
			// alguem clarear mais a tinta, os proximos a fundir sao os do meio e ai isto reprova.
			//
			// NAO E `== tons.Length` DE PROPOSITO: exigir que nenhum par jamais se funda proibiria o
			// estouro do realce, que e o que um cabelo branco DEVE fazer. O piso pergunta se sobrou
			// desenho, e essa e a pergunta que o borrao chapado do `COLOR = c * COLOR` reprovava (ele
			// derrubava a folha inteira a um ou dois tons).
			int distintos = saiu.Select(c => c.ToHtml(false)).Distinct().Count();
			Conferir(distintos >= 3,
					 $"`{d.Id}`: e o sombreado sobrevive ao matiz ({distintos} tons distintos "
				   + $"de {saiu.Length})");
		}

		// O PISO DA VARREDURA. Sem ele, um `ModoDoCabelo` que parasse de devolver `Trocar` -- ou um
		// resolvedor de folha quebrado -- esvaziaria a lista e as duas afirmacoes acima ficariam verdes
		// sem medir nada. Seis, e nao os oito de hoje: dois degraus podem virar um sem que isso seja
		// defeito (foi o que aconteceu com o `legendary_full_power`), e este numero nao existe pra
		// contar forma, existe pra acusar desabamento.
		Conferir(medidos.Count >= 6,
				 $"a varredura alcanca o matiz em {medidos.Count} forma(s) do catalogo "
			   + $"({semVariante} sem variante de folha, {semTinta} sem tinta): {string.Join(", ", medidos)}");

		// E AS DUAS ESCADAS LENDARIAS ESTAO DENTRO -- a afirmacao que a lista a mao nao fazia. Nomear as
		// LINHAS e nao os ids e de proposito: um degrau novo em qualquer uma das duas entra sozinho, e o
		// dia em que a linha inteira sair da varredura esta linha cai.
		bool comum = medidos.Any(id => Jandirus.Core.Forms.Catalogo.Def(id)?.Linha
			== Jandirus.Core.Forms.LinhaDeForma.Legendary);
		bool primal = medidos.Any(id => Jandirus.Core.Forms.Catalogo.Def(id)?.Linha
			== Jandirus.Core.Forms.LinhaDeForma.LegendaryPrimal);
		Conferir(comum && primal,
				 $"e as DUAS linhas Legendary passam pelo matiz medido (comum {comum}, primal {primal}) "
			   + "-- era a primal que estava fora da lista escrita a mao");
	}

	// =====================================================================
	// 3a-ter-D. A AURA DO ROSE E A ARTE PROPRIA DELE
	// =====================================================================
	/// <summary>
	/// *"a aura do Rose e a `Supa Saiyan Rose Aura-1`, e o cabelo dele e ROSA (nao loiro)"*.
	///
	/// A metade do CABELO cai no bloco de cima (o rosa e medido tom a tom sobre a arte dourada). Aqui
	/// mora a metade da AURA, e ela tem tres perguntas que nenhuma outra varredura faz:
	///
	///   1. o simbolo da forma aponta pra a folha PROPRIA -- e nao pra a `FieryGodBlue` compartilhada,
	///      que e como ela vivia antes de o dono achar a arte;
	///   2. a arte carrega, e ela e mesmo ROSA (medida no pixel). "Ja vem colorida" so e uma
	///      justificativa aceitavel pra nao tingir enquanto a cor certa estiver desenhada la;
	///   3. o `SemTinta` do desenho responde pelo SIMBOLO e nao pelo caminho -- foi assim que a
	///      `DeusRosa` e a `DeusFrio` deram a mesma resposta pra perguntas diferentes quando dividiam
	///      o mesmo `.tres`.
	///
	/// COMO REPROVA SE A REGRA SUMIR: aponte a `DeusRosa` de volta pra `FieryGodBlue.tres` -- caem a
	/// primeira e a terceira (o azul nao passa na medida de rosa). Tire a `DeusRosa` da `PreColorida` --
	/// cai a do `SemTinta`, e em jogo a chama rosa levaria tinta rosa por cima.
	/// </summary>
	private void AAuraDoRose(SpriteDeAura desenho)
	{
		// --- 1. O SIMBOLO, forma a forma ---
		foreach (string id in new[] { "rose", "rose2" })
			Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def(id))
						 == FolhaDeAura.DeusRosa,
					 $"`{id}`: a chama e a folha ROSA propria "
				   + $"(deu {Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def(id))})");

		// E O SSG DA LINHA ROSE FICA NA QUENTE: e o mesmo corte `ssj == 0` do DM, e ele e o que impede
		// "toda a linha GodKiRose e rosa" de passar por engano.
		Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def("rose_ssg"))
					 == FolhaDeAura.DeusQuente,
				 "e o `rose_ssg` continua na chama QUENTE (o corte `ssj==0` do AuraObject.dm)");

		// `?? ""` E NAO `!`: um simbolo que perdesse o arquivo cairia nas tres linhas abaixo (nome,
		// distincao e importacao) em vez de estourar o robo inteiro num quadro qualquer.
		string caminho = SpriteDeAura.CaminhoDa(FolhaDeAura.DeusRosa) ?? "";
		Conferir(caminho.GetFile() == "Supa Saiyan Rose Aura-1.tres",
				 $"e o arquivo dela e a `Supa Saiyan Rose Aura-1` ({caminho.GetFile()})");
		Conferir(caminho != SpriteDeAura.CaminhoDa(FolhaDeAura.DeusFrio),
				 "e ela NAO divide mais o arquivo com a chama azul (dividia, e o `SemTinta` mentia)");
		Conferir(ResourceLoader.Exists(caminho), "a folha do Rose esta IMPORTADA");

		var frames = ResourceLoader.Load<SpriteFrames>(caminho);
		int quadros = frames != null && frames.GetAnimationNames().Length > 0
			? frames.GetFrameCount(frames.GetAnimationNames()[0]) : 0;
		Conferir(quadros > 0, $"e ela CARREGA, com {quadros} quadro(s) (existir nao e importar)");

		// --- 2. O PIXEL: a arte e rosa mesmo ---
		(Color Cor, int Pixels)[] tons = TonsOpacosDe(PngDe(caminho));
		Conferir(tons.Length > 0, "o PNG da chama do Rose abre pra a medicao");
		if (tons.Length > 0)
		{
			// O TOM DOMINANTE **COLORIDO**: a folha tem 4090 pixels de contorno preto puro, e preto nao
			// responde "de que cor e esta chama". Sem esta filtragem a medida dependeria de a arte ter
			// ou nao contorno, que e outro assunto (e esta anotado como observacao pro dono).
			Color dom = tons.First(t => t.Cor.R8 + t.Cor.G8 + t.Cor.B8 > 30).Cor;
			_passos.Add($"  --     chama do Rose: {tons.Sum(t => t.Pixels)} px, dominante colorido "
					  + $"#{dom.ToHtml(false)}");
			Conferir(dom.R > dom.G + 0.2f && dom.B > dom.G + 0.1f,
					 $"a arte da chama do Rose e ROSA/CARMESIM de verdade (#{dom.ToHtml(false)})");
			Conferir(dom.R >= dom.B,
					 $"-- e nao e a AZUL do original com outro nome (#{dom.ToHtml(false)})");
		}

		// --- 3. ELA NAO SE TINGE, E QUEM RESPONDE ISSO E O SIMBOLO ---
		Conferir(SpriteDeAura.PreColorida(FolhaDeAura.DeusRosa),
				 "a chama do Rose esta na lista das JA COLORIDAS (tingi-la jogaria a arte fora)");

		desenho.DefinirFolha(FolhaDeAura.DeusRosa);
		Conferir(desenho.SimboloDeTeste == FolhaDeAura.DeusRosa && desenho.SemTinta,
				 $"e o desenho vivo responde pelo SIMBOLO: `{desenho.SimboloDeTeste}`, sem tinta "
			   + $"({desenho.SemTinta})");
		// O `NaN` E UM ESTADO REAL e nao um detalhe de C#: ele quer dizer "o sprite nao esta montado", e
		// nesse caso a ancora nao foi medida -- a linha tem que reprovar dizendo isso, e nao passar por
		// uma comparacao que com NaN e sempre falsa de um jeito que ninguem lê.
		float baseDela = desenho.BaseDeTeste;
		Conferir(!float.IsNaN(baseDela) && Mathf.Abs(baseDela - SpriteDeAura.LinhaDosPes) <= 2f,
				 $"e ela nasce no pe como as irmas (base {baseDela:0.#}, pe {SpriteDeAura.LinhaDosPes})");

		// E DEVOLVE A FOLHA -- o resto da bancada mede este mesmo desenho, e deixa-lo em rosa poria a
		// chama do Rose numa foto de Legendary mil linhas abaixo.
		desenho.DefinirFolha(FolhaDeAura.Base);
		Conferir(!desenho.SemTinta, "e voltando pra a folha colorivel ela volta a se tingir");
	}

	// =====================================================================
	// 3a-ter-E. O OLHO DA LINHA LENDARIA, E A EXCECAO QUE O DONO NOMEOU
	// =====================================================================
	/// <summary>
	/// *"a cor dos olhos de cada forma da tabela, com o `wrathful` sendo a unica excecao amarela dentro
	/// do branco do Legendary"*.
	///
	/// ============================ ISTO NAO E O QUE A TABELA DO CABELO JA MEDE ============================
	/// A <see cref="OQueCadaFormaFazNoCabelo"/> cobra o hexa de cada forma, uma linha por forma. Ela
	/// aprova qualquer conjunto de valores que alguem escreva nas duas pontas -- inclusive "todo mundo
	/// amarelo", se a tabela tambem disser amarelo em tudo. O que ela NAO sabe perguntar e a FORMA da
	/// regra: *uma* cor pra linha inteira e *uma* excecao, nomeada.
	///
	/// Por isso aqui a pergunta e por LINHA e nao por forma: quantas cores diferentes cada linha usa, e
	/// quem e o degrau que destoa. Uma segunda excecao entrando na linha Legendary (a coisa mais facil
	/// de fazer neste sistema -- e um `if` por id) reprova aqui e em lugar nenhum mais.
	/// ================================================================================================
	/// </summary>
	private void OOlhoDaLinhaLendaria()
	{
		const string Amarelo = "e8bc18";
		// O VERDE DA ESCADA SAIYAJIN, digitado de novo e nao buscado -- mesma regra do amarelo acima: uma
		// checagem escrita como `== VerdeDoOlhoSuperSaiyajin` passaria com qualquer valor la dentro. E o
		// ponto desta linha e justamente que a linha Legendary com as redeas na mao usa **a cor da
		// escada** e nao um verde proprio: "a pupila verde VOLTA", palavra do dono.
		const string Verde = "40a060";

		// --- 1. AS DUAS LINHAS LENDARIAS: o branco e da POSSE, e ele apaga ate o amarelo ---
		//
		// ============================ O EIXO DESTA MEDIDA MUDOU DE PERGUNTA ============================
		// Ela contava "quantos degraus lendarios sao brancos" porque o branco era da LINHA. O dono
		// corrigiu -- *"quando o jogador tem o controle a pupila verde volta, deixa de ser branca"* --,
		// e o DM ja dizia o mesmo nas duas pontas (`lssjbuff.dm:289` e `:609`: os olhos se apagam quando
		// a furia toma o corpo). Entao o que se conta agora e OUTRA COISA: que o mesmo degrau devolve
		// DUAS cores conforme quem dirige, e que o corte e a posse e nao o id.
		// ==========================================================================================
		FormaDef[] lendarias = [.. Jandirus.Core.Forms.Catalogo.Todas
			.Where(d => d.Linha is LinhaDeForma.Legendary or LinhaDeForma.LegendaryPrimal)];
		Conferir(lendarias.Length >= 10,
				 $"as duas linhas lendarias tem os degraus todos pra esta medida ({lendarias.Length})");

		// POSSUIDO: a linha INTEIRA apaga a iris, sem excecao -- e "sem excecao" e a metade que importa,
		// porque o Wrathful (o amarelo) e justamente o degrau que mais perde o controle: e o primeiro da
		// linha, o de maestria zero. Se o amarelo vencesse a posse, a unica forma em que o jogador vai
		// passar a maior parte do tempo possuido seria a que nao mostra isso.
		string[] acesosNaPosse = [.. lendarias
			.Where(d => Jandirus.Core.Forms.Catalogo.CorDoOlho(d, semRedeas: true) != EscleroticaDoCorpo)
			.Select(d => d.Id)];
		Conferir(acesosNaPosse.Length == 0,
				 $"com a FURIA dirigindo, os {lendarias.Length} degraus lendarios ficam de olho "
			   + $"#{EscleroticaDoCorpo} sem iris ({string.Join(", ", acesosNaPosse)})");

		// COM AS REDEAS NA MAO: ninguem fica branco, e ha exatamente UMA cor fora do verde da escada.
		string[] brancosLivres = [.. lendarias
			.Where(d => Jandirus.Core.Forms.Catalogo.CorDoOlho(d, semRedeas: false) == EscleroticaDoCorpo)
			.Select(d => d.Id)];
		Conferir(brancosLivres.Length == 0,
				 $"e com o DONO dirigindo nenhum deles continua branco ({string.Join(", ", brancosLivres)})");

		string[] fora = [.. lendarias
			.Where(d => Jandirus.Core.Forms.Catalogo.CorDoOlho(d, semRedeas: false) != Verde)
			.Select(d => d.Id)];
		_passos.Add($"  --     Legendary+Primal: {lendarias.Length} degraus -- "
				  + $"#{EscleroticaDoCorpo} sem iris na posse, #{Verde} nas redeas do dono");

		Conferir(fora.Length == 1,
				 $"dentro do verde do Legendary ha UMA excecao ({fora.Length}: {string.Join(", ", fora)})");
		Conferir(fora.Length == 1 && fora[0] == Jandirus.Core.Forms.Catalogo.IdWrathful,
				 $"e ela e o `{Jandirus.Core.Forms.Catalogo.IdWrathful}` "
			   + $"({(fora.Length == 1 ? fora[0] : "nenhuma")})");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoOlho(
					 Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdWrathful)) == Amarelo,
				 $"e o olho dele e AMARELO #{Amarelo} (deu #"
			   + (Jandirus.Core.Forms.Catalogo.CorDoOlho(
					  Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdWrathful)) ?? "nada") + ")");
		// E A POSSE VENCE O AMARELO. Sem esta linha, escrever a excecao do Wrathful ANTES da guarda de
		// posse no `CorDoOlho` passaria em tudo acima -- e e a ordem mais facil de trocar sem perceber.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoOlho(
					 Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdWrathful),
					 semRedeas: true) == EscleroticaDoCorpo,
				 "-- mas a furia apaga o amarelo dele tambem (a posse e perguntada ANTES da excecao)");

		// E O AMARELO E DELE E DE MAIS NINGUEM NO JOGO INTEIRO -- senao "excecao" seria so mais uma cor.
		string[] amarelos = [.. Jandirus.Core.Forms.Catalogo.Todas
			.Where(d => Jandirus.Core.Forms.Catalogo.CorDoOlho(d) == Amarelo).Select(d => d.Id)];
		Conferir(amarelos.Length == 1,
				 $"e o #{Amarelo} nao aparece em mais nenhuma forma do catalogo "
			   + $"({string.Join(", ", amarelos)})");

		// --- 2. AS OUTRAS LINHAS, pela mesma pergunta ---
		// Cada linha e uma frase do enunciado do dono, e o que se conta e quantas cores ela usa. As duas
		// que usam DUAS tem a razao escrita ao lado; qualquer outra que passe a usar duas reprova.
		(LinhaDeForma Linha, int Cores, string PorQue)[] esperado =
		[
			// A escada Saiyajin usa duas: o verde da escada e o amarelado dos TRES SSJ4 (corte por
			// `FormaDef.Corpo`). A base nao conta -- ela nao e forma e devolve nulo.
			(LinhaDeForma.Saiyajin, 2, "o verde da escada mais o amarelado dos tres SSJ4"),
			(LinhaDeForma.Futuro, 1, "a escada do futuro e verde inteira"),
			// AS DUAS LENDARIAS SAO CONTADAS **COM AS REDEAS NA MAO** (o `CorDoOlho` de um argumento so),
			// que e o estado normal delas. O branco sem iris da posse nao entra nesta conta de proposito:
			// ele nao e uma cor da linha, e a cor de um corpo que a furia esta dirigindo -- quem mede isso
			// e o bloco 1 la em cima.
			(LinhaDeForma.Legendary, 2, "o verde da escada mais o amarelo do Wrathful"),
			(LinhaDeForma.LegendaryPrimal, 1, "a primal inteira e verde -- o Wrathful nao e dela"),
			(LinhaDeForma.GodKi, 2, "o vermelho do SSG e o azul do Blue (o corte `ssj==0`)"),
			(LinhaDeForma.GodKiRose, 2, "o mesmo corte, do lado rosa"),
			(LinhaDeForma.UltraInstinct, 1, "a prata da linha -- o `Buff()` do DM nao ramifica por estagio"),
			(LinhaDeForma.UltraEgo, 1, "so o degrau que PINTA o cabelo (a Destroyer nao pinta nada)"),
			// ERA ZERO ("*igual a base, nada muda*") e virou UMA quando o dono pediu *"o olho do beast
			// era pra ser vermelho"*. UMA e nao duas: o Mistico continua sem tocar no olho, e quem
			// separa os dois e o corte `GodkiRoyalePct`. Esta conta sozinha nao distingue "so a Fera"
			// de "a linha inteira de vermelho" -- quem faz isso e o `OMisticoSoGanhouAFaisca`, que
			// cobra o NULO do Mistico entrada por entrada.
			(LinhaDeForma.Mistico, 1, "so o vermelho da Fera -- o Mistico nao mexe no olho"),
			(LinhaDeForma.Oozaru, 0, "o macaco nem olho desenhado tem"),
		];
		foreach ((LinhaDeForma linha, int cores, string porque) in esperado)
		{
			string[] daLinha = [.. Jandirus.Core.Forms.Catalogo.Todas.Where(d => d.Linha == linha)
				.Select(d => Jandirus.Core.Forms.Catalogo.CorDoOlho(d))
				.Where(c => c != null).Select(c => c!).Distinct().Order()];
			Conferir(daLinha.Length == cores,
					 $"a linha {linha} usa {cores} cor(es) de olho -- {porque} "
				   + $"({string.Join(", ", daLinha.Select(c => "#" + c))})");
		}

		// =====================================================================
		// 3. O OLHO DA FERA -- *"o olho do beast era pra ser vermelho"*
		// =====================================================================
		// ============================ A CONTAGEM DE CIMA NAO BASTA PRA ESTE ============================
		// "a linha do Mistico usa UMA cor" fica verde tambem se a cor for do MISTICO e nao do Beast, ou
		// se for verde em vez de vermelha. As duas linhas abaixo dizem QUAL e DE QUEM.
		//
		// O NULO DO MISTICO E TAO PEDIDO QUANTO O VERMELHO DA FERA: o dono nomeou o Beast, e o enunciado
		// do Mistico e *"tudo igual a base, nada muda"*. Um `LinhaDeForma.Mistico => VermelhoDaFera` sem
		// o `when` daria os dois de vermelho e passaria na contagem acima.
		//
		// E O HEXA E LITERAL, como no resto deste arquivo: comparar com a constante conferiria a
		// constante com ela mesma e passaria com qualquer valor trocado por engano.
		if (Jandirus.Core.Forms.Catalogo.Def("beast") is { } feraOlho)
		{
			string? olhoDaFera = Jandirus.Core.Forms.Catalogo.CorDoOlho(feraOlho);
			Conferir(olhoDaFera == "e5282a",
					 $"o olho da Fera e VERMELHO #e5282a (deu {olhoDaFera ?? "nada"})");

			// E ELE NAO E O VERMELHO DE MAIS NINGUEM. A tentacao aqui era reusar o `ff2d2f` do Limit
			// Breaker, que esta a um passo -- e o arquivo proibe (ver `VermelhoDaFera`): sao linhas que
			// nao se devem nada. Esta linha e o que acusa se alguem "simplificar" as duas numa.
			Conferir(olhoDaFera != Jandirus.Core.Forms.Catalogo.CorDoContorno(
						 Jandirus.Core.Forms.Catalogo.Def("ssj4_limit_breaker")),
					 "-- e ele NAO e o vermelho do Limit Breaker (constantes separadas, de proposito)");

			// E A COR SAI DA SOMA CERTA: a camada de olho e `Eyes_Black.png`, preta pura, entao o hexa
			// escrito E o pixel -- e "vermelho" tem que ser vermelho na tela, nao um marrom escuro.
			static bool EhVermelho(Color c) => c.R > c.G + 0.5f && c.R > c.B + 0.5f;
			var noOlho = new Color(olhoDaFera ?? "000000");
			Conferir(EhVermelho(noOlho),
					 $"-- e sobre o preto da camada de iris ele le VERMELHO mesmo (#{noOlho.ToHtml(false)})");

			// ============================ E O CRITERIO REPROVA ALGUEM ============================
			// A linha de cima sozinha nao diz se "vermelho" e uma medida ou uma frase: um criterio frouxo
			// (um `R > 0.5` seco) aprovaria metade do catalogo, inclusive a prata do Ultra Instinct e o
			// amarelo do Wrathful, que sao as duas cores de olho mais claras do jogo. As duas passam pelo
			// mesmo criterio aqui e as duas tem que CAIR.
			//
			// AS COBAIAS SAIEM DO CATALOGO e nao de hexas escritos: se alguem pintar o olho do Ultra
			// Instinct de vermelho amanha, este controle reprova e obriga a escolher outra cobaia -- que
			// e o aviso certo, porque nesse dia o vermelho da Fera deixa de ser dela.
			// ==================================================================================
			foreach (string cobaia in new[] { "ui_perfected", Jandirus.Core.Forms.Catalogo.IdWrathful })
				if (Jandirus.Core.Forms.Catalogo.CorDoOlho(
						Jandirus.Core.Forms.Catalogo.Def(cobaia)) is { } outro)
					Conferir(!EhVermelho(new Color(outro)),
							 $"CONTROLE: o olho de `{cobaia}` (#{outro}) NAO passa por vermelho");
		}
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoOlho(
					 Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdMistico)) == null,
				 "e o Mistico continua sem tocar no olho -- o dono nomeou a Fera, nao a linha");
	}

	// =====================================================================
	// 3a-ter-F. O RABO MEDIDO NO DESENHO -- branco no Perfected, roxo no Ego
	// =====================================================================
	/// <summary>
	/// *"rabo branco no Perfected UI e roxo no Ultra Ego"*.
	///
	/// ============================ "BRANCO" NAO E O HEXA ESCRITO, E O QUE SAI ============================
	/// O catalogo devolve `b9becb` pro rabo do Perfected -- que lido sozinho e um CINZA. Ele vira branco
	/// porque a tinta SOMA sobre um desenho escuro, e o desenho e que decide: o `Tail.png` tem dois tons
	/// (`#313131` e `#4d4d4d`), e `0x4d + 0xb9` passa de 255 nos tres canais.
	///
	/// Ou seja, a mesma tinta num rabo mais claro daria outra coisa -- e e por isso que esta medida le o
	/// PNG em vez de comparar strings. O dia em que alguem redesenhar a cauda mais clara, o "branco" do
	/// dono deixa de ser branco e a bancada avisa.
	/// ================================================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR: devolva `UltraInstinct`, `UltraEgo` ou `Mistico` a lista de
	/// exclusao do `CorDoRabo` (as tres estiveram la, e no DM e onde elas ficam) -- caem as linhas de
	/// resultado da que voltar.
	/// </summary>
	private void ORaboMedidoNoDesenho()
	{
		(Color Cor, int Pixels)[] tons = TonsOpacosDe(PngDe(CharacterVisual.SpriteDoRabo));
		Conferir(tons.Length > 0, $"a folha do rabo abre ({PngDe(CharacterVisual.SpriteDoRabo).GetFile()})");
		if (tons.Length == 0) return;

		_passos.Add($"  --     `Tail.png`: {tons.Sum(t => t.Pixels)} px opacos, tons "
				  + string.Join(" ", tons.Select(t => "#" + t.Cor.ToHtml(false))));

		// A PREMISSA: a cauda e ESCURA e NEUTRA. E o que autoriza a soma e o que faz um cinza claro
		// virar branco -- num desenho ja claro, qualquer tinta somada daria branco e a medida nao
		// separaria nada.
		int escuros = tons.Count(t => t.Cor.R < 0.4f && t.Cor.G < 0.4f && t.Cor.B < 0.4f
								   && Mathf.Abs(t.Cor.R - t.Cor.B) < 0.05f);
		Conferir(escuros == tons.Length,
				 $"os {tons.Length} tons da cauda sao escuros e neutros ({escuros}) -- e o que a soma pressupoe");

		void MedirRabo(string id, string? hexaEsperado, string comoLe, Func<Color, bool>? leCerto)
		{
			if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } d) { Conferir(false, $"`{id}` existe"); return; }
			string? hexa = Jandirus.Core.Forms.Catalogo.CorDoRabo(d);

			Conferir(hexa == hexaEsperado,
					 $"`{id}`: rabo {(hexaEsperado == null ? "SEM tinta" : "#" + hexaEsperado)} "
				   + $"(deu {hexa ?? "sem tinta"})");
			if (hexa == null || leCerto == null) return;

			var tinta = new Color(hexa);
			Color[] saiu = [.. tons.Select(t => EmSoma(t.Cor, tinta))];
			_passos.Add($"  --     `{id}` #{hexa} sobre a cauda -> "
					  + string.Join(" ", saiu.Select(c => "#" + c.ToHtml(false))));

			for (int i = 0; i < saiu.Length; i++)
				Conferir(leCerto(saiu[i]),
						 $"`{id}`: o tom #{tons[i].Cor.ToHtml(false)} da cauda sai {comoLe} "
					   + $"(#{saiu[i].ToHtml(false)})");
		}

		// BRANCO: os tres canais no alto E juntos. So "claro" nao basta -- um creme tambem e claro, e a
		// diferenca entre branco e creme e exatamente a distancia entre os canais.
		//
		// ESCRITO UMA VEZ SO E COM NOME. Esta conta estava copiada aqui e no bloco da Fera, palavra por
		// palavra: duas copias do mesmo criterio afrouxam juntas se alguem "ajustar" uma delas pra fazer
		// uma linha vermelha passar, e a outra vira um numero diferente com a mesma frase no log. E com
		// nome ela pode ser usada como CONTROLE la embaixo, que e o que faltava.
		static bool EhBranco(Color c) =>
			c.R > 0.85f && c.G > 0.85f && c.B > 0.85f
			&& Mathf.Max(c.R, Mathf.Max(c.G, c.B)) - Mathf.Min(c.R, Mathf.Min(c.G, c.B)) < 0.12f;

		MedirRabo("ui_perfected", "b9becb", "BRANCO", EhBranco);

		// ROXO: azul e vermelho no alto, verde EMBAIXO. E o verde baixo que separa roxo de branco-lilas
		// -- sem ele, um `b9becb` qualquer passaria por roxo.
		MedirRabo("ultra_ego", "8c32be", "ROXO CLARO",
				  c => c.B > c.G + 0.3f && c.R > c.G + 0.2f && c.B >= c.R && c.G < 0.55f);

		// ============================ A FERA, PELA MESMA DERIVACAO E POR UM PEDIDO NOVO ============================
		// *"o rabo do beast n ta branco"*. A linha do Mistico estava na lista de exclusao do
		// `CorDoRabo` -- como o Ultra Instinto e o Ultra Ego estiveram -- e saiu pelo mesmo caminho:
		// nenhuma regra nova, so a lista mais curta, e a derivacao "quem pinta o cabelo pinta o rabo"
		// respondendo sozinha.
		//
		// E A MEDIDA IMPORTA MAIS AQUI QUE NAS OUTRAS: o `b6bac4` da Fera e o hexa MAIS ESCURO dos tres
		// que somam nesta cauda (o Perfected usa `b9becb`), entao ele e o que chega mais perto de
		// deixar de fechar em branco. Se algum dia alguem clarear o `Tail.png`, ou escurecer o cabelo
		// da Fera, e esta linha que cai primeiro.
		MedirRabo("beast", "b6bac4", "BRANCO", EhBranco);

		// AS TRES QUE **NAO** PINTAM, e elas sao metade do enunciado: o dono nomeou o **Perfected**, o
		// **Ultra Ego** e o **Beast**, nao as linhas. Sem estas linhas, "pinta a linha inteira" passaria.
		//
		// O `mistico` E O CASO MAIS DELICADO DOS TRES: ele saiu da lista de exclusao JUNTO com o Beast
		// (e uma linha so no `CorDoRabo`), e continua sem tinta por DERIVACAO -- ele nao pinta o cabelo,
		// entao nao pinta o rabo. Ou seja o "nada muda" dele nao esta mais protegido por uma excecao
		// escrita: esta protegido pela regra. Esta linha e o que garante que continue assim.
		MedirRabo("ui_sign", null, "", null);
		MedirRabo("destroyer", null, "", null);
		MedirRabo("mistico", null, "", null);

		// O CONTROLE: o dourado da escada Saiyajin na MESMA cauda. Ele existe pra provar que a medicao
		// distingue cor -- se a soma devolvesse sempre a mesma coisa, esta linha cairia.
		MedirRabo("ssj1", "dada26", "DOURADO", c => c.R > 0.7f && c.G > 0.7f && c.B < c.G - 0.25f);

		// ============================ E O CONTROLE DO CRITERIO DE BRANCO, QUE E OUTRA COISA ============================
		// A linha de cima prova que o DOURADO sai dourado. Ela NAO prova nada sobre o criterio de
		// "branco": um `EhBranco` frouxo (so `> 0.85` nos tres canais, sem a distancia entre eles)
		// continuaria aprovando a Fera e o Perfected, e "branco" viraria "claro" sem uma linha vermelha.
		//
		// Entao o criterio da Fera e aplicado AO DOURADO de proposito, e cobra-se que ele REPROVE. O
		// dourado somado nesta cauda da `#ffff57` e `#ffff73` -- claro nos tres canais e a 168 pontos de
		// distancia entre o maior e o menor: e exatamente o creme que o comentario la de cima descreve.
		// =============================================================================================
		Color[] douradoNaCauda = [.. tons.Select(t => EmSoma(t.Cor, new Color("dada26")))];
		Conferir(douradoNaCauda.Length > 0 && douradoNaCauda.All(c => !EhBranco(c)),
				 "CONTROLE: o dourado do SSJ na MESMA cauda NAO passa pelo criterio de branco ("
			   + string.Join(" ", douradoNaCauda.Select(c => "#" + c.ToHtml(false))) + ")");
	}

	// =====================================================================
	// 3a-ter-G. O MISTICO SO GANHOU A FAISCA
	// =====================================================================
	/// <summary>
	/// *"o Mistico tem faisca e NAO muda cabelo, cor nem aura"*.
	///
	/// Ele e a unica forma transformada do jogo cujo enunciado e uma AUSENCIA -- *"tudo igual a base,
	/// MAS ele TEM os raiozinhos"* --, e ausencia e o que mais facilmente se perde: qualquer derivacao
	/// nova por linha (a do cabelo, a do olho, a do rabo, a da colada) pode passar a alcanca-lo sem que
	/// nada reprove, porque nao ha valor errado pra ver, so um valor onde nao devia haver nenhum.
	///
	/// Por isso este bloco pergunta as ausencias de uma vez, no dado e no boneco, e uma presenca -- a
	/// faisca, que e a unica coisa que ele acende.
	///
	/// ============================ E A COR DA FAISCA NAO E A DA AURA ============================
	/// Esta e a parte que o log tem que dizer em voz alta, porque ela e a UNICA linha do jogo assim: a
	/// faisca de todas as outras formas segue a aura, e a do Mistico e branca. O motivo esta medido no
	/// arquivo do DM (`Electric_Mystic.dmi` e neutra, cinco tons sem matiz) e na tela (lilas dentro de
	/// chama lilas some -- o mesmo defeito verde-sobre-verde que o dono mandou consertar no
	/// `primal_legendary2`).
	/// =====================================================================================
	///
	/// ============================ A AURA ENTROU NA LISTA DE AUSENCIAS ============================
	/// Ela era a excecao do enunciado -- "tudo igual a base MENOS a chama, que era a do SSG". Deixou de
	/// ser: por pedido do dono a chama dele e a do JOGADOR, e o *"tudo igual a base"* passou a ser
	/// literal em todos os canais. Ver `Catalogo.ChamaDoJogador`.
	/// ========================================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR: zere o `Raios` do `mistico` -- cai a faisca. Tire o ramo
	/// `LinhaDeForma.Mistico` do `CorDosRaios` -- a cor cai pro lilas da aura e cai a comparacao.
	/// Devolva o `Mistico` a lista de exclusao do `CorDoRabo` -- a linha do rabo continua VERDE (ele
	/// nao tem tinta de cabelo), e quem cai e a do Beast, no bloco do rabo medido. Devolva o ramo
	/// `Mistico => DeusQuente` ao `Catalogo.Folha`, ou tire o ramo dele do `ChamaDoJogador` -- cai uma
	/// das duas linhas de aura aqui embaixo, cada uma dizendo qual das duas perguntas quebrou.
	/// </summary>
	private void OMisticoSoGanhouAFaisca()
	{
		if (Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdMistico) is not { } mis)
		{ Conferir(false, "o `mistico` existe no catalogo"); return; }

		// --- 1. A PRESENCA: a faisca ---
		Conferir(mis.Raios > 0, $"o Mistico ACENDE faisca ({mis.Raios})");
		Conferir(mis.Raios == 1,
				 $"e no volume LEVE -- `Mystic.dm:37` acende UMA folha, como o SSJ2 ({mis.Raios})");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(mis) == "ffffff",
				 $"e ela e BRANCA (deu #{Jandirus.Core.Forms.Catalogo.CorDosRaios(mis)})");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(mis) != mis.Aura,
				 $"e NAO a cor da aura dele (#{mis.Aura}) -- lilas dentro de chama lilas some");

		// ============================ O BEAST HERDA O VOLUME E **NAO** A COR ============================
		// O volume nao foi pedido: `Mystic.dm:112` acende o MESMO objeto de overlay dentro do `Buff()`
		// dele, e deixar so o degrau de baixo faiscando faria a linha PERDER efeito ao subir.
		//
		// A COR SEPAROU. Esta segunda linha dizia "e branca nele tambem (a linha inteira, e nao um caso
		// por id)" -- o dono derrubou: *"no beast os raiozinhos sao roxos"*. As duas linhas juntas sao o
		// que descreve a regra de hoje: MESMO volume, cores DIFERENTES, e o corte e o `GodkiRoyalePct`.
		if (Jandirus.Core.Forms.Catalogo.Def("beast") is { } fera)
		{
			Conferir(fera.Raios == mis.Raios,
					 $"e o Beast continua com a MESMA faisca do Mistico ({fera.Raios}) -- `Mystic.dm:112`");
			Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(fera) == "d9b0ff",
					 $"mas ela e ROXA nele -- pedido do dono, e o corte e o mesmo `GodkiRoyalePct` do "
				   + $"contorno (deu #{Jandirus.Core.Forms.Catalogo.CorDosRaios(fera)})");
			Conferir(Jandirus.Core.Forms.Catalogo.CorDosRaios(fera)
					 != Jandirus.Core.Forms.Catalogo.CorDosRaios(mis),
					 "-- ou seja os DOIS degraus da linha nao dividem mais a cor da faisca");

			// ============================ E "ROXO" E UMA FORMA DE COR, NAO O HEXA ESCRITO ============================
			// A mesma regra do rabo branco (ver `ORaboMedidoNoDesenho`): o dono pediu uma COR, e cor se
			// le pela forma dela. Roxo e azul no alto, vermelho no meio e verde EMBAIXO -- e o verde
			// baixo e o que separa roxo de branco-lilas. Sem esta linha, trocar o `d9b0ff` por um
			// `f0f0ff` qualquer passaria em todo hexa deste arquivo desde que a tabela mudasse junto.
			static bool EhRoxo(Color c) => c.B > c.R && c.R > c.G && c.B - c.G >= 0.25f;
			var roxo = new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(fera));
			Conferir(EhRoxo(roxo),
					 $"-- e ela le ROXO mesmo (#{roxo.ToHtml(false)}: B {roxo.B:0.##} > R {roxo.R:0.##} "
				   + $"> G {roxo.G:0.##})");

			// O CONTROLE: a BRANCA do Mistico reprova no mesmo criterio. Sem ele um criterio frouxo
			// aprovaria as duas, e "roxo" viraria palavra no log em vez de medida.
			var branca = new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(mis));
			Conferir(!EhRoxo(branca),
					 $"CONTROLE: a faisca branca do Mistico NAO passa por roxa (#{branca.ToHtml(false)})");

			// ============================ E ELA VENCE A CHAMA POR VALOR, NAO POR MATIZ ============================
			// Este e o UNICO ponto do jogo em que a faisca cai dentro de uma chama da MESMA familia de
			// cor, e a matiz nao salva: a chama da Fera e `7d5af0` (matiz 254) e a faisca `d9b0ff`
			// (matiz 271) -- 17 graus, contra os ~180 que separam o azul da faisca do dourado que ela
			// atravessa nas escadas de sangue. Quem faz a faisca aparecer aqui e o BRILHO, e o criterio
			// esta escrito no `Catalogo.RoxoDaFaiscaDaFera` ("claro e lavado pra aparecer POR CIMA da
			// aura"). Esta linha e o que transforma aquela prosa em numero.
			//
			// O DIA EM QUE ELA CAIR, o remedio e a cor do RAIO e nao a da chama -- esta dito na constante,
			// e a colisao entre os dois pedidos do dono esta dita no relatorio.
			float lumFaisca = roxo.Luminance, lumChama = new Color(fera.Aura).Luminance;
			Conferir(lumChama > 0f && lumFaisca >= lumChama * 1.5f,
					 "-- e ela e ao menos 1,5x mais clara que a chama roxa em que cai ("
				   + $"{lumFaisca:0.###} contra {lumChama:0.###} = "
				   + $"{(lumChama > 0f ? lumFaisca / lumChama : 0f):0.##}x)");
		}

		// --- 2. AS AUSENCIAS, no dado ---
		Conferir(Jandirus.Core.Forms.Catalogo.ModoDoCabelo(mis) == ModoCabelo.Base,
				 $"o Mistico nao troca NEM pinta o cabelo (`Mystic.dm:33-36`) "
			   + $"-- modo {Jandirus.Core.Forms.Catalogo.ModoDoCabelo(mis)}");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoCabelo(mis) == null, "-- sem tinta de cabelo");
		Conferir(mis.SufixoDoCabelo.Length == 0, "-- e sem sprite proprio de cabelo");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoOlho(mis) == null, "-- e nao mexe na cor dos OLHOS");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(mis) == null, "-- nem na do RABO");
		Conferir(mis.Corpo == CorpoDeForma.Nenhum, "-- nem troca de CORPO");
		Conferir(Jandirus.Core.Forms.Catalogo.Coladas(mis).Length == 0,
				 "-- e nao cola overlay nenhum no corpo (a tabela do dono nao o lista)");

		// ============================ E A AURA DELE VIROU A SETIMA AUSENCIA ============================
		// Este bloco afirmava o CONTRARIO ("a chama dele e a QUENTE da linha, a mesma do SSG"), e era
		// porte fiel: `Mystic.dm:33` chama `Revert()`, zera `ssj`/`lssj`, e o `AuraObject.dm:174-176`
		// manda quem tem ki divino com a escada zerada pra a `FieryGod`.
		//
		// SO QUE O MISTICO NAO TEM KI DIVINO NENHUM (`PedeGodKi = -1`), e por isso no DM ele cai no
		// ramo de BAIXO -- `:191-192` usa `container.AURA` e o `centerAura()` de `:194` escreve
		// `icolor = rgb(AuraR, AuraG, AuraB)`, a cor sorteada no nascimento. O port tinha pego o ramo
		// errado, e foi isso que o dono viu: *"a aura do mistico tem q ser a mesma aura da BASE DO
		// PERSONAGEM, porem com os efeitos de raiozinhos q ja existem"*.
		//
		// ENTAO A CHAMA DELE ENTROU NA LISTA DE AUSENCIAS em vez de ficar como "a unica coisa que ele
		// muda": ele acende, e acende na cor do jogador. E o `*tudo igual a base*` do enunciado passou
		// a valer tambem pra a aura -- que era o unico canal em que nao valia.
		//
		// O QUE FICA POR ESCRITO E QUE SAO DUAS PERGUNTAS. A folha (colorivel, que aceita cor de fora)
		// e a cor (a do jogador). Uma folha certa com a cor da forma daria lilas; a cor certa numa
		// folha pre-colorida nao chegaria a pixel nenhum. As duas linhas abaixo sao uma cada.
		Conferir(Jandirus.Core.Forms.Catalogo.Folha(mis) == FolhaDeAura.Base
			  && Jandirus.Core.Forms.Catalogo.Folha(mis)
				 != Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def("ssg")),
				 $"a chama dele NAO e mais a do SSG -- e a folha colorivel, como a da base (deu "
			   + $"{Jandirus.Core.Forms.Catalogo.Folha(mis)})");
		Conferir(Jandirus.Core.Forms.Catalogo.ChamaDoJogador(mis)
			  && Aura.CorDaChamaDe(mis, CorPessoalDeTeste).IsEqualApprox(CorPessoalDeTeste),
				 $"-- e a cor dela e a do JOGADOR (a sorteada, ver `CorPessoalDeTeste`) e nao o "
			   + $"#{mis.Aura} do catalogo (deu #{Aura.CorDaChamaDe(mis, CorPessoalDeTeste).ToHtml(false)})");

		// --- 3. AS MESMAS AUSENCIAS, NUM BONECO ---
		// O dado pode estar certo e o boneco mudar assim mesmo: basta uma linha do `VestirCabeloDaForma`
		// pintar antes de perguntar. Aqui a pergunta e literal -- veste o Mistico e compara CADA canal
		// com o que o boneco tinha na base.
		//
		// O BONECO E PROPRIO (como os do cabelo e o do corpo inchado) e nao o corpo local: o corpo local
		// e medido por outros dez blocos deste arquivo, e vesti-lo no meio embaralharia todos eles.
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados))
		{ Conferir(false, "a bancada acha o catalogo visual pra o boneco do Mistico"); return; }
		var cat = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

		var boneco = new CharacterVisual { Name = "BonecoDoMistico" };
		AddChild(boneco);
		try
		{
			boneco.Vestir(cat, new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku" },
						  "Saiyan", "Male");
			boneco.MostrarRabo(true);
			boneco.VestirCabeloDaForma(null);

			string cabeloBase = boneco.CabeloDeTeste;
			Vector3 tintaBase = boneco.TintaDoCabeloDeTeste!.Value.Tinta;
			Vector3 raboBase = boneco.TintaDoRaboDeTeste!.Value;
			Vector3 olhoBase = boneco.TintaDoOlhoDeTeste!.Value;

			boneco.VestirCabeloDaForma(mis);
			boneco.ColadasDaForma(mis);
			boneco.CorpoDaForma(mis.Corpo);

			Conferir(boneco.CabeloDeTeste == cabeloBase,
					 $"no boneco: o Mistico fica com o penteado do jogador ({boneco.CabeloDeTeste.GetFile()})");
			Conferir(boneco.TintaDoCabeloDeTeste!.Value.Tinta.IsEqualApprox(tintaBase),
					 "-- na cor da ficha, sem tinta de forma");
			Conferir(boneco.TintaDoOlhoDeTeste!.Value.IsEqualApprox(olhoBase),
					 "-- com o olho da ficha");
			Conferir(boneco.TintaDoRaboDeTeste!.Value.IsEqualApprox(raboBase),
					 "-- e o rabo da ficha");
			Conferir(boneco.ColadasDeTeste == 0, "-- sem camada colada");
			Conferir(!boneco.CorpoDaFormaDeTeste, "-- e sem camada de corpo: o boneco e o mesmo de antes");

			// E O CONTROLE: o BEAST, do mesmo boneco, MUDA. Sem ele as seis linhas acima passariam num
			// `VestirCabeloDaForma` de corpo vazio -- o defeito que ja custou meses a tinta do cabelo.
			if (Jandirus.Core.Forms.Catalogo.Def("beast") is { } fera2)
			{
				boneco.VestirCabeloDaForma(fera2);
				Conferir(boneco.CabeloDeTeste != cabeloBase
					  || !boneco.TintaDoCabeloDeTeste!.Value.Tinta.IsEqualApprox(tintaBase),
						 "e o BEAST, no mesmo boneco, muda o cabelo -- o teste acima nao e vazio");
			}
			boneco.VestirCabeloDaForma(null);
		}
		finally
		{
			if (IsInstanceValid(boneco)) boneco.Free();
		}
	}

	// =====================================================================
	// 3a-ter-H. REVERTER DESFAZ **TODOS** -- overlay, olho, rabo e corpo
	// =====================================================================
	/// <summary>
	/// *"reverter desfaz TODOS: overlay, olhos, rabo e corpo"*.
	///
	/// ============================ POR QUE ISTO PRECISA DE UMA FORMA SO, E DELA ============================
	/// Cada canal ja tem a sua volta cobrada em algum lugar desta bancada -- e sempre SOZINHO, num
	/// boneco montado pra ele. O que ninguem media e a volta INTEIRA, e ela e outra coisa: os quatro
	/// canais sao escritos por tres metodos diferentes, em ordem que ja foi defeito uma vez (o
	/// `PintarRabo` desistia quando havia corpo proprio, e pintar antes do `CorpoDaForma` deixava tinta
	/// ARMADA pra reaparecer no reverter -- ver `Transformacao.Vestir`).
	///
	/// O `legendary` e a unica forma do catalogo que acende os QUATRO ao mesmo tempo: duas coladas, olho
	/// sem iris, rabo verde e corpo musculoso. Ir e voltar nele exercita a ordem inteira de uma vez.
	///
	/// ============================ E PELO CAMINHO DO JOGO ============================
	/// `World.AoMudarForma`, que e por onde o pacote do servidor entra -- e nao as tres chamadas na mao.
	/// Chamar `ColadasDaForma(null)` daqui provaria que o metodo funciona, nao que alguem o CHAMA: o
	/// `VestirAFormaSemCena` ja teve uma copia com a ordem trocada, e e esse tipo de defeito que so o
	/// caminho de verdade mostra.
	/// ==========================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR: apague a linha `vis.ColadasDaForma(def)` do `VestirAFormaSemCena`
	/// -- a fagulha do Legendary fica grudada no corpo em forma base. Troque o `def?.Corpo ?? Nenhum`
	/// por `def?.Corpo ?? Musculoso` (ou esqueca o `??`) -- o lutador volta ao normal inchado.
	/// </summary>
	private void AVoltaDesfazTudo()
	{
		if (GetTree().Root.FindChild("World", true, false) is not Jandirus.Client.World mundo)
		{ Conferir(false, "achei o `World` pra a volta passar pelo caminho do jogo"); return; }
		int meuId = GameClient.Instance?.LocalId ?? 0;
		if (meuId == 0) { Conferir(false, "a bancada esta conectada pra a ida e volta inteira"); return; }
		if (GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D corpo
			|| corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis)
		{ Conferir(false, "o corpo local tem Visual pra a volta inteira"); return; }

		void Mudar(string de, string para)
		{
			var antes = new HashSet<Transformacao>(
				corpo.GetParent().GetChildren().OfType<Transformacao>().Where(IsInstanceValid));
			mundo.AoMudarForma(meuId, Jandirus.Core.Forms.Catalogo.Rede(de),
							   Jandirus.Core.Forms.Catalogo.Rede(para),
							   Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			foreach (Transformacao t in corpo.GetParent().GetChildren().OfType<Transformacao>())
				if (IsInstanceValid(t) && !antes.Contains(t)) t.Free();
		}

		string Base = Jandirus.Core.Forms.Catalogo.IdBase;
		vis.MostrarRabo(true);
		Mudar(Base, Base);

		// A LINHA DE BASE, lida DEPOIS de uma volta pra base -- as tintas so guardam o natural na
		// primeira chamada, e ler antes disso compararia um boneco "ainda nao guardado" com um guardado.
		string cabeloBase = vis.CabeloDeTeste;
		Vector3 tintaBase = vis.TintaDoCabeloDeTeste?.Tinta ?? Vector3.Zero;
		Vector3 raboBase = vis.TintaDoRaboDeTeste ?? Vector3.Zero;
		Vector3 olhoBase = vis.TintaDoOlhoDeTeste ?? Vector3.Zero;
		Conferir(vis.TintaDoRaboDeTeste != null && vis.TintaDoOlhoDeTeste != null,
				 "o corpo local tem rabo e olhos (senao a volta nao teria o que desfazer)");
		Conferir(vis.ColadasDeTeste == 0 && !vis.CorpoDaFormaDeTeste,
				 "e ele comeca na base, sem colada e sem camada de corpo");

		// --- A IDA: os quatro canais acesos de uma vez ---
		FormaDef lend = Jandirus.Core.Forms.Catalogo.Def("legendary")!;
		Mudar(Base, "legendary");

		Conferir(vis.ColadasDeTeste == 2,
				 $"o Legendary cola DUAS camadas no corpo ({vis.ColadasDeTeste})");
		Conferir(vis.CabeloDeTeste != cabeloBase,
				 $"-- e troca o penteado ({vis.CabeloDeTeste.GetFile()})");
		// A COR ESPERADA SAI DO CATALOGO e nao e "diferente da base": a cor da ficha do personagem de
		// bancada e escolhida no nascimento, e um dia ela pode COINCIDIR com a da forma -- ai
		// "diferente da base" reprovaria com tudo certo. Perguntar o valor tambem e mais forte.
		static Vector3 Do(string? hexa) => hexa is { } h
			? new Vector3(new Color(h).R, new Color(h).G, new Color(h).B) : Vector3.Zero;

		Conferir(vis.TintaDoOlhoDeTeste is { } oi
				 && oi.IsEqualApprox(Do(Jandirus.Core.Forms.Catalogo.CorDoOlho(lend))),
				 $"-- e pinta o olho de #{Jandirus.Core.Forms.Catalogo.CorDoOlho(lend)} "
			   + "(o verde da escada: o boneco de bancada nasce com as redeas na mao)");
		Conferir(vis.TintaDoRaboDeTeste is { } ri
				 && ri.IsEqualApprox(Do(Jandirus.Core.Forms.Catalogo.CorDoRabo(lend))),
				 $"-- e o rabo de #{Jandirus.Core.Forms.Catalogo.CorDoRabo(lend)} (o verde do Legendary)");
		// O CORPO SO INCHA SE HOUVER FOLHA PRA ESTA PELE (nao ha musculosa feminina, nem de Namekuseijin).
		// A pergunta e feita pelo catalogo pra o teste continuar valendo com um personagem de bancada
		// qualquer: onde nao ha arte, o certo e NAO inchar, e a volta nao tem o que desfazer.
		bool inchou = vis.CorpoDaFormaDeTeste;
		Conferir(lend.Corpo == CorpoDeForma.Musculoso,
				 $"e o catalogo manda inchar nesta forma ({lend.Corpo})");
		_passos.Add($"  --     o corpo de bancada {(inchou ? "INCHOU" : "nao inchou -- pele sem folha musculosa")}");

		// --- A VOLTA: os quatro apagados, na mesma chamada ---
		Mudar("legendary", Base);

		Conferir(vis.ColadasDeTeste == 0,
				 $"voltar pra base tira as camadas COLADAS ({vis.ColadasDeTeste})");
		Conferir(vis.CabeloDeTeste == cabeloBase,
				 $"-- devolve o PENTEADO da ficha ({vis.CabeloDeTeste.GetFile()})");
		Conferir(vis.TintaDoCabeloDeTeste!.Value.Tinta.IsEqualApprox(tintaBase),
				 "-- e a cor de cabelo da ficha");
		Conferir(vis.TintaDoOlhoDeTeste!.Value.IsEqualApprox(olhoBase),
				 "-- devolve a cor dos OLHOS da ficha");
		Conferir(vis.TintaDoRaboDeTeste!.Value.IsEqualApprox(raboBase),
				 "-- devolve a cor do RABO da ficha");
		Conferir(!vis.CorpoDaFormaDeTeste, "-- e tira a camada de CORPO (o lutador desincha)");
		Conferir(vis.RaboVisivelDeTeste, "-- com o rabo de volta na tela");

		// ============================ E O CASO QUE ESCONDE O RABO ============================
		// O SSJ4 e o outro tipo de corpo proprio: a folha dele DESENHA o rabo, entao o `_rabo` base e
		// escondido e o `PintarRabo` desiste. Se a volta esquecesse qualquer uma das duas coisas, o
		// jogador voltaria ao normal sem cauda -- e sem cauda nao ha Oozaru, que e a porta do SSJ4.
		// Esta e a metade que a ida e volta do Legendary NAO exercita (o musculoso mantem o rabo).
		// ==================================================================================
		Mudar(Base, "ssj4");
		Conferir(vis.CorpoDaFormaDeTeste, "o SSJ4 poe a camada de corpo (a pelagem)");
		Conferir(!vis.RaboVisivelDeTeste, "-- e esconde o rabo base (a folha dele ja o desenha)");
		Conferir(vis.TintaDoRaboDeTeste!.Value.IsEqualApprox(raboBase),
				 "-- sem deixar tinta ARMADA no rabo escondido (o tombo da ordem do `Vestir`)");

		Mudar("ssj4", Base);
		Conferir(!vis.CorpoDaFormaDeTeste && vis.RaboVisivelDeTeste,
				 "e a volta devolve o corpo E o rabo");
		Conferir(vis.TintaDoOlhoDeTeste!.Value.IsEqualApprox(olhoBase)
			  && vis.TintaDoRaboDeTeste!.Value.IsEqualApprox(raboBase)
			  && vis.CabeloDeTeste == cabeloBase,
				 "-- com olho, rabo e penteado da ficha de volta");
	}

	// =====================================================================
	// 3a-quater. A FICHA QUE CHEGA NO **MEU** CORPO JA TRANSFORMADO
	// =====================================================================
	/// <summary>
	/// ============================ O RABO E O OLHO DEPOIS DE UM `CharacterVisual.Vestir` ============================
	/// A <see cref="AVoltaDesfazTudo"/> prova a ida e a volta pelo funil do jogo, mas ela nunca deixa
	/// uma FICHA passar no meio. E `Vestir` e o metodo que desfaz tinta sem pedir licenca: ele remonta
	/// as camadas, reescreve `_cabeloBase`, retinge o olho com a cor da ficha e derruba as travas que a
	/// forma usa pra saber o que devolver (`_tintaDoOlhoGuardada`, `_tintaDoCabeloGuardada`). Se nada
	/// repusesse a forma depois disso, o rabo e o olho voltariam ao normal no meio da transformacao --
	/// e ninguem veria defeito nenhum no log, porque nenhuma checagem passava por aqui.
	///
	/// ============================ POR QUE O CORPO **LOCAL**, E NAO SO UM FANTASMA ============================
	/// O bloco 5 da <see cref="AChegadaNaZona"/> ja cobre a ficha atrasada num corpo ALHEIO. Este cobre
	/// o corpo do DONO DA TELA, e ele nao e o mesmo caso por dois motivos:
	///
	///   * o jogador RECEBE O PROPRIO `PeerLook`. `GameServer.TrocarAparencias` manda a ficha do
	///     recem-chegado pra todo mundo da zona percorrendo a lista inteira -- e a guarda `outro != novo`
	///     so protege o SEGUNDO envio, o dos outros pra ele. Ou seja `AoReceberAparencia(meuId, ...)` e
	///     caminho de jogo, e ele roda no LOGIN e em TODA TROCA DE ZONA;
	///   * e so o corpo local tem ficha de VERDADE. O fantasma nasce com um `Appearance()` vazio, onde
	///     "a cor da ficha" e zero -- e zero e justamente o valor neutro do uniform. Um teste que so
	///     tivesse o fantasma nao distinguiria "a forma sobreviveu" de "a ficha nao tinha o que pintar".
	///
	/// Trocar de zona transformado e o caso real: e literalmente o que o jogador faz ao voar pra outro
	/// planeta em Super Saiyajin.
	///
	/// ============================ A FICHA E ADULTERADA DE PROPOSITO, E DEVOLVIDA DEPOIS ============================
	/// A cor de olho que entra aqui e um VERMELHO gritante que nao existe em forma nenhuma do catalogo.
	/// E isso que da as duas leituras opostas: se a forma sobreviver, o olho fica na cor do Legendary;
	/// se a ficha despir a forma, o olho fica VERMELHO -- e o log diz qual dos dois aconteceu, em vez de
	/// dizer so "diferente do esperado".
	///
	/// E A FICHA ORIGINAL VOLTA NO FIM (ver `World.LookDeTeste`): o `_looks` e permanente, e uma ficha
	/// adulterada deixada la sairia nas FOTOS que a bancada tira depois -- um teste mentindo sobre o
	/// proximo.
	///
	/// COMO REPROVA SE A REGRA SUMIR: troque o `VestirCorpoInteiro` do `World.AoReceberAparencia` por um
	/// `CharacterVisual.Vestir` direto (que era como ele era) -- o olho sai VERMELHO e o rabo perde o
	/// verde, e as duas linhas do meio caem juntas.
	/// ==========================================================================================================
	/// </summary>
	private void ORaboEOOlhoSobrevivemAFicha()
	{
		if (GetTree().Root.FindChild("World", true, false) is not Jandirus.Client.World mundo)
		{ Conferir(false, "achei o `World` pra a ficha passar pelo caminho do jogo"); return; }
		int meuId = GameClient.Instance?.LocalId ?? 0;
		if (meuId == 0) { Conferir(false, "a bancada esta conectada pra a ficha atrasada"); return; }
		if (GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D corpo
			|| corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis)
		{ Conferir(false, "o corpo local tem Visual pra a ficha atrasada"); return; }

		// A FICHA DE VERDADE, e ela e a primeira afirmacao do bloco: se o jogador NAO tem look
		// guardado, entao ele nunca recebeu o proprio `PeerLook` -- e ai o caminho que este teste
		// existe pra medir nao existe em jogo, o que e um achado e nao um motivo pra sair calado.
		if (mundo.LookDeTeste(meuId) is not { } ficha)
		{
			Conferir(false, "o jogador recebeu o PROPRIO `PeerLook` (`_looks[meuId]` escrito) -- "
						  + "sem isso o corpo local nunca passa pelo `VestirCorpoInteiro` em jogo");
			return;
		}
		Conferir(true, $"o jogador recebeu o proprio `PeerLook` ({ficha.Raca}/{ficha.Genero}) -- "
					 + "trocar de zona transformado passa por aqui");

		void Mudar(string de, string para)
		{
			var antes = new HashSet<Transformacao>(
				corpo.GetParent().GetChildren().OfType<Transformacao>().Where(IsInstanceValid));
			mundo.AoMudarForma(meuId, Jandirus.Core.Forms.Catalogo.Rede(de),
							   Jandirus.Core.Forms.Catalogo.Rede(para),
							   Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			foreach (Transformacao t in corpo.GetParent().GetChildren().OfType<Transformacao>())
				if (IsInstanceValid(t) && !antes.Contains(t)) t.Free();
		}

		static Vector3 Do(string? hexa) => hexa is { } h
			? new Vector3(new Color(h).R, new Color(h).G, new Color(h).B) : Vector3.Zero;

		string Base = Jandirus.Core.Forms.Catalogo.IdBase;
		FormaDef lend = Jandirus.Core.Forms.Catalogo.Def("legendary")!;
		vis.MostrarRabo(true);
		Mudar(Base, Base);

		Vector3 raboDaForma = Do(Jandirus.Core.Forms.Catalogo.CorDoRabo(lend));
		Vector3 olhoDaForma = Do(Jandirus.Core.Forms.Catalogo.CorDoOlho(lend));
		// AS DUAS CORES PRECISAM SER DISTINGUIVEIS DO VERMELHO DA SONDA, senao as duas leituras opostas
		// dariam o mesmo numero e a medida seria vazia. (Hoje o Legendary da verde no rabo e verde da
		// escada no olho -- ver `Catalogo.CorDoOlho(d, semRedeas)`, o branco sem iris so aparece com a
		// furia dirigindo; a linha existe pra o dia em que a tabela do dono mudar.)
		var vermelho = new Jandirus.Core.Appearance.Rgb(255, 0, 0);
		var vermelhoV = new Vector3(1f, 0f, 0f);
		Conferir(!raboDaForma.IsEqualApprox(vermelhoV) && !olhoDaForma.IsEqualApprox(vermelhoV),
				 $"o Legendary pinta rabo e olho em cores que dao pra distinguir da sonda vermelha "
			   + $"(rabo {raboDaForma}, olho {olhoDaForma})");

		Mudar(Base, "legendary");
		Conferir(vis.TintaDoRaboDeTeste is { } r0 && r0.IsEqualApprox(raboDaForma)
			  && vis.TintaDoOlhoDeTeste is { } o0 && o0.IsEqualApprox(olhoDaForma),
				 "transformado pelo funil do jogo, o corpo local tem rabo e olho da FORMA");

		// --- A FICHA CHEGA NO CORPO JA TRANSFORMADO -------------------------------
		Jandirus.Core.Appearance.Appearance sonda = ficha.Ap.Copiar();
		sonda.CorOlho = vermelho;
		mundo.AoReceberAparencia(meuId, GameClient.Instance?.LocalName ?? "Eu",
								 ficha.Raca, ficha.Genero, sonda);

		Vector3? raboDepois = vis.TintaDoRaboDeTeste;
		Vector3? olhoDepois = vis.TintaDoOlhoDeTeste;
		Conferir(olhoDepois is { } od && od.IsEqualApprox(olhoDaForma),
				 $"a ficha que chega no corpo JA TRANSFORMADO nao despe o OLHO "
			   + $"(esperado {olhoDaForma}, medido {olhoDepois}"
			   + $"{(olhoDepois is { } ov && ov.IsEqualApprox(vermelhoV) ? " -- e a cor da FICHA, a forma foi despida" : "")})");
		Conferir(raboDepois is { } rd && rd.IsEqualApprox(raboDaForma),
				 $"-- nem o RABO (esperado {raboDaForma}, medido {raboDepois})");
		// AS OUTRAS TRES CAMADAS JUNTO, porque `Vestir` mexe nas tres e o rabo e o olho sozinhos nao
		// contariam a historia inteira: um `VestirCorpoInteiro` que repusesse so a tinta deixaria o
		// jogador de cabelo de ficha e sem as coladas, transformado.
		Conferir(vis.ColadasDeTeste == 2, $"-- e as COLADAS continuam no corpo ({vis.ColadasDeTeste})");

		// --- E A VOLTA LE A FICHA **NOVA**, E NAO UMA COPIA VELHA -----------------
		// ============================ O QUE ESTA LINHA MEDE, E POR QUE ELA E O OLHO ============================
		// "Sair da forma devolve a cor da ficha" e servido pela `CharacterVisual.BaseDaFicha`, que
		// PERGUNTA a ficha guardada (`_ficha`, escrita pelo `Vestir`) em vez de consultar uma copia
		// tirada na primeira pintura. A sonda vermelha chegou DEPOIS da transformacao: se a volta der
		// vermelho, a base esta mesmo sendo derivada da ficha em uso; se der a cor de antes, ela voltou
		// a ser uma copia velha -- que e a forma exata do defeito que aquele bloco existe pra impedir.
		//
		// E ISTO E O TESTE DE FORA DAQUELA REGRA. Trocar de cor no guarda-roupa DENTRO de uma
		// transformacao e o caso de jogo que produz esta leitura, e ele nao tem outro teste.
		//
		// O RABO VEM JUNTO E DE PROPOSITO. A ficha nao tem cor de rabo -- `Vestir` nunca o tinge --,
		// entao a base dele e a arte crua (`Tinta.Nenhuma`), por DERIVACAO e nao por caso especial.
		// Por isso a linha dele pergunta apenas que ele LARGOU o verde do Legendary: cravar um valor
		// aqui transformaria "a ficha nao tem cor de rabo" num numero copiado, que e como as duas
		// pontas voltam a discordar no dia em que a ficha ganhar essa cor.
		// ==================================================================================================
		Mudar("legendary", Base);
		Conferir(vis.TintaDoOlhoDeTeste is { } ov2 && ov2.IsEqualApprox(vermelhoV),
				 $"e a volta devolve a cor da ficha NOVA (a que chegou durante a forma), nao a de antes "
			   + $"(esperado {vermelhoV}, medido {vis.TintaDoOlhoDeTeste})");
		Conferir(vis.TintaDoRaboDeTeste is { } rv2 && !rv2.IsEqualApprox(raboDaForma),
				 $"-- e o rabo larga o verde do Legendary ({vis.TintaDoRaboDeTeste})");

		// --- E A FICHA DE VERDADE VOLTA PRO LUGAR --------------------------------
		mundo.AoReceberAparencia(meuId, GameClient.Instance?.LocalName ?? "Eu",
								 ficha.Raca, ficha.Genero, ficha.Ap);
		Conferir(vis.TintaDoOlhoDeTeste is { } fim && fim.IsEqualApprox(ComoAFichaPinta(ficha.Ap.CorOlho)),
				 "e a ficha ORIGINAL volta pro corpo (as fotos daqui pra frente nao herdam a sonda)");
	}

	/// <summary>
	/// A cor da FICHA como o uniform a ve -- nula = sem tinta, que e o zero do shader.
	///
	/// E a mesma conta do `CharacterVisual.Tinta.DaFicha`, escrita de novo porque aquele tipo e
	/// privado do visual. Repetida de proposito e so aqui: a bancada tem que poder dizer o valor
	/// ESPERADO sem pedir a resposta pro codigo que ela esta medindo.
	/// </summary>
	private static Vector3 ComoAFichaPinta(Jandirus.Core.Appearance.Rgb? cor) => cor is { } c
		? new Vector3(c.R / 255f, c.G / 255f, c.B / 255f) : Vector3.Zero;

	// =====================================================================
	// 3a-PIXEL. O PAR (COR + MODO), E A COR QUE SAI NA TELA
	// =====================================================================
	/// <summary>
	/// ============================ O CEGO QUE JA CUSTOU CARO TRES VEZES ============================
	/// Tudo o que esta bancada sabia sobre tinta ate aqui vinha de `GetShaderParameter("tinta")` -- ou
	/// seja, **do que o C# escreveu**, e nao da cor que sai na tela. Isso e cego pras duas coisas que
	/// produziram o relato do dono (*"parece q tem algo pintando o cabelo, e o rabo, de pretos"*):
	///
	///   1. **O PAR.** `tinta` sozinha nao descreve desenho nenhum. `(0,0,0)` em SOMA e a arte INTACTA;
	///      `(0,0,0)` em MATIZ e preto CHAPADO (`tinta * luz * 2` com tinta zero e zero). O mesmo valor,
	///      dois desenhos opostos -- e uma leitura que so ve a cor aprova os dois pelo mesmo motivo
	///      ("sem tinta, ok"). Pior: `CharacterVisual` so tem propriedade de teste pro cabelo, pro olho e
	///      pro rabo, e a camada que MAIS usa matiz -- a roupa -- nunca teve.
	///   2. **O PIXEL.** A causa real daquele relato foi uma linha do `.gdshader` (`COLOR = c * COLOR`)
	///      elevando a folha ao quadrado, **com todos os uniformes certos**. Nenhuma leitura de uniform,
	///      de camada nenhuma, teria mudado de valor. So a foto ve isso.
	///
	/// Este bloco fecha os dois buracos, e o pixel e medido DE VERDADE: uma copia que carrega o material
	/// da camada viva (com os uniformes exatos daquele instante) desenha num `SubViewport`, e a imagem
	/// volta contada tom a tom. Nao e o replicador em C# (isso e o <see cref="EmMatiz"/>, e ele tem valor
	/// proprio); e a GPU rodando o `Personagem.gdshader` do repositorio sobre a arte do jogo.
	/// ==========================================================================================
	///
	/// ============================ POR QUE AS FOTOS SAO REVELADAS UM QUADRO DEPOIS ============================
	/// Tentei fazer isto sincrono, com `RenderingServer.ForceDraw()` logo depois de montar o viewport, e
	/// **nao funciona**: a foto volta 32x32 com alfa ZERO em toda camada. Chamado de dentro do `_Process`
	/// o `ForceDraw` nao desenha um alvo que acabou de nascer, e o `GetImage` le o buffer limpo. Isso
	/// merece registro porque a leitura ingenua ("o viewport existe, logo desenhou") entrega uma bancada
	/// VERDE medindo nada -- que e o defeito que esta bancada mais documenta.
	///
	/// Entao as fotos sao AGENDADAS enquanto as familias correm (cada uma leva uma COPIA do material,
	/// congelando os uniformes daquele instante) e reveladas no quadro seguinte, todas de uma vez. E por
	/// isso as afirmacoes do pixel moram juntas em <see cref="AsAfirmacoesDoPixel"/>, e nao ao lado das
	/// do par: entre agendar e revelar passa um quadro, e nesse meio tempo o boneco ja mudou de forma
	/// tres vezes.
	/// ==================================================================================================
	///
	/// AS CINCO FAMILIAS, e o que cada uma reprova:
	///
	///   1. **o cabelo do personagem BASE mantem mais de um tom.** E a afirmacao mais honesta que cabe
	///      num relato de cor, porque "perdeu o relevo" E "virou um tom so" -- ela reprova o achatamento
	///      **sem depender de qual cor e a certa**;
	///   2. **nenhuma camada fica em MATIZ com tinta PRETA**, em forma nenhuma do catalogo, **na ida e na
	///      VOLTA** -- reverter e onde o guarda entra;
	///   3. **o RABO**, nos dois sentidos: cru na base (que e o CERTO -- ver o bloco dele) e **nao** cru
	///      na forma que o pinta, que e onde o corpo alheio o perdia;
	///   4. **a mesma coisa depois de um `Vestir`** e **depois de um ciclo transformar -> reverter**;
	///   5. **o controle**, sem o qual tudo-preto passa verde.
	/// </summary>
	private void OParEOPixelDaTinta()
	{
		if (GetTree().Root.FindChild("World", true, false) is not Jandirus.Client.World mundo)
		{ Conferir(false, "achei o `World` pra medir o par e o pixel pelo caminho do jogo"); return; }
		int meuId = GameClient.Instance?.LocalId ?? 0;
		if (meuId == 0) { Conferir(false, "a bancada esta conectada pra medir o par e o pixel"); return; }
		if (GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D corpo
			|| corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis)
		{ Conferir(false, "o corpo local tem Visual pra medir o par e o pixel"); return; }

		void Mudar(string de, string para)
		{
			var antes = new HashSet<Transformacao>(
				corpo.GetParent().GetChildren().OfType<Transformacao>().Where(IsInstanceValid));
			mundo.AoMudarForma(meuId, Jandirus.Core.Forms.Catalogo.Rede(de),
							   Jandirus.Core.Forms.Catalogo.Rede(para),
							   Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			foreach (Transformacao t in corpo.GetParent().GetChildren().OfType<Transformacao>())
				if (IsInstanceValid(t) && !antes.Contains(t)) t.Free();
		}

		string Base = Jandirus.Core.Forms.Catalogo.IdBase;
		vis.MostrarRabo(true);
		Mudar(Base, Base);

		OCabeloBaseMantemRelevo(vis);
		NenhumaCamadaEmMatizComPreto(vis, Mudar, Base, mundo.LookDeTeste(meuId)?.Ap);
		// LOGO DEPOIS DELA de proposito: e a conferencia da UNICA excecao que ela abre, e uma excecao
		// que ninguem confere e um buraco. Ver o cabecalho.
		ARoupaPretaEUmaEscolha(vis, mundo, meuId);
		ORaboCruEOnaoCru(vis, Mudar, Base);
		ODepoisDoVestirEDoCiclo(vis, mundo, meuId, Mudar, Base);
		OControleDaMedida(vis);

		_passos.Add($"  --     {_fila.Count} foto(s) agendada(s) -- revelam no quadro seguinte");
	}

	// ---------------------------------------------------------------------
	// A CAMARA ESCURA: agendar, revelar, contar
	// ---------------------------------------------------------------------
	/// <summary>
	/// Uma foto pedida: o rotulo pelo qual as afirmacoes a procuram, o viewport que a desenha e os tons
	/// da ARTE daquele MESMO quadro -- a unica base de comparacao honesta (uma folha inteira tem dezenas
	/// de tons e um quadro tem meia duzia; comparar as duas contagens daria diferenca enorme com tudo
	/// certo).
	/// </summary>
	private readonly List<(string Rotulo, SubViewport Tela, string[] Arte)> _fila = [];

	/// <summary>O que cada foto revelou. Chave = o rotulo do agendamento.</summary>
	private readonly Dictionary<string, (string[] Arte, string[] Tela)> _medidas = [];

	/// <summary>
	/// AGENDA a foto desta camada. A copia carrega uma **duplicata** do material, e isso e o ponto: os
	/// uniformes ficam congelados no instante do agendamento, entao o boneco pode transformar tres vezes
	/// antes de a foto ser revelada sem que a medida mude.
	///
	/// (Compartilhar o material seria mais direto e estaria ERRADO aqui, justamente por isso: as fotos
	/// sao reveladas todas juntas, um quadro depois, e todas mostrariam o ULTIMO estado.)
	/// </summary>
	private void AgendarFoto(string rotulo, AnimatedSprite2D? fonte)
	{
		if (fonte is null || !IsInstanceValid(fonte) || fonte.SpriteFrames is not { } fr) return;
		string anim = fonte.Animation;
		if (!fr.HasAnimation(anim)) return;
		int total = fr.GetFrameCount(anim);
		if (total <= 0) return;
		int quadro = Mathf.Clamp(fonte.Frame, 0, total - 1);
		if (fr.GetFrameTexture(anim, quadro) is not { } tex) return;
		Vector2I tam = (Vector2I)tex.GetSize();
		if (tam.X <= 0 || tam.Y <= 0) return;
		if (tex.GetImage() is not { } arte || arte.IsEmpty()) return;

		// MUNDO PROPRIO de proposito: sem isso a copia entraria no canvas do jogo (aparecendo na tela e
		// nas fotos que esta bancada tira depois) e herdaria o `CanvasModulate` do ceu -- que o Godot
		// aplica DEPOIS do fragment e que abaixaria todo tom medido, misturando "o shader achatou" com
		// "e de noite". Aqui a unica coisa que mexe no pixel e o shader.
		var tela = new SubViewport { World2D = new World2D() };
		AddChild(tela);
		// AS PROPRIEDADES DEPOIS DE ENTRAR NA ARVORE: fora de um `SubViewportContainer` o modo padrao
		// (`WhenVisible`) quer dizer NUNCA, e um viewport que nunca desenha devolve alfa zero calado.
		tela.Size = tam;
		tela.TransparentBg = true;
		tela.Disable3D = true;
		tela.RenderTargetClearMode = SubViewport.ClearMode.Always;
		tela.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

		var copia = new AnimatedSprite2D
		{
			SpriteFrames = fr,
			Centered = false,
			Position = Vector2.Zero,
			// `Nearest` e obrigatorio: filtro linear inventa tons na borda e a contagem sobe sozinha --
			// o que aprovaria um achatamento por interpolacao.
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Material = (Material?)fonte.Material?.Duplicate(),
			Modulate = fonte.Modulate,
			SelfModulate = fonte.SelfModulate,
		};
		tela.AddChild(copia);
		copia.Animation = anim;
		copia.Frame = quadro;
		copia.Pause();

		_fila.Add((rotulo, tela, ContarTons(arte).Tons));
	}

	/// <summary>
	/// REVELA tudo o que estava agendado (ja passou um quadro, entao os viewports desenharam), guarda as
	/// medidas por rotulo e chama as afirmacoes. Depois derruba os viewports -- eles nao podem sobreviver
	/// as fotos que a bancada tira mais adiante.
	/// </summary>
	private void RevelarAFila()
	{
		int comPixel = 0;
		foreach ((string rotulo, SubViewport tela, string[] arte) in _fila)
		{
			if (IsInstanceValid(tela) && tela.GetTexture()?.GetImage() is { } img && !img.IsEmpty())
			{
				(string[] tons, _, int px) = ContarTons(img);
				_medidas[rotulo] = (arte, tons);
				if (px > 0) comPixel++;
				_passos.Add($"  --     foto `{rotulo}`: arte [{string.Join(" ", arte)}] -> tela "
						  + $"[{string.Join(" ", tons)}] ({px}px)");
			}
			if (IsInstanceValid(tela)) tela.QueueFree();
		}
		_fila.Clear();

		// ============================ O PISO QUE IMPEDE A FAMILIA DE SUMIR CALADA ============================
		// Toda afirmacao do pixel abaixo comeca por "se houver medida". Um `SubViewport` que pare de
		// desenhar -- foi o que aconteceu com o `ForceDraw`, e nao houve UMA linha vermelha -- apagaria a
		// familia inteira sem sinal nenhum. Esta linha e o sinal.
		//
		// SEM RENDERIZADOR ela nao se aplica, e o log diz isso em voz alta em vez de ficar verde: no
		// headless nao ha quadro pra ler e a bancada inteira ja avisa o mesmo nas fotos.
		bool temTela = DisplayServer.GetName() != "headless";
		if (temTela)
			Conferir(comPixel >= 8,
					 $"a rodada fotografou {comPixel} camada(s) COM pixel -- uma varredura que se esvazia "
				   + "nao passa por aqui");
		else
			_passos.Add($"  --     SEM PIXEL nesta rodada (headless): {comPixel} foto(s) com conteudo, "
					  + "so as familias do PAR valeram");

		AsAfirmacoesDoPixel();
	}

	/// <summary>
	/// Os tons OPACOS distintos de uma imagem, e quantos pixels foram contados.
	///
	/// O corte de alfa e 0,5 e nao "&gt; 0": a borda de um sprite tem pixels semitransparentes que a
	/// composicao sobre fundo transparente devolve em cores intermediarias inexistentes na arte --
	/// conta-los inflaria a contagem dos dois lados por motivos diferentes e a comparacao perderia o pe.
	/// </summary>
	private static (string[] Tons, float Brilho, int Pixels) ContarTons(Image img)
	{
		var vistos = new HashSet<int>();
		double soma = 0;
		int n = 0;
		for (int y = 0; y < img.GetHeight(); y++)
			for (int x = 0; x < img.GetWidth(); x++)
			{
				Color c = img.GetPixel(x, y);
				if (c.A < 0.5f) continue;
				vistos.Add((c.R8 << 16) | (c.G8 << 8) | c.B8);
				soma += c.R8 * 0.299 + c.G8 * 0.587 + c.B8 * 0.114;
				n++;
			}
		return ([.. vistos.Order().Select(k => $"{k:x6}")], n == 0 ? 0f : (float)(soma / n / 255.0), n);
	}

	/// <summary>O par (cor + modo) armado nesta camada agora. Nulo quando ela nao tem material de shader.</summary>
	private static (Vector3 Cor, int Modo)? ParDaCamada(AnimatedSprite2D? s) =>
		s != null && IsInstanceValid(s) && s.Material is ShaderMaterial m
			? (m.GetShaderParameter("tinta").AsVector3(), m.GetShaderParameter("tinta_modo").AsInt32())
			: null;

	/// <summary>A camada de nome <paramref name="nome"/> no boneco AGORA. Reprocurada sempre, de proposito:
	/// `Vestir` recria sprites, e uma referencia guardada mediria um node que ja saiu da arvore.</summary>
	private static AnimatedSprite2D? Camada(CharacterVisual v, string nome) =>
		v.CamadasDeTeste().FirstOrDefault(c => c.Nome == nome).Sprite;

	// ---------------------------------------------------------------------
	// FAMILIA 1 -- O CABELO DO PERSONAGEM BASE MANTEM RELEVO
	// ---------------------------------------------------------------------
	/// <summary>
	/// "Virou um borrao chapado" e "sobrou um tom so" sao a mesma frase dita duas vezes -- por isso esta e
	/// a afirmacao mais honesta que cabe num relato de COR: ela reprova o achatamento **sem depender de
	/// qual cor e a certa**. Um dia alguem pode escurecer o cabelo de proposito; ninguem vai querer o
	/// cabelo com um tom so.
	///
	/// E A LINHA FORTE E A DA IDENTIDADE. Na base o cabelo de um Saiyajin nao leva tinta nenhuma
	/// (`VisualCatalog.CabeloNatural`), e sem tinta a SOMA e a identidade -- entao a tela tem que sair
	/// **igual a arte**, tom por tom. Nao "parecida": igual. E o unico estado em que o `COLOR = c * COLOR`
	/// nao tem onde se esconder (ele derrubava o `#6b6b6b` da mecha pra `#2d2d2d` e fundia `#171717` e
	/// `#151515` no mesmo `#020202`).
	///
	/// COMO REPROVA: devolva `COLOR = c * COLOR;` ao shader -- cai a identidade em todas as fotos e cai a
	/// contagem, porque dois tons se fundem. Faca o `TingirCabelo` cair em matiz na base -- a contagem
	/// despenca pra 1 e a linha do PAR cai junto, um quadro antes.
	/// </summary>
	private void OCabeloBaseMantemRelevo(CharacterVisual vis)
	{
		if (Camada(vis, "cabelo") is not { } cab)
		{ Conferir(false, "o boneco de bancada tem camada de CABELO pra medir o relevo"); return; }

		// O PAR PRIMEIRO, e ele vale ate no headless: o penteado do jogador e arte quase toda preta, e
		// preto em MATIZ e o proprio borrao chapado. "Sem tinta" so e estado inofensivo em SOMA.
		Conferir(ParDaCamada(cab) is { } par && !(par.Modo == 1 && par.Cor.Length() < 0.02f),
				 $"o cabelo do personagem BASE nao esta em matiz com tinta preta ({FmtPar(ParDaCamada(cab))})");
		AgendarFoto("cabelo.base", cab);
	}

	// ---------------------------------------------------------------------
	// FAMILIA 2 -- NENHUMA CAMADA EM MATIZ COM PRETO, NA IDA E NA VOLTA
	// ---------------------------------------------------------------------
	/// <summary>
	/// ============================ POR QUE ESTA FAMILIA NAO E UM DETALHE ============================
	/// Os dois modos se comportam de forma OPOSTA com tinta zero: em SOMA a arte sai intacta, em MATIZ ela
	/// sai PRETA CHAPADA. Ou seja o mesmo `(0,0,0)` que e o estado de repouso de metade das camadas do jogo
	/// e, no outro modo, o defeito exato que o dono relatou -- e nenhuma leitura de `tinta` sozinha
	/// distingue os dois.
	///
	/// E NA VOLTA E QUE O GUARDA ENTRA. A ida escreve os dois uniformes na mesma linha
	/// (`CharacterVisual.Tinta.Escrever`), e por construcao nao ha como armar modo sem cor. **Reverter e
	/// outra historia**: e o gesto que DEVOLVE valores, e a forma classica de errar aqui e devolver a cor
	/// esquecendo o modo -- o que deixaria a camada em matiz com o preto da ficha, pra sempre. Por isso as
	/// duas metades, medidas separadas e nomeadas separadas no log.
	///
	/// ============================ E VARRE TODAS AS CAMADAS, NAO AS TRES DE SEMPRE ============================
	/// `CharacterVisual` tem propriedade de teste pro cabelo, pro olho e pro rabo. Roupa, corpo de forma e
	/// coladas nunca tiveram -- e a roupa e a UNICA camada do jogo que usa matiz o tempo todo, ou seja e a
	/// mais perto de cair neste defeito e a que menos gente mede. Quem entrega a lista completa e o
	/// `CharacterVisual.CamadasDeTeste`.
	/// ==================================================================================================
	///
	/// COMO REPROVA: faca o `Tinta.De(null, matiz: true)` devolver `new Tinta(Vector3.Zero, ModoMatiz)` em
	/// vez de `Nenhuma` -- toda forma de matiz cuja tinta sumir cai aqui, pelo nome e pela camada. Faca o
	/// gesto de reverter escrever so o uniform `tinta` -- cai a metade da VOLTA e so ela, que e o
	/// diagnostico pronto.
	/// </summary>
	private void NenhumaCamadaEmMatizComPreto(CharacterVisual vis, Action<string, string> mudar, string Base,
											  Jandirus.Core.Appearance.Appearance? ficha)
	{
		int formas = 0, leituras = 0, comMatiz = 0, pretoEscolhido = 0;
		var ruins = new List<string>();

		// ============================ A UNICA CAMADA EM QUE PRETO-EM-MATIZ E LEGITIMO ============================
		// A roupa sai de um `ColorPicker` livre (`CreationScreen.cs`), e o `CharacterVisual.Tinta` ja
		// documenta a decisao: *"preto e uma escolha que o jogador PODE fazer, e no matiz 'roupa preta' e
		// preto chapado mesmo -- e o que a peca deve parecer"*. Um guarda cru reprovaria esse jogador.
		//
		// A EXCECAO E DERIVADA DA FICHA, e nao um "pule a roupa": pergunta-se a peca `i` se a cor que ELA
		// pediu e preta. Se for, o preto na tela e o pedido; se nao for, matiz com preto naquela camada
		// continua sendo defeito -- e passa a ser um defeito NOVO que este bloco pega de graca (a peca
		// que perdeu a cor no caminho). Pular a camada perderia essa metade, que e a que interessa.
		//
		// (Peca SEM cor nao chega aqui: `Tinta.DaFicha(null, matiz: true)` devolve `Nenhuma`, que e SOMA.)
		// ==================================================================================================
		string Varrer(string quando)
		{
			(string[] achados, int lidas, int matiz, int pedidos) = VarrerMatizComPreto(vis, ficha);
			leituras += lidas; comMatiz += matiz; pretoEscolhido += pedidos;
			return string.Join(" ", achados.Select(a => $"{quando}/{a}"));
		}

		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			formas++;
			mudar(Base, d.Id);
			string naIda = Varrer("ida");
			// A VOLTA PELO FUNIL DO JOGO, e nao um `Vestir` de bancada: o defeito que esta metade procura
			// mora no gesto de REVERTER, e so o `AoMudarForma` passa por ele.
			mudar(d.Id, Base);
			string naVolta = Varrer("volta");

			if (naIda.Length > 0 || naVolta.Length > 0) ruins.Add($"`{d.Id}` {naIda} {naVolta}".Trim());
			Conferir(naIda.Length == 0 && naVolta.Length == 0,
					 $"`{d.Id}`: nenhuma camada em MATIZ com tinta preta, na ida e na volta"
				   + (naIda.Length + naVolta.Length > 0 ? $" -- {naIda} {naVolta}" : ""));
		}

		_passos.Add($"  --     par (cor+modo): {formas} formas x ida e volta, {leituras} leitura(s) de "
				  + $"camada, {comMatiz} delas em MATIZ, {pretoEscolhido} preto(s) PEDIDO(S) pela ficha");

		// OS DOIS PISOS, e o segundo e o que impede a familia de ser vazia: um jogo em que NINGUEM usasse
		// matiz passaria em cada linha acima sem que a varredura tivesse olhado um unico caso do modo
		// perigoso. Nao e hipotese -- e como esta familia deixaria de valer no dia em que alguem
		// "simplificasse" o shader tirando o modo.
		Conferir(formas >= 20 && leituras >= formas * 2,
				 $"a varredura do par percorreu o catalogo inteiro ({formas} formas, {leituras} camadas)");
		Conferir(comMatiz > 0,
				 $"-- e ela ENCONTROU o modo matiz em uso ({comMatiz} leitura(s)): a familia mede o modo "
			   + "perigoso, e nao um modo que ninguem liga");
		if (ruins.Count > 0) _passos.Add("  --     em matiz com preto: " + string.Join(" · ", ruins));
	}

	/// <summary>
	/// A VARREDURA EM SI, num instante so: que camadas estao em matiz com preto AGORA, quantas foram
	/// lidas, quantas estao em matiz e quantos pretos a FICHA pediu.
	///
	/// Mora sozinha porque tem DOIS chamadores, e o segundo e o que a protege: a
	/// <see cref="ARoupaPretaEUmaEscolha"/> a roda com uma ficha adulterada pra provar que a excecao da
	/// roupa preta e estreita. Duas copias desta conta permitiriam a excecao afrouxar de um lado so.
	/// </summary>
	private static (string[] Achados, int Lidas, int EmMatiz, int PretoPedido) VarrerMatizComPreto(
		CharacterVisual vis, Jandirus.Core.Appearance.Appearance? ficha)
	{
		var achados = new List<string>();
		int lidas = 0, matiz = 0, pedidos = 0;
		foreach ((string nome, AnimatedSprite2D s) in vis.CamadasDeTeste())
		{
			if (ParDaCamada(s) is not { } p) continue;
			lidas++;
			if (p.Modo == 1) matiz++;
			// O CORTE DE PRETO E GENEROSO de proposito: `0,02` de comprimento ja e mais escuro que
			// qualquer tinta do catalogo. Cobrar igualdade exata a zero deixaria passar um
			// `(0.004, 0, 0)`, que na tela e preto do mesmo jeito.
			if (p.Modo != 1 || p.Cor.Length() >= 0.02f) continue;
			if (OPretoFoiPedido(ficha, nome)) { pedidos++; continue; }
			achados.Add(nome);
		}
		return ([.. achados], lidas, matiz, pedidos);
	}

	/// <summary>A ficha PEDIU preto nesta camada? So a roupa pode responder que sim -- ver o bloco da excecao.</summary>
	private static bool OPretoFoiPedido(Jandirus.Core.Appearance.Appearance? ficha, string nome)
	{
		if (ficha is not { } f || !nome.StartsWith("roupa", StringComparison.Ordinal)
			|| !int.TryParse(nome.AsSpan(5), out int i) || i < 0 || i >= f.Roupa.Count) return false;
		return f.Roupa[i].Cor is { } c && c.R == 0 && c.G == 0 && c.B == 0;
	}

	/// <summary>
	/// ============================ A EXCECAO DA ROUPA PRETA, MEDIDA NOS DOIS SENTIDOS ============================
	/// A varredura acima abre UMA excecao (roupa cuja ficha PEDIU preto), e excecao nao conferida vira
	/// buraco: escrita como "pule a roupa" ela apagaria a camada que mais usa matiz no jogo inteiro, e
	/// ninguem notaria -- e literalmente o padrao que este port ja pagou varias vezes.
	///
	/// Entao ela e exercitada pelo caminho de jogo, com a ficha passando pela rede, nos DOIS sentidos:
	///
	///   * ficha pedindo PRETO   -> a camada fica mesmo em matiz com preto (o estado legitimo existe) e a
	///     varredura NAO acusa (a excecao cobre);
	///   * ficha pedindo VERMELHO -> a camada fica em matiz com vermelho, e ai a excecao nao se aplica --
	///     se a peca perdesse a cor no caminho, a varredura voltaria a acusar aquela camada.
	///
	/// Sem a segunda metade, "a roupa esta isenta" e indistinguivel de "a roupa nao e medida".
	///
	/// A FICHA ORIGINAL VOLTA NO FIM, e isso nao e higiene: as familias 4 e 5 medem o pixel a partir
	/// deste mesmo boneco, e uma roupa preta esquecida entraria nas fotos que a bancada tira depois.
	/// ==================================================================================================
	/// </summary>
	private void ARoupaPretaEUmaEscolha(CharacterVisual vis, Jandirus.Client.World mundo, int meuId)
	{
		if (mundo.LookDeTeste(meuId) is not { } ficha)
		{ Conferir(false, "o jogador tem ficha guardada pra exercitar a excecao da roupa preta"); return; }

		// O BONECO DE BANCADA PODE NASCER SEM ROUPA NENHUMA, e nasce: o `AutoEscolher` do `Boot` monta a
		// ficha minima. Vestir uma peca AQUI e o que impede a excecao de ficar sem exercicio na rodada
		// normal -- e uma excecao que nunca roda e um buraco que ninguem ve.
		Jandirus.Core.Appearance.Appearance Com(Jandirus.Core.Appearance.Rgb? cor)
		{
			Jandirus.Core.Appearance.Appearance sonda = ficha.Ap.Copiar();
			var peca = new Jandirus.Core.Appearance.PecaDeRoupa(
				sonda.Roupa.Count > 0 ? sonda.Roupa[0].Caminho : RoupaDoBoneco, cor);
			if (sonda.Roupa.Count > 0) sonda.Roupa[0] = peca; else sonda.Roupa.Add(peca);
			return sonda;
		}

		void Vestir(Jandirus.Core.Appearance.Appearance ap) =>
			mundo.AoReceberAparencia(meuId, GameClient.Instance?.LocalName ?? "Eu",
									 ficha.Raca, ficha.Genero, ap);

		AnimatedSprite2D? Peca0() =>
			vis.CamadasDeTeste().FirstOrDefault(c => c.Nome == "roupa0").Sprite;

		// --- 1. A FICHA PEDE PRETO: o estado legitimo existe, e a varredura DEIXA PASSAR ---
		Jandirus.Core.Appearance.Appearance emPreto = Com(new Jandirus.Core.Appearance.Rgb(0, 0, 0));
		Vestir(emPreto);
		Conferir(ParDaCamada(Peca0()) is { Modo: 1 } pp && pp.Cor.Length() < 0.02f,
				 $"uma roupa que a ficha pediu PRETA fica mesmo em MATIZ com preto ({FmtPar(ParDaCamada(Peca0()))}) "
			   + "-- e o estado legitimo que a varredura tem que deixar passar");
		(string[] comPreto, _, _, int pedidos) = VarrerMatizComPreto(vis, emPreto);
		Conferir(pedidos > 0 && !comPreto.Contains("roupa0"),
				 $"-- e a varredura NAO a acusa, porque a ficha e quem pediu ({pedidos} preto(s) pedido(s), "
			   + $"acusados: [{string.Join(" ", comPreto)}])");

		// --- 2. A MESMA CAMADA, COM A FICHA PEDINDO VERMELHO: a excecao NAO se aplica mais ---
		// O DEFEITO E INJETADO A MAO de proposito. Nao ha caminho de jogo que produza "a ficha pediu
		// vermelho e a camada ficou preta" -- que e exatamente o que se quer provar que seria VISTO. Sem
		// esta metade, "a roupa esta isenta" seria indistinguivel de "a roupa nao e medida".
		Jandirus.Core.Appearance.Appearance emVermelho = Com(new Jandirus.Core.Appearance.Rgb(220, 30, 30));
		Vestir(emVermelho);
		Conferir(ParDaCamada(Peca0()) is { Modo: 1 } pv
				 && pv.Cor.X > 0.5f && pv.Cor.Y < 0.3f && pv.Cor.Z < 0.3f,
				 $"e uma que a ficha pediu VERMELHA chega VERMELHA na camada ({FmtPar(ParDaCamada(Peca0()))})");
		if (Peca0()?.Material is ShaderMaterial mr)
		{
			mr.SetShaderParameter("tinta", Vector3.Zero);
			(string[] comVermelho, _, _, _) = VarrerMatizComPreto(vis, emVermelho);
			Conferir(comVermelho.Contains("roupa0"),
					 "-- e se essa peca PERDESSE a cor no caminho (matiz com preto, ficha pedindo vermelho) "
				   + $"a varredura acusaria, pelo nome ([{string.Join(" ", comVermelho)}]) -- a excecao e da "
				   + "COR pedida, e nao da camada");
		}
		else Conferir(false, "a peca de roupa tem material de shader pra o defeito ser injetado nela");

		// --- 3. A FICHA DE VERDADE VOLTA -- ver o cabecalho ---
		Vestir(ficha.Ap);
		(string[] noFim, _, _, _) = VarrerMatizComPreto(vis, ficha.Ap);
		Conferir(noFim.Length == 0,
				 $"e a ficha ORIGINAL volta pro corpo, sem sonda nenhuma pendurada ([{string.Join(" ", noFim)}]) "
			   + "-- as fotos daqui pra frente nao herdam a roupa de teste");
	}

	// ---------------------------------------------------------------------
	// FAMILIA 3 -- O RABO, NOS DOIS SENTIDOS
	// ---------------------------------------------------------------------
	/// <summary>
	/// ============================ "O RABO NAO PODE FICAR NA COR CRUA" ESTA INVERTIDO NA BASE ============================
	/// O enunciado que chegou ate aqui era *"o rabo do personagem base nao fica na cor CRUA do sprite
	/// (`#313131`/`#4d4d4d`)"*. Ele descreve um defeito REAL -- um registro de "guarde a cor pra devolver"
	/// que capturava um material recem-criado e devolvia zero pra sempre --, mas o defeito era o
	/// MECANISMO, e nao o valor. Hoje a base de cada camada e DERIVADA da ficha
	/// (`CharacterVisual.BaseDaFicha`), a ficha nao tem cor de rabo, e por isso a base dele e
	/// `Tinta.Nenhuma`: **a arte crua e a resposta certa na base**. Cravar o contrario reprovaria o
	/// codigo correto -- e uma bancada que reprova o certo e pior que nenhuma.
	///
	/// A afirmacao que sobrevive e o PAR dela, e e ela que esta escrita aqui:
	///
	///   * na BASE o rabo sai cru -- e "cru" quer dizer IGUAL A ARTE no pixel, nao "escuro";
	///   * na forma que o PINTA (`ssj1`, dourado) ele **nao** pode sair cru. Este e o lado que valia: e
	///     exatamente o estado em que o rabo do corpo ALHEIO foi flagrado com tinta `(0,0,0)` -- sprite
	///     cinza num personagem dourado -- porque nascia depois de a forma vestir;
	///   * e a volta devolve o cru, sem deixar o dourado armado.
	///
	/// Se um dia a ficha ganhar cor de rabo, a primeira linha muda junto com o `BaseDaFicha`: as duas
	/// pontas andam pela mesma derivacao, que e o que impede a assimetria de voltar.
	/// ==============================================================================================
	///
	/// COMO REPROVA: apague o `RevestirRaboRecemNascido` -- o rabo que nasce depois da forma fica cru e cai
	/// a linha do dourado. Faca o `BaseDaFicha` do rabo devolver uma cor -- caem as duas do cru.
	/// </summary>
	private void ORaboCruEOnaoCru(CharacterVisual vis, Action<string, string> mudar, string Base)
	{
		if (Camada(vis, "rabo") is null)
		{ Conferir(false, "o boneco de bancada tem RABO pra medir cru e nao-cru"); return; }

		// --- 1. A BASE: cru ---
		mudar(Base, Base);
		Conferir(ParDaCamada(Camada(vis, "rabo")) is { Modo: 0 } pb && pb.Cor.Length() < 0.02f,
				 $"na BASE o rabo esta sem tinta e em SOMA ({FmtPar(ParDaCamada(Camada(vis, "rabo")))}) -- a "
			   + "ficha nao tem cor de rabo, entao a arte crua E a resposta certa");
		AgendarFoto("rabo.base", Camada(vis, "rabo"));

		// --- 2. A FORMA QUE PINTA ---
		// O `ssj1` de proposito: e a forma cuja tinta de rabo (`dada26`) a bancada ja mede na conta
		// (`ORaboMedidoNoDesenho`), entao o valor esperado aqui nao e um numero novo -- e o mesmo
		// enunciado, agora conferido NA TELA.
		string? hexa = Jandirus.Core.Forms.Catalogo.CorDoRabo(Jandirus.Core.Forms.Catalogo.Def("ssj1"));
		Conferir(hexa != null,
				 $"o `ssj1` declara tinta de rabo pra esta familia medir (deu {hexa ?? "nenhuma"})");
		mudar(Base, "ssj1");
		Conferir(ParDaCamada(Camada(vis, "rabo")) is { Modo: 0 } pf && hexa != null
				 && pf.Cor.IsEqualApprox(new Vector3(new Color(hexa).R, new Color(hexa).G, new Color(hexa).B)),
				 $"em `ssj1` o rabo carrega a tinta da forma em SOMA ({FmtPar(ParDaCamada(Camada(vis, "rabo")))})");
		AgendarFoto("rabo.ssj1", Camada(vis, "rabo"));

		// --- 3. A VOLTA devolve o cru ---
		mudar("ssj1", Base);
		Conferir(ParDaCamada(Camada(vis, "rabo")) is { Modo: 0 } pv && pv.Cor.Length() < 0.02f,
				 $"e a volta devolve o rabo cru, sem dourado armado ({FmtPar(ParDaCamada(Camada(vis, "rabo")))})");
		AgendarFoto("rabo.volta", Camada(vis, "rabo"));
	}

	// ---------------------------------------------------------------------
	// FAMILIA 4 -- DEPOIS DE UM `Vestir` E DEPOIS DE UM CICLO
	// ---------------------------------------------------------------------
	/// <summary>
	/// As duas passagens existem porque as duas RECRIAM coisa, e recriar e onde tinta se perde:
	///
	///   * `Vestir` remonta as camadas, e o `MontarRabo` faz sprite NOVO com material NOVO -- e material
	///     recem-criado tem tinta zero. Era exatamente o cenario que a captura preguicosa transformava em
	///     "zero pra sempre";
	///   * o ciclo transformar -> reverter troca folha, tinta e modo nas duas direcoes.
	///
	/// A pergunta e a mesma nas duas: **o cabelo e o rabo voltam ao pixel que tinham antes?** Comparar com
	/// o proprio estado inicial vale mais que cravar um valor -- serve pra qualquer personagem que a
	/// bancada nasca sendo, e nao so pro Saiyajin de cabelo Goku desta rodada.
	///
	/// COMO REPROVA: faca o `World.AoReceberAparencia` chamar `CharacterVisual.Vestir` direto (era como
	/// ele era) -- cai a metade do `Vestir`. Tire o `BaseDaFicha` do `TingirCabelo` -- cai a do ciclo.
	/// </summary>
	private void ODepoisDoVestirEDoCiclo(CharacterVisual vis, Jandirus.Client.World mundo, int meuId,
										 Action<string, string> mudar, string Base)
	{
		if (mundo.LookDeTeste(meuId) is not { } ficha)
		{ Conferir(false, "o jogador tem ficha guardada pra a familia do `Vestir`"); return; }

		// A FICHA DE VERDADE, pelo caminho de jogo (e o que chega ao trocar de zona). Nao ha ficha
		// adulterada aqui de proposito: a irma de cima (`ORaboEOOlhoSobrevivemAFicha`) ja mede a sonda
		// vermelha, e o que ESTA quer e o estado que tem que ficar IGUAL.
		mudar(Base, Base);
		mundo.AoReceberAparencia(meuId, GameClient.Instance?.LocalName ?? "Eu",
								 ficha.Raca, ficha.Genero, ficha.Ap);
		AgendarFoto("cabelo.aposVestir", Camada(vis, "cabelo"));
		AgendarFoto("rabo.aposVestir", Camada(vis, "rabo"));

		// ============================ E A FICHA CHEGANDO **DENTRO** DA FORMA ============================
		// As duas fotos acima sao no estado de repouso, e la a base do rabo (`Tinta.Nenhuma`) por acaso e
		// IGUAL ao que um material recem-criado ja tem -- ou seja, um `Vestir` que largasse o rabo cru
		// passaria nelas por coincidencia. Esta terceira e a que nao passa por acaso: com o dourado do
		// `ssj1` no ar, remontar as camadas TEM que devolver o dourado, e material novo devolve cinza.
		//
		// E o caso de jogo e literal: voar transformado pra outro planeta faz a ficha chegar de novo.
		// ==========================================================================================
		mudar(Base, "ssj1");
		mundo.AoReceberAparencia(meuId, GameClient.Instance?.LocalName ?? "Eu",
								 ficha.Raca, ficha.Genero, ficha.Ap);
		AgendarFoto("rabo.vestirEmSsj1", Camada(vis, "rabo"));

		mudar("ssj1", Base);
		AgendarFoto("cabelo.aposCiclo", Camada(vis, "cabelo"));
		AgendarFoto("rabo.aposCiclo", Camada(vis, "rabo"));
	}

	// ---------------------------------------------------------------------
	// FAMILIA 5 -- O CONTROLE, SEM O QUAL TUDO-PRETO PASSA VERDE
	// ---------------------------------------------------------------------
	/// <summary>
	/// ============================ UMA MEDIDA CEGA APROVA QUALQUER COISA ============================
	/// Todas as familias acima sao da forma "**nao** aconteceu o defeito". Uma medida que devolvesse sempre
	/// o mesmo valor -- uma foto preta, um viewport que nunca desenhou, um contador que so ve o fundo --
	/// passaria em todas elas ao mesmo tempo, e a bancada ficaria verde medindo nada. Nao e hipotese: foi
	/// literalmente o que aconteceu na primeira versao deste bloco, com o `ForceDraw`.
	///
	/// Entao aqui a combinacao RUIM e armada DE PROPOSITO, numa copia de bancada (o boneco vivo nao e
	/// tocado), e cobra-se que a medida a ENXERGUE:
	///
	///   * SOMA com tinta zero  -> a tela sai IGUAL a arte (o neutro do shader);
	///   * MATIZ com tinta zero -> a tela vira UM TOM SO, preto. E a assinatura exata do borrao chapado,
	///     e e o que a familia 2 proibe -- aqui ela e produzida pra provar que seria vista;
	///   * MATIZ com uma tinta LEGITIMA do catalogo -> a mesma folha volta a ter relevo, e COM COR.
	///
	/// As tres juntas dizem: a medida distingue neutro de achatado, e achatado de colorido. Enquanto elas
	/// passarem, um "nao ha matiz com preto" verde significa alguma coisa.
	/// ==========================================================================================
	/// </summary>
	private void OControleDaMedida(CharacterVisual vis)
	{
		if (Camada(vis, "cabelo") is not { } cab || cab.SpriteFrames is not { } fr)
		{ Conferir(false, "ha uma folha de cabelo pra o controle da medida"); return; }

		// MATERIAL NOVO, e e a unica vez neste bloco em que isso e certo: aqui nao se mede o jogo, mede-se
		// a MEDIDA. Mexer nos uniformes do boneco vivo deixaria tinta suja nas fotos que a bancada tira
		// mais adiante.
		var cobaia = new AnimatedSprite2D
		{
			SpriteFrames = fr,
			Centered = false,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Material = new ShaderMaterial { Shader = GD.Load<Shader>(ShaderDoPersonagem) },
		};
		AddChild(cobaia);
		cobaia.Animation = cab.Animation;
		cobaia.Frame = cab.Frame;
		cobaia.Pause();
		var mat = (ShaderMaterial)cobaia.Material;

		void Armar(Vector3 cor, int modo)
		{
			mat.SetShaderParameter("tinta_modo", modo);
			mat.SetShaderParameter("tinta", cor);
		}

		Armar(Vector3.Zero, 0);
		AgendarFoto("controle.somaZero", cobaia);

		Armar(Vector3.Zero, 1);
		AgendarFoto("controle.matizPreto", cobaia);

		// UMA TINTA DE VERDADE DO CATALOGO, e nao um hexa inventado: o azul do Blue, que e a tinta que
		// criou o modo matiz. Se ela sumir do catalogo, esta linha reclama junto.
		string? azul = Jandirus.Core.Forms.Catalogo.CorDoCabelo(Jandirus.Core.Forms.Catalogo.Def("blue"));
		Conferir(azul != null, "o `blue` declara tinta de cabelo pra servir de controle colorido");
		if (azul is { } h)
		{
			var c = new Color(h);
			_hexaDoControle = h;
			Armar(new Vector3(c.R, c.G, c.B), 1);
			AgendarFoto("controle.matizAzul", cobaia);
		}

		cobaia.QueueFree();
	}

	/// <summary>Que hexa o controle colorido usou. So pra o log da <see cref="AsAfirmacoesDoPixel"/> dizer qual.</summary>
	private string _hexaDoControle = "";

	// ---------------------------------------------------------------------
	// AS AFIRMACOES DO PIXEL -- um quadro depois, com as fotos na mao
	// ---------------------------------------------------------------------
	/// <summary>
	/// Aqui moram as cobrancas das cinco familias que dependem da FOTO. Elas estao juntas e nao ao lado de
	/// cada familia por um motivo mecanico: entre agendar e revelar passa um quadro, e nesse meio tempo o
	/// boneco ja mudou de forma varias vezes -- ver o cabecalho do <see cref="OParEOPixelDaTinta"/>.
	///
	/// TODA LINHA COMECA POR "se a foto existe", e e por isso que o piso do <see cref="RevelarAFila"/>
	/// existe: sem ele, sumir com as fotos apagaria este bloco inteiro sem uma linha vermelha.
	/// </summary>
	private void AsAfirmacoesDoPixel()
	{
		bool Tem(string rotulo, out string[] arte, out string[] tela)
		{
			bool ok = _medidas.TryGetValue(rotulo, out (string[] Arte, string[] Tela) m) && m.Tela.Length > 0;
			(arte, tela) = ok ? (m.Arte, m.Tela) : ([], []);
			return ok;
		}

		// --- FAMILIA 1: o cabelo do personagem BASE ---
		if (Tem("cabelo.base", out string[] arteCab, out string[] telaCab))
		{
			// O PISO DA PROPRIA MEDIDA: um quadro de arte com um tom so nao tem relevo pra perder, e
			// cobrar "mais de um tom" dele seria cobrar o impossivel. Esta linha e quem diz que a de
			// baixo mede alguma coisa.
			Conferir(arteCab.Length >= 2,
					 $"a arte deste quadro de cabelo TEM relevo pra perder ({arteCab.Length} tons)");
			Conferir(telaCab.Length >= 2,
					 $"e o cabelo do personagem BASE mantem mais de um tom NA TELA ({telaCab.Length} tons) "
				   + "-- \"perdeu o relevo\" e \"virou um tom so\" sao a mesma frase");
			Conferir(telaCab.SequenceEqual(arteCab),
					 "-- e sem tinta a tela sai IDENTICA a arte, tom por tom (soma com zero e a identidade)");
		}

		// --- FAMILIA 3: o rabo cru, o nao-cru, e a volta ---
		if (Tem("rabo.base", out string[] arteRabo, out string[] raboCru))
			Conferir(raboCru.SequenceEqual(arteRabo),
					 "na BASE o rabo sai IDENTICO a arte no pixel (sem tinta, soma e identidade)");

		if (Tem("rabo.ssj1", out _, out string[] raboSsj))
		{
			Conferir(raboCru.Length == 0 || !raboSsj.SequenceEqual(raboCru),
					 $"em `ssj1` o rabo NAO sai na cor crua do sprite ([{string.Join(" ", raboSsj)}]) -- e o "
				   + "estado em que o corpo ALHEIO o perdia, cinza num personagem dourado");
			// DOURADO E NAO "diferente": "diferente" tambem seria verdade se ele saisse azul. A forma da
			// cor e a mesma que o `ORaboMedidoNoDesenho` cobra na conta -- duas leituras do mesmo enunciado.
			int dourados = raboSsj.Count(t => Dourado(new Color(t)));
			Conferir(dourados == raboSsj.Length,
					 $"-- e os {raboSsj.Length} tom(ns) dele saem DOURADOS na tela ({dourados})");
		}

		if (Tem("rabo.volta", out _, out string[] raboVolta) && raboCru.Length > 0)
			Conferir(raboVolta.SequenceEqual(raboCru), "e a volta devolve o rabo a arte crua, no pixel");

		// --- FAMILIA 4: depois de um `Vestir` e depois de um ciclo ---
		foreach ((string rotulo, string[] linhaDeBase, string oque) in new[]
		{
			("cabelo.aposVestir", telaCab, "depois de um `Vestir` o CABELO sai no mesmo pixel de antes"),
			("rabo.aposVestir", raboCru, "-- e o RABO tambem (o `MontarRabo` faz sprite e material NOVOS)"),
			// A UNICA DAS QUATRO QUE NAO PODE PASSAR POR ACASO: aqui a linha de base e o DOURADO, e
			// material recem-criado nao tem dourado nenhum -- ver o bloco no `ODepoisDoVestirEDoCiclo`.
			("rabo.vestirEmSsj1", raboSsj, "e uma ficha que chega DENTRO da forma nao despe o rabo: ele "
										 + "continua no dourado, e nao no cinza de um material novo"),
			("cabelo.aposCiclo", telaCab, "e depois de um ciclo transformar -> reverter o CABELO volta ao "
										+ "mesmo pixel"),
			("rabo.aposCiclo", raboCru, "-- e o RABO volta junto"),
		})
		{
			if (linhaDeBase.Length == 0 || !Tem(rotulo, out _, out string[] depois)) continue;
			Conferir(depois.SequenceEqual(linhaDeBase), $"{oque} ([{string.Join(" ", depois)}])");
		}

		// --- FAMILIA 5: o controle ---
		if (Tem("controle.somaZero", out string[] arteC, out string[] neutro))
			Conferir(neutro.SequenceEqual(arteC),
					 "CONTROLE: soma com tinta zero devolve a arte intacta -- e o neutro do shader");

		if (Tem("controle.matizPreto", out _, out string[] chapado))
			Conferir(chapado.Length == 1 && chapado[0] == "000000",
					 $"CONTROLE: matiz com tinta preta vira UM TOM SO, preto ([{string.Join(" ", chapado)}]) "
				   + "-- a medida ENXERGA o defeito que a familia 2 proibe");

		if (Tem("controle.matizAzul", out _, out string[] colorido))
		{
			Conferir(colorido.Length >= 2,
					 $"CONTROLE: e com uma tinta LEGITIMA (#{_hexaDoControle}) a mesma folha volta a ter "
				   + $"relevo ({colorido.Length} tons) -- nao e a folha que e chapada");
			Conferir(colorido.Any(t => Saturacao(new Color(t)) >= 0.2f),
					 "-- e ela sai COM COR na tela (tudo-preto nao passaria aqui)");
		}
	}

	/// <summary>DOURADO: R e G no alto e juntos, B bem abaixo. A mesma forma que o <see cref="ORaboMedidoNoDesenho"/> cobra.</summary>
	private static bool Dourado(Color c) => c.R > 0.7f && c.G > 0.7f && c.B < c.G - 0.25f;

	/// <summary>O par (cor + modo) escrito de um jeito que o log conta a historia: o MODO por extenso.</summary>
	private static string FmtPar((Vector3 Cor, int Modo)? p) =>
		p is { } x ? $"{x.Cor} / {(x.Modo == 1 ? "MATIZ" : "SOMA")}" : "sem material";

	// =====================================================================
	// 3a-ter-bis. O CABELO E O RABO DE **CADA** FORMA DO CATALOGO
	// =====================================================================
	/// <summary>
	/// O QUE CADA UMA DAS 36 ENTRADAS FAZ COM O CABELO E COM O RABO -- escrito a mao.
	///
	/// ============================ POR QUE A MAO, E NAO DERIVADO ============================
	/// No Core as duas respostas sao DERIVACOES (<see cref="Jandirus.Core.Forms.Catalogo.ModoDoCabelo"/>
	/// sai de `(SufixoDoCabelo, Cabelo)` e <see cref="Jandirus.Core.Forms.Catalogo.CorDoRabo"/> sai da
	/// tinta do cabelo). Recalcular a derivacao aqui seria conferir a funcao com ela mesma: uma
	/// `CorDoRabo` que devolvesse sempre nulo passaria numa copia que tambem devolvesse sempre nulo.
	///
	/// Esta tabela e a leitura do DM feita de novo, forma por forma -- as mesmas fontes que os
	/// comentarios do Core citam (`SaiyanObjects.dm:100-118` pro rabo, `HairObject.dm:73` e `:209`,
	/// `UltraEgo.dm:390`, `Mystic.dm:36` e `:81`, `UltraInstinct.dm:298`).
	///
	/// ============================ E POR ISSO ELA REPROVA FORMA NOVA ============================
	/// Uma entrada nova no catalogo cai na checagem de EXAUSTIVIDADE abaixo ate alguem escrever aqui
	/// o que ela faz com o cabelo. E de proposito, e e a diferenca entre esta tabela e as derivacoes
	/// por linha do Core: la um degrau novo tem que nascer certo sozinho, aqui ele tem que ser
	/// OLHADO. Um degrau que ninguem olhou saindo com o cabelo errado e o defeito mais barato de
	/// cometer neste sistema -- ja aconteceu tres vezes (Wrathful dourado, C-Type verde, SSG azul).
	/// ================================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR: troque o `null` do `wrathful` por `"dada26"` (o defeito
	/// original) -- cai a linha `wrathful: rabo`. Apague o corte por linha do `CorDoRabo` -- caem
	/// `beast`, `ultra_ego` e os tres do Prodigial de uma vez.
	/// </summary>
	private static readonly (string Id, ModoCabelo Modo, string? Rabo, string? Olho)[]
		OQueCadaFormaFazNoCabelo =
	[
		// --- a base nao e forma nenhuma: nao troca, nao pinta, nao mexe no rabo nem no olho ---
		("base",                            ModoCabelo.Base,             null,     null),

		// --- ESCADA SAIYAJIN: arte propria dourada, ZERO tinta (o veto do dono), rabo dourado ---
		// E OLHO VERDE ESMERALDA, a linha inteira -- eixo NOVO, e o dono ditou a tabela. Estes cinco
		// sao os degraus SEM corpo proprio; os tres do SSJ4 estao logo abaixo com outra cor.
		("ssj1",                            ModoCabelo.Trocar,           "dada26", "40a060"),
		("grade2",                          ModoCabelo.Trocar,           "dada26", "40a060"),
		("grade3",                          ModoCabelo.Trocar,           "dada26", "40a060"),
		("ssj2",                            ModoCabelo.Trocar,           "dada26", "40a060"),
		("ssj3",                            ModoCabelo.Trocar,           "dada26", "40a060"),
		// OS TRES DO SSJ4 NAO PINTAM RABO: a folha do CORPO deles ja desenha o rabo vermelho
		// (`SaiyanObjects.dm:134`, o `alpha = 0` do overlay). Ver `FormaDef.Corpo` no catalogo.
		//
		// E OS MESMOS TRES SAO O AMARELADO DO OLHO -- **pelo MESMO campo**. O `Corpo` responde
		// as duas perguntas, e essa e a unica razao de o `ssj4_limit_breaker` (aura VERMELHA) sair
		// amarelo aqui: se o olho saisse da `Aura`, esta linha estaria escrita `ff2d2f`.
		("ssj4",                            ModoCabelo.Trocar,           null,     "e5ca82"),
		("ssj4_full_power",                 ModoCabelo.Trocar,           null,     "e5ca82"),
		("ssj4_limit_breaker",              ModoCabelo.Trocar,           null,     "e5ca82"),
		("future_ssj",                      ModoCabelo.Trocar,           "dada26", "40a060"),

		// --- LEGENDARY: o Wrathful e a excecao da linha (cabelo BASE, rabo sem tinta) ---
		// E ELE E EXCECAO DUAS VEZES: a linha toda fica de olho VERDE (o `40a060` da escada Saiyajin) e
		// so ele fica AMARELO. E o unico id que aparece por nome numa derivacao do Core -- ver
		// `Catalogo.IdWrathful`.
		//
		// ============================ ESTA COLUNA E "COM AS REDEAS NA MAO" ============================
		// Ela dizia `fcfdfd` (o branco sem iris) nos nove degraus, e isso deixou de ser verdade: o branco
		// virou a cor de um corpo que a FURIA LENDARIA esta dirigindo, e nao a cor da linha
		// (`Catalogo.CorDoOlho(d, semRedeas)`; pedido do dono, e o `lssjbuff.dm:609` ja dizia isso). Esta
		// tabela mede o estado normal -- quem confere o branco da posse e o `OOlhoDaLinhaLendaria`.
		// ==========================================================================================
		("wrathful",                        ModoCabelo.Base,             null,     "e8bc18"),
		("c_type",                          ModoCabelo.Trocar,           "dada26", "40a060"),
		("legendary",                       ModoCabelo.TrocarETingir,    "7ba81f", "40a060"),

		("primal_c_type",                   ModoCabelo.Trocar,           "dada26", "40a060"),
		("primal_legendary",                ModoCabelo.TrocarETingir,    "7ba81f", "40a060"),
		("primal_legendary2",               ModoCabelo.TrocarETingir,    "7ba81f", "40a060"),
		("primal_legendary3",               ModoCabelo.TrocarETingir,    "7ba81f", "40a060"),
		("primal_legendary4",               ModoCabelo.Trocar,           null,     "40a060"),
		("primal_legendary4_full_power",    ModoCabelo.Trocar,           null,     "40a060"),
		("primal_legendary4_limit_breaker", ModoCabelo.Trocar,           null,     "40a060"),

		// --- KI DIVINO: os dois SSG so PINTAM (o penteado fica), e o vermelho vai pro rabo tambem ---
		// E PRO OLHO, pela mesma derivacao: o `e2331c` do SSG responde por cabelo, rabo E olho, e
		// esta escrito UMA vez no catalogo. Ver o cabecalho do `Catalogo.CorDoOlho`.
		("ssg",                             ModoCabelo.Tingir,           "e2331c", "e2331c"),
		// OS QUATRO DIVINOS QUE TROCAM O SPRITE SAIRAM DOS HEXAS DO DM, e a razao esta em
		// `Catalogo.AzulDoCabeloDivino`: eles caem na arte DOURADA de Super Saiyajin, onde a soma dava
		// BRANCO. O `blue_evolution` ainda ganhou um azul PROPRIO -- pedido do dono, e o DM nao
		// distingue os dois.
		//
		// E O OLHO DELES **NAO** E A TINTA DO CABELO, ao contrario do SSG: as duas sao cor de MATIZ,
		// onde o hexa e o PISO de uma rampa que o dourado multiplica por ate 1,81 -- somado num olho
		// preto o `082b8d` do Royale daria azul-marinho quase invisivel. Estas quatro linhas sao o que reprova quem "simplificar" o `CorDoOlho`
		// divino pra um `CorDoCabelo(d)` seco.
		("blue",                            ModoCabelo.TrocarETingir,    "3392c7", "1f6ae8"),
		("blue_evolution",                  ModoCabelo.TrocarETingir,    "082b8d", "1f6ae8"),
		("rose_ssg",                        ModoCabelo.Tingir,           "e2331c", "e2331c"),
		("rose",                            ModoCabelo.TrocarETingir,    "d15694", "e0409a"),
		("rose2",                           ModoCabelo.TrocarETingir,    "d15694", "e0409a"),

		// --- MISTICO: cabelo natural no Mistico; o Beast recolore em MATIZ ---
		// ============================ OS DOIS DEGRAUS DIVERGIRAM NESTA PASSADA ============================
		// A linha inteira era `null, null` (*"Mistico / Prodigial: igual a base, nada muda"*). O dono
		// separou os dois com dois pedidos: *"o rabo do beast n ta branco"* e *"o olho do beast era pra
		// ser vermelho"*. O Mistico continua intocado -- ele NAO foi citado, e o enunciado dele
		// continua sendo a ausencia.
		//
		// O RABO DA FERA NAO ESTA ESCRITO DUAS VEZES: `b6bac4` e a mesma tinta que o cabelo dela ja
		// recebe (coluna do `ModoCabelo.TrocarERecolorir`), e a derivacao "quem pinta o cabelo pinta o
		// rabo" e que a poe aqui -- por isso o mesmo hexa aparece nas duas colunas. O OLHO nao: ele
		// e cor propria, e o unico canal da Fera que nao deriva de nada.
		("mistico",                         ModoCabelo.Base,             null,     null),
		("beast",                           ModoCabelo.TrocarERecolorir, "b6bac4", "e5282a"),

		// --- ULTRA INSTINCT: arte de UMA pessoa, entao a tinta e ALTERNATIVA ---
		// O OLHO E DA LINHA (o `Buff()` do DM nao ramifica por estagio) e o RABO E DO PERFECTED.
		// As duas colunas discordam DE PROPOSITO nestas duas linhas, e e a unica vez no arquivo:
		// a prata do olho vem de `UltraInstinct.dm:481`, o rabo branco vem do dono e sai da tinta de
		// cabelo -- que o Sign nao tem.
		("ui_sign",                         ModoCabelo.Trocar,           null,     "bec4d0"),
		("ui_perfected",                    ModoCabelo.TrocarOuTingir,   "b9becb", "bec4d0"),

		// A DESTROYER NAO MUDA NEM RABO NEM OLHO, e o DM diz que essa e a maior diferenca visual
		// entre as duas (`UltraEgo.dm:395-396`). O roxo `8c32be` do Ultra Ego responde pelos tres
		// canais -- cabelo, rabo e olho -- e esta escrito uma vez so.
		("destroyer",                       ModoCabelo.Base,             null,     null),
		("ultra_ego",                       ModoCabelo.Tingir,           "8c32be", "8c32be"),

		// --- OOZARU: o macaco nem tem cabelo desenhado, e o pelo do rabo e o do corpo ---
		("oozaru",                          ModoCabelo.Base,             null,     null),
		("oozaru_dourado",                  ModoCabelo.Trocar,           null,     null),

		// ============================ AS QUATRO LINHAS RACIAIS -- DOZE LINHAS DE **AUSENCIA** ============================
		// Elas sao a metade menos glamourosa desta tabela e a que mais trabalha: `Base, null, null`
		// doze vezes seguidas. A pergunta que a linha responde nao e "que cor?", e sim **"alguem olhou
		// pra esta forma?"** -- e o proprio nome da checagem diz isso ("forma nova sem ninguem olhar").
		//
		// **AS SETE DO FROST DEMON ESTAVAM FALTANDO**, e a bancada as acusava desde que aquela linha
		// entrou no catalogo. Entram agora junto com as cinco novas porque a resposta e a mesma e o
		// motivo tambem: **nenhuma destas quatro racas tem penteado de Super Saiyajin, rabo, ou
		// qualquer mexida no olho no original**.
		//
		//   * o Frost Demon troca o CORPO INTEIRO (`CorpoDeForma.FrostEscolhido`) -- e corpo nao passa
		//     por nenhuma das tres colunas desta tabela;
		//   * o Heran veste `/obj/overlay/hairs/superheran/sh1`, e o `EffectStart()` dele faz
		//     `icon = container.truehair` -- o PROPRIO cabelo do jogador. Trocar o penteado por ele
		//     mesmo e nao trocar penteado nenhum, e e por isso que a coluna diz `Base` e nao `Trocar`;
		//   * o Namekuseijin e o Alien nao encostam em cabelo nem em olho: o que eles acendem e
		//     overlay de ELETRICIDADE (`snamek Elec.dmi`, `.../spc`), que e o campo `Raios` e nao este.
		//
		// Nenhuma das tres racas tem rabo em lugar nenhum do jogo -- o `CorDoRabo` ja devolve `null`
		// pra elas por derivacao (a guarda e "escada que escreve `ssj`/`lssj`"), e estas linhas sao o
		// que cobra que essa derivacao continue valendo.
		// ==========================================================================================================
		("frost1",                          ModoCabelo.Base,             null,     null),
		("frost2",                          ModoCabelo.Base,             null,     null),
		("frost3",                          ModoCabelo.Base,             null,     null),
		("frost4",                          ModoCabelo.Base,             null,     null),
		("frost5",                          ModoCabelo.Base,             null,     null),
		("frost6",                          ModoCabelo.Base,             null,     null),
		("frost7",                          ModoCabelo.Base,             null,     null),

		("snamek",                          ModoCabelo.Base,             null,     null),
		("heran1",                          ModoCabelo.Base,             null,     null),
		("heran2",                          ModoCabelo.Base,             null,     null),
		("alien1",                          ModoCabelo.Base,             null,     null),
		("alien2",                          ModoCabelo.Base,             null,     null),
	];

	/// <summary>
	/// A TABELA ACIMA COBRADA NO DADO **E** NO BONECO, no catalogo inteiro.
	///
	/// ============================ SAO DUAS PERGUNTAS DIFERENTES, E AS DUAS JA FALHARAM ============================
	///   1. **O CATALOGO DIZ A COISA CERTA?** (`ModoDoCabelo` / `CorDoRabo`) -- foi aqui que o
	///      Wrathful saiu dourado e o SSG saiu azul: dado errado, codigo perfeito.
	///   2. **O BONECO FAZ O QUE O CATALOGO DIZ?** -- foi aqui que a tinta do cabelo passou meses
	///      sem existir: o catalogo ja estava certo e o `PintarCabelo` tinha o corpo vazio. Conferir
	///      so o dado teria aprovado o jogo mudo.
	/// ==========================================================================================================
	///
	/// ============================ E POR QUE DOIS BONECOS ============================
	/// Porque o Ultra Instinct tem arte de UM personagem (`ui_apply_hair()` so troca
	/// `if(hairtypeSaved == "Goku")`, `UltraInstinct.dm:296`), e as duas metades da regra dele so
	/// existem em corpos diferentes: **com** cabelo de Goku ele ganha o sprite e NAO leva prata;
	/// **com qualquer outro** ele fica com o penteado do jogador e leva a prata `b9becb`. Um boneco
	/// so responde metade da pergunta, e a metade que faltasse seria a que ninguem olha.
	///
	/// Os bonecos sao montados AQUI (`CharacterVisual.Vestir`, o mesmo metodo que o `S2C.PeerLook`
	/// chama) em vez de a bancada trocar o penteado do corpo local: o corpo local e medido por outros
	/// dez blocos deste arquivo, e re-vesti-lo no meio embaralharia todos eles.
	///
	/// COMO REPROVA SE A REGRA SUMIR:
	///   * apague a linha `PintarRabo(...)` do `VestirCabeloDaForma` -- caem as 15 formas que pintam rabo;
	///   * troque o `!trocou` do `TrocarOuTingir` por `true` -- cai `ui_perfected` no boneco do Goku
	///     (prata somada por cima da arte prateada, que estoura pro branco);
	///   * troque o `troca ? d!.SufixoDoCabelo : ""` por `d!.SufixoDoCabelo` -- caem as tres formas
	///     `Tingir` e as seis `Base`, porque elas passariam a trocar o penteado.
	/// </summary>
	private void OCabeloDeCadaFormaNoCatalogo()
	{
		// ============================ 1. A TABELA E O CATALOGO SE COBREM ============================
		// Nos DOIS sentidos: uma forma nova sem linha aqui, e uma linha aqui apontando pra uma forma
		// que nao existe mais (o resto do bloco ignora em silencio uma linha orfa -- ela nunca
		// reprovaria nada e daria a impressao de cobertura).
		// ========================================================================================
		string[] noCatalogo = [.. Jandirus.Core.Forms.Catalogo.Todas.Select(d => d.Id)];
		foreach ((string id, _, _, _) in OQueCadaFormaFazNoCabelo)
			Conferir(noCatalogo.Contains(id), $"a tabela do cabelo fala de uma forma que existe (`{id}`)");
		foreach (string id in noCatalogo)
			Conferir(OQueCadaFormaFazNoCabelo.Any(x => x.Id == id),
					 $"`{id}` tem cabelo, rabo e OLHO DECIDIDOS na tabela (forma nova sem ninguem olhar)");

		// --- 2. O DADO: o modo, a cor do rabo e a cor do olho de cada entrada ---
		var porModo = new Dictionary<ModoCabelo, int>();
		int pintamRabo = 0, pintamOlho = 0;
		var coresDeOlho = new HashSet<string>(StringComparer.Ordinal);
		foreach ((string id, ModoCabelo modo, string? rabo, string? olho) in OQueCadaFormaFazNoCabelo)
		{
			if (Jandirus.Core.Forms.Catalogo.Def(id) is not { } d) continue;

			ModoCabelo deu = Jandirus.Core.Forms.Catalogo.ModoDoCabelo(d);
			Conferir(deu == modo, $"`{id}`: veste o cabelo em {modo} (deu {deu})");

			string? raboDeu = Jandirus.Core.Forms.Catalogo.CorDoRabo(d);
			Conferir(raboDeu == rabo,
					 $"`{id}`: rabo {(rabo == null ? "SEM tinta" : "#" + rabo)} "
				   + $"(deu {(raboDeu == null ? "SEM tinta" : "#" + raboDeu)})");

			// O OLHO, COM O HEXA LITERAL, pelo mesmo motivo do rabo: uma checagem escrita como
			// `== VerdeDoOlhoSuperSaiyajin` passaria com qualquer valor, inclusive um trocado por
			// engano. Aqui a cor tem que ser DIGITADA de novo pra mudar.
			string? olhoDeu = Jandirus.Core.Forms.Catalogo.CorDoOlho(d);
			Conferir(olhoDeu == olho,
					 $"`{id}`: olho {(olho == null ? "igual a ficha" : "#" + olho)} "
				   + $"(deu {(olhoDeu == null ? "igual a ficha" : "#" + olhoDeu)})");

			porModo[modo] = porModo.GetValueOrDefault(modo) + 1;
			if (rabo != null) pintamRabo++;
			if (olho != null) { pintamOlho++; coresDeOlho.Add(olho); }
		}

		// ============================ OS SEIS MODOS EXISTEM DE VERDADE ============================
		// Sem esta linha, um `ModoDoCabelo` que devolvesse `Base` pra tudo passaria em 36 checagens
		// acima se a tabela tambem dissesse `Base` em tudo -- e as duas contas errariam juntas, que e
		// exatamente o que a tabela escrita a mao existe pra impedir. Mesma rede do `porFolha.Count`
		// da aura, e pelo mesmo motivo.
		// ======================================================================================
		Conferir(porModo.Count == Enum.GetValues<ModoCabelo>().Length,
				 $"o catalogo usa os {Enum.GetValues<ModoCabelo>().Length} modos de cabelo ("
			   + string.Join(", ", porModo.OrderBy(p => p.Key).Select(p => $"{p.Key} {p.Value}")) + ")");
		Conferir(pintamRabo > 0 && pintamRabo < OQueCadaFormaFazNoCabelo.Length,
				 $"e o rabo se pinta em ALGUMAS e nao em todas ({pintamRabo} de "
			   + $"{OQueCadaFormaFazNoCabelo.Length})");

		// ============================ E O OLHO TEM QUE SER UM LEQUE, NAO UMA COR ============================
		// A mesma rede das duas linhas acima, e ela morde mais aqui: o `CorDoOlho` era um `?:` de UMA
		// cor (a prata do Ultra Instinct) e virou nove. Um `switch` que colapsasse pro `_ => null`, ou
		// que devolvesse o mesmo hexa em tudo, passaria nas 34 linhas de cima **se a tabela escrita a
		// mao dissesse a mesma coisa** -- e o jeito de a tabela dizer a mesma coisa e alguem "arrumar"
		// as linhas vermelhas uma a uma sem olhar o boneco. Estas duas linhas nao arrumam: elas contam.
		//
		// SAO NOVE CORES por construcao (verde da escada, amarelado do SSJ4, amarelo do Wrathful,
		// vermelho do SSG, azul, rosa, prata do UI, roxo do UE, vermelho da Fera) e o numero esta
		// ESCRITO: uma cor nova entra aqui de proposito, junto com a linha da tabela.
		//
		// O BRANCO SEM IRIS NAO ESTA NA CONTA -- nao porque alguem o apagou, mas porque ele deixou de
		// ser cor de FORMA: ele e a cor de um corpo que a furia lendaria esta dirigindo, e esta tabela
		// mede a forma com as redeas na mao. Quem o conta hoje e o `OOlhoDaLinhaLendaria`.
		//
		// O NONO E O `e5282a` DA FERA (*"o olho do beast era pra ser vermelho"*), e o numero subir
		// AQUI e o ponto do bloco: se alguem "simplificar" reusando o `ff2d2f` do Limit Breaker -- que
		// esta a um passo -- a contagem fica em nove do mesmo jeito, e por isso ha uma linha nomeada
		// contra isso no `OOlhoDaLinhaLendaria`, no bloco 3. Aqui se conta; la se diz qual.
		// ============================================================================================
		Conferir(pintamOlho > 0 && pintamOlho < OQueCadaFormaFazNoCabelo.Length,
				 $"e o olho se pinta em ALGUMAS e nao em todas ({pintamOlho} de "
			   + $"{OQueCadaFormaFazNoCabelo.Length})");
		Conferir(coresDeOlho.Count == 9,
				 $"e sao NOVE cores de olho diferentes no catalogo ({coresDeOlho.Count}: "
			   + string.Join(", ", coresDeOlho.Order().Select(c => "#" + c)) + ")");

		// ============================ "SEM IRIS" E A COR DA ESCLEROTICA DO CORPO ============================
		// Esta linha e a UNICA que liga o hexa do catalogo ao PIXEL do desenho, e sem ela a decisao
		// inteira fica sem prova: a camada de olhos e so a iris (2 a 4 pixels por quadro), e apaga-la e
		// pinta-la do branco que esta encostado nela. Um `ffffff` redondo no lugar do `fcfdfd` medido
		// desenharia de volta, em tom palido, exatamente a iris que se queria sumir.
		//
		// COMO REPROVA SE A REGRA SUMIR: troque o `BrancoSemIris` do Core por `"ffffff"` -- esta cai
		// junto com as do `OOlhoDaLinhaLendaria`, e esta e a que diz POR QUE.
		//
		// LIDO COM `semRedeas: true` porque o branco e da POSSE e nao da linha (ver o cabecalho da
		// tabela acima): com as redeas na mao o Legendary devolve o verde da escada.
		// ==============================================================================================
		if (Jandirus.Core.Forms.Catalogo.Def("legendary") is { } dLenda)
			Conferir(Jandirus.Core.Forms.Catalogo.CorDoOlho(dLenda, semRedeas: true) == EscleroticaDoCorpo,
					 $"o branco 'sem iris' e o #{EscleroticaDoCorpo} MEDIDO na esclerotica do corpo "
				   + $"(deu #{Jandirus.Core.Forms.Catalogo.CorDoOlho(dLenda, semRedeas: true) ?? "nada"})");

		// --- 3. AO VIVO, em dois bonecos com penteados diferentes ---
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados))
		{
			Conferir(false, "a bancada acha o catalogo visual pra montar os bonecos de prova");
			return;
		}
		var cat = Jandirus.Core.Appearance.VisualCatalog.Parse(
			Godot.FileAccess.GetFileAsString(dados));

		// O GOKU E O VEGETA NAO SAO ESCOLHA DE GOSTO: o primeiro e o unico penteado com arte de Ultra
		// Instinct e o segundo e o unico com arte propria de SSJ4 -- as duas excecoes do resolvedor
		// (ver `CabelosDeForma.Universal`) caem em bonecos diferentes de proposito.
		VarrerOCabeloNumBoneco(cat, "Goku");
		VarrerOCabeloNumBoneco(cat, "Vegeta");
	}

	/// <summary>
	/// UM BONECO, O CATALOGO INTEIRO, E AS QUATRO SAIDAS DO <see cref="CharacterVisual.VestirCabeloDaForma"/>.
	///
	/// A esperada do SPRITE sai do <see cref="CabelosDeForma"/> -- o mesmo resolvedor que o jogo usa --
	/// e nao de uma lista de arquivos: a pergunta aqui nao e "qual arte existe" (essa e a do bloco do
	/// SSJ4 la em cima, e ela e feita contra a PASTA), e sim **se o vestidor pergunta ao resolvedor,
	/// e so quando o modo manda trocar**. As formas que NAO trocam sao medidas contra o penteado
	/// natural, sem resolvedor nenhum no meio -- e sao elas as seis do Wrathful e as tres do SSG.
	/// </summary>
	private void VarrerOCabeloNumBoneco(Jandirus.Core.Appearance.VisualCatalog cat, string penteado)
	{
		var boneco = new CharacterVisual { Name = $"BonecoDe{penteado}" };
		AddChild(boneco);
		try
		{
			// MASCULINO DE PROPOSITO, e nao por descuido: o resolvedor tem um ramo por sexo (o
			// `Hair_SSJ4Female`), e a esperada abaixo chama `CabelosDeForma.De(..., feminino: false)`.
			// Vestir uma boneca aqui faria a esperada e o boneco discordarem por uma diferenca que
			// nao e defeito -- e o ramo feminino ja e cobrado no bloco do SSJ4, pelo resolvedor.
			boneco.Vestir(cat, new Jandirus.Core.Appearance.Appearance { Cabelo = penteado },
						  "Saiyan", "Male");
			boneco.MostrarRabo(true);

			string normal = boneco.CabeloDeTeste;
			Conferir(boneco.TemCabeloDeTeste && normal.Length > 0,
					 $"o boneco de `{penteado}` tem penteado ({normal.GetFile()})");
			Conferir(boneco.TintaDoRaboDeTeste != null,
					 $"o boneco de `{penteado}` tem RABO (senao as 15 linhas do rabo nao medem nada)");
			// MESMA GUARDA PRO OLHO: sem camada de olhos a prata do Ultra Instinct seria conferida
			// contra nada, e as duas linhas dela passariam verdes pra sempre.
			Conferir(boneco.TintaDoOlhoDeTeste != null,
					 $"o boneco de `{penteado}` tem OLHOS (senao a prata do Ultra Instinct nao mede nada)");
			if (!boneco.TemCabeloDeTeste || boneco.TintaDoRaboDeTeste == null
				|| boneco.TintaDoOlhoDeTeste == null) return;

			// O NATURAL, LIDO DEPOIS DA PRIMEIRA CHAMADA. `TingirCabelo`/`PintarRabo` guardam a cor
			// original na PRIMEIRA vez que sao chamados -- ler antes disso compararia um boneco "ainda
			// nao guardado" com um ja guardado, e a diferenca seria da bancada. E o mesmo cuidado que
			// o `AAparenciaInteiraDoDegrau` toma com a foto zero.
			boneco.VestirCabeloDaForma(null);
			Vector3 tintaNatural = boneco.TintaDoCabeloDeTeste!.Value.Tinta;
			Vector3 raboNatural = boneco.TintaDoRaboDeTeste!.Value;
			Vector3 olhoNatural = boneco.TintaDoOlhoDeTeste!.Value;

			static bool Igual(Vector3 v, string hexa)
			{
				var c = new Color(hexa);
				return v.IsEqualApprox(new Vector3(c.R, c.G, c.B));
			}

			int trocaram = 0, tingiram = 0, pintaramOlho = 0;
			foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
			{
				ModoCabelo modo = Jandirus.Core.Forms.Catalogo.ModoDoCabelo(d);
				bool troca = modo is ModoCabelo.Trocar or ModoCabelo.TrocarETingir
								  or ModoCabelo.TrocarOuTingir or ModoCabelo.TrocarERecolorir;

				// A ARTE QUE ESTE PENTEADO TEM PRA ESTA FORMA -- nula quando nao ha nenhuma, e ai o
				// penteado normal FICA (`CabeloDaForma` nao mexe). E o que faz a prata do Ultra
				// Instinct entrar em quem nao e Goku.
				string? arte = troca ? CabelosDeForma.De(normal, d.SufixoDoCabelo) : null;
				string spriteEsperado = arte ?? normal;

				// A TINTA: o `TrocarOuTingir` decide pelo RESULTADO (ganhou arte propria? entao nao
				// leva tinta), e e a unica que olha o `arte` acima. As outras cinco olham so o modo.
				string? tintaEsperada = modo switch
				{
					ModoCabelo.Tingir or ModoCabelo.TrocarETingir
						or ModoCabelo.TrocarERecolorir => d.Cabelo,
					ModoCabelo.TrocarOuTingir => arte != null ? null : d.Cabelo,
					_ => null,
				};
				// ============================ MATIZ QUANDO A ARTE VEIO DA FORMA ============================
				// Era `modo == TrocarERecolorir` -- so o Beast. A regra do jogo deixou de ser essa: ela
				// pergunta se a tinta esta caindo num sprite que a FORMA trouxe, porque esse sprite e
				// sempre a arte DOURADA de Super Saiyajin e somar cor em dourado da branco. Ver o bloco
				// no `CharacterVisual.VestirCabeloDaForma`.
				//
				// E A BANCADA PERGUNTA PELO `arte`, e nao pelo modo: e o mesmo dado que o jogo usa
				// (`CabeloDaForma` devolvendo verdadeiro), e o unico que sabe que ESTE penteado tem
				// variante. Chavear pelo modo aprovaria em silencio o caso que mais importa -- um
				// penteado SEM variante de SSj, onde a tinta cai no molde preto e a soma volta a ser a
				// operacao certa.
				// ==========================================================================================
				int modoDaTinta = arte != null ? 1 : 0;
				string? raboEsperado = Jandirus.Core.Forms.Catalogo.CorDoRabo(d);
				// O OLHO: nove cores, e a esperada sai do MESMO derivador que o jogo usa -- nao de uma
				// lista de ids escrita aqui. Quem confere o VALOR de cada uma e a tabela escrita a mao
				// do `OQueCadaFormaFazNoCabelo`; o que se pergunta AQUI e outra coisa: se o valor que o
				// catalogo diz chega ao MATERIAL do boneco. Foi exatamente essa distancia que deixou a
				// tinta do cabelo meses sem existir com o catalogo certo.
				string? olhoEsperado = Jandirus.Core.Forms.Catalogo.CorDoOlho(d);

				boneco.VestirCabeloDaForma(d);

				Conferir(boneco.CabeloDeTeste == spriteEsperado,
						 $"[{penteado}] `{d.Id}`: {(arte != null ? "arte propria" : "penteado do jogador")} "
					   + $"({boneco.CabeloDeTeste.GetFile()})");
				if (boneco.CabeloDeTeste != normal) trocaram++;

				(Vector3 tinta, int md) = boneco.TintaDoCabeloDeTeste!.Value;
				bool tintaOk = tintaEsperada == null
					? tinta.IsEqualApprox(tintaNatural)
					: Igual(tinta, tintaEsperada) && md == modoDaTinta;
				Conferir(tintaOk,
						 $"[{penteado}] `{d.Id}`: cabelo "
					   + (tintaEsperada == null ? "SEM tinta" : $"tingido de #{tintaEsperada} (modo {modoDaTinta})")
					   + $" -- deu {tinta} modo {md}");
				if (tintaEsperada != null) tingiram++;

				Vector3 rabo = boneco.TintaDoRaboDeTeste!.Value;
				Conferir(raboEsperado == null ? rabo.IsEqualApprox(raboNatural) : Igual(rabo, raboEsperado),
						 $"[{penteado}] `{d.Id}`: rabo "
					   + (raboEsperado == null ? "na cor da ficha" : $"#{raboEsperado}") + $" -- deu {rabo}");

				Vector3 olho = boneco.TintaDoOlhoDeTeste!.Value;
				Conferir(olhoEsperado == null ? olho.IsEqualApprox(olhoNatural) : Igual(olho, olhoEsperado),
						 $"[{penteado}] `{d.Id}`: olho "
					   + (olhoEsperado == null ? "na cor da ficha" : $"#{olhoEsperado}") + $" -- deu {olho}");
				if (olhoEsperado != null) pintaramOlho++;
			}

			// ============================ E A COR DE OLHO ACONTECE ============================
			// O mesmo motivo das duas linhas acima: se o `CorDoOlho` passasse a devolver nulo pra todo
			// mundo, as 34 linhas de olho ficariam verdes comparando ficha com ficha.
			//
			// O NUMERO ERA **2** ate a tabela do dono (as duas formas do Ultra Instinct, as unicas que o
			// DM pinta) e subiu com ela. Os NULOS que sobram sao a `base`, o `mistico` -- *"igual a
			// base, nada muda"* -- e os Oozaru, que nem olho desenhado tem: cada um e uma decisao, e
			// nao um esquecimento.
			//
			// O `beast` SAIU dessa lista nesta passada (*"o olho do beast era pra ser vermelho"*), e a
			// conta e derivada do catalogo justamente pra isso -- um numero cravado aqui teria virado
			// falha num conserto que nao tem nada de errado.
			// ==================================================================================
			int olhosEsperados = Jandirus.Core.Forms.Catalogo.Todas.Count(
				f => Jandirus.Core.Forms.Catalogo.CorDoOlho(f) != null);
			Conferir(pintaramOlho == olhosEsperados && pintaramOlho >= 25,
					 $"[{penteado}] e a cor de olho chega no material em {olhosEsperados} formas "
				   + $"(chegou em {pintaramOlho})");

			// ============================ O ZERO NAO PODE SER VAZIO ============================
			// Um `CabeloDaForma` que nunca trocasse nada daria 36 linhas verdes de sprite acima (todas
			// comparadas contra `arte ?? normal`, e `arte` seria sempre nulo). Estas duas linhas sao o
			// que separa "a regra e obedecida" de "nao ha regra nenhuma sendo exercitada".
			// ==============================================================================
			Conferir(trocaram >= 10,
					 $"[{penteado}] e a troca de penteado ACONTECE de verdade ({trocaram} formas trocam)");
			Conferir(tingiram >= 8,
					 $"[{penteado}] e a tinta ACONTECE de verdade ({tingiram} formas tingem)");

			// ============================ O ULTRA INSTINCT, DITO COM TODAS AS LETRAS ============================
			// As duas metades ja caem na varredura acima, cada uma no seu boneco -- mas elas caem como
			// "`ui_perfected`: penteado do jogador", que nao conta a HISTORIA. Estas duas linhas
			// nomeiam a regra pra quem for ler o log atras dela.
			// ============================================================================================
			if (Jandirus.Core.Forms.Catalogo.Def("ui_perfected") is { } uip)
			{
				boneco.VestirCabeloDaForma(uip);
				bool ehGoku = normal.Contains("Goku", StringComparison.OrdinalIgnoreCase);
				bool ganhouArte = boneco.CabeloDeTeste.Contains("UltraInstinct");
				Conferir(ganhouArte == ehGoku,
						 $"[{penteado}] o Ultra Instinct {(ehGoku ? "GANHA" : "nao ganha")} a arte propria "
					   + $"({boneco.CabeloDeTeste.GetFile()})");
				Vector3 t = boneco.TintaDoCabeloDeTeste!.Value.Tinta;
				Conferir(ehGoku ? t.IsEqualApprox(tintaNatural) : Igual(t, uip.Cabelo),
						 $"[{penteado}] ...e por isso {(ehGoku ? "NAO leva prata por cima" : $"leva a prata #{uip.Cabelo}")}"
					   + $" ({t})");
			}

			// E A VOLTA DESFAZ AS QUATRO, no boneco inteiro. (O olho entrou aqui junto com a prata: e
			// exatamente o `ui_restore_eyes()` do DM, `UltraInstinct.dm:310-312`.)
			boneco.VestirCabeloDaForma(null);
			Conferir(boneco.CabeloDeTeste == normal
					 && boneco.TintaDoCabeloDeTeste!.Value.Tinta.IsEqualApprox(tintaNatural)
					 && boneco.TintaDoRaboDeTeste!.Value.IsEqualApprox(raboNatural)
					 && boneco.TintaDoOlhoDeTeste!.Value.IsEqualApprox(olhoNatural),
					 $"[{penteado}] sair da forma devolve penteado, tinta, rabo e olho da ficha");
		}
		finally
		{
			// `Free` E NAO `QueueFree`, pelo mesmo motivo do `AAparenciaInteiraDoDegrau`: a bancada
			// inteira roda dentro de um quadro so, e um `QueueFree` deixaria os dois bonecos pendurados
			// na arvore ate o fim dela.
			if (IsInstanceValid(boneco)) boneco.Free();
		}
	}

	// =====================================================================
	// 3a-quinquies. AS QUATRO DIVINAS, PELO QUE AS VARREDURAS NAO ALCANCAM
	// =====================================================================
	/// <summary>
	/// ULTRA INSTINCT -Sign-, PERFECTED, DESTROYER E ULTRA EGO -- cabelo, rabo, folha de aura e os
	/// efeitos de cena, nas perguntas que as varreduras genericas <b>nao</b> sabem fazer.
	///
	/// ============================ POR QUE ESTE BLOCO E CURTO ============================
	/// Quase tudo destas quatro JA cai em bancada: o modo do cabelo e a cor do rabo saem na tabela do
	/// <see cref="OQueCadaFormaFazNoCabelo"/> e no boneco (<see cref="VarrerOCabeloNumBoneco"/>), a
	/// prata do olho tambem, a folha sai no `folha de X = Base` da varredura de aura, e a contagem de
	/// anel/cascalho/clarao/descarga sai no bloco vivo do <see cref="NoCorpo"/>. Repetir isso aqui
	/// daria log mais comprido e cobertura igual.
	///
	/// O que se escreve aqui e o RESTO -- e o resto tem uma forma so: as varreduras acima comparam o
	/// jogo com o CATALOGO, e nenhuma delas compara o catalogo com o DM. Um exemplo por eixo:
	///
	///   * a contagem de efeitos do `NoCorpo` pergunta "o tocador disparou tantos aneis quantos o
	///     roteiro pede". Ela e verdadeira por construcao pra qualquer roteiro -- acrescente
	///     `DescargaNoCeu` ao climax do `ui_sign` e ela continua verde, com o ceu partindo numa cena
	///     que o DM fecha em *silencio*;
	///   * a varredura de cabelo prova a regra do Ultra Instinct em DOIS penteados (Goku e Vegeta) de
	///     sessenta e dois. Ela nao ve os outros sessenta;
	///   * e `Contains("UltraInstinct")` -- como a varredura pergunta -- e verdade tambem pra
	///     `Hair_MasteredUltraInstinct`. Os dois estagios sao INDISTINGUIVEIS pra ela.
	/// ================================================================================
	/// </summary>
	private void AsQuatroDivinas()
	{
		// AS QUATRO, E ELAS TEM QUE EXISTIR. Sem esta guarda o bloco inteiro vira `continue` silencioso
		// no dia em que um id mudar -- trinta e tantas checagens sumindo sem uma linha vermelha.
		FormaDef? Def(string id)
		{
			FormaDef? d = Jandirus.Core.Forms.Catalogo.Def(id);
			Conferir(d != null, $"a forma `{id}` existe no catalogo (sem ela este bloco nao mede nada)");
			return d;
		}

		FormaDef? sign = Def("ui_sign"), perf = Def("ui_perfected");
		FormaDef? dest = Def("destroyer"), ego = Def("ultra_ego");
		if (sign == null || perf == null || dest == null || ego == null) return;
		FormaDef[] asQuatro = [sign, perf, dest, ego];

		// =====================================================================
		// 1. O CABELO: QUATRO FORMAS, QUATRO MODOS DIFERENTES
		// =====================================================================
		// ============================ E ISSO E UMA AFIRMACAO, NAO UMA COINCIDENCIA ============================
		// As quatro sao o unico lugar do catalogo onde os quatro jeitos de vestir cabelo aparecem lado a
		// lado, e cada um sai de uma linha diferente do DM:
		//
		//   `ui_sign`      Trocar          arte de UMA pessoa e mais nada  (UltraInstinct.dm:296-303)
		//   `ui_perfected` TrocarOuTingir  arte OU prata, nunca as duas    (UltraInstinct.dm:288-293)
		//   `destroyer`    Base            nao encosta no cabelo           (UltraEgo.dm:395-396, :400)
		//   `ultra_ego`    Tingir          so pinta, nao ergue             (UltraEgo.dm:387-392)
		//
		// A tabela do `OQueCadaFormaFazNoCabelo` ja cobra cada um destes quatro valores um por um. O que
		// ela NAO cobra e que eles sejam quatro valores DIFERENTES -- e essa e a regra que o dono
		// enunciou ("proprio / tingido / base / misto do UI"). Um `ModoDoCabelo` que colapsasse dois
		// deles seria corrigido na tabela por quem estivesse com pressa, e a regra morreria calada.
		//
		// COMO REPROVA SE A REGRA SUMIR: no `Catalogo.ModoDoCabelo`, tire o ramo
		// `LinhaDeForma.UltraInstinct => TrocarOuTingir` -- o `ui_perfected` cai no `TrocarETingir` do
		// `_`, vira o mesmo modo de meia duzia de outras formas, e esta linha acusa 3 modos em 4 formas.
		// ====================================================================================================
		ModoCabelo[] modos = [.. asQuatro.Select(Jandirus.Core.Forms.Catalogo.ModoDoCabelo)];
		Conferir(modos.Distinct().Count() == 4,
				 "as quatro divinas vestem o cabelo de QUATRO jeitos diferentes ("
			   + string.Join(", ", asQuatro.Zip(modos).Select(p => $"{p.First.Id} {p.Second}")) + ")");

		// ============================ O PAR DO ULTRA EGO, QUE E O CASO MAIS FACIL DE PERDER ============================
		// O DM comenta esta diferenca com todas as letras -- *"SO o ULTRA EGO pinta o cabelo de roxo -- a
		// Destroyer Form mantem o cabelo base (a maior diferenca visual entre as duas formas)"*
		// (`UltraEgo.dm:395-396`) -- e ela e frouxa exatamente por isso: sao duas formas da MESMA linha,
		// com a mesma aura roxa e o mesmo 60x/66x, e dar o roxo as duas "arruma" a Destroyer aos olhos de
		// quem nao leu o original. Feito isso, as duas formas ficam indistinguiveis no corpo.
		//
		// AS DUAS METADES SAO COBRADAS, e nao so a que falta: `destroyer` sem tinta E `ultra_ego` com ela.
		// So a primeira passaria num catalogo onde ninguem pinta nada.
		// ==========================================================================================================
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoCabelo(dest) == null,
				 "a Destroyer NAO pinta o cabelo -- e a maior diferenca visual entre as duas do Ultra Ego "
			   + $"(deu {Jandirus.Core.Forms.Catalogo.CorDoCabelo(dest) ?? "nada"})");
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoCabelo(ego) == "8c32be",
				 "...e o Ultra Ego pinta de #8c32be (`UltraEgo.dm:387-392`) -- deu "
			   + $"{Jandirus.Core.Forms.Catalogo.CorDoCabelo(ego) ?? "nada"}");

		// E NENHUMA DAS DUAS ERGUE O CABELO. O sufixo e o que autoriza a troca de sprite: com um sufixo
		// escrito aqui, a linha do Ultra Ego passaria a procurar arte de Super Saiyajin e a `Herdar` a
		// acharia -- um Ultra Ego de cabelo espetado, que e o defeito que o `Universal` do Ultra Instinct
		// ja existe pra impedir do outro lado.
		Conferir(dest.SufixoDoCabelo.Length == 0 && ego.SufixoDoCabelo.Length == 0,
				 "e nenhuma das duas tem sufixo de cabelo (o Ultra Ego pinta, nao ergue)");

		// =====================================================================
		// 2. O RABO: DUAS DAS QUATRO PINTAM, E SAO AS DUAS DE CIMA
		// =====================================================================
		// ============================ ESTE BLOCO DIZIA O CONTRARIO ATE AGORA ============================
		// Ele afirmava "nenhuma das quatro divinas pinta o rabo -- rabo e da escada Saiyajin", e era
		// leitura correta do DM: `SaiyanObjects.dm:100-118` so olha `ssj`/`lssj`, e nem `UltraInstinct.dm`
		// nem `UltraEgo.dm` tem uma linha sobre cauda. **O dono pediu outra coisa** -- *"Perfected Ultra
		// Instinct: o RABO fica BRANCO"* e *"Ultra Ego: o RABO fica ROXO tambem"* --, e a divergencia
		// esta declarada no `Catalogo.CorDoRabo`.
		//
		// O QUE ESTA LINHA GUARDA AGORA E O RECORTE, que e a parte facil de perder: **as duas de CIMA e
		// so elas**. Nao foi a linha inteira que ganhou rabo pintado -- o Sign e a Destroyer continuam
		// sem, e nos dois pelo mesmo motivo derivado (nao tem tinta de cabelo). Uma "simetria" bem
		// intencionada que desse a cor aos quatro apagaria a diferenca entre Sign e Perfected e entre
		// Destroyer e Ultra Ego, que e justamente o que distingue os degraus dessas duas linhas.
		//
		// COMO REPROVA SE A REGRA SUMIR: no `CorDoRabo`, devolva `d.Aura` no lugar do nulo final e os
		// quatro passam a pintar; devolva as duas linhas pra a lista de exclusao la de cima e os quatro
		// param. Esta linha cai nos dois casos, e ela e a unica que fala do CONJUNTO.
		// ============================================================================================
		string?[] rabos = [.. asQuatro.Select(Jandirus.Core.Forms.Catalogo.CorDoRabo)];
		Conferir(rabos[0] == null && rabos[1] == "b9becb" && rabos[2] == null && rabos[3] == "8c32be",
				 "das quatro divinas so o Perfected (#b9becb) e o Ultra Ego (#8c32be) pintam o rabo -- "
			   + $"deu [{string.Join(", ", rabos.Select(r => r ?? "sem tinta"))}]");

		// E A COR NAO E ESCRITA DUAS VEZES: ela e a tinta de cabelo da propria forma, buscada. Sem esta
		// linha, o `b9becb` e o `8c32be` de cima poderiam ser literais soltos no `CorDoRabo` que
		// envelheceriam separados do cabelo -- e o rabo do Ultra Ego ficaria de um roxo e a cabeca de
		// outro sem nada reclamar.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoRabo(perf) == Jandirus.Core.Forms.Catalogo.CorDoCabelo(perf)
			  && Jandirus.Core.Forms.Catalogo.CorDoRabo(ego) == Jandirus.Core.Forms.Catalogo.CorDoCabelo(ego),
				 "e as duas cores sao a MESMA tinta do cabelo delas (derivadas, nao repetidas)");

		// =====================================================================
		// 2-bis. O OLHO: AS QUATRO MEXEM, E O CORTE E DIFERENTE DO RABO
		// =====================================================================
		// ============================ ESTE E O UNICO LUGAR DO JOGO ONDE OS DOIS CORTES DISCORDAM ============================
		// Na linha do Ultra Instinct o OLHO vale pros dois degraus e o RABO so pro Perfected. Nao e
		// descuido: sao duas regras de fontes diferentes -- o olho e porte (`UltraInstinct.dm:481`, o
		// `Buff()` que nao ramifica por estagio) e o rabo e pedido do dono, derivado da tinta de cabelo,
		// que o Sign nao tem.
		//
		// Quem for "arrumar" a assimetria um dia vai olhar so um dos dois lados. Esta linha e o aviso.
		// ==============================================================================================================
		string?[] olhos = [.. asQuatro.Select(Jandirus.Core.Forms.Catalogo.CorDoOlho)];
		Conferir(olhos[0] == "bec4d0" && olhos[1] == "bec4d0",
				 $"os DOIS degraus do Ultra Instinct banham o olho da mesma prata (deu {olhos[0]}, {olhos[1]})");
		Conferir(olhos[2] == null && olhos[3] == "8c32be",
				 $"e no Ultra Ego so o degrau que PINTA o cabelo pinta o olho (deu {olhos[2] ?? "nada"}, {olhos[3]})");

		// =====================================================================
		// 2-ter. E NA TINTA DO RABO SAO TRES COISAS DIFERENTES NA TELA
		// =====================================================================
		OsTresRabosDivinosNoMaterial(asQuatro);

		// =====================================================================
		// 3. A FOLHA DE AURA: EXISTE, ESTA IMPORTADA, E SE DEIXA TINGIR
		// =====================================================================
		// ============================ E DUAS DAS QUATRO NAO TEM FOLHA NENHUMA ============================
		// Este bloco dizia "as quatro dividem UMA folha cinza e se distinguem SO pela cor", e isso deixou
		// de valer: os dois degraus do Ultra Instinto passaram a devolver `FolhaDeAura.Nebulosa` -- o
		// simbolo de "esta forma nao usa folha", porque o desenho dela e a nuvem procedural.
		//
		// A pergunta 2 (a folha se tinge?) continua valendo, e continua valendo PELOS MESMOS DOIS que
		// sobraram: Destroyer e Ultra Ego dividem a `Base` cinza e so a cor os separa. E a pergunta 1
		// (o arquivo existe?) ganhou um lado novo -- pros dois de UI o que se cobra e o contrario, que
		// NAO haja arquivo. Deixar os dois casos no mesmo laco e o que impede a regressao obvia: um dia
		// em que o Ultra Instinto voltar a apontar pra uma folha, esta linha cai.
		// ============================================================================================
		//
		// QUE FOLHA E ja sai na varredura de aura. O que
		// nao sai de lugar nenhum e se essa folha PRESTA pra estas quatro -- e sao duas perguntas:
		//
		//   1. o arquivo existe e foi importado? A varredura de folhas percorre o `enum` e pergunta isso
		//      folha por folha; aqui se pergunta pelo CAMINHO DA FORMA (`Folha(d)` -> `CaminhoDa`), que e
		//      o percurso que o `World.PrepararAuraDaForma` faz de verdade. As duas linhas caem juntas
		//      hoje; deixam de cair no dia em que uma forma apontar pra uma folha que nao existe.
		//   2. ela se TINGE? Esta e a que ninguem faz. As quatro dividem UMA folha cinza e se distinguem
		//      SO pela cor (`c9d8ff`, `eaf2ff`, `9b4dff`, `b96bff`). Se a `Base` entrasse na lista de arte
		//      ja colorida -- que e o que aconteceu com a `DeusRosa` quando ela ganhou desenho proprio --
		//      as quatro auras sairiam iguais, cinzas, e nenhuma checagem de cor acusaria: a cor
		//      continuaria certa no catalogo, certa no node, e invisivel na tela.
		//
		// COMO REPROVA SE A REGRA SUMIR: acrescente `or FolhaDeAura.Base` ao `SpriteDeAura.PreColorida`.
		// Todo o resto da bancada segue verde e esta linha cai sozinha.
		foreach (FormaDef d in asQuatro)
		{
			bool ehUi = Jandirus.Core.Forms.Catalogo.TemNebulosa(d);
			if (SpriteDeAura.CaminhoDa(Jandirus.Core.Forms.Catalogo.Folha(d)) is not { } caminho)
			{
				Conferir(ehUi, $"`{d.Id}`: sem folha de aura -- e so o Ultra Instinto pode estar assim "
							 + "(o desenho dele e a nuvem)");
				continue;
			}
			Conferir(!ehUi && ResourceLoader.Exists(caminho),
					 $"`{d.Id}`: a folha de aura dela existe e esta IMPORTADA ({caminho.GetFile()})");
		}
		Conferir(!SpriteDeAura.PreColorida(Jandirus.Core.Forms.FolhaDeAura.Base),
				 "e a folha das DUAS que tem folha (Destroyer e Ultra Ego) SE TINGE -- senao as duas "
			   + "auras saem cinzas e iguais");

		// E AS QUATRO CORES SAO QUATRO. Uma folha tingivel nao adianta com o mesmo hexa nas quatro
		// entradas -- e `Aura` era campo de texto livre ate a varredura das cores, entao "vazio" e um
		// estado alcancavel. As duas metades (nao-vazio e distinto) numa linha so.
		string[] cores = [.. asQuatro.Select(d => d.Aura)];
		Conferir(cores.All(c => c.Length > 0) && cores.Distinct().Count() == 4,
				 $"e cada uma tinge de uma cor propria ({string.Join(", ", cores.Select(c => "#" + c))})");

		// =====================================================================
		// 4. O ULTRA INSTINCT NO ROSTER INTEIRO: ARTE PRO GOKU, TINTA PRO RESTO,
		//    E CABELO DE SUPER SAIYAJIN PRA NINGUEM
		// =====================================================================
		AArteDoUltraInstinctNoRosterInteiro();

		// =====================================================================
		// 4-bis. O CABELO DE FULL POWER NO ROSTER INTEIRO (o Grade 4)
		// =====================================================================
		OCabeloPlenoNoRosterInteiro();

		// =====================================================================
		// 5. OS EFEITOS DE CENA: O ROTEIRO CONTRA O DM, E NAO CONTRA SI MESMO
		// =====================================================================
		AsCenasDasQuatroContraODm(asQuatro);
	}

	/// <summary>
	/// O RABO DAS QUATRO DIVINAS MEDIDO NO UNIFORM DO SHADER -- tres coisas diferentes, e nao quatro.
	///
	/// ============================ O QUE ESTE BLOCO ACRESCENTA AO DE CIMA ============================
	/// O bloco `2. O RABO` cobra o CATALOGO (`CorDoRabo` devolve nulo, `b9becb`, nulo, `8c32be`). E
	/// necessario e nao e suficiente, por duas razoes que ja custaram meses neste projeto:
	///
	///   1. **catalogo certo nao e pixel certo.** A tinta do cabelo ficou meses correta no catalogo e
	///      inexistente na tela, porque ninguem media o outro lado do `PintarRabo`. Aqui se le o
	///      uniform `tinta` do node do rabo depois de vestir a forma -- o valor que o shader recebe.
	///   2. **hexa igual nao e cor distinguivel.** O pedido do dono nao foi "#b9becb": foi *"o rabo
	///      fica BRANCO"* e *"o rabo fica ROXO"*. Comparar hexa contra hexa re-le o catalogo e nao
	///      responde se as duas se distinguem de olho -- e um `CorDoRabo` que devolvesse dois cinzas
	///      quase iguais passaria em toda linha do bloco de cima.
	///
	/// ============================ A AFIRMACAO E SOBRE O CONJUNTO: TRES ============================
	/// Quatro formas, TRES resultados na tela: o branco do Perfected, o roxo do Ultra Ego, e a cor da
	/// FICHA nas outras duas (Sign e Destroyer nao encostam no rabo, entao ele fica com o tom que o
	/// jogador escolheu). Nem quatro (que seria alguem dando cor as duas de fora) nem dois (que seria
	/// alguem "simetrizando" o par de cada linha) -- e nenhuma dessas duas deformacoes e pega por uma
	/// checagem que olhe uma forma de cada vez.
	///
	/// COMO REPROVA SE A REGRA SUMIR: devolva as duas linhas divinas pra a lista de exclusao do
	/// `CorDoRabo` e as quatro caem na cor da ficha -- 1 resultado, e a contagem cai. Devolva `d.Aura`
	/// no lugar do nulo final e viram quatro -- cai tambem, e junto cai a linha do branco (a aura do
	/// Perfected e `eaf2ff`, mas a do Sign e `c9d8ff`, que nao e branco nenhum).
	/// ==========================================================================================
	/// </summary>
	private void OsTresRabosDivinosNoMaterial(FormaDef[] asQuatro)
	{
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados)) { Conferir(false, "o catalogo visual pra medir o rabo divino"); return; }
		var cat = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

		var boneco = new CharacterVisual { Name = "BonecoDoRaboDivino" };
		AddChild(boneco);
		try
		{
			boneco.Vestir(cat, new Jandirus.Core.Appearance.Appearance { Cabelo = "Goku" }, "Saiyan", "Male");
			boneco.MostrarRabo(true);
			// O NATURAL SO DEPOIS DA PRIMEIRA CHAMADA: o `PintarRabo` guarda a cor original na primeira
			// vez que roda, e ler antes disso compararia um boneco "ainda nao guardado" com um guardado.
			boneco.VestirCabeloDaForma(null);
			if (boneco.TintaDoRaboDeTeste is not { } daFicha)
			{ Conferir(false, "o boneco divino tem RABO (senao as linhas abaixo nao medem nada)"); return; }

			var naTela = new Vector3[asQuatro.Length];
			for (int i = 0; i < asQuatro.Length; i++)
			{
				boneco.VestirCabeloDaForma(asQuatro[i]);
				naTela[i] = boneco.TintaDoRaboDeTeste ?? Vector3.Zero;
			}
			boneco.VestirCabeloDaForma(null);

			// --- 1. AS DUAS QUE NAO ENCOSTAM ---------------------------------
			// Cobradas contra a cor da FICHA lida do proprio boneco, e nao contra "nulo": o defeito que
			// isto pega e o `PintarRabo` armando tinta e nao a limpando na volta -- o rabo do Sign
			// sairia com o roxo do Ultra Ego que rodou antes dele, e nenhum catalogo saberia.
			Conferir(naTela[0].IsEqualApprox(daFicha) && naTela[2].IsEqualApprox(daFicha),
					 $"`ui_sign` e `destroyer` deixam o rabo na cor da FICHA ({daFicha})");

			// --- 2. O BRANCO DO PERFECTED ------------------------------------
			// A pergunta e "e branco?", nao "e #b9becb?". Branco na tela quer dizer canal alto e os tres
			// juntos: um cinza escuro tem os tres iguais e nao e branco; um azul claro e claro e nao e
			// branco. As duas metades sao cobradas.
			Vector3 br = naTela[1];
			float minBr = Math.Min(br.X, Math.Min(br.Y, br.Z)), maxBr = Math.Max(br.X, Math.Max(br.Y, br.Z));
			Conferir(minBr >= 0.65f && maxBr - minBr <= 0.10f,
					 $"o rabo do `ui_perfected` sai BRANCO como o dono pediu -- claro ({minBr:0.##}) e "
				   + $"neutro (desvio {maxBr - minBr:0.##}) -- {br}");

			// --- 3. O ROXO DO ULTRA EGO --------------------------------------
			// Roxo e o azul e o vermelho por cima do verde -- e o verde e o canal que decide: um roxo que
			// perdesse o corte viraria cinza-azulado e continuaria "com tinta" pra qualquer checagem de
			// hexa. Aqui se cobra a FORMA da cor.
			Vector3 rx = naTela[3];
			Conferir(rx.Z > rx.Y && rx.X > rx.Y && rx.Z - rx.Y >= 0.15f,
					 $"o rabo do `ultra_ego` sai ROXO -- azul e vermelho acima do verde ({rx})");

			// ============================ 4. E SAO TRES, NEM DOIS NEM QUATRO ============================
			// A linha do CONJUNTO, que e a que guarda o recorte. Ver o sumario: as duas deformacoes
			// plausiveis (simetrizar cada par, ou dar cor aos quatro) mudam esta contagem e nao mudam
			// nenhuma checagem por forma.
			// ======================================================================================
			var distintas = new List<Vector3>();
			foreach (Vector3 v in naTela)
				if (!distintas.Any(u => u.IsEqualApprox(v))) distintas.Add(v);
			Conferir(distintas.Count == 3,
					 $"e as quatro divinas dao TRES rabos distintos na tela -- branco, roxo e a cor da "
				   + $"ficha (deu {distintas.Count}: {string.Join(" | ", naTela.Select(v => v.ToString()))})");

			// E O BRANCO E O ROXO NAO SAO A COR DA FICHA. Redundante enquanto a ficha for a cor padrao;
			// deixa de ser no dia em que alguem trocar o boneco desta bancada por um de rabo claro, e ai
			// a contagem de tres continuaria certa com o Perfected "branco" por acidente.
			Conferir(!br.IsEqualApprox(daFicha) && !rx.IsEqualApprox(daFicha),
					 "e nenhuma das duas coincide com a cor da ficha (senao a contagem de tres seria acaso)");
		}
		finally { boneco.QueueFree(); }
	}

	/// <summary>
	/// A REGRA DO ULTRA INSTINCT MEDIDA NOS SESSENTA E DOIS PENTEADOS, E NAO EM DOIS.
	///
	/// ============================ A REGRA, EM UMA LINHA ============================
	/// `ui_apply_hair()` (`UltraInstinct.dm:296-303`) so troca o cabelo `if(hairtypeSaved == "Goku")`.
	/// Quem e Goku ganha ARTE (`Hair_UltraInstinct` no Sign, `Hair_MasteredUltraInstinct` no
	/// Perfected); todo o resto fica com o penteado proprio e recebe TINTA (prata no Perfected, nada
	/// no Sign -- ver `ModoDoCabelo.Trocar` x `TrocarOuTingir`).
	///
	/// ============================ E A TERCEIRA METADE, QUE E A QUE MORDE ============================
	/// **Nenhum dos dois herda cabelo de Super Saiyajin.** Isso nao e consequencia das duas regras
	/// acima: o resolvedor tem tres andares (`Universal` -> `Procurar` -> `Herdar`), e so o primeiro
	/// conhece o Ultra Instinct. Se o `Universal` devolve nulo pra quem nao e Goku e o pedido seguisse
	/// descendo, a `Herdar` acharia com folga -- ela existe justamente pra dar um degrau MAIS BAIXO a
	/// quem nao tem variante propria, e o degrau mais baixo de todos e o `SSj`.
	///
	/// O `Herdar` tem um ramo que devolve nulo pros dois sufixos de UI, e ele e **inerte hoje**: o
	/// `_ => null` do fim faria o mesmo. Ou seja o comentario que explica a regra esta la, e
	/// comportamento nenhum depende dele -- apagar aquelas duas linhas nao muda um pixel, e por isso
	/// nada nesta bancada as protegia. O que morde e a volta: trocar aquele ramo (ou o `_`) por
	/// `Procurar(nome, "SSj")` poe cabelo espetado e dourado numa forma que no original nem ergue o
	/// cabelo, em sessenta e um penteados de uma vez. E o `ui_perfected` continuaria "trocando o
	/// penteado" pra a varredura do boneco, que so pergunta se trocou.
	/// ==========================================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR:
	///   * troque o `return null` do ramo de UI do `CabelosDeForma.Universal` por um `break` pro
	///     `Procurar`/`Herdar` -- caem as duas linhas de "cai em SSJ Hairs" com 61 penteados cada;
	///   * apague a guarda `if(!nome.Contains("Goku"))` -- todo penteado ganha a arte do Goku e caem
	///     as duas linhas de "so o Goku";
	///   * faca o `Universal` devolver o mesmo arquivo pros dois sufixos (o erro de copiar-colar que a
	///     varredura do boneco NAO ve, porque `Contains("UltraInstinct")` e verdade pros dois) -- cai a
	///     linha das duas artes distintas e uma das duas do arquivo exato.
	/// </summary>
	private void AArteDoUltraInstinctNoRosterInteiro()
	{
		const string pasta = "res://Assets/Sprites/Hair/";
		string absoluta = ProjectSettings.GlobalizePath(pasta);
		if (!System.IO.Directory.Exists(absoluta))
		{ Conferir(false, "a bancada acha a pasta dos penteados"); return; }

		string[] roster = [.. System.IO.Directory.GetFiles(absoluta, "*.tres")
							  .Select(a => pasta + System.IO.Path.GetFileName(a))];

		// O ROSTER PRECISA TER GENTE. Uma pasta vazia (ou um filtro errado) faria as seis contagens
		// abaixo darem zero-igual-a-zero e o bloco inteiro passaria verde sem olhar um penteado --
		// o mesmo buraco que o "o boneco precisa ter rabo antes" ja pagou neste arquivo.
		Conferir(roster.Length >= 40, $"o roster de penteados tem gente ({roster.Length} arquivos)");
		if (roster.Length == 0) return;

		// QUEM E GOKU, PELA MESMA PERGUNTA QUE O RESOLVEDOR FAZ (`nome.Contains("Goku")`). Nao e
		// conferir a funcao com ela mesma: o que se mede abaixo e o RESULTADO (que arquivo saiu), e
		// isto aqui e so a particao esperada. Escrever "1" cravado envelheceria no primeiro penteado
		// novo do Goku.
		int gokus = roster.Count(c => c.GetFile().GetBaseName()
									   .Contains("Goku", StringComparison.OrdinalIgnoreCase));
		Conferir(gokus > 0 && gokus < roster.Length,
				 $"e o roster tem penteado do Goku E penteado de outra gente ({gokus} Goku de {roster.Length})");

		foreach ((string sufixo, string arquivoEsperado, string rotulo) in new[]
		{
			(Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstinto,        "Hair_UltraInstinct",         "-Sign-"),
			(Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstintoPerfeito, "Hair_MasteredUltraInstinct", "Perfected"),
		})
		{
			int comArte = 0, naoGokuComArte = 0, emCabeloDeSsj = 0, arquivoTorto = 0;
			foreach (string penteado in roster)
			{
				string? achado = CabelosDeForma.De(penteado, sufixo);
				if (achado == null) continue;

				comArte++;
				if (!penteado.GetFile().GetBaseName().Contains("Goku", StringComparison.OrdinalIgnoreCase))
					naoGokuComArte++;
				// A PASTA E A PROVA. `SSJ Hairs/` e onde moram as 57 variantes de Super Saiyajin, e o
				// Ultra Instinct nao tem uma unica arte la dentro (`UltraInstinct.dm:279`, `:285` poem
				// as duas dele fora da pasta de SSJ, e a conversao seguiu o original). Qualquer caminho
				// que caia ali so pode ter vindo do `Procurar` ou da `Herdar` -- ou seja, de heranca.
				if (achado.Contains("SSJ Hairs/", StringComparison.Ordinal)) emCabeloDeSsj++;
				if (achado.GetFile().GetBaseName() != arquivoEsperado) arquivoTorto++;
			}

			Conferir(comArte == gokus,
					 $"UI {rotulo}: SO o penteado do Goku ganha arte propria ({comArte} de {roster.Length}, "
				   + $"esperado {gokus})");
			Conferir(naoGokuComArte == 0,
					 $"UI {rotulo}: e nenhum outro penteado ganha ({naoGokuComArte} ganharam)");
			// A LINHA QUE O DONO PEDIU COM ESSE NOME. Ela e separada da de cima de proposito: "nao ganha
			// arte" e "nao ganha arte DE SUPER SAIYAJIN" falham por caminhos diferentes, e a segunda e a
			// que uma heranca nova traria de volta.
			Conferir(emCabeloDeSsj == 0,
					 $"UI {rotulo}: e NENHUM penteado herda cabelo de Super Saiyajin ({emCabeloDeSsj} herdaram)");
			Conferir(arquivoTorto == 0,
					 $"UI {rotulo}: e a arte que sai e `{arquivoEsperado}` ({arquivoTorto} fora do lugar)");
		}

		// ============================ AS DUAS ARTES SAO DOIS ARQUIVOS ============================
		// `"Hair_MasteredUltraInstinct".Contains("UltraInstinct")` e VERDADE, e e assim que a varredura
		// do boneco pergunta. Ou seja: se o `Universal` devolvesse a folha do Sign pros dois sufixos --
		// um `?:` invertido, uma constante trocada --, o Perfected sairia com o cabelo do Sign e a
		// bancada inteira continuaria verde. Esta linha e a unica que separa os dois estagios.
		// ====================================================================================
		string? doSign = CabelosDeForma.De(pasta + "Hair_Goku.tres",
										   Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstinto);
		string? doPerf = CabelosDeForma.De(pasta + "Hair_Goku.tres",
										   Jandirus.Core.Forms.Catalogo.SufixoDoUltraInstintoPerfeito);
		Conferir(doSign != null && doPerf != null && doSign != doPerf,
				 $"e o Sign e o Perfected sao DOIS desenhos distintos ({doSign?.GetFile() ?? "nada"} x "
			   + $"{doPerf?.GetFile() ?? "nada"})");

		// ============================ O CONTROLE NEGATIVO ============================
		// Tudo acima e uma conta de NULOS: 61 penteados devolvendo nada. Um `CabelosDeForma.De` quebrado
		// (pasta renomeada, `ResourceLoader` mudo, cache envenenado) devolve nada pra TUDO e as seis
		// linhas de cima ficam verdes -- provando que o sistema nao faz nada, com a cara de quem prova
		// que ele obedece. O mesmo roster pedido em `SSj` TEM que achar arte a rodo.
		// ======================================================================
		(int com, _) = CabelosDeForma.Cobertura(roster, "SSj");
		Conferir(com >= 15,
				 $"e o mesmo roster ACHA cabelo de SSJ ({com}) -- senao os nulos acima nao provam nada");
	}

	/// <summary>
	/// ============================ O CABELO DE FULL POWER, PENTEADO POR PENTEADO ============================
	/// O dono: *"quando masterizo o ssj com qualquer cabelo, ele n muda pra versao fp que seria a full
	/// power, nem todo cabelo tem, mas kidgohan, goku etc tem a versao fp e n ta mudando mesmo com
	/// maestria em 100%"*. Duas metades no mesmo pedido, e a segunda e a que uma bancada preguicosa
	/// deixa passar: **quem tem ganha, e quem NAO tem cai no cabelo normal da forma** -- nunca em nada,
	/// nunca no de outra forma.
	///
	/// ============================ POR QUE ISTO NAO CABE NA BANCADA DE CONSOLE ============================
	/// O `formas` do AssetPipeline ja prova a metade do Core: `SufixoDoCabeloDe(ssj1, dominada: true)`
	/// devolve `SSjFP` e nenhuma entrada escreve esse sufixo na mao. Mas o `SSjFP` so vira ARTE depois
	/// de passar pelo <see cref="CabelosDeForma"/>, que pergunta ao DISCO -- e a pergunta e irregular
	/// (sete padroes de nome, tres caixas, uma cadeia de heranca). O sufixo pode estar certo e a folha
	/// nao existir; a folha pode existir e o resolvedor nao a alcancar. Foi exatamente o caso do
	/// `Inferno SSJFP`, desenhado e inalcancavel ate o padrao do `" Hair"` no fim entrar.
	/// ================================================================================================
	///
	/// ============================ O FALLBACK ERRADO E PIOR QUE FALLBACK NENHUM ============================
	/// A `Herdar` mandava `SSjFP` pra `SSj3` antes de cair no `SSj`. Enquanto ninguem pedia `SSjFP`
	/// aquilo era letra morta; hoje e o caminho da MAIORIA dos penteados, e o salto pelo SSJ3 poria
	/// cabelo de um degrau que o jogador **pode nem ter despertado** na cabeca de quem acabou de
	/// dominar o SSJ1. Por isso a linha central deste bloco nao e "achou alguma coisa" e sim
	/// **`fp == ssj` exatamente**, penteado a penteado.
	/// ==================================================================================================
	///
	/// COMO CADA FAMILIA DAQUI REPROVA:
	///   * troque o `Procurar(nome, "SSj")` do ramo `SufixoDoSuperSaiyajinPleno` da `Herdar` por
	///     `Procurar(nome, "SSj3")` -- cai a linha do fallback, com a lista dos penteados que foram
	///     parar no cabelo errado;
	///   * apague aquele ramo inteiro (deixando o `_ => null`) -- caem a do fallback E a de "nenhum
	///     penteado fica sem cabelo", e o Grade 4 volta a ser careca pra 48 dos 58;
	///   * apague o padrao `$"{semHair} {s}"` do `Procurar` -- a contagem de folhas de FP cai de 10 pra
	///     9 e a linha do numero de artes proprias reprova.
	/// </summary>
	private void OCabeloPlenoNoRosterInteiro()
	{
		const string dados = "res://Assets/Data/visual.json";
		if (!Godot.FileAccess.FileExists(dados))
		{ Conferir(false, "o catalogo visual pra a varredura do Full Power"); return; }
		var cat = Jandirus.Core.Appearance.VisualCatalog.Parse(Godot.FileAccess.GetFileAsString(dados));

		// O ROSTER E O DOS PENTEADOS **JOGAVEIS**, e nao a pasta. A diferenca importa: a pasta tem
		// folhas que nenhum personagem consegue escolher (`Hair_TrunksUSSj`, `Hair_BrolyLssj`), e
		// contar por ela daria cobertura pra gente que nao existe. Quem responde "o que da pra
		// escolher na criacao" e o `visual.json`, que e o mesmo arquivo que a tela de criacao le.
		string[] roster = [.. cat.Cabelos.Where(c => !string.IsNullOrEmpty(c.Sprite)).Select(c => c.Sprite!)];

		// O ROSTER PRECISA TER GENTE -- mesmo buraco do bloco do Ultra Instinct logo acima: um roster
		// vazio faria todas as contagens abaixo darem zero-igual-a-zero e o bloco sairia verde sem
		// olhar um penteado.
		Conferir(roster.Length >= 40, $"FP: o roster de penteados jogaveis tem gente ({roster.Length})");
		if (roster.Length == 0) return;

		const string SufFp = Jandirus.Core.Forms.Catalogo.SufixoDoSuperSaiyajinPleno;

		int comArtePropria = 0, caiuNoSsj = 0, semNada = 0;
		var semCabelo = new List<string>();
		var noCabeloErrado = new List<string>();
		var artePropriaSemFp = new List<string>();

		foreach (string penteado in roster)
		{
			string? fp = CabelosDeForma.De(penteado, SufFp);
			string? ssj = CabelosDeForma.De(penteado, "SSj");

			if (fp == null) { semNada++; semCabelo.Add(penteado.GetFile()); continue; }

			// ARTE PROPRIA DE FULL POWER ou o cabelo normal da forma -- e nao ha terceira resposta
			// legitima. A pergunta e pelo NOME DO ARQUIVO e nao pela igualdade com `ssj`, porque as
			// duas coisas tem que ser conferidas separadas: um resolvedor que devolvesse o `SSj` pra
			// TODO mundo passaria numa e falharia na outra, que e o que se quer.
			bool ehFolhaDeFp = fp.GetFile().GetBaseName()
								 .EndsWith("FP", StringComparison.OrdinalIgnoreCase);
			if (ehFolhaDeFp)
			{
				comArtePropria++;
				// E ELA TEM QUE SER DO PENTEADO CERTO. `Hair_GokuSSjFP` pro Goku, e nao a do vizinho --
				// um `Procurar` largo demais casaria o primeiro FP que achasse pra todo mundo.
				if (!fp.Contains(SufFp, StringComparison.OrdinalIgnoreCase)
					&& !fp.Contains("SSJFP", StringComparison.OrdinalIgnoreCase))
					artePropriaSemFp.Add(fp.GetFile());
			}
			else if (fp == ssj) caiuNoSsj++;
			else noCabeloErrado.Add($"{penteado.GetFile()} -> {fp.GetFile()} (esperado {ssj?.GetFile() ?? "o de SSJ"})");
		}

		_passos.Add($"  --     FP: {comArtePropria} com arte propria, {caiuNoSsj} no cabelo de SSJ, "
				  + $"{semNada} no penteado base (de {roster.Length})");

		// ---- 1. QUEM TEM, GANHA ----
		// O numero e um PISO e nao um igual: uma folha nova de FP desenhada amanha nao pode reprovar a
		// bancada. O que ele impede e a regressao -- e dez e o que a pasta entrega hoje.
		Conferir(comArtePropria >= 10,
				 $"FP: os penteados com folha propria de Full Power a recebem ({comArtePropria})");
		Conferir(artePropriaSemFp.Count == 0,
				 $"FP: e a folha que sai e a de Full Power do PROPRIO penteado "
			   + $"({artePropriaSemFp.Count} tortas: {string.Join(", ", artePropriaSemFp.Take(4))})");

		// ---- 2. QUEM NAO TEM, CAI NO CABELO NORMAL DA FORMA -- E EM MAIS NADA ----
		// ESTA E A LINHA CENTRAL DO BLOCO. Ela nao aceita `SSj2`, `SSj3`, `USSj` nem `SSJ4`: dominar o
		// Super Saiyajin nao pode dar a ninguem o cabelo de um degrau que ele talvez nem tenha visto.
		Conferir(noCabeloErrado.Count == 0,
				 $"FP: quem nao tem folha propria cai EXATAMENTE no cabelo de SSJ, e nunca no de outra "
			   + $"forma ({noCabeloErrado.Count} desviados: {string.Join(" | ", noCabeloErrado.Take(3))})");

		// ---- 3. E NUNCA EM NADA, PRA QUEM TEM CABELO DE SSJ ----
		// Um `null` aqui e o Grade 4 CARECA. So e aceitavel pra quem tambem nao tem cabelo de Super
		// Saiyajin nenhum (o penteado fica e leva a tinta) -- e e por isso que a conta e comparada com
		// o `SSj` e nao com zero.
		var carecaSoNoFp = roster
			.Where(p => CabelosDeForma.De(p, SufFp) == null && CabelosDeForma.De(p, "SSj") != null)
			.Select(p => p.GetFile()).ToArray();
		Conferir(carecaSoNoFp.Length == 0,
				 $"FP: ninguem que tem cabelo de SSJ fica SEM cabelo no Full Power "
			   + $"({carecaSoNoFp.Length}: {string.Join(", ", carecaSoNoFp.Take(4))})");

		// ---- 4. O CONTROLE NEGATIVO: A VARREDURA ENXERGA ----
		// ============================ TRES DAS QUATRO LINHAS ACIMA CONTAM ZEROS ============================
		// Um `CabelosDeForma.De` mudo (pasta renomeada, `ResourceLoader` sem os `.import`, cache
		// envenenado) devolve nulo pra tudo: `noCabeloErrado` fica vazio, `artePropriaSemFp` fica vazio,
		// e so a primeira linha cairia. As duas medidas abaixo separam "o sistema obedece" de "o sistema
		// nao esta ligado" -- a primeira e que ele ACHA arte, a segunda e que ele DISCRIMINA (um sufixo
		// que nao existe no disco tem que dar nada pra todo mundo, senao ele esta casando por acaso).
		// ==============================================================================================
		(int comSsj, _) = CabelosDeForma.Cobertura(roster, "SSj");
		Conferir(comSsj >= 15,
				 $"FP CONTROLE NEGATIVO: o mesmo roster ACHA cabelo de SSJ ({comSsj}) -- senao os zeros "
			   + "acima nao provam nada");
		(int comInventado, _) = CabelosDeForma.Cobertura(roster, "SSjQueNaoExiste");
		Conferir(comInventado == 0,
				 $"FP CONTROLE NEGATIVO: e um sufixo inventado nao casa com ninguem ({comInventado}) -- "
			   + "senao o resolvedor estaria achando por acaso");

		// ---- 5. E O SUFIXO SO E PEDIDO AOS 100% ----
		// A ponta do Core, refeita AQUI de proposito: o resolvedor acima e alimentado pelo
		// `SufixoDoCabeloDe`, e provar as duas metades em bancadas diferentes deixa a costura entre elas
		// sem vigia. Se o sufixo passar a sair sem maestria, todo Super Saiyajin do jogo nasce Grade 4.
		var ssj1 = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdSsj1)!;
		string cru = Jandirus.Core.Forms.Catalogo.SufixoDoCabeloDe(ssj1, dominada: false);
		string pleno = Jandirus.Core.Forms.Catalogo.SufixoDoCabeloDe(ssj1, dominada: true);
		Conferir(cru == "SSj" && pleno == SufFp,
				 $"FP: o SSJ1 pede `{cru}` cru e `{pleno}` dominado -- e o resolvedor recebe essa troca");
		// E A TROCA TEM QUE APARECER NA ARTE de quem tem folha: sem esta linha, um resolvedor que
		// ignorasse o sufixo (devolvendo sempre o `SSj`) passaria em tudo acima, porque "cai no cabelo
		// de SSJ" e resposta valida pra 48 dos 58.
		string? gokuCru = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Goku.tres", cru);
		string? gokuPleno = CabelosDeForma.De("res://Assets/Sprites/Hair/Hair_Goku.tres", pleno);
		Conferir(gokuCru != null && gokuPleno != null && gokuCru != gokuPleno,
				 $"FP: e no Goku sao DUAS folhas distintas ({gokuCru?.GetFile() ?? "nada"} x "
			   + $"{gokuPleno?.GetFile() ?? "nada"})");
	}

	/// <summary>
	/// OS EFEITOS DE CENA DAS QUATRO, COBRADOS CONTRA O DM.
	///
	/// ============================ A DIFERENCA PRO BLOCO VIVO ============================
	/// O <see cref="NoCorpo"/> ja roda as quatro cenas e conta anel, cascalho, clarao e descarga --
	/// mas sempre contra o proprio roteiro delas (`tc.AneisDeTeste == quantos(AnelDeChoque)`). Essa
	/// pergunta mede o TOCADOR: ela pega um beat que nao chega no `Disparar`, e e pra isso que existe.
	///
	/// O que ela nao pode pegar, por construcao, e um roteiro ERRADO -- ela e verdadeira pra qualquer
	/// roteiro. Este bloco pergunta a outra metade: o roteiro diz o que o original diz?
	/// ================================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR:
	///   * acrescente `| Efeito.DescargaNoCeu` ao climax do `ui_sign` -- o bloco vivo continua verde
	///     (ele contaria 1 de 1) e caem duas linhas aqui;
	///   * tire a descarga do `ultra_ego` -- mesma coisa, do outro lado;
	///   * ponha `Efeito.Raios` em qualquer uma das quatro -- caem as linhas da faisca. Estas quatro
	///     cenas sao escritas beat a beat e NAO passam pelo `Cinematicas.Faisca`, entao o guarda que
	///     acerta as cenas de fabrica sozinho nao alcanca nenhuma delas.
	/// </summary>
	private void AsCenasDasQuatroContraODm(FormaDef[] asQuatro)
	{
		var cenas = new Dictionary<string, Jandirus.Core.Forms.Cinematica>();
		foreach (FormaDef d in asQuatro)
		{
			Jandirus.Core.Forms.Cinematica? c = Jandirus.Core.Forms.Cinematicas.De(d.Id);
			Conferir(c != null, $"`{d.Id}` tem cena propria (o climax dela e o que se mede abaixo)");
			if (c != null) cenas[d.Id] = c;
		}
		if (cenas.Count != asQuatro.Length) return;

		// O CLIMAX E O BEAT QUE ASSUME, e nao o ultimo: as quatro tem uma cauda de poeira depois dele
		// (ver o `new(20.0, Efeito.Poeira)` das tres longas). Medir o ultimo beat mediria a cauda.
		Jandirus.Core.Forms.Beat Climax(string id) =>
			cenas[id].Beats.First(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir));

		// --- 1. O INSTANTE DA FORMA E UM SO DESENHO NAS QUATRO ---------------
		// Clarao + anel + cratera no beat que assume. Nao e gosto: e o unico momento em que as quatro
		// concordam, e por isso ele serve de linha de base pro contraste que vem depois. Uma cena que
		// perca o clarao troca "a forma CHEGOU" por "o sprite mudou".
		foreach (string id in cenas.Keys)
		{
			Jandirus.Core.Forms.Beat cl = Climax(id);
			Conferir(cl.Faz.HasFlag(Jandirus.Core.Forms.Efeito.ClaraoDeTela)
					 && cl.Faz.HasFlag(Jandirus.Core.Forms.Efeito.AnelDeChoque)
					 && cl.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Cratera),
					 $"`{id}`: o climax traz clarao, anel e cratera ({cl.Faz})");
		}

		// --- 2. O EGO NAO CONHECE SILENCIO -----------------------------------
		// ============================ AS DUAS CENAS SAO A MESMA `proc`, E TERMINAM AO CONTRARIO ============================
		// `ui_grand_cinematic()` e `ue_grand_cinematic()` sao gemeas ate o tique: 12 ciclos de subida,
		// 10 de surto, 220 tiques. A UNICA diferenca de roteiro esta no fim, e o DM a comenta dos dois
		// lados -- *"... e entao, silencio. A poeira paira no ar."* no Ultra Instinto, *"o EGO nao
		// conhece silencio"* no Ultra Ego.
		//
		// Como sao a mesma receita, elas sao o alvo natural de um copiar-colar: quem for encher uma
		// enche a outra junto, e a cena que o original fecha em silencio ganha o ceu partindo. Nenhuma
		// checagem de contagem veria isso -- por isso ela esta escrita como uma AFIRMACAO SOBRE O
		// ROTEIRO, dos dois lados.
		// ============================================================================================================
		int comDescarga = cenas.Count(kv => kv.Value.Beats.Any(
			b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.DescargaNoCeu)));
		Conferir(comDescarga == 1,
				 $"so UMA das quatro parte o ceu ({comDescarga}) -- o resto das divinas cala no climax");
		Conferir(Climax("ultra_ego").Faz.HasFlag(Jandirus.Core.Forms.Efeito.DescargaNoCeu),
				 "e a que parte e o Ultra Ego, no climax (`o EGO nao conhece silencio`)");
		foreach (string mudo in new[] { "ui_sign", "ui_perfected", "destroyer" })
			Conferir(!cenas[mudo].Beats.Any(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.DescargaNoCeu)),
					 $"`{mudo}`: nenhuma descarga no ceu, em beat nenhum");

		// --- 3. O SIGN CALA, O PERFECTED NAO ---------------------------------
		// O contraste interno da linha do Ultra Instinct: o -Sign- e a contencao (*"nada explode, o ar e
		// que sai do lugar"*) e o Perfected e o instinto COMPLETO, que ja deixou a contencao pra tras.
		//
		// ============================ ISTO ERA MEDIDO EM PEDRA. NAO E MAIS ============================
		// A checagem dizia "o Sign nao levanta pedra em beat nenhum; o gemeo levanta", e ela era MINHA --
		// saiu da leitura do roteiro, nao de uma palavra do dono. O dono disse o contrario, e sem
		// excecao: *"deveria ter mais `rising rocks.png` q ficariam do inicio ao fim em TODAS as
		// transformacoes"*. As duas unicas cenas que ele isentou sao as do macaco.
		//
		// Entao o contraste passou a ser medido no que ele SEMPRE foi de verdade -- o silencio. O -Sign-
		// e a unica cena da linha divina sem descarga no ceu (a checagem 2, logo acima) e sem faisca
		// (`Raios = 0` no catalogo, checagem 4, logo abaixo). Contencao continua escrita na cena; ela so
		// deixou de ser escrita no efeito que o dono quis em todas.
		// ==========================================================================================
		Conferir(cenas["ui_sign"].OChaoSeSolta && cenas["ui_perfected"].OChaoSeSolta,
				 "`ui_sign` e `ui_perfected` levantam pedra, como TODAS (a excecao do dono e so o macaco)");

		// --- 4. A FAISCA CORTADA, NAS QUATRO ---------------------------------
		// *"raiozinhos somente o lssj 2 do primal legendary"*. O `Cinematicas.Faisca` faz as cenas de
		// FABRICA obedecerem sozinhas -- e as quatro divinas nao passam por ele: sao escritas beat a
		// beat, e tres delas (`ui_perfected`, `destroyer`, `ultra_ego`) precisaram de mao no corte.
		// Cena escrita a mao nao tem guarda; esta linha e a guarda.
		//
		// AS DUAS PONTAS, e e por isso que sao duas linhas: o roteiro nao pede faisca, e o catalogo nao
		// tem faisca. Uma forma que ganhasse `Raios` amanha sem beat nenhum nao le como defeito na
		// primeira; le na segunda.
		foreach (FormaDef d in asQuatro)
		{
			Conferir(!cenas[d.Id].Beats.Any(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Raios)),
					 $"`{d.Id}`: a cena nao solta faisca (o corte do dono)");
			Conferir(d.Raios == 0, $"`{d.Id}`: ...e a forma tambem nao tem faisca no catalogo ({d.Raios})");
		}

		// --- 5. OS GEMEOS TEM O MESMO RELOGIO, E A DESTROYER NAO -------------
		// 220 tiques nas tres que saem de `proc` (`UltraInstinct.dm:326` duas vezes, `UltraEgo.dm:415`),
		// e a Destroyer FORA -- ela e invencao deste port, porque o DM diz textualmente que ela nao tem
		// cinematica (`UltraEgo.dm:530`).
		//
		// A `ADuracaoDoDm` ja cobra cada um dos tres prazos contra a tabela de tiques. O que se afirma
		// aqui e o PARENTESCO: os tres sao o mesmo numero porque sao a mesma receita, e a quarta e
		// diferente porque nao tem receita. Escrito assim, dar 22,0 s a Destroyer "pra ficar igual as
		// irmas" -- que e a mudanca tentadora -- reprova, e reprova dizendo por que.
		double preso = cenas["ui_sign"].SegundosPreso;
		Conferir(Mathf.Abs(cenas["ui_perfected"].SegundosPreso - preso) < 0.01
				 && Mathf.Abs(cenas["ultra_ego"].SegundosPreso - preso) < 0.01,
				 $"as tres cenas de `proc` prendem o mesmo tanto ({preso:0.#}s) -- sao 220 tiques cada");
		Conferir(cenas["destroyer"].SegundosPreso < preso,
				 $"e a Destroyer NAO ({cenas["destroyer"].SegundosPreso:0.#}s) -- ela e invencao, o DM nao "
			   + "lhe da cinematica");
	}

	// =====================================================================
	// 3a-quater. O PISCAR DE CABELO DURA A CENA INTEIRA
	// =====================================================================
	/// <summary>
	/// O CABELO ALTERNANDO DA LARGADA ATE A FORMA FICAR -- nas DUAS versoes da cena.
	///
	/// ============================ O DEFEITO QUE ELE EXISTE PRA IMPEDIR ============================
	/// O dono: *"a do ssj1 o cabelo base e do ssj ficam trocando (oq e legal) mas e so no inicio da
	/// cinematica, teria q durar a cinematica toda, e na transformaçao acelerada (maestria menor q 50%)
	/// tb"*. O piscar era um PULSO por beat, e a cena do SSJ1 gastava os quatro beats dele no primeiro
	/// segundo e meio de vinte e cinco -- ou seja o efeito existia, funcionava, e ninguem o via depois
	/// dos 2,9 s.
	///
	/// E e o tipo de defeito que nao aparece em teste de roteiro nenhum: os quatro beats estavam la, a
	/// cena estava bonita, e a checagem "a cena tem beats" passava. So a TRAJETORIA acusa.
	///
	/// ============================ POR QUE NAO BASTA CONTAR TROCAS ============================
	/// "Piscou 37 vezes" tambem seria verdade com as 37 apertadas nos dois primeiros segundos -- que e
	/// literalmente o estado anterior, so que com mais beats. O que se mede aqui e ONDE a ultima troca
	/// cai: ela tem que estar a menos de uma piscada (<see cref="Jandirus.Core.Forms.Cinematicas.PiscadaMaxima"/>)
	/// do beat que ASSUME. Esse numero e derivado do roteiro e nao escolhido: se o piscar chega vivo ate
	/// o fim, o intervalo maximo do sorteio e a maior distancia possivel entre a ultima troca e o fim.
	///
	/// ============================ E A ENCURTADA CORRE JUNTO ============================
	/// O pedido do dono cita as duas ("e na transformaçao acelerada tb"), e elas podiam divergir de
	/// verdade: o `Encurtar` comprime os INSTANTES por `k`, e um piscar por beat pisca 2,5x mais rapido
	/// na curta. Como estado, nao: a cadencia e do relogio e nao dos beats. As duas passam pelo mesmo
	/// laco aqui pra que isso seja afirmado e nao suposto.
	///
	/// ============================ E TODAS AS CENAS QUE PISCAM, E NAO SO A DO SSJ1 ============================
	/// Sao tres hoje (`ssj1`, `future_ssj`, `primal_c_type`) e a lista NAO esta escrita aqui: ela sai
	/// do roteiro, por `Beats.Any(PiscaCabelo)`. Medir so a do SSJ1 deixava as outras duas de fora do
	/// mesmo jeito que o `Efeito` deixou o piscar de fora da encurtada -- e uma cena nova que arme o
	/// piscar entra nesta varredura sozinha, que e o unico jeito de a cobertura nao envelhecer.
	///
	/// As tres nao sao a mesma medida repetida: elas tem duracoes bem diferentes (a janela do
	/// `primal_c_type` e uma fracao da do `ssj1`), e o PISO de trocas e derivado da janela de cada uma.
	/// ====================================================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR:
	///   * troque o `_piscaLigada = true` do `Transformacao.Disparar` de volta por `_piscando = !_piscando`
	///     -- cai "pisca ate o fim" nas seis passadas (tres cenas x duas versoes);
	///   * apague o `_piscaLigada = false` do `Assumir` -- cai "para de piscar quando a forma fica";
	///   * apague o bloco do `Soltar` -- cai a ultima linha, a da cena interrompida.
	/// =========================================================================================
	/// </summary>
	private void OPiscarDuraACenaInteira(Node2D corpo, CharacterVisual vis)
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo) return;
		int eu = GameClient.Instance?.LocalId ?? 0;
		if (eu == 0) return;

		FormaDef raiz = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdBase)!;

		Node atores = corpo.GetParent();
		List<Transformacao> Cenas()
		{
			var l = new List<Transformacao>();
			foreach (Node n in atores.GetChildren()) if (n is Transformacao tr) l.Add(tr);
			return l;
		}

		const double Passo = 0.05;

		// ============================ UM PASSEIO POR VERSAO, E A MESMA REGRA NOS DOIS ============================
		// Funcao local e nao duas copias pelo motivo de sempre neste arquivo: a diferenca entre a estreia
		// e a encurtada e o `k`, e uma regra conferida so na cheia deixaria sem bancada justamente a
		// versao que o jogador ve na maioria das vezes.
		// ====================================================================================================
		void Passear(FormaDef alvo, string cabeloSsj, Jandirus.Core.Forms.DegrauDeCena degrau,
					 Jandirus.Core.Forms.Cinematica roteiro, string rotulo)
		{
			double arma = roteiro.Beats
				.First(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.PiscaCabelo)).Em;
			double assume = roteiro.Beats
				.First(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Assumir)).Em;

			mundo.AoMudarForma(eu, alvo.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			List<Transformacao> antes = Cenas();
			mundo.AoMudarForma(eu, raiz.IdRede, alvo.IdRede, degrau);

			Transformacao? cena = null;
			foreach (Transformacao tr in Cenas()) if (!antes.Contains(tr)) cena = tr;
			Conferir(cena != null, $"{rotulo}: a cena nasce");
			if (cena == null) return;
			cena.SetProcess(false);        // o relogio e nosso, como no resto desta bancada

			var trocas = new List<double>();
			string ultimo = vis.CabeloDeTeste;
			double depoisDeAssumir = -1;
			string cabeloNoFim = ultimo;

			for (double t = 0; t < roteiro.Segundos + 1.0 && IsInstanceValid(cena); t += Passo)
			{
				cena._Process(Passo);
				if (!IsInstanceValid(cena)) break;
				if (vis.CabeloDeTeste == ultimo) continue;

				ultimo = vis.CabeloDeTeste;
				trocas.Add(cena.TempoDeTeste);
				// UMA TROCA DEPOIS DA FORMA FICAR e o defeito espelhado: o piscar sobrevivendo ao
				// `Assumir` deixaria o personagem ja transformado voltando ao cabelo normal de vez em
				// quando. A folga de um passo e do laco, e nao da regra -- o `Assumir` e um beat e ele
				// dispara dentro do quadro em que vence.
				if (cena.TempoDeTeste > assume + Passo * 1.5 && depoisDeAssumir < 0)
					depoisDeAssumir = cena.TempoDeTeste;
			}
			cabeloNoFim = vis.CabeloDeTeste;

			double janela = assume - arma;
			double ultima = trocas.Count > 0 ? trocas[^1] : -1;

			// ============================ 1. PISCOU MUITO, E NAO QUATRO VEZES ============================
			// O PISO E DERIVADO: a janela dividida pelo MAIOR intervalo do sorteio e o minimo de trocas
			// que um piscar vivo pode dar. Escrever "10" aqui daria verde pro piscar antigo na cena
			// cheia (ele dava 4... e daria 10 se alguem enchesse a cena de beats, que e a saida errada).
			// ========================================================================================
			int minimo = (int)(janela / Jandirus.Core.Forms.Cinematicas.PiscadaMaxima) - 1;
			Conferir(trocas.Count >= minimo,
					 $"{rotulo}: o cabelo pisca a cena toda ({trocas.Count} trocas, minimo {minimo} "
				   + $"pra uma janela de {janela:0.#}s)");

			// --- 2. E A ULTIMA CAI COLADA NO FIM (a queixa do dono, medida) ---
			Conferir(ultima >= assume - Jandirus.Core.Forms.Cinematicas.PiscadaMaxima - Passo * 2,
					 $"{rotulo}: e a ULTIMA troca cai colada no fim (aos {ultima:0.##}s de {assume:0.##}s) "
				   + "-- nao so no inicio");

			// --- 3. E A PRIMEIRA CAI NO BEAT QUE ARMA, e nao antes ---
			Conferir(trocas.Count > 0 && trocas[0] >= arma - Passo * 1.5 && trocas[0] <= arma + Passo * 2,
					 $"{rotulo}: a primeira troca sai no beat que arma ({(trocas.Count > 0 ? trocas[0] : -1):0.##}s "
				   + $"contra {arma:0.##}s)");

			// --- 4. PARA QUANDO A FORMA FICA, e o cabelo que sobra e o da forma ---
			Conferir(depoisDeAssumir < 0,
					 $"{rotulo}: o piscar PARA no beat que assume (houve troca aos {depoisDeAssumir:0.##}s)");
			Conferir(cabeloNoFim == cabeloSsj,
					 $"{rotulo}: e a cena termina com o cabelo da FORMA ({cabeloNoFim.GetFile()})");

			_passos.Add($"  --     piscar {rotulo}: {trocas.Count} trocas entre {arma:0.##}s e "
					  + $"{ultima:0.##}s (assume aos {assume:0.##}s)");
		}

		// ============================ AS CENAS QUE ARMAM O PISCAR, PERGUNTADAS AO ROTEIRO ============================
		// Sao tres hoje, e escrever os tres ids aqui seria a mesma divida que a lista de "as cinco
		// formas com faisca" ja cobrou uma vez neste arquivo: a cena nova nasceria sem bancada e
		// ninguem saberia. Perguntar `Beats.Any(PiscaCabelo)` faz a cobertura acompanhar o roteiro.
		//
		// O PISO DE TRES nao e enfeite: se um dia a derivacao devolvesse lista VAZIA (um `Efeito`
		// renomeado, um beat perdido num merge), o `foreach` abaixo nao rodaria nenhuma vez e o
		// bloco inteiro sairia verde sem ter medido nada.
		// ========================================================================================================
		Jandirus.Core.Forms.Cinematica[] queArmam = [.. Jandirus.Core.Forms.Cinematicas.Todas
			.Where(c => c.Beats.Any(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.PiscaCabelo)))];
		Conferir(queArmam.Length >= 3,
				 $"o roteiro tem cenas que ARMAM o piscar ({queArmam.Length}): "
			   + string.Join(", ", queArmam.Select(c => c.Forma)));

		foreach (Jandirus.Core.Forms.Cinematica cheia in queArmam)
		{
			if (Jandirus.Core.Forms.Catalogo.Def(cheia.Forma) is not { } alvo)
			{ Conferir(false, $"a cena de `{cheia.Forma}` aponta pra uma forma que existe"); continue; }

			// --- COMO OS DOIS LADOS DA PISCADA SE PARECEM, medidos antes de a cena existir ---
			mundo.AoMudarForma(eu, alvo.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			vis.CabeloDaForma(alvo.SufixoDoCabelo);
			string cabeloSsj = vis.CabeloDeTeste;
			vis.CabeloDaForma("");
			string cabeloBase = vis.CabeloDeTeste;

			// O ZERO NAO PODE SER VAZIO: num corpo cujo penteado nao tenha variante de Super Saiyajin os
			// dois lados seriam o MESMO arquivo, nenhuma troca se anotaria, e todas as linhas abaixo
			// mediriam um piscar perfeito e um piscar deletado exatamente igual.
			Conferir(cabeloBase.Length > 0 && cabeloSsj != cabeloBase,
					 $"`{alvo.Id}`: os dois lados da piscada sao ARQUIVOS DIFERENTES -- senao a medicao e cega "
				   + $"({cabeloBase.GetFile()} / {cabeloSsj.GetFile()})");
			if (cabeloSsj == cabeloBase) continue;

			Passear(alvo, cabeloSsj, Jandirus.Core.Forms.DegrauDeCena.Estreia, cheia, $"{alvo.Id} estreia");
			Passear(alvo, cabeloSsj, Jandirus.Core.Forms.DegrauDeCena.Curta,
					Jandirus.Core.Forms.Cinematicas.Encurtada(cheia), $"{alvo.Id} encurtada");
		}

		// A CENA INTERROMPIDA CONTINUA SENDO A DO SSJ1: e a mais longa das tres que piscam, ou seja a
		// que tem a maior chance de o jogador morrer com o cabelo errado na cabeca -- e o passeio abaixo
		// para de proposito no lado errado.
		FormaDef alvoDoSsj1 = Jandirus.Core.Forms.Catalogo.Def("ssj1")!;
		if (Jandirus.Core.Forms.Cinematicas.Para(alvoDoSsj1) is not { } cheiaDoSsj1) return;
		mundo.AoMudarForma(eu, alvoDoSsj1.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		vis.CabeloDaForma(alvoDoSsj1.SufixoDoCabelo);
		string cabeloDoSsj1 = vis.CabeloDeTeste;
		vis.CabeloDaForma("");
		string cabeloNormal = vis.CabeloDeTeste;

		// ============================ 5. A CENA MORTA NO MEIO DE UMA PISCADA DEVOLVE O CABELO ============================
		// Com o piscar durando um segundo e meio, isto era quase impossivel de acontecer; durando vinte,
		// e metade das mortes. E o resultado seria o jogador andando por ai com o cabelo de uma forma que
		// ele NAO assumiu -- a mesma familia do "relog careca" que este projeto ja pagou uma vez.
		//
		// O passeio para no primeiro quadro em que o cabelo E o do Super Saiyajin, que e exatamente o
		// lado errado pra morrer. Sem isso a linha seria verdadeira metade das vezes por sorteio.
		// ==========================================================================================================
		{
			mundo.AoMudarForma(eu, alvoDoSsj1.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			List<Transformacao> antes = Cenas();
			mundo.AoMudarForma(eu, raiz.IdRede, alvoDoSsj1.IdRede, Jandirus.Core.Forms.DegrauDeCena.Estreia);

			Transformacao? cena = null;
			foreach (Transformacao tr in Cenas()) if (!antes.Contains(tr)) cena = tr;
			if (cena != null)
			{
				cena.SetProcess(false);
				for (double t = 0; t < cheiaDoSsj1.Segundos && IsInstanceValid(cena); t += Passo)
				{
					cena._Process(Passo);
					if (IsInstanceValid(cena) && vis.CabeloDeTeste == cabeloDoSsj1) break;
				}
				Conferir(vis.CabeloDeTeste == cabeloDoSsj1,
						 "a cena foi interrompida com o cabelo do SSJ no corpo (o lado errado pra morrer)");

				atores.RemoveChild(cena);      // `_ExitTree` -> `Soltar`
				cena.QueueFree();
				Conferir(vis.CabeloDeTeste == cabeloNormal,
						 "e matar a cena no meio de uma piscada devolve o penteado normal "
					   + $"({vis.CabeloDeTeste.GetFile()})");
			}
		}

		// --- devolve o corpo pra a base pelo mesmo caminho ---
		mundo.AoMudarForma(eu, alvoDoSsj1.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.TickDoTremorDeTeste(10.0);   // nao deixa a camera tremendo pra os blocos seguintes
	}

	// =====================================================================
	// 3a-quater-bis. O PISCAR CEDE A ESCADA -- NA CENA DO SSJ3, AO VIVO
	// =====================================================================
	/// <summary>
	/// AS DUAS BANDEIRAS NA MESMA CENA, RODADAS DE VERDADE.
	///
	/// ============================ POR QUE ISTO PRECISA DE UM TESTE VIVO ============================
	/// O roteiro ja tranca a invariante: `Cinematicas_` cobra que nenhuma cena tenha `PiscaCabelo` e
	/// `VesteDegrau` juntos (hoje piscam `ssj1`/`future_ssj`/`primal_c_type` e veste degrau so o
	/// `ssj3`). Mas invariante de roteiro nao e prova de comportamento: ela diz que o caso nao existe
	/// HOJE, e o `VestirODegrauSeguinte` tem uma linha (`_piscaLigada = false`) que so serve pro dia
	/// em que ele existir. Uma linha que nada exercita e uma linha que ninguem percebe sumir.
	///
	/// Entao a cena com as duas bandeiras e MONTADA aqui -- a do SSJ3 mais o `PiscaCabelo` no beat que
	/// veste o primeiro degrau -- e rodada pelo tocador de producao (`Transformacao.Rodar`, o mesmo que
	/// o `World` chama). O que se afirma sao as duas metades da decisao:
	///
	///   1. **o piscar VIVE** entre o beat que o arma e o degrau seguinte (senao a medida e cega: um
	///      piscar que nunca ligou tambem "para" no primeiro degrau);
	///   2. **e MORRE ali**, ou seja dali pra frente todo cabelo que troca troca EM CIMA DE UM BEAT --
	///      que e a assinatura da escada, e nao a do sorteio de 0,3 a 1,0 s.
	///
	/// A segunda e a que pega o defeito de verdade. Sem a cessao, o piscar continuaria alternando
	/// entre o SSJ3 e o degrau recem-vestido: o jogador veria o SSJ1 da cena virar SSJ3 e voltar duas
	/// vezes por segundo, enquanto o personagem fala "este e o meu estado normal".
	///
	/// COMO REPROVA SE A REGRA SUMIR: apague o `_piscaLigada = false` do
	/// `Transformacao.VestirODegrauSeguinte`. A segunda checagem cai com o numero de trocas fora de
	/// beat, e o instante da primeira delas.
	/// ==========================================================================================
	/// </summary>
	private void OPiscarCedeAEscadaAoVivo(Node2D corpo, CharacterVisual vis)
	{
		FormaDef alvo = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		if (Jandirus.Core.Forms.Cinematicas.NoDegrau(alvo, Jandirus.Core.Forms.DegrauDeCena.Estreia)
			is not { } cheia) return;

		double[] degraus = [.. cheia.Beats
			.Where(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.VesteDegrau)).Select(b => b.Em)];
		if (degraus.Length < 2)
		{ Conferir(false, "a cena do SSJ3 tem pelo menos dois degraus pra o piscar ceder a"); return; }

		// ============================ A CENA HIBRIDA ============================
		// O `PiscaCabelo` entra no beat que veste o PRIMEIRO degrau, e nao no beat 0: e o pior caso e
		// o mais honesto ao mesmo tempo. Pior porque no `Disparar` o `VesteDegrau` roda ANTES do
		// `PiscaCabelo` -- entao a cessao e a bandeira brigam dentro do mesmo instante, e quem
		// escrever a ordem ao contrario ali dentro apaga o piscar antes de ele nascer (e a primeira
		// checagem cai). Mais honesto porque a janela ate o degrau seguinte e larga o bastante pra o
		// piscar dar trocas de sobra: o piso delas e derivado dela.
		//
		// O ROTEIRO ORIGINAL NAO E TOCADO: `Beat` e um record struct, o `with` faz copia. Escrever a
		// bandeira na cena de producao a envenenaria pra o resto da bancada -- inclusive pro
		// `Cinematicas_`, que roda antes e cobra que ela NAO exista.
		// ====================================================================
		Jandirus.Core.Forms.Beat[] beats = [.. cheia.Beats.Select(b =>
			b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.VesteDegrau) && Math.Abs(b.Em - degraus[0]) < 1e-9
				? b with { Faz = b.Faz | Jandirus.Core.Forms.Efeito.PiscaCabelo } : b)];
		var comAsDuas = new Jandirus.Core.Forms.Cinematica
		{
			Forma = cheia.Forma,
			Musica = "",          // a bancada nao toca tema por causa de uma medicao
			SegundosPreso = cheia.SegundosPreso,
			Beats = beats,
		};

		// --- os penteados que a cena pode mostrar, medidos antes de ela existir ---
		FormaDef[] escada = Jandirus.Core.Forms.Cinematicas.EscadaDaCena(alvo);
		vis.VestirCabeloDaForma(null);
		string cabeloBase = vis.CabeloDeTeste;
		vis.CabeloDaForma(alvo.SufixoDoCabelo);
		string cabeloDoSsj3 = vis.CabeloDeTeste;
		vis.CabeloDaForma("");
		Conferir(cabeloBase.Length > 0 && cabeloDoSsj3 != cabeloBase,
				 "os dois lados da piscada do SSJ3 sao arquivos diferentes -- senao a medicao e cega "
			   + $"({cabeloBase.GetFile()} / {cabeloDoSsj3.GetFile()})");
		if (cabeloDoSsj3 == cabeloBase) return;

		var t = Transformacao.Rodar(corpo.GetParent(), corpo, alvo, comAsDuas, souEu: false);
		t.SetProcess(false);   // o relogio e nosso, como no resto desta bancada
		try
		{
			const double Passo = 0.05;
			var trocas = new List<double>();
			string ultimo = vis.CabeloDeTeste;
			for (double x = 0; x < comAsDuas.Segundos + 1.0 && IsInstanceValid(t); x += Passo)
			{
				t._Process(Passo);
				if (!IsInstanceValid(t)) break;
				if (vis.CabeloDeTeste == ultimo) continue;
				ultimo = vis.CabeloDeTeste;
				trocas.Add(t.TempoDeTeste);
			}

			// --- 1. O PISCAR VIVEU (senao a linha 2 nao mede nada) ---
			double janela = degraus[1] - degraus[0];
			int minimo = (int)(janela / Jandirus.Core.Forms.Cinematicas.PiscadaMaxima) - 1;
			int noVao = trocas.Count(x => x > degraus[0] - Passo * 2 && x < degraus[1] - Passo * 2);
			Conferir(noVao >= minimo,
					 $"o piscar VIVE do beat que o arma ate o degrau seguinte ({noVao} trocas em "
				   + $"{janela:0.##}s, minimo {minimo})");

			// ============================ 2. E DALI PRA FRENTE SO A ESCADA ESCREVE ============================
			// Toda troca depois do segundo degrau tem que cair EM CIMA de um beat. A escada troca em
			// tres instantes conhecidos (os degraus e o `Assumir`); o sorteio do piscar cai em
			// qualquer lugar, e e por isso que "esta em cima de um beat" separa os dois sem precisar
			// contar quantas foram.
			// ============================================================================================
			double[] instantes = [.. comAsDuas.Beats.Select(b => b.Em)];
			var foraDeBeat = trocas
				.Where(x => x >= degraus[1] - Passo * 2)
				.Where(x => !instantes.Any(i => Math.Abs(i - x) <= Passo * 2))
				.ToList();
			Conferir(foraDeBeat.Count == 0,
					 $"e CEDE a escada: depois do 2o degrau toda troca cai num beat ({foraDeBeat.Count} "
				   + $"fora{(foraDeBeat.Count > 0 ? $", a primeira aos {foraDeBeat[0]:0.##}s" : "")})");

			// --- 3. E a cena termina no cabelo da forma, e nao num lado da piscada ---
			Conferir(vis.CabeloDeTeste == cabeloDoSsj3,
					 $"e a cena hibrida termina no cabelo do SSJ3 ({vis.CabeloDeTeste.GetFile()})");

			_passos.Add($"  --     piscar x escada: {noVao} trocas entre {degraus[0]:0.##}s e "
					  + $"{degraus[1]:0.##}s, {foraDeBeat.Count} fora de beat depois disso "
					  + $"({escada.Length} degraus)");
		}
		finally
		{
			// `Free` E NAO `QueueFree`: a bancada roda dentro de um quadro so, e a cena ficaria
			// segurando o corpo durante os blocos seguintes. Ver `AAparenciaInteiraDoDegrau`.
			if (IsInstanceValid(t)) t.Free();
		}
		vis.VestirCabeloDaForma(null);
	}

	// =====================================================================
	// 3a-quinquies. O BANHO DE COR CHEGA NO CORPO
	// =====================================================================
	/// <summary>
	/// O `animate(src, color=rgb(...))` DO DM, MEDIDO NO UNIFORM.
	///
	/// ============================ O QUE ELE IMPEDE, E SAO TRES COISAS ============================
	///   1. **O efeito escrito e nao desenhado.** O <see cref="Jandirus.Core.Forms.Efeito.BanhoDeCor"/>
	///      e uma bandeira nova; um `HasFlag` esquecido no `Disparar` daria uma cena que passa em todo
	///      teste de roteiro e nao lava nada. E como ele nao tem node proprio (ele reusa o canal do
	///      soco), nao ha "node que nao nasceu" pra ninguem notar.
	///   2. **O banho saindo com o gesto do soco.** O `achatar` era um literal dentro do
	///      `AplicarImpacto` e virou campo -- se alguem o recopiar pra la, o Legendary passa um segundo
	///      espremido ao virar, o que le como "levou um golpe no meio da transformacao".
	///   3. **A cor errada.** Ela e DERIVADA (`Aura.CorDaChamaDe`, a mesma que pinta a chama e a aura),
	///      e o modo de falha da derivacao e sair branca ou preta -- as duas passariam por "efeito
	///      existe" e nenhuma pelas duas linhas de baixo.
	///
	/// A FORMA E O `legendary` porque e a que o dossie manda portar com todas as letras (*"o port DEVE
	/// fazer o flash"*, `lssjbuff.dm:439`) e porque a cor dela e a mais distante do neutro (`2bff3a`) --
	/// um verde nao passa por engano em nenhuma das duas checagens de cor.
	/// ========================================================================================
	/// </summary>
	private void OBanhoDeCorChegaNoCorpo(Node2D corpo, CharacterVisual vis)
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo) return;
		int eu = GameClient.Instance?.LocalId ?? 0;
		if (eu == 0) return;

		FormaDef alvo = Jandirus.Core.Forms.Catalogo.Def("legendary")!;
		FormaDef raiz = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdBase)!;
		if (Jandirus.Core.Forms.Cinematicas.Para(alvo) is not { } cheia) return;

		double banhoEm = cheia.Beats
			.First(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.BanhoDeCor)).Em;
		// A COR PESSOAL SAI DO PROPRIO CORPO -- o `Legendary` nao a usa (a chama dele e a da forma),
		// mas passar a de outro corpo aqui esconderia o dia em que ele passasse a usar.
		Color esperada = Aura.CorDaChamaDe(
			alvo, corpo.GetNodeOrNull<Aura>("Aura")?.CorPessoal ?? Aura.CorDoKiCru);

		Node atores = corpo.GetParent();
		List<Transformacao> Cenas()
		{
			var l = new List<Transformacao>();
			foreach (Node n in atores.GetChildren()) if (n is Transformacao tr) l.Add(tr);
			return l;
		}

		mundo.AoMudarForma(eu, alvo.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		List<Transformacao> antes = Cenas();
		mundo.AoMudarForma(eu, raiz.IdRede, alvo.IdRede, Jandirus.Core.Forms.DegrauDeCena.Estreia);

		Transformacao? cena = null;
		foreach (Transformacao tr in Cenas()) if (!antes.Contains(tr)) cena = tr;
		Conferir(cena != null, "a estreia do Legendary nasce pra o banho de cor rodar");
		if (cena == null) return;
		cena.SetProcess(false);

		const double Passo = 0.05;

		// ============================ O RELOGIO DA LAVAGEM E DO `CharacterVisual`, E ELE E NOSSO TAMBEM ============================
		// O `flash` decai no `_Process` do BONECO, e nao no da cena -- e a bancada inteira acontece dentro
		// de UM quadro, entao o `_Process` dele nao roda sozinho aqui. Sem esta funcao, "o corpo escoa"
		// mediria um uniform congelado no pico e reprovaria o codigo certo (foi o que aconteceu na
		// primeira rodada: 0,92 antes do beat E 0,92 depois, com o banho funcionando).
		//
		// E o PRE-ESCOAMENTO abaixo e a mesma higiene do `TickDoTremorDeTeste(10.0)` que os blocos
		// vizinhos ja fazem: outros passeios desta bancada rodam cenas com banho e deixam o uniform
		// aceso, porque ninguem o fez decair. Medir sem limpar seria medir o vizinho.
		// ================================================================================================================
		void Quadro(double dt)
		{
			if (IsInstanceValid(cena)) cena._Process(dt);
			vis._Process(dt);
		}

		for (int i = 0; i < 40; i++) vis._Process(Passo);   // 2,0 s > `SegundosDoBanho`: apaga a sobra

		// --- ANTES DO BEAT, O CORPO NAO ESTA LAVADO (senao a linha seguinte mede o vizinho) ---
		for (double t = 0; t < banhoEm - Passo * 2 && IsInstanceValid(cena); t += Passo) Quadro(Passo);
		Conferir(vis.LavagemDeTeste is { Mistura: <= 0.01f },
				 $"antes do beat o corpo NAO esta lavado ({vis.LavagemDeTeste?.Mistura ?? -1:0.###})");

		// --- O BEAT DISPARA: mistura acesa, cor da forma, e ZERO achatamento ---
		while (IsInstanceValid(cena) && cena.TempoDeTeste < banhoEm + Passo) Quadro(Passo);

		(float Mistura, Color Cor, float Achatar)? lav = vis.LavagemDeTeste;
		Conferir(lav is { Mistura: > 0.5f },
				 $"o beat BANHA o corpo (mistura {lav?.Mistura ?? -1:0.##})");
		Color pintou = lav?.Cor ?? Colors.Black;
		Conferir(Math.Abs(pintou.R - esperada.R) < 0.01f
			  && Math.Abs(pintou.G - esperada.G) < 0.01f
			  && Math.Abs(pintou.B - esperada.B) < 0.01f,
				 $"e a cor e a da FORMA (#{pintou.ToHtml(false)} contra #{esperada.ToHtml(false)})");
		Conferir(lav is { Achatar: <= 0.001f },
				 $"e o corpo NAO e achatado -- banho nao e soco ({lav?.Achatar ?? -1:0.###})");

		// ============================ E ELE ESCOA NO PRAZO DO `color = null` ============================
		// O prazo e PERGUNTADO (`Cinematicas.SegundosDoBanho`) e nao escrito aqui: um `1.0` recopiado
		// mediria outra coisa no dia em que o `spawn(12)` do DM virasse outro numero. A folga de dois
		// quadros e do laco.
		// ==========================================================================================
		double alvoDoEscoamento = banhoEm + Jandirus.Core.Forms.Cinematicas.SegundosDoBanho + Passo * 2;
		while (IsInstanceValid(cena) && cena.TempoDeTeste < alvoDoEscoamento) Quadro(Passo);
		Conferir(vis.LavagemDeTeste is { Mistura: <= 0.01f },
				 $"e ele escoa em {Jandirus.Core.Forms.Cinematicas.SegundosDoBanho:0.#}s "
			   + $"(sobrou {vis.LavagemDeTeste?.Mistura ?? -1:0.###})");

		if (IsInstanceValid(cena)) { atores.RemoveChild(cena); cena.QueueFree(); }
		mundo.AoMudarForma(eu, alvo.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.TickDoTremorDeTeste(10.0);
		_passos.Add($"  --     banho de cor: `legendary` aos {banhoEm:0.#}s em "
				  + $"#{esperada.ToHtml(false)}, escoa em {Jandirus.Core.Forms.Cinematicas.SegundosDoBanho:0.#}s");
	}

	// =====================================================================
	// 3a-quater. O MACACO NAO SEGUE OS DEGRAUS E NAO LEVANTA PEDRA
	// =====================================================================
	/// <summary>
	/// AS DUAS EXCECOES DO OOZARU, as duas do dono, e nenhuma delas visivel em jogo quando quebra.
	///
	/// ============================ 1. ELE NAO SEGUE OS DEGRAUS ============================
	/// *"isso serve pra TODAS as formas do jogo, menos as oozaru e golden oozaru"*. E a cena do macaco
	/// nao e comemoracao de estreia: ela E a transformacao -- sem ela um lutador de 32 px vira um bicho
	/// de 96 px em um quadro, que le como sprite errado e nao como transformacao.
	///
	/// A prova nao e ler o `if`: e chamar o caminho do macaco com `primeira: false` (que em qualquer
	/// outra forma daria a encurtada, ou nada) e conferir que nasce a cena CHEIA.
	///
	/// ============================ 2. ELE NAO LEVANTA PEDRA ============================
	/// *"oozaru n tem esse efeito de rocks nem de particulas, o resto da cinematica do oozaru pode
	/// deixar"*. Pedra a mais nao da erro, nao trava ninguem e ainda parece efeito -- e por isso ela
	/// so sai daqui por reprova.
	///
	/// E O ZERO NAO PODE SER VAZIO: uma folha que nao carregasse daria zero pedras em toda cena do
	/// jogo, e esta checagem passaria verde com o efeito inteiro morto. Por isso, no fim, a leva e
	/// disparada NA MARRA no mesmo node -- se ela nascer, o zero de cima e uma escolha do roteiro e
	/// nao uma pane. (O contra-teste largo esta no laco das 68 cenas: la, cena COM o beat que nao
	/// levanta pedra tambem reprova.)
	/// ==============================================================================
	/// </summary>
	private void AFeraForaDosDegraus(Node2D corpo, CharacterVisual vis)
	{
		// --- 1. OS DADOS: a cena nao solta o chao, nem vira degrau em maestria nenhuma ---
		var doMacaco = Jandirus.Core.Forms.Cinematicas.Oozaru;
		foreach (string id in new[] { "oozaru", "oozaru_dourado" })
		{
			FormaDef d = Jandirus.Core.Forms.Catalogo.Def(id)!;
			var c = Jandirus.Core.Forms.Cinematicas.Para(d)!;

			// A PERGUNTA E A CENA, e nao a contagem de um bit: a pedra deixou de ser um beat e virou o
			// estado `OChaoSeSolta`, derivado do catalogo. Contar beats aqui ficaria VERDE PRA SEMPRE
			// depois da mudanca -- nenhuma cena tem beat de pedra hoje --, medindo nada.
			Conferir(!c.OChaoSeSolta, $"`{id}`: a cena dele NAO solta o chao");

			// A FAIXA INTEIRA DE MAESTRIA, e nao so os 100%. O limiar mora em `Degrau`, e um `if` da
			// linha do macaco posto DEPOIS do teste de maestria (em vez de antes) so falharia acima do
			// corte -- exatamente o pedaco que uma checagem de 100% nao distingue.
			foreach (double m in new[] { 0.0, 49.9, 50.0, 100.0 })
				Conferir(Jandirus.Core.Forms.Cinematicas.Degrau(d, estreia: false, m)
						 == Jandirus.Core.Forms.DegrauDeCena.Estreia,
						 $"`{id}`: com {m:0.#}% de maestria a cena continua CHEIA");
		}

		// --- 2. AO VIVO, pelo caminho proprio do macaco ---
		if (GetTree().Root.FindChild("World", true, false) is not World mundo) return;
		int eu = GameClient.Instance?.LocalId ?? 0;
		if (eu == 0) return;

		Node atores = corpo.GetParent();
		List<Transformacao> Cenas()
		{
			var l = new List<Transformacao>();
			foreach (Node n in atores.GetChildren()) if (n is Transformacao tr) l.Add(tr);
			return l;
		}

		foreach ((Jandirus.Core.Forms.FormaOozaru f, string id) in new[]
		{
			(Jandirus.Core.Forms.FormaOozaru.Regular, "oozaru"),
			(Jandirus.Core.Forms.FormaOozaru.Dourado, "oozaru_dourado"),
		})
		{
			List<Transformacao> antes = Cenas();
			// `primeira: false` E O TESTE. Em qualquer outra forma este argumento tiraria a cena cheia.
			//
			// O DEGRAU E PERGUNTADO, E NAO ESCRITO. Cravar `DegrauDeCena.Estreia` aqui seria entregar a
			// resposta pro exame: a regra sob teste e "a linha do macaco ignora maestria", e quem a
			// aplica e o `Cinematicas.Degrau` -- o mesmo que o servidor consulta no `AnunciarOozaru`.
			// Perguntando, o caminho medido e o do jogo inteiro, do Core ate a `Transformacao` nascer.
			// (Os 50% sao de proposito: e o corte que dispensaria a cena em QUALQUER outra forma.)
			var degrauDaFera = Jandirus.Core.Forms.Cinematicas.Degrau(
				Jandirus.Core.Forms.Catalogo.Def(id), estreia: false, 50);
			mundo.AoVirarOozaru(eu, f, primeira: false, degrauDaFera);

			Transformacao? nova = null;
			foreach (Transformacao tr in Cenas()) if (!antes.Contains(tr)) nova = tr;
			Conferir(nova != null, $"`{id}`: a cena roda mesmo NAO sendo a estreia");
			if (nova == null) continue;
			nova.SetProcess(false);

			Conferir(ReferenceEquals(nova.CenaDeTeste, doMacaco), $"`{id}`: e e a cena do macaco");
			Conferir(!ReferenceEquals(nova.CenaDeTeste,
									  Jandirus.Core.Forms.Cinematicas.Encurtada(doMacaco))
				  && Mathf.IsEqualApprox((float)nova.CenaDeTeste.SegundosPreso, (float)doMacaco.SegundosPreso),
					 $"`{id}`: a CHEIA, e nao a encurtada (prende {nova.CenaDeTeste.SegundosPreso:0.##}s)");

			int pico = 0;
			for (double k = 0; k < doMacaco.Segundos + 1 && IsInstanceValid(nova); k += 0.1)
			{
				nova._Process(0.1);
				pico = Math.Max(pico, nova.PedrasVivasDeTeste);
			}
			Conferir(pico == 0, $"`{id}`: a cena inteira roda sem UMA pedra ({pico})");

			// O ZERO ACIMA E ESCOLHA, E NAO PANE -- ver o cabecalho.
			if (IsInstanceValid(nova))
			{
				nova.SoltarPedrasDeTeste();
				Conferir(nova.PedrasVivasDeTeste > 0,
						 $"`{id}`: e o maquinario de pedra desta cena FUNCIONA ({nova.PedrasVivasDeTeste} "
					   + "na marra) -- o zero acima e o gate da cena, nao a folha faltando");
				atores.RemoveChild(nova);
				nova.QueueFree();
			}
			vis.CorpoDaForma(CorpoDeForma.Nenhum);
		}

		// --- devolve o boneco ---
		// `Nenhuma` porque desfazer a fera nunca teve cena, e e o que o servidor manda: o `Degrau` de um
		// def nulo (que e o que `FormaOozaru.Nao` vira) e exatamente este.
		mundo.AoVirarOozaru(eu, Jandirus.Core.Forms.FormaOozaru.Nao, primeira: false,
							Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(!vis.EhCriatura, "sair do macaco pelo caminho dele devolve o corpo de gente");
	}

	// =====================================================================
	// 3a-quinquies. QUEM CHEGA NA ZONA -- a forma dos outros, sem a cena dos outros
	// =====================================================================
	/// <summary>
	/// O LADO DO CLIENTE DA SINCRONIA DE ENTRADA DE ZONA.
	///
	/// ============================ O REMEDIO QUE PODIA VIRAR DOENCA ============================
	/// `S2C.Forma` e `S2C.Oozaru` sao pacotes de ACONTECIMENTO -- saem uma vez, pra quem estava na zona
	/// naquele instante. Quem entrava depois nunca soube, e via um Super Saiyajin 3 desenhado como
	/// lutador comum. A sincronia conserta isso reusando os MESMOS pacotes como estado, e por isso ela
	/// carrega um risco proprio e maior que o defeito: mandar o pacote de sempre faria o recem-chegado
	/// ficar PRESO assistindo a estreia de um desconhecido -- ou, no Oozaru, uma transformacao de dez
	/// metros que aconteceu antes de ele pisar no planeta.
	///
	/// Sao TRES coisas diferentes que nao podem acontecer, e a bancada mede as tres separadas porque
	/// elas quebram separadas: a CENA (o corpo preso), o SOM (o estalo de um instante que nao houve) e
	/// o CHAT (o carimbo de estreia alheia).
	///
	/// E uma quarta que so aparece na ordem dos pacotes: a sincronia chega quando os bonecos remotos
	/// AINDA NAO EXISTEM -- quem os cria e o snapshot, que vem por outro canal. Toda a regra morre a um
	/// metro da tela se o registro da memoria (`_formaDaZona`/`_feraDaZona`) ficar depois do `return` do
	/// corpo nulo. Isso nao se ve chamando `AoMudarForma` num corpo que existe: e preciso fazer o corpo
	/// NASCER depois, pelo caminho que o faz nascer em jogo.
	/// ======================================================================================
	/// </summary>
	private void AChegadaNaZona(Node2D corpo, CharacterVisual vis)
	{
		if (GetTree().Root.FindChild("World", true, false) is not World mundo)
		{
			Conferir(false, "achei o `World` (sem ele a chegada na zona nao se mede)");
			return;
		}
		int eu = GameClient.Instance?.LocalId ?? 0;
		if (eu == 0) { Conferir(false, "a bancada tem id de jogador"); return; }

		Node atores = corpo.GetParent();
		FormaDef raiz = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdBase)!;
		FormaDef ssj3 = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		FormaDef ssj4 = Jandirus.Core.Forms.Catalogo.Def("ssj4")!;

		List<Transformacao> Cenas()
		{
			var l = new List<Transformacao>();
			foreach (Node n in atores.GetChildren()) if (n is Transformacao tr) l.Add(tr);
			return l;
		}

		// O SOM E CONTADO NO PROPRIO CORPO. `AudioDirector.EfeitoNoLugar` pendura um
		// `AudioStreamPlayer2D` no node que recebe o efeito -- entao contar filhos e olhar o caminho de
		// producao, sem um contador novo pra alguem esquecer de incrementar.
		int Estalos()
		{
			int n = 0;
			foreach (Node c in corpo.GetChildren()) if (c is AudioStreamPlayer2D) n++;
			return n;
		}

		// --- 1. O PACOTE DE ESTADO NAO TOCA CENA, E VESTE O BONECO --------------------
		// ============================ AS DUAS METADES SAO UMA SO CHECAGEM ============================
		// "Nao nasceu cena" sozinho passaria com um `return` colocado cedo demais no `AoMudarForma` -- e
		// ai o recem-chegado nao veria cena NEM forma, que e exatamente o defeito original de volta.
		// Perguntar pelo penteado no mesmo quadro e o que separa "chegou pronto" de "nao chegou".
		//
		// COMO REPROVA SE A REGRA SUMIR: tire a bifurcacao `Cinematicas.NoDegrau(...)` do `AoMudarForma`
		// (ou mande a sincronia com qualquer degrau que nao seja `Nenhuma`) e a primeira linha cai; tire
		// o `VestirAFormaSemCena` de baixo dela e cai a segunda.
		// ========================================================================================
		mundo.AoMudarForma(eu, ssj3.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		vis.CabeloDaForma("");
		string cabeloBase = vis.CabeloDeTeste;

		List<Transformacao> antesDoEstado = Cenas();
		int estalosAntes = Estalos();
		mundo.AoMudarForma(eu, ssj3.IdRede, ssj3.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);

		Conferir(Cenas().Count == antesDoEstado.Count,
				 $"chegar numa zona com um SSJ3 NAO faz nascer cinematica ({Cenas().Count - antesDoEstado.Count} nasceu)");
		Conferir(vis.CabeloDeTeste != cabeloBase,
				 $"...mas o boneco ja aparece transformado (penteado `{vis.CabeloDeTeste.GetFile()}`)");

		// ============================ O ESTALO E DE UM INSTANTE QUE NAO HOUVE ============================
		// O caminho instantaneo toca um `Trilha.Dash` no fim -- o estalo que marca a transformacao. Chegar
		// num planeta com tres transformados dispararia tres estalos de coisas que aconteceram antes de eu
		// existir ali: a mesma mentira da cinematica, so que no ouvido.
		//
		// COMO REPROVA SE A REGRA SUMIR: tire o `if (de != para)` do fim do `AoMudarForma` e esta linha
		// cai. E A LINHA DE BAIXO E O QUE A IMPEDE DE SER VAZIA: sem ela, "nao tocou" nao distinguiria a
		// regra de um som que nunca toca (um caminho errado, um `.mp3` faltando -- ja aconteceu aqui).
		// ============================================================================================
		Conferir(Estalos() == estalosAntes,
				 $"...e NENHUM estalo toca (era o som de uma transformacao que nao houve) ({Estalos() - estalosAntes})");

		mundo.AoMudarForma(eu, raiz.IdRede, ssj3.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(Estalos() > estalosAntes,
				 $"...e o estalo TOCA numa transformacao de verdade (`base -> ssj3`) ({Estalos() - estalosAntes})");

		// --- 2. A FERA QUE JA ESTAVA LA ---------------------------------------------
		// ============================ ESTE ERA O BURACO DE VERDADE ============================
		// A cena do macaco toca TODA VEZ que a fera nasce (ela E a transformacao, nao a comemoracao da
		// estreia) -- e o `AoVirarOozaru` chamava `Transformacao.Rodar` com a cena CRAVADA, sempre. Um
		// recem-chegado ficaria preso assistindo um macaco de dez metros se transformando, pela cena
		// inteira, sem andar.
		//
		// A SEGUNDA LINHA E A QUE IMPEDE O CONSERTO DE VIRAR OUTRO DEFEITO: sem cena, o corpo do macaco
		// tem que aparecer do mesmo jeito. Um `return` cedo daria um Oozaru desenhado como lutador de
		// 32 px -- e `EhCriatura` e derivado do tamanho do quadro, entao ela responde pelo desenho e nao
		// por um campo que alguem escreveu.
		//
		// COMO REPROVA SE A REGRA SUMIR: apague o `if (NoDegrau(...) is not { } cena)` do `AoVirarOozaru`
		// e a primeira cai; troque o `VestirAFormaSemCena(corpo, def)` dele por um `return` seco e cai a
		// segunda.
		// ==================================================================================
		mundo.AoMudarForma(eu, ssj3.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		List<Transformacao> antesDaFera = Cenas();
		mundo.AoVirarOozaru(eu, Jandirus.Core.Forms.FormaOozaru.Regular, primeira: false,
							Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(Cenas().Count == antesDaFera.Count,
				 $"chegar numa zona onde alguem JA e macaco nao roda a cinematica dele "
			   + $"({Cenas().Count - antesDaFera.Count} nasceu)");
		Conferir(vis.EhCriatura, "...mas o macaco esta la, de corpo inteiro (96 px, nao um lutador de 32)");

		mundo.AoVirarOozaru(eu, Jandirus.Core.Forms.FormaOozaru.Nao, primeira: false,
							Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(!vis.EhCriatura, "...e sair do macaco pelo mesmo caminho devolve o corpo de gente");

		// --- 3. O PACOTE QUE CHEGA ANTES DO CORPO EXISTIR ---------------------------
		// ============================ A ARMADILHA QUE SO A ORDEM REVELA ============================
		// Quem cria os `RemotePlayer` e o SNAPSHOT, que vem por outro canal. No instante em que a
		// sincronia chega, nenhum daqueles bonecos existe -- `AoMudarForma` sairia calado no
		// `Corpo(id) == null` e a regra inteira morreria a um metro da tela. Por isso a memoria
		// (`_formaDaZona`/`_feraDaZona`) e escrita ANTES do `return`, e consumida no nascimento
		// (`VestirCorpoInteiro`).
		//
		// Isto NAO se prova chamando `VestirCorpoInteiro` na mao: seria provar que a funcao funciona sem
		// provar que alguem a chama. O corpo aqui nasce pelo `AoReceberSnapshot`, que e o metodo que o
		// faz nascer em jogo.
		//
		// COMO REPROVA SE A REGRA SUMIR: mova o `_formaDaZona[id] = para` pra depois do `return` do corpo
		// nulo (a arrumacao "obvia" pra quem le o codigo sem contexto) e a ESCADA cai; faca o mesmo com o
		// `_feraDaZona[id] = forma` e cai a FERA. Em jogo, os dois defeitos sao o boneco nascendo careca.
		//
		// ============================ SAO DOIS FANTASMAS, E NAO UM ============================
		// A primeira versao deste bloco usava um corpo so, com escada E fera. Ela ficou VERDE com o
		// `_formaDaZona` mutado de proposito -- porque o macaco tambem poe camada de corpo, e a checagem
		// "nasceu vestido" nao sabia distinguir de quem era aquela camada. Uma memoria podia estar morta
		// e a outra respondia por ela.
		//
		// Agora cada fantasma carrega UMA memoria: o A so tem a escada (a pelagem do SSJ4, quadro de 32 --
		// entao `EhCriatura` e FALSO nele), o B tem as duas (e o macaco de 96 cobre a pelagem, que e a
		// ordem em que os dois pacotes chegam do servidor). As duas checagens caem separadas agora.
		// ==================================================================================
		int fantasmaA = eu + 90_000;   // so a escada
		int fantasmaB = eu + 90_001;   // escada E fera
		mundo.AoMudarForma(fantasmaA, ssj4.IdRede, ssj4.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.AoMudarForma(fantasmaB, ssj4.IdRede, ssj4.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.AoVirarOozaru(fantasmaB, Jandirus.Core.Forms.FormaOozaru.Regular, primeira: false,
							Jandirus.Core.Forms.DegrauDeCena.Nenhuma);

		var nascidos = new List<RemotePlayer>();
		CharacterVisual? Nascer(int id)
		{
			List<RemotePlayer> antes = [];
			foreach (Node n in atores.GetChildren()) if (n is RemotePlayer r0) antes.Add(r0);

			mundo.AoReceberSnapshot([new Jandirus.Net.EntityState
			{
				Id = id,
				Pos = new Jandirus.Core.World.Vec2(corpo.GlobalPosition.X + 64, corpo.GlobalPosition.Y),
			}]);

			foreach (Node n in atores.GetChildren())
				if (n is RemotePlayer r && !antes.Contains(r))
				{
					nascidos.Add(r);
					return r.GetNodeOrNull<CharacterVisual>("Visual");
				}
			return null;
		}

		CharacterVisual? soEscada = Nascer(fantasmaA);
		Conferir(soEscada != null, "o snapshot faz nascer o corpo de quem a sincronia ja tinha descrito");
		Conferir(soEscada is { CorpoDaFormaDeTeste: true },
				 "...e ele NASCE em SSJ4 (o pacote da ESCADA chegou antes do boneco e nao se perdeu)");
		Conferir(soEscada is { EhCriatura: false },
				 "...e so isso: sem fera no pacote, sem macaco no boneco");

		CharacterVisual? comFera = Nascer(fantasmaB);
		Conferir(comFera is { EhCriatura: true },
				 "...e quem tinha as duas nasce MACACO -- a fera por cima da escada, na ordem dos pacotes");

		// --- 4. E A MEMORIA VELHA NUNCA FICA SEM SER CONTESTADA ---------------------
		// ============================ POR QUE ELA TEM QUE MORRER ============================
		// O servidor CALA sobre quem esta na forma base ("so sai pacote de quem tem o que dizer").
		// Guardando o estado de quem saiu do meu campo de visao, um Super Saiyajin que voltou ao normal
		// enquanto eu estava em Namek renasceria dourado na minha tela quando eu voltasse -- e nada
		// jamais contestaria aquela lembranca.
		//
		// AQUI A REGRA DIVERGE DO `_looks` DE PROPOSITO: a aparencia de ficha fica (ela e permanente e
		// chega uma vez por sessao), a forma nao (ela e volatil e o servidor a reafirma na entrada).
		//
		// OS DOIS FANTASMAS DE NOVO, pelo mesmo motivo de cima: `_formaDaZona.Remove` e
		// `_feraDaZona.Remove` sao duas linhas, e uma sozinha ja daria o defeito.
		//
		// COMO REPROVA SE A REGRA SUMIR: tire o `_formaDaZona.Remove(id)` do `AoSair` e o A renasce em
		// SSJ4; tire o `_feraDaZona.Remove(id)` e o B renasce macaco.
		// ================================================================================
		mundo.AoSair(fantasmaA);
		mundo.AoSair(fantasmaB);

		CharacterVisual? escadaDeVolta = Nascer(fantasmaA);
		Conferir(escadaDeVolta != null, "o corpo renasce depois de o dono sair de vista");
		Conferir(escadaDeVolta is { CorpoDaFormaDeTeste: false },
				 "...e renasce na forma BASE: a memoria da ESCADA morre com quem saiu de vista");

		CharacterVisual? feraDeVolta = Nascer(fantasmaB);
		Conferir(feraDeVolta is { EhCriatura: false },
				 "...e a da FERA tambem (senao um lutador comum voltaria macaco)");

		// --- 5. A FICHA QUE CHEGA DEPOIS DO CORPO -----------------------------------
		// ============================ A OUTRA METADE DA MESMA CORRIDA ============================
		// O bloco 3 cobre "o pacote de forma chegou antes do boneco". Esta e a ordem INVERSA, e ela
		// ficou sem prova por dois anos: o `PeerLook` (canal CONFIAVEL, `GameServer.TrocarAparencias`)
		// chegando num corpo que o SNAPSHOT (canal NAO-confiavel) ja fez nascer. Nao ha ordem garantida
		// entre canais diferentes -- as duas ordens acontecem em jogo.
		//
		// O `AoReceberAparencia` chamava `CharacterVisual.Vestir` sozinho, e `Vestir` REMONTA as camadas:
		// ele reescreve o penteado base, refaz o rabo e retinge o olho com a cor da FICHA. Ou seja o
		// caminho da aparencia DESPIA quem estava transformado -- foi o "as transformacoes nao estao
		// sincronizando com quem acabou de entrar no server" que o dono viu.
		//
		// ============================ POR QUE A MEDIDA E O OLHO ============================
		// `CorpoDaFormaDeTeste` NAO serve aqui, e essa e a armadilha do bloco: `Vestir` REVESTE o corpo
		// da forma de proposito (`CharacterVisual.cs:716`, pra o musculo acompanhar a troca de tom de
		// pele), entao a pelagem do SSJ4 sobreviveria ao defeito e a checagem passaria com o jogo
		// quebrado. O que `Vestir` destroi mesmo e a tinta que a FORMA armou -- ele faz
		// `_tintaDoOlhoGuardada = false` e um `Tingir(_olhos, ap.CorOlho)` por cima. Por isso a regua e
		// o `TintaDoOlhoDeTeste`, LIDO DO MATERIAL e nao de um campo.
		//
		// E A CAMADA DE OLHO NASCE DO PROPRIO PeerLook: um corpo remoto sem ficha nao tem olho nenhum
		// (a camada e criada no `Vestir`). Ou seja a ordem deste bloco -- forma, corpo, DEPOIS ficha --
		// e a mesma que produz o defeito em jogo, e nao um arranjo pra medir mais facil.
		//
		// COMO REPROVA SE A REGRA SUMIR: troque o `VestirCorpoInteiro` do `AoReceberAparencia` de volta
		// por um `CharacterVisual.Vestir` direto -- a tinta do olho sai a da FICHA (zero, nesta ficha
		// vazia) em vez da do SSJ3, e a ultima linha cai.
		// ==============================================================================
		int fantasmaC = eu + 90_002;
		mundo.AoMudarForma(fantasmaC, ssj3.IdRede, ssj3.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		CharacterVisual? tarde = Nascer(fantasmaC);
		Conferir(tarde != null, "o corpo do fantasma C nasceu (a forma dele chegou antes, como no bloco 3)");

		// A COR ESPERADA SAI DO CORE e nao de um hexa escrito aqui: `Catalogo.CorDoOlho` e a mesma
		// funcao que o `VestirCabeloDaForma` consulta, entao mexer na tabela do dono nao volta a exigir
		// edicao nesta linha. E ela precisa ser DIFERENTE de zero, senao "a ficha nao despiu" e "a ficha
		// despiu" dariam a mesma leitura -- a medida seria vazia.
		Vector3 olhoDoSsj3 = Jandirus.Core.Forms.Catalogo.CorDoOlho(ssj3) is { } hexaOlho
			? new Vector3(new Color(hexaOlho).R, new Color(hexaOlho).G, new Color(hexaOlho).B)
			: Vector3.Zero;
		Conferir(!olhoDoSsj3.IsZeroApprox(),
				 $"o SSJ3 pinta o olho com uma cor que da pra distinguir de \"sem tinta\" ({olhoDoSsj3})");

		mundo.AoReceberAparencia(fantasmaC, "FantasmaC", "Saiyan", "Male",
								 new Jandirus.Core.Appearance.Appearance());
		Conferir(tarde?.TintaDoOlhoDeTeste is { } depois && depois.IsEqualApprox(olhoDoSsj3),
				 $"...e a ficha que chega DEPOIS nao despe a forma: o olho continua o do SSJ3 "
			   + $"(esperado {olhoDoSsj3}, medido {tarde?.TintaDoOlhoDeTeste})");

		// --- 6. O CONTORNO DO CORPO ALHEIO SEGUE O KI DELE, E NAO A FORMA -----------
		// ============================ "QUEM SE TRANSFORMA FICA SEMPRE COM A OUTLINE" ============================
		// Regra do dono, e ela vale nos DOIS corpos: Ki <= 100% sem contorno, > 100% com contorno --
		// quem brilha e a AURA. No corpo do dono da tela isso ja era medido (ver `OContornoQueVoltou`);
		// no corpo ALHEIO o contorno era da FORMA, aceso desde o instante da transformacao e apagado so
		// ao reverter, com a conta velha `0,35 + Intensidade * 0,13`.
		//
		// O bit que decide ja viajava (`EntityState.Sobrecarregado`) e ja era lido no
		// `World.AoReceberSnapshot` -- so que entregue exclusivamente a `CargaVisual`.
		//
		// PELO SNAPSHOT E NAO POR UM SETTER: e o canal que traz o bit em jogo. Escrever `_sobrecarregados`
		// na mao provaria o conjunto, nao o cano.
		//
		// COMO REPROVA SE A REGRA SUMIR: tire o `MarcarSobrecarga(e.Id, e.Sobrecarregado)` do
		// `AoReceberSnapshot` e a segunda linha cai; devolva o `vis.AuraDaForma(...)` da forma ao
		// `AcenderFormaNoCorpo` e cai a primeira.
		// ==================================================================================================
		void SnapshotDoC(bool sobrecarga) => mundo.AoReceberSnapshot([new Jandirus.Net.EntityState
		{
			Id = fantasmaC,
			Pos = new Jandirus.Core.World.Vec2(corpo.GlobalPosition.X + 64, corpo.GlobalPosition.Y),
			Sobrecarregado = sobrecarga,
		}]);

		float ContornoDoC() => tarde?.ContornoNoMaterialDeTeste().Forca ?? -1f;

		SnapshotDoC(false);
		Conferir(Mathf.IsZeroApprox(ContornoDoC()),
				 $"um SSJ3 ALHEIO com o Ki normal NAO tem contorno (era a queixa: `{ContornoDoC():0.##}`)");

		SnapshotDoC(true);
		float topo = Jandirus.Core.Forms.Catalogo.ForcaDoContorno;
		float piso = topo * (float)Jandirus.Core.Forms.Catalogo.PisoDoPulsoDoContorno;
		Conferir(ContornoDoC() >= piso - 0.01f && ContornoDoC() <= topo + 0.01f,
				 $"...e passar dos 100% acende, na MESMA forca do meu corpo ({ContornoDoC():0.##}, "
			   + $"faixa {piso:0.##}..{topo:0.##}) -- nao na conta velha da Intensidade");

		Conferir(tarde != null
				 && tarde.ContornoNoMaterialDeTeste().Cor.IsEqualApprox(
						new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(ssj3))),
				 "...e a COR continua sendo a da forma (o Ki manda no acender, a forma na cor)");

		SnapshotDoC(false);
		Conferir(Mathf.IsZeroApprox(ContornoDoC()),
				 $"...e o Ki dele voltando ao normal APAGA, sem ele sair da forma ({ContornoDoC():0.##})");

		// --- faxina: os fantasmas nao vao pra a foto ---
		mundo.AoSair(fantasmaA);
		mundo.AoSair(fantasmaB);
		mundo.AoSair(fantasmaC);
		foreach (RemotePlayer f in nascidos)
			if (IsInstanceValid(f)) { atores.RemoveChild(f); f.QueueFree(); }

		// e o boneco do dono volta pra base, pro resto da bancada e pra a foto
		mundo.AoMudarForma(eu, ssj3.IdRede, raiz.IdRede, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
	}

	// =====================================================================
	// 2z. A CENA DA FURIA, AO VIVO -- `AngerCinematic()`, `Murder.dm:136`
	// =====================================================================
	/// <summary>
	/// ============================ O QUE SO A EXECUCAO PROVA ============================
	/// O roteiro dela (os tempos, a cratera no beat da virada, a pedra do inicio ao fim, o `SegundosPreso
	/// = 0`) e dado do Core e a bancada `raiva` [10] ja o mede. Aqui se mede o que o dado NAO diz:
	///
	///   1. **O CORPO NAO PARA -- NEM POR UM QUADRO.** Ela e a unica cena do jogo que nao prende, e o
	///      caminho pra isso e uma condicao no `_Ready` do tocador. Um `SegundosPreso = 0` com o
	///      `Prender()` incondicional passaria em TODA checagem de dado e travaria o jogador por um
	///      quadro no meio de uma briga -- e o contador da tranca e `static`, ou seja o vazamento
	///      sobreviveria a cena.
	///   2. **ELA NAO DESPE NINGUEM.** E o risco de verdade da cena sem forma: o beat que vira chama
	///      `Assumir()`, e `Vestir(null)` desfaz cabelo, tinta e rabo. Entao o boneco entra aqui em
	///      SSJ3 e tem que sair em SSJ3 -- se esta checagem cair, quem perder um amigo em SSJ3 volta
	///      ao normal sozinho.
	///   3. **ELA TERMINA.** Roda os 8 beats, morre `Sozinha` e nao encosta no teto.
	///   4. **A CHAMA E VERMELHA.** Sem forma, `Aura.CorDaChamaDe(null)` daria o branco do ki cru; o DM
	///      pinta a `Aurabigcombined` de `#ff2a2a` (`Murder.dm:149`). A cor e lida do MATERIAL, e nao do
	///      campo que a pediu -- mesma regra do resto deste arquivo.
	///   5. **A PEDRA APARECE.** `OChaoSeSolta` e verdadeiro pra ela por derivacao (forma nula ->
	///      `NaoSeSobePraEla` falso), e "por derivacao" e exatamente o tipo de verdade que merece uma
	///      medida: um `Def(null)` que um dia devolvesse a base mataria o efeito sem erro nenhum.
	///   6. **A `PoeiraDeEstrago` NAO E CHAMADA.** A quarta das regras de cena do dono, medida no
	///      contador do proprio sistema de estrago.
	/// ==============================================================================
	/// </summary>
	private void ACenaDaFuriaAoVivo(Node2D corpo, CharacterVisual vis)
	{
		Jandirus.Core.Forms.Cinematica furia = Jandirus.Core.Forms.Cinematicas.Furia;

		// O BONECO ENTRA TRANSFORMADO -- ver o ponto 2 do cabecalho. SSJ3 porque ele troca CABELO (a
		// coisa que o `Vestir(null)` desfaria) e nao so tinta.
		FormaDef ssj3 = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		vis.VestirCabeloDaForma(ssj3);
		string cabeloAntes = vis.CabeloDeTeste;
		Conferir(cabeloAntes.Length > 0, $"a furia comeca num corpo transformado (cabelo `{cabeloAntes}`)");

		// OS DOIS SONS DO DM (`chargeaura.wav` na largada, `powerup.wav` na erupcao) RESOLVEM. Pela
		// MESMA funcao que o tocador usa (`CaminhoDoSom`) e nao por uma tabela paralela -- ver o
		// comentario dela: nome errado hoje vira SILENCIO, e silencio e o defeito que ninguem repara.
		foreach (Jandirus.Core.Forms.Beat b in furia.Beats)
		{
			if (b.Som.Length == 0) continue;
			string? cam = Transformacao.CaminhoDoSom(b.Som);
			Conferir(cam != null && ResourceLoader.Exists(cam),
					 $"furia: o som `{b.Som}` dos {b.Em:0.#}s resolve ({cam ?? "sem caminho"})");
		}

		int presosAntes = Transformacao.PresosDeTeste;
		int poseAntes = vis.DonosDaPoseDeTeste;
		int tetosAntes = Transformacao.TetosDeTeste;
		int estragoAntes = PoeiraDeEstrago.PedidosDeTeste;

		// `forma: null` -- E ESTE E O TESTE. Se o tocador voltar a exigir forma, isto nem compila.
		var t = Transformacao.Rodar(corpo.GetParent(), corpo, forma: null, furia, souEu: true);
		t.SetProcess(false);   // o relogio e nosso

		// ---- 1. o corpo nao para, e a medida e ANTES do primeiro `_Process` ----
		// AQUI E QUE ESTA O QUADRO. A soltura pelo prazo acontece no `_Process`; se o `_Ready` tivesse
		// prendido, este `==` seria `+ 1` e so voltaria ao normal no proximo quadro -- que e o defeito
		// invisivel que esta linha existe pra pegar.
		Conferir(Transformacao.PresosDeTeste == presosAntes,
				 "a furia NAO prende o corpo (nem por um quadro) -- o `set waitfor = 0` do DM");
		Conferir(vis.DonosDaPoseDeTeste == poseAntes,
				 "...e nao trava a pose (um corpo que anda em pose parada desliza pelo chao)");

		int pedrasMax = 0;
		Color? corDaChama = null;
		double ateQuando = furia.Segundos + Transformacao.FolgaDoTeto + 1.0;
		for (double k = 0; k < ateQuando && IsInstanceValid(t); k += 0.1)
		{
			t._Process(0.1);
			pedrasMax = Math.Max(pedrasMax, t.PedrasVivasDeTeste);

			// A CHAMA MEDIDA NO MEIO DA CENA e nao no instante zero: ela nasce com forca 0 (ver o
			// `_Ready` do tocador) e o `_Process` e quem a acende. Aos ~1,5 s ela esta cheia.
			if (Math.Abs(k - 1.5) < 0.051) corDaChama = t.ChamaDaCenaDeTeste.CorNoMaterialDeTeste;
		}

		// ============================ A COR SAI DO MATERIAL, E A LEITURA E COBRADA ============================
		// `CorNoMaterialDeTeste` e um `Color?`: sem material ele e nulo. Um `if (... is { } cor)` dentro
		// do laco faria a checagem SUMIR nesse caso em vez de reprovar -- que e o modo de falha que este
		// arquivo inteiro persegue (bancada que mede nada e verde). Entao o nulo e uma reprova propria.
		Conferir(corDaChama != null, "a chama da furia tem material pra medir");
		if (corDaChama is { } cor)
		{
			var esperada = new Color(Jandirus.Core.Forms.Cinematicas.CorDaFuria);
			Conferir(Mathf.Abs(cor.R - esperada.R) < 0.02f && Mathf.Abs(cor.G - esperada.G) < 0.02f
					 && Mathf.Abs(cor.B - esperada.B) < 0.02f,
					 $"a chama da furia chega VERMELHA no material ({cor.ToHtml(false)}, "
				   + $"esperado {Jandirus.Core.Forms.Cinematicas.CorDaFuria})");
		}

		// ---- 3. ela termina sozinha ----
		Conferir(t.BeatsDeTeste == furia.Beats.Length,
				 $"a furia disparou os {furia.Beats.Length} beats ({t.BeatsDeTeste})");
		Conferir(!IsInstanceValid(t) || t.FimDeTeste == Transformacao.FimDaCena.Sozinha,
				 "a furia termina SOZINHA (nao no teto, nao no alvo sumido)");
		Conferir(Transformacao.TetosDeTeste == tetosAntes, "e o teto nao disparou nela");

		// ---- 1b. e ela nao deixou tranca nenhuma pra tras ----
		Conferir(Transformacao.PresosDeTeste == presosAntes,
				 "a furia acabou sem deixar o corpo preso");
		Conferir(vis.DonosDaPoseDeTeste == poseAntes, "...nem a pose travada");

		// ---- 2. ela nao despe ninguem ----
		// A CHECAGEM MAIS IMPORTANTE DESTE BLOCO. Ver o ponto 2 do cabecalho: `Vestir(null)` no beat da
		// virada devolveria o corpo a base, e isso nao daria erro nenhum -- daria um Super Saiyajin que
		// vira gente comum ao perder um amigo.
		Conferir(vis.CabeloDeTeste == cabeloAntes,
				 $"a furia NAO desfaz a forma de quem ja esta transformado "
			   + $"(entrou `{cabeloAntes}`, saiu `{vis.CabeloDeTeste}`)");

		// ---- 5. a pedra do chao ----
		Conferir(pedrasMax > 0, $"o chao se solta na furia (pico de {pedrasMax} pedras)");

		// ---- 6. e o sistema de estrago fica de fora ----
		Conferir(PoeiraDeEstrago.PedidosDeTeste == estragoAntes,
				 "a furia nao chama o sistema de ESTRAGO de cenario (o bit `Cascalho`, aposentado)");

		// devolve o boneco a base pro resto da bancada
		vis.VestirCabeloDaForma(null);
	}

	// =====================================================================
	// 3a. AS PEDRAS SUBINDO -- a folha do BYOND, alinhada a grade
	// =====================================================================
	/// <summary>
	/// AS PEDRAS DA CINEMATICA, MEDIDAS EM VEZ DE OLHADAS.
	///
	/// ============================ POR QUE ISTO PRECISA DE BANCADA ============================
	/// O efeito anterior era `GpuParticles2D` com a textura pintada em codigo, e ele NUNCA teve como
	/// falhar: retangulo marrom sempre desenha. Sprite e outra coisa -- ele tem tres jeitos de sumir
	/// calado, e os tres ja aconteceram neste projeto:
	///
	///   * o `.tres` existe na pasta e o Godot nao importou o `.png` -> folha nula, zero pedras,
	///     nenhum erro (foi o caso dos sons e o do Oozaru, logo abaixo);
	///   * o `ZIndex` herdado da cena (90) poe a pedra POR CIMA do personagem -- tapar o corpo no
	///     unico momento em que o jogador para pra olhar pra ele;
	///   * a posicao fora da grade. "Meio tile" nao da erro nenhum: da uma pedra saindo do chao no
	///     meio de lugar nenhum, que foi o pedido explicito do dono pra corrigir.
	/// ====================================================================================
	/// </summary>
	/// <param name="corpo">
	/// A ANCORA E O CORPO, E NAO A CENA -- e isto ja reprovou uma vez. A primeira versao media a
	/// distancia a partir de `t.GlobalPosition`, mas a cena so se cola no personagem no `_Process`
	/// (`GlobalPosition = _alvo.GlobalPosition`), e aqui ela acabou de nascer: o que se lia era a
	/// posicao do PAI dela. As pedras estavam certas o tempo todo; a regua e que estava no lugar
	/// errado, e ela ainda por cima deixava a checagem do "debaixo do corpo" passar por sorte.
	/// </param>
	private void AsPedrasSubindo(Transformacao t, Node2D corpo)
	{
		// O CAMINHO E O DO JOGO, e nao uma copia dele. Escrito duas vezes, o dia em que a arte mudar de
		// lugar esta bancada continuaria provando que um arquivo que ninguem usa mais esta importado.
		const string folha = Transformacao.CaminhoDasPedras;
		const int T = Jandirus.Core.World.ZoneCollision.TileSize;

		// EXISTIR NA PASTA NAO E ESTAR IMPORTADO -- ver o bloco 1 de `AFera`. Sem esta linha, o dia em
		// que o `.png` perder o `.import` a cinematica roda sem pedras e a bancada segue verde.
		Conferir(ResourceLoader.Exists(folha), $"a folha das pedras existe E o Godot a resolve ({folha.GetFile()})");
		var fr = ResourceLoader.Load<SpriteFrames>(folha);
		Conferir(fr != null && fr.GetAnimationNames().Length > 0, "a folha das pedras tem animacao");

		// ============================ AS QUATRO PERGUNTAS DA IMPORTACAO ============================
		// Sao as MESMAS do bloco do macaco, e por um motivo que ja custou caro neste projeto: um `.tres`
		// de `SpriteFrames` NAO precisa de importacao pra carregar. Ele abre feliz apontando pra um
		// `.png` que o Godot nunca converteu, e o que sai e uma folha com animacao e nenhum pixel --
		// zero erro, zero aviso, e a cinematica roda sem pedra nenhuma. O `ResourceLoader.Exists` la de
		// cima responde por (a) e nao diz NADA sobre (b), (c) e (d).
		//
		//   a) o `.tres` carrega                    -- a linha acima
		//   b) a textura que ELE cita esta no disco  -- lida do proprio arquivo, nao adivinhada
		//   c) o Godot a importou (ha `.import`)
		//   d) e ela tem pixel de verdade
		//
		// COMO REPROVA SE A REGRA SUMIR: apague `Rising Rocks.png.import` (ou `.godot/imported/`) e (c)
		// e (d) caem enquanto (a) continua verde -- que e exatamente o estado que produz a cena muda.
		// ======================================================================================
		string arqFolha = ProjectSettings.GlobalizePath(folha);
		string textoFolha = System.IO.File.Exists(arqFolha) ? System.IO.File.ReadAllText(arqFolha) : "";
		Conferir(textoFolha.Length > 0, $"o .tres das pedras esta no disco ({arqFolha.GetFile()})");

		var artes = System.Text.RegularExpressions.Regex
			.Matches(textoFolha, "ext_resource[^\\n]*path=\"(res://[^\"]+)\"")
			.Select(m => m.Groups[1].Value).Distinct().ToList();
		Conferir(artes.Count > 0, $"a folha das pedras referencia alguma arte ({artes.Count})");

		foreach (string arte in artes)
		{
			Conferir(System.IO.File.Exists(ProjectSettings.GlobalizePath(arte)),
					 $"a arte das pedras esta NO DISCO ({arte.GetFile()})");
			Conferir(System.IO.File.Exists(ProjectSettings.GlobalizePath(arte) + ".import"),
					 $"a arte das pedras foi IMPORTADA (ha {arte.GetFile()}.import)");
			Conferir(ResourceLoader.Exists(arte), $"e o Godot a resolve ({arte.GetFile()})");
			var tex = ResourceLoader.Load<Texture2D>(arte);
			Conferir(tex != null && tex.GetWidth() > 0,
					 $"e ela tem pixel ({tex?.GetWidth() ?? 0}x{tex?.GetHeight() ?? 0})");
		}

		if (fr?.GetFrameTexture(fr.GetAnimationNames()[0], 0) is { } q0)
		{
			// O LADO DO QUADRO **E** O LADO DO TILE, e e disso que o alinhamento vive: centro do
			// sprite no centro da celula so cobre o tile inteiro se os dois medirem o mesmo. Uma
			// folha de 48 px passaria a sobrar meio tile pra cada lado sem ninguem notar.
			Conferir(q0.GetWidth() == T && q0.GetHeight() == T,
					 $"o quadro da pedra tem o tamanho de UM tile ({q0.GetWidth()}x{q0.GetHeight()}, tile {T})");
			Conferir(fr!.GetFrameCount(fr.GetAnimationNames()[0]) == 4,
					 $"a folha tem os 4 quadros do `Rising Rocks.dmi` ({fr.GetFrameCount(fr.GetAnimationNames()[0])})");
		}

		// ============================ NAO E PARTICULA, E SPRITE ============================
		// A reprova mais direta que esta bancada tem: se alguem repuser o `GpuParticles2D`, o node
		// `Pedras` volta a ser um emissor e nao um `Node2D` com `AnimatedSprite2D` dentro.
		//
		// O dono ja recusou particula pra isto uma vez, com estas palavras: *"nas cinematicas e pra
		// tirar o efeito de pedras levitando em particulas, ficou mt feio, prefiro usar o proprio
		// rising rocks .png"*. "Mais pedras" foi lido como mais INSTANCIAS do sprite, nunca como
		// emissor novo.
		int antes = t.PedrasVivasDeTeste;
		t.SoltarPedrasDeTeste();
		int nasceram = t.PedrasVivasDeTeste - antes;

		// ============================ A POPULACAO E DERIVADA, E ELA E O TETO DE CUSTO ============================
		// O alvo sai de `FracaoDoChaoSolto` (o `prob(15)` do DM) vezes os tiles que a camera alcanca, e
		// o teto ABSOLUTO e uma pedra por tile (o `_ocupadas`). Cobrar os dois aqui e o que impede a
		// resposta a *"mais pedras"* de virar "mil nodes vivos numa cena de dois minutos".
		//
		// A CONTA E REFEITA e nao lida do proprio objeto: perguntar `AlvoDePedrasDeTeste` dos dois lados
		// aprovaria qualquer numero que ele devolvesse, inclusive zero.
		// ===================================================================================================
		int alvoEsperado = Mathf.RoundToInt(
			t.TilesDePedraDeTeste * Transformacao.FracaoDoChaoSolto);
		Conferir(t.AlvoDePedrasDeTeste == alvoEsperado,
				 $"a populacao de pedra e {Transformacao.FracaoDoChaoSolto:P0} dos tiles alcancados "
			   + $"({t.AlvoDePedrasDeTeste} de {t.TilesDePedraDeTeste} tiles, esperado {alvoEsperado})");
		Conferir(nasceram > 0 && nasceram <= t.AlvoDePedrasDeTeste,
				 $"o enchimento respeita o alvo ({nasceram} de {t.AlvoDePedrasDeTeste})");
		Conferir(t.PedrasVivasDeTeste <= t.TilesDePedraDeTeste,
				 $"e o teto absoluto e UMA pedra por tile ({t.PedrasVivasDeTeste} de {t.TilesDePedraDeTeste})");

		// ============================ A AREA E A DA CAMERA, E ELA CRESCEU ============================
		// *"aumente a area q o jogo pode spawnar esse efeito de rising rock, pq ta mt perto do
		// personagem"*. Era `3 x 2` cravado; agora e `GetViewportRect().Size / (2 * zoom) / tile`,
		// arredondado pra CIMA (o tile da borda aparece pela metade e conta).
		//
		// A CONTA E REFEITA AQUI, da camera desta bancada, e nao lida do tocador -- pelo mesmo motivo do
		// alvo logo acima. E o `>= 3 x 2` e a CATRACA: seja qual for o zoom, a area nunca pode voltar a
		// ser menor do que a que o dono reclamou.
		// =======================================================================================
		float zoomDaTela = GetViewport()?.GetCamera2D() is { } camDaTela && camDaTela.Zoom.X > 0.01f
			? camDaTela.Zoom.X
			: Math.Max(1, Boot.Config.Zoom);
		// `GetVisibleRect` do viewport e o MESMO retangulo que o `GetViewportRect()` do `CanvasItem`
		// devolve la no tocador -- este robo nao e um `CanvasItem` e nao tem o atalho.
		Vector2 meiaVista = (GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero) / (2f * zoomDaTela);
		var alcanceEsperado = new Vector2I(
			Math.Max(1, Mathf.CeilToInt(meiaVista.X / T)), Math.Max(1, Mathf.CeilToInt(meiaVista.Y / T)));
		Conferir(t.AlcanceDePedraDeTeste == alcanceEsperado,
				 $"o alcance das pedras e o que a camera mostra ({t.AlcanceDePedraDeTeste}, "
			   + $"esperado {alcanceEsperado} no zoom {zoomDaTela:0.#})");
		Conferir(t.AlcanceDePedraDeTeste.X >= 3 && t.AlcanceDePedraDeTeste.Y >= 2,
				 $"e ele nunca encolhe abaixo do 3x2 de antes ({t.AlcanceDePedraDeTeste})");

		Vector2[] onde = t.PedrasDeTeste;
		bool naGrade = true, semRepetir = onde.Distinct().Count() == onde.Length;
		foreach (Vector2 p in onde)
		{
			// O CENTRO DE UMA CELULA E O MEIO DELA: `(cel + 0,5) * 32` deixa resto 16 nos dois eixos.
			// Qualquer outro resto e pedra saindo do chao no meio de um tile -- o defeito que o dono
			// apontou. `Mathf.PosMod` porque coordenada de mundo e negativa a oeste da origem.
			if (!Mathf.IsEqualApprox(Mathf.PosMod(p.X, T), T * 0.5f)
			 || !Mathf.IsEqualApprox(Mathf.PosMod(p.Y, T), T * 0.5f)) naGrade = false;
		}
		Conferir(naGrade, "toda pedra nasce ALINHADA a grade (centro da celula)");
		Conferir(semRepetir, "o sorteio nao repete tile (duas pedras no mesmo lugar somem uma na outra)");

		// DENTRO DO QUE A CAMERA MOSTRA, e nem um tile alem: o DM sorteia a 7 tiles porque a tela dele
		// era outra (15x15 tiles inteiros). Uma pedra fora do quadro e efeito pago e nao visto.
		//
		// O LIMITE E O ALCANCE MEDIDO (mais meio tile, que e do centro do sprite ao canto da celula), e
		// nao um numero solto: escrito a mao, ele ficaria pra tras no dia em que a janela mudasse -- que
		// e literalmente o defeito que este passe veio consertar.
		Vector2 pes = corpo.GlobalPosition + new Vector2(0, Jandirus.Core.World.MoveRules.FeetOffsetY);
		bool perto = true, foraDosPes = true;
		foreach (Vector2 p in onde)
		{
			if (Mathf.Abs(p.X - pes.X) > (t.AlcanceDePedraDeTeste.X + 1) * T
			 || Mathf.Abs(p.Y - pes.Y) > (t.AlcanceDePedraDeTeste.Y + 1) * T) perto = false;
			// NENHUMA embaixo do corpo: uma pedra de 32x32 sob os pes fica escondida pelo personagem.
			// O buraco e derivado da FOLHA do boneco (ver `Transformacao.MontarOChaoSolto`), entao num
			// macaco de 96 ele e 3x3 -- aqui o boneco e de 32 e a celula dos pes basta.
			if (Mathf.FloorToInt(p.X / T) == Mathf.FloorToInt(pes.X / T)
			 && Mathf.FloorToInt(p.Y / T) == Mathf.FloorToInt(pes.Y / T)) foraDosPes = false;
		}
		Conferir(perto, "as pedras caem dentro do que a camera mostra, e nao alem");
		Conferir(foraDosPes, "nenhuma pedra nasce DEBAIXO do corpo (o personagem a taparia)");

		// ============================ SOBRE O CHAO E SOB O CORPO ============================
		// `ZAsRelative = false` e o que importa: sem ele a pedra herda o `ZIndex = 90` da cena e passa
		// a desenhar por cima do personagem. Como o `ZIndex` proprio continuaria -1, um teste que so
		// lesse `ZIndex` passaria verde com a pedra tapando o corpo.
		Node? raiz = t.GetNodeOrNull("Pedras");
		Conferir(raiz is Node2D { TopLevel: true },
				 "as pedras vivem num node `TopLevel` (senao andam junto com o personagem)");
		bool camadaOk = raiz != null && raiz.GetChildCount() > 0;
		foreach (Node n in raiz?.GetChildren() ?? [])
			if (n is not AnimatedSprite2D { ZIndex: -1, ZAsRelative: false }) camadaOk = false;
		Conferir(camadaOk, "cada pedra e um AnimatedSprite2D em z ABSOLUTO -1 (sobre o chao, sob o corpo)");

		// ============================ ELAS SOMEM SOZINHAS ============================
		// Uma pedra que nao morre fica no cenario pra sempre -- e como o node e `TopLevel`, ela nem
		// acompanha o dono: fica um monte de pedra parada no meio do mapa. Aqui se prova que a cena
		// e DONA delas: matar a cena mata as pedras junto, que e o ultimo caminho de limpeza.
		int vivas = raiz?.GetChildCount() ?? 0;
		Conferir(vivas > 0, $"as pedras estao penduradas na cena ({vivas} node(s))");

		// ============================ E NENHUM EMISSOR EM PE NA CENA INTEIRA ============================
		// A checagem de tipo la de cima olha os FILHOS do node `Pedras`. Um emissor reposto em qualquer
		// outro canto do tocador (junto da aura grande, dos feixes, na raiz) escaparia dela -- e voltaria
		// a espalhar os cacos de 3x4 px marrons que o dono mandou tirar, do lado das pedras certas.
		//
		// Entao a varredura e da ARVORE inteira desta cena, recursiva. Ela pega o node exista onde
		// existir, e nao depende de o autor da volta ter posto no lugar de antes.
		// ==========================================================================================
		int emissores = ContarParticulas(t);
		Conferir(emissores == 0,
				 $"nenhum `GpuParticles2D` de pe na arvore da cinematica ({emissores}) -- as pedras sao sprite");

		AVidaDaPedraEADoDm();
		OGpuParticlesNaoVoltouAoFonte();
	}

	// =====================================================================
	// 3a-ter. OS NUMEROS DA PEDRA, CONTRA O ORIGINAL
	// =====================================================================
	/// <summary>
	/// OS QUATRO NUMEROS DO CHAO SOLTO, ESCRITOS AQUI A MAO -- lidos do `dusts.dm` e do
	/// `SSJCinematic.dm`, e NAO do <see cref="Transformacao"/>.
	///
	/// ============================ POR QUE A MAO, SE O TOCADOR JA OS TEM ============================
	/// Porque um numero conferido consigo mesmo nao e conferido. A bancada mede a vida observada de
	/// cada pedra contra `Transformacao.VidaMinima` (ver o laco das 64 cenas) -- e o piso daquela
	/// comparacao DESCE junto com a constante. Sem uma ancora fora do arquivo medido, aquele bloco
	/// prova que o tocador e coerente com o que ele mesmo declara, e nao que o numero e o do original.
	///
	/// (Medido, injetando `VidaMinima = 2,4`: esta linha reprova, e a la tambem -- mas a de la so
	/// porque a amostra do chao corre a cada 0,5 s e uma vida de 2,4 s nao sobra folga. Numa regressao
	/// mais branda -- 6 s, digamos -- a de la passaria e esta continuaria reprovando.)
	///
	/// E o <see cref="TetoDaCurtaEscritoAMao"/> ja tinha aberto o precedente pelo mesmo motivo.
	///
	/// ============================ E ELES SAO O PEDIDO DO DONO, NAO GOSTO MEU ============================
	/// *"aumente a area q o jogo pode spawnar esse efeito de rising rock, pq ta mt perto do personagem
	/// e dura mt pouco"*. A resposta a "dura mt pouco" foi tirada do original e nao inventada: eram
	/// 2,4/3,6 s aqui contra 10 a 40 s la, quatro a onze vezes menos. Se alguem "otimizar" a vida de
	/// volta um dia, e este bloco que diz de onde o numero veio.
	/// ==========================================================================================
	/// </summary>
	private void AVidaDaPedraEADoDm()
	{
		// `spawn(rand(100,400))` em `dusts.dm:207` -- tique do BYOND e um DECIMO de segundo.
		const double VidaMinimaDoDm = 100 / 10.0, VidaMaximaDoDm = 400 / 10.0;
		// `if(prob(15))` sobre o `view()` inteiro -- `SSJCinematic.dm:31` e `SSJ2Cinematic.dm:13`.
		const double FracaoDoDm = 0.15;
		// O TETO do `spawn(rand(10,150))` da mesma varredura: o chao termina de se soltar aos 15,0 s.
		const double EnchimentoDoDm = 150 / 10.0;

		Conferir(Math.Abs(Transformacao.VidaMinima - VidaMinimaDoDm) < 0.001
				 && Math.Abs(Transformacao.VidaMaxima - VidaMaximaDoDm) < 0.001,
				 $"a pedra vive os {VidaMinimaDoDm:0.#} a {VidaMaximaDoDm:0.#}s do `dusts.dm:207` "
			   + $"(esta com {Transformacao.VidaMinima:0.#} a {Transformacao.VidaMaxima:0.#})");

		Conferir(Math.Abs(Transformacao.FracaoDoChaoSolto - FracaoDoDm) < 0.0001,
				 $"e {FracaoDoDm:P0} do chao a vista se solta, como o `prob(15)` do DM "
			   + $"(esta com {Transformacao.FracaoDoChaoSolto:P0})");

		Conferir(Math.Abs(Transformacao.EnchimentoMaximo - EnchimentoDoDm) < 0.001,
				 $"e o chao termina de se soltar em {EnchimentoDoDm:0.#}s, o teto do `spawn(rand(10,150))` "
			   + $"(esta com {Transformacao.EnchimentoMaximo:0.#}s)");

		// ============================ E A VIDA E MUITO MAIOR QUE A QUE O DONO RECLAMOU ============================
		// A linha acima ja tranca o numero. Esta tranca a DISTANCIA dele pro estado anterior, e existe
		// porque o numero pode voltar disfarcado: `VidaMinima = 3.6` com um comentario novo dizendo que
		// "10 s era exagero" passaria pela comparacao acima so se alguem lembrasse de mudar o
		// `VidaMinimaDoDm` junto -- que e justamente o que quem mexe faz pra a bancada calar.
		//
		// A VIDA ANTIGA E DERIVADA e nao escrita: eram DOIS OU TRES ciclos da folha, entao o pior caso
		// dela e `ciclo * 3`. Trocar a arte por uma de mais quadros nao pode afrouxar esta linha, e por
		// isso o numero vem do `.tres` e nao de um "3,6" copiado do relatorio.
		//
		// O FATOR E DOIS, e nao os quatro a onze que a medida deu: o que se cobra e a ORDEM de grandeza,
		// porque o dia em que o dono pedir mais tempo ainda isto nao pode atrapalhar.
		// ====================================================================================================
		double cicloDaFolha = CicloDaFolhaDasPedras();
		Conferir(cicloDaFolha > 0, $"a folha da pedra diz quanto dura um ciclo dela ({cicloDaFolha:0.##}s)");
		Conferir(cicloDaFolha <= 0 || Transformacao.VidaMinima >= cicloDaFolha * 3 * 2,
				 $"e a mais curta ainda e ao menos o DOBRO da vida antiga "
			   + $"({Transformacao.VidaMinima:0.#}s contra os {cicloDaFolha * 3:0.##}s de tres ciclos)");
	}

	/// <summary>
	/// QUANTO DURA UMA VOLTA DA FOLHA DA PEDRA -- relido do `.tres`, do jeito que o tocador o le.
	///
	/// A conta e a mesma do `Transformacao.DuracaoDoCiclo` (que e privado). Repetida de proposito: e
	/// a UNICA maneira de a bancada ter um ciclo proprio pra comparar com o que o tocador usou. Pedir
	/// o numero ao tocador aprovaria qualquer resposta que ele desse -- inclusive zero, que e o que
	/// sai de uma folha que o Godot nao importou.
	/// </summary>
	private static double CicloDaFolhaDasPedras()
	{
		var f = ResourceLoader.Load<SpriteFrames>(Transformacao.CaminhoDasPedras);
		if (f == null || f.GetAnimationNames().Length == 0) return 0;
		string a = f.GetAnimationNames()[0];
		double fps = f.GetAnimationSpeed(a);
		if (fps <= 0) return 0;
		double soma = 0;
		for (int i = 0; i < f.GetFrameCount(a); i++) soma += f.GetFrameDuration(a, i);
		return soma > 0 ? soma / fps : 0;
	}

	// =====================================================================
	// 3a-quater. A AREA E MEDIDA -- provado mudando a camera
	// =====================================================================
	/// <summary>
	/// O ALCANCE DO SORTEIO ACOMPANHA O ZOOM. Provado rodando a MESMA cena em duas camaras.
	///
	/// ============================ O FURO QUE ESTE BLOCO FECHA ============================
	/// A checagem do <see cref="AsPedrasSubindo"/> refaz a conta da camera e compara com o
	/// `AlcanceDePedraDeTeste`. Ela pega o `3 x 2` cravado de volta -- mas so porque a janela desta
	/// bancada, hoje, da `10 x 6`. Um literal que por acaso EMPATE com a medida (e `10 x 6` cravado
	/// e o candidato obvio, porque e o numero que o autor veria no relatorio) passa por ela inteirinho,
	/// e o defeito volta na maquina de quem joga em outra resolucao ou noutro zoom.
	///
	/// Uma medida so nao distingue "derivado" de "cravado no valor certo". Duas distinguem.
	///
	/// ============================ POR QUE UMA CENA NOVA, E POR QUE A MAIS CURTA ============================
	/// O alcance e montado no `_Ready` (`MontarOChaoSolto`), entao mexer no zoom depois nao remonta
	/// nada: e preciso uma cena que NASCA com a outra camera. Escolhida a mais curta do catalogo, e ela
	/// e bombeada ate o fim -- uma cena abandonada no meio dispararia o teto, e o `Fechar` cobra que na
	/// rodada inteira ele dispare uma vez so.
	///
	/// O ZOOM VOLTA no `finally`: metade desta bancada mede pixel na tela, e deixar a camera torta
	/// derrubaria blocos que nao tem nada com pedra.
	/// ====================================================================================================
	/// </summary>
	private void AAreaDaPedraEMedidaENaoEscrita(Node2D corpo)
	{
		if (GetViewport()?.GetCamera2D() is not { } cam || cam.Zoom.X <= 0.01f)
		{
			Conferir(false, "ha camera 2D pra provar que o alcance da pedra e medido");
			return;
		}

		// A MAIS CURTA DO CATALOGO: o laco de bombeamento e proporcional ao tamanho dela, e este bloco
		// paga uma cena inteira so pra ler um `Vector2I`.
		Jandirus.Core.Forms.Cinematica curta = Jandirus.Core.Forms.Cinematicas.Todas
			.OrderBy(c => c.Segundos).First();
		FormaDef? df = Jandirus.Core.Forms.Catalogo.Def(curta.Forma);
		if (df == null) { Conferir(false, "a cena mais curta aponta pra uma forma do catalogo"); return; }

		Vector2 zoomAntes = cam.Zoom;
		Vector2I comOZoomDaBancada = default, comODobro = default;
		try
		{
			var a = Transformacao.Rodar(corpo.GetParent(), corpo, df, curta, souEu: true);
			a.SetProcess(false);
			comOZoomDaBancada = a.AlcanceDePedraDeTeste;
			for (double k = 0; k < curta.Segundos + 1.0 && IsInstanceValid(a); k += 0.1) a._Process(0.1);

			// DOBRAR O ZOOM E ENXERGAR A METADE: `GetViewportRect().Size / (2 * zoom)` -- o mundo visivel
			// encolhe na mesma proporcao. Por isso a comparacao la embaixo e por METADE e nao por
			// "mudou": "mudou" ficaria verde com o alcance reagindo ao contrario.
			cam.Zoom = zoomAntes * 2f;
			var b = Transformacao.Rodar(corpo.GetParent(), corpo, df, curta, souEu: true);
			b.SetProcess(false);
			comODobro = b.AlcanceDePedraDeTeste;
			for (double k = 0; k < curta.Segundos + 1.0 && IsInstanceValid(b); k += 0.1) b._Process(0.1);
		}
		finally { cam.Zoom = zoomAntes; }

		const int T = Jandirus.Core.World.ZoneCollision.TileSize;
		Vector2 meiaVista = (GetViewport()?.GetVisibleRect().Size ?? Vector2.Zero) / (2f * zoomAntes.X * 2f);
		var esperadoNoDobro = new Vector2I(
			Math.Max(1, Mathf.CeilToInt(meiaVista.X / T)), Math.Max(1, Mathf.CeilToInt(meiaVista.Y / T)));

		Conferir(comODobro == esperadoNoDobro,
				 $"com o zoom DOBRADO o alcance da pedra vira {esperadoNoDobro} "
			   + $"(veio {comODobro}, era {comOZoomDaBancada}) -- ele e medido, nao escrito");

		// E O SENTIDO. Um `Vector2I` cravado daria o MESMO nas duas camaras; um alcance que reagisse ao
		// contrario (crescendo com o zoom) casaria com "mudou" e nao com esta.
		Conferir(comODobro.X < comOZoomDaBancada.X || comODobro.Y < comOZoomDaBancada.Y,
				 $"e ele ENCOLHE quando a camera se aproxima ({comOZoomDaBancada} -> {comODobro})");
	}

	/// <summary>Quantos emissores de particula ha nesta arvore, deste node pra baixo.</summary>
	private static int ContarParticulas(Node raiz)
	{
		int n = raiz is GpuParticles2D or CpuParticles2D ? 1 : 0;
		foreach (Node f in raiz.GetChildren()) n += ContarParticulas(f);
		return n;
	}

	/// <summary>
	/// QUANTAS LINHAS DE CODIGO (nao de comentario) de um arquivo citam uma palavra.
	///
	/// Era funcao local de um bloco so. Virou metodo porque DOIS blocos varrem o mesmo fonte por
	/// palavras diferentes -- o emissor de particula e o sistema de estrago de cenario. Copiada, ela
	/// viraria dois filtros de comentario pra manter iguais, e o modo de falha de um filtro que
	/// envelhece e sempre o mesmo: passa a descartar demais e a checagem devolve zero pra sempre.
	///
	/// Devolve `(-1, 0)` quando o arquivo nao existe -- e `-1` nunca e igual ao zero que os
	/// chamadores exigem, entao um caminho errado REPROVA em vez de virar ausencia.
	/// </summary>
	private static (int codigo, int linhas) VarrerFonte(string res, string palavra)
	{
		string arq = ProjectSettings.GlobalizePath(res);
		if (!System.IO.File.Exists(arq)) return (-1, 0);

		int achados = 0, total = 0;
		foreach (string l in System.IO.File.ReadAllLines(arq))
		{
			total++;
			string s = l.TrimStart();
			// COMENTARIO DE LINHA, DE DOC E CORPO DE BLOCO. Basta pro que se mede aqui, e os
			// controles dos dois chamadores provam que basta.
			if (s.StartsWith("//") || s.StartsWith("*") || s.StartsWith("/*")) continue;
			if (l.Contains(palavra)) achados++;
		}
		return (achados, total);
	}

	// =====================================================================
	// 3a-quinquies. A VARREDURA NO FONTE
	// =====================================================================
	/// <summary>
	/// O `GpuParticles2D` DAS PEDRAS FOI DELETADO, E NAO PODE VOLTAR -- conferido NO ARQUIVO.
	///
	/// ============================ POR QUE NO FONTE, SE A ARVORE JA DIZ ============================
	/// A varredura de arvore (logo acima) so ve o que ESTA de pe naquele instante. Um emissor criado
	/// dentro de um `if` que a bancada nao percorre -- uma forma especifica, um beat que a cena de teste
	/// nao tem, um caminho de erro -- nao aparece nela e continua no jogo. O fonte nao tem instante: se
	/// a palavra estiver escrita como codigo, ela esta la.
	///
	/// A regra e do dono e ela e sobre a ARTE, nao sobre a API: *"nas cinematicas e pra tirar o efeito de
	/// pedras levitando em particulas, ficou mt feio, prefiro usar o proprio rising rocks .png q era
	/// usado no byond em tiles aleatorios perto do personagem"*. Por isso a varredura e do arquivo do
	/// TOCADOR e nao do projeto inteiro: `ClimaNaTela`, `PoeiraDeEstrago` e `RaiosDaForma` usam emissor
	/// de proposito e continuam certos.
	///
	/// ============================ COMENTARIO NAO CONTA, E ISSO E O DETALHE ============================
	/// O arquivo FALA de `GpuParticles2D` -- e o comentario que conta por que ele saiu, e ele tem que
	/// ficar (e a memoria da decisao). Uma varredura por `Contains` acusaria justamente a documentacao
	/// da propria regra, e a saida obvia -- apagar o comentario pra a bancada calar -- e o pior desfecho
	/// possivel.
	///
	/// E POR ISSO ELA PRECISA DE CONTROLE: um filtro de comentario com um bug (que descarte TODA linha)
	/// devolveria zero pra sempre e esta checagem viraria enfeite. O controle e o `RaiosDaForma`, que
	/// tem um emissor de verdade em codigo: se a varredura nao o achar la, ela nao esta achando nada em
	/// lugar nenhum.
	/// ==========================================================================================
	/// </summary>
	private void OGpuParticlesNaoVoltouAoFonte()
	{
		(int noTocador, int linhasTocador) = VarrerFonte("res://Client/Transformacao.cs", "GpuParticles2D");
		Conferir(linhasTocador > 100,
				 $"o fonte do tocador esta legivel pra varrer ({linhasTocador} linhas) -- "
			   + "sem isto a varredura passaria vazia");
		Conferir(noTocador == 0,
				 $"`GpuParticles2D` nao existe mais em codigo no tocador ({noTocador} linha(s) fora de comentario)");

		// O CONTROLE. Ver o cabecalho: sem ele, um filtro quebrado daria verde eterno.
		(int noRaio, _) = VarrerFonte("res://Client/RaiosDaForma.cs", "GpuParticles2D");
		Conferir(noRaio > 0,
				 $"a varredura ACHA emissor onde ele existe de verdade ({noRaio} em RaiosDaForma.cs)");

		// E O QUE SUBSTITUIU ELE CONTINUA LA. Se alguem apagar o `AnimatedSprite2D` das pedras junto
		// com o emissor, as duas checagens de cima ficam verdes com a cinematica sem efeito nenhum.
		(int comSprite, _) = VarrerFonte("res://Client/Transformacao.cs", "AnimatedSprite2D");
		Conferir(comSprite > 0, $"e o sprite que tomou o lugar dele esta la ({comSprite} linha(s))");

		OEstragoDeCenarioNaoVoltouAoFonte();
	}

	// =====================================================================
	// 3a-sexies. O QUADRADO MARROM, VARRIDO NO FONTE
	// =====================================================================
	/// <summary>
	/// NENHUMA CINEMATICA CHAMA O SISTEMA DE QUEBRAR CENARIO -- conferido NO ARQUIVO.
	///
	/// ============================ A CHECAGEM QUE IMPEDE A AUSENCIA DE VOLTAR CALADA ============================
	/// O pedido do dono foi *"vc colocou uns efeitos de particula nas cinematicas q parecem q tem uns
	/// quadrados marrons caindo e criando uma fumaca parecendo q quebrou uma parede ou objeto, TIRE
	/// esse efeito"* -- e o efeito era a cinematica chamando a <see cref="PoeiraDeEstrago"/>, o sistema
	/// do cenario sendo derrubado, pra fazer enfeite.
	///
	/// Ha duas barreiras antes desta, e as duas tem furo:
	///   * o COMPILADOR, porque o `Efeito.Cascalho` foi aposentado. Ele cobre o bit e nao a chamada:
	///     `PoeiraDeEstrago.Soltar(...)` escrito direto dentro do `Disparar` compila perfeitamente, e
	///     e literalmente assim que o efeito entrou ali da primeira vez;
	///   * o CONTADOR (`PedidosDeTeste`, medido em volta de cada uma das 64 cenas). Ele so ve o que a
	///     bancada EXECUTA -- uma chamada dentro de um `if` de forma, de zona ou de caminho de erro que
	///     nenhuma das 64 percorre nao aparece nele, e continua no jogo.
	///
	/// O fonte nao tem instante nem caminho: se a palavra estiver escrita como codigo, ela esta la.
	///
	/// ============================ E O SISTEMA CONTINUA VIVO -- E ISSO E METADE DA REGRA ============================
	/// Cortar o efeito da cinematica nao podia virar mutilar o estrago de cenario, que tem dono proprio
	/// e roda em combate. Por isso o CONTROLE aqui nao e um arquivo qualquer: e o `World`, que chama a
	/// `PoeiraDeEstrago` de verdade quando o cenario cai. Ele responde as duas perguntas de uma vez --
	/// a varredura enxerga (senao o zero do tocador nao valeria nada) E o caminho legitimo nao foi
	/// levado junto no corte.
	///
	/// ============================ COMENTARIO NAO CONTA ============================
	/// Pelo mesmo motivo do bloco irmao: o `Transformacao.cs` FALA muito de `PoeiraDeEstrago` -- sao os
	/// comentarios que guardam por que o `Cascalho()` foi deletado e por que o sistema nao foi tocado.
	/// Apagar aquilo pra a bancada calar seria o pior desfecho possivel, entao o filtro descarta
	/// comentario e o controle prova que ele nao esta descartando tudo.
	/// ====================================================================================================
	/// </summary>
	private void OEstragoDeCenarioNaoVoltouAoFonte()
	{
		const string Sistema = "PoeiraDeEstrago";

		(int noTocador, int linhasTocador) = VarrerFonte("res://Client/Transformacao.cs", Sistema);
		Conferir(linhasTocador > 100,
				 $"o fonte do tocador esta legivel pra varrer o estrago ({linhasTocador} linhas)");
		Conferir(noTocador == 0,
				 $"`{Sistema}` nao existe mais em codigo no tocador da cinematica "
			   + $"({noTocador} linha(s) fora de comentario)");

		// E NO ROTEIRO TAMBEM. O Core nao conhece o Godot e nao poderia chamar a classe; o que ele pode
		// e ganhar o bit de volta. `Cascalho` esta escrito nos comentarios de la (a memoria do corte),
		// entao vale a mesma regra -- e o `Efeito.` na frente e o que separa o bit da prosa.
		(int noRoteiro, int linhasRoteiro) = VarrerFonte("res://Core/Forms/Cinematicas.cs", "Efeito.Cascalho");
		Conferir(linhasRoteiro > 100, $"o fonte do roteiro esta legivel pra varrer ({linhasRoteiro} linhas)");
		Conferir(noRoteiro == 0,
				 $"e o bit `Efeito.Cascalho` nao voltou ao roteiro ({noRoteiro} linha(s) fora de comentario)");

		// ============================ O CONTROLE, QUE E TAMBEM A OUTRA METADE DA REGRA ============================
		// Ver o cabecalho. Zero aqui significa uma de duas coisas, e as duas sao reprova: ou o filtro de
		// comentario esta comendo o arquivo inteiro (e o zero do tocador la em cima nao vale nada), ou o
		// corte da cinematica levou junto o estrago de cenario -- o sistema que o dono NAO mandou tirar.
		// ====================================================================================================
		(int noMundo, _) = VarrerFonte("res://Client/World.cs", Sistema + ".Soltar");
		Conferir(noMundo > 0,
				 $"a varredura ACHA a chamada onde ela e legitima ({noMundo} em World.cs) "
			   + "-- o estrago de cenario continua inteiro e continua sendo chamado");
	}

	// =====================================================================
	// 3a-bis. O CORPO QUE O SERVIDOR DIRIGE -- o deslize, medido no corpo LOCAL
	// =====================================================================
	/// <summary>
	/// O DESLIZE DO DONO, MEDIDO ONDE ELE ACONTECE.
	///
	/// ============================ POR QUE ESTE BLOCO PRECISA EXISTIR ============================
	/// A bancada do servidor consegue provar que a IA anda e que o pacote diz "andando". O que ela
	/// NAO ve e o outro lado do fio: o corpo LOCAL era o unico do jogo que escolhia a propria pose
	/// pelo passo do TECLADO (`andando = _pos - antes`), e ninguem tinha avisado a ele que, com o
	/// servidor dirigindo, esse passo e sempre zero. Posicao andando + animacao parada = deslizar.
	/// Na tela dos OUTROS o mesmo macaco sempre andou animado, porque la ele e um `RemotePlayer` --
	/// e e por isso que o defeito sobreviveu: metade das telas mostrava a coisa certa.
	///
	/// Aqui o corpo local e alimentado como o servidor o alimenta (`ReceberPosse`, a mesma chamada
	/// que o `World` faz ao ler o snapshot) e a bancada olha as DUAS coisas: onde ele foi parar e o
	/// que ele desenhou pra chegar la.
	/// ==========================================================================================
	/// </summary>
	private void OCorpoTomado(Node2D corpoNode, CharacterVisual vis)
	{
		if (corpoNode is not LocalPlayer corpo)
		{
			Conferir(false, "o corpo local e um LocalPlayer (sem isso nada abaixo se mede)");
			return;
		}

		var origem = new Jandirus.Core.World.Vec2(corpo.Position.X, corpo.Position.Y);
		Jandirus.Core.World.Vec2 Onde() => new(corpo.Position.X, corpo.Position.Y);
		void Quadros(int n) { for (int i = 0; i < n; i++) corpo._Process(0.05); }

		// --- 1. SEM POSSE, NADA MUDA (a linha de base desta medicao) ------------------
		// Sem ela, "o corpo foi pro ponto do servidor" nao distingue a perseguicao de um corpo que
		// ja estivesse andando por conta propria nesta bancada.
		corpo.Destino = null;
		corpo.ReceberPosse(false, origem, Jandirus.Core.World.Facing.South, false,
						   Jandirus.Net.Protocol.Pose.Normal);
		vis.SetMotion(Jandirus.Core.World.Facing.South, moving: false);
		Quadros(4);
		Conferir((Onde() - origem).LengthSquared < 1,
				 $"sem posse e sem tecla o corpo fica parado ({origem} -> {Onde()})");

		// --- 2. A POSSE COMECA: o piloto automatico cai junto -------------------------
		// A fera nao continua a sua viagem. E a mesma regra que ja vale pra quem esta caido ou
		// carregando -- um piloto guiando um corpo que nao obedece e um comando pendurado.
		corpo.Destino = origem + new Jandirus.Core.World.Vec2(-400, 0);
		Jandirus.Core.World.Vec2 alvo = origem + new Jandirus.Core.World.Vec2(96, 0);
		corpo.ReceberPosse(true, alvo, Jandirus.Core.World.Facing.East, true,
						   Jandirus.Net.Protocol.Pose.Normal);
		Conferir(corpo.Destino == null, "assumir o corpo desliga o piloto automatico do dono");

		// --- 3. O CORPO VAI PRO PONTO DO SERVIDOR, E ANDANDO -------------------------
		// COMO REPROVA SE A REGRA SUMIR: tire o `_local?.ReceberPosse(...)` do laco de snapshot em
		// `World.AoReceberSnapshot` (ou o ramo `_semRedeas` do `_Process`) e o corpo nao sai do
		// lugar -- porque quem o movia era a correcao por pacote, que este ramo dispensa.
		Quadros(20);
		Conferir((Onde() - alvo).LengthSquared < 16,
				 $"o corpo persegue o ponto que o servidor mandou (alvo {alvo}, chegou {Onde()})");
		// E A ANIMACAO E A DE ANDAR. Esta e a linha do defeito: sem o `Moving` do snapshot o corpo
		// chega no lugar certo desenhando `default_east` -- deslizando.
		Conferir(vis.PoseDeTeste.StartsWith("walk"),
				 $"e ele ANDA em vez de deslizar (pose `{vis.PoseDeTeste}`)");
		Conferir(vis.PoseDeTeste.EndsWith("east"),
				 $"e olhando pra onde quem dirige o virou (pose `{vis.PoseDeTeste}`)");

		// --- 4. O DONO TENTA MEXER, E NAO ADIANTA ------------------------------------
		// O `Destino` e o unico "input" que uma bancada sem teclado consegue apertar -- e ele entra
		// no MESMO `dir` que as teclas de andar preenchem, tres linhas antes do `MoveRules.Advance`.
		// Como a posse ja esta em curso, ele nao e limpo (a faxina e so na virada), entao o pedido
		// fica pendurado exatamente como a tecla do dono ficaria.
		//
		// COMO REPROVA SE A REGRA SUMIR: apague o `return` do ramo `_semRedeas` no `_Process` e o
		// corpo passa a andar pro oeste e a desenhar `walk_west` -- que e, palavra por palavra, o
		// "eu ainda posso tentar mexer ai ele faz animaçao mas continua deslizando".
		corpo.Destino = origem + new Jandirus.Core.World.Vec2(-400, 0);
		Quadros(10);
		Conferir((Onde() - alvo).LengthSquared < 16,
				 $"o dono empurra pro outro lado e o corpo nao arreda ({Onde()} contra {alvo})");
		Conferir(vis.PoseDeTeste.EndsWith("east"),
				 $"nem vira o sprite pro lado que ele pediu (pose `{vis.PoseDeTeste}`)");

		// --- 4b. A UNICA TECLA QUE CONTINUA VALENDO ----------------------------------
		// ============================ A SAIDA TEM QUE EXISTIR DOS DOIS LADOS ============================
		// O servidor deixa `C2S.Activity` passar de proposito (`ComandoDeCorpo` nao o lista): meditar e o
		// `angertick` do DM e a UNICA resposta de quem perdeu o controle sem ter pericia. So que o pacote
		// nasce no CLIENTE, e o ramo `_semRedeas` do `_Process` pula todas as 15 leituras de tecla deste
		// arquivo -- inclusive essa. Sem a linha `LerAtividade(soASaida: true)` a saida existiria no
		// servidor e seria inalcancavel pelo jogador: regra escrita e desligada, a falha assinatura deste
		// port. E ela some sem barulho: ninguem estranha a falta de uma tecla num corpo que ja nao responde
		// a nenhuma outra.
		//
		// O T FICA DE FORA, e essa e a outra metade do `soASaida`: treinar com uma fera dirigindo o corpo
		// nao e um estado que o jogo deva aceitar, e o servidor renderia BP por ele.
		//
		// COMO REPROVA SE A REGRA SUMIR: apague o `LerAtividade` do ramo `_semRedeas` e a segunda linha
		// cai; troque `soASaida: true` por `false` e cai a primeira.
		// ============================================================================================
		Godot.Input.ActionPress("train");
		Quadros(1);
		Godot.Input.ActionRelease("train");
		Conferir(corpo.AtividadeDeTeste != Jandirus.Net.Protocol.Activity.Treinando,
				 $"possuido, a tecla de TREINAR nao vale (atividade `{corpo.AtividadeDeTeste}`)");

		Godot.Input.ActionPress("meditate");
		Quadros(1);
		Godot.Input.ActionRelease("meditate");
		Conferir(corpo.AtividadeDeTeste == Jandirus.Net.Protocol.Activity.Meditando,
				 $"...mas a de MEDITAR sim -- e a saida da fera (atividade `{corpo.AtividadeDeTeste}`)");

		// desliga a meditacao pra o resto da bancada nao herdar a pose
		Godot.Input.ActionPress("meditate");
		Quadros(1);
		Godot.Input.ActionRelease("meditate");

		// --- 5. E AS REDEAS VOLTAM ---------------------------------------------------
		// NINGUEM FICA PRESO PRA SEMPRE. Este projeto ja travou um jogador com um portao de input
		// que nunca abria; uma trava nova sem esta linha seria o mesmo tombo pela segunda vez.
		corpo.ReceberPosse(false, alvo, Jandirus.Core.World.Facing.East, false,
						   Jandirus.Net.Protocol.Pose.Normal);
		corpo.Teleportar(alvo);

		int andou = 0;
		foreach (Jandirus.Core.World.Vec2 rumo in new[]
		{
			new Jandirus.Core.World.Vec2(160, 0), new Jandirus.Core.World.Vec2(-160, 0),
			new Jandirus.Core.World.Vec2(0, 160), new Jandirus.Core.World.Vec2(0, -160),
		})
		{
			// AS QUATRO DIRECOES porque o corpo pode estar de costas pra uma parede: exigir uma
			// direcao especifica faria esta bancada reprovar por causa do CENARIO, que e o jeito
			// classico de um teste virar barulho e ser desligado.
			Jandirus.Core.World.Vec2 antes = Onde();
			corpo.Destino = antes + rumo;
			Quadros(6);
			if ((Onde() - antes).LengthSquared > 1) andou++;
			corpo.Destino = null;
		}
		Conferir(andou > 0, $"devolvidas as redeas, o corpo volta a obedecer ({andou}/4 direcoes)");

		// --- devolve o corpo como estava, pra o resto da bancada e pra a foto ---
		corpo.Teleportar(origem);
		corpo.Destino = null;
		vis.SetMotion(Jandirus.Core.World.Facing.South, moving: false);
	}


	// =====================================================================
	// 3a-sexies. O CONTORNO DO BEAST TROCANDO DE COR
	// =====================================================================
	/// <summary>
	/// A UNICA FORMA DO JOGO CUJO CONTORNO NAO E UMA COR, E SIM UMA ANIMACAO DE COR.
	/// *"beast fica trocando lentamente entre azul e roxo em uma transicao gradual"*.
	///
	/// ============================ POR QUE ISTO NAO SE TESTA NO CATALOGO ============================
	/// O bloco `AsCoresNoCatalogoInteiro` prova que o Core DIZ as duas cores. Isso passaria verde com
	/// o cliente inteiro ignorando a segunda: hexa escrito e ninguem lendo e a falha assinatura deste
	/// projeto. Aqui a medicao e no MATERIAL do sprite, quadro a quadro, pelo `_Process` de verdade.
	///
	/// SAO QUATRO PERGUNTAS, e a terceira e a que o dono pediu com todas as letras:
	///   1. ele SAI do azul (ou a oscilacao nao existe);
	///   2. meia volta o leva ao ROXO e a volta inteira o traz de volta -- e o meio do caminho nao e
	///      nenhuma das duas pontas, que e o que separa "transicao gradual" de "pisca-pisca";
	///   3. APAGADO ele nao anda. O contorno so acende acima dos 100% de Ki, e a oscilacao nao pode
	///      acender nada sozinha nem gastar quadro com o contorno apagado -- entao com forca 0 a cor
	///      NAO MUDA por mais quadros que passem;
	///   4. quem nao oscila fica parado. Sem esta, um `_Process` que ignorasse a guarda do nulo
	///      passaria nas tres primeiras e poria o SSJ3 inteiro a mudar de cor.
	///
	/// O PASSO E O DO JOGO (1/60 s) e nao um salto unico: a oscilacao acumula `delta`, e um teste que
	/// desse a meia volta de uma vez so passaria mesmo se ela estivesse implementada como um corte
	/// duro no meio do ciclo.
	/// ==========================================================================================
	/// </summary>
	private void OContornoDaFeraTrocandoDeCor(CharacterVisual vis)
	{
		FormaDef? fera = Jandirus.Core.Forms.Catalogo.Def("beast");
		if (fera == null) { Conferir(false, "a forma `beast` existe"); return; }

		var azul = new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(fera));
		Color? roxo = ContornoAlterna(fera);
		if (roxo == null) { Conferir(false, "o Beast tem a segunda cor do contorno"); return; }

		const double Passo = 1.0 / 60;
		double ciclo = Jandirus.Core.Forms.Catalogo.SegundosDoCicloDoContorno;

		// ============================ QUADROS CONTADOS EM INTEIRO ============================
		// Isto era `for (double t = 0; t < segundos; t += Passo)`, e o laco ERROU A CONTA: somando
		// 1/60 em ponto flutuante 120 vezes o acumulador cai em 1,9999999999999998, o `<` deixa
		// passar mais uma volta e a bancada rodava 121 quadros onde queria 120. A meia volta nao
		// notou (o cosseno e chato nas pontas), mas a volta inteira parava um sexagesimo de ciclo
		// antes do fim -- e a bancada reprovou uma cor que na tela e a mesma (`3f8cff` contra
		// `3f8cff`). O defeito era do medidor, nao do medido.
		// ================================================================================
		void Correr(double segundos)
		{
			int quadros = (int)Math.Round(segundos / Passo);
			for (int i = 0; i < quadros; i++) vis._Process(Passo);
		}
		Color Cor() => vis.ContornoNoMaterialDeTeste().Cor;

		// ============================ IGUAL ATE O PIXEL, E NAO ATE O BIT ============================
		// `IsEqualApprox` cobra 1e-6, que e mais fino do que uma cor de 8 bits sabe representar: a
		// soma de 240 `delta` deixa um residuo de fase que nao muda um unico pixel na tela e mesmo
		// assim reprovaria. A folga e 1/255 somada nos tres canais -- duas cores que caem no MESMO
		// byte sao a mesma cor, e e isso que o teste quer dizer.
		float Longe(Color x, Color y) =>
			Mathf.Abs(x.R - y.R) + Mathf.Abs(x.G - y.G) + Mathf.Abs(x.B - y.B);
		const float MesmoPixel = 1f / 255;

		// --- 1. acende na ponta A ---
		vis.AuraDaForma(azul, 1f, roxo);
		Conferir(Longe(Cor(), azul) < MesmoPixel,
				 $"o contorno da Fera estreia no azul ({Cor().ToHtml(false)})");

		// --- 2. um quarto de volta: nem uma ponta nem a outra ---
		Correr(ciclo / 4);
		Color meio = Cor();
		Conferir(Longe(meio, azul) > 0.05f && Longe(meio, roxo.Value) > 0.05f,
				 $"um quarto de volta o poe ENTRE as duas cores ({meio.ToHtml(false)}) -- transicao, nao corte");

		// --- 3. meia volta: a outra ponta ---
		Correr(ciclo / 4);
		Conferir(Longe(Cor(), roxo.Value) < MesmoPixel,
				 $"meia volta ({ciclo / 2:0.#}s) o leva ao roxo ({Cor().ToHtml(false)})");

		// --- 4. a volta inteira: de novo o azul ---
		Correr(ciclo / 2);
		Conferir(Longe(Cor(), azul) < MesmoPixel,
				 $"e a volta inteira ({ciclo:0.#}s) o traz de volta ao azul ({Cor().ToHtml(false)})");

		// ============================ 5. APAGADO ELE NAO ANDA ============================
		// A regra do Ki: contorno so acima dos 100%. Se a oscilacao rodasse com o contorno apagado ela
		// estaria gastando quadro por nada -- e, pior, o relogio chegaria numa fase qualquer, entao
		// acender de novo daria um SALTO de cor no instante em que o jogador passa dos 100%.
		//
		// APAGA FORA DA FASE ZERO, e isto e o teste inteiro: apagando na largada, "o relogio nao
		// andou" e "o relogio zerou" dariam a MESMA cor e a checagem passaria com a regra invertida.
		// Um quarto de volta poe o contorno no meio do caminho, que e a unica fase que os dois
		// comportamentos nao conseguem imitar um do outro.
		Correr(ciclo / 4);
		Color naFase = Cor();

		vis.AuraDaForma(azul, 0f, roxo);   // apagar reescreve a cor base -- e o esperado
		Conferir(Mathf.IsZeroApprox(vis.AuraDaFormaDeTeste),
				 $"apagar o contorno zera a forca dele ({vis.AuraDaFormaDeTeste:0.##})");
		Color apagado = Cor();
		Correr(ciclo / 2);
		Conferir(Longe(Cor(), apagado) < MesmoPixel && Mathf.IsZeroApprox(vis.AuraDaFormaDeTeste),
				 $"e meia volta com ele APAGADO nao mexe cor nenhuma nem acende nada "
			   + $"({apagado.ToHtml(false)} -> {Cor().ToHtml(false)}, forca {vis.AuraDaFormaDeTeste:0.##})");

		// E REACENDER CONTINUA DE ONDE PAROU. Um quadro depois a cor tem que estar de volta na FASE em
		// que apagou (o relogio nao correu), e nao na largada. A folga de 0,02 e um quadro de
		// oscilacao: no ponto mais rapido do cosseno a cor anda ~0,006 por quadro.
		vis.AuraDaForma(azul, 1f, roxo);
		Correr(Passo);
		Conferir(Longe(Cor(), naFase) < 0.02f && Longe(Cor(), azul) > 0.05f,
				 $"e reacender continua na fase em que parou ({naFase.ToHtml(false)} -> "
			   + $"{Cor().ToHtml(false)}), sem saltar pra a largada ({azul.ToHtml(false)})");

		// --- 6. quem NAO oscila fica parado ---
		FormaDef ssj3 = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		var amarelo = new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(ssj3));
		vis.AuraDaForma(amarelo, 1f, ContornoAlterna(ssj3));
		Correr(ciclo);
		Conferir(Longe(Cor(), amarelo) < MesmoPixel,
				 $"e o contorno do SSJ3 nao mexe uma volta inteira ({Cor().ToHtml(false)})");

		// ============================ 7. A VOLTA INTEIRA, QUADRO A QUADRO ============================
		// Os passos 1 a 4 medem QUATRO instantes escolhidos a dedo -- e quatro instantes nao dizem o
		// que acontece entre eles. Uma implementacao que saltasse do azul pro roxo no meio do ciclo
		// passa nos quatro: ela esta no azul em 0, no roxo em T/2 e no azul em T. O quarto de volta
		// pegaria o salto, mas so porque o salto foi posto justamente ali.
		//
		// Aqui a volta e percorrida INTEIRA, um quadro por vez, e as perguntas sao sobre o CONJUNTO:
		//   a) toda leitura cai no caminho entre as duas pontas -- nenhuma cor de fora, e nenhuma
		//      passando do azul pra tras nem do roxo pra frente;
		//   b) leituras em instantes diferentes dao cores DIFERENTES, e nao duas cores alternando;
		//   c) de um quadro pro outro a cor anda pouco (e o "sem corte duro" do pedido, medido);
		//   d) nas duas pontas ela inverte DEVAGAR -- e o que separa o cosseno de um vai-e-vem reto,
		//      que passa em (a), (b) e (c) e mesmo assim tem um canto visivel em cada ponta;
		//   e) e ela encosta nas duas pontas. Sem esta, uma oscilacao que fosse so ate a metade do
		//      caminho passaria em todas as outras -- e o Beast nunca ficaria roxo.
		// ==========================================================================================
		vis.AuraDaForma(azul, 1f, roxo);   // par novo -> o relogio zera, e a varredura comeca na ponta A
		int quadros = (int)Math.Round(ciclo / Passo);
		var lidas = new List<Color>(quadros);
		for (int i = 0; i < quadros; i++) { vis._Process(Passo); lidas.Add(Cor()); }

		// ONDE NO CAMINHO. Projecao no segmento azul->roxo: 0 e o azul, 1 e o roxo, e qualquer coisa
		// fora de [0,1] e uma cor que nao esta ENTRE as duas. Projecao e nao "canal R" porque o par
		// pode mudar -- hoje o azul e o roxo tem o mesmo B (1,0) e so R e G andam.
		Vector3 eixo = new(roxo.Value.R - azul.R, roxo.Value.G - azul.G, roxo.Value.B - azul.B);
		float Onde(Color c) =>
			new Vector3(c.R - azul.R, c.G - azul.G, c.B - azul.B).Dot(eixo) / eixo.LengthSquared();

		var foraDoCaminho = new List<string>();
		var distintas = new HashSet<string>();
		float menorT = 1, maiorT = 0, maiorPasso = 0, passoNasPontas = 0;
		int pontas = 0, meiaVolta = quadros / 2;
		for (int i = 0; i < lidas.Count; i++)
		{
			float t = Onde(lidas[i]);
			menorT = Mathf.Min(menorT, t);
			maiorT = Mathf.Max(maiorT, t);
			distintas.Add(lidas[i].ToHtml(false));

			// FORA DO CAMINHO sao DUAS coisas: passar das pontas (t fora de [0,1]) ou sair da reta
			// (uma cor que nao e mistura destas duas -- um verde no meio do caminho, por exemplo).
			// A folga e o mesmo pixel de 8 bits do resto do bloco.
			Color naReta = azul.Lerp(roxo.Value, Mathf.Clamp(t, 0, 1));
			if (t < -MesmoPixel || t > 1 + MesmoPixel || Longe(lidas[i], naReta) > MesmoPixel)
				foraDoCaminho.Add($"q{i}: {lidas[i].ToHtml(false)} (t={t:0.###})");

			float passo = Longe(lidas[i], i == 0 ? azul : lidas[i - 1]);
			maiorPasso = Mathf.Max(maiorPasso, passo);
			// AS DUAS PONTAS: os 8 quadros da largada (fase 0) e os 8 em volta da meia volta, que e
			// onde o caminho vira. Se a interpolacao for reta, a velocidade ali e a mesma do meio.
			if (i < 8 || Math.Abs(i - meiaVolta) < 4) { passoNasPontas += passo; pontas++; }
		}
		float mediaNasPontas = pontas > 0 ? passoNasPontas / pontas : 0;

		Conferir(foraDoCaminho.Count == 0,
				 $"as {lidas.Count} leituras da volta caem TODAS entre o azul e o roxo "
			   + $"({foraDoCaminho.Count} fora: {string.Join(" | ", foraDoCaminho.Take(3))})");
		// DEZESSEIS E NAO DOIS. O piso alto e o teste: um pisca-pisca entre as duas pontas devolve
		// exatamente 2 tons distintos e passaria por "leituras diferentes em instantes diferentes".
		Conferir(distintas.Count >= 16,
				 $"e duas leituras em instantes diferentes dao cores diferentes ({distintas.Count} tons "
			   + $"distintos numa volta)");
		// 0,05 SOMADO NOS TRES CANAIS e ~13/255 de mudanca num quadro. O caminho inteiro mede 0,61,
		// entao um corte duro da um passo dez vezes maior que este teto.
		Conferir(maiorPasso < 0.05f,
				 $"e de um quadro pro outro ela anda pouco -- transicao, nao corte (maior passo {maiorPasso:0.####})");
		Conferir(mediaNasPontas * 4 < maiorPasso,
				 $"e nas duas pontas ela inverte DEVAGAR, sem canto ({mediaNasPontas:0.####} na ponta "
			   + $"contra {maiorPasso:0.####} no meio)");
		Conferir(menorT <= 0.01f && maiorT >= 0.99f,
				 $"e a volta encosta nas duas pontas (foi de t={menorT:0.###} a t={maiorT:0.###})");
		_passos.Add($"  --     a volta da Fera: {lidas.Count} quadros, {distintas.Count} tons, passo "
				  + $"maior {maiorPasso:0.####} e {mediaNasPontas:0.####} nas pontas");

		// devolve o boneco apagado pra o resto da bancada
		vis.AuraDaForma(Colors.White, 0, null);
		_passos.Add($"  --     a Fera vai de #{Jandirus.Core.Forms.Catalogo.CorDoContorno(fera)} a "
				  + $"#{Jandirus.Core.Forms.Catalogo.CorDoContornoAlterna(fera)} e volta em {ciclo:0.#}s");
	}

	// =====================================================================
	// 3a-septies. O CONTORNO: FORA DOS OLHOS, MAIS FRACO E RESPIRANDO
	// =====================================================================
	/// <summary>
	/// TRES DEFEITOS DE UMA FOTO SO: *"o contorno n ta legal, ta mudando a cor dos olhos, e pra
	/// melhorar acho q o contorno deveria ficar um pouco mais fraco e ele ficar pulsando lentamente"*.
	///
	/// ============================ A. OS OLHOS ============================
	/// O shader mistura a cor onde `borda = c.a * (1 - viz)`. Num sprite de detalhe quase todo pixel
	/// tem vizinho transparente, entao "a borda" e o desenho INTEIRO e o olho troca de cor. O
	/// conserto e a lista de quem recebe o uniform (`CharacterVisual.EhSilhueta`), e por isso a
	/// medicao aqui e no MATERIAL da camada dos olhos -- que tem que ficar em zero enquanto a
	/// silhueta acende.
	///
	/// A PRIMEIRA PERGUNTA E "HA OLHOS?", e ela nao e formalidade: com a camada ausente, "os olhos
	/// estao em zero" seria verdade num boneco sem olhos e o bloco inteiro passaria verde sem provar
	/// nada. Este e o mesmo cuidado do `Camadas` no `ContornoNoMaterialDeTeste`.
	///
	/// ============================ B. MAIS FRACO ============================
	/// Medido pela FAIXA que o jogador ve, e nao pela constante: um teste que so lesse
	/// `Catalogo.ForcaDoContorno &lt; 1` seria a constante conferindo a si mesma. Aqui o topo e o fundo
	/// saem do material depois de o `_Process` girar, ou seja passam pelo caminho de verdade.
	///
	/// ============================ C. PULSANDO, E SEM ANULAR A COR ============================
	/// O pulso e a oscilacao da Fera dividem o mesmo pixel e tem relogios diferentes de proposito.
	/// O teste que importa e o ULTIMO: no instante em que a COR chega na outra ponta, o PULSO tem
	/// que estar no meio do caminho dele. Com periodos iguais (o erro obvio) ele estaria numa ponta
	/// tambem, e as duas animacoes viriam coladas -- uma leitura so em vez de duas.
	/// ==============================================================================
	/// </summary>
	private void OContornoMaisFracoEPulsando(CharacterVisual vis)
	{
		const double Passo = 1.0 / 60;
		double pulso = Jandirus.Core.Forms.Catalogo.SegundosDoPulsoDoContorno;
		float piso = (float)Jandirus.Core.Forms.Catalogo.PisoDoPulsoDoContorno;
		float topo = Jandirus.Core.Forms.Catalogo.ForcaDoContorno;

		// O MESMO CONTADOR EM INTEIRO do bloco da Fera, e pela mesma razao: somar 1/60 em ponto
		// flutuante erra a conta e a bancada roda um quadro a mais do que quer.
		void Correr(double segundos)
		{
			int quadros = (int)Math.Round(segundos / Passo);
			for (int i = 0; i < quadros; i++) vis._Process(Passo);
		}
		float Forca() => vis.ContornoNoMaterialDeTeste().Forca;

		var amarelo = new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(
			Jandirus.Core.Forms.Catalogo.Def("ssj3")!));

		// ============================ A. OS OLHOS FICAM DE FORA ============================
		vis.AuraDaForma(amarelo, topo, null);
		Conferir(vis.TemOlhosDeTeste,
				 "o boneco da bancada TEM camada de olhos (senao o resto deste bloco nao prova nada)");
		var nosOlhos = vis.ContornosNosOlhosDeTeste;
		Conferir(nosOlhos is { } o && Mathf.IsZeroApprox(o.Forma),
				 $"o contorno da FORMA nao chega nos olhos ({nosOlhos?.Forma.ToString("0.##") ?? "sem camada"})");
		// E A SILHUETA ACENDE -- sem esta, "os olhos em zero" passaria com o contorno morto no corpo
		// inteiro, que e o defeito oposto e igualmente ruim.
		(_, float naSilhueta, int camadas) = vis.ContornoNoMaterialDeTeste();
		Conferir(camadas >= 2 && naSilhueta > 0,
				 $"e chega na silhueta ({camadas} camadas, forca {naSilhueta:0.##}) -- corpo, cabelo, roupa e rabo");

		// O CANAL DO IMPACTO E A MESMA CONTA DE BORDA, entao tem o mesmo defeito nos olhos. Ele
		// escapou do olho do dono so porque dura 0,15 s debaixo de um flash branco -- e um teste que
		// cobrisse so o contorno da forma deixaria o irmao errado pra sempre.
		vis.Impacto(Colors.White, Colors.Red, Vector2.Right);
		var noSoco = vis.ContornosNosOlhosDeTeste;
		Conferir(noSoco is { } imp && Mathf.IsZeroApprox(imp.Impacto),
				 $"e o contorno do IMPACTO tambem nao ({noSoco?.Impacto.ToString("0.##") ?? "sem camada"})");
		// O CONTROLE: o que e do corpo INTEIRO tem que continuar chegando. Sem esta linha, excluir a
		// camada dos olhos de tudo passaria nas duas de cima -- e o rosto se descolaria no soco.
		Conferir(noSoco is { } fl && fl.Flash > 0,
				 $"-- mas o FLASH do mesmo soco chega nos olhos ({noSoco?.Flash.ToString("0.##") ?? "sem camada"}), "
			   + "senao o rosto se descolaria do corpo");

		// ============================ B. MAIS FRACO QUE ANTES ============================
		// A fase 0 e o TOPO: quem acabou de cruzar os 100% ve o contorno cheio.
		Conferir(Mathf.IsEqualApprox(naSilhueta, topo),
				 $"o contorno estreia no TOPO do pulso ({naSilhueta:0.###} de {topo:0.###})");
		Conferir(topo < 1f,
				 $"e o topo e mais fraco do que era ({topo:0.##} contra o 1,0 de antes)");

		// ============================ C. E RESPIRA ============================
		Correr(pulso / 2);
		float noFundo = Forca();
		Conferir(Mathf.IsEqualApprox(noFundo, topo * piso, 0.005f),
				 $"meia volta ({pulso / 2:0.##}s) o leva ao FUNDO ({noFundo:0.###}, esperado {topo * piso:0.###})");
		Conferir(noFundo > 0.3f,
				 $"e o fundo nao apaga o contorno ({noFundo:0.###}) -- respirar nao e piscar");

		Correr(pulso / 2);
		Conferir(Mathf.IsEqualApprox(Forca(), topo, 0.005f),
				 $"e a volta inteira ({pulso:0.##}s) o traz de volta ao topo ({Forca():0.###})");

		// --- a volta INTEIRA, quadro a quadro: nem salto, nem faixa errada ---
		// Os dois instantes acima passariam num pulso implementado como corte duro no meio do ciclo.
		var lidas = new List<float>();
		int quadrosDaVolta = (int)Math.Round(pulso / Passo);
		for (int i = 0; i < quadrosDaVolta; i++) { vis._Process(Passo); lidas.Add(Forca()); }

		var tons = new HashSet<int>();
		float maiorPasso = 0, menor = 1, maior = 0, anterior = topo;
		foreach (float f in lidas)
		{
			tons.Add((int)Math.Round(f * 255));
			maiorPasso = Mathf.Max(maiorPasso, Mathf.Abs(f - anterior));
			menor = Mathf.Min(menor, f);
			maior = Mathf.Max(maior, f);
			anterior = f;
		}
		Conferir(menor >= topo * piso - 0.005f && maior <= topo + 0.005f,
				 $"as {lidas.Count} leituras da volta ficam TODAS na faixa "
			   + $"({menor:0.###} .. {maior:0.###}, faixa {topo * piso:0.###} .. {topo:0.###})");
		Conferir(tons.Count >= 16,
				 $"e instantes diferentes dao intensidades diferentes ({tons.Count} degraus de 8 bits) "
			   + $"-- um pisca-pisca daria 2");
		// 0,01 num quadro e ~2,5/255. O caminho inteiro mede 0,245, entao um corte duro daria um
		// passo vinte vezes maior que este teto.
		Conferir(maiorPasso < 0.01f,
				 $"e de um quadro pro outro ela anda pouco ({maiorPasso:0.####}) -- respiracao, nao estalo");

		// ============================ D. APAGADO ELE NAO ACENDE NEM ANDA ============================
		// A regra do dono com todas as letras: o contorno so acende acima dos 100% de Ki. O pulso e um
		// FATOR sobre a forca pedida, entao com forca 0 nao ha fase do relogio que devolva pixel.
		Correr(pulso / 4);                        // sai da fase zero: apagar na largada nao provaria nada
		float naFase = Forca();
		vis.AuraDaForma(amarelo, 0f, null);
		Conferir(Mathf.IsZeroApprox(Forca()), $"forca 0 apaga o contorno ({Forca():0.###})");
		Correr(pulso);
		Conferir(Mathf.IsZeroApprox(Forca()),
				 $"e uma volta inteira APAGADO nao acende nada ({Forca():0.###}) -- o pulso nao acende sozinho");

		// E REACENDER CONTINUA DE ONDE PAROU: o relogio nao correu enquanto estava apagado. Sem isso,
		// cruzar os 100% de Ki daria um salto de intensidade em vez de continuar a respiracao.
		vis.AuraDaForma(amarelo, topo, null);
		Correr(Passo);
		Conferir(Mathf.Abs(Forca() - naFase) < 0.01f && Mathf.Abs(Forca() - topo) > 0.02f,
				 $"e reacender continua na fase em que parou ({naFase:0.###} -> {Forca():0.###}), "
			   + $"sem saltar pro topo ({topo:0.###})");

		// ============================ E. O PULSO E A COR NAO SE ANULAM ============================
		// Os dois relogios andam no mesmo boneco (a Fera e a unica que oscila de cor). Se tivessem o
		// mesmo periodo travariam em fase pra sempre e o olho leria UMA animacao.
		double ciclo = Jandirus.Core.Forms.Catalogo.SegundosDoCicloDoContorno;
		Conferir(!Mathf.IsEqualApprox((float)ciclo, (float)pulso),
				 $"a cor ({ciclo:0.#}s) e o pulso ({pulso:0.#}s) tem periodos diferentes");

		FormaDef fera = Jandirus.Core.Forms.Catalogo.Def("beast")!;
		var azul = new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(fera));
		Color roxo = ContornoAlterna(fera)!.Value;
		vis.AuraDaForma(azul, topo, roxo);        // par novo -> os dois relogios zeram juntos

		// MEIA VOLTA DA COR poe a cor na outra ponta. O pulso, num periodo diferente, tem que estar
		// no MEIO do caminho dele -- e e essa defasagem que faz as duas coisas serem lidas separadas.
		Correr(ciclo / 2);
		Color corLa = vis.ContornoNoMaterialDeTeste().Cor;
		float forcaLa = Forca();
		float distanciaDaCor = Mathf.Abs(corLa.R - roxo.R) + Mathf.Abs(corLa.G - roxo.G) + Mathf.Abs(corLa.B - roxo.B);
		Conferir(distanciaDaCor < 1f / 255,
				 $"na meia volta da COR a Fera esta no roxo ({corLa.ToHtml(false)})");
		Conferir(forcaLa > topo * piso + 0.02f && forcaLa < topo - 0.02f,
				 $"e o PULSO esta no meio do caminho dele ({forcaLa:0.###}, entre {topo * piso:0.###} "
			   + $"e {topo:0.###}) -- as duas animacoes nao andam coladas");

		_passos.Add($"  --     contorno: topo {topo:0.##}, fundo {topo * piso:0.###}, volta em {pulso:0.#}s "
				  + $"({tons.Count} degraus); olhos em 0; cor em {ciclo:0.#}s");

		// devolve o boneco apagado pra o resto da bancada
		vis.AuraDaForma(Colors.White, 0, null);
	}

	// =====================================================================
	// 3b. A FERA -- o Oozaru, do disco ate a tela
	// =====================================================================
	/// <summary>
	/// O OOZARU, MEDIDO PELO CAMINHO QUE O JOGO USA.
	///
	/// ============================ POR QUE ELE PRECISA DE BLOCO PROPRIO ============================
	/// O macaco e a unica forma do jogo que troca o CORPO por outra criatura e a unica cena que
	/// EMPRESTA a aura base do personagem antes de existir. As duas coisas quebram calado:
	///
	///   * um `.tres` que exista e nao esteja importado da uma folha sem textura -- e como a criatura
	///     APAGA o corpo de baixo (`CharacterVisual.Escondida`), o resultado nao e "um lutador
	///     normal", e um jogador INVISIVEL. Nenhum erro, nenhuma mensagem;
	///   * uma aura emprestada que nao seja devolvida deixa o macaco (ou, se a cena morrer antes, um
	///     lutador em forma base) brilhando pra sempre, sem nada no jogo que explique por que.
	///
	/// Cada checagem daqui diz, no comentario, COMO ela reprova se a regra sumir -- porque uma
	/// bancada que so passa nao prova nada, e esta ja deu verde tres vezes com o jogo quebrado.
	/// ==========================================================================================
	/// </summary>
	private void AFera(Node2D corpo, CharacterVisual vis, Aura aura)
	{
		// =====================================================================
		// 1. O SPRITE: NO DISCO **E** IMPORTADO
		// =====================================================================
		// ============================ EXISTIR NA PASTA NAO E EXISTIR PRO GODOT ============================
		// Este projeto ja pagou por isto com os SONS: os arquivos estavam na pasta, o codigo apontava
		// pros caminhos certos, e o Godot nunca os tinha importado -- silencio absoluto, zero erros.
		// Um `.tres` de SpriteFrames e pior que um som, porque ele NAO precisa de importacao: ele
		// carrega feliz mesmo quando o `.png` que ele referencia nunca virou `.ctex`. O que sai e uma
		// folha com 78 animacoes e nenhum pixel.
		//
		// Entao sao QUATRO perguntas diferentes, e nenhuma delas responde as outras:
		//   a) o `.tres` e carregavel;
		//   b) a textura que ELE referencia (lida do proprio arquivo, nao adivinhada) esta no disco;
		//   c) o Godot a importou (`ResourceLoader.Exists` do `.png` = ha `.import` + `.ctex`);
		//   d) o quadro tem tamanho de verdade -- e o de 96 do BYOND, que e o que faz a criatura ser
		//      criatura (a distincao e derivada do tamanho: `CharacterVisual.EhCriatura`).
		//
		// COMO REPROVA SE A REGRA SUMIR: apague `.godot/imported/` (ou mande o pipeline reescrever o
		// atlas sem reimportar) e (c) e (d) caem, enquanto (a) continua verde -- que e exatamente a
		// situacao que produz o jogador invisivel.
		// ============================================================================================
		foreach (string idMacaco in new[] { "oozaru", "oozaru_dourado" })
		{
			FormaDef mk = Jandirus.Core.Forms.Catalogo.Def(idMacaco)!;
			string folhaMk = CorposDeForma.Caminho(mk.Corpo, "") ?? "";
			Conferir(folhaMk.Length > 0, $"`{idMacaco}` aponta pra um corpo proprio");
			Conferir(CorposDeForma.Existe(folhaMk),
					 $"`{idMacaco}`: a folha carrega ({folhaMk.GetFile()})");

			// A TEXTURA SAI DO PROPRIO `.tres`, e nao de um `Replace(".tres", ".png")`: adivinhar o
			// nome faria a checagem procurar um arquivo que talvez nao seja o que a folha usa, e ela
			// passaria (ou reprovaria) por um motivo que nao tem nada a ver com o jogo.
			string arq = ProjectSettings.GlobalizePath(folhaMk);
			string texto = System.IO.File.Exists(arq) ? System.IO.File.ReadAllText(arq) : "";
			Conferir(texto.Length > 0, $"`{idMacaco}`: o .tres esta no disco ({arq.GetFile()})");

			var refs = System.Text.RegularExpressions.Regex
				.Matches(texto, "ext_resource[^\\n]*path=\"(res://[^\"]+)\"")
				.Select(m => m.Groups[1].Value).Distinct().ToList();
			Conferir(refs.Count > 0, $"`{idMacaco}`: a folha referencia alguma textura");

			foreach (string t in refs)
			{
				Conferir(System.IO.File.Exists(ProjectSettings.GlobalizePath(t)),
						 $"`{idMacaco}`: a arte esta NO DISCO ({t.GetFile()})");
				// O `.import` e o carimbo do Godot. Sem ele o `.png` e so um arquivo numa pasta.
				Conferir(System.IO.File.Exists(ProjectSettings.GlobalizePath(t) + ".import"),
						 $"`{idMacaco}`: a arte foi IMPORTADA (ha {t.GetFile()}.import)");
				Conferir(ResourceLoader.Exists(t), $"`{idMacaco}`: e o Godot a resolve ({t.GetFile()})");
				var tex = ResourceLoader.Load<Texture2D>(t);
				Conferir(tex != null && tex.GetWidth() > 0,
						 $"`{idMacaco}`: a arte tem pixel ({tex?.GetWidth() ?? 0}x{tex?.GetHeight() ?? 0})");
			}

			var folha = ResourceLoader.Load<SpriteFrames>(folhaMk);
			string[] nomes = folha?.GetAnimationNames() ?? [];
			Conferir(nomes.Length >= 40, $"`{idMacaco}`: a folha traz o repertorio inteiro ({nomes.Length} animacoes)");
			_passos.Add($"  --     `{idMacaco}`: {nomes.Length} animacoes em {folhaMk.GetFile()}");

			if (nomes.Length > 0 && folha!.GetFrameTexture(nomes[0], 0) is { } q0)
				Conferir(q0.GetWidth() == 96 && q0.GetHeight() == 96,
						 $"`{idMacaco}`: o quadro e o de 96 do BYOND ({q0.GetWidth()}x{q0.GetHeight()})");
			else Conferir(false, $"`{idMacaco}`: o primeiro quadro tem textura");
		}

		// =====================================================================
		// 2. A CENA: A FOLHA DA AURA, MEDIDA NO NODE, COM O NODE ENVENENADO ANTES
		// =====================================================================
		// ============================ ENVENENAR E O QUE FAZ ESTA CHECAGEM VALER ============================
		// O node `Aura` NASCE na folha base (`SpriteDeAura._folha = FolhaBase`). Uma checagem que so
		// rodasse a cena e perguntasse "a folha e a base?" passaria com o `aura.Folha(...)` do
		// `AcenderAuraBase` DELETADO -- ela estaria medindo o valor de fabrica, nao a regra.
		//
		// Por isso a folha e trocada pra a DOURADA do SSJ antes (que existe, e usada de verdade e nao
		// se tinge), e so entao a cena roda. Se `AcenderAuraBase` parar de escolher a folha, a cena
		// acende com a folha dourada e as tres linhas abaixo reprovam.
		//
		// O QUE MUDOU DE ALVO. O pedido do dono era textual contra uma ARTE: *"ativar a aura base dele
		// (NAO E O AURA BIG COMBINED)"*, e a `Aurabigcombined` era plausivel de ser escolhida por engano.
		// Ela nao e mais: nenhum node do jogo consegue chega-la (ver o bloco no fim desta secao). O que
		// este veneno cobra hoje e a escolha entre as TRES folhas vivas -- se `AcenderAuraBase` parar de
		// escrever a folha, a cena do Oozaru acende na dourada do SSJ e a linha da folha reprova.
		// ============================================================================================
		aura.Apagar();
		aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Ssj);
		Conferir(aura.DesenhoDeTeste.FolhaDeTeste == SpriteDeAura.FolhaSsj,
				 "o veneno pegou (a aura esta na folha dourada antes da cena)");

		FormaDef macaco = Jandirus.Core.Forms.Catalogo.Def("oozaru")!;
		Jandirus.Core.Forms.Cinematica cenaFera = Jandirus.Core.Forms.Cinematicas.Oozaru;

		int presosAntes = Transformacao.PresosDeTeste;
		var tf = Transformacao.Rodar(corpo.GetParent(), corpo, macaco, cenaFera, souEu: true);
		tf.SetProcess(false);   // o relogio e nosso, como no resto desta bancada

		// =====================================================================
		// 3. ACENDE ENQUANTO E GENTE, APAGA NO INSTANTE EM QUE VIRA BICHO
		// =====================================================================
		// ============================ A ORDEM E O TESTE, NAO O ESTADO FINAL ============================
		// O dono descreveu uma SEQUENCIA: *"a aura do personagem vai ativar (mesmo sem controle bom de
		// ki) so pra cinematica e ele vai virar o oozaru e nesse momento a aura desativa"*. Conferir
		// so o fim ("apagada no final") passaria com uma cena que nunca acende; conferir so o comeco
		// ("acende") passaria com uma cena que nunca apaga -- e a metade que nao apaga e a que da
		// defeito silencioso, porque ela so aparece DEPOIS.
		//
		// Entao o relogio e bombeado quadro a quadro e se anota QUANDO cada coisa acontece. A pergunta
		// e feita ao node `Aura` (quem fica aceso na tela) e nao ao `AuraBaseDeTeste` do tocador: o
		// tocador ja morreu quando a cena acaba, e um flag que morre junto nao prova nada.
		// ==========================================================================================
		bool acesaComoGente = false, acesaComoBicho = false;
		double virouEm = -1, apagouEm = -1;
		string folhaNaCena = "";
		for (double k = 0; k < cenaFera.Segundos + 1 && IsInstanceValid(tf); k += 0.1)
		{
			tf._Process(0.1);
			bool acesa = aura.AcesaDeTeste;
			if (acesa && folhaNaCena.Length == 0) folhaNaCena = aura.DesenhoDeTeste.FolhaDeTeste;

			if (vis.EhCriatura)
			{
				if (virouEm < 0) virouEm = k;
				if (acesa) acesaComoBicho = true;
			}
			else if (acesa) acesaComoGente = true;

			if (!acesa && acesaComoGente && apagouEm < 0) apagouEm = k;
		}

		_passos.Add($"  --     cena do Oozaru: aura acesa ate {apagouEm:0.0}s, macaco em {virouEm:0.0}s, "
				  + $"folha `{folhaNaCena.GetFile()}`");

		Conferir(acesaComoGente, "a aura BASE acende ANTES do macaco (o dono: 'a aura vai ativar')");
		Conferir(virouEm >= 0, "a cena instala a criatura (o beat `Assumir`)");
		Conferir(!acesaComoBicho,
				 "e ela APAGA no instante em que o corpo vira ('nesse momento a aura desativa')");
		Conferir(!aura.AcesaDeTeste, "no fim da cena a aura esta apagada (senao brilha pra sempre)");
		// APAGAR **NO** INSTANTE, e nao um quadro qualquer depois: as duas coisas acontecem dentro do
		// mesmo `Assumir`, entao o tempo medido tem que ser o mesmo. Sem esta linha, apagar a aura
		// tres segundos depois de o macaco nascer passaria nas de cima.
		Conferir(virouEm >= 0 && apagouEm >= 0 && Mathf.Abs(virouEm - apagouEm) < 0.15,
				 $"apaga NO MESMO instante em que o macaco nasce (aura {apagouEm:0.0}s, macaco {virouEm:0.0}s)");

		// A FOLHA, medida no quadro em que a cena estava acesa.
		Conferir(folhaNaCena == SpriteDeAura.FolhaBase,
				 $"a cena acende na folha COLORIVEL e nao na dourada envenenada ({folhaNaCena.GetFile()})");
		Conferir(SpriteDeAura.FolhaBase.Contains("colorablebigaura"),
				 $"e a folha base continua sendo a `colorablebigaura` ({SpriteDeAura.FolhaBase.GetFile()})");
		// ============================ AS DUAS LINHAS DA `Aurabigcombined` SAIRAM ============================
		// Havia aqui um `!folhaNaCena.Contains("Aurabigcombined")` (o pedido textual do dono, *"NAO E O
		// AURA BIG COMBINED"*) e, embaixo dele, um `ResourceLoader.Exists` provando que a primeira nao
		// media contra um fantasma. A segunda era honesta e e ela que condena as duas: a arte deixou de
		// ter QUALQUER caminho no C# quando a chama da cinematica passou a usar a aura da propria forma
		// -- nenhum node do jogo consegue mais escolhe-la, nem por engano.
		//
		// Uma checagem que nao pode falhar nao esta protegendo nada, e o que ela protegia continua
		// coberto pela linha logo acima (a folha da cena tem que ser a COLORIVEL, medida com o node
		// envenenado na dourada antes). Quem cobre a escolha da folha da CENA agora sao as tres
		// medidas novas -- ver `ChamaDaCenaDeTeste` no bloco da cinematica e no `AAparenciaInteiraDoDegrau`.
		// ================================================================================================

		Conferir(Transformacao.PresosDeTeste == presosAntes,
				 $"a cena do macaco devolveu a vez do corpo (donos {Transformacao.PresosDeTeste}, base {presosAntes})");
		if (IsInstanceValid(tf)) tf.QueueFree();

		// =====================================================================
		// 4. O MACACO E DESENHADO EM TODA POSE QUE O SERVIDOR SABE MANDAR
		// =====================================================================
		// ============================ O JOGADOR INVISIVEL ============================
		// A camada da criatura APAGA o corpo de baixo. Entao "a camada nao ligou" nao devolve o
		// lutador normal: nao desenha nada. E o jeito de ela nao ligar e uma pose sem substituta --
		// o macaco tem `flight_south` e o corpo em voo esta em `flight_mov_south`, entao quem olhasse
		// pra lua VOANDO caia numa guarda que ja foi deletada por causa disto.
		//
		// A varredura passa pelo `SetPose`, que e o MESMO metodo que traduz o `Protocol.Pose` vindo do
		// servidor -- percorrer o enum inteiro e o que garante que nenhuma pose ficou de fora, hoje ou
		// no dia em que uma sétima entrar nele. Vezes as quatro direcoes, vezes parado/andando.
		//
		// COMO REPROVA SE A REGRA SUMIR: reponha a guarda `frames.HasAnimation(...)` no
		// `CorpoDaForma`, ou tire uma familia da cadeia de substitutas do `Escolher`, e alguma das
		// combinacoes abaixo aparece com a camada apagada ou sem animacao.
		// ==========================================================================
		// ============================ PRIMEIRO O INSTANTE EM QUE A CAMADA NASCE ============================
		// Ha DOIS momentos em que o macaco pode deixar de ser desenhado, e eles falham por motivos
		// diferentes: a camada NASCENDO (troca de corpo) e a camada MUDANDO DE POSE. A varredura que
		// so instala o macaco parado e depois troca a pose cobre o segundo e passa por cima do
		// primeiro -- por isso aqui a camada nasce de novo A PARTIR de cada pose, que e a ordem que o
		// jogo faz: o corpo ja esta numa pose quando o pacote do Oozaru chega.
		//
		// ============================ O QUE EU NAO CONSEGUI REPRODUZIR, DITO ============================
		// O comentario de `CharacterVisual.CorpoDaForma` conta que a guarda velha
		// (`frames.HasAnimation(_corpo.Animation)` em volta do `Aplicar`) matava o desenho de quem
		// olhasse pra lua VOANDO, porque o corpo estaria em `flight_mov_south` e o macaco so tem
		// `flight_south`. Eu repus aquela guarda de proposito pra ver esta checagem reprovar, e ela
		// NAO reprovou: a folha do corpo em uso (`NewPaleMale.tres`) tem `flight_east`/`flight_south`
		// -- sem o sufixo `_mov` --, e o `Escolher` tenta `{fam}_{dir}` ANTES do apelido `_mov`. Com
		// os assets de hoje aquela guarda nao morde por nenhuma das 24 entradas.
		//
		// O que reprova de verdade (conferido): inverter o `Escondida` pra a propria camada de forma.
		// As duas varreduras acusam 24 e 48. Entao esta checagem esta ligada e morde -- so nao pelo
		// caminho que o comentario historico descreve, e ficar calado sobre isso deixaria a proxima
		// pessoa confiando numa protecao que ela nunca viu funcionar.
		// ==============================================================================================
		// ============================ "TEM ANIMACAO" NAO E "TEM PIXEL" ============================
		// A primeira versao desta checagem perguntava se a camada tinha `Visible` e um nome de
		// animacao nao-vazio -- e passou VERDE com a guarda velha reposta de proposito. O motivo e o
		// Godot: um `AnimatedSprite2D` recem-criado nasce visivel e com `Animation = "default"`. O
		// macaco nao TEM `default` (as animacoes dele sao `default_south`, `walk_east`...), entao a
		// camada estava visivel, com nome, e desenhando NADA -- que e exatamente o jogador invisivel
		// que esta checagem existe pra pegar, passando por cima dela.
		//
		// A pergunta certa e se o nome escolhido EXISTE NA FOLHA. E ela e feita a folha carregada do
		// caminho do catalogo, nao a uma lista escrita aqui.
		// ======================================================================================
		var folhaMacaco = ResourceLoader.Load<SpriteFrames>(CorposDeForma.Caminho(macaco.Corpo, "")!);
		Conferir(folhaMacaco != null, "a folha do macaco carregou pra a varredura de poses");

		bool Desenhando() => vis.CorpoDaFormaVisivelDeTeste
			&& folhaMacaco != null && folhaMacaco.HasAnimation(vis.PoseDoCorpoDaFormaDeTeste);

		int nasceuApagado = 0;
		string piorNascimento = "";
		foreach (Jandirus.Net.Protocol.Pose p in Enum.GetValues<Jandirus.Net.Protocol.Pose>())
			foreach (Jandirus.Core.World.Facing f in Enum.GetValues<Jandirus.Core.World.Facing>())
			{
				vis.CorpoDaForma(CorpoDeForma.Nenhum);
				vis.SetPose(p);
				vis.SetMotion(f, moving: true);
				vis.CorpoDaForma(macaco.Corpo);   // a lua o pega NESTA pose

				if (Desenhando()) continue;
				nasceuApagado++;
				if (piorNascimento.Length == 0)
					piorNascimento = $"{p}/{f}: corpo em `{vis.PoseDeTeste}`, macaco em "
								   + $"`{vis.PoseDoCorpoDaFormaDeTeste}`";
			}
		Conferir(nasceuApagado == 0,
				 $"o macaco NASCE desenhado venha de que pose vier ({nasceuApagado} apagados, ex.: {piorNascimento})");

		vis.CorpoDaForma(macaco.Corpo);
		Conferir(vis.EhCriatura, "o boneco esta de macaco pra a varredura de poses");

		int semDesenho = 0, semAnimacao = 0, corpoVazando = 0, combinacoes = 0;
		string piorCaso = "";
		foreach (Jandirus.Net.Protocol.Pose p in Enum.GetValues<Jandirus.Net.Protocol.Pose>())
			foreach (Jandirus.Core.World.Facing f in Enum.GetValues<Jandirus.Core.World.Facing>())
				foreach (bool andando in new[] { false, true })
				{
					vis.SetPose(p);
					vis.SetMotion(f, andando);
					combinacoes++;

					if (!Desenhando())
					{
						semDesenho++;
						if (piorCaso.Length == 0)
							piorCaso = $"{p}/{f}/{(andando ? "andando" : "parado")} em "
									 + $"`{vis.PoseDoCorpoDaFormaDeTeste}`";
					}
					if (vis.PoseDoCorpoDaFormaDeTeste.Length == 0) semAnimacao++;
					// E O CORPO DE BAIXO CONTINUA APAGADO em toda pose: era `Aplicar` quem reacendia
					// as camadas a cada troca, e a regra da criatura durava ate o proximo passo.
					if (vis.CorpoBaseVisivelDeTeste) corpoVazando++;
				}

		_passos.Add($"  --     {combinacoes} combinacoes de pose x direcao x movimento no macaco");
		Conferir(combinacoes >= 40, $"a varredura cobriu o enum inteiro ({combinacoes} combinacoes)");
		Conferir(semDesenho == 0, $"o macaco e DESENHADO em toda pose ({semDesenho} apagadas, ex.: {piorCaso})");
		Conferir(semAnimacao == 0, $"e sempre com uma animacao escolhida ({semAnimacao} sem)");
		Conferir(corpoVazando == 0, $"e o corpo de 32 px nunca reaparece por baixo ({corpoVazando} vezes)");

		// --- devolve o boneco como ele estava, pra o resto da bancada e pra a foto ---
		vis.SetPose(Jandirus.Net.Protocol.Pose.Normal);
		vis.SetMotion(Jandirus.Core.World.Facing.South, moving: false);
		vis.CorpoDaForma(CorpoDeForma.Nenhum);
		// `null` E "SEM FORMA NENHUMA" -- desfaz sprite, tinta de cabelo e tinta de rabo de uma vez.
		// Eram as duas linhas `CabeloDaForma("")` + `PintarCabelo(null)`.
		vis.VestirCabeloDaForma(null);
		vis.AuraDaForma(Colors.White, 0, null);
		corpo.GetNodeOrNull<RaiosDaForma>("Raios")?.Definir(false, Colors.White, 0);
		aura.Apagar();
		aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Base);
		Conferir(!vis.EhCriatura && vis.CorpoBaseVisivelDeTeste,
				 "e sair do macaco devolve o corpo de gente pra o resto da bancada");
	}

	/// <summary>
	/// O passo em que a bancada PARA e espera a cinematica correr.
	///
	/// E o valor PRE-incremento: a guarda de tempo o compara direto, e o `case` compara `+1`.
	/// Ver o comentario da guarda.
	/// </summary>
	private const int PassoDaEspera = 8;

	/// <summary>
	/// Quantos segundos o passo <see cref="PassoDaEspera"/> espera. Escrito por quem cria a cena de
	/// teste, a partir da duracao DELA -- ver a guarda em `_Process`. O valor inicial so cobre o caso
	/// de a cena nao chegar a ser criada (ai o proprio `CenaDepois` reprova).
	/// </summary>
	private double _esperaDaCena = 6.5;

	private Transformacao? _cena;
	private CharacterVisual? _visDaCena;
	private bool primeiraAura = true;

	/// <summary>
	/// CONFERE A CENA DEPOIS QUE ELA CORREU. Chamado num passo posterior -- o tocador precisa de
	/// tempo real pra andar, e medir no mesmo quadro em que se cria mediria zero.
	/// </summary>
	private void CenaDepois()
	{
		if (_cena == null) { Conferir(false, "a cena foi criada"); return; }
		if (IsInstanceValid(_cena))
			_passos.Add($"  --     cena viva: t={_cena.TempoDeTeste:0.##}s, beats={_cena.BeatsDeTeste}, "
					  + $"acabou={_cena.AcabouDeTeste}, preso={Transformacao.PrendendoOCorpo}");
		// A CENA JA DEVE TER MORRIDO SOZINHA -- ela se libera no fim (`QueueFree`). Se ainda estiver
		// viva depois de `_esperaDaCena` (a duracao dela mais 1,5 s), o tocador travou.
		Conferir(!IsInstanceValid(_cena), "a cena terminou e se liberou sozinha");
		Conferir(!Transformacao.PrendendoOCorpo,
				 "a cena SOLTOU o corpo (senao o jogador fica paralisado pra sempre)");
		// A TRANCA TAMBEM TEM QUE ABRIR -- e por dois caminhos: o prazo (`SegundosPreso`) e o
		// `_ExitTree`. Uma tranca que nao abre e pior que o defeito: o corpo nunca mais anda.
		if (_visDaCena != null)
		{
			Conferir(!_visDaCena.PoseTravadaDeTeste, "a cena DESTRANCOU a pose");
			_visDaCena.SetMotion(Jandirus.Core.World.Facing.East, moving: true);
			Conferir(_visDaCena.PoseDeTeste.StartsWith("walk"),
					 $"depois da cena o corpo VOLTA a andar (pose {_visDaCena.PoseDeTeste})");
		}
	}

	// =====================================================================
	// 5b. A ESCADA DA CENA DO SSJ3 -- NO ROTEIRO
	// =====================================================================
	/// <summary>
	/// A CENA DO SSJ3 VESTE BASE -> SSJ1 -> SSJ2 NOS INSTANTES DAS TRES FALAS.
	///
	/// ============================ O QUE ESTE BLOCO PROVA, E O QUE ELE NAO PROVA ============================
	/// Aqui e o ROTEIRO: que as tres bandeiras `Efeito.VesteDegrau` estao nos beats certos, que os
	/// beats certos sao os que FALAM, e que a escada que o Core deriva tem os tres degraus na ordem.
	/// Quem prova que a bandeira vira PIXEL e o <see cref="AEscadaDoSsj3AoVivo"/>, que dirige o
	/// `Transformacao` de verdade e le o cabelo do boneco. Os dois juntos fecham a regra; sozinho,
	/// nenhum dos dois vale -- um roteiro perfeito com o tocador desligado passa aqui, e um tocador
	/// perfeito com a bandeira no beat errado passa la.
	///
	/// ============================ POR QUE O PAREAMENTO E REFEITO AQUI ============================
	/// O `VestirODegrauSeguinte` guarda um CONTADOR: o beat nao diz qual degrau quer, ele diz "o
	/// proximo". Entao "qual degrau cai em qual fala" nao esta escrito em lugar nenhum -- e a
	/// composicao de duas coisas (a ordem dos beats e a ordem da escada). Este bloco refaz essa
	/// composicao pra poder afirmar o pareamento; a checagem de que o tocador a faz IGUAL mora no
	/// bloco ao vivo, que nao refaz nada.
	///
	/// ============================ AS TRES REGRAS SAO AS TRES DO DM ============================
	/// `SSJ3Cinematic.dm:12-32` sao tres atos identicos em forma: um `updateOverlay` seguido de um
	/// `say`. Nao ha ali um `updateOverlay` mudo nem uma fala sem troca de cabelo -- e e por isso que
	/// "todo beat que veste, fala" e afirmavel. O `overlayList += 'Elec.dmi'` da linha :29 e o
	/// terceiro ato, e por isso a faisca nasce no degrau do SSJ2 e nao antes.
	///
	/// COMO CADA UMA REPROVA SE A REGRA SUMIR:
	///   * mova o `| Efeito.VesteDegrau` do beat da fala "Isto e um Super Saiyajin" pro beat seguinte
	///     ("Ou voce pode simplesmente chamar de Super Saiyajin 2") -- o pareamento anda um beat, o
	///     SSJ1 passa a ser vestido na fala errada e a linha do `ssj1` cai com a fala impressa;
	///   * tire a bandeira de qualquer um dos tres e a contagem deixa de bater com `escada.Length`
	///     (que e derivado do catalogo, entao um degrau novo na linha Saiyajin tambem cobra beat novo);
	///   * tire o `Efeito.Raios` do terceiro e a linha do `Elec.dmi` cai -- ela e derivada do
	///     `Catalogo.Def(degrau).Raios`, entao ela segue o catalogo e nao uma lista escrita aqui;
	///   * faca o `Encurtar` DESCARTAR beats em vez de comprimir o relogio e a curta perde degraus:
	///     a versao que o jogador ve na maioria das vezes mostraria o SSJ3 nascendo do nada.
	/// ====================================================================================================
	/// </summary>
	private void AEscadaDoSsj3NoRoteiro()
	{
		FormaDef? alvo = Jandirus.Core.Forms.Catalogo.Def("ssj3");
		if (alvo == null) { Conferir(false, "a forma `ssj3` existe pra a escada da cena"); return; }

		// --- 1. A ESCADA, derivada do catalogo ---
		FormaDef[] escada = Jandirus.Core.Forms.Cinematicas.EscadaDaCena(alvo);
		string trilha = string.Join(" -> ", escada.Select(d => d.Id));
		Conferir(escada.Length == 3
			  && escada[0].Id == Jandirus.Core.Forms.Catalogo.IdBase
			  && escada[1].Id == "ssj1" && escada[2].Id == "ssj2",
				 $"a escada da cena do SSJ3 e base -> ssj1 -> ssj2 ({trilha})");

		// --- 2. O PAREAMENTO, nas DUAS versoes que o funil entrega ---
		void Parear(Jandirus.Core.Forms.DegrauDeCena g, bool comFala, string rotulo)
		{
			if (Jandirus.Core.Forms.Cinematicas.NoDegrau(alvo, g) is not { } c)
			{
				Conferir(false, $"{rotulo}: o funil `NoDegrau` entrega a cena do SSJ3");
				return;
			}

			var pares = new List<(FormaDef Degrau, Jandirus.Core.Forms.Beat B)>();
			int ordem = 0, ultimoIndice = -1;
			for (int i = 0; i < c.Beats.Length; i++)
			{
				if (!c.Beats[i].Faz.HasFlag(Jandirus.Core.Forms.Efeito.VesteDegrau)) continue;
				ultimoIndice = i;
				if (ordem < escada.Length) pares.Add((escada[ordem], c.Beats[i]));
				ordem++;
			}

			Conferir(ordem == escada.Length,
					 $"{rotulo}: veste os {escada.Length} degraus da escada ({ordem} beat(s) com a bandeira)");
			if (ordem != escada.Length) return;

			// OS TRES SAO A ABERTURA DA CENA, e nao tres beats espalhados. No DM os `updateOverlay`
			// acontecem antes de qualquer `sleep` grande (`:12-32`), e essa ordem e o que faz a
			// escada ser um crescendo em vez de um piscar no meio do grito.
			Conferir(ultimoIndice == escada.Length - 1,
					 $"{rotulo}: e eles sao os {escada.Length} PRIMEIROS beats (o ultimo caiu no indice {ultimoIndice})");

			_passos.Add($"  --     {rotulo}: " + string.Join(" | ",
						pares.Select(p => $"{p.B.Em:0.#}s -> {p.Degrau.Id}")));

			// A FAISCA NASCE NO DEGRAU QUE TEM FAISCA -- `overlayList += 'Elec.dmi'` (`:29`), que cai
			// no ato do SSJ2. Derivado do catalogo: o dia em que o dono der raio ao SSJ1, o beat dele
			// passa a ser cobrado sozinho.
			foreach ((FormaDef d, Jandirus.Core.Forms.Beat b) in pares)
			{
				bool temFaisca = b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.Raios);
				Conferir(temFaisca == d.Raios > 0,
						 $"{rotulo}: o beat que veste `{d.Id}` {(d.Raios > 0 ? "ACENDE" : "nao acende")} "
					   + $"faisca, como o catalogo dele ({d.Raios})");
			}

			if (!comFala)
			{
				// A CURTA NAO FALA -- e e o `Encurtar` que apaga (ninguem explica pela terceira vez o
				// que e um Super Saiyajin). O que importa e que ela guardou os DEGRAUS mesmo assim: a
				// versao encurtada e a que o jogador ve na maioria das vezes.
				Conferir(pares.All(p => p.B.Fala.Length == 0),
						 $"{rotulo}: e sem falas (o `Encurtar` as apaga e mantem os degraus)");
				return;
			}

			// TODO BEAT QUE VESTE, FALA. Um `updateOverlay` mudo nao existe no DM.
			Conferir(pares.All(p => p.B.Fala.Length > 0),
					 $"{rotulo}: todo beat que veste um degrau tambem FALA");

			// ============================ E A FALA E A DAQUELE DEGRAU ============================
			// As tres palavras-chave sao as tres falas do DM traduzidas. Casar por TEXTO e de
			// proposito: a fala e o degrau sao um par, e reescrever uma sem olhar a outra e
			// exatamente o desencontro que este projeto ja teve (o texto prometia a escada e a tela
			// mostrava o mesmo cabelo). Retraduzir a cena reprova aqui, e deve mesmo.
			//
			// O `ssj1` NAO pode ser casado so por "Super Saiyajin": as tres falas tem essa
			// expressao. O que separa a segunda da terceira e a ASCENSAO -- e por isso a do SSJ1 e
			// definida pela ausencia dela.
			// ================================================================================
			bool Diz(string s, string chave) => s.Contains(chave, StringComparison.OrdinalIgnoreCase);
			foreach ((FormaDef d, Jandirus.Core.Forms.Beat b) in pares)
			{
				bool casa = d.Id switch
				{
					"ssj1" => Diz(b.Fala, "Super Saiyajin") && !Diz(b.Fala, "ascend"),
					"ssj2" => Diz(b.Fala, "ascend"),
					_ => Diz(b.Fala, "normal"),   // o `RemoveHair()` de `:12`, o "meu estado normal"
				};
				Conferir(casa, $"{rotulo}: `{d.Id}` e vestido na fala dele, aos {b.Em:0.#}s "
							 + $"(\"{(b.Fala.Length > 44 ? b.Fala[..44] + "..." : b.Fala)}\")");
			}
		}

		Parear(Jandirus.Core.Forms.DegrauDeCena.Estreia, comFala: true, "roteiro do SSJ3");
		Parear(Jandirus.Core.Forms.DegrauDeCena.Curta, comFala: false, "roteiro do SSJ3 curto");

		// ============================ E SO O SSJ3 VESTE DEGRAU ============================
		// Nao porque seja proibido -- a `EscadaDaCena` foi escrita justamente pra qualquer cena longa
		// futura poder usa-la --, mas porque hoje ela e a unica, e uma bandeira que aparecesse em
		// outra cena sem ninguem pedir e sinal de copiar-e-colar de roteiro. Se o dono mandar a cena
		// do Blue mostrar a escada dela um dia, esta linha e o lugar de registrar a decisao.
		// ==============================================================================
		var vestem = Jandirus.Core.Forms.Cinematicas.Todas
			.Where(c => c.Beats.Any(b => b.Faz.HasFlag(Jandirus.Core.Forms.Efeito.VesteDegrau)))
			.Select(c => c.Forma).ToArray();
		Conferir(vestem.Length == 1 && vestem[0] == "ssj3",
				 $"a escada mostrada e exclusiva da cena do SSJ3 ({string.Join(", ", vestem)})");
	}

	// =====================================================================
	// 6. AS TRES CORES, PERCORRENDO O CATALOGO INTEIRO
	// =====================================================================
	/// <summary>
	/// A QUE GRUPO DE COR CADA FORMA PERTENCE **PELO ENUNCIADO DO DONO**, e nao pela derivacao que a
	/// `Catalogo.CorDoContorno` usa. Nulo = "esta segue a propria aura".
	///
	/// ============================ POR QUE ELE E ESCRITO A MAO ============================
	/// Perguntar a `CorDoContorno` a que grupo a forma pertence seria conferir a funcao com ela
	/// mesma: qualquer regra passaria, inclusive a errada. Aqui esta a fonte INDEPENDENTE -- amarelo
	/// na escada Saiyajin menos o Limit Breaker, verde no Legendary, azul no Blue, rosa no Rose,
	/// branco no UI, roxo no UE, e as duas cores da Fera so no Beast.
	///
	/// A `base` sai nula de proposito: ela e da linha Saiyajin no catalogo, mas nao e transformacao
	/// nenhuma -- e o `d.Id != IdBase` da regra e o que a manda pra a aura. Os dois SSG tambem, e
	/// pelo motivo oposto: o ki divino sobre a forma base e VERMELHO, nao um Blue mais fraco.
	///
	/// ============================ POR QUE ELE VIROU METODO ============================
	/// Dois blocos precisam da MESMA divisao: o <see cref="AsCoresNoCatalogoInteiro"/> (que afirma um
	/// tom so por grupo) e o <see cref="AsCoresNaoSaoAAura"/> (que envenena a aura e cobra que so o
	/// lado de fora da regra a acompanhe). Escrita duas vezes, a segunda copia envelheceria calada no
	/// dia em que uma linha nova entrasse -- e o pior e que ela envelheceria pra MENOS: uma forma
	/// esquecida cairia no "segue a aura" e o veneno passaria a ser exigido nela.
	/// ==============================================================================
	/// </summary>
	private static string? GrupoDoEnunciado(FormaDef d) => d.Linha switch
	{
		Jandirus.Core.Forms.LinhaDeForma.Saiyajin or Jandirus.Core.Forms.LinhaDeForma.Futuro
			when d.Id == Jandirus.Core.Forms.Catalogo.IdBase => null,
		Jandirus.Core.Forms.LinhaDeForma.Saiyajin or Jandirus.Core.Forms.LinhaDeForma.Futuro =>
			d.Id == "ssj4_limit_breaker" ? "vermelho" : "amarelo",
		Jandirus.Core.Forms.LinhaDeForma.Legendary
			or Jandirus.Core.Forms.LinhaDeForma.LegendaryPrimal => "verde",
		// Os dois SSG ficam FORA: o ki divino sobre a base e vermelho, nao um Blue fraco.
		Jandirus.Core.Forms.LinhaDeForma.GodKi => d.Id == "ssg" ? null : "azul",
		Jandirus.Core.Forms.LinhaDeForma.GodKiRose => d.Id == "rose_ssg" ? null : "rosa",
		Jandirus.Core.Forms.LinhaDeForma.UltraInstinct => "branco",
		Jandirus.Core.Forms.LinhaDeForma.UltraEgo => "roxo",
		// SO O BEAST da linha Prodigial: os dois Misticos ficam abaixo do corte do ki divino
		// maduro e seguem a aura.
		Jandirus.Core.Forms.LinhaDeForma.Mistico => d.Id == "beast" ? "fera" : null,
		_ => null,   // Oozaru: ponto de excecao ainda aberto, segue a aura
	};

	/// <summary>
	/// ============================ POR QUE UMA LISTA A MAO NAO BASTA ============================
	/// O bloco de cores que ja existe la em cima (`o contorno de X e AMARELO`) confere OITO ids
	/// escritos a mao. Ele prova que os oito estao certos e nao prova NADA sobre o nono: um degrau
	/// novo na escada nasce fora da lista, sai com a cor errada e a bancada continua verde. E a falha
	/// assinatura deste projeto -- o dado escrito e ninguem cobrando.
	///
	/// Aqui as entradas passam TODAS pelas mesmas duas funcoes que o `World` chama, e o que se afirma
	/// e a REGRA e nao os valores:
	///
	///   a) toda entrada devolve hexa de 6 (`new Color("xyz")` e erro em jogo, e ele estoura dentro
	///      do `AoMudarForma`, no meio de uma luta);
	///   b) UM TOM SO POR LINHA -- a escada Saiyajin inteira num amarelo, o Legendary inteiro num
	///      verde, as duas escadas divinas num azul e num rosa, o UI num branco, o UE num roxo;
	///   c) o `ssj4_limit_breaker` e o grupo do VERMELHO, e ele tem exatamente um membro;
	///   d) quem ficou de fora da regra (a `base`, os dois SSG, os dois Misticos e o Oozaru) segue a aura;
	///   e) a faisca de cada entrada sai EXATAMENTE como o <see cref="FaiscaDoEnunciado"/> manda --
	///      azul nas escadas de sangue, vermelha no Limit Breaker, e a aura no resto;
	///   f) UMA SO entrada do catalogo inteiro OSCILA, e e o Beast -- e ela oscila entre estes dois
	///      hexas. Esta e a checagem que impede a oscilacao de escapar pra a linha inteira: o corte do
	///      `GodkiRoyalePct` e o que segura os dois Misticos fora, e sem isto trocar ele por um
	///      `LinhaDeForma.Mistico` seco poria os dois degraus a trocar de cor sem reprovar nada.
	///
	/// O GRUPO DE CADA FORMA ESTA ESCRITO A MAO no <see cref="GrupoDoEnunciado"/>, a partir do que o
	/// dono ditou -- e nao lido da `CorDoContorno`. Perguntar a funcao a que grupo a forma pertence
	/// seria conferir a funcao com ela mesma: qualquer regra passaria, inclusive a errada.
	///
	/// E ESTE BLOCO NAO PROVA QUE A COR NAO E A AURA. Ele confere que o grupo tem UM tom, e um grupo
	/// de um membro so (o vermelho, a fera) tem um tom por definicao -- inclusive se a funcao tiver
	/// caido no `_ => d.Aura`. Quem cobra isso e o <see cref="AsCoresNaoSaoAAura"/>, envenenando.
	///
	/// COMO CADA UMA REPROVA SE A REGRA SUMIR:
	///   (b) troque qualquer constante de linha por `d.Aura` em `CorDoContorno` e os degraus daquela
	///       linha voltam a ter um tom cada (`ffcf3a`, `ffc21f`, `fff08a`... no amarelo; `76ff7a`,
	///       `00ff2a`, `4aff0a`... no verde): o grupo deixa de ter 1 tom, e a mensagem os imprime;
	///   (c) apague o `PedeGodKi < 0` da guarda e o Limit Breaker doura -- o grupo do vermelho passa
	///       a ter o tom do amarelo dentro dele, e a checagem exige o `ff2d2f`;
	///   (d) o corte do `OrdemDoKiSobreOSuperSaiyajin` e o que mantem o SSG vermelho. Troque o
	///       `>= 20` por `>= 10` e os dois SSG entram no azul/rosa: eles caem no grupo "pela aura"
	///       escrito aqui, e a divergencia sai com id e cor;
	///   (e) devolva o `_ => d.Aura` pra a linha Legendary Primal e as duas faiscas do Primal voltam a
	///       sair VERDES (o estado em que este projeto ESTEVE): as duas aparecem com id e cor. Troque
	///       a guarda do vermelho por `LinhaDeForma.LegendaryPrimal` junto e o Limit Breaker primal
	///       fica vermelho -- ele nem tem raio, o que faria a cor errada ser invisivel ate alguem lhe
	///       dar `Raios`;
	///   (f) devolva `null` no segundo membro do par do Beast e a lista de oscilantes fica vazia (o
	///       contorno dele trava no azul, que e o defeito mais provavel desta feature: ele nao da
	///       erro nenhum e so aparece pra quem fica olhando um Beast por quatro segundos).
	/// ======================================================================================
	/// </summary>
	private void AsCoresNoCatalogoInteiro()
	{
		// LITERAIS DE PROPOSITO, como no bloco do rabo: mudar a cor tem que obrigar a passar aqui.
		// Uma checagem escrita como `== AmareloSaiyajin` passaria com qualquer valor, inclusive um
		// trocado por engano.
		const string Amarelo = "ffd24a", Vermelho = "ff2d2f", Verde = "4dff5a";
		const string AzulDivino = "3ad2ff", Rosa = "ff7ac6", Branco = "f0f6ff", Roxo = "a95cff";
		const string AzulDaFaisca = "8fe3ff";
		const string AzulDaFera = "3f8cff", RoxoDaFera = "b163ff";

		// GRUPO -> os tons que sairam nele, e quem entrou. A afirmacao e "UM tom por grupo, e e este".
		var tonsDoGrupo = new Dictionary<string, HashSet<string>>();
		var idsDoGrupo = new Dictionary<string, List<string>>();

		var pelaAura = new List<string>();
		var oscilam = new List<string>();
		var tonsDaOscilacao = new HashSet<string>();
		var contornoDivergente = new List<string>();
		var faiscaErrada = new List<string>();
		var tonsDaFaisca = new HashSet<string>();
		var faiscaPelaAura = new List<string>();
		int semHexa = 0;

		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			string contorno = Jandirus.Core.Forms.Catalogo.CorDoContorno(d);
			string faisca = Jandirus.Core.Forms.Catalogo.CorDosRaios(d);
			if (contorno.Length != 6 || faisca.Length != 6)
			{
				semHexa++;
				Conferir(false, $"`{d.Id}`: contorno e faisca em hexa de 6 (deu {contorno}/{faisca})");
			}

			string? grupo = GrupoDoEnunciado(d);

			if (grupo != null)
			{
				if (!tonsDoGrupo.TryGetValue(grupo, out HashSet<string>? s))
					tonsDoGrupo[grupo] = s = new HashSet<string>();
				if (!idsDoGrupo.TryGetValue(grupo, out List<string>? l))
					idsDoGrupo[grupo] = l = new List<string>();
				s.Add(contorno);
				l.Add(d.Id);
			}
			else
			{
				pelaAura.Add(d.Id);
				if (contorno != d.Aura)
					contornoDivergente.Add($"{d.Id}: contorno {contorno} != aura {d.Aura}");
			}

			// QUEM OSCILA. Nulo = contorno parado, e e o que 35 das 36 entradas tem que devolver.
			if (Jandirus.Core.Forms.Catalogo.CorDoContornoAlterna(d) is { } outra)
			{
				oscilam.Add(d.Id);
				tonsDaOscilacao.Add(outra);
				if (outra.Length != 6)
					Conferir(false, $"`{d.Id}`: a segunda cor do contorno em hexa de 6 (deu {outra})");
			}

			// A FAISCA CONTRA O ENUNCIADO, entrada por entrada. Nulo la = "esta tem que ser a aura",
			// e as duas metades importam: sem a segunda, uma `CorDosRaios` que devolvesse `8fe3ff`
			// pra o catalogo INTEIRO passaria na primeira sem tocar em regra nenhuma.
			string? faiscaDitada = FaiscaDoEnunciado(d);
			if (faisca != (faiscaDitada ?? d.Aura))
				faiscaErrada.Add($"{d.Id}: faisca {faisca} != {faiscaDitada ?? d.Aura}");
			if (faiscaDitada != null) tonsDaFaisca.Add(faisca);
			else faiscaPelaAura.Add(d.Id);
		}

		Conferir(semHexa == 0,
				 $"as {Jandirus.Core.Forms.Catalogo.Todas.Length} entradas devolvem contorno e faisca em hexa de 6");

		// O `minimo` NAO PODE SER ZERO em nenhum grupo -- senao a checagem passaria num catalogo onde
		// NINGUEM tem aquela cor, que e o mesmo defeito visto do outro lado.
		void UmTomSo(string grupo, string cor, int minimo)
		{
			HashSet<string> s = tonsDoGrupo.GetValueOrDefault(grupo) ?? new HashSet<string>();
			List<string> l = idsDoGrupo.GetValueOrDefault(grupo) ?? new List<string>();
			Conferir(l.Count >= minimo && s.Count == 1 && s.Contains(cor),
					 $"o contorno {grupo} sai num tom SO ({cor}) nas {l.Count} forma(s) da regra "
				   + $"[{s.Count} tom(ns): {string.Join("/", s)}]");
		}

		UmTomSo("amarelo",  Amarelo,     8);   // ssj1, os 2 grades, ssj2, ssj3, ssj4, 4FP, future_ssj
		UmTomSo("vermelho", Vermelho,    1);   // so o ssj4_limit_breaker
		// TRES, E NAO OS QUATRO DE ONTEM: o `legendary_full_power` (rede 140) foi FUNDIDO no
		// `legendary` (130) -- ele virou o fim da rampa `Mult = [25, 50]` em vez de uma entrada
		// propria. A fusao esta documentada no bloco do Legendary das entradas e o save antigo
		// continua chegando pela tabela `_redeAntiga` (`[140] = 130`); o que ficou pra tras foi
		// ESTE piso, que nunca desceu junto e deixou a linha reprovando o catalogo CERTO.
		//
		// Um piso que reprova o estado correto e pior que piso nenhum: enquanto ele acusa, os
		// outros sete grupos continuam medindo mas ninguem olha a saida vermelha -- e o tom, que
		// e o que esta linha existe pra proteger, passa despercebido nos dois sentidos.
		UmTomSo("verde",    Verde,      10);   // Legendary (3) + Legendary Primal (7)
		UmTomSo("azul",     AzulDivino,  2);   // blue, blue_evolution
		UmTomSo("rosa",     Rosa,        2);   // rose, rose2
		UmTomSo("branco",   Branco,      2);   // ui_sign, ui_perfected
		UmTomSo("roxo",     Roxo,        2);   // destroyer, ultra_ego
		UmTomSo("fera",     AzulDaFera,  1);   // so o beast -- e este e o lado A da oscilacao

		Conferir(idsDoGrupo.GetValueOrDefault("vermelho")?.Count == 1
			  && idsDoGrupo["vermelho"][0] == "ssj4_limit_breaker",
				 $"a UNICA excecao da escada Saiyajin e o `ssj4_limit_breaker` "
			   + $"({string.Join(", ", idsDoGrupo.GetValueOrDefault("vermelho") ?? new List<string>())})");

		// SEIS, e ja foram oito. O Beast saiu daqui pra o grupo `fera` quando ganhou as duas cores
		// dele, e o `prodigial_mistico_ascendido` saiu do CATALOGO. Sobram `base`, os dois SSG, o
		// Mistico e os dois Oozaru -- e o piso continua sendo piso: ele existe pra a checagem de baixo
		// nao passar num catalogo onde ninguem segue a aura.
		Conferir(pelaAura.Count >= 6, $"ha catalogo fora da regra pra varrer ({pelaAura.Count} entradas)");
		Conferir(contornoDivergente.Count == 0,
				 $"fora da regra o contorno E a aura, sem excecao ({string.Join(" | ", contornoDivergente)})");
		// E O SSG NAO PODE TER ESCORREGADO PRO AZUL: e o unico jeito de o corte da Ordem sumir sem
		// ninguem notar, porque o SSG e o degrau que quase ninguem posa.
		Conferir(pelaAura.Contains("ssg") && pelaAura.Contains("rose_ssg"),
				 "os dois SSG ficaram vermelhos, fora do azul e do rosa");

		// A FAISCA DAS 36, uma a uma, contra o que o dono ditou. Esta e a checagem que reprova se
		// alguem devolver a linha Legendary Primal pro `_ => d.Aura` ou levar o vermelho pro Limit
		// Breaker errado -- e ela imprime id, cor obtida e cor esperada.
		Conferir(faiscaErrada.Count == 0,
				 $"a faisca das {Jandirus.Core.Forms.Catalogo.Todas.Length} entradas sai como o dono "
			   + $"ditou ({string.Join(" | ", faiscaErrada)})");
		// QUATRO TONS NO CATALOGO INTEIRO, e nao um: o azul das escadas de sangue, o vermelho do Limit
		// Breaker, o branco do Mistico e o roxo da Fera. Com `== 1` a checagem passaria se o vermelho
		// escorregasse pro azul, que e a metade da regra que chegou por ultimo.
		//
		// ERAM TRES ATE AGORA -- a linha do Mistico dividia o branco. O dono separou os dois degraus:
		// *"no beast os raiozinhos sao roxos"*, e so a Fera. Os dois ultimos tons sao os que uma
		// "simplificacao" do `CorDosRaios` apagaria primeiro, porque sao os unicos ramos que nao sao
		// nem escada de sangue nem aura -- e agora sao DOIS ramos e nao um, com um `when` entre eles.
		Conferir(tonsDaFaisca.Count == 4 && tonsDaFaisca.Contains(AzulDaFaisca)
			  && tonsDaFaisca.Contains(Vermelho) && tonsDaFaisca.Contains("ffffff")
			  && tonsDaFaisca.Contains("d9b0ff"),
				 $"a faisca da regra sai em QUATRO tons -- azul, vermelho, branco e roxo "
			   + $"({string.Join("/", tonsDaFaisca)})");
		// E O CONTROLE: tem que sobrar catalogo seguindo a aura. Se ele zerar, a regra virou "azul
		// pra todo mundo" e a checagem de cima passaria feliz.
		Conferir(faiscaPelaAura.Count >= 10,
				 $"e {faiscaPelaAura.Count} entradas continuam com a faisca da propria aura");

		// ============================ A OSCILACAO E DE UMA FORMA SO ============================
		// O tamanho da lista importa dos DOIS lados: zero e "o Beast travou no azul" (a feature
		// morreu calada), e mais de um e "a oscilacao escapou pra a linha" (os dois Misticos entram
		// se alguem trocar o corte do `GodkiRoyalePct` por um teste de linha seco).
		Conferir(oscilam.Count == 1 && oscilam[0] == "beast",
				 $"SO o Beast tem contorno que oscila ({string.Join(", ", oscilam)})");
		Conferir(tonsDaOscilacao.Count == 1 && tonsDaOscilacao.Contains(RoxoDaFera),
				 $"e a outra ponta dele e o roxo ({string.Join("/", tonsDaOscilacao)})");
		// AS DUAS PONTAS TEM QUE SER DIFERENTES -- um par com as duas cores iguais compila, passa por
		// "oscila" e desenha um contorno absolutamente parado.
		Conferir(Jandirus.Core.Forms.Catalogo.CorDoContorno(
					 Jandirus.Core.Forms.Catalogo.Def("beast")) == AzulDaFera,
				 $"e a ponta A dele e o azul ({Jandirus.Core.Forms.Catalogo.CorDoContorno(Jandirus.Core.Forms.Catalogo.Def("beast"))})");
		// O CICLO E LENTO: abaixo de 2s a troca le como PISCA e nao como transicao gradual, que e o
		// contrario do pedido. O teto existe pelo motivo oposto -- um ciclo longo demais nunca fecha
		// dentro de uma luta e o jogador so ve uma das duas cores.
		//
		// PELA VARIAVEL E NAO POR `is >= 2 and <= 8`: o ciclo e `const`, e o padrao contra um `const`
		// e resolvido pelo COMPILADOR -- ele vira "sempre casa" (aviso CS8793) e a checagem deixa de
		// ser uma pergunta. Guardada num `double`, ela volta a ser medida em tempo de execucao.
		double cicloDaFera = Jandirus.Core.Forms.Catalogo.SegundosDoCicloDoContorno;
		Conferir(cicloDaFera >= 2 && cicloDaFera <= 8,
				 $"e a volta inteira dura alguns segundos ({cicloDaFera:0.#}s)");

		_passos.Add($"  --     contorno: {string.Join(" + ", idsDoGrupo.Select(p => $"{p.Value.Count} {p.Key}"))}"
				  + $" + {pelaAura.Count} pela aura; faisca: {tonsDaFaisca.Count} tom(ns) na regra"
				  + $" + {faiscaPelaAura.Count} pela aura; oscila: {string.Join(",", oscilam)}");
	}

	// =====================================================================
	// 6b. E A COR NAO E A AURA -- medido ENVENENANDO a aura
	// =====================================================================
	/// <summary>
	/// ============================ A CHECAGEM QUE PEGA A COINCIDENCIA ============================
	/// Tudo o que o bloco de cima afirma sobre o contorno passaria verde se a `CorDoContorno` fosse
	/// uma linha so -- `=> d.Aura`. Nao e figura de retorica: SETE das 29 formas da regra tem a `Aura`
	/// escrita com EXATAMENTE o hexa que a regra manda o contorno delas ter.
	///
	///     ssj1 / future_ssj      aura ffd24a = o amarelo da escada
	///     c_type / primal_legendary2   aura 4dff5a = o verde do Legendary
	///     blue                   aura 3ad2ff = o azul divino
	///     rose                   aura ff7ac6 = o rosa divino
	///     ssj4_limit_breaker     aura ff2d2f = o vermelho dele
	///
	/// E o `ssj4_limit_breaker` e o caso limite: ele e o UNICO membro do grupo do vermelho, entao o
	/// "um tom so por grupo" e verdade nele por definicao. Ate a FASE 1 desta serie o contorno dele
	/// SAIA pelo `_ => d.Aura`, e a bancada inteira ficou verde o tempo todo -- a cor estava certa
	/// por acaso. E exatamente esse acaso que este bloco mata.
	///
	/// ============================ COMO ELE MEDE ============================
	/// Ele ENVENENA: troca a `Aura` de cada entrada do catalogo por um hexa que nao e a cor de
	/// ninguem, pergunta as tres funcoes de novo e devolve a aura no `finally`. Dai saem duas
	/// afirmacoes que so fazem sentido JUNTAS:
	///
	///   a) quem esta na regra NAO SE MEXE -- se mexeu, a cor vinha da aura;
	///   b) quem esta FORA da regra SE MEXE, e vai parar exatamente no veneno. Esta e o CONTROLE, e
	///      sem ela a (a) e inutil: uma `CorDoContorno` que devolvesse uma constante pra tudo, ou um
	///      veneno que nunca chegasse (uma copia da `FormaDef`, um cache), passariam na (a) inteira.
	///
	/// ============================ COMO CADA UMA REPROVA SE A REGRA SUMIR ============================
	///   * troque o corpo da `CorDoContorno` por `d.Aura` e a (a) acusa as 29 formas da regra de uma
	///     vez, com o id e as duas cores de cada uma;
	///   * troque so uma constante de linha por `d.Aura` (o erro provavel: "a aura do Rose ja e rosa,
	///     pra que a constante?") e a (a) acusa os dois degraus daquela linha;
	///   * ponha a cor do Limit Breaker de volta no `_ => d.Aura` -- que e o estado em que este
	///     projeto ESTEVE -- e o teste nomeado dele reprova sozinho;
	///   * faca a `CorDosRaios` devolver `AzulDaFaisca` pra todo mundo e a (b) da faisca acusa as 16
	///     que deviam ter seguido o veneno (as divinas, o Oozaru e a base);
	///   * devolva a faisca do Limit Breaker pro `_ => d.Aura` e NADA MUDA na tela -- a aura dele ja e
	///     o mesmo `ff2d2f`. So a (a) daqui pega isso, e e o unico lugar do projeto que pega;
	///   * faca o par da Fera derivar a segunda ponta da aura e o teste da alterna reprova.
	///
	/// O VENENO E DEVOLVIDO NO `finally` E A DEVOLUCAO E CONFERIDA no fim -- este metodo mexe no
	/// catalogo VIVO que o resto da bancada (e o jogo, que esta rodando) le. Uma aura que ficasse
	/// envenenada pintaria a chama de preto no primeiro `AoMudarForma` seguinte, e o defeito
	/// apareceria a dez blocos daqui.
	/// ==========================================================================================
	/// </summary>
	private void AsCoresNaoSaoAAura()
	{
		// O VENENO. Preto-quase-puro nao e a aura de forma nenhuma (a mais escura do catalogo e o
		// `1c7cff` do Blue Evolution) -- e mesmo assim a bancada CONFERE isso logo abaixo em vez de
		// eu afirmar: um veneno que por acaso fosse a cor de alguem faria o teste daquela forma
		// passar sem medir nada.
		const string Veneno = "010203";

		// AS SETE COINCIDENCIAS DE HOJE, escritas a mao (ver o cabecalho). Sao as formas em que "a
		// regra escreveu a cor" e "a funcao caiu no `_ => d.Aura`" sao INDISTINGUIVEIS sem veneno --
		// ou seja, as unicas cujo acerto atual nao prova nada. Cada uma reprova pelo proprio nome.
		string[] coincidencias =
			["ssj1", "future_ssj", "c_type", "primal_legendary2", "blue", "rose", "ssj4_limit_breaker"];

		var auraDeVerdade = new Dictionary<string, string>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas) auraDeVerdade[d.Id] = d.Aura;

		Conferir(!auraDeVerdade.ContainsValue(Veneno),
				 $"o veneno (#{Veneno}) nao e a aura de ninguem no catalogo -- senao ele nao envenena");

		var contornoVenenado = new Dictionary<string, string>();
		var contornoEscorregou = new List<string>();
		var contornoNaoSeguiu = new List<string>();
		var faiscaEscorregou = new List<string>();
		var faiscaNaoSeguiu = new List<string>();
		var alternaEscorregou = new List<string>();
		int daRegra = 0, seguemAAura = 0, faiscaDaRegra = 0, faiscaPelaAura = 0;

		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			string contornoLimpo = Jandirus.Core.Forms.Catalogo.CorDoContorno(d);
			string faiscaLimpa = Jandirus.Core.Forms.Catalogo.CorDosRaios(d);
			string? alternaLimpa = Jandirus.Core.Forms.Catalogo.CorDoContornoAlterna(d);

			string contorno, faisca;
			string? alterna;
			// `try/finally` E NAO SO AS DUAS LINHAS: se uma das funcoes estourar (um `new Color` num
			// hexa torto, por exemplo), sem o `finally` a bancada morre DEIXANDO a aura envenenada --
			// e o jogo continua rodando com ela.
			try
			{
				d.Aura = Veneno;
				contorno = Jandirus.Core.Forms.Catalogo.CorDoContorno(d);
				faisca = Jandirus.Core.Forms.Catalogo.CorDosRaios(d);
				alterna = Jandirus.Core.Forms.Catalogo.CorDoContornoAlterna(d);
			}
			finally { d.Aura = auraDeVerdade[d.Id]; }

			contornoVenenado[d.Id] = contorno;

			if (GrupoDoEnunciado(d) != null)
			{
				daRegra++;
				if (contorno != contornoLimpo)
					contornoEscorregou.Add($"{d.Id}: {contornoLimpo} -> {contorno}");
			}
			else
			{
				seguemAAura++;
				if (contorno != Veneno) contornoNaoSeguiu.Add($"{d.Id}: {contorno}");
			}

			if (FaiscaDoEnunciado(d) != null)
			{
				faiscaDaRegra++;
				if (faisca != faiscaLimpa) faiscaEscorregou.Add($"{d.Id}: {faiscaLimpa} -> {faisca}");
			}
			else
			{
				faiscaPelaAura++;
				if (faisca != Veneno) faiscaNaoSeguiu.Add($"{d.Id}: {faisca}");
			}

			// A SEGUNDA PONTA DA FERA nao pode vir da aura tampouco. Nas outras 35 as duas leituras
			// sao nulas, e "nulo continua nulo" tambem e uma afirmacao: uma forma que passasse a
			// oscilar por causa do veneno seria uma oscilacao ligada ao dado errado.
			if (alterna != alternaLimpa)
				alternaEscorregou.Add($"{d.Id}: {alternaLimpa ?? "parado"} -> {alterna ?? "parado"}");
		}

		// OS DOIS LADOS PRECISAM TER GENTE. Uma populacao vazia faz a afirmacao correspondente passar
		// sem medir nada -- e o `GrupoDoEnunciado` devolvendo nulo pra tudo (o erro de quem mexe nele)
		// esvaziaria justamente o lado da regra.
		Conferir(daRegra >= 20 && seguemAAura >= 5,
				 $"ha catalogo dos dois lados pra envenenar ({daRegra} na regra, {seguemAAura} pela aura)");
		// E A FAISCA TEM A PROPRIA DIVISAO, que NAO e a do contorno: ela junta as escadas de sangue
		// (amarelo + vermelho + verde) de um lado e as divinas do outro. Os dois lados precisam ter
		// gente pelo mesmo motivo -- uma `FaiscaDoEnunciado` que devolvesse nulo pra tudo esvaziaria o
		// lado da regra e as duas checagens de baixo passariam sem medir nada.
		Conferir(faiscaDaRegra >= 15 && faiscaPelaAura >= 10,
				 $"idem pra a faisca ({faiscaDaRegra} na regra, {faiscaPelaAura} pela aura)");

		Conferir(contornoEscorregou.Count == 0,
				 $"com a aura ENVENENADA o contorno das {daRegra} formas da regra fica onde estava "
			   + $"({string.Join(" | ", contornoEscorregou)})");
		Conferir(contornoNaoSeguiu.Count == 0,
				 $"e o das {seguemAAura} que seguem a aura ACOMPANHA o veneno -- e o controle, sem ele "
			   + $"uma cor constante passaria ({string.Join(" | ", contornoNaoSeguiu)})");

		// UMA POR UMA, PELO NOME. O agregado acima ja pega todas, mas estas sete sao as que hoje
		// acertam por acaso: quando uma delas reprovar, o que interessa e saber QUAL sem ler lista.
		foreach (string id in coincidencias)
		{
			FormaDef? d = Jandirus.Core.Forms.Catalogo.Def(id);
			if (d == null) { Conferir(false, $"a forma `{id}` existe pra ser envenenada"); continue; }
			string limpo = Jandirus.Core.Forms.Catalogo.CorDoContorno(d);
			Conferir(contornoVenenado.GetValueOrDefault(id) == limpo && limpo != Veneno,
					 $"`{id}` acerta a cor por COINCIDENCIA com a propria aura ({limpo}), e envenenada "
				   + $"ela continua la ({contornoVenenado.GetValueOrDefault(id)})");
		}

		// A FAISCA DA REGRA TAMBEM NAO PODE VIR DA AURA -- e o Limit Breaker e o caso que SO isto pega:
		// a aura dele ja e `ff2d2f`, entao "vermelho por regra" e "vermelho por acaso" sao a mesma
		// tela. Envenenada a aura, a regra continua vermelha e o acaso teria virado preto.
		Conferir(faiscaEscorregou.Count == 0,
				 $"com a aura envenenada a faisca das {faiscaDaRegra} formas da regra fica onde estava "
			   + $"({string.Join(" | ", faiscaEscorregou)})");
		Conferir(faiscaNaoSeguiu.Count == 0,
				 $"e a das outras {faiscaPelaAura} acompanha o veneno ({string.Join(" | ", faiscaNaoSeguiu)})");
		Conferir(alternaEscorregou.Count == 0,
				 $"e a segunda ponta do contorno da Fera nao vem da aura ({string.Join(" | ", alternaEscorregou)})");

		// E O CATALOGO SAI DAQUI COMO ENTROU. Sem esta linha, um `finally` que alguem apagasse por
		// engano deixaria o resto da bancada -- e o jogo, que esta rodando atras desta janela --
		// medindo um catalogo preto, e o defeito nao apareceria neste bloco.
		var ficaramVenenadas = Jandirus.Core.Forms.Catalogo.Todas
			.Where(d => d.Aura != auraDeVerdade[d.Id]).Select(d => d.Id).ToList();
		Conferir(ficaramVenenadas.Count == 0,
				 $"e o catalogo sai daqui com as {auraDeVerdade.Count} auras de volta "
			   + $"({string.Join(", ", ficaramVenenadas)})");

		_passos.Add($"  --     veneno #{Veneno}: contorno {daRegra} parados / {seguemAAura} seguiram, "
				  + $"faisca {faiscaDaRegra} parados / {faiscaPelaAura} seguiram; "
				  + $"coincidencias cobertas: {string.Join(",", coincidencias)}");
	}

	// =====================================================================
	// 6-ter. A CHAMA -- DE QUE FOLHA ELA E, E DE QUEM E A COR
	// =====================================================================
	/// <summary>
	/// AS DUAS PERGUNTAS DA CHAMA, EM CONJUNTO E NO CATALOGO INTEIRO: qual DESENHO ela usa
	/// (`Catalogo.Folha`) e de QUEM e a cor dele (`Catalogo.ChamaDoJogador`).
	///
	/// ============================ O QUE ESTE BLOCO VE QUE OS OUTROS NAO VEEM ============================
	/// A folha ja e varrida forma a forma no <see cref="Catalogo"/>, contra uma derivacao independente, e
	/// os dois degraus do Prodigial ja tem linha com nome la. O que faltava eram os CONJUNTOS, e sao eles
	/// que respondem as duas metades do enunciado do dono:
	///
	///   * *"o mistico e beast tao usando a aura de carga do ssj god"* -- "eles sairam da folha do God"
	///     fica verde tambem num jogo em que NINGUEM usa mais essa folha. O `ssg` e o `rose_ssg` sao o
	///     CONTRA-EXEMPLO: enquanto os dois estiverem la, "sair de la" quer dizer alguma coisa. E o
	///     `rose_ssg` e o que mais escorrega dos dois, porque ele responde por um ramo PROPRIO
	///     (`GodKiRose`, com corte de `Ordem`) -- uma edicao que derrubasse so ele passaria inteira por
	///     uma checagem que so nomeasse o `ssg`;
	///
	///   * `Catalogo.ChamaDoJogador` NASCEU nesta passada e nao tinha varredura nenhuma. Um `_ => true`,
	///     ou um ramo novo por linha, daria a chama do jogador a formas que tem cor propria declarada --
	///     e as linhas que existiam perguntam pelos dois ids do Prodigial e mais nada. A cor da chama
	///     ENTRA na foto da <see cref="Aparencia"/> (`aura=folha/cor`), mas la ela e comparada com a
	///     foto da BASE, procurando canal que VAZA -- ninguem afirma o valor dela forma a forma.
	///
	/// DEFEITO INJETADO E RODADO, pra isto nao ser suposicao: com um `LinhaDeForma.GodKi => true` no
	/// `ChamaDoJogador`, a linha do conjunto aqui embaixo cai imprimindo
	/// `[base, blue, blue_evolution, mistico, ssg]`. Do resto da bancada so o <see cref="AIdaEAVolta"/>
	/// reclamou, e so de DOIS dos tres (`blue` e `ssg`, que ele posa) -- o `blue_evolution` nao e posado
	/// por ninguem e sairia com a chama trocada em silencio.
	/// ================================================================================================
	///
	/// ============================ A COR PESSOAL VIROU CAMPO DE FICHA, E ISTO AQUI MUDOU ============================
	/// Este bloco dizia que a prova natural do pedido *"a aura do mistico tem q ser a mesma aura da BASE
	/// DO PERSONAGEM"* -- dois personagens com auras diferentes -- **nao era possivel neste port**, porque
	/// a cor pessoal era UMA constante compartilhada (o `Aura.CorDoKiCru`) e nenhuma funcao do funil
	/// aceitava um jogador como argumento. Deixou de ser verdade: `Appearance.CorAura` e sorteada no
	/// nascimento como no DM (`CharacterCreation.dm:25-27`) e `Aura.CorDaChamaDe` pede a cor de QUEM esta
	/// acendendo.
	///
	/// O QUE ESTA FUNCAO PASSOU A FAZER com isso e o minimo honesto: ela usa o
	/// <see cref="CorPessoalDeTeste"/> -- um tom que NAO e o fallback -- em todas as contas. Com o
	/// `CorDoKiCru` no lugar dele, tudo aqui ficaria verde num jogo em que a cor sorteada nunca saisse do
	/// save, que e o modo de falha que nasceu junto com o campo.
	///
	/// E A PROVA POR VENENO CONTINUA, porque ela mede outra coisa: em vez de duas cores de JOGADOR, duas
	/// cores de FORMA. Com a `FormaDef.Aura` de cada entrada trocada por um veneno, quem usa a chama do
	/// jogador tem que ficar PARADO e quem usa a propria tem que ACOMPANHAR. Isso e o que separa "a
	/// resposta e a do jogador" de "a resposta e essa cor por acaso" -- se alguem apagar o ramo do
	/// Mistico e escrever `Aura = "ffd2c8"` na entrada dele, a tela fica identica, todas as checagens de
	/// hexa continuam verdes, e SO esta varredura reprova. (O veneno e a tecnica do
	/// <see cref="AsCoresNaoSaoAAura"/>, que ja a usa pro contorno e pra faisca e nao alcanca a chama.)
	///
	/// O QUE CONTINUA FORA DAQUI, e esta dito no relatorio: **dois corpos ao mesmo tempo**. Tudo nesta
	/// funcao e funcao pura com uma cor pessoal so; "chega certa num corpo e errada no outro" e pergunta
	/// de dois bonecos vivos, e o lugar dela e o `RoboDeDoisCorpos`, que ja monta dois com `Aura`,
	/// `Carga` e `Nebulosa`.
	/// ==============================================================================================================
	///
	/// ============================ E OS DOIS CONTROLES SAO DEFEITO INJETADO DE VERDADE ============================
	/// Conjunto e a familia de checagem que mais passa por acaso: um `Where` que nao case com nada devolve
	/// vazio, e vazio comparado com vazio e verde. Entao os dois conjuntos sao medidos DE NOVO com um
	/// defeito armado no catalogo vivo, e o que se cobra e que a medida MUDE:
	///
	///   * `rose_ssg.Ordem` de 10 pra 20 -- o corte do `OrdemDoKiSobreOSuperSaiyajin` o joga na folha
	///     ROSA e a folha do God fica com um degrau so;
	///   * `beast.PedeGodKi` de 50 pra -1 -- e literalmente a regressao que o dono viu: o Beast passa a
	///     dividir a chama com o Mistico. O Core promete que esse campo move contorno, raiva e chama de
	///     uma vez (ver `Catalogo.ChamaDoJogador`), e este controle e quem cobra a promessa.
	///
	/// OS DOIS SAO DESFEITOS NO `finally` E A DEVOLUCAO E CONFERIDA, pelo motivo do bloco do veneno: o
	/// catalogo esta VIVO e o jogo esta rodando atras desta janela.
	/// ========================================================================================================
	/// </summary>
	private void AChamaDeQuemEDeQueFolha()
	{
		// A folha do God e o `ChamaDoJogador` sao perguntados tantas vezes aqui que escrever
		// `Jandirus.Core.Forms.Catalogo.` por extenso (a convencao deste arquivo, por causa do choque
		// com o metodo `Catalogo()`) esconderia os conjuntos dentro do proprio prefixo.
		static string[] NaFolha(FolhaDeAura f) =>
			[.. Jandirus.Core.Forms.Catalogo.Todas
				.Where(d => Jandirus.Core.Forms.Catalogo.Folha(d) == f).Select(d => d.Id).Order()];
		static string[] ComAChamaDoJogador() =>
			[.. Jandirus.Core.Forms.Catalogo.Todas
				.Where(Jandirus.Core.Forms.Catalogo.ChamaDoJogador).Select(d => d.Id).Order()];

		// ============================ 1. A FOLHA DO GOD TEM DOIS DONOS, E ELES TEM NOME ============================
		// Os ids estao LITERAIS pelo motivo do resto deste arquivo: escrever a esperada como "as formas
		// divinas com Ordem < 20" seria repetir a derivacao do Core e as duas errariam juntas.
		string[] noDeus = NaFolha(FolhaDeAura.DeusQuente);
		Conferir(noDeus.SequenceEqual(new[] { "rose_ssg", "ssg" }),
				 "a chama do SSG (`FieryGod`) e de DOIS degraus e eles tem nome -- `ssg` e `rose_ssg` "
			   + $"([{string.Join(", ", noDeus)}])");

		// E O `rose_ssg` DITO DE NOVO, sozinho: ele e o que sai de um ramo proprio (`GodKiRose`), e a
		// linha de cima o cobre em conjunto -- mas quem ler o log vermelho precisa saber que o degrau
		// rosa e o que ninguem posa e o que ninguem lembra de conferir.
		Conferir(Jandirus.Core.Forms.Catalogo.Folha(Jandirus.Core.Forms.Catalogo.Def("rose_ssg"))
				 == FolhaDeAura.DeusQuente,
				 "-- e o SSG da linha Rose continua nela junto com o comum (ramo proprio, corte proprio)");

		// ============================ 2. E O PRODIGIAL NAO ESTA MAIS LA ============================
		// A queixa foi sobre a LINHA (*"o mistico e beast"*), entao os dois sao nomeados: um conserto
		// que pegasse so um degrau e o jeito mais provavel de isto reaparecer.
		foreach (string prodigial in new[] { Jandirus.Core.Forms.Catalogo.IdMistico, "beast" })
			Conferir(!noDeus.Contains(prodigial),
					 $"`{prodigial}` NAO esta na folha do God -- e ela continua tendo {noDeus.Length} "
				   + "dono(s), entao \"saiu\" quer dizer alguma coisa");

		// ============================ 3. QUEM ACENDE A CHAMA **DO JOGADOR** ============================
		// Dois, e os dois por motivo diferente: a `base` porque nao e transformacao nenhuma (e o caso
		// original da pergunta), e o `mistico` por PORTE -- ele nao pede ki divino (`PedeGodKi = -1`) e
		// no DM cai no ramo de baixo do `AuraObject.dm:191-194`, que usa `container.AURA` mais o
		// `icolor = rgb(AuraR, AuraG, AuraB)` do `centerAura()`.
		//
		// O BEAST FICA DE FORA COM RAZAO PROPRIA e por isso ele nao esta nesta lista: ele tem cor de
		// chama declarada (`7d5af0`, o `rgb(125,90,240)` do `Mystic.dm:95`).
		// ============================ E A LINHA DO FROST DEMON INTEIRA, QUE E O TERCEIRO MOTIVO ============================
		// Os sete degraus dele entraram nesta lista de uma vez, e por porte: NENHUM deles acende aura
		// no original. `Frost_Demon_Forms` (`IcerTransform.dm:83-114`) toca `1aura.wav`, troca o icone
		// pelo corpo que o jogador escolheu e escreve uma linha no chat -- e mais nada. Quem veste
		// overlay nele e o GOLDEN (`/obj/overlay/icergod`, skill separada, fora do catalogo) e o
		// DESCONTROLE do Mutante (`fd_menacing_red`, que e estado e nao forma).
		//
		// OS IDS CONTINUAM LITERAIS, e agora a diferenca importa mais: escrever a esperada como "os da
		// linha FrostDemon" repetiria a derivacao do Core e as duas errariam juntas -- que e a regra
		// deste arquivo inteiro (ver o bloco 1 acima).
		string[] doJogador = ComAChamaDoJogador();
		Conferir(doJogador.SequenceEqual(new[]
				 {
					 Jandirus.Core.Forms.Catalogo.IdBase,
					 "frost1", "frost2", "frost3", "frost4", "frost5", "frost6", "frost7",
					 Jandirus.Core.Forms.Catalogo.IdMistico,
				 }),
				 "a chama do JOGADOR e de NOVE formas -- a `base`, o `mistico` e os sete degraus do "
			   + $"Frost Demon ([{string.Join(", ", doJogador)}])");

		// ============================ 4. E A CHAMA DE TODAS AS OUTRAS E A COR QUE ELAS DECLARAM ============================
		// Este e o "as 33 outras nao se moveram" desta passada. A afirmacao NAO e "a funcao concorda com
		// ela mesma": e que a chama de cada uma das outras entradas e o hexa ESCRITO na entrada dela --
		// a unica fonte que nao passa pelo `ChamaDoJogador`. Um ramo novo em `ChamaDoJogador` que
		// alcancasse uma linha inteira apagaria a cor declarada dessas formas, e cai aqui pelo nome.
		var chamaTrocada = new List<string>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			if (Jandirus.Core.Forms.Catalogo.ChamaDoJogador(d)) continue;
			if (!Aura.CorDaChamaDe(d, CorPessoalDeTeste).IsEqualApprox(new Color(d.Aura)))
				chamaTrocada.Add($"{d.Id}: {Aura.CorDaChamaDe(d, CorPessoalDeTeste).ToHtml(false)} != {d.Aura}");
		}
		Conferir(chamaTrocada.Count == 0,
				 $"e as outras {Jandirus.Core.Forms.Catalogo.Todas.Length - doJogador.Length} acendem a "
			   + $"COR QUE DECLARAM ({string.Join(" | ", chamaTrocada)})");

		// ============================ 5. O VENENO: DUAS CORES DE FORMA NO LUGAR DE DOIS JOGADORES ============================
		// Ver o cabecalho. O mesmo veneno do `AsCoresNaoSaoAAura` -- e conferido do mesmo jeito, porque
		// um veneno que por acaso fosse a cor de alguem faria o teste daquela forma passar sem medir.
		const string Veneno = "010203";
		var deVerdade = new Dictionary<string, string>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas) deVerdade[d.Id] = d.Aura;
		Conferir(!deVerdade.ContainsValue(Veneno),
				 $"o veneno (#{Veneno}) nao e a chama de ninguem no catalogo -- senao ele nao envenena");

		var seMexeuSemDever = new List<string>();
		var ficouParadaSemDever = new List<string>();
		foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
		{
			Color limpa = Aura.CorDaChamaDe(d, CorPessoalDeTeste);
			Color venenada;
			// `try/finally` PELO MOTIVO DO BLOCO IRMAO: se o `new Color` estourar num hexa torto, sem
			// ele a bancada morre DEIXANDO o catalogo envenenado -- e o jogo continua rodando com ele.
			try { d.Aura = Veneno; venenada = Aura.CorDaChamaDe(d, CorPessoalDeTeste); }
			finally { d.Aura = deVerdade[d.Id]; }

			bool mexeu = !venenada.IsEqualApprox(limpa);
			if (Jandirus.Core.Forms.Catalogo.ChamaDoJogador(d))
			{
				if (mexeu) seMexeuSemDever.Add($"{d.Id}: {limpa.ToHtml(false)} -> {venenada.ToHtml(false)}");
			}
			else if (!mexeu) ficouParadaSemDever.Add($"{d.Id}: {limpa.ToHtml(false)}");
		}
		Conferir(seMexeuSemDever.Count == 0,
				 $"com a cor da forma envenenada, a chama das {doJogador.Length} do JOGADOR nao se mexe "
			   + $"({string.Join(" | ", seMexeuSemDever)}) -- e o que separa \"e a do jogador\" de \"e "
			   + "branca por acaso\"");
		Conferir(ficouParadaSemDever.Count == 0,
				 "e a das outras ACOMPANHA o veneno, uma a uma "
			   + $"({string.Join(" | ", ficouParadaSemDever)})");

		// E O MISTICO DITO PELO NOME NA PONTA DOS DOIS: ele e quem o dono nomeou, e as duas listas acima
		// falam por contagem. Aqui se afirma o valor -- a chama dele continua sendo o ki cru DEPOIS de a
		// entrada dele ter passado pelo veneno e voltado.
		if (Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdMistico) is { } mis)
			Conferir(Aura.CorDaChamaDe(mis, CorPessoalDeTeste).IsEqualApprox(CorPessoalDeTeste)
				  && mis.Aura == deVerdade[mis.Id],
					 $"-- e a do Mistico e a cor do jogador #{CorPessoalDeTeste.ToHtml(false)} com a "
				   + $"entrada dele intacta (#{mis.Aura})");

		// ============================ 6. OS DOIS CONTROLES, COM O DEFEITO ARMADO ============================
		// (a) O `rose_ssg` FORA da folha do God -- ver o cabecalho.
		if (Jandirus.Core.Forms.Catalogo.Def("rose_ssg") is { } rosa)
		{
			int ordemDeVerdade = rosa.Ordem;
			string[] comDefeito;
			try { rosa.Ordem = 20; comDefeito = NaFolha(FolhaDeAura.DeusQuente); }
			finally { rosa.Ordem = ordemDeVerdade; }

			Conferir(!comDefeito.SequenceEqual(noDeus) && !comDefeito.Contains("rose_ssg"),
					 "CONTROLE: com o `rose_ssg` empurrado pra cima do corte, a folha do God fica com "
				   + $"[{string.Join(", ", comDefeito)}] -- a medida ENXERGA um degrau saindo dela");
			Conferir(rosa.Ordem == ordemDeVerdade
					 && NaFolha(FolhaDeAura.DeusQuente).SequenceEqual(noDeus),
					 "-- e o catalogo sai do controle como entrou");
		}

		// (b) O BEAST DIVIDINDO A CHAMA COM O MISTICO -- a regressao do dono, armada de proposito.
		if (Jandirus.Core.Forms.Catalogo.Def("beast") is { } fera)
		{
			double pedeDeVerdade = fera.PedeGodKi;
			string[] comDefeito;
			Color chamaComDefeito;
			try
			{
				fera.PedeGodKi = -1;
				comDefeito = ComAChamaDoJogador();
				chamaComDefeito = Aura.CorDaChamaDe(fera, CorPessoalDeTeste);
			}
			finally { fera.PedeGodKi = pedeDeVerdade; }

			Conferir(comDefeito.Contains("beast") && chamaComDefeito.IsEqualApprox(CorPessoalDeTeste),
					 "CONTROLE: com o `PedeGodKi` da Fera abaixo do corte ela CAI na chama do jogador "
				   + $"([{string.Join(", ", comDefeito)}]) -- a medida enxerga a regressao que o dono viu");
			Conferir(fera.PedeGodKi == pedeDeVerdade
					 && ComAChamaDoJogador().SequenceEqual(doJogador)
					 && Aura.CorDaChamaDe(fera, CorPessoalDeTeste).IsEqualApprox(new Color("7d5af0")),
					 "-- e a Fera volta ao roxo declarado dela "
				   + $"(#{Aura.CorDaChamaDe(fera, CorPessoalDeTeste).ToHtml(false)})");
		}

		_passos.Add($"  --     folha do God: {string.Join(", ", noDeus)}; chama do jogador: "
				  + string.Join(", ", doJogador));
	}

	// =====================================================================
	// 6b. O SAVE DO BINARIO ANTIGO -- a unica familia daqui que protege a CONTA
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ESTA FAMILIA NAO SE PARECE COM NENHUMA OUTRA DAQUI ============================
	/// Todo o resto desta bancada erra PIXEL. Esta erra CONTA. O `Appearance.CorAura` nasceu DENTRO de
	/// um objeto que ja esta gravado no disco de todo mundo, e o modo de falha desse tipo de mudanca ja
	/// tem nome e cadeia escritos neste repo (cabecalho do `PecaDeRoupaConverter`): um `JsonException`
	/// na leitura faz o `AccountStore.Carregar` devolver **nulo**, o `Login` le nulo como "conta nova",
	/// monta uma conta vazia e GRAVA POR CIMA. O save nao fica ilegivel -- fica APAGADO, com os tres
	/// personagens dentro. Nenhuma checagem de cor deste arquivo tem uma palavra a dizer sobre isso.
	///
	/// ============================ E POR ISSO A PECA E UM ARQUIVO DE VERDADE ============================
	/// As duas pecas em `Assets/Data/bancada-save-*.json` **nao foram escritas a mao**: sao dois saves
	/// que o binario ANTIGO deste projeto gravou (7 e 3 de agosto, antes de este campo existir), copiados
	/// do `%APPDATA%` com tres campos trocados -- `Conta`, `Sal` e `Hash`, porque credencial nao entra em
	/// repositorio (sal vazio = "conta antiga, sem senha", que o `AccountStore.Confere` ja aceita).
	///
	/// Montar o JSON aqui dentro seria escrever o que EU acho que o binario antigo escrevia, e o que se
	/// quer medir e justamente a diferenca entre as duas coisas. A segunda peca prova que isso importa:
	/// ela e de DUAS geracoes atras e nao tem oito campos que o `CharacterSave` de hoje tem
	/// (`Mochila`, `Porte`, `Historia`, `Social`, `FormasEstreadas`, `Disciplina`, `DiscReal`,
	/// `DiscAtual`) nem o `FormasDeFrost` dentro do `Visual` -- eu nao teria lembrado de omitir nenhum.
	///
	/// ============================ AS SEIS PERGUNTAS, E COMO CADA UMA REPROVA ============================
	///   (a) A PECA CARREGA -- `Carregar` nao devolve nulo. REPROVA se qualquer campo novo do
	///       `CharacterSave` mudar de TIPO por baixo de um save existente. E o primeiro elo da cadeia
	///       que apaga a conta, e o item (g) prova que esta medida enxerga esse elo.
	///   (b) O PERSONAGEM NAO PERDE NADA -- os literais desta peca (BP de 10 trilhoes, 35 maestrias, 15
	///       membros, disciplina 2 a 100%...) atravessam o parser. REPROVA com qualquer regressao que
	///       faca um pedaco do JSON ser engolido calado.
	///   (c) A COR NASCE PREENCHIDA E E A DERIVADA -- `CorDeAura.De(nome, CriadoEm)`. REPROVA se alguem
	///       trocar a derivacao por um sorteio na carga, ou tirar o `??=` do `ParaJogador`.
	///   (d) DUAS CARGAS DAO A MESMA COR, e uma TERCEIRA depois de passar pelo DISCO. REPROVA com a
	///       cor rerrolando por login (o pior defeito possivel aqui) e com o `Rgb` voltando PRETO do
	///       JSON -- que e o que acontece sem o `[JsonConstructor]` daquela struct, e que sumiria
	///       calado porque preto e a mesma coisa que "sem tinta".
	///   (e) O CAMPO E OVERRIDE E NAO CACHE -- uma cor gravada FORA da faixa do sorteio sobrevive a
	///       carga. REPROVA no dia em que alguem escrever `=` no lugar do `??=` (a tela fica igual, e
	///       so o verb `Aura_Color()` do DM, quando for portado, descobriria).
	///   (f) O BINARIO VELHO LENDO SAVE NOVO -- propriedade desconhecida e IGNORADA, nao estoura. E o
	///       mecanismo que faz campo novo ser seguro nas duas direcoes; REPROVA se alguem ligar
	///       `JsonUnmappedMemberHandling.Disallow` nas opcoes do `AccountStore`.
	///   (g) OS DOIS CONTROLES, com defeito injetado -- ver os blocos.
	///
	/// ============================ E O QUE ELA **NAO** MEDE ============================
	/// O `ParaJogador` e o funil "save -> jogador", e nao o `Entrar` inteiro: maestria, skill e membro
	/// sao remontados depois dele, no `GameServer.Entrar`. Por isso o item (b) cobra esses tres no
	/// `CharacterSave` LIDO (o parser atravessou) e nao no `ServerPlayer` (que ainda nao os tem).
	///
	/// TUDO ISTO RODA NUMA PASTA TEMPORARIA. A pasta de saves de verdade nao e tocada em momento
	/// nenhum -- `AccountStore` recebe a pasta no construtor exatamente por isso.
	/// ==============================================================================================================
	/// </summary>
	private void OSaveVelhoCarrega()
	{
		const string PecaSemCor = "res://Assets/Data/bancada-save-sem-cor-de-aura.json";
		const string PecaAntiga = "res://Assets/Data/bancada-save-sem-formas-de-frost.json";

		if (!Godot.FileAccess.FileExists(PecaSemCor) || !Godot.FileAccess.FileExists(PecaAntiga))
		{
			Conferir(false, "as duas pecas de save do binario antigo estao no repo "
						  + "(`Assets/Data/bancada-save-*.json`) -- sem elas esta familia nao mede nada");
			return;
		}

		// A PASTA E TEMPORARIA E E APAGADA NO FIM. Escrever na pasta de saves de verdade faria uma
		// bancada de leitura virar uma bancada que MEXE na conta do dono -- que e o acidente que ela
		// existe pra impedir.
		string pasta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "jandirus-bancada-save");
		try
		{
			if (System.IO.Directory.Exists(pasta)) System.IO.Directory.Delete(pasta, true);
			System.IO.Directory.CreateDirectory(pasta);
			var loja = new Jandirus.Server.AccountStore(pasta);

			// Escreve a peca na pasta temporaria, com uma mexida OPCIONAL no texto cru -- e o texto cru
			// que importa nos controles: o defeito mora no arquivo, nao no objeto.
			string Semear(string peca, string conta, Func<string, string>? mexer = null)
			{
				string txt = Godot.FileAccess.GetFileAsString(peca);
				if (mexer != null) txt = mexer(txt);
				System.IO.File.WriteAllText(System.IO.Path.Combine(pasta, conta + ".json"), txt,
								  new System.Text.UTF8Encoding(false));
				return txt;
			}

			// ============================ O RELOGIO PRECISA ANDAR ENTRE DUAS CARGAS ============================
			// ISTO NAO E PRECAUCAO, E CONSERTO DE UM CEGO MEDIDO. Com o `??=` do `ParaJogador` trocado
			// por `= CorDeAura.Sortear((ulong)Environment.TickCount64)` -- o desenho alternativo que esta
			// familia inteira existe pra proibir, "sortear na carga" --, as duas linhas de estabilidade
			// abaixo ficaram VERDES numa rodada de verdade: as duas cargas caem no MESMO tique de ~16 ms
			// do relogio, o sorteio recebe a mesma semente e devolve a mesma cor. Ou seja "carregue duas
			// vezes e compare" nao mede nada quando as duas vezes acontecem no mesmo instante.
			//
			// Quarenta milissegundos sao mais de dois tiques. Custa 80 ms na rodada inteira e e o preco
			// de a frase "a cor nao muda entre dois carregamentos" querer dizer o que ela diz.
			// ==============================================================================================
			static void EsperarORelogioAndar()
			{
				// `System.Environment` POR EXTENSO: `Godot.Environment` (o ambiente 3D) esta no `using
				// Godot` deste arquivo e o nome cru fica ambiguo.
				long inicio = System.Environment.TickCount64;
				while (System.Environment.TickCount64 - inicio < 40) System.Threading.Thread.Sleep(1);
			}

			// O CAMINHO DE PRODUCAO INTEIRO NUMA LINHA: `Carregar` (o parser) + `ParaJogador` (o funil
			// unico save->jogador, chamado so pelo `Entrar`). Nada aqui remonta objeto na mao.
			(Jandirus.Server.AccountSave? Conta, Jandirus.Server.ServerPlayer? Jogador) Entrar(string conta)
			{
				Jandirus.Server.AccountSave? a = loja.Carregar(conta);
				if (a?.Slots is not { Length: > 0 } || a.Slots[0] is not { } s) return (a, null);
				var pl = new Jandirus.Server.ServerPlayer();
				Jandirus.Server.AccountStore.ParaJogador(s, pl);
				return (a, pl);
			}

			// =============================================================
			// (a) A PECA E MESMO DE ANTES DO CAMPO, E ELA CARREGA
			// =============================================================
			// A PRIMEIRA LINHA E SOBRE A PECA E NAO SOBRE O JOGO, e ela e obrigatoria: uma peca que
			// (por descuido meu ou de quem regravar o arquivo um dia) JA TIVESSE a cor dentro faria
			// toda esta familia medir o caminho do save NOVO e passar verde sem nunca ter exercitado a
			// migracao. E o mesmo cuidado do veneno la em cima -- conferir que o teste testa.
			const string ContaVelha = "bancadasavevelho";
			string cru = Semear(PecaSemCor, ContaVelha);
			Conferir(!cru.Contains("CorAura"),
					 "a peca `bancada-save-sem-cor-de-aura.json` REALMENTE nao tem o campo -- ela e um "
				   + "save que o binario antigo gravou, e nao um save de hoje com o campo apagado");
			Conferir(loja.Existe(ContaVelha),
					 "-- e o `AccountStore` a acha pelo nome de conta (o arquivo e `<conta saneada>.json`)");

			(Jandirus.Server.AccountSave? accVelha, Jandirus.Server.ServerPlayer? plVelho) = Entrar(ContaVelha);
			Conferir(accVelha is not null,
					 "o save do binario ANTIGO carrega -- `Carregar` nao devolveu nulo, que e o primeiro "
				   + "elo da cadeia que apaga a conta (ver o cabecalho)");
			if (accVelha?.Slots[0] is not { } sv || plVelho is not { } pv)
			{ Conferir(false, "-- e o personagem do slot 1 esta la"); return; }

			// =============================================================
			// (b) E O PERSONAGEM NAO PERDE NADA
			// =============================================================
			// OS LITERAIS SAO DA PECA e nao do objeto lido: comparar o objeto com ele mesmo passaria com
			// o arquivo pela metade. Estes numeros foram lidos do JSON antes de escrever esta linha.
			Conferir(pv.Name == "Zv" && pv.Race == "Saiyan" && pv.Class == "Low-Class"
				  && pv.Planeta == "Vegeta" && pv.Genero == "Male" && pv.Linhagem == "Saiyan"
				  && pv.Idade == 18 && pv.Porte == "Medium" && pv.Historia.Length > 0
				  && pv.CriadoEm == 1786169643894L,
					 $"o personagem volta inteiro pelo `ParaJogador` ({pv.Name}/{pv.Race}/{pv.Class}, "
				   + $"idade {pv.Idade}, porte {pv.Porte}, criado em {pv.CriadoEm})");
			Conferir(System.Math.Abs(pv.Ficha.BP - 1e13) < 1e6 && pv.Ficha.Race == "Saiyan" && pv.Ficha.Idade == 18,
					 $"-- com a FICHA inteira junto (BP {pv.Ficha.BP:0}, esperado 1e13)");
			Conferir(Mathf.Abs(pv.Pos.X - 7692.4277f) < 0.01f && Mathf.Abs(pv.Pos.Y - 7849.0176f) < 0.01f,
					 $"-- e no lugar onde ele deslogou ({pv.Pos.X:0.##}, {pv.Pos.Y:0.##})");
			// O QUE O `ParaJogador` NAO CARREGA (o `Entrar` remonta depois) e cobrado no save LIDO: o
			// que se afirma aqui e que o PARSER atravessou o JSON inteiro, que e o que "nao perde nada"
			// quer dizer num save.
			Conferir(sv.Maestrias.Count == 35 && sv.Membros.Count == 15 && sv.Skills.Count == 2
				  && sv.FormasDespertadas.Count == 4 && sv.FormasEstreadas is { Count: 5 }
				  && sv.MarcosTotais == 3 && sv.Limiares.ssjat == 1_500_000
				  && sv is { Disciplina: 2, DiscReal: 100, DiscAtual: 100 },
					 $"-- e o resto do save atravessou o parser ({sv.Maestrias.Count} maestrias, "
				   + $"{sv.Membros.Count} membros, {sv.Skills.Count} skills, "
				   + $"{sv.FormasDespertadas.Count} formas liberadas, disciplina {sv.Disciplina})");

			// =============================================================
			// (c) A COR NASCE PREENCHIDA, E E A DERIVADA
			// =============================================================
			Jandirus.Core.Appearance.Rgb esperada =
				Jandirus.Core.Appearance.CorDeAura.De(sv.Nome, sv.CriadoEm);
			Conferir(pv.Visual.CorAura is { } cv
				  && cv.R == esperada.R && cv.G == esperada.G && cv.B == esperada.B,
					 $"o personagem de antes do campo ganha a cor DERIVADA de nome + `CriadoEm` "
				   + $"({pv.Visual.CorAura?.ToString() ?? "NULA"}, esperado {esperada}) -- sem ramo de "
				   + "migracao nenhum, que e o `??=` do `ParaJogador`");
			Conferir(pv.Visual.CorAura is { } cf
				  && cf.R >= Jandirus.Core.Appearance.CorDeAura.PicoDaFolha
				  && cf.G >= Jandirus.Core.Appearance.CorDeAura.PicoDaFolha
				  && cf.B >= Jandirus.Core.Appearance.CorDeAura.PicoDaFolha,
					 "-- e ela esta na faixa que o sorteio do DM produz (>= "
				   + $"{Jandirus.Core.Appearance.CorDeAura.PicoDaFolha} nos tres canais)");

			// =============================================================
			// (d) DUAS CARGAS, E UMA TERCEIRA DEPOIS DO DISCO
			// =============================================================
			// UMA COR QUE RERROLA E PIOR QUE NENHUMA COR: o jogador veria a propria aura mudar toda vez
			// que entrasse. E o teste tem que ser CARGA DE VERDADE, do arquivo -- comparar
			// `CorDeAura.De(x)` com ele mesmo prova a funcao pura, que ja e provada la em cima, e nao
			// que o caminho do disco chega no mesmo lugar.
			EsperarORelogioAndar();
			(_, Jandirus.Server.ServerPlayer? plDeNovo) = Entrar(ContaVelha);
			Conferir(plDeNovo?.Visual.CorAura is { } c2 && pv.Visual.CorAura is { } c1
				  && c2.R == c1.R && c2.G == c1.G && c2.B == c1.B,
					 $"CARREGAR DE NOVO da a MESMA cor ({plDeNovo?.Visual.CorAura?.ToString() ?? "NULA"}) "
				   + "-- a derivacao nao depende do processo nem do relogio");

			// E AGORA COM O CAMPO NO DISCO. O `ParaJogador` MUTA o `Visual` que veio do save (e o mesmo
			// objeto), entao gravar a conta recem-carregada e exatamente o que o `Persistir` do servidor
			// faz no primeiro tique -- e a partir dai o campo deixa de ser derivado e passa a ser LIDO.
			//
			// ESTA E A LINHA QUE PEGA O `Rgb` VOLTANDO PRETO: sem o `[JsonConstructor]` da struct, os
			// tres campos `readonly` sao gravados e nao lidos, a cor volta #000000 -- e preto, na tinta
			// somada, e indistinguivel de "sem cor". Ele sumiria calado em toda parte menos aqui.
			loja.Gravar(accVelha!);
			string gravado = System.IO.File.ReadAllText(System.IO.Path.Combine(pasta, ContaVelha + ".json"));
			Conferir(gravado.Contains("CorAura"),
					 "-- e depois de gravar, o campo esta NO DISCO (o `Persistir` do servidor faz isso "
				   + "no primeiro tique)");
			EsperarORelogioAndar();
			(_, Jandirus.Server.ServerPlayer? plDoDisco) = Entrar(ContaVelha);
			Conferir(plDoDisco?.Visual.CorAura is { } c3 && pv.Visual.CorAura is { } c0
				  && c3.R == c0.R && c3.G == c0.G && c3.B == c0.B
				  && !(c3.R == 0 && c3.G == 0 && c3.B == 0),
					 $"-- e relendo do disco ela e a MESMA e nao PRETA "
				   + $"({plDoDisco?.Visual.CorAura?.ToString() ?? "NULA"}): o round-trip do `Rgb` "
				   + "`readonly` esta de pe");

			// =============================================================
			// (e) O CAMPO E OVERRIDE, E NAO CACHE DA DERIVACAO
			// =============================================================
			// A COR ESCOLHIDA ESTA FORA DA FAIXA DO SORTEIO de proposito (10/20/30, e o sorteio nunca
			// desce de 200): assim "ela sobreviveu" nao pode ser confundido com "ela foi derivada de
			// novo e calhou de bater". E o lugar onde o verb `Aura_Color()` do DM
			// (`CharacterCreation.dm:129-151`) vai escrever no dia em que for portado.
			//
			// COMO REPROVA: troque o `??=` do `AccountStore.ParaJogador` por `=`. A tela fica
			// exatamente igual, todo o resto desta bancada continua verde, e so esta linha cai.
			accVelha!.Slots[0]!.Visual.CorAura = new Jandirus.Core.Appearance.Rgb(10, 20, 30);
			loja.Gravar(accVelha);
			(_, Jandirus.Server.ServerPlayer? plComEscolha) = Entrar(ContaVelha);
			Conferir(plComEscolha?.Visual.CorAura is { R: 10, G: 20, B: 30 },
					 "uma cor JA GRAVADA sobrevive a carga -- o campo e OVERRIDE e nao cache "
				   + $"({plComEscolha?.Visual.CorAura?.ToString() ?? "NULA"}, esperado #0A141E)");

			// =============================================================
			// (f) O BINARIO VELHO LENDO UM SAVE NOVO
			// =============================================================
			// O outro sentido da compatibilidade, e ele nao se testa com dois binarios: o que faz
			// "save novo -> binario velho" ser seguro e o leitor IGNORAR propriedade que nao conhece.
			// Entao e isso que se mede, pelo `Carregar` de verdade (com as opcoes de verdade, que sao
			// privadas do `AccountStore` -- repeti-las aqui seria conferir a bancada com ela mesma).
			const string ContaDoFuturo = "bancadasavedofuturo";
			int trocas = 0;
			Semear(PecaSemCor, ContaDoFuturo, t =>
			{
				string fora = t.Replace("\"Corpo\": 0,",
					"\"Corpo\": 0,\n      \"CorDeUmaCoisaQueAindaNaoExiste\": 7,");
				trocas = fora.Length != t.Length ? 1 : 0;
				return fora;
			});
			Conferir(trocas == 1, "a peca do 'save do futuro' recebeu mesmo a propriedade desconhecida");
			(_, Jandirus.Server.ServerPlayer? plDoFuturo) = Entrar(ContaDoFuturo);
			Conferir(plDoFuturo is { Name: "Zv" } && plDoFuturo.Visual.CorAura is not null,
					 "um save com campo que este binario NAO conhece carrega inteiro (a propriedade e "
				   + "ignorada) -- e o que faz o sentido inverso da migracao ser seguro");

			// =============================================================
			// (g) OS DOIS CONTROLES, COM DEFEITO INJETADO
			// =============================================================
			// (g1) A MEDIDA ENXERGA O DESASTRE. Item (a) so vale se "carregar" pudesse ter dado errado:
			// aqui a cor e gravada numa FORMA que o parser nao le (texto no lugar do objeto), e o que se
			// cobra e que o `Carregar` devolva NULO -- o primeiro elo da cadeia que apaga a conta. E
			// tambem o aviso pra quem for mexer nisto: mudar a FORMA de um campo gravado mata contas.
			const string ContaPodre = "bancadasavepodre";
			int trocasPodres = 0;
			Semear(PecaSemCor, ContaPodre, t =>
			{
				// a peca ainda nao tem `CorAura`; o defeito e escrito no `CorCabelo`, que e o mesmo
				// `Rgb?` e ja esta la em todas as geracoes de save
				string fora = t.Replace("\"CorCabelo\": null", "\"CorCabelo\": \"branco\"");
				trocasPodres = fora.Length != t.Length ? 1 : 0;
				return fora;
			});
			Conferir(trocasPodres == 1, "CONTROLE: a peca do defeito recebeu mesmo o `Rgb` malformado");
			Conferir(loja.Carregar(ContaPodre) is null,
					 "CONTROLE: com um `Rgb` gravado numa forma que o parser nao le, o `Carregar` "
				   + "devolve NULO -- ou seja esta familia ENXERGA o elo que apaga a conta, e o verde "
				   + "de (a) e uma medida e nao uma formalidade");

			// (g2) A DERIVACAO LE MESMO O NOME E O INSTANTE. Sem este controle, um `CorDeAura.De` que
			// devolvesse uma CONSTANTE passaria em (c), (d) e (e) -- "estavel" e a propriedade mais
			// facil de acertar errando.
			//
			// POPULACAO E NAO PAR: metade dos sorteios da branco puro, entao dois personagens quaisquer
			// batem em ~24% das vezes e um par cravado aqui piscaria vermelho sozinho uma vez a cada
			// quatro rodadas.
			var porNome = new HashSet<string>();
			var porInstante = new HashSet<string>();
			for (int i = 0; i < 32; i++)
			{
				porNome.Add(Jandirus.Core.Appearance.CorDeAura.De(sv.Nome + i, sv.CriadoEm).ToString());
				porInstante.Add(Jandirus.Core.Appearance.CorDeAura.De(sv.Nome, sv.CriadoEm + i).ToString());
			}
			Conferir(porNome.Count >= 5 && porInstante.Count >= 5,
					 $"CONTROLE: a cor MUDA com o nome ({porNome.Count} tons em 32) e com o `CriadoEm` "
				   + $"({porInstante.Count} tons em 32) -- ela nao e uma constante disfarcada de derivacao");

			// E O ARQUIVO MEXIDO CONFIRMA NO CAMINHO INTEIRO: trocado o nome do personagem dentro do
			// JSON, a cor carregada acompanha a derivacao do nome NOVO. (Nao se cobra "e diferente da
			// outra" aqui pelo motivo do paragrafo acima -- o que se cobra e que ela SEGUE o campo.)
			const string ContaRenomeada = "bancadasaverenomeada";
			Semear(PecaSemCor, ContaRenomeada, t => t.Replace("\"Nome\": \"Zv\"", "\"Nome\": \"Zk\""));
			(_, Jandirus.Server.ServerPlayer? plOutroNome) = Entrar(ContaRenomeada);
			Jandirus.Core.Appearance.Rgb esperadaZk =
				Jandirus.Core.Appearance.CorDeAura.De("Zk", sv.CriadoEm);
			Conferir(plOutroNome is { Name: "Zk" } && plOutroNome.Visual.CorAura is { } ck
				  && ck.R == esperadaZk.R && ck.G == esperadaZk.G && ck.B == esperadaZk.B,
					 $"-- e no caminho INTEIRO ela segue o campo: com outro nome no arquivo sai "
				   + $"{plOutroNome?.Visual.CorAura?.ToString() ?? "NULA"} (esperado {esperadaZk})");

			// =============================================================
			// (h) A PECA DE DUAS GERACOES ATRAS
			// =============================================================
			// Ela nao tem OITO campos que o `CharacterSave` de hoje tem, nem o `FormasDeFrost` dentro do
			// `Visual`. E a prova de que "anexar campo no fim" ja e um caminho ANDADO neste save, e nao
			// uma aposta desta passada -- e ela exercita os `?? new()` do `ParaJogador`, que sao o que
			// separa "campo ausente" de um nulo estourando no primeiro tique.
			const string ContaAntiga = "bancadasaveantigo";
			string cruAntigo = Semear(PecaAntiga, ContaAntiga);
			Conferir(!cruAntigo.Contains("FormasDeFrost") && !cruAntigo.Contains("Disciplina")
				  && !cruAntigo.Contains("Mochila"),
					 "a peca de DUAS geracoes atras nao tem `FormasDeFrost`, `Disciplina` nem `Mochila`");
			(Jandirus.Server.AccountSave? accAntiga, Jandirus.Server.ServerPlayer? plAntigo) = Entrar(ContaAntiga);
			Conferir(accAntiga is not null && plAntigo is { Name: "AdmTres", Race: "Human" },
					 $"-- e ela carrega igual ({plAntigo?.Name ?? "NULO"}/{plAntigo?.Race ?? "-"})");
			Conferir(plAntigo is not null && plAntigo.Visual.FormasDeFrost is not null
				  && plAntigo.Mochila is not null && plAntigo.Social is not null
				  && plAntigo.Porte == "Medium",
					 "-- com os campos que faltam preenchidos pelo padrao, e nao nulos");
			Jandirus.Core.Appearance.Rgb esperadaAntiga = accAntiga?.Slots[0] is { } sa
				? Jandirus.Core.Appearance.CorDeAura.De(sa.Nome, sa.CriadoEm)
				: default;
			Conferir(plAntigo?.Visual.CorAura is { } ca
				  && ca.R == esperadaAntiga.R && ca.G == esperadaAntiga.G && ca.B == esperadaAntiga.B,
					 $"-- e ela tambem ganha a cor derivada ({plAntigo?.Visual.CorAura?.ToString() ?? "NULA"})");

			// ============================ E O ALCANCE DESTA LINHA, DITO EM VOZ ALTA ============================
			// A cor derivada DESTE personagem e o BRANCO PURO, que e o resultado mais provavel do sorteio
			// (~49%, ver `CorDeAura`) -- ou seja a linha acima, sozinha, e uma MOEDA. Medido: na rodada com
			// o `??=` trocado por um sorteio, ela ficou VERDE porque o sorteio tambem calhou de dar branco.
			//
			// QUATRO CARGAS, COM O RELOGIO ANDADO ENTRE ELAS, e o que sobra dela: uma cor que rerrola so
			// escapa se as QUATRO derem branco (0,49^3 ~ 12%), e o que carrega a afirmacao de verdade e o
			// `Zv` la em cima, cuja derivada NAO e branca (#EBFFFF) e portanto reprova sempre.
			// ================================================================================================
			var tonsDaAntiga = new HashSet<string>();
			if (plAntigo?.Visual.CorAura is { } caPrimeira) tonsDaAntiga.Add(caPrimeira.ToString());
			for (int i = 0; i < 3; i++)
			{
				EsperarORelogioAndar();
				(_, Jandirus.Server.ServerPlayer? deNovo) = Entrar(ContaAntiga);
				if (deNovo?.Visual.CorAura is { } cn) tonsDaAntiga.Add(cn.ToString());
			}
			Conferir(tonsDaAntiga.Count == 1,
					 $"-- e ela NAO MUDA em QUATRO cargas seguidas ([{string.Join(", ", tonsDaAntiga)}]) "
				   + "-- a linha de cima e branco contra branco, e quem carrega a afirmacao e o `Zv`");

			// =============================================================
			// (i) OS DOIS CORPOS QUE NAO SAEM DE SAVE NENHUM
			// =============================================================
			// A cor chega em tres corpos por caminhos diferentes, e ate aqui so o do JOGADOR tinha
			// medida. Os outros dois nunca passam pelo `ParaJogador`:
			//
			//   * O CLONE, que copia o `Visual` do dono (`GameServer.Clone.cs`, o `A.AuraR = AuraR` do
			//     `CopyMaker.dm:98`). Quem faz a copia e o `Appearance.Copiar()`, que lista campo a
			//     campo -- e o cabecalho dele ja diz, por escrito, que esquecer a linha da cor faz o
			//     clone nascer com o fallback CALADO, e que "isso so aparece jogando". Uma armadilha
			//     nomeada e sem teste e uma armadilha.
			//   * O NPC, cuja cor sai do lugar onde ele nasceu (`CorDeAura.DeSemente`) e nao de um save.
			//     O `Hash64("aura")` no meio dela existe pra o NPC de cabelo tal NAO ter sempre a aura
			//     tal; o que se cobra aqui e o que da pra cobrar sem inventar regra: que a semente do
			//     lugar mande (estavel) e que o resultado esteja na faixa do sorteio do DM.
			// =============================================================
			var doDono = new Jandirus.Core.Appearance.Appearance
			{
				Cabelo = "Goku",
				CorAura = new Jandirus.Core.Appearance.Rgb(210, 233, 201),
			};
			Jandirus.Core.Appearance.Appearance doClone = doDono.Copiar();
			Conferir(doClone.CorAura is { R: 210, G: 233, B: 201 },
					 $"o CLONE herda a chama do dono pelo `Appearance.Copiar()` "
				   + $"({doClone.CorAura?.ToString() ?? "NULA"}, esperado #D2E9C9) -- a linha que aquele "
				   + "metodo diz, por escrito, que so falha jogando");

			Jandirus.Core.Appearance.Rgb npc1 = Jandirus.Core.Appearance.CorDeAura.DeSemente(0xC0FFEE);
			Jandirus.Core.Appearance.Rgb npc2 = Jandirus.Core.Appearance.CorDeAura.DeSemente(0xC0FFEE);
			var tonsDeNpc = new HashSet<string>();
			for (ulong s = 0; s < 64; s++) tonsDeNpc.Add(Jandirus.Core.Appearance.CorDeAura.DeSemente(s).ToString());
			Conferir(npc1.R == npc2.R && npc1.G == npc2.G && npc1.B == npc2.B
				  && npc1.R >= Jandirus.Core.Appearance.CorDeAura.PicoDaFolha
				  && npc1.G >= Jandirus.Core.Appearance.CorDeAura.PicoDaFolha
				  && npc1.B >= Jandirus.Core.Appearance.CorDeAura.PicoDaFolha
				  && tonsDeNpc.Count >= 5,
					 $"e o NPC tira a dele da SEMENTE DO LUGAR ({npc1}), estavel e na faixa do sorteio "
				   + $"-- e 64 lugares dao {tonsDeNpc.Count} tons, entao ela nao e uma constante");

			_passos.Add($"  --     save velho: `Zv` -> {esperada}; `AdmTres` -> {esperadaAntiga} "
					  + $"(pasta de bancada {pasta})");
		}
		catch (Exception e)
		{
			// EXCECAO AQUI E FALHA, e nao bancada que morre calada: tudo o que esta familia toca e
			// disco, e "estourou" e um resultado tao valido quanto "deu a cor errada".
			Conferir(false, $"a familia do save velho rodou sem estourar ({e.GetType().Name}: {e.Message})");
		}
		finally
		{
			try { if (System.IO.Directory.Exists(pasta)) System.IO.Directory.Delete(pasta, true); } catch { /* pasta temporaria */ }
		}
	}

	// =====================================================================
	// 7. A IDA E A VOLTA -- pelo caminho que o jogo usa
	// =====================================================================
	/// <summary>
	/// TRANSFORMAR, VOLTAR E TRANSFORMAR DE NOVO -- o sintoma que o dono relatou, encenado.
	///
	/// ============================ POR QUE ISTO NAO SE TESTA EM PECA SOLTA ============================
	/// O defeito nao morava em nenhuma das pecas: `AuraDaForma`, `Aura.Preparar` e `RaiosDaForma`
	/// estavam todos certos, e testados, e verdes. O que estava errado era a ORDEM em que o
	/// `World.AoMudarForma` os chamava -- o cache do contorno morava DEPOIS do `return` da cinematica,
	/// entao todo caminho com cena pulava por cima dele. Nenhum teste de node isolado pode ver isso.
	///
	/// Por isso tudo aqui entra pelos DOIS metodos que o fio entrega (`AoMudarForma` e `AoCairEfeito`,
	/// abertos como `internal` exatamente pra isto) e le o resultado NO MATERIAL do sprite -- o que o
	/// shader vai desenhar, e nao o campo que alguem guardou.
	///
	/// O QUE FICA DE FORA, dito: o decodificador do byte do degrau (o `switch` do `GameClient`, que
	/// valida com `Enum.IsDefined`). Daqui pra baixo o degrau ja e um valor de enum.
	/// ============================================================================================
	/// </summary>
	private void AIdaEAVolta()
	{
		if (GetTree().Root.FindChild("World", true, false) is not Jandirus.Client.World mundo)
		{ Conferir(false, "achei o node `World` (sem ele nada abaixo passa pelo caminho do jogo)"); return; }

		int meuId = GameClient.Instance?.LocalId ?? 0;
		if (meuId == 0)
		{ Conferir(false, "a bancada esta conectada (o `AoMudarForma` resolve o corpo pelo id da rede)"); return; }

		if (GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D corpo
			|| corpo.GetNodeOrNull<CharacterVisual>("Visual") is not { } vis
			|| corpo.GetNodeOrNull<Aura>("Aura") is not { } aura
			|| corpo.GetNodeOrNull<CargaVisual>("Carga") is not { } carga
			|| corpo.GetNodeOrNull<RaiosDaForma>("Raios") is not { } raios)
		{ Conferir(false, "o corpo local tem Visual, Aura, Carga e Raios pra a ida e volta"); return; }

		// ============================ AS CENAS SAO DESCARTADAS NA HORA ============================
		// Os degraus `Estreia` e `Curta` fazem o `AoMudarForma` acender uma `Transformacao` de verdade.
		// Ela e o que se quer exercitar (e o `return` dela que pulava o cache), mas deixa-la viva
		// prenderia o corpo por segundos e envenenaria os passos seguintes desta bancada.
		//
		// `Free()` e nao `QueueFree()`: o `QueueFree` e ADIADO pro fim do quadro, e o contador
		// `PresosDeTeste` so cairia depois que este metodo ja tivesse acabado de medir. O `Free`
		// dispara o `_ExitTree` -> `Soltar()` agora, que e o caminho de limpeza que o jogo usa
		// quando a cena morre.
		// ====================================================================================
		List<Transformacao> Cenas() =>
			[.. corpo.GetParent().GetChildren().OfType<Transformacao>().Where(IsInstanceValid)];

		int presosAntes = Transformacao.PresosDeTeste;
		void Mudar(string de, string para, Jandirus.Core.Forms.DegrauDeCena degrau)
		{
			var antes = new HashSet<Transformacao>(Cenas());
			mundo.AoMudarForma(meuId, Jandirus.Core.Forms.Catalogo.Rede(de),
							   Jandirus.Core.Forms.Catalogo.Rede(para), degrau);
			foreach (Transformacao t in Cenas())
				if (!antes.Contains(t)) t.Free();
		}

		string Base = Jandirus.Core.Forms.Catalogo.IdBase;
		float Contorno() => vis.ContornoNoMaterialDeTeste().Forca;
		Color CorDoContorno() => vis.ContornoNoMaterialDeTeste().Cor;
		var amarelo = new Color("ffd24a");

		// ============================ "ACESO" E UMA FAIXA, E NAO UM VALOR ============================
		// Estas checagens cobravam `> 0,99` -- o literal `1.0` que o `World` escrevia, repetido aqui.
		// Desde que o contorno passou a RESPIRAR (`CharacterVisual.ForcaNaFaseDoPulso`) nao ha mais um
		// numero certo: o que o material tem depende da fase em que o relogio estava, e entre dois
		// passos deste bloco passam quadros de jogo de verdade.
		//
		// A faixa nao afrouxa o teste. O defeito que este bloco existe pra pegar e o contorno preso em
		// ZERO ("virei SSJ de novo e o outline nao voltou"), e zero esta a 0,455 de distancia do fundo
		// da faixa -- longe de qualquer folga. E os numeros saem do CORE, entao mexer na forca do
		// contorno nao volta a exigir edicao aqui.
		// ========================================================================================
		float topoDoPulso = Jandirus.Core.Forms.Catalogo.ForcaDoContorno;
		float fundoDoPulso = topoDoPulso * (float)Jandirus.Core.Forms.Catalogo.PisoDoPulsoDoContorno;
		bool Aceso() => Contorno() >= fundoDoPulso - 0.01f && Contorno() <= topoDoPulso + 0.01f;

		// ---------------------------------------------------------------------
		// A. O CONTORNO QUE NAO VOLTAVA -- "se eu voltar pra base e virar ssj dnv o outline n volta"
		// ---------------------------------------------------------------------
		// O Ki passa dos 100% pelo canal do servidor, que e o unico jeito de o contorno existir desde
		// que ele deixou de significar "estou transformado" e passou a significar "passei do limite".
		mundo.AoCairEfeito("aura_ki", 1);
		Mudar(Base, Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(Mathf.IsZeroApprox(Contorno()),
				 $"na base, sobrecarregado, ainda nao ha contorno ({Contorno():0.##})");

		// 1) A ESTREIA. E o primeiro caminho com cena, e o que escondia o defeito: o cache era escrito
		//    DEPOIS do `return`, entao ele nunca era escrito aqui.
		Mudar(Base, "ssj1", Jandirus.Core.Forms.DegrauDeCena.Estreia);
		Conferir(Aceso(), $"a ESTREIA acende o contorno do dono ({Contorno():0.##})");
		Conferir(CorDoContorno().IsEqualApprox(amarelo),
				 $"e ele sai no amarelo da escada, nao no tom da aura ({CorDoContorno().ToHtml(false)})");

		// ============================ E A LINHA DE CIMA PASSA POR COINCIDENCIA ============================
		// A `Aura` do `ssj1` e `ffd24a` -- o MESMO hexa do amarelo da escada. Um `GuardarContornoDaForma`
		// que voltasse a ler `def.Aura` (que e como ele era) devolveria exatamente esta cor e a
		// checagem acima ficaria verde com o acoplamento de volta. Ela prova que a cor esta certa; nao
		// prova de ONDE ela veio.
		//
		// O veneno separa as duas leituras: com a aura do SSJ1 preta, o contorno tem que continuar
		// amarelo e a CHAMA tem que ficar preta. A segunda metade e o controle e nao enfeite -- sem
		// ela, um `AoMudarForma` que nao relesse nada (cache, corpo errado, guarda cedo demais)
		// passaria na primeira com a cor velha ainda escrita no material.
		//
		// O do <see cref="AsCoresNaoSaoAAura"/> mede a FUNCAO; este mede o CAMINHO -- sao dois
		// lugares diferentes onde a aura pode voltar a mandar no contorno.
		// ==========================================================================================
		FormaDef ss1 = Jandirus.Core.Forms.Catalogo.Def("ssj1")!;
		string auraDeVerdade = ss1.Aura;
		var veneno = new Color("010203");
		try
		{
			ss1.Aura = "010203";
			// IDA E VOLTA e nao so uma ida: o caminho so rele a forma quando ela MUDA, entao pedir
			// `ssj1` estando em `ssj1` poderia nao passar por lugar nenhum.
			Mudar("ssj1", Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			Mudar(Base, "ssj1", Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			Conferir(CorDoContorno().IsEqualApprox(amarelo),
					 $"e com a AURA do SSJ1 envenenada o contorno continua amarelo "
				   + $"({CorDoContorno().ToHtml(false)})");
			Conferir(aura.CorDaChama.IsEqualApprox(veneno),
					 $"-- e a CHAMA pegou o veneno, ou seja o caminho releu mesmo o catalogo "
				   + $"({aura.CorDaChama.ToHtml(false)})");
		}
		finally { ss1.Aura = auraDeVerdade; }

		// E DEVOLVE A CHAMA. O `finally` conserta o catalogo, mas o node continua com a cor preta
		// guardada -- e ele so a reescreve na proxima troca. Sem estas duas linhas a foto do fim da
		// bancada sairia com a aura da forma preta, e o defeito estaria a mil linhas daqui.
		Mudar("ssj1", Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Mudar(Base, "ssj1", Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(aura.CorDaChama.IsEqualApprox(new Color(auraDeVerdade)),
				 $"e tirado o veneno a chama volta a cor de verdade ({aura.CorDaChama.ToHtml(false)})");

		// 2) A VOLTA. Este caminho nao tem cena e SEMPRE zerou a forca -- e era so ele que a escrevia.
		Mudar("ssj1", Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(Mathf.IsZeroApprox(Contorno()), $"voltar pra base apaga o contorno ({Contorno():0.##})");

		// 3) E A SEGUNDA VEZ, QUE E O SINTOMA. Maestria abaixo de 50% -> cena CURTA -> o mesmo `return`.
		//    Com o cache preso em zero, o contorno nao voltava NUNCA MAIS nesta sessao.
		Mudar(Base, "ssj1", Jandirus.Core.Forms.DegrauDeCena.Curta);
		Conferir(Aceso(),
				 $"e virar Super Saiyajin DE NOVO devolve o contorno -- o sintoma A ({Contorno():0.##})");

		// 4) O TIRO DE MISERICORDIA: o proximo pacote de carga. Era ELE quem apagava, reescrevendo o
		//    sprite com o valor velho do cache. Se o cache nao tiver sido atualizado no passo 3, esta
		//    linha e a que reprova -- e em jogo ela e invisivel, porque o contorno aparece por um
		//    instante durante a cena e some sozinho depois.
		//
		//    ============================ E O PACOTE REPETIDO VIROU NO-OP ============================
		//    Desde que a sobrecarga passou a morar num CONJUNTO por id (`World._sobrecarregados`), um
		//    `aura_ki` que repete o valor nao reescreve nada -- o `MarcarSobrecarga` so trabalha na
		//    mudanca. Ou seja esta linha sozinha ja nao poderia reprovar por "valor velho vencendo",
		//    porque nao ha mais valor velho nem segundo dono: o passo 5 logo abaixo, que DESLIGA e
		//    religa, e quem exercita o caminho de verdade. Ela fica como guarda do dia em que alguem
		//    devolver um segundo escritor pro mesmo pixel.
		//    ====================================================================================
		mundo.AoCairEfeito("aura_ki", 1);
		Conferir(Aceso(),
				 $"e o pacote de carga seguinte NAO o apaga (era o valor velho vencendo) ({Contorno():0.##})");

		// 5) O CONTORNO E DO KI E NAO DA FORMA -- as duas metades. Sem a segunda, "acende com Ki alto"
		//    passaria num contorno que simplesmente nunca apaga.
		mundo.AoCairEfeito("aura_ki", 0);
		Conferir(Mathf.IsZeroApprox(Contorno()),
				 $"o Ki voltando ao normal apaga o contorno SEM sair da forma ({Contorno():0.##})");
		mundo.AoCairEfeito("aura_ki", 1);
		Conferir(Aceso(),
				 $"e ele volta quando o Ki passa de novo, sem a forma precisar avisar "
			   + $"(guardar != acender) ({Contorno():0.##})");

		// ---------------------------------------------------------------------
		// B. OS RAIOS QUE NUNCA APAGAVAM -- a metade AUDIVEL do mesmo defeito
		// ---------------------------------------------------------------------
		// Quando o cache do contorno se separou do corpo remoto, o unico ponto que chamava
		// `RaiosDaForma.Definir` ficou alcancavel so pelo corpo ALHEIO. O dono era aceso pela
		// cinematica e nunca apagado: voltar pra base deixava o jogador crepitando dourado em forma
		// base pra sempre, com o estalo tocando a cada dois segundos.
		//
		// COMO REPROVA SE A REGRA SUMIR: ponha de volta o `if (corpo != _local) return;` no comeco do
		// que hoje e `AcenderFormaNoCorpo` e as tres linhas abaixo caem juntas.
		Mudar("ssj1", Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Mudar(Base, "ssj2", Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(raios.EmitindoDeTeste, "o degrau INSTANTANEO acende a faisca no corpo do DONO");
		Mudar("ssj2", Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(!raios.EmitindoDeTeste,
				 "e voltar pra base APAGA (era daqui que vinha o crepitar eterno na forma base)");

		// E O SSJ1 NAO LIGA O NODE PRA EMITIR ZERO. `Raios: > 0` e nao `def != null` -- senao sobra um
		// `_Process` e um sorteio por quadro, por corpo, pra nao desenhar nada.
		Mudar(Base, "ssj1", Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(!raios.EmitindoDeTeste, "o SSJ1 (Raios=0) nao liga o emissor pra soltar nada");
		Mudar("ssj1", Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);

		// ---------------------------------------------------------------------
		// C. A AURA NAO GUARDA A COR DA FORMA ANTERIOR
		// ---------------------------------------------------------------------
		// "a aura da base ainda ta brilhando, e ela sai DOURADA". `Apagar()` desliga, mas nao DESFAZ:
		// a folha (`AuraSSjBig`, arte ja dourada) e a cor guardada continuavam la, esperando a proxima
		// tecla C. Medir `AcesaDeTeste` nao ve isso -- ela esta apagada mesmo. O defeito e o que ela
		// vai fazer DEPOIS.
		//
		// A IDA E O VENENO, e por isso ela vem antes de cada volta em vez de uma vez so: as tres formas
		// escolhidas caem em folhas DIFERENTES (SSJ dourada / LSSJ / colorivel), entao a volta tem que
		// desfazer tres estados distintos. Uma unica ida provaria so o caso que ela deixou.
		//
		// COMO REPROVA SE A REGRA SUMIR: devolva o ramo `def == null` de `PrepararAuraDaForma` a ser so
		// `aura.Apagar()` e as duas linhas da volta (cor e folha) caem nas TRES formas.
		// O BLUE SAIU DA COLORIVEL e virou a `FieryGodBlue`, entao a lista ganhou uma QUARTA forma pra
		// a volta continuar tendo que desfazer estados distintos: sem o `ssg` aqui, a chama quente das
		// divinas nunca seria vestida por este teste e a volta dela ficaria sem prova.
		foreach (string id in new[] { "ssj1", "legendary", "blue", "ssg" })
		{
			FormaDef d = Jandirus.Core.Forms.Catalogo.Def(id)!;
			// A ESPERADA SAI DO PROPRIO `Catalogo.Folha`, e nao de uma tabela escrita aqui: a tabela
			// paralela que morava nestas linhas ja tinha o Blue na folha errada no minuto em que a
			// derivacao mudou -- e ela teria reprovado o codigo CERTO.
			// AS QUATRO DAQUI TEM FOLHA (nenhuma e Ultra Instinto). O `?? ""` derruba a comparacao
			// abaixo em vez de estourar, caso alguem troque um id desta lista por um que nao tenha.
			string esperada = SpriteDeAura.CaminhoDa(Jandirus.Core.Forms.Catalogo.Folha(d)) ?? "";

			Mudar(Base, id, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			Conferir(aura.CorDaChama.IsEqualApprox(new Color(d.Aura)),
					 $"`{id}`: a ida guarda a cor da forma ({aura.CorDaChama.ToHtml(false)}, "
				   + $"esperado {d.Aura})");
			Conferir(aura.DesenhoDeTeste.FolhaDeTeste == esperada
				  && carga.DesenhoDeTeste.FolhaDeTeste == esperada,
					 $"`{id}`: a ida poe as DUAS chamas na folha {esperada.GetFile()} "
				   + $"(aura {aura.DesenhoDeTeste.FolhaDeTeste.GetFile()}, "
				   + $"carga {carga.DesenhoDeTeste.FolhaDeTeste.GetFile()})");

			Mudar(id, Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
			Conferir(!aura.AcesaDeTeste, $"`{id}` -> base: a aura apaga");
			// A VOLTA E PRA A COR **PESSOAL DESTE CORPO**, e nao mais pra a constante compartilhada:
			// a chama da base e a sorteada no nascimento (`Appearance.CorAura`), que chegou aqui pelo
			// `PeerLook` e mora no node. Comparar com o `Aura.CorDoKiCru` passaria a ser comparar com
			// o FALLBACK -- ou seja, ficaria verde exatamente no dia em que a cor sorteada nao
			// chegasse.
			Conferir(aura.CorDaChama.IsEqualApprox(aura.CorPessoal),
					 $"`{id}` -> base: a cor guardada volta a ser a PESSOAL deste corpo e nao a da "
				   + $"forma ({aura.CorDaChama.ToHtml(false)}, pessoal {aura.CorPessoal.ToHtml(false)})");
			Conferir(aura.DesenhoDeTeste.FolhaDeTeste == SpriteDeAura.FolhaBase
				  && carga.DesenhoDeTeste.FolhaDeTeste == SpriteDeAura.FolhaBase,
					 $"`{id}` -> base: as duas chamas voltam pra a folha colorivel "
				   + $"(aura {aura.DesenhoDeTeste.FolhaDeTeste.GetFile()}, "
				   + $"carga {carga.DesenhoDeTeste.FolhaDeTeste.GetFile()})");
		}

		// ---------------------------------------------------------------------
		// D. A CHAMA DO C TEM QUE ILUMINAR -- "a aura das transformaçoes n estao brilhando ao apertar C"
		// ---------------------------------------------------------------------
		// A luz vivia dentro do `Aura.Acender`. Depois que a forma passou a apenas PREPARAR a aura,
		// ninguem mais chamava aquele metodo no jogo: quem desenha a chama do C e a `CargaVisual`,
		// que nao tinha luz nenhuma. Sprite sem luz, no escuro, e uma mancha cinza.
		//
		// O teste vai pelo CANAL do servidor (`aura_carga`), que e o caminho da tecla C, e nao pelo
		// node -- chamar `aura.Acender` na mao provaria o metodo que o jogo deixou de usar, que foi
		// exatamente o que escondeu o defeito.
		//
		// COMO REPROVA SE A REGRA SUMIR: devolva o `CargaVisual.Pintar` a so suprimir o desenho da
		// aura (o antigo `SuprimirDesenho`) e as quatro linhas de luz abaixo caem juntas.
		Iluminacao.EscuridaoDeTeste(1f);   // breu: de dia a luz e ZERO de proposito, ver a secao da noite
		mundo.AoCairEfeito("aura_ki", 0);
		mundo.AoCairEfeito("aura_carga", 0);
		Conferir(Mathf.IsZeroApprox(aura.EnergiaDeTeste),
				 $"sem carga e sem forma nao ha luz nenhuma ({aura.EnergiaDeTeste:0.###})");

		// ============================ NA BASE A CHAMA DESENHA E NAO ILUMINA ============================
		// Esta linha ja mediu o CONTRARIO ("segurar C na BASE acende a luz da chama"), e era a regra
		// errada: o dono viu a luz acender na base ao passar dos 100% e disse que na base "a unica coisa
		// q deve ficar ativa e o node carga". O node `Aura` e da FORMA.
		//
		// E A CHAMA CONTINUA DESENHADA -- as duas medidas juntas, senao "na base nao acende" passaria
		// tambem com a carga inteira quebrada, que e o defeito de onde este conserto veio.
		//
		// ============================ E SAO DOIS NODES, PERGUNTADOS UM A UM ============================
		// "ha aura na tela?" nao e a pergunta: na base HA, e ela e legitima -- e a da `CargaVisual`. A
		// pergunta e DE QUEM e cada desenho, e por isso as tres linhas abaixo nomeiam o node em vez de
		// olhar o corpo de longe. O `Aura` responde por nada (nem luz nem pixel) e a `CargaVisual` responde
		// por tudo o que a base tem.
		// ==========================================================================================
		//
		// COMO REPROVA SE A REGRA SUMIR: devolva a guarda de `Aura.Aplicar` a `!_acesa && !_cargaAtiva`
		// (tire o `_temForma`) e a linha da LUZ cai. A do DESENHO tem outro dono: troque, no mesmo
		// `Aplicar`, o `_desenho.Definir(_acesa && !_cargaAtiva, ...)` por `_acesa || _cargaAtiva` e ela
		// cai -- sao as duas chamas empilhadas que o dono fotografou (uma clara atras, a outra na frente).
		mundo.AoCairEfeito("aura_carga", 1);
		Conferir(Mathf.IsZeroApprox(aura.EnergiaDeTeste),
				 $"segurar C na BASE NAO acende a luz -- na base o node `Aura` nao entra em jogo "
			   + $"({aura.EnergiaDeTeste:0.###})");
		Conferir(!aura.DesenhoDeTeste.Visible,
				 "-- nem o DESENHO dele: na base o node `Aura` nao poe pixel nenhum na tela");
		Conferir(carga.DesenhoDeTeste.Visible,
				 "-- mas a chama da CARGA continua desenhada, que e o que a base tem");

		// TRANSFORMADO A COR E A DA FORMA, e este era o remendo que a carga obrigava: a folha do SSJ e
		// arte ja dourada e ignora a tinta (`SpriteDeAura.SemTinta`), entao copiar o azul que a carga
		// mandava daria uma chama dourada lançando luz AZUL. Havia um `Aura.CorDaLuz(pedida)` so pra
		// escolher entre as duas; sem cor propria na carga ele deixou de ter o que escolher e foi
		// deletado -- desenho e luz saem do mesmo `_corAcesa` por construcao.
		// E COM O MESMO C SEGURADO, VESTIR A FORMA ACENDE. E aqui que a chama do C vira luz -- este e o
		// defeito original desta secao ("a aura das transformaçoes n estao brilhando ao apertar C") e
		// continua sendo cobrado, so que agora do lado certo da guarda.
		Mudar(Base, "ssj1", Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(aura.EnergiaDeTeste > 0,
				 $"e vestir a FORMA com o C ainda segurado acende a luz ({aura.EnergiaDeTeste:0.##})");
		Conferir(aura.CorDaLuzDeTeste.IsEqualApprox(amarelo),
				 $"na cor da FORMA, que e a cor que aquela folha desenha ({aura.CorDaLuzDeTeste.ToHtml(false)})");

		// E A VOLTA APAGA, sem soltar o C. A troca de forma tem que reavaliar a luz no mesmo quadro
		// (`Aura.Preparar` repinta quem esta aceso) -- senao quem voltasse pra base carregando ficaria
		// iluminando com a luz do Super Saiyajin ate soltar a tecla.
		Mudar("ssj1", Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		Conferir(Mathf.IsZeroApprox(aura.EnergiaDeTeste),
				 $"e voltar pra BASE apaga a luz sem soltar o C ({aura.EnergiaDeTeste:0.###})");
		Conferir(carga.DesenhoDeTeste.Visible,
				 "-- com a chama da carga sobrevivendo a volta, porque o C nao foi solto");

		// E SOLTAR O C APAGA A CHAMA. Sem esta metade, "acende ao carregar" passaria numa chama que
		// nunca apaga -- que e o defeito irmao, ja pago pelos raios da secao B. Mede o DESENHO porque
		// na base ja nao ha luz pra medir.
		mundo.AoCairEfeito("aura_carga", 0);
		Conferir(!carga.DesenhoDeTeste.Visible,
				 "soltar o C apaga a chama da carga");
		Conferir(Mathf.IsZeroApprox(aura.EnergiaDeTeste),
				 $"e a luz segue apagada ({aura.EnergiaDeTeste:0.###})");

		// ---------------------------------------------------------------------
		// E. O TREMOR DA CENA -- "ta mt rapido, deveria durar mais tempo e tremer um pouco mais devagar"
		// ---------------------------------------------------------------------
		// Duas medidas, porque sao duas queixas: quanto o solavanco DURA (a queda) e quantas vezes por
		// segundo a camera troca de rumo (a cadencia). E as duas so valem com o CONTROLE do impacto ao
		// lado: o `Sacudir` e compartilhado com soco, critico e embate de clash, entao "a cinematica
		// ficou mais lenta" e meia verdade se o combate tiver ficado junto.
		//
		// O tempo e dado na mao (`TickDoTremorDeTeste`) e nao esperado em quadros de jogo: medir
		// cadencia esperando o relogio de verdade mediria o fps da bancada, que e justamente o defeito.
		const double passo = 1.0 / 240.0;

		double Duracao(float forca, float queda, float cadencia)
		{
			mundo.TickDoTremorDeTeste(10.0);          // zera o que estivesse tremendo antes
			mundo.Sacudir(forca, 1f, queda, cadencia);
			double t = 0;
			while (mundo.TremorDeTeste > 0 && t < 10) { mundo.TickDoTremorDeTeste(passo); t += passo; }
			return t;
		}

		// REACENDE A CADA TICK de proposito: e o que o rumor continuo da aura base faz em jogo, e e o
		// unico jeito de contar rumos de uma tremida que nao esta morrendo no meio da conta.
		int Rumos(float queda, float cadencia, double segundos, double dt)
		{
			mundo.TickDoTremorDeTeste(10.0);
			int antes = Jandirus.Client.World.RumosDoTremorDeTeste;
			for (double t = 0; t < segundos - dt / 2; t += dt)
			{
				mundo.Sacudir(Jandirus.Core.Forms.Cinematicas.ForcaDoTremor, 1f, queda, cadencia);
				mundo.TickDoTremorDeTeste(dt);
			}
			return Jandirus.Client.World.RumosDoTremorDeTeste - antes;
		}

		float forcaDoBeat = Jandirus.Core.Forms.Cinematicas.ForcaDoTremor;
		float quedaDaCena = Jandirus.Core.Forms.Cinematicas.QuedaDoTremor;
		float cadenciaDaCena = Jandirus.Core.Forms.Cinematicas.CadenciaDoTremor;

		// ============================ OS NUMEROS DO IMPACTO SAO LITERAIS DE PROPOSITO ============================
		// 40 de queda e um rumo por 1/60 s sao os valores HISTORICOS do tremor de combate, de antes desta
		// mexida. Aqui eles nao sao uma constante copiada, sao a linha de base que o teste existe pra
		// defender: se alguem "arrumar" o tremor da cinematica mexendo no padrao do `Sacudir`, estas duas
		// linhas reprovam e dizem exatamente qual sistema foi junto sem querer.
		// ==================================================================================================
		double duracaoDoSoco = Duracao(forcaDoBeat, 40f, 1f / 60f);
		Conferir(Math.Abs(duracaoDoSoco - 6.0 / 40.0) < 0.02,
				 $"o solavanco de IMPACTO continua durando os 0,15 s de sempre ({duracaoDoSoco:0.###} s)");

		double duracaoDaCena = Duracao(forcaDoBeat, quedaDaCena, cadenciaDaCena);
		Conferir(Math.Abs(duracaoDaCena - forcaDoBeat / quedaDaCena) < 0.02,
				 $"e o da CINEMATICA dura `Forca/Queda` = {forcaDoBeat / quedaDaCena:0.##} s "
			   + $"({duracaoDaCena:0.###} s)");
		Conferir(duracaoDaCena > duracaoDoSoco * 2,
				 $"-- ou seja o tremor da transformacao dura MAIS que o de um soco "
			   + $"({duracaoDaCena:0.##} s contra {duracaoDoSoco:0.##} s)");

		// A CADENCIA. `1 +` porque a tremida que NASCE ja sorteia o primeiro rumo antes do relogio andar.
		int rumosDoSoco = Rumos(40f, 1f / 60f, 1.0, passo);
		Conferir(Math.Abs(rumosDoSoco - 61) <= 2,
				 $"o tremor de IMPACTO continua trocando de rumo ~60x por segundo ({rumosDoSoco})");

		int esperadoNaCena = 1 + (int)(1.0 / cadenciaDaCena);
		int rumosDaCena = Rumos(quedaDaCena, cadenciaDaCena, 1.0, passo);
		Conferir(Math.Abs(rumosDaCena - esperadoNaCena) <= 2,
				 $"e o da CINEMATICA troca ~{1.0 / cadenciaDaCena:0.#}x por segundo ({rumosDaCena})");
		Conferir(rumosDaCena < rumosDoSoco / 2,
				 $"-- ou seja ele treme bem mais DEVAGAR que o de um soco "
			   + $"({rumosDaCena} contra {rumosDoSoco} rumos por segundo)");

		// E O FPS NAO MANDA MAIS NA VELOCIDADE. Era o defeito de fundo: o rumo era sorteado por QUADRO,
		// entao a mesma cena tremia 60x/s numa maquina e 144x/s na outra. Duas medicoes do mesmo segundo
		// com passos diferentes tem que dar o mesmo numero.
		int rumosGrossos = Rumos(quedaDaCena, cadenciaDaCena, 1.0, 1.0 / 30.0);
		Conferir(Math.Abs(rumosGrossos - rumosDaCena) <= 2,
				 $"e a velocidade do tremor nao depende mais do fps ({rumosGrossos} a 30 quadros/s "
			   + $"contra {rumosDaCena} a 240)");

		mundo.TickDoTremorDeTeste(10.0);   // nao deixa a camera tremendo pra os passos seguintes

		// ---------------------------------------------------------------------
		// E2. O TREMOR ALCANCA O PLANETA -- "o camera shake tb afeta todos no planeta"
		// ---------------------------------------------------------------------
		// Tres coisas que so se medem juntas, porque a regra antiga (`souEu ? cheio : metade`) passaria
		// em duas delas sozinha:
		//
		//   1. quem esta PERTO leva o solavanco INTEIRO mesmo nao sendo o dono da cena -- e o `Quake()`
		//      do DM (`Ascension.dm:8`), que sacode `view(src)` com o mesmo `rand(-8,8)` pra todos;
		//   2. quem esta do OUTRO LADO do planeta ainda sente -- pedido literal do dono;
		//   3. e sente MENOS, senao "afeta todos" viraria o liquidificador que a metade evita.
		//
		// O CORPO DE MENTIRA E UM `Node2D` PELADO de proposito: o que se mede aqui e a distancia ate a
		// camera, e um boneco completo traria pose, aura e visual pra dentro de uma conta que nao os
		// usa. `souEu: false` e o que importa -- e a bandeira que a regra antiga consultava.
		//
		// O TERCEIRO DEGRAU -- "fora de um planeta nao treme NADA" -- era buraco declarado aqui: ele
		// depende da ZONA da bancada (a Terra) e a nota antiga dizia que forjar outra zona "mediria o
		// forjamento". Nao mede: escrever a zona poe a bancada no outro estado legitimo do jogo (estar no
		// espaco), e o codigo que responde continua sendo o de producao. Ver `GameClient.ZonaDeTeste` e o
		// bloco E3 logo abaixo, onde esse degrau agora e medido.
		Conferir(Jandirus.Core.World.Espaco.EhPlaneta(GameClient.Instance?.Zone ?? default),
				 $"a bancada esta num PLANETA (zona `{GameClient.Instance?.Zone.Name}`) -- "
			   + "sem isso as duas medidas abaixo mediriam o caso do espaco");

		float PesoAte(float dx)
		{
			var falso = new Node2D { Name = "CorpoDeMentira" };
			// A POSICAO DEPOIS DE ENTRAR NA ARVORE: `GlobalPosition` num node orfao e a posicao local,
			// e o corpo de mentira nasceria na origem do mundo -- o que faria a medida de PERTO virar
			// uma medida de longe sem nada acusar.
			corpo.GetParent().AddChild(falso);
			falso.GlobalPosition = corpo.GlobalPosition + new Vector2(dx, 0);
			Transformacao cena = Transformacao.Rodar(
				corpo.GetParent(), falso, Jandirus.Core.Forms.Catalogo.Def("ssj1")!,
				Jandirus.Core.Forms.Cinematicas.Ssj1, souEu: false);
			float peso = cena.PesoDoTremorDeTeste;
			cena.Free();    // `Free` e nao `QueueFree` pelo mesmo motivo do `Mudar` la em cima
			falso.Free();
			return peso;
		}

		float raioCheio = Jandirus.Core.Forms.Cinematicas.RaioDoTremorCheio;
		float ecoEsperado = Jandirus.Core.Forms.Cinematicas.PesoDoTremorDeLonge;

		float pesoPerto = PesoAte(raioCheio * 0.5f);
		Conferir(Mathf.IsEqualApprox(pesoPerto, 1f),
				 $"a {raioCheio * 0.5f:0} px do corpo alheio o tremor chega INTEIRO ({pesoPerto:0.##}) "
			   + $"-- o `view(src)` do `Quake()` sao {raioCheio:0} px");

		float pesoLonge = PesoAte(raioCheio * 8f);
		Conferir(pesoLonge > 0f,
				 $"e do outro lado do planeta ({raioCheio * 8f:0} px) ele ainda chega ({pesoLonge:0.##}) "
			   + "-- \"o camera shake tb afeta todos no planeta\"");
		Conferir(Mathf.IsEqualApprox(pesoLonge, ecoEsperado) && pesoLonge < pesoPerto,
				 $"mas so como eco: {pesoLonge:0.##} contra {pesoPerto:0.##} de perto "
			   + $"(esperado {ecoEsperado:0.##})");

		mundo.TickDoTremorDeTeste(10.0);   // as cenas de mentira podem ter deixado tremida no ar

		// ---------------------------------------------------------------------
		// E3. O ALCANCE INTEIRO -- "a musica de transformaçao toca pra todos os jogadores no planeta"
		// ---------------------------------------------------------------------
		// ============================ AS DUAS METADES QUE NUNCA SE ENCONTRARAM ============================
		// O alcance de uma cena de transformacao e feito de duas decisoes que moram em lados opostos do fio,
		// e por isso ele nunca teve bancada:
		//
		//   * o SERVIDOR recorta quem recebe (`foreach ZoneList(pl.Zone.Hash)`, `AnunciarForma`);
		//   * o CLIENTE nao recorta NADA -- o `Transformacao._Ready` poe a faixa no ar sem uma condicao
		//     sequer, e o tremor so pergunta a distancia.
		//
		// Medir so o cliente da o resultado bonito e errado: "a musica toca sempre" e verdade e nao responde
		// a pergunta do dono, que e *pra quem*. E medir so o servidor prova que o pacote saiu pra tres
		// pessoas sem provar que alguma delas ouviu alguma coisa.
		//
		// POR ISSO AS DUAS ESTAO NESTE BLOCO, no mesmo placar. Quem quebrar uma das duas metades quebra
		// linhas vizinhas, e quem ler o log ve a regra inteira de uma vez.
		// ============================================================================================
		AudioDirector? audio = AudioDirector.Instance;
		if (audio == null)
		{
			Conferir(false, "achei o `AudioDirector` (sem ele o alcance da musica nao se mede)");
		}
		else
		{
			string temaDaCena = $"res://Assets/Sounds/Music/{Jandirus.Core.Forms.Cinematicas.Ssj1.Musica}";

			// A cena de mentira, igual a do bloco de cima -- e devolvida pra ser fechada depois de a
			// medicao acontecer, porque a MUSICA e efeito do `_Ready` e nao de um metodo que da pra chamar.
			Transformacao CenaAlheia(float dx)
			{
				var falso = new Node2D { Name = "CorpoDeMentira" };
				corpo.GetParent().AddChild(falso);
				falso.GlobalPosition = corpo.GlobalPosition + new Vector2(dx, 0);
				return Transformacao.Rodar(
					corpo.GetParent(), falso, Jandirus.Core.Forms.Catalogo.Def("ssj1")!,
					Jandirus.Core.Forms.Cinematicas.Ssj1, souEu: false);
			}

			void Fechar(Transformacao t)
			{
				Node2D? alvo = t.GetParent()?.GetNodeOrNull<Node2D>("CorpoDeMentira");
				t.Free();
				alvo?.Free();
			}

			// --- 1. A MUSICA E DE QUEM ESTA NO PLANETA, E NAO SO DE QUEM VIRA ---------
			// ============================ ESTA E A LINHA QUE O COMENTARIO MENTIA SOBRE ============================
			// O `Rodar` afirmou por muito tempo que `souEu` ligava "a musica e o tremor". A musica NUNCA passou
			// por ali -- e ninguem duvidou, porque quem testa o jogo sozinho e sempre o dono da cena e ouve a
			// faixa nos dois casos. `souEu: false` e o unico jeito de a diferenca aparecer.
			//
			// COMO REPROVA SE A REGRA SUMIR: ponha `if (_souEu)` antes do `AudioDirector.Musica(...)` do
			// `Transformacao._Ready` (que e exatamente o que o comentario descrevia) e as duas linhas caem.
			// ================================================================================================
			// DO ZERO DE VERDADE. Isto era `PararCamada(CamadaDeTeste)`, que apaga o pedido de UMA
			// camada -- e o bloco E2 rodou cenas de RAIVA e de TRANSFORMACAO, fechadas na mao antes de
			// a faixa acabar, entao havia pedido de pe em duas camadas. Apagar so a de cima descobria a
			// outra, e a medicao seguinte media um combate que nunca entrou no ar.
			audio.Silenciar();
			Transformacao alheia = CenaAlheia(raioCheio * 8f);
			Conferir(audio.CamadaDeTeste == AudioDirector.Camada.Transformacao,
					 $"a cena de OUTRA pessoa poe musica na minha tela (camada `{audio.CamadaDeTeste}`) "
				   + "-- \"a musica de transformaçao toca pra todos os jogadores no planeta\"");
			Conferir(audio.FaixaDeTeste == temaDaCena,
					 $"...e e o tema DA CENA, e nao uma faixa qualquer (`{audio.FaixaDeTeste.GetFile()}`)");
			Fechar(alheia);

			// --- 2. E ELA ABAFA A DE COMBATE, QUE E O `duck_battle_music` ------------
			// A hierarquia `Lugar < Menu < Combate < Transformacao` e o `emit_TransformMusic` do DM
			// (`BattleMusic.dm:135-143`), que corta o canal de raiva e abafa a batalha pela duracao da faixa.
			// Sao DOIS sentidos e os dois quebram sozinhos: a de cima tem que INTERROMPER, e a de baixo tem
			// que NAO interromper (senao a primeira troca de musica de lugar cortaria o tema no meio).
			audio.Silenciar();
			audio.Musica(Trilha.Combate(), AudioDirector.Camada.Combate);
			bool combateEntrou = audio.CamadaDeTeste == AudioDirector.Camada.Combate;
			Transformacao porCima = CenaAlheia(raioCheio * 0.5f);
			Conferir(combateEntrou && audio.CamadaDeTeste == AudioDirector.Camada.Transformacao,
					 $"a transformacao ABAFA a musica de combate (o `duck_battle_music` do DM) "
				   + $"-- combate entrou: {combateEntrou}, camada agora `{audio.CamadaDeTeste}`");

			audio.Musica(Trilha.Combate(), AudioDirector.Camada.Combate);
			Conferir(audio.CamadaDeTeste == AudioDirector.Camada.Transformacao
				  && audio.FaixaDeTeste == temaDaCena,
					 $"...e o combate nao a derruba de volta enquanto ela toca (`{audio.CamadaDeTeste}`)");
			Fechar(porCima);

			// --- 3. E NAO ALCANCA OUTRO PLANETA: O PACOTE NEM SAI DA ZONA -------------
			// ============================ A METADE QUE MORA NO SERVIDOR ============================
			// Do lado de la nao acontece nada -- e "nada" e o que uma bancada de cliente ve o tempo todo, o
			// que torna esta a metade impossivel de medir daqui. Entao a pergunta e feita ao servidor, que no
			// modo `--host` esta no mesmo processo, e ele responde com os DESTINOS anotados dentro do proprio
			// `foreach` do `AnunciarForma`. Ver `GameServer.MedirAlcanceDoAnuncio`.
			//
			// SAO QUATRO PERGUNTAS E NENHUMA E DECORATIVA: sem "o anuncio saiu", uma lista vazia satisfaria o
			// "nao contem o estranho"; sem "o vizinho recebeu", um servidor que nao anunciasse pra ninguem
			// passaria; sem "eu recebi", o corte poderia ter comido a propria tela; e sem "o estranho NAO
			// recebeu" nao ha regra nenhuma sendo medida.
			//
			// COMO REPROVA SE A REGRA SUMIR: troque o `ZoneList(pl.Zone.Hash)` do `AnunciarForma` por
			// `_players.Values` (a simplificacao plausivel: o jogo continuaria funcionando pra quem esta
			// perto) e a quarta linha cai sozinha.
			// ==================================================================================
			int eu = GameClient.Instance?.LocalId ?? 0;
			if (Jandirus.Server.GameServer.Instance?.MedirAlcanceDoAnuncio(eu) is { } alcance)
			{
				string lista = string.Join(", ", alcance.Destinos);
				Conferir(alcance.Destinos.Length > 0,
						 $"o anuncio de forma SAIU ({alcance.Destinos.Length} destinatarios: {lista})");
				Conferir(alcance.Destinos.Contains(alcance.Vizinho),
						 $"e chega em quem esta no MESMO planeta (o vizinho {alcance.Vizinho})");
				Conferir(alcance.Destinos.Contains(eu),
						 $"e na minha propria tela, que esta na mesma zona ({eu})");
				Conferir(!alcance.Destinos.Contains(alcance.Estranho),
						 $"e NAO chega em quem esta em outro planeta ({alcance.Estranho}) "
					   + "-- e por isso que la nao ha musica nem tremor");
			}
			else
			{
				Conferir(false, $"o servidor respondeu o alcance do anuncio (id {eu})");
			}

			// --- 4. E O SEGUNDO CINTO: PACOTE SEM CORPO NAO VIRA CENA ----------------
			// ============================ POR QUE DOIS CINTOS PRA A MESMA REGRA ============================
			// O corte da zona nao cobre o ESPACO: la a zona e UMA so pro universo inteiro
			// (`Espaco.NomeDoEspaco`) e quem recorta e a chunk do snapshot. Quem esta a setores de distancia
			// esta na mesma ZONA, recebe o `S2C.Forma` -- e nao tem o corpo desenhado. O `AoMudarForma`
			// desiste no `Corpo(id) == null`, e e esse `return` que impede a musica de tocar pra um planeta
			// inteiro de gente que nem enxerga quem virou.
			//
			// MEDIDO PELO METODO QUE O PACOTE CHAMA, e nao remontando as duas linhas dele: um id que nao tem
			// corpo e exatamente o que chega do outro lado do universo.
			//
			// COMO REPROVA SE A REGRA SUMIR: mova o `Transformacao.Rodar` do `AoMudarForma` pra ANTES do
			// `if (corpo == null) return;` (usando o `_atores` como alvo, que compila) e as duas caem.
			// ========================================================================================
			audio.Silenciar();
			AudioDirector.Camada antesDoFantasma = audio.CamadaDeTeste;
			int cenasAntes = corpo.GetParent().GetChildren().OfType<Transformacao>().Count();
			mundo.AoMudarForma(eu + 80_001,
							   Jandirus.Core.Forms.Catalogo.Def(Base)!.IdRede,
							   Jandirus.Core.Forms.Catalogo.Def("ssj1")!.IdRede,
							   Jandirus.Core.Forms.DegrauDeCena.Estreia);
			int cenasDepois = corpo.GetParent().GetChildren().OfType<Transformacao>().Count();
			Conferir(cenasDepois == cenasAntes,
					 $"um anuncio de alguem que nao tem corpo aqui NAO faz nascer cena "
				   + $"({cenasDepois - cenasAntes} nasceu)");
			Conferir(audio.CamadaDeTeste == antesDoFantasma,
					 $"...e nao poe musica nenhuma no ar (camada `{audio.CamadaDeTeste}`)");

			// --- 5. FORA DE UM PLANETA, DE LONGE NAO TREME NADA ----------------------
			// ============================ O DEGRAU QUE FALTAVA, E COMO ELE E MEDIDO ============================
			// Este era o buraco declarado do bloco E2. A zona da tela e escrita pra a do ESPACO -- que e um
			// estado legitimo do jogo, o de quem decolou -- e o que roda depois e o `Transformacao._Ready`
			// inteiro, sem desvio: ele pergunta `Espaco.EhPlaneta(GameClient.Instance.Zone)` como sempre.
			//
			// O CONTRA-TESTE E OBRIGATORIO AQUI, e mais do que de costume: uma zona forjada e um jeito facil
			// de zerar TUDO por acidente (um corpo que nao entra na arvore, uma cena que nao nasce), e "deu
			// zero" seria lido como aprovacao. Se o tremor de PERTO continua inteiro com a mesma zona
			// forjada, o zero de longe e a regra e nao um efeito colateral do teste.
			//
			// COMO REPROVA SE A REGRA SUMIR: troque o `: 0f` do fim do `PesoDoTremor` por
			// `: Cinematicas.PesoDoTremorDeLonge` (o "afeta todos" levado longe demais) e a primeira cai.
			// ==========================================================================================
			if (GameClient.Instance is { } cli)
			{
				Jandirus.Core.World.ZoneKey daTerra = cli.ZonaDeTeste;
				try
				{
					cli.ZonaDeTeste = Jandirus.Core.World.Espaco.Zona(cli.SeedDoUniverso);
					Conferir(!Jandirus.Core.World.Espaco.EhPlaneta(cli.ZonaDeTeste),
							 $"a zona do ESPACO nao e planeta (`{cli.ZonaDeTeste.Name}`)");

					float noEspacoLonge = PesoAte(raioCheio * 8f);
					float noEspacoPerto = PesoAte(raioCheio * 0.5f);
					Conferir(Mathf.IsZeroApprox(noEspacoLonge),
							 $"fora de um planeta, quem esta longe NAO sente nada ({noEspacoLonge:0.##}) "
						   + "-- \"quem esta no espaco ou noutro planeta nao sente\"");
					Conferir(Mathf.IsEqualApprox(noEspacoPerto, 1f),
							 $"...mas quem esta na mesma chunk continua sentindo inteiro ({noEspacoPerto:0.##}) "
						   + "-- o contra-teste: o zero acima e a regra, e nao a bancada quebrada");
				}
				finally { cli.ZonaDeTeste = daTerra; }

				// E A VOLTA, que e o que impede este bloco de envenenar os proximos: no planeta, longe volta
				// a ser eco. Sem ela, um `finally` que nao restaurasse deixaria as checagens seguintes
				// medindo um jogador que o cliente acha que esta no espaco.
				Conferir(Mathf.IsEqualApprox(PesoAte(raioCheio * 8f), ecoEsperado),
						 $"e de volta no planeta o eco volta ({PesoAte(raioCheio * 8f):0.##})");
			}

			// --- 6. O RELATO DO DONO: A MUSICA DE MENU VAZANDO PRA DENTRO DA LUTA -----
			// ============================ O CAMINHO EXATO DA QUEIXA, REFEITO AQUI ============================
			// *"quando uma musica de TRANSFORMACAO ou COMBATE acaba, comeca a tocar umas MUSICAS DO MENU do
			// jogo em LOOP"*. O caminho tinha QUATRO passos e nenhum deles era absurdo sozinho: brigar (poe
			// `Combate` no ar), apertar ESC (o `Menu` perde do combate e fica GUARDADO), fechar o ESC (o
			// `PararCamada(Menu)` desistia porque o menu nao era a camada que tocava) e a tag de combate cair
			// -- e a maquina "voltava" pro tema de menu esquecido, em laco, no meio do jogo.
			//
			// MEDIDO SO COM API PUBLICA, nos mesmos quatro passos que o dono deu. As duas ultimas linhas sao
			// as que respondem a queixa: a de menu nao pode estar no ar, e o SILENCIO tem que ser o estado
			// final -- "a musica simplesmente E PARADA".
			//
			// COMO REPROVA SE A REGRA SUMIR: devolva o `_faixaDeBaixo` (uma string so, sem dono) no lugar do
			// vetor de pedidos do `AudioDirector` e as duas ultimas caem juntas.
			// ============================================================================================
			audio.Silenciar();
			string temaDeMenu = Trilha.Menu();
			audio.Musica(Trilha.Combate(), AudioDirector.Camada.Combate);
			audio.Musica(temaDeMenu, AudioDirector.Camada.Menu);          // ESC no meio da briga
			Conferir(audio.CamadaDeTeste == AudioDirector.Camada.Combate,
					 $"com o ESC aberto no meio da briga quem toca continua sendo o COMBATE "
				   + $"(camada `{audio.CamadaDeTeste}`)");

			audio.PararCamada(AudioDirector.Camada.Menu);                 // fechou o ESC
			audio.PararCamada(AudioDirector.Camada.Combate);              // a tag de combate caiu
			Conferir(audio.FaixaDeTeste != temaDeMenu,
					 $"a tag de combate cai e a musica de MENU nao assume o jogo "
				   + $"(`{(audio.FaixaDeTeste.Length == 0 ? "silencio" : audio.FaixaDeTeste.GetFile())}`) "
				   + "-- \"comeca a tocar umas MUSICAS DO MENU do jogo em LOOP, oq n deveria acontecer\"");
			Conferir(audio.FaixaDeTeste.Length == 0,
					 $"...e o que fica e o SILENCIO, que e um estado e nao um buraco pra preencher "
				   + "-- \"a musica simplesmente E PARADA\"");

			audio.Silenciar();   // nao deixa a trilha da bancada tocando
		}

		mundo.TickDoTremorDeTeste(10.0);

		// ---------------------------------------------------------------------
		// F. A SOBRECARGA -- "quando o ki passa de 100% ele fica brilhando de outra cor"
		// ---------------------------------------------------------------------
		// A queixa do dono, e ela era na BASE. A `CargaVisual` tinha DUAS cores proprias -- um azul de
		// carga e um LARANJA FIXO de sobrecarga -- e mandava a escolhida pra `Aura`. Ou seja havia tres
		// respostas pra "de que cor e a chama deste corpo" (a da forma, a de carga e a de sobrecarga) e
		// vencia quem escrevesse por ultimo; como este node repinta TODO QUADRO enquanto ha chama,
		// vencia sempre ele -- inclusive por cima da cor de uma transformacao.
		//
		// A decisao foi TIRAR a cor daqui, e nao escolher um laranja melhor: quem passa dos 100% nao
		// troca de energia, so aperta mais dela no mesmo lugar. O que continua dizendo isso e a FORCA
		// (pulsa mais forte, e mais rapido) -- ver a ultima medida deste bloco, que existe pra impedir
		// que "arrumar a cor" acabe apagando a distincao inteira.
		//
		// E SOBRECARGA E OUTRO CANAL: `aura_ki`, nao `aura_carga`. Ela vale sem a tecla C
		// (`World.AplicarChamaDaCargaLocal` liga a chama com `_auraDaCarga || SobrecargaLocal`), entao o bloco roda
		// com o C SOLTO -- com os dois ligados nao daria pra saber qual dos canais acendeu o que.
		//
		// COMO REPROVA SE A REGRA SUMIR: devolva `Color cor = _excesso ? CorExcesso : CorCarga;` ao
		// `CargaVisual.Pintar`. Ha duas formas de reverter e cada uma tem dono aqui -- se a cor voltar
		// so pro desenho, cai a linha das "duas chamas"; se ela voltar tambem pro `ChamaDaCarga`
		// (o parametro que sumiu), cai a do laranja.
		var laranjaAntigo = new Color(1.0f, 0.84f, 0.42f);   // o `CorExcesso` deletado, escrito a mao
		Color? ChamaDaCarga() => carga.DesenhoDeTeste.CorNoMaterialDeTeste;

		mundo.AoCairEfeito("aura_ki", 1);
		Conferir(carga.DesenhoDeTeste.Visible,
				 "passar dos 100% SEM segurar C ja desenha a chama");
		// ============================ E NA BASE ELA NAO ACENDE LUZ NENHUMA ============================
		// A QUEIXA LITERAL do dono, escrita como medida: "ao passar de 100% do ki na base o node aura
		// liga a luz, sendo q na base n importa a % do ki". Esta linha ja cobrou o oposto
		// ("a sobrecarga acende a luz junto") -- era a regra antes de a guarda `_temForma` existir.
		//
		// E ELA E DIFERENTE DA IRMA DA SECAO D: la o canal e o `aura_carga` (a tecla), aqui e o
		// `aura_ki` (a % de Ki). Sao dois caminhos ate a mesma chama, e a guarda tem que valer nos dois
		// -- foi ligar regra num caminho so que criou este defeito na rodada passada.
		// ==========================================================================================
		Conferir(Mathf.IsZeroApprox(aura.EnergiaDeTeste),
				 $"e na BASE a sobrecarga NAO acende luz -- a % de Ki nao e assunto do node `Aura` "
			   + $"({aura.EnergiaDeTeste:0.###})");

		// ============================ E NEM O DESENHO DELE -- A FOTO INTEIRA ============================
		// A foto do dono nao mostra so um chao iluminado: mostra DUAS chamas empilhadas no mesmo corpo, e
		// a de cima e deste node. Cobrar so a luz deixaria metade da foto sem medida -- e e a metade que
		// aparece de dia, quando a `PointLight2D` ja e zero por conta da noite (ver a secao da noite no
		// `Aura.Aplicar`) e mesmo assim o dono veria os dois desenhos.
		//
		// E ela e o par da linha logo acima ("a chama da CARGA ja desenha"), lida junto: as duas juntas
		// dizem "na base ha UMA chama, e ela e da `CargaVisual`". Sozinha, esta aqui passaria com a carga
		// inteira quebrada; sozinha, a de cima passaria com as duas chamas de volta.
		//
		// COMO REPROVA SE A REGRA SUMIR: troque o `_desenho.Definir(_acesa && !_cargaAtiva, ...)` do
		// `Aura.Aplicar` por `_acesa || _cargaAtiva` e ela cai neste canal e no da secao D.
		// ==========================================================================================
		Conferir(!aura.DesenhoDeTeste.Visible,
				 "-- e nem o DESENHO: na base quem desenha a chama e a `CargaVisual`, e ela sozinha");

		Color naBase = ChamaDaCarga() ?? Colors.Black;
		Conferir(ChamaDaCarga() is { } cb && cb.IsEqualApprox(aura.CorPessoal),
				 $"na BASE ela sai na cor PESSOAL deste personagem ({naBase.ToHtml(false)}, "
			   + $"esperado {aura.CorPessoal.ToHtml(false)})");

		// ============================ E ESSA COR VEIO MESMO DO SERVIDOR ============================
		// A linha de cima compara a chama com o node, e as duas pontas seriam o FALLBACK num jogo em
		// que a cor sorteada nunca saisse do save -- o modo de falha inteiro da cor pessoal, e ele
		// ficaria verde. Esta aqui e a que fecha: em jogo o `AccountStore.ParaJogador` deriva a cor de
		// TODO personagem antes de o corpo existir, entao o `Aura.CorDoKiCru` aqui significa
		// "a ficha nao chegou" ou "o campo se perdeu no caminho".
		//
		// E OS TRES CANAIS TEM QUE ESTAR NA FAIXA DO SORTEIO (>= 200/255, ver `CorDeAura`): uma cor
		// pessoal fora dela nao veio do sorteio do jogo, veio de outro lugar.
		float piso = Jandirus.Core.Appearance.CorDeAura.PicoDaFolha / 255f - 1f / 255f;
		Conferir(!aura.CorPessoal.IsEqualApprox(Aura.CorDoKiCru)
			  && aura.CorPessoal.R >= piso && aura.CorPessoal.G >= piso && aura.CorPessoal.B >= piso,
				 $"-- e ela e a SORTEADA que o servidor mandou (#{aura.CorPessoal.ToHtml(false)}), nao "
			   + $"o fallback #{Aura.CorDoKiCru.ToHtml(false)} de quem nao tem ficha");
		Conferir(ChamaDaCarga() is { } cl && !cl.IsEqualApprox(laranjaAntigo),
				 $"-- e nao no laranja fixo que a sobrecarga tinha ({laranjaAntigo.ToHtml(false)})");

		// ============================ E ELA E **A DELE**, PERGUNTADA AO SERVIDOR ============================
		// AS DUAS LINHAS ACIMA SAO UMA MOEDA EM METADE DAS RODADAS, e isso foi medido nesta passada, nao
		// suposto: o sorteio da aura devolve BRANCO PURO em ~49% dos casos (a saturacao do `min(255,
		// 200+rand)`), e o sujeito desta bancada nasce na hora -- ou seja, uma rodada em cada duas
		// compara branco com branco. Nesse caso um defeito que pintasse branco passaria verde, e branco
		// e exatamente o defeito historico deste port ("branco multiplicando a folha colorivel APAGA a
		// arte"). O `!= CorDoKiCru` e a faixa nao separam os dois.
		//
		// ENTAO A AFIRMACAO DEIXA DE SER SOBRE UMA COR E PASSA A SER SOBRE O CAMINHO: a cor que o
		// SERVIDOR guarda pra este corpo (`CorDeAuraDeTeste`, a ficha autoritativa, do lado de la do fio)
		// e a que chegou no node `Aura`, seja ela qual for. Coincidencia nao passa nisto.
		//
		// COMO REPROVA: qualquer coisa que perca a cor entre o `JoinAccepted` e o `VestirCorpoInteiro`
		// -- o `PutRgb`/`GetRgb` fora de ordem no pacote, o `_looks` sem a ficha, o node nao escrito --
		// derruba esta linha mesmo quando a cor sorteada calhou de ser a mesma dos dois lados.
		// ==============================================================================================
		int euNaRede = GameClient.Instance?.LocalId ?? 0;
		Jandirus.Core.Appearance.Rgb? noServidor =
			Jandirus.Server.GameServer.Instance?.CorDeAuraDeTeste(euNaRede);
		Conferir(noServidor is { } rs
			  && Mathf.Abs(aura.CorPessoal.R - rs.R / 255f) <= 1f / 255f
			  && Mathf.Abs(aura.CorPessoal.G - rs.G / 255f) <= 1f / 255f
			  && Mathf.Abs(aura.CorPessoal.B - rs.B / 255f) <= 1f / 255f,
				 $"-- e ela e A DESTE personagem: o servidor guarda {noServidor?.ToString() ?? "NADA"} e o "
			   + $"node carrega #{aura.CorPessoal.ToHtml(false)} (afirmacao sobre o CAMINHO, e nao sobre "
			   + "uma cor -- as duas linhas acima sao branco contra branco em ~49% das rodadas)");

		// (A medida "o DESENHO e a LUZ saem da mesma cor" morava aqui e MUDOU DE LUGAR, nao sumiu: na
		// base nao ha mais luz pra comparar. Ela vive agora no bloco verde la embaixo, onde ha forma --
		// e la ela cobra a mesma coisa, uma resposta so pra "de que cor e a chama deste corpo".)

		// O C E A SOBRECARGA SAO INDEPENDENTES. Sem esta, `cg.Definir(_auraDaCarga, ...)` -- o erro
		// obvio de quem mexer no `AplicarChamaDaCargaLocal` -- passaria despercebido: quem esta acima dos 100%
		// apagaria ao soltar a tecla e so voltaria a acender segurando C de novo.
		//
		// MEDE O DESENHO E NAO A LUZ, porque isto roda na BASE e na base a luz e sempre zero: cobrar
		// energia aqui mediria a guarda `_temForma` e nao a independencia dos dois canais.
		mundo.AoCairEfeito("aura_carga", 1);
		mundo.AoCairEfeito("aura_carga", 0);
		Conferir(carga.DesenhoDeTeste.Visible,
				 "soltar o C com o Ki AINDA alto nao apaga a chama");

		// ============================ TRANSFORMADO, A COR E DA FORMA ============================
		// O `primal_legendary2` e escolhido por ser VERDE (`4dff5a`): longe do ki cru e longe do
		// laranja, entao "saiu na cor da forma" nao pode passar por coincidencia com nenhum dos dois.
		// Um SSJ1 nao serviria -- a aura dele e `ffd24a`, vizinha do laranja deletado, e a checagem
		// ficaria verde com o defeito de volta.
		var verde = new Color("4dff5a");
		Mudar(Base, "primal_legendary2", Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		// UM QUADRO DA `CargaVisual`, porque e assim que o jogo repinta: `PrepararAuraDaForma` escreve
		// a cor no node `Aura` (e a luz troca na hora, pelo `Preparar`), mas o DESENHO da carga so a
		// rele no proximo `Pintar` -- que em jogo vem no quadro seguinte, deste `_Process`. Sem o tick
		// eu estaria medindo um instante que o jogador nunca ve.
		carga._Process(1.0 / 60.0);
		Conferir(ChamaDaCarga() is { } cf && cf.IsEqualApprox(verde),
				 $"transformado, a chama da sobrecarga sai na cor DA FORMA "
			   + $"({ChamaDaCarga()?.ToHtml(false) ?? "sem material"}, esperado {verde.ToHtml(false)})");
		Conferir(aura.CorDaLuzDeTeste.IsEqualApprox(verde),
				 $"e a luz dela junto ({aura.CorDaLuzDeTeste.ToHtml(false)})");

		// AS DUAS CHAMAS DO CORPO SAO DOIS `SpriteDeAura` DIFERENTES (o do node `Aura` e o da
		// `CargaVisual`), e a luz e de um so. Esta linha e a que cobra "uma resposta so": ela reprova
		// se alguem devolver uma cor propria a` carga sem devolve-la ao aviso, que e metade do defeito
		// -- chama laranja lancando luz azul no mesmo corpo. Ela media isto na BASE ate a base parar de
		// ter luz; aqui ha forma, e portanto ha as duas coisas pra comparar.
		Conferir(ChamaDaCarga() is { } cm && cm.IsEqualApprox(aura.CorDaLuzDeTeste),
				 $"e o DESENHO e a LUZ saem da mesma cor ({ChamaDaCarga()?.ToHtml(false) ?? "sem material"} "
			   + $"contra {aura.CorDaLuzDeTeste.ToHtml(false)})");

		// ============================ O QUE SOBROU PRA DIZER "PASSEI DOS 100%" ============================
		// Tirar a cor so esta certo porque a FORCA ficou. Sem esta medida, uma "simplificacao" que
		// igualasse os dois ramos do `Pintar` deixaria a base sobrecarregada identica a base
		// carregando -- a queixa teria sido resolvida apagando a informacao em vez da cor errada, e
		// nada nesta bancada notaria.
		//
		// OS DOIS NA MESMA FASE DA ONDA: a forca pulsa, e `Definir` so zera o `_fase` quando a chama
		// ACENDE. Apagar tudo antes de cada medida poe as duas no mesmo ponto -- senao eu estaria
		// comparando dois instantes quaisquer de duas senoides e o sinal do resultado seria sorte.
		//
		// E RODA AINDA TRANSFORMADO (o `primal_legendary2` continua vestido de proposito, e a volta pra
		// base ficou pra depois deste bloco): a medida e de ENERGIA DE LUZ, e na base a luz e zero dos
		// dois lados -- `0 > 0` reprovaria uma regra que esta certa.
		float Energia(bool carregando, bool sobrecarga)
		{
			mundo.AoCairEfeito("aura_carga", 0);
			mundo.AoCairEfeito("aura_ki", 0);
			mundo.AoCairEfeito("aura_ki", sobrecarga ? 1 : 0);
			mundo.AoCairEfeito("aura_carga", carregando ? 1 : 0);
			return aura.EnergiaDeTeste;
		}

		float soCarga = Energia(true, false);
		float soSobrecarga = Energia(false, true);
		Conferir(soSobrecarga > soCarga,
				 $"a sobrecarga acende MAIS que a carga comum ({soSobrecarga:0.###} contra "
			   + $"{soCarga:0.###}) -- a FORCA continua sendo da carga, so a COR e que saiu daqui");

		mundo.AoCairEfeito("aura_ki", 0);
		Conferir(Mathf.IsZeroApprox(aura.EnergiaDeTeste),
				 $"e o Ki voltando ao normal apaga a luz ({aura.EnergiaDeTeste:0.###})");
		Conferir(!carga.DesenhoDeTeste.Visible,
				 "-- com o desenho da chama sumindo junto, que e a outra metade do `ha chama, ha luz`");

		Mudar("primal_legendary2", Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);

		OFimDaCenaObedeceAoKi(mundo, meuId, vis, aura, carga, Cenas, Mudar, Base);

		// --- devolve o corpo como estava, pra o resto da bancada e pra a foto ---
		mundo.AoCairEfeito("aura_ki", 0);
		mundo.AoCairEfeito("aura_carga", 0);
		Conferir(Transformacao.PresosDeTeste == presosAntes,
				 $"a ida e volta nao deixou cena pendurada (donos {Transformacao.PresosDeTeste}, "
			   + $"base {presosAntes})");
	}

	// =====================================================================
	// 7-bis. G. O FIM DA CENA NAO DECIDE NADA -- QUEM DECIDE E O KI
	// =====================================================================
	/// <summary>
	/// A CENA ACABA E A AURA CONTINUA SENDO ASSUNTO DO KI.
	///
	/// ============================ POR QUE ISTO PRECISA DE UMA CENA DE VERDADE ============================
	/// A chama da cinematica e o TERCEIRO desenho da mesma arte (ver `Transformacao.ChamaDoDegrau`), e ela
	/// nao e do corpo: nasce e morre com o node da cena. Enquanto a cena roda, a tela tem aura por conta
	/// dela -- e por isso "tem aura no fim?" so responde alguma coisa depois que a cena SAIU. Medir isso
	/// sem rodar a cena inteira mediria justamente o instante em que os dois donos coexistem.
	///
	/// Depois que ela sai, quem responde e a regra normal e mais nada: a `CargaVisual` desenha se o
	/// servidor disser que ha carga ou sobrecarga (`World.AplicarChamaDaCargaLocal`), e a luz sai do node `Aura`
	/// se -- e so se -- houver FORMA vestida. Este bloco cobra as duas metades do mesmo enunciado, e as
	/// duas juntas: sozinha, a metade "apaga" passaria numa aura que nunca acende, e sozinha a metade
	/// "fica" passaria numa aura que nunca apaga (o defeito irmao, ja pago pelos raios da secao B).
	/// ==================================================================================================
	///
	/// ============================ A CENA E A ENCURTADA DO SSJ3, E A ESCOLHA E O TESTE ============================
	/// Nao e a mais curta do jogo nem a mais barata: e a UNICA que veste degraus (`Efeito.VesteDegrau`
	/// aparece num roteiro so). Isso importa porque a linha mais forte deste bloco e o CONTROLE -- com o Ki
	/// alto do comeco ao fim, a luz tem que ter APAGADO no meio, enquanto o corpo vestia a base. Sem esse
	/// mergulho, "no fim esta acesa" passaria com uma luz que nunca se apagou desde o pacote (e ela acende
	/// cedo mesmo: o `World.AoMudarForma` chama `PrepararAuraDaForma` ANTES de abrir a cena).
	///
	/// E o degrau `Curta` e o encurtamento de verdade do jogo (maestria abaixo de 50%, estreia ja vista):
	/// 140 s de SSJ3 viram 10 s (o teto da encurtada), com os mesmos beats. Rodar a cheia mediria a
	/// mesma regra por ~2600 quadros a mais.
	/// ==========================================================================================================
	/// </summary>
	private void OFimDaCenaObedeceAoKi(World mundo, int meuId, CharacterVisual vis, Aura aura,
									   CargaVisual carga, Func<List<Transformacao>> Cenas,
									   Action<string, string, Jandirus.Core.Forms.DegrauDeCena> Mudar,
									   string Base)
	{
		FormaDef doFim = Jandirus.Core.Forms.Catalogo.Def("ssj3")!;
		if (Jandirus.Core.Forms.Cinematicas.NoDegrau(doFim, Jandirus.Core.Forms.DegrauDeCena.Curta)
			is not { } curta)
		{ Conferir(false, "a encurtada do SSJ3 existe pra este bloco rodar"); return; }

		// A NOITE E CONDICAO DE MEDIDA E NAO ENFEITE: de dia a `PointLight2D` e ZERO de proposito (ver a
		// secao da noite no `Aura.Aplicar`), entao "no fim nao ha luz" passaria sozinho ao meio-dia. A
		// secao D ja poe a bancada no breu; repetir aqui e o que impede este bloco de depender da ordem.
		Iluminacao.EscuridaoDeTeste(1f);

		// O CORPO ENTRA NA BASE, e o penteado dele e o sinal de "estou vestindo a base" mais abaixo -- o
		// mesmo sinal que o `AEscadaDoSsj3AoVivo` usa, pelo mesmo motivo (perguntar ao tocador que degrau
		// ele ACHA que vestiu seria confirmar a intencao do codigo em vez do resultado dele).
		Mudar(doFim.Id, Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		string cabeloBase = vis.CabeloDeTeste;

		// O PASSO E MENOR QUE O MENOR VAO DA CENA ENCURTADA: os tres `VesteDegrau` do SSJ3 caem em
		// 0,8 / 5,8 / 10,0 s na cheia e o encurtamento os divide por ~11,7 -- o primeiro vao vira ~0,43 s.
		const double Passo = 0.02;

		// ============================ A JANELA DA BASE COMECA NO BEAT, E NAO EM ZERO ============================
		// O corpo ja entra na cena de penteado normal, entao "cabelo base" sozinho incluiria os quadros
		// ANTES do primeiro `VesteDegrau` -- e ali quem escreveu a aura nao foi a cena e sim o PACOTE: o
		// `World.AoMudarForma` chama `PrepararAuraDaForma(corpo, ssj3)` antes de abrir a cinematica, ou seja
		// ja ha forma guardada e a luz acende de verdade. Contar esses quadros seria cobrar da cena uma
		// escrita que nao e dela, e a medida reprovaria com o codigo certo.
		//
		// Do primeiro beat em diante o dono do estado E a cena (o `Vestir`), e e so isso que se mede.
		double primeiroDegrau = curta.Beats.First(b => b.Faz.HasFlag(Efeito.VesteDegrau)).Em;

		// RODA A CENA INTEIRA E DEVOLVE O QUE ACONTECEU NO MEIO. Ela NAO e liberada aqui de proposito:
		// uma cena que chega ao fim ja se liberou sozinha (`_fim = Sozinha` + `QueueFree`) e ja devolveu o
		// corpo no `SegundosPreso` -- forcar um `Free` por cima esconderia justamente a cena que NAO
		// terminou, que e o unico caso em que as medidas abaixo mentem.
		Transformacao? Rodar(out int quadrosComLuzNaBase, out int quadrosNaBase)
		{
			quadrosComLuzNaBase = 0; quadrosNaBase = 0;
			var antes = new HashSet<Transformacao>(Cenas());
			mundo.AoMudarForma(meuId, Jandirus.Core.Forms.Catalogo.Rede(Base), doFim.IdRede,
							   Jandirus.Core.Forms.DegrauDeCena.Curta);

			Transformacao? nova = null;
			foreach (Transformacao tr in Cenas()) if (!antes.Contains(tr)) nova = tr;
			if (nova == null) return null;

			nova.SetProcess(false);   // o relogio e nosso, como no resto desta bancada
			for (double t = 0; t < curta.Segundos + 1.0 && IsInstanceValid(nova)
							   && nova.FimDeTeste == Transformacao.FimDaCena.Rodando; t += Passo)
			{
				nova._Process(Passo);
				if (!IsInstanceValid(nova)) break;
				if (nova.TempoDeTeste < primeiroDegrau || vis.CabeloDeTeste != cabeloBase) continue;
				quadrosNaBase++;
				if (aura.EnergiaDeTeste > 0) quadrosComLuzNaBase++;
			}
			return nova;
		}

		// --- 1. KI ABAIXO DE 100%: a cena passa e nao deixa nada ---
		// COMO REPROVA SE A REGRA SUMIR: faca o `Assumir` (ou o `Soltar`) chamar `Aura.Acender` -- que e
		// literalmente a forma do defeito original, "a aura ja ta vindo ativada nas transformaçoes" -- e as
		// duas linhas de baixo caem juntas.
		mundo.AoCairEfeito("aura_carga", 0);
		mundo.AoCairEfeito("aura_ki", 0);
		Transformacao? fria = Rodar(out _, out _);
		Conferir(fria != null && fria.FimDeTeste == Transformacao.FimDaCena.Sozinha,
				 $"a cena encurtada do SSJ3 nasce e chega ao fim SOZINHA com o Ki normal "
			   + $"(fim: {fria?.FimDeTeste.ToString() ?? "nem nasceu"})");
		Conferir(!carga.DesenhoDeTeste.Visible,
				 "e com o Ki abaixo de 100% ela nao deixa chama nenhuma pra tras");
		Conferir(Mathf.IsZeroApprox(aura.EnergiaDeTeste),
				 $"-- nem luz, mesmo com a forma vestida no fim ({aura.EnergiaDeTeste:0.###})");

		Mudar(doFim.Id, Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);

		// --- 2. KI ACIMA DE 100%: a mesma cena, e a aura FICA ---
		// O `aura_ki` e ligado ANTES do pacote de forma e nunca desligado: o que se mede e uma cena
		// atravessada por um jogador que ja estava sobrecarregado, que e o caso real. Desligar e religar
		// depois mediria a troca de canal, nao o fim da cena.
		mundo.AoCairEfeito("aura_ki", 1);
		Transformacao? quente = Rodar(out int comLuzNaBase, out int naBase);
		Conferir(quente != null && quente.FimDeTeste == Transformacao.FimDaCena.Sozinha,
				 $"a MESMA cena chega ao fim sozinha com o Ki acima de 100% "
			   + $"(fim: {quente?.FimDeTeste.ToString() ?? "nem nasceu"})");
		Conferir(carga.DesenhoDeTeste.Visible,
				 "e a chama FICA -- passar dos 100% nao e assunto da cinematica, e ela nao apaga nada");
		Conferir(aura.EnergiaDeTeste > 0,
				 $"-- e agora ela ILUMINA, porque no fim ha FORMA vestida ({aura.EnergiaDeTeste:0.##})");

		// UM QUADRO DA `CargaVisual`, como no bloco F: a cor da chama e relida no `Pintar` seguinte, que em
		// jogo vem no quadro de depois. A COR e o que separa "sobrou aura" de "sobrou a aura DA FORMA" --
		// um `Assumir` que esquecesse de preparar o node deixaria a chama do ki cru acesa num SSJ3.
		carga._Process(1.0 / 60.0);
		var doSsj3 = new Color(doFim.Aura);
		Conferir(carga.DesenhoDeTeste.CorNoMaterialDeTeste is { } cq && cq.IsEqualApprox(doSsj3),
				 $"e a chama que ficou e a da FORMA que a cena assumiu "
			   + $"({carga.DesenhoDeTeste.CorNoMaterialDeTeste?.ToHtml(false) ?? "sem material"}, "
			   + $"esperado {doSsj3.ToHtml(false)})");

		// ============================ O CONTROLE: ELA APAGOU NO MEIO ============================
		// Sem esta linha, as tres de cima passariam com uma luz acesa desde o pacote e nunca mais tocada --
		// e a guarda `_temForma` desligada no caminho da CINEMATICA (o `Transformacao.Vestir`, que e um dos
		// dois lugares do jogo que sabem se ha forma) nao seria vista por ninguem. Foi ligar a regra num
		// caminho so que criou este defeito na rodada passada.
		//
		// A segunda metade -- houve quadros na base pra medir -- e o "zero que nao pode ser vazio": se a
		// cena parasse de vestir o degrau base, o corpo nunca passaria pelo penteado normal e a primeira
		// linha ficaria verde sem ter olhado nada.
		//
		// COMO REPROVA SE A REGRA SUMIR: troque o `!ehBase` do `aura.Preparar(...)` no
		// `Transformacao.Vestir` por `true` e ela cai com todos os quadros do degrau base acesos.
		Conferir(naBase > 0,
				 $"a cena PASSA pelo degrau base com o corpo em penteado normal ({naBase} quadro(s))");
		Conferir(comLuzNaBase == 0,
				 $"e enquanto ela vestia a base a luz esteve APAGADA, com o mesmo Ki alto "
			   + $"({comLuzNaBase} de {naBase} quadro(s) acesos)");

		Mudar(doFim.Id, Base, Jandirus.Core.Forms.DegrauDeCena.Nenhuma);
		mundo.AoCairEfeito("aura_ki", 0);
		mundo.TickDoTremorDeTeste(10.0);   // as duas cenas sacudiram a camera; nao deixa tremendo
	}

	// =====================================================================
	// 8. A PROPORCAO DE KI, NO METODO DO SERVIDOR
	// =====================================================================
	/// <summary>
	/// A REGRA DO DONO, LITERAL: *"se o ki do player no ssj1 for 200/200 ou seja 100% ao voltar pra
	/// base ele vai ficar com 100/100 pq ainda e 100%"*.
	///
	/// ============================ POR QUE ELA RODA `AplicarForma` DE VERDADE ============================
	/// A conta inteira nasce e morre dentro daquele metodo: ler a razao antes do `ssjBuff`, escrever o
	/// `trueKiMod`, chamar o `Statify` e so entao repor. Um teste que refizesse esses quatro passos
	/// provaria a conta do teste. E a ORDEM e metade da regra -- se a razao fosse lida DEPOIS do
	/// `Statify`, ela ja estaria medida contra o tanque novo e a reposicao seria um no-op silencioso,
	/// que e exatamente o defeito que o "sobe mantendo a razao" abaixo pega.
	///
	/// O lutador e NOVO e nao o do host: `AplicarForma` mexe em `Ki`, `MaxKi`, `ssjBuff` e `trueKiMod`
	/// da ficha, e usar o jogador vivo da bancada o deixaria em SSJ com o Ki mexido pro resto do
	/// `--diagforma`.
	/// ==============================================================================================
	/// </summary>
	private void AProporcaoDeKi()
	{
		// PELA INSTANCIA DO SERVIDOR, e nao pelo tipo: o `AplicarForma` deixou de ser `static` quando o
		// fator do cargo entrou nele (ver `GameServer.AplicarForma`), porque o trono mora no `_tronos`
		// da instancia. A bancada roda com `--host`, entao a instancia existe -- e se um dia nao
		// existir, isto reprova em vez de medir nada.
		if (Jandirus.Server.GameServer.Instance is not { } srv)
		{
			Conferir(false, "a bancada alcanca o servidor pra rodar o `AplicarForma` de verdade");
			return;
		}

		var pl = new Jandirus.Server.ServerPlayer
		{
			Race = "Saiyan",
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 3_000_000 },
		};
		pl.Ficha.Statify();

		void Ir(string id)
		{
			pl.Forma.Entrar(id);
			srv.AplicarForma(pl);
		}
		double Razao() => pl.Ficha.MaxKi > 0 ? pl.Ficha.Ki / pl.Ficha.MaxKi : double.NaN;

		string Base = Jandirus.Core.Forms.Catalogo.IdBase;
		Ir(Base);
		double baseMax = pl.Ficha.MaxKi;
		Conferir(baseMax > 0, $"o lutador de bancada tem tanque na base ({baseMax:0.0})");

		// ============================ A CENA QUE O DONO DESCREVEU, EM NUMERO ============================
		Ir(Base);
		pl.Ficha.Ki = pl.Ficha.MaxKi;                       // 100/100 na base
		Ir("ssj1");
		Conferir(Math.Abs(pl.Ficha.Ki - pl.Ficha.MaxKi) < 1e-6
			  && Math.Abs(pl.Ficha.MaxKi / baseMax - 2.0) < 0.01,
				 $"100/100 na base vira 200/200 em SSJ1 -- a barra nao despenca "
			   + $"({pl.Ficha.Ki:0.0}/{pl.Ficha.MaxKi:0.0})");
		Ir(Base);
		Conferir(Math.Abs(pl.Ficha.Ki - pl.Ficha.MaxKi) < 1e-6
			  && Math.Abs(pl.Ficha.MaxKi - baseMax) < 1e-6,
				 $"e 200/200 em SSJ1 volta pra 100/100 na base ({pl.Ficha.Ki:0.0}/{pl.Ficha.MaxKi:0.0})");

		// ============================ A VARREDURA: TETOS DIFERENTES x RAZOES DIFERENTES ============================
		// Os tetos vem do `Catalogo.TetoDeKi` e sao propositalmente desiguais: 2,0x no SSJ1, 3,5x no
		// SSJ3, 4,5x no SSJ4 e **1,0x** nas divinas. O Blue esta na lista justamente por ser o caso em
		// que o tanque NAO muda -- ele e o unico que distingue "a razao e reposta" de "o Ki nao foi
		// tocado", porque nele as duas coisas dao o mesmo numero em todas as outras formas.
		//
		// ============================ A RAZAO DA SOBRECARGA E DERIVADA, E ISSO NASCEU DE UM ERRO MEU ============================
		// A terceira razao existe pra matar o corte antigo: ele aparava o Ki em
		// `CargaDeKi.TetoDeCarga = MaxKi * max(powerupcap, 1)`, entao um Ki acima daquele teto nao
		// sobrevivia a descida. Eu tinha cravado **1,40** achando que bastava.
		//
		// NAO BASTAVA, e so o teste de mutacao mostrou: repus o corte de proposito no `AplicarForma` e
		// as doze checagens de 140% passaram VERDES. O motivo e que o `powerupcap` deste lutador (com
		// BP de bancada e o `Statify` cheio) e MAIOR que 1,40 -- o corte estava la, aplicado, e
		// simplesmente nao mordia naquele ponto da regua. Um numero cravado testava um teto que nao
		// era o teto.
		//
		// Entao a razao e lida DO PROPRIO TETO, no lutador que a bancada acabou de montar: sempre meio
		// ponto acima dele. Assim ela acompanha qualquer mudanca de `powerupcap` -- por skill, por
		// balanceamento ou por raca -- em vez de envelhecer calada como o 1,40 envelheceu antes mesmo
		// de nascer.
		// ==================================================================================================================
		double sobrecarga = Math.Max(1.40, Jandirus.Core.Combat.CargaDeKi.TetoDeCarga(pl.Ficha)
											/ Math.Max(pl.Ficha.MaxKi, 1) + 0.5);
		_passos.Add($"  --     razao de sobrecarga derivada do teto da carga: {sobrecarga * 100:0}% "
				  + $"(powerupcap {pl.Ficha.powerupcap:0.00})");

		foreach ((string id, double teto) in new[]
		{
			("ssj1", 2.0), ("ssj3", 3.5), ("ssj4", 4.5), ("blue", 1.0),
		})
		{
			Ir(Base);
			pl.Ficha.Ki = pl.Ficha.MaxKi;
			Ir(id);
			Conferir(Math.Abs(pl.Ficha.MaxKi / baseMax - teto) < 0.01,
					 $"`{id}`: o tanque muda de tamanho ({pl.Ficha.MaxKi / baseMax:0.00}x, esperado {teto:0.00}x)");

			foreach (double razao in new[] { 1.0, 0.30, sobrecarga })
			{
				Ir(Base);
				pl.Ficha.Ki = razao * pl.Ficha.MaxKi;
				double absNaBase = pl.Ficha.Ki;

				Ir(id);
				Conferir(Math.Abs(Razao() - razao) < 1e-9,
						 $"`{id}` a {razao * 100:0}%: SUBIR mantem a proporcao "
					   + $"({Razao() * 100:0.##}%, {pl.Ficha.Ki:0.0}/{pl.Ficha.MaxKi:0.0})");

				Ir(Base);
				Conferir(Math.Abs(Razao() - razao) < 1e-9
					  && Math.Abs(pl.Ficha.Ki - absNaBase) < 1e-6,
						 $"`{id}` a {razao * 100:0}%: DESCER devolve a proporcao E o numero "
					   + $"({Razao() * 100:0.##}%, {pl.Ficha.Ki:0.1} contra {absNaBase:0.1})");
			}
		}

		// ============================ O TIQUE NAO PODE DERIVAR ============================
		// `TickDaForma` chama `AplicarForma` TODO tique. Com o `MaxKi` inalterado a conta tem que
		// devolver exatamente o Ki que entrou -- se ela arredondasse, ou se lesse a razao no lugar
		// errado, o Ki escorreria alguns por cento por segundo e ninguem saberia de onde.
		Ir("ssj3");
		pl.Ficha.Ki = 0.42 * pl.Ficha.MaxKi;
		double antesDoTique = pl.Ficha.Ki;
		for (int i = 0; i < 60; i++) srv.AplicarForma(pl);
		Conferir(Math.Abs(pl.Ficha.Ki - antesDoTique) < 1e-6,
				 $"60 tiques na mesma forma nao movem o Ki ({pl.Ficha.Ki:0.000} contra {antesDoTique:0.000})");

		// ============================ O TANQUE ZERO E CASO REAL, MAS NAO PELO MOTIVO ESCRITO ============================
		// O comentario do `AplicarForma` afirmava que "um personagem que ainda nao passou por `Statify`
		// nenhum tem teto zero". ESTA BANCADA PROVOU QUE NAO: a primeira versao desta checagem exigia
		// `MaxKi <= 0` num lutador recem-construido e reprovou com 100,0 -- `Fighter.MaxKi` NASCE em
		// 100 (`Fighter.cs:38`). O comentario de la foi corrigido; o guarda continua certo.
		//
		// Certo porque o tanque zero e alcancavel por outro caminho, e por um que este projeto ja
		// pisou: o `MaxKi` e um PRODUTO (`Fighter.Statify.cs:118`) e basta um fator zerar. O `KiMod = 0`
		// esta documentado oito linhas abaixo daquela formula -- *"um KiMod zerado fazia log(8, 0)
		// derrubar o statify a cada tick"* --, ou seja ja aconteceu de verdade neste jogo.
		//
		// COMO REPROVA SE A REGRA SUMIR: tire a guarda `pl.Ficha.MaxKi > 0` e a divisao vira `0/0`.
		// Dali em diante `NaN > 0` e FALSO, entao a forma nunca mais cai por Ki zerado -- um Super
		// Saiyajin eterno, de graca, sem nenhum erro na tela. As duas linhas de baixo acusam: a
		// primeira o NaN, a segunda o Ki que sumiu.
		// ==========================================================================================================
		var cru = new Jandirus.Server.ServerPlayer
		{
			Race = "Saiyan",
			Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = 3_000_000 },
		};
		cru.Ficha.KiMod = 0;
		cru.Ficha.Statify();
		Conferir(cru.Ficha.MaxKi <= 0,
				 $"da pra chegar a tanque ZERO pela formula, com um fator zerado ({cru.Ficha.MaxKi:0.0})");
		cru.Ficha.Ki = 50;
		cru.Forma.Entrar("ssj1");
		srv.AplicarForma(cru);
		Conferir(!double.IsNaN(cru.Ficha.Ki) && !double.IsInfinity(cru.Ficha.Ki),
				 $"transformar sem tanque nao planta NaN no Ki ({cru.Ficha.Ki})");
		Conferir(Math.Abs(cru.Ficha.Ki - 50) < 1e-6,
				 $"e o Ki fica onde estava -- o sentinela -1 diz 'nao ha razao a repor', "
			   + $"nao inventa 0% nem 100% ({cru.Ficha.Ki:0.0})");
	}

	// =====================================================================
	// 9. O CORTE ANTIGO NAO EXISTE MAIS -- varredura no fonte
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE UMA VARREDURA DE TEXTO E NAO SO O COMPORTAMENTO ============================
	/// O comportamento acima ja pega o corte VOLTANDO: 140% deixaria de sobreviver a descida. Mas a
	/// regra do projeto e outra e mais forte -- *codigo substituido se DELETA* --, e um corte
	/// comentado, ou reescrito de um jeito que so morda em `powerupcap` alto, passaria no
	/// comportamento e continuaria sendo duas verdades sobre o mesmo numero.
	///
	/// E o precedente esta a tres blocos daqui: o `c.a = max(` do `Personagem.gdshader`, medido pelo
	/// ALFA e nao pela palavra "halo", porque e por ali que ele voltaria.
	///
	/// A TERCEIRA LINHA E O AVESSO E ela e a que este projeto mais esquece: deletar o consumidor pode
	/// deixar a funcao ORFA. `CargaDeKi.TetoDeCarga` continua sendo o teto da tecla C -- se ela ficar
	/// sem nenhum chamador, ela e que tem que ser deletada, e a bancada avisa antes de o campo morto
	/// virar patrimonio.
	/// ==========================================================================================================
	/// </summary>
	private void OCorteAntigoDoKi()
	{
		string alvo = ProjectSettings.GlobalizePath("res://Server/GameServer.Formas.cs");
		string fonte = System.IO.File.Exists(alvo) ? System.IO.File.ReadAllText(alvo) : "";
		Conferir(fonte.Contains("AplicarForma"),
				 $"a bancada leu o fonte do servidor ({alvo.GetFile()}, {fonte.Length} chars)");
		// ============================ E ELA OLHA CODIGO, E NAO PROSA ============================
		// Isto era `!fonte.Contains("TetoDeCarga")` sobre o arquivo INTEIRO -- comentario incluido --,
		// e reprovou no dia em que o conserto do Ki foi escrito: o bloco que explica por que encher
		// deixou de esvaziar (*"o teto absoluto continua de pe, porque nao e este: quem limita a
		// sobrecarga e o dono dela, o `CargaDeKi.TetoDeCarga`"*) CITA a constante pra dizer onde ela
		// mora agora. A bancada leu a citacao como se fosse a chamada.
		//
		// O modo de falha disso e o pior que uma bancada tem: ela ficou vermelha com o codigo certo, e
		// a saida obvia pra quem quisesse o verde de volta era APAGAR a explicacao -- ou seja, a
		// checagem pressionava contra o proprio comentario que documenta a regra que ela protege.
		//
		// Varrer linha a linha pulando `//` e `///` e a mesma disciplina das varreduras da bancada
		// `raiva` (ver `RaivaBench.OGanchoLigado`), e nao afrouxa nada: uma CHAMADA de verdade nunca
		// mora dentro de um comentario.
		// ==================================================================================
		var citacoes = new List<string>();
		string[] linhasDoFonte = fonte.Replace("\r\n", "\n").Split('\n');
		for (int i = 0; i < linhasDoFonte.Length; i++)
		{
			string l = linhasDoFonte[i].Trim();
			if (l.StartsWith("//")) continue;
			if (l.Contains("TetoDeCarga")) citacoes.Add($"linha {i + 1}");
		}
		Conferir(citacoes.Count == 0,
				 "o corte do Ki no teto da carga NAO existe mais em `GameServer.Formas.cs`"
			   + (citacoes.Count > 0 ? $" ({string.Join(", ", citacoes)})" : ""));

		string outro = ProjectSettings.GlobalizePath("res://Core/Combat/CargaDeKi.cs");
		string carga = System.IO.File.Exists(outro) ? System.IO.File.ReadAllText(outro) : "";
		Conferir(carga.Split("TetoDeCarga").Length - 1 >= 2,
				 "e `TetoDeCarga` continua tendo dono no `CargaDeKi` (deletar o corte nao a deixou orfa)");
	}

	// =====================================================================
	// 10. O KI PARADO NA CINEMATICA
	// =====================================================================
	/// <summary>
	/// ============================ O DEFEITO, E POR QUE ELE FICOU CARO AGORA ============================
	/// O dono: *"o ki continua caindo em cinematica, faça o ki ficar parado enquanto esta na
	/// cinematica"*. Enquanto as cenas estavam comprimidas isso era um arranhao de alguns segundos; com
	/// os prazos do DM de volta a cena do SSJ3 prende o corpo por 140 s, e o dreno tinha tempo de
	/// esvaziar o tanque e DERRUBAR o jogador da forma no meio da estreia dela -- sem nada que ele
	/// pudesse fazer, porque o corpo esta preso.
	///
	/// ============================ E POR QUE A MEDIDA TEM QUE VIR DO SERVIDOR ============================
	/// A cena e do cliente e o Ki e do servidor. Esta bancada nao pode olhar pra barra: o `expressedBP`
	/// e o Ki chegam censurados aqui (sigilo), e o portao que se quer provar mora em tres metodos
	/// privados. Entao quem mede e `GameServer.MedirCongelamentoNaCena`, que roda os TRES tiques de
	/// producao (forma, carga, voo) num corpo forjado -- e este bloco le os numeros.
	///
	/// OS TRES JUNTOS SAO A REGRA INTEIRA: congelar so o dreno da forma faria o Ki SUBIR durante a
	/// cena (a regeneracao continuaria correndo), que e mentira do mesmo tamanho, so que pro outro
	/// lado. Por isso a checagem e `Ki IGUAL`, e nao `Ki nao caiu`.
	///
	/// COMO REPROVA SE A REGRA SUMIR -- e isto foi RODADO, nao prometido: com o `if (EmCena(pl)) return;`
	/// do `TickDaForma` desligado, a bancada acusou `200,000 -> 187,147` em 8 s de cena (e a maestria
	/// subindo `0 -> 0,07438` junto). Sao 6,4% do tanque em oito segundos: na cena do SSJ3, que prende
	/// 140 s, e o tanque inteiro -- a queixa do dono em numero. Tirar o `if (!emCena)` do
	/// `TickDaCarga` acusa na mesma linha, pelo outro sinal (o Ki SOBE).
	/// ==========================================================================================
	/// </summary>
	private void OKiCongeladoNaCena()
	{
		if (Jandirus.Server.GameServer.Instance?.MedirCongelamentoNaCena() is not { } m)
		{
			Conferir(false, "a bancada conseguiu transformar um corpo forjado e medir a cena");
			return;
		}

		_passos.Add($"  --     `{m.Forma}` prende {m.PresoPorSegundos:0.#}s; medidos {m.SegundosMedidosDentro:0.#}s "
				  + $"dentro, restavam {m.RestanteNoMeio:0.#}s");

		Conferir(m.PresoPorSegundos > 0,
				 $"o SERVIDOR sabe da cena sem perguntar ao cliente: {m.PresoPorSegundos:0.#}s de corpo preso "
			   + "(derivados do mesmo `NoDegrau` que o cliente toca)");

		// O RESTANTE E MEDIDO PORQUE UM PRAZO QUE NAO ANDA CONGELA PRA SEMPRE -- e ele daria verde em
		// todas as outras linhas deste bloco, menos nesta e na do descongelamento.
		Conferir(Math.Abs(m.RestanteNoMeio - (m.PresoPorSegundos - m.SegundosMedidosDentro)) < 0.2,
				 $"e o prazo ANDA com o tique ({m.RestanteNoMeio:0.##}s restantes depois de "
			   + $"{m.SegundosMedidosDentro:0.#}s)");

		Conferir(Math.Abs(m.KiNoMeioDaCena - m.KiAoEntrar) < 1e-6,
				 $"o Ki nao se mexe DURANTE a cena -- nem pra baixo nem pra cima "
			   + $"({m.KiAoEntrar:0.000} -> {m.KiNoMeioDaCena:0.000})");

		Conferir(Math.Abs(m.MaestriaNoMeioDaCena - m.MaestriaAoEntrar) < 1e-12,
				 $"e a maestria tambem nao sobe: quem nao paga o dreno nao treina a forma "
			   + $"({m.MaestriaAoEntrar:0.#####} -> {m.MaestriaNoMeioDaCena:0.#####})");

		Conferir(m.KiDepoisDaCena < m.KiNoMeioDaCena - 1e-6,
				 $"passada a cena o dreno VOLTA a cobrar ({m.KiNoMeioDaCena:0.000} -> {m.KiDepoisDaCena:0.000}) "
			   + "-- congelamento que nao acaba e forma eterna");

		Conferir(m.MaestriaDepoisDaCena > m.MaestriaNoMeioDaCena,
				 $"e a maestria volta a subir junto ({m.MaestriaNoMeioDaCena:0.#####} -> {m.MaestriaDepoisDaCena:0.#####})");

		// O ITEM 4 DO PEDIDO: cair no meio desfaz a forma, e o congelamento cai junto. Sem a segunda
		// metade, um nocaute durante a cena deixaria o corpo em base com o Ki congelado ate o prazo
		// vencer -- energia parada num corpo que nao esta em cinematica nenhuma.
		Conferir(m.NocauteDerrubouAForma,
				 $"nocaute no meio da cena ainda desfaz a forma (a cena prendia {m.PresoAntesDoNocaute:0.#}s)");
		Conferir(m.CenaAposNocaute <= 0,
				 $"e o congelamento cai JUNTO com ela ({m.CenaAposNocaute:0.##}s de cena sobrando)");

		// ============================ E AGORA A CENA LONGA, INTEIRA ============================
		// As linhas de cima medem 8 s de uma cena de 25,0 -- prova que o portao EXISTE. O que o dono
		// reclamou e outra coisa: a estreia do SSJ3 prende 140 s, e era o tanque INTEIRO que sumia
		// nesse tempo. Entre "congela" e "congela por dois minutos" ha uma familia de defeitos que a
		// amostra curta nao alcanca -- um prazo que zere cedo, um contador que trave, ou a soma de
		// 3.501 subtracoes de 1/30 escorregando.
		//
		// A QUARTA LINHA E A MAIS DIFICIL DE PASSAR POR ACIDENTE: ela nao pergunta "o dreno voltou",
		// pergunta QUANDO. Um congelamento que acabasse cedo (aos 60 s) ou tarde (10 s depois) da o
		// mesmo verde nas outras tres e cai so nesta, com o numero.
		//
		// MEDIDO (contraprova rodada, com o `if (EmCena(pl)) return;` do `TickDaForma` desligado):
		// `o Ki nao se mexe em NENHUM dos 3562 tiques (350,000000 -> 0,000000)`. E o TANQUE INTEIRO,
		// e nao os 6,4% que a amostra de 8 s acusava -- a queixa do dono no tamanho real dela. A
		// maestria sobe `0 -> 0,385802` de graca junto, e o dreno "volta" aos 0,033 s.
		// =====================================================================================
		_passos.Add($"  --     cena LONGA: `{m.FormaLonga}` prende {m.PresoLongo:0.#}s, "
				  + $"{m.TiquesLongos} tiques de {m.PassoDoTique * 1000:0}ms");

		Conferir(m.PresoLongo >= 60,
				 $"a cena mais longa do jogo prende MESMO por minutos ({m.FormaLonga}, {m.PresoLongo:0.#}s) "
			   + "-- e ela e derivada de `Todas.MaxBy(SegundosPreso)`, nao escrita aqui");

		Conferir(Math.Abs(m.KiNoFimDaLonga - m.KiAoEntrarNaLonga) < 1e-9,
				 $"o Ki nao se mexe em NENHUM dos {m.TiquesLongos} tiques da cena inteira "
			   + $"({m.KiAoEntrarNaLonga:0.000000} -> {m.KiNoFimDaLonga:0.000000})");

		Conferir(Math.Abs(m.MaestriaNoFimDaLonga - m.MaestriaAoEntrarNaLonga) < 1e-12,
				 $"e a maestria atravessa os {m.PresoLongo:0.#}s parada "
			   + $"({m.MaestriaAoEntrarNaLonga:0.######} -> {m.MaestriaNoFimDaLonga:0.######})");

		// UM TIQUE DE FOLGA, e nao "logo depois": o prazo e abatido no topo do `TickDaForma`, entao o
		// tique que consome o resto dele JA e o primeiro tique livre. O Ki tem que se mexer nesse, e
		// nao no seguinte -- e nao pode ter se mexido em nenhum antes.
		Conferir(m.SegundosAteODrenoVoltar > 0
				 && Math.Abs(m.SegundosAteODrenoVoltar - m.PresoLongo) <= m.PassoDoTique * 1.5,
				 $"e o dreno volta no tique EXATO em que o prazo vence "
			   + $"(mexeu aos {m.SegundosAteODrenoVoltar:0.###}s, prazo {m.PresoLongo:0.###}s)");
	}

	// =====================================================================
	// 11. UMA DESCRICAO SO -- `Vestir` VESTE O CORPO INTEIRO
	// =====================================================================
	/// <summary>
	/// A FOTO DA APARENCIA DESTE CORPO, canal a canal. Ver <see cref="AAparenciaInteiraDoDegrau"/>.
	///
	/// ============================ POR QUE UMA STRING E NAO SETE CAMPOS ============================
	/// O que se compara aqui e "esta aparencia e a MESMA?", nunca "quanto ela mudou". Uma string junta
	/// diz isso numa comparacao so E imprime o motivo da reprova de graca -- com sete campos, quem
	/// lesse o log veria "as fotos diferem" e teria que abrir o codigo pra descobrir em qual.
	///
	/// TODO CANAL E LIDO DO NODE QUE DESENHA, nunca de um campo guardado: a faisca sai do uniform do
	/// shader, o contorno do material de cada camada, a tinta do rabo do uniform dele. Ler o que foi
	/// PEDIDO provaria que quem pede pede, que e o teste que sempre passa.
	/// </summary>
	private static string Aparencia(Node2D corpo, CharacterVisual vis)
	{
		var r = corpo.GetNodeOrNull<RaiosDaForma>("Raios");
		var a = corpo.GetNodeOrNull<Aura>("Aura");
		var c = corpo.GetNodeOrNull<CargaVisual>("Carga");
		(Color cor, float forca, int camadas) = vis.ContornoNoMaterialDeTeste();

		return string.Join(" | ",
			$"cabelo={vis.CabeloDeTeste.GetFile()}",
			$"corpo={(vis.CorpoDaFormaDeTeste ? vis.PoseDoCorpoDaFormaDeTeste : "-")}",
			$"rabo={(vis.TintaDoRaboDeTeste is { } t ? $"{t.X:0.###},{t.Y:0.###},{t.Z:0.###}" : "-")}",
			$"faisca={(r == null ? "?" : r.VivosDeTeste == 0 ? "off"
					   : $"{r.CorDeTeste.ToHtml(false)}x{r.IntensidadeDeTeste}")}",
			$"aura={(a == null ? "?" : $"{a.DesenhoDeTeste.FolhaDeTeste.GetFile()}/{a.CorDaChama.ToHtml(false)}")}",
			$"carga={(c == null ? "?" : c.DesenhoDeTeste.FolhaDeTeste.GetFile())}",
			$"contorno={cor.ToHtml(false)}x{forca:0.###}/{camadas}");
	}

	/// <summary>
	/// ============================ A REGRA, E POR QUE ELA VALE MAIS QUE O CONSERTO ============================
	/// O dono: *"se eu estiver no ssj2 e iniciar a cinematica do ssj3, ele faz tudo certinho voltando
	/// pra base etc, porem os efeitos dos raiozinhos continuam"*. A faisca foi so o canal que ele viu:
	/// o `Vestir` descrevia cabelo/corpo/tinta e o `Assumir` acendia faisca, aura e folha da carga por
	/// fora -- entao TODO degrau intermediario herdava calado o que estivesse ligado antes.
	///
	/// A regra que fecha a familia inteira e "**vestir uma forma descreve a aparencia inteira**", e a
	/// unica maneira honesta de a cobrar e esta: vestir CADA forma do catalogo, vestir a base em
	/// seguida, e exigir a MESMA foto que a base tirada limpa. Um canal que so se escreve e nunca se
	/// desfaz aparece como diferenca, tenha ele nome ou nao -- e vale pras 33 formas, inclusive as que
	/// forem escritas depois desta bancada.
	/// ====================================================================================================
	///
	/// ============================ PELO METODO DE PRODUCAO, E COM `souEu` FALSO ============================
	/// Quem veste e o `Transformacao.Vestir`, alcancado pelo `VestirDeTeste`. Refazer as chamadas aqui
	/// provaria a copia, que e o defeito que o `Vestir` existe pra impedir.
	///
	/// E A CENA NASCE COMO CORPO ALHEIO (`souEu: false`) DE PROPOSITO: no corpo do dono da tela o
	/// `Vestir` sai antes do contorno (la quem manda nele e o Ki), e o contorno e justamente um dos
	/// canais que podem vazar entre degraus. Como corpo remoto, os sete canais entram na foto.
	/// ==================================================================================================
	///
	/// ============================ COMO REPROVA SE A REGRA SUMIR -- e o que ela NAO ve ============================
	/// MEDIDO, contraprova rodada: com o `Vestir` deixando de mexer na faisca quando o degrau e a base
	/// (que e literalmente o defeito do dono), a terceira linha cai com *"5 de 36 vazaram -- depois de
	/// `ssj2`: ... faisca=8fe3ffx1 ... (base: ... faisca=off)"* -- a foto imprime o canal culpado.
	///
	/// E O BURACO, que a mesma bateria de contraprovas mostrou: apagar o canal do `Vestir` **por
	/// inteiro** (tirar as duas folhas em vez de so o ramo da base) NAO cai aqui. A conta explica: se
	/// nenhuma forma escreve a folha, ela e a mesma em todas as fotos, e "a base devolve a mesma foto"
	/// continua verdade. Um canal que ninguem escreve nao vaza -- ele so nao existe.
	///
	/// Quem pega esse e o <see cref="CadaCanalDaFormaTemUmDono"/>, pelo TEXTO: apagar a folha do
	/// `Vestir` deixa `Aura.Folha` escrito so pelo vestidor SEM cena, e a comparacao dos dois conjuntos
	/// cai com *"sem dono: Aura.Folha (so sem cena)"*. Os dois blocos existem porque falham por motivos
	/// opostos, e nenhum dos dois sozinho fecha a regra.
	/// ========================================================================================================
	/// </summary>
	private void AAparenciaInteiraDoDegrau(Node2D corpo, CharacterVisual vis)
	{
		FormaDef raiz = Jandirus.Core.Forms.Catalogo.Def(Jandirus.Core.Forms.Catalogo.IdBase)!;

		// A CENA E SO O VEICULO DO `Vestir`: ela nasce com `SetProcess(false)` e nunca toca um beat.
		// O roteiro escolhido e o ENCURTADO porque ele nasce sem musica (`Encurtar` limpa o campo) --
		// um roteiro cheio poria um tema tocando por causa de uma medicao.
		var t = Transformacao.Rodar(corpo.GetParent(), corpo, raiz,
									Jandirus.Core.Forms.Cinematicas.Encurtada(
										Jandirus.Core.Forms.Cinematicas.Ssj1), souEu: false);
		t.SetProcess(false);

		try
		{
			// DUAS VEZES ANTES DA PRIMEIRA FOTO. `PintarCabelo` guarda a tinta original do rabo na
			// PRIMEIRA chamada da vida do corpo; tirar a foto zero antes disso compararia um corpo
			// "ainda nao guardado" com corpos ja guardados, e a diferenca seria da bancada.
			t.VestirDeTeste(raiz);
			t.VestirDeTeste(raiz);
			string zero = Aparencia(corpo, vis);

			var comFaisca = Jandirus.Core.Forms.Catalogo.Todas.Where(d => d.Raios > 0).ToArray();
			Conferir(corpo.GetNodeOrNull<RaiosDaForma>("Raios") != null && comFaisca.Length > 0,
					 $"a bancada achou o node da faisca e as {comFaisca.Length} formas que a acendem");

			// ============================ SEM ISTO A LINHA DA COR E CEGA ============================
			// Quatro das cinco formas com faisca sao azuis e so o Limit Breaker e vermelho. Se um dia
			// as cinco caissem na mesma cor, "a cor acompanha o degrau" passaria verde com um `Vestir`
			// que pintasse tudo de azul fixo -- e ninguem saberia que a medida parou de medir.
			// ==================================================================================
			int tons = comFaisca.Select(d => Jandirus.Core.Forms.Catalogo.CorDosRaios(d)).Distinct().Count();
			Conferir(tons >= 2,
					 $"e a faisca do jogo tem mais de um tom ({tons}) -- senao a linha da cor nao mede nada");

			int corErrada = 0, forcaErrada = 0, faiscaSobrando = 0, mudaram = 0, vazaram = 0;
			string ondeCor = "", ondeSobrou = "", ondeVazou = "";

			// ============================ A CHAMA DA CENA E O TERCEIRO DESENHO, E ELE NAO CABE NA FOTO ============================
			// A `Aparencia` fotografa o CORPO (`corpo`, `vis`), e a chama da cinematica nao mora nele: ela
			// nasce e morre com a cena. Entao ela precisa da propria contagem, e ela roda AQUI porque este
			// laco ja veste as 33 formas pelo metodo de producao -- inclusive os tres degraus que a cena do
			// SSJ3 percorre, que sao o caso do pedido do dono.
			// ================================================================================================================
			int chamaErrada = 0;
			string ondeChama = "";

			foreach (FormaDef d in Jandirus.Core.Forms.Catalogo.Todas)
			{
				t.VestirDeTeste(d);
				string foto = Aparencia(corpo, vis);
				if (foto != zero) mudaram++;

				// ============================ A CENA VESTE A `Base` QUANDO A FORMA NAO TEM FOLHA ============================
				// E a regra que o `Transformacao.ChamaDoDegrau` escreve por extenso, repetida aqui de
				// proposito: a coluna de luz do beat `Efeito.AuraGrande` esta NARRADA na cinematica do
				// Ultra Instinto (*"uma coluna de luz azul-prateada engole tudo"*), e a nuvem so acende no
				// `Assumir`, 10 s depois daquele beat. Calar a chama da CENA junto com as outras duas
				// apagaria um beat escrito -- e o buraco so apareceria pra quem assistisse a estreia.
				// ======================================================================================================
				string folhaEsperada = SpriteDeAura.CaminhoDa(Jandirus.Core.Forms.Catalogo.Folha(d))
									?? SpriteDeAura.FolhaBase;
				if (t.ChamaDaCenaDeTeste.FolhaDeTeste != folhaEsperada)
				{
					chamaErrada++;
					if (ondeChama.Length == 0)
						ondeChama = $"`{d.Id}` desenhou `{t.ChamaDaCenaDeTeste.FolhaDeTeste.GetFile()}`, "
								  + $"esperado `{folhaEsperada.GetFile()}`";
				}

				if (corpo.GetNodeOrNull<RaiosDaForma>("Raios") is { } r)
				{
					if (d.Raios > 0)
					{
						var esperada = new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(d));
						if (r.CorDeTeste.ToHtml(false) != esperada.ToHtml(false))
						{
							corErrada++;
							if (ondeCor.Length == 0)
								ondeCor = $"`{d.Id}` saiu {r.CorDeTeste.ToHtml(false)}, esperado {esperada.ToHtml(false)}";
						}
						if (r.IntensidadeDeTeste != d.Raios) forcaErrada++;
					}
					// FORMA SEM FAISCA TEM QUE FICAR APAGADA -- e sao 28 das 33. Este e o lado da
					// regra que o dono viu quebrado, so que aqui ele e cobrado em toda forma e nao
					// so no degrau base da cena do SSJ3.
					else if (r.VivosDeTeste > 0)
					{
						faiscaSobrando++;
						if (ondeSobrou.Length == 0) ondeSobrou = d.Id;
					}
				}

				t.VestirDeTeste(raiz);
				string volta = Aparencia(corpo, vis);
				if (volta != zero)
				{
					vazaram++;
					if (ondeVazou.Length == 0) ondeVazou = $"depois de `{d.Id}`: {volta}  (base: {zero})";
				}
			}

			Conferir(corErrada == 0 && forcaErrada == 0,
					 $"vestir um degrau com faisca a liga NA COR e NA FORCA dele "
				   + $"({corErrada} cor(es) errada(s), {forcaErrada} forca(s) errada(s)"
				   + (ondeCor.Length > 0 ? $" -- {ondeCor}" : "") + ")");

			Conferir(faiscaSobrando == 0,
					 $"e vestir uma forma SEM faisca a deixa apagada ({faiscaSobrando} acesa(s)"
				   + (ondeSobrou.Length > 0 ? $", a primeira em `{ondeSobrou}`" : "") + ")");

			// O PEDIDO DO DONO, cobrado nas 33: *"eu vou usar a o proprio icone da carga dele na
			// cinematica"*. Vestir um degrau tem que trocar a folha da chama da CENA junto com a do corpo
			// -- era so o corpo que a recebia, e a cena desenhava a arte da forma alvo o tempo todo.
			Conferir(chamaErrada == 0,
					 $"e a chama da CENA veste a folha do mesmo degrau ({chamaErrada} errada(s)"
				   + (ondeChama.Length > 0 ? $" -- {ondeChama}" : "") + ")");

			Conferir(vazaram == 0,
					 $"vestir a base depois de QUALQUER forma devolve a aparencia base EXATA "
				   + $"({vazaram} de {Jandirus.Core.Forms.Catalogo.Todas.Length} vazaram"
				   + (ondeVazou.Length > 0 ? $" -- {ondeVazou}" : "") + ")");

			// O ZERO NAO PODE SER VAZIO: um `Vestir` que nao escrevesse NADA daria zero vazamentos
			// acima e passaria como se estivesse perfeito. Se as formas nao mudam a foto, nao ha o
			// que a base desfazer -- e a linha de cima deixou de medir.
			Conferir(mudaram >= 20,
					 $"e a foto MUDA de verdade ao vestir uma forma ({mudaram} das "
				   + $"{Jandirus.Core.Forms.Catalogo.Todas.Length} formas mudam algum canal)");

			_passos.Add($"  --     foto da base: {zero}");
		}
		finally
		{
			// `Free` E NAO `QueueFree`, pelo mesmo motivo do `AIdaEAVolta`: o `QueueFree` so mata o
			// node no fim do QUADRO, e a bancada inteira roda dentro de um quadro so -- a cena ficaria
			// segurando a pose do boneco durante os blocos seguintes, que medem exatamente isso.
			if (IsInstanceValid(t)) t.Free();
		}
	}

	// =====================================================================
	// 11-bis. CADA CANAL DA FORMA TEM UM DONO SO
	// =====================================================================
	/// <summary>
	/// ============================ O QUE ESTE BLOCO PEGA QUE A FOTO NAO PEGA ============================
	/// O bloco acima compara as FOTOS: ele acusa qualquer canal que a foto conheca. O que ele nao pode
	/// ver e um canal NOVO -- alguem acrescenta um efeito a um dos vestidores (um brilho, um decalque,
	/// uma segunda camada de aura), a foto nao o le, e o vazamento nasce invisivel exatamente como a
	/// faisca nasceu.
	///
	/// Entao aqui a pergunta e sobre o TEXTO, e hoje ela e uma so: **cada canal visual da forma tem um
	/// dono, e o dono e um so?**
	///
	/// ============================ POR QUE A LISTA A MAO MORREU ============================
	/// Este bloco cobrava, POR NOME, que o `Transformacao.Vestir` escrevesse seis canais -- e o sexto
	/// era o `AuraDaForma`, o contorno. Aquela chamada foi DELETADA de proposito, e a razao esta
	/// escrita no proprio `Transformacao.cs` (bloco "O CONTORNO NAO SAI DAQUI, E NEM DO CORPO ALHEIO"):
	/// era a TERCEIRA escrita do mesmo pixel, e a justificativa velha -- *"o cliente nao sabe o Ki
	/// alheio"* -- caducou quando o bit `EntityState.Sobrecarregado` passou a viajar no snapshot.
	///
	/// Ou seja: a bancada reprovava codigo CERTO. E isso e pior do que nao ter checagem nenhuma --
	/// uma linha vermelha apontando pra um refactor terminado ensina a proxima pessoa a "consertar"
	/// desfazendo o refactor, que e devolver o defeito que o dono viu ("o remoto fica sempre com a
	/// outline").
	///
	/// O defeito nao era o nome errado na lista. Era a FORMA da pergunta: uma lista de nomes escrita a
	/// mao envelhece a cada refactor, e ela nao sabe dizer de QUEM um canal e -- so que ele existe.
	/// ==========================================================================================
	///
	/// ============================ A AFIRMACAO NOVA, E ELA E POR CONJUNTO ============================
	/// O jogo veste uma forma por DOIS caminhos, de proposito:
	///
	///   com cena -> `Transformacao.Vestir` (a cinematica, degrau a degrau)
	///   sem cena -> `World.VestirAFormaSemCena` + `PrepararAuraDaForma` (+ os dois que eles chamam)
	///
	/// Nenhum dos dois traz uma lista: os canais sao LIDOS dos corpos dos dois lados e qualificados
	/// pelo TIPO do node que os recebe (ver <see cref="CanaisDe"/>) -- e por isso `Aura.Folha` e
	/// `Catalogo.Folha` deixam de ser a mesma palavra, que era o que tornava a lista velha ambigua.
	/// Dai saem cinco afirmacoes, e nenhuma delas cita um canal pelo nome:
	///
	///   1. os dois vestidores escrevem o MESMO conjunto de canais;
	///   2. toda diferenca entre eles esta na tabela `donoProprio`, que diz de quem o canal e;
	///   3. o dono declarado ESCREVE mesmo o canal dele;
	///   4. e o vestidor COM cena nao escreve nenhum canal de dono declarado;
	///   5. quem a tabela marca como exclusivo tem UM escritor no jogo inteiro.
	///
	/// Refactor que troque o dono de um canal move UMA linha da tabela. Refactor que RENOMEIE um canal
	/// nao mexe em nada: o nome muda dos dois lados, e os dois lados sao lidos do fonte.
	/// ==========================================================================================
	///
	/// ============================ "UM DONO SO" NAO VALE PRA TODO CANAL, E ESTE E O MOTIVO ============================
	/// Contar escritores e exigir 1 em todos reprovaria o jogo CERTO: `CorpoDaForma`,
	/// `VestirCabeloDaForma` e `ColadasDaForma` tem dois escritores por DESENHO (os dois vestidores), e
	/// o cabelo tem um terceiro -- a PISCADA da cinematica (`Transformacao:887`), que reescreve so ele.
	/// Dois donos dizendo a MESMA coisa nao sao o defeito; o defeito e dois donos dizendo coisas
	/// DIFERENTES, e e exatamente isso que a afirmacao 1 mede.
	///
	/// Entao a unicidade e cobrada onde ela e verdade: nos canais que a tabela marca `SoDele`,
	/// contados no jogo inteiro. O `Aura.Apagar` esta na tabela SEM a marca de proposito -- apagar a
	/// aura e o interruptor dela, chamado de varios lugares (`Transformacao:999` e `:1322`), e nao um
	/// canal que descreve a forma.
	/// ============================================================================================
	///
	/// ============================ O CONTORNO, E ELE REPROVA NOS DOIS SENTIDOS ============================
	/// Isto nao existia: nada no jogo cobrava que o contorno tivesse um dono so. Hoje o `AuraDaForma`
	/// esta na tabela com dono `World.AplicarContorno` e a marca `SoDele`, e o comentario de
	/// `Transformacao.cs` deixou de ser recado e virou teste:
	///
	///   repor o `vis.AuraDaForma(...)` no `Vestir`   -> cai a 4 (canal de dono declarado escrito pelo
	///                                                   vestidor com cena) E cai a 5 (2 escritores);
	///   tirar o `vis.AuraDaForma(...)` do `World`    -> cai a 3 (o dono declarado nao escreve o canal)
	///                                                   E cai a 5 (0 escritores).
	///
	/// As duas pontas tem que doer, porque as duas ja doeram: a primeira e o defeito de origem (tres
	/// escritas do mesmo pixel), a segunda deixaria o contorno sem ninguem -- e forca 0 nao desenha,
	/// entao ele sumiria calado.
	/// ==================================================================================================
	///
	/// ============================ E O `Assumir` CONTINUA SEM APARENCIA PROPRIA ============================
	/// Isto e o que a checagem velha protegia de verdade, e nao se perde aqui: **o `Assumir` chama
	/// algum escritor que o `Vestir` nao chama?** Se chamar, ha de novo duas descricoes do mesmo
	/// personagem, e a segunda vai envelhecer contra a primeira -- foi assim, literalmente, que este
	/// defeito existiu.
	///
	/// AS DUAS EXCECOES, e por que sao so duas:
	///   * `Vestir` -- o `Assumir` E o degrau final da escada, e chamar o vestidor e o trabalho dele;
	///   * `Apagar` -- a devolucao da aura BASE emprestada (`Efeito.AuraBase`, um roteiro so). E o
	///     avesso de acender: quem empresta devolve, e devolver nao deixa nada ligado pra tras.
	///
	/// Qualquer terceiro nome que apareca la e um efeito que o `Vestir` nao sabe desfazer. A lista e
	/// curta de proposito: uma excecao a mais devolve o problema inteiro.
	///
	/// MEDIDO, contraprova rodada na versao anterior deste bloco: devolvendo ao `Assumir` uma linha
	/// `Aura.Acender(...)` -- que e a forma exata do defeito original -- esta comparacao cai com
	/// *"sobrou: Acender"*.
	/// ========================================================================================
	///
	/// ============================ AS SONDAS, E POR QUE ELAS SAO OBRIGATORIAS ============================
	/// Toda linha que compara CONJUNTOS passa de graca com os conjuntos vazios -- um extrator que
	/// deixasse de casar (metodo renomeado, assinatura mudada, regex quebrada) daria verde eterno em
	/// todas elas ao mesmo tempo. Por isso, em toda rodada:
	///   * a primeira linha exige que os cinco corpos tenham sido lidos, e imprime o tamanho de cada;
	///   * a segunda exige um PISO de canais vistos -- um piso, e nao a lista: nome nenhum aqui;
	///   * a sonda da 4 cola no `Vestir` uma escrita do proprio canal da tabela e exige que a 4 caia;
	///   * a sonda do `Assumir` cola nele uma escrita fantasma e exige que a comparacao a acuse.
	///
	/// O QUE ELE NAO PEGA, dito: escrita que nao passe por um node pego com `GetNodeOrNull<T>` (um
	/// campo `_visual` guardado na classe, por exemplo) -- ai o dono do canal fica invisivel pra o
	/// extrator; e a contagem da 5, que e por NOME curto e por isso so vale nos canais marcados. As
	/// bancadas (`RoboDe*.cs`) ficam de fora da varredura da 5 de proposito: elas dirigem o visual na
	/// mao, que e o trabalho delas -- e isso inclui ESTE arquivo, entao a tabela aqui nao se acusa.
	/// ============================================================================================
	/// </summary>
	private void CadaCanalDaFormaTemUmDono()
	{
		string arqCena = ProjectSettings.GlobalizePath("res://Client/Transformacao.cs");
		string arqWorld = ProjectSettings.GlobalizePath("res://Client/World.cs");

		string fonteCena = SemComentario(LerFonte(arqCena));
		string fonteWorld = SemComentario(LerFonte(arqWorld));

		string doVestir = CorpoDoMetodo(fonteCena, "private void Vestir(FormaDef d)");
		string doAssumir = CorpoDoMetodo(fonteCena, "private void Assumir()");

		// ============================ O VESTIDOR SEM CENA SAO QUATRO METODOS, E ISSO E DESENHO ============================
		// O `World` os extraiu um a um, e cada extracao tem a razao escrita la: o `PrepararAuraDaForma`
		// saiu porque o Oozaru precisa dele e de mais nada; o `AcenderFormaNoCorpo` porque raio e
		// nebulosa sao da FORMA e valem nos dois corpos; o `AplicarContorno` porque o contorno e do KI.
		//
		// SAO ASSINATURAS E NAO NOMES DE CANAL, e a diferenca e o tempo de vida: canal muda de nome a
		// cada refactor de visual, ponto de entrada quase nunca. E quando um deles mudar, a PRIMEIRA
		// linha cai dizendo qual corpo veio vazio -- que e o aviso certo, no lugar certo.
		// ============================================================================================
		(string Nome, string Assinatura)[] semCena =
		[
			("World.VestirAFormaSemCena",
			 "private void VestirAFormaSemCena(int id, Node2D corpo, Jandirus.Core.Forms.FormaDef? def)"),
			("World.PrepararAuraDaForma",
			 "private static void PrepararAuraDaForma(Node2D corpo, Jandirus.Core.Forms.FormaDef? def)"),
			("World.AcenderFormaNoCorpo",
			 "private void AcenderFormaNoCorpo(Node2D corpo, Jandirus.Core.Forms.FormaDef? def)"),
			("World.AplicarContorno", "private void AplicarContorno(int id)"),
		];
		var corpoDoDono = semCena.ToDictionary(m => m.Nome, m => CorpoDoMetodo(fonteWorld, m.Assinatura));

		int menorSemCena = corpoDoDono.Values.Min(c => c.Length);
		Conferir(doVestir.Length > 200 && doAssumir.Length > 20 && menorSemCena > 40,
				 $"a bancada leu os cinco corpos no fonte (`Vestir` {doVestir.Length} chars, `Assumir` "
			   + $"{doAssumir.Length}, sem cena: "
			   + string.Join(", ", semCena.Select(m => $"{m.Nome} {corpoDoDono[m.Nome].Length}"))
			   + ")");
		if (doVestir.Length == 0 || doAssumir.Length == 0 || menorSemCena == 0) return;

		// ============================ O `Assumir` NAO TEM APARENCIA PROPRIA -- ver o cabecalho ============================
		// Mantida inteira: e o que a checagem velha protegia de verdade, e a unica coisa dela que
		// continuava certa. As excecoes sao duas, e o `GetNodeOrNull` entra por ser a PORTA e nao um
		// efeito -- quem escreve e o que vem depois do ponto.
		string[] permitidos = ["Vestir", "Apagar", "GetNodeOrNull"];
		string[] sobrando = EscritasDe(doAssumir)
			.Except(EscritasDe(doVestir)).Except(permitidos).ToArray();

		Conferir(sobrando.Length == 0,
				 "o `Assumir` nao escreve NADA que o `Vestir` nao escreva -- uma descricao so"
			   + (sobrando.Length > 0 ? $" (sobrou: {string.Join(", ", sobrando)})" : ""));

		Conferir(EscritasDe(doAssumir + " _no.LigarUmEfeitoNovo();")
					.Except(EscritasDe(doVestir)).Except(permitidos).Any(),
				 "e a sonda ENXERGA: uma escrita fantasma colada no `Assumir` faz a comparacao cair");

		// ============================ OS CANAIS, LIDOS DOS DOIS LADOS ============================
		string[] canaisDaCena = CanaisDe(doVestir);
		string[] canaisSemCena = [.. corpoDoDono.Values.SelectMany(CanaisDe).Distinct()];

		// O PISO, E ELE E UM PISO E NAO UMA LISTA: um extrator que parasse de casar devolveria conjunto
		// vazio, e conjunto vazio esta contido em qualquer coisa -- as quatro linhas abaixo ficariam
		// verdes com o `Vestir` sem escrever nada.
		//
		// SEIS, E NAO OS OITO DE HOJE, de proposito: um piso colado na contagem atual reprovaria o
		// jogo certo no dia em que dois canais virassem um -- que ja aconteceu aqui uma vez
		// (`CabeloDaForma` + `PintarCabelo` -> `VestirCabeloDaForma`). Ele nao existe pra contar canal,
		// existe pra acusar desabamento.
		Conferir(canaisDaCena.Length >= 6,
				 $"a bancada ENXERGA os canais dos dois vestidores ({canaisDaCena.Length} com cena, "
			   + $"{canaisSemCena.Length} sem cena)");
		_passos.Add($"  --     com cena: {string.Join(", ", canaisDaCena)}");
		_passos.Add($"  --     sem cena: {string.Join(", ", canaisSemCena)}");
		if (canaisDaCena.Length == 0) return;

		// ============================ A TABELA DE DONOS, E ELA E A UNICA COISA ESCRITA A MAO ============================
		// Cada linha diz um canal que **nao** e do vestidor com cena, quem e o dono dele, e se essa
		// posse e exclusiva no jogo inteiro. Trocar o dono de um canal e mover uma linha daqui.
		//
		// `SoDele` E POR NOME CURTO, e por isso ele nao vale em todo canal: a varredura da afirmacao 5
		// nao sabe tipos (ela le o repositorio linha a linha), entao um nome que outros nodes tambem
		// usem -- `Folha`, `Definir`, `Apagar` -- contaria escrita de gente que nao tem nada com forma.
		// ==========================================================================================
		(string Canal, string Dono, bool SoDele, string Porque)[] donoProprio =
		[
			// O CONTORNO E DO KI E NAO DA FORMA: a forma da a cor e a forca (`_contornoDaForma`), o Ki
			// decide se acende (`_sobrecarregados`). Ver `Transformacao.cs`, bloco "O CONTORNO NAO SAI
			// DAQUI" -- e e esta linha que impede que ele volte pra la.
			("CharacterVisual.AuraDaForma", "World.AplicarContorno", true,
			 "o contorno e do KI; a forma so guarda a cor"),

			// O BIT QUE DECIDE `SSj` OU `SSjFP`, marcado no funil dos tres caminhos que vestem sem cena
			// (ver `World.cs`, o comentario em cima da chamada). A cena NAO passa por ele -- fica
			// anotado aqui em vez de virar excecao calada, porque e uma pergunta em aberto e nao um
			// desenho: quem se transforma COM cinematica nao remarca o dominio.
			("CharacterVisual.MarcarFormaDominada", "World.VestirAFormaSemCena", true,
			 "o funil dos tres caminhos sem cena; a cena nao remarca (ver o cabecalho)"),

			// O BIT DE QUEM DIRIGE O CORPO, e ele e o GEMEO da linha de cima: mesmo funil, mesma
			// razao, escrito no mesmo gesto (`World.cs`, a chamada logo abaixo da outra -- *"E QUEM
			// DIRIGE O CORPO, NO MESMO GESTO E PELO MESMO MOTIVO"*). Ele decide a cor do olho pelo
			// `Catalogo.CorDoOlho(d, semRedeas)`, e por isso tem que valer ja no PRIMEIRO quadro de
			// quem entra numa zona onde alguem ja esta em furia lendaria.
			//
			// A CENA NAO O ESCREVE, e nao e esquecimento: quem se transforma com cinematica esta
			// dirigindo o proprio corpo por definicao. O canal nasceu depois desta tabela e ficou
			// sem linha -- e o defeito de sempre aqui, o gemeo declarado e o outro nao.
			//
			// SEM `SoDele`: alem do funil, a virada de posse (`World.AoMudarPosse`) tambem escreve,
			// que e o caminho de quem JA estava na tela quando a furia comeca.
			("CharacterVisual.MarcarSemRedeas", "World.VestirAFormaSemCena", false,
			 "o gemeo do `MarcarFormaDominada`; tambem escrito pela virada de posse"),

			// O INTERRUPTOR DA AURA. Na cena quem faz esse trabalho e a guarda da luz do `Preparar`
			// (`!ehBase`), e nao um `Apagar` -- por isso ele so aparece de um lado. Sem `SoDele`: e
			// chamado de varios lugares que nao tem nada com vestir (ver o cabecalho).
			("Aura.Apagar", "World.PrepararAuraDaForma", false,
			 "o interruptor da aura, chamado de fora do vestidor tambem"),
		];

		// --- 1 e 2: os dois vestidores descrevem a MESMA forma ---
		string[] soDeUmLado = [.. canaisDaCena.Except(canaisSemCena).Select(c => $"{c} (so com cena)")
									.Concat(canaisSemCena.Except(canaisDaCena).Select(c => $"{c} (so sem cena)"))];
		string[] semDono = [.. soDeUmLado
			.Where(c => !donoProprio.Any(t => c.StartsWith(t.Canal + " ", StringComparison.Ordinal)))];

		Conferir(semDono.Length == 0,
				 $"os dois vestidores escrevem os MESMOS canais, menos os {donoProprio.Length} de dono "
			   + $"declarado ({soDeUmLado.Length} diferenca(s))"
			   + (semDono.Length > 0 ? $" -- sem dono: {string.Join(", ", semDono)}" : ""));

		// --- 3: o dono declarado escreve mesmo o canal dele ---
		// ESTE E O SENTIDO "SUMIU": apagar o `vis.AuraDaForma(...)` do `World.AplicarContorno` deixa o
		// contorno sem ninguem, e forca 0 nao desenha -- ele sumiria calado da tela.
		string[] orfaos = [.. donoProprio
			.Where(t => !corpoDoDono.TryGetValue(t.Dono, out string? c) || !CanaisDe(c).Contains(t.Canal))
			.Select(t => $"{t.Canal} (dono: {t.Dono})")];

		Conferir(orfaos.Length == 0,
				 $"e o dono declarado de cada canal ESCREVE o canal dele ({donoProprio.Length} na tabela)"
			   + (orfaos.Length > 0 ? $" -- orfao(s): {string.Join(", ", orfaos)}" : ""));

		// --- 4: o vestidor com cena nao escreve canal de dono declarado ---
		// ESTE E O SENTIDO "VOLTOU": repor o `vis.AuraDaForma(...)` no `Transformacao.Vestir` e a
		// terceira escrita do mesmo pixel de novo, que e o defeito de origem.
		string[] intrusos = [.. donoProprio.Select(t => t.Canal).Intersect(canaisDaCena)];
		Conferir(intrusos.Length == 0,
				 "e o vestidor COM cena nao escreve canal de dono declarado"
			   + (intrusos.Length > 0 ? $" (escreveu: {string.Join(", ", intrusos)})" : ""));

		// A SONDA DA 4, montada com o canal da PROPRIA tabela: uma escrita fantasma do primeiro canal
		// declarado, colada no corpo do `Vestir`, tem que ser vista. Ela prova de uma vez que o
		// extrator casa E que ele qualifica pelo tipo certo -- o fantasma sai por `vis`, e so vira
		// `CharacterVisual.<canal>` se o `var vis = ...GetNodeOrNull<CharacterVisual>` foi lido.
		string curtoDoPrimeiro = donoProprio[0].Canal[(donoProprio[0].Canal.IndexOf('.') + 1)..];
		Conferir(CanaisDe(doVestir + $" vis.{curtoDoPrimeiro}(0);").Contains(donoProprio[0].Canal),
				 $"e a sonda ENXERGA: repor o `{donoProprio[0].Canal}` no `Vestir` faz a linha de cima cair");

		// --- 5: quem e exclusivo tem UM escritor no jogo inteiro ---
		// A varredura larga, irma do `AArteVelhaNaoTemDono`: as afirmacoes 3 e 4 olham so os dois
		// vestidores, e o defeito historico do contorno era justamente um TERCEIRO escritor morando em
		// outro arquivo ("o corpo alheio tinha regra propria em dois outros arquivos", `World.cs`).
		var producao = FontesDeProducao();
		Conferir(producao.Count > 50,
				 $"a varredura enxerga o codigo de producao ({producao.Count} arquivo(s) .cs fora das bancadas)");

		List<string> repetidos = [];
		foreach ((string canal, string dono, bool soDele, string _) in donoProprio)
		{
			if (!soDele) continue;
			string curto = canal[(canal.IndexOf('.') + 1)..];
			var rx = new System.Text.RegularExpressions.Regex(
				@"\??\.\s*" + System.Text.RegularExpressions.Regex.Escape(curto) + @"\s*\(");

			int quantos = 0;
			string onde = "";
			foreach ((string arquivo, string texto) in producao)
			{
				int q = rx.Matches(texto).Count;
				quantos += q;
				if (q > 0 && onde.Length == 0) onde = arquivo.GetFile();
			}
			if (quantos != 1)
				repetidos.Add($"{canal}: {quantos} escritor(es)"
							+ (onde.Length > 0 ? $", o primeiro em {onde}" : $" (dono seria {dono})"));
		}

		Conferir(repetidos.Count == 0,
				 $"e quem tem dono EXCLUSIVO tem um escritor so no jogo inteiro "
			   + $"({donoProprio.Count(t => t.SoDele)} canal(is) marcado(s))"
			   + (repetidos.Count > 0 ? $" -- {string.Join("; ", repetidos)}" : ""));
	}

	// =====================================================================
	// 11-ter. A ARTE APOSENTADA NAO TEM MAIS DONO
	// =====================================================================
	/// <summary>
	/// NINGUEM CARREGA A `Aurabigcombined` -- e isso e uma pergunta sobre o REPOSITORIO, nao sobre um node.
	///
	/// ============================ POR QUE UMA VARREDURA, E NAO UMA CHECAGEM DE NODE ============================
	/// O dono: *"vamos trocar das cinematicas o Aurabigcombined pela propria aura da transformaçao q vc ta
	/// virando"*. As checagens vivas provam que os TRES desenhos de aura que existem hoje escolhem a folha
	/// certa -- e nao provam nada sobre um QUARTO desenho que alguem acrescente amanha com a arte velha, nem
	/// sobre um `.tscn` que a carregue direto sem passar por C#.
	///
	/// A pergunta certa e negativa e larga: *existe algum jeito de essa arte entrar em jogo?* Enquanto nao
	/// houver um so caminho, ela nao pode voltar por engano. E a mesma forma do `OGpuParticlesNaoVoltouAoFonte`,
	/// so que sobre o repositorio inteiro em vez de um arquivo.
	///
	/// ============================ E O ARQUIVO CONTINUA NA PASTA, DE PROPOSITO ============================
	/// Ela e arte-fonte do DM, que ainda a usa em doze lugares (`SSJCinematic.dm`, `lssjbuff.dm`,
	/// `UltraInstinct.dm`...), e ha porte pela frente. Entao o que se cobra NAO e "o arquivo sumiu": e que
	/// nenhum codigo, cena ou recurso aponte pra ele. Por isso os proprios `Aurabigcombined.*` ficam de fora
	/// da varredura -- o `.tres` referencia o `.png` dele mesmo, e isso e o arquivo existindo, nao um dono.
	/// ========================================================================================================
	///
	/// ============================ OS COMENTARIOS SAEM, E ELES SAO MUITOS ============================
	/// O nome aparece em treze linhas de prosa deste projeto (a historia da troca esta contada em cinco
	/// arquivos). Uma varredura crua acusaria a documentacao e a unica saida seria apagar a explicacao --
	/// exatamente o contrario do que este repositorio quer. Some o filtro de comentario do
	/// `OGpuParticlesNaoVoltouAoFonte`, que ja provou dar conta.
	/// ==========================================================================================
	///
	/// COMO REPROVA SE A REGRA SUMIR: escreva `ResourceLoader.Load(".../Aurabigcombined.tres")` em qualquer
	/// `.cs`, ou ponha a folha num `.tscn`, e a primeira linha cai dizendo o arquivo e a linha.
	/// </summary>
	private void AArteVelhaNaoTemDono()
	{
		// ============================ OS DOIS NOMES SAO MONTADOS EM METADES ============================
		// Escritos inteiros, os primeiros achados da varredura seriam ESTAS PROPRIAS LINHAS -- ela
		// acusaria a si mesma, e a unica saida seria isentar o arquivo da bancada. Isentar seria pior do
		// que o defeito: o unico ponto cego do repositorio passaria a ser justamente o arquivo que decide
		// quem enxerga, e qualquer carga escrita aqui entraria de graca.
		//
		// MEDIDO, e nao previsto: a primeira rodada deste bloco reprovou com *"3 linha(s), a primeira em
		// RoboDeForma.cs:6902"* -- as tres eram as tres linhas em que o nome aparecia aqui.
		//
		// O DA FOLHA VIVA VAI JUNTO pelo mesmo motivo, e nele o estrago seria mais calado: o controle
		// (`> 0`) ficaria verde por causa da propria busca, ou seja continuaria passando no dia em que o
		// jogo parasse de carregar a folha certa.
		// ==========================================================================================
		const string Aposentada = "Aurabig" + "combined";
		const string EmUso = "colorable" + "bigaura";

		// AS QUATRO PORTAS por onde uma arte entra em jogo neste projeto: codigo, cena, recurso e shader.
		// O `.import` e o `.uid` ficam de fora porque sao METADADOS do arquivo existir -- eles falam da
		// arte, nao de um consumidor dela.
		string[] extensoes = [".cs", ".tscn", ".tres", ".gdshader"];

		List<string> Fontes()
		{
			var l = new List<string>();
			string raiz = ProjectSettings.GlobalizePath("res://");
			if (!System.IO.Directory.Exists(raiz)) return l;

			foreach (string f in System.IO.Directory.GetFiles(raiz, "*.*",
					 System.IO.SearchOption.AllDirectories))
			{
				// O CACHE DO GODOT E O BUILD FICAM DE FORA: o `.godot/` guarda uma copia importada de TODA
				// arte do projeto, entao varre-lo acusaria a existencia do arquivo como se fosse um dono.
				string rel = f[raiz.Length..].Replace('\\', '/');
				if (rel.StartsWith(".godot/") || rel.StartsWith("obj/") || rel.StartsWith("bin/")) continue;
				if (!extensoes.Contains(System.IO.Path.GetExtension(f))) continue;
				// A PROPRIA ARTE NAO CONTA COMO DONO -- ver o cabecalho.
				if (System.IO.Path.GetFileName(f).StartsWith(Aposentada, StringComparison.Ordinal))
					continue;
				l.Add(f);
			}
			return l;
		}

		List<string> fontes = Fontes();

		// O ZERO NAO PODE SER VAZIO: uma varredura que nao achasse arquivo nenhum (caminho errado, projeto
		// exportado, filtro trocado) daria as duas linhas de baixo em verde pra sempre.
		Conferir(fontes.Count > 50,
				 $"a varredura enxerga o repositorio ({fontes.Count} arquivo(s) de codigo, cena e recurso)");
		if (fontes.Count == 0) return;

		// OS DOIS NOMES NA MESMA PASSADA, e nao uma varredura por nome: sao ~3700 arquivos, e ler o
		// repositorio duas vezes pra responder duas perguntas sobre a mesma linha e pagar o dobro por nada.
		int velha = 0, nova = 0;
		string ondeVelha = "", ondeNova = "";
		foreach (string f in fontes)
		{
			int linha = 0;
			foreach (string l in System.IO.File.ReadAllLines(f))
			{
				linha++;
				// O FILTRO DE COMENTARIO do `OGpuParticlesNaoVoltouAoFonte` -- ver o cabecalho: sem ele o
				// que reprovaria seria a documentacao da propria troca, contada em cinco arquivos.
				string s = l.TrimStart();
				if (s.StartsWith("//") || s.StartsWith("*") || s.StartsWith("/*")) continue;

				if (l.Contains(Aposentada, StringComparison.Ordinal))
				{
					velha++;
					if (ondeVelha.Length == 0) ondeVelha = $"{f.GetFile()}:{linha}";
				}
				if (l.Contains(EmUso, StringComparison.Ordinal))
				{
					nova++;
					if (ondeNova.Length == 0) ondeNova = $"{f.GetFile()}:{linha}";
				}
			}
		}

		Conferir(velha == 0,
				 $"nada em codigo, cena ou recurso carrega a arte aposentada da cinematica "
			   + $"({velha} linha(s)"
			   + (ondeVelha.Length > 0 ? $", a primeira em {ondeVelha}" : "") + ")");

		// O CONTROLE, e ele e o par exato: a folha que TOMOU o lugar dela tem que aparecer na mesma
		// varredura, na mesma passada e com o mesmo filtro. Sem ele, um filtro de comentario que
		// engolisse tudo (ou uma leitura que falhasse calada) daria a linha de cima em verde com a arte
		// velha de volta no jogo.
		Conferir(nova > 0,
				 $"e a varredura ACHA a folha que tomou o lugar dela ({nova} linha(s)"
			   + (ondeNova.Length > 0 ? $", a primeira em {ondeNova}" : "") + ")");
	}

	/// <summary>
	/// O CORPO DE UM METODO, por casamento de chaves. A <paramref name="assinatura"/> e procurada
	/// literal: assinatura que mude de forma faz a primeira checagem do bloco cair, que e melhor do
	/// que um extrator esperto devolvendo o metodo errado em silencio.
	/// </summary>
	private static string CorpoDoMetodo(string fonte, string assinatura)
	{
		int i = fonte.IndexOf(assinatura, StringComparison.Ordinal);
		if (i < 0) return "";
		int abre = fonte.IndexOf('{', i);
		if (abre < 0) return "";

		int nivel = 0;
		for (int k = abre; k < fonte.Length; k++)
		{
			if (fonte[k] == '{') nivel++;
			else if (fonte[k] == '}' && --nivel == 0) return fonte[(abre + 1)..k];
		}
		return "";
	}

	/// <summary>
	/// OS NOMES DE METODO CHAMADOS num trecho de codigo -- `algo.Metodo(` e `algo?.Metodo(`.
	///
	/// So os que comecam com MAIUSCULA, que em C# e a convencao de metodo: isso deixa de fora os
	/// campos e as variaveis locais e nao precisa de um analisador de verdade pra uma pergunta que e
	/// "quem foi tocado aqui".
	/// </summary>
	private static string[] EscritasDe(string codigo) =>
		[.. System.Text.RegularExpressions.Regex
			.Matches(codigo, @"\??\.\s*([A-Z][A-Za-z0-9_]*)\s*\(")
			.Select(m => m.Groups[1].Value)
			.Distinct()];

	/// <summary>
	/// O fonte de um arquivo, ou vazio se ele nao estiver la. Vazio e melhor do que excecao: quem
	/// chama ja tem uma linha que exige corpo com tamanho, e ela diz QUAL arquivo veio vazio.
	/// </summary>
	private static string LerFonte(string caminho) =>
		System.IO.File.Exists(caminho) ? System.IO.File.ReadAllText(caminho) : "";

	/// <summary>
	/// OS COMENTARIOS SAEM ANTES DE QUALQUER COISA. Os arquivos deste projeto sao mais comentario que
	/// codigo, e as chaves e os `.Metodo(` que aparecem DENTRO deles fariam tanto o casador de chaves
	/// (<see cref="CorpoDoMetodo"/>) quanto os extratores lerem prosa como codigo -- o
	/// `Transformacao.cs` tem, escrito em comentario, o proprio `vis.AuraDaForma(...)` que a bancada
	/// existe pra proibir.
	/// </summary>
	private static string SemComentario(string fonte) =>
		System.Text.RegularExpressions.Regex.Replace(fonte, @"//[^\n]*", "");

	/// <summary>
	/// OS CANAIS VISUAIS QUE UM TRECHO ESCREVE, QUALIFICADOS PELO TIPO DO NODE -- `Aura.Folha`,
	/// `CharacterVisual.CorpoDaForma`, `RaiosDaForma.Definir`.
	///
	/// ============================ POR QUE O TIPO, E NAO SO O NOME ============================
	/// O extrator antigo (<see cref="EscritasDe"/>) devolve so o nome depois do ponto, e nele
	/// `aura.Folha(...)` e `Catalogo.Folha(...)` sao a MESMA palavra. Era isso que fazia a lista velha
	/// de canais ser ambigua: ela cobrava "Folha" e teria ficado verde com a folha da aura deletada,
	/// desde que alguem ainda perguntasse a folha ao catalogo na mesma linha.
	///
	/// ============================ O NODE E QUEM DIZ O TIPO ============================
	/// Nao ha analisador aqui, e nem precisa: neste projeto todo desenho de forma comeca em
	/// `GetNodeOrNull&lt;T&gt;("Nome")`, nas tres formas que o C# permite escrever --
	///
	///   `... GetNodeOrNull&lt;T&gt;("x") is { } v`   (e o `is not { } v`, que e a mesma ligacao)
	///   `var v = ... GetNodeOrNull&lt;T&gt;("x");`
	///   `... GetNodeOrNull&lt;T&gt;("x")?.Metodo(...)`  (sem variavel nenhuma)
	///
	/// -- e dai em diante a variavel carrega o tipo. Quem escrever num campo guardado na classe
	/// (`_visual.Algo()`) fica invisivel pra aqui, e isso esta dito no cabecalho de quem usa.
	///
	/// SO METODO COM MAIUSCULA, pela mesma convencao do <see cref="EscritasDe"/>: e o que separa
	/// chamada de campo sem precisar de um analisador de verdade.
	/// </summary>
	private static string[] CanaisDe(string codigo)
	{
		var tipoDaVar = new Dictionary<string, string>(StringComparer.Ordinal);
		var canais = new List<string>();

		string Curto(string tipo) => tipo[(tipo.LastIndexOf('.') + 1)..];

		// `GetNodeOrNull<T>("x") is { } v` -- e o `is not { } v` do `AplicarContorno`, que e a mesma
		// ligacao escrita pelo avesso (a guarda sai por `return`, e o resto do metodo tem o node).
		foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
					 codigo, @"GetNodeOrNull<\s*([A-Za-z0-9_.]+)\s*>\s*\([^)]*\)\s*is\s+(?:not\s+)?\{\s*\}\s+(\w+)"))
			tipoDaVar[m.Groups[2].Value] = Curto(m.Groups[1].Value);

		// `var v = ...GetNodeOrNull<T>("x");`
		foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
					 codigo, @"\b(?:var|[A-Za-z0-9_.?]+)\s+(\w+)\s*=\s*[^;]*?GetNodeOrNull<\s*([A-Za-z0-9_.]+)\s*>"))
			tipoDaVar[m.Groups[1].Value] = Curto(m.Groups[2].Value);

		// `GetNodeOrNull<T>("x")?.Metodo(` -- a folha da `CargaVisual` e escrita assim nos dois
		// vestidores, sem variavel nenhuma no caminho.
		foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
					 codigo, @"GetNodeOrNull<\s*([A-Za-z0-9_.]+)\s*>\s*\([^)]*\)\s*\??\.\s*([A-Z]\w*)\s*\("))
			canais.Add(Curto(m.Groups[1].Value) + "." + m.Groups[2].Value);

		// AS CHAMADAS EM CADA NODE CONHECIDO. O `(?<![A-Za-z0-9_.])` mantem `aura` e `Aura` separados
		// -- o C# e sensivel a maiuscula, e `Aura.CorDaChamaDe(d)` e uma PERGUNTA estatica ao tipo, nao
		// uma escrita no node `aura`. Sem essa distincao a conta estatica entraria como canal.
		foreach ((string v, string tipo) in tipoDaVar)
			foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
						 codigo, @"(?<![A-Za-z0-9_.])" + System.Text.RegularExpressions.Regex.Escape(v)
							   + @"\s*\??\.\s*([A-Z]\w*)\s*\("))
				canais.Add(tipo + "." + m.Groups[1].Value);

		return [.. canais.Distinct().OrderBy(c => c, StringComparer.Ordinal)];
	}

	/// <summary>
	/// TODO `.cs` DE PRODUCAO, ja sem comentario -- o par (caminho, texto).
	///
	/// AS BANCADAS FICAM DE FORA (`RoboDe*.cs`) e isso e desenho: elas dirigem o visual na mao, que e
	/// o trabalho delas -- um robo que veste um contorno pra medi-lo nao e um segundo dono do
	/// contorno. Isso inclui ESTE arquivo, e por isso a tabela de donos escrita aqui nao se acusa.
	///
	/// O `.godot/`, o `obj/` e o `bin/` saem pelo mesmo motivo do <see cref="AArteVelhaNaoTemDono"/>:
	/// sao copias geradas, e varre-las contaria o mesmo codigo duas vezes.
	/// </summary>
	private static List<(string Arquivo, string Texto)> FontesDeProducao()
	{
		var l = new List<(string, string)>();
		string raiz = ProjectSettings.GlobalizePath("res://");
		if (!System.IO.Directory.Exists(raiz)) return l;

		foreach (string f in System.IO.Directory.GetFiles(raiz, "*.cs", System.IO.SearchOption.AllDirectories))
		{
			string rel = f[raiz.Length..].Replace('\\', '/');
			if (rel.StartsWith(".godot/") || rel.StartsWith("obj/") || rel.StartsWith("bin/")) continue;
			if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
			if (System.IO.Path.GetFileName(f).StartsWith("RoboDe", StringComparison.Ordinal)) continue;
			l.Add((f, SemComentario(System.IO.File.ReadAllText(f))));
		}
		return l;
	}

	/// <summary>
	/// Solta uma rajada agora, pra a foto do quadro seguinte pega-la no ar.
	///
	/// E CONFERE QUE A FORMA POSADA TEM RAIO. O `DispararDeTeste` nao pergunta nada -- ele restarta
	/// o emissor mesmo com o node desligado, que e o que faz dele util pra a foto e perigoso pra a
	/// leitura dela. Com o `blue` posado (`Raios = 0`) a foto saia com faisca numa forma que nao tem
	/// nenhuma, e o unico jeito de descobrir isso era alguem abrir o PNG e desconfiar.
	/// </summary>
	/// <param name="cobrar">
	/// Se as duas checagens saem. FALSO nas cinco repeticoes da segunda tira de fotos: elas rodam no
	/// mesmo corpo, na mesma forma, a 0,14 s uma da outra -- cinco copias da mesma linha verde nao
	/// medem nada de novo e enterrariam o log.
	/// </param>
	private void ForcarRajada(bool cobrar = true)
	{
		if (GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D corpo) return;
		if (corpo.GetNodeOrNull<RaiosDaForma>("Raios") is not { } r) return;
		if (!cobrar) { r.DispararDeTeste(); return; }
		Conferir((Jandirus.Core.Forms.Catalogo.Def(_posada)?.Raios ?? 0) > 0,
				 $"a forma posada pra a foto (`{_posada}`) TEM raio -- senao a foto mente");

		// ============================ E O NODE PRECISA ESTAR LIGADO, NAO SO EMITINDO ============================
		// A linha de cima cobra o CATALOGO; esta cobra o NODE. Sao coisas diferentes e a diferenca custou
		// uma rodada inteira: `Definir(false, ...)` (que toda cinematica dispara ao despir a forma) apaga
		// `_ligado` e esconde o emissor, e o `DispararDeTeste` continua devolvendo `emitindo=True` porque
		// `Restart()` nao pergunta se alguem vai ver. Dez fotos limpas sairam sem um pixel de faisca com
		// esta bancada dando TUDO OK.
		// ====================================================================================================
		Conferir(r.IntensidadeDeTeste > 0,
				 $"e o node de raios esta LIGADO na hora da rajada (intensidade {r.IntensidadeDeTeste}) -- "
			   + "emissor restartado com o node apagado da foto sem faisca e log verde");
		r.DispararDeTeste();
		_passos.Add($"  --     rajada forcada: {r.UltimaRajadaDeTeste} raio(s), emitindo={r.EmitindoDeTeste}");
	}

	/// <summary>Poe o corpo numa forma so pra a foto. Nao passa pelo servidor -- e so pintura.</summary>
	/// <summary>Qual forma esta posada pra a foto. Lida pelo <see cref="ForcarRajada"/>.</summary>
	private string _posada = "";

	/// <summary>
	/// A OUTRA PONTA do contorno, ou nula quando a forma nao oscila. Igual a do `World` -- a bancada
	/// tem que passar pelo mesmo par que o jogo passa, senao ela exercita um contorno parado numa
	/// forma que em jogo esta trocando de cor.
	/// </summary>
	private static Color? ContornoAlterna(FormaDef? d) =>
		Jandirus.Core.Forms.Catalogo.CorDoContornoAlterna(d) is { } hexa ? new Color(hexa) : null;

	private void Posar(string id)
	{
		if (GetTree().Root.FindChild("LocalPlayer", true, false) is not Node2D corpo) return;
		FormaDef? d = Jandirus.Core.Forms.Catalogo.Def(id);
		if (d == null) return;
		_posada = id;

		corpo.GetNodeOrNull<RaiosDaForma>("Raios")
			 ?.Definir(true, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(d)), d.Raios);
		corpo.GetNodeOrNull<CharacterVisual>("Visual")
			 ?.AuraDaForma(new Color(Jandirus.Core.Forms.Catalogo.CorDoContorno(d)),
						   0.35f + d.Intensidade * 0.13f, ContornoAlterna(d));
		corpo.GetNodeOrNull<Aura>("Aura")?.Acender(new Color(d.Aura), 0.8f + d.Intensidade * 0.5f);
		_passos.Add($"  --     posando em {d.Nome} (aura #{d.Aura}) pra a segunda foto");
	}

	// =====================================================================
	// A FOTO DA RAJADA PRECISA DE CAMPO ABERTO, MEIO-DIA E LUPA
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ESTE BLOCO EXISTE ============================
	/// As fotos `raj05..raj12` sao a UNICA parte desta bancada que responde "esta bonito", e a rodada
	/// que me mandou escrever isto mostrou que elas nao respondiam nada: o recorte inteiro era uma
	/// NUVEM MARRON opaca com o personagem invisivel debaixo dela. Nao e defeito de arte -- e a
	/// POEIRA das cinematicas que os blocos do passo 2 rodam (`NoCorpo`, `AIdaEAVolta`,
	/// `AVoltaDesfazTudo`), que despejam poeira, cascalho e cratera em volta do corpo e nao evaporam
	/// a tempo. A `RoboDeNebulosa` ja tinha levado esse tombo e o anotou com todas as letras: *"as
	/// quatro fotos daquela rodada sairam com uma nuvem MARROM cobrindo o personagem ... eu quase
	/// reportei 'a rampa esta saindo marrom'"*.
	///
	/// Nenhuma das 4800 checagens desta bancada enxerga isso: o raio E emitido, o uniform E escrito,
	/// o shader E o compilado -- tudo verde, e a foto mostrando terra. E o cego que esta casa ja
	/// nomeou ("uniform escrito nao e pixel desenhado"), agora do lado do ENQUADRAMENTO.
	///
	/// E A MINHA PRIMEIRA HIPOTESE ESTAVA ERRADA, o que vale guardar: na foto de madrugada e debaixo
	/// de nevasca a mancha saia VERDE-ESCURA e eu a li como copa de arvore. Foi o meio-dia sem
	/// nevasca que revelou que ela era marrom. Por isso a busca de copa (que a versao anterior deste
	/// bloco fazia) foi DELETADA: ela media o cenario, respondia "ja estou em campo aberto, 462 px da
	/// copa mais perto" -- corretamente -- e a foto continuava sendo terra.
	///
	/// Tres consertos, e os tres sao de camera e nao de arte:
	///   1. MEIO-DIA pelo verb (`admin_meio_dia`), nao pelo `--horateste`: quem roda a bancada sem a
	///      bandeira ficava com a hora sorteada. E o clima no minimo, pelo mesmo `admin_clima` que a
	///      `--diagnebulosa` ja usa (nao da pra pedir "limpo" -- ver o comentario de la).
	///   2. ANDAR PRA LONGE DA SUJEIRA, que e o unico jeito confiavel: a poeira e local e nao segue o
	///      corpo, e a cratera e um decalque que NAO evapora (esperar nao resolve o segundo).
	///   3. LUPA: a tela e 1920x1080 e o raio tem ~1 px de largura. Uma foto de tela inteira nao
	///      distingue "afinou" de "sumiu" -- e a queixa do dono era justamente sobre GROSSURA.
	/// ==========================================================================================
	/// </summary>
	private void OCeuEOClimaDaFoto()
	{
		GameClient.Instance?.SendVerbo("admin_meio_dia");
		GameClient.Instance?.SendVerbo("admin_clima", "Neblina|0.05");
		_passos.Add("  --     ceu pedido no meio-dia e clima no minimo -- sem isto a foto da rajada "
				  + "sai de madrugada e/ou debaixo de nevasca (a nevasca ainda salpica ponto branco, "
				  + "que ja foi lido como faisca uma vez neste arquivo)");
	}

	/// <summary>
	/// Um passo da fuga da poeira. Devolve `true` enquanto ainda ha o que andar -- quem chama repete
	/// o proprio passo. Ver <see cref="OCeuEOClimaDaFoto"/> pra o motivo.
	///
	/// O ALVO E ESCOLHIDO NA PRIMEIRA CHAMADA e e simplesmente "longe daqui": onde o corpo esta
	/// quando esta funcao roda pela primeira vez e, por construcao, o epicentro de tudo que as cenas
	/// do passo 2 cavaram. Nao ha o que procurar no cenario -- o estorvo nao e cenario.
	///
	/// A TOLERANCIA DE 24 px E A DA `RoboDeNebulosa.AndarAteOAlvo`, e pelo mesmo motivo escrito la:
	/// uma decisao a cada 0,2 s ja anda ~22 px, entao um alvo com meio passo de folga faz o robo
	/// oscilar em volta dele ate estourar o prazo.
	/// </summary>
	private bool SairDaSujeira()
	{
		string[] teclas = ["move_left", "move_right", "move_up", "move_down"];
		if (Corpo is not { } corpo) return false;

		if (_alvoLimpo is not { } alvo)
		{
			// PRA CIMA E PRA DIREITA: pra cima porque a poeira sobe e o boneco tem que sair de baixo
			// dela, e nao so de dentro; 224 px sao 7 tiles, o dobro do raio da cratera cheia
			// (`Cinematicas.TilesDoTremorCheio` e 6).
			alvo = corpo.GlobalPosition + new Vector2(160, -160);
			_alvoLimpo = alvo;
			_caminhando = true;
			_passos.Add($"  --     saindo da poeira das cenas: de {corpo.GlobalPosition} pra {alvo} "
					  + "(a foto da rajada e a unica coisa aqui que responde 'esta bonito')");
		}

		Vector2 falta = alvo - corpo.GlobalPosition;
		bool travou = _voltasDaCaminhada > 0 && corpo.GlobalPosition.DistanceTo(_ondeEuEstava) < 0.5f;

		if (falta.Length() > 24f && !travou && _voltasDaCaminhada++ < 60)
		{
			_ondeEuEstava = corpo.GlobalPosition;
			foreach (string t in teclas) Input.ActionRelease(t);
			Input.ActionPress(Mathf.Abs(falta.X) > Mathf.Abs(falta.Y)
				? falta.X > 0 ? "move_right" : "move_left"
				: falta.Y > 0 ? "move_down" : "move_up");
			return true;
		}

		foreach (string t in teclas) Input.ActionRelease(t);
		if (_voltasDeAssentar == 0)
			_passos.Add($"  --     caminhada: parei a {falta.Length():0} px do alvo em "
					  + $"{_voltasDaCaminhada * 0.2:0.0}s"
					  + (travou ? " (TRAVEI num obstaculo -- a foto pode sair com ele na frente)" : ""));

		// ============================ E DEPOIS DE CHEGAR, UM RESPIRO ============================
		// A POEIRA DO PROPRIO PASSO tambem levanta, e ela e a ultima coisa que fica entre a camera e o
		// raio. Seis voltas de 0,2 s (o `_caminhando` continua ligado justamente pra a guarda usar os
		// 0,2 s) sao 1,2 s -- o mesmo respiro que a `RoboDeNebulosa.PararDeAndar` da, pelo mesmo motivo.
		// ====================================================================================
		if (_voltasDeAssentar++ < 6) return true;

		_caminhando = false;
		return false;
	}

	/// <inheritdoc cref="SairDaSujeira"/>
	private bool _caminhando;
	private Vector2? _alvoLimpo;
	private int _voltasDaCaminhada;
	private int _voltasDeAssentar;
	private Vector2 _ondeEuEstava;

	/// <summary>
	/// Salva a tela, se houver renderizador. No headless o `GetImage` volta vazio.
	///
	/// E SALVA A LUPA JUNTO (`-zoom`), centrada no boneco. Ver o item 3 do <see cref="SairDaCopa"/>:
	/// o raio tem ~1 px de largura numa tela de 1920, e a pergunta que estas fotos existem pra
	/// responder e sobre a GROSSURA dele. `Nearest` e nao `Bilinear` pelo motivo de sempre em arte de
	/// pixel -- interpolar INVENTA tons, e um raio de 1 px ampliado com suavizacao vira um borrao de
	/// 4 px que se le como "engordou".
	/// </summary>
	private void Fotografar(string destino)
	{
		try
		{
			Image? img = GetViewport()?.GetTexture()?.GetImage();
			if (img == null || img.IsEmpty()) { _passos.Add("  --     sem foto (headless nao renderiza)"); return; }
			string caminho = ProjectSettings.GlobalizePath(destino);
			img.SavePng(caminho);
			_passos.Add("  ok     foto salva em " + caminho);

			if (Corpo is not { } corpo) return;
			Vector2 p = corpo.GetGlobalTransformWithCanvas().Origin;
			const int Meio = 160;   // 320 px de tela em volta do boneco: ele tem 32 e a rajada ~64
			int lado = Mathf.Min(Meio * 2, Mathf.Min(img.GetWidth(), img.GetHeight()));
			int x = Mathf.Clamp((int)p.X - lado / 2, 0, img.GetWidth() - lado);
			int y = Mathf.Clamp((int)p.Y - lado / 2, 0, img.GetHeight() - lado);
			Image lupa = img.GetRegion(new Rect2I(x, y, lado, lado));
			lupa.Resize(lado * 3, lado * 3, Image.Interpolation.Nearest);
			lupa.SavePng(ProjectSettings.GlobalizePath(destino.Replace(".png", "-zoom.png")));
		}
		catch (Exception e) { _passos.Add("  --     sem foto: " + e.Message); }
	}

	private void Fechar()
	{
		// ============================ O TOTAL DA RODADA, E ELE E UM ============================
		// Ultima coisa a rodar, e de proposito: e a unica checagem que ve a execucao INTEIRA. As
		// outras medem trechos com linha de base, e um trecho sem base nenhuma (as cenas que os
		// blocos ao vivo criam, a cena de estreia que corre no relogio da ENGINE e e conferida no
		// `CenaDepois`, a cena posada pra a foto) ficaria fora da conta de todas elas.
		//
		// O ESPERADO E UM, e o um tem nome: a cena montada pra nao conseguir terminar, no bloco do
		// teto. Zero seria pior que dois -- significaria que o teto parou de existir e que aquele
		// bloco nao mede mais nada. E qualquer numero acima de um e uma cena que precisou da rede
		// sem ninguem ter pedido, que e a definicao do defeito.
		//
		// COMO REPROVA SE A REGRA SUMIR: qualquer caminho novo que deixe uma cena rodando alem do
		// prazo -- um `SetProcess(false)` esquecido num bloco novo, uma cena criada e nunca
		// bombeada, o guarda do `_Process` removido -- soma aqui, mesmo que o bloco culpado nao
		// tenha checagem propria. E a unica linha desta bancada que cobre o codigo que ainda nao
		// foi escrito.
		// ================================================================================
		Conferir(Transformacao.TetosDeTeste == 1,
				 $"na RODADA INTEIRA o teto disparou uma vez so, e foi a cena que o obriga "
			   + $"({Transformacao.TetosDeTeste} aviso(s) no total)");

		_acabou = true;
		GD.Print("[forma] ===== BANCADA DOS EFEITOS DE FORMA =====");
		foreach (string p in _passos) GD.Print("[forma] " + p);
		GD.Print(_falhas.Count == 0
			? "[forma] ===== TUDO OK ====="
			: $"[forma] ===== {_falhas.Count} FALHA(S) =====\n[forma]   " + string.Join("\n[forma]   ", _falhas));
	}
}
