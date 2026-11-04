using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Races;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// ============================ O POVO DE UM MUNDO COM CIVILIZACAO (o pedido 227, 2026-11-02) ============================
/// O dono: *"15% dos planetas procedurais vao ter civilizaçao [...] e os npcs desse planeta andando normalmente [...] essas cidades e npcs
/// sao gerados no momento em q alguem entra no planeta pela primeira vez"*. Qual mundo, a cidade e a raca sao do Core
/// (`Civilizacao`, `CidadeProcedural`, `RacaProcedural`); aqui mora o POVO:
///   * ele NASCE COM O CHAO (<see cref="PovoarACivilizacao"/>, chamado quando o mundo gerado fica pronto -- a primeira entrada de alguem),
///     pelas portas de sempre: a raca entra no catalogo (`RaceCatalog.Registrar`), o molde e o habitante comum com a raca, os nomes e o
///     poder do mundo (<see cref="MoldeDaCivilizacao"/>), e cada corpo sai do `NascerNpc` -- o mesmo de todo habitante (cerebro, perfil,
///     roupa, berco, a lingua nativa do pedido 221). O mundo em que so ficou o povo e podado como um vazio (`SoOPovoDaCidade`): o povo sai
///     com ele e o chao que nasce de novo os poe de novo, com os mesmos nomes e caras (a semente e a do lugar);
///   * ele NASCE NA CIDADE, pelas estradas (<see cref="PontoNaCidade"/>), e anda como todo habitante (`vagueia`);
///   * ele JA VEM ACOSTUMADO com a gravidade de casa (o berco dele e o mundo: `Birth.AclimatarAoBerco`), e e mais forte quanto mais pesado
///     o planeta (`RacaProcedural.FatorDePoder`, no BP do molde).
/// A IA so liga com jogador perto, como a de todo NPC -- o dono lembrou, e nada aqui mexe nisso.
/// ====================================================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O molde de onde o povo procedural copia tudo o que nao e dele: o habitante de sempre.</summary>
	private const string MoldeDoHabitanteComum = "cidadao";

	/// <summary>Quantos nomes de pessoa cada povo tem (o povo inteiro cabe com folga, e o reposto pela manutencao nao repete o vizinho).</summary>
	private const int NomesPorPovo = 80;

	/// <summary>Este corpo e do povo de um mundo com civilizacao (o molde `civ_`)?</summary>
	private static bool EhDoPovoDaCidade(ServerPlayer p) => p.Peer == null && p.Papel?.Molde.Id.StartsWith("civ_", StringComparison.Ordinal) == true;

	/// <summary>
	/// SO O POVO DA CIDADE NESTA ZONA (nenhum jogador, nenhum outro corpo): pra poda, a zona esta vazia -- o povo sai com o mundo e renasce
	/// com ele (os mesmos nomes e caras: a semente e a do lugar). Um vilao invadindo, um corpo largado ou um jogador seguram o mundo.
	/// </summary>
	private static bool SoOPovoDaCidade(List<ServerPlayer> gente) => gente.Count > 0 && gente.All(EhDoPovoDaCidade);

	/// <summary>As cidades dos mundos com civilizacao que estao na memoria, por zona -- o servidor joga o `TerrenoGerado` fora, e a cidade fica.</summary>
	private readonly Dictionary<ulong, CidadeProcedural> _cidades = [];

	/// <summary>A cidade desta zona, se ela e um mundo com civilizacao carregado.</summary>
	internal CidadeProcedural? CidadeDa(ZoneKey zona) => _cidades.GetValueOrDefault(zona.Hash);

	/// <summary>
	/// O MOLDE DO POVO DESTE MUNDO, montado uma vez e guardado no catalogo: o habitante comum (`cidadao`) com a raca procedural, os nomes
	/// dela, os dois generos e o poder multiplicado pela gravidade. A raca entra no catalogo de racas junto. Nulo sem os catalogos.
	/// </summary>
	internal MoldeDeNpc? MoldeDaCivilizacao(ulong seedDoMundo, double gravidade)
	{
		if (_moldes == null || _racas == null) return null;
		string raca = RacaProcedural.NomeDoMundo(seedDoMundo);
		_racas.Registrar(RacaProcedural.Proto(raca));

		string id = "civ_" + seedDoMundo.ToString("x16");
		if (_moldes.Get(id) is { } pronto) return pronto;
		if (_moldes.Get(MoldeDoHabitanteComum) is not { } comum) return null;

		MoldeDeNpc m = comum.Copiar();
		m.Id = id;
		m.Nome = raca;
		m.Racas = [raca];
		m.Nomes = RacaProcedural.NomesDePessoas(raca, NomesPorPovo);
		m.NomesPorRaca = new(StringComparer.OrdinalIgnoreCase);
		m.Corpo = "";
		m.Cabelo = "";
		m.CorDoCabelo = null;
		m.Generos = ["Male", "Male", "Female", "Female"];
		double f = RacaProcedural.FatorDePoder(gravidade);
		m.BpMin = comum.BpMin * f;
		m.BpMax = comum.BpMax * f;
		_moldes.Registrar(m);
		return m;
	}

	/// <summary>
	/// ONDE UM HABITANTE NASCE: numa celula de estrada da cidade (a ordem e fixa pela posicao, e o `lugar` anda nela de 37 em 37, pra o povo
	/// sair espalhado e nao em fila), no chao livre mais perto. Sem cidade (o mundo sem lugar pra ela), em volta da chegada.
	/// </summary>
	private Vec2 PontoNaCidade(ZoneKey zona, CidadeProcedural? cidade, ulong lugar)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		Vec2 chegada = ChegadaDaZonaGerada(zona) ?? PontoDeNascimento(zona);
		if (mapa == null) return chegada;
		if (cidade == null || cidade.Estradas.Count == 0)
		{
			var r = new Random((int)(Espaco.Misturar(zona.Hash, lugar, 0xC1F) & 0x7FFFFFFF));
			return mapa.PontoLivrePerto(chegada + new Vec2(r.Next(-10, 11) * ZoneCollision.TileSize, r.Next(-10, 11) * ZoneCollision.TileSize));
		}
		int[] estradas = [.. cidade.Estradas.Order()];
		int i = estradas[(int)(lugar * 37 % (ulong)estradas.Length)];
		return mapa.PontoLivrePerto(mapa.CentroDaCelula(i % cidade.Largura, i / cidade.Largura));
	}

	/// <summary>
	/// O MUNDO COM CIVILIZACAO NASCE POVOADO -- chamado quando o mundo gerado fica pronto (`TickDasGeracoes`), como o Yardrat. So nos 15%, e so
	/// se o povo ainda nao esta la (o chao que ficou na memoria nao ganha um segundo). Respeita os tetos do povoamento.
	/// </summary>
	private void PovoarACivilizacao(MundoProcedural ficha, TerrenoGerado t)
	{
		var zona = ZoneKey.Procedural(ficha.Nome, ficha.Seed);
		if (!Civilizacao.Tem(zona)) return;
		if (t.Cidade is { } cidade) _cidades[zona.Hash] = cidade;
		if (MoldeDaCivilizacao(ficha.Seed, ficha.Gravidade) is not { } molde) return;
		if (ZoneList(zona.Hash).Any(EhDoPovoDaCidade)) return;

		int quantos = t.Cidade is { } c ? Civilizacao.Habitantes(c) : Civilizacao.HabitantesNaPraca + 4 * Civilizacao.HabitantesPorCasa;
		int nasceram = 0;
		for (int i = 1; i <= quantos; i++)
		{
			if (!CabeMaisUmCorpo(zona, Povoamento.MaxPorZona, Povoamento.MaxNoServidor, out string porque))
			{
				GD.Print($"[server] civilizacao de {ficha.Nome}: {porque} -- {quantos - i + 1} habitante(s) nao nascem");
				break;
			}
			// ACOSTUMADO COM A GRAVIDADE DE CASA (*"eles ja vem acostumados com a gravidade padrao do planeta natal deles"*) sem linha aqui: o
			// berco do habitante e este mundo (`NascerNpc`), e o chao do berco ja acostuma todo corpo que nasce nele (`Birth.AclimatarAoBerco`,
			// no `AplicarGravidade`) -- a r2 provou: sem uma linha propria, a `--civteste` C6 continua achando o povo acostumado.
			if (NascerNpc(molde.Id, zona, PontoNaCidade(zona, t.Cidade, (ulong)i), (ulong)i) is null) continue;
			nasceram++;
		}
		GD.Print($"[server] o mundo {ficha.Nome} tem civilizacao: o povo {molde.Nome} ({nasceram} habitante(s), BP {molde.BpMin:N0}-{molde.BpMax:N0}, "
				 + $"gravidade {ficha.Gravidade:0.#}x), "
				 + (t.Cidade is { } cc ? $"cidade {cc.Desenho.Nome} de {cc.Casas.Count} casa(s) em ({cc.CentroX},{cc.CentroY})" : "sem lugar pra cidade"));
	}
}
