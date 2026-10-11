using Jandirus.Core.Economia;
using Jandirus.Core.Stats;

namespace Jandirus.Core.Tech;

/// <summary>Uma coisa que da pra construir no mundo.</summary>
public sealed class Construcao
{
	public string Id = "";
	public string Nome = "";
	public string Desc = "";
	public double Custo;
	public double Tech;
	public string[] Racas = [];

	/// <summary>res:// do SpriteFrames do que E ERGUIDO. Vazio = o .dmi nao existe no port.</summary>
	public string Arte = "";

	/// <summary>O `icon_state` dele. Vazio = a animacao "default" da folha.</summary>
	public string Estado = "";

	/// <summary>
	/// BLOQUEIA PASSAGEM. Sai do `create_type` -- o objeto que vai pro CHAO --, e nao do item de
	/// loja: o Creatable de Research_Station tem `density = 0` e o `/obj/Technology/Research_Station`
	/// que ele cria tem `density = 1`. Ler o errado deixava a bancada construida atravessavel ao
	/// lado da mesma maquina, vinda do mapa, que bloqueia.
	/// </summary>
	public bool Densa;

	/// <summary>`pixel_x`/`pixel_y`: a Research Bench e 96 px de largura com `pixel_x = -32`.</summary>
	public double PixelX, PixelY;

	/// <summary>
	/// O TYPEPATH DO QUE VAI PRO CHAO (`create_type`): `/obj/Technology/Research_Station`.
	///
	/// E a chave que liga o CATALOGO ao MAPA. Os `.dmm` do original ja tem bancadas, bancos e
	/// mainframes espalhados, e ate agora eles viravam celula de tilemap -- desenho parado, sem
	/// interacao nenhuma, ao lado de uma copia construida por jogador que funcionava. Com o
	/// typepath aqui, o conversor reconhece a maquina no mapa e a transforma na MESMA coisa.
	/// </summary>
	public string Tipo = "";

	/// <summary>
	/// ISTO E DE USO PESSOAL (se carrega, se veste) em vez de coisa que se assenta no chao?
	///
	/// ============================ A REGRA 2 DO DONO, E ELA E UM DADO ============================
	/// *"item que se poe no chao ganha a acao INSTALAR. Item de uso pessoal (scouter, armaduras,
	/// pesos) NAO ganha -- ele e equipavel."*
	///
	/// O campo vem do `construcoes.json`, extraido do escopo dos verbs do original (`set src in usr`
	/// x `set src in oview`, mais o verb `Bolt`) -- ver `DmVerbScanner` e `DmTechScanner.Classificar`
	/// no pipeline. Ele NAO e uma lista de nomes escrita aqui, e essa e a diferenca que importa: o
	/// port ja teve uma, e ela classificava por AUSENCIA numa tabela de nove linhas -- o que dava
	/// "assentar no chao" pra vinte e oito itens pessoais, a **Armadura** e as nove armas de fogo
	/// entre eles. A Armadura e um dos tres exemplos que o dono citou pelo nome.
	///
	/// O UNICO LEITOR E O `CatalogoDeItens.Get`, que traduz isto na acao "posicionar" -- e dai o
	/// menu do inventario e o servidor leem a MESMA lista de acoes. Ver
	/// `CatalogoDeItens.PodeAssentarNoChao`.
	/// ==========================================================================================
	/// </summary>
	public bool Pessoal;

	/// <summary>
	/// DA PRA CONSTRUIR ISTO, ou e so mobilia do mapa?
	///
	/// O banco e a unica coisa deste catalogo que ninguem ergue: ele nao e um `Creatable` do DM,
	/// e um `/obj/Bank` posto a mao nos mapas. Ele entra aqui mesmo assim porque precisa de tudo
	/// o que uma construcao tem -- arte, densidade, deslocamento, alcance de uso -- e duplicar
	/// isso num segundo catalogo seria manter duas verdades sobre a mesma maquina.
	///
	/// Custo negativo e a marca. Ver `Ofertas`, que o esconde da loja.
	/// </summary>
	public bool Construivel => Custo >= 0;

	/// <summary>
	/// ISTO NAO E OFERTA: pede mais tecnologia do que o original deixa alguem ter.
	///
	/// ============================ O "SPACE POD DE TECH 999" (dono, 2026-10-10) ============================
	/// *"remover space pod de tech 999 q aparece na aba tech"*. No DM a janela de tecnologia APAGA o que o
	/// jogador nao alcanca (`TechWindow.dm:48`: `if(techskill&lt;A.neededtech ...) deleteflag=1`), e o
	/// `techskill` para de ganhar xp em 100 (`TechSupport.dm:27`; `TechMinigame.dm:42-43` escreve "Capped").
	/// Entao `neededtech=999` e o jeito de o original dizer "isto NUNCA aparece na janela": o
	/// `Spacepod_Placed` (`Tier3.dm:16-29`) e o `Boat_Placed` (`Tier 1.5.dm:44-52`) sao BALCOES que quem
	/// desenhou o mapa pos no mundo (`/obj/Creatables/Boat_Placed` na agua de Arconia, `1to26.dmm:1369`) --
	/// clica-se neles la, e cada um vende uma vez so (o `count` do `Click()`).
	///
	/// O port mostra a lista INTEIRA com o motivo de cada nao (ver `Ofertas`), e a marca virou um card:
	/// "Spacepod Placed -- tech 999 -- falta tecnologia", pra sempre. O conserto na raiz e do extrator
	/// (`DmTechScanner.Scan` deixou de emiti-las, e elas sairam do `construcoes.json`); esta pergunta e o
	/// cinto do lado de ca -- um catalogo que voltar com uma delas nao chega a aba, e o servidor grita no boot.
	/// ======================================================================================================
	/// </summary>
	public bool AcimaDoTeto => Construivel && Tech > CatalogoDeObras.TechMaximoDoDm;
}

/// <summary>Por que uma construcao foi recusada. Tipado pelo mesmo motivo das skills.</summary>
public enum RecusaObra
{
	Pode,
	NaoExiste,
	SemTech,
	SemZeni,
	RacaErrada,
	LugarOcupado,
	ZonaProibida,

	/// <summary>
	/// O GEMEO DO <see cref="SemZeni"/> pra quem escolheu pagar com recursos (o dobro do preco -- ver
	/// <see cref="Recurso.CustoEmRecursos"/>). NO FIM do enum de proposito: o valor viaja como byte no
	/// `S2C.Tech`, e entrar no meio renumeraria os que ja estao no fio.
	/// </summary>
	SemRecursos,
}

/// <summary>
/// O CATALOGO DE CONSTRUCOES, extraido dos `obj/Creatables` do DM (comando `tech` do pipeline).
/// 99 entradas com preco em zeni e requisito de tecnologia.
///
/// E A SEGUNDA VIA DE PODER DO JOGO, e por isso ela tem uma moeda propria: o Saiyajin sobe socando
/// pedra, o humano sobe ESTUDANDO, e o que o estudo compra sao coisas no mundo. As duas escalas
/// tem que ser lidas juntas -- o mainframe que transforma um humano em androide pede tech 50 e
/// meio milhao de zeni, que e o equivalente, em horas, a subir uma transformacao.
/// </summary>
public sealed class CatalogoDeObras
{
	private readonly Dictionary<string, Construcao> _porId = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, Construcao> _porTipo = new(StringComparer.Ordinal);

	public int Total => _porId.Count;
	public IEnumerable<Construcao> Todas => _porId.Values;
	public Construcao? Get(string id) => _porId.GetValueOrDefault(id);

	/// <summary>
	/// OS IDS QUE O ARQUIVO TRAZ MAIS DE UMA VEZ. Lista vazia e o unico resultado aceitavel.
	///
	/// ============================ O `Staff` E O `PDA` REPETIDOS (2026-10-10) ============================
	/// O id e a chave: duas linhas com o mesmo viram UMA construcao -- a ultima do arquivo --, e a outra some
	/// sem ninguem ser avisado. Foi assim que o `Staff` da aba Tech era uma arma que o original desativou (tech
	/// 11) e nao o cajado magico de verdade (tech 1), que estava no arquivo e nunca chegou a tela.
	///
	/// O conserto e do extrator (`DmTechScanner`: o tipo declarado em dois arquivos e UM tipo); esta lista e o
	/// cinto do lado de ca, lido por <see cref="Parse"/>: o servidor grita no boot e a bancada `instalar-prova`
	/// reprova o arquivo.
	/// ==================================================================================================
	/// </summary>
	public List<string> Repetidos { get; } = [];

	/// <summary>
	/// ENTRA UMA CONSTRUCAO QUE NAO VEIO DO DISCO -- o Enma Daioh (`Alem.TipoDoEnma`), que no DM e um MOB
	/// de conversa (`SkyNPCs.dm:33`) e aqui e uma obra fixa do mapa com menu E. `Custo` negativo a deixa
	/// fora da aba Tech (`Construivel`); o `PorTypepath` nao a conhece porque ela nao tem typepath.
	/// </summary>
	public void Acrescentar(Construcao c) => _porId[c.Id] = c;

	/// <summary>
	/// A CONSTRUCAO CUJO `create_type` E ESTE TYPEPATH -- a ponte entre o catalogo e os `.dmm`.
	///
	/// E o que o conversor de mapa usa pra decidir que aquela celula nao e cenario: ela e uma
	/// maquina que o jogo ja sabe desenhar, bloquear e usar. Ver `MapConverter`.
	/// </summary>
	public Construcao? PorTypepath(string typepath) =>
		typepath.Length == 0 ? null : _porTipo.GetValueOrDefault(typepath);

	/// <summary>
	/// AS CONSTRUCOES MUDAS: as que BLOQUEIAM e nao tem desenho. Uma lista vazia e o unico
	/// resultado aceitavel, e por isso ela e uma pergunta e nao um `if` solto no meio da carga.
	///
	/// ============================ POR QUE ISTO MORA NO CORE ============================
	/// Porque a regra tem TRES leitores e eles nao se falam:
	///
	///   * o SERVIDOR bloqueia a celula pelo campo <see cref="Construcao.Densa"/>;
	///   * o CLIENTE desenha pelo campo <see cref="Construcao.Arte"/>;
	///   * a BANCADA cobra os dois de uma vez.
	///
	/// Enquanto cada lado so olhava o campo dele, uma construcao densa e sem arte era um obstaculo
	/// que nao existe na tela -- e ninguem tinha como notar, porque nenhum dos dois lados via o par.
	/// Foi metade da queixa do dono ("tem uma PAREDE INVISIVEL no meio do mapa") e o port ja
	/// produziu esse defeito quatro vezes por outros caminhos (a bandeira de conquista sem `.dmi`
	/// convertido, o `Ship_Control`/`Ship_Pad` fora do catalogo, 35 atlas escritos e nunca
	/// importados, a costura de Hera).
	///
	/// Escrita aqui, ela e a MESMA pergunta pro servidor (que grita na carga) e pra bancada (que
	/// reprova antes de o jogo subir). Duas copias divergiriam no primeiro campo que alguem
	/// acrescentasse, e a que envelheceria seria a da bancada -- a que ninguem olha quando esta
	/// verde.
	/// ==================================================================================
	/// </summary>
	public IEnumerable<Construcao> SemDesenho() =>
		Todas.Where(c => c.Densa && c.Arte.Length == 0);

	/// <summary>
	/// O TETO DE TECNOLOGIA DO ORIGINAL: o `techskill` do DM so ganha xp ate 100 (`TechSupport.dm:27`,
	/// `if(techskill&lt;=99)`). O que pede mais do que isto nunca esteve a venda -- ver
	/// <see cref="Construcao.AcimaDoTeto"/>. (O estudo do port nao tem teto; a regra e sobre o CATALOGO.)
	/// </summary>
	public const double TechMaximoDoDm = 100;

	/// <summary>
	/// DEFEITO INJETADO (bancada `--recursosteste`): o que pede tecnologia acima do teto volta a ser oferta --
	/// o jogo de antes de 2026-10-10, em que o "Spacepod Placed" de tech 999 era um card da aba Tech. Lido
	/// pelas duas linhas do conserto (<see cref="Ofertas"/> e <see cref="Permitida(Construcao, Fighter, string, Moeda)"/>).
	/// </summary>
	public static bool AcimaDoTetoViraOfertaDeTeste;

	/// <summary>
	/// AS QUE PEDEM TECNOLOGIA ACIMA DO TETO. Lista vazia e o unico resultado aceitavel num catalogo saido do
	/// extrator de hoje; o servidor a le no boot e grita por cada uma. Ver <see cref="Construcao.AcimaDoTeto"/>.
	/// </summary>
	public IEnumerable<Construcao> AcimaDoTeto() => Todas.Where(c => c.AcimaDoTeto);

	/// <summary>
	/// O QUE ESTE PERSONAGEM CONSEGUE COMPRAR AGORA, e o motivo de cada nao.
	///
	/// Devolve TUDO que a raca permite, inclusive o que nao cabe no bolso -- ver a lista inteira
	/// com "faltam 40 de tecnologia" ao lado e o que transforma tecnologia numa meta. Uma lista
	/// que so mostra o comprável esconde o jogo.
	/// </summary>
	public List<(Construcao Obra, RecusaObra Motivo)> Ofertas(Fighter f, string raca)
	{
		var l = new List<(Construcao, RecusaObra)>();
		foreach (Construcao c in _porId.Values.OrderBy(c2 => c2.Tech).ThenBy(c2 => c2.Custo))
		{
			// MOBILIA DE MAPA NAO E OFERTA. O banco existe no catalogo pra ter arte e densidade,
			// nao pra ser vendido -- ver `Construcao.Construivel`.
			if (!c.Construivel) continue;
			// NEM O BALCAO DE MAPA DO ORIGINAL (o "tech 999") -- ver `Construcao.AcimaDoTeto`.
			if (c.AcimaDoTeto && !AcimaDoTetoViraOfertaDeTeste) continue;
			RecusaObra r = Permitida(c, f, raca);
			if (r == RecusaObra.RacaErrada) continue;   // nem existe pra voce: nao vira linha
			l.Add((c, r));
		}
		return l;
	}

	/// <summary>
	/// A CELULA QUE UMA CONSTRUCAO OCUPA.
	///
	/// UMA celula, mesmo quando o desenho tem tres tiles de largura -- e a mesma regra que o
	/// conversor de mapa aplica a arvore: no BYOND densidade e do OBJETO, e um objeto mora num
	/// tile. Usar a extensao do icone faria a bancada barrar o corredor inteiro.
	///
	/// A ANCORA SAO OS PES, nao o centro do corpo: a construcao nasce onde o jogador ESTAVA, e a
	/// posicao guardada e a do no dele (o meio do sprite). Sem o deslocamento, construir encostado
	/// na parede de baixo poria a maquina uma celula acima de onde o jogador a viu nascer.
	///
	/// Mora no Core porque as DUAS pontas precisam da mesma conta: o servidor pra bloquear, o
	/// cliente pra desenhar. Se divergirem, a maquina aparece num lugar e barra noutro.
	/// </summary>
	public static (int X, int Y) Celula(double x, double y)
	{
		const int t = Jandirus.Core.World.ZoneCollision.TileSize;
		return ((int)Math.Floor(x / t),
				(int)Math.Floor((y + Jandirus.Core.World.MoveRules.FeetOffsetY) / t));
	}

	/// <summary>Pagando em ZENI -- e a pergunta de sempre, e a que o catalogo que vai no fio responde.</summary>
	public static RecusaObra Permitida(Construcao c, Fighter f, string raca) => Permitida(c, f, raca, Moeda.Zeni);

	/// <summary>
	/// ESTE PERSONAGEM PODE FABRICAR ISTO, PAGANDO NESTA MOEDA? Raca e tecnologia valem igual pras duas; so a
	/// ultima pergunta -- a da carteira -- muda, e ela mora em <see cref="ComSaldo"/>.
	/// </summary>
	public static RecusaObra Permitida(Construcao c, Fighter f, string raca, Moeda moeda)
	{
		// NAO SE CONSTROI MOBILIA DE MAPA. A guarda mora aqui e nao so no `Ofertas` porque o
		// pedido de construir chega do CLIENTE, e esconder da lista nunca foi permissao.
		if (!c.Construivel) return RecusaObra.NaoExiste;
		// ...NEM O QUE PEDE TECNOLOGIA ACIMA DO TETO, pela mesma razao -- ver `Construcao.AcimaDoTeto`.
		if (c.AcimaDoTeto && !AcimaDoTetoViraOfertaDeTeste) return RecusaObra.NaoExiste;
		if (c.Racas.Length > 0 && !c.Racas.Contains(raca, StringComparer.OrdinalIgnoreCase))
			return RecusaObra.RacaErrada;
		if (f.techskill < c.Tech) return RecusaObra.SemTech;
		return ComSaldo(RecusaObra.Pode, c.Custo, moeda == Moeda.Recursos ? f.Recursos : f.Zeni, moeda);
	}

	/// <summary>
	/// O VEREDITO DE UMA OFERTA COM A CARTEIRA DE AGORA, nesta moeda.
	///
	/// ============================ POR QUE ISTO E UMA FUNCAO DO CORE ============================
	/// O catalogo que vai no fio (`S2C.Tech`) traz o veredito do servidor em ZENI, tirado no instante do envio.
	/// A carteira passou a mudar sozinha -- quem minera ganha recurso a cada segundo -- e reenviar cem ofertas
	/// por segundo pra dizer "agora da" seria pagar o catalogo inteiro por um numero. Entao o servidor manda a
	/// CARTEIRA (`S2C.Carteira`, dezessete bytes) e a aba refaz so esta ultima pergunta, com a MESMA funcao que
	/// o servidor usa ao cobrar (<see cref="Permitida(Construcao, Fighter, string, Moeda)"/> termina aqui).
	///
	/// O QUE NAO DEPENDE DE CARTEIRA FICA COMO VEIO: raca, tecnologia e "isto nao se fabrica" o servidor ja
	/// julgou, e nenhum saldo muda. So `Pode`, `SemZeni` e `SemRecursos` sao refeitos.
	/// ==========================================================================================
	/// </summary>
	public static RecusaObra ComSaldo(RecusaObra doServidor, double custoEmZeni, double saldo, Moeda moeda)
	{
		if (doServidor is not (RecusaObra.Pode or RecusaObra.SemZeni or RecusaObra.SemRecursos)) return doServidor;
		return moeda == Moeda.Recursos
			? (saldo >= Recurso.CustoEmRecursos(custoEmZeni) ? RecusaObra.Pode : RecusaObra.SemRecursos)
			: (saldo >= custoEmZeni ? RecusaObra.Pode : RecusaObra.SemZeni);
	}

	public static CatalogoDeObras Parse(string json)
	{
		var cat = new CatalogoDeObras();
		foreach (string bloco in Blocos(json))
		{
			var c = new Construcao
			{
				Id = Str(bloco, "id"),
				Nome = Str(bloco, "nome"),
				Desc = Str(bloco, "desc"),
				Custo = Num(bloco, "custo"),
				Tech = Num(bloco, "tech"),
				Racas = Lista(bloco, "racas"),
				Arte = Str(bloco, "arte"),
				Estado = Str(bloco, "estado"),
				Densa = Num(bloco, "densa") > 0,
				PixelX = Num(bloco, "px"),
				PixelY = Num(bloco, "py"),
				Tipo = Str(bloco, "tipo"),

				// SEM O CAMPO, A CONSTRUCAO E DE CHAO -- e o padrao certo pra um catalogo antigo:
				// o que este arquivo sempre teve foi maquina, e uma maquina que nao se instala e
				// uma linha morta na mochila. Ver `Construcao.Pessoal`.
				Pessoal = Num(bloco, "pessoal") > 0,
			};
			if (c.Id.Length == 0) continue;
			if (cat._porId.ContainsKey(c.Id)) cat.Repetidos.Add(c.Id);
			cat._porId[c.Id] = c;

			// A PRIMEIRA VENCE. Duas entradas podem apontar pro mesmo `create_type` (o Creatable e
			// uma variante dele); a ordem do arquivo e por tech e custo, entao a primeira e a mais
			// barata -- que e a que faz sentido um mapa ter espalhado.
			if (c.Tipo.Length > 0) cat._porTipo.TryAdd(c.Tipo, c);
		}
		return cat;
	}

	// ---- o mesmo leitor de uma pagina do catalogo de skills ----
	private static IEnumerable<string> Blocos(string s)
	{
		int i = 0;
		while (true)
		{
			int a = s.IndexOf('{', i);
			if (a < 0) yield break;
			int b = s.IndexOf('}', a);
			if (b < 0) yield break;
			yield return s[(a + 1)..b];
			i = b + 1;
		}
	}

	private static string Str(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return "";
		int a = bloco.IndexOf('"', bloco.IndexOf(':', i) + 1);
		if (a < 0) return "";
		var sb = new System.Text.StringBuilder();
		for (int k = a + 1; k < bloco.Length; k++)
		{
			if (bloco[k] == '\\' && k + 1 < bloco.Length) { sb.Append(bloco[++k]); continue; }
			if (bloco[k] == '"') break;
			sb.Append(bloco[k]);
		}
		return sb.ToString();
	}

	private static double Num(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return 0;
		int a = bloco.IndexOf(':', i) + 1;
		int b = a;
		while (b < bloco.Length && (char.IsDigit(bloco[b]) || bloco[b] is '.' or '-' or ' ')) b++;
		return double.TryParse(bloco[a..b].Trim(), System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
	}

	private static string[] Lista(string bloco, string chave)
	{
		int i = bloco.IndexOf($"\"{chave}\"", StringComparison.Ordinal);
		if (i < 0) return [];
		int a = bloco.IndexOf('[', i);
		int b = bloco.IndexOf(']', a + 1);
		if (a < 0 || b < 0) return [];
		var l = new List<string>();
		string dentro = bloco[(a + 1)..b];
		int k = 0;
		while (true)
		{
			int q1 = dentro.IndexOf('"', k);
			if (q1 < 0) break;
			int q2 = dentro.IndexOf('"', q1 + 1);
			if (q2 < 0) break;
			l.Add(dentro[(q1 + 1)..q2]);
			k = q2 + 1;
		}
		return [.. l];
	}
}
