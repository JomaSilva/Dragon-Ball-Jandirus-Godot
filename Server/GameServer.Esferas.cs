using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Jandirus.Core.Magic;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>
/// UM SET DE ESFERAS -- a `obj/DragonStatue` do DM com as sete penduradas nela (`Dragonballs.dm:27`).
///
/// ============================ A ESTATUA E O SET, E NAO UM MOVEL ============================
/// No DM tudo o que importa (quantos desejos, que poder, que planeta, quando volta, quem criou) mora
/// na ESTATUA, e cada esfera COPIA esses campos a cada tique (`Tick`, :295-301) porque no BYOND nao
/// havia outro jeito de um objeto perguntar ao outro. Aqui a copia nao existe: as esferas apontam pro
/// set pelo <see cref="Id"/> e leem dele. O que o DM chama de "re-sincronizar" (o bloco :191-205 do
/// `D_Wish`) e simplesmente ler este objeto.
///
/// O `WishCount` E DO SET, e nao de cada esfera. **Este e o furo mais grave do original** e ele foi
/// medido na Fase 0: com o contador por ESFERA, sete jogadores clicando as sete esferas diferentes
/// tiram `Wishs x 7` desejos de uma ativacao -- vinte e um no Porunga de Namek. Aqui ha um contador,
/// no set, e uma trava, no set.
/// ========================================================================================
/// </summary>
public sealed class SetDeEsferas
{
	/// <summary>`BallID`, 1..999 nos sets de jogador e 0 no eterno. Ver <see cref="Esferas.SetDeJogador"/>.</summary>
	public int Id;

	/// <summary>Este e o Porunga de Namek -- o `obj/DragonStatue/Eternal` (:376).</summary>
	public bool Eterno;

	/// <summary>
	/// ONDE O SET VIVE -- as TRES partes da <see cref="ZoneKey"/>, e nao o nome.
	///
	/// Mesma escolha (e mesmo motivo) da <see cref="Obra.ZonaTipo"/>: o nome nao e endereco, dois
	/// mundos gerados homonimos existem, e uma estatua erguida num sorteado voltaria do disco numa
	/// zona pre-feita que nao existe. Ver o cabecalho de la -- este repo ja pagou por isso uma vez.
	/// </summary>
	public byte ZonaTipo;
	public string ZonaNome = "";
	public ulong ZonaSeed;

	[JsonIgnore] public ZoneKey Zona => new(ZonaTipo, ZonaNome, ZonaSeed);

	public void PorZona(ZoneKey z) { ZonaTipo = z.Kind; ZonaNome = z.Name; ZonaSeed = z.Seed; }

	/// <summary>Onde a estatua esta fincada, em pixels da zona.</summary>
	public float Ex, Ey;

	/// <summary>
	/// QUEM ERGUEU -- assinatura (o personagem), conta (a pessoa) e o nome que a tela le.
	///
	/// A ASSINATURA E A QUE MANDA, e e a mesma disciplina do <see cref="Dominio.Assinatura"/>: o set e
	/// de um PERSONAGEM. O DM guarda `CreatorSig` na estatua exatamente por isso (:164-169), e e a
	/// metade do bloqueio do criador que sobrevive ao relog -- a outra metade dele e um `mob` `tmp`
	/// que morre no reboot.
	/// </summary>
	public string CriadorSig = "", CriadorConta = "", CriadorNome = "";

	/// <summary>`WishPower` -- o BP do criador NA HORA (namekian.dm:97). Nunca se atualiza sozinho.</summary>
	public double Poder;

	/// <summary>`Wishs`, 1..3. Estatua de jogador nasce com 1; o eterno tem 3 (define).</summary>
	public int Desejos = 1;

	/// <summary>`OffTime` em `Year` do DM. Ver <see cref="Esferas.SegundosDe"/>.</summary>
	public double OffTime = Esferas.OffTimeDeJogador;

	/// <summary>Quando as sete voltam a valer, em segundos do <see cref="GameServer.TempoDoMundo"/>.</summary>
	public double AtivoEm;

	/// <summary>
	/// **QUANTAS VEZES ESTE SET JA SE ESPALHOU.** E o unico estado que a posicao precisa.
	///
	/// Ver <see cref="Esferas.CelulaDoEspalhamento"/>: a celula de cada esfera e funcao pura de
	/// (seed da zona, set, numero, ciclo). Espalhar de novo e `Ciclo + 1` -- nao ha sorteio em runtime
	/// e nao ha posicao inventada em lugar nenhum.
	/// </summary>
	public int Ciclo;

	/// <summary>`WishCount` -- quantos desejos ja saíram desta ativacao. Do SET, ver o cabecalho.</summary>
	public int Pedidos;

	/// <summary>`CompletelyInert` (:172): o set morreu. So o criador vivo o levanta (`Revive_Set`).</summary>
	public bool Inerte;

	/// <summary>
	/// **O DESEJO SUPREMO FOI GRAVADO NESTAS ESFERAS** -- `HasStrongestWish` (`Dragonballs.dm:44`).
	///
	/// Comprado na CRIACAO da estatua por `SW_WISH_PRICE` = 2.000.000 zeni (`namekian.dm:101-109`), e
	/// so entao "Strongest in the Universe" entra no menu (`WishTable.dm:147-148`). E o unico desejo do
	/// Shenron que **nao** sai da escada de poder: ele sai do bolso de quem ergueu.
	///
	/// PERSISTE NA ESTATUA e nao na esfera, como no DM: as sete se refazem a cada ciclo e levariam o
	/// campo pro lixo junto.
	/// </summary>
	public bool TemSupremo;

	/// <summary>`dragonname` e `title` (:45-47). O eterno nasce com os dele (:386-387).</summary>
	public string NomeDoDragao = "SHENRON", TituloDoDragao = "O DRAGÃO ETERNO";

	/// <summary>
	/// O SIMBOLO da folha do dragao -- "shenron" ou "porunga". **Simbolo, e nao `res://`**.
	///
	/// Regra da casa §0.1: quem traduz simbolo em caminho e o cliente (`EsferaDesenhada.FolhaDe`).
	/// O `MandarObras` manda `res://` porque o caminho vem do JSON do catalogo, que e DADO; aqui nao
	/// ha catalogo, e cravar `res://` no servidor seria o servidor conhecendo o Godot por dentro.
	/// </summary>
	public string Dragao = "shenron";

	/// <summary>A folha da ESFERA: "comum" (`Dragonballs.dmi`) ou "namek" (`dragonball.dmi`).</summary>
	public string Folha = "comum";

	/// <summary>Pro log: "#12 em Earth".</summary>
	[JsonIgnore] public string Etiqueta => $"#{Id} em {ZonaNome}";
}

/// <summary>UMA DAS SETE. O `obj/DB` (`Dragonballs.dm:152`).</summary>
public sealed class Esfera
{
	/// <summary>De que set ela e -- o `BallID`. E a unica ligacao; o resto ela le do set.</summary>
	public int Set;

	/// <summary>1..7. E o `icon_state` quando ativa, e e o leque de pixels quando varias empilham.</summary>
	public int Numero;

	/// <summary>Em que zona ela esta AGORA. Pode nao ser a do set: quem a carregou pra fora a tirou dali.</summary>
	public byte ZonaTipo;
	public string ZonaNome = "";
	public ulong ZonaSeed;

	[JsonIgnore] public ZoneKey Zona => new(ZonaTipo, ZonaNome, ZonaSeed);

	public void PorZona(ZoneKey z) { ZonaTipo = z.Kind; ZonaNome = z.Name; ZonaSeed = z.Seed; }

	public float X, Y;

	/// <summary>
	/// QUEM A CARREGA -- o `container` do DM (:167). Zero = no chao.
	///
	/// E o ID de sessao, e por isso ele **nao vai pro disco**: id se reusa, e uma esfera que voltasse
	/// do save "carregada pelo jogador 7" seria dada a quem entrasse com aquele numero. No DM o mesmo
	/// caso e resolvido no `Logout` (que espalha, :217) e no filtro do save (que exige `x>=1`, ou seja
	/// **esfera em inventario nunca era salva**) -- as duas metades dizendo "esfera sempre volta pro
	/// chao". Aqui uma linha diz isso: o portador nao persiste.
	/// </summary>
	[JsonIgnore] public int Portador;

	/// <summary>
	/// ELA CAIU NUM LUGAR SEM MAPA e precisa ser assentada quando houver um.
	///
	/// Um planeta gerado so tem colisao enquanto alguem esta nele. O espalhamento e funcao pura e
	/// acontece de qualquer jeito (senao um set num mundo vazio nao teria posicao nenhuma); o encaixe
	/// numa celula LIVRE precisa do mapa. Este bit e o bilhete: o tique reassenta quando a zona nascer.
	///
	/// ============================ ELE **VAI PRO DISCO**, E A PRIMEIRA VERSAO NAO IA ============================
	/// Ele nasceu `[JsonIgnore]`, e a carga ligava o bilhete em TODAS as esferas "porque o mapa pode ter
	/// mudado de forma". Duas coisas erradas numa linha:
	///
	///   * **o mapa nao muda de forma** -- ele e funcao pura da seed (regra 0.2). A justificativa era
	///     falsa;
	///   * e o efeito era grave: no primeiro tique depois de um reinicio, TODA esfera voltava pra
	///     posicao da semente. Quem tivesse juntado cinco num monte antes de deslogar as encontraria
	///     espalhadas de novo -- e o X/Y no arquivo, que existe justamente pra guardar onde a MAO do
	///     jogador as deixou, nao valeria nada.
	///
	/// Persistido, ele diz o que sempre quis dizer: *"esta aqui NAO foi encaixada ainda"*. Quem foi
	/// encaixada fica onde esta, e quem nasceu num mundo vazio e encaixada no dia em que alguem pisar la.
	/// ========================================================================================================
	/// </summary>
	public bool PrecisaAssentar;
}

/// <summary>
/// **AS ESFERAS DO DRAGAO EM JOGO** -- o set, o espalhamento, o radar, a invocacao e o dragao.
///
/// Porte de `Code/Modules/Magic/Dragonballs.dm`. A TABELA DE DESEJOS e a Fase 2 e mora noutro lugar:
/// aqui termina em <see cref="AbrirOsDesejos"/>, que e o ponto de plugue e esta documentado como tal.
///
/// ============================ TRES FUROS DO ORIGINAL FORAM FECHADOS, E ESTAO NOMEADOS ============================
///   1. **`Create_Dragon_Statue` nao chamava `RecreateBalls()`** (namekian.dm:75-109). Uma estatua
///      recem-erguida ficava SEM esferas ate o criador usar `Redo()`. Isso e um buraco, e nao um
///      desenho: o proprio `Redo` cria as sete. Aqui erguer a estatua cria as sete.
///   2. **A trava de invocacao era por ESFERA** (`obj/var/tmp/summoned`, :2), entao sete pessoas
///      clicando as sete esferas abriam sete listas de desejos da mesma ativacao. Aqui a trava e do
///      SET, que e o que o DM sempre quis dizer.
///   3. **`Ballplanet` nulo fazia `Scatter()` a cada 10 s pra sempre** (:323-329, `"Earth" != null`).
///      Aqui a policia compara ZONA com ZONA e um set sem zona nao existe -- ele nao chega a nascer.
///
/// O que NAO foi "consertado" de proposito: `Destroy_Statue` e `Destroy_Ball` continuam abertos a
/// QUALQUER UM (o proprio `desc` da estatua promete isso, :30), e o dragao continua matavel por quem
/// tiver `expressedBP >= WishPower`. Sao decisoes do original, e ruidosas -- mudar era design novo.
/// ============================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>Os sets vivos. Lista e nao dicionario: sao poucos, e a busca e por id E por zona.</summary>
	private readonly List<SetDeEsferas> _sets = [];

	/// <summary>As esferas soltas no mundo -- sete por set.</summary>
	private readonly List<Esfera> _esferas = [];

	/// <summary>
	/// O DRAGAO DE PE, por set: quem invocou e ate quando ele fica.
	///
	/// Dicionario e nao campo do set: ele e transiente (morre no reboot, como o `DragonObject` do DM
	/// que nem `SaveItem` tinha) e gravar no set poria estado de sessao dentro do arquivo do mundo.
	/// </summary>
	private readonly Dictionary<int, Invocacao> _invocacoes = [];

	/// <summary>Uma invocacao em pe -- o `DragonObject` (:5) mais o `usr.summoning` (:1).</summary>
	public sealed class Invocacao
	{
		public int Set;
		public int Invocador;
		public Vec2 Onde;

		/// <summary>Segundos do <see cref="TempoDoMundo"/> em que o dragao vai embora sozinho.</summary>
		public double ExpiraEm;
	}

	/// <summary>
	/// QUANTO TEMPO O DRAGAO ESPERA por um pedido antes de ir embora.
	///
	/// **NAO EXISTE NO DM**, e nao podia existir: la o `D_Wish` e um proc que BLOQUEIA dentro de um
	/// `input()` modal, entao o dragao vive exatamente enquanto a caixa de dialogo estiver aberta.
	/// Aqui nao ha modal (mesma escolha que a conquista ja fez: *"o que era caixa de dialogo virou
	/// ARGUMENTO"*), e sem um prazo um dragao invocado por quem fechou o jogo ficaria de pe pra
	/// sempre, trancando o set.
	/// </summary>
	private const double SegundosDoDragaoDePe = 120;

	/// <summary>O que as esferas disseram -- ligada so pela bancada, como a `EscutaDaConquista`.</summary>
	internal static List<string>? EscutaDasEsferas;

	private string CaminhoDasEsferas =>
		System.IO.Path.Combine(_store?.Pasta ?? ".", "esferas.json");

	// =====================================================================
	// DISCO
	// =====================================================================
	private sealed class LivroDasEsferas
	{
		public List<SetDeEsferas> Sets = [];
		public List<Esfera> Esferas = [];
	}

	private void CarregarEsferas()
	{
		try
		{
			if (System.IO.File.Exists(CaminhoDasEsferas))
			{
				LivroDasEsferas? l = JsonSerializer.Deserialize<LivroDasEsferas>(
					System.IO.File.ReadAllText(CaminhoDasEsferas),
					new JsonSerializerOptions { IncludeFields = true });

				if (l != null) { _sets.AddRange(l.Sets); _esferas.AddRange(l.Esferas); }
			}
		}
		catch (Exception e) { GD.PushWarning($"[server] esferas.json ilegivel: {e.Message}"); }

		// ============================ SET ORFAO SOME, ALTO ============================
		// Mesma disciplina do `CarregarConquista`: um set num mundo gerado que nao existe nesta seed
		// de universo nao pode virar "estatua de um planeta que nao existe". E a esfera sem set e a
		// que o DM DELETA no `Tick` quando `HomeStatue` e nulo (:293-294) -- aqui ela some na carga,
		// que e dez segundos mais cedo e uma varredura a menos.
		// ==========================================================================
		// O ID TAMBEM E CONFERIDO, e nao so a zona: `Esferas.SetDeJogador` diz o que e um `BallID`
		// valido (1..999), e o zero e reservado ao eterno. Um save com id fora da faixa faria dois sets
		// disputarem a mesma faixa de ids de tela (`set*10 + n`, ver `MandarEsferas`) -- e o cliente
		// desenharia um por cima do outro, calado.
		int setsOrfaos = _sets.RemoveAll(
			s => s.ZonaNome.Length == 0 || (!s.Eterno && !Esferas.SetDeJogador(s.Id)));
		int esferasOrfas = _esferas.RemoveAll(e => !_sets.Any(s => s.Id == e.Set));
		if (setsOrfaos > 0 || esferasOrfas > 0)
			GD.PushWarning($"[server] esferas: {setsOrfaos} set(s) e {esferasOrfas} esfera(s) sem dona -- descartados");

		GD.Print($"[server] esferas: {_sets.Count} set(s), {_esferas.Count} esfera(s)"
			   + (_sets.Count > 0 ? " -- " + string.Join(", ", _sets.Select(s => s.Etiqueta)) : ""));

		// A DIVIDA SAI NO BOOT, como a da conquista e a da reputacao. Ver o porque la.
		GD.Print($"[server] esferas: faltam {Esferas.OqueFalta.Length} -- "
			   + string.Join("; ", Esferas.OqueFalta));

		ErguerOSetEterno();
		SalvarEsferas();
	}

	private void SalvarEsferas()
	{
		if (_store == null) return;
		try
		{
			var l = new LivroDasEsferas { Sets = _sets, Esferas = _esferas };
			string tmp = CaminhoDasEsferas + ".tmp";
			System.IO.File.WriteAllText(tmp, JsonSerializer.Serialize(l,
				new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
			System.IO.File.Move(tmp, CaminhoDasEsferas, overwrite: true);
		}
		catch (Exception e) { GD.PushWarning($"[server] nao deu pra salvar esferas.json: {e.Message}"); }
	}

	// =====================================================================
	// O SET ETERNO -- `Build_Eternal_Dragonballs()` (:432-453)
	// =====================================================================
	/// <summary>
	/// AS ESFERAS DE NAMEK, que existem SEM jogador nenhum.
	///
	/// ============================ POR QUE ELAS PRECISAM EXISTIR ============================
	/// O cabecalho do bloco no DM (:360-368) explica melhor do que eu explicaria: *"nunca existia um
	/// set no mundo (esferas so nasciam se um deus criasse uma DragonStatue) -- por isso 'elas nunca
	/// aconteciam'"*. Sem este set, o sistema inteiro depende de um Namekuseijin do Cla do Dragao
	/// existir, ter marcos pra gastar e dominar um planeta.
	/// =================================================================================
	///
	/// NAMEK E PRE-FEITA NESTE PORT, entao a checagem do DM (varrer areas atras de `A.Planet ==
	/// "Namek"`, e remarcar pra 600 s se nao houver) vira uma pergunta ao catalogo: a zona existe? Se
	/// nao existir -- um manifesto de mapas mutilado --, o set nao nasce e o log diz por que. **Nunca
	/// no planeta errado**, que era a regra inteira daquele bloco.
	/// </summary>
	private void ErguerOSetEterno()
	{
		if (_sets.Any(s => s.Eterno)) { ManterOSetEterno(); return; }

		var zona = ZoneKey.Premade(Esferas.PlanetaEterno);
		if (_catalogo?.Get(zona) == null)
		{
			GD.PushWarning($"[server] esferas: {Esferas.PlanetaEterno} nao esta no manifesto de mapas -- "
						 + "o set eterno NAO nasce (a lore nao admite ele fora de Namek)");
			return;
		}

		Vec2 onde = PontoDeNascimento(zona);
		var s = new SetDeEsferas
		{
			Id = Esferas.IdDoSetEterno,
			Eterno = true,
			Ex = onde.X,
			Ey = onde.Y,
			Poder = Esferas.PoderDoEterno,
			Desejos = Esferas.DesejosDoEterno,
			OffTime = Esferas.OffTimeEterno,
			NomeDoDragao = "PORUNGA",
			TituloDoDragao = "O DRAGÃO DOS SONHOS",
			Dragao = "porunga",
			Folha = "namek",
		};
		s.PorZona(zona);
		_sets.Add(s);

		RefazerAsEsferas(s);
		AnunciarNoMundo("Lendas correm o universo: as Esferas do Dragão repousam em algum lugar de Namek...");
		GD.Print($"[server] esferas: set ETERNO erguido em {zona} @ ({onde.X:0},{onde.Y:0})");
	}

	/// <summary>
	/// O ZELADOR DO SET ETERNO -- `eternal_maintain()` (:400-419).
	///
	/// Cura save antigo (poder e numero de desejos de volta aos defines), levanta o set que alguem
	/// inertou, recria as sete se alguma sumiu, e corta a espera no teto de um mes. E o que faz o
	/// Porunga de Namek **nunca morrer de vez** -- o unico set do jogo com essa garantia.
	/// </summary>
	private void ManterOSetEterno()
	{
		foreach (SetDeEsferas s in _sets.Where(x => x.Eterno))
		{
			s.Inerte = false;
			s.Poder = Esferas.PoderDoEterno;
			s.Desejos = Esferas.DesejosDoEterno;
			s.OffTime = Esferas.OffTimeEterno;

			double teto = TempoDoMundo + Esferas.SegundosDe(Esferas.TetoDeEsperaEterna);
			if (s.AtivoEm > teto) s.AtivoEm = teto;

			if (_esferas.Count(e => e.Set == s.Id) < Esferas.Total) RefazerAsEsferas(s);
		}
	}

	// =====================================================================
	// NASCER E ESPALHAR
	// =====================================================================
	/// <summary>
	/// `RecreateBalls()` (:103-129) -- apaga as que existirem e cria as sete de novo, ja espalhadas.
	///
	/// A ESPERA DE NASCIMENTO E LITERAL (`ActiveYear = Year + 0.4`, :108): as sete nascem INERTES por
	/// quatro meses in-game. E o que impede erguer a estatua e pedir no minuto seguinte -- e e a
	/// unica coisa que separa "criar um set" de "ter um desejo".
	/// </summary>
	private void RefazerAsEsferas(SetDeEsferas s)
	{
		_esferas.RemoveAll(e => e.Set == s.Id);

		if (TempoDoMundo >= s.AtivoEm)
			s.AtivoEm = TempoDoMundo + Esferas.SegundosDe(Esferas.EsperaDeNascimento);
		s.Pedidos = 0;

		for (int n = 1; n <= Esferas.Total; n++)
		{
			var e = new Esfera { Set = s.Id, Numero = n };
			e.PorZona(s.Zona);
			_esferas.Add(e);
		}

		EspalharOSet(s);
	}

	/// <summary>
	/// **`Scatter()`** (:245-278) -- as sete voam e caem em pontos novos do planeta do set.
	///
	/// ============================ O CICLO ANDA UMA VEZ, PRO SET INTEIRO ============================
	/// Se cada esfera avancasse o proprio ciclo, duas espalhadas seguidas dariam a mesma posicao pra
	/// esferas diferentes em ciclos diferentes -- e a sequencia deixaria de ser reproduzivel a partir
	/// de um numero so. Um ciclo por SET e o que faz `(seed, set, numero, ciclo)` ser um endereco.
	/// ==========================================================================================
	///
	/// O QUE NAO FOI PORTADO, e esta escrito porque e visivel: o VFX do DM (`:248-265`) cria um
	/// `/obj/attack/blast` DE VERDADE, com `Pow=20` e `BP=500000`, e o solta com `walk_rand`. E um
	/// projetil que machuca quem estiver no caminho -- quase certamente sem intencao. Aqui o risco
	/// dourado e coisa do cliente (ver `EsferaDesenhada`), e nao um tiro.
	/// </summary>
	private void EspalharOSet(SetDeEsferas s)
	{
		s.Ciclo++;
		ZoneKey zona = s.Zona;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);

		foreach (Esfera e in _esferas.Where(x => x.Set == s.Id))
		{
			e.Portador = 0;
			e.PorZona(zona);
			Assentar(e, s, mapa);
		}

		SalvarEsferas();
		MandarEsferas(zona);
	}

	/// <summary>
	/// PoE UMA ESFERA NO PONTO QUE A SEMENTE MANDA, encaixada numa celula livre.
	///
	/// As duas metades sao funcoes puras -- a celula sai de <see cref="Esferas.CelulaDoEspalhamento"/>
	/// e o encaixe sai do `PontoLivrePerto` de um mapa que e funcao pura da seed (§0.2). Sem mapa
	/// (mundo gerado que ninguem visita) a esfera fica no centro da celula crua e marca
	/// <see cref="Esfera.PrecisaAssentar"/>: o tique a reencaixa quando a zona nascer.
	/// </summary>
	private static void Assentar(Esfera e, SetDeEsferas s, ZoneCollision? mapa)
	{
		int w = mapa?.Width ?? 256, h = mapa?.Height ?? 256;
		(int cx, int cy) = Esferas.CelulaDoEspalhamento(
			s.Zona.Seed ^ Espaco.Hash64(s.ZonaNome), s.Id, e.Numero, s.Ciclo, w, h);

		const int t = ZoneCollision.TileSize;
		var cru = new Vec2(cx * t + t / 2f, cy * t + t / 2f);

		if (mapa == null) { e.X = cru.X; e.Y = cru.Y; e.PrecisaAssentar = true; return; }

		Vec2 livre = mapa.PontoLivrePerto(cru);
		e.X = livre.X;
		e.Y = livre.Y;
		e.PrecisaAssentar = false;
	}

	// =====================================================================
	// CONSULTA
	// =====================================================================
	public SetDeEsferas? SetPorId(int id) => _sets.Find(s => s.Id == id);

	/// <summary>O set esta VALENDO agora? E a soma das duas mortes: a espera e a inercia.</summary>
	public bool SetAtivo(SetDeEsferas s) => !s.Inerte && TempoDoMundo >= s.AtivoEm;

	/// <summary>As esferas de uma zona que estao NO CHAO (as carregadas viajam com o corpo).</summary>
	private List<Esfera> EsferasNoChaoDe(ZoneKey z) =>
		[.. _esferas.Where(e => e.Portador == 0 && e.Zona.Hash == z.Hash)];

	/// <summary>Quantas esferas deste set alguem carrega. E o que a aba e o radar mostram.</summary>
	public int QuantasCarrega(int jogador) => _esferas.Count(e => e.Portador == jogador);

	// =====================================================================
	// O TIQUE -- a policia de planeta, a reativacao e a queda
	// =====================================================================
	/// <summary>
	/// UMA VEZ POR SEGUNDO. Quatro coisas, todas baratas -- a lista tem sete itens por set.
	///
	/// O DM roda isto num `spawn(100)` por ESFERA, ou seja sete corrotinas por set. Aqui e um laco no
	/// tique de segundo, junto da conquista e das sagas, pela mesma razao que o cabecalho da invasao
	/// registra: *"nao ha rotina pra morrer, so um dicionario que o tique varre"*.
	/// </summary>
	private void TickDasEsferas()
	{
		double agora = TempoDoMundo;
		bool mexeu = false;
		var zonasPraAvisar = new HashSet<ulong>();

		ManterOSetEterno();

		// ---------------------------------------------------------- 0. a divida do "Mais Forte do Universo"
		// ============================ ELA PRECISA DE UM RELOGIO QUE ANDA SOZINHO ============================
		// O `Aging.dm:114-122` mora no `AgeCheck`, que o DM chama do login e do virar do ANO -- e o virar
		// do ano la acontece sozinho, com o servidor de pe. Este port **nao tem calendario** (o
		// `ConferirMorteDeVelhice` documenta isso: aqui a idade so anda por gesto), entao pendurar a
		// cobranca so no login deixaria de fora justamente quem fica online -- e quem pede este desejo
		// vai ficar online: ele acabou de virar o ser mais forte do universo.
		//
		// Aqui, no tique de SEGUNDO que ja existe pras esferas, e uma comparacao por jogador. E o preco
		// do desejo e o unico prazo deste sistema que nao pode depender de alguem deslogar.
		// ================================================================================================
		// ============================ E O RECORTE E `Pessoas`, E ISSO FOI MEDIDO ============================
		// A primeira versao varria `Jogadores` (dono na tela), e a bancada a pegou: o desejo alcanca
		// **`EhPessoa`** (ver o cabecalho de `Pessoas`), entao a divida pode nascer num corpo que
		// `Jogadores` nao enxerga -- e ali ela nunca seria cobrada. Quem concede e quem cobra tem que
		// varrer a MESMA lista, senao o preco do desejo depende de quem o recebeu.
		//
		// O BONECO DO CORPO LARGADO FICA DE FORA (`DonoDoCorpoLargado != 0`) porque ele **compartilha a
		// Ficha do dono**: cobrar nele mataria a copia e nao a pessoa -- e a pessoa esta na lista.
		// ================================================================================================
		foreach (ServerPlayer p in Pessoas.Where(p => p.DonoDoCorpoLargado == 0).ToList())
			if (p.Ficha.sw_doom_year > 0) ConferirDividaDoSupremo(p);

		// ---------------------------------------------------------- 1. o set volta a valer
		foreach (SetDeEsferas s in _sets)
		{
			// `WishCount = 0` NA REATIVACAO (:286). O comentario do DM explica que sem isso o SEGUNDO
			// uso do mesmo set falhava pra sempre -- o contador nunca voltava a zero.
			//
			// O `Pedidos != 0` NAO E OTIMIZACAO: e ele que faz isto acontecer UMA vez, no instante da
			// virada. Sem ele, este laco marcaria "mexeu" e reenviaria o pacote da zona a cada segundo
			// pra todo set acordado do mundo, pra sempre.
			if (s.Inerte || agora < s.AtivoEm || s.Pedidos == 0) continue;

			s.Pedidos = 0;
			mexeu = true;
			zonasPraAvisar.Add(s.Zona.Hash);
		}

		// ---------------------------------------------------------- 2. o criador morreu (:290-292)
		foreach (SetDeEsferas s in _sets)
		{
			if (s.Eterno || s.Inerte || s.CriadorSig.Length == 0) continue;

			ServerPlayer? criador = Jogadores.FirstOrDefault(
				p => string.Equals(p.Assinatura, s.CriadorSig, StringComparison.Ordinal));

			// ============================ SO PEGA O CRIADOR ONLINE, E ISSO E DO ORIGINAL ============================
			// No DM o `Creator` e uma var `tmp` (nao vai pro save) e a condicao e
			// `!isnull(Creator) && Creator.dead` -- ou seja, depois de um reboot ela e FALSA ate o
			// `RefreshCreator()` reencontrar o mob. Um criador que morre offline nao inerta nada.
			//
			// Copiar a letra aqui e de proposito: a alternativa (guardar "ele esta morto" no disco)
			// mataria sets de gente que morreu uma vez ha tres meses e nunca mais voltou, e o unico
			// jeito de levantar e o proprio criador vivo. Seria mais severo que o original.
			// ==================================================================================================
			if (criador?.Ficha.dead != true) continue;

			s.Inerte = true;
			mexeu = true;
			zonasPraAvisar.Add(s.Zona.Hash);
			GD.Print($"[esferas] set {s.Etiqueta} ficou inerte: o criador {s.CriadorNome} morreu");
		}

		// ---------------------------------------------------------- 3. quem carrega ainda pode?
		foreach (Esfera e in _esferas)
		{
			if (e.Portador == 0) continue;

			// ============================ UM FUNIL PRAS TRES QUEDAS ============================
			// O DM tem tres lugares diferentes: `KO.dm:75-76` (nocaute derruba), `Login.dm:217` (o
			// logout ESPALHA) e `Admin.dm:187/200` (reboot joga no chao). Sao tres regras que dizem a
			// mesma coisa -- *"esfera nao fica com quem nao esta de pe"* -- e este projeto ja pagou
			// caro por regra ligada em um chamador e esquecida no outro.
			//
			// Aqui a pergunta e uma so, e ela cobre inclusive o caso que o DM NAO cobre: trocar de
			// zona carregando as sete (o corpo entra numa nave, na Sala do Tempo, no Outro Mundo).
			// ==============================================================================
			ServerPlayer? quem = _players.GetValueOrDefault(e.Portador);
			bool podeCarregar = quem is { Peer: not null } && !quem.Ficha.dead && !quem.Ficha.KO;

			if (podeCarregar && quem!.Zone.Hash == e.Zona.Hash) continue;

			// A ESFERA CAI ONDE O CORPO ESTAVA -- nocaute, morte, logout ou troca de zona, o mesmo
			// gesto. Se ele levou a esfera pra outro mundo, a policia (4) a devolve no segundo
			// seguinte; e o `container` do DM perdendo o dono e o `OnRelease` pondo no chao.
			//
			// SEM CORPO NENHUM (logout) ela fica exatamente onde estava: nao ha "onde ele estava" pra
			// perguntar, e inventar a origem da zona a poria dentro de uma parede.
			e.Portador = 0;
			if (quem != null) { e.X = quem.Pos.X; e.Y = quem.Pos.Y; e.PorZona(quem.Zone); }
			mexeu = true;
			zonasPraAvisar.Add(e.Zona.Hash);
		}

		// ---------------------------------------------------------- 4. a policia de planeta (:323-329)
		foreach (SetDeEsferas s in _sets)
		{
			ZoneKey zona = s.Zona;
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);

			foreach (Esfera e in _esferas.Where(x => x.Set == s.Id))
			{
				if (e.Portador != 0) continue;   // carregada: o DM pula a checagem (`if(!container)`)

				if (e.Zona.Hash != zona.Hash)
				{
					// FORA DO PLANETA DELA: volta. **Nao da pra levar uma esfera pra outro mundo** --
					// e a regra que faz reunir as sete ser uma viagem, e nao um saque.
					e.PorZona(zona);
					Assentar(e, s, mapa);
					mexeu = true;
					zonasPraAvisar.Add(zona.Hash);
					continue;
				}

				// O BILHETE DO ASSENTAMENTO: a zona nasceu, agora ha mapa. Ver `Esfera.PrecisaAssentar`.
				if (e.PrecisaAssentar && mapa != null)
				{
					Assentar(e, s, mapa);
					zonasPraAvisar.Add(zona.Hash);
				}
			}
		}

		// ---------------------------------------------------------- 5. o dragao vai embora sozinho
		foreach (Invocacao inv in _invocacoes.Values.ToList())
		{
			if (agora < inv.ExpiraEm) continue;
			FecharAInvocacao(inv.Set, "O dragão desaparece nas nuvens sem que ninguém tenha pedido nada.");
		}

		foreach (ulong h in zonasPraAvisar) MandarEsferasPorHash(h);
		if (mexeu) SalvarEsferas();
	}

	// =====================================================================
	// PEGAR E LARGAR -- `Get()` / `Drop()` (:344-355)
	// =====================================================================
	private Esfera? EsferaPerto(ServerPlayer pl) => _esferas
		.Where(e => e.Portador == 0 && e.Zona.Hash == pl.Zone.Hash
					&& Math.Abs(e.X - pl.Pos.X) <= Esferas.AlcanceDePegar
					&& Math.Abs(e.Y - pl.Pos.Y) <= Esferas.AlcanceDePegar)
		.OrderBy(e => (e.X - pl.Pos.X) * (e.X - pl.Pos.X) + (e.Y - pl.Pos.Y) * (e.Y - pl.Pos.Y))
		.FirstOrDefault();

	private void PegarEsfera(ServerPlayer pl)
	{
		// `if(usr.KO) return` (:346). Quem esta no chao nao cata nada -- e a mesma linha que faz o
		// nocaute derrubar as que ele ja tinha (ver o funil do tique).
		if (pl.Ficha.KO || pl.Ficha.dead) { Avisar(pl, "você não consegue nem se levantar."); return; }

		if (EsferaPerto(pl) is not { } e) { Avisar(pl, "não há nenhuma Esfera do Dragão ao seu alcance."); return; }

		e.Portador = pl.Id;
		SalvarEsferas();
		MandarEsferas(pl.Zone);

		SetDeEsferas? s = SetPorId(e.Set);
		int quantas = QuantasCarrega(pl.Id);
		Avisar(pl, $"você pega a {Esferas.NomeDaEsfera(e.Numero)} ({quantas}/{Esferas.Total} com você)"
				 + (s != null && !SetAtivo(s) ? " -- ela está apagada, o set ainda se refaz." : "."));
	}

	private void LargarEsferas(ServerPlayer pl)
	{
		List<Esfera> minhas = [.. _esferas.Where(e => e.Portador == pl.Id)];
		if (minhas.Count == 0) { Avisar(pl, "você não está carregando nenhuma esfera."); return; }

		foreach (Esfera e in minhas)
		{
			e.Portador = 0;
			e.X = pl.Pos.X;
			e.Y = pl.Pos.Y;
			e.PorZona(pl.Zone);
		}
		SalvarEsferas();
		MandarEsferas(pl.Zone);
		Avisar(pl, $"você põe {minhas.Count} esfera(s) no chão.");
	}

	// =====================================================================
	// A INVOCACAO -- `D_Wish` (:183-243)
	// =====================================================================
	/// <summary>
	/// AS SETE ESTAO JUNTAS? Conta as do MESMO set no alcance -- carregadas por quem invoca ou no
	/// chao ao redor.
	///
	/// ============================ A LETRA DO DM TEM UM DEFEITO, E AQUI ELE NAO ============================
	/// O laco do original (:187-190) e
	/// `for(var/obj/DB/A in view(1,usr)) if(A.BallID == BallID && !IsInactive) ballnum+=1` -- e o
	/// `!IsInactive` e do **SRC** (a esfera clicada), nao do `A` iterado. Efeito real: basta a esfera
	/// CLICADA estar ativa, e as outras seis sao contadas mesmo apagadas.
	///
	/// Na pratica as sete recarregam juntas, entao o defeito nunca aparecia. Copia-lo aqui seria
	/// copiar um acidente: o teste e do SET (<see cref="SetAtivo"/>), que e o que o DM quis dizer nas
	/// outras cinco linhas em que ele fala do assunto.
	/// ================================================================================================
	/// </summary>
	private List<Esfera> SeteJuntas(ServerPlayer pl, SetDeEsferas s) =>
		[.. _esferas.Where(e => e.Set == s.Id
			&& (e.Portador == pl.Id
				|| (e.Portador == 0 && e.Zona.Hash == pl.Zone.Hash
					&& Math.Abs(e.X - pl.Pos.X) <= Esferas.AlcanceDaInvocacao
					&& Math.Abs(e.Y - pl.Pos.Y) <= Esferas.AlcanceDaInvocacao)))];

	private void InvocarODragao(ServerPlayer pl)
	{
		if (pl.Ficha.dead || pl.Ficha.KO) { Avisar(pl, "você não está em condições de invocar nada."); return; }

		// QUAL SET: o das esferas que estao aqui. Perguntar ao jogador seria pedir que ele soubesse o
		// numero de um set -- que e um detalhe interno, e nao uma coisa que o mundo mostra.
		SetDeEsferas? s = null;
		List<Esfera> sete = [];
		foreach (SetDeEsferas cand in _sets)
		{
			List<Esfera> juntas = SeteJuntas(pl, cand);
			if (juntas.Count <= sete.Count) continue;
			s = cand;
			sete = juntas;
		}

		if (s == null || sete.Count == 0) { Avisar(pl, "não há Esferas do Dragão aqui."); return; }

		if (sete.Count < Esferas.Total)
		{
			Avisar(pl, $"só {sete.Count} das {Esferas.Total} esferas estão reunidas. "
					 + "Elas precisam estar com você ou no chão ao seu redor.");
			return;
		}

		if (!SetAtivo(s))
		{
			// As duas mortes tem mensagens diferentes de proposito: uma passa, a outra pede alguem.
			Avisar(pl, s.Inerte
				? "as esferas estão INERTES -- só quem as criou pode despertá-las de novo."
				: $"as esferas ainda se refazem. Falta {(s.AtivoEm - TempoDoMundo) / 3600.0:0.#} h real(is).");
			return;
		}

		if (_invocacoes.ContainsKey(s.Id))
		{ Avisar(pl, $"{s.NomeDoDragao} já está de pé -- alguém está diante dele agora."); return; }

		if (s.Pedidos >= s.Desejos)
		{ Avisar(pl, "este set já deu tudo o que tinha nesta ativação."); return; }

		// O DRAGAO NASCE UM TILE AO NORTE de quem invocou -- `locate(src.x, src.y+1, src.z)` (:220),
		// com o Y invertido (no BYOND ele cresce pra cima).
		var onde = new Vec2(pl.Pos.X, pl.Pos.Y - ZoneCollision.TileSize);
		_invocacoes[s.Id] = new Invocacao
		{
			Set = s.Id,
			Invocador = pl.Id,
			Onde = onde,
			ExpiraEm = TempoDoMundo + SegundosDoDragaoDePe,
		};

		string separador = s.TituloDoDragao.Length > 0 ? ", " : "!";
		AnunciarEsferas($"{s.NomeDoDragao} diz: 'EU SOU {s.NomeDoDragao}{separador}{s.TituloDoDragao}! "
					  + "DIGA O SEU DESEJO!'");

		MandarEsferas(pl.Zone);
		AbrirOsDesejos(pl, s);
	}

	// ============================ O PONTO DE PLUGUE DA FASE 2, JA LIGADO ============================
	// `AbrirOsDesejos` e `PedirDesejo` moram em `GameServer.Desejos.cs`. O contrato que a Fase 1
	// escreveu aqui foi cumprido sem reabrir este arquivo: a entrada continua `(pl, s)`, o gate de
	// poder sai de `s`, quem concede chama `ContarUmDesejo` e quem desiste chama `FecharAInvocacao`.
	//
	// A LINGUA DOS DEUSES CONTINUA FORA DESTE CAMINHO, e o grep continua limpo: `godtongue` nao
	// aparece uma unica vez em `Dragonballs.dm`. Ela e portao EXCLUSIVO do Super Shenron.
	// ==========================================================================================

	/// <summary>
	/// **UM DESEJO FOI CONCEDIDO.** Funil unico -- a Fase 2 chama isto e mais nada.
	///
	/// Ele faz o fim do `D_Wish` (:233-239): conta, e quando o set se esgota poe as sete inertes com a
	/// espera proporcional (<see cref="Esferas.EsperaDepoisDoDesejo"/>) e as espalha em posicoes NOVAS.
	/// </summary>
	private void ContarUmDesejo(SetDeEsferas s)
	{
		s.Pedidos++;
		if (s.Pedidos < s.Desejos) { SalvarEsferas(); return; }

		s.AtivoEm = TempoDoMundo
				  + Esferas.SegundosDe(Esferas.EsperaDepoisDoDesejo(s.OffTime, s.Pedidos, s.Desejos));

		FecharAInvocacao(s.Id, $"{s.NomeDoDragao} some no céu, e as esferas se espalham pelo mundo.");
		EspalharOSet(s);
	}

	/// <summary>Tira o dragao de pe. Funil unico das tres saidas: prazo, desejo gasto e cancelamento.</summary>
	private void FecharAInvocacao(int set, string frase)
	{
		if (!_invocacoes.Remove(set, out Invocacao? inv)) return;
		if (frase.Length > 0) AnunciarEsferas(frase);

		SetDeEsferas? s = SetPorId(set);
		if (s != null) MandarEsferas(s.Zona);
		else if (_players.TryGetValue(inv.Invocador, out ServerPlayer? quem)) MandarEsferas(quem.Zone);
	}

	// =====================================================================
	// O RADAR -- `verb/Locate()` (Tier 1.5.dm:234-243)
	// =====================================================================
	/// <summary>
	/// ONDE ESTAO AS ESFERAS -- so pra quem tem o Dragon Radar na mochila.
	///
	/// ============================ AS DUAS REGRAS DO ORIGINAL QUE FICARAM ============================
	///   * **so no MESMO lugar** (`O.z == usr.z`, :237): o radar nao atravessa planeta. E o que
	///     obriga a viajar em vez de consultar;
	///   * **so as ATIVAS** (`if(!nD.IsInactive)`, :241): esfera recarregando nao aparece. E o que faz
	///     o intervalo entre invocacoes ser uma espera de verdade e nao uma contagem.
	///
	/// O QUE NAO FICOU: o DM imprime o resultado pra `view(src)` -- **todo mundo por perto le as
	/// coordenadas**. Isso e quase certamente um `to_chat` no alvo errado (o proprio verb e pessoal), e
	/// copia-lo transformaria o radar num alto-falante que entrega o achado de quem o comprou.
	/// ==========================================================================================
	/// </summary>
	private void UsarORadar(ServerPlayer pl)
	{
		if (pl.Mochila.Quantos("Dragon_Radar") <= 0)
		{ Avisar(pl, "você não tem um Dragon Radar."); return; }

		var achadas = new List<(Esfera E, SetDeEsferas S, double D)>();
		foreach (Esfera e in _esferas)
		{
			if (e.Zona.Hash != pl.Zone.Hash) continue;
			if (SetPorId(e.Set) is not { } s || !SetAtivo(s)) continue;

			double dx = e.X - pl.Pos.X, dy = e.Y - pl.Pos.Y;
			achadas.Add((e, s, Math.Sqrt(dx * dx + dy * dy)));
		}

		if (achadas.Count == 0)
		{
			Avisar(pl, "o radar varre o horizonte e não devolve nada. Nenhuma Esfera do Dragão "
					 + "ACORDADA neste mundo.");
			return;
		}

		Avisar(pl, $"-- radar: {achadas.Count} sinal(is) em {NomeDoPlaneta(pl.Zone.Name)} --");
		foreach ((Esfera e, SetDeEsferas s, double d) in achadas.OrderBy(a => a.D))
			Avisar(pl, $"  {Esferas.NomeDaEsfera(e.Numero)}: {d / ZoneCollision.TileSize:0} tiles "
					 + $"a {Rumo(pl.Pos, new Vec2(e.X, e.Y))}"
					 + (e.Portador != 0 ? $" -- com {NomeDoCorpo(e.Portador)}" : "")
					 + (s.Eterno ? " [Namek]" : ""));
	}

	/// <summary>O rumo em palavras. Um numero de coordenada nao ajuda ninguem a andar.</summary>
	private static string Rumo(Vec2 de, Vec2 pra)
	{
		float dx = pra.X - de.X, dy = pra.Y - de.Y;
		string v = dy < -16 ? "norte" : dy > 16 ? "sul" : "";
		string h = dx > 16 ? "leste" : dx < -16 ? "oeste" : "";
		return (v + h).Length == 0 ? "bem aqui" : v + h;
	}

	// =====================================================================
	// ERGUER, REFAZER E DERRUBAR A ESTATUA
	// =====================================================================
	/// <summary>
	/// **`Create_Dragon_Statue`** (namekian.dm:75-109) -- os dois portoes, e o segundo reusa a conquista.
	///
	/// ============================ O SEGUNDO PORTAO NAO E NOVO: E O `conq_owner_sig` ============================
	/// O DM pergunta `conq_owner_sig(pl) == signature` (PlanetConquest.dm:78) pra qualquer planeta que
	/// nao seja a Terra, e `Earth_Guardian == signature` pra ela. As duas perguntas ja existem neste
	/// port -- <see cref="DominioDaZona"/> e o livro de tronos --, e a instrucao da tarefa foi
	/// explicita: **reuse, nao invente um segundo eixo**.
	///
	/// O que muda em relacao ao DM e que a skill `MakeDragonballs` (`/datum/skill/rank/...`) ainda nao
	/// existe neste port -- o censo de skills a marca como esperando pelo `SistEsferas`. Entao o portao
	/// e RACA + CLASSE + territorio, que sao as tres perguntas do verb; a skill entra quando a arvore
	/// racial Namek for fechada, e o `effector()` dela gateia pela MESMA `DominaAlgum`.
	/// ======================================================================================================
	/// </summary>
	private void ErguerEstatua(ServerPlayer pl, string arg)
	{
		bool namek = string.Equals(pl.Race, "Namekian", StringComparison.OrdinalIgnoreCase);
		bool cla = string.Equals(pl.Class, "Dragon clan", StringComparison.OrdinalIgnoreCase);

		if (!namek || !cla)
		{
			Avisar(pl, "apenas Namekuseijins do Clã do Dragão carregam o segredo das Esferas.");
			return;
		}

		if (CorpoDaZona(pl.Zone) is not { } corpo)
		{ Avisar(pl, "não há um mundo aqui para ancorar um set de esferas."); return; }

		if (string.Equals(corpo.Nome, "Earth", StringComparison.OrdinalIgnoreCase))
		{
			// O GUARDIAO DA TERRA. O livro de tronos e por CONTA (ver `GameServer.Ranks.cs`), e por
			// isso a comparacao e por conta -- o DM compara `signature` porque la o cargo e por
			// personagem. Uma comparacao "assinatura contra conta" nunca casaria, e o Guardiao
			// legitimo levaria a recusa: e exatamente o defeito que a Fase 0 achou no set eterno do
			// DM (`blockid = Earth_Guardian` comparado contra `usr.key`).
			if (!_tronos.TryGetValue("guardian", out string? dono)
				|| !string.Equals(dono, pl.Conta, StringComparison.OrdinalIgnoreCase))
			{
				Avisar(pl, "somente o Guardião da Terra pode erguer uma Estátua do Dragão na Terra.");
				return;
			}
		}
		else if (DominioDaZona(pl.Zone) is not { } d
				 || !string.Equals(d.Assinatura, pl.Assinatura, StringComparison.Ordinal))
		{
			Avisar(pl, "você só pode erguer uma Estátua do Dragão num planeta que dominou (bandeira de "
					 + "conquista) -- ou na Terra, se for o Guardião.");
			return;
		}

		if (_sets.Any(s => s.Zona.Hash == pl.Zone.Hash))
		{ Avisar(pl, $"já existe uma Estátua do Dragão em {NomeDoPlaneta(corpo.Nome)}."); return; }

		int id = ProximoIdDeSet();
		if (id < 0) { Avisar(pl, "não há mais lugar para um set de esferas neste mundo."); return; }

		bool emNamek = string.Equals(corpo.Nome, Esferas.PlanetaEterno, StringComparison.OrdinalIgnoreCase);
		var s = new SetDeEsferas
		{
			Id = id,
			Ex = pl.Pos.X,
			Ey = pl.Pos.Y,
			CriadorSig = pl.Assinatura,
			CriadorConta = pl.Conta,
			CriadorNome = pl.Name,

			// `A.WishPower = BP` (namekian.dm:97) -- o BP do criador NA HORA. Ele nunca se atualiza
			// sozinho, e e de proposito: o set carrega o poder de quem o fez, e nao o de quem o usa.
			Poder = pl.Ficha.BP,
			Desejos = 1,
			OffTime = Esferas.OffTimeDeJogador,

			// O ramo "Make it Default" do `Redo()` (:79-88) escolhe pelo PLANETA: em Namek, Porunga;
			// fora, Shenron. Aqui isso e o padrao de nascimento em vez de uma escolha de menu.
			NomeDoDragao = emNamek ? "PORUNGA" : "SHENRON",
			TituloDoDragao = emNamek ? "O DRAGÃO DOS SONHOS" : "O DRAGÃO ETERNO",
			Dragao = emNamek ? "porunga" : "shenron",
			Folha = emNamek ? "namek" : "comum",
		};
		s.PorZona(pl.Zone);
		_sets.Add(s);

		// ============================ O DESEJO SUPREMO, COMPRADO NA HORA DE ERGUER ============================
		// `namekian.dm:101-109`: se o criador tiver `SW_WISH_PRICE` (2.000.000 zeni), um alerta oferece
		// GRAVAR o "Strongest in the Universe" nestas esferas. E a UNICA porta desse desejo no Shenron
		// comum -- ele nao sai da escada de poder, sai do bolso.
		//
		// O ALERTA VIROU ARGUMENTO (`db_estatua supremo`), como o resto do sistema. E ele e explicito de
		// proposito: dois milhoes de zeni cobrados por um alerta que o jogador nao pediu seriam um
		// pedagio, e o DM ao menos perguntava.
		// ================================================================================================
		if (string.Equals(arg.Trim(), "supremo", StringComparison.OrdinalIgnoreCase))
		{
			if (pl.Ficha.Zeni >= Desejos.PrecoDoSupremo)
			{
				pl.Ficha.Zeni -= Desejos.PrecoDoSupremo;
				s.TemSupremo = true;
				Persistir(pl);
				Avisar(pl, $"você grava o desejo O MAIS FORTE DO UNIVERSO nestas esferas por "
						 + $"{Desejos.PrecoDoSupremo:N0} zeni.");
			}
			else Avisar(pl, $"gravar o desejo supremo custa {Desejos.PrecoDoSupremo:N0} zeni -- "
						  + $"você tem {pl.Ficha.Zeni:N0}. A estátua sobe sem ele.");
		}
		else if (pl.Ficha.Zeni >= Desejos.PrecoDoSupremo)
			Avisar(pl, $"você tem zeni pra gravar O MAIS FORTE DO UNIVERSO nestas esferas "
					 + $"({Desejos.PrecoDoSupremo:N0}). Derrube a estátua e erga de novo com o "
					 + "argumento 'supremo' se quiser.");

		// ============================ **E AQUI ELE CRIA AS SETE** ============================
		// O DM **nao faz isto** (namekian.dm:95-100 monta a estatua e para), e o resultado e que uma
		// estatua recem-erguida fica sem esferas ate o criador descobrir sozinho o verb `Redo`. E um
		// buraco, e nao uma decisao: o proprio `Redo` chama `RecreateBalls()` na ultima linha.
		// ================================================================================
		RefazerAsEsferas(s);

		AnunciarNoMundo($"{pl.Name} ergue uma Estátua do Dragão em {NomeDoPlaneta(corpo.Nome)}. "
					  + "Sete esferas se espalham pelo mundo.");
		Avisar(pl, $"as sete esferas nascem APAGADAS e acordam em "
				 + $"{Esferas.SegundosDe(Esferas.EsperaDeNascimento) / 3600.0:0.#} h reais.");
		GD.Print($"[esferas] {pl.Name} ergueu o set {s.Etiqueta}");
	}

	/// <summary>
	/// O PROXIMO `BallID` LIVRE. O DM sorteia e re-sorteia ate nao colidir (:142-151); aqui o
	/// primeiro livre serve igual e nao tem laco de rejeicao.
	/// </summary>
	private int ProximoIdDeSet()
	{
		for (int i = 1; i <= 999; i++)
			if (!_sets.Any(s => s.Id == i)) return i;
		return -1;
	}

	/// <summary>
	/// `Redo()` (:59-89), a parte que sobrevive sem menu modal: quantos desejos, e o poder relido.
	///
	/// O DM abre seis `input()` seguidos (desejos, nome, titulo, dois icones, um alerta). Aqui o canal
	/// de verbos tem um argumento, e a escolha e a mesma da conquista: **o que era caixa de dialogo
	/// virou ARGUMENTO**. O upload de icone custom nao tem porta neste port -- esta na
	/// <see cref="Esferas.OqueFalta"/> em vez de fingido.
	/// </summary>
	private void RefazerOSet(ServerPlayer pl, string arg)
	{
		SetDeEsferas? s = _sets.Find(x => x.Zona.Hash == pl.Zone.Hash);
		if (s == null) { Avisar(pl, "não há Estátua do Dragão neste mundo."); return; }
		if (s.Eterno) { Avisar(pl, "a estátua ancestral não responde a você."); return; }
		if (!string.Equals(s.CriadorSig, pl.Assinatura, StringComparison.Ordinal))
		{ Avisar(pl, "só quem ergueu a estátua pode reconfigurá-la."); return; }

		if (!int.TryParse(arg.Trim(), out int quantos))
		{
			Avisar(pl, $"a estátua concede {s.Desejos} pedido(s) por invocação. Repita com 1, 2 ou 3 "
					 + "pra mudar -- MAIS PEDIDOS DEIXA CADA UM MAIS FRACO.");
			return;
		}

		// `max(min(input,3),1)` (:65). Um a tres, e o teto e o que separa Shenron de Porunga.
		s.Desejos = Math.Clamp(quantos, 1, 3);
		s.Poder = pl.Ficha.BP;
		RefazerAsEsferas(s);
		SalvarEsferas();

		Avisar(pl, $"a estátua agora concede {s.Desejos} pedido(s) por invocação, com o seu poder atual. "
				 + "As sete se espalharam de novo.");
	}

	/// <summary>`Revive_Set()` (:90-101) -- so o criador, e so vivo. E a UNICA volta de um set inerte.</summary>
	private void ReviverOSet(ServerPlayer pl)
	{
		SetDeEsferas? s = _sets.Find(x => x.Zona.Hash == pl.Zone.Hash);
		if (s == null) { Avisar(pl, "não há Estátua do Dragão neste mundo."); return; }
		if (!string.Equals(s.CriadorSig, pl.Assinatura, StringComparison.Ordinal))
		{ Avisar(pl, "só quem ergueu a estátua pode despertá-la."); return; }
		if (pl.Ficha.dead) { Avisar(pl, "um morto não desperta nada."); return; }

		s.Inerte = false;
		if (_esferas.Count(e => e.Set == s.Id) < Esferas.Total) RefazerAsEsferas(s);
		SalvarEsferas();
		MandarEsferas(pl.Zone);
		Avisar(pl, "a estátua volta a brilhar. As esferas despertam.");
	}

	/// <summary>
	/// `Destroy_Statue()` (:50-58) -- **QUALQUER UM em view(1)**, sem requisito de poder.
	///
	/// Isto e do original e o `desc` da estatua promete ("Anyone can destroy it.", :30). Fica como
	/// esta: mudar seria design novo, e a defesa que o DM oferece e a mesma de tudo neste jogo --
	/// estar la. O set ETERNO tem o override (:388-391) e nao cai.
	/// </summary>
	private void DerrubarAEstatua(ServerPlayer pl, string arg)
	{
		SetDeEsferas? s = _sets.Find(x => x.Zona.Hash == pl.Zone.Hash
			&& Math.Abs(x.Ex - pl.Pos.X) <= AlcanceDeUso && Math.Abs(x.Ey - pl.Pos.Y) <= AlcanceDeUso);

		if (s == null) { Avisar(pl, "não há Estátua do Dragão ao seu alcance."); return; }
		if (s.Eterno) { Avisar(pl, "uma proteção ancestral impede que a estátua seja destruída."); return; }

		if (!string.Equals(arg.Trim(), "sim", StringComparison.OrdinalIgnoreCase))
		{
			Avisar(pl, "derrubar a estátua APAGA as sete esferas deste mundo, para sempre. "
					 + "Repita com o argumento 'sim' pra confirmar.");
			return;
		}

		ZoneKey zona = s.Zona;
		_esferas.RemoveAll(e => e.Set == s.Id);
		_sets.Remove(s);
		FecharAInvocacao(s.Id, "");
		SalvarEsferas();
		MandarEsferas(zona);

		AnunciarNoMundo($"{pl.Name} destrói a Estátua do Dragão de {NomeDoPlaneta(s.ZonaNome)}. "
					  + "As sete esferas viram pó.");
	}

	// =====================================================================
	// REDE
	// =====================================================================
	/// <summary>
	/// Manda as esferas, as estatuas e o dragao de uma zona pra quem esta nela. So quando muda.
	///
	/// ============================ O NOME **NAO** VIAJA, E ISSO E UMA DECISAO ============================
	/// A primeira versao mandava um `nome` por coisa ("Estatua do Dragao de Fulano", "Esfera de 4
	/// estrelas") -- e **nada no cliente lia**. Nao ha tooltip de `Node2D`, nao ha menu da tecla E
	/// apontando pra esfera, nao ha rotulo flutuante: era um campo de rede escrito pra ninguem.
	///
	/// Esse e o defeito que este repo mais paga (dado extraido sem consumidor, 8+ vezes), e a correcao
	/// e tirar e nao inventar um leitor. Quem responde "o que e isto" ja existe e e melhor: `db_ver`
	/// da a estatua inteira (dono, poder, prazo, pedidos) e `db_pegar` nomeia a esfera na hora em que
	/// ela vai pra mao. No dia em que houver um rotulo no mundo, o campo volta -- com leitor.
	/// ==============================================================================================
	/// </summary>
	private void MandarEsferas(ZoneKey zona)
	{
		var w = Protocol.Begin(Protocol.S2C.Esferas);

		List<SetDeEsferas> setsDaqui = [.. _sets.Where(s => s.Zona.Hash == zona.Hash)];
		List<Esfera> noChao = EsferasNoChaoDe(zona);
		List<Invocacao> dragoes = [.. _invocacoes.Values.Where(
			i => SetPorId(i.Set) is { } s && s.Zona.Hash == zona.Hash)];

		w.Put((ushort)(setsDaqui.Count + noChao.Count + dragoes.Count));

		foreach (SetDeEsferas s in setsDaqui)
		{
			w.Put(s.Id * 10);                       // ver `IdNaTela`
			w.Put((byte)Protocol.CoisaDeEsfera.Estatua);
			w.Put((byte)0);
			w.Put(s.Ex);
			w.Put(s.Ey);
			w.Put(!SetAtivo(s));
			w.Put("estatua");
		}

		foreach (Esfera e in noChao)
		{
			SetDeEsferas? s = SetPorId(e.Set);
			w.Put(e.Set * 10 + e.Numero);           // ver `IdNaTela`
			w.Put((byte)Protocol.CoisaDeEsfera.Esfera);
			w.Put((byte)e.Numero);
			w.Put(e.X);
			w.Put(e.Y);
			w.Put(s == null || !SetAtivo(s));
			w.Put(s?.Folha ?? "comum");
		}

		foreach (Invocacao i in dragoes)
		{
			SetDeEsferas? s = SetPorId(i.Set);
			w.Put(i.Set * 10 + 8);                  // ver `IdNaTela`
			w.Put((byte)Protocol.CoisaDeEsfera.Dragao);
			w.Put((byte)0);
			w.Put(i.Onde.X);
			w.Put(i.Onde.Y);
			w.Put(false);
			w.Put(s?.Dragao ?? "shenron");
		}

		foreach (ServerPlayer pl in _players.Values)
			if (pl.Zone.Hash == zona.Hash) pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// ============================ POR QUE O ID DA TELA E `set*10 + n` ============================
	/// O cliente nomeia o node com o id (`"Esfera" + id`), como ja faz com a obra. Set e numero sao
	/// dois contadores independentes -- a esfera 3 do set 1 e a estatua do set 3 colidiriam num id
	/// simples, e uma apagaria a outra. Multiplicar o set por dez abre uma faixa de dez por set:
	/// 0 = estatua, 1..7 = esferas, 8 = o dragao. E cabe folgado: `BallID` vai ate 999.
	/// ======================================================================================
	/// </summary>
	private void MandarEsferasPorHash(ulong hash)
	{
		foreach (ServerPlayer pl in _players.Values)
			if (pl.Zone.Hash == hash) { MandarEsferas(pl.Zone); return; }
	}

	/// <summary>Uma linha pra todo mundo. Mesmo canal do `AnunciarNoMundo`; a escuta e da bancada.</summary>
	private void AnunciarEsferas(string texto)
	{
		if (texto.Length == 0) return;
		EscutaDasEsferas?.Add(texto);
		AnunciarNoMundo(texto);
	}

	// =====================================================================
	// OS VERBOS
	// =====================================================================
	/// <summary>
	/// O CANAL DAS ESFERAS. Prefixo proprio (`db_`) e arquivo proprio pelo mesmo motivo do banco e da
	/// conquista: e um sistema inteiro com estado compartilhado (sets, esferas, invocacoes) e nao meia
	/// duzia de `case` soltos no funil dos verbos.
	/// </summary>
	private bool ComandoDeEsferas(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "db_ver": VerAsEsferas(pl); return true;
			case "db_radar": UsarORadar(pl); return true;
			case "db_pegar": PegarEsfera(pl); return true;
			case "db_largar": LargarEsferas(pl); return true;
			case "db_invocar": InvocarODragao(pl); return true;
			case "db_desejar": PedirDesejo(pl, arg); return true;
			case "db_estatua": ErguerEstatua(pl, arg); return true;
			case "db_refazer": RefazerOSet(pl, arg); return true;
			case "db_reviver": ReviverOSet(pl); return true;
			case "db_derrubar": DerrubarAEstatua(pl, arg); return true;
			default: return false;
		}
	}

	/// <summary>O estado das esferas daqui, em texto. E o que substitui os `desc` do DM.</summary>
	private void VerAsEsferas(ServerPlayer pl)
	{
		SetDeEsferas? s = _sets.Find(x => x.Zona.Hash == pl.Zone.Hash);
		if (s == null)
		{
			Avisar(pl, "não há Estátua do Dragão neste mundo -- e sem estátua não há esferas.");
			Avisar(pl, "  só um Namekuseijin do Clã do Dragão ergue uma, e só num mundo que ele domine "
					 + "(ou na Terra, se for o Guardião).");
			return;
		}

		Avisar(pl, $"-- {s.NomeDoDragao}, {s.TituloDoDragao} --");
		Avisar(pl, s.Eterno
			? "  a estátua ancestral de Namek: não se destrói, não se reconfigura, e nunca morre de vez."
			: $"  erguida por {s.CriadorNome}.");
		Avisar(pl, $"  pedidos por invocação: {s.Desejos} (já usados nesta ativação: {s.Pedidos})");
		Avisar(pl, $"  poder do set: {s.Poder:N0}");

		if (s.Inerte) Avisar(pl, "  INERTES: só quem as criou pode despertá-las.");
		else if (TempoDoMundo < s.AtivoEm)
			Avisar(pl, $"  apagadas -- acordam em {(s.AtivoEm - TempoDoMundo) / 3600.0:0.#} h real(is).");
		else Avisar(pl, "  ACORDADAS. Reúna as sete e invoque.");

		int comigo = _esferas.Count(e => e.Set == s.Id && e.Portador == pl.Id);
		Avisar(pl, $"  com você: {comigo}/{Esferas.Total}"
				 + (_invocacoes.ContainsKey(s.Id) ? $" | {s.NomeDoDragao} ESTÁ DE PÉ agora." : ""));
	}
}
