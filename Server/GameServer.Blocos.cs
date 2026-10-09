using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>Um bloco DE PE no mundo -- parede, piso ou porta que alguem ergueu. E o que o `bases.json` guarda.</summary>
public sealed class BlocoDePe
{
	/// <summary>O id do catalogo (`BlocoDef.Id`). No disco vai o id, e nao o numero: ver `BlocoDef.Numero`.</summary>
	public string Tipo = "";

	/// <summary>A ZONA, nas tres partes da `ZoneKey` -- pelo motivo escrito em `Obra.ZonaTipo`.</summary>
	public byte ZonaTipo;
	public string ZonaNome = "";
	public ulong ZonaSeed;

	[JsonIgnore] public ZoneKey Zona => new(ZonaTipo, ZonaNome, ZonaSeed);

	/// <summary>A CELULA. Bloco e terreno, e terreno mora na grade -- nao ha pixel solto aqui.</summary>
	public int X, Y;

	/// <summary>Quem ergueu -- a CONTA (o `proprietor = usr.ckey` do DM, `buildable.dm:404`), e o nome pra avisos.</summary>
	public string DonoConta = "";
	public string DonoNome = "";

	/// <summary>
	/// O QUANTO AGUENTA: a `Resistance` do tile, que e o `intBPcap` de quem ergueu NAQUELE instante
	/// (`buildable.dm:405`, `:453`). E uma foto -- so muda quando o dono ergue de novo por cima.
	/// </summary>
	public double Resistencia;

	/// <summary>So porta: a senha (o `seccode`, `buildturfs.dm:456`). Vazia = abre pra quem encostar.</summary>
	public string Senha = "";

	/// <summary>
	/// So porta: AS CONTAS QUE JA DIGITARAM A SENHA DE AGORA. No DM quem nao quer digitar toda vez carrega
	/// uma Key com a senha dentro (`Tier 1.dm:8-13`: "Keys let you enter passworded Doors without having to
	/// enter in the password every time"); aqui a propria porta lembra de quem acertou. Trocar a senha
	/// esvazia a lista.
	/// </summary>
	public List<string> Sabem = [];

	/// <summary>So porta: ate quando fica aberta (ms). Zero = fechada. Nao vai pro disco: toda porta volta fechada.</summary>
	[JsonIgnore] public long AbertaAte;

	[JsonIgnore] public BlocoDef Def = null!;

	/// <summary>Este bloco para o corpo agora? Parede sempre; porta, so fechada.</summary>
	[JsonIgnore]
	public bool Barra =>
		Def.Classe == ClasseDeBloco.Parede || (Def.Classe == ClasseDeBloco.Porta && AbertaAte == 0);
}

/// <summary>
/// A CONSTRUCAO DE BASE -- paredes, pisos e portas que o jogador ergue celula a celula.
///
/// ============================ O PEDIDO ============================
/// O dono (2026-10-09): *"adicione construcao de base (paredes, pisos e portas) no jogo, no DM ja tinha
/// isso podendo ver como era la e melhorar pra deixar mais intuitivo e atual, sendo um estilo 'minecraft'
/// de construcao q quero. ao construir uma porta o jogo sempre pergunta se ela vai ter senha, se nao tiver
/// qualquer um q passar nela ela abre, se tiver, ela e basicamente uma parede q ao chegar perto vc pode
/// por a senha pra ela abrir. outra coisa q falta no jogo e a possibilidade de destruir portas."*
/// ==================================================================
///
/// ============================ O QUE O ORIGINAL FAZ ============================
/// `Modules/Turfs/Building/*` (a tecla M). Com a janela aberta e um tipo escolhido, clicar ou arrastar
/// num turf a ate 2 tiles troca o turf pelo tipo (`click.dm:44-48`, `:68-75`; `buildable.dm:379-461`).
/// E de graca e na hora. O tile novo guarda `proprietor = usr.ckey` e `Resistance = intBPcap`, entra na
/// lista `turfsave` (gravada a cada 30 min, `MapSave.dm:14-23`) e se muda pra area interna do planeta
/// (`buildturfs.dm:4-22`). Cai como todo turf cai: soco com `prob(34)` se `Resistance &lt;= expressedBP`
/// (`attack_proc.dm:95-112`), corpo arremessado com `pow &gt;= Resistance` (`Movement Effects.dm:62-73`),
/// e vira `Ground8` sem avisar ninguem (`NewTurfs.dm:2-17`).
///
/// A PORTA (`turf/build/Door`, `buildturfs.dm:455-524`) nasce fechada, densa e opaca. Quem encosta
/// abre, e ela fecha sozinha em 5 s (`spawn(50)`). A senha NAO e posta ao construir: o dono compra uma
/// Key e usa o verb `Set_Password_On_Door` a um tile dela (`Tier 1.dm:233-237`). Com senha, quem encosta
/// recebe um `input("What's the password?")` a cada tentativa -- o dono inclusive --, a nao ser que
/// carregue uma Key com a senha gravada.
/// =============================================================================
///
/// ============================ O QUE FICOU IGUAL ============================
///   * tres familias saidas do mesmo catalogo (`/turf/build/*`), de graca, na hora, sem requisito;
///   * posse por CONTA, e so o dono constroi por cima do que e dele;
///   * a resistencia e a foto do `intBPcap` de quem ergueu (<see cref="TetoDeTecnologia"/>);
///   * o soco derruba com 34% por golpe a partir da resistencia; o arremesso, na hora;
///   * o chao racha em volta sem tocar no que tem dono (`attack cmn.dm:48-51`: `!T.proprietor`), e as
///     duas tecnicas do Berserker que varrem tudo levam o bloco junto (`Beserker Skills.dm:57-76`,
///     `:137-140`, que nao olham dono);
///   * a porta: fechada e parede, abre por encostar, 5 s, e quem esta no chao nao abre;
///   * o bloco cobre a celula (a area interna), e por isso nao chove nem escurece dentro da base.
/// ===========================================================================
///
/// ============================ DIVERGENCIAS DECLARADAS ============================
/// As regras de LUGAR estao no cabecalho de <see cref="RegrasDeBloco"/>. As daqui:
///   * A SENHA E PERGUNTADA AO CONSTRUIR (pedido do dono). A Key do DM nao entra na conta.
///   * O DONO PASSA SEM DIGITAR, e quem acertou a senha uma vez tambem (<see cref="BlocoDePe.Sabem"/>).
///     No DM nem o dono tem passe; quem nao quer o `input()` a cada encostada carrega a Key.
///   * SENHA ERRADA SE DIZ. No DM o `input()` errado so nao abre.
///   * TODA PAREDE CEGA E BARRA QUEM VOA. No DM so o "teto" (`opacity = 1`) cega, e a "parede" comum
///     deixa passar quem voa (`buildturfs.dm:41-45`). O dono pediu as duas coisas em 2026-10-08: *"voar
///     por cima de parede de base de player n deveria ser possivel"* e *"a sombra deveria ser
///     totalmente escura vendo de fora pra dentro de uma casa/base"*. Quem faz as duas e o plano sob
///     teto (`ClasseDePredio`, `Client/Visao.cs`), que o bloco passa a alimentar.
///   * DESMANCHAR EXISTE, e devolve o chao que estava la. No DM nao ha como tirar um tile sem por
///     outro por cima, e o turf substituido nunca volta. Aqui o bloco e uma camada sobre o mapa.
///   * O BLOCO DERRUBADO NA PORRADA deixa terra batida (o `Ground8` do `Destroy()`), como la; o
///     desmanchado pelo dono nao deixa marca.
///   * O DONO E AVISADO quando derrubam o que e dele, no maximo uma vez a cada 30 s.
///   * GRAVA EM SEGUNDOS, e nao a cada 30 min: um servidor que cai nao leva meia hora de obra.
///   * UM TETO DE BLOCOS POR CONTA (<see cref="RegrasDeBloco.TetoPorConta"/>).
///   * A RESISTENCIA NAO TEM O FATOR DA ASCENSAO (ver <see cref="TetoDeTecnologia"/>).
/// =================================================================================
///
/// O QUE NAO FOI PORTADO DESTA PASTA DO DM, e fica dito: o tile de icone enviado pelo jogador
/// (`BuildWindow.dm:230-252`), a decoracao e as barreiras (`buildobjects.dm`, `barrier.dm`), e os itens
/// Wall_Upgrader, Wall_Repair, Destructor e Recreator (`buildable.dm:2-39`).
/// </summary>
public sealed partial class GameServer
{
	// =====================================================================
	// DEFEITOS INJETADOS (bancada `--blocoteste`)
	// =====================================================================
	/// <summary>
	/// DEFEITO INJETADO (bancada): o bloco entra na lista e nao entra no MAPA -- a parede aparece no
	/// retrato e nao barra nem cega no servidor. Falso em jogo, sempre.
	/// </summary>
	public static bool BlocoNaoAssentaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): a leitura do `bases.json` nao esvazia antes -- o bloco que esta de pe e nao
	/// esta no arquivo sobrevive a recarga do mundo. Falso em jogo, sempre.
	/// </summary>
	public static bool BasesLidasPorCimaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): a porta com senha abre pra qualquer um que encostar, como a sem
	/// senha. Falso em jogo, sempre.
	/// </summary>
	public static bool PortaSemTrancaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): qualquer um desmancha o bloco de qualquer um. Falso em jogo, sempre.
	/// </summary>
	public static bool QualquerUmDesmanchaDeTeste;

	/// <summary>
	/// DEFEITO INJETADO (bancada): o soco derruba o bloco pela resistencia padrao do cenario (20), e nao
	/// pela do bloco. Falso em jogo, sempre.
	/// </summary>
	public static bool BlocoCaiComoCenarioDeTeste;

	// =====================================================================
	// ESTADO
	// =====================================================================
	/// <summary>Os blocos de UMA zona: por celula, e as portas a parte (quem tica e quem procura sao elas).</summary>
	private sealed class BaseDaZona
	{
		public ZoneKey Zona;
		public readonly Dictionary<(int X, int Y), BlocoDePe> Celulas = [];
		public readonly List<BlocoDePe> Portas = [];

		/// <summary>
		/// O MAPA EM QUE AS CAMADAS DESTA ZONA JA ESTAO ESCRITAS. O mapa de um mundo sorteado e descartado
		/// quando o mundo esvazia (`PodarZonasVazias`) e refeito na proxima chegada -- com as camadas de
		/// runtime em branco. Comparar a referencia e o que diz "este mapa e novo, assente tudo de novo".
		/// </summary>
		public ZoneCollision? Assentado;
	}

	private CatalogoDeBlocos? _blocos;
	private readonly Dictionary<ulong, BaseDaZona> _bases = [];
	private readonly Dictionary<string, int> _blocosPorConta = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>Ha mudanca que ainda nao foi pro disco, e quando foi a ultima gravacao (ms).</summary>
	private bool _basesSujas;
	private long _basesGravadasEm;

	/// <summary>De quanto em quanto tempo a obra vai pro disco, com mudanca pendente. Ver <see cref="TickDasBases"/>.</summary>
	private const long GravaBasesACadaMs = 3000;

	/// <summary>Quando cada conta foi avisada pela ultima vez de que estao derrubando a base dela (ms).</summary>
	private readonly Dictionary<string, long> _avisoDeBaseEm = new(StringComparer.OrdinalIgnoreCase);
	private const long AvisoDeBaseACadaMs = 30_000;

	/// <summary>Quando cada corpo tentou uma senha ou bateu numa porta pela ultima vez (ms).</summary>
	private readonly Dictionary<int, long> _gestoNaPortaEm = [];
	private const long GestoNaPortaACadaMs = 800;

	/// <summary>
	/// Tudo que sai pelo `S2C.Blocos` enquanto uma bancada escuta: pra quem (o id) e o fio. Nulo fora da
	/// bancada -- o mesmo molde do `EscutaDeCenario`.
	/// </summary>
	internal static List<(int Para, byte[] Fio)>? EscutaDeBlocos;

	private string CaminhoDasBases => System.IO.Path.Combine(_store?.Pasta ?? ".", "bases.json");

	private static readonly JsonSerializerOptions OpcoesDeBase = new() { IncludeFields = true };

	// =====================================================================
	// CARGA E DISCO
	// =====================================================================
	private void CarregarBlocos()
	{
		const string cj = "res://Assets/Data/blocos.json";
		_blocos = CatalogoDeBlocos.Parse(Godot.FileAccess.FileExists(cj) ? Godot.FileAccess.GetFileAsString(cj) : "");
		if (_blocos.Total == 0)
			GD.PushWarning("[server] sem blocos.json -- rode o AssetPipeline (comando 'blocos'); ninguem constroi base");
		else
			GD.Print($"[server] blocos de construir: {_blocos.Total} no catalogo");

		CarregarBasesDeDisco(CaminhoDasBases);
	}

	/// <summary>
	/// LE O `bases.json`. Separada da carga do catalogo pelo caminho, como a do `mundo.json`: a bancada
	/// exercita ESTA leitura contra um arquivo proprio, sem tocar no mundo de verdade.
	///
	/// ESVAZIA ANTES DE LER, e antes ate de perguntar se o arquivo existe: quem recarrega o mundo com o
	/// servidor de pe (so a bancada do wipe faz, `RecarregarMundoDoDisco`) tem que sair daqui com o que
	/// o DISCO diz -- pasta sem `bases.json` e mundo sem base. Sem esvaziar, o bloco que esta de pe e NAO
	/// esta no arquivo continuaria de pe (o `GuardarBloco` so troca celula por celula): uma parede de um
	/// mundo dentro do outro. No boot nao ha o que esvaziar.
	/// </summary>
	private void CarregarBasesDeDisco(string caminho)
	{
		if (!BasesLidasPorCimaDeTeste) ZerarBases();
		try
		{
			if (!System.IO.File.Exists(caminho)) return;

			List<BlocoDePe>? l = JsonSerializer.Deserialize<List<BlocoDePe>>(System.IO.File.ReadAllText(caminho), OpcoesDeBase);
			int postos = 0, semTipo = 0;
			foreach (BlocoDePe b in l ?? [])
			{
				// O TIPO SAIU DO CATALOGO (o `blocos.json` foi regerado sem ele): o bloco nao tem desenho
				// nem classe, e parede sem desenho e parede invisivel. Fica de fora, e se anuncia.
				if (_blocos?.Get(b.Tipo) is not { } def) { semTipo++; continue; }
				b.Def = def;
				b.Sabem ??= [];
				GuardarBloco(b);
				postos++;
			}

			foreach (BaseDaZona z in _bases.Values) AssentarBase(z);
			if (postos > 0) GD.Print($"[server] bases: {postos} bloco(s) de pe em {_bases.Count} zona(s)");
			if (semTipo > 0)
				GD.PushWarning($"[server] bases: {semTipo} bloco(s) do disco tem um tipo que o catalogo nao conhece mais -- ficaram de fora");
		}
		catch (Exception e) { GD.PushWarning($"[server] bases.json ilegivel: {e.Message}"); }
	}

	private void GravarBases() => GravarBasesEm(CaminhoDasBases);

	private void GravarBasesEm(string caminho)
	{
		_basesSujas = false;
		_basesGravadasEm = NowMs();
		try
		{
			System.IO.File.WriteAllText(caminho,
				JsonSerializer.Serialize(_bases.Values.SelectMany(z => z.Celulas.Values).ToList(), OpcoesDeBase));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei as bases: {e.Message}"); }
	}

	/// <summary>
	/// A OBRA VAI PRO DISCO COM ATRASO DE SEGUNDOS, e nao a cada bloco: arrastar o mouse ergue uma duzia
	/// por segundo, e regravar a lista inteira a cada um seria o disco no caminho do tique. O que se
	/// arrisca perder numa queda e o que foi erguido nos ultimos tres segundos. O fechamento do servidor
	/// e o "salvar tudo" do admin gravam na hora (<see cref="GravarBasesSeMudou"/>).
	/// </summary>
	private void TickDasBases()
	{
		if (_basesSujas && NowMs() - _basesGravadasEm >= GravaBasesACadaMs) GravarBases();
		TickDasPortasErguidas();
	}

	private void GravarBasesSeMudou()
	{
		if (_basesSujas) GravarBases();
	}

	/// <summary>O `Zerar` do wipe: nenhum bloco de pe, e os mapas voltam ao que o arquivo diz.</summary>
	private void ZerarBases()
	{
		foreach (BaseDaZona z in _bases.Values)
		{
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(z.Zona);
			ZoneCollision? vista = MapaDaVista(z.Zona);
			mapa?.BaixarTudo();
			mapa?.DescobrirTudo();
			vista?.BaixarTudo();
		}
		_bases.Clear();
		_blocosPorConta.Clear();
		_avisoDeBaseEm.Clear();
		_basesSujas = false;
	}

	// =====================================================================
	// A LISTA E O MAPA
	// =====================================================================
	private BaseDaZona BaseDe(ZoneKey zona)
	{
		if (!_bases.TryGetValue(zona.Hash, out BaseDaZona? z))
			_bases[zona.Hash] = z = new BaseDaZona { Zona = zona };
		return z;
	}

	/// <summary>O bloco de pe nesta celula, se ha um.</summary>
	private BlocoDePe? BlocoEm(ZoneKey zona, int cx, int cy) =>
		_bases.TryGetValue(zona.Hash, out BaseDaZona? z) && z.Celulas.TryGetValue((cx, cy), out BlocoDePe? b) ? b : null;

	/// <summary>O bloco que PARA o corpo nesta celula agora: parede, ou porta fechada.</summary>
	private BlocoDePe? BlocoQueBarra(ZoneKey zona, int cx, int cy) =>
		BlocoEm(zona, cx, cy) is { Barra: true } b ? b : null;

	/// <summary>Poe o bloco na lista (so na lista). Quem estava na celula sai da conta do dono dele.</summary>
	private void GuardarBloco(BlocoDePe b)
	{
		BaseDaZona z = BaseDe(b.Zona);
		if (z.Celulas.TryGetValue((b.X, b.Y), out BlocoDePe? velho)) EsquecerBloco(z, velho);

		z.Celulas[(b.X, b.Y)] = b;
		if (b.Def.Classe == ClasseDeBloco.Porta) z.Portas.Add(b);
		_blocosPorConta[b.DonoConta] = _blocosPorConta.GetValueOrDefault(b.DonoConta) + 1;
	}

	/// <summary>Tira o bloco da lista (so da lista).</summary>
	private void EsquecerBloco(BaseDaZona z, BlocoDePe b)
	{
		z.Celulas.Remove((b.X, b.Y));
		z.Portas.Remove(b);
		int n = _blocosPorConta.GetValueOrDefault(b.DonoConta) - 1;
		if (n > 0) _blocosPorConta[b.DonoConta] = n;
		else _blocosPorConta.Remove(b.DonoConta);
	}

	/// <summary>
	/// AS CAMADAS DESTA ZONA, ESCRITAS NO MAPA DELA -- se ainda nao estao. Devolve o mapa (nulo se a zona
	/// nao esta carregada: mundo sorteado sem ninguem). Barato quando ja esta assentado: uma comparacao.
	///
	/// TRES CAMADAS, DOIS MAPAS: no de colisao o bloco COBRE (sob teto) e, se barra, ERGUE; no de visao
	/// o que barra tambem CEGA. Ver `ZoneCollision._erguidas` e `_cobertas`.
	/// </summary>
	private ZoneCollision? AssentarBase(BaseDaZona z)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(z.Zona);
		if (mapa == null || ReferenceEquals(mapa, z.Assentado)) return mapa;

		ZoneCollision? vista = MapaDaVista(z.Zona);
		foreach (BlocoDePe b in z.Celulas.Values) PorNoMapa(mapa, vista, b);
		z.Assentado = mapa;
		return mapa;
	}

	private static void PorNoMapa(ZoneCollision mapa, ZoneCollision? vista, BlocoDePe b)
	{
		mapa.Cobrir(b.X, b.Y);
		if (b.Barra && !BlocoNaoAssentaDeTeste)
		{
			mapa.Erguer(b.X, b.Y);
			vista?.Erguer(b.X, b.Y);
		}
		else
		{
			mapa.Baixar(b.X, b.Y);
			vista?.Baixar(b.X, b.Y);
		}
	}

	/// <summary>O bloco muda no mapa (subiu, abriu, fechou). Nao faz nada se a zona nao esta carregada.</summary>
	private void RefazerNoMapa(BaseDaZona z, BlocoDePe b)
	{
		if (AssentarBase(z) is { } mapa) PorNoMapa(mapa, MapaDaVista(z.Zona), b);
	}

	/// <summary>Tira o bloco da lista E do mapa, e marca o disco.</summary>
	private void TirarBloco(BaseDaZona z, BlocoDePe b)
	{
		EsquecerBloco(z, b);
		if (AssentarBase(z) is { } mapa)
		{
			mapa.Descobrir(b.X, b.Y);
			mapa.Baixar(b.X, b.Y);
			MapaDaVista(z.Zona)?.Baixar(b.X, b.Y);
		}
		_basesSujas = true;
	}

	// =====================================================================
	// ERGUER E DESMANCHAR
	// =====================================================================
	/// <summary>O pacote `C2S.Bloco`: erguer ou desmanchar UMA celula.</summary>
	private void ComandoDeBloco(ServerPlayer pl, byte acao, int cx, int cy, int numero, string senha)
	{
		if (acao == Protocol.BlocoDesmanchar) DesmancharBloco(pl, cx, cy);
		else ErguerBloco(pl, numero, cx, cy, senha);
	}

	/// <summary>
	/// PODE ESTE CORPO CONSTRUIR AGORA? O `!usr.KO &amp;&amp; usr.move` do `click.dm:45`, mais a zona.
	/// Devolve a frase da recusa, ou vazio.
	/// </summary>
	private string PorQueNaoConstroi(ServerPlayer pl)
	{
		if (_blocos is not { Total: > 0 }) return "este servidor está sem o catálogo de blocos.";
		if (pl.Ficha.KO || EhCadaver(pl)) return "você não constrói caído.";
		if (!RegrasDeBloco.ZonaAceita(pl.Zone)) return RegrasDeBloco.Motivo(RecusaDeBloco.Zona);
		return "";
	}

	/// <summary>
	/// ESTE BLOCO SOBE NESTA CELULA? Todas as perguntas, na ordem em que o fantasma do cliente as faz
	/// (`ModoDeConstruir.Recusa`): o terreno pelo Core, e depois o que so a lista responde.
	/// </summary>
	private RecusaDeBloco RecusaDeErguer(ServerPlayer pl, BlocoDef def, int cx, int cy)
	{
		ZoneCollision? mapa = _bases.TryGetValue(pl.Zone.Hash, out BaseDaZona? z) ? AssentarBase(z) : MapaDaZonaOuCatalogo(pl.Zone);
		bool caiu = _cenarioCaido.TryGetValue(pl.Zone.Name, out HashSet<(int X, int Y)>? caidas) && caidas.Contains((cx, cy));

		RecusaDeBloco r = RegrasDeBloco.DoTerreno(mapa, pl.Pos, cx, cy, caiu);
		if (r != RecusaDeBloco.Pode) return r;

		BlocoDePe? antigo = BlocoEm(pl.Zone, cx, cy);
		if (antigo != null && !EDonoDoBloco(antigo, pl)) return RecusaDeBloco.DeOutro;

		if (def.Classe != ClasseDeBloco.Piso)
		{
			if (ObraNaCelula(pl.Zone, cx, cy) != null || NaveNaCelula(pl.Zone, cx, cy) != null) return RecusaDeBloco.Coisa;
			foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
				if (RegrasDeBloco.CorpoNaCelula(o.Pos, cx, cy)) return RecusaDeBloco.Corpo;
		}

		if (antigo == null && _blocosPorConta.GetValueOrDefault(pl.Conta) >= RegrasDeBloco.TetoPorConta) return RecusaDeBloco.Cheio;
		return RecusaDeBloco.Pode;
	}

	private static bool EDonoDoBloco(BlocoDePe b, ServerPlayer pl) =>
		pl.Conta.Length > 0 && string.Equals(b.DonoConta, pl.Conta, StringComparison.OrdinalIgnoreCase);

	private void ErguerBloco(ServerPlayer pl, int numero, int cx, int cy, string senhaCrua)
	{
		if (PorQueNaoConstroi(pl) is { Length: > 0 } nao) { Avisar(pl, nao); return; }
		if (_blocos!.PorNumero(numero) is not { } def) { Avisar(pl, "esse bloco não existe."); return; }

		RecusaDeBloco r = RecusaDeErguer(pl, def, cx, cy);
		if (r != RecusaDeBloco.Pode) { Avisar(pl, RegrasDeBloco.Motivo(r)); return; }

		Fighter f = pl.Ficha;
		var b = new BlocoDePe
		{
			Tipo = def.Id,
			Def = def,
			ZonaTipo = pl.Zone.Kind,
			ZonaNome = pl.Zone.Name,
			ZonaSeed = pl.Zone.Seed,
			X = cx,
			Y = cy,
			DonoConta = pl.Conta,
			DonoNome = pl.Name,

			// A FOTO DO `intBPcap` (`buildable.dm:405`, `:453`), com o piso de todo turf (20).
			Resistencia = Math.Max(Empurrao.ResistenciaPadrao, TetoDeTecnologia.De(f.relBPmax, f.techskill, f.techmod)),
			Senha = def.Classe == ClasseDeBloco.Porta ? RegrasDeBloco.SenhaLimpa(senhaCrua) : "",
		};

		GuardarBloco(b);   // quem estava na celula (do proprio dono) sai aqui: construir por cima TROCA
		BaseDaZona z = BaseDe(pl.Zone);
		RefazerNoMapa(z, b);
		_basesSujas = true;
		AnunciarBloco(z, b);
	}

	/// <summary>
	/// O DONO DESMANCHA O QUE E DELE -- e o chao que estava la volta, sem marca. O admin desmancha o de
	/// qualquer um: e a ferramenta dele contra quem ergue parede pra atrapalhar os outros.
	/// </summary>
	private void DesmancharBloco(ServerPlayer pl, int cx, int cy)
	{
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "você não desmancha nada caído."); return; }
		if (!_bases.TryGetValue(pl.Zone.Hash, out BaseDaZona? z) || !z.Celulas.TryGetValue((cx, cy), out BlocoDePe? b))
		{ Avisar(pl, RegrasDeBloco.Motivo(RecusaDeBloco.Nada)); return; }

		if (!RegrasDeBloco.AoAlcance(pl.Pos, cx, cy)) { Avisar(pl, RegrasDeBloco.Motivo(RecusaDeBloco.Longe)); return; }
		if (!EDonoDoBloco(b, pl) && !EhAdmin(pl) && !QualquerUmDesmanchaDeTeste)
		{ Avisar(pl, RegrasDeBloco.Motivo(RecusaDeBloco.DeOutro)); return; }

		TirarBloco(z, b);
		AnunciarBlocoTirado(z, cx, cy, derrubado: false);
	}

	// =====================================================================
	// DERRUBAR NA PORRADA
	// =====================================================================
	/// <summary>
	/// UM SOCO NUM BLOCO QUE BARRA. `attack_proc.dm:95-112`: `if(T.Resistance &lt;= expressedBP) if(prob(34))
	/// T.Destroy()` -- a mesma chance do soco no cenario (<see cref="ChanceDoSocoDerrubar"/>), contra a
	/// resistencia DESTE bloco e nao contra o 20 do mapa.
	/// </summary>
	private void SocarBloco(ServerPlayer a, BlocoDePe b, double bp)
	{
		double limiar = BlocoCaiComoCenarioDeTeste ? Empurrao.ResistenciaPadrao : b.Resistencia;
		if (bp < limiar)
		{
			// O PUNHO NAO ALCANCA A RESISTENCIA. Sem a frase, quem soca a base de alguem dez vezes mais
			// forte acharia que o jogo travou -- a mesma razao do aviso da obra que "nem balanca".
			Avisar(a, b.Def.Classe == ClasseDeBloco.Porta
				? "a porta nem balança -- você não bate forte o bastante."
				: "a parede nem balança -- você não bate forte o bastante.");
			return;
		}
		if (_rng.NextDouble() >= ChanceDoSocoDerrubar) return;

		DerrubarBloco(b, a);
		Avisar(a, b.Def.Classe == ClasseDeBloco.Porta ? "a porta vem abaixo." : "a parede cede sob o seu punho.");
	}

	/// <summary>
	/// A FORCA QUE CHEGOU DERRUBA ESTE BLOCO? Vale pro corpo arremessado (`pow &gt;= T.Resistance`,
	/// `Movement Effects.dm:66`) e pras duas tecnicas que varrem o chao (`expressedBP &gt;= T.Resistance`,
	/// `Beserker Skills.dm:73-76`, `:137-140`). Sem sorteio: quem sorteia e so o soco.
	/// </summary>
	private bool DerrubarBlocoSeAguenta(BlocoDePe b, double forca, ServerPlayer? autor)
	{
		if (forca < b.Resistencia) return false;
		DerrubarBloco(b, autor);
		return true;
	}

	/// <summary>
	/// O BLOCO CAI. Sai da lista e do mapa, a celula vira terra batida nas duas pontas (o `Ground8` do
	/// `turf/proc/Destroy()`, `NewTurfs.dm:2-17`) e o dono fica sabendo.
	/// </summary>
	private void DerrubarBloco(BlocoDePe b, ServerPlayer? autor)
	{
		if (!_bases.TryGetValue(b.Zona.Hash, out BaseDaZona? z) || !z.Celulas.ContainsKey((b.X, b.Y))) return;

		TirarBloco(z, b);
		AnunciarBlocoTirado(z, b.X, b.Y, derrubado: true);
		ACelulaDaObraCaiu(b.Zona, b.X, b.Y);   // a poeira, a terra batida e o retrato de quem chega depois

		GD.Print($"[server] bloco {b.Tipo} de {b.DonoNome} foi ao chao em {b.Zona} ({b.X},{b.Y})"
				 + (autor != null ? $" -- {autor.Name}" : ""));

		// O DONO FICA SABENDO, e nao a cada bloco: uma parede inteira caindo seriam vinte linhas no chat.
		long agora = NowMs();
		if (autor != null && EDonoDoBloco(b, autor)) return;
		if (agora - _avisoDeBaseEm.GetValueOrDefault(b.DonoConta, -AvisoDeBaseACadaMs) < AvisoDeBaseACadaMs) return;
		if (_players.Values.FirstOrDefault(p => p.Peer != null && EDonoDoBloco(b, p)) is not { } dono) return;

		_avisoDeBaseEm[b.DonoConta] = agora;
		Avisar(dono, autor != null
			? $"{NomeVisivel(autor)} está derrubando a sua base em {b.Zona.Name}!"
			: $"estão derrubando a sua base em {b.Zona.Name}!");
	}

	// =====================================================================
	// A PORTA ERGUIDA
	// =====================================================================
	/// <summary>Esta porta abre pra este corpo, so por ele encostar?</summary>
	private static bool PortaAbrePara(BlocoDePe p, ServerPlayer pl) =>
		p.Senha.Length == 0 || PortaSemTrancaDeTeste || SabeASenha(p, pl);

	/// <summary>O dono, ou quem ja digitou a senha de agora. Corpo sem conta (NPC) nunca sabe: `if(M.client)`, `buildturfs.dm:469`.</summary>
	private static bool SabeASenha(BlocoDePe p, ServerPlayer pl) =>
		pl.Conta.Length > 0 && (EDonoDoBloco(p, pl) || p.Sabem.Contains(pl.Conta, StringComparer.OrdinalIgnoreCase));

	/// <summary>
	/// ABRE PRA QUEM ENCOSTA E FECHA O QUE VENCEU -- a mesma regra da porta do mapa (`TickDasPortas`), com
	/// a tranca. Custa o numero de portas das zonas COM gente: uma base tem meia duzia.
	///
	/// A PORTA NAO FECHA EM CIMA DE NINGUEM: com um corpo no vao o prazo e empurrado. No DM ela fecha
	/// (`Close()` nao olha quem esta la, `buildturfs.dm:519-524`) e o mob fica dentro do turf denso;
	/// aqui o corpo ficaria preso numa parede que so o dono sabe abrir.
	/// </summary>
	private void TickDasPortasErguidas()
	{
		long agora = NowMs();
		foreach (BaseDaZona z in _bases.Values)
		{
			if (z.Portas.Count == 0) continue;
			_zones.TryGetValue(z.Zona.Hash, out List<ServerPlayer>? gente);

			foreach (BlocoDePe p in z.Portas)
			{
				if (p.AbertaAte == 0 || agora < p.AbertaAte) continue;
				if (gente != null && gente.Any(o => RegrasDeBloco.CorpoNaCelula(o.Pos, p.X, p.Y))) { p.AbertaAte = agora + 400; continue; }
				p.AbertaAte = 0;
				RefazerNoMapa(z, p);
				AnunciarPortaErguida(z, p);
			}

			if (gente == null) continue;
			foreach (ServerPlayer pl in gente)
			{
				// As tres recusas da porta do mapa, pelos mesmos motivos: caido nao abre (`if(M.KB) return`),
				// cadaver nao abre, e so abre quem esta ANDANDO contra ela (o `Enter()` e uma tentativa de passo).
				if (pl.Ficha.KO || EhCadaver(pl) || !pl.Moving) continue;
				foreach (BlocoDePe p in z.Portas)
					if (PortasDaZona.VaiEntrar(pl.Pos, pl.Facing, p.X, p.Y) && PortaAbrePara(p, pl))
						AbrirPortaErguida(z, p, agora);
			}
		}
	}

	/// <summary>Abre (ou renova o prazo de) uma porta erguida.</summary>
	private void AbrirPortaErguida(BaseDaZona z, BlocoDePe p, long agora)
	{
		bool jaEstava = p.AbertaAte != 0;
		p.AbertaAte = agora + PortaFechaEmMs;
		if (jaEstava) return;
		RefazerNoMapa(z, p);
		AnunciarPortaErguida(z, p);
	}

	/// <summary>A porta erguida mais perto deste corpo que <paramref name="serve"/>, ao alcance da tecla E.</summary>
	private (BaseDaZona Zona, BlocoDePe Porta)? PortaErguidaPerto(ServerPlayer pl, Func<BlocoDePe, bool> serve)
	{
		if (!_bases.TryGetValue(pl.Zone.Hash, out BaseDaZona? z)) return null;

		const float T = ZoneCollision.TileSize;
		BlocoDePe? melhor = null;
		float melhorDist = float.MaxValue;
		foreach (BlocoDePe p in z.Portas)
		{
			// O CORTE E POR EIXO, contra o centro da celula -- a mesma conta do menu do cliente
			// (`MenuDeInteracao.PortaPerto`), pra os dois nao discordarem de "perto".
			float dx = p.X * T + T / 2f - pl.Pos.X, dy = p.Y * T + T / 2f - pl.Pos.Y;
			if (Math.Abs(dx) > Interacoes.Alcance || Math.Abs(dy) > Interacoes.Alcance || !serve(p)) continue;
			float d = dx * dx + dy * dy;
			if (d >= melhorDist) continue;
			melhorDist = d;
			melhor = p;
		}
		return melhor != null ? (z, melhor) : null;
	}

	/// <summary>Os tres verbos da tecla E numa porta erguida. Ver `Interacoes.DaPorta`.</summary>
	private bool ComandoDePortaErguida(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "porta_senha": DigitarSenha(pl, arg); return true;
			case "porta_bater": BaterNaPorta(pl); return true;
			case "porta_trocar": TrocarSenha(pl, arg); return true;
			default: return false;
		}
	}

	private bool GestoNaPortaCedoDemais(ServerPlayer pl)
	{
		long agora = NowMs();
		if (agora - _gestoNaPortaEm.GetValueOrDefault(pl.Id, -GestoNaPortaACadaMs) < GestoNaPortaACadaMs) return true;
		_gestoNaPortaEm[pl.Id] = agora;
		return false;
	}

	/// <summary>
	/// "What's the password?" (`buildturfs.dm:479-482`). Certa: a porta abre e LEMBRA de quem acertou.
	/// Errada: nao abre, e diz -- e so depois de um intervalo aceita outra tentativa.
	/// </summary>
	private void DigitarSenha(ServerPlayer pl, string digitada)
	{
		if (PortaErguidaPerto(pl, p => p.Senha.Length > 0 && !SabeASenha(p, pl)) is not { } alvo)
		{ Avisar(pl, "não há porta trancada por perto."); return; }
		if (pl.Ficha.KO || EhCadaver(pl)) { Avisar(pl, "você não alcança a porta caído."); return; }
		if (GestoNaPortaCedoDemais(pl)) { Avisar(pl, "calma: uma tentativa de cada vez."); return; }

		if (!string.Equals(RegrasDeBloco.SenhaLimpa(digitada), alvo.Porta.Senha, StringComparison.Ordinal))
		{ Avisar(pl, "senha errada."); return; }

		alvo.Porta.Sabem.Add(pl.Conta);
		_basesSujas = true;
		AbrirPortaErguida(alvo.Zona, alvo.Porta, NowMs());
		MandarBlocoPara(pl, Protocol.BlocosPorta, alvo.Porta);   // o "sei a senha" e so dele
		Avisar(pl, "a porta abre. Ela lembra de você: da próxima vez é só encostar.");
	}

	/// <summary>
	/// "**[usr] knocks on the door!**" pra quem esta a ate 15 tiles (`buildturfs.dm:503-506`). No DM e o
	/// clique na porta trancada de quem nao tem a chave.
	/// </summary>
	private void BaterNaPorta(ServerPlayer pl)
	{
		if (PortaErguidaPerto(pl, p => p.Senha.Length > 0) is not { } alvo) { Avisar(pl, "não há porta trancada por perto."); return; }
		if (GestoNaPortaCedoDemais(pl)) return;

		const float T = ZoneCollision.TileSize;
		var porta = new Vec2(alvo.Porta.X * T + T / 2f, alvo.Porta.Y * T + T / 2f);
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash))
			if (o.Peer != null && Math.Max(Math.Abs(o.Pos.X - porta.X), Math.Abs(o.Pos.Y - porta.Y)) <= 15 * T)
				Avisar(o, $"** {NomeVisivel(pl)} bate na porta! **");
	}

	/// <summary>O dono troca a senha (vazia = tira). Quem sabia a antiga deixa de saber.</summary>
	private void TrocarSenha(ServerPlayer pl, string nova)
	{
		if (PortaErguidaPerto(pl, p => EDonoDoBloco(p, pl)) is not { } alvo) { Avisar(pl, "não há porta sua por perto."); return; }

		alvo.Porta.Senha = RegrasDeBloco.SenhaLimpa(nova);
		alvo.Porta.Sabem.Clear();
		_basesSujas = true;
		AnunciarPortaErguida(alvo.Zona, alvo.Porta);
		Avisar(pl, alvo.Porta.Senha.Length > 0
			? "senha trocada. Quem sabia a antiga vai ter de digitar a nova."
			: "a porta ficou sem senha: abre pra qualquer um que encostar.");
	}

	// =====================================================================
	// O FIO
	// =====================================================================
	private static byte MarcasPara(BlocoDePe b, ServerPlayer pl)
	{
		byte m = 0;
		bool meu = EDonoDoBloco(b, pl);
		if (meu) m |= Protocol.BlocoMeu;
		if (b.Senha.Length > 0) m |= Protocol.BlocoTrancado;
		if (b.AbertaAte != 0) m |= Protocol.BlocoAberto;
		if (meu || SabeASenha(b, pl)) m |= Protocol.BlocoSeiASenha;
		return m;
	}

	private static void DespacharBloco(ServerPlayer para, NetDataWriter w)
	{
		EscutaDeBlocos?.Add((para.Id, w.CopyData()));
		para.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O RETRATO DA ZONA pra quem chega nela: a zona e todos os blocos, num pacote so -- o molde do
	/// retrato de cenario (`MandarCenario`), e pelo mesmo motivo: o cliente guarda a lista com a zona
	/// dentro e so a aplica quando o chao DAQUELA zona estiver montado. Sai sempre, mesmo vazio.
	/// </summary>
	private void MandarBlocos(ServerPlayer pl)
	{
		if (pl.Peer == null && EscutaDeBlocos == null) return;

		_bases.TryGetValue(pl.Zone.Hash, out BaseDaZona? z);
		if (z != null) AssentarBase(z);   // o mundo sorteado que acabou de renascer ganha as camadas de volta

		NetDataWriter w = Protocol.Begin(Protocol.S2C.Blocos);
		w.Put(Protocol.BlocosRetrato);
		w.Put(pl.Zone.Hash);
		w.Put(z?.Celulas.Count ?? 0);
		if (z != null)
			foreach (BlocoDePe b in z.Celulas.Values)
			{
				w.Put((ushort)b.X);
				w.Put((ushort)b.Y);
				w.Put(b.Def.Numero);
				w.Put(MarcasPara(b, pl));
			}
		DespacharBloco(pl, w);
	}

	/// <summary>Um bloco (posto, ou porta que mudou) pra UM jogador: as marcas sao de quem recebe.</summary>
	private void MandarBlocoPara(ServerPlayer para, byte modo, BlocoDePe b)
	{
		if (para.Peer == null && EscutaDeBlocos == null) return;
		NetDataWriter w = Protocol.Begin(Protocol.S2C.Blocos);
		w.Put(modo);
		w.Put((ushort)b.X);
		w.Put((ushort)b.Y);
		w.Put(b.Def.Numero);
		w.Put(MarcasPara(b, para));
		DespacharBloco(para, w);
	}

	private void AnunciarBloco(BaseDaZona z, BlocoDePe b)
	{
		if (!_zones.TryGetValue(z.Zona.Hash, out List<ServerPlayer>? gente)) return;
		foreach (ServerPlayer o in gente) MandarBlocoPara(o, Protocol.BlocosPosto, b);
	}

	private void AnunciarPortaErguida(BaseDaZona z, BlocoDePe p)
	{
		if (!_zones.TryGetValue(z.Zona.Hash, out List<ServerPlayer>? gente)) return;
		foreach (ServerPlayer o in gente) MandarBlocoPara(o, Protocol.BlocosPorta, p);
	}

	private void AnunciarBlocoTirado(BaseDaZona z, int cx, int cy, bool derrubado)
	{
		if (!_zones.TryGetValue(z.Zona.Hash, out List<ServerPlayer>? gente)) return;
		foreach (ServerPlayer o in gente)
		{
			if (o.Peer == null && EscutaDeBlocos == null) continue;
			NetDataWriter w = Protocol.Begin(Protocol.S2C.Blocos);
			w.Put(Protocol.BlocosTirado);
			w.Put((ushort)cx);
			w.Put((ushort)cy);
			w.Put(derrubado);
			DespacharBloco(o, w);
		}
	}

	// =====================================================================
	// SUPERFICIE DE BANCADA (`--diagconstruir`: o robo do cliente, no modo host)
	// =====================================================================
	/// <summary>O bloco de pe numa celula, como o servidor o tem: tipo, conta do dono, se tem senha e se esta aberto.</summary>
	internal (string Tipo, string Dono, bool Trancado, bool Aberto)? BlocoDeTeste(ZoneKey zona, int cx, int cy) =>
		BlocoEm(zona, cx, cy) is { } b ? (b.Tipo, b.DonoConta, b.Senha.Length > 0, b.AbertaAte != 0) : null;

	/// <summary>Quantos blocos de pe ha numa zona.</summary>
	internal int BlocosDeTeste(ZoneKey zona) => _bases.TryGetValue(zona.Hash, out BaseDaZona? z) ? z.Celulas.Count : 0;

	/// <summary>Nenhum bloco de pe, e o retrato vazio pra quem esta conectado: o robo comeca (e termina) de chao limpo.</summary>
	internal void ZerarBasesDeTeste()
	{
		ZerarBases();
		_basesSujas = true;
		foreach (ServerPlayer p in _players.Values.ToList()) MandarBlocos(p);
	}

	/// <summary>Esta celula barra no mapa de colisao do servidor?</summary>
	internal bool BarraDeTeste(ZoneKey zona, int cx, int cy) => MapaDaZonaOuCatalogo(zona)?.BlockedCell(cx, cy) == true;

	/// <summary>A primeira porta de mapa da zona (na Terra, a do Banco).</summary>
	internal (int X, int Y)? PortaDoMapaDeTeste(ZoneKey zona) =>
		_portasDoMapa.TryGetValue(zona.Name, out List<PortaDoMapa>? l) && l.Count > 0 ? (l[0].X, l[0].Y) : null;

	// Derrubar uma celula e refazer o cenario ja tem superficie de bancada: `DerrubarCelulaDeTeste` e
	// `ConsertarCenarioDeTeste`, em `GameServer.TetoTeste.cs`. O robo `--diagconstruir` usa as mesmas.
}
