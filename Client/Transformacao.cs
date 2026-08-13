using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Client;

/// <summary>
/// A CINEMATICA DE UMA FORMA -- o tocador do roteiro que mora em <see cref="Cinematicas"/>.
///
/// ============================ ELE TOCA AS DUAS VERSOES, E NAO SO A ESTREIA ============================
/// Este arquivo dizia "a cinematica da PRIMEIRA VEZ" porque so havia duas situacoes: cena cheia ou
/// nada. Hoje sao tres degraus (ver <see cref="DegrauDeCena"/>) e o do meio -- a cena ENCURTADA, que
/// roda toda vez ate a maestria da forma passar dos 50% -- e a que o jogador vai ver na maioria das
/// vezes. Ela chega aqui pelo mesmo <see cref="Rodar"/>, com o mesmo tipo: quem a encurta e o Core, e
/// o tocador nao precisa saber qual das duas esta na mao.
///
/// Isso e o que permitiu a mudanca inteira sem tocar em uma linha deste arquivo.
/// ==================================================================================================
///
/// ============================ UM TOCADOR, NAO NOVE CENAS ============================
/// No BYOND cada cena e um `proc` proprio com os `sleep()` no meio dos efeitos
/// (`SSJCinematic.dm`, `SSJ2Cinematic.dm`, `SSJ3Cinematic.dm`...). Aqui a cena e uma lista de
/// <see cref="Beat"/> com o segundo de cada um, e esta classe so a executa. Forma nova = lista
/// nova; ninguem escreve outro tocador.
/// =============================================================================
///
/// ============================ O CABELO E A AURA SO NO BEAT `Assumir` ============================
/// Isto e o que separa uma cinematica de um efeito por cima. No DM o cabelo PISCA entre o normal e
/// o dourado durante a cena e so fica dourado no fim; se a forma fosse aplicada no comeco, o
/// personagem ja estaria transformado assistindo a propria transformacao.
///
/// Por isso o <see cref="World.AoMudarForma"/> ENTREGA a aplicacao pra ca quando ha cena: quem
/// pinta o cabelo e acende a aura e o beat, nao o pacote.
/// =========================================================================================
/// </summary>
public partial class Transformacao : Node2D
{
	// --- o que a cena precisa saber ---
	private Node2D _alvo = null!;
	private Cinematica _cena = null!;

	/// <summary>
	/// QUE FORMA ESTA CENA ESTA CONTANDO -- e **nulo quando ela nao conta forma nenhuma**.
	///
	/// ============================ O NULO E A FURIA, E ELE E UM ESTADO E NAO UM ERRO ============================
	/// Ate aqui toda cena deste tocador era a estreia de uma forma, e o campo era `null!` -- a promessa
	/// de que nunca faltaria. A <see cref="Jandirus.Core.Forms.Cinematicas.Furia"/> quebra a promessa
	/// de proposito: ela e o `AngerCinematic()` do DM (`Murder.dm:136`), que nao veste cabelo, nao
	/// acende aura de forma e nao termina com ninguem transformado.
	///
	/// Os onze usos deste campo passaram a perguntar, e cada um responde o que o DM responde:
	///
	///   * a CHAMA da cena (`ChamaDoDegrau`) -- sem forma ela e o vermelho da raiva, que e o unico
	///     lugar do port onde a `Aurabigcombined` mantem cor propria (ver `Cinematicas.CorDaFuria`);
	///   * a CRATERA (`Catalogo.NasceDaRaiva`) -- ja aceitava nulo, e devolve a cratera PEQUENA, que e
	///     o `createCrater(loc,2)` da furia contra o `(loc,3)` do SSJ1;
	///   * `Assumir`, `VesteDegrau`, `PiscaCabelo`, `Raios` e `BanhoDeCor` -- **nao existem na cena da
	///     furia**, e mesmo assim cada um foi fechado: um beat escrito por engano num roteiro futuro
	///     nao pode despir o personagem.
	///
	/// A ALTERNATIVA ERA PASSAR A `base` DO CATALOGO no lugar do nulo, e ela e pior de um jeito calado:
	/// `Assumir` com a base VESTE a base -- ou seja, a cena de furia de um Super Saiyajin o devolveria
	/// ao normal. Nulo obriga a pergunta; a base a esconde.
	/// ======================================================================================================
	/// </summary>
	private FormaDef? _forma;

	/// <summary>
	/// COMO CHAMAR ESTA CENA NUM AVISO DE LOG. `_cena.Forma` e o id quando ha forma; sem forma ele e
	/// vazio, e ai o nome e "furia" -- o unico caso que existe hoje. Serve so pra prosa de diagnostico.
	/// </summary>
	private string NomeDaCena() => _cena.Forma.Length > 0 ? _cena.Forma : "furia";

	// ============================ DUAS CORES, E ANTES ERA UMA SO ============================
	// Era um `_cor` unico, tirado da `FormaDef.Aura`, e ele pintava a chama, o contorno do sprite,
	// os raiozinhos e os feixes de chao. Enquanto foi um campo so, todo pedido de ajuste de cor
	// atravessava o efeito errado -- e o defeito aparecia na cinematica, longe de onde eu tinha
	// mexido. Quem decide agora e o Core (`Catalogo.CorDoContorno` / `CorDosRaios`), pelas mesmas
	// funcoes que o `World` usa fora da cena: uma forma nao pode estrear numa paleta e ficar noutra.
	//
	// ERAM TRES. As duas do CONTORNO sairam quando o `Vestir` passou a servir tambem aos degraus
	// intermediarios: elas guardavam a cor da forma ALVO, e um degrau precisa da cor DELE. Guardar
	// a da forma alvo e perguntar a do degrau seria ter as duas respostas para a mesma pergunta --
	// entao o `Vestir` pergunta ao Core as duas, uma vez por degrau (sao 3 numa cena de 35 s).
	// =====================================================================================
	private Color _corAura;
	private Color _corRaios;
	private bool _souEu;

	/// <summary>O nome de quem esta virando -- so pra linha do chat de quem assiste. Ver `Rodar`.</summary>
	private string _nome = "";

	private double _t;
	private int _proximo;
	private bool _piscando, _soltou;

	/// <summary>
	/// O PISCAR DE CABELO ESTA LIGADO -- e ele e um ESTADO, do beat que o arma ate o `Assumir`.
	///
	/// ============================ POR QUE UM ESTADO, E NAO MAIS BEATS ============================
	/// O dono: *"a do ssj1 o cabelo base e do ssj ficam trocando (oq e legal) mas e so no inicio da
	/// cinematica, teria q durar a cinematica toda, e na transformaçao acelerada (maestria menor q 50%)
	/// tb"*. Era um pulso por beat, e a cena do SSJ1 gastava quatro beats no primeiro segundo e meio de
	/// vinte e um.
	///
	/// A justificativa longa mora no `Efeito.PiscaCabelo` (Core), que e onde a decisao pertence. Aqui
	/// so o mecanismo: o beat liga, o `_Process` conta, o `Assumir` desliga.
	/// =======================================================================================
	/// </summary>
	private bool _piscaLigada;

	/// <summary>Em que instante da cena o cabelo troca de lado. Sorteado a cada troca -- o `rand(3,10)`.</summary>
	private double _proximaPiscada;

	/// <summary>
	/// O QUE O CORPO ESTAVA VESTINDO ANTES DE O PISCAR COMECAR -- pra ele ter pra onde VOLTAR.
	///
	/// Nulo e um valor legitimo aqui ("nenhuma forma", que e a base), e por isso ele nao serve de
	/// sentinela; quem responde "esta piscando?" e o <see cref="_piscaLigada"/>.
	///
	/// ============================ ELE EXISTE POR CAUSA DA CENA INTERROMPIDA ============================
	/// Com o piscar durando um segundo e meio, uma cena morta no meio dele quase nunca pegava o lado
	/// errado. Durando vinte, pega metade das vezes -- e o resultado seria o jogador andando por ai com
	/// o cabelo de uma forma que ele NAO assumiu, ate o proximo pacote de aparencia. E a mesma familia
	/// do "relog careca" que este projeto ja pagou: buff visual que sobrevive a quem o acendeu.
	/// ============================================================================================
	/// </summary>
	private FormaDef? _vestido;

	// --- os nodes da cena ---
	//
	// A ONDA DE CHOQUE SAIU. Era um `ColorRect` de 320x320 com o shader de transformacao desenhado
	// por cima do corpo: uma espiral escura que se abre, e num corpo de 32 px ela nao le como
	// "onda saindo do chao" -- le como o personagem sumindo atras de um borrao. Ver
	// `Efeito.Onda` no Core.
	//
	// ============================ A CHAMA DA CENA E A AURA DA PROPRIA FORMA ============================
	// O dono: *"vamos trocar das cinematicas o Aurabigcombined pela propria aura da transformaçao q vc
	// ta virando, entao se eu for virar ssj pela primeira eu vou usar a o proprio icone da carga dele
	// na cinematica"*.
	//
	// Isto era um `AnimatedSprite2D` cru carregando a `Aurabigcombined.tres` -- um clarao de 32x64 com
	// arte propria, que este node nao tingia ("ESTA NAO SE PINTA"). Virou um <see cref="SpriteDeAura"/>,
	// e nao um sprite parecido, porque a folha da forma traz DOIS problemas que aquela classe ja
	// resolveu e que uma copia teria que resolver de novo:
	//
	//   * a ANCORA sai da altura do quadro (`SpriteDeAura.AncoraPara`) -- a `colorablebigaura` e 96x96
	//     contra os 32x64 da antiga, e um offset cravado poria a chama meio corpo fora do lugar;
	//   * a `AuraSSjBig` da escada Saiyajin JA VEM DOURADA e nao se tinge (`SpriteDeAura.SemTinta` ->
	//     uniform `tingir`); mandar cor pra ela pelo caminho normal escurece o dourado.
	//
	// E O TERCEIRO DESENHO DA MESMA ARTE, dito com todas as letras: o node `Aura` e a `CargaVisual` sao
	// os outros dois. Ele nao briga com eles porque nao e do CORPO -- nasce e morre com a cena, tem
	// pivo proprio (cresce) e forca propria (aparece e some). Quem manda no corpo continua sendo um so.
	// ==============================================================================================
	private SpriteDeAura _auraGrande = null!;

	/// <summary>A chama desta cena. Pra bancada -- ela NAO e a do node `Aura` nem a da `CargaVisual`.</summary>
	public SpriteDeAura ChamaDaCenaDeTeste => _auraGrande;

	/// <summary>
	/// COM QUE CARA A CHAMA DA CENA ESTA AGORA -- a cor e a densidade do degrau que o corpo veste
	/// neste instante.
	///
	/// GUARDADO PORQUE A FORCA E TAMBEM O ALFA: o `Aura.gdshader` termina em `c.a * forca`, entao o
	/// aparecer/sumir da cena multiplica esta densidade quadro a quadro (ver `_Process`). Sem o valor
	/// de repouso guardado, a cena teria que reconstrui-lo da forma toda vez -- e "reconstruir pra
	/// desfazer" e exatamente o erro que os buffs deste projeto ja pagaram.
	/// </summary>
	private (Color Cor, float Forca) _chamaDaCena;

	/// <summary>
	/// A CHAMA DA CENA VESTE O MESMO DEGRAU QUE O CORPO -- folha, cor e densidade.
	///
	/// ============================ ELA TEM QUE SEGUIR O DEGRAU, E NAO A FORMA ALVO ============================
	/// A cena do SSJ3 veste tres degraus antes do dela (`Efeito.VesteDegrau`: base, SSJ1, SSJ2). Presa
	/// na forma ALVO, a chama mostraria a aura do SSJ3 durante os dois minutos em que o corpo ainda e
	/// SSJ1 -- e "a cena mostra um personagem que nao e o que esta na tela" e a familia de defeito que
	/// o <see cref="Vestir"/> existe pra fechar. Por isso quem chama isto e o proprio `Vestir`.
	///
	/// A FOLHA SAI DO CORE (`Catalogo.Folha`, derivada da LINHA da forma) e a cor/densidade da
	/// <see cref="Aura.CorDaChamaDe"/> -- as mesmas duas contas que o corpo usa. Uma chama da cena que
	/// calculasse a propria cor sairia de um tom no meio da cinematica e de outro no fim dela.
	/// ====================================================================================================
	/// </summary>
	private void ChamaDoDegrau(FormaDef? d)
	{
		// ============================ A CENA SEM FORMA TEM CHAMA PROPRIA, E E A UNICA ============================
		// A furia nao veste ninguem, entao nao ha `FormaDef.Aura` de onde tirar a cor -- e o DM escreve
		// o hexa na mao (`Murder.dm:146-150`: `Aurabigcombined.dmi` tingida de `#ff2a2a`, `plane = 7`).
		// Este `if` e a unica excecao viva a regra "a chama e a aura da forma"; ver `Cinematicas.CorDaFuria`.
		//
		// A FOLHA E A BASE (`Catalogo.Folha(null)` ja devolve `FolhaDeAura.Base`), que e a
		// `colorablebigaura` -- a folha COLORIVEL, que e o que um vermelho escrito precisa. A `AuraSSjBig`
		// da escada Saiyajin nao se tinge (ver `SpriteDeAura.SemTinta`) e sairia dourada.
		//
		// FORCA 1: a mesma que `Aura.ForcaDaChamaDe` da pra quem nao esta transformado. A furia nao tem
		// `Intensidade` porque nao tem entrada no catalogo, e inventar um numero aqui seria a chama da
		// raiva ser mais densa (ou menos) que a de qualquer forma, sem nada que justificasse.
		if (d == null)
		{
			_auraGrande.DefinirFolha(Jandirus.Core.Forms.FolhaDeAura.Base);
			_chamaDaCena = (new Color(Cinematicas.CorDaFuria), 1f);
			return;
		}

		// A FOLHA ANTES DA COR, a mesma ordem do resto do jogo: trocar a folha REMONTA o sprite (a
		// ancora depende da altura do quadro), e remontar repinta com o que estiver guardado.
		//
		// ============================ E A CENA DIZ EXPLICITAMENTE O QUE VESTE QUANDO NAO HA FOLHA ============================
		// Este e o TERCEIRO desenhista da chama, e o unico dos tres que nao pode ficar mudo. Os outros
		// dois (`Aura` e `CargaVisual`) desenham a aura PERSISTENTE, e em Ultra Instinto ela e a nuvem --
		// entao calar os dois e o conserto. Aqui e outra coisa: e a coluna de luz do beat
		// `Efeito.AuraGrande`, que a propria cena NARRA (*"uma coluna de luz azul-prateada engole
		// tudo"*, `Cinematicas.UiSign`). Deixar o nulo passar apagaria um beat escrito.
		//
		// E a nuvem NAO cobre esse buraco: ela so acende no `Assumir`, 22 s depois -- o beat da coluna
		// cai aos 12 s, com o corpo ainda vestindo o degrau anterior.
		//
		// A ESCOLHA E ESCRITA, e nao herdada de um `_ =>`: era exatamente um fallback calado
		// (`FolhaDeAura.Base` por omissao) que punha a chama de todo mundo por cima do Ultra Instinto, e
		// que esta tarefa deletou. Aqui a folha de todo mundo e o que se QUER -- tingida com a cor da
		// forma, que na linha do Ultra Instinto e o azul-prateado que a narracao promete.
		// ==============================================================================================================
		var f = Jandirus.Core.Forms.Catalogo.Folha(d);
		_auraGrande.DefinirFolha(SpriteDeAura.CaminhoDa(f) is null
									 ? Jandirus.Core.Forms.FolhaDeAura.Base : f);
		_chamaDaCena = (Aura.CorDaChamaDe(d, CorPessoalDoAlvo), Aura.ForcaDaChamaDe(d));
	}

	/// <summary>
	/// A COR DA CHAMA PESSOAL DE QUEM ESTA NESTA CENA. Ver <see cref="Aura.CorPessoal"/>.
	///
	/// ============================ E POR ISSO QUE ELA TEM QUE SAIR DO CORPO ============================
	/// Os beats desta cena vestem a BASE de verdade (o primeiro degrau da escada do SSJ3), e a base e
	/// pessoal desde sempre. Enquanto a chama pessoal era uma constante compartilhada isso nao tinha
	/// consequencia; com uma cor por personagem, ler a constante aqui poria a chama de OUTRA pessoa na
	/// coluna de luz -- ao lado do corpo, que estaria com a cor certa, no mesmo quadro.
	///
	/// PELO NODE `Aura` DO PROPRIO ALVO, e nao por um campo copiado no comeco da cena: uma copia so
	/// poderia divergir. O fallback e o mesmo dos outros dois leitores (`CargaVisual.Pintar`,
	/// `World.VestirCorpoInteiro`) -- corpo montado pela metade, que na cinematica nem chega a existir.
	/// ==========================================================================================
	/// </summary>
	private Color CorPessoalDoAlvo =>
		_alvo != null && IsInstanceValid(_alvo) && _alvo.GetNodeOrNull<Aura>("Aura") is { } a
			? a.CorPessoal : Aura.CorDoKiCru;

	/// <summary>
	/// ============================ A AURA CRESCE A PARTIR DOS PES ============================
	/// O `Scale` do Godot multiplica o `Offset` junto. Com o sprite escalando em torno do proprio
	/// centro -- que fica na altura do PEITO --, a base descia enquanto a chama crescia:
	///
	///     s=1,00 -> base +16,0 (nos pes)      s=1,24 -> base +19,8
	///     s=1,12 -> base +17,9                s=1,36 -> base +21,8  (~6 px enterrada)
	///
	/// Ou seja a aura AFUNDAVA no chao durante a cinematica -- "aura baixa" de novo, e por um
	/// motivo diferente do offset. Quem escala agora e este PIVO, que mora na linha dos pes; o
	/// sprite pendura nele com a base na origem. Escalar em torno dos pes e o que uma chama faz.
	/// ====================================================================================
	/// </summary>
	private Node2D _auraPivo = null!;

	/// <summary>
	/// ONDE AS PEDRAS MORAM. `TopLevel` NAO E ENFEITE.
	///
	/// Este node inteiro persegue o corpo (`GlobalPosition = _alvo.GlobalPosition`, todo quadro no
	/// `_Process`). Pedra pendurada nele ANDARIA JUNTO com o personagem -- e pedra que acompanha o
	/// dono nao esta saindo do chao, esta grudada nele. `TopLevel` corta a heranca de transformada:
	/// as posicoes dos filhos passam a ser as do mundo, e a pedra fica no tile onde nasceu.
	///
	/// Continua sendo FILHO daqui de proposito -- o `QueueFree` da cena leva as pedras junto, e essa
	/// e a limpeza que fecha qualquer caminho de saida (o teto, o alvo invalido, a troca de zona).
	/// </summary>
	private Node2D _pedrasRaiz = null!;

	/// <summary>
	/// ONDE O ANEL E O CASCALHO MORAM. `TopLevel` pelo mesmo motivo do <see cref="_pedrasRaiz"/> --
	/// ver o comentario no `_Ready`.
	/// </summary>
	private Node2D _chaoRaiz = null!;

	/// <summary>
	/// O CORPO DO JOGADOR ESTA PRESO POR UMA CINEMATICA AGORA? O `move = 0` do DM.
	///
	/// Estatico porque quem pergunta e o <see cref="LocalPlayer"/>, que nao conhece a cena -- e
	/// porque so UMA cinematica prende por vez (a sua). Um campo por instancia obrigaria o
	/// LocalPlayer a procurar o node na arvore todo quadro.
	///
	/// SO A CENA DO PROPRIO JOGADOR prende: assistir alguem transformar do lado nao te paralisa.
	/// </summary>
	/// <summary>
	/// ============================ TRANCA COM DOIS DONOS PRECISA CONTAR ============================
	/// Isto era um `bool`. Com UMA cinematica na tela funcionava; com duas, o `false` da primeira a
	/// terminar apagava a tranca da que ainda estava rodando.
	///
	/// O dono descreveu o sintoma exato: "quando executa todas as transformaçoes de uma vez, o jogo
	/// buga e ele tenta me deixar andar mas fico preso". As duas trancas (esta, do input, e a da
	/// POSE, no `CharacterVisual`) sao apagadas em momentos diferentes, e ai uma solta e a outra
	/// nao -- o input responde e o corpo nao acompanha.
	///
	/// Pior: `QueueFree()` e ADIADO. Uma cena que acaba e outra que comeca no mesmo quadro rodam na
	/// ordem "cena 2 tranca" -> "`_ExitTree` da cena 1 destranca", e a cena 2 nasce destrancada.
	///
	/// Com contador nao ha ordem errada: a tranca cai quando o ULTIMO dono solta.
	/// ====================================================================================
	/// </summary>
	public static bool PrendendoOCorpo => _presos > 0;

	/// <summary>
	/// ============================ A REDE: NINGUEM FICA PRESO PRA SEMPRE ============================
	/// O dono: "ao me transformar ao apertar C duas vezes eu fico preso pra sempre e n consigo
	/// andar". Travar o jogador e o pior defeito que este sistema pode ter -- pior que qualquer
	/// efeito errado, porque nao ha o que fazer a nao ser fechar o jogo.
	///
	/// O contador tem tres caminhos de saida (o prazo, o `_ExitTree` e o alvo invalido) e cada cena
	/// devolve a vez uma vez so (`_contado`). Ainda assim, ficar preso E POSSIVEL: basta um caminho
	/// novo aparecer, ou duas cenas nascerem e uma morrer de um jeito que ninguem previu.
	///
	/// Entao aqui esta a rede, e ela e cega de proposito: se ninguem SOLTAR dentro do prazo da cena
	/// mais longa do jogo com folga, o contador zera na marra. Uma tranca que so abre quando tudo
	/// da certo nao e tranca, e um risco.
	///
	/// ============================ E A "CENA MAIS LONGA" E PERGUNTADA, NAO CHUTADA ============================
	/// Isto era `45.0`, com este mesmo comentario dizendo "o prazo da cena mais longa do jogo com
	/// folga". Batia enquanto a cena mais longa tinha 35 s. Quando o SSJ3 voltou aos 140 s do DM
	/// (ver `Cinematicas`), o literal virou o oposto do que ele existe pra ser: uma rede que corta a
	/// cena legitima no meio e ainda escreve no log que alguem vazou.
	///
	/// A folga e DOBRADA e nao somada porque ela cobre uma cena travada, e nao um atraso: uma cena de
	/// 2 s presa merece a mesma paciencia proporcional que uma de 116.
	/// ====================================================================================================
	///
	/// PUBLICO por causa da bancada: ela e OBRIGADA a fazer a rede disparar, e o unico jeito de fazer
	/// isso sem recopiar o numero e perguntando por ele. Um teste que escreve "60" a mao passa a
	/// aprovar ou reprovar por um motivo diferente do que ele mede no dia em que este prazo mudar --
	/// e ele acabou de mudar.
	public static readonly double PrazoMaximoPreso = Cinematicas.CenaMaisLonga * 2.0;

	private static double _presoDesde;

	/// <summary>Chamado por quadro pelo corpo local. Ver `PrazoMaximoPreso`.</summary>
	public static void VigiarTranca(double delta)
	{
		if (_presos <= 0) { _presoDesde = 0; return; }
		_presoDesde += delta;
		if (_presoDesde < PrazoMaximoPreso) return;

		GD.PushError($"[transformacao] a tranca ficou {_presoDesde:0}s sem soltar ({_presos} dono(s)) "
				   + "-- soltando na marra. Ha um caminho de saida que nao devolveu a vez.");
		_presos = 0;
		_presoDesde = 0;
	}

	/// <summary>Segundos que o corpo esta preso agora. Pra bancada.</summary>
	public static double PresoHaDeTeste => _presoDesde;

	private static int _tetos;

	/// <summary>
	/// QUANTAS VEZES O TETO JA DISPAROU nesta execucao -- ver o bloco do teto no <see cref="_Process"/>.
	///
	/// ============================ O AVISO NAO E MEDIVEL, O CONTADOR E ============================
	/// O teto grita com `GD.PushWarning`, e um aviso e prosa: ele vai pro log da engine, ninguem dentro
	/// do jogo o le, e a bancada nao tem como se perguntar quantos saíram. Foi exatamente esse ponto
	/// cego que deixou setenta e um avisos falsos correrem rodada apos rodada com o placar todo VERDE --
	/// a unica pessoa capaz de contá-los era quem abrisse o log e reparasse neles.
	///
	/// Isto e um contador e nao um derivado de proposito: "quantas vezes ISTO ACONTECEU" nao esta escrito
	/// em lugar nenhum do estado, porque a cena que dispara o teto MORRE logo em seguida e leva o
	/// `FimDaCena.Teto` dela junto. Perguntar as cenas vivas responderia sempre zero.
	///
	/// E ele NAO ZERA. A bancada tira uma linha de base antes de cada trecho (como ja faz com o
	/// `PresosDeTeste`) e mede a diferenca; um `Zerar()` publico seria uma segunda maneira de mexer no
	/// numero, e a primeira bancada que esquecesse de chamá-lo mediria o trecho do vizinho.
	/// ========================================================================================
	/// </summary>
	public static int TetosDeTeste => _tetos;

	private static int _presos;

	/// <summary>
	/// Esta cena ja devolveu a sua vez? Sem isto, os TRES caminhos de saida (o prazo, o
	/// `_ExitTree` e o alvo invalido) decrementariam o mesmo dono ate tres vezes, e o contador
	/// ficaria negativo -- que e a mesma coisa que nao ter tranca.
	/// </summary>
	private bool _contado;

	private void Prender()
	{
		if (_contado) return;
		_contado = true;
		if (_souEu) _presos++;
	}

	private void Devolver()
	{
		if (!_contado) return;
		_contado = false;
		if (_souEu) _presos = Mathf.Max(0, _presos - 1);
	}

	/// <summary>Quantas cenas seguram o corpo agora. Pra bancada.</summary>
	public static int PresosDeTeste => _presos;

	/// <summary>
	/// ============================ A SAIDA DE EMERGENCIA ============================
	/// O contador e `static`: ele sobrevive a troca de zona, a morte, ao respawn e ao fim da cena.
	/// Isso o torna forte -- e o torna perigoso, porque UM vazamento trava o jogador pra sempre.
	/// Foi o que o dono viu: "n consigo mais andar no jogo, mesmo sem me transformar, eu tento
	/// andar mas fico batendo em algo invisivel".
	///
	/// Nao ha desculpa pra esse estado existir. Quem chama e o `LocalPlayer`, quando percebe que
	/// esta preso ha tempo demais pra ser cinematica nenhuma -- a cena mais longa do jogo prende
	/// 32 s. Se isto disparar e defeito, e por isso avisa no log.
	/// ==========================================================================
	/// </summary>
	public static void DestravarTudo(string motivo)
	{
		if (_presos <= 0) return;
		GD.PushWarning($"[transformacao] DESTRAVANDO na marra ({_presos} preso(s)): {motivo}");
		_presos = 0;
	}

	/// <summary>
	/// Onde a base da AURA GRANDE cai, no espaco do personagem. Pra bancada: tem que dar o mesmo
	/// numero da aura persistente, senao a mesma arte esta desenhada em duas alturas.
	/// </summary>
	public float BaseDaAuraGrandeDeTeste =>
		// A BASE DENTRO DO DESENHO E PERGUNTADA A ELE (`SpriteDeAura.BaseDeTeste`), e nao remontada
		// aqui a partir do `Offset` e da altura do quadro: a conta da ancora mudou de dono quando a
		// chama da cena virou uma `SpriteDeAura`, e uma copia da conta mediria a copia.
		//
		// O `* Scale.Y` NAO E DECORACAO. A primeira versao desta sonda lia so a base do sprite e por
		// isso passava em t=0 e NUNCA reprovava o afundamento -- uma checagem que so mede o instante
		// em que nada aconteceu ainda.
		float.IsNaN(_auraGrande.BaseDeTeste)
			? float.NaN
			: _auraPivo.Position.Y
			  + (_auraGrande.Position.Y + _auraGrande.BaseDeTeste) * _auraPivo.Scale.Y;

	/// <summary>Escala a aura grande na marra, pra a bancada medir a base COM ela crescida.</summary>
	public void EscalarAuraDeTeste(float s) => _auraPivo.Scale = Vector2.One * s;

	/// <summary>Quantos beats ja dispararam. Pra bancada -- ver `--diagcine`.</summary>
	public int BeatsDeTeste { get; private set; }

	/// <summary>
	/// COMO ESTA CENA TERMINOU -- e ele e o GUARDA do <see cref="_Process"/>, nao so uma sonda.
	///
	/// ============================ "ACABOU" E "ACABOU BEM" NAO SAO A MESMA PERGUNTA ============================
	/// Isto era um `bool AcabouDeTeste` escrito so no fim NORMAL. Com um bit so, "a cena chegou ao fim"
	/// e "a rede a soltou na marra" ficavam indistinguiveis de fora -- e quem so sabe perguntar se o
	/// CORPO foi solto da verde justamente no caso que o teto existe pra denunciar.
	/// ====================================================================================================
	/// </summary>
	public enum FimDaCena : byte
	{
		/// <summary>Ainda rodando.</summary>
		Rodando = 0,

		/// <summary>Chegou ao fim do roteiro e se liberou. O UNICO fim que nao e defeito.</summary>
		Sozinha = 1,

		/// <summary>A rede a soltou na marra. Se isto aparecer, e DEFEITO -- ver <see cref="FolgaDoTeto"/>.</summary>
		Teto = 2,

		/// <summary>O corpo sumiu no meio (morreu, mudou de zona, deslogou).</summary>
		AlvoSumiu = 3,
	}

	private FimDaCena _fim;

	/// <summary>Como a cena terminou. Pra bancada -- ver <see cref="FimDaCena"/>.</summary>
	public FimDaCena FimDeTeste => _fim;

	/// <summary>
	/// A cena acabou SOZINHA? Pra bancada. DERIVADO do <see cref="_fim"/> e nao um segundo campo: com
	/// os dois lado a lado, um fim novo teria que lembrar de escrever nos dois, e o esquecido daria a
	/// resposta velha calado.
	/// </summary>
	public bool AcabouDeTeste => _fim == FimDaCena.Sozinha;

	/// <summary>Quanto tempo de cena ja correu. Pra bancada saber se o `_Process` esta rodando.</summary>
	public double TempoDeTeste => _t;

	/// <summary>
	/// QUAL ROTEIRO ESTA CENA ESTA TOCANDO. Pra bancada, e ela e a pergunta central dos tres degraus:
	/// "nasceu uma cena" nao distingue a ESTREIA da ENCURTADA -- as duas sao um node com o mesmo tipo,
	/// o mesmo nome e o mesmo dono da tranca. O que as distingue e o objeto que elas estao tocando, e
	/// so quem o segura sabe dizer.
	/// </summary>
	public Cinematica CenaDeTeste => _cena;

	/// <summary>
	/// TOCA A CENA. `souEu` liga o que e do CORPO de quem virou: a tranca do movimento, as falas em
	/// segunda pessoa e o contorno que o Ki manda (ver `Vestir`).
	///
	/// ============================ O QUE `souEu` NAO DECIDE MAIS ============================
	/// Esta linha dizia "`souEu` liga o que e do dono da tela (musica e tremor de camera)", e ela
	/// estava errada nas duas metades:
	///
	///   * a MUSICA nunca passou por `souEu` -- o `_Ready` a poe no ar pra qualquer tela que receba
	///     a cena, e isso ja e o planeta inteiro (o servidor anuncia a forma pra `ZoneList`, e num
	///     planeta nao ha corte de interesse dentro da zona);
	///   * o TREMOR passava, e agora nao passa: quem manda e a DISTANCIA (ver
	///     <see cref="PesoDoTremor"/>).
	///
	/// Um comentario que descreve uma regra que o codigo nao tem e pior que nenhum: foi por ele que
	/// se concluiu que a musica era so de quem virava.
	/// ==================================================================================
	///
	/// A cena e do CLIENTE: ela nao trava o servidor nem o mundo. O que ela prende e o corpo, e so
	/// pelo tempo de <see cref="Cinematica.SegundosPreso"/> -- ver o comentario la sobre por que
	/// ele e menor que a cena.
	/// </summary>
	/// <param name="nome">
	/// COMO SE CHAMA QUEM ESTA VIRANDO. So pra escrever a linha do chat de quem ASSISTE ("Zx:
	/// AINDA MAIS ALEM!"); o balao sobre a cabeca nao precisa dele, porque ele ja mora no corpo.
	/// Vazio (o padrao das bancadas) sai da linha do chat alheio e nao muda mais nada.
	/// </param>
	/// <param name="forma">
	/// A forma que esta cena conta -- **nulo quando ela nao conta forma nenhuma** (a cena da furia).
	/// Ver o campo <see cref="_forma"/>: o nulo e um estado, e nao um esquecimento.
	/// </param>
	public static Transformacao Rodar(Node pai, Node2D alvo, FormaDef? forma, Cinematica cena,
									  bool souEu, string nome = "")
	{
		var t = new Transformacao
		{
			Name = "Cinematica",
			_alvo = alvo,
			_cena = cena,
			_forma = forma,
			// SEM FORMA A CENA E VERMELHA, e as duas cores caem no mesmo lugar: o vermelho da furia
			// (`Murder.dm:149`). `CorDosRaios(null)` ja devolvia um neutro proprio, mas ele existe pra
			// "forma desconhecida" e nao pra "cena sem forma" -- e nenhuma cena de furia acende raio.
			_corAura = new Color(forma?.Aura ?? Cinematicas.CorDaFuria),
			_corRaios = new Color(forma == null
									  ? Cinematicas.CorDaFuria
									  : Jandirus.Core.Forms.Catalogo.CorDosRaios(forma)),
			_souEu = souEu,
			_nome = nome,
		};
		pai.AddChild(t);
		return t;
	}

	public override void _Ready()
	{
		ZIndex = 90;
		ZAsRelative = false;

		// A CHAMA DA CENA. Ver o campo `_auraGrande`: e a aura da PROPRIA forma, desenhada pela mesma
		// classe que a do corpo -- entao a ancora e a tinta ja vem resolvidas.
		//
		// ATRAS DO CORPO, e essa linha e a que importa. `ZAsRelative = false` com `ZIndex = -1` a
		// poe abaixo de tudo do corpo; se ela ficasse na frente (que e o que acontece com um
		// ZIndex relativo, herdando o 90 deste node), ela taparia o personagem.
		_auraPivo = new Node2D { Name = "AuraPivo", Position = new Vector2(0, SpriteDeAura.LinhaDosPes) };
		AddChild(_auraPivo);

		// ============================ O DESCONTO DA LINHA DOS PES, E POR QUE ELE EXISTE ============================
		// A `SpriteDeAura` ancora sozinha pela ALTURA DO QUADRO, e a conta dela poe a base da chama na
		// `LinhaDosPes` do CORPO (ver `AncoraPara`). Aqui o pai ja esta nessa linha -- o pivo mora nela
		// pra que crescer nao afunde a chama --, entao a ancora seria aplicada duas vezes e a chama
		// nasceria um corpo inteiro abaixo do chao.
		//
		// O desconto e a MESMA constante, e nao um numero solto: mudar a linha dos pes conserta os dois
		// lados ao mesmo tempo, que e o unico jeito de eles nao divergirem.
		// ====================================================================================================
		_auraGrande = new SpriteDeAura
		{
			Name = "ChamaDaCena",
			Position = new Vector2(0, -SpriteDeAura.LinhaDosPes),
		};
		_auraPivo.AddChild(_auraGrande);

		// ============================ O Z **DEPOIS** DO `AddChild`, E ISTO E ARMADILHA ============================
		// `SpriteDeAura._Ready` escreve `ZIndex = 0` -- de proposito, porque no CORPO quem poe a aura atras
		// do personagem e a ordem de irmao e nao o z (ver o comentario la). E `_Ready` roda DENTRO do
		// `AddChild`: escrever o z no inicializador do objeto seria escreve-lo antes, e ele seria zerado
		// meio microssegundo depois -- calado, com a chama indo desenhar no balde z 0, na frente do corpo.
		//
		// Aqui a ordem de irmao nao serve porque a chama nao e irma de ninguem do corpo: ela mora dentro da
		// cena, que e `ZIndex = 90` ABSOLUTO. So o z absoluto a poe atras.
		// ====================================================================================================
		_auraGrande.ZIndex = -1;
		_auraGrande.ZAsRelative = false;

		// ============================ NASCE MONTADA E INVISIVEL ============================
		// `Definir(true, ...)` com FORCA ZERO, e nao `Definir(false, ...)`: a `SpriteDeAura` so monta o
		// sprite quando acende (ver `Montar`), e sem sprite montado nao ha quadro pra medir -- a base
		// da chama viraria `NaN` e a bancada da ancora nao teria o que perguntar antes do primeiro beat.
		// Com forca zero o shader zera o alfa (`c.a * forca`), entao ela esta la, animando, e nao aparece.
		//
		// A FOLHA E A COR SAO DA FORMA ALVO -- e o que o dono pediu ("o proprio icone da carga dele").
		// Numa cena que veste degraus, o primeiro `Efeito.VesteDegrau` reescreve as duas (ver `Vestir`).
		// ==============================================================================
		ChamaDoDegrau(_forma);
		_auraGrande.Definir(true, _chamaDaCena.Cor, 0f);

		_pedrasRaiz = new Node2D { Name = "Pedras", TopLevel = true };
		AddChild(_pedrasRaiz);

		// ============================ E O CHAO, PELO MESMO MOTIVO DAS PEDRAS ============================
		// O anel de choque e o cascalho sao coisas que acontecem NUM LUGAR: o chao se abriu ali. Se eles
		// pendurassem neste node -- que persegue o corpo todo quadro (`GlobalPosition = _alvo...`) --, o
		// buraco andaria junto com quem o abriu, e um anel que acompanha o dono nao esta saindo do chao.
		//
		// FILHO DAQUI mesmo assim, e tambem pela mesma razao: o `QueueFree` da cena leva tudo junto, e e
		// essa limpeza que fecha os caminhos de saida (o teto, o alvo invalido, a troca de zona).
		// ==========================================================================================
		_chaoRaiz = new Node2D { Name = "Chao", TopLevel = true };
		AddChild(_chaoRaiz);

		// ============================ EXISTIR NA PASTA NAO E ESTAR IMPORTADO ============================
		// `ResourceLoader.Exists` responde a pergunta certa: o Godot RESOLVE este caminho? Um `.tres`
		// solto na pasta sem o `.png` importado (sem `.import`, sem `.ctex` em `.godot/imported/`)
		// carrega nulo e o efeito some CALADO -- foi exatamente o que aconteceu com os sons que
		// estavam no disco e o editor nunca tinha importado.
		//
		// Nulo aqui vira aviso no console e cena SEM pedras (nao derruba nada), e vira REPROVA na
		// bancada -- ver a checagem deste mesmo caminho em `--diagforma`.
		// ==========================================================================================
		if (ResourceLoader.Exists(CaminhoDasPedras))
			_framesDaPedra = ResourceLoader.Load<SpriteFrames>(CaminhoDasPedras);
		else
			GD.PushWarning($"[cena] `{CaminhoDasPedras}` nao resolve -- o Godot nao importou a folha. "
						 + "A cinematica vai rodar sem as pedras subindo.");

		// DEPOIS DA FOLHA E DEPOIS DO `_pedrasRaiz`: ele mede a camera, o boneco e o relogio da cena
		// pra saber quanta pedra cabe e de quanto em quanto tempo ela nasce. Uma vez -- a area nao pode
		// mudar no meio da cena, senao o `_ocupadas` passaria a guardar celulas de outra grade.
		MontarOChaoSolto();

		// PELA MESMA RAZAO E NO MESMO LUGAR: ela congela ONDE a cena esta acontecendo antes de o corpo
		// poder sair de la. Ver `MontarATempestade` -- em 33 das 34 cenas ela nao faz nada.
		MontarATempestade();

		// ============================ CENA QUE NAO PRENDE NAO TRANCA NADA ============================
		// `SegundosPreso == 0` e a cena da FURIA, e o zero e do proprio DM: `AngerCinematic()` abre com
		// `set waitfor = 0` e o comentario do original explica -- *"Non-blocking so it never freezes the
		// player mid-fight"*. E a diferenca de natureza entre as duas cenas: transformar e escolha,
		// enfurecer e coisa que ACONTECE com voce no meio de uma briga (ver `Cinematicas.Furia`).
		//
		// O `if` nao e cosmetico. Sem ele o caminho seria "tranca no `_Ready`, destranca no primeiro
		// `_Process`" -- um quadro de `PrendendoOCorpo` verdadeiro e um quadro de pose travada. Um
		// quadro nao se ve, mas ele existe: o contador e `static` e serve o jogo inteiro, e o pior
		// defeito deste arquivo (o jogador preso pra sempre) mora exatamente no par prender/devolver.
		// Nao entrar nele e mais barato que sair dele.
		//
		// `_soltou` JA VAI VERDADEIRO nesse caso, pra o `_Process` nao chamar `TravarPose(false)` numa
		// pose que ninguem travou.
		//
		// E ELE **NAO** PULA O RESTO DO `_Ready`: a musica, o `_noPlaneta` e a chama continuam valendo.
		// A furia toca o tema do DM (`emit_RageMusic`) e treme a camera como qualquer cena -- o que ela
		// nao faz e tomar o corpo de quem esta lutando.
		// ======================================================================================
		_soltou = _cena.SegundosPreso <= 0;
		if (!_soltou) Prender();

		// ============================ O CORPO SO PARA -- NAO CONGELA ============================
		// O `move = 0; dir = SOUTH` do DM. E "parar" aqui e exatamente o que o corpo ja faz quando
		// o jogador solta a tecla depois de andar: `SetMotion(..., moving: false)`, e o resto do
		// visual se acerta sozinho.
		//
		// A primeira versao CONGELAVA os quadros em 0. Errado por dois motivos, e o dono apontou os
		// dois: o quadro 0 e um quadro qualquer do ciclo (podia ser um passo no meio da passada), e
		// o jogo ja tem o comportamento certo de parada -- reimplementa-lo era inventar um segundo
		// jeito de o corpo ficar parado.
		// ==================================================================================
		// ============================ VALE PRO MEU CORPO TAMBEM -- ESSE ERA O DEFEITO ============================
		// Este bloco tinha um `if (!_souEu)`: a cena trancava a pose de TODO MUNDO menos a de quem
		// estava se transformando. O argumento era "o corpo local reescreve a pose todo quadro,
		// entao quem o segura e a guarda do `LocalPlayer`" -- verdadeiro pra um `SetMotion` (que
		// dura um quadro) e FALSO pra uma tranca (que recusa a reescrita).
		//
		// O que o dono via era exatamente a diferenca entre as duas: "ele comeca na animaçao mas
		// logo dps para" -- os quadros ate a guarda do `LocalPlayer` alcancar.
		//
		// Agora e uma porta so: a tranca vive no `CharacterVisual` e nao interessa quem tenta
		// escrever depois. Pro corpo REMOTO ela ja era necessaria de todo jeito (quem assiste
		// recebe pose por snapshot, e sem tranca via o outro andando a cinematica inteira).
		// ================================================================================================
		//
		// **SO QUEM PRENDE TRAVA A POSE**, e as duas andam juntas por definicao: travar a pose de um
		// corpo que continua andando (a furia) deixaria o boneco deslizando pelo chao em pose parada.
		// Ver o bloco de cima.
		if (!_soltou) _alvo.GetNodeOrNull<CharacterVisual>("Visual")?.TravarPose(true);

		// ONDE ESTA CENA ACONTECEU. Lido aqui e guardado porque o rumor da aura base pergunta por ele
		// TODO QUADRO, e `Espaco.EhPlaneta` varre os pre-feitos -- sessenta varreduras por segundo
		// durante 116 s de SSJ3 pra sempre receber a mesma resposta. A zona nao muda no meio de uma
		// cena: trocar de planeta destroi o `World` e leva a cinematica junto.
		_noPlaneta = Jandirus.Core.World.Espaco.EhPlaneta(GameClient.Instance?.Zone ?? default);

		// ============================ A MUSICA E DE TODO O PLANETA, E JA ERA ============================
		// O dono: *"a musica de transformaçao toca pra todos os jogadores no planeta? pq e pra ser
		// assim"*. Ela toca, e esta linha e o motivo: nao ha `_souEu` nenhum aqui, e o `_Ready` roda
		// em TODA tela que recebeu a cena. Quem define "todas" e o servidor -- o `S2C.Forma` sai pra
		// `ZoneList(pl.Zone.Hash)` inteira (`GameServer.Formas.cs`) e um planeta nao tem corte de
		// interesse dentro da zona (o `Tick` manda o MESMO buffer de snapshot pra zona toda), entao
		// todo mundo ali tem corpo desenhado e recebe a cena. Fica registrado porque o comentario do
		// `Rodar` afirmava o contrario e por anos ninguem tinha por que duvidar dele.
		//
		// O ALCANCE DEIXA DE VALER SOZINHO FORA DE UM PLANETA, e sem precisar de `if`: no espaco o
		// corte de interesse E a chunk, entao quem esta a setores de distancia nao tem o corpo
		// desenhado, o `World.AoMudarForma` desiste no `Corpo(id) == null` e a cena nem nasce. Quem
		// ouve la e quem estaria dentro do `view` de todo jeito.
		//
		// AQUI ELA E MAIS GENEROSA QUE O ORIGINAL, e e decisao do dono: o `emit_TransformMusic`
		// (`BattleMusic.dm:135`) toca pra `view(src)`, ou seja so pra quem esta na tela.
		//
		// COMECA JUNTO COM A CENA E TOCA ATE O FIM DA FAIXA -- o `repetir: false` mais o
		// `AudioDirector.AoTerminarMusica` devolvem o comando pra camada de baixo quando o arquivo
		// acaba, e NADA aqui a corta no fim da cinematica (o DM tambem nao: la a faixa dura os
		// `durationDs` dela, muito alem da cena). A camada `Transformacao` e a mais alta do
		// `AudioDirector`, entao ela abafa a de combate pelo periodo -- que e o `duck_battle_music`
		// do original.
		// =========================================================================================
		//
		// ============================ E A CAMADA SAI DA CENA, NAO DE UM PARAMETRO ============================
		// Cena SEM FORMA e a furia, e o DM poe o tema dela num canal proprio, ABAIXO do de transformacao:
		// `emit_TransformMusic` corta o canal de raiva antes de tocar (*"a transformation always wins"*,
		// `BattleMusic.dm:133`) e `emit_RageMusic` nem comeca se um tema de forma estiver no ar (`:148`).
		//
		// Isso e exatamente `Camada.Raiva < Camada.Transformacao` -- a hierarquia do `AudioDirector` ja
		// faz as duas coisas (a de cima interrompe, a de baixo fica de sobreaviso), e o `duck` do combate
		// e o mesmo `Raiva > Combate`. Nao ha canal novo nem `if` de audio; ha uma pergunta.
		//
		// DERIVADA DE `_forma` E NAO UM CAMPO DA CENA: "que canal esta faixa usa" seria a MESMA
		// informacao que "esta cena tem forma?", escrita duas vezes -- e o dia em que as duas
		// discordassem daria o tema da furia por cima de uma transformacao, calado.
		// ==============================================================================================
		if (_cena.Musica.Length > 0)
			AudioDirector.Instance?.Musica($"res://Assets/Sounds/Music/{_cena.Musica}",
										   _forma == null ? AudioDirector.Camada.Raiva
														  : AudioDirector.Camada.Transformacao,
										   repetir: false);
	}

	// ======================================================================================
	//  AS PEDRAS QUE SOBEM DO CHAO -- `EliteGroundGrind` / `SSj2GroundGrind`
	//  (`Code/Ascension.dm:40` e `:77`) e o `lssj_transform_buildup`
	//  (`Code/Modules/Skills/Buffs/racial/lssjbuff.dm:465`).
	// ======================================================================================
	//
	// ============================ A ARTE DO BYOND, E NAO CACOS DESENHADOS ============================
	// Isto era um `GpuParticles2D` com a textura feita em codigo -- `Image.CreateEmpty(3, 4)` pintada
	// de marrom, 26 retangulos de 3x4 px subindo com gravidade. O dono: "nas cinematicas e pra tirar
	// o efeito de pedras levitando em particulas, ficou mt feio, prefiro usar o proprio rising rocks
	// .png q era usado no byond em tiles aleatorios perto do personagem".
	//
	// E ele esta certo pelo MESMO motivo que a aura grande e sprite e nao shader: no DM isto nunca
	// foi particula. `createDustmisc(T, 2)` poe um `/obj/meff/Rising` -- um icone de 32x32 com quatro
	// quadros (`Code/Modules/CombatMechanics/combat_effects/dusts.dm:203-208`) -- EM CIMA DE UM TURF,
	// e e o turf que da o alinhamento. Os `for(var/turf/T in view(N,src))` do `GroundGrind` sorteiam
	// TILES, nao pixels.
	//
	// O QUE SE PERDE: o espalhamento continuo da particula e a fisica dela (subir e cair). O que se
	// ganha: o desenho de verdade e a grade. Pedra nascendo a meio tile de distancia da grade le como
	// bug -- e era assim que a caixa de emissao de 68x12 px espalhava os cacos.
	// ==========================================================================================

	/// <summary>
	/// A FOLHA DAS PEDRAS. **O `.tres`, e NAO o `.png`** -- mesma regra da aura grande logo acima: o
	/// arquivo e uma FOLHA de 4 quadros de 32x32 (128x32 no total), e um `Sprite2D` com o PNG poria
	/// a folha inteira na tela.
	///
	/// `internal` porque a bancada confere ESTE caminho, e nao uma copia dele: com a string escrita
	/// duas vezes, trocar a arte de lugar deixaria o `--diagforma` provando que um arquivo que o jogo
	/// nao usa mais continua importado -- verde, e medindo nada.
	/// </summary>
	internal const string CaminhoDasPedras = "res://Assets/Sprites/Decor/Rising Rocks.tres";

	// ======================================================================================
	//  O CHAO SOLTO -- e ele e um ESTADO DA CENA, nao um acontecimento dela
	// ======================================================================================
	// ============================ O QUE O DONO PEDIU, E O QUE ELE TINHA ============================
	//   * *"deveria ter mais `rising rocks.png` q ficariam do INICIO AO FIM em todas as
	//     transformacoes"*;
	//   * *"aumente a area q o jogo pode spawnar esse efeito de rising rock, pq ta mt perto do
	//     personagem e dura mt pouco"*.
	//
	// MEDIDO ANTES DE MEXER: a pedra era uma leva de 10 disparada pelo bit `Efeito.PedrasSubindo`,
	// num retangulo de 3x2 tiles, viva 2,4 ou 3,6 s. Das 32 cenas, ONZE nao levantavam nenhuma; a do
	// SSJ1 tinha pedra em 13,1% do tempo e a melhor de todas em 46,5%.
	//
	// ============================ E A CULPA ERA DA FORMA, NAO DOS NUMEROS ============================
	// Um bit de beat so sabe dizer INSTANTE. "Do inicio ao fim" com beats seria escrever um beat de
	// pedra a cada dois segundos em trinta e duas cenas -- e a versao ENCURTADA multiplica os
	// instantes por `k`, entao a densidade mudaria sozinha entre as duas versoes da MESMA cena.
	//
	// Entao a pedra virou o que o <see cref="Cinematicas.PiscaCabelo"/> ja tinha virado antes dela:
	// um estado, com o tocador contando o tempo. Nem beat pra escrever, nem beat pra esquecer.
	//
	// ============================ TODO NUMERO DAQUI E DO DM ============================
	// E nao e coincidencia: o original ja fazia isto por fora do roteiro. `SSJCinematic.dm:28-31`
	// varre o `view()` inteiro e, em cada tile, `if(prob(15)) spawn(rand(10,150)) createDustmisc(T,2)`
	// -- 15% do chao, com atraso de 1,0 a 15,0 s --, e cada `/obj/meff/Rising` vive
	// `spawn(rand(100,400))` = 10 a 40 s (`dusts.dm:203-208`). Ou seja: um fundo que ENCHE ao longo
	// da cena e fica. O port tinha copiado o sprite e jogado fora o comportamento.
	// ==============================================================================================

	/// <summary>
	/// QUANTO DO CHAO A VISTA FICA SOLTO -- o `prob(15)` de `SSJCinematic.dm:31` e `SSJ2Cinematic.dm:13`,
	/// literal.
	///
	/// E uma FRACAO e nao um numero de pedras de proposito: quem multiplica e a quantidade de tiles
	/// que a camera mostra (ver <see cref="MontarOChaoSolto"/>), entao a densidade na TELA e a mesma
	/// em qualquer zoom. Um "17 pedras" cravado aqui daria chao vazio no zoom 2 e chao de pedra no 6.
	///
	/// (O SSJ3 do DM usa `prob(20)`, mas sobre `view(24)` -- 2401 tiles, ~480 pedras. Aquilo e a
	/// escala do BYOND, nao a nossa; o 15 e o numero das duas cenas que todo mundo ve.)
	/// </summary>
	internal const double FracaoDoChaoSolto = 0.15;

	/// <summary>
	/// ATE QUANDO A LEVA INICIAL TERMINA DE NASCER -- o teto do `spawn(rand(10,150))` de
	/// `SSJCinematic.dm:31`, que sao 15,0 s.
	///
	/// A pedra NAO nasce toda no primeiro quadro, e isso e do original: la o chao vai se soltando
	/// enquanto o poder sobe. Dezessete pedras aparecendo juntas em `_t = 0` leriam como um piscar.
	///
	/// E ele e um TETO e nao o prazo: quem manda e o <see cref="Cinematica.SegundosPreso"/> (o chao
	/// tem que estar todo solto quando a forma fica, que e quando a cratera cai). O teto so existe
	/// pro SSJ3, cujos 140 s de prazo deixariam a tela quase vazia pelos primeiros dois minutos.
	/// </summary>
	internal const double EnchimentoMaximo = 15.0;

	/// <summary>
	/// QUANTO UMA PEDRA VIVE -- `spawn(rand(100,400))` em `dusts.dm:207`, que sao 10,0 a 40,0 s.
	///
	/// ANTES ERAM 2,4 ou 3,6 s (dois ou tres ciclos da folha), e era daqui que saia o *"dura mt
	/// pouco"* do dono: quatro a onze vezes menos que o original. O encurtamento foi nosso e o
	/// numero certo sempre esteve no DM.
	/// </summary>
	internal const double VidaMinima = 10.0, VidaMaxima = 40.0;

	/// <summary>
	/// O RETANGULO DO SORTEIO, EM TILES (meia-largura e meia-altura) -- e ele e MEDIDO, nao escrito.
	///
	/// ============================ POR QUE ELE DEIXOU DE SER CONSTANTE ============================
	/// Era `3 x 2` cravado, com a justificativa certa e a conta feita a mao: "no zoom padrao cabem ~6
	/// tiles pra cada lado na horizontal e ~3,4 na vertical". O dono reclamou do resultado (*"ta mt
	/// perto do personagem"*), e com razao -- o retangulo era METADE do que a camera mostra.
	///
	/// Refazer a conta a mao daria o mesmo defeito com outro numero. Agora a pergunta e feita a
	/// CAMERA: o sorteio cobre todo tile que ela toca, nem um a mais. O que esta fora da tela e
	/// efeito pago e nao visto; o que esta dentro e o pedido do dono, e os dois lados se resolvem
	/// sozinhos em qualquer zoom, em qualquer janela.
	/// ========================================================================================
	/// </summary>
	private int _alcanceX = 3, _alcanceY = 2;

	/// <summary>
	/// OS TILES CANDIDATOS -- todo o retangulo MENOS o que o corpo desenha por cima.
	///
	/// POR INSTANCIA, e nao mais estatico: os dois numeros que o montam mudam de cena pra cena (a
	/// camera, la em cima; e o TAMANHO DO BONECO, aqui). Uma pedra de 32x32 embaixo de quem esta se
	/// transformando fica escondida pelo sprite -- efeito pago e nao visto, e ainda ocupando uma vaga.
	///
	/// O BURACO SAI DA FOLHA DO CORPO (`CharacterVisual.TamanhoDoQuadro`) e nao de um "1 tile"
	/// escrito: num boneco de 32 ele da exatamente a celula dos pes, que e o que valia antes; num
	/// macaco de 96 ele da 3x3, que e o que sempre devia valer. Cravar 1 aqui seria a mentira que
	/// este arquivo ja pagou caro duas vezes (`AuraObject.dm`, `SpriteDeAura.AncoraPara`).
	/// </summary>
	private Vector2I[] _tilesEmVolta = [];

	/// <summary>Quantas pedras ficam vivas ao mesmo tempo. Ver <see cref="MontarOChaoSolto"/>.</summary>
	private int _alvoDePedras;

	/// <summary>De quanto em quanto tempo nasce uma. Ver <see cref="MontarOChaoSolto"/>.</summary>
	private double _intervaloDePedra = 1.0;

	/// <summary>Quando nasce a proxima. Tempo de cena.</summary>
	private double _proximaPedra;

	/// <summary>
	/// A CELULA DO CHAO QUE SE SOLTA, congelada no comeco da cena.
	///
	/// O chao que quebra e o chao ONDE A TRANSFORMACAO ACONTECEU. Perguntar a posicao do corpo a cada
	/// nascimento parece igual (o corpo esta preso), mas nao e: o `Devolver` solta o jogador no beat
	/// que assume, e a cauda da cena ainda dura ate 2,2 s -- pedra nascendo em volta de quem ja saiu
	/// andando e pedra perseguindo o dono, que e o mesmo defeito que fez o `_pedrasRaiz` ser `TopLevel`.
	/// </summary>
	private Vector2I _celulaDoChao;

	private SpriteFrames? _framesDaPedra;

	/// <summary>
	/// Uma pedra viva: o node, a CELULA que ela ocupa, quando ela APARECE e quando ela morre.
	///
	/// A celula esta aqui porque ela precisa ser DEVOLVIDA na morte -- ver <see cref="_ocupadas"/>.
	/// </summary>
	private readonly record struct PedraViva(AnimatedSprite2D No, Vector2I Celula, double Nasce, double Morre);

	private readonly List<PedraViva> _pedras = [];

	/// <summary>
	/// AS CELULAS QUE JA TEM PEDRA. Uma por tile, e o teto de populacao sai daqui de graca.
	///
	/// Antes o "sem repetir" valia dentro de UMA leva (a sacola de tiles livres do sorteio). Com a
	/// pedra virando estado, as levas acabaram: agora as pedras nascem uma a uma ao longo da cena
	/// inteira, e duas nascidas com trinta segundos de diferenca cairiam no mesmo tile sem que
	/// ninguem visse -- desenhadas uma sobre a outra, o efeito perde uma pedra e paga por duas.
	/// </summary>
	private readonly HashSet<Vector2I> _ocupadas = [];

	/// <summary>
	/// MEDE O CHAO QUE VAI SE SOLTAR -- area, populacao e cadencia. Uma vez, no `_Ready`.
	///
	/// ============================ A AREA E O QUE A CAMERA TOCA ============================
	/// `GetViewportRect().Size / zoom` sao os pixels de MUNDO que cabem na tela; metade disso pra
	/// cada lado, dividido pelo tile, e o retangulo. `CeilToInt` e de proposito: o tile da borda
	/// aparece pela metade, e meia pedra na beirada e melhor que uma faixa de chao intocado exatamente
	/// onde a tela acaba.
	///
	/// SEM CAMERA (bancada headless antes do mundo montar) cai no zoom da configuracao, que e o que o
	/// jogador escolheu -- e nao um "3" escrito aqui, que mentiria pra quem joga em 2 ou em 6.
	///
	/// ============================ A POPULACAO SAI DA AREA, E NAO O CONTRARIO ============================
	/// <see cref="FracaoDoChaoSolto"/> vezes os tiles candidatos. Assim a densidade NA TELA nao muda
	/// com o zoom nem com o tamanho da janela: quem afasta a camera ve mais chao e mais pedra, na
	/// mesma proporcao.
	///
	/// ============================ E A CADENCIA SAI DO RELOGIO DA CENA ============================
	/// A leva enche ate o <see cref="Cinematica.SegundosPreso"/> -- o instante em que a forma fica e a
	/// cratera cai --, porque e ai que o chao tem que estar no seu pior. Com o teto do DM
	/// (<see cref="EnchimentoMaximo"/>) por cima, que e quem impede os 140 s do SSJ3 de virarem dois
	/// minutos de tela quase vazia.
	/// ================================================================================================
	/// </summary>
	private void MontarOChaoSolto()
	{
		const int T = ZoneCollision.TileSize;

		float zoom = GetViewport()?.GetCamera2D() is { } cam && cam.Zoom.X > 0.01f
			? cam.Zoom.X
			: Math.Max(1, Boot.Config.Zoom);
		Vector2 meiaVista = GetViewportRect().Size / (2f * zoom);
		_alcanceX = Math.Max(1, Mathf.CeilToInt(meiaVista.X / T));
		_alcanceY = Math.Max(1, Mathf.CeilToInt(meiaVista.Y / T));

		// A CELULA DOS PES, e nao a do centro do sprite. A origem do corpo fica no meio dele e o
		// `MoveRules.FeetOffsetY` e o desconto que poe a caixa no chao -- o MESMO que o `Plantar`
		// (cratera, fumaca) ja usa. Sem ele, com o corpo perto da borda de cima de um tile, o chao
		// inteiro sai deslocado uma celula pra cima.
		Vector2 pes = _alvo.GlobalPosition + new Vector2(0, MoveRules.FeetOffsetY);
		_celulaDoChao = new Vector2I(Mathf.FloorToInt(pes.X / T), Mathf.FloorToInt(pes.Y / T));

		// ============================ O BURACO DO CORPO, MEDIDO DA FOLHA ============================
		// O boneco desenha um quadrado de `lado` px com a BASE na linha dos pes (e por isso o retangulo
		// sobe de `pes.Y - lado` ate `pes.Y`) -- a mesma ancoragem que o `CharacterVisual` usa pra
		// alinhar o macaco de 96 pelo pe em vez de pelo centro.
		//
		// Um boneco de 32 da exatamente a celula dos pes, que e o que valia antes. O de 96 da 3x3.
		float lado = CharacterVisual.TamanhoDoQuadro(
			_alvo.GetNodeOrNull<CharacterVisual>("Visual")?.FolhaDoCorpo).X;
		if (lado <= 0) lado = T;
		var corpo = new Rect2(pes.X - lado * 0.5f, pes.Y - lado, lado, lado);

		var l = new List<Vector2I>();
		for (int dx = -_alcanceX; dx <= _alcanceX; dx++)
			for (int dy = -_alcanceY; dy <= _alcanceY; dy++)
			{
				// A CELULA DOS PES SAI SEMPRE, mesmo que o retangulo do corpo nao a pegue: com os pes
				// exatamente na borda de duas celulas, `Floor` cai numa e o retangulo cobre a outra.
				if (dx == 0 && dy == 0) continue;
				var centro = new Vector2((_celulaDoChao.X + dx + 0.5f) * T, (_celulaDoChao.Y + dy + 0.5f) * T);
				if (corpo.HasPoint(centro)) continue;
				l.Add(new Vector2I(dx, dy));
			}
		_tilesEmVolta = [.. l];

		_alvoDePedras = Mathf.RoundToInt(_tilesEmVolta.Length * FracaoDoChaoSolto);

		double janela = Math.Min(Math.Max(_cena.SegundosPreso, 0.1), EnchimentoMaximo);
		_intervaloDePedra = janela / Math.Max(1, _alvoDePedras);
		_proximaPedra = 0;
	}

	/// <summary>
	/// FAZ NASCER UMA PEDRA, se houver tile livre e tempo de cena pra ela viver.
	///
	/// O sorteio e de TILE e o desenho e ALINHADO A GRADE: o quadro tem exatamente
	/// <see cref="ZoneCollision.TileSize"/> de lado, entao o centro do sprite no centro da celula faz
	/// a pedra COBRIR o tile -- que e o que o `new/obj/meff/Rising(T)` do BYOND faz de graca, porque
	/// la o obj herda a posicao do turf.
	/// </summary>
	/// <param name="naMarra">
	/// Bancada: ignora o prazo da cena. Ver <see cref="SoltarPedrasDeTeste"/> -- a checagem do macaco
	/// enche o chao DEPOIS de rodar a cena inteira, e sem isto o "o maquinario funciona" mediria o
	/// relogio no fim em vez de medir a folha.
	/// </param>
	/// <returns>Verdadeiro se nasceu.</returns>
	private bool NascerPedra(bool naMarra = false)
	{
		if (_framesDaPedra is not { } frames || frames.GetAnimationNames().Length == 0) return false;
		if (_tilesEmVolta.Length == 0 || _ocupadas.Count >= _tilesEmVolta.Length) return false;

		string anim = frames.GetAnimationNames()[0];
		double ciclo = DuracaoDoCiclo(frames, anim);
		const int T = ZoneCollision.TileSize;

		// ============================ PEDRA QUE NAO CABE NA CENA NAO NASCE ============================
		// Com a vida do DM (10 a 40 s) e cenas de 4,6 a 143 s, quem nao tem nem UM ciclo da folha pela
		// frente nem chega a nascer: uma pedra que aparecesse a meio segundo do fim seria um piscar.
		//
		// Efeito colateral desejado: no ultimo ciclo da cena a populacao para de se repor, e o chao
		// assenta sozinho junto com a poeira da cratera.
		// ==========================================================================================
		double sobra = naMarra ? VidaMaxima : _cena.Segundos - _t;
		if (sobra < ciclo) return false;

		// ============================ A VIDA, E OS DOIS CORTES DELA ============================
		// A DO DM: `spawn(rand(100,400))` = 10,0 a 40,0 s (`dusts.dm:207`). Antes eram 2,4 ou 3,6 s --
		// quatro a onze vezes menos --, e era dai que saia o *"dura mt pouco"* do dono.
		//
		//  1. CICLOS INTEIROS: a pedra some no FIM da animacao e nao no meio dela. O ultimo quadro da
		//     folha e o fim do movimento, e cortar antes le como a pedra sendo apagada.
		//  2. E NUNCA ALEM DA CENA: o `QueueFree` da cena leva as pedras junto (e essa limpeza e o que
		//     fecha os caminhos de saida -- ver `_pedrasRaiz`), entao uma vida maior que o que resta
		//     nao existe, ela so nao seria cumprida.
		//
		// O CORTE 2 VEM DEPOIS DO 1 e engole os ciclos, de proposito: quando ele morde, a pedra morre
		// no MESMO instante em que a cena inteira some da tela -- e ai nao ha meio-movimento pra
		// ninguem ver. Cortar em ciclo inteiro tambem aqui deixaria as cenas curtas (4,6 s) com o
		// ultimo segundo e meio sem uma pedra na tela, que e o contrario do que o dono pediu.
		// ==================================================================================
		double vidaCrua = VidaMinima + GD.Randf() * (VidaMaxima - VidaMinima);
		double vida = Math.Min(Math.Max(1, (int)(vidaCrua / Math.Max(0.01, ciclo))) * ciclo, sobra);

		ZoneCollision? mapa = Mundo()?.Colisao;

		// SORTEIO SEM REPOSICAO, agora com memoria: `_ocupadas` guarda as celulas que ja tem pedra pela
		// cena inteira, e nao so dentro de uma leva. Ver o campo.
		//
		// `GD.Randi` E O SORTEIO DO CLIENTE. Aleatoriedade nao entra no Core -- a cena e do cliente e
		// nao viaja pela rede, entao duas maquinas assistindo a mesma transformacao veem pedras em
		// tiles diferentes, e isso nao e defeito: e a mesma licenca que o `Plantar` ja toma.
		//
		// TENTATIVAS LIMITADAS pelo numero de candidatos: com o chao quase cheio, sortear ate achar um
		// livre seria um laco sem teto no meio do `_Process`.
		for (int tentativa = 0; tentativa < _tilesEmVolta.Length; tentativa++)
		{
			Vector2I off = _tilesEmVolta[(int)(GD.Randi() % (uint)_tilesEmVolta.Length)];
			if (!_ocupadas.Add(off)) continue;

			int tx = _celulaDoChao.X + off.X, ty = _celulaDoChao.Y + off.Y;

			// PEDRA NAO SAI DE DENTRO DE PAREDE. `if(T && !T.density)` -- `lssjbuff.dm:471`. Sem
			// mapa (bancada, zona ainda carregando) nao ha o que checar e a pedra vai assim mesmo.
			//
			// A CELULA FICA MARCADA MESMO ASSIM: ela e um lugar onde nao vai nascer pedra nunca, e
			// devolve-la a sacola faria o sorteio insistir na mesma parede a cena inteira.
			if (mapa != null && mapa.BlockedCell(tx, ty)) return false;

			var p = new AnimatedSprite2D
			{
				SpriteFrames = frames,
				Animation = anim,
				// CENTRO DO SPRITE NO CENTRO DA CELULA -- ver o `+ 0.5f`, que e a mesma conta do
				// `GeradorDeTerreno.PontoDeNascimento`. Alinhar pelo canto poria meia pedra em cada
				// um de dois tiles.
				Position = new Vector2((tx + 0.5f) * T, (ty + 0.5f) * T),
				// ============================ SOBRE O CHAO, SOB OS CORPOS ============================
				// A ordem do BYOND: `OBJ_LAYER` fica ACIMA de `TURF_LAYER` e ABAIXO de `MOB_LAYER`.
				// Aqui: chao das zonas em -2 (ver os `.tscn` de `Assets/Maps`) e decalques em -2 (ver
				// `Decalques._Ready`); os atores em 0. Entao -1 e o degrau do meio, e e o mesmo que a
				// aura grande usa.
				//
				// ABSOLUTO (`ZAsRelative = false`) porque senao a pedra herdaria o `ZIndex = 90` desta
				// cena e desenharia POR CIMA do personagem -- tapar o corpo justo no unico momento em
				// que o jogador para pra olhar pra ele.
				ZIndex = -1,
				ZAsRelative = false,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				// NASCE INVISIVEL: quem a acende e o `TocarPedras`, na hora marcada logo abaixo.
				Visible = false,
			};
			_pedrasRaiz.AddChild(p);

			// UM PINGO DE ATRASO, e ele e o `if(prob(20)) sleep(1)` que o DM tem dentro da varredura
			// (`Ascension.dm:42`). O escalonamento GRANDE nao mora mais aqui -- ele e a cadencia da
			// cena inteira (ver `MontarOChaoSolto`); este resto so tira as pedras do compasso exato do
			// intervalo, que a olho vira ritmo de metronomo.
			double nasce = _t + GD.Randf() * 0.2;

			_pedras.Add(new PedraViva(p, off, nasce, nasce + vida));
			return true;
		}
		return false;
	}

	/// <summary>
	/// QUANTO DURA UMA VOLTA DA FOLHA, LIDO DO `.tres` -- e nao "1,2 s" escrito aqui.
	///
	/// Hoje sao 4 quadros de peso 3 a 10 fps = 1,2 s. Trocar a arte (mais quadros, outra velocidade)
	/// nao pode obrigar ninguem a lembrar deste arquivo: o numero cravado envelheceria calado e as
	/// pedras passariam a sumir no meio da animacao de novo.
	/// </summary>
	private static double DuracaoDoCiclo(SpriteFrames f, string anim)
	{
		double fps = f.GetAnimationSpeed(anim);
		if (fps <= 0) return 1.0;
		double soma = 0;
		for (int i = 0; i < f.GetFrameCount(anim); i++) soma += f.GetFrameDuration(anim, i);
		return soma > 0 ? soma / fps : 1.0;
	}

	/// <summary>
	/// FAZ NASCER, ACENDE E RECOLHE AS PEDRAS. Roda por quadro, junto com o resto da cena.
	///
	/// ============================ O RELOGIO DO CHAO INTEIRO MORA AQUI ============================
	/// Enquanto a cena roda: se falta pedra pro alvo e a hora da proxima chegou, nasce uma. E so.
	/// A mesma linha faz o ENCHIMENTO do comeco (a lista comeca vazia, entao nascem `_alvoDePedras`
	/// seguidas, uma a cada `_intervaloDePedra`) e a REPOSICAO do resto da cena (uma morreu, falta
	/// uma, nasce uma). Nao ha "modo de encher" e "modo de manter" pra sair de sincronia.
	///
	/// SEM `Tween` E SEM `SceneTreeTimer` de proposito: os dois so sabem chamar de volta por LAMBDA,
	/// e lambda nao da pra cancelar quando a cena morre antes da pedra (foi o que custou 19 assinaturas
	/// orfas por ciclo de relog nas telas). Aqui nao ha assinatura nenhuma pra vazar: a lista morre
	/// com o node, e o `QueueFree` da cena leva as pedras que ainda estiverem vivas.
	/// </summary>
	private void TocarPedras()
	{
		// O GATE E DA CENA E VEM DO CORE: `OChaoSeSolta` e falso so na linha do Oozaru, e por pedido
		// do dono (*"oozaru n tem esse efeito de rocks nem de particulas"*). Ver a propriedade la.
		if (_cena.OChaoSeSolta)
			while (_pedras.Count < _alvoDePedras && _t >= _proximaPedra)
			{
				_proximaPedra += _intervaloDePedra;
				if (!NascerPedra()) break;
			}

		for (int i = _pedras.Count - 1; i >= 0; i--)
		{
			PedraViva p = _pedras[i];
			if (!IsInstanceValid(p.No)) { _pedras.RemoveAt(i); _ocupadas.Remove(p.Celula); continue; }
			// A CELULA VOLTA PRA SACOLA na morte: sem isto o chao encheria uma vez e nunca mais se
			// renovaria numa cena longa -- o SSJ3 tem 143 s e a pedra vive no maximo 40.
			if (_t >= p.Morre) { p.No.QueueFree(); _pedras.RemoveAt(i); _ocupadas.Remove(p.Celula); continue; }
			if (!p.No.Visible && _t >= p.Nasce) { p.No.Visible = true; p.No.Play(); }
		}
	}

	/// <summary>Quantas pedras estao vivas agora. Pra bancada.</summary>
	public int PedrasVivasDeTeste => _pedras.Count;

	/// <summary>Quantas deveriam ficar vivas ao mesmo tempo -- o teto de custo da cena. Pra bancada.</summary>
	public int AlvoDePedrasDeTeste => _alvoDePedras;

	/// <summary>Quantos tiles o sorteio alcanca (o teto ABSOLUTO: uma pedra por tile). Pra bancada.</summary>
	public int TilesDePedraDeTeste => _tilesEmVolta.Length;

	/// <summary>A meia-extensao do sorteio, em tiles. Pra bancada conferir que ela veio da camera.</summary>
	public Vector2I AlcanceDePedraDeTeste => new(_alcanceX, _alcanceY);

	/// <summary>Onde as pedras vivas estao, no mundo. Pra bancada medir o alinhamento a grade.</summary>
	public Vector2[] PedrasDeTeste =>
		[.. _pedras.Where(p => IsInstanceValid(p.No)).Select(p => p.No.GlobalPosition)];

	/// <summary>
	/// ENCHE O CHAO NA MARRA, sem esperar a cadencia. Pra bancada.
	///
	/// Ela existe pelo mesmo motivo de sempre: o zero da cena do Oozaru precisa ser uma ESCOLHA
	/// provada, e nao uma folha que nao carregou. Por isso ela pula o gate `OChaoSeSolta` -- e o
	/// unico caminho do arquivo que pula.
	/// </summary>
	public void SoltarPedrasDeTeste()
	{
		for (int i = 0; i < _alvoDePedras && NascerPedra(naMarra: true); i++) { }
	}

	public override void _Process(double delta)
	{
		// ============================ CENA ENCERRADA NAO VOLTA A CONTAR O TEMPO ============================
		// `QueueFree()` e ADIADO: o node so morre no FIM do quadro, e ate la ele continua sendo um objeto
		// valido, dentro da arvore, que qualquer um pode tocar de novo. Todos os tres caminhos de saida
		// daqui de baixo terminam em `QueueFree` + `return` -- e nenhum deles impedia a chamada SEGUINTE.
		//
		// Quem chama de novo e quem bombeia o relogio a mao dentro de um quadro so: a bancada roda a cena
		// inteira com `SetProcess(false)` + `_Process(0.1)` num laco, e a guarda dela (`IsInstanceValid`)
		// nunca vira falsa porque a liberacao ainda nao aconteceu. A cena que ja tinha acabado voltava a
		// somar `_t`, ultrapassava `Segundos + FolgaDoTeto` e o TETO passava a acusar de "presa pra
		// sempre" justamente a cena que terminou na hora certa -- 71 avisos por rodada, todos falsos.
		//
		// A rede continua onde estava e cega como era. O que muda e o que ela julga: cena que ainda esta
		// RODANDO. Uma cena morta nao tem como estar presa.
		// ============================================================================================
		if (_fim != FimDaCena.Rodando) return;

		// O ALVO PODE SUMIR NO MEIO (morreu, mudou de zona, deslogou). Soltar o corpo aqui tambem
		// nao e redundancia: sem isto, uma cena interrompida deixaria o jogador PARALISADO PRA
		// SEMPRE -- o pior defeito que este arquivo pode ter.
		if (!IsInstanceValid(_alvo)) { _fim = FimDaCena.AlvoSumiu; Soltar(); QueueFree(); return; }

		GlobalPosition = _alvo.GlobalPosition;
		_t += delta;

		// SOLTA O CORPO NA HORA MARCADA, e nao no fim da cena. O congelamento sai junto: nao faz
		// sentido o corpo voltar a andar e continuar sem animacao.
		// ============================ TETO QUE DISPARA DE VERDADE ============================
		// O dono: "ao me transformar ao apertar C duas vezes eu fico preso pra sempre e n consigo
		// andar". A soltura depende deste `_Process` chegar no prazo e do node morrer -- e QUALQUER
		// coisa que interrompa isso (a cena nao processar, o node nao ser liberado, um caminho de
		// saida que nao passe pelo `_ExitTree`) deixa o jogador travado PARA SEMPRE, porque hoje
		// TODA tecla passa pelo `SemComando`.
		//
		// "Preso pra sempre" nao pode ser um estado alcancavel. Este teto transforma o pior caso em
		// "preso alguns segundos a mais": passou da duracao da cena com folga, solta na marra e se
		// libera. Se ele disparar, e defeito -- e por isso avisa no log em vez de calar.
		// ================================================================================
		if (_t > _cena.Segundos + FolgaDoTeto)
		{
			// O ROTULO SAI DA CENA e nao da forma: `_cena.Forma` e o id quando ha forma e a string
			// vazia quando nao ha (a furia). Escrever `_forma.Id` aqui explodiria com `NullReference`
			// -- dentro do bloco que existe pra impedir que o jogador fique preso pra sempre.
			GD.PushWarning($"[transformacao] TETO: a cena de `{NomeDaCena()}` passou de "
						 + $"{_cena.Segundos + FolgaDoTeto:0.#}s sem terminar -- soltando na marra.");
			_tetos++;
			_fim = FimDaCena.Teto;
			Soltar();
			QueueFree();
			return;
		}

		if (_t >= _cena.SegundosPreso && !_soltou)
		{
			_soltou = true;
			Devolver();
			_alvo.GetNodeOrNull<CharacterVisual>("Visual")?.TravarPose(false);
		}
		// --- dispara os beats que venceram ---
		while (_proximo < _cena.Beats.Length && _cena.Beats[_proximo].Em <= _t)
			Disparar(_cena.Beats[_proximo++]);

		// DEPOIS dos beats, e nao antes: uma leva solta neste quadro ja pode ter uma pedra com o
		// `Nasce` vencido (o sorteio permite atraso zero), e faze-la esperar o quadro seguinte pra
		// aparecer seria um quadro de nada no comeco do efeito.
		TocarPedras();

		// DEPOIS DOS BEATS TAMBEM, e o motivo aqui e o inverso do das pedras: o beat que ASSUME acende
		// o `ClaraoDeTela`, e um raio caindo no MESMO quadro tem que somar clarao POR CIMA dele em vez
		// de ser lavado por ele. Rodando antes, o unico instante da cena que o dono chama de climax
		// seria o unico em que a tempestade nao aparece.
		TocarTempestade();

		// ============================ O CABELO PISCANDO, ATE A FORMA FICAR ============================
		// DEPOIS dos beats pelo mesmo motivo das pedras: o beat que ARMA o piscar tem que poder trocar o
		// cabelo no proprio quadro em que ele vence, e nao no seguinte.
		//
		// E DEPOIS TAMBEM PORQUE O `Assumir` E UM BEAT: rodando antes, o quadro em que a forma fica
		// poderia acabar com o penteado da piscada por cima do que o `Assumir` acabou de vestir -- meio
		// segundo de base num personagem que ja e Super Saiyajin, no instante mais visto da cena.
		//
		// O SORTEIO E O `rand(3,10)` DO DM (`SSJCinematic.dm:13`), lido do Core -- ver
		// `Cinematicas.PiscadaMinima`.
		// ==========================================================================================
		if (_piscaLigada && _t >= _proximaPiscada
			&& _alvo.GetNodeOrNull<CharacterVisual>("Visual") is { } vp)
		{
			// PISCA ENTRE A FORMA E O QUE ESTAVA VESTIDO, e o segundo lado quase sempre e `null` (a
			// base). Nao e "entre o sufixo e o vazio": `null` no `VestirCabeloDaForma` e "sem forma
			// nenhuma", que desfaz sprite, tinta e rabo de uma vez -- inclusive nas formas que so
			// TINGEM (o SSG), onde piscar so o sufixo nao piscaria nada, porque o sufixo delas e vazio
			// dos dois lados.
			_piscando = !_piscando;
			vp.VestirCabeloDaForma(_piscando ? _forma : _vestido);
			_proximaPiscada = _t + Cinematicas.PiscadaMinima
							+ GD.Randf() * (Cinematicas.PiscadaMaxima - Cinematicas.PiscadaMinima);
		}


		// ============================ O TREMOR CONTINUO -- A CENA INTEIRA ============================
		// O dono: *"vamos fazer as transformacoes causarem camera shake do inicio ao fim sem ficar
		// parando, pq ta estranho ele tremendo e parando, tremendo e parando"*.
		//
		// O `Sacudir` e um IMPULSO: ele levanta `_tremor` e o `World` o derruba a
		// `Cinematicas.QuedaDoTremor` por segundo, ou seja um beat de forca 6 dura 0,75 s. Na cena do
		// SSJ3 sao 22 beats de tremor em 119,5 s -- 16,5 s de camera viva e 103 s de camera PARADA. E
		// exatamente o liga-desliga da queixa, e ele nao se conserta com mais beats: o buraco maior
		// tem 9,2 s (o vao de 10,0 s a 19,2 s), e enche-lo de solavancos seria trocar o entrecortado
		// pela camera sacudindo forte por dois minutos, que o dono tambem recusou.
		//
		// ENTAO O RUMOR VIROU O PISO, e os beats viraram os PICOS por cima dele. Reacender por quadro
		// num valor BAIXO e o que faz o piso: o `LevantarTremor` so SOBE (`if (forca <= _tremor)
		// return`), entao o pico de 6 desce sozinho ate os 1,6 e para ali em vez de morrer no zero --
		// nao ha como o rumor abafar um beat, nem como o beat apagar o rumor.
		//
		// ============================ ISTO ERA `if (_auraBaseAcesa)` ============================
		// So a cena do Oozaru acende a aura base (`Efeito.AuraBase` aparece em UM roteiro), entao o
		// rumor existia em 1 das 34 cenas e as outras 33 eram o liga-desliga inteiro. A condicao foi
		// DELETADA, e nao afrouxada: "a cena esta rodando" ja e a pergunta certa -- o `_Process`
		// inteiro so chega aqui enquanto `_fim == Rodando`, e a saida da cena deixa o `_tremor` cair
		// pelos 1,6/8 = 0,2 s da queda normal, que e o fim macio de que ela precisa.
		//
		// Quem decide a FORCA continua sendo o `PesoDoTremor`, pela distancia -- o dono: *"o camera
		// shake tb afeta todos no planeta"*. Quem esta longe sente o eco, nao o solavanco; quem esta
		// noutro planeta nao sente nada.
		// ==========================================================================================
		float pesoDoRumor = PesoDoTremor();
		if (pesoDoRumor > 0f)
			Mundo()?.Sacudir(Cinematicas.RumorDaCena, peso: pesoDoRumor,
							 queda: Cinematicas.QuedaDoTremor, cadencia: Cinematicas.CadenciaDoTremor);

		// --- a chama da cena cresce e apaga ---
		if (_auraT >= 0)
		{
			_auraT += delta;
			// ============================ ELA DURA A CENA INTEIRA ============================
			// O dono: "a AuraBigcombined pode durar do inicio ao fim da cinematica". Isto era
			// `(1.0 - _auraT / 3.0)`: um decaimento cravado em TRES segundos, sem relacao nenhuma
			// com o tamanho da cena. Numa cinematica de 11 s (SSJ1) ou 35 s (SSJ3) a aura morria no
			// comeco e o resto da cena rodava sem ela.
			//
			// Agora sao dois fatores: sobe em ~0,33 s e SEGURA em 1, e o unico decaimento e o do
			// FIM -- os ultimos 0,8 s da cena. Assim ela acompanha qualquer cena, inclusive as que
			// ainda nao existem.
			// ============================================================================
			//
			// ============================ E O QUE APARECE E A FORCA, NAO O `Modulate` ============================
			// Esta linha escrevia `_auraGrande.Modulate`, e ela PAROU de funcionar no instante em que a
			// chama passou a ser uma `SpriteDeAura`: o `Aura.gdshader` ESCREVE `COLOR` inteiro no
			// fragmento, entao a modulacao do node e descartada -- a chama sairia opaca da primeira ao
			// ultimo quadro da cena, e a "aparicao" e o "apagar" simplesmente nao existiriam.
			//
			// Quem faz as duas coisas agora e a `forca` do shader (`c.a * forca`, ultima linha dele) --
			// o MESMO canal por onde a chama da carga ja pulsa. Nao ha caminho novo: ha o de sempre,
			// multiplicado pela curva da cena.
			// ================================================================================================
			double restante = _cena.Segundos - _t;
			float a = (float)(Mathf.Min(_auraT * 3.0, 1.0) * Mathf.Clamp(restante / 0.8, 0, 1));
			_auraGrande.Definir(true, _chamaDaCena.Cor, _chamaDaCena.Forca * a * 0.8f);
			// QUEM ESCALA E O PIVO (que esta nos pes), nao o sprite (que esta centrado no peito).
			// O CRESCIMENTO TEM TETO agora. Antes ele nao precisava: a aura morria aos 3 s e nunca
			// passava de 1,36x. Durando a cena inteira, um SSJ3 de 35 s chegaria a 5x -- a chama
			// engoliria a tela. Ela cresce nos primeiros 3 s e fica.
			_auraPivo.Scale = Vector2.One * (float)(1.0 + Mathf.Min(_auraT, 3.0) * 0.12);
		}

		if (_proximo >= _cena.Beats.Length && _t > _cena.Segundos)
		{
			_fim = FimDaCena.Sozinha;
			QueueFree();
		}
	}

	/// <summary>Rede de seguranca: sair da arvore por qualquer motivo solta o corpo.</summary>
	public override void _ExitTree() => Soltar();

	/// <summary>Desfaz TUDO o que a cena prendeu. Chamado de todo caminho de saida.</summary>
	private void Soltar()
	{
		Devolver();
		if (!IsInstanceValid(_alvo)) return;
		_alvo.GetNodeOrNull<CharacterVisual>("Visual")?.TravarPose(false);

		// ============================ O CABELO VOLTA PRO LADO CERTO DA PISCADA ============================
		// A cena pode morrer no meio de um piscar (o teto, a troca de zona, a morte, o alvo sumindo) --
		// e agora a janela e a cena INTEIRA, e nao o segundo e meio de antes. Metade dessas mortes
		// pegaria o boneco com o cabelo da forma que ele NAO assumiu, e ele ficaria assim ate o proximo
		// pacote de aparencia chegar.
		//
		// E a mesma regra do emprestimo da aura logo abaixo: quem acende, apaga -- em TODO caminho de
		// saida, e nao so no bom. O `Assumir` nao passa por aqui piscando (ele desliga a bandeira antes
		// de vestir), entao esta linha so roda quando a cena de fato foi interrompida.
		// ============================================================================================
		if (_piscaLigada)
		{
			_piscaLigada = false;
			_alvo.GetNodeOrNull<CharacterVisual>("Visual")?.VestirCabeloDaForma(_vestido);
		}

		// A AURA EMPRESTADA VOLTA TAMBEM. Sem isto, uma cena do Oozaru interrompida antes do
		// `Assumir` (o teto, a troca de zona, a morte) deixaria o jogador brilhando PARA SEMPRE
		// numa forma que ele nem chegou a assumir -- e a unica saida seria carregar e soltar o C.
		// E a mesma logica da tranca: quem empresta devolve em TODO caminho de saida, nao so no bom.
		if (!_auraBaseAcesa) return;
		_auraBaseAcesa = false;
		_alvo.GetNodeOrNull<Aura>("Aura")?.Apagar();
	}

	/// <summary>
	/// Quanto a cena pode passar da propria duracao antes de o teto soltar o corpo na marra.
	/// Generoso de proposito: ele nao e o relogio da cena, e a rede embaixo dela.
	///
	/// PUBLICO por causa da bancada, pelo mesmo motivo do <see cref="PrazoMaximoPreso"/>: pra afirmar
	/// que uma cena NAO precisa do teto e preciso rodá-la ALEM do prazo dele -- parar antes torna a
	/// afirmacao vazia, e era o que acontecia (o laco ia ate `Segundos + 3`, e 3 e menor que esta
	/// folga). Um `5` recopiado la mediria outra coisa no dia em que este numero mudasse.
	/// </summary>
	public const double FolgaDoTeto = 5.0;

	/// <summary>
	/// HA QUANTO TEMPO A CHAMA DA CENA ESTA ACESA. **NEGATIVO = o beat <see cref="Efeito.AuraGrande"/>
	/// ainda nao veio**, e por isso ela nao desenha.
	///
	/// O ESTADO ERA LIDO DO `Modulate.A` do sprite, e ele deixou de responder (ver o `_Process`: o
	/// shader da aura escreve `COLOR` inteiro). Precisava de um estado proprio, e o sentinela cabe
	/// neste mesmo campo: "faz -1 s que acendeu" nao e um instante alcancavel, entao ele nao colide
	/// com nenhum valor legitimo -- e um `bool` ao lado seria um segundo lugar dizendo a mesma coisa.
	/// </summary>
	private double _auraT = -1;

	/// <summary>
	/// O `World`, achado UMA VEZ.
	///
	/// Era `GetTree().Root.FindChild("World", true, false)` escrito nos dois pontos que sacodem a
	/// camera -- uma busca RECURSIVA pela arvore inteira. Nos beats isso e barato (poucas por
	/// cena); no tremor continuo do Oozaru seriam 60 varreduras por segundo durante quatro
	/// segundos, pra sempre achar o mesmo node. E o cache nao pode vazar: a cena morre no fim, e
	/// com ela a referencia.
	/// </summary>
	private World? Mundo()
	{
		// O MUNDO PODE MORRER ANTES DA CENA -- trocar de zona destroi e refaz o `World`, e uma
		// cinematica de 32 s sobrevive a isso com folga. Chamar `Sacudir` num node ja liberado
		// derruba o cliente com `ObjectDisposedException`; largar o cache e procurar de novo custa
		// uma busca.
		if (_mundo != null && !IsInstanceValid(_mundo)) _mundo = null;
		return _mundo ??= GetTree()?.Root?.FindChild("World", true, false) as World;
	}

	private World? _mundo;

	/// <summary>
	/// ESTA CENA ACONTECEU NA SUPERFICIE DE UM PLANETA? Lido UMA VEZ, no `_Ready`.
	///
	/// ============================ E POR QUE A ZONA DA TELA RESPONDE POR ELA ============================
	/// A pergunta que interessa e "a cena e no MEU planeta?", e o pacote ja a respondeu: o
	/// `S2C.Forma` so sai pra `ZoneList(pl.Zone.Hash)` (`GameServer.Formas.cs`), ou seja quem esta na
	/// MESMA zona de quem virou. Chegou aqui = e no meu lugar. Entao perguntar pela minha zona e
	/// perguntar pela dele, e nao ha campo novo a inventar no pacote.
	///
	/// ============================ O QUE ISTO EXISTE PRA IMPEDIR ============================
	/// "Planeta" nao e "servidor", e ha uma zona no jogo que nao respeita isso: o ESPACO e UMA zona
	/// so pro universo inteiro (`Espaco.NomeDoEspaco`), e o corte de interesse la e a CHUNK. Sem esta
	/// pergunta, alguem virando Super Saiyajin numa chunk sacudiria a tela de quem esta a setores de
	/// distancia -- e o dono foi explicito: quem esta no espaco ou noutro planeta NAO sente.
	///
	/// Interiores (Sala do Tempo, o dentro de uma nave, a Dimensao Mental) tambem respondem `false`
	/// pelo motivo oposto: ali todo mundo ja esta a menos de um `view` de distancia, entao o tremor
	/// cheio alcanca -- e o "resto do planeta" nao existe pra alcancar.
	/// ==============================================================================
	/// </summary>
	private bool _noPlaneta;

	/// <summary>
	/// QUANTO DO TREMOR CHEGA A ESTA TELA -- 0 a 1, pelo `peso` do <see cref="World.Sacudir"/>.
	///
	/// ============================ DOIS DEGRAUS, E OS DOIS SAO DO DM OU DO DONO ============================
	///   * dentro de <see cref="Cinematicas.RaioDoTremorCheio"/> (o `view(src)` do `Quake()`,
	///     `Ascension.dm:8`): CHEIO, e pra todo mundo -- o original nao tem meia tremida;
	///   * fora dele, no mesmo planeta: <see cref="Cinematicas.PesoDoTremorDeLonge"/>, o eco pelo
	///     chao. Nunca zero, porque o dono disse "afeta todos no planeta";
	///   * fora dele e fora de um planeta: NADA. Ver <see cref="_noPlaneta"/>.
	///
	/// ============================ A COSTURA DO MAPA MENTE, E TUDO BEM ============================
	/// A distancia e a reta, e o planeta DA A VOLTA (`GameServer.Volta.cs`): quem esta encostado na
	/// borda leste esta a um passo de quem esta na oeste, e esta conta o poe a um mapa inteiro. O erro
	/// so pode cair pro lado do eco -- alguem que teria direito ao solavanco cheio recebe metade --, e
	/// esse e exatamente o caso que o dono ja aceitou pro resto do planeta. Consertar isso obrigaria a
	/// cena a conhecer o tamanho do mapa pra medir a distancia toroidal, e o preco nao paga o ganho.
	/// ====================================================================================================
	/// </summary>
	private float PesoDoTremor()
	{
		// O MEU PROPRIO CORPO NAO PRECISA DE MEDICAO: a distancia e zero por construcao. Vale pela
		// bancada tambem, que roda cenas com `souEu: true` sem corpo local montado no mundo.
		if (_souEu) return 1f;

		// SEM CORPO LOCAL NAO DA PRA MEDIR -- e nesse caso nao ha camera montada pra sacudir de todo
		// jeito. O eco e a resposta conservadora: reivindicar o solavanco cheio sem ter medido nada
		// seria o liquidificador voltando pela porta dos fundos.
		if (Mundo()?.PosicaoLocal is not { } eu) return Cinematicas.PesoDoTremorDeLonge;

		return eu.DistanceTo(_alvo.GlobalPosition) <= Cinematicas.RaioDoTremorCheio
			? 1f
			: _noPlaneta ? Cinematicas.PesoDoTremorDeLonge : 0f;
	}

	/// <summary>O peso do tremor nesta tela agora. Pra bancada -- ver `--diagforma`.</summary>
	public float PesoDoTremorDeTeste => PesoDoTremor();

	private void Disparar(Beat b)
	{
		BeatsDeTeste++;

		// ============================ O TREMOR E DE QUEM ESTA NO PLANETA ============================
		// A FORCA, A QUEDA E A CADENCIA SAO DA CENA e por isso saem do Core (ver
		// `Cinematicas.QuedaDoTremor`): o padrao do `Sacudir` e o solavanco SECO do combate, e uma
		// transformacao nao treme como um soco.
		//
		// A AMPLITUDE E SEMPRE A CHEIA aqui; quem a corta pela distancia e o `peso` -- ver
		// `PesoDoTremor`. Antes esta linha escolhia entre duas constantes por `_souEu`, e era uma
		// escolha errada nos dois sentidos: dava metade pra quem estava do lado (o DM da o solavanco
		// INTEIRO pra todo o `view`) e nao dava nada pra quem estava do outro lado do planeta.
		// ======================================================================================
		if (b.Faz.HasFlag(Efeito.Tremor))
			Mundo()?.Sacudir(Cinematicas.ForcaDoTremor, peso: PesoDoTremor(),
							 queda: Cinematicas.QuedaDoTremor, cadencia: Cinematicas.CadenciaDoTremor);

		// A CHAMA DA CENA ACENDE. Zerar o relogio ja e o suficiente -- o `_Process` cuida da subida, e
		// ele so olha pra ela quando `_auraT >= 0` (ver o campo).
		if (b.Faz.HasFlag(Efeito.AuraGrande)) _auraT = 0;

		if (b.Faz.HasFlag(Efeito.AuraBase)) AcenderAuraBase();

		// AQUI HAVIA `Efeito.PedrasSubindo` -> `SoltarPedras()`. A pedra deixou de ser um beat: ela
		// corre por baixo da cena inteira agora, no `TocarPedras`. Ver o bloco do CHAO SOLTO.

		if (b.Faz.HasFlag(Efeito.Cratera))
		{
			// QUEM VEM DA RAIVA ABRE O CHAO. A escolha sai de `Catalogo.NasceDaRaiva` -- a mesma
			// regra derivada que a bancada percorre nas 33 entradas --, e nao de uma lista escrita
			// aqui: um degrau novo de Legendary ja nasce com a cratera certa.
			Plantar(Jandirus.Core.Forms.Catalogo.NasceDaRaiva(_forma)
					? Protocol.Decal.CrateraGrande : Protocol.Decal.Cratera, 0);
			Plantar(Protocol.Decal.Fumaca, 0);
		}
		// ============================ POEIRA E FUMACA, NAO CICATRIZ ============================
		// Eu tinha ligado o `Efeito.Poeira` ao `ChaoDanificado` -- o decalque PERMANENTE de terra
		// revirada. Dois erros num:
		//
		//  1. **Errado no DM.** `createDustmisc` e uma baforada que sobe e some; a marca de terra e
		//     `createCrater`, que a cena ja dispara no beat certo.
		//  2. **Errado no jogo.** O `ChaoDanificado` so some quando o planeta recarrega (foi pedido
		//     assim). Toda primeira transformacao deixava tres cicatrizes permanentes no chao.
		//
		// E era ISTO que aparecia como "aura torta": tres manchas de terra em volta do corpo,
		// pintadas de vermelho pela luz da aura. Passei duas rodadas procurando o defeito no sprite
		// da aura -- que estava certo o tempo todo.
		// ==================================================================================
		if (b.Faz.HasFlag(Efeito.Poeira))
			for (int i = 0; i < 3; i++) Plantar(Protocol.Decal.Fumaca, 26);

		// ============================ VESTIR VEM ANTES DOS EFEITOS, E AGORA ISSO IMPORTA ============================
		// O DEGRAU DE BAIXO, VESTIDO POR UM INSTANTE. Ver `Efeito.VesteDegrau`.
		//
		// ELE SUBIU PRA CA, e o motivo nasceu neste passe: agora o `Vestir` mexe na faisca (ver la), e
		// o beat que veste o SSJ2 na cena do SSJ3 e o MESMO que pede `Efeito.Raios`. Rodando depois, o
		// estado calmo do degrau apagaria por cima a rajada que a cena acabou de soltar -- o degrau e o
		// ESTADO, o beat de efeito e o ACONTECIMENTO, e acontecimento se desenha por cima de estado.
		// ==========================================================================================================
		if (b.Faz.HasFlag(Efeito.VesteDegrau)) VestirODegrauSeguinte();

		// OS RAIOS DA CENA sao os MESMOS do estado transformado (`RaiosDaForma`), disparados a mao.
		// Ter dois sistemas de raio no jogo seria ter dois lugares pra consertar o mesmo defeito.
		// `_forma != null` NAO E PARANOIA DE COMPILADOR: sem forma nao ha `Raios` a ler, e a faisca
		// e ESTADO da forma (`RaiosDaForma.Definir` deixa o node armado depois da cena). Uma cena de
		// furia que acendesse faisca deixaria o corpo faiscando pra sempre, sem forma pra apagar.
		if (b.Faz.HasFlag(Efeito.Raios) && _forma != null
			&& _alvo.GetNodeOrNull<RaiosDaForma>("Raios") is { } r)
		{
			r.Definir(true, _corRaios, Math.Max(1, _forma.Raios));
			r.DispararDeTeste();
		}

		if (b.Faz.HasFlag(Efeito.FeixesNoChao)) Feixes();

		// ============================ OS TRES DO ENCHIMENTO ============================
		// Nenhum deles inventa engine: o anel e o `CombatFx.Onda` do combate, a descarga e o
		// `Iluminacao.Raio` da tempestade, e o clarao e um `ColorRect` numa `CanvasLayer`. Ver os
		// metodos, um a um, e os comentarios dos proprios `Efeito` no Core sobre o que e porte e o
		// que e invencao.
		//
		// ERAM QUATRO. O quarto era o `Efeito.Cascalho` -> `Cascalho()` -> `PoeiraDeEstrago.Soltar`, e
		// o dono o descreveu com precisao antes de mandar tirar: *"uns quadrados marrons caindo e
		// criando uma fumaca parecendo q quebrou uma parede ou objeto"*. Era literalmente isso -- a
		// cinematica chamando o sistema de ESTRAGO DE CENARIO pra fazer enfeite. A `PoeiraDeEstrago`
		// continua inteira e em uso no combate; o que morreu foi a chamada daqui.
		// ============================================================================
		if (b.Faz.HasFlag(Efeito.AnelDeChoque)) Anel();

		if (b.Faz.HasFlag(Efeito.DescargaNoCeu)) Descarga();

		if (b.Faz.HasFlag(Efeito.ClaraoDeTela)) Clarao();

		// ============================ O BANHO DE COR -- `animate(src, color=rgb(...))` ============================
		// A COR SAI DA FORMA e nao do beat: `Aura.CorDaChamaDe` e a MESMA funcao que pinta a chama da
		// cena, a aura do corpo e a folha da carga (ver `Vestir`). Um quinto lugar decidindo "de que cor
		// e esta forma" seria o quinto lugar a discordar dos outros quatro. Ver `Efeito.BanhoDeCor`.
		//
		// O PRAZO E O DO DM: `animate(time=6)` sobe em 0,6 s e `spawn(12) color=null` devolve em 1,2 s --
		// e o `Banhar` decai LINEAR do pico, entao 1,2 s de escoamento cobre os dois gestos com um
		// relogio so. `LSSj()` usa `time=7` (0,7 s); a diferenca de um tique nao vale um segundo campo.
		// =====================================================================================================
		//
		// SEM FORMA NAO HA BANHO: `Aura.CorDaChamaDe(null)` devolve o branco do ki cru, e lavar o
		// corpo de branco no meio de uma cena vermelha nao e o gesto do DM -- os cinco `animate` que
		// isto porta sao todos de transformacao.
		if (b.Faz.HasFlag(Efeito.BanhoDeCor) && _forma != null
			&& _alvo.GetNodeOrNull<CharacterVisual>("Visual") is { } vb)
			vb.Banhar(Aura.CorDaChamaDe(_forma, CorPessoalDoAlvo), Cinematicas.SegundosDoBanho);

		// ============================ O PISCAR DE CABELO: O BEAT SO ARMA ============================
		// Era `_piscando = !_piscando` aqui -- uma troca por beat. Virou um interruptor porque a cena
		// que ele serve tem 25,0 s e os beats acabavam aos 2,9 (ver `Efeito.PiscaCabelo` no Core, que e
		// onde a decisao mora). Quem troca agora e o `_Process`, na cadencia do DM.
		//
		// GUARDA O QUE ESTAVA VESTIDO ANTES: e pra la que o `Soltar` devolve o cabelo se a cena morrer
		// no meio de uma piscada -- ver `_vestido`.
		//
		// IDEMPOTENTE: um segundo beat com esta bandeira nao reinicia nada. Nao ha nenhum hoje (a
		// bancada cobra que seja um so), e se houvesse ele nao poderia significar "pisca mais rapido" --
		// a cadencia e do roteiro, nao do numero de beats.
		// ========================================================================================
		//
		// E SEM FORMA ELE NEM ARMA: piscar e alternar ENTRE dois penteados, e um dos dois e a forma.
		// Sem ela o `_Process` chamaria `VestirCabeloDaForma(null)` metade do tempo -- que e "sem forma
		// nenhuma", ou seja o Super Saiyajin em luto piscaria de volta pra base duas vezes por segundo.
		if (b.Faz.HasFlag(Efeito.PiscaCabelo) && _forma != null && !_piscaLigada)
		{
			_piscaLigada = true;
			_proximaPiscada = _t;   // a primeira troca sai no MESMO quadro do beat, e nao um sorteio depois
		}

		// O INSTANTE EM QUE A FORMA FICA. Ver o cabecalho da classe.
		if (b.Faz.HasFlag(Efeito.Assumir)) Assumir();

		if (b.Som.Length > 0) Som(b.Som);

		// ============================ AS FALAS SAO DE QUEM ESTA PERTO -- A DIVIDA FOI PAGA ============================
		// Aqui morava um `if (!_souEu) return`, justificado assim: *"o cliente nao tem mapa de
		// id -> nome (nenhum pacote o carrega)"*. **Isso caducou.** O `S2C.PeerLook` carrega o nome
		// desde que a aparencia passou a viajar, e o cliente ja o guarda em `World._nomes` -- e e de
		// la que sai o `_nome` deste node (ver `Rodar`). No DM as falas saiam pra `view(8)` COM o
		// nome (`SSJ3Cinematic.dm:16`), que e exatamente o que volta a acontecer.
		//
		// A FALA VIRA BALAO, pra todo mundo: e voz de um corpo, e ela nasce onde o corpo esta -- e o
		// mesmo tratamento que o `Diz` do chat recebe. O canal e `Diz` de proposito: as falas de cena
		// tem "!" e caem sozinhas no laranja de GRITO, que e o que elas sao.
		//
		// A NARRACAO NAO VIRA BALAO. Ela nao e voz de ninguem -- ela descreve o MUNDO ("o chao comeca
		// a tremer"), sem sujeito. Balao de narracao poria na boca do personagem uma frase que ele nao
		// disse. Ela continua no chat, onde ja era o `*[src] ...` do DM.
		// ========================================================================================================
		if (b.Fala.Length > 0)
		{
			_alvo.GetNodeOrNull<BalaoDeFala>("Balao")?.Dizer(Jandirus.Net.Protocol.Fala.Diz, b.Fala);
			if (_souEu) Chat.Sistema($"Você: {b.Fala}");
			else if (_nome.Length > 0) Chat.Sistema($"{_nome}: {b.Fala}");
		}
		if (b.Narra.Length > 0) Chat.Sistema($"* {b.Narra}");
	}

	/// <summary>
	/// ACENDE A AURA BASE DO CORPO -- o `Efeito.AuraBase`.
	///
	/// ============================ QUEM E O DONO DA AURA, E POR QUE ESTE ============================
	/// Ja ha DOIS desenhando a mesma arte: o node <see cref="Aura"/> e a <see cref="CargaVisual"/>.
	/// Um terceiro dono seria o começo do fim -- as duas chamas empilhadas ja foram defeito
	/// fotografado uma vez, e a `Aura.ChamaDaCarga` existe pra isso.
	///
	/// Escolhido o node `Aura`, por eliminacao honesta:
	///
	///   * A `CargaVisual` E O SERVIDOR. Ela e escrita pelo canal de efeito de carga
	///     (`Definir`, vindo do pacote), e o proprio cabecalho dela diz por que: a versao que
	///     acendia direto da tecla mentia quando o servidor recusava. Acender ali seria FINGIR uma
	///     carga que nao existe -- e o proximo pacote de carga apagaria a cena no meio.
	///   * O node `Aura` ja tem `Acender`/`Apagar` publicos, ja e onde a forma guarda cor e folha
	///     (`Preparar`), e HOJE SO A BANCADA o acende. Esta cena e o primeiro chamador de jogo do
	///     `Acender` -- o que tambem tira aquele metodo da situacao de existir so pra teste.
	///
	/// A supressao continua valendo nos dois sentidos: se o jogador estiver segurando C quando
	/// olhar pra lua, a `CargaVisual` ja mandou `ChamaDaCarga(true, ...)` e quem DESENHA e ela. A COR
	/// nao muda de dono nesse caso -- e a que a linha abaixo acabou de guardar, porque ha uma
	/// resposta so (`Aura.CorDaChama`). Uma chama por corpo, em qualquer ordem.
	/// ==========================================================================================
	/// </summary>
	private void AcenderAuraBase()
	{
		if (_alvo.GetNodeOrNull<Aura>("Aura") is not { } aura) return;

		// A FOLHA BASE NA MARRA, e nao a `Catalogo.Folha(_forma)`: o dono pediu a
		// `colorablebigaura`, e a folha da forma so vale quando a forma JA E dele -- o que so
		// acontece no `Assumir`, que reescreve a folha logo depois (pelo `Vestir`).
		aura.Folha(Jandirus.Core.Forms.FolhaDeAura.Base);
		// A COR PESSOAL DESTE CORPO, e nao o `Aura.CorDoKiCru` que estava cravado aqui. O beat se
		// chama `Efeito.AuraBase` e a aura da base e pessoal -- com a constante, o Oozaru era o
		// unico momento do jogo em que o jogador via a chama de outra pessoa acender no proprio
		// corpo. O node ja tem a resposta; pedir a ele e o que impede uma segunda.
		aura.Acender(aura.CorPessoal, 1.4f);
		_auraBaseAcesa = true;
	}

	/// <summary>
	/// A cena acendeu a aura base (emprestada) e ainda nao a devolveu. Ele NAO manda mais no tremor:
	/// o rumor de camera e da cena inteira agora (ver o `_Process`), e nao deste emprestimo.
	/// </summary>
	private bool _auraBaseAcesa;

	/// <summary>A aura base esta acesa por esta cena agora? Pra bancada.</summary>
	public bool AuraBaseDeTeste => _auraBaseAcesa;

	/// <summary>
	/// A FORMA FICA -- e "ficar" e vestir a forma ALVO, nada mais.
	///
	/// ============================ ELE NAO TEM MAIS APARENCIA PROPRIA ============================
	/// Este metodo escrevia a aura, os raios e a folha da carga por conta propria, e o `Vestir` so
	/// cuidava de cabelo/corpo/tinta. Eram duas descricoes do mesmo personagem, e a do `Vestir` era
	/// a incompleta -- entao todo degrau intermediario herdava calado o que estivesse ligado antes.
	/// Foi assim que o dono viu "os efeitos dos raiozinhos continuam" ao subir de SSJ2 pra SSJ3: o
	/// primeiro beat da cena veste a BASE, e vestir a base nao desligava faisca nenhuma.
	///
	/// Hoje ha uma descricao so (ver `Vestir`), e o `Assumir` e o degrau final dela.
	/// ==========================================================================================
	/// </summary>
	private void Assumir()
	{
		// ============================ QUEM ACENDEU, APAGA -- E APAGA AQUI ============================
		// O dono: "ele vai virar o oozaru e nesse momento a aura desativa". Nao ha um
		// `Efeito.ApagarAura` porque nao precisa haver: a aura base e um EMPRESTIMO que dura da
		// cena ate a forma ficar, e "a forma ficou" e exatamente este metodo. Um flag separado
		// deixaria possivel escrever uma cena que acende e nunca apaga.
		//
		// ANTES DO `Vestir`, e a ordem e a regra: quem prepara a aura hoje e o `Vestir` (ver la), e
		// `Preparar` num node ACESO troca a cor sem apagar (ver `Aura.Preparar`) -- apagar depois
		// desligaria tambem a cor que a forma acabou de guardar. E e este `false` que abre o desvio
		// do `Vestir`: enquanto o emprestimo dura, ele nao encosta na aura.
		if (_auraBaseAcesa)
		{
			_auraBaseAcesa = false;
			_alvo.GetNodeOrNull<Aura>("Aura")?.Apagar();
		}

		// ============================ E O PISCAR ACABA AQUI, PELA MESMA REGRA DA AURA ============================
		// O piscar e um estado que a cena acende e que a cena tem que apagar -- exatamente como o
		// emprestimo da aura base logo acima, e pelo mesmo motivo: nao ha `Efeito.ParaDePiscar` porque
		// nao precisa haver. "A forma ficou" e este metodo, e um beat separado pra desligar tornaria
		// possivel escrever uma cena que pisca pra sempre.
		//
		// ANTES DO `Vestir`, senao o `_Process` do proximo quadro trocaria o cabelo por cima da forma
		// que acabou de ficar. Nao ha nada a restaurar aqui: o `Vestir` da linha de baixo escreve o
		// penteado definitivo, qualquer que seja o lado em que a piscada parou.
		// ====================================================================================================
		_piscaLigada = false;

		// ============================ E QUANDO NAO HA FORMA, A VIRADA NAO VESTE NINGUEM ============================
		// Este e o ponto em que o <see cref="Efeito.Assumir"/> deixa de ser "assumir a forma" e passa a
		// ser o que ele sempre foi estruturalmente: a VIRADA da cena (ver o bit no Core). Na furia a
		// virada e a erupcao -- a cratera e o `powerup.wav` de `Murder.dm:158-160` --, e o DM nao
		// escreve cabelo, aura nem contorno em lugar nenhum daquele proc.
		//
		// `Vestir(null)` NAO seria "nao fazer nada": ele desfaz sprite, tinta e rabo (ver
		// `VestirAFormaSemCena` no `World`), ou seja um SSJ3 que perdesse um amigo voltaria ao normal
		// no meio da propria furia. Por isso a pergunta, e nao um `?`.
		//
		// As duas linhas de cima FICAM valendo mesmo sem forma, e de proposito: se um roteiro futuro de
		// cena sem forma acender a aura base ou o piscar, quem os apaga continua sendo a virada.
		// ======================================================================================================
		if (_forma != null) Vestir(_forma);
	}

	/// <summary>
	/// VESTE O DEGRAU SEGUINTE DA ESCADA -- o <see cref="Efeito.VesteDegrau"/>.
	///
	/// O contador e a UNICA memoria disto: o beat nao diz qual degrau quer, ele diz "o proximo". A
	/// tabela vem do Core (<see cref="Jandirus.Core.Forms.Cinematicas.EscadaDaCena"/>) e e guardada
	/// aqui porque ela aloca -- e uma cena pode pedir varias.
	///
	/// BEAT A MAIS NAO VESTE NADA: uma cena com mais `VesteDegrau` do que a escada tem degraus e
	/// erro de quem escreveu a cena, e a bancada o acusa (`--diagforma`). Repetir o ultimo degrau
	/// aqui esconderia isso atras de um instante em que nada muda, que e o defeito mais dificil de
	/// perceber que existe.
	/// </summary>
	private void VestirODegrauSeguinte()
	{
		// SEM FORMA NAO HA ESCADA -- e nao ha o que vestir. `EscadaDaCena` deriva os degraus ABAIXO da
		// forma da cena; sem forma a pergunta nao existe.
		if (_forma == null) return;
		_escada ??= Jandirus.Core.Forms.Cinematicas.EscadaDaCena(_forma);
		if (_degrau >= _escada.Length) return;

		// ============================ O PISCAR CEDE A ESCADA ============================
		// Os dois escrevem cabelo, e por dois relogios diferentes: o degrau e um ESTADO que a cena
		// narra (*"o que voce esta vendo agora e o meu estado normal"*), o piscar e uma textura que
		// troca duas vezes por segundo. Rodando juntos, o piscar apagaria o degrau meio segundo depois
		// de ele ser vestido -- e o defeito seria o mesmo que o `Vestir` existe pra fechar (descricao
		// parcial sobrescrevendo pedaco de outra), so que oscilando.
		//
		// QUEM CEDE E O PISCAR, e nao o contrario, porque a escada CONTA a cena e o piscar so a
		// tempera: uma fala sobre um cabelo errado e um defeito que o jogador le; um cabelo que parou
		// de piscar e uma cena mais calma.
		//
		// E ISTO NAO MUDA NADA HOJE -- e de proposito. Nenhuma cena tem as duas bandeiras (as tres que
		// piscam sao `ssj1`, `future_ssj` e `primal_c_type`, e nenhuma veste degrau; a unica que veste
		// e a do `ssj3`, que nao pisca), e a bancada tranca isso como invariante. Esta linha e a rede
		// embaixo: no dia em que alguem escrever as duas juntas numa cena nova, ela sai calma em vez de
		// sair quebrada. O `Disparar` ja ajuda -- ele chama este metodo ANTES do bloco do piscar.
		// ============================================================================
		_piscaLigada = false;

		Vestir(_escada[_degrau++]);
	}

	private FormaDef[]? _escada;
	private int _degrau;

	/// <summary>
	/// POE A APARENCIA DE UMA FORMA NO CORPO -- a aparencia INTEIRA: penteado, corpo proprio, tinta
	/// do rabo, faisca, cor/folha da aura, folha da carga e contorno. E o irmao do
	/// <see cref="Jandirus.Client.World.VestirAFormaSemCena"/>, que faz o mesmo fora de cinematica.
	///
	/// ============================ ELE PRECISA DESCREVER TUDO, E NAO SO O QUE MUDA ============================
	/// Ele descrevia so cabelo/corpo/tinta, e o resto (raios, aura, carga) era escrito uma vez so, no
	/// `Assumir`. Descricao parcial nao VESTE: ela sobrescreve um pedaco e deixa o resto do degrau
	/// ANTERIOR no corpo.
	///
	/// Foi o defeito que o dono viu: em SSJ2, iniciar a cena do SSJ3 faz o primeiro beat vestir a BASE
	/// ("o que voce esta vendo agora e o meu estado normal") -- e o cabelo voltava ao preto com a
	/// faisca azul do SSJ2 ainda crepitando por cima, porque desligar raio nao era trabalho de
	/// ninguem. A aura preparada e a folha da carga tinham exatamente a mesma falha, calada: quem
	/// segurasse o C no meio da cena veria a chama do Super Saiyajin num corpo em forma base.
	///
	/// A regra que fecha a familia inteira: **se e possivel ver, sai daqui**. Efeito novo que o
	/// `Assumir` acenda por fora renasce como este mesmo defeito com outro nome.
	/// =========================================================================================================
	///
	/// ============================ UM SO LUGAR DECIDE COMO UMA FORMA SE PARECE ============================
	/// Estas linhas eram do `Assumir`; os degraus intermediarios precisavam das MESMAS quatro, e uma
	/// segunda copia envelheceria contra a primeira -- e o modo de falha seria "a cena mostra um SSJ1
	/// diferente do SSJ1 de verdade", que ninguem liga a esta funcao.
	///
	/// ============================ A ORDEM MUDOU, E ELA ERA DEFEITO ============================
	/// No `Assumir` a tinta vinha ANTES do `CorpoDaForma`. `PintarCabelo` pinta o rabo BASE e desiste
	/// quando a forma traz o proprio corpo (`CharacterVisual.PintarCabelo:215`) -- rodando antes, o
	/// SSJ4 e o Oozaru tingiam de dourado um rabo que o `CorpoDaForma` esconderia na linha seguinte,
	/// deixando a tinta armada pra reaparecer no reverter. E o mesmo risco que o comentario de la ja
	/// descrevia, e a ordem certa e a que o `World.VestirAFormaSemCena` usa.
	/// ==============================================================================================
	/// </summary>
	private void Vestir(FormaDef d)
	{
		// A BASE NAO E FORMA NENHUMA, e tres das linhas abaixo perguntam isso. Ela aparece aqui como
		// degrau de verdade (e o primeiro da escada do SSJ3, o `RemoveHair()` de `SSJ3Cinematic.dm:12`),
		// entao "vestir" tem que saber desfazer tanto quanto sabe fazer.
		bool ehBase = d.Id == Jandirus.Core.Forms.Catalogo.IdBase;

		// O QUE O CORPO ESTA VESTINDO AGORA -- e o outro lado da piscada, e o valor de volta se a cena
		// morrer no meio de uma. `null` (a base) e o valor inicial e ele e legitimo: ate o primeiro
		// `Vestir` o corpo esta como o `World.AoMudarForma` o deixou, que numa cena e "sem mexer".
		// Ver `_vestido`.
		_vestido = ehBase ? null : d;

		// O BONECO PODE FALTAR E O RESTO NAO PODE PARAR POR ISSO. Este metodo saia com um `return`
		// quando o `Visual` nao existia, e com a faisca e a aura morando aqui esse `return` deixaria
		// de ser inofensivo: um corpo sem `CharacterVisual` (o macaco em construcao, um remoto que
		// ainda esta nascendo) ficaria com os raios do degrau anterior pra sempre.
		var vis = _alvo.GetNodeOrNull<CharacterVisual>("Visual");

		// PRIMEIRO AS CAMADAS: `CorpoDaForma` cria a camada da pelagem do SSJ4 com material novo, e
		// uniform novo nasce zerado -- escrevendo o contorno antes, a pelagem estreava sem ele.
		// Mesma ordem do `World.AoMudarForma`.
		vis?.CorpoDaForma(d.Corpo);
		// UMA CHAMADA, e a ordem entre sprite, tinta e rabo mora dentro dela -- ver
		// `CharacterVisual.VestirCabeloDaForma` e o bloco de cima sobre a ordem contra o `CorpoDaForma`.
		vis?.VestirCabeloDaForma(d);
		// O OVERLAY COLADO NO CORPO. Junto das outras duas e pelo mesmo motivo de ordem: as tres
		// mexem em CAMADAS, e o contorno/faisca daqui pra baixo escreve nos materiais delas.
		vis?.ColadasDaForma(d);

		// ============================ A FAISCA, E ELA E DOS DOIS CORPOS ============================
		// `Raios > 0` E NAO "tem forma": o SSJ1 e o SSJ4 tem `Raios = 0` e ligar o node pra emitir zero
		// raio seria um `_Process` e um sorteio por quadro pra nao desenhar nada. O mesmo teste do
		// `World.AcenderFormaNoCorpo`, que e quem faz isto quando nao ha cena.
		//
		// SEM PERGUNTAR DE QUEM E O CORPO: a faisca e da FORMA, e quem esta transformado a solta na
		// propria tela tambem. (Havia um `if (_souEu) return` logo abaixo, por causa do contorno; ele
		// morreu junto com o contorno daqui -- ver o bloco no fim deste metodo.)
		if (_alvo.GetNodeOrNull<RaiosDaForma>("Raios") is { } raios)
			raios.Definir(d.Raios > 0, new Color(Jandirus.Core.Forms.Catalogo.CorDosRaios(d)), d.Raios);

		// ============================ A NEBULOSA, PELO MESMO MOTIVO DA FAISCA ============================
		// Ela e da FORMA e nao do Ki, entao vale nos dois corpos sem perguntar de quem eles sao --
		// mesma razao escrita no bloco de cima. E entra AQUI, no `Vestir`, e nao no `Assumir`: o `Vestir` e
		// o unico lugar que descreve como uma forma se PARECE, e um degrau intermediario da cena que
		// nao passasse por ele deixaria a nuvem do degrau anterior acesa.
		//
		// A BASE APAGA SOZINHA: ela esta na linha Saiyajin do catalogo, e a paleta so existe pra a linha
		// do Ultra Instinto e pro `ultra_ego`. Nao ha `if (ehBase)` aqui de proposito -- seria uma
		// segunda verdade sobre a mesma pergunta.
		//
		// E A COR VEM JUNTA, no mesmo `nulo = apagar`: a nuvem tem duas paletas (indigo e roxa) desde o
		// pedido do Ultra Ego, e quem escolhe e o Core. Ver `NebulosaDaForma.Definir` pra o porque de
		// isto nao ser um `bool` mais uma chamada de cor.
		// ==============================================================================================
		if (_alvo.GetNodeOrNull<NebulosaDaForma>("Nebulosa") is { } nebulosa)
			nebulosa.Definir(Jandirus.Core.Forms.Catalogo.PaletaDaNebulosa(d));

		// ============================ A AURA E A CARGA: A COR E A FOLHA, NAO O ACENDER ============================
		// PREPARA, NAO ACENDE -- nem na estreia. A cinematica tem os proprios efeitos (a aura GRANDE,
		// os raios, a cratera); a aura persistente so nasce da carga ou da sobrecarga. O que se escreve
		// aqui e com que cara ela sairia SE alguem a acendesse -- e alguem acende: a tecla C.
		//
		// A FOLHA ANTES DA COR, e sao DUAS folhas porque sao dois desenhos da mesma chama (o node
		// `Aura` e a `CargaVisual`). Sem elas, carregar Ki no meio da cena desenha a chama do degrau
		// errado: a do Super Saiyajin num corpo que acabou de vestir a base, ou a da base num corpo que
		// ja e SSJ2.
		//
		// NA BASE A COR E O `CorDoKiCru` e nao o `d.Aura`, que la e `ffffff`: branco multiplicando a
		// folha colorivel APAGA a arte (defeito ja pago uma vez -- ver `Aura.Acender`). E a mesma
		// escolha que o `World.PrepararAuraDaForma` faz no ramo do `def == null`.
		//
		// ENQUANTO A AURA BASE EMPRESTADA ESTIVER ACESA ELA E A DONA DO NODE: `Preparar` num node ACESO
		// repinta na hora (ver `Aura.Preparar`), entao um degrau vestido durante o emprestimo trocaria a
		// cor de uma aura que a cena esta usando. Quem devolve o emprestimo e o `Assumir`, e ele o faz
		// ANTES de vestir -- por isso a forma final nunca cai neste desvio.
		if (!_auraBaseAcesa && _alvo.GetNodeOrNull<Aura>("Aura") is { } aura)
		{
			aura.Folha(Jandirus.Core.Forms.Catalogo.Folha(d));
			// O TERCEIRO ARGUMENTO E A GUARDA DA LUZ (o outro lugar que a escreve e o
			// `World.PrepararAuraDaForma`): `ehBase` e nao ter forma, e sem forma este node nao acende
			// por Ki nenhum. Ver `Aura.Aplicar`. Vale tambem pros degraus INTERMEDIARIOS da cena -- o
			// primeiro beat veste a BASE, e vestir a base tem que APAGAR a luz de quem estava segurando
			// C, senao a "base" da cinematica continuaria iluminando.
			//
			// A COR E A FORCA VEM DAS MESMAS DUAS FUNCOES que a chama da cena usa logo abaixo (e que o
			// `World` usa fora da cinematica): eram duas contas escritas a mao aqui, e a terceira copia
			// nasceria agora. Ver `Aura.CorDaChamaDe`.
			// A FORMA INTEIRA, e nao o par (cor, forca) ja resolvido: quem tem a cor PESSOAL deste
			// corpo em maos e o proprio node (ver `Aura.Preparar`). Este era o segundo dos dois
			// lugares que escreviam as duas contas a mao.
			aura.Preparar(d, !ehBase);
		}
		_alvo.GetNodeOrNull<CargaVisual>("Carga")?.Folha(Jandirus.Core.Forms.Catalogo.Folha(d));

		// ============================ E A CHAMA DA CENA VESTE O MESMO DEGRAU ============================
		// O terceiro desenho da mesma arte -- ver `ChamaDoDegrau`. Ele entra AQUI, junto dos outros dois,
		// exatamente pela regra que o cabecalho deste metodo escreve: **se e possivel ver, sai daqui**.
		// Deixa-lo no `Assumir` seria a segunda descricao do personagem de novo, e a cena do SSJ3 mostraria
		// a aura do SSJ3 por cima de um corpo que ainda esta em SSJ1.
		//
		// NAO ACENDE: quem acende e o beat `Efeito.AuraGrande` (`_auraT`). Aqui so se diz com que cara ela
		// sai -- a mesma divisao "prepara, mas nao acende" do node `Aura` logo acima.
		// ==========================================================================================
		ChamaDoDegrau(d);

		// ============================ O CONTORNO NAO SAI DAQUI, E NEM DO CORPO ALHEIO ============================
		// Aqui morava um `if (_souEu) return;` seguido de um `vis.AuraDaForma(CorDoContorno(d),
		// 0.35 + d.Intensidade * 0.13, ...)`: no corpo do dono da tela o contorno ja era do KI
		// (`World.AplicarContorno`), e no corpo ALHEIO era da FORMA, com a conta velha.
		//
		// Aquilo era a TERCEIRA escrita do mesmo pixel (as outras duas eram o `World.AcenderFormaNoCorpo`
		// e o cache do `World`), e a justificativa -- *"o cliente nao sabe o Ki alheio"* -- caducou: o bit
		// `EntityState.Sobrecarregado` viaja no snapshot por corpo. Hoje ha um dono so, e ele vale nos dois
		// corpos; ver `World._sobrecarregados`.
		//
		// E NAO FICA BURACO DE ORDEM: o `CorpoDaForma` la em cima cria camada nova com uniform zerado, mas
		// o `_Process` do `CharacterVisual` reescreve o contorno em TODAS as camadas de silhueta enquanto
		// ele estiver aceso (`EscreverContorno`) -- entao a pelagem que nasce no meio da cena se acerta no
		// quadro seguinte, e apagada nao ha o que acertar.
		// ====================================================================================================
	}

	/// <summary>
	/// VESTE UMA FORMA NESTE CORPO AGORA, sem esperar o beat. So pra bancada -- ver
	/// `RoboDeForma.AAparenciaInteiraDoDegrau`.
	///
	/// CHAMA O METODO DE PRODUCAO e nao uma copia dele, e isso e a checagem inteira: o que a bancada
	/// prova aqui e que UMA descricao veste o corpo todo. Uma segunda porta "equivalente" seria a
	/// segunda descricao de novo -- exatamente o defeito que o <see cref="Vestir"/> existe pra fechar.
	/// </summary>
	public void VestirDeTeste(FormaDef d) => Vestir(d);

	/// <summary>
	/// OS OITO FEIXES DE CHAO -- `Electricgroundbeam.dmi` saindo nas 8 direcoes.
	///
	/// Reaproveita o shader do raio: um feixe e um raio esticado, apontado pra fora. Fazer um
	/// segundo shader quase igual seria pagar duas vezes pelo mesmo desenho.
	/// </summary>
	private void Feixes()
	{
		var sh = ResourceLoader.Load<Shader>("res://Assets/Shaders/RaioDaForma.gdshader");
		if (sh == null) return;

		var img = Image.CreateEmpty(10, 40, false, Image.Format.Rgba8);
		img.Fill(Colors.White);
		ImageTexture tex = ImageTexture.CreateFromImage(img);

		for (int i = 0; i < 8; i++)
		{
			var m = new ShaderMaterial { Shader = sh };
			// O FEIXE E RAIO, e nao aura: ele reusa o `RaioDaForma.gdshader` logo acima e desenha a
			// mesma eletricidade deitada no chao. Entao ele segue a cor dos RAIOS -- no SSJ2 e no
			// SSJ3 os oito feixes saem azuis junto com a faisca, e nao dourados por conta propria.
			m.SetShaderParameter("cor", _corRaios);
			m.SetShaderParameter("zigue", 0.16f);
			m.SetShaderParameter("grossura", 0.07f);
			m.SetShaderParameter("halo", 1.4f);

			// ============================ O FEIXE NAO E UM RAIOZINHO ============================
			// O afinamento pedido pelo dono ("uns sao mt coisa") e dos raios que correm NO CORPO, e
			// mora no padrao do shader -- que este feixe herdaria por reusar o mesmo arquivo. Ele
			// se prende no 1,0 porque o quad dele tem 10 px de largura contra os 16 do raio: os
			// mesmos 0,07 de grossura ja dao um traco mais FINO em pixel de tela, e afinar de novo
			// deixaria os oito feixes do chao com meio pixel de nucleo -- some contra o clarao da
			// cinematica.
			//
			// E a variacao fica em zero porque os oito saem JUNTOS e em leque: a graca deles e a
			// simetria da estrela. Grossura sorteada aqui nao le como acaso, le como feixe torto.
			m.SetShaderParameter("afinar", 1.0f);
			m.SetShaderParameter("variacao_grossura", 0.0f);

			float ang = i * Mathf.Pi / 4f;
			var s = new Sprite2D
			{
				Texture = tex,
				Material = m,
				Rotation = ang,
				Position = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 26,
				Scale = new Vector2(1, 1.5f),
				ZIndex = 4,
				ZAsRelative = false,
			};
			AddChild(s);

			// O FEIXE VIAJA PRA FORA e apaga. `spawn walk(A, dir, 2)` + `spawn(50) del(A)` no DM.
			Tween tw = CreateTween().SetParallel();
			tw.TweenProperty(s, "position", s.Position * 3.4f, 0.75);
			tw.TweenProperty(s, "modulate:a", 0f, 0.75);
			tw.Chain().TweenCallback(Callable.From(s.QueueFree));
		}
	}

	// ======================================================================================
	//  OS TRES EFEITOS DO ENCHIMENTO -- ver `Efeito.AnelDeChoque`, `ClaraoDeTela` e
	//  `DescargaNoCeu` no Core, que e onde esta escrito o que e porte e o que e invencao.
	// ======================================================================================

	/// <summary>
	/// QUE TAMANHO TEM O ANEL, em tiles. TRES, e agora o numero e do DM e nao de uma coincidencia:
	/// `createShockwavemisc(loc,3)` -- o raio com que os quatro procs de surto (`SSj4FP` e irmaos)
	/// abrem, e o mesmo do `BeastUp` (`Mystic.dm:142`).
	///
	/// A justificativa ANTIGA era outra e morreu neste passe: "a mesma conta do `TilesEmVolta`, o
	/// sorteio das pedras cobre 3 tiles pra cada lado". O sorteio das pedras deixou de ser 3 (ele e
	/// medido da camera agora, ver <see cref="_alcanceX"/>), e amarrar o anel a ele teria dobrado o
	/// anel de tabela -- num efeito de que o dono nao reclamou. Sao duas coisas diferentes: a pedra e
	/// o CHAO que se solta, e o anel e o AR sendo empurrado.
	///
	/// Escrever o pixel envelheceria calado no dia em que o tile mudasse -- o mesmo motivo pelo qual
	/// o <see cref="Cinematicas.TilesDoTremorCheio"/> tambem esta em tiles.
	/// </summary>
	private const float RaioDoAnelEmTiles = 3f;

	/// <summary>
	/// O ANEL DE CHOQUE -- e ele e o `CombatFx.Onda` do combate, sem uma linha de desenho nova.
	///
	/// ============================ POR QUE A COR E A DA AURA, E NAO A DOS RAIOS ============================
	/// Os feixes de chao usam <see cref="_corRaios"/> porque feixe E raio (ver `Feixes`). O anel nao e
	/// eletricidade: e o ar sendo empurrado pelo poder que acabou de ser solto, entao ele e da cor do
	/// PODER. No SSJ2 os oito feixes saem azuis e o anel sai dourado, e isso e o certo -- sao duas
	/// coisas diferentes acontecendo no mesmo instante.
	/// ================================================================================================
	///
	/// SEMITRANSPARENTE de proposito. Opaco ele vira um disco no chao; a 0,75 ele le como deslocamento
	/// de ar, que e o que a onda do DM (`createShockwavemisc`) contava.
	/// </summary>
	private void Anel()
	{
		AneisDeTeste++;
		Vector2 pes = _alvo.GlobalPosition + new Vector2(0, MoveRules.FeetOffsetY);
		CombatFx.Onda(_chaoRaiz, pes, RaioDoAnelEmTiles * ZoneCollision.TileSize,
					  new Color(_corAura.R, _corAura.G, _corAura.B, 0.75f), duracao: 0.5);
	}

	// ============================ AQUI MORAVA O `Cascalho()` ============================
	// Ele soltava tres pontos de `PoeiraDeEstrago` por beat -- 73 beats em 22 cenas, 219 despejos --,
	// e foi cortado pelo dono: *"vc colocou uns efeitos de particula nas cinematicas q parecem q tem
	// uns quadrados marrons caindo e criando uma fumaca parecendo q quebrou uma parede ou objeto,
	// TIRE esse efeito"*.
	//
	// A DESCRICAO BATE COM O CODIGO DE LA, linha por linha (`PoeiraDeEstrago.cs:155,164`): a textura
	// e um `Quadrado(3, pedra)` -- 3x3 px de cor chapada --, a `Gravity` e `(0, 420)` (cai), a cor
	// padrao e `TerraPadrao` = marrom, e o node leva dois sistemas de fumaca junto.
	//
	// **A `PoeiraDeEstrago` NAO foi tocada.** Ela e do estrago de cenario, tem dono proprio e e usada
	// em combate; o defeito era esta classe estar chamando ela. O buraco que ela preenchia (as cenas
	// longas com tela parada) e da PEDRA agora, do inicio ao fim -- ver o bloco do CHAO SOLTO.
	// ==================================================================================

	/// <summary>
	/// UM RAIO CAI DO CEU EM CIMA DE QUEM ESTA VIRANDO.
	///
	/// Uma chamada, e o resto ja existia: <see cref="Iluminacao.Raio"/> -> `ClimaNaTela.Estourar`
	/// desenha o risco em zigue-zague, acende o ceu com forca que cai com a distancia e agenda o
	/// TROVAO pelo tempo que o som leva pra chegar. E o canal da tempestade, reusado.
	///
	/// ============================ SO EM PLANETA, E A PERGUNTA JA ESTAVA RESPONDIDA ============================
	/// Nao ha ceu pra rachar no espaco, dentro de uma nave ou na Sala do Tempo -- e o `_noPlaneta` ja
	/// sabe disso desde o `_Ready`, pela mesma leitura que o tremor de longe usa. Uma segunda pergunta
	/// aqui seria a mesma decisao anotada em dois lugares.
	///
	/// A SEMENTE E SORTEADA e nao viaja: ela existe pra dois jogadores olhando pro MESMO raio verem o
	/// mesmo risco, e isso e do canal do servidor. A cena e do cliente (o mesmo argumento do sorteio
	/// dos tiles das pedras) -- duas telas veem zigue-zagues diferentes, e isso nao e defeito.
	/// ====================================================================================================
	/// </summary>
	private void Descarga()
	{
		if (!_noPlaneta) return;
		DescargasDeTeste++;
		Cair(_alvo.GlobalPosition);
	}

	/// <summary>
	/// CAI UM RAIO NAQUELE PONTO. O unico caminho de desenho de descarga desta classe.
	///
	/// Extraido porque passaram a existir DOIS pedidos de raio -- o pulso do beat
	/// (<see cref="Descarga"/>) e a tempestade da estreia (<see cref="TocarTempestade"/>) -- e duas
	/// chamadas iguais a `Iluminacao.Raio` seriam duas oportunidades de uma delas envelhecer sozinha
	/// no dia em que o canal do clima mudar de assinatura. Os CONTADORES e que continuam separados: e
	/// a bancada que precisa distinguir "o roteiro pediu" de "o estado da cena pediu".
	///
	/// A SEMENTE E SORTEADA AQUI e nao viaja pela rede -- ver <see cref="Descarga"/> sobre isso.
	/// </summary>
	private void Cair(Vector2 onde) =>
		Mundo()?.GetNodeOrNull<Iluminacao>("Iluminacao")?.Raio(onde, GD.Randf() * 1000f);

	// ======================================================================================
	//  A TEMPESTADE DA ESTREIA -- e ela e um ESTADO DA CENA, como a pedra e como o piscar
	// ======================================================================================
	// O dono: *"o ssj1 na cinematica da primeira vez, deveria fazer raios cairem durante TODA a
	// cinematica na regiao q o personagem esta se transformando"*.
	//
	// QUEM RESPONDE "esta cena?" E O CORE, numa linha (`Cinematica.OCeuDescarrega`): so o `ssj1` e so
	// a versao cheia. O tocador nao sabe o nome de forma nenhuma, e nao e ele que decide quando a
	// estreia acontece -- essa e a mesma divisao que o `OChaoSeSolta` e o `PiscaCabelo` ja usam.
	//
	// NAO HA DESENHO NOVO: e o `Iluminacao.Raio` -> `ClimaNaTela.Estourar` da tempestade, que ja
	// desenha o risco em zigue-zague, acende o ceu com forca que cai com a distancia e agenda o trovao
	// pelo tempo que o som leva pra chegar. O beat `Efeito.DescargaNoCeu` ja o usava como pulso; aqui
	// ele vira cadencia.

	/// <summary>ONDE a tempestade cai, congelado no comeco da cena. Ver <see cref="MontarATempestade"/>.</summary>
	private Vector2 _centroDaTempestade;

	/// <summary>O buraco do meio, em pixels. Ver <see cref="MontarATempestade"/>.</summary>
	private float _mioloDaTempestade;

	/// <summary>Quando cai o proximo. Tempo de cena.</summary>
	private double _proximoRaio;

	private readonly List<Vector2> _pontosDeRaio = [];

	/// <summary>
	/// MEDE A REGIAO ONDE O CEU VAI DESCARREGAR -- centro, buraco do meio e o primeiro prazo. Uma vez,
	/// no `_Ready`.
	///
	/// ============================ O CENTRO E CONGELADO, E ISSO NAO E DETALHE ============================
	/// O `Devolver` solta o corpo no beat que assume (25,0 s) e a cena ainda tem 1,4 s de cauda: raio
	/// perguntando a posicao do corpo a cada queda PERSEGUIRIA o jogador que ja saiu andando. E o mesmo
	/// defeito -- e a mesma correcao -- do <see cref="_celulaDoChao"/> das pedras.
	///
	/// Nos PES e nao no centro do sprite: `MoveRules.FeetOffsetY` e o mesmo desconto que o `Plantar`
	/// (cratera, fumaca) e o chao solto ja usam. O raio atinge o CHAO, e o chao esta nos pes.
	///
	/// ============================ O BURACO DO MEIO SAI DO BONECO ============================
	/// *"na regiao"* e uma area EM VOLTA, e o `Descarga` do beat ja cobre o caso de cair em cima (ele
	/// atinge o proprio corpo). Aqui o pe do risco tem que sobrar do sprite, senao dezessete descargas
	/// caem todas dentro do mesmo boneco e a "regiao" some.
	///
	/// O numero e o LADO DA FOLHA DO CORPO (`CharacterVisual.TamanhoDoQuadro`) -- a mesma medida, da
	/// mesma fonte, que ja faz o buraco das pedras. Num boneco de 32 e um tile inteiro de folga (meio
	/// tile ja tiraria o pe de cima do sprite; um tile e a primeira distancia em que a descarga LE como
	/// estando ao lado dele); num macaco de 96 sao tres, sozinho.
	///
	/// O TETO EM METADE DO ALCANCE existe pra que o anel nunca se inverta: uma folha maior que o
	/// proprio `RaioDoTremorCheio` daria `miolo > raio` e um sorteio negativo, que e um defeito que
	/// nao acusa -- ele so poe o raio do lado errado do personagem.
	/// ================================================================================================
	/// </summary>
	private void MontarATempestade()
	{
		if (!_cena.OCeuDescarrega) return;

		_centroDaTempestade = _alvo.GlobalPosition + new Vector2(0, MoveRules.FeetOffsetY);

		float lado = CharacterVisual.TamanhoDoQuadro(
			_alvo.GetNodeOrNull<CharacterVisual>("Visual")?.FolhaDoCorpo).X;
		if (lado <= 0) lado = ZoneCollision.TileSize;
		_mioloDaTempestade = Math.Min(lado, Cinematicas.RaioDoTremorCheio * 0.5f);

		// O PRIMEIRO JA SORTEADO, e nao em `_t = 0`: no DM tambem nao cai raio antes do primeiro
		// segundo (`spawn(rand(10,150))`, `SSJCinematic.dm:51-52`), e uma descarga no quadro de
		// abertura disputaria a tela com o `rockmoving` e o tremor que abrem a cena.
		_proximoRaio = IntervaloDoRaio();
	}

	/// <summary>
	/// QUANTO O CEU SEGURA ATE O PROXIMO -- o sorteio do Core, ver <see cref="Cinematicas.DescargaMinima"/>.
	/// </summary>
	private static double IntervaloDoRaio() =>
		Cinematicas.DescargaMinima
		+ GD.Randf() * (Cinematicas.DescargaMaxima - Cinematicas.DescargaMinima);

	/// <summary>
	/// FAZ CAIR O QUE VENCEU. Chamado todo quadro, do primeiro segundo da cena ao ultimo.
	///
	/// ============================ DO INICIO AO FIM, E SEM UM BEAT SEQUER ============================
	/// O prazo nao para no `Assumir`: o dono disse *"durante TODA a cinematica"*, e a cauda de 1,4 s da
	/// cena e a poeira da cratera baixando -- uma ultima descarga ali fecha o acontecimento em vez de
	/// deixa-lo terminar no silencio. Quem para o efeito e o `_Process` deixando de rodar quando a cena
	/// morre, que e o mesmo guarda das pedras.
	///
	/// `while` E NAO `if` pelo mesmo motivo do chao solto: a bancada bombeia o relogio a mao em passos
	/// de 0,1 s, mas um quadro engasgado em jogo pode passar de 2,0 s -- e ai o prazo vencido teria que
	/// esperar o quadro seguinte, e o seguinte, acumulando atraso que nunca se paga. Ele TERMINA sempre:
	/// cada volta soma pelo menos <see cref="Cinematicas.DescargaMinima"/> a um alvo que nao anda.
	/// ============================================================================================
	/// </summary>
	private void TocarTempestade()
	{
		// AS DUAS PERGUNTAS, e nenhuma delas e redundante: a primeira e do ROTEIRO (so a estreia do
		// SSJ1 tem tempestade) e a segunda e do LUGAR (nao ha ceu pra rachar no espaco, dentro de uma
		// nave ou na Sala do Tempo). O `Descarga` do beat faz a segunda pela mesma razao.
		if (!_cena.OCeuDescarrega || !_noPlaneta) return;

		while (_t >= _proximoRaio)
		{
			_proximoRaio += IntervaloDoRaio();

			RaiosDaEstreiaDeTeste++;
			float ang = GD.Randf() * Mathf.Tau;
			// UNIFORME NO RAIO e nao na AREA, de proposito: sortear a area espalharia a maioria das
			// descargas na borda do circulo (ha mais area longe do centro que perto), e o pedido e
			// *"na regiao q o personagem esta se transformando"* -- ele e o centro do acontecimento,
			// nao o furo de uma rosca.
			float d = _mioloDaTempestade
					+ GD.Randf() * (Cinematicas.RaioDoTremorCheio - _mioloDaTempestade);
			Vector2 onde = _centroDaTempestade + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * d;

			_pontosDeRaio.Add(onde);
			Cair(onde);
		}
	}

	/// <summary>Quantos raios da tempestade cairam nesta cena. Pra bancada. Ver <see cref="TocarTempestade"/>.</summary>
	public int RaiosDaEstreiaDeTeste { get; private set; }

	/// <summary>Onde cada um caiu, em coordenada de mundo. Pra bancada medir a regiao.</summary>
	public Vector2[] PontosDeRaioDeTeste => [.. _pontosDeRaio];

	/// <summary>O centro congelado da regiao. Pra bancada -- ver <see cref="MontarATempestade"/>.</summary>
	public Vector2 CentroDaTempestadeDeTeste => _centroDaTempestade;

	/// <summary>O buraco do meio, em pixels. Pra bancada.</summary>
	public float MioloDaTempestadeDeTeste => _mioloDaTempestade;

	/// <summary>
	/// EM QUE CAMADA O CLARAO VIVE.
	///
	/// 1: acima do mundo e da nevoa de altitude (0), abaixo do chat (2), dos menus (3 e 4) e do pause
	/// (20). Empata com o HUD e com o tinte do clima, e nos 0,45 s dele isso e o certo -- um clarao
	/// que respeitasse a barra de vida nao seria um clarao.
	/// </summary>
	private const int CamadaDoClarao = 1;

	/// <summary>Quanto do branco entra na cor da forma, e quanto de tela ele toma, e quanto dura.</summary>
	private const float BrancoDoClarao = 0.7f, AlfaDoClarao = 0.55f;

	/// <inheritdoc cref="BrancoDoClarao"/>
	private const double DuracaoDoClarao = 0.45;

	private ColorRect? _telaDoClarao;

	/// <summary>
	/// O CLARAO QUE LAVA A TELA. Um `ColorRect` de tela cheia que nasce na cor da forma puxada pro
	/// branco e some em meio segundo.
	///
	/// ============================ ELE NAO E BRANCO PURO, E NAO E OPACO ============================
	/// Branco puro a 100% e um corte pra branco -- perde-se o quadro inteiro e, com ele, justamente o
	/// que se queria ver (o cabelo mudando). A 55% de um branco puxado pra cor da forma, o clarao
	/// LAVA a cena sem apaga-la: o Blue estoura azulado, o Ultra Ego roxo, e o personagem continua
	/// visivel por dentro.
	///
	/// ============================ E A FORCA CAI COM A DISTANCIA ============================
	/// Pelo mesmo <see cref="PesoDoTremor"/> do tremor, e nao por uma regra propria: quem esta do
	/// outro lado do planeta ve um lampejo (metade), quem esta fora de um planeta nao ve nada. Uma
	/// segunda regra de alcance envelheceria contra a primeira -- e o modo de falha seria a tela de
	/// alguem no espaco ficando branca por causa de um SSJ3 num planeta qualquer.
	/// ==================================================================================
	///
	/// UM POR CENA, e a regra mora no Core (ver <see cref="Efeito.ClaraoDeTela"/>): so o beat que
	/// ASSUME pode acende-lo. Este metodo REAPROVEITA o node se for chamado de novo em vez de
	/// empilhar dois -- nao pra permitir, mas pra que o pior caso seja um clarao mais forte e nao
	/// duas camadas de tela cheia vazando pelo resto da cena.
	/// </summary>
	private void Clarao()
	{
		float peso = PesoDoTremor();
		if (peso <= 0f) return;
		ClaroesDeTeste++;

		if (_telaDoClarao == null || !IsInstanceValid(_telaDoClarao))
		{
			var camada = new CanvasLayer { Name = "Clarao", Layer = CamadaDoClarao };
			AddChild(camada);
			_telaDoClarao = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore };
			_telaDoClarao.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			camada.AddChild(_telaDoClarao);
		}

		Color c = _corAura.Lerp(Colors.White, BrancoDoClarao);
		_telaDoClarao.Color = new Color(c.R, c.G, c.B, AlfaDoClarao * peso);

		// EXPO OUT: ele cai rapido no comeco e alonga o rabo. Linear le como uma cortina subindo;
		// um estouro de luz apaga quase todo na primeira fracao e deixa um resto no ar.
		_telaDoClarao.CreateTween()
			.TweenProperty(_telaDoClarao, "color:a", 0f, DuracaoDoClarao)
			.SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
	}

	/// <summary>Quantas vezes cada efeito novo disparou nesta cena. Pra bancada.</summary>
	public int AneisDeTeste { get; private set; }

	/// <inheritdoc cref="AneisDeTeste"/>
	public int DescargasDeTeste { get; private set; }

	/// <inheritdoc cref="AneisDeTeste"/>
	public int ClaroesDeTeste { get; private set; }

	private void Plantar(Protocol.Decal tipo, float raio)
	{
		if (Decalques.Instancia is not { } d) return;
		Vector2 onde = GlobalPosition + new Vector2(0, MoveRules.FeetOffsetY);
		if (raio > 0)
			onde += new Vector2(GD.Randf() - 0.5f, GD.Randf() - 0.5f) * raio * 2;
		d.Plantar(tipo, onde, Facing.South);
	}

	/// <summary>
	/// O NOME QUE O BEAT PEDE -> O ARQUIVO. Nulo = nome desconhecido.
	///
	/// ============================ POR QUE NULO E NAO UM SOM QUALQUER ============================
	/// Este switch tinha um `_ => Trilha.Dash` no fim, e ele nao era uma rede: era um DISFARCE. As
	/// cinematicas do SSJ1 e do SSJ2 pedem `Som: "rockmoving"` (o nome do proprio DM, citado no
	/// comentario do beat), o `rockmoving.ogg` esta convertido e importado -- e nao havia caso pra
	/// ele. As duas estreias abriam com o swoosh do dash, e nada dizia que o som pedido nao existia,
	/// porque um som errado toca exatamente como um som certo.
	///
	/// Devolvendo nulo, o nome errado vira SILENCIO + aviso no console -- e, o que importa mais,
	/// vira REPROVA na bancada (`--diagforma` percorre todo `Beat.Som` de todas as cenas). O custo
	/// e um som a menos num caso que hoje nao existe; o ganho e que o caso nao pode voltar calado.
	///
	/// PUBLICO E ESTATICO de proposito: a bancada precisa fazer a mesma pergunta que o jogo faz, e
	/// nao uma tabela paralela que envelheceria em silencio junto com esta.
	/// ========================================================================================
	/// </summary>
	public static string? CaminhoDoSom(string nome) => nome switch
	{
		"powerup" => Trilha.PowerUp,
		"chargeaura" => Trilha.CargaInicio,
		"roar" => Trilha.Rugido,
		"rockmoving" => Trilha.PedrasRolando,
		"dash" => Trilha.Dash,

		// ============================ OS DOIS QUE ENTRARAM COM O ENCHIMENTO DAS CENAS LONGAS ============================
		// `zumbido` e o MESMO arquivo do laco da carga (`aurapowered.wav`, ver `Trilha.CargaLaco`), e
		// nao um som novo: ele existe nas cenas pra encher intervalo com som CONTINUO onde antes so
		// havia estalo. Numa cena de 20 s (e mais ainda numa de 116) tres solavancos secos e mais
		// silencio soam iguais entre si; um zumbido no meio do vao e o que separa a segunda metade da
		// primeira sem precisar de mais um efeito na tela.
		//
		// `explosao` e o `explosion.ogg` -- ver `Trilha.Explosao`, que anota que o DM so o usa em
		// bomba e por que ele aparece no penultimo beat do SSJ3.
		// ============================================================================================================
		"zumbido" => Trilha.CargaLaco,
		"explosao" => Trilha.Explosao,

		// ============================ OS DOIS DIVINOS, QUE ESTAVAM NA PASTA E SEM PORTA ============================
		// `ssg.wav` e `ssb.wav` estao convertidos e com `.import` desde o pipeline e nao tinham um unico
		// leitor em `.cs` -- ver `Trilha.KiDivino` e `Trilha.KiDivinoAzul`. Nao e o mesmo defeito do
		// `rockmoving` (que era pedido pelo beat e devolvido errado pelo `_ =>`): aqui ninguem pedia. E
		// por isso ele e mais silencioso -- um som que ninguem toca nao soa errado, so nao soa.
		// ======================================================================================================
		"ssg" => Trilha.KiDivino,
		"ssb" => Trilha.KiDivinoAzul,

		_ => null,
	};

	private void Som(string nome)
	{
		if (CaminhoDoSom(nome) is not { Length: > 0 } caminho)
		{
			GD.PushWarning($"[cena] beat pede um som que nao existe no resolvedor: '{nome}'");
			return;
		}
		AudioDirector.EfeitoNoLugar(_alvo, caminho, 1.0f);
	}
}
