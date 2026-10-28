using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// **O QUE O SERVIDOR CONFERE EM TODO VERBO DE JOGADOR** -- e as pecas que os verbos dividem pra conferir.
///
/// ============================ O PEDIDO DO DONO (2026-10-10) ============================
/// *"melhore essas validaçoes do server"* -- as que o inventario de verbs daquele dia achou fracas. A regra da
/// casa e uma frase so: o servidor decide e valida tudo, e o cliente so PEDE. Um cliente mexido manda qualquer
/// verbo, com qualquer argumento, a qualquer hora; esconder o botao nunca foi permissao.
/// ======================================================================================
///
/// ============================ AS QUATRO FALHAS QUE SE REPETIAM ============================
/// Todo verbo novo responde a estas quatro perguntas ANTES de tocar em qualquer coisa:
///
///   1. **O ALVO ESTA AO ALCANCE?** Id ou nome vindo do cliente nao e alvo: e um pedido. No DM o alvo saia de
///      `in view(N)` / `oview(N)` / `get_dist`, e o motor nem oferecia quem estava longe. Aqui a porta e o
///      <see cref="AlvoAoAlcance"/> (mesma zona + os tiles do verbo). O `PorNome` e o resolvedor de ADMIN -- ele
///      acha qualquer um, em qualquer planeta, e por isso nao serve a verbo de jogador.
///   2. **O CORPO PODE?** Morto, nocauteado, meditando, treinando, preso: o que o `if(KO || dead || med ||
///      train) return` do verbo do DM dizia, com a frase que explica.
///   3. **A RECUSA VEM ANTES DO EFEITO.** Nada se grava, se cobra ou se anuncia antes da ultima recusa: um
///      pedido recusado nao deixa rastro.
///   4. **NUMERO DO CLIENTE TEM TRAVA.** Quantidade negativa, indice fora da lista, texto sem limite, NaN e
///      infinito (`double.TryParse` aceita os dois) sao recusados na porta.
///
/// A bancada `--validacoesteste` (`GameServer.ValidacoesTeste.cs`) aperta cada verbo consertado como um cliente
/// mexido apertaria, cobra a recusa SEM efeito colateral, e guarda um defeito injetado por conserto. Ela tambem
/// vigia a fonte: um `PorNome(` novo fora dos arquivos de admin reprova a bancada, e uma leitura nova do mapa de
/// colisao de ARQUIVO (`.Mapa`) fora do funil de "onde ha parede" tambem -- ver <see cref="MapaDoSalto"/>.
/// =========================================================================================
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// 1. O ALVO AO ALCANCE
	// =====================================================================

	/// <summary>
	/// **O ALVO DE UM VERBO DE JOGADOR**: o corpo que o cliente apontou (o id da mira, ou um nome), e SO se ele
	/// estiver na mesma zona e a ate <paramref name="tiles"/> de quem pediu.
	///
	/// A DISTANCIA E A DO BYOND (`get_dist`, tabuleiro -- <see cref="Fusao.DistanciaEmTilesDoDm"/>): `view(1)` e o
	/// quadrado de nove celulas, e o vizinho de canto conta. Linha de visao NAO entra: a colisao deste port e um
	/// bit por celula e nao distingue o que e opaco (parede) do que so e denso (uma mesa), e o `view()` do DM so
	/// e barrado pelo opaco. Fica declarado; o `VerboAnotar` (`set src in view(10)`) ja vivia com a mesma falta.
	/// </summary>
	/// <param name="tiles">O `view(N)` do verbo no DM.</param>
	/// <param name="longe">
	/// Quem o cliente apontou e NAO esta ao alcance (outra zona, ou longe demais) -- pra recusa dizer quem.
	/// Nulo quando o pedido nao aponta ninguem (e ai a frase e a de quem chamou: "marque alguem antes").
	/// </param>
	/// <param name="euMesmoConto">
	/// O verbo aceita quem pediu como alvo (o `view()` do DM inclui o proprio `usr`: o Guardiao faz a chave da
	/// Sala pra si mesmo). Falso = apontar pra si e "nao ha alvo".
	/// </param>
	private ServerPlayer? AlvoAoAlcance(ServerPlayer pl, string idOuNome, int tiles, out ServerPlayer? longe,
										bool euMesmoConto = false)
	{
		longe = null;
		ServerPlayer? o = PorNome(idOuNome);
		if (o == null) return null;
		if (o == pl) return euMesmoConto ? pl : null;
		if (AlvoSemAlcanceDeTeste) return o;

		if (o.Zone.Hash == pl.Zone.Hash
			&& Fusao.DistanciaEmTilesDoDm(pl.Pos, o.Pos, ZoneCollision.TileSize) <= tiles)
			return o;

		longe = o;
		return null;
	}

	/// <summary>A recusa de quem apontou alguem fora do alcance. Uma frase pra todos os verbos, com o nome que o mundo ve.</summary>
	private static string FraseDeLonge(ServerPlayer alvo) => $"{NomeVisivel(alvo)} está longe demais.";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o alvo de um verbo volta a ser qualquer corpo do MUNDO, pelo
	/// id ou pelo nome -- nomear Anciao, escolher herdeiro, dar a chave da Sala e rejuvenescer alguem que esta em
	/// outro planeta. Falso em jogo, sempre.
	/// </summary>
	internal bool AlvoSemAlcanceDeTeste;

	// =====================================================================
	// 2. OS DEFEITOS INJETADOS DOS OUTROS CONSERTOS -- cada um e lido pela linha do conserto dele
	// =====================================================================

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): `splitform_alvo` grava a ordem nas copias ANTES de recusar
	/// por falta de alvo -- a copia fica esperando, e ataca quem o dono marcar depois. Falso em jogo, sempre.
	/// </summary>
	internal bool OrdemAntesDaRecusaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): `regenerar` so pergunta se o corpo esta morto -- nocauteado,
	/// meditando ou treinando, o membro volta. Falso em jogo, sempre.
	/// </summary>
	internal bool RegeneraCaidoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o Super Shenron atende de qualquer lugar -- de dentro de um
	/// planeta, a galaxias das Super Esferas. Falso em jogo, sempre.
	/// </summary>
	internal bool SuperShenronDeQualquerLugarDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): `trn_recusar` vale em qualquer fase -- com a chave montada, o
	/// mundo ouve "desistiu da inscricao" de quem continua nela. Falso em jogo, sempre.
	/// </summary>
	internal bool RecusaEmQualquerFaseDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o mestre caido (nocauteado ou morto) provoca o aluno e gasta
	/// a recarga do ensino. Falso em jogo, sempre.
	/// </summary>
	internal bool MestreCaidoEnsinaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): a habilidade `decolar` volta a ser um salto de graca pro
	/// espaco, pra qualquer um. Falso em jogo, sempre.
	/// </summary>
	internal bool DecolarSemPortaoDeTeste;

	// =====================================================================
	// 3. A DECOLAGEM SEM VOAR E SO DAS BANCADAS
	// =====================================================================

	/// <summary>
	/// **AS BANCADAS QUE CHEGAM AO ESPACO SEM VOAR.** A habilidade `decolar` (`GameServer.Raciais.cs`) saiu do
	/// menu em 2026-09-03 -- a porta do jogador e voar pra cima ate romper a atmosfera
	/// (`TentarRomperAAtmosfera`, com os tres portoes do `Space_Flight`, `Mindo.dm:162`) -- e o id ficou porque
	/// estes robos precisam chegar la sem voar. Ele so atende quando o PROCESSO DO SERVIDOR subiu com uma destas
	/// flags (o molde do `--embarqueteste`, `GameServer.Verbos.cs`): num servidor de verdade nenhuma existe, e um
	/// cliente mexido que mande `decolar` ouve onde fica a porta.
	///
	///   `--diagnav` (RoboDeNav), `--diaguniverso` (RoboDoUniverso), `--diagcachezona` (RoboDoCacheDeZona),
	///   `--diaggodki` (RoboDoGodKi: o `admin_mundo_divino` so atende do espaco), `--trailer` (RoboDoTrailer),
	///   `--espaco` (o `--socar --espaco N` do RoboDeSoco, hospedando) e `--decolagemlivre` (pra um servidor de
	///   bancada separado dos clientes).
	///
	/// Robo novo que precise da mesma porta entra AQUI, de proposito.
	/// </summary>
	internal static readonly string[] BancadasQueDecolamSemVoar =
		["--diagnav", "--diaguniverso", "--diagcachezona", "--diaggodki", "--trailer", "--espaco", "--decolagemlivre"];

	/// <summary>Este processo subiu com uma das flags de <see cref="BancadasQueDecolamSemVoar"/>.</summary>
	private static readonly bool DecolagemDeBancada =
		Array.Exists(OS.GetCmdlineArgs(), a => Array.IndexOf(BancadasQueDecolamSemVoar, a) >= 0);

	// =====================================================================
	// 4. O QUE A VARREDURA DOS VERBOS ACHOU (2026-10-10) -- as pecas que mais de um conserto divide
	// =====================================================================

	/// <summary>
	/// **UM NUMERO VINDO DO CLIENTE SO VALE SE FOR FINITO.** `double.TryParse` e `float.TryParse` ACEITAM "NaN"
	/// e "Infinity", e um NaN nao e pego por `Math.Clamp`, `Math.Min`/`Math.Max` nem por comparacao (toda
	/// comparacao com NaN e falsa): ele atravessa a trava e vai parar na ficha ou no disco. `Power_Control:NaN`
	/// escrevia `powerMod = NaN` -- e dali o poder expresso, o dano e os membros de quem encostasse; um
	/// `posicionar item/NaN/0` punha no mundo uma obra que o `mundo.json` nao sabe gravar.
	/// </summary>
	private bool NumeroFinito(double n) => NumeroSemTravaDeTeste || double.IsFinite(n);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): NaN e infinito voltam a passar como numero do cliente.
	/// Falso em jogo, sempre.
	/// </summary>
	internal bool NumeroSemTravaDeTeste;

	/// <summary>
	/// **UM CORPO DERIVADO NAO E OUTRA PESSOA**: o reflexo da mente e a copia do Split Form sao o proprio dono de
	/// novo, com o poder DELE. No DM os dois sao `/mob/npc` -- `HasSoul = 0`, `isNPC = 1`, sem `client`
	/// (`NPClist.dm:23-25`) --, e por isso nenhuma absorcao paga por eles como paga por um jogador
	/// (`Absorption.dm:344` `if(ismajor && M.client)`, `:383` `if(!M.isNPC)`, `demon.dm:41` `M.HasSoul`). Aqui
	/// eles nascem com `Papel` nulo, e as absorcoes os tomavam por gente: derrubar o proprio reflexo (que se
	/// levanta sozinho, sempre com o poder do dono) e absorve-lo era uma fabrica de poder sem vitima.
	/// </summary>
	private bool CorpoDerivado(ServerPlayer o) =>
		!AbsorveCorpoDerivadoDeTeste && (o.DonoDoClone != 0 || _splitformsG12.ContainsKey(o.Id));

	/// <summary>
	/// DENTRO DE UMA MENTE NADA SE ABSORVE -- a regra que a absorcao do Majin ja tinha
	/// (`GameServer.AbsorcaoMajin.cs`: "aqui dentro nada e real"), agora das outras quatro: la a derrota nem
	/// conta (`AoPerderALuta` sai na primeira linha), e absorver um visitante o mataria de verdade.
	/// </summary>
	private bool RecusarAbsorcaoNaMente(ServerPlayer pl)
	{
		if (AbsorveCorpoDerivadoDeTeste || !NaMente(pl)) return false;
		Avisar(pl, "aqui dentro nada e real: nao ha quem absorver.");
		return true;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o reflexo da mente e a copia do Split Form voltam a valer
	/// como gente pras absorcoes, dentro e fora da mente. Falso em jogo, sempre.
	/// </summary>
	internal bool AbsorveCorpoDerivadoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): refazer as esferas com o dragao de pe zera a conta de
	/// pedidos e deixa a invocacao de pe -- pedidos sem fim. Falso em jogo, sempre.
	/// </summary>
	internal bool RefazerComODragaoDePeDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o corpo caido volta a agir onde o verbo do DM abre com
	/// `if(KO || dead) return` -- reivindicar cargo nocauteado (ou morto, um cargo do mundo dos vivos), ensinar
	/// uma disciplina divina, infundir o Hakai. Falso em jogo, sempre.
	/// </summary>
	internal bool CaidoAgeDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o canal dos cargos repassa o texto como veio (com quebra de
	/// linha, que desenha linhas falsas de sistema no chat alheio) e sem intervalo entre falas. Falso em jogo, sempre.
	/// </summary>
	internal bool CanalDosCargosCruDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): a fala aceita qualquer byte de canal vindo do cliente -- com
	/// 200 (`Fala.Sistema`) o texto chega sem autor, como recado do servidor. Falso em jogo, sempre.
	/// </summary>
	internal bool CanalDeFalaLivreDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): esquecer uma skill que o CARGO deu devolve marcos, e o
	/// tique dos cargos a entrega de novo -- a impressora de marcos. Falso em jogo, sempre.
	/// </summary>
	internal bool ReembolsaODoCargoDeTeste;

	// =====================================================================
	// 5. A SEGUNDA RODADA (2026-10-11) -- *"resolva todos esses problemas, e o guardiao tem q estar no templo pra
	//    soltar"*: os de risco alto da lista. O treino e a meditacao moram em `GameServer.Atividade.cs`.
	// =====================================================================

	/// <summary>
	/// **ESTE CORPO PODE SER TIRADO DE ONDE ESTA?** -- por um teleporte dele (o salto de planeta, a Transmissao, o
	/// ponto cronico, o carimbo do juiz, o duelo pelo titulo), ou de CARONA no de outro. Vazio = pode; senao, a
	/// frase, dita a ele.
	///
	/// UMA PERGUNTA, UM LUGAR. Cada teleporte tinha a sua lista, e cada lista esquecia um lugar: o ponto cronico e o
	/// carimbo nao perguntavam nada, a Transmissao e o salto nao sabiam da prisao da Sala nem do bolso da fusao, a
	/// carona levava qualquer corpo vivo ao lado, e o duelo buscava o desafiante onde ele estivesse e devolvia os
	/// dois ao berco. O DM nao tem nenhum destes lugares fechados menos o selo (`Planet != "Sealed"`,
	/// `kai.dm:114`): a prisao da Sala e regra do dono (`GameServer.SalaDoTempo.cs`: *"sair da sala do tempo dps de
	/// preso so morrendo pra sair"*), e os outros sao bolsos deste port de onde sair por fora deixa estado orfao.
	///
	///   * PRESO NA SALA DO TEMPO -- quem solta e o Guardiao (do Templo) ou um admin; a morte e a saida cara;
	///   * CARREGANDO UM PRESO DA SALA (o passageiro da fusao, o absorvido do Majin) -- `PresoQueEsteCorpoCarrega`;
	///   * DENTRO DE UM MAJIN -- a saida e vencer a imagem (`GameServer.AbsorcaoMajin.cs`);
	///   * SELADO, ou PASSAGEIRO DE UMA FUSAO -- os dois moram num bolso `Selado` (`GameServer.Fusao.cs`);
	///   * NUMA MENTE -- sai-se acordando;
	///   * NA ESPERA DO TORNEIO -- imovel e intocavel ate a vez (`apply_hold`, `Tournament.dm:326-336`).
	///
	/// O ADMIN CONTINUA MOVENDO QUEM QUISER: os verbs dele (`admin_ir`, `admin_trazer`, `admin_sala_soltar`) nao
	/// passam por aqui, e a valvula da prisao e justamente ele.
	/// </summary>
	/// <summary>
	/// (2026-10-27, o pedido 132) ESTE CORPO E DE SAGA? O chefe que uma saga pos no mundo (`PapelDeNpc.DaSaga` -- e tambem o vilao do Inferno e o
	/// campeao do wipe), ou um soldado da tropa dela. O dono: *"n deixe nenhum npc de saga pode sair do planeta q a saga acontece pq isso causa
	/// bugs"* -- o Freeza que seguia um jogador ate a orbita fazia a tropa fugir e as esferas cairem, e a tropa renascia mais forte na volta.
	/// Eles nascem no planeta da saga (`NascerNpc` na zona dela) e nunca viajam por conta propria: o corpo de saga simplesmente nao troca de
	/// zona -- nem de carona, nem por teleporte, nem selado (`PorQueNaoSaiDaqui`, `MoveToZone`, `Selar`).
	/// </summary>
	private bool EhDeSaga(ServerPlayer pl) =>
		!SagaSaiDoPlanetaDeTeste && pl.Peer == null && pl.Papel is { } papel && (papel.DaSaga || papel.Tipo == Jandirus.Core.Npc.TipoDeNpc.Soldado);

	/// <summary>DEFEITO INJETADO (bancada `--espacovooteste`, familia S): o corpo de saga volta a sair do planeta. Lido pelo <see cref="EhDeSaga"/>. Falso em jogo, sempre.</summary>
	internal static bool SagaSaiDoPlanetaDeTeste;

	private string PorQueNaoSaiDaqui(ServerPlayer pl)
	{
		if (TeleporteFuraPrisaoDeTeste) return "";
		// (2026-10-24) O BILLS E O WHIS NAO SAEM DO PLANETA (o dono) -- nem de carona, nem por teleporte. A rede e o `MoveToZone`.
		if (!Jandirus.Core.Npc.BillsEWhis.DivinoSaiDoPlanetaDeTeste && DivinoDe(pl) is { } divino)
			return divino.Quem == Jandirus.Core.Npc.QuemFala.Whis ? "o Whis não sai deste planeta." : "o Bills não sai deste planeta.";
		// (2026-10-27, o pedido 132) NEM O NPC DE SAGA -- o chefe, a tropa, o vilao do Inferno. A rede e o `MoveToZone`. Ver `EhDeSaga`.
		if (EhDeSaga(pl)) return $"{pl.Name} não sai do planeta da saga.";
		if (pl.SalaPreso && NaSala(pl))
			return "você está preso na Sala do Tempo: só o Guardião da Terra (ou um administrador) solta.";
		if (PresoQueEsteCorpoCarrega(pl) is { } carga)
			return $"{NomeVisivel(carga)} está preso na Sala do Tempo e está dentro de você: com ele, você também não sai -- {SoQuemSoltaDaSala}";
		if (DentroDeUmMajin(pl)) return "daqui de dentro não: a saída é vencer a imagem de quem absorveu você.";
		if (pl.Selo.Preso) return "daqui de dentro não: você está selado.";
		if (EhOSelo(pl.Zone)) return "daqui de dentro não: você é o passageiro de uma fusão.";
		if (NaMente(pl)) return "primeiro saia do transe.";
		if (PresoNoTorneio(pl.Id)) return "você está na área de espera do torneio.";
		// (2026-10-19) CARREGANDO ESFERA DO DRAGAO -- ela nao sai do planeta (dono), e esta pergunta nao sabe o destino: recusar e o que
		// nunca a perde. Ver `GameServer.EsferasNaMochila.cs`.
		if (PorQueAsEsferasPrendem(pl) is { Length: > 0 } esferas) return esferas;
		return "";
	}

	/// <summary>
	/// **NESTA ZONA NAO SE CHEGA POR TELEPORTE**: a Sala do Tempo (a porta cobra chave, lotacao e recarga --
	/// `EntrarNaSala`), a mente de alguem, o interior de um Majin e o bolso do selo e da fusao. A Transmissao ja
	/// tirava da lista quem estava na Sala ou selado (`ListaDeAssinaturasG11`), mas a lista e montada no PEDIDO e a
	/// chegada e segundos depois; e o ponto cronico cravado la dentro era uma porta dos fundos permanente.
	/// </summary>
	private bool ZonaFechadaAoTeleporte(ZoneKey z) =>
		!TeleporteFuraPrisaoDeTeste
		&& (string.Equals(z.Name, ZonaDaSala, StringComparison.OrdinalIgnoreCase)
			|| DimensaoMental.EhAMente(z) || InteriorDoMajin.EhOInterior(z) || EhOSelo(z));

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): os teleportes voltam a nao perguntar de onde nao se sai nem
	/// onde nao se chega -- o preso da Sala do Tempo volta no tempo pra fora dela, vai de carona no Kai Kai de
	/// outro, e o duelo pelo titulo o busca la dentro. Falso em jogo, sempre.
	/// </summary>
	internal bool TeleporteFuraPrisaoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o `PodeAtirar` volta a nao perguntar pelo atordoamento -- o
	/// corpo que acabou de levar o golpe atira no meio dele. Falso em jogo, sempre.
	/// </summary>
	internal bool AtiraAtordoadoDeTeste;

	/// <summary>
	/// **O MAPA QUE BARRA UM SALTO** -- todo deslocamento que o servidor escreve de uma vez, sem passar pelo passo
	/// validado do jogador: a piscada do Zanzoken, o avanco das tecnicas de aproximacao, a investida e o passo curto
	/// do soco (`Aproximar`, `GameServer.Combat.cs`), o puxao que junta os dois antes de uma fusao (`AndarNoPuxao`),
	/// os saltos do embate de Zanzoken e o lugar onde o vencedor dele nasce (`Cruzar`, `Terminar`). O mapa e o da
	/// zona em que o corpo ESTA, seja ela de arquivo, sorteada, a planta de uma nave ou de uma mente
	/// (`MapaDaZonaOuCatalogo`, o funil unico de "onde ha parede" -- `GameServer.Volta.cs`). Todos perguntavam so
	/// ao catalogo de mapas de arquivo, que nao conhece planeta gerado: la `mapa` vinha nulo, a conferencia de
	/// parede era pulada, e o corpo atravessava montanha, rocha, arvore e a parede da base de quem construiu ali (o
	/// passo curto do soco, que so anda COM mapa, nem acontecia).
	///
	/// QUEM LE O MAPA DE ARQUIVO DIRETO E VIGIADO: a `--validacoesteste` conta, no fonte do servidor, quem escreve
	/// `.Mapa` fora da lista de quem pode (`QuemPodeLerOMapaDeArquivo`, `GameServer.ValidacoesTeste.cs` -- o
	/// carregador, o proprio funil, as portas e passagens de zona de arquivo). Parede nova se pergunta por aqui.
	/// </summary>
	private ZoneCollision? MapaDoSalto(ZoneKey zona) =>
		ParedeSoDeCatalogoDeTeste ? _catalogo?.Get(zona)?.Mapa : MapaDaZonaOuCatalogo(zona);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o Zanzoken, o avanco das tecnicas, a investida do soco, o
	/// puxao da fusao e o embate de Zanzoken voltam a conferir parede so em mapa de arquivo -- em planeta gerado
	/// atravessam tudo. Falso em jogo, sempre.
	/// </summary>
	internal bool ParedeSoDeCatalogoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o Bio-Android Lab volta a atender qualquer um -- quem espeta
	/// a primeira agulha no laboratorio alheio vira o criador, e qualquer passante fecha o tanque de outra pessoa.
	/// Falso em jogo, sempre.
	/// </summary>
	internal bool LabSemDonoDeTeste;

	/// <summary>
	/// **A TECLA DE UMA FORMA NAO PASSA POR CIMA DA POSSE.** O `forma &lt;id&gt;` viaja no `C2S.Verbo`, que nao e
	/// comando de corpo: o despacho recusa o `C2S.Transformar` de quem perdeu as redeas (`ComandoDeCorpo`,
	/// `GameServer.Clone.cs` -- o `ctrlParalysis` do DM, `Oozaru.dm:163`) e este passava por fora. O possuido pela
	/// furia lendaria revertia com `forma base`; a fera descontrolada trocava de forma. Devolve se recusou.
	/// </summary>
	private bool RecusarFormaSemAsRedeas(ServerPlayer pl)
	{
		if (FormaSemRedeasDeTeste || !SemAsRedeas(pl)) return false;
		Avisar(pl, "você não manda neste corpo agora: a forma não muda pela sua mão.");
		return true;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o verbo `forma` volta a atender quem perdeu as redeas do
	/// corpo. Falso em jogo, sempre.
	/// </summary>
	internal bool FormaSemRedeasDeTeste;

	/// <summary>
	/// **ESTA SKILL NAO SE VENDE AGORA, MESMO QUE O LIVRO A TENHA COMO COMPRADA**: ela e do kit do cargo que o corpo
	/// carrega. O livro sabe o que foi comprado com marcos (`SkillBook.FoiComprada`) e so isso devolve marco -- mas
	/// num save de antes dessa lista tudo o que ninguem ensinou conta como comprado, inclusive o que o cargo
	/// entregou, e a skill do kit, esquecida, voltaria pelo tique dos cargos. Enquanto o cargo for dele ela nao
	/// devolve marco, nem esquecida pelo verbo, nem levada pela arvore que encolhe.
	///
	/// (A emprestada pela fusao saiu daqui: ela entra por `Livro.Dar`, nunca esta entre as compradas, e corpo
	/// fundido nao se grava -- `Persistir`. O verbo continua recusando esquece-la, por `EmprestadaPelaFusao`.)
	/// </summary>
	private bool SkillQueNinguemComprou(ServerPlayer pl, string path) => EstaNoKit(KitQueNaoSeVende(pl), path);

	/// <summary>O kit do cargo que este corpo carrega agora -- vazio pra quem nao tem cargo.</summary>
	private string[] KitQueNaoSeVende(ServerPlayer pl) =>
		ReembolsaODoCargoDeTeste ? [] : Jandirus.Core.Ranks.DadivaDeCargo.De(CargoDe(pl.Conta));

	/// <summary>(laco a mao, e nao `Contains` com comparador: este roda por skill, dentro da assinatura de todo tique)</summary>
	private static bool EstaNoKit(string[] kit, string path)
	{
		foreach (string k in kit)
			if (string.Equals(k, path, StringComparison.OrdinalIgnoreCase)) return true;
		return false;
	}

	/// <summary>Esta skill veio do passageiro da fusao que este corpo conduz agora?</summary>
	private bool EmprestadaPelaFusao(ServerPlayer pl, string path) =>
		!EsqueceOEmprestadoDeTeste
		&& _fundidos.TryGetValue(pl.Id, out FusaoAtiva? f) && f.Dono == pl
		&& f.SkillsEmprestadas.Contains(path, StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o `skill_esquecer` volta a aceitar a skill que a fusao
	/// emprestou -- o dono a tira do livro antes da separacao. (Marco ela nao devolve nem assim: quem fecha essa
	/// conta, desde a lista de compradas, e o livro.) Falso em jogo, sempre.
	/// </summary>
	internal bool EsqueceOEmprestadoDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o Guardiao volta a destrancar a Sala do Tempo de qualquer
	/// lugar do universo. Falso em jogo, sempre.
	/// </summary>
	internal bool SoltaDeLongeDeTeste;

	// =====================================================================
	// 6. A TERCEIRA RODADA (2026-10-11) -- *"sim pode fechar os dois, e a investida do soco etc tb pode resolver"*:
	//    o marco de volta so pelo que foi comprado, o preso que saia da Sala do Tempo dentro de outro corpo, e as
	//    sobras da parede so de catalogo (`MapaDoSalto`, acima).
	// =====================================================================

	/// <summary>
	/// **ESTA SKILL DEVOLVE MARCO SE FOR ESQUECIDA?** A pergunta do `skill_esquecer`, feita num lugar so pra que o
	/// verbo e a tela (a cauda do `S2C.Skills`, <see cref="SkillsSemReembolso"/>) nunca respondam diferente: foi
	/// COMPRADA com marcos (`SkillBook.FoiComprada`), ninguem a ensinou, e nao e do kit do cargo de agora. E a
	/// mesma conta do `SkillBook.EsquecerEReembolsar` com o predicado que o verbo lhe passa.
	/// </summary>
	private bool DevolveMarcoAoEsquecer(ServerPlayer pl, string path) =>
		DevolveMarco(pl.Livro, KitQueNaoSeVende(pl), path);

	private static bool DevolveMarco(Jandirus.Core.Skills.SkillBook livro, string[] kit, string path) =>
		livro.FoiComprada(path) && !livro.FoiEnsinada(path) && !EstaNoKit(kit, path);

	/// <summary>
	/// AS APRENDIDAS QUE NAO DEVOLVEM MARCO, em ordem -- o que o pacote de skills leva pro botao de esquecer nao
	/// prometer o que o servidor nao paga: a herdada pela absorcao Namekuseijin, a que um admin deu, a que um
	/// degrau concedeu, a ensinada, a do cargo, a emprestada pela fusao.
	/// </summary>
	private List<string> SkillsSemReembolso(ServerPlayer pl)
	{
		string[] kit = KitQueNaoSeVende(pl);
		var sem = new List<string>();
		foreach (string p in pl.Livro.Aprendidas)
			if (!DevolveMarco(pl.Livro, kit, p)) sem.Add(p);
		sem.Sort(StringComparer.Ordinal);
		return sem;
	}

	/// <summary>
	/// A MESMA LISTA, RESUMIDA PRA ASSINATURA DO PACOTE: quantas sao, e uma soma delas que nao depende da ordem. O
	/// `MandarSkills` e chamado todo tique pra cada jogador (`TickFichas`), e montar e juntar a lista so pra
	/// comparar seria lixo por tique -- a mesma conta que tirou os corpos sem `Peer` daquele laco.
	/// </summary>
	private (int Quantas, int Soma) ResumoDasSemReembolso(ServerPlayer pl)
	{
		string[] kit = KitQueNaoSeVende(pl);
		int quantas = 0, soma = 0;
		foreach (string p in pl.Livro.Aprendidas)
			if (!DevolveMarco(pl.Livro, kit, p))
			{
				quantas++;
				soma = unchecked(soma + StringComparer.OrdinalIgnoreCase.GetHashCode(p));
			}
		return (quantas, soma);
	}

	/// <summary>Este corpo esta PRESO na Sala do Tempo agora. O bit so prende la dentro (`APrisaoRecusaASaida`).</summary>
	private static bool PresoDaSala(ServerPlayer pl) => pl.SalaPreso && NaSala(pl);

	/// <summary>O fim de toda frase da prisao da Sala -- o mesmo do <see cref="PorQueNaoSaiDaqui"/>.</summary>
	private const string SoQuemSoltaDaSala = "só o Guardião da Terra (ou um administrador) solta.";

	/// <summary>
	/// **O PRESO DA SALA DO TEMPO NAO SAI DELA DENTRO DE OUTRO CORPO.** A tranca e do corpo que esta na Sala (a
	/// passagem, `APrisaoRecusaASaida`; os teleportes, <see cref="PorQueNaoSaiDaqui"/>), e o preso deixava de ser um
	/// corpo na Sala por dois caminhos: virando o PASSAGEIRO de uma fusao (vai pro bolso `Selado` e volta ao lado do
	/// dono, onde o dono estiver -- `Separar`) ou sendo ABSORVIDO por um Majin (vai pro interior dele e e cuspido
	/// ao lado dele -- `OndeOMajinCospe`). O outro saia pela porta, e ele saia junto.
	///
	/// O DESENHO (dono, 2026-10-11: *"sim pode fechar os dois"*; o DM nao tem prisao na Sala, e regra dele --
	/// entao a mais simples que fecha a fuga, e so pra quem esta preso):
	///   1. NAO ENTRA: enquanto preso, ele nao FUNDE pela Danca nem pela Potara -- nem convidando: o
	///      `PassarOControle` faz do dono um passageiro a qualquer hora -- e nenhum Majin o ABSORVE. A recusa sai com
	///      a frase da prisao (esta funcao e a <see cref="PorQueNaoLevaOPreso"/>);
	///   2. NAO SAI: quem ja carrega um preso (o bit fechou no meio da danca, da cinematica ou do nocaute) fica na
	///      Sala com ele -- <see cref="PresoQueEsteCorpoCarrega"/>, lido pela porta e pelos teleportes.
	///
	/// O QUE NAO MUDA: a fusao e a absorcao de quem nao esta preso; a fusao NAMEKUSEIJIN (permanente: o absorvido
	/// deixa de existir, como na morte, que ja e a saida cara da prisao -- e quem absorve continua preso no proprio
	/// corpo, entao ninguem sai por ela); o Majin PRESO que absorve alguem (ele continua na Sala, e quem ele cospe
	/// nao estava preso).
	/// </summary>
	/// <returns>A frase pra `ouvinte` quando ele ou o `outro` esta preso; vazio quando nenhum dos dois esta.</returns>
	private string PorQueOPresoNaoFunde(ServerPlayer ouvinte, ServerPlayer outro)
	{
		if (PresoEntraEmQuemSaiDeTeste) return "";
		if (PresoDaSala(ouvinte)) return $"você está preso na Sala do Tempo, e daqui ninguém sai dentro de outro corpo: {SoQuemSoltaDaSala}";
		return PorQueNaoLevaOPreso(outro);
	}

	/// <summary>A frase pra quem tenta por `carga` dentro de si (fundindo ou absorvendo) -- vazio se ela nao esta presa.</summary>
	private string PorQueNaoLevaOPreso(ServerPlayer carga) =>
		PresoEntraEmQuemSaiDeTeste || !PresoDaSala(carga)
			? ""
			: $"{NomeVisivel(carga)} está preso na Sala do Tempo, e de lá ninguém sai dentro de outro corpo: {SoQuemSoltaDaSala}";

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): o preso da Sala do Tempo volta a fundir e a ser absorvido --
	/// e sai da Sala como passageiro de quem nao esta preso. Falso em jogo, sempre.
	/// </summary>
	internal bool PresoEntraEmQuemSaiDeTeste;

	/// <summary>
	/// **O PRESO QUE ESTE CORPO LEVARIA PRA FORA DA SALA DO TEMPO**, se saisse agora: o passageiro da fusao que ele
	/// conduz, ou alguem selado dentro dele (Majin). Nulo quando nao leva preso nenhum, e quando ele mesmo nao esta
	/// na Sala: o bit so prende la dentro.
	///
	/// E A REDE POR BAIXO DA PORTA DE CIMA (<see cref="PorQueOPresoNaoFunde"/>), e ela pega o que a porta nao ve:
	///   * a tranca fecha por RELOGIO (`JanelaOuTranca`), e pode fechar com o convite ja aceito -- no meio da
	///     danca, do puxao ou da cinematica: a fusao nasce com um preso de passageiro;
	///   * a tranca fecha em quem JA CONDUZ uma fusao: ele fica preso, passa o controle (`PassarOControle`), e
	///     quem era o passageiro vira o dono, solto, com o preso dentro.
	/// A absorcao do Majin nao tem essa folga (o nocaute, a pergunta e o selo sao a mesma chamada, e a tranca nao
	/// fecha em quem esta la dentro: o tique so olha quem esta na Sala). A linha dela existe pra quem um dia chamar
	/// o `SelarNoMajin` por outra porta.
	/// </summary>
	private ServerPlayer? PresoQueEsteCorpoCarrega(ServerPlayer pl)
	{
		if (CargaPresaSaiDeTeste || !NaSala(pl)) return null;
		if (_fundidos.TryGetValue(pl.Id, out FusaoAtiva? f) && f.Dono == pl && f.Passageiro.SalaPreso) return f.Passageiro;
		foreach (AbsorvidoPeloMajin rec in AbsorvidosPor(pl.Id))
			if (rec.Quem != 0 && _players.TryGetValue(rec.Quem, out ServerPlayer? dentro) && dentro.SalaPreso) return dentro;
		return null;
	}

	/// <summary>
	/// DEFEITO INJETADO (bancada `--validacoesteste`): quem carrega um preso (o passageiro da fusao, o absorvido do
	/// Majin) volta a sair da Sala do Tempo com ele, pela porta e por teleporte. Falso em jogo, sempre.
	/// </summary>
	internal bool CargaPresaSaiDeTeste;
}
