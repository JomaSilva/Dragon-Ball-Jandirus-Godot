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
	// J. A FUSAO NAMEKUSEIJIN -- a PERMANENTE, e ela nao tinha porta
	// =====================================================================
	/// <summary>
	/// `Namekian_Fusion` (`Fusion.dm:549-569`) -- o verb que faltava.
	///
	/// ============================ O QUE ESTA SECAO EXISTE PRA PROVAR ============================
	/// <see cref="TipoDeFusao.Namek"/> governava QUATRO coisas neste servidor (energia zero, roupa
	/// nenhuma, o `return` do nocaute, o pulo do dreno) e **nenhum caminho de producao a alcancava**:
	/// os unicos chamadores de `Convidar` eram a Danca e a Potara. Aqui ela entra pelo verb, pelo
	/// mesmo funil, e cada uma das quatro consequencias e cobrada.
	/// ======================================================================================
	/// </summary>
	private void AFusaoNamekuseijin()
	{
		GD.Print("[fusao2] -- J) a fusao Namekuseijin: o verb, o portao racial e a permanencia --");

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
		// (a do alvo) passaria verde na J1 e deixaria um Saiyajin fundir permanentemente com um Namek.
		AfirmarFd("J2 Saiyajin convidando Namekuseijin: NAO entra (o portao vale pros dois lados)",
				  !Convite(goku, piccolo));

		// ---- J3: A FUSAO ACONTECE, E E PERMANENTE ----
		FusaoAtiva? f = FundirDeVerdade(piccolo, nail, TipoDeFusao.Namek);
		AfirmarFd("J3 a fusao Namekuseijin acontece pelo funil de producao",
				  f != null && EstaFundido(piccolo.Id));
		if (f == null) return;

		AfirmarFd("J4 ela nasce PERMANENTE -- energia maxima ZERO (`Fusion.dm:271`)",
				  Math.Abs(f.EnergiaMax) < 1e-9, $"{f.EnergiaMax}");

		// O DRENO NAO A TOCA: `if (f.EnergiaMax <= 0) { f.UltimoDreno = agora; continue; }`. Empurrar
		// o relogio dez minutos pra tras e o mesmo gesto da secao H, e aqui ele nao pode fazer efeito.
		f.UltimoDreno = NowMs() - 600_000;
		TickDaFusao();
		AfirmarFd("J5 ...e o dreno de energia NAO a desfaz (dez minutos de relogio nao a tocam)",
				  EstaFundido(piccolo.Id));

		// ---- J6: O NOCAUTE NAO SEPARA (`Fusion.dm:79`) ----
		FusaoAoCair(piccolo, "nocaute");
		AfirmarFd("J6 ...e o NOCAUTE nao a separa (`Fusion.dm:79`) -- o que nem o tempo desfaz, "
				+ "um golpe nao desfaz", EstaFundido(piccolo.Id));

		// ---- J7-J9: A HERANCA FICA DE FORA, E E PERGUNTA ABERTA DO DONO ----
		// Ver `Fusao.HerancaNaFusaoNamekuseijin`. As duas primeiras provas leem a CONSTANTE em vez de
		// cravar "zero": no dia em que o dono responder e ela virar `true`, esta secao acompanha em vez
		// de ficar vermelha por estar desatualizada.
		AfirmarFd("J7 sem heranca: nenhuma skill do passageiro foi emprestada (o DM nao herda skill)",
				  Fusao.HerancaNaFusaoNamekuseijin || f.SkillsEmprestadas.Count == 0,
				  $"{f.SkillsEmprestadas.Count} skills");

		AfirmarFd("J8 ...e nenhum stat do passageiro foi copiado",
				  Fusao.HerancaNaFusaoNamekuseijin
				  || StatsDe(piccolo.Ficha).SequenceEqual(f.StatsDoDono),
				  "os oito stats do dono continuam os dele");

		// O PODER, ESSE SIM, E O DA FUSAO -- `(A+B)*2` vale pros TRES tipos (`Fusion.dm:264` nao
		// pergunta o `FType`). Sem esta linha, "sem heranca" poderia ser lido como "sem nada".
		AfirmarFd("J9 ...mas o PODER e o da fusao, como nos outros dois tipos ((A+B)*2)",
				  Math.Abs(piccolo.Ficha.BP + f.DeltaDeBp
						   - Fusao.BpDaFusao(2_000_000, 400_000)) < 1.0,
				  $"{piccolo.Ficha.BP + f.DeltaDeBp:N0}");

		// FORCADA: e o `Defuse(Forced)` do admin (`Fusion.dm:299`) -- a unica saida de uma permanente.
		Separar(f, "fim da secao J");

		OContraExemploDaPermanente();
	}

	/// <summary>
	/// ============================ O CONTRA-EXEMPLO DA PERMANENTE, LADO A LADO ============================
	/// As provas J4, J5 e J6 dizem "a Namekuseijin nasce com energia zero, o dreno nao a desfaz e o
	/// nocaute nao a separa". **As tres passariam num mundo em que o dreno e o nocaute simplesmente nao
	/// funcionassem pra fusao nenhuma** -- e sao exatamente as duas coisas que este passe mexeu
	/// (`Fusao.Avaliar` e o `FusaoAoCair` ganharam um tipo novo pra pensar). Uma afirmacao de "NAO
	/// acontece" so vale ao lado de um "acontece".
	///
	/// Entao aqui roda a MESMA sequencia -- mesmo `TickDaFusao`, mesmo `FusaoAoCair`, mesmos dez minutos
	/// de relogio empurrado --, so que numa Danca. Ela tem que se desfazer nas duas.
	/// ====================================================================================================
	/// </summary>
	private void OContraExemploDaPermanente()
	{
		GD.Print("[fusao2] -- J') o contra-exemplo: a MESMA sequencia numa fusao TEMPORARIA --");

		ServerPlayer a = ForjarNaFusaoDupla("Krillin2", "Human", 1_000_000, sabeDancar: true, 0);
		ServerPlayer b = ForjarNaFusaoDupla("Yajirobe2", "Human", 900_000, sabeDancar: true, 1);

		FusaoAtiva? d = FundirDeVerdade(a, b, TipoDeFusao.Danca);
		AfirmarFd("J10 a Danca dos mesmos moldes acontece", d != null && EstaFundido(a.Id));
		if (d == null) return;

		AfirmarFd("J11 ...e ela nasce com energia MAXIMA maior que zero (o contrario da J4)",
				  d.EnergiaMax > 0, $"{d.EnergiaMax:0.#}");

		// O MESMO GESTO DA J5, e aqui ele TEM que funcionar.
		d.UltimoDreno = NowMs() - 600_000;
		d.Energia = 0.0001;
		TickDaFusao();
		AfirmarFd("J12 ...e o dreno DESFAZ esta (o contrario da J5) -- era o dreno funcionando, e nao "
				+ "a permanencia sendo ignorada", !EstaFundido(a.Id));

		// E O NOCAUTE, o mesmo gesto da J6.
		a.Ficha.fusion_cooldown_until = 0;
		b.Ficha.fusion_cooldown_until = 0;
		FusaoAtiva? d2 = FundirDeVerdade(a, b, TipoDeFusao.Danca);
		AfirmarFd("J13 a dupla fundiu de novo (pra o nocaute ter o que separar)", d2 != null);
		if (d2 == null) return;

		FusaoAoCair(a, "nocaute");
		AfirmarFd("J14 ...e o NOCAUTE separa esta (o contrario da J6, `Fusion.dm:79`)",
				  !EstaFundido(a.Id));
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
