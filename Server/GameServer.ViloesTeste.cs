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
/// Um defeito injetado: o premio pago de novo a cada queda (`VilaoPagaDeNovoDeTeste`).
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
		ZoneKey terra = pl.Zone;
		ServerPlayer? visitante = null;
		bool ligadoDeAntes = VilaoAvulso.Ligado;

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

		try
		{
			_viloes.Clear();
			_proximoVilao.Clear();
			VilaoAvulso.Ligado = true;
			SorteioDoVilaoDeTeste = 0.5;

			// ---------------------------------------------------------------- 1. o molde e as regras
			GD.Print("[viloes] -- 1) o molde e os numeros --");
			MoldeDeNpc? molde = _moldes?.Get(VilaoAvulso.Molde);
			Checa("(montagem) o molde `vilao_avulso` existe no npcs.json, e e do tipo proprio (Vilao)", molde is { Tipo: TipoDeNpc.Vilao });
			if (molde == null) return;
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
			Checa("**O PLANETA E AVISADO**: o nome dele, pra que lado esta, o karma e o prazo",
				  Disse(falas, "VILÃO APARECEU") && Disse(falas, vilao.Name) && Disse(falas, "passos a") && Disse(falas, "karma"), Ultimos(falas));

			VencerORelogio(terra);
			TickDosViloes();
			Checa("O TETO POR PLANETA: com um de pe, a hora vence e nao nasce o segundo", _viloes.Count == 1);

			// um SEGUNDO planeta com gente: um corpo forjado em Vegeta, com o `Peer` do host emprestado
			ZoneKey vegeta = ZoneKey.Premade("Vegeta");
			visitante = new ServerPlayer
			{
				Id = _nextId++, Peer = pl.Peer, Name = "Visitante", Race = "Human", Genero = "Male", Idade = 25,
				Zone = vegeta, Pos = PontoDeNascimento(vegeta), LastInputMs = NowMs(), Conta = "bancada_vilao_v", Slot = 0,
				Ficha = new Jandirus.Core.Stats.Fighter { Name = "Visitante", Race = "Human", BP = 1000, Idade = 25, maxstamina = 100, stamina = 100 },
				Livro = new Jandirus.Core.Skills.SkillBook(),
			};
			visitante.Ficha.Class = "Normal";
			PorNoMundo(visitante);
			TickDosViloes();        // o relogio de Vegeta comeca
			VencerORelogio(vegeta);
			TickDosViloes();
			Checa("CADA PLANETA TEM O SEU RELOGIO: com gente em Vegeta, nasce um la tambem (dois no servidor)",
				  ViloesDeTeste(vegeta) == (2, 1), $"{ViloesDeTeste(vegeta)}");

			// ---------------------------------------------------------------- 5. derrotar
			GD.Print("[viloes] -- 5) derrotar ou matar da karma, uma vez, a quem derrotou --");
			pl.Karma = 0;
			falas = Ouvir(() => AoPerderALuta(vilao, pl, morreu: false));
			Checa($"**NOCAUTEAR O VILAO DA KARMA** (+{VilaoAvulso.KarmaPorDerrotar}) a quem nocauteou", pl.Karma == VilaoAvulso.KarmaPorDerrotar, $"karma {pl.Karma}");
			Checa("...e o planeta fica sabendo quem foi", Disse(falas, "derrotou o vilão"), Ultimos(falas));
			AoPerderALuta(vilao, pl, morreu: true);
			Checa("...UMA VEZ: matar o mesmo vilao que ja estava no chao nao paga de novo", pl.Karma == VilaoAvulso.KarmaPorDerrotar, $"karma {pl.Karma}");

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
			segundo.Combate.Morrer(ignorarSeguro: true);
			falas = Ouvir(() => AoPerderALuta(segundo, pl, morreu: true));
			Checa($"**MATAR O VILAO TAMBEM DA KARMA** (+{VilaoAvulso.KarmaPorDerrotar})", pl.Karma == VilaoAvulso.KarmaPorDerrotar && Disse(falas, "acabou com o vilão"), Ultimos(falas));
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
				falas = Ouvir(() => AoPerderALuta(deVegeta, outroNpc, morreu: false));
				Checa("derrubado por um NPC, o vilao cai e NINGUEM ganha karma", pl.Karma == 0 && Disse(falas, "caiu"), Ultimos(falas));
				TickDosViloes();
				RemoverNpc(outroNpc);
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
		}
		catch (Exception e)
		{
			Checa("a bancada rodou ate o fim", false, e.ToString());
		}
		finally
		{
			VilaoPagaDeNovoDeTeste = false;
			VilaoAvulso.Ligado = ligadoDeAntes;
			SorteioDoVilaoDeTeste = null;
			foreach (ServerPlayer v in ViloesDePe()) RemoverNpc(v);
			_viloes.Clear();
			_proximoVilao.Clear();
			if (visitante != null)
			{
				// O `Peer` EMPRESTADO VOLTA ANTES DE O CORPO SAIR: `RemoverNpc` recusa corpo com dono.
				visitante.Peer = null;
				RemoverNpc(visitante);
			}
			pl.Karma = karmaDeAntes;
			if (entrouPelaBancada) daZona.Remove(pl);   // o `Entrar` o poe de volta, na linha dele

			// O PLACAR SAI DAQUI, e nao depois do bloco: a bancada tem saidas no meio (montagem que falhou), e
			// sem placar o lancador fica esperando o prazo inteiro.
			GD.Print($"[viloes] ================ {ok} OK, {falhou} FALHA(S) ================");
		}
	}
}
