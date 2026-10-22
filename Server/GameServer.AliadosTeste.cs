using Godot;
using Jandirus.Core.Forms;
using Jandirus.Core.Items;
using Jandirus.Core.Npc;
using Jandirus.Core.Stats;
using Jandirus.Core.Tech;
using Jandirus.Core.World;
using Jandirus.Net;
using LiteNetLib.Utils;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA AMIZADE E DOS ALIADOS (`--aliadosteste`, 2026-10-21). Os corpos sao forjados, no boot, e cada
/// regra e lida pela producao: o verbo entra por `Verbo`, o pacote da aba sai pelo `FioDosAliados.Ler`, e cada
/// botao `*DeTeste` e ligado num try/finally pra mostrar que a mesma regua reprova.
///
///     Godot --headless -- --server --port 10140 --aliadosteste
/// </summary>
public partial class GameServer
{
	private int _amiOk, _amiFalhou;
	private int _lugarDosAliados = 92001;

	private void AfirmarAmi(string nome, bool cond, string detalhe = "")
	{
		if (cond) { _amiOk++; GD.Print($"[aliados]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
		else { _amiFalhou++; GD.PrintErr($"[aliados]   FALHA {nome}   [{detalhe}]"); }
	}

	/// <summary>Um corpo de jogador forjado (conta e slot dao a assinatura; sem tela). O id sai do `_nextId` de producao.</summary>
	private ServerPlayer ForjarJogadorDeAliado(string nome, int slot, Vec2 onde, double bp)
	{
		var novo = new ServerPlayer
		{
			Id = _nextId++,
			Peer = null,
			Name = nome,
			Race = "Human",
			Genero = "Male",
			Idade = 25,
			Class = "Normal",
			Zone = ZonaDaBancadaDeProjetil,
			Pos = onde,
			Conta = "bancada_aliados",
			Slot = slot,
			Ficha = new Fighter { Name = nome, Race = "Human", BP = bp, HP = 100, Class = "Normal" },
			Livro = new Jandirus.Core.Skills.SkillBook(),
			Visual = new Jandirus.Core.Appearance.Appearance(),
		};
		PorNoMundo(novo);
		novo.Ficha.Ki = novo.Ficha.MaxKi;
		novo.Ficha.Tick(agoraMs: NowMs());
		novo.Ficha.HP = 100;
		return novo;
	}

	/// <summary>Um chao de verdade, duas celulas lado a lado, pra o par da bancada nao nascer na agua.</summary>
	private Vec2 ChaoDaAmizade(ZoneKey zona)
	{
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) return new Vec2(16 * ZoneCollision.TileSize + 16, 16 * ZoneCollision.TileSize + 16);
		for (int y = 12; y < 220; y += 4)
			for (int x = 12; x < 220; x += 3)
				if (mapa.ServeDeChao(x, y) && mapa.ServeDeChao(x + 1, y))
					return new Vec2(x * ZoneCollision.TileSize + 16, y * ZoneCollision.TileSize + 16);
		return new Vec2(16 * ZoneCollision.TileSize + 16, 16 * ZoneCollision.TileSize + 16);
	}

	private ServerPlayer? NascerHabitante(string molde, Vec2 onde)
	{
		ServerPlayer? npc = NascerNpc(molde, ZonaDaBancadaDeProjetil, onde, (ulong)_lugarDosAliados++);
		if (npc == null) return null;
		npc.Ficha.HP = 100;
		npc.Ficha.Tick(agoraMs: NowMs());
		npc.Ficha.HP = 100;
		return npc;
	}

	/// <summary>Tira do alcance quem ja estava na zona (o povoamento), e guarda onde estava.</summary>
	private void AfastarQuemEstiverPerto(ServerPlayer pl, List<(ServerPlayer Corpo, Vec2 Onde)> guardar, params ServerPlayer[] meus)
	{
		foreach (ServerPlayer o in ZoneList(pl.Zone.Hash).ToList())
		{
			if (o == pl || meus.Contains(o) || !AoAlcanceDaTeclaE(pl, o)) continue;
			guardar.Add((o, o.Pos));
			o.Pos = pl.Pos + new Vec2(8000, 8000);
		}
	}

	private bool DarItem(ServerPlayer pl, ServerPlayer npc, Func<string, bool> qual, out double ganho, out List<string> falas)
	{
		double antes = LacoDe(npc, pl)?.Pontos ?? 0;
		EscutaDeEscolhas ??= [];
		EscutaDeEscolhas.Clear();
		falas = Ouvir(() => Verbo(pl, Amizade.VerboPresente, ""));
		string rotulo = "";
		foreach ((int _, string _, List<string> itens) in EscutaDeEscolhas)
			foreach (string item in itens)
				if (rotulo.Length == 0 && qual(item)) rotulo = item;
		if (rotulo.Length == 0) { ganho = 0; return false; }
		falas = Ouvir(() => Verbo(pl, Amizade.VerboPresente, rotulo));
		ganho = (LacoDe(npc, pl)?.Pontos ?? 0) - antes;
		return true;
	}

	private string PoderNoPacote(ServerPlayer pl)
	{
		EscutaDosAliados ??= [];
		EscutaDosAliados.Clear();
		MandarAliados(pl, forcar: true);
		if (EscutaDosAliados.Count == 0) return "";
		var r = new NetDataReader(EscutaDosAliados[^1].Pacote);
		r.GetByte();
		AliadosState s = FioDosAliados.Ler(r);
		return s.Aliados.Count == 0 ? "" : s.Aliados[0].Poder;
	}

	private void IgualarPoder(ServerPlayer a, ServerPlayer b)
	{
		double poder = Math.Max(1, Math.Max(a.Ficha.expressedBP, b.Ficha.expressedBP));
		a.Ficha.expressedBP = b.Ficha.expressedBP = poder;
		a.Ficha.HP = b.Ficha.HP = 100;
	}

	private void SoltarOsBotoesDosAliados()
	{
		HabitanteSemMenuDeTeste = false;
		MenuAbertoSemFolgaDeTeste = false;
		TreinoDeAmizadeEhDerrotaDeTeste = false;
		TreinoSemMedoDeTeste = false;
		TreinoComEspeciaisDeTeste = false;
		TreinoSemMetadeDeTeste = false;
		ApanharNaoZeraDeTeste = false;
		RecrutaSemAmizadeDeTeste = false;
		AmizadeHerdaAVagaDeTeste = false;
		AliadoVoltaAoMorrerDeTeste = false;
		AliadoForaDoLutoDeTeste = false;
		PoderDoAliadoEmNumeroDeTeste = false;
		Amizade.QualquerHabitanteDeTeste = false;
		Amizade.SemRetornoDecrescenteDeTeste = false;
		EscutaDosAliados = null;
		EscutaDeEscolhas = null;
		EscutaDeAvisos = null;
	}

	public void RodarBancadaDosAliados()
	{
		_amiOk = _amiFalhou = 0;
		GD.Print("[aliados] ================ AMIZADE, TREINO E ALIADOS (2026-10-21) ================");
		var afastados = new List<(ServerPlayer Corpo, Vec2 Onde)>();
		var criados = new List<ServerPlayer>();
		string sig = "";
		ZoneKey zona = ZonaDaBancadaDeProjetil;
		try
		{
			EscutaDosAliados = [];

			// ---------------------------------------------------------------- 1. a regra pura
			GD.Print("[aliados] -- 1) o valor do presente, quem entra, o retorno --");
			ItemDef senzu = CatalogoDeItens.Get(CatalogoDeItens.Senzu)!;
			ItemDef maca = CatalogoDeItens.Get(CatalogoDeItens.Maca)!;
			ItemDef scouter = CatalogoDeItens.Get(CatalogoDeItens.Scouter)!;
			AfirmarAmi("a Senzu vale 20, de proposito acima da comida", Amizade.ValorDoPresente(CatalogoDeItens.Senzu, senzu, 0) == Amizade.PontosDaSenzu);
			AfirmarAmi("a maca (nutricao 6) vale 3, no teto da comida",
					  Math.Abs(Amizade.ValorDoPresente(CatalogoDeItens.Maca, maca, 0) - 3) < 0.01);
			AfirmarAmi("sem preco e sem nutricao o item nao e presente", Amizade.ValorDoPresente(CatalogoDeItens.Scouter, scouter, 0) == 0);
			AfirmarAmi("o Scouter (8.000 zeni) vale cerca de 9,5, e nada passa do teto",
					  Math.Abs(Amizade.ValorDoPresente(CatalogoDeItens.Scouter, scouter, 8000) - 9.515) < 0.05
					  && Amizade.ValorDoPresente(CatalogoDeItens.Scouter, scouter, 1_000_000_000) == Amizade.TetoDoPresente);
			AfirmarAmi("capsula com gaveta, Potara e o que tem '|' nao se dao",
					  !Amizade.DaPraDar("PeeKay|c1|2", scouter, 8000)
					  && !Amizade.DaPraDar(CatalogoDeItens.BrincosPotara, CatalogoDeItens.Get(CatalogoDeItens.BrincosPotara), 1000)
					  && Amizade.DaPraDar(CatalogoDeItens.Maca, maca, 0));
			AfirmarAmi("poder desigual e a mesma recusa do spar entre habitantes (acima do limiar, ou abaixo do inverso)",
					  Amizade.PoderDesigual(4, 3) && !Amizade.PoderDesigual(2, 3) && Amizade.PoderDesigual(0.2, 3));
			AfirmarAmi("so o cidadao entra; o Rei de Vegeta nao",
					  Amizade.MoldeRecrutavel("cidadao") && !Amizade.MoldeRecrutavel("rei_vegeta") && !Amizade.MoldeRecrutavel("alma"));
			AfirmarAmi("o segundo gesto do dia vale a metade",
					  Math.Abs(Amizade.Ganho(10, 0) - 10) < 0.01 && Math.Abs(Amizade.Ganho(10, 1) - 5) < 0.01
					  && Math.Abs(Amizade.Ganho(10, 2) - 2.5) < 0.01);
			Interacoes.Acao[] hab = Interacoes.De(Amizade.TipoDoHabitante);
			Interacoes.Acao[] amigo = Interacoes.De(Amizade.TipoDoAmigo);
			Interacoes.Acao[] noTreino = Interacoes.De(Amizade.TipoEmTreino);
			AfirmarAmi("o menu do habitante e o presente e o treino; o Recrutar so no laco cheio; no treino, a saida",
					  hab.Any(a => a.Verbo == Amizade.VerboPresente) && hab.Any(a => a.Verbo == Amizade.VerboTreinar)
					  && hab.All(a => a.Verbo != Amizade.VerboRecrutar)
					  && amigo.Any(a => a.Verbo == Amizade.VerboRecrutar)
					  && noTreino.Any(a => a.Verbo == Amizade.VerboParar));

			Amizade.SemRetornoDecrescenteDeTeste = true;
			try
			{
				AfirmarAmi("A12. (defeito injetado: sem o retorno decrescente) a mesma regua REPROVA",
						  Math.Abs(Amizade.Ganho(10, 3) - 10) < 0.01);
			}
			finally { Amizade.SemRetornoDecrescenteDeTeste = false; }
			AfirmarAmi("...e desligado, o quarto gesto do dia volta a valer um oitavo", Math.Abs(Amizade.Ganho(10, 3) - 1.25) < 0.01);

			Amizade.QualquerHabitanteDeTeste = true;
			try
			{
				AfirmarAmi("A13. (defeito injetado: qualquer molde entra) a mesma regua REPROVA", Amizade.MoldeRecrutavel("rei_vegeta"));
			}
			finally { Amizade.QualquerHabitanteDeTeste = false; }

			// ---------------------------------------------------------------- 2. o par
			GD.Print("[aliados] -- 2) a tecla E, o presente e o retorno no servidor --");
			Vec2 chao = ChaoDaAmizade(zona);
			ServerPlayer pl = ForjarJogadorDeAliado("Fulano", 1, chao, 5000);
			ServerPlayer algoz = ForjarJogadorDeAliado("Algoz", 2, chao + new Vec2(0, 8), 5000);
			criados.Add(pl);
			criados.Add(algoz);
			sig = pl.Assinatura;
			AfirmarAmi("o corpo forjado tem assinatura (conta e slot)", sig.Length > 0 && sig != algoz.Assinatura, sig);

			ServerPlayer? npc = NascerHabitante("cidadao", chao + new Vec2(ZoneCollision.TileSize, 0));
			AfirmarAmi("nasceu um cidadao ao lado", npc != null, npc?.Name ?? "");
			if (npc == null) return;
			criados.Add(npc);
			IgualarPoder(pl, npc);
			AfastarQuemEstiverPerto(pl, afastados, npc, algoz);
			pl.Mochila.Guardar(CatalogoDeItens.Maca, 5);

			(int corpo, string tipo, string nome) = HabitanteAoAlcance(pl);
			AfirmarAmi("perto do cidadao a tecla E abre o menu do laco, sem o Recrutar",
					  corpo == npc.Id && tipo == Amizade.TipoDoHabitante && nome == npc.Name);
			HabitanteSemMenuDeTeste = true;
			try
			{
				AfirmarAmi("A1. (defeito injetado: o apontador nao enxerga o habitante) a mesma regua REPROVA",
						  HabitanteAoAlcance(pl).Corpo == 0);
			}
			finally { HabitanteSemMenuDeTeste = false; }
			AfirmarAmi("...e desligado, o menu volta", HabitanteAoAlcance(pl).Corpo == npc.Id);

			// O Rei, colado, com o cidadao afastado: unico e de nome, nao entra.
			Vec2 ondeDoCidadao = npc.Pos;
			npc.Pos = pl.Pos + new Vec2(6000, 0);
			ServerPlayer? rei = NascerHabitante("rei_vegeta", pl.Pos + new Vec2(16, 0));
			if (rei != null) criados.Add(rei);
			AfastarQuemEstiverPerto(pl, afastados, rei ?? npc, algoz);
			AfirmarAmi("colado no Rei de Vegeta, o menu nao abre (habitante unico)",
					  rei != null && HabitanteAoAlcance(pl).Corpo == 0, rei == null ? "o Rei nao nasceu" : HabitanteAoAlcance(pl).Tipo);
			if (rei != null)
			{
				Amizade.QualquerHabitanteDeTeste = true;
				try
				{
					(int cR, string tR, _) = HabitanteAoAlcance(pl);
					AfirmarAmi("A13b. (defeito injetado: qualquer molde entra) o Rei ganharia o menu -- a mesma regua REPROVA",
							  cR == rei.Id && tR == Amizade.TipoDoHabitante);
				}
				finally { Amizade.QualquerHabitanteDeTeste = false; }
				RemoverNpc(rei);
				criados.Remove(rei);
			}
			npc.Pos = ondeDoCidadao;

			// A folga: o menu apontou este corpo, ele deu um passo alem dos dois tiles, o clique ainda vale.
			_divinoApontado[pl.Id] = (pl, npc.Id, Amizade.TipoDoHabitante);
			Vec2 colado = npc.Pos;
			npc.Pos = pl.Pos + new Vec2(Interacoes.Alcance + Amizade.FolgaDoMenuAberto * 0.5f, 0);
			bool naFolga = DarItem(pl, npc, s => s.Contains("Maçã", StringComparison.Ordinal), out _, out List<string> falasFolga);
			AfirmarAmi("um passo alem da tecla E, o clique ainda acha quem o menu mostrou",
					   naFolga && Disse(falasFolga, "aceita"), Ultimos(falasFolga));
			MenuAbertoSemFolgaDeTeste = true;
			try
			{
				bool semFolga = !DarItem(pl, npc, s => s.Contains("Maçã", StringComparison.Ordinal), out _, out List<string> semFalas);
				AfirmarAmi("A14. (defeito injetado: o menu aberto nao da folga) a mesma regua REPROVA",
						   semFolga && Disse(semFalas, "habitante"), Ultimos(semFalas));
			}
			finally { MenuAbertoSemFolgaDeTeste = false; }
			npc.Pos = pl.Pos + new Vec2(Interacoes.Alcance + Amizade.FolgaDoMenuAberto + ZoneCollision.TileSize, 0);
			bool alemDaFolga = !DarItem(pl, npc, s => s.Contains("Maçã", StringComparison.Ordinal), out _, out _);
			AfirmarAmi("alem da folga o clique recusa, mesmo com o menu apontando esse corpo", alemDaFolga);
			npc.Pos = colado;
			_amizades.Remove(npc.Id);
			pl.Mochila.Pilhas.Clear();
			pl.Mochila.Guardar(CatalogoDeItens.Maca, 5);

			bool deu = DarItem(pl, npc, s => s.Contains("Maçã", StringComparison.Ordinal), out double g1, out List<string> falas);
			AfirmarAmi("dar a maca tira uma da mochila e sobe o laco em 3",
					  deu && Math.Abs(g1 - 3) < 0.05 && pl.Mochila.Quantos(CatalogoDeItens.Maca) == 4 && Disse(falas, "Amizade"),
					  $"ganho {g1:0.##}; {Ultimos(falas)}");
			deu = DarItem(pl, npc, s => s.Contains("Maçã", StringComparison.Ordinal), out double g2, out _);
			AfirmarAmi("a segunda maca do mesmo dia vale a metade (1,5)", deu && Math.Abs(g2 - 1.5) < 0.05, $"{g2:0.##}");

			Amizade.SemRetornoDecrescenteDeTeste = true;
			try
			{
				deu = DarItem(pl, npc, s => s.Contains("Maçã", StringComparison.Ordinal), out double g3, out _);
				AfirmarAmi("A12b. (defeito injetado: sem o retorno decrescente) a terceira maca vale inteira -- a mesma regua REPROVA",
						  deu && g3 > 2.5, $"{g3:0.##}");
			}
			finally { Amizade.SemRetornoDecrescenteDeTeste = false; }

			List<string> cedo = Ouvir(() => Verbo(pl, Amizade.VerboRecrutar, ""));
			AfirmarAmi("sem o laco cheio o Recrutar recusa e o cidadao continua no mundo",
					  Disse(cedo, "não confia") && _players.ContainsKey(npc.Id) && (AliadosDe(pl)?.Count ?? 0) == 0, Ultimos(cedo));

			// ---------------------------------------------------------------- 3. o treino
			GD.Print("[aliados] -- 3) o treino, no molde do Bills --");
			IgualarPoder(pl, npc);
			pl.Combate.Letal = true;
			List<string> comeco = Ouvir(() => Verbo(pl, Amizade.VerboTreinar, ""));
			TreinoDeAmizade? treino = TreinoDeAmizadeDe(pl);
			AfirmarAmi("poder parecido: o treino comeca, o golpe letal desliga e o cerebro sabe que e treino",
					  treino != null && !pl.Combate.Letal && Disse(comeco, "treino"), Ultimos(comeco));
			if (treino != null && npc.Cerebro != null)
			{
				npc.Cerebro.LutaDeTreino = false;
				bool guiou = GuiarOTreinoDeAmizade(npc, treino, out ServerPlayer? presa, out _);
				AfirmarAmi("o ramo do treino da a presa ao parceiro e cala os especiais",
						  guiou && presa == pl && npc.Cerebro.LutaDeTreino);
				npc.Cerebro.LutaDeTreino = false;
				TreinoComEspeciaisDeTeste = true;
				try
				{
					GuiarOTreinoDeAmizade(npc, treino, out _, out _);
					AfirmarAmi("A5. (defeito injetado: o treino nao cala os especiais) a mesma regua REPROVA", !npc.Cerebro.LutaDeTreino);
				}
				finally { TreinoComEspeciaisDeTeste = false; }
			}

			AfirmarAmi("enquanto dura, a queda nao e derrota", TreinoDeAmizadeEntre(npc, pl) && TreinoDeAmizadeEntre(pl, npc));
			TreinoDeAmizadeEhDerrotaDeTeste = true;
			try
			{
				AfirmarAmi("A3. (defeito injetado: a queda no treino volta a ser derrota) a mesma regua REPROVA",
						  !TreinoDeAmizadeEntre(npc, pl));
			}
			finally { TreinoDeAmizadeEhDerrotaDeTeste = false; }

			double noTreinoAntes = LacoDe(npc, pl)?.Pontos ?? 0;
			AmizadeAoApanhar(npc, pl);
			AfirmarAmi("apanhar NO TREINO nao zera o laco", Math.Abs((LacoDe(npc, pl)?.Pontos ?? -1) - noTreinoAntes) < 0.01);

			npc.Ficha.HP = 40;
			TickDosAliados(0.05);
			AfirmarAmi("na metade da vida o treino acaba e rende amizade, e o letal de quem pediu volta",
					  TreinoDeAmizadeDe(pl) == null && pl.Combate.Letal
					  && (LacoDe(npc, pl)?.Pontos ?? 0) > noTreinoAntes + 1);
			npc.Ficha.HP = 100;
			pl.Ficha.HP = 100;

			// De novo, so pra o defeito da metade: com ele ligado, 40% de vida nao encerra.
			Ouvir(() => Verbo(pl, Amizade.VerboTreinar, ""));
			npc.Ficha.HP = 40;
			TreinoSemMetadeDeTeste = true;
			try
			{
				TickDosAliados(0.05);
				AfirmarAmi("A6. (defeito injetado: o juiz nao olha a vida) a mesma regua REPROVA", TreinoDeAmizadeDe(pl) != null);
			}
			finally { TreinoSemMetadeDeTeste = false; }
			if (TreinoDeAmizadeDe(pl) is { } aberto) EncerrarOTreinoDeAmizade(aberto, FimDoTreinoDeAmizade.Parou);
			npc.Ficha.HP = 100;

			// O esmagado recusa; o defeito aceita.
			double poderJusto = pl.Ficha.expressedBP;
			pl.Ficha.expressedBP = Math.Max(1, npc.Ficha.expressedBP) * 20;
			List<string> recusa = Ouvir(() => Verbo(pl, Amizade.VerboTreinar, ""));
			AfirmarAmi("poder desigual demais: o habitante recusa", TreinoDeAmizadeDe(pl) == null && Disse(recusa, "desigual"), Ultimos(recusa));
			TreinoSemMedoDeTeste = true;
			try
			{
				Ouvir(() => Verbo(pl, Amizade.VerboTreinar, ""));
				AfirmarAmi("A4. (defeito injetado: o habitante aceita qualquer poder) a mesma regua REPROVA", TreinoDeAmizadeDe(pl) != null);
			}
			finally { TreinoSemMedoDeTeste = false; }
			if (TreinoDeAmizadeDe(pl) is { } medo) EncerrarOTreinoDeAmizade(medo, FimDoTreinoDeAmizade.Parou);
			pl.Ficha.expressedBP = poderJusto;

			// ---------------------------------------------------------------- 4. bater zera; encher; recrutar
			GD.Print("[aliados] -- 4) bater zera, recrutar, ordens --");
			_relogioDosAliados += Amizade.SegundosDeFolegoAoFim + 1;
			double antesDoSoco = LacoDe(npc, pl)?.Pontos ?? 0;
			AfirmarAmi("havia laco antes do soco", antesDoSoco > 0, $"{antesDoSoco:0.##}");
			AmizadeAoApanhar(npc, pl);
			AfirmarAmi("bater de verdade zera o laco", LacoDe(npc, pl) == null);

			DarItem(pl, npc, s => s.Contains("Maçã", StringComparison.Ordinal), out _, out _);
			double restaurou = LacoDe(npc, pl)?.Pontos ?? 0;
			ApanharNaoZeraDeTeste = true;
			try
			{
				AmizadeAoApanhar(npc, pl);
				AfirmarAmi("A7. (defeito injetado: bater nao zera) a mesma regua REPROVA",
						  LacoDe(npc, pl) != null && Math.Abs((LacoDe(npc, pl)?.Pontos ?? 0) - restaurou) < 0.01);
			}
			finally { ApanharNaoZeraDeTeste = false; }

			// Cinco Senzu, cada uma num "dia" novo: 20 de cada vez, o caminho de producao ate o cheio.
			pl.Mochila.Guardar(CatalogoDeItens.Senzu, 6);
			for (int i = 0; i < 6 && !Amizade.Cheio(LacoDe(npc, pl)); i++)
			{
				if (LacoDe(npc, pl) is { } laco) laco.Dia = DiaDoMundo - 1;
				DarItem(pl, npc, s => s.Contains("Senzu", StringComparison.Ordinal), out _, out _);
			}
			AfirmarAmi("com a Senzu, virando o dia, o laco chega a 100", Amizade.Cheio(LacoDe(npc, pl)),
					  $"{LacoDe(npc, pl)?.Pontos ?? 0:0.##}");

			(int corpoCheio, string tipoCheio, _) = HabitanteAoAlcance(pl);
			AfirmarAmi("laco cheio: o menu passa a ser o do amigo, com o Recrutar",
					  corpoCheio == npc.Id && tipoCheio == Amizade.TipoDoAmigo);

			int idDoCidadao = npc.Id;
			string nomeDoCidadao = npc.Name;
			List<string> veio = Ouvir(() => Verbo(pl, Amizade.VerboRecrutar, ""));
			Aliado? aliado = AliadosDe(pl)?.Find(a => a.Criatura.Nome == nomeDoCidadao);
			AfirmarAmi("Recrutar tira o habitante do mundo e poe o aliado na ficha, solto",
					  aliado != null && !_players.ContainsKey(idDoCidadao) && SoltoDoAliado(pl, aliado) != null
					  && Disse(veio, "aliado"), Ultimos(veio));
			criados.Remove(npc);

			if (aliado != null)
			{
				AfirmarAmi("nasce com a inteligencia de gente e a relacao da amizade",
						  aliado.Criatura.Inteligencia == Aliados.InteligenciaDoAliado && aliado.Criatura.Relacao >= Amizade.Cheia - 1
						  && aliado.Criatura.Nivel == 1);
				string id = aliado.Id.ToString();
				Ouvir(() => Verbo(pl, Aliados.VerboGuardar, id));
				AfirmarAmi("Guardar tira ele do mundo e ele continua na ficha",
						  SoltoDoAliado(pl, aliado) == null && AliadosDe(pl)!.Contains(aliado));
				Ouvir(() => Verbo(pl, Aliados.VerboSoltar, id));
				(ServerPlayer Corpo, CompanheiroSolto Estado)? solto = SoltoDoAliado(pl, aliado);
				AfirmarAmi("Soltar traz ele de volta, seguindo", solto != null && solto.Value.Estado.Ordem == OrdemDeCompanheiro.Seguir);
				Ouvir(() => Verbo(pl, Aliados.VerboSeguir, id));
				AfirmarAmi("Seguir/Ficar alterna: agora ele fica",
						  SoltoDoAliado(pl, aliado)?.Estado.Ordem == OrdemDeCompanheiro.Ficar);
				Ouvir(() => Verbo(pl, Aliados.VerboSeguir, id));

				aliado.Criatura.Bp = 1_234_567;
				string cru = 1234567.ToString("N0");
				string escrito = PoderEscrito(pl, 1_234_567);
				string noFio = PoderNoPacote(pl);
				AfirmarAmi("sem medidor o pacote leva o poder escrito, nao o numero", noFio == escrito && escrito != cru, $"[{noFio}]");
				PoderDoAliadoEmNumeroDeTeste = true;
				try
				{
					AfirmarAmi("A10. (defeito injetado: o pacote leva o poder em numero) a mesma regua REPROVA", PoderNoPacote(pl) == cru);
				}
				finally { PoderDoAliadoEmNumeroDeTeste = false; }

				// O teto de aliados, com reservas forjadas so na lista.
				List<Aliado> lista = AliadosOuNovos(pl);
				while (lista.Count < Aliados.Maximo)
					lista.Add(new Aliado { Id = 8000 + lista.Count, Criatura = new Companheiro { Nome = "Reserva" } });
				ServerPlayer? extra = NascerHabitante("cidadao", pl.Pos + new Vec2(16, 0));
				if (extra != null)
				{
					criados.Add(extra);
					IgualarPoder(pl, extra);
					LacoNovo(extra, pl)!.Pontos = Amizade.Cheia;
					Ouvir(() => Verbo(pl, Amizade.VerboRecrutar, ""));
					AfirmarAmi($"no teto de {Aliados.Maximo} o Recrutar recusa",
							  lista.Count == Aliados.Maximo && _players.ContainsKey(extra.Id));
					RemoverNpc(extra);
					criados.Remove(extra);
				}
				lista.RemoveAll(a => a.Criatura.Nome == "Reserva");

				// ---------------------------------------------------------------- 5. o luto e a morte
				GD.Print("[aliados] -- 5) luto como amigo jogador, morte definitiva --");
				(ServerPlayer Corpo, CompanheiroSolto Estado)? vivo = SoltoDoAliado(pl, aliado);
				AfirmarAmi("o aliado esta no mundo pra o luto", vivo != null);
				if (vivo is { } par)
				{
					void ZerarJanelas() { pl.RaivaComumAte = 0; pl.FuriaExtremaAte = 0; }
					ZerarJanelas();
					LutoDoAliado(par.Corpo, algoz, morreu: false);
					AfirmarAmi("nocaute do aliado, com o dono vendo, acende a raiva comum",
							  pl.RaivaComumAte > NowMs() && pl.FuriaExtremaAte == 0);
					ZerarJanelas();
					LutoDoAliado(par.Corpo, algoz, morreu: true);
					AfirmarAmi("a morte do aliado acende a furia", pl.FuriaExtremaAte > NowMs());
					ZerarJanelas();
					LutoDoAliado(par.Corpo, pl, morreu: false);
					AfirmarAmi("o proprio dono derrubando o aliado nao acende raiva", pl.RaivaComumAte == 0 && pl.FuriaExtremaAte == 0);
					ZerarJanelas();
					Vec2 junto = pl.Pos;
					pl.Pos = par.Corpo.Pos + new Vec2(RaioDeTestemunha * 4, 0);
					LutoDoAliado(par.Corpo, algoz, morreu: false);
					AfirmarAmi("dono longe nao acende raiva", pl.RaivaComumAte == 0);
					pl.Pos = junto;
					ZerarJanelas();
					LutoNaVizinhanca(par.Corpo, algoz, morreu: false);
					AfirmarAmi("o veto do luto continua pros outros: este caminho nao acende pelo NPC",
							  pl.RaivaComumAte == 0 && pl.FuriaExtremaAte == 0);
					ZerarJanelas();
					AliadoForaDoLutoDeTeste = true;
					try
					{
						LutoDoAliado(par.Corpo, algoz, morreu: false);
						AfirmarAmi("A9. (defeito injetado: o aliado fica fora do luto) a mesma regua REPROVA", pl.RaivaComumAte == 0);
					}
					finally { AliadoForaDoLutoDeTeste = false; }

					par.Corpo.Ficha.dead = true;
					TickDosCompanheiros(0.05);
					AfirmarAmi("morto, o aliado sai da ficha (a morte e definitiva)", AliadosDe(pl)?.Contains(aliado) != true);
				}

				// O defeito da morte: um segundo aliado, enchido pela conta, que VOLTA pra ficha.
				ServerPlayer? npc2 = NascerHabitante("cidadao", pl.Pos + new Vec2(ZoneCollision.TileSize, 0));
				if (npc2 != null)
				{
					criados.Add(npc2);
					IgualarPoder(pl, npc2);
					AfastarQuemEstiverPerto(pl, afastados, npc2, algoz);
					LacoNovo(npc2, pl)!.Pontos = Amizade.Cheia;
					string nome2 = npc2.Name;
					Ouvir(() => Verbo(pl, Amizade.VerboRecrutar, ""));
					criados.Remove(npc2);
					Aliado? segundo = AliadosDe(pl)?.Find(a => a.Criatura.Nome == nome2);
					(ServerPlayer Corpo, CompanheiroSolto Estado)? solto2 = segundo == null ? null : SoltoDoAliado(pl, segundo);
					AfirmarAmi("o segundo aliado saiu pro defeito da morte", solto2 != null);
					if (solto2 is { } p2 && segundo != null)
					{
						p2.Corpo.Ficha.dead = true;
						AliadoVoltaAoMorrerDeTeste = true;
						try
						{
							TickDosCompanheiros(0.05);
							AfirmarAmi("A8. (defeito injetado: o aliado morto volta pra casa) a mesma regua REPROVA",
									  AliadosDe(pl)?.Contains(segundo) == true);
						}
						finally { AliadoVoltaAoMorrerDeTeste = false; }
						AliadosDe(pl)?.Remove(segundo);
					}
				}
			}

			// ---------------------------------------------------------------- 6. a poda e a vaga
			GD.Print("[aliados] -- 6) o laco morre com o habitante, e com a vaga --");
			ServerPlayer? npc3 = NascerHabitante("cidadao", pl.Pos + new Vec2(ZoneCollision.TileSize, 0));
			if (npc3 != null)
			{
				criados.Add(npc3);
				AfastarQuemEstiverPerto(pl, afastados, npc3, algoz);
				pl.Mochila.Guardar(CatalogoDeItens.Maca, 1);
				DarItem(pl, npc3, s => s.Contains("Maçã", StringComparison.Ordinal), out _, out _);
				AfirmarAmi("o terceiro cidadao tem laco", LacoDe(npc3, pl) != null);
				int id3 = npc3.Id;
				RemoverNpc(npc3);
				criados.Remove(npc3);
				_relogioDosAliados += 2;
				TickDosAliados(0.05);
				AfirmarAmi("removido do mundo, a poda de 1 Hz esquece o laco",
						  !_amizades.ContainsKey(id3));

				ServerPlayer? npc4 = NascerHabitante("cidadao", pl.Pos + new Vec2(ZoneCollision.TileSize, 0));
				if (npc4 != null)
				{
					criados.Add(npc4);
					LacoNovo(npc4, pl)!.Pontos = 40;
					EsquecerAsAmizades(sig);
					AfirmarAmi("a vaga reciclada esquece os lacos da assinatura", LacoDe(npc4, pl) == null);
					LacoNovo(npc4, pl)!.Pontos = 40;
					AmizadeHerdaAVagaDeTeste = true;
					try
					{
						EsquecerAsAmizades(sig);
						AfirmarAmi("A11. (defeito injetado: a vaga herda os lacos) a mesma regua REPROVA",
								  Math.Abs((LacoDe(npc4, pl)?.Pontos ?? 0) - 40) < 0.01);
					}
					finally { AmizadeHerdaAVagaDeTeste = false; }
					EsquecerAAssinaturaNoMundo(sig);
					AfirmarAmi("pelo funil da vaga (PurgarAssinatura), o laco tambem sai", LacoDe(npc4, pl) == null);
				}
			}

			// Um cidadao sem amizade, so pro defeito do Recrutar.
			ServerPlayer? npc5 = NascerHabitante("cidadao", pl.Pos + new Vec2(16, 0));
			if (npc5 != null)
			{
				criados.Add(npc5);
				AfastarQuemEstiverPerto(pl, afastados, npc5, algoz);
				int antes = AliadosDe(pl)?.Count ?? 0;
				RecrutaSemAmizadeDeTeste = true;
				try
				{
					Ouvir(() => Verbo(pl, Amizade.VerboRecrutar, ""));
					AfirmarAmi("A2. (defeito injetado: o Recrutar nao confere o laco) a mesma regua REPROVA",
							  (AliadosDe(pl)?.Count ?? 0) == antes + 1);
				}
				finally { RecrutaSemAmizadeDeTeste = false; }
				if (!_players.ContainsKey(npc5.Id)) criados.Remove(npc5);
			}

			AliadosState estado = EstadoDosAliados(pl);
			AfirmarAmi("a aba cabe no fio e a leitura de producao devolve o que foi escrito",
					  estado.Teto == Aliados.Maximo && estado.TetoDeSoltos == Companheiros.MaxSoltos);
			AfirmarAmi("a escuta da bancada viu ao menos um pacote da aba", EscutaDosAliados is { Count: > 0 });
		}
		catch (Exception e)
		{
			AfirmarAmi("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			try
			{
				SoltarOsBotoesDosAliados();
				if (sig.Length > 0) EsquecerAsAmizades(sig);
				foreach (ServerPlayer corpo in criados.ToList())
				{
					if (_companheiros.ContainsKey(corpo.Id)) RecolherCompanheiro(corpo.Id, null);
					if (_players.ContainsKey(corpo.Id)) RemoverNpc(corpo);
				}
				foreach ((int id, CompanheiroSolto s) in _companheiros.ToList())
					if (_players.TryGetValue(s.Dono, out ServerPlayer? dono) && dono.Conta == "bancada_aliados")
						RecolherCompanheiro(id, null);
				foreach (ServerPlayer o in ZoneList(zona.Hash).ToList())
					if (o.ECadaver) RemoverNpc(o);
				foreach ((ServerPlayer corpo, Vec2 onde) in afastados)
					if (_players.ContainsKey(corpo.Id)) corpo.Pos = onde;
				foreach (int id in _treinosDeAmizade.Keys.ToList()) _treinosDeAmizade.Remove(id);
			}
			catch (Exception e)
			{
				GD.PrintErr("[aliados] limpeza: " + e);
			}
			SoltarOsBotoesDosAliados();
			GD.Print($"[aliados] ================ {_amiOk} OK, {_amiFalhou} FALHA(S) ================");
		}
	}

	// =====================================================================
	// O PALCO DO ROBO (`--diagaliados`) -- o host de verdade, um cidadao ao lado
	// =====================================================================
	private int _lugarDoPalcoAl = 91001;

	/// <summary>Poem um cidadao a um passo do host, com o poder parecido e macas na mochila. Devolve o id, ou 0.</summary>
	internal int PalcoAl_Habitante(int idHost)
	{
		if (!_players.TryGetValue(idHost, out ServerPlayer? host) || host.Peer == null) return 0;
		if (host.Ficha.HP < 80) host.Ficha.HP = 100;
		ServerPlayer? npc = NascerNpc("cidadao", host.Zone, host.Pos + new Vec2(ZoneCollision.TileSize, 0), (ulong)_lugarDoPalcoAl++);
		if (npc == null) return 0;
		npc.Ficha.HP = 100;
		double poder = Math.Max(1, host.Ficha.expressedBP);
		npc.Ficha.expressedBP = poder;
		host.Mochila.Guardar(CatalogoDeItens.Maca, 4);
		MandarMochila(host);
		TrocarAparencias(npc);
		return npc.Id;
	}

	internal string PalcoAl_Nome(int id) => _players.TryGetValue(id, out ServerPlayer? p) ? p.Name : "";

	/// <summary>
	/// Iguala o BP de verdade (o tique reescreve o poder expresso a partir dele) e cura as partes, pra o treino
	/// nao recusar e nao acabar no primeiro soco.
	/// </summary>
	internal void PalcoAl_Igualar(int idHost, int idNpc)
	{
		if (!_players.TryGetValue(idHost, out ServerPlayer? host) || !_players.TryGetValue(idNpc, out ServerPlayer? npc)) return;
		npc.Ficha.BP = Math.Max(1, host.Ficha.BP);
		for (int i = 0; i < 4; i++)
		{
			host.Ficha.Tick(agoraMs: NowMs());
			npc.Ficha.Tick(agoraMs: NowMs());
			double h = Math.Max(1, host.Ficha.expressedBP);
			double n = Math.Max(1, npc.Ficha.expressedBP);
			double razao = h / n;
			if (razao is > 0.5 and < 2) break;
			npc.Ficha.BP = Math.Max(1, npc.Ficha.BP * razao);
		}
		CurarOCorpoDoPalco(host);
		CurarOCorpoDoPalco(npc);
	}

	/// <summary>A vida da ficha e a media das partes. Encher o numero sem curar o corpo dura um tique.</summary>
	private static void CurarOCorpoDoPalco(ServerPlayer p)
	{
		if (p.Combate != null)
		{
			foreach (Jandirus.Core.Combat.BodyPart parte in p.Combate.Corpo.Partes)
				if (!parte.Decepado) parte.Vida = parte.VidaMax;
			p.Combate.SincronizarVida();
		}
		else p.Ficha.HP = 100;
		p.Ficha.KO = false;
	}

	/// <summary>
	/// Traz o habitante pra um tile do host e para a rotina: o spar e o passeio afastam, e o clique do menu
	/// (presente, Recrutar) chega no segundo seguinte. A folga do menu cobre o passo que ele ainda der.
	/// </summary>
	internal void PalcoAl_Juntar(int idHost, int idNpc)
	{
		if (!_players.TryGetValue(idHost, out ServerPlayer? host) || !_players.TryGetValue(idNpc, out ServerPlayer? npc)) return;
		if (npc.Zone.Hash != host.Zone.Hash) return;
		npc.Rotina?.Interromper("menu da amizade");
		npc.Moving = false;
		npc.Pos = host.Pos + new Vec2(ZoneCollision.TileSize, 0);
	}

	/// <summary>Enche o laco pela conta de producao, pra o menu mostrar o Recrutar sem esperar varios dias.</summary>
	internal bool PalcoAl_Encher(int idHost, int idNpc)
	{
		if (!_players.TryGetValue(idHost, out ServerPlayer? pl) || !_players.TryGetValue(idNpc, out ServerPlayer? npc)) return false;
		if (LacoNovo(npc, pl) is not { } l) return false;
		Amizade.Somar(l, Amizade.Cheia);
		MandarAliados(pl, forcar: true);
		ApontarODivinoPerto();
		return Amizade.Cheio(l);
	}

	/// <summary>
	/// Poe o habitante abaixo da metade da vida DE VERDADE: a ficha e a media das partes, e o tique do jogo
	/// reescreve <c>Ficha.HP</c> por <c>SincronizarVida</c>. 40% fica acima do nocaute (20%) e abaixo do fim do treino.
	/// </summary>
	internal void PalcoAl_Ceder(int idNpc)
	{
		if (!_players.TryGetValue(idNpc, out ServerPlayer? npc) || npc.Combate == null) return;
		foreach (Jandirus.Core.Combat.BodyPart parte in npc.Combate.Corpo.Partes)
		{
			if (parte.Decepado) continue;
			parte.Vida = parte.VidaMax * 0.40;
		}
		npc.Combate.SincronizarVida();
		TickDosAliados(0.05);
	}
}
