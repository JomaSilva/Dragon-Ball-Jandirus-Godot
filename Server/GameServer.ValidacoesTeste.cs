using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Magic;
using Jandirus.Core.Torneio;
using Jandirus.Core.Verbos;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DAS VALIDACOES DO SERVIDOR (`--validacoesteste`).
///
/// O dono (2026-10-10): *"melhore essas validaçoes do server"* -- as que o inventario de verbs daquele dia achou
/// fracas. Cada secao aperta um verbo PELO FUNIL DE PRODUCAO (`Verbo` / `UsarHabilidade`, o que o pacote de um
/// cliente mexido alcanca), e cobra tres coisas:
///
///   * a RECUSA, com a frase que o jogador le;
///   * NENHUM EFEITO COLATERAL na recusa (nada gravado, nada cobrado, nada dito a terceiros);
///   * o CAMINHO LEGITIMO continua funcionando.
///
/// E cada conserto guarda o defeito injetado dele (`GameServer.Validacoes.cs`): com o botao ligado a prova
/// REPROVA, que e o comportamento de antes.
///
///   1. `splitform_alvo` gravava a ordem nas copias antes de recusar por falta de alvo.
///   2. `cargo_nomear` / `cargo_herdeiro` / `cargo_herdeiro_tirar` aceitavam o alvo por id, do mundo inteiro.
///   3. `regenerar` so conferia morto (`namekian.dm:164` pede KO, med e train).
///   4. `sdb_invocar` nao tinha portao de lugar (`set src in oview(2)`, `ProceduralSpace.dm:1580`).
///   5. `trn_recusar` ignorava a fase do torneio.
///   6. `mst_ensinar` nao conferia o mestre caido (`MasterStudent.dm:480`).
///   7. `decolar` era um salto de graca pro espaco.
///   8. O que a varredura consertou (a familia do `PorNome` e as outras).
///   9. AS VIGIAS DO FONTE: um `PorNome(` novo num verbo de jogador reprova, e uma leitura nova do mapa de
///      ARQUIVO (`.Mapa`) fora do funil de "onde ha parede" tambem.
///   10 a 17. A SEGUNDA RODADA (2026-10-11), os de risco alto: `GameServer.ValidacoesRiscoAltoTeste.cs`.
///   18 a 20. A TERCEIRA (2026-10-11), as sobras: `GameServer.ValidacoesSobrasTeste.cs`.
///
/// HOSPEDANDO, no primeiro tique de cargos com um jogador no mundo: a secao do torneio precisa de um jogador de
/// verdade (so quem tem tela se inscreve). Os outros personagens sao corpos forjados.
///
///     Godot --headless --path . --host --rede 8260 --validacoesteste --raca Human --conta bancada_validacoes --nome Fiscal
/// </summary>
public sealed partial class GameServer
{
	/// <summary>A flag da bancada, lida AQUI (o molde da `--verbsteste`): quem a dispara e o tique dos cargos.</summary>
	private static readonly bool ValidacoesDeTeste = Array.IndexOf(OS.GetCmdlineArgs(), "--validacoesteste") >= 0;

	private bool _validacoesTestou;
	private int _valOk, _valFalhou, _valGente;

	/// <summary>A BANCADA RODA UMA VEZ, no primeiro tique de cargos em que ha um jogador com tela (o host).</summary>
	private void DispararABancadaDasValidacoes()
	{
		if (_validacoesTestou || Jogadores.FirstOrDefault() is not { } host) return;
		_validacoesTestou = true;
		GD.Print("[server] BANCADA: as validacoes do servidor (--validacoesteste)");
		RodarBancadaDasValidacoes(host);
	}

	private void ValCheca(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _valOk++; GD.Print($"[validacoes]   OK    {nome}"); }
		else { _valFalhou++; GD.PrintErr($"[validacoes]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>UM PERSONAGEM DE BANCADA: corpo forjado, com conta propria (a assinatura sai dela) e a raca nos dois lugares que o servidor le.</summary>
	private ServerPlayer ValGente(string nome, string raca, Vec2 onde, double bp = 5_000, string classe = "Normal",
								 string[]? skills = null, (string Path, int Nivel)[]? degraus = null, double kiMin = 0)
	{
		ServerPlayer c = ForjarComSkills(nome, onde, bp, skills, degraus, kiMin: kiMin);
		c.Conta = $"bancada_valid_{(char)('a' + _valGente / 26)}{(char)('a' + _valGente % 26)}";
		c.Race = raca;
		c.Ficha.Race = raca;
		c.Ficha.ParentRace = raca;
		c.Class = classe;
		c.Ficha.Class = classe;
		_valGente++;
		return c;
	}

	/// <summary>Uma secao que estoura nao leva as outras junto: a excecao vira uma falha com nome.</summary>
	private void ValSecao(string nome, Action secao)
	{
		try { secao(); }
		catch (Exception e)
		{
			ValCheca($"a secao \"{nome}\" correu ate o fim sem lancar excecao", false, $"{e.GetType().Name}: {e.Message}");
			GD.PrintErr(e.ToString());
		}
	}

	private void RodarBancadaDasValidacoes(ServerPlayer host)
	{
		GD.Print("\n[validacoes] ================ O SERVIDOR CONFERE O QUE O CLIENTE PEDE (dono, 2026-10-10) ================");
		_valOk = _valFalhou = _valGente = 0;

		// ============================ O MUNDO VOLTA COMO ESTAVA ============================
		ZoneKey zonaDoHost = host.Zone;
		Vec2 posDoHost = host.Pos;
		var tronosDeAntes = new Dictionary<string, string>(_tronos);
		List<string> herdeirosDeAntes = [.. _herdeirosDeVegeta];
		var mestresDeAntes = new Dictionary<string, string>(_mestreDe);
		var nomesDeMestreDeAntes = new Dictionary<string, string>(_nomePorAssinatura);
		(int Numero, string Dono, string Nome)[] supersDeAntes = [.. _supers.Select(s => (s.Numero, s.Dono, s.DonoNome))];
		int cicloDeAntes = _cicloDasSupers;
		(string Sig, string Nome, string Pedido, string Alvo) procuracaoDeAntes = (_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido);
		(long Terra, long Alem, long Ultimo) agendaDeAntes = (_agenda.ProximoDaTerraMs, _agenda.ProximoDoAlemMs, _agenda.UltimoDaTerraMs);

		try
		{
			_pjMapa ??= MapaDaZonaOuCatalogo(ZonaDaBancadaDeProjetil);
			ZoneKey terra = ZonaDaBancadaDeProjetil;

			ValSecao("splitform_alvo", () => ValOrdemDasCopias());
			ValSecao("cargo_nomear / cargo_herdeiro", () => ValAlvoDosCargos());
			ValSecao("regenerar", () => ValRegenerar());
			ValSecao("sdb_invocar", () => ValLugarDoSuperShenron(terra));
			ValSecao("trn_recusar", () => ValFaseDoTorneio(host));
			ValSecao("mst_ensinar", () => ValMestreCaido());
			ValSecao("decolar", () => ValDecolar(terra));
			ValSecao("a varredura", () => ValVarredura(terra));
			ValSecao("a vigia do PorNome", () => ValVigiaDoPorNome());
			ValSecao("a vigia do mapa de arquivo", () => ValVigiaDoMapaDeArquivo());

			// ---- A SEGUNDA RODADA (2026-10-11): os de risco alto -- `GameServer.ValidacoesRiscoAltoTeste.cs`
			ValSecao("treinar e meditar", () => ValAtividade(host));
			ValSecao("os teleportes e a prisao", () => ValTeleportes(terra));
			ValSecao("atirar atordoado", () => ValAtordoado());
			ValSecao("a parede do salto", () => ValParedeDoSalto());
			ValSecao("o Bio-Android Lab", () => ValLabDeBio(host));
			ValSecao("forma e a posse", () => ValFormaSemAsRedeas());
			ValSecao("a emprestada pela fusao", () => ValEmprestadaPelaFusao());
			ValSecao("o Guardiao no Templo", () => ValOGuardiaoNoTemplo(terra));

			// ---- A TERCEIRA RODADA (2026-10-11): as sobras -- `GameServer.ValidacoesSobrasTeste.cs`
			ValSecao("o marco so do que foi comprado", () => ValOMarcoSoDoComprado(host));
			ValSecao("o preso carregado pra fora", () => ValOPresoCarregado());
			ValSecao("a parede no planeta gerado", () => ValAParedeNoPlanetaGerado(terra));
		}
		finally
		{
			// ---- o mundo volta: botoes, escutas, livros e corpos ----
			AlvoSemAlcanceDeTeste = OrdemAntesDaRecusaDeTeste = RegeneraCaidoDeTeste = SuperShenronDeQualquerLugarDeTeste = false;
			RecusaEmQualquerFaseDeTeste = MestreCaidoEnsinaDeTeste = DecolarSemPortaoDeTeste = false;
			AtividadeSemConferenciaDeTeste = TeleporteFuraPrisaoDeTeste = AtiraAtordoadoDeTeste = ParedeSoDeCatalogoDeTeste = false;
			LabSemDonoDeTeste = FormaSemRedeasDeTeste = EsqueceOEmprestadoDeTeste = SoltaDeLongeDeTeste = false;
			PresoEntraEmQuemSaiDeTeste = CargaPresaSaiDeTeste = ReembolsaODoCargoDeTeste = false;
			Jandirus.Core.Skills.SkillBook.TudoContaComoCompradoDeTeste = false;
			foreach (int id in _fundidos.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _fundidos.Remove(id);
			foreach (int id in _pontoCronoG4.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _pontoCronoG4.Remove(id);
			_noChao.RemoveAll(o => o.Id == 987_014);
			EscutaDasSupers = null;
			EscutaDoTorneio = null;

			if (_torneio != null) EncerrarTorneio(_torneio);
			(_agenda.ProximoDaTerraMs, _agenda.ProximoDoAlemMs, _agenda.UltimoDaTerraMs) = agendaDeAntes;
			SalvarAgenda();

			_tronos.Clear();
			foreach ((string chave, string conta) in tronosDeAntes) _tronos[chave] = conta;
			_herdeirosDeVegeta.Clear();
			_herdeirosDeVegeta.AddRange(herdeirosDeAntes);
			SalvarHerdeiros();
			foreach (string conta in _convitesDeAnciao.Keys.Where(k => k.StartsWith("bancada_valid_", StringComparison.Ordinal)).ToList())
				_convitesDeAnciao.Remove(conta);
			foreach (string conta in _ofertasDeJuventudeG8.Keys.Where(k => k.StartsWith("bancada_valid_", StringComparison.Ordinal)).ToList())
				_ofertasDeJuventudeG8.Remove(conta);

			_mestreDe.Clear();
			foreach ((string a, string m) in mestresDeAntes) _mestreDe[a] = m;
			_nomePorAssinatura.Clear();
			foreach ((string a, string n) in nomesDeMestreDeAntes) _nomePorAssinatura[a] = n;
			SalvarMestres();

			foreach ((int numero, string dono, string nome) in supersDeAntes)
				if (_supers.Find(s => s.Numero == numero) is { } s) { s.Dono = dono; s.DonoNome = nome; }
			_cicloDasSupers = cicloDeAntes;
			(_benefSig, _benefNome, _pedidoDoBenef, _alvoDoPedido) = procuracaoDeAntes;
			_precoPendente = default;
			SalvarSupers();
			foreach (int id in _olhandoODragao.Keys.Where(k => k >= IdBaseDeProjetil).ToList()) _olhandoODragao.Remove(id);

			// AS COPIAS DO SPLIT FORM nascem com id de producao (`_nextId`): a faxina comum nao as ve.
			foreach (int id in _splitformsG12.Where(kv => kv.Value.Master >= IdBaseDeProjetil).Select(kv => kv.Key).ToList())
				DesfazerSplitformG12(id);

			if (host.Zone.Hash == zonaDoHost.Hash) CravarPosicao(host, posDoHost);
			else MoveToZone(host.Id, zonaDoHost, posDoHost);

			// OS CORPOS FORJADOS SAEM DE TODA LISTA DE ZONA: a bancada trocou a `Zone` de alguns na mao, e a faxina
			// comum tira cada corpo so da lista da zona em que ele DIZ estar.
			foreach (ServerPlayer c in _players.Values)
				if (c.Id >= IdBaseDeProjetil)
					foreach (List<ServerPlayer> lista in _zones.Values) lista.Remove(c);
			LimparTudoDaBancada();

			GD.Print($"[validacoes] ================ {_valOk} OK, {_valFalhou} FALHA(S) ================\n");
		}
	}

	// =====================================================================
	// 1) splitform_alvo
	// =====================================================================
	private void ValOrdemDasCopias()
	{
		GD.Print("[validacoes] -- 1) `splitform_alvo`: a ordem recusada nao chega as copias --");
		const int T = ZoneCollision.TileSize;
		Vec2 palco = CorredorLivre(24);
		ServerPlayer dono = ValGente("DivisorDaBancada", "Human", palco, skills: ["/datum/skill/general/splitform"], kiMin: 100_000);
		dono.Facing = Facing.East;
		ServerPlayer passante = ValGente("PassanteDaBancada", "Human", palco + new Vec2(6 * T, 0));

		List<string> divisao = Ouvir(() => UsarHabilidade(dono, "SplitForm"));
		KeyValuePair<int, SplitformG12> par = _splitformsG12.FirstOrDefault(kv => kv.Value.Master == dono.Id);
		SplitformG12? sf = par.Value;
		ServerPlayer? copia = sf != null ? _players.GetValueOrDefault(par.Key) : null;
		ValCheca("(montagem) o dono divide o corpo pelo verb de producao, e a copia esta de pe", copia != null, Ultimos(divisao));
		if (copia == null || sf == null) return;

		Ouvir(() => Verbo(dono, "splitform_seguir", ""));
		ValCheca("(montagem) a copia recebeu a ordem de SEGUIR o dono", sf.Funcao == FuncaoDeSplitformG12.Seguir, $"{sf.Funcao}");

		// SEM ALVO MARCADO, a ordem de atacar e recusada... e o dono marca alguem SO DEPOIS.
		dono.AlvoId = 0;
		List<string> recusa = Ouvir(() => Verbo(dono, "splitform_alvo", ""));
		dono.AlvoId = passante.Id;
		GuiarSplitformG12(copia, sf, out ServerPlayer? presa, out _);
		ValCheca("**`splitform_alvo` SEM ALVO E RECUSADO SEM MEXER NA ORDEM DAS COPIAS**: o dono ouve \"marque um alvo\", a copia "
				+ "continua SEGUINDO, e marcar alguem DEPOIS da recusa nao a solta em cima dele",
				Disse(recusa, "marque um alvo") && sf.Funcao == FuncaoDeSplitformG12.Seguir && presa == null,
				$"ordem {sf.Funcao}, presa {presa?.Name ?? "nenhuma"} | {Ultimos(recusa)}");

		OrdemAntesDaRecusaDeTeste = true;
		try
		{
			dono.AlvoId = 0;
			Ouvir(() => Verbo(dono, "splitform_alvo", ""));
			dono.AlvoId = passante.Id;
			GuiarSplitformG12(copia, sf, out ServerPlayer? cacada, out _);
			ValCheca("(defeito injetado: a ordem e gravada antes da recusa) a prova de cima REPROVA -- a copia ficou em Attack Target, "
					+ "esperando, e caca quem o dono marcou depois",
					sf.Funcao == FuncaoDeSplitformG12.AtacarAlvo && cacada == passante, $"ordem {sf.Funcao}, presa {cacada?.Name ?? "nenhuma"}");
		}
		finally { OrdemAntesDaRecusaDeTeste = false; }
		Ouvir(() => Verbo(dono, "splitform_seguir", ""));

		// O CAMINHO LEGITIMO
		dono.AlvoId = passante.Id;
		List<string> ordem = Ouvir(() => Verbo(dono, "splitform_alvo", ""));
		GuiarSplitformG12(copia, sf, out ServerPlayer? alvoDaCopia, out _);
		ValCheca("O CAMINHO LEGITIMO: com alguem marcado a ordem vale -- a copia passa a cacar o alvo do dono",
				Disse(ordem, "o seu alvo") && sf.Funcao == FuncaoDeSplitformG12.AtacarAlvo && alvoDaCopia == passante,
				$"ordem {sf.Funcao}, presa {alvoDaCopia?.Name ?? "nenhuma"} | {Ultimos(ordem)}");

		List<string> semCopia = Ouvir(() => Verbo(passante, "splitform_alvo", ""));
		ValCheca("...e quem nao tem copia nenhuma ouve isso, com alvo marcado ou sem", Disse(semCopia, "nenhuma copia de pe"), Ultimos(semCopia));

		Ouvir(() => Verbo(dono, "splitform_destruir", ""));
		dono.AlvoId = 0;
	}

	// =====================================================================
	// 2) cargo_nomear / cargo_herdeiro / cargo_herdeiro_tirar
	// =====================================================================
	private void ValAlvoDosCargos()
	{
		GD.Print("[validacoes] -- 2) `cargo_nomear` e `cargo_herdeiro`: o alvo tem que estar ao alcance (`view(1)` e `view(10)`) --");
		const int T = ZoneCollision.TileSize;
		ZoneKey terra = ZonaDaBancadaDeProjetil;

		// ---- o Grande Anciao: `Appoint_Elder(var/mob/M in view(1))` (`NamekRanks.dm:56`)
		Vec2 palco = CorredorLivre(24);
		ServerPlayer anciao = ValGente("AnciaoDaBancada", "Namekian", palco, classe: "Dragon clan", skills: [Jandirus.Core.Skills.AcendedoresDoPort.EstatuaDoDragao]);
		ServerPlayer novico = ValGente("NovicoDaBancada", "Namekian", palco + new Vec2(5 * T, 0), classe: "Warrior clan");
		_tronos["elder"] = anciao.Conta;

		List<string> longe = Ouvir(() => Verbo(anciao, "cargo_nomear", novico.Id.ToString()));
		ValCheca("**NOMEAR ANCIAO E PRESENCIAL** (`in view(1)`, `NamekRanks.dm:56`): a 5 tiles o servidor recusa, o convite nao nasce "
				+ "e o convidado nao ouve nada",
				Disse(longe, "longe demais") && !_convitesDeAnciao.ContainsKey(novico.Conta) && !Disse(longe, "te oferece o assento"),
				$"convite {_convitesDeAnciao.ContainsKey(novico.Conta)} | {Ultimos(longe)}");

		// de OUTRO PLANETA, pelo NOME -- com as mesmas coordenadas de quem estaria colado
		novico.Zone = ZoneKey.Premade("Namek");
		novico.Pos = anciao.Pos + new Vec2(T, 0);
		List<string> deOutroMundo = Ouvir(() => Verbo(anciao, "cargo_nomear", novico.Name));
		ValCheca("...nem pelo NOME, com o alvo em OUTRO planeta (as coordenadas iguais nao enganam: a zona e outra)",
				Disse(deOutroMundo, "longe demais") && !_convitesDeAnciao.ContainsKey(novico.Conta), Ultimos(deOutroMundo));
		novico.Zone = terra;

		novico.Pos = anciao.Pos + new Vec2(2 * T, 0);
		List<string> aDois = Ouvir(() => Verbo(anciao, "cargo_nomear", novico.Id.ToString()));
		ValCheca("...e a 2 tiles tambem nao (o `view(1)` e so o quadrado de nove celulas)",
				Disse(aDois, "longe demais") && !_convitesDeAnciao.ContainsKey(novico.Conta), Ultimos(aDois));

		AlvoSemAlcanceDeTeste = true;
		try
		{
			novico.Pos = anciao.Pos + new Vec2(5 * T, 0);
			List<string> furo = Ouvir(() => Verbo(anciao, "cargo_nomear", novico.Id.ToString()));
			ValCheca("(defeito injetado: o alvo e qualquer corpo do mundo) a prova de cima REPROVA -- o convite nasce a 5 tiles",
					_convitesDeAnciao.ContainsKey(novico.Conta) && Disse(furo, "te oferece o assento"), Ultimos(furo));
		}
		finally { AlvoSemAlcanceDeTeste = false; _convitesDeAnciao.Remove(novico.Conta); }

		novico.Pos = anciao.Pos + new Vec2(T, T);
		List<string> colado = Ouvir(() => Verbo(anciao, "cargo_nomear", novico.Id.ToString()));
		ValCheca("O CAMINHO LEGITIMO: com o convidado no tile vizinho (o de CANTO conta, como no `view(1)`) o convite nasce e ele o ouve",
				_convitesDeAnciao.ContainsKey(novico.Conta) && Disse(colado, "te oferece o assento"), Ultimos(colado));
		_convitesDeAnciao.Remove(novico.Conta);

		// ---- o Rei de Vegeta: `as mob in view(10)` (`SpaceRankSkills.dm:52`)
		Vec2 palcoDoRei = CorredorLivre(24);
		ServerPlayer rei = ValGente("ReiDaBancada", "Saiyan", palcoDoRei, classe: "Elite");
		ServerPlayer principe = ValGente("PrincipeDaBancada", "Saiyan", palcoDoRei + new Vec2(14 * T, 0));
		_tronos["kov"] = rei.Conta;
		_herdeirosDeVegeta.Clear();
		bool NaLinha() => _herdeirosDeVegeta.Contains(principe.Conta, StringComparer.OrdinalIgnoreCase);

		List<string> herdeiroLonge = Ouvir(() => Verbo(rei, "cargo_herdeiro", principe.Id.ToString()));
		ValCheca("**O REI SO NOMEIA HERDEIRO QUEM ELE VE** (`in view(10)`, `SpaceRankSkills.dm:52`): a 14 tiles o servidor recusa, a "
				+ "linha de sucessao nao muda e o escolhido nao ouve nada",
				Disse(herdeiroLonge, "longe demais") && !NaLinha() && !Disse(herdeiroLonge, "te nomeia herdeiro"), Ultimos(herdeiroLonge));

		AlvoSemAlcanceDeTeste = true;
		try
		{
			Ouvir(() => Verbo(rei, "cargo_herdeiro", principe.Id.ToString()));
			ValCheca("(defeito injetado: o alvo e qualquer corpo do mundo) a prova de cima REPROVA -- o principe entra na linha a 14 tiles",
					NaLinha());
		}
		finally { AlvoSemAlcanceDeTeste = false; _herdeirosDeVegeta.Clear(); }

		principe.Pos = rei.Pos + new Vec2(9 * T, 0);
		List<string> herdeiroPerto = Ouvir(() => Verbo(rei, "cargo_herdeiro", principe.Id.ToString()));
		ValCheca("O CAMINHO LEGITIMO: a 9 tiles (dentro dos 10) o principe entra na linha de sucessao e ouve do Rei",
				NaLinha() && Disse(herdeiroPerto, "te nomeia herdeiro"), Ultimos(herdeiroPerto));

		principe.Pos = rei.Pos + new Vec2(14 * T, 0);
		List<string> tirarLonge = Ouvir(() => Verbo(rei, "cargo_herdeiro_tirar", principe.Id.ToString()));
		ValCheca("...e TIRAR da linha pede o mesmo alcance (a mesma `view(10)`): de longe ele continua nela",
				Disse(tirarLonge, "longe demais") && NaLinha(), Ultimos(tirarLonge));
		principe.Pos = rei.Pos + new Vec2(9 * T, 0);
		Ouvir(() => Verbo(rei, "cargo_herdeiro_tirar", principe.Id.ToString()));
		ValCheca("...e de perto ele sai", !NaLinha());
	}

	// =====================================================================
	// 3) regenerar
	// =====================================================================
	private void ValRegenerar()
	{
		GD.Print("[validacoes] -- 3) `regenerar`: caido, meditando ou treinando nao se concentra (`namekian.dm:164`) --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer nk = ValGente("NamekDaBancada", "Namekian", palco, kiMin: 100_000);
		BodyPart membro = nk.Combate.Corpo.Partes.First(p => !p.Decepado);

		void Ferir()
		{
			nk.Ficha.Ki = nk.Ficha.MaxKi;
			nk.RegenLivreEm = 0;
			membro.Vida = membro.VidaMax * 0.4;
			nk.Combate.SincronizarVida();
		}

		// (estado, como por, como tirar)
		(string Nome, Action Por, Action Tirar)[] estados =
		[
			("NOCAUTEADO", () => nk.Ficha.KO = true, () => nk.Ficha.KO = false),
			("MEDITANDO", () => nk.Ficha.med = true, () => nk.Ficha.med = false),
			("TREINANDO", () => nk.Ficha.train = true, () => nk.Ficha.train = false),
		];

		foreach ((string nome, Action por, Action tirar) in estados)
		{
			Ferir();
			por();
			double ki = nk.Ficha.Ki, vida = membro.Vida;
			List<string> falas = Ouvir(() => UsarHabilidade(nk, "regenerar"));
			ValCheca($"**{nome} NAO REGENERA** (`if(usr.KO || usr.dead || usr.med || usr.train)`, `namekian.dm:164`): o membro fica como "
					+ "estava, o Ki nao e cobrado e a recarga nao comeca",
					Disse(falas, "concentrar") && Math.Abs(membro.Vida - vida) < 1e-9 && Math.Abs(nk.Ficha.Ki - ki) < 1e-9 && nk.RegenLivreEm == 0,
					$"vida {vida:0.#}->{membro.Vida:0.#}, Ki {ki:0}->{nk.Ficha.Ki:0}, recarga {nk.RegenLivreEm} | {Ultimos(falas)}");

			RegeneraCaidoDeTeste = true;
			try
			{
				Ouvir(() => UsarHabilidade(nk, "regenerar"));
				ValCheca($"(defeito injetado: so confere morto) a prova de cima REPROVA -- {nome.ToLowerInvariant()}, o membro volta ao cheio",
						membro.Vida >= membro.VidaMax - 1e-6 && nk.Ficha.Ki < ki, $"vida {membro.Vida:0.#}, Ki {nk.Ficha.Ki:0}");
			}
			finally { RegeneraCaidoDeTeste = false; }
			tirar();
		}

		Ferir();
		double kiDePe = nk.Ficha.Ki;
		List<string> dePe = Ouvir(() => UsarHabilidade(nk, "regenerar"));
		ValCheca("O CAMINHO LEGITIMO: de pe e desperto, o membro mais ferido volta ao cheio e custa 70% do Ki maximo",
				membro.Vida >= membro.VidaMax - 1e-6 && Math.Abs(kiDePe - nk.Ficha.Ki - nk.Ficha.MaxKi * 0.7) < 1e-3,
				$"vida {membro.Vida:0.#}/{membro.VidaMax:0.#}, gasto {kiDePe - nk.Ficha.Ki:0} | {Ultimos(dePe)}");
	}

	// =====================================================================
	// 4) sdb_invocar
	// =====================================================================
	private void ValLugarDoSuperShenron(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 4) `sdb_invocar`: o Super Shenron so e chamado ao lado de uma Super Esfera (`set src in oview(2)`) --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer kaio = ValGente("KaioDaBancada", "Kai", palco);
		ConferirALingua(kaio);

		void DarAsSete() { foreach (SuperEsfera s in _supers) { s.Dono = kaio.Assinatura; s.DonoNome = kaio.Name; } }
		DarAsSete();
		_benefSig = _benefNome = _pedidoDoBenef = _alvoDoPedido = "";
		ValCheca("(montagem) um Kai (fala a Lingua dos Deuses pelo sangue) com as SETE Super Esferas, de pe, na Terra",
				kaio.Ficha.godtongue && QuantasSupersTem(kaio.Assinatura) == SuperEsferas.Total && !Espaco.EhEspaco(kaio.Zone));

		List<string> mundo = [];
		EscutaDasSupers = mundo;
		try
		{
			double zeni = kaio.Ficha.Zeni;
			int ciclo = _cicloDasSupers;

			// ---- de dentro de um planeta
			List<string> daTerra = Ouvir(() => Verbo(kaio, "sdb_invocar", "sdb_riqueza"));
			ValCheca("**DE UM PLANETA O SUPER SHENRON NAO ATENDE** (`set src in oview(2)`, `ProceduralSpace.dm:1580`): com as sete e a lingua, "
					+ "o servidor recusa -- nenhum zeni, as sete continuam dele, o ciclo nao anda e a galaxia nao ouve anuncio",
					Disse(daTerra, "ao lado de uma delas") && Math.Abs(kaio.Ficha.Zeni - zeni) < 1 && _cicloDasSupers == ciclo
					&& QuantasSupersTem(kaio.Assinatura) == SuperEsferas.Total && mundo.Count == 0,
					$"zeni {zeni:N0}->{kaio.Ficha.Zeni:N0}, ciclo {ciclo}->{_cicloDasSupers}, anuncios {mundo.Count} | {Ultimos(daTerra)}");

			MenuDoDragao naTerra = RetratoDoDragaoDe(kaio, QualDragao.Super).Menu;
			ValCheca("...e A JANELA NAO OFERECE O QUE O DRAGAO RECUSARIA: na Terra os desejos saem apagados, e o alto da janela diz onde ir",
					naTerra.Entrada("sdb_riqueza") is { Pode: false } && !naTerra.DePe && naTerra.Estado.Contains("AO LADO", StringComparison.OrdinalIgnoreCase),
					$"pode {naTerra.Entrada("sdb_riqueza")?.Pode}, de pe {naTerra.DePe}, estado '{naTerra.Estado}'");

			// ---- no espaco, longe de todas
			kaio.Zone = ZonaDoEspaco;
			kaio.Pos = OndeEstaASuper(1) + new Vec2(SuperEsferas.AlcanceDeReivindicar * 8, 0);
			List<string> doVazio = Ouvir(() => Verbo(kaio, "sdb_invocar", "sdb_riqueza"));
			ValCheca("...e no espaco, mas longe de todas as sete, tambem nao: \"nao ha Super Esfera ao seu alcance\"",
					Disse(doVazio, "ao seu alcance") && Math.Abs(kaio.Ficha.Zeni - zeni) < 1 && _cicloDasSupers == ciclo && mundo.Count == 0,
					Ultimos(doVazio));

			// ---- o defeito
			kaio.Zone = terra;
			kaio.Pos = palco;
			SuperShenronDeQualquerLugarDeTeste = true;
			try
			{
				Ouvir(() => Verbo(kaio, "sdb_invocar", "sdb_riqueza"));
				ValCheca("(defeito injetado: sem portao de lugar) a prova de cima REPROVA -- da Terra, o desejo e concedido e as sete se espalham",
						Math.Abs(kaio.Ficha.Zeni - (zeni + Desejos.ZeniDoSuperDesejo)) < 1 && _cicloDasSupers == ciclo + 1 && mundo.Count > 0,
						$"zeni {zeni:N0}->{kaio.Ficha.Zeni:N0}, ciclo {ciclo}->{_cicloDasSupers}");
			}
			finally { SuperShenronDeQualquerLugarDeTeste = false; }
			kaio.Ficha.Zeni = zeni;
			_cicloDasSupers = ciclo;
			DarAsSete();
			mundo.Clear();

			// ---- o caminho legitimo: em cima de uma delas
			kaio.Zone = ZonaDoEspaco;
			kaio.Pos = OndeEstaASuper(3);
			MenuDoDragao naEsfera = RetratoDoDragaoDe(kaio, QualDragao.Super).Menu;
			List<string> naSuper = Ouvir(() => Verbo(kaio, "sdb_invocar", "sdb_riqueza"));
			ValCheca("O CAMINHO LEGITIMO: ao lado de uma Super Esfera a janela acende os desejos, o pedido e atendido e as sete se espalham",
					naEsfera.Entrada("sdb_riqueza") is { Pode: true, Verbo: "sdb_invocar" } && naEsfera.DePe
					&& Math.Abs(kaio.Ficha.Zeni - (zeni + Desejos.ZeniDoSuperDesejo)) < 1 && _cicloDasSupers == ciclo + 1,
					$"pode {naEsfera.Entrada("sdb_riqueza")?.Pode}, zeni {zeni:N0}->{kaio.Ficha.Zeni:N0}, ciclo {ciclo}->{_cicloDasSupers} | {Ultimos(naSuper)}");
		}
		finally { EscutaDasSupers = null; kaio.Zone = terra; kaio.Pos = palco; }

		ValCheca("...e a tabela de requisitos passou a COBRAR a recusa do `sdb_invocar` (a `--verbsteste` o aperta por quem esta em terra)",
				RequisitosDosVerbos.Linha("verbo:sdb_invocar") is { } linha && linha.Recusa.Length > 0);
	}

	// =====================================================================
	// 5) trn_recusar
	// =====================================================================
	private void ValFaseDoTorneio(ServerPlayer host)
	{
		GD.Print("[validacoes] -- 5) `trn_recusar`: so enquanto as inscricoes estao abertas (`Tournament.dm:378-380`) --");
		string sig = host.Assinatura;
		ValCheca("(montagem) nenhum torneio rolando, e o host e um jogador vivo na Terra (elegivel)",
				_torneio == null && Elegivel(host, TipoDeTorneio.Terra), $"torneio {_torneio?.Fase}, zona {host.Zone.Name}");
		if (_torneio != null || !Elegivel(host, TipoDeTorneio.Terra)) return;

		List<string> anuncios = [];
		EscutaDoTorneio = anuncios;
		try
		{
			// ---- com a chave MONTADA
			ComecarTorneio(TipoDeTorneio.Terra, "a bancada das validacoes");
			if (_torneio is not { } t) { ValCheca("(montagem) o torneio abriu as inscricoes", false); return; }
			Ouvir(() => Verbo(host, "trn_participar", ""));
			FecharInscricoes(t);
			ValCheca("(montagem) o host se inscreveu pelo verb, as inscricoes FECHARAM e ele esta na chave, na area de espera",
					_torneio == t && t.Fase == FaseDoTorneio.Preparando && t.Inscritos.ContainsKey(sig) && t.Chave.Quem(sig) != null
					&& PresoNoTorneio(host.Id), $"fase {t.Fase}, inscritos {t.Inscritos.Count}");
			if (_torneio != t || t.Fase == FaseDoTorneio.Inscricao) return;

			int ditos = anuncios.Count;
			List<string> tarde = Ouvir(() => Verbo(host, "trn_recusar", ""));
			ValCheca("**COM A CHAVE MONTADA, `trn_recusar` E RECUSADO SEM EFEITO**: o jogador ouve que as inscricoes ja fecharam, continua "
					+ "na lista e na chave, e o mundo NAO ouve \"desistiu da inscricao\"",
					Disse(tarde, "ja fecharam") && !Disse(tarde, "fica de fora") && t.Inscritos.ContainsKey(sig)
					&& !t.Recusaram.Contains(sig) && anuncios.Count == ditos && t.Chave.Quem(sig) != null,
					$"inscrito {t.Inscritos.ContainsKey(sig)}, anuncios {ditos}->{anuncios.Count} | {Ultimos(tarde)}");

			RecusaEmQualquerFaseDeTeste = true;
			try
			{
				Ouvir(() => Verbo(host, "trn_recusar", ""));
				ValCheca("(defeito injetado: a recusa vale em qualquer fase) a prova de cima REPROVA -- o mundo ouve \"desistiu da inscricao\" "
						+ "de quem continua na chave, travado na area de espera",
						!t.Inscritos.ContainsKey(sig) && anuncios.Count > ditos && anuncios[^1].Contains("desistiu", StringComparison.Ordinal)
						&& t.Chave.Quem(sig) != null && PresoNoTorneio(host.Id),
						$"inscrito {t.Inscritos.ContainsKey(sig)}, anuncios {ditos}->{anuncios.Count}");
			}
			finally { RecusaEmQualquerFaseDeTeste = false; }
			EncerrarTorneio(t);

			// ---- o caminho legitimo: com as inscricoes ABERTAS
			ComecarTorneio(TipoDeTorneio.Terra, "a bancada das validacoes");
			if (_torneio is not { } t2) { ValCheca("(montagem) o segundo torneio abriu as inscricoes", false); return; }
			Ouvir(() => Verbo(host, "trn_participar", ""));
			ditos = anuncios.Count;
			List<string> cedo = Ouvir(() => Verbo(host, "trn_recusar", ""));
			ValCheca("O CAMINHO LEGITIMO: com as inscricoes ABERTAS, `trn_recusar` tira o inscrito da lista e o mundo fica sabendo",
					Disse(cedo, "fica de fora") && !t2.Inscritos.ContainsKey(sig) && anuncios.Skip(ditos).Any(a => a.Contains("desistiu", StringComparison.Ordinal)),
					$"inscrito {t2.Inscritos.ContainsKey(sig)} | {Ultimos(cedo)}");
			EncerrarTorneio(t2);

			List<string> semTorneio = Ouvir(() => Verbo(host, "trn_recusar", ""));
			ValCheca("...e sem torneio nenhum o verbo so diz isso", Disse(semTorneio, "nao ha torneio"), Ultimos(semTorneio));
		}
		finally
		{
			EscutaDoTorneio = null;
			if (_torneio != null) EncerrarTorneio(_torneio);
		}
	}

	// =====================================================================
	// 6) mst_ensinar
	// =====================================================================
	private void ValMestreCaido()
	{
		GD.Print("[validacoes] -- 6) `mst_ensinar`: o mestre caido nao ensina (`if(dead || KO || !signature) return`, `MasterStudent.dm:480`) --");
		const int T = ZoneCollision.TileSize;
		Vec2 palco = CorredorLivre(24);
		ServerPlayer mestre = ValGente("MestreDaBancada", "Saiyan", palco, bp: 9_000_000);
		ServerPlayer aluno = ValGente("AlunoDaBancada", "Saiyan", palco + new Vec2(T * 0.9f, 0), bp: 3_000_000);
		mestre.Facing = Facing.East;
		aluno.Facing = Facing.West;
		mestre.AlvoId = aluno.Id;

		List<string> convite = Ouvir(() => UsarHabilidade(mestre, "mst_convidar"));
		List<string> aceite = Ouvir(() => UsarHabilidade(aluno, "mst_aceitar"));
		mestre.Forma.Liberar("ssj1");
		ValCheca("(montagem) o vinculo nasceu pelos verbs de producao, e o mestre possui o SSJ pra mostrar",
				MestreDe(aluno.Assinatura) == mestre.Assinatura && mestre.Forma.Despertou("ssj1"), $"{Ultimos(convite)} || {Ultimos(aceite)}");
		if (MestreDe(aluno.Assinatura) != mestre.Assinatura) return;

		void Zerar() { mestre.RecargaDeEnsino = 0; aluno.PedidoDoMestre = null; }

		(string Nome, Action Por, Action Tirar)[] estados =
		[
			("NOCAUTEADO", () => mestre.Ficha.KO = true, () => mestre.Ficha.KO = false),
			("MORTO", () => mestre.Ficha.dead = true, () => mestre.Ficha.dead = false),
		];
		foreach ((string nome, Action por, Action tirar) in estados)
		{
			Zerar();
			por();
			try
			{
				List<string> falas = Ouvir(() => UsarHabilidade(mestre, "mst_ensinar:ssj1"));
				ValCheca($"**O MESTRE {nome} NAO ENSINA**: a oferta nao sai, a recarga de 5 minutos NAO e gasta e o aluno nao ouve provocacao nenhuma",
						aluno.PedidoDoMestre == null && mestre.RecargaDeEnsino == 0 && Disse(falas, "caido") && !Disse(falas, "te provoca"),
						$"pedido {aluno.PedidoDoMestre != null}, recarga {mestre.RecargaDeEnsino} | {Ultimos(falas)}");

				MestreCaidoEnsinaDeTeste = true;
				try
				{
					Ouvir(() => UsarHabilidade(mestre, "mst_ensinar:ssj1"));
					ValCheca($"(defeito injetado: nao confere o mestre) a prova de cima REPROVA -- {nome.ToLowerInvariant()}, ele provoca o aluno e gasta a recarga",
							aluno.PedidoDoMestre is { Despertar: true } && mestre.RecargaDeEnsino > 0);
				}
				finally { MestreCaidoEnsinaDeTeste = false; }
			}
			finally { tirar(); }
		}

		Zerar();
		List<string> dePe = Ouvir(() => UsarHabilidade(mestre, "mst_ensinar:ssj1"));
		ValCheca("O CAMINHO LEGITIMO: de pe, a oferta sai pro aluno da frente e a recarga comeca no gesto",
				aluno.PedidoDoMestre is { Despertar: true, FormaId: "ssj1" } && mestre.RecargaDeEnsino > NowMs(), Ultimos(dePe));
		Zerar();
		mestre.AlvoId = 0;
	}

	// =====================================================================
	// 7) decolar
	// =====================================================================
	private void ValDecolar(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 7) `decolar`: a porta do espaco e voar ate romper a atmosfera, e mais nenhuma --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer fujao = ValGente("FujaoDaBancada", "Human", palco);
		ServerPlayer voador = ValGente("VoadorDaBancada", "Human", palco + new Vec2(4 * ZoneCollision.TileSize, 0), bp: 500_000,
									  skills: [SkillDoKi], degraus: [(SkillDoKi, MaestriaQueDestravaVoo)], kiMin: 50_000);

		ValCheca("(montagem) esta bancada NAO subiu com flag de robo que decola sem voar -- a lista existe, e este processo nao esta nela",
				!DecolagemDeBancada && BancadasQueDecolamSemVoar.Contains("--diagnav") && BancadasQueDecolamSemVoar.Contains("--trailer"),
				string.Join(" ", BancadasQueDecolamSemVoar));
		ValCheca("(montagem) o novato NAO passa nos tres portoes do `Space_Flight` (`Mindo.dm:162`), e o voador passa",
				!Voo.PodeRomperAAtmosfera(fujao.Ficha.Ki, fujao.Ficha.expressedBP, HabilidadeDeVoo(fujao))
				&& Voo.PodeRomperAAtmosfera(voador.Ficha.Ki, voador.Ficha.expressedBP, HabilidadeDeVoo(voador)),
				$"novato Ki {fujao.Ficha.Ki:0} BP {fujao.Ficha.expressedBP:0}; voador Ki {voador.Ficha.Ki:0} BP {voador.Ficha.expressedBP:0} voo {HabilidadeDeVoo(voador):0}");

		List<string> salto = Ouvir(() => UsarHabilidade(fujao, "decolar"));
		ValCheca("**`decolar` NAO LEVA NINGUEM AO ESPACO**: o novato sem Ki, sem poder e sem voo aperta, fica onde estava e ouve onde e a porta",
				fujao.Zone.Hash == terra.Hash && Disse(salto, "atmosfera"), $"{fujao.Zone.Name} | {Ultimos(salto)}");

		List<string> saltoDoForte = Ouvir(() => UsarHabilidade(voador, "decolar"));
		ValCheca("...e nem quem PASSA nos tres portoes pula pela habilidade: o gesto e subir voando (ela seria uma fuga instantanea de qualquer luta)",
				voador.Zone.Hash == terra.Hash && Disse(saltoDoForte, "atmosfera"), $"{voador.Zone.Name} | {Ultimos(saltoDoForte)}");

		fujao.Ficha.KO = true;
		List<string> saltoDoCaido = Ouvir(() => UsarHabilidade(fujao, "decolar"));
		fujao.Ficha.KO = false;
		ValCheca("...nem o nocauteado foge do golpe final por ela", fujao.Zone.Hash == terra.Hash, $"{fujao.Zone.Name} | {Ultimos(saltoDoCaido)}");

		DecolarSemPortaoDeTeste = true;
		try
		{
			Ouvir(() => UsarHabilidade(fujao, "decolar"));
			ValCheca("(defeito injetado: a habilidade sem portao) a prova de cima REPROVA -- o novato aparece no espaco",
					Espaco.EhEspaco(fujao.Zone), fujao.Zone.Name);
		}
		finally { DecolarSemPortaoDeTeste = false; }
		if (Espaco.EhEspaco(fujao.Zone)) MoveToZone(fujao.Id, terra, palco);

		// O CAMINHO LEGITIMO: no teto da atmosfera, com os portoes.
		voador.Voando = true;
		voador.Altitude = Voo.AlturaMaxima;
		List<string> rompeu = Ouvir(() => TentarRomperAAtmosfera(voador));
		ValCheca("O CAMINHO LEGITIMO: no teto da atmosfera, quem tem Ki, poder e voo ROMPE e chega ao espaco, vindo da Terra",
				Espaco.EhEspaco(voador.Zone) && voador.PlanetaDeOrigem == "Earth", $"{voador.Zone.Name} | {Ultimos(rompeu)}");

		fujao.Voando = true;
		fujao.Altitude = Voo.AlturaMaxima;
		fujao.AvisoDeTetoAte = 0;
		List<string> bateu = Ouvir(() => TentarRomperAAtmosfera(fujao));
		ValCheca("...e o novato, no mesmo teto, bate no limite e fica: \"falta\"",
				fujao.Zone.Hash == terra.Hash && Disse(bateu, "falta"), $"{fujao.Zone.Name} | {Ultimos(bateu)}");
		fujao.Voando = false;
		fujao.Altitude = 0;
	}

	// =====================================================================
	// 8) a varredura
	// =====================================================================
	private void ValVarredura(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 8) o que a varredura consertou --");
		ValSecao("8a: a familia do PorNome", () => ValAFamiliaDoPorNome(terra));
		ValSecao("8b: as absorcoes", () => ValAbsorcoes(terra));
		ValSecao("8c: db_refazer", () => ValRefazerComODragao(terra));
		ValSecao("8d: numeros do cliente", () => ValNumerosDoCliente());
		ValSecao("8e: o corpo caido", () => ValOCorpoCaido());
		ValSecao("8f: as falas", () => ValAsFalas());
		ValSecao("8g: o reembolso do kit", () => ValOReembolsoDoKit());
	}

	/// <summary>
	/// 8b) O REFLEXO DA MENTE E A COPIA DO SPLIT FORM NAO SE ABSORVEM (bio, Soul_Absorb, Absorb_Android, Life_Suck),
	/// e dentro de uma mente nada se absorve.
	/// </summary>
	private void ValAbsorcoes(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 8b) absorver o proprio reflexo ou a propria copia nao e mais uma fabrica de poder --");
		const int T = ZoneCollision.TileSize;
		Vec2 palco = CorredorLivre(24);
		ServerPlayer dono = ValGente("DonoDaCopiaDaBancada", "Human", palco, skills: ["/datum/skill/general/splitform"], kiMin: 100_000);
		dono.Facing = Facing.East;
		Ouvir(() => UsarHabilidade(dono, "SplitForm"));
		KeyValuePair<int, SplitformG12> par = _splitformsG12.FirstOrDefault(kv => kv.Value.Master == dono.Id);
		ServerPlayer? copia = par.Value != null ? _players.GetValueOrDefault(par.Key) : null;
		ValCheca("(montagem) uma copia do Split Form de pe", copia != null);
		if (copia == null) return;
		copia.Ficha.KO = true;    // caida, e com vida de sobra: o tique so a desfaz com HP <= 8
		copia.Ficha.HP = 50;

		// ---- o bio-androide
		ServerPlayer bio = ValGente("BioDaBancada", Jandirus.Core.Races.BioAndroids.Raca, copia.Pos + new Vec2(T * 0.9f, 0));
		bio.Facing = Facing.West;
		bio.AlvoId = copia.Id;
		double absorvidoAntes = bio.Ficha.AbsorbBP;
		List<string> doBio = Ouvir(() => UsarHabilidade(bio, "absorver"));
		ValCheca("**A COPIA DO SPLIT FORM NAO E BIOMASSA** (`if(ismajor && M.client)`, `Absorption.dm:344`): nocauteada ao lado de um "
				 + "bio-androide, ela nao e consumida, nenhum poder entra nele e a conta de poder dele nao muda de regra",
				 !copia.Ficha.dead && Math.Abs(bio.Ficha.AbsorbBP - absorvidoAntes) < 1e-9 && !bio.Ficha.AbsorbDeterminesBP
				 && Disse(doBio, "NOCAUTEADO"), $"morta {copia.Ficha.dead}, AbsorbBP {absorvidoAntes:0}->{bio.Ficha.AbsorbBP:0} | {Ultimos(doBio)}");

		// ---- o ladrao de almas (lote G12)
		ServerPlayer demonio = ValGente("DemonioDaBancada", "Demon", copia.Pos + new Vec2(0, T * 0.9f), skills: ["/datum/skill/demon/soulabsorb"]);
		demonio.AlvoId = copia.Id;
		double almaAntes = demonio.Ficha.AbsorbBP;
		List<string> doDemonio = Ouvir(() => UsarHabilidade(demonio, "Soul_Absorb"));
		ValCheca("...nem tem ALMA pra tomar (`M.HasSoul`, `demon.dm:41`; todo `mob/npc` nasce com `HasSoul = 0`): o Soul_Absorb nao a acha",
				 copia.Ficha.HasSoul && Math.Abs(demonio.Ficha.AbsorbBP - almaAntes) < 1e-9 && !Disse(doDemonio, "arranca"),
				 $"alma {copia.Ficha.HasSoul}, AbsorbBP {almaAntes:0}->{demonio.Ficha.AbsorbBP:0} | {Ultimos(doDemonio)}");

		// ---- dentro de uma mente
		bio.Zone = Jandirus.Core.World.DimensaoMental.De(bio.Id);
		List<string> naMente = Ouvir(() => UsarHabilidade(bio, "absorver"));
		bio.Zone = terra;
		ValCheca("**DENTRO DE UMA MENTE NADA SE ABSORVE** (a regra que a absorcao do Majin ja tinha): o reflexo e o proprio poder de quem "
				 + "absorve, e se levanta de novo a cada 15 s", Disse(naMente, "nada e real"), Ultimos(naMente));

		AbsorveCorpoDerivadoDeTeste = true;
		try
		{
			Ouvir(() => UsarHabilidade(demonio, "Soul_Absorb"));
			_prontoAbsorver.Remove(bio.Id);
			Ouvir(() => AbsorverAteOFimBio(bio));   // (2026-10-17) a execucao do bio leva 5 s: o relogio anda ate ela se consumar
			ValCheca("(defeito injetado: o corpo derivado vale como gente) as provas de cima REPROVAM -- a alma da copia e tomada, ela e "
					 + "consumida e o poder dela (o do proprio dono) entra em quem absorveu, sem vitima nenhuma",
					 !copia.Ficha.HasSoul && demonio.Ficha.AbsorbBP > almaAntes && copia.Ficha.dead && bio.Ficha.AbsorbBP > absorvidoAntes,
					 $"alma {copia.Ficha.HasSoul}, demonio {almaAntes:0}->{demonio.Ficha.AbsorbBP:0}, morta {copia.Ficha.dead}, bio {absorvidoAntes:0}->{bio.Ficha.AbsorbBP:0}");
		}
		finally
		{
			AbsorveCorpoDerivadoDeTeste = false;
			_prontoAbsorver.Remove(bio.Id);
			_mortesPendentesG12.RemoveAll(m => m.Vitima == copia.Id);
		}

		// O CAMINHO LEGITIMO: uma PESSOA nocauteada continua servindo.
		ServerPlayer vitima = ValGente("VitimaDaBancada", "Human", bio.Pos + new Vec2(T * 0.9f, 0));
		vitima.Ficha.KO = true;
		bio.Facing = Facing.East;
		bio.AlvoId = vitima.Id;
		double antesDaVitima = bio.Ficha.AbsorbBP;
		List<string> legitimo = Ouvir(() => AbsorverAteOFimBio(bio));
		ValCheca("O CAMINHO LEGITIMO: uma PESSOA nocauteada ao lado continua sendo absorvida -- ao fim dos 5 s da execucao ela morre e o poder "
				  + "dela entra no bio-androide",
				 vitima.Ficha.dead && bio.Ficha.AbsorbBP > antesDaVitima, $"morta {vitima.Ficha.dead}, AbsorbBP {antesDaVitima:0}->{bio.Ficha.AbsorbBP:0} | {Ultimos(legitimo)}");
		_prontoAbsorver.Remove(bio.Id);
		_prontoAbsorver.Remove(demonio.Id);
		Ouvir(() => Verbo(dono, "splitform_destruir", ""));
	}

	/// <summary>8c) REFAZER AS ESFERAS COM O DRAGAO DE PE: o dragao vai embora (`RecreateBalls()` apaga as esferas, `Dragonballs.dm:103-106`).</summary>
	private void ValRefazerComODragao(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 8c) `db_refazer` com o dragao de pe nao devolve os pedidos --");
		List<int> setsDeAntes = [.. _sets.Select(s => s.Id)];
		Vec2 palco = CorredorLivre(24);
		ServerPlayer escultor = ValGente("EscultorDaBancada", "Namekian", palco, classe: "Dragon clan", skills: [Jandirus.Core.Skills.AcendedoresDoPort.EstatuaDoDragao]);
		string guardiaoDeAntes = _tronos.GetValueOrDefault("guardian", "");
		try
		{
			ValCheca("(montagem) a Terra desta bancada nao tem Estatua do Dragao", !_sets.Exists(s => s.Zona.Hash == terra.Hash));
			_tronos["guardian"] = escultor.Conta;   // na Terra quem ergue e o Guardiao
			List<string> erguer = Ouvir(() => Verbo(escultor, "db_estatua", ""));
			SetDeEsferas? set = _sets.Find(s => s.Zona.Hash == terra.Hash && !setsDeAntes.Contains(s.Id));
			ValCheca("(montagem) o Namekuseijin do Cla do Dragao ergue a estatua pelo verb de producao", set != null, Ultimos(erguer));
			if (set == null) return;

			// (palco) o dragao de pe, invocado por quem ergueu, com o unico pedido JA gasto -- o instante depois de um desejo.
			void DragaoDePeEGasto()
			{
				_invocacoes[set.Id] = new Invocacao { Set = set.Id, Invocador = escultor.Id, Onde = escultor.Pos, ExpiraEm = TempoDoMundo + 120 };
				set.Pedidos = set.Desejos;
			}
			DragaoDePeEGasto();
			double zeni = escultor.Ficha.Zeni;
			List<string> gasto = Ouvir(() => Verbo(escultor, "db_desejar", "dinheiro"));
			ValCheca("(montagem) com o pedido gasto, o dragao recusa outro", Disse(gasto, "deu tudo") && Math.Abs(escultor.Ficha.Zeni - zeni) < 1, Ultimos(gasto));

			Ouvir(() => Verbo(escultor, "db_refazer", "1"));
			List<string> depois = Ouvir(() => Verbo(escultor, "db_desejar", "dinheiro"));
			ValCheca("**REFAZER AS ESFERAS COM O DRAGAO DE PE O MANDA EMBORA** (`RecreateBalls()` apaga as sete, e a lista de desejos e um proc "
					 + "da esfera: `Dragonballs.dm:103-106`): a invocacao acaba, e pedir de novo nao rende nada",
					 !_invocacoes.ContainsKey(set.Id) && Math.Abs(escultor.Ficha.Zeni - zeni) < 1 && Disse(depois, "não há dragão nenhum"),
					 $"invocacao {_invocacoes.ContainsKey(set.Id)}, zeni {zeni:N0}->{escultor.Ficha.Zeni:N0} | {Ultimos(depois)}");

			RefazerComODragaoDePeDeTeste = true;
			try
			{
				DragaoDePeEGasto();
				Ouvir(() => Verbo(escultor, "db_refazer", "1"));
				List<string> infinito = Ouvir(() => Verbo(escultor, "db_desejar", "dinheiro"));
				ValCheca("(defeito injetado: refazer nao fecha a invocacao) a prova de cima REPROVA -- a conta zera, o dragao fica, e o pedido "
						 + "gasto e atendido DE NOVO (e de novo, a cada `db_refazer`)",
						 escultor.Ficha.Zeni > zeni, $"zeni {zeni:N0}->{escultor.Ficha.Zeni:N0} | {Ultimos(infinito)}");
			}
			finally { RefazerComODragaoDePeDeTeste = false; escultor.Ficha.Zeni = zeni; }
			FecharAInvocacao(set.Id, "");

			List<string> semDragao = Ouvir(() => Verbo(escultor, "db_refazer", "2"));
			ValCheca("O CAMINHO LEGITIMO: sem dragao de pe, quem ergueu a estatua a reconfigura normalmente",
					 set.Desejos == 2 && Disse(semDragao, "2 pedido"), Ultimos(semDragao));
		}
		finally
		{
			if (guardiaoDeAntes.Length > 0) _tronos["guardian"] = guardiaoDeAntes; else _tronos.Remove("guardian");
			foreach (SetDeEsferas s in _sets.Where(s => !setsDeAntes.Contains(s.Id)).ToList())
			{
				_esferas.RemoveAll(e => e.Set == s.Id);
				_sets.Remove(s);
				FecharAInvocacao(s.Id, "");
			}
			SalvarEsferas();
		}
	}

	/// <summary>8d) NaN E INFINITO NAO SAO NUMERO DE CLIENTE (`NumeroFinito`): Power_Control, posicionar, pesos (e banco e gravidade, pela mesma linha).</summary>
	private void ValNumerosDoCliente()
	{
		GD.Print("[validacoes] -- 8d) \"NaN\" e \"Infinity\" passam no TryParse, e nao passam mais daqui --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer pl = ValGente("NumeroDaBancada", "Human", palco);

		ValCheca("(montagem) o `double.TryParse` do .NET ACEITA \"NaN\" e \"Infinity\" -- e por isso que a trava existe",
				 double.TryParse("NaN", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double n) && double.IsNaN(n)
				 && double.TryParse("Infinity", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double inf) && double.IsInfinity(inf)
				 && !NumeroFinito(n) && !NumeroFinito(inf) && NumeroFinito(50));

		pl.Ficha.powerMod = 1;
		List<string> controle = Ouvir(() => ControleDePoderG9(pl, "NaN"));
		ValCheca("**`Power_Control:NaN` E RECUSADO**: o `powerMod` continua um numero (era NaN -- e dali o poder expresso, o dano e os membros "
				 + "de quem encostasse)", double.IsFinite(pl.Ficha.powerMod) && Math.Abs(pl.Ficha.powerMod - 1) < 1e-9, $"powerMod {pl.Ficha.powerMod} | {Ultimos(controle)}");

		pl.Ficha.Weighted = 0;
		List<string> peso = Ouvir(() => AjustarPeso(pl, Jandirus.Core.Items.CatalogoDeItens.Pesos, "NaN"));
		ValCheca("**`item_ajustar Weights/NaN` E RECUSADO**: o peso vestido continua um numero (um NaN na ficha quebra a gravacao da conta)",
				 double.IsFinite(pl.Ficha.Weighted) && Disse(peso, "inválido"), $"Weighted {pl.Ficha.Weighted} | {Ultimos(peso)}");

		List<string> ponto = Ouvir(() => Posicionar(pl, "ItemQueNaoExisteDaBancada/NaN/NaN"));
		ValCheca("**`posicionar item/NaN/NaN` E RECUSADO NA PORTA** (\"ponto invalido\"), antes de olhar o item: uma obra com X = NaN "
				 + "no mundo impedia o `mundo.json` de ser gravado de novo", Disse(ponto, "ponto inválido"), Ultimos(ponto));

		NumeroSemTravaDeTeste = true;
		try
		{
			Ouvir(() => ControleDePoderG9(pl, "NaN"));
			Ouvir(() => AjustarPeso(pl, Jandirus.Core.Items.CatalogoDeItens.Pesos, "NaN"));
			List<string> passou = Ouvir(() => Posicionar(pl, "ItemQueNaoExisteDaBancada/NaN/NaN"));
			ValCheca("(defeito injetado: NaN vale como numero) as tres provas de cima REPROVAM -- `powerMod` e `Weighted` viram NaN, e o "
					 + "ponto NaN passa da porta do `posicionar`",
					 double.IsNaN(pl.Ficha.powerMod) && double.IsNaN(pl.Ficha.Weighted) && !Disse(passou, "ponto inválido"),
					 $"powerMod {pl.Ficha.powerMod}, Weighted {pl.Ficha.Weighted} | {Ultimos(passou)}");
		}
		finally { NumeroSemTravaDeTeste = false; pl.Ficha.powerMod = 1; pl.Ficha.Weighted = 0; pl.Ficha.WeightTick(); }

		Ouvir(() => ControleDePoderG9(pl, "50"));
		ValCheca("O CAMINHO LEGITIMO: `Power_Control:50` segura o poder em 50%", Math.Abs(pl.Ficha.powerMod - 0.5) < 1e-9, $"{pl.Ficha.powerMod}");
		pl.Ficha.powerMod = 1;
	}

	/// <summary>8e) O CORPO CAIDO NAO REIVINDICA CARGO, NAO ENSINA DISCIPLINA E NAO INFUNDE O HAKAI.</summary>
	private void ValOCorpoCaido()
	{
		GD.Print("[validacoes] -- 8e) caido (ou morto) nao reivindica cargo, nao ensina disciplina divina e nao infunde o Hakai --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer pl = ValGente("CaidoDaBancada", "Human", palco, kiMin: 100_000);
		_tronos.Remove("turtle");
		_tronos.Remove("nkai");

		// ---- o cargo (`C2S.Cargo`): `RankQuests.dm:272` e `:299`
		pl.Ficha.KO = true;
		List<string> cargoCaido = Ouvir(() => ReivindicarCargo(pl, "turtle"));
		pl.Ficha.KO = false;
		pl.Ficha.dead = true;
		List<string> cargoMorto = Ouvir(() => ReivindicarCargo(pl, "turtle"));
		List<string> cargoDoAlem = Ouvir(() => ReivindicarCargo(pl, "nkai"));
		pl.Ficha.dead = false;
		ValCheca("**NOCAUTEADO NAO REIVINDICA CARGO** (`usr.KO` -> return, `RankQuests.dm:272`), **E O MORTO SO REIVINDICA CARGO DO OUTRO "
				 + "MUNDO** (`if(usr.dead && !(key in RQ_SKY))`, `:299`): o Eremita Tartaruga nao, o Kaio do Norte chega aos requisitos",
				 Disse(cargoCaido, "caido") && Disse(cargoMorto, "Outro Mundo") && !Disse(cargoDoAlem, "Outro Mundo") && !Disse(cargoDoAlem, "caido")
				 && CargoDe(pl.Conta).Length == 0, $"{Ultimos(cargoCaido)} || {Ultimos(cargoMorto)} || {Ultimos(cargoDoAlem)}");

		// ---- o Hakai (`UltraEgo.dm:596`) e o ensino da disciplina (`UltraEgo.dm:132`)
		pl.PoderDaDestruicao.Aprendida = true;
		pl.PoderDaDestruicao.Real = 100;
		void Zerar() { pl.HakaiAte = pl.HakaiRecargaAte = 0; pl.Ficha.Ki = pl.Ficha.MaxKi; }
		Zerar();
		pl.Ficha.KO = true;
		double ki = pl.Ficha.Ki;
		List<string> hakaiCaido = Ouvir(() => UsarHabilidade(pl, "ue_hakai"));
		List<string> ensinoCaido = Ouvir(() => UsarHabilidade(pl, "ue_ensinar"));
		ValCheca("**CAIDO NAO INFUNDE O HAKAI** (`if(usr.dead || usr.KO || usr.med || usr.train) return`, `UltraEgo.dm:596`): a infusao nao "
				 + "acende e o Ki nao e cobrado; **E NAO ENSINA A DISCIPLINA** (`if(T2.dead || T2.KO) return`, `UltraEgo.dm:132`)",
				 pl.HakaiAte == 0 && Math.Abs(pl.Ficha.Ki - ki) < 1e-9 && Disse(hakaiCaido, "caido") && Disse(ensinoCaido, "caido"),
				 $"HakaiAte {pl.HakaiAte}, Ki {ki:0}->{pl.Ficha.Ki:0} | {Ultimos(hakaiCaido)} || {Ultimos(ensinoCaido)}");

		CaidoAgeDeTeste = true;
		try
		{
			Ouvir(() => UsarHabilidade(pl, "ue_hakai"));
			List<string> ensinou = Ouvir(() => UsarHabilidade(pl, "ue_ensinar"));
			List<string> reivindicou = Ouvir(() => ReivindicarCargo(pl, "turtle"));
			ValCheca("(defeito injetado: o caido age) as provas de cima REPROVAM -- nocauteado, o Hakai acende e cobra o Ki, o ensino passa "
					 + "do portao (procura o aluno) e a reivindicacao chega aos requisitos",
					 pl.HakaiAte > 0 && pl.Ficha.Ki < ki && !Disse(ensinou, "caido") && !Disse(reivindicou, "caido"),
					 $"HakaiAte {pl.HakaiAte}, Ki {pl.Ficha.Ki:0} | {Ultimos(ensinou)} || {Ultimos(reivindicou)}");
		}
		finally { CaidoAgeDeTeste = false; _tronos.Remove("turtle"); }
		pl.Ficha.KO = false;

		Zerar();
		List<string> dePe = Ouvir(() => UsarHabilidade(pl, "ue_hakai"));
		ValCheca("O CAMINHO LEGITIMO: de pe, o Hakai acende por um minuto", pl.HakaiAte > NowMs(), Ultimos(dePe));
		Zerar();
		pl.PoderDaDestruicao.Aprendida = false;
		pl.PoderDaDestruicao.Real = 0;
	}

	/// <summary>8f) O BYTE DO CANAL DA FALA E O CANAL DOS CARGOS.</summary>
	private void ValAsFalas()
	{
		GD.Print("[validacoes] -- 8f) a fala so sai pelos seis canais, e o canal dos cargos passa pelo que toda fala passa --");
		Vec2 palco = CorredorLivre(24);
		ServerPlayer pl = ValGente("FalanteDaBancada", "Human", palco);
		List<(int Quem, Protocol.Fala Canal, string Texto)> falas = [];
		EscutaDeFalas = falas;
		try
		{
			_ultimaFala.Remove(pl.Id);
			Falar(pl, Protocol.Fala.Sistema, "AVISO DO SERVIDOR: todos serao banidos");
			ValCheca("**O CANAL 200 (`Fala.Sistema`) NAO E DE JOGADOR**: o byte do canal vem do cliente, e com ele o texto chegava sem autor, "
					 + "como recado do servidor -- agora nao passa do funil", falas.Count == 0, $"{falas.Count} fala(s)");

			CanalDeFalaLivreDeTeste = true;
			try
			{
				_ultimaFala.Remove(pl.Id);
				Falar(pl, Protocol.Fala.Sistema, "AVISO DO SERVIDOR: todos serao banidos");
				ValCheca("(defeito injetado: qualquer byte de canal) a prova de cima REPROVA -- a fala passa, no canal de sistema",
						 falas.Count == 1 && falas[0].Canal == Protocol.Fala.Sistema, $"{falas.Count} fala(s)");
			}
			finally { CanalDeFalaLivreDeTeste = false; }

			falas.Clear();
			_ultimaFala.Remove(pl.Id);
			Falar(pl, Protocol.Fala.Diz, "oi");
			ValCheca("O CAMINHO LEGITIMO: a fala normal continua passando", falas.Count == 1 && falas[0].Canal == Protocol.Fala.Diz);

			// ---- o canal dos cargos
			_tronos["turtle"] = pl.Conta;
			falas.Clear();
			_ultimaFala.Remove(pl.Id);
			Ouvir(() => Verbo(pl, "cargo_falar", "bom dia\nSISTEMA: o servidor vai fechar"));
			Ouvir(() => Verbo(pl, "cargo_falar", "de novo, no mesmo instante"));
			ValCheca("**O CANAL DOS CARGOS E UMA FALA**: a quebra de linha vira espaco (com ela dava pra desenhar uma linha falsa de sistema no "
					 + "chat alheio) e a segunda mensagem do mesmo instante nao sai (o intervalo de toda fala)",
					 falas.Count == 1 && !falas[0].Texto.Contains('\n') && falas[0].Texto.Contains("SISTEMA", StringComparison.Ordinal),
					 $"{falas.Count} fala(s): {string.Join(" | ", falas.Select(f => f.Texto.Replace("\n", "\\n")))}");

			CanalDosCargosCruDeTeste = true;
			try
			{
				falas.Clear();
				Ouvir(() => Verbo(pl, "cargo_falar", "bom dia\nSISTEMA: o servidor vai fechar"));
				Ouvir(() => Verbo(pl, "cargo_falar", "de novo, no mesmo instante"));
				ValCheca("(defeito injetado: o canal repassa cru) a prova de cima REPROVA -- a quebra de linha viaja, e as duas mensagens saem",
						 falas.Count == 2 && falas[0].Texto.Contains('\n'), $"{falas.Count} fala(s)");
			}
			finally { CanalDosCargosCruDeTeste = false; }
		}
		finally { EscutaDeFalas = null; _tronos.Remove("turtle"); _ultimaFala.Remove(pl.Id); }
	}

	/// <summary>8g) A SKILL QUE O CARGO DEU NAO DEVOLVE MARCO (`SkillTreesWindow.dm:33-35`: so o que esta em `investedskills`).</summary>
	private void ValOReembolsoDoKit()
	{
		GD.Print("[validacoes] -- 8g) `skill_esquecer` numa skill que o CARGO deu nao imprime marcos --");
		if (_skills == null) { ValCheca("(montagem) ha catalogo de skills", false); return; }
		Vec2 palco = CorredorLivre(24);
		ServerPlayer pl = ValGente("EremitaDaBancada", "Human", palco);
		try
		{
			_tronos["turtle"] = pl.Conta;
			ReconciliarDadiva(pl);
			string? doKit = Jandirus.Core.Ranks.DadivaDeCargo.De("turtle")
				.FirstOrDefault(p => pl.Livro!.Sabe(p) && _skills.Get(p) is { Esquecivel: true, Arvore: false });
			ValCheca("(montagem) o Eremita Tartaruga recebeu o kit do cargo, e ha nele uma skill que se esquece", doKit != null);
			if (doKit == null) return;

			// (palco) O PORTADOR DE SAVE ANTIGO. Desde a lista de compradas (`SkillBook.FoiComprada`, secao 18) o livro
			// nao paga pelo que o cargo deu -- menos no save de antes da lista, onde tudo o que ninguem ensinou conta
			// como comprado (`CarregarCompradas(null)`). E pra ele que a recusa deste verbo continua sendo a tranca.
			pl.Livro!.CarregarCompradas(null);
			ValCheca("(montagem) ...num livro de SAVE ANTIGO, em que a skill do kit conta como comprada", pl.Livro.FoiComprada(doKit));

			int marcos = pl.Livro.MarcosLivres;
			List<string> recusa = Ouvir(() => Verbo(pl, "skill_esquecer", doKit));
			ValCheca("**O QUE O CARGO DEU NAO SE VENDE DE VOLTA**: esquecer uma skill do kit e recusado -- ela fica no livro e nenhum marco entra",
					 pl.Livro.Sabe(doKit) && pl.Livro.MarcosLivres == marcos && Disse(recusa, "veio com o seu cargo"),
					 $"sabe {pl.Livro.Sabe(doKit)}, marcos {marcos}->{pl.Livro.MarcosLivres} | {Ultimos(recusa)}");

			ReembolsaODoCargoDeTeste = true;
			try
			{
				Ouvir(() => Verbo(pl, "skill_esquecer", doKit));
				int depois = pl.Livro.MarcosLivres;
				ReconciliarDadiva(pl);   // ...e o tique de 1 Hz dos cargos devolve a skill ao livro
				ValCheca("(defeito injetado: reembolsa o que o cargo deu) a prova de cima REPROVA -- entram marcos por uma skill que ninguem "
						 + "comprou, e o tique dos cargos a devolve ao livro: a impressora",
						 depois > marcos && pl.Livro.Sabe(doKit), $"marcos {marcos}->{depois}, sabe de novo {pl.Livro.Sabe(doKit)}");

				// ...E A LISTA DE COMPRADAS FAZ DELA UMA IMPRESSORA DE UMA FOLHA SO: a skill que o tique devolveu entrou
				// por `Livro.Dar`, nao esta entre as compradas, e a segunda volta nao paga nada.
				Ouvir(() => Verbo(pl, "skill_esquecer", doKit));
				ValCheca("...e mesmo com esse defeito a SEGUNDA volta nao paga: a skill que o tique devolveu nao esta entre as compradas",
						 pl.Livro.MarcosLivres == depois && !pl.Livro.Sabe(doKit), $"marcos {depois}->{pl.Livro.MarcosLivres}");
			}
			finally { ReembolsaODoCargoDeTeste = false; pl.Livro.MarcosLivres = marcos; }
		}
		finally { _tronos.Remove("turtle"); }
	}

	/// <summary>
	/// 8a) OS OUTROS TRES VERBOS QUE ACHAVAM O ALVO NO MUNDO INTEIRO: a chave da Sala do Tempo (`view(6)`), a
	/// juventude (`view(1)`) e o corpo que fica (`view(src)`).
	/// </summary>
	private void ValAFamiliaDoPorNome(ZoneKey terra)
	{
		GD.Print("[validacoes] -- 8a) `sala_autorizar`, Restore_Youth e Keep_Body: o alvo a vista, e nao no mundo inteiro --");
		const int T = ZoneCollision.TileSize;
		ZoneKey namek = ZoneKey.Premade("Namek");

		// ---- a chave da Sala: `Permission(mob/M in view(6))` (`EarthRanks.dm:140`)
		Vec2 palco = CorredorLivre(24);
		ServerPlayer korin = ValGente("KorinDaBancada", "Human", palco, skills: ["/datum/skill/rank/Permission"]);
		ServerPlayer visita = ValGente("VisitaDaBancada", "Human", palco + new Vec2(10 * T, 0));
		visita.SalaAutorizada = false;

		List<string> chaveLonge = Ouvir(() => Verbo(korin, "sala_autorizar", visita.Id.ToString()));
		ValCheca("**A CHAVE DA SALA DO TEMPO SE ENTREGA A QUEM ESTA A VISTA** (`in view(6)`, `EarthRanks.dm:140`): a 10 tiles o servidor "
				 + "recusa, a chave nao nasce e o alvo nao ouve nada",
				 Disse(chaveLonge, "longe demais") && !visita.SalaAutorizada && !Disse(chaveLonge, "autorizou a sua entrada"), Ultimos(chaveLonge));

		visita.Zone = namek;
		visita.Pos = korin.Pos + new Vec2(T, 0);
		List<string> chaveDeOutroMundo = Ouvir(() => Verbo(korin, "sala_autorizar", visita.Name));
		ValCheca("...nem pelo NOME, pra quem esta em outro planeta", Disse(chaveDeOutroMundo, "longe demais") && !visita.SalaAutorizada,
				 Ultimos(chaveDeOutroMundo));
		visita.Zone = terra;

		AlvoSemAlcanceDeTeste = true;
		try
		{
			visita.Pos = korin.Pos + new Vec2(10 * T, 0);
			Ouvir(() => Verbo(korin, "sala_autorizar", visita.Id.ToString()));
			ValCheca("(defeito injetado: o alvo e qualquer corpo do mundo) a prova de cima REPROVA -- a chave nasce a 10 tiles",
					 visita.SalaAutorizada);
		}
		finally { AlvoSemAlcanceDeTeste = false; visita.SalaAutorizada = false; }

		visita.Pos = korin.Pos + new Vec2(5 * T, 0);
		List<string> chavePerto = Ouvir(() => Verbo(korin, "sala_autorizar", visita.Id.ToString()));
		ValCheca("O CAMINHO LEGITIMO: a 5 tiles (dentro dos 6) a chave nasce e o alvo ouve",
				 visita.SalaAutorizada && Disse(chavePerto, "autorizou a sua entrada"), Ultimos(chavePerto));
		visita.SalaAutorizada = false;

		korin.SalaAutorizada = false;
		Ouvir(() => Verbo(korin, "sala_autorizar", korin.Name));
		ValCheca("...e quem faz a chave continua podendo faze-la pra SI (o `view()` do DM inclui o proprio `usr`)", korin.SalaAutorizada);
		korin.SalaAutorizada = false;

		// ---- a juventude: `as null|mob in view(1)` (`OtherworldRankSkills.dm:165`)
		Vec2 palcoDoKai = CorredorLivre(24);
		ServerPlayer grandKai = ValGente("GrandKaiDaBancada", "Kai", palcoDoKai);
		ServerPlayer velho = ValGente("VelhoDaBancada", "Human", palcoDoKai + new Vec2(4 * T, 0));
		grandKai.AlvoId = velho.Id;
		bool Oferta() => _ofertasDeJuventudeG8.ContainsKey(velho.Conta);

		List<string> juventudeLonge = Ouvir(() => OferecerJuventudeG8(grandKai, "20"));
		ValCheca("**REJUVENESCER E UM GESTO DE QUEM ESTA COLADO** (`in view(1)`, `OtherworldRankSkills.dm:165`): com o marcado a 4 tiles "
				 + "a oferta nao nasce e ele nao ouve nada",
				 Disse(juventudeLonge, "longe demais") && !Oferta() && !Disse(juventudeLonge, "oferece devolver"), Ultimos(juventudeLonge));

		// A MIRA VELHA: o marcado trocou de planeta depois da marca (a mira so e conferida na hora de marcar).
		velho.Zone = namek;
		velho.Pos = grandKai.Pos + new Vec2(T, 0);
		List<string> juventudeDeOutroMundo = Ouvir(() => OferecerJuventudeG8(grandKai, "20"));
		ValCheca("...nem com a MIRA VELHA: o marcado foi pra outro planeta, e a oferta nao o segue",
				 Disse(juventudeDeOutroMundo, "longe demais") && !Oferta(), Ultimos(juventudeDeOutroMundo));

		bool corpoFicaAntes = velho.Ficha.KeepsBody;
		List<string> corpoDeOutroMundo = Ouvir(() => ManterOCorpoG8(grandKai));
		ValCheca("**`Keep_Body` SO ALCANCA QUEM ESTA A VISTA** (`in view(src)`, `OtherworldRankSkills.dm:195`): com a mira velha (o marcado "
				 + "em outro planeta) o bit nao muda",
				 Disse(corpoDeOutroMundo, "longe demais") && velho.Ficha.KeepsBody == corpoFicaAntes, Ultimos(corpoDeOutroMundo));

		AlvoSemAlcanceDeTeste = true;
		try
		{
			Ouvir(() => OferecerJuventudeG8(grandKai, "20"));
			Ouvir(() => ManterOCorpoG8(grandKai));
			ValCheca("(defeito injetado: o alvo e qualquer corpo do mundo) as duas provas de cima REPROVAM -- a oferta e o bit chegam a outro planeta",
					 Oferta() && velho.Ficha.KeepsBody != corpoFicaAntes);
		}
		finally { AlvoSemAlcanceDeTeste = false; _ofertasDeJuventudeG8.Remove(velho.Conta); velho.Ficha.KeepsBody = corpoFicaAntes; }

		velho.Zone = terra;
		velho.Pos = grandKai.Pos + new Vec2(T, 0);
		List<string> juventudePerto = Ouvir(() => OferecerJuventudeG8(grandKai, "20"));
		ValCheca("O CAMINHO LEGITIMO: colado, a oferta de juventude nasce e o alvo a ouve",
				 Oferta() && Disse(juventudePerto, "oferece devolver"), Ultimos(juventudePerto));
		_ofertasDeJuventudeG8.Remove(velho.Conta);

		velho.Pos = grandKai.Pos + new Vec2(15 * T, 0);
		Ouvir(() => ManterOCorpoG8(grandKai));
		ValCheca("...e o `Keep_Body` alcanca o marcado a 15 tiles, na mesma tela", velho.Ficha.KeepsBody != corpoFicaAntes);
		velho.Ficha.KeepsBody = corpoFicaAntes;
		grandKai.AlvoId = 0;
	}

	// =====================================================================
	// 9) a vigia do `PorNome`
	// =====================================================================
	/// <summary>
	/// OS ARQUIVOS DE SERVIDOR QUE PODEM CHAMAR `PorNome` (o resolvedor de ADMIN, que acha qualquer corpo do mundo),
	/// e QUANTAS VEZES. `-1` = a vontade (o proprio arquivo de admin). Um uso novo num verbo de jogador reprova a
	/// bancada: ou o alvo passa pelo <see cref="AlvoAoAlcance"/>, ou entra aqui de proposito, com o porque.
	/// </summary>
	private static readonly Dictionary<string, int> QuemPodeChamarPorNome = new(StringComparer.OrdinalIgnoreCase)
	{
		["GameServer.Admin.cs"] = -1,          // os verbs de admin (o funil confere `EhAdmin` antes de chegar la)
		["GameServer.Destruicao.cs"] = 1,      // `admin_...` da destruicao de planeta
		["GameServer.Torneio.cs"] = 1,         // `trn_inscrever`, depois do `EhAdmin`
		["GameServer.Convivio.cs"] = 1,        // `VerboAnotar`: confere zona e os 10 tiles por conta propria
		["GameServer.SalaDoTempo.cs"] = 1,     // `SoltarDaSala`: o preso esta DENTRO da Sala e o Guardiao fora (regra do dono)
		["GameServer.Validacoes.cs"] = 1,      // o `AlvoAoAlcance`, que e quem confere o alcance
		// (esta vigia pegou a linha abaixo no dia em que nasceu: o `GameServer.GodKi.cs` foi publicado com um
		// `PorNome` novo enquanto a bancada rodava. Conferido: e o `admin_godki`, alcancado so pelo funil de admin.)
		["GameServer.GodKi.cs"] = 1,           // `AdminDespertarOGodKi` (`admin_godki`), depois do `EhAdmin`
		// (2026-10-17) o console do servidor dedicado: quem digita e o dono da maquina, e nao um jogador (`TerminalExpulsar`, o `expulsar`).
		// A vigia o acusava desde que ele entrou (2026-10-14), em toda rodada.
		["GameServer.Terminal.cs"] = 1,
	};

	private static List<string> PorNomeForaDaLista(IEnumerable<(string Nome, string[] Linhas)> fontes)
	{
		var fora = new List<string>();
		foreach ((string nome, string[] linhas) in fontes)
		{
			int usos = linhas.Count(l => System.Text.RegularExpressions.Regex.IsMatch(SemTextoNemComentario(l), @"\bPorNome\(")
										 && !l.Contains("ServerPlayer? PorNome(", StringComparison.Ordinal));
			if (usos == 0) continue;
			if (!QuemPodeChamarPorNome.TryGetValue(nome, out int teto)) fora.Add($"{nome} ({usos})");
			else if (teto >= 0 && usos > teto) fora.Add($"{nome} ({usos}, a lista deixa {teto})");
		}
		return fora;
	}

	/// <summary>
	/// OS FONTES DO SERVIDOR QUE AS VIGIAS LEEM: so a pasta `Server` deste projeto (o `res://` contem as worktrees, e
	/// uma varredura recursiva contaria copias), sem os arquivos de bancada -- eles apertam por oficio.
	/// </summary>
	private static List<(string Nome, string[] Linhas)> FontesDoServidor()
	{
		var fontes = new List<(string Nome, string[] Linhas)>();
		string pasta = ProjectSettings.GlobalizePath("res://Server");
		if (System.IO.Directory.Exists(pasta))
			foreach (string arq in System.IO.Directory.EnumerateFiles(pasta, "*.cs", System.IO.SearchOption.TopDirectoryOnly))
			{
				string nome = System.IO.Path.GetFileName(arq);
				if (nome.Contains("Teste", StringComparison.Ordinal)) continue;   // bancada aperta por oficio
				fontes.Add((nome, System.IO.File.ReadAllLines(arq)));
			}
		return fontes;
	}

	private void ValVigiaDoPorNome()
	{
		GD.Print("[validacoes] -- 9) a vigia: `PorNome` (o resolvedor de admin) nao entra em verbo de jogador --");
		List<(string Nome, string[] Linhas)> fontes = FontesDoServidor();

		List<string> fora = PorNomeForaDaLista(fontes);
		ValCheca($"**NENHUM VERBO DE JOGADOR ACHA O ALVO PELO `PorNome`** ({fontes.Count} fontes do servidor lidos): fora da lista de quem pode, "
				+ "o alvo passa pelo `AlvoAoAlcance` (mesma zona + os tiles do verbo)",
				fontes.Count > 100 && fora.Count == 0, $"{fontes.Count} fontes; fora da lista: {string.Join(", ", fora)}");

		(string Nome, string[] Linhas) intruso = ("GameServer.VerboNovo.cs",
			new[] { "\t\tServerPlayer? alvo = PorNome(arg);   // um verbo novo, escrito amanha" });
		List<string> comIntruso = PorNomeForaDaLista(fontes.Append(intruso));
		ValCheca("(defeito injetado: um verbo novo que resolve o alvo pelo `PorNome`) a vigia REPROVA, e diz o arquivo",
				comIntruso.Exists(f => f.StartsWith("GameServer.VerboNovo.cs", StringComparison.Ordinal)), string.Join(", ", comIntruso));
	}

	// =====================================================================
	// 9b) a vigia do mapa de arquivo
	// =====================================================================
	/// <summary>
	/// OS ARQUIVOS DE SERVIDOR QUE PODEM LER O MAPA DE COLISAO DE UMA ZONA DE ARQUIVO DIRETO (o `.Mapa` de uma entrada
	/// do catalogo), e EM QUANTAS LINHAS. Esse mapa so existe pras zonas pre-feitas: quem pergunta por parede
	/// atraves dele recebe nulo em planeta gerado, em nave, em mente -- e pula a conferencia. Foi assim que a
	/// piscada do Zanzoken, a investida do soco, o puxao da fusao e o embate de Zanzoken atravessavam montanha.
	/// "ONDE HA PAREDE" SE PERGUNTA AO FUNIL (`MapaDaZonaOuCatalogo`, `GameServer.Volta.cs`; pros saltos, o
	/// `MapaDoSalto`). Uma leitura nova fora desta lista reprova a bancada: ou vai pelo funil, ou entra aqui de
	/// proposito, com o porque.
	/// </summary>
	private static readonly Dictionary<string, int> QuemPodeLerOMapaDeArquivo = new(StringComparer.OrdinalIgnoreCase)
	{
		["GameServer.cs"] = 8,               // o CARREGADOR das zonas de arquivo: e ele que enche o campo (colisao, agua, nuvem, duro, dentro)
		["GameServer.Volta.cs"] = 1,         // o FUNIL: `MapaDaZonaOuCatalogo` (a metade do planeta de arquivo)
		["GameServer.Interiores.cs"] = 2,    // a metade INTERIOR do funil: a PLANTA de um interior feito de mapa de arquivo (copiada por instancia), e os mapas de quem so tem a chave de estado na mao -- e por ali que a porta abre e fecha, na zona de arquivo e na instancia
		["GameServer.Procedural.cs"] = 1,    // `MapaDaZona`, a metade "arquivo ou gerado" do mesmo funil
		["GameServer.Validacoes.cs"] = 1,    // `MapaDoSalto` com o defeito injetado ligado (a prova de que a vigia faz falta)
		["GameServer.Passagens.cs"] = 2,     // passagem so existe em zona de arquivo: lacra a celula dela e le a altura do mapa
		["GameServer.Parada.cs"] = 3,        // a PARADA devolve cada mapa de arquivo ao arquivo (o que caiu fecha, a rebrota tomba, a rocha sai): nao e pergunta de parede, e mundo sorteado e interior nao passam por ali (um e descarregado, o outro renasce da planta)
		["GameServer.Nuvem.cs"] = 1,         // a Nuvem Voadora pousa em zona de arquivo: a chegada e uma entrada do catalogo
		["GameServer.Lenha.cs"] = 1,         // `LerArvoresDoArquivo`: as arvores que o ARQUIVO da zona traz (nao e pergunta de parede)
		["GameServer.FotoDaMuda.cs"] = 3,    // bancada de foto (o nome nao diz "Teste"): o palco dela e uma zona de arquivo
	};

	/// <summary>
	/// As linhas de codigo (sem texto nem comentario) em que um fonte le `.Mapa` -- ou o casa num padrao de
	/// propriedade, `{ Mapa: ... }`. O `campo.Mapa` do `CampoDeRochas` e OUTRO campo (o mapa em que as rochas
	/// daquela zona estao, que ja vem do funil) e nao conta.
	/// </summary>
	private static List<string> MapaDeArquivoForaDaLista(IEnumerable<(string Nome, string[] Linhas)> fontes)
	{
		var fora = new List<string>();
		foreach ((string nome, string[] linhas) in fontes)
		{
			int usos = linhas.Count(l => System.Text.RegularExpressions.Regex.IsMatch(
				System.Text.RegularExpressions.Regex.Replace(SemTextoNemComentario(l), @"\bcampo\??\.Mapa\b", ""), @"(?:\.|\{\s*)Mapa\b"));
			if (usos == 0) continue;
			if (!QuemPodeLerOMapaDeArquivo.TryGetValue(nome, out int teto)) fora.Add($"{nome} ({usos})");
			else if (usos > teto) fora.Add($"{nome} ({usos}, a lista deixa {teto})");
		}
		return fora;
	}

	private void ValVigiaDoMapaDeArquivo()
	{
		GD.Print("[validacoes] -- 9b) a vigia: parede se pergunta ao FUNIL, e nao ao mapa de arquivo (que planeta gerado nao tem) --");
		List<(string Nome, string[] Linhas)> fontes = FontesDoServidor();

		List<string> fora = MapaDeArquivoForaDaLista(fontes);
		ValCheca($"**NINGUEM CONFERE PAREDE SO NO MAPA DE ARQUIVO** ({fontes.Count} fontes do servidor lidos): fora da lista de quem pode ler "
				+ "o `.Mapa` de uma zona de arquivo (o carregador, o funil, as portas e passagens), a pergunta vai pelo `MapaDaZonaOuCatalogo`",
				fontes.Count > 100 && fora.Count == 0, $"{fontes.Count} fontes; fora da lista: {string.Join(", ", fora)}");

		// OS QUATRO QUE ATRAVESSAVAM: nenhuma leitura direta sobrou neles (zero, e nao "dentro do teto").
		string[] consertados = ["GameServer.Combat.cs", "GameServer.Fusao.cs", "GameServer.ZanzoClash.cs", "GameServer.Empurrao.cs", "GameServer.Zanzoken.cs"];
		List<string> aindaLeem = MapaDeArquivoForaDaLista(fontes.Where(f => consertados.Contains(f.Nome, StringComparer.OrdinalIgnoreCase)));
		ValCheca("...e nos cinco arquivos que atravessavam (o soco, o puxao da fusao, o embate de Zanzoken, o martelo da borda, a piscada) "
				+ "nao sobrou leitura direta nenhuma",
				fontes.Count(f => consertados.Contains(f.Nome, StringComparer.OrdinalIgnoreCase)) == consertados.Length && aindaLeem.Count == 0,
				string.Join(", ", aindaLeem));

		(string Nome, string[] Linhas) intruso = ("GameServer.SaltoNovo.cs",
			new[] { "\t\tZoneCollision? mapa = _catalogo?.Get(pl.Zone)?.Mapa;   // um salto novo, escrito amanha" });
		(string Nome, string[] Linhas) aMais = ("GameServer.Passagens.cs",
			[.. fontes.Find(f => f.Nome == "GameServer.Passagens.cs").Linhas ?? [], "\t\tif (_catalogo?.Get(zona) is not { Mapa: { } m }) return;"]);
		List<string> comIntruso = MapaDeArquivoForaDaLista(fontes.Where(f => f.Nome != "GameServer.Passagens.cs").Append(intruso).Append(aMais));
		ValCheca("(defeito injetado: um salto novo que le o mapa de arquivo, e uma leitura a mais num arquivo da lista) a vigia REPROVA os "
				+ "dois, e diz os arquivos",
				comIntruso.Exists(f => f.StartsWith("GameServer.SaltoNovo.cs", StringComparison.Ordinal))
				&& comIntruso.Exists(f => f.StartsWith("GameServer.Passagens.cs", StringComparison.Ordinal)), string.Join(", ", comIntruso));

		// O `campo.Mapa` NAO E O MAPA DE ARQUIVO, e um comentario que cita o caminho velho nao e codigo.
		(string Nome, string[] Linhas) inocente = ("GameServer.Inocente.cs",
			new[] { "\t\tif (campo.Mapa != null) campo.Mapa.TirarRocha(x, y);", "\t\t// era `_catalogo?.Get(zona)?.Mapa`, que planeta gerado nao tem" });
		ValCheca("CONTRA-EXEMPLO: o `campo.Mapa` das rochas (outro campo) e um comentario que cita o caminho velho nao acusam",
				MapaDeArquivoForaDaLista([inocente]).Count == 0);
	}
}
