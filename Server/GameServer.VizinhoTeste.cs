using Jandirus.Core.Combat;
using Jandirus.Core.Stats;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// OS GANCHOS DE SERVIDOR DA BANCADA `--diagvizinho` (item 5 do playtest de 2026-10-10): o que quem esta PERTO
/// ouve e sente de um corpo que reune Ki e arranca. O robo e do cliente (`RoboDosVizinhos`), roda em `--host`
/// e pede por aqui o que so o servidor pode fazer: por um corpo SEM DONO ao lado do host e mandar nele pelos
/// funis de producao -- o `Carregar` da tecla C, o tique da carga, o snapshot.
///
/// NADA DAQUI DECIDE REGRA: cada gancho so arruma a cena. Quem responde se o corpo zumbe, se o chao treme e
/// pra quem, e o caminho de jogo (`EstadoDe` -> o fio -> `World.AoReceberSnapshot`).
/// </summary>
public sealed partial class GameServer
{
	private readonly List<ServerPlayer> _vizinhosDeBancada = [];

	private ServerPlayer? VizinhoDeBancada(int id) => _vizinhosDeBancada.Find(v => v.Id == id);

	/// <summary>
	/// Poe um corpo sem dono a <paramref name="tiles"/> tiles a leste do host e devolve o id dele. Com
	/// <paramref name="comControle"/> ele tem o `canPower` (aura, e passar dos 100%); sem, so o Ki Unlocked --
	/// reune sem aura nenhuma, que e o caso que era mudo pra quem estava do lado.
	/// </summary>
	internal int MontarVizinhoDeBancada(int idDoHost, float tiles, bool comControle)
	{
		if (!_players.TryGetValue(idDoHost, out ServerPlayer? host)) return 0;

		var v = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = $"Vizinho{_vizinhosDeBancada.Count + 1}",
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = host.Zone,
			Pos = new Vec2(host.Pos.X + tiles * ZoneCollision.TileSize, host.Pos.Y),
			LastInputMs = NowMs(),
			Conta = "bancada_vizinho",
			Slot = _vizinhosDeBancada.Count,
			// Os mesmos numeros do corpo forjado da `--arenaiateste`: uma ficha sem atributo nenhum tem
			// folego ZERO, e a carga morreria no primeiro tique por "sem folego".
			Ficha = new Fighter
			{
				Name = "Vizinho", Race = "Human", BP = 1000, Idade = 25,
				physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
				kiskill = 5, speed = 5, magiskill = 5, maxstamina = 100, stamina = 100,
			},
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		v.Ficha.Class = "Normal";
		PorNoMundo(v);

		// AS DUAS CHAVES DA TECLA C, direto no campo (ver `CargaDeKi`): a skill so vira campo pelo
		// `AplicarEfeitos`, e o que se mede aqui e o que os OUTROS percebem, nao a compra da skill.
		v.Ficha.MeditateGivesKiRegen = 1;
		v.Ficha.canPower = comControle ? 1 : 0;
		v.Ficha.Ki = v.Ficha.MaxKi * 0.5;
		v.Ficha.stamina = v.Ficha.maxstamina;
		_vizinhosDeBancada.Add(v);
		return v.Id;
	}

	/// <summary>O vizinho aperta (ou solta) o C, pela porta do jogador (`Carregar`). Devolve se o servidor aceitou.</summary>
	internal bool VizinhoSeguraOC(int id, bool ligado)
	{
		if (VizinhoDeBancada(id) is not { } v) return false;
		Carregar(v, ligado);
		return v.Carregando;
	}

	/// <summary>O vizinho arranca (ou para): os dois campos que o `EstadoDe` junta no bit `Correndo`.</summary>
	internal void VizinhoCorre(int id, bool ligado)
	{
		if (VizinhoDeBancada(id) is not { } v) return;
		v.Moving = ligado;
		v.Correndo = ligado;
	}

	/// <summary>Leva o vizinho pra <paramref name="tiles"/> tiles a leste do host -- o tremor cai com a distancia.</summary>
	internal void LevarVizinhoDeBancada(int id, int idDoHost, float tiles)
	{
		if (VizinhoDeBancada(id) is not { } v || !_players.TryGetValue(idDoHost, out ServerPlayer? host)) return;
		v.Pos = new Vec2(host.Pos.X + tiles * ZoneCollision.TileSize, host.Pos.Y);
	}

	/// <summary>
	/// Da a um corpo (um vizinho, ou o proprio host) o controle de Ki que deixa passar dos 100%, com o tanque
	/// ja em <paramref name="razao"/> do maximo -- 1,25 e "acima de 120%" sem passar do teto seguro (130%).
	/// </summary>
	internal void DarControleDeBancada(int id, double razao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? c)) return;
		c.Ficha.MeditateGivesKiRegen = 1;
		c.Ficha.canPower = 1;
		c.Ficha.powerMod = Math.Max(c.Ficha.powerMod, 1);
		c.Ficha.stamina = c.Ficha.maxstamina;
		c.Ficha.Ki = c.Ficha.MaxKi * razao;
		// O TETO SEGURO NO LUGAR DE FABRICA (130% do tanque): abaixo dele a sobrecarga so vaza devagar. Sem
		// isto uma ficha de bancada com o teto baixo perderia 10% do Ki por segundo e sairia dos 120% antes
		// de a regua olhar.
		c.Ficha.kicapacity = Math.Max(c.Ficha.kicapacity, c.Ficha.MaxKi * 1.3);
	}

	/// <summary>O que o servidor diz deste corpo AGORA -- os tres fatos que viram bit no snapshot.</summary>
	internal (bool Reunindo, bool Aura, bool Treme) CargaDeBancada(int id)
		=> _players.TryGetValue(id, out ServerPlayer? c)
			? (c.Carregando, c.AuraDaCarga, CargaDeKi.TremeOChao(c.Ficha, c.Carregando, c.EstagioDaCarga))
			: default;

	/// <summary>Tira os vizinhos do mundo: a bancada nao deixa corpo pra tras.</summary>
	internal void DesmontarVizinhosDeBancada()
	{
		foreach (ServerPlayer v in _vizinhosDeBancada)
		{
			PararCarga(v);
			RemoverNpc(v);
		}
		_vizinhosDeBancada.Clear();
	}
}
