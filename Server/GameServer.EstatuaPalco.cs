using Jandirus.Core.Magic;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// AS PORTAS DE PALCO DO ROBO `--diagestatua` (2026-10-22): o menu da Estatua do Dragao na tela, com foto -- o dono pediu *"um menu bem
/// intuitivo e bem feito pro jogador q criou o set escolher os desejos, a quantidade, a força etc"*. O palco so poe o corpo no lugar de
/// quem ergue (um Namekuseijin do Cla do Dragao que aprendeu a estatua, dono de um planeta fora da Terra e de Namek); o resto -- o botao da
/// aba World, a janela, o `db_estatua`, a janela de desejos e o `db_refazer` -- e o caminho de producao.
/// </summary>
public partial class GameServer
{
	/// <summary>O mundo do palco: pre-feito, fora da Terra (o Shenlong) e de Namek (o Porunga) -- e la o nome do dragao e livre.</summary>
	internal const string PlanetaDoPalcoES = "Vegeta";

	/// <summary>
	/// O CRIADOR: o corpo vira Namekuseijin do Cla do Dragao que aprendeu a estatua, com zeni pro desejo supremo, e DOMINA Vegeta (a
	/// bandeira posta pela bancada -- conquistar e medido pela `--conquistateste`); a estatua que houver la sai. Depois ele vai pra la, e
	/// a ficha lenta e o livro saem de novo (a raca, a classe e a skill sao o requisito do botao no cliente). Devolve se armou.
	/// </summary>
	internal bool PalcoES_Criador(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return false;
		ZoneKey mundo = ZoneKey.Premade(PlanetaDoPalcoES);
		PalcoES_TirarAEstatua(mundo);

		pl.Race = "Namekian";
		pl.Class = pl.Ficha.Class = "Dragon clan";
		pl.Livro ??= new Jandirus.Core.Skills.SkillBook();
		if (!pl.Livro.Sabe(Jandirus.Core.Skills.AcendedoresDoPort.EstatuaDoDragao)) pl.Livro.Dar(Jandirus.Core.Skills.AcendedoresDoPort.EstatuaDoDragao);
		pl.Ficha.Zeni = Math.Max(pl.Ficha.Zeni, 3 * Desejos.PrecoDoSupremo);
		pl.SigAtributos = "";
		MandarSkills(pl);

		_dominios.RemoveAll(d => d.PreFeito && string.Equals(d.Planeta, PlanetaDoPalcoES, StringComparison.OrdinalIgnoreCase));
		_dominios.Add(new Dominio { PreFeito = true, Planeta = PlanetaDoPalcoES, Assinatura = pl.Assinatura, Conta = pl.Conta, Nome = pl.Name });

		MoveToZone(pl.Id, mundo, PontoDeNascimento(mundo));
		return true;
	}

	/// <summary>A estatua do mundo deste corpo como o servidor a tem (nome, pedidos, desligados, supremo), ou nula.</summary>
	internal (string Nome, int Pedidos, string Desligados, bool Supremo, int Esferas)? PalcoES_SetDaqui(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _sets.Find(x => !x.Eterno && x.Zona.Hash == pl.Zone.Hash) is not { } s) return null;
		return (s.NomeDoDragao, s.Desejos, string.Join(",", s.Desligados.OrderBy(x => x, StringComparer.Ordinal)), s.TemSupremo,
				_esferas.Count(e => e.Set == s.Id));
	}

	/// <summary>O zeni do corpo agora -- o supremo e cobrado na hora de erguer.</summary>
	internal double PalcoES_Zeni(int id) => _players.TryGetValue(id, out ServerPlayer? pl) ? pl.Ficha.Zeni : -1;

	/// <summary>
	/// O DRAGAO DE PE pra foto da janela de desejos: o set acordado, com as sete na mao do corpo, e o `db_invocar` de producao. Devolve se
	/// subiu.
	/// </summary>
	internal bool PalcoES_Invocar(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || _sets.Find(x => !x.Eterno && x.Zona.Hash == pl.Zone.Hash) is not { } s) return false;
		s.AtivoEm = TempoDoMundo - 1;
		s.Inerte = false;
		s.Pedidos = 0;
		foreach (Esfera e in _esferas.Where(x => x.Set == s.Id))
		{
			e.Portador = pl.Id;
			e.PorZona(pl.Zone);
			e.PrecisaAssentar = false;
		}
		MandarEsferas(pl.Zone);
		MandarMochila(pl);
		InvocarODragao(pl);
		return _invocacoes.ContainsKey(s.Id);
	}

	/// <summary>O fim: o dragao vai embora, a estatua do palco e as esferas dela saem, e a bandeira plantada tambem.</summary>
	internal void PalcoES_Encerrar()
	{
		PalcoES_TirarAEstatua(ZoneKey.Premade(PlanetaDoPalcoES));
		_dominios.RemoveAll(d => d.PreFeito && string.Equals(d.Planeta, PlanetaDoPalcoES, StringComparison.OrdinalIgnoreCase));
	}

	private void PalcoES_TirarAEstatua(ZoneKey mundo)
	{
		foreach (SetDeEsferas s in _sets.Where(x => !x.Eterno && x.Zona.Hash == mundo.Hash).ToList())
		{
			if (_invocacoes.ContainsKey(s.Id)) FecharAInvocacao(s.Id, "");
			_esferas.RemoveAll(e => e.Set == s.Id);
			_sets.Remove(s);
		}
		SalvarEsferas();
		MandarEsferas(mundo);
	}
}
