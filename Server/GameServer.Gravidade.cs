using System.Text.Json.Serialization;
using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// O ESTADO DE UMA MAQUINA DE GRAVIDADE. Ver `Obra.Gravidade`.
///
/// Os valores iniciais sao os do original (`Gravity.dm:198-210`): a maquina sai da fabrica com teto
/// de 10x, sem alcance, com uma unica carga de bateria e desligada.
/// </summary>
public sealed class GravidadeDaObra
{
	/// <summary>
	/// A gravidade LIGADA agora. Zero = desligada.
	///
	/// NAO VAI PRO DISCO: no original toda maquina acorda desligada (`New()`: `Grav = 0`, `Gravity.dm:176`).
	/// Antes ela ia junto com o resto da obra, e voltar ligada dependia de alguem ter gravado o mundo
	/// depois do ajuste -- uma sala de treino que as vezes sobrevivia ao reinicio e as vezes nao.
	/// </summary>
	[JsonIgnore] public double Grav;

	/// <summary>O teto DESTA maquina. Sobe 20% por melhoria de forca.</summary>
	public double Max = 10;

	/// <summary>O alcance do campo -- ver <see cref="CampoDeGravidade"/>. O original para em 10.</summary>
	public int Range;

	/// <summary>
	/// A estabilizacao, comprada uma vez so. **NAO FAZ NADA, E E DO ORIGINAL**: o `Stability` de la e
	/// escrito pela melhoria (`Gravity.dm:259`), mostrado pelo `Info` (`:216`) e nunca lido por regra
	/// nenhuma do jogo. Fica como esta ate o dono decidir o que ela deveria fazer.
	/// </summary>
	public bool Estavel;

	public double Energia = 1, EnergiaMax = 1;

	/// <summary>Nivel de regeneracao por nanites: a chance, em pontos percentuais, de recarregar.</summary>
	public int Nanites;

	// ---------------------------------------------------------------------
	// A MUDANCA EM ESPERA -- o `sleep(50)` do `Click()` (ver `GameServer.DefinirGravidade`)
	// ---------------------------------------------------------------------
	/// <summary>Pra quanto a gravidade vai, quando a espera acabar.</summary>
	[JsonIgnore] public double Pedida;

	/// <summary>Quando ela muda (ms). Zero = nada em espera -- e e o `choosinggrav` do original.</summary>
	[JsonIgnore] public long MudaEm;

	/// <summary>Quem pediu: o anuncio da mudanca leva o nome.</summary>
	[JsonIgnore] public string PedidaPor = "";

	// ---------------------------------------------------------------------
	// O LIQUIDO -- os tiles que o campo alcanca (ver `CampoDeGravidade.Preencher`)
	// ---------------------------------------------------------------------
	/// <summary>Nulo = ainda nao encheu, ou algo mudou e ele se refaz na proxima leitura.</summary>
	[JsonIgnore] public HashSet<(int X, int Y)>? Cheias;

	[JsonIgnore] public long EncheuEm;

	/// <summary>A marca do desenho do liquido: muda quando o formato muda, e e o que faz o cliente redesenhar.</summary>
	[JsonIgnore] public int MarcaDoLiquido;
}

/// <summary>
/// A MAQUINA DE GRAVIDADE -- `Gravity.dm` do original.
///
/// ============================ ELA E O PRIMEIRO OBJETO COM ESTADO ============================
/// Banco e bancada nao guardam nada: usar duas vezes da o mesmo resultado. A maquina de gravidade
/// e outra coisa -- ela tem bateria que drena, teto que sobe, alcance que cresce, e um numero
/// LIGADO que muda o mundo em volta. Foi por causa dela que `Obra` ganhou estado e que o menu de
/// interacao aprendeu a fazer perguntas.
/// ============================================================================================
///
/// ============================ O QUE NAO FUNCIONAVA (dono, 2026-10-09) ============================
/// *"verifique se a maquina de gravidade tb esta funcionando"*. Nao estava, por quatro coisas:
///
///   1. QUEM ESTAVA NO CAMPO so era decidido quando alguem MEXIA na maquina (o `AplicarCampos` que
///      morava aqui). Entrar andando num campo ligado nao pesava; sair -- ate de planeta -- nao
///      aliviava. Agora e relido a cada volta do laco de stats, como la (<see cref="LerOsCampos"/>);
///   2. O TAMANHO DO CAMPO nao era o do original (ver <see cref="CampoDeGravidade"/>);
///   3. O MENU DA TECLA E nao tinha o parafuso, e a maquina nao liga solta (ver `Interacoes`);
///   4. A MUDANCA ERA NA HORA. La o clique neutraliza o campo, avisa *"Gravity changing in five
///      seconds"* e so entao muda (`Gravity.dm:291-312`) -- e o tempo de quem nao quer ficar na sala
///      sair dela.
///
/// E UMA REGRA NOVA, do dono: o campo se molda ao lugar, igual um liquido (ver `CampoDeGravidade`).
/// ================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// A CADA QUANTO A BATERIA DRENA, em ms. O `sleep(100)` do DM sao dez segundos.
	/// </summary>
	private const long MsDoDrenoDeGravidade = 10_000;
	private long _proximoDreno;

	/// <summary>Quanto de bateria um ponto de gravidade custa por passada (`Energy -= Grav*0.01`).</summary>
	private const double DrenoPorPontoDeGravidade = 0.01;

	private bool ComandoDeGravidade(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "grav_definir": DefinirGravidade(pl, arg); return true;
			case "grav_info": InfoDaGravidade(pl); return true;
			case "grav_up": MelhorarGravidade(pl, arg); return true;
			default: return false;
		}
	}

	/// <summary>A maquina perto de mim, ja com o estado garantido.</summary>
	private GravidadeDaObra? MaquinaPerto(ServerPlayer pl, out Obra? obra)
	{
		obra = ObraQueAceita(pl, "grav_definir");
		if (obra == null) return null;

		// O ESTADO NASCE NA PRIMEIRA VEZ, e nao na construcao: maquinas que ja estavam no
		// `mundo.json` de antes desta mudanca voltam do disco sem ele, e uma delas sem estado seria
		// uma excecao nula no primeiro uso.
		obra.Gravidade ??= new GravidadeDaObra();
		return obra.Gravidade;
	}

	/// <summary>
	/// O `Click()` DA MAQUINA (`Gravity.dm:277-321`).
	///
	/// ============================ A ORDEM DE LA, E O QUE MUDA ============================
	/// La: confere bateria e parafuso -> NEUTRALIZA o campo -> pergunta o numero -> avisa "cinco segundos"
	/// -> espera -> apara -> liga. Aqui o numero ja vem digitado (o teclado do menu perguntou antes de
	/// mandar o verbo), entao neutralizar e avisar acontecem juntos, e a espera e a mesma.
	///
	/// ENQUANTO ELA ESPERA, NINGUEM PEDE DE NOVO -- o `choosinggrav` (`:289`). Sem ele dois jogadores
	/// brigando pelo painel deixariam a sala sem gravidade pra sempre: cada pedido zera e rearma.
	/// =====================================================================================
	/// </summary>
	private void DefinirGravidade(ServerPlayer pl, string arg)
	{
		GravidadeDaObra? g = MaquinaPerto(pl, out Obra? obra);
		if (g == null || obra == null) { Avisar(pl, "não há máquina de gravidade por perto."); return; }

		if (g.Energia <= 0) { Avisar(pl, "a máquina está sem bateria."); return; }
		if (!obra.Aparafusada) { Avisar(pl, "aparafuse a máquina antes de usá-la."); return; }
		if (g.MudaEm != 0) { Avisar(pl, "a máquina já está mudando de gravidade: espere ela assentar."); return; }

		if (!double.TryParse(arg, out double pedido)) { Avisar(pl, "número inválido."); return; }

		// TRES CORTES, na ordem do original: o teto da MAQUINA, o piso, e o teto do MUNDO. O do
		// mundo vem por ultimo porque ele vale mesmo pra uma maquina melhorada alem dele -- a
		// progressao de forca passa de 500 por volta da vigesima segunda melhoria.
		double teto = Math.Min(g.Max, Interacoes.TetoDeGravidade);
		g.Pedida = Math.Clamp(pedido, 0, teto);
		if (g.Pedida < pedido) Avisar(pl, $"esta máquina não passa de {teto:0}x.");

		bool estavaLigada = g.Grav > 0;
		g.Grav = 0;
		g.PedidaPor = pl.Name;
		g.MudaEm = NowMs() + (long)(CampoDeGravidade.SegundosAteMudar * 1000);

		AnunciarAVista(obra, estavaLigada
			? "máquina de gravidade: campo neutralizado. A gravidade muda em cinco segundos."
			: "máquina de gravidade: a gravidade muda em cinco segundos.");

		// A MAQUINA DE FABRICA NAO PEGA NINGUEM (alcance 0), e isso e do original. Sem este aviso quem a
		// liga pela primeira vez ve o anuncio de "50x" e nao sente nada -- e conclui que ela esta quebrada.
		if (g.Pedida > 0 && g.Range <= 0)
			Avisar(pl, "o alcance desta máquina é 0: o campo não sai do tile dela e não pega ninguém. "
					   + "Compre \"Alcance\" em \"Melhorar...\".");

		LerOsCampos();   // quem estava no campo sai dele AGORA, e nao na proxima volta
	}

	private void InfoDaGravidade(ServerPlayer pl)
	{
		GravidadeDaObra? g = MaquinaPerto(pl, out Obra? obra);
		if (g == null || obra == null) { Avisar(pl, "não há máquina de gravidade por perto."); return; }

		Avisar(pl, $"-- {NomeDaObra(obra)} --");
		if (!obra.Aparafusada) Avisar(pl, "  solta: aparafuse pra poder ligar.");
		Avisar(pl, g.MudaEm != 0
			? $"  gravidade agora: 0x -- mudando pra {g.Pedida:0}x (teto desta máquina: {g.Max:0}x)"
			: $"  gravidade agora: {g.Grav:0}x (teto desta máquina: {g.Max:0}x)");
		Avisar(pl, $"  bateria: {g.Energia * 100:0} de {g.EnergiaMax * 100:0}");
		Avisar(pl, g.Range <= 0
			? "  alcance: 0 -- o campo não sai do tile da máquina (não pega ninguém)"
			: $"  alcance: {g.Range} -- o campo cobre até {g.Range + 1} tiles de lado em volta dela, e para em parede");
		if (g is { Grav: > 0, Cheias: { } cheias })
			Avisar(pl, $"  o campo alcança {cheias.Count - 1} tile(s) agora");
		Avisar(pl, $"  estabilização: {(g.Estavel ? "sim" : "não")}");
		if (g.Nanites > 0) Avisar(pl, $"  nanites: {g.Nanites}");

		// OS PRECOS SAO DO ESTADO ATUAL, e por isso saem daqui e nao do catalogo de interacoes --
		// eles mudam a cada melhoria comprada.
		Avisar(pl, "-- melhorias --");
		Avisar(pl, $"  força do campo: {CustoDeForca(g):N0} zeni");
		Avisar(pl, $"  bateria: {CustoDeBateria(g):N0} zeni");
		Avisar(pl, g.Range < AlcanceMaximo
			? $"  alcance: {CustoDeAlcance(g):N0} zeni"
			: $"  alcance: no máximo ({AlcanceMaximo})");
		Avisar(pl, g.Estavel ? "  estabilização: já comprada" : $"  estabilização: {CustoDeEstabilidade:N0} zeni");
		Avisar(pl, $"  nanites: {CustoDeNanites(g):N0} zeni (pede {TechDosNanites:0} de tecnologia)");
	}

	// Os precos do `verb/Upgrade` do DM (Gravity.dm:229-233), cada um em funcao do estado atual.
	private const int AlcanceMaximo = 10;
	private const double CustoDeEstabilidade = 500_000;
	private const double TechDosNanites = 6;
	private static double CustoDeForca(GravidadeDaObra g) => 5 * g.Max;
	private static double CustoDeBateria(GravidadeDaObra g) => 50 * g.EnergiaMax;
	private static double CustoDeAlcance(GravidadeDaObra g) => 500 * (g.Range + 1);
	private static double CustoDeNanites(GravidadeDaObra g) => 500 * (g.Nanites + 1);

	private void MelhorarGravidade(ServerPlayer pl, string qual)
	{
		GravidadeDaObra? g = MaquinaPerto(pl, out Obra? obra);
		if (g == null || obra == null) { Avisar(pl, "não há máquina de gravidade por perto."); return; }

		double custo;
		string feito;

		switch (qual)
		{
			case "forca":
				custo = CustoDeForca(g);
				if (!Cobrar(pl, custo)) return;
				g.Max = Math.Round(g.Max * 1.2);
				feito = $"o campo aguenta {g.Max:0}x agora";
				break;

			case "bateria":
				custo = CustoDeBateria(g);
				if (!Cobrar(pl, custo)) return;
				g.EnergiaMax *= 2;
				g.Energia = g.EnergiaMax;   // a melhoria ENCHE, como no original
				feito = "bateria dobrada e recarregada";
				break;

			case "alcance":
				if (g.Range >= AlcanceMaximo) { Avisar(pl, $"o alcance já está no máximo ({AlcanceMaximo})."); return; }
				custo = CustoDeAlcance(g);
				if (!Cobrar(pl, custo)) return;
				g.Range++;
				g.Cheias = null;   // o liquido tem mais onde escorrer
				feito = $"o campo cobre até {g.Range + 1} tiles de lado em volta dela";
				LerOsCampos();
				break;

			case "estabilidade":
				if (g.Estavel) { Avisar(pl, "esta máquina já está estabilizada."); return; }
				custo = CustoDeEstabilidade;
				if (!Cobrar(pl, custo)) return;
				g.Estavel = true;
				feito = "campo estabilizado";
				break;

			case "nanites":
				if (pl.Ficha.techskill < TechDosNanites)
				{
					Avisar(pl, $"nanites pedem {TechDosNanites:0} de tecnologia -- você tem {pl.Ficha.techskill:0}.");
					return;
				}
				custo = CustoDeNanites(g);
				if (!Cobrar(pl, custo)) return;
				g.Nanites++;
				feito = $"regeneração de nanites em {g.Nanites}";
				break;

			default: Avisar(pl, "essa melhoria não existe."); return;
		}

		GravarMundo();
		Avisar(pl, $"você melhora a máquina por {custo:N0} zeni: {feito}. Restam {pl.Ficha.Zeni:N0}.");
	}

	/// <summary>Tira o zeni, ou explica que nao da. Devolve se pagou.</summary>
	private bool Cobrar(ServerPlayer pl, double quanto)
	{
		if (pl.Ficha.Zeni < quanto)
		{
			Avisar(pl, $"isso custa {quanto:N0} zeni -- você tem {pl.Ficha.Zeni:N0}.");
			return false;
		}
		pl.Ficha.Zeni -= quanto;
		return true;
	}

	// =====================================================================
	// O CAMPO
	// =====================================================================
	private long _proximaLeituraDosCampos;

	/// <summary>As maquinas LIGADAS na ultima leitura, de todas as zonas, com o liquido em dia.</summary>
	private readonly List<Obra> _camposLigados = [];

	/// <summary>A marca do desenho dos campos de cada zona: o que ja foi mandado, e o que ha agora.</summary>
	private Dictionary<ZoneKey, int> _desenhoDosCampos = [], _desenhoAnterior = [];

	/// <summary>
	/// QUEM ESTA DENTRO DE QUAL CAMPO, e com quanta gravidade -- o bloco do `Grav()` de `Gravity.dm:135-146`.
	///
	/// ============================ E UMA LEITURA, E NAO UM DELTA ============================
	/// O comentario de la conta a historia: o campo ja foi somado e subtraido por entrada e saida da caixa
	/// (`BBCross`/`BBUnCross`), e *"dessincronizava"*. Virou `gravmult = fieldgrav`, recalculado a cada
	/// volta -- *"Modelo SET = robusto"*. E o que este metodo faz: o numero de cada corpo e ESCRITO a
	/// partir de onde ele esta agora, e nao acertado por diferenca. Trocar de zona, ser arremessado pra
	/// fora, teleportar e morrer nao precisam de linha propria -- a proxima leitura ja os ve fora.
	/// =======================================================================================
	///
	/// ============================ OS CAMPOS SOMAM ============================
	/// Duas maquinas sobrepostas somam a gravidade delas (`fieldgrav += GM.Grav` no DM), e nao vence
	/// a maior. E o que permite montar uma sala de treino empilhando maquinas baratas em vez de
	/// melhorar uma cara -- e o original deixa isso de pe de proposito.
	/// =========================================================================
	/// </summary>
	private void LerOsCampos()
	{
		long agora = NowMs();

		// (1) as maquinas ligadas, cada uma com o liquido dela em dia
		_camposLigados.Clear();
		foreach (Obra o in _noChao)
		{
			if (o.Gravidade is not { Grav: > 0, Energia: > 0 } g) continue;
			if (g.Cheias == null || agora >= g.EncheuEm + (long)(CampoDeGravidade.SegundosEntreEnchentes * 1000))
				Encher(o, g, agora);
			_camposLigados.Add(o);
		}

		// (2) cada corpo, lido de onde esta
		foreach (ServerPlayer pl in _players.Values)
		{
			double campo = 0;
			foreach (Obra o in _camposLigados)
			{
				GravidadeDaObra g = o.Gravidade!;
				if (o.Zona.Equals(pl.Zone) && CampoDeGravidade.Alcanca(o.X, o.Y, g.Range, pl.Pos, g.Cheias!))
					campo += g.Grav;
			}

			if (Math.Abs(pl.Ficha.gravmult - campo) < 0.001) continue;

			pl.Ficha.gravmult = campo;
			pl.Ficha.Statify();
			pl.SigAtributos = "";
			Avisar(pl, campo > 0
				? $"o campo de gravidade te pressiona: {campo:0}x além do planeta."
				: "você sai do campo de gravidade.");
		}

		// (3) o desenho: so a zona cujo conjunto de campos mudou recebe pacote
		(_desenhoAnterior, _desenhoDosCampos) = (_desenhoDosCampos, _desenhoAnterior);
		_desenhoDosCampos.Clear();
		foreach (Obra o in _camposLigados)
		{
			GravidadeDaObra g = o.Gravidade!;
			_desenhoDosCampos[o.Zona] =
				HashCode.Combine(_desenhoDosCampos.GetValueOrDefault(o.Zona), o.Id, g.Range, g.MarcaDoLiquido);
		}
		foreach ((ZoneKey zona, int marca) in _desenhoDosCampos)
			if (!_desenhoAnterior.TryGetValue(zona, out int velha) || velha != marca) MandarCampos(zona);
		foreach (ZoneKey zona in _desenhoAnterior.Keys)
			if (!_desenhoDosCampos.ContainsKey(zona)) MandarCampos(zona);   // o ultimo campo da zona caiu
	}

	/// <summary>
	/// ENCHE O CAMPO DE UMA MAQUINA: escorre do tile dela ate onde o alcance deixa, parando em parede.
	/// Refeito de segundo em segundo com o campo de pe (`CampoDeGravidade.SegundosEntreEnchentes`), porque
	/// parede se ergue e cai.
	/// </summary>
	private void Encher(Obra o, GravidadeDaObra g, long agora)
	{
		ZoneKey zona = o.Zona;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		HashSet<(int X, int Y)> cheias = CampoDeGravidade.Preencher(
			CampoDeGravidade.CelulaDaMaquina(o.X, o.Y), g.Range, (cx, cy) => ParedeProCampo(zona, mapa, cx, cy));

		// SOMA, e nao encadeamento: a ordem em que um conjunto se enumera nao e promessa.
		int marca = cheias.Count;
		foreach ((int x, int y) in cheias) marca += HashCode.Combine(x, y);

		g.Cheias = cheias;
		g.MarcaDoLiquido = marca;
		g.EncheuEm = agora;
	}

	/// <summary>
	/// ============================ O QUE REPRESA A GRAVIDADE ============================
	/// PAREDE DE TERRENO, e so ela:
	///
	///   * o que alguem ERGUEU -- parede e PORTA (`BlocoEm`). A porta conta ABERTA OU FECHADA: ela e o vao
	///     da parede, e um campo que derramasse pro corredor toda vez que alguem entra na sala faria a
	///     gravidade do vizinho piscar;
	///   * a porta do MAPA, pelo mesmo motivo (aberta, ela sai da colisao -- por isso e perguntada antes);
	///   * a parede do ARQUIVO que o estrago nao abriu, e a beirada do mapa.
	///
	/// MOBILIA NAO REPRESA (a camada de obras do `ZoneCollision`): uma bancada no meio da sala barra o
	/// corpo, e nao a gravidade -- senao a propria maquina, que e densa, represaria o proprio campo.
	/// ====================================================================================
	/// </summary>
	private bool ParedeProCampo(ZoneKey zona, ZoneCollision? mapa, int cx, int cy)
	{
		if (BlocoEm(zona, cx, cy) is { } bloco) return bloco.Def.Classe != ClasseDeBloco.Piso;
		if (mapa == null) return false;

		// fora do bitset: parede -- menos onde nao ha borda (a Sala do Tempo, ver `ZoneCollision.SemBorda`),
		// cujo anel de fora tambem nao e parede de lugar nenhum.
		if (cx < 0 || cy < 0 || cx >= mapa.Width || cy >= mapa.Height) return !mapa.SemBorda;
		if (mapa.SemBorda && mapa.NaBorda(cx, cy)) return false;

		if (zona.Kind == ZoneKey.KindPremade && _portasDoMapa.TryGetValue(zona.Name, out List<PortaDoMapa>? portas))
			foreach (PortaDoMapa p in portas)
				if (p.X == cx && p.Y == cy) return true;

		return mapa.BloqueadaNoArquivo(cx, cy) && !mapa.Aberta(cx, cy);
	}

	/// <summary>
	/// O DESENHO DOS CAMPOS DE UMA ZONA, pra quem esta nela (ou so pra quem acabou de chegar): cada
	/// maquina ligada, o alcance e os tiles que o liquido alcancou. Ver `Protocol.S2C.CamposDeGravidade`.
	///
	/// SEM ISTO O CAMPO ERA INVISIVEL: o original desenha um `Gravity Field.dmi` esticado pelo alcance em
	/// cima da maquina (`Gravity.dm:316-318`), e o port nao desenhava nada. Com o campo se moldando ao
	/// lugar, ver ONDE ele chegou deixou de ser enfeite.
	/// </summary>
	private void MandarCampos(ZoneKey zona, ServerPlayer? soPra = null)
	{
		int n = 0;
		foreach (Obra o in _camposLigados)
			if (o.Zona.Equals(zona)) n++;
		n = Math.Min(n, byte.MaxValue);

		NetDataWriter w = Protocol.Begin(Protocol.S2C.CamposDeGravidade);
		w.Put((byte)n);
		foreach (Obra o in _camposLigados)
		{
			if (!o.Zona.Equals(zona)) continue;
			if (n-- <= 0) break;

			GravidadeDaObra g = o.Gravidade!;
			(int mx, int my) = CampoDeGravidade.CelulaDaMaquina(o.X, o.Y);
			w.Put((short)mx);
			w.Put((short)my);
			w.Put((byte)g.Range);
			// O DESLOCAMENTO CABE NUM BYTE: o liquido nao passa de `TilesDeRaio(10)` = 5 tiles da maquina.
			w.Put((ushort)g.Cheias!.Count);
			foreach ((int x, int y) in g.Cheias)
			{
				w.Put((sbyte)(x - mx));
				w.Put((sbyte)(y - my));
			}
		}

		if (soPra != null) { soPra.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered); return; }
		if (!_zones.TryGetValue(zona.Hash, out List<ServerPlayer>? naZona)) return;
		foreach (ServerPlayer p in naZona) p.Peer?.Send(w, Protocol.ChannelReliable, DeliveryMethod.ReliableOrdered);
	}

	/// <summary>
	/// O RELOGIO DAS MAQUINAS: a bateria (de dez em dez segundos), a mudanca em espera e a leitura de quem
	/// esta no campo (a cada volta do laco de stats -- `CampoDeGravidade.SegundosEntreLeituras`).
	///
	/// UM RELOGIO SO PRO MUNDO, e nao um por maquina: o original tinha um laco por objeto, o que
	/// com dezenas de maquinas no chao seriam dezenas de laços fazendo a mesma conta.
	/// </summary>
	private void TickDaGravidade()
	{
		long agora = NowMs();
		bool daMaquina = DrenarAsBaterias(agora);

		if (agora >= _proximaLeituraDosCampos)
		{
			_proximaLeituraDosCampos = agora + (long)(CampoDeGravidade.SegundosEntreLeituras * 1000);
			daMaquina |= AssentarOsPedidos(agora);

			// A RELEITURA DE TODA VOLTA: e esta linha que faz entrar andando pesar e sair aliviar.
			if (!CampoDeGravidade.SoMudaNaMaquinaDeTeste) { LerOsCampos(); return; }
		}

		// ...e a de quando a MAQUINA mudou (ligou, a bateria acabou), que nao espera a volta.
		if (daMaquina) LerOsCampos();
	}

	/// <summary>A espera de cinco segundos acabou: a gravidade pedida passa a valer. Devolve se alguma mudou.</summary>
	private bool AssentarOsPedidos(long agora)
	{
		bool mudou = false;
		foreach (Obra o in _noChao)
		{
			if (o.Gravidade is not { MudaEm: > 0 } g || agora < g.MudaEm) continue;

			g.MudaEm = 0;
			g.Grav = g.Pedida;
			g.Cheias = null;
			mudou = true;
			AnunciarAVista(o, g.Grav <= 0
				? $"{g.PedidaPor} deixa a gravidade normal."
				: $"{g.PedidaPor} ajusta a gravidade para {g.Grav:0}x.");
		}
		return mudou;
	}

	/// <summary>A BATERIA DRENA, e as nanites recarregam. Devolve se alguma maquina desligou.</summary>
	private bool DrenarAsBaterias(long agora)
	{
		if (agora < _proximoDreno) return false;
		_proximoDreno = agora + MsDoDrenoDeGravidade;

		bool desligou = false;
		foreach (Obra o in _noChao)
		{
			if (o.Gravidade is not { } g) continue;

			if (g.Grav > 0)
			{
				g.Energia -= g.Grav * DrenoPorPontoDeGravidade;
				if (g.Energia > 0) continue;

				// A BATERIA ACABOU: a maquina desliga sozinha e o campo cai junto.
				g.Energia = 0;
				g.Grav = 0;
				desligou = true;
				AnunciarAVista(o, "a máquina de gravidade fica sem bateria e desliga.");
			}
			// AS NANITES SO TRABALHAM COM A MAQUINA DESLIGADA -- e a regra do original, e ela e o
			// que impede a maquina de virar energia infinita: pra recarregar, tem que parar.
			else if (g.Nanites > 0 && g.Energia < g.EnergiaMax
					 && _rng.Next(100) < g.Nanites)
			{
				g.Energia = g.EnergiaMax;
				AnunciarAVista(o, "as nanites recarregam a máquina de gravidade.");
			}
		}
		return desligou;
	}
}
