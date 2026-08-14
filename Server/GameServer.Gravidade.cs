using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O ESTADO DE UMA MAQUINA DE GRAVIDADE. Ver `Obra.Gravidade`.
///
/// Os valores iniciais sao os do original (`Gravity.dm:198-210`): a maquina sai da fabrica com teto
/// de 10x, sem alcance, com uma unica carga de bateria e desligada.
/// </summary>
public sealed class GravidadeDaObra
{
	/// <summary>A gravidade LIGADA agora. Zero = desligada.</summary>
	public double Grav;

	/// <summary>O teto DESTA maquina. Sobe 20% por melhoria de forca.</summary>
	public double Max = 10;

	/// <summary>Raio do campo, em tiles. O original para em 10.</summary>
	public int Range;

	/// <summary>A estabilizacao, comprada uma vez so.</summary>
	public bool Estavel;

	public double Energia = 1, EnergiaMax = 1;

	/// <summary>Nivel de regeneracao por nanites: a chance, em pontos percentuais, de recarregar.</summary>
	public int Nanites;
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

	private void DefinirGravidade(ServerPlayer pl, string arg)
	{
		GravidadeDaObra? g = MaquinaPerto(pl, out Obra? obra);
		if (g == null || obra == null) { Avisar(pl, "não há máquina de gravidade por perto."); return; }

		if (!obra.Aparafusada) { Avisar(pl, "aparafuse a máquina antes de usá-la."); return; }
		if (g.Energia <= 0) { Avisar(pl, "a máquina está sem bateria."); return; }

		if (!double.TryParse(arg, out double pedido)) { Avisar(pl, "número inválido."); return; }

		// TRES CORTES, na ordem do original: o teto da MAQUINA, o piso, e o teto do MUNDO. O do
		// mundo vem por ultimo porque ele vale mesmo pra uma maquina melhorada alem dele -- a
		// progressao de forca passa de 500 por volta da vigesima segunda melhoria.
		double antes = g.Grav;
		g.Grav = Math.Clamp(pedido, 0, Math.Min(g.Max, Interacoes.TetoDeGravidade));

		if (g.Grav < pedido)
			Avisar(pl, $"esta máquina não passa de {Math.Min(g.Max, Interacoes.TetoDeGravidade):0}x.");

		AnunciarNaZona(obra, g.Grav <= 0
			? $"{pl.Name} desliga a máquina de gravidade."
			: $"{pl.Name} ajusta a gravidade para {g.Grav:0}x.");

		if (Math.Abs(antes - g.Grav) > 0.001) AplicarCampos(obra.Zona);
	}

	private void InfoDaGravidade(ServerPlayer pl)
	{
		GravidadeDaObra? g = MaquinaPerto(pl, out Obra? obra);
		if (g == null || obra == null) { Avisar(pl, "não há máquina de gravidade por perto."); return; }

		Avisar(pl, $"-- {NomeDaObra(obra)} --");
		Avisar(pl, $"  gravidade agora: {g.Grav:0}x (teto desta máquina: {g.Max:0}x)");
		Avisar(pl, $"  bateria: {g.Energia * 100:0} de {g.EnergiaMax * 100:0}");
		Avisar(pl, $"  alcance: {g.Range} tile(s)");
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
				feito = $"o campo cobre {g.Range} tile(s)";
				AplicarCampos(obra.Zona);
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

	private void AnunciarNaZona(Obra obra, string texto)
	{
		foreach (ServerPlayer p in _players.Values)
			if (p.Zone.Equals(obra.Zona)) Avisar(p, texto);
	}

	// =====================================================================
	// O CAMPO
	// =====================================================================
	/// <summary>
	/// QUEM ESTA DENTRO DE QUAL CAMPO, e com quanta gravidade.
	///
	/// ============================ OS CAMPOS SOMAM ============================
	/// Duas maquinas sobrepostas somam a gravidade delas (`fieldgrav += GM.Grav` no DM), e nao vence
	/// a maior. E o que permite montar uma sala de treino empilhando maquinas baratas em vez de
	/// melhorar uma cara -- e o original deixa isso de pe de proposito.
	/// =========================================================================
	/// </summary>
	private void AplicarCampos(ZoneKey zona)
	{
		foreach (ServerPlayer pl in _players.Values)
		{
			if (!pl.Zone.Equals(zona)) continue;

			double campo = 0;
			foreach (Obra o in _noChao)
			{
				if (!o.Zona.Equals(zona) || o.Gravidade is not { Grav: > 0, Energia: > 0 } g) continue;

				// O RAIO E EM TILES a partir do centro da maquina, e a caixa e quadrada -- e o
				// `bounding_box` do original, que tambem e retangular.
				float raio = (g.Range + 1) * ZoneCollision.TileSize;
				if (Math.Abs(o.X - pl.Pos.X) > raio || Math.Abs(o.Y - pl.Pos.Y) > raio) continue;

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
	}

	/// <summary>
	/// A BATERIA DRENA, e as nanites recarregam.
	///
	/// UM RELOGIO SO PRO MUNDO, e nao um por maquina: o original tinha um laco por objeto, o que
	/// com dezenas de maquinas no chao seriam dezenas de laços fazendo a mesma conta.
	/// </summary>
	private void TickDaGravidade()
	{
		long agora = NowMs();
		if (agora < _proximoDreno) return;
		_proximoDreno = agora + MsDoDrenoDeGravidade;

		var zonasMexidas = new HashSet<ZoneKey>();

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
				zonasMexidas.Add(o.Zona);
				AnunciarNaZona(o, "a máquina de gravidade fica sem bateria e desliga.");
			}
			// AS NANITES SO TRABALHAM COM A MAQUINA DESLIGADA -- e a regra do original, e ela e o
			// que impede a maquina de virar energia infinita: pra recarregar, tem que parar.
			else if (g.Nanites > 0 && g.Energia < g.EnergiaMax
					 && _rng.Next(100) < g.Nanites)
			{
				g.Energia = g.EnergiaMax;
				AnunciarNaZona(o, "as nanites recarregam a máquina de gravidade.");
			}
		}

		foreach (ZoneKey z in zonasMexidas) AplicarCampos(z);
	}
}
