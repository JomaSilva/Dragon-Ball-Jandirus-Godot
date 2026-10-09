using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// ============================ AS JANELAS DO SEGUNDO TRAILER (as SAGAS) ============================
/// O dono pediu (2026-10-07) um trailer novo, com o feixe novo, que CONTA as lutas do anime em cima dos
/// sistemas do jogo: o Kamehameha contra o Galick Ho virado por um Kaio-ken, o amigo morto diante do
/// heroi e o primeiro Super Saiyajin em Namek explodindo, o Gohan contra o Cell, o Boo e a Genkidama, e
/// a Terra estourando vista do espaco.
///
/// As janelas do primeiro trailer (`GameServer.Trailer.cs`) poem um palco e deixam o jogo acontecer.
/// Estas fazem o mesmo, com uma diferenca que precisa estar ESCRITA: uma luta de saga tem ROTEIRO -- quem
/// perde primeiro, em que instante o poder sobe, quem morre. Entao aqui o diretor escolhe a HORA de cada
/// coisa; o que ACONTECE nessa hora continua saindo pela porta de producao (o `Kaioken`, o `Canalizar`,
/// a disputa de ki, o `ComecarDestruicao`, o `Morrer`). Onde a cena pede algo que o jogo nao faz
/// sozinho, a janela diz isso no proprio comentario.
/// ==================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// O KAIO-KEN DE UM CORPO DE CENA, pelo `Kaioken` de producao (o buff, a aura vermelha e o grito
	/// "KAIOKEN TIMES N!!" sao dele). O corpo forjado nao tem a skill no livro, e por isso a janela entra
	/// depois da porta do verbo (`VivoPorPrefixo`), que e so a pergunta "voce sabe isto?".
	///
	/// A VIRADA DO EMBATE NAO E DAQUI: a disputa de ki le o poder de agora dos dois a cada tique
	/// (`LerOPoderDeAgora`), entao o Kaio-ken aceso no meio do encontro empurra sozinho. Esta janela
	/// ja teve uma irma que reescrevia a vantagem na mao -- de quando o jogo a congelava no comeco.
	/// </summary>
	internal void KaiokenNoTrailer(int id, double vezes)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;

		// O FOLEGO DO KAIO-KEN: ele so se sustenta com o Ki acima de `100 / KiMod` (`kaioken.dm:189`), e o
		// corpo forjado nasce com um tanque de novato -- cinco segundos de raio e ele ja esta abaixo do
		// piso, entao o buff acendia e caia no primeiro segundo. O heroi de uma saga nao e novato: o
		// tanque dele e reposto pela mesma janela das fotos de tecnica.
		RegarOKiDaVariedade(id);
		Kaioken(pl, vezes);
		GD.Print($"[trailer2] Kaio-ken de {pl.Name}: Ki {pl.Ficha.Ki:0}/{pl.Ficha.MaxKi:0}, "
				 + $"piso {100 / Math.Max(pl.Ficha.KiMod, 0.01):0}");
	}

	/// <summary>
	/// QUANTO VALE AGORA CADA EMPURRAO DO LADO A da disputa da foto -- so leitura, pra a cena anotar
	/// na marca o instante em que o poder virou. Zero se nao ha disputa.
	/// </summary>
	internal double EmbateDeFoto_Vantagem()
	{
		if (_fotoEkA == 0 || !_emEmbateDeKi.TryGetValue(_fotoEkA, out DisputaDeKi? d)) return 0;
		return (d.A.Quem.Id == _fotoEkA ? d.A : d.B).Vantagem;
	}

	/// <summary>
	/// UM CORREDOR DE CHAO PRA UMA CENA DE DOIS, perto de quem olha: as duas pontas, `tiles` de
	/// distancia uma da outra. E a mesma busca da foto do embate (`FaixaDeChaoDaFoto`: chao de verdade,
	/// nunca agua, e dentro do que a camera mostra).
	/// </summary>
	internal (bool Achou, Vec2 Esquerda, Vec2 Direita) CorredorNoTrailer(int idDono, int tiles, int tilesAbaixo)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)
			|| !FaixaDeChaoDaFoto(dono, tiles, tilesAbaixo, out Vec2 esquerda)) return (false, default, default);
		return (true, esquerda, new Vec2(esquerda.X + tiles * ZoneCollision.TileSize, esquerda.Y));
	}

	/// <summary>
	/// A DISPUTA DE KI ENTRE DOIS CORPOS QUE JA ESTAO NA CENA -- o heroi, um chefe de molde, um ator
	/// forjado ja transformado. E a irma da <see cref="EmbateDeFoto_Montar"/>, que FORJA os dois: uma
	/// saga precisa do Freeza de verdade de um lado e do Super Saiyajin que acabou de estrear do outro,
	/// e nao de dois duelistas novos.
	///
	/// QUEM POE OS DOIS DE FRENTE E A CENA (o <see cref="CorredorNoTrailer"/> da as pontas), e nao esta
	/// janela: o `Facing` do jogador e do CLIENTE, entao o heroi tem que ser virado pelo lado dele antes
	/// de o raio sair -- escrever o do servidor aqui duraria ate o proximo pacote de estado.
	///
	/// O que ela arruma e o folego (o tanque de Ki cheio) e a cor do ki. O raio de cada um sai pelo
	/// <see cref="Canalizar"/> de producao, o encontro pelo gatilho do tique de projetil e o cabo de
	/// guerra pelo `TickDosEmbatesDeKi` -- e dai em diante valem as mesmas janelas da foto
	/// (`EmbateDeFoto_Estado`, `_Apertar`, `_Limpar`), que leem estes dois ids.
	///
	/// O LADO A RESPONDE AS LETRAS: se ele nao tem conexao (NPC, ator), ganha o teclado de mentira da
	/// bancada (`_comTecladoDeTeste`), que so responde "este corpo tem teclado"; o heroi ja tem o dele.
	/// O lado B fica como for -- um NPC empurra pela taxa automatica (`side_presses`).
	/// </summary>
	internal bool EmbateDeCena_Disparar(int idA, int idB, string verboDeA, string verboDeB,
										string corDeA = "", string corDeB = "", double escala = 1)
	{
		if (!_players.TryGetValue(idA, out ServerPlayer? pa) || !_players.TryGetValue(idB, out ServerPlayer? pb))
			return false;

		RegarOKiDaVariedade(idA);
		RegarOKiDaVariedade(idB);
		if (pa.Peer == null) _comTecladoDeTeste.Add(idA);

		if (LerCor(corDeA, out Jandirus.Core.Appearance.Rgb ca)) { pa.Visual.CorKi = ca; ReapresentarAparencia(pa); }
		if (LerCor(corDeB, out Jandirus.Core.Appearance.Rgb cb)) { pb.Visual.CorKi = cb; ReapresentarAparencia(pb); }

		ReceitaDeProjetil ra = RaioDeTeste(), rb = RaioDeTeste();
		ra.EscalaVisual = rb.EscalaVisual = escala;
		// O GRITO E O DA TECNICA: quem canaliza grita o nome da receita (`TickDosCanaisDeKi`), e a de
		// bancada se chama "Onda de Ki" -- o Kamehameha do roteiro saia com o balao errado em cima.
		ra.Nome = verboDeA.Replace('_', ' ');
		rb.Nome = verboDeB.Replace('_', ' ');

		// O PRAZO DE RAIO DE CADA UM COMECA DO ZERO: a conta de um raio anterior pode ter ficado pra tras
		// (um canal fechado pela briga, sem outro canal no mundo naquele tique -- ver o comentario do
		// `SoltarOsRaiosNoTrailer`), e o raio do roteiro nao pode nascer com o tempo de outro ja gasto.
		_raioDaIaAte.Remove(idA);
		_raioDaIaAte.Remove(idB);
		Canalizar(pa, verboDeA, 10 * pa.Ficha.BaseDrain(), ra);
		Canalizar(pb, verboDeB, 10 * pb.Ficha.BaseDrain(), rb);

		_fotoEkA = idA;
		_fotoEkB = idB;
		return true;
	}

	// =====================================================================
	// O ROTEIRO DE UMA SAGA: a fala, a raiva, a tecla, o golpe que mata
	// =====================================================================
	/// <summary>
	/// UM CORPO DA CENA FALA -- pelo <see cref="Falar"/> de producao (o balao e a linha do chat saem
	/// dele). A recarga de 0,4 s entre falas e contra cliente modificado usando o chat de megafone; a
	/// cena fala quando o roteiro manda, e por isso a marca dela e apagada antes.
	/// </summary>
	internal void FalaNoTrailer(int id, string texto)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		_ultimaFala.Remove(id);
		Falar(pl, Protocol.Fala.Diz, texto);
	}

	/// <summary>
	/// O AMIGO MORREU NA FRENTE DELE -- o gancho de producao do luto (<see cref="AmigoAbatido"/>, grau
	/// extremo), chamado direto. Em jogo quem o chama e o `LutoNaVizinhanca` do convivio, quando a
	/// vitima estava na lista social de quem assiste; o corpo de cena acabou de nascer e nao tem lista
	/// nenhuma, entao a cena diz o que a lista diria.
	/// </summary>
	/// <returns>verdadeiro se a raiva ERUPCIONOU agora (e nao so se prolongou).</returns>
	internal bool FuriaNoTrailer(int id, string nomeDoAmigo) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
		&& AmigoAbatido(pl, nomeDoAmigo, Jandirus.Core.Forms.NivelDeRaiva.Extrema);

	/// <summary>
	/// "A TECLA DE TRANSFORMAR", apertada por um corpo de cena: o <see cref="Transformar"/> de producao,
	/// que escolhe o degrau que cabe (com a raiva acesa, o que nasce dela). Diferente do
	/// <see cref="FormaNoTrailer"/>, que e o `admin_forma` e veste a forma pedida sem perguntar nada.
	/// </summary>
	/// <returns>a forma em que o corpo ficou.</returns>
	internal string SubirDeFormaNoTrailer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return "";
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		Transformar(pl, subir: true);
		return pl.Forma.Atual;
	}

	/// <summary>
	/// ESTE CORPO LUTA PRA MATAR (ou so pra derrubar) -- o `murderToggle` (`CombatState.Letal`), que o
	/// jogador liga no menu. NPC NENHUM O LIGA em producao (o padrao e falso, e nada o escreve por eles):
	/// um chefe de saga so nocauteia, e o cerebro dele larga a presa caida. Uma saga tem mortes marcadas
	/// no roteiro -- e o tiro copia este valor QUANDO NASCE (`Projetil.Letal`), entao ele tem que ser
	/// escrito antes do disparo.
	/// </summary>
	internal void LetalNoTrailer(int id, bool letal)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null) pl.Combate.Letal = letal;
	}

	/// <summary>
	/// O ZANZO CLASH ENTRE DOIS CORPOS DA CENA, sem o sorteio: o `Comecar` de producao, direto. Em jogo
	/// ele nasce de dois golpes simultaneos entre quem tem Zanzoken, com 50% de chance (`TentarEmbate`);
	/// a cena escolhe a HORA, e o embate -- os sumicos, os baques, as letras -- e o de sempre.
	///
	/// ============================ O QUE A CENA NAO PULA: AS TRAVAS DE CORPO ============================
	/// O que esta janela dispensa e o SORTEIO e a troca de socos; as travas de corpo do `TentarEmbate`
	/// continuam valendo aqui, e a do ki foi aprendida do jeito caro. A primeira versao chamava o
	/// `Comecar` sem perguntar nada, e a tomada de Namek comecou o embate com o duble ainda segurando
	/// uma Onda de Ki: o embate some com o corpo e o leva de um lado pro outro, e o raio -- cuja cauda e
	/// a boca do dono -- foi junto, girando pela tela em pedacos (o rival cruzou o tronco doze vezes).
	/// O dono viu: "o beam ficou bugado durante o zanzoclash". E a mesma razao que o `TentarEmbate`
	/// escreve pra recusar quem canaliza; quem limpa o ar antes e a cena (`ApagarOKiNoTrailer`).
	/// ================================================================================================
	/// </summary>
	internal bool ZanzoClashNoTrailer(int a, int b)
	{
		if (!_players.TryGetValue(a, out ServerPlayer? pa) || !_players.TryGetValue(b, out ServerPlayer? pb)) return false;
		if (_emEmbate.ContainsKey(a) || _emEmbate.ContainsKey(b)) return false;
		if (EnraizadoPorKi(a) || EnraizadoPorKi(b) || _emEmbateDeKi.ContainsKey(a) || _emEmbateDeKi.ContainsKey(b)) return false;
		if (pa.Deitado || pb.Deitado) return false;
		Comecar(pa, pb, NowMs());
		return true;
	}

	/// <summary>
	/// O KI DOS DOIS SAI DO AR: eles soltam o que tem na mao (<see cref="SoltarOsRaiosNoTrailer"/>) e
	/// todo tiro deles que ainda voa acaba -- pelo `Matar` de producao, entao o raio ESVAZIA como
	/// qualquer raio que termina (a cabeca para e o rastro e engolido), em vez de sumir num quadro.
	///
	/// E pra a marca da cena que nao pode ter ki solto por perto: o Zanzo Clash. Um raio de NPC fica
	/// aberto por doze segundos (`SegundosDeFeixeDeNpc`) e quem o fecharia antes e o cerebro -- que a
	/// cena acabou de guardar na coxia.
	/// </summary>
	internal void ApagarOKiNoTrailer(int a, int b)
	{
		SoltarOsRaiosNoTrailer(a, b);
		foreach (int id in new[] { a, b })
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
				if (p.Dono == id) Matar(p, FimDeProjetil.Apagou);
		}
	}

	/// <summary>Quantos tiros dos dois ainda estao vivos -- pra a cena esperar o ar limpar.</summary>
	internal int KiNoArNoTrailer(int a, int b)
	{
		int vivos = 0;
		foreach (int id in new[] { a, b })
		{
			if (!_players.TryGetValue(id, out ServerPlayer? pl)) continue;
			foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
				if (p.Dono == id && p.Vivo) vivos++;
		}
		return vivos;
	}

	/// <summary>
	/// OS NPCS DA ZONA DE QUEM OLHA FICAM CALADOS: a fala automatica de combate (o `Falatorio` de cada
	/// cerebro -- "Toma essa!", "Patetico!") e desligada, inclusive a dos cerebros que esperam a deixa.
	///
	/// E pra as tomadas da versao em INGLES do trailer. O jogo so existe em portugues: as falas do
	/// ROTEIRO a cena traduz (sao texto dela, passado por opcao), mas as do `Falatorio` sao do jogo, e
	/// um vilao que diz "One by one." e no soco seguinte grita "Toma essa!" e pior que um vilao calado.
	/// O interruptor e o mesmo `Ligado` que ja nasce desligado em todo cerebro montado a mao -- nada de
	/// producao muda. A cena repete a chamada porque os atores nascem ao longo dela.
	/// </summary>
	internal void CalarAsBocasNoTrailer(int idDono)
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return;
		foreach (ServerPlayer p in ZoneList(dono.Zone.Hash))
			if (p.Cerebro is { } c) c.Boca.Ligado = false;
		foreach (Jandirus.Core.Ai.Cerebro c in _cerebrosNaCoxia.Values) c.Boca.Ligado = false;
	}

	/// <summary>
	/// O ATOR ESPERA A DEIXA (ou a recebe): enquanto espera, o cerebro dele fica guardado e o corpo nao
	/// decide nada -- fica de pe onde esta, como um corpo cuja posse venceu (o laco da IA ja pula quem
	/// esta sem cerebro, `GameServer.Clone.cs`).
	///
	/// ============================ POR QUE NAO E O `Stun` ============================
	/// Foi a primeira tentativa, e a tomada mostrou que nao serve: o `Stun` de combate segura o SOCO, e o
	/// chefe "preso" continuou andando em volta do heroi durante a transformacao -- e falando. Um chefe
	/// de saga e HOSTIL: sem presa imposta ele caca o jogador mais perto (`PresaDoHostil`), e o jogador
	/// desta cena e o heroi no meio da cinematica (ou a camera escondida). O vilao tem que ASSISTIR,
	/// como no anime; pra isso o que se tira dele e a decisao, e nao o braco.
	/// ==============================================================================
	/// </summary>
	internal void DeixaNoTrailer(int id, bool esperar)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? npc)) return;

		if (!esperar)
		{
			if (_cerebrosNaCoxia.Remove(id, out Jandirus.Core.Ai.Cerebro? volta)) npc.Cerebro = volta;
			return;
		}
		if (npc.Cerebro is not { } cerebro) return;
		_cerebrosNaCoxia[id] = cerebro;
		npc.Cerebro = null;
		npc.Moving = false;
	}

	private readonly Dictionary<int, Jandirus.Core.Ai.Cerebro> _cerebrosNaCoxia = [];

	/// <summary>
	/// UM NPC DE MOLDE DE UMA RACA CERTA. O molde de lutador SORTEIA a raca entre quatro (e o do
	/// cidadao a herda do planeta), entao o amigo careca do heroi nasceu namekuseijin em Namek -- o
	/// dono viu na tomada. A raca decide o CORPO (`VisualCatalog.CorpoSprite` le a raca, o genero e o
	/// indice), e por isso nao ha figurino que conserte depois: sorteia-se de novo, em outro "lugar"
	/// da semente, ate sair a raca pedida.
	/// </summary>
	internal int NpcDeRacaNoTrailer(int idDono, string moldeId, string raca, Vec2 desloc)
	{
		for (int tentativa = 0; tentativa < 40; tentativa++)
		{
			int id = NpcDoTrailer(idDono, moldeId, desloc);
			if (id == 0) return 0;
			if (_players[id].Race == raca) return id;

			_nascidosDoTrailer.Remove(id);
			RemoverNpc(_players[id]);
		}
		GD.PrintErr($"[trailer2] o molde `{moldeId}` nao deu um `{raca}` em 40 sorteios");
		return 0;
	}

	/// <summary>
	/// O CORPO DE UM CHEFE, quando o do molde nao e o da cena. **E um buraco do jogo, e nao so do
	/// trailer**: o `cell` nasce como a larva nos quatro degraus e o `majin_boo` como o Majin cinza sem
	/// tinta, porque so o sorteio de nascimento escreve o `Visual.Corpo` de um NPC (`AparenciaDeNpc`) e
	/// o `corpo` de um degrau do roteiro so e lido pra racas Frost (`EntrarNoDegrau`).
	///
	/// A cena escreve o indice que o degrau deveria escrever: o corpo e o MESMO campo que toda raca usa
	/// (`Appearance.Corpo`), e ele chega na tela pelo `TrocarAparencias` de sempre.
	/// </summary>
	/// <param name="pele">a cor da pele "R,G,B" (so Majin e Kai a usam), ou vazio pra nao mexer.</param>
	internal void CorpoDoAtorNoTrailer(int id, int corpo, string pele = "")
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Visual.Corpo = corpo;
		if (pele == "nenhuma") pl.Visual.CorPele = null;
		else if (LerCor(pele, out Jandirus.Core.Appearance.Rgb cor)) pl.Visual.CorPele = cor;
		TrocarAparencias(pl);
	}

	/// <summary>
	/// UMA FORMA SEM CENA NENHUMA: o corpo ja "viu" a estreia (`EstadoDeForma.EstreiaVista`) e ja a
	/// domina (maestria 50 -- abaixo disso a troca ainda e a cena CURTA, dez segundos plantado no
	/// Super Saiyajin; ver `Cinematicas.Degrau`). E pro DUBLE do heroi: a estreia foi filmada no plano
	/// anterior, e a luta do plano seguinte comeca com ele ja transformado.
	/// </summary>
	internal void FormaJaVistaNoTrailer(int id, string forma)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Forma.EstreiaVista.Add(Jandirus.Core.Forms.Catalogo.Rede(forma));
		pl.Forma.Maestria.Por(forma, 50);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AdminForcarForma(pl, forma);
	}

	/// <summary>Este corpo esta num Zanzo Clash agora? Pra a cena esperar o embate acabar.</summary>
	internal bool EmZanzoNoTrailer(int id) => _emEmbate.ContainsKey(id);

	// =====================================================================
	// O PLANETA MORRE DURANTE A LUTA
	// =====================================================================
	private PalcoDeMortes? _agoniaDoTrailer;
	private ZoneKey _mundoQueMorreNoTrailer;

	/// <summary>
	/// UM MUNDO COMECA A SE PARTIR, e a agonia dele fica no ponto que o diretor pede.
	///
	/// A destruicao entra pela porta unica de producao (<see cref="ComecarDestruicao"/>, a do verb
	/// Planet Destroy), e dali pra frente quem aperta o ceu, sorteia o tremor, abre a cratera e derruba
	/// o chao e o `TremorDaExplosao` de producao. O que a cena escolhe e o RELOGIO: `faltam` segundos
	/// pra explodir (de 310 a 0), fixados enquanto ela repetir a chamada -- o mesmo atalho do palco
	/// `--agoniaviva`, e pelo mesmo motivo (o efeito precisa de tempo pra ACUMULAR num patamar).
	///
	/// O REGISTRO DO DONO NAO PAGA: a morte acontece dentro de um <see cref="PalcoDeMortes"/>, aberto
	/// ANTES de a arma disparar e fechado pelo <see cref="FecharAAgoniaNoTrailer"/>.
	/// </summary>
	/// <param name="mundo">o nome de um planeta pre-feito ("Earth", "Namek"), ou vazio = o mundo em que o dono esta.</param>
	internal bool AgoniaNoTrailer(int idDono, double faltam, string mundo = "")
	{
		if (!_players.TryGetValue(idDono, out ServerPlayer? dono)) return false;

		if (_agoniaDoTrailer == null)
		{
			_mundoQueMorreNoTrailer = mundo.Length > 0 ? ZoneKey.Premade(mundo) : dono.Zone;
			_agoniaDoTrailer = PalcoDeMortesDeBancada();
			bool foi = ComecarDestruicao(_mundoQueMorreNoTrailer,
										 Math.Max(dono.Ficha.expressedBP, MortePlanetaria.BpExigido(1)),
										 "trailer 2: o planeta morre durante a luta");
			GD.Print($"[trailer2] a destruicao de '{_mundoQueMorreNoTrailer.Name}' comecou (aceita={foi})");
		}

		if (MorteDaZona(_mundoQueMorreNoTrailer) is not { } e) return false;
		e.Faltam = faltam;
		MandarMortosPraTodos();
		return true;
	}

	/// <summary>Quanto falta, e em que fase o mundo da cena esta -- pra a marca do diretor.</summary>
	internal (double Faltam, string Fase) EstadoDaAgoniaNoTrailer() =>
		_agoniaDoTrailer != null && MorteDaZona(_mundoQueMorreNoTrailer) is { } e ? (e.Faltam, e.Fase.ToString()) : (-1, "");

	/// <summary>O mundo volta a existir no registro (o palco fecha). Ver <see cref="AgoniaNoTrailer"/>.</summary>
	internal void FecharAAgoniaNoTrailer()
	{
		_agoniaDoTrailer?.Dispose();
		_agoniaDoTrailer = null;
	}

	/// <summary>
	/// A ESTREIA DE UMA FORMA, DE NOVO: o corpo "esquece" que ja a viu e que a domina, e o proximo
	/// `FormaNoTrailer` toca a cinematica inteira. Os moldes que ja nascem com a forma na lista (o
	/// guardiao saiyajin) gastam a estreia no nascimento -- e a saga quer filma-la.
	/// </summary>
	internal void EstreiaDeNovoNoTrailer(int id, string forma)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Forma.EstreiaVista.Remove(Jandirus.Core.Forms.Catalogo.Rede(forma));
		pl.Forma.Maestria.Por(forma, 0);
	}

	/// <summary>
	/// UMA FORMA PELA CENA CURTA: a estreia ja foi vista, a maestria ainda e zero -- e o que o jogo
	/// toca na SEGUNDA vez que alguem assume a forma (`Cinematicas.Degrau`). O Super Saiyajin 3 estreia
	/// em 140 segundos, e um trailer nao tem 140 segundos pra uma transformacao; a curta dele tem dez.
	/// </summary>
	internal void FormaCurtaNoTrailer(int id, string forma)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Forma.EstreiaVista.Add(Jandirus.Core.Forms.Catalogo.Rede(forma));
		pl.Forma.Maestria.Por(forma, 0);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		AdminForcarForma(pl, forma);
	}

	/// <summary>Um ator sai de cena antes do fim (o duble, quando o heroi de verdade volta pro plano).</summary>
	internal void TirarDoTrailer(int id)
	{
		_cerebrosNaCoxia.Remove(id);
		if (_nascidosDoTrailer.Remove(id) && _players.TryGetValue(id, out ServerPlayer? npc)) RemoverNpc(npc);
	}

	/// <summary>
	/// O PODER EXPRESSO DE UM ATOR, levado ao numero que o roteiro pede -- escalando o BP de base dele
	/// (a forma, a raiva e o resto da conta do `PowerLevel` ficam como estao). Uma saga tem quem e mais
	/// forte em cada momento: o vilao domina ate a virada, e "dominar" numa disputa de ki e a razao
	/// entre os dois poderes expressos. Acertar isso pelo BP de base seria adivinhar o multiplicador de
	/// cada forma e a curva de cada chefe.
	/// </summary>
	internal void ExpressoNoTrailer(int id, double alvo)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || alvo <= 0) return;
		pl.Ficha.PowerLevel(agoraMs: NowMs());
		if (pl.Ficha.expressedBP <= 0) return;

		double fator = alvo / pl.Ficha.expressedBP;
		pl.Ficha.BP *= fator;
		// O CHEFE GUARDA O BP DE CADA DEGRAU no roteiro (`BpsPinados`), e o reescreve ao trocar de degrau:
		// os degraus andam juntos, senao a proxima forma dele desfaria o acerto.
		if (pl.Papel?.BpsPinados is { } pinados)
			for (int i = 0; i < pinados.Length; i++) pinados[i] *= fator;
		pl.Ficha.Statify();
		pl.Ficha.PowerLevel(agoraMs: NowMs());
	}

	/// <summary>
	/// O ATOR FICA DE PE E INTEIRO pra a proxima marca da cena: levanta do nocaute, sai do arremesso,
	/// perde o atordoamento, e o corpo e o tanque de Ki voltam cheios. Uma colisao de ki marcada no
	/// roteiro nao pode depender de como a briga de cerebros do plano anterior acabou -- na primeira
	/// tomada do Cell ele estava no ar de um arremesso e o raio dele nao saiu (`PodeAtirar` recusa).
	/// </summary>
	internal void DePeNoTrailer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.Ficha.dead) return;
		pl.Combate.Corpo.Restaurar();
		if (pl.Ficha.KO) pl.Combate.Levantar();
		pl.Combate.Stun = 0;
		pl.Combate.SincronizarVida();
		pl.TiquesDeVoo = 0;
		pl.ArrastoRestante = 0;
		RegarOKiDaVariedade(id);
	}

	/// <summary>Este corpo esta com um raio na mao (canalizando)? Pra a cena conferir que o disparo saiu.</summary>
	internal bool TemCanalNoTrailer(int id) => _canais.ContainsKey(id);

	/// <summary>
	/// OS DOIS SOLTAM O QUE TEM NA MAO: a disputa (se houver) sai sem resolver e os canais fecham. E a
	/// metade do `EmbateDeFoto_Limpar` que nao apaga os corpos -- pra uma cena tentar o disparo de novo.
	/// </summary>
	internal void SoltarOsRaiosNoTrailer(int a, int b)
	{
		foreach (int id in new[] { a, b })
		{
			if (_emEmbateDeKi.TryGetValue(id, out DisputaDeKi? d))
			{
				if (d.A.Feixe != null) d.A.Feixe.EmEmbate = false;
				if (d.B.Feixe != null) d.B.Feixe.EmEmbate = false;
				Fechar(d);
			}
			if (_canais.TryGetValue(id, out CanalDeKi? c)) FecharCanal(id, c, null);

			// E O PRAZO DO RAIO DELE ZERA. Quem nao tem teclado segura um raio por doze segundos
			// (`TickDoPrazoDeRaioDaIa`), e a conta de quem soltou so e apagada no tique seguinte SE ainda
			// houver algum canal no mundo -- e esta janela fecha os dois de uma vez. O tempo velho ficava,
			// o raio marcado do roteiro nascia com onze segundos "ja gastos" e era fechado no meio da
			// carga: numa tomada de Namek o Freeza disparou e o Kamehameha do outro lado nunca saiu.
			_raioDaIaAte.Remove(id);
		}
	}

	/// <summary>
	/// O HEROI E SO O PAR DE OLHOS DESTA CENA (ou volta a ser um corpo como os outros): enquanto so
	/// olha, ele e INTOCAVEL -- a mesma carencia de quem acabou de renascer (`CombatState.Carencia`),
	/// que tira o corpo do caminho dos tiros, do dano em area e da mira dos cerebros.
	///
	/// O corpo do jogador TEM que estar no palco (o servidor so manda a cada um o que esta perto dele),
	/// e escondido ele continua la, com o poder de um novato no meio de uma luta de chefes. Numa tomada
	/// do Cell a explosao do fim da colisao o matou: a tela foi pro carregamento do Outro Mundo e o
	/// resto do plano filmou nuvens.
	/// </summary>
	internal void OlhosNoTrailer(int id, bool soOlha)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl) && pl.Combate != null) pl.Combate.Carencia = soOlha ? 1e6 : 0;
	}

	/// <summary>
	/// O raio deste corpo JA NASCEU e esta vivo? O canal abre na hora, mas a cabeca so nasce quando a
	/// carga fecha (um segundo, no raio de bancada) -- e e so entao que a cena sabe se o disparo saiu.
	/// </summary>
	internal bool TemRaioNoTrailer(int id) => _canais.GetValueOrDefault(id)?.Raio is { Vivo: true };

	/// <summary>
	/// A VELOCIDADE, em pixels por segundo, do tiro mais novo deste dono (zero sem tiro). A cena do ki rebatido
	/// ergue a guarda um TEMPO antes do contato -- e pra virar esse tempo em distancia ela precisa saber quanto
	/// o tiro anda, que e de cada tecnica.
	/// </summary>
	internal float VelocidadeDoTiroNoTrailer(int dono)
	{
		if (!_players.TryGetValue(dono, out ServerPlayer? pl)) return 0;
		Projetil? novo = null;
		foreach (Projetil p in ProjeteisDaZona(pl.Zone.Hash))
			if (p.Dono == dono && p.Vivo && (novo == null || p.Id > novo.Id)) novo = p;
		return novo == null ? 0 : (float)(ZoneCollision.TileSize / novo.SegundosPorTile);
	}

	/// <summary>
	/// A COR DO KI DE UM CORPO DE CENA ("R,G,B") -- a `Blast_Color` de producao (`Appearance.CorKi`), que a
	/// tela de criacao deixa escolher. O personagem de linha de comando nasce com um ki quase branco, e
	/// uma Genkidama branca num piso claro some; a do roteiro e azul.
	/// </summary>
	internal void CorDeKiNoTrailer(int id, string cor)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || !LerCor(cor, out Jandirus.Core.Appearance.Rgb c)) return;
		pl.Visual.CorKi = c;
		ReapresentarAparencia(pl);
	}

	/// <summary>
	/// QUEM DOMINA A DISPUTA DA CENA: o poder do lado B passa a ser `razao` vezes o do lado A (1 =
	/// parelhos; acima de 1, B empurra). A conta e a do proprio encontro -- o PODER DO FEIXE
	/// (`PoderDoLado`: BP, pericia de ki e a tecnica), e nao so o BP: a primeira versao igualava o poder
	/// expresso dos dois corpos antes do disparo, e um heroi contra um figurante de ficha diferente
	/// saia com vantagem 0,54 onde o roteiro pedia 1,25.
	///
	/// O ajuste e no BP de base do lado B (<see cref="ExpressoNoTrailer"/>), e a disputa o le no tique
	/// seguinte, como le qualquer salto de poder.
	/// </summary>
	/// <returns>a vantagem do lado A depois do ajuste, ou zero se nao ha disputa.</returns>
	internal double RazaoDoEmbateNoTrailer(double razao)
	{
		if (razao <= 0 || _fotoEkA == 0 || !_emEmbateDeKi.TryGetValue(_fotoEkA, out DisputaDeKi? d)) return 0;

		LadoDeKi la = d.A.Quem.Id == _fotoEkA ? d.A : d.B, lb = la == d.A ? d.B : d.A;
		LerOPoderDeAgora(d.A, d.B);
		double pa = PoderDoLado(la), pb = PoderDoLado(lb);
		if (pa <= 0 || pb <= 0) return 0;

		ExpressoNoTrailer(lb.Quem.Id, lb.Quem.Ficha.expressedBP * razao * pa / pb);
		// A ARRUMACAO DO PALCO NAO E UM AUMENTO DE PODER. O raio de B ja saiu da mao, e o poder dele acabou de ser
		// posto no numero do roteiro: a regua do tamanho do raio (`Projetil.PoderDeReferencia`, 2026-10-08) recomeca
		// daqui -- senao o raio de quem o roteiro fez dominar ENGROSSARIA no primeiro segundo do encontro, sem
		// ninguem ter se transformado. A virada (`ViradaDoEmbateNoTrailer`) nao faz isto, e de proposito: la o
		// salto de poder E a cena, e o raio crescendo com ele e o que o dono pediu pra ver.
		if (lb.Feixe != null) lb.Feixe.PoderDeReferencia = lb.Quem.Ficha.expressedBP;
		LerOPoderDeAgora(d.A, d.B);
		return la.Vantagem;
	}

	/// <summary>
	/// A VIRADA TEM TAMANHO: a vantagem do lado A da disputa da cena sobe ate `alvo` (se ja nao
	/// estiver la), aumentando o poder DELE -- o BP de base, pelo <see cref="ExpressoNoTrailer"/>, e a
	/// disputa o le no tique seguinte como le qualquer salto de poder.
	///
	/// A furia que a cena acende (`AmigoAbatido`) da o que a ficha de cada duble der, e o duble e
	/// sorteado: numa tomada do Cell a virada veio com vantagem 1,21 e o filho venceu no limite dos
	/// quinze segundos; na seguinte veio com 1,10 e a disputa acabou EMPATADA (medidor 46, a onda de
	/// choque nos dois) -- e o roteiro e "ele vence o clash matando o Cell". Com 2 o encontro atravessa
	/// o corredor em uns cinco segundos, e quem fecha a disputa e o empurrao, nao o relogio.
	/// </summary>
	/// <returns>a vantagem do lado A depois do ajuste, ou zero se nao ha disputa.</returns>
	internal double ViradaDoEmbateNoTrailer(double alvo)
	{
		if (alvo <= 0 || _fotoEkA == 0 || !_emEmbateDeKi.TryGetValue(_fotoEkA, out DisputaDeKi? d)) return 0;

		LadoDeKi la = d.A.Quem.Id == _fotoEkA ? d.A : d.B, lb = la == d.A ? d.B : d.A;
		LerOPoderDeAgora(d.A, d.B);
		double pa = PoderDoLado(la), pb = PoderDoLado(lb);
		if (pa <= 0 || pb <= 0) return 0;

		if (pa < alvo * pb) ExpressoNoTrailer(la.Quem.Id, la.Quem.Ficha.expressedBP * alvo * pb / pa);
		LerOPoderDeAgora(d.A, d.B);
		return la.Vantagem;
	}

	/// <summary>
	/// UM ATOR SOLTA UM RAIO, sozinho, pro lado que esta olhando -- o <see cref="Canalizar"/> de producao
	/// com o raio de bancada (a mesma receita da foto do embate) e a cor de ki pedida. Pra a cena em que
	/// um corpo so mostra o que tem, sem rival do outro lado.
	/// </summary>
	internal void RaioNoTrailer(int id, string verbo, string cor = "")
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		RegarOKiDaVariedade(id);
		if (LerCor(cor, out Jandirus.Core.Appearance.Rgb c)) { pl.Visual.CorKi = c; ReapresentarAparencia(pl); }
		ReceitaDeProjetil raio = RaioDeTeste();
		raio.Nome = verbo.Replace('_', ' ');     // o grito e o da tecnica -- ver `EmbateDeCena_Disparar`
		Canalizar(pl, verbo, 10 * pl.Ficha.BaseDrain(), raio);
	}

	/// <summary>
	/// O PARCEIRO DE CENA ACERTA O PASSO DELE NA DANCA DA FUSAO -- pelo `TeclaDeQualquerEmbate`, o funil
	/// do pacote do jogador. Um corpo sem teclado responde pela agenda da maquina, que erra uma parte; e
	/// na danca um erro de QUALQUER um dos dois estraga a fusao. A primeira tomada filmou exatamente
	/// isso ("A COREOGRAFIA FALHOU"), que e do jogo -- mas a cena pedida e a danca que da certo. O
	/// parceiro ganha o teclado de mentira da bancada (deixa de responder sozinho) e a cena aperta a
	/// letra dele.
	/// </summary>
	/// <returns>verdadeiro se havia uma letra esperando por ele.</returns>
	internal bool PassoDaDancaNoTrailer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? p)) return false;
		_comTecladoDeTeste.Add(id);     // antes de a danca nascer: a primeira letra dele ja nao e da maquina
		if (!_dancando.TryGetValue(id, out DancaDeFusao? d)) return false;
		char letra = p == d.A ? d.LetraA : d.LetraB;
		if (letra == '\0') return false;
		TeclaDeQualquerEmbate(p, letra);
		return true;
	}

	/// <summary>
	/// O QUE ESTE ATOR SABE FAZER NA BRIGA: so o corpo (soco, esquiva, corrida), ou tudo -- o
	/// `PerfilDeCombate` que o molde sorteia no nascimento. Uma cena de "troca de golpes" pede dois
	/// lutadores sem ki: o cerebro de quem tem raio prefere o raio, e a luta vira um tiroteio.
	/// </summary>
	internal void PerfilNoTrailer(int id, bool soCorpo)
	{
		if (_players.TryGetValue(id, out ServerPlayer? npc))
			npc.Perfil = soCorpo ? Jandirus.Core.Ai.PerfilDeCombate.SoCorpo : Jandirus.Core.Ai.PerfilDeCombate.Completo;
	}
}
