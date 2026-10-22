using Jandirus.Core.Npc;

namespace Jandirus.Server;

/// <summary>
/// A ETAPA DA LEMBRANCA (2026-10-21). O chefe na mente nasce na forma que voce escolheu, ja
/// transformado e sem a cinematica, e so sobe por dano ate a ultima forma que voce viu. A sua
/// mente nao imagina uma forma que voce nunca viu. A escada do meio, entre a escolhida e essa
/// ultima, continua a de sempre -- o teto e o maximo visto, nao um pulo.
/// </summary>
public partial class GameServer
{
	/// <summary>DEFEITO INJETADO (bancada --mentechefeteste): ver o chefe nao grava a etapa. Falso em jogo, sempre.</summary>
	internal static bool EtapaVistaNaoGravaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --mentechefeteste): varias etapas vistas nascem sem a lista, no jogo de antes. Falso em jogo, sempre.</summary>
	internal static bool VariasEtapasNascemSemPerguntarDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --mentechefeteste): a etapa pedida e ignorada e a lembranca nasce na primeira. Falso em jogo, sempre.</summary>
	internal static bool NasceSemAEtapaEscolhidaDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --mentechefeteste): a lembranca sobe alem da ultima forma vista. Falso em jogo, sempre.</summary>
	internal static bool LembrancaSobeAlemDoVistoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --mentechefeteste): os dois Freeza voltam a se chamar so "Freeza" e um botao come o outro. Falso em jogo, sempre.</summary>
	internal static bool NomeDeChefeRepetidoDeTeste;

	/// <summary>DEFEITO INJETADO (bancada --mentechefeteste): o save antigo nao ganha a etapa 1. Falso em jogo, sempre.</summary>
	internal static bool SaveAntigoSemEtapaDeTeste;

	/// <summary>O teto de cada lembranca convocada, pelo id do corpo. Chefe de saga nao entra aqui.</summary>
	private static readonly Dictionary<int, int> _tetoDaLembranca = [];

	/// <summary>
	/// Grava a etapa em que o chefe esta agora. Devolve true se essa etapa ainda nao estava na memoria.
	/// A primeira vez que se ve o chefe e uma etapa nova de um chefe ja visto passam por aqui.
	/// </summary>
	private bool AnotarEtapaVista(ServerPlayer o, PapelDeNpc papel)
	{
		if (EtapaVistaNaoGravaDeTeste) return false;
		if (papel.Estagio < 0 || papel.Molde.Id.Length == 0) return false;
		if (!o.EtapasDeChefe.TryGetValue(papel.Molde.Id, out HashSet<int>? set))
		{
			set = [];
			o.EtapasDeChefe[papel.Molde.Id] = set;
		}
		return set.Add(papel.Estagio);
	}

	/// <summary>Mais de uma etapa vista, e o botao ainda nao escolheu: a lista aparece. Com uma so, nasce direto.</summary>
	private static bool DevePerguntarAEtapa(int? escolhida, int quantas) =>
		escolhida == null && quantas > 1 && !VariasEtapasNascemSemPerguntarDeTeste;

	/// <summary>O indice em que a lembranca nasce. O defeito devolve a primeira, como o jogo de antes.</summary>
	private static int EtapaEmQueNasce(int indice) =>
		NasceSemAEtapaEscolhidaDeTeste ? 0 : indice;

	/// <summary>
	/// A carga do personagem. O conjunto de chefes ja tem de estar no corpo: o save antigo completa
	/// a etapa 1 a partir dele.
	/// </summary>
	private static void AplicarEtapasDoSave(ServerPlayer pl, List<string>? gravadas)
	{
		pl.EtapasDeChefe = EtapasDaMente.Ler(gravadas);
		if (!SaveAntigoSemEtapaDeTeste)
			EtapasDaMente.CompletarSaveAntigo(pl.ChefesVistos, pl.EtapasDeChefe);
	}

	/// <summary>
	/// O nome do botao "Enfrentar na mente". Os dois Freeza do arquivo se chamam "Freeza", e o cliente
	/// descarta verbo de nome repetido: quem viu os dois ficava so com o de Namek. O rotulo distingue.
	/// </summary>
	private static string RotuloDoChefeNaMente(MoldeDeNpc m, IReadOnlyList<MoldeDeNpc> juntos)
	{
		string cru = m.Nome.Length > 0 ? m.Nome : m.Id;
		bool repetido = NomeDeChefeRepetidoDeTeste;
		if (repetido) return cru;
		string distinto = m.Id switch
		{
			"freeza_vegeta" => "Freeza (Planeta Vegeta)",
			"freeza_namek" => "Freeza (Namek)",
			_ => "",
		};
		if (distinto.Length > 0) return distinto;
		bool colide = juntos.Any(o => !string.Equals(o.Id, m.Id, StringComparison.OrdinalIgnoreCase)
			&& string.Equals(o.Nome, m.Nome, StringComparison.OrdinalIgnoreCase));
		return colide ? cru + " (" + m.Id + ")" : cru;
	}

	/// <summary>True se esta lembranca ainda pode subir um degrau. Quem nao e lembranca (sem teto) sobe como a saga.</summary>
	private static bool LembrancaPodeSubir(ServerPlayer npc)
	{
		if (LembrancaSobeAlemDoVistoDeTeste) return true;
		if (npc.Papel == null) return true;
		if (!_tetoDaLembranca.TryGetValue(npc.Id, out int teto)) return true;
		return npc.Papel.Estagio < teto;
	}

	private static void MarcarTetoDaLembranca(ServerPlayer chefe, int teto) => _tetoDaLembranca[chefe.Id] = teto;

	private static void EsquecerTetoDaLembranca(int id) => _tetoDaLembranca.Remove(id);

	/// <summary>A lista das etapas VISTAS, pelo canal que o menu de escolhas ja tem. Nao nasce ninguem ainda.</summary>
	private void PedirEtapaDaLembranca(ServerPlayer pl, MoldeDeNpc molde, HashSet<int> vistas)
	{
		var itens = vistas
			.Where(i => i >= 0 && i < molde.Estagios.Length)
			.OrderBy(i => i)
			.Select(i => EtapasDaMente.Item(molde.Id, i, EtapasDaMente.NomeDaEtapa(molde.Estagios[i], i)));
		MandarEscolhas(pl, EtapasDaMente.Verbo, itens);
		Avisar(pl, "Escolha a forma que você vai enfrentar.");
	}

	/// <summary>A resposta da lista. O indice tem de ser um que este personagem viu: o cliente nao escolhe forma.</summary>
	private void EscolherEtapaDaLembranca(ServerPlayer pl, string arg)
	{
		if (!EtapasDaMente.LerItem(arg, out string molde, out int indice, out _))
		{
			Avisar(pl, "Essa forma não existe.");
			return;
		}
		ChamarChefe(pl, molde + ":" + indice.ToString(System.Globalization.CultureInfo.InvariantCulture));
	}
}
