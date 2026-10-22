using Godot;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO `--diagcellpod` (2026-10-21, o desenho do mainframe conforme o laboratorio -- `ArteDoLaboratorio`). Quatro mainframes de
/// androides CRUS, do robo, aparafusados, numa fileira acima do corpo; e as portas que o robo usa pra chegar perto de cada um (quem crava a
/// posicao e o servidor, como num teleporte de tecnica), ler o laboratorio e pedir o pacote das construcoes de novo. A INSTALACAO e a de
/// producao: o robo humano manda o verbo pelo fio (`lab_androide`, `lab_bio`); so o Neo Tuffle Lab e posto aqui, porque so um Tsufurujin o
/// instala (a instalacao dele pelo menu e da `--diagtuffle`). Ver `Client/RoboDoCellPod.cs`.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>O comeco dos ids dos mainframes deste palco.</summary>
	private const int IdDoPalcoDoCellPod = 986_300;

	private readonly List<Obra> _mainframesDoCellPod = [];

	/// <summary>Onde o robo estava quando a fileira foi montada -- e de la que as fotos sao tiradas.</summary>
	private Vec2 _meioDoPalcoDoCellPod;

	/// <summary>
	/// MONTA A FILEIRA: tira o povo de perto (um cidadao que passe na frente sujaria a foto do chao do robo), da a tecnologia do laboratorio, e
	/// poe um mainframe cru em cada coluna de `colunas` (em tiles, a partir do corpo), `linha` tiles acima dele. Devolve as celulas, na ordem.
	/// </summary>
	internal List<(int X, int Y)> PalcoCP_Montar(int id, int[] colunas, int linha)
	{
		var celulas = new List<(int X, int Y)>();
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return celulas;
		PalcoCP_Limpar();
		float longe = 30 * ZoneCollision.TileSize;
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).Where(o => o.Peer == null && o != pl && Vec2.Distance(o.Pos, pl.Pos) < longe).ToList())
			RemoverNpc(o);
		pl.Ficha.techskill = Math.Max(pl.Ficha.techskill, TechDoLaboratorio + 10);
		_meioDoPalcoDoCellPod = pl.Pos;
		const float T = ZoneCollision.TileSize;
		for (int i = 0; i < colunas.Length; i++)
		{
			var o = new Obra
			{
				Id = IdDoPalcoDoCellPod + i, Tipo = ArteDoLaboratorio.TipoDoMainframe, Aparafusada = true,
				X = pl.Pos.X + colunas[i] * T, Y = pl.Pos.Y - linha * T, DonoConta = pl.Conta, DonoNome = pl.Name,
			};
			o.PorZona(pl.Zone);
			_noChao.Add(o);
			_mainframesDoCellPod.Add(o);
			celulas.Add(CatalogoDeObras.Celula(o.X, o.Y));
		}
		MandarObras(pl.Zone);
		return celulas;
	}

	/// <summary>O corpo um tile abaixo do mainframe `i`, de frente pra ele -- como quem andou ate ele pra apertar a tecla E.</summary>
	internal bool PalcoCP_IrAte(int id, int i)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || i < 0 || i >= _mainframesDoCellPod.Count) return false;
		Obra o = _mainframesDoCellPod[i];
		CravarPosicao(pl, new Vec2(o.X, o.Y + ZoneCollision.TileSize));
		pl.Facing = Facing.North;
		return true;
	}

	/// <summary>O corpo de volta ao meio da fileira, de onde as fotos sao tiradas.</summary>
	internal void PalcoCP_Voltar(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) CravarPosicao(pl, _meioDoPalcoDoCellPod);
	}

	/// <summary>O laboratorio do mainframe `i` agora (-1 se ele nao existe).</summary>
	internal int PalcoCP_Lab(int i) => i >= 0 && i < _mainframesDoCellPod.Count ? _mainframesDoCellPod[i].Lab : -1;

	/// <summary>Um laboratorio posto pelo palco no mainframe `i` (o Neo Tuffle Lab, que o robo humano nao instala), e o pacote de sempre.</summary>
	internal void PalcoCP_PorOLab(int i, int lab)
	{
		if (i < 0 || i >= _mainframesDoCellPod.Count) return;
		_mainframesDoCellPod[i].Lab = lab;
		MandarObras(_mainframesDoCellPod[i].Zona);
	}

	/// <summary>O pacote das construcoes da zona do robo de novo: o defeito injetado do desenho so chega a tela por ele.</summary>
	internal void PalcoCP_Reenviar(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) MandarObras(pl.Zone);
	}

	/// <summary>A fileira sai do chao (e da tela).</summary>
	internal void PalcoCP_Limpar()
	{
		if (_mainframesDoCellPod.Count == 0) return;
		ZoneKey zona = _mainframesDoCellPod[0].Zona;
		foreach (Obra o in _mainframesDoCellPod) _noChao.Remove(o);
		_mainframesDoCellPod.Clear();
		MandarObras(zona);
	}
}
