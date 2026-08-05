using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PASSAGENS ENTRE MAPAS -- pisou, mudou de mundo.
///
/// ============================ ELAS NAO EXISTIAM, E OS DESTINOS TAMBEM NAO ============================
/// A boca da caverna, a escada do Templo e a saida do Inferno eram celulas de tilemap: desenho e
/// nada mais. Pior -- tres dos destinos NEM ESTAVAM NO CATALOGO: nove andares diferentes tinham
/// `/area/Outside` como area dominante e viravam todos a zona "Outside", e o `ZoneCatalog` guarda
/// um por nome. O Templo Sagrado (z12), a caverna de Vegeta (z22) e a caverna da Terra (z23) eram
/// mapas convertidos, no disco, que o servidor nunca conseguiria carregar.
///
/// Isso foi consertado no conversor (ver `Passagens.NomeDoZ`), e este arquivo e a outra metade: o
/// gancho que leva o corpo pra la.
/// ======================================================================================================
///
/// ============================ E O MESMO CAMINHO DO DECOLAR ============================
/// O dono descreveu o efeito que queria: "assim como acontece com ir pro espaço ao apertar decolar".
/// E literalmente o mesmo caminho -- `MoveToZone`, que ja sabe tirar o corpo de uma zona, por na
/// outra, avisar os dois lados e carimbar a sequencia pra o cliente nao puxar de volta. Passagem
/// nao precisou de mecanismo novo; precisou de um gatilho.
/// =======================================================================================
/// </summary>
public sealed partial class GameServer
{
	/// <summary>As passagens de cada zona, por nome de zona. Lidas no boot.</summary>
	private readonly Dictionary<string, List<Passagem>> _passagens =
		new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// QUEM ACABOU DE CHEGAR POR UMA PASSAGEM, e ate quando esta imune.
	///
	/// ============================ SEM ISTO, IDA E VOLTA VIRA UM LOOP ============================
	/// A saida da caverna fica em cima da entrada correspondente do outro lado -- e assim no DM
	/// tambem. Sem carencia, o corpo chega, o tique seguinte ve que ele esta sobre uma passagem, e
	/// ele volta. E de novo. O jogador ficaria piscando entre dois mapas sem conseguir andar.
	///
	/// A carencia e por PESSOA e nao por celula: ela protege a chegada, e nao a passagem.
	/// ============================================================================================
	/// </summary>
	private readonly Dictionary<int, long> _acabouDeAtravessar = [];

	/// <summary>Quanto tempo o corpo fica imune a passagens depois de atravessar uma.</summary>
	private const long MsDeCarenciaDePassagem = 1500;

	/// <summary>Le o `.passagens` de cada zona do manifesto. Chamado no boot, junto do `.col`.</summary>
	private void CarregarPassagens()
	{
		if (_catalogo == null) return;

		int total = 0;
		foreach (ZoneEntry e in _catalogo.Todas)
		{
			if (e.PassagensArq.Length == 0 || !Godot.FileAccess.FileExists(e.PassagensArq)) continue;

			List<Passagem> lista = Passagem.Parse(Godot.FileAccess.GetFileAsString(e.PassagensArq));

			// UMA PASSAGEM PRA ZONA QUE NAO EXISTE E PIOR QUE NENHUMA: ela levaria o corpo pro
			// vazio. Melhor recusar aqui, com o nome no log, do que descobrir com um jogador preso.
			int mortas = lista.RemoveAll(p => _catalogo.Get(p.Zona) == null);
			if (mortas > 0)
				GD.PushWarning($"[server] {e.Zona}: {mortas} passagem(ns) apontam pra zona inexistente");

			if (lista.Count == 0) continue;
			_passagens[e.Zona] = lista;
			total += lista.Count;
		}
		if (total > 0) GD.Print($"[server] passagens: {total} em {_passagens.Count} zona(s)");
	}

	/// <summary>
	/// QUEM PISOU NUMA PASSAGEM ATRAVESSA. Roda no tique cheio.
	///
	/// O CUSTO E O NUMERO DE JOGADORES, e nao o de passagens -- como nas portas. So as zonas com
	/// gente sao olhadas, e a mais carregada tem seis.
	/// </summary>
	private void TickDasPassagens()
	{
		long agora = NowMs();

		// A LISTA E COPIADA porque atravessar MEXE em `_players` por dentro (`MoveToZone` troca a
		// lista da zona), e iterar uma colecao que muda no meio estoura.
		foreach (ServerPlayer pl in _players.Values.ToList())
		{
			if (pl.Ficha.dead || pl.Ficha.KO) continue;
			if (_acabouDeAtravessar.TryGetValue(pl.Id, out long livre) && agora < livre) continue;
			if (!_passagens.TryGetValue(pl.Zone.Name, out List<Passagem>? lista)) continue;

			// A CELULA DOS PES, e nao a do centro do sprite. E a mesma conta que a colisao e as
			// portas fazem (`MoveRules.FeetOffsetY`): o corpo ocupa o tile em que ele PISA, e medir
			// pelo meio do desenho abriria a passagem um tile antes de chegar nela.
			int cx = (int)Math.Floor(pl.Pos.X / ZoneCollision.TileSize);
			int cy = (int)Math.Floor((pl.Pos.Y + MoveRules.FeetOffsetY) / ZoneCollision.TileSize);

			foreach (Passagem p in lista)
			{
				if (p.X != cx || p.Y != cy) continue;
				Atravessar(pl, p);
				break;
			}
		}
	}

	private void Atravessar(ServerPlayer pl, Passagem p)
	{
		// A CARENCIA E POSTA ANTES DA MUDANCA, e no jogador -- a saida do outro lado costuma cair
		// em cima da entrada de volta, e sem isto o corpo ricochetearia entre os dois mapas.
		_acabouDeAtravessar[pl.Id] = NowMs() + MsDeCarenciaDePassagem;

		string destino = p.Nome.Length > 0 ? p.Nome : p.Zona;
		Avisar(pl, $"você atravessa e chega em {destino}.");
		GD.Print($"[server] {pl.Name}: {pl.Zone.Name} -> {p.Zona} ({destino})");

		MoveToZone(pl.Id, ZoneKey.Premade(p.Zona), new Vec2(p.Dx, p.Dy));
	}

	/// <summary>Esquece a carencia de quem saiu -- senao o dicionario cresce a sessao inteira.</summary>
	private void EsquecerPassagem(int id) => _acabouDeAtravessar.Remove(id);
}
