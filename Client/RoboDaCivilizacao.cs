using System.Collections;
using Godot;
using Jandirus.Core.Races;
using Jandirus.Core.World;
using Jandirus.Server;

namespace Jandirus.Client;

/// <summary>
/// O ROBO DAS CIVILIZACOES (`--diagciv`, o pedido 227, 2026-11-02). O dono: *"15% dos planetas procedurais vao ter civilizaçao, com isso
/// vc pode fazer cidades procedurais com casas e estradas ligando essas casas etc, e os npcs desse planeta andando normalmente"*. As
/// regras sao da bancada `--civteste`; aqui se mede o que o JOGADOR ve, no primeiro mundo com cidade da seed de fabrica (o mesmo em toda
/// rodada):
///   K1. a CIDADE NA TELA: a camada das casas e estradas tem uma celula pintada por celula da cidade, e a porta de cada casa esta pintada;
///   K2. o POVO NA TELA: os habitantes nasceram com o mundo, sao todos da raca dele, e os que estao perto da praca chegam ao cliente --
///       foto `civ-1-a-cidade`;
///   K3. o povo ANDA: em oito segundos com o jogador perto, habitantes saem do lugar;
///   K4. o povo FALA A LINGUA DELE: o habitante mais perto diz uma frase, e o jogador (que nao sabe a lingua) a ouve embaralhada -- foto
///       `civ-2-a-lingua-do-povo`;
///   e uma foto da rua de uma casa, `civ-3-uma-casa`.
///
///     Godot --path . --janela --resolution 1280x720 --host --rede 12981 --horateste 0.5 --semfauna --diagciv --raca Human --conta bancada_civ --nome Viajante
/// </summary>
public partial class RoboDaCivilizacao : Node
{
	private const string Frase = "Bem-vindo a nossa cidade, viajante";

	private int _ok, _falhou;
	private IEnumerator? _roteiro;
	private double _espera = 3.0;
	private bool _acabou;
	private readonly List<(string Autor, string Texto)> _falas = [];

	private void Afirmar(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _ok++; GD.Print($"[dciv]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _falhou++; GD.PrintErr($"[dciv]   FALHA {nome}   [{detalhe}]"); }
	}

	public override void _Process(double delta)
	{
		if (_acabou) return;
		if (GameClient.Instance is not { Connected: true } || World.Instancia?.PosicaoLocal == null) return;

		_espera -= delta;
		if (_espera > 0) return;

		_roteiro ??= Roteiro().GetEnumerator();
		if (!_roteiro.MoveNext())
		{
			_acabou = true;
			GD.Print(_falhou == 0 ? $"\n[dciv] ===== TUDO OK ===== ({_ok} OK)" : $"\n[dciv] ===== {_falhou} FALHA(S) ===== ({_ok} OK)");
			GD.Print("[dciv] ===== FIM =====");
			return;
		}
		_espera = _roteiro.Current is double d ? d : 0;
	}

	public override void _ExitTree()
	{
		if (GameClient.Instance is { } cli) cli.Falou -= AoFalar;
	}

	private void AoFalar(Jandirus.Net.Protocol.Fala canal, string autor, string texto)
	{
		if (canal == Jandirus.Net.Protocol.Fala.Diz) _falas.Add((autor, texto));
	}

	private void Fotografar(string nome)
	{
		Image? img = GetViewport()?.GetTexture()?.GetImage();
		if (img == null || img.IsEmpty()) { GD.Print("[dciv] sem foto (headless nao renderiza): rode com janela"); return; }
		string caminho = ProjectSettings.GlobalizePath($"user://{nome}.png");
		img.SavePng(caminho);
		GD.Print($"[dciv] foto {caminho}");
	}

	private static IEnumerable<Node> Todos(Node raiz)
	{
		foreach (Node f in raiz.GetChildren())
		{
			yield return f;
			foreach (Node n in Todos(f)) yield return n;
		}
	}

	private IEnumerable Roteiro()
	{
		GameClient cli = GameClient.Instance!;
		if (GameServer.Instance is not { } srv) { Afirmar("(montagem) o robo roda no modo host", false); yield break; }
		if (GameServer.MundoCivilizadoDeTeste() is not { } achado) { Afirmar("(montagem) ha um mundo com cidade na seed de fabrica", false); yield break; }
		(PlanetaNoEspaco planeta, MundoProcedural ficha, TerrenoGerado t) = achado;
		CidadeProcedural cidade = t.Cidade!;
		var zona = ZoneKey.Procedural(planeta.Nome, planeta.Seed);
		string raca = RacaProcedural.NomeDoMundo(planeta.Seed);
		cli.Falou += AoFalar;

		// ---- POUSAR, NA PRACA, DE DIA E DE CEU LIMPO ----
		for (int i = 0; i < 60 && !srv.ProceduralDeFoto_Pousar(cli.LocalId, planeta.Nome, planeta.Seed); i++) yield return 0.25;
		for (int i = 0; i < 40 && !cli.Zone.Equals(zona); i++) yield return 0.25;
		yield return 4.0;
		Afirmar($"(montagem) o corpo pousou em {planeta.Nome} ({ficha.Descricao()}) e o cliente montou o mundo", cli.Zone.Equals(zona), cli.Zone.ToString());
		if (!cli.Zone.Equals(zona)) yield break;
		srv.EmbateDeFoto_CeuLimpo(cli.LocalId);
		for (int i = 0; i < 20; i++)
		{
			double daqui = World.Instancia?.Ceu is { } ceu ? ceu.Hora - Math.Floor(ceu.Hora) : -1;
			if (daqui is >= 0.42 and <= 0.6) break;
			srv.CeuDaTerraDeTeste(i / 20.0);
			yield return 0.35;
		}
		srv.ProceduralDeFoto_Levar(cli.LocalId, cidade.CentroX, cidade.CentroY + 1);
		yield return 3.5;

		// ---- K1. A CIDADE NA TELA ----
		TileMapLayer? camada = Todos(GetTree().Root).OfType<TileMapLayer>().FirstOrDefault(l => l.Name == "Cidade" && l.IsVisibleInTree());
		int esperadas = cidade.Celulas().Count();
		int pintadas = camada?.GetUsedCells().Count ?? 0;
		int portas = cidade.Casas.Count(c => camada != null && camada.GetCellSourceId(new Vector2I(c.Porta.X, c.Porta.Y)) >= 0);
		Afirmar($"K1. **A CIDADE NA TELA** (estilo {cidade.Desenho.Nome}): uma celula pintada por celula da cidade, e a porta de cada uma das {cidade.Casas.Count} casas",
				camada != null && pintadas == esperadas && portas == cidade.Casas.Count,
				$"camada {(camada != null)}; {pintadas} de {esperadas} celulas; {portas} portas");

		// ---- K2. O POVO NA TELA ----
		List<(int Id, string Nome, string Raca, Vec2 Pos)> povo = srv.PalcoCiv_Povo(zona);
		var remotos = new HashSet<int>(World.Instancia!.RemotosNaGradeDeTeste().Select(r => r.Id));
		Vector2 eu = World.Instancia.PosicaoLocal ?? Vector2.Zero;
		var pertoDaPraca = povo.Where(p => new Vector2(p.Pos.X, p.Pos.Y).DistanceTo(eu) < 12 * ZoneCollision.TileSize).ToList();
		int naTela = pertoDaPraca.Count(p => remotos.Contains(p.Id));
		Afirmar($"K2. **O POVO NA TELA**: os habitantes nasceram com o mundo, todos {raca}, e os que estao perto da praca chegaram ao cliente",
				povo.Count == Civilizacao.Habitantes(cidade) && povo.All(p => p.Raca == raca) && pertoDaPraca.Count > 0 && naTela == pertoDaPraca.Count,
				$"{povo.Count} habitante(s) (esperados {Civilizacao.Habitantes(cidade)}); {pertoDaPraca.Count} perto da praca, {naTela} no cliente; "
				+ $"nomes {string.Join(", ", povo.Take(6).Select(p => p.Nome))}");
		Fotografar("civ-1-a-cidade");

		// ---- K3. O POVO ANDA ----
		Dictionary<int, Vec2> antes = povo.ToDictionary(p => p.Id, p => p.Pos);
		yield return 8.0;
		int andaram = srv.PalcoCiv_Povo(zona).Count(p => antes.TryGetValue(p.Id, out Vec2 a) && (p.Pos - a).Length > ZoneCollision.TileSize / 2f);
		Afirmar("K3. **O POVO ANDA**: em oito segundos com o jogador perto, habitantes saem do lugar", andaram >= 2, $"{andaram} andaram");

		// ---- K4. A LINGUA DO POVO ----
		eu = World.Instancia.PosicaoLocal ?? Vector2.Zero;
		var falante = srv.PalcoCiv_Povo(zona).OrderBy(p => new Vector2(p.Pos.X, p.Pos.Y).DistanceTo(eu)).FirstOrDefault();
		_falas.Clear();
		if (falante.Id != 0) srv.PalcoLN_Dizer(falante.Id, Frase);
		yield return 1.5;
		string? ouvido = _falas.FindLast(f => f.Autor == falante.Nome) is { Autor.Length: > 0 } fl ? fl.Texto : null;
		Afirmar($"K4. **O POVO FALA A LINGUA DELE** (a lingua {raca}): o habitante mais perto diz \"{Frase}\", e o jogador, que nao a sabe, ouve embaralhado",
				falante.Id != 0 && ouvido != null && ouvido != Frase && ouvido.Length > 0,
				$"{falante.Nome}: \"{ouvido}\"");
		Fotografar("civ-2-a-lingua-do-povo");

		// ---- A RUA DE UMA CASA ----
		CidadeProcedural.Casa casa = cidade.Casas[0];
		srv.ProceduralDeFoto_Levar(cli.LocalId, casa.Soleira.X + 2, casa.Soleira.Y + 1);
		yield return 3.0;
		Fotografar("civ-3-uma-casa");
	}
}
