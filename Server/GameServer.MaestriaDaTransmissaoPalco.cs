using Jandirus.Core.Skills;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// O PALCO DO ROBO <c>--diagitmaestria</c> (a maestria do Instant Transmission, 2026-10-23). O robo e o servidor rodam no mesmo
/// processo (<c>--host</c>). Quem decide a viagem, a espera e a maestria continua sendo o verbo de producao (o botao da aba Sense);
/// aqui so se monta a cena, se poe a maestria de um veterano e se arma a luta.
/// </summary>
public partial class GameServer
{
	private readonly Dictionary<string, ServerPlayer> _corposDaMaestria = new(StringComparer.Ordinal);

	/// <summary>
	/// Sense e Instant Transmission no nivel do planeta (3 s de concentracao sem maestria), maestria 0, e dois conhecidos no campo de
	/// visao: o Aliado a 10 tiles e o Rival a uns 6 tiles (que vai brigar). O Rival tem metade do poder do jogador: o soco da luta
	/// nao pode derruba-lo.
	/// </summary>
	internal bool PalcoMIT_Montar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return false;
		PalcoSN_Sense(id, SenseEmNiveis.Planeta);
		// O personagem recem-criado tem uns 10 de poder: o Rival com metade disso ficaria abaixo do piso do Sense, e um mais forte
		// derrubaria o jogador no soco. Mil de poder pra cena (o `PalcoSN_Limpar` devolve o de antes).
		PalcoSN_MeuPoder(id, 1_000);
		eu.Livro ??= new SkillBook();
		if (!eu.Livro.Sabe(TransmissaoEmNiveis.Path)) eu.Livro.Dar(TransmissaoEmNiveis.Path);
		eu.Niveis.Por(TransmissaoEmNiveis.Path, TransmissaoEmNiveis.Planeta);
		eu.Ficha.teleskill = 1;
		eu.Ficha.MaestriaDaTransmissao = 0;
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		MandarSkills(eu, forcar: true);
		_corposDaMaestria.Clear();
		Vec2 c = eu.Pos;
		ServerPlayer Conhecido(string chave, int dx, int dy, double bp)
		{
			ServerPlayer o = StCorpo(chave, Em(c, dx, dy), bp);
			o.Conta = "palco_mit_" + chave.ToLowerInvariant();
			eu.Social.Fotografar(o.Assinatura, o.Name, o.Race, "Male", 25, NowMs());
			_corposDaMaestria[chave] = o;
			return o;
		}
		Conhecido("Aliado", 10, 0, 9_000);
		Conhecido("Rival", 5, 3, 500);
		foreach (ServerPlayer o in _corposDaMaestria.Values) TrocarAparencias(o);
		TickDosSentidos();
		return true;
	}

	internal int PalcoMIT_Id(string chave) =>
		_corposDaMaestria.TryGetValue(chave, out ServerPlayer? o) ? o.Id : 0;

	/// <summary>A maestria do jogador no servidor (a verdade, pra comparar com o cartao).</summary>
	internal double PalcoMIT_Maestria(int id) =>
		_players.TryGetValue(id, out ServerPlayer? pl) ? pl.Ficha.MaestriaDaTransmissao : -1;

	/// <summary>O VETERANO: a maestria posta a mao, como se ele ja tivesse viajado muito. O cartao a recebe no tique de 1 Hz do Sense.</summary>
	internal void PalcoMIT_PorMaestria(int id, double maestria)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.Ficha.MaestriaDaTransmissao = maestria;
		pl.Ficha.Ki = pl.Ficha.MaxKi;
	}

	/// <summary>Distancia em pixels entre o jogador e um corpo do palco (99999 em outra zona; negativa se nao ha).</summary>
	internal float PalcoMIT_Distancia(int id, string chave)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu) || !_corposDaMaestria.TryGetValue(chave, out ServerPlayer? o)) return -1;
		return o.Zone.Hash != eu.Zone.Hash ? 99999 : Vec2.Distance(eu.Pos, o.Pos);
	}

	/// <summary>
	/// A LUTA: o Rival vem pro lado do jogador e da um soco pela porta de producao (<see cref="Atacar"/>, a mesma do espaco) --
	/// o resolvedor arma a tag de combate dos dois lados, acerte ou nao. Devolve se os dois estao em combate.
	/// </summary>
	internal bool PalcoMIT_Luta(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu) || !_corposDaMaestria.TryGetValue("Rival", out ServerPlayer? rival)) return false;
		CravarPosicao(rival, Em(eu.Pos, 1, 0));
		rival.Ficha.Ki = rival.Ficha.MaxKi;
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		rival.AlvoId = eu.Id;
		Atacar(rival, Protocol.Golpe.Leve);
		return PalcoMIT_EmLuta(id);
	}

	/// <summary>O jogador e o Rival estao com a tag de combate acesa (a luta de 90 s do jogo).</summary>
	internal bool PalcoMIT_EmLuta(int id) =>
		_players.TryGetValue(id, out ServerPlayer? eu) && eu.Combate.EmCombate > 0
		&& _corposDaMaestria.TryGetValue("Rival", out ServerPlayer? rival) && rival.Combate.EmCombate > 0;

	/// <summary>Ha uma concentracao pendente do jogador (o botao chegou ao servidor)?</summary>
	internal bool PalcoMIT_Concentrando(int id) => _transmissaoG11.ContainsKey(id);

	/// <summary>
	/// (2026-10-24) O VIAJANTE, pra quem joga ver o clarao de FORA: um corpo forjado com o Instant Transmission no nivel 5 (o universo,
	/// pra alcancar outro planeta) e maestria cheia (0,2 s de concentracao), a 4 tiles de quem joga; e o Destino dele, em Namek, no berco
	/// do planeta. Os dois sao conhecidos de quem joga (o nome aparece na tela).
	/// </summary>
	internal bool PalcoMIT_Viajante(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return false;
		ServerPlayer Forjado(string chave, Vec2 onde, double bp, int nivel)
		{
			ServerPlayer o = ForjarComSkills(chave, onde, bp, skills: [TransmissaoEmNiveis.Path],
				degraus: [(TransmissaoEmNiveis.Path, nivel)], efeitosDeSkill: true);
			o.Conta = "palco_mit_" + chave.ToLowerInvariant();
			o.Ficha.teleskill = 1;
			o.Niveis.Por(TransmissaoEmNiveis.Path, nivel);
			// (a forja poe o corpo na Terra; a cena e a zona de quem joga, seja qual for)
			if (!o.Zone.Equals(eu.Zone))
			{
				ZoneList(o.Zone.Hash).Remove(o);
				o.Zone = eu.Zone;
				ZoneList(eu.Zone.Hash).Add(o);
			}
			eu.Social.Fotografar(o.Assinatura, o.Name, o.Race, "Male", 25, NowMs());
			_corposDaMaestria[chave] = o;
			return o;
		}
		ServerPlayer v = Forjado("Viajante", Em(eu.Pos, -4, 0), 9_000, TransmissaoEmNiveis.Universo);
		v.Ficha.MaestriaDaTransmissao = TransmissaoEmNiveis.MaestriaMaxima;
		ServerPlayer d = Forjado("Destino", Em(eu.Pos, 0, 3), 9_000, 1);
		ZoneKey namek = ZoneKey.Premade("Namek");
		MoveToZone(d.Id, namek, PontoDeNascimento(namek));
		foreach (ServerPlayer o in new[] { v, d })
		{
			o.Ficha.Ki = o.Ficha.MaxKi;
			o.Ficha.Tick(agoraMs: NowMs());
			TrocarAparencias(o);
		}
		return true;
	}

	/// <summary>
	/// O VIAJANTE VAI ate um corpo do palco ("Destino", em Namek; "Rival", aqui), pelo verbo de producao (`Instant_Transmission:#id`), com
	/// o Ki cheio. A concentracao, a saida piscando, o salto e o clarao da chegada andam no tique de verdade. Devolve o id dele.
	/// </summary>
	internal int PalcoMIT_ViajanteVai(string chaveDoAlvo)
	{
		if (!_corposDaMaestria.TryGetValue("Viajante", out ServerPlayer? v) || !_corposDaMaestria.TryGetValue(chaveDoAlvo, out ServerPlayer? alvo))
			return 0;
		v.Ficha.Ki = v.Ficha.MaxKi;
		UsarHabilidade(v, "Instant_Transmission:#" + alvo.Id);
		return v.Id;
	}

	internal void PalcoMIT_Limpar(int id)
	{
		MaestriaDoItNaoEncurtaDeTeste = false;
		MaestriaDoItSemPressaDePertoDeTeste = false;
		TransmissaoSomeNaHoraDeTeste = false;
		TransmissaoClaraoNaHoraDeTeste = false;
		if (_players.TryGetValue(id, out ServerPlayer? eu))
		{
			_transmissaoG11.Remove(id);
			_transmissoesEmCurso.Remove(id);
			eu.Ficha.MaestriaDaTransmissao = 0;
			if (eu.Livro?.Sabe(TransmissaoEmNiveis.Path) == true) eu.Livro.Esquecer(TransmissaoEmNiveis.Path);
		}
		_corposDaMaestria.Clear();
		PalcoSN_Limpar(id);
	}
}
