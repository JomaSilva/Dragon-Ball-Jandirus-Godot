using Godot;
using Jandirus.Core.Ai;
using Jandirus.Core.Combat;
using Jandirus.Core.Npc;
using Jandirus.Core.Social;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DO OUTRO MUNDO POVOADO E DOS VILOES DE SAGA NO INFERNO (`--infernoteste`, 2026-10-16). O dono: *"faça com q viloes de saga
/// derrotados as vezes apareçam no inferno no outro mundo como uma boss fight e quem vencer ganha zenni e honra , alem de colocar npcs
/// no outro mundo, npcs mortos com a aureula etc, no inferno tb"* -- e, perguntado: a honra e o KARMA, e a forca e a da MEDIA DE AGORA.
///
/// No PRIMEIRO LOGIN, hospedando, como a `--vilaoteste`: "gente no Outro Mundo", a media do servidor (que da a forca do vilao) e
/// "quem derrotou" pedem um corpo com `Peer` -- o do host, posto no Inferno, morto e de pe, pela duracao da bancada (e devolvido no
/// fim). Tudo pelas funcoes de producao: a alma nasce pelo `NascerNpc` do molde de verdade, a conta e a `Manutencao` do povoamento, o
/// relogio e o `TickDosViloesDoInferno`, e a derrota entra pelo funil unico (`AoPerderALuta`) ou pela segunda morte (`Morrer`).
///
/// Os defeitos injetados: a alma sem aureola, com o relogio da morte solto, fora da conta, indo a mesa do Enma; o vilao com o BP da
/// saga, pagando a cada queda, sem pagar o zeni; e (2026-10-23, familia 27) o vilao pela media do SERVIDOR, e nao a do Outro Mundo.
///
///     Godot --headless --path . --host --rede 8437 --infernoteste --raca Human --conta bancada_inferno --nome Defunto
/// </summary>
public partial class GameServer
{
	private bool _infernoDeTeste;

	private void RodarBancadaDoInferno(ServerPlayer pl)
	{
		GD.Print("\n[inferno] ================ O OUTRO MUNDO POVOADO E OS VILOES DO INFERNO (dono, 2026-10-16) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[inferno]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[inferno]   FALHA {nome}   [{detalhe}]"); }
		}

		ZoneKey alem = ZoneKey.Premade(Alem.ZonaDoOutroMundo), inferno = ZoneKey.Premade(Alem.ZonaDoInferno), ceu = ZoneKey.Premade(Alem.ZonaDoCeu);

		// O QUE O HOST ERA ANTES, pra voltar inteiro. Ele ainda NAO esta na lista da zona (as bancadas de 1o login rodam dentro do
		// `Entrar`, antes da linha que o poe no mundo); o `Entrar` o poe la depois, na zona dele.
		ZoneKey zonaDeAntes = pl.Zone;
		Vec2 posDeAntes = pl.Pos;
		bool mortoDeAntes = pl.Ficha.dead, viajouDeAntes = pl.MorteJaViajou;
		long relogioDeAntes = pl.RelogioDaMorte;
		double bpDeAntes = pl.Ficha.BP, zeniDeAntes = pl.Ficha.Zeni;
		int karmaDeAntes = pl.Karma;
		EstadoDasSagas sagasDeAntes = _sagas;
		bool ligadoDeAntes = ViloesDoInferno.Ligado;
		List<ServerPlayer> nascidos = [];

		void PorOHostNo(ZoneKey z, Vec2 onde)
		{
			ZoneList(pl.Zone.Hash).Remove(pl);
			pl.Zone = z;
			pl.Pos = onde;
			if (!ZoneList(z.Hash).Contains(pl)) ZoneList(z.Hash).Add(pl);
		}

		ServerPlayer? Nascer(string molde, ZoneKey z, ulong lugar)
		{
			ServerPlayer? n = NascerNpc(molde, z, PontoDeHabitante(z, lugar), 0x7130_0000UL + lugar);
			if (n != null) nascidos.Add(n);
			return n;
		}

		// (2026-10-23) OUTROS "JOGADORES" pra a media do Outro Mundo: corpos forjados com o `Peer` do host emprestado (e o que faz deles
		// jogador pro `EhJogador`), devolvido no fim antes de o corpo sair (`RemoverNpc` recusa corpo com dono).
		List<ServerPlayer> forjados = [];
		ServerPlayer ForjarGente(string nome, ZoneKey z, double bp, bool morto)
		{
			var f = new ServerPlayer
			{
				Id = _nextId++, Peer = pl.Peer, Name = nome, Race = "Human", Genero = "Male", Idade = 25,
				Zone = z, Pos = PontoDeNascimento(z), LastInputMs = NowMs(), Conta = "bancada_inferno_" + nome.ToLowerInvariant(), Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Name = nome, Race = "Human", BP = bp, Idade = 25, maxstamina = 100, stamina = 100 },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			f.Ficha.Class = "Normal";
			PorNoMundo(f);
			f.Ficha.dead = morto;
			forjados.Add(f);
			return f;
		}

		EstadoDasSagas EstadoCom(params (string Elo, string Molde, FaseDoChefe Fase, int Degrau, double RessurgeEm)[] chefes)
		{
			var e = new EstadoDasSagas();
			foreach (EloDaSaga elo in _cadeia) e.Elos.Add(new EstadoDoElo { Id = elo.Id });
			foreach (var c in chefes)
				e.Do(c.Elo)?.Chefes.Add(new EstadoDoChefe { Molde = c.Molde, Fase = c.Fase, Degrau = c.Degrau, RessurgeEm = c.RessurgeEm });
			return e;
		}

		ServerPlayer? VilaoDePe() => _vilaoDoInferno is { } v && _players.TryGetValue(v.Npc, out ServerPlayer? c) ? c : null;

		// A MESMA LINHA DO `TickCombate`: o morto com o prazo vencido sai da triagem -- e o dreno do fim do tique o tira do mundo.
		void TiqueDaMorte(ServerPlayer x)
		{
			if (x.Ficha.dead && NowMs() >= x.RelogioDaMorte) VenceuOPrazoDaMorte(x);
			TirarOsNpcsMortos();
		}

		try
		{
			// ---------------------------------------------------------------- 1. os numeros
			GD.Print("[inferno] -- 1) os numeros e as contas puras --");
			MoldeDeNpc? freeza = _moldes?.Get("freeza_namek");
			Checa("(montagem) o molde do Freeza de Namek existe, com a ficha de cinco degraus", freeza is { EhChefe: true, Estagios.Length: 5 });
			if (freeza == null) return;

			double[] bps = ViloesDoInferno.Bps(freeza, 1_000_000);
			double topo = 1_000_000 * ViloesDoInferno.FatorDaMedia;
			Checa($"**O BP E O DA MEDIA DE AGORA**: o degrau mais forte vale {ViloesDoInferno.FatorDaMedia:0}x a media (1 milhao -> {topo:N0})",
				  Math.Abs(bps[^1] - topo) < 1e-6, $"{bps[^1]:N0}");
			Checa("...e os outros degraus guardam a proporcao da ficha (a 1a forma: 530 mil de 8 milhoes, a forma final que o dono dobrou)",
				  Math.Abs(bps[0] - topo * 530_000 / 8_000_000) < 1e-3, $"{bps[0]:N0}");
			Checa("...SEM PISO no BP da saga: com a media baixa ele volta mais fraco do que era (a escolha do dono, 'pela media de agora')",
				  ViloesDoInferno.Bps(freeza, 1_000)[^1] < freeza.Estagios[^1].Bp, $"{ViloesDoInferno.Bps(freeza, 1_000)[^1]:N0}");
			Checa("...e com a media zero (servidor vazio) nenhum degrau fica abaixo de 1", ViloesDoInferno.Bps(freeza, 0).All(b => b >= 1));

			ChefeDaSaga? fichaDoFreeza = Array.Find(_cadeia, e => e.Id == "freeza_namek")?.Chefes.FirstOrDefault();
			ChefeDaSaga? fichaDo17 = Array.Find(_cadeia, e => e.Id == "cell")?.Chefes.FirstOrDefault(c => c.Molde == "androide_17");
			Checa("**O ZENI**: 250 mil por um chefe, 125 mil por um androide (pela `recompensa` que ele tem na saga)",
				  fichaDoFreeza != null && fichaDo17 != null && ViloesDoInferno.Premio(fichaDoFreeza) == 250_000 && ViloesDoInferno.Premio(fichaDo17) == 125_000);

			var candidatos = ViloesDoInferno.Candidatos(_cadeia, EstadoCom(
				("freeza_namek", "freeza_namek", FaseDoChefe.Derrotado, 3, 0),
				("cell", "androide_17", FaseDoChefe.Consumido, 0, 0),
				("cell", "cell", FaseDoChefe.Derrotado, 2, 9_999),
				("boo", "majin_boo", FaseDoChefe.NoMundo, 0, 0)));
			Checa("**QUEM VOLTA**: so o chefe DERROTADO -- nao o consumido (o androide que o Cell absorveu), nem o que esta ressurgindo, nem o que esta no mundo",
				  candidatos.Count == 1 && candidatos[0].Ficha.Molde == "freeza_namek" && candidatos[0].Estado.Degrau == 3,
				  string.Join(", ", candidatos.Select(c => c.Ficha.Molde)));

			// ---------------------------------------------------------------- 2. as almas
			GD.Print("[inferno] -- 2) as almas do Outro Mundo, do Ceu e do Inferno --");
			MoldeDeNpc? moldeAlma = _moldes?.Get("alma"), moldeCondenada = _moldes?.Get("alma_condenada");
			Checa("(montagem) os dois moldes de alma existem: habitantes (cidadao), 'alma': true, e as racas MORTAIS cravadas (nem Kai nem Demonio, que nascem vivos la)",
				  moldeAlma is { Tipo: TipoDeNpc.Cidadao, Alma: true } && moldeCondenada is { Tipo: TipoDeNpc.Cidadao, Alma: true }
				  && moldeAlma.Racas.Length > 0 && moldeCondenada.Racas.Length > 0
				  && !moldeAlma.Racas.Contains("Kai") && !moldeCondenada.Racas.Contains("Demon"));
			if (moldeAlma == null || moldeCondenada == null) return;

			List<string> linhas = [.. _moldes!.Plano.Where(l => Alem.EhOAlem(l.Planeta)).Select(l => $"{l.Planeta}:{l.Molde}:{l.Quantos}")];
			Checa("O POVOAMENTO TEM AS TRES LINHAS: 40 no Outro Mundo, 20 no Ceu e 30 no Inferno",
				  linhas.Contains("Afterlife:alma:40") && linhas.Contains("Heaven:alma:20") && linhas.Contains("Hell:alma_condenada:30"),
				  string.Join(", ", linhas));

			ServerPlayer? alma = Nascer("alma", alem, 1);
			Checa("(montagem) uma alma nasceu no Outro Mundo pelo `NascerNpc` de producao", alma != null);
			if (alma == null) return;
			Checa("**A ALMA NASCE MORTA, DE AUREOLA E DE PE** (morta, nao cadaver)",
				  alma.Ficha.dead && Alem.TemAureola(alma.Ficha.dead, alma.MorteJaViajou) && alma.MortoDePe && !EhCadaver(alma));
			Checa("...de uma raca mortal do molde (e nao a do berco do lugar)", moldeAlma.Racas.Contains(alma.Race), alma.Race);
			Checa("...habitante do Outro Mundo (o berco dela), NPC do mundo e pacifica",
				  alma.Berco.Natal == alem.Name && EhNpcDoMundo(alma) && alma.Papel is { Pacifico: true });

			TiqueDaMorte(alma);
			Checa("**O TIQUE DA MORTE NAO A TIRA DO MUNDO** (o relogio dela esta trancado)", _players.ContainsKey(alma.Id) && alma.RelogioDaMorte == long.MaxValue);
			ServerPlayer? solta;
			AlmaComRelogioDeTeste = true;
			try { solta = Nascer("alma", alem, 2); }
			finally { AlmaComRelogioDeTeste = false; }
			if (solta != null)
			{
				TiqueDaMorte(solta);
				Checa("(defeito injetado: o relogio solto) a alma some do mundo no tique da morte -- a regua de cima REPROVA", !_players.ContainsKey(solta.Id));
			}
			ServerPlayer? semAureola;
			AlmaSemAureolaDeTeste = true;
			try { semAureola = Nascer("alma", alem, 3); }
			finally { AlmaSemAureolaDeTeste = false; }
			Checa("(defeito injetado: sem aureola) a alma nasce como um morto qualquer -- a regua de cima REPROVA",
				  semAureola != null && !Alem.TemAureola(semAureola.Ficha.dead, semAureola.MorteJaViajou));

			// A CONTA E A MANUTENCAO
			int contadas = ContarHabitantes(alem.Name, "alma");
			Checa("**A ALMA CONTA COMO HABITANTE** (nasce morta, e e habitante assim mesmo)", contadas >= 1, $"{contadas}");
			int semContar;
			AlmaForaDaContaDeTeste = true;
			try { semContar = ContarHabitantes(alem.Name, "alma"); }
			finally { AlmaForaDaContaDeTeste = false; }
			Checa("(defeito injetado: o 'morto nao conta' de antes) nenhuma alma e contada", semContar == 0, $"{semContar}");

			int PedidosDeAlma() => _filaDoPovoamento.Count(p => p.Zona.Equals(alem) && p.Molde == "alma");
			void TirarOsPedidosDeAlma()
			{
				var resto = _filaDoPovoamento.Where(p => !(p.Zona.Equals(alem) && p.Molde == "alma")).ToList();
				_filaDoPovoamento.Clear();
				foreach (var p in resto) _filaDoPovoamento.Enqueue(p);
			}
			TirarOsPedidosDeAlma();
			for (ulong i = 10; ContarHabitantes(alem.Name, "alma") < 40 && i < 200; i++) Nascer("alma", alem, i);
			Manutencao();
			int pedidos = PedidosDeAlma(), deAlma = ContarHabitantes(alem.Name, "alma");
			Checa("**COM AS 40 DE PE, A MANUTENCAO NAO PEDE MAIS NENHUMA** -- o Outro Mundo nao enche alem do alvo",
				  deAlma >= 40 && pedidos == 0, $"{deAlma} almas, {pedidos} pedido(s)");
			AlmaForaDaContaDeTeste = true;
			try { TirarOsPedidosDeAlma(); Manutencao(); pedidos = PedidosDeAlma(); }
			finally { AlmaForaDaContaDeTeste = false; TirarOsPedidosDeAlma(); }
			Checa("(defeito injetado: a alma fora da conta) a manutencao pede outras -- a cada cinco minutos, pra sempre", pedidos > 0, $"{pedidos} pedido(s)");

			// A SEGUNDA MORTE
			ServerPlayer? condenada = Nascer("alma_condenada", inferno, 1);
			Checa("(montagem) uma alma condenada nasceu no Inferno", condenada != null && condenada.Zone.Equals(inferno) && moldeCondenada.Racas.Contains(condenada.Race));
			if (condenada != null)
			{
				Vec2 antes = condenada.Pos;
				bool morreu = condenada.Combate.Morrer();
				Checa("**A SEGUNDA MORTE DA ALMA E RECOMPOR-SE ALI MESMO**: o `Morrer` recusa, ela fica no Inferno, no mesmo lugar, curada e em coma curto",
					  !morreu && condenada.Zone.Equals(inferno) && (condenada.Pos - antes).Length < 1 && condenada.Ficha.KO && condenada.Combate.Corpo.Vida() >= 99,
					  $"zona {condenada.Zone.Name}, KO {condenada.Ficha.KO}, vida {condenada.Combate.Corpo.Vida():0}");
			}
			// (2026-10-16) A ALMA NOCAUTEADA SE REVISTA, E A ALMA ATACADA REVIDA -- o dono: *"npcs mortos n estao lutando mesmo q eu bata
			// neles"* e *"n ta dando pra revistar npcs mortos quando vc nocauteia eles, diz q n tem ninguem no alcance"*. Quem bate e o
			// host MORTO, de pe no Inferno, como todo jogador la.
			if (condenada != null)
			{
				pl.Ficha.dead = true;
				pl.MorteJaViajou = true;
				pl.RelogioDaMorte = long.MaxValue;
				PorOHostNo(inferno, condenada.Pos + new Vec2(ZoneCollision.TileSize, 0));
				Checa("**A ALMA NOCAUTEADA SE REVISTA**: caida na segunda morte, ela e a caida ao alcance do jogador morto ao lado",
					condenada.Ficha.KO && PodeSerSaqueado(condenada) && CaidoPerto(pl) == condenada, $"KO {condenada.Ficha.KO}, achou {CaidoPerto(pl)?.Name ?? "ninguem"}");

				ServerPlayer? brava = Nascer("alma_condenada", inferno, 3);
				if (brava != null)
				{
					brava.Pos = pl.Pos + new Vec2(2 * ZoneCollision.TileSize, 0);
					MarcarAgressao(brava, pl);
					Checa("**A ALMA ATACADA REVIDA**: a presa dela e o jogador MORTO que bateu (de pe no Inferno, e nao cadaver)",
						PresaDoNpc(brava) == pl, PresaDoNpc(brava)?.Name ?? "ninguem");

					MortoDePeComoCadaverDeTeste = true;
					try
					{
						Checa("(defeito injetado: o morto de pe como cadaver, o `dead` cru de antes) a alma nao revida e a caida nao se revista -- as duas reguas de cima REPROVAM",
							PresaDoNpc(brava) == null && !PodeSerSaqueado(condenada));
					}
					finally { MortoDePeComoCadaverDeTeste = false; }
				}

				// o host volta ao que era -- a familia do vilao o poe no Inferno de novo, do jeito dela
				pl.Ficha.dead = mortoDeAntes;
				pl.MorteJaViajou = viajouDeAntes;
				pl.RelogioDaMorte = relogioDeAntes;
				PorOHostNo(zonaDeAntes, posDeAntes);
			}

			ServerPlayer? perdida;
			AlmaVaiAoEnmaDeTeste = true;
			try { perdida = Nascer("alma_condenada", inferno, 2); }
			finally { AlmaVaiAoEnmaDeTeste = false; }
			if (perdida != null)
			{
				perdida.Combate.Morrer();
				Checa("(defeito injetado: a segunda morte do jogador) a alma do Inferno vai parar na mesa do Enma, no Outro Mundo -- a regua de cima REPROVA",
					  perdida.Zone.Equals(alem), perdida.Zone.Name);
			}

			// A VIDA DIARIA
			ServerPlayer? a1 = Nascer("alma", alem, 300);
			ServerPlayer? a2 = a1 == null ? null : NascerNpc("alma", alem, a1.Pos + new Vec2(ZoneCollision.TileSize * 2, 0), 0x7130_0000UL + 301);
			if (a2 != null) nascidos.Add(a2);
			if (a1 != null && a2 != null)
			{
				Rotina r1 = a1.Rotina ??= new Rotina(_rotina, 1, a1.Id, conversas: ConversaDaAlma(a1));
				a2.Rotina ??= new Rotina(_rotina, 2, a2.Id, conversas: ConversaDaAlma(a2));
				Arredores ar = LerArredores(a1, r1);
				Checa("**A ALMA NAO ESTA CAIDA** na vida diaria (o morto de pe nao e cadaver) -- lendo o `dead` cru ela ficaria parada pra sempre", !ar.Caido);
				ServerPlayer? vizinho = HabitanteLivrePerto(a1);
				Checa("**...E ACHA OUTRA ALMA PRA CONVERSAR** (o morto de pe e um habitante livre)",
					  vizinho != null && vizinho.Ficha.dead && vizinho.MortoDePe && vizinho.Papel is { Molde.Alma: true }, vizinho?.Name ?? "ninguem");
			}
			Checa("A CONVERSA E A DE MORTO: o Outro Mundo fala do alem, o Inferno fala do Inferno, e o habitante comum fala como sempre",
				  a1 != null && condenada != null
				  && ReferenceEquals(ConversaDaAlma(a1), _rotina.Frases.ConversaDoAlem) && _rotina.Frases.ConversaDoAlem.Length > 0
				  && ReferenceEquals(ConversaDaAlma(condenada), _rotina.Frases.ConversaDoInferno) && _rotina.Frases.ConversaDoInferno.Length > 0
				  && ConversaDaAlma(pl) == null,
				  $"alem {_rotina.Frases.ConversaDoAlem.Length}, inferno {_rotina.Frases.ConversaDoInferno.Length}");

			var rotinaDeAlma = new Rotina(_rotina, 7, 999_001, conversas: ["__a fala da alma__"]);
			rotinaDeAlma.Chamado(999_002, 30);
			var emConversa = new Arredores
			{
				ParceiroPresente = true, AfazerDoParceiro = Afazer.Conversando, ParceiroDoParceiro = 999_001, DoParceiro = new Vec2(32, 0),
			};
			string? falou = null;
			for (int i = 0; i < 40 && falou == null; i++) falou = rotinaDeAlma.Tique(0.5, emConversa).Falar;
			Checa("...e a ROTINA fala da lista propria (a mesma maquina de conversa de todo habitante)", falou == "__a fala da alma__", falou ?? "calada");

			Checa("O OUTRO MUNDO NAO E PLANETA DE CONQUISTA: ter povo la nao abre bandeira nem invasao",
				  CorpoDaZona(alem) == null && CorpoDaZona(inferno) == null && CorpoDaZona(ceu) == null);

			// ---------------------------------------------------------------- 3. o vilao
			GD.Print("[inferno] -- 3) o vilao de saga que volta no Inferno --");
			// O HOST NO INFERNO, MORTO E DE PE, com 1 milhao de BP base -- e a media do servidor (ele e o unico jogador).
			pl.Ficha.dead = true;
			pl.MorteJaViajou = true;
			pl.RelogioDaMorte = long.MaxValue;
			pl.Ficha.BP = 1_000_000;
			PorOHostNo(inferno, PontoDeNascimento(inferno));
			double media = MediaDoServidor();
			Checa("(montagem) o host esta no Inferno, morto e de pe, e a media do servidor e o BP dele",
				  pl.MortoDePe && !EhCadaver(pl) && GenteNoOutroMundo() && Math.Abs(media - 1_000_000) < 1, $"media {media:N0}");

			_sagas = EstadoCom();   // nenhuma saga vencida
			_vilaoDoInferno = null;
			_proximoVilaoDoInferno = 0;
			ViloesDoInferno.Ligado = true;
			SorteioDoInfernoDeTeste = 0.5;
			long agora = NowMs();
			TickDosViloesDoInferno();
			Checa("O RELOGIO COMECA quando alguem esta no Outro Mundo -- e ninguem e recebido por um vilao na chegada",
				  _vilaoDoInferno == null && Math.Abs(_proximoVilaoDoInferno - agora - (long)(ViloesDoInferno.ProximoIntervaloS(0.5) * 1000)) < 2000,
				  $"em {(_proximoVilaoDoInferno - agora) / 1000.0:0} s");
			_proximoVilaoDoInferno = NowMs() - 1;
			TickDosViloesDoInferno();
			Checa("SEM VILAO DERROTADO NA CADEIA, a hora vence e ninguem volta (nenhuma saga foi vencida ainda)", _vilaoDoInferno == null);

			PorOHostNo(zonaDeAntes, posDeAntes);   // morto fora do Outro Mundo e cadaver: ninguem pode lutar
			TickDosViloesDoInferno();
			Checa("SEM NINGUEM NO OUTRO MUNDO O RELOGIO PARA (e o primeiro que chegar o recomeca)", _proximoVilaoDoInferno == 0);
			PorOHostNo(inferno, PontoDeNascimento(inferno));

			// (2026-10-23) A MEDIA E A DO OUTRO MUNDO (dono: *"Pela media do Outro Mundo"*): uma alma de pe no CEU, a 3 milhoes (as tres
			// zonas contam), e um vivo na TERRA, a 10 mil (fora do Alem: nao conta) -- a media do Outro Mundo (o host e a alma: 2 milhoes)
			// fica longe da do servidor (os tres: ~1,34 milhao). Nascem DEPOIS do "o relogio para" de cima: a alma no Ceu e gente no Outro
			// Mundo, e com ela la o relogio nunca pararia.
			const double mediaDoAlem = 2_000_000;
			ServerPlayer almaDoCeu = ForjarGente("AlmaDoCeu", ceu, 3_000_000, morto: true);
			ServerPlayer vivoDaTerra = ForjarGente("VivoDaTerra", ZoneKey.Premade("Earth"), 10_000, morto: false);
			Checa("(montagem) a alma do Ceu (morta e de pe) e o vivo da Terra sao jogadores, e a media do servidor -- que conta o vivo -- fica longe da do Outro Mundo",
				  EhJogador(almaDoCeu) && almaDoCeu.MortoDePe && EhJogador(vivoDaTerra) && !vivoDaTerra.Ficha.dead
				  && Math.Abs(MediaDoServidor() - mediaDoAlem) > 100_000, $"servidor {MediaDoServidor():N0}");

			_sagas = EstadoCom(("freeza_namek", "freeza_namek", FaseDoChefe.Derrotado, 3, 0));
			TickDosViloesDoInferno();   // o relogio recomeca
			_proximoVilaoDoInferno = NowMs() - 1;
			EscutaDeChegadas = [];
			List<string> falas = Ouvir(TickDosViloesDoInferno);
			ServerPlayer? vilao = VilaoDePe();
			Checa("**NA HORA, O VILAO DERROTADO VOLTA NO INFERNO**", vilao != null && vilao.Zone.Equals(inferno), _vilaoDoInferno?.Molde ?? "ninguem");
			if (vilao == null) return;
			nascidos.Add(vilao);
			Checa("...e o Freeza de Namek, no degrau em que caiu (o 4o): um chefe que caca o Inferno inteiro e nao fica sem Ki",
				  vilao.Papel is { DaSaga: true, Pacifico: false, Estagio: 3 } pv && pv.Molde.Id == "freeza_namek" && vilao.Ficha.KiDeChefe,
				  $"degrau {vilao.Papel?.Estagio + 1}");
			Checa("**...MORTO, DE AUREOLA E DE PE**, como todo mundo la",
				  vilao.Ficha.dead && Alem.TemAureola(vilao.Ficha.dead, vilao.MorteJaViajou) && vilao.MortoDePe && vilao.RelogioDaMorte == long.MaxValue);
			double esperado = ViloesDoInferno.Bps(freeza, mediaDoAlem)[3];
			Checa($"**27i. COM O BP DA MEDIA DE AGORA NO OUTRO MUNDO** (dono, 2026-10-23: *\"Pela media do Outro Mundo\"*) -- o 4o degrau a "
				  + $"{esperado:N0} (25x a media do host e da alma do Ceu, 2 milhoes), e nao os {freeza.Estagios[3].Bp:N0} da saga nem os "
				  + $"{ViloesDoInferno.Bps(freeza, MediaDoServidor())[3]:N0} da media do servidor",
				  Math.Abs(vilao.Ficha.BP - esperado) < 1, $"BP {vilao.Ficha.BP:N0}");
			Checa("...carregando o premio: 250 mil zeni na bolsa", Math.Abs(vilao.Ficha.Zeni - 250_000) < 1, $"{vilao.Ficha.Zeni:N0}");
			float dist = (vilao.Pos - pl.Pos).Length / ZoneCollision.TileSize;
			// A FAIXA DO VILAO AVULSO, com dois tiles de folga (o `PontoLivrePerto` empurra pro tile livre vizinho): a primeira versao o
			// punha em qualquer vaga a dez tiles ou mais -- 274 e 406 passos, num Inferno com ilhas.
			Checa("...na FAIXA JOGAVEL de quem esta la: nem em cima de ninguem, nem do outro lado do Inferno",
				  dist >= VilaoAvulso.DistanciaMinTiles - 2 && dist <= VilaoAvulso.DistanciaMaxTiles + 2, $"{dist:0.#} tiles do host");
			Checa("**O OUTRO MUNDO E AVISADO**: o nome, pra que lado, o premio e o prazo",
				  Disse(falas, "ESCAPOU DAS PROFUNDEZAS") && Disse(falas, "passos a") && Disse(falas, "250.000") && Disse(falas, "karma"), Ultimos(falas));
			Checa("...e quem esta no Outro Mundo ve a CENA DE CHEGADA de chefe",
				  EscutaDeChegadas is { Count: > 0 } chegadas && chegadas[^1].Para >= 1 && chegadas[^1].Planeta == Alem.ZonaDoInferno,
				  EscutaDeChegadas is { Count: > 0 } c2 ? $"{c2[^1].Nome} pra {c2[^1].Para}" : "nenhuma");
			Checa("**ELE CACA QUEM FOI LA LUTAR**: o morto de pe e presa (so o cadaver fica de fora)", PresaDoNpc(vilao)?.Id == pl.Id,
				  PresaDoNpc(vilao)?.Name ?? "ninguem");

			// A DERROTA POR NOCAUTE
			pl.Karma = 0;
			double z0 = pl.Ficha.Zeni;
			falas = Ouvir(() => AoPerderALuta(vilao, pl, morreu: false));
			Checa($"**NOCAUTEAR O VILAO PAGA A HONRA**: o karma de derrubar um chefe de saga (+{Karma.PorDerrotarChefe})",
				  pl.Karma == Karma.PorDerrotarChefe, $"karma {pl.Karma}");
			Checa("**...E O ZENI**: os 250 mil da bolsa vao pra carteira de quem o derrotou, e ele le quanto",
				  Math.Abs(pl.Ficha.Zeni - z0 - 250_000) < 1 && vilao.Ficha.Zeni < 1 && Disse(falas, "250.000 zeni"),
				  $"{z0:N0} -> {pl.Ficha.Zeni:N0}; {Ultimos(falas)}");
			Checa("...e o Outro Mundo fica sabendo quem foi", Disse(falas, "derrotou") && Disse(falas, vilao.Name), Ultimos(falas));
			AoPerderALuta(vilao, pl, morreu: true);
			Checa("...UMA VEZ: derrubar de novo o mesmo vilao nao paga outra vez (nem o karma, nem o zeni)",
				  pl.Karma == Karma.PorDerrotarChefe && Math.Abs(pl.Ficha.Zeni - z0 - 250_000) < 1, $"karma {pl.Karma}, zeni {pl.Ficha.Zeni:N0}");
			long antesDoFim = NowMs();
			TickDosViloesDoInferno();
			Checa("o derrotado SAI no tique seguinte, e o relogio conta do fim do evento",
				  !_players.ContainsKey(vilao.Id) && _vilaoDoInferno == null
				  && _proximoVilaoDoInferno >= antesDoFim + (long)(ViloesDoInferno.IntervaloMinS * 1000) - 50);

			// A DERROTA PELA SEGUNDA MORTE
			_proximoVilaoDoInferno = NowMs() - 1;
			TickDosViloesDoInferno();
			ServerPlayer? v2 = VilaoDePe();
			Checa("(montagem) outro vilao volta", v2 != null);
			if (v2 == null) return;
			nascidos.Add(v2);
			pl.Karma = 0;
			z0 = pl.Ficha.Zeni;
			MarcarAgressao(v2, pl);
			bool v2Morreu = v2.Combate.Morrer();
			Checa("**A SEGUNDA MORTE DELE E A DERROTA** (o golpe letal num morto): o premio vai pro ultimo agressor, e ele NAO vai parar na mesa do Enma",
				  !v2Morreu && pl.Karma == Karma.PorDerrotarChefe && Math.Abs(pl.Ficha.Zeni - z0 - 250_000) < 1 && v2.Zone.Equals(inferno) && v2.Ficha.KO,
				  $"karma {pl.Karma}, zeni +{pl.Ficha.Zeni - z0:N0}, zona {v2.Zone.Name}");
			TickDosViloesDoInferno();
			Checa("...e sai no tique seguinte", !_players.ContainsKey(v2.Id) && _vilaoDoInferno == null);

			// O PRAZO
			_proximoVilaoDoInferno = NowMs() - 1;
			TickDosViloesDoInferno();
			ServerPlayer? v3 = VilaoDePe();
			Checa("(montagem) um terceiro vilao volta", v3 != null);
			if (v3 == null) return;
			nascidos.Add(v3);
			pl.Karma = 0;
			_vilaoDoInferno!.Ate = NowMs() - 1;
			falas = Ouvir(TickDosViloesDoInferno);
			Checa("**NINGUEM O DERROTOU E O PRAZO VENCEU: ELE AFUNDA DE VOLTA**, o Outro Mundo e avisado, e ninguem ganha nada",
				  !_players.ContainsKey(v3.Id) && _vilaoDoInferno == null && pl.Karma == 0 && Disse(falas, "afundou de volta"), Ultimos(falas));

			// A CHAVE
			ViloesDoInferno.Ligado = false;
			_proximoVilaoDoInferno = NowMs() - 1;
			TickDosViloesDoInferno();
			Checa("com a chave geral DESLIGADA (`ViloesDoInferno.Ligado`) a hora vence e ninguem volta", _vilaoDoInferno == null);
			ViloesDoInferno.Ligado = true;

			// OS DEFEITOS
			InfernoComOBpDaSagaDeTeste = true;
			try { _proximoVilaoDoInferno = NowMs() - 1; TickDosViloesDoInferno(); }
			finally { InfernoComOBpDaSagaDeTeste = false; }
			ServerPlayer? v4 = VilaoDePe();
			if (v4 != null)
			{
				nascidos.Add(v4);
				Checa($"(defeito injetado: o BP da saga) o Freeza volta com os {freeza.Estagios[3].Bp:N0} de quando caiu -- a regua da media REPROVA",
					  Math.Abs(v4.Ficha.BP - freeza.Estagios[3].Bp) < 1, $"BP {v4.Ficha.BP:N0}");
				pl.Karma = 0;
				InfernoPagaDeNovoDeTeste = true;
				try { AoPerderALuta(v4, pl, morreu: false); AoPerderALuta(v4, pl, morreu: true); }
				finally { InfernoPagaDeNovoDeTeste = false; }
				Checa("(defeito injetado: o premio a cada queda) nocautear e derrubar de novo o MESMO vilao paga duas vezes -- a regua do 'uma vez' REPROVA",
					  pl.Karma == 2 * Karma.PorDerrotarChefe, $"karma {pl.Karma}");
				TickDosViloesDoInferno();
			}
			// (2026-10-23) a media do SERVIDOR, o jogo de antes: o vivo da Terra entra na conta
			InfernoPelaMediaDoServidorDeTeste = true;
			try { _proximoVilaoDoInferno = NowMs() - 1; TickDosViloesDoInferno(); }
			finally { InfernoPelaMediaDoServidorDeTeste = false; }
			ServerPlayer? peloServidor = VilaoDePe();
			if (peloServidor != null)
			{
				nascidos.Add(peloServidor);
				double doServidor = ViloesDoInferno.Bps(freeza, MediaDoServidor())[3];
				Checa($"(defeito injetado: a media do servidor inteiro) o Freeza volta a {doServidor:N0} -- o vivo da Terra entra na conta -- e a regua 27i REPROVA",
					  Math.Abs(peloServidor.Ficha.BP - doServidor) < 1 && Math.Abs(peloServidor.Ficha.BP - esperado) > 1, $"BP {peloServidor.Ficha.BP:N0}");
				_vilaoDoInferno!.Ate = NowMs() - 1;
				TickDosViloesDoInferno();
			}
			_proximoVilaoDoInferno = NowMs() - 1;
			TickDosViloesDoInferno();
			ServerPlayer? v5 = VilaoDePe();
			if (v5 != null)
			{
				nascidos.Add(v5);
				z0 = pl.Ficha.Zeni;
				InfernoNaoPagaZeniDeTeste = true;
				try { AoPerderALuta(v5, pl, morreu: false); }
				finally { InfernoNaoPagaZeniDeTeste = false; }
				Checa("(defeito injetado: sem zeni) a carteira de quem o derrotou nao muda e a bolsa fica com ele -- a regua do zeni REPROVA",
					  Math.Abs(pl.Ficha.Zeni - z0) < 1 && v5.Ficha.Zeni >= 1, $"{z0:N0} -> {pl.Ficha.Zeni:N0}");
				TickDosViloesDoInferno();
			}

			// ---------------------------------------------------------------- 4. o Majin Boo (2026-10-23)
			GD.Print("[inferno] -- 4) o Majin Boo do Inferno so cai pela morte por ki (dono, 2026-10-23) --");
			// O dono: *"boss majin boo no inferno ta dando pra matar no soco"* -> *"So a morte por ki"*. Os GOLPES DE VERDADE: o dano pronto
			// do soco e do tiro num nucleo (`MeleeResolver.AplicarDanoPronto` -- o `porKi` separa um do outro) e o desfecho pelo
			// `ResolverDesfecho`, o mesmo par do projetil (`GameServer.Projeteis.cs`) e da familia M da `--v030teste`. UM BOO NOVO POR GOLPE:
			// cada regua parte de um evento intacto (sem ele, um soco que derrotasse o primeiro derrubaria as reguas seguintes junto).
			var sorteDoBoo = new Random(23);
			GolpeResultado GolpeDeVerdade(ServerPlayer v, bool letal, bool porKi)
			{
				foreach (BodyPart p in v.Combate.Corpo.Partes)
					if (p.Papel != Vitalidade.Nucleo) p.Chance = 0;
				MarcarAgressao(v, pl);
				GolpeResultado r = MeleeResolver.AplicarDanoPronto(v.Combate, 1e9, letal, sorteDoBoo, "cabeca", porKi);
				ResolverDesfecho(pl, v, r);
				return r;
			}
			ServerPlayer? NovoBoo()
			{
				// o vilao de antes acaba (o prazo vence, ou o tique tira o derrotado), e o proximo nasce na hora: o Boo, o unico derrotado da cadeia
				if (_vilaoDoInferno != null) { _vilaoDoInferno.Ate = NowMs() - 1; TickDosViloesDoInferno(); }
				_proximoVilaoDoInferno = NowMs() - 1;
				TickDosViloesDoInferno();
				ServerPlayer? b = VilaoDePe();
				if (b != null) nascidos.Add(b);
				pl.Karma = 0;
				return b;
			}
			_sagas = EstadoCom(("boo", "majin_boo", FaseDoChefe.Derrotado, 0, 0));

			ServerPlayer? boo = NovoBoo();
			Checa("(montagem) o Majin Boo volta no Inferno: corpo de Majin, morto e de pe, com os 250 mil na bolsa",
				  boo != null && _vilaoDoInferno?.Molde == "majin_boo" && CorpoDeMajin(boo) && boo.MortoDePe && Math.Abs(boo.Ficha.Zeni - 250_000) < 1,
				  $"{_vilaoDoInferno?.Molde ?? "ninguem"}; majin {boo != null && CorpoDeMajin(boo)}; zeni {boo?.Ficha.Zeni:N0}");
			if (boo == null) return;

			// (a) o SOCO LETAL: o Boo se desfaz (a regra do Majin de 2026-10-15) -- e o evento continua
			double zb = pl.Ficha.Zeni;
			GolpeResultado socoLetal = default;
			List<string> doSoco = Ouvir(() => socoLetal = GolpeDeVerdade(boo, letal: true, porKi: false));
			TickDosViloesDoInferno();
			Checa("**4a. O SOCO LETAL NAO DERROTA O BOO** (o dono: *\"So a morte por ki\"*): ele se DESFAZ em pedacos (e se refaz em 30 s), o evento continua, ninguem leva o premio -- e quem bateu le por que",
				  socoLetal.Desfez && boo.Combate.Desfeito && _players.ContainsKey(boo.Id) && _vilaoDoInferno is { Derrotado: false }
				  && pl.Karma == 0 && Math.Abs(pl.Ficha.Zeni - zb) < 1 && boo.Ficha.Zeni >= 1 && Disse(doSoco, "golpe de ki letal"),
				  $"desfez {socoLetal.Desfez}, desfeito {boo.Combate.Desfeito}, no mundo {_players.ContainsKey(boo.Id)}, derrotado {_vilaoDoInferno?.Derrotado}, "
				  + $"karma {pl.Karma}, zeni +{pl.Ficha.Zeni - zb:N0} | {Ultimos(doSoco)}");

			// ...e o EM PEDACOS QUANDO O PRAZO VENCE: afunda como qualquer vilao, sem premio
			if (_vilaoDoInferno != null) _vilaoDoInferno.Ate = NowMs() - 1;
			List<string> doPrazo = Ouvir(TickDosViloesDoInferno);
			Checa("**4b. ...E SE O PRAZO VENCE COM ELE EM PEDACOS**, ele afunda de volta: o corpo sai do mundo, o Outro Mundo e avisado e ninguem ganha nada",
				  !_players.ContainsKey(boo.Id) && _vilaoDoInferno == null && pl.Karma == 0 && Math.Abs(pl.Ficha.Zeni - zb) < 1 && Disse(doPrazo, "afundou de volta"),
				  $"no mundo {_players.ContainsKey(boo.Id)}, karma {pl.Karma}, zeni +{pl.Ficha.Zeni - zb:N0} | {Ultimos(doPrazo)}");

			// (c) o SOCO NAO-LETAL e (d) o KI NAO-LETAL: os dois so o derrubam
			foreach ((string codigo, bool porKi, string oQue) in new[] { ("4c.", false, "O SOCO NAO-LETAL"), ("4d.", true, "O KI NAO-LETAL") })
			{
				boo = NovoBoo();
				if (boo == null) { Checa($"(montagem) um Boo novo pra {oQue}", false); return; }
				zb = pl.Ficha.Zeni;
				GolpeResultado leve = GolpeDeVerdade(boo, letal: false, porKi: porKi);
				TickDosViloesDoInferno();
				Checa($"**{codigo} ...NEM {oQue}**: ele so cai nocauteado -- o evento continua e o premio fica na bolsa",
					  leve.Nocauteou && boo.Ficha.KO && _players.ContainsKey(boo.Id) && _vilaoDoInferno is { Derrotado: false }
					  && pl.Karma == 0 && Math.Abs(pl.Ficha.Zeni - zb) < 1 && boo.Ficha.Zeni >= 1,
					  $"nocauteou {leve.Nocauteou}, KO {boo.Ficha.KO}, no mundo {_players.ContainsKey(boo.Id)}, derrotado {_vilaoDoInferno?.Derrotado}, karma {pl.Karma}");
			}

			// (e) o KI LETAL: a segunda morte -- a unica derrota
			boo = NovoBoo();
			if (boo == null) { Checa("(montagem) um Boo novo pro ki letal", false); return; }
			zb = pl.Ficha.Zeni;
			GolpeResultado raioLetal = default;
			List<string> doRaio = Ouvir(() => raioLetal = GolpeDeVerdade(boo, letal: true, porKi: true));
			Checa("**4e. SO O KI LETAL O DERROTA**: a segunda morte dele -- o premio (o karma e os 250 mil da bolsa) vai pra quem atirou, e o Outro Mundo fica sabendo",
				  !raioLetal.Morreu && _vilaoDoInferno is { Derrotado: true } && pl.Karma == Karma.PorDerrotarChefe && Math.Abs(pl.Ficha.Zeni - zb - 250_000) < 1
				  && boo.Ficha.Zeni < 1 && Disse(doRaio, "derrotou"),
				  $"derrotado {_vilaoDoInferno?.Derrotado}, karma {pl.Karma}, zeni +{pl.Ficha.Zeni - zb:N0} | {Ultimos(doRaio)}");
			GolpeDeVerdade(boo, letal: true, porKi: true);
			Checa("**4f. ...UMA VEZ**: o segundo tiro letal, antes de o tique o tirar, nao paga de novo",
				  pl.Karma == Karma.PorDerrotarChefe && Math.Abs(pl.Ficha.Zeni - zb - 250_000) < 1, $"karma {pl.Karma}, zeni +{pl.Ficha.Zeni - zb:N0}");
			TickDosViloesDoInferno();
			Checa("...e ele sai no tique seguinte", !_players.ContainsKey(boo.Id) && _vilaoDoInferno == null);

			// O DEFEITO: o soco que o desfaz volta a contar como derrota
			boo = NovoBoo();
			if (boo != null)
			{
				zb = pl.Ficha.Zeni;
				MajinDoInfernoCaiNoSocoDeTeste = true;
				try { GolpeDeVerdade(boo, letal: true, porKi: false); }
				finally { MajinDoInfernoCaiNoSocoDeTeste = false; }
				Checa("(defeito injetado: o jogo de antes de 2026-10-23) o soco letal que o desfaz DERROTA o Boo e paga -- a regua 4a REPROVA",
					  _vilaoDoInferno is { Derrotado: true } && pl.Karma == Karma.PorDerrotarChefe && Math.Abs(pl.Ficha.Zeni - zb - 250_000) < 1,
					  $"derrotado {_vilaoDoInferno?.Derrotado}, karma {pl.Karma}, zeni +{pl.Ficha.Zeni - zb:N0}");
				TickDosViloesDoInferno();
			}

			// ---------------------------------------------------------------- 5. o Supremo Kaioh do Ceu (2026-10-23)
			GD.Print("[inferno] -- 5) o Supremo Kaioh do Ceu morre como todo mundo (dono, 2026-10-23: 'Toda a raca Kai morre normal') --");
			// O NPC DE PRODUCAO, pelo molde de verdade (`supremo_kaioh`, raca Kai, cidadao vivo no Ceu): a regra e a mesma da familia M da
			// `--v030teste` (o corpo forjado); aqui e o habitante que o dono via "regenerando" -- ele morre, sai do mundo pelo dreno de todo
			// NPC morto, e o povoamento do Ceu o repoe (o alvo e cinco menos os Kaioshins jogadores; o `cadaDonoTiraUm` nao mudou).
			ServerPlayer? kaioh = Nascer("supremo_kaioh", ceu, 900);
			Checa("(montagem) um Supremo Kaioh nasceu no Ceu pelo `NascerNpc` de producao, vivo e da raca Kai",
				  kaioh != null && kaioh.Zone.Equals(ceu) && kaioh.Race == "Kai" && !kaioh.Ficha.dead, kaioh?.Race ?? "ninguem");
			if (kaioh != null)
			{
				int noCeuAntes = ContarHabitantes(ceu.Name, "supremo_kaioh");
				GolpeResultado socoNoKaioh = GolpeDeVerdade(kaioh, letal: true, porKi: false);
				Checa("**5a. O SUPREMO KAIOH MORRE NO SOCO LETAL**, como todo mundo -- e nao cai mais em coma pra se regenerar e levantar",
					  socoNoKaioh.Morreu && kaioh.Ficha.dead && !socoNoKaioh.Nocauteou,
					  $"morreu {socoNoKaioh.Morreu} ({socoNoKaioh.Membro}), nocauteou {socoNoKaioh.Nocauteou}, morto {kaioh.Ficha.dead}, KO {kaioh.Ficha.KO}");
				kaioh.RelogioDaMorte = NowMs() - 1;   // os 2 s do corpo no chao, adiantados
				TiqueDaMorte(kaioh);
				bool cadaver = ZoneList(ceu.Hash).Any(c => c.ECadaver && c.NomeDeQuemMorreu == kaioh.Name);
				Checa("**5b. ...SAI DO MUNDO E DEIXA O CADAVER**, como todo NPC morto -- e deixa de contar no povoamento do Ceu",
					  !_players.ContainsKey(kaioh.Id) && cadaver && ContarHabitantes(ceu.Name, "supremo_kaioh") == noCeuAntes - 1,
					  $"no mundo {_players.ContainsKey(kaioh.Id)}, cadaver {cadaver}, no Ceu {noCeuAntes} -> {ContarHabitantes(ceu.Name, "supremo_kaioh")}");
				LinhaDePovoamento? linhaDoKaioh = _moldes!.Plano.FirstOrDefault(l => l.Planeta == ceu.Name && l.Molde == "supremo_kaioh");
				int alvoDoKaioh = linhaDoKaioh == null ? -1 : Povoamento.AlvoDaLinha(linhaDoKaioh, QuantosDonos);
				Manutencao();
				int pedidosDoKaioh = _filaDoPovoamento.Count(p => p.Zona.Equals(ceu) && p.Molde == "supremo_kaioh");
				int vivosNoCeu = ContarHabitantes(ceu.Name, "supremo_kaioh");
				Checa("5c. ...e a MANUTENCAO do povoamento repoe o Ceu ate o alvo (cinco menos os Kaioshins jogadores)",
					  alvoDoKaioh > 0 && vivosNoCeu + pedidosDoKaioh >= alvoDoKaioh, $"alvo {alvoDoKaioh}, vivos {vivosNoCeu}, pedidos {pedidosDoKaioh}");
			}

			// O VERB DO ADMIN
			_sagas = EstadoCom();
			_vilaoDoInferno = null;
			falas = Ouvir(() => AdminVilaoDoInferno(pl));
			ServerPlayer? doAdmin = VilaoDePe();
			if (doAdmin != null) nascidos.Add(doAdmin);
			Checa("O VERB DO ADMIN chama um vilao AGORA -- sem nenhum derrotado ainda, um da cadeia, pro teste (e diz isso)",
				  doAdmin != null && doAdmin.Zone.Equals(inferno) && Disse(falas, "pro teste"), Ultimos(falas));
			falas = Ouvir(() => AdminVilaoDoInferno(pl));
			Checa("...e recusa o segundo: um vilao por vez", Disse(falas, "um vilão por vez"), Ultimos(falas));
		}
		catch (Exception e)
		{
			Checa("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			AlmaSemAureolaDeTeste = AlmaComRelogioDeTeste = AlmaVaiAoEnmaDeTeste = AlmaForaDaContaDeTeste = false;
			InfernoComOBpDaSagaDeTeste = InfernoPagaDeNovoDeTeste = InfernoNaoPagaZeniDeTeste = MajinDoInfernoCaiNoSocoDeTeste = false;
			SorteioDoInfernoDeTeste = null;
			EscutaDeChegadas = null;
			if (VilaoDePe() is { } restou) RemoverNpc(restou);
			_vilaoDoInferno = null;
			_proximoVilaoDoInferno = 0;
			foreach (ServerPlayer n in nascidos)
				if (_players.ContainsKey(n.Id)) RemoverNpc(n);
			// (2026-10-23) a familia 27: o defeito da media e os jogadores forjados
			InfernoPelaMediaDoServidorDeTeste = false;
			foreach (ServerPlayer f in forjados)
			{
				// O `Peer` EMPRESTADO VOLTA ANTES DE O CORPO SAIR: `RemoverNpc` recusa corpo com dono.
				f.Peer = null;
				RemoverNpc(f);
			}
			_sagas = sagasDeAntes;
			ViloesDoInferno.Ligado = ligadoDeAntes;

			ZoneList(pl.Zone.Hash).Remove(pl);   // o `Entrar` o poe de volta, na zona dele
			pl.Zone = zonaDeAntes;
			pl.Pos = posDeAntes;
			pl.Ficha.dead = mortoDeAntes;
			pl.MorteJaViajou = viajouDeAntes;
			pl.RelogioDaMorte = relogioDeAntes;
			pl.Ficha.BP = bpDeAntes;
			pl.Ficha.Zeni = zeniDeAntes;
			pl.Karma = karmaDeAntes;

			// O PLACAR SAI DAQUI: a bancada tem saidas no meio (montagem que falhou), e sem placar o lancador espera o prazo inteiro.
			GD.Print($"[inferno] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}
}
