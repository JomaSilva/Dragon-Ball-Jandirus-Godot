using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Social;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// A FUSAO -- chamar, aceitar, dancar, e virar um so. Porte de
/// `Code/Modules/Magic/Fusion.dm` com os pedidos novos do dono.
///
/// As regras puras (energia, dreno, BP, portoes, nome) moram no <see cref="Fusao"/>. Aqui mora o
/// gesto: quem convidou quem, o pendente vivo, o quick time event, o corpo que some, o que volta
/// quando a energia acaba.
///
/// ============================ A CORRENTE INTEIRA ============================
///   METAMORO:  `fus_danca` (alvo na frente) -> convite pendente no OUTRO -> `fus_sim`
///              -> os DOIS dancam o quick time event -> funde (bem ou estragada).
///   POTARA:    item `jogar` (alvo MARCADO) -> convite pendente no OUTRO -> `fus_sim`
///              -> funde na hora, SEM quick time event.
///
/// **QUEM CONVIDOU CONTROLA** nos dois casos -- e o `usr.Fuse(M, ...)` do DM (`Fusion.dm:569`,
/// `:729`), onde o iniciador vira `Keeper`. O outro vira passageiro, e o controle troca de mao pelo
/// `Pass Fusion Control` (`Fusion.dm:513`), portado aqui como `fus_passar`.
/// ===========================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// O ESTADO
	// =====================================================================
	/// <summary>
	/// O CONVITE NA MESA DE ALGUEM (a chave do dicionario e o id de QUEM RECEBEU).
	///
	/// VIVO, nao vai pro disco -- mesma regra do `PedidoDeAmizade` e do `PedidoDeLicao`: um convite
	/// e um gesto do momento.
	/// </summary>
	private sealed record PedidoDeFusao(int DeQuem, string Nome, TipoDeFusao Tipo, long Ate);

	private readonly Dictionary<int, PedidoDeFusao> _pedidosDeFusao = [];

	/// <summary>
	/// UMA FUSAO DE PE. Guarda TUDO o que foi mexido, pra o desfazer nao precisar recalcular nada.
	///
	/// ============================ GUARDE O QUE APLICOU, NUNCA RECALCULE PRA DESFAZER ============================
	/// E a mesma disciplina do `startbuff`/`stopbuff` das 28 tecnicas, e aqui ela custa caro se for
	/// ignorada: entre fundir e separar o jogador pode ter treinado (o BP sobe), aprendido skill,
	/// ganhado stat. Recalcular `(A+B)*2` na hora de separar devolveria um numero DIFERENTE do que
	/// foi somado, e a diferenca ficaria pra sempre no corpo -- pra mais ou pra menos.
	/// ========================================================================================================
	/// </summary>
	private sealed class FusaoAtiva
	{
		public required ServerPlayer Dono;          // o Keeper do DM: quem convidou, quem controla
		public required ServerPlayer Passageiro;    // o Loser do DM: quem aceitou
		public TipoDeFusao Tipo;
		public bool Estragada;

		public double Energia, EnergiaMax;

		/// <summary>O quanto foi somado ao `FuseBuff` do dono. Ver o cabecalho da classe.</summary>
		public double DeltaDeBp;

		public string NomeDaFusao = "";

		/// <summary>De onde o passageiro saiu, pra ele ter pra onde voltar (`LoserBackupLoc`).</summary>
		public ZoneKey ZonaDoPassageiro;
		public Vec2 PosDoPassageiro;

		/// <summary>As skills que vieram do passageiro e que o dono NAO tinha. Saem no fim.</summary>
		public readonly List<string> SkillsEmprestadas = [];

		/// <summary>
		/// QUAIS MEMBROS FALTAVAM NO CORPO DO DONO ANTES DE FUNDIR -- o `KeeperLoppedTypes` do DM
		/// (`Fusion.dm:108`), e ele existe pra o <see cref="Separar"/> poder re-decepar na saida.
		///
		/// ============================ E O QUE ELE IMPEDE ESTA ESCRITO NO PROPRIO DM ============================
		/// O comentario do `fusion_snapshot_lopped` (`Fusion.dm:41`) e literal: *"no free regen
		/// exploit"*. Sem esta lista, a fusao seria a cura de membro mais barata do jogo -- funde,
		/// separa, braco de volta --, e a amputacao permanente deixaria de ser permanente.
		///
		/// **Nao e `List` a toa**: ele e reescrito no <see cref="PassarOControle"/>, porque quem passa
		/// a ter corpo em cena e OUTRA pessoa, com outros membros faltando (`Fusion.dm:402-403`).
		/// </summary>
		public List<string> MembrosQueFaltavam = [];

		/// <summary>Os oito stats crus do dono ANTES da fusao, na ordem do `StatsDe`.</summary>
		public double[] StatsDoDono = [];

		/// <summary>Quando o dreno cobrou pela ultima vez (ms de relogio real).</summary>
		public long UltimoDreno;
	}

	private readonly List<FusaoAtiva> _fusoes = [];

	/// <summary>Quem esta numa fusao AGORA: id -> a fusao. Serve de trava e de roteador.</summary>
	private readonly Dictionary<int, FusaoAtiva> _fundidos = [];

	// ============================ A RECARGA DE 1 h NAO MORA MAIS AQUI ============================
	// Havia um `Dictionary<int, long> _recargaDeFusao` neste arquivo, com o ID DE SESSAO por chave, e
	// um comentario admitindo a divida: *"NAO PERSISTE, e no DM persiste"*. Ela foi paga -- o carimbo
	// virou `Fighter.fusion_cooldown_until`, exatamente como no original (`Fusion.dm:28`, um `mob/var`
	// SEM `tmp/`), e viaja no save de graca porque o `CharacterSave` serializa a ficha inteira.
	//
	// O dicionario foi DELETADO junto com a linha do logout que o limpava: com a chave sendo o corpo e
	// nao a sessao, nao ha id reusado pra herdar espera de ninguem -- e nao ha Alt+F4 que zere a unica
	// coisa que impede fundir sem parar. Ver `NaRecargaDeFusao` e `CobrarARecargaDeFusao`.
	// ========================================================================================

	// =====================================================================
	// O QUICK TIME EVENT DA DANCA
	// =====================================================================
	/// <summary>
	/// A DANCA EM CURSO -- os dois lados, as letras e o placar.
	///
	/// ============================ POR QUE ELA NAO E UM `Embate` ============================
	/// O motor de embate ja existe (ZanzoClash e colisao de ki) e este arquivo REUSA tudo o que
	/// nele e generico: o alfabeto (<see cref="SortearLetraDeEmbate"/>), o pacote de letra
	/// (<see cref="MandarTecla"/>), o veredito (<see cref="Julgou"/>), o placar, o fim, o piso de
	/// cadencia e ate a resposta do corpo sem teclado. O que NAO se reusa e a estrutura, e por uma
	/// razao de desenho: o `Embate` e um CABO DE GUERRA -- quem faz mais pontos vence e o outro
	/// perde. A danca nao tem vencedor: **os dois ganham juntos ou os dois saem estragados**, e
	/// enfiar isso num placar de soma zero exigiria explicar o que significa "empate" numa fusao.
	///
	/// Sao ~40 linhas de estado proprio contra reescrever a regra de vitoria do motor inteiro.
	/// =====================================================================================
	/// </summary>
	private sealed class DancaDeFusao
	{
		public required ServerPlayer A, B;   // A convidou
		public char LetraA, LetraB;
		public long PrazoA, PrazoB;
		public int AcertosA, AcertosB;
		public bool FalhouA, FalhouB;

		/// <summary>Quando o corpo sem teclado "aperta". Ver <see cref="ResponderPelaMaquina"/>.</summary>
		public long RespondeA, RespondeB;

		public long Acaba;

		/// <summary>
		/// JA RESOLVIDA -- a trava contra resolver duas vezes.
		///
		/// Ela nao e zelo: num unico tique o corpo sem teclado pode fechar o ultimo passo (e chamar o
		/// <see cref="TalvezAcabarADanca"/>) e, logo abaixo no mesmo laco, o prazo pode vencer e
		/// chamar de novo. Sem esta linha o segundo caminho chamaria o `Fundir` uma segunda vez -- e
		/// o resultado seria dois pacotes de fim e um `FuseBuff` somado em dobro.
		/// </summary>
		public bool Resolvida;
	}

	private readonly List<DancaDeFusao> _dancas = [];
	private readonly Dictionary<int, DancaDeFusao> _dancando = [];

	/// <summary>Este corpo esta no meio da danca? Usado pelos outros dois embates como trava.</summary>
	private bool EstaDancando(int id) => _dancando.ContainsKey(id);

	// =====================================================================
	// A CINEMATICA -- o intervalo entre "vai fundir" e "fundiu"
	// =====================================================================
	/// <summary>
	/// UMA CINEMATICA DE FUSAO EM CURSO: os dois corpos ainda separados, o relogio correndo, e a
	/// fusao ainda **nao existindo**.
	///
	/// ============================ POR QUE ELA EXISTE, EM UMA LINHA ============================
	/// O dono: *"a fusao so EXISTE no fim da cena -- pendure no beat de climax do motor que ja
	/// existe"*, e ele ja cobrou duas vezes efeito de fim caindo no comeco (a cratera no meio da
	/// cinematica, o corpo do bio trocando antes da hora). Antes deste registro o `Fundir` era
	/// chamado na mesma linha em que a danca resolvia e em que a Potara era aceita -- ou seja, no
	/// instante ZERO da cena. Este objeto e o intervalo entre as duas coisas, e ele e **estado de
	/// servidor**: quem funde e o servidor, e uma cena de cliente nao pode ser a autoridade sobre
	/// quando dois personagens viram um.
	///
	/// ============================ E O DM TEM ESTE MESMO INTERVALO ============================
	/// Nao e invencao: `Fusion.dm:678-683` faz `sleep(40)` entre os dois andarem um pro outro e o
	/// `Fuse()` -- e neste port aquela espera virou o PUXAO (ver `ComecarOPuxaoDeFusao`), enquanto a
	/// cena passou a durar o que o clarao dura. O proprio `Fuse()` segura mais `sleep(5)` antes de selar
	/// o passageiro,
	/// com o comentario do original dizendo por que a janela existe: *"defused (e.g. KO) during the
	/// brief setup window"*. Ou seja, la tambem ha um trecho em que a fusao foi decidida, os dois
	/// corpos ainda estao no mapa, e **cair cancela tudo**.
	///
	/// ============================ O QUE ELA GUARDA E O QUE ELA NAO GUARDA ============================
	/// Guarda so o que o `Fundir` vai precisar (os dois corpos, o tipo, se a coreografia falhou) e
	/// dois instantes. **Nao guarda nada do CORPO** -- nem stat, nem BP, nem skill: nada foi mexido
	/// ainda, e e exatamente esse o ponto. Abortar e tirar isto da lista e soltar os dois; nao ha o
	/// que desfazer, porque nao ha o que ter sido feito.
	/// ==========================================================================================
	/// </summary>
	private sealed class CenaDeFusao
	{
		public required ServerPlayer Dono, Passageiro;
		public TipoDeFusao Tipo;
		public bool Estragada;

		/// <summary>
		/// QUANDO O `Fundir` ACONTECE (ms de relogio real) -- o beat que ASSUME da cena, e nao o fim
		/// dela. Ver <c>Cinematica.SegundosAteAVirada</c>.
		/// </summary>
		public long Funde;

		/// <summary>QUANDO A CENA ACABA (ms). So serve pra tirar isto da lista.</summary>
		public long Acaba;

		/// <summary>JA FUNDIU (a virada passou) -- a trava contra fundir duas vezes, irma da
		/// <see cref="DancaDeFusao.Resolvida"/> e pelo mesmo motivo escrito la.</summary>
		public bool Fundiu;
	}

	private readonly List<CenaDeFusao> _cenasDeFusao = [];

	/// <summary>
	/// QUEM ESTA NO MEIO DE UMA CINEMATICA DE FUSAO: id -> a cena. Serve de TRAVA, e essa e a razao
	/// de ele existir ao lado da lista.
	///
	/// Sem ele haveria uma janela de 4 segundos em que os dois nao estao fundidos (`_fundidos` vazio),
	/// nao estao dancando (`_dancando` vazio) e mesmo assim ja tem uma fusao a caminho -- e nela caberia
	/// um segundo convite aceito, uma segunda danca, ou a mesma pessoa entrando em duas fusoes. Ver
	/// <see cref="OcupadoPorFusao"/>, que e por onde a trava e lida.
	/// </summary>
	private readonly Dictionary<int, CenaDeFusao> _emCenaDeFusao = [];

	// =====================================================================
	// O PUXAO -- os segundos entre "aceitou" e "encostaram" (so a Potara)
	// =====================================================================
	/// <summary>
	/// UM PUXAO EM CURSO: os dois corpos andando um pro outro, com o input dos dois desligado, e a
	/// fusao ainda **nao tendo nem cena**.
	///
	/// ============================ ELE E O PORTE DE UM `while` QUE FALTAVA INTEIRO ============================
	/// `Code/Combat/Skills/Ki/Fusion/Potara_Fusion.dm:122-132`:
	///
	///     C.mob.AlterInputDisabled(1)
	///     B.AlterInputDisabled(1)
	///     while(get_dist(C.mob,B) &gt; 1 &amp;&amp; C.mob.z == B.z)
	///         step_to(B,C.mob,0,32)      B.AlignToTile()
	///         step_to(C.mob,B,0,32)      C.mob.AlignToTile()
	///         sleep(world.tick_lag)
	///     sleep(1)
	///     C.mob.AlterInputDisabled(-1)   B.AlterInputDisabled(-1)
	///
	/// E o pedido do dono, palavra por palavra: *"na potara quando ela comecar eles sao puxados um pro
	/// lado do outro e QUANDO SE ENCOSTAREM a cinematica comeca"*. Ou seja o puxao e uma etapa ANTES da
	/// cinematica, com fim proprio (*"encostaram"*), e nao um pedaco dela.
	///
	/// ============================ E ELE E UM ESTADO DE SERVIDOR, COMO A CENA ============================
	/// Mesmo desenho e mesmo argumento do <see cref="CenaDeFusao"/>: quem escreve `Pos` e o servidor, e
	/// **nada foi aplicado ainda** -- nem poder, nem stat, nem skill, nem selo. Abortar e tirar duas
	/// linhas de dicionario e soltar os dois; nao ha o que desfazer porque nao ha o que ter sido feito.
	///
	/// ============================ O QUE ELE GUARDA, E POR QUE A "MELHOR" E A CHAVE ============================
	/// <see cref="MelhorDistancia"/> e a MENOR distancia ja alcancada, e ela e o que torna o fim um
	/// FATO em vez de um relogio: enquanto ela encurtar, o puxao continua o tempo que precisar; parou
	/// de encurtar (parede, um deles preso, um deles arremessado pro outro lado) e a fusao **nao
	/// comeca**. Ver <see cref="Fusao.SegundosSemAproximarParaDesistir"/>, que explica por que o `while`
	/// do original -- que nao tem saida nenhuma -- nao podia ser portado como esta.
	/// ======================================================================================================
	/// </summary>
	private sealed class PuxaoDeFusao
	{
		public required ServerPlayer Dono, Passageiro;
		public TipoDeFusao Tipo;

		/// <summary>A menor distancia (em PIXELS) ja alcancada. Ver o cabecalho da classe.</summary>
		public double MelhorDistancia = double.MaxValue;

		/// <summary>Desde quando ela nao melhora (ms de relogio real).</summary>
		public long SemMelhorarDesde;
	}

	private readonly List<PuxaoDeFusao> _puxoesDeFusao = [];

	/// <summary>
	/// QUEM ESTA SENDO PUXADO AGORA: id -> o puxao. Serve de TRAVA pela mesma razao que o
	/// <see cref="_emCenaDeFusao"/> serve -- ver <see cref="OcupadoPorFusao"/>.
	/// </summary>
	private readonly Dictionary<int, PuxaoDeFusao> _sendoPuxadoPraFusao = [];

	/// <summary>
	/// ESTE CORPO JA ESTA COMPROMETIDO COM UMA FUSAO? -- fundido, dancando, **sendo puxado** ou no
	/// meio da cena.
	///
	/// As quatro perguntas juntas, num lugar so, porque as quatro respondem a MESMA coisa pra quem vai
	/// decidir se um convite pode fechar: *"da pra comecar outra fusao com este aqui?"*. Elas estavam
	/// escritas em duas expressoes iguais (a do convite e a da avaliacao), a terceira chegou com a cena
	/// e a quarta com o puxao -- e o modo de falha e sempre o classico: uma das copias lembraria da
	/// fase nova e a outra nao.
	/// </summary>
	private bool OcupadoPorFusao(int id) =>
		_fundidos.ContainsKey(id) || _dancando.ContainsKey(id) || _emCenaDeFusao.ContainsKey(id)
		|| _sendoPuxadoPraFusao.ContainsKey(id);

	// =====================================================================
	// OS VERBOS
	// =====================================================================
	/// <summary>
	/// Os verbs da fusao, pelo mesmo cano de todo o resto (`C2S.Habilidade`, um id de texto).
	/// </summary>
	private bool UsarVerboDeFusao(ServerPlayer pl, string id)
	{
		switch (id)
		{
			case "fus_danca": ConvidarParaADanca(pl); return true;
			case "fus_namek": ConvidarParaAFusaoNamekuseijin(pl); return true;
			case "fus_sim": ResponderAoConvite(pl, aceitou: true); return true;
			case "fus_nao": ResponderAoConvite(pl, aceitou: false); return true;
			case "fus_passar": PassarOControle(pl); return true;
			default: return false;
		}
	}

	// =====================================================================
	// 1. O CONVITE DA METAMORO
	// =====================================================================
	/// <summary>
	/// `Fusion_Dance` (`Fusion.dm:711`): convida quem esta na frente.
	///
	/// O DM abre `input("Who?") in oview(1)` -- um menu com os corpos colados. Aqui quem escolhe e o
	/// <see cref="AlvoNaFrente"/>, o mesmo cone do soco, do ensino e do discipulado: e o precedente
	/// vivo deste port pra "quem esta na minha frente", e uma segunda resposta pra mesma pergunta
	/// seria a duplicata de sempre.
	/// </summary>
	private void ConvidarParaADanca(ServerPlayer pl)
	{
		ServerPlayer? outro = AlvoNaFrente(pl);
		if (outro == null) { Avisar(pl, "nao ha ninguem na sua frente."); return; }
		Convidar(pl, outro, TipoDeFusao.Danca);
	}

	/// <summary>
	/// `Namekian_Fusion` (`Fusion.dm:549-569`): a fusao PERMANENTE dos Namekuseijin, e ela e a unica
	/// que nunca se desfaz.
	///
	/// ============================ ELA EXISTIA NO MOTOR E NAO TINHA PORTA ============================
	/// <see cref="TipoDeFusao.Namek"/> ja governava quatro coisas neste arquivo -- energia zero
	/// (permanente), roupa nenhuma, o `return` do <see cref="FusaoAoCair"/> e o pulo do dreno --, e
	/// **nenhuma linha de producao chamava `Convidar` com ela**. Os unicos chamadores eram a Danca e a
	/// Potara; o resto era ramo morto que so a bancada tocava. Este metodo e a porta que faltava.
	///
	/// ============================ ELA NAO PEDE SKILL, E ISSO E DO DM ============================
	/// O verb do original nao consulta livro nenhum: ele mora num `obj/Namekian_Fusion` e num
	/// `mob/keyable/verb`, e os unicos portoes sao **as duas racas** (`:556-557`) mais os que toda
	/// fusao tem (ja fundido, recarga). Ver `Fusao.Avaliar`, onde o portao racial mora.
	///
	/// O ALVO E O DA FRENTE, como na Danca -- o `oview(1)` do original (`:553`) e um menu com quem
	/// esta colado, e o <see cref="AlvoNaFrente"/> e o precedente vivo deste port pra isso.
	/// ========================================================================================
	/// </summary>
	private void ConvidarParaAFusaoNamekuseijin(ServerPlayer pl)
	{
		ServerPlayer? outro = AlvoNaFrente(pl);
		if (outro == null) { Avisar(pl, "nao ha ninguem na sua frente."); return; }
		Convidar(pl, outro, TipoDeFusao.Namek);
	}

	/// <summary>O typepath da skill da Danca -- `/datum/skill/rank/Fusion_Dance` (`skills.json`).</summary>
	private const string PathDaDanca = "/datum/skill/rank/Fusion_Dance";

	private bool SabeDancar(ServerPlayer p) => p.Livro?.Sabe(PathDaDanca) == true;

	/// <summary>
	/// O CONVITE, os dois tipos por uma porta so. Ele avalia, poe o pendente na mesa do outro e
	/// avisa os dois -- e nada mais acontece ate a resposta.
	/// </summary>
	private void Convidar(ServerPlayer pl, ServerPlayer outro, TipoDeFusao tipo)
	{
		long agora = NowMs();

		RecusaDeFusao r = AvaliarOConvite(pl, outro, tipo, agora);

		if (r != RecusaDeFusao.Pode) { Avisar(pl, PorQueNaoFunde(r, tipo, pl, outro, agora)); return; }

		_pedidosDeFusao[outro.Id] = new PedidoDeFusao(pl.Id, pl.Name, tipo,
			agora + (long)(Fusao.PrazoDoConviteSegundos * 1000));

		string oque = tipo switch
		{
			TipoDeFusao.Potara => "os brincos Potara",
			TipoDeFusao.Namek => "a fusao Namekuseijin",
			_ => "a Danca da Fusao",
		};

		ConvidarContinua(pl, outro, tipo, oque);
	}

	/// <summary>
	/// AS PERGUNTAS DO CONVITE, num lugar so.
	///
	/// ============================ POR QUE ELE SAIU DE DENTRO DO `Convidar` ============================
	/// O <see cref="Convidar"/> so sabe dizer SIM ou NAO (ele avisa o jogador e volta), e quem
	/// diagnostica um "nao" precisa do MOTIVO. A `--diagfotofusao` prova o portao da Metamoro pelas duas
	/// metades -- de longe recusa, colado aceita -- e a metade que reprovava nao tinha como dizer se
	/// reprovou por distancia, por skill ou por poder desigual: a bancada anunciava uma coisa e podia
	/// estar medindo outra.
	///
	/// **Nao ha copia nova de regra aqui**: e o mesmo `Fusao.Avaliar` com os mesmos argumentos, so que
	/// nomeado. O <see cref="Convidar"/> passou a chamar este metodo.
	/// ============================================================================================
	/// </summary>
	private RecusaDeFusao AvaliarOConvite(ServerPlayer pl, ServerPlayer outro, TipoDeFusao tipo,
										  long agora)
	{
		return Fusao.Avaliar(
			tipo,
			convidaTemAssinatura: EhPessoa(pl), convidadoTemAssinatura: EhPessoa(outro),
			mesmaPessoa: pl == outro,
			// AS TRES TRAVAS NUM LUGAR SO -- ver `OcupadoPorFusao`. A terceira (a cinematica em
			// curso) entrou com a cena e e a que faltava: entre "vai fundir" e "fundiu" passam 4
			// segundos em que nenhum dos dois esta fundido nem dancando.
			algumJaFundido: OcupadoPorFusao(pl.Id) || OcupadoPorFusao(outro.Id),
			algumNaRecarga: NaRecargaDeFusao(pl, agora) || NaRecargaDeFusao(outro, agora),
			algumCaido: pl.Ficha.KO || pl.Ficha.dead || outro.Ficha.KO || outro.Ficha.dead,
			mesmaZona: pl.Zone.Hash == outro.Zone.Hash,
			// A DISTANCIA NA MOEDA DO BYOND (`get_dist`, tabuleiro), e nao a linha reta que estava aqui.
			// Com o portao da Danca valendo UM tile, o vizinho de DIAGONAL esta a 1,41 em linha reta e a
			// 1 no `get_dist` -- e o `oview(1)` do original o inclui. Ver `Fusao.DistanciaEmTilesDoDm`.
			distanciaTiles: Fusao.DistanciaEmTilesDoDm(pl.Pos, outro.Pos, ZoneCollision.TileSize),
			euSeiDancar: SabeDancar(pl), eleSabeDancar: SabeDancar(outro),
			racaA: pl.Race, racaB: outro.Race,
			expressoA: PoderPraComparar(pl), expressoB: PoderPraComparar(outro),
			eleJaTemPedido: _pedidosDeFusao.ContainsKey(outro.Id));
	}

	/// <summary>A metade do <see cref="Convidar"/> que AVISA os dois -- separada so pra o metodo caber.</summary>
	private void ConvidarContinua(ServerPlayer pl, ServerPlayer outro, TipoDeFusao tipo, string oque)
	{
		// ============================ A NAMEKUSEIJIN AVISA QUE E PRA SEMPRE ============================
		// O DM escreve isso na propria caixa de pergunta (`Fusion.dm:566`): *"[usr] wishes to
		// PERMANENTLY fuse with you"*. Aqui nao ha caixa modal (ver `Fusao.PrazoDoConviteSegundos`),
		// entao a palavra tem que estar na frase -- ela e a unica diferenca que o convidado precisa
		// entender antes de dizer sim, e nao ha desfazer depois.
		// ==========================================================================================
		string comoAcaba = tipo switch
		{
			TipoDeFusao.Danca => "e voces dois terao que acertar a coreografia. ",
			TipoDeFusao.Namek => "e a fusao e PERMANENTE -- nem o nocaute, nem o tempo, nem a morte "
							   + "desfazem. Voce nao volta a ter corpo proprio. ",
			_ => "e a fusao acontece na hora. ",
		};

		Avisar(pl, $"voce oferece {oque} a {outro.Name}. Ele tem "
				 + $"{Fusao.PrazoDoConviteSegundos:0} s pra responder.");
		Avisar(outro, $"{pl.Name} quer fundir com voce ({oque}). {pl.Name} vai CONTROLAR o corpo "
					+ comoAcaba
					+ $"Aceite ou recuse na aba Learning ({Fusao.PrazoDoConviteSegundos:0} s).");
	}

	/// <summary>
	/// O PODER QUE O PORTAO DE PROXIMIDADE COMPARA. O dono pediu **BP expresso**, e e o
	/// `Ficha.expressedBP` -- o mesmo numero que um scouter le.
	///
	/// O PISO EM `BP` NAO E UM ATALHO: o `expressedBP` so tem valor depois que o `PowerLevel` rodou
	/// (ele e escrito la, `Fighter.Power.cs:127`), e um corpo recem-carregado o tem em zero por um
	/// tique. Sem o piso, dois jogadores que se convidassem no primeiro instante da sessao teriam
	/// razao 1 (zero sobre zero) e o portao aprovaria qualquer desigualdade -- calado.
	/// </summary>
	private static double PoderPraComparar(ServerPlayer p) =>
		p.Ficha.expressedBP > 0 ? p.Ficha.expressedBP : p.Ficha.BP;

	/// <summary>
	/// `fusion_on_cooldown()` (`Fusion.dm:55-56`): `world.realtime &lt; fusion_cooldown_until`, literal.
	/// </summary>
	private static bool NaRecargaDeFusao(ServerPlayer p, long agora) =>
		agora < p.Ficha.fusion_cooldown_until;

	/// <summary>
	/// COBRA A RECARGA DE 1 h DESTE CORPO -- `fusion_cooldown_until = world.realtime + FUSION_COOLDOWN`
	/// (`Fusion.dm:320` e `:334`).
	///
	/// UM METODO E NAO DUAS ATRIBUICOES: o DM escreve a mesma linha em dois lugares (o dono e o
	/// passageiro) e o port fazia igual. Com o carimbo indo pro DISCO agora, duas escritas do mesmo
	/// numero sao duas chances de uma delas ficar pra tras.
	/// </summary>
	private static void CobrarARecargaDeFusao(ServerPlayer p, long agora) =>
		p.Ficha.fusion_cooldown_until = agora + (long)(Fusao.RecargaSegundos * 1000);

	/// <summary>
	/// A RECUSA DIZ O QUE FALTA -- mesmo criterio do `PorQueNaoVincula` e do `PorQueNaoEnsina`.
	///
	/// Aqui ela carrega uma carga a mais: **os dois portoes novos (raca e poder proximo) so existem
	/// nestas frases**. Um "nao da" mudo mandaria o jogador procurar pra sempre uma condicao que
	/// nunca foi escrita em lugar nenhum do jogo.
	/// </summary>
	/// <param name="tipo">
	/// QUAL FUSAO FOI PEDIDA -- e ele entrou porque os portoes deixaram de ser um so: a distancia agora
	/// e por tipo (<see cref="Fusao.TilesDoConvite"/>), e uma frase que citasse um teto unico mandaria
	/// metade dos jogadores procurar a distancia errada.
	/// </param>
	private string PorQueNaoFunde(RecusaDeFusao r, TipoDeFusao tipo,
								  ServerPlayer eu, ServerPlayer outro, long agora) => r switch
	{
		RecusaDeFusao.SemAssinatura => "essa criatura nao tem identidade propria.",
		RecusaDeFusao.EleMesmo => "ninguem funde consigo mesmo.",
		RecusaDeFusao.JaFundido => "um de voces ja esta fundido (ou no meio da danca).",
		RecusaDeFusao.NaRecarga => EmQuantoTempo(eu, outro, agora),
		RecusaDeFusao.Caido => "nao da, com alguem caido.",
		RecusaDeFusao.OutraZona => $"{outro.Name} nem esta neste lugar.",
		// A FRASE DIZ O NUMERO DO TIPO, e nao um teto unico: com a Danca cobrando o tile ao lado e a
		// Potara enxergando vinte, uma frase so mandaria metade dos jogadores procurar a distancia
		// errada. O tipo sai do pedido pendente (o convidado respondendo) ou do que esta sendo
		// oferecido -- ver `PorQueNaoFunde`, que agora o recebe.
		RecusaDeFusao.Longe => tipo == TipoDeFusao.Potara
			? $"{outro.Name} esta longe demais -- os brincos alcancam {Fusao.TilesDaPotara} passos."
			: $"{outro.Name} precisa estar NO TILE AO LADO (a {Fusao.TilesColados} passo). "
			  + "(Os brincos Potara alcancam de longe e puxam os dois.)",
		RecusaDeFusao.JaTemPedido => $"{outro.Name} ja tem um convite na mesa -- espere ele responder.",
		RecusaDeFusao.SemSkill => "voce nao sabe a Danca da Fusao.",
		RecusaDeFusao.OutroSemSkill =>
			$"{outro.Name} nao sabe a Danca da Fusao -- a coreografia e A DOIS, e os dois precisam conhece-la.",
		RecusaDeFusao.RacaDiferente =>
			$"a Danca da Fusao so funciona entre a mesma raca: voce e {Fusao.RaizDaRaca(eu.Race)} "
			+ $"e {outro.Name} e {Fusao.RaizDaRaca(outro.Race)}. (Os brincos Potara nao ligam pra isso.)",
		RecusaDeFusao.NaoEhNamekuseijin =>
			"a fusao permanente e coisa de Namekuseijin, e os DOIS precisam ser: voce e "
			+ $"{eu.Race} e {outro.Name} e {outro.Race}.",
		RecusaDeFusao.PoderDesigual =>
			"os poderes de voces dois estao longe demais um do outro: o mais fraco precisa expressar "
			+ $"pelo menos {Fusao.LimiarDeProximidade * 100:0}% do mais forte, e hoje da "
			+ $"{Fusao.RazaoDePoder(PoderPraComparar(eu), PoderPraComparar(outro)) * 100:0}%. "
			+ "(Quem esta escondendo o proprio poder tambem nao consegue ser medido.)",
		_ => "agora nao.",
	};

	private static string EmQuantoTempo(ServerPlayer a, ServerPlayer b, long agora)
	{
		long ate = Math.Max(a.Ficha.fusion_cooldown_until, b.Ficha.fusion_cooldown_until);
		return $"um de voces ainda esta se recuperando da ultima fusao ({(ate - agora) / 60000.0:0.#} min).";
	}

	// =====================================================================
	// 2. A RESPOSTA
	// =====================================================================
	/// <summary>
	/// O "SIM" OU O "NAO" DE QUEM FOI CONVIDADO.
	///
	/// TUDO E REVALIDADO -- mesma razao do `ResponderAoMestre` e do `ResponderALicao`: entre a
	/// oferta e a resposta os dois podem ter se afastado, brigado, caido ou trocado de planeta.
	/// **E este e o caminho que responde "e se um sair de perto?"**: nao ha fusao a distancia; o
	/// convite simplesmente nao fecha, e os dois ouvem por que.
	/// </summary>
	private void ResponderAoConvite(ServerPlayer pl, bool aceitou)
	{
		if (!_pedidosDeFusao.TryGetValue(pl.Id, out PedidoDeFusao? p))
		{
			Avisar(pl, "ninguem te convidou pra fundir.");
			return;
		}

		long agora = NowMs();
		if (agora > p.Ate)
		{
			_pedidosDeFusao.Remove(pl.Id);
			Avisar(pl, $"o convite de {p.Nome} ja tinha passado.");
			return;
		}

		// O PENDENTE SAI DA MESA ANTES DE QUALQUER COISA. Se ele so saisse no fim, todo `return` de
		// erro daqui pra baixo deixaria o convite de pe -- e uma revalidacao que falha viraria um
		// pedido que o jogador pode tentar aceitar pra sempre.
		_pedidosDeFusao.Remove(pl.Id);

		ServerPlayer? quem = _players.GetValueOrDefault(p.DeQuem);
		if (quem == null || quem.Name != p.Nome)
		{
			// O NOME CONFERE JUNTO COM O ID porque id de rede SE REUSA: quem saiu e devolveu o
			// numero pro proximo a entrar nao pode ser fundido no lugar dele.
			Avisar(pl, $"{p.Nome} nao esta mais por aqui.");
			return;
		}

		if (!aceitou)
		{
			Avisar(pl, $"voce recusa a fusao com {quem.Name}.");
			Avisar(quem, $"{pl.Name} recusou a fusao.");
			return;
		}

		RecusaDeFusao r = Fusao.Avaliar(
			p.Tipo,
			convidaTemAssinatura: EhPessoa(quem), convidadoTemAssinatura: EhPessoa(pl),
			mesmaPessoa: quem == pl,
			algumJaFundido: OcupadoPorFusao(quem.Id) || OcupadoPorFusao(pl.Id),
			algumNaRecarga: NaRecargaDeFusao(quem, agora) || NaRecargaDeFusao(pl, agora),
			algumCaido: quem.Ficha.KO || quem.Ficha.dead || pl.Ficha.KO || pl.Ficha.dead,
			mesmaZona: quem.Zone.Hash == pl.Zone.Hash,
			// A MESMA MOEDA DO CONVITE (`get_dist`) -- ver o `Convidar`. E o DU cobra a distancia nos
			// DOIS momentos tambem: `Metamoran Fusion.dm:92` antes da caixa de pergunta e `:122` depois
			// do "Accept".
			distanciaTiles: quem.Zone.Hash != pl.Zone.Hash
				? double.MaxValue
				: Fusao.DistanciaEmTilesDoDm(quem.Pos, pl.Pos, ZoneCollision.TileSize),
			euSeiDancar: SabeDancar(quem), eleSabeDancar: SabeDancar(pl),
			racaA: quem.Race, racaB: pl.Race,
			expressoA: PoderPraComparar(quem), expressoB: PoderPraComparar(pl),
			eleJaTemPedido: false);

		if (r != RecusaDeFusao.Pode)
		{
			string porque = PorQueNaoFunde(r, p.Tipo, quem, pl, agora);
			Avisar(pl, porque);
			Avisar(quem, $"a fusao com {pl.Name} nao fechou: {porque}");
			return;
		}

		// ============================ A POTARA NAO TEM DANCA ============================
		// Pedido do dono, literal: *"aceitando funde na hora, SEM QTE"*. E faz sentido no proprio
		// desenho -- o que a Potara cobra e o ITEM (e o item so cai no colo de um Kaioshin), e a
		// coreografia e o que a Danca cobra NO LUGAR de um item.
		// ===============================================================================
		// "SEM QTE" NAO E "SEM CENA" NEM "NA HORA". O que o dono dispensou aqui foi a COREOGRAFIA; o
		// que ele acrescentou depois foi o PUXAO -- *"na potara quando ela comecar eles sao puxados um
		// pro lado do outro e quando se encostarem a cinematica comeca"*. Quem decide qual dos dois
		// caminhos vale e o `Fusao.PuxaOsCorpos`, no Core, e nao um `if` de tipo escrito aqui.
		if (p.Tipo != TipoDeFusao.Danca) { ComecarOPuxaoDeFusao(quem, pl, p.Tipo); return; }

		ComecarADanca(quem, pl);
	}

	// =====================================================================
	// 3. A DANCA (o quick time event dos DOIS)
	// =====================================================================
	/// <summary>
	/// OS DOIS DANCAM. Tres letras cada um; **os dois acertando as tres, a fusao sai inteira; um
	/// erro de qualquer um dos dois estraga a fusao dos dois** -- que e o pedido do dono, literal.
	///
	/// OS CORPOS FICAM PARADOS enquanto isso, pelo mesmo `Stun` que o ZanzoClash usa: uma
	/// coreografia a dois em que um dos dois pode sair andando nao e uma coreografia. E o prazo do
	/// `Stun` e o da danca -- ele nao sobra depois.
	/// </summary>
	private void ComecarADanca(ServerPlayer a, ServerPlayer b)
	{
		long agora = NowMs();
		var d = new DancaDeFusao
		{
			A = a, B = b,
			Acaba = agora + (long)(Fusao.SegundosDaDanca * 1000),
		};

		_dancas.Add(d);
		_dancando[a.Id] = d;
		_dancando[b.Id] = d;

		foreach (ServerPlayer p in new[] { a, b })
		{
			p.Combate.Stun = Math.Max(p.Combate.Stun, Fusao.SegundosDaDanca);
			ComecoDaDanca(p, p == a ? b : a, (int)(Fusao.SegundosDaDanca * 1000));
		}

		NovaLetraDaDanca(d, a);
		NovaLetraDaDanca(d, b);

		Avisar(a, $"DANCA DA FUSAO com {b.Name}! Acerte as {Fusao.LetrasDaDanca} letras -- "
				+ "se qualquer um dos dois errar, a fusao sai estragada.");
		Avisar(b, $"DANCA DA FUSAO com {a.Name}! Acerte as {Fusao.LetrasDaDanca} letras -- "
				+ "se qualquer um dos dois errar, a fusao sai estragada.");
		GD.Print($"[server] DANCA DA FUSAO: {a.Name} x {b.Name}");
	}

	/// <summary>
	/// O `Comecou` do embate, com o byte da danca. **O mesmo opcode dos outros dois** -- ver
	/// `Protocol.TipoDeEmbate`: comecou, letra, placar, veredito, acabou e a mesma conversa, e o
	/// que muda e o vocabulario na tela.
	/// </summary>
	private static void ComecoDaDanca(ServerPlayer p, ServerPlayer outro, int ms)
	{
		var w = Protocol.Begin(Protocol.S2C.Clash);
		w.Put((byte)Protocol.ClashSub.Comecou);
		w.Put((byte)Protocol.TipoDeEmbate.Fusao);
		w.Put(p.Id);
		w.Put(outro.Id);
		w.Put(ms);
		// A DANCA NAO TEM VANTAGEM DE PODER, e os dois uns dizem isso na tela: aqui ninguem ganha do
		// outro. Mandar a razao de BP faria o cliente desenhar um cabo de guerra que nao existe.
		w.Put(1f);
		w.Put(1f);
		p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	private void NovaLetraDaDanca(DancaDeFusao d, ServerPlayer p)
	{
		bool ehA = p == d.A;
		if (ehA ? d.FalhouA : d.FalhouB) return;           // quem ja errou nao dança mais
		if ((ehA ? d.AcertosA : d.AcertosB) >= Fusao.LetrasDaDanca) return;

		char c = SortearLetraDeEmbate();
		long agora = NowMs();
		long prazo = agora + MsPorTecla;
		if (ehA) { d.LetraA = c; d.PrazoA = prazo; } else { d.LetraB = c; d.PrazoB = prazo; }

		// O CORPO SEM TECLADO RESPONDE SOZINHO -- a mesma agenda do `NovaTecla` do ZanzoClash, e
		// pela mesma razao escrita la: um quick time event contra ninguem nao e um quick time event.
		// Na producao isto nao acontece (a fusao e entre dois jogadores), mas a bancada precisa dele
		// pra exercitar a danca sem duas pessoas de verdade sentadas no teclado.
		if (!TemTeclado(p))
		{
			double reacao = p.Cerebro?.TempoDeReacao ?? 0.3;
			long quando = agora + (long)(reacao * 1000 * (0.7 + _rng.NextDouble() * 0.9));
			if (ehA) d.RespondeA = quando; else d.RespondeB = quando;
		}

		MandarTecla(p, c, MsPorTecla);
	}

	/// <summary>
	/// O JOGADOR APERTOU UMA LETRA NA DANCA. Devolve falso quando ele nao esta dancando -- e ai quem
	/// trata sao os outros dois embates (ver <see cref="TeclaDeQualquerEmbate"/>).
	/// </summary>
	private bool TeclaDaDanca(ServerPlayer p, char c)
	{
		if (!_dancando.TryGetValue(p.Id, out DancaDeFusao? d)) return false;

		bool ehA = p == d.A;
		char esperada = ehA ? d.LetraA : d.LetraB;
		long prazo = ehA ? d.PrazoA : d.PrazoB;
		if (esperada == '\0' || NowMs() > prazo) return true;

		if (char.ToUpperInvariant(c) != esperada)
		{
			// ============================ ERRAR AQUI NAO CUSTA UM PONTO: CUSTA A FUSAO ============================
			// Nos outros dois embates o erro tira um ponto e a disputa continua, porque la ha um placar
			// a recuperar. Aqui nao ha: o pedido do dono e binario -- *"os dois acertando -> fusao
			// normal; falhando -> a fusao ACONTECE mas fica EXTREMAMENTE FRACA"*. Um erro fecha a
			// coreografia daquele lado na hora, e e por isso que a letra some junto: ele nao recebe
			// mais nenhuma.
			// ================================================================================================
			if (ehA) { d.FalhouA = true; d.LetraA = '\0'; } else { d.FalhouB = true; d.LetraB = '\0'; }
			Julgou(p, false);
			PlacarDaDanca(d);
			Avisar(p, "voce errou o passo!");
			TalvezAcabarADanca(d);
			return true;
		}

		// ACERTOU: PONTUA E A PROXIMA JA VEM -- o mesmo adiantamento com piso de cadencia dos outros
		// dois embates (`MsMinimoEntreLetras`), e pela mesma razao: quanto mais rapido, melhor, ate o
		// limite da mao humana, e abaixo dele todo mundo empata.
		if (ehA) { d.AcertosA++; d.LetraA = '\0'; d.PrazoA -= MsPorTecla - MsMinimoEntreLetras; }
		else { d.AcertosB++; d.LetraB = '\0'; d.PrazoB -= MsPorTecla - MsMinimoEntreLetras; }

		Julgou(p, true);
		PlacarDaDanca(d);
		TalvezAcabarADanca(d);
		return true;
	}

	/// <summary>
	/// O PLACAR DA DANCA e "quantos passos cada um acertou". O cliente desenha
	/// `meus / (meus + dele)` -- ver `Protocol.ClashSub.Placar` --, entao com os dois em dia a barra
	/// fica no meio, que e exatamente a leitura certa: **ninguem esta ganhando de ninguem**.
	/// </summary>
	private static void PlacarDaDanca(DancaDeFusao d)
	{
		MandarPlacar(d.A, d.AcertosA, d.AcertosB);
		MandarPlacar(d.B, d.AcertosB, d.AcertosA);
	}

	/// <summary>Os dois lados fecharam (acertando tudo ou errando)? Entao acabou.</summary>
	private void TalvezAcabarADanca(DancaDeFusao d)
	{
		bool fechouA = d.FalhouA || d.AcertosA >= Fusao.LetrasDaDanca;
		bool fechouB = d.FalhouB || d.AcertosB >= Fusao.LetrasDaDanca;
		if (fechouA && fechouB) ResolverADanca(d);
	}

	/// <summary>
	/// A DANCA ACABOU: funde. **Sempre funde** -- o pedido do dono e explicito em que falhar nao
	/// cancela nada (*"a fusao ACONTECE mas fica EXTREMAMENTE FRACA"*).
	/// </summary>
	private void ResolverADanca(DancaDeFusao d)
	{
		if (d.Resolvida) return;   // ver `DancaDeFusao.Resolvida`
		d.Resolvida = true;
		SoltarDaDanca(d);

		bool estragada = d.AcertosA < Fusao.LetrasDaDanca || d.AcertosB < Fusao.LetrasDaDanca;

		// ============================ O FIM PELO MESMO PACOTE, COM OUTRA LEITURA ============================
		// Nao ha vencedor numa danca, entao o par (venc, perd) do `ClashSub.Acabou` carrega o DESFECHO
		// em vez do placar: **o meu id nos dois campos = a fusao saiu perfeita; os dois zerados = saiu
		// estragada** (a mesma convencao do empate da colisao de ki). Sem isso a tela do quick time
		// event terminaria dizendo "voce foi mais rapido" no fim de uma coreografia a dois.
		//
		// PESSOAL nos dois lados, e nao um pacote so: e o mesmo id nos dois campos, entao cada um
		// precisa receber o SEU. Um pacote unico faria o outro ler "o id dele" e achar que perdeu.
		Acabou(d.A, estragada ? 0 : d.A.Id, estragada ? 0 : d.A.Id);
		Acabou(d.B, estragada ? 0 : d.B.Id, estragada ? 0 : d.B.Id);

		// OS CORPOS SAO SOLTOS ANTES DE FUNDIR. O `Stun` foi posto pela danca e a fusao nao herda
		// castigo nenhum dela -- sem esta linha o corpo fundido nasceria plantado no chao por ate 4 s.
		d.A.Combate.Stun = 0;
		d.B.Combate.Stun = 0;

		if (estragada)
		{
			foreach (ServerPlayer p in new[] { d.A, d.B })
				Avisar(p, "a coreografia saiu errada! O que se ergue dali e fraco, desengoncado -- "
						+ "e nao consegue se transformar.");
		}

		// A CENA ENTRA AQUI, e o `Fundir` sai daqui. O que este metodo decide -- QUEM funde com quem e
		// se saiu estragada -- continua sendo decidido agora; o que ele deixou de fazer e **executar**
		// a fusao no mesmo instante. Ver `ComecarACenaDaFusao`.
		// A PORTA E A MESMA DA POTARA -- `ComecarOPuxaoDeFusao`, e nao o `ComecarACenaDaFusao` direto.
		// A Danca nao puxa ninguem (ela ja exigiu `Fusao.TilesColados` no convite E no aceite, e os dois
		// ficaram presos pelo `Stun` a coreografia inteira), entao aquele metodo cai na cena no mesmo
		// tique. O que se ganha e a porta unica: no dia em que a ordem "puxao -> cena" mudar, ela muda
		// em um lugar e nao em dois.
		ComecarOPuxaoDeFusao(d.A, d.B, TipoDeFusao.Danca, estragada);
	}

	/// <summary>Tira a danca das listas. Um lugar so, pra nao sobrar id em dicionario nenhum.</summary>
	private void SoltarDaDanca(DancaDeFusao d)
	{
		_dancas.Remove(d);
		_dancando.Remove(d.A.Id);
		_dancando.Remove(d.B.Id);
	}

	/// <summary>
	/// A DANCA MORREU NO MEIO -- alguem caiu, morreu, saiu do mundo ou trocou de zona.
	///
	/// **AQUI NAO HA FUSAO NENHUMA, nem estragada**, e a decisao e deliberada: a fusao estragada e o
	/// preco de DANCAR MAL, e nao o preco de levar um golpe no meio da coreografia. Se um nocaute no
	/// segundo passo produzisse uma fusao fraca, o jeito de estragar a fusao de dois inimigos seria
	/// bater neles enquanto dancam -- e o outro sairia preso a um corpo fraco por 15 minutos por
	/// causa de um terceiro.
	/// </summary>
	private void AbortarADanca(DancaDeFusao d, string motivo)
	{
		if (d.Resolvida) return;
		d.Resolvida = true;
		SoltarDaDanca(d);
		foreach (ServerPlayer p in new[] { d.A, d.B })
		{
			if (!_players.ContainsKey(p.Id)) continue;
			p.Combate.Stun = 0;
			// OS DOIS ZERADOS = "nao deu certo" (ver `ResolverADanca`). A tela diz que a coreografia
			// falhou, e a frase seguinte diz por que -- e, ao contrario da falha por erro de tecla,
			// **daqui nao sai fusao nenhuma**.
			Acabou(p, 0, 0);
			Avisar(p, $"a danca se desfaz: {motivo}");
		}
		GD.Print($"[server] DANCA DA FUSAO abortada ({d.A.Name} x {d.B.Name}): {motivo}");
	}

	// =====================================================================
	// 3a. O PUXAO -- os corpos andam um pro outro, e a cena so comeca quando encostam
	// =====================================================================
	/// <summary>
	/// COMECA O PUXAO -- **e ele e a porta unica pra cinematica**, pros tres tipos de fusao.
	///
	/// ============================ QUEM PUXA E QUEM NAO PUXA ============================
	/// A pergunta e do Core (<see cref="Fusao.PuxaOsCorpos"/>) e a resposta e "so a Potara". A Danca e
	/// a Namekuseijin ja exigiram <see cref="Fusao.TilesColados"/> no convite E no aceite, entao pra
	/// elas este metodo cai na cena no MESMO tique -- nao ha fase de puxao, e nao ha `if` de tipo
	/// escrito aqui: quem ja esta colado passa pelo primeiro `return`.
	///
	/// **JA COLADOS TAMBEM NAO PUXAM**, e essa e a mesma linha: o `while` do original nem entra quando
	/// `get_dist &lt;= 1` (`Potara_Fusion.dm:124`). Uma Potara oferecida ao vizinho de tile comeca a cena
	/// na hora, como no DU.
	///
	/// ============================ E A ULTIMA TRAVA SOBE PRA CA ============================
	/// O <see cref="ComecarACenaDaFusao"/> tem a dele (dois convites cruzados podem chegar no mesmo
	/// tique); esta e a mesma pergunta um degrau antes, porque agora ha uma fase a mais em que os dois
	/// estao comprometidos e nao estao em `_fundidos` nem em `_dancando`. Ver
	/// <see cref="OcupadoPorFusao"/>.
	/// ==================================================================================
	/// </summary>
	private void ComecarOPuxaoDeFusao(ServerPlayer dono, ServerPlayer passageiro,
									  TipoDeFusao tipo, bool estragada = false)
	{
		if (OcupadoPorFusao(dono.Id) || OcupadoPorFusao(passageiro.Id)) return;

		if (!Fusao.PuxaOsCorpos(tipo) || JaEncostaram(dono, passageiro))
		{
			ComecarACenaDaFusao(dono, passageiro, tipo, estragada);
			return;
		}

		var p = new PuxaoDeFusao
		{
			Dono = dono,
			Passageiro = passageiro,
			Tipo = tipo,
			MelhorDistancia = Vec2.Distance(dono.Pos, passageiro.Pos),
			SemMelhorarDesde = NowMs(),
		};

		_puxoesDeFusao.Add(p);
		_sendoPuxadoPraFusao[dono.Id] = p;
		_sendoPuxadoPraFusao[passageiro.Id] = p;

		foreach (ServerPlayer q in new[] { dono, passageiro })
			Avisar(q, "os brincos brilham e o chão foge dos seus pés -- você é puxado!");

		GD.Print($"[server] PUXAO DE FUSAO {tipo}: {dono.Name} + {passageiro.Name} | "
			   + $"{p.MelhorDistancia:0} px a fechar");
	}

	/// <summary>
	/// OS DOIS ESTAO NO TILE AO LADO? -- o `get_dist(A,B) &lt;= 1` do original, pela mesma funcao que o
	/// convite usa (<see cref="Fusao.DistanciaEmTilesDoDm"/>).
	///
	/// A ZONA ENTRA JUNTO porque no BYOND `get_dist` entre `z` diferentes nao quer dizer nada, e o
	/// proprio `while` do `Potara_Fusion.dm:124` carrega o `&amp;&amp; C.mob.z == B.z` ao lado da distancia.
	/// </summary>
	private static bool JaEncostaram(ServerPlayer a, ServerPlayer b) =>
		a.Zone.Hash == b.Zone.Hash
		&& Fusao.DistanciaEmTilesDoDm(a.Pos, b.Pos, ZoneCollision.TileSize) <= Fusao.TilesColados;

	/// <summary>
	/// O RELOGIO DO PUXAO -- o corpo do `while` do `Potara_Fusion.dm:124-129`, um tique de cada vez.
	///
	/// ============================ O TIQUE CHEIO, E NAO O DE 1 Hz ============================
	/// A velocidade e de <see cref="Fusao.VelocidadeDoPuxao"/> px/s por corpo (1280, que sao os 32 px
	/// do `step_to` a cada `world.tick_lag` de um mundo a 40 fps). A 1 Hz cada passada moveria 1280 px
	/// de uma vez -- quarenta tiles num quadro. O que se veria seria teleporte, e nao puxao. E o mesmo
	/// argumento, com o mesmo numero na frente, que ja poe o arremesso e o selo neste laco.
	///
	/// ============================ CUSTO ZERO QUANDO NAO HA PUXAO ============================
	/// A primeira linha. Fora dos poucos decimos em que uma Potara esta fechando distancia, este metodo
	/// e uma comparacao de inteiro por tique.
	///
	/// ============================ OS CORTES SAO OS DA CENA, E NA MESMA ORDEM ============================
	/// De proposito: as duas fases sao a MESMA janela fragil -- dois corpos comprometidos um com o
	/// outro e **nada aplicado ainda** --, e uma lista de cortes que divergisse deixaria a fusao
	/// comecar por um caminho que a fase seguinte ja considera impossivel. O que este laco tem a mais e
	/// o corte que so existe aqui: **pararam de se aproximar**. Ver
	/// <see cref="Fusao.SegundosSemAproximarParaDesistir"/> -- e e ele o *"se um dos dois nao chegar
	/// (parede, KO, logout, teleporte), a fusao NAO comeca"* do pedido.
	/// ==============================================================================================
	/// </summary>
	private void TickDoPuxaoDeFusao()
	{
		if (_puxoesDeFusao.Count == 0) return;
		long agora = NowMs();

		for (int i = _puxoesDeFusao.Count - 1; i >= 0; i--)
		{
			PuxaoDeFusao p = _puxoesDeFusao[i];
			ServerPlayer a = p.Dono, b = p.Passageiro;

			if (!_players.ContainsKey(a.Id) || !_players.ContainsKey(b.Id))
			{ AbortarOPuxaoDeFusao(p, "um dos dois saiu do mundo."); continue; }
			if (a.Ficha.dead || b.Ficha.dead)
			{ AbortarOPuxaoDeFusao(p, "um dos dois morreu."); continue; }
			if (a.Ficha.KO || b.Ficha.KO)
			{ AbortarOPuxaoDeFusao(p, "um dos dois foi derrubado."); continue; }
			if (a.Zone.Hash != b.Zone.Hash)
			{ AbortarOPuxaoDeFusao(p, "voces nao estao mais no mesmo lugar."); continue; }

			// ============================ ENCOSTARAM: A CENA COMECA AQUI ============================
			// *"quando se encostarem a cinematica comeca"*. E o `while` acabando, e nao um prazo
			// vencendo -- por isso a pergunta e feita ANTES de andar mais um passo: chegar e chegar.
			// ====================================================================================
			if (JaEncostaram(a, b))
			{
				TipoDeFusao tipo = p.Tipo;
				SoltarDoPuxaoDeFusao(p);
				ComecarACenaDaFusao(a, b, tipo, estragada: false);
				continue;
			}

			// ---- os dois andam, e os dois pelo mesmo tanto (`step_to` nos DOIS, `:125` e `:127`) ----
			double passo = Fusao.VelocidadeDoPuxao * Protocol.TickSeconds;
			AndarNoPuxao(a, b, passo);
			AndarNoPuxao(b, a, passo);

			// ============================ E OS DOIS FICAM SEM AS REDEAS ============================
			// `AlterInputDisabled(1)` nos dois (`:122-123`). Aqui isso sao os dois canais que ja existem
			// e que o resto do jogo ja obedece: o `PuxaoDeFusaoRestante` (o funil de vetor recusa o passo
			// e o cliente para de integrar tecla) e o `Stun` (o combate). REGADO por tique e nao escrito
			// uma vez -- ver o campo: parou de regar, o corpo se solta sozinho num decimo de segundo,
			// **inclusive se este laco morrer por um caminho que ninguem previu**.
			// ==================================================================================
			foreach (ServerPlayer q in new[] { a, b })
			{
				q.PuxaoDeFusaoRestante = Jandirus.Core.Combat.Empurrao.SegundosPorTique;
				q.Combate.Stun = Math.Max(q.Combate.Stun, Jandirus.Core.Combat.Empurrao.SegundosPorTique);
			}

			// ============================ PARARAM DE SE APROXIMAR? ============================
			// A MENOR distancia ja alcancada e o marco, e nao a de agora: assim um corpo que oscila na
			// quina de uma parede nao renova o prazo por acaso. Ver o campo `MelhorDistancia`.
			// ==============================================================================
			double dist = Vec2.Distance(a.Pos, b.Pos);
			if (dist < p.MelhorDistancia - 0.5)
			{
				p.MelhorDistancia = dist;
				p.SemMelhorarDesde = agora;
			}
			else if (agora - p.SemMelhorarDesde
					 >= (long)(Fusao.SegundosSemAproximarParaDesistir * 1000))
			{
				AbortarOPuxaoDeFusao(p, "voces nao conseguem se alcancar (ha algo no caminho).");
			}
		}
	}

	/// <summary>
	/// UM PASSO DO PUXAO -- o `step_to(quem, ate, 0, 32)` do original, com a parede mandando.
	///
	/// ============================ O PASSO E CORTADO PRA NAO ATRAVESSAR O OUTRO ============================
	/// O `while` do DM para em `get_dist &lt;= 1` -- um tile. Sem o corte, os dois andariam
	/// <see cref="Fusao.VelocidadeDoPuxao"/> px cada por tique e no ultimo passo se atravessariam,
	/// trocando de lado e disparando outro passo de volta. Como os DOIS andam, o que sobra a fechar se
	/// divide por dois.
	///
	/// ============================ PAREDE MANDA MAIS QUE O PUXAO ============================
	/// A mesma pergunta do passo a pe e da investida (`MoveRules.PathOccupied` com o
	/// <see cref="ModoDeTravessiaDe"/> do proprio corpo): sem ela, o puxao seria o unico jeito do jogo
	/// de andar por dentro de uma parede. Quem esbarra simplesmente **nao anda neste tique** -- e e
	/// isso que faz o detector de "pararam de se aproximar" desistir sozinho, sem uma segunda regra
	/// dizendo o que e uma parede.
	///
	/// ARREMESSADO NAO ANDA: o `Arremessar` ganha do puxao (ele zera o prazo), e um corpo no ar tem o
	/// proprio `Pos` sendo escrito pelo `TickDoEmpurrao` no mesmo tique. Dois escritores no mesmo campo
	/// e o defeito que aquele arquivo inteiro existe pra evitar.
	/// </summary>
	private void AndarNoPuxao(ServerPlayer quem, ServerPlayer ate, double passo)
	{
		if (quem.TiquesDeVoo > 0) return;

		Vec2 d = ate.Pos - quem.Pos;
		float dist = d.Length;
		if (dist <= 1e-3f) return;

		// O QUE FALTA FECHAR, dividido por dois porque o outro tambem esta vindo.
		double falta = (dist - ZoneCollision.TileSize) * 0.5;
		if (falta <= 0) return;

		Vec2 destino = quem.Pos + d.Normalized() * (float)Math.Min(passo, falta);

		ZoneCollision? mapa = _catalogo?.Get(quem.Zone)?.Mapa;
		if (mapa != null && MoveRules.PathOccupied(mapa, quem.Pos, destino, ModoDeTravessiaDe(quem)))
			return;

		quem.Pos = destino;
		quem.Facing = MoveRules.FacingFrom(d, quem.Facing);   // chega OLHANDO pro outro
	}

	/// <summary>Tira o puxao das listas. Um lugar so, pra nao sobrar id em dicionario nenhum.</summary>
	private void SoltarDoPuxaoDeFusao(PuxaoDeFusao p)
	{
		_puxoesDeFusao.Remove(p);
		_sendoPuxadoPraFusao.Remove(p.Dono.Id);
		_sendoPuxadoPraFusao.Remove(p.Passageiro.Id);
	}

	/// <summary>
	/// O PUXAO MORREU ANTES DE ELES SE ENCOSTAREM -- e **daqui nao sai fusao nenhuma**.
	///
	/// E a mesma decisao (e o mesmo argumento) do <see cref="AbortarACenaDeFusao"/> e do
	/// <see cref="AbortarADanca"/>: nada foi aplicado, entao nao ha o que desfazer, e **a recarga de
	/// 1 h nao e cobrada** -- ela e o preco de uma fusao que ACONTECEU (`Fusion.dm:320`, `:334`).
	///
	/// O `Stun` e o prazo do puxao sao zerados na saida porque foi ESTE laco que os pos, e eles sao
	/// regados por tique: sem estas duas linhas o corpo ficaria ate um decimo de segundo a mais sem as
	/// redeas -- pouco, e pouco calado e como este projeto perde uma tarde.
	/// </summary>
	private void AbortarOPuxaoDeFusao(PuxaoDeFusao p, string motivo)
	{
		SoltarDoPuxaoDeFusao(p);

		foreach (ServerPlayer q in new[] { p.Dono, p.Passageiro })
		{
			if (!_players.ContainsKey(q.Id)) continue;
			q.PuxaoDeFusaoRestante = 0;
			q.Combate.Stun = 0;
			MandarFicha(q);   // o bit "sou dirigido" apagado -- senao o cliente continua sem obedecer a tecla
			Avisar(q, $"a atração se desfaz e voces continuam dois: {motivo}");
		}

		GD.Print($"[server] PUXAO DE FUSAO abortado ({p.Dono.Name} x {p.Passageiro.Name}): {motivo}");
	}

	// =====================================================================
	// 3b. A CINEMATICA -- os segundos entre "vai fundir" e "fundiu"
	// =====================================================================
	/// <summary>A cena, lida do Core UMA VEZ. Ver <see cref="Jandirus.Core.Forms.Cinematicas.Fusao"/>.</summary>
	private static Jandirus.Core.Forms.Cinematica CenaDaFusao =>
		Jandirus.Core.Forms.Cinematicas.Fusao;

	/// <summary>
	/// COMECA A CINEMATICA DA FUSAO. **Nada e fundido aqui** -- o `Fundir` sai do
	/// <see cref="TickDaCenaDeFusao"/>, no instante da virada.
	///
	/// ============================ ELE E O IRMAO DO `CenaDoBio`, E DE PROPOSITO ============================
	/// Mesmo desenho, e o `CenaDoBio` ja documenta o porque de ser um funil: **manda o pacote pra zona
	/// e ANOTA o prazo em que o corpo fica preso**, as duas coisas juntas, num lugar so. Aqui ha dois
	/// chamadores (a danca resolvida e a Potara aceita) e eles precisam fazer exatamente as mesmas
	/// cinco coisas -- e o modo de falha de escrever cinco linhas em dois lugares e o de sempre.
	///
	/// ============================ OS DOIS PRAZOS SAEM DA CENA, E SO DELA ============================
	/// `SegundosAteAVirada` diz quando fundir -- e ele e o FIM da animacao da luz
	/// (`Cinematicas.SegundosDaLuzDaFusao`), e nao um prazo escrito a mao --, e `SegundosPreso` diz por
	/// quanto tempo os corpos ficam parados. Os dois vem do MESMO objeto que o cliente vai tocar, e por isso as duas
	/// pontas nao tem como discordar -- e a mesma disciplina do `MarcarCena`, que aquele arquivo
	/// justifica com o sintoma: *"o servidor descongelaria o Ki num instante e o cliente devolveria o
	/// controle noutro"*.
	///
	/// **CENA SEM VIRADA FUNDE NA HORA.** E o caminho impossivel (a bancada reprova cena sem `Assumir`),
	/// e a saida escolhida e a que nao esconde nada: sem instante de climax nao ha onde pendurar a
	/// fusao, e adivinhar um seria exatamente o *"efeito de fim caindo no comeco"* que o dono ja cobrou
	/// duas vezes -- so que sem ninguem saber.
	///
	/// ============================ E OS CORPOS FICAM PRESOS PELOS DOIS CANAIS ============================
	/// `CenaSegundos` e `Stun`, e nao um so:
	///
	///   * o `CenaSegundos` e o portao do SERVIDOR -- ele desliga o dreno da forma, a regeneracao, a
	///     carga e o custo do voo, e (o que importa aqui) torna o corpo INTOCAVEL (`Combate.Intocavel`).
	///     Sem ele, dois jogadores parados 7 segundos com o chao se soltando em volta seriam dois sacos
	///     de pancada -- e a cena que o dono pediu viraria a armadilha mais barata do jogo;
	///   * o `Stun` e o que ja para os dois na DANCA, e ele existe aqui pelo caso que o `CenaSegundos`
	///     nao cobre: ele e do motor de FORMA (ver `TickDaForma`), e quem prende o passo e o combate.
	///
	/// Sao dois relogios pro mesmo prazo, os dois escritos do mesmo numero. Ver `TickDaCenaDeFusao`,
	/// que os renova enquanto a cena durar.
	/// ================================================================================================
	/// </summary>
	private void ComecarACenaDaFusao(ServerPlayer dono, ServerPlayer passageiro,
									 TipoDeFusao tipo, bool estragada)
	{
		// A MESMA ULTIMA TRAVA DO `Fundir`, e ela sobe pra ca junto com a decisao: entre a resolucao
		// da danca e esta linha passa um tique inteiro. Ver o `fusing_now` do DM (`Fusion.dm:29`).
		if (OcupadoPorFusao(dono.Id) || OcupadoPorFusao(passageiro.Id)) return;

		Jandirus.Core.Forms.Cinematica cena = CenaDaFusao;
		double ateAVirada = cena.SegundosAteAVirada;
		if (ateAVirada < 0)
		{
			GD.PushWarning("[server] a cena da fusao nao tem beat que ASSUME -- fundindo sem cena. "
						 + "Isto e DEFEITO de roteiro, ver `Cinematicas.Fusao`.");
			Fundir(dono, passageiro, tipo, estragada);
			return;
		}

		long agora = NowMs();
		var c = new CenaDeFusao
		{
			Dono = dono,
			Passageiro = passageiro,
			Tipo = tipo,
			Estragada = estragada,
			Funde = agora + (long)(ateAVirada * 1000),
			Acaba = agora + (long)(cena.SegundosPreso * 1000),
		};

		_cenasDeFusao.Add(c);
		_emCenaDeFusao[dono.Id] = c;
		_emCenaDeFusao[passageiro.Id] = c;

		PrenderNaCenaDeFusao(c, cena.SegundosPreso);

		// O PACOTE PRA ZONA INTEIRA -- os dois ids, na ordem do dono do jogo (quem convidou primeiro).
		// Ver `Protocol.S2C.CenaDeFusao`.
		var w = Protocol.Begin(Protocol.S2C.CenaDeFusao);
		w.Put(dono.Id);
		w.Put(passageiro.Id);
		foreach (ServerPlayer o in ZoneList(dono.Zone.Hash))
			o.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		foreach (ServerPlayer p in new[] { dono, passageiro })
			Avisar(p, "uma luz envolve voces dois -- o chao se solta e o ar racha!");

		GD.Print($"[server] CENA DA FUSAO {tipo}{(estragada ? " ESTRAGADA" : "")}: "
			   + $"{dono.Name} + {passageiro.Name} | funde em {ateAVirada:0.#}s, "
			   + $"cena de {cena.SegundosPreso:0.#}s");
	}

	/// <summary>
	/// SEGURA OS DOIS CORPOS PELO QUE FALTA DA CENA -- e ele e chamado NO COMECO e A CADA TIQUE.
	///
	/// ============================ POR QUE RENOVAR, E NAO ESCREVER UMA VEZ ============================
	/// Os dois prazos sao CONTAGENS REGRESSIVAS que outros donos tambem escrevem: o `CenaSegundos` e
	/// descontado pelo `TickDaForma` e **zerado pelo `AnunciarForma`** (a volta pra base marca a cena
	/// da base, que e zero), e o `Stun` e escrito por todo golpe que acerta. Uma escrita unica no
	/// comeco valeria ate o primeiro desses eventos -- e o sintoma seria a fusao mais dificil de
	/// diagnosticar que existe: os corpos soltando no meio da cena **as vezes**.
	///
	/// `Math.Max` E NAO ATRIBUICAO em cima do `Stun`, pela mesma razao que a danca ja usa: um castigo
	/// mais longo que a cena nao pode ser encurtado por ela.
	///
	/// **E ELE NAO E "NADA PESADO NO TIQUE"**: sao duas escritas de double por cena viva, e cena viva
	/// e coisa que existe por 7 segundos algumas vezes por dia. O laco que o chama sai na primeira
	/// linha quando a lista esta vazia, que e o estado normal do servidor.
	/// ==========================================================================================
	/// </summary>
	private static void PrenderNaCenaDeFusao(CenaDeFusao c, double segundos)
	{
		foreach (ServerPlayer p in new[] { c.Dono, c.Passageiro })
		{
			p.CenaSegundos = Math.Max(p.CenaSegundos, segundos);
			p.Combate.Stun = Math.Max(p.Combate.Stun, segundos);
		}
	}

	/// <summary>
	/// O RELOGIO DA CENA DA FUSAO. Roda no tique CHEIO (30 Hz) e nao no de 1 Hz, e pelo mesmo motivo
	/// que as letras da danca rodam la: o instante da virada e um PONTO (o fim da animacao da luz,
	/// 0,7 s de cena) e a 1 Hz ele erraria por ate um segundo -- ou seja por mais que a cena inteira ate
	/// a virada. A fusao aconteceria antes ou depois do clarao que existe pra anuncia-la, que e, letra
	/// por letra, a queixa que o dono ja fez duas vezes.
	///
	/// ============================ CUSTO ZERO QUANDO NAO HA CENA ============================
	/// A primeira linha. Fora dos poucos segundos em que uma fusao esta nascendo, este metodo e uma
	/// comparacao de inteiro por tique -- e e assim que ele cabe no `_Tick` sem entrar na conta.
	///
	/// ============================ A CENA INTERROMPIDA NAO DEIXA MEIO-CORPO ============================
	/// Este e o item 4 do pedido, e a resposta e curta porque o desenho e que a torna curta: **ate a
	/// virada nada foi feito**. Nao ha stat somado, nem skill emprestada, nem `FuseBuff`, nem corpo no
	/// selo -- entao abortar e tirar duas linhas de dicionario e soltar os dois. Ver
	/// <see cref="AbortarACenaDeFusao"/>.
	///
	/// Os cortes sao os MESMOS da danca (`TickDaFusao`, item 2a) e na mesma ordem, de proposito: as
	/// duas coisas sao a mesma janela frágil -- dois corpos comprometidos um com o outro e nada
	/// aplicado ainda --, e uma lista de cortes que divergisse daria uma fusao acontecendo por um
	/// caminho que a danca ja considera impossivel.
	///
	/// DEPOIS DA VIRADA NAO HA MAIS O QUE ABORTAR: a fusao existe, e quem a desfaz e o
	/// <see cref="Separar"/> pelos caminhos que ja existem (o nocaute, a morte, a energia, o logout).
	/// Por isso os cortes so valem enquanto `!c.Fundiu`.
	/// ==============================================================================================
	/// </summary>
	private void TickDaCenaDeFusao()
	{
		if (_cenasDeFusao.Count == 0) return;
		long agora = NowMs();

		for (int i = _cenasDeFusao.Count - 1; i >= 0; i--)
		{
			CenaDeFusao c = _cenasDeFusao[i];

			if (!c.Fundiu)
			{
				if (!_players.ContainsKey(c.Dono.Id) || !_players.ContainsKey(c.Passageiro.Id))
				{ AbortarACenaDeFusao(c, "um dos dois saiu do mundo."); continue; }
				if (c.Dono.Ficha.dead || c.Passageiro.Ficha.dead)
				{ AbortarACenaDeFusao(c, "um dos dois morreu."); continue; }
				if (c.Dono.Ficha.KO || c.Passageiro.Ficha.KO)
				{ AbortarACenaDeFusao(c, "um dos dois foi derrubado."); continue; }
				if (c.Dono.Zone.Hash != c.Passageiro.Zone.Hash)
				{ AbortarACenaDeFusao(c, "voces nao estao mais no mesmo lugar."); continue; }

				// OS CORPOS CONTINUAM PRESOS -- ver `PrenderNaCenaDeFusao` sobre por que isto se
				// renova em vez de ser escrito uma vez. Do que RESTA, e nao do prazo cheio: escrever
				// os 7,0 s todo tique deixaria os dois plantados 7 s depois do fim da cena.
				PrenderNaCenaDeFusao(c, Math.Max(0, (c.Acaba - agora) / 1000.0));

				// ============================ A VIRADA -- AQUI A FUSAO PASSA A EXISTIR ============================
				// E este e o item 3 do pedido, na unica linha em que ele podia caber. O `Fundir` faz
				// tudo o que sempre fez, sem mudanca nenhuma: e o INSTANTE que mudou, e nao o gesto.
				//
				// A CENA NAO ACABA AQUI. Ela segue ate `Acaba` -- e e nessa cauda que o branco escoa
				// do lado do cliente. Tirar a cena da lista agora soltaria os corpos no meio do
				// escoamento (ver o `PrenderNaCenaDeFusao` renovando logo acima).
				// ==========================================================================================
				if (agora >= c.Funde)
				{
					c.Fundiu = true;
					Fundir(c.Dono, c.Passageiro, c.Tipo, c.Estragada);
				}
			}

			if (agora >= c.Acaba) SoltarDaCenaDeFusao(c);
		}
	}

	/// <summary>Tira a cena das listas. Um lugar so, pra nao sobrar id em dicionario nenhum.</summary>
	private void SoltarDaCenaDeFusao(CenaDeFusao c)
	{
		_cenasDeFusao.Remove(c);
		_emCenaDeFusao.Remove(c.Dono.Id);
		_emCenaDeFusao.Remove(c.Passageiro.Id);
	}

	/// <summary>
	/// A CENA MORREU ANTES DA VIRADA -- e **daqui nao sai fusao nenhuma**.
	///
	/// ============================ NEM ESTRAGADA, E A DECISAO E A DA DANCA ============================
	/// O `AbortarADanca` ja tomou esta decisao e o argumento dela vale igual aqui: a fusao estragada e
	/// o preco de DANCAR MAL, e nao o preco de levar um golpe. Se um nocaute no ultimo decimo antes da
	/// virada produzisse uma fusao fraca, o jeito de arruinar a fusao de dois inimigos seria bater neles
	/// durante a cinematica -- e um deles sairia preso a um corpo fraco por 15 minutos por causa de um
	/// terceiro. (Na pratica os dois estao INTOCAVEIS enquanto a cena roda; os cortes que sobram sao os
	/// que nao passam por dano -- sair do mundo, trocar de zona, morrer por outro caminho.)
	///
	/// ============================ E NAO HA O QUE DESFAZER ============================
	/// Nenhum stat foi somado, nenhuma skill emprestada, nenhum `FuseBuff` escrito, nenhum corpo
	/// selado -- porque tudo isso mora no `Fundir`, e o `Fundir` ainda nao rodou. **Nem a recarga de
	/// 1 h e cobrada**: ela e o preco de uma fusao que ACONTECEU (`Fusion.dm:320`,`:334`), e aqui nao
	/// aconteceu nenhuma. Cobra-la puniria os dois por um terceiro ter passado por perto.
	///
	/// O `Stun` E ZERADO NA SAIDA porque foi esta cena que o pos (ver `PrenderNaCenaDeFusao`) -- e a
	/// mesma linha que o `AbortarADanca` ja tem, pelo mesmo motivo. O `CenaSegundos` NAO e zerado a
	/// mao: ele e contagem regressiva descontada pelo `TickDaForma` e some sozinho; zera-lo aqui
	/// apagaria a cinematica de forma de quem estivesse no meio de uma (o caso e raro e ele existe).
	/// ==================================================================================
	/// </summary>
	private void AbortarACenaDeFusao(CenaDeFusao c, string motivo)
	{
		SoltarDaCenaDeFusao(c);

		foreach (ServerPlayer p in new[] { c.Dono, c.Passageiro })
		{
			if (!_players.ContainsKey(p.Id)) continue;
			p.Combate.Stun = 0;
			Avisar(p, $"a luz se apaga e voces continuam dois: {motivo}");
		}

		GD.Print($"[server] CENA DA FUSAO abortada ({c.Dono.Name} x {c.Passageiro.Name}): {motivo}");
	}

	// =====================================================================
	// 4. FUNDIR
	// =====================================================================
	// =====================================================================
	// O CORPO DA FUSAO -- `fusion_fresh_body` / `_snapshot_lopped` / `_restore_lopped`
	// =====================================================================
	/// <summary>
	/// `fusion_snapshot_lopped()` (`Fusion.dm:41-46`): quais membros JA FALTAVAM antes de fundir.
	///
	/// Por NOME e nao por indice: o `Body.Novo` monta a lista com ou sem rabo conforme a raca
	/// (`Body.cs:171`), entao um indice significaria coisas diferentes em corpos diferentes -- e o
	/// unico consumidor desta lista, o <see cref="RedeceparOsMembrosQueFaltavam"/>, procura pelo mesmo
	/// `Corpo.Achar(nome)` que o resto do servidor ja usa.
	/// </summary>
	private static List<string> MembrosQueFaltamEm(ServerPlayer p)
	{
		if (p.Combate is not { } c) return [];

		var l = new List<string>();
		foreach (Jandirus.Core.Combat.BodyPart m in c.Corpo.Partes)
			if (m.Decepado) l.Add(m.Nome);
		return l;
	}

	/// <summary>
	/// `fusion_fresh_body()` (`Fusion.dm:36-39`): **a fusao e uma pessoa nova** -- todo membro volta e
	/// a vida vai ao maximo.
	///
	/// ============================ ELE E O `Restaurar` QUE O ALEM JA USA ============================
	/// `Corpo.Restaurar()` faz exatamente as duas linhas do original (`if(B.lopped) B.RegrowLimb()` e
	/// `B.health = B.maxhealth`), e ele ja e o caminho da morte (`GameServer.Alem.cs:335`) e da
	/// absorcao. Uma segunda versao disto aqui seria a segunda verdade sobre o que "corpo inteiro"
	/// quer dizer.
	///
	/// A `SincronizarVida` e o RABO vem junto pela mesma razao que vem la: o `Ficha.HP` e a media das
	/// partes e nao se recalcula sozinho, e o `tailgain` do Saiyajin muda quando o rabo volta.
	/// ==========================================================================================
	/// </summary>
	private static void CorpoNovoDaFusao(ServerPlayer p)
	{
		if (p.Combate is not { } c) return;
		c.Corpo.Restaurar();
		c.SincronizarVida();
		AjustarGanhoDoRabo(p);
	}

	/// <summary>
	/// `fusion_restore_lopped(types)` (`Fusion.dm:47-53`): re-deceper na saida o que faltava na
	/// entrada -- `lopped = 1`, `health = 0`, as tres linhas do original.
	///
	/// **E ele e a metade que impede o exploit.** Ver <see cref="FusaoAtiva.MembrosQueFaltavam"/>: sem
	/// esta funcao, o corpo novo da fusao viraria cura de graca e a amputacao permanente do jogo
	/// deixaria de existir.
	///
	/// ============================ SEM CASCATA, E ISSO E DE PROPOSITO ============================
	/// O <see cref="Jandirus.Core.Combat.Body.Decepar"/> leva junto o que estava DENTRO do membro (a
	/// mao vai com o braco). Aqui nao ha o que levar: a lista fotografada ja contem os aninhados que
	/// estavam decepados, um por um, porque ela foi tirada do estado FINAL do corpo. Chamar o
	/// `Decepar` aqui repetiria a cascata e cobraria Ki do dono (`Regras.CustoDeceparKi`) por um
	/// membro que ele nunca teve de volta.
	/// ======================================================================================
	/// </summary>
	private static void RedeceparOsMembrosQueFaltavam(ServerPlayer p, List<string> nomes)
	{
		if (nomes.Count == 0 || p.Combate is not { } c) return;

		foreach (string nome in nomes)
			if (c.Corpo.Achar(nome) is { } m) { m.Decepado = true; m.Vida = 0; }

		c.SincronizarVida();
		AjustarGanhoDoRabo(p);
	}

	/// <summary>Os oito stats crus, na ordem em que o <see cref="FusaoAtiva.StatsDoDono"/> guarda.</summary>
	private static double[] StatsDe(Jandirus.Core.Stats.Fighter f) =>
		[f.physoff, f.physdef, f.technique, f.kioff, f.kidef, f.kiskill, f.speed, f.magiskill];

	private static void PorStats(Jandirus.Core.Stats.Fighter f, double[] s)
	{
		f.physoff = s[0]; f.physdef = s[1]; f.technique = s[2]; f.kioff = s[3];
		f.kidef = s[4]; f.kiskill = s[5]; f.speed = s[6]; f.magiskill = s[7];
	}

	// =====================================================================
	// A APARENCIA DA FUSAO -- roupa e cabelo
	// =====================================================================
	/// <summary>
	/// O `res://` DA PECA QUE A FUSAO VESTE, pelo NOME do arquivo. Nulo = a arte nao existe.
	///
	/// ============================ DUAS PORTAS, E A SEGUNDA NAO E PREGUICA ============================
	/// A primeira e o catalogo (<see cref="Jandirus.Core.Appearance.VisualCatalog.Peca"/>), que e a
	/// porta certa e a que o `Metamoran Vest` usa: por NOME, entao mover a pasta de arte nao quebra nada
	/// e a arte SUMIR devolve nulo -- que e o unico jeito de a falta ser denunciada em vez de silenciosa.
	///
	/// A segunda existe pro `potara`, e ela nao contorna a primeira: o brinco **nao esta no catalogo de
	/// proposito**. O extrator o recusa por nome (`DmAppearanceScanner.Varrer`, a lista `fora`), porque a
	/// pasta `Clothes/` do jogo e o deposito de todo overlay de corpo -- olho, halo, rabo, brinco -- e
	/// varre-la inteira poria "olhos" na grade de camisas da criacao. **Aquela regra esta certa e nao vai
	/// mudar**; o que muda e que a fusao nao esta pedindo uma roupa de guarda-roupa, esta pedindo um
	/// overlay -- a mesma coisa que o `Fusion.dm:218` faz com `potara.dmi`.
	///
	/// O precedente de escrever `res://` deste lado da casa e o `GameServer.Feridas.cs:81`, e a fronteira
	/// e a mesma: quem escolhe a peca e o Core (`Fusao.PecaDe`), quem sabe onde ela mora e o servidor.
	/// ============================================================================================
	/// </summary>
	private string? CaminhoDaPecaDeFusao(string nomeDoArquivo)
	{
		if (nomeDoArquivo.Length == 0) return null;
		if (_visual?.Peca(nomeDoArquivo) is { } doCatalogo) return doCatalogo;

		string direto = $"res://Assets/Sprites/Clothes/{nomeDoArquivo}.tres";

		// PERGUNTA AO BANCO DE RECURSOS IMPORTADOS, e nao ao disco. Este projeto ja perdeu tempo duas
		// vezes com arte que estava na pasta e que o Godot nunca importou (os 35 atlas, depois os sons):
		// `ResourceLoader.Exists` e a unica pergunta que corresponde ao que o jogo vai conseguir
		// carregar. Ver `ColadasDeForma.Existe`, que faz a mesma e diz o mesmo.
		if (ResourceLoader.Exists(direto)) return direto;

		// A FALTA E DENUNCIADA UMA VEZ, EM LOG, e a fusao continua acontecendo com a roupa de quem
		// convidou (ver `Fusao.RoupaDaFusao`). Abortar a fusao por causa de um sprite seria punir o
		// jogador por um problema de pipeline.
		GD.PushWarning($"[fusao] a arte '{nomeDoArquivo}' nao esta no catalogo nem importada -- "
					 + "a fusao vai sair sem ela.");
		return null;
	}

	/// <summary>
	/// QUEM A FUSAO E, do lado de fora: a aparencia que o mundo ve enquanto ela dura.
	///
	/// ============================ ELA NAO ENCOSTA EM `pl.Visual` ============================
	/// O objeto sai de um <see cref="Jandirus.Core.Appearance.Appearance.Copiar"/> do dono e vive no
	/// <see cref="ServerPlayer.LookDeFusao"/> -- ver o campo pros dois motivos (o salvamento periodico de
	/// 2 minutos, e o `Sanear` que descartaria o brinco). Nada aqui pode ser gravado, e nada aqui passa
	/// por saneamento.
	/// ====================================================================================
	///
	/// AS TRES REGRAS DO DONO, nesta ordem:
	///   * **o corpo e o de quem convidou** -- raca, tom, cor de pele, olho, corpo do Frost, chama e cor
	///     de ki vem todos do `Copiar()`, e nao ha uma linha decidindo isso: e o que "a fusao e da RACA
	///     de quem convidou" quer dizer no desenho;
	///   * **o cabelo** -- Goku + Vegeta da Vegito, e o resto fica com o penteado de quem convidou
	///     (`Fusao.CabeloDaFusao`);
	///   * **a roupa** -- a Danca SUBSTITUI pelo colete, a Potara SOMA o brinco (`Fusao.RoupaDaFusao`).
	/// </summary>
	private Jandirus.Core.Appearance.Appearance AparenciaDaFusao(
		ServerPlayer dono, ServerPlayer passageiro, TipoDeFusao tipo)
	{
		Jandirus.Core.Appearance.Appearance ap = dono.Visual.Copiar();

		ap.Cabelo = Fusao.CabeloDaFusao(dono.Visual.Cabelo, passageiro.Visual.Cabelo);

		// A COR DO CABELO SAI JUNTO COM O PENTEADO, e so quando ele muda. O Vegito e um cabelo de
		// Saiyajin -- preto, com os realces desenhados --, e a tinta que estava armada pro penteado
		// anterior (o loiro de um Humano, por exemplo) cairia SOMADA em cima dele. Quando o penteado
		// continua sendo o de quem convidou, a cor dele continua tambem: e o mesmo cabelo.
		if (!string.Equals(ap.Cabelo, dono.Visual.Cabelo, StringComparison.Ordinal)) ap.CorCabelo = null;

		ap.Roupa = Fusao.RoupaDaFusao(tipo, dono.Visual.Roupa, CaminhoDaPecaDeFusao(Fusao.PecaDe(tipo)));
		return ap;
	}

	/// <summary>
	/// PENDURA A APARENCIA DA FUSAO no corpo que esta no controle, e reapresenta ele a zona.
	///
	/// UM METODO E NAO DUAS COPIAS: o <see cref="Fundir"/> e o <see cref="PassarOControle"/> fazem a
	/// mesma coisa, e a segunda copia envelheceria calada no dia em que uma regra de aparencia entrasse
	/// -- que e o defeito mais repetido deste port inteiro.
	/// </summary>
	private void VestirAFusao(FusaoAtiva f)
	{
		f.Dono.LookDeFusao = AparenciaDaFusao(f.Dono, f.Passageiro, f.Tipo);
		f.Dono.NomeDeFusao = f.NomeDaFusao;
		TrocarAparencias(f.Dono);   // o nome, a roupa, o cabelo e o bit da fusao viajam no `PeerLook`
	}

	/// <summary>
	/// TIRA A APARENCIA DA FUSAO e devolve o corpo ao que ele era.
	///
	/// **NAO "recalcula a aparencia certa": DESCARTA a emprestada.** A aparencia de verdade nunca foi
	/// tocada (esse e o desenho inteiro do <see cref="ServerPlayer.LookDeFusao"/>), entao devolver e
	/// apagar um campo -- e nao ha caminho em que o jogador acorde com o colete metamoriano vestido
	/// porque alguem esqueceu de reverter uma peca.
	/// </summary>
	private void DespirADeFusao(ServerPlayer p)
	{
		p.LookDeFusao = null;
		p.NomeDeFusao = "";
	}

	/// <summary>
	/// `Fuse()` (`Fusion.dm:260`): os dois viram um. <paramref name="dono"/> e quem convidou, e
	/// **quem convidou controla** -- ver o cabecalho deste arquivo.
	/// </summary>
	private void Fundir(ServerPlayer dono, ServerPlayer passageiro, TipoDeFusao tipo, bool estragada)
	{
		// A ULTIMA TRAVA. Entre a resolucao da danca e esta linha passa um tique inteiro, e dois
		// convites cruzados poderiam chegar aqui ao mesmo tempo -- o `fusing_now` do DM
		// (`Fusion.dm:29`) existe exatamente por isso, e a razao de la vale igual aqui.
		if (_fundidos.ContainsKey(dono.Id) || _fundidos.ContainsKey(passageiro.Id)) return;

		var f = new FusaoAtiva
		{
			Dono = dono,
			Passageiro = passageiro,
			Tipo = tipo,
			Estragada = estragada,
			EnergiaMax = Fusao.EnergiaMaxima(tipo),
			ZonaDoPassageiro = passageiro.Zone,
			PosDoPassageiro = passageiro.Pos,
			UltimoDreno = NowMs(),
			StatsDoDono = StatsDe(dono.Ficha),
		};
		f.Energia = f.EnergiaMax;

		// ---- 1. O PODER (`Fusion.dm:264-266`) ----
		double baseDaFusao = Fusao.BpBase(dono.Ficha.BP, passageiro.Ficha.BP, estragada);
		f.DeltaDeBp = baseDaFusao - dono.Ficha.BP;
		dono.Ficha.FuseBuff += f.DeltaDeBp;

		// ============================ O `FuseBuff` ESTAVA ORFAO, E AGORA TEM QUEM O ESCREVA ============================
		// Ele e lido pela conta de poder desde sempre (`Fighter.Power.cs:78`, na soma da base) e
		// **nunca era escrito por ninguem** -- `grep` por `FuseBuff +=` no port inteiro devolvia so a
		// declaracao. Ou seja: o encanamento da fusao ja estava de pe e somava zero. Esta linha e o
		// que o acende, e por ela ser SOMA NA BASE (familia 2 da taxonomia de buffs) o poder da fusao
		// nao se multiplica com forma nem com raiva -- que e o certo, e o que o DM tambem faz.
		// =========================================================================================================

		// ============================ A NAMEKUSEIJIN NAO HERDA, E A DECISAO NAO E MINHA ============================
		// Os passos 2 e 3 (o maior stat de cada, as skills dos dois) sao PEDIDO NOVO do dono e nao
		// existem no DM. Ele nunca disse se valem pra fusao PERMANENTE -- ver
		// `Fusao.HerancaNaFusaoNamekuseijin`, que e o interruptor e a explicacao. Enquanto ele nao
		// responde, a Namekuseijin sai como o original a escreve.
		// ======================================================================================================
		bool herda = tipo != TipoDeFusao.Namek || Fusao.HerancaNaFusaoNamekuseijin;

		// ---- 2. OS STATS: O MAIOR DE CADA (pedido do dono; nao existe no DM) ----
		// *"se jogador 1 tem 30 de physical e o 2 tem 40, a fusao tem 40"*. Nos stats CRUS e nao nos
		// efetivos: os efetivos ja carregam estilo, forma e buffs temporarios, e copiar um numero ja
		// temperado deixaria o tempero preso no corpo depois que a fonte dele acabasse.
		if (herda)
		{
			double[] meus = StatsDe(dono.Ficha), dele = StatsDe(passageiro.Ficha);
			for (int i = 0; i < meus.Length; i++) meus[i] = Math.Max(meus[i], dele[i]);
			PorStats(dono.Ficha, meus);
		}

		// ---- 3. AS SKILLS DOS DOIS (pedido do dono; nao existe no DM) ----
		// So o que ele NAO tinha entra na lista de emprestimo -- assim o `Separar` devolve exatamente
		// o que pegou e nunca apaga uma skill que o dono ja tinha comprado.
		if (herda && dono.Livro != null && passageiro.Livro != null)
			foreach (string path in passageiro.Livro.Aprendidas)
			{
				if (dono.Livro.Sabe(path)) continue;
				dono.Livro.Dar(path);
				f.SkillsEmprestadas.Add(path);
			}

		// ============================ 3b. O CORPO SE REFAZ INTEIRO -- `Fusion.dm:276-277` ============================
		// `KeeperLoppedTypes = Keeper.fusion_snapshot_lopped()` seguido de `Keeper.fusion_fresh_body()`,
		// nessa ordem: **fotografa primeiro, cura depois**. Invertido, a foto sairia de um corpo ja
		// inteiro e o `Separar` nao teria o que devolver -- e a fusao viraria cura de graca, que e o
		// que o comentario do original chama de *"no free regen exploit"* (`:41`).
		//
		// **ISTO NAO ESTAVA PORTADO.** O `Fundir` mexia em poder, stats, skills, nome, selo, aparencia
		// -- e nao encostava em membro nenhum. Quem fundisse sem um braco via a fusao nascer sem o
		// braco, num sistema que ja desenha membro arrancado no sprite.
		// =========================================================================================================
		f.MembrosQueFaltavam = MembrosQueFaltamEm(dono);
		CorpoNovoDaFusao(dono);

		// ============================ E O QUE **NAO** ATRAVESSA JUNTO: a Respiracao no Vacuo ============================
		// O DM tem uma quarta linha nesse mesmo bloco (`Fusion.dm:278-279`): `if(Loser.spacebreather)
		// Keeper.spacebreather = 1`, desfeita em `:317`. Ela nao entra aqui, e a razao e de MODELO e nao
		// esquecimento: no port o folego no vacuo nao e um campo do corpo, e uma funcao PURA da raca,
		// do cargo e do traje (`Core/World/Vacuo.RespiraNoVacuo`). Nao ha bit pra copiar nem pra
		// devolver -- conceder isso pediria um "folego emprestado" novo no `ServerPlayer`, com o
		// `GameServer.Vacuo` aprendendo a le-lo. E divida anotada, e nao um `if` a mais neste metodo.
		// ==========================================================================================================

		// ---- 4. O NOME ----
		f.NomeDaFusao = Fusao.NomeDaFusao(tipo, dono.Name, passageiro.Name);

		_fusoes.Add(f);
		_fundidos[dono.Id] = f;
		_fundidos[passageiro.Id] = f;

		// ---- 5. O PASSAGEIRO SAI DO MUNDO ----
		// `Loser.GotoPlanet("Sealed")` (`Fusion.dm:290`). Ver `ZonaDoSelo`.
		MoveToZone(passageiro.Id, ZonaDoSelo(dono.Id), PosDoSelo);

		// ---- 6. O CORPO SE REFAZ ----
		// Recalcula poder, reaplica os efeitos das skills que chegaram, remanda o livro e a ficha.
		// Sem a trinca, as skills do outro entrariam no livro e **nao aconteceria nada**: sem botao,
		// sem buff, sem bit de poder -- o mesmo alerta que o ensino de skill ja escreve.
		AplicarPoderes(dono);
		AplicarEfeitos(dono);
		MandarSkills(dono, forcar: true);
		MandarFicha(dono);
		MandarAtributos(dono);

		// ---- 7. QUEM ELA E POR FORA ----
		// O nome, o cabelo, a roupa e o bit da fusao, os quatro pelo mesmo `PeerLook`. Ver
		// `VestirAFusao` -- e ver `ServerPlayer.LookDeFusao` pra saber por que nada disso encosta na
		// aparencia que vai pro disco.
		VestirAFusao(f);

		string comoFoi = estragada ? " -- e o resultado e uma piada de mau gosto" : "";
		foreach (ServerPlayer o in ZoneList(dono.Zone.Hash))
			Avisar(o, $"{dono.Name} e {passageiro.Name} se fundem em {f.NomeDaFusao}{comoFoi}!");

		Avisar(dono, estragada
			? $"voce e {f.NomeDaFusao}. A fusao saiu errada: voce esta mais fraco que qualquer um dos "
			+ "dois sozinho, e nao consegue se transformar. Aguente ate a energia acabar."
			: $"voce e {f.NomeDaFusao}! Voce controla o corpo -- use 'Passar o controle' pra entregar "
			+ "o volante ao seu outro lado.");
		// A FUSAO PERMANENTE NAO PROMETE VOLTA. `EnergiaMax == 0` e o que o DM chama de permanente
		// (`Fusion.dm:271`), e e o mesmo numero que faz o dreno pular esta fusao e o nocaute nao a
		// separar. Dizer "voce volta quando a fusao acabar" pra quem entrou numa Namekuseijin seria a
		// unica frase mentirosa do sistema -- e a que o jogador so descobriria falsa horas depois.
		Avisar(passageiro, f.EnergiaMax > 0
			? $"voce se dissolve em {f.NomeDaFusao}. Quem dirige e {dono.Name}; "
			+ "voce volta quando a fusao acabar."
			: $"voce se dissolve em {f.NomeDaFusao}, e e PRA SEMPRE. Quem dirige e {dono.Name} -- "
			+ "peca o controle a ele se quiser dirigir a sua vez.");

		GD.Print($"[server] FUSAO {tipo}{(estragada ? " ESTRAGADA" : "")}: {dono.Name} + {passageiro.Name} "
			   + $"= {f.NomeDaFusao} | BP base {baseDaFusao:N0} | energia {f.EnergiaMax:0}");
	}

	/// <summary>
	/// O SELO -- pra onde o passageiro vai. O `GotoPlanet("Sealed")` do DM (`Fusion.dm:290`).
	///
	/// ============================ UM BOLSO POR FUSAO, E A PLANTA E A DA MENTE ============================
	/// A chave e `Interior("Selado", id do dono)`: um bolso por fusao, o que resolve sozinho o caso de
	/// duas fusoes acontecerem no mesmo servidor -- os dois passageiros nao se encontram.
	///
	/// A PLANTA E EMPRESTADA da dimensao mental (o quarto branco vazio) porque e literalmente o mesmo
	/// lugar: um vazio sem saida onde ha uma pessoa so. Desenhar um segundo quarto branco identico
	/// seria uma segunda copia da mesma coisa, e a primeira a divergir.
	/// ==================================================================================================
	///
	/// ============================ DIVIDA ABERTA: O `Set_Fusion_View` DO PASSAGEIRO ============================
	/// **Ela nao foi paga, e nao foi paga inteira de proposito.** O DM da ao passageiro um verb
	/// (`Fusion.dm:502-511`) que e um INTERRUPTOR de camera:
	///
	///     usr.client.perspective = EYE_PERSPECTIVE
	///     usr.client.eye = usr.Fusee.client.mob
	///
	/// Concedido no `Fuse()` (`:282-283`), retirado no `Defuse()` (`:324-329`) e trocado de mao no
	/// `PassControl` (`:380-381`, `:390-391`). Com ele, quem entrega o corpo **assiste a luta pelos
	/// olhos da propria fusao**. Sem ele -- que e o estado de hoje -- metade dos jogadores de toda
	/// fusao fica olhando um quarto branco e vazio por ate 30 minutos (Potara) ou 15 (Danca), e
	/// PARA SEMPRE na Namekuseijin. Combinado com *"quem convida controla"*, o convidado hoje so perde:
	/// entrega o corpo e nao ganha nem o espetaculo.
	///
	/// ============================ POR QUE NAO CABE EM UMA LINHA AQUI ============================
	/// No BYOND `client.eye` e uma atribuicao porque o servidor DESENHA. Aqui o cliente desenha, e ele
	/// so recebe os pacotes da PROPRIA zona -- e a zona do passageiro e este bolso, que esta vazio.
	/// Portar o verb pede tres coisas que sao de REDE e nao de fusao:
	///
	///   1. um canal S2C dizendo *"sua camera segue a entidade N"*, com o cliente aceitando seguir um
	///      corpo que nao e o dele (hoje a camera e filha do `LocalPlayer`);
	///   2. o servidor mandando ao passageiro os snapshots da zona do DONO -- ou seja, um jogador
	///      recebendo duas zonas, que nenhum caminho deste port faz hoje;
	///   3. o corpo dele continuando congelado no bolso enquanto a camera passeia.
	///
	/// **Meia entrega aqui seria pior que nenhuma**: um `fus_ver` que so troca a camera desenharia um
	/// vazio (a zona do dono nao esta carregada no cliente dele), e o jogador leria isso como o verb
	/// quebrado em vez de ausente.
	///
	/// A ALTERNATIVA QUE EXISTE, medida e nao adivinhada: manter o passageiro na zona do DONO com o bit
	/// `Oculto` (que o snapshot ja carrega, `GameServer.cs:4972`) e a posicao colada na dele, em vez de
	/// manda-lo pro bolso. Ai a camera dele seguiria o proprio corpo e ele veria a luta sem canal novo.
	/// O preco e o que essa mudanca arrasta: colisao, alvo de IA, `ZoneList`, a troca de zona da fusao
	/// levando o passageiro junto, e o `Persistir` -- e ela apaga este bolso, que e um porte fiel do
	/// `GotoPlanet("Sealed")` e tem bancada em cima. E outra tarefa, e nao um remendo desta.
	/// ======================================================================================================
	/// </summary>
	internal const string NomeDoSelo = "Selado";

	internal static ZoneKey ZonaDoSelo(int idDoDono) => ZoneKey.Interior(NomeDoSelo, (ulong)idDoDono);

	internal static bool EhOSelo(ZoneKey z) =>
		z.Kind == ZoneKey.KindInterior && string.Equals(z.Name, NomeDoSelo, StringComparison.Ordinal);

	/// <summary>
	/// ONDE O PASSAGEIRO NASCE DENTRO DO SELO -- o MESMO ponto em que quem medita nasce na planta.
	///
	/// **Nao e `new Vec2(1600, 1600)` escrito a mao**, que era o que estava aqui: 1600 px so e o meio
	/// enquanto o quarto tiver 100 tiles de lado (`DimensaoMental.Lado`), e no dia em que ele mudasse
	/// de tamanho o passageiro nasceria fora do mapa -- calado, e so quando alguem fundisse.
	/// Derivado da mesma celula que a planta ja usa, ele acompanha.
	/// </summary>
	private static readonly Vec2 PosDoSelo =
		DimensaoMental.PixelDe(DimensaoMental.CelDeQuemMedita);

	// =====================================================================
	// 5. SEPARAR
	// =====================================================================
	/// <summary>
	/// `Defuse()` (`Fusion.dm:296`): desfaz TUDO o que o <see cref="Fundir"/> fez, na ordem inversa,
	/// e devolve os dois corpos.
	///
	/// ============================ A RECARGA E DOS DOIS, SEMPRE ============================
	/// `Fusion.dm:320` e `:334` -- o dono E o passageiro pegam 1 h cada um. Cobrar so de quem
	/// controlava faria do passageiro um recurso reutilizavel: fundir, separar, fundir de novo com
	/// outra pessoa. A espera e do CORPO que se desfez, e sao dois corpos.
	/// ======================================================================================
	/// </summary>
	private void Separar(FusaoAtiva f, string motivo)
	{
		_fusoes.Remove(f);
		_fundidos.Remove(f.Dono.Id);
		_fundidos.Remove(f.Passageiro.Id);

		long agora = NowMs();

		ServerPlayer dono = f.Dono, passageiro = f.Passageiro;
		bool donoNoMundo = _players.ContainsKey(dono.Id);
		bool passageiroNoMundo = _players.ContainsKey(passageiro.Id);

		if (donoNoMundo)
		{
			dono.Ficha.FuseBuff -= f.DeltaDeBp;
			PorStats(dono.Ficha, f.StatsDoDono);
			DespirADeFusao(dono);   // o nome, a roupa e o cabelo emprestados saem juntos

			if (dono.Livro != null)
				foreach (string path in f.SkillsEmprestadas) dono.Livro.Esquecer(path);

			// OS MEMBROS QUE FALTAVAM VOLTAM A FALTAR -- `Keeper.fusion_restore_lopped(...)`
			// (`Fusion.dm:318`). O corpo inteiro era da FUSAO e nao dele; ver
			// `FusaoAtiva.MembrosQueFaltavam`. **A vida ganha na fusao ele fica** (o DM tambem: o
			// `fresh_body` poe `health = maxhealth` e nada a devolve), e isso e coerente -- o que a
			// fusao empresta e o corpo novo, e o que ela cobra de volta e a amputacao.
			RedeceparOsMembrosQueFaltavam(dono, f.MembrosQueFaltavam);

			CobrarARecargaDeFusao(dono, agora);

			AplicarPoderes(dono);
			AplicarEfeitos(dono);
			MandarSkills(dono, forcar: true);
			MandarFicha(dono);
			MandarAtributos(dono);
			TrocarAparencias(dono);
			Avisar(dono, $"voce se parte em dois de novo ({motivo}).");
		}

		if (passageiroNoMundo)
		{
			CobrarARecargaDeFusao(passageiro, agora);

			// ============================ ELE VOLTA AO LADO DO OUTRO, E NAO DE ONDE SAIU ============================
			// O DM guarda a `LoserBackupLoc` e devolve o passageiro pra la (`Fusion.dm:330`). Aqui a
			// volta e pro lado do dono -- e a divergencia e de olho aberto: uma fusao anda pelo mundo,
			// e "separar" e os dois corpos se descolando ONDE a fusao estava. Com a regra do DM, uma
			// Potara que atravessou o espaco cuspiria o passageiro no planeta onde ele fundiu, sozinho,
			// a horas de distancia do proprio corpo -- que e o oposto do que o jogador entende por
			// "voces se separaram".
			//
			// A `ZonaDoPassageiro` continua guardada porque ela e o PLANO B: com o dono fora do mundo
			// (ele deslogou, ele morreu e sumiu), o passageiro nao tem ao lado de quem nascer.
			// ==================================================================================================
			ZoneKey volta = donoNoMundo ? dono.Zone : f.ZonaDoPassageiro;
			Vec2 pos = donoNoMundo ? dono.Pos : f.PosDoPassageiro;
			MoveToZone(passageiro.Id, volta, pos);
			MandarFicha(passageiro);
			Avisar(passageiro, $"voce volta a ter corpo proprio ({motivo}).");
		}

		GD.Print($"[server] FUSAO desfeita ({f.NomeDaFusao}): {motivo}");
	}

	/// <summary>A fusao deste corpo, se houver. Serve pro nocaute, pra morte e pro logout.</summary>
	private FusaoAtiva? FusaoDe(int id) => _fundidos.GetValueOrDefault(id);

	/// <summary>
	/// ESTE CORPO ESTA DENTRO DE UMA FUSAO? (dono OU passageiro).
	///
	/// Existe por um consumidor de fora deste arquivo: o <see cref="Persistir"/>, que **nao grava corpo
	/// fundido** -- ver o item 3 do `&lt;remarks&gt;` de la, que lista as tres coisas que o save nao sabe
	/// descrever (a zona do passageiro, os stats emprestados e o `FuseBuff`).
	/// </summary>
	private bool EstaFundido(int id) => _fundidos.ContainsKey(id);

	/// <summary>
	/// ESTE CORPO ESTA NUMA FUSAO ESTRAGADA? E a pergunta que barra a transformacao -- ver
	/// <see cref="AscendePorDecisao"/>.
	/// </summary>
	private bool EmFusaoEstragada(ServerPlayer pl) =>
		_fundidos.TryGetValue(pl.Id, out FusaoAtiva? f) && f.Estragada && f.Dono == pl;

	/// <summary>
	/// `defuse_on_downed()` (`Fusion.dm:75`): **cair separa a fusao**. Chamado do nocaute e da morte.
	///
	/// A Namekuseijin e a excecao no DM (`:79`) e continua sendo aqui: ela e permanente, e um
	/// nocaute nao desfaz o que nem o tempo desfaz.
	/// </summary>
	private void FusaoAoCair(ServerPlayer pl, string motivo)
	{
		if (FusaoDe(pl.Id) is not { } f) return;
		if (f.Tipo == TipoDeFusao.Namek) return;
		Separar(f, motivo);
	}

	// =====================================================================
	// 6. PASSAR O CONTROLE
	// =====================================================================
	/// <summary>
	/// `Pass_Fusion_Control` (`Fusion.dm:513`) + `PassControl()` (`:359`): o volante troca de mao.
	///
	/// ============================ AQUI ELE E UMA TROCA DE PAPEIS, E SO ============================
	/// No DM o `PassControl` refaz meia fusao na mao: tira o buff do antigo, troca as assinaturas,
	/// sela um, solta o outro, recalcula o delta e reaplica os overlays -- 45 linhas em que e facil
	/// esquecer um campo. Aqui a fusao inteira ja e um objeto com TUDO o que foi aplicado guardado
	/// dentro (ver <see cref="FusaoAtiva"/>), entao a troca e literalmente: desfaz no antigo, aplica
	/// no novo, com as MESMAS funcoes que o `Fundir` e o `Separar` usam.
	///
	/// **O BP NAO E O MESMO DEPOIS DA TROCA, e isso e o certo**: a base da fusao foi congelada em
	/// `(A+B)*2` no instante em que ela nasceu, e ela nao muda -- o que muda e o DELTA, porque o
	/// delta e "quanto falta pro BP DESTE corpo chegar la". E exatamente o que o DM faz
	/// (`Fusion.dm:398`), e por isso a fusao expressa o mesmo poder nas duas maos.
	/// ==========================================================================================
	/// </summary>
	private void PassarOControle(ServerPlayer pl)
	{
		if (FusaoDe(pl.Id) is not { } f) { Avisar(pl, "voce nao esta fundido."); return; }
		if (f.Dono != pl) { Avisar(pl, "quem esta no controle e o seu outro lado."); return; }

		ServerPlayer novo = f.Passageiro, antigo = f.Dono;
		if (!_players.ContainsKey(novo.Id) || novo.Peer == null)
		{
			Avisar(pl, "seu outro lado nao esta disponivel pra assumir.");
			return;
		}

		// A BASE DA FUSAO, RECONSTITUIDA a partir do que foi aplicado -- e nao recalculada dos BPs de
		// agora. Ver o cabecalho da `FusaoAtiva`.
		double baseDaFusao = antigo.Ficha.BP + f.DeltaDeBp;

		// ---- desfaz no antigo ----
		antigo.Ficha.FuseBuff -= f.DeltaDeBp;
		PorStats(antigo.Ficha, f.StatsDoDono);
		DespirADeFusao(antigo);
		if (antigo.Livro != null)
			foreach (string path in f.SkillsEmprestadas) antigo.Livro.Esquecer(path);

		// E O CORPO DELE VOLTA A SER O DELE -- `oldK.fusion_restore_lopped(KeeperLoppedTypes)`
		// (`Fusion.dm:372`). Passar o controle e uma separacao pela metade: o corpo que sai de cena e
		// o corpo de uma pessoa, e ele leva de volta as proprias faltas.
		RedeceparOsMembrosQueFaltavam(antigo, f.MembrosQueFaltavam);

		ZoneKey ondeEstavaOCorpo = antigo.Zone;
		Vec2 posDoCorpo = antigo.Pos;

		// ---- troca os papeis ----
		f.Dono = novo;
		f.Passageiro = antigo;
		_fundidos[novo.Id] = f;
		_fundidos[antigo.Id] = f;

		// ---- aplica no novo ----
		f.StatsDoDono = StatsDe(novo.Ficha);
		f.DeltaDeBp = baseDaFusao - novo.Ficha.BP;
		novo.Ficha.FuseBuff += f.DeltaDeBp;

		// A MESMA PERGUNTA DO `Fundir`, e ela tem que ser a mesma: a heranca de stats e skills e pedido
		// novo do dono e ele nao a respondeu pra fusao PERMANENTE. Ver `Fusao.HerancaNaFusaoNamekuseijin`.
		// Se este funil divergisse do outro, passar o controle numa Namekuseijin CONCEDERIA o que fundir
		// nao concedeu.
		bool herda = f.Tipo != TipoDeFusao.Namek || Fusao.HerancaNaFusaoNamekuseijin;

		if (herda)
		{
			double[] meus = StatsDe(novo.Ficha), dele = StatsDe(antigo.Ficha);
			for (int i = 0; i < meus.Length; i++) meus[i] = Math.Max(meus[i], dele[i]);
			PorStats(novo.Ficha, meus);
		}

		f.SkillsEmprestadas.Clear();
		if (herda && novo.Livro != null && antigo.Livro != null)
			foreach (string path in antigo.Livro.Aprendidas)
			{
				if (novo.Livro.Sabe(path)) continue;
				novo.Livro.Dar(path);
				f.SkillsEmprestadas.Add(path);
			}

		// E O CORPO DO NOVO DONO SE REFAZ -- `KeeperLoppedTypes = Keeper.fusion_snapshot_lopped()` +
		// `Keeper.fusion_fresh_body()` (`Fusion.dm:402-403`), na mesma ordem do `Fundir`: fotografa
		// antes de curar. A lista e REESCRITA porque o corpo em cena passou a ser outro -- guardar a do
		// antigo aqui devolveria as faltas DELE ao corpo de quem assumiu.
		f.MembrosQueFaltavam = MembrosQueFaltamEm(novo);
		CorpoNovoDaFusao(novo);

		// ---- os corpos trocam de lugar: o novo dono sai do selo e o antigo entra ----
		f.ZonaDoPassageiro = ondeEstavaOCorpo;
		f.PosDoPassageiro = posDoCorpo;
		MoveToZone(novo.Id, ondeEstavaOCorpo, posDoCorpo);
		MoveToZone(antigo.Id, ZonaDoSelo(novo.Id), PosDoSelo);

		foreach (ServerPlayer p in new[] { novo, antigo })
		{
			AplicarPoderes(p);
			AplicarEfeitos(p);
			MandarSkills(p, forcar: true);
			MandarFicha(p);
			MandarAtributos(p);
		}

		// ============================ A APARENCIA E REMONTADA PRO NOVO DONO, E NAO COPIADA ============================
		// O corpo em cena passou a ser OUTRO, entao a roupa e o cabelo tem que ser recalculados a partir
		// dele -- `Fusao.RoupaDaFusao` soma o brinco na roupa de QUEM ESTA NO CONTROLE, e o dono pediu
		// exatamente isso ("potara = `potara.png` + a roupa de quem convidou"; depois da troca, quem
		// dirige e quem convida o mundo a olhar).
		//
		// O NOME NAO MUDA -- `f.NomeDaFusao` foi congelado no `Fundir` e o `VestirAFusao` o repoe. A
		// fusao e a MESMA pessoa com outra mao no volante, e renomea-la aqui trocaria a identidade dela
		// no meio de uma luta. (E o cabelo do Vegito e simetrico: Goku+Vegeta da Vegito nos dois
		// sentidos, entao a troca de controle nao muda o penteado quando ele foi ele que valeu.)
		// ========================================================================================================
		VestirAFusao(f);

		Avisar(novo, $"voce assume o controle de {f.NomeDaFusao}!");
		Avisar(antigo, $"voce entrega o controle de {f.NomeDaFusao} ao seu outro lado.");
		GD.Print($"[server] FUSAO {f.NomeDaFusao}: controle passou de {antigo.Name} pra {novo.Name}");
	}

	// =====================================================================
	// 7. OS BRINCOS POTARA
	// =====================================================================
	/// <summary>
	/// `Equip()` do brinco, reescrito pelo pedido do dono: *"com os brincos no inventario, clicar
	/// neles da a opcao de jogar pro alvo atual; o alvo recebe um pedido que pode aceitar"*.
	///
	/// ============================ O DM FUNDE SOZINHO, E ISSO E O QUE SAI ============================
	/// No original nao ha convite nenhum: o `checkEarringDist()` (`Fusion.dm:649`) varre a `view()` a
	/// cada 5 s procurando o par, e quando acha faz `walk_to` nos DOIS corpos, espera 4 s e funde --
	/// **sem perguntar a ninguem**. Quem esta com o outro brinco e arrastado pelo mapa e fundido a
	/// forca. O dono pediu o oposto, e com razao: *"o alvo recebe um pedido que pode aceitar (igual a
	/// metamoro)"*.
	///
	/// E O ALVO E O MARCADO, e nao o da frente: *"jogar pro alvo atual"*. O alvo marcado com duplo
	/// clique ja e o conceito de "alvo atual" deste port (`ServerPlayer.AlvoId`), e jogar um brinco e
	/// justamente a coisa que se faz a distancia -- ao contrario da Danca, que e colada.
	/// ==========================================================================================
	/// </summary>
	private void OferecerOsBrincos(ServerPlayer pl)
	{
		ServerPlayer? alvo = Marcado(pl);
		if (alvo == null)
		{
			Avisar(pl, "marque um alvo primeiro (duplo clique nele) -- os brincos vao pra quem voce "
					 + "esta mirando.");
			return;
		}
		Convidar(pl, alvo, TipoDeFusao.Potara);
	}

	/// <summary>
	/// OS BRINCOS SAO INSIGNIA DE CARGO -- o Kaioshin ganha ao assumir, e perde ao sair.
	///
	/// ============================ NO DM ELES SAO INALCANCAVEIS ============================
	/// O unico lugar do original que instancia um `Potara_Earring` e um bloco **COMENTADO** da
	/// criacao de personagem (`CharacterCreation.dm:81-87`) -- e mesmo descomentado ele era
	/// `if(Race=="Kai")`, ou seja por RACA na criacao e nao pelo CARGO. O texto de ajuda do proprio
	/// brinco (`Fusion.dm:608`) ainda promete *"Kaioshins start with two"*, mentindo pro jogador ha
	/// anos: nao ha dadiva de cargo, nem loja, nem drop. **A Potara nao existe no jogo do BYOND.**
	///
	/// O dono disse como quer: *"Kaioshins ganham ao virar do rank Kaioshin"*. Entao os brincos
	/// entram pelo mesmo funil do resto do kit do cargo -- o <see cref="ReconciliarDadiva"/>, que e
	/// idempotente e derivado --, e nao por um evento de "virou Kaioshin" que seis caminhos
    /// diferentes teriam que lembrar de disparar.
	///
	/// **E ELES VOLTAM PRO COFRE COM O CARGO.** Insignia nao e loot: quem deixou de ser Kaioshin nao
	/// leva os brincos embora, do mesmo jeito que nao leva o Mistico. E como eles voltam sozinhos
	/// quando o cargo continua de pe, largar os brincos no chao nao e uma maneira de se trancar fora
	/// da propria Potara.
	/// ======================================================================================
	/// </summary>
	private void ReconciliarOsBrincos(ServerPlayer pl)
	{
		if (!EhPessoa(pl)) return;

		// PELA TABELA DE INSIGNIAS e nao pelo kit de skills: o kit e uma lista de TYPEPATHS, e a
		// bancada `--portasteste` afirma que todos os 51 existem no catalogo de skills. Um id de item
		// enfiado la deixaria essa afirmacao vermelha por um motivo que nao tem nada a ver com ela.
		bool deveTer = Jandirus.Core.Ranks.DadivaDeCargo.ItensDe(CargoDe(pl.Conta))
			.Contains(CatalogoDeItens.BrincosPotara, StringComparer.OrdinalIgnoreCase);
		int tem = pl.Mochila.Quantos(CatalogoDeItens.BrincosPotara);

		if (deveTer && tem == 0)
		{
			pl.Mochila.Guardar(CatalogoDeItens.BrincosPotara);
			MandarMochila(pl);
			Avisar(pl, "um par de brincos Potara chega as suas maos com o cargo. Mire alguem e use "
					 + "os brincos pra oferecer a fusao.");
		}
		else if (!deveTer && tem > 0)
		{
			pl.Mochila.Tirar(CatalogoDeItens.BrincosPotara, tem);
			MandarMochila(pl);
			Avisar(pl, "os brincos Potara voltam pro cofre dos Kaioshin junto com o cargo.");
		}
	}

	// =====================================================================
	// 8. O TIQUE -- e e ele quem garante que nada fica preso
	// =====================================================================
	/// <summary>
	/// O TIQUE DA FUSAO, a 1 Hz. Ele faz TRES coisas, e as duas primeiras sao as bordas do pedido:
	///
	///   1. **varre os convites vencidos** -- ninguem fica preso num pedido eterno;
	///   2. **derruba a danca que perdeu um dos dois** (morte, nocaute, queda de conexao, troca de
	///      zona) e a fusao que perdeu um dos dois;
	///   3. **dreana a energia** e separa quando ela acaba (`EnergyLoop`, `Fusion.dm:337`).
	/// </summary>
	private void TickDaFusao()
	{
		long agora = NowMs();

		// ---- 1. OS CONVITES QUE VENCERAM ----
		if (_pedidosDeFusao.Count > 0)
			foreach (int id in _pedidosDeFusao.Keys.ToList())
			{
				PedidoDeFusao p = _pedidosDeFusao[id];
				ServerPlayer? quem = _players.GetValueOrDefault(id);

				// O CONVIDADO SUMIU, MORREU OU CAIU: o pedido cai junto. Aceitar deitado ja seria
				// recusado la na revalidacao -- tirar aqui e so nao deixar lixo no dicionario.
				bool morreu = quem == null || quem.Ficha.dead || quem.Ficha.KO;
				if (agora <= p.Ate && !morreu) continue;

				_pedidosDeFusao.Remove(id);
				if (quem != null && !morreu) Avisar(quem, $"o convite de fusao de {p.Nome} expirou.");
				if (_players.GetValueOrDefault(p.DeQuem) is { } convidou && convidou.Name == p.Nome)
					Avisar(convidou, morreu
						? "seu convite de fusao se perdeu."
						: "seu convite de fusao expirou sem resposta.");
			}

		// ---- 2a. A DANCA ----
		for (int i = _dancas.Count - 1; i >= 0; i--)
		{
			DancaDeFusao d = _dancas[i];

			if (!_players.ContainsKey(d.A.Id) || !_players.ContainsKey(d.B.Id))
			{ AbortarADanca(d, "um dos dois saiu do mundo."); continue; }
			if (d.A.Ficha.dead || d.B.Ficha.dead) { AbortarADanca(d, "um dos dois morreu."); continue; }
			if (d.A.Ficha.KO || d.B.Ficha.KO) { AbortarADanca(d, "um dos dois foi derrubado."); continue; }
			if (d.A.Zone.Hash != d.B.Zone.Hash) { AbortarADanca(d, "voces nao estao mais no mesmo lugar."); continue; }

			// O PRAZO VENCEU: o que faltou conta como erro, e a fusao sai estragada. E o que impede
			// que largar o teclado no meio da danca deixe os dois parados pra sempre.
			if (agora >= d.Acaba)
			{
				if (d.AcertosA < Fusao.LetrasDaDanca) d.FalhouA = true;
				if (d.AcertosB < Fusao.LetrasDaDanca) d.FalhouB = true;
				ResolverADanca(d);
			}
		}

		// ---- 2b. AS LETRAS (no tique cheio, ver `TickDasLetrasDaDanca`) ----

		// ---- 3. A ENERGIA ----
		for (int i = _fusoes.Count - 1; i >= 0; i--)
		{
			FusaoAtiva f = _fusoes[i];

			// UM DOS DOIS SUMIU DO MUNDO. O `Drop` ja separa antes de persistir; isto e a rede por
			// baixo (morte que apagou o corpo, admin, bancada) -- sem ela a fusao ficaria de pe com
			// uma referencia pra alguem que nao existe mais, drenando energia pra ninguem.
			if (!_players.ContainsKey(f.Dono.Id) || !_players.ContainsKey(f.Passageiro.Id))
			{ Separar(f, "um dos dois deixou o mundo"); continue; }

			if (f.Dono.Ficha.dead) { Separar(f, "o corpo fundido caiu de vez"); continue; }
			if (f.EnergiaMax <= 0) { f.UltimoDreno = agora; continue; }   // permanente (Namek)

			double dt = (agora - f.UltimoDreno) / 1000.0;
			f.UltimoDreno = agora;
			if (dt <= 0) continue;

			// O DRENO USA O DELTA DE TEMPO REAL, e nao "um por tique" -- e o `world.realtime` do DM
			// (`Fusion.dm:344-346`). Contando tiques, uma travada do servidor daria minutos de fusao
			// de graca (ou cobraria em dobro na recuperacao).
			double mult = Fusao.MultiplicadorDaFormaAtual(f.Dono.Ficha);
			f.Energia -= Fusao.DrenoPorSegundo(mult) * dt;

			if (f.Energia <= 0) Separar(f, "a energia da fusao acabou");
		}
	}

	/// <summary>
	/// AS LETRAS DA DANCA ANDAM NO TIQUE CHEIO (30 Hz), e nao no de 1 Hz junto com o resto.
	///
	/// Pelo mesmo motivo escrito no `TickDosEmbates`: o prazo de uma letra e de 900 ms e o piso de
	/// cadencia e de 300 ms. A 1 Hz o adiantamento do acerto simplesmente **nao existiria** -- quem
	/// acertasse em 100 ms esperaria o proximo segundo cheio pra receber a proxima letra, e a
	/// promessa de "quanto mais rapido melhor" seria mentira.
	/// </summary>
	private void TickDasLetrasDaDanca()
	{
		if (_dancas.Count == 0) return;
		long agora = NowMs();

		for (int i = _dancas.Count - 1; i >= 0; i--)
		{
			DancaDeFusao d = _dancas[i];

			// O CORPO SEM TECLADO APERTA -- ANTES do prazo, como no ZanzoClash: ele tem que poder
			// responder DENTRO da janela, como qualquer um.
			ResponderPelaMaquinaNaDanca(d, d.A, agora);
			ResponderPelaMaquinaNaDanca(d, d.B, agora);

			if (agora > d.PrazoA && d.LetraA != '\0')
			{
				// DEIXAR VENCER E ERRAR. Numa coreografia nao existe "perdi a vez e continuo
				// dancando": o passo que nao saiu no tempo e o passo errado.
				d.FalhouA = true; d.LetraA = '\0';
				Julgou(d.A, false);
				Avisar(d.A, "voce perdeu o tempo do passo!");
				TalvezAcabarADanca(d);
				continue;
			}
			if (agora > d.PrazoB && d.LetraB != '\0')
			{
				d.FalhouB = true; d.LetraB = '\0';
				Julgou(d.B, false);
				Avisar(d.B, "voce perdeu o tempo do passo!");
				TalvezAcabarADanca(d);
				continue;
			}

			// A PROXIMA LETRA de quem acertou (o prazo dele ja esta no passado pelo adiantamento).
			if (d.LetraA == '\0' && !d.FalhouA && d.AcertosA < Fusao.LetrasDaDanca && agora >= d.PrazoA)
				NovaLetraDaDanca(d, d.A);
			if (d.LetraB == '\0' && !d.FalhouB && d.AcertosB < Fusao.LetrasDaDanca && agora >= d.PrazoB)
				NovaLetraDaDanca(d, d.B);
		}
	}

	/// <summary>
	/// O CORPO SEM TECLADO APERTA A LETRA -- **pelo mesmo funil do jogador** (o
	/// <see cref="TeclaDaDanca"/>), e com a mesma conta de acerto dos outros dois embates
	/// (<see cref="PisoDeAcertoDaMaquina"/>). Ver o `ResponderPelaMaquina` do ZanzoClash: uma
	/// segunda contagem aqui seria a segunda verdade do placar.
	/// </summary>
	private void ResponderPelaMaquinaNaDanca(DancaDeFusao d, ServerPlayer p, long agora)
	{
		if (TemTeclado(p)) return;

		bool ehA = p == d.A;
		char esperada = ehA ? d.LetraA : d.LetraB;
		long quando = ehA ? d.RespondeA : d.RespondeB;
		if (esperada == '\0' || quando == 0 || agora < quando) return;

		if (ehA) d.RespondeA = 0; else d.RespondeB = 0;

		double tino = p.Cerebro?.Inteligencia ?? PisoDeAcertoDaMaquina;
		bool acerta = _rng.NextDouble() < PisoDeAcertoDaMaquina + (1 - PisoDeAcertoDaMaquina) * tino;
		TeclaDaDanca(p, acerta ? esperada : SortearLetraDeEmbate());
	}

	/// <summary>
	/// QUEM SAIU DO MUNDO SOLTA TUDO -- chamado do <see cref="Drop"/>, **antes** do `Persistir`.
	///
	/// ============================ A ORDEM E A REGRA INTEIRA ============================
	/// O save grava a ZONA do personagem, e o passageiro de uma fusao esta num bolso `Interior`
	/// ("Selado") de uma pessoa so, sem porta e sem ninguem. Persistido la dentro, ele **relogaria
	/// preso num quarto branco pra sempre** -- e a unica saida (a fusao) morreu com a sessao. E
	/// exatamente o defeito que o `VoltarDeOndeEstiver` ja evita pra quem desloga meditando, e o
	/// remedio e o mesmo: desfazer antes de gravar.
	///
	/// **E O DM AQUI DIVERGE DE PROPOSITO.** La a fusao SOBREVIVE ao logout (`CheckFusion`,
	/// `Fusion.dm:465-496`: o relog reaplica o visual e religa o `EnergyLoop`), porque la ela mora
	/// num `datum` global salvo com o mundo. Este port nao persiste fusao nenhuma -- e enquanto nao
	/// persistir, deixar a fusao "de pe" com um dos dois offline seria prometer o que o disco nao
	/// guarda: o outro ficaria preso num corpo que o servidor esqueceria no proximo reinicio.
	/// ==================================================================================
	/// </summary>
	private void SoltarDaFusao(int id)
	{
		if (_dancando.TryGetValue(id, out DancaDeFusao? d)) AbortarADanca(d, "um dos dois saiu do jogo.");

		// O PUXAO CAI JUNTO, e ele entra ANTES da cena porque e a fase anterior: quem esta sendo puxado
		// nao esta em `_emCenaDeFusao` nem em `_fundidos`, e sem esta linha o OUTRO ficaria deslizando
		// pra um corpo que nao existe mais -- com o input desligado, ate o prazo do
		// `PuxaoDeFusaoRestante` escorrer. Mesma razao da linha da danca e da linha da cena: o `Drop`
		// chama isto ANTES do `Persistir`.
		if (_sendoPuxadoPraFusao.TryGetValue(id, out PuxaoDeFusao? px))
			AbortarOPuxaoDeFusao(px, "um dos dois saiu do jogo.");

		// A CINEMATICA CAI JUNTO, e ela entra ANTES do `FusaoDe` porque e a fase anterior: quem esta
		// no meio da cena ainda nao esta em `_fundidos`, e sem esta linha o outro ficaria preso
		// esperando uma virada que nunca viria -- 7 segundos de corpo travado e nenhuma fusao, calado.
		//
		// O TIQUE JA PEGARIA ISTO (o corte "um dos dois saiu do mundo"), e mesmo assim a linha existe
		// pelo mesmo motivo que a da danca existe: o `Drop` chama isto ANTES do `Persistir`, e deixar a
		// limpeza pro proximo tique poria uma gravacao no meio de um estado que o save nao descreve.
		if (_emCenaDeFusao.TryGetValue(id, out CenaDeFusao? c) && !c.Fundiu)
			AbortarACenaDeFusao(c, "um dos dois saiu do jogo.");

		if (FusaoDe(id) is { } f) Separar(f, "um dos dois saiu do jogo");

		// O CONVITE QUE ELE TINHA NA MESA, E O QUE ELE TINHA FEITO. Sem as duas linhas, um id reusado
		// herdaria o pendente do anterior -- e "aceite" viraria fundir com quem nunca convidou voce.
		_pedidosDeFusao.Remove(id);
		foreach (int alvo in _pedidosDeFusao.Where(kv => kv.Value.DeQuem == id).Select(kv => kv.Key).ToList())
			_pedidosDeFusao.Remove(alvo);

		// ============================ E A RECARGA **FICA** ============================
		// Aqui havia `_recargaDeFusao.Remove(id)`, e a linha estava certa pro desenho de entao: a chave
		// era o id de SESSAO, e id se reusa -- guardar a espera faria o proximo a entrar naquele numero
		// nascer proibido de fundir. So que o preco disso era o defeito: deslogar zerava a espera.
		//
		// Com o carimbo morando no `Fighter` (ver o bloco do topo deste arquivo), a chave passou a ser
		// o CORPO. Ninguem herda espera de ninguem, e o `Separar` logo acima ja escreveu a 1 h nas duas
		// fichas ANTES do `Persistir` -- que e a ordem que este metodo existe pra garantir.
		// ==========================================================================
	}
}
