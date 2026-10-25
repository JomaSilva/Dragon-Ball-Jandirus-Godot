using Godot;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O palco do robo <c>--diagkaikai</c> (2026-10-23). O robo e o servidor rodam no mesmo processo (<c>--host</c>). Quem decide o salto
/// continua sendo o verbo de producao (`KaiKaiG11`) e o tique de producao (`TickDoKaiKai`, que aqui roda no tique de verdade); o palco
/// so monta a cena: a skill, o Ki, a carona colada, o mundo do livro e o Kaioshin forjado que salta pra quem olha ver.
/// </summary>
public partial class GameServer
{
	private readonly Dictionary<string, ServerPlayer> _corposDoKaiKai = new(StringComparer.Ordinal);

	/// <summary>O mundo gerado do livro de batismo que o robo usa (o menor perto da Terra -- nasce rapido).</summary>
	private PlanetaNoEspaco? _mundoDoPalcoKK;

	/// <summary>O batismo do mundo do palco: e o nome que a foto da tela tem de mostrar.</summary>
	internal const string BatismoDoPalcoKK = "Kaiopolis";

	/// <summary>
	/// A cena: quem joga ganha o Kai Kai (o livro e o pacote de skills, que e o que faz o botao do verbo nascer no cliente), o Ki cheio,
	/// uma CARONA forjada a 1 tile, e o menor mundo gerado perto da Terra anotado no livro de batismo como se alguem o tivesse pisado e
	/// batizado (so na memoria). O mundo NAO e gerado aqui: o salto do robo tem de esperar ele nascer, como no jogo.
	/// </summary>
	internal bool PalcoKK_Montar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return false;
		BatismosSoNaMemoriaDeTeste = true;
		eu.Livro ??= new Jandirus.Core.Skills.SkillBook();
		if (!eu.Livro.Sabe(PathDoKaiKaiKK)) eu.Livro.Dar(PathDoKaiKaiKK);
		MandarSkills(eu, forcar: true);
		PalcoKK_Encher(id);

		if (AcharMundosDoKK() is not { } m) return false;
		_mundoDoPalcoKK = m.A;
		AnotarNoLivroKK(m.A, BatismoDoPalcoKK);

		_corposDoKaiKai.Clear();
		ServerPlayer carona = ForjarComSkills("Carona", eu.Pos + new Vec2(ZoneCollision.TileSize, 0), 1_000);
		carona.Conta = "palco_kk_carona";
		// (a forja poe o corpo na Terra; o campo de teste tambem e na Terra pra quem nasce la, mas a zona e a de quem joga, seja qual for)
		if (!carona.Zone.Equals(eu.Zone))
		{
			ZoneList(carona.Zone.Hash).Remove(carona);
			carona.Zone = eu.Zone;
			ZoneList(eu.Zone.Hash).Add(carona);
		}
		_corposDoKaiKai["Carona"] = carona;
		TrocarAparencias(carona);
		GD.Print($"[kaikai] palco: {eu.Name} com o Kai Kai, carona colada, {m.A.Nome} no livro como {BatismoDoPalcoKK}");
		return true;
	}

	/// <summary>Ki cheio e o espaco recomposto (a recarga de 1 s de todo salto de planeta): pronto pra apertar de novo.</summary>
	internal void PalcoKK_Encher(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return;
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		_saltoPronto.Remove(id);
	}

	/// <summary>A chave do mundo do livro (`#seed`), a que o botao dele manda.</summary>
	internal string PalcoKK_ChaveDoGerado() => _mundoDoPalcoKK is { } p ? ChaveDePlaneta.De(p).Texto : "";

	/// <summary>A zona do mundo do livro (o robo compara com a zona do cliente).</summary>
	internal ZoneKey PalcoKK_ZonaDoGerado() => _mundoDoPalcoKK is { } p ? Espaco.ZonaDe(p) : default;

	/// <summary>Quem joga esta no ponto de chegada que o gerador garantiu livre (a uma folga de um tile)?</summary>
	internal bool PalcoKK_NaChegadaDoGerado(int id) =>
		_players.TryGetValue(id, out ServerPlayer? eu) && _mundoDoPalcoKK is { } p
		&& ChegadaDaZonaGerada(Espaco.ZonaDe(p)) is { } ch && eu.Zone.Equals(Espaco.ZonaDe(p))
		&& Vec2.Distance(eu.Pos, ch) <= ZoneCollision.TileSize;

	internal int PalcoKK_Id(string chave) => _corposDoKaiKai.TryGetValue(chave, out ServerPlayer? o) ? o.Id : 0;

	/// <summary>Distancia em pixels entre quem joga e um corpo do palco; 99999 em outra zona, negativa se nao ha.</summary>
	internal float PalcoKK_Distancia(int id, string chave)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu) || !_corposDoKaiKai.TryGetValue(chave, out ServerPlayer? o)) return -1;
		if (o.Zone.Hash != eu.Zone.Hash) return 99999;
		return Vec2.Distance(eu.Pos, o.Pos);
	}

	/// <summary>
	/// Este corpo esta no BERCO do planeta em que esta (a um tile de folga) e em chao firme? E a chegada dos doze de sempre desde
	/// 2026-10-24 (`ChegadaDoSaltoDePlaneta`). O detalhe diz a celula, o que ha nela e a distancia ate o berco.
	/// </summary>
	internal bool PalcoKK_NoBerco(int id, out string detalhe)
	{
		detalhe = "sem corpo";
		if (!_players.TryGetValue(id, out ServerPlayer? o)) return false;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(o.Zone);
		Vec2 berco = PontoDeNascimento(o.Zone);
		float d = Vec2.Distance(o.Pos, berco);
		detalhe = $"{o.Zone.Name} {CelulaEmTextoKK(mapa, o.Pos)}, a {d:0} px do berco {CelulaEmTextoKK(mapa, berco)}";
		return NoChaoKK(mapa, o.Pos) && d <= ZoneCollision.TileSize;
	}

	/// <summary>
	/// Quem joga da uns passos pra um lado, e a carona pra outro: quem chega pelo Kai Kai cai no ponto de chegada, e por cima de alguem a
	/// foto nao diria nada. Os lados sao os primeiros, de oeste, leste, norte e sul, em que ha chao a pe -- o berco de Namek e dentro de
	/// uma casa, e um passo cego entra na parede.
	/// </summary>
	internal void PalcoKK_DePasso(int id, int tiles)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(eu.Zone);
		Vec2 ponto = eu.Pos;
		float t = tiles * ZoneCollision.TileSize;
		List<Vec2> lados = [.. new[] { new Vec2(-t, 0), new Vec2(t, 0), new Vec2(0, -t), new Vec2(0, t) }
			.Select(v => ponto + v).Where(p => NoChaoKK(mapa, p))];
		if (lados.Count == 0) lados = [ponto + new Vec2(t, 0), ponto - new Vec2(t, 0)];
		CravarPosicao(eu, lados[0]);
		if (_corposDoKaiKai.TryGetValue("Carona", out ServerPlayer? c) && c.Zone.Hash == eu.Zone.Hash)
			CravarPosicao(c, lados.Count > 1 ? lados[1] : ponto);
	}

	/// <summary>
	/// O KAIOSHIN DO PALCO SALTA (forjado na Terra na primeira vez, com a skill): Ki cheio, a recarga zerada e o verbo de producao com
	/// o destino. O salto anda no tique de verdade -- 0,2 s piscando, some, chega e pisca 0,1 s depois. Devolve o id dele.
	/// </summary>
	internal int PalcoKK_KaioshinSalta(string destino)
	{
		if (!_corposDoKaiKai.TryGetValue("Kaioshin", out ServerPlayer? k) || !_players.ContainsKey(k.Id))
		{
			ZoneKey terra = ZoneKey.Premade("Earth");
			k = ForjarComSkills("Kaioshin", PontoDeNascimento(terra), 5_000, [PathDoKaiKaiKK], efeitosDeSkill: true);
			k.Conta = "palco_kk_kaioshin";
			_corposDoKaiKai["Kaioshin"] = k;
			TrocarAparencias(k);
		}
		k.Ficha.Ki = k.Ficha.MaxKi;
		_saltoPronto.Remove(k.Id);
		UsarHabilidade(k, "Kai_Kai:" + destino);
		return k.Id;
	}

	/// <summary>Em que zona esta o Kaioshin do palco ("" = nao ha).</summary>
	internal string PalcoKK_ZonaDoKaioshin() => _corposDoKaiKai.TryGetValue("Kaioshin", out ServerPlayer? k) ? k.Zone.Name : "";

	internal void PalcoKK_Limpar(int id)
	{
		KaiKaiSomeNaHoraDeTeste = KaiKaiClaraoNaHoraDeTeste = false;
		if (_players.TryGetValue(id, out ServerPlayer? eu) && eu.Livro?.Sabe(PathDoKaiKaiKK) == true) eu.Livro.Esquecer(PathDoKaiKaiKK);
		_kaiKaiEmCurso.Clear();
		_corposDoKaiKai.Clear();
	}
}
