using System.Text.Json;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;

namespace Jandirus.Server;

/// <summary>Uma construcao DE PE no mundo. E o que o `mundo.json` guarda.</summary>
public sealed class Obra
{
	public int Id;
	public string Tipo = "";        // o id do catalogo ("Research_Station")
	public string Zona = "";        // ZoneKey.Name
	public float X, Y;
	public string DonoConta = "";   // quem ergueu -- CONTA, nao nome (ver o comentario do trono)
	public string DonoNome = "";
	public bool Aparafusada;        // `Bolted`: aparafusada nao se carrega, e so o dono desfaz

	/// <summary>
	/// VEIO DO MAPA, e nao de alguem que a ergueu.
	///
	/// ============================ POR QUE ELA NAO VAI PRO DISCO ============================
	/// Ela nasce do `.objetos` da zona a cada boot -- a fonte dela e o mapa, nao o `mundo.json`.
	/// Gravar as duas juntas duplicaria cada bancada a cada reinicio, e no dia em que o mapa fosse
	/// reconvertido o disco teria a versao velha discutindo com a nova.
	///
	/// No mais ela e uma construcao como qualquer outra: bloqueia, aparece na tela, responde aos
	/// comandos e conta pro alcance de uso. E isso e a coisa toda -- a bancada que estava no mapa
	/// desde sempre passa a servir pra estudar.
	/// </summary>
	public bool DoMapa;

	/// <summary>
	/// QUE LABORATORIO FOI INSTALADO no mainframe: 0 nenhum, 1 Android Lab, 2 Bio-Android Lab.
	///
	/// E assim no original e vale manter: nao se CONSTROI um laboratorio, se constroi o
	/// mainframe e depois se escolhe o que ele vira. A escolha e definitiva, e e ela que
	/// separa quem quis virar maquina de quem quis FAZER uma.
	/// </summary>
	public int Lab;
	public double Vida = 8;         // `DNL_LAB_HEALTH`: 8 golpes derrubam
	public long ErguidaEm;

	/// <summary>Se for um laboratorio: em que estado esta a gestacao (ver <see cref="Gestacao"/>).</summary>
	public Gestacao? Fornada;
}

/// <summary>
/// A GESTACAO DO BIO-ANDROIDE. Um mes in-game (`DNL_BIO_BREW_MONTHS 0.1` de ano) contando os
/// DNAs colhidos -- e quem morre quando termina e o CRIADOR.
/// </summary>
public sealed class Gestacao
{
	public List<string> Dna = [];        // as racas colhidas
	public double MaiorBp;               // BP do doador mais forte: o bio nasce com metade
	public long PrometidaEm;             // ms reais em que a gestacao termina
	public string DonoConta = "";
}

/// <summary>
/// TECNOLOGIA: o caminho de quem nao sobe socando pedra.
///
/// TRES SISTEMAS QUE SE SEGURAM: estudar sobe `techskill`, `techskill` + zeni compram CONSTRUCOES,
/// e duas construcoes especificas (os laboratorios) transformam quem as ergueu -- em androide, ou
/// no criador de um bio-androide. Cortar qualquer um deixa os outros sem sentido: sem construcao
/// nao ha o que fazer com tecnologia, e sem os labs nao ha por que juntar meio milhao de zeni.
///
/// O MUNDO PERSISTE SEPARADO DAS CONTAS (`mundo.json`). Uma construcao nao e propriedade de um
/// personagem, e uma coisa que EXISTE no chao -- e continua existindo com o dono desconectado, ou
/// morto. Guardar isso dentro do save do dono faria o laboratorio piscar junto com ele.
/// </summary>
public partial class GameServer
{
	private CatalogoDeObras? _obras;
	private readonly List<Obra> _noChao = [];
	private int _proximaObraId = 1;

	/// <summary>O raio, em pixels, dentro do qual da pra usar uma construcao.</summary>
	private const float AlcanceDeUso = 64f;

	/// <summary>
	/// TECH MINIMO PRO LABORATORIO DE DNA (`DNL_INT_REQ`). Setenta pontos: e o numero que faz um
	/// humano precisar de uma vida de estudo pra chegar onde um Saiyajin chega nascendo.
	/// </summary>
	private const double TechDoLaboratorio = 70;

	private const double CustoAndroideAbsorcao = 1_000_000;
	private const double CustoAndroideInfinito = 2_000_000;
	private const double BpAndroideAbsorcao = 1_000_000;
	private const double BpAndroideInfinito = 2_000_000;

	/// <summary>
	/// A GESTACAO: UM MES IN-GAME (`DNL_BIO_BREW_MONTHS 0.1` de ano, que o comentario do original
	/// traduz como "1 MES in-game").
	///
	/// NA ESCALA DESTE JOGO ISSO E ENORME -- o dia in-game tem 24 minutos reais, entao trinta
	/// dias sao DOZE HORAS de relogio. E o preco certo: e a mesma escala que faz Terra->Namek
	/// levar 168 minutos, e o bio-androide e a coisa mais cara que alguem pode fazer. Quem
	/// comeca uma fornada tem que defender aquele tanque por meio dia.
	///
	/// A primeira versao disto multiplicava `0.1 * 12` e dava 1,2 DIAS em vez de um mes -- o erro
	/// classico de misturar "fracao de ano" com "numero de meses". Por isso a conta esta escrita
	/// em dias, que e a unidade que o resto do jogo usa.
	/// </summary>
	private const double DiasDeGestacao = 30;

	private double GestacaoSegundosReais => _gestacaoDeTeste > 0
		? _gestacaoDeTeste
		: DiasDeGestacao * Espaco.SegundosPorDiaInGame;

	/// <summary>Bancada: encurta a gestacao (`--gestacaoteste N`, em segundos reais).</summary>
	private double _gestacaoDeTeste;

	/// <summary>`DNL_BIO_BP_SHARE`: o bio nasce com metade do BP do doador mais forte.</summary>
	private const double BioFracaoDoDoador = 0.5;

	/// <summary>`DNL_BIO_MAX_DNA`.</summary>
	private const int BioMaxDna = 4;

	private string CaminhoDoMundo => System.IO.Path.Combine(_store?.Pasta ?? ".", "mundo.json");

	private void CarregarTech()
	{
		const string cj = "res://Assets/Data/construcoes.json";
		if (Godot.FileAccess.FileExists(cj))
		{
			_obras = CatalogoDeObras.Parse(Godot.FileAccess.GetFileAsString(cj));
			GD.Print($"[server] construcoes: {_obras.Total} no catalogo");
		}
		else GD.PushWarning("[server] sem construcoes.json -- rode o AssetPipeline (comando 'tech')");

		try
		{
			if (System.IO.File.Exists(CaminhoDoMundo))
			{
				List<Obra>? l = JsonSerializer.Deserialize<List<Obra>>(
					System.IO.File.ReadAllText(CaminhoDoMundo), new JsonSerializerOptions { IncludeFields = true });
				if (l != null) _noChao.AddRange(l);
				_proximaObraId = _noChao.Count > 0 ? _noChao.Max(o => o.Id) + 1 : 1;
				GD.Print($"[server] mundo: {_noChao.Count} construcoes de pe");
			}
		}
		catch (Exception e) { GD.PushWarning($"[server] mundo.json ilegivel: {e.Message}"); }

		// AS OBRAS DO DISCO TAMBEM BLOQUEIAM. Sem esta volta, uma bancada erguida ontem so viraria
		// parede quando alguem construisse OUTRA coisa na mesma zona -- e o `MandarObras` fosse
		// chamado por acaso. Bloquear na carga e o que faz o mundo salvo valer desde o boot.
		int densas = 0;
		foreach (string zona in _noChao.Select(o => o.Zona).Distinct())
		{
			AplicarColisaoDasObras(ZoneKey.Premade(zona));
			densas += _noChao.Count(o => o.Zona == zona && _obras?.Get(o.Tipo) is { Densa: true });
		}
		if (densas > 0) GD.Print($"[server] construcoes que bloqueiam: {densas}");

		CarregarObjetosDoMapa();
	}

	/// <summary>
	/// AS MAQUINAS QUE O MAPA JA TRAZ viram construcoes de pe.
	///
	/// ============================ O QUE ISTO CONSERTA ============================
	/// Banco, bancada de pesquisa, sala de gravidade e os laboratorios estao espalhados pelos
	/// `.dmm` do original desde sempre -- e eram celula de tilemap: desenho parado, sem estado e
	/// sem resposta. Um jogador podia ERGUER uma bancada ao lado de outra que ja estava la e so a
	/// dele funcionava. As duas eram a mesma maquina; uma era um node e a outra era pintura.
	///
	/// Registrando-as aqui, elas entram inteiras no sistema que ja existe: o mesmo pacote as
	/// desenha, a mesma colisao as bloqueia, o mesmo alcance de uso as alcanca e os mesmos
	/// comandos as usam. Estudar numa bancada do mapa passa a funcionar sem uma linha nova no
	/// caminho de estudar.
	/// =============================================================================
	///
	/// APARAFUSADAS de saida: `estudar` exige bancada aparafusada, e mobilia de mapa nao tem dono
	/// pra aparafusar. Ela sempre esteve la -- e o mesmo que dizer que ela e parte do lugar.
	/// </summary>
	private void CarregarObjetosDoMapa()
	{
		if (_catalogo == null) return;

		int n = 0, semArte = 0;
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.Objetos.Length == 0 || !Godot.FileAccess.FileExists(e.Objetos)) continue;

			foreach (ObjetoDoMapa o in ObjetosDoMapa.Parse(Godot.FileAccess.GetFileAsString(e.Objetos)))
			{
				if (_obras?.Get(o.Id) == null) { semArte++; continue; }

				// A CELULA VIRA PIXEL NO MEIO DELA. O conversor grava a celula; a obra guarda a
				// posicao do CORPO que a ergueu, e `CatalogoDeObras.Celula` desfaz isso somando o
				// deslocamento dos pes. Aqui a conta e a inversa, e tem que ser a mesma -- senao a
				// maquina desenha uma celula acima de onde ela esta no mapa.
				const int t = ZoneCollision.TileSize;
				_noChao.Add(new Obra
				{
					Id = _proximaObraId++,
					Tipo = o.Id,
					Zona = e.Zona,
					X = o.X * t + t / 2f,
					Y = o.Y * t + t / 2f - MoveRules.FeetOffsetY,
					DonoNome = "",
					Aparafusada = true,
					DoMapa = true,
					ErguidaEm = 0,
				});
				n++;
			}

			AplicarColisaoDasObras(ZoneKey.Premade(e.Zona));
		}

		if (n > 0) GD.Print($"[server] maquinas do mapa: {n} viraram construcoes de pe");
		if (semArte > 0)
			GD.PushWarning($"[server] {semArte} maquina(s) do mapa sem entrada no catalogo -- "
						   + "rode o AssetPipeline ('tech' e depois 'maps')");
	}

	private void GravarMundo()
	{
		try
		{
			// SO O QUE ALGUEM ERGUEU. A mobilia do mapa nasce do `.objetos` a cada boot; grava-la
			// aqui duplicaria cada bancada a cada reinicio. Ver `Obra.DoMapa`.
			System.IO.File.WriteAllText(CaminhoDoMundo,
				JsonSerializer.Serialize(_noChao.Where(o => !o.DoMapa).ToList(),
										 new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
		}
		catch (Exception e) { GD.PushWarning($"[server] nao gravei o mundo: {e.Message}"); }
	}

	// =====================================================================
	// CONSTRUIR
	// =====================================================================
	/// <summary>
	/// ERGUE UMA CONSTRUCAO onde a pessoa esta.
	///
	/// A POSICAO E A DO SERVIDOR, nao a que o cliente mandou. Deixar o cliente escolher onde
	/// construir seria deixar ele construir do outro lado do mapa, dentro de parede, ou dentro da
	/// casa de outro. Constroi-se onde se esta -- e onde se esta o servidor ja sabe.
	/// </summary>
	private void Construir(ServerPlayer pl, string tipo)
	{
		if (_obras == null) { Avisar(pl, "o servidor esta sem catalogo de construcoes."); return; }
		Construcao? c = _obras.Get(tipo);
		if (c == null) { Avisar(pl, "isso nao existe."); return; }

		RecusaObra r = CatalogoDeObras.Permitida(c, pl.Ficha, pl.Race);
		if (r != RecusaObra.Pode) { Avisar(pl, MotivoObra(r, c, pl)); return; }

		// NAO EMPILHA: duas construcoes no mesmo lugar viram uma so na tela e as duas respondem
		// ao mesmo clique. Meio tile de folga e o bastante pra nao encavalar.
		if (_noChao.Any(o => o.Zona == pl.Zone.Name
							 && Math.Abs(o.X - pl.Pos.X) < 24 && Math.Abs(o.Y - pl.Pos.Y) < 24))
		{ Avisar(pl, "ja tem coisa demais neste ponto."); return; }

		pl.Ficha.Zeni -= c.Custo;
		var obra = new Obra
		{
			Id = _proximaObraId++,
			Tipo = c.Id,
			Zona = pl.Zone.Name,
			X = pl.Pos.X,
			Y = pl.Pos.Y,
			DonoConta = pl.Conta,
			DonoNome = pl.Name,
			ErguidaEm = NowMs(),
		};
		_noChao.Add(obra);
		GravarMundo();

		GD.Print($"[server] {pl.Name} ergueu {c.Nome} (#{obra.Id}) em {obra.Zona} @ ({obra.X:0},{obra.Y:0})");
		Avisar(pl, $"voce ergue {c.Nome}. Restam {pl.Ficha.Zeni:N0} zeni.");
		MandarObras(pl.Zone);
	}

	private static string MotivoObra(RecusaObra r, Construcao c, ServerPlayer pl) => r switch
	{
		RecusaObra.SemTech => $"{c.Nome} pede {c.Tech:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.",
		RecusaObra.SemZeni => $"{c.Nome} custa {c.Custo:N0} zeni -- voce tem {pl.Ficha.Zeni:N0}.",
		RecusaObra.RacaErrada => $"{c.Nome} nao e coisa de {pl.Race}.",
		_ => "nao deu pra construir.",
	};

	/// <summary>
	/// APARAFUSA (o `Bolt` do original): a construcao para de ser carregavel e passa a poder ser
	/// USADA. E o que separa uma caixa no chao de uma instalacao.
	/// </summary>
	private void Aparafusar(ServerPlayer pl)
	{
		Obra? o = ObraPerto(pl);
		if (o == null) { Avisar(pl, "nao ha nada aqui pra aparafusar."); return; }
		if (o.DonoConta != pl.Conta) { Avisar(pl, "isso nao e seu."); return; }

		o.Aparafusada = !o.Aparafusada;
		GravarMundo();
		Avisar(pl, o.Aparafusada
			? $"voce aparafusa {NomeDaObra(o)} no chao."
			: $"voce solta {NomeDaObra(o)} do chao.");
		MandarObras(pl.Zone);
	}

	private Obra? ObraPerto(ServerPlayer pl) => _noChao
		.Where(o => o.Zona == pl.Zone.Name)
		.OrderBy(o => (o.X - pl.Pos.X) * (o.X - pl.Pos.X) + (o.Y - pl.Pos.Y) * (o.Y - pl.Pos.Y))
		.FirstOrDefault(o => Math.Abs(o.X - pl.Pos.X) <= AlcanceDeUso && Math.Abs(o.Y - pl.Pos.Y) <= AlcanceDeUso);

	private string NomeDaObra(Obra o) => _obras?.Get(o.Tipo)?.Nome ?? o.Tipo;

	// =====================================================================
	// ESTUDAR -- o que faz o techskill subir
	// =====================================================================
	/// <summary>
	/// ESTUDA numa Research Station. E o `Experiment()` do original, e a regra que importa dele e
	/// esta: <b>so se ganha tecnologia numa estacao</b> ("Research Stations are the only really
	/// good way of getting Tech XP at all"). Estudar sozinho no campo nao existe.
	///
	/// Sem material pra sacrificar (o port ainda nao tem itens), o custo e TEMPO: a estacao rende
	/// por segundo enquanto a pessoa fica nela. O ganho passa pelo `techmod`, como la.
	/// </summary>
	private void TickDoEstudo()
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (!pl.Estudando || pl.Ficha.KO || pl.Ficha.dead) continue;

			Obra? est = _noChao.FirstOrDefault(o => o.Tipo == "Research_Station" && o.Aparafusada
				&& o.Zona == pl.Zone.Name
				&& Math.Abs(o.X - pl.Pos.X) <= AlcanceDeUso && Math.Abs(o.Y - pl.Pos.Y) <= AlcanceDeUso);
			if (est == null)
			{
				pl.Estudando = false;
				Avisar(pl, "voce precisa de uma Research Station aparafusada por perto pra estudar.");
				continue;
			}

			int subiu = pl.Ficha.Estudar(XpDeEstudoPorSegundo);

			// O PAINEL TEM QUE ANDAR ENQUANTO SE ESTUDA. Um nivel leva mais de vinte minutos no
			// topo da curva; sem a barra de XP mexendo, quem estuda nao tem NENHUM sinal de que a
			// coisa esta funcionando -- e para. O pacote so sai pra quem esta debrucado na
			// bancada, entao e um por segundo por estudante, nao por jogador.
			MandarCatalogoDeObras(pl);
			if (subiu <= 0) continue;

			Avisar(pl, $"seu estudo rende: tecnologia agora e {pl.Ficha.techskill:0}.");
			GD.Print($"[server] {pl.Name}: tech {pl.Ficha.techskill:0}");
			pl.SigAtributos = "";
		}
	}

	/// <summary>
	/// QUANTO XP UM SEGUNDO DE ESTUDO VALE.
	///
	/// Calibrado pra que os 70 pontos do laboratorio de DNA custem algumas HORAS, nao alguns
	/// minutos: com a curva de <see cref="Jandirus.Core.Stats.Fighter.TechXpDoProximo"/>, 15 por
	/// segundo poe o nivel 70 por volta de 4 h de estudo continuo. E o preco certo pra uma coisa
	/// que transforma um humano comum num androide.
	/// </summary>
	private const double XpDeEstudoPorSegundo = 15;

	// =====================================================================
	// OS LABORATORIOS
	// =====================================================================
	/// <summary>
	/// VIRAR ANDROIDE. O `Android Lab` do original: um humano com tecnologia e dinheiro reconstroi
	/// o proprio corpo por dentro.
	///
	/// O QUE NAO MUDA: icone, roupas e skills. A maquina mexe no que ha DENTRO -- e por isso o
	/// personagem continua sendo o mesmo personagem, com a mesma cara. Trocar a aparencia junto
	/// faria parecer que o jogador perdeu o personagem em vez de transformar.
	///
	/// O BP VIRA OU SOMA (`dnl_apply_android_bp`): quem estava abaixo do piso e alcado ate ele;
	/// quem ja passou GANHA o valor por cima. Sem essa segunda regra, um humano forte ficaria mais
	/// fraco ao se tornar androide, e ninguem faria isso depois de treinar.
	/// </summary>
	private void VirarAndroide(ServerPlayer pl, bool infinito)
	{
		if (ObraPerto(pl) is not { Aparafusada: true, Lab: 1 })
		{ Avisar(pl, "voce precisa estar dentro de um Android Lab."); return; }
		if (pl.Race != "Human") { Avisar(pl, "essa maquina foi feita pra fisiologia humana."); return; }
		if (pl.Ficha.techskill < TechDoLaboratorio)
		{
			Avisar(pl, $"instalar isso pede {TechDoLaboratorio:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.");
			return;
		}

		double custo = infinito ? CustoAndroideInfinito : CustoAndroideAbsorcao;
		if (pl.Ficha.Zeni < custo) { Avisar(pl, $"a conversao custa {custo:N0} zeni."); return; }

		double piso = infinito ? BpAndroideInfinito : BpAndroideAbsorcao;
		pl.Ficha.Zeni -= custo;
		pl.Ficha.BP = pl.Ficha.BP < piso ? piso : pl.Ficha.BP + piso;
		pl.Race = "Android";
		pl.Ficha.AndroideInfinito = infinito;
		pl.Ficha.AndroideAbsorcao = !infinito;
		pl.Ficha.Statify();
		pl.SigAtributos = "";

		GD.Print($"[server] {pl.Name} virou androide ({(infinito ? "energia infinita" : "absorcao")}), BP {pl.Ficha.BP:N0}");
		Avisar(pl, infinito
			? "a maquina se fecha sobre voce. Quando abre, voce nao sente mais fome nem cansaco -- a energia simplesmente NAO ACABA."
			: "a maquina se fecha sobre voce. Quando abre, o Ki alheio parece... comestivel.");
		Persistir(pl);
	}

	/// <summary>
	/// COLHE DNA de alguem NOCAUTEADO. O `Extract DNA` do original.
	///
	/// So de nocauteado, e isso e a mecanica inteira: pra fazer um bio-androide e preciso DERRUBAR
	/// gente forte primeiro. O cientista nao escapa de lutar -- ele escapa de lutar SOZINHO.
	/// </summary>
	private void ColherDna(ServerPlayer pl)
	{
		Obra? lab = LabDeBio(pl);
		if (lab == null) { Avisar(pl, "voce precisa de um Bio-Android Lab aparafusado por perto."); return; }

		ServerPlayer? vitima = _players.Values
			.Where(o => o != pl && o.Zone.Equals(pl.Zone) && (o.Ficha.KO || o.Ficha.dead))
			.OrderBy(o => Vec2.Distance(o.Pos, pl.Pos))
			.FirstOrDefault(o => Vec2.Distance(o.Pos, pl.Pos) <= AlcanceDeUso);

		if (vitima == null) { Avisar(pl, "nao ha ninguem caido ao seu alcance."); return; }

		lab.Fornada ??= new Gestacao { DonoConta = pl.Conta };
		if (lab.Fornada.PrometidaEm > 0) { Avisar(pl, "a gestacao ja comecou -- nao da pra acrescentar DNA."); return; }
		if (lab.Fornada.Dna.Count >= BioMaxDna) { Avisar(pl, $"o tanque so comporta {BioMaxDna} amostras."); return; }

		lab.Fornada.Dna.Add(vitima.Race);
		lab.Fornada.MaiorBp = Math.Max(lab.Fornada.MaiorBp, vitima.Ficha.BP);
		GravarMundo();

		Avisar(pl, $"voce colhe DNA de {vitima.Name} ({vitima.Race}). "
				   + $"Amostras: {lab.Fornada.Dna.Count}/{BioMaxDna}.");
		Avisar(vitima, "alguem enfia uma agulha em voce enquanto voce esta caido.");
	}

	/// <summary>
	/// COMECA A GESTACAO. Um mes in-game, e destruir o laboratorio cancela.
	/// </summary>
	private void GestarBio(ServerPlayer pl)
	{
		Obra? lab = LabDeBio(pl);
		if (lab?.Fornada == null || lab.Fornada.Dna.Count == 0)
		{ Avisar(pl, "o tanque esta vazio -- colha DNA primeiro."); return; }
		if (lab.Fornada.PrometidaEm > 0)
		{
			double falta = (lab.Fornada.PrometidaEm - NowMs()) / 1000.0;
			Avisar(pl, $"ja esta gestando. Faltam {falta / 60:0.#} minutos.");
			return;
		}

		lab.Fornada.PrometidaEm = NowMs() + (long)(GestacaoSegundosReais * 1000);
		GravarMundo();
		GD.Print($"[server] {pl.Name} iniciou gestacao de bio-androide ({string.Join("+", lab.Fornada.Dna)})");
		Avisar(pl, $"o tanque se fecha. A criatura leva um mes pra ficar pronta "
				   + $"({GestacaoSegundosReais / 60:0} minutos reais). Nao deixe destruirem o laboratorio.");
	}

	private Obra? LabDeBio(ServerPlayer pl)
	{
		Obra? o = ObraPerto(pl);
		return o is { Aparafusada: true, Lab: 2 } ? o : null;
	}

	/// <summary>
	/// INSTALA UM LABORATORIO no mainframe aparafusado. E o passo do meio que o original tem e
	/// que e facil achar burocratico -- ate perceber que ele e a ESCOLHA: o mesmo meio milhao de
	/// zeni vira "eu viro maquina" ou "eu construo uma". Um mainframe so faz uma das duas coisas.
	/// </summary>
	private void InstalarLab(ServerPlayer pl, int qual)
	{
		Obra? o = ObraPerto(pl);
		if (o is not { Tipo: "Android_Creation_Mainframe" })
		{ Avisar(pl, "voce precisa estar perto de um mainframe de criacao de androides."); return; }
		if (!o.Aparafusada) { Avisar(pl, "aparafuse o mainframe no chao antes de instalar."); return; }
		if (o.DonoConta != pl.Conta) { Avisar(pl, "isso nao e seu."); return; }
		if (o.Lab != 0) { Avisar(pl, "este mainframe ja foi convertido -- ergue outro."); return; }
		if (pl.Race != "Human") { Avisar(pl, "a instalacao pede um humano."); return; }
		if (pl.Ficha.techskill < TechDoLaboratorio)
		{
			Avisar(pl, $"instalar pede {TechDoLaboratorio:0} de tecnologia -- voce tem {pl.Ficha.techskill:0}.");
			return;
		}

		o.Lab = qual;
		GravarMundo();
		Avisar(pl, qual == 1
			? "o mainframe vira um Android Lab. Agora da pra entrar nele."
			: "o mainframe vira um Bio-Android Lab. Agora falta o DNA.");
		MandarObras(pl.Zone);
	}

	/// <summary>
	/// A GESTACAO TERMINOU. No original a criatura MATA o criador e o jogador passa a jogar com
	/// ela -- e essa e a parte que da peso ao sistema inteiro: nao e um pet, e uma sucessao.
	///
	/// O PORT AINDA NAO TROCA O PERSONAGEM DA CONTA (a criacao de personagem passa pela tela de
	/// slots, e forcar um slot por fora dela abriria um segundo caminho de criacao que ninguem
	/// mais valida). Entao aqui a criatura NASCE, mata o criador e fica anotada; a troca de corpo
	/// e o passo que falta, e esta escrito em vez de fingido.
	/// </summary>
	private void TickDaGestacao()
	{
		long agora = NowMs();
		foreach (Obra lab in _noChao.ToList())
		{
			if (lab.Fornada is not { PrometidaEm: > 0 } g || agora < g.PrometidaEm) continue;

			double bp = g.MaiorBp * BioFracaoDoDoador;
			ServerPlayer? criador = _players.Values.FirstOrDefault(p => p.Conta == g.DonoConta);

			GD.Print($"[server] bio-androide pronto no lab #{lab.Id}: DNA {string.Join("+", g.Dna)}, BP {bp:N0}");
			if (criador != null)
			{
				Avisar(criador, "o tanque se abre. A coisa la dentro olha pra voce -- e ela sabe exatamente "
								+ "de quem ela veio.");
				// A CRIATURA MATA O CRIADOR. E o original inteiro, e e o que fecha o ciclo: quem
				// faz um bio-androide esta assinando a propria sentenca, e sabe disso desde a
				// primeira agulha. Sem isto o sistema vira "ganhe um pet muito forte".
				criador.Ficha.dead = true;
				criador.Ficha.KO = true;
				criador.RenasceEm = NowMs() + MsAteRenascer;
			}

			lab.Fornada = null;
			lab.Vida = 0;   // o tanque se rompe ao nascer
			_noChao.Remove(lab);
			GravarMundo();
			MandarObras(ZoneKey.Premade(lab.Zona));
		}
	}

	// =====================================================================
	// REDE
	// =====================================================================
	/// <summary>Manda as construcoes de uma zona pra quem esta nela. So quando muda.</summary>
	private void MandarObras(ZoneKey zona)
	{
		List<Obra> daZona = [.. _noChao.Where(o => o.Zona == zona.Name)];
		var w = Protocol.Begin(Protocol.S2C.Construcoes);
		w.Put((ushort)daZona.Count);
		foreach (Obra o in daZona)
		{
			Construcao? c = _obras?.Get(o.Tipo);
			w.Put(o.Id);
			w.Put(o.Tipo);
			w.Put(o.X);
			w.Put(o.Y);
			w.Put(o.Aparafusada);
			w.Put((byte)o.Lab);
			w.Put(o.DonoNome);
			// A ARTE VIAJA JUNTO. O cliente tem o catalogo, mas so o que ELE pode comprar -- e a
			// bancada de outra pessoa tem que aparecer do mesmo jeito. Mandar o caminho aqui evita
			// que "ver" dependa de "poder construir".
			w.Put(c?.Arte ?? "");
			w.Put(c?.Estado ?? "");
			w.Put((float)(c?.PixelX ?? 0));
			w.Put((float)(c?.PixelY ?? 0));
			// ...e a DENSIDADE pelo mesmo motivo: o cliente tem que barrar o corpo no que o
			// servidor barra, e sem isto ele teria que adivinhar pelo catalogo -- que so lista o
			// que ELE pode comprar.
			w.Put(c?.Densa ?? false);
		}
		foreach (ServerPlayer pl in _players.Values)
			if (pl.Zone.Equals(zona)) pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);

		AplicarColisaoDasObras(zona);
	}

	/// <summary>
	/// AS CONSTRUCOES VIRAM PAREDE no mapa da zona.
	///
	/// ============================ POR QUE ISTO FALTAVA ============================
	/// A queixa do dono foi sobre o banco: "o banco n tem fisica, eu atravesso ele". O banco do
	/// MAPA foi consertado na arvore de tipos do proprio DM. Mas a construcao ERGUIDA por um
	/// jogador nunca teve fisica em lugar nenhum -- ela era so um desenho no cliente. A mesma
	/// Research Station bloqueia quando vem do `.dmm` e nao bloqueava quando alguem a construia.
	/// ==============================================================================
	///
	/// REFAZ A ZONA INTEIRA em vez de somar a celula nova: e uma lista de dezenas, muda so quando
	/// alguem constroi ou derruba, e o caminho incremental teria que acertar tambem o caso de
	/// remocao -- que e onde uma parede fantasma ficaria pra tras sem nada apontando pra ela.
	/// </summary>
	private void AplicarColisaoDasObras(ZoneKey zona)
	{
		ZoneCollision? mapa = _catalogo?.Get(zona)?.Mapa;
		if (mapa == null) return;

		mapa.LimparObras();
		foreach (Obra o in _noChao)
		{
			if (o.Zona != zona.Name) continue;
			if (_obras?.Get(o.Tipo) is not { Densa: true }) continue;
			(int cx, int cy) = CatalogoDeObras.Celula(o.X, o.Y);
			mapa.Bloquear(cx, cy);
		}
	}

	/// <summary>Manda o catalogo com o motivo de cada recusa PRA MIM. E a aba Tech.</summary>
	private void MandarCatalogoDeObras(ServerPlayer pl)
	{
		if (_obras == null) return;
		List<(Construcao Obra, RecusaObra Motivo)> ofertas = _obras.Ofertas(pl.Ficha, pl.Race);

		var w = Protocol.Begin(Protocol.S2C.Tech);
		w.Put(pl.Ficha.techskill);
		w.Put(pl.Ficha.Zeni);
		w.Put(pl.Ficha.techXp);
		w.Put(pl.Ficha.TechXpDoProximo());
		w.Put((ushort)ofertas.Count);
		foreach ((Construcao c, RecusaObra r) in ofertas)
		{
			w.Put(c.Id);
			w.Put(c.Nome);
			w.Put(c.Custo);
			w.Put(c.Tech);
			w.Put((byte)r);
		}
		pl.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>O canal unico de tecnologia, do mesmo jeito que o de habilidade.</summary>
	private void ComandoDeTech(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "lista": MandarCatalogoDeObras(pl); break;
			case "construir": Construir(pl, arg); break;
			case "aparafusar": Aparafusar(pl); break;
			case "estudar":
				pl.Estudando = !pl.Estudando;
				Avisar(pl, pl.Estudando ? "voce se debruca sobre a bancada." : "voce para de estudar.");
				break;
			case "androide_absorcao": VirarAndroide(pl, infinito: false); break;
			case "androide_infinito": VirarAndroide(pl, infinito: true); break;
			case "lab_androide": InstalarLab(pl, 1); break;
			case "lab_bio": InstalarLab(pl, 2); break;
			case "colher_dna": ColherDna(pl); break;
			case "gestar": GestarBio(pl); break;
			default: Avisar(pl, $"comando de tecnologia desconhecido: {cmd}"); break;
		}

		// QUALQUER COMANDO PODE TER MEXIDO NO ZENI OU NA RACA, e as duas coisas mudam o que a aba
		// mostra e o que ela deixa comprar. Reenviar o catalogo depois de TODOS e mais barato que
		// lembrar, comando a comando, qual deles precisava -- e e o esquecimento nesse tipo de
		// lista que deixa a interface mentindo.
		if (cmd != "lista") MandarCatalogoDeObras(pl);
	}
}
