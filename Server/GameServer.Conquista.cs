using System.Text.Json;
using Godot;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// UM DOMINIO PLANETARIO -- uma linha do `conq_data` do DM (PlanetConquest.dm:62), rechaveada.
///
/// ============================ A CHAVE E A DO JOGO, E ELA JA EXISTIA ============================
/// O DM indexa por `conq_data["Planeta"]` -- uma STRING de nome -- e `conq_premade()` e literalmente
/// `pl == "Earth" || pl == "Vegeta" || pl == "Namek"`. As duas coisas morreram com os sistemas
/// solares: ha planetas gerados em qualquer lugar e os nomes deles COLIDEM.
///
/// A resposta **nao foi inventada aqui**: e a <see cref="ChaveDePlaneta"/>, que a destruicao de
/// planeta trouxe e cujo cabecalho ja explica o porque (o `% 1000` do nome gerado colide e a
/// concatenacao e ambigua). Conquista e destruicao sao dois destinos do MESMO planeta -- se cada uma
/// dissesse "que planeta e este" do seu jeito, o dia em que um mundo explodisse com dono seria o dia
/// em que as duas listas discordariam.
///
/// Os campos do disco sao os tres da chave. `Zona` e `Chave` sao derivados.
/// ==========================================================================================
/// </summary>
public sealed class Dominio
{
	/// <summary>Mapa feito a mao (a chave e o nome) ou mundo sorteado (a chave e a seed).</summary>
	public bool PreFeito;

	/// <summary>Nome do corpo. **E chave** no pre-feito; no gerado e so rotulo -- ver `ChaveDePlaneta`.</summary>
	public string Planeta = "";

	/// <summary>Seed do corpo. **E a chave** no gerado, e zero no pre-feito.</summary>
	public ulong Seed;

	/// <summary>
	/// ============================ O ENDERECO NO CEU -- POSICAO, NAO IDENTIDADE ============================
	/// A trinca (sistema, sistema, orbita), a mesma que o <see cref="Jandirus.Core.Races.Berco"/>
	/// guarda. Ela **nao** e uma segunda chave: quem diz "qual planeta e este" e a
	/// <see cref="Chave"/>, e esta trinca diz outra coisa -- ONDE ELE FICA.
	///
	/// Ela e guardada porque a identidade deliberadamente nao carrega posicao, e ha um caso que
	/// precisa dela: renascer num dominio GERADO cuja superficie esta descarregada. O DM desiste
	/// nesse caso (:712-715, *"por ora, spawn padrao"*); aqui o funil do berco poe o corpo em ORBITA
	/// e o `TickDoEspaco` pousa quando o mundo nascer -- mas pra isso e preciso saber onde e a orbita.
	///
	/// `K = -1` = nao resolve (save de outro universo, ou lugar fora da carta).
	/// ==================================================================================================
	/// </summary>
	public int Sx, Sy, K = -1;

	/// <summary>
	/// A ASSINATURA do soberano -- o `R["sig"]` do DM (:421). E do PERSONAGEM, e e ela que manda:
	/// territorio e de alguem especifico, e nao da conta inteira.
	/// </summary>
	public string Assinatura = "";

	/// <summary>
	/// A CONTA -- o `ckey` do DM, guardado na mesma linha (:421) e usado pela LIBERTACAO (:572-574),
	/// que pergunta a reputacao do EX-DONO. A reputacao deste port e por conta (ver
	/// `GameServer.Reputacao.cs`), entao sem este campo aquela regra nao teria como ser feita.
	/// </summary>
	public string Conta = "";

	/// <summary>O nome que o mundo le. Atualizado quando o dono aparece -- ninguem renomeia um trono.</summary>
	public string Nome = "";

	/// <summary>Segundos do <see cref="GameServer.TempoDoMundo"/> -- o `world.realtime` do DM.</summary>
	public double Desde, Coletado, Visto;

	/// <summary>Onde a bandeira ficou fincada, em pixels da zona.</summary>
	public float Fx, Fy;

	/// <summary>Ver <see cref="Conquista.LealdadeMaxima"/>. **Nao existe no DM** -- e o "manter custa".</summary>
	public double Lealdade = Conquista.LealdadeMaxima;

	/// <summary>
	/// Este e o dominio em que o dono RENASCE? O `conq_spawn` do DM (:67), que la e campo do mob.
	///
	/// MORA NO DOMINIO E NAO NO PERSONAGEM de proposito: o DM precisa de uma conferencia no login
	/// (*"o dominio-spawn ainda e seu?"*, :214-216) justamente porque a escolha vive longe da posse.
	/// Aqui a escolha morre junto com o dominio, sozinha -- e nao ha campo novo no save de
	/// personagem, que era a outra opcao e a que mexeria na forma do JSON de todo mundo.
	/// </summary>
	public bool EhOSpawn;

	public ChaveDePlaneta Chave => new(PreFeito, Planeta, Seed);

	public ZoneKey Zona => PreFeito ? ZoneKey.Premade(Planeta) : ZoneKey.Procedural(Planeta, Seed);

	/// <summary>Pro log: "[1:-2]k0". Posicao, nao identidade -- ver <see cref="Sx"/>.</summary>
	public string Endereco => $"[{Sx}:{Sy}]k{K}";
}

/// <summary>
/// **A CONQUISTA DE PLANETAS EM JOGO** -- o livro dos dominios, o tributo, a lealdade e a API.
///
/// Porte de `Code/Modules/Globals/PlanetConquest.dm`. A INVASAO mora no irmao
/// (`GameServer.Invasao.cs`); aqui esta o que existe quando ninguem esta lutando.
///
/// ============================ A BANDEIRA NAO E UM OBJETO NO MUNDO, E ISSO E DELIBERADO ============================
/// No DM ela e um `obj/Conquest_Flag` com `icon = 'ConquestFlag.dmi'`. Este port tem uma porta pra
/// um objeto no chao -- a `Obra` -- e ela esteve fechada por um defeito que era dela: a `Obra`
/// guardava a zona como **string de nome** e a remontava com `ZoneKey.Premade(...)`, entao uma
/// bandeira erguida num planeta GERADO voltaria do disco numa zona pre-feita que nao existe, e dois
/// gerados homonimos dividiriam a mesma bandeira. **Isso foi consertado** (ver `Obra.ZonaTipo`): a
/// `Obra` guarda hoje a `ZoneKey` inteira, e a porta esta aberta.
///
/// O QUE FALTA PRA BANDEIRA VIRAR OBJETO E ARTE, e nao chave: `Images/ConquestFlag.dmi` nunca foi
/// convertida. E note que a `Obra` continuaria sem responder a pergunta que a conquista faz -- "que
/// PLANETA e este?" --, que e da <see cref="ChaveDePlaneta"/>: uma bandeira-objeto guardaria a zona
/// onde ela esta fincada, e o dominio continuaria sendo o dono da linha do livro.
///
/// Entao a bandeira e o que ela sempre foi de verdade -- uma POSICAO no livro (`Fx`,`Fy`) -- e tudo
/// o que dependia dela como objeto mede distancia contra essa posicao, que e a mesma conta que o
/// `get_dist(usr, src) > 1` do DM fazia. O que se perde e o sprite, e ele esta em
/// <see cref="Conquista.OqueFalta"/> em vez de fingido.
///
/// E O REPLANTIO DO DM SUMIU DE GRACA: `conq_on_surface_ready` (:349) existe porque a superficie
/// procedural do BYOND era destruida e refeita, levando o objeto junto. Aqui o terreno e funcao PURA
/// da seed (regra 0.2) -- a mesma celula volta livre na mesma coordenada --, entao a posicao guardada
/// continua valendo sem ninguem replantar nada.
/// ==============================================================================================================
/// </summary>
public partial class GameServer
{
	/// <summary>Os dominios do mundo. Lista e nao dicionario: sao poucos, e a busca e por chave E por dono.</summary>
	private readonly List<Dominio> _dominios = [];

	/// <summary>
	/// assinatura -> recados guardados. E o `conq_notify` (:63): "seu dominio foi invadido", "voce
	/// perdeu X". Um aviso desses so vale se chegar, e o dono costuma estar offline na hora.
	/// </summary>
	private readonly Dictionary<string, List<string>> _recadosDeConquista = new(StringComparer.Ordinal);

	/// <summary>Quando a lealdade foi cobrada pela ultima vez (segundos do <see cref="TempoDoMundo"/>).</summary>
	private double _ultimaCobrancaDeLealdade;

	/// <summary>O que a conquista disse -- ligada so pela bancada, como a `EscutaDasSagas`.</summary>
	internal static List<string>? EscutaDaConquista;

	private string CaminhoDaConquista =>
		System.IO.Path.Combine(_store?.Pasta ?? ".", "conquista.json");

	// =====================================================================
	// DISCO
	// =====================================================================
	/// <summary>O que vai pro arquivo. Tipo proprio pelo mesmo motivo do `LivroDeReputacao`.</summary>
	private sealed class LivroDeConquista
	{
		/// <summary>
		/// ============================ O CARIMBO DA ULTIMA COBRANCA DE LEALDADE ============================
		/// **PRIMEIRO CAMPO DO ARQUIVO** de proposito -- ver a familia C da `--conquistateste`, que
		/// injeta "um save ANTIGO, sem este campo" apagando a linha; com ele no meio, apagar a linha
		/// deixaria o JSON invalido e a bancada estaria medindo um arquivo quebrado em vez da
		/// compatibilidade.
		///
		/// ELE PRECISA IR PRO DISCO, e a bancada foi quem mostrou. O <see cref="TempoDoMundo"/> e
		/// absoluto (UTC) e por isso a AUSENCIA atravessa um desligamento -- mas o quanto se COBRA
		/// dela vinha de `_ultimaCobrancaDeLealdade`, que nascia zerado a cada boot: o primeiro tique
		/// reancorava e o intervalo em que o servidor esteve DESLIGADO nunca era cobrado de ninguem.
		///
		/// Ou seja: o "manter custa" valia so pelo tempo de uptime, e quem quisesse guardar tres
		/// dominios sem nunca aparecer so precisava que o servidor reiniciasse -- exatamente o buraco
		/// que o cabecalho da <see cref="Conquista.Lealdade"/> diz estar fechado.
		///
		/// Zero (save antigo, ou arquivo inexistente) cai na ancora de sempre: nao se cobra do
		/// passado que ninguem mediu.
		/// ============================================================================================
		/// </summary>
		public double Cobranca;

		public List<Dominio> Dominios = [];
		public Dictionary<string, List<string>> Recados = [];
	}

	private void CarregarConquista()
	{
		try
		{
			if (System.IO.File.Exists(CaminhoDaConquista))
			{
				LivroDeConquista? l = JsonSerializer.Deserialize<LivroDeConquista>(
					System.IO.File.ReadAllText(CaminhoDaConquista),
					new JsonSerializerOptions { IncludeFields = true });

				if (l != null)
				{
					_dominios.AddRange(l.Dominios);
					foreach ((string sig, List<string> q) in l.Recados) _recadosDeConquista[sig] = q;

					// O CARIMBO VOLTA DO DISCO -- e o que faz o tempo de servidor desligado ser
					// cobrado. Ver `LivroDeConquista.Cobranca`.
					_ultimaCobrancaDeLealdade = l.Cobranca;
				}
			}
		}
		catch (Exception e) { GD.PushWarning($"[server] conquista.json ilegivel: {e.Message}"); }

		// ============================ O SAVE PODE SER DE OUTRO UNIVERSO ============================
		// A identidade de um mundo gerado e a SEED dele, e uma seed de universo diferente nao produz
		// aquele mundo em lugar nenhum. Um dominio que nao resolve nao pode virar "dominio de um
		// planeta que nao existe": ele some, alto, no boot. Mesma disciplina do `CarregarSagas` com
		// elo orfao.
		// ======================================================================================
		int orfaos = _dominios.RemoveAll(d => !d.PreFeito && CorpoDoDominio(d) == null);
		if (orfaos > 0)
			GD.PushWarning($"[server] conquista: {orfaos} dominio(s) apontavam pra um mundo que nao "
						 + "existe nesta seed de universo -- descartados");

		ValidarDominios();

		GD.Print($"[server] conquista: {_dominios.Count} dominio(s) de pe"
			   + (_dominios.Count > 0
					? " -- " + string.Join(", ", _dominios.Select(d => $"{d.Planeta} ({d.Nome}, lealdade {d.Lealdade:0})"))
					: ""));

		// A DIVIDA SAI NO BOOT, como a da reputacao e o recorte do inimigo comum. Ver o porque la.
		GD.Print($"[server] conquista: faltam {Conquista.OqueFalta.Length} -- "
			   + string.Join("; ", Conquista.OqueFalta));

		SalvarConquista();
	}

	private void SalvarConquista()
	{
		if (_store == null) return;
		try
		{
			var l = new LivroDeConquista { Dominios = _dominios, Cobranca = _ultimaCobrancaDeLealdade };
			foreach ((string sig, List<string> q) in _recadosDeConquista) l.Recados[sig] = q;

			// TEMPORARIO E RENOMEIA, como o `reputacao.json` e o `planetas-mortos.json`.
			string tmp = CaminhoDaConquista + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(l,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDaConquista, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar conquista.json: {e.Message}"); }
	}

	// =====================================================================
	// A API -- e por aqui que as QUESTS DE CARGO perguntam
	// =====================================================================
	/// <summary>O dominio deste planeta, ou nulo. E o `conq_owner_sig` (:78) com o objeto inteiro.</summary>
	public Dominio? DominioDe(ChaveDePlaneta chave) => _dominios.Find(d => d.Chave == chave);

	/// <summary>O dominio da superficie em que alguem esta. Nulo tambem quando o lugar nao e planeta.</summary>
	public Dominio? DominioDaZona(ZoneKey z) =>
		ChaveDePlaneta.Da(z) is { } c ? DominioDe(c) : null;

	/// <summary>
	/// Os dominios desta ASSINATURA. Por assinatura porque e assim que o DM pergunta
	/// (`R["sig"] == usr.signature`): territorio e do personagem, nao da conta.
	/// </summary>
	public List<Dominio> DominiosDe(string assinatura) => assinatura.Length == 0
		? []
		: [.. _dominios.Where(d => string.Equals(d.Assinatura, assinatura, StringComparison.Ordinal))];

	/// <summary>`conq_count_owned` (:101). E o teto do <see cref="Conquista.MaximoDeDominios"/>.</summary>
	public int QuantosDomina(string assinatura) => DominiosDe(assinatura).Count;

	/// <summary>
	/// `conq_owns_any` (:88) -- no DM, o gate de compra da criacao de Esferas do Cla do Dragao.
	///
	/// **ELA E CHAMADA**, e nao decorativa: alimenta o
	/// <see cref="Jandirus.Core.Ranks.FichaDeRank.PlanetasDominados"/>, que e o que faz uma regra de
	/// cargo poder dizer "conquiste um planeta". Uma API de conquista que ninguem consultasse
	/// repetiria o defeito que este projeto ja pagou uma vez -- a API de sigilo de BP, escrita e 100%
	/// orfa.
	/// </summary>
	public bool DominaAlgum(string assinatura) => QuantosDomina(assinatura) > 0;

	/// <summary>
	/// O SUFIXO DE LISTAGEM -- `conq_owner_tag` (:95). Por ZONA e nao por nome: a zona ja carrega a
	/// seed, entao dois mundos gerados homonimos nao trocam de rotulo.
	/// </summary>
	public string TagDoDono(ZoneKey z) =>
		DominioDaZona(z) is { } d ? $" · Domínio de {d.Nome}" : "";

	/// <summary>
	/// ESTE PLANETA TEM POVO? A divisao que substituiu o `conq_premade` do DM.
	///
	/// A pergunta e sobre a LINHA DE POVOAMENTO e nao sobre o tipo da zona: Arconia e pre-feita e
	/// nao tem um habitante sequer, e um mundo gerado tambem nao. Perguntar "e pre-feito?" daria a
	/// Arconia uma guarnicao que nao existe e um tributo de mundo habitado.
	/// </summary>
	public bool TemPovo(string nomeDoPlaneta) =>
		_moldes?.Plano.Any(l => string.Equals(l.Planeta, nomeDoPlaneta, StringComparison.OrdinalIgnoreCase)) == true;

	/// <summary>
	/// ============================ O PLANETA AINDA EXISTE? -- `conq_planet_gone` (:109) ============================
	/// **NAO HA UMA SEGUNDA RESPOSTA AQUI.** Quem sabe se um mundo morreu e o sistema de destruicao de
	/// planeta (`GameServer.Destruicao.cs`), que nasceu em paralelo a isto e cujo `ZonaMorta` e a
	/// pergunta unica -- ele ja e quem responde ao berco, ao pouso e a carta estelar. Duplicar o
	/// criterio aqui (lendo o `Condenado` das sagas, por exemplo) daria a conquista uma nocao propria
	/// de "morto" que divergiria no dia em que a destruicao mudasse a dela.
	///
	/// O que sobra de PROPRIO e uma linha: o pre-feito que sumiu do MANIFESTO de mapas -- um mundo
	/// sem mapa nao e um mundo destruido, e o `ZonaMorta` nao tem por que saber disso.
	///
	/// O DM varre isto de 60 em 60 s (`conq_watch_loop`, :179). Aqui nao ha varredura separada: o
	/// <see cref="TickDaConquista"/> ja passa por todos os dominios de segundo em segundo pra cobrar
	/// lealdade, e perguntar isto ali custa uma comparacao por dominio.
	/// ========================================================================================================
	/// </summary>
	private bool PlanetaDoDominioMorreu(Dominio d)
	{
		ZoneKey z = d.Zona;
		if (ZonaMorta(z)) return true;
		return d.PreFeito && _catalogo?.Get(z) == null;
	}

	// =====================================================================
	// ONDE EU ESTOU / ONDE FICA
	// =====================================================================
	/// <summary>
	/// O CORPO CELESTE DE UMA ZONA, ou nulo quando aquela zona nao e um mundo (espaco, interior,
	/// Paraiso, Inferno).
	///
	/// Uma casa so: o pre-feito sai de `Espaco.PreFeitos()` e o gerado sai do registro de mundos
	/// vivos, que e quem sabe onde um mundo sorteado fica.
	/// </summary>
	public PlanetaNoEspaco? CorpoDaZona(ZoneKey zona)
	{
		if (!Espaco.EhPlaneta(zona)) return null;

		if (EhZonaProcedural(zona))
			return _zonasGeradas.TryGetValue(zona.Hash, out ZonaGerada? v) ? v.NoEspaco : null;

		foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
			if (string.Equals(p.Nome, zona.Name, StringComparison.OrdinalIgnoreCase)) return p;
		return null;
	}

	/// <summary>
	/// ONDE ESTE CORPO FICA NO CEU -- a trinca (Sx, Sy, K), ou `K = -1` quando ele nao esta na carta.
	///
	/// PRE-FEITO sai da lista de sistemas ancorados (sete entradas fixas). GERADO sai da POSICAO: o
	/// `Sistemas.PorPerto` devolve o 3x3 de celulas que alcanca aquele ponto -- o mesmo recorte que a
	/// carta estelar ja usa, com a conta de folga escrita la. A conferencia e pela SEED, que e a
	/// identidade; casar por nome devolveria o vizinho homonimo.
	/// </summary>
	private static (int Sx, int Sy, int K) EnderecoDoCorpo(ulong seedDoUniverso, PlanetaNoEspaco corpo)
	{
		if (corpo.Premade)
		{
			foreach (SistemaSolar s in Sistemas.ComPreFeito)
				if (string.Equals(s.PreFeito.Nome, corpo.Nome, StringComparison.OrdinalIgnoreCase))
					return (s.Id.Sx, s.Id.Sy, s.OrbitaPreFeita);
			return (0, 0, -1);   // Paraiso e Inferno: existem como zona, nao como corpo
		}

		var perto = new List<SistemaSolar>();
		Sistemas.PorPerto(seedDoUniverso, corpo.Pos, perto);
		foreach (SistemaSolar s in perto)
			for (int k = 0; k < s.Orbitas; k++)
				if (s.Planeta(k).Seed == corpo.Seed) return (s.Id.Sx, s.Id.Sy, k);

		return (0, 0, -1);
	}

	/// <summary>
	/// O CORPO DE UM DOMINIO, resolvido do endereco guardado. Nulo = o mundo nao existe nesta seed.
	///
	/// A CONFERENCIA DE IDENTIDADE NAO E OPCIONAL: o endereco e POSICAO, e a mesma celula noutro
	/// universo tem outro mundo. Sem comparar a seed, um save trazido de outro servidor entregaria o
	/// planeta que por acaso estivesse naquela orbita.
	/// </summary>
	private PlanetaNoEspaco? CorpoDoDominio(Dominio d)
	{
		if (d.PreFeito)
		{
			foreach (PlanetaNoEspaco p in Espaco.PreFeitos())
				if (string.Equals(p.Nome, d.Planeta, StringComparison.OrdinalIgnoreCase)) return p;
			return null;
		}

		if (d.K < 0 || Sistemas.Do(SeedDoUniverso, d.Sx, d.Sy) is not { } s || d.K >= s.Orbitas) return null;
		PlanetaNoEspaco corpo = s.Planeta(d.K);
		return corpo.Seed == d.Seed ? corpo : null;
	}

	/// <summary>"Earth" e nome de mapa; "Terra" e o que o jogador le. Mesma funcao do `conq_pname` (:128).</summary>
	private static string NomeVisivel(string zona) => NomeDoPlaneta(zona);

	// =====================================================================
	// O TIQUE: manter custa
	// =====================================================================
	/// <summary>
	/// ============================ A CONTA DA LEALDADE, UMA VEZ POR SEGUNDO ============================
	/// Tres coisas por dominio, todas baratas: o planeta ainda existe, o dono esta la (presenca) e a
	/// lealdade anda. A lista tem no maximo alguns itens -- nao ha varredura de mundo aqui.
	///
	/// O RELOGIO E O <see cref="TempoDoMundo"/> (UTC + o adianto do ceu), e nao o `_relogioDoMundo`
	/// acumulado, por DOIS motivos que puxam pro mesmo lado:
	///   * o tributo do DM e por hora REAL (`world.realtime`, :124), e lealdade e tributo tem que
	///     medir a MESMA ausencia -- dois relogios dariam duas negligencias diferentes;
	///   * ele ANDA COM O SERVIDOR DESLIGADO, que e o unico jeito de "tres dias sem aparecer"
	///     significar tres dias. Um acumulador de `dt` zeraria a ausencia a cada reinicio, e o
	///     jogador manteria o mundo inteiro so reiniciando o servidor.
	///
	/// E ele e adiantavel pela MESMA manivela do ceu (`_adiantoDoCeu`), que e o que torna esta regra
	/// testavel em vez de uma promessa de tres dias.
	/// ==========================================================================================
	/// </summary>
	private void TickDaConquista()
	{
		double agora = TempoDoMundo;

		// PRIMEIRA VOLTA: ancora e nao cobra nada. Sem isto o primeiro passo mediria "desde 1970".
		if (_ultimaCobrancaDeLealdade <= 0) { _ultimaCobrancaDeLealdade = agora; return; }

		// O RELOGIO PODE RECUAR (a bancada do ceu desfaz o adianto). Reancora sem cobrar, como o
		// `ContarOsDias` das sagas -- e pela mesma razao: nada do que passou se desfaz.
		if (agora < _ultimaCobrancaDeLealdade) { _ultimaCobrancaDeLealdade = agora; return; }

		double horasDoPasso = (agora - _ultimaCobrancaDeLealdade) / 3600.0;
		_ultimaCobrancaDeLealdade = agora;
		if (horasDoPasso <= 0 || _dominios.Count == 0) return;

		bool mexeu = false;
		foreach (Dominio d in _dominios.ToList())
		{
			// 1. O PLANETA AINDA EXISTE? Ver `PlanetaDoDominioMorreu` -- e o encontro com a destruicao.
			if (PlanetaDoDominioMorreu(d))
			{
				PerderDominio(d, $"Seu domínio {NomeVisivel(d.Planeta)} foi DESTRUÍDO. O território se perdeu.",
							  "planeta-morto", anunciar: false);
				mexeu = true;
				continue;
			}

			// 2. O DONO ESTA LA? Presenca e estar VIVO na zona do dominio -- um corpo caido no chao
			// nao governa nada, e e o mesmo criterio que o DM usa pra tudo (`!M.dead`).
			bool presente = false;
			foreach (ServerPlayer p in Jogadores)
			{
				if (p.Ficha.dead || !string.Equals(p.Assinatura, d.Assinatura, StringComparison.Ordinal)) continue;
				if (p.Zone.Hash != d.Zona.Hash) continue;
				presente = true;

				// O NOME ACOMPANHA O DONO: o livro guarda o ultimo conhecido, como o
				// `planet_rep_names` da reputacao, pelo mesmo motivo (a tela cita o nome).
				d.Nome = p.Name;
				d.Conta = p.Conta;
				break;
			}

			double horasDesde = Math.Max(agora - d.Visto, 0) / 3600.0;
			double antes = d.Lealdade;
			d.Lealdade = Conquista.Lealdade(antes, presente, horasDesde, horasDoPasso);
			if (presente) d.Visto = agora;
			if (Math.Abs(d.Lealdade - antes) > 1e-9) mexeu = true;

			// 3. O AVISO DE TRAVESSIA. Sem ele o jogador perde um planeta sem nunca ter lido que
			// estava perdendo -- ver `Conquista.TravessiaDeLealdade`.
			string frase = Conquista.TravessiaDeLealdade(antes, d.Lealdade, NomeVisivel(d.Planeta));
			if (frase.Length > 0) RecadoAoDono(d.Assinatura, frase);

			// 4. ZEROU: o povo derruba a bandeira. **Isto nao existe no DM** -- ver o cabecalho da
			// `Conquista.Lealdade`.
			if (d.Lealdade <= 0)
			{
				PerderDominio(d,
					$"Você PERDEU {NomeVisivel(d.Planeta)}: sem você por perto, o povo derrubou sua bandeira.",
					"lealdade-zerada", anunciar: true);
				mexeu = true;
			}
		}

		if (mexeu) SalvarConquista();
	}

	/// <summary>
	/// TIRA UM DOMINIO DO LIVRO e conta ao ex-dono por que. Funil unico das QUATRO formas de perder
	/// (planeta destruido, lealdade zerada, invasao vencida, abandono) -- do contrario o recado e a
	/// limpeza ficariam em tres dos quatro caminhos, que e o erro mais repetido deste port.
	/// </summary>
	private void PerderDominio(Dominio d, string recado, string motivo, bool anunciar)
	{
		_dominios.Remove(d);
		if (recado.Length > 0) RecadoAoDono(d.Assinatura, recado);

		if (anunciar)
			AnunciarConquista($"{NomeVisivel(d.Planeta)} não é mais domínio de {d.Nome}.");

		GD.Print($"[conquista] {d.Nome} perdeu {d.Planeta} {d.Endereco} [{motivo}]");
	}

	/// <summary>Varre o livro atras de dominio em planeta morto. E o `conq_validate_all` (:168) do boot.</summary>
	private void ValidarDominios()
	{
		foreach (Dominio d in _dominios.ToList())
			if (PlanetaDoDominioMorreu(d))
				PerderDominio(d, $"Seu domínio {NomeVisivel(d.Planeta)} foi DESTRUÍDO. O território se perdeu.",
							  "validacao-de-boot", anunciar: false);
	}

	// =====================================================================
	// RECADOS
	// =====================================================================
	/// <summary>
	/// UM RECADO PRO DONO: na hora se ele estiver online, guardado pro login se nao. E o
	/// `conq_notify_owner` (:196), e ele existe porque quase tudo o que acontece com um dominio
	/// acontece quando o dono NAO esta olhando.
	/// </summary>
	private void RecadoAoDono(string assinatura, string texto)
	{
		if (assinatura.Length == 0 || texto.Length == 0) return;
		EscutaDaConquista?.Add(texto);

		foreach (ServerPlayer p in Jogadores)
			if (string.Equals(p.Assinatura, assinatura, StringComparison.Ordinal)) { Avisar(p, texto); return; }

		if (!_recadosDeConquista.TryGetValue(assinatura, out List<string>? fila))
			_recadosDeConquista[assinatura] = fila = [];
		fila.Add(texto);
		SalvarConquista();
	}

	/// <summary>
	/// OS RECADOS GUARDADOS, entregues no login. E o `conq_login_check` (:208).
	///
	/// A conferencia do "o dominio-spawn ainda e seu?" que o DM faz aqui NAO existe: a escolha mora
	/// no proprio dominio (ver <see cref="Dominio.EhOSpawn"/>), entao ela morre com ele.
	/// </summary>
	private void EntregarRecadosDeConquista(ServerPlayer pl)
	{
		string sig = pl.Assinatura;
		if (sig.Length == 0 || !_recadosDeConquista.TryGetValue(sig, out List<string>? fila)) return;

		foreach (string m in fila) Avisar(pl, m);
		_recadosDeConquista.Remove(sig);
		SalvarConquista();
	}

	// =====================================================================
	// O RENASCIMENTO NO DOMINIO
	// =====================================================================
	/// <summary>
	/// **ONDE ESTE CORPO APARECE** -- o berco, ou o dominio quando ele escolheu um.
	///
	/// ============================ ELE ENTRA NO FUNIL, E NAO AO LADO DELE ============================
	/// O `GameServer.Berco.cs` abre dizendo que havia CINCO lugares cravando a Terra e que a regra
	/// virou um funil. Um "if de dominio" copiado nos tres chamadores (morte, verb `spawn`, login)
	/// seria desfazer isso pela metade -- e a metade que ficasse de fora e a que ninguem testa.
	///
	/// Entao o dominio nao e um caminho novo: ele monta um <see cref="Jandirus.Core.Races.Berco"/> e
	/// entrega ao MESMO <see cref="DestinoDoBerco"/>. De graca vem o filtro de planeta morto que
	/// aquele funil ganhou com a destruicao, e vem o caso dificil que o DM trata na mao (:712-715,
	/// *"superficie descarregada -> spawn padrao"*): aqui o funil poe o corpo em ORBITA e o
	/// `TickDoEspaco` pousa quando o mundo nascer.
	/// ==========================================================================================
	/// </summary>
	public (ZoneKey Zona, Vec2 Pos) DestinoDe(ServerPlayer pl)
	{
		Dominio? d = _dominios.Find(x => x.EhOSpawn
			&& string.Equals(x.Assinatura, pl.Assinatura, StringComparison.Ordinal));

		if (d == null || CorpoDoDominio(d) is not { } p) return DestinoDoBerco(pl.Berco);

		(ZoneKey zona, Vec2 pos) = DestinoDoBerco(new Jandirus.Core.Races.Berco
		{
			Planeta = p.Nome,
			Seed = p.Seed,
			PreFeito = p.Premade,
			Sx = d.Sx,
			Sy = d.Sy,
			K = d.K,
			Pos = p.Pos,
			Raio = p.Raio,
			Natal = p.Nome,
		});

		// CHEGAR NA PROPRIA BANDEIRA quando a zona esta carregada e ha celula livre perto dela. O
		// funil ja devolveu um ponto BOM; isto so o aproxima do trono. Zona nao carregada (ou destino
		// desviado por planeta morto) cai fora deste `if` e fica o do funil.
		if (zona.Hash == d.Zona.Hash && (d.Fx != 0 || d.Fy != 0)
			&& MapaDaZonaOuCatalogo(zona)?.PontoLivrePerto(new Vec2(d.Fx, d.Fy)) is { } naBandeira)
			pos = naBandeira;

		return (zona, pos);
	}

	// =====================================================================
	// A REIVINDICACAO PACIFICA
	// =====================================================================
	/// <summary>
	/// O POVO QUE TE AMA TE ENTREGA O MUNDO -- `conq_claim_peaceful` (:413).
	///
	/// So com povo, so com o planeta SEM dono e so pra quem e HEROI dele
	/// (<see cref="Reputacao.LimiarDeHeroi"/>). E a unica porta da conquista que nao derrama sangue,
	/// e ela e o motivo de a reputacao e a conquista serem o mesmo sistema visto de dois lados.
	/// </summary>
	private void ReivindicarEmPaz(ServerPlayer pl, PlanetaNoEspaco p)
	{
		Dominio d = FincarDominio(pl, p, pl.Pos);
		SomarReputacao(p.Nome, pl, Conquista.RepPorReivindicarEmPaz, "reivindicacao-pacifica");
		AnunciarConquista($"O povo de {NomeVisivel(p.Nome)} aclama {pl.Name} como protetor e soberano do planeta!");
		Avisar(pl, "colete o tributo na sua bandeira -- e apareça: domínio sem soberano por perto se perde.");
		GD.Print($"[conquista] {pl.Name} reivindicou {p.Nome} {d.Endereco} em paz");
	}

	/// <summary>
	/// **FINCA A BANDEIRA.** As duas vitorias (a pacifica e a da invasao) passam por aqui -- escrever
	/// o registro nos dois lugares deixaria um campo novo (a lealdade foi um) em so um deles.
	/// </summary>
	private Dominio FincarDominio(ServerPlayer pl, PlanetaNoEspaco p, Vec2 bandeira)
	{
		(int sx, int sy, int k) = EnderecoDoCorpo(SeedDoUniverso, p);
		var d = new Dominio
		{
			PreFeito = p.Premade,
			Planeta = p.Nome,
			Seed = p.Premade ? 0 : p.Seed,
			Sx = sx, Sy = sy, K = k,
			Assinatura = pl.Assinatura,
			Conta = pl.Conta,
			Nome = pl.Name,
			Desde = TempoDoMundo,
			Coletado = TempoDoMundo,
			Visto = TempoDoMundo,
			Fx = bandeira.X,
			Fy = bandeira.Y,
			Lealdade = Conquista.LealdadeMaxima,
		};
		_dominios.Add(d);
		SalvarConquista();
		return d;
	}

	// =====================================================================
	// OS VERBOS
	// =====================================================================
	/// <summary>
	/// O CANAL DA CONQUISTA. Prefixo proprio (`conq_`) e arquivo proprio pelo mesmo motivo do banco e
	/// da criacao de tecnicas: e um sistema inteiro com estado compartilhado (dominios, invasoes,
	/// canais de arranque), e nao meia duzia de `case` soltos no funil dos verbos.
	///
	/// AS PERGUNTAS DO DM ERAM `alert()`/`input()` MODAIS, e aqui nao ha modal: o que era caixa de
	/// dialogo virou ARGUMENTO ("forca", "sim"). Perde-se o aviso automatico antes do ato; ganha-se
	/// que nada bloqueia o tique esperando um clique -- e o aviso continua existindo, so que como
	/// resposta do verbo sem argumento.
	/// </summary>
	private bool ComandoDeConquista(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "conq_ver": ConquistaOndeEstou(pl); return true;
			case "conq_dominios": ConquistaMeusDominios(pl); return true;
			case "conq_invadir": ConquistaInvadir(pl, arg); return true;
			case "conq_tributo": ConquistaTributo(pl); return true;
			case "conq_spawn": ConquistaSpawn(pl); return true;
			case "conq_abandonar": ConquistaAbandonar(pl, arg); return true;
			case "conq_arrancar": ConquistaArrancar(pl); return true;
			default: return false;
		}
	}

	/// <summary>O dominio deste jogador NO CHAO EM QUE ELE ESTA, ou nulo.</summary>
	private Dominio? MeuDominioAqui(ServerPlayer pl) =>
		DominioDaZona(pl.Zone) is { } d
		&& string.Equals(d.Assinatura, pl.Assinatura, StringComparison.Ordinal) ? d : null;

	/// <summary>
	/// PERTO DA BANDEIRA? E o `get_dist(usr, src) > 1` do DM (:232), medido contra a posicao guardada
	/// -- ver o cabecalho deste arquivo. O alcance e o mesmo de qualquer construcao
	/// (<see cref="AlcanceDeUso"/>): duas distancias de "usar uma coisa que esta no chao" seriam duas
	/// respostas pra mesma pergunta.
	/// </summary>
	private bool PertoDaBandeira(ServerPlayer pl, Dominio d) =>
		Math.Abs(d.Fx - pl.Pos.X) <= AlcanceDeUso && Math.Abs(d.Fy - pl.Pos.Y) <= AlcanceDeUso;

	private void ConquistaOndeEstou(ServerPlayer pl)
	{
		if (CorpoDaZona(pl.Zone) is not { } p)
		{
			Avisar(pl, "aqui não é a superfície de um mundo -- não há o que dominar.");
			return;
		}

		bool povo = TemPovo(p.Nome);
		Avisar(pl, $"-- {NomeVisivel(p.Nome)} --");
		Avisar(pl, povo
			? "  habitado: há um povo, e ele defende o planeta em ondas."
			: $"  desabitado: ninguém vive aqui. A bandeira leva {Conquista.SegundosDePlantio:0}s pra "
			  + "firmar, e pode ser arrancada nesse tempo.");
		Avisar(pl, $"  poder mínimo pra invadir: {(povo ? Conquista.BpMinimoComPovo : Conquista.BpMinimoSemPovo):N0} "
				 + $"(expresso) | o seu: {pl.Ficha.expressedBP:N0}");
		Avisar(pl, $"  tributo: {Conquista.TributoPorHora(povo):N0} zeni por hora real "
				 + $"(teto de {Conquista.HorasDeTetoDoTributo:0} h de acúmulo)");

		Dominio? d = DominioDaZona(pl.Zone);
		if (d == null) Avisar(pl, "  SEM DONO.");
		else
		{
			Avisar(pl, $"  domínio de {d.Nome} | lealdade {d.Lealdade:0}/100 ({Conquista.FaixaDeLealdade(d.Lealdade)})");
			if (string.Equals(d.Assinatura, pl.Assinatura, StringComparison.Ordinal))
				Avisar(pl, $"  ...e é SEU. Tributo acumulado: {TributoAcumulado(d):N0} zeni"
						 + (d.EhOSpawn ? " | você renasce aqui." : ""));
		}

		if (InvasaoAqui(pl.Zone) is { } inv)
			Avisar(pl, $"  INVASÃO EM ANDAMENTO por {NomeDoCorpo(inv.Invasor)} -- 'Arrancar bandeira' a frustra.");

		double rep = ReputacaoDe(p.Nome, pl.Conta);
		Avisar(pl, $"  sua reputação com este povo: {rep:0} ({Reputacao.Faixa(rep)})");
	}

	private void ConquistaMeusDominios(ServerPlayer pl)
	{
		List<Dominio> meus = DominiosDe(pl.Assinatura);
		Avisar(pl, $"-- seus domínios ({meus.Count}/{Conquista.MaximoDeDominios}) --");
		if (meus.Count == 0)
		{
			Avisar(pl, "  (nenhum -- finque sua bandeira com 'Conquistar planeta')");
			return;
		}
		foreach (Dominio d in meus)
			Avisar(pl, $"  {NomeVisivel(d.Planeta)} | lealdade {d.Lealdade:0} "
					 + $"({Conquista.FaixaDeLealdade(d.Lealdade)}) | tributo {TributoAcumulado(d):N0} zeni"
					 + (d.EhOSpawn ? " | SPAWN" : ""));
	}

	private double TributoAcumulado(Dominio d) => Conquista.TributoDevido(
		Math.Max(TempoDoMundo - d.Coletado, 0) / 3600.0, TemPovo(d.Planeta), d.Lealdade);

	private void ConquistaTributo(ServerPlayer pl)
	{
		Dominio? d = MeuDominioAqui(pl);
		if (d == null) { Avisar(pl, "você não tem domínio neste planeta."); return; }
		if (!PertoDaBandeira(pl, d)) { Avisar(pl, "chegue mais perto da sua bandeira."); return; }

		double quanto = TributoAcumulado(d);
		if (quanto < 1)
		{
			Avisar(pl, $"o tributo ainda está se acumulando ({Conquista.TributoPorHora(TemPovo(d.Planeta)):N0} "
					 + $"zeni/hora real, cortado pela lealdade de {d.Lealdade:0}%).");
			return;
		}

		pl.Ficha.Zeni += quanto;
		d.Coletado = TempoDoMundo;
		d.Visto = TempoDoMundo;   // coletar e estar la: presenca conta
		SalvarConquista();
		Persistir(pl);
		MandarCatalogoDeObras(pl);   // a aba Tech mostra o zeni -- mesma razao do banco

		Avisar(pl, $"você recolhe {quanto:N0} zeni de tributo de {NomeVisivel(d.Planeta)}.");
		GD.Print($"[conquista] {pl.Name} coletou {quanto:N0} zeni de {d.Planeta}");
	}

	private void ConquistaSpawn(ServerPlayer pl)
	{
		Dominio? d = MeuDominioAqui(pl);
		if (d == null) { Avisar(pl, "você não tem domínio neste planeta."); return; }
		if (!PertoDaBandeira(pl, d)) { Avisar(pl, "chegue mais perto da sua bandeira."); return; }

		if (d.EhOSpawn)
		{
			d.EhOSpawn = false;
			Avisar(pl, $"você deixa de renascer em {NomeVisivel(d.Planeta)} -- volta a ser o seu berço.");
		}
		else
		{
			// UM SPAWN SO. O DM tambem so guarda um (`conq_spawn` e uma string), e sem esta volta dois
			// dominios marcados fariam o `DestinoDe` devolver o primeiro da LISTA -- uma escolha
			// decidida pela ordem de insercao, que e o pior tipo de regra que existe.
			foreach (Dominio o in DominiosDe(pl.Assinatura)) o.EhOSpawn = false;
			d.EhOSpawn = true;
			Avisar(pl, $"você passa a renascer em {NomeVisivel(d.Planeta)} enquanto for o soberano.");
		}
		SalvarConquista();
	}

	private void ConquistaAbandonar(ServerPlayer pl, string arg)
	{
		Dominio? d = MeuDominioAqui(pl);
		if (d == null) { Avisar(pl, "você não tem domínio neste planeta."); return; }

		if (!string.Equals(arg.Trim(), "sim", StringComparison.OrdinalIgnoreCase))
		{
			Avisar(pl, $"abandonar {NomeVisivel(d.Planeta)}? A posse se perde na hora. "
					 + "Repita com o argumento 'sim' pra confirmar.");
			return;
		}

		string nome = NomeVisivel(d.Planeta);
		PerderDominio(d, "", "abandono", anunciar: false);
		SalvarConquista();
		AnunciarConquista($"{pl.Name} abandonou o domínio de {nome}.");
	}
}
