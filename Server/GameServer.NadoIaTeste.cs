using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA IA NA AGUA (`--nadoiateste`, no 1o login) -- o pedido do dono (2026-09-07):
/// *"faca com que a IA saiba nadar quando precisar (quando e jogada na agua ou quando precisa
/// atravessar a agua) mas se ela souber voar ela de preferencia pra voo"*.
///
/// Roda no PRIMEIRO LOGIN e nao no boot, e nao e capricho: o habitante da familia 5 nasce de molde
/// (`NascerNpc("cidadao")`), e a mente de quem tem molde DORME numa zona sem jogador
/// (`MenteDormindo`). Com o host na zona ela acorda -- e o host fica a vinte tiles do lago, dentro
/// do raio de atencao da rotina e fora do caminho de todo mundo.
///
/// O LAGO E ACHADO NO MAPA DE VERDADE (`Praia`): uma faixa de agua de 8 a 14 celulas de largura com
/// chao dos dois lados, na Terra. Nada e forjado no `.agua`: se um dia o mapa mudar e nao houver
/// lago assim, a precondicao reprova e diz por que.
///
/// COMO RODAR:  testar-nado-ia.bat   (ou `--headless --host --nadoiateste --raca Human --conta X --nome Y`)
/// </summary>
public sealed partial class GameServer
{
	private bool _nadoIaDeTeste;

	private void RodarBancadaDoNadoDaIa(ServerPlayer pl)
	{
		GD.Print("\n[nadoia] ================ A IA NA AGUA: NADAR QUANDO PRECISA, VOAR QUANDO PODE (pedido do dono, 2026-09-07) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[nadoia]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[nadoia]   FALHA {nome}   [{detalhe}]"); }
		}
		void Placar() => GD.Print($"[nadoia] ================ {ok} OK, {falhou} FALHA(S) ================");

		ZoneKey zona = pl.Zone;
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) { Checa("PRECONDICAO: a zona do host tem mapa", false, zona.Name); Placar(); return; }
		if (Praia(mapa) is not { } praia)
		{
			Checa("PRECONDICAO: ha um lago de 8 a 14 celulas de largura, com chao dos dois lados, na zona do host", false, zona.Name);
			Placar();
			return;
		}
		(int xL, int xR, int y) = praia;
		int largura = xR - xL + 1;
		Vec2 Celula(int cx, int cy) => mapa.CentroDaCelula(cx, cy);
		Vec2 margemEsq = Celula(xL - 2, y), margemDir = Celula(xR + 2, y);
		int tiquesDoArremesso = Math.Max(2, (2 + largura / 2) / 2);   // 2 tiles por tique: cai no meio do lago
		GD.Print($"[nadoia] lago na linha {y}, celulas {xL}..{xR} ({largura} de largura); margens em ({xL - 2},{y}) e ({xR + 2},{y}); arremesso de {tiquesDoArremesso} tiques");

		List<ServerPlayer> naZona = ZoneList(zona.Hash);
		bool puseu = !naZona.Contains(pl);
		if (puseu) naZona.Add(pl);
		Vec2 posDoHost = pl.Pos;
		double carenciaDoHost = pl.Combate.Carencia;
		// O HOST A VINTE TILES, DO LADO DE CA: acorda a mente do habitante (raio de atencao 40) e nao vira
		// alvo de ninguem (as iscas ficam mais perto). Intocavel, porque a fera que sair do lago vai
		// procurar o corpo mais proximo e este projeto ja teve host nocauteado por bancada.
		int hostCx = xL - 20 >= 2 ? xL - 20 : xR + 20;
		CravarPosicao(pl, mapa.PontoLivrePerto(Celula(hostCx, y)));
		pl.Combate.Carencia = 1e6;

		var nascidos = new List<ServerPlayer>();
		ServerPlayer Forjar(string nome, Vec2 pos, bool comCerebro = true, PerfilDeCombate? perfil = null)
		{
			var f = new Jandirus.Core.Stats.Fighter
			{
				Name = nome, Race = "Saiyan", BP = 5_000,
				physoff = 5, physdef = 5, technique = 5, kioff = 5, kidef = 5,
				kiskill = 5, speed = 5, magiskill = 5, Idade = 25,
				maxstamina = 100, stamina = 100,
			};
			f.Statify();
			f.PowerLevel();
			f.Ki = f.MaxKi;
			var c = new ServerPlayer
			{
				Id = _nextId++, Peer = null, Name = nome, Zone = zona, Pos = pos,
				Race = "Saiyan", Class = "", Genero = "Male", Idade = 25,
				LastInputMs = NowMs(), Ficha = f,
				Livro = new Jandirus.Core.Skills.SkillBook(),
				Cerebro = comCerebro ? new Cerebro() : null,
				Perfil = perfil ?? PerfilDeCombate.SoCorpo,
			};
			PorNoMundo(c);
			nascidos.Add(c);
			return c;
		}
		void EnsinarAVoar(ServerPlayer c)
		{
			c.Livro.Dar(SkillDoKi);
			c.Niveis.Por(SkillDoKi, MaestriaQueDestravaVoo);
		}
		void Armar(ServerPlayer c) => c.Cerebro!.Poderes = c.Perfil.Filtrar(LerCapacidades(c));
		void Tiques(int n, params ServerPlayer[] corpos)
		{
			for (int i = 0; i < n; i++)
			{
				TickCombate(Protocol.TickSeconds);
				foreach (ServerPlayer c in corpos)
				{
					TickDoVoo(c, Protocol.TickSeconds);
					TickDoNado(c, Protocol.TickSeconds);
					TickDaCarga(c, Protocol.TickSeconds);
				}
				TickDoEmpurrao();
				TickDosCorposSemDono(Protocol.TickSeconds);
			}
		}
		void Descartar(params ServerPlayer[] corpos)
		{
			foreach (ServerPlayer c in corpos) { if (_players.ContainsKey(c.Id)) RemoverNpc(c); nascidos.Remove(c); }
		}
		// JOGA NO LAGO E ESPERA CAIR. Devolve se caiu DENTRO (a precondicao de quase todas as familias).
		bool JogarNoLago(ServerPlayer c)
		{
			Arremessar(c, new Vec2(1, 0), 10, tiquesDoArremesso);
			int n = 0;
			while (c.TiquesDeVoo > 0 && n++ < 60) Tiques(1, c);
			return c.TiquesDeVoo == 0 && PesNaAgua(c);
		}
		float tile = ZoneCollision.TileSize;

		try
		{
			// A ISCA: um corpo sem mente a oito tiles do lago, do lado de ca. A fera que cair no lago
			// procura o corpo mais proximo, e e pra ELA que ele volta -- e nao pro host.
			ServerPlayer isca = Forjar("nado: isca", Celula(xL - 8, y), comCerebro: false);
			// INTOCAVEL: a fera que volta do lago socaria a isca, e o soco tem ARRANQUE (um salto de ate 15
			// tiles) -- a bancada via um "passo" de 276 px e chamava de teleporte. Sem alvo tocavel nao ha
			// soco, e o corpo so anda e nada.
			isca.Combate.Carencia = 1e6;

			GD.Print("[nadoia] -- 1) jogado no lago, so corpo: nada ate a margem --");
			{
				// SABE A SKILL DE VOO, mas o PERFIL diz "so corpo": e o perfil que manda, como no torneio.
				ServerPlayer a = Forjar("nado: so corpo", margemEsq, perfil: PerfilDeCombate.SoCorpo);
				EnsinarAVoar(a);
				Armar(a);
				Checa("PRECONDICAO: o arremesso o deixou DENTRO do lago", JogarNoLago(a), $"{a.Pos}, agua={PesNaAgua(a)}");
				bool nadou = false, disseAgua = false, voou = false;
				int tiqueDoNado = -1;
				float maiorPasso = 0;
				Vec2 antes = a.Pos;
				for (int i = 0; i < 900; i++)
				{
					Tiques(1, a, isca);
					maiorPasso = Math.Max(maiorPasso, (a.Pos - antes).Length);
					antes = a.Pos;
					if (a.Nadando && !nadou) { nadou = true; tiqueDoNado = i; }
					if (a.Voando) voou = true;
					if (a.Cerebro!.Porque.Contains("agua", StringComparison.Ordinal)) disseAgua = true;
					if (nadou && !a.Nadando && !PesNaAgua(a)) break;
				}
				Checa("no lago, sem poder voar, ele LIGA O NADO sozinho (pelo verb `nadar`, o mesmo do jogador)", nadou, $"tique {tiqueDoNado}");
				Checa("...e nada ate o chao seco", !PesNaAgua(a), $"{a.Pos}");
				Checa("...onde o nado desliga sozinho (o tique do servidor, como pro jogador)", nadou && !a.Nadando);
				Checa("...sem teleporte: nenhum passo maior que um tile", maiorPasso <= tile, $"{maiorPasso:0} px");
				Checa("...e nunca levantou voo: o perfil 'so corpo' vale mesmo sabendo a skill", !voou);
				Checa("...e o cerebro disse por que (\"agua: ...\")", disseAgua, a.Cerebro!.Porque);
				Descartar(a);
			}

			GD.Print("[nadoia] -- 2) jogado no lago, sabe voar: decola em vez de nadar, e nao pousa na agua --");
			{
				ServerPlayer v = Forjar("nado: voador", margemEsq, perfil: PerfilDeCombate.Completo);
				EnsinarAVoar(v);
				Armar(v);
				Checa("PRECONDICAO: o arremesso o deixou DENTRO do lago", JogarNoLago(v));
				bool decolou = false, nadou = false, pousouNaAgua = false;
				for (int i = 0; i < 300; i++)
				{
					Tiques(1, v, isca);
					if (v.Voando) decolou = true;
					if (v.Nadando) nadou = true;
					if (decolou && !v.Voando && v.Altitude <= 0f && PesNaAgua(v)) pousouNaAgua = true;
				}
				Checa("sabendo voar, ele DECOLA em vez de nadar (voo antes de nado)", decolou && !nadou, $"voou={decolou} nadou={nadou}");
				Checa("...e enquanto esta sobre a agua nao pousa nela", !pousouNaAgua);
				Descartar(v);
			}

			GD.Print("[nadoia] -- 3) sem folego: boia parado, e nada quando o Ki volta --");
			{
				ServerPlayer e = Forjar("nado: exausto", margemEsq, perfil: PerfilDeCombate.SoCorpo);
				Armar(e);
				Checa("PRECONDICAO: o arremesso o deixou DENTRO do lago", JogarNoLago(e));
				e.Ficha.Ki = 0;
				Vec2 ondeCaiu = e.Pos;
				bool disseFolego = false;
				for (int i = 0; i < 120; i++)
				{
					Tiques(1, e, isca);
					if (e.Cerebro!.Porque.Contains("folego", StringComparison.Ordinal)) disseFolego = true;
				}
				Checa("sem folego ele BOIA parado: nao nada, nao voa, nao anda por cima da agua e nao e teleportado",
					  !e.Nadando && !e.Voando && PesNaAgua(e) && (e.Pos - ondeCaiu).Length <= 2f, $"{(e.Pos - ondeCaiu).Length:0.0} px");
				Checa("...e o cerebro diz que espera o Ki voltar", disseFolego, e.Cerebro!.Porque);
				e.Ficha.Ki = e.Ficha.MaxKi;
				bool nadou = false;
				for (int i = 0; i < 900; i++)
				{
					// O KI FICA CHEIO: a pergunta e "com folego ele sai?", e nao a economia do nado -- um cerebro
					// que resolve guardar ou carregar no meio do lago drena o tanque e a bancada media sorte.
					e.Ficha.Ki = e.Ficha.MaxKi;
					Tiques(1, e, isca);
					if (e.Nadando) nadou = true;
					if (nadou && !e.Nadando && !PesNaAgua(e)) break;
				}
				Checa("com o Ki de volta ele nada e sai do lago", nadou && !PesNaAgua(e), $"{e.Pos}");
				Descartar(e);
			}

			GD.Print("[nadoia] -- 4) precisa atravessar: o alvo esta na outra margem --");
			{
				Descartar(isca);   // senao a isca e o corpo mais proximo e ninguem atravessa nada
				ServerPlayer alvo = Forjar("nado: alvo na outra margem", margemDir, comCerebro: false);
				// A PRESA E IMPOSTA (o `tourney_engage`, `Papel.PresaDoRoteiro`): um corpo forjado sem molde e
				// uma FERA, e a fera vai atras do corpo mais perto da zona inteira -- e a Terra esta cheia de
				// habitantes passeando. A bancada media pra que lado o vento soprava (3 rodadas, 2 placares).
				ServerPlayer? Lutador(string nome, Vec2 pos, PerfilDeCombate perfil, int presa)
				{
					ServerPlayer? n = NascerNpc("lutador_de_torneio", zona, pos, (ulong)(900 + nascidos.Count));
					if (n == null) return null;
					n.Name = nome;
					n.Perfil = perfil;
					n.Ficha.Ki = n.Ficha.MaxKi;
					n.Papel!.PresaDoRoteiro = presa;
					nascidos.Add(n);
					return n;
				}
				ServerPlayer? b = Lutador("nado: atravessa nadando", margemEsq, PerfilDeCombate.SoCorpo, alvo.Id);
				Checa("PRECONDICAO: um lutador de molde nasceu na margem de ca, com a presa imposta", b != null);
				if (b == null) { Descartar(alvo); goto FimDaFamilia4; }
				Armar(b);
				bool nadou = false, barrado = false;
				for (int i = 0; i < 1500; i++)
				{
					b.Ficha.Ki = b.Ficha.MaxKi;   // idem: a pergunta e a travessia, nao o tanque
					Tiques(1, b, alvo);
					if (b.BarradoPelaAgua) barrado = true;
					if (b.Nadando) nadou = true;
					if ((b.Pos - alvo.Pos).Length <= 2.5f * tile) break;
				}
				Checa("com o alvo do outro lado do lago, o passo bate na agua e ele o SABE (`BarradoPelaAgua`)", barrado);
				Checa("...entra NADANDO", nadou);
				Checa("...e chega do outro lado", (b.Pos - alvo.Pos).Length <= 2.5f * tile, $"{(b.Pos - alvo.Pos).Length / tile:0.0} tiles");
				// A margem de la fica a duas celulas do alvo: a 2,5 tiles dele o corpo ainda pode estar
				// com um pe na agua. Uns tiques a mais pra ele pisar no seco -- e o nado cair sozinho.
				for (int i = 0; i < 90 && (b.Nadando || PesNaAgua(b)); i++) Tiques(1, b, alvo);
				Checa("...e sai da agua a pe (o nado desligou no seco)", !b.Nadando && !PesNaAgua(b), $"nadando={b.Nadando} agua={PesNaAgua(b)}");
				Descartar(b);

				// VOA MAS NAO ATIRA: com Ki no perfil o lutador de molde (que sabe Ki Wave) atirava de cima do
				// lago em vez de atravessar -- e a pergunta aqui e a travessia, nao o tiro.
				ServerPlayer? c = Lutador("nado: atravessa voando", margemEsq, new PerfilDeCombate(Voa: true, UsaKi: false), alvo.Id);
				if (c == null) { Checa("PRECONDICAO: o voador de molde nasceu", false); Descartar(alvo); goto FimDaFamilia4; }
				EnsinarAVoar(c);
				Armar(c);
				bool voou = false;
				nadou = false;
				for (int i = 0; i < 1500; i++)
				{
					c.Ficha.Ki = c.Ficha.MaxKi;
					Tiques(1, c, alvo);
					if (c.Voando) voou = true;
					if (c.Nadando) nadou = true;
					if ((c.Pos - alvo.Pos).Length <= 2.5f * tile) break;
				}
				Checa("quem sabe voar atravessa VOANDO, sem nadar", voou && !nadou, $"voou={voou} nadou={nadou}");
				Checa("...e chega do outro lado", (c.Pos - alvo.Pos).Length <= 2.5f * tile, $"{(c.Pos - alvo.Pos).Length / tile:0.0} tiles");
				Descartar(c);

				// CONTRA-EXEMPLO: alvo do MESMO lado, caminho seco -- ninguem nada nem voa a toa.
				// LONGE DA AGUA DOS DOIS LADOS: a 3 celulas da margem o corpo que recuava do alvo dava um passo
				// pra dentro do lago e decolava -- e ai o contra-exemplo media a beira, nao o seco. O alvo e
				// intocavel pra o soco nao o jogar no lago no meio da medida.
				ServerPlayer perto = Forjar("nado: alvo no seco", Celula(xL - 8, y), comCerebro: false);
				perto.Combate.Carencia = 1e6;
				ServerPlayer? d = Lutador("nado: no seco", Celula(xL - 12, y), new PerfilDeCombate(Voa: true, UsaKi: false), perto.Id);
				if (d != null)
				{
					EnsinarAVoar(d);
					d.Cerebro!.Inteligencia = 0.3;   // burro o bastante pra NAO pairar rasante por tatica -- so a agua poderia fazê-lo decolar
					Armar(d);
					bool mexeuNoModo = false, porAgua = false;
					for (int i = 0; i < 120; i++)
					{
						d.Ficha.Ki = d.Ficha.MaxKi;
						Tiques(1, d, perto);
						if (d.Nadando || d.Voando) mexeuNoModo = true;
						if (d.Cerebro!.Porque.Contains("agua", StringComparison.Ordinal)) porAgua = true;
					}
					Checa("CONTRA-EXEMPLO: com o alvo do mesmo lado e caminho seco, nem nada nem decola (e a agua nunca entra no porque)",
						  !mexeuNoModo && !porAgua, $"nadou/voou={mexeuNoModo} porque-agua={porAgua} ({d.Cerebro!.Porque})");
					Descartar(d);
				}
				Descartar(alvo, perto);
				FimDaFamilia4:
				isca = Forjar("nado: isca", Celula(xL - 8, y), comCerebro: false);
				isca.Combate.Carencia = 1e6;
			}

			GD.Print("[nadoia] -- 5) o habitante (rotina) jogado no lago: nada e volta a rotina; o que voa pousa no seco --");
			{
				ServerPlayer? h = NascerNpc("cidadao", zona, margemEsq, 777);
				Checa("PRECONDICAO: um cidadao de molde nasceu na margem", h != null);
				if (h != null)
				{
					h.Perfil = PerfilDeCombate.SoCorpo;
					h.Ficha.Ki = h.Ficha.MaxKi;
					Tiques(5, h);   // a rotina nasce (o `Rotina` e criado no primeiro tique dela)
					Checa("PRECONDICAO: o arremesso o deixou DENTRO do lago", JogarNoLago(h));
					bool nadou = false;
					for (int i = 0; i < 900; i++)
					{
						h.Ficha.Ki = h.Ficha.MaxKi;   // a pergunta e a travessia, nao o tanque do habitante
						Tiques(1, h, isca);
						if (h.Nadando) nadou = true;
						if (nadou && !h.Nadando && !PesNaAgua(h)) break;
					}
					Checa("o HABITANTE (rotina, sem cerebro de luta) jogado no lago tambem nada ate a margem", nadou && !PesNaAgua(h), $"nadou={nadou} agua={PesNaAgua(h)}");
					Checa("...e volta a rotina (ocioso ou passeando, e nao boiando)", h.Rotina is { Afazer: Afazer.Ocioso or Afazer.Passeando or Afazer.Treinando }, h.Rotina?.Afazer.ToString() ?? "sem rotina");
					RemoverNpc(h);
				}

				ServerPlayer? hv = NascerNpc("cidadao", zona, margemEsq, 778);
				if (hv != null)
				{
					hv.Perfil = PerfilDeCombate.Completo;
					EnsinarAVoar(hv);
					hv.Ficha.Ki = hv.Ficha.MaxKi;
					Tiques(5, hv);
					Checa("PRECONDICAO: o arremesso o deixou DENTRO do lago", JogarNoLago(hv));
					bool decolou = false, nadou = false;
					for (int i = 0; i < 900; i++)
					{
						hv.Ficha.Ki = hv.Ficha.MaxKi;
						Tiques(1, hv, isca);
						if (hv.Voando) decolou = true;
						if (hv.Nadando) nadou = true;
						if (decolou && !hv.Voando && hv.Altitude <= 0f && !PesNaAgua(hv)) break;
					}
					Checa("o habitante que sabe voar DECOLA do lago em vez de nadar", decolou && !nadou, $"voou={decolou} nadou={nadou}");
					Checa("...voa ate o seco e POUSA (ele nao e um voador: so saiu da agua)", decolou && !hv.Voando && hv.Altitude <= 0f && !PesNaAgua(hv),
						  $"voando={hv.Voando} alt={hv.Altitude:0} agua={PesNaAgua(hv)}");
					RemoverNpc(hv);
				}
			}

			GD.Print("[nadoia] -- 6) defeito injetado: a regra desligada --");
			{
				Travessia.DesligadaDeTeste = true;
				ServerPlayer d = Forjar("nado: regra desligada", margemEsq, perfil: PerfilDeCombate.SoCorpo);
				Armar(d);
				Checa("PRECONDICAO: o arremesso o deixou DENTRO do lago", JogarNoLago(d));
				bool mexeu = false;
				for (int i = 0; i < 300; i++) { Tiques(1, d, isca); if (d.Nadando || d.Voando) mexeu = true; }
				Checa("[injecao] com a `Travessia` DESLIGADA o corpo fica no lago (a bancada mede a regra, e nao a sorte)",
					  !mexeu && PesNaAgua(d), $"nadou/voou={mexeu} agua={PesNaAgua(d)}");
				Travessia.DesligadaDeTeste = false;
				Descartar(d, isca);
			}
		}
		catch (Exception ex) { Checa("a bancada rodou ate o fim sem excecao", false, ex.ToString()); }
		finally
		{
			Travessia.DesligadaDeTeste = false;
			foreach (ServerPlayer c in nascidos.ToList()) if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			pl.Combate.Carencia = carenciaDoHost;
			CravarPosicao(pl, posDoHost);
			if (puseu) naZona.Remove(pl);
		}
		Placar();
	}

	/// <summary>
	/// UMA FAIXA DE AGUA de 8 a 14 celulas de largura numa linha do mapa, com tres celulas de chao (e
	/// chao nas linhas vizinhas) dos dois lados, e agua tambem duas linhas acima e abaixo no miolo --
	/// pra quem cair la cair mesmo na agua, e nao numa quina. Devolve (primeira celula de agua,
	/// ultima, linha).
	/// </summary>
	private static (int XL, int XR, int Y)? Praia(ZoneCollision mapa)
	{
		for (int y = 6; y < mapa.Height - 6; y++)
		{
			int x = 24;
			while (x < mapa.Width - 24)
			{
				if (!mapa.EhAgua(x, y)) { x++; continue; }
				int x0 = x;
				while (x < mapa.Width - 24 && mapa.EhAgua(x, y)) x++;
				int x1 = x - 1;
				int largura = x1 - x0 + 1;
				if (largura < 8 || largura > 14) continue;

				bool ok = true;
				for (int k = 1; k <= 3 && ok; k++)
					for (int dy = -1; dy <= 1 && ok; dy++)
						ok = mapa.ServeDeChao(x0 - k, y + dy) && mapa.ServeDeChao(x1 + k, y + dy);
				for (int cx = x0 + 2; cx <= x1 - 2 && ok; cx++)
					for (int dy = -2; dy <= 2 && ok; dy++)
						ok = mapa.EhAgua(cx, y + dy);
				if (ok) return (x0, x1, y);
			}
		}
		return null;
	}
}
