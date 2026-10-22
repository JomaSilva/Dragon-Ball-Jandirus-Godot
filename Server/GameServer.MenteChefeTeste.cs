using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// BANCADA DA ETAPA NA MENTE -- `--mentechefeteste` (2026-10-21). O dono escolhe a forma do chefe
/// que ja viu, e a lembranca nao sobe alem da ultima forma vista. Cada regra tem o defeito injetado
/// na propria linha, e a mesma regua reprova com ele ligado.
///
///     Godot --headless --server --port 10200 --mentechefeteste
/// </summary>
public partial class GameServer
{
	private const int IdBaseDaEtapa = 102_700;

	private int _mcfOk, _mcfFalhou;

	private void AfirmarMcf(string oque, bool passou, string detalhe = "")
	{
		if (passou) { _mcfOk++; GD.Print($"[mentechefe]   OK    {oque}"); return; }
		_mcfFalhou++;
		GD.PrintErr($"[mentechefe]   FALHA {oque}   {detalhe}");
	}

	/// <summary>No boot, depois dos moldes, como a `--menteteste`.</summary>
	public void RodarBancadaDaEtapaDoChefe()
	{
		_mcfOk = _mcfFalhou = 0;
		GD.Print("[mentechefe] ================ A ETAPA DO CHEFE NA MENTE ================");
		var escutaAntes = EscutaDeEscolhas;
		var roteiroAntes = EscutaDoRoteiro;
		EscutaDeEscolhas = [];
		EscutaDoRoteiro = [];
		var forjados = new List<ServerPlayer>();
		var npcs = new List<ServerPlayer>();

		ZoneKey fora = ZoneKey.Premade(
			Espaco.PreFeitos().Select(p => p.Nome)
				.FirstOrDefault(n => !_players.Values.Any(pl => string.Equals(pl.Zone.Name, n, StringComparison.OrdinalIgnoreCase)))
			?? "Namek");

		ServerPlayer Forjar(int i, string nome, ZoneKey zona, Vec2 pos)
		{
			var novo = new ServerPlayer
			{
				Id = IdBaseDaEtapa + i,
				Peer = null,
				Name = nome,
				Race = "Human",
				Genero = "Male",
				Idade = 25,
				Zone = zona,
				Pos = pos,
				Conta = "bancada_etapa_" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
				Slot = 0,
				Ficha = new Fighter { Race = "Human", BP = 100_000 },
			};
			novo.Ficha.Class = "Normal";
			PorNoMundo(novo);
			forjados.Add(novo);
			return novo;
		}

		try
		{
			if (_moldes?.Get("freeza_namek") is not { EhChefe: true } freeza || _moldes.Get("cell") is not { EhChefe: true } cell
				|| _moldes.Get("freeza_vegeta") is not { EhChefe: true } vegeta || _moldes.Get("guardiao_saiyajin") is not { } guardiao)
			{
				AfirmarMcf("os moldes freeza_namek, cell, freeza_vegeta e guardiao_saiyajin existem", false, "catalogo");
			}
			else
			{
				OsNomesDasEtapas(freeza, cell, guardiao);
				OQueFoiVisto(Forjar, fora, npcs);
				AEscolhaEONascimento(Forjar, vegeta, cell, freeza);
				OTeto(Forjar, freeza);
				OsDoisFreeza(vegeta, freeza);
				OSaveAntigo(Forjar);
			}
		}
		catch (Exception e)
		{
			AfirmarMcf("a bancada rodou inteira (estourou: " + e.GetType().Name + ")", false, e.Message);
		}
		finally
		{
			EtapaVistaNaoGravaDeTeste = false;
			VariasEtapasNascemSemPerguntarDeTeste = false;
			NasceSemAEtapaEscolhidaDeTeste = false;
			LembrancaSobeAlemDoVistoDeTeste = false;
			NomeDeChefeRepetidoDeTeste = false;
			SaveAntigoSemEtapaDeTeste = false;
			EscutaDeEscolhas = escutaAntes;
			EscutaDoRoteiro = roteiroAntes;
			foreach (ServerPlayer p in forjados)
			{
				if (p.CloneId != 0 && _players.TryGetValue(p.CloneId, out ServerPlayer? clone)) RemoverNpc(clone);
				ZoneList(p.Zone.Hash).Remove(p);
				_players.Remove(p.Id);
			}
			foreach (ServerPlayer n in npcs)
				if (_players.ContainsKey(n.Id)) RemoverNpc(n);
			GD.Print($"[mentechefe] ================ {_mcfOk} OK, {_mcfFalhou} FALHA(S) ================");
		}
	}

	private void OsNomesDasEtapas(MoldeDeNpc freeza, MoldeDeNpc cell, MoldeDeNpc guardiao)
	{
		AfirmarMcf("Freeza de Namek: Forma 1, Forma 2, Forma 3, Forma Final e 100%",
			freeza.Estagios.Length == 5
			&& freeza.Estagios[0].Nome == "Forma 1" && freeza.Estagios[1].Nome == "Forma 2"
			&& freeza.Estagios[2].Nome == "Forma 3" && freeza.Estagios[3].Nome == "Forma Final"
			&& freeza.Estagios[4].Nome == "100%",
			string.Join(", ", freeza.Estagios.Select(e => e.Nome)));
		AfirmarMcf("Cell: Imperfeito, Semi-Perfeito, Perfeito, Super Perfeito",
			cell.Estagios.Length == 4
			&& cell.Estagios[0].Nome == "Imperfeito" && cell.Estagios[1].Nome == "Semi-Perfeito"
			&& cell.Estagios[2].Nome == "Perfeito" && cell.Estagios[3].Nome == "Super Perfeito",
			string.Join(", ", cell.Estagios.Select(e => e.Nome)));
		AfirmarMcf("guardiao: Base e Super Saiyajin",
			guardiao.Estagios.Length == 2 && guardiao.Estagios[0].Nome == "Base" && guardiao.Estagios[1].Nome == "Super Saiyajin",
			string.Join(", ", guardiao.Estagios.Select(e => e.Nome)));
	}

	private void OQueFoiVisto(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar, ZoneKey fora, List<ServerPlayer> npcs)
	{
		GD.Print("[mentechefe] --- o que foi visto ---");
		ulong lugar = 1_027_000;
		ServerPlayer? chefe = NascerNpc("freeza_namek", fora, new Vec2(2000, 2000), lugar);
		if (chefe?.Papel == null) { AfirmarMcf("o Freeza nasceu pra ser visto", false); return; }
		npcs.Add(chefe);

		ServerPlayer perto = forjar(1, "bancada: perto", fora, new Vec2(2096, 2000));
		ServerPlayer longe = forjar(2, "bancada: longe", fora, new Vec2(2000 + 40 * 32, 2000));
		TickDoRoteiro();
		AfirmarMcf("quem esta a 40 tiles nao grava etapa", !longe.EtapasDeChefe.ContainsKey("freeza_namek"));

		chefe.Papel.Estagio = 3;
		TickDoRoteiro();
		AfirmarMcf("MCF1. ver o chefe ja na forma grava essa etapa, sem precisar da cena da troca",
			perto.EtapasDeChefe.TryGetValue("freeza_namek", out HashSet<int>? a1) && a1.Contains(0) && a1.Contains(3),
			Juntas(perto, "freeza_namek"));

		ZoneList(chefe.Zone.Hash).Remove(chefe);
		chefe.Zone = DimensaoMental.De(IdBaseDaEtapa + 40);
		ZoneList(chefe.Zone.Hash).Add(chefe);
		chefe.Papel.Estagio = 4;
		ServerPlayer dentro = forjar(3, "bancada: na mente", chefe.Zone, chefe.Pos);
		TickDoRoteiro();
		AfirmarMcf("chefe dentro da mente nao vira etapa vista", !dentro.EtapasDeChefe.ContainsKey("freeza_namek"));
		AfirmarMcf("...e quem ja tinha visto nao ganha a forma que so existiu na mente",
			perto.EtapasDeChefe.TryGetValue("freeza_namek", out HashSet<int>? a2) && !a2.Contains(4));

		ServerPlayer fresco = forjar(4, "bancada: defeito", fora, new Vec2(2096, 2000));
		ZoneList(chefe.Zone.Hash).Remove(chefe);
		chefe.Zone = fora;
		chefe.Pos = new Vec2(2000, 2000);
		ZoneList(fora.Hash).Add(chefe);
		chefe.Papel.Estagio = 1;
		EtapaVistaNaoGravaDeTeste = true;
		try
		{
			TickDoRoteiro();
			bool gravou = fresco.EtapasDeChefe.TryGetValue("freeza_namek", out HashSet<int>? ax) && ax.Contains(1);
			AfirmarMcf("(defeito injetado: ver o chefe nao grava a etapa) a mesma regua REPROVA", !gravou);
		}
		finally { EtapaVistaNaoGravaDeTeste = false; }

		RemoverNpc(chefe);
		npcs.Remove(chefe);
	}

	private void AEscolhaEONascimento(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar, MoldeDeNpc vegeta, MoldeDeNpc cell, MoldeDeNpc freeza)
	{
		GD.Print("[mentechefe] --- a escolha e o nascimento ---");
		ZoneKey mente = DimensaoMental.De(IdBaseDaEtapa + 10);
		ServerPlayer dono = forjar(10, "bancada: dono", mente, new Vec2(400, 400));

		dono.ChefesVistos.Add(vegeta.Id);
		EscutaDeEscolhas!.Clear();
		ChamarChefe(dono, vegeta.Id);
		AfirmarMcf("com uma etapa so, nasce direto e nao pergunta",
			dono.CloneId != 0 && !EscutaDeEscolhas.Any(e => e.Itens.Count > 0)
			&& _players.TryGetValue(dono.CloneId, out ServerPlayer? um) && um.Papel?.Estagio == 0);
		AfirmarMcf("fora da propria mente a convocacao e recusada", RecusadaFora(dono));

		ServerPlayer visita = forjar(11, "bancada: visita", mente, dono.Pos + new Vec2(64, 0));
		visita.ChefesVistos.Add(cell.Id);
		visita.EtapasDeChefe[cell.Id] = [0, 2];
		int cloneDaVisita = visita.CloneId;
		ChamarChefe(visita, cell.Id);
		AfirmarMcf("visita nao convoca e nao recebe a lista", visita.CloneId == cloneDaVisita
			&& !EscutaDeEscolhas.Any(e => e.Quem == visita.Id && e.Itens.Count > 0));
		ZoneList(mente.Hash).Remove(visita);
		_players.Remove(visita.Id);

		dono.ChefesVistos.Add(cell.Id);
		dono.EtapasDeChefe[cell.Id] = [0, 1, 2];
		DesfazerOOponente(dono, "");
		EscutaDeEscolhas.Clear();
		EscutaDoRoteiro!.Clear();
		ChamarChefe(dono, cell.Id);
		List<string> lista = EscutaDeEscolhas.LastOrDefault(e => e.Verbo == EtapasDaMente.Verbo).Itens ?? [];
		string[] nomes = [.. lista.Select(EtapasDaMente.NomeDoItem)];
		AfirmarMcf("MCF2. com mais de uma etapa vista, a lista sai e ninguem nasce ainda",
			dono.CloneId == 0 && nomes.Length == 3
			&& nomes.Contains("Imperfeito") && nomes.Contains("Semi-Perfeito") && nomes.Contains("Perfeito")
			&& !nomes.Contains("Super Perfeito"),
			string.Join(" | ", lista));

		VariasEtapasNascemSemPerguntarDeTeste = true;
		try
		{
			EscutaDeEscolhas.Clear();
			ChamarChefe(dono, cell.Id);
			bool perguntou = EscutaDeEscolhas.Any(e => e.Itens.Count > 1) && dono.CloneId == 0;
			AfirmarMcf("(defeito injetado: varias etapas nascem sem perguntar) a mesma regua REPROVA", !perguntou);
		}
		finally
		{
			VariasEtapasNascemSemPerguntarDeTeste = false;
			DesfazerOOponente(dono, "");
		}

		EscutaDoRoteiro.Clear();
		ChamarChefe(dono, "cell:2");
		bool noPerfeito = _players.TryGetValue(dono.CloneId, out ServerPlayer? corpo)
			&& corpo.Papel?.Estagio == 2 && corpo.Papel.Degrau?.DegrauDoBio == 4 && EscutaDoRoteiro.Count == 0;
		AfirmarMcf("MCF3. nasce na etapa pedida, ja na forma, sem a cinematica", noPerfeito,
			corpo == null ? "sem corpo" : "estagio " + corpo.Papel?.Estagio + " bio " + corpo.Papel?.Degrau?.DegrauDoBio + " anuncios " + EscutaDoRoteiro.Count);

		int antes = dono.CloneId;
		ChamarChefe(dono, "cell:3");
		AfirmarMcf("etapa que nao viu e recusada", dono.CloneId == antes);

		DesfazerOOponente(dono, "");
		NasceSemAEtapaEscolhidaDeTeste = true;
		try
		{
			ChamarChefe(dono, "cell:2");
			bool naPedida = _players.TryGetValue(dono.CloneId, out ServerPlayer? torto) && torto.Papel?.Estagio == 2;
			AfirmarMcf("(defeito injetado: a etapa pedida e ignorada) a mesma regua REPROVA", !naPedida);
		}
		finally
		{
			NasceSemAEtapaEscolhidaDeTeste = false;
			DesfazerOOponente(dono, "");
		}

		dono.ChefesVistos.Add(freeza.Id);
		dono.EtapasDeChefe[freeza.Id] = [4];
		EscutaDeEscolhas.Clear();
		ChamarChefe(dono, freeza.Id);
		AfirmarMcf("MCF3b. so viu a forma final: nasce nela, sem lista",
			!EscutaDeEscolhas.Any(e => e.Itens.Count > 1)
			&& _players.TryGetValue(dono.CloneId, out ServerPlayer? fin) && fin.Papel?.Estagio == 4);
		DesfazerOOponente(dono, "");
	}

	private bool RecusadaFora(ServerPlayer dono)
	{
		ZoneKey mente = dono.Zone;
		ZoneList(mente.Hash).Remove(dono);
		dono.Zone = ZoneKey.Premade("Earth");
		ZoneList(dono.Zone.Hash).Add(dono);
		DesfazerOOponente(dono, "");
		ChamarChefe(dono, "freeza_vegeta");
		bool recusou = dono.CloneId == 0;
		ZoneList(dono.Zone.Hash).Remove(dono);
		dono.Zone = mente;
		ZoneList(mente.Hash).Add(dono);
		dono.CloneId = 0;
		return recusou;
	}

	private void OTeto(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar, MoldeDeNpc freeza)
	{
		GD.Print("[mentechefe] --- o teto ---");
		ZoneKey mente = DimensaoMental.De(IdBaseDaEtapa + 20);
		ServerPlayer dono = forjar(20, "bancada: teto", mente, new Vec2(500, 500));
		dono.ChefesVistos.Add(freeza.Id);
		dono.EtapasDeChefe[freeza.Id] = [0, 2];
		ChamarChefe(dono, "freeza_namek:0");
		if (!_players.TryGetValue(dono.CloneId, out ServerPlayer? chefe) || chefe.Papel == null)
		{
			AfirmarMcf("MCF4. a lembranca do Freeza nasceu no degrau 1 pra medir o teto", false, "sem corpo");
			AfirmarMcf("MCF5. no teto o golpe letal nao vira a forma seguinte", false, "sem corpo");
			return;
		}

		for (int passo = 0; passo < 3 && chefe.Papel.GatilhoAtual >= 0; passo++)
		{
			PorPiorMembroEm(chefe, chefe.Papel.GatilhoAtual - 0.01);
			TickDoRoteiro();
		}
		AfirmarMcf("MCF4. sobe por dano so ate a ultima etapa vista", chefe.Papel.Estagio == 2, "estagio " + chefe.Papel.Estagio);

		int noTeto = chefe.Papel.Estagio;
		bool negou = NegarMorteDoChefe(chefe);
		AfirmarMcf("MCF5. no teto o golpe letal nao vira a forma seguinte", !negou && chefe.Papel.Estagio == noTeto,
			"negou " + negou + " estagio " + chefe.Papel.Estagio);

		DesfazerOOponente(dono, "");
		ChamarChefe(dono, "freeza_namek:2");
		if (!_players.TryGetValue(dono.CloneId, out ServerPlayer? deNovo) || deNovo.Papel == null)
		{
			AfirmarMcf("(defeito injetado: a lembranca sobe alem do visto) a mesma regua REPROVA", false, "sem corpo");
			return;
		}
		LembrancaSobeAlemDoVistoDeTeste = true;
		try
		{
			int antes = deNovo.Papel.Estagio;
			bool subiu = NegarMorteDoChefe(deNovo);
			AfirmarMcf("(defeito injetado: a lembranca sobe alem do visto) a mesma regua REPROVA",
				subiu && deNovo.Papel.Estagio == antes + 1, "estagio " + deNovo.Papel.Estagio);
		}
		finally
		{
			LembrancaSobeAlemDoVistoDeTeste = false;
			DesfazerOOponente(dono, "");
		}
	}

	private void OsDoisFreeza(MoldeDeNpc vegeta, MoldeDeNpc namek)
	{
		GD.Print("[mentechefe] --- os dois Freeza ---");
		MoldeDeNpc[] juntos = [vegeta, namek];
		string a = RotuloDoChefeNaMente(vegeta, juntos);
		string b = RotuloDoChefeNaMente(namek, juntos);
		AfirmarMcf("MCF6. os dois Freeza tem botoes diferentes",
			a == "Freeza (Planeta Vegeta)" && b == "Freeza (Namek)", a + " | " + b);
		NomeDeChefeRepetidoDeTeste = true;
		try
		{
			string a2 = RotuloDoChefeNaMente(vegeta, juntos);
			string b2 = RotuloDoChefeNaMente(namek, juntos);
			AfirmarMcf("(defeito injetado: nome de chefe repetido) a mesma regua REPROVA", a2 == b2 && a2 == "Freeza", a2 + " | " + b2);
		}
		finally { NomeDeChefeRepetidoDeTeste = false; }
	}

	private void OSaveAntigo(Func<int, string, ZoneKey, Vec2, ServerPlayer> forjar)
	{
		GD.Print("[mentechefe] --- o save antigo ---");
		ServerPlayer pl = forjar(30, "bancada: save", ZoneKey.Premade("Earth"), new Vec2(0, 0));
		pl.ChefesVistos.Add("cell");
		pl.ChefesVistos.Add("freeza_namek");
		AplicarEtapasDoSave(pl, null);
		bool ganhou = pl.EtapasDeChefe.TryGetValue("cell", out HashSet<int>? c) && c.SetEquals([0])
			&& pl.EtapasDeChefe.TryGetValue("freeza_namek", out HashSet<int>? f) && f.SetEquals([0]);
		AfirmarMcf("MCF7. o save antigo ganha a etapa 1 de cada chefe ja visto", ganhou, Juntas(pl, "cell") + " / " + Juntas(pl, "freeza_namek"));

		pl.EtapasDeChefe["freeza_namek"] = [4];
		AplicarEtapasDoSave(pl, EtapasDaMente.Gravar(pl.EtapasDeChefe));
		AfirmarMcf("save novo que so viu a 100% nao ganha a etapa 1 por cima",
			pl.EtapasDeChefe.TryGetValue("freeza_namek", out HashSet<int>? so) && so.SetEquals([4]) && !so.Contains(0));

		SaveAntigoSemEtapaDeTeste = true;
		try
		{
			pl.EtapasDeChefe.Clear();
			AplicarEtapasDoSave(pl, null);
			bool ainda = pl.EtapasDeChefe.TryGetValue("cell", out HashSet<int>? v) && v.Contains(0);
			AfirmarMcf("(defeito injetado: o save antigo nao ganha a etapa 1) a mesma regua REPROVA", !ainda);
		}
		finally { SaveAntigoSemEtapaDeTeste = false; }
	}

	/// <summary>O robo (--diagetapa) planta as lembrancas no jogador de verdade, ja dentro da mente.</summary>
	internal void PalcoMcf_Lembrancas(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl)) return;
		pl.ChefesVistos.Add("cell");
		pl.ChefesVistos.Add("freeza_namek");
		pl.ChefesVistos.Add("freeza_vegeta");
		pl.EtapasDeChefe["cell"] = [0, 1, 2];
		pl.EtapasDeChefe["freeza_namek"] = [0, 1, 2, 3, 4];
		pl.EtapasDeChefe["freeza_vegeta"] = [0];
		MandarChefesDaMente(pl);
	}

	/// <summary>"molde|estagio|nome|bioN|corpo|forma", ou "sem lembranca".</summary>
	internal string PalcoMcf_Estado(int id)
	{
		if (!_players.TryGetValue(id, out ServerPlayer? pl) || pl.CloneId == 0) return "sem lembranca";
		if (!_players.TryGetValue(pl.CloneId, out ServerPlayer? c) || c.Papel?.Degrau == null) return "sem lembranca";
		EstagioDeChefe e = c.Papel.Degrau;
		return c.Papel.Molde.Id + "|" + c.Papel.Estagio + "|" + e.Nome + "|bio" + e.DegrauDoBio + "|" + e.Corpo + "|" + e.Forma;
	}

	private static string Juntas(ServerPlayer pl, string molde) =>
		pl.EtapasDeChefe.TryGetValue(molde, out HashSet<int>? s) ? string.Join(",", s.OrderBy(n => n)) : "-";
}
