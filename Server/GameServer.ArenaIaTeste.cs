using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.World;
using Jandirus.Net;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DA LINHA QUE A IA NAO CRUZA (`--arenaiateste`) -- consciencia de arena, voo de
/// emergencia e perfis de combate.
///
/// O pedido do dono (2026-09-06): *"consciencia de borda de arena (nao sair andando pra fora),
/// reagir a knockback voando pra evitar ring-out se puder voar, perfis de NPC com skills variaveis
/// (uns voam, uns usam tecnicas de ki, outros so corpo a corpo)"*, com o criterio *"NPC voador se
/// salva do ring-out, nao voador perde"*.
///
/// ============================ O QUE SO ELA RESPONDE ============================
/// O `NaArena` do cerebro e Core puro. O que a mesa nao prova e que o corpo DE PRODUCAO obedece:
/// que o `AlternarVoo` que o cerebro pede cobra o Ki e liga o `Voando` de verdade, que o passo
/// pro centro passa pela colisao da Terra, e que o arremesso (`Arremessar`, o mesmo do soco forte)
/// e quem leva o corpo ate a linha. Por isso ela roda na TERRA, numa clareira do mapa de verdade,
/// e nao num mapa vazio.
///
/// Roda no boot: forja corpos sem molde (que nao dormem sem jogador) e os remove no `finally`.
/// </summary>
public sealed partial class GameServer
{
	private void RodarBancadaDaArenaDaIa()
	{
		GD.Print("\n[arenaia] ================ A LINHA QUE A IA NAO CRUZA (pedido do dono, 2026-09-06) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[arenaia]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[arenaia]   FALHA {nome}   [{detalhe}]"); }
		}
		void Placar() => GD.Print($"[arenaia] ================ {ok} OK, {falhou} FALHA(S) ================");

		var zona = ZoneKey.Premade("Earth");
		ZoneCollision? mapa = MapaDaZonaOuCatalogo(zona);
		if (mapa == null) { Checa("PRECONDICAO: a Terra tem mapa", false); Placar(); return; }

		// A CLAREIRA: um quadrado de chao livre no mapa de verdade. A arena fica no miolo dele, com
		// folga de 4 celulas de cada lado -- e pra fora da linha ainda ser chao, senao o "cruzou a
		// linha" viraria "bateu na parede".
		const int lado = 24;
		if (Clareira(mapa, PontoDeNascimento(zona), lado) is not { } canto)
		{
			Checa($"PRECONDICAO: ha uma clareira de {lado}x{lado} celulas de chao na Terra", false);
			Placar();
			return;
		}
		var arena = new Arena(canto.X + 4, canto.Y + 4, canto.X + lado - 5, canto.Y + lado - 5);
		Vec2 centro = arena.Centro;
		float linhaLeste = (arena.X2 + 1) * ZoneCollision.TileSize;
		GD.Print($"[arenaia] clareira em ({canto.X},{canto.Y}); {arena}; centro {centro}; linha leste em x={linhaLeste:0}");

		var nascidos = new List<ServerPlayer>();
		ServerPlayer Forjar(string nome, Vec2 pos, bool comCerebro = true, PerfilDeCombate? perfil = null, bool naArena = true)
		{
			var f = new Jandirus.Core.Stats.Fighter
			{
				Name = nome, Race = "Saiyan", BP = 50_000,
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
				Perfil = perfil ?? PerfilDeCombate.Completo,
				Arena = naArena ? arena : null,
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
				foreach (ServerPlayer c in corpos) TickDoVoo(c, Protocol.TickSeconds);
				foreach (ServerPlayer c in corpos) TickDaCarga(c, Protocol.TickSeconds);
				TickDoEmpurrao();
				TickDosCorposSemDono(Protocol.TickSeconds);
			}
		}
		void Descartar(params ServerPlayer[] corpos)
		{
			foreach (ServerPlayer c in corpos) { if (_players.ContainsKey(c.Id)) RemoverNpc(c); nascidos.Remove(c); }
		}
		Vec2 Tiles(float x, float y) => new(x * ZoneCollision.TileSize, y * ZoneCollision.TileSize);

		try
		{
			// =====================================================================
			// 1) NA MARGEM, SEM VOO: recua pro centro em vez de ir atras do alvo que esta fora
			// =====================================================================
			GD.Print("[arenaia] -- 1) na margem, sem voo, o corpo recua pro centro --");
			{
				ServerPlayer alvo = Forjar("arena: alvo fora", new Vec2(linhaLeste + Tiles(3, 0).X, centro.Y), comCerebro: false);
				Vec2 partida = new(linhaLeste - Tiles(1.5f, 0).X, centro.Y);   // uma celula e meia da linha: margem 1
				ServerPlayer a = Forjar("arena: so corpo", partida, perfil: PerfilDeCombate.SoCorpo);
				Armar(a);
				Checa("PRECONDICAO: o corpo esta na margem (1 celula da linha)", arena.MargemEmCelulas(a.Pos) == 1, $"{arena.MargemEmCelulas(a.Pos)}");
				int maiorMargem = 0;
				bool cruzou = false;
				string primeiraSaida = "";
				for (int i = 0; i < 90; i++)
				{
					Tiques(1, a, alvo);
					maiorMargem = Math.Max(maiorMargem, arena.MargemEmCelulas(a.Pos));
					if (!arena.ContemEm(a.Pos) && !cruzou)
					{
						cruzou = true;
						primeiraSaida = $"tique {i}: {a.Pos}, plano {a.Cerebro!.Atual}, porque '{a.Cerebro.Porque}', correndo {a.Correndo}, voo {a.TiquesDeVoo}";
					}
				}
				Checa("com o alvo FORA da linha, o corpo na margem anda pro CENTRO (a margem cresce)", maiorMargem >= 2, $"maior margem {maiorMargem}");
				Checa("...e em 3 s nunca cruza a linha", !cruzou, cruzou ? primeiraSaida : $"x final {a.Pos.X:0}, linha {linhaLeste:0}");
				Checa("...e o cerebro diz por que", a.Cerebro!.Porque.Contains("arena", StringComparison.Ordinal), a.Cerebro.Porque);

				a.Arena = null;
				a.Pos = partida;
				Tiques(90, a, alvo);
				Checa("[injecao] SEM a arena o mesmo corpo vai atras do alvo e cruza a linha", !arena.ContemEm(a.Pos), $"x {a.Pos.X:0}, linha {linhaLeste:0}");
				Descartar(a, alvo);
			}

			// =====================================================================
			// 2) ARREMESSADO PRA FORA: o voador decola antes da linha; o 'so corpo' sai
			// =====================================================================
			GD.Print("[arenaia] -- 2) arremessado pra fora: quem voa se salva, quem nao voa perde --");
			{
				ServerPlayer alvo = Forjar("arena: parceiro", centro, comCerebro: false);
				Vec2 partida = new(linhaLeste - Tiles(6.5f, 0).X, centro.Y);
				ServerPlayer voador = Forjar("arena: voador", partida + Tiles(0, -3));
				ServerPlayer pesado = Forjar("arena: so corpo", partida + Tiles(0, 3), perfil: PerfilDeCombate.SoCorpo);
				EnsinarAVoar(voador); EnsinarAVoar(pesado);
				Armar(voador); Armar(pesado);
				Checa("PRECONDICAO: o voador sabe voar pelo funil do jogador (`PodeVoar`)", voador.Cerebro!.Poderes.PodeVoar);
				Checa("PRECONDICAO: o 'so corpo' sabe voar pelo funil, e o PERFIL desliga", PodeVoar(pesado) && !pesado.Cerebro!.Poderes.PodeVoar);
				Checa("PRECONDICAO: os dois estao a 6 celulas da linha leste, fora da margem",
					  arena.MargemEmCelulas(voador.Pos) >= 4 && arena.MargemEmCelulas(pesado.Pos) >= 4
					  && (arena.X2 - (int)MathF.Floor(voador.Pos.X / ZoneCollision.TileSize)) == 6,
					  $"margens {arena.MargemEmCelulas(voador.Pos)} e {arena.MargemEmCelulas(pesado.Pos)}");

				// O MESMO ARREMESSO do soco forte: 8 tiques do DM a 2 tiles por tique = 16 tiles pro leste.
				Arremessar(voador, new Vec2(1, 0), 10, 8);
				Arremessar(pesado, new Vec2(1, 0), 10, 8);
				bool decolouDentro = false, voadorForaSemVoar = false, pesadoSaiu = false, pesadoSaiuSemVoar = false, pesadoVoou = false;
				string quandoForaSemVoar = "";
				double kiAntes = voador.Ficha.Ki;
				for (int i = 0; i < 90; i++)
				{
					Tiques(1, voador, pesado, alvo);
					bool vDentro = arena.ContemEm(voador.Pos), pDentro = arena.ContemEm(pesado.Pos);
					if (voador.Voando && vDentro) decolouDentro = true;
					if (!vDentro && !voador.Voando && !voadorForaSemVoar)
					{
						voadorForaSemVoar = true;
						quandoForaSemVoar = $"tique {i}: x {voador.Pos.X:0}, altitude {voador.Altitude:0}, arremesso {voador.TiquesDeVoo}, plano {voador.Cerebro!.Atual} ({voador.Cerebro.Porque})";
					}
					if (!pDentro) pesadoSaiu = true;
					if (!pDentro && !pesado.Voando) pesadoSaiuSemVoar = true;
					if (pesado.Voando) pesadoVoou = true;
					if (i % 5 == 0 && i <= 40)
						GD.Print($"[arenaia]      t={i,2}: voador x={voador.Pos.X:0} voo={voador.Voando} arremesso={voador.TiquesDeVoo} ({voador.Cerebro!.Porque})"
							   + $" | so-corpo x={pesado.Pos.X:0} arremesso={pesado.TiquesDeVoo} | linha {linhaLeste:0}");
				}
				Checa("arremessado pra fora, o VOADOR decola ANTES de cruzar a linha (o voo de emergencia do `trn_ai_assist`)",
					  decolouDentro, $"voando {voador.Voando}, x {voador.Pos.X:0}, linha {linhaLeste:0}");
				Checa("...pagando o Ki de decolar pelo funil do jogador", voador.Ficha.Ki < kiAntes, $"{kiAntes:0} -> {voador.Ficha.Ki:0}");
				Checa("...e nunca esteve fora da linha SEM voar (voando, o ring-out nao vale)", !voadorForaSemVoar, voadorForaSemVoar ? quandoForaSemVoar : voador.Cerebro!.Porque);
				Checa("o 'so corpo' arremessado pela MESMA forca cruza a linha SEM voar (o perfil nao deixa)",
					  pesadoSaiu && pesadoSaiuSemVoar && !pesadoVoou, $"saiu {pesadoSaiu}, voou {pesadoVoou}, x final {pesado.Pos.X:0}");
				Descartar(voador, pesado, alvo);
			}

			// =====================================================================
			// 3) OS PERFIS: o que cada um corta, e o que nenhum da
			// =====================================================================
			GD.Print("[arenaia] -- 3) os perfis de combate --");
			{
				ServerPlayer atirador = Forjar("arena: atirador", centro + Tiles(-5, 0), naArena: false);
				atirador.Livro.Dar("/datum/skill/ki/Ki_Wave");
				Capacidades tudo = LerCapacidades(atirador);
				Checa("PRECONDICAO: com o Ki Wave no livro, o arsenal de longe tem alguma coisa", tudo.DeLonge.TemAlguma, $"{tudo.DeLonge.Quantas} tiro(s)");
				Checa("o perfil COMPLETO nao corta nada", PerfilDeCombate.Completo.Filtrar(tudo).DeLonge.TemAlguma
					  && PerfilDeCombate.Completo.Filtrar(tudo).SabeSopro == tudo.SabeSopro);
				Capacidades soCorpo = PerfilDeCombate.SoCorpo.Filtrar(tudo);
				Checa("o perfil SO CORPO desliga o arsenal, o sopro e o voo", !soCorpo.DeLonge.TemAlguma && !soCorpo.SabeSopro && !soCorpo.PodeVoar);
				Checa("...mas nao o reunir de Ki (carregar e coisa do corpo)", soCorpo.SabeReunirKi == tudo.SabeReunirKi);
				Capacidades soVoo = new PerfilDeCombate(true, false).Filtrar(tudo);
				Checa("o perfil 'voa, sem ki' corta o raio e o sopro, e deixa o voo como estava",
					  !soVoo.DeLonge.TemAlguma && !soVoo.SabeSopro && soVoo.PodeVoar == tudo.PodeVoar);
				Checa("o perfil nunca DA o que o funil negou: sem a maestria de Ki, `Voa = true` continua sem voar",
					  !tudo.PodeVoar && !PerfilDeCombate.Completo.Filtrar(tudo).PodeVoar);
				Checa("...e o texto do perfil e legivel no log de nascimento",
					  PerfilDeCombate.SoCorpo.ToString() == "so corpo" && PerfilDeCombate.Completo.ToString() == "completo");
				Descartar(atirador);

				int voam = 0, ki = 0;
				for (ulong s = 1; s <= 400; s++)
				{
					PerfilDeCombate p = PerfilDeCombate.Sortear(0.5, 0.6, s);
					if (p.Voa) voam++;
					if (p.UsaKi) ki++;
				}
				Checa("o sorteio do molde bate com as chances (0,5 / 0,6 em 400 sementes, folga de 10 pontos)",
					  Math.Abs(voam / 400.0 - 0.5) < 0.1 && Math.Abs(ki / 400.0 - 0.6) < 0.1, $"{voam / 4.0:0}% voam, {ki / 4.0:0}% usam ki");
				Checa("...e e deterministico: a mesma semente da o mesmo perfil", PerfilDeCombate.Sortear(0.5, 0.6, 77) == PerfilDeCombate.Sortear(0.5, 0.6, 77));
				bool sempreCompleto = true, sempreSoCorpo = true;
				for (ulong s = 1; s <= 50; s++)
				{
					if (PerfilDeCombate.Sortear(1, 1, s) != PerfilDeCombate.Completo) sempreCompleto = false;
					if (PerfilDeCombate.Sortear(0, 0, s) != PerfilDeCombate.SoCorpo) sempreSoCorpo = false;
				}
				Checa("...chance 1/1 = completo sempre; 0/0 = so corpo sempre (contra-exemplos)", sempreCompleto && sempreSoCorpo);

				// NO FUNIL DE PRODUCAO: o habitante nascido do molde recebe o perfil sorteado da semente dele.
				if (_moldes?.Get("cidadao") is { } molde)
				{
					int diferentesDeCompleto = 0, batem = 0, nascidosAqui = 0;
					for (ulong lugar = 700_001; lugar <= 700_006; lugar++)
					{
						ServerPlayer? c = NascerNpc("cidadao", zona, PontoDeHabitante(zona, lugar), lugar);
						if (c == null) continue;
						nascidos.Add(c);
						nascidosAqui++;
						if (c.Perfil != PerfilDeCombate.Completo) diferentesDeCompleto++;
						ulong semente = Jandirus.Core.Npc.SorteioDeNpc.SementeDe(SeedDoUniverso, molde.Id, Espaco.Misturar(zona.Hash, lugar, 0));
						if (c.Perfil == PerfilDeCombate.Sortear(molde.ChanceDeVoar, molde.ChanceDeKi, semente)) batem++;
					}
					Checa("o habitante nascido pelo funil carrega o perfil sorteado do molde (`perfil` do npcs.json: voa 0,5 / ki 0,6)",
						  nascidosAqui == 6 && batem == 6, $"{batem} de {nascidosAqui} batem com o sorteio");
					Checa("...e entre seis habitantes ha pelo menos um que NAO usa tudo", diferentesDeCompleto >= 1, $"{diferentesDeCompleto} de {nascidosAqui}");
					Checa("...(o molde do cidadao declara o perfil)", molde.ChanceDeVoar < 1 && molde.ChanceDeKi < 1, $"voa {molde.ChanceDeVoar}, ki {molde.ChanceDeKi}");
				}
				else Checa("PRECONDICAO: o molde 'cidadao' existe", false);
			}

			// =====================================================================
			// 4) POUSA QUANDO SEGURO: voando, oponente no chao, linha longe
			// =====================================================================
			GD.Print("[arenaia] -- 4) voando com o oponente no chao e a linha longe, pousa --");
			{
				ServerPlayer alvo = Forjar("arena: alvo no chao", centro + Tiles(2, 0), comCerebro: false);
				ServerPlayer v = Forjar("arena: pousa", centro + Tiles(-2, 0));
				EnsinarAVoar(v);
				Armar(v);
				AplicarComando(v, new Comando { AlternarVoo = true }, Protocol.TickSeconds);
				Checa("PRECONDICAO: esta voando, no centro (margem >= 5)", v.Voando && arena.MargemEmCelulas(v.Pos) >= 5, $"margem {arena.MargemEmCelulas(v.Pos)}");
				bool pousou = false;
				for (int i = 0; i < 300 && !pousou; i++) { Tiques(1, v, alvo); pousou = !v.Voando; }
				Checa("com o oponente no chao e a linha longe, a IA pousa (o `flight = 0` do `trn_ai_assist`)", pousou, v.Cerebro!.Porque);
				Descartar(v, alvo);
			}

			// =====================================================================
			// 5) O DEFEITO INJETADO NA CONFIG: margem -1 = regra desligada
			// =====================================================================
			GD.Print("[arenaia] -- 5) contra-exemplo: com a margem desligada o corpo cruza --");
			{
				int guardada = _rotina.MargemDaArenaTiles;
				_rotina.MargemDaArenaTiles = -1;
				ServerPlayer alvo = Forjar("arena: alvo fora", new Vec2(linhaLeste + Tiles(3, 0).X, centro.Y), comCerebro: false);
				ServerPlayer a = Forjar("arena: sem margem", new Vec2(linhaLeste - Tiles(1.5f, 0).X, centro.Y), perfil: PerfilDeCombate.SoCorpo);
				Armar(a);
				bool saiu = false;
				for (int i = 0; i < 150 && !saiu; i++) { Tiques(1, a, alvo); saiu = !arena.ContemEm(a.Pos); }
				Checa("[injecao] com `margemDaArenaTiles = -1` a regra nunca dispara e o corpo cruza a linha atras do alvo",
					  saiu, $"x {a.Pos.X:0}, linha {linhaLeste:0}, plano {a.Cerebro!.Atual} ({a.Cerebro.Porque})");
				_rotina.MargemDaArenaTiles = guardada;
				Descartar(a, alvo);
			}

			GD.Print("[arenaia] -- 6) a altura: os andares que se acertam, e o voador que acompanha o alvo (dono, 2026-09-07) --");
			{
				// A TABELA, como o dono a ditou (2026-09-07): rasante bate no chao e nao leva; do chao nao se
				// alcanca quem voa; dois voando se acertam com ate um andar de diferenca; dois ou mais, ninguem.
				Checa("regra: andar 1 acerta o chao; o chao NAO acerta o andar 1",
					  Jandirus.Core.World.Voo.PodeAcertar(1, 0) && !Jandirus.Core.World.Voo.PodeAcertar(0, 1));
				Checa("regra: dois voando com UM andar de diferenca se acertam (1<->2, 2<->3), nos dois sentidos",
					  Jandirus.Core.World.Voo.PodeAcertar(1, 2) && Jandirus.Core.World.Voo.PodeAcertar(2, 1)
					  && Jandirus.Core.World.Voo.PodeAcertar(2, 3) && Jandirus.Core.World.Voo.PodeAcertar(3, 2));
				Checa("regra: com dois andares de diferenca ninguem se acerta (1<->3, 2->chao, 3->chao)",
					  !Jandirus.Core.World.Voo.PodeAcertar(1, 3) && !Jandirus.Core.World.Voo.PodeAcertar(3, 1)
					  && !Jandirus.Core.World.Voo.PodeAcertar(2, 0) && !Jandirus.Core.World.Voo.PodeAcertar(3, 0) && !Jandirus.Core.World.Voo.PodeAcertar(0, 2));
				Checa("regra: tudo o que te acerta, voce ve (`PodeAcertar` dentro de `Enxerga`)",
					  Enumerable.Range(0, 4).All(a => Enumerable.Range(0, 4).All(b => !Jandirus.Core.World.Voo.PodeAcertar(a, b) || Jandirus.Core.World.Voo.Enxerga(b, a))));

				float meioDoAndar(int andar) => (andar - 0.5f) * (Jandirus.Core.World.Voo.AlturaMaxima / Jandirus.Core.World.Voo.Andares);
				ServerPlayer alvo = Forjar("altura: alvo", centro + Tiles(3, 0), comCerebro: false);
				ServerPlayer v = Forjar("altura: voador", centro - Tiles(3, 0), perfil: PerfilDeCombate.Completo, naArena: false);
				EnsinarAVoar(v);
				v.Cerebro!.Inteligencia = 0.9;   // esperto o bastante pra pairar rasante (a receita do andar 1)
				Armar(v);
				int maiorAndar = 0;
				void Correr(int tiques, Action? segurarOAlvo = null)
				{
					for (int i = 0; i < tiques; i++)
					{
						segurarOAlvo?.Invoke();
						v.Ficha.Ki = v.Ficha.MaxKi;   // o Ki nao entra nesta pergunta
						Tiques(1, v, alvo);
						maiorAndar = Math.Max(maiorAndar, Jandirus.Core.World.Voo.Andar(v.Altitude));
					}
				}
				Correr(300);
				Checa("contra um alvo NO CHAO o voador fica no andar 1 (rasante) e nunca sobe ao 2",
					  maiorAndar == 1 && Jandirus.Core.World.Voo.Andar(v.Altitude) <= 1, $"maior andar {maiorAndar}, agora {Jandirus.Core.World.Voo.Andar(v.Altitude)}");

				void AlvoNoAndar(int andar) { alvo.Voando = true; alvo.Altitude = meioDoAndar(andar); alvo.Ficha.Ki = alvo.Ficha.MaxKi; }
				maiorAndar = 0;
				Correr(300, () => AlvoNoAndar(2));
				Checa("o alvo sobe ao andar 2: o voador SEGUE (andar 2)", Jandirus.Core.World.Voo.Andar(v.Altitude) == 2, $"andar {Jandirus.Core.World.Voo.Andar(v.Altitude)}");
				Checa("...sem passar dele (nunca no 3)", maiorAndar <= 2, $"maior {maiorAndar}");
				Correr(300, () => AlvoNoAndar(3));
				Checa("o alvo sobe ao andar 3: o voador segue ate o 3", Jandirus.Core.World.Voo.Andar(v.Altitude) == 3, $"andar {Jandirus.Core.World.Voo.Andar(v.Altitude)}");
				Correr(300, () => AlvoNoAndar(1));
				Checa("o alvo desce ao andar 1: o voador DESCE junto (andar 1)", Jandirus.Core.World.Voo.Andar(v.Altitude) == 1, $"andar {Jandirus.Core.World.Voo.Andar(v.Altitude)}");
				alvo.Voando = false;
				alvo.Altitude = 0f;
				Correr(300);
				Checa("o alvo pousa: o voador volta ao andar 1 rasante (ou pousa), nunca fica alto sobre quem esta no chao",
					  Jandirus.Core.World.Voo.Andar(v.Altitude) <= 1, $"andar {Jandirus.Core.World.Voo.Andar(v.Altitude)}");
				Descartar(v, alvo);
			}
		}
		finally
		{
			foreach (ServerPlayer c in nascidos)
				if (_players.ContainsKey(c.Id)) RemoverNpc(c);
		}
		Placar();
	}

	/// <summary>
	/// O CANTO SUPERIOR ESQUERDO de um quadrado de `lado` celulas todas de chao, o mais perto do
	/// centro pedido. Varre em aneis de 4 em 4 celulas ate 160 celulas de distancia.
	/// </summary>
	private static (int X, int Y)? Clareira(ZoneCollision mapa, Vec2 centro, int lado)
	{
		int cx = (int)MathF.Floor(centro.X / ZoneCollision.TileSize) - lado / 2;
		int cy = (int)MathF.Floor(centro.Y / ZoneCollision.TileSize) - lado / 2;
		for (int r = 0; r <= 160; r += 4)
			for (int dx = -r; dx <= r; dx += 4)
				for (int dy = -r; dy <= r; dy += 4)
				{
					if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
					int x0 = cx + dx, y0 = cy + dy;
					bool livre = true;
					for (int y = y0; livre && y < y0 + lado; y++)
						for (int x = x0; x < x0 + lado; x++)
							if (!mapa.ServeDeChao(x, y)) { livre = false; break; }
					if (livre) return (x0, y0);
				}
		return null;
	}
}
