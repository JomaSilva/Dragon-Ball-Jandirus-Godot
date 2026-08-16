using Jandirus.Core.Appearance;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS JANELAS DA BANCADA QUE FOTOGRAFA A FUSAO (`--diagfotofusao`) ============================
/// A `--fusaoduplateste` mede a fusao inteira em NUMERO, com treze defeitos injetados, e fecha 157 de 157.
/// **E ela ficaria verde com a fusao desenhada careca e de calcao** -- entre o `LookDeFusao` do servidor
/// e o pixel ha o `PeerLook`, o `_fusaoDaZona` do `World`, a pilha de camadas do `CharacterVisual`, o
/// `CabelosDeForma` e o shader. Este projeto ja catalogou esse cego cinco vezes ("a bancada mede
/// INTENCAO"), e as fotos que o dono pediu sao exatamente a metade que so o olho fecha:
///
///     fusao-A-cena.png        a UNICA `FusionLight` no meio dos dois corpos -- e a janela LIMPA
///                             entre dois estouros dela, que e onde da pra VER os dois
///     fusao-B-branco.png      o corpo completamente BRANCO, so a silhueta, no climax
///     fusao-C-lado-a-lado.png a METAMORO e a POTARA lado a lado (roupa e cabelo DIFEREM)
///     fusao-D-ssj4.png        a fusao em SSJ4, de cabelo VERMELHO
/// ====================================================================================================================
///
/// ============================ O QUE ESTAS JANELAS FAZEM, E O QUE ELAS NAO FAZEM ============================
/// Elas forjam UM corpo ao lado do jogador e trocam o PENTEADO dos dois (pra o par Goku+Vegeta existir,
/// que e a regra nomeada do dono). Isso e cenario, e nao a coisa medida.
///
/// **Elas nao fundem ninguem.** Quem funde e o <see cref="TickDaCenaDeFusao"/>, no `if (agora >= c.Funde)`
/// -- estas janelas so chamam o <see cref="ComecarACenaDaFusao"/>, que e o MESMO funil por onde a danca
/// resolvida e a Potara aceita entram. Quem veste e o `Fundir`, quem despe e o `Separar`, e quem
/// transforma e o `AdminForcarForma` (o botao do menu P).
///
/// O CONVITE E O QUICK TIME EVENT FICAM DE FORA de proposito: eles nao tem pixel, sao medidos de ponta a
/// ponta pela `--fusaoduplateste`, e uma tela de QTE por cima da cinematica atrapalharia justamente a
/// foto que esta bancada existe pra tirar.
/// ======================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// Faixa de ids propria -- longe do `_nextId` e das outras bancadas (91.900 da `--diagcorpos`,
	/// 92.000 da `--cenafusaoteste`, 93.000 da `--fusaoduplateste`).
	/// </summary>
	private const int IdBaseDaFotoDeFusao = 94_000;

	private int _ffCorpos;
	private readonly List<int> _ffNascidos = [];

	/// <summary>
	/// O PENTEADO ORIGINAL DE QUEM ESTA JOGANDO, pra a bancada devolver o que pegou emprestado.
	///
	/// Ela troca o cabelo do jogador porque a regra nomeada do dono e *"se um tiver o cabelo do Vegeta
	/// e o outro do Goku, a fusao usa `VegitoHairPVP.png`"* -- e um personagem de bancada que nasceu
	/// careca nunca faria a regra aparecer. Nao gravar isso de volta seria trocar o cabelo do dono por
	/// causa de uma foto.
	/// </summary>
	private string _ffCabeloDoJogador = "";
	private int _ffDonoOriginal;

	/// <summary>O adianto do ceu antes da bancada -- ela move o sol e devolve. Ver <see cref="SolAPinoNaFotoDeFusao"/>.</summary>
	private double _ffCeuAntes;
	private bool _ffMexeuNoCeu;

	/// <summary>
	/// ============================ O SOL A PINO, TIQUE A TIQUE -- E ISTO E UM ACHADO DA PRIMEIRA RODADA ============================
	/// A primeira rodada desta bancada fechou **TUDO OK, cinco tomadas** -- e as cinco fotos estavam
	/// pretas. O mundo estava de noite, e no escuro os bonecos desenham: o colete metamoriano, o brinco
	/// Potara e o cabelo vermelho do SSJ4 sao todos silhuetas cinza-escuras sobre grama cinza-escura.
	/// **As checagens de campo ficaram verdes porque elas leem o `LookDeFusao`, e nao o pixel** -- que e
	/// exatamente o cego que esta bancada existe pra cobrir, encontrado nela mesma.
	///
	/// O precedente e literal, e ate a linha e a mesma: o `GameServer.BioPalco.cs:223` ja crava
	/// `AjustarCeuDaTerra(0.5)` **a cada tique** porque cravar so na largada deixou os ultimos retratos
	/// dele saindo ao anoitecer. Esta bancada dura ~45 s e passa por duas cinematicas de fusao e uma de
	/// forma; com o sol andando, a foto da METAMORO e a da POTARA sairiam com luzes diferentes -- e a
	/// tira lado a lado, que e o pedido literal do dono, compararia iluminacao em vez de roupa.
	///
	/// **E ELE E DEVOLVIDO NA LIMPEZA**: `_adiantoDoCeu` e acumulado, e este processo e um `--host` no
	/// mundo de verdade. Deixar o adianto de pe seria mexer no relogio do jogo do dono por causa de uma
	/// foto.
	///
	/// ============================ E A SEGUNDA RODADA MOSTROU QUE A REGUA IMPORTA ============================
	/// A primeira tentativa chamou o `AjustarCeuDaTerra(0.5)` -- o mesmo do `BioPalco` --, e a foto da
	/// METAMORO saiu clara e a da POTARA, oito segundos depois, saiu PRETA. O motivo: aquele metodo
	/// acerta o meio-dia **da TERRA**, e a bancada nasce onde a conta de teste nasce. Cada planeta tem
	/// rotacao e defasagem propria (`RelogioDoPlaneta`), entao meio-dia na Terra e uma hora local
	/// qualquer aqui -- e como cada chamada empurra o mundo um dia inteiro pra frente ("o PROXIMO
	/// meio-dia"), a hora daqui saltava a cada quadro.
	///
	/// Esta versao acerta a hora **DA ZONA EM QUE O JOGADOR ESTA** e faz a correcao MINIMA (pode ser pra
	/// tras), entao ela e idempotente: depois da primeira chamada, cada quadro so compensa o tempo que
	/// passou de verdade. O sol para de ser uma variavel, que e o ponto.
	/// ==================================================================================================
	/// </summary>
	/// <returns>a hora local que ficou, e se este mundo esta de noite mesmo assim.</returns>
	internal (double Hora, bool Noite) SolAPinoNaFotoDeFusao(int idDoJogador)
	{
		if (!_players.TryGetValue(idDoJogador, out ServerPlayer? pl)) return (0, true);
		if (!_ffMexeuNoCeu) { _ffCeuAntes = _adiantoDoCeu; _ffMexeuNoCeu = true; }

		RelogioDoPlaneta relogio = RelogioDaZona(pl.Zone);
		double dia = relogio.DiaLocal(TempoDoMundo);
		_adiantoDoCeu += (Ceu.MeioDia - (dia - Math.Floor(dia))) * relogio.SegundosPorDia;

		// O QUE O CEU FICOU, e nao o que foi pedido -- a mesma disciplina do `AdminMeioDia`. Num mundo
		// de noite eterna (`TemDia` falso) este ajuste nao clareia nada, e a bancada precisa PODER
		// dizer isso em vez de gravar cinco fotos pretas com o log verde.
		EstadoDoCeu fim = CeuDe(pl);
		return (fim.Hora, fim.Noite);
	}

	/// <summary>
	/// UM CORPO AO LADO DO JOGADOR, com o penteado que a foto precisa.
	///
	/// Reusa o <see cref="ForjarCorpoDeFoto"/> das outras bancadas de foto de proposito: aquele metodo
	/// ja resolve as tres coisas faceis de esquecer (`PowerLevel`, `AparenciaDeNpc` e
	/// `TrocarAparencias`), e uma segunda copia delas seria a primeira a divergir.
	/// </summary>
	internal int ForjarParaAFotoDeFusao(int idDono, Vec2 desloc, string nome, double bp, string cabelo)
	{
		int id = ForjarCorpoDeFoto(idDono, desloc, nome, bp, comEscada: false);
		if (id == 0) return 0;

		_ffNascidos.Add(id);
		_ffCorpos++;
		CabeloNaFotoDeFusao(id, cabelo);
		return id;
	}

	/// <summary>
	/// TROCA O PENTEADO DE UM CORPO e o reapresenta a zona.
	///
	/// O `TrocarAparencias` no fim NAO e zelo: a aparencia nao viaja no snapshot (uma ficha por quadro
	/// por corpo gastaria a banda repetindo o que nao muda), entao um `Visual.Cabelo` escrito sem ele e
	/// um cabelo que so o servidor conhece.
	/// </summary>
	internal void CabeloNaFotoDeFusao(int id, string cabelo)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;

		// O PENTEADO DO JOGADOR E GUARDADO NA PRIMEIRA TROCA e devolvido na limpeza -- ver o campo.
		if (pl.Peer != null && _ffCabeloDoJogador.Length == 0)
		{
			_ffCabeloDoJogador = pl.Visual.Cabelo;
			_ffDonoOriginal = id;
		}

		pl.Visual.Cabelo = cabelo;
		pl.Visual.CorCabelo = null;   // a tinta armada pro penteado anterior cairia neste
		TrocarAparencias(pl);
	}

	/// <summary>
	/// VESTE UMA PECA NO GUARDA-ROUPA DE ALGUEM -- e ela existe por causa de um ACHADO desta bancada.
	///
	/// ============================ SEM ROUPA NENHUMA, AS DUAS REGRAS SE PARECEM ============================
	/// O personagem de linha de comando (`--conta bancada_foto_fusao`) nasce **sem uma peca vestida**. E
	/// com o guarda-roupa vazio as duas regras opostas do dono desenham a MESMA coisa:
	///
	///   * Metamoro (*"apenas `Metamoran Vest.png`"*, a peca SUBSTITUI) -> 1 camada: o colete;
	///   * Potara  (*"`potara.png` + a roupa de quem convidou"*, a peca SOMA) -> 1 camada: o brinco.
	///
	/// A primeira rodada desta bancada afirmou "a roupa e SO o colete" e ficou VERDE **sem ter provado
	/// nada**: nao havia roupa nenhuma pra o colete ter substituido. Vestir uma peca antes e o que
	/// separa as duas regras -- e e o que faz a foto lado a lado significar alguma coisa.
	/// ==================================================================================================
	/// </summary>
	internal bool VestirNaFotoDeFusao(int id, string nomeDaPeca)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (_visual?.Peca(nomeDaPeca) is not { } caminho) return false;
		if (pl.Visual.Roupa.Any(r => r.Caminho == caminho)) return true;

		pl.Visual.Roupa.Add(new PecaDeRoupa(caminho));
		TrocarAparencias(pl);
		return true;
	}

	/// <summary>
	/// COMECA A CINEMATICA DA FUSAO -- pelo funil de producao, e sem fundir nada.
	///
	/// A fusao passa a existir sozinha, no `if (agora >= c.Funde)` do <see cref="TickDaCenaDeFusao"/>,
	/// quando a animacao da luz acaba (`Cinematicas.SegundosDaLuzDaFusao`). E isso que faz a foto B (o
	/// corpo branco) e a foto C (a fusao pronta) serem duas fotos e nao uma.
	/// </summary>
	internal bool ComecarACenaNaFotoDeFusao(int idDono, int idOutro, TipoDeFusao tipo)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return false;
		if (!_players.TryGetValue(idOutro, out ServerPlayer? outro)) return false;

		// A RECARGA DE 1 H E LIMPADA ENTRE AS DUAS FOTOS, e e a unica concessao desta bancada: a
		// METAMORO e a POTARA sao duas fusoes do MESMO jogador, e o dono pediu as duas LADO A LADO.
		// Sem isto a segunda foto sairia do texto "voce ainda esta se recuperando da ultima fusao".
		// A recarga em si e medida pela `--fusaoduplateste` (H8 e I.nocaute).
		dono.Ficha.fusion_cooldown_until = 0;
		outro.Ficha.fusion_cooldown_until = 0;

		ComecarACenaDaFusao(dono, outro, tipo, estragada: false);
		return _emCenaDeFusao.ContainsKey(idDono);
	}

	/// <summary>
	/// O QUE A FOTO PRECISA LER DE UM CORPO -- e **nada disto e escrito por esta bancada**.
	///
	/// A roupa sai do <see cref="ServerPlayer.LookDeFusao"/> quando ha fusao e do `Visual` quando nao
	/// ha, que e exatamente o que o <see cref="VisualVisivel"/> faz pro `PeerLook`: a legenda da foto
	/// tem que dizer o que o CLIENTE recebeu, e nao o que o servidor guardou noutro campo.
	/// </summary>
	internal (bool Existe, bool Fundido, string Nome, string Cabelo, string[] Roupa, string Forma)
		EstadoNaFotoDeFusao(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl))
			return (false, false, "", "", [], "");

		Appearance v = VisualVisivel(pl);
		return (true, EstaFundido(id), NomeVisivel(pl), v.Cabelo,
				v.Roupa.Select(r => r.Caminho).ToArray(),
				pl.Forma.Atual);
	}

	/// <summary>
	/// PLANTA UM CORPO AO LADO DE OUTRO -- **cenario, e nao a coisa medida**.
	///
	/// Ela existe por um caso so: o `Separar` devolve o passageiro **ao lado de quem dirigia**, ou seja
	/// em cima do jogador. Sem esta linha os dois corpos sairiam empilhados na foto seguinte e o recorte
	/// mostraria um boneco onde ha dois. Nenhuma regra da fusao depende de posicao depois do convite.
	/// </summary>
	internal void PorPertoNaFotoDeFusao(int id, int idDono, Vec2 desloc)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return;
		pl.Zone = dono.Zone;
		pl.Pos = dono.Pos + desloc;
	}

	/// <summary>DESFAZ a fusao deste corpo, pelo `Separar` de producao.</summary>
	internal void SepararNaFotoDeFusao(int id, string motivo)
	{
		if (FusaoDe(id) is { } f) Separar(f, motivo);
	}

	/// <summary>
	/// FORCA UMA FORMA -- <see cref="AdminForcarForma"/>, o mesmo `admin_forma` do menu P.
	///
	/// E o unico jeito de haver SSJ4 numa bancada: o degrau pede maestria, Oozaru Dourado despertado e
	/// BP de porta, e forjar tudo isso seria escrever a escada inteira a mao pra tirar uma foto.
	/// </summary>
	internal void FormaNaFotoDeFusao(int id, string forma)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;

		// ============================ O KI ENTRA CHEIO, E ISSO NAO E ZELO ============================
		// A forma tem DRENO (`TickDaForma`), e o SSJ4 tem o maior deles. Este corpo chega aqui depois de
		// atravessar a coreografia inteira -- duas fusoes de verdade, um puxao abortado e tres
		// cinematicas --, e com o Ki no fim a forma cai sozinha durante os segundos em que a bancada
		// espera a folha do cabelo trocar. O painel de admin do menu P ja faz exatamente isto pelo mesmo
		// motivo, e o diz no proprio aviso ("o Ki entra cheio, senao o tique derrubaria a forma").
		// ==========================================================================================
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AdminForcarForma(pl, forma);
	}

	// =====================================================================
	// AS JANELAS DA **COREOGRAFIA** -- o convite, o aceite, o puxao e a borda
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE A FOTO PRECISOU DESTAS SEIS JANELAS ============================
	/// Ate a fase 1 esta bancada entrava pelo <see cref="ComecarACenaNaFotoDeFusao"/>, ou seja **pelo
	/// meio**: ela plantava os dois corpos onde queria e mandava a cinematica comecar. Isso fotografa o
	/// CLARAO e nao fotografa a COREOGRAFIA -- e a coreografia e literalmente o que o dono descreveu
	/// (*"eles sao puxados um pro lado do outro e quando se encostarem a cinematica comeca"*).
	///
	/// Com estas janelas a bancada entra pela PORTA: `Convidar` (o mesmo que o verb da danca e o dos
	/// brincos chamam), `ResponderAoConvite` (o mesmo `fus_sim`), e dali em diante quem manda e o
	/// `TickDoPuxaoDeFusao` e o `TickDaCenaDeFusao` de producao. **Nenhuma delas move ninguem pra perto,
	/// nenhuma delas comeca cena, nenhuma delas funde.**
	/// ================================================================================================
	/// </summary>
	/// <returns>
	/// o convite ENTROU na mesa do convidado, e **o motivo quando nao entrou**. O motivo importa: a
	/// prova do portao da Metamoro tem duas metades (de longe recusa, colado aceita) e as duas so valem
	/// se a recusa for por DISTANCIA -- uma bancada que so soubesse "nao entrou" anunciaria o portao da
	/// distancia medindo o da skill.
	/// </returns>
	internal (bool Entrou, RecusaDeFusao Porque) ConvidarNaFotoDeFusao(int idA, int idB, TipoDeFusao tipo)
	{
		if (!_players.TryGetValue(idA, out ServerPlayer? a)) return (false, RecusaDeFusao.SemAssinatura);
		if (!_players.TryGetValue(idB, out ServerPlayer? b)) return (false, RecusaDeFusao.SemAssinatura);

		// A MESA E LIMPA ANTES, e nao depois: `RecusaDeFusao.JaTemPedido` faria a segunda metade da
		// prova do portao (o "colado ACEITA") reprovar por um motivo que nao e a distancia.
		_pedidosDeFusao.Remove(b.Id);
		RecusaDeFusao porque = AvaliarOConvite(a, b, tipo, NowMs());
		Convidar(a, b, tipo);
		return (_pedidosDeFusao.ContainsKey(b.Id), porque);
	}

	/// <summary>
	/// ============================ O PORTAO DA DANCA TEM QUATRO TRANCAS, E TRES DELAS NAO SAO A DISTANCIA ============================
	/// A prova R1 (*"Metamoro de LONGE e recusada, e COLADA e aceita"*) so vale se a metade "aceita"
	/// aceitar **por causa da distancia**. `Fusao.Avaliar` cobra da Danca, alem do tile ao lado, mais
	/// tres coisas: os DOIS sabem dancar, mesma raca, e poder proximo (<see cref="Fusao.LimiarDeProximidade"/>).
	///
	/// O corpo forjado nasce com o livro vazio e com o BP que a bancada mandou, e o do JOGADOR e o que a
	/// conta de linha de comando tiver -- ou seja, sem esta janela a metade "colada" reprovaria por
	/// `SemSkill` ou por `PoderDesigual`, **e a metade "de longe" ficaria verde pelo motivo errado**: a
	/// ordem do `Avaliar` poe `Longe` antes dos portoes da Danca, entao de longe a recusa e a certa, mas
	/// colado a recusa seria outra e a prova diria o contrario do que anuncia.
	///
	/// Aqui isso e CENARIO e nao a coisa medida: a skill entra pelo mesmo `Livro.Dar` que o
	/// aprendizado usa, e o BP expresso e igualado ao do jogador (o `PoderPraComparar` le aquele campo).
	/// O portao da distancia continua intocado, e e ele que as duas metades separam.
	/// ============================================================================================================================
	/// </summary>
	internal void PrepararAMetamoroNaFotoDeFusao(int idA, int idB)
	{
		if (!_players.TryGetValue(idA, out ServerPlayer? a)) return;
		if (!_players.TryGetValue(idB, out ServerPlayer? b)) return;

		foreach (ServerPlayer p in new[] { a, b })
		{
			p.Livro ??= new Jandirus.Core.Skills.SkillBook();
			p.Livro.Dar(PathDaDanca);
		}

		b.Race = a.Race;
		b.Ficha.expressedBP = a.Ficha.expressedBP > 0 ? a.Ficha.expressedBP : a.Ficha.BP;
	}

	/// <summary>
	/// APAGA A RECARGA DE 1 h DOS DOIS -- a mesma (e unica) concessao que o
	/// <see cref="ComecarACenaNaFotoDeFusao"/> ja faz, e pelo mesmo motivo: esta bancada fotografa
	/// VARIAS fusoes do mesmo jogador em cinquenta segundos, e a recarga e medida de verdade -- indo ao
	/// DISCO e voltando -- pela `--fusaoduplateste` (secoes L e L').
	/// </summary>
	internal void ZerarRecargaNaFotoDeFusao(int idA, int idB)
	{
		if (_players.TryGetValue(idA, out ServerPlayer? a)) a.Ficha.fusion_cooldown_until = 0;
		if (_players.TryGetValue(idB, out ServerPlayer? b)) b.Ficha.fusion_cooldown_until = 0;
	}

	/// <summary>O `fus_sim` de producao. Devolve se o convidado tinha mesmo um pedido pra aceitar.</summary>
	internal bool AceitarNaFotoDeFusao(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (!_pedidosDeFusao.ContainsKey(id)) return false;
		ResponderAoConvite(pl, aceitou: true);
		return true;
	}

	/// <summary>Tira um pedido pendente da mesa -- limpeza entre as duas metades do portao.</summary>
	internal void LimparPedidoNaFotoDeFusao(int id) => _pedidosDeFusao.Remove(id);

	/// <summary>
	/// O QUE O SERVIDOR SABE DESTA DUPLA NESTE INSTANTE -- e as quatro perguntas juntas, porque a prova
	/// do dono e sobre a ORDEM delas (*"existe um instante em que o puxao esta correndo e a cinematica
	/// ainda nao"*). Lidas em chamadas separadas, as quatro poderiam vir de tiques diferentes.
	/// </summary>
	internal (bool Puxando, bool EmCena, bool Fundido, double DistanciaPx, double DistanciaTiles,
			  bool SemRedeas, long Recarga)
		EstadoDaCoreografiaNaFotoDeFusao(int idA, int idB)
	{
		if (!_players.TryGetValue(idA, out ServerPlayer? a) || !_players.TryGetValue(idB, out ServerPlayer? b))
			return (false, false, false, -1, -1, false, 0);

		return (_sendoPuxadoPraFusao.ContainsKey(idA) && _sendoPuxadoPraFusao.ContainsKey(idB),
				_emCenaDeFusao.ContainsKey(idA),
				EstaFundido(idA),
				Vec2.Distance(a.Pos, b.Pos),
				// A MOEDA DO BYOND, e nao a linha reta: `JaEncostaram` (o `while` do
				// `Potara_Fusion.dm:124`) pergunta ao `get_dist`, que e Chebyshev sobre TURFS. Dois
				// corpos em turfs vizinhos na diagonal estao a 1 no `get_dist` e a ate 90 px em linha
				// reta -- e uma prova que cobrasse pixels reprovaria a cena certa.
				Fusao.DistanciaEmTilesDoDm(a.Pos, b.Pos, ZoneCollision.TileSize),
				a.PuxaoDeFusaoRestante > 0 && b.PuxaoDeFusaoRestante > 0,
				Math.Max(a.Ficha.fusion_cooldown_until, b.Ficha.fusion_cooldown_until));
	}

	/// <summary>
	/// DERRUBA UM CORPO -- o `Ficha.KO` que o `TickDoPuxaoDeFusao` le no corte *"um dos dois foi
	/// derrubado"*. E o gesto que a bancada de campo ja usa (`BioTeste`, `FusaoDuplaTeste`): o que
	/// interessa aqui e o ESTADO que o corte consulta, e nao por qual soco ele chegou.
	/// </summary>
	internal void DerrubarNaFotoDeFusao(int id, bool caido)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.KO = caido;
	}

	/// <summary>
	/// UM CORREDOR LIVRE PRA O PUXAO ANDAR, **na zona em que o jogador esta**.
	///
	/// Pelo mesmo laco da bancada de campo (<see cref="UmCorredorLivreParaOPuxao"/>), que ja aprendeu a
	/// perguntar ao `MoveRules.Occupied` de producao em vez de adivinhar onde ha chao. Sem isto os dois
	/// podem nascer com uma arvore (ou um lago) no meio, e o puxao desistiria sozinho -- a bancada
	/// ficaria vermelha por um motivo que nao tem nada a ver com a fusao.
	/// </summary>
	internal bool PlantarNoCorredorNaFotoDeFusao(int idDono, int idOutro, int tiles)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return false;
		if (!_players.TryGetValue(idOutro, out ServerPlayer? outro)) return false;

		Vec2 corredor = UmCorredorLivreParaOPuxao(tiles, dono.Zone);
		if (corredor.X == 0 && corredor.Y == 0) return false;

		dono.Pos = corredor;
		outro.Zone = dono.Zone;
		outro.Pos = corredor + new Vec2(tiles * ZoneCollision.TileSize, 0);
		return true;
	}

	/// <summary>
	/// TIRA DO MUNDO tudo o que esta bancada pos nele, e **devolve o cabelo do jogador**.
	///
	/// As fusoes primeiro: uma fusao viva com o corpo do passageiro removido do `_players` faria o
	/// tique de producao acusar "um dos dois deixou o mundo" a cada segundo depois da bancada -- e o
	/// jogador ficaria com o `FuseBuff` somado pra sempre, porque quem o tira e o `Separar`.
	/// </summary>
	internal void LimparAFotoDeFusao()
	{
		// O PUXAO PRIMEIRO, e ele so entrou na limpeza quando a bancada passou a fotografar a
		// coreografia: um puxao vivo continua escrevendo `Pos` e regando `PuxaoDeFusaoRestante` nos dois
		// corpos, e um deles e o do JOGADOR -- ficaria deslizando pra um corpo que a limpeza acabou de
		// tirar do mundo, sem as redeas.
		foreach (PuxaoDeFusao p in _puxoesDeFusao.ToList())
			AbortarOPuxaoDeFusao(p, "fim da bancada de foto");
		foreach (CenaDeFusao c in _cenasDeFusao.ToList()) SoltarDaCenaDeFusao(c);
		foreach (FusaoAtiva f in _fusoes.ToList()) Separar(f, "fim da bancada de foto");

		foreach (int id in _ffNascidos)
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			pl.Ficha.fusion_cooldown_until = 0;
			_pedidosDeFusao.Remove(id);
			_npcsPraTirar.Remove(pl);
			_players.Remove(id);
			ZoneList(pl.Zone.Hash).Remove(pl);
		}
		_ffNascidos.Clear();

		if (_ffCabeloDoJogador.Length > 0
			&& _players.TryGetValue(_ffDonoOriginal, out ServerPlayer? dono))
		{
			dono.Visual.Cabelo = _ffCabeloDoJogador;
			TrocarAparencias(dono);
			_ffCabeloDoJogador = "";
		}

		// O CEU VOLTA AO QUE ERA -- ver `SolAPinoNaFotoDeFusao`.
		if (_ffMexeuNoCeu) { _adiantoDoCeu = _ffCeuAntes; _ffMexeuNoCeu = false; }
	}
}
