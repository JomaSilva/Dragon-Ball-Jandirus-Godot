using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ BANCADA DA **VIRADA DA FUSAO** (`--cenafusaoteste`) ============================
/// A `--diagcenafusao` (cliente) mede o que a cena DESENHA. Esta mede a unica coisa que ela nao pode
/// medir e que e a regra que o dono ditou com todas as letras:
///
///   *"a fusao so EXISTE no fim da cena -- pendure no beat de climax do motor que ja existe"*, e
///   *"a cena interrompida (nocaute, morte, zona, logout de um dos dois) tem que desfazer sem deixar
///   meio-corpo nem cena presa"*.
///
///     Godot --headless --path . --server --port 7966 --cenafusaoteste
///
/// ============================ POR QUE ELA E DO SERVIDOR, E POR QUE ELA E CURTA ============================
/// Quem funde e o servidor. A cena de cliente e desenho -- ela nao pode ser a autoridade sobre quando
/// dois personagens viram um, e nao e: quem segura o `Fundir` e o <see cref="TickDaCenaDeFusao"/>.
///
/// E ela e curta porque o DESENHO e que a torna curta, e isso e o achado desta etapa: **ate a virada,
/// nada foi feito**. Nao ha stat somado, nem skill emprestada, nem `FuseBuff`, nem corpo no selo --
/// entao "desfazer sem deixar meio-corpo" nao e um procedimento de limpeza, e a ausencia de um. As
/// provas do bloco C sao literalmente "o corpo continua exatamente como estava".
///
/// ============================ O RELOGIO E ADIANTADO, E NAO ESPERADO ============================
/// A cena mede 7,0 s e a virada cai aos 4,0. Uma bancada que ESPERASSE isso levaria sete segundos por
/// caso e cinco casos dariam trinta e cinco -- e, pior, mediria o relogio de parede em vez de medir a
/// regra. Aqui os instantes da <see cref="CenaDeFusao"/> sao empurrados pra tras e o tique e chamado a
/// mao. Nao ha atalho no meio: o `Fundir` continua saindo do mesmo `if (agora >= c.Funde)` do jogo.
///
/// ============================ OS CORPOS SAO FORJADOS, E O QUE ISSO CUSTA ESTA ANOTADO ============================
/// `new ServerPlayer` com BP escrito a mao, como na `--mestreteste`. O que ela NAO pode afirmar, por
/// isso: nada sobre valores derivados do nascimento (classe, limiar, `relBPmax`). Ela nao precisa --
/// o que ela mede sao instantes e presenca/ausencia de estado, e os dois independem de quanto o corpo
/// e forte. O QUE a fusao herda (BP, stats, skills, nome, roupa, cabelo) e a `--diagfusaolook`.
/// ==========================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Faixa de ids desta bancada -- acima da `--mestrevivo`, que era a maior (91.000).</summary>
	private const int IdBaseDaCenaDeFusao = 92_000;

	private int _cfOk, _cfFalhou;

	private void AfirmarCf(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _cfOk++; GD.Print($"[cenafusao]   OK    {oque}"); return; }
		_cfFalhou++;
		GD.Print($"[cenafusao]  FALHOU  {oque}{(detalhe.Length > 0 ? $"  ({detalhe})" : "")}");
	}

	public void RodarBancadaDaCenaDeFusao()
	{
		_cfOk = _cfFalhou = 0;
		GD.Print("[cenafusao] ================ A VIRADA DA FUSAO ================");

		// UMA ZONA SEM NINGUEM CONECTADO, pela mesma razao da `--mestreteste`: a cena manda pacote pra
		// `ZoneList` inteira, e a bancada nao pode disparar cinematica na tela de quem esta jogando.
		ZoneKey zona = ZoneKey.Premade(
			Jandirus.Core.World.Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(
					p => string.Equals(p.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		var forjados = new List<ServerPlayer>();

		ServerPlayer Forjar(int i, string nome, float tilesX, double bp)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDaCenaDeFusao + i,
				Peer = null,
				Name = nome,
				Race = "Saiyan",
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = new Vec2(tilesX * ZoneCollision.TileSize, 0),
				Conta = $"bancada_cenafusao_{i}",
				Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Race = "Saiyan", BP = bp },

				// O LIVRO VAZIO E OBRIGATORIO, e nao enfeite: o `Fundir` empresta as skills do
				// passageiro pro dono e chama `AplicarPoderes`, que varre `pl.Livro.Aprendidas` SEM
				// perguntar por nulo. Com o campo nulo (o padrao do `ServerPlayer`) a virada estoura
				// -- e o estouro so aparece nesta bancada porque em jogo todo corpo nasce com livro.
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			novo.Ficha.Ki = novo.Ficha.MaxKi;
			forjados.Add(novo);
			return novo;
		}

		try
		{
			OPrazoSaiDaCena();
			AViradaSegura(Forjar);
			AInterrupcaoNaoDeixaMeioCorpo(Forjar);
			ATravaDosQuatroSegundos(Forjar);
			OLogoutNoMeio(Forjar);
		}
		catch (Exception e)
		{
			AfirmarCf($"a bancada rodou inteira (estourou: {e.Message})", false, e.StackTrace ?? "");
		}
		finally
		{
			// TUDO O QUE A BANCADA CRIOU SAI. As cenas primeiro: uma cena viva com um corpo removido
			// do `_players` faria o tique de producao acusar "um dos dois saiu do mundo" a cada
			// quadro depois que a bancada acabasse.
			foreach (CenaDeFusao c in _cenasDeFusao.ToList()) SoltarDaCenaDeFusao(c);
			foreach (FusaoAtiva f in _fusoes.ToList()) Separar(f, "fim da bancada");
			foreach (ServerPlayer p in forjados)
			{
				p.Ficha.fusion_cooldown_until = 0;
				_players.Remove(p.Id);
				ZoneList(zona.Hash).Remove(p);
			}
		}

		GD.Print($"[cenafusao] ================ {_cfOk} passaram, {_cfFalhou} falharam ================");
	}

	// =====================================================================
	// A. OS DOIS PRAZOS SAEM DA CENA, E SO DELA
	// =====================================================================
	/// <summary>
	/// A UNICA secao sem corpo nenhum: ela confere que o servidor **pergunta** os prazos em vez de os
	/// escrever. Uma segunda verdade aqui e o defeito que o `MarcarCena` ja documenta pelo sintoma --
	/// *"o servidor descongelaria o Ki num instante e o cliente devolveria o controle noutro"*.
	/// </summary>
	private void OPrazoSaiDaCena()
	{
		GD.Print("[cenafusao] -- A) os prazos --");

		Jandirus.Core.Forms.Cinematica c = CenaDaFusao;

		AfirmarCf("a cena que o servidor toca E a `Cinematicas.Fusao` (uma so verdade)",
				  ReferenceEquals(c, Jandirus.Core.Forms.Cinematicas.Fusao));

		AfirmarCf("ela tem instante de virada -- sem ele o servidor funde na hora e avisa em voz alta",
				  c.SegundosAteAVirada > 0, $"{c.SegundosAteAVirada:0.##}s");

		AfirmarCf("e a virada vem ANTES do fim (ha cauda pro branco escoar)",
				  c.SegundosAteAVirada < c.SegundosPreso,
				  $"vira {c.SegundosAteAVirada:0.#}s, prende {c.SegundosPreso:0.#}s");
	}

	// =====================================================================
	// B. A VIRADA -- a fusao nasce no climax, e nao antes
	// =====================================================================
	private void AViradaSegura(Func<int, string, float, double, ServerPlayer> Forjar)
	{
		GD.Print("[cenafusao] -- B) a fusao so existe na virada --");

		ServerPlayer a = Forjar(1, "bancada: convidou", 0, 4_000_000);
		ServerPlayer b = Forjar(2, "bancada: aceitou", 1, 6_000_000);

		double bpAntes = a.Ficha.BP, buffAntes = a.Ficha.FuseBuff;
		ZoneKey zonaDoBAntes = b.Zone;

		ComecarACenaDaFusao(a, b, TipoDeFusao.Potara, estragada: false);

		CenaDeFusao? c = _emCenaDeFusao.GetValueOrDefault(a.Id);
		AfirmarCf("B1 a cena entrou na lista, pelos DOIS ids",
				  c != null && ReferenceEquals(_emCenaDeFusao.GetValueOrDefault(b.Id), c));
		if (c == null) return;

		// ============================ O ESTADO DO CORPO NO INSTANTE ZERO ============================
		// Estas quatro linhas sao o coracao da tarefa. Antes desta etapa o `Fundir` rodava na mesma
		// linha em que a danca resolvia -- ou seja, tudo isto ja estaria escrito aqui.
		// ========================================================================================
		AfirmarCf("B2 NINGUEM esta fundido ainda", !EstaFundido(a.Id) && !EstaFundido(b.Id));
		AfirmarCf("B3 o `FuseBuff` nao foi tocado -- o poder da fusao ainda nao existe",
				  Math.Abs(a.Ficha.FuseBuff - buffAntes) < 1e-9, $"{a.Ficha.FuseBuff}");
		AfirmarCf("B4 o passageiro ainda esta no MAPA (nao foi pro selo)",
				  b.Zone.Hash == zonaDoBAntes.Hash && !GameServer.EhOSelo(b.Zone));
		AfirmarCf("B5 e o nome dele continua o dele", a.NomeDeFusao.Length == 0, a.NomeDeFusao);

		// OS DOIS CORPOS ESTAO PRESOS E INTOCAVEIS -- e o segundo e o que impede a cena de virar a
		// armadilha mais barata do jogo (dois alvos parados por 7 s).
		AfirmarCf("B6 os dois corpos estao presos pela cena",
				  a.CenaSegundos > 0 && b.CenaSegundos > 0,
				  $"{a.CenaSegundos:0.#} / {b.CenaSegundos:0.#}");
		AfirmarCf("B7 ...e por isso INTOCAVEIS enquanto ela dura",
				  a.Combate.Intocavel && b.Combate.Intocavel);

		// UM TIQUE ANTES DA VIRADA nao funde nada -- e esta linha e a que separa "o tique funciona"
		// de "o tique funde sempre que roda".
		TickDaCenaDeFusao();
		AfirmarCf("B8 um tique ANTES da virada nao funde nada",
				  !EstaFundido(a.Id) && !c.Fundiu);

		// ---- O RELOGIO ANDA ATE A VIRADA (adiantado, ver o cabecalho) ----
		c.Funde = NowMs() - 1;
		TickDaCenaDeFusao();

		AfirmarCf("B9 NA VIRADA a fusao passa a existir", EstaFundido(a.Id) && EstaFundido(b.Id));
		AfirmarCf("B10 ...o `FuseBuff` recebe o `(A+B)x2` de uma vez",
				  Math.Abs(a.Ficha.FuseBuff - buffAntes
						   - (Fusao.BpDaFusao(bpAntes, b.Ficha.BP) - bpAntes)) < 1.0,
				  $"{a.Ficha.FuseBuff:N0}");
		AfirmarCf("B11 ...o passageiro vai pro SELO", GameServer.EhOSelo(b.Zone), b.Zone.Name);
		AfirmarCf("B12 ...e o corpo passa a ter nome de fusao", a.NomeDeFusao.Length > 0, a.NomeDeFusao);

		// ---- A CAUDA: a cena continua depois da virada, e os corpos continuam presos ----
		AfirmarCf("B13 a cena NAO acaba na virada -- a cauda e onde o branco escoa",
				  _emCenaDeFusao.ContainsKey(a.Id) && !c.Fundiu == false);
		AfirmarCf("B14 e o corpo continua preso durante a cauda", a.CenaSegundos > 0,
				  $"{a.CenaSegundos:0.##}");

		// ---- O FIM ----
		c.Acaba = NowMs() - 1;
		TickDaCenaDeFusao();
		AfirmarCf("B15 no fim da cena ela sai das listas -- e nao sobra id em dicionario nenhum",
				  !_emCenaDeFusao.ContainsKey(a.Id) && !_emCenaDeFusao.ContainsKey(b.Id)
				  && _cenasDeFusao.Count == 0);
		AfirmarCf("B16 ...mas a FUSAO fica de pe (quem a desfaz e a energia, o nocaute ou o logout)",
				  EstaFundido(a.Id));

		if (FusaoDe(a.Id) is { } f) Separar(f, "fim da secao B");
	}

	// =====================================================================
	// C. A CENA INTERROMPIDA -- e a ausencia de meio-corpo
	// =====================================================================
	/// <summary>
	/// OS QUATRO CORTES DO PEDIDO, um por vez, cada um num par novo de corpos.
	///
	/// ============================ PAR NOVO POR CORTE, E NAO UM SO REUSADO ============================
	/// Reusar o mesmo par cobraria a recarga de 1 h entre um caso e outro -- e ela e por ID. Pior: um
	/// corte que deixasse lixo (o defeito que esta secao existe pra pegar) mascararia o corte seguinte,
	/// e a bancada acusaria o caso ERRADO. Corpo forjado e barato; diagnostico errado nao e.
	/// ==========================================================================================
	/// </summary>
	private void AInterrupcaoNaoDeixaMeioCorpo(Func<int, string, float, double, ServerPlayer> Forjar)
	{
		GD.Print("[cenafusao] -- C) a cena interrompida --");

		int n = 10;
		void UmCorte(string nome, Action<ServerPlayer, ServerPlayer> quebrar)
		{
			ServerPlayer a = Forjar(n++, $"bancada: A ({nome})", 0, 4_000_000);
			ServerPlayer b = Forjar(n++, $"bancada: B ({nome})", 1, 4_000_000);

			double buffAntes = a.Ficha.FuseBuff;
			double forcaAntes = a.Ficha.physoff;
			int skillsAntes = a.Livro?.Aprendidas.Count() ?? 0;
			ZoneKey zonaDoB = b.Zone;

			ComecarACenaDaFusao(a, b, TipoDeFusao.Danca, estragada: false);
			AfirmarCf($"C.{nome}: a cena comecou (senao o corte nao mede nada)",
					  _emCenaDeFusao.ContainsKey(a.Id));

			quebrar(a, b);
			TickDaCenaDeFusao();

			AfirmarCf($"C.{nome}: a cena caiu e NAO fundiu ninguem",
					  !_emCenaDeFusao.ContainsKey(a.Id) && !_emCenaDeFusao.ContainsKey(b.Id)
					  && !EstaFundido(a.Id) && !EstaFundido(b.Id));

			// ============================ E NAO FICOU MEIO-CORPO ============================
			// As quatro coisas que o `Fundir` mexe, conferidas uma a uma. Elas nao podem ter mudado
			// **porque o `Fundir` nao rodou** -- e e essa a prova: se um dia alguem antecipar parte
			// do `Fundir` pro comeco da cena (o BP, "so pra ja mostrar na tela"), estas linhas
			// ficam vermelhas.
			// ============================================================================
			AfirmarCf($"C.{nome}: o poder nao ficou somado", Math.Abs(a.Ficha.FuseBuff - buffAntes) < 1e-9,
					  $"{a.Ficha.FuseBuff}");
			AfirmarCf($"C.{nome}: os stats nao ficaram trocados",
					  Math.Abs(a.Ficha.physoff - forcaAntes) < 1e-9);
			AfirmarCf($"C.{nome}: nenhuma skill ficou emprestada",
					  (a.Livro?.Aprendidas.Count() ?? 0) == skillsAntes);
			AfirmarCf($"C.{nome}: o nome continua o dele", a.NomeDeFusao.Length == 0, a.NomeDeFusao);

			// O PASSAGEIRO NAO FOI PRO SELO -- este e o "meio-corpo" mais caro que existe: preso num
			// quarto branco sem porta, com a unica saida (a fusao) tendo morrido antes de nascer.
			AfirmarCf($"C.{nome}: o passageiro NAO ficou selado",
					  !GameServer.EhOSelo(b.Zone), b.Zone.Name);

			// E A RECARGA DE 1 H NAO E COBRADA: ela e o preco de uma fusao que ACONTECEU
			// (`Fusion.dm:320`,`:334`). Cobra-la aqui puniria os dois por um terceiro ter passado.
			AfirmarCf($"C.{nome}: a recarga de 1 h NAO e cobrada -- nao houve fusao",
					  a.Ficha.fusion_cooldown_until == 0 && b.Ficha.fusion_cooldown_until == 0);

			// ============================ E O CORPO E SOLTO -- **QUEM AINDA TEM CORPO** ============================
			// O `Stun` foi posto por esta cena, entao esta cena o tira. Mas so de quem continua no
			// mundo: o `AbortarACenaDeFusao` pula quem ja saiu do `_players` (a mesma guarda que o
			// `AbortarADanca` tem), e ela esta certa -- limpar o castigo de um objeto que esta sendo
			// descartado nao devolve nada a ninguem. A bancada cobra o mesmo recorte que o codigo faz,
			// senao ela estaria pedindo trabalho que nao muda nada e chamando isso de regra.
			// ==================================================================================================
			foreach (ServerPlayer p in new[] { a, b })
			{
				if (!_players.ContainsKey(p.Id)) continue;
				AfirmarCf($"C.{nome}: `{p.Name}` foi SOLTO (nada de cena presa)",
						  p.Combate.Stun <= 0, $"{p.Combate.Stun:0.##}");
			}

			// Devolve os dois ao estado de pe pra nao contaminar nada por engano.
			a.Ficha.KO = a.Ficha.dead = false;
			b.Ficha.KO = b.Ficha.dead = false;
			b.Zone = zonaDoB;
		}

		UmCorte("nocaute", (a, b) => b.Ficha.KO = true);
		UmCorte("morte", (a, b) => a.Ficha.dead = true);

		// A ZONA DE DESTINO NAO PODE SER O SELO. A primeira versao usava `Interior("Selado", ...)` --
		// e a prova "o passageiro NAO ficou selado" ficava vermelha porque **a bancada** o tinha
		// selado, e nao a fusao. Ela estava acusando o proprio cenario de teste.
		UmCorte("zona", (a, b) => b.Zone = ZoneKey.Interior("BancadaDaCenaDeFusao", 999_999));

		UmCorte("saiu do mundo", (a, b) => _players.Remove(b.Id));
	}

	// =====================================================================
	// D. A TRAVA -- ninguem entra numa segunda fusao durante a cena
	// =====================================================================
	/// <summary>
	/// A JANELA DE QUATRO SEGUNDOS em que os dois nao estao em `_fundidos` nem em `_dancando` e mesmo
	/// assim ja tem uma fusao a caminho. Sem o <see cref="_emCenaDeFusao"/> caberia nela um segundo
	/// convite aceito -- e a mesma pessoa sairia de duas fusoes ao mesmo tempo.
	/// </summary>
	private void ATravaDosQuatroSegundos(Func<int, string, float, double, ServerPlayer> Forjar)
	{
		GD.Print("[cenafusao] -- D) a trava da janela --");

		ServerPlayer a = Forjar(30, "bancada: trava A", 0, 4_000_000);
		ServerPlayer b = Forjar(31, "bancada: trava B", 1, 4_000_000);
		ServerPlayer terceiro = Forjar(32, "bancada: o terceiro", 2, 4_000_000);

		ComecarACenaDaFusao(a, b, TipoDeFusao.Potara, estragada: false);

		AfirmarCf("D1 os dois contam como OCUPADOS durante a cena",
				  OcupadoPorFusao(a.Id) && OcupadoPorFusao(b.Id));
		AfirmarCf("D2 ...e quem nao esta na cena continua livre", !OcupadoPorFusao(terceiro.Id));

		// A MESMA PORTA QUE O CONVITE USA. Nao e uma reimplementacao: `Fusao.Avaliar` e o funil unico,
		// e `algumJaFundido` e o parametro por onde a trava entra nele.
		RecusaDeFusao r = Fusao.Avaliar(
			TipoDeFusao.Potara,
			convidaTemAssinatura: true, convidadoTemAssinatura: true,
			mesmaPessoa: false,
			algumJaFundido: OcupadoPorFusao(terceiro.Id) || OcupadoPorFusao(a.Id),
			algumNaRecarga: false, algumCaido: false, mesmaZona: true, distanciaTiles: 1,
			euSeiDancar: true, eleSabeDancar: true,
			racaA: "Saiyan", racaB: "Saiyan", expressoA: 1000, expressoB: 1000,
			eleJaTemPedido: false);
		AfirmarCf("D3 um TERCEIRO nao consegue fundir com quem esta no meio da cena",
				  r == RecusaDeFusao.JaFundido, r.ToString());

		// E UMA SEGUNDA CENA NAO NASCE POR CIMA -- o `ComecarACenaDaFusao` tem a sua propria trava,
		// e ela e a ultima (entre a decisao e a chamada passa um tique inteiro).
		int antes = _cenasDeFusao.Count;
		ComecarACenaDaFusao(a, terceiro, TipoDeFusao.Potara, estragada: false);
		AfirmarCf("D4 ...e uma segunda cena nao nasce por cima da primeira",
				  _cenasDeFusao.Count == antes, $"{antes} -> {_cenasDeFusao.Count}");

		if (_emCenaDeFusao.GetValueOrDefault(a.Id) is { } c) AbortarACenaDeFusao(c, "fim da secao D");
	}

	// =====================================================================
	// E. O LOGOUT NO MEIO -- o caminho que passa pelo `Drop`
	// =====================================================================
	/// <summary>
	/// O `SoltarDaFusao` e chamado pelo <c>Drop</c> **antes** do `Persistir`, e essa ordem e a regra
	/// inteira (ver o cabecalho dele). Esta secao confere que a CENA tambem cai por ali -- sem essa
	/// linha o outro ficaria preso esperando uma virada que nunca viria, calado.
	/// </summary>
	private void OLogoutNoMeio(Func<int, string, float, double, ServerPlayer> Forjar)
	{
		GD.Print("[cenafusao] -- E) o logout no meio --");

		ServerPlayer a = Forjar(40, "bancada: quem fica", 0, 4_000_000);
		ServerPlayer b = Forjar(41, "bancada: quem desloga", 1, 4_000_000);

		ComecarACenaDaFusao(a, b, TipoDeFusao.Danca, estragada: true);
		AfirmarCf("E1 a cena comecou (senao o corte nao mede nada)", _emCenaDeFusao.ContainsKey(a.Id));

		SoltarDaFusao(b.Id);

		AfirmarCf("E2 o logout derruba a cena na hora -- e nao no proximo tique",
				  !_emCenaDeFusao.ContainsKey(a.Id) && !_emCenaDeFusao.ContainsKey(b.Id));
		AfirmarCf("E3 ...e ninguem ficou fundido", !EstaFundido(a.Id) && !EstaFundido(b.Id));
		AfirmarCf("E4 ...e quem ficou nao ficou preso", a.Combate.Stun <= 0, $"{a.Combate.Stun:0.##}");
	}
}
