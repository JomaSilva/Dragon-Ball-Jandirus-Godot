using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Economia;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O ESTADO DE UM REGENERADOR. Ver `Obra.Regenerador`.
///
/// Os valores iniciais sao os do original (`Tier 1.5.dm:144-150`): eficiencia 1, uma carga de bateria,
/// sem nanites e sem tratar ferimentos. O interruptor e o unico campo que la nao existe -- ver
/// <see cref="Regenerador"/>.
/// </summary>
public sealed class RegeneradorDaObra
{
	/// <summary>O INTERRUPTOR -- o pedido do dono. Vai pro `mundo.json`: o tanque de alguem volta do reinicio como estava.</summary>
	public bool Ligado;

	/// <summary>O `efficiency`: escala a vida e o Ki de cada pulso, a bateria que ele gasta e a chance de tratar ferimento.</summary>
	public int Eficiencia = 1;

	public double Energia = 1, EnergiaMax = 1;

	/// <summary>O `NanoCore`: decimos de ponto percentual de chance, por pulso, de a bateria quase vazia se recarregar.</summary>
	public int Nanites;

	/// <summary>O `injuryheal`: comprado uma vez, o tanque passa a refazer membro perdido.</summary>
	public bool CuraFerimentos;

	/// <summary>O tanque do laboratorio de Vegeta, com tudo comprado -- ver <see cref="Regenerador.EficienciaDaCidade"/>.</summary>
	public static RegeneradorDaObra DaCidade() => new()
	{
		Eficiencia = Regenerador.EficienciaDaCidade,
		Energia = Regenerador.BateriaDaCidade,
		EnergiaMax = Regenerador.BateriaDaCidade,
		Nanites = Regenerador.NanitesDaCidade,
		CuraFerimentos = true,
	};
}

/// <summary>
/// O REGENERADOR -- `obj/items/Regenerator` (`Tech/Tier 1.5.dm:55-218`).
///
/// ============================ O QUE ESTAVA NO LUGAR DISTO ============================
/// Um achatado dentro do `TickDasMaquinasDeCura`: 3 de vida por SEGUNDO em cada membro de quem estivesse
/// a ate um tile, fora de combate, e um membro perdido de volta a cada 300 s seguidos. Sem bateria, sem
/// Ki, sem melhoria nenhuma, sempre ligado -- e sem nada na tela dizendo que curava. O dono (2026-10-09):
/// *"as maquinas de regeneraçao nao estao funcionando (veja no DM como elas funcionavam)"*.
///
/// Os NUMEROS moram em <see cref="Regenerador"/>, com as divergencias declaradas la. Aqui mora o que
/// acontece: o interruptor, o pulso de dois segundos, a bateria e as melhorias.
/// =====================================================================================
///
/// ============================ O QUE O PULSO DO ORIGINAL NAO FAZ, E PARECE FAZER ============================
/// Duas coisas do `Ticker()` estao escritas e nunca rodam -- medido lendo, e por isso nao foram portadas
/// como estao:
///
///   * **A EJECAO** (`:111-118`, *"Ejecting [M]"*): o laco procura o turf com `if(T == orange(1))`, que
///     compara um turf com uma LISTA e nunca e verdade; o `for` termina com `T` nulo e ninguem e
///     movido. La quem ja esta curado fica no tanque, e o tanque nao gasta nada com ele -- e aqui tambem;
///   * **O AVISO DE BATERIA** (`:106-110`, *"Battery has been completely drained"*): mora DENTRO do
///     `if(... Energy>=0.002 ...)`, onde `Energy<=0` nao acontece. La o tanque sem bateria para de curar
///     calado. Aqui ele avisa e DESLIGA -- a campanula some, que e como se ve de longe que parou.
/// ===========================================================================================================
/// </summary>
public sealed partial class GameServer
{
	private long _proximoPulsoDosTanques;

	private bool ComandoDeRegenerador(ServerPlayer pl, string cmd, string arg)
	{
		switch (cmd)
		{
			case "regen_ligar": LigarORegenerador(pl); return true;
			case "regen_info": InfoDoRegenerador(pl); return true;
			case "regen_up": MelhorarORegenerador(pl, arg); return true;
			case Interacoes.PrecosDoRegenerador: PrecosDoRegenerador(pl); return true;
			default: return false;
		}
	}

	/// <summary>
	/// O ESTADO DESTE TANQUE, nascendo na primeira vez que alguem pergunta -- pelo motivo escrito em
	/// `MaquinaPerto`: o que ja estava no `mundo.json` de antes volta do disco sem ele.
	///
	/// O QUE VEIO DO MAPA NASCE COMO O DA CIDADE: o unico regenerador que o mapa traz e o do laboratorio
	/// de Vegeta, e o construtor de la o poe *"turbinado"* (`VegetaCity.dm:103-115`). Ele nao vai pro
	/// disco (`Obra.DoMapa`), entao volta assim -- e desligado -- a cada reinicio.
	/// </summary>
	private static RegeneradorDaObra EstadoDoTanque(Obra o) =>
		o.Regenerador ??= o.DoMapa ? RegeneradorDaObra.DaCidade() : new RegeneradorDaObra();

	private RegeneradorDaObra? TanquePerto(ServerPlayer pl, out Obra? obra)
	{
		obra = ObraQueAceita(pl, "regen_ligar");
		return obra == null ? null : EstadoDoTanque(obra);
	}

	/// <summary>
	/// O ESTADO DO DESENHO DE UMA OBRA, pro pacote de construcoes: o do catalogo -- menos o tanque LIGADO,
	/// que ganha a campanula por cima. E o que mostra de longe qual tanque esta trabalhando.
	/// </summary>
	private static string EstadoDaArte(Obra o, Construcao? c) =>
		o is { Tipo: Regenerador.Tipo, Aparafusada: true, Regenerador.Ligado: true }
			? Regenerador.ArteLigado
			// (v0.2.1) A LUA ARTIFICIAL E O EMISSOR: apagados ate alguem acender (`icon_state="Off"` / `"On"`).
			: LuaArtificial.EhFonte(o.Tipo)
				? (o.Tech?.Num(LuaArtificial.ChaveAcesaAte) > 0 ? LuaArtificial.ArteAcesa : LuaArtificial.ArteApagada)
			: c?.Estado ?? "";

	/// <summary>
	/// (2026-10-21) A ARTE INTEIRA DE UMA OBRA pro pacote de construcoes -- a folha, o estado e o deslocamento. Sao os do catalogo, com o
	/// estado do <see cref="EstadoDaArte"/>, menos o mainframe de androides com o Bio-Android Lab ou o Neo Tuffle Lab, que vira o Cell Pod
	/// (o pedido do dono, em `ArteDoLaboratorio`). Uma conta so, pro desenho e pra bancada que o confere.
	/// </summary>
	private static (string Arte, string Estado, float PixelX, float PixelY) ArteDaObra(Obra o, Construcao? c) =>
		ArteDoLaboratorio.NoCellPod(o.Tipo, o.Lab)
			? (ArteDoLaboratorio.FolhaDoCellPod, ArteDoLaboratorio.EstadoDoCellPod,
			   (float)ArteDoLaboratorio.DeslocamentoXDoCellPod, (float)ArteDoLaboratorio.DeslocamentoYDoCellPod)
			: (c?.Arte ?? "", EstadoDaArte(o, c), (float)(c?.PixelX ?? 0), (float)(c?.PixelY ?? 0));

	// =====================================================================
	// O INTERRUPTOR
	// =====================================================================
	private void LigarORegenerador(ServerPlayer pl)
	{
		RegeneradorDaObra? r = TanquePerto(pl, out Obra? obra);
		if (r == null || obra == null) { Avisar(pl, "não há regenerador por perto."); return; }

		if (r.Ligado)
		{
			DesligarOTanque(obra, r);
			AnunciarAVista(obra, $"{pl.Name} desliga o regenerador.");
			return;
		}

		// `if(usable&&Bolted)` (`:69`): solto, ele e uma caixa no chao.
		if (!obra.Aparafusada) { Avisar(pl, "aparafuse o regenerador antes de ligá-lo."); return; }
		if (r.Energia < Regenerador.BateriaMinima)
		{
			Avisar(pl, $"o regenerador está sem bateria. Recarregar custa {Recurso.DoisPrecos(Regenerador.CustoDeRecarga(r.EnergiaMax))}, em \"Melhorar...\".");
			return;
		}

		r.Ligado = true;
		GravarMundo();
		AnunciarAVista(obra, $"{pl.Name} liga o regenerador: quem ficar no tile dele é curado.");
		MandarObras(obra.Zona);
	}

	private void DesligarOTanque(Obra obra, RegeneradorDaObra r)
	{
		r.Ligado = false;
		GravarMundo();
		MandarObras(obra.Zona);
	}

	// =====================================================================
	// O ESTADO E AS MELHORIAS -- `verb/Info` e `verb/Upgrade` (`:151-218`)
	// =====================================================================
	private void InfoDoRegenerador(ServerPlayer pl)
	{
		RegeneradorDaObra? r = TanquePerto(pl, out Obra? obra);
		if (r == null || obra == null) { Avisar(pl, "não há regenerador por perto."); return; }

		Avisar(pl, $"-- {NomeDaObra(obra)} --");
		Avisar(pl, !obra.Aparafusada ? "  solto: aparafuse pra poder ligar."
				 : r.Ligado ? "  LIGADO: cura quem estiver no mesmo tile dele."
				 : "  desligado: ligue pelo menu da tecla E.");
		Avisar(pl, $"  bateria: {r.Energia * 100:0.#} de {r.EnergiaMax * 100:0}");
		Avisar(pl, $"  a cada {Regenerador.SegundosDoPulso:0} s: +{Regenerador.CuraPorPulso(r.Eficiencia):0} de vida em cada membro ferido "
				   + $"e +{Regenerador.FracaoDeKiEVigorPorPulso(r.Eficiencia) * 100:0}% do Ki e do vigor (velocidade {r.Eficiencia})");
		Avisar(pl, r.CuraFerimentos
			? $"  ferimentos: trata -- {Regenerador.ChanceDeFerimento(r.Eficiencia):0}% por pulso de refazer um membro perdido"
			: "  ferimentos: não trata (membro perdido não volta neste tanque)");
		if (r.Nanites > 0) Avisar(pl, $"  nanites: {r.Nanites}");

		DizerOsPrecosDoRegenerador(pl, r);
	}

	/// <summary>
	/// O QUE O MENU PEDE AO ABRIR "Melhorar..." (<see cref="Interacoes.PerguntaAoAbrir"/>): so os precos e o
	/// saldo, sem o resto do estado -- e a pagina seguinte pergunta com que moeda pagar.
	/// </summary>
	private void PrecosDoRegenerador(ServerPlayer pl)
	{
		if (TanquePerto(pl, out _) is not { } r) { Avisar(pl, "não há regenerador por perto."); return; }
		DizerOsPrecosDoRegenerador(pl, r);
	}

	/// <summary>
	/// OS PRECOS SAO DO ESTADO ATUAL -- mudam a cada melhoria comprada, e por isso saem daqui e nao do catalogo
	/// de interacoes. NAS DUAS MOEDAS (dono, 2026-10-10): cada melhoria se paga em zeni ou, pelo dobro, em
	/// recursos; e o SALDO vai junto, que e com ele que se escolhe.
	///
	/// (2026-10-19) E O TETO DE QUEM PERGUNTA: ate onde a tecnologia dele leva cada melhoria (`TetoPorTecnologia`).
	/// </summary>
	private void DizerOsPrecosDoRegenerador(ServerPlayer pl, RegeneradorDaObra r)
	{
		double tech = pl.Ficha.techskill;
		int bateria = TetoPorTecnologia.BateriaDoRegenerador(tech);
		int velocidade = TetoPorTecnologia.VelocidadeDoRegenerador(tech);
		int nanites = TetoPorTecnologia.NanitesDoRegenerador(tech);
		static string AteOnde(int teto, string escrito) => teto == TetoPorTecnologia.SemTeto ? "" : $" (a sua tecnologia leva até {escrito})";

		Avisar(pl, "-- melhorias (em zeni, ou o dobro em recursos) --");
		Avisar(pl, $"  recarregar a bateria: {Recurso.DoisPrecos(Regenerador.CustoDeRecarga(r.EnergiaMax))}");
		Avisar(pl, r.EnergiaMax >= bateria ? $"  bateria maior: no teto da sua tecnologia ({(double)bateria * 100:N0})"
			: $"  bateria maior: {Recurso.DoisPrecos(Regenerador.CustoDeBateria(r.EnergiaMax))}{AteOnde(bateria, $"{(double)bateria * 100:N0}")}");
		Avisar(pl, r.Eficiencia >= velocidade ? $"  velocidade de cura: no teto da sua tecnologia ({velocidade})"
			: $"  velocidade de cura: {Recurso.DoisPrecos(Regenerador.CustoDeVelocidade(r.Eficiencia))}{AteOnde(velocidade, $"{velocidade}")}");
		Avisar(pl, r.CuraFerimentos ? "  tratar ferimentos: já comprada" : $"  tratar ferimentos: {Recurso.DoisPrecos(Regenerador.CustoDeFerimentos)}");
		Avisar(pl, r.Nanites >= nanites ? $"  nanites: no teto da sua tecnologia ({nanites})"
			: $"  nanites: {Recurso.DoisPrecos(Regenerador.CustoDeNanites(r.Nanites))} (pede {Regenerador.TechDosNanites:0} de tecnologia){AteOnde(nanites, $"{nanites}")}");
		Avisar(pl, $"  você tem {CarteiraEscrita(pl)}.");
	}

	/// <summary>
	/// O `verb/Upgrade` (`Tier 1.5.dm:159-218`). O pedido e a melhoria e, se for o caso, a moeda no fim
	/// ("bateria", "bateria recursos" -- ver <see cref="Recurso.LerPedidoPago"/>): em zeni o preco de sempre, em
	/// recursos o dobro. Quem cobra e o contrato (<see cref="CobrarOuExplicar"/>).
	/// </summary>
	private void MelhorarORegenerador(ServerPlayer pl, string pedido)
	{
		RegeneradorDaObra? r = TanquePerto(pl, out Obra? obra);
		if (r == null || obra == null) { Avisar(pl, "não há regenerador por perto."); return; }

		(string qual, Moeda moeda) = Recurso.LerPedidoPago(pedido);
		double custo;
		string feito;
		// (2026-10-19) O TETO DE QUEM PAGA (`TetoPorTecnologia`): a recusa vem antes da cobranca, e o tanque que ja passou dele
		// continua como esta.
		double tech = pl.Ficha.techskill;

		switch (qual)
		{
			case "recarga":
				// O DM deixa pagar por uma bateria cheia (o botao so confere o zeni, `:167`). Aqui a recusa vem
				// antes da cobranca: vender uma carga que nao cabe e tirar zeni por nada.
				if (r.Energia >= r.EnergiaMax) { Avisar(pl, "a bateria já está cheia."); return; }
				custo = Regenerador.CustoDeRecarga(r.EnergiaMax);
				if (!CobrarOuExplicar(pl, custo, moeda, "recarregar a bateria")) return;
				r.Energia = r.EnergiaMax;
				feito = "bateria recarregada";
				break;

			case "bateria":
				int bateria = TetoPorTecnologia.BateriaDoRegenerador(tech);
				if (r.EnergiaMax >= bateria)
				{ Avisar(pl, TetoPorTecnologia.Recusa("a bateria do regenerador", tech, $"{(double)bateria * 100:N0} de carga", proporcional: false)); return; }
				custo = Regenerador.CustoDeBateria(r.EnergiaMax);
				if (!CobrarOuExplicar(pl, custo, moeda, "a bateria maior")) return;
				r.EnergiaMax += 1;
				r.Energia = r.EnergiaMax;   // a melhoria ENCHE (`:191`)
				feito = $"a bateria guarda {r.EnergiaMax * 100:0} agora, e está cheia";
				break;

			case "velocidade":
				int velocidade = TetoPorTecnologia.VelocidadeDoRegenerador(tech);
				if (r.Eficiencia >= velocidade)
				{ Avisar(pl, TetoPorTecnologia.Recusa("a velocidade de cura", tech, $"velocidade {velocidade}", proporcional: false)); return; }
				custo = Regenerador.CustoDeVelocidade(r.Eficiencia);
				if (!CobrarOuExplicar(pl, custo, moeda, "a velocidade de cura")) return;
				r.Eficiencia += 1;
				feito = $"cada pulso dá +{Regenerador.CuraPorPulso(r.Eficiencia):0} de vida e +{Regenerador.FracaoDeKiEVigorPorPulso(r.Eficiencia) * 100:0}% do Ki e do vigor";
				break;

			case "ferimentos":
				if (r.CuraFerimentos) { Avisar(pl, "este regenerador já trata ferimentos."); return; }
				custo = Regenerador.CustoDeFerimentos;
				if (!CobrarOuExplicar(pl, custo, moeda, "tratar ferimentos")) return;
				r.CuraFerimentos = true;
				feito = "o tanque passa a refazer membro perdido";
				break;

			case "nanites":
				if (pl.Ficha.techskill < Regenerador.TechDosNanites)
				{
					Avisar(pl, $"nanites pedem {Regenerador.TechDosNanites:0} de tecnologia -- você tem {pl.Ficha.techskill:0}.");
					return;
				}
				int nanites = TetoPorTecnologia.NanitesDoRegenerador(tech);
				if (r.Nanites >= nanites) { Avisar(pl, TetoPorTecnologia.Recusa("a regeneração de nanites", tech, $"{nanites}", proporcional: false)); return; }
				custo = Regenerador.CustoDeNanites(r.Nanites);
				if (!CobrarOuExplicar(pl, custo, moeda, "a regeneração de nanites")) return;
				r.Nanites += 1;
				feito = $"regeneração de nanites em {r.Nanites}";
				break;

			default: Avisar(pl, "essa melhoria não existe."); return;
		}

		GravarMundo();
		Avisar(pl, $"você melhora o regenerador por {Recurso.PrecoEscrito(custo, moeda)}: {feito}. Restam {CarteiraEscrita(pl)}.");
		// O TANQUE DO MAPA NAO VAI PRO DISCO (`Obra.DoMapa`): la tambem a cidade e refeita a cada boot.
		if (obra.DoMapa) Avisar(pl, "(este tanque faz parte do lugar: a melhoria vale até o servidor reiniciar.)");
		// O MENU DE QUEM ESTA NA ZONA FICA SABENDO: a melhoria de uma vez so sai da lista (`MelhoriasFeitas`).
		MandarObras(obra.Zona);
	}

	// =====================================================================
	// O PULSO -- `proc/Ticker` (`:65-141`)
	// =====================================================================
	/// <summary>
	/// UM RELOGIO SO PRO MUNDO, e nao um `spawn` por tanque -- o mesmo desenho da bateria da maquina de
	/// gravidade e das macieiras. Os tanques pulsam juntos, de dois em dois segundos.
	/// </summary>
	private void TickDosRegeneradores()
	{
		long agora = NowMs();
		if (agora < _proximoPulsoDosTanques) return;
		_proximoPulsoDosTanques = agora + (long)(Regenerador.SegundosDoPulso * 1000);
		PulsoDosTanques();
	}

	/// <summary>
	/// UM PULSO DE TODOS OS TANQUES. Separado do relogio pra bancada poder dar pulsos contados
	/// (`--maquinasteste`): o que se mede e a conta de UM pulso, e esperar dois segundos de parede por
	/// pulso mediria o relogio.
	/// </summary>
	private void PulsoDosTanques()
	{
		long agora = NowMs();
		foreach (Obra o in _noChao)
		{
			if (o.Tipo != Regenerador.Tipo) continue;
			RegeneradorDaObra r = EstadoDoTanque(o);

			// SOLTARAM O PARAFUSO COM ELE LIGADO: o desenho ja caiu na hora (`EstadoDaArte`), e o interruptor
			// cai aqui -- senao aparafusar de novo o religaria sozinho.
			if (!o.Aparafusada)
			{
				if (r.Ligado) { r.Ligado = false; GravarMundo(); }
				continue;
			}

			// OS NANITES NAO OLHAM O INTERRUPTOR (`:124-126`): la eles trabalham em todo tanque
			// aparafusado, e aqui sao o que faz uma bateria que acabou voltar sem ninguem pagar.
			if (r.Nanites > 0 && r.Energia < r.EnergiaMax * Regenerador.BateriaDosNanites
				&& _rng.NextDouble() * 100 < Regenerador.ChanceDeNanites(r.Nanites))
			{
				r.Energia = r.EnergiaMax;
				AnunciarAVista(o, "os nanites recarregam a bateria do regenerador.");
			}

			if (!r.Ligado && !Regenerador.DesligadoCuraDeTeste) continue;
			if (!_zones.TryGetValue(o.Zona.Hash, out List<ServerPlayer>? naZona)) continue;

			foreach (ServerPlayer pl in naZona)
			{
				if (r.Energia < Regenerador.BateriaMinima) break;   // `Energy>=0.002` (`:75`)
				if (!NoTileDoTanque(o, pl)) continue;

				// `M.Player` (`:75`): gente. O cadaver e um corpo neste port, e cadaver nao se cura.
				if (!EhPessoa(pl) || EhCadaver(pl) || pl.Combate is not { } c) continue;
				if (!PrecisaDoTanque(r, c, pl.Ficha)) continue;

				// O ANDROIDE DE ABSORCAO NAO SE CURA NO TANQUE (dono, 2026-10-13): *"androides q absorvem energia nao curam
				// na maquina de regeneração, androides de energia infinita q custa 2 milhoes regeneram normalmente"*. O
				// corpo dele se conserta com os kits (`Jandirus.Core.Tech.KitsDeAndroide`). O tanque nem gasta bateria
				// com ele, e explica o porque -- de tempos em tempos, e nao a cada pulso.
				if (pl.Ficha.AndroideAbsorcao && !KitsDeAndroide.AbsorcaoCuraNoTanqueDeTeste)
				{
					AvisarQueOTanqueNaoCura(pl, agora);
					continue;
				}

				// `M.dir=SOUTH` e `M.inregen = 1` (`:77-78`): o paciente fica de frente, e nao golpeia.
				if (pl.Facing != Facing.South) CravarOlhar(pl, Facing.South);
				pl.NoTanqueAte = agora + (long)(Regenerador.SegundosSemGolpear * 1000);

				c.Corpo.CurarComFocoNosVitais(Regenerador.CuraPorPulso(r.Eficiencia));
				// (2026-10-26) O KI E O VIGOR, em fracao do maximo e no ritmo da vida (o dono) -- ver `Regenerador.FracaoDeKiEVigorPorPulso`. O
				// numero solto do DM fica de piso; o Ki comprimido ACIMA do teto nao e mexido.
				double fracao = Regenerador.SoAVidaDeTeste ? 0 : Regenerador.FracaoDeKiEVigorPorPulso(r.Eficiencia);
				if (pl.Ficha.Ki < pl.Ficha.MaxKi)
					pl.Ficha.Ki = Math.Min(pl.Ficha.Ki + Math.Max(Regenerador.KiPorPulso(r.Eficiencia), pl.Ficha.MaxKi * fracao), pl.Ficha.MaxKi);
				if (pl.Ficha.stamina < pl.Ficha.maxstamina)
					pl.Ficha.stamina = Math.Min(pl.Ficha.stamina + pl.Ficha.maxstamina * fracao, pl.Ficha.maxstamina);
				r.Energia = Math.Max(r.Energia - Regenerador.BateriaPorPulso(r.Eficiencia), 0);

				if (r.CuraFerimentos && _rng.NextDouble() * 100 < Regenerador.ChanceDeFerimento(r.Eficiencia))
					TratarUmFerimento(pl, c, r);

				c.SincronizarVida();
			}

			if (r.Ligado && r.Energia < Regenerador.BateriaMinima)
			{
				DesligarOTanque(o, r);
				AnunciarAVista(o, "a bateria do regenerador acabou: ele desliga.");
			}
		}
	}

	/// <summary>
	/// ESTE CORPO ESTA NO TILE DO TANQUE? `view(0,src)` (`:71`) -- o proprio turf, e so ele. O dono:
	/// *"curando todos q estiverem no mesmo tile da maquina"*.
	///
	/// A CELULA E A DOS PES, pelas duas pontas: a da obra (`CatalogoDeObras.Celula`) e a que o servidor
	/// trata como "onde ela esta", e a mesma conta sobre a posicao do corpo diz em que tile ele pisa.
	/// </summary>
	private static bool NoTileDoTanque(Obra tanque, ServerPlayer pl)
	{
		(int tx, int ty) = CatalogoDeObras.Celula(tanque.X, tanque.Y);
		(int px, int py) = CatalogoDeObras.Celula(pl.Pos.X, pl.Pos.Y);
		int folga = Regenerador.VizinhoContaDeTeste ? 1 : 0;
		return Math.Abs(px - tx) <= folga && Math.Abs(py - ty) <= folga;
	}

	/// <summary>
	/// ESTE TANQUE TEM O QUE FAZER POR ESTE CORPO? O `M.HP&lt;100` do original (`:75`).
	///
	/// A DIFERENCA ESTA NO MEMBRO PERDIDO: la o decepado conta como zero na vida do corpo (`Injuries.dm:160`,
	/// *"we want lopped limbs to contribute to health as well"*), entao quem perdeu um braco esta sempre
	/// abaixo de 100 e o tanque o segura pra sempre -- gastando bateria mesmo sem a melhoria que refaz o
	/// braco. Aqui o decepado sai da media (`Body.Vida`), e a pergunta e feita por extenso: o tanque so
	/// trabalha por um membro perdido se souber refaze-lo.
	/// </summary>
	private static bool PrecisaDoTanque(RegeneradorDaObra r, CombatState c, Jandirus.Core.Stats.Fighter f)
	{
		// (2026-10-26) Ki ou vigor faltando tambem fazem o tanque agir -- ver `Regenerador.FracaoDeKiEVigorPorPulso`
		if (!Regenerador.SoAVidaDeTeste && (f.Ki < f.MaxKi || f.stamina < f.maxstamina)) return true;
		bool falta = false;
		foreach (BodyPart p in c.Corpo.Partes)
		{
			if (p.Decepado) falta = true;
			else if (p.Vida < p.VidaMax) return true;
		}
		return falta && r.CuraFerimentos;
	}

	/// <summary>
	/// O SORTEIO DO `injuryheal` (`:91-105`): UM membro dos que estao abaixo de 99,1% ou decepados. O
	/// decepado volta (`RegrowLimb`); o ferido ganha `1*efficiency` de vida.
	///
	/// A MAO NAO ENTRA NO SORTEIO ENQUANTO O BRACO ESTA CAIDO: ela volta junto com ele (`Body.Regenerar`),
	/// e sortea-la seria gastar a vez num membro que o `RegrowLimb` recusa. (No DM a vez E gasta -- e a mao
	/// volta solta, porque o `parentlimb` de la nunca e atribuido. Ver `Body.Regenerar`.)
	/// </summary>
	private void TratarUmFerimento(ServerPlayer pl, CombatState c, RegeneradorDaObra r)
	{
		// SORTEIO SEM LISTA: o k-esimo candidato fica com a vez com chance 1/k, e ao fim cada um teve a mesma.
		BodyPart? escolhido = null;
		int vistos = 0;
		foreach (BodyPart p in c.Corpo.Partes)
		{
			// (o membro PERDIDO DE VEZ fica de fora: o rabo que nao volta nao volta nem aqui -- `BodyPart.Perdido`)
			bool candidato = p.Decepado ? !p.Perdido && !Caiu(c.Corpo, p.Dono) : p.Vida < p.VidaMax * Regenerador.FerimentoInteiro;
			if (candidato && _rng.Next(++vistos) == 0) escolhido = p;
		}
		if (escolhido == null) return;

		if (!escolhido.Decepado)
		{
			escolhido.Vida = Math.Min(escolhido.Vida + r.Eficiencia, escolhido.VidaMax);
			return;
		}

		if (!c.Corpo.Regenerar(escolhido)) return;
		AjustarGanhoDoRabo(pl);
		pl.CorpoEnviado = "";
		Avisar(pl, $"o tanque refaz o seu {escolhido.Nome}.");
		GD.Print($"[server] o regenerador devolveu o {escolhido.Nome} de {pl.Name}");
	}

	/// <summary>
	/// `to_chat(view(src), ...)`: quem esta VENDO a maquina ouve o que ela diz -- e nao o planeta inteiro.
	/// Vale pro regenerador e pra maquina de gravidade.
	/// </summary>
	private void AnunciarAVista(Obra obra, string texto)
	{
		if (!_zones.TryGetValue(obra.Zona.Hash, out List<ServerPlayer>? naZona)) return;
		foreach (ServerPlayer p in naZona)
			if (Math.Abs(p.Pos.X - obra.X) <= RaioDaVista && Math.Abs(p.Pos.Y - obra.Y) <= RaioDaVista) Avisar(p, texto);
	}
}
