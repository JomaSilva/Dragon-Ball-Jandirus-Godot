using Godot;
using Jandirus.Core.Npc;
using Jandirus.Core.World;

namespace Jandirus.Server;

/// <summary>
/// A BANCADA DOS VILOES AVULSOS (`--vilaoteste`) -- o item 12 do playtest de 2026-10-10: *"de tempos em tempos
/// aparecem no planeta NPCs viloes; derrotar ou matar da karma positivo. Versao minima: intervalo aleatorio,
/// spawn em local valido, aviso para os jogadores do planeta, despawn depois de um tempo, limite de eventos
/// simultaneos, forca e karma configuraveis. Padrao: o karma vai para quem derrotou ou matou. Mantenha
/// separado do sistema de sagas."*
///
/// No PRIMEIRO LOGIN, e hospedando: "planeta com gente" e "quem derrotou" pedem um JOGADOR, e jogador e corpo
/// com `Peer` -- o do host. O segundo planeta com gente e um corpo forjado com o `Peer` do host emprestado
/// (devolvido no fim). Tudo passa pelas funcoes de producao: o relogio e o `TickDosViloes`, o nascimento e o
/// `NascerNpc` do molde de verdade, e a derrota entra pelo funil unico (`AoPerderALuta`).
///
/// Os defeitos injetados: o premio pago de novo a cada queda (`VilaoPagaDeNovoDeTeste`) e o vilao sem zeni
/// (`VilaoNaoPagaZeniDeTeste`); e (2026-10-23, familias 27) a forca pela media do SERVIDOR no lugar da do planeta
/// (`VilaoPelaMediaDoServidorDeTeste`) e o vilao de mundo gerado sem raca (`VilaoAvulso.ForasteiroSemRacaDeTeste`).
///
///     Godot --headless --path . --host --rede 7999 --vilaoteste --raca Human --conta bancada_vilao --nome Heroi
/// </summary>
public partial class GameServer
{
	private bool _vilaoDeTeste;

	private void RodarBancadaDosViloes(ServerPlayer pl)
	{
		GD.Print("\n[viloes] ================ OS VILOES AVULSOS (playtest, 2026-10-10) ================");
		int ok = 0, falhou = 0;
		void Checa(string nome, bool cond, string detalhe = "")
		{
			if (cond) { ok++; GD.Print($"[viloes]   OK    {nome}" + (detalhe.Length > 0 ? $"   [{detalhe}]" : "")); }
			else { falhou++; GD.PrintErr($"[viloes]   FALHA {nome}   [{detalhe}]"); }
		}

		int karmaDeAntes = pl.Karma;
		double zeniDeAntes = pl.Ficha.Zeni, bpDeAntes = pl.Ficha.BP;
		ZoneKey terra = pl.Zone;
		bool ligadoDeAntes = VilaoAvulso.Ligado;

		// OS OUTROS "JOGADORES": corpos forjados com o `Peer` do host emprestado (e o que faz deles jogador pro `EhJogador`) -- o
		// Visitante de Vegeta, e (2026-10-23) o Defunto e o Explorador das familias 27. O `Peer` volta no fim, antes de o corpo sair.
		List<ServerPlayer> forjados = [];
		ServerPlayer Forjar(string nome, ZoneKey z, Vec2 onde, double bp, bool morto = false)
		{
			var f = new ServerPlayer
			{
				Id = _nextId++, Peer = pl.Peer, Name = nome, Race = "Human", Genero = "Male", Idade = 25,
				Zone = z, Pos = onde, LastInputMs = NowMs(), Conta = "bancada_vilao_" + nome.ToLowerInvariant(), Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Name = nome, Race = "Human", BP = bp, Idade = 25, maxstamina = 100, stamina = 100 },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			f.Ficha.Class = "Normal";
			PorNoMundo(f);
			f.Ficha.dead = morto;
			forjados.Add(f);
			return f;
		}

		// O HOST AINDA NAO ESTA NA LISTA DA ZONA: as bancadas de 1o login rodam dentro do `Entrar`, ANTES da
		// linha que poe o corpo no mundo (`ZoneList(...).Add(pl)`). Pra "planeta com gente" e pra o aviso do
		// planeta (`AnunciarNoPlaneta`) ele precisa estar la, como estara meio quadro depois. A primeira rodada
		// desta bancada reprovou o nascimento por isso: nao havia "gente" em volta de quem o vilao nascer.
		List<ServerPlayer> daZona = ZoneList(pl.Zone.Hash);
		bool entrouPelaBancada = !daZona.Contains(pl);
		if (entrouPelaBancada) daZona.Add(pl);

		// Quem esta de pe por causa desta bancada, pra nada ficar pra tras.
		List<ServerPlayer> ViloesDePe() =>
			[.. _viloes.Select(v => _players.GetValueOrDefault(v.Npc)).Where(n => n != null).Select(n => n!)];
		void VencerORelogio(ZoneKey z) => _proximoVilao[z.Hash] = NowMs() - 1;

		// (2026-10-23) TODO EVENTO DE PE ACABA (o prazo vence agora) e o tique os fecha: a medida seguinte parte do zero, longe do teto.
		void EncerrarOsEventos()
		{
			foreach (EventoDeVilao e in _viloes) e.Ate = NowMs() - 1;
			TickDosViloes();
		}

		try
		{
			_viloes.Clear();
			_proximoVilao.Clear();
			VilaoAvulso.Ligado = true;
			SorteioDoVilaoDeTeste = 0.5;

			// (2026-10-23) O HOST A 1 MILHAO na Terra, e o Visitante (secao 4) a 1.000 em Vegeta: a media do servidor (~500 mil) fica longe
			// da de cada planeta, e "o vilao segue o planeta" nao passa por coincidencia (com os dois iguais, as duas medias dariam o mesmo).
			pl.Ficha.BP = 1_000_000;

			// ---------------------------------------------------------------- 1. o molde e as regras
			GD.Print("[viloes] -- 1) o molde e os numeros --");
			MoldeDeNpc? molde = _moldes?.Get(VilaoAvulso.Molde);
			Checa("(montagem) o molde `vilao_avulso` existe no npcs.json, e e do tipo proprio (Vilao)", molde is { Tipo: TipoDeNpc.Vilao });
			if (molde == null) return;

			// (2026-10-23) A FAIXA DO SORTEIO DE BP (`SorteioDeNpc.SortearBp`): de 85% a 115% da media, com o piso do molde por baixo.
			double piso = molde.BpPiso;
			bool NaFaixa(double bp, double media) =>
				bp >= Math.Max(media * 0.85, piso) - 0.5 && bp <= Math.Max(media * 1.15, piso) + 0.5;
			Checa("...sem estagios: nao e chefe, e as sagas nao sabem que ele existe", !molde.EhChefe);
			Checa("o vilao NASCE, e o recorte do dono sobre o inimigo COMUM continua de pe (os dois sao tipos diferentes)",
				  Povoamento.PodeNascer(TipoDeNpc.Vilao) && !Povoamento.PodeNascer(TipoDeNpc.Inimigo));
			Checa("O INTERVALO E ALEATORIO, entre o minimo e o maximo",
				  VilaoAvulso.ProximoIntervaloS(0) == VilaoAvulso.IntervaloMinS && VilaoAvulso.ProximoIntervaloS(1) == VilaoAvulso.IntervaloMaxS
				  && VilaoAvulso.ProximoIntervaloS(0.5) > VilaoAvulso.IntervaloMinS && VilaoAvulso.ProximoIntervaloS(0.5) < VilaoAvulso.IntervaloMaxS);
			Checa("O TETO: cabe enquanto ha vaga no servidor E no planeta",
				  VilaoAvulso.Cabe(0, 0) && !VilaoAvulso.Cabe(VilaoAvulso.MaxSimultaneos, 0) && !VilaoAvulso.Cabe(0, VilaoAvulso.MaxPorPlaneta));

			// ---------------------------------------------------------------- 2. onde
			GD.Print("[viloes] -- 2) que chao recebe vilao --");
			Checa("(montagem) o host esta num planeta com povo", RecebeVilao(terra), terra.Name);
			Checa("o Mirante, o Outro Mundo e o dentro de uma mente NAO recebem",
				  !RecebeVilao(ZoneKey.Premade("Lookout")) && !RecebeVilao(ZoneKey.Premade(Alem.ZonaDoOutroMundo))
				  && !RecebeVilao(ZoneKey.Interior("Interdimension", (ulong)pl.Id)));

			// ---------------------------------------------------------------- 3. o relogio
			GD.Print("[viloes] -- 3) o relogio de cada planeta com gente --");
			long agora = NowMs();
			TickDosViloes();
			Checa("PRIMEIRA VEZ COM GENTE: o relogio COMECA, e ninguem e recebido por um vilao",
				  _viloes.Count == 0 && _proximoVilao.TryGetValue(terra.Hash, out long marcado)
				  && Math.Abs(marcado - agora - (long)(VilaoAvulso.ProximoIntervaloS(0.5) * 1000)) < 2000,
				  _proximoVilao.TryGetValue(terra.Hash, out long m2) ? $"em {(m2 - agora) / 1000.0:0} s" : "sem relogio");

			TickDosViloes();
			Checa("antes da hora, nada nasce", _viloes.Count == 0);

			// ---------------------------------------------------------------- 4. o nascimento
			GD.Print("[viloes] -- 4) o nascimento --");
			VencerORelogio(terra);
			List<string> falas = Ouvir(TickDosViloes);
			ServerPlayer? vilao = ViloesDePe().FirstOrDefault();
			Checa("**NA HORA, UM VILAO APARECE NO PLANETA**", _viloes.Count == 1 && vilao != null);
			if (vilao == null) return;
			ZoneCollision? mapa = MapaDaZonaOuCatalogo(terra);
			float dist = (vilao.Pos - pl.Pos).Length / ZoneCollision.TileSize;
			Checa("...do molde certo, HOSTIL (nao e pacifico: a IA dele caca jogador) e sem dono",
				  vilao.Papel is { Tipo: TipoDeNpc.Vilao, Pacifico: false } && vilao.Peer == null && vilao.Zone.Equals(terra));
			Checa("...EM TERRA FIRME: uma vaga do habitat, fora de parede e de agua",
				  mapa != null && !mapa.BloqueiaEm(vilao.Pos, ModoDeTravessia.APe) && !mapa.EhAguaEm(vilao.Pos), $"({vilao.Pos.X:0}, {vilao.Pos.Y:0})");
			// A FAIXA COM DOIS TILES DE FOLGA: a vaga e medida pelo centro da celula, e o `PontoLivrePerto` ainda
			// pode empurrar o corpo pro tile livre vizinho.
			Checa("...a distancia jogavel de alguem: nem em cima, nem do outro lado do mapa",
				  dist >= VilaoAvulso.DistanciaMinTiles - 2 && dist <= VilaoAvulso.DistanciaMaxTiles + 2, $"{dist:0.#} tiles do host");
			Checa("...com a FORCA do molde: nunca abaixo do piso (`bpPiso`)", vilao.Ficha.BP >= molde.BpPiso, $"BP {vilao.Ficha.BP:N0}, piso {molde.BpPiso:N0}");
			// (a Terra so tem o host: a media do planeta e a do servidor dao o mesmo aqui -- quem separa as duas e Vegeta, mais abaixo)
			Checa("...e com a forca de quem esta na Terra: o host, a 1 milhao, no sorteio de 85-115%", NaFaixa(vilao.Ficha.BP, 1_000_000), $"BP {vilao.Ficha.BP:N0}");
			Checa("**O PLANETA E AVISADO**: o nome dele, pra que lado esta, o karma e o prazo",
				  Disse(falas, "VILÃO APARECEU") && Disse(falas, vilao.Name) && Disse(falas, "passos a") && Disse(falas, "karma"), Ultimos(falas));

			VencerORelogio(terra);
			TickDosViloes();
			Checa("O TETO POR PLANETA: com um de pe, a hora vence e nao nasce o segundo", _viloes.Count == 1);

			// um SEGUNDO planeta com gente: um corpo forjado em Vegeta, com o `Peer` do host emprestado
			ZoneKey vegeta = ZoneKey.Premade("Vegeta");
			Forjar("Visitante", vegeta, PontoDeNascimento(vegeta), 1_000);
			TickDosViloes();        // o relogio de Vegeta comeca
			VencerORelogio(vegeta);
			TickDosViloes();
			Checa("CADA PLANETA TEM O SEU RELOGIO: com gente em Vegeta, nasce um la tambem (dois no servidor)",
				  ViloesDeTeste(vegeta) == (2, 1), $"{ViloesDeTeste(vegeta)}");
			// (2026-10-23) A FORCA E A DO PLANETA: o Visitante esta sozinho em Vegeta, a 1.000, e o host na Terra a 1 milhao
			ServerPlayer? deVegetaNaChegada = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(vegeta));
			Checa("**27a. O VILAO DE VEGETA TEM A FORCA DE QUEM ESTA EM VEGETA** (dono, 2026-10-23: *\"escalar com o poder do planeta atual e "
				  + "nao do server\"*): a media do planeta -- o Visitante, 1.000 --, no sorteio de 85-115%, e nao a do servidor",
				  deVegetaNaChegada != null && NaFaixa(deVegetaNaChegada.Ficha.BP, 1_000),
				  $"BP {deVegetaNaChegada?.Ficha.BP:N0}; a do servidor daria {MediaDoServidor():N0}");

			// ---------------------------------------------------------------- 5. derrotar
			GD.Print("[viloes] -- 5) derrotar ou matar da karma, uma vez, a quem derrotou --");
			pl.Karma = 0;
			// (2026-10-13) O VILAO CARREGA ZENI, e quem o derrota fica com ele -- ver `BolsaDeNpc` e `VilaoFoiDerrotado`.
			double bolsaDoVilao = vilao.Ficha.Zeni, meuZeni = pl.Ficha.Zeni;
			Checa($"(montagem) o vilao nasce com a bolsa dele: entre {BolsaDeNpc.ZeniMinDoVilao:N0} e {BolsaDeNpc.ZeniMaxDoVilao:N0} zeni",
				  bolsaDoVilao >= BolsaDeNpc.ZeniMinDoVilao && bolsaDoVilao <= BolsaDeNpc.ZeniMaxDoVilao, $"{bolsaDoVilao:N0}");
			double repAntes = ReputacaoDe(terra.Name, pl.Conta);
			falas = Ouvir(() => AoPerderALuta(vilao, pl, morreu: false));
			Checa($"**NOCAUTEAR O VILAO DA KARMA** (+{VilaoAvulso.KarmaPorDerrotar}) a quem nocauteou", pl.Karma == VilaoAvulso.KarmaPorDerrotar, $"karma {pl.Karma}");
			double repDepois = ReputacaoDe(terra.Name, pl.Conta);
			Checa($"**...E REPUTACAO COM O PLANETA** (+{VilaoAvulso.ReputacaoPorDerrotar:0}, dono 2026-10-25): a Terra, onde o vilao apareceu, "
				  + "fica devendo a quem o derrotou", Math.Abs(repDepois - repAntes - VilaoAvulso.ReputacaoPorDerrotar) < 1e-6,
				  $"reputacao na Terra {repAntes:0} -> {repDepois:0}");
			Checa("**...E O ZENI QUE ELE CARREGAVA**: a bolsa do vilao vai inteira pra carteira de quem o derrotou, e o jogador le quanto",
				  Math.Abs(pl.Ficha.Zeni - meuZeni - bolsaDoVilao) < 1e-6 && vilao.Ficha.Zeni < 1 && Disse(falas, "zeni"), $"{meuZeni:N0} -> {pl.Ficha.Zeni:N0} (bolsa {bolsaDoVilao:N0}); {Ultimos(falas)}");
			Checa("...e o planeta fica sabendo quem foi", Disse(falas, "derrotou o vilão"), Ultimos(falas));
			AoPerderALuta(vilao, pl, morreu: true);
			Checa("...UMA VEZ: matar o mesmo vilao que ja estava no chao nao paga de novo (nem o karma, nem o zeni, nem a reputacao)",
				  pl.Karma == VilaoAvulso.KarmaPorDerrotar && Math.Abs(pl.Ficha.Zeni - meuZeni - bolsaDoVilao) < 1e-6
				  && Math.Abs(ReputacaoDe(terra.Name, pl.Conta) - repDepois) < 1e-6,
				  $"karma {pl.Karma}, zeni {pl.Ficha.Zeni:N0}, reputacao {ReputacaoDe(terra.Name, pl.Conta):0}");

			long antesDoFim = NowMs();
			TickDosViloes();
			Checa("o derrotado VAI EMBORA no tique seguinte (o corpo sai do mundo) e o evento fecha",
				  !_players.ContainsKey(vilao.Id) && ViloesDeTeste(terra).NoPlaneta == 0);
			Checa("...e o intervalo do planeta conta DO FIM do evento",
				  _proximoVilao[terra.Hash] >= antesDoFim + (long)(VilaoAvulso.IntervaloMinS * 1000) - 50);

			VencerORelogio(terra);
			TickDosViloes();
			ServerPlayer? segundo = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(terra));
			Checa("(montagem) outro vilao nasce na Terra", segundo != null);
			if (segundo == null) return;
			pl.Karma = 0;
			double bolsaDoSegundo = segundo.Ficha.Zeni, zeniAntesDoSegundo = pl.Ficha.Zeni;
			double repAntesDoSegundo = ReputacaoDe(terra.Name, pl.Conta);
			segundo.Combate.Morrer(ignorarSeguro: true);
			falas = Ouvir(() => AoPerderALuta(segundo, pl, morreu: true));
			Checa($"**MATAR O VILAO TAMBEM DA KARMA** (+{VilaoAvulso.KarmaPorDerrotar})", pl.Karma == VilaoAvulso.KarmaPorDerrotar && Disse(falas, "acabou com o vilão"), Ultimos(falas));
			Checa($"...e a reputacao com o planeta (+{VilaoAvulso.ReputacaoPorDerrotar:0}) tambem vem pra quem MATA",
				  Math.Abs(ReputacaoDe(terra.Name, pl.Conta) - repAntesDoSegundo - VilaoAvulso.ReputacaoPorDerrotar) < 1e-6,
				  $"{repAntesDoSegundo:0} -> {ReputacaoDe(terra.Name, pl.Conta):0}");
			Checa("**MATAR O VILAO DA ZENI** (dono, 2026-10-13): quem mata fica com a bolsa dele",
				  bolsaDoSegundo >= 1 && Math.Abs(pl.Ficha.Zeni - zeniAntesDoSegundo - bolsaDoSegundo) < 1e-6, $"{zeniAntesDoSegundo:N0} -> {pl.Ficha.Zeni:N0} (bolsa {bolsaDoSegundo:N0})");
			TickDosViloes();
			Checa("...e o evento fecha (o corpo morto sai pelo caminho de todo NPC)", ViloesDeTeste(terra).NoPlaneta == 0);

			// quem derruba NAO e jogador: ninguem ganha karma
			ServerPlayer? deVegeta = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(vegeta));
			Checa("(montagem) o vilao de Vegeta continua de pe", deVegeta != null);
			if (deVegeta == null) return;
			pl.Karma = 0;
			ServerPlayer? outroNpc = NascerNpc("cidadao", vegeta, deVegeta.Pos, 0x7120_0001UL);
			if (outroNpc != null)
			{
				double zeniAntesDoNpc = pl.Ficha.Zeni;
				falas = Ouvir(() => AoPerderALuta(deVegeta, outroNpc, morreu: false));
				Checa("derrubado por um NPC, o vilao cai e NINGUEM ganha karma (nem zeni)", pl.Karma == 0 && Math.Abs(pl.Ficha.Zeni - zeniAntesDoNpc) < 1e-6 && Disse(falas, "caiu"), Ultimos(falas));
				TickDosViloes();
				RemoverNpc(outroNpc);
			}

			// CONTRA-EXEMPLO DA REPUTACAO (o jogo de antes): o mesmo nocaute, num vilao novo da Terra, nao muda a reputacao.
			VencerORelogio(terra);
			TickDosViloes();
			ServerPlayer? terceiro = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(terra));
			Checa("(montagem) um terceiro vilao nasce na Terra", terceiro != null);
			if (terceiro != null)
			{
				double repAntesDoTerceiro = ReputacaoDe(terra.Name, pl.Conta);
				VilaoNaoDaReputacaoDeTeste = true;
				try { Ouvir(() => AoPerderALuta(terceiro, pl, morreu: false)); }
				finally { VilaoNaoDaReputacaoDeTeste = false; }
				Checa("(defeito injetado: o vilao nao da reputacao, o jogo de antes) o nocaute nao mexe na reputacao com a Terra -- a regua de "
					  + "cima REPROVA", Math.Abs(ReputacaoDe(terra.Name, pl.Conta) - repAntesDoTerceiro) < 1e-6,
					  $"{repAntesDoTerceiro:0} -> {ReputacaoDe(terra.Name, pl.Conta):0}");
				TickDosViloes();
			}

			// ---------------------------------------------------------------- 6. o prazo
			GD.Print("[viloes] -- 6) o prazo --");
			VencerORelogio(terra);
			TickDosViloes();
			ServerPlayer? demorado = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(terra));
			Checa("(montagem) um vilao de pe na Terra", demorado != null);
			if (demorado == null) return;
			pl.Karma = 0;
			foreach (EventoDeVilao e in _viloes) e.Ate = NowMs() - 1;
			falas = Ouvir(TickDosViloes);
			Checa("**NINGUEM O DERROTOU E O PRAZO VENCEU: ELE VAI EMBORA**, o planeta e avisado e ninguem ganha karma",
				  !_players.ContainsKey(demorado.Id) && ViloesDeTeste(terra).NoPlaneta == 0 && pl.Karma == 0 && Disse(falas, "foi embora"), Ultimos(falas));

			// ---------------------------------------------------------------- a chave e o defeito
			VilaoAvulso.Ligado = false;
			VencerORelogio(terra);
			TickDosViloes();
			Checa("com a chave geral DESLIGADA (`VilaoAvulso.Ligado`) a hora vence e nada nasce", ViloesDeTeste(terra).NoPlaneta == 0);
			VilaoAvulso.Ligado = true;

			VencerORelogio(terra);
			TickDosViloes();
			ServerPlayer? repetido = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(terra));
			if (repetido != null)
			{
				// (defeito injetado: o vilao nao paga zeni) -- a v0.2.0, em que o premio era so o karma. NUM VILAO NOVO, e
				// antes do defeito de baixo: aquele derruba o mesmo corpo duas vezes, e a bolsa so paga uma.
				double antesDoDefeito = pl.Ficha.Zeni;
				VilaoNaoPagaZeniDeTeste = true;
				try
				{
					AoPerderALuta(repetido, pl, morreu: false);
					Checa("(defeito injetado: derrotar o vilao nao rende zeni) a carteira de quem o derrotou nao muda, e a bolsa fica com ele -- a regua sabe reprovar",
						  Math.Abs(pl.Ficha.Zeni - antesDoDefeito) < 1e-6 && repetido.Ficha.Zeni >= 1, $"{antesDoDefeito:N0} -> {pl.Ficha.Zeni:N0}");
				}
				finally { VilaoNaoPagaZeniDeTeste = false; }

				pl.Karma = 0;
				VilaoPagaDeNovoDeTeste = true;
				try
				{
					Ouvir(() => { AoPerderALuta(repetido, pl, morreu: false); AoPerderALuta(repetido, pl, morreu: true); });
					Checa("(defeito injetado: o premio e pago a cada queda) nocautear e depois matar o MESMO vilao paga duas vezes -- a regua sabe reprovar",
						  pl.Karma == VilaoAvulso.KarmaPorDerrotar * 2, $"karma {pl.Karma}");
				}
				finally { VilaoPagaDeNovoDeTeste = false; }
			}

			// ---------------------------------------------------------------- 7. so os vivos do planeta (2026-10-23)
			GD.Print("[viloes] -- 7) a forca e a dos VIVOS do planeta, e nao a do servidor (dono, 2026-10-23) --");
			EncerrarOsEventos();
			// UM MORTO DE 50 MILHOES CAIDO EM VEGETA (o corpo de quem acabou de cair, ou o visitante com o passe do Enma): o dono disse
			// "os jogadores VIVOS". Ele entra na media do SERVIDOR (que conta os mortos) e nao pode entrar na do planeta.
			Forjar("Defunto", vegeta, PontoDeNascimento(vegeta) + new Vec2(2 * ZoneCollision.TileSize, 0), 50_000_000, morto: true);
			VencerORelogio(vegeta);
			TickDosViloes();
			ServerPlayer? comOMorto = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(vegeta));
			Checa("**27b. ...SO OS VIVOS CONTAM**: um morto de 50 milhoes caido em Vegeta nao muda a media do planeta -- o vilao continua na faixa do Visitante",
				  comOMorto != null && NaFaixa(comOMorto.Ficha.BP, 1_000), $"BP {comOMorto?.Ficha.BP:N0}; a do servidor daria {MediaDoServidor():N0}");

			// O CONTRA-EXEMPLO: a media do servidor inteiro (o host da Terra e o morto contam), o jogo de antes
			EncerrarOsEventos();
			VilaoPelaMediaDoServidorDeTeste = true;
			try { VencerORelogio(vegeta); TickDosViloes(); }
			finally { VilaoPelaMediaDoServidorDeTeste = false; }
			ServerPlayer? peloServidor = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(vegeta));
			double doServidor = MediaDoServidor();
			Checa("(defeito injetado: a media do servidor inteiro, o jogo de antes) o vilao de Vegeta nasce na faixa do SERVIDOR, longe da do Visitante -- as reguas 27a e 27b REPROVAM",
				  peloServidor != null && NaFaixa(peloServidor.Ficha.BP, doServidor) && !NaFaixa(peloServidor.Ficha.BP, 1_000),
				  $"BP {peloServidor?.Ficha.BP:N0}; media do servidor {doServidor:N0}");
			EncerrarOsEventos();

			// ---------------------------------------------------------------- 8. o mundo gerado (2026-10-23)
			GD.Print("[viloes] -- 8) o mundo gerado: o vilao vem de fora (o sorteio estourava dentro do tique) --");
			// UM MUNDO GERADO DE VERDADE, pelo caminho de producao (a encomenda e a colheita da thread -- os ajudantes da bancada da
			// conquista), com um explorador de 5 mil nele. Antes de 2026-10-23 a pool de racas do vilao saia VAZIA la (ninguem tem berco em
			// mundo gerado) e o `Sortear` estourava dentro do tique, a cada 12-30 min, em todo mundo gerado com gente.
			PlanetaNoEspaco? mundo = AcharMundoGerado();
			ZoneKey? gerado = mundo is { } mg ? ZonaGeradaPronta(mg) : null;
			Checa("(montagem) um mundo GERADO, vivo no servidor, que recebe vilao e tem chao pra ele (vagas no habitat)",
				  gerado is { } g0 && RecebeVilao(g0) && HabitatDaZona(g0) is { Vagas: > 0 },
				  $"{mundo?.Nome ?? "nenhum mundo gerado perto"}; vagas {(gerado is { } g1 ? HabitatDaZona(g1)?.Vagas : null)}");
			if (gerado is not { } zg) return;
			Forjar("Explorador", zg, PontoDeNascimento(zg), 5_000);
			TickDosViloes();        // o relogio do mundo gerado comeca
			VencerORelogio(zg);
			Exception? estouro = null;
			try { TickDosViloes(); }
			catch (Exception e) { estouro = e; }
			ServerPlayer? forasteiro = ViloesDePe().FirstOrDefault(v => v.Zone.Equals(zg));
			Checa("**27d. NUM MUNDO GERADO O VILAO NASCE**, e o tique nao estoura", estouro == null && forasteiro != null,
				  estouro != null ? $"o tique estourou: {estouro.Message}" : forasteiro == null ? "nao nasceu" : $"{forasteiro.Name} em {zg.Name}");
			Checa("27e. ...e ele vem DE FORA: um Alien (a raca sem lar do DM, a da tropa do Freeza) -- ninguem tem berco num mundo gerado",
				  forasteiro?.Race == "Alien", forasteiro?.Race ?? "ninguem");
			Checa("27f. ...com a forca de QUEM ESTA LA -- o Explorador, 5.000 --, e nao a do servidor",
				  forasteiro != null && NaFaixa(forasteiro.Ficha.BP, 5_000), $"BP {forasteiro?.Ficha.BP:N0}; a do servidor daria {MediaDoServidor():N0}");

			// O CONTRA-EXEMPLO: a pool vazia de antes
			EncerrarOsEventos();
			Exception? comODefeito = null;
			VilaoAvulso.ForasteiroSemRacaDeTeste = true;
			try { VencerORelogio(zg); TickDosViloes(); }
			catch (Exception e) { comODefeito = e; }
			finally { VilaoAvulso.ForasteiroSemRacaDeTeste = false; }
			Checa("(defeito injetado: a pool vazia de antes) o sorteio ESTOURA dentro do tique (`nenhuma raca tem berco`) e nenhum vilao nasce no mundo gerado -- a regua 27d REPROVA",
				  comODefeito is InvalidOperationException && comODefeito.Message.Contains("nenhuma raca tem berco") && !ViloesDePe().Exists(v => v.Zone.Equals(zg)),
				  comODefeito?.Message ?? "nao estourou");
		}
		catch (Exception e)
		{
			Checa("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			VilaoPagaDeNovoDeTeste = false;
			VilaoNaoPagaZeniDeTeste = false;
			VilaoPelaMediaDoServidorDeTeste = false;
			VilaoAvulso.ForasteiroSemRacaDeTeste = false;
			pl.Ficha.Zeni = zeniDeAntes;
			pl.Ficha.BP = bpDeAntes;
			VilaoAvulso.Ligado = ligadoDeAntes;
			SorteioDoVilaoDeTeste = null;
			foreach (ServerPlayer v in ViloesDePe()) RemoverNpc(v);
			_viloes.Clear();
			_proximoVilao.Clear();
			foreach (ServerPlayer f in forjados)
			{
				// O `Peer` EMPRESTADO VOLTA ANTES DE O CORPO SAIR: `RemoverNpc` recusa corpo com dono.
				f.Peer = null;
				RemoverNpc(f);
			}
			pl.Karma = karmaDeAntes;
			if (entrouPelaBancada) daZona.Remove(pl);   // o `Entrar` o poe de volta, na linha dele

			// O PLACAR SAI DAQUI, e nao depois do bloco: a bancada tem saidas no meio (montagem que falhou), e
			// sem placar o lancador fica esperando o prazo inteiro.
			GD.Print($"[viloes] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}
}
