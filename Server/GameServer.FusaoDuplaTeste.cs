using Godot;
using Jandirus.Core.Items;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ A FUSAO ENTRE **DOIS JOGADORES** (`--fusaoduplateste`) ============================
/// *"fusao entre jogadores nao se testa com um"* -- e as tres bancadas que ja existiam testavam com
/// zero, com um, ou com dois corpos que nunca se convidaram:
///
///   * `--diagfusaolook` (cliente) mede FUNCOES PURAS: o nome, a roupa, o cabelo, o vermelho do SSJ4,
///     os onze pontos da tabela de energia. Nada ali passa por um corpo;
///   * `--diagcenafusao` (cliente) mede o que a cena DESENHA;
///   * `--cenafusaoteste` (servidor) mede o INSTANTE da virada -- e chama o `ComecarACenaDaFusao`
///     **na mao**, pulando o convite, o aceite e a danca.
///
/// **Esta atravessa a corrente inteira, de ponta a ponta, com dois corpos no mundo**: o `Convidar` do
/// verb, o pendente na mesa do outro, o `ResponderAoConvite`, as letras do quick time event pelo mesmo
/// `TeclaDaDanca` que a tecla do jogador chama, a cena, a virada, o corpo fundido, e o `Separar`. Tudo
/// o que o dono listou -- os portoes, as duas metades da falha, a heranca, os nomes, a energia e as
/// bordas -- e medido NO CORPO depois de o caminho todo ter acontecido, e nao no valor de retorno de
/// uma funcao.
///
///     Godot --headless --path . --server --rede 7908 --fusaoduplateste
///
/// ============================ E CADA AFIRMACAO CENTRAL LEVA UM DEFEITO INJETADO ============================
/// Pelo <see cref="Mutacao"/>, o mesmo helper da `--provateste`: mede o criterio, ESTRAGA o mundo, exige
/// que o MESMO criterio reprove, desfaz e exige que ele volte a passar. Uma checagem que so foi vista
/// passando e indistinguivel de `Checa("...", true)` -- e este projeto ja catalogou quatro vezes o
/// custo de nao fazer isso ("a bancada mede INTENCAO").
///
/// Os defeitos sao TODOS pelo DADO (a raca do convidado, o BP dele, a skill no livro, a distancia, a
/// forma que acelera o dreno, o bit de estragada, o item na mochila). Nenhum troca codigo por copia de
/// codigo: o criterio e o mesmo objeto nas tres passadas, que e a regra do `Mutacao`.
///
/// ============================ O QUE ELA NAO PODE AFIRMAR ============================
/// Os corpos sao forjados (`new ServerPlayer` com BP escrito a mao, como a `--mestreteste` e a
/// `--cenafusaoteste`), entao ela nao afirma nada sobre valores derivados do NASCIMENTO -- classe,
/// limiar, `relBPmax`, genetica. Ela nao precisa: o que ela mede sao portoes, heranca e relogios, e os
/// tres independem de como o corpo veio ao mundo.
///
/// E ela nao ve PIXEL nenhum. A roupa e o cabelo da fusao sao medidos como CAMPO (o `LookDeFusao` que
/// o `PeerLook` carrega); quem olha o desenho e a `ver-a-fusao.bat` (`--diagfotofusao`), que fotografa
/// a metamoro e a potara lado a lado e o SSJ4 vermelho.
///
/// ============================ O RELOGIO E ADIANTADO, E NAO ESPERADO ============================
/// Mesma disciplina da `--cenafusaoteste`: os instantes (`c.Funde`, `d.Prazo`, `f.UltimoDreno`, o `Ate`
/// do convite) sao empurrados pra tras e os tiques sao chamados a mao. Uma bancada que ESPERASSE a
/// energia de uma Danca acabar levaria quinze minutos -- e mediria o relogio de parede em vez da regra.
/// Nao ha atalho no meio: o `Fundir` continua saindo do mesmo `if (agora >= c.Funde)` do jogo, e as
/// letras continuam saindo do mesmo `TeclaDaDanca`.
/// ==============================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- acima da `--cenafusaoteste`, que era a maior (92.000).</summary>
	private const int IdBaseDaFusaoDupla = 93_000;

	private int _fdOk, _fdFalhou, _fdSerie;
	private readonly List<string> _fdFalhas = [];

	private void AfirmarFd(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _fdOk++; GD.Print($"[fusao2]   OK    {oque}"); return; }
		_fdFalhou++;
		_fdFalhas.Add(oque);
		GD.Print($"[fusao2]  FALHOU  {oque}{(detalhe.Length > 0 ? $"  ({detalhe})" : "")}");
	}

	// =====================================================================
	// O PALCO
	// =====================================================================
	private ZoneKey _fdZona;
	private readonly List<ServerPlayer> _fdForjados = [];

	/// <summary>
	/// UM CORPO NO MUNDO, com raca, poder e livro proprios.
	///
	/// ============================ O TECLADO DE MENTIRA E OBRIGATORIO AQUI ============================
	/// `TemTeclado` (`GameServer.EmbateDeKi.cs:153`) le `p.Peer != null || _comTecladoDeTeste`. Sem a
	/// segunda metade, um corpo forjado cai na resposta automatica (`ResponderPelaMaquinaNaDanca`) e a
	/// danca vira um SORTEIO -- a bancada mediria a inteligencia do cerebro em vez de medir a regra
	/// "os dois acertando funde inteiro, um errando estraga". Com ele, quem aperta as letras sou eu.
	///
	/// ============================ E O `expressedBP` E ESCRITO DEPOIS DO `PorNoMundo` ============================
	/// O portao de proximidade compara BP EXPRESSO (`PoderPraComparar`), e o `PorNoMundo` roda o
	/// `PowerLevel`, que escreve aquele campo. Escrito antes, o valor seria sobrescrito e a razao que
	/// esta bancada quer testar (0,60 aceita, 0,10 recusa) viraria a razao que o motor de poder
	/// calculou -- ou seja, a bancada estaria medindo outra coisa e passando verde.
	/// </summary>
	private ServerPlayer ForjarNaFusaoDupla(string nome, string raca, double bp, bool sabeDancar,
											float tileX)
	{
		int id = IdBaseDaFusaoDupla + (++_fdSerie);
		var novo = new ServerPlayer
		{
			Id = id,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = _fdZona,
			Pos = new Vec2(tileX * ZoneCollision.TileSize, 0),
			Conta = $"bancada_fusao2_{id}",
			Slot = 0,
			Ficha = new Jandirus.Core.Stats.Fighter { Race = raca, BP = bp },

			// O LIVRO VAZIO NAO E ENFEITE: o `Fundir` empresta as skills do passageiro e chama
			// `AplicarPoderes`, que varre `pl.Livro.Aprendidas` SEM perguntar por nulo.
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		if (sabeDancar) novo.Livro.Dar(PathDaDanca);

		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.expressedBP = bp;   // ver o <summary>

		_comTecladoDeTeste.Add(id);
		_fdForjados.Add(novo);
		return novo;
	}

	// =====================================================================
	// A CORRENTE INTEIRA, NUMA CHAMADA
	// =====================================================================
	/// <summary>
	/// CONVIDA, ACEITA, DANCA E FUNDE -- **tudo pelos funis de producao**, sem um unico atalho.
	///
	/// O que a bancada faz que o jogo nao faz e adiantar RELOGIO (o prazo da letra e o instante da
	/// virada). Nada mais: o convite sai do <see cref="Convidar"/>, o sim sai do
	/// <see cref="ResponderAoConvite"/>, cada letra sai do <see cref="TeclaDaDanca"/> (o mesmo metodo
	/// que o pacote `C2S.ClashTecla` do jogador chama) e a fusao sai do `if (agora >= c.Funde)` do
	/// <see cref="TickDaCenaDeFusao"/>.
	/// </summary>
	/// <param name="aAcerta">Falso = quem convidou erra o primeiro passo (fusao estragada).</param>
	/// <param name="bAcerta">Falso = quem aceitou erra o primeiro passo (fusao estragada).</param>
	private FusaoAtiva? FundirDeVerdade(ServerPlayer a, ServerPlayer b, TipoDeFusao tipo,
										bool aAcerta = true, bool bAcerta = true)
	{
		Convidar(a, b, tipo);
		if (!_pedidosDeFusao.ContainsKey(b.Id)) return null;

		ResponderAoConvite(b, aceitou: true);

		if (_dancando.TryGetValue(a.Id, out DancaDeFusao? d)) DancarInteiro(d, aAcerta, bAcerta);

		// A CENA: adianta a virada e depois o fim. Os dois tiques sao o do jogo.
		if (_emCenaDeFusao.GetValueOrDefault(a.Id) is { } c)
		{
			c.Funde = NowMs() - 1;
			TickDaCenaDeFusao();
			c.Acaba = NowMs() - 1;
			TickDaCenaDeFusao();
		}

		return FusaoDe(a.Id);
	}

	/// <summary>
	/// APERTA AS LETRAS DOS DOIS LADOS ATE A DANCA FECHAR.
	///
	/// ============================ POR QUE O PRAZO E EMPURRADO ENTRE UMA LETRA E OUTRA ============================
	/// Acertar ADIANTA a proxima letra ate o piso de cadencia (`MsMinimoEntreLetras`, 300 ms) -- e o
	/// adiantamento que o motor de embate ja tem e que a danca herdou. Numa bancada sincrona ninguem
	/// espera 300 ms de relogio de parede tres vezes por lado: o prazo e posto no passado e o
	/// <see cref="TickDasLetrasDaDanca"/> -- **o do jogo** -- entrega a letra seguinte.
	///
	/// ERRAR FECHA AQUELE LADO NA HORA (`FalhouA`/`FalhouB`), entao um erro no primeiro passo e tudo o
	/// que a bancada precisa mandar: o resto da coreografia daquele lado nao existe mais.
	/// ========================================================================================================
	/// </summary>
	private void DancarInteiro(DancaDeFusao d, bool aAcerta, bool bAcerta)
	{
		if (!aAcerta && d.LetraA != '\0') TeclaDaDanca(d.A, OutraLetra(d.LetraA));
		if (!bAcerta && d.LetraB != '\0') TeclaDaDanca(d.B, OutraLetra(d.LetraB));

		// A GUARDA E DE SEGURANCA E NAO DE RITMO: sao 3 letras por lado, entao 20 voltas sobram. Sem
		// ela, um dia em que a danca deixasse de fechar penduraria o servidor da bancada em silencio.
		for (int volta = 0; volta < 20 && !d.Resolvida; volta++)
		{
			bool andou = false;
			andou |= UmPassoDaDanca(d, ehA: true);
			andou |= UmPassoDaDanca(d, ehA: false);
			if (!andou) break;
		}
	}

	/// <summary>Um passo de um dos dois lados: pede a letra que falta, ou acerta a que esta na tela.</summary>
	private bool UmPassoDaDanca(DancaDeFusao d, bool ehA)
	{
		if (ehA ? d.FalhouA : d.FalhouB) return false;
		if ((ehA ? d.AcertosA : d.AcertosB) >= Fusao.LetrasDaDanca) return false;

		ServerPlayer p = ehA ? d.A : d.B;
		char letra = ehA ? d.LetraA : d.LetraB;

		if (letra == '\0')
		{
			if (ehA) d.PrazoA = NowMs() - 1; else d.PrazoB = NowMs() - 1;
			TickDasLetrasDaDanca();   // o tique do jogo entrega a proxima letra
			return true;
		}

		TeclaDaDanca(p, letra);
		return true;
	}

	/// <summary>Qualquer letra que NAO seja a esperada -- e o unico jeito de errar de proposito.</summary>
	private static char OutraLetra(char esperada) => esperada == 'A' ? 'B' : 'A';

	// =====================================================================
	// O PONTO DE ENTRADA
	// =====================================================================
	public void RodarBancadaDaFusaoDupla()
	{
		_fdOk = _fdFalhou = _fdSerie = 0;
		_fdFalhas.Clear();
		GD.Print("[fusao2] ============ A FUSAO ENTRE DOIS JOGADORES, DE PONTA A PONTA ============");

		// UMA ZONA SEM NINGUEM CONECTADO, pela mesma razao da `--cenafusaoteste`: a cena manda pacote
		// pra `ZoneList` inteira, e bancada nao dispara cinematica na tela de quem esta jogando.
		_fdZona = ZoneKey.Premade(
			Jandirus.Core.World.Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		List<string>? escutaAntes = EscutaDeAvisos;
		EscutaDeAvisos = [];

		try
		{
			OsPortoesDaMetamoro();
			APotaraNaoTemOsPortoes();
			OsBrincosSaoOBilhete();
			ADancaBemFeita();
			ADancaMalFeita();
			AHeranca();
			OsNomes();
			AEnergiaViva();
			AsBordas();
			AFusaoNamekuseijin();
			OCorpoInteiroAoFundir();
			ARecargaAtravessaOLogout();
			OPuxaoDaPotara();
		}
		catch (Exception e)
		{
			AfirmarFd($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			// TUDO O QUE ELA POS NO MUNDO SAI, na ordem que nao deixa lixo: cenas, dancas, fusoes,
			// convites, recargas, corpos. Uma fusao viva com um corpo fora do `_players` faria o tique
			// de producao acusar "um dos dois deixou o mundo" a cada segundo depois da bancada.
			// O PUXAO SAI PRIMEIRO, porque ele e a fase mais antiga da corrente: um puxao vivo com um
			// corpo fora do `_players` faria o tique de producao acusar "um dos dois saiu do mundo" e
			// tentar avisar um jogador que nao existe mais.
			foreach (PuxaoDeFusao px in _puxoesDeFusao.ToList()) SoltarDoPuxaoDeFusao(px);
			foreach (CenaDeFusao c in _cenasDeFusao.ToList()) SoltarDaCenaDeFusao(c);
			foreach (DancaDeFusao d in _dancas.ToList()) AbortarADanca(d, "fim da bancada");
			foreach (FusaoAtiva f in _fusoes.ToList()) Separar(f, "fim da bancada");
			foreach (ServerPlayer p in _fdForjados)
			{
				_pedidosDeFusao.Remove(p.Id);
				// E A CONFIRMACAO PELA METADE DA ABSORCAO: ela e um carimbo por id, e id se reusa. Um
				// carimbo deixado pra tras poria quem entrasse depois a UM clique de perder o
				// personagem -- ver `_confirmacoesDeAbsorcao`.
				_confirmacoesDeAbsorcao.Remove(p.Id);
				// O CANAL DE "O SERVIDOR ESTA ME DIRIGINDO" TAMBEM E LIMPO: ele e um PRAZO regado por
				// tique, e um corpo de bancada sai do mundo sem ninguem pra escorre-lo.
				p.PuxaoDeFusaoRestante = 0;
				p.Ficha.fusion_cooldown_until = 0;
				_comTecladoDeTeste.Remove(p.Id);
				_players.Remove(p.Id);
				ZoneList(_fdZona.Hash).Remove(p);
				ZoneList(ZonaDoSelo(p.Id).Hash).Remove(p);
			}
			_fdForjados.Clear();
			EscutaDeAvisos = escutaAntes;
		}

		GD.Print($"[fusao2] ============ {_fdOk} passaram, {_fdFalhou} falharam ============");
		if (_fdFalhou > 0)
			foreach (string f in _fdFalhas) GD.Print($"[fusao2]   -> {f}");
	}

	// =====================================================================
	// A. OS PORTOES DA METAMORO -- raca, poder e a skill NOS DOIS
	// =====================================================================
	/// <summary>
	/// Os tres portoes que **so a Danca tem**, medidos pelo <see cref="Convidar"/> de verdade: o que a
	/// bancada le e se o pendente FICOU NA MESA do outro, que e a unica coisa que o convite produz.
	/// </summary>
	private void OsPortoesDaMetamoro()
	{
		GD.Print("[fusao2] -- A) os portoes da Metamoro --");

		ServerPlayer goku = ForjarNaFusaoDupla("Goku", "Saiyan", 1_000_000, sabeDancar: true, 0);
		ServerPlayer vegeta = ForjarNaFusaoDupla("Vegeta", "Saiyan", 900_000, sabeDancar: true, 1);
		// ============================ TODOS NO TILE AO LADO DO GOKU, E ISSO PASSOU A IMPORTAR ============================
		// A Danca cobra `Fusao.TilesColados` (UM tile, o `get_dist > 1` do `Metamoran Fusion.dm:92`), e
		// nao mais os 4 de antes. Com o Gohan a 2 tiles e o Piccolo a 3, TODAS as provas de raca desta
		// secao passariam pelo motivo errado -- a recusa seria `Longe`, e a A5 (que exige a frase da
		// RACA) ficaria vermelha apontando um portao que nem chegou a ser consultado.
		// ==========================================================================================================
		ServerPlayer gohan = ForjarNaFusaoDupla("Gohan", "Halfbreed", 950_000, sabeDancar: true, 1);
		ServerPlayer piccolo = ForjarNaFusaoDupla("Piccolo", "Namekian", 950_000, sabeDancar: true, 1);
		ServerPlayer yamcha = ForjarNaFusaoDupla("Yamcha", "Human", 950_000, sabeDancar: false, 1);

		bool Convite(ServerPlayer a, ServerPlayer b)
		{
			_pedidosDeFusao.Remove(b.Id);
			Convidar(a, b, TipoDeFusao.Danca);
			bool entrou = _pedidosDeFusao.ContainsKey(b.Id);
			_pedidosDeFusao.Remove(b.Id);
			return entrou;
		}

		// ---- A MESMA RACA FUNCIONA ----
		Mutacao(AfirmarFd,
			"A1 Saiyajin convida Saiyajin: o convite ENTRA na mesa dele",
			"o convidado vira Namekuseijin",
			() => Convite(goku, vegeta),
			() => vegeta.Race = "Namekian",
			() => vegeta.Race = "Saiyan");

		// ---- E O MEIO-SAIYAJIN CONTA COMO SAIYAJIN ----
		// *"meio saiyajin com saiyajin puro ainda funciona"*, literal. A resposta e do
		// `Fusao.RaizDaRaca`, que e a mesma que o `Birth.cs:123` ja da pro CORPO do mestico.
		Mutacao(AfirmarFd,
			"A2 meio-Saiyajin (`Halfbreed`) convida Saiyajin puro: ENTRA",
			"o meio-Saiyajin vira Humano",
			() => Convite(gohan, goku),
			() => gohan.Race = "Human",
			() => gohan.Race = "Halfbreed");

		AfirmarFd("A3 ...e nos dois sentidos (puro convidando o mestico)", Convite(goku, gohan));

		// ---- RACAS DIFERENTES NAO ----
		AfirmarFd("A4 Saiyajin x Namekuseijin: o convite NAO entra", !Convite(goku, piccolo));

		EscutaDeAvisos?.Clear();
		Convidar(goku, piccolo, TipoDeFusao.Danca);
		AfirmarFd("A5 ...e a recusa DIZ que e a raca (o portao so existe nesta frase)",
				  EscutaDeAvisos?.Any(t => t.Contains("mesma raca", StringComparison.OrdinalIgnoreCase))
				  == true,
				  string.Join(" | ", EscutaDeAvisos ?? []));
		_pedidosDeFusao.Remove(piccolo.Id);

		// ---- O PODER PROXIMO ----
		// O 0,5 e do DM: `Fusion.dm:95`, o `PowerEqual` morto -- *"Below <0.5 will mean the fusion
		// itself will become botched"*. Ver `Fusao.LimiarDeProximidade`.
		Mutacao(AfirmarFd,
			"A6 BP expresso PROXIMO (0,90 do maior): o convite entra",
			"o convidado passa a expressar 10% do outro (razao 0,10)",
			() => Convite(goku, vegeta),
			() => vegeta.Ficha.expressedBP = goku.Ficha.expressedBP * 0.10,
			() => vegeta.Ficha.expressedBP = 900_000);

		AfirmarFd("A7 na fronteira exata (0,50) ainda entra -- o limiar e `< 0,5` e nao `<=`",
				  RodadaDePoder(goku, vegeta, 0.50));
		AfirmarFd("A8 ...e logo abaixo dela (0,49) NAO entra", !RodadaDePoder(goku, vegeta, 0.49));

		EscutaDeAvisos?.Clear();
		vegeta.Ficha.expressedBP = goku.Ficha.expressedBP * 0.10;
		Convidar(goku, vegeta, TipoDeFusao.Danca);
		AfirmarFd("A9 ...e a recusa DIZ a porcentagem que falta",
				  EscutaDeAvisos?.Any(t => t.Contains("mais fraco precisa expressar",
													  StringComparison.OrdinalIgnoreCase)) == true,
				  string.Join(" | ", EscutaDeAvisos ?? []));
		_pedidosDeFusao.Remove(vegeta.Id);
		vegeta.Ficha.expressedBP = 900_000;

		// ---- A SKILL NOS DOIS ----
		// Pedido do dono, e ele DIVERGE do DM de olho aberto: la o verb mora no `obj` que a skill
		// concede (`SpaceRankSkills.dm:146`), entao so o convidador precisa saber.
		Mutacao(AfirmarFd,
			"A10 com a skill nos DOIS o convite entra",
			"o CONVIDADO esquece a Danca da Fusao",
			() => Convite(goku, vegeta),
			() => vegeta.Livro!.Esquecer(PathDaDanca),
			() => vegeta.Livro!.Dar(PathDaDanca));

		Mutacao(AfirmarFd,
			"A11 ...e o mesmo vale pro CONVIDADOR",
			"quem convida esquece a Danca da Fusao",
			() => Convite(goku, vegeta),
			() => goku.Livro!.Esquecer(PathDaDanca),
			() => goku.Livro!.Dar(PathDaDanca));

		AfirmarFd("A12 quem nunca soube dancar nao recebe convite", !Convite(goku, yamcha));

		// ============================ E A ORDEM DOS PORTOES E VISIVEL NA FRASE ============================
		// O Yamcha desta bancada e Humano **e** nao sabe dancar -- ou seja ele reprova em DOIS portoes ao
		// mesmo tempo, e so um deles vira mensagem. A ordem do `Fusao.Avaliar` poe a skill antes da raca,
		// entao o que ele ouve e "nao sabe a Danca da Fusao". Isto e afirmacao e nao curiosidade: a frase
		// e o UNICO lugar do jogo em que estes portoes existem pro jogador, e uma ordem trocada mandaria
		// alguem procurar um parceiro da propria raca quando o que falta e a skill.
		// ==============================================================================================
		EscutaDeAvisos?.Clear();
		Convidar(goku, yamcha, TipoDeFusao.Danca);
		AfirmarFd("A13 ...e a recusa nomeia a SKILL, que e o portao que vem primeiro",
				  EscutaDeAvisos?.Any(t => t.Contains("nao sabe a Danca da Fusao",
													  StringComparison.OrdinalIgnoreCase)) == true,
				  string.Join(" | ", EscutaDeAvisos ?? []));
		_pedidosDeFusao.Remove(yamcha.Id);

		// ---- A DISTANCIA ----
		Mutacao(AfirmarFd,
			"A14 colado (1 tile) o convite entra",
			$"o convidado anda pra {Fusao.TilesColados + 3} tiles de distancia",
			() => Convite(goku, vegeta),
			() => vegeta.Pos = new Vec2((Fusao.TilesColados + 3) * ZoneCollision.TileSize, 0),
			() => vegeta.Pos = new Vec2(ZoneCollision.TileSize, 0));

		// ---- A ZONA ----
		Mutacao(AfirmarFd,
			"A15 no mesmo lugar o convite entra",
			"o convidado troca de planeta",
			() => Convite(goku, vegeta),
			() => vegeta.Zone = ZoneKey.Interior("BancadaDaFusaoDupla", 999_998),
			() => vegeta.Zone = _fdZona);

		// ---- E NINGUEM FICA MARCADO POR UM CONVITE RECUSADO ----
		AfirmarFd("A16 depois de tudo isso ninguem esta ocupado por fusao nenhuma",
				  !OcupadoPorFusao(goku.Id) && !OcupadoPorFusao(vegeta.Id)
				  && !OcupadoPorFusao(gohan.Id) && !OcupadoPorFusao(piccolo.Id));
	}

	/// <summary>Uma rodada de convite com a razao de poder exata pedida. Devolve se o convite entrou.</summary>
	private bool RodadaDePoder(ServerPlayer a, ServerPlayer b, double razao)
	{
		double antes = b.Ficha.expressedBP;
		b.Ficha.expressedBP = a.Ficha.expressedBP * razao;
		_pedidosDeFusao.Remove(b.Id);
		Convidar(a, b, TipoDeFusao.Danca);
		bool entrou = _pedidosDeFusao.ContainsKey(b.Id);
		_pedidosDeFusao.Remove(b.Id);
		b.Ficha.expressedBP = antes;
		return entrou;
	}

	// =====================================================================
	// B. A POTARA NAO TEM NENHUM DOS TRES PORTOES
	// =====================================================================
	/// <summary>
	/// *"POTARA: qualquer raca"*, sem QTE, sem skill. O que ela cobra e o ITEM -- ver
	/// <see cref="OsBrincosSaoOBilhete"/>.
	///
	/// **A prova aqui e o CONTRASTE**: os mesmos dois corpos, o mesmo instante, e a Danca recusando
	/// enquanto a Potara aceita. Duas afirmacoes separadas ("a Potara aceita" numa secao e "a Danca
	/// recusa" noutra) passariam verdes com os dois portoes desligados.
	/// </summary>
	private void APotaraNaoTemOsPortoes()
	{
		GD.Print("[fusao2] -- B) a Potara nao tem os portoes da Danca --");

		ServerPlayer kaio = ForjarNaFusaoDupla("Kaioshin", "Kai", 5_000_000, sabeDancar: false, 0);
		ServerPlayer krilin = ForjarNaFusaoDupla("Kuririn", "Human", 200_000, sabeDancar: false, 1);

		bool Convite(TipoDeFusao t)
		{
			_pedidosDeFusao.Remove(krilin.Id);
			Convidar(kaio, krilin, t);
			bool entrou = _pedidosDeFusao.ContainsKey(krilin.Id);
			_pedidosDeFusao.Remove(krilin.Id);
			return entrou;
		}

		// Kai x Humano, razao de poder 0,04, e nenhum dos dois sabe dancar: os TRES portoes reprovam.
		AfirmarFd("B1 a DANCA recusa este par (raca diferente, poder distante, ninguem sabe dancar)",
				  !Convite(TipoDeFusao.Danca));
		AfirmarFd("B2 ...e a POTARA aceita o MESMO par, no MESMO instante",
				  Convite(TipoDeFusao.Potara));

		// E ela funde SEM danca nenhuma -- *"aceitando funde na hora, SEM QTE"*.
		int dancasAntes = _dancas.Count;
		FusaoAtiva? f = FundirDeVerdade(kaio, krilin, TipoDeFusao.Potara);

		AfirmarFd("B3 a Potara FUNDE entre racas diferentes", f != null && EstaFundido(kaio.Id));
		AfirmarFd("B4 ...e nenhuma danca nasceu no caminho (sem QTE)",
				  _dancas.Count == dancasAntes, $"{dancasAntes} -> {_dancas.Count}");
		AfirmarFd("B5 ...e ela nao nasce estragada (nao ha coreografia pra errar)",
				  f is { Estragada: false } && AscendePorDecisao(kaio, avisar: false));
		AfirmarFd("B6 ...e a energia dela e a da Potara (1800), e nao a da Danca",
				  f != null && Math.Abs(f.EnergiaMax - Fusao.EnergiaDaPotara) < 0.5,
				  $"{f?.EnergiaMax:0}");

		if (f != null) Separar(f, "fim da secao B");
	}

	// =====================================================================
	// C. OS BRINCOS -- o que a Potara cobra no lugar da coreografia
	// =====================================================================
	/// <summary>
	/// *"precisa dos brincos Potara"*, e o clique neles e que oferece. O caminho medido e o de
	/// producao inteiro: <see cref="ComandoDeItem"/> com `item_jogar`, que e o pacote que o botao do
	/// inventario manda.
	/// </summary>
	private void OsBrincosSaoOBilhete()
	{
		GD.Print("[fusao2] -- C) os brincos Potara --");

		ServerPlayer dono = ForjarNaFusaoDupla("Shin", "Kai", 3_000_000, sabeDancar: false, 0);
		ServerPlayer alvo = ForjarNaFusaoDupla("Kibito", "Kai", 2_900_000, sabeDancar: false, 1);
		dono.AlvoId = alvo.Id;   // o duplo clique -- *"jogar pro alvo atual"*

		bool Clicar()
		{
			_pedidosDeFusao.Remove(alvo.Id);
			ComandoDeItem(dono, "item_jogar", CatalogoDeItens.BrincosPotara);
			bool entrou = _pedidosDeFusao.ContainsKey(alvo.Id);
			_pedidosDeFusao.Remove(alvo.Id);
			return entrou;
		}

		AfirmarFd("C1 SEM os brincos na mochila, o clique nao oferece nada", !Clicar());

		dono.Mochila.Guardar(CatalogoDeItens.BrincosPotara);

		// O DEFEITO E O ITEM SUMINDO DA MOCHILA -- e o criterio (o clique de producao) e o MESMO objeto
		// nas tres passadas, que e a regra do `Mutacao`. A primeira versao desta chamada punha e tirava
		// os brincos DENTRO do criterio e deixava o `estragar` vazio: a passada do defeito media
		// exatamente a mesma coisa que a primeira, e ficou vermelha -- o helper acusando a bancada.
		Mutacao(AfirmarFd,
			"C2 COM os brincos, o clique poe o convite na mesa do alvo marcado",
			"os brincos saem da mochila",
			Clicar,
			() => dono.Mochila.Tirar(CatalogoDeItens.BrincosPotara,
									 dono.Mochila.Quantos(CatalogoDeItens.BrincosPotara)),
			() => dono.Mochila.Guardar(CatalogoDeItens.BrincosPotara));

		// ---- E SEM ALVO MARCADO ELE NAO ADIVINHA ----
		int alvoAntes = dono.AlvoId;
		dono.AlvoId = 0;
		AfirmarFd("C3 sem alvo marcado o brinco nao escolhe ninguem por conta propria", !Clicar());
		dono.AlvoId = alvoAntes;

		AfirmarFd("C4 ...e com o alvo de volta ele oferece de novo", Clicar());

		// ---- A INSIGNIA E DO CARGO, E NAO LOOT ----
		// `ReconciliarOsBrincos` e idempotente e derivado do cargo -- e por isso ele cobre o caso
		// "perdeu o cargo offline", que evento nenhum cobre.
		dono.Mochila.Tirar(CatalogoDeItens.BrincosPotara,
						   dono.Mochila.Quantos(CatalogoDeItens.BrincosPotara));
		ReconciliarOsBrincos(dono);
		AfirmarFd("C5 quem nao e Kaioshin nao ganha brincos na reconciliacao",
				  dono.Mochila.Quantos(CatalogoDeItens.BrincosPotara) == 0);
	}

	// =====================================================================
	// D. A DANCA BEM FEITA -- e a heranca inteira
	// =====================================================================
	/// <summary>
	/// OS DOIS ACERTANDO. Aqui mora o grosso do pedido: BP `(A+B)x2`, stats o maior de cada (dois
	/// campos nomeados), skills dos dois, e o corpo que se transforma.
	/// </summary>
	private void ADancaBemFeita()
	{
		GD.Print("[fusao2] -- D) os dois acertando: a fusao inteira --");

		ServerPlayer a = ForjarNaFusaoDupla("Goku", "Saiyan", 1_000_000, sabeDancar: true, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Vegeta", "Saiyan", 800_000, sabeDancar: true, 1);

		// OS DOIS CAMPOS QUE O DONO NOMEOU: *"se jogador 1 tem 30 de physical e o 2 tem 40, a fusao
		// tem 40"*. `physoff` e o "physical"; `speed` e o segundo, e ele esta CRUZADO de proposito --
		// com os dois maiores no mesmo corpo, "o maior de cada" e "copiar o do dono" dariam o mesmo
		// numero e a bancada passaria verde com a regra errada.
		a.Ficha.physoff = 30; b.Ficha.physoff = 40;
		a.Ficha.speed = 55; b.Ficha.speed = 20;

		a.Livro!.Dar("/datum/skill/bancada/so_do_A");
		b.Livro!.Dar("/datum/skill/bancada/so_do_B");

		double bpA = a.Ficha.BP, bpB = b.Ficha.BP;
		double buffAntes = a.Ficha.FuseBuff;

		FusaoAtiva? f = FundirDeVerdade(a, b, TipoDeFusao.Danca);
		AfirmarFd("D1 os dois acertando as tres letras, a fusao acontece",
				  f != null && EstaFundido(a.Id) && EstaFundido(b.Id));
		if (f == null) return;

		AfirmarFd("D2 ...e ela NAO sai estragada", !f.Estragada);

		// ---- O PODER: (A+B)x2 ----
		double esperado = Fusao.BpDaFusao(bpA, bpB);
		AfirmarFd("D3 BP base = (A+B)x2, somado no `FuseBuff`",
				  Math.Abs(a.Ficha.BP + a.Ficha.FuseBuff - buffAntes - esperado) < 1.0,
				  $"{a.Ficha.BP + a.Ficha.FuseBuff:N0} vs {esperado:N0}");

		AfirmarFd("D4 ...e ela e MAIS FORTE que os dois separados",
				  esperado > bpA && esperado > bpB);

		// ---- OS STATS: O MAIOR DE CADA ----
		AfirmarFd("D5 stat `physoff`: o maior dos dois (30 x 40 -> 40)",
				  Math.Abs(a.Ficha.physoff - 40) < 1e-9, $"{a.Ficha.physoff}");
		AfirmarFd("D6 stat `speed`: o maior dos dois no sentido CONTRARIO (55 x 20 -> 55)",
				  Math.Abs(a.Ficha.speed - 55) < 1e-9, $"{a.Ficha.speed}");

		// ---- AS SKILLS DOS DOIS ----
		AfirmarFd("D7 a fusao sabe a skill que so o convidador tinha",
				  a.Livro!.Sabe("/datum/skill/bancada/so_do_A"));
		AfirmarFd("D8 ...e a que so o convidado tinha", a.Livro!.Sabe("/datum/skill/bancada/so_do_B"));

		// ---- E ELA SE TRANSFORMA ----
		Mutacao(AfirmarFd,
			"D9 a fusao BEM FEITA se transforma (`AscendePorDecisao`)",
			"o bit de estragada e ligado nela",
			() => AscendePorDecisao(a, avisar: false),
			() => f.Estragada = true,
			() => f.Estragada = false);

		// ---- E O NOME ----
		AfirmarFd("D10 o corpo passa a se chamar pela fusao", a.NomeDeFusao.Length > 0, a.NomeDeFusao);
		AfirmarFd("D11 ...e o passageiro esta selado", EhOSelo(b.Zone), b.Zone.Name);

		// ---- E O `Separar` DEVOLVE EXATAMENTE O QUE PEGOU ----
		Separar(f, "fim da secao D");

		AfirmarFd("D12 desfeita, o `FuseBuff` volta ao que era",
				  Math.Abs(a.Ficha.FuseBuff - buffAntes) < 1e-9, $"{a.Ficha.FuseBuff}");
		AfirmarFd("D13 ...os stats voltam aos dele (30 e 55, e nao os emprestados)",
				  Math.Abs(a.Ficha.physoff - 30) < 1e-9 && Math.Abs(a.Ficha.speed - 55) < 1e-9,
				  $"{a.Ficha.physoff} / {a.Ficha.speed}");
		AfirmarFd("D14 ...a skill EMPRESTADA sai", !a.Livro!.Sabe("/datum/skill/bancada/so_do_B"));
		AfirmarFd("D15 ...e a que era DELE fica (o emprestimo devolve so o que pegou)",
				  a.Livro!.Sabe("/datum/skill/bancada/so_do_A"));
		AfirmarFd("D16 ...o passageiro sai do selo", !EhOSelo(b.Zone), b.Zone.Name);
		AfirmarFd("D17 ...e o nome dele volta a ser o dele", a.NomeDeFusao.Length == 0);
	}

	// =====================================================================
	// E. A DANCA MAL FEITA -- **as duas metades**
	// =====================================================================
	/// <summary>
	/// *"Falhando -> a fusao ACONTECE mas fica EXTREMAMENTE FRACA -- mais fraca que os personagens
	/// separados, e ela NEM SE TRANSFORMA"*.
	///
	/// **AS DUAS METADES**, e o dono pediu as duas com todas as letras: quem erra pode ser o
	/// convidador ou o convidado, e o resultado tem que ser o mesmo. Uma so passaria verde com a
	/// bancada lendo o placar de um lado apenas.
	/// </summary>
	private void ADancaMalFeita()
	{
		GD.Print("[fusao2] -- E) um dos dois errando: a fusao estragada --");

		void UmaMetade(string quem, bool aAcerta, bool bAcerta)
		{
			ServerPlayer a = ForjarNaFusaoDupla($"Goten ({quem})", "Saiyan", 1_000_000, true, 0);
			ServerPlayer b = ForjarNaFusaoDupla($"Trunks ({quem})", "Saiyan", 800_000, true, 1);
			double bpA = a.Ficha.BP, bpB = b.Ficha.BP;

			FusaoAtiva? f = FundirDeVerdade(a, b, TipoDeFusao.Danca, aAcerta, bAcerta);

			AfirmarFd($"E.{quem} a fusao ACONTECE mesmo assim (errar nao cancela nada)",
					  f != null && EstaFundido(a.Id));
			if (f == null) return;

			AfirmarFd($"E.{quem} ...e ela vem marcada como estragada", f.Estragada);

			double baseDaFusao = a.Ficha.BP + f.DeltaDeBp;
			AfirmarFd($"E.{quem} ...e e MAIS FRACA que os DOIS separados",
					  baseDaFusao < bpA && baseDaFusao < bpB,
					  $"{baseDaFusao:N0} contra {bpA:N0} e {bpB:N0}");

			AfirmarFd($"E.{quem} ...e ela NAO se transforma", !AscendePorDecisao(a, avisar: false));

			// DESCER CONTINUA LIVRE nao e medido aqui de proposito: o corte mora so no
			// `AscendePorDecisao`, que e o funil da SUBIDA (tecla C, botao de forma, admin). Nao ha
			// nada no caminho da descida pra ficar vermelho -- ver `GameServer.Npc.cs:451`.

			Separar(f, $"fim da metade {quem}");
			AfirmarFd($"E.{quem} ...e desfeita, o corpo volta a poder se transformar",
					  AscendePorDecisao(a, avisar: false));
		}

		UmaMetade("quem CONVIDOU errou", aAcerta: false, bAcerta: true);
		UmaMetade("quem ACEITOU errou", aAcerta: true, bAcerta: false);

		// ---- E O DEFEITO INJETADO: com os dois acertando, ela NAO sai estragada ----
		ServerPlayer x = ForjarNaFusaoDupla("Kakarotto", "Saiyan", 1_000_000, true, 0);
		ServerPlayer y = ForjarNaFusaoDupla("Bardock", "Saiyan", 900_000, true, 1);
		FusaoAtiva? boa = FundirDeVerdade(x, y, TipoDeFusao.Danca);
		AfirmarFd("E1 DEFEITO INJETADO (os dois acertam): a MESMA corrente NAO produz fusao estragada",
				  boa is { Estragada: false } && AscendePorDecisao(x, avisar: false));
		if (boa != null) Separar(boa, "fim da secao E");
	}

	// =====================================================================
	// F. A HERANCA -- raca, formas, e "nasce transformada"
	// =====================================================================
	/// <summary>
	/// *"a fusao e da RACA de quem convidou e tem as TRANSFORMACOES de quem convidou"* e *"se quem
	/// convidou estiver transformado, a fusao ja comeca transformada"*.
	///
	/// As duas coisas saem POR OMISSAO -- nada no `Fundir` escreve `Race` nem `ssjBuff` --, e e
	/// exatamente por isso que elas precisam de prova: uma regra que ninguem escreveu e uma regra que
	/// qualquer linha nova pode apagar sem querer.
	/// </summary>
	private void AHeranca()
	{
		GD.Print("[fusao2] -- F) a heranca: raca, forma e comecar transformada --");

		// POTARA de proposito: so ela permite racas diferentes, e sem raca diferente a afirmacao "a
		// fusao e da raca de quem convidou" nao tem como ficar vermelha.
		ServerPlayer a = ForjarNaFusaoDupla("Zarbon", "Icer", 4_000_000, sabeDancar: false, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Nail", "Namekian", 3_800_000, sabeDancar: false, 1);

		// QUEM CONVIDA ESTA TRANSFORMADO (4x), e quem aceita esta noutra forma (8x pelo `transBuff`).
		a.Ficha.ssjBuff = 4;
		b.Ficha.transBuff = 8;

		FusaoAtiva? f = FundirDeVerdade(a, b, TipoDeFusao.Potara);
		AfirmarFd("F1 a fusao aconteceu", f != null && EstaFundido(a.Id));
		if (f == null) return;

		AfirmarFd("F2 a fusao e da RACA de quem convidou", a.Race == "Icer", a.Race);

		Mutacao(AfirmarFd,
			"F3 quem convidou transformado -> a fusao JA COMECA transformada (mult 4x de pe)",
			"a forma de quem convidou e derrubada",
			() => Math.Abs(Fusao.MultiplicadorDaFormaAtual(a.Ficha) - 4) < 1e-9,
			() => a.Ficha.ssjBuff = 1,
			() => a.Ficha.ssjBuff = 4);

		AfirmarFd("F4 ...e a forma de QUEM ACEITOU nao vem junto (`transBuff` continua 1 no corpo)",
				  Math.Abs(a.Ficha.transBuff - 1) < 1e-9, $"{a.Ficha.transBuff}");

		// ---- QUEM CONVIDOU CONTROLA ----
		AfirmarFd("F5 quem CONVIDOU e quem controla (o outro e o passageiro)",
				  f.Dono == a && f.Passageiro == b);
		AfirmarFd("F6 ...e e o corpo dele que fica no mapa", !EhOSelo(a.Zone) && EhOSelo(b.Zone));

		// ============================ O PASS CONTROL FICA FORA DO ALCANCE DESTA BANCADA ============================
		// `PassarOControle` recusa quando o outro lado nao tem `Peer` -- e a recusa esta CERTA: entregar
		// o volante a quem nao esta na tela deixaria a fusao dirigindo sozinha ate a energia acabar. Os
		// corpos forjados aqui nao tem soquete, e nao ha `_comTecladoDeTeste` pra `Peer`: um bypass
		// existiria so pra bancada e mediria uma coisa que o jogo nao faz.
		//
		// Entao o que ela mede e a METADE que cabe: **a recusa existe e nao quebra a fusao**. Quem
		// exercita a troca de verdade e a foto (`--diagfotofusao`), onde o convidador e um cliente com
		// soquete -- e mesmo la o passageiro continua sendo forjado, entao a troca de mao com DOIS
		// soquetes de verdade continua sem cobertura. Anotado no veredito, e nao escondido.
		// ======================================================================================================
		double poderAntes = a.Ficha.BP + f.DeltaDeBp;
		PassarOControle(a);
		AfirmarFd("F7 o `Pass Control` RECUSA quando o outro lado nao esta disponivel pra assumir",
				  f.Dono == a && f.Passageiro == b);
		AfirmarFd("F8 ...e a recusa nao mexeu no poder da fusao (nada foi desfeito pela metade)",
				  Math.Abs(a.Ficha.BP + f.DeltaDeBp - poderAntes) < 1.0,
				  $"{a.Ficha.BP + f.DeltaDeBp:N0} vs {poderAntes:N0}");
		AfirmarFd("F9 ...e o nome dela continua no corpo que dirige",
				  a.NomeDeFusao == f.NomeDaFusao && b.NomeDeFusao.Length == 0, a.NomeDeFusao);

		Separar(f, "fim da secao F");
		AfirmarFd("F10 desfeita, ninguem ficou selado", !EhOSelo(a.Zone) && !EhOSelo(b.Zone));
	}

	// =====================================================================
	// G. OS NOMES -- diferentes por tipo, iguais na repeticao
	// =====================================================================
	/// <summary>
	/// *"Potara e Metamoro dos MESMOS jogadores tem nomes DIFERENTES"*.
	///
	/// **MEDIDO NO CORPO** (`ServerPlayer.NomeDeFusao`) e nao na funcao pura -- a funcao ja e medida
	/// pela `--diagfusaolook`. O que esta secao acrescenta e que o nome CHEGA no corpo e que ele nao
	/// depende de sorteio nenhum.
	///
	/// ============================ A RECARGA DE 1 H E LIMPADA ENTRE AS RODADAS ============================
	/// E o unico lugar da bancada em que ela mexe num relogio do jogo por conveniencia, e esta escrito
	/// por isso: a mesma dupla precisa fundir tres vezes pra a comparacao existir, e o `Separar` cobra
	/// 1 h de cada um. Esperar seria esperar tres horas. A recarga em si e medida na secao I.
	/// ================================================================================================
	/// </summary>
	private void OsNomes()
	{
		GD.Print("[fusao2] -- G) os nomes das duas fusoes --");

		ServerPlayer a = ForjarNaFusaoDupla("Goku", "Saiyan", 1_000_000, sabeDancar: true, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Vegeta", "Saiyan", 900_000, sabeDancar: true, 1);

		string UmaFusao(TipoDeFusao t)
		{
			a.Ficha.fusion_cooldown_until = 0;
			b.Ficha.fusion_cooldown_until = 0;
			FusaoAtiva? f = FundirDeVerdade(a, b, t);
			string nome = a.NomeDeFusao;
			if (f != null) Separar(f, "fim da rodada de nome");
			return nome;
		}

		string metamoro = UmaFusao(TipoDeFusao.Danca);
		string potara = UmaFusao(TipoDeFusao.Potara);
		string metamoroDeNovo = UmaFusao(TipoDeFusao.Danca);

		AfirmarFd("G1 a Metamoro tem nome", metamoro.Length > 0, metamoro);
		AfirmarFd("G2 a Potara tem nome", potara.Length > 0, potara);
		AfirmarFd("G3 os DOIS nomes sao DIFERENTES pra mesma dupla",
				  !string.Equals(metamoro, potara, StringComparison.Ordinal),
				  $"'{metamoro}' x '{potara}'");
		AfirmarFd("G4 ...e a repeticao da o MESMO nome (nao ha sorteio no caminho)",
				  string.Equals(metamoro, metamoroDeNovo, StringComparison.Ordinal),
				  $"'{metamoro}' x '{metamoroDeNovo}'");

		// TROCAR QUEM CONVIDA TROCA O NOME -- e a metade que prova que o nome depende de QUEM
		// convidou, e nao so do tipo.
		a.Ficha.fusion_cooldown_until = 0;
		b.Ficha.fusion_cooldown_until = 0;
		FusaoAtiva? invertida = FundirDeVerdade(b, a, TipoDeFusao.Danca);
		string aoContrario = b.NomeDeFusao;
		if (invertida != null) Separar(invertida, "fim da secao G");

		AfirmarFd("G5 trocar QUEM CONVIDA troca o nome da Metamoro",
				  !string.Equals(metamoro, aoContrario, StringComparison.Ordinal),
				  $"'{metamoro}' x '{aoContrario}'");
	}

	// =====================================================================
	// H. A ENERGIA -- medida no corpo fundido, e nao na formula
	// =====================================================================
	/// <summary>
	/// A tabela do dono, atravessada por um corpo de verdade: **1/s na base, 2,0/s numa forma de 50x**,
	/// e a fusao acabando no tempo que a tabela promete.
	///
	/// A formula pura (`1 + mult/50`) ja e medida nos onze pontos pela `--diagfusaolook`. O que esta
	/// secao acrescenta e o CANO: que o `TickDaFusao` le a forma do corpo que esta no controle, que ele
	/// desconta por tempo REAL (e nao por tique) e que, quando a energia zera, os dois se separam
	/// sozinhos.
	/// </summary>
	private void AEnergiaViva()
	{
		GD.Print("[fusao2] -- H) a energia, drenada num corpo de verdade --");

		ServerPlayer a = ForjarNaFusaoDupla("Gogeta", "Saiyan", 1_000_000, sabeDancar: true, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Vegetto", "Saiyan", 900_000, sabeDancar: true, 1);

		FusaoAtiva? f = FundirDeVerdade(a, b, TipoDeFusao.Danca);
		AfirmarFd("H1 a fusao (Danca) nasce com 900 de energia",
				  f != null && Math.Abs(f.Energia - Fusao.EnergiaDaDanca) < 1.0, $"{f?.Energia:0.##}");
		if (f == null) return;

		// ---- 1/s NA BASE ----
		// O relogio e empurrado dez segundos pra tras e o tique de producao cobra a diferenca.
		double antes = f.Energia;
		f.UltimoDreno = NowMs() - 10_000;
		TickDaFusao();
		double gasto = antes - f.Energia;

		Mutacao(AfirmarFd,
			"H2 sem forma nenhuma o dreno e 1/s (10 s -> 10 de energia)",
			"o corpo entra numa forma de 50x",
			() => Math.Abs(DrenoEmDezSegundos(f) - 10) < 0.2,
			() => a.Ficha.ssjBuff = 50,
			() => a.Ficha.ssjBuff = 1);

		AfirmarFd("H2b (a primeira medida, fora do `Mutacao`, deu o mesmo)",
				  Math.Abs(gasto - 10) < 0.2, $"{gasto:0.###}");

		// ---- 2,0/s NUMA FORMA DE 50x ----
		a.Ficha.ssjBuff = 50;
		AfirmarFd("H3 numa forma de 50x o dreno dobra: 2,0/s (10 s -> 20 de energia)",
				  Math.Abs(DrenoEmDezSegundos(f) - 20) < 0.2);
		AfirmarFd("H4 ...e o multiplicador que o tique LE do corpo e mesmo 50",
				  Math.Abs(Fusao.MultiplicadorDaFormaAtual(a.Ficha) - 50) < 1e-9,
				  $"{Fusao.MultiplicadorDaFormaAtual(a.Ficha)}");

		// ---- E ELA ACABA NO TEMPO DA TABELA ----
		// 900 de energia a 2,0/s = 450 s = **7,5 min**, que e a linha "50x -> Danca 7,5" da tabela.
		f.Energia = Fusao.EnergiaDaDanca;
		f.UltimoDreno = NowMs() - 449_000;   // 449 s: falta 1 s
		TickDaFusao();
		AfirmarFd("H5 a 7,48 min (449 s) numa forma de 50x a fusao AINDA esta de pe",
				  EstaFundido(a.Id), $"energia {f.Energia:0.##}");

		f.UltimoDreno = NowMs() - 2_000;     // mais 2 s: passa dos 450
		TickDaFusao();
		AfirmarFd("H6 ...e passados os 7,5 min da tabela ela se desfaz sozinha", !EstaFundido(a.Id));
		AfirmarFd("H7 ...e os dois corpos voltam (ninguem fica no selo)",
				  !EhOSelo(a.Zone) && !EhOSelo(b.Zone), $"{a.Zone.Name} / {b.Zone.Name}");
		AfirmarFd("H8 ...e a recarga de 1 h e cobrada dos DOIS",
				  a.Ficha.fusion_cooldown_until > NowMs() && b.Ficha.fusion_cooldown_until > NowMs());

		a.Ficha.ssjBuff = 1;
	}

	/// <summary>Quanto de energia o TIQUE DE PRODUCAO cobra em dez segundos de relogio real.</summary>
	private double DrenoEmDezSegundos(FusaoAtiva f)
	{
		double antes = f.Energia;
		f.UltimoDreno = NowMs() - 10_000;
		TickDaFusao();
		double gasto = antes - f.Energia;
		f.Energia = antes;   // devolve, pra a medida seguinte comecar do mesmo lugar
		return gasto;
	}

	// =====================================================================
	// I. AS BORDAS -- e nenhuma delas prende ninguem
	// =====================================================================
	/// <summary>
	/// As seis saidas do pedido, cada uma com a mesma pergunta no fim: **os dois continuam livres?**
	/// A resposta e sempre `!OcupadoPorFusao` nos dois e `Stun` zerado -- porque a queixa que estas
	/// linhas existem pra impedir nao e "a fusao nao aconteceu", e "eu fiquei preso".
	/// </summary>
	private void AsBordas()
	{
		GD.Print("[fusao2] -- I) as bordas --");

		void Livres(string caso, ServerPlayer a, ServerPlayer b)
		{
			AfirmarFd($"I.{caso}: os dois continuam LIVRES",
					  !OcupadoPorFusao(a.Id) && !OcupadoPorFusao(b.Id));
			AfirmarFd($"I.{caso}: ...e nenhum dos dois ficou preso",
					  a.Combate.Stun <= 0 && b.Combate.Stun <= 0,
					  $"{a.Combate.Stun:0.##} / {b.Combate.Stun:0.##}");
			AfirmarFd($"I.{caso}: ...e nao sobrou convite na mesa",
					  !_pedidosDeFusao.ContainsKey(a.Id) && !_pedidosDeFusao.ContainsKey(b.Id));
		}

		// ---- 1. O CONVITE IGNORADO EXPIRA ----
		{
			ServerPlayer a = ForjarNaFusaoDupla("Tenshinhan", "Saiyan", 1_000_000, true, 0);
			ServerPlayer b = ForjarNaFusaoDupla("Chaos", "Saiyan", 900_000, true, 1);
			Convidar(a, b, TipoDeFusao.Danca);
			AfirmarFd("I.ignorado: o convite esta na mesa", _pedidosDeFusao.ContainsKey(b.Id));

			_pedidosDeFusao[b.Id] = _pedidosDeFusao[b.Id] with { Ate = NowMs() - 1 };
			TickDaFusao();
			Livres("ignorado", a, b);
		}

		// ---- 2. A RECUSA ----
		{
			ServerPlayer a = ForjarNaFusaoDupla("Raditz", "Saiyan", 1_000_000, true, 0);
			ServerPlayer b = ForjarNaFusaoDupla("Nappa", "Saiyan", 900_000, true, 1);
			Convidar(a, b, TipoDeFusao.Danca);
			EscutaDeAvisos?.Clear();
			ResponderAoConvite(b, aceitou: false);
			AfirmarFd("I.recusa: os DOIS sao avisados (um 'nao' mudo pareceria verb quebrado)",
					  (EscutaDeAvisos?.Count ?? 0) >= 2, $"{EscutaDeAvisos?.Count}");
			Livres("recusa", a, b);
		}

		// ---- 3. SAIR DE PERTO ANTES DO ACEITE ----
		{
			ServerPlayer a = ForjarNaFusaoDupla("Turles", "Saiyan", 1_000_000, true, 0);
			ServerPlayer b = ForjarNaFusaoDupla("Broly", "Saiyan", 900_000, true, 1);
			Convidar(a, b, TipoDeFusao.Danca);
			b.Pos = new Vec2((Fusao.TilesColados + 10) * ZoneCollision.TileSize, 0);
			ResponderAoConvite(b, aceitou: true);
			AfirmarFd("I.afastou: o 'sim' de longe NAO funde", !EstaFundido(a.Id));
			Livres("afastou", a, b);
			b.Pos = new Vec2(ZoneCollision.TileSize, 0);
		}

		// ---- 4. O NOCAUTE NO MEIO DA DANCA ----
		{
			ServerPlayer a = ForjarNaFusaoDupla("Paragus", "Saiyan", 1_000_000, true, 0);
			ServerPlayer b = ForjarNaFusaoDupla("Kale", "Saiyan", 900_000, true, 1);
			Convidar(a, b, TipoDeFusao.Danca);
			ResponderAoConvite(b, aceitou: true);
			AfirmarFd("I.nocaute: a danca comecou", _dancando.ContainsKey(a.Id));

			b.Ficha.KO = true;
			TickDaFusao();

			AfirmarFd("I.nocaute: daqui NAO sai fusao nenhuma -- nem estragada",
					  !EstaFundido(a.Id) && !_emCenaDeFusao.ContainsKey(a.Id));
			b.Ficha.KO = false;
			Livres("nocaute", a, b);
			AfirmarFd("I.nocaute: ...e a recarga de 1 h NAO e cobrada (nao houve fusao)",
					  a.Ficha.fusion_cooldown_until == 0 && b.Ficha.fusion_cooldown_until == 0);
		}

		// ---- 5. LARGAR O TECLADO NA DANCA ----
		{
			ServerPlayer a = ForjarNaFusaoDupla("Caulifla", "Saiyan", 1_000_000, true, 0);
			ServerPlayer b = ForjarNaFusaoDupla("Cabba", "Saiyan", 900_000, true, 1);
			Convidar(a, b, TipoDeFusao.Danca);
			ResponderAoConvite(b, aceitou: true);

			DancaDeFusao? d = _dancando.GetValueOrDefault(a.Id);
			AfirmarFd("I.teclado: a danca comecou", d != null);
			if (d != null)
			{
				d.Acaba = NowMs() - 1;
				TickDaFusao();

				// A CENA nasce; adianto ela pra ler o resultado.
				if (_emCenaDeFusao.GetValueOrDefault(a.Id) is { } c)
				{
					c.Funde = NowMs() - 1; TickDaCenaDeFusao();
					c.Acaba = NowMs() - 1; TickDaCenaDeFusao();
				}

				FusaoAtiva? f = FusaoDe(a.Id);
				AfirmarFd("I.teclado: quem larga o teclado FUNDE ESTRAGADO (e nao fica dancando pra sempre)",
						  f is { Estragada: true }, f == null ? "nao fundiu" : "boa");
				if (f != null) Separar(f, "fim do caso do teclado");
			}
			AfirmarFd("I.teclado: ...e ninguem ficou dancando", !_dancando.ContainsKey(a.Id));
		}

		// ---- 6. O LOGOUT NO MEIO DA DANCA ----
		{
			ServerPlayer a = ForjarNaFusaoDupla("Kefla", "Saiyan", 1_000_000, true, 0);
			ServerPlayer b = ForjarNaFusaoDupla("Renso", "Saiyan", 900_000, true, 1);
			Convidar(a, b, TipoDeFusao.Danca);
			ResponderAoConvite(b, aceitou: true);

			SoltarDaFusao(b.Id);
			AfirmarFd("I.logout: a danca cai NA HORA (e nao no proximo tique)",
					  !_dancando.ContainsKey(a.Id) && !_dancando.ContainsKey(b.Id));
			AfirmarFd("I.logout: ...e nao sai fusao nenhuma", !EstaFundido(a.Id));
			Livres("logout", a, b);
		}

		// ---- 7. O NOCAUTE COM A FUSAO JA DE PE SEPARA ----
		{
			ServerPlayer a = ForjarNaFusaoDupla("Bra", "Saiyan", 1_000_000, true, 0);
			ServerPlayer b = ForjarNaFusaoDupla("Pan", "Saiyan", 900_000, true, 1);
			FusaoAtiva? f = FundirDeVerdade(a, b, TipoDeFusao.Danca);
			AfirmarFd("I.KO fundido: a fusao esta de pe", f != null && EstaFundido(a.Id));

			if (f != null)
			{
				FusaoAoCair(a, "nocaute");
				AfirmarFd("I.KO fundido: cair SEPARA a fusao (`defuse_on_downed`, `Fusion.dm:75`)",
						  !EstaFundido(a.Id) && !EstaFundido(b.Id));
				AfirmarFd("I.KO fundido: ...e o passageiro sai do selo", !EhOSelo(b.Zone), b.Zone.Name);
			}
		}
	}

	// =====================================================================
	// J. A FUSAO NAMEKUSEIJIN -- a ABSORCAO, e ela custa um personagem
	// =====================================================================
	/// <summary>
	/// `Namekian_Fusion` (`Fusion.dm:549-569`) + as cinco regras que o dono ditou (N1 a N5).
	///
	/// ============================ O QUE ESTA SECAO EXISTE PRA PROVAR ============================
	/// Ela mudou de assunto neste passe. Antes media *"a fusao Namekuseijin existe, e permanente e o
	/// nocaute nao a separa"*; hoje mede uma coisa mais grave: **ela APAGA UM PERSONAGEM**, e o pedido
	/// do dono foi literal -- *"o outro namek se for jogador, perde o personagem pra sempre (a fusao e
	/// eterna)"*.
	///
	/// As quatro perguntas, na ordem em que uma sustenta a outra:
	///
	///   1. **quem pode** -- o portao racial dos DOIS lados (`Fusion.dm:556-557`), e nada mais;
	///   2. **o consentimento** -- o botao comum de aceitar fusao **recusa**, o botao proprio pede DUAS
	///      vezes, e a segunda so vale depois do intervalo. E a metade do sistema que, errada, custa o
	///      personagem de alguem por um clique;
	///   3. **o bonus** -- BP `(A+B)*2`, o maior stat de cada, as skills dele e o Super Namekuseijin;
	///      e o NPC dando quase nada disso (N4);
	///   4. **o apagamento** -- o slot vazio, o corpo que nao pode mais ser gravado, **e a conta que
	///      continua viva** (o dono nunca pediu que a pessoa perdesse a conta).
	///
	/// ============================ E ELA NAO ENCOSTA NA PASTA DE SAVES DO DONO ============================
	/// Todo apagamento roda dentro de um `using PalcoDeApagamentos` -- irmao do `PalcoDeMortes`, e
	/// existe pela mesma cicatriz (uma bancada ja gravou a morte da Terra no save real). O palco
	/// empresta uma `AccountSave` que so existe em memoria e desvia a UNICA escrita em disco; o
	/// `Slots[slot] = null`, o `PersonagemConsumido`, o `PurgarAssinatura` e o log continuam sendo o
	/// codigo de producao, rodando de verdade sobre um objeto de verdade.
	/// ================================================================================================
	/// </summary>
	private void AFusaoNamekuseijin()
	{
		GD.Print("[fusao2] -- J) a fusao Namekuseijin: o portao, o consentimento e a absorcao --");

		ServerPlayer piccolo = ForjarNaFusaoDupla("Piccolo2", "Namekian", 2_000_000, sabeDancar: false, 0);
		ServerPlayer nail = ForjarNaFusaoDupla("Nail2", "Namekian", 400_000, sabeDancar: false, 1);
		// NO TILE AO LADO tambem, e pela mesma razao da secao A: a Namekuseijin cobra
		// `Fusao.TilesColados` (o `oview(1)` do `Fusion.dm:553`), entao um Saiyajin a 2 tiles seria
		// recusado por DISTANCIA e a J2 assinaria o portao RACIAL sem ele ter sido consultado.
		ServerPlayer goku = ForjarNaFusaoDupla("Goku2", "Saiyan", 2_000_000, sabeDancar: true, 1);

		bool Convite(ServerPlayer a, ServerPlayer b)
		{
			_pedidosDeFusao.Remove(b.Id);
			Convidar(a, b, TipoDeFusao.Namek);
			bool entrou = _pedidosDeFusao.ContainsKey(b.Id);
			_pedidosDeFusao.Remove(b.Id);
			return entrou;
		}

		// ---- J1: OS DOIS NAMEKUSEIJIN, E NADA MAIS E PEDIDO ----
		// Nenhum dos dois sabe dancar e a razao de poder e 0,2 (bem abaixo do `LimiarDeProximidade`):
		// se um portao da Danca tivesse vazado pra ca, este convite nao entraria.
		Mutacao(AfirmarFd,
			"J1 dois Namekuseijin: o convite ENTRA (sem skill e sem poder proximo -- `Fusion.dm:556-557`)",
			"o convidado deixa de ser Namekuseijin",
			() => Convite(piccolo, nail),
			() => nail.Race = "Saiyan",
			() => nail.Race = "Namekian");

		// ---- J2: E O PORTAO VALE PRO CONVIDADOR TAMBEM ----
		// `if(usr.Race!="Namekian") return` e uma linha SEPARADA no DM (`:557`), e uma checagem so
		// (a do alvo) passaria verde na J1 e deixaria um Saiyajin absorver um Namek.
		AfirmarFd("J2 Saiyajin convidando Namekuseijin: NAO entra (o portao vale pros dois lados)",
				  !Convite(goku, piccolo));

		OConsentimentoDaAbsorcao();
		AAbsorcaoDeJogador();
		AAbsorcaoDeNpc();
		OAlvoQueCaiAntesDaConsumacao();
		ODespertarPeloProprioPoder();

		// ============================ AS TRES FILHAS DA FASE 2 ============================
		// As cinco de cima medem o gesto FUNCIONANDO e as travas do botao. Estas tres medem o que
		// sobrou, e as tres nasceram de um pedido explicito:
		//
		//   * <see cref="OsCaminhosQueNaoConsentem"/> -- **todo** caminho que poderia chegar na
		//     absorcao sem passar pelo aceite (admin, IA, pacote forjado, offline, KO, ja fundido, e
		//     o que desconecta no meio), cada um com o positivo ao lado;
		//   * <see cref="OPersonagemPerdidoAtravessaODisco"/> -- "pra sempre" mora no ARQUIVO, e nao
		//     no objeto: gravar, reabrir, e perguntar a tela de selecao de verdade;
		//   * <see cref="JogadorContraNpcLadoALado"/> -- os dois numeros da regra N4 na mesma linha,
		//     pelo mesmo alvo e por dois absorvedores identicos.
		// ==============================================================================
		OsCaminhosQueNaoConsentem();
		OPersonagemPerdidoAtravessaODisco();
		JogadorContraNpcLadoALado();
	}

	// =====================================================================
	// J'. O CONSENTIMENTO -- a metade que, errada, custa o personagem de alguem
	// =====================================================================
	/// <summary>
	/// ============================ POR QUE ISTO E UMA SECAO INTEIRA ============================
	/// Porque a consequencia e a mais grave do jogo e o gesto e o mais barato: um clique. O portao
	/// equivalente do port -- o `DeleteChar` -- exige DIGITAR o nome do personagem, e em jogo nao ha
	/// campo de texto. O que substitui a digitacao sao tres travas, e cada uma tem prova propria aqui:
	/// o botao comum **recusa**, o botao proprio **nao aceita na primeira**, e a segunda **nao vale
	/// antes do intervalo**.
	///
	/// Uma prova so ("a absorcao acontece quando eu confirmo duas vezes") ficaria VERDE num mundo em
	/// que qualquer clique aceitasse -- por isso as tres primeiras afirmam o que **nao** acontece.
	/// ==================================================================================
	/// </summary>
	private void OConsentimentoDaAbsorcao()
	{
		GD.Print("[fusao2] -- J') o consentimento da absorcao: tres travas, tres provas --");

		ServerPlayer a = ForjarNaFusaoDupla("Kami2", "Namekian", 1_000_000, sabeDancar: false, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Nail3", "Namekian", 500_000, sabeDancar: false, 1);

		Convidar(a, b, TipoDeFusao.Namek);
		AfirmarFd("J3 o convite da absorcao fica na mesa do outro", _pedidosDeFusao.ContainsKey(b.Id));

		// ---- J4: O BOTAO COMUM RECUSA, E O CONVITE **CONTINUA** NA MESA ----
		// A trava mais importante das tres: o `fus_sim` e o mesmo botao da Danca e da Potara, e o texto
		// dele promete "voce volta quando a fusao acabar". Se ele aceitasse aqui, a memoria muscular de
		// quem ja aceitou uma Danca custaria um personagem.
		ResponderAoConvite(b, aceitou: true);
		AfirmarFd("J4 o botao COMUM de aceitar fusao NAO aceita uma absorcao",
				  !_emCenaDeFusao.ContainsKey(a.Id) && !EstaFundido(a.Id));
		AfirmarFd("J5 ...e o convite CONTINUA na mesa (ele foi encaminhado, nao respondido)",
				  _pedidosDeFusao.ContainsKey(b.Id));

		// ---- J6: UMA CONFIRMACAO SO NAO ACEITA ----
		ResponderAoConviteDeAbsorcao(b);
		AfirmarFd("J6 a PRIMEIRA confirmacao no botao proprio nao aceita nada",
				  !_emCenaDeFusao.ContainsKey(a.Id));
		AfirmarFd("J7 ...mas ela fica anotada (o relogio das duas confirmacoes armou)",
				  _confirmacoesDeAbsorcao.ContainsKey(b.Id));

		// ---- J8: A SEGUNDA CEDO DEMAIS TAMBEM NAO ----
		// Esta e a que pega o clique duplo, o macro e a rajada de pacote de um cliente modificado.
		ResponderAoConviteDeAbsorcao(b);
		AfirmarFd("J8 a SEGUNDA confirmacao antes do intervalo nao aceita "
				+ $"({AbsorcaoNamekuseijin.SegundosEntreAsConfirmacoes:0} s)",
				  !_emCenaDeFusao.ContainsKey(a.Id));

		// ---- J9: PASSADO O INTERVALO, ELA ACEITA ----
		// O RELOGIO E EMPURRADO PRA TRAS, e nao esperado -- a mesma disciplina do resto desta bancada.
		// O que roda e o metodo de producao, com o carimbo dele antigo.
		_confirmacoesDeAbsorcao[b.Id] =
			NowMs() - (long)(AbsorcaoNamekuseijin.SegundosEntreAsConfirmacoes * 1000) - 1;
		ResponderAoConviteDeAbsorcao(b);
		AfirmarFd("J9 ...e passado o intervalo ela ACEITA (a cena comeca)",
				  _emCenaDeFusao.ContainsKey(a.Id));

		// LIMPEZA: esta secao nao consuma a absorcao (quem faz isso e a J''), entao a cena sai daqui.
		if (_emCenaDeFusao.GetValueOrDefault(a.Id) is { } c) AbortarACenaDeFusao(c, "fim da secao J'");
		_pedidosDeFusao.Remove(b.Id);
		_confirmacoesDeAbsorcao.Remove(b.Id);
		a.Ficha.fusion_cooldown_until = 0;
		b.Ficha.fusion_cooldown_until = 0;
	}

	// =====================================================================
	// J''. A ABSORCAO DE UM JOGADOR -- o bonus E o personagem que se perde
	// =====================================================================
	private void AAbsorcaoDeJogador()
	{
		GD.Print("[fusao2] -- J'') absorver um JOGADOR: o bonus, a forma e o personagem apagado --");

		// ============================ O PALCO PROTEGE A PASTA DE SAVES DO DONO ============================
		// Ver `GameServer.PalcoDeApagamentos`. Sem ele, esta secao APAGARIA um personagem de verdade no
		// disco do dono -- e nao ha desfazer pra isso.
		// ============================================================================================
		using PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();

		// ============================ E O PALCO E MEDIDO, NAO SUPOSTO ============================
		// Mesmo argumento do `PalcoDeMortes.MatouAqui`: um crivo que nunca corta e indistinguivel de
		// crivo nenhum. Esta linha existe porque a PRIMEIRA versao do palco vazou -- ela desviava um
		// metodo, e o `Persistir` grava pelo `_store` direto; a rodada deixou um
		// `bancada_fusao2_93043.json` na pasta do dono. Hoje o palco troca o DESTINO, e isto e a prova.
		// ====================================================================================
		AfirmarFd("J9b o palco desviou o armazenamento pra uma pasta temporaria "
				+ "(a pasta de saves do dono nao e tocada por esta secao)",
				  _store != null && _store.Pasta == palco.PastaDeTeste,
				  _store?.Pasta ?? "sem store");

		ServerPlayer dono = ForjarNaFusaoDupla("Piccolo3", "Namekian", 2_000_000, sabeDancar: false, 0);
		ServerPlayer comido = ForjarNaFusaoDupla("Nail4", "Namekian", 400_000, sabeDancar: false, 1);

		// O ABSORVIDO E MELHOR EM UM STAT E TEM UMA SKILL QUE O OUTRO NAO TEM: sem isso, "herdou o maior
		// de cada" e "herdou as skills dele" ficariam verdes sem nada ter atravessado.
		// NOS STATS **CRUS** e nao nos efetivos, porque e o que o `StatsDe`/`PorStats` leem -- e a razao
		// esta escrita no `Fundir`: os efetivos ja carregam estilo, forma e buffs temporarios, e o
		// `Statify` os reescreve todo tique. Medir `Ephysoff` aqui mediria o motor de stats, nao a
		// heranca (foi o que esta linha fez na primeira rodada: 40 virou 2 no tique seguinte).
		dono.Ficha.physoff = 30;
		comido.Ficha.physoff = 40;
		dono.Ficha.GravMastered = 10;
		comido.Ficha.GravMastered = 25;
		const string SkillDele = "/datum/skill/namek/regeneration";
		comido.Livro.Dar(SkillDele);

		// A CONTA DE MENTIRA, com o personagem no slot 0 -- e o que o `PodeApagarOPersonagem` vai achar.
		AccountSave acc = palco.Emprestar(comido.Conta, comido.Slot,
										  AccountStore.DeJogador(comido, NowMs()));
		AfirmarFd("J10 o personagem do absorvido esta no slot antes de tudo",
				  acc.Slots[comido.Slot] != null);

		double bpDono = dono.Ficha.BP, bpDele = comido.Ficha.BP;

		AbsorverDeVerdade(dono, comido);

		// ---- O QUE **NAO** ACONTECEU: nao ha fusao viva ----
		// A regra estrutural inteira depende disto. Se a absorcao produzisse uma `FusaoAtiva`, o
		// `Persistir` recusaria gravar o corpo do absorvedor PARA SEMPRE (ele nunca separa) -- ver o
		// cabecalho de `AbsorcaoNamekuseijin`.
		AfirmarFd("J11 a absorcao NAO produz fusao nenhuma (nem o corpo do absorvedor esta fundido)",
				  !EstaFundido(dono.Id) && !EstaFundido(comido.Id) && _fusoes.Count == 0);

		// ---- N2: O PODER, E ELE E A CONTA DO DM ----
		AfirmarFd("J12 o BP vira `(A+B)*2` -- `Fusion.dm:264` + `:308`, o estado terminal do original",
				  Math.Abs(dono.Ficha.BP - Fusao.BpDaFusao(bpDono, bpDele)) < 1.0,
				  $"{dono.Ficha.BP:N0} (esperado {Fusao.BpDaFusao(bpDono, bpDele):N0})");

		// ---- N2: OS OUTROS BONUS ----
		AfirmarFd("J13 o maior stat de cada atravessa (30 x 40 -> 40)",
				  Math.Abs(dono.Ficha.physoff - 40) < 1e-6, $"{dono.Ficha.physoff}");
		AfirmarFd("J14 ...e a gravidade dominada tambem (10 x 25 -> 25)",
				  Math.Abs(dono.Ficha.GravMastered - 25) < 1e-6, $"{dono.Ficha.GravMastered}");
		AfirmarFd("J15 ...e as skills dele entram no livro DE VEZ (nao emprestadas)",
				  dono.Livro.Sabe(SkillDele));

		// ---- N1: O SUPER NAMEKUSEIJIN ----
		AfirmarFd("J16 o Super Namekuseijin foi destravado (a skill que escreve a flag entrou no livro)",
				  dono.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin));

		// **E A PORTA DE VERDADE ABRE.** A prova de cima mede a skill; esta mede a FORMA, pelo `Avaliar`
		// que a tecla C e a aba Formas usam. Sem ela, "destravou" seria uma afirmacao sobre um texto no
		// livro -- e a memoria deste projeto chama isso de medir INTENCAO.
		Jandirus.Core.Forms.RecusaForma r = dono.Forma.Avaliar(IdDoSuperNamekuseijin, dono.Ficha.BP,
										   kiFracao: 1, caido: false, Perfil(dono));
		AfirmarFd("J17 ...e o portao da FORMA abre de verdade (`EstadoDeForma.Avaliar`)",
				  r == Jandirus.Core.Forms.RecusaForma.Pode, $"{r}");

		// ---- N3: O PERSONAGEM SE PERDE ----
		AfirmarFd("J18 o personagem do absorvido foi APAGADO (o slot esta vazio)",
				  acc.Slots[comido.Slot] == null);
		AfirmarFd("J19 ...e o corpo dele nao pode mais ser gravado (`PersonagemConsumido`)",
				  comido.PersonagemConsumido);

		// **E A CONTA CONTINUA VIVA.** O dono pediu que a pessoa perdesse o PERSONAGEM, e nao a conta --
		// ela entra de novo e cria outro no slot que vagou. Sem esta prova, um apagamento que zerasse a
		// conta inteira passaria despercebido.
		AfirmarFd("J20 ...mas a CONTA continua de pe e nao foi banida (da pra criar outro personagem)",
				  !acc.Banida && acc.Conta == comido.Conta && acc.Slots.Length == AccountStore.Slots);

		// ---- E O SALVAMENTO PERIODICO NAO O RESSUSCITA ----
		// A metade que so o `Persistir` responde. Sem o `PersonagemConsumido`, a gravacao de 2 em 2
		// minutos (ou a do `Drop`) recriaria o personagem do `ServerPlayer` que ainda esta de pe --
		// e "o personagem voltou depois de ser apagado" e um defeito que ninguem consegue explicar.
		Persistir(comido, acc);
		AfirmarFd("J21 ...e o `Persistir` NAO o traz de volta (o slot continua vazio)",
				  acc.Slots[comido.Slot] == null);

		// ---- E O ABSORVEDOR CONTINUA GRAVAVEL, que e o outro lado da mesma moeda ----
		AccountSave accDono = palco.Emprestar(dono.Conta, dono.Slot, AccountStore.DeJogador(dono, NowMs()));
		accDono.Slots[dono.Slot] = null;
		Persistir(dono, accDono);
		AfirmarFd("J22 ...e quem absorveu CONTINUA sendo gravado (a fusao eterna nao trava o save dele)",
				  accDono.Slots[dono.Slot] != null
				  && Math.Abs((accDono.Slots[dono.Slot]?.Ficha.BP ?? 0) - dono.Ficha.BP) < 1.0);

		// ---- A RECARGA DE 1 h FOI COBRADA DE QUEM ABSORVEU (`Fusion.dm:320`) ----
		AfirmarFd("J23 quem absorveu leva a recarga de 1 h (nao da pra comer o servidor inteiro seguido)",
				  dono.Ficha.fusion_cooldown_until > NowMs());

		dono.Ficha.fusion_cooldown_until = 0;
	}

	// =====================================================================
	// J'''. O NPC -- regra N4: BEM MENOS, e sem a forma
	// =====================================================================
	/// <summary>
	/// *"fundir com npc namek ganha BEM menos bp e outros bonus e nao ganha o super namek"*.
	///
	/// ============================ AS DUAS METADES, E A SEGUNDA E A QUE IMPORTA ============================
	/// "Ganha menos" so quer dizer alguma coisa ao lado de um "ganha muito": as provas daqui comparam
	/// com o que a **mesma dupla** renderia se o alvo fosse jogador, e nao com um numero escrito a mao.
	/// E a metade que a memoria deste projeto manda nunca esquecer -- uma bancada que so mede o caso
	/// pequeno fica verde num mundo em que os dois casos sao pequenos.
	/// ==================================================================================================
	/// </summary>
	private void AAbsorcaoDeNpc()
	{
		GD.Print("[fusao2] -- J''') absorver um NPC: BEM menos, e sem a forma (regra N4) --");

		ServerPlayer dono = ForjarNaFusaoDupla("Piccolo4", "Namekian", 2_000_000, sabeDancar: false, 0);
		ServerPlayer npc = ForjarNaFusaoDupla("AldeaoNamek", "Namekian", 400_000, sabeDancar: false, 1);

		// O CORPO VIRA NPC DO MUNDO: sem `Peer` (ele ja nasce assim aqui) e COM papel -- as duas pernas
		// do `Gente.EhNpcDoMundo`. E o mesmo gesto que a `--convivioteste` usa pra provar o corte do
		// `EhPessoa`, e nao uma segunda definicao de NPC.
		npc.Papel = new Jandirus.Core.Npc.PapelDeNpc(
			new Jandirus.Core.Npc.MoldeDeNpc { Id = "bancada", Nome = "aldeao" }, 0);
		npc.Ficha.physoff = 40;
		const string SkillDele = "/datum/skill/namek/regeneration";
		npc.Livro.Dar(SkillDele);

		dono.Ficha.physoff = 30;
		double bpDono = dono.Ficha.BP, bpNpc = npc.Ficha.BP;

		// ---- O CAMINHO DO NPC NAO PASSA POR CONVITE ----
		// E isso FECHA um buraco em vez de abrir um: um pendente na mesa de um corpo dirigido por IA
		// seria um caminho novo pra fundir sem consentimento. Ver `ConvidarParaAFusaoNamekuseijin`.
		//
		// ============================ O VERB ENTRA PELO CONE DO SOCO, E ELE PRECISA DE MIRA ============================
		// Esta e a UNICA secao desta bancada que chama o verb de verdade em vez do `Convidar` -- porque
		// aqui a escolha do alvo E parte da regra (o NPC nao e convidado, ele e agarrado). E o
		// `AlvoNaFrente` e o cone do soco: sem virar o corpo, ele nao acha ninguem e a secao mediria
		// "verb sem alvo" achando que mediu "NPC recusado". Na primeira rodada foi exatamente isso.
		// ==========================================================================================================
		// ============================ E OS DOIS SAEM DE PERTO DA MULTIDAO ============================
		// O `AlvoNaFrente` pega o corpo MAIS PROXIMO no cone, e a esta altura da bancada ha uma duzia de
		// corpos forjados empilhados nos tiles 0 e 1 da mesma zona (cada secao poe os seus e nao os
		// tira). Sem esta mudanca o verb agarrava um Saiyajin de uma secao anterior e a recusa vinha do
		// portao RACIAL -- a bancada anunciaria "NPC nao entra" tendo medido outra coisa.
		//
		// **E ela nao afrouxa nada**: os dois continuam no tile ao lado um do outro, que e o que o
		// `TilesColados` cobra. O que mudou foi o bairro, nao a distancia entre eles.
		// ========================================================================================
		var canto = new Vec2(0, 40 * ZoneCollision.TileSize);
		dono.Pos = canto;
		npc.Pos = canto + new Vec2(ZoneCollision.TileSize, 0);

		dono.Facing = Jandirus.Core.World.Facing.East;
		ConvidarParaAFusaoNamekuseijin(dono);
		AfirmarFd("J24 absorver NPC nao poe convite nenhum na mesa dele (NPC nao consente)",
				  !_pedidosDeFusao.ContainsKey(npc.Id));
		AfirmarFd("J25 ...e vai direto pra cena (o gesto de quem absorve basta)",
				  _emCenaDeFusao.ContainsKey(dono.Id));

		if (_emCenaDeFusao.GetValueOrDefault(dono.Id) is { } c)
		{
			c.Funde = NowMs() - 1;
			TickDaCenaDeFusao();
			c.Acaba = NowMs() - 1;
			TickDaCenaDeFusao();
		}

		// ---- N4: BEM MENOS BP ----
		double ganho = dono.Ficha.BP - bpDono;
		double ganhoSeFosseJogador = Fusao.BpDaFusao(bpDono, bpNpc) - bpDono;
		AfirmarFd("J26 o NPC rende o previsto pela conta do Core (fracao do BP dele, com teto no meu)",
				  Math.Abs(dono.Ficha.BP - AbsorcaoNamekuseijin.BpDepoisDeAbsorverNpc(bpDono, bpNpc)) < 1.0,
				  $"{dono.Ficha.BP:N0}");
		AfirmarFd("J27 ...e isso e BEM menos que o mesmo alvo renderia como JOGADOR "
				+ $"({ganho:N0} contra {ganhoSeFosseJogador:N0})",
				  ganho < ganhoSeFosseJogador / 10);

		// ---- N4: E MENOS BONUS ----
		AfirmarFd("J28 o NPC NAO passa stat nenhum (30 continua 30)",
				  Math.Abs(dono.Ficha.physoff - 30) < 1e-6, $"{dono.Ficha.physoff}");
		AfirmarFd("J29 ...nem skill", !dono.Livro.Sabe(SkillDele));
		AfirmarFd("J30 ...e NAO da o Super Namekuseijin (o dono foi literal nisto)",
				  !dono.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin));

		// ---- E O CORPO DO NPC SAIU DO MUNDO ----
		AfirmarFd("J31 o NPC absorvido sai do mundo (`RemoverNpc`, o avesso do `PorNoMundo`)",
				  !_players.ContainsKey(npc.Id));

		// ---- O CONTRA-EXEMPLO DO PORTAO: clone e boneco NAO sao absorviveis ----
		// `EhNamekNpcAbsorvivel` e estreito de proposito: o terceiro grupo do `Gente` (clone da mente,
		// boneco do corpo largado) carrega a Ficha de uma pessoa VIVA, e absorve-lo seria absorver o
		// dono dela por uma porta lateral. Sem esta prova, "aceita NPC" poderia ter virado "aceita
		// qualquer corpo sem dono".
		ServerPlayer clone = ForjarNaFusaoDupla("CloneNamek", "Namekian", 400_000, sabeDancar: false, 1);
		clone.Papel = null;
		clone.Conta = "";
		clone.Slot = -1;
		AfirmarFd("J32 um corpo sem dono e SEM papel (clone/boneco) NAO e absorvivel",
				  !EhNamekNpcAbsorvivel(clone));

		dono.Ficha.fusion_cooldown_until = 0;
	}

	// =====================================================================
	// J''''. O ALVO QUE CAI ENTRE O ACEITE E A CONSUMACAO
	// =====================================================================
	/// <summary>
	/// A regra que o dono exigiu junto com N3: *"se o alvo cair/desconectar/morrer entre o aceite e a
	/// consumacao, a fusao NAO acontece"* -- e ela ja era a regra da Danca (`AbortarACenaDeFusao`, que
	/// nao produz fusao nenhuma, nem estragada).
	///
	/// **AQUI ELA VALE MAIS CARO**, e por isso tem prova propria: numa Danca abortada os dois so
	/// continuam dois; numa absorcao abortada o que NAO pode acontecer e um personagem ser apagado.
	/// </summary>
	private void OAlvoQueCaiAntesDaConsumacao()
	{
		GD.Print("[fusao2] -- J'''') o alvo que cai entre o aceite e a consumacao --");

		using PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();

		ServerPlayer dono = ForjarNaFusaoDupla("Piccolo5", "Namekian", 2_000_000, sabeDancar: false, 0);
		ServerPlayer alvo = ForjarNaFusaoDupla("Nail5", "Namekian", 900_000, sabeDancar: false, 1);

		AccountSave acc = palco.Emprestar(alvo.Conta, alvo.Slot, AccountStore.DeJogador(alvo, NowMs()));
		double bpAntes = dono.Ficha.BP;

		Convidar(dono, alvo, TipoDeFusao.Namek);
		_confirmacoesDeAbsorcao[alvo.Id] =
			NowMs() - (long)(AbsorcaoNamekuseijin.SegundosEntreAsConfirmacoes * 1000) - 1;
		ResponderAoConviteDeAbsorcao(alvo);
		AfirmarFd("J33 aceita, a cena comeca", _emCenaDeFusao.ContainsKey(dono.Id));

		// O NOCAUTE NO MEIO DA CENA -- a guarda do `TickDaCenaDeFusao`.
		alvo.Ficha.KO = true;
		TickDaCenaDeFusao();

		AfirmarFd("J34 o alvo cai no meio: a cena aborta e NAO ha absorcao",
				  !_emCenaDeFusao.ContainsKey(dono.Id) && !_emCenaDeFusao.ContainsKey(alvo.Id));
		AfirmarFd("J35 ...o personagem dele CONTINUA existindo (a prova que mais importa)",
				  acc.Slots[alvo.Slot] != null && !alvo.PersonagemConsumido);
		AfirmarFd("J36 ...e ninguem ganhou poder nenhum", Math.Abs(dono.Ficha.BP - bpAntes) < 1.0);

		// E NEM A RECARGA E COBRADA: ela e o preco de uma absorcao que ACONTECEU. Punir os dois por um
		// terceiro ter passado por perto e o argumento que o `AbortarACenaDeFusao` ja escreve.
		AfirmarFd("J37 ...e nem a recarga de 1 h foi cobrada (nao aconteceu nada pra cobrar)",
				  dono.Ficha.fusion_cooldown_until <= NowMs());

		alvo.Ficha.KO = false;
	}

	// =====================================================================
	// J'''''. N5 -- O SUPER NAMEKUSEIJIN PELO PROPRIO PODER
	// =====================================================================
	/// <summary>
	/// *"namekuseijins ganham super namek aprox no mesmo requisito do SSJ (mantendo a ideia de cada um
	/// ter um requisito pessoal, mas em torno de um valor)"*.
	///
	/// ============================ UMA PORTA, DOIS CAMINHOS -- E OS DOIS SAO PROVADOS ============================
	/// A secao J'' ja provou o caminho da ABSORCAO. Esta prova o caminho do PODER, e a coisa que as
	/// duas juntas afirmam e que **nao ha duas formas**: os dois caminhos escrevem a mesma skill, que
	/// e o unico escritor da flag que o unico portao da unica entrada de catalogo consulta.
	///
	/// O numero nao esta aqui: ele e o `snamekat` pessoal (`LimiaresPessoais.RolarNamek`), e quem o
	/// mede e a `--formasbench`. Aqui o que se mede e o GESTO -- abaixo da porta nao desperta, acima
	/// desperta, e uma vez so.
	/// ========================================================================================================
	/// </summary>
	private void ODespertarPeloProprioPoder()
	{
		GD.Print("[fusao2] -- J''''') N5: o Super Namekuseijin desperta pelo proprio poder --");

		Jandirus.Core.Forms.FormaDef? d = Jandirus.Core.Forms.Catalogo.Def(IdDoSuperNamekuseijin);
		if (d == null) { AfirmarFd("J38 o catalogo tem a forma `snamek`", false); return; }

		ServerPlayer fraco = ForjarNaFusaoDupla("Dende2", "Namekian", 1, sabeDancar: false, 0);
		double porta = fraco.Forma.PortaDeBp(d);
		AfirmarFd($"J38 a porta pessoal existe e e positiva ({porta:N0})", porta > 0);

		// ---- ABAIXO DA PORTA: NADA ----
		fraco.Ficha.BP = porta * 0.9;
		ConferirODespertarDoSuperNamekuseijin(fraco);
		AfirmarFd("J39 abaixo da porta pessoal, o Super Namekuseijin NAO desperta",
				  !fraco.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin));

		// ---- ACIMA: DESPERTA, E A FORMA ABRE ----
		fraco.Ficha.BP = porta * 1.01;
		ConferirODespertarDoSuperNamekuseijin(fraco);
		AfirmarFd("J40 cruzada a porta pessoal, ele desperta sozinho (como o SSJ de um Saiyajin)",
				  fraco.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin));

		Jandirus.Core.Forms.RecusaForma r = fraco.Forma.Avaliar(IdDoSuperNamekuseijin, fraco.Ficha.BP,
											kiFracao: 1, caido: false, Perfil(fraco));
		AfirmarFd("J41 ...e a FORMA passa a abrir de verdade (`EstadoDeForma.Avaliar`)",
				  r == Jandirus.Core.Forms.RecusaForma.Pode, $"{r}");

        // ---- E SO NAMEKUSEIJIN ----
		// O contra-exemplo do portao racial: sem ele, o despertar por BP seria dado a qualquer raca que
		// passasse pelo tique -- e o `snamek()` do DM abre com `if(Race=="Namekian")` (`Super_Namek.dm:9`).
		ServerPlayer saiyajin = ForjarNaFusaoDupla("Nappa2", "Saiyan", porta * 5, sabeDancar: false, 1);
		ConferirODespertarDoSuperNamekuseijin(saiyajin);
		AfirmarFd("J42 um Saiyajin com poder de sobra NAO ganha o Super Namekuseijin",
				  !saiyajin.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin));

		// =================================================================
		// N1 x N5: OS DOIS CAMINHOS DESEMBOCAM NA **MESMA** PORTA
		// =================================================================
		// ============================ POR QUE ISTO PRECISA DE VARREDURA DE FONTE ============================
		// A J16/J17 provam o caminho da ABSORCAO e a J40/J41 o do PODER, e as duas usam o mesmo
		// `IdDoSuperNamekuseijin`. **Isso nao prova que a forma e uma so**: dois caminhos com duas
		// implementacoes -- um dando a skill, o outro escrevendo um bit novo -- passariam nas quatro,
		// porque cada um abriria a sua metade, e a divergencia so apareceria no dia em que uma delas
		// mudasse. O que amarra e a contagem de ESCRITORES: se ha um so, nao ha o que divergir.
		//
		// (A outra metade -- "nao ha uma segunda ENTRADA de catalogo com a mesma flag" -- e da
		// `formas` bench, secao 20, que conta as entradas do `Catalogo.Todas`.)
		// ================================================================================================
		string[] fonte = Fonte("Server/GameServer.Namekuseijin.cs");
		AfirmarFd("J78 o fonte da absorcao foi lido", fonte.Length > 100, $"{fonte.Length} linhas");

		int escritores = fonte.Count(l => !l.TrimStart().StartsWith("///", StringComparison.Ordinal)
										  && l.Contains(".Dar(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin)",
														StringComparison.Ordinal));
		AfirmarFd("J78b ...e ha **UM** unico lugar que poe a skill do Super Namekuseijin no livro "
				+ "(o `DarOSuperNamekuseijin`) -- os dois caminhos passam por ele",
				  escritores == 1, $"{escritores} escritor(es)");

		string[] absorve = CorpoDoMetodo(fonte, "private void AbsorverNamekuseijin(ServerPlayer dono");
		string[] desperta = CorpoDoMetodo(fonte, "private void ConferirODespertarDoSuperNamekuseijin(");
		AfirmarFd("J79 o caminho da ABSORCAO chama aquela porta, e nao uma copia dela",
				  absorve.Any(l => l.Contains("DarOSuperNamekuseijin", StringComparison.Ordinal)),
				  $"{absorve.Length} linhas");
		AfirmarFd("J79b ...e o caminho do PODER chama a MESMA porta",
				  desperta.Any(l => l.Contains("DarOSuperNamekuseijin", StringComparison.Ordinal)),
				  $"{desperta.Length} linhas");

		// A ENTRADA DE CATALOGO, e o portao dela -- a ponta que o livro nao ve.
		Jandirus.Core.Forms.FormaDef? snamek = Jandirus.Core.Forms.Catalogo.Def(IdDoSuperNamekuseijin);
		AfirmarFd("J80 e a forma que os dois abrem e a UNICA entrada `snamek` do catalogo, gateada "
				+ "pela flag que aquela skill escreve",
				  snamek?.PedeFlag?.Campo == "snamek" && snamek?.ChaveDoLimiar == "snamekat",
				  $"{snamek?.PedeFlag?.Campo ?? "sem flag"} / {snamek?.ChaveDoLimiar ?? "sem limiar"}");
	}


	// =====================================================================
	// J^. OS CAMINHOS QUE CHEGAM **SEM** O ACEITE -- um por um, com o positivo ao lado
	// =====================================================================
	/// <summary>
	/// ============================ ESTA E A SECAO QUE, ERRADA, CUSTA O PERSONAGEM DE UM JOGADOR ============================
	/// A J' ja prova as tres travas do BOTAO (o comum recusa, o proprio pede duas vezes, a segunda so
	/// vale depois do intervalo). Esta pergunta a coisa oposta e maior: **existe algum OUTRO caminho que
	/// chegue na absorcao sem passar por aquele botao?**
	///
	/// A lista nao foi inventada aqui -- ela e o levantamento de fase 0, caminho por caminho: verb de
	/// admin, IA dirigindo um corpo, pacote forjado, alvo desligado, alvo caido, alvo ja fundido, e o
	/// alvo que desconecta ENTRE o aceite e a consumacao. Cada um tem prova propria, **e cada um tem o
	/// contra-exemplo ao lado**: se so houvesse a metade que recusa, um mundo em que NADA funde ficaria
	/// verde inteiro.
	///
	/// ============================ E A REGUA E A MESMA PRA TODOS ============================
	/// <see cref="OQueMudouNaAbsorcao"/>: nao ficou convite na mesa, nao comecou cena, ninguem fundiu,
	/// **o personagem continua no slot**, o BP nao andou e a recarga nao foi cobrada. Sete perguntas
	/// numa linha, e o detalhe da falha diz QUAL delas caiu -- uma afirmacao composta que so sabe dizer
	/// "false" e uma afirmacao que ninguem consegue consertar.
	/// ==============================================================================================================
	/// </summary>
	private void OsCaminhosQueNaoConsentem()
	{
		GD.Print("[fusao2] -- J^) os caminhos SEM aceite: admin, IA, pacote forjado, offline, KO, "
			   + "ja fundido, e o que cai no meio --");

		// O PALCO PROTEGE A PASTA DO DONO: esta secao chama verb de ADMIN (que grava `admin.log`) e
		// deixa o apagamento a um passo de acontecer em varios pontos. Ver `PalcoDeApagamentos`.
		using PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();

		int serie = 0;
		(ServerPlayer Dono, ServerPlayer Alvo, AccountSave Acc, double Bp) Dupla()
		{
			serie++;
			ServerPlayer d = ForjarNaFusaoDupla($"Piccolo7{serie}", "Namekian", 2_000_000, false, 0);
			ServerPlayer a = ForjarNaFusaoDupla($"Nail7{serie}", "Namekian", 500_000, false, 1);
			AccountSave acc = palco.Emprestar(a.Conta, a.Slot, AccountStore.DeJogador(a, NowMs()));
			return (d, a, acc, d.Ficha.BP);
		}

		// `conviteDePe` = este caso e um em que o pendente **deve** continuar na mesa (o forjado de um
		// terceiro nao pode nem aceitar nem DERRUBAR o convite alheio). Sem este parametro a regua
		// cobraria a mesa vazia e reprovaria por acertar.
		void NadaAconteceu(string id, ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp,
						   bool conviteDePe = false)
		{
			string mudou = OQueMudouNaAbsorcao(dono, alvo, acc, bp, conviteDePe);
			AfirmarFd($"{id}: **nada acontece** -- o personagem de {alvo.Name} continua existindo",
					  mudou.Length == 0, mudou);
		}

		// =================================================================
		// J43-J44: O CONTROLE POSITIVO. Ele vem PRIMEIRO de proposito.
		// =================================================================
		// Sem ele, esta secao inteira ficaria verde num mundo em que a absorcao simplesmente nao
		// funciona -- e "nada acontece" seria verdade por um motivo que nao tem nada a ver com
		// consentimento. Este bloco e o unico da secao em que um personagem morre de verdade.
		{
			(ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp) = Dupla();

			AbsorverDeVerdade(dono, alvo);

			AfirmarFd($"J43 **COM o aceite** (convite + as DUAS confirmacoes): a absorcao acontece "
					+ $"(BP {bp:N0} -> {dono.Ficha.BP:N0})",
					  dono.Ficha.BP > bp + 1.0, $"{dono.Ficha.BP:N0}");
			AfirmarFd("J44 ...e o personagem do absorvido se perde (o slot vazio e o corpo consumido)",
					  acc.Slots[alvo.Slot] == null && alvo.PersonagemConsumido);

			dono.Ficha.fusion_cooldown_until = 0;
		}

		// =================================================================
		// J45-J47: O VERB DE ADMIN
		// =================================================================
		// ============================ DUAS PROVAS, PORQUE UMA SO NAO FECHA ============================
		// A runtime (J45) prova que os nomes plausiveis nao existem HOJE. Ela nao prova nada sobre os
		// ~40 verbs que existem: um `admin_fundir` escrito amanha passaria por ela sem tocar em nada.
		// Por isso a J46 varre o CORPO INTEIRO do `VerboDeAdmin` no fonte e cobra que nenhuma linha dele
		// chame o encanamento da absorcao -- e a J47 roda a MESMA varredura sobre uma copia adulterada
		// pra provar que ela sabe ficar vermelha. Sem a J47, a J46 seria um comentario bonito.
		// ==========================================================================================
		{
			(ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp) = Dupla();
			dono.Poderes |= Jandirus.Net.Protocol.Poder.Admin;

			EscutaDeAvisos?.Clear();
			string[] tentados =
				["admin_fundir", "admin_fusao", "admin_absorver", "admin_namek", "admin_fusao_namek"];
			foreach (string cmd in tentados) Verbo(dono, cmd, alvo.Name);

			int recusados = EscutaDeAvisos?.Count(a => a.Contains("nao existe")) ?? 0;
			AfirmarFd($"J45 o admin manda os {tentados.Length} nomes plausiveis e o funil responde "
					+ "\"esse comando de administrador nao existe\" em todos",
					  recusados == tentados.Length, $"{recusados} de {tentados.Length}");
			NadaAconteceu("J45b (verb de admin)", dono, alvo, acc, bp);

			// A VARREDURA DO FONTE -- exaustiva sobre a tabela inteira, e nao sobre cinco palpites.
			string[] admin = CorpoDoMetodo(Fonte("Server/GameServer.Admin.cs"),
										   "private bool VerboDeAdmin(ServerPlayer pl, string cmd, string arg)");
			AfirmarFd("J46 o corpo do `VerboDeAdmin` foi extraido do fonte (a assinatura ainda bate)",
					  admin.Length > 20, $"{admin.Length} linhas");
			AfirmarFd("J46b ...e NENHUM dos verbs de admin chama o encanamento da absorcao",
					  SemAbsorcaoNestasLinhas(admin), PrimeiraLinhaComAbsorcao(admin));

			// O MUTANTE: a mesma varredura sobre uma copia com o verb que nao existe.
			string[] adulterado = [.. admin, "case \"admin_absorver\": AbsorverNamekuseijin(pl, alvo); break;"];
			AfirmarFd("   DEFEITO INJETADO (um `admin_absorver` no fonte adulterado): a MESMA varredura REPROVA",
					  !SemAbsorcaoNestasLinhas(adulterado),
					  "a J46b e decoracao -- ela nao sabe ficar vermelha");

			dono.Poderes &= ~Jandirus.Net.Protocol.Poder.Admin;
		}

		// =================================================================
		// J48-J50: A IA / O NPC DIRIGINDO O GESTO
		// =================================================================
		{
			(ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp) = Dupla();

			// O CORPO DE IA: sem `Peer` e COM papel -- as duas pernas do `Gente.EhNpcDoMundo`.
			ServerPlayer ia = ForjarNaFusaoDupla("NamekDaIa", "Namekian", 900_000, false, 1);
			ia.Papel = new Jandirus.Core.Npc.PapelDeNpc(
				new Jandirus.Core.Npc.MoldeDeNpc { Id = "bancada", Nome = "aldeao" }, 0);

			// ============================ O CANTO LIVRE E OBRIGATORIO AQUI ============================
			// O verb entra pelo `AlvoNaFrente`, que e o cone do soco -- e a esta altura da bancada ha
			// uma duzia de corpos empilhados nos tiles 0 e 1. Sem mudar de bairro, o verb agarraria um
			// Saiyajin de outra secao e a recusa viria do portao RACIAL: a bancada anunciaria "a IA nao
			// funde" tendo medido outra coisa. E a mesma armadilha que a secao J''' documenta.
			// ======================================================================================
			var canto = new Vec2(0, 60 * ZoneCollision.TileSize);
			ia.Pos = canto;
			alvo.Pos = canto + new Vec2(ZoneCollision.TileSize, 0);
			ia.Facing = Jandirus.Core.World.Facing.East;

			// O CORPO DE IA MANDA OS DOIS IDS PELO CANAL DE PRODUCAO (`C2S.Habilidade` ->
			// `UsarHabilidade`). E o que aconteceria se o cerebro um dia escrevesse `fus_namek` no
			// `Comando.Habilidade`.
			UsarHabilidade(ia, "fus_namek");
			UsarHabilidade(ia, "fus_namek_sim");
			AfirmarFd("J48 um corpo de IA mandando `fus_namek`/`fus_namek_sim` nao poe convite nem cena "
					+ "(o `EhPessoa` corta antes -- assinatura vazia)",
					  !_pedidosDeFusao.ContainsKey(alvo.Id) && !_emCenaDeFusao.ContainsKey(ia.Id)
					  && !_confirmacoesDeAbsorcao.ContainsKey(ia.Id));
			NadaAconteceu("J48b (IA como autor)", dono, alvo, acc, bp);

			// E O CEREBRO NUNCA ESCREVE ESSES IDS -- varredura do fonte, com o mutante ao lado.
			string[] cerebro = Fonte("Core/Ai/Cerebro.cs");
			AfirmarFd("J49 o fonte do cerebro foi lido", cerebro.Length > 100, $"{cerebro.Length} linhas");
			AfirmarFd("J49b ...e a palavra `fus_` nao aparece nele em lugar nenhum "
					+ "(a IA nao tem como pedir uma fusao)",
					  !cerebro.Any(l => l.Contains("fus_", StringComparison.Ordinal)));
			AfirmarFd("   DEFEITO INJETADO (uma linha `Habilidade = \"fus_namek\"` no fonte adulterado): "
					+ "a MESMA varredura REPROVA",
					  new[] { "cmd.Habilidade = \"fus_namek\";" }
						.Concat(cerebro).Any(l => l.Contains("fus_", StringComparison.Ordinal)));

			// ---- J50: E O ALVO DE NPC NAO PODE SER GENTE ----
			// Abrir o portao pro NPC (regra N4) e a linha que MAIS podia abrir demais: ela e a mesma
			// que protege contra pacote de IA. Se o `EhNamekNpcAbsorvivel` aceitasse um jogador, a
			// absorcao teria um caminho sem convite NENHUM -- e o dono perderia o personagem sem
			// nunca ter visto uma caixa.
			AfirmarFd("J50 o portao de NPC NAO aceita gente (abrir a porta do NPC nao abriu a das pessoas)",
					  !EhNamekNpcAbsorvivel(alvo) && EhNamekNpcAbsorvivel(ia));

			_pedidosDeFusao.Remove(alvo.Id);
			_players.Remove(ia.Id);
			ZoneList(_fdZona.Hash).Remove(ia);
		}

		// =================================================================
		// J51-J54: O PACOTE FORJADO
		// =================================================================
		// O `case Protocol.C2S.Habilidade` le uma STRING e despacha: nao ha nonce, nao ha id de
		// convite, nao ha nada que so o cliente de verdade saiba mandar. Entao a pergunta nao e "da
		// pra forjar o pacote?" (da), e sim **"o que o pacote forjado consegue?"**.
		{
			(ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp) = Dupla();

			// ---- J51: SEM CONVITE NENHUM, EM RAJADA ----
			for (int i = 0; i < 3; i++) UsarHabilidade(alvo, "fus_namek_sim");
			AfirmarFd("J51 `fus_namek_sim` em rajada SEM convite nenhum nao arma nem o relogio "
					+ "das confirmacoes",
					  !_confirmacoesDeAbsorcao.ContainsKey(alvo.Id));
			NadaAconteceu("J51b (pacote forjado sem convite)", dono, alvo, acc, bp);

			// ---- J52: COM CONVITE, PELO BOTAO COMUM, ATRAVES DO DESPACHANTE DE VERDADE ----
			// A J4 mede isto chamando o `ResponderAoConvite` na mao. Aqui a mesma regra e cobrada
			// pelo caminho que um cliente modificado usaria: a string crua no canal de habilidade.
			Convidar(dono, alvo, TipoDeFusao.Namek);
			AfirmarFd("J52 o convite entrou (pra o forjado ter o que atacar)",
					  _pedidosDeFusao.ContainsKey(alvo.Id));
			UsarHabilidade(alvo, "fus_sim");
			AfirmarFd("J52b o `fus_sim` cru no canal de habilidade NAO aceita a absorcao "
					+ "(e o convite fica na mesa)",
					  !_emCenaDeFusao.ContainsKey(dono.Id) && _pedidosDeFusao.ContainsKey(alvo.Id));

			// ---- J53: O TERCEIRO QUE FORJA O SIM DE OUTRO ----
			// O pacote carrega a identidade de quem o mandou (o `Peer`), entao "forjar o sim de
			// outra pessoa" so seria possivel se o servidor guardasse a confirmacao por outra chave
			// que nao o id de quem confirma. Esta prova e o que amarra isso.
			ServerPlayer terceiro = ForjarNaFusaoDupla("NamekIntruso", "Namekian", 700_000, false, 1);
			UsarHabilidade(terceiro, "fus_namek_sim");
			_confirmacoesDeAbsorcao[terceiro.Id] =
				NowMs() - (long)(AbsorcaoNamekuseijin.SegundosEntreAsConfirmacoes * 1000) - 1;
			UsarHabilidade(terceiro, "fus_namek_sim");
			AfirmarFd("J53 um TERCEIRO confirmando duas vezes nao aceita o convite que e de outro "
					+ "(a confirmacao e por id de quem confirma)",
					  !_emCenaDeFusao.ContainsKey(dono.Id) && _pedidosDeFusao.ContainsKey(alvo.Id));
			NadaAconteceu("J53b (o sim de um terceiro)", dono, alvo, acc, bp, conviteDePe: true);

			// ---- J54: E O DONO DO CONVITE, ESSE, ACEITA -- o contra-exemplo dos tres de cima ----
			UsarHabilidade(alvo, "fus_namek_sim");
			_confirmacoesDeAbsorcao[alvo.Id] =
				NowMs() - (long)(AbsorcaoNamekuseijin.SegundosEntreAsConfirmacoes * 1000) - 1;
			UsarHabilidade(alvo, "fus_namek_sim");
			AfirmarFd("J54 **e o dono do convite, pelo mesmo canal cru, ACEITA** -- era o "
					+ "consentimento barrando, e nao o canal",
					  _emCenaDeFusao.ContainsKey(dono.Id));

			if (_emCenaDeFusao.GetValueOrDefault(dono.Id) is { } c) AbortarACenaDeFusao(c, "fim da J54");
			_confirmacoesDeAbsorcao.Remove(terceiro.Id);
			_pedidosDeFusao.Remove(alvo.Id);
			_players.Remove(terceiro.Id);
			ZoneList(_fdZona.Hash).Remove(terceiro);
			dono.Ficha.fusion_cooldown_until = 0;
		}

		// =================================================================
		// J55-J56: O ALVO DESLIGADO
		// =================================================================
		{
			(ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp) = Dupla();

			Convidar(dono, alvo, TipoDeFusao.Namek);
			AfirmarFd("J55 o convite estava na mesa antes de ele sair", _pedidosDeFusao.ContainsKey(alvo.Id));

			// O QUE O `Drop` CHAMA, e nao uma limpeza inventada aqui.
			SoltarDaFusao(alvo.Id);
			AfirmarFd("J55b sair do jogo LIMPA o convite pendente "
					+ "(id de rede se reusa: quem entrasse depois herdaria o \"sim\")",
					  !_pedidosDeFusao.ContainsKey(alvo.Id));

			// E O FORJADO DEPOIS DE OFFLINE TAMBEM NAO PEGA NADA.
			UsarHabilidade(alvo, "fus_namek_sim");
			_confirmacoesDeAbsorcao[alvo.Id] =
				NowMs() - (long)(AbsorcaoNamekuseijin.SegundosEntreAsConfirmacoes * 1000) - 1;
			UsarHabilidade(alvo, "fus_namek_sim");
			NadaAconteceu("J56 (alvo desligado, e o sim chegando depois)", dono, alvo, acc, bp);

			_confirmacoesDeAbsorcao.Remove(alvo.Id);
			dono.Ficha.fusion_cooldown_until = 0;
		}

		// =================================================================
		// J57-J58: O ALVO CAIDO (nocaute e morte)
		// =================================================================
		{
			(ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp) = Dupla();

			alvo.Ficha.KO = true;
			bool comKo = ConviteDeAbsorcaoEntra(dono, alvo);
			alvo.Ficha.KO = false;
			bool semKo = ConviteDeAbsorcaoEntra(dono, alvo);
			AfirmarFd("J57 alvo NOCAUTEADO: o convite nao entra -- **e de pe entra** "
					+ "(era o nocaute barrando, e nao a raca, a distancia ou a recarga)",
					  !comKo && semKo, $"com KO {comKo}, de pe {semKo}");

			alvo.Ficha.dead = true;
			bool morto = ConviteDeAbsorcaoEntra(dono, alvo);
			alvo.Ficha.dead = false;
			AfirmarFd("J58 alvo MORTO: idem -- nao se absorve quem ja se foi",
					  !morto && ConviteDeAbsorcaoEntra(dono, alvo));
			NadaAconteceu("J58b (alvo caido)", dono, alvo, acc, bp);
		}

		// =================================================================
		// J59: O ALVO JA EM OUTRA FUSAO
		// =================================================================
		{
			(ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp) = Dupla();

			// UMA DANCA DE VERDADE com um terceiro -- pela corrente inteira, como a secao D faz.
			ServerPlayer par = ForjarNaFusaoDupla("NamekDoPar", "Namekian", 480_000, true, 1);
			alvo.Livro.Dar(PathDaDanca);
			FusaoAtiva? danca = FundirDeVerdade(alvo, par, TipoDeFusao.Danca);
			AfirmarFd("J59 o alvo esta MESMO fundido noutra fusao", danca != null && EstaFundido(alvo.Id));

			bool fundido = ConviteDeAbsorcaoEntra(dono, alvo);
			if (danca != null) Separar(danca, "fim da J59");
			alvo.Ficha.fusion_cooldown_until = 0;
			dono.Ficha.fusion_cooldown_until = 0;
			bool solto = ConviteDeAbsorcaoEntra(dono, alvo);

			AfirmarFd("J59b alvo JA FUNDIDO: o convite nao entra -- **e separado entra** "
					+ "(o `OcupadoPorFusao` cobre as quatro fases numa pergunta so)",
					  !fundido && solto, $"fundido {fundido}, solto {solto}");
			NadaAconteceu("J59c (alvo ja fundido)", dono, alvo, acc, bp);

			par.Ficha.fusion_cooldown_until = 0;
			_players.Remove(par.Id);
			ZoneList(_fdZona.Hash).Remove(par);
		}

		// =================================================================
		// J60-J61: O ALVO QUE DESCONECTA **ENTRE O ACEITE E A CONSUMACAO**
		// =================================================================
		// A J'''' ja mede o NOCAUTE no meio da cena. Este e o outro corte, e ele e o mais assustador
		// dos dois: a pessoa aceitou, a cinematica esta rodando, e ela fecha o jogo. Se o tique
		// consumasse assim mesmo, o personagem morreria com o dono dele offline.
		{
			(ServerPlayer dono, ServerPlayer alvo, AccountSave acc, double bp) = Dupla();

			AbsorverAteACena(dono, alvo);
			AfirmarFd("J60 aceita, a cena esta rodando", _emCenaDeFusao.ContainsKey(dono.Id));

			SoltarDaFusao(alvo.Id);          // o que o `Drop` chama
			TickDaCenaDeFusao();             // e o tique de producao passa por cima

			AfirmarFd("J60b ele fecha o jogo no meio: a cena cai e NAO ha consumacao",
					  !_emCenaDeFusao.ContainsKey(dono.Id) && !_emCenaDeFusao.ContainsKey(alvo.Id));
			NadaAconteceu("J61 (desconectou entre o aceite e a consumacao)", dono, alvo, acc, bp);
		}
	}

	/// <summary>
	/// A REGUA DE "NADA ACONTECEU" -- e ela devolve O QUE mudou, e nao um `bool`.
	///
	/// Sete perguntas: convite na mesa, cena aberta, alguem fundido, o personagem apagado, o corpo
	/// marcado como consumido, o BP andado e a recarga cobrada. Uma afirmacao composta que so sabe
	/// dizer "false" e uma afirmacao que ninguem consegue consertar as duas da manha.
	/// </summary>
	private string OQueMudouNaAbsorcao(ServerPlayer dono, ServerPlayer alvo, AccountSave acc,
									   double bpAntes, bool conviteDePe = false)
	{
		var m = new List<string>();
		if (!conviteDePe && _pedidosDeFusao.ContainsKey(alvo.Id)) m.Add("ficou convite na mesa");
		if (conviteDePe && !_pedidosDeFusao.ContainsKey(alvo.Id)) m.Add("**DERRUBOU o convite alheio**");
		if (_emCenaDeFusao.ContainsKey(dono.Id) || _emCenaDeFusao.ContainsKey(alvo.Id)) m.Add("abriu cena");
		if (EstaFundido(dono.Id) || EstaFundido(alvo.Id)) m.Add("fundiu");
		if (alvo.Slot >= 0 && alvo.Slot < acc.Slots.Length && acc.Slots[alvo.Slot] == null)
			m.Add("**APAGOU O PERSONAGEM**");
		if (alvo.PersonagemConsumido) m.Add("marcou o corpo como consumido");
		if (Math.Abs(dono.Ficha.BP - bpAntes) >= 1.0) m.Add($"o BP andou ({bpAntes:N0} -> {dono.Ficha.BP:N0})");
		if (dono.Ficha.fusion_cooldown_until > NowMs()) m.Add("cobrou a recarga de 1 h");
		return string.Join("; ", m);
	}

	/// <summary>Um convite de ABSORCAO que nao deixa pendente na mesa. So pra ler o sim/nao.</summary>
	private bool ConviteDeAbsorcaoEntra(ServerPlayer a, ServerPlayer b)
	{
		_pedidosDeFusao.Remove(b.Id);
		Convidar(a, b, TipoDeFusao.Namek);
		bool entrou = _pedidosDeFusao.ContainsKey(b.Id);
		_pedidosDeFusao.Remove(b.Id);
		return entrou;
	}

	/// <summary>O encanamento que SO a absorcao usa -- ver a J46 e a J47.</summary>
	private static readonly string[] OEncanamentoDaAbsorcao =
		["AbsorverNamekuseijin", "ApagarOPersonagemParaSempre", "ResponderAoConviteDeAbsorcao",
		 "ConvidarParaAFusaoNamekuseijin", "fus_namek"];

	private static bool SemAbsorcaoNestasLinhas(IEnumerable<string> linhas) =>
		!linhas.Any(l => OEncanamentoDaAbsorcao.Any(p => l.Contains(p, StringComparison.Ordinal)));

	private static string PrimeiraLinhaComAbsorcao(IEnumerable<string> linhas) =>
		linhas.FirstOrDefault(l => OEncanamentoDaAbsorcao.Any(p => l.Contains(p, StringComparison.Ordinal)))
			?.Trim() ?? "";

	/// <summary>Convida e ACEITA (as duas confirmacoes), e para NA CENA -- sem virar.</summary>
	private void AbsorverAteACena(ServerPlayer dono, ServerPlayer alvo)
	{
		Convidar(dono, alvo, TipoDeFusao.Namek);
		if (!_pedidosDeFusao.ContainsKey(alvo.Id)) return;
		ResponderAoConviteDeAbsorcao(alvo);
		_confirmacoesDeAbsorcao[alvo.Id] =
			NowMs() - (long)(AbsorcaoNamekuseijin.SegundosEntreAsConfirmacoes * 1000) - 1;
		ResponderAoConviteDeAbsorcao(alvo);
	}

	// =====================================================================
	// J^^. O PERSONAGEM PERDIDO **ATRAVESSA O DISCO** -- gravar, reabrir, conferir
	// =====================================================================
	/// <summary>
	/// ============================ "O SLOT FICOU NULO" E MEMORIA RAM ============================
	/// A secao J'' prova que o objeto `AccountSave` na mao do servidor perdeu o personagem. Isso nao e
	/// o que o dono pediu: ele pediu *"perde o personagem pra sempre"*, e "pra sempre" mora no DISCO.
	/// Entre o objeto e o arquivo ha o `_store.Gravar`, e entre o arquivo e a proxima sessao ha o
	/// `_store.Carregar`, o `SlotsVisiveisDe` da tela de selecao e o `PickSlot`.
	///
	/// Esta secao faz a volta inteira **pelo codigo de producao**: grava de verdade, reabre de verdade,
	/// e pergunta a tela de selecao de verdade. O precedente e a secao L' logo abaixo (a recarga que
	/// atravessa o disco), que existe pelo mesmo argumento.
	///
	/// ============================ E ELA COBRA AS TRES METADES DO PEDIDO ============================
	///   1. **nao volta** -- o slot esta vazio no ARQUIVO, e some da tela de selecao;
	///   2. **a conta continua** -- o arquivo existe, nao esta banida, e o OUTRO personagem dela nao
	///      foi tocado (o dono nunca pediu que a pessoa perdesse a conta);
	///   3. **ela cria outro** -- um personagem novo entra no slot que vagou, atravessa o disco, e o
	///      corpo consumido que ainda esta de pe **nao o sobrescreve** no salvamento seguinte.
	///
	/// A terceira e a que mais tinha como dar errado: o `ServerPlayer` do absorvido continua no mundo
	/// ate o `Disconnect` chegar, e o salvamento periodico roda a cada 2 minutos.
	/// ==========================================================================================
	/// </summary>
	private void OPersonagemPerdidoAtravessaODisco()
	{
		GD.Print("[fusao2] -- J^^) o personagem perdido atravessa o DISCO: gravar, reabrir, conferir --");

		using PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();

		if (_store == null) { AfirmarFd("J62 ha armazenamento pra gravar", false); return; }

		// ESTA SECAO PRECISA DE DOIS SLOTS na mesma conta (o comido e o irmao que sobrevive), e
		// `AccountStore.Slots` e uma CONSTANTE de compilacao -- conferi-la em tempo de execucao seria
		// codigo que o compilador ja sabe que nunca roda (e ele avisa). Se um dia a constante cair pra
		// 1, o `slotDoIrmao` abaixo estoura na cara de quem mudou, que e o aviso certo.
		ServerPlayer dono = ForjarNaFusaoDupla("Piccolo8", "Namekian", 2_000_000, false, 0);
		ServerPlayer comido = ForjarNaFusaoDupla("Nail8", "Namekian", 600_000, false, 1);
		ServerPlayer irmao = ForjarNaFusaoDupla("IrmaoDeNail8", "Namekian", 111_111, false, 1);

		// A CONTA COM **DOIS** PERSONAGENS: o que vai ser comido no slot dele e um irmao no outro.
		// Sem o irmao, "a conta continua de pe" seria uma frase sobre um objeto vazio.
		AccountSave acc = palco.Emprestar(comido.Conta, comido.Slot, AccountStore.DeJogador(comido, NowMs()));
		int slotDoIrmao = comido.Slot == 0 ? 1 : 0;
		acc.Slots[slotDoIrmao] = AccountStore.DeJogador(irmao, NowMs());

		// O DISCIPULADO DELE, pra a reciclagem da assinatura ter o que limpar.
		string sig = ServerPlayer.AssinaturaDe(comido.Conta, comido.Slot);
		_mestreDe[sig] = "9999999999";

		// ---- O ANTES, NO ARQUIVO ----
		_store.Gravar(acc);
		string arquivo = System.IO.Path.Combine(_store.Pasta, comido.Conta + ".json");
		AfirmarFd("J62 o personagem foi pro disco antes de tudo (a conta existe como ARQUIVO)",
				  System.IO.File.Exists(arquivo)
				  && _store.Carregar(comido.Conta)?.Slots[comido.Slot] != null, arquivo);

		Jandirus.Net.SlotInfo[] antes = SlotsVisiveisDe(_store.Carregar(comido.Conta)!);
		AfirmarFd("J63 ...e a TELA DE SELECAO o mostra (o mesmo `SlotsVisiveisDe` que o cliente recebe)",
				  antes[comido.Slot].Ocupado
				  && string.Equals(antes[comido.Slot].Nome, comido.Name, StringComparison.OrdinalIgnoreCase),
				  antes[comido.Slot].Nome);

		AbsorverDeVerdade(dono, comido);

		// ---- O DEPOIS, NO MESMO ARQUIVO ----
		AccountSave? doDisco = _store.Carregar(comido.Conta);
		AfirmarFd("J64 a CONTA continua no disco depois da absorcao (nao e ela que se perde)",
				  doDisco != null && !doDisco.Banida, doDisco == null ? "sumiu" : "de pe");
		if (doDisco == null) return;

		AfirmarFd("J65 **o personagem NAO esta mais no arquivo** -- e o que o `PickSlot` le pra recusar "
				+ "com \"esse slot esta vazio\"",
				  doDisco.Slots[comido.Slot] == null);

		Jandirus.Net.SlotInfo[] depois = SlotsVisiveisDe(doDisco);
		AfirmarFd("J66 ...e ele sumiu da TELA DE SELECAO (slot vazio, sem nome)",
				  !depois[comido.Slot].Ocupado && depois[comido.Slot].Nome.Length == 0,
				  $"ocupado={depois[comido.Slot].Ocupado} nome='{depois[comido.Slot].Nome}'");

		AfirmarFd("J67 ...e o OUTRO personagem da mesma conta continua intacto no disco e na tela "
				+ "(morre o personagem, nao a conta)",
				  doDisco.Slots[slotDoIrmao] != null && depois[slotDoIrmao].Ocupado
				  && string.Equals(depois[slotDoIrmao].Nome, irmao.Name, StringComparison.OrdinalIgnoreCase),
				  depois[slotDoIrmao].Nome);

		AfirmarFd("J68 ...e a ASSINATURA foi reciclada (o `mst_purge_sig`: o proximo personagem deste "
				+ "slot nao herda o mestre do absorvido)",
				  MestreDe(sig).Length == 0, MestreDe(sig));

		// ---- A CONTA CRIA OUTRO, E ELE ATRAVESSA O DISCO ----
		ServerPlayer novo = ForjarNaFusaoDupla("GenteNova", "Saiyan", 5_000, false, 1);
		doDisco.Slots[comido.Slot] = AccountStore.DeJogador(novo, NowMs());
		_store.Gravar(doDisco);

		AccountSave? comONovo = _store.Carregar(comido.Conta);
		AfirmarFd("J69 **a conta cria outro personagem no slot que vagou**, e ele atravessa o disco",
				  comONovo?.Slots[comido.Slot] != null
				  && string.Equals(comONovo!.Slots[comido.Slot]!.Nome, novo.Name, StringComparison.OrdinalIgnoreCase),
				  comONovo?.Slots[comido.Slot]?.Nome ?? "vazio");
		AfirmarFd("J70 ...e ele e OUTRA pessoa, e nao o absorvido de volta (nome, raca e poder)",
				  comONovo?.Slots[comido.Slot] is { } n
				  && !string.Equals(n.Nome, comido.Name, StringComparison.OrdinalIgnoreCase)
				  && n.Raca != comido.Race && Math.Abs(n.Ficha.BP - 600_000) > 1.0);

		// ============================ E O CORPO CONSUMIDO NAO SOBRESCREVE O NOVO ============================
		// O `ServerPlayer` do absorvido continua de pe ate o `Disconnect` chegar, e o salvamento
		// periodico roda a cada 2 minutos. Sem o `PersonagemConsumido`, esta gravacao poria o MORTO por
		// cima do personagem novo -- ou seja, apagaria o personagem de quem nao fez nada.
		// ==================================================================================================
		Persistir(comido, comONovo!);
		AccountSave? depoisDoTique = _store.Carregar(comido.Conta);
		AfirmarFd("J71 ...e o salvamento periodico do corpo consumido NAO o sobrescreve "
				+ "(o `PersonagemConsumido` fecha a porta do save)",
				  depoisDoTique?.Slots[comido.Slot] is { } d
				  && string.Equals(d.Nome, novo.Name, StringComparison.OrdinalIgnoreCase),
				  depoisDoTique?.Slots[comido.Slot]?.Nome ?? "vazio");

		// ---- O CONTRA-EXEMPLO, PELO MESMO CAMINHO ----
		// Sem a marca, a MESMA gravacao ressuscita o absorvido por cima do personagem novo. E o
		// defeito exato que a marca existe pra impedir, e ele e reproduzido no unico lugar que
		// importa -- o campo -- com a rodada refeita pelo mesmo `Persistir`/`Carregar`.
		comido.PersonagemConsumido = false;
		Persistir(comido, depoisDoTique!);
		CharacterSave? ressuscitado = _store.Carregar(comido.Conta)?.Slots[comido.Slot];
		AfirmarFd("   DEFEITO INJETADO (a marca `PersonagemConsumido` apagada): o MESMO criterio da J71 "
				+ "REPROVA -- o absorvido volta POR CIMA do personagem novo",
				  ressuscitado != null
				  && string.Equals(ressuscitado.Nome, comido.Name, StringComparison.OrdinalIgnoreCase),
				  ressuscitado?.Nome ?? "vazio");

		comido.PersonagemConsumido = true;
		_mestreDe.Remove(sig);
		dono.Ficha.fusion_cooldown_until = 0;
	}

	// =====================================================================
	// J^^^. JOGADOR **CONTRA** NPC, LADO A LADO -- as duas metades da regra N4
	// =====================================================================
	/// <summary>
	/// ============================ "GANHA BEM MENOS" SO QUER DIZER ALGO AO LADO DE UM "GANHA MUITO" ============================
	/// A J''' mede o NPC contra a conta do Core. Esta mede o NPC contra **o mesmo alvo absorvido como
	/// JOGADOR, na mesma rodada, por um absorvedor identico** -- e imprime os dois numeros na mesma
	/// linha. E a diferenca entre "o NPC rende 40.000" (que nao diz nada) e "o NPC rende 40.000 onde o
	/// jogador renderia 2.800.000", que e a frase do dono em numero.
	///
	/// As duas metades sao cobradas juntas por um motivo que a memoria deste projeto ja catalogou: uma
	/// bancada que so mede o caso pequeno fica verde num mundo em que **os dois** casos sao pequenos.
	/// ================================================================================================================
	/// </summary>
	private void JogadorContraNpcLadoALado()
	{
		GD.Print("[fusao2] -- J^^^) jogador x NPC, os dois numeros na mesma linha --");

		using PalcoDeApagamentos palco = PalcoDeApagamentosDeBancada();

		const double BpDoAbsorvedor = 2_000_000, BpDoAlvo = 400_000;
		const string SkillDele = "/datum/skill/namek/regeneration";

		// DOIS ABSORVEDORES IDENTICOS: mesmo BP, mesmo stat, mesma gravidade dominada, livro vazio.
		// Se eles nao fossem iguais, a comparacao mediria a diferenca entre eles.
		ServerPlayer comeGente = ForjarNaFusaoDupla("PiccoloG", "Namekian", BpDoAbsorvedor, false, 0);
		ServerPlayer comeNpc = ForjarNaFusaoDupla("PiccoloN", "Namekian", BpDoAbsorvedor, false, 0);
		foreach (ServerPlayer p in new[] { comeGente, comeNpc })
		{
			p.Ficha.physoff = 30;
			p.Ficha.GravMastered = 10;
		}

		// E DOIS ALVOS IDENTICOS -- so que um e gente e o outro nao.
		ServerPlayer gente = ForjarNaFusaoDupla("NailG", "Namekian", BpDoAlvo, false, 1);
		ServerPlayer npc = ForjarNaFusaoDupla("NailN", "Namekian", BpDoAlvo, false, 1);
		foreach (ServerPlayer p in new[] { gente, npc })
		{
			p.Ficha.physoff = 40;
			p.Ficha.GravMastered = 25;
			p.Livro.Dar(SkillDele);
		}
		npc.Papel = new Jandirus.Core.Npc.PapelDeNpc(
			new Jandirus.Core.Npc.MoldeDeNpc { Id = "bancada", Nome = "aldeao" }, 0);

		AfirmarFd("J72 os dois alvos sao gemeos, e so um deles e gente "
				+ "(senao a comparacao mediria a diferenca entre eles)",
				  Math.Abs(gente.Ficha.BP - npc.Ficha.BP) < 1.0 && EhPessoa(gente) && !EhPessoa(npc)
				  && EhNamekNpcAbsorvivel(npc));

		// ---- O JOGADOR, PELA CORRENTE INTEIRA ----
		palco.Emprestar(gente.Conta, gente.Slot, AccountStore.DeJogador(gente, NowMs()));
		AbsorverDeVerdade(comeGente, gente);
		double ganhoDeGente = comeGente.Ficha.BP - BpDoAbsorvedor;

		// ---- O NPC, PELO VERB (que e o caminho dele: sem convite, o gesto de quem absorve basta) ----
		var canto = new Vec2(0, 80 * ZoneCollision.TileSize);
		comeNpc.Pos = canto;
		npc.Pos = canto + new Vec2(ZoneCollision.TileSize, 0);
		comeNpc.Facing = Jandirus.Core.World.Facing.East;
		ConvidarParaAFusaoNamekuseijin(comeNpc);
		if (_emCenaDeFusao.GetValueOrDefault(comeNpc.Id) is { } c)
		{
			c.Funde = NowMs() - 1;
			TickDaCenaDeFusao();
			c.Acaba = NowMs() - 1;
			TickDaCenaDeFusao();
		}
		double ganhoDeNpc = comeNpc.Ficha.BP - BpDoAbsorvedor;

		// ---- OS DOIS NUMEROS, NA MESMA LINHA ----
		GD.Print($"[fusao2]   N2 x N4, o MESMO alvo de {BpDoAlvo:N0} de BP absorvido por dois "
			   + $"Namekuseijin identicos de {BpDoAbsorvedor:N0}:");
		GD.Print($"[fusao2]     como JOGADOR : +{ganhoDeGente:N0}  (BP final {comeGente.Ficha.BP:N0})  "
			   + $"stats SIM, skills SIM, Super Namekuseijin SIM");
		GD.Print($"[fusao2]     como NPC     : +{ganhoDeNpc:N0}  (BP final {comeNpc.Ficha.BP:N0})  "
			   + $"stats nao, skills nao, Super Namekuseijin NAO");
		GD.Print($"[fusao2]     o jogador rende {(ganhoDeNpc > 0 ? ganhoDeGente / ganhoDeNpc : 0):N0}x "
			   + "o que o NPC rende.");

		AfirmarFd($"J73 **o jogador rende MUITO mais que o NPC** -- +{ganhoDeGente:N0} contra "
				+ $"+{ganhoDeNpc:N0} pelo MESMO alvo (razao {(ganhoDeNpc > 0 ? ganhoDeGente / ganhoDeNpc : 0):N0}x)",
				  ganhoDeNpc > 0 && ganhoDeGente > ganhoDeNpc * 20);

		AfirmarFd("J74 ...e o NPC rende alguma coisa (o \"bem menos\" nao virou \"nada\", que seria "
				+ "outra regra)",
				  ganhoDeNpc > 0, $"+{ganhoDeNpc:N0}");

		// ---- E AS **DUAS METADES** DO SUPER NAMEKUSEIJIN, MEDIDAS JUNTAS ----
		bool formaPeloJogador = comeGente.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin);
		bool formaPeloNpc = comeNpc.Livro.Sabe(AbsorcaoNamekuseijin.PathDaSkillDoSuperNamekuseijin);
		AfirmarFd("J75 **o jogador DA o Super Namekuseijin e o NPC NAO** -- as duas metades da frase "
				+ "do dono, no mesmo mundo e na mesma rodada",
				  formaPeloJogador && !formaPeloNpc,
				  $"jogador {formaPeloJogador}, NPC {formaPeloNpc}");

		// E A FORMA ABRE MESMO PRA UM E NAO PRA O OUTRO -- o `Avaliar` de producao, e nao o livro.
		Jandirus.Core.Forms.RecusaForma rG = comeGente.Forma.Avaliar(
			IdDoSuperNamekuseijin, comeGente.Ficha.BP, 1, false, Perfil(comeGente));
		Jandirus.Core.Forms.RecusaForma rN = comeNpc.Forma.Avaliar(
			IdDoSuperNamekuseijin, comeNpc.Ficha.BP, 1, false, Perfil(comeNpc));
		AfirmarFd("J76 ...e o portao da FORMA concorda: abre pra quem comeu gente, recusa por SEM "
				+ "HABILIDADE pra quem comeu NPC",
				  rG == Jandirus.Core.Forms.RecusaForma.Pode
				  && rN == Jandirus.Core.Forms.RecusaForma.SemHabilidade, $"{rG} x {rN}");

		AfirmarFd("J77 os OUTROS bonus seguem a mesma divisao: stat e skill atravessam do jogador "
				+ "(30 -> 40, gravidade 10 -> 25) e NADA atravessa do NPC",
				  Math.Abs(comeGente.Ficha.physoff - 40) < 1e-6
				  && Math.Abs(comeGente.Ficha.GravMastered - 25) < 1e-6
				  && comeGente.Livro.Sabe(SkillDele)
				  && Math.Abs(comeNpc.Ficha.physoff - 30) < 1e-6
				  && Math.Abs(comeNpc.Ficha.GravMastered - 10) < 1e-6
				  && !comeNpc.Livro.Sabe(SkillDele),
				  $"gente {comeGente.Ficha.physoff}/{comeGente.Ficha.GravMastered}, "
				+ $"npc {comeNpc.Ficha.physoff}/{comeNpc.Ficha.GravMastered}");

		comeGente.Ficha.fusion_cooldown_until = 0;
		comeNpc.Ficha.fusion_cooldown_until = 0;
	}

	/// <summary>
	/// A ABSORCAO PELA CORRENTE INTEIRA DE PRODUCAO -- convite, duas confirmacoes, cena e virada.
	///
	/// O unico atalho e o RELOGIO (o carimbo da primeira confirmacao e o instante da virada), que e a
	/// mesma disciplina do <see cref="FundirDeVerdade"/> logo acima: nada aqui chama
	/// `AbsorverNamekuseijin` na mao.
	/// </summary>
	private void AbsorverDeVerdade(ServerPlayer dono, ServerPlayer alvo)
	{
		Convidar(dono, alvo, TipoDeFusao.Namek);
		if (!_pedidosDeFusao.ContainsKey(alvo.Id)) return;

		ResponderAoConviteDeAbsorcao(alvo);
		_confirmacoesDeAbsorcao[alvo.Id] =
			NowMs() - (long)(AbsorcaoNamekuseijin.SegundosEntreAsConfirmacoes * 1000) - 1;
		ResponderAoConviteDeAbsorcao(alvo);

		if (_emCenaDeFusao.GetValueOrDefault(dono.Id) is { } c)
		{
			c.Funde = NowMs() - 1;
			TickDaCenaDeFusao();
			c.Acaba = NowMs() - 1;
			TickDaCenaDeFusao();
		}
	}

	// =====================================================================
	// K. O CORPO INTEIRO AO FUNDIR -- e a amputacao de volta na saida
	// =====================================================================
	/// <summary>
	/// `fusion_fresh_body` / `fusion_snapshot_lopped` / `fusion_restore_lopped` (`Fusion.dm:36-53`),
	/// chamados em `:276-277` e `:318`.
	///
	/// ============================ ELA MEDE OS DOIS SENTIDOS, PORQUE SO UM E O EXPLOIT ============================
	/// Portar so o `fresh_body` faria da fusao a cura de membro mais barata do jogo -- funde, separa,
	/// braco de volta. O comentario do proprio DM chama isso pelo nome (`:41`, *"no free regen
	/// exploit"*). Entao sao duas afirmacoes e nao uma: **a fusao nasce inteira** E **a amputacao
	/// volta na separacao**.
	/// ========================================================================================================
	/// </summary>
	private void OCorpoInteiroAoFundir()
	{
		GD.Print("[fusao2] -- K) o corpo da fusao: inteiro ao nascer, amputado ao se partir --");

		ServerPlayer a = ForjarNaFusaoDupla("Yamcha2", "Saiyan", 1_000_000, sabeDancar: true, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Tenshin2", "Saiyan", 900_000, sabeDancar: true, 1);

		const string Membro = "Braco direito";

		Jandirus.Core.Combat.BodyPart? braco = a.Combate.Corpo.Achar(Membro);
		AfirmarFd("K0 o corpo forjado tem o membro que esta secao mexe", braco != null, Membro);
		if (braco == null) return;
		braco.Decepado = true;
		braco.Vida = 0;
		a.Combate.SincronizarVida();

		AfirmarFd("K1 quem vai fundir esta SEM o braco", a.Combate.Corpo.Achar(Membro)!.Decepado);

		FusaoAtiva? f = FundirDeVerdade(a, b, TipoDeFusao.Danca);
		AfirmarFd("K2 a fusao aconteceu", f != null && EstaFundido(a.Id));
		if (f == null) return;

		// ---- K3-K4: A FUSAO E UMA PESSOA NOVA ----
		AfirmarFd("K3 a fusao nasce com o CORPO INTEIRO (`fusion_fresh_body`, `Fusion.dm:36-39`)",
				  !a.Combate.Corpo.Achar(Membro)!.Decepado);
		AfirmarFd("K4 ...e a foto do que faltava ficou guardada (`KeeperLoppedTypes`, `Fusion.dm:108`)",
				  f.MembrosQueFaltavam.Contains(Membro),
				  string.Join(", ", f.MembrosQueFaltavam));

		// ---- K5-K6: E A SAIDA COBRA DE VOLTA ----
		Separar(f, "fim da secao K");
		AfirmarFd("K5 separada, o braco volta a FALTAR (`fusion_restore_lopped`, `Fusion.dm:318`)",
				  a.Combate.Corpo.Achar(Membro)!.Decepado);
		AfirmarFd("K6 ...e ele volta ZERADO, e nao meio curado (`health = 0`, `Fusion.dm:51`)",
				  a.Combate.Corpo.Achar(Membro)!.Vida <= 0,
				  $"{a.Combate.Corpo.Achar(Membro)!.Vida:0.##}");

		// ============================ K7: O EXPLOIT, INJETADO ============================
		// O defeito plausivel e o de quem porta so metade: apagar a lista fotografada (ou nunca
		// escreve-la) e chamar so o `fresh_body`. O criterio e o MESMO da K5, e ele TEM que reprovar --
		// senao a K5 esta verde por acaso e a fusao e cura de graca.
		//
		// A RECARGA E ZERADA PRA A DUPLA PODER FUNDIR DE NOVO, e e a mesma concessao que a secao G faz:
		// esperar uma hora de relogio de parede nao e uma bancada. A recarga em si e medida na secao L.
		// ================================================================================
		a.Ficha.fusion_cooldown_until = 0;
		b.Ficha.fusion_cooldown_until = 0;
		FusaoAtiva? f2 = FundirDeVerdade(a, b, TipoDeFusao.Danca);
		AfirmarFd("K7 a dupla fundiu de novo (pra o defeito ter onde ser injetado)", f2 != null);
		if (f2 == null) return;

		f2.MembrosQueFaltavam = [];   // o defeito: a foto se perdeu
		Separar(f2, "fim da injecao da secao K");
		AfirmarFd("   DEFEITO INJETADO (a foto dos membros se perde): o MESMO criterio da K5 REPROVA",
				  !a.Combate.Corpo.Achar(Membro)!.Decepado,
				  "a fusao virou cura de membro de graca -- o `no free regen exploit` do `Fusion.dm:41`");

		// E O MUNDO FICA COMO ESTAVA: a injecao curou o braco de verdade, entao ela o tira de novo.
		// Bancada que deixa estrago no mundo e bancada que envenena a proxima secao.
		if (a.Combate.Corpo.Achar(Membro) is { } volta) { volta.Decepado = true; volta.Vida = 0; }
		a.Combate.SincronizarVida();
	}

	// =====================================================================
	// L. A RECARGA DE 1 h ATRAVESSA O LOGOUT
	// =====================================================================
	/// <summary>
	/// `fusion_cooldown_until` (`Fusion.dm:28`) -- um `mob/var` **sem `tmp/`**, e por isso ele viaja no
	/// savefile.
	///
	/// ============================ O QUE ESTAVA QUEBRADO ============================
	/// O port guardava a espera num dicionario com o ID DE SESSAO por chave, e a apagava no logout
	/// (id se reusa). Ou seja: **Alt+F4 zerava a recarga dos dois**, e o unico freio do sistema virava
	/// opcional. Agora o carimbo mora na `Fighter`, que o `CharacterSave` serializa inteira.
	///
	/// **E O DISCO ENTRA JUNTO**, na secao L' logo abaixo: esta aqui prova que o carimbo esta na FICHA e
	/// que sair do mundo nao o apaga -- o que e memoria RAM --, e a <see cref="ARecargaAtravessaODisco"/>
	/// leva o mesmo carimbo pelo `Persistir` / `Carregar` / `ParaJogador` de producao e pergunta ao
	/// portao de novo. Uma espera de UMA HORA passa por reinicio de servidor mais vezes do que passa por
	/// sessao, e provar so ate a borda do `_players` seria provar o pedaco facil.
	/// ==========================================================================
	/// </summary>
	private void ARecargaAtravessaOLogout()
	{
		GD.Print("[fusao2] -- L) a recarga de 1 h: ela mora na FICHA e sobrevive ao logout --");

		ServerPlayer a = ForjarNaFusaoDupla("Trunks2", "Saiyan", 1_000_000, sabeDancar: true, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Goten2", "Saiyan", 900_000, sabeDancar: true, 1);

		a.Ficha.fusion_cooldown_until = 0;
		b.Ficha.fusion_cooldown_until = 0;

		FusaoAtiva? f = FundirDeVerdade(a, b, TipoDeFusao.Danca);
		AfirmarFd("L1 a fusao aconteceu", f != null);
		if (f == null) return;

		Separar(f, "fim da fusao da secao L");

		long agora = NowMs();
		AfirmarFd("L2 a recarga foi carimbada NA FICHA dos DOIS (`Fusion.dm:320`,`:334`)",
				  a.Ficha.fusion_cooldown_until > agora && b.Ficha.fusion_cooldown_until > agora,
				  $"{a.Ficha.fusion_cooldown_until - agora} / {b.Ficha.fusion_cooldown_until - agora} ms");

		AfirmarFd("L3 ...e o prazo e a hora do `FUSION_COOLDOWN` (36000 decimos)",
				  Math.Abs(a.Ficha.fusion_cooldown_until - agora - Fusao.RecargaSegundos * 1000) < 2000,
				  $"{(a.Ficha.fusion_cooldown_until - agora) / 60000.0:0.#} min");

		// ============================ L4: O ALT+F4 ============================
		// `SoltarDaFusao` e o que o `Drop` chama quando alguem sai do jogo, e ele APAGAVA a recarga
		// (era a linha `_recargaDeFusao.Remove(id)`). Chamar o mesmo metodo aqui e reproduzir o logout
		// pelo caminho de producao -- e o carimbo tem que sobreviver a ele.
		// ==================================================================
		SoltarDaFusao(a.Id);
		SoltarDaFusao(b.Id);
		AfirmarFd("L4 SAIR DO JOGO nao zera a espera -- era o Alt+F4 que devolvia a fusao de graca",
				  NaRecargaDeFusao(a, NowMs()) && NaRecargaDeFusao(b, NowMs()));

		// ============================ L5: E ELA REALMENTE BARRA ============================
		// O criterio e "o convite NAO entra", e o defeito injetado e o proprio Alt+F4 de antes --
		// zerar o carimbo. Com ele zerado o convite passa e o criterio REPROVA, que e o que prova que
		// era o carimbo barrando, e nao a distancia, a raca ou a skill.
		// ==================================================================================
		long carimboA = a.Ficha.fusion_cooldown_until, carimboB = b.Ficha.fusion_cooldown_until;

		Mutacao(AfirmarFd,
			"L5 com a recarga de pe o convite NAO entra na mesa",
			"a recarga e zerada -- o Alt+F4 que este passe fechou",
			() => !PodeConvidarNaFusaoDupla(a, b),
			() => { a.Ficha.fusion_cooldown_until = 0; b.Ficha.fusion_cooldown_until = 0; },
			() => { a.Ficha.fusion_cooldown_until = carimboA; b.Ficha.fusion_cooldown_until = carimboB; });

		ARecargaAtravessaODisco(a);
	}

	// =====================================================================
	// L'. ...E ATRAVESSA O **DISCO**
	// =====================================================================
	/// <summary>
	/// ============================ "SOBREVIVE AO LOGOUT" NAO E "SOBREVIVE AO DISCO" ============================
	/// A secao L de cima prova que o carimbo mora na `Fighter` e que o <see cref="SoltarDaFusao"/> (o que
	/// o `Drop` chama) nao o apaga. **Isso e memoria RAM.** O defeito que este passe fechou era o de uma
	/// espera que morria com o processo, e uma espera de UMA HORA passa por reinicio de servidor mais
	/// vezes do que passa por sessao: provar so ate a borda do `_players` e provar o pedaco facil.
	///
	/// Aqui a ida e a volta sao o caminho de producao inteiro -- o mesmo <see cref="Persistir"/> que o
	/// jogo chama, o mesmo `_store.Carregar` do login e o mesmo `AccountStore.ParaJogador` que monta o
	/// corpo de quem entra. Nada de ler o JSON na mao: o que se quer saber e se o CAMINHO leva o
	/// carimbo, e nao se o arquivo tem a palavra.
	///
	/// O precedente e a familia 8 da `--mestrevivo` ("o relogin: o que atravessa o disco"), que existe
	/// exatamente porque quatro estados diferentes sumiam sozinhos e calados entre uma sessao e outra.
	/// ======================================================================================================
	/// </summary>
	private void ARecargaAtravessaODisco(ServerPlayer a)
	{
		GD.Print("[fusao2] -- L') ...e ela atravessa o DISCO: gravar, reabrir, conferir --");

		if (_store == null) { AfirmarFd("L6 ha armazenamento pra gravar (o `--server` abriu a pasta)", false); return; }

		long carimbo = a.Ficha.fusion_cooldown_until;
		AfirmarFd("L6 antes de gravar, a recarga esta de pe na ficha", carimbo > NowMs(),
				  $"faltam {(carimbo - NowMs()) / 60000.0:0.#} min");

		// A CONTA NASCE AQUI porque o corpo e FORJADO (`new ServerPlayer`, sem peer e sem login), e o
		// `Persistir` acha a conta pelo `Peer`. E a mesma ponte que a `--mestrevivo` faz no
		// `PersistirNaBancada`: a conta vem de fora, o resto e a MESMA funcao.
		var conta = new AccountSave { Conta = a.Conta, CriadaEm = NowMs() };
		Persistir(a, conta);

		AccountSave? doDisco = _store.Carregar(a.Conta);
		AfirmarFd("L7 o personagem foi pro disco pelo `Persistir` de producao",
				  doDisco?.Slots[a.Slot] != null,
				  System.IO.Path.Combine(_store.Pasta, a.Conta + ".json"));
		if (doDisco?.Slots[a.Slot] is not { } fichaDoDisco) return;

		AfirmarFd("L8 **e a recarga estava no arquivo** -- o `fusion_cooldown_until` viaja no `Fighter` "
				+ "inteiro que o `CharacterSave` serializa (`Fusion.dm:28`, um `mob/var` SEM `tmp/`)",
				  fichaDoDisco.Ficha.fusion_cooldown_until == carimbo,
				  $"{fichaDoDisco.Ficha.fusion_cooldown_until} contra {carimbo}");

		// ============================ E O CORPO QUE VOLTA E BARRADO DE VERDADE ============================
		// Ler o campo do save prova que o numero atravessou. Nao prova que o JOGO o usa: entre o JSON e o
		// portao ha o `ParaJogador`, e este projeto ja pagou pra aprender que "escrever o corte nao e
		// aplicar o corte" (a memoria do sigilo do BP, com uma API inteira orfa). Entao o corpo e
		// remontado como o login remonta e a pergunta e feita ao `NaRecargaDeFusao` de producao.
		// ==============================================================================================
		var voltou = new ServerPlayer { Id = IdBaseDaFusaoDupla + 900, Peer = null, Conta = a.Conta, Slot = a.Slot };
		AccountStore.ParaJogador(fichaDoDisco, voltou);

		AfirmarFd("L9 ...e o corpo REMONTADO do disco continua na recarga (o portao le a ficha que voltou)",
				  NaRecargaDeFusao(voltou, NowMs()),
				  $"faltam {(voltou.Ficha.fusion_cooldown_until - NowMs()) / 60000.0:0.#} min");

		// ============================ O CONTRA-EXEMPLO, PELO MESMO CAMINHO ============================
		// O defeito e o que existia antes deste passe: a espera vivia num dicionario por id de SESSAO e
		// era apagada na saida. Aqui isso e reproduzido no unico lugar que importa -- o carimbo zerado
		// ANTES de gravar --, e a rodada inteira e refeita pelo mesmo `Persistir` / `Carregar` /
		// `ParaJogador`. Se a L9 ficasse verde tambem assim, ela nao estaria medindo o disco.
		// ==========================================================================================
		a.Ficha.fusion_cooldown_until = 0;
		Persistir(a, conta);
		CharacterSave? zerada = _store.Carregar(a.Conta)?.Slots[a.Slot];
		var reZerado = new ServerPlayer { Id = IdBaseDaFusaoDupla + 901, Peer = null, Conta = a.Conta, Slot = a.Slot };
		if (zerada != null) AccountStore.ParaJogador(zerada, reZerado);

		AfirmarFd("   DEFEITO INJETADO (a recarga e apagada antes de gravar, que era o Alt+F4 de antes): "
				+ "o MESMO criterio da L9 REPROVA",
				  zerada != null && !NaRecargaDeFusao(reZerado, NowMs()),
				  "o disco devolveu um corpo livre pra fundir de novo");

		// E O MUNDO VOLTA: o carimbo na memoria e o arquivo que esta bancada criou. O `finally` do
		// runner nao alcanca o disco, e deixar uma conta de bancada na pasta do jogo do dono e lixo.
		a.Ficha.fusion_cooldown_until = carimbo;
		Persistir(a, conta);
		AfirmarFd("L10 ...e desfeito o defeito o disco volta a barrar (era a causa, e nao um estrago que ficou)",
				  _store.Carregar(a.Conta)?.Slots[a.Slot]?.Ficha.fusion_cooldown_until == carimbo);

		try
		{
			string arquivo = System.IO.Path.Combine(_store.Pasta, a.Conta + ".json");
			if (System.IO.File.Exists(arquivo)) System.IO.File.Delete(arquivo);
			AfirmarFd("L11 ...e a conta de bancada saiu da pasta (bancada nao deixa conta no jogo do dono)",
					  _store.Carregar(a.Conta) == null);
		}
		catch (Exception e) { AfirmarFd("L11 ...e a conta de bancada saiu da pasta", false, e.Message); }
	}

	/// <summary>Um convite de Danca que NAO deixa pendente na mesa. So pra ler o sim/nao.</summary>
	private bool PodeConvidarNaFusaoDupla(ServerPlayer a, ServerPlayer b)
	{
		_pedidosDeFusao.Remove(b.Id);
		Convidar(a, b, TipoDeFusao.Danca);
		bool entrou = _pedidosDeFusao.ContainsKey(b.Id);
		_pedidosDeFusao.Remove(b.Id);
		return entrou;
	}

	// =====================================================================
	// M. O PUXAO DA POTARA -- *"eles sao puxados um pro lado do outro"*
	// =====================================================================
	/// <summary>
	/// A ETAPA QUE NAO EXISTIA: entre o "sim" e a cinematica, os dois ANDAM um pro outro.
	///
	/// ============================ O QUE ESTA SENDO MEDIDO, E CONTRA O QUE ============================
	/// O dono, literal: *"na potara quando ela comecar eles sao puxados um pro lado do outro e QUANDO SE
	/// ENCOSTAREM a cinematica comeca"*. Sao TRES afirmacoes encadeadas, e cada uma tem prova propria
	/// aqui, porque cada uma falha de um jeito diferente:
	///
	///   1. **puxa** -- os dois andam, e os DOIS (`step_to` nos dois, `Potara_Fusion.dm:125` e `:127`);
	///   2. **eles nao dirigem** -- `AlterInputDisabled(1)` nos dois (`:122-123`), que aqui e o funil de
	///      vetor recusando o passo e o bit que manda o cliente parar de integrar tecla;
	///   3. **a cena so comeca quando encostam** -- e nao num prazo. Enquanto nao encostam, NADA existe:
	///      nem cena, nem fusao, nem poder somado.
	///
	/// E a quarta, que o original **nao tem** e sem a qual o porte seria pior que a ausencia: o `while`
	/// do `Potara_Fusion.dm:124` nao tem saida nenhuma -- com uma parede no meio ele gira pra sempre com
	/// o input dos dois desligado. Ver <see cref="Fusao.SegundosSemAproximarParaDesistir"/> e a M9.
	///
	/// ============================ O PALCO E ESCOLHIDO NO MAPA, E NAO CRAVADO ============================
	/// As outras secoes nunca movem corpo, entao nunca precisaram saber onde estavam. Esta move, e a
	/// parede manda mais que o puxao (`AndarNoPuxao`): plantados no `(0,0)` de um planeta -- que e a
	/// QUINA do mapa, e quina e borda do mundo -- os dois nao andariam um pixel e todas as provas
	/// ficariam vermelhas por um motivo que nao tem nada a ver com a fusao. Ver
	/// <see cref="UmCorredorLivreParaOPuxao"/>.
	/// ==============================================================================================
	/// </summary>
	private void OPuxaoDaPotara()
	{
		GD.Print("[fusao2] -- M) o puxao da Potara: os dois andam, e a cena so comeca quando encostam --");

		const int TilesDeDistancia = 6;
		Vec2 corredor = UmCorredorLivreParaOPuxao(TilesDeDistancia, _fdZona);

		ServerPlayer a = ForjarNaFusaoDupla("Shin3", "Kai", 3_000_000, sabeDancar: false, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Kibito3", "Kai", 2_900_000, sabeDancar: false, 1);
		a.Pos = corredor;
		b.Pos = corredor + new Vec2(TilesDeDistancia * ZoneCollision.TileSize, 0);

		// ---- M1: LONGE, A POTARA AINDA CONVIDA (e a Danca nao convidaria) ----
		// Os 20 tiles do `oview(usr,20)` (`Potara_Fusion.dm:96`) contra o tile ao lado da Metamoro. E o
		// que faz o puxao ter o que fechar: cobrar proximidade pra depois aproximar nao faz sentido.
		AfirmarFd($"M1 a {TilesDeDistancia} tiles a POTARA convida (o `oview(20)` do DU)",
				  ConviteDePotara(a, b));
		AfirmarFd("M1b ...e a DANCA nao, na mesma distancia (ela cobra o tile ao lado)",
				  !PodeConvidarNaFusaoDupla(a, b));

		// ---- M2: O "SIM" ABRE O PUXAO, E NAO A CENA ----
		Convidar(a, b, TipoDeFusao.Potara);
		ResponderAoConvite(b, aceitou: true);

		AfirmarFd("M2 o 'sim' abre o PUXAO (e nao a cinematica)",
				  _sendoPuxadoPraFusao.ContainsKey(a.Id) && _sendoPuxadoPraFusao.ContainsKey(b.Id));
		AfirmarFd("M2b ...e a cena ainda NAO comecou -- ela espera eles se encostarem",
				  !_emCenaDeFusao.ContainsKey(a.Id));
		AfirmarFd("M2c ...e ninguem esta fundido (nada foi aplicado)",
				  !EstaFundido(a.Id) && !EstaFundido(b.Id)
				  && Math.Abs(a.Ficha.FuseBuff) < 1e-9);

		// ---- M3: UM TERCEIRO NAO ENTRA NO MEIO ----
		AfirmarFd("M3 os dois contam como OCUPADOS enquanto sao puxados",
				  OcupadoPorFusao(a.Id) && OcupadoPorFusao(b.Id));

		// ---- M4: OS DOIS ANDAM, E OS DOIS PELO MESMO TANTO ----
		Vec2 antesA = a.Pos, antesB = b.Pos;
		double distAntes = Vec2.Distance(a.Pos, b.Pos);
		TickDoPuxaoDeFusao();
		double andouA = Vec2.Distance(antesA, a.Pos), andouB = Vec2.Distance(antesB, b.Pos);

		AfirmarFd("M4 os DOIS corpos andaram (`step_to` nos dois, `Potara_Fusion.dm:125` e `:127`)",
				  andouA > 0.01 && andouB > 0.01, $"{andouA:0.#} px e {andouB:0.#} px");
		AfirmarFd("M4b ...e pelo MESMO tanto -- nenhum dos dois e o que vai ate o outro",
				  Math.Abs(andouA - andouB) < 0.5, $"{andouA:0.##} contra {andouB:0.##}");

		// ============================ M5: A VELOCIDADE E A DO DM, MEDIDA NO CORPO ============================
		// `step_to(...,32)` a cada `world.tick_lag` de um mundo a 40 fps = 1280 px/s
		// (`Fusao.VelocidadeDoPuxao`). A prova le o CORPO e compara com a formula -- ler a constante e
		// dizer que ela vale a si mesma nao prova que alguem a aplicou, que e a licao que este projeto
		// ja pagou por escrito ("uniform escrito nao e pixel desenhado").
		// ==========================================================================================
		double esperado = Fusao.VelocidadeDoPuxao * Jandirus.Net.Protocol.TickSeconds;
		AfirmarFd("M5 cada um anda a velocidade do `step_to` do DM (1280 px/s)",
				  Math.Abs(andouA - esperado) < 0.5,
				  $"andou {andouA:0.##} px num tique, esperado {esperado:0.##}");

		AfirmarFd("M5b ...e a distancia entre os dois ENCURTOU pelos dois lados de uma vez",
				  Vec2.Distance(a.Pos, b.Pos) < distAntes - esperado,
				  $"{distAntes:0.#} -> {Vec2.Distance(a.Pos, b.Pos):0.#} px");

		// ---- M6: E ELES NAO DIRIGEM O PROPRIO CORPO ENQUANTO ISSO ----
		// `AlterInputDisabled(1)` nos dois (`:122-123`). As duas metades: o funil de vetor (que vale
		// pro jogador E pra IA) e o bit que viaja na ficha pro cliente parar de integrar tecla.
		AfirmarFd("M6 o funil de vetor recusa o passo dos dois (o `AlterInputDisabled(1)` do DU)",
				  !PodeMexerOCorpo(a) && !PodeMexerOCorpo(b));
		AfirmarFd("M6b ...e a ficha diz ao cliente que quem dirige e o servidor",
				  a.DirigidoPeloServidor && b.DirigidoPeloServidor);
		AfirmarFd("M6c ...com o bit que o distingue do ARREMESSO (senao o corpo sai girado 90 graus)",
				  (EstadoDeFichaNoPuxao(a) & 2) != 0 && (EstadoDeFichaNoPuxao(b) & 2) != 0);

		// ---- M7: QUANDO ENCOSTAM, A CENA COMECA ----
		// O laco do jogo, rodado ate encostarem. O teto de voltas e de seguranca e nao de ritmo: a
		// {TilesDeDistancia} tiles bastam ~3 tiques, e sem ele um dia em que o puxao deixasse de fechar
		// penduraria o servidor da bancada em silencio.
		for (int volta = 0; volta < 200 && _sendoPuxadoPraFusao.ContainsKey(a.Id); volta++)
			TickDoPuxaoDeFusao();

		AfirmarFd("M7 eles se encostaram e o puxao saiu das listas",
				  !_sendoPuxadoPraFusao.ContainsKey(a.Id) && !_sendoPuxadoPraFusao.ContainsKey(b.Id)
				  && _puxoesDeFusao.Count == 0);
		AfirmarFd("M7b ...a no maximo o TILE AO LADO (o `get_dist <= 1` do `while` do DU)",
				  Fusao.DistanciaEmTilesDoDm(a.Pos, b.Pos, ZoneCollision.TileSize) <= Fusao.TilesColados,
				  $"{Fusao.DistanciaEmTilesDoDm(a.Pos, b.Pos, ZoneCollision.TileSize)} tile(s)");
		AfirmarFd("M7c ...e A CINEMATICA COMECOU -- *\"quando se encostarem a cinematica comeca\"*",
				  _emCenaDeFusao.ContainsKey(a.Id) && _emCenaDeFusao.ContainsKey(b.Id));

		if (_emCenaDeFusao.GetValueOrDefault(a.Id) is { } cena) AbortarACenaDeFusao(cena, "fim da M7");
		SoltarNoFimDoPuxao(a, b);

		// ---- M8: JA COLADOS, NAO HA PUXAO NENHUM ----
		// O `while` do original nem entra quando `get_dist <= 1`. Uma Potara oferecida ao vizinho de
		// tile comeca a cena na hora, como no DU.
		{
			ServerPlayer c = ForjarNaFusaoDupla("Shin4", "Kai", 3_000_000, sabeDancar: false, 0);
			ServerPlayer d = ForjarNaFusaoDupla("Kibito4", "Kai", 2_900_000, sabeDancar: false, 1);
			c.Pos = corredor;
			d.Pos = corredor + new Vec2(ZoneCollision.TileSize, 0);

			Convidar(c, d, TipoDeFusao.Potara);
			ResponderAoConvite(d, aceitou: true);

			AfirmarFd("M8 ja colados, NAO nasce puxao nenhum", !_sendoPuxadoPraFusao.ContainsKey(c.Id));
			AfirmarFd("M8b ...e a cena comeca no mesmo instante do 'sim'",
					  _emCenaDeFusao.ContainsKey(c.Id));

			if (_emCenaDeFusao.GetValueOrDefault(c.Id) is { } cd) AbortarACenaDeFusao(cd, "fim da M8");
		}

		// ---- M9: QUEM NAO CHEGA NAO FUNDE ----
		// **Este e o item que o dono pediu por escrito** (*"se um dos dois nao chegar (parede, KO,
		// logout, teleporte), a fusao NAO comeca"*) e o unico que o original nao tem: la o laco gira
		// pra sempre. O corte e um FATO -- a MENOR distancia ja alcancada parou de encurtar --, e nao um
		// relogio global; a bancada empurra o carimbo pro passado e AFASTA os dois, que e o que uma
		// parede (ou um arremesso) produz.
		{
			ServerPlayer c = ForjarNaFusaoDupla("Shin5", "Kai", 3_000_000, sabeDancar: false, 0);
			ServerPlayer d = ForjarNaFusaoDupla("Kibito5", "Kai", 2_900_000, sabeDancar: false, 1);
			c.Pos = corredor;
			d.Pos = corredor + new Vec2(TilesDeDistancia * ZoneCollision.TileSize, 0);

			Convidar(c, d, TipoDeFusao.Potara);
			ResponderAoConvite(d, aceitou: true);
			AfirmarFd("M9 o puxao comecou (senao o corte nao mede nada)",
					  _sendoPuxadoPraFusao.ContainsKey(c.Id));

			if (_sendoPuxadoPraFusao.GetValueOrDefault(c.Id) is { } px)
			{
				// AFASTA (o que uma parede produz: a menor distancia nao melhora) e envelhece o carimbo.
				d.Pos = corredor + new Vec2(30 * ZoneCollision.TileSize, 0);
				px.SemMelhorarDesde = NowMs()
					- (long)(Fusao.SegundosSemAproximarParaDesistir * 1000) - 1;
				TickDoPuxaoDeFusao();
			}

			AfirmarFd("M9b quem nao se alcanca NAO funde -- o puxao caiu",
					  !_sendoPuxadoPraFusao.ContainsKey(c.Id) && !_emCenaDeFusao.ContainsKey(c.Id)
					  && !EstaFundido(c.Id));
			AfirmarFd("M9c ...e nada ficou aplicado (nem poder, nem selo, nem nome)",
					  Math.Abs(c.Ficha.FuseBuff) < 1e-9 && c.NomeDeFusao.Length == 0
					  && !GameServer.EhOSelo(d.Zone));
			// A RECARGA DE 1 h E O PRECO DE UMA FUSAO QUE ACONTECEU (`Fusion.dm:320`, `:334`). Cobra-la
			// aqui puniria os dois por uma parede.
			AfirmarFd("M9d ...e a recarga de 1 h NAO foi cobrada",
					  c.Ficha.fusion_cooldown_until == 0 && d.Ficha.fusion_cooldown_until == 0);
			AfirmarFd("M9e ...e os DOIS voltaram a dirigir o proprio corpo (ninguem fica travado)",
					  c.PuxaoDeFusaoRestante <= 0 && d.PuxaoDeFusaoRestante <= 0
					  && c.Combate.Stun <= 0 && d.Combate.Stun <= 0
					  && !OcupadoPorFusao(c.Id) && !OcupadoPorFusao(d.Id));
		}

		// ---- M10: O LOGOUT NO MEIO DO PUXAO ----
		// O `SoltarDaFusao` e chamado pelo `Drop` ANTES do `Persistir`. Sem a linha do puxao la, o OUTRO
		// ficaria deslizando pra um corpo que nao existe mais, com o input desligado.
		{
			ServerPlayer c = ForjarNaFusaoDupla("Shin6", "Kai", 3_000_000, sabeDancar: false, 0);
			ServerPlayer d = ForjarNaFusaoDupla("Kibito6", "Kai", 2_900_000, sabeDancar: false, 1);
			c.Pos = corredor;
			d.Pos = corredor + new Vec2(TilesDeDistancia * ZoneCollision.TileSize, 0);

			Convidar(c, d, TipoDeFusao.Potara);
			ResponderAoConvite(d, aceitou: true);
			AfirmarFd("M10 o puxao comecou (senao o corte nao mede nada)",
					  _sendoPuxadoPraFusao.ContainsKey(c.Id));

			SoltarDaFusao(d.Id);   // o mesmo metodo que o `Drop` chama

			AfirmarFd("M10b o logout derruba o puxao NA HORA, e nao no proximo tique",
					  !_sendoPuxadoPraFusao.ContainsKey(c.Id) && !_sendoPuxadoPraFusao.ContainsKey(d.Id));
			AfirmarFd("M10c ...e quem ficou nao ficou preso",
					  c.PuxaoDeFusaoRestante <= 0 && c.Combate.Stun <= 0 && !OcupadoPorFusao(c.Id));
		}
	}

	/// <summary>Um convite de Potara que NAO deixa pendente na mesa. Irmao do <see cref="PodeConvidarNaFusaoDupla"/>.</summary>
	private bool ConviteDePotara(ServerPlayer a, ServerPlayer b)
	{
		_pedidosDeFusao.Remove(b.Id);
		Convidar(a, b, TipoDeFusao.Potara);
		bool entrou = _pedidosDeFusao.ContainsKey(b.Id);
		_pedidosDeFusao.Remove(b.Id);
		return entrou;
	}

	/// <summary>
	/// O SEGUNDO BYTE DE ESTADO **DA FICHA DE PRODUCAO** deste corpo.
	///
	/// Pelo <c>EstadoDe</c> de verdade e nao por uma conta propria: o bit do puxao so serve se ele
	/// VIAJAR, e uma bancada que recalculasse `PuxaoDeFusaoRestante > 0 ? 2 : 0` ficaria verde no dia em
	/// que alguem esquecesse de o escrever no pacote -- que e a familia de defeito que este projeto ja
	/// catalogou por escrito ("escrever o corte nao e aplicar o corte").
	/// </summary>
	private static byte EstadoDeFichaNoPuxao(ServerPlayer p) => p.Sheet().Estado2;

	/// <summary>Tira do puxao o que sobrou depois de uma secao. So limpeza de bancada.</summary>
	private static void SoltarNoFimDoPuxao(params ServerPlayer[] quais)
	{
		foreach (ServerPlayer p in quais) { p.PuxaoDeFusaoRestante = 0; p.Combate.Stun = 0; }
	}

	/// <summary>
	/// ACHA UM CORREDOR HORIZONTAL LIVRE de <paramref name="tiles"/> tiles no mapa da zona da bancada.
	///
	/// ============================ POR QUE ISTO PRECISOU EXISTIR ============================
	/// O puxao e a primeira coisa desta bancada que MOVE corpo, e a parede manda mais que ele
	/// (`AndarNoPuxao` faz a mesma pergunta que o passo a pe). As outras secoes plantam os corpos em
	/// `(0,0)` porque nunca andam -- e `(0,0)` e a QUINA do mapa de um planeta, que e borda do mundo:
	/// ali os dois nao andariam um pixel e todas as provas ficariam vermelhas por um motivo que nao tem
	/// nada a ver com a fusao.
	///
	/// A pergunta e feita ao mapa com o `MoveRules.Occupied` **de producao**, no modo `APe`, e nao a um
	/// palpite sobre onde ha chao: agua tambem barra o passo, e um corredor "sem parede" no meio de um
	/// lago daria o mesmo falso vermelho.
	///
	/// ZONA SEM MAPA carregado devolve a origem, e ai o `AndarNoPuxao` nao consulta colisao nenhuma
	/// (`mapa == null`) -- o caso degrada pro que a bancada quer medir, que e o puxao.
	///
	/// ============================ A ZONA E PARAMETRO PORQUE A BANCADA DE **FOTO** TAMBEM PUXA ============================
	/// A `--diagfotofusao` fotografa o puxao (a coreografia e o pedido do dono, e foto e a unica prova
	/// dela), e ela nao roda numa zona forjada: ela roda **onde o jogador esta**. Uma segunda copia
	/// deste laco la seria a primeira a divergir no dia em que a pergunta ao mapa mudasse -- e ela ja
	/// mudou uma vez (era "sem parede" e virou `MoveRules.Occupied`, por causa da agua).
	/// ================================================================================================================
	/// </summary>
	/// <summary>
	/// QUANTOS TILES LIVRES DE CADA LADO do corredor. Quatro cobrem a sombra que um predio joga sobre a
	/// cena a partir do lado -- ver o bloco no corpo do metodo.
	/// </summary>
	private const int TilesDeMargemDoCorredor = 4;

	private Vec2 UmCorredorLivreParaOPuxao(int tiles, ZoneKey zona)
	{
		const float T = ZoneCollision.TileSize;
		if (MapaDaZonaOuCatalogo(zona) is not { } mapa) return new Vec2(0, 0);

		// ============================ E O CORREDOR VIROU UM **CAMPO ABERTO** ============================
		// Era uma LINHA de tiles livres, e isso basta pro puxao andar -- mas nao basta pra fotografar. A
		// `--diagfotofusao` herdou este palco e caiu numa faixa de chao colada a um predio de pedra: a
		// sombra do predio (o `.vis` do cliente) cobria a cena, o `CanvasModulate` da hora virou
		// irrelevante e a cor do clarao amostrada na foto saiu `272a2f` -- preto. Dali em diante toda
		// medida de disco comparava sombra com sombra e a bancada gravou numeros sem sentido (42 copias
		// da folha), sem uma unica linha dizendo por que.
		//
		// Uma FAIXA de <see cref="TilesDeMargemDoCorredor"/> tiles pra cada lado nao e zelo de foto: e o
		// mesmo motivo do laco original (a parede manda mais que o puxao), so que aplicado ao que o
		// jogador VE e nao so ao que ele pisa.
		// ============================================================================================
		for (int cy = 4; cy < mapa.Height - 4; cy += 3)
			for (int cx = 4; cx + tiles + 4 < mapa.Width; cx++)
			{
				bool livre = true;
				for (int k = 0; k <= tiles && livre; k++)
					for (int m = -TilesDeMargemDoCorredor; m <= TilesDeMargemDoCorredor && livre; m++)
					{
						int y = cy + m;
						if (y < 0 || y >= mapa.Height) { livre = false; break; }
						if (MoveRules.Occupied(mapa, new Vec2((cx + k + 0.5f) * T, (y + 0.5f) * T),
											   ModoDeTravessia.APe))
							livre = false;
					}
				if (livre) return new Vec2((cx + 0.5f) * T, (cy + 0.5f) * T);
			}

		return new Vec2(0, 0);
	}
}
