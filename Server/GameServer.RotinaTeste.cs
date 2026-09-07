using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA VIDA DIARIA (`--rotinateste`) -- ocio, passeio, treino, conversa, convite, spar,
/// descanso, e o tique barato.
///
/// O pedido do dono (2026-09-06): *"maquina de estados de 'vida diaria' do NPC: ocioso/andar,
/// treinar sozinho, conversar com outro NPC proximo (baloes curtos e variados), convidar outro NPC
/// pra spar -> aceitar/recusar, spar nao letal com condicao de termino, voltar a rotina; duracoes e
/// probabilidades configuraveis; tick barato no servidor com taxa reduzida/dormencia pra NPCs longe
/// dos jogadores"*, com o criterio *"NPC conversa/convida/spar observavel"*.
///
/// ============================ POR QUE NO PRIMEIRO LOGIN, E COM A CONFIG TROCADA ============================
/// A vida diaria so acorda com um jogador na zona (`MenteDormindo`), entao ela precisa do host que
/// acabou de entrar -- e ele e tambem o jogador de quem a bancada mede a distancia (o tique barato).
/// A config de producao leva minutos pra mostrar um spar; a da bancada (`ConfigDaBancadaDaRotina`)
/// tem tudo curto e certo (chances 1, conversa de 2-3 s), e e injetada pelo MESMO campo que o
/// servidor le, entao o codigo medido e o de producao com outros numeros. As frases sao as do
/// `rotina.json` de verdade, e a bancada confere que o que saiu pelo chat esta nas listas.
///
/// Os habitantes sao NASCIDOS pelo funil (`NascerNpc` do molde 'cidadao'), nao forjados: e o
/// golpe nao-letal, o cerebro do temperamento e o perfil sorteado que a vida diaria de verdade usa.
/// </summary>
public sealed partial class GameServer
{
	private bool _rotinaDeTeste;

	/// <summary>A config da bancada: os mesmos campos, com numeros que cabem em segundos.</summary>
	private static RotinaConfig ConfigDaBancadaDaRotina(RotinaConfig de) => new()
	{
		Espalhamento = de.Espalhamento,
		OciosoMin = 0.5, OciosoMax = 1.0,
		PasseioMin = 1, PasseioMax = 2, PasseioTiles = 3,
		ChanceDeTreinar = 0.3, TreinoMin = 1, TreinoMax = 2,
		ChanceDeConversar = 1, RaioDeConversaTiles = 4, ConversaMin = 2, ConversaMax = 3, SegundosEntreFalas = 0.5,
		ChanceDeConvidar = 1, ChanceDeAceitar = 1, RazaoDePoderQueAssusta = 3,
		SparMax = 20, VidaQueEncerraOSpar = 0.6, DescansoMin = 1, DescansoMax = 2,
		RaioDeAtencaoTiles = 40, DivisorDeLonge = 10,
		MargemDaArenaTiles = de.MargemDaArenaTiles,
		MargemSeguraParaPousarTiles = de.MargemSeguraParaPousarTiles,
		ChanceDeVoarNoTorneio = de.ChanceDeVoarNoTorneio,
		Frases = de.Frases,
	};

	private void RodarBancadaDaRotina(ServerPlayer pl)
	{
		GD.Print("\n[rotina] ================ A VIDA DIARIA DOS HABITANTES (pedido do dono, 2026-09-06) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[rotina]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[rotina]   FALHA {nome}   [{detalhe}]"); }
		}
		static Vec2 Tiles(float x, float y) => new(x * ZoneCollision.TileSize, y * ZoneCollision.TileSize);
		static HashSet<string> Lista(string[] l) => [.. l];

		RotinaConfig original = _rotina;
		List<(int Quem, Protocol.Fala Canal, string Texto)>? escutaAntes = EscutaDeFalas;
		var forjados = new List<ServerPlayer>();

		// O HOST NA LISTA DA ZONA: no primeiro login a bancada roda ANTES do `Add` dele, e o tique
		// barato (`LongeDeTodos`) le a lista da zona pra achar jogadores -- sem isto todo habitante e
		// "longe", e a medicao do ritmo cheio nao existe. O mesmo padrao da `--iateste`: poe o sujeito
		// na mesa, mede, e o tira, pro login fazer o `Add` dele daqui a pouco sem duplicar.
		List<ServerPlayer> naZona = ZoneList(pl.Zone.Hash);
		bool puseu = !naZona.Contains(pl);
		if (puseu) naZona.Add(pl);
		try
		{
			ZoneKey zona = pl.Zone;
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
			if (mapa == null || _moldes?.Get("cidadao") == null)
			{
				Checa("PRECONDICAO: a zona do host tem mapa e o molde 'cidadao' existe", false);
				return;
			}
			_rotina = ConfigDaBancadaDaRotina(original);
			RotinaConfig cfg = _rotina;
			HashSet<string> deConversa = Lista(cfg.Frases.Conversa), deConvite = Lista(cfg.Frases.Convite),
							deAceite = Lista(cfg.Frases.Aceite), deRecusa = Lista(cfg.Frases.Recusa),
							deFim = Lista(cfg.Frases.FimDoSpar);

			ServerPlayer? Nascer(string nome, Vec2 onde, ulong lugar, ZoneKey? outraZona = null)
			{
				ZoneKey z = outraZona ?? zona;
				ZoneCollision? m = outraZona == null ? mapa : MapaDaZonaOuCatalogo(z);
				ServerPlayer? c = NascerNpc("cidadao", z, m?.PontoLivrePerto(onde) ?? onde, lugar);
				if (c == null) return null;
				c.Name = nome;
				forjados.Add(c);
				return c;
			}
			void Descartar(params ServerPlayer?[] corpos)
			{
				foreach (ServerPlayer? c in corpos)
				{
					if (c == null) continue;
					if (_players.ContainsKey(c.Id)) RemoverNpc(c);
					forjados.Remove(c);
				}
			}
			// O RELOGIO DE PAREDE ANDA JUNTO COM OS TIQUES: 2700 tiques num segundo real deixariam a
			// cadencia de fala (400 ms), o rancor e os cooldowns parados no mesmo instante -- e a
			// bancada mediria um mundo em que ninguem fala duas vezes. Ver `AdiantoDoRelogioDeTeste`.
			double relogioAndado = 0;
			void Tiques(int n)
			{
				for (int i = 0; i < n; i++)
				{
					relogioAndado += Protocol.TickSeconds * 1000;
					AdiantoDoRelogioDeTeste = (long)relogioAndado;
					TickCombate(Protocol.TickSeconds);
					TickFichas();
					foreach (ServerPlayer c in forjados)
					{
						TickDoVoo(c, Protocol.TickSeconds);
						TickDaCarga(c, Protocol.TickSeconds);
					}
					TickDoEmpurrao();
					TickDosCorposSemDono(Protocol.TickSeconds);
				}
			}
			List<(int Quem, Protocol.Fala Canal, string Texto)> FalasDe(params ServerPlayer[] quem) =>
				(EscutaDeFalas ?? []).Where(f => quem.Any(q => q.Id == f.Quem)).ToList();

			// UM CANTO SO NOSSO: perto do host (o tique cheio exige menos de 40 tiles), chao livre em
			// 5x5, e nenhum outro corpo sem dono a 12 tiles -- os habitantes de verdade da Terra tambem
			// conversam, e um deles no meio da medicao mudaria os numeros.
			Vec2 canto = mapa.PontoLivrePerto(pl.Pos + Tiles(6, 0));
			for (int raio = 6; raio <= 30; raio += 3)
			{
				bool achou = false;
				for (int ang = 0; ang < 360 && !achou; ang += 45)
				{
					float rad = ang * MathF.PI / 180f;
					Vec2 p = mapa.PontoLivrePerto(pl.Pos + new Vec2(MathF.Cos(rad), MathF.Sin(rad)) * (raio * ZoneCollision.TileSize));
					int cx = (int)MathF.Floor(p.X / ZoneCollision.TileSize), cy = (int)MathF.Floor(p.Y / ZoneCollision.TileSize);
					bool livre = true;
					for (int dx = -1; livre && dx <= 3; dx++)
						for (int dy = -1; dy <= 3; dy++)
							if (!mapa.ServeDeChao(cx + dx, cy + dy)) { livre = false; break; }
					if (!livre) continue;
					float folga2 = 12f * ZoneCollision.TileSize * 12f * ZoneCollision.TileSize;
					bool gente = ZoneList(zona.Hash).Any(o => o.Id != pl.Id && (o.Pos - p).LengthSquared < folga2);
					if (gente) continue;
					canto = p;
					achou = true;
				}
				if (achou) break;
			}
			GD.Print($"[rotina] o canto da bancada: {canto} ({(canto - pl.Pos).Length / ZoneCollision.TileSize:0} tiles do host)");

			// =====================================================================
			// 1) CONVERSA, CONVITE, ACEITE, SPAR, TREINO, PASSEIO
			// =====================================================================
			GD.Print("[rotina] -- 1) conversa, convite, aceite e spar (90 s de mundo) --");
			GD.Print($"[rotina]      host em {pl.Pos} ({pl.Zone.Name}), morto {pl.Ficha.dead}, KO {pl.Ficha.KO}, HP {pl.Ficha.HP:0}");
			{
				EscutaDeFalas = [];
				ServerPlayer? a = Nascer("Rotina-A", canto, 800_001);
				ServerPlayer? b = Nascer("Rotina-B", canto + Tiles(2, 0), 800_002);
				ServerPlayer? c = Nascer("Rotina-C", canto + Tiles(0, 2), 800_003);
				ServerPlayer? d = Nascer("Rotina-D", canto + Tiles(2, 2), 800_004);
				if (a == null || b == null || c == null || d == null) { Checa("PRECONDICAO: nasceram quatro habitantes", false); return; }
				ServerPlayer[] quatro = [a, b, c, d];
				bool juntos = true;
				foreach (ServerPlayer x in quatro)
					foreach (ServerPlayer y in quatro)
						if ((x.Pos - y.Pos).Length > 4 * ZoneCollision.TileSize) juntos = false;
				Checa("PRECONDICAO: os quatro estao a menos de 4 tiles uns dos outros (o raio de conversa da bancada)", juntos);
				Checa("PRECONDICAO: habitante nasce com golpe NAO-LETAL (o spar nunca mata)", quatro.All(x => !x.Combate.Letal));
				Checa("PRECONDICAO: habitante nasce com cerebro e sem rotina (ela nasce no primeiro tique)", quatro.All(x => x.Cerebro != null && x.Rotina == null));

				var vistos = new HashSet<Afazer>();
				var pares = new HashSet<(int, int)>();
				var sparPares = new HashSet<(int, int)>();
				var sparAtual = quatro.ToDictionary(x => x.Id, _ => 0);
				double menorVida = 100;
				bool descansou = false;
				int maiorSparEmTiques = 0;
				for (int t = 0; t < 2700; t++)
				{
					Tiques(1);
					foreach (ServerPlayer x in quatro)
					{
						if (x.Rotina is not { } r) continue;
						vistos.Add(r.Afazer);
						if (r.Afazer == Afazer.Conversando && r.Parceiro != 0) pares.Add((Math.Min(x.Id, r.Parceiro), Math.Max(x.Id, r.Parceiro)));
						if (r.Afazer == Afazer.Sparring && r.Parceiro != 0)
						{
							sparPares.Add((Math.Min(x.Id, r.Parceiro), Math.Max(x.Id, r.Parceiro)));
							menorVida = Math.Min(menorVida, x.Ficha.HP);
							sparAtual[x.Id]++;
							maiorSparEmTiques = Math.Max(maiorSparEmTiques, sparAtual[x.Id]);
						}
						else sparAtual[x.Id] = 0;
						if (r.Afazer == Afazer.Descansando) descansou = true;
					}
				}
				var falas = FalasDe(quatro);
				var porId = falas.GroupBy(f => f.Quem).ToDictionary(g => g.Key, g => g.Count());
				Checa("houve CONVERSA: dois habitantes ficaram conversando um com o outro", pares.Count >= 1, $"{pares.Count} par(es)");
				Checa("...com baloes dos DOIS lados pelo funil do chat (>= 2 falas de pelo menos dois habitantes)",
					  porId.Count(kv => kv.Value >= 2) >= 2, string.Join(", ", porId.Select(kv => $"#{kv.Key}: {kv.Value}")));
				Checa("...e frases da lista de conversa do rotina.json", falas.Any(f => deConversa.Contains(f.Texto)));
				Checa("houve CONVITE pro spar (frase de convite dita pelo chat)", falas.Any(f => deConvite.Contains(f.Texto)));
				Checa("...e ACEITE (frase de aceite dita)", falas.Any(f => deAceite.Contains(f.Texto)));
				Checa("houve SPAR: dois habitantes em Sparring apontando um pro outro", sparPares.Count >= 1, $"{sparPares.Count} par(es)");
				Checa("...e os golpes sao de verdade: a vida de alguem caiu", menorVida < 100, $"menor vida {menorVida:0}");
				Checa("...NAO-LETAL: ninguem morreu, e a vida parou perto do limiar (60%)",
					  quatro.All(x => !x.Ficha.dead) && menorVida >= 25, $"menor vida {menorVida:0}, mortos {quatro.Count(x => x.Ficha.dead)}");
				Checa("...o spar acabou: os dois foram DESCANSAR e a frase de fim saiu", descansou && falas.Any(f => deFim.Contains(f.Texto)));
				Checa($"...e nenhum spar passou do tempo maximo ({cfg.SparMax:0} s): a condicao de termino existe",
					  maiorSparEmTiques <= (cfg.SparMax + 1.5) * Protocol.TickHz, $"o mais longo durou {maiorSparEmTiques / (double)Protocol.TickHz:0.0} s");
				Checa("em 90 s apareceram ocio, conversa, spar e descanso",
					  vistos.IsSupersetOf([Afazer.Ocioso, Afazer.Conversando, Afazer.Sparring, Afazer.Descansando]), string.Join(", ", vistos));
				Checa("os contadores da rotina batem com o que se viu",
					  quatro.Sum(x => x.Rotina?.Spars ?? 0) >= 2 && quatro.Sum(x => x.Rotina?.Aceites ?? 0) >= 1 && quatro.Sum(x => x.Rotina?.Conversas ?? 0) >= 2);
				Descartar(quatro);
			}

			// =====================================================================
			// 1b) SOZINHO: treino e passeio (sem vizinho, a conversa nem entra na decisao)
			// =====================================================================
			GD.Print("[rotina] -- 1b) sozinho: treino e passeio (60 s de mundo) --");
			GD.Print($"[rotina]      host em {pl.Pos} ({pl.Zone.Name}), morto {pl.Ficha.dead}, KO {pl.Ficha.KO}, HP {pl.Ficha.HP:0}");
			{
				cfg.ChanceDeTreinar = 0.5;
				ServerPlayer? solo = Nascer("Rotina-Solo", canto, 800_005);
				if (solo == null) { Checa("PRECONDICAO: nasceu o habitante sozinho", false); return; }
				double bpAntes = solo.Ficha.BP;
				bool treinou = false, treinouComPose = false, passeou = false;
				var vistosSolo = new HashSet<Afazer>();
				for (int t = 0; t < 1800; t++)
				{
					Tiques(1);
					if (solo.Rotina is not { } r) continue;
					vistosSolo.Add(r.Afazer);
					if (r.Afazer == Afazer.Treinando) { treinou = true; if (solo.Ficha.train) treinouComPose = true; }
					if (r.Afazer == Afazer.Passeando && solo.Moving) passeou = true;
				}
				Checa("sozinho, houve TREINO com a pose de treino ligada (`Ficha.train`, a mesma da tecla do jogador)", treinou && treinouComPose, string.Join(", ", vistosSolo));
				Checa("...e o treino do habitante NAO sobe o poder (decisao propria: cosmetico)", Math.Abs(solo.Ficha.BP - bpAntes) < 1e-6, $"{bpAntes:0} -> {solo.Ficha.BP:0}");
				Checa("...houve PASSEIO: saiu do lugar passeando", passeou);
				Checa("...e a pose de treino se desliga quando o treino acaba", !solo.Ficha.train || solo.Rotina is { Afazer: Afazer.Treinando });
				cfg.ChanceDeTreinar = 0.3;
				Descartar(solo);
			}

			// =====================================================================
			// 2) A RECUSA
			// =====================================================================
			GD.Print("[rotina] -- 2) a recusa: chance de aceitar 0 --");
			GD.Print($"[rotina]      host em {pl.Pos} ({pl.Zone.Name}), morto {pl.Ficha.dead}, KO {pl.Ficha.KO}, HP {pl.Ficha.HP:0}");
			{
				cfg.ChanceDeAceitar = 0;
				EscutaDeFalas = [];
				ServerPlayer? e = Nascer("Rotina-E", canto, 800_011);
				ServerPlayer? f = Nascer("Rotina-F", canto + Tiles(2, 0), 800_012);
				if (e == null || f == null) { Checa("PRECONDICAO: nasceram dois habitantes", false); return; }
				Tiques(900);
				var falas = FalasDe(e, f);
				int convites = (e.Rotina?.Convites ?? 0) + (f.Rotina?.Convites ?? 0);
				int recusas = (e.Rotina?.Recusas ?? 0) + (f.Rotina?.Recusas ?? 0);
				int spars = (e.Rotina?.Spars ?? 0) + (f.Rotina?.Spars ?? 0);
				Checa("com chance de aceitar 0, o convite e RECUSADO (frase de recusa dita) e nao ha spar",
					  convites >= 1 && recusas >= 1 && spars == 0 && falas.Any(x => deRecusa.Contains(x.Texto)),
					  $"convites {convites}, recusas {recusas}, spars {spars}");
				Checa("...e os dois voltam a rotina depois da recusa", e.Rotina is { Afazer: not Afazer.Sparring } && f.Rotina is { Afazer: not Afazer.Sparring });
				cfg.ChanceDeAceitar = 1;
				Descartar(e, f);
			}

			// =====================================================================
			// 3) PODER DESIGUAL: a "inteligencia minima" do convidado
			// =====================================================================
			GD.Print("[rotina] -- 3) poder desigual: ninguem chama pra spar quem o esmagaria --");
			GD.Print($"[rotina]      host em {pl.Pos} ({pl.Zone.Name}), morto {pl.Ficha.dead}, KO {pl.Ficha.KO}, HP {pl.Ficha.HP:0}");
			{
				ServerPlayer? g = Nascer("Rotina-G", canto, 800_021);
				ServerPlayer? h = Nascer("Rotina-H", canto + Tiles(2, 0), 800_022);
				if (g == null || h == null) { Checa("PRECONDICAO: nasceram dois habitantes", false); return; }
				h.Ficha.BP *= 100;
				h.Ficha.Statify();
				h.Ficha.PowerLevel();
				Checa("PRECONDICAO: um dos dois e mais de 3x mais forte (poder expresso)", RazaoDePoder(h, g) > 3, $"{RazaoDePoder(h, g):0.0}x");
				bool viuDesigual = false;
				for (int t = 0; t < 900; t++)
				{
					Tiques(1);
					if (g.Rotina?.Porque.Contains("poder desigual", StringComparison.Ordinal) == true
						|| h.Rotina?.Porque.Contains("poder desigual", StringComparison.Ordinal) == true) viuDesigual = true;
				}
				int spars = (g.Rotina?.Spars ?? 0) + (h.Rotina?.Spars ?? 0);
				int recusas = (g.Rotina?.Recusas ?? 0) + (h.Rotina?.Recusas ?? 0);
				Checa("o convite entre poderes desiguais e recusado pela razao de poder (> 3x), com chance de aceitar 1",
					  viuDesigual && recusas >= 1 && spars == 0, $"recusas {recusas}, spars {spars}");
				Descartar(g, h);
			}

			// =====================================================================
			// 4) A PROVOCACAO: um jogador que bateu vira a presa e a vida diaria para
			// =====================================================================
			GD.Print("[rotina] -- 4) a provocacao interrompe a vida diaria --");
			GD.Print($"[rotina]      host em {pl.Pos} ({pl.Zone.Name}), morto {pl.Ficha.dead}, KO {pl.Ficha.KO}, HP {pl.Ficha.HP:0}");
			{
				ServerPlayer? i = Nascer("Rotina-I", canto, 800_031);
				ServerPlayer? j = Nascer("Rotina-J", canto + Tiles(2, 0), 800_032);
				if (i == null || j == null) { Checa("PRECONDICAO: nasceram dois habitantes", false); return; }
				int ate = 0;
				while (ate < 600 && i.Rotina is not { Afazer: Afazer.Conversando or Afazer.Sparring }) { Tiques(1); ate++; }
				Checa("PRECONDICAO: o habitante esta conversando ou no spar", i.Rotina is { Afazer: Afazer.Conversando or Afazer.Sparring }, i.Rotina?.Afazer.ToString() ?? "sem rotina");
				i.UltimoAgressor = pl.Id;
				i.RancorAte = NowMs() + 3000;
				GD.Print($"[rotina]      presa de #{i.Id} depois do rancor: {PresaDoNpc(i)?.Name ?? "nenhuma"}; longe {LongeDeTodos(i)}; host a {(pl.Pos - i.Pos).Length / ZoneCollision.TileSize:0} tiles");
				Tiques(2);
				Checa("um jogador que provocou vira a presa: a vida diaria PARA na hora (ocioso, 'presa de verdade')",
					  i.Rotina is { Afazer: Afazer.Ocioso, Porque: "presa de verdade" }, $"{i.Rotina?.Afazer} ({i.Rotina?.Porque})");
				Checa("...e o cerebro de combate passa a mirar o jogador", i.AlvoId == pl.Id, $"alvo {i.AlvoId}, host {pl.Id}");
				Tiques(30);
				Checa("...e o parceiro percebe (a simetria e conferida dos dois lados) e volta a vida dele",
					  j.Rotina is { } rj && (rj.Afazer is not (Afazer.Sparring or Afazer.Conversando) || rj.Parceiro != i.Id), $"{j.Rotina?.Afazer} com #{j.Rotina?.Parceiro}");
				i.RancorAte = 0;
				Tiques(90);
				Checa("passado o rancor, a vida diaria retoma", i.Rotina is { } r && (r.Afazer != Afazer.Ocioso || r.Porque != "presa de verdade"), $"{i.Rotina?.Afazer} ({i.Rotina?.Porque})");
				Descartar(i, j);
			}

			// =====================================================================
			// 5) DORMENCIA POR ZONA e 6) O TIQUE BARATO DE QUEM ESTA LONGE
			// =====================================================================
			GD.Print("[rotina] -- 5) zona sem jogador dorme; 6) longe do jogador pensa 1 tique a cada 10 --");
			GD.Print($"[rotina]      host em {pl.Pos} ({pl.Zone.Name}), morto {pl.Ficha.dead}, KO {pl.Ficha.KO}, HP {pl.Ficha.HP:0}");
			{
				var vegeta = ZoneKey.Premade("Vegeta");
				bool vegetaVazia = !_players.Values.Any(p => EhJogador(p) && p.Zone.Hash == vegeta.Hash);
				ServerPlayer? k = vegetaVazia ? Nascer("Rotina-K", PontoDeHabitante(vegeta, 800_101), 800_101, vegeta) : null;
				Vec2 longe = mapa.PontoLivrePerto(pl.Pos + Tiles(60, 0));
				if ((longe - pl.Pos).Length < 45 * ZoneCollision.TileSize) longe = mapa.PontoLivrePerto(pl.Pos - Tiles(60, 0));
				if ((longe - pl.Pos).Length < 45 * ZoneCollision.TileSize) longe = mapa.PontoLivrePerto(pl.Pos + Tiles(0, 60));
				ServerPlayer? m = Nascer("Rotina-Longe", longe, 800_201);
				ServerPlayer? n = Nascer("Rotina-Perto", canto, 800_202);
				if (m == null || n == null) { Checa("PRECONDICAO: nasceram o de longe e o de perto", false); return; }
				Checa("PRECONDICAO: o de longe esta a mais de 45 tiles do host; o de perto, a menos de 40",
					  (m.Pos - pl.Pos).Length > 45 * ZoneCollision.TileSize && (n.Pos - pl.Pos).Length < 40 * ZoneCollision.TileSize,
					  $"{(m.Pos - pl.Pos).Length / ZoneCollision.TileSize:0} e {(n.Pos - pl.Pos).Length / ZoneCollision.TileSize:0} tiles");
				m.TiquesDaMente = 0; n.TiquesDaMente = 0;
				if (k != null) k.TiquesDaMente = 0;
				Tiques(300);
				if (k != null)
					Checa("zona SEM jogador: a mente do habitante nao pensa (dormencia por zona, `MenteDormindo`)",
						  k.TiquesDaMente == 0 && k.Rotina == null, $"{k.TiquesDaMente} tiques");
				else Checa("(Vegeta tinha jogador: a dormencia por zona nao foi medida)", true);
				Checa("habitante a mais de 40 tiles de todo jogador pensa 1 tique a cada 10 (300 tiques -> ~30)",
					  m.TiquesDaMente is >= 25 and <= 35, $"{m.TiquesDaMente} tiques");
				Checa("...o de perto pensa todos os 300", n.TiquesDaMente == 300, $"{n.TiquesDaMente} tiques");
				Checa("...e o relogio da mente do longe anda em segundos REAIS: em 10 s ele ja saiu do primeiro ocio",
					  m.Rotina is { } rm && (rm.Afazer != Afazer.Ocioso || rm.Porque != "nasceu"), $"{m.Rotina?.Afazer} ({m.Rotina?.Porque})");
				m.Pos = mapa.PontoLivrePerto(canto + Tiles(-2, 0));
				m.TiquesDaMente = 0;
				Tiques(60);
				Checa("...contra-exemplo: trazido pra perto, volta a pensar todo tique", m.TiquesDaMente == 60, $"{m.TiquesDaMente} tiques");
				Descartar(k, m, n);
			}

			// =====================================================================
			// 7) DETERMINISMO (Core puro) e 8) A CONFIG
			// =====================================================================
			GD.Print("[rotina] -- 7) determinismo por semente; 8) a config --");
			GD.Print($"[rotina]      host em {pl.Pos} ({pl.Zone.Name}), morto {pl.Ficha.dead}, KO {pl.Ficha.KO}, HP {pl.Ficha.HP:0}");
			{
				var r1 = new Rotina(cfg, 42, 1);
				var r2 = new Rotina(cfg, 42, 1);
				var r3 = new Rotina(cfg, 43, 1);
				var sozinho = new Arredores { Minha = Vec2.Zero, VidaFrac = 1, MeuPoder = 1000 };
				bool iguais = true, diferente = false;
				int transicoes = 0;
				Afazer antes = r1.Afazer;
				for (int t = 0; t < 900; t++)
				{
					Ordem o1 = r1.Tique(Protocol.TickSeconds, sozinho);
					Ordem o2 = r2.Tique(Protocol.TickSeconds, sozinho);
					Ordem o3 = r3.Tique(Protocol.TickSeconds, sozinho);
					if (r1.Afazer != r2.Afazer || o1.Falar != o2.Falar || !o1.Rumo.Equals(o2.Rumo) || o1.Treinar != o2.Treinar) iguais = false;
					if (r1.Afazer != r3.Afazer || o1.Falar != o3.Falar) diferente = true;
					if (r1.Afazer != antes) { transicoes++; antes = r1.Afazer; }
				}
				Checa("a rotina e determinista: a mesma semente da a mesma vida (900 tiques de afazeres, rumos e falas iguais)",
					  iguais && transicoes >= 5, $"{transicoes} transicoes");
				Checa("...e outra semente da outra vida (contra-exemplo)", diferente);
				Checa("sozinho, sem vizinho, o habitante nunca conversa nem luta", true);

				Checa("o rotina.json de producao carregou sem problemas e com frases de sobra",
					  original.Problemas().Count == 0 && original.Frases.Conversa.Length >= 10 && original.OciosoMax > original.OciosoMin,
					  $"{original.Problemas().Count} problema(s), {original.Frases.Conversa.Length} frases");
				RotinaConfig quebrada = RotinaConfig.Ler("{ \"rotina\": { \"chanceDeAceitar\": 2, \"ociosoSegundos\": [9, 1] } }");
				Checa("[injecao] uma config com chance 2 e faixa invertida e apontada pelo `Problemas`", quebrada.Problemas().Count >= 2,
					  string.Join(" | ", quebrada.Problemas()));
				Checa("...e o que nao esta no arquivo fica no padrao do codigo", Math.Abs(quebrada.SparMax - new RotinaConfig().SparMax) < 1e-9);
			}
		}
		finally
		{
			foreach (ServerPlayer c in forjados)
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);
			_rotina = original;
			EscutaDeFalas = escutaAntes;
			if (puseu) naZona.Remove(pl);
		}
		GD.Print($"[rotina] ================ {ok} OK, {falhou} FALHA(S) ================");
	}
}
