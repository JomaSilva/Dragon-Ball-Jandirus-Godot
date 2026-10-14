using Godot;
using Jandirus.Core.Combat;
using Jandirus.Core.Items;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DA `--diagv021` (`Client/RoboDaV021.cs`): o que o robo de tela precisa do servidor do mesmo processo --
/// por coisas na mochila e na carteira do jogador, por um mestre e uma bancada ao lado dele, e ler a VERDADE do
/// servidor pra comparar com o que a tela mostrou. Nada daqui e chamado em jogo.
/// </summary>
public sealed partial class GameServer
{
	/// <summary>As obras que o palco pos de pe, pra ele tirar no fim.</summary>
	private readonly List<Obra> _palcoV21Obras = [];

	/// <summary>Poe itens na mochila do jogador, e manda a mochila. Falso se nao coube.</summary>
	internal bool PalcoV21_Dar(int id, string item, int quantos = 1)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		bool coube = pl.Mochila.Guardar(item, quantos) == 0;
		MandarMochila(pl);
		return coube;
	}

	/// <summary>Escreve a carteira de um corpo (o jogador, ou um caido de bancada), e a manda.</summary>
	internal bool PalcoV21_Carteira(int id, double zeni, double recursos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.Ficha.Zeni = zeni;
		pl.Ficha.Recursos = recursos;
		MandarCarteira(pl);
		return true;
	}

	/// <summary>A carteira de um corpo, como o servidor a tem.</summary>
	internal (double Zeni, double Recursos) PalcoV21_CarteiraDe(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? (pl.Ficha.Zeni, pl.Ficha.Recursos) : (0, 0);

	/// <summary>Esvazia a mochila do jogador e o chao da zona dele: cada familia comeca limpa.</summary>
	internal void PalcoV21_Limpar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Mochila.Pilhas.Clear();
		LargadosDe(pl.Zone.Hash).Clear();
		MandarMochila(pl);
		MandarOChao(pl.Zone);
	}

	/// <summary>Quantas coisas ha largadas na zona do jogador, e quantas delas sao este item.</summary>
	internal (int Todas, int Deste) PalcoV21_NoChao(int id, string item)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return (0, 0);
		List<Largado> chao = LargadosDe(pl.Zone.Hash);
		return (chao.Count, chao.Count(l => l.Item == item));
	}

	/// <summary>
	/// Um MESTRE de bancada de frente pro jogador, que o convida a ser aluno pela porta de producao (`mst_convidar`).
	/// Devolve o id dele, ou zero se o convite nao saiu.
	/// </summary>
	internal int PalcoV21_Mestre(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		var mestre = new ServerPlayer
		{
			Id = IdBaseDeProjetil + 800 + _pjCorpos++,
			Peer = null,
			Name = "Mestre de bancada",
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Zone = pl.Zone,
			Pos = pl.Pos + new Vec2(ZoneCollision.TileSize * 0.9f, 0),
			Facing = Facing.West,
			Conta = "bancada_v021_mestre",
			Slot = 0,
			Ficha = new Fighter { Race = "Human", BP = Math.Max(pl.Ficha.BP * 1000, 1_000_000) },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		mestre.Ficha.Class = "Normal";
		mestre.Class = "Normal";
		PorNoMundo(mestre);
		mestre.Ficha.Ki = mestre.Ficha.MaxKi;
		mestre.Ficha.Tick(agoraMs: NowMs());
		UsarHabilidade(mestre, "mst_convidar");
		return pl.PedidoDoMestre != null ? mestre.Id : 0;
	}

	/// <summary>O mestre de bancada convida de novo (depois de recusado).</summary>
	internal bool PalcoV21_ConvidarDeNovo(int idDoMestre, int idDoAluno)
	{
		if (!_players.TryGetValue(idDoMestre, out ServerPlayer? mestre) || !_players.TryGetValue(idDoAluno, out ServerPlayer? aluno)) return false;
		UsarHabilidade(mestre, "mst_convidar");
		return aluno.PedidoDoMestre != null;
	}

	/// <summary>O jogador tem um convite de mestre pendente no servidor?</summary>
	internal bool PalcoV21_TemConvite(int id) => _players.TryGetValue(id, out ServerPlayer? pl) && pl.PedidoDoMestre != null;

	/// <summary>Apaga o convite pendente do jogador (o palco limpando o que montou).</summary>
	internal void PalcoV21_SemConvite(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.PedidoDoMestre = null;
	}

	/// <summary>Uma BANCADA DE CRIACAO aparafusada, do jogador, um tile acima dele -- e a zona fica sabendo.</summary>
	internal bool PalcoV21_Bancada(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		var o = new Obra
		{
			Id = _proximaObraId++, Tipo = BancadaDeCriacao.Tipo, X = pl.Pos.X, Y = pl.Pos.Y - ZoneCollision.TileSize,
			DonoConta = pl.Conta, DonoNome = pl.Name, Aparafusada = true, ErguidaEm = NowMs(),
		};
		o.PorZona(pl.Zone);
		_noChao.Add(o);
		_palcoV21Obras.Add(o);
		MandarObras(pl.Zone);
		return true;
	}

	/// <summary>Tira as obras que o palco pos, e avisa a zona.</summary>
	internal void PalcoV21_TirarAsObras(int id)
	{
		foreach (Obra o in _palcoV21Obras) _noChao.Remove(o);
		_palcoV21Obras.Clear();
		if (_players.TryGetValue(id, out ServerPlayer? pl)) MandarObras(pl.Zone);
	}

	/// <summary>O penteado, a cor do cabelo e a cor dos olhos do jogador, como o servidor os tem (cor vazia = a do desenho).</summary>
	internal (string Cabelo, string CorDoCabelo, string CorDoOlho) PalcoV21_Aparencia(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (pl.Visual.Cabelo, pl.Visual.CorCabelo?.ToString() ?? "", pl.Visual.CorOlho?.ToString() ?? "")
			: ("", "", "");

	/// <summary>O scouter ligado e o peso vestido do jogador, como o servidor os tem.</summary>
	internal (bool Scouter, double Peso) PalcoV21_Usando(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? ((pl.PoderesConcedidos & Jandirus.Net.Protocol.Poder.Scouter) != 0, pl.Ficha.Weighted)
			: (false, 0);

	// =====================================================================
	// OS FIGURANTES (o agarrao e as formas na tela)
	// =====================================================================
	/// <summary>
	/// Um FIGURANTE de bancada ao lado do jogador. Sem slot (o padrao do `ServerPlayer`): nada dele vai pro disco. O
	/// penteado e o potencial desperto entram ANTES de ele ser apresentado, porque e na entrada que a aparencia de um
	/// corpo sem tela chega a zona (o `ReapresentarOCorpo` so serve a jogador). APRESENTADO como um NPC que nasce
	/// (`TrocarAparencias`): sem isso o cliente o desenharia com o corpo padrao, sem o cabelo nem as marcas.
	/// </summary>
	private ServerPlayer FigurantePalcoV21(ServerPlayer pl, string nome, string raca, Vec2 deslocamento, double bp,
										   string cabelo = "", bool potencialDesperto = false)
	{
		var novo = new ServerPlayer
		{
			Id = IdBaseDeProjetil + 900 + _pjCorpos++,
			Peer = null,
			Name = nome,
			Race = raca,
			Genero = "Male",
			Idade = 25,
			Zone = pl.Zone,
			Pos = pl.Pos + deslocamento,
			Conta = "bancada_v021_figurante",
			Ficha = new Fighter { Race = raca, BP = bp },
			Livro = new Jandirus.Core.Skills.SkillBook(),
		};
		novo.Ficha.Class = "Normal";
		novo.Class = "Normal";
		if (cabelo.Length > 0) novo.Visual.Cabelo = cabelo;
		if (potencialDesperto) novo.Ficha.unlockPotential = 1;
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		TrocarAparencias(novo);
		return novo;
	}

	/// <summary>
	/// Tira do mundo um corpo de bancada AVISANDO a zona (`RemoverNpc`, o `PeerLeft`): o boneco sai da tela de quem o
	/// via na hora, em vez de ficar congelado ate o cliente desistir dele. Quem ele segurava (ou quem o segurava) e
	/// solto antes.
	/// </summary>
	internal void PalcoV21_TirarOFigurante(int idDoFigurante)
	{
		if (!_players.TryGetValue(idDoFigurante, out ServerPlayer? corpo) || corpo.Peer != null) return;
		if (corpo.AgarradoPorId != 0 && _players.TryGetValue(corpo.AgarradoPorId, out ServerPlayer? quem)) Soltar(quem, MotivoDaSoltura.Tecla);
		if (corpo.AgarrandoId != 0) Soltar(corpo, MotivoDaSoltura.Tecla);
		EsquecerTecnicas(corpo.Id);
		RemoverNpc(corpo);
	}

	/// <summary>
	/// Uma PRESA de bancada: um corpo de pe, do mesmo poder do jogador, de frente pra ele e ao alcance do agarrao -- a
	/// tecla Q a pega pela porta de producao. Devolve o id dela.
	/// </summary>
	internal int PalcoV21_Presa(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		ServerPlayer presa = FigurantePalcoV21(pl, "Presa de bancada", "Human",
											   MeleeArea.Frente(pl.Facing) * (ZoneCollision.TileSize * 0.9f), Math.Max(pl.Ficha.expressedBP, 10));
		presa.Facing = pl.Facing switch
		{
			Facing.North => Facing.South,
			Facing.South => Facing.North,
			Facing.East => Facing.West,
			_ => Facing.East,
		};
		return presa.Id;
	}

	/// <summary>O agarrao do jogador como o servidor o tem: quem ele segura, se ja o levantou no colo e se aperta o pescoco.</summary>
	internal (int Quem, bool NoColo, bool Apertando) PalcoV21_Agarrao(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl)
			? (pl.AgarrandoId, pl.AgarrandoId != 0 && pl.ModoDoAgarrao == ModoDeAgarrao.Carregando, pl.AgarrandoId != 0 && pl.Estrangulando)
			: (0, false, false);

	/// <summary>
	/// Um figurante da raca e do penteado pedidos, a `tilesALeste` do jogador e duas fileiras abaixo dele (fora do
	/// caminho da tecla E e dos tiros). Devolve o id.
	/// </summary>
	internal int PalcoV21_Figurante(int id, string raca, string cabelo, int tilesALeste, bool potencialDesperto = false)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return 0;
		return FigurantePalcoV21(pl, $"Figurante {raca}", raca, new Vec2(tilesALeste * ZoneCollision.TileSize, 2 * ZoneCollision.TileSize),
								 5_000_000, cabelo, potencialDesperto).Id;
	}

	/// <summary>
	/// Poe o figurante numa forma pela porta de producao (`EntrarNaForma`, sem a cinematica), liberando-a antes; vazio =
	/// de volta ao piso da escada dele (`Reverter`). Devolve a forma em que o servidor o deixou.
	/// </summary>
	internal string PalcoV21_Forma(int idDoFigurante, string forma)
	{
		if (!_players.TryGetValue(idDoFigurante, out ServerPlayer? corpo) || corpo.Peer != null) return "";
		if (forma.Length == 0) Reverter(corpo, "bancada");
		else if (Jandirus.Core.Forms.Catalogo.Def(forma) is { } def)
		{
			corpo.Forma.Liberar(forma);
			EntrarNaForma(corpo, def, semCena: true);
		}
		return corpo.Forma.Atual;
	}

	// =====================================================================
	// O KI NA TELA (itens 1, 7, 8, 11 e 34)
	// =====================================================================
	/// <summary>
	/// Empresta verbos de tecnica ao jogador SO NESTA SESSAO -- pelo conjunto que um Majin recebe de quem absorveu
	/// (`ServerPlayer.VerbosAbsorvidos`), que nao entra no livro nem vai pro disco: a conta da bancada e reusada entre
	/// rodadas e nao pode ir engordando. Enche o tanque junto. Devolve se o servidor passou a reconhecer todos.
	/// </summary>
	internal bool PalcoV21_EmprestarVerbos(int id, params string[] verbos)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		pl.VerbosAbsorvidos = new HashSet<string>(verbos, StringComparer.OrdinalIgnoreCase);
		pl.Ficha.Ki = pl.Ficha.MaxKi;
		MandarFicha(pl);
		return verbos.All(v => SabeTecnica(pl, v));
	}

	/// <summary>Tira do jogador os verbos emprestados.</summary>
	internal void PalcoV21_DevolverVerbos(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? pl)) pl.VerbosAbsorvidos = null;
	}

	/// <summary>
	/// Escreve o Ki do jogador como fracao do tanque (1 = cheio; acima de 1 e o excesso que a tecla X libera) e manda
	/// a ficha.
	/// </summary>
	internal void PalcoV21_Ki(int id, double razao)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.Ki = pl.Ficha.MaxKi * razao;
		MandarFicha(pl);
	}

	/// <summary>O Ki do jogador como fracao do tanque (1 = cheio), como o servidor o tem.</summary>
	internal double PalcoV21_RazaoDeKi(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? pl.Ficha.Ki / Math.Max(pl.Ficha.MaxKi, 1e-9) : 0;
}
