using System.Text.Json;
using Jandirus.Core.Items;
using Jandirus.Core.Tech;

namespace Jandirus.Server;

/// <summary>
/// O QUE A GAVETA DE UMA MAQUINA RECOLHIDA GUARDA: o estado que morava na obra (ou na nave), ate ela ser assentada de novo.
/// Mora no cofre de tech (`CofreDeTech.Maquinas`, o `tech-itens.json`), e o item da mochila leva so o numero -- ver
/// `MaquinaGuardada`, no Core.
/// </summary>
public sealed class MaquinaNaGaveta
{
	/// <summary>O tipo do catalogo: a gaveta so devolve o estado a uma maquina do mesmo tipo.</summary>
	public string Tipo = "";

	public GravidadeDaObra? Gravidade;
	public RegeneradorDaObra? Regenerador;
	public EstadoDeTech? Tech;

	/// <summary>A armadura maxima que ela tinha -- a blindagem do super computador, o reforco do `Wall_Upgrader`.</summary>
	public double ArmaduraMax;

	/// <summary>A velocidade comprada, quando e uma nave (zero nas obras).</summary>
	public int Velocidade;
}

/// <summary>
/// ============================ A MAQUINA RECOLHIDA LEVA AS MELHORIAS (dono, 2026-10-19) ============================
/// O "Pegar" guardava na mochila o id cru do catalogo, e o estado da maquina (as melhorias, a bateria, os ajustes) sumia
/// com a obra -- re-instalada, ela voltava de fabrica e o dono pagava tudo de novo. A causa e o desenho estao no cabecalho
/// de `MaquinaGuardada` (Core). Aqui moram as duas pontas:
///
///   * RECOLHER (<see cref="IdAoRecolher"/>, <see cref="IdDaNaveAoRecolher"/>): a maquina mexida abre uma gaveta com o
///     estado dela, e o item da mochila ganha o numero dela no id;
///   * ASSENTAR (<see cref="DevolverAsMelhorias"/>, <see cref="DevolverAVelocidade"/>): o item com gaveta devolve o estado
///     a maquina nova, e a gaveta se esvazia -- o mesmo item nao devolve duas vezes.
///
/// O QUE VAI NA GAVETA: o estado inteiro de cada sistema (gravidade, regenerador, o saco da v0.2.1), e nao so as melhorias
/// -- a bateria pela metade continua pela metade, e a frequencia do comunicador continua a mesma. A armadura nunca desce
/// (a obra nasce com a de quem a assenta, e fica com a maior das duas). O que NAO vai: a Drill, que tem o caminho mais
/// antigo dela (o nivel na ficha de quem recolhe, `AoRecolherADrill`), e da nave so a velocidade -- o tanque continua
/// cheio pra ir pra mochila, e o casco nasce de novo de quem a assenta, como antes.
/// ===================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>
	/// DEFEITO INJETADO (bancada `--melhoriasteste`): a maquina recolhida volta pra mochila com o id cru e o estado fica pra
	/// tras -- o jogo de antes de 2026-10-19. Lido pela propria linha que abre a gaveta, em <see cref="AbrirAGaveta"/>. Falso
	/// em jogo, sempre.
	/// </summary>
	internal bool MaquinaRecolhidaPerdeAsMelhoriasDeTeste;

	private static readonly JsonSerializerOptions JsonDoEstadoDaMaquina = new() { IncludeFields = true };

	/// <summary>
	/// ESTE ESTADO E O DE FABRICA? Comparado pelo que vai pro disco (o JSON): um campo de estado que alguem acrescentar depois
	/// conta sozinho, sem esta linha precisar saber dele.
	/// </summary>
	private static bool DeFabrica<T>(T? estado, T daFabrica) where T : class =>
		estado == null
		|| JsonSerializer.Serialize(estado, JsonDoEstadoDaMaquina) == JsonSerializer.Serialize(daFabrica, JsonDoEstadoDaMaquina);

	/// <summary>
	/// O ID QUE A MAQUINA RECOLHIDA GANHA NA MOCHILA: o cru, se ela esta como saiu da fabrica; senao, o de uma gaveta nova com o
	/// estado dela (`Gravity|c12|500x`). Quem chama ja passou por todas as guardas do `PegarObra`: daqui a obra sai do chao.
	/// </summary>
	private string IdAoRecolher(Obra o)
	{
		// A DRILL TEM O CAMINHO DELA, mais antigo: o nivel fica na ficha de quem a recolhe (`AoRecolherADrill`).
		if (o.Tipo == Drill.Tipo) return o.Tipo;

		// O INTERRUPTOR NAO E MELHORIA: o tanque sai da tomada ao ser recolhido, e volta desligado.
		if (o.Regenerador != null) o.Regenerador.Ligado = false;

		GravidadeDaObra? g = DeFabrica(o.Gravidade, new GravidadeDaObra()) ? null : o.Gravidade;
		RegeneradorDaObra? r = DeFabrica(o.Regenerador, new RegeneradorDaObra()) ? null : o.Regenerador;
		EstadoDeTech? t = o.Tech is { } e && (e.N.Count > 0 || e.S.Count > 0) ? e : null;
		// O QUE NAO SE INSTALA DA MOCHILA NAO GANHA GAVETA: o item com estado so existe pra ser assentado de novo
		// (`MaquinaGuardada.Ficha` pede a acao de instalar), e uma gaveta que nenhum item alcanca seria estado perdido igual.
		if ((g == null && r == null && t == null) || !CatalogoDeItens.PodeAssentarNoChao(o.Tipo)) return o.Tipo;

		return AbrirAGaveta(new MaquinaNaGaveta
		{
			Tipo = o.Tipo,
			Gravidade = g,
			Regenerador = r,
			Tech = t,
			ArmaduraMax = o.ArmaduraMax,
		}, RotuloDa(o));
	}

	/// <summary>A nave recolhida: a velocidade comprada vai numa gaveta, como o estado de uma obra.</summary>
	private string IdDaNaveAoRecolher(Nave n) =>
		n.Velocidade <= Naves.VelocidadeInicial
			? n.Tipo
			: AbrirAGaveta(new MaquinaNaGaveta { Tipo = n.Tipo, Velocidade = n.Velocidade }, $"{n.Velocidade}x");

	/// <summary>Guarda o estado numa gaveta nova do cofre e devolve o id do item que aponta pra ela (o cru, com o defeito ligado).</summary>
	private string AbrirAGaveta(MaquinaNaGaveta m, string rotulo)
	{
		if (MaquinaRecolhidaPerdeAsMelhoriasDeTeste) return m.Tipo;
		int gaveta = _cofreDeTech.Proximo++;
		_cofreDeTech.Maquinas[gaveta] = m;
		GravarOCofreDeTech();
		return MaquinaGuardada.Id(m.Tipo, gaveta, rotulo);
	}

	/// <summary>
	/// O ROTULO QUE A MOCHILA MOSTRA ao lado do nome: o numero que mais separa uma maquina da outra. O teto de poder do
	/// simulador NAO aparece -- e numero de poder, e esse so se le com medidor (`PoderEscrito`).
	/// </summary>
	private static string RotuloDa(Obra o) => o switch
	{
		{ Gravidade: { } g } => $"{g.Max:0}x",
		{ Regenerador: { } r } => $"velocidade {r.Eficiencia}",
		{ Tipo: EstacaoDeRecarga.Tipo, Tech: { } e } => $"nível {e.Num(EstacaoDeRecarga.ChaveNivel, 1):0}",
		_ => MaquinaGuardada.RotuloComum,
	};

	/// <summary>
	/// A GAVETA DESTE ITEM, se ela existe e e de uma maquina deste tipo -- e ela sai do cofre: o mesmo item nao devolve o
	/// estado duas vezes (nem um id copiado, se um dia houver um).
	/// </summary>
	private MaquinaNaGaveta? EsvaziarAGaveta(string idDoItem, string tipo)
	{
		int gaveta = MaquinaGuardada.Gaveta(idDoItem);
		if (gaveta <= 0 || !_cofreDeTech.Maquinas.TryGetValue(gaveta, out MaquinaNaGaveta? m)
			|| !string.Equals(m.Tipo, tipo, StringComparison.OrdinalIgnoreCase)) return null;
		_cofreDeTech.Maquinas.Remove(gaveta);
		GravarOCofreDeTech();
		return m;
	}

	/// <summary>
	/// A OBRA RECEM-ASSENTADA DE UM ITEM COM GAVETA volta com o estado de antes. Devolve se voltou algo. Chamado pelo
	/// `Posicionar` antes de a obra entrar no chao.
	/// </summary>
	private bool DevolverAsMelhorias(Obra obra, string idDoItem)
	{
		if (EsvaziarAGaveta(idDoItem, obra.Tipo) is not { } m) return false;
		obra.Gravidade = m.Gravidade;
		obra.Regenerador = m.Regenerador;
		obra.Tech = m.Tech;
		if (obra.Gravidade != null) ApararAoTetoDoMundo(obra.Gravidade);
		// A ARMADURA NAO DESCE: a obra nasceu com a de quem a assenta, e fica com a maior das duas.
		if (m.ArmaduraMax > obra.ArmaduraMax) obra.ArmaduraMax = obra.Armadura = m.ArmaduraMax;
		return true;
	}

	/// <summary>A nave recem-assentada de um item com gaveta volta com a velocidade de antes. Devolve se voltou.</summary>
	private bool DevolverAVelocidade(Nave nave, string idDoItem)
	{
		if (EsvaziarAGaveta(idDoItem, nave.Tipo) is not { } m || m.Velocidade <= nave.Velocidade) return false;
		nave.Velocidade = Math.Min(m.Velocidade, Naves.VelocidadeMaxima);
		return true;
	}
}
