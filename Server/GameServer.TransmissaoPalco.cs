using Godot;
using Jandirus.Core.Skills;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// O palco do robo <c>--diagteleporte</c>. O robo e o servidor rodam no mesmo processo (<c>--host</c>).
/// Quem decide o teleporte continua sendo o verbo de producao; aqui so se monta a cena e se vence o relogio.
/// </summary>
public partial class GameServer
{
	private readonly Dictionary<string, ServerPlayer> _corposDaTransmissao = new(StringComparer.Ordinal);

	/// <summary>Sense e Instant Transmission no nivel do planeta, um Alvo e dois Gemeo de mesmo nome.</summary>
	internal bool PalcoIT_Montar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return false;
		PalcoSN_Sense(id, SenseEmNiveis.Planeta);
		eu.Livro ??= new Jandirus.Core.Skills.SkillBook();
		if (!eu.Livro.Sabe(TransmissaoEmNiveis.Path)) eu.Livro.Dar(TransmissaoEmNiveis.Path);
		eu.Niveis.Por(TransmissaoEmNiveis.Path, TransmissaoEmNiveis.Planeta);
		eu.Ficha.teleskill = 1;
		MandarSkills(eu, forcar: true);
		_corposDaTransmissao.Clear();
		Vec2 c = eu.Pos;
		ServerPlayer Conhecido(string chave, string nome, int dx, int dy)
		{
			ServerPlayer o = StCorpo(nome, Em(c, dx, dy), 9_000);
			o.Conta = "palco_it_" + chave;
			eu.Social.Fotografar(o.Assinatura, o.Name, o.Race, "Male", 25, NowMs());
			_corposDaTransmissao[chave] = o;
			return o;
		}
		Conhecido("Alvo", "Alvo", 8, 0);
		Conhecido("GemeoA", "Gemeo", 3, 3);
		Conhecido("GemeoB", "Gemeo", 3, -3);
		Conhecido("Junto", "Junto", 1, 0);
		Conhecido("DePe", "DePe", 0, 1);
		foreach (ServerPlayer o in _corposDaTransmissao.Values) TrocarAparencias(o);
		TickDosSentidos();
		return true;
	}

	internal int PalcoIT_Id(string chave) =>
		_corposDaTransmissao.TryGetValue(chave, out ServerPlayer? o) ? o.Id : 0;

	/// <summary>
	/// Vence a concentracao agora, pelo tique de producao. (2026-10-24) E a viagem inteira: a saida piscando e o clarao da chegada vencem
	/// junto -- quem os mede no relogio de verdade e o robo `--diagitmaestria`.
	/// </summary>
	internal bool PalcoIT_Vencer(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		if (!_transmissaoG11.TryGetValue(id, out TransmissaoG11? t)) return false;
		t.QuandoMs = NowMs() - 1;
		TickDaTransmissaoG11(pl, t, NowMs());
		TerminarATransmissaoDeTeste(id);
		return true;
	}

	/// <summary>A cena de quem medita junto: o robo e o Junto meditam a 1 tile; o DePe fica de pe; o Alvo a 8.</summary>
	internal void PalcoIT_CenaMeditando(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return;
		_transmissaoG11.Remove(id);
		Vec2 c = eu.Pos;
		if (_corposDaTransmissao.TryGetValue("Junto", out ServerPlayer? junto))
		{
			junto.Pos = Em(c, 1, 0);
			junto.Ficha.med = true;
			junto.Zone = eu.Zone;
		}
		if (_corposDaTransmissao.TryGetValue("DePe", out ServerPlayer? dePe))
		{
			dePe.Pos = Em(c, 0, 1);
			dePe.Ficha.med = false;
			dePe.Zone = eu.Zone;
		}
		if (_corposDaTransmissao.TryGetValue("Alvo", out ServerPlayer? alvo))
		{
			alvo.Pos = Em(c, 8, 0);
			alvo.Ficha.med = false;
			alvo.Zone = eu.Zone;
		}
		eu.Ficha.med = true;
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		TickDosSentidos();
	}

	/// <summary>A cena do agarrao: o robo segura o Junto (de pe) e o Alvo fica a 8 tiles.</summary>
	internal void PalcoIT_CenaAgarrao(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return;
		_transmissaoG11.Remove(id);
		eu.Ficha.med = false;
		if (eu.AgarrandoId != 0) Soltar(eu, MotivoDaSoltura.Tecla);
		Vec2 c = eu.Pos;
		if (_corposDaTransmissao.TryGetValue("Junto", out ServerPlayer? junto))
		{
			junto.Ficha.med = false;
			junto.Pos = Em(c, 1, 0);
			junto.Zone = eu.Zone;
			Prender(eu, junto);
		}
		if (_corposDaTransmissao.TryGetValue("Alvo", out ServerPlayer? alvo))
		{
			alvo.Pos = Em(c, 8, 0);
			alvo.Zone = eu.Zone;
		}
		eu.Ficha.Ki = eu.Ficha.MaxKi;
		TickDosSentidos();
	}

	/// <summary>Distancia em pixels entre o robo e um corpo do palco. Negativa se nao ha.</summary>
	internal float PalcoIT_Distancia(int id, string chave)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? eu)) return -1;
		if (!_corposDaTransmissao.TryGetValue(chave, out ServerPlayer? o)) return -1;
		if (o.Zone.Hash != eu.Zone.Hash) return 99999;
		return Vec2.Distance(eu.Pos, o.Pos);
	}

	internal void PalcoIT_Limpar(int id)
	{
		if (_players.TryGetValue(id, out ServerPlayer? eu))
		{
			eu.Ficha.med = false;
			if (eu.AgarrandoId != 0) Soltar(eu, MotivoDaSoltura.Tecla);
			if (eu.Livro?.Sabe(TransmissaoEmNiveis.Path) == true) eu.Livro.Esquecer(TransmissaoEmNiveis.Path);
			_transmissaoG11.Remove(id);
		}
		_corposDaTransmissao.Clear();
		PalcoSN_Limpar(id);
	}
}
